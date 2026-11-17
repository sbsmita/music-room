// The grab pinch through the real input path (HandInput.Step -> PointerInput -> HandState), scripted with FakeHandSource
// at 72 Hz: Meta's index pinch (pinch strength with hysteresis 0.85 / 0.5, or IsIndexPinching) OR the thumb-index rule,
// with its own edges; the pinch data dropping out (InputStateValid off) is held for 0.25 s and then ends the pinch; the
// OS system gesture freezes it like the other pinch state; the palm-toward-the-head test (PointerInput.SystemGestureLike).
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class MetaPinchInputTests
    {
        const float DT = 1f / 72f;
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(manualFrames: true);
            _boot.Options.ModuleFilter = t => false;   // the input path only
            yield return _boot.Start();
        }

        [UnityTearDown] public IEnumerator TearDown() => _boot.Stop();

        static readonly Vector3 At = new Vector3(0.1f, 1.0f, 0.35f);
        static HandPose Open() => HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.identity).WithIndexTipAt(At);

        [UnityTest]
        public IEnumerator MetaPinchOrTheThumbIndexRuleMakeTheGrabPinch()
        {
            HandState h = _boot.App.Input.Hand(Handedness.Right);
            HandInput input = _boot.App.HandInput;
            _boot.Hands.SetPose(HandSide.Right, Open());
            _boot.Step(DT, 3);
            Assert.IsFalse(h.GrabPinching);
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.9f);
            _boot.Step(DT);
            Assert.IsTrue(input.grabPinchStart[1], "the start edge");
            Assert.IsTrue(h.GrabPinching && h.MetaPinching && !h.Pinching, "Meta's pinch alone");
            Assert.That(h.PinchStrength, Is.EqualTo(0.9f));
            _boot.Step(DT, 9);
            Assert.That(h.GrabPinchFor, Is.EqualTo(10 * DT).Within(1e-4f), "its clock");
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.55f);
            _boot.Step(DT);
            Assert.IsTrue(h.GrabPinching, "hysteresis: 0.55 holds");
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.3f);
            _boot.Step(DT);
            Assert.IsTrue(input.grabPinchEnd[1], "the end edge");
            Assert.IsFalse(h.GrabPinching);
            // The thumb-index rule alone still makes one.
            _boot.Hands.SetPose(HandSide.Right, Open().WithPinch(0f));
            _boot.Step(DT);
            Assert.IsTrue(h.Pinching && h.GrabPinching && !h.MetaPinching);
            // Both, then the geometric one opens while Meta's holds: one pinch throughout, no extra edges.
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.95f);
            _boot.Step(DT);
            Assert.IsFalse(input.grabPinchStart[1]);
            _boot.Hands.SetPose(HandSide.Right, Open());
            _boot.Step(DT);
            Assert.IsFalse(h.Pinching);
            Assert.IsTrue(h.GrabPinching);
            Assert.IsFalse(input.grabPinchEnd[1]);
            // The runtime's IsIndexPinching flag counts on its own.
            _boot.Hands.SetMetaPinch(HandSide.Right, 0f);
            _boot.Step(DT);
            Assert.IsFalse(h.GrabPinching);
            _boot.Hands.SetMetaPinch(HandSide.Right, 0f, runtimePinch: true);
            _boot.Step(DT);
            Assert.IsTrue(h.GrabPinching, "IsIndexPinching");
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnusablePinchDataIsHeldBrieflyThenEndsThePinch()
        {
            HandState h = _boot.App.Input.Hand(Handedness.Right);
            _boot.Hands.SetPose(HandSide.Right, Open());
            _boot.Hands.SetMetaPinch(HandSide.Right, 1f);
            _boot.Step(DT, 2);
            Assert.IsTrue(h.GrabPinching);
            _boot.Hands.SetInputValid(HandSide.Right, false);
            _boot.Step(DT, 10);   // 0.14 s without pinch data
            Assert.IsTrue(h.GrabPinching, "a short drop-out is held");
            _boot.Step(DT, 10);   // 0.28 s
            Assert.IsFalse(h.GrabPinching, "a long one ends it");
            _boot.Hands.SetInputValid(HandSide.Right, true);
            _boot.Step(DT);
            Assert.IsTrue(h.GrabPinching, "back with the data");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSystemGestureFreezesItAndThePalmTowardTheHeadIsRecognised()
        {
            HandState h = _boot.App.Input.Hand(Handedness.Right);
            Vector3 head = _boot.App.Head.position, fwd = _boot.App.Head.forward;
            _boot.Hands.SetPose(HandSide.Right, Open());
            _boot.Step(DT);
            _boot.Hands.SystemGesture = true;
            _boot.Hands.SetMetaPinch(HandSide.Right, 1f);
            _boot.Hands.SetPose(HandSide.Right, Open().WithPinch(0f));
            _boot.Step(DT, 5);
            Assert.IsFalse(h.GrabPinching, "a pinch during the OS gesture never starts");
            Assert.IsTrue(_boot.App.Input.SystemGestureLike(h, head, fwd), "the flag counts as the system gesture");
            _boot.Hands.SystemGesture = false;
            _boot.Step(DT);
            Assert.IsTrue(h.GrabPinching);
            Assert.IsFalse(_boot.App.Input.SystemGestureLike(h, head, fwd), "palm down: not the system gesture");
            // Palm turned toward the head (no flag yet): recognised by the pose itself.
            Vector3 palm = head + new Vector3(0f, -0.25f, 0.3f);
            Vector3 toHead = (head - palm).normalized;
            var facing = HandPose.OpenFacing(HandSide.Right, palm, Vector3.ProjectOnPlane(Vector3.up, toHead).normalized, toHead);
            _boot.Hands.SetPose(HandSide.Right, facing);
            _boot.Step(DT);
            Assert.IsTrue(_boot.App.Input.SystemGestureLike(h, head, fwd), "the palm faces the headset");
            // A palm-down hand held above the head faces the eyes too, but it is not the gesture (it faces down, not back).
            _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, head + new Vector3(0f, 0.3f, 0.2f), Quaternion.identity));
            _boot.Step(DT);
            Assert.IsFalse(_boot.App.Input.SystemGestureLike(h, head, fwd), "a palm down above the head");
            yield return null;
        }
    }
}
