// The skeleton end to end without a headset: FakeHandSource hands, every module discovered, services registered, the
// desktop fallback, the pointer layer and the pinch / grasp hysteresis. Foundation-only assertions: module behaviour
// (audio recipes, layout homes, the menu) is asserted in Tests/PlayMode/<Module>/ (rule 3, ARCHITECTURE section 2).
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class AppBootTests
    {
        TestBoot _boot;
        HandPose _right;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: false);
            _right = HandPose.Open(HandSide.Right, new Vector3(0.2f, 1.0f, 0.4f), Quaternion.identity).WithPointing();
            _boot.Hands.SetPose(HandSide.Right, _right);
            yield return _boot.Start();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        [UnityTest]
        public IEnumerator ModulesAreDiscoveredAndServicesRegistered()
        {
            var app = App.Current;
            Assert.That(app, Is.Not.Null);
            var names = app.Modules.Select(m => m.GetType().Name).ToList();
            foreach (var want in new[] { "AudioModule", "RoomModule", "PianoModule", "DrumsModule", "HarpModule", "LessonsModule", "GuideModule", "MenuModule", "MrModule", "LayoutModule" })
                Assert.That(names, Does.Contain(want));
            Assert.That(app.Get<IAudioEngine>(), Is.Not.Null);
            Assert.That(app.Get<ILayoutService>(), Is.Not.Null);
            Assert.That(app.Get<IRigRegistry>().Rigs.Count, Is.GreaterThanOrEqualTo(5), "piano, drums, harp, table, menu rigs");
            foreach (var r in app.Get<IRigRegistry>().Rigs) Assert.That(r.Handles, Is.Not.Null, r + " handles list (filled once, never per call)");
            Assert.That(app.Instruments.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(app.Instruments[0].Kind, Is.EqualTo(InstrumentKind.Piano), "tick order piano first");
            Assert.That(app.Get<IPianoApi>().TryGetKey(60, out var c4), Is.True);
            Assert.That(c4.X, Is.EqualTo(-0.05875f).Within(1e-4f), "middle C x");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DesktopFallbackCameraAndLayers()
        {
            var app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(app.Rig.State, Is.EqualTo(XrState.Desktop), "no runtime in the editor");
            Assert.IsFalse(app.IsPresenting, "the desktop is not an XR session");
            Assert.That(app.Head.localPosition, Is.EqualTo(DesktopRigFallback.EyePosition));
            var cam = app.Rig.HeadCamera;
            Assert.That(cam.backgroundColor, Is.EqualTo(XrMode.VrBackground), "VR / desktop clear #05070f alpha 1 applied at start");
            Assert.That(cam.cullingMask & Layers.HeadCameraExcludeMask, Is.EqualTo(0), "the head camera never draws Hidden / PanelUI");
            Assert.That(app.Rig.Desktop.dragBlockMask, Is.EqualTo(Layers.PlaySurfaceMask), "look-drag yields to desktop click targets");
            yield return null;
            Assert.IsTrue(app.Get<ILayoutService>().FirstArrangeDone, "the desktop camera counts as the first tracked frame");
        }

        [UnityTest]
        public IEnumerator FakeHandsAreVisibleAndDriveThePointerLayer()
        {
            var app = App.Current;
            Assert.That(app.HandInput.Source, Is.SameAs(_boot.Hands));
            Assert.IsTrue(app.HandInput.Right.isTracked);
            Assert.That(app.HandInput.DebugHands, Is.Not.Null);
            Assert.That(app.HandInput.DebugHands.VisibleHands, Is.EqualTo(1), "one scripted hand drawn");
            var hand = app.Input.Hand(Handedness.Right);
            Assert.IsTrue(hand.Connected);
            Assert.IsFalse(hand.IsMouse, "scripted hands play (only the desktop mouse hand never does)");
            Assert.IsTrue(hand.Pointers[0].Active);
            Assert.That(hand.Pointers[0].Tip, Is.EqualTo(_right.Position[(int)HandJoint.IndexTip]).Within(1e-5f));
            // Move the hand 1 cm per frame: the pointer learns the gap and reports a speed.
            var pose = _right;
            for (int i = 0; i < 4; i++)
            {
                pose = pose.Translated(new Vector3(0f, -0.01f, 0f));
                _boot.Hands.SetPose(HandSide.Right, pose);
                yield return null;
            }
            Assert.That(hand.Pointers[0].Speed, Is.GreaterThan(0.1f));
            Assert.That(hand.Pointers[0].TrackerVel.y, Is.LessThan(-0.1f));
        }

        [UnityTest]
        public IEnumerator PinchAndGraspHysteresisThroughHandInput()
        {
            var app = App.Current;
            var hi = app.HandInput;
            var hand = app.Input.Hand(Handedness.Right);
            Assert.IsFalse(hand.Pinching);
            _boot.Hands.SetPose(HandSide.Right, _right.WithPinch(0.014f)); yield return null;
            Assert.IsTrue(hi.pinching[1], "<= 15 mm starts");
            Assert.IsTrue(hi.pinchStart[1]);
            Assert.IsTrue(hand.Pinching);
            _boot.Hands.SetPose(HandSide.Right, _right.WithPinch(0.020f)); yield return null;
            Assert.IsTrue(hi.pinching[1], "20 mm keeps the pinch");
            Assert.IsFalse(hi.pinchStart[1]);
            _boot.Hands.SetPose(HandSide.Right, _right.WithPinch(0.026f)); yield return null;
            Assert.IsFalse(hi.pinching[1], "> 25 mm ends");
            Assert.IsTrue(hi.pinchEnd[1]);
            Assert.That(hand.PinchFor, Is.EqualTo(0f));
            _boot.Hands.SetPose(HandSide.Right, _right.WithFist()); yield return null;
            Assert.IsTrue(hi.grasping[1], "a fist (~0.09 m) grasps");
            _boot.Hands.SetPose(HandSide.Right, _right); yield return null;
            Assert.IsFalse(hi.grasping[1], "the pointing pose (0.139 m) opens");
        }

        [UnityTest]
        public IEnumerator HoverGlowsAreOneDynamicMeshWithoutPropertyBlocks()
        {
            var app = App.Current;
            var pool = app.HoverGlow;
            Assert.That(pool.Renderer, Is.Not.Null);
            pool.Update(0f);
            Assert.IsFalse(pool.Renderer.enabled, "no glow, no draw");
            Assert.IsTrue(pool.Add(HoverGlowSpec.At(new Vector3(0f, 1f, 0.5f), Vector3.up, 0.1f, 0.02f, 1f, steady: true)));
            pool.Update(0f);
            Assert.IsTrue(pool.Renderer.enabled);
            Assert.IsFalse(pool.Renderer.HasPropertyBlock(), "no MaterialPropertyBlock (SRP Batcher compatible)");
            Vector3 a = pool.Corner(0, 0), b = pool.Corner(0, 1), c = pool.Corner(0, 2);
            Assert.That(Vector3.Distance(a, b), Is.EqualTo(0.1f).Within(1e-4f), "length along the axis");
            Assert.That(Vector3.Distance(b, c), Is.EqualTo(0.02f).Within(1e-4f), "width");
            Assert.That(a.y, Is.EqualTo(1.002f).Within(1e-4f), "lifted 2 mm along the normal");
            pool.Update(0f);
            Assert.IsFalse(pool.Renderer.enabled, "nothing added this frame: off again");
            yield return null;
        }
    }
}
