// Procedural music glyphs (no music font), as the Node sheet lab's glyphs module: every shape is built from ellipses,
// rectangles and variable-width strokes, in staff-space units (S = the distance between two staff lines), y UP, origin at
// the glyph's anchor. Parts form ONE union group (nonzero, same orientation); notehead holes are reversed contours.
// The glyphs are pure functions of constants, so each is built once and shared (SheetGlyphs.X are cached).
using System;
using System.Collections.Generic;

namespace MusicRoom.Sheet.Engrave
{
    public sealed class Glyph
    {
        public readonly List<double[]> Parts;
        public readonly double X0, Y0, X1, Y1;
        public Glyph(List<double[]> parts)
        {
            Parts = parts;
            double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
            foreach (var c in parts)
                for (int i = 0; i < c.Length; i += 2)
                {
                    x0 = Math.Min(x0, c[i]); x1 = Math.Max(x1, c[i]);
                    y0 = Math.Min(y0, c[i + 1]); y1 = Math.Max(y1, c[i + 1]);
                }
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
        }
        public int PointCount { get { int n = 0; foreach (var c in Parts) n += c.Length >> 1; return n; } }
    }

    public static class SheetGlyphs
    {
        // ---------------------------------------------------------------- engraving constants (S units)
        public const double StaffLine = 0.15, Ledger = 0.18, LedgerExt = 0.38, Stem = 0.13, StemLen = 3.5,
            Beam = 0.5, BeamGap = 0.25, Bar = 0.17, BarThick = 0.55, BarGap = 0.4, HeadW = 1.21, HeadH = 1.0,
            DotR = 0.21, DotGap = 0.32;

        const double HeadRx = 0.62, HeadRy = 0.48, HeadRot = 0.35;   // ~20 degrees, rising to the right

        /// <summary>Half width of a black / half notehead (S).</summary>
        public static readonly double HeadHalfWidth = HeadHalfW();
        static double HeadHalfW()
        {
            double c = Math.Cos(HeadRot), s = Math.Sin(HeadRot);
            double a = HeadRx * c, b = HeadRy * s;   // JS (x) ** 2: V8's pow returns x * x for an exponent of 2
            return Math.Sqrt(a * a + b * b);
        }

        static Glyph s_black, s_half, s_whole, s_dot, s_sharp, s_natural, s_flat, s_restWhole, s_restHalf, s_restQuarter, s_treble, s_bass;
        static readonly Glyph[] s_flagUp = new Glyph[3], s_flagDown = new Glyph[3], s_restEighth = new Glyph[3];
        static readonly object s_lock = new object();

        static Glyph Cached(ref Glyph slot, Func<Glyph> make) { lock (s_lock) return slot ?? (slot = make()); }

        // ---------------------------------------------------------------- noteheads
        public static Glyph NoteheadBlack() => Cached(ref s_black, () => new Glyph(new List<double[]> { Contours.Orient(Contours.Ellipse(0, 0, HeadRx, HeadRy, HeadRot, 48)) }));
        public static Glyph NoteheadHalf() => Cached(ref s_half, () =>
        {
            var outer = Contours.Orient(Contours.Ellipse(0, 0, HeadRx, HeadRy, HeadRot, 48));
            var inner = Contours.Reverse(Contours.Orient(Contours.Ellipse(0, 0, 0.47, 0.205, 0.62, 40)));
            return new Glyph(new List<double[]> { outer, inner });
        });
        public static Glyph NoteheadWhole() => Cached(ref s_whole, () =>
        {
            var outer = Contours.Orient(Contours.Ellipse(0, 0, 0.8, 0.5, 0.0, 56));
            var inner = Contours.Reverse(Contours.Orient(Contours.Ellipse(0, 0, 0.42, 0.26, -1.05, 40)));
            return new Glyph(new List<double[]> { outer, inner });
        });

