// The test-vector scenarios of the Burst lab (vectors.mjs), in C# (spec 10-upgrade/burst.md section 9).
// In Assets/MusicRoom/Tests/EditMode/Audio/Burst/. No NUnit, no UnityEngine: the same file runs in the
// Burst lab's .NET harness, which proves the managed path bit-exact against Node before Unity sees it.
using System;
using System.Runtime.InteropServices;
using MusicRoom.Audio.Burst;
using Unity.Mathematics;

namespace MusicRoom.Tests.Audio
{
    public static unsafe class BurstScenarios
    {
        public const int SR = 48000, F = 256;

        public static uint Fnv1a32(float[] a)
        {
            uint h = 0x811c9dc5;
            for (int i = 0; i < a.Length; i++)
            {
                uint w = (uint)BitConverter.SingleToInt32Bits(a[i]);
                for (int k = 0; k < 4; k++) { h ^= w & 0xff; h *= 0x01000193; w >>= 8; }
            }
            return h;
        }

        static float F32(double v) => (float)v;

        sealed class Mem : IDisposable
        {
            public readonly IntPtr P;
            public Mem(long bytes) { P = Marshal.AllocHGlobal((IntPtr)(bytes + 64)); new Span<byte>((void*)P, (int)(bytes + 64)).Clear(); }
            public byte* Aligned => (byte*)(((long)P + 63) & ~63L);
            public void Dispose() => Marshal.FreeHGlobal(P);
        }

        // ---- V1: MB4 free, 12 modes, struck with phases
        public static float[] Mb4Free()
        {
            using var m = new Mem(3 * sizeof(ModalGroup) + 16 * F + 4 * F);
            var bank = (ModalGroup*)m.Aligned; var acc = (float4*)(m.Aligned + 3 * sizeof(ModalGroup)); var outp = (float*)(acc + F);
            for (int i = 0; i < 12; i++)
            {
                float hz = F32(110 * (i + 1) * Math.Sqrt(1 + 0.0004 * (i + 1) * (i + 1)));
                float tau = F32(0.8 / (1 + 0.3 * i)), go = F32(1.0 / (i + 1)), amp = 0.5f, ph = F32(0.37 * i);
                DspSetup.SetMode(bank, i, hz, tau, go, 0f, SR);
                DspSetup.Strike(bank, i, amp, ph);
            }
            DspKernels.ModalFree(bank, 3, acc, 0, F);
            DspKernels.AccResolve(acc, outp, 0, F);
            return Copy(outp, F);
        }

        // ---- V2: MB4 driven, 20 modes (one 4-group pass + one single group), half-sine of 32 samples
        public static float[] Mb4Driven()
        {
            using var m = new Mem(5 * sizeof(ModalGroup) + 16 * F + 8 * F);
            var bank = (ModalGroup*)m.Aligned; var acc = (float4*)(m.Aligned + 5 * sizeof(ModalGroup)); var outp = (float*)(acc + F); var x = outp + F;
            for (int i = 0; i < 20; i++)
                DspSetup.SetMode(bank, i, F32(80 + 61.7 * i), F32(0.3 + 0.05 * i), 0.2f, F32(0.05 * (1 + (i % 3))), SR);
            for (int n = 0; n < 32; n++) x[n] = F32(Math.Sin(Math.PI * n / 32));
            DspKernels.ModalDriven(bank, 5, x, acc, 0, F);
            DspKernels.AccResolve(acc, outp, 0, F);
            return Copy(outp, F);
        }

        // ---- V3: one mode, unit impulse, 16 samples
        public static float[] Mb4Impulse(out float pr, out float pi)
        {
            using var m = new Mem(sizeof(ModalGroup) + 16 * 16 + 8 * 16);
            var bank = (ModalGroup*)m.Aligned; var acc = (float4*)(m.Aligned + sizeof(ModalGroup)); var outp = (float*)(acc + 16); var x = outp + 16;
            DspSetup.SetMode(bank, 0, 1000, 0.01, 1f, 1f, SR);
            x[0] = 1f;
            DspKernels.ModalDriven(bank, 1, x, acc, 0, 16);
            DspKernels.AccResolve(acc, outp, 0, 16);
            pr = bank->Pr.x; pi = bank->Pi.x;
            return Copy(outp, 16);
        }

