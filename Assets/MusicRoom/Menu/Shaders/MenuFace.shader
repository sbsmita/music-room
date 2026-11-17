// The palm menu's face and back plate in the room (spec 08 section 1.3): unlit, the painted canvas times the open /
// close fade. The canvas render texture is linear-format but holds sRGB numbers blended in gamma space
// (premultiplied wherever it is not opaque), so this shader converts the premultiplied sRGB value to linear and outputs
// premultiplied colour: Blend One OneMinusSrcAlpha, "output tex x fade". As designed (alpha test 0.02, depth
// write on), it clips nearly-clear texels and writes depth, so hover glows drawn after it land on top of the face and
// transparents behind it (window glass, snow) never draw over it. Queue comes from the template (face 2999, back plate
// 2998: before the 3000 transparents). Single-pass instanced stereo for the headset.
// Hover (ARCHITECTURE.md 15.6: never a canvas repaint for hover): up to two row highlights, the hovered row fading in
// and the previous one fading out, are drawn here from uniforms of the face's own material clone, with the
// drawRow / drawHeader shapes (rgba(95,216,255,.22k) fill, a 4 px stroke at alpha k inset 2 px; the back row: fill
// only). _HoverRectN = canvas px (x0, y0, x1, y1), _HoverParamN = (corner radius, k, stroke 0/1, -),
// _HoverClip = (y0, y1) of each slot's list clip. The back plate uses the same shader with k = 0.
Shader "MusicRoom/MenuFace"
{
    Properties
    {
        [MainTexture] _BaseMap ("Canvas", 2D) = "black" {}
        [MainColor] _BaseColor ("Fade (alpha)", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.02
        [HideInInspector] _HoverRect0 ("Hover rect 0 (px)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HoverParam0 ("Hover 0 (radius, k, stroke)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HoverRect1 ("Hover rect 1 (px)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HoverParam1 ("Hover 1 (radius, k, stroke)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HoverClip ("Hover clips (y0, y1, y0, y1)", Vector) = (-100000, 100000, -100000, 100000)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "MenuFace"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

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
                half _Cutoff;
                float4 _HoverRect0, _HoverParam0, _HoverRect1, _HoverParam1, _HoverClip;
            CBUFFER_END

            // The canvas size in px (MenuConstants.CanvasW / CanvasH) and the hover colour (sRGB, as painted).
            static const float2 CanvasPx = float2(1024.0, 768.0);
            static const half3 HoverRgb = half3(95.0 / 255.0, 216.0 / 255.0, 1.0);

            // Signed distance (px) to a rounded rectangle given by its corners and radius.
            float RoundRectSd(float2 p, float4 rect, float r)
            {
                float2 c = 0.5 * (rect.xy + rect.zw);
                float2 h = 0.5 * (rect.zw - rect.xy);
                float2 q = abs(p - c) - (h - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // One highlight over the premultiplied canvas colour c (canvas "source-over").
            half4 Highlight(half4 c, float2 px, float aa, float4 rect, float4 prm, float2 clipY)
            {
                if (prm.y <= 0.0) return c;
                float inClip = step(clipY.x, px.y) * step(px.y, clipY.y);
                float fill = saturate(0.5 - RoundRectSd(px, rect, prm.x) / aa) * inClip;
                half a = (half)(0.22 * prm.y * fill);
                c = half4(HoverRgb * a, a) + c * (1.0h - a);
                if (prm.z > 0.5)
                {
                    float d = RoundRectSd(px, rect + float4(2.0, 2.0, -2.0, -2.0), max(prm.x - 2.0, 0.0));
                    float ring = saturate(0.5 - (abs(d) - 2.0) / aa) * inClip;
                    a = (half)(prm.y * ring);
                    c = half4(HoverRgb * a, a) + c * (1.0h - a);
                }
                return c;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // The canvas texel is premultiplied in gamma space (P = C a). The panel is designed to blend in
                // gamma space; a Linear project blends in linear space, which would make the back plate's glow and every
                // fading texel far brighter (a .27-alpha glow: sRGB 144 instead of 70 over the dark room). Converting the
                // premultiplied value, lin(P k), gives the gamma-space result exactly over black and closely over the dark
                // room; opaque texels (the whole face) are unchanged.
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                if (_HoverParam0.y > 0.0 || _HoverParam1.y > 0.0)
                {
                    float2 px = float2(i.uv.x, 1.0 - i.uv.y) * CanvasPx;   // canvas px, y down (MenuMath.ToCanvas)
                    float aa = max(length(fwidth(px)), 1.0);              // >= 1 canvas px, wider when minified
                    c = Highlight(c, px, aa, _HoverRect0, _HoverParam0, _HoverClip.xy);
                    c = Highlight(c, px, aa, _HoverRect1, _HoverParam1, _HoverClip.zw);
                }
                half k = _BaseColor.a;
                half a = c.a * k;
                clip(a - _Cutoff);
                return half4(SRGBToLinear(saturate(c.rgb * k)), a);
            }
            ENDHLSL
        }
    }
}
