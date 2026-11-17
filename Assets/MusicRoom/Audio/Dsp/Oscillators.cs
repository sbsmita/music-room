// Oscillators for the tone and metal voices. Two implementations behind one struct:
//  - PeriodicWaveTables: band-limited periodic-wave tables (spec 06 section 1.3, 06 reference tables()/oscillator()):
//    N = 4096, 36 ranges of 400 cents, range r keeps partials 1..min(2047, floor(2^(-r/3) * 2048)), every table scaled by
//    1/max|range-0 table| so the square's plateau is about +-0.843. Built once (worker thread) and used when ready.
//  - PolyBLEP square / naive triangle / table sine (04 sources): the fallback until the tables exist (and for tests of
//    the cheap path). Both start at phase 0.
// The table range (two tables + their weight) depends on the frequency only, so Oscillator selects it once per frequency
// (Start / SetFrequency, and incrementally while gliding) instead of per sample: Next() is the two-table interpolation of
// the 06 reference, bit-identical to PeriodicWaveTables.Sample for a constant frequency (EditMode OscillatorTests).
// MetalBank pre-renders each metal ratio's 6-square sum once (spec 04 section 4.9 optimisation 1): exact, because the metal
// oscillators restart at phase 0 on every hit.
using System;
using System.Diagnostics;
using System.Threading;

namespace MusicRoom.Audio.Dsp
{
    /// <summary>4096-point sine table with linear interpolation (max error ~3e-7, about -130 dB).</summary>
    public static class SineTable
    {
        public const int Size = 4096;
        private static readonly float[] Table = Build();

        private static float[] Build()
        {
            var t = new float[Size + 1];
            for (int i = 0; i <= Size; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / Size);
            return t;
        }

        /// <summary>phase in cycles, [0, 1).</summary>
        public static float Lookup(double phase)
        {
            double x = phase * Size;
            int i = (int)x;
            float frac = (float)(x - i);
            float a = Table[i];
            return a + frac * (Table[i + 1] - a);
        }
    }

    /// <summary>Band-limited periodic-wave tables for sine, square and triangle (06 reference `tables(type)`).</summary>
    public static class PeriodicWaveTables
    {
        public const int N = 4096, Half = 2048, Ranges = 36;
        private static float[][] s_sine, s_square, s_triangle;
        private static volatile bool s_ready;
        private static int s_building;

        public static bool Ready => s_ready;
        /// <summary>How long the last Build took (ms; start-up log).</summary>
        public static double BuildMilliseconds { get; private set; }

        public static float[][] Get(Wave wave) => wave == Wave.Square ? s_square : wave == Wave.Triangle ? s_triangle : s_sine;

        public static double Coefficient(Wave wave, int n)
        {
            switch (wave)
            {
                case Wave.Square: return (n & 1) != 0 ? 4.0 / (n * Math.PI) : 0.0;
                case Wave.Triangle: return (n & 1) != 0 ? 8.0 / (Math.PI * Math.PI * n * n) * ((((n - 1) >> 1) & 1) != 0 ? -1 : 1) : 0.0;
                default: return n == 1 ? 1.0 : 0.0;
            }
        }

        /// <summary>partialsForRange(r) = min(2047, floor(fround(2^(-400 r / 1200)) * 2048)).</summary>
        public static int PartialsForRange(int r) => Math.Min(Half - 1, (int)Math.Floor((float)Math.Pow(2, -(r * 400) / 1200.0) * Half));

        /// <summary>Build all three waveforms (run on a worker; ~4 M multiply-adds per waveform). Idempotent.</summary>
        public static void Build()
        {
            if (s_ready || Interlocked.Exchange(ref s_building, 1) == 1) return;
            var sw = Stopwatch.StartNew();
            double[] sin = SinTable();
            s_sine = BuildWaveFast(Wave.Sine, sin);
            s_square = BuildWaveFast(Wave.Square, sin);
            s_triangle = BuildWaveFast(Wave.Triangle, sin);
            BuildMilliseconds = sw.Elapsed.TotalMilliseconds;
            s_ready = true;
        }