        // ---- V4: WG4, strings 110/220/440/880 Hz, two blocks
        public static float[] Wg4()
        {
            const int len = 1024;
            using var m = new Mem(sizeof(WaveguideGroup) + 4 * len * 4 + 3 * 16 * F);
            var s = (WaveguideGroup*)m.Aligned; var lines = (float*)(m.Aligned + 192); var e4 = (float4*)(lines + 4 * len); var acc = e4 + F; var o = (float*)(acc + F);
            s->Len = len; s->Mask = len - 1;
            double[] f0 = { 110, 220, 440, 880 }, t60 = { 3, 2.5, 2, 1.5 }, br = { 0.5, 0.6, 0.7, 0.8 };
            for (int l = 0; l < 4; l++) DspSetup.SetString(s, l, f0[l], t60[l], br[l], 0.25f, SR);
            var outAll = new float[2 * F];
            for (int b = 0; b < 2; b++)
            {
                for (int n = 0; n < F; n++) { e4[n] = float4.zero; acc[n] = float4.zero; o[n] = 0; }
                if (b == 0) for (int n = 0; n < 24; n++) for (int l = 0; l < 4; l++) e4[n][l] = F32(Math.Sin(Math.PI * n / 24) * (0.5 + 0.1 * l));
                DspKernels.Waveguide4(s, lines, e4, acc, 0, F);
                DspKernels.AccResolve(acc, o, 0, F);
                for (int n = 0; n < F; n++) outAll[b * F + n] = o[n];
            }
            return outAll;
        }

        // ---- V5: NZ4 seed 12345 and four biquads
        public static float[] Noise(out float[] filtered, out uint[] laneSeeds)
        {
            using var m = new Mem(2 * 16 * F + 256);
            var io = (float4*)m.Aligned; var io2 = io + F; var q = (Biquad4*)(io2 + F);
            var nz = SplitMix32.SeedNoise(12345);
            laneSeeds = new[] { nz.State.x, nz.State.y, nz.State.z, nz.State.w };
            DspKernels.Noise4Process(&nz, io, 0, F);
            var noise = Copy((float*)io, 4 * F);
            float[][] c = { new[] { 0.2f, 0.4f, 0.2f, -0.5f, 0.3f }, new[] { 0.05f, 0f, -0.05f, -1.8f, 0.9f }, new[] { 0.9f, -1.8f, 0.9f, -1.78f, 0.81f }, new[] { 1f, 0f, 0f, 0f, 0f } };
            *q = default;
            for (int l = 0; l < 4; l++) DspSetup.SetBiquad(q, l, c[l][0], c[l][1], c[l][2], c[l][3], c[l][4]);
            for (int n = 0; n < F; n++) io2[n] = io[n];
            DspKernels.Biquad4Process(q, io2, 0, F);
            filtered = Copy((float*)io2, 4 * F);
            return noise;
        }

