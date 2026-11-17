// BowlsHands against the reference's 25 scripted-hand cases (interaction-cases.mjs) on the reviewed layout and again on
// the app's layout (the big bowl nearest, review item 15), plus the phase-A review-item cases (13: the lip, 19: a full
// hand by a neighbour) with their "before" variants as evidence. The hand logic runs exactly as the module will drive it;
// the unit renders managed (bit-exact to the reference engine), so SingVelocity comes from the real DSP.
using MusicRoom.Tests.Bowls;
using NUnit.Framework;
using UnityEngine;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.BowlsHandsCases
{
    [TestFixture(Placement.Reference)]
    [TestFixture(Placement.App)]
    public class BowlsInteractionTests
    {
        readonly Placement L;
        public BowlsInteractionTests(Placement layout) { L = layout; }

        static void Check(CaseResult r) { Debug.Log("[BOWLS-CASE] " + r); Assert.That(r.Pass, Is.True, r.Measured); }

        [Test] public void Tap() => Check(BowlsCases.Tap(L));
        [Test] public void FirstTimerRub([Values(0, 1, 2)] int bowl) => Check(BowlsCases.FirstTimer(L, bowl));
        [Test] public void RestingFingerDampsLightlyAfterItsGrace() => Check(BowlsCases.Rest(L));
        [Test] public void TapThenRub([Values(0, 1, 2)] int bowl) => Check(BowlsCases.TapThenRub(L, bowl));
        [Test] public void Hover() => Check(BowlsCases.Hover(L));
        [Test] public void TrackingLoss80msKeepsTheRub() => Check(BowlsCases.Loss80(L));
        [Test] public void TrackingLoss400msResumesWithoutAStrike() => Check(BowlsCases.Loss400(L));
        [Test] public void BounceStrikesOnce() => Check(BowlsCases.Bounce(L));
        [Test] public void GlitchNeverStrikes() => Check(BowlsCases.Glitch(L));
        [Test] public void InnerWallNeverSings() => Check(BowlsCases.InnerWall(L));
        [Test] public void PalmRestDamps() => Check(BowlsCases.PalmRest(L));
        [Test] public void TwoHandsOneBowlEach() => Check(BowlsCases.TwoHands(L));
        [Test] public void GentlePokesRing() => Check(BowlsCases.Pokes(L, out _));
        [Test] public void TapAndHoldRings() => Check(BowlsCases.TapHold(L));
        [Test] public void DoubleTapsStrikeTwice() => Check(BowlsCases.DoubleTaps(L));
        [Test] public void FastTapsAreSwept() => Check(BowlsCases.FastTaps(L));
        [Test] public void FastTaps3MsAt30HzInformational() => Check(BowlsCases.FastTaps3At30(L));
        [Test] public void OtherFingersKeepTheSong() => Check(BowlsCases.Fingers(L, out _));
        [Test] public void FreehandCirclesSing() => Check(BowlsCases.Freehand(L, out _));
        [Test] public void PujaShaftRubSings() => Check(BowlsCases.PujaRub(L));
        [Test] public void PujaSuedeStrikes() => Check(BowlsCases.PujaSuede(L));

        // ---- review item 13: the lip (the wine-glass gesture) sings like the outside
        [Test] public void LipFirstTimerRubSings([Values(0, 1, 2)] int bowl) => Check(BowlsCases.LipFirstTimer(L, bowl, true));

        [Test]
        public void LipRubBeforeTheFixStartsSlowlyOnTheBigBowl()
        {
            var r = BowlsCases.LipFirstTimer(L, 0, false);
            Debug.Log("[BOWLS-CASE] (before the fix) " + r);
            Assert.That(r.Pass, Is.False, "the unit's own lip model starts the big bowl too slowly (the review's evidence)");
        }

        // ---- review item 19: a full hand rubbing next to a neighbour never plays the neighbour
        [Test] public void FullHandRubNextToANeighbourLeavesItAlone() => Check(BowlsCases.FullHandNeighbour(L, true));

        [Test]
        public void FullHandRubBeforeTheFixTouchesTheNeighbour()
        {
            var r = BowlsCases.FullHandNeighbour(L, false);
            Debug.Log("[BOWLS-CASE] (before the fix) " + r);
            Assert.That(r.Pass, Is.False);
        }
    }
}
