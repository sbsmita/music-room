// The butterflies (Decor/Butterflies.cs): ONE dynamic mesh of wing quads written on the CPU each frame (positions in the
// renderer's space, the wing's normal, the vertex colour = (glow rgb, fade alpha), uv into the wing atlas
// Room/butterfly_wings). Thin wings: both faces drawn (Cull Off) and lit alike, per vertex as the harp's strings
// (ambient SH from both sides + the main light + the room's point lights, |n.l| wrapped so the far side of a wing is
// not black), a faint self light (_Ambient) so their colours read in the evening room, plus the glow (the playback glow's
// orange x the wing's own colour) brighter with louder notes. Alpha-blended (soft edges, the fade in and out); the alpha
// channel accumulates as over (One OneMinusSrcAlpha) so mixed reality's passthrough sees the right coverage.
// Single-pass instanced / multiview: instance id in, stereo output initialised, eye index set up in the fragment.
Shader "MusicRoom/Butterfly"
{
    Properties
    {
        _BaseMap ("Wings", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Ambient ("Self light", Float) = 0.14
        _GlowGain ("Glow gain", Float) = 1.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardButterfly"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            // Everything per material in UnityPerMaterial: required for the SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Ambient;
                half _GlowGain;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 light : TEXCOORD1;
                half4 glow : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // A thin wing is lit from either side: |n.l|, wrapped so a wing edge-on to a light still shows its colour.
            half Both(half3 n, half3 l)
            {
                return abs(dot(n, l)) * 0.7h + 0.3h;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                half3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                half3 light = (SampleSH(n) + SampleSH(-n)) * 0.5h;
                Light mainLight = GetMainLight();
                light += mainLight.color * (mainLight.distanceAttenuation * Both(n, mainLight.direction));
                uint count = (uint)GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light l = GetAdditionalLight(i, positionWS);
                    light += l.color * (l.distanceAttenuation * Both(n, l.direction));
                }
                o.light = light;
                o.glow = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;
                half3 rgb = albedo * (i.light + _Ambient) + albedo * i.glow.rgb * _GlowGain;
                return half4(rgb, tex.a * i.glow.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
