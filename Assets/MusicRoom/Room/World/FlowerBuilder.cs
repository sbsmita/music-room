// The room's flowers (2026-11-11, "bring all the butterflies and flowers in the room"): four small arrangements that stand
// on furniture, built in the right-handed source frame like every other piece (RoomBuilder.Flowers places them):
//   - a cream ceramic vase of tulips (orange, red, yellow) at the right end of the mantel;
//   - a brass bud vase of white daisies at its left end;
//   - a terracotta pot of red geraniums on the window sill, under the mullion;
//   - a stone jar of pink cosmos on top of the bookshelf, beside the fern.
// Vases join the room's static batches (Trim, Brass, Terracotta, Stone, Soil), stems and leaves the plants' stem and leaf
// batches, petals ONE new batch (RoomMat.Petal: MusicRoom/RoomLit with _SWAY, both faces lit, _EMISSION; the colours
// are a 8 x 2 palette, PetalPalette, so every petal of every flower is one renderer). Everything above the vase mouth
// sways about it with the plants' sway (TEXCOORD2 / 3), and its pivot's w (Response) lets it answer the music too (the
// _MusicSway global, ListenBackPulse): the flowers sway more and their petals glow brighter while music is recorded or
// played back. Perches: the top of every flower head (the butterflies land there between phrases).
// Pure geometry (no Unity objects but the palette texture): EditMode tests build it directly.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Geometry;
using UnityEngine;

namespace MusicRoom.Room.World
{
    public enum FlowerKind : byte { Tulip = 0, Daisy, Geranium, Cosmos }

    public enum VaseKind : byte { Ceramic = 0, BudBrass, Pot, StoneJar }

    /// <summary>The petals' colours: 8 columns (a flower colour each), 2 rows (the petal's base, its tip), sampled at the
    /// column's centre so filtering only blends base to tip.</summary>
    public static class PetalPalette
    {
        public const int Columns = 8;
        public const int TulipOrange = 0, TulipRed = 1, TulipYellow = 2, DaisyWhite = 3, Centre = 4, Geranium = 5, CosmosPink = 6, TulipPeach = 7;
        /// <summary>sRGB, base then tip (a flower centre: its dark middle, then its golden rim).</summary>
        public static readonly string[] Base = { "#e0611a", "#b3172a", "#e3a417", "#d9d4c8", "#6b4410", "#c21f3d", "#c9507a", "#e8825a" };
        public static readonly string[] Tip = { "#ffa24a", "#e9443c", "#ffe066", "#ffffff", "#f2c22e", "#f2546a", "#f7b2cc", "#ffd2b4" };

        public static Vector2 Uv(int column, float along) => new Vector2((column + 0.5f) / Columns, 0.25f + 0.5f * Mathf.Clamp01(along));

