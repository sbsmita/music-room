// The "no low-frequency push" conformance rule (platform.md 10.3, drums H1) read the bowls' way (bowls.md 11 R3,
// BUILD-GUIDE 13: the variant is the bowls' own test): energy below 60 Hz <= -20 dB re the hit, and mean / rms <= 0.01
// over 400 ms windows (a short window's edge on the big bowl's 196 Hz tone with its beating partials and its decay flips
// sign with the window's position: not a push; the 100 and 200 ms figures are logged).
using System;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class BowlsTailRuleTests
    {
        const int SR = 48000;

        static float[] StrikeAndRender(int bowl, double seconds)
        {
            using var e = new BowlsRig(1);
            for (int b = 0; b < 3; b++) e.Pickup(b, 0.3, 0, true);
            e.Strike(bowl, BowlStrikeKind.Tap, 1.2, 0.4, 1, 10);
            int n = (int)(seconds * SR) / 256 * 256;
            var y = new float[n];
            for (int k = 0; k < n / 256; k++) { e.RenderBlock(256); Array.Copy(e.Mix, 0, y, k * 256, 256); }
            return y;
        }

        /// <summary>Second-order Butterworth low-pass (RBJ) at fc, run twice (24 dB / octave).</summary>
        static double[] LowPass(float[] x, double fc)
        {
            double w = 2 * Math.PI * fc / SR, q = Math.Sqrt(0.5), al = Math.Sin(w) / (2 * q), c = Math.Cos(w);
            double b0 = (1 - c) / 2, b1 = 1 - c, b2 = b0, a0 = 1 + al, a1 = -2 * c, a2 = 1 - al;
            b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            double[] y = new double[x.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    double xi = pass == 0 ? x[i] : y[i];
                    double yi = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1; x1 = xi; y2 = y1; y1 = yi; y[i] = yi;
                }
            }
            return y;
        }

        /// <summary>The worst |mean| / rms over windows of `seconds`, sliding by 25 ms from 50 ms after the strike.</summary>
        static double Worst(float[] y, double seconds)
        {
            double worst = 0;
            int win = (int)(seconds * SR), hop = (int)(0.025 * SR);
            for (int s = (int)(0.05 * SR); s + win <= y.Length; s += hop)
            {
                double m = 0, r = 0;
                for (int i = s; i < s + win; i++) { m += y[i]; r += (double)y[i] * y[i]; }
                m /= win; r = Math.Sqrt(r / win);
                if (r > 1e-5) worst = Math.Max(worst, Math.Abs(m) / r);
            }
            return worst;
        }

        [Test]
        public void NoBowlPushesTheLowEnd()
        {
            for (int bowl = 0; bowl < 3; bowl++)
            {
                float[] y = StrikeAndRender(bowl, 2.5);
                double e = 0; foreach (float v in y) e += (double)v * v;
                double[] lp = LowPass(y, 60);
                double elf = 0; foreach (double v in lp) elf += v * v;
                double db = 10 * Math.Log10(Math.Max(1e-30, elf) / e);
                double w100 = Worst(y, 0.1), w200 = Worst(y, 0.2), w400 = Worst(y, 0.4);
                Debug.Log($"[BOWLS] tail rule, bowl {bowl}: energy below 60 Hz {db:F1} dB re the hit; worst mean/rms over 100 / 200 / 400 ms windows {w100:F4} / {w200:F4} / {w400:F4}");
                Assert.That(db, Is.LessThanOrEqualTo(-20.0), $"bowl {bowl}: energy below 60 Hz");
                Assert.That(w400, Is.LessThanOrEqualTo(0.01), $"bowl {bowl}: mean / rms over 400 ms windows");
            }
        }
    }
}
