// Voice allocation and stealing (spec 10-upgrade/burst.md section 5). Target: Assets/MusicRoom/Audio/Burst/VoiceAllocator.cs.
//
// Two mechanisms, chosen per sound:
//  1. KEYED (piano keys, harp strings, kit pieces, cymbals, soundboards): each physical object owns a modal bank or a
//     waveguide lane for the whole session (AudioGraph). A new strike adds energy into the same state, exactly like the
//     real instrument, so nothing is ever stolen; CPU is capped by the graph's LOD budget instead.
//  2. ONE-SHOT pools (hammer knocks, damper thuds, stick and wire noise, finger squeaks, baked-sample playback): a fixed
//     pool of N live slots plus G ghost slots. Policy when full:
//       a. a free slot;
//       b. else the victim with the lowest score among slots older than ProtectSamples (30 ms; when every slot is
//          younger, all slots are candidates): score = LevelDb - (Released ? ReleasedBonusDb : 0), ties -> older, then
//          lower index. (Old engine: oldest released, else oldest, cut dead = a click when the victim is loud.)
//       c. the victim's state is copied to a ghost slot that fades it out over StealFadeSamples (2.7 ms, inaudible as a
//          click); with no free ghost, the most-faded ghost is reused (it is already near silence).
// Pure struct + pointer code: called from Burst (AudioGraph) or managed, identical results.
using System.Runtime.InteropServices;

namespace MusicRoom.Audio.Burst
{
    public enum SlotState : byte { Free = 0, Active = 1, Released = 2, Ghost = 3 }

    [StructLayout(LayoutKind.Sequential)]
    public struct OneShotSlot
    {
        public int Id;            // engine voice id (0 = anonymous)
        public SlotState State;
        public byte Pad0;
        public short Pad1;
        public long Start;        // start sample
        public float LevelDb;     // current output level estimate (dBFS), updated by the voice once per block
        public float Fade;        // ghost: gain 1 -> 0
        public float FadeStep;    // ghost: -1 / StealFadeSamples
        public int Pad2;
    }

    public static unsafe class VoiceAllocator
    {
        public const int StealFadeSamples = 128;     // 2.67 ms at 48 kHz
        public const int ProtectSamples = 1440;      // 30 ms
        public const float ReleasedBonusDb = 20f;

        /// <summary>Returns the slot for a new voice in [0, count). ghost = the slot the stolen victim moved to (in
        /// [count, count + ghosts)) or -1; the caller copies the victim's DSP payload slot -> ghost before reusing it.</summary>
        public static int Allocate(OneShotSlot* s, int count, int ghosts, long now, out int ghost)
        {
            ghost = -1;
            for (int i = 0; i < count; i++) if (s[i].State == SlotState.Free) return i;

            bool anyOld = false;
            for (int i = 0; i < count; i++) if (now - s[i].Start >= ProtectSamples) { anyOld = true; break; }
            int best = -1; float bestScore = float.MaxValue; long bestStart = long.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (anyOld && now - s[i].Start < ProtectSamples) continue;
                float score = s[i].LevelDb - (s[i].State == SlotState.Released ? ReleasedBonusDb : 0f);
                if (score < bestScore || (score == bestScore && s[i].Start < bestStart))
                {
                    best = i; bestScore = score; bestStart = s[i].Start;
                }
            }
            if (best < 0) return -1;

            int gsel = -1; float minFade = float.MaxValue;
            for (int k = count; k < count + ghosts; k++)
            {
                if (s[k].State == SlotState.Free) { gsel = k; break; }
                if (s[k].Fade < minFade) { minFade = s[k].Fade; gsel = k; }
            }
            if (gsel >= 0)
            {
                s[gsel] = s[best];
                s[gsel].State = SlotState.Ghost;
                s[gsel].Fade = 1f;
                s[gsel].FadeStep = -1f / StealFadeSamples;
                ghost = gsel;
            }
            s[best] = default;
            return best;
        }

        /// <summary>Per block, for a ghost slot: render the victim's samples [0, samples) and mix them with the ramp
        /// (g0, dg) (DspKernels.MixRamp); the slot is freed once the ramp reaches 0.</summary>
        public static void GhostRamp(OneShotSlot* s, int frames, out float g0, out float dg, out int samples)
        {
            g0 = s->Fade; dg = s->FadeStep;
            int left = dg < 0f ? (int)System.Math.Ceiling(s->Fade / -dg) : frames;
            samples = left < frames ? left : frames;
            float end = s->Fade + dg * samples;
            if (samples < frames || end <= 0f) { s->State = SlotState.Free; s->Fade = 0f; }
            else s->Fade = end;
        }
    }
}
