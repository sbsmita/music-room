// Sunny's contextual tips in the running app (Docs/11-first-five/sunny-tour.md 3.1): a tip comes in context once,
// with its ghost hand at the target, and is remembered when learned; the Tips list plays any tip at once; doing it yourself
// counts; "Tips while I play" off keeps the discovery tips quiet; the first tip ever adds "poke me anytime to skip"; the
// room's celebration is calm and puts the room back; the Done / Reset buttons stop the finger on their face.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using MusicRoom.Menu;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class TourTipsTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        MenuModule _menu;
        GuideModule _guide;
        double _t;
        readonly List<string> _lines = new List<string>();
        int _version = -1;

        const string PlatformTipBringGrid = "tip_bring_grid";

        IEnumerator Boot(bool tips = true, string seen = null)
        {
            _t = 30.0;
            _lines.Clear();
            TourTipRunner.ForceEnabled = tips;
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetInt(FirstRun.KeyTourVersion, FirstRun.TourVersion);
            if (seen != null) _boot.Settings.SetString(GuideModule.KeyTipsSeen, seen);
            _boot.Hands.SetPose(HandSide.Right, Rest());
            _boot.Hands.SetTracked(HandSide.Left, false);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            foreach (var m in _app.Modules)
            {
                if (m is MenuModule mm) _menu = mm;
                if (m is GuideModule g) _guide = g;
            }
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TourTipRunner.ForceEnabled = false;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        Sunny S => _guide.Sunny;
        TourTipRunner Tips => _guide.TourTips;

        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                _t += Dt;
                _boot.Step(Dt);
                if (S.LineVersion != _version) { _version = S.LineVersion; if (S.BubbleVisible) _lines.Add(S.LineId.Length > 0 ? S.LineId : S.Line); }
            }
        }

        void StepUntil(Func<bool> done, float seconds, string what)
        {
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n; i++) { if (done()) return; Step(); }
            Assert.IsTrue(done(), $"{what}: not within {seconds} s (tip {Tips.ShowingId}, sunny {S.State}, line {S.LineId}, time {Tips.Signals.Time:F1}, last play {Tips.Signals.LastLivePlay:F1}, last poke {Tips.Signals.LastMenuPoke:F1}, menu {Tips.Signals.MenuShown})");
        }

        static HandPose Rest() =>
            HandPose.Open(HandSide.Right, new Vector3(0.45f, 1.7f, -0.6f), Quaternion.LookRotation(Vector3.forward, Vector3.up)).WithPointing();

        static HandPose Finger(Vector3 tip, Vector3 along) =>
            HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(along, Vector3.up)).WithPointing().WithIndexTipAt(tip);

        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        void PokeRow(Func<MenuRow, bool> pick)
        {
            MenuController c = _menu.Controller;
            MenuRow row = null;
            for (int i = 0; i < c.RowCount; i++) if (pick(c.Row(i))) row = c.Row(i);
            Assert.That(row, Is.Not.Null, "no such row on " + c.Screen);
            Transform root = _menu.Root;
            Step(20);
            foreach (float z in new[] { 0.03f, 0.012f, -0.002f, 0.001f, 0.03f })
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(root.TransformPoint(MenuMath.CanvasPoint((row.X0 + row.X1) / 2f, (row.Y0 + row.Y1) / 2f, z)), root.forward));
                Step();
            }
            _boot.Hands.SetPose(HandSide.Right, Rest());
            Step(2);
        }

        void PokeSunny()
        {
            Vector3 c = S.Position, far = c + Vector3.up * 0.2f + Vector3.left * 0.1f;
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step(); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(S.Position)); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step();
            _boot.Hands.SetPose(HandSide.Right, Rest()); Step(45);
        }

        // =====================================================================================================

        [UnityTest]
        public IEnumerator TheLessonsScreenBringsItsTipOnceWithTheGhostFingerOnThePianoRow()
        {
            yield return Boot();
            Assert.IsFalse(_guide.FirstRun.Running);
            _menu.Show();
            Step(5);
            _menu.Go(MenuScreen.Lessons);
            StepUntil(() => Tips.ShowingId == "tip_lessons", 3f, "the Lessons tip");
            Assert.That(S.State, Is.EqualTo(SunnyState.Script));
            Assert.That(S.Owner, Is.SameAs(Tips));
            StepUntil(() => S.LineId == "tip_lessons", 2f, "its line");
            Step(10);
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Press));
            Assert.IsTrue(_app.Get<IMenuGuideHooks>().TryGetRowRect("inst.piano", out Vector3 row, out _, out _));
            Assert.That(Vector3.Distance(S.Anchor.Point, row), Is.LessThan(1e-3f), "the ghost finger on the Piano row");
            // The first tip ever adds the skip hint.
            StepUntil(() => S.LineId == TourTips.PokeMe, 8f, "poke me anytime to skip");
            // Doing it: poking the Piano row is the tip's success.
            PokeRow(r => r.Id == RowKind.Inst && r.Inst == InstrumentKind.Piano);
            Assert.That(_menu.Screen, Is.EqualTo(MenuScreen.Courses));
            Assert.IsTrue(_guide.TipSeen("tip_lessons"), "learned");
            StepUntil(() => !Tips.Showing && !S.Present, 8f, "Sunny leaves without a word");
            Assert.That(_boot.Settings.GetString(GuideModule.KeyTipsSeen, ""), Does.Contain("tip_lessons"), "persisted");
            Assert.That(_lines, Does.Not.Contain(GuideLines.Leave), "no Bye! after a tip");
            // Again: never twice.
            _menu.Go(MenuScreen.Lessons);
            Step(72 * 3);
            Assert.That(Tips.ShowingId, Is.Not.EqualTo("tip_lessons"));
        }

        [UnityTest]
        public IEnumerator ATipFromTheListPlaysAtOnceAtItsTargetAndAPokeEndsIt()
        {
            yield return Boot();
            ((IGuideTour)_guide).PlayTip("tip_hihat");
            Step(3);
            Assert.That(Tips.ShowingId, Is.EqualTo("tip_hihat"));
            StepUntil(() => S.LineId == "tip_hihat", 2f, "its line");
            Step(10);
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Pinch), "a pinch at the hi-hat stand");
            DrumPieceInfo hat = default;
            foreach (DrumPieceInfo p in _app.Get<IDrumsApi>().Pieces) if (p.Kind == "hat") hat = p;
            Assert.That(Vector3.Distance(S.Anchor.Point, hat.Transform.position), Is.LessThan(0.3f), "at the hi-hat");
            Assert.IsTrue(S.WorldDemo);
            PokeSunny();
            Assert.That(S.Pokes, Is.EqualTo(1));
            Assert.IsFalse(Tips.Showing, "a poke ends it");
            Assert.IsTrue(_guide.TipSeen("tip_hihat"), "and counts as heard");
            StepUntil(() => !S.Present, 2f, "gone");
        }

        [UnityTest]
        public IEnumerator DoingItYourselfLearnsATipThatWasNeverShown()
        {
            yield return Boot();
            Assert.IsFalse(_guide.TipSeen("tip_switches"));
            _app.Get<IAmbienceSwitches>().Set(AmbienceKind.Fire, true);
            Step(2);
            Assert.IsTrue(_guide.TipSeen("tip_switches"), "flipping a switch learns the switches tip");
            Assert.IsFalse(Tips.Showing);
            _app.Get<IDrumsApi>().PlaybackHit("snare", 0.7f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" });
            Step(2);
            Assert.IsTrue(_guide.TipSeen("tip_drum"), "a drum hit learns the drum tip");
        }

        [UnityTest]
        public IEnumerator TipsWhileIPlayOffKeepsTheFirstUseTipsQuiet()
        {
            yield return Boot(seen: PlatformTipBringGrid);   // the platform's Instruments tip heard: the tour's comes
            ((IGuideTour)_guide).TipsEnabled = false;
            Assert.That(_boot.Settings.GetInt(TourTipRunner.KeyTipsOn, 1), Is.EqualTo(0), "persisted");
            _menu.Show();
            Step(5);
            _menu.Go(MenuScreen.Instruments);
            Step(72 * 4);
            Assert.IsFalse(Tips.Showing, "P2: quiet while the switch is off");
            ((IGuideTour)_guide).TipsEnabled = true;
            _menu.Go(MenuScreen.Home);
            Step(10);
            _menu.Go(MenuScreen.Instruments);
            StepUntil(() => Tips.ShowingId == "tip_instruments", 3f, "on again");
        }

        [UnityTest]
        public IEnumerator OneSchedulerForEveryTipAndTheListHasThemAll()
        {
            yield return Boot();
            Assert.That(_guide.Tips.Presenter, Is.SameAs(Tips), "the platform's TipRunner schedules; the tour's tips are presented by TourTipRunner");
            var tour = (IGuideTour)_guide;
            // The Tips list: the tour's tips, the platform's six and every registry instrument's.
            var listed = new HashSet<string>();
            for (int i = 0; i < tour.TipCount; i++) listed.Add(tour.TipId(i));
            // Every tip of the runner, except the tour's switched-off ones (tip_bowl_sing, until the bowls work wires it).
            int off = 0;
            foreach (TourTip t in TourTips.All) if (t.Trigger == TourTrigger.Off) off++;
            Assert.That(off, Is.EqualTo(1));
            Assert.That(tour.TipCount, Is.EqualTo(_guide.Tips.Entries.Count - off), "every tip of the runner is in the list");
            foreach (string p in new[] { "tip_bring_grid", "tip_summon_far", "tip_my_set", "tip_set_full", "tip_put_away", "tip_page_turn" })
                Assert.IsTrue(listed.Contains(p), "the platform's " + p);
            int instrumentTips = 0;
            foreach (InstrumentDescriptor d in _app.Registry.All)
                if (d.Tips != null) foreach (TipSpec t in d.Tips) { Assert.IsTrue(listed.Contains(t.Id), d.Id + ": " + t.Id); instrumentTips++; }
            Assert.That(instrumentTips, Is.GreaterThanOrEqualTo(5), "one arrival tip per new instrument at least");
            foreach (TourTip t in TourTips.All) Assert.That(listed.Contains(t.Id), Is.EqualTo(t.Trigger != TourTrigger.Off), t.Id);
            Assert.That(tour.TipId(0), Is.EqualTo(TourTips.All[0].Id), "the tour's first, in their order");

            // The quiet-moment rules hold for the platform's tips too: no tip over playing.
            _app.Events.Publish(new RecorderEvent { Event = TakeEvent.PianoOn(60, 0.6f) });
            _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Table, Signal = "menu.instruments" });
            Step(72);
            Assert.That(_guide.ActiveTip, Is.Null, "1 s after a note: not yet");
            Assert.That(_guide.Tips.Find(PlatformTipBringGrid).Pending, Is.True, "pending in the one scheduler");
            StepUntil(() => _guide.ActiveTip == PlatformTipBringGrid, 3f, "the platform's tip once it is quiet");
            Assert.That(S.Line, Is.EqualTo("Poke an instrument in Instruments to bring it here."));
            // Never two at once: a tour tip triggered now waits for Sunny.
            ((IGuide)_guide).Tip("tip_loop_start");
            Step(10);
            Assert.IsFalse(Tips.Showing, "Sunny is saying the platform's tip");
            StepUntil(() => !S.Present, 10f, "the platform's tip ends");

            // The list plays any tip at once, the platform's too.
            tour.PlayTip("tip_page_turn");
            Step(3);
            Assert.That(_guide.ActiveTip, Is.EqualTo("tip_page_turn"));
            Assert.That(S.Line, Is.EqualTo("Swipe your hand sideways for the next instrument."));
            tour.PlayTip("tip_hihat");   // a tour tip from the list takes over
            Step(3);
            Assert.That(Tips.ShowingId, Is.EqualTo("tip_hihat"));
            Assert.IsFalse(S.InTip, "one tip at a time");
        }

        [UnityTest]
        public IEnumerator WithoutTheStartPanelTheTipsStayQuietInOtherTests()
        {
            yield return Boot(tips: false);
            _menu.Show();
            Step(5);
            _menu.Go(MenuScreen.Lessons);
            Step(72 * 3);
            Assert.IsFalse(Tips.Showing);
            Assert.IsFalse(S.Present);
        }

        [UnityTest]
        public IEnumerator TheCelebrationIsCalmAndRealAndPutsTheRoomBack()
        {
            yield return Boot(tips: false);
            RoomModule room = null;
            RoomMoments moments = null;
            foreach (var m in _app.Modules) { if (m is RoomModule r) room = r; if (m is RoomMoments rm) moments = rm; }
            Assert.That(moments, Is.Not.Null);
            Light lamp = room.Scene.FloorLamp;
            float lamp0 = lamp.intensity;
            Color color0 = lamp.color;
            var plucks = new List<int>();
            _app.Events.Subscribe<RecorderEvent>(e => { if (e.Event.type == TakeEventType.Harp) plucks.Add(e.Event.i); });
            Assert.IsFalse(_app.Get<IAmbienceSwitches>().Get(AmbienceKind.Snow));
            ((IRoomMoments)moments).Celebrate();
            Step(36);   // 0.5 s
            Assert.That(plucks, Is.EqualTo(new[] { 0, 2, 4, 7 }), "a rolled C chord, C3 E3 G3 C4, 70 ms apart");
            Assert.IsTrue(_app.Get<IAmbienceSwitches>().Get(AmbienceKind.Snow), "snow starts falling (the lever flips)");
            Assert.That(moments.FireFactor, Is.GreaterThan(1.3f), "the fire flares");
            Step(72 * 2);
            Assert.That(lamp.intensity, Is.GreaterThan(lamp0 * 1.1f), "the lamp glows a little brighter");
            Assert.That(lamp.color.b, Is.LessThan(color0.b - 0.02f), "and warmer");
            Assert.IsTrue(((IRoomMoments)moments).Celebrating);
            Step((int)(RoomMoments.Duration / Dt) + 10);
            Assert.IsFalse(((IRoomMoments)moments).Celebrating);
            Assert.That(lamp.intensity, Is.EqualTo(lamp0), "the lamp exactly as it was");
            Assert.That(lamp.color, Is.EqualTo(color0));
            Assert.That(moments.FireFactor, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator TheDoneAndResetButtonsStopTheFingerOnTheirFace()
        {
            yield return Boot(tips: false);
            IArrangeMode arrange = _app.Get<IArrangeMode>();
            arrange.BeginArrange();
            Step(20);
            Assert.IsTrue(_menu.ArrangeButtonsShown);
            Assert.IsTrue(_app.Get<IMenuGuideHooks>().TryGetRowRect("done", out Vector3 c, out Vector3 n, out Vector2 size));
            Assert.That(size.x, Is.InRange(0.06f, 0.09f), "the half-size face's Done button");
            Vector3 behind = c - n * 0.004f;
            Assert.IsTrue(_menu.QueryContact(behind, 0.02f, 0.03f, out ContactHit hit), "4 mm into the button: in contact");
            Assert.That(hit.Use, Is.EqualTo(ContactUse.Clamp), "the drawn finger stops on the face");
            Assert.That(hit.Height, Is.EqualTo(-0.004f).Within(0.001f));
            Assert.That(Vector3.Distance(hit.Point, c), Is.LessThan(0.002f), "on the face");
            Assert.That(Vector3.Dot(hit.Normal, n), Is.GreaterThan(0.99f));
            Assert.IsFalse(_menu.QueryContact(c + n * 0.1f, 0.02f, 0.03f, out _), "10 cm in front: no contact");
            arrange.EndArrange();
            Step(20);
            Assert.IsFalse(_menu.QueryContact(behind, 0.02f, 0.03f, out _), "the buttons gone: nothing");
        }
    }
}