        private static double[] SinTable()
        {
            var sin = new double[N];
            for (int j = 0; j < N; j++) sin[j] = Math.Sin(2 * Math.PI * j / N);
            return sin;
        }

        /// <summary>The 06 reference algorithm verbatim (every range summed from partial 1): the oracle for BuildWaveFast.</summary>
        public static float[][] BuildWave(Wave wave, double[] sin = null)
        {
            if (sin == null) sin = SinTable();
            var out_ = new float[Ranges][];
            double norm = 1;
            var t = new double[N];
            for (int r = 0; r < Ranges; r++)
            {
                int P = PartialsForRange(r);
                Array.Clear(t, 0, N);
                for (int n = 1; n <= P; n++)
                {
                    double bn = Coefficient(wave, n);
                    if (bn == 0) continue;
                    for (int j = 0; j < N; j++) t[j] += bn * sin[(n * j) % N];
                }
                if (r == 0)
                {
                    double m = 0;
                    for (int j = 0; j < N; j++) m = Math.Max(m, Math.Abs(t[j]));
                    norm = m != 0 ? 1 / m : 1;
                }
                var tf = new float[N];
                for (int j = 0; j < N; j++) tf[j] = (float)(t[j] * norm);
                out_[r] = tf;
            }
            return out_;
        }

        /// <summary>Bit-identical to BuildWave, 5x faster: the ranges keep nested partial sets (1..P(r), P falling with r),
        /// so one running sum from the top range down adds every partial once, in the same order BuildWave adds it.</summary>
        public static float[][] BuildWaveFast(Wave wave, double[] sin = null)
        {
            if (sin == null) sin = SinTable();
            var sums = new double[Ranges][];
            var acc = new double[N];
            int done = 0;
            for (int r = Ranges - 1; r >= 0; r--)
            {
                int P = PartialsForRange(r);
                for (int n = done + 1; n <= P; n++)
                {
                    double bn = Coefficient(wave, n);
                    if (bn == 0) continue;
                    int idx = 0;
                    for (int j = 0; j < N; j++)
                    {
                        acc[j] += bn * sin[idx];
                        idx = (idx + n) & (N - 1);
                    }
                }
                if (P > done) done = P;
                sums[r] = (double[])acc.Clone();
            }
            double m = 0;
            for (int j = 0; j < N; j++) m = Math.Max(m, Math.Abs(sums[0][j]));
            double norm = m != 0 ? 1 / m : 1;
            var out_ = new float[Ranges][];
            for (int r = 0; r < Ranges; r++)
            {
                var tf = new float[N];
                double[] s = sums[r];
                for (int j = 0; j < N; j++) tf[j] = (float)(s[j] * norm);
                out_[r] = tf;
            }
            return out_;
        }

        /// <summary>The unclamped range position 1 + 3 log2(f / lowest) of the 06 reference (lowest = sr / 2 / 2048).</summary>
        public static double RangePosition(double f, double sampleRate)
        {
            double lowest = sampleRate / 2 / Half;
            double ratio = f > 0 ? f / lowest : 0.5;
            return 1 + Math.Log(ratio, 2) * 1200 / 400;
        }

        /// <summary>One sample at `phase` (in table units, [0, N)) for frequency f (06 reference oscillator()). Reference
        /// path (tests): it recomputes the range every call; voices use Oscillator, which selects it once per frequency.</summary>
        public static float Sample(float[][] T, double phase, double f, double sampleRate)
        {
            double pr = RangePosition(f, sampleRate);
            pr = Math.Min(Math.Max(pr, 0), Ranges - 1);
            int r1 = (int)Math.Floor(pr), r2 = r1 < Ranges - 1 ? r1 + 1 : r1;
            double tf = pr - r1;
            float[] hi = T[r1], lo = T[r2];
            int i0 = (int)Math.Floor(phase), i1 = (i0 + 1) & (N - 1);
            double fr = phase - i0;
            double sh = (1 - fr) * hi[i0] + fr * hi[i1];
            double sl = (1 - fr) * lo[i0] + fr * lo[i1];
            return (float)((1 - tf) * sh + tf * sl);
        }
    }

