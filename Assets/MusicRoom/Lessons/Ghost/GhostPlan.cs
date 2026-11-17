// The ghost hands' plan (pure numbers, EditMode-tested): what each hand plays and when, and where its fingers or its
// stick are at any moment. The recorder's playback is read ahead (Recorder.Upcoming), so every stroke is planned before
// it sounds and the hand arrives on time: the motion is a pure function of the plan and the audio time, which makes it
// the same at any frame rate and lets the tests ask "where is the fingertip at the moment the key sounds".
//
// Positions are in the instrument's own frame (its root), so the hands follow an instrument that glides in or is moved.
//
// Two kinds of hand:
//   - strike hands (drums, harp, handpan, hand drums, marimba, bowls): one contact (a fingertip, a palm or a held tool's
//     tip) travels from stroke to stroke. Close strokes are joined by one arc (it lifts between them, higher for a louder
//     or slower stroke); after a phrase the hand rises to rest a hand's width above its last note and waits there, and
//     comes down for the next one Approach before it. A rub (bowls) circles the rim while its lane sounds;
//   - piano hands: each finger has its own strokes (a key down from On to Off) and the palm moves between the positions
//     the fingering chose (PianoFingering), arriving just before the notes. An idle finger hovers over the keys at its
//     natural place under the palm.
using System;
using UnityEngine;

namespace MusicRoom.Lessons.Ghost
{
    public enum StrokeStyle : byte { Key = 0, Tap, Slap, Stick, Mallet, Pluck, Rub }

    /// <summary>One planned stroke of one hand (instrument-local).</summary>
    public struct GhostStroke
    {
        /// <summary>Audio time it sounds (On) and lets go (Off: +inf while not known; On for a plain strike).</summary>
        public double On, Off;
        /// <summary>The contact point, the surface's up (unit) and the direction the hand reaches along (unit, across Up).</summary>
        public Vector3 Point, Up, Forward;
        public float Vel;
        /// <summary>Piano: the finger (0 thumb .. 4 little; -1 a palm move only). Others: -1.</summary>
        public sbyte Finger;
        public StrokeStyle Style;
        /// <summary>Piano: the MIDI key. Rub: its lane. Otherwise -1.</summary>
        public int Key;
        /// <summary>Piano: the palm's planned x and its forward shift (black keys) at On.</summary>
        public float PalmX, PalmZ;
        /// <summary>Rub: the rim's radius and the angular speed (rad/s) and start angle around Point (Forward = angle 0).</summary>
        public float Radius, Spin, Phase;
    }

    /// <summary>What a strike hand does at a moment (instrument-local).</summary>
    public struct StrikeFrame
    {
        public bool Has;
        /// <summary>Where the contact (fingertip, palm, tool tip) is, lift included.</summary>
        public Vector3 Point;
        public Vector3 Up, Forward;
        /// <summary>Height above the surface along Up (m).</summary>
        public float Lift;
        /// <summary>1 at an impact, falling to 0 over HitTime (a fingertip flick, a pluck's curl).</summary>
        public float Hit;
        /// <summary>0 playing .. 1 resting between phrases.</summary>
        public float Rest;
        public StrokeStyle Style;
    }

    /// <summary>One hand's strokes in time order (a fixed ring, allocation-free after construction).</summary>
    public sealed class GhostTrack
    {
        public const int Capacity = 256;
        /// <summary>A strike hand: rest height above its last note, the time it takes to get there, the approach from rest.</summary>
        public const float RestLift = 0.075f, RiseTime = 0.4f, Approach = 0.45f, HitTime = 0.12f;
        /// <summary>Two strokes closer than this (s) are joined by one arc; farther apart the hand rests between them.</summary>
        public const float ArcGap = 2f * Approach;

        readonly GhostStroke[] _s = new GhostStroke[Capacity];
        int _n;

        public int Count => _n;
        public ref GhostStroke this[int i] => ref _s[i];
        /// <summary>Strokes dropped because the ring was full (a dense take seen far ahead: the oldest past ones go first).</summary>
        public int Dropped { get; private set; }

