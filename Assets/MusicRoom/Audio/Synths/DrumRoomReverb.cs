// The drum room (spec 06 sections 4 and 6: a convolver with makeRoomImpulse 0.45 s, preDelay 4 ms, fast .3, slow .45,
// lp .3, early .8, normalize = true; return x 0.3 to 'drum:room'), as a processor on the DrumRoom source that the drum
// sources feed through sendTarget (the 0.3 return is the source's bus). Placed at the kit centre by the Drums module
// (the reference left it at the origin by accident).
// The convolution is exact: a uniformly partitioned overlap-save FFT convolution (partitions of 256 = the DSP block, so no
// added latency at 256-frame callbacks) with the seeded IR's left channel times the convolver's normalisation scale (the
// scale takes the rms over both channels, spec 06 sections 1.4 and 6). Why not the earlier FDN: the reference IR
// is 450 ms of decaying noise cut off while still at -19 dB, which no feedback network reproduces; this is ~80 us per
// block while drums ring and nothing at all once the input has been silent for the IR's length (exact idle skip).
// The room is mono: our DrumRoom source feeds one spatializer channel (a stereo IR into an HRTF panner is
// unverified, spec 06 section 13.4).
using System;
using MusicRoom.Audio.Dsp;
using MusicRoom.Core;

namespace MusicRoom.Audio.Synths
{
    public sealed class DrumRoomReverb : ISynthProcessor
    {
        public const double Seconds = 0.45, PreDelay = 0.004, Fast = 0.3, Slow = 0.45, Lp = 0.3, Early = 0.8;
        public const int Block = 256;

        private readonly Fft _fft = new Fft(2 * Block);
        private readonly int _parts;
        private readonly float[][] _hRe, _hIm;      // partition spectra (bins 0..Block)
        private readonly float[][] _xRe, _xIm;      // frequency-domain delay line of input spectra
        private readonly float[] _re = new float[2 * Block], _im = new float[2 * Block];
        private readonly float[] _prev = new float[Block], _in = new float[Block], _out = new float[Block];
        private int _head, _fill, _silent;

        /// <summary>The effective impulse response (IR left channel x convolver scale): what an impulse into the room returns.</summary>
        public float[] Impulse { get; }
        public double Scale { get; }
        /// <summary>Partition steps computed (diagnostics: stays put while the room is idle).</summary>
        public long Steps { get; private set; }

        /// <summary>Main thread. seed: the IR noise (uniform random draws; 06 seeds.drumRoomIR = 6).</summary>
        public DrumRoomReverb(int sr, uint seed)
        {
            var rng = new Mulberry32(seed);
            float[][] ir = Notes.MakeRoomImpulse(sr, Seconds, PreDelay, Fast, Slow, Lp, Early, ref rng);
            Scale = Notes.ConvolverScale(ir, sr);
            var h = new float[ir[0].Length];
            for (int i = 0; i < h.Length; i++) h[i] = (float)(ir[0][i] * Scale);
            Impulse = h;
            _parts = (h.Length + Block - 1) / Block;
            _hRe = new float[_parts][]; _hIm = new float[_parts][];
            _xRe = new float[_parts][]; _xIm = new float[_parts][];
            for (int p = 0; p < _parts; p++)
            {
                Array.Clear(_re, 0, _re.Length);
                Array.Clear(_im, 0, _im.Length);
                for (int i = 0; i < Block && p * Block + i < h.Length; i++) _re[i] = h[p * Block + i];
                _fft.Forward(_re, _im);
                _hRe[p] = new float[Block + 1]; _hIm[p] = new float[Block + 1];
                Array.Copy(_re, _hRe[p], Block + 1);
                Array.Copy(_im, _hIm[p], Block + 1);
                _xRe[p] = new float[Block + 1]; _xIm[p] = new float[Block + 1];
            }
            _silent = _parts;   // idle: the delay line holds only zeros
        }

        public int Partitions => _parts;

        public void OnCommand(in SynthCommand c, long atSample) { }

        /// <summary>Idle (never in practice: the DrumRoom bus is the constant 0.3 return): as a block of zero input once the
        /// room has rung out; a tail still ringing is dropped (nothing is pending and the bus is silent).</summary>
        public void Skip(long blockStart, int frames)
        {
            if (_silent > _parts && _fill == 0) return;   // already idle: the delay line and the overlap half are zero
            for (int p = 0; p < _parts; p++) { Array.Clear(_xRe[p], 0, Block + 1); Array.Clear(_xIm[p], 0, Block + 1); }
            Array.Clear(_prev, 0, Block);
            Array.Clear(_in, 0, Block);
            Array.Clear(_out, 0, Block);
            _fill = 0;
            _silent = _parts + 1;
        }

        /// <summary>Replaces the dry sends in mix with the room's output.</summary>
        public void Process(float[] mix, long blockStart, int frames)
        {
            if (_fill == 0 && frames % Block == 0)
            {
                // Callbacks of whole partitions (the 256-frame DSP buffer): convolve in place, no latency.
                for (int c = 0; c < frames; c += Block)
                {
                    Array.Copy(mix, c, _in, 0, Block);
                    Step();
                    Array.Copy(_out, 0, mix, c, Block);
                }
                return;
            }
            // Any other callback size: a partition FIFO (one partition of latency).
            for (int i = 0; i < frames; i++)
            {
                float x = mix[i];
                mix[i] = _out[_fill];
                _in[_fill] = x;
                if (++_fill == Block) { Step(); _fill = 0; }
            }
        }

        /// <summary>One partition: _in -> _out (overlap-save over [previous, current]).</summary>
        private void Step()
        {
            bool zero = true;
            for (int i = 0; i < Block; i++) if (_in[i] != 0f) { zero = false; break; }
            _silent = zero ? _silent + 1 : 0;
            if (_silent > _parts)
            {
                // Every spectrum in the delay line and the previous half are zero: the output is exactly zero.
                Array.Clear(_out, 0, Block);
                return;
            }
            Steps++;
            int bins = Block + 1, n = 2 * Block;
            Array.Copy(_prev, 0, _re, 0, Block);
            Array.Copy(_in, 0, _re, Block, Block);
            Array.Clear(_im, 0, n);
            Array.Copy(_in, 0, _prev, 0, Block);
            _fft.Forward(_re, _im);
            _head = _head == 0 ? _parts - 1 : _head - 1;
            Array.Copy(_re, _xRe[_head], bins);
            Array.Copy(_im, _xIm[_head], bins);
            // Y = sum_p X(now - p) H_p over the non-negative bins.
            Array.Clear(_re, 0, n);
            Array.Clear(_im, 0, n);
            for (int p = 0, slot = _head; p < _parts; p++)
            {
                float[] xr = _xRe[slot], xi = _xIm[slot], hr = _hRe[p], hi = _hIm[p];
                for (int k = 0; k < bins; k++)
                {
                    float a = xr[k], b = xi[k], c = hr[k], d = hi[k];
                    _re[k] += a * c - b * d;
                    _im[k] += a * d + b * c;
                }
                if (++slot == _parts) slot = 0;
            }
            // Real signal: the negative bins mirror the positive ones.
            for (int k = 1; k < Block; k++) { _re[n - k] = _re[k]; _im[n - k] = -_im[k]; }
            _fft.Inverse(_re, _im);
            Array.Copy(_re, Block, _out, 0, Block);
        }
    }
}
