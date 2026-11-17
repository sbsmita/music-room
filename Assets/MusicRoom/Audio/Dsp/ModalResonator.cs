// The synths' body convolvers (piano soundboard, harp soundbox) as a bank of two-pole resonators.
// makeBodyImpulse (06 reference) builds h(t) = sum_k sin(2 pi f_k t + phi_k) e^(-t / (seconds (0.35 - 0.03 k))) / (1 + 0.5 k)
// over len = floor(sr seconds) samples: a sum of exponentially decaying sines, so convolving with it is exactly a parallel
// bank of resonators H_k(z) = a (sin phi - r sin(phi - W) z^-1) / (1 - 2 r cos W z^-1 + r^2 z^-2), MINUS the same modes fed
// with the input delayed by len (amplitude a r^len, phase phi + W len), which removes each mode's ring past the IR's end.
// The IR stops while its first mode is still at -25 dB (0.28 s), so the cancel bank is what makes the piano and harp
// null tests pass (spec 06 section 6). 2 x 8 modes x 5 MACs per sample instead of a 13,440-tap convolution.
// Level: the convolver normalization (Notes.ConvolverScale) rescales the IR by
// 1 / rms(h) * 10^(-58/20) * 44100 / sampleRate over the IR's length; the bank is scaled to the same rms.
using System;
using MusicRoom.Core;

namespace MusicRoom.Audio.Dsp
{
    public sealed class ModalResonator
    {
        private readonly Biquad[] _modes, _cancel;
        private int _count;
        private float[] _delay = Array.Empty<float>();   // the last len inputs, for the cancel bank
        private int _len = 1, _pos, _zeroRun;

        public ModalResonator(int maxModes = 12)
        {
            _modes = new Biquad[maxModes];
            _cancel = new Biquad[maxModes];
        }

        public int ModeCount => _count;
        /// <summary>The IR length in samples (floor(sr * seconds)).</summary>
        public int Length => _len;
        /// <summary>The convolver normalisation applied (1 / rms * 10^(-58/20) * 44100 / sr for the unit-amplitude modes).</summary>
        public double Scale { get; private set; }

        /// <summary>Main thread. Phases from mulberry32(seed), drawn first, one per mode, as makeBodyImpulse draws
        /// Math.random() * 2 pi: seed 2 reproduces the 06 piano board, seed 3 the old harp box.</summary>
        public void Build(float[] modeHz, float seconds, float sampleRate, uint seed)
        {
            var rng = new Mulberry32(seed);
            int count = Math.Min(modeHz.Length, _modes.Length);
            var phases = new double[count];
            for (int k = 0; k < count; k++) phases[k] = rng.Next() * Math.PI * 2;
            Build(modeHz, seconds, sampleRate, phases);
        }

        /// <summary>Main thread (allocates the delay line and measures the IR's rms by running an impulse).</summary>
        public void Build(float[] modeHz, float seconds, float sampleRate, double[] phases)
        {
            _count = Math.Min(Math.Min(modeHz.Length, _modes.Length), phases.Length);
            _len = Math.Max(1, (int)Math.Floor(sampleRate * seconds));
            _delay = new float[_len];
            SetModes(modeHz, seconds, sampleRate, phases, 1.0);
            double sumSq = 0;
            for (int n = 0; n < _len; n++)
            {
                float x = n == 0 ? 1f : 0f;
                double y = 0;
                for (int k = 0; k < _count; k++) y += _modes[k].Process(x);
                sumSq += y * y;
            }
            double rms = Math.Sqrt(sumSq / _len);
            Scale = (rms > 1.25e-4 ? 1.0 / rms : 1.0 / 1.25e-4) * Math.Pow(10, -58.0 / 20.0) * (44100.0 / sampleRate);
            SetModes(modeHz, seconds, sampleRate, phases, Scale);
            Reset();
        }

        private void SetModes(float[] modeHz, float seconds, float sampleRate, double[] phases, double scale)
        {
            for (int k = 0; k < _count; k++)
            {
                double tau = seconds * (0.35 - k * 0.03);
                double r = Math.Exp(-1.0 / (tau * sampleRate));
                double w = 2 * Math.PI * modeHz[k] / sampleRate;
                double a = scale / (1 + k * 0.5), phi = phases[k];
                _modes[k].SetCoefficients(a * Math.Sin(phi), -a * r * Math.Sin(phi - w), 0, 1, -2 * r * Math.Cos(w), r * r);
                // The ring past len: a r^len r^m sin(W m + phi + W len), m = n - len.
                double ac = a * Math.Pow(r, _len), pc = phi + w * _len;
                _cancel[k].SetCoefficients(ac * Math.Sin(pc), -ac * r * Math.Sin(pc - w), 0, 1, -2 * r * Math.Cos(w), r * r);
                _modes[k].Reset();
                _cancel[k].Reset();
            }
        }

        public void Reset()
        {
            for (int k = 0; k < _count; k++) { _modes[k].Reset(); _cancel[k].Reset(); }
            Array.Clear(_delay, 0, _delay.Length);
            _pos = 0;
            _zeroRun = _len;
        }

        /// <summary>Audio thread: output[i] += gain * (input * h)[i] for i in [0, frames), h the truncated IR.</summary>
        public void ProcessAdd(float[] input, float[] output, int frames, float gain)
        {
            // Exact idle skip: once the input has been zero for len samples the truncated convolution is exactly 0, so
            // both banks restart from rest (the delay line already holds only zeros).
            bool silent = true;
            for (int i = 0; i < frames; i++) if (input[i] != 0f) { silent = false; break; }
            if (silent && _zeroRun >= _len) return;
            float[] delay = _delay;
            int len = _len, pos = _pos, count = _count, zeroRun = _zeroRun;
            for (int i = 0; i < frames; i++)
            {
                float x = input[i];
                float xd = delay[pos];
                delay[pos] = x;
                if (++pos == len) pos = 0;
                zeroRun = x == 0f ? zeroRun + 1 : 0;
                double y = 0;
                for (int k = 0; k < count; k++) y += _modes[k].Process(x) - (double)_cancel[k].Process(xd);
                output[i] += (float)(gain * y);
            }
            _pos = pos;
            _zeroRun = zeroRun;
            if (zeroRun >= len)
            {
                for (int k = 0; k < count; k++) { _modes[k].Reset(); _cancel[k].Reset(); }
            }
        }
    }
}
