// The headset report after the graze fix: "still broken cassette" whenever a bowl is rubbed. The app's own output, recorded
// on the Quest while the big bowl was rubbed (listen/nov9/audiotap-153102.wav), was dominated by a smooth broadband noise:
// flat from 0.9 to 3.5 kHz, -6 dB near 750 Hz and 4.3 kHz (exactly the unit's rub-noise filter, a band-pass at 1.8 kHz,
// Q 0.5, then a 5 kHz low-pass), no peaks at the bowl's partials, and the 196 Hz fundamental only 3 to 18 dB above it per
// 10 Hz bin. The reference's rub noise follows the friction power alone (NOISE_K x F_c x slip speed). A rub that has not
// yet made the bowl sing (a rub takes seconds to build), a fast or deep one, slips at full friction power from the first
// moment, so it was all hiss. These tests reproduce it with scripted, jittery hands (a 60 Hz tracker with 3 to 4 mm of
// jitter, 72 and 90 Hz display frames, rim speeds 0.1 to 0.9 m/s, the pressure breathing) on a finger and on the puja held
// in a jittery hand, and measure what the recording showed: per 0.1 s Hann window, the mean level of six bins between the
// partials (0.7, 0.9, 1.3, 1.8, 2.7, 3.3 kHz) against the fundamental's bin, the fundamental floored at -50 dBFS (an almost
// silent onset is not grit), and how steady the 5 ms RMS envelope is. "Old" is the unit without the tonal rub noise.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Bowls;
using MusicRoom.Tests.Audio;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsRubNoiseTests
    {
        const int Bowl = 0;
        const double F0 = 195.998, From = 1.0, To = 6.0, Seconds = 6.6;
        /// <summary>The bins between the big bowl's partials (196, 543, 1013, 1592, 2279, 3052 Hz).</summary>
        static readonly double[] NoiseHz = { 700, 900, 1300, 1800, 2700, 3300 };
        /// <summary>The fundamental's floor (dBFS) in the grit ratio: under it, a window is judged against the floor.</summary>
        const double FundFloorDb = -50;

        public struct Grit
        {
            /// <summary>Noise bins over the (floored) fundamental, dB: median, 90th percentile and worst window.</summary>
            public double Median, P90, Worst;
            /// <summary>The fundamental and the noise bins, dBFS (sine amplitude), over all windows.</summary>
            public double FundDb, NoiseDb;
            /// <summary>Envelope roughness: per window, the RMS of the second difference of the twenty 5 ms RMS values over their
            /// mean; median and 90th percentile. A swell or the slow warble of the circling pattern scores near 0, broadband
            /// noise 0.2 to 0.4 (the recording: 0.23 to 0.27 while rubbed, 0.003 while ringing).</summary>
            public double Rough, RoughP90;
            public override string ToString() => $"grit median {Median:F1} p90 {P90:F1} worst {Worst:F1} dB, fund {FundDb:F1} dBFS, noise {NoiseDb:F1} dBFS, roughness {Rough:F3} p90 {RoughP90:F3}";
        }

        static double Goertzel(float[] y, int i0, int n, double f, double[] win)
        {
            double k = 2 * Math.Cos(2 * Math.PI * f / SR), s1 = 0, s2 = 0;
            for (int i = 0; i < n; i++) { double s0 = y[i0 + i] * win[i] + k * s1 - s2; s2 = s1; s1 = s0; }
            return Math.Max(1e-30, s1 * s1 + s2 * s2 - k * s1 * s2);
        }

        /// <summary>The recording's metric on y (48 kHz) in [from, to): 0.1 s Hann windows every 50 ms.</summary>
        public static Grit Measure(float[] y, double from = From, double to = To, double f0 = F0)
        {
            int n = SR / 10, w5 = SR / 200; var win = new double[n];
            for (int i = 0; i < n; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            double Db(double p) => 20 * Math.Log10(2 * Math.Sqrt(p) / (n * 0.5) + 1e-12);
            var rel = new List<double>(); var env = new List<double>(); var e5 = new double[n / w5]; double fSum = 0, nSum = 0;
            for (double t = from; t + 0.1 <= to && (int)(t * SR) + n <= y.Length; t += 0.05)
            {
                int i0 = (int)(t * SR);
                double pf = Goertzel(y, i0, n, f0, win), pn = 0;
                foreach (double f in NoiseHz) pn += Goertzel(y, i0, n, f, win);
                pn /= NoiseHz.Length;
                rel.Add(Db(pn) - Math.Max(Db(pf), FundFloorDb));
                fSum += pf; nSum += pn;
                int m = n / w5; double mean = 0;
                for (int j = 0; j < m; j++)
                {
                    double e = 0; for (int i = 0; i < w5; i++) e += (double)y[i0 + j * w5 + i] * y[i0 + j * w5 + i];
                    e5[j] = Math.Sqrt(e / w5); mean += e5[j] / m;
                }
                double d2 = 0; for (int j = 1; j < m - 1; j++) { double d = e5[j + 1] - 2 * e5[j] + e5[j - 1]; d2 += d * d; }
                env.Add(mean > 0 ? Math.Sqrt(d2 / (m - 2)) / mean : 0);
            }
            var r = new List<double>(rel); r.Sort(); env.Sort();
            return new Grit
            {
                Median = r[r.Count / 2], P90 = r[(int)(r.Count * 0.9)], Worst = r[r.Count - 1], Rough = env[env.Count / 2], RoughP90 = env[(int)(env.Count * 0.9)],
                FundDb = Db(fSum / rel.Count), NoiseDb = Db(nSum / rel.Count),
            };
        }

        public struct RubCase
        {
            public string Name; public double V, DepthMm, PressMm, YOff, Hz, Jitter; public bool Stick;
            public override string ToString() => Name;
        }

        public static RubCase Finger(string name, double v, double depth = 6, double press = 3, double hz = 72, double jitter = 0.003, double yOff = -0.010)
            => new RubCase { Name = name, V = v, DepthMm = depth, PressMm = press, Hz = hz, Jitter = jitter, YOff = yOff };
        public static RubCase Puja(string name, double v, double depth = 5, double press = 2, double hz = 72, double jitter = 0.003)
            => new RubCase { Name = name, V = v, DepthMm = depth, PressMm = press, Hz = hz, Jitter = jitter, YOff = -0.015, Stick = true };

        /// <summary>The rim speed v (m/s), the depth into the wall breathing by press mm at 0.7 Hz.</summary>
        static Func<double, double3> Path(RubCase c, double reach)
        {
            var g = Placed(Placement.App)[Bowl]; double A0 = Approach(Placement.App, Bowl), w = c.V / (g.R + reach);
            return t =>
            {
                double tt = Math.Max(0, t - 0.4), a = A0 + w * tt;
                double rr = g.R + reach - (c.DepthMm + c.PressMm * Math.Sin(2 * Math.PI * 0.7 * tt)) * 1e-3;
                if (t < 0.4) rr += Math.Min(0.03, (0.4 - t) * 0.1);
                return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H + c.YOff, g.C.z + rr * Math.Sin(a));
            };
        }

        /// <summary>The unit's output for a rub case (the app's pad and contact smoothing; tonal: the tonal rub noise).</summary>
        public static float[] Render(RubCase c, bool tonal, uint seed = 7, double seconds = Seconds)
        {
            Func<double, SimHand[]> hands;
            BowlsGrazeTests.PujaHand ph = null;
            if (c.Stick) { ph = new BowlsGrazeTests.PujaHand(Path(c, BowlsHands.IX.PUJA_R), c.Jitter, 1.5, seed, true); hands = ph.At; }
            else { var p = Path(c, TIP_R); hands = Hand(t => t > seconds - 0.2 ? (double3?)null : p(t)); }
            var r = Simulate(new Opts
            {
                Layout = Placement.App, Seconds = seconds, HandsAt = hands, Hz = c.Hz, TrackerHz = 60, Jitter = c.Stick ? 0 : c.Jitter, Seed = seed, KeepAudio = true,
                TonalNoise = tonal, BeforeFrame = ph != null ? ph.Before : (Action<double, BowlsHandInput[]>)null,
                OnFrame = ph != null ? ph.After : (Action<double, BowlsHands, SimResult>)null,
            });
            var a = r.Audio.ToArray(); r.Rig.Dispose();
            return a;
        }

        static readonly RubCase[] Cases =
        {
            Finger("finger 0.1 m/s", 0.1), Finger("finger 0.35 m/s", 0.35), Finger("finger 0.6 m/s", 0.6), Finger("finger 0.9 m/s", 0.9),
            Finger("finger 0.35 m/s 90 Hz 4 mm", 0.35, hz: 90, jitter: 0.004), Finger("finger 0.6 m/s 90 Hz 4 mm", 0.6, hz: 90, jitter: 0.004),
            Finger("finger 35 mm deep", 0.35, depth: 35, press: 8), Finger("finger at the lip", 0.35, yOff: 0),
            Puja("stick 0.2 m/s", 0.2), Puja("stick 0.35 m/s", 0.35), Puja("stick 0.6 m/s", 0.6),
            Puja("stick 0.6 m/s 90 Hz 4 mm", 0.6, hz: 90, jitter: 0.004), Puja("stick 20 mm deep", 0.35, depth: 20, press: 5),
        };

        /// <summary>The device's signature: 3 to 18 dB per bin under the fundamental. Old renders that come within this of it.</summary>
        const double GrittyDb = -20;
        /// <summary>A real rubbed bowl's friction noise sits at least 30 dB under the fundamental.</summary>
        const double CleanDb = -30;

        [Test]
        public void ARealisticRubSingsWithoutGrit()
        {
            string dir = Environment.GetEnvironmentVariable("BOWLS_WAV_DIR");
            int gritty = 0;
            var fails = new List<string>();
            foreach (RubCase c in Cases)
            {
                float[] oldY = Render(c, false), newY = Render(c, true);
                Grit o = Measure(oldY), n = Measure(newY);
                Debug.Log($"[BowlsRubNoise] {c}: old {o} | new {n}");
                if (!string.IsNullOrEmpty(dir))
                {
                    string k = c.Name.Replace(' ', '-').Replace('/', '-');
                    WriteWav(System.IO.Path.Combine(dir, $"rub-old-{k}.wav"), oldY); WriteWav(System.IO.Path.Combine(dir, $"rub-new-{k}.wav"), newY);
                }
                if (o.Worst > GrittyDb) gritty++;
                if (n.Worst > CleanDb) fails.Add($"{c}: worst window {n.Worst:F1} dB");
                if (n.Median > -45) fails.Add($"{c}: median {n.Median:F1} dB");
                if (n.RoughP90 > 0.1) fails.Add($"{c}: envelope roughness p90 {n.RoughP90:F3}");
                if (Math.Abs(n.FundDb - o.FundDb) > 0.5) fails.Add($"{c}: the sing changed ({o.FundDb:F1} -> {n.FundDb:F1} dBFS)");
            }
            Assert.That(gritty, Is.GreaterThanOrEqualTo(6), "the old unit's rub comes within 20 dB of the fundamental (the headset's broken cassette, reproduced)");
            Assert.That(fails, Is.Empty, "every rub is a tonal sing: the friction noise at least 30 dB under the fundamental in every window");
        }

        [Test]
        public void TheDeviceSignatureIsReproducedAndGone()
        {
            // the stick at 0.6 m/s: a rub that is still building its sing, as on the headset (the noise bins 3 to 18 dB under it)
            RubCase c = Puja("stick 0.6 m/s", 0.6);
            Grit o = Measure(Render(c, false)), n = Measure(Render(c, true));
            Debug.Log($"[BowlsRubNoise] signature {c}: old {o} | new {n}");
            Assert.That(o.Median, Is.GreaterThan(-35), "old: broadband noise close under the fundamental for most of the rub");
            Assert.That(o.P90, Is.GreaterThan(-20), "old: within 20 dB in many windows (the recording: -3 to -18 dB)");
            Assert.That(o.RoughP90, Is.GreaterThan(0.15), "old: a rough, rapidly fluctuating envelope (the recording: 0.33)");
            Assert.That(n.Worst, Is.LessThan(CleanDb), "new: never within 30 dB");
            Assert.That(n.Median, Is.LessThan(-50), "new: typically 50 dB under");
            Assert.That(n.Rough, Is.LessThan(0.02), "new: a smooth envelope");
            Assert.That(n.RoughP90, Is.LessThan(0.05), "new: a smooth envelope");
        }

        [Test]
        public void StrikesAndTheReferenceAreUntouched()
        {
            // a tap and a wood strike: they render bit-identical with the tonal rub noise on
            float[] Strikes(bool tonal)
            {
                var rig = new BowlsRig(3);
                unsafe { BowlsDesign.SetPad(rig.S, SR, true); BowlsDesign.SetRubNoise(rig.S, SR, tonal); }
                rig.Strike(0, BowlStrikeKind.Tap, 0.8, 0.3, 1, 2400);
                rig.Strike(1, BowlStrikeKind.Wood, 1.2, 1.1, 0.9, 24000);
                var y = new List<float>();
                for (int k = 0; k < 400; k++) { rig.RenderBlock(256); for (int i = 0; i < 256; i++) y.Add(rig.Mix[i]); }
                rig.Dispose();
                return y.ToArray();
            }
            float[] a = Strikes(false), b = Strikes(true);
            Assert.That(b.Length, Is.EqualTo(a.Length));
            double peak = 0; int diff = 0;
            for (int i = 0; i < a.Length; i++) { if (b[i] != a[i]) diff++; peak = Math.Max(peak, Math.Abs(a[i])); }
            Assert.That(diff, Is.EqualTo(0), "strikes are bit-identical");
            Assert.That(peak, Is.GreaterThan(0.01), "the strikes sound");
            unsafe
            {
                var rig = new BowlsRig(1);
                for (int i = 0; i < 3; i++) Assert.That(rig.B(i)->NoiseRel, Is.EqualTo(0f), "the reference rig (and its vectors) keeps the reference's rub noise");
                rig.Dispose();
            }
        }

        [Test]
        public void TheDevelopmentRubLogSaysWhatTheRubDoesOnceASecond()
        {
            Assert.That(BowlsRubLog.Enabled, Is.False, "never in the editor (and never in a release player: Debug.isDebugBuild)");
            var log = new BowlsRubLog();
            var lines = new List<string>();
            var st = new BowlsRubStats();
            for (int k = 0; k < 72 * 3; k++)
            {
                bool rub = k >= 20 && k < 20 + 72 * 2 + 10;
                st.Contact += rub ? 667 : 0; st.Stick += rub ? 100 : 0; st.Lost += rub ? 67 : 0;
                st.Force = 3.25f; st.Slip = 0.31f; st.Rms = 0.1f; st.Sing = 0.42f;
                string line = log.Frame(1 / 72.0, rub ? 0 : -1, 0.35, 3.5, st, 0);
                if (line != null) lines.Add(line);
            }
            Debug.Log("[BowlsRubNoise] " + string.Join(" / ", lines));
            Assert.That(lines.Count, Is.EqualTo(2), "one line per second of rubbing, none while not rubbing");
            StringAssert.Contains("[Bowls] rub bowl 0 speed 0.35 m/s N 3.50 N Fc 3.25 N slip 0.310 m/s stick 0.15 slip-frac 0.85 lost 0.10 level -20.0 dBFS", lines[0]);
        }

        public static void WriteWav(string path, float[] y)
        {
            using (var f = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int n = y.Length;
                f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + n * 2); f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(SR); f.Write(SR * 2); f.Write((short)2); f.Write((short)16);
                f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(n * 2);
                for (int i = 0; i < n; i++) f.Write((short)Math.Max(-32767, Math.Min(32767, Math.Round(y[i] * 32767))));
            }
        }
    }
}