        public static Texture2D Make()
        {
            var t = new Texture2D(Columns, 2, TextureFormat.RGBA32, false, false)
            {
                name = "Room petal palette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[Columns * 2];
            for (int c = 0; c < Columns; c++) { px[c] = TextureUtil.Hex(Base[c]); px[Columns + c] = TextureUtil.Hex(Tip[c]); }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }

    public struct BouquetSpec
    {
        public string Name;
        public FlowerKind Kind;
        public VaseKind Vase;
        public int Count, Leaves;
        /// <summary>Stem height above the vase mouth (min, max) and how far the heads lean out (m).</summary>
        public float HeightMin, HeightMax, Spread;
        /// <summary>The palette columns the heads take in turn.</summary>
        public int[] Colours;
        /// <summary>A lean of the whole arrangement toward source -z (the room, for the mantel's), radians.</summary>
        public float Lean;
        public float Size;
    }

    public sealed class Bouquet
    {
        /// <summary>The vase (or pot) and the soil, in the arrangement's frame (source frame, base at the origin).</summary>
        public readonly Geo Vase = new Geo(), Soil = new Geo();
        public readonly Geo Stems = new Geo(), Leaves = new Geo(), Petals = new Geo();
        /// <summary>Where a butterfly lands: the top of each flower head (arrangement frame).</summary>
        public readonly List<Vector3> Heads = new List<Vector3>();
        public float MouthY, Phase, Amp;
        public int FlowerCount;
    }

    public static class FlowerBuilder
    {
        /// <summary>How strongly the flowers answer the music's sway (the pivot's w; the plants' is 0).</summary>
        public const float Response = 1.6f;

        public static readonly BouquetSpec Tulips = new BouquetSpec
        {
            Name = "tulips", Kind = FlowerKind.Tulip, Vase = VaseKind.Ceramic, Count = 7, Leaves = 4, HeightMin = 0.2f, HeightMax = 0.3f,
            Spread = 0.11f, Colours = new[] { PetalPalette.TulipOrange, PetalPalette.TulipRed, PetalPalette.TulipYellow, PetalPalette.TulipPeach }, Lean = 0.12f, Size = 1f,
        };
        public static readonly BouquetSpec Daisies = new BouquetSpec
        {
            Name = "daisies", Kind = FlowerKind.Daisy, Vase = VaseKind.BudBrass, Count = 4, Leaves = 2, HeightMin = 0.13f, HeightMax = 0.2f,
            Spread = 0.08f, Colours = new[] { PetalPalette.DaisyWhite }, Lean = 0.15f, Size = 1f,
        };
        public static readonly BouquetSpec Geraniums = new BouquetSpec
        {
            Name = "geraniums", Kind = FlowerKind.Geranium, Vase = VaseKind.Pot, Count = 4, Leaves = 9, HeightMin = 0.15f, HeightMax = 0.22f,
            Spread = 0.07f, Colours = new[] { PetalPalette.Geranium }, Lean = 0f, Size = 1f,
        };
        public static readonly BouquetSpec CosmosJar = new BouquetSpec
        {
            Name = "cosmos", Kind = FlowerKind.Cosmos, Vase = VaseKind.StoneJar, Count = 5, Leaves = 3, HeightMin = 0.2f, HeightMax = 0.3f,
            Spread = 0.12f, Colours = new[] { PetalPalette.CosmosPink }, Lean = 0.1f, Size = 1f,
        };

        public static Bouquet Build(in BouquetSpec s, uint seed)
        {
            var rng = new Mulberry32(seed);
            float Rand(float a, float b) => a + (float)rng.Next() * (b - a);
            var b = new Bouquet();
            float mouth = BuildVase(b, s.Vase);
            b.MouthY = mouth;
            Geo blade = PlantBuilder.LeafBlade();
            Vector3 lean = new Vector3(0f, 0f, -Mathf.Sin(s.Lean));
            for (int i = 0; i < s.Count; i++)
            {
                float a = (i + 0.5f) / s.Count * Mathf.PI * 2f + Rand(-0.4f, 0.4f);
                float out_ = s.Spread * Rand(0.45f, 1f);
                if (s.Count > 4 && i == 0) out_ *= 0.2f;   // one stands up in the middle
                float h = Rand(s.HeightMin, s.HeightMax);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 start = new Vector3(dir.x * 0.006f, mouth - 0.05f, dir.z * 0.006f);
                Vector3 mid = new Vector3(dir.x * out_ * 0.35f, mouth + h * 0.5f, dir.z * out_ * 0.35f) + lean * (h * 0.4f);
                Vector3 top = new Vector3(dir.x * out_, mouth + h, dir.z * out_) + lean * h;
                if (s.Kind == FlowerKind.Geranium) start.y = mouth - 0.01f;
                var curve = new CatmullRom3(start, mid, top);
                b.Stems.Append(PrimitiveMeshes.Tube(curve, 8, s.Kind == FlowerKind.Tulip ? 0.0028f : 0.0018f, 5));
                Vector3 tangent = curve.GetTangent(1f).normalized;
                // Daisies and cosmos turn their faces outward a little more than their stems lean.
                if (s.Kind == FlowerKind.Daisy || s.Kind == FlowerKind.Cosmos) tangent = (tangent + dir * 0.45f + lean).normalized;
                Quaternion q = Quaternion.FromToRotation(Vector3.up, tangent) * Rh.AxisAngle(Vector3.up, Rand(0f, 6.28f));
                int col = s.Colours[i % s.Colours.Length];
                float size = s.Size * Rand(0.88f, 1.12f);
                Matrix4x4 head = Matrix4x4.TRS(top, q, Vector3.one * size);
                float tipY;
                switch (s.Kind)
                {
                    case FlowerKind.Tulip: tipY = Tulip(b.Petals, head, col); break;
                    case FlowerKind.Daisy: tipY = Daisy(b.Petals, head, col, 16, 0.024f, 0.0034f); break;
                    case FlowerKind.Cosmos: tipY = Cosmos(b.Petals, head, col); break;
                    default: tipY = Geranium(b.Petals, head, col, ref rng); break;
                }
                b.Heads.Add(head.MultiplyPoint3x4(new Vector3(0f, tipY, 0f)));
                b.FlowerCount++;
                // a leaf or two up the stem for the small flowers
                if (s.Kind == FlowerKind.Daisy || s.Kind == FlowerKind.Cosmos)
                {
                    Vector3 p = curve.GetPoint(0.45f);
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                    Leaf(b.Leaves, blade, p, (dir * 0.7f + Vector3.up + side * Rand(-0.5f, 0.5f)).normalized, 0.012f, 0.03f);
                }
            }
            // Leaves from the mouth: long straps (tulips), small (daisies, cosmos), round at the soil (geraniums).
            for (int i = 0; i < s.Leaves; i++)
            {
                float a = (i + Rand(0f, 0.8f)) / s.Leaves * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                switch (s.Kind)
                {
                    case FlowerKind.Tulip:
                        Leaf(b.Leaves, blade, new Vector3(dir.x * 0.012f, mouth - 0.01f, dir.z * 0.012f), (dir * 0.45f + Vector3.up).normalized, 0.028f, 0.11f);
                        break;
                    case FlowerKind.Geranium:
                        // broad round leaves spreading out over the pot's rim on short stalks
                        Vector3 at = new Vector3(dir.x * Rand(0.025f, 0.045f), mouth + Rand(0.005f, 0.03f), dir.z * Rand(0.025f, 0.045f));
                        b.Stems.Append(PrimitiveMeshes.Tube(new CatmullRom3(new Vector3(dir.x * 0.005f, mouth - 0.005f, dir.z * 0.005f), at), 3, 0.0014f, 4));
                        Leaf(b.Leaves, blade, at, (dir * 1.6f + Vector3.up * Rand(0.1f, 0.4f)).normalized, 0.062f, 0.056f, spread: true);
                        break;
                    default:
                        Leaf(b.Leaves, blade, new Vector3(dir.x * 0.008f, mouth - 0.005f, dir.z * 0.008f), (dir * 0.8f + Vector3.up).normalized, 0.014f, 0.05f);
                        break;
                }
            }
            b.Phase = (float)rng.Next() * 6.28f;
            b.Amp = 0.014f + (float)rng.Next() * 0.008f;
            return b;
        }

        /// <summary>A leaf blade (PlantBuilder.LeafBlade, 1 x 1.8 base at the origin) from <paramref name="at"/> along
        /// <paramref name="dir"/>, its face turned outward, w wide and len long.</summary>
        static void Leaf(Geo into, Geo blade, Vector3 at, Vector3 dir, float w, float len, bool spread = false)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            Vector3 face = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            // spread: a leaf held out flat faces the sky (its face normal is "up" made square to the blade)
            if (spread) { Vector3 n = Vector3.up - dir * Vector3.Dot(Vector3.up, dir); if (n.sqrMagnitude > 1e-4f) face = n.normalized; }
            Quaternion q = Mathf.Abs(Vector3.Dot(face, dir)) > 0.995f ? Quaternion.LookRotation(Vector3.up, dir) : Quaternion.LookRotation(face, dir);
            Geo g = blade.Clone().RemapUv(Textures.RoomTextures.LeafAtlasScale, Textures.RoomTextures.LeafAtlasOffset(false));
            into.Append(g, Matrix4x4.TRS(at, q, new Vector3(w, len / 1.8f, w)));
        }

        // ------------------------------------------------------------------ vases

        /// <summary>The vase into b.Vase (and the pot's soil into b.Soil); returns the mouth's height.</summary>
        static float BuildVase(Bouquet b, VaseKind kind)
        {
            switch (kind)
            {
                case VaseKind.Ceramic:
                    b.Vase.Append(Lathe(new[]
                    {
                        new Vector2(0f, 0f), new Vector2(0.034f, 0f), new Vector2(0.042f, 0.012f), new Vector2(0.052f, 0.05f), new Vector2(0.054f, 0.085f),
                        new Vector2(0.046f, 0.13f), new Vector2(0.032f, 0.168f), new Vector2(0.026f, 0.19f), new Vector2(0.031f, 0.205f),
                        new Vector2(0.027f, 0.207f), new Vector2(0.022f, 0.19f), new Vector2(0.02f, 0.15f),
                    }, 28));
                    return 0.205f;
                case VaseKind.BudBrass:
                    b.Vase.Append(Lathe(new[]
                    {
                        new Vector2(0f, 0f), new Vector2(0.028f, 0f), new Vector2(0.03f, 0.006f), new Vector2(0.036f, 0.035f), new Vector2(0.03f, 0.07f),
                        new Vector2(0.013f, 0.1f), new Vector2(0.011f, 0.115f), new Vector2(0.016f, 0.125f), new Vector2(0.012f, 0.126f), new Vector2(0.009f, 0.11f),
                    }, 24));
                    return 0.125f;
                case VaseKind.StoneJar:
                    b.Vase.Append(Lathe(new[]
                    {
                        new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.042f, 0.015f), new Vector2(0.05f, 0.05f), new Vector2(0.045f, 0.09f),
                        new Vector2(0.03f, 0.115f), new Vector2(0.024f, 0.125f), new Vector2(0.026f, 0.135f), new Vector2(0.022f, 0.136f), new Vector2(0.019f, 0.12f),
                    }, 24));
                    return 0.135f;
                default:
                {
                    // The plants' terracotta pot (PlantBuilder), smaller: a tapered cylinder, a rolled rim and soil.
                    const float pot = 0.065f;
                    b.Vase.Append(PrimitiveMeshes.Cylinder(pot, pot * 0.72f, pot * 1.5f, 24, 1), Rh.T(0f, pot * 0.75f, 0f));
                    b.Vase.Append(PrimitiveMeshes.Torus(pot, pot * 0.07f, 8, 32), Rh.TRS(new Vector3(0f, pot * 1.5f, 0f), Rh.Euler(Mathf.PI / 2f, 0f, 0f)));
                    float bas = pot * 1.5f - 0.01f;
                    b.Soil.Append(PrimitiveMeshes.Circle(pot * 0.94f, 24), Rh.TRS(new Vector3(0f, bas, 0f), Rh.Euler(-Mathf.PI / 2f, 0f, 0f)));
                    return bas;
                }
            }
        }

