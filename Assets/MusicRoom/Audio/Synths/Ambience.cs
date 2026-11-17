// Ambience beds (spec 06 section 7): fire (lp 420 + pops), wind (rumble lp 260 + whistle swept bp
// 650 +- 320), snow (hush lp 520 Q .3) and the snow gust. Beds are audio-thread processors on the Fire/Wind/Snow
// sources and keep time from the first audio block on (built once at audio start, LFOs from phase 0);
// on/off ramps the source bus with setTarget tau 0.4 s. Once a switch has been off long enough for its bus to fall
// below -120 dB (5.5 s), SynthSource skips the source: the processors' Skip advances the LFO time, the noise position
// and the pop timer exactly, without the per-sample filters and trig (about 7 Math.Sin/Cos per sample for the four beds),
// and a bed coming back re-filters its last 85 ms of noise first, so it continues as if it had never been skipped.
// Nulled against Tests/EditMode/Audio/Reference~/amb_*.wav (fire 6 s with the 110 ms pop timer, wind 8 s, snow with a gust at 2 s): every
// detail below is what that took.
using System;
using MusicRoom.Audio.Dsp;
using MusicRoom.Core;

namespace MusicRoom.Audio.Synths
{
    public sealed class Ambience
    {
        readonly SynthKit _kit;
        readonly FirePops _pops;
        readonly SnowGustProcessor _gust;
        Mulberry32 _rng;   // the gust's f0 (main thread); the pops draw on the audio thread from their own stream
        public bool FireOn { get; private set; }
        public bool WindOn { get; private set; }
        public bool SnowOn { get; private set; }
        /// <summary>Gusts sent to the snow source (diagnostics, tests).</summary>
        public int GustsPlayed { get; private set; }

        public Ambience(SynthKit kit, int sr, uint seed = 11)
        {
            _kit = kit;
            _rng = new Mulberry32(seed);
            var fire = kit.S(SourceId.Fire);
            fire.AddProcessor(new NoiseBed(sr, BiquadType.Lowpass, 420, 1, 0.09, 0.03, 0.17));
            _pops = new FirePops(sr, seed);
            fire.AddProcessor(_pops);
            var wind = kit.S(SourceId.Wind);
            wind.AddProcessor(new NoiseBed(sr, BiquadType.Lowpass, 260, 1, 0.1, 0.06, 0.07));
            wind.AddProcessor(new NoiseBed(sr, BiquadType.Bandpass, 650, 6, 0.1, 0.07, 0.05, 320, 0.11));
            var snow = kit.S(SourceId.Snow);
            snow.AddProcessor(new NoiseBed(sr, BiquadType.Lowpass, 520, 0.3, 0.07, 0.025, 0.05));
            _gust = new SnowGustProcessor(sr);
            snow.AddProcessor(_gust);
        }

        /// <summary>Pops and the gust's f0 are uniform random draws; tests seed both streams (06 seeds.ambience = 11).
        /// The pop stream is re-seeded on the audio thread (a command), so this is safe while playing.</summary>
        public void Seed(uint seed)
        {
            _rng = new Mulberry32(seed);
            _kit.Stage(_kit.S(SourceId.Fire), new SynthCommand { Op = SynthOp.Custom, Kind = SynthCustom.Seed, VoiceId = (int)seed });
            _kit.Flush();
        }

        public void SetFire(bool on)
        {
            FireOn = on;
            _kit.Bus(SourceId.Fire, on);
            _kit.Stage(_kit.S(SourceId.Fire), new SynthCommand { Op = SynthOp.Custom, Kind = SynthCustom.FirePops, Level = on ? 1 : 0 });
            _kit.Flush();
        }

        public void SetWind(bool on) { WindOn = on; _kit.Bus(SourceId.Wind, on); _kit.Flush(); }
        public void SetSnow(bool on) { SnowOn = on; _kit.Bus(SourceId.Snow, on); _kit.Flush(); }

        /// <summary>SnowWeather.onGust: only while the snow sound is on; f0 = 360 + 220 R(),
        /// peak = .025 + .05 strength; the gust starts now with the visual gust's own attack / hold / release.</summary>
        public void Gust(float strength, float attack, float hold, float release)
        {
            if (!SnowOn) return;
            float f0 = (float)(360 + _rng.Next() * 220);
            _kit.Stage(_kit.S(SourceId.Snow), new SynthCommand
            {
                Op = SynthOp.Custom, Kind = SynthCustom.SnowGust, F0 = f0, Level = strength, Slide = attack, StopAfter = hold, Tau = release,
                Peak = 0.025f + 0.05f * strength,
            });
            _kit.Flush();
            GustsPlayed++;
        }
    }

