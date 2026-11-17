// The engine's generic voices (spec 06 section 2) and playBuffer (spec 06 section 3.2), verified in spec 04 and nulled
// against the 06 reference renders (Tests/EditMode/Audio/NullTests.cs).
using System;
using MusicRoom.Audio.Dsp;

namespace MusicRoom.Audio.Voices
{
    /// <summary>noiseVoice: looped shared noise from index 0 -> [bandpass q] -> [highpass Q 1 dB] -> [lowpass Q 1 dB] -> exp envelope
    /// max(peak, 0.0005) -> 0.0005 over decay; the source stops at decay + 0.05 s (filters ring on until then).</summary>
    public sealed class NoiseVoice : Voice
    {
        private readonly Envelope _env = new Envelope(2);
        private NoiseReader _noise;
        private Biquad _bp, _hp, _lp;
        private bool _useBp, _useHp, _useLp;

        public void Start(in SynthCommand c, long startSample, float sampleRate)
        {
            Begin(c.VoiceId, c.Group, startSample, sampleRate, c.Level <= 0 ? 1f : c.Level);
            _useBp = c.Bp > 0;
            _useHp = c.Hp > 0;
            _useLp = c.Lp > 0 && c.Lp < 20000;
            if (_useBp) _bp = Biquad.Create(BiquadType.Bandpass, c.Bp, c.Q > 0 ? c.Q : 1, 0, sampleRate);
            if (_useHp) _hp = Biquad.Create(BiquadType.Highpass, c.Hp, 1, 0, sampleRate);   // default Q = 1 (dB)
            if (_useLp) _lp = Biquad.Create(BiquadType.Lowpass, c.Lp, 1, 0, sampleRate);
            _env.SetValue(Math.Max(c.Peak, 0.0005f));
            _env.ExponentialTo(0.0005, c.Decay * (double)sampleRate);   // fractional: the ramp ends at t + decay exactly
            _noise.Start(0);
            StopAt(startSample + StopSamples(c.Decay + 0.05));
        }

        protected override bool Process(float[] mix, float[] send, int from, int to)
        {
            float[] table = NoiseTable.Samples;
            bool playing = !SourceStopped;
            for (int i = from; i < to; i++)
            {
                float x = playing ? _noise.Next(table) : 0f;
                if (_useBp) x = _bp.Process(x);
                if (_useHp) x = _hp.Process(x);
                if (_useLp) x = _lp.Process(x);
                mix[i] += x * _env.Next() * Gate.Next();
            }
            return true;
        }
    }

    /// <summary>toneVoice: oscillator f0 -> f1 (exponential glide over slide) -> [lowpass Q 1 dB] -> exp envelope.
    /// The envelope, the glide and the stop count from the voice's start (the event time). Two start details, used by
    /// Sunny's chirps (see SynthFlags): OscLateStart, the oscillator's first frame is one after the voice's (a start time
    /// a hair past a frame; the automation still counts from the event); QuantumLead (Buffer = the oscillator start's offset
    /// in its quantum), inside that first quantum oscillator frame k reads the automation at quantum frame k: the default
    /// 440 Hz before the event, then the glide lagged by the offset, then the true glide from the next quantum.</summary>
    public sealed class ToneVoice : Voice
    {
        /// <summary>The oscillator's default frequency (what the first quantum reads before the start's setValueAtTime).</summary>
        public const double DefaultFrequency = 440;
        public const int Quantum = 128;

        private readonly Envelope _env = new Envelope(2);
        private Oscillator _osc;
        private Biquad _lp;
        private bool _useLp;
        private int _glideLeft, _glideLength, _k, _skip, _pre, _leadDefault, _leadEnd;
        private double _glideFactor, _glideRangeStep, _f0, _f1;