        /// <summary>A surface of revolution about +y from a (radius, height) profile, outside then over the lip and in.</summary>
        public static Geo Lathe(Vector2[] profile, int segments)
        {
            var g = new Geo();
            int rows = profile.Length;
            for (int i = 0; i < rows; i++)
                for (int k = 0; k <= segments; k++)
                {
                    float a = (float)k / segments * Mathf.PI * 2f;
                    g.P.Add(new Vector3(profile[i].x * Mathf.Cos(a), profile[i].y, profile[i].x * Mathf.Sin(a)));
                    g.N.Add(Vector3.up);
                    g.UV.Add(new Vector2((float)k / segments, (float)i / (rows - 1)));
                }
            int row = segments + 1;
            for (int i = 0; i < rows - 1; i++)
                for (int k = 0; k < segments; k++)
                {
                    int a = i * row + k, b = a + 1, c = a + row, d = c + 1;
                    g.I.Add(a); g.I.Add(c); g.I.Add(b);
                    g.I.Add(b); g.I.Add(c); g.I.Add(d);
                }
            g.ComputeVertexNormals();
            return g;
        }

        // ------------------------------------------------------------------ heads (head frame: +y out of the flower)

        /// <summary>A grid patch of (cols + 1) x (rows + 1) vertices from a point function, normals computed; palette UV by v.</summary>
        static void Patch(Geo into, Matrix4x4 m, int column, int cols, int rows, System.Func<float, float, Vector3> at)
        {
            var g = new Geo();
            for (int j = 0; j <= rows; j++)
                for (int i = 0; i <= cols; i++)
                {
                    float u = -1f + 2f * i / cols, v = (float)j / rows;
                    g.P.Add(at(u, v));
                    g.N.Add(Vector3.up);
                    g.UV.Add(PetalPalette.Uv(column, v));
                }
            int r = cols + 1;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = j * r + i, b = a + 1, c = a + r, d = c + 1;
                    g.I.Add(a); g.I.Add(c); g.I.Add(b);
                    g.I.Add(b); g.I.Add(c); g.I.Add(d);
                }
            g.ComputeVertexNormals();
            into.Append(g, m);
        }