    /// <summary>A looped-noise bed: shared noise from index 0 -> biquad -> gain base + depth sin(2 pi rate t).
    /// A swept corner (the wind's whistle: 650 + 320 sin(2 pi 0.11 t)) is a per-sample parameter driven by an oscillator,
    /// so the coefficients are recomputed every sample: nulling the wind needs that (-75 dB; every 32 frames: -66 dB).</summary>
    public sealed class NoiseBed : ISynthProcessor
    {
        private readonly BiquadType _type;
        private readonly double _freq, _q, _sweepHz, _sweepW, _gain, _gainDepth, _gainW, _sr, _bandQ;
        private Biquad _filter;
        private NoiseReader _noise;
        private long _t;

        public NoiseBed(double sampleRate, BiquadType type, double freq, double q, double gain, double gainDepth, double gainRate,
            double sweepHz = 0, double sweepRate = 0)
        {
            _sr = sampleRate; _type = type; _freq = freq; _q = q;
            _gain = gain; _gainDepth = gainDepth; _gainW = 2 * Math.PI * gainRate / sampleRate;
            _sweepHz = sweepHz; _sweepW = 2 * Math.PI * sweepRate / sampleRate;
            _bandQ = (float)q;
            _filter = Biquad.Create(type, freq, q, 0, sampleRate);
            _noise.Start(0);
        }

        public void OnCommand(in SynthCommand c, long atSample) { }

        /// <summary>Samples of noise the filter re-reads when the bed comes back from idle (85 ms at 48 kHz: the beds'
        /// filters forget their state much faster, the slowest being the whistle's Q 6 bandpass at 5.8 ms).</summary>
        public const int PreRollSamples = 4096;
        private bool _skipped;

        /// <summary>Idle (the switch has been off for seconds): the LFO time and the noise position move on exactly, so the
        /// bed comes back in phase with a never-skipped one.</summary>
        public void Skip(long blockStart, int frames)
        {
            _t += frames;
            _noise.Skip(frames, NoiseTable.Samples.Length);
            _skipped = true;
        }

        /// <summary>Back from idle: rebuild the filter's state from the noise it would have filtered in the last
        /// PreRollSamples (at their exact sweep times), so the bed continues as if it had never been skipped.</summary>
        private void PreRoll(float[] table)
        {
            int len = table.Length, n = (int)Math.Min(PreRollSamples, _t);
            int start = (int)(((long)_noise.Position - n) % len);
            if (start < 0) start += len;
            var reader = new NoiseReader();
            reader.Start(start);
            _filter.Reset();
            bool swept = _sweepHz != 0;
            for (long t = _t - n; t < _t; t++)
            {
                if (swept) Sweep((float)(_freq + _sweepHz * Math.Sin(_sweepW * t)));
                _filter.Process(reader.Next(table));
            }
        }

        public void Process(float[] mix, long blockStart, int frames)
        {
            float[] table = NoiseTable.Samples;
            if (_skipped) { PreRoll(table); _skipped = false; }
            bool swept = _sweepHz != 0;
            for (int i = 0; i < frames; i++, _t++)
            {
                if (swept) Sweep((float)(_freq + _sweepHz * Math.Sin(_sweepW * _t)));
                float g = (float)(_gain + _gainDepth * Math.Sin(_gainW * _t));
                mix[i] += _filter.Process(_noise.Next(table)) * g;
            }
        }

        private void Sweep(double f)
        {
            if (_type != BiquadType.Bandpass) { _filter.Set(_type, f, _q, 0, _sr); return; }
            // Biquad.Set's bandpass without its Pow (gain is unused): b = (alpha, 0, -alpha), a = (1 + alpha, -2 cos, 1 - alpha).
            double w0 = 2 * Math.PI * f / _sr, alpha = Math.Sin(w0) / (2 * _bandQ);
            _filter.SetCoefficients(alpha, 0, -alpha, 1 + alpha, -2 * Math.Cos(w0), 1 - alpha);
        }
    }

    /// <summary>Fire pops (spec 06 section 7). A 110 ms interval timer runs from the ambience's start; each tick while
    /// the fire is on draws R() and, if R() &lt;= 0.65, a pop at tick + 0.2 R() s: noise -> bandpass 1600 + 3500 R() Hz Q 2.5
    /// -> peak .05 + .1 R() falling to .0005 over .012 + .03 R() s (draw order: test, time, bp, peak, decay; mulberry32 so
    /// a seed reproduces the reference stream). The tick is a sample counter (5280 frames at 48 kHz, first tick 110 ms in).
    /// A pop starts between samples: its noise is read at the fractional position (linear interpolation) and it runs the
    /// envelope on the true time, which the reference render needs to null (-77 dB; whole-sample starts: -23 dB).</summary>
    public sealed class FirePops : ISynthProcessor
    {
        private struct Pop
        {
            public bool Active;
            public double Start;        // fractional engine sample
            public long First, End;     // first frame at or after Start; the source's stop frame
            public Biquad Bp;
            public double Env, Factor;  // envelope at the next frame and its per-frame factor
            public long DecayEnd;       // first frame at or after Start + decay (the envelope holds 0.0005 from there)
        }

