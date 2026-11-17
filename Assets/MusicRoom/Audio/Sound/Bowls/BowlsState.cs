// The singing bowls' unmanaged state (bowls.md 3.5): three BowlState blocks plus the shared tables and the render's own
// scratch, one zeroed 64-byte aligned allocation (the unit's Alloc, or the test rig's). Layout of the modal groups is the
// reference's G (8 float4 per group: ZR, ZI, PR, PI, GOI, GOR, GI, OM), so a group reads as eight float4 loads.
// Fields written by the audio thread at block end and read by the main thread (stats) are marked "stat".
using System.Runtime.InteropServices;

namespace MusicRoom.Audio.Burst
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BowlState
    {
        public const int STRIDE = 32, ZR = 0, ZI = 4, PR = 8, PI = 12, GOI = 16, GOR = 20, GI = 24, OM = 28;
        public const int F4ZR = 0, F4ZI = 1, F4PR = 2, F4PI = 3, F4GOI = 4, F4GOR = 5, F4GI = 6, F4OM = 7;

        /// <summary>4 groups x 8 float4 (the modes: mode m in group m >> 2, lane m &amp; 3; pair p = m >> 1, A even / B odd).</summary>
        public fixed float G[BowlsConst.NG * STRIDE];
        /// <summary>The 8 pole tables: (L * 16 + m) * 2 + {0 pr, 1 pi}, L = damp level + 4 (finger on the rim).</summary>
        public fixed float Poles[BowlsConst.NPOLESET * BowlsConst.NM * 2];
        public fixed float Rad[BowlsConst.NM];
        public fixed float Oc[BowlsConst.NP], Os[BowlsConst.NP], InvN[BowlsConst.NP], Hx[BowlsConst.NP];
        public float Y0t, T;
        public int Index, Level, PoleSet;
        // pickup (listener azimuth in the bowl frame) as a phasor; goI / goR are rebuilt when it moves
        public float Pc, Ps, Ptc, Pts;
        public int PickDirty;
        // contact: one stick per bowl, owned by one lane (0 live, 1..3 playback)
        public int On, Kind, Init, Rel, Lane;
        public float N, Ntar, DN;
        public int NN;
        public float Vf, VfTar, DVf;
        public int NVf;
        public float Ec, Es, Tc, Ts, Trc, Trs, Omega, Hs;
        public float XP, VP, Fc;
        /// <summary>The soft pad on an outer contact (BowlsDesign.SetPad; 0 = the reference's bare stick): the force the bowl
        /// receives is the stick's dynamic force blended toward N by PadMix and low-passed by PadK (one-pole), PadF its state.</summary>
        public float PadK, PadMix, PadF;
        /// <summary>The pad's pressing force (N): below it the stick is held on at PadN and the bowl feels the forces scaled
        /// by N / PadN, so a light touch, a contact's swell-in and its release never let the stick bounce.</summary>
        public float PadN;
        /// <summary>The tonal rub noise (BowlsDesign.SetRubNoise; NoiseRel 0 = the reference's free-running rub noise): the noise
        /// gain is NOISE_K x NoiseScale x the friction power, capped at NoiseRel x the bowl's own tone envelope (ToneEnv, a
        /// one-pole of |modal output| with coefficient ToneK), so the friction hiss always sits far under the sing.</summary>
        public float NoiseScale, NoiseRel, ToneK, ToneEnv;
        // strike pulses: 4 slots (two overlapping strikes of two components)
        public fixed int POn[BowlsConst.NPULSE], PK[BowlsConst.NPULSE], PLen[BowlsConst.NPULSE];
        public fixed float PAmp[BowlsConst.NPULSE], PRc[BowlsConst.NPULSE], PRs[BowlsConst.NPULSE], PSc[BowlsConst.NPULSE], PSs[BowlsConst.NPULSE];
        /// <summary>Per slot, the 16 mode weights gi phi_r(theta_s) s^n (slot k at k * 16).</summary>
        public fixed float PW[BowlsConst.NPULSE * BowlsConst.NM];
        public fixed double PStart[BowlsConst.NPULSE];
        public int PActive;
        // rub noise (xorshift32 -> band-pass -> low-pass) and the output high-pass (TDF-II states)
        public uint Nz;
        public fixed float Nb[5], Lb[5], Hb[5];
        public float Nz1, Nz2, Lz1, Lz2, Hz1, Hz2;
        public int Awake;
        public float NEnv, NEnvK;
        // the work cap: groups rendered (Keep), groups fading out this block [Keep, FadeHi)
        public int Keep, FadeHi, Frames;
        public float InvFrames;
        public int Work;
        // ---- stats (audio thread at block end; main thread reads with Volatile.Read) ----
        public float StEnergy, StRms, StPeak, StSingVel, StP3, StPump, StFc, StU;
        public long StContactSamples, StLostSamples, StStickSamples, StBlocks;
        public int StBlockContact, StBlockLost, StPreempts, StLaneDrops, StLane, StWork;
        public double StPumpAcc;
        public int StPumpN;
        public fixed float StPairAmp[BowlsConst.NP];
        public fixed float StP2[4];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BowlsState
    {
        public BowlState B0, B1, B2;
        /// <summary>cos / sin (pi / K) for the half-sine strike pulses, K = 2..600 (main thread, double maths).</summary>
        public fixed float PulseCos[BowlsConst.PulseKMax + 1], PulseSin[BowlsConst.PulseKMax + 1];
        /// <summary>The listener slew rotation cos / sin (0.005 rad).</summary>
        public float PickRc, PickRs;
        public int SampleRate;
        /// <summary>1 once BowlsDesign.Build filled the tables (main thread publishes it last).</summary>
        public int Ready;
        /// <summary>Phase B: the unit's own Strict Burst render (FunctionPointer value), 0 = render inline.</summary>
        public System.IntPtr StrictFn;
        /// <summary>stat: unit work of the last block (mode-samples + 24 per contact sample), and its block counter.</summary>
        public int Work;
        public int Resets, NotReady;
        // ---- render scratch (audio thread only) ----
        /// <summary>The free path's lane accumulators (float4 per frame).</summary>
        public fixed float Acc[4 * BowlsConst.MaxFrames];
        /// <summary>Each bowl's block before the sum (bowl b at b * MaxFrames).</summary>
        public fixed float Y[BowlsConst.NBOWLS * BowlsConst.MaxFrames];

        /// <summary>Bowl i's block (the three are contiguous).</summary>
        public static BowlState* Bowl(BowlsState* s, int i) => &s->B0 + i;
    }
}
