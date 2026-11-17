// The night sky outside the window (spec 07 sections 5.2 and 5.3): unlit nightSky texture x the snowing tint, then
// the hill-snow overlay rgb = max(rgb, lerp(rgb, hs.rgb, hs.a * smoothstep(0, .8, cover))) once the overlay exists.
// The spec draws it with a colour-managed unlit material, so no extra conversion here. Cover: global _SnowCover.
// The playback show (WindowShow.hlsl): northern lights added over the sky while a recording plays back, in the sky's own
// metres (the 40 x 11 m plane, its lower edge 1 m below the floor), behind the hills (above their ridges); 0 at rest.
Shader "MusicRoom/NightSky"
{
    Properties
    {
        _BaseMap ("Sky", 2D) = "black" {}
        _HillSnowMap ("Hill snow (rgb colour, a amount)", 2D) = "black" {}
        _Tint ("Tint (linear)", Vector) = (1, 1, 1, 1)
        _HillSnowOn ("Hill snow ready", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "NightSky"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WindowShow.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HillSnowMap); SAMPLER(sampler_HillSnowMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float _HillSnowOn;
            CBUFFER_END
            float _SnowCover;

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
                float3 rgb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Tint.rgb;
                if (_HillSnowOn > 0.5 && _SnowCover > 0.0)
                {
                    float4 hs = SAMPLE_TEXTURE2D(_HillSnowMap, sampler_HillSnowMap, i.uv);
                    float ha = hs.a * smoothstep(0.0, 0.8, _SnowCover);
                    rgb = max(rgb, lerp(rgb, hs.rgb, ha));
                }
                if (_WindowAurora.x > 0.0)
                {
                    // the sky plane in metres; the hills' ridges reach at most v .27 (RoomTexGen.NightSky), so the curtains
                    // fade out above them and never hang in front of a hill
                    float2 p = float2((i.uv.x - 0.5) * 40.0, i.uv.y * 11.0 - 1.0);
                    // and out again before the plane's top edge (seen close to the glass)
                    rgb += WindowAurora(p) * (smoothstep(0.28, 0.36, i.uv.y) * smoothstep(0.99, 0.86, i.uv.y));
                }
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
