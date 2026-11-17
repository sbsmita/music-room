// The app's seeded PRNG (the string synth's rng, the snow flakes): mulberry32. Seed 1 -> 0.6270739405881613,
// 0.002735721180215478, 0.5274470399599522, ... (Docs/06-audio-dsp.md, mulberry32 seed 1, first five).
// Used wherever a test needs bit-exact randomness; sessions use a random (unseeded) seed.
namespace MusicRoom.Core
{
    public struct Mulberry32
    {
        uint _a;

        public Mulberry32(uint seed) { _a = seed; }

        /// <summary>Uniform in [0, 1) as a double, exactly the JS result.</summary>
        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public float NextFloat() => (float)Next();

        /// <summary>Uniform in [a, b).</summary>
        public double Range(double a, double b) => a + (b - a) * Next();

        /// <summary>A session seed (unseeded: different every session).</summary>
        public static uint SessionSeed() => (uint)System.Environment.TickCount ^ (uint)System.DateTime.UtcNow.Ticks;

        /// <summary>JS Math.round: floor(x + 0.5), not banker's rounding.</summary>
        public static int JsRound(double x) => (int)System.Math.Floor(x + 0.5);
    }
}
