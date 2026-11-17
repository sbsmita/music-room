// The pair as GameObjects (three renderers: shells, heads, hardware = 3 draws at the seat, handdrums.md 4.4) and the
// head's physical response (4.5): the dent under each patch (depth from the engine's own peak contact force table, a
// Gaussian over the patch's points, a critically damped recovery, tau 25 ms), a 1.5 mm dent under a resting hand until
// the lift, written into the head grid only while anything is above 0.05 mm, normals from the displaced grid. Reference:
// the Node visual model (vectors in Tests/EditMode/HandDrums/Reference~/visual-vectors.json). Visual only: nothing here changes a sound.
using System;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    /// <summary>The Node visual model: the dent of one patch.</summary>
    public static class HandDrumsDent
    {
        public const double Max = 0.006, F = 400, Extra = 0.015, Tau = 0.025, RestDent = 0.0015, Eps = 0.00005;
        /// <summary>Peak contact force (N) per part at u .2 / .5 / .8 / 1 (the engine's contact solve, verify.json contact).</summary>
        static readonly double[][] FMax =
        {
            new[] { 39.8, 88.7, 167.5, 240.7 }, new[] { 21.7, 50.2, 85.2, 112.5 }, new[] { 23.3, 66.2, 120.5, 164.1 },
            new[] { 14, 31.7, 52.1, 67.4 }, new[] { 49.7, 136.4, 243.6, 325.2 }, new[] { 7.4, 17.6, 29.5, 38 },
        };
        static readonly double[] Us = { 0.2, 0.5, 0.8, 1 };
        public static readonly double[] PartB = { 0.035, 0.022, 0.012, 0.007, 0.020, 0.012 };

        public static double FmaxOf(HandPart part, double u)
        {
            double[] t = FMax[(int)part];
            if (u <= Us[0]) return t[0] * Math.Max(0, u) / Us[0];
            for (int i = 1; i < 4; i++) if (u <= Us[i]) { double s = (u - Us[i - 1]) / (Us[i] - Us[i - 1]); return t[i - 1] + (t[i] - t[i - 1]) * s; }
            return t[3];
        }

        public static double Depth(HandPart part, double u) => Max * Math.Min(1, Math.Pow(FmaxOf(part, u) / F, 0.6));

        /// <summary>The displacement (m, into the head) at (rho, theta) under a patch of np points (rho, theta pairs).</summary>
        public static double At(double rho, double theta, double[] pts, int np, double depth, double b, double a)
        {
            double s = b + Extra, x = rho * a * Math.Cos(theta), y = rho * a * Math.Sin(theta), sum = 0;
            for (int i = 0; i < np; i++)
            {
                double dx = x - pts[2 * i] * a * Math.Cos(pts[2 * i + 1]), dy = y - pts[2 * i] * a * Math.Sin(pts[2 * i + 1]);
                sum += Math.Exp(-(dx * dx + dy * dy) / (2 * s * s));
            }
            return Math.Min(depth, depth * sum / Math.Sqrt(np)) * (1 - rho * rho);
        }

        public static double Spring(double depth, double t) => t < 0 ? 0 : depth * (1 + t / Tau) * Math.Exp(-t / Tau);
    }

    public sealed class HandDrumsModel : IDisposable
    {
        public GameObject Root { get; }
        public MeshRenderer Shells { get; }
        public MeshRenderer Heads { get; }
        public MeshRenderer Hardware { get; }
        public HandDrumsMeshes Meshes { get; }
        /// <summary>The display set (static heads, about a third of the triangles), built on the first SetDisplay(true).</summary>
        public HandDrumsMeshes DisplayMeshes { get; private set; }
        /// <summary>Showing the display set (IStowable): no dents, Update does nothing.</summary>
        public bool Display { get; private set; }
        public HandDrumsMaterials Materials { get; }
        public readonly Renderer[] Handles;      // the bodies (Rig.Handles: shells, hardware)
        public readonly Renderer[] PlayedParts;  // the heads

        // ---- dents
        const int MaxDents = 12, MaxPts = 4;
        struct Dent { public int Drum, Np; public double Depth, Sigma, T0; public float X0, Y0, X1, Y1, X2, Y2, X3, Y3; }
        readonly Dent[] _dents = new Dent[MaxDents];
        int _count;
        readonly Dent[] _rest = new Dent[4];     // [drum * 2 + hand]
        readonly bool[] _restOn = new bool[4];
        readonly Vector3[] _v, _n;
        readonly float[] _disp;                  // per head-grid vertex, metres
        readonly float[] _ringCos = new float[HandDrumsDims.HeadSegs], _ringSin = new float[HandDrumsDims.HeadSegs];
        bool _dirty;
        readonly bool[] _headWasActive = new bool[2];
        readonly double[] _now = new double[MaxDents];
        readonly double[] _pts = new double[2 * MaxPts];

        /// <summary>textures false (a headless boot): the materials without their generated maps.</summary>
        public HandDrumsModel(Transform parent, MaterialLibrary lib, bool textures = true)
        {
            Meshes = HandDrumsGeometry.Build();
            Materials = HandDrumsMaterials.Create(lib, textures);
            Root = new GameObject("HandDrums");
            if (parent != null) Root.transform.SetParent(parent, false);
            Shells = Part("Shells", Meshes.Shells, Materials.Shell);
            Heads = Part("Heads", Meshes.Heads, Materials.Rawhide);
            Hardware = Part("Hardware", Meshes.Hardware, Materials.Hardware);
            Heads.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // the heads sit inside the shells' shadow
            Handles = new Renderer[] { Shells, Hardware };
            PlayedParts = new Renderer[] { Heads };
            _v = (Vector3[])Meshes.HeadRest.Clone();
            _n = (Vector3[])Meshes.HeadRestN.Clone();
            _disp = new float[_v.Length];
            for (int s = 0; s < HandDrumsDims.HeadSegs; s++) { float th = 2 * Mathf.PI * s / HandDrumsDims.HeadSegs; _ringCos[s] = Mathf.Cos(th); _ringSin[s] = Mathf.Sin(th); }
        }

        MeshRenderer Part(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        /// <summary>A patch landed (the Patch command's points, the part, its u) at time now (s, any monotonic clock).</summary>
        public void Strike(int drum, HandPart part, double u, PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count, double now)
        {
            int np = Fill(shape, p0, p1, p2, count);
            int slot = _count < MaxDents ? _count++ : Oldest();
            _dents[slot] = Make(drum, np, HandDrumsDent.Depth(part, u), HandDrumsDent.PartB[(int)part], now);
            _dirty = true;
        }

        /// <summary>A hand rests on drum's head: a 1.5 mm dent under its points until Lift.</summary>
        public void Rest(int drum, int hand, RestPoint p0, RestPoint p1, int count, double now)
        {
            int np = Math.Max(1, Math.Min(2, count));
            _pts[0] = p0.Rho; _pts[1] = p0.Theta; _pts[2] = p1.Rho; _pts[3] = p1.Theta;
            int k = drum * 2 + (hand & 1);
            _rest[k] = Make(drum, np, HandDrumsDent.RestDent, HandDrumsDent.PartB[(int)p0.Part], now);
            _restOn[k] = true;
            _dirty = true;
        }

        public void Lift(int drum, int hand) { int k = drum * 2 + (hand & 1); if (_restOn[k]) { _restOn[k] = false; _dirty = true; } }

        public void LiftAll() { for (int k = 0; k < 4; k++) _restOn[k] = false; _count = 0; _dirty = true; }

        int Fill(PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count)
        {
            if (shape == PatchShape.Line)
            {
                for (int k = 0; k < 4; k++) { _pts[2 * k] = p0.Rho + (p1.Rho - p0.Rho) * (k / 3.0); _pts[2 * k + 1] = p0.Theta + (p1.Theta - p0.Theta) * (k / 3.0); }
                return 4;
            }
            int np = Math.Max(1, Math.Min(3, count));
            _pts[0] = p0.Rho; _pts[1] = p0.Theta; _pts[2] = p1.Rho; _pts[3] = p1.Theta; _pts[4] = p2.Rho; _pts[5] = p2.Theta;
            return np;
        }

        Dent Make(int drum, int np, double depth, double b, double now)
        {
            double a = HandDrumsDims.A[drum];
            var d = new Dent { Drum = drum, Np = np, Depth = depth, Sigma = b + HandDrumsDent.Extra, T0 = now };
            float X(int i) => (float)(_pts[2 * i] * a * Math.Cos(_pts[2 * i + 1]));
            float Y(int i) => (float)(_pts[2 * i] * a * Math.Sin(_pts[2 * i + 1]));
            d.X0 = X(0); d.Y0 = Y(0);
            if (np > 1) { d.X1 = X(1); d.Y1 = Y(1); }
            if (np > 2) { d.X2 = X(2); d.Y2 = Y(2); }
            if (np > 3) { d.X3 = X(3); d.Y3 = Y(3); }
            return d;
        }

        int Oldest() { int b = 0; for (int i = 1; i < MaxDents; i++) if (_dents[i].T0 < _dents[b].T0) b = i; return b; }

        /// <summary>Writes the head grid when anything moved (now: the same clock as the strikes). Returns true when written.</summary>
        /// <summary>Swap the three meshes between the seat's set and the display set (same renderers, same materials). Going
        /// on display flattens every dent.</summary>
        public void SetDisplay(bool display)
        {
            if (display == Display) return;
            if (display && DisplayMeshes == null) DisplayMeshes = HandDrumsGeometry.BuildDisplay();
            Display = display;
            HandDrumsMeshes m = display ? DisplayMeshes : Meshes;
            Shells.GetComponent<MeshFilter>().sharedMesh = m.Shells;
            Heads.GetComponent<MeshFilter>().sharedMesh = m.Heads;
            Hardware.GetComponent<MeshFilter>().sharedMesh = m.Hardware;
            if (display) { LiftAll(); Update(double.MaxValue / 4); }
        }

        /// <summary>Triangles of the set shown now.</summary>
        public int Triangles => (Display ? DisplayMeshes : Meshes).Triangles;

        public bool Update(double now)
        {
            // drop the dents that have recovered
            for (int i = 0; i < _count;)
            {
                if (HandDrumsDent.Spring(_dents[i].Depth, now - _dents[i].T0) < HandDrumsDent.Eps && now - _dents[i].T0 > 0) { _dents[i] = _dents[--_count]; _dirty = true; }
                else i++;
            }
            bool any = _count > 0;
            for (int k = 0; k < 4; k++) any |= _restOn[k];
            if (!any && !_dirty) return false;
            _dirty = any;   // keep writing while something is dented; one more write to flatten
            int R = HandDrumsDims.HeadRings, S = HandDrumsDims.HeadSegs;
            Vector3 n = HandDrumsDims.Normal;
            for (int i = 0; i < _count; i++) _now[i] = HandDrumsDent.Spring(_dents[i].Depth, now - _dents[i].T0);
            for (int d = 0; d < 2; d++)
            {
                // only a head that is dented (or was, for its flattening write) is recomputed
                bool active = _restOn[d * 2] || _restOn[d * 2 + 1];
                for (int i = 0; i < _count && !active; i++) active = _dents[i].Drum == d;
                if (!active && !_headWasActive[d]) continue;
                _headWasActive[d] = active;
                int b0 = Meshes.HeadBase[d];
                float a = HandDrumsDims.A[d];
                // displacement per grid vertex: the sum of the dents (each clamped to its own depth), x (1 - rho^2)
                _disp[b0] = Disp(d, 0, 0, 0, now);
                for (int r = 1; r <= R; r++)
                {
                    float rho = r / (float)R;
                    bool reach = r < R && RingReached(d, rho * a);
                    for (int s = 0; s < S; s++) _disp[b0 + 1 + (r - 1) * S + s] = reach ? Disp(d, rho * a * _ringCos[s], rho * a * _ringSin[s], rho, now) : 0f;
                }
                int last = b0 + 1 + R * S;
                for (int i = b0; i < last; i++) _v[i] = Meshes.HeadRest[i] - n * _disp[i];
                // normals from the displaced grid: central differences along the ring and across the rings
                for (int r = 1; r < R; r++)
                    for (int s = 0; s < S; s++)
                    {
                        int i = b0 + 1 + (r - 1) * S + s;
                        int sp = b0 + 1 + (r - 1) * S + (s + 1) % S, sm = b0 + 1 + (r - 1) * S + (s + S - 1) % S;
                        int rin = r == 1 ? b0 : b0 + 1 + (r - 2) * S + s, rout = b0 + 1 + r * S + s;
                        Vector3 tang = _v[sp] - _v[sm], rad = _v[rout] - _v[rin];
                        Vector3 nn = Vector3.Cross(rad, tang).normalized;
                        if (Vector3.Dot(nn, n) < 0) nn = -nn;
                        _n[i] = nn;
                    }
                _n[b0] = n;
            }
            Mesh m = Meshes.Heads;
            const UnityEngine.Rendering.MeshUpdateFlags keep = UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices;
            m.SetVertices(_v, 0, _v.Length, keep);
            m.SetNormals(_n, 0, _n.Length, keep);
            return true;
        }

        /// <summary>Some dent of this drum reaches a ring of radius rr (m): its points' radii within the Gaussian's cut.</summary>
        bool RingReached(int drum, float rr)
        {
            for (int i = 0; i < _count; i++) if (_dents[i].Drum == drum && Near(ref _dents[i], rr)) return true;
            for (int k = drum * 2; k < drum * 2 + 2; k++) if (_restOn[k] && Near(ref _rest[k], rr)) return true;
            return false;
        }

        static bool Near(ref Dent d, float rr)
        {
            float cut = (float)(4.3 * d.Sigma);
            bool N(float x, float y) => Mathf.Abs(Mathf.Sqrt(x * x + y * y) - rr) < cut;
            return N(d.X0, d.Y0) || (d.Np > 1 && N(d.X1, d.Y1)) || (d.Np > 2 && N(d.X2, d.Y2)) || (d.Np > 3 && N(d.X3, d.Y3));
        }

        float Disp(int drum, float x, float y, float rho, double now)
        {
            double sum = 0;
            for (int i = 0; i < _count; i++) if (_dents[i].Drum == drum) sum += One(ref _dents[i], x, y, _now[i]);
            for (int k = drum * 2; k < drum * 2 + 2; k++) if (_restOn[k]) sum += One(ref _rest[k], x, y, _rest[k].Depth);
            return (float)(sum * (1 - rho * rho));
        }

        static double One(ref Dent d, float x, float y, double depth)
        {
            if (depth <= 0) return 0;
            double s2 = 2 * d.Sigma * d.Sigma, cut = 9 * d.Sigma * d.Sigma, sum = 0;
            sum += G(x - d.X0, y - d.Y0, s2, cut);
            if (d.Np > 1) sum += G(x - d.X1, y - d.Y1, s2, cut);
            if (d.Np > 2) sum += G(x - d.X2, y - d.Y2, s2, cut);
            if (d.Np > 3) sum += G(x - d.X3, y - d.Y3, s2, cut);
            return Math.Min(depth, depth * sum / Math.Sqrt(d.Np));
        }

        static double G(double dx, double dy, double s2, double cut) { double q = dx * dx + dy * dy; return q > 2 * cut ? 0 : Math.Exp(-q / s2); }

        /// <summary>The world pose of a point on drum d's head (rho, theta of the stroke logic's frame).</summary>
        public Vector3 HeadPoint(int d, float rho, float theta, float h = 0) => Root.transform.TransformPoint(HandDrumsDims.HeadPoint(d, rho, theta, h));

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            foreach (var mesh in new[] { Meshes.Shells, Meshes.Heads, Meshes.Hardware }) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (DisplayMeshes != null) foreach (var mesh in new[] { DisplayMeshes.Shells, DisplayMeshes.Heads, DisplayMeshes.Hardware }) if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }
    }
}
