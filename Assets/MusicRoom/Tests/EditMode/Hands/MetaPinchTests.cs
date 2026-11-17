// Meta's own index pinch beside the thumb-index rule (added after the first headset test): hysteresis 0.85 on / below 0.5
// off, the runtime's IsIndexPinching flag counts too; and the palm-toward-the-head test the system-gesture exclusion uses.
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Hands
{
    public class MetaPinchTests
    {
        [Test]
        public void MetaPinchStartsAtPoint85AndEndsBelowHalf()
        {
            bool p = false;
            p = HandMath.NextMetaPinch(p, 0.84f, false); Assert.IsFalse(p, "0.84 does not start");
            p = HandMath.NextMetaPinch(p, 0.85f, false); Assert.IsTrue(p, "0.85 starts");
            p = HandMath.NextMetaPinch(p, 0.6f, false); Assert.IsTrue(p, "0.6 keeps it");
            p = HandMath.NextMetaPinch(p, 0.5f, false); Assert.IsTrue(p, "0.5 still keeps it");
            p = HandMath.NextMetaPinch(p, 0.49f, false); Assert.IsFalse(p, "below 0.5 ends");
            p = HandMath.NextMetaPinch(p, 0.7f, false); Assert.IsFalse(p, "0.7 does not start again");
            p = HandMath.NextMetaPinch(p, 0.2f, true); Assert.IsTrue(p, "the runtime's IsIndexPinching flag starts it");
            p = HandMath.NextMetaPinch(p, 0.2f, true); Assert.IsTrue(p, "and keeps it");
            p = HandMath.NextMetaPinch(p, 0.2f, false); Assert.IsFalse(p);
            Assert.That(HandMath.MetaPinchStart, Is.EqualTo(0.85f));
            Assert.That(HandMath.MetaPinchEnd, Is.EqualTo(0.5f));
            // The geometric rule is unchanged: 15 mm on, 25 mm off.
            Assert.That(HandMath.PinchStartGap, Is.EqualTo(0.015f));
            Assert.That(HandMath.PinchEndGap, Is.EqualTo(0.025f));
        }

        [Test]
        public void TouchingFingertipJointsSitNearTheGeometricStartThreshold()
        {
            // Why the Meta pinch is needed: the OpenXR tip joints are inside the fingertips, so two touching tips are the sum
            // of their radii apart (HandRestPose.Radius: index tip 7.6 mm, thumb tip 9.8 mm), above the 15 mm start.
            float touching = HandRestPose.Radius[(int)HandJoint.IndexTip] + HandRestPose.Radius[(int)HandJoint.ThumbTip];
            Assert.That(touching, Is.GreaterThan(HandMath.PinchStartGap));
            Assert.That(touching, Is.LessThan(HandMath.PinchEndGap));
        }

        [Test]
        public void PalmFacesPointWithinTheConeAndDistance()
        {
            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Vector3 palm = new Vector3(0f, 1.0f, 0.35f);
            Vector3 toHead = (head - palm).normalized;
            Assert.IsTrue(HandMath.PalmFacesPoint(palm, toHead, head, 0.643f, 0.8f), "straight at the head");
            Vector3 off45 = Quaternion.AngleAxis(45f, Vector3.up) * toHead;
            Assert.IsTrue(HandMath.PalmFacesPoint(palm, off45, head, 0.643f, 0.8f), "45 degrees off: still facing (cone 50)");
            Vector3 off60 = Quaternion.AngleAxis(60f, Vector3.up) * toHead;
            Assert.IsFalse(HandMath.PalmFacesPoint(palm, off60, head, 0.643f, 0.8f), "60 degrees off");
            Assert.IsFalse(HandMath.PalmFacesPoint(palm, Vector3.down, head, 0.643f, 0.8f), "palm down (a grab)");
            Assert.IsFalse(HandMath.PalmFacesPoint(palm + Vector3.forward * 1f, toHead, head, 0.643f, 0.8f), "too far");
        }
    }
}
