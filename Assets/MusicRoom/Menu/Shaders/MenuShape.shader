// The palm menu's 2D painter (spec 08 section 1.3): one primitive per draw, evaluated as a signed distance in canvas
// pixels (origin top-left, y down, MenuStyle's canvas px), so rounded rectangles, strokes, icons and
// the back plate's glow come out crisp with a one-pixel edge and need no textures. Drawn ONLY by GpuMenuCanvas through
// a CommandBuffer into the menu's render texture (never by a scene camera, so no stereo or lighting variants are
// needed). Colours arrive as raw sRGB numbers and are blended as such into a linear (non-sRGB) render texture, which
// blends in gamma space; MusicRoom/MenuFace converts to linear when the panel is drawn in the room. Primitive 5
// draws an image (the instruments' portrait atlas, platform.md 5.2) the same way, encoding a linear source to sRGB.
Shader "MusicRoom/MenuShape"
{
    Properties
    {
        _Color ("Colour", Vector) = (1, 1, 1, 1)
        _MainTex ("Image (primitive 5)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        // Straight-alpha colour over a cleared (0,0,0,0) target, alpha accumulated "over" (source-over compositing).
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _Bounds;   // x0 y0 x1 y1: the quad covers the primitive plus its edge
            float4 _A;        // primitive geometry (see frag)
            float4 _B;
            float4 _Clip;     // x0 y0 x1 y1: fragments outside are discarded (list viewport)
            float4 _Grad;     // mode (0 flat, 1 vertical, 2 horizontal), from, to, mid stop (0 = two stops)
            float4 _Color, _Color2, _Color3;
            float _Prim;
            sampler2D _MainTex;
            float4 _UvRect;   // primitive 5: the texture's u0 v0 u1 v1 (v0 = the image's bottom)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 px : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float2 px = lerp(_Bounds.xy, _Bounds.zw, v.uv);
                o.px = px;
                o.pos = UnityObjectToClipPos(float4(px, 0.0, 1.0));
                return o;
            }

            // Rounded box: centre c, half size h, corner radius r (r <= min(h)).
            float sdBox(float2 p, float2 c, float2 h, float r)
            {
                float2 q = abs(p - c) - (h - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h);
            }

            // Distance to an ellipse outline (iq's first-order estimate; exact for a circle).
            float sdEllipse(float2 q, float2 ab)
            {
                float k0 = length(q / ab);
                float k1 = length(q / (ab * ab));
                return k0 * (k0 - 1.0) / max(k1, 1e-6);
            }

            float sdTriangle(float2 p, float2 p0, float2 p1, float2 p2)
            {
                float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
                float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
                float2 pq0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
                float2 pq1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
                float2 pq2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
                float s = sign(e0.x * e2.y - e0.y * e2.x);
                float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                                   float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                                   float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
                return -sqrt(d.x) * sign(d.y);
            }

            // erf to ~1e-4 (Winitzki): the box shadow is a Gaussian of sigma = blur / 2.
            float erfApprox(float x)
            {
                float x2 = x * x;
                float t = sqrt(1.0 - exp(-x2 * (1.27323954 + 0.147 * x2) / (1.0 + 0.147 * x2)));
                return x < 0.0 ? -t : t;
            }

            // The exact sRGB encoding (the canvas keeps sRGB numbers; an sRGB render target samples as linear).
            float3 LinearToSrgb(float3 c)
            {
                c = saturate(c);
                float3 lo = c * 12.92;
                float3 hi = 1.055 * pow(max(c, 1e-6), 1.0 / 2.4) - 0.055;
                return lerp(hi, lo, step(c, 0.0031308));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.px;
                if (p.x < _Clip.x || p.y < _Clip.y || p.x > _Clip.z || p.y > _Clip.w) discard;
                int prim = (int)(_Prim + 0.5);
                if (prim == 5)
                {
                    // Image in a rounded box: _A box, _B = (radius, 1 = the texture holds linear values). Canvas y runs
                    // down, texture v up: the box's top row samples v1.
                    float d = sdBox(p, _A.xy, _A.zw, _B.x);
                    float2 t = saturate((p - (_A.xy - _A.zw)) / max(2.0 * _A.zw, 1e-4));
                    float2 uv = float2(lerp(_UvRect.x, _UvRect.z, t.x), lerp(_UvRect.w, _UvRect.y, t.y));
                    float3 rgb = tex2D(_MainTex, uv).rgb;
                    if (_B.y > 0.5) rgb = LinearToSrgb(rgb);
                    return float4(rgb * _Color.rgb, _Color.a * saturate(0.5 - d));
                }
                float cov;
                if (prim == 0)
                {
                    // Rounded box: _A = (cx, cy, hx, hy), _B = (radius, stroke width; 0 = fill). A stroke is
                    // centred on the path, so the stroke is |d| <= width / 2.
                    float d = sdBox(p, _A.xy, _A.zw, _B.x);
                    if (_B.y > 0.0) d = abs(d) - _B.y * 0.5;
                    cov = saturate(0.5 - d);
                }
                else if (prim == 1)
                {
                    // Segment with round caps: _A = (x0, y0, x1, y1), _B.x = line width.
                    cov = saturate(0.5 - (sdSegment(p, _A.xy, _A.zw) - _B.x * 0.5));
                }
                else if (prim == 2)
                {
                    // Ellipse outline: _A = (cx, cy, rx, ry), _B = (line width, half): half +1 keeps the upper half
                    // (canvas y <= cy), -1 the lower half, 0 the whole ring. Past the kept half the distance is to the
                    // two ends, which gives round line caps.
                    float2 q = p - _A.xy;
                    float d = (_B.y != 0.0 && q.y * _B.y > 0.0)
                        ? min(length(q - float2(-_A.z, 0.0)), length(q - float2(_A.z, 0.0)))
                        : abs(sdEllipse(q, _A.zw));
                    cov = saturate(0.5 - (d - _B.x * 0.5));
                }
                else if (prim == 3)
                {
                    // Filled triangle: _A = (x0, y0, x1, y1), _B.xy = (x2, y2).
                    cov = saturate(0.5 - sdTriangle(p, _A.xy, _A.zw, _B.xy));
                }
                else
                {
                    // Box shadow (Gaussian blur): _A box, _B = (radius, sigma).
                    float d = sdBox(p, _A.xy, _A.zw, _B.x);
                    cov = 0.5 * (1.0 - erfApprox(d / (_B.y * 1.41421356)));
                }

                float4 c = _Color;
                int gm = (int)(_Grad.x + 0.5);
                if (gm != 0)
                {
                    float t = saturate(((gm == 2 ? p.x : p.y) - _Grad.y) / max(_Grad.z - _Grad.y, 1e-4));
                    float mid = _Grad.w;
                    if (mid > 0.0 && mid < 1.0)
                        c = t < mid ? lerp(_Color, _Color2, t / mid) : lerp(_Color2, _Color3, (t - mid) / (1.0 - mid));
                    else
                        c = lerp(_Color, _Color2, t);
                }
                c.a *= cov;
                return c;
            }
            ENDCG
        }
    }
}
