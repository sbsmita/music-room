// The pair's procedural geometry (handdrums.md 4.1, with review item 14's fixes): rig-local, origin on the floor under
// the midpoint of the head centres, +z away from the player, the heads tilted 15 degrees toward the player.
//  - Shells: staved ash barrels (12 / 13 staves), each stave its own UV strip (u 0..1 across, v along), 4 facets per
//    stave on the circle and a 0.6 mm crease at every joint (geometry), an inner wall and the bottom lip; one mesh.
//  - Heads: rawhide on a polar grid of 16 rings x 64 segments plus the centre (the dent grid; ring 16 is the bearing edge
//    and never moves) and 4 static rings where the hide wraps the bearing edge down under the crown hoop; one mesh.
//  - Hardware: crown hoops, 6 lugs per drum (side plates, tension hooks over the hoop, nuts), the chrome bottom bands,
//    and the basket stands (ring, 3 satin-black legs, rubber feet), UV-mapped into the hardware atlas; one mesh.
// Shell length: 0.72 m along the axis (the spec's 0.762 with the head at 0.75 m and the 15 degree tilt would put the far
// bottom edge 1.6 cm under the floor); a 28.3" conga, inside the WIKI range (~75 cm, 28-30").
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    /// <summary>The pair's dimensions (handdrums.md 4.1, 4.3).</summary>
    public static class HandDrumsDims
    {
        public const float TiltDeg = 15f;
        public static readonly Vector3[] HeadCentre = { new Vector3(-0.185f, 0.750f, 0f), new Vector3(0.19f, 0.755f, 0f) };
        /// <summary>Vibrating radius a (the stroke logic's), the crown hoop's radius and tube.</summary>
        public static readonly float[] A = { 0.146f, 0.156f }, HoopR = { 0.157f, 0.167f };
        public const float HoopTube = 0.005f, HoopAbove = 0.006f;
        public static readonly int[] Staves = { 12, 13 };
        /// <summary>Shell profile radii: top (under the bearing edge), belly (at 45 % from the bottom), bottom opening.</summary>
        public static readonly float[] RTop = { 0.150f, 0.160f }, RBelly = { 0.1675f, 0.180f }, RBottom = { 0.1175f, 0.1275f };
        public const float ShellLength = 0.72f, BellyAt = 0.45f, Wall = 0.009f;
        public const int HeadRings = 16, HeadSegs = 64;
        /// <summary>The hide's wrap beyond the bearing edge: (radius over a, height re the head plane) per static ring.</summary>
        public static readonly Vector2[] Wrap = { new Vector2(0.0018f, -0.0012f), new Vector2(0.0034f, -0.0038f), new Vector2(0.0044f, -0.0085f), new Vector2(0.0050f, -0.0160f) };
        public const int Lugs = 6;
        public const float LugBelowRim = 0.12f, LugLen = 0.070f, LugWidth = 0.018f, LugThick = 0.006f;
        public const float StandRingAt = 0.13f, StandFootR = 0.165f;

        public static Quaternion HeadRotation => Quaternion.Euler(-TiltDeg, 0f, 0f);
        /// <summary>The head normal (rig-local): up, tilted toward the player (-z).</summary>
        public static Vector3 Normal => HeadRotation * Vector3.up;
        /// <summary>A point of drum d's head plane at (rho, theta) of the stroke logic's frame (theta 0 toward the player,
        /// e1 = n x e0 = -x), lifted h along the normal.</summary>
        public static Vector3 HeadPoint(int d, float rho, float theta, float h = 0f)
        {
            Quaternion q = HeadRotation;
            Vector3 e0 = q * Vector3.back, e1 = Vector3.Cross(q * Vector3.up, e0);
            return HeadCentre[d] + (e0 * Mathf.Cos(theta) + e1 * Mathf.Sin(theta)) * (rho * A[d]) + q * Vector3.up * h;
        }

        /// <summary>The shell's outer radius at axis fraction t (0 bottom .. 1 top): a smooth barrel through the three radii.</summary>
        public static float ShellRadius(int d, float t)
        {
            if (t >= BellyAt)
            {
                float x = (t - BellyAt) / (1 - BellyAt);
                return Mathf.Lerp(RBelly[d], RTop[d], x * x * (1.15f - 0.15f * x));
            }
            float y = 1 - t / BellyAt;   // 0 at the belly, 1 at the bottom
            return Mathf.Lerp(RBelly[d], RBottom[d], Mathf.Pow(y, 1.7f));
        }

        /// <summary>A point on drum d's shell axis at fraction t (0 bottom .. 1 top), rig-local.</summary>
        public static Vector3 Axis(int d, float t) => HeadCentre[d] - Normal * (TopBelowHead + (1 - t) * ShellLength);
        /// <summary>The shell's top edge sits this far below the head plane (the bearing edge under the hide).</summary>
        public const float TopBelowHead = 0.004f;
    }

    /// <summary>A small mesh accumulator (positions, normals, uvs, triangles), merged into one Mesh per material.</summary>
    public sealed class MeshAccum
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public int Count => V.Count;

        public int Add(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n.normalized); UV.Add(uv); return V.Count - 1; }
        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        public Mesh ToMesh(string name, bool dynamic = false)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
            if (dynamic) m.MarkDynamic();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>A tube around a polyline-free straight segment a -> b (radius r, sides), uv constant (atlas region).</summary>
        public void Cylinder(Vector3 a, Vector3 b, float r, int sides, Vector2 uv, bool caps = true)
        {
            Vector3 ax = (b - a).normalized, u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ax, u);
            int i0 = Count;
            for (int s = 0; s <= sides; s++)
            {
                float t = s * Mathf.PI * 2 / sides;
                Vector3 n = u * Mathf.Cos(t) + w * Mathf.Sin(t);
                Add(a + n * r, n, uv); Add(b + n * r, n, uv);
            }
            for (int s = 0; s < sides; s++) Quad(i0 + 2 * s, i0 + 2 * s + 2, i0 + 2 * s + 3, i0 + 2 * s + 1);
            if (!caps) return;
            int ca = Add(a, -ax, uv), cb = Add(b, ax, uv);
            int r0 = Count;
            for (int s = 0; s <= sides; s++) { float t = s * Mathf.PI * 2 / sides; Vector3 n = u * Mathf.Cos(t) + w * Mathf.Sin(t); Add(a + n * r, -ax, uv); Add(b + n * r, ax, uv); }
            for (int s = 0; s < sides; s++) { Tri(ca, r0 + 2 * s + 2, r0 + 2 * s); Tri(cb, r0 + 2 * s + 1, r0 + 2 * s + 3); }
        }

        /// <summary>A torus of major radius R about axis (centre c, axis n), tube radius r.</summary>
        public void Torus(Vector3 c, Vector3 n, float R, float r, int seg, int sides, Vector2 uv)
        {
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(n, u);
            int i0 = Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                Vector3 dir = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2 / sides;
                    Vector3 nn = dir * Mathf.Cos(b) + n * Mathf.Sin(b);
                    Add(c + dir * R + nn * r, nn, uv);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < seg; i++) for (int j = 0; j < sides; j++) Quad(i0 + i * row + j, i0 + i * row + j + 1, i0 + (i + 1) * row + j + 1, i0 + (i + 1) * row + j);
        }

        /// <summary>A UV sphere (centre c, radius r).</summary>
        public void Sphere(Vector3 c, float r, int seg, int rings, Vector2 uv)
        {
            int i0 = Count;
            for (int i = 0; i <= rings; i++)
            {
                float ph = Mathf.PI * i / rings;
                for (int j = 0; j <= seg; j++)
                {
                    float th = 2 * Mathf.PI * j / seg;
                    Vector3 nn = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
                    Add(c + nn * r, nn, uv);
                }
            }
            for (int i = 0; i < rings; i++) for (int j = 0; j < seg; j++) Quad(i0 + i * (seg + 1) + j, i0 + i * (seg + 1) + j + 1, i0 + (i + 1) * (seg + 1) + j + 1, i0 + (i + 1) * (seg + 1) + j);
        }

        /// <summary>A box with rounded-off look (bevelled by normals only): centre, axes (half sizes along x, y, z).</summary>
        public void Box(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector2 uv)
        {
            Vector3[] n = { ax.normalized, -ax.normalized, ay.normalized, -ay.normalized, az.normalized, -az.normalized };
            Vector3[] a = { ax, -ax, ay, -ay, az, -az };
            Vector3[] u = { ay, ay, az, az, ax, ax }, w = { az, -az, ax, -ax, ay, -ay };
            for (int f = 0; f < 6; f++)
            {
                Vector3 o = c + a[f];
                int i = Count;
                Add(o - u[f] - w[f], n[f], uv); Add(o + u[f] - w[f], n[f], uv); Add(o + u[f] + w[f], n[f], uv); Add(o - u[f] + w[f], n[f], uv);
                Quad(i, i + 1, i + 2, i + 3);
            }
        }
    }

    /// <summary>The three meshes of the pair and the head grid's layout (for the dents).</summary>
    public sealed class HandDrumsMeshes
    {
        public Mesh Shells, Heads, Hardware;
        /// <summary>Head d's first vertex in Heads (its centre), then rings 1..16 x 64 segments, then the wrap rings.</summary>
        public readonly int[] HeadBase = new int[2];
        public int HeadVertsPerDrum;
        public Vector3[] HeadRest;    // the undented positions
        public Vector3[] HeadRestN;   // and normals
        public int Triangles => (Shells.triangles.Length + Heads.triangles.Length + Hardware.triangles.Length) / 3;
    }

    public static class HandDrumsGeometry
    {
        // hardware atlas regions (HandDrumsTextures.Hardware): chrome, satin black (legs), rubber (feet), nut brass-dark
        public static readonly Vector2 UvChrome = new Vector2(0.125f, 0.5f), UvBlack = new Vector2(0.625f, 0.5f), UvRubber = new Vector2(0.875f, 0.5f), UvSteel = new Vector2(0.375f, 0.5f);

        /// <summary>How finely the pair is built: the seat's set (the dent grid, every lug part) or the display set (static
        /// heads, fewer segments everywhere: platform 11, at most 8 k triangles on display).</summary>
        struct Detail
        {
            public int StaveFacets, StaveRings, InnerSeg, InnerRings, HeadRings, HeadSegs, WrapRings, Torus, Tube, Lug, LugSphere, Rod, Band, Ring, Leg;
            public bool Flange, LugCaps;
        }
        static readonly Detail Seat = new Detail { StaveFacets = 3, StaveRings = 18, InnerSeg = 32, InnerRings = 6, HeadRings = HandDrumsDims.HeadRings, HeadSegs = HandDrumsDims.HeadSegs,
            WrapRings = 4, Torus = 60, Tube = 8, Lug = 12, LugSphere = 12, Rod = 8, Band = 48, Ring = 48, Leg = 10, Flange = true, LugCaps = true };
        static readonly Detail Shown = new Detail { StaveFacets = 1, StaveRings = 8, InnerSeg = 16, InnerRings = 2, HeadRings = 2, HeadSegs = 32,
            WrapRings = 2, Torus = 32, Tube = 4, Lug = 6, LugSphere = 6, Rod = 4, Band = 24, Ring = 24, Leg = 6, Flange = false, LugCaps = true };
        static Detail _q = Seat;

        /// <summary>The display set (IStowable): the same three meshes and materials, static heads, about a third of the
        /// triangles. No dent grid (HeadBase / HeadRest stay empty).</summary>
        public static HandDrumsMeshes BuildDisplay()
        {
            _q = Shown;
            try
            {
                var m = new HandDrumsMeshes();
                var shells = new MeshAccum(); var heads = new MeshAccum(); var hw = new MeshAccum();
                for (int d = 0; d < 2; d++) { Shell(shells, d); Head(heads, d); Hardware(hw, d); }
                m.Shells = shells.ToMesh("HandDrums shells (display)");
                m.Heads = heads.ToMesh("HandDrums heads (display)");
                m.Hardware = hw.ToMesh("HandDrums hardware (display)");
                return m;
            }
            finally { _q = Seat; }
        }

        public static HandDrumsMeshes Build()
        {
            _q = Seat;
            var m = new HandDrumsMeshes();
            var shells = new MeshAccum(); var heads = new MeshAccum(); var hw = new MeshAccum();
            for (int d = 0; d < 2; d++)
            {
                Shell(shells, d);
                m.HeadBase[d] = heads.Count;
                Head(heads, d);
                Hardware(hw, d);
            }
            m.HeadVertsPerDrum = m.HeadBase[1] - m.HeadBase[0];
            m.Shells = shells.ToMesh("HandDrums shells");
            m.Heads = heads.ToMesh("HandDrums heads", dynamic: true);
            { Bounds bb = m.Heads.bounds; bb.Expand(0.02f); m.Heads.bounds = bb; }   // the dents stay inside: no bounds update per frame
            m.Hardware = hw.ToMesh("HandDrums hardware");
            m.HeadRest = heads.V.ToArray();
            m.HeadRestN = heads.N.ToArray();
            return m;
        }

        /// <summary>The shell's frame: axis (down -> up = n), and the in-plane directions (x and the perpendicular).</summary>
        static void Frame(out Vector3 n, out Vector3 ex, out Vector3 ez)
        {
            n = HandDrumsDims.Normal;
            ex = Vector3.right;
            ez = Vector3.Cross(ex, n);   // in the head plane, away from the player... (n x ... orientation for the lathe)
        }

        static void Shell(MeshAccum a, int d)
        {
            Frame(out Vector3 n, out Vector3 ex, out Vector3 ez);
            int S = HandDrumsDims.Staves[d], facets = _q.StaveFacets, rings = _q.StaveRings;
            float joint = 0.0006f;
            // per-stave variation: u mirrored on alternate staves, v reversed on every third (the grain map does not repeat
            // along a stave: one stave spans v 0.03 .. 0.97 once)
            for (int s = 0; s < S; s++)
            {
                float flip = (s & 1) == 0 ? 1f : -1f;
                bool vRev = s % 3 == 1 ^ d == 1;
                float a0 = (s + 0.5f) * Mathf.PI * 2 / S;   // a joint never sits at theta 0 (the near edge)
                int i0 = a.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float t = r / (float)rings;
                    Vector3 c = HandDrumsDims.Axis(d, t);
                    float R = HandDrumsDims.ShellRadius(d, t);
                    // slope of the profile for the normals (dR/dt over the axis length)
                    float dt = 0.002f, dR = (HandDrumsDims.ShellRadius(d, Mathf.Min(1, t + dt)) - HandDrumsDims.ShellRadius(d, Mathf.Max(0, t - dt))) / ((Mathf.Min(1, t + dt) - Mathf.Max(0, t - dt)) * HandDrumsDims.ShellLength);
                    for (int f = 0; f <= facets; f++)
                    {
                        float x = f / (float)facets, ang = a0 + x * Mathf.PI * 2 / S;
                        Vector3 dir = ex * Mathf.Cos(ang) + ez * Mathf.Sin(ang);
                        float edge = Mathf.Abs(x - 0.5f) * 2;   // 1 at the joints
                        float rr = R - joint * Mathf.Pow(edge, 8);
                        Vector3 nrm = (dir - n * dR).normalized;
                        float u = flip > 0 ? x : 1 - x;
                        float vv = 0.03f + 0.94f * (vRev ? 1 - t : t);
                        a.Add(c + dir * rr, nrm, new Vector2(u, vv));
                    }
                }
                int row = facets + 1;
                for (int r = 0; r < rings; r++) for (int f = 0; f < facets; f++) a.Quad(i0 + r * row + f, i0 + (r + 1) * row + f, i0 + (r + 1) * row + f + 1, i0 + r * row + f + 1);
            }
            // the inner wall (seen only through the bottom opening) and the bottom lip
            int seg = _q.InnerSeg, ii = a.Count, ir = _q.InnerRings;
            for (int r = 0; r <= ir; r++)
            {
                float t = r / (float)ir * 0.5f;
                Vector3 c = HandDrumsDims.Axis(d, t);
                float R = HandDrumsDims.ShellRadius(d, t) - HandDrumsDims.Wall;
                for (int k = 0; k <= seg; k++)
                {
                    float ang = k * Mathf.PI * 2 / seg;
                    Vector3 dir = ex * Mathf.Cos(ang) + ez * Mathf.Sin(ang);
                    a.Add(c + dir * R, -dir, new Vector2(k / (float)seg * 6, t * 1.6f));
                }
            }
            for (int r = 0; r < ir; r++) for (int k = 0; k < seg; k++) a.Quad(ii + r * (seg + 1) + k, ii + r * (seg + 1) + k + 1, ii + (r + 1) * (seg + 1) + k + 1, ii + (r + 1) * (seg + 1) + k);
            int il = a.Count;
            {
                Vector3 c = HandDrumsDims.Axis(d, 0);
                float Ro = HandDrumsDims.ShellRadius(d, 0), Ri = Ro - HandDrumsDims.Wall;
                for (int k = 0; k <= seg; k++)
                {
                    float ang = k * Mathf.PI * 2 / seg;
                    Vector3 dir = ex * Mathf.Cos(ang) + ez * Mathf.Sin(ang);
                    a.Add(c + dir * Ro, -n, new Vector2(k / (float)seg * 6, 0)); a.Add(c + dir * Ri, -n, new Vector2(k / (float)seg * 6, 0.02f));
                }
                for (int k = 0; k < seg; k++) a.Quad(il + 2 * k, il + 2 * k + 1, il + 2 * k + 3, il + 2 * k + 2);
            }
        }

        /// <summary>The head: centre + 16 rings x 64 segments on the vibrating disc (the dent grid), then the wrap rings.
        /// UV: a disc in the rawhide texture, uv radius 0.5 at the end of the wrap; the tumba's mirrored (another hide).</summary>
        static void Head(MeshAccum a, int d)
        {
            Quaternion q = HandDrumsDims.HeadRotation;
            Vector3 n = q * Vector3.up, c = HandDrumsDims.HeadCentre[d];
            float A = HandDrumsDims.A[d];
            int R = _q.HeadRings, S = _q.HeadSegs, W = _q.WrapRings;
            int wStep = HandDrumsDims.Wrap.Length / W;   // the display set keeps every second wrap ring
            float uvR = A + HandDrumsDims.Wrap[HandDrumsDims.Wrap.Length - 1].x + 0.004f;
            float mirror = d == 0 ? 1 : -1;
            Vector2 Uv(float lx, float lz) => new Vector2(0.5f + mirror * lx / (2 * uvR), 0.5f + lz / (2 * uvR));
            int centre = a.Add(c, n, Uv(0, 0));
            for (int r = 1; r <= R; r++)
                for (int s = 0; s < S; s++)
                {
                    // segment s at theta = 2 pi s / 64 of the stroke logic's frame (theta 0 toward the player, e1 = -x)
                    float th = 2 * Mathf.PI * s / S, rho = r / (float)R;
                    float lx = -Mathf.Sin(th) * rho * A, lz = -Mathf.Cos(th) * rho * A;
                    a.Add(c + q * new Vector3(lx, 0, lz), n, Uv(lx, lz));
                }
            for (int w = 0; w < W; w++)
                for (int s = 0; s < S; s++)
                {
                    Vector2 wr = HandDrumsDims.Wrap[(w + 1) * wStep - 1];
                    float th = 2 * Mathf.PI * s / S, rr = A + wr.x, h = wr.y;
                    float lx = -Mathf.Sin(th) * rr, lz = -Mathf.Cos(th) * rr;
                    // the normal turns from up to outward over the bearing edge
                    float k = (w + 1f) / W;
                    Vector3 outward = q * new Vector3(lx, 0, lz).normalized;
                    Vector3 nrm = Vector3.Slerp(n, outward, Mathf.Clamp01(k * 1.1f));
                    a.Add(c + q * new Vector3(lx, h, lz), nrm, Uv(lx, lz));
                }
            int ring0 = centre + 1;
            // Unity's front faces wind clockwise seen from the front (here: from above the head)
            for (int s = 0; s < S; s++) a.Tri(centre, ring0 + s, ring0 + (s + 1) % S);
            for (int r = 0; r < R - 1 + W; r++)
                for (int s = 0; s < S; s++)
                {
                    int i = ring0 + r * S + s, j = ring0 + r * S + (s + 1) % S;
                    a.Quad(i, i + S, j + S, j);
                }
        }

        static void Hardware(MeshAccum a, int d)
        {
            Quaternion q = HandDrumsDims.HeadRotation;
            Vector3 n = q * Vector3.up, c = HandDrumsDims.HeadCentre[d];
            Frame(out _, out Vector3 ex, out Vector3 ez);
            // crown hoop: a rounded rim just above the head plane, around the wrap
            float hoopR = HandDrumsDims.HoopR[d];
            a.Torus(c + n * (HandDrumsDims.HoopAbove - 0.004f), n, hoopR, HandDrumsDims.HoopTube, _q.Torus, _q.Tube, UvChrome);
            // a lower flange of the hoop that holds the hide down (a thin band below the torus)
            if (_q.Flange) a.Torus(c + n * (HandDrumsDims.HoopAbove - 0.016f), n, hoopR - 0.0015f, 0.0035f, _q.Torus, 6, UvChrome);
            // lugs: side plates on the shell, tension hooks from the hoop down to them, a nut on each
            float tLug = 1 - (HandDrumsDims.LugBelowRim - HandDrumsDims.TopBelowHead) / HandDrumsDims.ShellLength;
            for (int k = 0; k < HandDrumsDims.Lugs; k++)
            {
                float ang = (k + 0.5f) * Mathf.PI * 2 / HandDrumsDims.Lugs + (d == 0 ? 0.05f : -0.08f);
                Vector3 dir = ex * Mathf.Cos(ang) + ez * Mathf.Sin(ang), side = Vector3.Cross(n, dir);
                Vector3 axisP = HandDrumsDims.Axis(d, tLug);
                float R = HandDrumsDims.ShellRadius(d, tLug);
                // the lug: a chrome capsule along the shell (two screws hold it), the tension rod from its top to a hook over
                // the hoop, a hex nut on the rod at the lug
                float lr = 0.0075f;
                Vector3 body = axisP + dir * (R + lr * 0.85f);
                Vector3 lo = body - n * (HandDrumsDims.LugLen * 0.5f - lr), hi = body + n * (HandDrumsDims.LugLen * 0.5f - lr);
                a.Cylinder(lo, hi, lr, _q.Lug, UvChrome, caps: false);
                a.Sphere(lo, lr, _q.LugSphere, _q.LugSphere / 2, UvChrome);
                a.Sphere(hi, lr, _q.LugSphere, _q.LugSphere / 2, UvChrome);
                Vector3 top = hi + n * (lr * 0.6f) + dir * 0.004f;
                Vector3 hook = c + n * (HandDrumsDims.HoopAbove + 0.001f) + dir * (hoopR + 0.0045f);
                a.Cylinder(top, hook, 0.0024f, _q.Rod, UvSteel);
                a.Cylinder(hook, hook - dir * 0.009f + n * 0.0015f, 0.0024f, _q.Rod, UvSteel);
                a.Cylinder(top - n * 0.001f, top + n * 0.009f, 0.0052f, 6, UvChrome);
            }
            // the chrome bottom band
            {
                float t = 0.012f;
                a.Torus(HandDrumsDims.Axis(d, t), n, HandDrumsDims.ShellRadius(d, t) + 0.0015f, 0.0035f, _q.Band, Mathf.Min(6, _q.Tube), UvChrome);
            }
            // the basket stand: a ring around the shell near its bottom, 3 legs to the floor (two at the front, one behind),
            // rubber feet; the ring is square to the shell's axis, so the front legs come out shorter (the tilt)
            {
                float t = HandDrumsDims.StandRingAt;
                Vector3 rc = HandDrumsDims.Axis(d, t);
                float rr = HandDrumsDims.ShellRadius(d, t) + 0.010f;
                a.Cylinder(rc - n * 0.010f, rc + n * 0.010f, rr + 0.0035f, _q.Ring, UvBlack, caps: false);
                a.Torus(rc + n * 0.010f, n, rr + 0.0015f, 0.002f, _q.Ring, 4, UvBlack);
                a.Torus(rc - n * 0.010f, n, rr + 0.0015f, 0.002f, _q.Ring, 4, UvBlack);
                Vector3 floorC = new Vector3(rc.x, 0, rc.z - 0.03f);   // toward the player: the tilted drum's weight sits well inside the feet
                float[] legAng = { Mathf.PI * 0.5f, Mathf.PI * 1.17f, Mathf.PI * 1.83f };   // in the floor plane: behind, front-left, front-right
                for (int k = 0; k < 3; k++)
                {
                    float la = legAng[k];
                    Vector3 fdir = new Vector3(Mathf.Cos(la), 0, Mathf.Sin(la));
                    Vector3 ringDir = (ex * Mathf.Cos(la) + ez * Mathf.Sin(la)).normalized;
                    Vector3 topP = rc + ringDir * rr;
                    Vector3 foot = floorC + fdir * HandDrumsDims.StandFootR + Vector3.up * 0.012f;
                    a.Cylinder(topP + n * 0.008f, foot, 0.0075f, _q.Leg, UvBlack);
                    a.Cylinder(foot + Vector3.up * 0.004f, new Vector3(foot.x, 0.0f, foot.z), 0.011f, _q.Leg, UvRubber);
                }
            }
        }
    }
}
