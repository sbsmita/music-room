// E7 (experience.md section 9): the kit moves like a real kit instead of pulsing.
//   CymbalMotion  a struck cymbal tips DOWN where it was struck and rocks slowly on its felt: a damped two-axis oscillator
//                 whose plane of rocking swirls (replaces a 4.5 Hz Lissajous wobble). Reference cymbalStrike /
//                 cymbalStep (the experience reference engine, Node prototype).
//   HeadGive      a drum head dents under the stick for one to three frames, then is flat again (replaces the 1.5 %
//                 scale pulse of the whole drum, FlashScale 0): the batter head is a ringed disc whose vertices are
//                 rewritten only on those frames. Reference headDimple.
//   KickBeater    the decorative kick pedal: the beater hits the batter head on every kick (nod or pad) and swings back,
//                 critically damped; the footboard follows. Two draws (the chrome shaft and footboard, the felt ball).
// Everything runs from DrumKit.Tick (Instruments phase) or DrumKit.Hit; no allocation after the build.
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Drums
{
    /// <summary>A cymbal's rocking parameters (experience.md section 9.1 table).</summary>
    public readonly struct CymbalParams
    {
        public readonly float F0, Zeta, SwirlHz, MaxTiltDeg, Radius;
        public CymbalParams(float f0, float zeta, float swirlHz, float maxTiltDeg, float radius)
        {
            F0 = f0; Zeta = zeta; SwirlHz = swirlHz; MaxTiltDeg = maxTiltDeg; Radius = radius;
        }
    }

    /// <summary>One cymbal's tilt state: Tx, Tz (rad) and their rates Wx, Wz (rad/s). The plate's localRotation is
    /// Euler(Tx deg, 0, Tz deg): +Tx sends local +z down, +Tz lifts +x.</summary>
    public struct CymbalTilt
    {
        public float Tx, Tz, Wx, Wz;
        /// <summary>The plate is off its rest pose (the kit writes its rotation only while this holds).</summary>
        public bool Moving;
        public Quaternion Rotation => Quaternion.Euler(Tx * Mathf.Rad2Deg, 0f, Tz * Mathf.Rad2Deg);
        public float MagnitudeDeg => Mathf.Sqrt(Tx * Tx + Tz * Tz) * Mathf.Rad2Deg;
    }

    public static class CymbalMotion
    {
        // f0 Hz, zeta, swirl Hz, max tilt deg, R m. Gravity alone on a 1 kg 16" crash gives 0.61 Hz; the felt washers stiffen
        // it: design values, verify by eye on the headset. The ride is heavier, so slower and less; the hi-hat's clutch
        // clamps it while closed.
        public static readonly CymbalParams Crash = new CymbalParams(1.6f, 0.115f, 0.35f, 8f, 0.21f);
        public static readonly CymbalParams Ride = new CymbalParams(1.2f, 0.10f, 0.25f, 4f, 0.22f);
        public static readonly CymbalParams HatOpen = new CymbalParams(2.5f, 0.20f, 0.5f, 3f, 0.17f);
        public static readonly CymbalParams HatClosed = new CymbalParams(2.5f, 0.20f, 0.5f, 0f, 0.17f);
        /// <summary>Semi-implicit Euler substeps of at most 4 ms.</summary>
        public const float Substep = 0.004f;
        /// <summary>A hand on the plate (the choke) damps the rocking with this time constant.</summary>
        public const float ChokeTau = 0.03f;
        /// <summary>|theta| never exceeds 1.5 x maxTilt.</summary>
        public const float MaxScale = 1.5f;
        /// <summary>Below these the plate is put back at rest and no longer written.</summary>
        public const float StillRad = 2e-6f, StillRate = 2e-5f;

        public static CymbalParams For(DrumPiece piece) =>
            piece.IsHat ? (piece.Open ? HatOpen : HatClosed) : piece.Id == DrumSpec.RideId ? Ride : Crash;

        /// <summary>A hit at plate-local (x, z) with velocity vel (0..1): k = maxTilt 2 pi f0 vel / R; Wx += k z; Wz -= k x.</summary>
        public static void Strike(ref CymbalTilt s, in CymbalParams p, float x, float z, float vel)
        {
            double w0 = 2.0 * System.Math.PI * p.F0;
            double k = p.MaxTiltDeg * System.Math.PI / 180.0 * w0 * Mathf.Clamp01(vel) / p.Radius;
            s.Wx = (float)(s.Wx + k * z);
            s.Wz = (float)(s.Wz - k * x);
            s.Moving |= k * (Mathf.Abs(x) + Mathf.Abs(z)) > 0.0;
        }

        /// <summary>theta'' = -w0^2 theta - 2 zeta w0 theta' + 2 Omega J(theta'), J(a, b) = (-b, a), in substeps of at most
        /// 4 ms; then the choke and the cap. Settles the state to rest when it is still.</summary>
        public static void Step(ref CymbalTilt s, in CymbalParams p, float dt, bool choking)
        {
            if (dt <= 0f) return;
            double w0 = 2.0 * System.Math.PI * p.F0, om = 2.0 * System.Math.PI * p.SwirlHz, c = 2.0 * p.Zeta * w0;
            int steps = System.Math.Max(1, (int)System.Math.Ceiling(dt / (double)Substep));
            double h = dt / (double)steps;
            double tx = s.Tx, tz = s.Tz, wx = s.Wx, wz = s.Wz;
            for (int i = 0; i < steps; i++)
            {
                double ax = -w0 * w0 * tx - c * wx - 2.0 * om * wz;
                double az = -w0 * w0 * tz - c * wz + 2.0 * om * wx;
                wx += ax * h; wz += az * h;
                tx += wx * h; tz += wz * h;
            }
            if (choking)
            {
                double g = System.Math.Exp(-dt / ChokeTau);
                tx *= g; tz *= g; wx *= g; wz *= g;
            }
            double lim = MaxScale * System.Math.Max(p.MaxTiltDeg, 1e-3) * System.Math.PI / 180.0;
            double m = System.Math.Sqrt(tx * tx + tz * tz);
            if (m > lim) { tx *= lim / m; tz *= lim / m; }
            s.Tx = (float)tx; s.Tz = (float)tz; s.Wx = (float)wx; s.Wz = (float)wz;
            if (System.Math.Abs(tx) < StillRad && System.Math.Abs(tz) < StillRad && System.Math.Abs(wx) < StillRate && System.Math.Abs(wz) < StillRate)
                s = default;
            else s.Moving = true;
        }
    }

    /// <summary>A batter head's give: w(d, t) = 0.004 vel exp(-d^2 / (2 (0.25 R)^2)) exp(-t / 0.018) m into the head at
    /// distance d from the strike point, shown on the hit frame and the two after it, then the head is flat again. The
    /// 4 mm peak is the order of a point-loaded membrane's static deflection, F / (2 pi T) ln(R / a): 300 N forte, 3 kN/m
    /// head tension, R / a = 25 give about 5 mm (Fletcher & Rossing ch. 3).</summary>
    public sealed class HeadGive
    {
        public const float Depth = 0.004f, SigmaOverR = 0.25f, Tau = 0.018f;
        /// <summary>Frames the dent is drawn (the hit frame included).</summary>
        public const int Frames = 3;
        public const int Rings = 6;
        const MeshUpdateFlags Upload = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        readonly Mesh _mesh;
        readonly Vector3[] _rest, _restN, _v, _n;
        readonly float _r, _s2;
        float _cx, _cz, _vel, _t;
        int _frame = -1;

        /// <summary>w(d, t) in metres (reference headDimple).</summary>
        public static float Dimple(float d, float t, float vel, float r)
        {
            float s = SigmaOverR * r;
            return Depth * Mathf.Clamp01(vel) * Mathf.Exp(-(d * d) / (2f * s * s)) * Mathf.Exp(-t / Tau);
        }

        /// <param name="mesh">The head's ringed disc (MeshKit.RingedDisc) in the piece's frame.</param>
        public HeadGive(Mesh mesh, float r)
        {
            _mesh = mesh;
            _r = r;
            float s = SigmaOverR * r;
            _s2 = s * s;
            _rest = mesh.vertices;
            _restN = mesh.normals;
            _v = (Vector3[])_rest.Clone();
            _n = (Vector3[])_restN.Clone();
            mesh.MarkDynamic();
            Bounds b = mesh.bounds;
            b.Expand(new Vector3(0f, 2f * Depth + 0.002f, 0f));   // the dent stays inside the bounds: no bounds pass on upload
            mesh.bounds = b;
        }

        public Mesh Mesh => _mesh;
        public bool Active => _frame >= 0;
        /// <summary>The deepest point drawn now (m, positive into the head; 0 at rest).</summary>
        public float CurrentDepth { get; private set; }
        public int VertexCount => _rest.Length;

        /// <summary>A hit at head-local (x, z): the dent is drawn at once (this frame shows t = 0).</summary>
        public void Strike(float x, float z, float vel)
        {
            _cx = x; _cz = z; _vel = vel; _t = 0f; _frame = 0;
            Write();
        }

        /// <summary>Once per kit Tick, BEFORE this frame's hits (DrumKit.Tick ages every head first): the dent ages; after
        /// Frames frames the head is flat again (written once). A strike in any phase of the frame (the hit test, the nod,
        /// playback, a desktop click) is then drawn at t = 0 once and aged by the next Tick.</summary>
        public void Step(float dt)
        {
            if (_frame < 0) return;
            _t += dt;
            _frame++;
            if (_frame < Frames) { Write(); return; }
            _frame = -1;
            CurrentDepth = 0f;
            _mesh.SetVertices(_rest, 0, _rest.Length, Upload);
            _mesh.SetNormals(_restN, 0, _restN.Length, Upload);
        }

        void Write()
        {
            float a = Depth * Mathf.Clamp01(_vel) * Mathf.Exp(-_t / Tau), inv2s2 = 0.5f / _s2, invS2 = 1f / _s2;
            float deepest = 0f;
            for (int i = 0; i < _rest.Length; i++)
            {
                Vector3 p = _rest[i];
                float dx = p.x - _cx, dz = p.z - _cz;
                float w = a * Mathf.Exp(-(dx * dx + dz * dz) * inv2s2);
                if (w > deepest) deepest = w;
                _v[i] = new Vector3(p.x, p.y - w, p.z);
                // Height h = -w: n ~ (-dh/dx, 1, -dh/dz) = (-w dx / s^2, 1, -w dz / s^2), the dent's walls face its centre.
                var n = new Vector3(-w * dx * invS2, 1f, -w * dz * invS2);
                _n[i] = n.normalized;
            }
            CurrentDepth = deepest;
            _mesh.SetVertices(_v, 0, _v.Length, Upload);
            _mesh.SetNormals(_n, 0, _n.Length, Upload);
        }
    }

    /// <summary>The kick pedal's beater (decorative: hands have no feet, so seeing it strike makes the nod read as a
    /// kick). The axle is kit-local (0.12, 0.08, 0.62) above the pedal, a 0.19 m shaft to a 0.025 m felt ball; its angle
    /// is measured from vertical toward +z: rest -30 deg, contact +16.8 deg (the ball on the batter head at z .70, y .26).
    /// On every kick it is at contact, then returns critically damped, theta(t) = rest + (contact - rest)(1 + t/tau) e^(-t/tau)
    /// with tau 35 ms (90 % back in about 0.13 s); the footboard pitch follows, 14.3 deg - 10 deg (theta - rest)/(contact - rest).</summary>
    public sealed class KickBeater
    {
        public static readonly Vector3 Axle = new Vector3(0.12f, 0.08f, 0.62f);
        public const float ShaftLength = 0.19f, BallRadius = 0.025f, ShaftRadius = 0.0045f;
        public const float RestDeg = -30f, ContactDeg = 16.8f, Tau = 0.035f, FootTravelDeg = 10f;
        /// <summary>After this long the beater is back at rest (10 tau: 5e-4 of its travel left).</summary>
        public const float SettleSeconds = 10f * Tau;
        const MeshUpdateFlags Upload = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        readonly Transform _pivot;
        readonly Mesh _chrome;
        readonly Vector3[] _shaftRest, _shaftRestN, _footRest, _footRestN, _v, _n;
        readonly Vector3 _footPivot, _footArm;
        float _t;

        public static float AngleAt(float t)
        {
            float x = t / Tau;
            return RestDeg + (ContactDeg - RestDeg) * (1f + x) * Mathf.Exp(-x);
        }

        public static float FootPitchDeg(float thetaDeg) =>
            DrumSpec.PedalPitchDeg - FootTravelDeg * (thetaDeg - RestDeg) / (ContactDeg - RestDeg);

        /// <summary>Kit-local centre of the felt ball at angle theta.</summary>
        public static Vector3 BallCentre(float thetaDeg)
        {
            float a = thetaDeg * Mathf.Deg2Rad;
            return Axle + new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * ShaftLength;
        }

        /// <summary>Kit-local point where the ball meets the kick's batter head at contact.</summary>
        public static Vector3 ContactPoint => BallCentre(ContactDeg) + new Vector3(0f, 0f, BallRadius);

        /// <param name="pivot">At Axle (kit-local), parent of the ball; turned about x by the beater angle.</param>
        /// <param name="chrome">One mesh, kit-local: the shaft vertices first (shaftVerts of them, built vertical from
        /// the axle at the origin), then the footboard (built centred on the origin, unpitched).</param>
        public KickBeater(Transform pivot, Mesh chrome, int shaftVerts)
        {
            _pivot = pivot;
            _chrome = chrome;
            Vector3[] v = chrome.vertices, n = chrome.normals;
            int foot = v.Length - shaftVerts;
            _shaftRest = new Vector3[shaftVerts]; _shaftRestN = new Vector3[shaftVerts];
            _footRest = new Vector3[foot]; _footRestN = new Vector3[foot];
            System.Array.Copy(v, 0, _shaftRest, 0, shaftVerts); System.Array.Copy(n, 0, _shaftRestN, 0, shaftVerts);
            System.Array.Copy(v, shaftVerts, _footRest, 0, foot); System.Array.Copy(n, shaftVerts, _footRestN, 0, foot);
            _v = new Vector3[v.Length];
            _n = new Vector3[v.Length];
            // The footboard turns about the middle of its kick-side end (the end on the floor), so it never floats.
            _footArm = new Vector3(0f, 0f, DrumSpec.PedalSize.z * 0.5f);
            _footPivot = DrumSpec.PedalPos + Quaternion.Euler(DrumSpec.PedalPitchDeg, 0f, 0f) * _footArm;
            chrome.MarkDynamic();
            // Fixed bounds over the whole swing (contact, through vertical, rest), so uploads skip the bounds pass.
            Apply(ContactDeg);
            Bounds b = chrome.bounds;
            Apply(0f);
            b.Encapsulate(Recalculated(chrome));
            Apply(RestDeg);
            b.Encapsulate(Recalculated(chrome));
            chrome.bounds = b;
        }

        static Bounds Recalculated(Mesh m)
        {
            m.RecalculateBounds();
            return m.bounds;
        }

        public float AngleDeg { get; private set; } = RestDeg;
        public bool Moving { get; private set; }
        public int Strikes { get; private set; }
        public Transform Pivot => _pivot;
        public Mesh ChromeMesh => _chrome;

        /// <summary>A kick sounded: the ball is on the head this frame.</summary>
        public void Strike()
        {
            _t = 0f;
            Moving = true;
            Strikes++;
            Apply(ContactDeg);
        }

        /// <summary>Once per kit Tick, before this frame's hits (as HeadGive.Step): the swing back ages.</summary>
        public void Step(float dt)
        {
            if (!Moving) return;
            _t += dt;
            if (_t >= SettleSeconds) { Moving = false; Apply(RestDeg); return; }
            Apply(AngleAt(_t));
        }

        void Apply(float thetaDeg)
        {
            AngleDeg = thetaDeg;
            Quaternion shaft = Quaternion.Euler(thetaDeg, 0f, 0f);
            if (_pivot != null) _pivot.localRotation = shaft;
            Quaternion foot = Quaternion.Euler(FootPitchDeg(thetaDeg), 0f, 0f);
            int k = 0;
            for (int i = 0; i < _shaftRest.Length; i++, k++)
            {
                _v[k] = Axle + shaft * _shaftRest[i];
                _n[k] = shaft * _shaftRestN[i];
            }
            for (int i = 0; i < _footRest.Length; i++, k++)
            {
                _v[k] = _footPivot + foot * (_footRest[i] - _footArm);
                _n[k] = foot * _footRestN[i];
            }
            _chrome.SetVertices(_v, 0, _v.Length, Upload);
            _chrome.SetNormals(_n, 0, _n.Length, Upload);
        }
    }
}
