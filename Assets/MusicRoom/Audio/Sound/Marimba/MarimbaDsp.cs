// The marimba's audio-thread render (spec 6.2 "Render(u)"; marimba-engine.mjs MarimbaUnit.render), Burst, in the
// canonical order of the reference so the managed-strict path is bit-exact with the Node vectors:
//   0. Budget and Share once; 1. Reset; 2. due events in (At, Seq) order (Strike: a player + an Inject action at
//   At + nh; Damp / Undamp: a pole action; Silence: everything fades); 3. bars with an action this block become active,
//   then the quietest beyond the cap fades (one block); 4. each active bar, ascending: both MB4 groups free-run
//   (DspKernels.ModalFree) in segments split at its actions, AccResolve, add to its source (or fade out), ModalEndBlock,
//   retire under -110 dBFS; 5. strike records (contact sound + knock) play onto their bar's source; 6. Work.
// Entry points: Render(UnitSlot*) is what UnitHooks.RenderMarimba calls inside AudioGraph.RenderBlock (phase B; the
// platform declares the hook). RenderFast / RenderStrict are the same code behind Burst entries with the engine's
// FloatMode.Fast and with FloatMode.Default (the bit-exact path of the tests). No allocation, no managed objects.
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    [BurstCompile]
    public static unsafe class MarimbaDsp
    {
        const int NB = MarimbaConst.Bars, L = MarimbaConst.Lanes;

        /// <summary>The engine's FloatMode (what the app runs): tests compare to the Node vectors within 1e-5.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void RenderFast(UnitSlot* u) => Render(u);

        /// <summary>The same render with IEEE arithmetic (no fused mad): bit-exact with the reference's strict vectors.</summary>
        [BurstCompile(FloatMode = FloatMode.Default, CompileSynchronously = true)]
        public static void RenderStrict(UnitSlot* u) => Render(u);

        /// <summary>The unit's block (UnitHooks.RenderMarimba). Channels 0..2 = MarimbaLo / Mid / Hi. Blocks longer than the
        /// scratch are rendered in chunks of MarimbaState.ChunkFrames (the device block is 256: one chunk).</summary>
        public static void Render(UnitSlot* u)
        {
            var s = (MarimbaState*)u->State;
            if (s == null || u->Channels < MarimbaConst.Channels) return;
            if (Volatile.Read(ref s->Ready) == 0) return;   // tables not in yet: drop (first-frame edge case)
            int budget = u->Graph != null ? u->Graph->Budget : s->Budget;
            // the governor's share (UnitSlot.Share, 1 by default); the phase-A host leaves u->Share at 0 and sets s->Share
            float share = u->Share > 0f ? u->Share : s->Share;
            if ((u->Flags & UnitFlags.Reset) != 0) ResetAll(s);
            float* o0 = u->Channel(0), o1 = u->Channel(1), o2 = u->Channel(2);
            int frames = u->Frames, e = 0;
            for (int off = 0; off < frames; off += MarimbaState.ChunkFrames)
            {
                int n = math.min(MarimbaState.ChunkFrames, frames - off);
                int e0 = e;
                while (e < u->DueCount && u->Due[e].Offset < off + n) e++;
                Block(s, u->Due + e0, e - e0, o0 + off, o1 + off, o2 + off, u->BlockStart + off, n, budget, share);
            }
            u->Work = s->Work;   // the unit's own work count (live lanes rendered), for the governor and UnitShareTests
            s->Blocks++;
        }

        /// <summary>UnitFlags.Reset (resume after a suspension): nothing of the unit's may still ring. The undamped poles come
        /// back too (a damper's Undamp may have been dropped while suspended).</summary>
        public static void ResetAll(MarimbaState* s)
        {
            for (int i = 0; i < NB; i++)
            {
                ZeroBar(s, i);
                s->Active[i] = 0; s->Fading[i] = 0; s->Damped[i] = 0;
                RestorePoles(s, i);
            }
            for (int p = 0; p < MarimbaConst.Players; p++) s->Players[p].On = 0;
            s->ActionCount = 0;
        }

        static void ZeroBar(MarimbaState* s, int i)
        {
            ModalGroup* g = s->Groups + 2 * i;
            g[0].Zr = float4.zero; g[0].Zi = float4.zero; g[1].Zr = float4.zero; g[1].Zi = float4.zero;
            s->Energy[i] = 0; s->Level[i] = 0;
        }

        static void RestorePoles(MarimbaState* s, int i)
        {
            for (int l = 0; l < L; l++)
            {
                ModalGroup* g = s->Groups + 2 * i + (l >> 2);
                int c = l & 3, k = i * L + l;
                ((float*)&g->Pr)[c] = s->P0r[k];
                ((float*)&g->Pi)[c] = s->P0i[k];
            }
        }

        /// <summary>Inserts an action keeping (At, Seq) order (addAction: after every entry that is not later).</summary>
        static void AddAction(MarimbaState* s, long at, int seq, int kind, int bar, int id)
        {
            if (s->ActionCount >= MarimbaConst.Actions) { s->DroppedActions++; return; }
            int k = s->ActionCount;
            MarimbaAction* a = s->Actions;
            while (k > 0 && (a[k - 1].At > at || (a[k - 1].At == at && a[k - 1].Seq > seq))) { a[k] = a[k - 1]; k--; }
            a[k] = new MarimbaAction { At = at, Seq = seq, Kind = kind, Bar = bar, Id = id };
            s->ActionCount++;
        }

        static bool HasActionBefore(MarimbaState* s, int bar, long end)
        {
            for (int k = 0; k < s->ActionCount; k++) if (s->Actions[k].Bar == bar && s->Actions[k].At < end) return true;
            return false;
        }

        static bool HasAction(MarimbaState* s, int bar)
        {
            for (int k = 0; k < s->ActionCount; k++) if (s->Actions[k].Bar == bar) return true;
            return false;
        }

        static bool HasPlayer(MarimbaState* s, int bar)
        {
            for (int p = 0; p < MarimbaConst.Players; p++) if (s->Players[p].On != 0 && s->Players[p].Bar == bar) return true;
            return false;
        }

        static void Apply(MarimbaState* s, in MarimbaAction a, int i)
        {
            if (a.Kind == MarimbaAction.Inject)
            {
                MarimbaStrikeRecord* rec = s->Strikes + a.Id;
                for (int l = 0; l < L; l++)
                {
                    if (s->Live[i * L + l] == 0) continue;
                    ModalGroup* g = s->Groups + 2 * i + (l >> 2);
                    int c = l & 3;
                    ((float*)&g->Zr)[c] += rec->InjR[l];
                    ((float*)&g->Zi)[c] += rec->InjI[l];
                }
                s->Injections++;
            }
            else if (a.Kind == MarimbaAction.Poles)
            {
                MarimbaDampRecord* rec = s->Damps + a.Id;
                for (int l = 0; l < L; l++)
                {
                    ModalGroup* g = s->Groups + 2 * i + (l >> 2);
                    int c = l & 3;
                    ((float*)&g->Pr)[c] = rec->Pr[l];
                    ((float*)&g->Pi)[c] = rec->Pi[l];
                }
                s->Damped[i] = 1;
            }
            else if (a.Kind == MarimbaAction.Undamp)
            {
                RestorePoles(s, i);
                s->Damped[i] = 0;
            }
        }

        /// <summary>Free decay of two bars (two MB4 groups each) over [0, frames): bar A into accA, bar B into accB. The four
        /// groups are interleaved in registers (the kernels' latency is hidden, as DspKernels.ModalFree4), and per element
        /// every operation is ModalFree1's, in its order: acc = mad(go0, zi0, acc) then mad(go1, zi1, acc).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void FreeTwoBars(ModalGroup* a, float4* accA, ModalGroup* b, float4* accB, int frames)
        {
            float4 zr0 = a[0].Zr, zi0 = a[0].Zi, pr0 = a[0].Pr, pi0 = a[0].Pi, go0 = a[0].Go, np0 = -pi0;
            float4 zr1 = a[1].Zr, zi1 = a[1].Zi, pr1 = a[1].Pr, pi1 = a[1].Pi, go1 = a[1].Go, np1 = -pi1;
            float4 zr2 = b[0].Zr, zi2 = b[0].Zi, pr2 = b[0].Pr, pi2 = b[0].Pi, go2 = b[0].Go, np2 = -pi2;
            float4 zr3 = b[1].Zr, zi3 = b[1].Zi, pr3 = b[1].Pr, pi3 = b[1].Pi, go3 = b[1].Go, np3 = -pi3;
            for (int n = 0; n < frames; n++)
            {
                float4 t0 = math.mad(pr0, zi0, pi0 * zr0); zr0 = math.mad(pr0, zr0, np0 * zi0); zi0 = t0;
                float4 t1 = math.mad(pr1, zi1, pi1 * zr1); zr1 = math.mad(pr1, zr1, np1 * zi1); zi1 = t1;
                float4 t2 = math.mad(pr2, zi2, pi2 * zr2); zr2 = math.mad(pr2, zr2, np2 * zi2); zi2 = t2;
                float4 t3 = math.mad(pr3, zi3, pi3 * zr3); zr3 = math.mad(pr3, zr3, np3 * zi3); zi3 = t3;
                float4 x = accA[n];
                x = math.mad(go0, zi0, x);
                x = math.mad(go1, zi1, x);
                accA[n] = x;
                float4 y = accB[n];
                y = math.mad(go2, zi2, y);
                y = math.mad(go3, zi3, y);
                accB[n] = y;
            }
            a[0].Zr = zr0; a[0].Zi = zi0; a[1].Zr = zr1; a[1].Zi = zi1;
            b[0].Zr = zr2; b[0].Zi = zi2; b[1].Zr = zr3; b[1].Zi = zi3;
        }

        static float* Out(int src, float* o0, float* o1, float* o2)
        {
            float* ch = o0;
            if (src == 1) ch = o1;
            else if (src == 2) ch = o2;
            return ch;
        }

        /// <summary>One block of at most ChunkFrames: render() of the reference.</summary>
        static void Block(MarimbaState* s, UnitEvent* due, int nDue, float* o0, float* o1, float* o2, long blockStart, int frames, int budget, float share)
        {
            long end = blockStart + frames;
            // ---- 2. due events
            for (int k = 0; k < nDue; k++)
            {
                UnitEvent* ev = due + k;
                long at = math.max(ev->At, blockStart);
                byte op = (byte)ev->C.Op;
                if (op == MarimbaConst.OpStrike)
                {
                    int id = ev->C.Id & (MarimbaConst.StrikeSlots - 1);
                    MarimbaStrikeRecord* rec = s->Strikes + id;
                    int pf = -1;
                    for (int p = 0; p < MarimbaConst.Players; p++) if (s->Players[p].On == 0) { pf = p; break; }
                    if (pf < 0) { s->DroppedPlayers++; continue; }
                    s->Players[pf].On = 1; s->Players[pf].Rec = id; s->Players[pf].Start = at; s->Players[pf].Bar = rec->Bar;
                    AddAction(s, at + rec->Nh, ev->Seq, MarimbaAction.Inject, rec->Bar, id);
                    s->StrikesPlayed++;
                }
                else if (op == MarimbaConst.OpDamp)
                {
                    int id = ev->C.Id & (MarimbaConst.DampSlots - 1);
                    AddAction(s, at, ev->Seq, MarimbaAction.Poles, s->Damps[id].Bar, id);
                }
                else if (op == MarimbaConst.OpUndamp)
                {
                    int bar = ev->C.N;
                    if ((uint)bar < NB) AddAction(s, at, ev->Seq, MarimbaAction.Undamp, bar, 0);
                }
                else if (op == MarimbaConst.OpSilence)
                {
                    for (int i = 0; i < NB; i++) if (s->Active[i] != 0) s->Fading[i] = 1;
                    for (int p = 0; p < MarimbaConst.Players; p++) s->Players[p].On = 0;
                    s->ActionCount = 0;
                }
            }
            // ---- 3. activate on actions, cap the active set (the quietest bar without an action fades over this block)
            for (int k = 0; k < s->ActionCount; k++) if (s->Actions[k].At < end) s->Active[s->Actions[k].Bar] = 1;
            int nAct = 0;
            for (int i = 0; i < NB; i++) if (s->Active[i] != 0 && s->Fading[i] == 0) nAct++;
            int cap = MarimbaConst.ActiveCap(budget, share);
            while (nAct > cap)
            {
                int q = -1;
                float qe = float.MaxValue;   // finite: FloatMode.Fast assumes no infinities
                for (int i = 0; i < NB; i++)
                    if (s->Active[i] != 0 && s->Fading[i] == 0 && s->Energy[i] < qe && !HasActionBefore(s, i, end)) { qe = s->Energy[i]; q = i; }
                if (q < 0) break;
                s->Fading[q] = 1; nAct--; s->Capped++;
            }
            if (nAct > s->MaxActive) s->MaxActive = nAct;
            // ---- 4. the bars. Two bars with no action in this block render together (four MB4 groups in flight, each
            // bar into its own accumulator: per element the same operations in the same order as two ModalFree calls,
            // so the result is unchanged to the bit); the outputs still reach the sources in ascending bar order.
            int work = 0, activeBars = 0;
            float4* acc = s->Acc;
            float* bo = s->BarOut;
            float* energy2 = stackalloc float[2];
            int pendingBar = -1;   // a later bar already rendered into Acc2 in this block
            for (int i = 0; i < NB; i++)
            {
                if (s->Active[i] == 0) continue;
                work += s->LiveCount[i];
                ModalGroup* g0 = s->Groups + 2 * i;
                float4* barAcc = acc;
                if (pendingBar == i) { barAcc = s->Acc2; pendingBar = -1; }
                else
                {
                    int pair = -1;
                    if (!HasActionBefore(s, i, end))
                        for (int j = i + 1; j < NB; j++)
                            if (s->Active[j] != 0) { if (!HasActionBefore(s, j, end)) pair = j; break; }
                    if (pair >= 0)
                    {
                        FreeTwoBars(g0, acc, s->Groups + 2 * pair, s->Acc2, frames);
                        pendingBar = pair;
                    }
                    else
                    {
                        int cursor = 0;
                        while (true)
                        {
                            int k = -1;
                            for (int a = 0; a < s->ActionCount; a++) if (s->Actions[a].Bar == i && s->Actions[a].At < end) { k = a; break; }
                            if (k < 0) break;
                            MarimbaAction act = s->Actions[k];
                            for (int a = k + 1; a < s->ActionCount; a++) s->Actions[a - 1] = s->Actions[a];
                            s->ActionCount--;
                            int off = (int)(act.At - blockStart);
                            if (off > cursor) { DspKernels.ModalFree(g0, 2, acc, cursor, off); cursor = off; }
                            Apply(s, act, i);
                        }
                        if (frames > cursor) DspKernels.ModalFree(g0, 2, acc, cursor, frames);
                    }
                }
                for (int n = 0; n < frames; n++) bo[n] = 0f;
                DspKernels.AccResolve(barAcc, bo, 0, frames);
                float* ch = Out(s->Src[i], o0, o1, o2);
                bool xf = s->Crossfade != 0 && s->Src2[i] != 0xFF;
                float* ch2 = o0;
                float g1 = 1f, g2 = 0f;
                if (xf) { ch2 = Out(s->Src2[i], o0, o1, o2); g1 = s->G1[i]; g2 = s->G2[i]; }
                if (s->Fading[i] != 0)
                {
                    float dg = 1f / frames;
                    if (!xf) for (int n = 0; n < frames; n++) ch[n] = math.mad(bo[n], 1f - n * dg, ch[n]);
                    else
                        for (int n = 0; n < frames; n++)
                        {
                            float w = 1f - n * dg;
                            ch[n] = math.mad(bo[n] * g1, w, ch[n]);
                            ch2[n] = math.mad(bo[n] * g2, w, ch2[n]);
                        }
                    ZeroBar(s, i); s->Fading[i] = 0; s->Active[i] = 0;
                    continue;
                }
                if (!xf) for (int n = 0; n < frames; n++) ch[n] += bo[n];
                else for (int n = 0; n < frames; n++) { ch[n] = math.mad(bo[n], g1, ch[n]); ch2[n] = math.mad(bo[n], g2, ch2[n]); }
                DspKernels.ModalEndBlock(g0, 2, energy2);
                float en = math.max(energy2[0], energy2[1]);
                s->Energy[i] = en;
                float s2 = 0f;
                for (int n = 0; n < frames; n++) s2 += bo[n] * bo[n];
                Volatile.Write(ref s->Level[i], math.sqrt(s2 / frames));
                if (en < DspKernels.CullEnergy && !HasAction(s, i) && !HasPlayer(s, i)) { ZeroBar(s, i); s->Active[i] = 0; s->Retired++; continue; }
                activeBars++;
            }
            // ---- 5. strike records (contact sound + knock) on their bar's source
            for (int p = 0; p < MarimbaConst.Players; p++)
            {
                MarimbaPlayer* pl = s->Players + p;
                if (pl->On == 0) continue;
                MarimbaStrikeRecord* rec = s->Strikes + pl->Rec;
                float* ch = Out(s->Src[pl->Bar], o0, o1, o2);
                int from = (int)math.max(0L, pl->Start - blockStart);
                int j0 = (int)(blockStart + from - pl->Start);
                int n1 = math.min(frames, from + (rec->Len - j0));
                float* seg = rec->Seg;
                if (s->Crossfade != 0 && s->Src2[pl->Bar] != 0xFF)
                {
                    float* ch2 = Out(s->Src2[pl->Bar], o0, o1, o2);
                    float g1 = s->G1[pl->Bar], g2 = s->G2[pl->Bar];
                    for (int n = from, j = j0; n < n1; n++, j++) { ch[n] = math.mad(seg[j], g1, ch[n]); ch2[n] = math.mad(seg[j], g2, ch2[n]); }
                }
                else for (int n = from, j = j0; n < n1; n++, j++) ch[n] += seg[j];
                if (pl->Start + rec->Len <= end) pl->On = 0;
                s->Active[pl->Bar] = 1;
            }
            s->Work = work;          // live lanes rendered this block (UnitSlot.Work)
            s->ActiveBars = activeBars;
        }
    }
}
