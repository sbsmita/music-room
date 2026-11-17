using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Hands
{
    public class HandMathTests
    {
        [Test]
        public void PinchHysteresisIsTheReferenceRule()
        {
            bool p = false;
            p = HandMath.NextPinch(p, 0.020f); Assert.IsFalse(p, "20 mm does not start");
            p = HandMath.NextPinch(p, 0.0149f); Assert.IsTrue(p, "<= 15 mm starts");
            p = HandMath.NextPinch(p, 0.020f); Assert.IsTrue(p, "20 mm keeps a pinch");
            p = HandMath.NextPinch(p, 0.0251f); Assert.IsFalse(p, "> 25 mm ends");
        }

        [Test]
        public void GraspHysteresisUsesTheReferenceThresholds()
        {
            bool g = false;
            g = HandMath.NextGrasp(g, 0.12f); Assert.IsFalse(g);
            g = HandMath.NextGrasp(g, 0.104f); Assert.IsTrue(g, "< 0.105 closes");
            g = HandMath.NextGrasp(g, 0.125f); Assert.IsTrue(g, "stays until 0.13");
            g = HandMath.NextGrasp(g, 0.131f); Assert.IsFalse(g);
        }

        [Test]
        public void RestPoseValuesMatchTheJointTable()
        {
            // 02-meta-hands-joints.json derivedRestValues: open grasp 0.1765, open pinch gap 0.1148, pointing grasp 0.1392.
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            {
                var open = HandPose.Open(side, Vector3.zero, Quaternion.identity);
                var f = Frame(side, open);
                Assert.That(HandMath.GraspAmount(f), Is.EqualTo(0.1765f).Within(5e-4f), side + " open grasp");
                Assert.That(HandMath.PinchGap(f), Is.EqualTo(0.1148f).Within(5e-4f), side + " open pinch gap");
                Assert.IsTrue(HandMath.Palm(f, out _, out Vector3 n));
                Assert.That(n.y, Is.LessThan(-0.9f), side + " palm normal points out of the palm (down for a palm-down hand)");
                Assert.That(HandMath.YawDegrees(f), Is.EqualTo(0f).Within(1e-3f));
                var pointing = Frame(side, open.WithPointing());
                Assert.That(HandMath.GraspAmount(pointing), Is.EqualTo(0.1392f).Within(5e-4f), side + " pointing grasp");
                var fist = Frame(side, open.WithFist());
                Assert.That(HandMath.GraspAmount(fist), Is.LessThan(HandMath.GraspClose), side + " fist is a grasp");
                var pinch = Frame(side, open.WithPinch());
                Assert.That(HandMath.PinchGap(pinch), Is.LessThan(HandMath.PinchStartGap), side + " pinch pose pinches");
            }
        }

        [Test]
        public void PalmNormalFollowsTheWristRotation()
        {
            // Palm facing +z (toward a head in front of the hand): fingers up, palm normal = +z.
            var pose = HandPose.OpenFacing(HandSide.Right, new Vector3(0f, 1.1f, 0.3f), Vector3.up, Vector3.forward);
            var f = Frame(HandSide.Right, pose);
            Assert.IsTrue(HandMath.Palm(f, out Vector3 c, out Vector3 n));
            Assert.That(Vector3.Dot(n, Vector3.forward), Is.GreaterThan(0.9f));
            Assert.That(c.y, Is.GreaterThan(1.1f));
        }

        static HandFrame Frame(HandSide side, HandPose pose)
        {
            var f = new HandFrame(side);
            for (int i = 0; i < HandFrame.JointCount; i++) { f.position[i] = pose.Position[i]; f.rotation[i] = pose.Rotation[i]; }
            f.isTracked = true;
            return f;
        }
    }

    public class SampleVelocityTests
    {
        [Test]
        public void VelocityUsesTheTrackerInterval()
        {
            var sv = new SampleVelocity();
            var tips = new Vector3[5];
            var vel = new Vector3[5];
            float interval = 0f;
            Assert.IsTrue(sv.Push(10.0, tips, vel, ref interval));
            tips[1] = new Vector3(0, 0, 0.01f);
            Assert.IsFalse(sv.Push(10.0, tips, vel, ref interval), "same timestamp is not a new sample");
            Assert.IsTrue(sv.Push(10.016, tips, vel, ref interval));
            Assert.That(vel[1].z, Is.EqualTo(0.625f).Within(1e-3f), "1 cm over 16 ms");
            Assert.That(interval, Is.EqualTo(0.016f).Within(1e-4f));
            Assert.IsTrue(sv.Push(10.5, tips, vel, ref interval), "gap > 0.2 s restarts");
            Assert.That(vel[1].magnitude, Is.EqualTo(0f).Within(1e-6f));
        }
    }

    public class FakeHandSourceTests
    {
        [Test]
        public void ScriptedJointsProduceTrackedFramesAndVelocities()
        {
            var fake = new FakeHandSource();
            var pose = HandPose.Open(HandSide.Right, new Vector3(0.2f, 1.0f, 0.3f), Quaternion.identity);
            fake.SetPose(HandSide.Right, pose);
            fake.Tick(1f / 72f);
            var r = fake.Get(HandSide.Right);
            Assert.IsTrue(r.isTracked);
            Assert.IsFalse(fake.Get(HandSide.Left).isTracked);
            Assert.That(r.Get(HandJoint.Wrist), Is.EqualTo(new Vector3(0.2f, 1.0f, 0.3f)));
            fake.SetPose(HandSide.Right, pose.Translated(new Vector3(0f, -0.01f, 0f)));
            fake.Tick(1f / 72f);
            Assert.That(r.tipVelocity[1].y, Is.EqualTo(-0.72f).Within(1e-3f), "1 cm down in one 72 Hz frame");
            Assert.IsTrue(r.newSample);
            fake.SetTracked(HandSide.Right, false);
            fake.Tick(1f / 72f);
            Assert.IsFalse(r.isTracked);
            Assert.That(r.tipVelocity[1], Is.EqualTo(Vector3.zero));
            Assert.That(r.Get(HandJoint.Wrist).y, Is.EqualTo(0.99f).Within(1e-4f), "positions are kept while lost");
        }

        [Test]
        public void WithIndexTipAtMovesTheWholeHand()
        {
            var pose = HandPose.Open(HandSide.Left, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(1f, 2f, 3f));
            Assert.That(pose.Position[(int)HandJoint.IndexTip], Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(Vector3.Distance(pose.Position[(int)HandJoint.Wrist], pose.Position[(int)HandJoint.IndexTip]),
                Is.EqualTo(HandRestPose.Left[(int)HandJoint.IndexTip].magnitude).Within(1e-5f));
        }
    }
}
