// The handpan unit's unmanaged state (handpan.md 5.3): one block of memory, 64-byte aligned, laid out once on the main
// thread (HandpanStateBuilder) and filled from a voice table (HandpanTables.g.cs). The audio thread reads and writes it
// through raw pointers only. Node names (handpan-engine.mjs createHandpan) in brackets.
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    /// <summary>One contact pulse slot [on, n, W1, W2, A1, A2, c1, s1, c2, s2, C1, S1, C2, S2, Fprev] (handpan.md 2.3).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HandpanPulse
    {
        public int On, N, W1, W2;
        public float A1, A2;
        /// <summary>The running rotations (cos, sin) of both raised cosines.</summary>
        public float c1, s1, c2, s2;
        /// <summary>The per-sample rotation constants cos/sin(2 pi / W).</summary>
        public float C1, S1, C2, S2;
        public float Fp;
        public float U, V;     // pending hits only: the strike point
        public int Tech;       // pending hits only
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HandpanState
    {
        public const int NF = 9, Tak = 9, GPF = 5, NG = 54, NLanes = 216, ShellG0 = 45, NShell = 8, CavG = 53;
        public const int Slots = 4, Modes = 12, TakSlots = 4;
        public const byte OpStrike = 1, OpDamp = 2, OpReset = 3, OpSetBudget = 4, OpReleaseAll = 5;

        // ---- the bank and its design (Reset restores Bank from Design)
        public ModalGroup* Bank;        // [bank] 54 groups
        public ModalGroup* Design;      // the voice table's bank: undamped poles, go, base gi, zero state
        public float* P0r, P0i;         // [P0] undamped poles per lane (216)
        public float* BaseGi;           // [baseGi] per lane (216)
        public HandpanPulse* Pulse;     // [pulse] 9
        public HandpanPulse* Pend;      // [pend] 9 (pending hits: U, V, Tech used)
        public HandpanPulse* TakP;      // [tak] 4 shell-only pulses

        // ---- scratch (per block), MaxFrames each
        public int MaxFrames;
        public float* Exc;              // 9 x MaxFrames
        public float* Tuned;            // 9 x MaxFrames
        public float* Y0, Bus, Xs, ShellX, CavX, TakX, Xnl;
        public float4* Acc;
        /// <summary>Burst path only: every field's sympathetic drive (9 x MaxFrames) and a silent input (free groups), so the
        /// groups that only feed the output render four at a time (HandpanDsp's batch).</summary>
        public float* XsAll, Zero;
        public fixed int BatchG[48];
        public fixed int BatchX[48];
        public int BatchN;

        // ---- per field / per lane flags and values
        public fixed byte Active[NG];
        public fixed byte ExcOn[NF];
        public fixed byte PendOn[NF];
        public fixed byte GlideOn[NF];
        public fixed float ShellAmp[NF], CavRes[NF], NlPrev[NF], U2[NF];
        public fixed float DSlot[NF * Slots * Modes];
        public fixed float DCur[NF * Modes];
        public fixed float DGlobal[Slots];
        /// <summary>Per field: sqrt of the T1..T3 energy at the last block end (audio -&gt; main, Volatile.Read).</summary>
        public fixed float Level[NF];

        // ---- per-voice design values the audio thread needs
        public fixed float URef[NF];
        public fixed float GlideW[NF];
        public fixed int UpperM[NF * 9];
        public fixed float UpperCosB[NF * 9];
        public fixed float UpperSinB[NF * 9];
        public fixed byte Ding[NF];

        // ---- control
        /// <summary>Op 4 (tests): a fixed LOD budget; 0 = the graph's (u-&gt;Graph-&gt;Budget x Share).</summary>
        public float BudgetOverride;
        /// <summary>The budget the last block used.</summary>
        public float Budget;
        /// <summary>1: run the reference's two-rounding kernels (managed calls only: bit-exact with Node). 0: DspKernels.</summary>
        public int Exact;
        public int Ready;
        public int Voice;

        // ---- stats (audio thread writes; main thread reads with Volatile.Read)
        public long Blocks, GroupsRendered, LaneSamples, Events, Merged, Retired, Subnormals, Resets;
        public int MaxGroups, ActiveGroups;
        /// <summary>Lane-samples of the last block (the platform's UnitSlot.Work).</summary>
        public int Work;
    }

    /// <summary>Main-thread layout and fill of HandpanState (no allocation after construction).</summary>
    public static unsafe class HandpanStateBuilder
    {
        static long Align(long x) => (x + 63) & ~63L;

        /// <summary>Bytes for one state with scratch for blocks of up to maxFrames.</summary>
        public static long Bytes(int maxFrames)
        {
            long b = Align(sizeof(HandpanState));
            b += 2 * Align(sizeof(ModalGroup) * HandpanState.NG);
            b += 3 * Align(4L * HandpanState.NLanes);
            b += 2 * Align(sizeof(HandpanPulse) * HandpanState.NF) + Align(sizeof(HandpanPulse) * HandpanState.TakSlots);
            b += 2 * Align(4L * HandpanState.NF * maxFrames);
            b += 7 * Align(4L * maxFrames);
            b += Align(16L * maxFrames);
            b += Align(4L * HandpanState.NF * maxFrames) + Align(4L * maxFrames);
            return b;
        }

        static byte* Take(byte* mem, ref long o, long bytes) { byte* p = mem + o; o += Align(bytes); return p; }

        /// <summary>Lays a zeroed block out (mem: Bytes(maxFrames) bytes, zeroed, 64-byte aligned).</summary>
        public static HandpanState* Layout(byte* mem, int maxFrames)
        {
            var s = (HandpanState*)mem;
            long o = Align(sizeof(HandpanState));
            s->Bank = (ModalGroup*)Take(mem, ref o, sizeof(ModalGroup) * HandpanState.NG);
            s->Design = (ModalGroup*)Take(mem, ref o, sizeof(ModalGroup) * HandpanState.NG);
            s->P0r = (float*)Take(mem, ref o, 4L * HandpanState.NLanes);
            s->P0i = (float*)Take(mem, ref o, 4L * HandpanState.NLanes);
            s->BaseGi = (float*)Take(mem, ref o, 4L * HandpanState.NLanes);
            s->Pulse = (HandpanPulse*)Take(mem, ref o, sizeof(HandpanPulse) * HandpanState.NF);
            s->Pend = (HandpanPulse*)Take(mem, ref o, sizeof(HandpanPulse) * HandpanState.NF);
            s->TakP = (HandpanPulse*)Take(mem, ref o, sizeof(HandpanPulse) * HandpanState.TakSlots);
            s->MaxFrames = maxFrames;
            s->Exc = (float*)Take(mem, ref o, 4L * HandpanState.NF * maxFrames);
            s->Tuned = (float*)Take(mem, ref o, 4L * HandpanState.NF * maxFrames);
            s->Y0 = (float*)Take(mem, ref o, 4L * maxFrames);
            s->Bus = (float*)Take(mem, ref o, 4L * maxFrames);
            s->Xs = (float*)Take(mem, ref o, 4L * maxFrames);
            s->ShellX = (float*)Take(mem, ref o, 4L * maxFrames);
            s->CavX = (float*)Take(mem, ref o, 4L * maxFrames);
            s->TakX = (float*)Take(mem, ref o, 4L * maxFrames);
            s->Xnl = (float*)Take(mem, ref o, 4L * maxFrames);
            s->Acc = (float4*)Take(mem, ref o, 16L * maxFrames);
            s->XsAll = (float*)Take(mem, ref o, 4L * HandpanState.NF * maxFrames);
            s->Zero = (float*)Take(mem, ref o, 4L * maxFrames);
            if (o > Bytes(maxFrames)) throw new InvalidOperationException("handpan state layout overflow");
            return s;
        }

        /// <summary>Writes a voice's table into the state (main thread, before the first block or while the unit is idle).</summary>
        public static void Fill(HandpanState* s, HandpanVoice voice)
        {
            float[] pr = HandpanTables.PoleRe(voice), pi = HandpanTables.PoleIm(voice), go = HandpanTables.Go(voice), gi = HandpanTables.Gi(voice);
            float[] uref = HandpanTables.URef(voice), glide = HandpanTables.GlideW(voice);
            for (int g = 0; g < HandpanState.NG; g++)
            {
                ModalGroup m = default;
                for (int l = 0; l < 4; l++)
                {
                    int k = 4 * g + l;
                    m.Pr[l] = pr[k]; m.Pi[l] = pi[k]; m.Go[l] = go[k]; m.Gi[l] = gi[k];
                    s->P0r[k] = pr[k]; s->P0i[k] = pi[k]; s->BaseGi[k] = gi[k];
                }
                s->Design[g] = m;
            }
            for (int i = 0; i < HandpanState.NF; i++)
            {
                s->URef[i] = uref[i];
                s->GlideW[i] = glide[i];
                s->Ding[i] = (byte)(i == 0 ? 1 : 0);
                for (int k = 0; k < 9; k++)
                {
                    s->UpperM[i * 9 + k] = HandpanTables.UpperM[i * 9 + k];
                    s->UpperCosB[i * 9 + k] = HandpanTables.UpperCosB[i * 9 + k];
                    s->UpperSinB[i * 9 + k] = HandpanTables.UpperSinB[i * 9 + k];
                }
            }
            s->Voice = (int)voice;
            HandpanDsp.Reset(s);
        }
    }
}
