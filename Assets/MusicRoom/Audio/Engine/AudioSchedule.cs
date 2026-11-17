// The ambient "schedule at" scope (ENGINE-API.md section 10). A caller that knows WHEN a sound must start (the looper:
// loop events dispatched ~80 ms ahead on the audio clock) opens a scope around an ordinary instrument call, and every
// command the instrument issues inside it that would play "as soon as possible" plays at the scope's time instead,
// sample-accurately, on either sound path (today's synths or a Burst sound unit). No instrument signature changes:
//
//   using (audio.ScheduleAt(when)) piano.PlaybackOn(midi, vel);     // the key's voice starts at `when`, to the sample
//
// Rules (main thread only, like every other engine call):
// - Inside a scope at T > 0: a When of 0 (ASAP) becomes T; a relative When (< 0, "-s after the batch") becomes T + s;
//   an absolute When (> 0) is kept as the caller computed it.
// - Applied in three places, so nothing an instrument (or a sound unit's own extra API) stages escapes it:
//   AudioEngine's instrument calls (Piano, Release, Pluck, Damp, Drum, Choke: the stamped `when` is what the unit or the
//   synth receives and what AudioTestTap.Calls records), SynthKit.Stage (today's instrument sources PianoLo..Ride only:
//   Sunny, the UI and the ambience are never stamped), SoundUnit.Stage (every role but the room) and AudioGraphHost.Stage
//   (banks and one-shots).
// - Scopes nest (the inner one wins) and Dispose restores the outer one. A scope with when <= 0 changes nothing.
// - Allocation-free: Scope is a struct; `using` on it never boxes.
namespace MusicRoom.Audio
{
    public static class AudioSchedule
    {
        static double s_at;
        static int s_depth;

        /// <summary>A scope is open (main thread).</summary>
        public static bool Active => s_depth > 0;

        /// <summary>The open scope's time (absolute audio seconds), 0 when none.</summary>
        public static double At => s_depth > 0 ? s_at : 0.0;

        /// <summary>Opens a scope at `when` (absolute audio seconds, the engine clock). when &lt;= 0: a scope that changes nothing.</summary>
        public static Scope Begin(double when)
        {
            var s = new Scope(s_at, s_depth, when > 0);
            if (when > 0)
            {
                s_at = when;
                s_depth++;
            }
            return s;
        }

        /// <summary>The engine rule for a command's When inside the open scope (identity outside one).</summary>
        public static double Stamp(double when)
        {
            if (s_depth == 0) return when;
            if (when == 0) return s_at;
            if (when < 0) return s_at - when;
            return when;
        }

        /// <summary>The handle `using` disposes: restores the enclosing scope.</summary>
        public readonly struct Scope : System.IDisposable
        {
            readonly double _prevAt;
            readonly int _prevDepth;
            readonly bool _open;

            internal Scope(double prevAt, int prevDepth, bool open)
            {
                _prevAt = prevAt;
                _prevDepth = prevDepth;
                _open = open;
            }

            public void Dispose()
            {
                if (!_open) return;
                s_at = _prevAt;
                s_depth = _prevDepth;
            }
        }

        /// <summary>Tests and recovery after an exception thrown inside a scope: close every scope.</summary>
        public static void Reset()
        {
            s_at = 0;
            s_depth = 0;
        }
    }
}
