// Sunny and "Move instruments" (IArrangeMode): the first time arrange mode turns on Sunny comes by with tip_arrange
// ("Pinch an instrument and drag it where you like.", since 2026-11-10), leaves without "Bye!" and never says it again
// (musicroom.tipsSeen); an arrange grab during the tip counts as seen; a tour in progress is held out of sight and silent
// instead (no prompts to play while nothing plays) and carries on after Done; a tip before the first-launch tour still
// lets the tour begin afterwards. Audio + Guide with GuideProbe and an arrange probe, manual frames, scripted audio clock.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class GuideArrangeTipTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        GuideProbe _probe;
        ArrangeProbe _arrange;
        GuideModule _guide;
        AudioTestTap _tap;
        double _t;

        /// IArrangeMode and IGrabService as the Guide reads them (Order 45, before the Guide).
        sealed class ArrangeProbe : AppModule, IArrangeMode, IGrabService
        {
            public override int Order => 45;
            public override void Init(App app) { app.Register<IArrangeMode>(this); app.Register<IGrabService>(this); }
            public bool Arranging { get; set; }
            public void BeginArrange() => Arranging = true;
            public void EndArrange() => Arranging = false;
            public void ResetArrangement() { }
            public Rig HeldRig => null;
            public bool AnyHeld { get; set; }
            public Rig GrabTarget(Vector3 point, float reach = 0.07f) => null;
        }

        IEnumerator Boot(bool tourDone, bool arrangedAtBoot = true)
        {
            _t = 10.0;
            MusicRoom.Guide.GuideModule.ClassicTour = true;   // first five: these tests check the classic five-step tour
            _boot = new TestBoot(headless: true, manualFrames: true, audioClock: () => _t);
            if (tourDone) _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule) || t == typeof(GuideModule);
            _probe = new GuideProbe { FirstArrangeDone = arrangedAtBoot };
            _arrange = new ArrangeProbe();
            _boot.Options.ExtraModules.Add(_probe);
            _boot.Options.ExtraModules.Add(_arrange);
            AudioBootGuard.Begin();
            yield return _boot.Start();
            yield return null;
            AudioBootGuard.End();
            _guide = App.Current.Get<IGuide>() as GuideModule;
            Assert.That(_guide, Is.Not.Null);
            var engine = App.Current.Get<IAudioEngine>();
            yield return GuideModuleTests.WaitForLines(engine, GuideLines.TipArrange, GuideLines.IntroDesk);
            _tap = new AudioTestTap();
            engine.TestTap = _tap;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MusicRoom.Guide.GuideModule.ClassicTour = false;
            AudioBootGuard.Reset();
            if (App.Current != null && App.Current.Get<IAudioEngine>() is IAudioEngine e) e.TestTap = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); }
        }

        List<string> Said()
        {
            var ids = new List<string>();
            foreach (var c in _tap.Calls) if (c.Method == AudioCall.VoiceLine) ids.Add(c.Line);
            return ids;
        }

        [UnityTest]
        public IEnumerator TheFirstArrangeModeBringsSunnyWithTheTipOnce()
        {
            yield return Boot(tourDone: true);
            Step(5);
            Assert.IsFalse(_guide.Present, "the tour was done on an earlier launch");
            _arrange.Arranging = true;
            Step(1);
            Assert.IsTrue(_guide.Present, "Sunny comes by");
            Assert.IsTrue(_guide.Sunny.InTip);
            Assert.That(_guide.ActiveTip, Is.EqualTo("tip_arrange"));
            Assert.That(_guide.Sunny.Line, Is.EqualTo("Pinch an instrument and drag it where you like."));
            Assert.IsTrue(_guide.View.Root.gameObject.activeSelf, "in view while the instruments are moved");
            Step(3);
            Assert.That(Said(), Is.EqualTo(new[] { "tip_arrange" }), "the tip's voice line");
            Assert.IsFalse(_guide.TipSeen("tip_arrange"), "not yet: the line is still playing");
            for (int i = 0; i < 6 * 72 && _guide.Present; i++) Step();
            Assert.IsFalse(_guide.Present, "it leaves once the line has been heard");
            Assert.That(Said(), Is.EqualTo(new[] { "tip_arrange" }), "no Bye! after a tip");
            Assert.IsTrue(_guide.TipSeen("tip_arrange"));
            Assert.That(_boot.Settings.GetString(GuideModule.KeyTipsSeen, ""), Is.EqualTo("tip_arrange"));
            Assert.IsFalse(_guide.Done, "a tip is not the tour");
            // Done, then Move instruments again: no second tip.
            _arrange.Arranging = false;
            Step(3);
            _arrange.Arranging = true;
            Step(30);
            Assert.IsFalse(_guide.Present);
            Assert.That(_guide.TipsShown, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AnArrangeGrabDuringTheTipCountsAsSeen()
        {
            yield return Boot(tourDone: true);
            _boot.Settings.SetString(GuideModule.KeyTipsSeen, "tip_other");
            _arrange.Arranging = true;
            Step(10);
            Assert.IsTrue(_guide.Sunny.InTip);
            _arrange.AnyHeld = true;   // the player picked an instrument up
            Step(1);
            Assert.IsTrue(_guide.TipSeen("tip_arrange"), "doing it counts");
            Assert.That(_boot.Settings.GetString(GuideModule.KeyTipsSeen, ""), Is.EqualTo("tip_other,tip_arrange"));
        }

        [UnityTest]
        public IEnumerator ATourInProgressIsHeldOutOfSightAndCarriesOnAfterDone()
        {
            yield return Boot(tourDone: false);
            for (int i = 0; i < 72 && !_guide.Present; i++) Step();
            Assert.IsTrue(_guide.Present, "the first-launch tour");
            Assert.IsFalse(_guide.Sunny.InTip);
            Step(10);
            float t0 = _guide.Sunny.Time;
            SunnyState s0 = _guide.Sunny.State;
            int calls = _tap.Calls.Count;
            _arrange.Arranging = true;
            Step(5 * 72);
            Assert.IsFalse(_guide.View.Root.gameObject.activeSelf, "out of sight");
            Assert.That(_guide.Sunny.Time, Is.EqualTo(t0).Within(1e-4f), "the tour holds");
            Assert.That(_guide.Sunny.State, Is.EqualTo(s0));
            Assert.IsNull(_guide.ActiveTip, "no tip over a tour");
            foreach (var c in _tap.Calls.GetRange(calls, _tap.Calls.Count - calls))
                Assert.That(c.Method, Is.Not.EqualTo(AudioCall.VoiceLine), "silent while the instruments are moved");
            _arrange.Arranging = false;
            Step(2);
            Assert.IsTrue(_guide.View.Root.gameObject.activeSelf, "back after Done");
            Assert.That(_guide.Sunny.Time, Is.GreaterThan(t0), "and the tour carries on");
            Assert.IsTrue(_guide.Present);
        }

        [UnityTest]
        public IEnumerator ATipBeforeTheFirstLaunchTourStillLetsTheTourBegin()
        {
            yield return Boot(tourDone: false, arrangedAtBoot: false);
            // The rig's desktop fallback counts as arranged after its own real-time start: move the instruments at once.
            _arrange.Arranging = true;
            Step(1);
            Assert.IsTrue(_guide.Sunny.InTip, "the tip comes first");
            for (int i = 0; i < 6 * 72 && _guide.Sunny.InTip; i++) Step();
            Assert.IsFalse(_guide.Sunny.InTip);
            Step(72);
            Assert.IsFalse(_guide.Present, "no tour while the instruments are moved");
            _arrange.Arranging = false;
            _probe.FirstArrangeDone = true;
            for (int i = 0; i < 8 * 72 && !(_guide.Present && !_guide.Sunny.InTip); i++) Step();
            Assert.IsTrue(_guide.Present, "the tour begins after Done");
            Assert.IsFalse(_guide.Sunny.InTip);
            Assert.That(Said(), Does.Contain(GuideLines.IntroDesk));
        }
    }
}