        public void Clear() { _n = 0; }

        /// <summary>Insert keeping On order (after equal ones). A full ring drops its oldest stroke.</summary>
        public void Add(in GhostStroke s)
        {
            if (_n == Capacity) { Array.Copy(_s, 1, _s, 0, _n - 1); _n--; Dropped++; }
            int i = _n;
            while (i > 0 && _s[i - 1].On > s.On) i--;
            if (i < _n) Array.Copy(_s, i, _s, i + 1, _n - i);
            _s[i] = s;
            _n++;
        }

        /// <summary>The latest stroke with On &lt;= t (finger: that finger's only; -2 any), -1 when none.</summary>
        public int Last(double t, int finger = -2)
        {
            for (int i = _n - 1; i >= 0; i--) if (_s[i].On <= t && (finger == -2 || _s[i].Finger == finger)) return i;
            return -1;
        }

        /// <summary>The first stroke with On &gt; t (finger: that finger's only; -2 any), -1 when none.</summary>
        public int Next(double t, int finger = -2)
        {
            for (int i = 0; i < _n; i++) if (_s[i].On > t && (finger == -2 || _s[i].Finger == finger)) return i;
            return -1;
        }

        /// <summary>The newest stroke's On (-inf when empty).</summary>
        public double LastOn => _n > 0 ? _s[_n - 1].On : double.NegativeInfinity;

        /// <summary>Something to show around t: a stroke within [t - linger, t + ahead], or one still held.</summary>
        public bool Active(double t, double linger, double ahead)
        {
            for (int i = 0; i < _n; i++)
            {
                ref GhostStroke s = ref _s[i];
                if (s.On <= t + ahead && (s.On >= t - linger || s.Off >= t - linger)) return true;
            }
            return false;
        }

        /// <summary>Sets Off on the latest held stroke of this key (piano key, rub lane) that is not let go yet. False when none.</summary>
        public bool Release(int key, double off)
        {
            for (int i = _n - 1; i >= 0; i--)
            {
                ref GhostStroke s = ref _s[i];
                if (s.Key != key || !double.IsPositiveInfinity(s.Off)) continue;
                s.Off = Math.Max(s.On, off);
                return true;
            }
            return false;
        }

        /// <summary>Lets every held stroke go at t (playback stopped).</summary>
        public void ReleaseAll(double t)
        {
            for (int i = 0; i < _n; i++) if (_s[i].Off > t) _s[i].Off = Math.Max(_s[i].On, t);
        }

        /// <summary>Forgets every stroke after t (playback stopped: what was planned ahead never sounds).</summary>
        public void DropAfter(double t)
        {
            int k = 0;
            for (int i = 0; i < _n; i++) if (_s[i].On <= t) _s[k++] = _s[i];
            _n = k;
        }

        /// <summary>Drops what no longer shapes the motion: per finger (or for the whole hand) only the latest stroke that
        /// started before <paramref name="now"/> is kept, plus every held and every future one.</summary>
        public void Prune(double now)
        {
            int seen = 0;   // bit per finger slot (0..5: finger + 1)
            int k = _n;
            for (int i = _n - 1; i >= 0; i--)
            {
                ref GhostStroke s = ref _s[i];
                int bit = 1 << Mathf.Clamp(s.Finger + 1, 0, 5);
                bool keep = s.On > now || s.Off > now || (seen & bit) == 0;
                if (s.On <= now) seen |= bit;
                if (keep) _s[--k] = s;
            }
            if (k > 0) { Array.Copy(_s, k, _s, 0, _n - k); _n -= k; }
        }

        static float Ease(float u) { u = u < 0f ? 0f : u > 1f ? 1f : u; return u * u * (3f - 2f * u); }

