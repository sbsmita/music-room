// A sprite: a camera-facing quad of world size _Size at the object's
// origin, soft dot x colour, additive. The fireplace cavity glow's opacity .45 + .15 sin(5t + 1.3) + .08 sin(13.1t)
// is computed here from the global _FireTime (_Pulse = base, amp 1, rate 1, phase; _Pulse2 = amp 2, rate 2). The
// sprite is specified colour-managed (linear colour x map, encoded to sRGB), then added x alpha in a non-sRGB framebuffer;
// URP adds linear light, so the colour written is the one whose x alpha adds that sRGB value over black (a
// plain linear add made the firebox glow about twice as bright as the reference renders).
Shader "MusicRoom/Billboard"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 0.353, 0.078, 1)
        _Size ("World size", Vector) = (1.1, 0.9, 0, 0)
        _Pulse ("Opacity base, amp, rate, phase", Vector) = (0.45, 0.15, 5, 1.3)
        _Pulse2 ("Second amp, rate", Vector) = (0.08, 13.1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Billboard"
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color, _Size, _Pulse, _Pulse2;
            CBUFFER_END
            float _FireTime;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(float3(0.0, 0.0, 0.0)));
                float3 positionVS = centerVS + float3(v.positionOS.xy * _Size.xy, 0.0);
                o.positionCS = TransformWViewToHClip(positionVS);
                o.uv = v.uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float t = _FireTime;
                float opacity = _Pulse.x + _Pulse.y * sin(t * _Pulse.z + _Pulse.w) + _Pulse2.x * sin(t * _Pulse2.y);
                float a = tex.a * saturate(opacity);
                float3 srgbOverlay = LinearToSRGB(saturate(_Color.rgb * tex.rgb));
                return float4(SRGBToLinear(srgbOverlay * a) / max(a, 1e-4), a);
            }
            ENDHLSL
        }
    }
}
