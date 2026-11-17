// The marimba unit's own API (spec 12-new-instruments/marimba.md 6.1; ENGINE-API.md 4.4): the module reaches it as
// audio.ActiveUnit(UnitId.Marimba) as IMarimbaSound once the platform registers the role (phase B). Until then the
// offline harness (MarimbaOffline) implements the same interface, so the module, its tests and the renders all drive one
// main-thread core (MarimbaSound) and one Burst render (MarimbaDsp).
namespace MusicRoom.Audio.Burst
{
    /// <summary>The striking implement (spec 2.4): the app's pair is Medium yarn; Finger is a bare fingertip.</summary>
    public enum MarimbaMallet : byte { Medium = 0, Soft = 1, Hard = 2, Finger = 3 }

    /// <summary>What rests on a bar (spec 2.5): a hand (palm or 2+ fingertips) or a mallet held down (dead stroke, rest).</summary>
    public enum MarimbaDampKind : byte { Hand = 0, Mallet = 1 }

    public interface IMarimbaSound
    {
        /// <summary>A stroke on bar 0..36 (C3..C6). vel 0..1; u along the bar 0 (near end) .. 1 (far end); c across -1..1.
        /// when: as GraphCommand.When (0 = the next block).</summary>
        void Strike(int bar, float vel, float u, float c, MarimbaMallet mallet, double when = 0);
        /// <summary>A hand or a held mallet starts resting on the bar at u (c only matters for a mallet: torsion).</summary>
        void Damp(int bar, MarimbaDampKind kind, float u, float c, double when = 0);
        /// <summary>The rest ends: the bar rings freely again.</summary>
        void Undamp(int bar, double when = 0);
        /// <summary>Every bar fades out over one block (arrange mode, stow).</summary>
        void Silence();
        /// <summary>The bar's block RMS (audio thread, Volatile.Read): visuals and tests.</summary>
        float BarLevel(int bar);
    }

    /// <summary>The unit's constants (marimba-engine.mjs M; spec 2.6).</summary>
    public static class MarimbaConst
    {
        public const int Block = 256, Lanes = 8, GroupsPerBar = 2, Bars = 37, Channels = 3;
        public const int StrikeSlots = 32, DampSlots = 64, Players = 16, Actions = 128, SegMax = 1024, KnockTail = 192;
        public const double MaxContactS = 0.012;
        public const int MaxActiveBase = 24;
        public const double KnockHz = 2200, KnockQ = 1.1, KnockTau = 0.0015, KnockLevel = 0.90;
        public const double HandAlpha = 35, MalletRestAlpha = 60, HandSpan = 0.12;
        /// <summary>Graph ops of the unit's commands (GraphCommand.Op, the unit's own sub-ops).</summary>
        public const byte OpStrike = 1, OpDamp = 2, OpUndamp = 3, OpSilence = 4;
        /// <summary>An onset more than this many samples late is dropped by the engine (CommandFlags.LateDrop, 100 ms).</summary>
        public const int LateDropSamples = 4800;
        /// <summary>The active-bar cap at Budget 1200 and Share 1, scaled by Budget / 1200 x Share and clamped 8..37:
        /// JS Math.round, i.e. floor(x + 0.5).</summary>
        public static int ActiveCap(int budget, float share)
        {
            double x = MaxActiveBase * (double)budget / 1200.0 * share;
            int cap = (int)System.Math.Floor(x + 0.5);
            return cap < 8 ? 8 : cap > Bars ? Bars : cap;
        }
    }
}
