// The room-only procedural textures (spec 07 section 7): the per-pixel generators bit-for-bit (plaster,
// weave, hill snow: EditMode tests compare bytes with the reference maths) and the canvas-drawn ones (rug, leaf, brick,
// scenery, night sky) call by call through Canvas2D. The integer hash, value noise, fbm and the Uint8ClampedArray store
// are Core's (MusicRoom.Core.ProcTex, fingerprinted against the goldens), as are the shared woods and the soft dot
// (SharedTextures "Core/<name>"); the room derives only its URP metallic-gloss maps from Core's wood roughness bytes.
// Everything here is pure (no Unity API but the Color32 struct): generators run on worker threads at runtime and
// synchronously in the TextureBaker. Named RoomTexGen so `using MusicRoom.Core` never makes ProcTex ambiguous.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Textures
{
    /// <summary>Generated pixels in Texture2D order (row 0 = bottom) plus how the texture is to be sampled.</summary>
    public sealed class PixelImage
    {
        public string Name;
        public int Width, Height;
        public Color32[] Pixels;
        public bool Linear, Mipmaps = true, Repeat, PointFilter, NormalMap;
        public int Aniso = 1;

        /// <summary>Main thread only: the Texture2D. Runtime textures drop their CPU copy after the upload; the editor's
        /// bake needs them readable (EncodeToPNG).</summary>
        public Texture2D ToTexture(bool readable = false)
        {
            var t = new Texture2D(Width, Height, TextureFormat.RGBA32, Mipmaps, Linear)
            {
                name = Name,
                wrapMode = Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = PointFilter ? FilterMode.Point : FilterMode.Trilinear,
                anisoLevel = Aniso,
            };
            t.SetPixels32(Pixels);
            t.Apply(Mipmaps, !readable);
            return t;
        }

        public static PixelImage FromCanvasBytes(string name, int w, int h, byte[] rgbaTopDown)
        {
            var img = new PixelImage { Name = name, Width = w, Height = h, Pixels = new Color32[w * h] };
            for (int y = 0; y < h; y++)
            {
                int src = y * w * 4, dst = (h - 1 - y) * w;
                for (int x = 0; x < w; x++, src += 4)
                    img.Pixels[dst + x] = new Color32(rgbaTopDown[src], rgbaTopDown[src + 1], rgbaTopDown[src + 2], rgbaTopDown[src + 3]);
            }
            return img;
        }
    }

    public static class RoomTexGen
    {
        // ------------------------------------------------------------------ noise core (Core's ProcTex)

        public static double Hash(int x, int y) => ProcTex.Hash(x, y);
        public static double VNoise(double x, double y) => ProcTex.VNoise(x, y);
        public static double Fbm(double x, double y, int oct = 3) => ProcTex.Fbm(x, y, oct);
        /// <summary>Uint8ClampedArray store (ToUint8Clamp): clamp to 0..255, round half to even, NaN to 0.</summary>
        public static byte ClampByte(double v) => ProcTex.ClampByte(v);

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        static void Rows(int h, Action<int> row)
        {
            // Pure maths per row: spread over the thread pool (worker or editor thread alike).
            Parallel.For(0, h, row);
        }

        // ------------------------------------------------------------------ wood gloss (oak / walnut)

        /// <summary>The URP metallic-gloss map of a shared Core wood (R metallic 0, A smoothness = 1 - roughness x texel):
        /// the material's roughness (oak floor 1, walnut .8) is multiplied by the map's roughness byte, which is Core's
        /// ProcTex.WoodPixel (the same pass that draws SharedTextures "Core/wood_oak" / "Core/wood_walnut").</summary>
        public static PixelImage WoodGloss(in WoodParams p, double roughness, string name)
        {
            int n = p.Size;
            var gp = new Color32[n * n];
            WoodParams wp = p;
            Rows(n, y =>
            {
                int dst = (n - 1 - y) * n;
                for (int x = 0; x < n; x++)
                {
                    ProcTex.WoodPixel(wp, x, y, out _, out byte rough, out _);
                    gp[dst + x] = new Color32(0, 0, 0, ClampByte((1.0 - roughness * rough / 255.0) * 255.0));
                }
            });
            return new PixelImage { Name = name, Width = n, Height = n, Pixels = gp, Repeat = true, Linear = true, Aniso = RoomTextures.AnisoFor(name) };
        }

        /// <summary>Runtime fallback for an unbaked shared wood colour map (worker thread): Core's pixels and settings.</summary>
        public static PixelImage CoreWoodMap(in WoodParams p, string name)
        {
            int n = p.Size;
            var mp = new Color32[n * n];
            WoodParams wp = p;
            Rows(n, y =>
            {
                int dst = (n - 1 - y) * n;
                for (int x = 0; x < n; x++) { ProcTex.WoodPixel(wp, x, y, out var c, out _, out _); mp[dst + x] = c; }
            });
            return new PixelImage { Name = name, Width = n, Height = n, Pixels = mp, Repeat = true, Aniso = RoomTextures.AnisoFor(name) };
        }

        /// <summary>Runtime fallback for an unbaked Core/soft_dot (worker thread): Core's pixels, clamped.</summary>
        public static PixelImage CoreSoftDot(string name, int size = 64)
        {
            var c = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[(size - 1 - y) * size + x] = new Color32(255, 255, 255, ProcTex.SoftDotAlpha(size, x, y));
            return new PixelImage { Name = name, Width = size, Height = size, Pixels = c };
        }

        // ------------------------------------------------------------------ plaster

        public static PixelImage Plaster(string name = "Room/plaster", int size = 256)
        {
            double[] c = { 0.91, 0.86, 0.78 };   // the wall plaster's colour
            var px = new Color32[size * size];
            Rows(size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    double v = 0.92 + 0.08 * Fbm(x * 0.12, y * 0.12, 3);
                    px[(size - 1 - y) * size + x] = new Color32(ClampByte(c[0] * v * 255), ClampByte(c[1] * v * 255), ClampByte(c[2] * v * 255), 255);
                }
            });
            return new PixelImage { Name = name, Width = size, Height = size, Pixels = px, Repeat = true, Aniso = 4 };
        }

        // ------------------------------------------------------------------ rug

        public static PixelImage Rug(string name = "Room/rug", int size = 512)
        {
            var cv = new Canvas2D(size, size);
            cv.FillRect(0, 0, size, size, Rgba.Hex("#5e1c26"));
            var gold = Rgba.Hex("#c99a4b");
            cv.StrokeRect(22, 22, size - 44, size - 44, 6, gold);
            cv.StrokeRect(40, 40, size - 80, size - 80, 2, gold);
            var diamond = Paint.Solid(Rgba.Hex("#7a2a34"));
            const int step = 48;
            var poly = new List<float>(8);
            for (int y = 60; y < size - 60; y += step)
                for (int x = 60; x < size - 60; x += step)
                {
                    poly.Clear();
                    poly.Add(x + step / 2f); poly.Add(y + 6);
                    poly.Add(x + step - 6); poly.Add(y + step / 2f);
                    poly.Add(x + step / 2f); poly.Add(y + step - 6);
                    poly.Add(x + 6); poly.Add(y + step / 2f);
                    FillSmallPolygon(cv, poly, diamond);
                }
            byte[] img = cv.GetBytes();
            for (int p = 0; p < size * size; p++)
            {
                double n = 0.85 + 0.15 * VNoise((p % size) * 0.9, (p / size) * 0.9);
                int i = p * 4;
                img[i] = ClampByte(img[i] * n); img[i + 1] = ClampByte(img[i + 1] * n); img[i + 2] = ClampByte(img[i + 2] * n);
            }
            var outImg = PixelImage.FromCanvasBytes(name, size, size, img);
            outImg.Aniso = 4;
            return outImg;
        }

        /// <summary>A small polygon filled with 4x4 supersampling inside its own bounding box only.</summary>
        static void FillSmallPolygon(Canvas2D cv, List<float> xy, Paint p)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < xy.Count; i += 2)
            {
                minX = Math.Min(minX, xy[i]); maxX = Math.Max(maxX, xy[i]);
                minY = Math.Min(minY, xy[i + 1]); maxY = Math.Max(maxY, xy[i + 1]);
            }
            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(cv.W - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(cv.H - 1, (int)Math.Ceiling(maxY));
            int n = xy.Count / 2;
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (Inside(xy, n, px + (sx + 0.5f) / 4f, py + (sy + 0.5f) / 4f)) hits++;
                    if (hits > 0) cv.Blend(px, py, hits / 16f, p.At(px + 0.5f, py + 0.5f));
                }
        }

        static bool Inside(List<float> xy, int n, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = xy[2 * i], yi = xy[2 * i + 1], xj = xy[2 * j], yj = xy[2 * j + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
            }
            return c;
        }

        // ------------------------------------------------------------------ leaf

        public static PixelImage Leaf(bool dark, string name)
        {
            const int w = 128, h = 256;
            Rgba light = Rgba.Hex(dark ? "#5f9a4a" : "#7fb35a"), deep = Rgba.Hex(dark ? "#1f4d24" : "#2f6a2e");
            var cv = new Canvas2D(w, h);
            // Outline: two cubic Beziers from the stalk (w/2, h-4) to the tip (w/2, 4) and back.
            var outline = new List<float> { w / 2f, h - 4 };
            Canvas2D.Bezier(outline, w / 2f, h - 4, 4, h * 0.72f, 4, h * 0.2f, w / 2f, 4);
            Canvas2D.Bezier(outline, w / 2f, 4, w - 4, h * 0.2f, w - 4, h * 0.72f, w / 2f, h - 4);
            float[] clip = cv.PolygonCoverage(outline);
            cv.ApplyCoverage(clip, Paint.Linear(0, 0, w, h).Stop(0f, light).Stop(1f, deep));
            cv.Clip(clip);
            var rim = Paint.Radial(w / 2f, h * 0.45f, w * 0.1f, w * 0.95f)
                .Stop(0f, Rgba.Bytes(255, 255, 220, 0.18)).Stop(0.7f, Rgba.Bytes(0, 0, 0, 0)).Stop(1f, Rgba.Bytes(0, 20, 0, 0.35));
            cv.FillRect(0, 0, w, h, rim);
            var vein = Rgba.Bytes(235, 245, 200, 0.35);
            cv.StrokePolylines(new List<float[]> { new float[] { w / 2f, h - 4, w / 2f, 10 } }, 3f, vein);
            for (int i = 1; i < 14; i++)
            {
                float y = 16 + (i / 14f) * (h - 32);
                cv.StrokePolylines(new List<float[]>
                {
                    Canvas2D.Quadratic(w / 2f, y, w * 0.3f, y - 14, 10, y - 40),
                    Canvas2D.Quadratic(w / 2f, y, w * 0.7f, y - 14, w - 10, y - 40),
                }, 1.5f, vein);
            }
            cv.ResetClip();
            byte[] img = cv.GetBytes();
            for (int p = 0; p < w * h; p++)
            {
                int i = p * 4;
                if (img[i + 3] == 0) continue;
                double n = 0.9 + 0.1 * VNoise((p % w) * 0.15, (p / w) * 0.15);
                img[i] = ClampByte(img[i] * n); img[i + 1] = ClampByte(img[i + 1] * n); img[i + 2] = ClampByte(img[i + 2] * n);
            }
            return PixelImage.FromCanvasBytes(name, w, h, img);
        }

        // ------------------------------------------------------------------ atlases (spec 07 section 11)

        /// <summary>Both leaf variants side by side (light | dark, 128 x 256 each), so every plant's leaves are one
        /// renderer. The leaves' outlines stay 4 px inside each half, so the halves never bleed into each other.</summary>
        public static PixelImage LeafAtlas(string name = "Room/leaf_atlas")
        {
            PixelImage light = Leaf(false, "leaf_light"), dark = Leaf(true, "leaf_dark");
            int w = light.Width, h = light.Height;
            var px = new Color32[2 * w * h];
            for (int y = 0; y < h; y++)
            {
                Array.Copy(light.Pixels, y * w, px, y * 2 * w, w);
                Array.Copy(dark.Pixels, y * w, px, y * 2 * w + w, w);
            }
            return new PixelImage { Name = name, Width = 2 * w, Height = h, Pixels = px };
        }

        public const int SceneryW = 512, SceneryH = 384, SceneryGutter = 16;
        public const int SceneryAtlasH = 3 * SceneryH + 2 * SceneryGutter;

        /// <summary>Texture row of scenery slot <paramref name="slot"/>'s bottom (Unity row order, slot 0 at the bottom).</summary>
        public static int ScenerySlotRow(int slot) => slot * (SceneryH + SceneryGutter);

        /// <summary>The three paintings (seeds 1, 2, 3 in slots 0, 1, 2) stacked in one 512 x 1184 atlas, so the canvases
        /// are one renderer. The 16-row gutters repeat each slot's edge row, so filtering and the first mips never mix
        /// two paintings.</summary>
        public static PixelImage SceneryAtlas(string name = "Room/scenery_atlas")
        {
            int w = SceneryW, h = SceneryAtlasH;
            var px = new Color32[w * h];
            for (int slot = 0; slot < 3; slot++)
            {
                PixelImage img = Scenery(slot + 1, null, SceneryW, SceneryH);
                int row0 = ScenerySlotRow(slot);
                Array.Copy(img.Pixels, 0, px, row0 * w, w * SceneryH);
                int half = SceneryGutter / 2;
                if (slot > 0) for (int g = 1; g <= half; g++) Array.Copy(img.Pixels, 0, px, (row0 - g) * w, w);                          // below: bottom row
                if (slot < 2) for (int g = 0; g < half; g++) Array.Copy(img.Pixels, (SceneryH - 1) * w, px, (row0 + SceneryH + g) * w, w); // above: top row
            }
            return new PixelImage { Name = name, Width = w, Height = h, Pixels = px };
        }

        // ------------------------------------------------------------------ brick

        public static PixelImage Brick(string name = "Room/brick", int size = 512)
        {
            var cv = new Canvas2D(size, size);
            cv.FillRect(0, 0, size, size, Rgba.Hex("#b9ada0"));
            float bw = size / 6f, bh = size / 16f, m = 5f;
            for (int row = 0; row < 16; row++)
            {
                float off = row % 2 != 0 ? bw / 2f : 0f;
                for (int col = -1; col < 7; col++)
                {
                    double t = Hash(col * 13 + row * 7, 21);
                    // rgb(${r | 0}, ...): the channel values are truncated.
                    var c = Rgba.Bytes((int)(110 + t * 40), (int)(55 + t * 25), (int)(45 + t * 18));
                    cv.FillRect(col * bw + off + m / 2f, row * bh + m / 2f, bw - m, bh - m, c);
                }
            }
            byte[] img = cv.GetBytes();
            for (int p = 0; p < size * size; p++)
            {
                double n = 0.85 + 0.15 * VNoise((p % size) * 0.4, (p / size) * 0.4);
                int i = p * 4;
                img[i] = ClampByte(img[i] * n); img[i + 1] = ClampByte(img[i + 1] * n); img[i + 2] = ClampByte(img[i + 2] * n);
            }
            var o = PixelImage.FromCanvasBytes(name, size, size, img);
            o.Repeat = true; o.Aniso = 4;
            return o;
        }

        // ------------------------------------------------------------------ weave as a normal map

        /// <summary>The weave height h = .5 + .25 sin(.8x) + .25 sin(.8y) + .15 (vnoise(.5x, .5y) - .5) at canvas pixel (x, y).</summary>
        public static double WeaveHeight(int x, int y) => 0.5 + 0.25 * Math.Sin(x * 0.8) + 0.25 * Math.Sin(y * 0.8) + 0.15 * (VNoise(x * 0.5, y * 0.5) - 0.5);

        /// <summary>The spec uses the weave as a bump map (bumpScale .4); URP Lit takes a tangent-space normal map: Sobel of the
        /// stored height bytes (wrapping, the texture tiles), strength 2: the steepest threads tilt about 22 degrees.</summary>
        public static PixelImage WeaveNormal(string name = "Room/weave_normal", int size = 128)
        {
            var hgt = new double[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) hgt[y * size + x] = ClampByte(WeaveHeight(x, y) * 255) / 255.0;
            var px = new Color32[size * size];
            const double strength = 2.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double H(int dx, int dy) => hgt[((y + dy + size) % size) * size + (x + dx + size) % size];
                    double gx = (H(1, -1) + 2 * H(1, 0) + H(1, 1)) - (H(-1, -1) + 2 * H(-1, 0) + H(-1, 1));
                    double gy = (H(-1, 1) + 2 * H(0, 1) + H(1, 1)) - (H(-1, -1) + 2 * H(0, -1) + H(1, -1));
                    // Canvas y runs down the image; texture v runs up: the v-gradient flips sign.
                    var n = new Vector3((float)(-gx / 8 * strength), (float)(gy / 8 * strength), 1f).normalized;
                    px[(size - 1 - y) * size + x] = new Color32(ClampByte((n.x * 0.5 + 0.5) * 255), ClampByte((n.y * 0.5 + 0.5) * 255), ClampByte((n.z * 0.5 + 0.5) * 255), 255);
                }
            return new PixelImage { Name = name, Width = size, Height = size, Pixels = px, Linear = true, Repeat = true, NormalMap = true };
        }

        // ------------------------------------------------------------------ scenery paintings

        public static PixelImage Scenery(int seed, string name = null, int w = 512, int h = 384)
        {
            string[] kinds = { "lake", "hills", "coast" };
            string kind = kinds[seed % 3];
            var pal = SceneryPalettes[seed % 3];
            var cv = new Canvas2D(w, h);
            float horizon = h * (kind == "coast" ? 0.5f : 0.55f);
            cv.FillRect(0, 0, w, h, Paint.Linear(0, 0, 0, horizon).Stop(0f, Rgba.Hex(pal.Top)).Stop(0.6f, Rgba.Hex(pal.Mid)).Stop(1f, Rgba.Hex(pal.Hor)));
            // Sun.
            float sx = (float)(w * (0.3 + Hash(seed, 3) * 0.4)), sy = (float)(horizon * (0.55 + Hash(seed, 4) * 0.3));
            var sunCol = Rgba.Hex(pal.Sun);
            cv.FillRect(0, 0, w, h, Paint.Radial(sx, sy, 0, 90).Stop(0f, sunCol).Stop(0.12f, sunCol)
                .Stop(0.3f, Rgba.Bytes(255, 230, 180, 0.35)).Stop(1f, Rgba.Bytes(255, 230, 180, 0)));
            // Hills, far to near.
            const int layers = 4;
            int nx = w / 4 + 1;
            for (int k = 0; k < layers; k++)
            {
                double f = (double)k / (layers - 1);
                var col = Rgba.Bytes(Math.Round(Lerp(pal.Far[0], pal.Near[0], f)), Math.Round(Lerp(pal.Far[1], pal.Near[1], f)), Math.Round(Lerp(pal.Far[2], pal.Near[2], f)));
                float bas = horizon - 10 + k * 22;
                double amp = kind == "hills" ? 30 + k * 14 : 55 + k * 10;
                var rx = new float[nx + 2];
                var ry = new float[nx + 2];
                rx[0] = 0; ry[0] = h;                                   // moveTo(0, h)
                for (int i = 0; i < nx; i++)
                {
                    int x = i * 4;
                    rx[i + 1] = x;
                    ry[i + 1] = (float)(bas - Fbm(x * (0.004 + k * 0.002) + seed * 10 + k * 7, k, 3) * amp * 1.6);
                }
                rx[nx + 1] = w; ry[nx + 1] = h;                        // lineTo(w, h)
                FillRidgePath(cv, rx, ry, col);
            }
            // Water or meadow in the foreground.
            float fy = h * 0.72f;
            if (kind != "hills")
            {
                cv.FillRect(0, fy, w, h - fy, Paint.Linear(0, fy, 0, h).Stop(0f, Rgba.Hex(pal.Hor)).Stop(0.3f, Rgba.Hex(pal.Mid)).Stop(1f, Rgba.Hex(pal.Top)));
                var streak = Rgba.Bytes(255, 240, 210, 0.35);
                for (int i = 0; i < 40; i++)
                {
                    float y = (float)(fy + Hash(i, seed) * (h - fy));
                    float x0 = (float)(Hash(i + 3, seed) * w), len = (float)(20 + Hash(i + 9, seed) * 60);
                    float lw = (float)(1 + Hash(i + 5, seed) * 1.5);
                    cv.StrokePolylines(new List<float[]> { new[] { x0, y, x0 + len, y } }, lw, streak);
                }
            }
            else
            {
                cv.FillRect(0, fy - 10, w, h - fy + 10, Paint.Linear(0, fy, 0, h).Stop(0f, Rgba.Hex("#7d9a4e")).Stop(1f, Rgba.Hex("#3f5a2c")));
            }
            // Trees on the sides.
            for (int i = 0; i < 14; i++)
            {
                int side = i % 2 != 0 ? 1 : -1;
                float x = (float)(w / 2.0 + side * (w * 0.25 + Hash(i, seed + 1) * w * 0.25));
                float y = (float)(fy + 6 + Hash(i, seed + 2) * (h - fy) * 0.5);
                float s = (float)(14 + Hash(i, seed + 3) * 26);
                var c = Rgba.Bytes((int)(30 + Hash(i, 4) * 20), (int)(55 + Hash(i, 5) * 25), 30);
                cv.FillEllipse(x, y - s, s * 0.6f, s, c);
                cv.FillRect(x - 2, y - s * 0.3f, 4, s * 0.6f, c);
            }
            // Brushwork, canvas weave, vignette.
            byte[] img = cv.GetBytes();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    double stroke = 0.94 + 0.06 * VNoise(x * 0.25, y * 0.9);
                    double weave = 0.97 + 0.03 * (Math.Sin(x * 1.6) * Math.Sin(y * 1.6));
                    double dx = (double)x / w - 0.5, dy = (double)y / h - 0.5;
                    double vig = 1 - 0.35 * (dx * dx + dy * dy) * 2.2;
                    double k = stroke * weave * vig;
                    img[i] = ClampByte(img[i] * k); img[i + 1] = ClampByte(img[i + 1] * k); img[i + 2] = ClampByte(img[i + 2] * k);
                }
            return PixelImage.FromCanvasBytes(name ?? "Room/scenery_" + seed, w, h, img);
        }

        struct SceneryPalette { public string Top, Mid, Hor, Sun; public double[] Far, Near; }

        static readonly SceneryPalette[] SceneryPalettes =
        {
            new SceneryPalette { Top = "#2b3f6b", Mid = "#8fa9c9", Hor = "#f0c48a", Sun = "#fff1c4", Far = new double[] { 150, 165, 190 }, Near = new double[] { 45, 80, 50 } },
            new SceneryPalette { Top = "#6f8fc0", Mid = "#bcd0e6", Hor = "#f4e2c0", Sun = "#ffffff", Far = new double[] { 160, 170, 195 }, Near = new double[] { 60, 95, 55 } },
            new SceneryPalette { Top = "#3a2f5a", Mid = "#c37a6a", Hor = "#f7c48a", Sun = "#ffe8b0", Far = new double[] { 120, 110, 140 }, Near = new double[] { 50, 60, 60 } },
        };

        /// <summary>A hill path (0, h) -> ridge points -> (w, h): the region below the ridge polyline.</summary>
        static void FillRidgePath(Canvas2D cv, float[] xs, float[] ys, Rgba c)
        {
            // Drop the two corner points: FillBelowRidge fills down to the canvas bottom.
            var rx = new float[xs.Length - 2];
            var ry = new float[ys.Length - 2];
            Array.Copy(xs, 1, rx, 0, rx.Length);
            Array.Copy(ys, 1, ry, 0, ry.Length);
            cv.FillBelowRidge(rx, ry, c);
        }

        // ------------------------------------------------------------------ night sky

        public static PixelImage NightSky(string name = "Room/night_sky", int w = 2048, int h = 1024)
        {
            var cv = new Canvas2D(w, h);
            cv.FillRect(0, 0, w, h, Paint.Linear(0, 0, 0, h).Stop(0f, Rgba.Hex("#040814")).Stop(0.55f, Rgba.Hex("#0b1430")).Stop(0.8f, Rgba.Hex("#182448")).Stop(1f, Rgba.Hex("#223058")));
            for (int i = 0; i < 1100; i++)
            {
                float x = (float)(Hash(i, 1) * w), y = (float)(Hash(i, 2) * h * 0.78);
                float r = (float)(0.8 + Math.Pow(Hash(i, 3), 3) * 3.2);
                double a = 0.6 + Hash(i, 4) * 0.4;
                cv.FillCircle(x, y, r, Hash(i, 5) > 0.85 ? Rgba.Bytes(200, 220, 255, a) : Rgba.Bytes(255, 250, 235, a));
            }
            // Thin cloud band (getImageData, edit, putImageData).
            byte[] cimg = cv.GetBytes();
            int yStart = (int)Math.Floor(h * 0.3);
            Rows(h, y =>
            {
                if (y < yStart || y >= h * 0.6) return;
                double band = Math.Exp(-Math.Pow((y - h * 0.45) / (h * 0.08), 2));
                for (int x = 0; x < w; x++)
                {
                    double n = Fbm(x * 0.003, y * 0.012, 3);
                    double a = Math.Max(0, n - 0.5) * band * 0.5;
                    if (a <= 0) continue;
                    int i = (y * w + x) * 4;
                    cimg[i] = ClampByte(cimg[i] + (90 - cimg[i]) * a);
                    cimg[i + 1] = ClampByte(cimg[i + 1] + (100 - cimg[i + 1]) * a);
                    cimg[i + 2] = ClampByte(cimg[i + 2] + (140 - cimg[i + 2]) * a);
                }
            });
            cv.PutBytes(cimg);
            // Moon: halo, disc, craters.
            float mx = w * 0.62f, my = h * 0.42f;
            cv.FillRect(0, 0, w, h, Paint.Radial(mx, my, 20, 160).Stop(0f, Rgba.Bytes(220, 230, 255, 0.35)).Stop(1f, Rgba.Bytes(220, 230, 255, 0)));
            cv.FillCircle(mx, my, 26, Rgba.Hex("#f2f3ea"));
            for (int i = 0; i < 6; i++)
                cv.FillCircle((float)(mx - 12 + Hash(i, 7) * 24), (float)(my - 12 + Hash(i, 8) * 24), (float)(2 + Hash(i, 9) * 5), Rgba.Bytes(180, 185, 175, 0.5));
            // Hills, two layers: moveTo(0, h), lineTo(x, ridge) every 6 px, lineTo(w, h), fill.
            HillRidge(cv, w, h, 0, Rgba.Hex("#0d1226"), 0.82, 0.09);
            HillRidge(cv, w, h, 1, Rgba.Hex("#070a16"), 0.9, 0.06);
            for (int i = 0; i < 160; i++)
            {
                float x = (float)(Hash(i, 11) * w), y = (float)(h * (0.9 + Hash(i, 12) * 0.08));
                cv.FillRect(x, y, (float)(1.5 + Hash(i, 14) * 1.5), 1.5f, Hash(i, 13) > 0.3 ? Rgba.Bytes(255, 205, 130, 0.9) : Rgba.Bytes(200, 225, 255, 0.8));
            }
            var o = PixelImage.FromCanvasBytes(name, w, h, cv.GetBytes());
            o.Aniso = 8;
            return o;
        }

        static void HillRidge(Canvas2D cv, int w, int h, int k, Rgba col, double bas, double amp)
        {
            int n = (w - 1) / 6 + 1;   // x = 0, 6, ..., <= w
            var xs = new float[n + 1];
            var ys = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                int x = i * 6;
                xs[i] = x;
                ys[i] = (float)(h * bas - Fbm(x * 0.0012 + k * 5, k, 3) * h * amp);
            }
            xs[n] = w; ys[n] = h;   // the closing lineTo(w, h)
            cv.FillBelowRidge(xs, ys, col);
        }

        // ------------------------------------------------------------------ hill snow

        /// <summary>One hill-snow pixel (canvas coordinates): rgba bytes.</summary>
        public static Color32 HillSnowPixel(int x, int y, int w = 1024, int h = 512)
        {
            const double W = 2048, H = 1024;
            double sx = W / w, sy = H / h;
            double X = x * sx;
            double r0 = H * 0.82 - Fbm(X * 0.0012, 0, 3) * H * 0.09;
            double r1 = H * 0.9 - Fbm(X * 0.0012 + 5, 1, 3) * H * 0.06;
            if (y < Math.Max(0, Math.Floor(r0 / sy) - 1)) return default;
            double Y = y * sy;
            if (Y < r0) return default;
            bool near = Y >= r1;
            double u = near ? Y - r1 : Y - r0;
            double patch = Fbm(X * 0.05 + (near ? 9 : 0), Y * 0.1, 3) * 0.7 + VNoise(X * 0.35, Y * 0.35) * 0.3;
            double cut = (near ? 0.36 : 0.3) + Math.Min(0.12, u * 0.0015);
            double a = near ? 0.5 * Math.Exp(-u / 18) + 0.5 : 0.45 * Math.Exp(-u / 26) + 0.55;
            a *= Math.Min(1, Math.Max(0, (patch - cut) * 5));
            a *= Math.Min(1, (u + 1) / (2 * sy));
            return new Color32((byte)(near ? 92 : 112), (byte)(near ? 104 : 126), (byte)(near ? 140 : 162), (byte)MusicRoom.Core.Mulberry32.JsRound(Math.Min(1, a) * 255));
        }

        public static PixelImage HillSnow(string name = "Room/hill_snow", int w = 1024, int h = 512)
        {
            var px = new Color32[w * h];
            Rows(h, y =>
            {
                int dst = (h - 1 - y) * w;
                for (int x = 0; x < w; x++) px[dst + x] = HillSnowPixel(x, y, w, h);
            });
            return new PixelImage { Name = name, Width = w, Height = h, Pixels = px };
        }

        // ------------------------------------------------------------------ books palette

        /// <summary>The 10 book tints HSL(k/10, .45, .4), computed in the linear-sRGB working space (HslLinear), stored
        /// sRGB-encoded in a 10 x 1 point-filtered palette (URP Lit ignores vertex colours).</summary>
        public static PixelImage BookPalette(string name = "books")
        {
            var px = new Color32[10];
            for (int k = 0; k < 10; k++)
            {
                HslLinear(k / 10.0, 0.45, 0.4, out double r, out double g, out double b);
                px[k] = new Color32(ClampByte(LinearToSrgb(r) * 255), ClampByte(LinearToSrgb(g) * 255), ClampByte(LinearToSrgb(b) * 255), 255);
            }
            return new PixelImage { Name = name, Width = 10, Height = 1, Pixels = px, Mipmaps = false, PointFilter = true };
        }

        /// <summary>HSL to RGB with the working space = linear sRGB.</summary>
        public static void HslLinear(double h, double s, double l, out double r, out double g, out double b)
        {
            h = ((h % 1) + 1) % 1;
            double p = l <= 0.5 ? l * (1 + s) : l + s - l * s;
            double q = 2 * l - p;
            r = Hue2Rgb(q, p, h + 1.0 / 3);
            g = Hue2Rgb(q, p, h);
            b = Hue2Rgb(q, p, h - 1.0 / 3);
        }

        static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * 6 * (2.0 / 3 - t);
            return p;
        }

        public static double LinearToSrgb(double c) => c < 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 0.41666) - 0.055;
        public static double SrgbToLinear(double c) => c < 0.04045 ? c * 0.0773993808 : Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);
    }
}
