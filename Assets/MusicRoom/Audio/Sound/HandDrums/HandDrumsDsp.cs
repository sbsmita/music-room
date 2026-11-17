// The hand drums' audio-thread side (handdrums.md 2.3-2.13): a transliteration of the Node reference
// (Tools/SoundLab/handdrums/engine/handdrums.reference.mjs, v2: HandDrums.process, Drum.apply / contact / segment / control /
// updatePoles / beginBlock / endBlock) onto the engine's MB4 layout (ModalGroup) and its canonical mad order
// (DspKernels.ModalFree4 / ModalDriven, lab/kernels.mjs). Every double expression keeps the reference's order of
// operations, so the managed path (and a Burst FloatMode.Strict entry) is bit-identical to Node; Burst FloatMode.Fast
// (the graph's mode) fuses the mads and stays within the spec's tolerances (6.1).
//
// Each drum is one persistent modal object: patches (hand parts on a Hunt-Crossley flesh spring, solved per sample in
// double while they touch) drive its modes through their force difference (DC-free), the cavity group through the air
// pop, a resting hand damps it, the tension glide bends its pitch, and the budget sheds its high groups first.
//
// Rules (ENGINE-API 3.4): no allocation, no managed objects, no exceptions, no Unity API. The MB4 free and driven loops
// are written here (same recurrence as DspKernels, four groups interleaved for the free decay) rather than called in
// DspKernels, so a managed call of Render stays managed (the bit-exact test) instead of jumping into Burst code.
using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    [BurstCompile]
    public static unsafe class HandDrumsDsp
    {
        const double Tau = 2 * Math.PI;
        const double Db = 8.685889638065035;   // 20 / ln 10

        // ------------------------------------------------------------------ the block (UnitHooks.RenderHandDrums in phase B)

        /// <summary>One block (UnitHooks.RenderHandDrums): u->State is a HandDrumsState, u->Due the block's events sorted by (At,
        /// Seq), drum d added into u->Channel(1 - d). The LOD budget is u->Graph->Budget (1200 when there is no graph), the
        /// share UnitSlot.Share (the governor's) x HandDrumsState.Share (1; the standalone host's tests set it). The unit's
        /// work count goes to u->Work. UnitFlags.Resumed (after an idle skip) needs nothing: the drums were at rest and the
        /// control grid is absolute.</summary>
        public static void Render(UnitSlot* u)
        {
            var s = (HandDrumsState*)u->State;
            if (s == null || s->Ready == 0 || u->Channels < 2) return;
            if ((u->Flags & UnitFlags.Reset) != 0) { ResetDrum(&s->D0); ResetDrum(&s->D1); s->Resets++; }
            double budget = 1200;
            if (u->Graph != null) budget = u->Graph->Budget;
            double share = (double)s->Share * u->Share;
            RenderCore(s, u->Due, u->DueCount, u->Channel(HandDrumsConstChannel(0)), u->Channel(HandDrumsConstChannel(1)), u->BlockStart, u->Frames, budget, share);
            u->Work = s->Work;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int HandDrumsConstChannel(int drum) => 1 - drum;

        /// <summary>HandDrums.process: budget scaling at block start, then each drum in drum order with its due events.</summary>
        public static void RenderCore(HandDrumsState* s, UnitEvent* due, int dueCount, float* outConga, float* outTumba, long b0, int n, double budget, double share)
        {
            if (n <= 0) return;
            if (n > HandDrumsConst.MaxFrames) n = HandDrumsConst.MaxFrames;
            HandDrumsDrum* d0 = &s->D0, d1 = &s->D1;
            int cap = JsRound(s->GTot * (budget / 1200) * share);
            if (cap < HandDrumsConst.CapMin) cap = HandDrumsConst.CapMin;
            if (cap > s->GTot) cap = s->GTot;
            double e0 = d0->Energy, e1 = d1->Energy;
            if (s->Louder == 0 && e1 > HandDrumsConst.LouderHyst * e0) s->Louder = 1;
            else if (s->Louder == 1 && e0 > HandDrumsConst.LouderHyst * e1) s->Louder = 0;
            s->Cap = cap;
            Masks(s);
            double fadeMul = Math.Exp((HandDrumsConst.FadeDb / Db) / n);
            BeginBlock(s, d0, fadeMul);
            BeginBlock(s, d1, fadeMul);
            int work = 0;
            for (int d = 0; d < 2; d++)
            {
                HandDrumsDrum* dr = d0;
                float* o = outConga;
                if (d == 1) { dr = d1; o = outTumba; }
                int cursor = 0;
                for (int e = 0; e < dueCount; e++)
                {
                    UnitEvent* ev = due + e;
                    byte op = (byte)ev->C.Op;
                    if (op != HandDrumsConst.OpReset && ev->C.Target != d) continue;
                    int off = ev->Offset;
                    if (off < 0) off = 0;
                    if (off > n) off = n;
                    RenderDrum(dr, o, b0, cursor, off);
                    cursor = off;
                    Apply(s, dr, ev);
                }
                RenderDrum(dr, o, b0, cursor, n);
                EndBlock(dr);
                work += dr->Work;
                dr->Work = 0;
                int active = 0;
                HandDrumsPatch* P = &dr->P0;
                for (int p = 0; p < HandDrumsConst.Slots; p++) if (P[p].On != 0) active++;
                if (active > s->MaxActivePatches) s->MaxActivePatches = active;
            }
            s->Work = work >> 5;
            s->WorkTotal += s->Work;
            s->Blocks++;
            s->Level0 = Rms(outConga, n);
            s->Level1 = Rms(outTumba, n);
        }

        static float Rms(float* x, int n)
        {
            double e = 0;
            for (int i = 0; i < n; i++) e += (double)x[i] * x[i];
            return (float)Math.Sqrt(e / n);
        }

        /// <summary>JS Math.round for the values used here (non-negative): floor(x + 0.5).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int JsRound(double x) => (int)Math.Floor(x + 0.5);

        // ------------------------------------------------------------------ budget scaling (platform 10.3; handdrums.md 2.13)

        /// <summary>The shed masks for this cap and this louder drum: the quieter drum's dense, then high head groups, then
        /// the louder drum's (only when either changes).</summary>
        static void Masks(HandDrumsState* s)
        {
            int key = s->Cap * 2 + s->Louder;
            if (key == s->MaskKey) return;
            s->MaskKey = key;
            HandDrumsDrum* d0 = &s->D0, d1 = &s->D1;
            for (int g = 0; g < d0->G; g++) d0->Allowed[g] = 1;
            for (int g = 0; g < d1->G; g++) d1->Allowed[g] = 1;
            int nShed = s->GTot - s->Cap;
            HandDrumsDrum* q = d1, L = d0;
            if (s->Louder == 1) { q = d0; L = d1; }
            for (int i = 0; i < q->ShedCount && nShed > 0; i++, nShed--) q->Allowed[q->ShedOrder[i]] = 0;
            for (int i = 0; i < L->ShedCount && nShed > 0; i++, nShed--) L->Allowed[L->ShedOrder[i]] = 0;
        }

        static void BeginBlock(HandDrumsState* s, HandDrumsDrum* d, double fadeMul)
        {
            int any = 0;
            for (int g = 0; g < d->G; g++)
                if (d->Act[g] != 0 && d->Allowed[g] == 0) { d->Fading[g] = 1; any = 1; s->ShedGroups++; }
            if (any != 0) { d->AnyFading = 1; d->FadeMul = fadeMul; UpdatePoles(d, true); }
        }

        static void EndBlock(HandDrumsDrum* d)
        {
            if (d->AnyFading == 0) return;
            for (int g = 0; g < d->G; g++)
            {
                if (d->Fading[g] == 0) continue;
                d->Bank[g].Zr = float4.zero; d->Bank[g].Zi = float4.zero;
                d->Act[g] = 0; d->Fading[g] = 0;
            }
            d->AnyFading = 0;
            UpdatePoles(d, true);
        }

        // ------------------------------------------------------------------ events

        static void Apply(HandDrumsState* s, HandDrumsDrum* d, UnitEvent* ev)
        {
            GraphCommand* c = &ev->C;
            switch ((byte)c->Op)
            {
                case HandDrumsConst.OpReset:
                    ResetDrum(d);
                    if (c->N != 0) d->Rng = HandDrumsSeed(c->Seed, d == &s->D0 ? 0 : 1);
                    return;
                case HandDrumsConst.OpLift:
                    Lift(d, c->N & 1);
                    return;
                case HandDrumsConst.OpRest:
                {
                    int hand = c->N & 1, np = (c->N >> 4) & 15;
                    if (np > 3) np = 3;
                    // A..F: rho0 theta0 rho1 theta1 rho2 theta2
                    double r0 = c->A, t0 = c->B, r1 = c->C, t1 = c->D, r2 = c->E, t2 = c->F;
                    int p0 = (c->N >> 8) & 15, p1 = (c->N >> 12) & 15, p2 = (c->N >> 16) & 15;
                    BeginRest(d);
                    if (np > 0) RestPoint(d, p0, r0, t0);
                    if (np > 1) RestPoint(d, p1, r1, t1);
                    if (np > 2) RestPoint(d, p2, r2, t2);
                    EndRest(d, hand);
                    return;
                }
                case HandDrumsConst.OpRestCanon:
                {
                    int hand = c->N & 1, kind = (c->N >> 4) & 15;
                    double th = ((double*)&c->C)[0];
                    BeginRest(d);
                    CanonRest(d, kind, th);
                    EndRest(d, hand);
                    return;
                }
                case HandDrumsConst.OpPatch:
                {
                    int hand = c->N & 1, part = (c->N >> 4) & 15, shape = (c->N >> 8) & 15, count = (c->N >> 12) & 15;
                    double* raw = stackalloc double[2 * HandDrumsConst.MaxPts];
                    int np;
                    if (shape == (int)PatchShape.Line)
                    {
                        double ra = c->B, ta = c->C, rb = c->D, tb = c->E;
                        for (int k = 0; k < HandDrumsConst.MaxPts; k++) { raw[2 * k] = ra + (rb - ra) * (k / 3.0); raw[2 * k + 1] = ta + (tb - ta) * (k / 3.0); }
                        np = HandDrumsConst.MaxPts;
                    }
                    else
                    {
                        np = count < 1 ? 1 : count > 3 ? 3 : count;
                        raw[0] = c->B; raw[1] = c->C; raw[2] = c->D; raw[3] = c->E; raw[4] = c->F; raw[5] = c->G;
                    }
                    ApplyPatch(s, d, hand, part, c->A, raw, np);
                    return;
                }
                case HandDrumsConst.OpStrokePatch:
                {
                    int hand = c->N & 1, kind = (c->N >> 4) & 15, idx = (c->N >> 8) & 15;
                    double* dd = (double*)&c->A;   // A:B the stroke's u (double), C:D the theta offset (double)
                    CanonPatch(s, d, hand, kind, idx, dd[0], dd[1], c->E);
                    return;
                }
            }
        }

        /// <summary>The reference's per-drum seed from a session seed (HandDrums constructor).</summary>
        static uint HandDrumsSeed(uint seed, int d)
        {
            double x = seed * 2654435761.0 + 2654435769.0 * (d + 1);
            return (uint)(ulong)x;
        }

        /// <summary>op 'reset' (and UnitFlags.Reset): everything silent, no rests, no glide; the RNG keeps going.</summary>
        static void ResetDrum(HandDrumsDrum* d)
        {
            for (int g = 0; g < d->G; g++) { d->Bank[g].Zr = float4.zero; d->Bank[g].Zi = float4.zero; d->Act[g] = 0; d->Fading[g] = 0; }
            HandDrumsPatch* P = &d->P0;
            for (int p = 0; p < HandDrumsConst.Slots; p++) P[p].On = 0;
            for (int k = 0; k < d->NPad; k++) { d->DMul0[k] = 1; d->DMul1[k] = 1; }
            d->RestOn0 = d->RestOn1 = 0;
            d->S = 1; d->CavMul = 1;
            UpdatePoles(d, true);
            d->AnyFading = 0; d->Idle = 1; d->Energy = 0;
        }

        static void Lift(HandDrumsDrum* d, int hand)
        {
            double* dm = d->DMul0;
            if (hand == 1) dm = d->DMul1;
            for (int k = 0; k < d->NPad; k++) dm[k] = 1;
            if (hand == 1) d->RestOn1 = 0; else d->RestOn0 = 0;
            d->PolesDirty = 1;
        }

        static void BeginRest(HandDrumsDrum* d) { for (int k = 0; k < d->NPad; k++) d->DAl[k] = 0; }

        /// <summary>dAlpha_k += R (phi_k A_k)^2 / (2 M_k) of one resting point (shell and cavity modes: 0).</summary>
        static void RestPoint(HandDrumsDrum* d, int part, double rho, double theta)
        {
            if ((uint)part >= HandDrumsConst.Parts) return;
            double R = HandDrumsConst.PartR[part] * 1;
            Angles(d, 0, theta);
            double* da = d->DAl;
            for (int k = 0; k < d->N; k++)
            {
                int kd = d->Kind[k];
                if (kd == HandDrumsConst.KindShell || kd == HandDrumsConst.KindCav) continue;
                double ph = PhiAt(d, k, 0, rho) * Avg(d, k, part);
                da[k] += (R * ph * ph) / (2 * d->Mk[k]);
            }
        }

        static void EndRest(HandDrumsDrum* d, int hand)
        {
            double* dm = d->DMul0;
            if (hand == 1) dm = d->DMul1;
            double sr = HandDrumsConst.Sr;
            for (int k = 0; k < d->NPad; k++) dm[k] = d->DAl[k] > 0 ? Math.Exp(-d->DAl[k] / sr) : 1;
            if (hand == 1) d->RestOn1 = 1; else d->RestOn0 = 1;
            d->PolesDirty = 1;
        }

        /// <summary>RESTS (handdrums.md 2.10) + the renders' toe pads, turned by th.</summary>
        static void CanonRest(HandDrumsDrum* d, int kind, double th)
        {
            switch (kind)
            {
                case (int)HandRest.Mute: RestPoint(d, (int)HandPart.Fingers, 0.75, 0 + th); RestPoint(d, (int)HandPart.Fingers, 0.6, 0 + th); break;
                case (int)HandRest.SlapClosed: RestPoint(d, (int)HandPart.Tips, 0.56, 0 + th); RestPoint(d, (int)HandPart.Base, 0.94, 0 + th); break;
                case (int)HandRest.Heel: RestPoint(d, (int)HandPart.Heel, 0.30, 0 + th); break;
                case (int)HandRest.Palm: RestPoint(d, (int)HandPart.Palm, 0.15, 0 + th); break;
                case (int)HandRest.Toe: RestPoint(d, (int)HandPart.Pads, 0.5, Math.PI + th); break;
            }
        }

        /// <summary>STROKES (handdrums.md 2.10): patch idx of the canonical stroke kind (Mute = Open, SlapClosed = Slap), the
        /// stroke's u and theta offset in double, exactly as playStroke posts them.</summary>
        static void CanonPatch(HandDrumsState* s, HandDrumsDrum* d, int hand, int kind, int idx, double u, double th, double rhoOff)
        {
            double* p = stackalloc double[2 * HandDrumsConst.MaxPts];
            int part, np;
            double pu;
            switch (kind)
            {
                case (int)HandStroke.Open:
                case (int)HandStroke.Mute:
                    part = (int)HandPart.Fingers; pu = u; np = 4; Line(p, 0.97, 0.50, 0, th); break;
                case (int)HandStroke.Bass:
                    if (idx == 0) { part = (int)HandPart.Palm; pu = u; np = 1; p[0] = 0.10; p[1] = 0 + th; }
                    else { part = (int)HandPart.Pads; pu = u * HandDrumsConst.BassPadsU; np = 1; p[0] = 0.62; p[1] = Math.PI + th; }
                    break;
                case (int)HandStroke.Slap:
                case (int)HandStroke.SlapClosed:
                    if (idx == 0) { part = (int)HandPart.Base; pu = u * 0.8; np = 1; p[0] = 0.94; p[1] = 0 + th; }
                    else { part = (int)HandPart.Tips; pu = u; np = 3; p[0] = 0.58; p[1] = -0.26 + th; p[2] = 0.55; p[3] = 0 + th; p[4] = 0.58; p[5] = 0.26 + th; }
                    break;
                case (int)HandStroke.Heel: part = (int)HandPart.Heel; pu = u; np = 1; p[0] = 0.30; p[1] = 0 + th; break;
                case (int)HandStroke.Toe: part = (int)HandPart.Pads; pu = u; np = 4; Line(p, 0.62, 0.40, Math.PI, th); break;
                case (int)HandStroke.Touch: part = (int)HandPart.Pads; pu = u; np = 1; p[0] = 0.75; p[1] = 0 + th; break;
                default: return;
            }
            if (rhoOff != 0) for (int i = 0; i < np; i++) { double r = p[2 * i] + rhoOff; p[2 * i] = r < 0 ? 0 : r > 1.2 ? 1.2 : r; }
            ApplyPatch(s, d, hand, part, pu, p, np);
        }

        /// <summary>line(r0, r1, th) of the reference, each theta then turned by the stroke's offset.</summary>
        static void Line(double* p, double r0, double r1, double th, double off)
        {
            for (int i = 0; i < HandDrumsConst.MaxPts; i++) { p[2 * i] = r0 + (r1 - r0) * (i / 3.0); p[2 * i + 1] = th + off; }
        }

        /// <summary>partV(part, u): Vmin + (Vmax - Vmin) u^1.1, the palm and the heel capped at cap x handSpeed(u, palm).</summary>
        public static double PartV(int part, double u)
        {
            double c = u < 0 ? 0 : u > 1 ? 1 : u;
            double V = HandDrumsConst.PartVmin[part] + (HandDrumsConst.PartVmax[part] - HandDrumsConst.PartVmin[part]) * Math.Pow(c, 1.1);
            double cap = HandDrumsConst.PartCap[part];
            if (cap != 0) V = Math.Min(V, cap * HandSpeed(c, true));
            return V;
        }

        /// <summary>handSpeed(u, g): V0g + (V1 - V0g) u^(1/0.7), the inverse of the input logic's u law.</summary>
        public static double HandSpeed(double u, bool palm)
        {
            double c = u < 0 ? 0 : u > 1 ? 1 : u, v0 = palm ? HandDrumsConst.UV0Palm : HandDrumsConst.UV0Fingers;
            return v0 + (HandDrumsConst.UV1 - v0) * Math.Pow(c, 1 / HandDrumsConst.UExp);
        }

        /// <summary>Drum.apply for a patch: the micro-variation in the reference's draw order (V, rho, theta, k, pop length,
        /// cavity, then one rho per point), the slot (first free, else the oldest is stolen), the bearing-edge clamp, the local
        /// head reaction and the per-mode input gains. pts: np (rho, theta) pairs.</summary>
        static void ApplyPatch(HandDrumsState* s, HandDrumsDrum* d, int hand, int part, double u, double* pts, int np)
        {
            if ((uint)part >= HandDrumsConst.Parts) return;
            if (np > HandDrumsConst.MaxPts) np = HandDrumsConst.MaxPts;
            if (np < 1) return;
            uint* r = &d->Rng;
            double V = PartV(part, u) * (1 + 0.05 * Bip(r));
            double dRho = 0.04 * Bip(r), dTh = 0.25 * Bip(r), kMul = 1 + 0.2 * Bip(r);
            int popLen = HandDrumsConst.PopMin + (int)Math.Floor(RngNext(r) * (HandDrumsConst.PopMax - HandDrumsConst.PopMin + 1));
            double cavMul = 1 + 0.08 * Bip(r);
            HandDrumsPatch* P = &d->P0;
            int slot = -1;
            for (int p = 0; p < HandDrumsConst.Slots; p++) if (P[p].On == 0) { slot = p; break; }
            if (slot < 0)
            {
                int best = 0;
                for (int p = 1; p < HandDrumsConst.Slots; p++) if (P[p].Age > P[best].Age) best = p;
                slot = best;
                s->PatchesStolen++;
            }
            HandDrumsPatch* S = P + slot;
            double b = HandDrumsConst.PartB[part], a = d->A;
            double rimMax = 1 - (0.5 * b) / a, rimOut = 1 + (0.5 * b) / a;
            double rhoC = 0;
            for (int i = 0; i < np; i++)
            {
                double qr = pts[2 * i], qt = pts[2 * i + 1];
                double rr = qr > rimMax && qr < rimOut ? rimMax : qr;
                rr += dRho + 0.02 * Bip(r);
                if (rr < 0) rr = 0;
                d->Pr[i] = rr;
                d->Pt[i] = qt + dTh;
                rhoC += rr < 1 ? rr : 1;
                Angles(d, i, d->Pt[i]);
            }
            rhoC /= np;
            // headReaction(t, rhoC, b): the head's local static stiffness and wave impedance under the patch
            double dRim = Math.Max(0.25 * b, a * (1 - Math.Min(rhoC, 1)));
            double Kh = (Tau * d->T) / Math.Log(1 + dRim / b);
            double Zh = Tau * b * Math.Sqrt(d->T * d->Sigma);
            double y2 = HandDrumsConst.Y2Rel * a;
            double cup = HandDrumsConst.PartCup[part];
            if (cup > 0) { d->CavMul = cavMul; d->GlideDirty = 1; }
            double alpha = HandDrumsConst.PartAlpha[part];
            S->PopLen = popLen; S->On = 1; S->Started = 0; S->Part = part; S->Hand = hand;
            S->M = HandDrumsConst.PartM[part]; S->K = HandDrumsConst.PartK[part] * kMul;
            S->An = JsRound(alpha - 0.5); S->Alpha = alpha; S->Lam = HandDrumsConst.PartLam[part];
            S->Kh = Kh; S->Zh = Zh; S->Iy2 = 1 / y2;
            S->X = 0; S->V = V; S->Y = 0; S->Yd = 0; S->F = 0; S->Fp = 0; S->Flight = 0; S->Age = 0; S->Fmax = 0;
            S->TFirst = -1; S->TLast = -1; S->Cup = cup; S->V0 = V; S->PopLeft = 0; S->PopA = 0;
            // the radial table position of each point (PhiAt's x, j, fr: the same for every mode)
            int* pj = stackalloc int[HandDrumsConst.MaxPts];
            double* pf = stackalloc double[HandDrumsConst.MaxPts];
            for (int i = 0; i < np; i++)
            {
                double x = d->Pr[i] * (HandDrumsConst.ShapeN - 1), jd = Math.Floor(x);
                if (jd > HandDrumsConst.ShapeN - 2) jd = HandDrumsConst.ShapeN - 2;
                pj[i] = (int)jd; pf[i] = x - jd;
            }
            float* gi = S->Gi;
            for (int k = 0; k < d->NPad; k++)
            {
                if (k >= d->N) { gi[k] = 0; continue; }
                double g;
                if (d->Kind[k] == HandDrumsConst.KindCav) g = cup;   // driven by the pop stream, not by dF
                else
                {
                    double sph = 0;
                    for (int i = 0; i < np; i++) sph += PhiAtPre(d, k, i, d->Pr[i], pj[i], pf[i]);
                    g = (sph / np) * Avg(d, k, part);
                }
                gi[k] = (float)(g / (d->Mk[k] * d->Om[k]));
            }
            for (int g = 0; g < d->G; g++) if (d->Allowed[g] != 0) d->Act[g] = 1;
            d->Idle = 0;
            s->Strokes++;
        }

        /// <summary>Tests and probes: apply one patch now, as the reference's eng.patch(at, drum, hand, part, u, [{ rho, theta }])
        /// at the start of the next block.</summary>
        public static void ApplyPatchNow(HandDrumsState* s, int drum, int hand, HandPart part, double u, double rho, double theta)
        {
            double* p = stackalloc double[2];
            p[0] = rho; p[1] = theta;
            HandDrumsDrum* d = &s->D0;
            if (drum == 1) d = &s->D1;
            ApplyPatch(s, d, hand, (int)part, u, p, 1);
        }

        /// <summary>headReaction(t, rho, b): the head's static point stiffness and wave impedance under a patch.</summary>
        public static void HeadReaction(HandDrumsDrum* d, double rho, double b, out double kh, out double zh)
        {
            double dRim = Math.Max(0.25 * b, d->A * (1 - Math.Min(rho, 1)));
            kh = (Tau * d->T) / Math.Log(1 + dRim / b);
            zh = Tau * b * Math.Sqrt(d->T * d->Sigma);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double Avg(HandDrumsDrum* d, int k, int part) => d->AvgTab[part * d->NPad + k];

        /// <summary>cos / sin (m theta), m = 0..24, of point i (Chebyshev recurrence: two trig calls per point).</summary>
        static void Angles(HandDrumsDrum* d, int i, double th)
        {
            const int W = HandDrumsConst.MaxM + 1;
            int o = i * W;
            double c1 = Math.Cos(th), s1 = Math.Sin(th);
            double* cm = d->Cm, sm = d->Sm;
            cm[o] = 1; sm[o] = 0; cm[o + 1] = c1; sm[o + 1] = s1;
            for (int m = 2; m <= HandDrumsConst.MaxM; m++) { cm[o + m] = 2 * c1 * cm[o + m - 1] - cm[o + m - 2]; sm[o + m] = 2 * c1 * sm[o + m - 1] - sm[o + m - 2]; }
        }

        /// <summary>Mode k's shape at point i (rho, with Angles(i) done): the radial table (linear interpolation) times the
        /// angular factor; rho &gt;= 1 is off the head for head modes; shell modes take the rim share, the cavity none.</summary>
        static double PhiAt(HandDrumsDrum* d, int k, int i, double rho)
        {
            int kd = d->Kind[k];
            if (kd == HandDrumsConst.KindCav) return 0;
            if (kd == HandDrumsConst.KindShell) return RimShare(rho < 1 ? rho : 1);
            if (rho >= 1) return 0;
            double x = rho * (HandDrumsConst.ShapeN - 1);
            double jd = Math.Floor(x);
            if (jd > HandDrumsConst.ShapeN - 2) jd = HandDrumsConst.ShapeN - 2;
            int j = (int)jd;
            double fr = x - jd;
            float* sh = d->Shape + k * HandDrumsConst.ShapeN;
            double bse = sh[j] * (1 - fr) + sh[j + 1] * fr;
            int m = d->Mm[k], o = i * (HandDrumsConst.MaxM + 1) + m;
            if (kd == HandDrumsConst.KindDense) return bse * (0.75 + 0.25 * (d->Cm[o] * d->CPh[k] - d->Sm[o] * d->SPh[k]));
            if (kd != HandDrumsConst.KindMem || m == 0) return bse;
            return bse * (d->Orient[k] == 0 ? d->Cm[o] : d->Sm[o]);
        }

        /// <summary>PhiAt with the point's table position precomputed (j = floor(rho 64) clamped, fr = rho 64 - j): same values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double PhiAtPre(HandDrumsDrum* d, int k, int i, double rho, int j, double fr)
        {
            int kd = d->Kind[k];
            if (kd == HandDrumsConst.KindCav) return 0;
            if (kd == HandDrumsConst.KindShell) return RimShare(rho < 1 ? rho : 1);
            if (rho >= 1) return 0;
            float* sh = d->Shape + k * HandDrumsConst.ShapeN;
            double bse = sh[j] * (1 - fr) + sh[j + 1] * fr;
            int m = d->Mm[k], o = i * (HandDrumsConst.MaxM + 1) + m;
            if (kd == HandDrumsConst.KindDense) return bse * (0.75 + 0.25 * (d->Cm[o] * d->CPh[k] - d->Sm[o] * d->SPh[k]));
            if (kd != HandDrumsConst.KindMem || m == 0) return bse;
            return bse * (d->Orient[k] == 0 ? d->Cm[o] : d->Sm[o]);
        }

        /// <summary>The share of a force near the rim that reaches the bearing edge: 0 inside rho .7, 1 at the rim.</summary>
        public static double RimShare(double rho)
        {
            double x = (rho - 0.7) / 0.3, c = x < 0 ? 0 : x > 1 ? 1 : x;
            return c * c * (3 - 2 * c);
        }

        // ------------------------------------------------------------------ RNG (mulberry32, the spec-06 / drums generator)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double RngNext(uint* a)
        {
            uint x = *a + 0x6d2b79f5u;
            *a = x;
            uint t = x;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return (t ^ (t >> 14)) / 4294967296.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double Bip(uint* a) => RngNext(a) * 2 - 1;

        // ------------------------------------------------------------------ poles

        /// <summary>|p| = r0 dMul_L dMul_R (x the fade of a shed group), angle w0 s (the glide, as a polynomial rotation by
        /// w0 (s - 1)); the cavity at its per-stroke detune. full: every mode (a rest, lift, fade, reset); otherwise only
        /// the glide list.</summary>
        public static void UpdatePoles(HandDrumsDrum* d, bool full)
        {
            double s1 = d->S - 1;
            double* m0 = d->DMul0, m1 = d->DMul1;
            int cnt = full ? d->N : d->GlideCount;
            for (int i = 0; i < cnt; i++)
            {
                int k = full ? i : d->GlideList[i], g = k >> 2;
                double R = d->R0[k] * m0[k] * m1[k];
                if (d->Fading[g] != 0) R *= d->FadeMul;
                double cr = d->Cr0[k], ci = d->Ci0[k];
                if (d->Kind[k] == HandDrumsConst.KindCav && d->CavMul != 1) { double w = d->W0[k] * d->CavMul; cr = Math.Cos(w); ci = Math.Sin(w); }
                if (d->GlideOn[k] != 0 && s1 != 0)
                {
                    double dl = d->W0[k] * s1, d2 = dl * dl;
                    double cd = 1 - d2 * (0.5 - d2 / 24), sd = dl * (1 - d2 * (1.0 / 6 - d2 / 120));
                    double nr = cr * cd - ci * sd, ni = ci * cd + cr * sd;
                    cr = nr; ci = ni;
                }
                float* b = (float*)(d->Bank + g);
                int l = k & 3;
                b[8 + l] = (float)(R * cr);
                b[12 + l] = (float)(R * ci);
            }
            if (full) d->PolesDirty = 0;
            d->GlideDirty = 0;
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>Drum.render: [i0, i1) of the block that starts at b0, split at the absolute 32-sample grid; the control
        /// step runs at every grid point (also when idle, as the reference).</summary>
        static void RenderDrum(HandDrumsDrum* d, float* o, long b0, int i0, int i1)
        {
            int i = i0;
            while (i < i1)
            {
                long abs = b0 + i;
                int toGrid = HandDrumsConst.Sub - (int)(abs % HandDrumsConst.Sub);
                int j = math.min(i1, i + toGrid);
                if (d->Idle == 0) Segment(d, o, i, j);
                if ((b0 + j) % HandDrumsConst.Sub == 0) Control(d);
                i = j;
            }
        }

        static void Segment(HandDrumsDrum* d, float* o, int i0, int i1)
        {
            int len = i1 - i0;
            HandDrumsPatch* P = &d->P0;
            HandDrumsPatch* on0 = null, on1 = null, on2 = null, on3 = null;
            int nOn = 0;
            for (int p = 0; p < HandDrumsConst.Slots; p++)
            {
                HandDrumsPatch* S = P + p;
                if (S->On == 0) continue;
                if (nOn == 0) on0 = S; else if (nOn == 1) on1 = S; else if (nOn == 2) on2 = S; else on3 = S;
                nOn++;
                Contact(S, i0, i1);
            }
            int count = 0;
            for (int g = 0; g < d->G; g++) if (d->Act[g] != 0) d->ActList[count++] = g;
            float4* acc = d->Acc;
            if (nOn == 0) FreeList(d->Bank, d->ActList, count, acc, i0, i1);
            else
            {
                // the head and dense groups four at a time (their inputs are the patches' force differences), then the
                // cavity group (the patches' air pops); ascending group order either way, so the per-lane sums keep the
                // reference's order
                float* x0 = on0->DF, x1 = null, x2 = null, x3 = null;
                if (nOn > 1) x1 = on1->DF;
                if (nOn > 2) x2 = on2->DF;
                if (nOn > 3) x3 = on3->DF;
                int nHead = count;
                if (count > 0 && d->ActList[count - 1] == d->GCav) nHead = count - 1;
                int c = 0;
                for (; c + 4 <= nHead; c += 4)
                    Driven4(d->Bank, d->ActList + c, nOn, on0, on1, on2, on3, x0, x1, x2, x3, acc, i0, i1);
                for (; c < nHead; c++)
                {
                    int g = d->ActList[c];
                    DrivenOne(d->Bank + g, g, nOn, on0, on1, on2, on3, x0, x1, x2, x3, acc, i0, i1);
                }
                if (nHead < count)
                {
                    int g = d->GCav;
                    float* p0 = on0->Pop, p1 = null, p2 = null, p3 = null;
                    if (nOn > 1) p1 = on1->Pop;
                    if (nOn > 2) p2 = on2->Pop;
                    if (nOn > 3) p3 = on3->Pop;
                    DrivenOne(d->Bank + g, g, nOn, on0, on1, on2, on3, p0, p1, p2, p3, acc, i0, i1);
                }
            }
            d->Work += count * len;
            for (int n = i0; n < i1; n++)
            {
                float4 a = acc[n];
                o[n] = o[n] + ((a.x + a.y) + (a.z + a.w));
                acc[n] = float4.zero;
            }
        }

        /// <summary>MB4 free decay of bank[list[0..count)] (DspKernels.ModalFreeList, four groups interleaved).</summary>
        static void FreeList(ModalGroup* bank, int* list, int count, float4* acc, int from, int to)
        {
            int i = 0;
            for (; i + 4 <= count; i += 4) Free4(bank + list[i], bank + list[i + 1], bank + list[i + 2], bank + list[i + 3], acc, from, to);
            for (; i < count; i++) Free1(bank + list[i], acc, from, to);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Free1(ModalGroup* g, float4* acc, int from, int to)
        {
            float4 zr = g->Zr, zi = g->Zi, pr = g->Pr, pi = g->Pi, go = g->Go, npi = -pi;
            for (int n = from; n < to; n++)
            {
                float4 t = math.mad(pr, zi, pi * zr);
                zr = math.mad(pr, zr, npi * zi);
                zi = t;
                acc[n] = math.mad(go, zi, acc[n]);
            }
            g->Zr = zr; g->Zi = zi;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Free4(ModalGroup* g0, ModalGroup* g1, ModalGroup* g2, ModalGroup* g3, float4* acc, int from, int to)
        {
            float4 zr0 = g0->Zr, zi0 = g0->Zi, pr0 = g0->Pr, pi0 = g0->Pi, go0 = g0->Go, np0 = -pi0;
            float4 zr1 = g1->Zr, zi1 = g1->Zi, pr1 = g1->Pr, pi1 = g1->Pi, go1 = g1->Go, np1 = -pi1;
            float4 zr2 = g2->Zr, zi2 = g2->Zi, pr2 = g2->Pr, pi2 = g2->Pi, go2 = g2->Go, np2 = -pi2;
            float4 zr3 = g3->Zr, zi3 = g3->Zi, pr3 = g3->Pr, pi3 = g3->Pi, go3 = g3->Go, np3 = -pi3;
            for (int n = from; n < to; n++)
            {
                float4 t0 = math.mad(pr0, zi0, pi0 * zr0); zr0 = math.mad(pr0, zr0, np0 * zi0); zi0 = t0;
                float4 t1 = math.mad(pr1, zi1, pi1 * zr1); zr1 = math.mad(pr1, zr1, np1 * zi1); zi1 = t1;
                float4 t2 = math.mad(pr2, zi2, pi2 * zr2); zr2 = math.mad(pr2, zr2, np2 * zi2); zi2 = t2;
                float4 t3 = math.mad(pr3, zi3, pi3 * zr3); zr3 = math.mad(pr3, zr3, np3 * zi3); zi3 = t3;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float4 Gi(HandDrumsPatch* p, int g) => *(float4*)(p->Gi + 4 * g);

        static void DrivenOne(ModalGroup* bank, int g, int nOn, HandDrumsPatch* on0, HandDrumsPatch* on1, HandDrumsPatch* on2, HandDrumsPatch* on3,
            float* x0, float* x1, float* x2, float* x3, float4* acc, int from, int to)
        {
            float4 g0 = Gi(on0, g), g1 = float4.zero, g2 = float4.zero, g3 = float4.zero;
            if (nOn > 1) g1 = Gi(on1, g);
            if (nOn > 2) g2 = Gi(on2, g);
            if (nOn > 3) g3 = Gi(on3, g);
            Driven(bank, nOn, g0, x0, g1, x1, g2, x2, g3, x3, acc, from, to);
        }

        /// <summary>Four groups list[0..4) driven by the same nOn patches: the inputs are loaded once per sample for all four,
        /// and four independent recurrences hide the mad latency (the driven twin of DspKernels.ModalFree4).</summary>
        static void Driven4(ModalGroup* bank, int* list, int nOn, HandDrumsPatch* on0, HandDrumsPatch* on1, HandDrumsPatch* on2, HandDrumsPatch* on3,
            float* x0, float* x1, float* x2, float* x3, float4* acc, int from, int to)
        {
            ModalGroup* G0 = bank + list[0], G1 = bank + list[1], G2 = bank + list[2], G3 = bank + list[3];
            int k0 = list[0], k1 = list[1], k2 = list[2], k3 = list[3];
            float4 zr0 = G0->Zr, zi0 = G0->Zi, pr0 = G0->Pr, pi0 = G0->Pi, go0 = G0->Go, np0 = -pi0;
            float4 zr1 = G1->Zr, zi1 = G1->Zi, pr1 = G1->Pr, pi1 = G1->Pi, go1 = G1->Go, np1 = -pi1;
            float4 zr2 = G2->Zr, zi2 = G2->Zi, pr2 = G2->Pr, pi2 = G2->Pi, go2 = G2->Go, np2 = -pi2;
            float4 zr3 = G3->Zr, zi3 = G3->Zi, pr3 = G3->Pr, pi3 = G3->Pi, go3 = G3->Go, np3 = -pi3;
            // per group: the gains of patch 0..3
            float4 a0 = Gi(on0, k0), a1 = Gi(on0, k1), a2 = Gi(on0, k2), a3 = Gi(on0, k3);
            float4 b0 = float4.zero, b1 = float4.zero, b2 = float4.zero, b3 = float4.zero;
            float4 c0 = float4.zero, c1 = float4.zero, c2 = float4.zero, c3 = float4.zero;
            float4 e0 = float4.zero, e1 = float4.zero, e2 = float4.zero, e3 = float4.zero;
            if (nOn > 1) { b0 = Gi(on1, k0); b1 = Gi(on1, k1); b2 = Gi(on1, k2); b3 = Gi(on1, k3); }
            if (nOn > 2) { c0 = Gi(on2, k0); c1 = Gi(on2, k1); c2 = Gi(on2, k2); c3 = Gi(on2, k3); }
            if (nOn > 3) { e0 = Gi(on3, k0); e1 = Gi(on3, k1); e2 = Gi(on3, k2); e3 = Gi(on3, k3); }
            switch (nOn)
            {
                case 1:
                    for (int n = from; n < to; n++)
                    {
                        float v0 = x0[n];
                        float4 u0 = a0 * v0, u1 = a1 * v0, u2 = a2 * v0, u3 = a3 * v0;
                        Step4(ref zr0, ref zi0, pr0, pi0, np0, u0, ref zr1, ref zi1, pr1, pi1, np1, u1, ref zr2, ref zi2, pr2, pi2, np2, u2, ref zr3, ref zi3, pr3, pi3, np3, u3);
                        float4 a = acc[n];
                        a = math.mad(go0, zi0, a); a = math.mad(go1, zi1, a); a = math.mad(go2, zi2, a); a = math.mad(go3, zi3, a);
                        acc[n] = a;
                    }
                    break;
                case 2:
                    for (int n = from; n < to; n++)
                    {
                        float v0 = x0[n], v1 = x1[n];
                        float4 u0 = math.mad(b0, v1, a0 * v0), u1 = math.mad(b1, v1, a1 * v0), u2 = math.mad(b2, v1, a2 * v0), u3 = math.mad(b3, v1, a3 * v0);
                        Step4(ref zr0, ref zi0, pr0, pi0, np0, u0, ref zr1, ref zi1, pr1, pi1, np1, u1, ref zr2, ref zi2, pr2, pi2, np2, u2, ref zr3, ref zi3, pr3, pi3, np3, u3);
                        float4 a = acc[n];
                        a = math.mad(go0, zi0, a); a = math.mad(go1, zi1, a); a = math.mad(go2, zi2, a); a = math.mad(go3, zi3, a);
                        acc[n] = a;
                    }
                    break;
                case 3:
                    for (int n = from; n < to; n++)
                    {
                        float v0 = x0[n], v1 = x1[n], v2 = x2[n];
                        float4 u0 = math.mad(c0, v2, math.mad(b0, v1, a0 * v0)), u1 = math.mad(c1, v2, math.mad(b1, v1, a1 * v0));
                        float4 u2 = math.mad(c2, v2, math.mad(b2, v1, a2 * v0)), u3 = math.mad(c3, v2, math.mad(b3, v1, a3 * v0));
                        Step4(ref zr0, ref zi0, pr0, pi0, np0, u0, ref zr1, ref zi1, pr1, pi1, np1, u1, ref zr2, ref zi2, pr2, pi2, np2, u2, ref zr3, ref zi3, pr3, pi3, np3, u3);
                        float4 a = acc[n];
                        a = math.mad(go0, zi0, a); a = math.mad(go1, zi1, a); a = math.mad(go2, zi2, a); a = math.mad(go3, zi3, a);
                        acc[n] = a;
                    }
                    break;
                default:
                    for (int n = from; n < to; n++)
                    {
                        float v0 = x0[n], v1 = x1[n], v2 = x2[n], v3 = x3[n];
                        float4 u0 = math.mad(e0, v3, math.mad(c0, v2, math.mad(b0, v1, a0 * v0))), u1 = math.mad(e1, v3, math.mad(c1, v2, math.mad(b1, v1, a1 * v0)));
                        float4 u2 = math.mad(e2, v3, math.mad(c2, v2, math.mad(b2, v1, a2 * v0))), u3 = math.mad(e3, v3, math.mad(c3, v2, math.mad(b3, v1, a3 * v0)));
                        Step4(ref zr0, ref zi0, pr0, pi0, np0, u0, ref zr1, ref zi1, pr1, pi1, np1, u1, ref zr2, ref zi2, pr2, pi2, np2, u2, ref zr3, ref zi3, pr3, pi3, np3, u3);
                        float4 a = acc[n];
                        a = math.mad(go0, zi0, a); a = math.mad(go1, zi1, a); a = math.mad(go2, zi2, a); a = math.mad(go3, zi3, a);
                        acc[n] = a;
                    }
                    break;
            }
            G0->Zr = zr0; G0->Zi = zi0; G1->Zr = zr1; G1->Zi = zi1;
            G2->Zr = zr2; G2->Zi = zi2; G3->Zr = zr3; G3->Zi = zi3;
        }

        /// <summary>The canonical driven MB4 step for four groups: zi' = mad(pr, zi, pi zr); zr' = mad(pr, zr, mad(-pi, zi, u)).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Step4(ref float4 zr0, ref float4 zi0, float4 pr0, float4 pi0, float4 np0, float4 u0,
                          ref float4 zr1, ref float4 zi1, float4 pr1, float4 pi1, float4 np1, float4 u1,
                          ref float4 zr2, ref float4 zi2, float4 pr2, float4 pi2, float4 np2, float4 u2,
                          ref float4 zr3, ref float4 zi3, float4 pr3, float4 pi3, float4 np3, float4 u3)
        {
            float4 t0 = math.mad(pr0, zi0, pi0 * zr0); zr0 = math.mad(pr0, zr0, math.mad(np0, zi0, u0)); zi0 = t0;
            float4 t1 = math.mad(pr1, zi1, pi1 * zr1); zr1 = math.mad(pr1, zr1, math.mad(np1, zi1, u1)); zi1 = t1;
            float4 t2 = math.mad(pr2, zi2, pi2 * zr2); zr2 = math.mad(pr2, zr2, math.mad(np2, zi2, u2)); zi2 = t2;
            float4 t3 = math.mad(pr3, zi3, pi3 * zr3); zr3 = math.mad(pr3, zr3, math.mad(np3, zi3, u3)); zi3 = t3;
        }

        /// <summary>MB4 driven by up to 4 patches: u = gi_0 x_0[n]; u = mad(gi_p, x_p[n], u) (ascending slot order), then
        /// z = p z + u (handdrums.md 2.4). x is the patches' force differences, or their air pops for the cavity group.</summary>
        static void Driven(ModalGroup* g, int nIn, float4 g0, float* x0, float4 g1, float* x1, float4 g2, float* x2, float4 g3, float* x3, float4* acc, int from, int to)
        {
            float4 zr = g->Zr, zi = g->Zi, pr = g->Pr, pi = g->Pi, go = g->Go, npi = -pi;
            switch (nIn)
            {
                case 1:
                    for (int n = from; n < to; n++)
                    {
                        float4 u = g0 * x0[n];
                        float4 t = math.mad(pr, zi, pi * zr);
                        zr = math.mad(pr, zr, math.mad(npi, zi, u));
                        zi = t;
                        acc[n] = math.mad(go, zi, acc[n]);
                    }
                    break;
                case 2:
                    for (int n = from; n < to; n++)
                    {
                        float4 u = g0 * x0[n];
                        u = math.mad(g1, x1[n], u);
                        float4 t = math.mad(pr, zi, pi * zr);
                        zr = math.mad(pr, zr, math.mad(npi, zi, u));
                        zi = t;
                        acc[n] = math.mad(go, zi, acc[n]);
                    }
                    break;
                case 3:
                    for (int n = from; n < to; n++)
                    {
                        float4 u = g0 * x0[n];
                        u = math.mad(g1, x1[n], u);
                        u = math.mad(g2, x2[n], u);
                        float4 t = math.mad(pr, zi, pi * zr);
                        zr = math.mad(pr, zr, math.mad(npi, zi, u));
                        zi = t;
                        acc[n] = math.mad(go, zi, acc[n]);
                    }
                    break;
                default:
                    for (int n = from; n < to; n++)
                    {
                        float4 u = g0 * x0[n];
                        u = math.mad(g1, x1[n], u);
                        u = math.mad(g2, x2[n], u);
                        u = math.mad(g3, x3[n], u);
                        float4 t = math.mad(pr, zi, pi * zr);
                        zr = math.mad(pr, zr, math.mad(npi, zi, u));
                        zi = t;
                        acc[n] = math.mad(go, zi, acc[n]);
                    }
                    break;
            }
            g->Zr = zr; g->Zi = zi;
        }

        /// <summary>One patch over [i0, i1): the hand-part mass on a Hunt-Crossley flesh spring against the locally reacting
        /// head, solved implicitly by 4 fixed Newton steps per sample (double); dF and the air pop out (handdrums.md 2.3, 2.6).</summary>
        static void Contact(HandDrumsPatch* S, int i0, int i1)
        {
            const double sr = HandDrumsConst.Sr, dt = 1.0 / HandDrumsConst.Sr;
            int flightMax = (int)Math.Floor(HandDrumsConst.FlightS * sr + 0.5), ageMax = (int)Math.Floor(HandDrumsConst.ContactMaxS * sr + 0.5);
            double zdt = S->Zh * sr;
            for (int n = i0; n < i1; n++)
            {
                double F = 0;
                if (S->On != 0)
                {
                    double xn = S->X + S->V * dt, y0 = S->Y, y = y0;
                    for (int it = 0; it < 4; it++)
                    {
                        double dd = xn - y, Fh = 0, dFdy = 0;
                        if (dd > 0)
                        {
                            double sd = Math.Sqrt(dd), dv = S->V - (y - y0) * sr, damp = 1 + S->Lam * dv;
                            double dn1 = S->An == 1 ? 1 : S->An == 2 ? dd : dd * dd, pw = dn1 * dd * sd;
                            Fh = S->K * pw * damp;
                            if (Fh > 0) dFdy = -(S->Alpha * dn1 * sd) * S->K * damp - S->K * pw * S->Lam * sr; else Fh = 0;
                        }
                        double q = y * S->Iy2, gg = zdt * (y - y0) + S->Kh * y * (1 + q * q) - Fh, gp = zdt + S->Kh * (1 + 3 * q * q) - dFdy;
                        y -= gg / gp;
                    }
                    {
                        double dd = xn - y;
                        if (dd > 0)
                        {
                            double sd = Math.Sqrt(dd), dn1 = S->An == 1 ? 1 : S->An == 2 ? dd : dd * dd;
                            F = S->K * dn1 * dd * sd * (1 + S->Lam * (S->V - (y - y0) * sr));
                            if (F < 0) F = 0;
                        }
                    }
                    S->Yd = (y - y0) * sr; S->Y = y; S->X = xn; S->V -= (F / S->M) * dt;
                    if (F > 0)
                    {
                        S->Started = 1; S->Flight = 0; S->TLast = S->Age;
                        if (F > S->Fmax) S->Fmax = F;
                        // first touch of a cupped hand: the trapped air is pushed out (a short pulse, as in a hand clap)
                        if (S->TFirst < 0)
                        {
                            S->TFirst = S->Age;
                            if (S->Cup > 0) { S->PopLeft = S->PopLen; S->PopA = (HandDrumsConst.PopK * S->Cup * S->V0 * S->V0 * 2) / S->PopLen; }
                        }
                    }
                    else if (S->Started != 0 && S->V < 0) { if (++S->Flight > flightMax) S->On = 0; }
                    if (++S->Age > ageMax) S->On = 0;
                }
                S->DF[n] = (float)(F - S->Fp);
                S->Fp = F;
                if (S->PopLeft > 0)
                {
                    int i = S->PopLen - S->PopLeft;
                    S->Pop[n] = (float)(S->PopA * 0.5 * (1 - Math.Cos((Tau * (i + 0.5)) / S->PopLen)));
                    S->PopLeft--;
                }
                else S->Pop[n] = 0;
            }
        }

        /// <summary>The control step (every 32 samples): flush, per-group energy and cull, the glide from the head's strain
        /// energy, free slots aged, poles refreshed, idle.</summary>
        static void Control(HandDrumsDrum* d)
        {
            HandDrumsPatch* P = &d->P0;
            int contactOn = 0;
            for (int p = 0; p < HandDrumsConst.Slots; p++) if (P[p].On != 0) { contactOn = 1; break; }
            // idle and settled: nothing would change (no active group, no glide, no dirty pole)
            if (d->Idle != 0 && contactOn == 0 && d->S == 1 && d->PolesDirty == 0 && d->GlideDirty == 0)
            {
                for (int p = 0; p < HandDrumsConst.Slots; p++) if (P[p].On == 0) P[p].Age = 0;
                d->Energy = 0;
                return;
            }
            double E = 0, Esum = 0;
            int anyAct = 0;
            double cull = HandDrumsConst.Cull2;
            for (int g = 0; g < d->G; g++)
            {
                if (d->Act[g] == 0) continue;
                float* b = (float*)(d->Bank + g);
                double eg = 0;
                for (int l = 0; l < 4; l++)
                {
                    float zr = b[l], zi = b[4 + l];
                    if (Math.Abs(zr) < HandDrumsConst.Flush) zr = 0;
                    if (Math.Abs(zi) < HandDrumsConst.Flush) zi = 0;
                    b[l] = zr; b[4 + l] = zi;
                    int k = 4 * g + l;
                    double zrd = zr, zid = zi, god = b[16 + l];
                    double el = (zrd * zrd + zid * zid) * god * god;
                    if (el > eg) eg = el;
                    Esum += el;
                    if (k < d->N && d->GlideOn[k] != 0) E += d->EW[k] * (zrd * zrd + zid * zid);
                }
                if (eg < cull && contactOn == 0)
                {
                    for (int l = 0; l < 4; l++) { b[l] = 0; b[4 + l] = 0; }
                    d->Act[g] = 0;
                }
                else anyAct = 1;
            }
            double sT = Math.Min(1 + d->GlideMax, Math.Sqrt(1 + d->Kappa * E));
            if (Math.Abs(sT - d->S) > HandDrumsConst.GlideEps) { d->S = sT; d->GlideDirty = 1; }
            for (int p = 0; p < HandDrumsConst.Slots; p++) if (P[p].On == 0) P[p].Age = 0;
            if (d->PolesDirty != 0) UpdatePoles(d, true); else if (d->GlideDirty != 0) UpdatePoles(d, false);
            d->Energy = Esum;
            if (anyAct == 0 && contactOn == 0) d->Idle = 1;
        }
    }

    /// <summary>Burst entries for running the unit outside the graph (phase-A tests, the offline renders and the perf test).
    /// In the app the graph's RenderBlock (FloatMode.Fast) calls HandDrumsDsp.Render through UnitHooks.</summary>
    [BurstCompile]
    public static unsafe class HandDrumsBurst
    {
        /// <summary>As in the app: FloatMode.Fast (mads fused).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void RenderFast(UnitSlot* u) => HandDrumsDsp.Render(u);

        /// <summary>IEEE, no contraction: the reference's arithmetic (bit-exact candidate under Burst).</summary>
        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        public static void RenderStrict(UnitSlot* u) => HandDrumsDsp.Render(u);
    }
}