        /// <summary>The height a strike lifts to between two strokes gap seconds apart (louder and slower = higher).</summary>
        public static float StrokeHeight(StrokeStyle style, float vel, double gap)
        {
            float room = Mathf.Clamp01((float)gap / 0.35f);
            vel = Mathf.Clamp01(vel);
            switch (style)
            {
                case StrokeStyle.Stick: return (0.05f + 0.13f * vel) * room;
                case StrokeStyle.Mallet: return (0.04f + 0.08f * vel) * room;
                case StrokeStyle.Slap: return (0.04f + 0.05f * vel) * room;
                case StrokeStyle.Pluck: return 0.012f * room;
                default: return (0.02f + 0.025f * vel) * room;
            }
        }

        /// <summary>Where the rub's contact is at t (on the rim circle).</summary>
        public static Vector3 RubPoint(in GhostStroke s, double t)
        {
            float a = s.Phase + s.Spin * (float)(t - s.On);
            Vector3 side = Vector3.Cross(s.Up, s.Forward);
            return s.Point + s.Radius * (Mathf.Cos(a) * s.Forward + Mathf.Sin(a) * side);
        }

        static Vector3 EndPoint(in GhostStroke s) => s.Style == StrokeStyle.Rub && !double.IsPositiveInfinity(s.Off) ? RubPoint(s, s.Off) : s.Point;
        static double EndTime(in GhostStroke s) => s.Style == StrokeStyle.Rub ? s.Off : s.On;

        /// <summary>A strike hand at audio time t.</summary>
        public StrikeFrame Strike(double t)
        {
            var f = new StrikeFrame();
            int ia = Last(t), ib = Next(t);
            if (ia < 0 && ib < 0) return f;
            f.Has = true;
            if (ia >= 0)
            {
                ref GhostStroke a = ref _s[ia];
                f.Style = a.Style;
                if (a.Style == StrokeStyle.Rub && t < a.Off)
                {
                    f.Point = RubPoint(a, t);
                    f.Up = a.Up;
                    f.Forward = a.Forward;
                    return f;
                }
                f.Hit = Mathf.Clamp01(1f - (float)(t - a.On) / HitTime);
            }
            if (ia < 0)
            {
                // Before the first stroke: waiting at rest above it, then down onto it.
                ref GhostStroke b = ref _s[ib];
                float v = Mathf.Clamp01((float)(t - (b.On - Approach)) / Approach);
                float e = Ease(v);
                float h = RestLift * (1f - e) + StrokeHeight(b.Style, b.Vel, Approach) * 0.5f * Mathf.Sin(Mathf.PI * v);
                f.Style = b.Style;
                f.Up = b.Up;
                f.Forward = b.Forward;
                f.Lift = h;
                f.Point = b.Point + b.Up * h;
                f.Rest = 1f - e;
                return f;
            }
            ref GhostStroke s0 = ref _s[ia];
            Vector3 p0 = EndPoint(s0);
            double end = EndTime(s0);
            if (ib >= 0)
            {
                ref GhostStroke s1 = ref _s[ib];
                double gap = s1.On - end;
                if (gap <= ArcGap)
                {
                    // One arc from stroke to stroke.
                    float u = gap > 1e-6 ? Mathf.Clamp01((float)((t - end) / gap)) : 1f;
                    float e = Ease(u);
                    float h = StrokeHeight(s1.Style, s1.Vel, gap) * Mathf.Sin(Mathf.PI * u);
                    f.Up = Vector3.Slerp(s0.Up, s1.Up, e).normalized;
                    f.Forward = Vector3.Slerp(s0.Forward, s1.Forward, e).normalized;
                    f.Lift = h;
                    f.Point = Vector3.Lerp(p0, s1.Point, e) + f.Up * h;
                    f.Style = u < 0.5f ? s0.Style : s1.Style;
                    return f;
                }
                if (t >= s1.On - Approach)
                {
                    // From rest above the last note down onto the next.
                    float v = Mathf.Clamp01((float)(t - (s1.On - Approach)) / Approach);
                    float e = Ease(v);
                    float h = RestLift * (1f - e) + StrokeHeight(s1.Style, s1.Vel, Approach) * 0.5f * Mathf.Sin(Mathf.PI * v);
                    f.Up = Vector3.Slerp(s0.Up, s1.Up, e).normalized;
                    f.Forward = Vector3.Slerp(s0.Forward, s1.Forward, e).normalized;
                    f.Lift = h;
                    f.Point = Vector3.Lerp(p0, s1.Point, e) + f.Up * h;
                    f.Rest = 1f - e;
                    f.Style = s1.Style;
                    return f;
                }
            }
            // After a note, nothing near: up to rest above it, a small rebound on the way.
            float w = Mathf.Clamp01((float)(t - end) / RiseTime);
            float hr = RestLift * Ease(w) + StrokeHeight(s0.Style, s0.Vel, 0.35) * 0.35f * Mathf.Sin(Mathf.PI * w);
            f.Up = s0.Up;
            f.Forward = s0.Forward;
            f.Lift = hr;
            f.Point = p0 + s0.Up * hr;
            f.Rest = Ease(w);
            return f;
        }

