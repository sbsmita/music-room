// Sunny's bowls tips as the platform's TipSpec data (bowls.md 7.2 with review item 12's corrections; BUILD-GUIDE 7). Lines
// are in Resources/GuideLines/Bowls.json, voices in Resources/Voice/tip_bowls_*.wav (eSpeak NG via
// the project's voice pipeline), lengths in
// Resources/VoiceManifests/Bowls.json (the Guide's manifest shape).
// Coverage: tapping (arrive), rubbing (rub, outside, tap_rub: tap, then circle), faster / slower, the stick (puja,
// puja_use), the lip, damping (quiet), palm strikes (palm), where the bowls are (where) and the two lesson tips. The
// counters and filters behind the Signal triggers are BowlsCoach's ("bowls.tapped3" two seconds after the third tap with
// no rub yet, ...); the instrument publishes them as InstrumentSignalEvent { Kind = Bowls }.
// Anchors resolve in BowlsAnchors: "@rubbed" / "@ringing" name the bowl being rubbed / ringing loudest (the big bowl when
// none), bowls.lesson.@target the current lesson target's bowl.
using MusicRoom.Core;

namespace MusicRoom.Bowls
{
    public static class BowlsTips
    {
        public const string Arrive = "tip_bowls_arrive", Rub = "tip_bowls_rub", Lesson = "tip_bowls_lesson", LessonRub = "tip_bowls_lesson_rub";
        /// <summary>The first lesson (strikes) and the rub lesson (pack ids).</summary>
        public const string FirstLessonId = "bowls-first", RubLessonId = "bowls-sing";

        public static FeatureSpec[] Features => new[]
        {
            new FeatureSpec("bowls.tap"), new FeatureSpec("bowls.palm"), new FeatureSpec("bowls.rub"), new FeatureSpec("bowls.rubSpeed"),
            new FeatureSpec("bowls.lip"), new FeatureSpec("bowls.damp"), new FeatureSpec("bowls.puja"), new FeatureSpec("bowls.where"),
            new FeatureSpec("bowls.lessons"),
            new FeatureSpec("bowls.beat", "the slow beating of a ringing bowl is heard, not done"),
            new FeatureSpec("bowls.rock", "a struck bowl rocks on its cushion by itself"),
        };

        static TipSpec T(string id, string title, TipTrigger trigger, string arg, TipClass cls, DemoKind demo, string anchor,
            TipSuccess success, string successArg, params string[] covers) => new TipSpec
        {
            Id = id, Title = title, Lines = new[] { id }, Again = "again", Trigger = trigger, TriggerArg = arg, Class = cls,
            Demo = demo, Anchor = anchor, Success = success, SuccessArg = successArg, Covers = covers,
        };

        public static TipSpec[] All
        {
            get
            {
                var tapRub = T("tip_bowls_tap_rub", "Tap, then circle", TipTrigger.Signal, "bowls.slowStart", TipClass.P3, DemoKind.Tap, "bowl.big.rim", TipSuccess.None, null, "bowls.rub");
                tapRub.ThenDemo = DemoKind.FingerCircle;
                var pujaUse = T("tip_bowls_puja_use", "Using the stick", TipTrigger.Signal, "bowls.pujaPick", TipClass.P3, DemoKind.Tap, "bowl.big.rim", TipSuccess.Signal, "bowls.suedeStrike", "bowls.puja");
                pujaUse.ThenDemo = DemoKind.OpenHand;
                pujaUse.ThenAnchor = "puja.grip";
                return new[]
                {
                    T(Arrive, "Tap a bowl", TipTrigger.FirstArrival, null, TipClass.P2, DemoKind.Tap, "bowl.big.near", TipSuccess.EventType, "bowlStrike", "bowls.tap"),
                    T(Rub, "Make a bowl sing", TipTrigger.Signal, "bowls.tapped3", TipClass.P2, DemoKind.FingerCircle, "bowl.big.rim", TipSuccess.Signal, "bowls.sang", "bowls.rub"),
                    T("tip_bowls_slower", "Slower", TipTrigger.Signal, "bowls.chatter", TipClass.P2, DemoKind.FingerCircle, "bowl.@rubbed.rim", TipSuccess.Signal, "bowls.sang", "bowls.rubSpeed"),
                    T("tip_bowls_faster", "A little faster", TipTrigger.Signal, "bowls.slow", TipClass.P2, DemoKind.FingerCircle, "bowl.@rubbed.rim", TipSuccess.Signal, "bowls.sang", "bowls.rubSpeed"),
                    T("tip_bowls_outside", "The outside", TipTrigger.Signal, "bowls.inner", TipClass.P2, DemoKind.FingerCircle, "bowl.@rubbed.rim", TipSuccess.Signal, "bowls.rubOuter", "bowls.rub"),
                    T("tip_bowls_lip", "On top of the rim", TipTrigger.Signal, "bowls.sang3", TipClass.P3, DemoKind.FingerCircle, "bowl.big.rim", TipSuccess.Signal, "bowls.lipSang", "bowls.lip"),
                    T("tip_bowls_quiet", "Quiet a bowl", TipTrigger.Signal, "bowls.ringing", TipClass.P3, DemoKind.PalmRest, "bowl.@ringing.rim", TipSuccess.Signal, "bowls.palmRest", "bowls.damp"),
                    T("tip_bowls_palm", "A palm strike", TipTrigger.Signal, "bowls.tapped10", TipClass.P3, DemoKind.Slap, "bowl.big.rim", TipSuccess.Signal, "bowls.palmStrike", "bowls.palm"),
                    tapRub,
                    T("tip_bowls_puja", "The bowl stick", TipTrigger.Signal, "bowls.sang2", TipClass.P3, DemoKind.Pinch, "puja.grip", TipSuccess.Signal, "bowls.pujaPick", "bowls.puja"),
                    pujaUse,
                    T("tip_bowls_where", "The singing bowls", TipTrigger.Signal, "bowls.unplayed120", TipClass.P1, DemoKind.Approach, "bowl.big.near", TipSuccess.EventType, "bowlStrike", "bowls.where"),
                    T(Lesson, "Bowls lesson", TipTrigger.LessonStart, FirstLessonId, TipClass.P2, DemoKind.Tap, "bowls.lesson.@target", TipSuccess.LessonHit, null, "bowls.lessons"),
                    T(LessonRub, "Sing in a lesson", TipTrigger.LessonStart, RubLessonId, TipClass.P2, DemoKind.FingerCircle, "bowls.lesson.@target", TipSuccess.LessonHit, null, "bowls.lessons"),
                };
            }
        }

        /// <summary>Every anchor the tips and lessons name (InstrumentDescriptor.Anchors; conformance 13 resolves each).</summary>
        public static readonly string[] Anchors =
        {
            "bowl.big.near", "bowl.middle.near", "bowl.small.near", "bowl.big.rim", "bowl.middle.rim", "bowl.small.rim",
            "bowl.@rubbed.rim", "bowl.@ringing.rim", "puja.grip", "bowls.lesson.0", "bowls.lesson.1", "bowls.lesson.2", "bowls.lesson.@target",
        };
    }
}