        // ---------------------------------------------------------------- flags (anchor = stem top, right edge of the stem)
        public static Glyph Flag8(bool up, int count)
        {
            var cache = up ? s_flagUp : s_flagDown;
            lock (s_lock)
            {
                if (cache[count] != null) return cache[count];
                var parts = new List<double[]>();
                for (int k = 0; k < count; k++)
                {
                    double y0 = -k * 0.9;
                    var outer = SheetStroke.Bezier(new[] { 0.0, y0 }, new[] { 0.05, y0 - 0.85 }, new[] { 1.25, y0 - 1.05 }, new[] { 0.98, y0 - 2.55 }, 0, 0, 36);
                    var inner = SheetStroke.Bezier(new[] { 0.98, y0 - 2.55 }, new[] { 1.02, y0 - 1.75 }, new[] { 0.45, y0 - 1.45 }, new[] { 0.0, y0 - 1.1 }, 0, 0, 36);
                    var pts = new List<double[]>();
                    foreach (var p in outer) pts.Add(new[] { p[0], p[1] });
                    pts.Add(new[] { 0.86, y0 - 2.92 });
                    for (int i = 1; i < inner.Count; i++) pts.Add(new[] { inner[i][0], inner[i][1] });
                    var c = Contours.Flat(pts);
                    if (!up) for (int i = 1; i < c.Length; i += 2) c[i] = -c[i];
                    parts.Add(Contours.Orient(c));
                }
                return cache[count] = new Glyph(parts);
            }
        }

        // ---------------------------------------------------------------- dots, accidentals
        public static Glyph Dot() => Cached(ref s_dot, () => new Glyph(new List<double[]> { Contours.Orient(Contours.Ellipse(0, 0, DotR, DotR, 0, 24)) }));

        public static Glyph Sharp() => Cached(ref s_sharp, () => new Glyph(new List<double[]>
        {
            Contours.Orient(Contours.Rect(-0.31, -1.25, -0.19, 1.05)),
            Contours.Orient(Contours.Rect(0.19, -1.05, 0.31, 1.25)),
            Contours.Orient(Contours.Slab(-0.55, -0.55, 0.55, -0.25, 0.32)),
            Contours.Orient(Contours.Slab(-0.55, 0.30, 0.55, 0.60, 0.32)),
        }));

        public static Glyph Natural() => Cached(ref s_natural, () => new Glyph(new List<double[]>
        {
            Contours.Orient(Contours.Rect(-0.34, -0.55, -0.22, 1.4)),
            Contours.Orient(Contours.Rect(0.22, -1.4, 0.34, 0.55)),
            Contours.Orient(Contours.Slab(-0.34, -0.45, 0.34, -0.25, 0.3)),
            Contours.Orient(Contours.Slab(-0.34, 0.30, 0.34, 0.50, 0.3)),
        }));

        public static Glyph FlatSign() => Cached(ref s_flat, () =>
        {
            var parts = new List<double[]> { Contours.Orient(Contours.Rect(-0.4, -0.5, -0.28, 1.9)) };
            var bowl = SheetStroke.Spline(new[] { new[] { -0.34, 0.25, 0.1 }, new[] { 0.05, 0.6, 0.16 }, new[] { 0.42, 0.35, 0.26 }, new[] { 0.2, -0.15, 0.2 }, new[] { -0.34, -0.5, 0.1 } }, 0.02);
            parts.AddRange(SheetStroke.Stroke(bowl, capStartRound: false, capEndRound: true));
            return new Glyph(parts);
        });

