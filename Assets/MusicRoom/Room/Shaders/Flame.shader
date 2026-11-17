// The cluster flame (spec 07 section 4.2), verbatim maths: noise tongues on crossed quads, seed and intensity
// per vertex (TEXCOORD1 = (seed, intensity)) so every fireplace flame is one mesh / one draw and the candle flames another.
// Time is the global _FireTime (Decor time). The spec writes (col*I*a, a) with additive blending into a non-sRGB
// framebuffer, i.e. it adds clamp(col*I*a)*a as an sRGB value; this pass keeps the spec's blend state (SrcAlpha One,
// One One: alpha accumulates for passthrough) and outputs the colour that adds exactly that value over black.
Shader "MusicRoom/Flame"
{
    Properties
    {
        _Intensity ("Intensity scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "Flame"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One, One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "RoomNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END
            float _FireTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 data : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 data : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.data = v.data;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float uSeed = i.data.x;
                float t = _FireTime * 0.9 + uSeed;
                float x = (uv.x - 0.5) * 2.0;
                float n = FireFbm(float2(uv.x * 3.0 + uSeed, uv.y * 2.2 - t * 1.7));
                float n2 = FireFbm(float2(uv.x * 7.0 - uSeed, uv.y * 4.0 - t * 2.6));
                float n3 = FireFbm(float2(uv.x * 14.0 + uSeed * 2.0, uv.y * 9.0 - t * 4.0));
                float sway = (n - 0.5) * 0.9 * uv.y;
                // Several tongues: the envelope width wobbles across x so the flame splits into licks.
                float tongues = 0.75 + 0.35 * sin(uv.x * 9.0 + n2 * 6.0 - t * 2.0);
                float width = lerp(0.95, 0.05, pow(max(uv.y, 0.0), 0.7)) * tongues;
                float edge = abs(x + sway) / width;
                float body = 1.0 - smoothstep(0.45, 1.0, edge);
                float heat = body * (1.0 - uv.y * 0.9) + body * n2 * 0.5 + body * (n3 - 0.5) * 0.35;
                heat = saturate(heat);
                float alpha = smoothstep(0.14, 0.5, heat) * (1.0 - smoothstep(0.7, 1.0, uv.y)) * smoothstep(0.0, 0.07, uv.y);
                float3 col = lerp(float3(0.45, 0.02, 0.0), float3(0.95, 0.30, 0.02), smoothstep(0.1, 0.5, heat));
                col = lerp(col, float3(1.0, 0.72, 0.18), smoothstep(0.5, 0.8, heat));
                col = lerp(col, float3(1.0, 0.92, 0.6), smoothstep(0.85, 1.0, heat));
                float intensity = i.data.y * _Intensity;
                float3 written = min(col * intensity * alpha, 1.0);              // the spec's clamped colour output
                float3 rgb = SRGBToLinear(written * alpha) / max(alpha, 1e-4);   // x SrcAlpha = the spec's sRGB add, in linear
                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