    /// <summary>Oscillator for the tone and metal voices (sine / square / triangle). Starts at phase 0.</summary>
    public struct Oscillator
    {
        /// <summary>The band-limited square's plateau is about +-0.848 (06 section 1.3 measured 0.8434 mean |x| at 263 Hz).</summary>
        public const double SquareScale = 0.848;
        /// <summary>Default for new voices: exact periodic-wave tables when built, PolyBLEP otherwise.</summary>
        public static bool PreferPeriodicTables = true;

        private double _phase, _inc;   // phase in cycles [0, 1)
        private double _hz, _sr;
        private Wave _wave;
        private float[][] _tables;
        // Periodic-table range for the current frequency (selected once per frequency, never per sample).
        private float[] _hi, _lo;
        private double _tf;            // weight of the next (fewer partials) table
        private double _prRaw;         // unclamped RangePosition(_hz)

        public void Start(Wave wave, double hz, double sampleRate) =>
            Start(wave, hz, sampleRate, PreferPeriodicTables && PeriodicWaveTables.Ready);

        /// <summary>useTables: the exact periodic-wave tables (they must be built), else PolyBLEP / naive / sine table.</summary>
        public void Start(Wave wave, double hz, double sampleRate, bool useTables)
        {
            _wave = wave;
            _phase = 0;
            _sr = sampleRate;
            _tables = useTables && PeriodicWaveTables.Ready ? PeriodicWaveTables.Get(wave) : null;
            SetFrequency(hz, sampleRate);
        }

        public bool UsingPeriodicTables => _tables != null;

        public void SetFrequency(double hz, double sampleRate)
        {
            _hz = hz;
            _sr = sampleRate;
            _inc = Math.Min(hz / sampleRate, 0.5);
            if (_tables != null)
            {
                _prRaw = PeriodicWaveTables.RangePosition(hz, sampleRate);
                SelectRange();
            }
        }

        /// <summary>Multiply the frequency (exponential glide step). rangeStep = RangeStep(factor), computed once per glide
        /// by the caller, moves the table range with the frequency without a log per sample.</summary>
        public void ScaleFrequency(double factor, double rangeStep)
        {
            _hz *= factor;
            _inc = Math.Min(_inc * factor, 0.5);
            if (_tables != null)
            {
                _prRaw += rangeStep;
                SelectRange();
            }
        }

        /// <summary>How far one ScaleFrequency(factor) moves the range position: 3 log2(factor).</summary>
        public static double RangeStep(double factor) => factor > 0 ? Math.Log(factor, 2) * 1200 / 400 : 0;

        private void SelectRange()
        {
            double pr = Math.Min(Math.Max(_prRaw, 0), PeriodicWaveTables.Ranges - 1);
            int r1 = (int)pr, r2 = r1 < PeriodicWaveTables.Ranges - 1 ? r1 + 1 : r1; // pr >= 0: (int) == floor
            _tf = pr - r1;
            _hi = _tables[r1];
            _lo = _tables[r2];
        }

        public float Next()
        {
            double t = _phase;
            _phase += _inc;
            if (_phase >= 1.0) _phase -= 1.0;
            if (_tables != null)
            {
                // PeriodicWaveTables.Sample with the range hoisted out: same operations in the same order.
                double p = t * PeriodicWaveTables.N;
                int i0 = (int)p, i1 = (i0 + 1) & (PeriodicWaveTables.N - 1);
                double fr = p - i0;
                double sh = (1 - fr) * _hi[i0] + fr * _hi[i1];
                double sl = (1 - fr) * _lo[i0] + fr * _lo[i1];
                return (float)((1 - _tf) * sh + _tf * sl);
            }
            switch (_wave)
            {
                case Wave.Square:
                {
                    double v = t < 0.5 ? 1.0 : -1.0;
                    v += PolyBlep(t, _inc);
                    double t2 = t + 0.5;
                    if (t2 >= 1.0) t2 -= 1.0;
                    v -= PolyBlep(t2, _inc);
                    return (float)(v * SquareScale);
                }
                case Wave.Triangle:
                    return (float)(t < 0.25 ? 4 * t : t < 0.75 ? 2 - 4 * t : 4 * t - 4);
                default:
                    return SineTable.Lookup(t);
            }
        }