        // ---------------------------------------------------------------- rests (anchor: the staff's middle line)
        public static Glyph RestWhole() => Cached(ref s_restWhole, () => new Glyph(new List<double[]> { Contours.Orient(Contours.Rect(-0.62, 0.5, 0.62, 1.0)) }));
        public static Glyph RestHalf() => Cached(ref s_restHalf, () => new Glyph(new List<double[]> { Contours.Orient(Contours.Rect(-0.62, 0.0, 0.62, 0.5)) }));
        public static Glyph RestQuarter() => Cached(ref s_restQuarter, () =>
        {
            var parts = new List<double[]>();
            parts.AddRange(SheetStroke.Stroke(SheetStroke.Spline(new[] { new[] { -0.20, 1.50, 0.10 }, new[] { 0.06, 1.12, 0.38 }, new[] { 0.36, 0.72, 0.46 }, new[] { 0.30, 0.56, 0.20 } }, 0.012)));
            parts.AddRange(SheetStroke.Stroke(SheetStroke.Line(0.30, 0.58, 0.14, -0.22, 0.02, 0.14)));
            parts.AddRange(SheetStroke.Stroke(SheetStroke.Spline(new[] { new[] { -0.22, 0.02, 0.16 }, new[] { 0.06, -0.30, 0.44 }, new[] { 0.34, -0.62, 0.40 } }, 0.012)));
            parts.AddRange(SheetStroke.Stroke(SheetStroke.Spline(new[] { new[] { 0.34, -0.62, 0.16 }, new[] { 0.02, -0.66, 0.16 }, new[] { -0.30, -0.80, 0.20 }, new[] { -0.22, -1.16, 0.22 }, new[] { 0.10, -1.46, 0.10 } }, 0.012)));
            return new Glyph(parts);
        });

        public static Glyph RestEighth(int count)
        {
            lock (s_lock)
            {
                if (s_restEighth[count] != null) return s_restEighth[count];
                var parts = new List<double[]>();
                for (int k = 0; k < count; k++)
                {
                    double y = -k * 1.0, x = -k * 0.22;
                    parts.Add(Contours.Orient(Contours.Ellipse(x - 0.28, y + 0.55, 0.24, 0.24, 0, 24)));
                    parts.AddRange(SheetStroke.Stroke(SheetStroke.Spline(new[] { new[] { x - 0.42, y + 0.42, 0.1 }, new[] { x - 0.05, y + 0.28, 0.12 }, new[] { x + 0.42, y + 0.62, 0.12 } }, 0.02), capStartRound: false));
                }
                parts.AddRange(SheetStroke.Stroke(SheetStroke.Line(0.42, 0.62, 0.13, 0.05 - (count - 1) * 0.22, -1.0 - (count - 1) * 1.0, 0.15)));
                return s_restEighth[count] = new Glyph(parts);
            }
        }

        // ---------------------------------------------------------------- clefs
        /// <summary>Treble (G) clef: origin on the G line (2nd line from the bottom). Height about 7.2 S.</summary>
        public static Glyph TrebleClef() => Cached(ref s_treble, () =>
        {
            var pts = new[]
            {
                new[] { 0.30, 0.04, 0.26 }, new[] { 0.16, 0.42, 0.15 }, new[] { -0.26, 0.38, 0.16 }, new[] { -0.47, -0.02, 0.25 }, new[] { -0.20, -0.46, 0.21 },
                new[] { 0.38, -0.50, 0.16 }, new[] { 0.84, -0.10, 0.14 }, new[] { 0.88, 0.46, 0.13 }, new[] { 0.52, 0.98, 0.14 }, new[] { -0.10, 1.16, 0.18 },
                new[] { -0.78, 0.84, 0.32 }, new[] { -1.18, 0.12, 0.44 }, new[] { -1.08, -0.62, 0.40 }, new[] { -0.56, -1.10, 0.25 }, new[] { 0.20, -1.22, 0.16 },
                new[] { 0.92, -0.92, 0.14 }, new[] { 1.24, -0.30, 0.15 },
                new[] { 1.14, 0.48, 0.22 }, new[] { 0.70, 1.18, 0.34 }, new[] { 0.16, 1.98, 0.40 }, new[] { -0.22, 2.84, 0.35 }, new[] { -0.22, 3.62, 0.23 },
                new[] { 0.06, 4.28, 0.15 }, new[] { 0.46, 4.62, 0.13 }, new[] { 0.74, 4.38, 0.15 }, new[] { 0.70, 3.82, 0.18 }, new[] { 0.36, 3.22, 0.20 },
                new[] { 0.14, 2.40, 0.20 }, new[] { 0.08, 1.20, 0.20 }, new[] { 0.14, 0.00, 0.20 }, new[] { 0.26, -1.20, 0.20 }, new[] { 0.34, -2.05, 0.19 },
                new[] { 0.22, -2.52, 0.17 }, new[] { -0.14, -2.68, 0.15 }, new[] { -0.48, -2.46, 0.15 },
            };
            var parts = SheetStroke.Stroke(SheetStroke.Spline(pts, 0.012));
            parts.Add(Contours.Orient(Contours.Ellipse(-0.42, -2.22, 0.33, 0.33, 0, 28)));
            return new Glyph(parts);
        });

