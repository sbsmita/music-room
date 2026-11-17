// Shared helpers: pitch, normalize(), the generic envelope closed form, the convolver normalization scale,
// the exact inverse distance law and the IR generators (06 reference) with a seedable PRNG.
using System;
using MusicRoom.Core;

namespace MusicRoom.Audio.Dsp
{
    public static class Notes
    {
        public static double Mtof(double midi) => 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);

        public static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        /// <summary>normalize(d, peak): peak-normalise, then a 50 ms linear fade at the end (06 reference).</summary>
        public static void Normalize(float[] d, float peak, int sampleRate)
        {
            float m = 0;
            for (int i = 0; i < d.Length; i++) { float a = Math.Abs(d[i]); if (a > m) m = a; }
            if (m > 0) { double s = peak / (double)m; for (int i = 0; i < d.Length; i++) d[i] = (float)(d[i] * s); }
            int fade = Math.Min(d.Length, (int)Math.Floor(sampleRate * 0.05));
            for (int i = 0; i < fade; i++) { int j = d.Length - 1 - i; d[j] = (float)(d[j] * ((double)i / fade)); }
        }

        /// <summary>engine env() closed form: P (0.0005 / P)^(t / decay), P = max(peak, 0.0005), 0.0005 after decay (06 envAt).</summary>
        public static double EnvAt(double t, double peak, double decay)
        {
            double P = (float)Math.Max(peak, 0.0005);
            if (t < 0) return 1;
            if (t >= decay) return (float)0.0005;
            return P * Math.Pow((float)0.0005 / P, t / decay);
        }

        /// <summary>Convolver normalisation (normalize = true): scale = 1 / max(rms, 1.25e-4) * 10^(-58/20) * 44100 / sr (06 convolverScale).</summary>
        public static double ConvolverScale(float[][] channels, int sampleRate)
        {
            double p = 0;
            int len = channels[0].Length;
            foreach (var d in channels) for (int i = 0; i < len; i++) p += d[i] * d[i];
            double power = Math.Sqrt(p / (channels.Length * len));
            if (double.IsNaN(power) || double.IsInfinity(power) || power < 0.000125) power = 0.000125;
            double scale = 1 / power * Math.Pow(10, -58 / 20.0) * (44100.0 / sampleRate);
            if (channels.Length == 4) scale *= 0.5;
            return scale;
        }

        /// <summary>Inverse distance law with refDistance 0.6 and rolloffFactor 1.1 (spec 06): 1 m 0.5769, 2 m 0.2804.
        /// Unity reproduces it with Logarithmic rolloff, minDistance 0.6 and AudioManager Rolloff Scale 1.1.</summary>
        public static double PannerGain(double distance) => 0.6 / (0.6 + 1.1 * (Math.Max(distance, 0.6) - 0.6));

        /// <summary>makeBodyImpulse(modes, seconds) (06 reference): phases first (modes.Length draws), then the decaying sines; d /= peak * 6.</summary>
        public static float[] MakeBodyImpulse(int sr, float[] modes, double seconds, ref Mulberry32 rand)
        {
            int len = (int)Math.Floor(sr * seconds);
            var d = new float[len];
            var phases = new double[modes.Length];
            for (int k = 0; k < modes.Length; k++) phases[k] = rand.Next() * Math.PI * 2;
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / sr, v = 0;
                for (int k = 0; k < modes.Length; k++)
                    v += Math.Sin(2 * Math.PI * modes[k] * t + phases[k]) * Math.Exp(-t / (seconds * (0.35 - k * 0.03))) / (1 + k * 0.5);
                d[i] = (float)v;
            }
            double peak = 0;
            for (int i = 0; i < len; i++) peak = Math.Max(peak, Math.Abs(d[i]));
            for (int i = 0; i < len; i++) d[i] = (float)(d[i] / (peak * 6));
            return d;
        }

        /// <summary>makeRoomImpulse (06 reference): stereo; per channel the noise tail (draw order: tail then taps), then 7 early taps.</summary>
        public static float[][] MakeRoomImpulse(int sr, double seconds, double preDelay, double fast, double slow, double lp, double early, ref Mulberry32 rand)
        {
            int len = (int)Math.Floor(sr * seconds);
            var chs = new[] { new float[len], new float[len] };
            double[] taps = { 0.009, 0.014, 0.019, 0.027, 0.034, 0.045, 0.058 };
            double[] gains = { 0.7, 0.55, 0.45, 0.38, 0.3, 0.22, 0.15 };
            for (int ch = 0; ch < 2; ch++)
            {
                var d = chs[ch];
                int pd = (int)Math.Floor(sr * preDelay);
                double low = 0;
                for (int i = pd; i < len; i++)
                {
                    double t = (double)(i - pd) / sr;
                    double n = rand.Next() * 2 - 1;
                    low += lp * (n - low);
                    double fadeIn = Math.Min(1, t / 0.006);
                    d[i] = (float)(fadeIn * (0.55 * n * Math.Exp(-t / (seconds * fast)) + 1.5 * low * Math.Exp(-t / (seconds * slow))));
                }
                for (int k = 0; k < taps.Length; k++)
                {
                    int at = pd + (int)Math.Floor(sr * (taps[k] + (ch == 1 ? 0.0011 * (k % 2 == 1 ? 1 : -1) : 0)));
                    for (int j = 0; j < 3 && at + j < len; j++) d[at + j] += (float)(early * gains[k] * (1 - j * 0.3) * (rand.Next() < 0.5 ? -1 : 1));
                }
            }
            return chs;
        }
    }
}
