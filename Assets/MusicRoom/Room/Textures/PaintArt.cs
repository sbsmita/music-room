// The wall paint's brush atlas (Room/Decor/WallPaint.cs, the gallery's pictures): 512 x 512, a 4 x 4 grid of 128 px cells
// of watercolour nature motifs, made in code (no file, no bake): Build() computes the pixels on any thread (a worker at
// boot), Make() turns them into the mipmapped texture on the main thread. Per pixel: R = pigment density (the darker rim a
// watercolour dries to, the paper's grain, veins, bark, the shade under a cloud), A = coverage (a soft ragged edge). Linear
// data, not a colour: each quad brings its own pigment, so a motif of several colours is several quads (a flower's petals
// and its golden heart, a tree's trunk and its crown).
// The motifs (round 4, 2026-11-16: "make paintings of flowers birds nature scenery"; Decor/PaintBrushes.cs paints them):
//   0 Blot      a round blot with a wandering edge (pebbles)     8 Butterfly four wings, a body, antennae
//   1 Trunk     a tree trunk with its boughs, foot at the bottom  9 Band     a soft wash band, tiles along u (a bird's path)
//   2 Leaf      a pointed leaf, stalk at the bottom, veins        10 Cloud    a soft heaped cloud, shaded underneath
//   3 Blossom   a five-petal flower head                          11 Petal    a teardrop petal from a base
//   4 Ribbon    a stem's body, tiles along u, wet edges           12 Crown    a leafy tree crown, shade and light in it
//   5 Grass     a tuft of blades from a foot at the bottom        13 Bead     a round bead, dense rim, a light spot
//   6 Droplet   a small round drop                                14 Bud      a tulip cup of three petals
//   7 Bird      a bird in flight, wings up (seen from the front)  15 Cap      a mushroom cap with pale spots
// Up is +y of a cell (a quad's D/C edge). The non-tiling marks stay inside 0.92 of a cell's half width so mip levels never
// bleed into a neighbour; the two tiling cells (Ribbon, Band) repeat over pixel columns 1..127 of their cell (TileU maps
// a stroke's u into that range).
using UnityEngine;

namespace MusicRoom.Room.Textures
{
    public static class PaintArt
    {
        public const int Size = 512, CellPx = 128, Grid = 4, Cells = 16;
        public const int Blot = 0, Trunk = 1, Leaf = 2, Blossom = 3, Ribbon = 4, Grass = 5, Droplet = 6, Bird = 7,
            Butterfly = 8, Band = 9, Cloud = 10, Petal = 11, Crown = 12, Bead = 13, Bud = 14, Cap = 15;
        /// <summary>The tiling cells repeat over this many pixel columns (1 .. 127 of the cell).</summary>
        public const int TilePx = CellPx - 2;

        /// <summary>A cell that repeats along u (a stroke's body).</summary>
        public static bool Tiles(int cell) => cell == Ribbon || cell == Band;

        /// <summary>The atlas cell's UV offset (4 x 4 cells, a quarter each).</summary>
        public static Vector2 CellOffset(int cell) => new Vector2((cell % Grid) * 0.25f, (cell / Grid % Grid) * 0.25f);

        /// <summary>A tiling cell's u (any real number, one repeat per 1) as a u inside the cell (0..1).</summary>
        public static float TileU(float u)
        {
            float f = u - Mathf.Floor(u);
            return (f * TilePx + 1f) / CellPx;
        }

