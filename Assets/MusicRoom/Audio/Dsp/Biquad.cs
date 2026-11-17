// RBJ biquad with float32 parameters (spec 06 section 1.2, verified against the reference renders to
// < 3e-6 dB): lowpass/highpass Q is in dB (alpha = sin w0 / (2 * 10^(Q/20)); the default Q = 1 -> 1.122),
// bandpass/peaking Q is linear, shelves use S = 1. Transposed direct form II, double state. A struct: embed it in a
// voice; Set() may be called while running (state is kept).
using System;

namespace MusicRoom.Audio.Dsp
{
    public enum BiquadType : byte { Lowpass, Highpass, Bandpass, Peaking, Lowshelf, Highshelf }

    public struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public static Biquad Create(BiquadType type, double freq, double q, double gainDb, double sampleRate)
        {
            var f = new Biquad();
            f.Set(type, freq, q, gainDb, sampleRate);
            return f;
        }

        public void Set(BiquadType type, double freq, double q, double gainDb, double sampleRate)
        {
            // Parameter values are float32; rounding f, Q and G first keeps bit-level parity with the reference.
            freq = (float)freq; q = (float)q; gainDb = (float)gainDb;
            double nyquist = 0.5 * sampleRate;
            if (freq >= nyquist)
            {
                // At or above Nyquist: lowpass, peaking, highshelf = 1; lowshelf = A^2; highpass and bandpass = 0.
                double g = Math.Pow(10.0, gainDb / 20.0);
                switch (type)
                {
                    case BiquadType.Lowpass: case BiquadType.Peaking: case BiquadType.Highshelf: SetCoefficients(1, 0, 0, 1, 0, 0); return;
                    case BiquadType.Lowshelf: SetCoefficients(g, 0, 0, 1, 0, 0); return;
                    default: SetCoefficients(0, 0, 0, 1, 0, 0); return;
                }
            }
            freq = Math.Max(freq, 1e-3);
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cs = Math.Cos(w0), sn = Math.Sin(w0);
            double A = Math.Pow(10.0, gainDb / 40.0);
            double alpha;
            switch (type)
            {
                case BiquadType.Lowpass:
                    alpha = sn / (2.0 * Math.Pow(10.0, q / 20.0));
                    SetCoefficients((1 - cs) * 0.5, 1 - cs, (1 - cs) * 0.5, 1 + alpha, -2 * cs, 1 - alpha);
                    return;
                case BiquadType.Highpass:
                    alpha = sn / (2.0 * Math.Pow(10.0, q / 20.0));
                    SetCoefficients((1 + cs) * 0.5, -(1 + cs), (1 + cs) * 0.5, 1 + alpha, -2 * cs, 1 - alpha);
                    return;
                case BiquadType.Bandpass:
                    alpha = sn / (2.0 * Math.Max(q, 1e-4));
                    SetCoefficients(alpha, 0, -alpha, 1 + alpha, -2 * cs, 1 - alpha);
                    return;
                case BiquadType.Peaking:
                    alpha = sn / (2.0 * Math.Max(q, 1e-4));
                    SetCoefficients(1 + alpha * A, -2 * cs, 1 - alpha * A, 1 + alpha / A, -2 * cs, 1 - alpha / A);
                    return;
                case BiquadType.Lowshelf:
                {
                    alpha = sn / 2.0 * Math.Sqrt(2.0);
                    double k = 2 * Math.Sqrt(A) * alpha;
                    SetCoefficients(
                        A * ((A + 1) - (A - 1) * cs + k), 2 * A * ((A - 1) - (A + 1) * cs), A * ((A + 1) - (A - 1) * cs - k),
                        (A + 1) + (A - 1) * cs + k, -2 * ((A - 1) + (A + 1) * cs), (A + 1) + (A - 1) * cs - k);
                    return;
                }
                default: // Highshelf
                {
                    alpha = sn / 2.0 * Math.Sqrt(2.0);
                    double k = 2 * Math.Sqrt(A) * alpha;
                    SetCoefficients(
                        A * ((A + 1) + (A - 1) * cs + k), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - k),
                        (A + 1) - (A - 1) * cs + k, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - k);
                    return;
                }
            }
        }

        /// <summary>Raw coefficients (a0 is normalised away). Used by the modal resonator.</summary>
        public void SetCoefficients(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            double inv = 1.0 / a0;
            _b0 = b0 * inv; _b1 = b1 * inv; _b2 = b2 * inv; _a1 = a1 * inv; _a2 = a2 * inv;
        }

        /// <summary>Normalised coefficients (tests compare them with the spec 06 formulas).</summary>
        public void GetCoefficients(out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            b0 = _b0; b1 = _b1; b2 = _b2; a1 = _a1; a2 = _a2;
        }

        /// <summary>|H(f)| in dB of the current coefficients (tests).</summary>
        public double MagnitudeDb(double freq, double sampleRate)
        {
            double w = 2 * Math.PI * freq / sampleRate;
            double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
            double nr = _b0 + _b1 * c1 + _b2 * c2, ni = -(_b1 * s1 + _b2 * s2);
            double dr = 1 + _a1 * c1 + _a2 * c2, di = -(_a1 * s1 + _a2 * s2);
            return 10 * Math.Log10((nr * nr + ni * ni) / Math.Max(dr * dr + di * di, 1e-300));
        }

        public void Reset() { _z1 = 0; _z2 = 0; }

        public float Process(float x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return (float)y;
        }

        /// <summary>In place over buf[from..to).</summary>
        public void Process(float[] buf, int from, int to)
        {
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2, z1 = _z1, z2 = _z2;
            for (int i = from; i < to; i++)
            {
                double x = buf[i];
                double y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                buf[i] = (float)y;
            }
            _z1 = z1; _z2 = z2;
        }
    }
}
