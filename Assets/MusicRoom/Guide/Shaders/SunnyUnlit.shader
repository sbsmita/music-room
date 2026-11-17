// Sunny's unlit surfaces (basic colour, not tone mapped): the face sphere, the speech bubble and the
// additive cyan ghost hand. Render state comes from the template's floats (GuideMaterialTemplates), so one shader
// covers alpha-blended (body, bubble with ZTest Always) and additive (demo) parts with no keywords.
// Additive parts: the design adds the sRGB-encoded colour * opacity straight into the (encoded) framebuffer. A linear
// framebuffer cannot reproduce that sum for every background; adding the same encoded amount as a linear value is the
// closest fixed-function match over the room's mid-bright walls and instruments where Sunny floats (converting it with
// SRGBToLinear would only match over black and leaves the ghost hand a faint tint on the walls). Blend One One; alpha
// accumulates opacity^2 as a SrcAlpha/One blend does (it matters for the passthrough alpha in mixed reality).
Shader "MusicRoom/SunnyUnlit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Additive ("Additive (sRGB-space add)", Float) = 0
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
        _SrcBlendA ("Src Blend Alpha", Float) = 1
        _DstBlendA ("Dst Blend Alpha", Float) = 10
        _ZWrite ("ZWrite", Float) = 0
        _ZTest ("ZTest", Float) = 4
        _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SunnyUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Additive;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;   // white on every Sunny mesh; the merged demo mesh puts each part's opacity in alpha
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
                half a = saturate(c.a);
                half3 added = saturate(LinearToSRGB(c.rgb)) * a;   // the encoded colour * opacity
                return _Additive > 0.5 ? half4(added, a * a) : c;
            }
            ENDHLSL
        }
    }
}