        /// <summary>The atlas pixels (Size x Size, row 0 at the bottom): R = density, A = coverage. Pure maths: any thread.</summary>
        public static Color32[] Build()
        {
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Pixel(x, y, out float density, out float alpha);
                    byte d = (byte)Mathf.RoundToInt(Mathf.Clamp01(density) * 255f);
                    px[y * Size + x] = new Color32(d, d, d, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            return px;
        }

        /// <summary>The atlas texture (main thread).</summary>
        public static Texture2D Make() => Make(Build());

        public static Texture2D Make(Color32[] px)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "Room/paint_brushes",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>One atlas pixel: pigment density and coverage (0..1).</summary>
        public static void Pixel(int x, int y, out float density, out float alpha)
        {
            int cx = x / CellPx, cy = y / CellPx, cell = cx + Grid * cy;
            int ix = x % CellPx, iy = y % CellPx;
            // local coordinates: -1..1 across the cell
            float lx = (ix + 0.5f) / CellPx * 2f - 1f, ly = (iy + 0.5f) / CellPx * 2f - 1f;
            float cover, rim;
            if (Tiles(cell))
            {
                float phase = ((ix - 1) + 0.5f) / TilePx;   // one period over columns 1..127
                phase -= Mathf.Floor(phase);
                if (cell == Ribbon) RibbonMark(phase, ly, out cover, out rim);
                else BandMark(phase, ly, out cover, out rim);
                float g = FbmP(phase * 24f, ly * 3f + 9f, 3, 77 + cell, 24);
                density = Mathf.Clamp01(0.5f + 0.4f * rim + 0.26f * (g - 0.5f));
                alpha = Mathf.Clamp01(cover * (0.84f + 0.28f * (g - 0.5f)));
                alpha *= 1f - Smooth(0.9f, 0.97f, Mathf.Abs(ly));
                return;
            }
            switch (cell)
            {
                case Blot: BlotMark(lx, ly, 11, 0.74f, 0.12f, out cover, out rim); break;
                case Trunk: TrunkMark(lx, ly, out cover, out rim); break;
                case Leaf: LeafMark(lx, ly, out cover, out rim); break;
                case Blossom: BlossomMark(lx, ly, out cover, out rim); break;
                case Grass: GrassMark(lx, ly, out cover, out rim); break;
                case Droplet: BlotMark(lx, ly, 71, 0.62f, 0.08f, out cover, out rim); break;
                case Bird: BirdMark(lx, ly, out cover, out rim); break;
                case Butterfly: ButterflyMark(lx, ly, out cover, out rim); break;
                case Cloud: CloudMark(lx, ly, out cover, out rim); break;
                case Petal: PetalMark(lx, ly, out cover, out rim); break;
                case Crown: CrownMark(lx, ly, out cover, out rim); break;
                case Bead: BeadMark(lx, ly, out cover, out rim); break;
                case Bud: BudMark(lx, ly, out cover, out rim); break;
                default: CapMark(lx, ly, out cover, out rim); break;
            }
            // the paper's grain: pigment settles unevenly
            float grain = Fbm(x * 0.09f + 3.1f * cell, y * 0.09f, 3, 77);
            density = Mathf.Clamp01(0.52f + 0.38f * rim + 0.28f * (grain - 0.5f));
            alpha = Mathf.Clamp01(cover * (0.93f + 0.2f * (grain - 0.5f)));
            // nothing near the cell's border (the mip chain)
            float edge = Mathf.Max(Mathf.Abs(lx), Mathf.Abs(ly));
            alpha *= 1f - Smooth(0.9f, 0.97f, edge);
        }

        // ---------------------------------------------------------------- the motifs

        /// <summary>A watercolour blot: radius R(theta) wandering round base by amp, a soft edge and the darker dried rim.</summary>
        static void BlotMark(float x, float y, int seed, float baseR, float amp, out float cover, out float rim)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float th = Mathf.Atan2(y, x);
            float R = baseR + amp * (Fbm(Mathf.Cos(th) * 1.6f + 5f, Mathf.Sin(th) * 1.6f + 5f, 3, seed) - 0.5f) * 2f
                      + 0.035f * Mathf.Sin(3f * th + seed) + 0.025f * Mathf.Sin(5f * th + 2f * seed);
            float rag = 0.04f * (Fbm(x * 9f + seed, y * 9f, 2, seed + 1) - 0.5f);
            float d = R + rag - r;
            cover = Smooth(-0.015f, 0.05f, d);
            rim = Smooth(0.22f, 0.02f, d) * cover;
        }

