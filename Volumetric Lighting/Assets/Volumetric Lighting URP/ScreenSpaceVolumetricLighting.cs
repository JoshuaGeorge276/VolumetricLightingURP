using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

public class ScreenSpaceVolumetricLighting : ScriptableRendererFeature
{

    class RenderObjectsToRTPass : ScriptableRenderPass
    {
        private readonly int renderTargetId;
        private List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
        private RenderTargetIdentifier renderTargetIdentifier;
        private ProfilingSampler profilerSampler;
        private FilteringSettings filteringSettings;
        private Material overrideMaterial;

        public RenderObjectsToRTPass(int renderTargetId, string profilerTag, int layerMask, Material overrideMaterial)
        {
            this.renderTargetId = renderTargetId;

            profilingSampler = new ProfilingSampler(profilerTag);

            shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            shaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));

            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);

            this.overrideMaterial = overrideMaterial;
        }


        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor renderTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            renderTextureDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            cmd.GetTemporaryRT(renderTargetId, renderTextureDescriptor);
            renderTargetIdentifier = new RenderTargetIdentifier(renderTargetId);
             
            ConfigureTarget(renderTargetIdentifier, renderingData.cameraData.renderer.cameraDepthTarget);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, sortingCriteria);
            drawingSettings.overrideMaterial = overrideMaterial;

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilerSampler))
            {
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    public class RadialBlurPass : ScriptableRenderPass
    {
        private readonly int blurSourceRTId;
        private readonly int blurDestinationRTId;
        private Material radialBlurMaterial;
        private Material blitMaterial;
        private ProfilingSampler profilerSampler;
        private RenderTargetIdentifier blurSourceRTIdentifier;
        private RenderTargetIdentifier blurDestinationIdentifier;

        private Vector3 mainLightScreenSpacePos;
        private int mainLightSSPosProperty = Shader.PropertyToID("_Center");

        public RadialBlurPass(int blurSourceRTId, int blurDestinationRTId, Material radialBlurMaterial)
        {
            this.blurSourceRTId = blurSourceRTId;
            this.blurDestinationRTId = blurDestinationRTId;
            this.radialBlurMaterial = radialBlurMaterial;

            blitMaterial = new Material(Shader.Find("Hidden/ColorBlit"));
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            blurSourceRTIdentifier = new RenderTargetIdentifier(blurSourceRTId);

            RenderTextureDescriptor renderTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            renderTextureDescriptor.colorFormat = RenderTextureFormat.ARGBFloat;
            cmd.GetTemporaryRT(blurDestinationRTId, renderTextureDescriptor);
            blurDestinationIdentifier = new RenderTargetIdentifier(blurDestinationRTId);

            ConfigureTarget(blurDestinationIdentifier);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Do not perform the radial blur if there is no main light.
            if(renderingData.lightData.visibleLights.Length == 0)
            {
                return;
            }

            VisibleLight mainLight = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex];
            Matrix4x4 vpMatrix = renderingData.cameraData.camera.projectionMatrix * renderingData.cameraData.camera.worldToCameraMatrix;
            mainLightScreenSpacePos = vpMatrix.MultiplyPoint(mainLight.light.transform.position);

            // convert screen space to valid UV-coordinates
            mainLightScreenSpacePos.x = (mainLightScreenSpacePos.x + 1) * 0.5f;
            mainLightScreenSpacePos.y = (mainLightScreenSpacePos.y + 1) * 0.5f;

            radialBlurMaterial.SetVector(mainLightSSPosProperty, new Vector4(mainLightScreenSpacePos.x, mainLightScreenSpacePos.y, mainLightScreenSpacePos.z, 1.0f));

            CommandBuffer cmd = CommandBufferPool.Get();
            using ( new ProfilingScope(cmd, profilerSampler))
            {
                cmd.Blit(blurSourceRTIdentifier, blurDestinationIdentifier, radialBlurMaterial);
                cmd.Blit(blurDestinationIdentifier, renderingData.cameraData.renderer.cameraColorTarget, blitMaterial);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }
    }

    [SerializeField]
    private string renderTargetName = "_OccludersMapRT";

    [SerializeField]
    private string blurredRenderTargetName = "_BlurredOccludersMapRT";

    [SerializeField]
    private Material lightSourceOverrideMaterial;

    [SerializeField]
    private Material copyDepthMaterial;

    [SerializeField]
    private Material radialBlurMaterial;

    RenderObjectsToRTPass renderLightSourcesPass;
    RadialBlurPass radialBlurPass;

    /// <inheritdoc/>
    public override void Create()
    {
        int renderTargetId = Shader.PropertyToID(renderTargetName);
        int blurRenderTargetId = Shader.PropertyToID(blurredRenderTargetName);

        renderLightSourcesPass = new RenderObjectsToRTPass(renderTargetId, "RenderLightSource", LayerMask.GetMask("Light Source"), lightSourceOverrideMaterial);
        // Configures where the render pass should be injected.
        renderLightSourcesPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques+1;

        radialBlurPass = new RadialBlurPass(renderTargetId, blurRenderTargetId, radialBlurMaterial);
        radialBlurPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(renderLightSourcesPass);
        renderer.EnqueuePass(radialBlurPass);
    }
}


