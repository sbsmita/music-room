// Sunny's two textures, painted on the CPU (Docs/08-ux-lessons-menu-guide.md 3.1; SunnyFace, SunnyBubbleArt):
// the 512 x 256 face in its 8 states (mood x eyes x mouth) and the 768 x 340 speech bubble (the
// text itself is TextMeshPro on top). SunnyCanvas is a small raster: straight-alpha RGBA in sRGB space, source-over
// compositing, 1 px anti-aliasing from signed distances. Canvas row 0 is the top; it lands in Texture2D row h-1.
// The textures are baked by Tools/unity.sh bake (GuideTextures) and generated here as the runtime fallback.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Guide
{
    /// <summary>A tiny 2D raster: shapes are anti-aliased with coverage = clamp(0.5 - signedDistance, 0, 1).</summary>
    public sealed class SunnyCanvas
    {
        public readonly int W, H;
        public readonly float[] R, G, B, A;

        public SunnyCanvas(int w, int h)
        {
            W = w;
            H = h;
            R = new float[w * h];
            G = new float[w * h];
            B = new float[w * h];
            A = new float[w * h];
        }

        /// <summary>Source-over with straight alpha, in sRGB space.</summary>
        public void Composite(int i, Color c, float a)
        {
            if (a <= 0f) return;
            if (a > 1f) a = 1f;
            float da = A[i];
            float oa = a + da * (1f - a);
            float kd = da * (1f - a) / oa, ks = a / oa;
            R[i] = c.r * ks + R[i] * kd;
            G[i] = c.g * ks + G[i] * kd;
            B[i] = c.b * ks + B[i] * kd;
            A[i] = oa;
        }

        static float Cover(float sd) => Mathf.Clamp01(0.5f - sd);

        void Bounds(float x0, float y0, float x1, float y1, out int ix0, out int iy0, out int ix1, out int iy1)
        {
            ix0 = Mathf.Max(0, Mathf.FloorToInt(x0) - 2);
            iy0 = Mathf.Max(0, Mathf.FloorToInt(y0) - 2);
            ix1 = Mathf.Min(W - 1, Mathf.CeilToInt(x1) + 2);
            iy1 = Mathf.Min(H - 1, Mathf.CeilToInt(y1) + 2);
        }

        /// <summary>fillRect(0, 0, w, h) with createRadialGradient(cx, cy, r0, cx, cy, r1) (concentric circles: t = (d - r0) / (r1 - r0)).</summary>
        public void FillRadial(float cx, float cy, float r0, float r1, float[] offsets, Color[] colors)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float t = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - r0) / (r1 - r0));
                    Composite(y * W + x, Gradient(offsets, colors, t), 1f);
                }
        }

        static Color Gradient(float[] offsets, Color[] colors, float t)
        {
            if (t <= offsets[0]) return colors[0];
            for (int i = 1; i < offsets.Length; i++)
                if (t <= offsets[i]) return Color.Lerp(colors[i - 1], colors[i], (t - offsets[i - 1]) / (offsets[i] - offsets[i - 1]));
            return colors[colors.Length - 1];
        }

        /// <summary>The usual first-order ellipse distance (exact for circles): k0 (k0 - 1) / k1.</summary>
        public static float EllipseDistance(float dx, float dy, float rx, float ry)
        {
            float k0 = Mathf.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
            if (k0 < 1e-6f) return -Mathf.Min(rx, ry);
            float k1 = Mathf.Sqrt(dx * dx / (rx * rx * rx * rx) + dy * dy / (ry * ry * ry * ry));
            return k0 * (k0 - 1f) / k1;
        }

        public void FillEllipse(float cx, float cy, float rx, float ry, Color c, float alpha = 1f)
        {
            Bounds(cx - rx, cy - ry, cx + rx, cy + ry, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Composite(y * W + x, c, alpha * Cover(EllipseDistance(x + 0.5f - cx, y + 0.5f - cy, rx, ry)));
        }

        public void StrokeEllipse(float cx, float cy, float rx, float ry, float width, Color c)
        {
            float hw = width * 0.5f;
            Bounds(cx - rx - hw, cy - ry - hw, cx + rx + hw, cy + ry + hw, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Abs(EllipseDistance(x + 0.5f - cx, y + 0.5f - cy, rx, ry));
                    Composite(y * W + x, c, Cover(d - hw));
                }
        }

        public void FillRect(float rx, float ry, float rw, float rh, Color c)
        {
            float cx = rx + rw * 0.5f, cy = ry + rh * 0.5f, hx = rw * 0.5f, hy = rh * 0.5f;
            Bounds(rx, ry, rx + rw, ry + rh, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float sd = Mathf.Max(Mathf.Abs(x + 0.5f - cx) - hx, Mathf.Abs(y + 0.5f - cy) - hy);
                    Composite(y * W + x, c, Cover(sd));
                }
        }

        // Canvas angles run clockwise from +x with y down, which is atan2(dy, dx) in canvas coordinates.
        static float Angle01(float dx, float dy)
        {
            float a = Mathf.Atan2(dy, dx);
            return a < 0f ? a + Mathf.PI * 2f : a;
        }

        /// <summary>arc(cx, cy, r, a0, a1) + stroke() with lineCap round (the caps come from the endpoint distance).</summary>
        public void StrokeArc(float cx, float cy, float r, float a0, float a1, float width, Color c)
        {
            float hw = width * 0.5f;
            float p0x = cx + r * Mathf.Cos(a0), p0y = cy + r * Mathf.Sin(a0), p1x = cx + r * Mathf.Cos(a1), p1y = cy + r * Mathf.Sin(a1);
            Bounds(cx - r - hw, cy - r - hw, cx + r + hw, cy + r + hw, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f, dx = px - cx, dy = py - cy;
                    float ang = Angle01(dx, dy);
                    float d;
                    if (ang >= a0 && ang <= a1) d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r);
                    else d = Mathf.Min(Dist(px, py, p0x, p0y), Dist(px, py, p1x, p1y));
                    Composite(y * W + x, c, Cover(d - hw));
                }
        }

        /// <summary>arc(cx, cy, r, a0, a1) + closePath() + fill(): the circular segment cut off by the chord.</summary>
        public void FillArcChord(float cx, float cy, float r, float a0, float a1, Color c)
        {
            float mx = cx + r * 0.5f * (Mathf.Cos(a0) + Mathf.Cos(a1)), my = cy + r * 0.5f * (Mathf.Sin(a0) + Mathf.Sin(a1));
            float am = (a0 + a1) * 0.5f, nx = Mathf.Cos(am), ny = Mathf.Sin(am);
            Bounds(cx - r, cy - r, cx + r, cy + r, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float circle = Dist(px, py, cx, cy) - r;
                    float half = (mx - px) * nx + (my - py) * ny;   // negative beyond the chord, on the arc's side
                    Composite(y * W + x, c, Cover(Mathf.Max(circle, half)));
                }
        }

        /// <summary>fill() then stroke() of a closed polygon (even-odd inside, round-looking joins).</summary>
        public void FillAndStrokePolygon(IReadOnlyList<Vector2> pts, Color fill, float fillAlpha, Color stroke, float width)
        {
            int n = pts.Count;
            float hw = width * 0.5f, band = hw + 2f;
            var dist = new float[W * H];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
            for (int s = 0; s < n; s++)
            {
                Vector2 a = pts[s], b = pts[(s + 1) % n];
                Bounds(Mathf.Min(a.x, b.x) - band, Mathf.Min(a.y, b.y) - band, Mathf.Max(a.x, b.x) + band, Mathf.Max(a.y, b.y) + band,
                    out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = SegmentDistance(x + 0.5f, y + 0.5f, a, b);
                        int i = y * W + x;
                        if (d < dist[i]) dist[i] = d;
                    }
            }
            var xs = new List<float>(16);
            for (int y = 0; y < H; y++)
            {
                float py = y + 0.5f;
                xs.Clear();
                for (int s = 0; s < n; s++)
                {
                    Vector2 a = pts[s], b = pts[(s + 1) % n];
                    if ((a.y <= py) == (b.y <= py)) continue;   // half-open rule: each crossing counted once
                    xs.Add(a.x + (py - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int x = 0; x < W; x++)
                {
                    float px = x + 0.5f;
                    int crossings = 0;
                    for (int k = 0; k < xs.Count; k++) if (xs[k] < px) crossings++;
                    bool inside = (crossings & 1) == 1;
                    int i = y * W + x;
                    float d = dist[i];
                    float sd = inside ? -d : d;
                    Composite(i, fill, fillAlpha * Cover(sd));
                    if (d < band) Composite(i, stroke, Cover(d - hw));
                }
            }
        }

        static float Dist(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static float SegmentDistance(float px, float py, Vector2 a, Vector2 b)
        {
            float vx = b.x - a.x, vy = b.y - a.y;
            float l2 = vx * vx + vy * vy;
            float t = l2 > 0f ? Mathf.Clamp01(((px - a.x) * vx + (py - a.y) * vy) / l2) : 0f;
            return Dist(px, py, a.x + t * vx, a.y + t * vy);
        }

        /// <summary>To texture pixels: canvas row y goes to row H-1-y. Fully transparent pixels take `bleed` as their colour
        /// so filtering and mips never pull a dark fringe into the edges.</summary>
        public void ToPixels(Color32[] px, Color bleed)
        {
            for (int y = 0; y < H; y++)
            {
                int row = (H - 1 - y) * W;
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float a = A[i];
                    px[row + x] = a <= 0f
                        ? new Color32(To8(bleed.r), To8(bleed.g), To8(bleed.b), 0)
                        : new Color32(To8(R[i]), To8(G[i]), To8(B[i]), To8(a));
                }
            }
        }

        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>The colour at canvas pixel (x, y) (tests).</summary>
        public Color At(int x, int y)
        {
            int i = y * W + x;
            return new Color(R[i], G[i], B[i], A[i]);
        }
    }

    /// <summary>The face: the sun body gradient, cheeks, eyes (open with a shine, or shut to a line) and mouth.</summary>
    public static class SunnyFace
    {
        public const int W = 512, H = 256, StateCount = 8;
        public const float Cx = W * 0.25f, Cy = H * 0.5f;   // the face sits at u = 0.25, the side turned to the player

        public static readonly Color Ink = Hex("#3a2114"), MouthFill = Hex("#7a2a1a"), Shine = Color.white;
        public static readonly Color Cheek = new Color(255f / 255f, 130f / 255f, 90f / 255f, 1f);
        public const float CheekAlpha = 0.35f;
        static readonly float[] s_offsets = { 0f, 0.45f, 1f };
        static readonly Color[] s_stops = { Hex("#fff1a8"), Hex("#ffd45a"), Hex("#f59a2a") };

        /// <summary>The module prefix of every Guide bake name (ARCHITECTURE.md section 14: `<Module>/<name>`).</summary>
        public const string BakeFolder = "Guide/";
        static readonly string[] s_names = BuildNames();

        /// <summary>mood x eyes x mouth: (happy ? 4 : 0) + (eyesShut ? 2 : 0) + (mouthOpen ? 1 : 0).</summary>
        public static int StateIndex(bool happy, bool eyesShut, bool mouthOpen) => (happy ? 4 : 0) + (eyesShut ? 2 : 0) + (mouthOpen ? 1 : 0);

        public static string Name(int state) => s_names[state];

        static string[] BuildNames()
        {
            var n = new string[StateCount];
            for (int i = 0; i < StateCount; i++)
                n[i] = BakeFolder + "sunnyFace_" + ((i & 4) != 0 ? "happy" : "calm") + "_eyes" + ((i & 2) != 0 ? "Shut" : "Open") + "_mouth" + ((i & 1) != 0 ? "Open" : "Shut");
            return n;
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }

        public static SunnyCanvas Paint(int state)
        {
            bool happy = (state & 4) != 0, shut = (state & 2) != 0, open = (state & 1) != 0;
            var c = new SunnyCanvas(W, H);
            // Sun body: warm yellow, deeper toward the edges and the back.
            c.FillRadial(Cx, Cy, 10f, W * 0.5f, s_offsets, s_stops);
            // Cheeks.
            for (int sx = -1; sx <= 1; sx += 2) c.FillEllipse(Cx + sx * 42f, H * 0.57f, 13f, 8f, Cheek, CheekAlpha);
            // Eyes: round and shiny, or shut to a line while blinking.
            float ey = H * 0.42f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float ex = Cx + sx * 24f;
                if (shut)
                {
                    c.FillRect(ex - 11f, ey - 2f, 22f, 5f, Ink);
                    continue;
                }
                c.FillEllipse(ex, ey, 10f, happy ? 12f : 11f, Ink);
                c.FillEllipse(ex + 3.5f, ey - 4f, 3.5f, 3.5f, Shine);
            }
            // Mouth: a calm smile, wider and filled when happy; a round "o" while a word is spoken.
            const float stroke = 5f;
            if (open)
            {
                float rx = happy ? 15f : 12f, ry = happy ? 12f : 9f;
                c.FillEllipse(Cx, H * 0.59f, rx, ry, MouthFill);
                c.StrokeEllipse(Cx, H * 0.59f, rx, ry, stroke, Ink);
            }
            else if (happy)
            {
                c.StrokeArc(Cx, H * 0.55f, 20f, 0.15f * Mathf.PI, 0.85f * Mathf.PI, stroke, Ink);
                c.FillArcChord(Cx, H * 0.55f, 20f, 0.15f * Mathf.PI, 0.85f * Mathf.PI, MouthFill);
            }
            else c.StrokeArc(Cx, H * 0.56f, 14f, 0.2f * Mathf.PI, 0.8f * Mathf.PI, stroke, Ink);
            return c;
        }

        /// <summary>The face as an sRGB texture (clamped; mipmapped). `readable` keeps the CPU copy
        /// for the bake's PNG encoding.</summary>
        public static Texture2D Create(int state, bool readable)
        {
            var px = new Color32[W * H];
            Paint(state).ToPixels(px, s_stops[2]);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false)
            {
                name = Name(state), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
            };
            tex.SetPixels32(px);
            tex.Apply(true, !readable);
            return tex;
        }
    }

    /// <summary>The bubble: a rounded box (quadratic corners, r 44) with a tail pointing down-left at the robot,
    /// cream fill at 0.96, a 6 px amber outline. The caption is TextMeshPro (SunnyView) on top of this texture.</summary>
    public static class SunnyBubbleArt
    {
        public const int W = 768, H = 340;
        public const float Tail = 44f, X0 = 10f, Y0 = 10f, X1 = W - 10f, Y1 = H - Tail, Corner = 44f;
        public const float FillAlpha = 0.96f, StrokeWidth = 6f;
        // Bake names are <Module>/<name> (ARCHITECTURE.md sections 2 and 14): the PNG lands in Generated/Guide/.
        public const string TextureName = SunnyFace.BakeFolder + "sunnyBubble";
        public static readonly Color Fill = new Color(1f, 250f / 255f, 238f / 255f, 1f);
        public static readonly Color Stroke = SunnyFace.Hex("#f0b24a");
        public const int CurveSteps = 16;

        /// <summary>The outline path in canvas px (lines and quadratic corners), flattened.</summary>
        public static List<Vector2> Outline()
        {
            var p = new List<Vector2>(96);
            float r = Corner;
            p.Add(new Vector2(X0 + r, Y0));
            p.Add(new Vector2(X1 - r, Y0));
            Quadratic(p, new Vector2(X1 - r, Y0), new Vector2(X1, Y0), new Vector2(X1, Y0 + r));
            p.Add(new Vector2(X1, Y1 - r));
            Quadratic(p, new Vector2(X1, Y1 - r), new Vector2(X1, Y1), new Vector2(X1 - r, Y1));
            p.Add(new Vector2(X0 + 190f, Y1));
            p.Add(new Vector2(X0 + 120f, Y1 + Tail - 6f));
            p.Add(new Vector2(X0 + 130f, Y1));
            p.Add(new Vector2(X0 + r, Y1));
            Quadratic(p, new Vector2(X0 + r, Y1), new Vector2(X0, Y1), new Vector2(X0, Y1 - r));
            p.Add(new Vector2(X0, Y0 + r));
            Quadratic(p, new Vector2(X0, Y0 + r), new Vector2(X0, Y0), new Vector2(X0 + r, Y0));
            // closePath: the last point equals the first; drop it so the polygon has no zero-length edge.
            if ((p[p.Count - 1] - p[0]).sqrMagnitude < 1e-6f) p.RemoveAt(p.Count - 1);
            return p;
        }

        static void Quadratic(List<Vector2> p, Vector2 a, Vector2 ctrl, Vector2 b)
        {
            for (int i = 1; i <= CurveSteps; i++)
            {
                float t = (float)i / CurveSteps, u = 1f - t;
                p.Add(u * u * a + 2f * u * t * ctrl + t * t * b);
            }
        }

        public static SunnyCanvas Paint()
        {
            var c = new SunnyCanvas(W, H);
            c.FillAndStrokePolygon(Outline(), Fill, FillAlpha, Stroke, StrokeWidth);
            return c;
        }

        public static Texture2D Create(bool readable)
        {
            var px = new Color32[W * H];
            Paint().ToPixels(px, Stroke);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false)
            {
                name = TextureName, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, !readable);
            return tex;
        }
    }
}
