// The seat gestures read the grab pinch (HandState.GrabPinch*: the thumb-index rule OR Meta's own index pinch, the
// u-arrange fallback). On the headset the OpenXR fingertip joint centres sit about 17 mm apart when the tips touch, above
// the rule's 15 mm start, so the rule alone flickered: a pinch started late and a flicker ended the swivel. Scripted with
// FakeHandSource through the real input path: Meta's pinch alone takes the swivel and snaps the seat, a flickering joint
// gap under it does not end it, two Meta pinches summon, and a pinch made like the OS system gesture (the runtime's flag,
// or the palm turned back toward the headset before the flag rises) neither turns the seat nor counts for the summon.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class GrabPinchGestureTests : LayoutPlayTestBase
    {
        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        /// <summary>A point in empty air in front of the head, above every instrument's zone (SeatGestureTests).</summary>
        Vector3 Air(float x) => Head.position + new Vector3(x, 0.3f, 0.25f);

        /// <summary>An open hand at `tip` whose palm is turned back toward the eyes (the OS system gesture's pose).</summary>
        HandPose FacingTheHead(HandSide side, Vector3 tip)
        {
            HandPose probe = OpenAt(side, tip);
            Vector3 toHead = (Head.position - tip).normalized;
            Vector3 fingers = Vector3.ProjectOnPlane(Vector3.up, toHead).normalized;
            HandPose p = HandPose.OpenFacing(side, probe.Position[(int)HandJoint.Wrist], fingers, toHead);
            return p.Translated(tip - p.Position[(int)HandJoint.IndexTip]);
        }

        [UnityTest]
        public IEnumerator MetaPinchAloneTakesTheSwivelAndAFlickeringJointGapDoesNotEndIt()
        {
            Vector3 p = Air(0f);
            Assert.IsFalse(Layout.NearInstrument(p), "empty air");
            float yaw0 = Head.eulerAngles.y;
            HandState right = Hand(HandSide.Right);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, p));
            yield return Frame();
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            yield return Frame();
            Assert.IsFalse(right.Pinching, "the thumb-index rule sees no pinch (fingers apart)");
            Assert.IsTrue(right.GrabPinching);
            Assert.That(Layout.SwivelHand, Is.SameAs(right), "Meta's pinch took the swivel");
            // The joint gap flickers across the rule's thresholds every frame while Meta's pinch holds: one swivel throughout.
            Vector3 r = Head.right;
            for (int i = 1; i <= 10 && Layout.SnapTurns == 0; i++)
            {
                Vector3 at = p + r * (0.016f * i);
                Fake.SetPose(HandSide.Right, i % 2 == 0 ? PinchAt(HandSide.Right, at) : OpenAt(HandSide.Right, at));
                yield return Frame();
                if (Layout.SnapTurns == 0) Assert.That(Layout.SwivelHand, Is.SameAs(right), $"frame {i}: still the swivel");
            }
            Assert.That(Layout.SnapTurns, Is.EqualTo(1), "past 14 cm: one snap");
            Assert.That(Mathf.DeltaAngle(yaw0, Head.eulerAngles.y), Is.EqualTo(-45f).Within(0.01f));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, p));
            Fake.SetMetaPinch(HandSide.Right, 0.2f);
            yield return Frame();
            Assert.IsNull(Layout.SwivelHand, "it ends when the grab pinch parts");
        }

        [UnityTest]
        public IEnumerator TwoMetaPinchesInTheAirSummonEverything()
        {
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            yield return Frame();
            Fake.SetMetaPinch(HandSide.Left, 0.95f);
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            int frames = 0;
            while (Layout.Summons == 0 && frames < 80) { yield return Frame(); frames++; }
            Assert.IsFalse(Hand(HandSide.Left).Pinching || Hand(HandSide.Right).Pinching, "no thumb-index pinch at all");
            Assert.That(Layout.Summons, Is.EqualTo(1));
            Assert.That(frames * DT, Is.EqualTo(GrabConstants.TwoHandSummonSeconds).Within(2f * DT), "after the hold (1 s)");
        }

        [UnityTest]
        public IEnumerator APinchMadeLikeTheSystemGestureNeitherTurnsTheSeatNorSummons()
        {
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            // 1. The runtime's flag (SystemGestureInProgress): the pinch state freezes, nothing starts.
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            yield return Frame();
            Fake.SystemGesture = true;
            Fake.SetMetaPinch(HandSide.Left, 0.95f);
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            yield return Frames(80);
            Assert.IsNull(Layout.SwivelHand, "the flag: no swivel");
            Assert.That(Layout.Summons, Is.Zero, "the flag: no summon");
            Fake.SystemGesture = false;
            Fake.SetMetaPinch(HandSide.Left, 0f);
            Fake.SetMetaPinch(HandSide.Right, 0f);
            yield return Frames(3);

            // 2. The palm already turned back toward the headset, before the flag rises.
            Fake.SetPose(HandSide.Left, FacingTheHead(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, FacingTheHead(HandSide.Right, r));
            yield return Frame();
            Assert.IsTrue(App.Input.SystemGestureLike(Hand(HandSide.Right), Head.position, Head.forward), "the pose reads as the system gesture");
            Assert.IsTrue(App.Input.SystemGestureLike(Hand(HandSide.Left), Head.position, Head.forward));
            Assert.IsFalse(Layout.NearInstrument(Hand(HandSide.Right).Pointers[0].Tip), "in empty air");
            Fake.SetMetaPinch(HandSide.Left, 0.95f);
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            yield return Frame();
            Assert.IsTrue(Hand(HandSide.Right).GrabPinching, "a pinch");
            yield return Frames(80);
            Assert.IsNull(Layout.SwivelHand, "a system-gesture pinch never takes the swivel");
            Assert.That(Layout.SnapTurns, Is.Zero);
            Assert.That(Layout.Summons, Is.Zero, "nor counts for the summon");

            // The same pinches with the palms turned down again count for the summon (only the gesture is left out).
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            int frames = 0;
            while (Layout.Summons == 0 && frames < 80) { yield return Frame(); frames++; }
            Assert.That(Layout.Summons, Is.EqualTo(1));
        }
    }
}
