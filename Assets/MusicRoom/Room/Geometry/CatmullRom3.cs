// Centripetal open Catmull-Rom curve with an arc-length parametrisation (200 divisions), getTangent (delta 1e-4)
// and computeFrenetFrames, so the plant stems' tubes match the golden vertex for vertex.
using UnityEngine;

namespace MusicRoom.Room.Geometry
{
    public sealed class CatmullRom3
    {
        const int ArcLengthDivisions = 200;
        readonly Vector3[] _pts;
        float[] _lengths;

        public CatmullRom3(params Vector3[] points) { _pts = points; }

        /// <summary>Curve parameter t in [0, 1] (not arc length): getPoint.</summary>
        public Vector3 GetPoint(float t)
        {
            int l = _pts.Length;
            float p = (l - 1) * t;
            int intPoint = Mathf.FloorToInt(p);
            float weight = p - intPoint;
            if (weight == 0f && intPoint == l - 1) { intPoint = l - 2; weight = 1f; }
            Vector3 p0 = intPoint > 0 ? _pts[(intPoint - 1) % l] : (_pts[0] - _pts[1]) + _pts[0];
            Vector3 p1 = _pts[intPoint % l];
            Vector3 p2 = _pts[(intPoint + 1) % l];
            Vector3 p3 = intPoint + 2 < l ? _pts[(intPoint + 2) % l] : (_pts[l - 1] - _pts[l - 2]) + _pts[l - 1];
            // centripetal: pow 0.25 of the squared distances
            float dt0 = Mathf.Pow((p1 - p0).sqrMagnitude, 0.25f);
            float dt1 = Mathf.Pow((p2 - p1).sqrMagnitude, 0.25f);
            float dt2 = Mathf.Pow((p3 - p2).sqrMagnitude, 0.25f);
            if (dt1 < 1e-4f) dt1 = 1f;
            if (dt0 < 1e-4f) dt0 = dt1;
            if (dt2 < 1e-4f) dt2 = dt1;
            return new Vector3(
                Calc(p0.x, p1.x, p2.x, p3.x, dt0, dt1, dt2, weight),
                Calc(p0.y, p1.y, p2.y, p3.y, dt0, dt1, dt2, weight),
                Calc(p0.z, p1.z, p2.z, p3.z, dt0, dt1, dt2, weight));
        }

        static float Calc(float x0, float x1, float x2, float x3, float dt0, float dt1, float dt2, float t)
        {
            float t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
            float t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;
            t1 *= dt1; t2 *= dt1;
            float c0 = x1, c1 = t1, c2 = -3f * x1 + 3f * x2 - 2f * t1 - t2, c3 = 2f * x1 - 2f * x2 + t1 + t2;
            float tt = t * t, ttt = tt * t;
            return c0 + c1 * t + c2 * tt + c3 * ttt;
        }

        float[] Lengths()
        {
            if (_lengths != null) return _lengths;
            var cache = new float[ArcLengthDivisions + 1];
            Vector3 last = GetPoint(0f);
            float sum = 0f;
            for (int p = 1; p <= ArcLengthDivisions; p++)
            {
                Vector3 cur = GetPoint((float)p / ArcLengthDivisions);
                sum += Vector3.Distance(cur, last);
                cache[p] = sum;
                last = cur;
            }
            return _lengths = cache;
        }

        /// <summary>Curve.getUtoTmapping: arc-length fraction u to curve parameter t (binary search, linear in a segment).</summary>
        public float UtoT(float u)
        {
            float[] arc = Lengths();
            int il = arc.Length;
            float target = u * arc[il - 1];
            int low = 0, high = il - 1, i = 0;
            while (low <= high)
            {
                i = low + (high - low) / 2;
                float cmp = arc[i] - target;
                if (cmp < 0f) low = i + 1;
                else if (cmp > 0f) high = i - 1;
                else { high = i; break; }
            }
            i = high;
            if (arc[i] == target) return (float)i / (il - 1);
            float before = arc[i], after = arc[i + 1];
            return (i + (target - before) / (after - before)) / (il - 1);
        }

        public Vector3 GetPointAt(float u) => GetPoint(UtoT(u));

        public Vector3 GetTangent(float t)
        {
            const float delta = 0.0001f;
            float t1 = t - delta, t2 = t + delta;
            if (t1 < 0f) t1 = 0f;
            if (t2 > 1f) t2 = 1f;
            return (GetPoint(t2) - GetPoint(t1)).normalized;
        }

        public Vector3 GetTangentAt(float u) => GetTangent(UtoT(u));

        /// <summary>Curve.computeFrenetFrames (open curve): initial normal along the smallest tangent component, then
        /// parallel transport by the rotation between successive tangents.</summary>
        public void FrenetFrames(int segments, out Vector3[] tangents, out Vector3[] normals, out Vector3[] binormals)
        {
            tangents = new Vector3[segments + 1];
            normals = new Vector3[segments + 1];
            binormals = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++) tangents[i] = GetTangentAt((float)i / segments);
            float min = float.MaxValue;
            float tx = Mathf.Abs(tangents[0].x), ty = Mathf.Abs(tangents[0].y), tz = Mathf.Abs(tangents[0].z);
            Vector3 normal = Vector3.zero;
            if (tx <= min) { min = tx; normal = new Vector3(1f, 0f, 0f); }
            if (ty <= min) { min = ty; normal = new Vector3(0f, 1f, 0f); }
            if (tz <= min) normal = new Vector3(0f, 0f, 1f);
            Vector3 vec = Vector3.Cross(tangents[0], normal).normalized;
            normals[0] = Vector3.Cross(tangents[0], vec);
            binormals[0] = Vector3.Cross(tangents[0], normals[0]);
            for (int i = 1; i <= segments; i++)
            {
                normals[i] = normals[i - 1];
                binormals[i] = binormals[i - 1];
                vec = Vector3.Cross(tangents[i - 1], tangents[i]);
                if (vec.magnitude > 2.220446049250313e-16f)
                {
                    vec.Normalize();
                    float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(tangents[i - 1], tangents[i]), -1f, 1f));
                    normals[i] = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, vec) * normals[i];
                }
                binormals[i] = Vector3.Cross(tangents[i], normals[i]);
            }
        }
    }
}
