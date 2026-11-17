// Sunny's corona (Docs/08-ux-lessons-menu-guide.md 3.1): a plane behind the body with slowly turning rays. _SunnyTime is
// Sunny's own clock, _Glow fades it while Sunny leaves. The design outputs (col * al, al) with SrcAlpha/One blending and
// no colour conversion, i.e. it adds clamp(col * al) * clamp(al) to the encoded framebuffer. That same amount is added
// here as a linear value (Blend One One): the closest fixed-function match over the room's walls behind Sunny, where the
// corona reads as a bright rim (see SunnyUnlit.shader); alpha accumulates al^2 like a SrcAlpha/One blend.
Shader "MusicRoom/SunnyCorona"
{
    Properties
    {
        _Glow ("Glow", Float) = 1
        _SunnyTime ("Sunny Time", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SunnyCorona"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One, One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Glow;
                float _SunnyTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // GLSL smoothstep with edge0 > edge1, written out (HLSL leaves the reversed form to the compiler).
            float Smooth(float e0, float e1, float x)
            {
                float t = saturate((x - e0) / (e1 - e0));
                return t * t * (3.0 - 2.0 * t);
            }

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = (i.uv - 0.5) * 2.0;
                float r = length(p);
                float a = atan2(p.y, p.x + 1e-6);
                float t = _SunnyTime;
                float rays = 0.55 + 0.45 * sin(a * 9.0 + t * 0.7) * sin(a * 4.0 - t * 0.45);
                float halo = Smooth(1.0, 0.3, r) * (0.35 + 0.65 * rays);
                float core = Smooth(0.62, 0.3, r);
                float al = (halo * 0.55 + core * 0.6) * _Glow;
                float3 col = lerp(float3(1.0, 0.55, 0.12), float3(1.0, 0.86, 0.45), core);
                float a1 = saturate(al);
                float3 added = saturate(col * al) * a1;
                return float4(added, a1 * a1);
            }
            ENDHLSL
        }
    }
}
