// The summon gesture (spec 08 section 1.1) and its acceptance cases (spec 08 section 9, tutorial-check.mjs "the summon
// gesture"): an open palm facing the head from 0.3 m toggles once after 0.5 s; pinching, playing, a fist, the back of
// the hand, a hand below the waist, a pinch into the system menu never do (the bare system-gesture flag, which Quest
// raises for this very pose, does not block it); it re-arms after a break; the left hand mirrors.
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class SummonGestureTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, -0.1f);

        static HandFrame Frame(HandSide side, in HandPose pose, bool systemGesture = false)
        {
            var f = new HandFrame(side) { isTracked = true, systemGesture = systemGesture };
            for (int i = 0; i < HandFrame.JointCount; i++) { f.position[i] = pose.Position[i]; f.rotation[i] = pose.Rotation[i]; }
            return f;
        }

        /// <summary>A hand raised 0.3 m in front of the face, fingers up, palm toward the head.</summary>
        static HandPose Raised(HandSide side, float x = 0.05f, float dy = -0.15f, float dz = 0.3f)
        {
            Vector3 wrist = Head + new Vector3(x, dy, dz);
            Vector3 toHead = (Head - (wrist + new Vector3(0f, 0.07f, 0f))).normalized;
            return HandPose.OpenFacing(side, wrist, Vector3.up, toHead);
        }

        static SummonSample S(HandFrame f, bool pinching = false, bool holding = false, bool played = false) =>
            SummonSample.FromFrame(f, pinching, holding, played);

        static readonly SummonSample None = default;

        [Test]
        public void AnOpenPalmTowardTheFaceTogglesOnceAfterHalfASecond()
        {
            var g = new SummonGesture();
            var right = S(Frame(HandSide.Right, Raised(HandSide.Right)));
            Assert.IsTrue(SummonGesture.Qualifies(right, Head), "the raised palm qualifies");
            Assert.That(g.Step(None, right, Head, 0.1f), Is.EqualTo(-1));
            Assert.That(g.Hand, Is.EqualTo(1), "charging");
            Assert.IsTrue(g.RingVisible);
            Assert.That(g.RingOpacity, Is.GreaterThan(0.15f).And.LessThan(0.8f));
            float op = g.RingOpacity;
            g.Step(None, right, Head, 0.1f);
            Assert.That(g.RingOpacity, Is.GreaterThan(op), "the ring brightens");
            Assert.That(g.RingScale, Is.GreaterThan(0.5f));
            g.Step(None, right, Head, 0.1f);
            Assert.That(g.Step(None, right, Head, 0.1f), Is.EqualTo(-1), "not yet at 0.4 s");
            Assert.That(g.Step(None, right, Head, 0.11f), Is.EqualTo(1), "fires at 0.5 s");
            Assert.IsTrue(g.Done);
            Assert.IsFalse(g.RingVisible);
            for (int i = 0; i < 10; i++) Assert.That(g.Step(None, right, Head, 0.1f), Is.EqualTo(-1), "a held palm toggles once");
            Assert.IsFalse(g.RingVisible, "no ring once done");
        }

        [Test]
        public void TheRingSitsOnThePalmFacingTheHead()
        {
            var g = new SummonGesture();
            var f = Frame(HandSide.Right, Raised(HandSide.Right));
            var s = S(f);
            HandMath.Palm(f, out Vector3 c, out Vector3 n);
            g.Step(None, s, Head, 0.25f);
            Assert.That(Vector3.Distance(g.RingPosition, c + 0.03f * n), Is.LessThan(1e-5f), "3 cm out of the palm");
            Vector3 toHead = (Head - g.RingPosition).normalized;
            Assert.That(Vector3.Dot(g.RingRotation * Vector3.forward, toHead), Is.GreaterThan(0.9999f));
            Assert.That(g.RingScale, Is.EqualTo(0.75f).Within(1e-5f), "0.5 + 0.5k at k = .5");
            Assert.That(g.RingOpacity, Is.EqualTo(0.475f).Within(1e-5f), "0.15 + 0.65k");
        }

        [Test]
        public void ItReArmsAfterABreakAndTheSameGestureHidesAgain()
        {
            var g = new SummonGesture();
            var palm = S(Frame(HandSide.Right, Raised(HandSide.Right)));
            for (int i = 0; i < 6; i++) g.Step(None, palm, Head, 0.1f);
            Assert.IsTrue(g.Done);
            // The back of the hand toward the face breaks the gesture...
            var back = S(Frame(HandSide.Right, HandPose.OpenFacing(HandSide.Right, Head + new Vector3(0.05f, -0.15f, 0.3f), Vector3.up, Vector3.forward)));
            Assert.IsFalse(SummonGesture.Qualifies(back, Head));
            g.Step(None, back, Head, 0.1f);
            Assert.That(g.Hand, Is.EqualTo(-1));
            Assert.IsFalse(g.RingVisible);
            // ...and the palm again charges afresh and fires again (the panel toggles closed).
            g.Step(None, palm, Head, 0.1f);
            Assert.IsFalse(g.Done);
            int fired = -1;
            for (int i = 0; i < 6 && fired < 0; i++) fired = g.Step(None, palm, Head, 0.1f);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void PinchingPlayingHoldingFistLowHandAndSystemGestureNeverCount()
        {
            var pose = Raised(HandSide.Right);
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose), pinching: true), Head), "pinching");
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose), played: true), Head), "played within 400 ms");
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose), holding: true), Head), "holding a stick");
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose.WithFist())), Head), "a fist");
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose.WithPointing())), Head), "the pointing (mouse) hand: grasp .139 < .15");
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose.Translated(new Vector3(0f, -0.6f, 0f)))), Head), "below the waist (> .45 m under the eyes)");
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose.WithPinch(), systemGesture: true)), Head), "a pinch into the system menu");
            Assert.IsTrue(SummonGesture.Qualifies(S(Frame(HandSide.Right, pose, systemGesture: true)), Head),
                "Quest flags the system gesture for any open palm facing the headset: that alone must still summon");
            var palmDown = HandPose.OpenFacing(HandSide.Right, Head + new Vector3(0.05f, -0.3f, 0.3f), Vector3.forward, Vector3.down);
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, palmDown)), Head), "palm down");
            var untracked = new HandFrame(HandSide.Right);
            Assert.IsFalse(SummonGesture.Qualifies(S(untracked), Head), "untracked");
            // None of them charges over a full second.
            var g = new SummonGesture();
            for (int i = 0; i < 12; i++) Assert.That(g.Step(None, S(Frame(HandSide.Right, pose), pinching: true), Head, 0.1f), Is.EqualTo(-1));
            Assert.IsFalse(g.RingVisible);
        }

        [Test]
        public void DistanceAndTheFortyDegreeConeBound()
        {
            Assert.IsFalse(SummonGesture.Qualifies(S(Frame(HandSide.Right, Raised(HandSide.Right, 0.05f, -0.15f, 0.85f))), Head), "farther than 0.8 m");
            var near = new SummonSample { Eligible = true, HasPalm = true, Center = Head + new Vector3(0f, 0f, 0.07f), Normal = Vector3.back, Grasp = 0.17f };
            Assert.IsFalse(SummonGesture.Qualifies(near, Head), "closer than 8 cm");
            var ok = near; ok.Center = Head + new Vector3(0f, -0.1f, 0.3f);
            Vector3 toHead = (Head - ok.Center).normalized;
            ok.Normal = Quaternion.AngleAxis(35f, Vector3.up) * toHead;
            Assert.IsTrue(SummonGesture.Qualifies(ok, Head), "35 degrees off: inside the cone");
            ok.Normal = Quaternion.AngleAxis(45f, Vector3.up) * toHead;
            Assert.IsFalse(SummonGesture.Qualifies(ok, Head), "45 degrees off: outside");
            ok.Normal = toHead; ok.Grasp = 0.149f;
            Assert.IsFalse(SummonGesture.Qualifies(ok, Head), "grasp under 0.15: not open");
            ok.Grasp = float.NaN;
            Assert.IsFalse(SummonGesture.Qualifies(ok, Head));
        }

        [Test]
        public void TheLeftHandMirrorsAndTheFirstQualifyingHandWins()
        {
            var left = S(Frame(HandSide.Left, Raised(HandSide.Left, -0.05f)));
            Assert.IsTrue(SummonGesture.Qualifies(left, Head), "a left palm toward the face charges");
            var right = S(Frame(HandSide.Right, Raised(HandSide.Right)));
            var g = new SummonGesture();
            g.Step(left, right, Head, 0.1f);
            Assert.That(g.Hand, Is.EqualTo(0), "hands in order: left first");
            int fired = -1;
            for (int i = 0; i < 6 && fired < 0; i++) fired = g.Step(left, right, Head, 0.1f);
            Assert.That(fired, Is.EqualTo(0));
            // A different charging hand resets the charge.
            g.Step(None, right, Head, 0.1f);
            Assert.That(g.Hand, Is.EqualTo(1));
            Assert.IsFalse(g.Done);
            Assert.That(g.T, Is.EqualTo(0.1f).Within(1e-6f));
            // No hand clears it.
            g.Step(None, None, Head, 0.1f);
            Assert.That(g.Hand, Is.EqualTo(-1));
            Assert.IsFalse(g.RingVisible);
        }

        [Test]
        public void PauseKeepsTheChargeAndResetClearsIt()
        {
            var g = new SummonGesture();
            var right = S(Frame(HandSide.Right, Raised(HandSide.Right)));
            g.Step(None, right, Head, 0.2f);
            g.Pause();
            Assert.IsFalse(g.RingVisible, "held panel: no ring");
            Assert.That(g.T, Is.EqualTo(0.2f).Within(1e-6f));
            g.Reset();
            Assert.That(g.Hand, Is.EqualTo(-1));
            Assert.That(g.T, Is.EqualTo(0f));
        }
    }
}
