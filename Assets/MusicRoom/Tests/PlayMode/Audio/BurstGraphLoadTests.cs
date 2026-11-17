// Burst graph benchmarks (category "Performance": Tools/unity.sh test-perf). burst.md section 13's T-KERNEL and
// T-DENORM measured in the editor (the device numbers are what count), the graph at the LOD ceiling rendered offline on
// the main thread, and the same load live on the booted app's audio thread through DspLoadMeter (ms per block with
// Burst on). Results are "[PERF] ..." lines (unity.sh test-perf prints them).
using System;
using System.Collections;
using System.Diagnostics;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Dsp;
using MusicRoom.Core;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.Audio
{
    [Category("Performance")]
    public unsafe class BurstGraphLoadTests
    {
        const int Sr = 48000, F = 256;
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); }

        static double Ns(Action run, long items)
        {
            run();
            var sw = Stopwatch.StartNew();
            run();
            return sw.Elapsed.TotalMilliseconds * 1e6 / items;
        }

        [Test]
        public void KernelCostsInTheEditor()
        {
            const int groups = 64, blocks = 400;
            long bytes = groups * sizeof(ModalGroup) + F * 16 + F * 4 + F * 16 + 6 * sizeof(WaveguideGroup) + 6 * 4 * 1024 * 4;
            byte* mem = (byte*)UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(mem, bytes);
            try
            {
                var g = (ModalGroup*)mem; var acc = (float4*)(mem + groups * sizeof(ModalGroup)); var x = (float*)(acc + F);
                for (int m = 0; m < 4 * groups; m++)
                {
                    DspSetup.SetMode(g, m, 80 + 13.7 * m, 4.0, 0.001f, 0.01f, Sr);
                    DspSetup.Strike(g, m, 0.1, 0.3 * m);
                }
                for (int n = 0; n < F; n++) x[n] = (n % 7) * 1e-3f;
                double free = Ns(() => { for (int b = 0; b < blocks; b++) { DspKernels.ModalFree(g, groups, acc, 0, F); for (int n = 0; n < F; n++) acc[n] = float4.zero; } }, (long)blocks * F * groups * 4);
                double driven = Ns(() => { for (int b = 0; b < blocks; b++) { DspKernels.ModalDriven(g, groups, x, acc, 0, F); for (int n = 0; n < F; n++) acc[n] = float4.zero; } }, (long)blocks * F * groups * 4);
                var exc = (float4*)(x + F); var wg = (WaveguideGroup*)(exc + F); var lines = (float*)(wg + 6);
                exc[0] = new float4(0.5f);
                double[] f0 = { 98, 131, 165, 196, 247, 294 };
                for (int s = 0; s < 6; s++) { wg[s].Len = 1024; wg[s].Mask = 1023; for (int l = 0; l < 4; l++) DspSetup.SetString(wg + s, l, f0[s] * (l + 1), 2.0, 0.6, 0.25f, Sr); }
                double wave = Ns(() => { for (int b = 0; b < blocks; b++) for (int s = 0; s < 6; s++) DspKernels.Waveguide4(wg + s, lines + s * 4096, exc, acc, 0, F); }, (long)blocks * F * 24);
                var bq = (Biquad4*)UnsafeUtility.Malloc(sizeof(Biquad4), 64, Allocator.Persistent);
                *bq = default;
                for (int l = 0; l < 4; l++) DspSetup.SetBiquad(bq, l, BiquadType.Bandpass, 500 + 700 * l, 2, 0, Sr);
                double biq = Ns(() => { for (int b = 0; b < blocks; b++) DspKernels.Biquad4Process(bq, acc, 0, F); }, (long)blocks * F * 4);
                UnsafeUtility.Free(bq, Allocator.Persistent);
                Debug.Log($"[PERF] burst={BurstCompiler.IsEnabled} T-KERNEL (editor, M-series core): MB4 free {free:F3} ns/mode-sample, MB4 driven {driven:F3}, WG4 {wave:F3} ns/string-sample, BQ4 {biq:F3} ns/filter-sample (device target: free <= 0.6)");
                if (BurstCompiler.IsEnabled) Assert.That(free, Is.LessThan(2.0), "Burst-compiled (managed is ~2.8 ns)");
            }
            finally { UnsafeUtility.Free(mem, Allocator.Persistent); }

            DspKernels.DenormProbe(1 << 12, 1e-30f, 0.5f, 1e-30f);
            var sw = Stopwatch.StartNew();
            DspKernels.DenormProbe(1 << 20, 1e-30f, 0.5f, 1e-30f);
            double normal = sw.Elapsed.TotalMilliseconds; sw.Restart();
            DspKernels.DenormProbe(1 << 20, 1e-40f, 0.5f, 1e-40f);
            double sub = sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[PERF] T-DENORM (editor): normal {normal:F1} ms, subnormal {sub:F1} ms, ratio {sub / normal:F2}");
        }

        /// <summary>150 banks x 32 modes (8 groups) = 1200 groups (the LOD ceiling), all struck, long decays; plus 64 one-shots.
        /// level: the output gain (the cost is the same at any level); strike: the strike amplitude (with level, above the
        /// -110 dBFS cull for the whole run); oneShotDb: the one-shots' level estimate (keeps them above their cull).</summary>
        static void LoadCeiling(AudioGraphHost h, float level, float strike, float oneShotDb, int[] banks, out int preset)
        {
            var hz = new float[32]; var tau = new float[32]; var go = new float[32]; var a = new float[32]; var gi = new float[32];
            for (int b = 0; b < banks.Length; b++)
            {
                for (int m = 0; m < 32; m++) { hz[m] = 60 + 7.3f * b + 97.1f * m; tau[m] = 10f; go[m] = level; a[m] = strike / (1 + 0.05f * m); gi[m] = 0.01f; }
                banks[b] = h.AddBank((int)SourceId.PianoMid, hz, tau, go, gi, a, null, null, null, null);
            }
            var p = new OneShotPreset { LevelDb = oneShotDb, Seed = 3, EnvGain = new float4(1, 0.5f, 0.25f, 0.1f), Mix = new float4(level) };
            for (int l = 0; l < 4; l++) DspSetup.SetBiquad(&p.Filter, l, BiquadType.Bandpass, 400 + 900 * l, 1.5, 0, Sr);
            p.EnvMul = new float4(DspSetup.DecayMultiplier(5.0, Sr));
            preset = h.AddOneShotPreset(p);
        }

        static void StrikeAll(AudioGraphHost h, int[] banks, int preset, bool pulse)
        {
            foreach (int b in banks) h.Stage(new GraphCommand { Op = pulse ? GraphOp.Pulse : GraphOp.Strike, Target = b, A = 0.8f, B = pulse ? 0.002f : 0.3f });
            for (int i = 0; i < 64; i++) h.Stage(new GraphCommand { Op = GraphOp.OneShot, Target = preset, A = 1f, N = (int)SourceId.PianoMid, Id = i + 1 });
            h.Commit();
        }

        [Test]
        public void GraphAtTheLodCeilingOffline()
        {
            using var h = new AudioGraphHost(GraphConfig.QuestDefault(Sr)) { AdaptiveBudget = false };
            var banks = new int[150];
            LoadCeiling(h, 1e-3f, 1f, -20f, banks, out int preset);
            StrikeAll(h, banks, preset, false);
            h.RenderOffline(0, F);
            const int blocks = 400;
            double sum = 0, max = 0;
            for (int k = 1; k <= blocks; k++)
            {
                if (k == 200) StrikeAll(h, banks, preset, true);   // the driven kernels too
                long t0 = Stopwatch.GetTimestamp();
                h.RenderOffline(k * (long)F, F);
                double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
                sum += us; max = Math.Max(max, us);
            }
            GraphState* g = h.State;
            Debug.Log($"[PERF] graph offline (main thread, burst={BurstCompiler.IsEnabled}): 1200 groups (4800 modes) + 64 one-shots: avg {sum / blocks / 1000:F3} ms, max {max / 1000:F3} ms per 256-frame block " +
                      $"({100 * sum / blocks / 5333.3:F1}% of 5.33 ms); active groups {g->ActiveGroups}/{g->Budget}, lod {g->LodDropped}, one-shots {g->ActiveOneShots}");
            Assert.That(g->MaxActiveGroups, Is.EqualTo(1200));
            if (BurstCompiler.IsEnabled) Assert.That(sum / blocks, Is.LessThan(2000), "far below the 2 ms target in the editor");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator GraphAtTheLodCeilingOnTheAudioThread()
        {
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
            var engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            var h = engine.Graph;
            Assert.IsNotNull(h);
            h.AdaptiveBudget = false;
            var banks = new int[150];
            LoadCeiling(h, 1e-6f, 20f, 120f, banks, out int preset);   // about -60 dBFS rms: the cost is the same at any level
            h.Load.ResetPeaks();
            yield return null;
            var idle = h.Load.Read();
            StrikeAll(h, banks, preset, false);
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - t0 < 0.3) yield return null;
            h.Load.ResetPeaks();
            t0 = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - t0 < 2.0) yield return null;
            var s = h.Load.Read();
            AudioSettings.GetDSPBufferSize(out int frames, out int buffers);
            Debug.Log($"[PERF] graph live (audio thread, burst={BurstCompiler.IsEnabled}), 1200 groups + 64 one-shots: avg {s.AvgMicros / 1000:F3} ms, p99 {s.P99Micros / 1000:F3} ms, max {s.MaxMicros / 1000:F3} ms per block " +
                      $"(block {s.BlockMicros / 1000:F2} ms, load {s.AvgLoad * 100:F1}%/{s.MaxLoad * 100:F1}%), idle graph before {idle.AvgMicros:F1} us, gaps {s.Gaps}, active groups {h.ActiveGroups}/{h.Budget}, buf {frames}x{buffers}");
            Debug.Log(DspLoadLog.Line(h, s, frames, buffers));
            Assert.That(h.ActiveGroups, Is.EqualTo(1200), "the load is really there");
            Assert.That(s.Blocks, Is.GreaterThan(100));
        }
    }
}
