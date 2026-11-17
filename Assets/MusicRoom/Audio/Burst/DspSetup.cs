// Main-thread setup for the Burst kernels (spec 10-upgrade/burst.md section 4): coefficients in double, rounded once
// to float32, exactly as lab/kernels.mjs (mbSetMode, mbSetPole, mbStrike, wgSetString, bqSet). Never called per
// sample; may allocate nothing. Target: Assets/MusicRoom/Audio/Burst/DspSetup.cs.
using System;

namespace MusicRoom.Audio.Burst
{
    public static unsafe class DspSetup
    {
        /// <summary>Mode m of a bank (group m >> 2, lane m &amp; 3): f in Hz, tau = amplitude 1/e time in s (0 = silent).</summary>
        public static void SetMode(ModalGroup* bank, int m, double hz, double tau, float go, float gi, double sr)
        {
            ref ModalGroup g = ref bank[m >> 2];
            int l = m & 3;
            double r = tau > 0 ? Math.Exp(-1.0 / (tau * sr)) : 0.0;
            double w = 2.0 * Math.PI * hz / sr;
            g.Pr[l] = (float)(r * Math.Cos(w));
            g.Pi[l] = (float)(r * Math.Sin(w));
            g.Go[l] = go;
            g.Gi[l] = gi;
        }

        /// <summary>Pole with an explicit per-sample amplitude factor (piecewise envelopes: mult = (end/start)^(1/samples)).</summary>
        public static void SetPole(ModalGroup* bank, int m, double hz, double mult, double sr)
        {
            ref ModalGroup g = ref bank[m >> 2];
            int l = m & 3;
            double w = 2.0 * Math.PI * hz / sr;
            g.Pr[l] = (float)(mult * Math.Cos(w));
            g.Pi[l] = (float)(mult * Math.Sin(w));
        }

        /// <summary>z += amp e^{j phase}.</summary>
        public static void Strike(ModalGroup* bank, int m, double amp, double phase)
        {
            ref ModalGroup g = ref bank[m >> 2];
            int l = m & 3;
            g.Zr[l] = g.Zr[l] + (float)(amp * Math.Cos(phase));
            g.Zi[l] = g.Zi[l] + (float)(amp * Math.Sin(phase));
        }

        /// <summary>Lane l of a WG4 group: f0 (Hz), t60 at f0 (s), brightness in (0, 1] (loss pole a1 = 1 - bright), pickup go.
        /// Returns false when the line is too short (Len must be &gt;= L + 2).</summary>
        public static bool SetString(WaveguideGroup* s, int l, double f0, double t60, double bright, float go, double sr)
        {
            double a1 = 1 - bright;
            double P = sr / f0;
            double g = Math.Pow(10, -3 / (t60 * f0));
            double w = 2 * Math.PI * f0 / sr;
            double re = 1 - a1 * Math.Cos(w), im = a1 * Math.Sin(w);
            double H = (1 - a1) / Math.Sqrt(re * re + im * im);
            double b0 = (1 - a1) * (g / H);
            double lossDelay = Math.Atan2(im, re) / w;
            double rest = P - lossDelay;
            int L = (int)Math.Floor(rest - 0.5);
            double d = rest - L;
            double c = (1 - d) / (1 + d);
            if (L + 2 > s->Len) return false;
            s->L[l] = L; s->C[l] = (float)c; s->B0[l] = (float)b0; s->A1[l] = (float)a1; s->Go[l] = go;
            return true;
        }

        /// <summary>Lane l of a BQ4 from normalized coefficients (b0, b1, b2, a1, a2), e.g. the RBJ formulas of Dsp/Biquad.cs.</summary>
        public static void SetBiquad(Biquad4* q, int l, double b0, double b1, double b2, double a1, double a2)
        {
            q->B0[l] = (float)b0; q->B1[l] = (float)b1; q->B2[l] = (float)b2; q->NA1[l] = -(float)a1; q->NA2[l] = -(float)a2;
        }

        /// <summary>Engine addition: lane l of a BQ4 from a BiquadType and its parameters (the formulas of Dsp/Biquad.cs,
        /// in double, rounded once).</summary>
        public static void SetBiquad(Biquad4* q, int l, MusicRoom.Audio.Dsp.BiquadType type, double hz, double q0, double gainDb, double sr)
        {
            var b = MusicRoom.Audio.Dsp.Biquad.Create(type, hz, q0, gainDb, sr);
            b.GetCoefficients(out double b0, out double b1, out double b2, out double a1, out double a2);
            SetBiquad(q, l, b0, b1, b2, a1, a2);
        }

        /// <summary>Denormal policy (spec section 6, item 3): zero a waveguide lane's line and states when its string retires.</summary>
        public static void ZeroLine(WaveguideGroup* s, float* lines, int l)
        {
            float* p = lines + l * s->Len;
            for (int i = 0; i < s->Len; i++) p[i] = 0f;
            s->X1[l] = 0f; s->Y1[l] = 0f; s->S1[l] = 0f;
        }

        /// <summary>Per-sample multiplier of an exponential decay with time constant tau seconds: exp(-1 / (tau sr)) (0 = silent).</summary>
        public static float DecayMultiplier(double tau, double sr) => tau > 0 ? (float)Math.Exp(-1.0 / (tau * sr)) : 0f;
    }
}
