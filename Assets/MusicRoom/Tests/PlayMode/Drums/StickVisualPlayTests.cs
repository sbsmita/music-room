// E2 of 10-upgrade/experience.md section 4 in the running app (TestBoot, simulated headset, manual frames, the real
// Layout makes the pick-up call): a scripted stick stroke through the snare plays exactly one Drum(Snare), unchanged by
// the drawn stick, and the stick's Visual child tip never goes below TipRadius over the head while phiC < 0.30; it springs
// back off the hit; the held stick's drawn tip casts a contact shadow on the head (E3); the kit's own pieces are contact
// surfaces (E1: a fingertip in the snare head is lifted); and the stick visuals allocate nothing.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Drums
{
    public class StickVisualPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        DrumsModule _mod;
        DrumKit _kit;
        AudioTestTap _tap;
        readonly List<TakeEvent> _events = new List<TakeEvent>();

        IEnumerator Boot()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is DrumsModule d) _mod = d;
            Assert.That(_mod, Is.Not.Null);
            _kit = _mod.Kit;
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            yield return null;
            var layout = _app.Get<ILayoutService>();
            for (int i = 0; i < 10 && !layout.FirstArrangeDone; i++) _boot.Step(Dt);
            Assert.IsTrue(layout.FirstArrangeDone);
            _tap = new AudioTestTap();
            _app.Get<IAudioEngine>().TestTap = _tap;
            _events.Clear();
            _app.Events.Subscribe<RecorderEvent>(e => { if (!e.DuringPlayback) _events.Add(e.Event); });
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var engine = _app?.Get<IAudioEngine>();
            if (engine != null) engine.TestTap = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
        }

        HandState Right => _app.Input.Hand(Handedness.Right);
        void Pose(in HandPose p) => _boot.Hands.SetPose(HandSide.Right, p);

        static HandPose PoseAt(Vector3 indexTip, Quaternion rot, bool pinched)
        {
            var p = HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing();
            if (pinched) p = p.WithPinch(0f);
            return p.WithIndexTipAt(indexTip);
        }

        static Vector3 StickTipFor(in HandPose p)
        {
            DrumMath.StickGrip(p.Position[(int)HandJoint.Wrist], p.Rotation[(int)HandJoint.Wrist], p.Position[(int)HandJoint.ThumbTip],
                p.Position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
            DrumMath.StickPose(grip, axis, out _, out _, out Vector3 tip, out _);
            return tip;
        }

        static HandPose WithStickTipAt(in HandPose p, Vector3 tip) => p.Translated(tip - StickTipFor(p));

        Drumstick PickUp(Quaternion rot)
        {
            Drumstick s = _mod.Sticks[1];
            Vector3 at = s.GripWorld + Vector3.up * 0.02f;
            Pose(PoseAt(at, rot, false));
            _boot.Step(Dt, 2);
            Pose(PoseAt(at, rot, true));
            _boot.Step(Dt);
            Assert.That(Right.Holding, Is.SameAs(s), "the pinch picked up a stick");
            return s;
        }

        int Count(string type, string kind) { int n = 0; foreach (var e in _events) if (e.type == type && e.kind == kind) n++; return n; }

        [UnityTest]
        public IEnumerator AStrokeThroughTheSnarePlaysOnceAndTheDrawnStickNeverEntersTheHead()
        {
            yield return Boot();
            Quaternion rot = Quaternion.LookRotation(_kit.transform.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), Vector3.up);
            Drumstick stick = PickUp(rot);
            Assert.That(stick.Visual, Is.Not.Null);
            DrumPiece snare = _kit.Piece(DrumSpec.SnareId);
            HandPose hold = PoseAt(Vector3.zero, rot, true);
            Vector3 up = snare.Group.up;
            // Down at 1.44 m/s (2 cm per 1/72 s), 4 cm past the head (the hand overshoots), held, then back up.
            var heights = new List<float> { 0.10f, 0.08f, 0.06f, 0.04f, 0.02f, 0.0f, -0.02f, -0.04f, -0.04f, -0.04f, -0.03f, -0.02f, -0.01f, 0.0f, 0.01f, 0.03f, 0.05f, 0.07f, 0.09f, 0.10f, 0.10f, 0.10f };
            _tap.Clear();
            float minDrawn = float.MaxValue, maxPhi = 0f, maxBounce = 0f;
            int shadowFrames = 0;
            bool hit = false;
            for (int i = 0; i < heights.Count; i++)
            {
                Pose(WithStickTipAt(hold, snare.Group.TransformPoint(new Vector3(0.02f, heights[i], 0.02f))));
                _boot.Step(Dt);
                float raw = Vector3.Dot(up, stick.TipWorld - snare.Group.position);
                // the drawn tip over the DRAWN head (1 mm above the strike plane the hit test uses)
                float drawn = Vector3.Dot(up, stick.VisualTipWorld - snare.Group.position) - DrumSpec.HeadLift;
                Assert.That(raw, Is.EqualTo(heights[i]).Within(2e-3f), $"f{i}: the raw stick follows the hand");
                if (stick.PhiC < StickVisualMath.FadeFrom)
                    Assert.That(drawn, Is.GreaterThanOrEqualTo(StickSpec.TipRadius - 2e-4f), $"f{i}: raw {raw:F3} -> drawn {drawn:F4} (phiC {stick.PhiC:F3})");
                if (drawn < minDrawn) minDrawn = drawn;
                maxPhi = Mathf.Max(maxPhi, stick.Phi);
                if (hit) maxBounce = Mathf.Max(maxBounce, stick.Phi - stick.PhiC);   // the rebound on top of the clamp (w = 1 here)
                if (!hit && Count(TakeEventType.Drum, "snare") == 1)
                {
                    hit = true;
                    Assert.That(drawn, Is.EqualTo(StickSpec.TipRadius).Within(2e-4f), "the hit frame draws the stick on the head");
                }
                if (_app.Contacts.ShadowCount > 0) shadowFrames++;
            }
            Debug.Log($"[Drums] stroke: drawn tip min {minDrawn * 1000f:F1} mm, max phi {maxPhi * Mathf.Rad2Deg:F1} deg, rebound {maxBounce * Mathf.Rad2Deg:F1} deg, {shadowFrames} shadow frames");
            Assert.That(Count(TakeEventType.Drum, "snare"), Is.EqualTo(1), "exactly one snare hit");
            Assert.That(_tap.Count(AudioCall.Drum), Is.EqualTo(1));
            Assert.That(maxBounce, Is.GreaterThan(0.02f), "the drawn stick springs back off the head after the hit (rad)");
            Assert.That(shadowFrames, Is.GreaterThan(5), "the drawn tip shadows the head");
            Assert.That(maxPhi, Is.LessThanOrEqualTo(StickVisualMath.PhiMax + 1e-5f));
            // The bounce dies out: back to the raw pose.
            _boot.Step(Dt, 20);
            Assert.That(stick.Phi, Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(stick.VisualTipWorld, stick.TipWorld), Is.LessThan(5e-4f));
        }

        [UnityTest]
        public IEnumerator AStickOnTheRideRestsOnThePlateItselfAndItsShadowLiesOnTop()
        {
            // The plate's top is 6 mm above its strike plane on the bow (0.6 r) and 16 mm on the bell: the drawn tip ball must
            // stay on what is drawn, and the shadow must lie on the plate's top, not inside it (it used the y = 0 plane).
            yield return Boot();
            DrumPiece ride = _kit.Piece(DrumSpec.RideId);
            Transform plate = ride.Top;
            Quaternion rot = Quaternion.LookRotation(_kit.transform.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), Vector3.up);
            Drumstick stick = PickUp(rot);
            // the stroke as the snare's, relative to the ride's tilted plate (well away from its normal: the clamp's
            // near-normal fade is not what this test is about)
            Quaternion strike = Quaternion.LookRotation(plate.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), plate.up);
            HandPose hold = PoseAt(Vector3.zero, strike, true);
            foreach (float frac in new[] { 0.6f, 0.12f / ride.R })
            {
                float rr = ride.R * frac, top = DrumSpec.CymbalTopY(ride.R, rr);
                float minDrawn = float.MaxValue;
                bool shadowOnTop = false;
                foreach (float y in new[] { 0.06f, 0.04f, 0.02f, 0.01f, 0.004f, 0.002f, 0.002f, 0.002f })
                {
                    // raw tip ball y above the strike plane: it ends below the drawn top (the hand overshoots)
                    Pose(WithStickTipAt(hold, plate.TransformPoint(new Vector3(rr, y, 0f))));
                    _boot.Step(Dt);
                    Vector3 pu = plate.up;
                    float drawn = Vector3.Dot(pu, stick.VisualTipWorld - plate.position) - top;
                    float fromNormal = Vector3.Angle(stick.TipWorld - stick.GripWorld, -pu);
                    Assert.That(fromNormal, Is.GreaterThan(32f), "the stick is well away from the plate's normal");
                    if (stick.PhiC < StickVisualMath.FadeFrom) minDrawn = Mathf.Min(minDrawn, drawn);
                    var pool = _app.Contacts.Shadows;
                    for (int b = 0; b < pool.Count; b++)
                    {
                        Vector3 c = pool.Get(b).Centre;
                        Vector3 lc = plate.InverseTransformPoint(c);
                        if (Mathf.Abs(Mathf.Sqrt(lc.x * lc.x + lc.z * lc.z) - rr) > 0.02f) continue;
                        Assert.That(Vector3.Dot(pu, c - plate.position), Is.EqualTo(DrumSpec.CymbalTopY(ride.R, Mathf.Sqrt(lc.x * lc.x + lc.z * lc.z))).Within(1e-3f),
                            $"r {rr:F3}: the stick's shadow lies on the plate's top");
                        shadowOnTop = true;
                    }
                }
                Debug.Log($"[Drums] ride at r {rr * 1000f:F0} mm (top {top * 1000f:F1} mm): drawn tip ball min {minDrawn * 1000f:F2} mm over the plate's top");
                Assert.That(minDrawn, Is.GreaterThanOrEqualTo(StickSpec.TipRadius - 5e-4f), $"r {rr:F3}: the drawn tip ball never sinks into the plate");
                Assert.IsTrue(shadowOnTop, $"r {rr:F3}: a shadow on the plate");
                Pose(WithStickTipAt(hold, plate.TransformPoint(new Vector3(rr, 0.15f, 0f))));
                _boot.Step(Dt, 30);
            }
        }

        [UnityTest]
        public IEnumerator AFingertipInTheSnareHeadIsLiftedAndTheStickVisualsAllocateNothing()
        {
            yield return Boot();
            DrumPiece snare = _kit.Piece(DrumSpec.SnareId);
            Quaternion playing = Quaternion.LookRotation(_kit.transform.TransformDirection(new Vector3(0f, -0.7f, 1f).normalized), Vector3.up);
            _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, playing).WithPointing()
                .WithIndexTipAt(snare.Group.TransformPoint(new Vector3(-0.05f, 0.04f, 0f))));
            _boot.Step(Dt, 2);
            _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, playing).WithPointing()
                .WithIndexTipAt(snare.Group.TransformPoint(new Vector3(-0.05f, -0.006f, 0f))));
            _boot.Step(Dt, 2);
            Assert.That(_app.Contacts.Lift(HandSide.Left, Finger.Index), Is.GreaterThan(0.01f), "the drawn finger stops on the head");
            Assert.That(Vector3.Dot(_app.Contacts.LiftNormal(HandSide.Left, Finger.Index), snare.Group.up), Is.GreaterThan(0.999f));

            Quaternion rot = Quaternion.LookRotation(_kit.transform.forward, Vector3.up);
            Drumstick stick = PickUp(rot);
            HandPose hold = PoseAt(Vector3.zero, rot, true);
            Pose(WithStickTipAt(hold, snare.Group.TransformPoint(new Vector3(0.02f, -0.01f, 0.02f))));
            _boot.Step(Dt, 2);
            var spheres = new Vector4[4];
            Assert.That(_kit.StickShadowSpheres(spheres), Is.EqualTo(1));
            void Work()
            {
                for (int i = 0; i < 20; i++)
                {
                    _mod.Tick(FramePhase.StickHover, Dt);
                    _kit.QueryContact(stick.VisualTipWorld, 0.06f, 0.04f, out _);
                    _app.Contacts.Update(Dt);
                }
            }
            Work();
            Assert.That(() => { var a = new float[1024]; a[0] = 1f; }, Is.AllocatingGCMemory(), "calibration");
            Assert.That(Work, Is.Not.AllocatingGCMemory());
        }
    }
}
