// The note-name stickers (drawLabel, buildLabels; Docs/05-instruments.md piano.build.labels): one atlas of the 19
// distinct names, 8 columns of 128 px cells, each cell mapped onto its key's quad so one cell unit is one millimetre on
// the key. White keys get a pale rounded sticker rgb(252,249,241) with dark ink #2a2420, the octave digit set smaller after
// every C; black keys get their name in pale ink rgb(240,232,214) with a raised sharp sign; middle C wears the same sticker
// with a thin coral rgb(238,132,108) bar under its name. Glyphs are drawn 1.5x taller along the key (the seat sees the key
// top at about 40 degrees).
// How it is made (ARCHITECTURE.md 15.1, "a TMP render-to-texture bake"): TextMeshPro lays the pieces out (its bold
// LiberationSans metrics give the widths and the cap height) and renders their coverage once into
// a render texture; the stickers, the edge, the ink colours, the coral bar and the "bleed the fill colour into clear
// texels" step are composited on the CPU so they are exact and need no GPU. Without a GPU (batchmode -nographics) the atlas has
// the stickers and the bar but no glyphs. Built once per session and cached.
using System.Collections.Generic;
using MusicRoom.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Piano
{
    public static class PianoLabelAtlas
    {
        public const int CellPx = 128, Cols = 8;
        public const float LabelLift = 0.0003f;   // the labels float this far above the key top, so they never z-fight
        public const float Stretch = 1.5f;
        // Layers.Hidden: never drawn by the head camera (and not PanelUI, 31, which is the menu's offscreen canvas). The bake
        // objects sit 5 km from the room and live for one render only, so no other Hidden helper is in the bake camera's view.
        public const int BakeLayer = Layers.Hidden;

        public struct Quad { public float W, Front, Rear; }   // metres; Front / Rear are distances from the hinge
        public static readonly Quad WhiteQuad = new Quad { W = 0.017f, Front = PianoSpec.L - 0.004f, Rear = PianoSpec.L - 0.028f };
        public static readonly Quad BlackQuad = new Quad { W = 0.01f, Front = PianoSpec.BL - 0.004f, Rear = PianoSpec.BL - 0.022f };

        public static readonly Color32 Sticker = new Color32(252, 249, 241, 255);
        public static readonly Color32 MiddleCColour = new Color32(238, 132, 108, 255);
        public static readonly Color32 SharpInk = new Color32(240, 232, 214, 255);
        public static readonly Color32 Ink = new Color32(0x2a, 0x24, 0x20, 255);
        static readonly Color32 EdgeInk = new Color32(120, 110, 100, 255);
        const float EdgeAlpha = 0.22f, EdgeWidthMm = 0.2f, StickerInsetMm = 0.5f, StickerCornerMm = 1.5f;

        public sealed class Cell
        {
            public int Index;
            public string Key, Text, Name, Mark, Digit;
            public bool Black, Marked;
        }

        /// <summary>One distinct (text, black, middle C) per cell, in the order of the keys (A0 first): 19 cells.</summary>
        public static readonly List<Cell> Cells = BuildCells();
        public static int Rows => (Cells.Count + Cols - 1) / Cols;
        public static int Width => Cols * CellPx;
        public static int Height => Rows * CellPx;

        static Dictionary<string, int> s_byKey;
        static Texture2D s_atlas;
        static bool s_atlasHasGlyphs;

        static List<Cell> BuildCells()
        {
            var cells = new List<Cell>();
            s_byKey = new Dictionary<string, int>();
            for (int m = PianoSpec.FirstMidi; m <= PianoSpec.LastMidi; m++)
            {
                string key = PianoSpec.LabelCellKey(m);
                if (s_byKey.ContainsKey(key)) continue;
                string text = PianoSpec.LabelText(m);
                // name, optional raised mark, optional octave digit (/^([A-Za-z]+)([#']?)(\d*)$/).
                int i = 0;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                string name = text.Substring(0, i), mark = "", digit = "";
                if (i < text.Length && text[i] == '#') { mark = "#"; i++; }
                if (i < text.Length) digit = text.Substring(i);
                s_byKey[key] = cells.Count;
                cells.Add(new Cell { Index = cells.Count, Key = key, Text = text, Name = name, Mark = mark, Digit = digit, Black = PianoSpec.IsBlack(m), Marked = m == PianoSpec.MiddleC });
            }
            return cells;
        }

        public static int CellOf(int midi) => s_byKey.TryGetValue(PianoSpec.LabelCellKey(midi), out int c) ? c : -1;

        /// <summary>The cell's uv rectangle (u0, v0, u1, v1) with rows counted from the top of the atlas.</summary>
        public static Vector4 CellUv(int cell)
        {
            float u0 = (cell % Cols) / (float)Cols, u1 = u0 + 1f / Cols;
            float v1 = 1f - (cell / Cols) / (float)Rows, v0 = v1 - 1f / Rows;
            return new Vector4(u0, v0, u1, v1);
        }

        public static bool HasGlyphs => s_atlas != null && s_atlasHasGlyphs;

        /// <summary>The session's atlas (sRGB, trilinear, aniso 8, mips), built on first use.</summary>
        public static Texture2D Get()
        {
            if (s_atlas != null) return s_atlas;
            s_atlas = Build(out s_atlasHasGlyphs);
            return s_atlas;
        }

        // ------------------------------------------------------------------------------------------------------------

        /// <summary>A run of text drawn with one size: the pen origin and baseline (cell mm, canvas y down) and the em in mm.</summary>
        public struct Run { public int Cell; public string Text; public float PenX, Baseline, EmX, EmY; }

        /// <summary>Per cell: the em (mm) after shrink-to-fit, the total width in em, the cap height and the runs.</summary>
        public struct Layout { public float Em, Total, Cap, Baseline; public float W, H; }

        /// <summary>drawLabel's layout for every cell, measured with <paramref name="advanceEm"/> (text -> advance in em) and
        /// <paramref name="ascentEm"/> (name -> ink ascent in em).</summary>
        public static void LayoutCells(System.Func<string, float> advanceEm, System.Func<string, float> ascentEm, List<Run> runs, Layout[] layouts)
        {
            runs.Clear();
            foreach (var c in Cells)
            {
                var q = c.Black ? BlackQuad : WhiteQuad;
                float w = q.W * 1000f, h = (q.Front - q.Rear) * 1000f;
                float markK = c.Mark == "#" ? 0.62f : 0.9f, digitK = c.Black ? 0.7f : 0.75f;
                float total = advanceEm(c.Name);
                if (c.Mark.Length > 0) total += 0.03f + advanceEm(c.Mark) * markK;
                if (c.Digit.Length > 0) total += 0.05f + advanceEm(c.Digit) * digitK;
                float em = c.Black ? 5.9f : 10f;
                float maxW = w * (c.Black ? 0.92f : 0.88f);
                if (em * total > maxW) em = maxW / total;
                float cap = ascentEm(c.Name) * em * Stretch;
                float baseline = h / 2f + cap / 2f;
                float cx = (w - em * total) / 2f;
                runs.Add(new Run { Cell = c.Index, Text = c.Name, PenX = cx, Baseline = baseline, EmX = em, EmY = em * Stretch });
                cx += advanceEm(c.Name) * em;
                if (c.Mark.Length > 0)
                {
                    cx += 0.03f * em;
                    float rise = c.Mark == "#" ? 0.35f : 0f;
                    runs.Add(new Run { Cell = c.Index, Text = c.Mark, PenX = cx, Baseline = baseline - rise * cap, EmX = em * markK, EmY = em * markK * Stretch });
                    cx += advanceEm(c.Mark) * markK * em;
                }
                if (c.Digit.Length > 0)
                {
                    cx += 0.05f * em;
                    runs.Add(new Run { Cell = c.Index, Text = c.Digit, PenX = cx, Baseline = baseline, EmX = em * digitK, EmY = em * digitK * Stretch });
                }
                layouts[c.Index] = new Layout { Em = em, Total = total, Cap = cap, Baseline = baseline, W = w, H = h };
            }
        }

        /// <summary>Bold sans advance widths (em) for when no font is available: only the coral bar's width depends on them.</summary>
        static float FallbackAdvance(string t)
        {
            float a = 0f;
            foreach (char ch in t) a += ch == '#' ? 0.556f : char.IsDigit(ch) ? 0.556f : ch == 'F' ? 0.611f : ch == 'E' ? 0.667f : ch == 'G' ? 0.778f : 0.722f;
            return a;
        }

        /// <summary>Where the last Build spent its time (ms): TMP font load + layout, the glyph render and readback, the CPU
        /// composite, the texture upload with mips. Logged once per build as "[Piano] label atlas ...".</summary>
        public struct BuildTimes { public double Layout, Glyphs, Compose, Upload; public double Total => Layout + Glyphs + Compose + Upload; }
        public static BuildTimes LastBuild { get; private set; }

        public static Texture2D Build(out bool glyphs)
        {
            int W = Width, H = Height;
            var runs = new List<Run>(48);
            var layouts = new Layout[Cells.Count];
            var coverage = new byte[W * H];
            glyphs = false;
            var times = new BuildTimes();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double Lap() { double ms = watch.Elapsed.TotalMilliseconds; watch.Restart(); return ms; }

            GameObject root = null;
            try
            {
                var font = TMP_Settings.defaultFontAsset;
                if (font != null)
                {
                    // Active (TextMeshPro lays text out only once awake), far from the room, on the Hidden layer (BakeLayer),
                    // and destroyed below before any frame is rendered.
                    root = new GameObject("PianoLabelBake");
                    root.transform.position = new Vector3(-5000f, -5000f, 0f);
                    var measure = MakeText(root.transform, font);
                    LayoutCells(t => MeasureAdvance(measure, t), t => MeasureAscent(font, t), runs, layouts);
                    times.Layout = Lap();
                    glyphs = RenderCoverage(root, font, runs, layouts, coverage, W, H);
                    times.Glyphs = Lap();
                }
                else
                {
                    Debug.LogWarning("[Piano] no TextMeshPro default font: the key labels have no letters");
                    LayoutCells(FallbackAdvance, _ => 0.72f, runs, layouts);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Piano] label glyphs not rendered: " + e.Message);
                glyphs = false;
                System.Array.Clear(coverage, 0, coverage.Length);
                if (layouts[0].W == 0f) LayoutCells(FallbackAdvance, _ => 0.72f, runs, layouts);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
            }
            double cleanup = Lap();   // destroying the bake objects (or, without a font, the fallback layout)
            if (times.Glyphs > 0) times.Glyphs += cleanup; else times.Layout += cleanup;
            var px = Compose(layouts, coverage, W, H);
            times.Compose = Lap();
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false)
            {
                name = "piano_labels", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8,
            };
            tex.SetPixels32(px);
            tex.Apply(true, !Application.isEditor);   // the editor keeps it readable for the atlas checks
            times.Upload = Lap();
            LastBuild = times;
            Debug.Log($"[Piano] label atlas {W}x{H} (glyphs={(glyphs ? "yes" : "no")}): font+layout {times.Layout:F1} ms, " +
                      $"glyph render+readback {times.Glyphs:F1} ms, compose {times.Compose:F1} ms, upload {times.Upload:F1} ms");
            return tex;
        }

        static TextMeshPro MakeText(Transform parent, TMP_FontAsset font)
        {
            var go = new GameObject("text");
            go.layer = BakeLayer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSize = 100f;
            t.fontStyle = FontStyles.Bold;
            t.isOrthographic = true;              // 1 em = fontSize local units (no 0.1 world scale)
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.margin = Vector4.zero;
            t.color = Color.white;
            t.rectTransform.pivot = new Vector2(0f, 1f);
            t.rectTransform.sizeDelta = new Vector2(1000f, 400f);
            return t;
        }

        /// <summary>Pen advance of a string in em (the text's advance width / F with F = 100).</summary>
        static float MeasureAdvance(TextMeshPro t, string text)
        {
            t.text = text;
            t.ForceMeshUpdate(true, true);
            var info = t.textInfo;
            if (info.characterCount == 0) return FallbackAdvance(text);
            float w = info.characterInfo[info.characterCount - 1].xAdvance - info.characterInfo[0].origin;
            return w / t.fontSize;
        }

        /// <summary>Ink ascent of a name in em (its ink bounding-box ascent / F): the tallest glyph's bearing above the baseline.</summary>
        static float MeasureAscent(TMP_FontAsset font, string text)
        {
            float a = 0f;
            foreach (char ch in text)
            {
                if (font.characterLookupTable != null && font.characterLookupTable.TryGetValue(ch, out var c) && c.glyph != null)
                    a = Mathf.Max(a, c.glyph.metrics.horizontalBearingY * font.faceInfo.scale / font.faceInfo.pointSize);
            }
            return a > 0f ? a : 0.72f;
        }

        /// <summary>Renders the glyph runs white on black into a render texture once and reads the red channel back as coverage.</summary>
        static bool RenderCoverage(GameObject root, TMP_FontAsset font, List<Run> runs, Layout[] layouts, byte[] coverage, int W, int H)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            Vector3 o = root.transform.position;
            foreach (var r in runs)
            {
                var lay = layouts[r.Cell];
                float sx = CellPx / lay.W, sy = CellPx / lay.H;   // px per mm across and along the key
                int col = r.Cell % Cols, row = r.Cell / Cols;
                var t = MakeText(root.transform, font);
                t.text = r.Text;
                t.ForceMeshUpdate(true, true);
                if (t.textInfo.characterCount == 0) continue;
                float ox = t.textInfo.characterInfo[0].origin, oy = t.textInfo.characterInfo[0].baseLine;
                float kx = r.EmX * sx / t.fontSize, ky = r.EmY * sy / t.fontSize;
                float penX = col * CellPx + r.PenX * sx, baseY = H - (row * CellPx + r.Baseline * sy);
                t.transform.localScale = new Vector3(kx, ky, 1f);
                t.transform.position = o + new Vector3(penX - ox * kx, baseY - oy * ky, 0f);
            }
            var camGo = new GameObject("bakeCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = o + new Vector3(W / 2f, H / 2f, -10f);
            camGo.transform.rotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = H / 2f;
            cam.aspect = W / (float)H;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 1f);
            cam.cullingMask = 1 << BakeLayer;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            // No stereoTargetEye (URP logs that it is a built-in-renderer setting): URP never renders a camera with a
            // target texture through XR (XRLayout.AddCamera: xrSupported needs camera.targetTexture == null).
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "PianoLabelBake", antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            Texture2D read = null;
            var prev = RenderTexture.active;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                RenderTexture.active = rt;
                read = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
                read.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                var px = read.GetPixels32();
                int inked = 0;
                for (int i = 0; i < px.Length; i++) { coverage[i] = px[i].r; if (px[i].r > 128) inked++; }
                return inked > 0;
            }
            finally
            {
                RenderTexture.active = prev;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                if (read != null) Object.DestroyImmediate(read);
            }
        }

        // ---- the CPU composite (straight alpha, source-over), then the bleed ----

        static Color32[] Compose(Layout[] layouts, byte[] coverage, int W, int H)
        {
            var px = new Color32[W * H];
            // Each cell reads only finished shared arrays and writes only its own texels, and the maths is plain C# (no
            // Unity API): the 24 cells compose in parallel on the worker pool, with exactly the serial result.
            System.Threading.Tasks.Parallel.For(0, Cols * Rows, cell => ComposeCell(cell, layouts, coverage, W, H, px));
            return px;
        }

        static void ComposeCell(int cell, Layout[] layouts, byte[] coverage, int W, int H, Color32[] px)
        {
            int col = cell % Cols, row = cell / Cols;
            bool real = cell < Cells.Count;
            Cell c = real ? Cells[cell] : null;
            Color32 fill = real && c.Black ? SharpInk : Sticker;
            Layout lay = real ? layouts[cell] : default;
            float w = lay.W, h = lay.H;
            float sx = real ? CellPx / w : 1f, sy = real ? CellPx / h : 1f;
            float uw = 0f, uh = 0.9f * Stretch, uy = 0f;
            if (real && c.Marked) { uw = lay.Em * lay.Total * 0.66f; uy = lay.Baseline + 1.4f * Stretch; }
            for (int cy = 0; cy < CellPx; cy++)
            {
                int ty = H - 1 - (row * CellPx + cy);
                for (int cx = 0; cx < CellPx; cx++)
                {
                    int tx = col * CellPx + cx;
                    float r = fill.r / 255f, g = fill.g / 255f, b = fill.b / 255f, a = 0f;
                    if (real)
                    {
                        float xm = (cx + 0.5f) / sx, ym = (cy + 0.5f) / sy;
                        if (!c.Black)
                        {
                            float d = RoundRectDistance(xm, ym, StickerInsetMm, StickerInsetMm, w - 2f * StickerInsetMm, h - 2f * StickerInsetMm, StickerCornerMm, sx, sy, out float pxPerMm);
                            float dpx = d * pxPerMm;
                            Over(ref r, ref g, ref b, ref a, Sticker, Mathf.Clamp01(0.5f - dpx));
                            float half = EdgeWidthMm * 0.5f * pxPerMm;
                            Over(ref r, ref g, ref b, ref a, EdgeInk, EdgeAlpha * Mathf.Clamp01(half + 0.5f - Mathf.Abs(dpx)));
                        }
                        byte cov = coverage[ty * W + tx];
                        if (cov > 0) Over(ref r, ref g, ref b, ref a, c.Black ? SharpInk : Ink, cov / 255f);
                        if (c.Marked)
                        {
                            float d = RoundRectDistance(xm, ym, (w - uw) / 2f, uy, uw, uh, uh / 2f, sx, sy, out float k);
                            Over(ref r, ref g, ref b, ref a, MiddleCColour, Mathf.Clamp01(0.5f - d * k));
                        }
                    }
                    byte a8 = (byte)Mathf.RoundToInt(a * 255f);
                    // The GPU averages texels straight (not premultiplied) for the mips: clear texels carry the cell's fill.
                    px[ty * W + tx] = a8 == 0 ? new Color32(fill.r, fill.g, fill.b, 0)
                        : new Color32((byte)Mathf.RoundToInt(r * 255f), (byte)Mathf.RoundToInt(g * 255f), (byte)Mathf.RoundToInt(b * 255f), a8);
                }
            }
        }

        /// <summary>Signed distance (mm, negative inside) to the rounded rectangle (x, y, w, h, radius) and the px per mm along its
        /// normal for a cell scaled sx, sy px/mm (the cell maps non-uniformly onto the key's quad).</summary>
        static float RoundRectDistance(float px, float py, float x, float y, float w, float h, float radius, float sx, float sy, out float pxPerMm)
        {
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2f);
            float cxm = x + w / 2f, cym = y + h / 2f;
            float qx = Mathf.Abs(px - cxm) - (w / 2f - radius), qy = Mathf.Abs(py - cym) - (h / 2f - radius);
            float nx, ny, d;
            if (qx > 0f && qy > 0f)
            {
                float len = Mathf.Sqrt(qx * qx + qy * qy);
                d = len - radius;
                nx = qx / len; ny = qy / len;
            }
            else
            {
                d = Mathf.Max(qx, qy) - radius;
                if (qx > qy) { nx = 1f; ny = 0f; } else { nx = 0f; ny = 1f; }
            }
            // A step of one pixel moves 1/sx mm across, 1/sy mm along: the distance in px is d / |(nx/sx, ny/sy)|.
            float gx = nx / sx, gy = ny / sy;
            pxPerMm = 1f / Mathf.Max(1e-6f, Mathf.Sqrt(gx * gx + gy * gy));
            return d;
        }

        static void Over(ref float r, ref float g, ref float b, ref float a, Color32 c, float sa)
        {
            if (sa <= 0f) return;
            float oa = sa + a * (1f - sa);
            float k = a * (1f - sa);
            r = (c.r / 255f * sa + r * k) / oa;
            g = (c.g / 255f * sa + g * k) / oa;
            b = (c.b / 255f * sa + b * k) / oa;
            a = oa;
        }
    }
}
