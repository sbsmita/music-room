// In-place iterative radix-2 complex FFT on split float arrays (re, im), for the drum room's partitioned convolution.
// Tables (bit reversal, twiddles) are built once in the constructor; Forward / Inverse allocate nothing (audio thread).
// Twiddles are computed in double and stored as float; the butterflies run in float, ~1e-7 relative error (-140 dB).
using System;

namespace MusicRoom.Audio.Dsp
{
    public sealed class Fft
    {
        public readonly int Size;
        private readonly int[] _rev;
        private readonly float[] _cos, _sin;

        public Fft(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two", nameof(size));
            Size = size;
            int bits = 0;
            while ((1 << bits) < size) bits++;
            _rev = new int[size];
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _rev[i] = r;
            }
            _cos = new float[size / 2];
            _sin = new float[size / 2];
            for (int k = 0; k < size / 2; k++)
            {
                double a = -2 * Math.PI * k / size;
                _cos[k] = (float)Math.Cos(a);
                _sin[k] = (float)Math.Sin(a);
            }
        }

        /// <summary>X[k] = sum_n x[n] e^(-2 pi i k n / N), in place.</summary>
        public void Forward(float[] re, float[] im) => Transform(re, im, 1f);

        /// <summary>x[n] = (1/N) sum_k X[k] e^(+2 pi i k n / N), in place.</summary>
        public void Inverse(float[] re, float[] im)
        {
            Transform(re, im, -1f);
            float s = 1f / Size;
            for (int i = 0; i < Size; i++) { re[i] *= s; im[i] *= s; }
        }

        private void Transform(float[] re, float[] im, float sign)
        {
            int n = Size;
            for (int i = 0; i < n; i++)
            {
                int j = _rev[i];
                if (j > i)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1, step = n / len;
                for (int start = 0; start < n; start += len)
                {
                    for (int k = 0, tw = 0; k < half; k++, tw += step)
                    {
                        float wr = _cos[tw], wi = sign * _sin[tw];
                        int a = start + k, b = a + half;
                        float xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                    }
                }
            }
        }
    }
}
