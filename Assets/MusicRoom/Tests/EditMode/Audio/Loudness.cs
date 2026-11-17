// BS.1770-4 loudness for the EditMode audio tests (the lab's dsp-util.mjs: 48 kHz K-weighting biquads, 400 ms blocks,
// 75 % overlap, the -70 LKFS and -10 LU gates). Moved here from the modal piano's test kit when the upgraded piano was
// deleted (2026-11-11); the hand drums', bowls' and marimba's tests measure with it.
using System;
using System.Collections.Generic;

namespace MusicRoom.Tests.Audio
{
    public static class Loudness
    {
        /// <summary>The piano's mf reference phrase (Ode to Joy bar 1: E4 E4 F4 G4, quarters at 108 bpm from 0.2 s, vel 0.55,
        /// keys held 90 %, dry) as the lab's piano engine measured it: the level every unit-only instrument was tuned to
        /// (platform.md 10.3).</summary>
        public const double PianoMfLufs = -24.15;

        static double[] Biq(double[] x, double b0, double b1, double b2, double a1, double a2)
        {
            var y = new double[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v;
            }
            return y;
        }

        public static double[] KWeight(float[] x, int len = -1)
        {
            if (len < 0) len = x.Length;
            var d = new double[len];
            for (int i = 0; i < len; i++) d[i] = x[i];
            d = Biq(d, 1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
            return Biq(d, 1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
        }

        /// <summary>Integrated loudness (LUFS) with the -70 and -10 LU gates, channels summed by power.</summary>
        public static double Lufs(params float[][] chans)
        {
            var kw = new double[chans.Length][];
            for (int c = 0; c < chans.Length; c++) kw[c] = KWeight(chans[c]);
            int n = kw[0].Length, blk = 19200, hop = 4800;
            var z = new List<double>();
            for (int s = 0; s + blk <= n; s += hop)
            {
                double e = 0;
                foreach (var c in kw) { double a = 0; for (int i = s; i < s + blk; i++) a += c[i] * c[i]; e += a / blk; }
                z.Add(e);
            }
            double L(double e) => -0.691 + 10 * Math.Log10(e);
            var abs = z.FindAll(e => L(e) > -70);
            if (abs.Count == 0) return double.NegativeInfinity;
            double mean(List<double> a) { double t = 0; foreach (var v in a) t += v; return t / a.Count; }
            double rel = L(mean(abs)) - 10;
            var g = abs.FindAll(e => L(e) > rel);
            return L(mean(g));
        }
    }
}
