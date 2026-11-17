// The glide (12-new-instruments/platform.md 4.5): instruments travel, they never teleport. A rig lifts 5 cm (0.15 s),
// travels on a horizontal arc about the head at a radius of at least 0.75 m (it first moves straight out from the head if
// it starts closer, and comes straight in at the end), so its origin never enters the head's 0.6 m sphere, then descends
// onto its target (0.15 s). The arc goes round the side of the head that keeps it inside the room (the shorter way unless
// that one runs into a wall), and is held 0.35 m inside the walls (the instruments' display spots stand by them). The yaw turns with the travel; the travel eases in and out on arc length. Duration 0.9 s +
// 0.1 s per metre of path beyond 2 m, at most 1.4 s. Mixed reality (and a rig with no display spot) keeps today's page
// turn instead: a 0.35 s slide across the seat's heading with a scale from 0.85 (Kind Slide).
//
// One RigGlide per rig, reused: Begin precomputes the path into fixed arrays (no allocation), Step moves the rig. While a
// glide runs the rig is not present (Rig.InScene false, set by Layout) and its sounds ring on (its sources follow it).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed class RigGlide
    {
        public const float Lift = 0.05f, LiftSeconds = 0.15f, MinRadius = 0.75f, HeadClear = 0.6f;
        public const float BaseSeconds = 0.9f, PerMetre = 0.1f, FreeMetres = 2f, MaxSeconds = 1.4f;
        /// <summary>When the set changes the outgoing instrument leaves this long before the incoming one starts.</summary>
        public const float OutgoingLead = 0.15f;

        public enum Kind : byte { Glide = 0, SlideIn, SlideOut }

        /// <summary>Where the glide goes: to the seat (an arrival), to its display place (a put-away), or from one seat pose to
        /// another (a member that changes slot, the side table yielding).</summary>
        public enum Goal : byte { Seat = 0, Display, Reseat }

        public Rig Rig;
        public InstrumentKind Instrument;
        public BringReason Reason;
        public Goal Target;
        public Kind Mode;
        public bool Active;
        /// <summary>Seconds before it starts moving (the incoming one waits for the outgoing one's lead).</summary>
        public float Delay;
        public float T, Duration;
        /// <summary>Total horizontal path length (m).</summary>
        public float Length { get; private set; }
        /// <summary>The departure was published (InstrumentMoveEvent Leaving).</summary>
        public bool Left;

        Vector3 _from, _to, _head, _slideRight;
        float _yawFrom, _yawTo;
        const int Max = 72;
        readonly Vector2[] _pts = new Vector2[Max];
        readonly float[] _cum = new float[Max];
        int _n;

        public Vector3 From => _from;
        public Vector3 To => _to;
        public float YawFrom => _yawFrom;
        public float YawTo => _yawTo;
        public bool Started => Active && Delay <= 0f;
        /// <summary>0..1 through the motion (0 while it waits).</summary>
        public float Progress => Duration > 0f ? Mathf.Clamp01(T / Duration) : 1f;

        public static float DurationFor(float pathMetres) => Mathf.Min(MaxSeconds, BaseSeconds + PerMetre * Mathf.Max(0f, pathMetres - FreeMetres));

        /// <summary>A glide of the rig origin from (from, yawFrom) to (to, yawTo) about the head (world, degrees).</summary>
        public void Begin(Rig rig, InstrumentKind instrument, Goal goal, BringReason reason, Vector3 from, float yawFrom, Vector3 to, float yawTo, Vector3 head, float delay)
        {
            Rig = rig;
            Instrument = instrument;
            Target = goal;
            Reason = reason;
            Mode = Kind.Glide;
            _from = from; _to = to; _yawFrom = yawFrom; _yawTo = yawTo; _head = head;
            BuildPath();
            Duration = DurationFor(Length);
            Delay = Mathf.Max(0f, delay);
            T = 0f;
            Left = false;
            Active = true;
        }

        /// <summary>Today's page turn for one rig (mixed reality, or no display spot): in, it slides from rest + right x 0.9 m
        /// x dir at 0.85 scale to its rest; out, from rest to rest - right x 0.9 m x dir, shrinking (0.35 s, eased).</summary>
        public void BeginSlide(Rig rig, InstrumentKind instrument, Goal goal, BringReason reason, bool incoming, Vector3 rest, float yaw, Vector3 seatRight, int dir, float delay)
        {
            Rig = rig;
            Instrument = instrument;
            Target = goal;
            Reason = reason;
            Mode = incoming ? Kind.SlideIn : Kind.SlideOut;
            _from = _to = rest;
            _yawFrom = _yawTo = yaw;
            _slideRight = seatRight * (dir >= 0 ? 1f : -1f);
            Length = GrabConstants.TurnSlide;
            Duration = GrabConstants.TurnSeconds;
            Delay = Mathf.Max(0f, delay);
            T = 0f;
            Left = false;
            Active = true;
        }

        /// <summary>The room's walls (GrabConstants.RoomBox) and how far inside them the arc keeps (m).</summary>
        public const float WallClear = 0.35f;
        static readonly Rect s_room = Rect.MinMaxRect(GrabConstants.RoomBox.min.x, GrabConstants.RoomBox.min.z, GrabConstants.RoomBox.max.x, GrabConstants.RoomBox.max.z);

        void BuildPath()
        {
            Vector2 h = new Vector2(_head.x, _head.z);
            Vector2 a = new Vector2(_from.x, _from.z) - h, b = new Vector2(_to.x, _to.z) - h;
            float ra = a.magnitude, rb = b.magnitude;
            float ta = ra > 1e-4f ? Mathf.Atan2(a.x, a.y) : (rb > 1e-4f ? Mathf.Atan2(b.x, b.y) : 0f);
            float tb = rb > 1e-4f ? Mathf.Atan2(b.x, b.y) : ta;
            float RA = Mathf.Max(ra, MinRadius), RB = Mathf.Max(rb, MinRadius);
            float d = Mathf.DeltaAngle(ta * Mathf.Rad2Deg, tb * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            // the other way round is taken when the shorter one runs further outside the walls
            if (Mathf.Abs(d) > 1e-3f)
            {
                float other = d > 0f ? d - 2f * Mathf.PI : d + 2f * Mathf.PI;
                if (WallPenalty(h, ta, other, RA, RB) + 1e-4f < WallPenalty(h, ta, d, RA, RB)) d = other;
            }
            _n = 0;
            Add(h + a);
            if (RA > ra + 1e-4f) Add(h + Polar(ta, RA));
            // the arc (a spiral between the two radii): segments of at most ~4 cm, within the arrays, held inside the walls
            float approx = Mathf.Abs(d) * Mathf.Max(RA, RB);
            int segs = Mathf.Clamp(Mathf.CeilToInt(approx / 0.04f), 1, Max - 4);
            for (int i = 1; i <= segs; i++)
            {
                float u = i / (float)segs;
                Vector2 p = h + Polar(ta + d * u, Mathf.Lerp(RA, RB, u));
                if (i < segs) p = Inside(p, h);   // glides run in the VR room only (mixed reality slides)
                Add(p);
            }
            if (RB > rb + 1e-4f) Add(h + b);
            if (_n == 1) Add(h + b);
            Length = _cum[_n - 1];
        }

        static float WallPenalty(Vector2 h, float ta, float d, float ra, float rb)
        {
            float sum = 0f;
            for (int i = 1; i < 24; i++)
            {
                float u = i / 24f;
                Vector2 p = h + Polar(ta + d * u, Mathf.Lerp(ra, rb, u));
                sum += Mathf.Max(0f, s_room.xMin + WallClear - p.x) + Mathf.Max(0f, p.x - (s_room.xMax - WallClear))
                     + Mathf.Max(0f, s_room.yMin + WallClear - p.y) + Mathf.Max(0f, p.y - (s_room.yMax - WallClear));
            }
            return sum;
        }

        /// <summary>A point of the arc held WallClear inside the walls, and never nearer the head than MinRadius.</summary>
        static Vector2 Inside(Vector2 p, Vector2 h)
        {
            var q = new Vector2(Mathf.Clamp(p.x, s_room.xMin + WallClear, s_room.xMax - WallClear), Mathf.Clamp(p.y, s_room.yMin + WallClear, s_room.yMax - WallClear));
            Vector2 r = q - h;
            if (r.sqrMagnitude < MinRadius * MinRadius && r.sqrMagnitude > 1e-8f) q = h + r.normalized * MinRadius;
            return q;
        }

        static Vector2 Polar(float t, float r) => new Vector2(Mathf.Sin(t) * r, Mathf.Cos(t) * r);

        void Add(Vector2 p)
        {
            if (_n >= Max) return;
            _cum[_n] = _n == 0 ? 0f : _cum[_n - 1] + Vector2.Distance(_pts[_n - 1], p);
            _pts[_n] = p;
            _n++;
        }

        /// <summary>The horizontal point at arc length s along the path.</summary>
        Vector2 At(float s)
        {
            if (_n <= 1) return _pts[0];
            if (s <= 0f) return _pts[0];
            if (s >= Length) return _pts[_n - 1];
            int i = 1;
            while (i < _n - 1 && _cum[i] < s) i++;
            float seg = _cum[i] - _cum[i - 1];
            float k = seg > 1e-6f ? (s - _cum[i - 1]) / seg : 1f;
            return Vector2.Lerp(_pts[i - 1], _pts[i], k);
        }

        /// <summary>The pose (and scale) at time t into the motion (0..Duration).</summary>
        public void Sample(float t, out Vector3 pos, out float yawDeg, out float scale)
        {
            scale = 1f;
            if (Mode != Kind.Glide)
            {
                float e = LayoutMath.Ease(Mathf.Clamp01(t / Duration));
                float shrink = 1f - GrabConstants.TurnScale;
                yawDeg = _yawTo;
                if (Mode == Kind.SlideIn)
                {
                    pos = _to + _slideRight * (GrabConstants.TurnSlide * (1f - e));
                    scale = GrabConstants.TurnScale + shrink * e;
                }
                else
                {
                    pos = _from - _slideRight * (GrabConstants.TurnSlide * e);
                    scale = 1f - shrink * e;
                }
                return;
            }
            float travel = Mathf.Max(0.01f, Duration - 2f * LiftSeconds);
            if (t < LiftSeconds)
            {
                float k = LayoutMath.Ease(Mathf.Clamp01(t / LiftSeconds));
                pos = _from + Vector3.up * (Lift * k);
                yawDeg = _yawFrom;
                return;
            }
            if (t >= Duration - LiftSeconds)
            {
                float k = LayoutMath.Ease(Mathf.Clamp01((Duration - t) / LiftSeconds));
                pos = _to + Vector3.up * (Lift * k);
                yawDeg = _yawTo;
                return;
            }
            float f = LayoutMath.Ease(Mathf.Clamp01((t - LiftSeconds) / travel));
            Vector2 p = At(f * Length);
            float y = Mathf.Lerp(_from.y, _to.y, f) + Lift;
            pos = new Vector3(p.x, y, p.y);
            yawDeg = Mathf.LerpAngle(_yawFrom, _yawTo, f);
        }

        /// <summary>Advances the glide by dt and poses the rig. True on the frame it ends (the rig is then at its target).</summary>
        public bool Step(float dt)
        {
            if (!Active) return false;
            if (Delay > 0f)
            {
                Delay -= dt;
                if (Delay > 0f) return false;
                dt = -Delay;
                Delay = 0f;
            }
            T += dt;
            bool done = T >= Duration;
            Apply(done ? Duration : T);
            return done;
        }

        /// <summary>Poses the rig at its end now (a glide cut short: a new arrangement, a mode change).</summary>
        public void Finish()
        {
            if (!Active) return;
            Delay = 0f;
            T = Duration;
            Apply(Duration);
        }

        void Apply(float t)
        {
            Transform g = Rig?.Group;
            if (g == null) return;
            Sample(t, out Vector3 p, out float yaw, out float s);
            if (Mode == Kind.SlideOut && t >= Duration) { p = _from; s = 1f; }   // back at rest, ready to be shown again
            g.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            Vector3 ls = Vector3.one * s;
            if (g.localScale != ls) g.localScale = ls;
        }

        /// <summary>The closest the rig origin comes to the head over the motion, sampled every <paramref name="step"/> metres
        /// of travel and through the lift and descent (GlideTests).</summary>
        public float MinHeadDistance(Vector3 head, float step = 0.02f)
        {
            float min = float.PositiveInfinity;
            int n = Mathf.Max(8, Mathf.CeilToInt((Length + 2f * Lift) / step) * 2);
            for (int i = 0; i <= n; i++)
            {
                Sample(Duration * i / n, out Vector3 p, out _, out _);
                min = Mathf.Min(min, Vector3.Distance(p, head));
            }
            return min;
        }

        public void Clear()
        {
            Active = false;
            Rig = null;
            Delay = T = 0f;
        }
    }
}
