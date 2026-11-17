// The handpan's procedural meshes (handpan.md 7.1, 7.2; platform.md 11 budgets: <= 25 k triangles at the seat, <= 8 k on
// display), built once. Instrument-local frame (rim plane y = 0, the top apex at y = TopH) for the shell and the edge band;
// rig-local (the stand's floor point at the origin) for the stand and the wall bracket.
//   Shell (steel, one mesh): the top cap as a polar grid in its map (40 rings x 128 segments at the seat: vertices 7-9 mm
//   apart; the relief displaced along the cap normal, normals and tangents from the exact surface; the hammer dents and
//   tone rings are in the normal map), the bottom cap down to the gu, the gu's inward-rolled 12 mm lip, and dark inner
//   surfaces (so the gu never shows the room through the shell). The display LOD (on the wall bracket) has no lip and no
//   inner surfaces: the gu faces the wall.
//   Band (rubber): a lathe over the seam; the display LOD also carries the wall bracket's two rubber-coated hooks and its
//   wall plate (one draw).
//   Stand (walnut + felt): a cradle tripod. Three felt-topped rests hold the bottom cap around the gu; the two near rests
//   sit 0.63 m up and their arms run back and out before the legs go down, so nothing of the stand is in the space of the
//   knees (the seat planner's knee box: x +-0.22 m, 0.09-0.41 m ahead of the eyes, 0.40-0.62 m up; review item 9).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Handpan
{
    public static class HandpanGeometry
    {
        /// <summary>Mesh resolutions: the seat model and the display LOD.</summary>
        public struct Lod { public int TopRings, TopSegs, BotRings, BotSegs, BandSegs; public bool Inside; }
        public static readonly Lod Seat = new Lod { TopRings = 40, TopSegs = 128, BotRings = 14, BotSegs = 64, BandSegs = 128, Inside = true };
        public static readonly Lod Display = new Lod { TopRings = 18, TopSegs = 64, BotRings = 4, BotSegs = 32, BandSegs = 64, Inside = false };
        public const int LipRings = 6, LipSegs = 48, BandRows = 6;
        /// <summary>Gu: the outer cap ends at this horizontal radius; the lip rolls in over LipR to a 45 mm neck, 12 mm deep.</summary>
        public const double GuEdgeR = 0.049, LipR = 0.004;
        /// <summary>The cradle's rests on the bottom cap (instrument-local horizontal radius and map azimuth, 0 = toward the
        /// player): two near rests either side of the gu (their contact 0.636 m up) and one far rest.</summary>
        public static readonly double[] RestRho = { 0.11, 0.11, 0.12 }, RestAz = { 60, 300, 180 };
        /// <summary>Each leg's elbow (rig-local: where the arm from its rest turns down; the near ones stay above 0.62 m, out of
        /// the knee box) and foot (on the floor).</summary>
        public static readonly Vector3[] Elbow = { new Vector3(0.175f, 0.618f, 0.072f), new Vector3(-0.175f, 0.618f, 0.072f), new Vector3(0f, 0.66f, 0.21f) };
        public static readonly Vector3[] Foot = { new Vector3(0.30f, 0f, 0.13f), new Vector3(-0.30f, 0f, 0.13f), new Vector3(0f, 0f, 0.30f) };
        public const float ArmR = 0.0085f, LegTopR = 0.011f, LegFootR = 0.009f, RestR = 0.022f, RestT = 0.012f, FeltT = 0.004f;
        /// <summary>The display pose (rig-local, the rig at its Stow pose on the wall): the rim centre 1.58 m up, 5 mm off the
        /// box centre, the top facing out of the wall (-z), the far fields up.</summary>
        public static readonly Vector3 DisplayCentre = new Vector3(0f, 1.58f, 0.005f);
        public static readonly Quaternion DisplayTilt = Quaternion.Euler(-90f, 0f, 0f);
        /// <summary>The wall (rig-local z) behind the displayed instrument: the bracket's plate.</summary>
        public const float WallZ = 0.135f;

        sealed class MeshBuilder
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector4> T = new List<Vector4>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> I = new List<int>();
            public int Add(Vector3 p, Vector3 n, Vector4 t, Vector2 uv) { P.Add(p); N.Add(n); T.Add(t); UV.Add(uv); return P.Count - 1; }
            /// <summary>A triangle, wound so that its face normal agrees with the vertex normals (Unity: clockwise = front).</summary>
            public void Tri(int a, int b, int c)
            {
                Vector3 fn = Vector3.Cross(P[b] - P[a], P[c] - P[a]);
                Vector3 vn = N[a] + N[b] + N[c];
                if (Vector3.Dot(fn, vn) >= 0) { I.Add(a); I.Add(b); I.Add(c); } else { I.Add(a); I.Add(c); I.Add(b); }
            }
            public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = P.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(P); m.SetNormals(N); m.SetTangents(T); m.SetUVs(0, UV); m.SetTriangles(I, 0, true);
                m.UploadMeshData(false);
                return m;
            }
        }

        static Vector3 V(D3 d) => new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        static D3 D(Vector3 v) => new D3(v.x, v.y, v.z);

        /// <summary>A surface vertex from a position function over map coordinates: exact normal and tangent (+X = +U).</summary>
        static int SurfVertex(MeshBuilder b, Func<double, double, D3> f, double X, double Z, Vector2 uv)
        {
            const double e = 2e-4;
            D3 p = f(X, Z);
            D3 tx = (f(X + e, Z) - f(X - e, Z)) * (1 / (2 * e)), tz = (f(X, Z + e) - f(X, Z - e)) * (1 / (2 * e));
            D3 n = D3.Cross(tz, tx).Normalized;
            D3 t = tx.Normalized;
            double w = D3.Dot(D3.Cross(n, t), tz) < 0 ? -1 : 1;
            return b.Add(V(p), V(n), new Vector4((float)t.X, (float)t.Y, (float)t.Z, (float)w), uv);
        }

        /// <summary>The top cap with the relief (capPoint).</summary>
        public static D3 TopPoint(double X, double Z) => HandpanMath.CapPoint(X, Z, true, out _);

        /// <summary>The bottom cap at its own geodesic map point (Xb, Zb) from the bottom apex (instrument-local).</summary>
        public static D3 BottomPoint(double X, double Z)
        {
            double s = Math.Sqrt(X * X + Z * Z), R = HandpanSpec.BotR, th = s / R;
            double dx = s > 1e-12 ? X / s : 0, dz = s > 1e-12 ? Z / s : 0;
            double rho = R * Math.Sin(th);
            return new D3(rho * dx, -HandpanSpec.BotH + R * (1 - Math.Cos(th)), rho * dz);
        }

        /// <summary>The bottom cap's height at horizontal radius rho (instrument-local y).</summary>
        public static double BottomY(double rho) { double R = HandpanSpec.BotR; return -HandpanSpec.BotH + R - Math.Sqrt(R * R - rho * rho); }

        // ---------------------------------------------------------------- the shell

        public static Mesh Shell(Lod q)
        {
            var b = new MeshBuilder();
            // ---- top cap (polar grid in the map; ring 0 is the apex)
            int apex = SurfVertex(b, TopPoint, 0, 0, HandpanTextures.TopUv(0, 0));
            var rows = new int[q.TopRings + 1][];
            for (int k = 1; k <= q.TopRings; k++)
            {
                double s = HandpanSpec.RimArc * k / q.TopRings;
                rows[k] = new int[q.TopSegs + 1];
                for (int j = 0; j <= q.TopSegs; j++)
                {
                    double a = 2 * Math.PI * j / q.TopSegs, X = s * Math.Sin(a), Z = -s * Math.Cos(a);
                    rows[k][j] = SurfVertex(b, TopPoint, X, Z, HandpanTextures.TopUv(X, Z));
                }
            }
            for (int j = 0; j < q.TopSegs; j++) b.Tri(apex, rows[1][j], rows[1][j + 1]);
            for (int k = 1; k < q.TopRings; k++) for (int j = 0; j < q.TopSegs; j++) b.Quad(rows[k][j], rows[k + 1][j], rows[k + 1][j + 1], rows[k][j + 1]);
            // ---- bottom cap, from the rim down to the gu's edge (the display LOD closes it at the apex: the gu faces the wall)
            double sGu = q.Inside ? HandpanSpec.BotR * Math.Asin(GuEdgeR / HandpanSpec.BotR) : 0;
            var brow = new int[q.BotRings + 1][];
            for (int k = q.Inside ? 0 : 1; k <= q.BotRings; k++)
            {
                double s = sGu + (HandpanSpec.BotArc - sGu) * k / q.BotRings;
                brow[k] = new int[q.BotSegs + 1];
                for (int j = 0; j <= q.BotSegs; j++)
                {
                    double a = 2 * Math.PI * j / q.BotSegs, X = s * Math.Sin(a), Z = -s * Math.Cos(a);
                    brow[k][j] = BottomVertex(b, X, Z);
                }
            }
            if (!q.Inside)
            {
                int bottomApex = BottomVertex(b, 0, 0);
                for (int j = 0; j < q.BotSegs; j++) b.Tri(bottomApex, brow[1][j], brow[1][j + 1]);
            }
            for (int k = q.Inside ? 0 : 1; k < q.BotRings; k++) for (int j = 0; j < q.BotSegs; j++) b.Quad(brow[k][j], brow[k + 1][j], brow[k + 1][j + 1], brow[k][j + 1]);
            if (q.Inside)
            {
                Lip(b);
                InnerCap(b, top: true);
                InnerCap(b, top: false);
            }
            return b.Build(q.Inside ? "Handpan/Shell" : "Handpan/ShellDisplay");
        }

        static int BottomVertex(MeshBuilder b, double X, double Z)
        {
            int vtx = SurfVertex(b, BottomPoint, X, Z, HandpanTextures.BottomUv(X, Z));
            // the bottom's outward normal points away from the cavity (down): flip the grid's (which faces +y)
            b.N[vtx] = -b.N[vtx];
            Vector4 t = b.T[vtx]; t.w = -t.w; b.T[vtx] = t;
            return vtx;
        }

        /// <summary>The gu's rolled lip: a quarter round of LipR turning up into the cavity, then the neck to 12 mm.</summary>
        static void Lip(MeshBuilder b)
        {
            double ye = BottomY(GuEdgeR);
            var lip = new int[LipRings + 1][];
            for (int k = 0; k <= LipRings; k++)
            {
                double t = (double)k / LipRings;
                double rho, y, nr, ny;
                if (t <= 0.5)
                {
                    double psi = Math.PI / 2 + (Math.PI / 2) * (t / 0.5);
                    rho = GuEdgeR + LipR * Math.Cos(psi); y = ye + LipR - LipR * Math.Sin(psi);
                    nr = Math.Cos(psi); ny = -Math.Sin(psi);
                }
                else
                {
                    rho = GuEdgeR - LipR; y = ye + LipR + (HandpanSpec.GuLip - LipR) * ((t - 0.5) / 0.5);
                    nr = -1; ny = 0;
                }
                double suv = 0.046 - 0.026 * t;   // steel at the rim of the hole, the dark inner steel deeper in
                lip[k] = new int[LipSegs + 1];
                for (int j = 0; j <= LipSegs; j++)
                {
                    double a = 2 * Math.PI * j / LipSegs, sx = Math.Sin(a), cz = -Math.Cos(a);
                    var p = new Vector3((float)(rho * sx), (float)y, (float)(rho * cz));
                    var n = new Vector3((float)(nr * sx), (float)ny, (float)(nr * cz));
                    var tan = new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                    lip[k][j] = b.Add(p, n.normalized, new Vector4(tan.x, tan.y, tan.z, 1), HandpanTextures.BottomUv(suv * sx, suv * cz));
                }
            }
            for (int k = 0; k < LipRings; k++) for (int j = 0; j < LipSegs; j++) b.Quad(lip[k][j], lip[k + 1][j], lip[k + 1][j + 1], lip[k][j + 1]);
        }

        static void InnerCap(MeshBuilder b, bool top)
        {
            const int rings = 8, segs = 24;
            double th = HandpanSpec.Steel;
            double R = top ? HandpanSpec.CapR - th : HandpanSpec.BotR - th;
            double cy = top ? HandpanSpec.TopH - HandpanSpec.CapR : -HandpanSpec.BotH + HandpanSpec.BotR;
            double rim = HandpanSpec.RimR - 0.002;
            double thMax = Math.Asin(rim / R), thMin = top ? 0 : Math.Asin((GuEdgeR - LipR) / R);
            var rows = new int[rings + 1][];
            for (int k = 0; k <= rings; k++)
            {
                double t = thMin + (thMax - thMin) * k / rings;
                rows[k] = new int[segs + 1];
                for (int j = 0; j <= segs; j++)
                {
                    double a = 2 * Math.PI * j / segs, sx = Math.Sin(a), cz = -Math.Cos(a);
                    double rho = R * Math.Sin(t), y = top ? cy + R * Math.Cos(t) : cy - R * Math.Cos(t);
                    var p = new Vector3((float)(rho * sx), (float)y, (float)(rho * cz));
                    var c = new Vector3(0, (float)cy, 0);
                    Vector3 n = (c - p).normalized;   // toward the sphere centre = into the cavity
                    rows[k][j] = b.Add(p, n, new Vector4(1, 0, 0, 1), HandpanTextures.InnerUv(0.5));
                }
            }
            for (int k = 0; k < rings; k++) for (int j = 0; j < segs; j++) b.Quad(rows[k][j], rows[k + 1][j], rows[k + 1][j + 1], rows[k][j + 1]);
        }

        // ---------------------------------------------------------------- the edge band (and, on display, the bracket)

        /// <summary>The band in instrument-local coordinates; with displayToRig (the display LOD) the bracket is added in the
        /// same mesh, mapped back from rig-local coordinates into the instrument's frame.</summary>
        public static Mesh Band(Lod q, Func<Vector3, Vector3> rigToInstrument = null)
        {
            var b = new MeshBuilder();
            double rTop = Math.Sqrt(HandpanSpec.CapR * HandpanSpec.CapR - Math.Pow(0.0115 - (HandpanSpec.TopH - HandpanSpec.CapR), 2)) + 0.0005;
            double rBot = Math.Sqrt(HandpanSpec.BotR * HandpanSpec.BotR - Math.Pow(-0.0115 - (-HandpanSpec.BotH + HandpanSpec.BotR), 2)) + 0.0005;
            var prof = new (double r, double y, double nr, double ny)[BandRows + 1];
            for (int k = 0; k <= BandRows; k++)
            {
                double phi = Math.PI / 2 - Math.PI * k / BandRows;    // +90 (top edge) .. -90 (bottom edge)
                double r0 = k <= BandRows / 2 ? rTop : rBot;
                double r = r0 + (HandpanSpec.RimR + 0.0035 - r0) * Math.Cos(phi), y = 0.0115 * Math.Sin(phi);
                double drdphi = -(HandpanSpec.RimR + 0.0035 - r0) * Math.Sin(phi), dydphi = 0.0115 * Math.Cos(phi);
                double nr = dydphi, ny = -drdphi, l = Math.Sqrt(nr * nr + ny * ny);
                prof[k] = (r, y, nr / l, ny / l);
            }
            var rows = new int[BandRows + 1][];
            for (int k = 0; k <= BandRows; k++)
            {
                rows[k] = new int[q.BandSegs + 1];
                for (int j = 0; j <= q.BandSegs; j++)
                {
                    double a = 2 * Math.PI * j / q.BandSegs, sx = Math.Sin(a), cz = -Math.Cos(a);
                    var p = new Vector3((float)(prof[k].r * sx), (float)prof[k].y, (float)(prof[k].r * cz));
                    var n = new Vector3((float)(prof[k].nr * sx), (float)prof[k].ny, (float)(prof[k].nr * cz)).normalized;
                    rows[k][j] = b.Add(p, n, new Vector4((float)Math.Cos(a), 0, (float)Math.Sin(a), 1), new Vector2((float)j / q.BandSegs, (float)k / BandRows));
                }
            }
            for (int k = 0; k < BandRows; k++) for (int j = 0; j < q.BandSegs; j++) b.Quad(rows[k][j], rows[k + 1][j], rows[k + 1][j + 1], rows[k][j + 1]);
            if (rigToInstrument != null)
            {
                // the wall bracket (rig-local, the rig at its wall pose): a plate on the wall and two padded hooks under the rim
                float rimBottom = DisplayCentre.y - (float)(HandpanSpec.RimR + 0.0035);
                Box(b, rigToInstrument, new Vector3(0f, rimBottom - 0.02f, WallZ - 0.004f), new Vector3(0.13f, 0.03f, 0.004f));
                foreach (float x in new[] { -0.085f, 0.085f })
                {
                    Box(b, rigToInstrument, new Vector3(x, rimBottom - 0.008f, (WallZ - 0.008f + DisplayCentre.z - 0.03f) * 0.5f), new Vector3(0.012f, 0.008f, (WallZ - 0.008f - DisplayCentre.z + 0.03f) * 0.5f));
                    Box(b, rigToInstrument, new Vector3(x, rimBottom + 0.004f, DisplayCentre.z - 0.03f), new Vector3(0.012f, 0.014f, 0.005f));
                }
            }
            return b.Build(rigToInstrument != null ? "Handpan/BandDisplay" : "Handpan/Band");
        }

        /// <summary>An axis-aligned box (centre, half sizes) in rig-local coordinates, mapped into another frame.</summary>
        static void Box(MeshBuilder b, Func<Vector3, Vector3> map, Vector3 c, Vector3 h)
        {
            Vector3 o = map(Vector3.zero);
            for (int face = 0; face < 6; face++)
            {
                int axis = face >> 1; float sign = (face & 1) == 0 ? 1f : -1f;
                Vector3 n = Vector3.zero; n[axis] = sign;
                Vector3 u = Vector3.zero, v = Vector3.zero; u[(axis + 1) % 3] = 1f; v[(axis + 2) % 3] = 1f;
                Vector3 fc = c + Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                Vector3 nm = (map(n) - o).normalized;
                Vector3 tm = (map(u) - o).normalized;
                int i0 = b.Add(map(fc - du - dv), nm, new Vector4(tm.x, tm.y, tm.z, 1), Vector2.zero);
                int i1 = b.Add(map(fc + du - dv), nm, new Vector4(tm.x, tm.y, tm.z, 1), Vector2.right);
                int i2 = b.Add(map(fc + du + dv), nm, new Vector4(tm.x, tm.y, tm.z, 1), Vector2.one);
                int i3 = b.Add(map(fc - du + dv), nm, new Vector4(tm.x, tm.y, tm.z, 1), Vector2.up);
                b.Quad(i0, i1, i2, i3);
            }
        }

        // ---------------------------------------------------------------- the stand (rig-local: the floor point at the origin)

        /// <summary>A tapered 8-sided tube from a (radius ra) to b (radius rb), walnut UVs.</summary>
        static void Tube(MeshBuilder b, Vector3 a, Vector3 c, float ra, float rc, float uOffset, int rowsAlong = 1)
        {
            const int sides = 8;
            Vector3 axis = (c - a).normalized;
            Vector3 side = Vector3.Cross(axis, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Vector3 fwd = Vector3.Cross(side, axis).normalized;
            var ring = new int[rowsAlong + 1][];
            for (int k = 0; k <= rowsAlong; k++)
            {
                float t = (float)k / rowsAlong;
                Vector3 p0 = Vector3.Lerp(a, c, t);
                float r = Mathf.Lerp(ra, rc, t);
                ring[k] = new int[sides + 1];
                for (int s = 0; s <= sides; s++)
                {
                    float q = 2 * Mathf.PI * s / sides;
                    Vector3 n = side * Mathf.Cos(q) + fwd * Mathf.Sin(q);
                    ring[k][s] = b.Add(p0 + n * r, n, new Vector4(axis.x, axis.y, axis.z, 1), HandpanTextures.WalnutUv(uOffset + 0.12 * s / sides, t));
                }
            }
            for (int k = 0; k < rowsAlong; k++) for (int s = 0; s < sides; s++) b.Quad(ring[k][s], ring[k + 1][s], ring[k + 1][s + 1], ring[k][s + 1]);
        }

        /// <summary>A disc cap (the end of a tube or a foot pad): centre, normal, radius, felt or walnut UVs.</summary>
        static void Disc(MeshBuilder b, Vector3 c, Vector3 n, float r, bool felt)
        {
            const int sides = 8;
            Vector3 side = Vector3.Cross(n, Vector3.forward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Vector3 fwd = Vector3.Cross(side, n).normalized;
            Vector2 Uv(double u, double v) => felt ? HandpanTextures.FeltUv(u, v) : HandpanTextures.WalnutUv(u * 0.2, v * 0.2);
            int centre = b.Add(c, n, new Vector4(side.x, side.y, side.z, 1), Uv(0.5, 0.5));
            var ring = new int[sides + 1];
            for (int s = 0; s <= sides; s++)
            {
                float q = 2 * Mathf.PI * s / sides;
                ring[s] = b.Add(c + (side * Mathf.Cos(q) + fwd * Mathf.Sin(q)) * r, n, new Vector4(side.x, side.y, side.z, 1), Uv(0.5 + 0.45 * Mathf.Cos(q), 0.5 + 0.45 * Mathf.Sin(q)));
            }
            for (int s = 0; s < sides; s++) b.Tri(centre, ring[s], ring[s + 1]);
        }

        /// <summary>A rest's contact point on the bottom cap (instrument-local) and the cap's outward normal there.</summary>
        public static D3 RestPoint(int i, out D3 normal)
        {
            double az = RestAz[i] * Math.PI / 180, rho = RestRho[i], sx = Math.Sin(az), cz = -Math.Cos(az);
            double R = HandpanSpec.BotR, cy = -HandpanSpec.BotH + R;
            var p = new D3(rho * sx, BottomY(rho), rho * cz);
            normal = (p - new D3(0, cy, 0)).Normalized;   // away from the bottom sphere's centre = down and out
            return p;
        }

        /// <summary>The cradle tripod; toRig maps instrument-local points (the shell's rest pose) into the rig frame.</summary>
        public static Mesh Stand(Func<D3, D3> toRig)
        {
            var b = new MeshBuilder();
            for (int i = 0; i < 3; i++)
            {
                D3 contact = RestPoint(i, out D3 nLocal);
                Vector3 n = V((toRig(contact + nLocal) - toRig(contact)).Normalized);   // out of the shell: down into the rest
                Vector3 c = V(toRig(contact));
                // the rest: a felt pad on a short walnut block, under the shell's surface
                Vector3 feltTop = c + n * 0.0005f, feltBottom = c + n * (0.0005f + FeltT), blockBottom = feltBottom + n * RestT;
                Disc(b, feltTop, -n, RestR, felt: true);
                TubeFelt(b, feltTop, feltBottom, RestR);
                Tube(b, feltBottom, blockBottom, RestR, RestR * 0.8f, 0.1f * i);
                // the arm: from under the rest back and out to the elbow, then the leg down to the floor
                Vector3 armStart = blockBottom + n * ArmR * 0.5f;
                Tube(b, armStart, Elbow[i], ArmR, ArmR, 0.3f + 0.1f * i, 2);
                Tube(b, Elbow[i], Foot[i] + Vector3.up * 0.006f, LegTopR, LegFootR, 0.55f + 0.1f * i, 4);
                Disc(b, Elbow[i] + (Elbow[i] - Foot[i]).normalized * 0.001f, (Elbow[i] - Foot[i]).normalized, LegTopR, felt: false);
                // a felt foot pad
                Disc(b, Foot[i] + Vector3.up * 0.0005f, Vector3.down, LegFootR + 0.004f, felt: true);
                TubeFelt(b, Foot[i] + Vector3.up * 0.006f, Foot[i] + Vector3.up * 0.0005f, LegFootR + 0.004f);
            }
            return b.Build("Handpan/Stand");
        }

        /// <summary>A short felt band (the side of a pad).</summary>
        static void TubeFelt(MeshBuilder b, Vector3 a, Vector3 c, float r)
        {
            const int sides = 8;
            Vector3 axis = (c - a).normalized;
            Vector3 side = Vector3.Cross(axis, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Vector3 fwd = Vector3.Cross(side, axis).normalized;
            var r0 = new int[sides + 1]; var r1 = new int[sides + 1];
            for (int s = 0; s <= sides; s++)
            {
                float q = 2 * Mathf.PI * s / sides;
                Vector3 n = side * Mathf.Cos(q) + fwd * Mathf.Sin(q);
                r0[s] = b.Add(a + n * r, n, new Vector4(axis.x, axis.y, axis.z, 1), HandpanTextures.FeltUv((double)s / sides, 0.1));
                r1[s] = b.Add(c + n * r, n, new Vector4(axis.x, axis.y, axis.z, 1), HandpanTextures.FeltUv((double)s / sides, 0.3));
            }
            for (int s = 0; s < sides; s++) b.Quad(r0[s], r1[s], r1[s + 1], r0[s + 1]);
        }

        /// <summary>Triangle counts (the triangle budget, platform.md 11).</summary>
        public static int Triangles(Mesh m) => (int)(m.GetIndexCount(0) / 3);
    }
}