        /// <summary>A trunk from its foot (the cell's bottom) to a fork, leaning a little and tapering, a flare at the roots,
        /// three boughs and a twig up from the fork (the crown is laid over them); bark streaks run up it.</summary>
        static void TrunkMark(float x, float y, out float cover, out float rim)
        {
            float d = -1f;
            if (y > -0.9f && y < 0.4f)
            {
                float t = (y + 0.88f) / 1.2f;
                float cx = 0.035f * Mathf.Sin(y * 2.4f + 0.6f);
                float hw = Mathf.Lerp(0.13f, 0.07f, Mathf.Clamp01(t)) + 0.07f * Smooth(0.18f, 0f, t);
                d = Mathf.Min(hw - Mathf.Abs(x - cx), (y + 0.88f) * 0.8f);
            }
            d = Mathf.Max(d, Capsule(x, y, 0.03f, 0.18f, -0.44f, 0.7f, 0.065f, 0.02f));
            d = Mathf.Max(d, Capsule(x, y, 0.02f, 0.12f, 0.4f, 0.76f, 0.06f, 0.02f));
            d = Mathf.Max(d, Capsule(x, y, 0.03f, 0.3f, 0.06f, 0.86f, 0.05f, 0.016f));
            d = Mathf.Max(d, Capsule(x, y, -0.22f, 0.42f, -0.52f, 0.46f, 0.028f, 0.012f));
            d += 0.012f * (Fbm(x * 12f, y * 12f, 2, 171) - 0.5f);
            cover = Smooth(-0.012f, 0.03f, d);
            float streak = Fbm(x * 20f, y * 2.5f, 2, 172);
            rim = Mathf.Clamp01(0.42f + 0.5f * Smooth(0.05f, 0f, d) + 0.6f * (streak - 0.5f) + 0.2f * Smooth(0.3f, -0.6f, y)) * cover;
        }

