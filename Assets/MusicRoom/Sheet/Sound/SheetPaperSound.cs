// The page-turn sound (sheet.md 5.5, the lab's page-sound.mjs): only the engine's one-shot pool, four presets of four noise
// lanes (NZ4 -> BQ4 -> EXP4 -> mix), started sample-accurately by a seeded schedule. Paper, not a whoosh: no lane above
// Q 2.4, no tau over 200 ms, the low lanes lowpassed and quiet, no reverb of its own (the room adds it).
//   Turn(T, level, seed): lift 0.55 at 0; 3-6 crinkles (0.18-0.43) in [0.015, 0.4 T]; air 0.32 / 0.55 / 0.45 (+-15 %) at
//   0.10 T / 0.24 T / 0.40 T; land 0.6 (+-10 %) at T. A riffle: leaves 0.085 s apart, each 5 % quieter.
// Phase A has no one-shot pool in the engine (ISheetHost.PaperTurn only counts); phase B registers Presets with
// IAudioEngine.AddOneShotPreset and plays Schedule's shots with PlayOneShot on PianoMid at gain 0.25 (device item S4).
using MusicRoom.Core;

namespace MusicRoom.Sheet
{
    public static class SheetPaperSound
    {
        public enum Lane : byte { BandPass = 0, LowPass, HighPass }
        public struct LaneSpec { public Lane Type; public float Hz, Q, Tau, Mix; public LaneSpec(Lane t, float hz, float q, float tau, float mix) { Type = t; Hz = hz; Q = q; Tau = tau; Mix = mix; } }
        public struct PresetSpec { public string Name; public float Seconds, LevelDb; public LaneSpec[] Lanes; }

        public const int Lift = 0, Air = 1, Crinkle = 2, Land = 3;
        public const float SourceGain = 0.25f;

        public static readonly PresetSpec[] Presets =
        {
            new PresetSpec { Name = "lift", Seconds = 0.10f, LevelDb = -30, Lanes = new[] { new LaneSpec(Lane.BandPass, 2600, 0.9f, 0.018f, 0.55f), new LaneSpec(Lane.BandPass, 5200, 1.2f, 0.011f, 0.35f), new LaneSpec(Lane.LowPass, 650, 0.7f, 0.028f, 0.30f), new LaneSpec(Lane.HighPass, 9000, 0.7f, 0.005f, 0.12f) } },
            new PresetSpec { Name = "air", Seconds = 0.55f, LevelDb = -34, Lanes = new[] { new LaneSpec(Lane.BandPass, 650, 0.6f, 0.15f, 0.50f), new LaneSpec(Lane.BandPass, 1500, 0.7f, 0.11f, 0.40f), new LaneSpec(Lane.BandPass, 3300, 0.8f, 0.07f, 0.20f), new LaneSpec(Lane.LowPass, 260, 0.7f, 0.20f, 0.18f) } },
            new PresetSpec { Name = "crinkle", Seconds = 0.03f, LevelDb = -36, Lanes = new[] { new LaneSpec(Lane.BandPass, 4300, 2.4f, 0.004f, 0.60f), new LaneSpec(Lane.BandPass, 2300, 2.0f, 0.006f, 0.40f), new LaneSpec(Lane.BandPass, 7600, 2.0f, 0.003f, 0.25f), new LaneSpec(Lane.HighPass, 1200, 0.7f, 0.002f, 0.15f) } },
            new PresetSpec { Name = "land", Seconds = 0.14f, LevelDb = -30, Lanes = new[] { new LaneSpec(Lane.LowPass, 380, 0.8f, 0.032f, 0.75f), new LaneSpec(Lane.BandPass, 1100, 1.0f, 0.018f, 0.35f), new LaneSpec(Lane.BandPass, 2800, 1.2f, 0.009f, 0.18f), new LaneSpec(Lane.HighPass, 6000, 0.7f, 0.004f, 0.08f) } },
        };

        static readonly double[] AirAt = { 0.10, 0.24, 0.40 }, AirGain = { 0.32, 0.55, 0.45 };
        public struct Shot { public int Preset; public float Gain; public double At; public uint Seed; }
        public const int MaxShotsPerTurn = 1 + 6 + 3 + 1;

        /// <summary>One leaf over `seconds` from time `at` (s): writes its shots (page-sound.mjs turn) and returns the count.</summary>
        public static int Turn(Shot[] into, int offset, double at, float seconds, float level, uint seed)
        {
            var r = new Mulberry32(seed);
            int n0 = offset;
            into[offset++] = new Shot { Preset = Lift, Gain = 0.55f * level, At = at, Seed = seed };
            int n = 3 + (int)System.Math.Floor(r.Next() * 4);
            for (int i = 0; i < n; i++)
            {
                double g = (0.18 + 0.25 * r.Next()) * level;
                double t = 0.015 + r.Next() * 0.38 * seconds;
                into[offset++] = new Shot { Preset = Crinkle, Gain = (float)g, At = at + t, Seed = seed + 17u * (uint)i };
            }
            for (int i = 0; i < 3; i++)
                into[offset++] = new Shot { Preset = Air, Gain = (float)(AirGain[i] * level * (0.85 + 0.3 * r.Next())), At = at + AirAt[i] * seconds, Seed = seed + 101u + (uint)i };
            into[offset++] = new Shot { Preset = Land, Gain = (float)(0.6 * level * (0.9 + 0.2 * r.Next())), At = at + seconds, Seed = seed + 211u };
            return offset - n0;
        }

        /// <summary>The level of riffle leaf k (each 5 % quieter, from 0.55).</summary>
        public static float RiffleLevel(int k) => 0.55f - 0.05f * k;
    }
}
