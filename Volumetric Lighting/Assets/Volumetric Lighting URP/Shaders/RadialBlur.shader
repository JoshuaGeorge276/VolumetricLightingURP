Shader "Hidden/RadialBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurWidth("Blur Width", Range(0,1)) = 0.85
        _Intensity("Intensity", Range(0,1)) = 1
    }
    SubShader
    {
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float4 vertex : SV_POSITION;
                float4 mainLightScreenPos : TEXCOORD2;
            };

            float4 _Center;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.mainLightScreenPos = _Center;
                o.uv = v.uv;
                return o;
            }

#define NUM_SAMPLES 100

            sampler2D _MainTex;

            float _BlurWidth;
            float _Intensity;

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = fixed4(0, 0, 0, 0);

                float2 ray = i.uv - i.mainLightScreenPos.xy;

                for (int s = 0; s < NUM_SAMPLES; s++)
                {
                    float scale = 1.0f - _BlurWidth * (float(s) / float(NUM_SAMPLES - 1));
                    col.xyz += tex2D(_MainTex, (ray * scale) + i.mainLightScreenPos.xy).xyz / float(NUM_SAMPLES);
                }
                col.a = col.x;

                return col * _Intensity;
            }
            ENDCG
        }
    }
}
