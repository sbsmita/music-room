// Noise: xorshift32 per voice (fresh random grain), the shared 2 s noise table that EVERY noise voice
// reads from index 0 (spec 06), and the seeded builder tests use (06 fingerprint noiseBuffer_seed1).
using System;
using MusicRoom.Core;

namespace MusicRoom.Audio.Dsp
{
    /// <summary>xorshift32: 3 shifts + 3 xors per sample, no allocation, deterministic per seed.</summary>
    public struct XorShift32
    {
        private uint _state;

        public XorShift32(uint seed) { _state = seed == 0 ? 0x9E3779B9u : seed; }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform in [-1, 1).</summary>
        public float NextBipolar() => (int)NextUInt() * (1f / 2147483648f);

        /// <summary>Uniform in [0, 1).</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);
    }

    /// <summary>The synths' shared white noise: 2 * sr samples of (float)(R() * 2 - 1). Build once on the main thread.</summary>
    public static class NoiseTable
    {
        public static float[] Samples { get; private set; }

        /// <summary>Session build (random grain from a fresh seed).</summary>
        public static void Build(int sampleRate, uint seed = 0)
        {
            BuildSeeded(sampleRate, seed == 0 ? Mulberry32.SessionSeed() : seed);
        }

        /// <summary>Deterministic build with mulberry32: seed 1 reproduces the 06 fingerprint (n 96000, peak 0.99998194).</summary>
        public static void BuildSeeded(int sampleRate, uint seed)
        {
            Samples = Generate(sampleRate, seed);
        }

        public static float[] Generate(int sampleRate, uint seed)
        {
            int n = sampleRate * 2;
            var data = new float[n];
            var rng = new Mulberry32(seed);
            for (int i = 0; i < n; i++) data[i] = (float)(rng.Next() * 2 - 1);
            return data;
        }
    }

    /// <summary>One noise reader: loops the shared table from index 0 (reference parity) or from an offset.</summary>
    public struct NoiseReader
    {
        private int _pos;

        public void Start(int offset = 0) { _pos = offset; }

        /// <summary>The index the next Next reads.</summary>
        public int Position => _pos;

        public float Next(float[] table)
        {
            float v = table[_pos];
            if (++_pos >= table.Length) _pos = 0;
            return v;
        }

        /// <summary>Advance by n samples of a table of the given length (as n calls of Next, without reading).</summary>
        public void Skip(int n, int length) { _pos = (int)((_pos + (long)n) % length); }
    }
}