        /// <summary>A tulip: three outer and three inner cupped petals, the cup ~5.5 cm tall. Returns the rim's height.</summary>
        static float Tulip(Geo petals, Matrix4x4 head, int col)
        {
            const float H = 0.055f;
            for (int ring = 0; ring < 2; ring++)
                for (int p = 0; p < 3; p++)
                {
                    float centre = p * Mathf.PI * 2f / 3f + ring * Mathf.PI / 3f;
                    float scale = ring == 0 ? 1f : 0.9f, height = ring == 0 ? H : H * 0.96f;
                    Patch(petals, head, col, 4, 7, (u, v) =>
                    {
                        float r = (0.005f + 0.019f * Mathf.Pow(Mathf.Sin(Mathf.PI * v * 0.85f), 0.8f)) * scale + 0.0018f * (1f - u * u);
                        float half = 0.64f * (0.55f + 0.45f * Mathf.Sin(Mathf.PI * Mathf.Min(v, 0.85f))) * (1f - 0.55f * v * v * v * v);
                        float a = centre + u * half;
                        float y = height * v + (ring == 0 ? 0.004f * v * v * (1f - Mathf.Abs(u)) : 0f);
                        return new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
                    });
                }
            return H;
        }

        /// <summary>A daisy: n thin petals around a domed centre. Returns the centre's height.</summary>
        static float Daisy(Geo petals, Matrix4x4 head, int col, int n, float length, float width)
        {
            for (int p = 0; p < n; p++)
            {
                float a = p * Mathf.PI * 2f / n + (p % 2) * 0.06f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                float len = length * (p % 3 == 0 ? 0.92f : 1f);
                Patch(petals, head, col, 2, 4, (u, v) =>
                {
                    float r = 0.006f + len * v;
                    float w = width * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (0.12f + 0.88f * v))));
                    float y = 0.003f * v - 0.007f * v * v + 0.0007f * (1f - u * u);
                    return radial * r + side * (u * w) + Vector3.up * y;
                });
            }
            Centre(petals, head, 0.0068f, 0.0035f);
            return 0.0035f;
        }

        /// <summary>A cosmos: eight broad petals with notched tips around a small centre. Returns the centre's height.</summary>
        static float Cosmos(Geo petals, Matrix4x4 head, int col)
        {
            const int n = 8;
            for (int p = 0; p < n; p++)
            {
                float a = p * Mathf.PI * 2f / n;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Patch(petals, head, col, 4, 4, (u, v) =>
                {
                    float notch = v >= 1f ? 0.0035f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 1.5f)) : 0f;
                    float r = 0.005f + 0.03f * v - notch;
                    float w = 0.0105f * Mathf.Sin(Mathf.PI * (0.12f + 0.55f * v));
                    float y = 0.002f * v - 0.004f * v * v + 0.0012f * (1f - u * u) * v;
                    return radial * r + side * (u * w) + Vector3.up * y;
                });
            }
            Centre(petals, head, 0.0062f, 0.003f);
            return 0.003f;
        }

        /// <summary>A geranium umbel: florets of five round petals on a dome. Returns the dome's top.</summary>
        static float Geranium(Geo petals, Matrix4x4 head, int col, ref Mulberry32 rng)
        {
            const float R = 0.022f;
            int florets = 0;
            for (int ring = 0; ring < 3; ring++)
            {
                int n = ring == 0 ? 1 : ring == 1 ? 5 : 8;
                float tilt = ring * 0.55f;
                for (int k = 0; k < n; k++)
                {
                    float az = k * Mathf.PI * 2f / n + ring * 0.3f + (float)rng.Next() * 0.2f;
                    Vector3 nrm = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(az));
                    Vector3 at = nrm * R + Vector3.down * (R * 0.65f);
                    Matrix4x4 m = head * Matrix4x4.TRS(at, Quaternion.FromToRotation(Vector3.up, nrm) * Rh.AxisAngle(Vector3.up, (float)rng.Next() * 6.28f), Vector3.one);
                    Floret(petals, m, col);
                    florets++;
                }
            }
            return R * 0.35f + 0.002f;
        }

        static void Floret(Geo petals, Matrix4x4 m, int col)
        {
            for (int p = 0; p < 5; p++)
            {
                float a = p * Mathf.PI * 2f / 5f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Patch(petals, m, col, 2, 2, (u, v) =>
                {
                    float r = 0.0012f + 0.0085f * v;
                    float w = 0.0052f * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (0.2f + 0.8f * v * 0.85f))));
                    return radial * r + side * (u * w) + Vector3.up * (0.0012f * v);
                });
            }
        }

        /// <summary>A domed flower centre (palette Centre, darker in the middle).</summary>
        static void Centre(Geo petals, Matrix4x4 head, float radius, float height)
        {
            var g = new Geo();
            const int seg = 14, rings = 3;
            // one palette column, dark in the middle to golden at the rim (no blend through other columns)
            g.P.Add(new Vector3(0f, height, 0f)); g.N.Add(Vector3.up); g.UV.Add(PetalPalette.Uv(PetalPalette.Centre, 0f));
            for (int r = 1; r <= rings; r++)
            {
                float t = (float)r / rings;
                for (int k = 0; k < seg; k++)
                {
                    float a = k * Mathf.PI * 2f / seg;
                    g.P.Add(new Vector3(radius * t * Mathf.Cos(a), height * (1f - t * t) + 0.0008f, radius * t * Mathf.Sin(a)));
                    g.N.Add(Vector3.up);
                    g.UV.Add(PetalPalette.Uv(PetalPalette.Centre, Mathf.Sqrt(t)));
                }
            }
            for (int k = 0; k < seg; k++) { g.I.Add(0); g.I.Add(1 + (k + 1) % seg); g.I.Add(1 + k); }
            for (int r = 1; r < rings; r++)
            {
                int a0 = 1 + (r - 1) * seg, b0 = 1 + r * seg;
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    g.I.Add(a0 + k); g.I.Add(a0 + k1); g.I.Add(b0 + k);
                    g.I.Add(a0 + k1); g.I.Add(b0 + k1); g.I.Add(b0 + k);
                }
            }
            g.ComputeVertexNormals();
            petals.Append(g, head);
        }
    }
}
