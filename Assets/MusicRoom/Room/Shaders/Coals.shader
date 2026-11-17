// The coals (spec 07 section 4.3): a bed of pulsing embers on a quad facing up, oval mask, normal blending.
// The spec's shader skips colour conversion: its output is displayed as sRGB, so it goes out through
// SRGBToLinear. Time: global _FireTime.
Shader "MusicRoom/Coals"
{
    Properties
    {
        _Seed ("Seed", Float) = 1.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Coals"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "RoomNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Seed;
            CBUFFER_END
            float _FireTime;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float r = length((uv - 0.5) * 2.0);
                float mask = 1.0 - smoothstep(0.75, 1.0, r);
                float n = FireFbm(uv * 7.0 + _Seed);
                float cracks = smoothstep(0.42, 0.6, n);
                float pulse = 0.65 + 0.35 * sin(_FireTime * 1.6 + n * 14.0);
                float3 col = lerp(float3(0.03, 0.015, 0.01), float3(0.95, 0.3, 0.04), cracks * pulse);
                col += float3(1.0, 0.65, 0.25) * smoothstep(0.6, 0.78, n) * pulse * 0.7;
                return float4(SRGBToLinear(saturate(col)), mask);
            }
            ENDHLSL
        }
    }
}
