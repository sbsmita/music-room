// The handpan's audio-thread render (handpan.md sections 2 and 5.4): renderHandpan() of
// the Node handpan reference engine, operation for operation, on the engine's MB4 kernels (DspKernels.ModalDriven /
// ModalFree / AccResolve / ModalEndBlock). Inside AudioGraph.RenderBlock it is Burst-compiled with the entry's
// FloatMode.Fast (fused multiply-adds: within 1e-5 of the reference); called from managed code with State.Exact = 1 it
// runs the reference's two-rounding kernels below instead and is bit-exact with Node (the EditMode vectors).
//
// Per block: the due events split it sample-accurately (and so does every pending hit's start, segmentSplit); per
// segment: the 9 fields (contact pulse -> G0 -> nonlinear pump -> G1, G2 -> bus, cavity and shell inputs), the tak
// pulses, the shell bank, the cavity, then the sympathetic lanes; at block end: flush, retire, levels, damping and
// glide poles. Double maths only where the reference uses it (strike shapes, damping, glide, three constants that are
// not float32 values: 0.025, 0.010, 0.15). No allocation, no managed objects, no Unity API, no locks.
// IL2CPP: never `cond ? pointer : null` (ENGINE-API 3.4); pointer choices are if/else.
// Mono (this editor) evaluates a float expression in double and rounds only at its end (HandpanFloatProbeTests), so the
// scalar per-sample code writes every product and sum the reference rounds as (float)(...): no-ops under Burst.
using System;
using System.Threading;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    [BurstCompile]
    public static unsafe class HandpanDsp
    {
        const int NF = HandpanState.NF, GPF = HandpanState.GPF, NG = HandpanState.NG, TAK = HandpanState.Tak;
        const int SHELL0 = HandpanState.ShellG0, NSHELL = HandpanState.NShell, CAV = HandpanState.CavG;
        const int Slots = HandpanState.Slots, Modes = HandpanState.Modes;
        /// <summary>|z|^2 go^2 below this (-110 dBFS) retires a group (DspKernels.CullEnergy).</summary>
        const float Cull2 = DspKernels.CullEnergy;
        const double NlMinU2 = HandpanK.NL_MIN_U * HandpanK.NL_MIN_U;

        // ------------------------------------------------------------------ entry points

        /// <summary>Tests and tools: the same render through its own Burst entry (FloatMode.Fast, as RenderBlock).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void RenderDirect(HandpanState* s, UnitEvent* due, int dueCount, float* outMono, int frames, float budget)
        {
            Render(s, due, dueCount, outMono, frames, budget);
        }

        /// <summary>The unit's block (phase B: UnitHooks.RenderHandpan). Reset flag, the graph's budget, then Render into
        /// channel 0 (the unit's one source).</summary>
        public static void RenderUnit(UnitSlot* u)
        {
            var s = (HandpanState*)u->State;
            if (s == null || u->Channels < 1) return;
            if (Volatile.Read(ref s->Ready) == 0) return;
            if ((u->Flags & UnitFlags.Reset) != 0) { Reset(s); s->Resets++; }
            // platform.md 10.3: the graph's LOD budget x the governor's Share scales the unit's own caps (Render: below 0.6 x
            // 1200 the sympathetic lanes rest; below 0.45 x 1200 also the nonlinear pump, and the second upper group rings
            // only while struck)
            float budget = 1200f;
            if (u->Graph != null) budget = u->Graph->Budget;
            budget *= u->Share;
            // the state's scratch is sized for blocks of up to s->MaxFrames (1024; the device renders 256): a longer
            // block (an offline render) is rendered in chunks, each with its own due events
            int frames = u->Frames, done = 0, e0 = 0;
            int work = 0;
            while (done < frames)
            {
                int n = frames - done < s->MaxFrames ? frames - done : s->MaxFrames;
                int e1 = e0;
                while (e1 < u->DueCount && u->Due[e1].Offset < done + n) e1++;
                Render(s, u->Due + e0, e1 - e0, u->Channel(0) + done, n, budget, done);
                work += s->Work;
                e0 = e1; done += n;
            }
            s->Work = work;
            u->Work = work;
            // the platform's loudness parity (LoudnessParityTests: the piano's mf phrase +-2 LU): the lab matched OUT_GAIN to
            // the harp's single reference stroke (-25.8 LUFS); the four-note mf phrase rings on and measured +3.6 LU over the
            // piano's, so the graph path trims 3 dB (+0.6 LU). The reference port (RenderDirect, the vectors) stays untrimmed.
            float* ch = u->Channel(0);
            for (int i = 0; i < frames; i++) ch[i] *= GraphTrim;
        }

        /// <summary>The graph path's output trim: -3 dB (see RenderUnit).</summary>
        public const float GraphTrimDb = -3f, GraphTrim = 0.70794578f;

        /// <summary>renderHandpan(): due events sorted by (At, Seq) with Offset - offsetBase in [0, frames); out is ADDED to.</summary>
        public static void Render(HandpanState* s, UnitEvent* due, int dueCount, float* outp, int frames, float budget, int offsetBase = 0)
        {
            s->Budget = s->BudgetOverride > 0f ? s->BudgetOverride : budget;
            s->Work = 0;
            int cursor = 0;
            for (int e = 0; e < dueCount; e++)
            {
                int off = due[e].Offset - offsetBase;
                if (off < 0) off = 0;
                if (off > frames) off = frames;
                SegmentSplit(s, cursor, off, outp);
                Apply(s, &due[e].C);
                cursor = off;
            }
            SegmentSplit(s, cursor, frames, outp);
            EndBlock(s);
            SoftClip(outp, frames);
        }

        /// <summary>The safety soft clip (review item 16; the reference's renderHandpan end): above 0.95 full scale the output
        /// bends smoothly toward 1.0 (0.95 + 0.05 tanh((|x| - 0.95) / 0.05)), so ten fingers landing within a few ms (+11 dBFS)
        /// never leave the unit hotter than 1. Below 0.95 nothing changes: every vector and render peaks under 0.93.</summary>
        public const float SoftClipAt = 0.95f;
        static void SoftClip(float* outp, int frames)
        {
            for (int n = 0; n < frames; n++)
            {
                float v = outp[n];
                if (!(v > SoftClipAt || v < -SoftClipAt)) continue;
                double a = Math.Abs((double)v), k = 0.95;
                double y = k + (1 - k) * Math.Tanh((a - k) / (1 - k));
                outp[n] = (float)(v < 0f ? -y : y);
            }
        }

        // ------------------------------------------------------------------ events

        static void Apply(HandpanState* s, GraphCommand* c)
        {
            s->Events++;
            byte op = (byte)c->Op;
            if (op == HandpanState.OpStrike)
            {
                int i = c->N;
                if (i == TAK)
                {
                    int k = 0;
                    for (; k < HandpanState.TakSlots; k++) if (s->TakP[k].On == 0) break;
                    if (k == HandpanState.TakSlots) { k = 0; s->Merged++; }
                    LoadPulse(s->TakP + k, c);
                    for (int g = 0; g < NSHELL; g++) s->Active[SHELL0 + g] = 1;
                    s->Active[CAV] = 1;
                    return;
                }
                if ((uint)i >= NF) return;
                if (s->Pulse[i].On != 0)
                {
                    // a pulse is still in contact: the new hit waits for it (<= 2 ms); two waiting hits merge, louder wins
                    if (s->PendOn[i] == 0 || c->A > s->Pend[i].A1)
                    {
                        LoadPulse(s->Pend + i, c);
                        s->Pend[i].U = c->B; s->Pend[i].V = c->C; s->Pend[i].Tech = c->Target;
                    }
                    if (s->PendOn[i] != 0) s->Merged++;
                    s->PendOn[i] = 1;
                    return;
                }
                var p = new HandpanPulse();
                LoadPulse(&p, c);
                StartPulse(s, i, &p, c->B, c->C, c->Target);
                if (c->Target != (int)HandpanTech.Tak) for (int g = 0; g < NSHELL; g++) s->Active[SHELL0 + g] = 1;
                s->Active[CAV] = 1;
            }
            else if (op == HandpanState.OpDamp)
            {
                int i = c->N, slot = c->Target;
                if ((uint)slot >= Slots) return;
                float a = c->A;
                if (i == TAK) { s->DGlobal[slot] = a > 0f ? (float)(HandpanK.D_SHELL_TOUCH * a) : 0f; return; }
                if ((uint)i >= NF) return;
                float* d = s->DSlot + (i * Slots + slot) * Modes;
                if (a <= 0f) { for (int l = 0; l < Modes; l++) d[l] = 0f; return; }
                if ((int)c->D == (int)HandpanDampKind.Palm) { float v = (float)(HandpanK.D_PALM * a); for (int l = 0; l < Modes; l++) d[l] = v; return; }
                float* sh = stackalloc float[Modes];
                Shapes(s, i, c->B, c->C, (int)HandpanTech.Tip, sh);
                for (int l = 0; l < Modes; l++)
                {
                    double q = Math.Min(1.0, Math.Abs((double)sh[l]) / (l < 3 ? 1.0 : 1.3));
                    d[l] = (float)(HandpanK.D_FINGER * a * (HandpanK.D_FINGER_FLOOR + (1 - HandpanK.D_FINGER_FLOOR) * q * q));
                }
            }
            else if (op == HandpanState.OpReset) Reset(s);
            else if (op == HandpanState.OpSetBudget) { s->BudgetOverride = c->A; s->Budget = c->A > 0f ? c->A : s->Budget; }
            else if (op == HandpanState.OpReleaseAll)
            {
                for (int k = 0; k < NF * Slots * Modes; k++) s->DSlot[k] = 0f;
                for (int k = 0; k < Slots; k++) s->DGlobal[k] = 0f;
            }
        }

        /// <summary>A strike command's pulse fields (handpan.md 5.2 payload): A = A1 x gain, Seed = bits(A2 x gain),
        /// D E F G = cos/sin(2 pi / W1), cos/sin(2 pi / W2), Id = W1 | W2 &lt;&lt; 16.</summary>
        static void LoadPulse(HandpanPulse* p, GraphCommand* c)
        {
            uint seed = c->Seed;
            p->On = 1; p->N = 0; p->W1 = c->Id & 0xffff; p->W2 = (c->Id >> 16) & 0xffff;
            p->A1 = c->A; p->A2 = *(float*)&seed;
            p->c1 = 1f; p->s1 = 0f; p->c2 = 1f; p->s2 = 0f;
            p->C1 = c->D; p->S1 = c->E; p->C2 = c->F; p->S2 = c->G;
            p->Fp = 0f;
        }

        /// <summary>startPulse(): the strike position's mode shapes scale the 12 input gains of G0..G2; the pulse starts.</summary>
        static void StartPulse(HandpanState* s, int i, HandpanPulse* src, float u, float v, int tech)
        {
            float* sh = stackalloc float[Modes];
            Shapes(s, i, u, v, tech, sh);
            int g = i * GPF;
            double techK = tech == (int)HandpanTech.Tak ? HandpanK.TAK_FIELD_K : 1.0;
            for (int l = 0; l < Modes; l++)
            {
                int gg, ll;
                if (l < 4) { gg = g; ll = l; } else { gg = g + 1 + ((l - 4) >> 2); ll = (l - 4) & 3; }
                s->Bank[gg].Gi[ll] = (float)((double)s->BaseGi[gg * 4 + ll] * sh[l] * techK);
            }
            HandpanPulse* p = s->Pulse + i;
            *p = *src;
            p->On = 1; p->N = 0; p->c1 = 1f; p->s1 = 0f; p->c2 = 1f; p->s2 = 0f; p->Fp = 0f;
            double rho2 = Math.Min(1.0, (double)u * u + (double)v * v);
            bool ding = s->Ding[i] != 0;
            if (tech == (int)HandpanTech.Tak) { s->ShellAmp[i] = 0f; s->CavRes[i] = 0f; }
            else
            {
                s->ShellAmp[i] = (float)(HandpanK.SHELL_W_CENTRE + HandpanK.SHELL_W_EDGE * rho2 * rho2);
                double cr = 0;
                if (tech == (int)HandpanTech.Slap) cr = HandpanK.CAV_RES_PALM * (ding ? 1 : 0.35);
                else if (ding) cr = HandpanK.CAV_RES_DING * 0.25;
                s->CavRes[i] = (float)cr;
            }
            for (int q = 0; q < 4; q++) s->Active[g + q] = 1;
        }

        static float J0p(double r2) => (float)(1 + r2 * (-1.446 + r2 * (0.5227 + r2 * (-0.0840 + r2 * 0.00227))));
        static float J1p(double r2) => (float)((1.916 + r2 * (-3.517 + r2 * (2.152 + r2 * (-0.658 + r2 * (0.1208 - r2 * 0.0148))))) / 0.5819);

        /// <summary>shapes(): the 12 mode shapes (T1 T2 T3 U0..U8) at (u, v) for a technique (handpan.md 2.4), double maths
        /// rounded once per value (u and v are the command's float32 values, as in the reference).</summary>
        public static void Shapes(HandpanState* s, int field, float uf, float vf, int tech, float* outp) => Shapes(s, field, (double)uf, (double)vf, tech, outp);

        /// <summary>The same with double coordinates (the reference's own shapes() test values).</summary>
        public static void Shapes(HandpanState* s, int field, double u, double v, int tech, float* outp)
        {
            double r2 = Math.Min(1.0, u * u + v * v);
            double p1 = J0p(r2);
            double ua = u - HandpanK.U_OFF, ra = ua * ua + v * v;
            double p2 = 0; if (ra < 1) p2 = ua * J1p(ra);
            double vb = v - HandpanK.V_OFF, rb = u * u + vb * vb;
            double p3 = 0; if (rb < 1) p3 = vb * J1p(rb);
            if (tech == (int)HandpanTech.Slap) { p1 = 0.55; p2 = 0.10; p3 = 0.08; }
            if (tech == (int)HandpanTech.Tak) { p1 = 0.55; p2 = 0.12; p3 = 0.10; }
            outp[0] = (float)p1; outp[1] = (float)p2; outp[2] = (float)p3;
            double r = Math.Sqrt(r2);
            double cr = 1, sr = 0;
            if (r > 1e-6) { cr = u / r; sr = v / r; }
            double env = (0.35 + 0.65 * r2) * (1 - r2) * 4;
            for (int k = 0; k < 9; k++)
            {
                int m = s->UpperM[field * 9 + k];
                double c = 1, sn = 0;
                for (int q = 0; q < m; q++) { double nc = c * cr - sn * sr; sn = sn * cr + c * sr; c = nc; }
                double cb = s->UpperCosB[field * 9 + k], sb = s->UpperSinB[field * 9 + k];
                double val = env * (c * cb - sn * sb);
                if (tech == (int)HandpanTech.Slap) val = 0.15 * cb;
                else if (tech == (int)HandpanTech.Tak) val = 0.10 * cb;
                outp[3 + k] = (float)val;
            }
        }

        /// <summary>resetState(): the bank, gains and poles from the table; pulses, pending hits, tak slots, NL history,
        /// damping, glide and levels cleared (the budget override is kept).</summary>
        public static void Reset(HandpanState* s)
        {
            for (int g = 0; g < NG; g++) { s->Bank[g] = s->Design[g]; s->Active[g] = 0; }
            for (int i = 0; i < NF; i++)
            {
                s->Pulse[i] = default; s->Pend[i] = default; s->PendOn[i] = 0; s->ExcOn[i] = 0;
                s->NlPrev[i] = 0f; s->U2[i] = 0f; s->GlideOn[i] = 0; s->Level[i] = 0f;
            }
            for (int k = 0; k < HandpanState.TakSlots; k++) s->TakP[k] = default;
            for (int k = 0; k < NF * Slots * Modes; k++) s->DSlot[k] = 0f;
            for (int k = 0; k < NF * Modes; k++) s->DCur[k] = 0f;
            for (int k = 0; k < Slots; k++) s->DGlobal[k] = 0f;
            for (int n = 0; n < s->MaxFrames; n++) s->Acc[n] = float4.zero;
        }

        // ------------------------------------------------------------------ the block

        /// <summary>runPulse(): one pulse slot's force derivative ADDED into dst[from, to); false when the slot is off.</summary>
        static bool RunPulse(HandpanPulse* P, float* dst, int from, int to)
        {
            if (P->On == 0) return false;
            int n = P->N, W1 = P->W1, W2 = P->W2, Wm = W1 > W2 ? W1 : W2;
            float c1 = P->c1, s1 = P->s1, c2 = P->c2, s2 = P->s2, Fp = P->Fp;
            float A1h = 0.5f * P->A1, A2h = 0.5f * P->A2, C1 = P->C1, S1 = P->S1, C2 = P->C2, S2 = P->S2;
            for (int k = from; k < to; k++)
            {
                n++;
                float nc1 = (float)(c1 * C1) - (float)(s1 * S1); s1 = (float)(s1 * C1) + (float)(c1 * S1); c1 = nc1;
                float nc2 = (float)(c2 * C2) - (float)(s2 * S2); s2 = (float)(s2 * C2) + (float)(c2 * S2); c2 = nc2;
                float F = 0f;
                if (n < W1) F = (float)(A1h * (float)(1f - c1));
                if (n < W2) F = F + (float)(A2h * (float)(1f - c2));
                dst[k] = dst[k] + (float)(F - Fp);
                Fp = F;
                if (n >= Wm) { P->On = 0; break; }
            }
            P->N = n; P->c1 = c1; P->s1 = s1; P->c2 = c2; P->s2 = s2; P->Fp = Fp;
            return true;
        }

        static void RenderGroup(HandpanState* s, int g, float* x, int from, int to)
        {
            if (s->Exact != 0)
            {
                if (x != null) ExactDriven(s->Bank + g, x, s->Acc, from, to); else ExactFree(s->Bank + g, s->Acc, from, to);
            }
            else
            {
                if (x != null) DspKernels.ModalDriven(s->Bank + g, 1, x, s->Acc, from, to); else DspKernels.ModalFree(s->Bank + g, 1, s->Acc, from, to);
            }
            s->GroupsRendered++;
            s->LaneSamples += 4 * (to - from);
            s->Work += 4 * (to - from);
        }

        static void Resolve(HandpanState* s, float* outp, int from, int to)
        {
            if (s->Exact != 0) ExactResolve(s->Acc, outp, from, to);
            else DspKernels.AccResolve(s->Acc, outp, from, to);
        }

        static void Segment(HandpanState* s, int from, int to, float* outp)
        {
            if (to <= from) return;
            int MF = s->MaxFrames;
            float* bus = s->Bus; float* shellX = s->ShellX; float* cavX = s->CavX; float* y0 = s->Y0; float4* acc = s->Acc;
            for (int n = from; n < to; n++) { bus[n] = 0f; shellX[n] = 0f; cavX[n] = 0f; }
            bool busOn = false, shellOn = false, cavOn = false;
            // the reference's levels are 0.4 and 0.6 x 1200; the low one is 0.45 here so a governor Share of 0.4 (480) reaches it
            bool lowBudget = s->Budget < 1200 * 0.45, midBudget = s->Budget < 1200 * 0.6;
            bool batch = s->Exact == 0;
            s->BatchN = 0;
            for (int i = 0; i < NF; i++)
            {
                int g = i * GPF;
                float* ex = s->Exc + i * MF;
                bool hasExc = false;
                if (s->Pulse[i].On != 0)
                {
                    for (int n = from; n < to; n++) ex[n] = 0f;
                    RunPulse(s->Pulse + i, ex, from, to);   // may end inside: SegmentSplit starts a pending hit exactly there
                    hasExc = true;
                }
                s->ExcOn[i] = (byte)(hasExc ? 1 : 0);
                bool anyField = hasExc || s->Active[g] != 0 || s->Active[g + 1] != 0 || s->Active[g + 2] != 0 || s->Active[g + 3] != 0;
                if (!anyField) continue;
                float* tu = s->Tuned + i * MF;
                float* x = null;
                if (hasExc) x = ex;
                // G0: T1 T2 T3 U0 -> the fundamental y0 and the tuned sum for the bus
                if (hasExc || s->Active[g] != 0)
                {
                    RenderGroup(s, g, x, from, to);
                    for (int n = from; n < to; n++) { float4 a = acc[n]; y0[n] = a.x; tu[n] = (float)((float)(a.x + a.y) + a.z); }
                    Resolve(s, outp, from, to);
                }
                else for (int n = from; n < to; n++) { y0[n] = 0f; tu[n] = 0f; }
                // NL: x = d/dn (u^2 + u^3 + u^4 + u^5), u = y0 / uRef
                if (!lowBudget && (s->Active[g] != 0 || s->Active[g + 3] != 0))
                {
                    float inv = (float)(1.0 / s->URef[i]);
                    float prev = s->NlPrev[i], peak = 0f;
                    float* xnl = s->Xnl;
                    for (int n = from; n < to; n++)
                    {
                        float u = (float)(y0[n] * inv), uu, p;
                        if (u > HandpanK.NL_UMAX || u < -HandpanK.NL_UMAX) NlClamped(u > 0f ? HandpanK.NL_UMAX : -HandpanK.NL_UMAX, out uu, out p);
                        else
                        {
                            // p = uu (((u + 1) u + 1) u + 1): the reference's mad chain with k = 1, every step rounded
                            uu = (float)(u * u);
                            float t = (float)(u + 1f);
                            t = (float)((float)(u * t) + 1f);
                            t = (float)((float)(u * t) + 1f);
                            p = (float)(uu * t);
                        }
                        xnl[n] = p - prev;
                        prev = p;
                        if (uu > peak) peak = uu;
                    }
                    s->NlPrev[i] = prev; s->U2[i] = peak;
                    if (peak > NlMinU2 || s->Active[g + 3] != 0)
                    {
                        s->Active[g + 3] = 1;
                        float* xn = null;
                        if (peak > NlMinU2) xn = xnl;
                        RenderGroup(s, g + 3, xn, from, to);
                        for (int n = from; n < to; n++) { float4 a = acc[n]; tu[n] = tu[n] + (float)((float)(a.x + a.y) + (float)(a.z + a.w)); }
                        Resolve(s, outp, from, to);
                    }
                }
                // G1, G2: the upper modes (they only reach the output: the Burst path batches them, see FlushBatch)
                for (int q = 1; q <= 2; q++)
                {
                    if (!(hasExc || s->Active[g + q] != 0)) continue;
                    if (lowBudget && q == 2 && !hasExc) { s->Active[g + q] = 0; continue; }
                    if (batch) { Batch(s, g + q, hasExc ? i : XZero); continue; }
                    RenderGroup(s, g + q, x, from, to);
                    Resolve(s, outp, from, to);
                }
                // bus, shell and cavity inputs
                for (int n = from; n < to; n++) bus[n] = tu[n] + bus[n];
                busOn = true;
                double cw = s->Ding[i] != 0 ? HandpanK.CAV_W_DING : HandpanK.CAV_W_RING;
                for (int n = from; n < to; n++) cavX[n] = (float)(cw * y0[n]) + cavX[n];
                cavOn = true;
                if (hasExc && s->ShellAmp[i] != 0f)
                {
                    float sa = s->ShellAmp[i], cr = s->CavRes[i];
                    for (int n = from; n < to; n++) { float e = ex[n]; shellX[n] = (float)(sa * e) + shellX[n]; cavX[n] = (float)(cr * e) + cavX[n]; }
                    shellOn = true;
                }
            }
            // interstitial taps: the tak pulses (summed) drive the shell and push the cavity
            bool takOn = false;
            float* takX = s->TakX;
            for (int k = 0; k < HandpanState.TakSlots; k++)
            {
                if (s->TakP[k].On == 0) continue;
                if (!takOn) { for (int n = from; n < to; n++) takX[n] = 0f; takOn = true; }
                RunPulse(s->TakP + k, takX, from, to);
            }
            if (takOn)
            {
                for (int n = from; n < to; n++) { shellX[n] = shellX[n] + takX[n]; cavX[n] = (float)(HandpanK.CAV_TAK * takX[n]) + cavX[n]; }
                shellOn = true; cavOn = true;
            }
            // shell bank and cavity
            for (int q = 0; q < NSHELL; q++)
            {
                int g = SHELL0 + q;
                if (!(shellOn || s->Active[g] != 0)) continue;
                s->Active[g] = 1;
                if (batch) { Batch(s, g, shellOn ? XShell : XZero); continue; }
                float* xs = null;
                if (shellOn) xs = shellX;
                RenderGroup(s, g, xs, from, to);
                Resolve(s, outp, from, to);
            }
            if (cavOn || s->Active[CAV] != 0)
            {
                s->Active[CAV] = 1;
                if (batch) Batch(s, CAV, cavOn ? XCav : XZero);
                else
                {
                    float* xc = null;
                    if (cavOn) xc = cavX;
                    RenderGroup(s, CAV, xc, from, to);
                    Resolve(s, outp, from, to);
                }
            }
            // sympathetic lanes of every field: the bus minus the field's own tuned output (one-way coupling)
            if (!midBudget)
            {
                float* xsym = s->Xs;
                for (int j = 0; j < NF; j++)
                {
                    int g = j * GPF + 4;
                    if (!busOn && s->Active[g] == 0) continue;
                    float* tu = s->Tuned + j * MF;
                    bool own = s->ExcOn[j] != 0 || s->Active[j * GPF] != 0 || s->Active[j * GPF + 3] != 0;
                    float* xj = xsym;
                    if (batch) xj = s->XsAll + j * MF;
                    if (own) for (int n = from; n < to; n++) xj[n] = bus[n] - tu[n];
                    else for (int n = from; n < to; n++) xj[n] = bus[n];
                    s->Active[g] = 1;
                    if (batch) { Batch(s, g, busOn ? XSym0 + j : XZero); continue; }
                    float* xb = null;
                    if (busOn) xb = xsym;
                    RenderGroup(s, g, xb, from, to);
                    Resolve(s, outp, from, to);
                }
            }
            if (batch) FlushBatch(s, outp, from, to);
        }

        // ------------------------------------------------------------------ the Burst path's batch
        // The upper modes (G1, G2), the shell, the cavity and the sympathetic groups reach only the output, so on the Burst
        // path they are queued during the segment and rendered FOUR GROUPS AT A TIME (each with its own input), into one
        // accumulator resolved once: the MB4 kernel's latency is hidden behind four independent groups (DspKernels'
        // ModalDriven4 idea with per-group inputs). The sum's order differs from the reference's group-by-group order, so
        // this path is within 1e-5 of the reference (as Burst's fused multiply-adds are), never bit-exact; the managed exact
        // path keeps the reference's order.
        const int XZero = -1, XShell = 200, XCav = 201, XSym0 = 100;

        static void Batch(HandpanState* s, int g, int x)
        {
            int n = s->BatchN;
            s->BatchG[n] = g; s->BatchX[n] = x; s->BatchN = n + 1;
        }

        static float* XOf(HandpanState* s, int code)
        {
            float* p = s->Zero;
            if (code >= 0 && code < NF) p = s->Exc + code * s->MaxFrames;
            else if (code >= XSym0 && code < XSym0 + NF) p = s->XsAll + (code - XSym0) * s->MaxFrames;
            else if (code == XShell) p = s->ShellX;
            else if (code == XCav) p = s->CavX;
            return p;
        }

        static void FlushBatch(HandpanState* s, float* outp, int from, int to)
        {
            int n = s->BatchN;
            if (n == 0) return;
            ModalGroup* b = s->Bank;
            int k = 0;
            for (; k + 4 <= n; k += 4)
                Driven4x(b + s->BatchG[k], b + s->BatchG[k + 1], b + s->BatchG[k + 2], b + s->BatchG[k + 3],
                    XOf(s, s->BatchX[k]), XOf(s, s->BatchX[k + 1]), XOf(s, s->BatchX[k + 2]), XOf(s, s->BatchX[k + 3]), s->Acc, from, to);
            for (; k < n; k++) DspKernels.ModalDriven(b + s->BatchG[k], 1, XOf(s, s->BatchX[k]), s->Acc, from, to);
            DspKernels.AccResolve(s->Acc, outp, from, to);
            s->GroupsRendered += n;
            s->LaneSamples += 4L * n * (to - from);
            s->Work += 4 * n * (to - from);
            s->BatchN = 0;
        }

        /// <summary>Four MB4 groups, each driven by its own input, interleaved in registers (DspKernels.ModalDriven4's
        /// arithmetic: z = p z + gi x, acc += go Im z).</summary>
        static void Driven4x(ModalGroup* g0, ModalGroup* g1, ModalGroup* g2, ModalGroup* g3, float* x0, float* x1, float* x2, float* x3, float4* acc, int from, int to)
        {
            float4 zr0 = g0->Zr, zi0 = g0->Zi, pr0 = g0->Pr, pi0 = g0->Pi, go0 = g0->Go, gi0 = g0->Gi, np0 = -pi0;
            float4 zr1 = g1->Zr, zi1 = g1->Zi, pr1 = g1->Pr, pi1 = g1->Pi, go1 = g1->Go, gi1 = g1->Gi, np1 = -pi1;
            float4 zr2 = g2->Zr, zi2 = g2->Zi, pr2 = g2->Pr, pi2 = g2->Pi, go2 = g2->Go, gi2 = g2->Gi, np2 = -pi2;
            float4 zr3 = g3->Zr, zi3 = g3->Zi, pr3 = g3->Pr, pi3 = g3->Pi, go3 = g3->Go, gi3 = g3->Gi, np3 = -pi3;
            for (int n = from; n < to; n++)
            {
                float4 t0 = math.mad(pr0, zi0, pi0 * zr0); zr0 = math.mad(pr0, zr0, math.mad(np0, zi0, gi0 * x0[n])); zi0 = t0;
                float4 t1 = math.mad(pr1, zi1, pi1 * zr1); zr1 = math.mad(pr1, zr1, math.mad(np1, zi1, gi1 * x1[n])); zi1 = t1;
                float4 t2 = math.mad(pr2, zi2, pi2 * zr2); zr2 = math.mad(pr2, zr2, math.mad(np2, zi2, gi2 * x2[n])); zi2 = t2;
                float4 t3 = math.mad(pr3, zi3, pi3 * zr3); zr3 = math.mad(pr3, zr3, math.mad(np3, zi3, gi3 * x3[n])); zi3 = t3;
                float4 a = acc[n];
                a = math.mad(go0, zi0, a);
                a = math.mad(go1, zi1, a);
                a = math.mad(go2, zi2, a);
                a = math.mad(go3, zi3, a);
                acc[n] = a;
            }
            g0->Zr = zr0; g0->Zi = zi0; g1->Zr = zr1; g1->Zi = zi1;
            g2->Zr = zr2; g2->Zi = zi2; g3->Zr = zr3; g3->Zi = zi3;
        }

        /// <summary>The NL polynomial at the clamp: the reference clamps u to the DOUBLE +-1.6 and rounds each step (a float
        /// 1.6f would differ by an ulp), so this rare branch runs in double, rounded where the reference rounds.</summary>
        static void NlClamped(double u, out float uu, out float p)
        {
            uu = (float)(u * u);
            float t = (float)((float)(u * 1.0) + 1.0);
            t = (float)((float)(u * t) + 1.0);
            t = (float)((float)(u * t) + 1.0);
            p = (float)(uu * t);
        }

        /// <summary>segmentSplit(): a pending hit starts on the sample after its field's running pulse ends.</summary>
        static void SegmentSplit(HandpanState* s, int from, int to, float* outp)
        {
            int a = from;
            while (a < to)
            {
                int b = to;
                for (int i = 0; i < NF; i++)
                {
                    if (s->PendOn[i] == 0 || s->Pulse[i].On == 0) continue;
                    HandpanPulse* P = s->Pulse + i;
                    int rem = (P->W1 > P->W2 ? P->W1 : P->W2) - P->N;
                    if (a + rem < b) b = a + rem;
                }
                Segment(s, a, b, outp);
                for (int i = 0; i < NF; i++)
                {
                    if (s->PendOn[i] == 0 || s->Pulse[i].On != 0) continue;
                    s->PendOn[i] = 0;
                    HandpanPulse* pe = s->Pend + i;
                    StartPulse(s, i, pe, pe->U, pe->V, pe->Tech);
                }
                a = b;
            }
        }

        /// <summary>endBlock(): flush, energy, retire; levels; damping and glide pole updates for the next block.</summary>
        static void EndBlock(HandpanState* s)
        {
            int alive = 0;
            float e;
            for (int g = 0; g < NG; g++)
            {
                if (s->Active[g] == 0) continue;
                if (s->Exact != 0) e = ExactEndBlock(s->Bank + g); else DspKernels.ModalEndBlock(s->Bank + g, 1, &e);
                int fi = g < SHELL0 ? g / GPF : -1;
                bool driven = fi >= 0 && (s->Pulse[fi].On != 0 || s->PendOn[fi] != 0);
                if (e < Cull2 && !driven)
                {
                    s->Active[g] = 0;
                    s->Bank[g].Zr = float4.zero; s->Bank[g].Zi = float4.zero;
                    s->Retired++;
                }
                else alive++;
            }
            s->ActiveGroups = alive;
            if (alive > s->MaxGroups) s->MaxGroups = alive;
            for (int i = 0; i < NF; i++)
            {
                ModalGroup* b = s->Bank + i * GPF;
                double en = 0;
                for (int l = 0; l < 3; l++)
                {
                    double zr = b->Zr[l], zi = b->Zi[l], go = b->Go[l];
                    en += (zr * zr + zi * zi) * go * go;
                }
                s->Level[i] = (float)Math.Sqrt(en);
            }
            // damping: target rate per mode = the slots + the shell touch; smoothed; poles p = p0 (1 - d / sr), f0 x glide
            double dg = 0;
            for (int k = 0; k < Slots; k++) dg += s->DGlobal[k];
            float isr = (float)(1.0 / 48000.0);
            for (int i = 0; i < NF; i++)
            {
                bool changed = false;
                for (int l = 0; l < Modes; l++)
                {
                    double t = dg;
                    for (int k = 0; k < Slots; k++) t += s->DSlot[(i * Slots + k) * Modes + l];
                    float c = s->DCur[i * Modes + l];
                    double nv = c + (t - c) * HandpanK.D_SMOOTH;
                    if (Math.Abs(nv - t) < 0.05) nv = t;
                    if (nv != c) { s->DCur[i * Modes + l] = (float)nv; changed = true; }
                }
                float u2 = s->U2[i];
                bool glide = u2 > 1e-4 || s->GlideOn[i] != 0;
                if (!changed && !glide) continue;
                s->GlideOn[i] = (byte)(u2 > 1e-4 ? 1 : 0);
                int g0 = i * GPF;
                for (int q = 0; q < GPF; q++)
                {
                    int g = g0 + q;
                    ModalGroup* b = s->Bank + g;
                    for (int l = 0; l < 4; l++)
                    {
                        int mode;
                        if (q == 0) mode = l;
                        else if (q < 3) mode = 4 + (q - 1) * 4 + l;
                        else if (q == 3) mode = (l & 1) == 0 ? 1 : 2;              // NL [T2 T3 T2 T3]
                        else mode = l == 3 ? 1 : l;                                  // SYM [T1 T2 T3 T2]
                        float d = s->DCur[i * Modes + (mode < 11 ? mode : 11)];
                        float m = (float)(1 - (double)d * isr);
                        float pr = (float)(s->P0r[g * 4 + l] * m), pi = (float)(s->P0i[g * 4 + l] * m);
                        if (q == 0 && l == 0 && u2 > 1e-4)
                        {
                            float dw = (float)(s->GlideW[i] * Math.Min(u2, 1.5));
                            float cg = (float)(1 - 0.5 * dw * dw);
                            float npr = (float)(pr * cg) - (float)(pi * dw);
                            pi = (float)(pi * cg) + (float)(pr * dw);
                            pr = npr;
                        }
                        b->Pr[l] = pr; b->Pi[l] = pi;
                    }
                }
            }
            s->Blocks++;
        }

        // ------------------------------------------------------------------ the reference's kernels (State.Exact = 1)
        // Plain C# (no Burst attribute): called from managed code they round every multiply and add separately, exactly as
        // the Node kernels (mad = two roundings), lane by lane. Called inside a Burst entry they would compile Fast like
        // DspKernels, so the flag only matters to the managed EditMode vectors.

        static void ExactDriven(ModalGroup* g, float* x, float4* acc, int from, int to)
        {
            float4 zr = g->Zr, zi = g->Zi, pr = g->Pr, pi = g->Pi, go = g->Go, gi = g->Gi, npi = -pi;
            for (int n = from; n < to; n++)
            {
                float xn = x[n];
                float4 t = pr * zi + pi * zr;
                zr = pr * zr + (npi * zi + gi * xn);
                zi = t;
                acc[n] = go * zi + acc[n];
            }
            g->Zr = zr; g->Zi = zi;
        }

        static void ExactFree(ModalGroup* g, float4* acc, int from, int to)
        {
            float4 zr = g->Zr, zi = g->Zi, pr = g->Pr, pi = g->Pi, go = g->Go, npi = -pi;
            for (int n = from; n < to; n++)
            {
                float4 t = pr * zi + pi * zr;
                zr = pr * zr + npi * zi;
                zi = t;
                acc[n] = go * zi + acc[n];
            }
            g->Zr = zr; g->Zi = zi;
        }

        static void ExactResolve(float4* acc, float* outp, int from, int to)
        {
            for (int n = from; n < to; n++)
            {
                float4 a = acc[n];
                outp[n] = outp[n] + (float)((float)(a.x + a.y) + (float)(a.z + a.w));
                acc[n] = float4.zero;
            }
        }

        static float ExactEndBlock(ModalGroup* g)
        {
            float4 zr = g->Zr, zi = g->Zi, go = g->Go;
            zr = math.select(zr, 0f, math.abs(zr) < DspKernels.Flush);
            zi = math.select(zi, 0f, math.abs(zi) < DspKernels.Flush);
            g->Zr = zr; g->Zi = zi;
            return math.cmax((zr * zr + zi * zi) * (go * go));
        }
    }
}
