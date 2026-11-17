// Audio CPU benchmarks (category "Performance": Tools/unity.sh test-perf; excluded from test-play). Spec 04's E8 / E12
// numbers predate the periodic-wave tables (measured on the PolyBLEP path); these re-measure the code that runs on device
// once the render workers have built the tables and the metal sums:
//  1. per-voice cost of the six-square metal sum: the old per-sample range selection (PeriodicWaveTables.Sample, the 06
//     reference path), the hoisted table oscillator, PolyBLEP, and the pre-rendered MetalBank read;
//  2. the E12 busy groove (4 s: 16th-note drums, crash, ride, 30 held piano notes, harp plucks every 125 ms, all beds)
//     in the booted app, per-source average / max microseconds per 256-frame block.
// Results are logged as "[PERF] ..." lines (unity.sh test-perf prints them) and recorded in ARCHITECTURE section 17.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MusicRoom.Audio;
using MusicRoom.Audio.Dsp;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.Audio
{
    [Category("Performance")]
    public class AudioLoadTests
    {
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        static double NsPerSample(Func<int, double> run, int samples)
        {
            run(4800); // warm up the JIT
            var sw = Stopwatch.StartNew();
            double acc = run(samples);
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / samples;
            if (double.IsNaN(acc)) Debug.Log("nan");
            return ns;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MetalVoiceCostAndBusyGrooveWithPeriodicWaveTables()
        {
            var engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            int sr = engine.SampleRate;
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (!engine.Renders.IsDone && Time.realtimeSinceStartupAsDouble - t0 < 30) yield return null;
            Debug.Log($"[PERF] renders: {engine.Renders.Completed}/{engine.Renders.Total} in {(Time.realtimeSinceStartupAsDouble - t0) * 1000:0} ms on {engine.renderWorkers} workers, failed {engine.Renders.Failed}, bank {engine.Bank.TotalSamples() * 4 / 1048576.0:0.0} MiB");
            Assert.IsTrue(engine.Renders.IsDone, "renders finished");
            Assert.IsTrue(PeriodicWaveTables.Ready && MetalBank.Ready, "tables and metal sums built");

            // 1. Six-square metal sum per sample (ride ratio 1.3), 2 s of samples per variant.
            float ratio = 1.3f;
            int n = 2 * sr;
            var T = PeriodicWaveTables.Get(Wave.Square);
            double perSampleRange = NsPerSample(m =>
            {
                double acc = 0;
                var phase = new double[6]; var inc = new double[6]; var hz = new double[6];
                for (int k = 0; k < 6; k++) { hz[k] = (float)(MetalBank.Partials[k] * ratio); inc[k] = hz[k] * PeriodicWaveTables.N / sr; }
                for (int i = 0; i < m; i++)
                {
                    float x = 0;
                    for (int k = 0; k < 6; k++)
                    {
                        x += PeriodicWaveTables.Sample(T, phase[k], hz[k], sr);
                        phase[k] += inc[k]; if (phase[k] >= PeriodicWaveTables.N) phase[k] -= PeriodicWaveTables.N;
                    }
                    acc += x;
                }
                return acc;
            }, n);
            double hoisted = NsPerSample(m =>
            {
                var o = new Oscillator[6];
                for (int k = 0; k < 6; k++) o[k].Start(Wave.Square, (float)(MetalBank.Partials[k] * ratio), sr, true);
                double acc = 0;
                for (int i = 0; i < m; i++) acc += o[0].Next() + o[1].Next() + o[2].Next() + o[3].Next() + o[4].Next() + o[5].Next();
                return acc;
            }, n);
            double polyblep = NsPerSample(m =>
            {
                var o = new Oscillator[6];
                for (int k = 0; k < 6; k++) o[k].Start(Wave.Square, (float)(MetalBank.Partials[k] * ratio), sr, false);
                double acc = 0;
                for (int i = 0; i < m; i++) acc += o[0].Next() + o[1].Next() + o[2].Next() + o[3].Next() + o[4].Next() + o[5].Next();
                return acc;
            }, n);
            var sum = MetalBank.Get(ratio, sr);
            double bank = NsPerSample(m => { double acc = 0; for (int i = 0; i < m; i++) acc += sum[i % sum.Length]; return acc; }, n);
            Debug.Log($"[PERF] metal 6-square sum ns/sample (editor Mono): per-sample range (old) {perSampleRange:F1}, hoisted tables {hoisted:F1}, PolyBLEP {polyblep:F1}, pre-rendered bank {bank:F1}");
            Assert.That(hoisted, Is.LessThan(perSampleRange), "hoisting the range selection is cheaper");
            yield return null;

            // 2. E12 busy groove for 4 s (spec 04 AudioEngineLoadTests), tables and metal sums ready.
            foreach (var s in engine.Sources) s.ResetStats();
            engine.SetFire(true); engine.SetWind(true); engine.SetSnow(true);
            var held = new Queue<KeyValuePair<double, VoiceHandle>>(256);
            double start = Time.realtimeSinceStartupAsDouble, next = start;
            int step = 0, peakHeld = 0;
            while (Time.realtimeSinceStartupAsDouble - start < 4.0)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now >= next)
                {
                    engine.Drum(step % 4 == 0 ? DrumKind.Kick : DrumKind.Hat, 0.8f, DrumStrike.Default);
                    if (step % 8 == 4) engine.Drum(DrumKind.Snare, 0.9f, DrumStrike.Default);
                    if (step % 16 == 0) engine.Drum(DrumKind.Crash, 0.9f, new DrumStrike { Pos = 0.6f, Zone = DrumZone.Bow });
                    if (step % 2 == 1) engine.Drum(DrumKind.Ride, 0.6f, new DrumStrike { Pos = 0.5f, Zone = DrumZone.Bow });
                    if (step % 6 == 3) engine.Drum(DrumKind.Tom2, 0.7f, DrumStrike.Default);
                    if (step % 4 == 0)
                        for (int k = 0; k < 6; k++) held.Enqueue(new KeyValuePair<double, VoiceHandle>(now + 2.0, engine.Piano(36 + (step * 7 + k * 9) % 60, 0.75f)));
                    engine.Pluck(step % 22, 0.6f);
                    step++;
                    next += 0.125;
                }
                while (held.Count > 0 && held.Peek().Key <= now) engine.Release(held.Dequeue().Value, false);
                peakHeld = Math.Max(peakHeld, held.Count);
                yield return null;
            }
            engine.SetFire(false); engine.SetWind(false); engine.SetSnow(false);
            var sb = new StringBuilder("[PERF] groove per-source us/block (avg/max): ");
            double sumAvg = 0, sumMax = 0;
            int dropped = 0, stolen = 0;
            foreach (var s in engine.Sources)
            {
                sb.Append($"{s.id} {s.AvgBlockMicros:0.0}/{s.MaxBlockMicros:0.0}; ");
                sumAvg += s.AvgBlockMicros;
                sumMax += s.MaxBlockMicros;
                dropped += s.DroppedCommands;
                stolen += s.StolenVoices;
            }
            Debug.Log(sb.ToString());
            AudioSettings.GetDSPBufferSize(out int frames, out _);
            double blockUs = frames * 1e6 / sr;
            Debug.Log($"[PERF] groove total of averages {sumAvg:0} us ({100 * sumAvg / blockUs:0.0}% of a {blockUs:0} us block), total of maxima {sumMax:0} us; held piano notes peak {peakHeld}; dropped {dropped}, stolen {stolen}; Ride {engine.Source(SourceId.Ride).AvgBlockMicros:0.0}/{engine.Source(SourceId.Ride).MaxBlockMicros:0.0} Crash {engine.Source(SourceId.Crash).AvgBlockMicros:0.0}/{engine.Source(SourceId.Crash).MaxBlockMicros:0.0} Hat {engine.Source(SourceId.Hat).AvgBlockMicros:0.0}/{engine.Source(SourceId.Hat).MaxBlockMicros:0.0}");
            Assert.That(dropped, Is.EqualTo(0), "no dropped commands");
        }
    }
}
