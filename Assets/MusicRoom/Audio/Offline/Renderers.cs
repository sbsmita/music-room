// Offline note renders (spec 06 sections 3.1 and 5.1), seedable: the piano render (06 renderPianoKey) and the old harp's
// Karplus-Strong (06 karplusOld, bit-exact with the fingerprints when fed mulberry32(2000 + midi)). Thread-safe.
using System;
using MusicRoom.Audio.Dsp;
using MusicRoom.Core;

namespace MusicRoom.Audio.Offline
{
    /// <summary>The piano key render (spec 06 section 3.1): additive piano, up to 12 inharmonic partials, two strings
    /// detuned per note, three exponential envelope segments per partial; a band-passed 30 ms hammer noise; output 0.5,
    /// then normalised to 0.9. Per-sample Pow/Sin replaced by segment multipliers and a rotating phasor (exact to ~1e-9).</summary>
    public static class PianoRenderer
    {
        public static double Seconds(int midi) => Notes.Clamp(7 - (midi - 21) / 87.0 * 5.6, 1.1, 7);

        /// <summary>Samples in the render of this key: floor(sr * Seconds(midi)).</summary>
        public static int Length(int midi, int sampleRate) => (int)Math.Floor((double)sampleRate * Seconds(midi));

        // Per worker thread: the double accumulator (up to 7 s) and the 30 ms hammer noise, sized once and reused for every
        // key, so a render allocates nothing but its published float[] (spec review: 142 MB of start-up garbage otherwise).
        [ThreadStatic] private static double[] s_acc;
        [ThreadStatic] private static float[] s_noise;

        /// <summary>Session render: the hammer noise uses a seed derived from the midi (randomness is per session in the app).</summary>
        public static float[] Render(int midi, int sampleRate) => Render(midi, sampleRate, (uint)(1000 + midi) ^ Mulberry32.SessionSeed());

        /// <summary>Deterministic render: seed 1000 + midi reproduces 06 fingerprints pianoBuffer_seed1000plusMidi (to ~1e-4).</summary>
        public static float[] Render(int midi, int sampleRate, uint noiseSeed) => RenderInto(midi, sampleRate, noiseSeed, new float[Length(midi, sampleRate)]);

        /// <summary>Renders into <paramref name="d"/> (length Length(midi, sampleRate), e.g. allocated up front at start-up)
        /// and returns it. Thread-safe; reuses this thread's scratch buffers.</summary>
        public static float[] RenderInto(int midi, int sampleRate, uint noiseSeed, float[] d)
        {
            double sr = sampleRate;
            double f = Notes.Mtof(midi);
            double u = (midi - 21) / 87.0;
            double seconds = Seconds(midi);
            int n = Length(midi, sampleRate);
            if (d == null || d.Length != n) throw new ArgumentException($"destination must hold {n} samples", nameof(d));
            int cap = (int)Math.Ceiling(7.0 * sr);
            if (s_acc == null || s_acc.Length < Math.Max(cap, n)) s_acc = new double[Math.Max(cap, n)];
            var acc = s_acc;
            Array.Clear(acc, 0, n);
            double B = 0.00008 + 0.0007 * u * u;
            double nyq = Math.Min(sr * 0.45, 15000);
            double detune = 1.2 + 2.5 * u;
            for (int k = 1; k <= 12; k++)
            {
                double fk = f * k * Math.Sqrt(1 + B * k * k);
                if (fk > nyq) break;
                double amp = (float)(Math.Pow(k, -(0.9 + 0.75 * u)) * (k == 1 ? 1 : 0.9));
                double tau = seconds / 2.9 / (1 + 0.5 * (k - 1));
                double tEnd = Math.Min(seconds, 0.12 + tau * 5.5);
                double a2 = (float)(amp * 0.45), a3 = (float)Math.Max(amp * 0.001, 1e-5), a0 = (float)0.0001;
                for (int s = -1; s <= 1; s += 2)
                {
                    double hz = (float)fk * Math.Pow(2, (float)(s * detune / 2) / 1200.0);
                    double w = 2 * Math.PI * hz / sr;
                    double cw = Math.Cos(w), sw = Math.Sin(w);
                    double c = 1, si = 0; // phasor at phase 0 (an oscillator sine starts at phase 0)
                    int n1 = (int)Math.Round(0.004 * sr), n2 = (int)Math.Round(0.12 * sr), n3 = (int)Math.Round(tEnd * sr);
                    double g = a0;
                    double m1 = Math.Pow(amp / a0, 1.0 / n1), m2 = Math.Pow(a2 / amp, 1.0 / (n2 - n1)), m3 = n3 > n2 ? Math.Pow(a3 / a2, 1.0 / (n3 - n2)) : 1;
                    for (int i = 0; i < n; i++)
                    {
                        acc[i] += si * g;
                        if (i < n1) g *= m1; else if (i == n1) g = amp; else if (i < n2) g *= m2; else if (i == n2) g = a2; else if (i < n3) g *= m3; else g = a3;
                        double c2 = c * cw - si * sw;
                        si = si * cw + c * sw;
                        c = c2;
                    }
                }
            }
            // Hammer thump: 30 ms of fresh noise (float)(R()*2-1) -> bandpass(clamp(2.5 f, 300, 4000), Q 0.8) * (g0 -> 0.001 @25 ms, hold).
            var rng = new Mulberry32(noiseSeed);
            int nn = (int)Math.Floor(sr * 0.03);
            if (s_noise == null || s_noise.Length < nn) s_noise = new float[nn];
            var nz = s_noise;
            for (int i = 0; i < nn; i++) nz[i] = (float)(rng.Next() * 2 - 1);
            var bp = Biquad.Create(BiquadType.Bandpass, Notes.Clamp(f * 2.5, 300, 4000), 0.8, 0, sr);
            double g0 = (float)(0.5 - 0.3 * u), gEnd = (float)0.001;
            int n25 = (int)Math.Round(0.025 * sr);
            double gh = g0, mh = Math.Pow(gEnd / g0, 1.0 / n25);
            for (int i = 0; i < n; i++)
            {
                float x = bp.Process(i < nn ? nz[i] : 0f);
                acc[i] += x * gh;
                if (i < n25 - 1) gh *= mh; else gh = gEnd;
            }
            for (int i = 0; i < n; i++) d[i] = (float)(acc[i] * 0.5);
            Notes.Normalize(d, 0.9f, sampleRate);
            return d;
        }
    }

