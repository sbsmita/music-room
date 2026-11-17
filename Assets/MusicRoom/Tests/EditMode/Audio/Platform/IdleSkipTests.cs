// Idle skip (12-new-instruments/platform.md 10.2): a unit registered with idleSkip is not rendered while it has nothing due
// or pending, no fade runs and it has output nothing for AudioGraph.IdleSkipBlocks (8) blocks; UnitSlot.IdleBlocks counts
// the skipped blocks; the first event after a skip still lands on its exact sample; the first Render after a skip gets
// UnitFlags.Resumed, once. Through the offline engine and the phase-0 stubs' knock (the handpan role).
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio
{
    public unsafe class IdleSkipTests
    {
        const int Sr = OfflineEngine.Sr, B = OfflineEngine.Block;
        static readonly SourceId[] Out = { SourceId.Handpan };

        static int FirstNonZero(float[] y, int from = 0) { for (int i = from; i < y.Length; i++) if (y[i] != 0f) return i; return -1; }

        [Test]
        public void EveryStubRoleRegistersWithIdleSkipAndTheClassicUnitsDoNot()
        {
            using var e = new OfflineEngine(new[] { UnitId.Handpan, UnitId.HandDrums, UnitId.Marimba, UnitId.Bowls }, null, sources: false);
            // the piano has no unit since 2026-11-11: the Example unit stands in for a unit registered the default way
            var piano = e.Add(c => new ExampleUnit(c, UnitId.Piano, null));
            foreach (UnitId r in SoundUnits.LiveRoles)
            {
                Assert.That(e.Unit(r), Is.Not.Null, r + " has its (stub) unit");
                Assert.That(e.Unit(r).IdleSkip, Is.True, r + " idle-skips");
                Assert.That(e.Host.Unit(r)->Share, Is.EqualTo(1f), r + ": full share until the governor says otherwise");
            }
            Assert.That(piano.IdleSkip, Is.False, "a unit registered without idle skip keeps its clocks running");
        }

        [Test]
        public void ASilentUnitIsSkippedAfterEightBlocksAndAnEventAfterTheSkipLandsOnItsSample()
        {
            using var e = new OfflineEngine(Array.Empty<UnitId>(), null, sources: false);
            var knock = e.Add(ctx => new KnockUnit(ctx, UnitId.Handpan, Out, 294f));   // a knock in the handpan's slot (the role has its own unit now)
            UnitSlot* u = e.Host.Unit(UnitId.Handpan);
            // never played: 8 rendered silent blocks, then skipped
            e.RenderGraph(8 * B, Out);
            Assert.That(knock.IdleBlocks, Is.EqualTo(0), "the first 8 silent blocks render");
            Assert.That(u->Blocks, Is.EqualTo(8));
            e.RenderGraph(12 * B, Out);
            Assert.That(knock.IdleBlocks, Is.EqualTo(12), "then every block is skipped");
            Assert.That(u->Blocks, Is.EqualTo(8), "no Render while skipped");
            Assert.That(knock.Work, Is.EqualTo(0), "no work while skipped");
            Assert.That(knock.Resumes, Is.EqualTo(0));

            // a knock posted for a sample in the middle of a later block: the unit wakes for that block, on that sample
            long start = e.Clock, at = start + 3 * B + 77;
            var y = e.RenderGraph(6 * B, Out, t => { if (t == start) knock.Knock(0, 0.8f, at / (double)Sr); })[0];
            Assert.That(FirstNonZero(y), Is.EqualTo(3 * B + 77), "the onset lands on round(t sr) after a skip");
            Assert.That(knock.Knocks, Is.EqualTo(1));
            Assert.That(knock.Resumes, Is.EqualTo(1), "UnitFlags.Resumed on the first Render after the skip, once");
            int idleBefore = knock.IdleBlocks;
            Assert.That(idleBefore, Is.EqualTo(12), "a pending event keeps the unit awake (no skip before it is due)");

            // it rings ~2 s, retires below -110 dBFS, then 8 silent blocks later it sleeps again
            var tail = e.RenderGraph(OfflineEngine.Samples(3.0), Out)[0];
            Assert.That(knock.IdleBlocks, Is.GreaterThan(idleBefore), "asleep again once silent");
            Assert.That(knock.Resumes, Is.EqualTo(1), "Resumed only after a skip");
            int lastSound = -1;
            for (int i = tail.Length - 1; i >= 0; i--) if (tail[i] != 0f) { lastSound = i; break; }
            int quietBlocks = (tail.Length - 1 - lastSound) / B;
            Assert.That(knock.IdleBlocks - idleBefore, Is.InRange(quietBlocks - AudioGraph.IdleSkipBlocks - 1, quietBlocks - AudioGraph.IdleSkipBlocks + 1),
                "skipping starts 8 blocks after the last output");
            Assert.That(knock.ChannelsUsed, Is.EqualTo(0));
        }

        [Test]
        public void ASkippedUnitRendersTheSameAsOneThatNeverSleeps()
        {
            // the same knocks, 2.5 s apart (each rings out in about 1.7 s), through an idle-skipping unit and one registered
            // without the skip: the same samples (the knock's state is at rest when it is skipped)
            float[] Render(bool skip, out int idle)
            {
                using var e = new OfflineEngine(Array.Empty<UnitId>(), null, sources: false);
                var k = e.Add(ctx => new KnockUnit(ctx, UnitId.Handpan, Out, 294f, idleSkip: skip));
                // each knock is posted in the block before it is due (a knock posted ahead stays pending and keeps the unit awake)
                var y = e.RenderGraph(OfflineEngine.Samples(7.0), Out, t =>
                {
                    for (int n = 0; n < 3; n++)
                    {
                        double at = 0.1 + 2.5 * n + n * 0.0007;
                        long s = (long)Math.Round(at * Sr);
                        if (s >= t + B && s < t + 2 * B) k.Knock(n, 0.7f, OfflineEngine.At(at));
                    }
                })[0];
                idle = k.IdleBlocks;
                return y;
            }
            float[] a = Render(true, out int skipped), b = Render(false, out int never);
            Assert.That(skipped, Is.GreaterThan(40), "the gaps between the knocks were skipped");
            Assert.That(never, Is.EqualTo(0));
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) Assert.Fail($"sample {i}: {a[i]} vs {b[i]}");
            Assert.That(NullTest.Peak(a), Is.GreaterThan(0.05f));
        }

        [Test]
        public void ARunningFadeAndPendingEventsKeepTheUnitAwake()
        {
            using var e = new OfflineEngine(Array.Empty<UnitId>(), null, sources: false);
            var knock = e.Add(ctx => new KnockUnit(ctx, UnitId.Handpan, Out, 294f));   // a knock in the handpan's slot (the role has its own unit now)
            // an event 2 s ahead: pending from the first block, so the silent blocks before it are never skipped
            knock.Knock(1, 0.5f, OfflineEngine.At(2.0));
            e.RenderGraph(OfflineEngine.Samples(1.5), Out);
            Assert.That(knock.IdleBlocks, Is.EqualTo(0), "a pending event: never skipped");
            e.RenderGraph(OfflineEngine.Samples(1.0), Out);
            Assert.That(knock.Knocks, Is.EqualTo(1));
            // a fade (FadeUnit) running: rendered every block until it ends
            using var f = new OfflineEngine(Array.Empty<UnitId>(), null, sources: false);
            var k2 = f.Add(ctx => new KnockUnit(ctx, UnitId.Handpan, Out, 294f));
            f.RenderGraph(10 * B, Out);
            int asleep = k2.IdleBlocks;
            Assert.That(asleep, Is.EqualTo(2));
            f.Host.FadeUnit(UnitId.Handpan, false, 20 * B);
            f.RenderGraph(10 * B, Out);
            Assert.That(k2.IdleBlocks, Is.EqualTo(asleep), "the fade keeps it rendering");
        }
    }
}