        // ---- V6: graph with strikes, a pulse, a damper and LOD over six blocks. Returns 12 blocks (src0, src1 per block).
        public static float[][] Graph(out int lodDropped, out int culled, out int maxActive)
        {
            var cfg = new GraphConfig { SampleRate = SR, MaxFrames = F, Sources = 2, Banks = 8, Groups = 64, StrikeModes = 256, Pending = 64, RingCapacity = 64, Budget = 1000 };
            long bytes = GraphArena.Bytes(cfg);
            int head = (sizeof(GraphState) + 63) & ~63;
            using var m = new Mem(bytes + head);
            var g = (GraphState*)m.Aligned;
            GraphArena.Layout(g, m.Aligned + head, cfg);
            int b0 = AddBank(g, 0, 8, 196, 0.6, 0.4), b1 = AddBank(g, 0, 10, 82.4, 1.2, 0.3), b2 = AddBank(g, 1, 6, 523.25, 2.0, 0.3);
            void Post(GraphOp op, int target, long at, float a, float b, int n = 0)
            {
                var c = new GraphCommand { Op = op, Target = target, When = at / (double)SR, A = a, B = b, N = n };
                g->Ring.TryStage(c); g->Ring.Commit();
            }
            Post(GraphOp.Strike, b0, 100, 0.8f, 0.25f);
            Post(GraphOp.Pulse, b1, 30, 0.5f, 0.002f);
            Post(GraphOp.Strike, b2, 256 + 5, 0.6f, 0f);
            Post(GraphOp.Damp, b2, 512 + 77, 8f, 0f);
            Post(GraphOp.SetBudget, 0, 768, 0f, 0f, 3);
            Post(GraphOp.Strike, b0, 1024 + 200, 0.5f, 1f);
            var outs = new float[12][];
            for (int k = 0; k < 6; k++)
            {
                AudioGraph.RenderBlock(g, k * F, F);
                outs[2 * k] = Copy(g->Out, F);
                outs[2 * k + 1] = Copy(g->Out + g->MaxFrames, F);
            }
            lodDropped = g->LodDropped; culled = g->Culled; maxActive = g->MaxActiveGroups;
            return outs;
        }

        static int AddBank(GraphState* g, int source, int n, double f0, double tau, double amp)
        {
            var hz = new float[n]; var t = new float[n]; var go = new float[n]; var gi = new float[n]; var a = new float[n]; var b = new float[n]; var e = new float[n]; var ph = new float[n]; var w = new float[n];
            for (int i = 0; i < n; i++)
            {
                hz[i] = F32(f0 * (i + 1)); t[i] = F32(tau / (1 + 0.2 * i)); go[i] = 1f; gi[i] = 0.02f;
                a[i] = F32(amp / (i + 1)); b[i] = F32(amp / (i + 1) * (i % 2 == 1 ? 0.2 : 1)); e[i] = F32(1 + 0.1 * i); ph[i] = F32(0.1 * i); w[i] = 1f;
            }
            fixed (float* p0 = hz, p1 = t, p2 = go, p3 = gi, p4 = a, p5 = b, p6 = e, p7 = ph, p8 = w)
                return AudioGraph.AddBank(g, source, n, p0, p1, p2, p3, p4, p5, p6, p7, p8);
        }

        // ---- V7: allocator steal order (no Node counterpart needed: the policy is the spec)
        public static int[] StealOrder()
        {
            var slots = new OneShotSlot[6];
            fixed (OneShotSlot* s = slots)
            {
                // 4 live slots + 2 ghosts; voices started at 0, 100, 2000, 3000 with levels -20, -45, -30, -10 dB; slot 0 released
                long[] st = { 0, 100, 2000, 3000 }; float[] lv = { -20, -45, -30, -10 };
                for (int i = 0; i < 4; i++) s[i] = new OneShotSlot { Id = i + 1, State = i == 0 ? SlotState.Released : SlotState.Active, Start = st[i], LevelDb = lv[i] };
                // now = 4000: slot 3 (age 1000 < 1440) is protected; scores: s0 released -20 - 20 = -40, s1 -45, s2 -30 -> s1 goes to ghost 4
                int a = VoiceAllocator.Allocate(s, 4, 2, 4000, out int ga);
                s[a] = new OneShotSlot { Id = 9, State = SlotState.Active, Start = 4000, LevelDb = -6 };   // the engine starts the new voice there
                // second steal at 4000: s1 (new) and s3 protected; s0 (-40) vs s2 (-30) -> s0 goes to ghost 5
                int b = VoiceAllocator.Allocate(s, 4, 2, 4000, out int gb);
                return new[] { a, ga, b, gb };   // expected { 1, 4, 0, 5 }
            }
        }

        static float[] Copy(float* p, int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = p[i]; return a; }
    }
}
