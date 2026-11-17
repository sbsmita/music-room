// Budget scaling (12-new-instruments/platform.md 10.3, conformance "Budget scaling"): a unit reads Graph->Budget / 1200 x
// UnitSlot.Share at block start and scales its own caps; Share 0.4 must lower its Work by 50 % or more. The phase-0 knock
// keeps round(6 x scale) of its six voices; the oldest voices over the cap fade out across one block (no click). Each real
// unit has its own share test (BowlsPlatformUnitTests, HandpanPlatformTests, ...); this one keeps the platform's knock.
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class UnitShareTests
    {
        const int B = OfflineEngine.Block;
        static readonly SourceId[] Out = { SourceId.Bowls };

        /// <summary>Six knocks in the first block, then 0.4 s; the summed Work and the output.</summary>
        static long Work(float share, int budget, out float[] y, int changeAtBlock = -1, float changeTo = 1f)
        {
            // the phase-0 knock in the bowls' slot: the role has its own unit now, so the knock is added to an engine without
            // it and renders with its own kernel (KnockUnit.Kernel), whatever slot it sits in (integration: bowls R2 + handpan R1)
            using var e = new OfflineEngine(Array.Empty<UnitId>(), null, sources: false);
            var k = e.Add(ctx => new KnockUnit(ctx, UnitId.Bowls, Out, 330f));
            k.Share = share;
            if (budget != 1200) e.Host.SetBudget(budget);
            long work = 0;
            int block = 0;
            y = e.RenderGraph(OfflineEngine.Samples(0.4), Out, t =>
            {
                if (t > 0) work += k.Work;   // the previous block's
                if (t == 0) for (int n = 0; n < 6; n++) k.Knock(n, 0.8f, OfflineEngine.At(0.001 + n * 0.0001));
                if (block++ == changeAtBlock) k.Share = changeTo;
            })[0];
            return work + k.Work;
        }

        [Test]
        public void AShareOfPointFourLowersTheKnocksWorkByHalfOrMore()
        {
            long full = Work(1f, 1200, out var a), low = Work(0.4f, 1200, out var b);
            Debug.Log($"[SHARE] knock work over 0.4 s with six voices: share 1 {full}, share 0.4 {low} ({100.0 * low / full:F0} %)");
            Assert.That(full, Is.GreaterThan(0));
            Assert.That(low, Is.LessThanOrEqualTo(full / 2), "Share 0.4: at most half the work");
            Assert.That(NullTest.Peak(b), Is.GreaterThan(0.01f), "still audible");
            Assert.That(KnockDsp.VoiceCap(0.4f), Is.EqualTo(2));
            Assert.That(KnockDsp.VoiceCap(1f), Is.EqualTo(6));
            Assert.That(KnockDsp.VoiceCap(0.01f), Is.EqualTo(1), "at least one voice");
        }

        [Test]
        public void TheLodBudgetScalesTheCapsToo()
        {
            long full = Work(1f, 1200, out _), half = Work(1f, 600, out _);
            Assert.That(half, Is.LessThanOrEqualTo(full * 0.55), "Budget 600 = scale 0.5: three voices of six");
        }

        [Test]
        public void DroppingTheShareWhileTheVoicesRingFadesThemWithoutAClick()
        {
            Work(1f, 1200, out var steady);
            Work(1f, 1200, out var dropped, changeAtBlock: 10, changeTo: 0.4f);
            int from = 10 * B, to = 11 * B;
            float before = NullTest.MaxStep(steady, B, from), atDrop = NullTest.MaxStep(dropped, from, to + 1);
            Assert.That(atDrop, Is.LessThanOrEqualTo(before * 1.01f + 1e-7f), "the voices over the cap fade out across the block: no step larger than the knocks' own");
            for (int i = 0; i < from; i++) Assert.That(dropped[i], Is.EqualTo(steady[i]), "identical before the drop");
            Assert.That(NullTest.Peak(dropped, to, to + 4 * B), Is.LessThan(NullTest.Peak(steady, to, to + 4 * B)), "quieter after: four voices gone");
        }
    }
}
