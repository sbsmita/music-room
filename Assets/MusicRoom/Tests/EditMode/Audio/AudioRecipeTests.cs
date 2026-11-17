// The Audio module's recipes and DSP pieces checked against their definitions (Docs/06-audio-dsp.md and its measured
// reference behaviour) where a reference WAV does not cover them: exactness of the fast table build, the truncated
// body convolution, the drum room's IR, the robot chain, the resampler, the mouth window, the pop timer, gusts,
// chirp scheduling, jitter draw order, releases and damps, chokes and the drum-room routing.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Offline;
using MusicRoom.Audio.Synths;
using MusicRoom.Audio.Voices;
using MusicRoom.Core;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio
{
    public class TableAndEnvelopeTests
    {
        [TestCase(Wave.Sine)]
        [TestCase(Wave.Square)]
        [TestCase(Wave.Triangle)]
        public void FastTableBuildIsBitIdenticalToTheReferenceAlgorithm(Wave wave)
        {
            var slow = PeriodicWaveTables.BuildWave(wave);
            var fast = PeriodicWaveTables.BuildWaveFast(wave);
            for (int r = 0; r < PeriodicWaveTables.Ranges; r++)
                for (int j = 0; j < PeriodicWaveTables.N; j++)
                    if (slow[r][j] != fast[r][j]) Assert.Fail($"{wave} range {r} index {j}: {slow[r][j]} vs {fast[r][j]}");
        }

        [Test]
        public void ExponentialRampOfFractionalLengthEndsAtTheTrueTime()
        {
            // The knock's 9.8 ms decay is 470.4 samples: the ramp evaluates P (T/P)^(t / decay) on the frame time.
            double P = 0.197, T = 0.0005, n = 470.4;
            var e = new Envelope(2);
            e.SetValue(P);
            e.ExponentialTo(T, n);
            for (int k = 0; k <= 471; k++)
            {
                float v = e.Next();
                double want = k < n ? P * Math.Pow(T / P, k / n) : T;
                Assert.That(v, Is.EqualTo(want).Within(want * 1e-6), "sample " + k);
            }
        }
    }

    public class BodyAndRoomTests
    {
        const int Sr = 48000;

        [Test]
        public void SoundboardResonatorEqualsTheTruncatedConvolution()
        {
            // makeBodyImpulse(seed 2) x the convolver's normalisation, then nothing after len = 13440 samples.
            var rand = new Mulberry32(2);
            float[] d = Notes.MakeBodyImpulse(Sr, AudioRig.PianoBoardModes, 0.28, ref rand);
            double scale = Notes.ConvolverScale(new[] { d }, Sr);
            var res = new ModalResonator();
            res.Build(AudioRig.PianoBoardModes, 0.28f, Sr, 2u);
            Assert.That(res.Length, Is.EqualTo(d.Length));
            int n = 2 * d.Length;
            var input = new float[n];
            var output = new float[n];
            input[0] = 1f;
            for (int at = 0; at < n; at += 256) ProcessBlock(res, input, output, at, Math.Min(256, n - at));
            double peak = 0;
            for (int i = 0; i < d.Length; i++) peak = Math.Max(peak, Math.Abs(d[i] * scale));
            for (int i = 0; i < n; i++)
            {
                double want = i < d.Length ? d[i] * scale : 0;
                Assert.That(output[i], Is.EqualTo(want).Within(peak * 2e-6), "sample " + i);
            }
        }

        static void ProcessBlock(ModalResonator r, float[] input, float[] output, int at, int frames)
        {
            var inBlock = new float[frames];
            var outBlock = new float[frames];
            Array.Copy(input, at, inBlock, 0, frames);
            r.ProcessAdd(inBlock, outBlock, frames, 1f);
            Array.Copy(outBlock, 0, output, at, frames);
        }

        [Test]
        public void DrumRoomIsTheSeededImpulseResponseExactly()
        {
            var room = new DrumRoomReverb(Sr, 6);
            Assert.That(room.Scale, Is.EqualTo(0.004307626969399304).Within(1e-9), "06 fingerprint impulseResponses.drumRoom.scale");
            float[] h = room.Impulse;
            Assert.That(h.Length, Is.EqualTo(21600));
            var rand = new Mulberry32(6);
            var ir = Notes.MakeRoomImpulse(Sr, 0.45, 0.004, 0.3, 0.45, 0.3, 0.8, ref rand);
            Assert.That(ir[0][1000], Is.EqualTo(0.385866672f).Within(1e-6f), "06 fingerprint drumRoom ch0[1000]");
            Assert.That(h[1000], Is.EqualTo((float)(ir[0][1000] * room.Scale)).Within(1e-9f));

            // An impulse through the partitioned convolution returns h, then exact silence, and the room goes idle.
            int n = h.Length + 4096;
            var block = new float[256];
            double peak = 0;
            foreach (var v in h) peak = Math.Max(peak, Math.Abs(v));
            for (int at = 0; at < n; at += 256)
            {
                Array.Clear(block, 0, 256);
                if (at == 0) block[0] = 1f;
                room.Process(block, at, 256);
                for (int i = 0; i < 256; i++)
                {
                    double want = at + i < h.Length ? h[at + i] : 0;
                    Assert.That(block[i], Is.EqualTo(want).Within(peak * 1e-5), "sample " + (at + i));
                }
            }
            long steps = room.Steps;
            for (int k = 0; k < 4; k++) { Array.Clear(block, 0, 256); room.Process(block, n + k * 256, 256); }
            Assert.That(room.Steps, Is.EqualTo(steps), "idle: no partitions computed while the input and the tail are silent");
        }

        [Test]
        public void DrumRoomHearsTheHitsButNotTheWiresOrTheChokeSkin()
        {
            using (var a = new OfflineAudio(drumRoom: true))
            {
                var snare = a.S(SourceId.Snare);
                // The snare wires go straight to drum:snare (NoSend): the room stays silent.
                a.Kit.Noise(snare, 0, hp: 2800, lp: 9000, peak: 0.07f, decay: 0.08f, flags: SynthFlags.NoSend);
                a.Kit.Flush();
                float[] room = a.Render(9600, SourceId.DrumRoom);
                Assert.That(Energy(room), Is.EqualTo(0), "no room send for a NoSend voice");
                // A snare hit's own layers go through the hit's dest gain and its 0.5 room send.
                a.Drums.Hit(DrumKind.Snare, 0.8f, DrumStrike.Default, 0);
                room = a.Render(9600, SourceId.DrumRoom);
                Assert.That(Energy(room), Is.GreaterThan(1e-6), "the hit reaches the drum room");
            }
        }

        static double Energy(float[] x) { double e = 0; foreach (var v in x) e += v * v; return e; }

        [Test]
        public void BassAndTrebleNotesSoundTheOneSoundboardOnPianoMid()
        {
            using (var a = new OfflineAudio())
            {
                a.Bank.Publish(AudioEngine.PianoSlot0, PianoRenderer.Render(21, Sr, 1021u));
                a.Piano.Play(21, 0.7f, 0);
                float[] mid = a.Render(9600, SourceId.PianoMid);
                Assert.That(Energy(mid), Is.GreaterThan(1e-6), "A0 plays on piano-lo; its soundboard return plays on piano-mid");
                Assert.That(a.S(SourceId.PianoLo).Resonator, Is.Null);
                Assert.That(a.S(SourceId.PianoHi).Resonator, Is.Null);
                Assert.That(a.S(SourceId.PianoMid).Resonator, Is.Not.Null);
            }
        }
    }

    public class GuideVoiceTests
    {
        [Test]
        public void RobotChainIsDryRingCombHighPassAndLift()
        {
            // An impulse: s = 0.92 d[n] (the ring term is x sin(0) = 0 at n = 0); the comb adds 0.12 s[n - 240] (1 + 0.3 z^-240 + ...),
            // so the high-pass + peaking cascade sees 0.92, 0.12*0.92, 0.12*0.3*0.92, ... every 240 samples.
            const int sr = 48000;
            var x = new float[2000];
            x[0] = 1f;
            float[] y = RobotVoice.Process(x, sr);
            var hp = Biquad.Create(BiquadType.Highpass, 120, 0.7, 0, sr);
            var pk = Biquad.Create(BiquadType.Peaking, 2500, 0.8, 2, sr);
            for (int n = 0; n < x.Length; n++)
            {
                double v = 0;
                if (n == 0) v = 0.92;
                else if (n % 240 == 0) v = 0.12 * 0.92 * Math.Pow(0.3, n / 240 - 1);
                float want = pk.Process(hp.Process((float)v));
                Assert.That(y[n], Is.EqualTo(want).Within(1e-6f), "sample " + n);
            }
        }

        [Test]
        public void ResamplerKeepsAToneAndAddsLittleElse()
        {
            const int inRate = 22050, outRate = 48000;
            var x = new float[inRate];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / inRate));
            float[] y = Resampler.Resample(x, inRate, outRate);
            Assert.That(y.Length, Is.EqualTo(outRate));
            double e = 0, s = 0;
            for (int m = 2000; m < outRate - 2000; m++)
            {
                double want = 0.5 * Math.Sin(2 * Math.PI * 1000 * m / (double)outRate);
                e += (y[m] - want) * (y[m] - want);
                s += want * want;
            }
            Assert.That(10 * Math.Log10(e / s), Is.LessThan(-60), "1 kHz at 22.05 -> 48 kHz");
        }

        [Test]
        public void MouthLevelIsTheRmsOfTheLast1024Samples()
        {
            var y = new float[48000];
            for (int i = 0; i < y.Length; i++) y[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 375 * i / 48000.0));   // 128 samples per period
            float[] env = RobotVoice.LevelEnvelope(y, GuideVoice.MouthWindow, GuideVoice.MouthHop);
            Assert.That(env[0], Is.EqualTo(0f), "nothing before the line");
            Assert.That(env[2], Is.EqualTo(0.4 / Math.Sqrt(2) * Math.Sqrt(512.0 / 1024)).Within(1e-3), "half a window of signal");
            for (int k = 4; k < env.Length - 1; k++) Assert.That(env[k], Is.EqualTo(0.4 / Math.Sqrt(2)).Within(1e-3), "window " + k);
        }

        [Test]
        public void ChirpsStartOnTheFirstFrameAtOrAfterTheirTime()
        {
            Assert.That(GuideVoice.QuantumStartFrame(0.01, 48000), Is.EqualTo(480));
            Assert.That(GuideVoice.QuantumStartFrame(0.01 + 0.065, 48000), Is.EqualTo(3600));
            Assert.That(GuideVoice.QuantumStartFrame(0.02, 48000), Is.EqualTo(960));
            Assert.That(GuideVoice.QuantumStartFrame(0.02 + 0.065, 48000), Is.EqualTo(4081), "0.085 in double is a hair past frame 4080");
            using (var a = new OfflineAudio(guide: true))
            {
                var tap = new AudioTestTap();
                a.Kit.Tap = tap;
                a.Guide.Chirp(ChirpKind.Close, 0);
                Assert.That(tap.Count(SourceId.Guide, SynthOp.StartTone), Is.EqualTo(2));
                var c0 = tap.Commands[0].Command;
                var c1 = tap.Commands[1].Command;
                Assert.That(c0.When, Is.EqualTo(-960 / 48000.0).Within(1e-12));
                Assert.That(c0.Flags, Is.EqualTo(SynthFlags.QuantumLead));
                Assert.That(c0.Buffer, Is.EqualTo(960 % 128));
                Assert.That(c1.When, Is.EqualTo(-4080 / 48000.0).Within(1e-12), "the voice counts from the event frame");
                Assert.That(c1.Flags, Is.EqualTo(SynthFlags.QuantumLead | SynthFlags.OscLateStart));
                Assert.That(c1.Buffer, Is.EqualTo(4081 % 128));
                Assert.That(c0.Wave, Is.EqualTo(Wave.Square));
                Assert.That(c0.Lp, Is.EqualTo(5000f));
                Assert.That(c1.F0, Is.EqualTo(700f));
                Assert.That(c1.F1, Is.EqualTo(420f));
            }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator VoiceLinesLoadAndRobotiseAtTheOutputRate()
        {
            using (var a = new OfflineAudio(guide: true))
            {
                // Lines load lazily (platform.md 8.2): every line of the merged table has its WAV (loaded here, one by one).
                Assert.That(a.Guide.VerifyClips(), Is.EqualTo(GuideVoice.LineCount), "every line has its WAV in a Resources/Voice folder");
                // The clips load in the background (Load In Background): the editor finishes them between frames.
                var clip = UnityEngine.Resources.Load<UnityEngine.AudioClip>(GuideVoice.ResourcesFolder + "intro_vr");
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (clip.loadState != UnityEngine.AudioDataLoadState.Loaded && UnityEngine.Time.realtimeSinceStartup - t0 < 10f) yield return null;
                Assert.That(a.Guide.LoadedLines, Is.EqualTo(GuideVoice.LineCount));
                Assert.That(a.Guide.Seconds("intro_vr"), Is.EqualTo(3.102f).Within(0.01f), "manifest.json intro_vr (first five: reworded)");
                Assert.That(a.Guide.Seconds("nope"), Is.EqualTo(-1f));
                Assert.That(a.Guide.Has("praise_3"), Is.True);
                Assert.That(a.Guide.IsRendered(0), Is.False);
                double end = a.Guide.Play("intro_vr", 0);       // not rendered yet: rendered on the spot
                Assert.That(end, Is.GreaterThan(0));
                Assert.That(a.Guide.SyncRenders, Is.EqualTo(1));
                float[] y = a.Bank.Get(AudioEngine.VoiceSlot0 + a.Guide.IndexOf("intro_vr"));
                Assert.That(y, Is.Not.Null);
                int want = (int)Math.Ceiling(3.102 * 48000) + (int)Math.Ceiling(GuideVoice.TailSeconds * 48000);
                Assert.That(y.Length, Is.EqualTo(want).Within(60));
                double peak = 0;
                foreach (var v in y) peak = Math.Max(peak, Math.Abs(v));
                Assert.That(peak, Is.InRange(0.3, 1.2), "the robotised line keeps the recording's level (peak 0.89 dry)");
            }
        }
    }

    public class AmbienceTests
    {
        [Test]
        public void FirePopTimerDrawsInTheReferenceOrder()
        {
            // Ticks every 110 ms from the ambience's start (first at 110 ms); each tick: R() > .65 skips, else time, bp, peak, decay.
            const int sr = 48000;
            NoiseTable.BuildSeeded(sr, 1);
            var pops = new FirePops(sr, 11);
            pops.OnCommand(new SynthCommand { Op = SynthOp.Custom, Kind = SynthCustom.FirePops, Level = 1 }, 0);
            var mix = new float[256];
            int blocks = 2 * sr / 256;   // 2 s: ticks 1..18 (18 x 0.11 = 1.98 s)
            for (int b = 0; b < blocks; b++) { Array.Clear(mix, 0, 256); pops.Process(mix, b * 256L, 256); }
            var r = new Mulberry32(11);
            int want = 0;
            for (int k = 1; k * 0.11 < 2.0; k++)
            {
                if (r.Next() > 0.65) continue;
                want++;
                r.Next(); r.Next(); r.Next(); r.Next();
            }
            Assert.That(pops.PopCount, Is.EqualTo(want));
            // Switched off: no more draws (the timer tests amb.fireOn first).
            pops.OnCommand(new SynthCommand { Op = SynthOp.Custom, Kind = SynthCustom.FirePops, Level = 0 }, 0);
            for (int b = blocks; b < 2 * blocks; b++) { Array.Clear(mix, 0, 256); pops.Process(mix, b * 256L, 256); }
            Assert.That(pops.PopCount, Is.EqualTo(want));
        }

        [Test]
        public void GustShapeIsTheVisualGustsAttackHoldRelease()
        {
            double a = 1.5, h = 1, r = 3;
            Assert.That(SnowGustProcessor.Shape(0, a, h, r, 0.0001, 1), Is.EqualTo(0.0001).Within(1e-12));
            Assert.That(SnowGustProcessor.Shape(0.75, a, h, r, 0, 1), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(SnowGustProcessor.Shape(2.0, a, h, r, 0, 1), Is.EqualTo(1).Within(1e-12));
            Assert.That(SnowGustProcessor.Shape(4.0, a, h, r, 0, 1), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(SnowGustProcessor.Shape(6.0, a, h, r, 0.0001, 1), Is.EqualTo(0.0001).Within(1e-12));
        }

        [Test]
        public void GustsPlayOnlyWhileTheSnowSoundIsOn()
        {
            using (var a = new OfflineAudio(ambience: true))
            {
                var tap = new AudioTestTap();
                a.Kit.Tap = tap;
                a.Ambience.Gust(0.8f, 1.2f, 0.5f, 2.5f);
                Assert.That(tap.Count(SourceId.Snow, SynthOp.Custom), Is.EqualTo(0), "snow off: no gust");
                a.Ambience.SetSnow(true);
                a.Ambience.Gust(0.8f, 1.2f, 0.5f, 2.5f);
                Assert.That(tap.Count(SourceId.Snow, SynthOp.Custom), Is.EqualTo(1));
                var c = tap.Commands[tap.Commands.Count - 1].Command;
                Assert.That(c.Kind, Is.EqualTo(SynthCustom.SnowGust));
                Assert.That(c.F0, Is.InRange(360f, 580f), "f0 = 360 + 220 R()");
                Assert.That(c.Level, Is.EqualTo(0.8f));
                Assert.That(c.Slide, Is.EqualTo(1.2f));
                Assert.That(c.StopAfter, Is.EqualTo(0.5f));
                Assert.That(c.Tau, Is.EqualTo(2.5f));
                Assert.That(c.Peak, Is.EqualTo(0.025f + 0.05f * 0.8f).Within(1e-7f));
                Assert.That(a.Ambience.GustsPlayed, Is.EqualTo(1));
                // The gust swells and fades on the snow source.
                float[] y = a.Render(48000 * 5, SourceId.Snow);
                Assert.That(y[48000 * 1], Is.Not.EqualTo(0f));
            }
        }

        [Test]
        public void SwitchesRampTheBedsWithTheReferenceTimeConstant()
        {
            using (var a = new OfflineAudio(ambience: true))
            {
                a.Ambience.SetFire(true);
                Assert.That(a.Ambience.FireOn, Is.True);
                float[] on = a.Render(48000 * 2, SourceId.Fire);
                Assert.That(Rms(on, 48000, 96000), Is.GreaterThan(0.005), "fire bed audible after 1 s");
                a.Ambience.SetFire(false);
                float[] off = a.Render(48000 * 4, SourceId.Fire);
                // setTarget(0, now, 0.4): after 3.5 s the bed is e^(-3.5/0.4) = -76 dB down.
                Assert.That(Rms(off, 48000 * 7 / 2, 48000 * 4), Is.LessThan(Rms(on, 48000, 96000) * 3e-4));
            }
        }

        /// <summary>Review finding: the beds ran their filters and trig every block with every switch off. Now a source whose
        /// bus has fallen below -120 dB skips its processors; Skip keeps the LFO time, the noise position and the pop timer
        /// exact and a bed re-filters its last 85 ms on the way back, so the render equals the never-skipped one.</summary>
        [Test]
        public void IdleBedsSkipTheirProcessorsAndComeBackAsIfNeverSkipped()
        {
            const int B = OfflineAudio.Block, Sr = OfflineAudio.Sr;
            const int OffBlock = 200, OnBlock = 1700, Blocks = 2300;   // off at 1.07 s, back on at 9.07 s, end at 12.3 s
            var ids = new[] { SourceId.Fire, SourceId.Wind, SourceId.Snow };
            float[] Run(bool skip, out long idle)
            {
                using (var a = new OfflineAudio(ambience: true))
                {
                    foreach (var id in ids) a.S(id).idleSkip = skip;
                    a.Ambience.SetFire(true);
                    a.Ambience.SetWind(true);
                    a.Ambience.SetSnow(true);
                    var y = a.Render(Blocks * B, ids, at =>
                    {
                        if (at == OffBlock * B) { a.Ambience.SetFire(false); a.Ambience.SetWind(false); a.Ambience.SetSnow(false); }
                        if (at == OnBlock * B) { a.Ambience.SetFire(true); a.Ambience.SetWind(true); a.Ambience.SetSnow(true); }
                        if (at == (OnBlock + 40) * B) a.Ambience.Gust(0.7f, 0.5f, 0.3f, 0.6f);
                    });
                    idle = 0;
                    foreach (var id in ids) idle += a.S(id).IdleBlocks;
                    return y;
                }
            }
            float[] skipped = Run(true, out long idleBlocks);
            float[] full = Run(false, out long noIdle);
            Assert.That(noIdle, Is.EqualTo(0));
            // The bus rose from 0 (tau 0.4 s) until the switch-off, then falls; a block is idle once it starts below -120 dB.
            double tau = 0.4 * Sr, atOff = 1 - Math.Exp(-OffBlock * (double)B / tau);
            int firstIdle = (int)Math.Ceiling(tau * Math.Log(atOff / SynthSource.SilentBus) / B);   // blocks after the switch-off
            long expected = ids.Length * (long)(OnBlock - OffBlock - firstIdle);
            Assert.That(idleBlocks, Is.EqualTo(expected).Within(2 * ids.Length), "idle blocks");
            int idleStart = (OffBlock + firstIdle - 1) * B;
            for (int i = 0; i < idleStart; i++)
                if (skipped[i] != full[i]) Assert.Fail($"sample {i} differs before the first idle block: {skipped[i]} vs {full[i]}");
            double peak = 0, after = 0;
            for (int i = 0; i < full.Length; i++) peak = Math.Max(peak, Math.Abs(full[i]));
            for (int i = OnBlock * B; i < full.Length; i++) after = Math.Max(after, Math.Abs(skipped[i] - full[i]));
            double residual = Wav.ResidualDb(skipped, full);
            UnityEngine.Debug.Log($"[AudioTest] idle skip: {idleBlocks} idle blocks (expected {expected}), residual {residual:F1} dB, " +
                      $"max |diff| after the switch-on {after:E2} (peak {peak:F3})");
            Assert.That(peak, Is.GreaterThan(0.01), "the beds sound");
            Assert.That(residual, Is.LessThan(-110), "the skipped render equals the always-processed one");
            Assert.That(after, Is.LessThan(peak * 1e-6), "back in phase: LFO time, noise position, pop timer and its draws");
        }

        static double Rms(float[] x, int from, int to) { double s = 0; for (int i = from; i < to; i++) s += x[i] * x[i]; return Math.Sqrt(s / (to - from)); }
    }

    public class InstrumentRecipeTests
    {
        [Test]
        public void PianoJitterDrawsRateThenCutoffThenLevel()
        {
            using (var a = new OfflineAudio())
            {
                a.Kit.RandOverride = float.NaN;
                a.Kit.Seed(1234);
                var tap = new AudioTestTap();
                a.Kit.Tap = tap;
                a.Piano.Play(64, 0.6f, 0);
                var rng = new XorShift32(1234);
                float r1 = rng.Next01(), r2 = rng.Next01(), r3 = rng.Next01();
                SynthCommand buf = default;
                foreach (var e in tap.Commands) if (e.Command.Op == SynthOp.StartBuffer) buf = e.Command;
                float f = (float)Notes.Mtof(64);
                float cutoff = UnityEngine.Mathf.Clamp(f * 1.5f + 300 + UnityEngine.Mathf.Pow(0.6f, 1.6f) * 11000, 500, 16000);
                Assert.That(buf.Rate, Is.EqualTo(1 + (r1 - 0.5f) * 0.0032f).Within(1e-7f), "rate first (+-2.8 cents)");
                Assert.That(buf.Lp, Is.EqualTo(cutoff * (1 + (r2 - 0.5f) * 0.1f * 0.25f)).Within(1e-2f), "cutoff second (x 0.25)");
                Assert.That(buf.Level, Is.EqualTo((0.08f + 0.8f * 0.36f) * (1 + (r3 - 0.5f) * 0.12f * 0.25f)).Within(1e-6f), "level third");
                Assert.That(buf.Q, Is.EqualTo(0.4f));
                Assert.That(buf.Res, Is.EqualTo(0.6f + 0.4f * 0.6f).Within(1e-6f), "soundboard send 0.6 + 0.4 vel");
                Assert.That(buf.Buffer, Is.EqualTo(AudioEngine.PianoSlot0 + 64 - 21));
            }
        }

        [Test]
        public void VelocitiesAreClampedAndRegistersSplit()
        {
            using (var a = new OfflineAudio())
            {
                var tap = new AudioTestTap();
                a.Kit.Tap = tap;
                var h = a.Piano.Play(51, 0f, 0);
                Assert.That(h.Source.id, Is.EqualTo(SourceId.PianoLo));
                Assert.That(a.Piano.Play(52, 1f, 0).Source.id, Is.EqualTo(SourceId.PianoMid));
                Assert.That(a.Piano.Play(76, 1f, 0).Source.id, Is.EqualTo(SourceId.PianoHi));
                SynthCommand first = default;
                foreach (var e in tap.Commands) if (e.Command.Op == SynthOp.StartBuffer) { first = e.Command; break; }
                Assert.That(first.Level, Is.EqualTo(0.08f + 0.8f * 0.05f * 0.05f).Within(1e-6f), "vel clamps to 0.05");
                Assert.That(PianoSynth.SourceFor(21), Is.EqualTo(SourceId.PianoLo));
                Assert.That(PianoSynth.SourceFor(108), Is.EqualTo(SourceId.PianoHi));
            }
            Assert.That(HarpRenderer.BaseSeconds(48), Is.EqualTo(4.5).Within(1e-12));
            Assert.That(HarpRenderer.BaseSeconds(84), Is.EqualTo(4.5 - 36 * 0.08).Within(1e-12));
            Assert.That(HarpRenderer.BaseSeconds(120), Is.EqualTo(0.7).Within(1e-12));
        }

        [Test]
        public void ReleaseDampAndRepluckFollowTheReferenceVoices()
        {
            using (var a = new OfflineAudio())
            {
                var tap = new AudioTestTap();
                a.Kit.Tap = tap;
                var piano = new VoiceHandle(a.S(SourceId.PianoMid), 77);
                a.Piano.Release(piano, true, 0);
                Assert.That(tap.Commands[0].Command.Op, Is.EqualTo(SynthOp.Release));
                Assert.That(tap.Commands[0].Command.Tau, Is.EqualTo(0.06f), "release: setTarget(0, tt, 0.06)");
                Assert.That(tap.Commands[0].Command.StopAfter, Is.EqualTo(0.5f), "stop tt + 0.5");
                var thud = tap.Commands[1].Command;
                Assert.That(thud.Op, Is.EqualTo(SynthOp.StartNoise));
                Assert.That(thud.Bp, Is.EqualTo(260f)); Assert.That(thud.Q, Is.EqualTo(0.8f));
                Assert.That(thud.Peak, Is.EqualTo(0.028f)); Assert.That(thud.Decay, Is.EqualTo(0.03f));
                tap.Clear();
                a.Piano.Release(piano, false, 0);
                Assert.That(tap.Count(SourceId.PianoMid, SynthOp.StartNoise), Is.EqualTo(0), "re-pluck / later releases: no thud");
                tap.Clear();
                var harp = new VoiceHandle(a.S(SourceId.Harp), 78);
                a.Strings.Damp(harp, 4, 0);
                Assert.That(tap.Commands[0].Command.Tau, Is.EqualTo(0.018f), "StringVoice.damp tau 18 ms");
                Assert.That(tap.Commands[0].Command.StopAfter, Is.EqualTo(0.25f));
                Assert.That(tap.Commands[1].Command.Peak, Is.EqualTo(0.004f * 2f).Within(1e-7f), "shh x sqrt(max(1, n))");
                Assert.That(tap.Commands[1].Command.Bp, Is.EqualTo(2400f));
            }
        }

        [Test]
        public void GlissandoDelaysShiftThePluck()
        {
            using (var a = new OfflineAudio())
            {
                var tap = new AudioTestTap();
                a.Kit.Tap = tap;
                a.Strings.Pluck(5, 0.6f, 0, new PluckOptions { U = 0.35f, Delay = 0.03 });
                foreach (var e in tap.Commands) Assert.That(e.Command.When, Is.EqualTo(-0.03).Within(1e-12), "ASAP + delay is relative");
                tap.Clear();
                a.Strings.Pluck(5, 0.6f, 100.0, new PluckOptions { Delay = 0.03 });
                foreach (var e in tap.Commands) Assert.That(e.Command.When, Is.EqualTo(100.03).Within(1e-9), "absolute + delay");
            }
        }

        [Test]
        public void ChokeSilencesTheCymbalWithinAFewTimeConstants()
        {
            using (var a = new OfflineAudio())
            {
                a.Drums.Hit(DrumKind.Crash, 0.9f, new DrumStrike { Pos = 0.6f, Zone = DrumZone.Bow }, 0);
                float[] before = a.Render(4800, SourceId.Crash);
                a.Drums.Choke(DrumKind.Crash);
                float[] after = a.Render(9600, SourceId.Crash);
                double eBefore = 0, eLate = 0;
                for (int i = 2400; i < 4800; i++) eBefore += before[i] * before[i];
                for (int i = 4800; i < 7200; i++) eLate += after[i] * after[i];   // 100-150 ms after: e^(-8) and the skin is over
                Assert.That(10 * Math.Log10(eLate / eBefore), Is.LessThan(-50), "tau 12 ms to 1e-4");
            }
        }

        [Test]
        public void VoicesRingOutAfterTheirSourceStopsThenFree()
        {
            using (var a = new OfflineAudio())
            {
                var s = a.S(SourceId.PianoMid);
                a.Kit.Noise(s, 0, bp: 260, q: 0.8f, peak: 0.028f, decay: 0.03f);
                a.Kit.Flush();
                float[] y = a.Render(3840 + 512, SourceId.PianoMid);   // the source stops at 3840 (0.08 s)
                Assert.That(y[3840 + 10], Is.Not.EqualTo(0f), "the bandpass rings on past the stop (x 0.0005)");
                Assert.That(Math.Abs(y[3840 + 10]), Is.LessThan(1e-4f));
                Assert.That(s.ActiveVoices, Is.EqualTo(1));
                a.Render(Voice.RingSamples, SourceId.PianoMid);
                Assert.That(s.ActiveVoices, Is.EqualTo(0), "freed RingSamples after the stop");
                Assert.That(s.StolenVoices, Is.EqualTo(0));
            }
        }
    }
}
