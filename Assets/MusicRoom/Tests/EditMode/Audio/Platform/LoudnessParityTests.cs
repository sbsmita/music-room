// Loudness parity (12-new-instruments/platform.md 10.3): every unit-only role's mf reference phrase, dry (the offline
// engine's RenderSources sum of its sources), within +-2 LU of the piano's mf reference (Ode to Joy bar 1: E4 E4 F4 G4,
// quarters at 108 bpm from 0.2 s, vel 0.55, keys held 90 %: Loudness.PianoMfLufs, the level the lab's piano engine and
// the upgraded piano unit measured, which every unit was tuned to; the unit was deleted on 2026-11-11, the classic piano
// is reported beside it), integrated loudness per BS.1770-4 (K-weighting at 48 kHz, 400
// ms blocks every 100 ms, the -70 LKFS and -10 LU gates; Tools/SoundLab/burst/lab/common.mjs integratedLufs). Units
// whose descriptor is a phase-0 placeholder are exempt: their knock is measured and reported, not asserted. A real unit
// must offer its phrase (ILoudnessReference).
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public class LoudnessParityTests
    {
        const int Sr = OfflineEngine.Sr;
        /// <summary>The lab's piano mf reference (the hand-drums lab's parity.json pianoMfLufs, the Node piano engine).</summary>
        const double LabPianoMfLufs = Loudness.PianoMfLufs;
        public const double ParityLu = 2.0;

        /// <summary>BS.1770-4 integrated loudness (LKFS) of channels at 48 kHz.</summary>
        public static double IntegratedLufs(params float[][] channels)
        {
            // K-weighting biquads for 48 kHz from BS.1770-4 Annex 1 (Tables 1 and 2): b0 b1 b2, a1 a2
            double[][] kw =
            {
                new[] { 1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585 },
                new[] { 1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621 },
            };
            int n = channels[0].Length, block = (int)Math.Round(0.4 * Sr), hop = (int)Math.Round(0.1 * Sr);
            var z = new double[channels.Length][];
            for (int c = 0; c < channels.Length; c++)
            {
                var s = new double[n];
                for (int i = 0; i < n; i++) s[i] = channels[c][i];
                foreach (var q in kw)
                {
                    double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double v = q[0] * s[i] + q[1] * x1 + q[2] * x2 - q[3] * y1 - q[4] * y2;
                        x2 = x1; x1 = s[i]; y2 = y1; y1 = v; s[i] = v;
                    }
                }
                z[c] = s;
            }
            var power = new System.Collections.Generic.List<double>();
            for (int s0 = 0; s0 + block <= n; s0 += hop)
            {
                double sum = 0;
                foreach (var zc in z) { double e = 0; for (int i = s0; i < s0 + block; i++) e += zc[i] * zc[i]; sum += e / block; }
                power.Add(sum);
            }
            double Lk(double p) => -0.691 + 10 * Math.Log10(p);
            var abs = power.FindAll(p => Lk(p) > -70);
            if (abs.Count == 0) return double.NegativeInfinity;
            double Mean(System.Collections.Generic.List<double> a) { double m = 0; foreach (double v in a) m += v; return m / a.Count; }
            double gate = Lk(Mean(abs)) - 10;
            var rel = abs.FindAll(p => Lk(p) > gate);
            return Lk(Mean(rel));
        }

        static SourceId[] Sources(InstrumentDescriptor d)
        {
            var list = new System.Collections.Generic.List<SourceId>();
            foreach (var s in d.Audio.Sources) if (AudioSourceTable.Defaults[s.Source].Instrument) list.Add((SourceId)s.Source);
            return list.ToArray();
        }

        /// <summary>The classic piano's mf reference (today's PianoSynth on its rendered buffers, dry: PianoLo / Mid / Hi summed,
        /// 4 s), for the report: the piano the app plays.</summary>
        public static double ClassicPianoMfReference(out float peak)
        {
            using var a = new OfflineAudio();
            foreach (int m in new[] { 64, 65, 67 })
                a.Bank.Publish(AudioEngine.PianoSlot0 + m - 21, MusicRoom.Audio.Offline.PianoRenderer.Render(m, Sr, (uint)(1000 + m)));
            const double spb = 60.0 / 108.0;
            int[] notes = { 64, 64, 65, 67 };
            var held = new VoiceHandle[notes.Length];
            float[] mono = a.Render(OfflineEngine.Samples(4.0), new[] { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi }, blockStart =>
            {
                double t0 = blockStart / (double)Sr, t1 = (blockStart + OfflineAudio.Block) / (double)Sr;
                for (int i = 0; i < notes.Length; i++)
                {
                    double on = 0.2 + i * spb, off = on + 0.9 * spb;
                    if (on >= t0 && on < t1) held[i] = a.Piano.Play(notes[i], 0.55f, on);
                    if (off >= t0 && off < t1) a.Piano.Release(held[i], true, off);
                }
            });
            peak = NullTest.Peak(mono);
            return IntegratedLufs(mono);
        }

        [Test]
        public void TheLoudnessMeterMatchesTheStandardsReferencePoint()
        {
            // BS.1770: a 997 Hz sine at 0 dBFS peak on one channel reads -3.01 LKFS (K-weighting is ~0 dB at 1 kHz)
            var x = new float[Sr * 4];
            for (int i = 0; i < x.Length; i++) x[i] = Mathf.Sin(2 * Mathf.PI * 997f * i / Sr);
            Assert.That(IntegratedLufs(x), Is.EqualTo(-3.01).Within(0.05));
            var q = new float[x.Length];
            for (int i = 0; i < q.Length; i++) q[i] = x[i] * 0.1f;
            Assert.That(IntegratedLufs(q), Is.EqualTo(-23.01).Within(0.05), "-20 dB is -20 LU");
        }

        [Test]
        public void EveryUnitsMfReferenceIsWithinTwoLuOfThePianos()
        {
            double piano = LabPianoMfLufs;
            double classic = ClassicPianoMfReference(out float classicPeak);
            Debug.Log($"[LOUDNESS] piano mf reference (Ode to Joy bar 1, vel 0.55, dry): {piano:F2} LUFS (the lab's piano engine); the classic piano " +
                      $"(today's synth, dry, its sources summed) {classic:F2} LUFS, peak {20 * Math.Log10(classicPeak):F1} dBFS ({classic - piano:+0.00;-0.00} LU)");
            Assert.That(double.IsInfinity(classic) || double.IsNaN(classic), Is.False, "the classic piano's phrase is measured");

            var reg = InstrumentRegistry.Discover();
            int measured = 0;
            foreach (var d in reg.All)
            {
                if (d.Audio == null || !SoundUnits.IsUnitOnly((UnitId)d.Audio.Unit)) continue;
                var role = (UnitId)d.Audio.Unit;
                using var e = new OfflineEngine(role);
                SoundUnit unit = e.Unit(role);
                Assert.That(unit, Is.Not.Null, d.Id + " has a unit");
                if (!(unit is ILoudnessReference r))
                {
                    Assert.That(d.Placeholder, Is.True, $"{d.Id}: a real unit offers its mf reference phrase (ILoudnessReference)");
                    continue;
                }
                var src = Sources(d);
                r.PlayReference(0);
                var y = e.RenderSources(OfflineEngine.Samples(Math.Max(1.0, r.ReferenceSeconds)), src);
                double l = IntegratedLufs(OfflineEngine.Sum(y)), diff = l - piano;
                measured++;
                Debug.Log($"[LOUDNESS] {d.Id} ({role}{(d.Placeholder ? ", phase-0 stub knock: exempt, reported" : "")}): {l:F2} LUFS, {diff:+0.00;-0.00} LU re the piano's mf reference");
                Assert.That(double.IsNaN(l), Is.False, d.Id + " measured");
                if (!d.Placeholder) Assert.That(diff, Is.InRange(-ParityLu, ParityLu), $"{d.Id}: within +-{ParityLu} LU of the piano's mf reference");
            }
            Assert.That(measured, Is.EqualTo(4), "the four unit-only roles");
        }
    }
}
