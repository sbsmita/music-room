// The listen-back pulse's numbers (no Unity objects: EditMode tests drive it directly). While a take or a loop plays, every
// note the recorder sends back (PlaybackNoteEvent) lifts its instrument's level, and so does every live note that goes
// into a recording (RecordedNoteEvent: the tape deck's REC or a looper layer recording its pass; 2026-11-11, "while playing
// piano as recording, it should also change lights in the room"). Plain live play sends neither, so it never pulses; a
// sound is announced by one source only (a played-back note is never also a recorded one), so an overdub over a playing
// loop pulses once per note. An onset kicks the level to its velocity with a fast attack (Attack) and it falls back slowly
// (Decay); a continuous lane (a rubbed bowl) holds it at its level (eased over HoldEase) until its release
// (a live lane also lets go LiveHoldTimeout after its last control, in case its release went outside the recording).
// Room = the loudest instrument now; the room's lights, the soft motes, the flowers and the butterflies follow it. A note
// the looper sends ahead of time (When, its audio time) waits in a small ring until it sounds. Stop (the recorder went
// idle) lets go of every hold and drops what waits, so everything falls back to rest on its own and rests exactly at 0
// (Resting). SincePlayback / SinceRecorded / SinceNote count the seconds since the last note of each source (Sunny's
// lines, the butterflies' phrases); Onset(kind) is the loudest onset that sounded in the last Tick.
// Motes: an onset asks for 1 + 2.99 vel motes, a hold for FlowPerSecond x level a second; a shared budget (BudgetPerSecond,
// at most BudgetBurst at once) keeps a drum roll from flooding the room. Allocation-free after construction.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public sealed class ListenBackModel
    {
        public const int Kinds = InstrumentIds.KindCount;
        /// <summary>The envelope: rise time constant, fall time constant, a hold's ease (s), and the level that counts as rest.</summary>
        public const float Attack = 0.02f, Decay = 0.55f, HoldEase = 0.15f, Rest = 0.002f;
        /// <summary>Motes: a held lane's flow at full level (per s), the shared budget (per s) and its most at once.</summary>
        public const float FlowPerSecond = 7f, BudgetPerSecond = 36f, BudgetBurst = 12f;
        public const int PendingMax = 64;
        /// <summary>A live (recorded) lane's hold lets go this long after its last control (s).</summary>
        public const float LiveHoldTimeout = 0.6f;

        struct Pending { public int Kind; public EventRole Role; public float Level; public double When; }

        readonly float[] _env = new float[Kinds], _kick = new float[Kinds], _hold = new float[Kinds], _holdS = new float[Kinds];
        readonly float[] _burst = new float[Kinds], _flow = new float[Kinds];
        readonly float[] _liveHold = new float[Kinds], _liveAge = new float[Kinds];
        readonly float[] _onsetIn = new float[Kinds], _onset = new float[Kinds];
        readonly int[] _motes = new int[Kinds];
        readonly Pending[] _pending = new Pending[PendingMax];
        int _pendingCount;
        float _budget = BudgetBurst;

        /// <summary>The loudest instrument's level now (0..1).</summary>
        public float Room { get; private set; }
        /// <summary>Every level is exactly 0 and nothing waits.</summary>
        public bool Resting { get; private set; } = true;
        /// <summary>Notes taken in (tests, the log): played back and recorded.</summary>
        public int Notes { get; private set; }
        /// <summary>Of Notes, the live ones that went into a recording.</summary>
        public int RecordedNotes { get; private set; }
        /// <summary>Seconds since the last played-back / recorded note (any role but a marker), and since either.</summary>
        public float SincePlayback { get; private set; } = float.PositiveInfinity;
        public float SinceRecorded { get; private set; } = float.PositiveInfinity;
        public float SinceNote => SincePlayback < SinceRecorded ? SincePlayback : SinceRecorded;
        /// <summary>A lane (played back or recorded) is held now.</summary>
        public bool Holding { get; private set; }
        public int PendingCount => _pendingCount;

        public float Level(InstrumentKind k) => Valid(k) ? _env[(int)k] : 0f;
        /// <summary>The motes this frame's Tick released for an instrument (after the budget).</summary>
        public int Motes(InstrumentKind k) => Valid(k) ? _motes[(int)k] : 0;
        /// <summary>The loudest onset of an instrument that sounded in the last Tick (0 when none).</summary>
        public float Onset(InstrumentKind k) => Valid(k) ? _onset[(int)k] : 0f;

        static bool Valid(InstrumentKind k) => (uint)(int)k < (uint)Kinds;

        /// <summary>A note sent back by the playback. now: the audio time; a note for later waits until it sounds.</summary>
        public void Note(in PlaybackNoteEvent e, double now)
        {
            if (!Valid(e.Kind) || e.Role == EventRole.Marker) return;
            Notes++;
            Resting = false;
            SincePlayback = 0f;
            if (e.When > now + 1e-4 && _pendingCount < PendingMax)
            {
                _pending[_pendingCount++] = new Pending { Kind = (int)e.Kind, Role = e.Role, Level = e.Level, When = e.When };
                return;
            }
            Apply((int)e.Kind, e.Role, e.Level);
        }

        /// <summary>A live note that went into a recording: it sounds now (the player just played it).</summary>
        public void Recorded(in RecordedNoteEvent e)
        {
            if (!Valid(e.Kind) || e.Role == EventRole.Marker) return;
            Notes++;
            RecordedNotes++;
            Resting = false;
            SinceRecorded = 0f;
            int k = (int)e.Kind;
            float level = Mathf.Clamp01(e.Level);
            switch (e.Role)
            {
                case EventRole.Control: _liveHold[k] = level; _liveAge[k] = 0f; break;
                case EventRole.Release: _liveHold[k] = 0f; Apply(k, e.Role, level); break;
                default: Apply(k, e.Role, level); break;
            }
        }

        void Apply(int k, EventRole role, float level)
        {
            level = Mathf.Clamp01(level);
            switch (role)
            {
                case EventRole.Onset:
                    if (level > _kick[k]) _kick[k] = level;
                    if (level > _onsetIn[k]) _onsetIn[k] = level;
                    _burst[k] += 1f + 2.99f * level;
                    break;
                case EventRole.Control:
                    _hold[k] = level;
                    break;
                case EventRole.Release:
                    _hold[k] = 0f;   // a lane's off; a key's release changes nothing (an onset is not held here)
                    break;
            }
        }

        /// <summary>The playback stopped: every hold lets go and what waits is dropped; the levels fall back on their own.</summary>
        public void Stop()
        {
            _pendingCount = 0;
            for (int k = 0; k < Kinds; k++) { _hold[k] = 0f; _liveHold[k] = 0f; _burst[k] = 0f; }
        }

        /// <summary>One frame: what is due sounds, the levels move, the motes are counted.</summary>
        public void Tick(float dt, double now)
        {
            float step = dt > 0f ? dt : 0f;
            if (SincePlayback < float.PositiveInfinity) SincePlayback += step;
            if (SinceRecorded < float.PositiveInfinity) SinceRecorded += step;
            if (Resting) { Holding = false; return; }
            for (int i = 0; i < _pendingCount;)
            {
                if (_pending[i].When <= now + 1e-4)
                {
                    Apply(_pending[i].Kind, _pending[i].Role, _pending[i].Level);
                    _pending[i] = _pending[--_pendingCount];
                }
                else i++;
            }
            if (dt < 0f) dt = 0f;
            float up = 1f - Mathf.Exp(-dt / Attack), down = Mathf.Exp(-dt / Decay), ease = 1f - Mathf.Exp(-dt / HoldEase);
            _budget = Mathf.Min(BudgetBurst, _budget + BudgetPerSecond * dt);
            float room = 0f;
            bool any = _pendingCount > 0, holding = false;
            for (int k = 0; k < Kinds; k++)
            {
                _motes[k] = 0;
                _onset[k] = _onsetIn[k];
                _onsetIn[k] = 0f;
                if (_liveHold[k] > 0f && (_liveAge[k] += dt) > LiveHoldTimeout) _liveHold[k] = 0f;
                float hold = _hold[k] > _liveHold[k] ? _hold[k] : _liveHold[k];
                if (hold > 0f) holding = true;
                _holdS[k] += (hold - _holdS[k]) * ease;
                if (hold <= 0f && _holdS[k] < Rest) _holdS[k] = 0f;
                float target = Mathf.Max(_kick[k], _holdS[k]);
                float env = _env[k];
                if (target > env)
                {
                    env += (target - env) * up;
                    if (env >= 0.9f * _kick[k]) _kick[k] = 0f;   // the kick has (nearly) landed: from here it falls
                }
                else
                {
                    _kick[k] = 0f;
                    env = target + (env - target) * down;
                }
                if (env < Rest && target <= 0f) env = 0f;
                _env[k] = env;
                if (env > room) room = env;
                // motes: the onsets' bursts now, a hold's steady flow
                _flow[k] += _holdS[k] * FlowPerSecond * dt;
                float want = _burst[k] + Mathf.Floor(_flow[k]);
                _flow[k] -= Mathf.Floor(_flow[k]);
                _burst[k] = 0f;
                int n = Mathf.Min((int)want, (int)_budget);
                if (n > 0) { _motes[k] = n; _budget -= n; }
                if (env > 0f || hold > 0f || _holdS[k] > 0f) any = true;
            }
            Room = room;
            Holding = holding;
            if (holding) { if (AnyPlaybackHold()) SincePlayback = 0f; if (AnyLiveHold()) SinceRecorded = 0f; }
            if (!any)
            {
                Resting = true;
                for (int k = 0; k < Kinds; k++) { _flow[k] = 0f; _kick[k] = 0f; _onsetIn[k] = 0f; _onset[k] = 0f; }
            }
        }

        bool AnyPlaybackHold() { for (int k = 0; k < Kinds; k++) if (_hold[k] > 0f) return true; return false; }
        bool AnyLiveHold() { for (int k = 0; k < Kinds; k++) if (_liveHold[k] > 0f) return true; return false; }

        // ---------------------------------------------------------------- colour

        /// <summary>The playback glow: one warm orange for every instrument's motes and the lamp's tint (sRGB; the user's choice,
        /// 2026-11-11: "playback glow should be orange color"). Saturated enough to read as orange as additive motes over the
        /// dark VR room and over passthrough, and as the floor lamp's tint.</summary>
        public static readonly Color Glow = new Color(1.00f, 0.50f, 0.10f);

        /// <summary>The colour of an instrument's motes: the playback glow, the same for every instrument.</summary>
        public static Color ColorOf(InstrumentKind k) => Glow;

        /// <summary>The colour of what plays now: the playback glow (white when silent).</summary>
        public Color Blend()
        {
            for (int k = 0; k < Kinds; k++) if (_env[k] > 0f) return Glow;
            return Color.white;
        }
    }
}