        public const double Period = 0.110;
        private static readonly double Floor = (float)0.0005;   // the envelope target as a float32 parameter
        private readonly Pop[] _pops = new Pop[8];
        private readonly double _sr, _period;
        private Mulberry32 _rng;
        private long _origin = -1, _tick;
        private bool _on;

        public FirePops(double sampleRate, uint seed = 11)
        {
            _sr = sampleRate;
            _period = Period * sampleRate;
            _rng = new Mulberry32(seed);
        }

        /// <summary>Pops started so far (diagnostics, tests). Audio thread writes, any thread reads.</summary>
        public int PopCount { get; private set; }

        public void OnCommand(in SynthCommand c, long atSample)
        {
            if (c.Kind == SynthCustom.FirePops) _on = c.Level > 0;
            else if (c.Kind == SynthCustom.Seed) _rng = new Mulberry32((uint)c.VoiceId);
        }

        /// <summary>The 110 ms timer up to blockEnd: one tick per period, a draw (and maybe a pop) only while the fire is on.</summary>
        private void Ticks(long blockStart, long blockEnd)
        {
            if (_origin < 0) { _origin = blockStart; _tick = 1; }
            while (true)
            {
                double tickAt = _origin + _tick * _period;
                if (tickAt >= blockEnd) break;
                _tick++;
                if (!_on || _rng.Next() > 0.65) continue;   // amb.fireOn is tested first: no draw while the fire is off
                double start = tickAt + _rng.Next() * 0.2 * _sr;
                double bp = 1600 + _rng.Next() * 3500, peak = 0.05 + _rng.Next() * 0.1, decay = 0.012 + _rng.Next() * 0.03;
                Spawn(start, bp, peak, decay);
            }
        }

        /// <summary>Idle: the timer keeps its phase and its draws (none while the fire is off); finished pops are freed.</summary>
        public void Skip(long blockStart, int frames)
        {
            long blockEnd = blockStart + frames;
            Ticks(blockStart, blockEnd);
            for (int p = 0; p < _pops.Length; p++)
                if (_pops[p].Active && _pops[p].End + Voices.Voice.RingSamples <= blockEnd) _pops[p].Active = false;
        }

        public void Process(float[] mix, long blockStart, int frames)
        {
            long blockEnd = blockStart + frames;
            Ticks(blockStart, blockEnd);
            float[] table = NoiseTable.Samples;
            int len = table.Length;
            for (int p = 0; p < _pops.Length; p++)
            {
                ref Pop pop = ref _pops[p];
                if (!pop.Active) continue;
                long finish = pop.End + Voices.Voice.RingSamples;   // the bandpass rings on after the source stops
                long from = Math.Max(pop.First, blockStart), to = Math.Min(finish, blockEnd);
                for (long n = from; n < to; n++)
                {
                    float x = 0f;
                    if (n < pop.End)
                    {
                        double pos = n - pop.Start;
                        int i0 = (int)pos;
                        double fr = pos - i0;
                        float a = table[i0 % len], b = table[(i0 + 1) % len];
                        x = (float)(a + fr * (b - a));
                    }
                    double e = n < pop.DecayEnd ? pop.Env : Floor;
                    mix[(int)(n - blockStart)] += (float)(pop.Bp.Process(x) * e);
                    pop.Env *= pop.Factor;
                }
                if (finish <= blockEnd) pop.Active = false;
            }
        }

        private void Spawn(double start, double bp, double peak, double decay)
        {
            for (int p = 0; p < _pops.Length; p++)
            {
                ref Pop pop = ref _pops[p];
                if (pop.Active) continue;
                double P = (float)Math.Max(peak, 0.0005), decaySamples = decay * _sr;
                pop.Active = true;
                pop.Start = start;
                pop.First = (long)Math.Ceiling(start);
                pop.End = (long)Math.Ceiling(start + (decay + 0.05) * _sr);
                pop.DecayEnd = (long)Math.Ceiling(start + decaySamples);
                pop.Bp = Biquad.Create(BiquadType.Bandpass, bp, 2.5, 0, _sr);
                pop.Factor = Math.Pow(Floor / P, 1.0 / decaySamples);
                pop.Env = P * Math.Pow(Floor / P, (pop.First - start) / decaySamples);   // envAt(true time of the first frame)
                PopCount++;
                return;
            }
        }
    }