        public void Start(in SynthCommand c, long startSample, float sampleRate)
        {
            Begin(c.VoiceId, c.Group, startSample, sampleRate, c.Level <= 0 ? 1f : c.Level);
            _f0 = c.F0;
            _f1 = c.F1 > 0 ? c.F1 : c.F0;
            _glideLength = _f1 != _f0 ? Math.Max(1, Samples(c.Slide > 0 ? c.Slide : 0.05)) : 0;
            _glideFactor = _glideLength > 0 ? Math.Pow(_f1 / _f0, 1.0 / _glideLength) : 1.0;
            _glideRangeStep = Oscillator.RangeStep(_glideFactor); // one log per glide, not per sample
            _k = 0;
            _skip = _pre = (c.Flags & SynthFlags.OscLateStart) != 0 ? 1 : 0;
            int offset = (c.Flags & SynthFlags.QuantumLead) != 0 ? c.Buffer & (Quantum - 1) : 0;
            _leadEnd = _leadDefault = 0;
            int index = _skip;                                // automation index of oscillator frame 0
            if (offset > 0)
            {
                _leadEnd = Quantum - offset;                  // oscillator frames in the start's quantum
                index = _skip - offset;
                if (index < 0) _leadDefault = Math.Min(-index, _leadEnd);   // ... that read the default before the event
            }
            if (index < 0)
            {
                _osc.Start(c.Wave, DefaultFrequency, sampleRate);
                _glideLeft = 0;
            }
            else
            {
                _osc.Start(c.Wave, Ramp(index), sampleRate);
                _glideLeft = Math.Max(0, _glideLength - index);
            }
            _useLp = c.Lp > 0 && c.Lp < 20000;
            if (_useLp) _lp = Biquad.Create(BiquadType.Lowpass, c.Lp, 1, 0, sampleRate);
            _env.SetValue(Math.Max(c.Peak, 0.0005f));
            _env.ExponentialTo(0.0005, c.Decay * (double)sampleRate);   // fractional: the ramp ends at t + decay exactly
            StopAt(startSample + StopSamples(c.Decay + 0.05));
        }

        /// <summary>The glide at automation index i (i in samples from the event): f0 (f1/f0)^(i / slide), then f1.</summary>
        private double Ramp(int i) => i >= _glideLength ? _f1 : i <= 0 ? _f0 : _f0 * Math.Pow(_glideFactor, i);

        protected override bool Process(float[] mix, float[] send, int from, int to)
        {
            bool playing = !SourceStopped;
            for (int i = from; i < to; i++)
            {
                float x = 0f;
                if (_pre > 0) _pre--;                         // OscLateStart: the oscillator has not started yet
                else if (playing)
                {
                    if (_leadEnd > 0) Lead();
                    x = _osc.Next();
                    if (_glideLeft > 0)
                    {
                        // The glide lands exactly on f1 (exponentialRampToValueAtTime holds its target afterwards).
                        if (--_glideLeft == 0) _osc.SetFrequency(_f1, SampleRate); else _osc.ScaleFrequency(_glideFactor, _glideRangeStep);
                    }
                    _k++;
                }
                if (_useLp) x = _lp.Process(x);
                mix[i] += x * _env.Next() * Gate.Next();
            }
            return true;
        }

        /// <summary>Before oscillator frame _k of a QuantumLead tone: the glide begins (lagged), then the quantum ends.</summary>
        private void Lead()
        {
            if (_k == _leadEnd)
            {
                int index = _leadEnd + _skip;
                _osc.SetFrequency(Ramp(index), SampleRate);
                _glideLeft = Math.Max(0, _glideLength - index);
                _leadEnd = 0;
            }
            else if (_k == _leadDefault)
            {
                _osc.SetFrequency(_f0, SampleRate);
                _glideLeft = _glideLength;
            }
        }
    }

    /// <summary>metalVoice: six squares (263, 400, 421, 474, 587, 845 Hz x ratio) -> bandpass (Q 0.9) -> highpass (Q 1 dB) -> exp envelope.
    /// The oscillators restart at phase 0 on every hit, so the raw six-square sum depends only on the ratio:
    /// MetalBank pre-renders it once per ratio (same code, bit-identical) and the voice is a table read plus two biquads;
    /// before that, or for other ratios, the oscillators run live.</summary>
    public sealed class MetalVoice : Voice
    {
        public static readonly float[] Partials = MetalBank.Partials;
        private readonly Envelope _env = new Envelope(2);
        private readonly Oscillator[] _osc = new Oscillator[6];
        private Biquad _bp, _hp;
        private float[] _sum;
        private int _pos;

        /// <summary>True when this hit reads the pre-rendered sum (diagnostics, tests).</summary>
        public bool UsingBank => _sum != null;

