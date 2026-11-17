// The handpan's pure geometry (handpan-layout.mjs + the helpers of handpan-interaction.mjs), in double, allocation-free:
// map coordinates, field lookup, the relief, the cap point and normal, the placement transform, the tak's neighbours and
// the velocity curves. Shared by HandpanPlay (contacts), HandpanGeometry (mesh) and the lessons (block paths).
using System;

namespace MusicRoom.Handpan
{
    /// <summary>A double-precision 3-vector (the reference's maths; Vector3 only at the Unity boundary).</summary>
    public struct D3
    {
        public double X, Y, Z;
        public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator *(D3 a, double k) => new D3(a.X * k, a.Y * k, a.Z * k);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public D3 Normalized { get { double l = Length; return l > 0 ? new D3(X / l, Y / l, Z / l) : this; } }
        public static double Dist(D3 a, D3 b) => (a - b).Length;
        public UnityEngine.Vector3 ToVector3() => new UnityEngine.Vector3((float)X, (float)Y, (float)Z);
        public static D3 From(UnityEngine.Vector3 v) => new D3(v.x, v.y, v.z);
        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }

    /// <summary>Where the instrument is: the rim centre (world), the tilt toward the player and the yaw (handpan-layout PLACE).
    /// Local -&gt; world = centre + RotY(yaw) RotX(-tilt) local (Unity: Quaternion.Euler(-tilt, yaw, 0)).</summary>
    public struct HandpanPlace
    {
        public D3 Centre;
        public double TiltDeg, YawDeg;
        double _ct, _st, _cy, _sy;
        bool _ready;

        public HandpanPlace(D3 centre, double tiltDeg, double yawDeg)
        {
            Centre = centre; TiltDeg = tiltDeg; YawDeg = yawDeg;
            double t = tiltDeg * Math.PI / 180, y = yawDeg * Math.PI / 180;
            _ct = Math.Cos(t); _st = Math.Sin(t); _cy = Math.Cos(y); _sy = Math.Sin(y); _ready = true;
        }

        /// <summary>The default seat placement (rim centre (0, 0.72, 0.38), 28 degrees).</summary>
        public static HandpanPlace Default => new HandpanPlace(new D3(HandpanSpec.PlaceCentre[0], HandpanSpec.PlaceCentre[1], HandpanSpec.PlaceCentre[2]), HandpanSpec.TiltDeg, HandpanSpec.YawDeg);

        void Ensure() { if (!_ready) this = new HandpanPlace(Centre, TiltDeg, YawDeg); }

        /// <summary>toWorld().</summary>
        public D3 ToWorld(D3 p)
        {
            Ensure();
            double y = p.Y * _ct + p.Z * _st, z = -p.Y * _st + p.Z * _ct;
            double x = p.X * _cy + z * _sy, z2 = -p.X * _sy + z * _cy;
            return new D3(Centre.X + x, Centre.Y + y, Centre.Z + z2);
        }

        /// <summary>dirToWorld(): a direction (no translation).</summary>
        public D3 DirToWorld(D3 d)
        {
            Ensure();
            double y = d.Y * _ct + d.Z * _st, z = -d.Y * _st + d.Z * _ct;
            return new D3(d.X * _cy + z * _sy, y, -d.X * _sy + z * _cy);
        }

        /// <summary>fromWorld().</summary>
        public D3 FromWorld(D3 w)
        {
            Ensure();
            double dx = w.X - Centre.X, dy = w.Y - Centre.Y, dz = w.Z - Centre.Z;
            double x = dx * _cy - dz * _sy, z = dx * _sy + dz * _cy;
            return new D3(x, dy * _ct - z * _st, dy * _st + z * _ct);
        }
    }

    /// <summary>A map point's field: field 0..8, 9 = the steel between the fields (tak), -1 = off the instrument.</summary>
    public struct HandpanHit
    {
        public int Field;
        public double U, V, Rho, S;
    }

    /// <summary>One world point against the steel (contactOf).</summary>
    public struct HandpanContact
    {
        public double X, Z, H;
        public bool OnTop;
        public HandpanHit Hit;
        public D3 NLocal;
    }

