// The handpan's generated textures (handpan.md 7.3; no third-party image): the nitrided steel's colour, metallic /
// smoothness and normal maps, in one 2048 x 1024 atlas, and the stand's walnut + felt atlas. CPU only, deterministic.
//   Steel atlas: the left square is the top cap in its map (U = 0.25 + X / 1.2, V = 0.5 + Z / 0.6: 0.59 mm per texel),
//   the right square the bottom cap in its own polar map; the bottom square's centre (inside the gu) is the dark inner
//   steel, which the inner surfaces and the gu's rolled lip use. Hammer dents are Worley bowls (3.5 mm cells in the tone
//   fields, 5.5 mm on the shoulder, depth by cell), the tone ring a 0.5 mm raised shoulder at each field's edge.
//   Colour: dark bronze-brown #3a2f27 with a faint mottle, a warmer (+5 % red) heat tint around each dimple.
//   Smoothness 0.55, +0.08 in the dimples (burnished by playing), -0.1 in a soft ring at each field edge; metallic 0.85.
// Names "Handpan/<name>" (GeneratedAssets: baked PNG if present, else generated at runtime; the bake is phase B).
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Handpan
{
    public sealed class HandpanTextures : ITextureBakeProvider
    {
        public const string SteelBase = "Handpan/steel_base", SteelMs = "Handpan/steel_ms", SteelNormal = "Handpan/steel_normal", Stand = "Handpan/stand";
        public const int W = 2048, H = 1024, StandSize = 512;
        /// <summary>The map half-width a square covers (m).</summary>
        public const double MapHalf = 0.30;

        public IEnumerable<TextureBakeSpec> Textures
        {
            get
            {
                yield return new TextureBakeSpec { Name = SteelBase, Generate = () => Make(SteelBase), Mipmaps = true };
                // HasAlpha: the smoothness lives in the alpha (URP Lit _METALLICSPECGLOSSMAP); without it the importer drops the alpha
                // and the steel turns into a mirror (smoothness 1)
                yield return new TextureBakeSpec { Name = SteelMs, Generate = () => Make(SteelMs), Mipmaps = true, Linear = true, HasAlpha = true };
                yield return new TextureBakeSpec { Name = SteelNormal, Generate = () => Make(SteelNormal), Mipmaps = true, Linear = true, NormalMap = true };
                yield return new TextureBakeSpec { Name = Stand, Generate = () => Make(Stand), Mipmaps = true };
            }
        }

        public static Texture2D Get(string name) => GeneratedAssets.Texture(name, () => Make(name));

        // ---------------------------------------------------------------- UV conventions (shared with HandpanGeometry)

        public static Vector2 TopUv(double X, double Z) => new Vector2((float)(0.25 + X / (4 * MapHalf)), (float)(0.5 + Z / (2 * MapHalf)));
        public static Vector2 BottomUv(double X, double Z) => new Vector2((float)(0.75 + X / (4 * MapHalf)), (float)(0.5 + Z / (2 * MapHalf)));
        /// <summary>The dark inner steel (inside the gu, under every inner surface).</summary>
        public static Vector2 InnerUv(double r01 = 0) => BottomUv(0.012 * r01, 0);
        /// <summary>The gu's dark centre radius in the bottom map (m): the hole is 45 mm; inside 40 mm everything is the inner look.</summary>
        public const double InnerR = 0.040;
        /// <summary>The stand atlas: walnut in U [0, 0.75), felt in [0.75, 1].</summary>
        public static Vector2 WalnutUv(double u, double v) => new Vector2((float)(u * 0.74), (float)v);
        public static Vector2 FeltUv(double u, double v) => new Vector2((float)(0.76 + u * 0.23), (float)v);

        // ---------------------------------------------------------------- generation

        static readonly Dictionary<string, Texture2D> s_made = new Dictionary<string, Texture2D>();

        /// <summary>One texture by name (the three steel maps share one pass; the others are kept for the caller).</summary>
        public static Texture2D Make(string name)
        {
            if (name == Stand) return MakeStand();
            if (s_made.TryGetValue(name, out var t) && t != null) { s_made.Remove(name); return t; }
            MakeSteel(out var b, out var ms, out var nrm);
            s_made[SteelBase] = b; s_made[SteelMs] = ms; s_made[SteelNormal] = nrm;
            t = s_made[name]; s_made.Remove(name);
            return t;
        }

        static double Smooth(double e0, double e1, double x) { double t = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t); }

        /// <summary>Worley bowls: the height (m) of a dent field at p (cell size c, depth d): d ((F1 / (c/2))^2 - 1) per cell,
        /// depth jittered by cell; the cell edges become the ridges a hammer leaves.</summary>
        static double Dents(double x, double y, double cell, double depth, int seed)
        {
            double gx = x / cell, gy = y / cell;
            int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
            double best = 1e9, bestD = depth;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = ix + i, cy = iy + j;
                    double fx = cx + 0.15 + 0.7 * ProcTex.Hash(cx * 7 + seed, cy * 13 + 1);
                    double fy = cy + 0.15 + 0.7 * ProcTex.Hash(cx * 11 + 3, cy * 5 + seed);
                    double dx = gx - fx, dy = gy - fy, d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; bestD = depth * (0.6 + 0.8 * ProcTex.Hash(cx + 17 * seed, cy - 29)); }
                }
            double f = Math.Min(1.0, Math.Sqrt(best) / 0.62);
            return bestD * (f * f - 1);
        }

        /// <summary>The field look at a top-cap map point: in-field weight, dimple weight, field-edge ring weight, dimple tint
        /// ring weight, and the tone ring's height (m).</summary>
        static void FieldLook(double X, double Z, out double inField, out double dimple, out double edge, out double tint, out double toneRing, out double crease)
        {
            inField = 0; dimple = 0; edge = 0; tint = 0; toneRing = 0; crease = 0;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
            {
                HandpanField F = HandpanSpec.Fields[i];
                HandpanMath.FieldCoords(F, X, Z, out double u, out double v);
                double rho = Math.Sqrt(u * u + v * v);
                if (rho > 1.35) continue;
                double semi = F.Short / 2;
                double dEdge = (rho - 1) * semi;                       // metres outside (+) / inside (-) the field edge
                inField = Math.Max(inField, 1 - Smooth(-0.004, 0.004, dEdge));
                double q = rho / F.DimpleFrac;                          // 1 at the dimple's rim
                dimple = Math.Max(dimple, 1 - Smooth(0.75, 1.05, q));
                edge = Math.Max(edge, Math.Exp(-(dEdge + 0.006) * (dEdge + 0.006) / (2 * 0.006 * 0.006)));
                tint = Math.Max(tint, Math.Exp(-(q - 1.15) * (q - 1.15) / (2 * 0.22 * 0.22)));
                crease = Math.Max(crease, Math.Exp(-(q - 1.0) * (q - 1.0) / (2 * 0.07 * 0.07)));   // the dimple's rim: a darker patina line
                toneRing += 0.0005 * Math.Exp(-(dEdge * dEdge) / (2 * 0.0012 * 0.0012));
            }
        }

        static void MakeSteel(out Texture2D baseMap, out Texture2D msMap, out Texture2D normalMap)
        {
            var hgt = new float[W * H];
            var col = new Color32[W * H];
            var ms = new Color32[W * H];
            double px = 4 * MapHalf / W;      // metres per texel (both squares: 0.6 m over 1024)
            // base colour, sRGB #3a2f27
            const double R0 = 58, G0 = 47, B0 = 39;
            for (int y = 0; y < H; y++)
            {
                double vv = (y + 0.5) / H;
                for (int x = 0; x < W; x++)
                {
                    double uu = (x + 0.5) / W;
                    bool top = uu < 0.5;
                    double X = top ? (uu - 0.25) * 4 * MapHalf : (uu - 0.75) * 4 * MapHalf, Z = (vv - 0.5) * 2 * MapHalf;
                    int k = y * W + x;
                    double s = Math.Sqrt(X * X + Z * Z);
                    double mottle = (ProcTex.Fbm(X * 90 + (top ? 0 : 50), Z * 90, 3) - 0.5) * 0.10 + (ProcTex.Fbm(X * 9, Z * 9 + (top ? 0 : 20), 2) - 0.5) * 0.08;
                    double r = R0, g = G0, b = B0, smooth = 0.55, metal = 0.85, h;
                    if (top)
                    {
                        FieldLook(X, Z, out double inField, out double dimple, out double edge, out double tint, out double toneRing, out double crease);
                        h = Dents(X, Z, 0.0035, 0.00006, 1) * inField + Dents(X, Z, 0.0055, 0.00008, 2) * (1 - inField) + toneRing;
                        smooth += 0.08 * dimple - 0.10 * edge;
                        double warm = 0.05 * tint;
                        r *= 1 + warm + mottle; g *= 1 + mottle * 0.9 - 0.01 * tint; b *= 1 + mottle * 0.8 - 0.02 * tint;
                        // the tuned fields are hammered brighter than the matte shoulder; the dimples burnished by playing;
                        // a darker patina line where each dimple meets its field and along each field's tone ring
                        double lift = 0.07 * inField + 0.10 * dimple - 0.16 * crease - 0.08 * edge * inField;
                        r *= 1 + lift; g *= 1 + lift * 0.95; b *= 1 + lift * 0.9;
                    }
                    else
                    {
                        // bottom: shoulder dents everywhere, darker toward the gu, the inner steel inside it
                        h = Dents(X, Z, 0.0055, 0.00007, 3);
                        r *= 1 + mottle; g *= 1 + mottle * 0.9; b *= 1 + mottle * 0.8;
                        double inner = 1 - Smooth(HandpanTextures.InnerR, HandpanTextures.InnerR + 0.004, s);
                        if (inner > 0)
                        {
                            r = r * (1 - inner) + 9 * inner; g = g * (1 - inner) + 8 * inner; b = b * (1 - inner) + 7 * inner;
                            smooth = smooth * (1 - inner) + 0.25 * inner; metal = metal * (1 - inner) + 0.4 * inner;
                            h *= 1 - inner;
                        }
                    }
                    hgt[k] = (float)h;
                    col[k] = new Color32(ProcTex.ClampByte(r), ProcTex.ClampByte(g), ProcTex.ClampByte(b), 255);
                    ms[k] = new Color32(ProcTex.ClampByte(metal * 255), 0, 0, ProcTex.ClampByte(Math.Max(0, Math.Min(1, smooth)) * 255));
                }
            }
            // tangent-space normals from the height field (central differences; +x = +U = +X, +y = +V = +Z)
            var nrm = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int xl = x > 0 ? x - 1 : x, xr = x < W - 1 ? x + 1 : x, yd = y > 0 ? y - 1 : y, yu = y < H - 1 ? y + 1 : y;
                    if (x == W / 2 - 1) xr = x;      // no difference across the two squares
                    if (x == W / 2) xl = x;
                    double dhx = (hgt[y * W + xr] - hgt[y * W + xl]) / ((xr - xl) * px + 1e-12);
                    double dhy = (hgt[yu * W + x] - hgt[yd * W + x]) / ((yu - yd) * px + 1e-12);
                    double nx = -dhx, ny = -dhy, nz = 1, l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx /= l; ny /= l; nz /= l;
                    nrm[y * W + x] = new Color32(ProcTex.ClampByte((nx * 0.5 + 0.5) * 255), ProcTex.ClampByte((ny * 0.5 + 0.5) * 255), ProcTex.ClampByte((nz * 0.5 + 0.5) * 255), 255);
                }
            baseMap = Tex(col, W, H, false, SteelBase);
            // the metallic / smoothness map is low-frequency: half resolution (the instrument's textures stay under 3 MB ASTC,
            // platform.md 11)
            var ms2 = new Color32[(W / 2) * (H / 2)];
            for (int y = 0; y < H / 2; y++)
                for (int x = 0; x < W / 2; x++)
                {
                    int r = 0, a = 0;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { Color32 c = ms[(2 * y + dy) * W + 2 * x + dx]; r += c.r; a += c.a; }
                    ms2[y * (W / 2) + x] = new Color32((byte)(r / 4), 0, 0, (byte)(a / 4));
                }
            msMap = Tex(ms2, W / 2, H / 2, true, SteelMs);
            normalMap = Tex(nrm, W, H, true, SteelNormal);
        }

        static Texture2D Tex(Color32[] px, int w, int h, bool linear, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        /// <summary>Walnut (oil finish, the room's walnut grain) in U [0, 0.75), undyed wool felt in [0.75, 1]; smoothness in
        /// alpha is not used (one Lit material: walnut 0.45 overall, as handpan.md 7.3).</summary>
        static Texture2D MakeStand()
        {
            int n = StandSize;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double u = (x + 0.5) / n;
                    Color32 c;
                    if (u < 0.75)
                    {
                        // the generator's grain stripes run along its canvas y, i.e. along V: the legs' length
                        ProcTex.WoodPixel(WoodParams.Walnut, x % WoodParams.Walnut.Size, (n - 1 - y) % WoodParams.Walnut.Size, out c, out _, out _);
                    }
                    else
                    {
                        double f = ProcTex.Fbm(x * 0.35, y * 0.35, 3), fib = ProcTex.VNoise(x * 1.7, y * 0.6);
                        double l = 0.70 + (f - 0.5) * 0.10 + (fib - 0.5) * 0.06;
                        c = new Color32(ProcTex.ClampByte(l * 238), ProcTex.ClampByte(l * 226), ProcTex.ClampByte(l * 205), 255);
                    }
                    px[y * n + x] = c;
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { name = Stand, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }
    }
}
