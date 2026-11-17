// Per-partial decay of the harp's strings, for visuals that must die away exactly as the sound does (the strings' blur,
// experience.md section 10 / E8). IAudioEngine.HarpPartialTau is today's Karplus-Strong harp (HarpDecay, OldTau), the
// app's only harp sound since 2026-11-10; V2Tau (the upgraded harp's design decay, deleted 2026-11-11) stays as the
// harp module's test doubles' default. Main thread only, allocation-free, plain math (no Unity API).
using System;

namespace MusicRoom.Audio
{
    public static class HarpPartialDecay
    {
        public const int StringCount = 22;
        /// <summary>ln(1000): amplitude nepers per T60 (the harp engine's 6.908).</summary>
        public const double NepersPerT60 = 6.908;

        /// <summary>harp.md section 4 (designHarp(48000), the lab's test vectors loops[0..21].t60): the
        /// in-plane T60 of each string's fundamental, s. T60_C3 7.0 s halving every 18 semitones, times the 3-tap smoothed
        /// material factor (wound 1.25, gut 1.0, nylon 1.12) and the fixed per-string spread (+-4 %, mulberry32 seed 7).</summary>
        static readonly double[] T60 =
        {
            8.40819333, 7.29633659, 6.7487881, 5.86595988, 5.35519471, 4.91233597, 4.57046596, 4.31797503, 4.10026928,
            3.84971113, 3.56693342, 3.27508812, 3.18402317, 2.89126235, 2.71064479, 2.54486615, 2.34199393, 2.29779124,
            2.17048274, 2.2025685, 1.99580721, 2.01923584,
        };

        static readonly int[] Degrees = { 0, 2, 4, 5, 7, 9, 11 };

        /// <summary>C major from C3 to C6: 48 + 12 (i / 7) + [0 2 4 5 7 9 11][i % 7] (spec 05 section 5.1).</summary>
        public static int MidiOf(int stringIndex) => 48 + 12 * (stringIndex / 7) + Degrees[stringIndex % 7];

        /// <summary>The string's stringing: 0 wound (C3 D3), 1 gut (E3 .. G5), 2 nylon (A5 B5 C6).</summary>
        static int Material(int i) => i <= 1 ? 0 : i <= 18 ? 1 : 2;

        static readonly double[] KhfOfMaterial = { 1.1e-6, 1.6e-6, 0.9e-6 };

        /// <summary>harp.md section 3 KHF, 3-tap smoothed like the engine's matF (s^-1 Hz^-2): highs die fastest.</summary>
        public static double Khf(int i)
        {
            double s = 0;
            int n = 0;
            for (int k = i - 1; k <= i + 1; k++)
                if (k >= 0 && k < StringCount) { s += KhfOfMaterial[Material(k)]; n++; }
            return s / n;
        }

        /// <summary>The v2 harp (harp.md sections 3-4): amplitude 1/e time of partial <paramref name="partial"/> (1 = the
        /// fundamental) of string <paramref name="stringIndex"/>, from the design target the loop filters are fitted to,
        /// sigma(f) = 6.908 / T60 + KHF (f^2 - f0^2) at f = n f0 (in-plane polarisation; the stiffness stretch of the
        /// partials, under 0.5 % at n = 8, is left out). Out-of-range arguments are clamped.</summary>
        public static float V2Tau(int stringIndex, int partial)
        {
            int i = Math.Max(0, Math.Min(StringCount - 1, stringIndex));
            int n = Math.Max(1, partial);
            double f0 = 440.0 * Math.Pow(2.0, (MidiOf(i) - 69) / 12.0);
            double f = n * f0;
            double sigma = NepersPerT60 / T60[i] + Khf(i) * (f * f - f0 * f0);
            return (float)(1.0 / sigma);
        }

        /// <summary>The old harp (06 reference karplusOld, spec 06 section 5.1): per period the loop gain of partial
        /// n is g |cos(w/2)| |(1 - frac) + frac e^-jw| with w = 2 pi n f0 / sr, g = 0.001^(1 / (seconds f0)),
        /// seconds = clamp(4.5 - .08 (midi - 48), .7, 4.5), D = sr / f0 - .5, frac = D - floor(D); tau = -1 / (f0 ln(gain))
        /// (experience.md section 10; matches the renders to under 1 % for partials 1-4).</summary>
        public static float OldTau(int midi, int partial, int sampleRate = 48000)
        {
            int n = Math.Max(1, partial);
            double f0 = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
            double seconds = Math.Min(4.5, Math.Max(0.7, 4.5 - (midi - 48) * 0.08));
            double g = Math.Pow(0.001, 1.0 / (seconds * f0));
            double d = sampleRate / f0 - 0.5, frac = d - Math.Floor(d), w = 2.0 * Math.PI * n * f0 / sampleRate;
            double avg = Math.Abs(Math.Cos(w / 2.0));
            double interp = Math.Sqrt(1.0 - 2.0 * frac * (1.0 - frac) * (1.0 - Math.Cos(w)));
            return (float)(-1.0 / (f0 * Math.Log(g * avg * interp)));
        }
    }

    /// <summary>Per-partial decay times for the string blur (experience.md E8): today's Karplus-Strong recipe (06 reference
    /// karplusOld, exactly the formula in experience.md section 10).</summary>
    public static class HarpDecay
    {
        /// <summary>IAudioEngine.HarpPartialTau's body. stringIndex 0..21 (a harp string's midi number 48..84 is accepted
        /// too); partial 1 = the fundamental. 0 when out of range.</summary>
        public static double PartialTau(int stringIndex, int partial, int sampleRate)
        {
            int i = StringOf(stringIndex);
            if (i < 0 || partial < 1) return 0;
            return ClassicPartialTau(i, partial, sampleRate > 0 ? sampleRate : 48000);
        }

        /// <summary>A string index from 0..21, or from a harp string's midi number; -1 if neither.</summary>
        public static int StringOf(int indexOrMidi)
        {
            if (indexOrMidi >= 0 && indexOrMidi < HarpPartialDecay.StringCount) return indexOrMidi;
            for (int i = 0; i < HarpPartialDecay.StringCount; i++) if (HarpPartialDecay.MidiOf(i) == indexOrMidi) return i;
            return -1;
        }

        /// <summary>Today's harp: tau_n = -1 / (f0 ln(g |cos(w/2)| sqrt(1 - 2 frac (1 - frac)(1 - cos w)))), w = 2 pi n f0 / sr,
        /// g = 0.001^(1 / (seconds f0)), seconds = clamp(4.5 - 0.08 (midi - 48), 0.7, 4.5), frac = frac(sr / f0 - 0.5).</summary>
        public static double ClassicPartialTau(int stringIndex, int partial, int sampleRate)
        {
            int midi = HarpPartialDecay.MidiOf(stringIndex);
            double f0 = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0), sr = sampleRate;
            double seconds = Math.Min(4.5, Math.Max(0.7, 4.5 - (midi - 48) * 0.08));
            double g = Math.Pow(0.001, 1.0 / (seconds * f0));
            double D = sr / f0 - 0.5, frac = D - Math.Floor(D);
            double w = 2 * Math.PI * partial * f0 / sr;
            double mag = g * Math.Abs(Math.Cos(w / 2)) * Math.Sqrt(Math.Max(0, 1 - 2 * frac * (1 - frac) * (1 - Math.Cos(w))));
            return mag > 0 && mag < 1 ? -1.0 / (f0 * Math.Log(mag)) : 0;
        }
    }
}