    /// <summary>Snow gust (spec 06 section 7): one noise loop (from index 0 at the gust's start) into two paths: bandpass
    /// Q .7 whose frequency ramps linearly f0 -> f0 (1.25 + .35 strength) over the attack, holds, and ramps back over the
    /// release, times a gain .0001 -> peak -> .0001 with the same shape; and highpass 6000 (Q 1 dB) times .0001 -> .12 peak
    /// -> .0001. The source stops at dur + 0.1 s. Coefficients follow the per-sample frequency every sample;
    /// two gusts may overlap (SnowWeather never overlaps them, but the engine does not rely on that).</summary>
    public sealed class SnowGustProcessor : ISynthProcessor
    {
        private struct Gust
        {
            public bool Active;
            public long Start, End;
            public double F0, F1, Peak, A, H, R;
            public Biquad Bp, Hp;
        }

        private static readonly double Q = (float)0.7, Low = (float)0.0001;
        private readonly Gust[] _gusts = new Gust[2];
        private readonly double _sr;

        public SnowGustProcessor(double sampleRate) { _sr = sampleRate; }

        public void OnCommand(in SynthCommand c, long atSample)
        {
            if (c.Kind != SynthCustom.SnowGust) return;
            int slot = -1;
            for (int i = 0; i < _gusts.Length && slot < 0; i++) if (!_gusts[i].Active) slot = i;
            if (slot < 0)
            {
                slot = 0;   // both busy: replace the older one
                for (int i = 1; i < _gusts.Length; i++) if (_gusts[i].Start < _gusts[slot].Start) slot = i;
            }
            ref Gust g = ref _gusts[slot];
            g.F0 = c.F0;
            g.F1 = c.F0 * (1.25 + 0.35 * c.Level);
            g.Peak = c.Peak;
            g.A = Math.Max(c.Slide, 1e-6); g.H = Math.Max(c.StopAfter, 0); g.R = Math.Max(c.Tau, 1e-6);
            g.Start = atSample;
            g.End = atSample + (long)Math.Ceiling((g.A + g.H + g.R + 0.1) * _sr);
            g.Bp = Biquad.Create(BiquadType.Bandpass, g.F0, Q, 0, _sr);
            g.Hp = Biquad.Create(BiquadType.Highpass, 6000, 1, 0, _sr);
            g.Active = true;
        }

        /// <summary>The gust's automation shape: v0 -> v1 linear over a, hold h, v1 -> v0 linear over r, then v0.</summary>
        public static double Shape(double t, double a, double h, double r, double v0, double v1)
        {
            if (t < a) return v0 + (v1 - v0) * t / a;
            if (t < a + h) return v1;
            if (t < a + h + r) return v1 + (v0 - v1) * (t - a - h) / r;
            return v0;
        }

        /// <summary>Idle (gusts only start while the snow is on, so at most the tail of one is cut): free finished gusts.</summary>
        public void Skip(long blockStart, int frames)
        {
            long blockEnd = blockStart + frames;
            for (int k = 0; k < _gusts.Length; k++) if (_gusts[k].Active && _gusts[k].End <= blockEnd) _gusts[k].Active = false;
        }

        public void Process(float[] mix, long blockStart, int frames)
        {
            long blockEnd = blockStart + frames;
            float[] table = NoiseTable.Samples;
            int len = table.Length;
            for (int k = 0; k < _gusts.Length; k++)
            {
                ref Gust g = ref _gusts[k];
                if (!g.Active) continue;
                long from = Math.Max(g.Start, blockStart), to = Math.Min(g.End, blockEnd);
                for (long n = from; n < to; n++)
                {
                    double t = (n - g.Start) / _sr;
                    double f = (float)Shape(t, g.A, g.H, g.R, g.F0, g.F1);
                    double w0 = 2 * Math.PI * f / _sr, alpha = Math.Sin(w0) / (2 * Q);
                    g.Bp.SetCoefficients(alpha, 0, -alpha, 1 + alpha, -2 * Math.Cos(w0), 1 - alpha);
                    float x = table[(int)((n - g.Start) % len)];
                    float gain = (float)Shape(t, g.A, g.H, g.R, Low, g.Peak);
                    float hiss = (float)Shape(t, g.A, g.H, g.R, Low, g.Peak * 0.12);
                    mix[(int)(n - blockStart)] += g.Bp.Process(x) * gain + g.Hp.Process(x) * hiss;
                }
                if (g.End <= blockEnd) g.Active = false;
            }
        }
    }
}