        public static double PolyBlep(double t, double dt)
        {
            if (dt <= 0) return 0;
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1.0;
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return t * t + t + t + 1.0;
            }
            return 0;
        }
    }

    /// <summary>Pre-rendered raw 6-square sums of the metal voice (263, 400, 421, 474, 587, 845 Hz x ratio), one per ratio
    /// the drum recipes use (spec 04 section 4.9 optimisation 1). Every metal hit restarts its six oscillators at phase
    /// 0, so the sum depends only on the ratio and the sample rate: the voice reads it instead of running six oscillators
    /// (exactly the same floats as the live periodic-table path). Built once on a render worker after the periodic tables;
    /// until then (and for other ratios) MetalVoice runs its oscillators.</summary>
    public static class MetalBank
    {
        public static readonly float[] Partials = { 263f, 400f, 421f, 474f, 587f, 845f };
        /// <summary>The ratios of DrumSynth: hats 1, ride 1.3, crash 1.7, crash bell 1.9.</summary>
        public static readonly float[] Ratios = { 1f, 1.3f, 1.7f, 1.9f };
        /// <summary>Longest metal layer is 1.2 s + 0.05 s stop; 2 s leaves room for retuned recipes.</summary>
        public const double Seconds = 2.0;

        private sealed class Sums { public int SampleRate; public float[][] ByRatio; }
        private static Sums s_bank; // published whole (Volatile.Write) so a reader never mixes two builds

        public static bool Ready => Volatile.Read(ref s_bank) != null;

        /// <summary>Worker thread, after PeriodicWaveTables.Build. Idempotent per sample rate.</summary>
        public static void Build(int sampleRate)
        {
            var cur = Volatile.Read(ref s_bank);
            if (cur != null && cur.SampleRate == sampleRate) return;
            if (!PeriodicWaveTables.Ready) PeriodicWaveTables.Build();
            if (!PeriodicWaveTables.Ready) return; // another thread is still building them; voices keep their oscillators
            int n = (int)Math.Ceiling(Seconds * sampleRate);
            var sums = new Sums { SampleRate = sampleRate, ByRatio = new float[Ratios.Length][] };
            for (int r = 0; r < Ratios.Length; r++) sums.ByRatio[r] = Render(Ratios[r], sampleRate, n);
            Volatile.Write(ref s_bank, sums);
        }

        /// <summary>The live MetalVoice computation (periodic-wave tables), sample by sample: float sum of the six oscillators.</summary>
        public static float[] Render(float ratio, int sampleRate, int n)
        {
            var osc = new Oscillator[6];
            for (int k = 0; k < 6; k++) osc[k].Start(Wave.Square, (float)(Partials[k] * ratio), sampleRate, true);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = osc[0].Next() + osc[1].Next() + osc[2].Next() + osc[3].Next() + osc[4].Next() + osc[5].Next();
            return d;
        }

        /// <summary>The sum for this ratio at this rate, or null (not built yet / another ratio). Any thread.</summary>
        public static float[] Get(float ratio, int sampleRate)
        {
            var bank = Volatile.Read(ref s_bank);
            if (bank == null || bank.SampleRate != sampleRate) return null;
            for (int r = 0; r < Ratios.Length; r++) if (Ratios[r] == ratio) return bank.ByRatio[r];
            return null;
        }
    }
}
