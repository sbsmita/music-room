// The butterflies' wings (Decor/Butterflies.cs): one 512 x 256 atlas of eight wing designs, 4 x 2 tiles of 128 px, each
// tile ONE wing (forewing over hindwing, the body's half along the hinge edge) drawn with signed distances to its outline:
// x runs from the hinge (0) out to the tip (1), y from the rear (0) to the front (1); the other wing is the same tile
// mirrored by the mesh. Colourful but natural, two of them orange like the playback glow:
//   0 orange with black veins and a white-dotted black border; 1 pale yellow with black bands, a tail and a blue-and-orange
//   hind spot; 2 bright blue fading out from the body, black edge, white fringe; 3 cream white with a dark forewing tip;
//   4 near black with an orange-red band and white tip spots; 5 orange with black spots; 6 lemon yellow with an orange
//   dot; 7 rust red with eyespots.
// Every texel gets the colour the pattern has there (outside the outline too), so filtering and mips never darken an
// edge; alpha is the outline's coverage. Pure (Color32 only): baked by `Tools/unity.sh bake` (Room/butterfly_wings), or
// generated on a worker at runtime when unbaked.
using System;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Textures
{
    public static class ButterflyArt
    {
        public const int Tile = 128, Cols = 4, Rows = 2, Designs = Cols * Rows, Inset = 4;
        public const int Width = Tile * Cols, Height = Tile * Rows;
        /// <summary>The outlines' corner rounding (tile units).</summary>
        public const float Round = 0.035f;

        /// <summary>The atlas UV of a point of design <paramref name="d"/>'s wing (x hinge 0 to tip 1, y rear 0 to front 1).</summary>
        public static Vector2 Uv(int d, float x, float y)
        {
            int c = d % Cols, r = (d / Cols) % Rows;
            float s = (Tile - 2f * Inset) / Tile;
            return new Vector2((c + (Inset / (float)Tile) + x * s) / Cols, (r + (Inset / (float)Tile) + y * s) / Rows);
        }

        static readonly Vector2[] Fore =
        {
            new Vector2(0f, 0.52f), new Vector2(0.4f, 0.47f), new Vector2(0.67f, 0.52f), new Vector2(0.85f, 0.67f), new Vector2(0.93f, 0.83f),
            new Vector2(0.87f, 0.92f), new Vector2(0.55f, 0.955f), new Vector2(0.18f, 0.94f), new Vector2(0f, 0.9f),
        };
        static readonly Vector2[] Hind =
        {
            new Vector2(0f, 0.54f), new Vector2(0f, 0.27f), new Vector2(0.1f, 0.13f), new Vector2(0.3f, 0.06f), new Vector2(0.5f, 0.1f),
            new Vector2(0.64f, 0.24f), new Vector2(0.65f, 0.4f), new Vector2(0.5f, 0.52f),
        };
        static readonly Vector2[] HindTail =
        {
            new Vector2(0f, 0.54f), new Vector2(0f, 0.32f), new Vector2(0.1f, 0.19f), new Vector2(0.28f, 0.14f), new Vector2(0.37f, 0.12f),
            new Vector2(0.41f, 0.03f), new Vector2(0.45f, 0.04f), new Vector2(0.47f, 0.14f), new Vector2(0.58f, 0.21f), new Vector2(0.65f, 0.36f),
            new Vector2(0.5f, 0.52f),
        };

        public static PixelImage Wings(string name = "Room/butterfly_wings")
        {
            var px = new Color32[Width * Height];
            for (int d = 0; d < Designs; d++)
            {
                int c = d % Cols, r = d / Cols;
                for (int ty = 0; ty < Tile; ty++)
                    for (int tx = 0; tx < Tile; tx++)
                    {
                        float x = (tx + 0.5f - Inset) / (Tile - 2f * Inset), y = (ty + 0.5f - Inset) / (Tile - 2f * Inset);
                        px[(r * Tile + ty) * Width + c * Tile + tx] = Texel(d, x, y);
                    }
            }
            return new PixelImage { Name = name, Width = Width, Height = Height, Pixels = px, Mipmaps = true };
        }

        /// <summary>One texel of design d at wing point (x, y).</summary>
        public static Color32 Texel(int d, float x, float y)
        {
            var p = new Vector2(x, y);
            Vector2[] hindShape = d == 1 ? HindTail : Hind;
            // rounded outlines: the polygons dilated by Round, cut at the hinge
            float sf = Mathf.Max(Sdf(Fore, p) - Round, -x), sh = Mathf.Max(Sdf(hindShape, p) - Round, -x);
            float s = Mathf.Min(sf, sh);
            bool fore = sf <= sh;
            float edge = -s;   // distance inside the outline (negative outside)
            float px = 1f / (Tile - 2f * Inset);
            float alpha = Mathf.Clamp01(0.5f - s / (1.2f * px));
            Color col = Pattern(d, p, fore, edge);
            // the body's half along the hinge edge: a dark, slightly furry strip with a rounded head in front
            float bodyHalf = Body(y);
            float db = x - bodyHalf;
            float head = new Vector2(x, y - 0.86f).magnitude - 0.04f;
            float body = Mathf.Min(db, head);
            if (y < 0.2f || y > 0.92f) body = Mathf.Max(body, head);
            float bodyA = Mathf.Clamp01(0.5f - body / (1.5f * px));
            if (bodyA > 0f)
            {
                Color bc = new Color(0.16f, 0.13f, 0.11f) * (0.85f + 0.15f * Mathf.Clamp01(x / Mathf.Max(bodyHalf, 1e-3f)));
                col = Color.Lerp(col, bc, bodyA);
                alpha = Mathf.Max(alpha, bodyA);
            }
            // fine scales: a little value noise
            float n = (float)ProcTex.VNoise(x * 40.0 + d * 13.0, y * 40.0);
            col *= 0.93f + 0.1f * n;
            col.a = alpha;
            return (Color32)new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), alpha);
        }

        /// <summary>The body's half width at y (thorax wide, abdomen tapering to the rear).</summary>
        static float Body(float y)
        {
            if (y < 0.2f || y > 0.92f) return -1f;
            if (y > 0.55f) return 0.042f;
            float t = (y - 0.2f) / 0.35f;
            return 0.018f + 0.022f * Mathf.Sqrt(t);
        }

        static Color Hex(string h) => TextureUtil.Hex(h);

        static Color Pattern(int d, Vector2 p, bool fore, float edge)
        {
            float x = p.x, y = p.y;
            Vector2 c = new Vector2(0f, 0.55f);
            Vector2 rel = p - c;
            float r = rel.magnitude, th = Mathf.Atan2(rel.y, rel.x);
            // radial veins from the wing base
            float veinStep = Mathf.PI / 9f;
            float vf = th / veinStep;
            float veinDist = Mathf.Abs(vf - Mathf.Round(vf)) * veinStep * r;
            bool vein = veinDist < 0.009f && r > 0.12f;
            bool cell = Mathf.Abs(r - 0.42f) < 0.008f && r > 0.1f;
            Color black = Hex("#17120f"), white = Hex("#f7f3ea");
            float hingeShade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(x / 0.25f));
            switch (d)
            {
                case 0:   // orange, black veins and border with white dots
                {
                    Color b = Color.Lerp(Hex("#d9590f"), Hex("#f59a2c"), Mathf.Clamp01(r * 1.4f)) * hingeShade;
                    if (vein || cell) b = black;
                    if (fore && (p - new Vector2(0.93f, 0.88f)).magnitude < 0.2f) b = black;
                    if (edge < 0.075f) b = black;
                    if (Dots(edge, th, 0.075f, 22f, 0.012f)) b = white;
                    if (fore && Spot(p, 0.8f, 0.84f, 0.025f) || fore && Spot(p, 0.86f, 0.77f, 0.02f)) b = Hex("#f7c27a");
                    return b;
                }
                case 1:   // pale yellow, black bands, blue and orange spots on the hindwing
                {
                    Color b = Hex("#f4d64e") * hingeShade;
                    if (fore && Frac(x * 4.2f + y * 0.9f) < 0.2f && x > 0.12f) b = black;
                    if (vein) b = Color.Lerp(b, black, 0.7f);
                    if (edge < 0.08f) b = black;
                    if (Dots(edge, th, 0.08f, 18f, 0.014f)) b = Hex("#f2d55a");
                    if (!fore && Spot(p, 0.33f, 0.22f, 0.05f)) b = Hex("#3c6fd6");
                    if (!fore && Spot(p, 0.12f, 0.24f, 0.045f)) b = Hex("#e86a1a");
                    return b;
                }
                case 2:   // blue
                {
                    Color b = Color.Lerp(Hex("#1f4fbf"), Hex("#62c0ff"), Mathf.Clamp01(r * 1.6f)) * hingeShade;
                    float bw = fore ? Mathf.Lerp(0.05f, 0.14f, Mathf.Clamp01((x - 0.5f) * 2.4f) * Mathf.Clamp01((y - 0.6f) * 3f)) : 0.05f;
                    if (edge < bw) b = black;
                    if (edge < 0.014f) b = white;
                    if (vein) b = Color.Lerp(b, black, 0.35f);
                    return b;
                }
                case 3:   // cream white, a dark forewing tip and a spot
                {
                    Color b = Hex("#f3efe2") * Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(x / 0.2f));
                    if (fore && (p - new Vector2(0.95f, 0.9f)).magnitude < 0.2f) b = Hex("#3a3632");
                    if (fore && Spot(p, 0.58f, 0.72f, 0.04f)) b = Hex("#2e2a27");
                    if (vein && r < 0.35f) b = Color.Lerp(b, Hex("#8a867c"), 0.5f);
                    return b;
                }
                case 4:   // near black, orange-red band, white tip spots
                {
                    Color b = Hex("#2b201b");
                    // a broad band across the forewing's middle, from its front edge toward the rear corner
                    float band = Mathf.Abs(0.62f * (x - 0.42f) + 0.78f * (y - 0.74f));
                    if (fore && band < 0.065f && edge > 0.018f) b = Hex("#e2512a");
                    // the hindwing's outer border
                    if (!fore && edge > 0.022f && edge < 0.08f && r > 0.32f && y < 0.48f) b = Hex("#e2512a");
                    if (fore && (Spot(p, 0.82f, 0.86f, 0.03f) || Spot(p, 0.72f, 0.9f, 0.025f) || Spot(p, 0.88f, 0.76f, 0.022f))) b = white;
                    return b;
                }
                case 5:   // orange with black spots
                {
                    Color b = Hex("#e8892e") * hingeShade;
                    float ring = Frac(r * 6.5f);
                    float seg = Frac(th / veinStep);
                    if (ring > 0.35f && ring < 0.6f && seg > 0.3f && seg < 0.7f && r > 0.15f) b = black;
                    if (vein) b = Color.Lerp(b, black, 0.6f);
                    if (edge < 0.03f) b = black;
                    return b;
                }
                case 6:   // lemon yellow, an orange dot
                {
                    Color b = Color.Lerp(Hex("#e3e35a"), Hex("#f5ee8f"), Mathf.Clamp01(r * 1.5f));
                    if (Spot(p, fore ? 0.5f : 0.36f, fore ? 0.74f : 0.3f, 0.028f)) b = Hex("#e8862a");
                    if (edge < 0.018f) b = Hex("#c98a5c");
                    return b;
                }
                default:  // rust red with eyespots
                {
                    Color b = Color.Lerp(Hex("#7e2418"), Hex("#b5402a"), Mathf.Clamp01(r * 1.5f));
                    float e1 = (p - new Vector2(0.8f, 0.84f)).magnitude, e2 = (p - new Vector2(0.45f, 0.22f)).magnitude;
                    if (fore && e1 < 0.13f) b = e1 < 0.045f ? black : e1 < 0.08f ? Hex("#4f86d8") : e1 < 0.11f ? Hex("#f2d24e") : Hex("#2b201b");
                    if (!fore && e2 < 0.12f) b = e2 < 0.05f ? black : e2 < 0.085f ? Hex("#5a8fe0") : Hex("#2b201b");
                    if (edge < 0.035f) b = Hex("#3a2a22");
                    return b;
                }
            }
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        static bool Spot(Vector2 p, float x, float y, float r) => (p - new Vector2(x, y)).sqrMagnitude < r * r;

        /// <summary>A row of dots inside the border band (n around the wing).</summary>
        static bool Dots(float edge, float th, float band, float n, float radius)
        {
            if (edge < band * 0.2f || edge > band * 0.8f) return false;
            float f = Frac(th * n / Mathf.PI);
            float along = Mathf.Abs(f - 0.5f) * Mathf.PI / n * 0.5f;
            float across = Mathf.Abs(edge - band * 0.5f);
            return along * along + across * across < radius * radius;
        }

        /// <summary>Signed distance to a closed polygon (negative inside).</summary>
        public static float Sdf(Vector2[] poly, Vector2 p)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], e = b - a, w = p - a;
                float t = Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                d = Mathf.Min(d, (w - e * t).sqrMagnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x)) inside = !inside;
            }
            d = Mathf.Sqrt(d);
            return inside ? -d : d;
        }
    }
}
