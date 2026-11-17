// Opaque unlit vertex colour (linear values as stored in the mesh): the three switch LEDs as ONE mesh and one draw
// (ARCHITECTURE.md section 14: per-state colour as vertex colours in a custom unlit shader, never a MaterialPropertyBlock
// on URP Lit). AmbiencePanel rewrites the colours of an LED's vertices when its switch flips, never per frame. The lit
// value of the switch LED (#331a0a, emissive #ff8a2a x 1.4 when on) is folded into the two colours.
Shader "MusicRoom/VertexColorUnlit"
{
    Properties
    {
        _Gain ("Colour gain", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Gain;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = saturate(v.color.rgb * _Gain);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.color, 1.0);
            }
            ENDHLSL
        }
    }
}
