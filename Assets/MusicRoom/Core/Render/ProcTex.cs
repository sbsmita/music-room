// Shared procedural textures (spec 07 section 7), implemented once in the foundation so every module draws the
// same wood, drum head, cymbal, grille and soft dot (one shared material set). CPU only and exact:
// double arithmetic in the reference operation order, bytes written as clamped 8-bit RGBA (clamp, then
// round half to even), image row y (row 0 = top) stored in Texture2D row h-1-y (ARCHITECTURE section 5). Fingerprints
// computed with the reference formulas: Tests/EditMode/Core/ProcTexTests.cs.
// Module-specific textures (plaster, rug, leaves, brick, weave, scenery, night sky, hill snow) stay in their module and
// may call Hash / VNoise / Fbm from here. Text labels are TextMeshPro, never generated textures.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>Wood texture options. Doubles, exactly the reference literals (a float 1.6 would move the
    /// noise lattice); colours are 0..1 sRGB components.</summary>
    public struct WoodParams
    {
        public int Size, Planks, Seed;
        public double GrainScale;
        public double LightR, LightG, LightB, DarkR, DarkG, DarkB;

        public WoodParams(int size, int planks, int seed, double grainScale, double lr, double lg, double lb, double dr, double dg, double db)
        {
            Size = size; Planks = planks; Seed = seed; GrainScale = grainScale;
            LightR = lr; LightG = lg; LightB = lb; DarkR = dr; DarkG = dg; DarkB = db;
        }

        // The oak and walnut presets (repeat is a material / bake setting, not part of the image).
        public static readonly WoodParams Oak = new WoodParams(1024, 16, 3, 1.6, 0.72, 0.52, 0.33, 0.44, 0.28, 0.14);
        public static readonly WoodParams Walnut = new WoodParams(512, 0, 5, 2.4, 0.36, 0.21, 0.12, 0.17, 0.09, 0.05);
        public static readonly WoodParams Maple = new WoodParams(512, 0, 9, 2.4, 0.88, 0.71, 0.48, 0.72, 0.53, 0.32);
        public static readonly WoodParams Spruce = new WoodParams(512, 0, 11, 1.6, 0.94, 0.82, 0.60, 0.82, 0.66, 0.44);
        public static readonly WoodParams Rosewood = new WoodParams(512, 0, 13, 2.4, 0.32, 0.16, 0.10, 0.12, 0.05, 0.03);
        public static readonly WoodParams Stand = new WoodParams(256, 0, 17, 2.4, 0.075, 0.072, 0.07, 0.035, 0.034, 0.034);
    }

    public static class ProcTex
    {
        // ---------------- noise ----------------

        /// <summary>JS ToInt32 (x | 0): truncate, wrap modulo 2^32.</summary>
        public static int ToInt32(double d)
        {
            if (d > -2147483648.0 && d < 2147483648.0) return (int)d;
            if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
            double m = Math.Truncate(d) % 4294967296.0;
            if (m < 0) m += 4294967296.0;
            return unchecked((int)(uint)m);
        }

        /// <summary>hash(x, y): 32-bit integer-multiply (Math.imul) hash to [0, 1].</summary>
        public static double Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return (uint)(h ^ (int)((uint)h >> 16)) / 4294967295.0;
            }
        }

        public static double Hash(double x, double y) => Hash(ToInt32(x), ToInt32(y));

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>vnoise: value noise, smoothstep fade, bilinear over the 4 lattice hashes.</summary>
        public static double VNoise(double x, double y)
        {
            double xi = Math.Floor(x), yi = Math.Floor(y);
            double fx = x - xi, fy = y - yi;
            double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Lerp(
                Lerp(Hash(xi, yi), Hash(xi + 1, yi), sx),
                Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), sx),
                sy);
        }

        /// <summary>fbm: oct octaves, amplitude .5 halving, frequency doubling, normalised by the amplitudes.</summary>
        public static double Fbm(double x, double y, int oct = 3)
        {
            double s = 0, a = 0.5, f = 1, n = 0;
            for (int i = 0; i < oct; i++)
            {
                s += a * VNoise(x * f, y * f);
                n += a;
                a *= 0.5;
                f *= 2;
            }
            return s / n;
        }

        /// <summary>Uint8ClampedArray store: NaN -> 0, clamp to [0, 255], round half to even.</summary>
        public static byte ClampByte(double v)
        {
            if (double.IsNaN(v) || v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)Math.Round(v, MidpointRounding.ToEven);
        }

        // ---------------- wood ----------------

        /// <summary>One wood pixel at image (x, y) (row 0 = top): sRGB colour and the roughness byte; seam = plank seam.</summary>
        public static void WoodPixel(in WoodParams p, int x, int y, out Color32 color, out byte rough, out bool seam)
        {
            int size = p.Size;
            double pw = p.Planks != 0 ? (double)size / p.Planks : size;
            double plankLen = size / 2.0;
            double px = x, py = y, tint = 0;
            seam = false;
            if (p.Planks != 0)
            {
                double col = Math.Floor(x / pw);
                double off = Math.Floor(Hash(ToInt32(col), p.Seed) * plankLen);
                double row = Math.Floor((y + off) / plankLen);
                double id = Hash(ToInt32(col * 31 + row * 7), p.Seed + 7);
                tint = (id - 0.5) * 0.22;
                px = x + id * 1000;
                py = y + off + row * 300;
                double ex = x - col * pw;
                double ey = (y + off) % plankLen;
                if (ex < 2 || ey < 2) seam = true;
            }
            double gs = p.GrainScale;
            double figure = Fbm(px * 0.004, py * 0.0015, 2);
            double warp = (Fbm(px * 0.02 * gs, py * 0.004 * gs, 2) - 0.5) * 5;
            double stripe = 0.5 + 0.5 * Math.Sin(px * 0.35 * gs + warp);
            double fine = VNoise(px * 1.8 * gs, py * 0.1 * gs);
            double t = 0.5 + (stripe - 0.5) * 0.32 + (fine - 0.5) * 0.28 + (figure - 0.5) * 0.6 + tint;
            t = Math.Min(1, Math.Max(0, t));
            double rr = Lerp(p.DarkR, p.LightR, t), gg = Lerp(p.DarkG, p.LightG, t), bb = Lerp(p.DarkB, p.LightB, t);
            if (seam) { rr *= 0.35; gg *= 0.35; bb *= 0.35; }
            color = new Color32(ClampByte(rr * 255), ClampByte(gg * 255), ClampByte(bb * 255), 255);
            double r = 0.5 + 0.35 * (1 - t) + (seam ? 0.2 : 0);
            rough = ClampByte(Math.Min(255, r * 255));
        }

        /// <summary>The wood colour map (sRGB) and roughness map (linear, grey) in Unity row order.</summary>
        public static void Wood(in WoodParams p, out Texture2D map, out Texture2D roughness)
        {
            int n = p.Size;
            var c = new Color32[n * n];
            var r = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                int row = (n - 1 - y) * n;
                for (int x = 0; x < n; x++)
                {
                    WoodPixel(p, x, y, out var col, out byte rough, out _);
                    c[row + x] = col;
                    r[row + x] = new Color32(rough, rough, rough, 255);
                }
            }
            map = Make(n, n, c, false, true, "wood");
            roughness = Make(n, n, r, true, true, "wood_rough");
        }

        public static Texture2D WoodMap(in WoodParams p) { Wood(p, out var m, out var r); UnityEngine.Object.DestroyImmediate(r); return m; }
        public static Texture2D WoodRoughness(in WoodParams p) { Wood(p, out var m, out var r); UnityEngine.Object.DestroyImmediate(m); return r; }

        // ---------------- drum head, cymbal ----------------

        /// <summary>drumHeadTexture pixel: coated head, fine white speckle.</summary>
        public static Color32 DrumHeadPixel(int x, int y)
        {
            double n = 0.86 + 0.1 * VNoise(x * 0.8, y * 0.8) + 0.04 * Hash(x, y);
            return new Color32(ClampByte(242 * n), ClampByte(238 * n), ClampByte(228 * n), 255);
        }

        public static Texture2D DrumHead(int size = 256)
        {
            var c = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[(size - 1 - y) * size + x] = DrumHeadPixel(x, y);
            return Make(size, size, c, false, true, "drum_head");
        }

        /// <summary>cymbalTexture pixel: lathe grooves (rings along v) in brightness and roughness.</summary>
        public static void CymbalPixel(int x, int y, out Color32 color, out byte rough)
        {
            double ring = 0.5 + 0.5 * Math.Sin(y * 1.9);
            double n = VNoise(x * 0.3, y * 0.9);
            double b = 0.78 + 0.14 * ring + 0.08 * n;
            color = new Color32(ClampByte(220 * b), ClampByte(180 * b), ClampByte(95 * b), 255);
            rough = ClampByte((0.22 + 0.2 * ring + 0.1 * n) * 255);
        }

        public static void Cymbal(int size, out Texture2D map, out Texture2D roughness)
        {
            var c = new Color32[size * size];
            var r = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int row = (size - 1 - y) * size;
                for (int x = 0; x < size; x++)
                {
                    CymbalPixel(x, y, out var col, out byte rough);
                    c[row + x] = col;
                    r[row + x] = new Color32(rough, rough, rough, 255);
                }
            }
            map = Make(size, size, c, false, true, "cymbal");
            roughness = Make(size, size, r, true, true, "cymbal_rough");
        }

        // ---------------- grille ----------------

        static readonly Color32 GrilleMetal = new Color32(0x2b, 0x2c, 0x2f, 255), GrilleHole = new Color32(0x06, 0x06, 0x07, 255);

        /// <summary>Speaker grille tile: 4 x 4 round holes (r .3 pitch), odd rows offset half a pitch, wrapping in x. The
        /// arcs are anti-aliased: each pixel's hole coverage is measured on a 4 x 4 grid and blended in sRGB
        /// (sRGB-space compositing).</summary>
        public static Color32 GrillePixel(int size, int x, int y)
        {
            const int n = 4;
            double pitch = size / (double)n, r = pitch * 0.3, r2 = r * r;
            int inside = 0;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    double qx = x + (sx + 0.5) / 4.0, qy = y + (sy + 0.5) / 4.0;
                    bool hit = false;
                    for (int j = 0; j < n && !hit; j++)
                    {
                        double cy = (j + 0.5) * pitch;
                        double dy = qy - cy;
                        if (dy * dy > r2) continue;
                        for (int i = 0; i < n && !hit; i++)
                        {
                            double cx = (i + 0.5 + (j % 2) * 0.5) * pitch;
                            for (int w = -1; w <= 1 && !hit; w++)
                            {
                                double dx = qx - (cx + w * size);
                                if (dx * dx + dy * dy <= r2) hit = true;
                            }
                        }
                    }
                    if (hit) inside++;
                }
            double k = inside / 16.0;
            return new Color32(ClampByte(GrilleMetal.r + (GrilleHole.r - GrilleMetal.r) * k), ClampByte(GrilleMetal.g + (GrilleHole.g - GrilleMetal.g) * k),
                               ClampByte(GrilleMetal.b + (GrilleHole.b - GrilleMetal.b) * k), 255);
        }

        public static Texture2D Grille(int size = 64)
        {
            var c = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[(size - 1 - y) * size + x] = GrillePixel(size, x, y);
            return Make(size, size, c, false, true, "grille");
        }

        // ---------------- soft dot ----------------

        /// <summary>Radial gradient alpha at image pixel (x, y): 1 at the centre, .55 at 35 %, 0 at the edge (pixel centres).</summary>
        public static byte SoftDotAlpha(int size, int x, int y)
        {
            double c = size * 0.5;
            double dx = x + 0.5 - c, dy = y + 0.5 - c;
            double r = Math.Sqrt(dx * dx + dy * dy) / c;
            double a = r < 0.35 ? 1 + (0.55 - 1) * (r / 0.35) : r < 1 ? 0.55 + (0 - 0.55) * ((r - 0.35) / 0.65) : 0;
            return ClampByte(a * 255);
        }

        public static Texture2D SoftDot(int size = 64)
        {
            var c = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[(size - 1 - y) * size + x] = new Color32(255, 255, 255, SoftDotAlpha(size, x, y));
            var t = Make(size, size, c, false, true, "soft_dot");
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        // ---------------- helpers ----------------

        /// <summary>A Texture2D from Unity-row-order pixels (repeat wrap, trilinear; readable until baked or released).</summary>
        public static Texture2D Make(int w, int h, Color32[] px, bool linear, bool mipmaps, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipmaps, linear) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(mipmaps, false);
            return t;
        }
    }

    /// <summary>The foundation's shared texture set (names "Core/<name>"): baked by TextureBaker through
    /// CoreTextureProvider, loaded with Get(name) (the baked PNG, else the generator). Room, Piano, Drums, Harp and
    /// Lessons use these instead of generating their own copies.</summary>
    public static class SharedTextures
    {
        public const string WoodOak = "Core/wood_oak", WoodOakRough = "Core/wood_oak_rough";
        public const string WoodWalnut = "Core/wood_walnut", WoodWalnutRough = "Core/wood_walnut_rough";
        public const string WoodMaple = "Core/wood_maple", WoodMapleRough = "Core/wood_maple_rough";
        public const string WoodSpruce = "Core/wood_spruce", WoodRosewood = "Core/wood_rosewood", WoodStand = "Core/wood_stand";
        public const string DrumHead = "Core/drum_head", Cymbal = "Core/cymbal", CymbalRough = "Core/cymbal_rough";
        public const string Grille = "Core/grille", SoftDot = "Core/soft_dot";

        /// <summary>The baked texture, else generated now (and cached by GeneratedAssets).</summary>
        public static Texture2D Get(string name) => GeneratedAssets.Texture(name, () => Generate(name));

        public static Texture2D Generate(string name)
        {
            switch (name)
            {
                case WoodOak: return ProcTex.WoodMap(WoodParams.Oak);
                case WoodOakRough: return ProcTex.WoodRoughness(WoodParams.Oak);
                case WoodWalnut: return ProcTex.WoodMap(WoodParams.Walnut);
                case WoodWalnutRough: return ProcTex.WoodRoughness(WoodParams.Walnut);
                case WoodMaple: return ProcTex.WoodMap(WoodParams.Maple);
                case WoodMapleRough: return ProcTex.WoodRoughness(WoodParams.Maple);
                case WoodSpruce: return ProcTex.WoodMap(WoodParams.Spruce);
                case WoodRosewood: return ProcTex.WoodMap(WoodParams.Rosewood);
                case WoodStand: return ProcTex.WoodMap(WoodParams.Stand);
                case DrumHead: return ProcTex.DrumHead(256);
                case Cymbal: { ProcTex.Cymbal(256, out var m, out var r); UnityEngine.Object.DestroyImmediate(r); return m; }
                case CymbalRough: { ProcTex.Cymbal(256, out var m, out var r); UnityEngine.Object.DestroyImmediate(m); return r; }
                case Grille: return ProcTex.Grille(64);
                case SoftDot: return ProcTex.SoftDot(64);
                default: return null;
            }
        }

        /// <summary>Bake specs (sRGB colour maps with mipmaps and repeat; roughness maps linear; the soft dot clamped with alpha).</summary>
        public static IEnumerable<TextureBakeSpec> Specs()
        {
            foreach (var n in new[] { WoodOak, WoodWalnut, WoodMaple, WoodSpruce, WoodRosewood, WoodStand, DrumHead, Cymbal, Grille })
                yield return new TextureBakeSpec { Name = n, Generate = () => Generate(n), Mipmaps = true, Repeat = true };
            foreach (var n in new[] { WoodOakRough, WoodWalnutRough, WoodMapleRough, CymbalRough })
                yield return new TextureBakeSpec { Name = n, Generate = () => Generate(n), Linear = true, Mipmaps = true, Repeat = true };
            yield return new TextureBakeSpec { Name = SoftDot, Generate = () => Generate(SoftDot), Mipmaps = true, HasAlpha = true, AlphaIsTransparency = true };
        }
    }

    /// <summary>Registers the shared textures with TextureBaker (discovered by reflection like every provider).</summary>
    public sealed class CoreTextureProvider : ITextureBakeProvider
    {
        public IEnumerable<TextureBakeSpec> Textures => SharedTextures.Specs();
    }
}
