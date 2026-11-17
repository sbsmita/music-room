// Minimal TrueType reader (the Node sheet lab's ttf module): head, hhea, maxp, OS/2, cmap format 4, hmtx, loca,
// glyf (simple + composite), legacy kern format 0. It reads the project's own text font (TextMesh Pro's
// LiberationSans.ttf, SIL OFL 1.1, Assets/TextMesh Pro/Fonts) and writes the small flattened table the book prints with
// (Sheet/Resources/sheet-font.bytes, SheetFontTable.Build). The app never parses the TTF at run time: the table is
// generated from it (like Piano/PianoKfelt.g.cs) and SheetFontTests checks that regenerating it from the TTF gives the
// shipped bytes exactly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusicRoom.Sheet.Engrave
{
    public sealed class SheetTrueType
    {
        public struct Pt { public double X, Y; public bool On; }

        readonly byte[] _b;
        readonly Dictionary<string, (int off, int len)> _tables = new Dictionary<string, (int, int)>();
        readonly int _indexToLocFormat, _numHMetrics, _segs, _endO, _startO, _deltaO, _rangeO, _hmtx, _loca, _glyf;
        readonly Dictionary<long, int> _kern = new Dictionary<long, int>();

        public readonly int UnitsPerEm, Ascender, Descender, CapHeight, XHeight, NumGlyphs;

        int U16(int o) => (_b[o] << 8) | _b[o + 1];
        int I16(int o) => (short)((_b[o] << 8) | _b[o + 1]);
        int U32(int o) => (int)(((uint)_b[o] << 24) | ((uint)_b[o + 1] << 16) | ((uint)_b[o + 2] << 8) | _b[o + 3]);

        public SheetTrueType(byte[] bytes)
        {
            _b = bytes;
            int numTables = U16(4);
            for (int i = 0; i < numTables; i++)
            {
                int o = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(_b, o, 4);
                _tables[tag] = (U32(o + 8), U32(o + 12));
            }
            int head = _tables["head"].off;
            UnitsPerEm = U16(head + 18);
            _indexToLocFormat = I16(head + 50);
            int hhea = _tables["hhea"].off;
            Ascender = I16(hhea + 4); Descender = I16(hhea + 6); _numHMetrics = U16(hhea + 34);
            NumGlyphs = U16(_tables["maxp"].off + 4);
            int os2 = _tables.TryGetValue("OS/2", out var t2) ? t2.off : -1;
            CapHeight = os2 >= 0 && U16(os2) >= 2 ? I16(os2 + 88) : (int)Math.Round(UnitsPerEm * 0.7);
            XHeight = os2 >= 0 && U16(os2) >= 2 ? I16(os2 + 86) : (int)Math.Round(UnitsPerEm * 0.52);
            int cmapOff = _tables["cmap"].off, sub = -1;
            for (int i = 0, n = U16(cmapOff + 2); i < n; i++)
            {
                int pid = U16(cmapOff + 4 + i * 8), eid = U16(cmapOff + 6 + i * 8), off = U32(cmapOff + 8 + i * 8);
                if (pid == 3 && eid == 1) sub = cmapOff + off;
            }
            if (sub < 0 || U16(sub) != 4) throw new InvalidDataException("no cmap format 4");
            int segX2 = U16(sub + 6);
            _segs = segX2 / 2;
            _endO = sub + 14; _startO = _endO + segX2 + 2; _deltaO = _startO + segX2; _rangeO = _deltaO + segX2;
            _hmtx = _tables["hmtx"].off; _loca = _tables["loca"].off; _glyf = _tables["glyf"].off;
            if (_tables.TryGetValue("kern", out var kt))
            {
                int k = kt.off, nSub = U16(k + 2), o = k + 4;
                for (int s = 0; s < nSub; s++)
                {
                    int len = U16(o + 2), cov = U16(o + 4);
                    if ((cov >> 8) == 0)
                    {
                        int nPairs = U16(o + 6);
                        for (int i = 0; i < nPairs; i++) { int q = o + 14 + i * 6; _kern[(long)U16(q) * 65536 + U16(q + 2)] = I16(q + 4); }
                    }
                    o += len;
                }
            }
        }

        public static SheetTrueType Load(string path) => new SheetTrueType(File.ReadAllBytes(path));

        public int GlyphIndex(int cp)
        {
            for (int s = 0; s < _segs; s++)
            {
                int end = U16(_endO + s * 2);
                if (cp > end) continue;
                int start = U16(_startO + s * 2);
                if (cp < start) return 0;
                int delta = I16(_deltaO + s * 2), ro = U16(_rangeO + s * 2);
                if (ro == 0) return (cp + delta) & 0xffff;
                int gi = U16(_rangeO + s * 2 + ro + (cp - start) * 2);
                return gi == 0 ? 0 : (gi + delta) & 0xffff;
            }
            return 0;
        }

        public int Advance(int gi) => U16(_hmtx + 4 * Math.Min(gi, _numHMetrics - 1));
        public int Kerning(int a, int b) => _kern.TryGetValue((long)a * 65536 + b, out int v) ? v : 0;

        (int a, int b) GlyphRange(int gi)
        {
            if (_indexToLocFormat == 0) return (_glyf + U16(_loca + gi * 2) * 2, _glyf + U16(_loca + gi * 2 + 2) * 2);
            return (_glyf + U32(_loca + gi * 4), _glyf + U32(_loca + gi * 4 + 4));
        }

        /// <summary>Contours of on/off-curve points in font units (y up).</summary>
        public List<List<Pt>> Outline(int gi, int depth = 0)
        {
            var (a, b) = GlyphRange(gi);
            var result = new List<List<Pt>>();
            if (b <= a) return result;
            int nc = I16(a);
            if (nc >= 0)
            {
                var ends = new int[nc];
                for (int i = 0; i < nc; i++) ends[i] = U16(a + 10 + i * 2);
                int npts = nc > 0 ? ends[nc - 1] + 1 : 0;
                int p = a + 10 + nc * 2;
                int insLen = U16(p); p += 2 + insLen;
                var flags = new List<int>(npts);
                while (flags.Count < npts)
                {
                    int f = _b[p++]; flags.Add(f);
                    if ((f & 8) != 0) { int r = _b[p++]; while (r-- > 0) flags.Add(f); }
                }
                var xs = new int[npts]; var ys = new int[npts];
                int v = 0;
                for (int i = 0; i < npts; i++)
                {
                    int f = flags[i];
                    if ((f & 2) != 0) { int d = _b[p++]; v += (f & 16) != 0 ? d : -d; }
                    else if ((f & 16) == 0) { v += I16(p); p += 2; }
                    xs[i] = v;
                }
                v = 0;
                for (int i = 0; i < npts; i++)
                {
                    int f = flags[i];
                    if ((f & 4) != 0) { int d = _b[p++]; v += (f & 32) != 0 ? d : -d; }
                    else if ((f & 32) == 0) { v += I16(p); p += 2; }
                    ys[i] = v;
                }
                int s = 0;
                foreach (int e in ends)
                {
                    var c = new List<Pt>();
                    for (int i = s; i <= e; i++) c.Add(new Pt { X = xs[i], Y = ys[i], On = (flags[i] & 1) == 1 });
                    result.Add(c);
                    s = e + 1;
                }
                return result;
            }
            if (depth > 4) return result;
            int pp = a + 10;
            bool more = true;
            while (more)
            {
                int fl = U16(pp), ci = U16(pp + 2); pp += 4;
                int dx, dy;
                if ((fl & 1) != 0) { dx = I16(pp); dy = I16(pp + 2); pp += 4; }
                else { dx = (sbyte)_b[pp]; dy = (sbyte)_b[pp + 1]; pp += 2; }
                double m00 = 1, m01 = 0, m10 = 0, m11 = 1;
                if ((fl & 8) != 0) { m00 = m11 = I16(pp) / 16384.0; pp += 2; }
                else if ((fl & 0x40) != 0) { m00 = I16(pp) / 16384.0; m11 = I16(pp + 2) / 16384.0; pp += 4; }
                else if ((fl & 0x80) != 0) { m00 = I16(pp) / 16384.0; m01 = I16(pp + 2) / 16384.0; m10 = I16(pp + 4) / 16384.0; m11 = I16(pp + 6) / 16384.0; pp += 8; }
                foreach (var c in Outline(ci, depth + 1))
                {
                    var t = new List<Pt>(c.Count);
                    foreach (var q in c) t.Add(new Pt { X = m00 * q.X + m10 * q.Y + dx, Y = m01 * q.X + m11 * q.Y + dy, On = q.On });
                    result.Add(t);
                }
                more = (fl & 0x20) != 0;
            }
            return result;
        }

        /// <summary>TrueType contour to a closed polyline in em (x / upm, y / upm), quadratics flattened to about tol em
        /// (ttf.mjs flattenTrueType, same operation order).</summary>
        public List<double[]> Flatten(List<Pt> contour, double tol)
        {
            var o = new List<double[]>();
            var pts = contour;
            int n0 = pts.Count;
            if (n0 == 0) return o;
            int start = pts.FindIndex(p => p.On);
            if (start < 0)
            {
                Pt a = pts[n0 - 1], b = pts[0];
                pts = new List<Pt> { new Pt { X = (a.X + b.X) / 2, Y = (a.Y + b.Y) / 2, On = true } };
                pts.AddRange(contour);
                start = 0;
            }
            int n = pts.Count;
            double upm = UnitsPerEm;
            double[] P(Pt q) => new[] { q.X / upm, q.Y / upm };
            void Quad(Pt a, Pt c, Pt b)
            {
                double[] pa = P(a), pc = P(c), pb = P(b);
                double dd = JsMath.Hypot(pa[0] - 2 * pc[0] + pb[0], pa[1] - 2 * pc[1] + pb[1]);
                int segs = Math.Max(1, Math.Min(24, (int)Math.Ceiling(Math.Sqrt(dd / (8 * tol)))));
                for (int i = 1; i <= segs; i++)
                {
                    double t = (double)i / segs, u = 1 - t;
                    o.Add(new[] { u * u * pa[0] + 2 * u * t * pc[0] + t * t * pb[0], u * u * pa[1] + 2 * u * t * pc[1] + t * t * pb[1] });
                }
            }
            o.Add(P(pts[start]));
            Pt cur = pts[start];
            bool hasCtrl = false;
            Pt ctrl = default;
            for (int k = 1; k <= n; k++)
            {
                Pt q = pts[(start + k) % n];
                if (q.On)
                {
                    if (hasCtrl) Quad(cur, ctrl, q); else o.Add(P(q));
                    cur = q; hasCtrl = false;
                }
                else
                {
                    if (hasCtrl) { var mid = new Pt { X = (ctrl.X + q.X) / 2, Y = (ctrl.Y + q.Y) / 2 }; Quad(cur, ctrl, mid); cur = mid; }
                    ctrl = q; hasCtrl = true;
                }
            }
            o.RemoveAt(o.Count - 1);
            return o;
        }
    }
}
