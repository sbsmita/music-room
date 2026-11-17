// Per-hand helpers of the grab code (Docs/05-instruments.md section 8), as plain classes so EditMode tests can drive
// them sample by sample: the dwell (smoothed pinch point, where it settled, and its speed over the last SPEED_WINDOW,
// all on the hand's own pinch clock) and the unwrapped wrist heading.
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>The dwell's record of a pinch point. Hand tracking jitters by a centimetre or so from frame to frame, so
    /// stillness is measured on the point smoothed over GRAB_SMOOTH (it may wander less than GRAB_STILL from where it
    /// settled, the mean of the first 80 ms) and the speed on that smoothed point over SPEED_WINDOW.</summary>
    public sealed class DwellTracker
    {
        public const int Samples = GrabConstants.DwellSamples;
        /// <summary>The settling window: while PinchFor is at most this, `from` is the running mean of the raw samples.</summary>
        public const float SettleSeconds = 0.08f;
        /// <summary>Less history than this reads as speed 0.</summary>
        public const float MinSpan = 0.02f;

        Vector3 _ema, _from;
        int _n, _head;
        float _t;
        bool _started;
        readonly float[] _histT = new float[Samples];
        readonly Vector3[] _histP = new Vector3[Samples];

        public bool Started => _started;
        public Vector3 Smoothed => _ema;
        public Vector3 SettledAt => _from;

        /// <summary>dwellStart(h, at): ema = from = at; the history cleared; t = the hand's pinch clock.</summary>
        public void Start(Vector3 at, float pinchFor)
        {
            _started = true;
            _ema = _from = at;
            _n = 1;
            _t = pinchFor;
            for (int i = 0; i < Samples; i++) _histT[i] = -1f;
            _head = 0;
            _histT[0] = _t;
            _histP[0] = at;
        }

        /// <summary>dwellSample(h, at): adds this frame's point when the pinch clock advanced; returns how far the smoothed
        /// point is from where it settled (+inf before Start).</summary>
        public float Sample(Vector3 at, float pinchFor)
        {
            if (!_started) return float.PositiveInfinity;
            float t = pinchFor;
            float dt = Mathf.Max(0f, t - _t);
            if (dt > 0f)
            {
                _ema = Vector3.Lerp(_ema, at, 1f - Mathf.Exp(-dt / GrabConstants.GrabSmooth));
                _t = t;
                if (t <= SettleSeconds)
                {
                    _n++;
                    _from = _from * ((_n - 1f) / _n) + at * (1f / _n);
                }
                _head = (_head + 1) % Samples;
                _histT[_head] = t;
                _histP[_head] = _ema;
            }
            return Vector3.Distance(_ema, _from);
        }

        /// <summary>dwellSpeed(h): the smoothed point's speed, walking back from the newest entry to the first one at least
        /// SPEED_WINDOW older (or the oldest); 0 with under 20 ms of history (+inf before Start).</summary>
        public float Speed()
        {
            if (!_started) return float.PositiveInfinity;
            float nowT = _histT[_head];
            Vector3 nowP = _histP[_head];
            int refI = -1;
            for (int k = 1; k < Samples; k++)
            {
                int e = (_head - k + Samples) % Samples;
                if (_histT[e] < 0f || _histT[e] > nowT) break;
                refI = e;
                if (nowT - _histT[e] >= GrabConstants.SpeedWindow) break;
            }
            if (refI < 0 || nowT - _histT[refI] < MinSpan) return 0f;
            return Vector3.Distance(_histP[refI], nowP) / (nowT - _histT[refI]);
        }
    }

    /// <summary>The wrist's heading for turning a held rig, unwrapped frame by frame so it never
    /// jumps at a half turn. Measured from whichever of the wrist's long axis (toward the fingers) and cross axis lies
    /// flatter, so a hand pinching palm down, on its side or fingers up all read steadily; a change of axis carries the
    /// angle on without a jump. In Unity the joint's rot * forward points to the fingers and rot * right is the cross axis;
    /// the heading is atan2(x, z) (Unity yaw), so turns follow the wrist in Unity's own sense.</summary>
    public sealed class WristYawTracker
    {
        /// <summary>The other axis must be this much flatter before the reading switches to it.</summary>
        public const float AxisHysteresis = 0.15f;
        /// <summary>An axis whose floor projection is shorter than this (pointing up or down) gives no heading.</summary>
        public const float MinFlat = 0.25f;

        int _axis;   // 0 none, 1 fingers, 2 cross
        float _raw;
        public float Accumulated { get; private set; }

        public float Read(HandFrame f, bool reset = false)
        {
            if (reset) _axis = 0;
            if (f == null || !f.isTracked)
            {
                _axis = 0;
                return Accumulated;
            }
            Quaternion q = f.rotation[(int)HandJoint.Wrist];
            return Read(q * Vector3.forward, q * Vector3.right);
        }

        /// <summary>The same reading from the two wrist axes in world space (tests).</summary>
        public float Read(Vector3 fingers, Vector3 cross)
        {
            float la = Mathf.Sqrt(fingers.x * fingers.x + fingers.z * fingers.z), lb = Mathf.Sqrt(cross.x * cross.x + cross.z * cross.z);
            int axis = _axis != 0 ? _axis : (la >= lb ? 1 : 2);
            if (axis == 1 && lb > la + AxisHysteresis) axis = 2;
            else if (axis == 2 && la > lb + AxisHysteresis) axis = 1;
            Vector3 v = axis == 1 ? fingers : cross;
            float len = axis == 1 ? la : lb;
            if (len < MinFlat) return Accumulated;
            float yaw = Mathf.Atan2(v.x, v.z);
            if (_axis == axis) Accumulated += LayoutMath.Wrap(yaw - _raw);
            _axis = axis;
            _raw = yaw;
            return Accumulated;
        }
    }

    /// <summary>The One Euro filter (Casiez et al. 2012) of one value: a low-pass whose cutoff rises with the value's
    /// speed (cutoff = minCutoff + beta |dx/dt|), so a still hand's jitter is calmed while a moving one is followed with
    /// little lag. Allocation-free; dt &lt;= 0 returns the last output.</summary>
    public struct OneEuro
    {
        float _x, _dx;
        bool _init;

        public bool Initialised => _init;
        public float Value => _x;

        public void Reset(float x)
        {
            _x = x;
            _dx = 0f;
            _init = true;
        }

        /// <summary>The smoothing factor of a first-order low-pass at <paramref name="cutoff"/> Hz over dt.</summary>
        public static float Alpha(float dt, float cutoff)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-3f));
            return 1f / (1f + tau / dt);
        }

        public float Step(float x, float dt, float minCutoff, float beta, float dCutoff)
        {
            if (!_init) { Reset(x); return x; }
            if (dt <= 0f) return _x;
            float dx = (x - _x) / dt;
            _dx += Alpha(dt, dCutoff) * (dx - _dx);
            float cutoff = minCutoff + beta * Mathf.Abs(_dx);
            _x += Alpha(dt, cutoff) * (x - _x);
            return _x;
        }
    }

    /// <summary>The "Move instruments" drag's light smoothing (MoveConstants.Smooth* / Yaw*): the pinch point through a One Euro
    /// filter whose cutoff follows the point's speed (one speed for the three axes, so a diagonal move is not bent), and the
    /// unwrapped wrist turn through its own. Reset at the grab start on the current values, so the first frame of a drag
    /// returns exactly the hand's point (no jump).</summary>
    public sealed class DragFilter
    {
        Vector3 _p, _v;
        bool _init;
        OneEuro _yaw;

        public Vector3 Point => _p;
        public float Yaw => _yaw.Value;

        public void Reset(Vector3 p, float yaw)
        {
            _p = p;
            _v = Vector3.zero;
            _init = true;
            _yaw.Reset(yaw);
        }

        public Vector3 StepPoint(Vector3 p, float dt)
        {
            if (!_init) { Reset(p, 0f); return p; }
            if (dt <= 0f) return _p;
            Vector3 v = (p - _p) / dt;
            _v += OneEuro.Alpha(dt, MoveConstants.SmoothDerivCutoff) * (v - _v);
            float cutoff = MoveConstants.SmoothMinCutoff + MoveConstants.SmoothBeta * _v.magnitude;
            _p += OneEuro.Alpha(dt, cutoff) * (p - _p);
            return _p;
        }

        public float StepYaw(float yaw, float dt) =>
            _yaw.Step(yaw, dt, MoveConstants.YawMinCutoff, MoveConstants.YawBeta, MoveConstants.SmoothDerivCutoff);

        /// <summary>A soft dead band: 0 within +-band, then the excess (continuous, so leaving the band never jumps).</summary>
        public static float SoftDeadband(float x, float band) =>
            x > band ? x - band : x < -band ? x + band : 0f;
    }

    /// <summary>One hand as the page-turn sweep sees it this frame (filled by the module from PointerInput).</summary>
    public struct SweepHand
    {
        public bool Connected, Pinching, Holding;
        public double LastPlayMs;
        /// <summary>The palm could be measured (PointerInput.TryPalm).</summary>
        public bool PalmOk;
        public Vector3 PalmCentre, PalmNormal;
        /// <summary>PointerInput.GraspAmount: mean wrist distance of the middle/ring/little tips (NaN untracked).</summary>
        public float Open;
    }

    /// <summary>The page turn (Docs/08-ux-lessons-menu-guide.md 5.2): a fast sideways sweep of an open hand with the palm roughly vertical,
    /// measured on the palm centre in the head's floor frame. It must travel SWEEP across within WINDOW, at SPEED or more
    /// over the shortest run of samples that covers it, and mostly across (RATIO times its up/down and in/out travel).
    /// Left = +1 (next), right = -1. After a turn the hand re-arms below REARM_SPEED, never within COOLDOWN.</summary>
    public sealed class PageTurnGesture
    {
        public const float SweepDistance = 0.15f, SweepSpeed = 1.2f, WindowSeconds = 0.3f, Ratio = 1.5f;
        public const float PalmVertical = 0.6f, OpenHand = 0.15f, CooldownSeconds = 0.6f, RearmSpeed = 0.5f;
        public const double PlayGuardMs = 400;
        /// <summary>A jump of the palm larger than this (snap turn, re-placement, tracking back after a loss) is not a sweep.</summary>
        public const float JumpReset = 0.4f;
        public const int Samples = 16;

        struct Sample { public double T; public float X, Y, Z; }

        sealed class Track
        {
            public readonly Sample[] Ring = new Sample[Samples];
            public int N, Head;
            public bool Armed = true;
        }

        readonly Track[] _track = { new Track(), new Track() };
        public bool Enabled = true;
        public float Cooldown;
        public int LastDir, Count;

        /// <summary>Every hand's history forgotten (after a re-arrangement).</summary>
        public void ResetMotion()
        {
            for (int i = 0; i < _track.Length; i++) _track[i].N = 0;
        }

        public bool Armed(int hand) => _track[hand].Armed;
        public int SampleCount(int hand) => _track[hand].N;

        /// <summary>One sample (dt, grabbing, now): returns +1 / -1 on the frame a turn fires, else 0.
        /// <paramref name="headForward"/> is the head's forward (flattened here).</summary>
        public int Update(float dt, bool grabbing, double nowMs, Vector3 head, Vector3 headForward, SweepHand[] hands)
        {
            if (Cooldown > 0f) Cooldown -= dt;
            if (!Enabled || hands == null) return 0;
            Vector3 fwd = headForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;   // source frame: fwd x up (mirrored)
            int fired = 0;
            for (int i = 0; i < hands.Length && i < _track.Length; i++)
            {
                SweepHand h = hands[i];
                Track s = _track[i];
                if (!h.Connected) { s.N = 0; continue; }
                bool ok = !h.Pinching && !h.Holding && !grabbing && nowMs - h.LastPlayMs >= PlayGuardMs && h.PalmOk;
                if (!ok) { s.N = 0; continue; }
                if (float.IsNaN(h.Open) || h.Open < OpenHand || Mathf.Abs(Vector3.Dot(h.PalmNormal, Vector3.up)) > PalmVertical) { s.N = 0; continue; }
                Vector3 pc = h.PalmCentre - head;
                float x = Vector3.Dot(pc, right), y = pc.y, z = Vector3.Dot(pc, fwd);
                if (s.N > 0)
                {
                    Sample last = s.Ring[s.Head];
                    float jx = x - last.X, jy = y - last.Y, jz = z - last.Z;
                    if (Mathf.Sqrt(jx * jx + jy * jy + jz * jz) > JumpReset) s.N = 0;
                }
                s.Head = (s.Head + 1) % Samples;
                s.Ring[s.Head] = new Sample { T = nowMs / 1000.0, X = x, Y = y, Z = z };
                Sample cur = s.Ring[s.Head];
                if (s.N < Samples) s.N++;
                // Re-arming: the hand has slowed (measured over the last two samples).
                if (!s.Armed)
                {
                    if (s.N > 1)
                    {
                        Sample prev = s.Ring[(s.Head - 1 + Samples) % Samples];
                        if (Mathf.Abs(cur.X - prev.X) / Mathf.Max((float)(cur.T - prev.T), 0.004f) < RearmSpeed) s.Armed = true;
                    }
                    continue;
                }
                // Walk back to the nearest sample at least SWEEP away across: the shortest span that covers the travel,
                // so a hand at rest just before the sweep does not dilute its speed.
                float dx = 0f, dy = 0f, dz = 0f;
                double span = 0;
                for (int k = 1; k < s.N; k++)
                {
                    Sample c = s.Ring[(s.Head - k + Samples) % Samples];
                    if (cur.T - c.T > WindowSeconds) break;
                    if (Mathf.Abs(cur.X - c.X) >= SweepDistance)
                    {
                        dx = cur.X - c.X; dy = cur.Y - c.Y; dz = cur.Z - c.Z;
                        span = cur.T - c.T;
                        break;
                    }
                }
                if (span == 0) continue;
                float ax = Mathf.Abs(dx);
                float speed = (float)(ax / span);
                if (speed < SweepSpeed || ax < Ratio * Mathf.Abs(dy) || ax < Ratio * Mathf.Abs(dz)) continue;
                if (Cooldown > 0f || fired != 0) continue;
                fired = dx < 0f ? 1 : -1;   // a sweep to the left turns the page forward (next)
                s.Armed = false;
                s.N = 0;
                Cooldown = CooldownSeconds;
                LastDir = fired;
                Count++;
            }
            return fired;
        }
    }
}