        // ---------------------------------------------------------------- piano hands

        /// <summary>The palm's planned x / forward shift at t: it moves from one planned position to the next over at most
        /// PalmMove, arriving PalmLead before the next notes. False when the hand has no strokes.</summary>
        public bool Palm(double t, out float x, out float z)
        {
            x = z = 0f;
            int ia = Last(t), ib = Next(t);
            if (ia < 0 && ib < 0) return false;
            if (ia < 0) { x = _s[ib].PalmX; z = _s[ib].PalmZ; return true; }
            x = _s[ia].PalmX;
            z = _s[ia].PalmZ;
            if (ib < 0) return true;
            ref GhostStroke b = ref _s[ib];
            double start = Math.Max(_s[ia].On + 0.02, b.On - PianoFingering.PalmMove), stop = b.On - PianoFingering.PalmLead;
            if (t <= start) return true;
            float e = stop > start ? Ease((float)((t - start) / (stop - start))) : 1f;
            x = Mathf.Lerp(x, b.PalmX, e);
            z = Mathf.Lerp(z, b.PalmZ, e);
            return true;
        }

        /// <summary>Piano finger f's tip at t (instrument-local; up = the piano's +y): pressed with its key between On and
        /// Off, lifting off after, travelling in a small arc to the next key and on it FingerLead before it sounds; an idle
        /// finger hovers at its place under the palm (rest: that hover point now). Pressed: 0 up .. 1 the key down.</summary>
        public Vector3 Finger(int finger, double t, Vector3 rest, out float pressed)
        {
            pressed = 0f;
            int ia = Last(t, finger), ib = Next(t, finger);
            Vector3 pos = rest;
            double free = double.NegativeInfinity;
            if (ia >= 0)
            {
                ref GhostStroke a = ref _s[ia];
                if (t < a.Off)
                {
                    pressed = Ease((float)((t - (a.On - PianoFingering.FingerLead)) / (PianoFingering.FingerLead + 0.03)));
                    pos = a.Point - Vector3.up * (PianoFingering.KeyDip * pressed);
                    return pos;
                }
                float r = Ease((float)((t - a.Off) / PianoFingering.FingerLift));
                pos = Vector3.Lerp(a.Point + Vector3.up * 0.004f, rest, r);
                free = a.Off;
            }
            if (ib >= 0)
            {
                ref GhostStroke b = ref _s[ib];
                double window = Math.Min(PianoFingering.FingerTravel, Math.Max(0.06, b.On - PianoFingering.FingerLead - free));
                double from = b.On - PianoFingering.FingerLead - window;
                if (t > from)
                {
                    float w = Ease((float)((t - from) / window));
                    float arc = Mathf.Min(0.02f, 0.006f + 0.15f * Mathf.Abs(b.Point.x - pos.x)) * Mathf.Sin(Mathf.PI * w);
                    pos = Vector3.Lerp(pos, b.Point, w) + Vector3.up * arc;
                    if (t > b.On - PianoFingering.FingerLead)
                        pressed = Ease((float)((t - (b.On - PianoFingering.FingerLead)) / (PianoFingering.FingerLead + 0.03)));
                    pos -= Vector3.up * (PianoFingering.KeyDip * pressed);
                }
            }
            return pos;
        }
    }
}