        public void Start(in SynthCommand c, long startSample, float sampleRate)
        {
            Begin(c.VoiceId, c.Group, startSample, sampleRate, c.Level <= 0 ? 1f : c.Level);
            float ratio = c.F0 > 0 ? c.F0 : 1f;
            _sum = Oscillator.PreferPeriodicTables ? MetalBank.Get(ratio, (int)sampleRate) : null;
            _pos = 0;
            if (_sum == null) StartOscillators(_osc, ratio, sampleRate);
            _bp = Biquad.Create(BiquadType.Bandpass, c.Bp > 0 ? c.Bp : 9500, 0.9, 0, sampleRate);
            _hp = Biquad.Create(BiquadType.Highpass, c.Hp > 0 ? c.Hp : 6500, 1, 0, sampleRate);
            _env.SetValue(Math.Max(c.Peak, 0.0005f));
            _env.ExponentialTo(0.0005, c.Decay * (double)sampleRate);   // fractional: the ramp ends at t + decay exactly
            StopAt(startSample + StopSamples(c.Decay + 0.05));
        }

        protected override bool Process(float[] mix, float[] send, int from, int to)
        {
            float[] sum = _sum;
            bool playing = !SourceStopped;
            for (int i = from; i < to; i++)
            {
                float x;
                if (!playing) x = 0f;
                else if (sum != null) x = _pos < sum.Length ? sum[_pos++] : 0f;
                else x = NextSum(_osc);
                x = _hp.Process(_bp.Process(x));
                mix[i] += x * _env.Next() * Gate.Next();
            }
            return true;
        }

        public static void StartOscillators(Oscillator[] osc, float ratio, float sampleRate)
        {
            for (int k = 0; k < 6; k++) osc[k].Start(Wave.Square, (float)(Partials[k] * ratio), sampleRate);
        }

        /// <summary>One sample of the six squares, summed in the reference order (float, as a Float32 bus).</summary>
        public static float NextSum(Oscillator[] o) => o[0].Next() + o[1].Next() + o[2].Next() + o[3].Next() + o[4].Next() + o[5].Next();
    }

    /// <summary>playBuffer plus the harp's pluck comb: pre-rendered buffer at Rate (linear
    /// interpolation) -> [comb: x(t) - depth x(t - lag)] -> lowpass (Q in dB) -> gain Level;
    /// Res of the output goes to the source's resonator send. If the buffer is still rendering the voice waits up to 300 ms.</summary>
    public sealed class BufferVoice : Voice
    {
        private SampleBank _bank;
        private int _slot;
        private float[] _data;
        private double _pos, _rate, _lag;
        private float _comb, _res;
        private Biquad _lp;
        private bool _useLp;
        private int _waitLeft;

        public int Slot => _slot;

        public void Start(in SynthCommand c, long startSample, float sampleRate, SampleBank bank)
        {
            Begin(c.VoiceId, c.Group, startSample, sampleRate, c.Level);
            _bank = bank;
            _slot = c.Buffer;
            _data = bank.Get(_slot);
            _pos = 0;
            _rate = c.Rate > 0 ? c.Rate : 1.0;
            _lag = c.Comb > 0 ? c.F0 * sampleRate : 0;
            _comb = c.Comb;
            _res = c.Res;
            _useLp = c.Lp > 0 && c.Lp < 0.5f * sampleRate;
            if (_useLp) _lp = Biquad.Create(BiquadType.Lowpass, c.Lp, c.Q, 0, sampleRate); // Q in dB (0.4)
            _waitLeft = Samples(0.3);
        }

        protected override bool Process(float[] mix, float[] send, int from, int to)
        {
            if (SourceStopped)
            {
                // The buffer source stopped (release / damp hard stop): the lowpass rings out under the falling gate.
                for (int i = from; i < to; i++)
                {
                    float y = (_useLp ? _lp.Process(0f) : 0f) * Gate.Next();
                    mix[i] += y;
                    if (send != null) send[i] += y * _res;
                }
                return true;
            }
            if (_data == null)
            {
                _data = _bank.Get(_slot);
                if (_data == null)
                {
                    _waitLeft -= to - from;
                    return _waitLeft > 0;
                }
            }
            float[] d = _data;
            int last = d.Length - 1;
            for (int i = from; i < to; i++)
            {
                int k = (int)_pos;
                if (k >= last) return false;
                float frac = (float)(_pos - k);
                float x = d[k] + frac * (d[k + 1] - d[k]);
                if (_comb > 0)
                {
                    double p = _pos - _lag;
                    if (p >= 0)
                    {
                        int j = (int)p;
                        float fj = (float)(p - j);
                        x -= _comb * (d[j] + fj * (d[j + 1] - d[j]));
                    }
                }
                if (_useLp) x = _lp.Process(x);
                float y = x * Gate.Next();
                mix[i] += y;
                if (send != null) send[i] += y * _res;
                _pos += _rate;
            }
            return true;
        }
    }
}
