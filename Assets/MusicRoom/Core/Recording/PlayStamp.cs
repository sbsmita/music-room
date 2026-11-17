// Stamping a live play on the hand that made it (platform.md 6.1): LastPlayMs (the grab guards, "not within 1 s of
// playing") and LastInstrument (the contactless rule: a hand that played another instrument in the last 0.4 s never plays
// a contactless instrument). Instruments call it for every live play, next to IRecorder.Add.
namespace MusicRoom.Core
{
    public static class PlayStamp
    {
        /// <summary>The contactless guard (platform.md 4.6): 0.4 s.</summary>
        public const double OtherInstrumentGuardMs = 400.0;

        /// <summary>The hand played <paramref name="kind"/> at nowMs (Clock.NowMs).</summary>
        public static void Mark(HandState hand, InstrumentKind kind, double nowMs)
        {
            if (hand == null) return;
            hand.LastPlayMs = nowMs;
            hand.LastInstrument = kind;
        }

        /// <summary>As Mark(hand, kind, nowMs) with App.Current's clock.</summary>
        public static void Mark(HandState hand, InstrumentKind kind)
        {
            App app = App.Current;
            Mark(hand, kind, app != null ? app.Clock.NowMs : 0.0);
        }

        /// <summary>As Mark for any pointer owner: a hand directly, a tool through IHandToolOwner (its holder).</summary>
        public static void Mark(IPointerOwner owner, InstrumentKind kind, double nowMs)
        {
            if (owner == null) return;
            owner.LastPlayMs = nowMs;
            if (owner is HandState h) h.LastInstrument = kind;
            else if (owner is IHeldBy held && held.Holder != null) { held.Holder.LastPlayMs = nowMs; held.Holder.LastInstrument = kind; }
        }

        /// <summary>True when the hand played an instrument other than <paramref name="self"/> within <paramref name="ms"/>.</summary>
        public static bool PlayedOtherWithin(HandState hand, InstrumentKind self, double nowMs, double ms = OtherInstrumentGuardMs)
            => hand != null && hand.LastInstrument != self && hand.LastInstrument != InstrumentKind.Table && nowMs - hand.LastPlayMs < ms;
    }

    /// <summary>A tool pointer's owner that is held by a hand (a stick, a mallet): PlayStamp stamps the holder too.</summary>
    public interface IHeldBy
    {
        HandState Holder { get; }
    }
}
