// Sunny's tips from data in the booted Guide (platform.md 8; the phase-0 brief), the events and signals published by
// hand (Layout and the Menu publish them in the integrated app): an instrument's caption-only arrival tip on its first
// Arrived, once, and learned by playing it (even before it was shown); a signal tip with its success signal; P3 tips not in
// the first 90 s after the tour and 60 s apart; the gaze tip pointing at the displayed instrument in a headset only; a
// manual tip through IGuide.Tip; nothing over the tour; LessonStart / LessonHit / Facing on synthetic tips. Guide alone
// with a layout and tutorial probe, manual frames (the voice is the Audio module's: lines here are captions).
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class GuideTipsPlayTests
    {
        const float Dt = 1f / 72f;
        /// The app clamps a frame to Clock.MaxDt (0.05 s): long waits step at that.
        const float Long = Clock.MaxDt;
        TestBoot _boot;
        TipProbe _probe;
        GuideModule _guide;

        /// The layout (display points, places) and a tutorial as the Guide reads them (Order 45, before the Guide).
        sealed class TipProbe : AppModule, ILayoutService, ITutorial
        {
            App _app;
            public override int Order => 45;
            public override void Init(App app) { _app = app; app.Register<ILayoutService>(this); app.Register<ITutorial>(this); }
            public bool Display;
            public Vector3 DisplayPoint;
            // ILayoutService
            public LayoutMode Mode => LayoutMode.All;
            public InstrumentKind Current => default;
            public bool IsTurning => false;
            public bool FirstArrangeDone => true;
            public SeatFrame Seat => default;
            public void Set(LayoutMode mode, InstrumentKind instrument) { }
            public void Next(int direction) { }
            public void ArrangeAroundPlayer(bool force, bool movePiano = true) { }
            public void PlaceAroundPiano() { }
            public void RestoreHomes() { }
            public Pose Anchor => Pose.identity;
            public bool IsInScene(InstrumentKind instrument) => true;
            public bool TryGetDisplayPoint(InstrumentKind instrument, out Vector3 world)
            {
                world = DisplayPoint;
                return Display && instrument == InstrumentKind.Handpan;
            }
            // ITutorial
            public TutorialState State { get; set; }
            public string LessonId { get; set; }
            public InstrumentKind Instrument { get; set; } = InstrumentKind.Handpan;
            public int Version { get; set; }
            public float Countdown => 0f;
            public string Hint => "";
            public float Progress => 0f;
            public int EventsDone { get; set; }
            public int EventsTotal => 6;
            public TutorialSummary Summary => default;
            public void Start(string lessonId) { }
            public void Stop() { }
            public void Publish() => _app.Events.Publish(new TutorialEvent { State = State, LessonId = LessonId, Version = ++Version });
        }

        IEnumerator Boot(bool presenting = false)
        {
            _boot = new TestBoot(headless: true, simulateHeadset: presenting, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Options.ModuleFilter = t => t == typeof(GuideModule);
            _probe = new TipProbe();
            _boot.Options.ExtraModules.Add(_probe);
            yield return _boot.Start();
            _guide = App.Current.Get<IGuide>() as GuideModule;
            Assert.That(_guide, Is.Not.Null);
            Assert.That(_guide.Tips.Entries.Count, Is.GreaterThanOrEqualTo(11), "five arrival tips and the platform's six");
            Assert.That(_guide.Tips.TourEnd, Is.EqualTo(0f), "the tour was done on an earlier launch");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GuideModule.ClassicTour = false;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void Step(float seconds, float dt = Dt)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
            for (int i = 0; i < n; i++) _boot.Step(dt);
        }

        void Signal(InstrumentKind kind, string signal) => App.Current.Events.Publish(new InstrumentSignalEvent { Kind = kind, Signal = signal });
        void Arrived(InstrumentKind kind) => App.Current.Events.Publish(new InstrumentMoveEvent { Kind = kind, Phase = MovePhase.Arrived, Reason = BringReason.Menu });
        string Seen => _boot.Settings.GetString(GuideModule.KeyTipsSeen, "");

        bool UntilGone(float max = 12f)
        {
            for (float t = 0f; t < max && _guide.Sunny.Visible; t += Dt) _boot.Step(Dt);
            return !_guide.Sunny.Visible;
        }

        [UnityTest]
        public IEnumerator AnArrivalTipIsACaptionOnceAndLearnedByPlaying()
        {
            yield return Boot();
            Step(0.5f);
            Assert.IsFalse(_guide.Present);
            InstrumentDescriptor handpan = App.Current.Registry.Get(InstrumentKind.Handpan);
            TipSpec tip = handpan.Tips[0];
            Arrived(InstrumentKind.Handpan);
            Step(0.1f);
            Assert.IsTrue(_guide.Sunny.InTip, "Sunny comes by on the first arrival");
            Assert.That(_guide.ActiveTip, Is.EqualTo(tip.Id));
            Assert.That(_guide.Sunny.Line, Is.EqualTo(tip.Title), "TextOnly: the Title is the caption");
            Assert.IsTrue(UntilGone(), "it leaves silently");
            Assert.IsTrue(_guide.TipSeen(tip.Id), "heard: seen");
            Assert.That(Seen, Does.Contain(tip.Id), "persisted in musicroom.tipsSeen");
            int shown = _guide.TipsShown;
            Arrived(InstrumentKind.Handpan);
            Step(1f);
            Assert.That(_guide.TipsShown, Is.EqualTo(shown), "once, ever");
            // Playing an instrument before its tip came teaches it: the bowls' tip is learned, never shown.
            InstrumentDescriptor bowls = App.Current.Registry.Get(InstrumentKind.Bowls);
            App.Current.Events.Publish(new RecorderEvent { Event = new TakeEvent { type = bowls.Events[0].Type, i = 1, vel = 0.6f }, DuringPlayback = true });
            Assert.IsFalse(_guide.TipSeen(bowls.Tips[0].Id), "playback never counts");
            App.Current.Events.Publish(new RecorderEvent { Event = new TakeEvent { type = bowls.Events[0].Type, i = 1, vel = 0.6f } });
            Assert.IsTrue(_guide.TipSeen(bowls.Tips[0].Id), "a live onset of its own instrument");
            Arrived(InstrumentKind.Bowls);
            Step(1f);
            Assert.That(_guide.TipsShown, Is.EqualTo(shown));
        }

        [UnityTest]
        public IEnumerator ASignalTipIsSaidAndItsSuccessSignalLearnsIt()
        {
            yield return Boot();
            Step(0.2f);
            Signal(InstrumentKind.Table, PlatformTips.SignalMenuInstruments);
            Step(0.1f);
            Assert.That(_guide.ActiveTip, Is.EqualTo(PlatformTips.BringGrid), "P2: when the Instruments screen opens");
            Assert.That(_guide.Sunny.Line, Is.EqualTo("Poke an instrument in Instruments to bring it here."));
            Assert.IsFalse(_guide.TipSeen(PlatformTips.BringGrid));
            Signal(InstrumentKind.Handpan, PlatformTips.SignalBringMenu);
            Assert.IsTrue(_guide.TipSeen(PlatformTips.BringGrid), "brought from the menu: learned at once");
            Assert.IsTrue(UntilGone());
            Signal(InstrumentKind.Table, PlatformTips.SignalMenuInstruments);
            Step(1f);
            Assert.IsFalse(_guide.Present, "never again");
            // P2 tips are spaced 10 s apart: "Only three fit" right after another tip waits for its turn.
            Assert.That(_guide.Tip(PlatformTips.SetFull), Is.GreaterThan(0f), "a manual tip (IGuide.Tip with a TipSpec id) says it now");
            Assert.That(_guide.ActiveTip, Is.EqualTo(PlatformTips.SetFull));
            Assert.That(_guide.Sunny.Line, Is.EqualTo("Only three fit. The least played one goes back."));
        }

        [UnityTest]
        public IEnumerator P3TipsWaitNinetySecondsAfterTheTourAndSixtyBetween()
        {
            yield return Boot();
            Step(5f, Long);
            Signal(InstrumentKind.Table, PlatformTips.SignalSingle);
            Step(1f);
            Assert.IsFalse(_guide.Present, "P3: not in the first 90 s after the tour");
            Step(PlatformTipsWait(), Long);
            Assert.IsFalse(_guide.TipSeen(PlatformTips.PageTurn), "dropped, not seen");
            Step(90f, Long);
            Signal(InstrumentKind.Table, PlatformTips.SignalSingle);
            Step(0.1f);
            Assert.That(_guide.ActiveTip, Is.EqualTo(PlatformTips.PageTurn), "after 90 s it comes");
            Assert.That(_guide.Sunny.Line, Is.EqualTo("Swipe your hand sideways for the next instrument."));
            Assert.IsTrue(UntilGone());
            Assert.IsTrue(_guide.TipSeen(PlatformTips.PageTurn));
            Signal(InstrumentKind.Table, PlatformTips.SignalMenuSet);
            Step(PlatformTipsWait() + 1f, Long);
            Assert.IsFalse(_guide.TipSeen(PlatformTips.PutAway), "a second P3 within 60 s waits, then is dropped");
            Step(40f, Long);
            Signal(InstrumentKind.Table, PlatformTips.SignalMenuSet);
            Step(0.1f);
            Assert.That(_guide.ActiveTip, Is.EqualTo(PlatformTips.PutAway), "60 s later it may come");
            Signal(InstrumentKind.Handpan, PlatformTips.SignalPutAway);
            Assert.IsTrue(_guide.TipSeen(PlatformTips.PutAway));
        }

        static float PlatformTipsWait() => TipRunner.WaitP3 + 0.5f;

        [UnityTest]
        public IEnumerator TheGazeTipPointsAtTheDisplayedInstrumentInAHeadsetOnly()
        {
            yield return Boot(presenting: true);
            Assert.IsTrue(_guide.IsPresenting);
            Transform head = App.Current.Head;
            _probe.DisplayPoint = head.position + head.forward * 3f + Vector3.up * 0.1f;
            _probe.Display = true;
            Step(10f, Long);
            Assert.IsFalse(_guide.Present, "within 90 s of the tour: no gaze tip");
            Step(85f, Long);
            for (float t = 0f; t < 3.5f && _guide.ActiveTip == null; t += Dt) _boot.Step(Dt);
            Assert.That(_guide.ActiveTip, Is.EqualTo(PlatformTips.SummonFar), "3 s looking at a displayed instrument");
            Assert.That(_guide.Sunny.Line, Is.EqualTo("Point at an instrument and pinch to bring it here."));
            Assert.That(_guide.Sunny.DemoKind, Is.EqualTo(DemoKind.Point));
            Assert.IsTrue(_guide.Sunny.TipDemoAnchored, "the demo points at the instrument");
            Assert.That(Vector3.Distance(_guide.Sunny.TipAnchor, _probe.DisplayPoint), Is.LessThan(1e-4f), "anchor \"display\": its pick point");
            Step(0.2f);
            Assert.IsTrue(_guide.Sunny.Demo.Visible && _guide.Sunny.Demo.Wide);
            Signal(InstrumentKind.Handpan, PlatformTips.SignalBringRay);
            Assert.IsTrue(_guide.TipSeen(PlatformTips.SummonFar), "a ray summon learns it");
            Assert.IsTrue(UntilGone());
        }

        [UnityTest]
        public IEnumerator OnTheDesktopTheGazeTipNeverComesAndNoTipInterruptsTheTour()
        {
            // The classic five-step tour, which lasts until dismissed here: the first five's replay in a Guide-only boot
            // has no instruments to show and says goodbye after 5 s (TourTipsTests check its tips against the first run).
            GuideModule.ClassicTour = true;
            yield return Boot(presenting: false);
            Transform head = App.Current.Head;
            _probe.DisplayPoint = head.position + head.forward * 3f;
            _probe.Display = true;
            Step(100f, Long);
            Assert.IsFalse(_guide.Present, "VR only");
            _guide.Start();
            Step(0.5f);
            Assert.IsTrue(_guide.Present && !_guide.Sunny.InTip, "the tour runs");
            Signal(InstrumentKind.Table, PlatformTips.SignalMenuInstruments);
            Step(1f);
            Assert.That(_guide.ActiveTip, Is.Null, "never over the tour");
            Assert.IsFalse(_guide.Sunny.InTip);
            Step(TipRunner.WaitP2 + 0.5f, Long);
            _guide.Dismiss();
            Assert.IsTrue(UntilGone());
            Assert.IsFalse(_guide.TipSeen(PlatformTips.BringGrid), "it waited its 6 s and was dropped, unseen");
            Signal(InstrumentKind.Table, PlatformTips.SignalMenuInstruments);
            Step(0.2f);
            Assert.That(_guide.ActiveTip, Is.EqualTo(PlatformTips.BringGrid), "the next time it comes");
        }

        [UnityTest]
        public IEnumerator LessonStartLessonHitAndFacingWorkOnAnInstrumentsTips()
        {
            yield return Boot();
            InstrumentDescriptor handpan = App.Current.Registry.Get(InstrumentKind.Handpan);
            InstrumentDescriptor marimba = App.Current.Registry.Get(InstrumentKind.Marimba);
            TipEntry lesson = _guide.Tips.Add(new TipSpec
            {
                Id = "tip_handpan_test_lesson", Title = "Wait for the block, then tap.", TextOnly = true, Class = TipClass.P2,
                Trigger = TipTrigger.LessonStart, Success = TipSuccess.LessonHit,
            }, handpan);
            TipEntry facing = _guide.Tips.Add(new TipSpec
            {
                Id = "tip_marimba_test_face", Title = "Face the marimba to play it.", TextOnly = true, Class = TipClass.P2,
                Trigger = TipTrigger.Manual, Success = TipSuccess.Facing,
            }, marimba);
            Assert.That(lesson, Is.Not.Null); Assert.That(facing, Is.Not.Null);
            _probe.LessonId = "handpan-first";
            _probe.State = TutorialState.Countdown;
            _probe.Publish();
            Step(0.1f);
            Assert.That(_guide.ActiveTip, Is.EqualTo("tip_handpan_test_lesson"), "the instrument's lesson started");
            Assert.That(_guide.Sunny.Line, Is.EqualTo("Wait for the block, then tap."));
            _probe.State = TutorialState.Playing;
            _probe.Publish();
            Assert.IsFalse(_guide.TipSeen("tip_handpan_test_lesson"), "no hit yet");
            _probe.EventsDone = 1;
            _probe.State = TutorialState.Waiting;
            _probe.Publish();
            Assert.IsTrue(_guide.TipSeen("tip_handpan_test_lesson"), "a lesson target hit learns it");
            App.Current.Events.Publish(new FocusEvent { Kind = InstrumentKind.Handpan, Has = true });
            Assert.IsFalse(_guide.TipSeen("tip_marimba_test_face"), "facing another instrument");
            App.Current.Events.Publish(new FocusEvent { Kind = InstrumentKind.Marimba, Has = true });
            Assert.IsTrue(_guide.TipSeen("tip_marimba_test_face"), "facing it");
        }

        /// <summary>Bowls R5 (integration): the lesson re-demo shows the running lesson's LessonStart tip demo when the
        /// instrument has one (the bowls' rub lesson circles a finger), else its arrival demo (a tap for a lesson of strikes).</summary>
        [UnityTest]
        public IEnumerator TheLessonReDemoShowsTheRunningLessonsOwnDemo()
        {
            yield return Boot();
            int checkedTips = 0;
            foreach (TipEntry e in _guide.Tips.Entries)
            {
                if (e.Owner == null || e.Spec.Trigger != TipTrigger.LessonStart || string.IsNullOrEmpty(e.Spec.TriggerArg)) continue;
                DemoKind d = e.Spec.Demo;
                if (d == DemoKind.None || d == DemoKind.Custom || d == DemoKind.Nod || d == DemoKind.Press || SunnyDemo.IsTourKind(d)) continue;
                Assert.That(_guide.Tips.RedemoKind(e.Owner.Kind, e.Spec.TriggerArg), Is.EqualTo(d), e.Spec.Id + ": its lesson's re-demo");
                checkedTips++;
            }
            Assert.That(checkedTips, Is.GreaterThan(0), "at least one instrument has a lesson tip with its own demo");
            Assert.That(_guide.Tips.RedemoKind(InstrumentKind.Bowls, "bowls-sing"), Is.EqualTo(DemoKind.FingerCircle), "the bowls' rub lesson");
            Assert.That(_guide.Tips.RedemoKind(InstrumentKind.Bowls, "no-such-lesson"), Is.EqualTo(_guide.Tips.RedemoKind(InstrumentKind.Bowls)), "another lesson: the arrival demo");
        }
    }
}
