// The test hooks of BootOptions (ARCHITECTURE section 16): isolated settings and data folder, the simulated headset gate
// (App.IsPresenting), deterministic manual frames (App.Step) and the scripted audio clock.
using System.Collections;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class BootOptionsTests
    {
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator SettingsAndTakesStayInTheTestsOwnStoreAndFolder()
        {
            _boot = new TestBoot();
            yield return _boot.Start();
            var app = App.Current;
            Assert.That(app.Settings.Store, Is.SameAs(_boot.Settings), "BootOptions.Settings replaces PlayerPrefs");
            Assert.That(app.DataPath, Is.EqualTo(_boot.DataPath));
            app.Get<ILayoutService>().Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(_boot.Settings.GetString(AppSettings.KeyLayoutMode, null), Is.EqualTo("single"), "the layout persisted into the test store");
            Assert.That(_boot.Settings.GetString(AppSettings.KeyCurrentInstrument, null), Is.EqualTo("harp"));
            Assert.That(Directory.Exists(Path.Combine(_boot.DataPath, "takes")), Is.True, "the take library lives under DataPath/takes");
            Assert.That(app.Get<ITakeLibrary>().Available, Is.True);
        }

        [UnityTest]
        public IEnumerator SimulateHeadsetMakesTheAppPresent()
        {
            _boot = new TestBoot(simulateHeadset: true);
            yield return _boot.Start();
            var app = App.Current;
            Assert.IsTrue(app.IsPresenting, "XR-only paths run with scripted hands");
            Assert.IsFalse(app.IsHeadset, "the hands are still scripted, not the tracker");
            yield return _boot.Stop();
            _boot = new TestBoot();
            yield return _boot.Start();
            Assert.IsFalse(App.Current.IsPresenting, "without the option the desktop / editor is not presenting");
        }

        [UnityTest]
        public IEnumerator ManualFramesStepHandsAndClockExactly()
        {
            double audio = 100.0;
            _boot = new TestBoot(manualFrames: true, audioClock: () => audio);
            var open = HandPose.Open(HandSide.Right, new Vector3(0.2f, 1.0f, 0.4f), Quaternion.identity).WithPointing();
            _boot.Hands.SetPose(HandSide.Right, open);
            yield return _boot.Start();
            var app = App.Current;
            Assert.IsTrue(app.ManualFrames);
            int frame0 = app.Clock.Frame;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(app.Clock.Frame, Is.EqualTo(frame0), "Unity's Update drives nothing");

            const float dt = 1f / 72f;
            var hand = app.Input.Hand(Handedness.Right);
            var hi = app.HandInput;   // HandState edges are cleared by Input.EndFrame inside the step; HandInput's last until the next step
            _boot.Hands.SetPose(HandSide.Right, open.WithPinch(0f));
            _boot.Step(dt);
            Assert.IsTrue(hi.pinchStart[1], "the pinch edge of this step");
            Assert.IsFalse(hand.PinchStart, "HandState edges live inside one frame only (cleared before EndFrame)");
            int starts = 1;
            for (int i = 1; i < 22; i++) { _boot.Step(dt); if (hi.pinchStart[1]) starts++; }
            Assert.That(starts, Is.EqualTo(1), "PinchStart is raised once, not repeated by later steps");
            Assert.IsTrue(hand.Pinching);
            Assert.That(hand.PinchFor, Is.EqualTo(22 * dt).Within(1e-5f), "a still 0.3 s pinch is exactly 22 steps of 1/72 s");
            Assert.That(hand.PinchFor, Is.GreaterThanOrEqualTo(GrabConstants.GrabDwell));
            Assert.That(app.Clock.Now, Is.EqualTo(22 * (double)dt).Within(1e-6));

            // A finger moved 1 cm per step reads exactly 0.72 m/s at 72 Hz (the scripted tracker samples every step).
            _boot.Hands.SetPose(HandSide.Right, open);
            _boot.Step(dt);
            var pose = open;
            for (int i = 0; i < 10; i++) { pose = pose.Translated(new Vector3(0f, -0.01f, 0f)); _boot.Hands.SetPose(HandSide.Right, pose); _boot.Step(dt); }
            Assert.That(hand.Pointers[0].Speed, Is.EqualTo(0.72f).Within(0.01f));

            // The scripted audio clock drives IAudioEngine.Now and Clock.AudioNow.
            audio = 123.5;
            _boot.Step(dt);
            Assert.That(app.Clock.AudioNow, Is.EqualTo(123.5));
            Assert.That(app.Get<IAudioEngine>().Now, Is.EqualTo(123.5));
            Assert.That(app.AudioTime, Is.EqualTo(123.5));
        }
    }
}
