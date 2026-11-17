// The drawn hands' feel (headset report "fingers wrong", "lag or jump"): the contact turn of a finger is capped at 20
// degrees and eased in and out over frames instead of snapping, and a brief low-confidence flicker no longer dims the hand.
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Hands
{
    public class HandFeelTests
    {
        const float Dt = 1f / 72f;

        [Test]
        public void TheDrawnTurnEasesInAndOutAndIsCapped()
        {
            float t = ContactMath.SmoothTheta(0f, 0.2f, Dt);
            Assert.That(t, Is.GreaterThan(0.05f).And.LessThan(0.2f), "eased in: part of the way in one frame, no snap");
            for (int i = 0; i < 10; i++) t = ContactMath.SmoothTheta(t, 0.2f, Dt);
            Assert.That(t, Is.EqualTo(0.2f).Within(0.002f), "there within ~0.15 s");
            float big = 0f;
            for (int i = 0; i < 40; i++) big = ContactMath.SmoothTheta(big, ContactMath.ThetaMax, Dt);
            Assert.That(big, Is.LessThanOrEqualTo(ContactMath.DrawnThetaMax + 1e-6f), "never past 20 degrees");
            Assert.That(ContactMath.DrawnThetaMax * Mathf.Rad2Deg, Is.EqualTo(20f).Within(1e-3f));
            float r = ContactMath.SmoothTheta(0.2f, 0f, Dt);
            Assert.That(r, Is.GreaterThan(0.1f), "eased out: no pop at a key edge");
            for (int i = 0; i < 40; i++) r = ContactMath.SmoothTheta(r, 0f, Dt);
            Assert.That(r, Is.EqualTo(0f), "and fully released");
            Assert.That(ContactMath.SmoothTheta(0.1f, 0.3f, 0f), Is.EqualTo(0.1f), "dt 0 keeps it");
        }

        [Test]
        public void TheCappedFingerRotationNeverExceedsTheDrawnLimit()
        {
            Vector3 pivot = new Vector3(0f, 0.045f, -0.06f), tip = new Vector3(0f, -0.01f, 0.02f);
            Assert.IsTrue(ContactMath.FingerRotation(pivot, tip, Vector3.up, 0.04f, out float full, out _));
            Assert.IsTrue(ContactMath.FingerRotation(pivot, tip, Vector3.up, 0.04f, ContactMath.DrawnThetaMax, out float drawn, out _));
            Assert.That(full, Is.GreaterThan(ContactMath.DrawnThetaMax), "the reference engine would turn it further");
            Assert.That(drawn, Is.EqualTo(ContactMath.DrawnThetaMax).Within(1e-6f));
            Assert.IsTrue(ContactMath.FingerRotation(pivot, tip, Vector3.up, 0.003f, ContactMath.DrawnThetaMax, out float small, out _));
            Assert.IsTrue(ContactMath.FingerRotation(pivot, tip, Vector3.up, 0.003f, out float smallRef, out _));
            Assert.That(small, Is.EqualTo(smallRef).Within(1e-7f), "small lifts unchanged");
        }

        [Test]
        public void ABriefLowConfidenceFlickerKeepsTheHandOpaque()
        {
            const float delay = 0.3f, low = 0.4f;
            Assert.That(MetaHandVisual.FadeTarget(false, true, 0f, delay, low), Is.EqualTo(0f), "untracked: hidden");
            Assert.That(MetaHandVisual.FadeTarget(true, true, 0f, delay, low), Is.EqualTo(1f));
            Assert.That(MetaHandVisual.FadeTarget(true, false, 0.1f, delay, low), Is.EqualTo(1f), "a flicker shorter than the delay");
            Assert.That(MetaHandVisual.FadeTarget(true, false, 0.35f, delay, low), Is.EqualTo(low), "lasting low confidence dims");
        }
    }
}