    public struct HandpanNeighbour
    {
        public int Field;
        public double D, W;
    }

    public static class HandpanMath
    {
        // ---------------------------------------------------------------- the map

        /// <summary>fieldCoords(): u along the radial (long) axis, + toward the rim; v across, + clockwise from above; both / semi-axes.</summary>
        public static void FieldCoords(HandpanField F, double X, double Z, out double u, out double v)
        {
            if (F.Ding) { u = X / (F.Long / 2); v = Z / (F.Short / 2); return; }
            double dx = X - F.Cx, dz = Z - F.Cz;
            u = (dx * F.Rx + dz * F.Rz) / (F.Long / 2);
            v = (dx * F.Tx + dz * F.Tz) / (F.Short / 2);
        }

        /// <summary>The map point of field i at normalised (u, v) (the inverse of FieldCoords).</summary>
        public static void MapAt(int i, double u, double v, out double X, out double Z)
        {
            HandpanField F = HandpanSpec.Fields[i];
            if (F.Ding) { X = u * F.Long / 2; Z = v * F.Short / 2; return; }
            X = F.Cx + F.Rx * u * F.Long / 2 + F.Tx * v * F.Short / 2;
            Z = F.Cz + F.Rz * u * F.Long / 2 + F.Tz * v * F.Short / 2;
        }

        /// <summary>hitField(): the field whose normalised radius is smallest (&lt; 1 + 6 mm / semi-short-axis), else the steel
        /// between them while on the shell, else -1. u, v are scaled back onto the field's edge when just outside it.</summary>
        public static HandpanHit HitField(double X, double Z)
        {
            int best = -1; double bestR = double.PositiveInfinity, bu = 0, bv = 0;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
            {
                HandpanField F = HandpanSpec.Fields[i];
                FieldCoords(F, X, Z, out double u, out double v);
                double m = 1 + HandpanSpec.EdgeMargin / (F.Short / 2);
                double r = Hypot(u, v);
                if (r < m && r < bestR) { best = i; bestR = r; bu = u; bv = v; }
            }
            if (best >= 0)
            {
                double k = bestR > 1 ? 1 / bestR : 1;
                return new HandpanHit { Field = best, U = bu * k, V = bv * k, Rho = Math.Min(1, bestR) };
            }
            double s = Hypot(X, Z);
            if (s < HandpanSpec.RimArc + 0.01) return new HandpanHit { Field = HandpanSpec.Tak, U = 0, V = 0, Rho = 1, S = s };
            return new HandpanHit { Field = -1 };
        }

        /// <summary>takNeighbours(): the two fields nearest to a tak at (X, Z), by distance to their edge along the ray from
        /// their centre, each with w = exp(-d / 25 mm). Ties keep the lower field index.</summary>
        public static void TakNeighbours(double X, double Z, out HandpanNeighbour n0, out HandpanNeighbour n1)
        {
            n0 = new HandpanNeighbour { Field = -1, D = double.PositiveInfinity };
            n1 = n0;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
            {
                HandpanField F = HandpanSpec.Fields[i];
                FieldCoords(F, X, Z, out double u, out double v);
                double rho = Hypot(u, v);
                double cx = F.Ding ? 0 : F.Cx, cz = F.Ding ? 0 : F.Cz;
                double d = rho > 1 ? Hypot(X - cx, Z - cz) * (1 - 1 / rho) : 0;
                var c = new HandpanNeighbour { Field = i, D = d, W = Math.Exp(-d / HandpanSpec.TakFallM) };
                if (n0.Field < 0 || d < n0.D) { n1 = n0; n0 = c; }
                else if (n1.Field < 0 || d < n1.D) n1 = c;
            }
        }

