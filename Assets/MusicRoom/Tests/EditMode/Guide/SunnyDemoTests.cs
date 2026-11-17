// The ghost hand's motion (updateDemo / nodPose) at the loop times the reference check samples, in Unity's
// mirrored frame (right-handed z offsets toward the player are negative here, x rotations are negated).
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public class SunnyDemoTests
    {
        static DemoPose At(DemoKind kind, float t)
        {
            var p = new DemoPose();
            SunnyDemo.Evaluate(kind, t, ref p);
            return p;
        }

        static float PitchDeg(Quaternion q)
        {
            float x = q.eulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        [Test]
        public void PalmTurnsToThePlayerHoldsWithTheChargingRingThenRests()
        {
            DemoPose p0 = At(DemoKind.Palm, 0.016f);
            // demoVisibleOpen: visible, open, no ring yet; fingers point away from the player (+z) with the palm up.
            Assert.IsTrue(p0.Visible);
            Assert.IsFalse(p0.Ring);
            Assert.That(p0.IndexCurl + p0.FingerCurl + p0.ThumbCurl, Is.EqualTo(0f));
            Assert.That((p0.HandRotation * Vector3.up).z, Is.GreaterThan(0.99f), "fingers away from the player");
            Assert.That(p0.HandPosition.y, Is.EqualTo(-0.05f - 0.04f).Within(1e-3f));
            // palmTurns: between t .016 and .316 the hand moves and turns toward facing the player.
            DemoPose p1 = At(DemoKind.Palm, 0.316f);
            Assert.That(Vector3.Distance(p0.HandPosition, p1.HandPosition), Is.GreaterThan(0.005f));
            Assert.That(PitchDeg(p1.HandRotation), Is.LessThan(PitchDeg(p0.HandRotation)));
            Assert.That(PitchDeg(p1.HandRotation), Is.GreaterThanOrEqualTo(0f));
            // palmHoldsWithRing at .816: upright, palm to the player, ring charging (opacity > .3, scale > .6).
            DemoPose p2 = At(DemoKind.Palm, 0.816f);
            Assert.That(Quaternion.Angle(p2.HandRotation, Quaternion.identity), Is.LessThan(1e-3f));
            Assert.IsTrue(p2.Ring);
            Assert.That(p2.RingOpacity, Is.GreaterThan(0.3f));
            Assert.That(p2.RingScale, Is.GreaterThan(0.6f));
            Assert.That(p2.RingScale, Is.EqualTo(0.5f + 0.5f * (0.816f - 0.6f) / 0.5f).Within(1e-5f));
            Assert.That(p2.RingOpacity, Is.EqualTo(0.15f + 0.65f * (0.816f - 0.6f) / 0.5f).Within(1e-5f));
            // ringCharges
            Assert.That(At(DemoKind.Palm, 1.016f).RingOpacity, Is.GreaterThan(p2.RingOpacity));
            // palmDemoRests: visible, no ring, back to palm up
            DemoPose p3 = At(DemoKind.Palm, 2.216f);
            Assert.IsTrue(p3.Visible);
            Assert.IsFalse(p3.Ring);
            Assert.That(PitchDeg(p3.HandRotation), Is.EqualTo(90f).Within(0.01f));
            // turning back on [1.1, 1.6)
            Assert.That(PitchDeg(At(DemoKind.Palm, 1.35f).HandRotation), Is.InRange(1f, 89f));
        }

        [Test]
        public void PokeReachesTheGhostRowTwiceAndLightsIt()
        {
            DemoPose a = At(DemoKind.Poke, 0.016f);
            // pokeDemo: ghost panel shown, index straight, the other fingers curled (> 1 rad)
            Assert.IsTrue(a.Visible && a.Ghost);
            Assert.That(a.IndexCurl, Is.EqualTo(0f));
            Assert.That(a.FingerCurl * SunnyDemo.CurlFingerRad, Is.GreaterThan(1f));
            Assert.That(a.ThumbCurl, Is.EqualTo(0.5f));
            Assert.That(a.HandPosition.y, Is.EqualTo(-0.02f).Within(1e-6f));
            // pokeMoves: half a second later the finger is >4 cm further from the player and the row lit (> .4)
            DemoPose b = At(DemoKind.Poke, 0.566f);
            Assert.That(b.HandPosition.z, Is.GreaterThan(a.HandPosition.z + 0.04f));
            Assert.That(b.RowOpacity, Is.GreaterThan(0.4f));
            // pokeComesBack
            DemoPose c = At(DemoKind.Poke, 1.016f);
            Assert.That(c.HandPosition.z, Is.LessThan(b.HandPosition.z - 0.04f));
            Assert.That(c.RowOpacity, Is.LessThan(0.2f));
            // pokeTwice
            Assert.That(At(DemoKind.Poke, 1.516f).HandPosition.z, Is.GreaterThan(a.HandPosition.z + 0.04f));
            // the peak push: 7 cm toward the panel, row fully lit
            DemoPose peak = At(DemoKind.Poke, 0.6f);
            Assert.That(peak.HandPosition.z, Is.EqualTo(-0.25f + 0.07f).Within(1e-4f));
            Assert.That(peak.RowOpacity, Is.EqualTo(0.65f).Within(1e-4f));
            // Pointing away from the player: the hand's fingers (+y) point along +z.
            Assert.That((a.HandRotation * Vector3.up).z, Is.GreaterThan(0.99f));
        }

        [Test]
        public void SwipeSweepsThirtyCentimetresLeftTwiceAndFades()
        {
            // sweepDemo / sweepMoves (sampled at .206, .326, .456 after the step began)
            DemoPose a = At(DemoKind.Swipe, 0.206f), b = At(DemoKind.Swipe, 0.326f), c = At(DemoKind.Swipe, 0.456f);
            Assert.IsTrue(a.Visible && a.Arrow);
            Assert.That(a.IndexCurl + a.FingerCurl + a.ThumbCurl, Is.EqualTo(0f), "an open hand");
            Assert.That(a.HandPosition.x, Is.GreaterThan(b.HandPosition.x));
            Assert.That(b.HandPosition.x, Is.GreaterThan(c.HandPosition.x));
            float span = a.HandPosition.x - c.HandPosition.x;
            Assert.That(span, Is.GreaterThan(0.25f));
            Assert.That(span, Is.LessThanOrEqualTo(0.3f + 1e-6f));
            Assert.That(c.HandPosition.x, Is.EqualTo(0.07f - 0.15f).Within(1e-5f), "ends 15 cm left of the hand root");
            // sweepFades: after the sweep it fades out, then is invisible until it fades in for the next
            Assert.That(At(DemoKind.Swipe, 0.856f).Opacity, Is.LessThan(0.5f));
            Assert.That(At(DemoKind.Swipe, 1.0f).Opacity, Is.EqualTo(0f));
            Assert.That(At(DemoKind.Swipe, 1.35f).Opacity, Is.EqualTo(0.5f * 0.5f).Within(1e-4f));
            // sweepTwice
            Assert.That(At(DemoKind.Swipe, 1.6f).HandPosition.x - At(DemoKind.Swipe, 1.76f).HandPosition.x, Is.GreaterThan(0.1f));
            Assert.That(At(DemoKind.Swipe, 1.256f).HandPosition.x - At(DemoKind.Swipe, 1.756f).HandPosition.x, Is.GreaterThan(0.25f));
            Assert.That(At(DemoKind.Swipe, 2.85f).Opacity, Is.EqualTo(0.25f).Within(1e-4f), "fades in before the loop restarts");
        }

        [Test]
        public void NodHidesTheHandAndDipsTwicePerLoop()
        {
            Assert.IsFalse(At(DemoKind.Nod, 0.3f).Visible);
            Assert.IsFalse(At(DemoKind.None, 0.3f).Visible);
            Assert.That(SunnyDemo.NodAmount(0.1f), Is.EqualTo(0f));
            Assert.That(SunnyDemo.NodAmount(0.4f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(SunnyDemo.NodAmount(0.7f), Is.EqualTo(0f));
            Assert.That(SunnyDemo.NodAmount(1.1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(SunnyDemo.NodAmount(2.0f), Is.EqualTo(0f));
        }

        [Test]
        public void FingersCurlTowardThePlayerAndTheIndexSitsBesideTheThumb()
        {
            // The palm faces the player (-z): a curl bends the finger tips toward -z.
            Assert.That((SunnyDemo.FingerRotation(1f) * Vector3.up).z, Is.LessThan(-0.9f));
            Assert.That(SunnyDemo.FingerRotation(0f), Is.EqualTo(Quaternion.identity));
            // The thumb leans 1 rad toward +x, the side of its own pivot.
            Vector3 thumb = SunnyDemo.ThumbRotation(0f) * Vector3.up;
            Assert.That(thumb.x, Is.EqualTo(Mathf.Sin(1f)).Within(1e-5f));
            Assert.That(thumb.y, Is.EqualTo(Mathf.Cos(1f)).Within(1e-5f));
            // Mirrored (spec 08 section 8): the index finger is the one next to the thumb.
            Assert.That(SunnyDemo.ThumbPivot.x, Is.GreaterThan(0f));
            int nearest = 0;
            for (int i = 1; i < 4; i++)
                if (Mathf.Abs(SunnyDemo.FingerX[i] - SunnyDemo.ThumbPivot.x) < Mathf.Abs(SunnyDemo.FingerX[nearest] - SunnyDemo.ThumbPivot.x)) nearest = i;
            Assert.That(nearest, Is.EqualTo(0), "index");
            Assert.That(SunnyDemo.StraightLength(0.065f), Is.EqualTo(0.048f).Within(1e-6f));
            Assert.That(SunnyDemo.StraightLength(0.02f), Is.EqualTo(0.01f));
        }

        [Test]
        public void TheDemoSitsBetweenSunnyAndThePlayer()
        {
            // handNearSunny (0.2 .. 0.32 m from the body), on the player's side (-z).
            float d = SunnyDemo.HandRoot.magnitude;
            Assert.That(d, Is.InRange(0.2f, 0.32f));
            Assert.That(SunnyDemo.HandRoot.z, Is.LessThan(0f));
            Assert.That(SunnyDemo.GhostPos.z, Is.GreaterThan(SunnyDemo.HandRoot.z), "the ghost panel is beyond the hand, toward Sunny");
            Assert.That(SunnyDemo.RingLocal.z, Is.LessThan(0f), "the ring floats off the palm toward the player");
        }
    }
}
