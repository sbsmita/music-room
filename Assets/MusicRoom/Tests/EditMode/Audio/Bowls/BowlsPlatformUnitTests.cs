// The bowls unit in the platform's graph (BUILD-GUIDE 4.3; platform.md 10.3): SoundUnits.CreateBowls makes a BowlsUnit
// on SourceId.Bowls that records every call (RecordUnit), plays strikes and rubs through UnitHooks.RenderBowls, reports
// its Work, and at a Share of 0.4 does half the work or less (the UnitShareTests pattern, on the real unit).
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class BowlsPlatformUnitTests
    {
        static readonly SourceId[] Out = { SourceId.Bowls };

        /// <summary>Three strikes and a rub on the middle bowl for 0.6 s; the summed Work and the output.</summary>
        static long Work(float share, out float[] y, out BowlsUnit unit, int budget = 1200)
        {
            using var e = new OfflineEngine(new[] { UnitId.Bowls }, null, sources: false);
            if (budget != 1200) e.Host.SetBudget(budget);
            var u = (BowlsUnit)e.Unit(UnitId.Bowls);
            unit = u;
            u.Share = share;
            long work = 0;
            double t = 0;
            y = e.RenderGraph(OfflineEngine.Samples(0.6), Out, s =>
            {
                if (s > 0) work += u.Work;
                t = s / (double)OfflineEngine.Sr;
                if (s == 0) for (int b = 0; b < 3; b++) u.Strike(b, BowlStrikeKind.Tap, 1.2f, 0.3f + b, 1f, OfflineEngine.At(0.001 + b * 0.0005));
                if (t > 0.05) u.Contact(1, BowlSurface.Outer, 4f, 0.45f, (float)(0.45 / 0.075 * t), 6f, 0.85f);
            })[0];
            return work + u.Work;
        }

        [Test]
        public void ThePlatformMakesTheBowlsUnitAndItPlays()
        {
            long full = Work(1f, out var y, out BowlsUnit u);
            Assert.That(u, Is.Not.Null);
            Assert.That(u.Id, Is.EqualTo(UnitId.Bowls));
            float peak = NullTest.Peak(y);
            Debug.Log($"[BOWLS] platform graph: work {full} over 0.6 s, peak {peak:F3}");
            Assert.That(full, Is.GreaterThan(0), "the hook reports its work");
            Assert.That(peak, Is.GreaterThan(1e-3f), "it sounds on SourceId.Bowls");
            Assert.That(peak, Is.LessThan(1f));
        }

        [Test]
        public void AShareOfPointFourAtLeastHalvesTheBowlsWork()
        {
            long full = Work(1f, out _, out _), low = Work(0.4f, out _, out _);
            Debug.Log($"[BOWLS] platform share: work at 1 {full}, at 0.4 {low} ({100.0 * low / full:F0} %)");
            Assert.That(low, Is.LessThanOrEqualTo(full / 2), "Share 0.4: at most half the work");
        }

        [Test]
        public void TheLodBudgetScalesTheBowlsToo()
        {
            long full = Work(1f, out _, out _), half = Work(1f, out _, out _, 600);
            Debug.Log($"[BOWLS] platform budget 600: work {half} of {full} ({100.0 * half / full:F0} %)");
            Assert.That(half, Is.LessThan(full), "Budget 600 lowers the work");
        }

        [Test]
        public void EveryCallIsRecorded()
        {
            using var e = new OfflineEngine(new[] { UnitId.Bowls }, null, sources: false);
            var u = (BowlsUnit)e.Unit(UnitId.Bowls);
            var tap = new AudioTestTap();
            e.Context.TapSource = () => tap;
            u.Strike(0, BowlStrikeKind.Tap, 1f, 0f, 1f);
            u.Contact(1, BowlSurface.Outer, 4f, 0.4f, 0f, 5f, 0.85f);
            u.Release(1);
            u.Damp(2, 3);
            Assert.That(tap.CountUnit(UnitId.Bowls, BowlsDsp.OpStrike), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.Bowls, BowlsDsp.OpContact), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.Bowls, BowlsDsp.OpRelease), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.Bowls, BowlsDsp.OpDamp), Is.EqualTo(1));
        }
    }
}
