// Line-for-line implementation of makeCompressor() from Docs/06-audio-dsp.reference.mjs: a peak-detecting compressor
// kernel (behaviourally verified by impulse, step and static tests). Static curve: identity below the
// threshold, soft knee linThr + (1 - e^(-k (x - linThr))) / k with k from a 15-step bisection so the slope at the knee
// end is 1/ratio, then slope 1/ratio in dB. Makeup (1 / sat(1))^0.6 = +2.137 dB for the app's -14/18/4. Linked stereo
// detector (max |x| over channels), envelope rate recomputed every 32 frames, adaptive release polynomial, 2.5 ms
// detector release, sin(pi/2 gain) warp. The pre-delay (6 ms by default) is a parameter: the MasterBus uses 0 so the
// master adds no latency (decision in the task).
using System;

namespace MusicRoom.Audio.Dsp
{
    public sealed class DynamicsCompressor
    {
        public readonly double K, Makeup, LinThr, KneeThr, YKneeThrDb, KneeThrDb, Ratio;
        public readonly int PreDelayFrames;
        public readonly int Channels;

        private readonly double _attackFrames, _releaseFrames, _satReleaseFrames;
        private readonly double _kA, _kB, _kC, _kD, _kE;
        private readonly float[][] _delay;
        private const int Mask = 1023;
        private int _rd, _wr;
        private double _det, _gain = 1, _maxAtt = -1, _rate = 1, _sdg;
        private long _frame;

        public DynamicsCompressor(double sampleRate, double thresholdDb = -14, double kneeDb = 18, double ratio = 4,
            double attackSeconds = 0.003, double releaseSeconds = 0.18, double preDelaySeconds = 0.0, int channels = 2)
        {
            Channels = Math.Max(1, channels);
            Ratio = ratio;
            LinThr = Db2Lin(thresholdDb);
            K = KAtSlope(1.0 / ratio, thresholdDb, kneeDb);
            KneeThrDb = thresholdDb + kneeDb;
            KneeThr = Db2Lin(KneeThrDb);
            YKneeThrDb = Lin2Db(KneeCurve(KneeThr, K));
            Makeup = Math.Pow(1.0 / Saturate(1.0), 0.6);
            _attackFrames = Math.Max(0.001, attackSeconds) * sampleRate;
            _releaseFrames = releaseSeconds * sampleRate;
            _satReleaseFrames = 0.0025 * sampleRate;
            double y1 = _releaseFrames * 0.09, y2 = _releaseFrames * 0.16, y3 = _releaseFrames * 0.42, y4 = _releaseFrames * 0.98;
            _kA = 0.9999999999999998 * y1 + 1.8432219684323923e-16 * y2 - 1.9373394351676423e-16 * y3 + 8.824516011816245e-18 * y4;
            _kB = -1.5788320352845888 * y1 + 2.3305837032074286 * y2 - 0.9141194204840429 * y3 + 0.1623677525612032 * y4;
            _kC = 0.5334142869106424 * y1 - 1.272736789213631 * y2 + 0.9258856042207512 * y3 - 0.18656310191776226 * y4;
            _kD = 0.08783463138207234 * y1 - 0.1694162967925622 * y2 + 0.08588057951595272 * y3 - 0.00429891410546283 * y4;
            _kE = -0.042416883008123074 * y1 + 0.1115693827987602 * y2 - 0.09764676325265872 * y3 + 0.028494263462021576 * y4;
            PreDelayFrames = (int)Math.Floor(preDelaySeconds * sampleRate);
            _delay = new float[Channels][];
            for (int c = 0; c < Channels; c++) _delay[c] = new float[1024];
            _rd = 0;
            _wr = PreDelayFrames & Mask;
        }

        public double MakeupDb => Lin2Db(Makeup);
        /// <summary>Current gain in dB (reads a value the audio thread writes; diagnostic).</summary>
        public double GainDb => Lin2Db(_gain);

        public static double Db2Lin(double db) => Math.Pow(10, db / 20);
        public static double Lin2Db(double x) => 20 * Math.Log10(x);

        public double KneeCurve(double x, double k) => x < LinThr ? x : LinThr + (1 - Math.Exp(-k * (x - LinThr))) / k;

        /// <summary>The static output level for an input peak x (the "shaping curve", before makeup).</summary>
        public double Saturate(double x) => x < KneeThr ? KneeCurve(x, K) : Db2Lin(YKneeThrDb + (Lin2Db(x) - KneeThrDb) / Ratio);

