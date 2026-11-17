// A centripetal, open Catmull-Rom curve through 3D control points plus the curve methods the harp uses:
// getPoint(t) (curve parameter: the strings' upper ends), getPointAt(u) / getUtoTmapping (arc length over 200
// divisions: the neck tube), getTangent / getTangentAt (finite difference, delta 1e-4: the tube's frames).
// Computed in double precision so the string ends match Docs/05-instruments.md to the printed digits. The harp's
// curve lies in the plane z = 0, which the z -> -z conversion leaves in place, so no mirroring is involved here.
using System;
using UnityEngine;

namespace MusicRoom.Harp
{
    public sealed class CatmullRomCurve
    {
        public const int ArcLengthDivisions = 200; // arc-length table divisions

        readonly double[] _x, _y, _z;
        readonly int _n;
        double[] _lengths;

        /// <param name="xyz">Control points as x0, y0, z0, x1, y1, z1, ...</param>
        public CatmullRomCurve(double[] xyz)
        {
            if (xyz == null || xyz.Length < 6 || xyz.Length % 3 != 0) throw new ArgumentException("need at least two xyz points", nameof(xyz));
            _n = xyz.Length / 3;
            _x = new double[_n]; _y = new double[_n]; _z = new double[_n];
            for (int i = 0; i < _n; i++) { _x[i] = xyz[3 * i]; _y[i] = xyz[3 * i + 1]; _z[i] = xyz[3 * i + 2]; }
        }

        public int PointCount => _n;

        /// <summary>CatmullRomCurve3.getPoint(t): t in [0, 1] is the curve parameter (not arc length).</summary>
        public void GetPoint(double t, out double px, out double py, out double pz)
        {
            int l = _n;
            double p = (l - 1) * t;
            int intPoint = (int)Math.Floor(p);
            double weight = p - intPoint;
            if (weight == 0 && intPoint == l - 1)
            {
                intPoint = l - 2;
                weight = 1;
            }
            // p0 and p3 are extrapolated past the ends (tmp = 2 p[0] - p[1], 2 p[l-1] - p[l-2]).
            double p0x, p0y, p0z, p3x, p3y, p3z;
            if (intPoint > 0) { p0x = _x[intPoint - 1]; p0y = _y[intPoint - 1]; p0z = _z[intPoint - 1]; }
            else { p0x = 2 * _x[0] - _x[1]; p0y = 2 * _y[0] - _y[1]; p0z = 2 * _z[0] - _z[1]; }
            int i1 = intPoint % l, i2 = (intPoint + 1) % l;
            double p1x = _x[i1], p1y = _y[i1], p1z = _z[i1];
            double p2x = _x[i2], p2y = _y[i2], p2z = _z[i2];
            if (intPoint + 2 < l) { int i3 = intPoint + 2; p3x = _x[i3]; p3y = _y[i3]; p3z = _z[i3]; }
            else { p3x = 2 * _x[l - 1] - _x[l - 2]; p3y = 2 * _y[l - 1] - _y[l - 2]; p3z = 2 * _z[l - 1] - _z[l - 2]; }

            // Centripetal: knot spacing |p_i+1 - p_i|^0.5, i.e. (squared distance)^0.25.
            double dt0 = Math.Pow(Sq(p0x - p1x, p0y - p1y, p0z - p1z), 0.25);
            double dt1 = Math.Pow(Sq(p1x - p2x, p1y - p2y, p1z - p2z), 0.25);
            double dt2 = Math.Pow(Sq(p2x - p3x, p2y - p3y, p2z - p3z), 0.25);
            // safety check for repeated points
            if (dt1 < 1e-4) dt1 = 1.0;
            if (dt0 < 1e-4) dt0 = dt1;
            if (dt2 < 1e-4) dt2 = dt1;
            px = Nonuniform(p0x, p1x, p2x, p3x, dt0, dt1, dt2, weight);
            py = Nonuniform(p0y, p1y, p2y, p3y, dt0, dt1, dt2, weight);
            pz = Nonuniform(p0z, p1z, p2z, p3z, dt0, dt1, dt2, weight);
        }

        public Vector3 GetPoint(double t)
        {
            GetPoint(t, out double x, out double y, out double z);
            return new Vector3((float)x, (float)y, (float)z);
        }

        /// <summary>Curve.getLengths(200): cumulative chord lengths at t = k/200.</summary>
        public double[] GetLengths()
        {
            if (_lengths != null) return _lengths;
            var cache = new double[ArcLengthDivisions + 1];
            GetPoint(0, out double lx, out double ly, out double lz);
            double sum = 0;
            for (int p = 1; p <= ArcLengthDivisions; p++)
            {
                GetPoint((double)p / ArcLengthDivisions, out double cx, out double cy, out double cz);
                sum += Math.Sqrt(Sq(cx - lx, cy - ly, cz - lz));
                cache[p] = sum;
                lx = cx; ly = cy; lz = cz;
            }
            _lengths = cache;
            return cache;
        }

        public double Length => GetLengths()[ArcLengthDivisions];

        /// <summary>Curve.getUtoTmapping(u): the curve parameter at arc-length fraction u (binary search + linear).</summary>
        public double UToT(double u)
        {
            double[] arc = GetLengths();
            int il = arc.Length;
            double target = u * arc[il - 1];
            int low = 0, high = il - 1, i = 0;
            while (low <= high)
            {
                i = (int)Math.Floor(low + (high - low) / 2.0);
                double comparison = arc[i] - target;
                if (comparison < 0) low = i + 1;
                else if (comparison > 0) high = i - 1;
                else { high = i; break; }
            }
            i = high;
            if (i < 0) return 0;
            if (arc[i] == target) return (double)i / (il - 1);
            if (i + 1 >= il) return 1;
            double before = arc[i], after = arc[i + 1];
            double fraction = (target - before) / (after - before);
            return (i + fraction) / (il - 1);
        }

        /// <summary>Curve.getPointAt(u): the point at arc-length fraction u.</summary>
        public void GetPointAt(double u, out double x, out double y, out double z) => GetPoint(UToT(u), out x, out y, out z);

        /// <summary>Curve.getTangent(t): normalised finite difference over t +- 1e-4 (clamped to [0, 1]).</summary>
        public void GetTangent(double t, out double x, out double y, out double z)
        {
            const double delta = 0.0001;
            double t1 = t - delta, t2 = t + delta;
            if (t1 < 0) t1 = 0;
            if (t2 > 1) t2 = 1;
            GetPoint(t1, out double ax, out double ay, out double az);
            GetPoint(t2, out double bx, out double by, out double bz);
            x = bx - ax; y = by - ay; z = bz - az;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len > 0) { x /= len; y /= len; z /= len; }
        }

        public void GetTangentAt(double u, out double x, out double y, out double z) => GetTangent(UToT(u), out x, out y, out z);

        static double Sq(double x, double y, double z) => x * x + y * y + z * z;

        // Non-uniform Catmull-Rom cubic: tangents from the knot spacing, then the Hermite polynomial at t.
        static double Nonuniform(double x0, double x1, double x2, double x3, double dt0, double dt1, double dt2, double t)
        {
            double t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
            double t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;
            t1 *= dt1;
            t2 *= dt1;
            double c0 = x1, c1 = t1;
            double c2 = -3 * x1 + 3 * x2 - 2 * t1 - t2;
            double c3 = 2 * x1 - 2 * x2 + t1 + t2;
            double tt = t * t;
            return c0 + c1 * t + c2 * tt + c3 * tt * t;
        }
    }
}
