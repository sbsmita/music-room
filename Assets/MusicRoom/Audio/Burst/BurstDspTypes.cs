// Burst DSP data layouts (spec 10-upgrade/burst.md section 4). Target: Assets/MusicRoom/Audio/Burst/BurstDspTypes.cs.
// Every struct is blittable and made of float4 / int4 / uint4 members (16-byte aligned when the arena is), laid out
// exactly like the Node reference (Tools/SoundLab/burst/lab/kernels.mjs: MB, WG, BQ), so the test vectors carry over 1:1.
// No UnityEngine dependency: these files also compile and run in a plain .NET harness.
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    /// <summary>MB4: four damped complex resonators, one per lane (96 bytes).
    /// z &lt;- p z + gi x (driven) or z &lt;- p z (free); y = go * Im(z); p = r e^{jw}, r = exp(-1/(tau sr)), w = 2 pi f / sr.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ModalGroup
    {
        public float4 Zr, Zi;   // state
        public float4 Pr, Pi;   // pole (r cos w, r sin w)
        public float4 Go;       // output gain
        public float4 Gi;       // input gain (driven kernels only)
    }

    /// <summary>WG4: four waveguide strings, one per lane (144 bytes). Lines live elsewhere: lane l uses
    /// lines[l * Len .. (l + 1) * Len). Loop: x = line[w - L] -> allpass (C) -> one-pole loss (B0, A1) -> + exc -> line[w].</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WaveguideGroup
    {
        public int4 L;          // integer loop delay per lane (>= 2)
        public float4 C;        // first-order allpass coefficient (fractional delay d in [0.5, 1.5): c = (1 - d) / (1 + d))
        public float4 X1, Y1;   // allpass state
        public float4 B0, A1;   // loss filter s = b0 y + a1 s1
        public float4 S1;       // loss state
        public float4 Go;       // bridge pickup gain
        public int W;           // write index, shared by the lanes
        public int Mask;        // Len - 1
        public int Len;         // power of two, >= max(L) + 2
        public int Pad;
    }

    /// <summary>BQ4: four TDF-II biquads, one per lane (112 bytes). NA1 = -a1, NA2 = -a2 (normalized, a0 = 1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Biquad4
    {
        public float4 B0, B1, B2, NA1, NA2, Z1, Z2;
    }

    /// <summary>NZ4: four xorshift32 lanes (13/17/5). Seed lanes with SplitMix32 (never 0).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Noise4
    {
        public uint4 State;
    }

    /// <summary>EXP4: per-lane exponential gain, y = x g then g *= m.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Exp4
    {
        public float4 G, M;
    }

    /// <summary>Seeding (main thread or audio thread; pure uint math, identical to lab/common.mjs splitmix32).</summary>
    public struct SplitMix32
    {
        public uint S;
        public SplitMix32(uint seed) { S = seed; }
        public uint Next()
        {
            S += 0x9e3779b9u;
            uint z = S;
            z = (z ^ (z >> 16)) * 0x85ebca6bu;
            z = (z ^ (z >> 13)) * 0xc2b2ae35u;
            return z ^ (z >> 16);
        }
        public static Noise4 SeedNoise(uint seed)
        {
            var s = new SplitMix32(seed);
            uint4 st = default;
            for (int l = 0; l < 4; l++) { uint v = s.Next(); st[l] = v == 0 ? 0x9e3779b9u : v; }
            return new Noise4 { State = st };
        }
    }
}