    /// <summary>The harp's "old" sound (spec 06 section 5.1): Karplus-Strong with a fractional
    /// delay, pick-position comb on the excitation and a one-pole brightness filter; normalised to 0.8. Bit-exact with
    /// float[] storage and double arithmetic (06 harpOldBuffer fingerprints with seed 2000 + midi).</summary>
    public static class HarpRenderer
    {
        /// <summary>The C-major scale degrees of the harp strings.</summary>
        public static readonly int[] Degrees = { 0, 2, 4, 5, 7, 9, 11 };
        public static readonly int[] Strings = BuildStrings(); // C major, C3 (48) .. C6 (84): 22 strings

        private static int[] BuildStrings()
        {
            var list = new System.Collections.Generic.List<int>();
            for (int m = 48; m <= 84; m++) if (Array.IndexOf(Degrees, m % 12) >= 0) list.Add(m);
            return list.ToArray();
        }

        /// <summary>String i: midi = 48 + 12 floor(i/7) + [0,2,4,5,7,9,11][i % 7]. Allocation-free.</summary>
        public static int MidiOf(int i) => 48 + 12 * (i / 7) + Degrees[i % 7];

        /// <summary>clamp(4.5 - 0.08 (midi - 48), 0.7, 4.5) (the current synth's ring time, HarpRingSeconds).</summary>
        public static double BaseSeconds(int midi) => Notes.Clamp(4.5 - (midi - 48) * 0.08, 0.7, 4.5);

        /// <summary>Samples in the old harp render of this string: floor(sr * BaseSeconds(midi)).</summary>
        public static int Length(int midi, int sampleRate) => (int)Math.Floor((double)sampleRate * BaseSeconds(midi));

        public static float[] RenderOld(int midi, int sampleRate) => RenderOld(midi, sampleRate, (uint)(2000 + midi) ^ Mulberry32.SessionSeed());

        public static float[] RenderOld(int midi, int sampleRate, uint seed) => RenderOldInto(midi, sampleRate, seed, new float[Length(midi, sampleRate)]);

        /// <summary>Renders into <paramref name="o"/> (length Length(midi, sampleRate)) and returns it. Allocation-free.</summary>
        public static float[] RenderOldInto(int midi, int sampleRate, uint seed, float[] o)
        {
            double sr = sampleRate;
            double f = Notes.Mtof(midi);
            double seconds = BaseSeconds(midi);
            double bright = Notes.Clamp(0.3 + (midi - 48) * 0.009, 0.3, 0.62);
            const double pick = 0.32;
            int n = Length(midi, sampleRate);
            if (o == null || o.Length != n) throw new ArgumentException($"destination must hold {n} samples", nameof(o));
            Array.Clear(o, 0, n);
            double D = sr / f - 0.5;
            int Ni = (int)Math.Floor(D);
            double frac = D - Ni;
            int L = Ni + 2;
            var rng = new Mulberry32(seed);
            for (int i = 0; i < L; i++) o[i] = (float)(rng.Next() * 2 - 1);
            int pp = Math.Max(1, Mulberry32.JsRound(L * pick));
            for (int i = L - 1; i >= pp; i--) o[i] = (float)(o[i] - o[i - pp] * 0.9);
            double lp = 0;
            for (int i = 0; i < L; i++) { lp += bright * (o[i] - lp); o[i] = (float)lp; }
            double g = Math.Pow(0.001, 1 / (seconds * f));
            for (int i = L; i < n; i++)
            {
                double a0 = (double)o[i - Ni] + o[i - Ni - 1];
                double a1 = (double)o[i - Ni - 1] + o[i - Ni - 2];
                o[i] = (float)(g * 0.5 * ((1 - frac) * a0 + frac * a1));
            }
            Notes.Normalize(o, 0.8f, sampleRate);
            return o;
        }
    }
}
