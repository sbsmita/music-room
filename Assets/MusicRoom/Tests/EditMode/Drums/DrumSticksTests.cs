// The sticks, case by case (spec 05 section 4) on scripted hand frames: pick-up within .12 m of a shaft, the .24 m dead
// zone, the nearest free stick, the grip pose (TILT droop, butt .13 behind the grip, tip and shoulder pointers), the
// open-hand release after .25 s (a pinch or a fist keeps it), the 1.5 s tracking-loss grace, the .4 s settle back onto
// the snare, the pick-up glow and ReleaseAll.
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class DrumSticksTests
    {
        const float Dt = 1f / 72f;

        static DrumSticks.GrabResult PinchAt(KitFixture k, HandState h, Vector3 at)
        {
            k.TrackHandAt(h, at, Quaternion.identity, pinched: true);
            return k.Sticks.TryGrab(h, at);
        }

        [Test]
        public void APinchWithinTwelveCentimetresOfAShaftPicksUpTheNearestFreeStick()
        {
            using var k = new KitFixture();
            Drumstick s0 = k.Sticks[0], s1 = k.Sticks[1];
            Assert.That(PinchAt(k, k.Right, s0.GripWorld + new Vector3(0f, 0.30f, 0f)), Is.EqualTo(DrumSticks.GrabResult.None), "30 cm away");
            Assert.That(PinchAt(k, k.Right, s0.GripWorld + new Vector3(0f, 0.15f, 0f)), Is.EqualTo(DrumSticks.GrabResult.Near), "a near miss is consumed");
            Assert.IsNull(k.Right.Holding);
            Assert.That(PinchAt(k, k.Right, s0.GripWorld + new Vector3(0f, 0.02f, 0f)), Is.EqualTo(DrumSticks.GrabResult.Grabbed));
            Assert.That(k.Right.Holding, Is.SameAs(s0));
            Assert.That(s0.Hand, Is.SameAs(k.Right));
            Assert.That(s0.Root.parent, Is.SameAs(k.Scene), "a held stick lives in the scene, keeping its world pose");
            Assert.IsTrue(s0.TipPointer.Down && s0.ShoulderPointer.Down);
            Assert.That(PinchAt(k, k.Right, s1.GripWorld), Is.EqualTo(DrumSticks.GrabResult.None), "a hand holds one stick");
            Assert.That(PinchAt(k, k.Left, s0.GripWorld + new Vector3(0f, 0.02f, 0f)), Is.EqualTo(DrumSticks.GrabResult.Grabbed), "the other, free stick");
            Assert.That(k.Left.Holding, Is.SameAs(s1));
            Assert.IsTrue(k.Sticks.AnyHeld);
            Assert.That(k.Kit.AnyStickHeld, Is.True);
            k.Kit.ReleaseAllSticks();
            Assert.IsFalse(k.Sticks.AnyHeld);
            Assert.IsNull(k.Right.Holding);
            Assert.IsNull(k.Left.Holding);
        }

        [Test]
        public void NoKitInTheRoomNoSticks()
        {
            using var k = new KitFixture();
            k.Root.SetActive(false);
            Assert.That(PinchAt(k, k.Right, k.Sticks[0].GripWorld), Is.EqualTo(DrumSticks.GrabResult.None));
        }

        [Test]
        public void AHeldStickFollowsTheGripWithItsDroopAndFeedsItsPointers()
        {
            using var k = new KitFixture();
            Drumstick s = k.Sticks[0];
            PinchAt(k, k.Right, s.GripWorld + new Vector3(0f, 0.02f, 0f));
            // Move the hand somewhere else, turned 30 degrees, and run the StickPoses step.
            Quaternion rot = Quaternion.Euler(20f, 30f, 0f);
            HandFrame f = k.TrackHandAt(k.Right, new Vector3(0.2f, 1.0f, 0.1f), rot, pinched: true);
            k.Sticks.UpdatePoses(Dt);
            DrumMath.StickGrip(f.position[(int)HandJoint.Wrist], f.rotation[(int)HandJoint.Wrist], f.position[(int)HandJoint.ThumbTip],
                f.position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
            DrumMath.StickPose(grip, axis, out Vector3 butt, out Quaternion srot, out Vector3 tip, out Vector3 shoulder);
            Assert.That(Vector3.Distance(s.Root.position, butt), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(s.Root.rotation, srot), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(s.TipPointer.Next, tip), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(s.ShoulderPointer.Next, shoulder), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(s.GripWorld, grip), Is.LessThan(1e-5f), "the stick sits in the hand at the pinch point");
            Assert.That(Vector3.Distance(s.TipWorld, tip), Is.LessThan(1e-5f));
            Vector3 fwd = (grip - f.position[(int)HandJoint.Wrist]).normalized;
            Assert.That(Vector3.Angle(fwd, axis), Is.EqualTo(12.605f).Within(0.01f), "it continues the hand's line with the 12.6 degree droop");
        }

        [Test]
        public void ItIsLetGoOnlyAfterTheHandStayedOpenForAQuarterSecond()
        {
            using var k = new KitFixture();
            Drumstick s = k.Sticks[0];
            PinchAt(k, k.Right, s.GripWorld + new Vector3(0f, 0.02f, 0f));
            Vector3 at = s.GripWorld + new Vector3(0f, 0.05f, 0f);
            k.TrackHandAt(k.Right, at, Quaternion.identity, pinched: false);   // thumb-index gap 11.5 cm: open
            for (int i = 0; i < 10; i++) k.Sticks.UpdateReleases(Dt);
            k.TrackHandAt(k.Right, at, Quaternion.identity, pinched: true);    // a pinch keeps it
            k.Sticks.UpdateReleases(Dt);
            Assert.That(s.OpenFor, Is.EqualTo(0f));
            k.TrackHandAt(k.Right, at, Quaternion.identity, pinched: false);
            k.Right.Grasping = true;                                           // so does a fist
            for (int i = 0; i < 40; i++) k.Sticks.UpdateReleases(Dt);
            Assert.IsTrue(s.Held);
            k.Right.Grasping = false;
            for (int i = 0; i < 17; i++) k.Sticks.UpdateReleases(Dt);
            Assert.IsTrue(s.Held, ".236 s open");
            k.Sticks.UpdateReleases(Dt);
            k.Sticks.UpdateReleases(Dt);
            Assert.IsFalse(s.Held, "open for more than .25 s");
            Assert.IsNull(k.Right.Holding);
            Assert.That(s.Root.parent, Is.SameAs(k.Root.transform), "back to the kit");
            Assert.IsFalse(s.TipPointer.Down || s.TipPointer.Active || s.ShoulderPointer.Down || s.ShoulderPointer.Active);
            Assert.That(s.Settle, Is.EqualTo(0f));
        }

        [Test]
        public void AReleasedStickSettlesOntoTheSnareInPointFourSeconds()
        {
            using var k = new KitFixture();
            Drumstick s = k.Sticks[1];
            PinchAt(k, k.Right, s.GripWorld + new Vector3(0.01f, 0.02f, 0f));
            Assert.That(k.Right.Holding, Is.SameAs(s));
            k.TrackHandAt(k.Right, new Vector3(0.3f, 1.1f, 0.0f), Quaternion.Euler(0f, 40f, 0f), pinched: true);
            k.Sticks.UpdatePoses(Dt);
            k.Sticks.Release(s);
            Vector3 away = s.Root.localPosition;
            Assert.That(Vector3.Distance(away, s.RestPos), Is.GreaterThan(0.1f));
            for (int i = 0; i < 4; i++) k.Sticks.UpdatePoses(Dt);
            // Each frame lerps by k = 1 - (1 - settle)^3: after 4 frames .90 x .81 x .72 x .64 = .33 of the way is left.
            float mid = Vector3.Distance(s.Root.localPosition, s.RestPos) / Vector3.Distance(away, s.RestPos);
            Assert.That(mid, Is.EqualTo(0.333f).Within(0.01f), "eased toward the rest pose");
            for (int i = 0; i < 25; i++) k.Sticks.UpdatePoses(Dt);
            Assert.That(Vector3.Distance(s.Root.localPosition, s.RestPos), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(s.Root.localRotation, s.RestRot), Is.LessThan(1e-3f));
            Assert.That(s.Settle, Is.EqualTo(1f));
        }

        [Test]
        public void TrackingLossFreezesTheStickAndDropsItAfterTheGrace()
        {
            using var k = new KitFixture();
            Drumstick s = k.Sticks[0];
            PinchAt(k, k.Right, s.GripWorld + new Vector3(0f, 0.02f, 0f));
            k.TrackHandAt(k.Right, new Vector3(0.1f, 1.0f, 0.2f), Quaternion.identity, pinched: true);
            k.Sticks.UpdatePoses(Dt);
            Vector3 frozen = s.Root.position;
            k.LoseHand(k.Right);
            for (int i = 0; i < 50; i++) k.Sticks.UpdatePoses(Dt);
            Assert.IsTrue(s.Held);
            Assert.IsFalse(s.TipPointer.Down || s.ShoulderPointer.Down, "the pointers go up: silent");
            Assert.That(s.Root.position, Is.EqualTo(frozen), "frozen in place");
            // Tracking returns inside the grace: it snaps to the hand.
            HandFrame f = k.TrackHandAt(k.Right, new Vector3(0.15f, 1.05f, 0.2f), Quaternion.identity, pinched: true);
            k.Sticks.UpdatePoses(Dt);
            Assert.IsTrue(s.TipPointer.Down);
            Assert.That(s.LostFor, Is.EqualTo(0f));
            DrumMath.StickGrip(f.position[(int)HandJoint.Wrist], f.rotation[(int)HandJoint.Wrist], f.position[(int)HandJoint.ThumbTip], f.position[(int)HandJoint.IndexTip], out Vector3 grip, out _);
            Assert.That(Vector3.Distance(s.GripWorld, grip), Is.LessThan(1e-5f));
            k.LoseHand(k.Right);
            for (int i = 0; i < 100; i++) k.Sticks.UpdatePoses(Dt);
            Assert.IsTrue(s.Held, "1.39 s lost");
            for (int i = 0; i < 12; i++) k.Sticks.UpdatePoses(Dt);
            Assert.IsFalse(s.Held, "dropped after 1.5 s");
            Assert.IsNull(k.Right.Holding);
        }

        [Test]
        public void AFingertipNearAFreeStickLightsItsGrip()
        {
            using var k = new KitFixture();
            var glow = new HoverGlowPool(new MaterialLibrary(), null, true);
            Drumstick s0 = k.Sticks[0];
            Pointer p = k.Right.Pointers[0];
            KitFixture.Rest(p, s0.GripWorld + new Vector3(0f, 0.05f, 0f));
            KitFixture.Rest(k.Right.Pointers[1], s0.GripWorld + new Vector3(0f, 0.08f, 0f));
            k.Sticks.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(1), "one glow per stick, the nearest fingertip's");
            var g = glow.Spec(0);
            float d = k.Sticks.ShaftDistance(s0, p.Tip);
            Assert.That(Vector3.Distance(g.Position, s0.GripWorld + new Vector3(0f, 0.014f, 0f)), Is.LessThan(1e-5f));
            Assert.That(g.Normal, Is.EqualTo(Vector3.up));
            Assert.That(Vector3.Angle(g.Axis, s0.Root.up), Is.LessThan(1e-3f));
            Assert.That(g.Length, Is.EqualTo(0.12f));
            Assert.That(g.Width, Is.EqualTo(0.035f));
            Assert.That(g.Strength, Is.EqualTo(1f - d / 0.14f).Within(1e-5f));
            glow.Update(0f);

            KitFixture.Idle(k.Right.Pointers[1]);
            KitFixture.Rest(p, s0.GripWorld + new Vector3(0f, 0.2f, 0f));
            k.Sticks.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "beyond .14 m");

            // The desktop mouse hand cannot pick a stick up (Layout grabs only while presenting): it gets no pick-up glow.
            KitFixture.Rest(p, s0.GripWorld + new Vector3(0f, 0.05f, 0f));
            k.Right.IsMouse = true;
            k.Sticks.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(0), "no glow for the mouse hand");
            k.Right.IsMouse = false;

            KitFixture.Rest(p, s0.GripWorld + new Vector3(0f, 0.05f, 0f));
            PinchAt(k, k.Left, s0.GripWorld + new Vector3(0f, 0.02f, 0f));
            k.Sticks.Hover(k.Pointers, glow);
            Assert.That(glow.Pending, Is.EqualTo(1));
            Assert.That(Vector3.Distance(glow.Spec(0).Position, k.Sticks[1].GripWorld + new Vector3(0f, 0.014f, 0f)), Is.LessThan(1e-5f), "a held stick never glows; the free one does");
        }
    }
}
