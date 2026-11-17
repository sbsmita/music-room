// Procedural meshes of the bowls rig (bowls.md 5.1), built once on the main thread:
//   bowls   a lathe of the hammered-bowl profile (outer wall R (0.55 + 0.45 sin(pi/2 y/H)), the wall's thickness, a
//           rounded lip, a slightly concave base and a smooth inner floor); UV u around, v = 0.5 + arc length from the lip
//           / 0.26 m (outer wall below 0.5, inner above), so the polished band, the engraved rings and the hammer dimples
//           sit at the same distance from the lip on all three bowls with one shared material
//   cushions ring tori whose crown carries the bowl's foot ring, in the stand's mesh at the seat (the cushions alone on a
//           real table in MR), in the display mesh on the sill
//   stand   a small walnut table: rounded top with a raised rim, aprons, four turned legs, stretchers at 0.14 m
//   puja    a turned walnut stick (150 mm, Ø 22 mm), its top 35 mm wrapped in suede (Ø 27 mm): its own mesh and renderer
//           at the seat (it follows the hand and stays when the stand hides), in the display mesh on the sill
// Everything but the bowls uses the one Wood atlas (walnut grain, the cushion cloth, the suede).
// No Unity objects are created here except the Meshes.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Bowls
{
    public static class BowlsGeometry
    {
        /// <summary>The bowl texture's v span (m of profile arc length): the lip at v = 0.5.</summary>
        public const float ProfileSpan = 0.26f;
        /// <summary>The Wood atlas rows: walnut grain below GrainVMax, the cushion fabric in [FabricV0, FabricV1], the puja's
        /// suede above SuedeV0 (one material for the stand, the cushions and the stick).</summary>
        public const float GrainVMax = 0.74f, FabricV0 = 0.755f, FabricV1 = 0.865f, SuedeV0 = 0.875f;

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public Mesh ToMesh(string name, bool tangents)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
                if (tangents) m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }

            /// <summary>A surface of revolution about +y through `origin`: profile (r, y) points with outward normals,
            /// segments around; u around in [u0, u1], v per profile point. axisX: the lathe axis lies along x instead.</summary>
            public void Lathe(IList<Vector2> prof, IList<Vector2> nrm, IList<float> vs, int seg, Vector3 origin, float u0 = 0, float u1 = 1, bool axisX = false, IList<float> grainU = null)
            {
                int b = V.Count, K = prof.Count;
                for (int k = 0; k < K; k++)
                    for (int j = 0; j <= seg; j++)
                    {
                        float a = 2 * Mathf.PI * j / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        Vector3 p = axisX ? new Vector3(prof[k].y, prof[k].x * c, prof[k].x * s) : new Vector3(prof[k].x * c, prof[k].y, prof[k].x * s);
                        Vector3 n = axisX ? new Vector3(nrm[k].y, nrm[k].x * c, nrm[k].x * s) : new Vector3(nrm[k].x * c, nrm[k].y, nrm[k].x * s);
                        V.Add(origin + p); N.Add(n.normalized);
                        // grainU: wood turned on a lathe: the grain runs along the axis (u = grainU[k]), v goes around within [u0, u1]
                        if (grainU != null && grainU[k] >= 0) UV.Add(new Vector2(grainU[k], Mathf.Lerp(u0, u1, (float)j / seg)));
                        else UV.Add(new Vector2(Mathf.Lerp(u0, u1, (float)j / seg), vs[k]));
                    }
                int W = seg + 1;
                for (int k = 0; k < K - 1; k++)
                    for (int j = 0; j < seg; j++)
                    {
                        int a = b + k * W + j, up = a + W;
                        if (!axisX) { T.Add(a); T.Add(up); T.Add(a + 1); T.Add(up); T.Add(up + 1); T.Add(a + 1); }
                        else { T.Add(a); T.Add(a + 1); T.Add(up); T.Add(up); T.Add(a + 1); T.Add(up + 1); }
                    }
            }

            /// <summary>An axis-aligned box with per-face normals (planar UVs from world-scaled faces into the grain rows).</summary>
            public void Box(Vector3 c, Vector3 h, float uvScale = 2f, float grainMax = GrainVMax)
            {
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                for (int f = 0; f < 6; f++)
                {
                    int ax = f >> 1; float sg = (f & 1) == 0 ? 1 : -1;
                    Vector3 n = axes[ax] * sg, t1 = axes[(ax + 1) % 3], t2 = axes[(ax + 2) % 3];
                    if (sg < 0) { var tmp = t1; t1 = t2; t2 = tmp; }
                    int b = V.Count;
                    for (int q = 0; q < 4; q++)
                    {
                        float s1 = (q == 1 || q == 2) ? 1 : -1, s2 = q >= 2 ? 1 : -1;
                        Vector3 p = c + Vector3.Scale(n, h) + Vector3.Scale(t1 * s1, h) + Vector3.Scale(t2 * s2, h);
                        V.Add(p); N.Add(n);
                        // the grain runs along the box's longest horizontal axis
                        float gu = h.x >= h.z ? p.x : p.z, gv = h.x >= h.z ? p.z + p.y : p.x + p.y;
                        UV.Add(new Vector2(gu * uvScale + 0.5f, Mathf.Repeat(gv * uvScale + 0.31f, 1f) * grainMax));
                    }
                    T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3);
                    // Unity's front faces are clockwise seen from outside: flip when the winding points inward
                    Vector3 cr = Vector3.Cross(V[b + 1] - V[b], V[b + 2] - V[b]);
                    if (Vector3.Dot(cr, n) < 0) { int last = T.Count; T[last - 5] = b + 2; T[last - 4] = b + 1; T[last - 2] = b + 3; T[last - 1] = b + 2; }
                }
            }
        }

        static void ProfileNormals(List<Vector2> p, List<Vector2> n)
        {
            n.Clear();
            for (int k = 0; k < p.Count; k++)
            {
                Vector2 t = p[Math.Min(p.Count - 1, k + 1)] - p[Math.Max(0, k - 1)];
                n.Add(new Vector2(t.y, -t.x).normalized);
            }
        }

        // ------------------------------------------------------------------------------------------ the bowl

        /// <summary>The profile (r, y) of bowl i in its own frame (y up from the base), outer bottom centre -> foot -> outer
        /// wall -> rounded lip -> inner wall -> inner floor centre, with v (texture) per point.</summary>
        public static void BowlProfile(int i, List<Vector2> prof, List<float> vs)
        {
            prof.Clear(); vs.Clear();
            float R = BowlsLayout.R(i), H = BowlsLayout.Height[i], w = BowlsLayout.Wall[i];
            float rf = 0.55f * R;
            // the outer bottom: slightly concave (4 mm at the centre), a rounded foot
            prof.Add(new Vector2(0f, 0.004f)); prof.Add(new Vector2(rf * 0.55f, 0.0031f)); prof.Add(new Vector2(rf * 0.88f, 0.0011f)); prof.Add(new Vector2(rf - 0.0005f, 0.0006f));
            // the outer wall (denser at the foot, where it flares)
            const int nw = 8;
            for (int k = 1; k <= nw; k++)
            {
                float u = (float)k / nw, y = Mathf.Lerp(0.0015f, H - w * 0.5f, 0.55f * u * u + 0.45f * u);
                prof.Add(new Vector2((float)BowlsLayout.ROut(i, y), y));
            }
            // the lip: a half round over the top (wall / 2)
            Vector2 lc = new Vector2(R - w * 0.5f, H - w * 0.5f);
            for (int k = 1; k <= 5; k++) { float a = Mathf.PI * k / 6f; prof.Add(lc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (w * 0.5f)); }
            prof.Add(new Vector2(R - w, H - w * 0.5f));
            // the inner wall down to the floor
            for (int k = 1; k <= nw; k++)
            {
                float u = (float)k / nw, y = Mathf.Lerp(H - w * 0.5f, w + 0.004f, 1f - 0.55f * (1f - u) * (1f - u) - 0.45f * (1f - u));
                prof.Add(new Vector2((float)BowlsLayout.ROut(i, y) - w * 1.05f, y));
            }
            // the inner floor: a smooth dish to the centre
            Vector2 last = prof[prof.Count - 1];
            for (int k = 1; k <= 3; k++) { float u = k / 3f; prof.Add(new Vector2(last.x * (1 - u), Mathf.Lerp(last.y, w + 0.0012f, u * (2 - u)))); }
            // v: 0.5 at the lip's top, outer below, inner above (m of arc / ProfileSpan)
            var s = new float[prof.Count];
            for (int k = 1; k < prof.Count; k++) s[k] = s[k - 1] + Vector2.Distance(prof[k], prof[k - 1]);
            float sTop = s[4 + nw + 2];   // the lip's crown (its 3rd point)
            for (int k = 0; k < prof.Count; k++) vs.Add(Mathf.Clamp01(0.5f + (s[k] - sTop) / ProfileSpan));
        }

        /// <summary>Bowl i in its own frame (base centre at the origin). 40 segments at the seat, 20 on display.</summary>
        public static Mesh Bowl(int i, int segments = 40)
        {
            var prof = new List<Vector2>(); var vs = new List<float>(); var n = new List<Vector2>();
            BowlProfile(i, prof, vs);
            ProfileNormals(prof, n);
            var b = new Builder();
            b.Lathe(prof, n, vs, segments, Vector3.zero);
            return b.ToMesh("Bowls/Bowl" + i, true);
        }

        // ------------------------------------------------------------------------------------------ the cushions

        /// <summary>The ring cushion under bowl i (its own frame: the stand top at y = 0): a torus whose crown carries the
        /// bowl's foot ring, flattened to reach the bowl's base height (the bowl sinks 1 mm into it).</summary>
        static void Cushion(Builder b, int i, Vector3 at, int seg = 32, int tube = 12)
        {
            float R = BowlsLayout.R(i), rc = 0.55f * R - 0.0005f, tr = 0.013f;
            float sy = (BowlsLayout.CushionLift + 0.001f) / (2f * tr);
            int b0 = b.V.Count;
            for (int k = 0; k <= tube; k++)
            {
                float a = 2 * Mathf.PI * k / tube;           // around the tube, 0 = outermost
                float cr = Mathf.Cos(a), sr = Mathf.Sin(a);
                for (int j = 0; j <= seg; j++)
                {
                    float t = 2 * Mathf.PI * j / seg, ct = Mathf.Cos(t), st = Mathf.Sin(t);
                    float r = rc + tr * cr, y = tr * sy * (1 + sr);
                    b.V.Add(at + new Vector3(r * ct, y, r * st));
                    b.N.Add(new Vector3(cr * ct, sr / sy, cr * st).normalized);
                    b.UV.Add(new Vector2((float)j / seg * 3f, Mathf.Lerp(FabricV0, FabricV1, (float)k / tube)));
                }
            }
            int W = seg + 1;
            for (int k = 0; k < tube; k++)
                for (int j = 0; j < seg; j++)
                {
                    int a = b0 + k * W + j, up = a + W;
                    b.T.Add(a); b.T.Add(up); b.T.Add(a + 1); b.T.Add(up); b.T.Add(up + 1); b.T.Add(a + 1);
                }
        }

        static void Cushions(Builder b, bool stowed, int seg)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = stowed ? new Vector3(BowlsLayout.StowX[i], 0, 0) : new Vector3(BowlsLayout.BowlXZ[i].x, BowlsLayout.StandTop, BowlsLayout.BowlXZ[i].y);
                Cushion(b, i, at, stowed ? 16 : seg, stowed ? 6 : 10);
            }
        }

        static void Append(Builder b, Builder from, Matrix4x4 m)
        {
            int v0 = b.V.Count;
            for (int k = 0; k < from.V.Count; k++) { b.V.Add(m.MultiplyPoint3x4(from.V[k])); b.N.Add(m.MultiplyVector(from.N[k]).normalized); b.UV.Add(from.UV[k]); }
            foreach (int t in from.T) b.T.Add(v0 + t);
        }

        /// <summary>At the seat: the stand with the three cushions on it (one Wood-atlas mesh, rig frame: the floor at y 0).</summary>
        public static Mesh StandAndCushions(int seg = 24)
        {
            var b = new Builder();
            Stand(b);
            Cushions(b, false, seg);
            return b.ToMesh("Bowls/StandAndCushions", false);
        }

        /// <summary>On a real table in MR (the stand hidden): the cushions alone, in the same renderer.</summary>
        public static Mesh CushionsOnly(int seg = 24)
        {
            var b = new Builder();
            Cushions(b, false, seg);
            return b.ToMesh("Bowls/Cushions", false);
        }

        /// <summary>The puja in its own frame (+x from the plain end to the suede end): its own renderer, which follows the
        /// hand while held and stays when the stand hides.</summary>
        public static Mesh PujaMesh()
        {
            var b = new Builder();
            Puja(b);
            return b.ToMesh("Bowls/Puja", false);
        }

        /// <summary>On display: the cushions in a row with the puja lying in front (the sill top at y 0).</summary>
        public static Mesh DisplayCushionsAndPuja()
        {
            var b = new Builder();
            Cushions(b, true, 16);
            var pb = new Builder();
            Puja(pb);
            Append(b, pb, PujaStowedMatrix());
            return b.ToMesh("Bowls/CushionsAndPujaStowed", false);
        }

        /// <summary>The puja lying in front of the stowed row (rig frame of the display: the sill top at y 0).</summary>
        public static Matrix4x4 PujaStowedMatrix() => Matrix4x4.TRS(new Vector3(-BowlsLayout.PujaLength * 0.5f, BowlsLayout.PujaRadius, BowlsLayout.StowPujaZ), Quaternion.Euler(0, 0, 0.8f), Vector3.one);

        // ------------------------------------------------------------------------------------------ the stand

        static void RoundedSlab(Builder b, float hx, float hz, float y0, float y1, float corner, float bevel, int cornerSeg = 4)
        {
            // an outline with rounded corners, extruded with a bevel at the top and the bottom edges
            var ring = new List<Vector2>();
            Vector2[] cs = { new Vector2(hx - corner, hz - corner), new Vector2(-hx + corner, hz - corner), new Vector2(-hx + corner, -hz + corner), new Vector2(hx - corner, -hz + corner) };
            for (int c = 0; c < 4; c++)
                for (int k = 0; k <= cornerSeg; k++) { float a = (c + (float)k / cornerSeg) * 0.5f * Mathf.PI; ring.Add(cs[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * corner); }
            int n = ring.Count;
            // layers: (inset, y, normal tilt): top cap ring, bevel top, side top, side bottom, bevel bottom
            float[] inset = { bevel, 0, 0, bevel };
            float[] ys = { y1, y1 - bevel, y0 + bevel, y0 };
            int baseIdx = b.V.Count;
            for (int L = 0; L < 4; L++)
                for (int k = 0; k <= n; k++)
                {
                    Vector2 p = ring[k % n], c = ring[(k + 1) % n] - ring[(k - 1 + n) % n];
                    Vector2 outN = new Vector2(c.y, -c.x).normalized;
                    Vector2 q = p - outN * inset[L];
                    float ny = L == 0 ? 0.7f : L == 3 ? -0.7f : 0f;
                    b.V.Add(new Vector3(q.x, ys[L], q.y));
                    b.N.Add(new Vector3(outN.x * (1 - Mathf.Abs(ny)), ny, outN.y * (1 - Mathf.Abs(ny))).normalized);
                    b.UV.Add(new Vector2((float)k / n * 3f, Mathf.Repeat(ys[L] * 3f, 1f) * GrainVMax));
                }
            int W = n + 1;
            for (int L = 0; L < 3; L++)
                for (int k = 0; k < n; k++)
                {
                    int a = baseIdx + L * W + k, d = a + W;
                    b.T.Add(a); b.T.Add(a + 1); b.T.Add(d); b.T.Add(d); b.T.Add(a + 1); b.T.Add(d + 1);
                }
            // caps (fans from the centre), planar UVs: grain along x
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? y1 : y0; float inS = bevel;
                int c0 = b.V.Count;
                b.V.Add(new Vector3(0, y, 0)); b.N.Add(cap == 0 ? Vector3.up : Vector3.down); b.UV.Add(new Vector2(0.5f, 0.4f));
                for (int k = 0; k < n; k++)
                {
                    Vector2 p = ring[k], c = ring[(k + 1) % n] - ring[(k - 1 + n) % n];
                    Vector2 outN = new Vector2(c.y, -c.x).normalized, q = p - outN * inS;
                    b.V.Add(new Vector3(q.x, y, q.y)); b.N.Add(cap == 0 ? Vector3.up : Vector3.down);
                    b.UV.Add(new Vector2(0.5f + q.x * 2f, 0.42f + q.y * 2f));
                }
                for (int k = 0; k < n; k++)
                {
                    int a = c0 + 1 + k, d = c0 + 1 + (k + 1) % n;
                    if (cap == 0) { b.T.Add(c0); b.T.Add(d); b.T.Add(a); } else { b.T.Add(c0); b.T.Add(a); b.T.Add(d); }
                }
            }
        }

        static readonly Vector2[] LegProfile =
        {
            new Vector2(0.0001f, 0f), new Vector2(0.0105f, 0f), new Vector2(0.0118f, 0.006f), new Vector2(0.0122f, 0.09f), new Vector2(0.0146f, 0.124f),
            new Vector2(0.0158f, 0.135f), new Vector2(0.0158f, 0.145f), new Vector2(0.0140f, 0.158f), new Vector2(0.0128f, 0.19f), new Vector2(0.0142f, 0.47f),
            new Vector2(0.0158f, 0.505f), new Vector2(0.0162f, 0.515f), new Vector2(0.0150f, 0.53f), new Vector2(0.0160f, 0.545f), new Vector2(0.0160f, 0.588f),
        };

        static void Leg(Builder b, Vector3 foot)
        {
            var p = new List<Vector2>(LegProfile); var n = new List<Vector2>(); var vs = new List<float>(); var gu = new List<float>();
            ProfileNormals(p, n);
            for (int k = 0; k < p.Count; k++) { vs.Add(0); gu.Add(p[k].y * 1.6f + foot.x * 3f); }
            b.Lathe(p, n, vs, 8, foot, 0.1f + Mathf.Abs(foot.z) * 2f, 0.16f + Mathf.Abs(foot.z) * 2f, false, gu);
        }

        static void Bar(Builder b, Vector3 a, Vector3 c, float r, int seg = 8)
        {
            Vector3 d = c - a; float L = d.magnitude; Vector3 ax = d / L;
            Vector3 up = Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 e1 = Vector3.Cross(ax, up).normalized, e2 = Vector3.Cross(ax, e1);
            int b0 = b.V.Count;
            for (int end = 0; end < 2; end++)
                for (int j = 0; j <= seg; j++)
                {
                    float t = 2 * Mathf.PI * j / seg;
                    Vector3 nn = e1 * Mathf.Cos(t) + e2 * Mathf.Sin(t);
                    b.V.Add((end == 0 ? a : c) + nn * r); b.N.Add(nn); b.UV.Add(new Vector2(end * L * 2.5f, (float)j / seg * 0.2f));
                }
            int W = seg + 1;
            for (int j = 0; j < seg; j++)
            {
                int q = b0 + j, w = q + W;
                b.T.Add(q); b.T.Add(w); b.T.Add(q + 1); b.T.Add(w); b.T.Add(w + 1); b.T.Add(q + 1);
                // fix the winding to face outward
                int last = b.T.Count;
                Vector3 cr = Vector3.Cross(b.V[w] - b.V[q], b.V[q + 1] - b.V[q]);
                if (Vector3.Dot(cr, b.N[q]) < 0) { b.T[last - 5] = q + 1; b.T[last - 4] = w; b.T[last - 2] = q + 1; b.T[last - 1] = w + 1; b.T[last - 3] = w; }
            }
        }

        /// <summary>The walnut stand (rig frame: the floor at y = 0).</summary>
        static void Stand(Builder b)
        {
            float hx = BowlsLayout.StandHx, hz = BowlsLayout.StandHz, top = BowlsLayout.StandTop, th = BowlsLayout.TopThick;
            RoundedSlab(b, hx, hz, top - th, top, 0.012f, 0.004f);
            // the raised rim: a thin frame 8 mm wide, 3 mm proud of the top
            float rw = 0.008f, lip = BowlsLayout.TopLip;
            b.Box(new Vector3(0, top + lip * 0.5f, hz - rw * 0.5f - 0.002f), new Vector3(hx - 0.012f, lip * 0.5f, rw * 0.5f));
            b.Box(new Vector3(0, top + lip * 0.5f, -hz + rw * 0.5f + 0.002f), new Vector3(hx - 0.012f, lip * 0.5f, rw * 0.5f));
            b.Box(new Vector3(hx - rw * 0.5f - 0.002f, top + lip * 0.5f, 0), new Vector3(rw * 0.5f, lip * 0.5f, hz - 0.012f));
            b.Box(new Vector3(-hx + rw * 0.5f + 0.002f, top + lip * 0.5f, 0), new Vector3(rw * 0.5f, lip * 0.5f, hz - 0.012f));
            // aprons under the top
            float ax = hx - 0.032f, az = hz - 0.032f, ay = top - th - 0.025f;
            b.Box(new Vector3(0, ay, az), new Vector3(ax - 0.016f, 0.025f, 0.009f));
            b.Box(new Vector3(0, ay, -az), new Vector3(ax - 0.016f, 0.025f, 0.009f));
            b.Box(new Vector3(ax, ay, 0), new Vector3(0.009f, 0.025f, az - 0.016f));
            b.Box(new Vector3(-ax, ay, 0), new Vector3(0.009f, 0.025f, az - 0.016f));
            // the legs and the stretchers at 0.14 m
            Vector3[] feet = { new Vector3(ax, 0, az), new Vector3(-ax, 0, az), new Vector3(-ax, 0, -az), new Vector3(ax, 0, -az) };
            foreach (var f in feet) Leg(b, f);
            float sy = BowlsLayout.StretcherY;
            for (int k = 0; k < 4; k++) Bar(b, feet[k] + Vector3.up * sy, feet[(k + 1) % 4] + Vector3.up * sy, 0.0075f);
        }

        /// <summary>The puja in its own frame: the axis along +x from the plain end (x = 0) to the suede end (x = 0.15).</summary>
        static void Puja(Builder b)
        {
            float L = BowlsLayout.PujaLength, r = BowlsLayout.PujaRadius, rs = BowlsLayout.PujaSuedeRadius, sl = BowlsLayout.PujaSuedeLength;
            var p = new List<Vector2>
            {
                new Vector2(0.0001f, 0f), new Vector2(r * 0.6f, 0.0006f), new Vector2(r * 0.92f, 0.003f), new Vector2(r, 0.007f),
                new Vector2(r * 1.02f, 0.06f), new Vector2(r, L - sl - 0.002f), new Vector2(r * 0.96f, L - sl),
                // the suede wrap (a soft round end)
                new Vector2(rs * 0.96f, L - sl), new Vector2(rs, L - sl + 0.004f), new Vector2(rs, L - 0.012f), new Vector2(rs * 0.86f, L - 0.004f),
                new Vector2(rs * 0.5f, L - 0.0005f), new Vector2(0.0001f, L),
            };
            var n = new List<Vector2>(); ProfileNormals(p, n);
            var vs = new List<float>(); var gu = new List<float>();
            for (int k = 0; k < p.Count; k++)
            {
                bool suede = k >= 7;
                vs.Add(suede ? Mathf.Lerp(SuedeV0 + 0.02f, 0.99f, (p[k].y - (L - sl)) / sl) : 0f);
                gu.Add(suede ? -1f : 0.2f + p[k].y * 2.2f);     // the shaft's grain runs along the stick
            }
            // the shaft: u along the stick, v around within a narrow band of grain rows; the suede wrap: its own rows
            var shaftP = new List<Vector2>(); var shaftN = new List<Vector2>(); var shaftV = new List<float>(); var shaftU = new List<float>();
            var sueP = new List<Vector2>(); var sueN = new List<Vector2>(); var sueV = new List<float>();
            for (int k = 0; k < p.Count; k++)
            {
                if (k <= 7) { shaftP.Add(k == 7 ? p[6] + new Vector2(-0.0004f, 0.0001f) : p[k]); shaftN.Add(n[Mathf.Min(k, 6)]); shaftV.Add(0); shaftU.Add(0.2f + p[Mathf.Min(k, 6)].y * 2.2f); }
                if (k >= 7) { sueP.Add(p[k]); sueN.Add(n[k]); sueV.Add(vs[k]); }
            }
            b.Lathe(shaftP, shaftN, shaftV, 12, Vector3.zero, 0.42f, 0.5f, axisX: true, grainU: shaftU);
            b.Lathe(sueP, sueN, sueV, 12, Vector3.zero, 0f, 1f, axisX: true);
        }

        /// <summary>The puja's rest pose in the rig frame (lying along x on the stand top, suede end toward +x).</summary>
        public static Matrix4x4 PujaRestMatrix()
        {
            Vector3 c = BowlsLayout.PujaRestCentre;
            // lift the suede end onto its thicker wrap: a slight tilt about z
            float tilt = Mathf.Atan2(BowlsLayout.PujaSuedeRadius - BowlsLayout.PujaRadius, BowlsLayout.PujaLength * 0.8f);
            return Matrix4x4.TRS(c - new Vector3(BowlsLayout.PujaLength * 0.5f, 0, 0), Quaternion.Euler(0, 0, tilt * Mathf.Rad2Deg), Vector3.one);
        }

        /// <summary>The puja's suede end and plain end in its own frame (+x axis).</summary>
        public static Vector3 PujaSuedeEndLocal => new Vector3(BowlsLayout.PujaLength - BowlsLayout.PujaSuedeLength * 0.4f, 0, 0);
        public static Vector3 PujaPlainEndLocal => new Vector3(0.004f, 0, 0);
        public static Vector3 PujaGripLocal => new Vector3(BowlsLayout.PujaLength - BowlsLayout.PujaGrip, 0, 0);
    }
}
