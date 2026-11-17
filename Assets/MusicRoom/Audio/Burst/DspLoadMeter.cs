// DSP load meter (spec 10-upgrade/burst.md section 13). Audio thread: Record() once per rendered block (no allocation,
// no lock). Main thread: Read() returns the published values, ResetPeaks() starts a new window (the adaptive budget does
// this once a second). Load = render time / block period. Also counts dspTime gaps (a block that did not follow the
// previous one: the mixer skipped or restarted, the only underrun-like signal visible from inside Unity).
using System.Diagnostics;
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    public sealed class DspLoadMeter
    {
        public struct Snapshot
        {
            /// <summary>Mean, worst and 99th percentile render time per block in the current window (since ResetPeaks).</summary>
            public float AvgMicros, MaxMicros, P99Micros;
            /// <summary>The block period and the window's mean and worst load (render time / period).</summary>
            public float BlockMicros, AvgLoad, MaxLoad;
            /// <summary>Worst block since start (never reset).</summary>
            public float PeakMicros;
            public long Blocks;
            public int Gaps, OverBudget;
        }

        static readonly double TicksToMicros = 1e6 / Stopwatch.Frequency;
        public const int Bins = 128;                 // 50 us bins up to 6.4 ms
        public const float BinMicros = 50f;
        /// <summary>Blocks between publications (~0.34 s at 256 frames, 48 kHz).</summary>
        public const int PublishEvery = 64;

        /// <summary>Per-block budget for "over budget" counting and the adaptive budget: the audio side's one budget
        /// (AudioModule.BlockBudgetFor: 1.5 ms per 256 frames, in proportion to the block length last recorded).</summary>
        public float BudgetMicros => AudioModule.BlockBudgetFor(Volatile.Read(ref _lastFrames));

        readonly int[] _hist = new int[Bins];  // allocated once on the main thread (constructor)
        double _sum; long _n, _total; float _max, _peak; long _lastStart = long.MinValue; int _lastFrames;
        int _gaps, _over, _reset;
        // published (audio -> main)
        float _pAvg, _pMax, _pP99, _pBlock, _pPeak; long _pBlocks; int _pGaps, _pOver;

        /// <summary>Audio thread.</summary>
        public void Record(long ticks, int frames, int sampleRate, long blockStart)
        {
            if (Volatile.Read(ref _reset) != 0)
            {
                Volatile.Write(ref _reset, 0);
                for (int i = 0; i < Bins; i++) _hist[i] = 0;
                _sum = 0; _n = 0; _max = 0;
            }
            float us = (float)(ticks * TicksToMicros);
            if (_lastStart != long.MinValue && blockStart != _lastStart + _lastFrames) _gaps++;
            _lastStart = blockStart; _lastFrames = frames;
            _sum += us; _n++; _total++;
            if (us > _max) _max = us;
            if (us > _peak) _peak = us;
            int b = (int)(us / BinMicros); _hist[b < Bins ? b : Bins - 1]++;
            if (us > AudioModule.BlockBudgetFor(frames)) _over++;
            if (_total % PublishEvery == 0 || _total == 1)
            {
                long target = (long)System.Math.Ceiling(_n * 0.99), acc = 0; int p = Bins - 1;
                for (int i = 0; i < Bins; i++) { acc += _hist[i]; if (acc >= target) { p = i; break; } }
                Volatile.Write(ref _pAvg, (float)(_sum / _n));
                Volatile.Write(ref _pMax, _max);
                Volatile.Write(ref _pP99, System.Math.Min((p + 1) * BinMicros, _max));
                Volatile.Write(ref _pBlock, sampleRate > 0 ? 1e6f * frames / sampleRate : 0f);
                Volatile.Write(ref _pPeak, _peak);
                Volatile.Write(ref _pGaps, _gaps);
                Volatile.Write(ref _pOver, _over);
                Volatile.Write(ref _pBlocks, _total);
            }
        }

        /// <summary>Main thread: start a new window (taken by the audio thread at its next block).</summary>
        public void ResetPeaks() => Volatile.Write(ref _reset, 1);

        /// <summary>Main thread.</summary>
        public Snapshot Read()
        {
            var s = new Snapshot
            {
                AvgMicros = Volatile.Read(ref _pAvg), MaxMicros = Volatile.Read(ref _pMax), P99Micros = Volatile.Read(ref _pP99),
                BlockMicros = Volatile.Read(ref _pBlock), PeakMicros = Volatile.Read(ref _pPeak), Blocks = Volatile.Read(ref _pBlocks),
                Gaps = Volatile.Read(ref _pGaps), OverBudget = Volatile.Read(ref _pOver),
            };
            if (s.BlockMicros > 0) { s.AvgLoad = s.AvgMicros / s.BlockMicros; s.MaxLoad = s.MaxMicros / s.BlockMicros; }
            return s;
        }
    }
}