        /// <summary>A pointed leaf: its stalk at the cell's bottom, its tip near the top, curving a little; a lighter midrib
        /// and side veins, a darker wet edge.</summary>
        static void LeafMark(float x, float y, out float cover, out float rim)
        {
            float t = (y + 0.7f) / 1.56f;
            float tc = Mathf.Clamp01(t);
            float cx = 0.12f * tc * tc - 0.03f;
            float w = 0.4f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(tc, 0.8f))), 0.85f);
            float d = Mathf.Min(w - Mathf.Abs(x - cx), Mathf.Min(t, 1f - t) * 0.6f);
            d = Mathf.Max(d, Capsule(x, y, 0f, -0.9f, -0.03f, -0.62f, 0.028f, 0.024f));
            d += 0.012f * (Fbm(x * 10f, y * 10f, 2, 181) - 0.5f);
            cover = Smooth(-0.012f, 0.035f, d);
            float ax = Mathf.Abs(x - cx);
            float mid = Smooth(0.025f, 0f, ax) * Smooth(0f, 0.08f, t) * Smooth(1f, 0.82f, t);
            float s = (y - 0.9f * ax) * 6.5f;
            float vein = Smooth(0.12f, 0f, Mathf.Abs(s - Mathf.Round(s))) * Smooth(0.03f, 0.1f, ax) * Smooth(0.02f, 0.08f, d);
            float g = Fbm(x * 5f, y * 5f, 2, 182);
            rim = Mathf.Clamp01(0.42f + 0.6f * Smooth(0.08f, 0f, d) + 0.3f * (g - 0.5f) - 0.6f * mid - 0.35f * vein) * cover;
        }

        /// <summary>A five-petal flower head, a petal pointing up: rounded petals with a small notch at each tip, deeper toward
        /// the heart and at the edges, faint streaks running out along each petal, a darker line between petals.</summary>
        static void BlossomMark(float x, float y, out float cover, out float rim)
        {
            float r = Mathf.Sqrt(x * x + y * y), th = Mathf.Atan2(y, x);
            float lobe = Mathf.Abs(Mathf.Cos(2.5f * (th - 1.5708f)));
            float R = 0.3f + 0.56f * Mathf.Pow(lobe, 0.55f) - 0.06f * Smooth(0.975f, 1f, lobe);
            R += 0.025f * (Fbm(Mathf.Cos(th) * 2f + 3f, Mathf.Sin(th) * 2f, 2, 191) - 0.5f) * 2f;
            float d = R - r;
            cover = Smooth(-0.012f, 0.035f, d);
            float heart = Smooth(0.4f, 0.08f, r);
            float ridge = 0.5f + 0.5f * Mathf.Sin(th * 30f + 4f * Fbm(x * 3f, y * 3f, 2, 192));
            float valley = Smooth(0.3f, 0.0f, lobe) * Smooth(0.12f, 0.3f, r);
            rim = Mathf.Clamp01(0.22f + 0.5f * heart + 0.55f * Smooth(0.1f, 0f, d) + 0.18f * (ridge - 0.5f) * Smooth(0.2f, 0.5f, r) + 0.5f * valley) * cover;
        }

        /// <summary>A tuft of grass: nine blades from a foot at the cell's bottom, curving out, the outer ones leaning further;
        /// darker at the foot, lighter at the tips.</summary>
        static void GrassMark(float x, float y, out float cover, out float rim)
        {
            float d = -1f;
            for (int k = 0; k < 9; k++)
            {
                float f = (k - 4f) / 4f;   // -1 .. 1
                float bx = 0.2f * f + 0.05f * (Hash01(k, 201) - 0.5f);
                float tx = bx + 0.5f * f + 0.12f * (Hash01(k, 202) - 0.5f);
                float ty = 0.82f - 0.55f * Mathf.Abs(f) - 0.2f * Hash01(k, 203);
                float mx = bx + 0.08f * f, my = Mathf.Lerp(-0.88f, ty, 0.6f) + 0.1f;
                float w0 = 0.06f + 0.015f * Hash01(k, 204);
                // the blade as a quadratic curve in 8 pieces, tapering to its tip
                float ax = bx, ay = -0.88f;
                for (int s = 1; s <= 8; s++)
                {
                    float t = s / 8f, u = 1f - t;
                    float px = u * u * bx + 2f * u * t * mx + t * t * tx, py = u * u * -0.88f + 2f * u * t * my + t * t * ty;
                    d = Mathf.Max(d, Capsule(x, y, ax, ay, px, py, w0 * (1f - (s - 1) / 8f) + 0.004f, w0 * (1f - t) + 0.004f));
                    ax = px; ay = py;
                }
            }
            d += 0.008f * (Fbm(x * 12f, y * 12f, 2, 205) - 0.5f);
            cover = Smooth(-0.01f, 0.025f, d);
            rim = Mathf.Clamp01(0.3f + 0.45f * Smooth(0.2f, -0.9f, y) + 0.4f * Smooth(0.03f, 0f, d)) * cover;
        }

        /// <summary>A bird in flight seen from the front: wings raised from a small body to the wrists and down to the
        /// tips, tapering; darker toward the wingtips.</summary>
        static void BirdMark(float x, float y, out float cover, out float rim)
        {
            float ax = Mathf.Abs(x);
            float d = Ellipse(x, y + 0.05f, 0.075f, 0.12f);
            // body to wrist, wrist to tip (quadratic curves), each in 6 pieces
            d = Mathf.Max(d, Curve(ax, y, 0.02f, -0.02f, 0.16f, 0.26f, 0.38f, 0.24f, 0.085f, 0.06f));
            d = Mathf.Max(d, Curve(ax, y, 0.38f, 0.24f, 0.62f, 0.24f, 0.86f, -0.06f, 0.06f, 0.012f));
            d = Mathf.Max(d, Capsule(x, y, 0f, -0.12f, 0f, -0.26f, 0.05f, 0.03f));   // the tail
            d += 0.006f * (Fbm(x * 14f, y * 14f, 2, 211) - 0.5f);
            cover = Smooth(-0.008f, 0.022f, d);
            rim = Mathf.Clamp01(0.4f + 0.5f * Smooth(0.35f, 0.85f, ax) + 0.3f * Smooth(0.025f, 0f, d)) * cover;
        }

        /// <summary>A butterfly from above: two rounded upper wings, two smaller lower ones, a slim body and two antennae with
        /// knobs; a pale eye spot on each upper wing, darker wing edges.</summary>
        static void ButterflyMark(float x, float y, out float cover, out float rim)
        {
            float ax = Mathf.Abs(x);
            float upper = RotEllipse(ax - 0.37f, y - 0.2f, 0.5f, 0.33f, 0.27f);
            float lower = RotEllipse(ax - 0.25f, y + 0.3f, -0.45f, 0.22f, 0.19f);
            float wings = Mathf.Max(upper, lower);
            wings += 0.012f * (Fbm(x * 9f, y * 9f, 2, 221) - 0.5f);
            float body = Ellipse(x, y + 0.02f, 0.05f, 0.34f);
            float ant = Mathf.Max(Capsule(ax, y, 0.02f, 0.28f, 0.17f, 0.66f, 0.014f, 0.012f), 0.04f - Mathf.Sqrt((ax - 0.17f) * (ax - 0.17f) + (y - 0.67f) * (y - 0.67f)));
            float d = Mathf.Max(Mathf.Max(wings, body), ant);
            cover = Smooth(-0.01f, 0.03f, d);
            float sx = ax - 0.43f, sy = y - 0.28f;
            float spot = Smooth(0.1f, 0.05f, Mathf.Sqrt(sx * sx + sy * sy));
            float vein = Smooth(0.02f, 0f, Mathf.Abs(Mathf.Sin(Mathf.Atan2(y - 0.02f, ax) * 7f))) * Smooth(0.08f, 0.3f, ax) * 0.4f;
            rim = Mathf.Clamp01(0.32f + 0.6f * Smooth(0.07f, 0f, wings) + 0.6f * Smooth(0f, 0.02f, body) * (1f - Smooth(0f, 0.02f, wings)) - 0.55f * spot + vein) * cover;
            if (body > 0f) rim = Mathf.Max(rim, 0.85f * cover);
        }

        /// <summary>A soft heaped cloud with a flatter base: overlapping puffs, a feathered edge, shaded underneath.</summary>
        static void CloudMark(float x, float y, out float cover, out float rim)
        {
            float d = -1f;
            d = Mathf.Max(d, 0.24f - Dist(x + 0.5f, y + 0.16f));
            d = Mathf.Max(d, 0.33f - Dist(x + 0.2f, y - 0.04f));
            d = Mathf.Max(d, 0.37f - Dist(x - 0.16f, y - 0.1f));
            d = Mathf.Max(d, 0.27f - Dist(x - 0.5f, y + 0.1f));
            d = Mathf.Max(d, 0.29f - Dist(x - 0.02f, y + 0.2f));
            d = Mathf.Max(d, 0.17f - Dist(x - 0.68f, y + 0.24f));
            d = Mathf.Min(d, (y + 0.4f) * 0.7f);
            d += 0.05f * (Fbm(x * 4f, y * 4f, 3, 231) - 0.5f);
            cover = Smooth(-0.07f, 0.12f, d) * 0.92f;
            rim = Mathf.Clamp01(0.12f + 0.62f * Smooth(0.15f, -0.38f, y) + 0.3f * Smooth(0.06f, -0.02f, d)) * cover;
        }

        static void PetalMark(float x, float y, out float cover, out float rim)
        {
            float t = (y + 0.86f) / 1.72f;                              // 0 the base .. 1 the tip
            float tc = Mathf.Clamp01(t);
            float w = 0.4f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * tc)), 0.85f) * (1f - 0.25f * tc);
            float cx = 0.06f * Mathf.Sin(tc * 3f);
            float d = Mathf.Min(w - Mathf.Abs(x - cx), Mathf.Min(t, 1f - t) * 0.5f);
            d += 0.02f * (Fbm(x * 8f, y * 8f, 2, 121) - 0.5f);
            cover = Smooth(-0.015f, 0.05f, d);
            float vein = Smooth(0.03f, 0.0f, Mathf.Abs(x - cx)) * Smooth(0.1f, 0.6f, tc);
            rim = Mathf.Clamp01(Smooth(0.12f, 0.01f, d) + 0.3f * (1f - tc) - 0.35f * vein) * cover;
        }

        /// <summary>A leafy crown: clumps round a middle, a bumpy edge, shade low on the right and light high on the left.</summary>
        static readonly float[] CrownClumps =
        {
            0f, 0.06f, 0.4f,   -0.36f, -0.12f, 0.3f,   0.38f, -0.08f, 0.3f,   -0.2f, 0.36f, 0.3f,   0.2f, 0.4f, 0.28f,
            0.02f, 0.6f, 0.22f,   -0.47f, 0.2f, 0.21f,   0.48f, 0.24f, 0.2f,   -0.1f, -0.36f, 0.27f,   0.22f, -0.34f, 0.25f,
            -0.57f, -0.2f, 0.15f,   0.6f, -0.16f, 0.15f,
        };

        static void CrownMark(float x, float y, out float cover, out float rim)
        {
            // clumps of leaves: each a little ball, shaded low on its right, a darker seam where one lies over another
            float wob = 0.06f * (Fbm(x * 5f, y * 5f, 2, 241) - 0.5f) + 0.05f * (Fbm(x * 15f, y * 15f, 2, 242) - 0.5f);
            float d = -1f, seam = 0f, ball = 0f;
            for (int k = 0; k < CrownClumps.Length; k += 3)
            {
                float cx = CrownClumps[k], cy = CrownClumps[k + 1], r = CrownClumps[k + 2];
                float di = r - Dist(x - cx, y - cy) + wob;
                if (di > d)
                {
                    if (d > 0f) seam = Mathf.Max(seam, Smooth(0.045f, 0f, di - d));
                    d = di;
                    ball = Smooth(-0.7f, 0.9f, ((x - cx) * 0.5f - (y - cy) * 0.85f) / r);
                }
                else if (di > -0.02f) seam = Mathf.Max(seam, Smooth(0.035f, 0f, Mathf.Abs(di)) * Smooth(0f, 0.04f, d));
            }
            cover = Smooth(-0.012f, 0.03f, d);
            float clumps = Fbm(x * 6f + 2f, y * 6f, 3, 243);
            float shade = Smooth(-0.6f, 0.7f, x * 0.4f - y * 0.7f);
            rim = Mathf.Clamp01(0.1f + 0.35f * shade + 0.35f * ball + 0.4f * (clumps - 0.5f) + 0.4f * seam + 0.3f * Smooth(0.05f, 0f, d)) * cover;
        }

        static void BeadMark(float x, float y, out float cover, out float rim)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float d = 0.62f + 0.03f * (Fbm(x * 5f, y * 5f, 2, 141) - 0.5f) - r;
            cover = Smooth(-0.015f, 0.05f, d);
            float hx = x + 0.2f, hy = y - 0.22f;
            float light = Smooth(0.22f, 0.0f, Mathf.Sqrt(hx * hx + hy * hy));
            rim = Mathf.Clamp01(Smooth(0.2f, 0.02f, d) + 0.25f - 0.6f * light) * cover;
        }

        /// <summary>A tulip cup: a middle petal and two side petals leaning out, their pointed tips up, a darker line where
        /// they overlap.</summary>
        static void BudMark(float x, float y, out float cover, out float rim)
        {
            float mid = Ellipse(x, y - 0.02f, 0.25f, 0.56f);
            float left = RotEllipse(x + 0.2f, y + 0.06f, 0.32f, 0.23f, 0.5f);
            float right = RotEllipse(x - 0.2f, y + 0.06f, -0.32f, 0.23f, 0.5f);
            float cup = 0.34f - Dist(x, y + 0.2f);
            float side = Mathf.Max(left, right);
            float d = Mathf.Max(Mathf.Max(mid, side), Mathf.Min(cup, 0.1f - y));
            d += 0.012f * (Fbm(x * 9f, y * 9f, 2, 251) - 0.5f);
            cover = Smooth(-0.012f, 0.035f, d);
            float seam = Smooth(0.03f, 0f, Mathf.Abs(mid)) * Smooth(-0.02f, 0.02f, side);
            rim = Mathf.Clamp01(0.32f + 0.5f * Smooth(0.07f, 0f, d) + 0.6f * seam + 0.25f * Smooth(0.2f, -0.5f, y)) * cover;
        }

        /// <summary>A mushroom cap: a dome with its rim curled under, pale spots on it, the gills' shade along its foot.</summary>
        static void CapMark(float x, float y, out float cover, out float rim)
        {
            float d = Ellipse(x, y + 0.2f, 0.8f, 0.62f);
            d = Mathf.Min(d, (y + 0.28f + 0.1f * (1f - x * x / 0.64f)) * 0.8f);
            d += 0.01f * (Fbm(x * 8f, y * 8f, 2, 261) - 0.5f);
            cover = Smooth(-0.012f, 0.035f, d);
            float spot = 0f;
            spot = Mathf.Max(spot, Smooth(0.12f, 0.08f, Dist(x + 0.36f, y - 0.1f)));
            spot = Mathf.Max(spot, Smooth(0.13f, 0.09f, Dist(x - 0.06f, y - 0.28f)));
            spot = Mathf.Max(spot, Smooth(0.11f, 0.07f, Dist(x - 0.42f, y - 0.04f)));
            spot = Mathf.Max(spot, Smooth(0.08f, 0.05f, Dist(x + 0.04f, y + 0.02f)));
            spot = Mathf.Max(spot, Smooth(0.07f, 0.04f, Dist(x - 0.64f, y + 0.12f)));
            float gills = Smooth(-0.05f, -0.25f, y);
            rim = Mathf.Clamp01(0.5f + 0.45f * Smooth(0.07f, 0f, d) + 0.35f * gills - 0.8f * spot) * cover;
        }

        /// <summary>A stem's body across v (y), repeating along u (phase 0..1): ragged edges, a darker wet edge each side,
        /// fibres running along it with a few dry gaps.</summary>
        static void RibbonMark(float phase, float y, out float cover, out float rim)
        {
            float hw = 0.8f + 0.07f * (FbmP(phase * 4f, 0.3f, 2, 61, 4) - 0.5f) * 2f;
            float rag = 0.05f * (FbmP(phase * 32f, y * 4f, 2, 62, 32) - 0.5f) * 2f;
            float d = hw + rag - Mathf.Abs(y + 0.04f * (FbmP(phase * 2f, 5f, 2, 64, 2) - 0.5f));
            cover = Smooth(-0.02f, 0.07f, d);
            float streak = FbmP(phase * 2f, y * 15f, 3, 63, 2);
            cover *= 1f - 0.3f * Smooth(0.6f, 0.78f, streak);
            rim = Mathf.Clamp01(Smooth(0.2f, 0.02f, d) * 0.95f + 0.45f * (streak - 0.5f) + 0.15f) * cover;
        }

        /// <summary>A soft wash band (a bird's swooping path): feathered both sides, faint streaks along it.</summary>
        static void BandMark(float phase, float y, out float cover, out float rim)
        {
            float hw = 0.66f + 0.06f * (FbmP(phase * 3f, 1.1f, 2, 65, 3) - 0.5f) * 2f;
            float d = hw - Mathf.Abs(y);
            cover = Smooth(-0.12f, 0.28f, d);
            float streak = FbmP(phase * 3f, y * 9f, 2, 66, 3);
            rim = Mathf.Clamp01(Smooth(0.14f, 0.0f, d) * 0.7f + 0.3f * (streak - 0.5f) + 0.1f) * cover;
        }

        // ---------------------------------------------------------------- shapes

        static float Dist(float x, float y) => Mathf.Sqrt(x * x + y * y);

        /// <summary>Inside an axis ellipse: positive inside (about the distance to its edge near it, scaled by its smaller radius).</summary>
        static float Ellipse(float x, float y, float rx, float ry)
        {
            float q = Mathf.Sqrt(x * x / (rx * rx) + y * y / (ry * ry));
            return (1f - q) * Mathf.Min(rx, ry);
        }

        /// <summary>An ellipse turned by angle (radians).</summary>
        static float RotEllipse(float x, float y, float angle, float rx, float ry)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            return Ellipse(c * x + s * y, -s * x + c * y, rx, ry);
        }

        /// <summary>A capsule from a to b, radius r0 at a to r1 at b: positive inside.</summary>
        static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r0, float r1)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / Mathf.Max(1e-6f, vx * vx + vy * vy));
            float px = x - (ax + vx * t), py = y - (ay + vy * t);
            return Mathf.Lerp(r0, r1, t) - Mathf.Sqrt(px * px + py * py);
        }

        /// <summary>A tapering stroke along a quadratic curve a (width w0) .. control m .. b (width w1), in six pieces.</summary>
        static float Curve(float x, float y, float ax, float ay, float mx, float my, float bx, float by, float w0, float w1)
        {
            float d = -1f, px0 = ax, py0 = ay;
            for (int s = 1; s <= 6; s++)
            {
                float t = s / 6f, u = 1f - t;
                float px = u * u * ax + 2f * u * t * mx + t * t * bx, py = u * u * ay + 2f * u * t * my + t * t * by;
                d = Mathf.Max(d, Capsule(x, y, px0, py0, px, py, Mathf.Lerp(w0, w1, (s - 1) / 6f), Mathf.Lerp(w0, w1, t)));
                px0 = px; py0 = py;
            }
            return d;
        }

        // ---------------------------------------------------------------- noise

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Hash01(int i, int seed)
        {
            unchecked
            {
                uint h = (uint)i * 0x9E3779B1u ^ (uint)seed * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h >> 8) / 16777216f;
            }
        }

        static float Lattice(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h >> 8) / 16777216f;
            }
        }

        /// <summary>Value noise; with period &gt; 0 the lattice repeats along x every period cells (a seamless tile).</summary>
        static float Noise(float x, float y, int seed, int period = 0)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = x0, xb = x0 + 1;
            if (period > 0) { xa = ((xa % period) + period) % period; xb = ((xb % period) + period) % period; }
            float a = Lattice(xa, y0, seed), b = Lattice(xb, y0, seed), c = Lattice(xa, y0 + 1, seed), d = Lattice(xb, y0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal value noise (public: the gallery's landscapes wash with it too).</summary>
        public static float Fbm(float x, float y, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(x, y, seed + 97 * o);
                norm += amp;
                x *= 2.03f; y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>Fbm that tiles along x with the given period (x in lattice cells; each octave doubles x and the period).</summary>
        static float FbmP(float x, float y, int octaves, int seed, int period)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(x, y, seed + 97 * o, period);
                norm += amp;
                x *= 2f; y *= 2.03f; period *= 2;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
