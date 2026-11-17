// Geometry primitives of the placement solver (Docs/07-world-visuals.md section 8): oriented boxes (with the separating-
// axis test of Ericson 4.4.1) and floor rectangles. Everything is in Unity world or rig-local coordinates; the formulas
// only use matrix columns, dot products and absolute values, so they hold in any handedness unchanged (a mirrored
// basis flips the sign of the cross-axis terms, which the absolute values absorb).
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>Box: centre, three unit axes and the half extents along them.</summary>
    public struct Obb
    {
        public Vector3 C, A0, A1, A2;
        /// <summary>Half extents along A0, A1, A2.</summary>
        public Vector3 H;

        public Vector3 Axis(int i) => i == 0 ? A0 : i == 1 ? A1 : A2;

        /// <summary>Box.set(b, m): the axis-aligned box <paramref name="b"/> carried by <paramref name="m"/> (rotation, any
        /// scale, translation). A zero-length column keeps a zero axis and a zero extent (it divides by 1).</summary>
        public static Obb FromBounds(in Bounds b, in Matrix4x4 m)
        {
            Obb o;
            o.C = m.MultiplyPoint3x4(b.center);
            Vector3 e = b.extents;
            Vector3 a0 = new Vector3(m.m00, m.m10, m.m20), a1 = new Vector3(m.m01, m.m11, m.m21), a2 = new Vector3(m.m02, m.m12, m.m22);
            float s0 = a0.magnitude, s1 = a1.magnitude, s2 = a2.magnitude;
            if (!(s0 > 0f)) s0 = 1f;
            if (!(s1 > 0f)) s1 = 1f;
            if (!(s2 > 0f)) s2 = 1f;
            o.A0 = a0 / s0; o.A1 = a1 / s1; o.A2 = a2 / s2;
            o.H = new Vector3(e.x * s0, e.y * s1, e.z * s2);
            return o;
        }

        /// <summary>Box.copyMoved(o, m): centre carried by m, axes by m's rotation (transformDirection normalises), extents
        /// unchanged (a rig's own scale is ignored here; the layout golden relies on it).</summary>
        public Obb Moved(in Matrix4x4 m)
        {
            Obb o;
            o.C = m.MultiplyPoint3x4(C);
            o.A0 = Norm(m.MultiplyVector(A0));
            o.A1 = Norm(m.MultiplyVector(A1));
            o.A2 = Norm(m.MultiplyVector(A2));
            o.H = H;
            return o;
        }

        static Vector3 Norm(Vector3 v) { float l = v.magnitude; return l > 0f ? v / l : v; }

        /// <summary>Distance from a point to the box (0 inside).</summary>
        public float DistanceTo(Vector3 p)
        {
            Vector3 v = p - C;
            float d2 = 0f, t;
            t = Mathf.Abs(Vector3.Dot(v, A0)) - H.x; if (t > 0f) d2 += t * t;
            t = Mathf.Abs(Vector3.Dot(v, A1)) - H.y; if (t > 0f) d2 += t * t;
            t = Mathf.Abs(Vector3.Dot(v, A2)) - H.z; if (t > 0f) d2 += t * t;
            return Mathf.Sqrt(d2);
        }

        /// <summary>Slab test of a ray against the box (the desktop Shift / Alt pick of the
        /// rig's meshes): the distance along the ray to the first hit, 0 when the origin is inside. A zero axis (a flat
        /// part) still counts, as a slab of zero thickness.</summary>
        public bool Raycast(in Ray ray, out float distance)
        {
            distance = 0f;
            Vector3 o = ray.origin - C, d = ray.direction;
            float tMin = 0f, tMax = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                Vector3 a = Axis(i);
                float h = i == 0 ? H.x : i == 1 ? H.y : H.z;
                float po = Vector3.Dot(o, a), pd = Vector3.Dot(d, a);
                if (Mathf.Abs(pd) < 1e-9f)
                {
                    if (Mathf.Abs(po) > h) return false;   // parallel to this slab and outside it
                    continue;
                }
                float t0 = (-h - po) / pd, t1 = (h - po) / pd;
                if (t0 > t1) { float s = t0; t0 = t1; t1 = s; }
                if (t0 > tMin) tMin = t0;
                if (t1 < tMax) tMax = t1;
                if (tMin > tMax) return false;
            }
            distance = tMin;
            return true;
        }

        /// <summary>The eight corners (sx, sy, sz in {-1, 1}, x slowest), written into <paramref name="out8"/>.</summary>
        public void Corners(Vector3[] out8)
        {
            int k = 0;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        out8[k++] = C + A0 * (sx * H.x) + A1 * (sy * H.y) + A2 * (sz * H.z);
        }

        // Scratch for the separating-axis test (main thread only; no allocation per call).
        static readonly float[] s_R = new float[9], s_AR = new float[9], s_t = new float[3], s_A = new float[3], s_B = new float[3];

        /// <summary>boxesIntersect(a, b, slop): true when the boxes intersect by more than <paramref name="slop"/>.</summary>
        public static bool Intersect(in Obb a, in Obb b, float slop = 0f)
        {
            const float Eps = 1e-6f;
            float[] R = s_R, AR = s_AR, t = s_t, A = s_A, B = s_B;
            for (int i = 0; i < 3; i++)
            {
                Vector3 ai = a.Axis(i);
                for (int j = 0; j < 3; j++)
                {
                    float r = Vector3.Dot(ai, b.Axis(j));
                    R[i * 3 + j] = r;
                    AR[i * 3 + j] = Mathf.Abs(r) + Eps;
                }
            }
            Vector3 v = b.C - a.C;
            t[0] = Vector3.Dot(v, a.A0); t[1] = Vector3.Dot(v, a.A1); t[2] = Vector3.Dot(v, a.A2);
            A[0] = a.H.x; A[1] = a.H.y; A[2] = a.H.z;
            B[0] = b.H.x; B[1] = b.H.y; B[2] = b.H.z;
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(t[i]) > A[i] + B[0] * AR[i * 3] + B[1] * AR[i * 3 + 1] + B[2] * AR[i * 3 + 2] - slop) return false;
            for (int j = 0; j < 3; j++)
                if (Mathf.Abs(t[0] * R[j] + t[1] * R[3 + j] + t[2] * R[6 + j]) > A[0] * AR[j] + A[1] * AR[3 + j] + A[2] * AR[6 + j] + B[j] - slop) return false;
            for (int i = 0; i < 3; i++)
            {
                int i1 = (i + 1) % 3, i2 = (i + 2) % 3;
                for (int j = 0; j < 3; j++)
                {
                    int j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                    float ra = A[i1] * AR[i2 * 3 + j] + A[i2] * AR[i1 * 3 + j];
                    float rb = B[j1] * AR[i * 3 + j2] + B[j2] * AR[i * 3 + j1];
                    if (Mathf.Abs(t[i2] * R[i1 * 3 + j] - t[i1] * R[i2 * 3 + j]) > ra + rb - slop) return false;
                }
            }
            return true;
        }
    }

    /// <summary>A floor rectangle in a rig's own frame: centre (x, z) and half extents (the measured footprint rects).</summary>
    public struct LocalRect
    {
        public float X, Z, Hx, Hz;
        public LocalRect(float x, float z, float hx, float hz) { X = x; Z = z; Hx = hx; Hz = hz; }
    }

    /// <summary>A floor rectangle in the world: centre, unit axes u and v, half extents along them.</summary>
    public struct WorldRect
    {
        public float X, Z, Ux, Uz, Vx, Vz, Hu, Hv;
    }

    public static class FloorRects
    {
        /// <summary>The rig-frame rectangle carried by the rig's world matrix (its local x and
        /// z columns give the floor axes, their lengths the scale).</summary>
        public static void ToWorld(in LocalRect r, in Matrix4x4 m, ref WorldRect o)
        {
            float ux = m.m00, uz = m.m20, vx = m.m02, vz = m.m22;
            float su = Mathf.Sqrt(ux * ux + uz * uz), sv = Mathf.Sqrt(vx * vx + vz * vz);
            if (!(su > 0f)) su = 1f;
            if (!(sv > 0f)) sv = 1f;
            o.X = m.m03 + ux * r.X + vx * r.Z;
            o.Z = m.m23 + uz * r.X + vz * r.Z;
            o.Ux = ux / su; o.Uz = uz / su; o.Vx = vx / sv; o.Vz = vz / sv;
            o.Hu = r.Hx * su; o.Hv = r.Hz * sv;
        }

        /// <summary>Overlap of two rectangles each grown by gap / 2. False when they
        /// are apart; else the depth and the axis of least overlap (nx, nz), pointing from b to a.</summary>
        public static bool Overlap(in WorldRect a, in WorldRect b, float gap, out float depth, out float nx, out float nz)
        {
            float best = float.PositiveInfinity;
            nx = nz = 0f;
            depth = 0f;
            float dx = a.X - b.X, dz = a.Z - b.Z;
            for (int k = 0; k < 4; k++)
            {
                bool useA = k < 2;
                float ax = (k % 2 == 0) ? (useA ? a.Ux : b.Ux) : (useA ? a.Vx : b.Vx);
                float az = (k % 2 == 0) ? (useA ? a.Uz : b.Uz) : (useA ? a.Vz : b.Vz);
                float ra = a.Hu * Mathf.Abs(a.Ux * ax + a.Uz * az) + a.Hv * Mathf.Abs(a.Vx * ax + a.Vz * az);
                float rb = b.Hu * Mathf.Abs(b.Ux * ax + b.Uz * az) + b.Hv * Mathf.Abs(b.Vx * ax + b.Vz * az);
                float d = dx * ax + dz * az;
                float o = ra + rb + gap - Mathf.Abs(d);
                if (o <= 0f) return false;
                if (o < best)
                {
                    best = o;
                    nx = d < 0f ? -ax : ax;
                    nz = d < 0f ? -az : az;
                }
            }
            depth = best;
            return true;
        }

        public static bool Overlap(in WorldRect a, in WorldRect b, float gap) => Overlap(a, b, gap, out _, out _, out _);
    }

    /// <summary>Small angle and pose helpers shared by the layout, grab and gesture code. Yaw is the Unity heading in
    /// radians (atan2(forward.x, forward.z)); positive turns clockwise seen from above.</summary>
    public static class LayoutMath
    {
        /// <summary>The angle brought into (-pi, pi].</summary>
        public static float Wrap(float a) => Mathf.Atan2(Mathf.Sin(a), Mathf.Cos(a));

        /// <summary>easeInOutQuad.</summary>
        public static float Ease(float k) => k < 0.5f ? 2f * k * k : 1f - (-2f * k + 2f) * (-2f * k + 2f) / 2f;

        /// <summary>The heading of an upright transform (radians).</summary>
        public static float YawOf(Transform t)
        {
            Vector3 f = t.forward;
            return Mathf.Atan2(f.x, f.z);
        }

        public static Quaternion YawRotation(float yawRad) => Quaternion.Euler(0f, yawRad * Mathf.Rad2Deg, 0f);

        /// <summary>(x, z) rotated by a Unity yaw: (x cos + z sin, -x sin + z cos), the same formula as a right-handed y
        /// rotation (the seat poses, the pivot), applied here to Unity coordinates and Unity yaw.</summary>
        public static void RotY(float x, float z, float yawRad, out float rx, out float rz)
        {
            float c = Mathf.Cos(yawRad), s = Mathf.Sin(yawRad);
            rx = x * c + z * s;
            rz = -x * s + z * c;
        }

        /// <summary>World position of a seat-relative home: head on the floor + yaw-rotated (x, z), height = home.y.</summary>
        public static Vector3 SeatPlace(Vector3 head, float seatYawRad, Vector3 home)
        {
            RotY(home.x, home.z, seatYawRad, out float rx, out float rz);
            return new Vector3(head.x + rx, home.y, head.z + rz);
        }

        /// <summary>The player's right on the floor for a seat yaw: (cos, 0, -sin) (SeatFrame.Right).</summary>
        public static Vector3 SeatRight(float seatYawRad) => new Vector3(Mathf.Cos(seatYawRad), 0f, -Mathf.Sin(seatYawRad));

        public static Vector3 SeatForward(float seatYawRad) => new Vector3(Mathf.Sin(seatYawRad), 0f, Mathf.Cos(seatYawRad));
    }
}
