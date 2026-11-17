// The harp's 22 strings, drawn as ONE mesh (ARCHITECTURE.md sections 14 and 15.3: harp <= 6 draws, no
// MaterialPropertyBlock on URP Lit/Unlit). Per-string data rides in the vertices, so one SRP-Batcher-friendly material
// serves every string:
//   COLOR     the string's base colour (cream, C red, F blue), linear, written once at build;
//   TEXCOORD1 xyz the string's glow (pluck flash / lesson target), linear RGB, rewritten only when a glow changes;
//             w   the vertex alpha of the blur (E8, experience.md section 10): the mesh holds every string three times,
//             the cores first (index order = draw order), then the two edges of the blur at the envelope of the motion.
//             A still string is its core at alpha 1; a ringing one fades its core and shows its edges. Alpha-blended in
//             the transparent queue (one draw, sorted with the other transparent objects);
//   COLOR.a   1 on the cores, 0 on the edge copies: the depth pass (StringsDepth, before the colour pass) writes the
//             cores' depth only, so a string still hides a finger behind it in VR and keeps its place in front of the
//             real hand's cut-out in mixed reality (E6: virtual content in front keeps its depth), while the blur's edges
//             stay depth-free (experience.md 10: "ZWrite Off for the edge copies, core first").
// URP Lit and Unlit ignore vertex colours, hence this small custom shader. The strings are designed as a lit
// surface (roughness .5) plus emissive; a string is ~3 mm wide on screen, so a per-vertex diffuse (ambient SH + main light + the
// room's point lights, half-Lambert so the unlit side is not black) reads the same and costs almost nothing per pixel.
// Single-pass instanced stereo: instance id in, stereo output initialised.
Shader "MusicRoom/HarpStrings"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _GlowGain ("Glow gain", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StringsDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _GlowGain;
            CBUFFER_END

            struct AttributesDepth
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
                half core : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsDepth vertDepth(AttributesDepth v)
            {
                VaryingsDepth o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.core = v.color.a;
                return o;
            }

            half4 fragDepth(VaryingsDepth i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(i.core - 0.5h);   // the blur's edge copies write no depth
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardStrings"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Everything per material in UnityPerMaterial: required for the SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _GlowGain;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 glow : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Half-Lambert squared: soft wrap-around so a 1.5 mm cylinder keeps a readable colour on its dark side.
            half Wrap(half3 n, half3 l)
            {
                half w = saturate(dot(n, l)) * 0.5h + 0.5h;
                return w * w;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                half3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                half3 light = SampleSH(n);
                Light mainLight = GetMainLight();
                light += mainLight.color * (mainLight.distanceAttenuation * Wrap(n, mainLight.direction));
                uint count = (uint)GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light l = GetAdditionalLight(i, positionWS);
                    light += l.color * (l.distanceAttenuation * Wrap(n, l.direction));
                }
                half3 albedo = v.color.rgb * _BaseColor.rgb;
                o.color = half4(albedo * light + (half3)v.glow.xyz * _GlowGain, (half)saturate(v.glow.w));
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return i.color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
