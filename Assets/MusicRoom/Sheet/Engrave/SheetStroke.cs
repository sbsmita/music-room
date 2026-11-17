// Variable-width strokes for the procedural music glyphs (the Node sheet lab's stroke module): a centreline is a
// centripetal Catmull-Rom spline through key points [x, y, w] (w = full width there), sampled by arc length; the stroke is
// the union of one quad per sample step plus round caps and joins. Every part has the same orientation, so one nonzero
// fill of the group is the exact union. Operation order follows the lab exactly (bit-identical coordinates).
using System;
using System.Collections.Generic;

namespace MusicRoom.Sheet.Engrave
{
    public static class SheetStroke
    {
        /// <summary>Centripetal Catmull-Rom through pts ([x, y, w]); dense samples [x, y, w].</summary>
        public static List<double[]> Spline(double[][] pts, double step = 0.02)
        {
            int n = pts.Length;
            var P = new double[n][];
            for (int i = 0; i < n; i++) P[i] = (double[])pts[i].Clone();
            var ext0 = new[] { 2 * P[0][0] - P[1][0], 2 * P[0][1] - P[1][1], P[0][2] };
            var extN = new[] { 2 * P[n - 1][0] - P[n - 2][0], 2 * P[n - 1][1] - P[n - 2][1], P[n - 1][2] };
            var Q = new double[n + 2][];
            Q[0] = ext0;
            for (int i = 0; i < n; i++) Q[i + 1] = P[i];
            Q[n + 1] = extN;
            var o = new List<double[]>();
            for (int i = 1; i < Q.Length - 2; i++)
            {
                double[] p0 = Q[i - 1], p1 = Q[i], p2 = Q[i + 1], p3 = Q[i + 2];
                double t0 = 0, t1 = t0 + D(p0, p1), t2 = t1 + D(p1, p2), t3 = t2 + D(p2, p3);
                double segLen = JsMath.Hypot(p2[0] - p1[0], p2[1] - p1[1]);
                int m = Math.Max(2, (int)Math.Ceiling(segLen / step));
                for (int k = 0; k < m; k++)
                {
                    double t = t1 + (t2 - t1) * ((double)k / m);
                    // only x and y of the de Boor-like cascade are used (the width interpolates separately)
                    Lerp(p0, p1, t0, t1, t, out double a1x, out double a1y);
                    Lerp(p1, p2, t1, t2, t, out double a2x, out double a2y);
                    Lerp(p2, p3, t2, t3, t, out double a3x, out double a3y);
                    Lerp2(a1x, a1y, a2x, a2y, t0, t2, t, out double b1x, out double b1y);
                    Lerp2(a2x, a2y, a3x, a3y, t1, t3, t, out double b2x, out double b2y);
                    Lerp2(b1x, b1y, b2x, b2y, t1, t2, t, out double cx, out double cy);
                    double u = (double)k / m, s = (1 - Math.Cos(Math.PI * u)) / 2;
                    o.Add(new[] { cx, cy, p1[2] + (p2[2] - p1[2]) * s });
                }
            }
            o.Add((double[])P[n - 1].Clone());
            return o;
        }

        // Math.pow(hypot || 1e-6, 0.5): V8's pow takes fdlibm's y == 0.5 shortcut, which is sqrt.
        static double D(double[] a, double[] b)
        {
            double h = JsMath.Hypot(b[0] - a[0], b[1] - a[1]);
            if (h == 0 || double.IsNaN(h)) h = 1e-6;
            return Math.Sqrt(h);
        }

        static void Lerp(double[] a, double[] b, double ta, double tb, double t, out double x, out double y)
        {
            double u = (t - ta) / (tb - ta);
            x = a[0] + (b[0] - a[0]) * u;
            y = a[1] + (b[1] - a[1]) * u;
        }

        static void Lerp2(double ax, double ay, double bx, double by, double ta, double tb, double t, out double x, out double y)
        {
            double u = (t - ta) / (tb - ta);
            x = ax + (bx - ax) * u;
            y = ay + (by - ay) * u;
        }

        /// <summary>Cubic Bezier samples with linear width from w0 to w1 (stroke.mjs bezier).</summary>
        public static List<double[]> Bezier(double[] p0, double[] p1, double[] p2, double[] p3, double w0, double w1, int n = 40)
        {
            var o = new List<double[]>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n, u = 1 - t;
                double x = u * u * u * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t * t * t * p3[0];
                double y = u * u * u * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t * t * t * p3[1];
                o.Add(new[] { x, y, w0 + (w1 - w0) * t });
            }
            return o;
        }

        /// <summary>Stroke samples [x, y, w] into oriented contours (round or flat caps, round joins at sharp turns).</summary>
        public static List<double[]> Stroke(List<double[]> samples, bool capStartRound = true, bool capEndRound = true)
        {
            int n = samples.Count;
            var L = new double[n][];
            var R = new double[n][];
            for (int i = 0; i < n; i++)
            {
                double[] a = samples[Math.Max(0, i - 1)], b = samples[Math.Min(n - 1, i + 1)];
                double tx = b[0] - a[0], ty = b[1] - a[1];
                double tl = JsMath.Hypot(tx, ty);
                if (tl == 0 || double.IsNaN(tl)) tl = 1;
                tx /= tl; ty /= tl;
                double hw = samples[i][2] / 2;
                double nx = -ty, ny = tx;
                L[i] = new[] { samples[i][0] + nx * hw, samples[i][1] + ny * hw };
                R[i] = new[] { samples[i][0] - nx * hw, samples[i][1] - ny * hw };
            }
            var o = new List<double[]>();
            for (int i = 0; i < n - 1; i++)
            {
                var q = new[] { L[i][0], L[i][1], L[i + 1][0], L[i + 1][1], R[i + 1][0], R[i + 1][1], R[i][0], R[i][1] };
                o.Add(Contours.Orient(q));
            }
            if (capStartRound) o.Add(Cap(samples[0]));
            if (capEndRound) o.Add(Cap(samples[n - 1]));
            for (int i = 1; i < n - 1; i++)
            {
                double[] a = samples[i - 1], b = samples[i], c = samples[i + 1];
                double d1x = b[0] - a[0], d1y = b[1] - a[1], d2x = c[0] - b[0], d2y = c[1] - b[1];
                double den = JsMath.Hypot(d1x, d1y) * JsMath.Hypot(d2x, d2y);
                if (den == 0 || double.IsNaN(den)) den = 1;
                double cr = Math.Abs(d1x * d2y - d1y * d2x) / den;
                if (cr > 0.25) o.Add(Cap(b));
            }
            return o;
        }

        static double[] Cap(double[] s)
        {
            double r = s[2] / 2;
            const int m = 16;
            var c = new double[2 * m];
            for (int k = 0; k < m; k++)
            {
                double a = ((double)k / m) * Math.PI * 2;
                c[2 * k] = s[0] + r * Math.Cos(a);
                c[2 * k + 1] = s[1] + r * Math.Sin(a);
            }
            return Contours.Orient(c);
        }

        /// <summary>Two-point "polyline" samples (stroke.mjs callers pass [[x, y, w], [x, y, w]] directly).</summary>
        public static List<double[]> Line(double x0, double y0, double w0, double x1, double y1, double w1)
            => new List<double[]> { new[] { x0, y0, w0 }, new[] { x1, y1, w1 } };
    }
}
