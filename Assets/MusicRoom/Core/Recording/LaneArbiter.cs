// Live play against playback on one voice (platform.md 6.4; the bowls review, item 14): a voice that only one gesture can
// drive at a time (a bowl's single contact slot) is owned by one lane. The live hand (lane 0) takes it
// from a playback lane (a preempt: re-seat from the live gesture); a playback lane never takes it from the live hand (its
// commands are dropped until the voice is free); between two playback lanes the first keeps it. A Release from a lane
// that does not own the voice is ignored. Plain value type: keep one per voice in the instrument (or its unit's state).
namespace MusicRoom.Core
{
    public enum LaneClaim : byte
    {
        /// <summary>Ignore this lane's command (another lane owns the voice).</summary>
        Drop = 0,
        /// <summary>The lane already owns the voice: apply the command.</summary>
        Continue,
        /// <summary>The voice was free: the lane owns it now (start from this command).</summary>
        Take,
        /// <summary>The live hand took the voice from a playback lane: re-seat from the live gesture.</summary>
        Preempt,
    }

    public struct LaneArbiter
    {
        /// <summary>The owning lane, -1 when free.</summary>
        public int Owner;
        public int Preempts, Drops;

        public static LaneArbiter Free => new LaneArbiter { Owner = -1 };
        public bool IsFree => Owner < 0;

        /// <summary>A command (contact, control) of <paramref name="lane"/> arrives for the voice.</summary>
        public LaneClaim Claim(int lane)
        {
            if (Owner < 0) { Owner = lane; return LaneClaim.Take; }
            if (Owner == lane) return LaneClaim.Continue;
            if (lane == PlaybackLanes.Live && Owner != PlaybackLanes.Live) { Owner = lane; Preempts++; return LaneClaim.Preempt; }
            Drops++;
            return LaneClaim.Drop;
        }

        /// <summary>The lane lets go (a release, its lane closed). True when it owned the voice (now free).</summary>
        public bool Release(int lane)
        {
            if (Owner != lane || Owner < 0) return false;
            Owner = -1;
            return true;
        }
    }
}