        /// <summary>relief(): the surface offset (m, along the outward normal): the ding's dome, each field's shallow
        /// depression and its concave dimple.</summary>
        public static double Relief(double X, double Z)
        {
            double d = 0;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
            {
                HandpanField F = HandpanSpec.Fields[i];
                FieldCoords(F, X, Z, out double u, out double v);
                double r2 = u * u + v * v;
                if (r2 >= 1) continue;
                if (F.Ding) d += F.DomeH * (1 - r2) * (1 - r2);
                else d -= F.FieldDepth * (1 - r2);
                double q = r2 / (F.DimpleFrac * F.DimpleFrac);
                if (q < 1) d -= F.DimpleDepth * (1 - q) * (1 - q);
            }
            return d;
        }

        /// <summary>The relief's gradient d(relief)/d(X, Z) in the map (analytic; for the mesh normals).</summary>
        public static void ReliefGradient(double X, double Z, out double gx, out double gz)
        {
            gx = 0; gz = 0;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
            {
                HandpanField F = HandpanSpec.Fields[i];
                FieldCoords(F, X, Z, out double u, out double v);
                double r2 = u * u + v * v;
                if (r2 >= 1) continue;
                // d(r2)/dX, d(r2)/dZ
                double a = F.Long / 2, b = F.Short / 2, dux, duz, dvx, dvz;
                if (F.Ding) { dux = 1 / a; duz = 0; dvx = 0; dvz = 1 / b; }
                else { dux = F.Rx / a; duz = F.Rz / a; dvx = F.Tx / b; dvz = F.Tz / b; }
                double dr2x = 2 * u * dux + 2 * v * dvx, dr2z = 2 * u * duz + 2 * v * dvz;
                double dd = F.Ding ? -2 * F.DomeH * (1 - r2) : F.FieldDepth;   // d(relief)/d(r2)
                double q = r2 / (F.DimpleFrac * F.DimpleFrac);
                if (q < 1) dd += 2 * F.DimpleDepth * (1 - q) / (F.DimpleFrac * F.DimpleFrac);
                gx += dd * dr2x; gz += dd * dr2z;
            }
        }

        /// <summary>capPoint(): a map point -&gt; the instrument-local point on the top cap (relief along the cap normal) and
        /// the cap normal (without the relief's tilt).</summary>
        public static D3 CapPoint(double X, double Z, bool withRelief, out D3 normal)
        {
            double s = Hypot(X, Z), R = HandpanSpec.CapR, th = s / R;
            double dirx = s > 1e-12 ? X / s : 0, dirz = s > 1e-12 ? Z / s : 0;
            normal = new D3(Math.Sin(th) * dirx, Math.Cos(th), Math.Sin(th) * dirz);
            double cy = HandpanSpec.TopH - R;
            double h = withRelief ? Relief(X, Z) : 0;
            return new D3(normal.X * (R + h), cy + normal.Y * (R + h), normal.Z * (R + h));
        }

        /// <summary>toMap(): an instrument-local point -&gt; map point, signed height above the cap sphere (no relief), normal.</summary>
        public static void ToMap(D3 p, out double X, out double Z, out double height, out D3 n)
        {
            double R = HandpanSpec.CapR, cy = HandpanSpec.TopH - R;
            double dx = p.X, dy = p.Y - cy, dz = p.Z;
            double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double th = Math.Acos(Math.Max(-1, Math.Min(1, dy / r)));
            double h = Hypot(dx, dz), s = R * th;
            X = h > 1e-12 ? (s * dx) / h : 0;
            Z = h > 1e-12 ? (s * dz) / h : 0;
            height = r - R;
            n = new D3(dx / r, dy / r, dz / r);
        }

        /// <summary>contactOf(): a world point against the steel (the pad's height above the relieved surface, minus its radius).</summary>
        public static HandpanContact ContactOf(D3 world, double padRadius, in HandpanPlace place)
        {
            D3 pl = place.FromWorld(world);
            ToMap(pl, out double X, out double Z, out double height, out D3 n);
            double s = Hypot(X, Z);
            bool onTop = s < HandpanSpec.RimArc + 0.01 && pl.Y > -0.02;
            HandpanHit hit = onTop ? HitField(X, Z) : new HandpanHit { Field = -1 };
            return new HandpanContact { X = X, Z = Z, H = height - Relief(X, Z) - padRadius, OnTop = onTop, Hit = hit, NLocal = n };
        }

