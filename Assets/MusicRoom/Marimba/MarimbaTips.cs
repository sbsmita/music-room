// Sunny on the marimba (spec 8; platform 8.1, BUILD-GUIDE 7): the descriptor's TipSpecs and feature ids. Every feature is
// covered by a tip; lines are <= 10 words and <= 3.8 s (review item 18), voiced with the project's eSpeak pipeline
// into Resources/Voice, Resources/VoiceManifests/Marimba.json and
// Resources/GuideLines/Marimba.json. The wording matches the real controls (MarimbaPlay, MarimbaMallets). The counters behind
// the Signal triggers (marimba.noRoll, .noSharps, .ringing) are MarimbaInstrument's; the dead-stroke tip is Manual and
// turns dead strokes on when Sunny says it.
using MusicRoom.Core;

namespace MusicRoom.Marimba
{
    public static class MarimbaTips
    {
        /// <summary>The marimba's feature ids (InstrumentDescriptor.Features).</summary>
        public static readonly string[] Features =
        {
            "marimba.play", "marimba.mallets", "marimba.fingertips", "marimba.release", "marimba.position", "marimba.roll",
            "marimba.deadStroke", "marimba.handMute", "marimba.malletRest", "marimba.sharps", "marimba.lessons",
        };

        /// <summary>The anchor ids the tips and lessons name (InstrumentDescriptor.Anchors).</summary>
        public static readonly string[] Anchors = { "tray.left", "tray.right", "bars.middle", "bar.C4", "bar.E4", "bar.G4", "bar.F#4.tip" };

        /// <summary>The setting tip_marimba_dead turns on when it is shown (spec 5.4).</summary>
        public const string DeadStrokeTip = "tip_marimba_dead";

        static TipSpec T(string id, string title, TipTrigger trigger, string triggerArg, TipClass cls, DemoKind demo, string anchor,
            TipSuccess success, string successArg, params string[] covers) => new TipSpec
        {
            Id = id, Title = title, Lines = new[] { id }, Trigger = trigger, TriggerArg = triggerArg, Class = cls, Demo = demo,
            Anchor = anchor, Success = success, SuccessArg = successArg, Covers = covers,
        };

        public static TipSpec[] Specs() => new[]
        {
            T("tip_marimba_arrive", "Pinch a mallet to pick it up.", TipTrigger.FirstArrival, null, TipClass.P2, DemoKind.Pinch, "tray.right",
                TipSuccess.EventType, MarimbaEvents.Strike, "marimba.play", "marimba.mallets"),
            T("tip_marimba_fingers", "Fingertips play the bars too, a little softer.", TipTrigger.Signal, "marimba.release", TipClass.P3, DemoKind.Tap, "bar.E4",
                TipSuccess.EventType, MarimbaEvents.Strike, "marimba.fingertips"),
            T("tip_marimba_mallets", "Mallets sound fuller. Pinch one from the tray.", TipTrigger.Signal, "marimba.fingerOnly", TipClass.P3, DemoKind.Pinch, "tray.right",
                TipSuccess.Signal, "marimba.pickup", "marimba.mallets"),
            T("tip_marimba_drop", "Open your hand to put a mallet back.", TipTrigger.Signal, "marimba.pickup", TipClass.P2, DemoKind.OpenHand, "tray.right",
                TipSuccess.Signal, "marimba.release", "marimba.release"),
            T("tip_marimba_centre", "Strike near the middle of a bar for warmth.", TipTrigger.Signal, "marimba.offCentre", TipClass.P3, DemoKind.MalletStrike, "bar.C4",
                TipSuccess.None, null, "marimba.position"),
            T("tip_marimba_roll", "For long notes, alternate mallets quickly on one bar.", TipTrigger.Signal, "marimba.noRoll", TipClass.P3, DemoKind.Roll, "bar.G4",
                TipSuccess.Signal, "marimba.roll", "marimba.roll"),
            T(DeadStrokeTip, "Strike, then hold the mallet down for a short note.", TipTrigger.Manual, null, TipClass.P3, DemoKind.MalletHold, "bar.E4",
                TipSuccess.Signal, "marimba.deadStroke", "marimba.deadStroke"),
            T("tip_marimba_damp", "Quiet a bar: rest a hand or mallet on it.", TipTrigger.Signal, "marimba.ringing", TipClass.P3, DemoKind.PalmRest, "bars.middle",
                TipSuccess.EventType, MarimbaEvents.Damp, "marimba.handMute", "marimba.malletRest"),
            T("tip_marimba_back", "Sharps are the back row. Strike their nearest tip.", TipTrigger.Signal, "marimba.noSharps", TipClass.P3, DemoKind.MalletStrike, "bar.F#4.tip",
                TipSuccess.Signal, "marimba.backRow", "marimba.sharps"),
            T("tip_marimba_lesson", "Strike each bar as its block lands.", TipTrigger.LessonStart, null, TipClass.P2, DemoKind.MalletStrike, "bars.middle",
                TipSuccess.LessonHit, null, "marimba.lessons"),
        };
    }
}
