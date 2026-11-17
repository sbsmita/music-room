// The menu under a simulated headset with manual frames (ARCHITECTURE.md section 16: timed behaviour on scripted time):
// the summon fires after half a second of frame time, not of wall time, and the session rule
// (spec 08 section 1.2): the panel is put away when App.IsPresenting changes, here when the hands fall back to the
// desktop mouse source (IsPresenting false), whose hand never summons. Also the frame order of the panel's update():
// the open / close animation runs inside the menu's instrument tick, so whatever stops that tick (MR placing)
// freezes the fade.
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Menu
{
    public class MenuPresentingTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        PlacingProbe _mr;

        /// IMixedReality registered before the MR module (Order 65 &lt; 70), so Core's instrument step reads its IsPlacing.
        sealed class PlacingProbe : AppModule, IMixedReality
        {
            public override int Order => 65;
            public override void Init(App app) => app.Register<IMixedReality>(this);
            public bool Available => false;
            public bool Active => false;
            public bool IsPlacing { get; set; }
            public MrStatus Status => MrStatus.Idle;
            public bool HasSpaceSetup => false;
            public Task Enter() => Task.CompletedTask;
            public void Exit() { }
            public Task RunSpaceSetup() => Task.CompletedTask;
            public bool TryGetPianoPose(out Pose pose, out bool onTable) { pose = default; onTable = false; return false; }
            public void StartPlacing() => IsPlacing = true;
        }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _mr = new PlacingProbe();
            _boot.Options.ExtraModules.Add(_mr);
            MenuBootGuard.Begin();   // the known SynthSource boot race only (CONTRACT-REQUESTS.md 3)
            yield return _boot.Start();
            MenuBootGuard.End();
            // The rig's desktop fallback poses the head after its own (real-time) start-up wait.
            var app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            _boot.Step(Dt, 2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            yield return _boot.Stop();
        }

        static MenuModule Menu => (MenuModule)App.Current.Get<IMenuPanel>();

        void OpenPalmTowardTheFace()
        {
            Vector3 head = App.Current.Head.position;
            Vector3 wrist = head + new Vector3(0.05f, -0.17f, 0.3f);
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, Vector3.up, (head - (wrist + new Vector3(0f, 0.07f, 0f))).normalized));
        }

        [UnityTest]
        public IEnumerator ThePalmSummonsAfterHalfASecondOfFrameTime()
        {
            var app = App.Current;
            var m = Menu;
            Assert.IsTrue(app.IsPresenting, "simulated headset with scripted hands");
            OpenPalmTowardTheFace();
            _boot.Step(Dt, 34);   // 0.472 s of charge
            Assert.IsFalse(m.Shown, "not before 0.5 s");
            Assert.IsTrue(m.View.Ring.gameObject.activeSelf, "the ring charges on the palm");
            Assert.That(m.Gesture.T, Is.InRange(32 * Dt, 34 * Dt + 1e-4f), "the charge counts frame time");
            _boot.Step(Dt, 4);
            Assert.IsTrue(m.Shown, "summoned at 0.5 s");
            Assert.IsFalse(m.View.Ring.gameObject.activeSelf);
            _boot.Step(Dt, 72);
            Assert.IsTrue(m.Shown, "a held palm toggles once");
            Assert.That(m.AnimK, Is.EqualTo(1f), "the open animation ran on frame time (0.15 s)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheCloseFadeRunsInTheMenusTickAndFreezesWhileThePianoIsPlaced()
        {
            var m = Menu;
            Assert.That(App.Current.Get<IMixedReality>(), Is.SameAs(_mr));
            m.Show();
            _boot.Step(Dt, 15);
            Assert.That(m.AnimK, Is.EqualTo(1f));
            m.Hide();
            _boot.Step(Dt, 3);
            float k = m.AnimK;
            Assert.That(k, Is.LessThan(1f).And.GreaterThan(0f), "fading out");
            // MR placing: Core ticks no instrument, the menu included: the fade holds where it is.
            _mr.IsPlacing = true;
            _boot.Step(Dt, 20);
            Assert.That(m.AnimK, Is.EqualTo(k), "frozen while placing");
            Assert.IsTrue(m.View.VisualActive);
            _mr.IsPlacing = false;
            _boot.Step(Dt, 20);
            Assert.That(m.AnimK, Is.EqualTo(0f));
            Assert.IsFalse(m.View.VisualActive, "and finishes once the piano is placed");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePanelIsPutAwayWhenTheSessionStopsPresenting()
        {
            var app = App.Current;
            var m = Menu;
            m.Show();
            _boot.Step(Dt, 2);
            Assert.IsTrue(m.Shown);
            Assert.IsTrue(app.IsPresenting);
            try
            {
                // The hands fall back to the desktop mouse source: no XR session presents any more.
                HandInput.OverrideSource = null;
                _boot.Step(Dt);
                Assert.That(app.HandInput.Source, Is.SameAs(app.HandInput.Mouse));
                Assert.IsFalse(app.IsPresenting);
                Assert.IsFalse(m.Shown, "hidden when the session ends");
                // The mouse hand never charges the summon (only a tracked hand does).
                _boot.Step(Dt, 72);
                Assert.IsFalse(m.Shown);
                Assert.IsFalse(m.View.Ring.gameObject.activeSelf);
                Assert.That(m.Gesture.Hand, Is.EqualTo(-1));
                // Desktop: M (and the face click) open it; showing works as before.
                m.Show();
                _boot.Step(Dt);
                Assert.IsTrue(m.Shown);
            }
            finally
            {
                HandInput.OverrideSource = _boot.Hands;
            }
            _boot.Step(Dt);
            Assert.IsTrue(app.IsPresenting, "presenting again");
            Assert.IsFalse(m.Shown, "and put away again when the session starts");
            yield return null;
        }
    }
}
