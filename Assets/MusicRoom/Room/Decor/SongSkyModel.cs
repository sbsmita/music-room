// "Your song becomes a night sky" (the playback show), its numbers (no Unity objects: EditMode tests drive it directly;
// Room/Decor/SongSky.cs draws it). While a recording or a loop plays back, every note it sends back makes a star: the
// star rises from the instrument that played it (From) and settles in the sky (To): across the sky with the song's time
// (an arc around where the player looks, left to right), higher for a higher note (SongStars.TryHeight), brighter and
// larger for a louder one, in its instrument's colour (SongStars.ColorOf). A star is joined by a thin line to the star
// before it of the same instrument, so by the end the sky shows the song as a constellation.
// Where the sky is (Frame, fixed when the playback starts, from the head then):
//   - VR (the room shown): just under the room's ceiling (its real height and its walls: the ceiling's bounds as built,
//     so the sky follows the room wherever it stands), ceiling - TopGap at the highest note, never closer to the head than
//     MinAbove above it, inside the ceiling's rectangle (Margin from its walls);
//   - mixed reality (the room hidden): in open air over the instruments, MrAbove .. MrTop above the head (and below
//     MrCeiling over the floor, a real room's ceiling), MrDistance away.
// Every star stays out of the head's sphere (HeadClear: placed outside it, and pushed out of it while it rises: the
// drawing's shader does the same with the head of the frame it draws), and above the eyes: nothing settles in front of
// the player's eyes or the instruments.
// A loop: its passes lay their notes over the same arc (song time modulo the pass), a note landing where the same
// instrument's star already shines takes its place (the old one fades, DieFade). A long take: Max stars at most, the
// oldest fade (FadeAhead slots before they are reused).
// After the playback ends the sky stays Linger s, fades over FadeOut s, then everything is cleared (Resting); a new
// playback clears the old sky first. Time: the model's own clock (Tick's dt), the drawing's shader gets the same number.
// Allocation-free after construction.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public sealed class SongSkyModel
    {
        public const int Max = 256;
        /// <summary>A star's rise (s), its line's fade in after it settles (s), a replaced or dropped star's fade (s).</summary>
        public const float Rise = 1.6f, LineIn = 0.9f, DieFade = 0.7f;
        /// <summary>After the playback: the sky stays this long (s), then fades over FadeOut (s).</summary>
        public const float Linger = 4f, FadeOut = 2.5f;
        /// <summary>The head's sphere nothing enters (m), as the butterflies'.</summary>
        public const float HeadClear = 0.35f;
        /// <summary>The arc of the sky around the player's view at its widest (rad), and the song length that fills it (s):
        /// a shorter song draws a narrower arc (MinSpanK of it at the least).</summary>
        public const float SpanMax = 150f * Mathf.Deg2Rad, FullSpanSeconds = 14f, MinSpanK = 0.45f;
        /// <summary>VR: the highest note this far under the ceiling, the lowest at most LowBand under it, every star at least
        /// MinAbove over the head; how far out (m) and the margin from the walls (m).</summary>
        public const float TopGap = 0.14f, LowBand = 0.95f, MinAbove = 0.55f, VrDistance = 1.9f, Margin = 0.3f, MinDistance = 0.5f;
        /// <summary>Mixed reality: heights over the head (m), a real ceiling's height over the floor (m), how far out (m).</summary>
        public const float MrAbove = 0.6f, MrTop = 1.15f, MrCeiling = 2.45f, MrDistance = 1.45f;
        /// <summary>A star's core size (m) at the softest and the loudest note, and its brightness range.</summary>
        public const float SizeMin = 0.024f, SizeMax = 0.05f, BrightMin = 0.5f;
        /// <summary>A loop's note this close to a star of its instrument takes its place (m).</summary>
        public const float SameStar = 0.025f;
        /// <summary>When the sky is full, the star this many slots ahead starts to fade, so a reused slot has faded first.</summary>
        public const int FadeAhead = 24;
        /// <summary>The song length used when none is known (a loop played live without its pass): s.</summary>
        public const float DefaultSeconds = 16f;

        public struct Star
        {
            public Vector3 From, To;
            public float Birth, Death, LineDeath, Size, Bright, Phase, S;
            public InstrumentKind Kind;
            /// <summary>The previous star of the same instrument its line joins (-1: none) and the next one joined to it.</summary>
            public int Prev, Next;
            public bool Alive;
        }

        /// <summary>Where the sky is: fixed when a playback starts.</summary>
        public struct Frame
        {
            public Vector3 Head, Forward;
            /// <summary>VR: the room's ceiling as built (its world bounds: x/z its rectangle, max.y its height).</summary>
            public bool Room;
            public Bounds Ceiling;
            /// <summary>Mixed reality: the floor's height.</summary>
            public float Floor;
        }

        public enum Phase : byte { Resting = 0, Playing, Lingering, Fading }

        public readonly Star[] Stars = new Star[Max];
        readonly int[] _dirty = new int[Max * 2];
        readonly bool[] _isDirty = new bool[Max];
        readonly int[] _last = new int[InstrumentIds.KindCount];
        int _dirtyCount, _next, _live;
        float _phaseAt, _seconds = DefaultSeconds, _span = SpanMax;
        bool _loop;
        double _t0 = double.NaN;
        Frame _frame;
        Mulberry32 _rng;

        public SongSkyModel(uint seed = 0x5a7u)
        {
            _rng = new Mulberry32(seed);
            for (int k = 0; k < _last.Length; k++) _last[k] = -1;
            for (int i = 0; i < Max; i++) { Stars[i].Prev = Stars[i].Next = -1; Stars[i].Death = Stars[i].LineDeath = float.PositiveInfinity; }
        }

        /// <summary>The model's clock (s): Tick's dt summed. The shader gets the same number.</summary>
        public float Time { get; private set; }
        public Phase State { get; private set; }
        public bool Resting => State == Phase.Resting;
        /// <summary>The whole sky's fade: 1 while it plays and lingers, down to 0 as it fades out.</summary>
        public float Fade { get; private set; }
        /// <summary>Stars made since the playback started (Sunny's tip) and alive now.</summary>
        public int Made { get; private set; }
        public int Live => _live;
        /// <summary>Stars made in all (tests).</summary>
        public int TotalMade { get; private set; }
        /// <summary>Stars older than this are from an earlier sky and never drawn (the shader's epoch).</summary>
        public float Epoch { get; private set; } = -1f;
        public bool HasFrame { get; private set; }
        public Frame Current => _frame;
        public float SongSeconds => _seconds;
        public bool Loop => _loop;
        /// <summary>The sky was cleared (rest) or re-framed since the drawing last looked (it rewrites its field).</summary>
        public int FrameVersion { get; private set; }

        /// <summary>Slots changed since the drawing took them (it uploads those stars and their lines).</summary>
        public int DirtyCount => _dirtyCount;
        public int Dirty(int i) => _dirty[i];
        public void ClearDirty()
        {
            for (int i = 0; i < _dirtyCount; i++) _isDirty[_dirty[i]] = false;
            _dirtyCount = 0;
        }

        // ---------------------------------------------------------------- the playback

        /// <summary>A playback starts: an old sky clears (its stars fade), the sky is framed from the head now, the song's
        /// time starts at audioStart (its length: seconds; loop: notes repeat modulo it).</summary>
        public void Start(in Frame frame, double audioStart, float seconds, bool loop)
        {
            if (_live > 0) KillAll();
            _frame = Normalise(frame);
            HasFrame = true;
            FrameVersion++;
            _t0 = audioStart;
            _loop = loop;
            _seconds = seconds > 0.05f ? seconds : DefaultSeconds;
            _span = SpanMax * Mathf.Clamp(_seconds / FullSpanSeconds, MinSpanK, 1f);
            for (int k = 0; k < _last.Length; k++) _last[k] = -1;
            Made = 0;
            Fade = 1f;
            Enter(Phase.Playing);
        }

        /// <summary>The playback ended: the sky lingers, then fades.</summary>
        public void End()
        {
            if (State == Phase.Playing) Enter(Phase.Lingering);
        }

        void Enter(Phase p) { State = p; _phaseAt = Time; }

        /// <summary>A played-back onset: a star rising from <paramref name="from"/> (world), at its song time (audio time),
        /// its height 0..1 and loudness 0..1. delay: s until it sounds (a looper note sent ahead). Returns its slot (-1: no
        /// frame yet).</summary>
        public int Note(InstrumentKind kind, Vector3 from, double audioTime, float height, float vel, float delay = 0f)
        {
            if (!HasFrame) return -1;
            if (State != Phase.Playing) { Fade = 1f; Enter(Phase.Playing); }
            if (double.IsNaN(_t0)) _t0 = audioTime;
            float s = SongFraction(audioTime);
            Vector3 to = Place(s, height);
            float birth = Time + Mathf.Max(0f, delay);
            int k = (int)kind;
            bool kindOk = (uint)k < (uint)_last.Length;
            // a loop's pass lays its notes over the last one's: the same note takes its old star's place
            if (_loop)
                for (int i = 0; i < Max; i++)
                {
                    ref Star o = ref Stars[i];
                    if (!o.Alive || o.Kind != kind || o.Death < float.PositiveInfinity) continue;
                    if ((o.To - to).sqrMagnitude < SameStar * SameStar) Kill(i, birth);
                }
            int slot = _next;
            _next = (_next + 1) % Max;
            Free(slot);
            // the slot a little ahead starts to fade, so it has faded when its turn comes
            int ahead = (slot + FadeAhead) % Max;
            if (Stars[ahead].Alive && Stars[ahead].Death == float.PositiveInfinity) Kill(ahead, Time);
            vel = Mathf.Clamp01(vel);
            ref Star st = ref Stars[slot];
            st.Alive = true;
            st.Kind = kind;
            st.From = ClearOfHead(from, _frame.Head);
            st.To = to;
            st.S = s;
            st.Birth = birth;
            st.Death = st.LineDeath = float.PositiveInfinity;
            st.Size = Mathf.Lerp(SizeMin, SizeMax, vel);
            st.Bright = Mathf.Lerp(BrightMin, 1f, vel);
            st.Phase = (float)_rng.Next();
            st.Prev = st.Next = -1;
            // joined to the star before it of the same instrument (not back across a loop's turn)
            if (kindOk)
            {
                int p = _last[k];
                if (p >= 0 && Stars[p].Alive && Stars[p].Kind == kind && Stars[p].Death == float.PositiveInfinity && Stars[p].S <= s + 1e-5f)
                {
                    st.Prev = p;
                    Stars[p].Next = slot;
                }
                _last[k] = slot;
            }
            _live++;
            Made++;
            TotalMade++;
            MarkDirty(slot);
            return slot;
        }

        /// <summary>A slot is taken over: its old star goes (its line, and the line that started at it).</summary>
        void Free(int slot)
        {
            ref Star o = ref Stars[slot];
            if (!o.Alive) return;
            if (o.Prev >= 0 && Stars[o.Prev].Next == slot) Stars[o.Prev].Next = -1;
            if (o.Next >= 0 && Stars[o.Next].Prev == slot)
            {
                Stars[o.Next].Prev = -1;
                Stars[o.Next].LineDeath = float.NegativeInfinity;   // its line had nothing to start from any more
                MarkDirty(o.Next);
            }
            for (int k = 0; k < _last.Length; k++) if (_last[k] == slot) _last[k] = -1;
            o.Alive = false;
            o.Prev = o.Next = -1;
            _live--;
        }

        /// <summary>A star fades from <paramref name="at"/> (its line with it, and the line that starts at it).</summary>
        void Kill(int i, float at)
        {
            ref Star o = ref Stars[i];
            if (o.Death <= at) return;
            o.Death = at;
            if (o.LineDeath > at) o.LineDeath = at;
            MarkDirty(i);
            if (o.Next >= 0 && Stars[o.Next].Prev == i && Stars[o.Next].LineDeath > at)
            {
                Stars[o.Next].LineDeath = at;
                MarkDirty(o.Next);
            }
        }

        void KillAll()
        {
            for (int i = 0; i < Max; i++) if (Stars[i].Alive) Kill(i, Time);
        }

        void MarkDirty(int i)
        {
            if (_isDirty[i] || _dirtyCount >= _dirty.Length) return;
            _isDirty[i] = true;
            _dirty[_dirtyCount++] = i;
        }

        /// <summary>One frame: the clock, the linger and the fade, the dead stars' slots, the rest.</summary>
        public void Tick(float dt)
        {
            Time += dt > 0f ? dt : 0f;
            switch (State)
            {
                case Phase.Lingering:
                    if (Time - _phaseAt >= Linger) Enter(Phase.Fading);
                    break;
                case Phase.Fading:
                    Fade = Mathf.Clamp01(1f - (Time - _phaseAt) / FadeOut);
                    if (Fade <= 0f) Clear();
                    break;
            }
            // stars that have faded out give their slots back (their slot keeps its data; the drawing never shows it again)
            if (_live > 0)
                for (int i = 0; i < Max; i++)
                    if (Stars[i].Alive && Time - Stars[i].Death > DieFade) Free(i);
        }

        /// <summary>Everything goes: no star, no frame, the clock's epoch moves on so nothing older is drawn.</summary>
        public void Clear()
        {
            for (int i = 0; i < Max; i++) { Stars[i].Alive = false; Stars[i].Prev = Stars[i].Next = -1; }
            for (int k = 0; k < _last.Length; k++) _last[k] = -1;
            _live = 0;
            Made = 0;
            Fade = 0f;
            Epoch = Time;
            HasFrame = false;
            _t0 = double.NaN;
            FrameVersion++;
            ClearDirty();
            Enter(Phase.Resting);
        }

        // ---------------------------------------------------------------- where

        /// <summary>The song's time as a fraction of the arc (0 its left end .. 1 its right): a take from its start, a loop
        /// modulo its pass.</summary>
        public float SongFraction(double audioTime)
        {
            double t = double.IsNaN(_t0) ? 0 : audioTime - _t0;
            if (t < 0) t = 0;
            double f = t / _seconds;
            if (_loop) f -= System.Math.Floor(f);
            return Mathf.Clamp01((float)f);
        }

        static Frame Normalise(Frame f)
        {
            Vector3 fw = f.Forward;
            fw.y = 0f;
            if (fw.sqrMagnitude < 1e-6f) fw = Vector3.forward;
            f.Forward = fw.normalized;
            return f;
        }

        /// <summary>The lowest and highest star heights of the frame (world y).</summary>
        public void Heights(out float low, out float high)
        {
            Vector3 head = _frame.Head;
            if (_frame.Room)
            {
                float ceil = _frame.Ceiling.max.y;
                high = ceil - TopGap;
                low = Mathf.Max(ceil - LowBand, head.y + MinAbove);
                if (low > high - 0.2f) low = high - 0.2f;
            }
            else
            {
                high = Mathf.Min(head.y + MrTop, _frame.Floor + MrCeiling);
                low = head.y + MrAbove;
                if (low > high - 0.25f) high = low + 0.25f;   // a tall player under a low guess: the head's clearance wins
            }
        }

        /// <summary>Where a star settles: s across the arc, h its height 0..1. Outside the head's sphere always.</summary>
        public Vector3 Place(float s, float h)
        {
            Vector3 head = _frame.Head;
            float az = (Mathf.Clamp01(s) - 0.5f) * _span;
            Vector3 f = _frame.Forward;
            float c = Mathf.Cos(az), sn = Mathf.Sin(az);
            // turned by az about up (positive: to the right of the forward)
            var dir = new Vector3(f.x * c + f.z * sn, 0f, f.z * c - f.x * sn);
            Heights(out float low, out float high);
            float y = Mathf.Lerp(low, high, Mathf.Clamp01(h));
            float d;
            if (_frame.Room)
            {
                d = Mathf.Min(VrDistance, Exit(head, dir, _frame.Ceiling, Margin));
                if (d < MinDistance) d = MinDistance;
            }
            else d = MrDistance;
            var p = new Vector3(head.x + dir.x * d, y, head.z + dir.z * d);
            return ClearOfHead(p, head);
        }

        /// <summary>How far a ray from p along a horizontal dir runs inside the box's x/z rectangle shrunk by margin (0 when p
        /// is outside it).</summary>
        static float Exit(Vector3 p, Vector3 dir, Bounds box, float margin)
        {
            float x0 = box.min.x + margin, x1 = box.max.x - margin, z0 = box.min.z + margin, z1 = box.max.z - margin;
            if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) return VrDistance;   // the head outside the room: no walls to keep to
            float t = float.PositiveInfinity;
            if (dir.x > 1e-5f) t = Mathf.Min(t, (x1 - p.x) / dir.x);
            else if (dir.x < -1e-5f) t = Mathf.Min(t, (x0 - p.x) / dir.x);
            if (dir.z > 1e-5f) t = Mathf.Min(t, (z1 - p.z) / dir.z);
            else if (dir.z < -1e-5f) t = Mathf.Min(t, (z0 - p.z) / dir.z);
            return t;
        }

        /// <summary>p, pushed out of the head's sphere (straight up when it is at the head's centre).</summary>
        public static Vector3 ClearOfHead(Vector3 p, Vector3 head)
        {
            Vector3 v = p - head;
            float m = v.magnitude;
            if (m >= HeadClear) return p;
            return head + (m > 1e-5f ? v / m : Vector3.up) * HeadClear;
        }

        // ---------------------------------------------------------------- the drawing's mirror

        /// <summary>Where a star is drawn at time t with the head at <paramref name="head"/> (the shader's maths): it rises on
        /// a gentle curve from From to To (ease-out over Rise), pushed out of the head's sphere.</summary>
        public static Vector3 PositionAt(in Star st, float t, Vector3 head)
        {
            float u = Mathf.Clamp01((t - st.Birth) / Rise);
            if (u >= 1f) return ClearOfHead(st.To, head);   // settled: exactly where it was placed
            float e = 1f - (1f - u) * (1f - u) * (1f - u);
            Vector3 ctrl = (st.From + st.To) * 0.5f + Vector3.up * 0.2f;
            Vector3 a = Vector3.LerpUnclamped(st.From, ctrl, e), b = Vector3.LerpUnclamped(ctrl, st.To, e);
            return ClearOfHead(Vector3.LerpUnclamped(a, b, e), head);
        }

        /// <summary>A star is drawn (born, not from an earlier sky, not faded) at time t.</summary>
        public bool Shown(int i, float t)
        {
            ref Star st = ref Stars[i];
            return st.Alive && st.Birth >= Epoch && t >= st.Birth && t - st.Death < DieFade && Fade > 0f;
        }
    }
}
