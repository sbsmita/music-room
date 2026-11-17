// The hand drums' unit inside the platform's graph (BUILD-GUIDE 4.3, phase B): created by SoundUnits.CreateHandDrums in
// the role UnitId.HandDrums with its two outputs, idle skip on (it sleeps after its tail and wakes on the exact sample of
// the next stroke), the governor's UnitSlot.Share scaling its work (Share 0.4: at most half), its Work reported, every
// public method on the test tap, the graph render identical to the standalone host's (the bit-exact vector path), and the
// conga on HandDrumHi, the tumba on HandDrumLo.
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio.HandDrums
{
    public unsafe class HandDrumsPlatformTests
    {
        const int B = OfflineEngine.Block;
        static readonly SourceId[] Out = { SourceId.HandDrumLo, SourceId.HandDrumHi };

        static float Peak(float[] y, int from = 0, int to = -1) => NullTest.Peak(y, from, to);

        [Test]
        public void TheFactoryMakesTheRealUnitWithIdleSkipAndItsTwoOutputs()
        {
            using var e = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u = e.Unit(UnitId.HandDrums) as HandDrumsUnit;
            Assert.That(u, Is.Not.Null, "SoundUnits.CreateHandDrums makes a HandDrumsUnit");
            Assert.That(u.Id, Is.EqualTo(UnitId.HandDrums));
            Assert.That(u.Outputs, Is.EqualTo(new[] { SourceId.HandDrumLo, SourceId.HandDrumHi }), "channel 0 the tumba, channel 1 the conga");
            Assert.That(u.IdleSkip, Is.True);
            Assert.That(u is ILoudnessReference, Is.True);
            Assert.That(u.StateBytes / 1024.0, Is.InRange(150.0, 200.0), "the arena (descriptor ArenaMiB 0.19)");
            Assert.That(SoundUnits.IsUnitOnly(UnitId.HandDrums), Is.True);
        }

        [Test]
        public void TheCongaPlaysOnHandDrumHiAndTheTumbaOnHandDrumLo()
        {
            using var e = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u = (HandDrumsUnit)e.Unit(UnitId.HandDrums);
            var y = e.RenderGraph(OfflineEngine.Samples(0.5), Out, t => { if (t == 0) u.Stroke(0, 1, HandStroke.Open, 0.7, 0, OfflineEngine.At(0.01)); });
            Assert.That(Peak(y[1]), Is.GreaterThan(1e-3f), "the conga on HandDrumHi");
            Assert.That(Peak(y[0]), Is.EqualTo(0f), "nothing on HandDrumLo");
            using var e2 = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u2 = (HandDrumsUnit)e2.Unit(UnitId.HandDrums);
            var y2 = e2.RenderGraph(OfflineEngine.Samples(0.5), Out, t => { if (t == 0) u2.Stroke(1, 1, HandStroke.Open, 0.7, 0, OfflineEngine.At(0.01)); });
            Assert.That(Peak(y2[0]), Is.GreaterThan(1e-3f), "the tumba on HandDrumLo");
            Assert.That(Peak(y2[1]), Is.EqualTo(0f));
        }

        [Test]
        public void ItSleepsAfterItsTailAndTheNextStrokeLandsOnItsSample()
        {
            using var e = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u = (HandDrumsUnit)e.Unit(UnitId.HandDrums);
            // a stroke, then a long tail (the drums ring out to exact silence: the modes are culled below 1e-11)
            var y = e.RenderGraph(OfflineEngine.Samples(8.0), Out, t => { if (t == 0) u.Stroke(0, 1, HandStroke.Bass, 0.8, 0, OfflineEngine.At(0.01)); });
            int idle = u.IdleBlocks;
            Assert.That(idle, Is.GreaterThan(0), "skipped once silent");
            int last = -1;
            for (int i = y[1].Length - 1; i >= 0; i--) if (y[1][i] != 0f) { last = i; break; }
            UnityEngine.Debug.Log($"[HandDrums] idle skip: a bass at 0.01 s rings to exact silence at {last / (double)OfflineEngine.Sr:F2} s; {idle} blocks skipped by 8 s");
            // the next stroke after the skip: on its exact sample, as in a fresh engine
            long start = e.Clock, at = start + 3 * B + 17;
            var z = e.RenderGraph(OfflineEngine.Samples(0.2), Out, t => { if (t == start) u.Stroke(0, 1, HandStroke.Open, 0.7, 0, at / (double)OfflineEngine.Sr); });
            int first = -1;
            for (int i = 0; i < z[1].Length; i++) if (z[1][i] != 0f) { first = i; break; }
            // the same stroke in a fresh engine (never slept)
            using var f = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var uf = (HandDrumsUnit)f.Unit(UnitId.HandDrums);
            var w = f.RenderGraph(OfflineEngine.Samples(0.2), Out, t => { if (t == 0) uf.Stroke(0, 1, HandStroke.Open, 0.7, 0, (3 * B + 17) / (double)OfflineEngine.Sr); });
            int fresh = -1;
            for (int i = 0; i < w[1].Length; i++) if (w[1][i] != 0f) { fresh = i; break; }
            UnityEngine.Debug.Log($"[HandDrums] after the skip: the stroke due at sample {3 * B + 17} sounds from {first} (a fresh engine: {fresh})");
            Assert.That(fresh, Is.GreaterThanOrEqualTo(3 * B + 17));
            Assert.That(first, Is.EqualTo(fresh), "the first stroke after a skip sounds on the same sample as in a fresh engine");
        }

        /// <summary>A dense phrase (a two-hand roll on both drums with rests), the summed Work and the output.</summary>
        static long Work(float share, out float[][] y)
        {
            using var e = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u = (HandDrumsUnit)e.Unit(UnitId.HandDrums);
            u.Share = share;
            long work = 0;
            y = e.RenderGraph(OfflineEngine.Samples(1.2), Out, t =>
            {
                if (t > 0) work += u.Work;
                if (t != 0) return;
                for (int i = 0; i < 16; i++)
                {
                    double at = 0.01 + i * 0.055;
                    u.Stroke(i % 2, i % 2, i % 4 == 0 ? HandStroke.Slap : HandStroke.Open, 0.75, 0, at);
                    u.Stroke(1 - i % 2, 1 - i % 2, HandStroke.Bass, 0.6, 0, at + 0.02);
                }
            });
            return work + u.Work;
        }

        [Test]
        public void TheGovernorsShareOfPointFourCutsTheWorkByHalfOrMore()
        {
            long full = Work(1f, out var a), low = Work(0.4f, out var b);
            UnityEngine.Debug.Log($"[HandDrums] graph Work over a 1.2 s roll on both drums: share 1 {full}, share 0.4 {low} ({100.0 * low / full:F0} %)");
            Assert.That(full, Is.GreaterThan(0), "the unit reports its work (UnitSlot.Work)");
            Assert.That(low, Is.LessThanOrEqualTo(full / 2), "Share 0.4: at most half the work");
            Assert.That(Peak(b[1]), Is.GreaterThan(0.01f), "still audible");
        }

        [Test]
        public void EveryPublicMethodRecordsItselfOnTheTestTap()
        {
            using var e = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u = (HandDrumsUnit)e.Unit(UnitId.HandDrums);
            var tap = new AudioTestTap();
            e.Context.TapSource = () => tap;
            u.Patch(0, 1, HandPart.Fingers, 0.6f, PatchShape.Points, new PatchPoint { Rho = 0.7f }, default, default, 1, 0);
            u.Stroke(1, 0, HandStroke.Bass, 0.5, 0, 0);
            u.Rest(0, 1, new RestPoint { Part = HandPart.Palm, Rho = 0.1f }, default, default, 1, 0);
            u.RestCanonical(1, 0, HandRest.Palm, 0, 0);
            u.Lift(0, 1, 0);
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpPatch), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpStrokePatch), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpRest), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpRestCanon), Is.EqualTo(1));
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpLift), Is.EqualTo(1));
            Assert.That(tap.Calls[1].Index, Is.EqualTo(1), "the drum");
            Assert.That(tap.Calls[1].Vel, Is.EqualTo(0.5f), "the stroke's u");
            e.Context.TapSource = null;
        }

        [Test]
        public void TheGraphRendersTheStandaloneHostsSamples()
        {
            // the same open tone through the platform graph (Burst, FloatMode.Fast) and the standalone host's Fast entry
            using var e = new OfflineEngine(new[] { UnitId.HandDrums }, null, sources: false);
            var u = (HandDrumsUnit)e.Unit(UnitId.HandDrums);
            var y = e.RenderGraph(OfflineEngine.Samples(1.0), Out, t => { if (t == 0) u.Stroke(0, 1, HandStroke.Open, 0.8, 0, 2.0 * B / OfflineEngine.Sr); });
            using var h = new HandDrumsOffline();
            h.Entry = HandDrumsEntry.BurstFast;
            h.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, HandDrumsOffline.At(2 * B));
            var r = h.Render(OfflineEngine.Samples(1.0));
            double res = NullTest.ResidualDb(y[1], r[0]);
            UnityEngine.Debug.Log($"[HandDrums] graph vs standalone host, an open tone 1 s: residual {res:F1} dB");
            Assert.That(res, Is.LessThan(-100.0), "the graph renders what the vectors were checked on");
        }
        /// <summary>The platform's tail rule (platform.md 10.3 "no metallic tails"), in the hand drums' form (BUILD-GUIDE 13: a
        /// pitched skin's tail IS its tone, so the top-20-partials share does not apply; T4 of the lab's verify.json): after
        /// 0.5 s the tone partial stands 12 dB or more over every other partial from 300 Hz to 9 kHz, and no membrane
        /// partial above 1.3 x the tone has a design T60 over 0.8 x the tone's.</summary>
        [Test]
        public void ThePitchedSkinTailRuleHoldsOnBothDrums()
        {
            var t = HandDrumsTables.Parse(HandDrumsTables.LoadDefaultBytes());
            string log = "[HandDrums] tail rule (T4):";
            for (int d = 0; d < 2; d++)
            {
                string k = d == 0 ? "conga." : "tumba.";
                double fT = d == 0 ? 261.6 : 196.0;
                double[] f = t.D(k + "fHz"), t60 = t.D(k + "t60");
                int[] kind = t.I(k + "kind");
                int tone = 0;
                for (int q = 1; q < f.Length; q++) if (Math.Abs(f[q] / fT - 1) < Math.Abs(f[tone] / fT - 1)) tone = q;
                fT = f[tone];
                double longest = 0;
                for (int q = 0; q < f.Length && q < t60.Length; q++) if (kind[q] == HandDrumsConst.KindMem && f[q] > 1.3 * fT) longest = Math.Max(longest, t60[q]);
                float[] y = HdRender.Stroke(HandStroke.Open, d, 0.8, 1.6);
                var pk = HdAnalysis.SpectralPeaks(y, 24000, 24000, 12, 150, 9000);
                var tp = pk.Find(q => Math.Abs(q.F / fT - 1) < 0.04);
                var other = pk.Find(q => q.F >= 300 && Math.Abs(q.F / fT - 1) >= 0.04);
                double over = tp.Db - other.Db, ratio = longest / t60[tone];
                log += $" {k.TrimEnd('.')}: tone {fT:F1} Hz, {over:F1} dB over the next ({other.F:F0} Hz), longest partial T60 {ratio:F2} x the tone's;";
                Assert.That(tp.F, Is.GreaterThan(0), k + " the tone partial is there after 0.5 s");
                Assert.That(over, Is.GreaterThanOrEqualTo(12.0), k + " the tone dominates the tail");
                Assert.That(ratio, Is.LessThanOrEqualTo(0.8), k + " no partial outlives the tone");
            }
            UnityEngine.Debug.Log(log);
        }
    }
}
