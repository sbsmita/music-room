// The book's text face (the Node sheet lab's text module): the project's own LiberationSans (TextMesh Pro's face, SIL
// OFL 1.1) as flattened outlines, emboldened like TMP's synthetic bold by offsetting every contour outward, laid out with
// kerning, and turned into filled contours for the page rasteriser (printed text is ink in the page texture, sheet.md
// 7.5). The outlines come from Resources/sheet-font.bytes, which SheetFontTable.Build writes from the TTF:
//   'SFNT' u32 version = 2, f64 capHeightEm, u16 glyphCount, u16 kernCount
//   per glyph: u32 codepoint, f64 advanceEm, u16 contourCount, per contour: u16 pointCount, f64 x, f64 y ... (em, y up)
//   per kern pair: u32 left, u32 right, f64 kernEm            (little endian)
// Version 2 stores doubles (the lab's export-font.mjs wrote float32, version 1) so the C# pages are bit-identical with the
// lab's, which flattens the TTF in double precision.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicRoom.Sheet.Engrave
{
    /// <summary>One styled run of text (text.mjs runs: { s, scale = 1, rise = 0 }; rise in em of the base size, upward).</summary>
    public struct TextRun
    {
        public string S; public double Scale; public double Rise;
        public TextRun(string s, double scale = 1, double rise = 0) { S = s; Scale = scale; Rise = rise; }
    }

    public sealed class SheetFont
    {
        public const string ResourceName = "sheet-font";
        public const double FlattenTolerance = 0.0015;

        sealed class GlyphData { public double Adv; public double[][] Contours; }   // contours: flat x, y pairs (em, y up)

        readonly Dictionary<int, GlyphData> _glyphs = new Dictionary<int, GlyphData>();
        readonly Dictionary<long, double> _kern = new Dictionary<long, double>();
        readonly Dictionary<(int, double), double[][]> _bold = new Dictionary<(int, double), double[][]>();
        readonly object _lock = new object();

        public double CapHeightEm { get; private set; }
        public int GlyphCount => _glyphs.Count;
        public int KernCount => _kern.Count;
        /// <summary>Characters asked for that the table does not have (printed as a space).</summary>
        public readonly HashSet<int> Missing = new HashSet<int>();

        public bool Has(char c) => _glyphs.ContainsKey(c);

        public static SheetFont FromBytes(byte[] bytes)
        {
            var f = new SheetFont();
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "SFNT") throw new InvalidDataException("not a sheet font table");
                uint version = r.ReadUInt32();
                if (version != 2) throw new InvalidDataException("sheet font table version " + version + " (want 2)");
                f.CapHeightEm = r.ReadDouble();
                int glyphs = r.ReadUInt16(), kerns = r.ReadUInt16();
                for (int g = 0; g < glyphs; g++)
                {
                    int cp = (int)r.ReadUInt32();
                    double adv = r.ReadDouble();
                    int nc = r.ReadUInt16();
                    var cs = new double[nc][];
                    for (int c = 0; c < nc; c++)
                    {
                        int n = r.ReadUInt16();
                        var pts = new double[2 * n];
                        for (int i = 0; i < 2 * n; i++) pts[i] = r.ReadDouble();
                        cs[c] = pts;
                    }
                    f._glyphs[cp] = new GlyphData { Adv = adv, Contours = cs };
                }
                for (int k = 0; k < kerns; k++)
                {
                    int a = (int)r.ReadUInt32(), b = (int)r.ReadUInt32();
                    f._kern[(long)a * 65536 + b] = r.ReadDouble();
                }
            }
            return f;
        }

        double Kerning(int prev, int cp) => prev >= 0 && _kern.TryGetValue((long)prev * 65536 + cp, out double k) ? k : 0;

        GlyphData Data(int cp)
        {
            if (_glyphs.TryGetValue(cp, out var g)) return g;
            lock (_lock) Missing.Add(cp);
            return _glyphs.TryGetValue(' ', out var sp) ? sp : new GlyphData { Adv = 0.278, Contours = Array.Empty<double[]>() };
        }

        /// <summary>Contours in em (y up), emboldened by `bold` em (text.mjs glyphEm), cached per (char, bold).</summary>
        double[][] Contours(int cp, double bold)
        {
            var g = Data(cp);
            if (bold <= 0) return g.Contours;
            lock (_lock)
            {
                if (_bold.TryGetValue((cp, bold), out var c)) return c;
                c = new double[g.Contours.Length][];
                for (int i = 0; i < c.Length; i++) c[i] = OffsetContour(g.Contours[i], bold);
                _bold[(cp, bold)] = c;
                return c;
            }
        }

        /// <summary>Offset a closed contour (TrueType orientation, y up) outward by d (text.mjs offsetContour).</summary>
        public static double[] OffsetContour(double[] c, double d)
        {
            int n = c.Length >> 1;
            var o = new double[c.Length];
            for (int i = 0; i < n; i++)
            {
                int ip = (i + n - 1) % n, ir = (i + 1) % n;
                double px = c[2 * ip], py = c[2 * ip + 1], qx = c[2 * i], qy = c[2 * i + 1], rx = c[2 * ir], ry = c[2 * ir + 1];
                double ax = qx - px, ay = qy - py, bx = rx - qx, by = ry - qy;
                double la = JsMath.Hypot(ax, ay); if (la == 0 || double.IsNaN(la)) la = 1;
                double lb = JsMath.Hypot(bx, by); if (lb == 0 || double.IsNaN(lb)) lb = 1;
                ax /= la; ay /= la; bx /= lb; by /= lb;
                double n1x = -ay, n1y = ax, n2x = -by, n2y = bx;
                double mx = n1x + n2x, my = n1y + n2y;
                double ml = JsMath.Hypot(mx, my);
                if (ml < 1e-6) { mx = n1x; my = n1y; } else { mx /= ml; my /= ml; }
                double cosh = mx * n1x + my * n1y;
                double k = d / Math.Max(cosh, 0.5);
                o[2 * i] = qx + mx * k;
                o[2 * i + 1] = qy + my * k;
            }
            return o;
        }

        /// <summary>Width of runs in output units (text.mjs runsWidth).</summary>
        public double RunsWidth(IList<TextRun> runs, double em, double bold = 0)
        {
            double pen = 0;
            int prev = -1;
            for (int r = 0; r < runs.Count; r++)
            {
                double sc = runs[r].Scale;
                string s = runs[r].S;
                for (int i = 0; i < s.Length; i++)
                {
                    int cp = s[i];
                    var g = Data(cp);
                    if (prev >= 0) pen += Kerning(prev, cp) * sc;
                    pen += g.Adv * sc;
                    prev = cp;
                }
            }
            return (pen + 2 * bold) * em;
        }

        /// <summary>Lays out runs at base em size `em` with the baseline origin (x, y), y DOWN in the output (text.mjs
        /// layoutRuns). Appends the glyph contours to `parts` and returns the width.</summary>
        public double LayoutRuns(IList<TextRun> runs, double em, double x, double y, double bold, List<double[]> parts)
        {
            double pen = 0;
            int prev = -1;
            for (int r = 0; r < runs.Count; r++)
            {
                double sc = runs[r].Scale, rise = runs[r].Rise;
                string s = runs[r].S;
                for (int i = 0; i < s.Length; i++)
                {
                    int cp = s[i];
                    var g = Data(cp);
                    if (prev >= 0) pen += Kerning(prev, cp) * sc;
                    var cs = Contours(cp, bold);
                    for (int ci = 0; ci < cs.Length; ci++)
                    {
                        var c = cs[ci];
                        if (c.Length == 0) continue;
                        var o = new double[c.Length];
                        for (int k = 0; k < c.Length; k += 2)
                        {
                            o[k] = x + (pen + c[k] * sc + bold) * em;
                            o[k + 1] = y - (c[k + 1] * sc + rise) * em;
                        }
                        parts.Add(o);
                    }
                    pen += g.Adv * sc;
                    prev = cp;
                }
            }
            return (pen + 2 * bold) * em;
        }

        static readonly Regex s_label = new Regex("^([A-G])(#?)([0-9]?)$", RegexOptions.CultureInvariant);

        /// <summary>The key-label styling of PianoSpec.LabelText: sharp at 0.62 raised 0.35 cap, octave digit at 0.72.</summary>
        public List<TextRun> LabelRuns(string label)
        {
            var runs = new List<TextRun>(3);
            var m = s_label.Match(label);
            if (!m.Success) { runs.Add(new TextRun(label)); return runs; }
            runs.Add(new TextRun(m.Groups[1].Value));
            if (m.Groups[2].Value.Length > 0) runs.Add(new TextRun("#", 0.62, 0.35 * CapHeightEm));
            if (m.Groups[3].Value.Length > 0) runs.Add(new TextRun(m.Groups[3].Value, 0.72));
            return runs;
        }
    }

    /// <summary>Builds sheet-font.bytes from the TTF (export-font.mjs, version 2 with doubles).</summary>
    public static class SheetFontTable
    {
        /// <summary>Where the project's TMP face lives (the only font the app ships).</summary>
        public const string TtfAssetPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        public const string OflAssetPath = "Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt";

        /// <summary>The characters the table carries: printable ASCII, Latin-1, a few typographic marks (so any lesson pack's
        /// titles print), plus everything in `extra` (book titles, credits).</summary>
        public static string Charset(string extra)
        {
            var set = new SortedSet<int>();
            for (int c = 0x20; c <= 0x7e; c++) set.Add(c);
            for (int c = 0xa1; c <= 0xff; c++) if (c != 0xad) set.Add(c);
            foreach (char c in "\u00a0\u2009\u2013\u2014\u2018\u2019\u201c\u201d\u2026\u2022") set.Add(c);
            if (extra != null) foreach (char c in extra) set.Add(c);
            var sb = new StringBuilder();
            foreach (int c in set) sb.Append((char)c);
            return sb.ToString();
        }

        public static byte[] Build(SheetTrueType f, string chars)
        {
            var cps = new SortedSet<int>();
            foreach (char c in chars) cps.Add(c);
            var gis = new Dictionary<int, int>();
            var list = new List<int>();
            foreach (int cp in cps)
            {
                int gi = f.GlyphIndex(cp);
                if (gi == 0 && cp != 0) continue;   // not in the face
                gis[cp] = gi;
                list.Add(cp);
            }
            var kerns = new List<(int a, int b, double k)>();
            foreach (int a in list) foreach (int b in list) { int k = f.Kerning(gis[a], gis[b]); if (k != 0) kerns.Add((a, b, k / (double)f.UnitsPerEm)); }
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("SFNT"));
                w.Write((uint)2);
                w.Write(f.CapHeight / (double)f.UnitsPerEm);
                w.Write((ushort)list.Count);
                w.Write((ushort)kerns.Count);
                foreach (int cp in list)
                {
                    int gi = gis[cp];
                    w.Write((uint)cp);
                    w.Write(f.Advance(gi) / (double)f.UnitsPerEm);
                    var outline = f.Outline(gi);
                    w.Write((ushort)outline.Count);
                    foreach (var c in outline)
                    {
                        var pts = f.Flatten(c, SheetFont.FlattenTolerance);
                        w.Write((ushort)pts.Count);
                        foreach (var p in pts) { w.Write(p[0]); w.Write(p[1]); }
                    }
                }
                foreach (var (a, b, k) in kerns) { w.Write((uint)a); w.Write((uint)b); w.Write(k); }
            }
            return ms.ToArray();
        }
    }
}
