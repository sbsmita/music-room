// Lesson blocks (unlit, transparent, no depth write, no tone mapping, render order 4 =
// after the room's transparents). One instanced draw per frame (Graphics.RenderMeshInstanced) carries every block of the
// lesson's instrument; the per-instance colour array _Color (MaterialPropertyBlock.SetVectorArray, LINEAR rgb, alpha =
// the opacity 0.6 or 0.38) gives each instance its colour. Unlit on purpose: the blocks are not lit.
// Blending is normal alpha blending with separate alpha factors (spec 07 section 4.1) so the eye buffer's alpha
// stays right over passthrough. The instancing and stereo macros make one draw cover both eyes (single-pass instanced).
Shader "MusicRoom/LessonBlock"
{
    Properties
    {
        _Color ("Colour (linear rgb, alpha = opacity)", Color) = (0.0976, 0.7157, 1.0, 0.6)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+5" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "LessonBlock"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            // 3.5: the instancing macros only switch on from shader target 3.5 (UnityInstancing.hlsl).
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_INSTANCING_BUFFER_START(LessonBlockProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(LessonBlockProps)

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // The colour is read in the vertex stage so the fragment needs no instance id.
                output.color = (half4)UNITY_ACCESS_INSTANCED_PROP(LessonBlockProps, _Color);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
