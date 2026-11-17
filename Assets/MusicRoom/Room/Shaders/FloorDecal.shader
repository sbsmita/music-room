// Contact shadows and wall-base AO strips on the floor (spec 07 section 3.4): black, alpha = shape x
// opacity, all static decals in one mesh. The shape is computed instead of sampled: kind 0 (TEXCOORD1.x) is
// softDotTexture's radial gradient (alpha 1 at the centre, .55 at 35 %, 0 at the edge), kind 1 is
// gradientStripTexture's ramp (.42 at the wall edge v = 1, .12 at 45 % in, 0 at v = 0). Opacity = vertex colour alpha.
// Offset -1,-1 keeps the back strip above the rug at the same height. Drawn after the opaques, before the glass.
// The spec darkens an sRGB framebuffer by (1 - a); a linear framebuffer needs (1 - a)^2.2 for the same look, so the
// alpha written is 1 - (1 - a)^2.2 (exact for black under the 2.2 power law, whatever lies below).
Shader "MusicRoom/FloorDecal"
{
    Properties
    {
        _Opacity ("Opacity scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-50" "IgnoreProjector"="True" }
        Pass
        {
            Name "FloorDecal"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 kind : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 ka : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.ka = float2(v.kind.x, v.color.a);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float shape;
                if (i.ka.x < 0.5)
                {
                    float r = length(i.uv - 0.5) * 2.0;
                    shape = r < 0.35 ? lerp(1.0, 0.55, r / 0.35) : (r < 1.0 ? lerp(0.55, 0.0, (r - 0.35) / 0.65) : 0.0);
                }
                else
                {
                    float t = 1.0 - i.uv.y;   // canvas row from the top (the wall edge)
                    shape = t < 0.45 ? lerp(0.42, 0.12, t / 0.45) : lerp(0.12, 0.0, saturate((t - 0.45) / 0.55));
                }
                float a = saturate(shape * i.ka.y * _Opacity);
                return float4(0.0, 0.0, 0.0, 1.0 - pow(1.0 - a, 2.2));
            }
            ENDHLSL
        }
    }
}