        /// <summary>Bass (F) clef: origin on the F line (4th line from the bottom); dots in the spaces either side.</summary>
        public static Glyph BassClef() => Cached(ref s_bass, () =>
        {
            var parts = new List<double[]> { Contours.Orient(Contours.Ellipse(-0.62, -0.02, 0.36, 0.36, 0, 32)) };
            var pts = new[] { new[] { -0.86, 0.10, 0.20 }, new[] { -0.72, 0.62, 0.12 }, new[] { -0.18, 0.98, 0.14 }, new[] { 0.52, 0.86, 0.32 }, new[] { 0.98, 0.20, 0.50 },
                new[] { 0.92, -0.62, 0.48 }, new[] { 0.50, -1.40, 0.34 }, new[] { -0.20, -2.08, 0.20 }, new[] { -0.90, -2.55, 0.08 } };
            parts.AddRange(SheetStroke.Stroke(SheetStroke.Spline(pts, 0.012), capStartRound: true, capEndRound: true));
            parts.Add(Contours.Orient(Contours.Ellipse(1.48, 0.5, 0.21, 0.21, 0, 24)));
            parts.Add(Contours.Orient(Contours.Ellipse(1.48, -0.5, 0.21, 0.21, 0, 24)));
            return new Glyph(parts);
        });

        /// <summary>Curly brace spanning height h (S), origin at its vertical middle, right edge at x = 0.</summary>
        public static Glyph Brace(double h)
        {
            double half = h / 2;
            var top = new[] { new[] { 0.0, half, 0.08 }, new[] { -0.62, half - 0.1 * h, 0.30 }, new[] { -0.80, half * 0.5, 0.55 }, new[] { -0.80, half * 0.2, 0.42 }, new[] { -1.30, 0.0, 0.08 } };
            var bottom = new double[top.Length][];
            for (int i = 0; i < top.Length; i++) bottom[i] = new[] { top[i][0], -top[i][1], top[i][2] };
            var parts = SheetStroke.Stroke(SheetStroke.Spline(top, 0.03));
            parts.AddRange(SheetStroke.Stroke(SheetStroke.Spline(bottom, 0.03)));
            return new Glyph(parts);
        }

        /// <summary>Tie crescent from (x0, y) to (x1, y), bulging by h (sign = direction), mid thickness t.</summary>
        public static Glyph Tie(double x0, double x1, double y, double h, double t = 0.2)
        {
            const int n = 32;
            var o = new double[(n + 1) * 4];
            int w = 0;
            for (int i = 0; i <= n; i++) { double u = (double)i / n; o[w++] = x0 + (x1 - x0) * u; o[w++] = y + h * 4 * u * (1 - u); }
            for (int i = n; i >= 0; i--) { double u = (double)i / n; o[w++] = x0 + (x1 - x0) * u; o[w++] = y + (h - JsMath.Sign(h) * t) * 4 * u * (1 - u); }
            return new Glyph(new List<double[]> { Contours.Orient(o) });
        }
    }
}
