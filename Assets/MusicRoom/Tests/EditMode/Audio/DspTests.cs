// DSP primitives against the reference formulas and the measured values of Docs/06-audio-dsp.md.
using System;
using MusicRoom.Audio;
using MusicRoom.Audio.Dsp;
using MusicRoom.Core;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio
{
    public class SpscRingTests
    {
        [Test]
        public void StagedItemsAppearTogetherOnCommit()
        {
            var ring = new SpscRing<SynthCommand>(8);
            Assert.IsTrue(ring.TryStage(new SynthCommand { VoiceId = 1 }));
            Assert.IsTrue(ring.TryStage(new SynthCommand { VoiceId = 2 }));
            Assert.IsFalse(ring.TryDequeue(out _), "nothing published before Commit");
            ring.Commit();
            Assert.IsTrue(ring.TryDequeue(out var a)); Assert.That(a.VoiceId, Is.EqualTo(1));
            Assert.IsTrue(ring.TryDequeue(out var b)); Assert.That(b.VoiceId, Is.EqualTo(2));
            Assert.IsFalse(ring.TryDequeue(out _));
        }

        [Test]
        public void FullRingRefusesWithoutLosingOrder()
        {
            var ring = new SpscRing<SynthCommand>(4);
            for (int i = 0; i < 4; i++) Assert.IsTrue(ring.TryEnqueue(new SynthCommand { VoiceId = i }));
            Assert.IsFalse(ring.TryEnqueue(new SynthCommand { VoiceId = 99 }), "full");
            Assert.IsTrue(ring.TryDequeue(out var first)); Assert.That(first.VoiceId, Is.EqualTo(0));
            Assert.IsTrue(ring.TryEnqueue(new SynthCommand { VoiceId = 4 }), "a slot was freed");
            int n = 1;
            while (ring.TryDequeue(out var c)) Assert.That(c.VoiceId, Is.EqualTo(n++));
            Assert.That(n, Is.EqualTo(5));
        }

        [Test]
        public void CapacityMustBeAPowerOfTwo()
        {
            Assert.Throws<ArgumentException>(() => new SpscRing<SynthCommand>(6));
        }
    }

    public class BiquadTests
    {
        const double Sr = 48000;

        // Measured biquad gain at f0: the reference Q semantics (lowpass/highpass Q in dB).
        [TestCase(BiquadType.Lowpass, 1000, 0.4, 0, 0.4)]
        [TestCase(BiquadType.Lowpass, 420, 1, 0, 1)]
        [TestCase(BiquadType.Lowpass, 520, 0.3, 0, 0.3)]
        [TestCase(BiquadType.Lowpass, 5000, 1, 0, 1)]
        [TestCase(BiquadType.Highpass, 6000, 1, 0, 1)]
        [TestCase(BiquadType.Highpass, 120, 0.7, 0, 0.7)]
        [TestCase(BiquadType.Bandpass, 1600, 2.5, 0, 0)]
        [TestCase(BiquadType.Bandpass, 650, 6, 0, 0)]
        [TestCase(BiquadType.Bandpass, 3200, 0.7, 0, 0)]
        [TestCase(BiquadType.Highshelf, 7500, 1, 1.5, 0.75)]
        [TestCase(BiquadType.Peaking, 2500, 0.8, 2, 2)]
        public void GainAtCornerMatchesTheMeasurement(BiquadType type, double f, double q, double g, double wantDb)
        {
            var b = Biquad.Create(type, f, q, g, Sr);
            Assert.That(b.MagnitudeDb(f, Sr), Is.EqualTo(wantDb).Within(0.01));
        }

        [Test]
        public void CoefficientsFollowTheSpecFormulas()
        {
            // lowpass 1000 Hz, Q 0.4 dB: alpha = sin w0 / (2 * 10^(0.4/20)).
            double w0 = 2 * Math.PI * 1000 / Sr, cw = Math.Cos(w0), sw = Math.Sin(w0);
            double al = sw / (2 * Math.Pow(10, 0.4 / 20));
            double a0 = 1 + al;
            var b = Biquad.Create(BiquadType.Lowpass, 1000, 0.4, 0, Sr);
            b.GetCoefficients(out double b0, out double b1, out double b2, out double a1, out double a2);
            Assert.That(b0, Is.EqualTo((1 - cw) / 2 / a0).Within(1e-9));
            Assert.That(b1, Is.EqualTo((1 - cw) / a0).Within(1e-9));
            Assert.That(b2, Is.EqualTo((1 - cw) / 2 / a0).Within(1e-9));
            Assert.That(a1, Is.EqualTo(-2 * cw / a0).Within(1e-9));
            Assert.That(a2, Is.EqualTo((1 - al) / a0).Within(1e-9));
        }

        [Test]
        public void ImpulseResponseIsStableAndUnityDcForLowpass()
        {
            var b = Biquad.Create(BiquadType.Lowpass, 350, 1, 0, Sr); // the reference defaults
            double sum = 0;
            for (int i = 0; i < 48000; i++) sum += b.Process(i == 0 ? 1f : 0f);
            Assert.That(sum, Is.EqualTo(1.0).Within(1e-3), "DC gain 1");
        }
    }

    public class EnvelopeTests
    {
        [Test]
        public void ExponentialRampHitsTheGeometricMidpointAndTheEnd()
        {
            // Measured ramp: setValueAtTime(0.3) + exponentialRamp to 0.0005: mid 0.0122474, end 0.0005, then hold.
            var e = new Envelope(2);
            e.SetValue(0.3);
            e.ExponentialTo(0.0005, 1000);
            float first = e.Next();
            Assert.That(first, Is.EqualTo(0.3f).Within(1e-7f), "the first sample is exactly the start value");
            for (int i = 1; i < 500; i++) e.Next();
            Assert.That(e.Next(), Is.EqualTo(0.0122474f).Within(2e-5f), "geometric midpoint");
            for (int i = 501; i < 1000; i++) e.Next();
            Assert.That(e.Next(), Is.EqualTo(0.0005f).Within(1e-7f), "lands on the target");
            Assert.That(e.Next(), Is.EqualTo(0.0005f).Within(1e-7f), "holds");
            Assert.IsTrue(e.IsSettled);
        }

        [Test]
        public void SetTargetDecaysWithTheTimeConstant()
        {
            var e = new Envelope(2);
            e.SetValue(1.0);
            e.SetTarget(0.0, 100.0); // tau = 100 samples
            for (int i = 0; i < 100; i++) e.Next();
            Assert.That(e.Value, Is.EqualTo(Math.Exp(-1.0)).Within(2e-3), "e^-1 after tau");
            for (int i = 0; i < 200; i++) e.Next();
            Assert.That(e.Value, Is.EqualTo(Math.Exp(-3.0)).Within(2e-3), "e^-3 after 3 tau");
        }
    }

    public class OscillatorTests
    {
        [Test]
        public void SquareTableMatchesTheMeasuredShape()
        {
            // Measured 1 s square at 263 Hz, 48 kHz: first [0, 0.95136, 0.84436, 0.82185, 0.87102, 0.84083], meanAbs 0.8434, max 0.9897.
            var T = PeriodicWaveTables.BuildWave(Wave.Square);
            double[] first = { 0, 0.95136, 0.84436, 0.82185, 0.87102, 0.84083 };
            double phase = 0, inc = 263.0 * PeriodicWaveTables.N / 48000.0, sumAbs = 0, max = 0;
            for (int k = 0; k < 48000; k++)
            {
                float v = PeriodicWaveTables.Sample(T, phase, 263.0, 48000);
                if (k < first.Length) Assert.That(v, Is.EqualTo(first[k]).Within(2e-3), "sample " + k);
                sumAbs += Math.Abs(v);
                max = Math.Max(max, Math.Abs(v));
                phase += inc;
                phase -= Math.Floor(phase / PeriodicWaveTables.N) * PeriodicWaveTables.N;
            }
            Assert.That(sumAbs / 48000, Is.EqualTo(0.8434).Within(2e-3));
            Assert.That(max, Is.EqualTo(0.9897).Within(3e-3));
        }

        [Test]
        public void SineTableIsASine()
        {
            var T = PeriodicWaveTables.BuildWave(Wave.Sine);
            double[] first = { 0, 0.13053, 0.25882, 0.38268, 0.5, 0.60876 };
            double phase = 0, inc = 1000.0 * PeriodicWaveTables.N / 48000.0;
            for (int k = 0; k < first.Length; k++)
            {
                Assert.That(PeriodicWaveTables.Sample(T, phase, 1000, 48000), Is.EqualTo(first[k]).Within(1e-4));
                phase += inc;
            }
            Assert.That(PeriodicWaveTables.PartialsForRange(0), Is.EqualTo(2047));
            Assert.That(PeriodicWaveTables.PartialsForRange(3), Is.EqualTo(1024));
        }

        /// The voices' oscillator selects the table range once per frequency; its samples must be exactly the 06 reference
        /// path (PeriodicWaveTables.Sample recomputing the range every sample) for a constant frequency.
        [TestCase(Wave.Square, 263.0)]
        [TestCase(Wave.Square, 845.0 * 1.9)]
        [TestCase(Wave.Sine, 2900.0)]
        [TestCase(Wave.Triangle, 195.0)]
        [TestCase(Wave.Sine, 20.0)]
        [TestCase(Wave.Square, 15000.0)]
        public void HoistedRangeIsBitIdenticalToThePerSampleReference(Wave wave, double hz)
        {
            PeriodicWaveTables.Build();
            var T = PeriodicWaveTables.Get(wave);
            var o = new Oscillator();
            o.Start(wave, hz, 48000, true);
            Assert.IsTrue(o.UsingPeriodicTables);
            double phase = 0, inc = Math.Min(hz / 48000.0, 0.5);
            for (int k = 0; k < 48000; k++)
            {
                double t = phase;
                phase += inc;
                if (phase >= 1.0) phase -= 1.0;
                float want = PeriodicWaveTables.Sample(T, t * PeriodicWaveTables.N, hz, 48000);
                float got = o.Next();
                if (got != want) Assert.Fail($"sample {k}: {got:R} != {want:R}");
            }
        }

        /// Glides move the range incrementally (one log per glide): within float rounding of the per-sample recompute.
        [Test]
        public void GlideRangeTracksThePerSampleReference()
        {
            PeriodicWaveTables.Build();
            var T = PeriodicWaveTables.Get(Wave.Sine);
            const double f0 = 165, f1 = 46; const int n = 4320; // the kick's 90 ms glide
            double factor = Math.Pow(f1 / f0, 1.0 / n), step = Oscillator.RangeStep(factor);
            var o = new Oscillator();
            o.Start(Wave.Sine, f0, 48000, true);
            double phase = 0, inc = f0 / 48000.0, hz = f0, maxErr = 0;
            for (int k = 0; k < n + 100; k++)
            {
                double t = phase;
                phase += inc;
                if (phase >= 1.0) phase -= 1.0;
                float want = PeriodicWaveTables.Sample(T, t * PeriodicWaveTables.N, hz, 48000);
                maxErr = Math.Max(maxErr, Math.Abs(o.Next() - want));
                if (k < n) { o.ScaleFrequency(factor, step); hz *= factor; inc = Math.Min(inc * factor, 0.5); }
            }
            Assert.That(maxErr, Is.LessThan(1e-6));
        }

        /// The pre-rendered metal sums are the live voice's six-oscillator sum, sample for sample: each oscillator equals
        /// the per-sample reference path, and the six are added in MetalVoice's own expression (one six-term float sum).
        [Test]
        public void MetalBankSumEqualsTheLiveSixOscillators()
        {
            PeriodicWaveTables.Build();
            var T = PeriodicWaveTables.Get(Wave.Square);
            const int n = 9600;
            var sum = MetalBank.Render(1.7f, 48000, n);
            var o = new float[6][];
            for (int k = 0; k < 6; k++)
            {
                o[k] = new float[n];
                double hz = (float)(MetalBank.Partials[k] * 1.7f), inc = Math.Min(hz / 48000.0, 0.5), phase = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = phase;
                    phase += inc;
                    if (phase >= 1.0) phase -= 1.0;
                    o[k][i] = PeriodicWaveTables.Sample(T, t * PeriodicWaveTables.N, hz, 48000);
                }
            }
            for (int i = 0; i < n; i++)
            {
                float x = o[0][i] + o[1][i] + o[2][i] + o[3][i] + o[4][i] + o[5][i];
                if (x != sum[i]) Assert.Fail($"sample {i}: {sum[i]:R} != {x:R}");
            }
            MetalBank.Build(48000);
            Assert.IsTrue(MetalBank.Ready);
            Assert.That(MetalBank.Get(1.7f, 48000).Length, Is.EqualTo((int)Math.Ceiling(MetalBank.Seconds * 48000)));
            Assert.That(MetalBank.Get(1.7f, 44100), Is.Null, "built for one sample rate");
        }

        [Test]
        public void PolyBlepSquareHasTheMeasuredPlateau()
        {
            Oscillator.PreferPeriodicTables = false;
            var o = new Oscillator();
            o.Start(Wave.Square, 263, 48000);
            double sumAbs = 0;
            for (int k = 0; k < 48000; k++) sumAbs += Math.Abs(o.Next());
            Oscillator.PreferPeriodicTables = true;
            Assert.That(sumAbs / 48000, Is.EqualTo(0.8434).Within(0.02));
        }
    }

    public class CompressorTests
    {
        // The compressor's measured static curve: [inputPeakDbFS, outputGainDb incl. makeup, reportedReductionDb].
        // The numbers are the STEADY gain of the running kernel for a 1 kHz sine at that peak (spec 06 section 4):
        // between peaks the detector recovers, so they sit above the peak-only static curve (by 0.08 dB at 0 dBFS).
        static readonly double[][] MeasuredStaticCurve =
        {
            new[] { -60.0, 2.136, -0.035 }, new[] { -40.0, 2.136, -0.035 }, new[] { -20.0, 2.136, -0.035 }, new[] { -14.0, 2.136, -0.035 },
            new[] { -12.0, 2.073, -0.1 }, new[] { -10.0, 1.879, -0.296 }, new[] { -8.0, 1.548, -0.627 }, new[] { -6.0, 1.078, -1.1 },
            new[] { -4.0, 0.451, -1.728 }, new[] { -2.0, -0.351, -2.526 }, new[] { 0.0, -1.344, -3.515 }, new[] { 2.0, -2.531, -4.7 },
            new[] { 4.0, -3.914, -6.079 }, new[] { 6.0, -5.369, -7.541 }, new[] { 8.0, -6.827, -8.998 }, new[] { 10.0, -8.276, -10.459 },
            new[] { 12.0, -9.75, -11.923 },
        };

        [Test]
        public void DerivedConstantsMatchTheMeasurement()
        {
            // Measured derived constants: k 1.51254, makeupDb 2.137, saturate(1.0) 0.66366.
            var c = new DynamicsCompressor(48000);
            Assert.That(c.K, Is.EqualTo(1.51254).Within(1e-3));
            Assert.That(c.MakeupDb, Is.EqualTo(2.137).Within(5e-3));
            Assert.That(c.Saturate(1.0), Is.EqualTo(0.66366).Within(1e-4));
        }

        [Test]
        public void SteadySineGainMatchesTheMeasuredCurve([Values(0.0, 0.006)] double preDelay)
        {
            // Drive Process (the code the MasterBus runs) with a stereo 1 kHz sine for 0.6 s, past the start-up transient,
            // and compare output peak / input peak over the last 0.1 s. The reference matches the curve to 0.01 dB; this code
            // lands within 0.001 dB at every level, with or without the 6 ms pre-delay (288 frames = 6 periods at 1 kHz).
            // 256-frame blocks in place, exactly as MasterBus.OnAudioFilterRead feeds it.
            const int sr = 48000, block = 256, blocks = 113, window = 4800;   // 113 blocks = 0.603 s
            var buf = new float[2 * block];
            foreach (var p in MeasuredStaticCurve)
            {
                var c = new DynamicsCompressor(sr, preDelaySeconds: preDelay, channels: 2);
                double amp = DynamicsCompressor.Db2Lin(p[0]), peak = 0;
                for (int b = 0; b < blocks; b++)
                {
                    int start = b * block;
                    for (int i = 0; i < block; i++) buf[2 * i] = buf[2 * i + 1] = (float)(amp * Math.Sin(2 * Math.PI * 1000 * (start + i) / sr));
                    c.Process(buf, 2, block);
                    for (int i = 0; i < block; i++) if (start + i >= blocks * block - window) peak = Math.Max(peak, Math.Abs(buf[2 * i]));
                }
                Assert.That(DynamicsCompressor.Lin2Db(peak / amp), Is.EqualTo(p[1]).Within(0.01), "input " + p[0] + " dBFS");
            }
        }

        [Test]
        public void PeakStaticCurveIsTheRippleFreeApproximation()
        {
            // StaticGainDb ignores the detector ripple: exact makeup below the threshold, and below the measured steady sine
            // gain above it by at most 0.08 dB up to 0 dBFS (diagnostic helper; tests of the real path drive Process).
            var c = new DynamicsCompressor(48000);
            Assert.That(c.StaticGainDb(-30), Is.EqualTo(c.MakeupDb).Within(1e-9));
            foreach (var p in MeasuredStaticCurve)
            {
                if (p[0] > 0) continue;
                double d = c.StaticGainDb(p[0]) - p[1];
                Assert.That(d, Is.InRange(-0.085, 0.005), "input " + p[0] + " dBFS");
            }
        }

        [Test]
        public void ImpulseAfterStartUpMatchesTheMeasurementWithThePreDelay()
        {
            // Measured impulse: 1e-3 at frame 1000 -> frame 1288 (6 ms pre-delay), gain 0.45801 (start-up transient).
            var c = new DynamicsCompressor(48000, preDelaySeconds: 0.006, channels: 2);
            Assert.That(c.PreDelayFrames, Is.EqualTo(288));
            var data = new float[2 * 2000];
            data[1000 * 2] = data[1000 * 2 + 1] = 1e-3f;
            c.Process(data, 2, 2000);
            int at = -1;
            for (int i = 0; i < 2000; i++) if (Math.Abs(data[i * 2]) > 1e-9f) { at = i; break; }
            Assert.That(at, Is.EqualTo(1288));
            Assert.That(data[1288 * 2] / 1e-3, Is.EqualTo(0.45801).Within(2e-3));
        }

        [Test]
        public void NoPreDelayAddsNoLatency()
        {
            var c = new DynamicsCompressor(48000, preDelaySeconds: 0, channels: 2);
            var data = new float[2 * 400];
            data[100 * 2] = data[100 * 2 + 1] = 1e-3f;
            c.Process(data, 2, 400);
            int at = -1;
            for (int i = 0; i < 400; i++) if (Math.Abs(data[i * 2]) > 1e-9f) { at = i; break; }
            Assert.That(at, Is.EqualTo(100));
        }
    }

    public class NoiseAndIrTests
    {
        [Test]
        public void NoiseBufferSeed1MatchesTheFingerprint()
        {
            var n = NoiseTable.Generate(48000, 1);
            Assert.That(n.Length, Is.EqualTo(96000));
            Assert.That(n[0], Is.EqualTo(0.254147887f).Within(1e-7f));
            Assert.That(n[1], Is.EqualTo(-0.994528532f).Within(1e-7f));
            Assert.That(n[2], Is.EqualTo(0.0548940785f).Within(1e-7f));
            Assert.That(n[10000], Is.EqualTo(-0.675121129f).Within(1e-7f));
            Assert.That(n[95999], Is.EqualTo(-0.396879584f).Within(1e-7f));
            double peak = 0, sq = 0;
            foreach (var v in n) { peak = Math.Max(peak, Math.Abs(v)); sq += v * v; }
            Assert.That(peak, Is.EqualTo(0.99998194).Within(1e-6));
            Assert.That(Math.Sqrt(sq / n.Length), Is.EqualTo(0.576882854).Within(1e-6));
        }

        [Test]
        public void PianoBoardImpulseMatchesTheFingerprint()
        {
            var rand = new Mulberry32(2);
            var d = Notes.MakeBodyImpulse(48000, AudioRig.PianoBoardModes, 0.28, ref rand);
            Assert.That(d.Length, Is.EqualTo(13440));
            Assert.That(d[0], Is.EqualTo(-0.0253473762f).Within(1e-6f));
            Assert.That(d[1000], Is.EqualTo(0.029704079f).Within(1e-6f));
            Assert.That(d[13439], Is.EqualTo(0.00445597991f).Within(1e-6f));
            Assert.That(Notes.ConvolverScale(new[] { d }, 48000), Is.EqualTo(0.03065467522967764).Within(1e-7));
        }

        [Test]
        public void RoomImpulsesMatchTheFingerprints()
        {
            var rand = new Mulberry32(5);
            var main = Notes.MakeRoomImpulse(48000, 2.0, 0.014, 0.32, 0.62, 0.11, 1.0, ref rand);
            Assert.That(main[0].Length, Is.EqualTo(96000));
            Assert.That(main[0][1000], Is.EqualTo(-0.316427052f).Within(1e-6f));
            Assert.That(main[1][1000], Is.EqualTo(0.788458586f).Within(1e-6f));
            Assert.That(Notes.ConvolverScale(main, 48000), Is.EqualTo(0.005668360040085107).Within(1e-8));
            rand = new Mulberry32(6);
            var drum = Notes.MakeRoomImpulse(48000, 0.45, 0.004, 0.3, 0.45, 0.3, 0.8, ref rand);
            Assert.That(drum[0][1000], Is.EqualTo(0.385866672f).Within(1e-6f));
            Assert.That(drum[1][1000], Is.EqualTo(0.952661753f).Within(1e-6f));
            Assert.That(Notes.ConvolverScale(drum, 48000), Is.EqualTo(0.004307626969399304).Within(1e-8));
        }

        [Test]
        public void PannerGainMatchesTheMeasuredValues()
        {
            Assert.That(Notes.PannerGain(0.8), Is.EqualTo(0.73171).Within(1e-5));
            Assert.That(Notes.PannerGain(1), Is.EqualTo(0.57692).Within(1e-5));
            Assert.That(Notes.PannerGain(2), Is.EqualTo(0.28037).Within(1e-5));
            Assert.That(Notes.PannerGain(5), Is.EqualTo(0.11029).Within(1e-5));
            Assert.That(Notes.PannerGain(0.3), Is.EqualTo(1.0).Within(1e-9));
        }
    }
}
