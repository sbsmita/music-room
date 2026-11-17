// The bowls inside the booted app (TestBoot, headless, manual frames, the scripted audio clock):
//   review item 14 through the real looper: a loop records a rub on the middle bowl; on the next pass its playback owns
//   the bowl (lane 1); a live rub takes the bowl from it (a preempt: the loop's controls are dropped and its Off ignored
//   while the hand rubs); once the hand lifts, the loop's next pass takes the bowl back;
//   the desktop: a click strikes once (check 16: the mouse hand's fingertips never play the bowls too), a circle-drag
//   rubs (On / controls / Off recorded) and button-up lifts;
//   the stick through Layout's grab pass: a pinch at its grip picks it up (HandState.Holding, its two tool pointers down
//   in the next frame), the stick's pointers reach the bowls' Tick, and the bowls going on display put it back.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Bowls;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.BowlsPlay
{
    public class BowlsPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t;
        BowlsInstrument _b;
        ILayoutService _layout;
        AudioTestTap _tap;
        readonly List<TakeEvent> _recorded = new List<TakeEvent>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AudioSchedule.Reset();
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }

        IEnumerator BootAtTheSeat(bool headset = false)
        {
            _t = 50.0;
            _boot = new TestBoot(headless: true, simulateHeadset: headset, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            yield return _boot.Start();
            _layout = App.Current.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!_layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            Assert.IsTrue(_layout.FirstArrangeDone);
            foreach (IInstrument i in App.Current.Instruments) if (i.Kind == InstrumentKind.Bowls) _b = i as BowlsInstrument;
            Assert.That(_b, Is.Not.Null);
            Assert.IsTrue(_layout.Bring(InstrumentKind.Bowls, BringReason.Menu));
            for (int i = 0; i < 400 && (!_b.IsPresent || _layout.AnyMoving); i++) { Step(); yield return null; }
            Assert.That(_b.IsPresent, Is.True, "the bowls arrived at the seat");
            Step(4);
            Physics.SyncTransforms();
            var engine = App.Current.Get<IAudioEngine>();
            _tap = new AudioTestTap();
            engine.TestTap = _tap;
            var rec = App.Current.Get<IRecorder>();
            Assert.That(rec, Is.Not.Null);
        }

        /// <summary>A ray straight down onto bowl i at rig-frame angle a, 0.9 R from its centre.</summary>
        Ray DownOnto(int bowl, float a)
        {
            float r = BowlsLayout.R(bowl) * 0.9f;
            Vector3 l = new Vector3(BowlsLayout.BowlXZ[bowl].x + r * Mathf.Cos(a), BowlsLayout.RimY(bowl) + 0.3f, BowlsLayout.BowlXZ[bowl].y + r * Mathf.Sin(a));
            return new Ray(_b.transform.TransformPoint(l), -_b.transform.up);
        }

        /// <summary>A desktop press on bowl i, then a circle-drag at `speed` m/s for `seconds`, then button up.</summary>
        void DesktopRub(int bowl, float speed, double seconds, ref float angle)
        {
            float omega = speed / BowlsLayout.R(bowl);
            _b.ScriptedMouse = true; _b.ScriptedButton = true; _b.ScriptedRay = DownOnto(bowl, angle);
            Step();
            int frames = (int)Math.Round(seconds / Dt);
            for (int k = 0; k < frames; k++) { angle += omega * Dt; _b.ScriptedRay = DownOnto(bowl, angle); Step(); }
            _b.ScriptedButton = false; Step();
        }

        [UnityTest]
        public IEnumerator ALoopedRubYieldsToTheLiveHandAndResumes()
        {
            yield return BootAtTheSeat();
            var lessons = App.Current.Modules.OfType<LessonsModule>().First();
            Looper looper = lessons.Looper;
            looper.InputLatency = 0;
            ILooper loop = App.Current.Get<ILooper>();
            loop.Press(LoopControl.Loop);
            Step(2);
            Assert.That(looper.State, Is.EqualTo(LoopState.Waiting));
            double sr = App.Current.Get<IAudioEngine>().SampleRate > 0 ? App.Current.Get<IAudioEngine>().SampleRate : 48000;
            // wait for a click, then the first note: a press on the middle bowl (a strike) and a 2.5 s circle-drag
            long click = looper.ClickStart;
            while (_t * sr < click + looper.Session.BeatSamples) Step();
            float a = Mathf.PI * 0.75f;
            DesktopRub(1, 0.45f, 2.5, ref a);
            Assert.That(looper.State, Is.EqualTo(LoopState.Recording), "the strike started layer 1");
            long anchor = looper.Anchor;
            double pass = looper.Session.PassSamples / sr;
            Debug.Log($"[BowlsLoop] layer 1 anchored at {anchor / sr:F3} s, pass {pass:F3} s");
            // let layer 1 close and loop: pass 2 starts at anchor + pass; its rub sounds from about 0.1 s to 2.6 s in
            double p2 = anchor / sr + pass;
            while (_t < p2 + 0.8) Step();
            Assert.That(looper.State, Is.EqualTo(LoopState.Looping));
            Assert.That(_b.Arbiter(1).Owner, Is.EqualTo(PlaybackLanes.Layer(0)), "the loop's playback owns the middle bowl (lane 1)");
            int contactsBefore = _tap.CountUnit(UnitId.Bowls, BowlsDsp.OpContact);
            Assert.That(contactsBefore, Is.GreaterThan(0), "the loop rubs the bowl through the unit");
            // the live hand takes the bowl in the middle of the loop's rub, and rubs past the loop's own Off
            int preempts = _b.Arbiter(1).Preempts, drops = _b.Arbiter(1).Drops;
            float la = 0f;
            _b.ScriptedMouse = true; _b.ScriptedButton = true; _b.ScriptedRay = DownOnto(1, la); Step();
            float omega = 0.45f / BowlsLayout.R(1);
            while (_t < p2 + 3.2) { la += omega * Dt; _b.ScriptedRay = DownOnto(1, la); Step(); }
            Assert.That(_b.Arbiter(1).Preempts, Is.EqualTo(preempts + 1), "one preempt: the live hand took the bowl from the loop");
            Assert.That(_b.Arbiter(1).Owner, Is.EqualTo(PlaybackLanes.Live), "the hand keeps it past the loop's Off");
            Assert.That(_b.Arbiter(1).Drops, Is.GreaterThan(drops), "the loop's controls were dropped while the hand rubbed");
            _b.ScriptedButton = false; Step();
            Assert.That(_b.Arbiter(1).IsFree, Is.True, "the hand lifted: the bowl is free");
            // pass 3: the loop takes the bowl back
            double p3 = p2 + pass;
            while (_t < p3 + 0.8) Step();
            Assert.That(_b.Arbiter(1).Owner, Is.EqualTo(PlaybackLanes.Layer(0)), "the loop resumes on the next pass");
            while (_t < p3 + pass - 0.2) Step();
            Assert.That(_b.Arbiter(1).IsFree, Is.True, "the loop's own Off frees it again");
            Debug.Log($"[BowlsLoop] preempts {_b.Arbiter(1).Preempts}, drops {_b.Arbiter(1).Drops}, unit contacts {_tap.CountUnit(UnitId.Bowls, BowlsDsp.OpContact)}, releases {_tap.CountUnit(UnitId.Bowls, BowlsDsp.OpRelease)}");
            loop.Press(LoopControl.Stop);
            Step(4);
        }

        [UnityTest]
        public IEnumerator ADesktopClickStrikesOnceAndADragRubs()
        {
            yield return BootAtTheSeat();
            var rec = App.Current.Get<IRecorder>();
            int strikes0 = _b.LiveStrikes;
            int unitStrikes0 = _tap.CountUnit(UnitId.Bowls, BowlsDsp.OpStrike);
            _b.ScriptedMouse = true; _b.ScriptedButton = true; _b.ScriptedRay = DownOnto(2, 3.0f);
            Step();
            _b.ScriptedButton = false; Step(10);
            Assert.That(_b.LiveStrikes, Is.EqualTo(strikes0 + 1), "one strike per click");
            Assert.That(_tap.CountUnit(UnitId.Bowls, BowlsDsp.OpStrike), Is.EqualTo(unitStrikes0 + 1), "the unit heard one strike (no fingertip double-fire)");
            float a = 2f;
            int contacts0 = _tap.CountUnit(UnitId.Bowls, BowlsDsp.OpContact);
            DesktopRub(0, 0.5f, 1.0, ref a);
            Assert.That(_tap.CountUnit(UnitId.Bowls, BowlsDsp.OpContact), Is.GreaterThan(contacts0 + 30), "a circle-drag rubs every frame");
            Assert.That(_b.Arbiter(0).IsFree, Is.True, "button up lifts");
            Assert.That(_tap.CountUnit(UnitId.Bowls, BowlsDsp.OpRelease), Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator APinchAtTheGripPicksUpTheStickThroughTheGrabPass()
        {
            yield return BootAtTheSeat(headset: true);   // the hands grab only in a headset session
            _b.TryGetAnchor("puja.grip", out Vector3 grip, out _);
            var tools = App.Current.GetAll<IHandToolProvider>().First(t => t.Owner == InstrumentKind.Bowls);
            Assert.That(tools.NearestFree(grip), Is.LessThan(0.02f), "free on the stand");
            HandState right = App.Current.Input.Hand(Handedness.Right);
            _boot.Hands.SetTracked(HandSide.Right, true);
            Vector3 wrist = grip + new Vector3(0.02f, 0.12f, -0.09f);
            HandPose open = HandPose.OpenFacing(HandSide.Right, wrist, (grip - wrist).normalized, Vector3.down);
            HandPose pinch = open.WithPinch(0f);
            pinch = pinch.WithIndexTipAt(grip + (pinch.Position[(int)HandJoint.IndexTip] - pinch.Position[(int)HandJoint.ThumbTip]) * 0.5f);
            _boot.Hands.SetPose(HandSide.Right, open.WithIndexTipAt(grip + Vector3.up * 0.06f)); Step(6);
            _boot.Hands.SetPose(HandSide.Right, pinch); Step(1);
            HandFrame f = App.Current.Input.Frame(right);
            Debug.Log($"[BowlsStick] pinch frame: tracked {f.isTracked}, thumb-index {Vector3.Distance(f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip]) * 1000f:F1} mm, " +
                      $"grab pinch {right.GrabPinching} start {right.GrabPinchStart}, pinch {right.Pinching}, index tip -> stick {tools.NearestFree(right.Pointers[0].Tip) * 1000f:F0} mm, " +
                      $"mid -> stick {tools.NearestFree((f.position[(int)HandJoint.ThumbTip] + f.position[(int)HandJoint.IndexTip]) * 0.5f) * 1000f:F0} mm, holding {right.Holding}");
            Step(5);
            Assert.That(right.Holding, Is.SameAs(_b.Stick), "the grab pass picked the stick up");
            Assert.That(tools.AnyHeld, Is.True);
            Step(2);
            Assert.That(_b.Stick.Suede.Down && _b.Stick.Shaft.Down, Is.True, "its two tool pointers are down");
            IReadOnlyList<Pointer> list = App.Current.Input.PointersFor(App.Current.Registry.Get(InstrumentKind.Bowls).Tools);
            Assert.That(list.Contains(_b.Stick.Suede) && list.Contains(_b.Stick.Shaft), Is.True, "Core hands them to the bowls' Tick");
            Assert.That(_b.Stick.Suede.Tool, Is.EqualTo(ToolKind.Striker)); Assert.That(_b.Stick.Shaft.Tool, Is.EqualTo(ToolKind.RubStick));
            Assert.That(Vector3.Distance(_b.View.Puja.position, _b.transform.TransformPoint(BowlsGeometry.PujaRestMatrix().GetColumn(3))), Is.GreaterThan(0.01f), "the drawn stick left the stand with the hand");
            // the bowls go on display: the stick goes back on the stand
            ((IStowable)_b).SetStowed(true, StowMount.Surface);
            Step(2);
            Assert.That(right.Holding, Is.Null, "a stowed instrument holds no tool");
            Assert.That(tools.AnyHeld, Is.False);
            Assert.That(tools.NearestFree(grip), Is.EqualTo(float.PositiveInfinity), "nothing to pick up on display");
            ((IStowable)_b).SetStowed(false, StowMount.Floor);
        }
    }
}
