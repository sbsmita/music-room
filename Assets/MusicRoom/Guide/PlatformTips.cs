// The platform's own Sunny tips (12-new-instruments/platform.md 8.1; the phase-0 brief): how instruments come to the seat
// and leave it. Each is a voiced line (Resources/GuideLines/Guide.json, Resources/VoiceManifests/Guide.json and
// Resources/Voice/<id>.wav, rendered with the project's eSpeak NG pipeline like tip_arrange) whose words match the real
// controls: the Instruments screen's tiles (poke one to bring it; poke a ticked one to put it back), point and pinch at a
// displayed instrument, the page-turn swipe in One alone, and the three-instrument limit of My set.
// Signals: the Menu publishes InstrumentSignalEvent{Kind = Table} "menu.instruments" when the Instruments screen opens and
// "menu.instruments.set" when it opens in My set with 2 or more members; Layout publishes, with the instrument's Kind,
// "layout.bring.menu" | "layout.bring.ray" | "layout.bring.pageturn" | "layout.bring.lesson" | "layout.bring.sunny" |
// "layout.replaced" | "layout.putaway", and {Kind = Table} "layout.single" when One alone is chosen.
using MusicRoom.Core;

namespace MusicRoom.Guide
{
    public sealed class PlatformTips : IGuideTipSource
    {
        public const string BringGrid = "tip_bring_grid", SummonFar = "tip_summon_far", MySet = "tip_my_set",
            SetFull = "tip_set_full", PutAway = "tip_put_away", PageTurn = "tip_page_turn";

        /// <summary>The demo anchor a gaze tip resolves itself: the gazed displayed instrument's pick point
        /// (ILayoutService.TryGetDisplayPoint).</summary>
        public const string DisplayAnchor = "display";

        public const string SignalMenuInstruments = "menu.instruments", SignalMenuSet = "menu.instruments.set";
        public const string SignalBringMenu = "layout.bring.menu", SignalBringRay = "layout.bring.ray", SignalBringPageTurn = "layout.bring.pageturn";
        public const string SignalBringLesson = "layout.bring.lesson", SignalBringSunny = "layout.bring.sunny";
        public const string SignalReplaced = "layout.replaced", SignalPutAway = "layout.putaway", SignalSingle = "layout.single";

        public const string FeatureBringMenu = "platform.bring.menu", FeatureBringRay = "platform.bring.ray",
            FeatureBringPageTurn = "platform.bring.pageturn", FeatureMySet = "platform.myset", FeaturePutAway = "platform.putaway";

        public TipSpec[] Tips() => new[]
        {
            new TipSpec
            {
                Id = BringGrid, Title = "Bring an instrument from the menu", Lines = new[] { BringGrid }, Class = TipClass.P2,
                Trigger = TipTrigger.Signal, TriggerArg = SignalMenuInstruments, Demo = DemoKind.Poke,
                Success = TipSuccess.Signal, SuccessArg = SignalBringMenu, Covers = new[] { FeatureBringMenu },
            },
            new TipSpec
            {
                Id = SummonFar, Title = "Bring one by pointing", Lines = new[] { SummonFar }, Class = TipClass.P3, VrOnly = true,
                Trigger = TipTrigger.FirstDisplayedGaze, TriggerArg = "", Demo = DemoKind.Point, Anchor = DisplayAnchor,
                Success = TipSuccess.Signal, SuccessArg = SignalBringRay, Covers = new[] { FeatureBringRay },
            },
            new TipSpec
            {
                Id = MySet, Title = "Choose your set", Lines = new[] { MySet }, Class = TipClass.P3,
                Trigger = TipTrigger.Signal, TriggerArg = SignalBringRay, Demo = DemoKind.Palm,
                Success = TipSuccess.None, Covers = new[] { FeatureMySet },
            },
            new TipSpec
            {
                Id = SetFull, Title = "Three at a time", Lines = new[] { SetFull }, Class = TipClass.P2,
                Trigger = TipTrigger.Signal, TriggerArg = SignalReplaced, Demo = DemoKind.None,
                Success = TipSuccess.None, Covers = new[] { FeatureMySet },
            },
            new TipSpec
            {
                Id = PutAway, Title = "Put one back", Lines = new[] { PutAway }, Class = TipClass.P3,
                Trigger = TipTrigger.Signal, TriggerArg = SignalMenuSet, Demo = DemoKind.Poke,
                Success = TipSuccess.Signal, SuccessArg = SignalPutAway, Covers = new[] { FeaturePutAway },
            },
            new TipSpec
            {
                Id = PageTurn, Title = "The next instrument", Lines = new[] { PageTurn }, Class = TipClass.P3,
                Trigger = TipTrigger.Signal, TriggerArg = SignalSingle, Demo = DemoKind.Swipe,
                Success = TipSuccess.Signal, SuccessArg = SignalBringPageTurn, Covers = new[] { FeatureBringPageTurn },
            },
        };

        public FeatureSpec[] Features() => new FeatureSpec[]
        {
            FeatureBringMenu, FeatureBringRay, FeatureBringPageTurn, FeatureMySet, FeaturePutAway,
        };
    }
}
