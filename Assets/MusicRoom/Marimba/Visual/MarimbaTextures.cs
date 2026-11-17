// The marimba's generated textures (spec 4.2 "Textures"; review item 17), all from code, seeded and deterministic:
//   Rosewood  1024 x 256: four horizontal bands of the same grain family, each baked with its own tint (-4 % .. +4 %),
//             grain running along u (the bar's length); the right-hand strip holds end grain and the near-black of the
//             cord holes. Each bar takes its own seeded window of a band (u offset, flip): no two neighbours look alike.
//   FrameWood 512 x 256: dark walnut (grain along u), felt, the mallets' rattan shaft and yarn, the dark red cord.
//   Metal     256 x 64: brushed dark bronze (resonators, brush lines along u), brushed brass (rims, posts, brackets) and
//             the blackened inside of a resonator mouth.
// Phase B: these become ITextureBakeProvider entries ("Marimba/rosewood", "Marimba/framewood", "Marimba/metal") so the
// build ships baked PNGs; here they are generated at build time of the view (about 60 ms in the editor).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public static class MarimbaTextures
    {
        public const int RoseW = 1024, RoseH = 256, FrameW = 512, FrameH = 256, MetalW = 256, MetalH = 64;
        public const int RoseBands = 4;
        /// <summary>The bands' baked tints (review item 17: +-4 %).</summary>
        public static readonly float[] BandTint = { 0.96f, 0.987f, 1.013f, 1.04f };
        /// <summary>u where the bars' grain ends; the strip to the right is end grain (to 0.985) then the holes' near-black.</summary>
        public const float RoseGrainU = 0.955f, RoseEndU0 = 0.962f, RoseEndU1 = 0.982f, RoseHoleU0 = 0.988f, RoseHoleU1 = 0.998f;

        // FrameWood atlas regions (u)
        public static readonly UvRegion Walnut = new UvRegion(0.004f, 0.496f, 0.02f, 0.98f);
        public static readonly UvRegion Felt = new UvRegion(0.51f, 0.615f, 0.05f, 0.95f);
        public static readonly UvRegion Rattan = new UvRegion(0.635f, 0.74f, 0.02f, 0.98f);
        public static readonly UvRegion Yarn = new UvRegion(0.76f, 0.865f, 0.02f, 0.98f);
        public static readonly UvRegion Cord = new UvRegion(0.885f, 0.93f, 0.05f, 0.95f);
        public static readonly UvRegion TrayShadow = new UvRegion(0.95f, 0.99f, 0.1f, 0.9f);
        // Metal atlas regions (u)
        public static readonly UvRegion Bronze = new UvRegion(0.01f, 0.72f, 0.04f, 0.96f);
        public static readonly UvRegion Brass = new UvRegion(0.76f, 0.94f, 0.04f, 0.96f);
        public static readonly UvRegion TubeInside = new UvRegion(0.965f, 0.995f, 0.1f, 0.9f);

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Smooth(double e0, double e1, double x) { double t = System.Math.Min(1, System.Math.Max(0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t); }
        static byte B(double v) => ProcTex.ClampByte(v * 255);

        /// <summary>One rosewood grain sample (linear 0..1 brightness of the figure) at texture pixel (x, y) of a band.</summary>
        static double RoseFigure(double x, double y, int band)
        {
            double yb = y + band * 157.0;                       // each band its own slice of the same wood
            double warp = (ProcTex.Fbm(x * 0.0035, yb * 0.03, 3) - 0.5) * 9.0;
            double lines = ProcTex.Fbm(x * 0.0025 + 3.1, (yb + warp) * 0.19, 3);           // long dark lines along u
            double streak = Smooth(0.60, 0.80, lines);
            double fine = ProcTex.VNoise(x * 0.05, (yb + warp * 0.6) * 1.3);               // fine grain lines
            double pores = ProcTex.VNoise(x * 0.7, yb * 2.3);                              // open pores (tiny dark flecks)
            double figure = ProcTex.Fbm(x * 0.0009 + 7.7, yb * 0.006, 2);                  // slow colour drift
            double t = 0.60 + (figure - 0.5) * 0.45 + (fine - 0.5) * 0.18 - streak * 0.30 - Smooth(0.84, 0.96, pores) * 0.12;
            return System.Math.Min(1, System.Math.Max(0, t));
        }

        public static Texture2D Rosewood()
        {
            var px = new Color32[RoseW * RoseH];
            int bandH = RoseH / RoseBands;
            // #8a4a30 light, #5a2a1c mid-dark, deep streak #2a120a (sRGB)
            double lr = 0x94 / 255.0, lg = 0x4e / 255.0, lb = 0x32 / 255.0, dr = 0x5a / 255.0, dg = 0x2a / 255.0, db = 0x1c / 255.0;
            double sr = 0x33 / 255.0, sg = 0x16 / 255.0, sb = 0x0d / 255.0;
            for (int y = 0; y < RoseH; y++)
            {
                int band = y / bandH;
                double tint = BandTint[band];
                for (int x = 0; x < RoseW; x++)
                {
                    double u = (x + 0.5) / RoseW;
                    double r, g, b;
                    if (u < RoseGrainU + 0.003)
                    {
                        double t = RoseFigure(x, y, band);
                        if (t > 0.35) { double k = (t - 0.35) / 0.65; r = Lerp(dr, lr, k); g = Lerp(dg, lg, k); b = Lerp(db, lb, k); }
                        else { double k = t / 0.35; r = Lerp(sr, dr, k); g = Lerp(sg, dg, k); b = Lerp(sb, db, k); }
                        r *= tint; g *= tint; b *= tint;
                    }
                    else if (u < RoseHoleU0 - 0.002)
                    {
                        // end grain: darker and duller than the face; faint growth rings across the bar's thickness
                        // (texture x maps to the height of the end face), tiny pores
                        double ring = 0.5 + 0.5 * System.Math.Sin(x * 1.3 + ProcTex.VNoise(x * 0.2, y * 0.05) * 3);
                        double pore = ProcTex.VNoise(x * 1.7, y * 1.9);
                        double k = 0.62 + 0.10 * ring - Smooth(0.8, 0.95, pore) * 0.12;
                        r = dr * k; g = dg * k; b = db * k;
                    }
                    else { r = 0.035; g = 0.022; b = 0.018; }
                    px[y * RoseW + x] = new Color32(B(r), B(g), B(b), 255);
                }
            }
            var tex = ProcTex.Make(RoseW, RoseH, px, false, true, "Marimba rosewood");
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        public static Texture2D FrameWood()
        {
            var px = new Color32[FrameW * FrameH];
            for (int y = 0; y < FrameH; y++)
                for (int x = 0; x < FrameW; x++)
                {
                    double u = (x + 0.5) / FrameW, v = (y + 0.5) / FrameH;
                    double r, g, b;
                    if (u < 0.5)
                    {
                        // dark walnut stain, grain along u
                        double warp = (ProcTex.Fbm(x * 0.01, y * 0.05, 2) - 0.5) * 6;
                        double grain = 0.5 + 0.5 * System.Math.Sin((y + warp) * 0.55 + ProcTex.Fbm(x * 0.004, y * 0.02, 2) * 6);
                        double fine = ProcTex.VNoise(x * 0.08, y * 1.4);
                        double t = 0.55 + (grain - 0.5) * 0.35 + (fine - 0.5) * 0.25 + (ProcTex.Fbm(x * 0.003, y * 0.01, 2) - 0.5) * 0.35;
                        t = System.Math.Min(1, System.Math.Max(0, t));
                        r = Lerp(0.16, 0.33, t); g = Lerp(0.095, 0.195, t); b = Lerp(0.055, 0.115, t);
                    }
                    else if (u < 0.625)
                    {
                        double n = ProcTex.Fbm(x * 0.6, y * 0.6, 3);                 // felt #3a3430
                        double k = 0.85 + (n - 0.5) * 0.35;
                        r = 0x44 / 255.0 * k; g = 0x3b / 255.0 * k; b = 0x34 / 255.0 * k;
                    }
                    else if (u < 0.75)
                    {
                        // rattan #c9a77a: lengthwise fibres, faint darker nodes
                        double fib = ProcTex.VNoise(x * 0.12, y * 2.0);
                        double node = Smooth(0.92, 0.99, 0.5 + 0.5 * System.Math.Sin(x * 0.09));
                        double k = 0.9 + (fib - 0.5) * 0.2 - node * 0.18;
                        r = 0xc9 / 255.0 * k; g = 0xa7 / 255.0 * k; b = 0x7a / 255.0 * k;
                    }
                    else if (u < 0.875)
                    {
                        // wound wool yarn #4a5248: strands across the wrap, fuzz
                        double strand = 0.5 + 0.5 * System.Math.Sin(y * 2.2 + ProcTex.VNoise(x * 0.3, y * 0.3) * 3);
                        double fuzz = ProcTex.Fbm(x * 0.9, y * 0.9, 3);
                        double k = 0.86 + (strand - 0.5) * 0.24 + (fuzz - 0.5) * 0.4;
                        r = 0x55 / 255.0 * k; g = 0x5f / 255.0 * k; b = 0x53 / 255.0 * k;
                    }
                    else if (u < 0.94)
                    {
                        // dark red nylon cord with a twist
                        double tw = 0.5 + 0.5 * System.Math.Sin((x + y) * 0.8);
                        double k = 0.8 + tw * 0.25;
                        r = 0.36 * k; g = 0.07 * k; b = 0.06 * k;
                    }
                    else { r = 0.05; g = 0.035; b = 0.03; }
                    px[y * FrameW + x] = new Color32(B(r), B(g), B(b), 255);
                    _ = v;
                }
            var tex = ProcTex.Make(FrameW, FrameH, px, false, true, "Marimba framewood");
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        public static Texture2D Metal()
        {
            var px = new Color32[MetalW * MetalH];
            for (int y = 0; y < MetalH; y++)
                for (int x = 0; x < MetalW; x++)
                {
                    double u = (x + 0.5) / MetalW;
                    double brush = ProcTex.VNoise(x * 0.05, y * 3.1) * 0.6 + ProcTex.VNoise(x * 0.2, y * 7.3) * 0.4;   // lines along u
                    double r, g, b;
                    if (u < 0.75)
                    {
                        // anodised warm bronze (the resonators): an even metal colour with only a faint brushed grain along the
                        // tube (a few per cent), so the surface reads as metal, not as striped wood; the sheen comes from the
                        // material's metallic / smoothness, the brushing from these fine lines
                        double patina = ProcTex.Fbm(x * 0.02, y * 0.15, 2);
                        double k = 0.97 + (brush - 0.5) * 0.06 + (patina - 0.5) * 0.03;
                        r = 0.47 * k; g = 0.365 * k; b = 0.235 * k;
                    }
                    else if (u < 0.95)
                    {
                        // brushed brass (mouth rims, posts, tray brackets)
                        double k = 0.97 + (brush - 0.5) * 0.06;
                        r = 0.80 * k; g = 0.635 * k; b = 0.36 * k;
                    }
                    else { r = 0.03; g = 0.025; b = 0.02; }
                    px[y * MetalW + x] = new Color32(B(r), B(g), B(b), 255);
                }
            var tex = ProcTex.Make(MetalW, MetalH, px, false, true, "Marimba metal");
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
