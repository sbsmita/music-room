// The window frost (spec 07 section 5.5), verbatim: frost creeping in from each pane's edges and corners (soft
// minimum of the edge distances), ice needles at scales 22 and 47, a fine bloom by the edge, alpha <= .5, and the mixed
// reality veil. Quad over the glass, 4 mm toward the room; the spec blends its sRGB output in a non-sRGB framebuffer, so
// the colour is SRGBToLinear(rgb x alpha) / alpha (the same sRGB value over black under URP's linear blend).
Shader "MusicRoom/Frost"
{
    Properties
    {
        _Half ("Glass half-size", Vector) = (0.9, 0.85, 0, 0)
        _Mull ("Mullion half-width (0 = one pane)", Float) = 0.02
        _Transom ("Transom height, present", Vector) = (0.204, 1, 0, 0)
        _Frost ("How far the frost has crept in 0..1", Float) = 0
        _Veil ("Snowy-sky veil (mixed reality)", Float) = 0
        _Reach ("Reach at full cover (m)", Float) = 0.15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+4" "IgnoreProjector"="True" }
        Pass
        {
            Name "Frost"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "RoomNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Half, _Transom;
                float _Mull, _Frost, _Veil, _Reach;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 pos : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = (v.uv - 0.5) * 2.0 * _Half.xy;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            // Ice needles: in each small cell a random direction, and noise stretched hard along it, so the frost reads
            // as clusters of fine crystals rather than a smooth haze.
            float Needles(float2 p, float scale, float seed)
            {
                float2 cell = floor(p * scale + seed);
                float ang = FrostHash(cell + seed) * 6.2831853;
                float2 d = float2(cos(ang), sin(ang));
                float2 lp = float2(dot(p, d), dot(p, float2(-d.y, d.x))) * scale;
                float n = FrostNoise(float2(lp.x * 1.5, lp.y * 16.0) + cell * 7.31);
                return smoothstep(0.5, 0.92, n);
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = i.pos;
                // Distance to the nearest edge of this pane (outer frame, mullion, transom).
                float dx = _Half.x - abs(p.x);
                if (_Mull > 0.0) dx = min(dx, abs(p.x) - _Mull);
                float dy = _Half.y - abs(p.y);
                if (_Transom.y > 0.5) dy = min(dy, abs(p.y - _Transom.x) - max(_Mull, 0.0));
                dx = max(dx, 0.0); dy = max(dy, 0.0);
                // Soft minimum: the frost reaches further into the corners than along the edges.
                float m = -0.05 * log(exp(-dx / 0.05) + exp(-dy / 0.05)) + 0.035;
                float n = FrostNoise(p * 6.0) * 0.6 + FrostNoise(p * 17.0) * 0.4;
                float reach = _Frost * _Reach;
                float front = m + (n - 0.5) * 0.09;
                float dens = 1.0 - smoothstep(reach * 0.3, reach + 0.004, front);
                clip((dens < 0.002 && _Veil < 0.002) ? -1.0 : 1.0);
                // Needles at two scales; the oldest frost by the edge is a fine white bloom between them.
                float nd = max(Needles(p, 22.0, 0.0), Needles(p, 47.0, 3.7) * 0.8);
                float fine = FrostNoise(p * 160.0);
                float thick = 1.0 - smoothstep(0.0, reach * 0.5 + 0.008, front);
                float a = dens * (0.05 + 0.32 * nd + 0.06 * fine) + thick * (0.1 + 0.08 * fine);
                a = min(a, 0.5);
                float3 col = float3(0.85, 0.9, 0.97) * (0.75 + 0.3 * nd);
                float alpha = a + _Veil * (1.0 - a);
                float3 veilCol = float3(0.78, 0.81, 0.86);
                float3 rgb = saturate(lerp(veilCol, col, a / max(alpha, 1e-4)));
                // The spec blends rgb x alpha over the dark sky in a non-sRGB framebuffer; URP blends linear light. The
                // colour that adds that sRGB value over black (as Flame and Billboard do) matches it within ~10 %
                // over the night sky behind the glass (review: the old 50/50 mix with plain SRGBToLinear(rgb) read
                // 1.2-1.6x too bright).
                float3 overBlack = SRGBToLinear(rgb * alpha) / max(alpha, 1e-4);
                return float4(overBlack, alpha);
            }
            ENDHLSL
        }
    }
}
