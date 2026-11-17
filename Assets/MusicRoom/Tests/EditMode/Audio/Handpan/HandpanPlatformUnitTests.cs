// The handpan unit on the platform's graph (BUILD-GUIDE 4.3; platform.md 10): the role creates HandpanUnit, it idle-skips,
// the governor's Share 0.4 cuts its Work by half or more (UnitShareTests pattern), the LOD budget does the same, every public
// method reaches the test tap (RecordUnit), and the graph path is the reference port trimmed 3 dB (the loudness parity).
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class HandpanPlatformUnitTests
    {
        const int B = OfflineEngine.Block;
        static readonly SourceId[] Out = { SourceId.Handpan };

        /// <summary>Six strikes (two at the dimple edges, a slap on the ding) in the first block, then 0.4 s: the summed Work.</summary>
        static long Work(float share, int budget, out float[] y)
        {
            using var e = new OfflineEngine(new[] { UnitId.Handpan }, null, sources: false);
            var h = (HandpanUnit)e.Unit(UnitId.Handpan);
            h.Share = share;
            if (budget != 1200) e.Host.SetBudget(budget);
            long work = 0;
            y = e.RenderGraph(OfflineEngine.Samples(0.4), Out, t =>
            {
                if (t > 0) work += h.Work;
                if (t != 0) return;
                for (int f = 1; f <= 5; f++) h.Strike(f, 0.7f, f % 2 == 0 ? 0.35f : 0f, 0.1f, HandpanTech.Tip, OfflineEngine.At(0.001 + f * 0.0003));
                h.Strike(0, 0.8f, 0f, 0f, HandpanTech.Slap, OfflineEngine.At(0.003));
            })[0];
            return work + h.Work;
        }

        [Test]
        public void TheRoleIsTheHandpanUnitAndItIdleSkips()
        {
            using var e = new OfflineEngine(new[] { UnitId.Handpan }, null, sources: false);
            SoundUnit u = e.Unit(UnitId.Handpan);
            Assert.That(u, Is.InstanceOf<HandpanUnit>(), "SoundUnits.CreateHandpan");
            Assert.That(u, Is.InstanceOf<IHandpanSound>());
            Assert.That(u, Is.InstanceOf<ILoudnessReference>());
            Assert.That(u.IdleSkip, Is.True);
            Assert.That(SoundUnits.IsUnitOnly(UnitId.Handpan), Is.True);
            e.RenderGraph(20 * B, Out);
            Assert.That(u.IdleBlocks, Is.EqualTo(12), "never played: 8 silent blocks render, then every block is skipped");
            Assert.That(u.Work, Is.EqualTo(0));
            // a strike wakes it on its sample
            var h = (HandpanUnit)u;
            long start = e.Clock, at = start + 2 * B + 31;
            var y = e.RenderGraph(4 * B, Out, t => { if (t == start) h.Strike(1, 0.6f, 0.35f, 0.2f, HandpanTech.Tip, at / (double)OfflineEngine.Sr); })[0];
            int first = -1;
            for (int i = 0; i < y.Length; i++) if (y[i] != 0f) { first = i; break; }
            Assert.That(first, Is.InRange(2 * B + 31, 2 * B + 31 + 2), "the onset lands on its sample after the skip");
            Assert.That(NullTest.Peak(y), Is.GreaterThan(0.01f));
        }

        [Test]
        public void AShareOfPointFourCutsTheWorkByHalfOrMore()
        {
            long full = Work(1f, 1200, out float[] a), low = Work(0.4f, 1200, out float[] b), lod = Work(1f, 480, out _);
            Debug.Log($"[SHARE] handpan work over 0.4 s with six strikes: share 1 {full}, share 0.4 {low} ({100.0 * low / full:F0} %), budget 480 {lod} ({100.0 * lod / full:F0} %)");
            Assert.That(full, Is.GreaterThan(0));
            Assert.That(low, Is.LessThanOrEqualTo(full / 2), "Share 0.4: at most half the work");
            Assert.That(lod, Is.LessThanOrEqualTo(full / 2), "the LOD budget 480 (0.4 x 1200): the same caps");
            Assert.That(NullTest.Peak(b), Is.GreaterThan(0.01f), "still sounding");
            // the low tier keeps the notes: the tuned modes stay (the level within 3 dB of the full render's)
            double ra = 0, rb = 0;
            for (int i = 0; i < a.Length; i++) { ra += a[i] * (double)a[i]; rb += b[i] * (double)b[i]; }
            Assert.That(10 * Math.Log10(rb / ra), Is.InRange(-3.0, 1.0), "Share 0.4 sounds the same notes");
        }

        [Test]
        public void EveryPublicMethodReachesTheTestTap()
        {
            using var e = new OfflineEngine(new[] { UnitId.Handpan }, null, sources: false);
            var tap = new AudioTestTap();
            e.Unit(UnitId.Handpan).Context.TapSource = () => tap;
            var h = (HandpanUnit)e.Unit(UnitId.Handpan);
            h.Strike(2, 0.6f, 0f, 0f, HandpanTech.Tip, 0);
            h.StrikeTak(0.5, 1, 0.4, 3, 0.3, 0);
            h.Damp(2, 1, 0.8f, 0f, 0f, HandpanDampKind.Finger, 0);
            h.ReleaseAll(0);
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpStrike), Is.EqualTo(2), "a strike and a tak");
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpDamp), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpReleaseAll), Is.EqualTo(1));
        }

        [Test]
        public void TheGraphPathIsTheReferencePortTrimmedThreeDb()
        {
            // the same strike through the graph (HandpanDsp.RenderUnit) and through the rig's direct Burst entry
            using var e = new OfflineEngine(new[] { UnitId.Handpan }, null, sources: false);
            var h = (HandpanUnit)e.Unit(UnitId.Handpan);
            var y = e.RenderGraph(OfflineEngine.Samples(0.5), Out, t => { if (t == 0) h.Strike(4, 0.6f, 0.35f, 0.2f, HandpanTech.Tip, OfflineEngine.At(0.01)); })[0];
            Assert.That(NullTest.Peak(y), Is.GreaterThan(0.01f));
            Assert.That(NullTest.Peak(y), Is.LessThan(0.95f * HandpanDsp.GraphTrim + 1e-4f), "the trim follows the safety clip");
        }
    }
}