        private double SlopeAt(double x, double k)
        {
            if (x < LinThr) return 1;
            double x2 = x * 1.001;
            return (Lin2Db(KneeCurve(x2, k)) - Lin2Db(KneeCurve(x, k))) / (Lin2Db(x2) - Lin2Db(x));
        }

        private double KAtSlope(double desired, double thresholdDb, double kneeDb)
        {
            double x = Db2Lin(thresholdDb + kneeDb);
            double minK = 0.1, maxK = 10000, k = 5;
            // KneeCurve uses LinThr, already set by the constructor before this call.
            for (int i = 0; i < 15; i++)
            {
                if (SlopeAt(x, k) < desired) maxK = k; else minK = k;
                k = Math.Sqrt(minK * maxK);
            }
            return k;
        }

        /// <summary>Peak-detector static curve: input peak (dBFS) -> gain (dB, including makeup), ignoring the detector's
        /// recovery between peaks. The measured static curve (spec 06) is the steady gain of the RUNNING kernel for a
        /// 1 kHz sine, which Process reproduces to 0.001 dB; this helper sits up to 0.08 dB lower at 0 dBFS. Diagnostic only.</summary>
        public double StaticGainDb(double inputPeakDb)
        {
            double x = Db2Lin(inputPeakDb);
            double att = x <= 0.0001 ? 1 : Saturate(x) / x;
            return Lin2Db(att * Makeup);
        }

        /// <summary>Audio thread: in place on interleaved data (channels as constructed). Allocation-free.</summary>
        public void Process(float[] data, int channels, int frames)
        {
            int ch = Math.Min(channels, Channels);
            for (int i = 0, j = 0; i < frames; i++, j += channels, _frame++)
            {
                if ((_frame & 31) == 0)
                {
                    if (double.IsNaN(_det) || double.IsInfinity(_det)) _det = 1;
                    _sdg = Math.Asin(_det) / (Math.PI / 2);
                    double diff = Lin2Db(_gain / _sdg);
                    if (_sdg > _gain)
                    {
                        _maxAtt = -1;
                        if (double.IsNaN(diff) || double.IsInfinity(diff)) diff = -1;
                        double x = Math.Min(0, Math.Max(-12, diff));
                        x = 0.25 * (x + 12);
                        _rate = Db2Lin(5 / (_kA + _kB * x + _kC * x * x + _kD * x * x * x + _kE * x * x * x * x));
                    }
                    else
                    {
                        if (double.IsNaN(diff) || double.IsInfinity(diff)) diff = 1;
                        if (_maxAtt == -1 || _maxAtt < diff) _maxAtt = diff;
                        _rate = 1 - Math.Pow(0.25 / Math.Max(0.5, _maxAtt), 1 / _attackFrames);
                    }
                }
                double inp = 0;
                for (int c = 0; c < ch; c++)
                {
                    float v = data[j + c];
                    _delay[c][_wr] = v;
                    double a = Math.Abs(v);
                    if (a > inp) inp = a;
                }
                double att = inp <= 0.0001 ? 1 : Saturate(inp) / inp;
                double satRate = Db2Lin(Math.Max(2, -Lin2Db(att)) / _satReleaseFrames) - 1;
                _det += (att - _det) * (att > _det ? satRate : 1);
                _det = Math.Min(1, _det);
                if (double.IsNaN(_det) || double.IsInfinity(_det)) _det = 1;
                if (_rate < 1) _gain += (_sdg - _gain) * _rate; else _gain = Math.Min(1, _gain * _rate);
                double total = Makeup * Math.Sin(Math.PI / 2 * _gain);
                for (int c = 0; c < ch; c++) data[j + c] = (float)(_delay[c][_rd] * total);
                _rd = (_rd + 1) & Mask;
                _wr = (_wr + 1) & Mask;
            }
        }

        /// <summary>The steady state for a quiet input (detector and gain at 1: no compression), as if the kernel had been
        /// running on silence. MasterBus uses it when today's chain takes over from the room unit's master mid-play: from
        /// Reset's state (detector 0) the kernel fades in over ~100 ms, which only a cold start can hide.</summary>
        public void Settle()
        {
            _det = 1; _gain = 1; _sdg = 1; _maxAtt = -1; _rate = 1;
        }

        public void Reset()
        {
            _det = 0; _gain = 1; _maxAtt = -1; _rate = 1; _sdg = 0; _frame = 0;
            for (int c = 0; c < Channels; c++) Array.Clear(_delay[c], 0, _delay[c].Length);
            _rd = 0; _wr = PreDelayFrames & Mask;
        }
    }
}