        /// <summary>The cap normal at a map point, in world (surfaceNormalWorld).</summary>
        public static D3 SurfaceNormalWorld(double X, double Z, in HandpanPlace place)
        {
            double s = Hypot(X, Z), th = s / HandpanSpec.CapR, dx = s > 1e-9 ? X / s : 0, dz = s > 1e-9 ? Z / s : 0;
            var nl = new D3(Math.Sin(th) * dx, Math.Cos(th), Math.Sin(th) * dz);
            return place.DirToWorld(nl);
        }

        /// <summary>The world point and normal of field i's dimple apex (IHandpanApi.FieldCentre / FieldNormal).</summary>
        public static D3 FieldApexWorld(int i, in HandpanPlace place, out D3 normalWorld)
        {
            HandpanField F = HandpanSpec.Fields[i];
            double X = F.Ding ? 0 : F.Cx, Z = F.Ding ? 0 : F.Cz;
            D3 p = CapPoint(X, Z, true, out D3 n);
            normalWorld = place.DirToWorld(n).Normalized;
            return place.ToWorld(p);
        }

        /// <summary>The palm's cover of each field (palmCover): 1 within 7.5 cm of the field centre, 0 beyond 11 cm.</summary>
        public static double PalmCover(int field, double X, double Z)
        {
            HandpanField F = HandpanSpec.Fields[field];
            double d = Hypot(X - F.Cx, Z - F.Cz);
            return d <= HandpanPlayK.PALM_COVER_FULL ? 1 : d >= HandpanPlayK.PALM_COVER_ZERO ? 0
                : (HandpanPlayK.PALM_COVER_ZERO - d) / (HandpanPlayK.PALM_COVER_ZERO - HandpanPlayK.PALM_COVER_FULL);
        }

        // ---------------------------------------------------------------- velocity curves (handpan.md 6.3, 6.4)

        /// <summary>fingerVel(): -1 below V0 (no strike); else ((vn - 0.12) / 0.88)^0.75 within [0.06, 1].</summary>
        public static double FingerVel(double vn) =>
            vn < HandpanPlayK.V0 ? -1 : Math.Max(HandpanPlayK.VMIN, Math.Min(1, Math.Pow((vn - HandpanPlayK.V0) / (HandpanPlayK.V1 - HandpanPlayK.V0), HandpanPlayK.VEXP)));

        /// <summary>palmVel(): -1 below 0.35 m/s; else ((vn - 0.25) / 1.35)^0.75 within [0.06, 1].</summary>
        public static double PalmVel(double vn) =>
            vn < HandpanPlayK.PALM_SLAP_MIN ? -1 : Math.Max(HandpanPlayK.VMIN, Math.Min(1, Math.Pow((vn - HandpanPlayK.PALM_SLAP_V0) / (HandpanPlayK.PALM_SLAP_V1 - HandpanPlayK.PALM_SLAP_V0), HandpanPlayK.VEXP)));

        /// <summary>JavaScript's Math.hypot for two values (sqrt of the sum of squares; the reference's inputs never overflow).</summary>
        public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
        public static double Hypot(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);

        /// <summary>The reference's round3 (Math.round(v * 1000) / 1000, half up).</summary>
        public static double Round3(double v) => Math.Floor(v * 1000 + 0.5) / 1000;
        public static double Round6(double v) => Math.Floor(v * 1e6 + 0.5) / 1e6;

        /// <summary>The Helmholtz (cavity) resonance of this shell (handpan.md 2.2): about 91 Hz.</summary>
        public static double HelmholtzHz()
        {
            double Cap(double h, double R) => Math.PI * h * h * (3 * R - h) / 3;
            double V = Cap(HandpanSpec.TopH, HandpanSpec.CapR) + Cap(HandpanSpec.BotH, HandpanSpec.BotR);
            double a = HandpanSpec.GuD / 2, S = Math.PI * a * a, Leff = HandpanSpec.GuLip + 1.7 * a;
            return 343 / (2 * Math.PI) * Math.Sqrt(S / (V * Leff));
        }
    }
}
