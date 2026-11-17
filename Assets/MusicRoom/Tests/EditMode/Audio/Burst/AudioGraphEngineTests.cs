// EditMode tests of the engine around the kernels (burst.md sections 3-6 and 13, ENGINE-API.md): the arena, sound units
// (sample-accurate events in call order, the late guard, fade-out / suspend / reset / resume), the graph slice through a
// SynthSource and its bus, the one-shot pool (quietest-first stealing into a 128-sample ghost
// fade, expiry, release), the CPU cap (LOD fades the quietest groups, keeps just-struck ones), the denormal policy, the
// adaptive budget rule, the load meter and the WAV helpers. All inputs are synthetic; the Example unit stands in for an
// instrument.
using System;
using System.Diagnostics;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Dsp;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.Audio
{
    public unsafe class AudioGraphEngineTests
    {
        const int Sr = OfflineEngine.Sr, F = OfflineEngine.Block;
        static readonly SourceId[] Mid = { SourceId.PianoMid };

        static OfflineEngine WithExample(out ExampleUnit ex, bool sources = false, UnitId role = UnitId.Example, GraphConfig? cfg = null)
        {
            var e = new OfflineEngine(null, null, sources, 1, cfg);
            ex = e.Add(c => new ExampleUnit(c, role, Mid));
            return e;
        }

        static int FirstNonZero(float[] y) { for (int i = 0; i < y.Length; i++) if (y[i] != 0f) return i; return -1; }

        [Test]
        public void QuestArenaIsAlignedAndAboutTheSpecSize()
        {
            var cfg = GraphConfig.QuestDefault(Sr);
            using var h = new AudioGraphHost(cfg);
            GraphState* g = h.State;
            // burst.md section 4: 1,462,144 bytes for the spec's arrays at its 18 sources (Out alone 18 x 8192 x 4). The
            // many-instrument platform's 7 unit-only sources (25 in all since 2026-11-10) add their Out slices, 7 x 8192 x 4
            // = 229,376 bytes. The engine additions: the one-shot pool (64 live + 16 ghost slots and voices, 64 presets), its
            // lane scratch (8192 x 16), the 10 unit slots (UnitId.Count, 232 bytes each with the platform's Share / Class /
            // Work / idle-skip fields) and the per-source flags, each rounded up to 64 bytes: about 0.16 MiB.
            static long Up(long n) => (n + 63) & ~63L;
            long spec = 1462144L + (long)(cfg.Sources - 18) * cfg.MaxFrames * 4;
            int slots = cfg.OneShots + cfg.Ghosts;
            long additions = Up((long)slots * sizeof(OneShotSlot)) + Up((long)slots * sizeof(OneShotVoice)) + Up((long)cfg.Presets * sizeof(OneShotPreset))
                             + Up((long)cfg.MaxFrames * 16) + Up((long)(int)UnitId.Count * sizeof(UnitSlot)) + Up(cfg.Sources);
            Debug.Log($"[BURST] arena {h.ArenaBytes} bytes ({h.ArenaBytes / 1048576.0:F2} MiB) = spec arrays {spec} ({cfg.Sources} sources) + engine additions " +
                      $"{additions} (unit slot {sizeof(UnitSlot)} B x {(int)UnitId.Count}), host allocations {h.AllocatedBytes} bytes");
            Assert.That(cfg.Sources, Is.EqualTo(25), "18 classic sources + the platform's 7");
            Assert.That(sizeof(UnitSlot), Is.EqualTo(232), "208 bytes + Share, Class, IdleSkip, Skipping, Work, IdleBlocks, QuietBlocks");
            Assert.That(spec, Is.EqualTo(1691520L), "1,462,144 + 229,376");
            Assert.That(h.ArenaBytes, Is.EqualTo(spec + additions), "the arena is exactly the arithmetic above");
            Assert.That(h.ArenaBytes, Is.InRange(spec, spec + 200 * 1024), "the engine additions stay under 200 KiB");
            foreach (long p in new[] { (long)g->Out, (long)g->Acc, (long)g->Groups, (long)g->Banks, (long)g->Ring.Slots, (long)g->Slots, (long)g->Voices, (long)g->Units, (long)g->Scratch4 })
                Assert.That(p % 64, Is.EqualTo(0), "64-byte aligned");
            Assert.That(sizeof(GraphCommand), Is.EqualTo(64));
            Assert.That(sizeof(GraphEvent), Is.EqualTo(32));
            Assert.That(sizeof(ModalGroup), Is.EqualTo(96));
            Assert.That(sizeof(OneShotSlot), Is.EqualTo(32));
            Assert.That(g->OneShotLive, Is.EqualTo(64)); Assert.That(g->OneShotGhosts, Is.EqualTo(16));
            Assert.That(g->Budget, Is.EqualTo(1200));
            Assert.That(g->UnitCount, Is.EqualTo((int)UnitId.Count));
        }

        [Test]
        public void UnitEventsAreSampleAccurateAndInCallOrder()
        {
            using var e = WithExample(out var ex);
            long at = 2 * F + 100;
            var y = e.RenderGraph(4 * F, Mid, t => { if (t == 0) ex.Note(0, 440, 0.5f, 1f, at / (double)Sr); })[0];
            Assert.That(FirstNonZero(y), Is.EqualTo(at), "the onset lands on its sample (round(t sr)), three blocks after posting");
            Assert.That(ex.State->LastOffset, Is.EqualTo(100));

            // Same sample: Note then Hush is silence, Hush then Note sounds (due events run in (At, Seq) = call order).
            using var e2 = WithExample(out var a);
            var quiet = e2.RenderGraph(2 * F, Mid, t => { if (t == 0) { a.Note(0, 440, 0.5f, 1f, 0.004); a.Hush(0.004); } })[0];
            Assert.That(NullTest.Peak(quiet), Is.EqualTo(0f));
            using var e3 = WithExample(out var b);
            var loud = e3.RenderGraph(2 * F, Mid, t => { if (t == 0) { b.Hush(0.004); b.Note(0, 440, 0.5f, 1f, 0.004); } })[0];
            Assert.That(NullTest.Peak(loud), Is.GreaterThan(0.1f));
        }

        [Test]
        public void LateOnsetsAreDroppedButStateCommandsStillApply()
        {
            using var e = WithExample(out var ex);
            e.RenderGraph(40 * F, Mid);                           // the clock is now 10240 samples, more than 100 ms past 0.001 s
            var y = e.RenderGraph(2 * F, Mid, t => { if (t == 40 * F) { ex.Note(0, 440, 0.5f, 1f, 0.001); } })[0];
            Assert.That(NullTest.Peak(y), Is.EqualTo(0f), "an onset 100+ ms late is dropped, not fired in a burst");
            Assert.That(ex.Slot->LateDropped, Is.EqualTo(1));
            // A recent past time still plays at once (the max(t, now) rule).
            var z = e.RenderGraph(F, Mid, t => ex.Note(0, 440, 0.5f, 1f, (t - 1000) / (double)Sr))[0];
            Assert.That(FirstNonZero(z), Is.EqualTo(0));
        }

        /// <summary>A unit fade of 21 ms at 48 kHz (the length the old Classic sound switch used).</summary>
        const int FadeSamples = 1024;

        [Test]
        public void FadeOutSuspendsThenResumeResetsWithoutClicks()
        {
            using var e = WithExample(out var ex);
            var before = e.RenderGraph(4 * F, Mid, t => { if (t == 0) ex.Note(0, 220, 0.5f, 5f); })[0];
            float level = NullTest.Peak(before, 3 * F);
            float step = NullTest.MaxStep(before, F);
            e.Host.FadeUnit(UnitId.Example, false, FadeSamples);
            var fade = e.RenderGraph(6 * F, Mid)[0];
            Assert.That(NullTest.MaxStep(fade), Is.LessThanOrEqualTo(step * 1.01f), "the ramp never steps more than the tone itself");
            Assert.That(NullTest.Peak(fade, 0, 128), Is.GreaterThan(0.8f * level), "the fade starts from the full level");
            Assert.That(NullTest.Peak(fade, FadeSamples), Is.EqualTo(0f), "silent once the 1024-sample ramp ends");
            Assert.IsTrue(e.Host.IsSuspended(UnitId.Example));
            long blocks = ex.Slot->Blocks;
            var asleep = e.RenderGraph(2 * F, Mid, t => ex.Note(0, 330, 0.5f, 1f))[0];
            Assert.That(NullTest.Peak(asleep), Is.EqualTo(0f), "a suspended unit renders nothing and drops its events");
            Assert.That(ex.Slot->Blocks, Is.EqualTo(blocks), "and costs nothing");

            e.Host.FadeUnit(UnitId.Example, true, FadeSamples);
            var back = e.RenderGraph(2 * F, Mid)[0];
            Assert.That(NullTest.Peak(back), Is.EqualTo(0f), "the old ringing voice does not come back (reset)");
            Assert.That(ex.Resets, Is.EqualTo(1));
            ex.Note(0, 330, 0.5f, 1f);
            var note = e.RenderGraph(2 * F, Mid)[0];
            Assert.That(NullTest.Peak(note, 0, 64), Is.GreaterThan(0.1f), "a resumed unit plays at full level at once");
        }

        [Test]
        public void TheGraphSliceGoesThroughTheSourceBus()
        {
            using var e = WithExample(out var ex, sources: true);
            using var g = WithExample(out var gx);
            var viaSources = e.RenderSources(8 * F, Mid, t => { if (t == 0) ex.Note(0, 261.6f, 0.3f, 2f); })[0];
            var graphOnly = g.RenderGraph(8 * F, Mid, t => { if (t == 0) gx.Note(0, 261.6f, 0.3f, 2f); })[0];
            Assert.That(NullTest.Peak(viaSources), Is.GreaterThan(0.2f));
            for (int i = 0; i < graphOnly.Length; i++) Assert.That(viaSources[i], Is.EqualTo(graphOnly[i]), $"bit-exact through the source at {i}");
        }

        static int Samples(double s) => OfflineEngine.Samples(s);

        [Test]
        public void UnitHandlesAndTheRingCountDrops()
        {
            var h = UnitHandles.Make(null, UnitId.Harp, (77 << 5) | 21);
            Assert.IsTrue(UnitHandles.IsUnit(h));
            Assert.That(UnitHandles.Unit(h), Is.EqualTo(UnitId.Harp));
            Assert.That(UnitHandles.Payload(h), Is.EqualTo((77 << 5) | 21));
            Assert.IsFalse(UnitHandles.IsUnit(new VoiceHandle(null, 5)), "today's ids are positive");

            using var e = WithExample(out var ex);   // ring of 64 slots
            for (int i = 0; i < 70; i++) ex.Note(0, 440, 0.01f, 0.1f);
            Assert.That(ex.DroppedCommands, Is.EqualTo(6));
            e.RenderGraph(F, Mid);
            Assert.That(ex.Notes, Is.EqualTo(64));
        }

        // ---------------------------------------------------------------- one-shots (section 5.4)

        static int LowNoisePreset(AudioGraphHost h, float tau, int length = 0)
        {
            var p = new OneShotPreset { Length = length, LevelDb = -6f, Seed = 7 };
            DspSetup.SetBiquad(&p.Filter, 0, BiquadType.Lowpass, 200, 0, 0, Sr);
            p.EnvGain = new float4(1, 0, 0, 0);
            p.EnvMul = new float4(DspSetup.DecayMultiplier(tau, Sr), 0, 0, 0);
            p.Mix = new float4(1, 0, 0, 0);
            return h.AddOneShotPreset(p);
        }

        [Test]
        public void AStolenOneShotFadesOutOver128SamplesInsteadOfCutting()
        {
            var cfg = GraphConfig.QuestDefault(Sr); cfg.OneShots = 1; cfg.Ghosts = 1;
            using var e = new OfflineEngine(null, null, false, 1, cfg);
            int preset = LowNoisePreset(e.Host, 5f);
            var y = e.RenderGraph(16 * F, Mid, t =>
            {
                if (t == 0) e.Host.PlayOneShot(preset, 1f, SourceId.PianoMid, 1);
                if (t == 12 * F) e.Host.PlayOneShot(preset, 0f, SourceId.PianoMid, 2);   // silent thief, 64 ms later
            })[0];
            GraphState* g = e.Host.State;
            Assert.That(g->OneShotSteals, Is.EqualTo(1));
            int s0 = 12 * F;
            float level = NullTest.Peak(y, s0 - 8 * F, s0);
            Assert.That(level, Is.GreaterThan(0.01f));
            Assert.That(NullTest.Peak(y, s0, s0 + VoiceAllocator.StealFadeSamples), Is.GreaterThan(0f), "the victim still sounds from its ghost slot");
            Assert.That(NullTest.Peak(y, s0 + VoiceAllocator.StealFadeSamples), Is.EqualTo(0f), "gone after 128 samples");
            Assert.That(NullTest.MaxStep(y, s0 - 4, s0 + 200), Is.LessThan(0.1f * level), "no click: no step near the voice's level");
            Assert.That(g->Slots[1].State, Is.EqualTo(SlotState.Free), "the ghost slot is free again");
        }

        [Test]
        public void OneShotsExpireAfterTheirLengthAndReleaseFaster()
        {
            using var e = new OfflineEngine(null, null, false);
            int shortOne = LowNoisePreset(e.Host, 5f, 480), ringing = LowNoisePreset(e.Host, 5f);
            GraphState* g = e.Host.State;
            var y = e.RenderGraph(4 * F, Mid, t => { if (t == 0) { e.Host.PlayOneShot(shortOne, 1f, SourceId.PianoMid, 0, 10 / (double)Sr); } })[0];
            Assert.That(FirstNonZero(y), Is.EqualTo(10), "sample-accurate start");
            Assert.That(NullTest.Peak(y, 490), Is.EqualTo(0f), "480 samples, then free");
            Assert.That(g->ActiveOneShots, Is.EqualTo(0));
            var z = e.RenderGraph(40 * F, Mid, t =>
            {
                if (t == 4 * F) e.Host.PlayOneShot(ringing, 1f, SourceId.PianoMid, 42);
                if (t == 8 * F) e.Host.ReleaseOneShot(42, 0.005f);
            })[0];
            Assert.That(g->ActiveOneShots, Is.EqualTo(0), "the released voice decays below the cull level and frees itself");
            Assert.That(NullTest.Peak(z, 20 * F), Is.EqualTo(0f));
        }

        [Test]
        public void StealOrderMatchesThePolicyVector()
        {
            CollectionAssert.AreEqual(new[] { 1, 4, 0, 5 }, BurstScenarios.StealOrder());
        }

        // ---------------------------------------------------------------- CPU cap, denormals, budget, meter

        [Test]
        public void TheCpuCapFadesTheQuietestGroupsAndKeepsJustStruckOnes()
        {
            // 10 banks x 8 modes (2 groups each) 3 dB apart (more than the 2 dB LOD buckets), struck together: 20 groups for
            // a budget of 6 (and a reference with room for all).
            float[] Render(int budget, out int[] banks, out OfflineEngine engine)
            {
                var cfg = GraphConfig.QuestDefault(Sr); cfg.Budget = budget;
                var e = new OfflineEngine(null, null, false, 1, cfg);
                var ids = new int[10];
                for (int b = 0; b < 10; b++)
                {
                    var hz = new float[8]; var tau = new float[8]; var go = new float[8]; var a = new float[8];
                    for (int m = 0; m < 8; m++) { hz[m] = 200 + 37 * b + 410 * m; tau[m] = 3f; go[m] = 1f; a[m] = Mathf.Pow(10f, (-40f + 3f * b) / 20f); }
                    ids[b] = e.Host.AddBank((int)SourceId.PianoMid, hz, tau, go, null, a, null, null, null, null);
                }
                banks = ids; engine = e;
                return e.RenderGraph(12 * F, Mid, t => { if (t == 0) for (int b = 0; b < 10; b++) e.Host.Post(new GraphCommand { Op = GraphOp.Strike, Target = ids[b], A = 1f }); })[0];
            }
            var y = Render(6, out var banks, out var lod);
            var full = Render(1000, out _, out var all);
            using (lod) using (all)
            {
                GraphState* g = lod.Host.State;
                Assert.That(g->MaxActiveGroups, Is.EqualTo(20), "protected for 30 ms: all 20 groups play the attack");
                Assert.That(g->LodDropped, Is.EqualTo(14), "then the 14 quietest fade out");
                for (int b = 0; b < 10; b++)
                    Assert.That(g->Banks[banks[b]].ListCount, Is.EqualTo(b >= 7 ? 2 : 0), $"bank {b}: only the three loudest banks remain");
                Assert.That(all.Host.State->LodDropped, Is.EqualTo(0));
                int drop = (AudioGraph.ProtectSamples + F - 1) / F * F;   // the first block after the protection: the fade block
                var diff = new float[y.Length];
                for (int i = 0; i < y.Length; i++) diff[i] = y[i] - full[i];
                Assert.That(NullTest.Peak(diff, 0, drop), Is.EqualTo(0f), "bit-identical until the cap engages");
                Assert.That(Math.Abs(diff[drop]), Is.LessThan(1e-4f * NullTest.Peak(full)), "the fade starts at full gain: no step");
                Assert.That(NullTest.MaxStep(diff, drop, drop + F), Is.LessThan(1.5f * NullTest.MaxStep(diff, drop + F + 1, drop + 2 * F)),
                    "while fading, the removed part changes no faster than it moves once gone (a cut would step by its full level)");
                Assert.That(NullTest.Peak(diff, drop + F), Is.GreaterThan(0.1f), "and the dropped groups are really gone");
            }
        }

        [Test]
        public void DenormalsAreFlushedAndQuietGroupsRetire()
        {
            using var e = new OfflineEngine(null, null, false);
            var hz = new float[] { 100, 1000, 5000, 15000 }; var tau = new float[] { 0.005f, 0.01f, 0.02f, 0.05f };
            var go = new float[] { 1, 1, 1, 1 };
            int bank = e.Host.AddBank((int)SourceId.PianoMid, hz, tau, go, null, go, null, null, null, null);
            GraphState* g = e.Host.State;
            e.RenderGraph(Sr, Mid, t => { if (t == 0) e.Host.Post(new GraphCommand { Op = GraphOp.Strike, Target = bank, A = 1f }); });
            ModalGroup* mg = g->Groups + g->Banks[bank].First;
            Assert.That(g->Culled, Is.EqualTo(1), "the group retired below -110 dBFS");
            Assert.That(g->Banks[bank].ListCount, Is.EqualTo(0));
            for (int l = 0; l < 4; l++) { Assert.That(mg->Zr[l], Is.EqualTo(0f)); Assert.That(mg->Zi[l], Is.EqualTo(0f)); }
        }

        [Test]
        public void DenormProbeRunsInBothRanges()
        {
            // T-DENORM in the editor (the device test is the one that decides; section 6).
            DspKernels.DenormProbe(1 << 10, 1e-30f, 0.5f, 1e-30f);
            var sw = Stopwatch.StartNew();
            float a = DspKernels.DenormProbe(1 << 18, 1e-30f, 0.5f, 1e-30f);
            double normal = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            float b = DspKernels.DenormProbe(1 << 18, 1e-40f, 0.5f, 1e-40f);
            double sub = sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[BURST] T-DENORM (editor): normal {normal:F2} ms, subnormal {sub:F2} ms, ratio {sub / Math.Max(1e-6, normal):F2} (device: < 1.2 done, > 1.5 add mr_fpcr.c)");
            Assert.That(a, Is.EqualTo(2e-30f).Within(1e-31f), "y -> b / (1 - a)");
            Assert.That(float.IsNaN(b), Is.False);
        }

        [Test]
        public void TheAdaptiveBudgetFollowsTheRule()
        {
            Assert.That(AudioGraphHost.AdaptBudget(1200, 1300, 2000, 0), Is.EqualTo(1080), "p99 above 60 %: x 0.9");
            Assert.That(AudioGraphHost.AdaptBudget(1200, 500, 2000, 3), Is.EqualTo(1260), "below 30 % with drops: x 1.05");
            Assert.That(AudioGraphHost.AdaptBudget(1200, 500, 2000, 0), Is.EqualTo(1200), "no drops: unchanged");
            Assert.That(AudioGraphHost.AdaptBudget(420, 1900, 2000, 0), Is.EqualTo(400), "floor 400");
            Assert.That(AudioGraphHost.AdaptBudget(2000, 100, 2000, 9), Is.EqualTo(2048), "ceiling 2048");
            // a lowered budget climbs back to the configured one without LOD drops (the units' own caps), and no further
            Assert.That(AudioGraphHost.AdaptBudget(875, 500, 2000, 0, 1200), Is.EqualTo(919), "quiet, below the default: x 1.05");
            Assert.That(AudioGraphHost.AdaptBudget(1190, 500, 2000, 0, 1200), Is.EqualTo(1200), "back to the default, not past it");
            Assert.That(AudioGraphHost.AdaptBudget(1200, 500, 2000, 0, 1200), Is.EqualTo(1200), "at the default: unchanged");
            Assert.That(AudioGraphHost.AdaptBudget(875, 900, 2000, 0, 1200), Is.EqualTo(875), "between 30 and 60 %: unchanged");
            Assert.That(AudioGraphHost.AdaptBudget(1200, 500, 2000, 3, 1200), Is.EqualTo(1260), "drops still raise it past the default");
        }

        static long LoadTicks(double us) => (long)(us * Stopwatch.Frequency / 1e6);

        /// <summary>One second of blocks at us per block, the main thread's once-a-second Tick, then one block (SetBudget lands).</summary>
        static void LoadSecond(OfflineEngine e, ref long clock, double us)
        {
            for (int i = 0; i < 188; i++) { e.Host.Load.Record(LoadTicks(us), F, Sr, clock); clock += F; }
            e.Host.Tick(1.01f);
            e.RenderGraph(F, Mid);
        }

        [Test]
        public void TheAdaptiveBudgetRecoversAfterALoadSpike()
        {
            // As in the app: the units scale their caps by the budget (Graph->Budget) and no graph bank is LOD-dropped.
            using var e = new OfflineEngine(null, null, false);
            e.Host.AdaptiveBudget = true;   // OfflineEngine turns it off
            e.RenderGraph(F * 4, Mid);
            long clock = 1L << 40;
            int b0 = e.Host.Budget;
            for (int s = 0; s < 3; s++) LoadSecond(e, ref clock, 1500);   // p99 1.5 ms > 60 % of 2 ms
            int spike = e.Host.Budget;
            Assert.That(spike, Is.LessThan(b0), "three heavy seconds lower the budget");
            for (int s = 0; s < 60; s++) LoadSecond(e, ref clock, 100);   // a quiet minute (p99 0.1 ms < 30 %)
            Debug.Log($"[BURST] adaptive budget: {b0} -> {spike} after 3 heavy s -> {e.Host.Budget} after 60 quiet s (LOD drops {e.Host.LodDropped})");
            Assert.AreEqual(0, e.Host.LodDropped, "no graph bank was dropped (the app's case)");
            Assert.AreEqual(b0, e.Host.Budget, "the budget is back at the configured one");
        }

        [Test]
        public void TheLoadMeterPublishesAverageMaxP99AndGaps()
        {
            var m = new DspLoadMeter();
            double ticksPerUs = Stopwatch.Frequency / 1e6;
            for (int i = 0; i < 128; i++)
                m.Record((long)((i == 100 ? 1500 : 100) * ticksPerUs), F, Sr, (i < 64 ? i : i + 1) * (long)F);   // one gap at 64
            var s = m.Read();
            Assert.That(s.Blocks, Is.EqualTo(128));
            Assert.That(s.MaxMicros, Is.EqualTo(1500).Within(1));
            Assert.That(s.AvgMicros, Is.EqualTo(100 + 1400 / 128.0).Within(1));
            Assert.That(s.P99Micros, Is.EqualTo(150).Within(1), "the 127th of 128 blocks took 100 us: p99 = the upper edge of its 50 us bin");
            Assert.That(s.Gaps, Is.EqualTo(1));
            Assert.That(s.BlockMicros, Is.EqualTo(5333.3f).Within(0.1f));
            m.ResetPeaks();
            for (int i = 0; i < 64; i++) m.Record((long)(100 * ticksPerUs), F, Sr, (200 + i) * (long)F);
            s = m.Read();
            Assert.That(s.MaxMicros, Is.EqualTo(100).Within(1), "a new window");
            Assert.That(s.PeakMicros, Is.EqualTo(1500).Within(1), "the all-time peak stays");
        }

        [Test]
        public void WavFilesRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "musicroom-wav-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var y = new[] { new float[] { 0f, 0.5f, -0.5f, 0.999f }, new float[] { 0.25f, -1f, 1f, 0f } };
                WavFile.Write(path, y, Sr, 24);
                var z = WavFile.Read(path, out int sr);
                Assert.That(sr, Is.EqualTo(Sr));
                for (int c = 0; c < 2; c++) for (int i = 0; i < 4; i++) Assert.That(z[c][i], Is.EqualTo(y[c][i]).Within(2.5e-7));
                WavFile.Write(path, y, Sr, 32);
                z = WavFile.Read(path, out _);
                for (int c = 0; c < 2; c++) for (int i = 0; i < 4; i++) Assert.That(z[c][i], Is.EqualTo(y[c][i]));
                Assert.That(NullTest.ResidualDb(z[0], y[0]), Is.LessThan(-200));
            }
            finally { File.Delete(path); }
        }
    }
}
