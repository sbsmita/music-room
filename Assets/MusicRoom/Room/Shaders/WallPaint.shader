// The wall paint (Decor/WallPaint.cs): ONE dynamic mesh of brush quads lying 3 mm off the walls (Decor/PaintBrushes.cs:
// each instrument's own nature motifs: flowers on a vine, trees, grass, leaves, birds, clouds, butterflies, mushrooms). Per vertex: the wall's normal, the colour (the pigment, linear; alpha = how strong),
// TEXCOORD0 = (u, v in the cell, atlas cell, birth time), TEXCOORD1 = (painting 0/1, mode, p0, p1),
// TEXCOORD2 = (p2, p3, wipe 0 at a piece's start .. 1 at its end, a random 0..1 per quad), TEXCOORD3 = the quad's centre.
// How a quad comes alive (mode):
//   0 Grow: from p1 (start scale) to full size over p0 s, its alpha toward p2 as it grows (a flower opening, a cloud);
//   1 Fall: the vertex runs by (p0, p1, p2) (object space; the same on every corner: a leaf drifting down the wall) over
//     p3 s, easing to a stop;
//   2 Wipe: the vine, a stalk: drawn from its start edge to its end edge over p0 s; the cell tiles along u (Ribbon);
//   3 Band: a Wipe whose hue turns across the band by p1 turns (a bird's faint path; the Band cell tiles along u).
// Each painting fades with its own uniform (_Fade0 / _Fade1). The gather (the song becoming a framed painting): a second
// renderer of the same mesh with _GatherOn 1 draws copies of painting _GatherGen swirling off the walls into the frame
// (_GatherCentre, _GatherRight / _GatherUp: the picture's half axes, _GatherNormal: out of its wall), shrinking and fading
// as they arrive (_Gather 0..1, each quad a little later than the last by its random).
// Alpha-blended, so layers of paint build toward the pigment's own colour (never toward black):
//   - the VR room (_Wash 0): LIT as the wall under it, per vertex (ambient SH + the main light + the room's point lights),
//     its albedo the pigment (a little deeper where it is dense: the dried rim, the grain, the bristles), so it never glows;
//   - mixed reality (_Wash 1): a translucent unlit wash over the passthrough, its alpha accumulated as over.
// Atlas Room/paint_brushes (Textures/PaintArt.cs, 4 x 4 cells): R = pigment density, A = coverage.
// Single-pass instanced / multiview: instance id in, stereo output initialised, eye index set up in the fragment.
Shader "MusicRoom/WallPaint"
{
    Properties
    {
        _BaseMap ("Brushes", 2D) = "white" {}
        _PaintTime ("Paint time", Float) = 0
        _Fade0 ("Fade A", Float) = 1
        _Fade1 ("Fade B", Float) = 1
        _Wash ("MR wash", Float) = 0
        _Strength ("Strength", Float) = 1
        _Albedo ("Albedo gain", Float) = 1.35
        _GatherOn ("Gather copies", Float) = 0
        _Gather ("Gather", Float) = 0
        _GatherGen ("Gather painting", Float) = 0
        _GatherCentre ("Gather centre", Vector) = (0, 0, 0, 0)
        _GatherRight ("Gather right", Vector) = (0.3, 0, 0, 0)
        _GatherUp ("Gather up", Vector) = (0, 0.2, 0, 0)
        _GatherNormal ("Gather normal", Vector) = (0, 0, 1, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendA ("Src alpha", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendA ("Dst alpha", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-40" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardWallPaint"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendA] [_DstBlendA]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _PaintTime;
                half _Fade0;
                half _Fade1;
                half _Wash;
                half _Strength;
                half _Albedo;
                half _GatherOn;
                float _Gather;
                half _GatherGen;
                float4 _GatherCentre;
                float4 _GatherRight;
                float4 _GatherUp;
                float4 _GatherNormal;
                half _SrcBlend;
                half _DstBlend;
                half _SrcBlendA;
                half _DstBlendA;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 anim : TEXCOORD1;
                float4 anim2 : TEXCOORD2;
                float4 centre : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;      // u, v in the cell, the cell's offset in the atlas
                half4 color : COLOR;        // pigment, strength x life x fade
                half4 light : TEXCOORD1;    // the wall's light here (rgb), the grow scale (a)
                float4 misc : TEXCOORD2;    // mode, wipe progress, wipe coordinate, hue turn
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The swirl: each copy lifts off its wall and flies (a little later than the last, by its random) to a ring
            // turning in front of the frame, which then winds inward into the picture; the copies shrink on the way and
            // fade into the picture as they arrive.
            float3 GatherPosition(float3 p, float3 c, float seed, inout float alpha)
            {
                float d = seed * 0.3;
                float k = saturate((_Gather - d) / 0.7);
                float fly = smoothstep(0.0, 0.55, k), wind = smoothstep(0.4, 1.0, k);
                float3 right = normalize(_GatherRight.xyz), up = normalize(_GatherUp.xyz), n = _GatherNormal.xyz;
                float hx = frac(seed * 7.13 + 0.31) * 2.0 - 1.0, hy = frac(seed * 3.71 + 0.17) * 2.0 - 1.0;
                float3 target = _GatherCentre.xyz + _GatherRight.xyz * (hx * 0.8) + _GatherUp.xyz * (hy * 0.8) + n * 0.01;
                // the ring: in front of the frame, turning; its radius shrinks as it winds in
                float phi = seed * 6.2832 + k * 7.0;
                float radius = lerp(0.5, 0.05, wind);
                float3 ring = _GatherCentre.xyz + n * lerp(0.45, 0.02, wind) + (right * cos(phi) + up * sin(phi)) * radius;
                // the flight to the ring arcs out into the room
                float3 path = lerp(c, ring, fly);
                path += (n * 0.3 + float3(0, 0.15, 0)) * sin(3.14159 * fly) * length(ring - c) * 0.25;
                path = lerp(path, target, wind * wind);
                float shrink = lerp(1.0, 0.7, fly) * lerp(1.0, 0.25, wind);
                alpha *= 1.0 - smoothstep(0.78, 1.0, k);
                return path + (p - c) * shrink;
            }

            float3 HueTurn(float3 c, float turns)
            {
                float a = turns * 6.2832;
                float ca = cos(a), sa = sin(a);
                const float3 k = float3(0.57735, 0.57735, 0.57735);
                return c * ca + cross(k, c) * sa + k * dot(k, c) * (1.0 - ca);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float mode = v.anim.y;
                float age = _PaintTime - v.uv.w;
                float born = step(0.0, age);
                float3 pos = v.positionOS.xyz;
                float scale = 1.0, life = born, wipe = 1.0;
                if (mode < 0.5)
                {
                    float k = saturate(age / max(v.anim.z, 1e-3));
                    float e = 1.0 - (1.0 - k) * (1.0 - k) * (1.0 - k);   // ease out
                    scale = lerp(v.anim.w, 1.0, e);
                    life = born * saturate(e * 1.6) * lerp(1.0, v.anim2.x, e);
                }
                else if (mode < 1.5)
                {
                    float k = saturate(age / max(v.anim2.y, 1e-3));
                    float e = 1.0 - (1.0 - k) * (1.0 - k);
                    pos += float3(v.anim.z, v.anim.w, v.anim2.x) * e;
                    life = born * saturate(age * 10.0);
                }
                else
                {
                    wipe = saturate(age / max(v.anim.z, 1e-3));
                }
                float fade = v.anim.x < 0.5 ? _Fade0 : _Fade1;
                float alpha = v.color.a * life * _Strength;
                if (_GatherOn > 0.5)
                {
                    // the copies do not fade with the walls: only the painting being framed, fully drawn
                    // one centre for all four corners (a falling leaf keeps its drift relative to it)
                    pos = GatherPosition(pos, v.centre.xyz, v.anim2.w, alpha);
                    alpha *= saturate(1.0 - abs(v.anim.x - _GatherGen));
                    alpha *= mode > 1.5 ? 0.6 : 1.0;   // the strokes' pieces fly lighter than the splashes
                    wipe = 1.0;
                }
                else alpha *= fade;
                float3 positionWS = TransformObjectToWorld(pos);
                o.positionCS = TransformWorldToHClip(positionWS);
                // the light on the wall here (VR); MR's wash ignores it
                half3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                half3 light = SampleSH(n);
                Light mainLight = GetMainLight();
                light += mainLight.color * (mainLight.distanceAttenuation * saturate(dot(n, mainLight.direction)));
                uint count = (uint)GetAdditionalLightsCount();
                for (uint li = 0u; li < count; ++li)
                {
                    Light l = GetAdditionalLight(li, positionWS);
                    light += l.color * (l.distanceAttenuation * saturate(dot(n, l.direction)));
                }
                float cell = v.uv.z;
                float2 offset = float2(fmod(cell, 4.0), floor(cell * 0.25)) * 0.25;
                o.uv = float4(v.uv.xy, offset);
                o.light = half4(light, (half)scale);
                o.color = half4(v.color.rgb, (half)alpha);
                o.misc = float4(mode, wipe, v.anim2.z, v.anim.w);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex;
                half vis = 1.0h;
                half3 pigment = i.color.rgb;                                    // linear
                if (i.misc.x > 1.5)
                {
                    // a stroke: the cell repeats along u (columns 1..127 of it), drawn up to the wipe
                    float cu = (frac(i.uv.x) * 126.0 + 1.0) / 128.0;
                    float2 suv = i.uv.zw + float2(cu, saturate(i.uv.y)) * 0.25;
                    float2 g = float2(i.uv.x * (126.0 / 128.0), i.uv.y) * 0.25;
                    tex = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, suv, ddx(g), ddy(g));
                    vis = (half)saturate((i.misc.y * 1.04 - i.misc.z) * 25.0);
                    if (i.misc.x > 2.5) pigment = max(HueTurn(pigment, (i.uv.y - 0.5) * i.misc.w), 0.0);
                }
                else
                {
                    // grow from the middle: the corner uv scaled about the centre, clamped to the cell
                    float2 local = (i.uv.xy - 0.5) / max(i.light.a, 1e-3) + 0.5;
                    vis = (half)(step(0.0, local.x) * step(local.x, 1.0) * step(0.0, local.y) * step(local.y, 1.0));
                    tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.zw + saturate(local) * 0.25);
                }
                half a = saturate(tex.a * i.color.a * vis);
                half deep = 1.12h - 0.42h * tex.r;                              // the dried rim, the grain: deeper
                // VR: the paint's albedo lit as the wall; MR: the pigment as a wash
                half3 lit = saturate(pigment * _Albedo * deep) * i.light.rgb;
                half3 wash = pigment * (0.6h + 0.5h * (1.0h - tex.r));
                return half4(lerp(lit, wash, _Wash), a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
