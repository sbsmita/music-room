// The singing bowls' audio-thread render (bowls.md sections 2 and 3.6): BowlsEngine.renderBlock of
// the Node bowls reference engine, sample for sample, in its canonical operation order (math.mad(a, b, c) = a*b + c,
// two roundings when not fused). The modes run as float4 lanes of 4 groups (MB4 layout plus a quadrature output gain):
// every lane operation is the reference's element-wise operation, and every sum over modes (the rim velocity, the
// radial displacement, the output) keeps the reference's order, so the managed path and the Strict Burst path are
// bit-identical to the Node vectors.
//
// Paths per segment (split at events to the sample):
//   asleep -> zeros; free (no finger, no pulse, no noise tail) -> group-outer MB4 with goR;
//   active -> sample-outer: contact shapes, predicted states, rim quantities, stick-tip normal force, closed-form implicit
//   friction, pulses, injection, output, rub noise.
// Entry points: Render (plain C#, the managed reference), RenderFast (Burst, FloatMode.Fast, what a plain callee of
// AudioGraph.RenderBlock gets), RenderStrict (Burst, FloatMode.Strict), RenderStrictFp (the same through a Burst function
// pointer: phase B calls it from the unit hook so the graph's Fast entry does not fuse the bowls' maths), and
// RenderUnit (the UnitSlot hook body for phase B). No allocation, no managed objects, no Unity API, no exceptions.
using System;
using System.Threading;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    [BurstCompile]
    public static unsafe class BowlsDsp
    {
        // the unit's own GraphOp byte values (bowls.md 2.10)
        public const byte OpStrike = 1, OpContact = 2, OpRelease = 3, OpDamp = 4, OpPickup = 5, OpSeed = 6, OpSilence = 7;

        const int NP = BowlsConst.NP, NM = BowlsConst.NM, NG = BowlsConst.NG, NPULSE = BowlsConst.NPULSE, MF = BowlsConst.MaxFrames;
        const float KC = (float)BowlsConst.KC, CC = (float)BowlsConst.CC, INV_MT = (float)(1 / BowlsConst.M_TIP), EPS = (float)BowlsConst.EPS;
        const float MUS = (float)BowlsConst.MU_S, MUI = (float)BowlsConst.MU_INF, DMU = (float)(BowlsConst.MU_S - BowlsConst.MU_INF), VC = (float)BowlsConst.VC;
        const float KL = (float)(1 / BowlsConst.ANGLE_LOCK), NOISEK = (float)BowlsConst.NOISE_K, FILL = (float)BowlsConst.FILL;
        const float SIXTH = (float)(1.0 / 6), TWO31 = (float)(1.0 / 2147483648.0), FL = (float)BowlsConst.FLUSH;

        public delegate void RenderFn(BowlsState* s, UnitEvent* due, int dueCount, int frames, float* outMix, int budget, float share);

        /// <summary>Groups to render from the work cap: Budget / 1200 x Share (platform 10.3), read once per block.</summary>
        public static int GroupsFor(int budget, float share)
        {
            double k = budget / 1200.0 * share;
            return k >= 0.75 ? 4 : k >= 0.6 ? 3 : k >= 0.45 ? 2 : 1;
        }

        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void RenderFast(BowlsState* s, UnitEvent* due, int dueCount, int frames, float* outMix, int budget, float share)
            => Render(s, due, dueCount, frames, outMix, budget, share);

        [BurstCompile(FloatMode = FloatMode.Strict, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void RenderStrict(BowlsState* s, UnitEvent* due, int dueCount, int frames, float* outMix, int budget, float share)
            => Render(s, due, dueCount, frames, outMix, budget, share);

        /// <summary>The Strict render as a function pointer (compile once on the main thread with CompileStrictPointer).</summary>
        [BurstCompile(FloatMode = FloatMode.Strict, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        [AOT.MonoPInvokeCallback(typeof(RenderFn))]
        public static void RenderStrictFp(BowlsState* s, UnitEvent* due, int dueCount, int frames, float* outMix, int budget, float share)
            => Render(s, due, dueCount, frames, outMix, budget, share);

        /// <summary>Main thread: the Strict render's function pointer (0 when Burst is off: RenderUnit then renders inline).</summary>
        public static IntPtr CompileStrictPointer()
        {
            if (!BurstCompiler.IsEnabled) return IntPtr.Zero;
            return BurstCompiler.CompileFunctionPointer<RenderFn>(RenderStrictFp).Value;
        }

        /// <summary>Phase B: the body of UnitHooks.RenderBowls(u). Reset, readiness, then the block (in chunks of at most
        /// 1024 frames), through the Strict function pointer when the unit compiled one.</summary>
        public static void RenderUnit(UnitSlot* u, float share)
        {
            var s = (BowlsState*)u->State;
            if (s == null || u->Channels < 1) return;
            if (Volatile.Read(ref s->Ready) == 0) { s->NotReady += u->DueCount; return; }
            if ((u->Flags & UnitFlags.Reset) != 0) { for (int b = 0; b < BowlsConst.NBOWLS; b++) ResetBowl(BowlsState.Bowl(s, b)); s->Resets++; }
            int budget = u->Graph != null ? u->Graph->Budget : 1200;
            float* outp = u->Channel(0);
            int i0 = 0;
            for (int c0 = 0; c0 < u->Frames; c0 += MF)
            {
                int len = math.min(MF, u->Frames - c0);
                int i1 = i0;
                while (i1 < u->DueCount && u->Due[i1].Offset < c0 + len) { u->Due[i1].Offset -= c0; i1++; }
                if (s->StrictFn != IntPtr.Zero) new FunctionPointer<RenderFn>(s->StrictFn).Invoke(s, u->Due + i0, i1 - i0, len, outp + c0, budget, share);
                else Render(s, u->Due + i0, i1 - i0, len, outp + c0, budget, share);
                i0 = i1;
            }
            u->Work = s->Work;
        }

        /// <summary>One block of the three bowls. due: this block's events, (At, Seq) order, Offset in [0, frames),
        /// C.Target = the bowl. outMix += ((y0 + y1) + y2). frames &lt;= 1024.</summary>
        public static void Render(BowlsState* u, UnitEvent* due, int dueCount, int frames, float* outMix, int budget, float share)
        {
            if (frames > MF) frames = MF;
            int keep = GroupsFor(budget, share);
            int work = 0;
            for (int b = 0; b < BowlsConst.NBOWLS; b++)
            {
                BowlState* s = BowlsState.Bowl(u, b);
                float* y = u->Y + b * MF;
                s->Work = 0; s->StBlockContact = 0; s->StBlockLost = 0;
                s->Frames = frames; s->InvFrames = 1f / frames;
                if (keep < s->Keep) { s->FadeHi = s->Keep; s->Keep = keep; } else { s->Keep = keep; s->FadeHi = keep; }
                SlewPickup(u, s);
                if (s->PickDirty != 0) RebuildPickup(s);
                if (s->On != 0) { Renorm(ref s->Ec, ref s->Es); Renorm(ref s->Tc, ref s->Ts); }
                int cur = 0;
                for (int e = 0; e < dueCount; e++)
                {
                    UnitEvent* ev = due + e;
                    if (ev->C.Target != b) continue;
                    int off = math.clamp(ev->Offset, 0, frames);
                    if (off > cur) { Segment(u, s, y, cur, off); cur = off; }
                    Apply(u, s, ev);
                    if ((byte)ev->C.Op == OpPickup && s->PickDirty != 0) RebuildPickup(s);
                }
                if (frames > cur) Segment(u, s, y, cur, frames);
                if (s->Awake != 0 || s->Hz1 != 0f || s->Hz2 != 0f) Highpass(s, y, frames);
                EndBlock(s, y, frames);
                work += s->Work;
            }
            float* y0 = u->Y, y1 = u->Y + MF, y2 = u->Y + 2 * MF;
            for (int n = 0; n < frames; n++)
            {
                float m = y0[n];
                m = m + y1[n];
                m = m + y2[n];
                outMix[n] += m;
            }
            u->Work = work;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        static void Renorm(ref float re, ref float im)
        {
            float k = 0.5f * (3f - (re * re + im * im));
            re = re * k; im = im * k;
        }

        static void SlewPickup(BowlsState* u, BowlState* s)
        {
            float sinD = s->Pc * s->Pts - s->Ps * s->Ptc, cosD = s->Pc * s->Ptc + s->Ps * s->Pts;
            if ((double)cosD > 0.9999875 && math.abs((double)sinD) <= BowlsConst.PICK_SLEW)
            {
                if (s->Pc != s->Ptc || s->Ps != s->Pts) { s->Pc = s->Ptc; s->Ps = s->Pts; s->PickDirty = 1; }
                return;
            }
            float rc = u->PickRc, rs = sinD >= 0f ? u->PickRs : -u->PickRs;
            float nr = s->Pc * rc - s->Ps * rs, ni = s->Pc * rs + s->Ps * rc;
            Renorm(ref nr, ref ni);
            s->Pc = nr; s->Ps = ni; s->PickDirty = 1;
        }

        // pickup gains from the listener phasor (pc, ps): q = e^{j n thetaL} e^{-j n theta0}
        static void RebuildPickup(BowlState* s)
        {
            float hr = s->Pc, hi = s->Ps;
            float cr = hr * hr - hi * hi, ci = hr * hi + hi * hr;
            for (int p = 0; p < NP; p++)
            {
                if (p > 0) { float nr = cr * hr - ci * hi, ni = cr * hi + ci * hr; cr = nr; ci = ni; }
                float qr = cr * s->Oc[p] - ci * s->Os[p], qi = cr * s->Os[p] + ci * s->Oc[p];
                int mA = 2 * p, mB = 2 * p + 1;
                int oA = (mA >> 2) * BowlState.STRIDE + (mA & 3), oB = (mB >> 2) * BowlState.STRIDE + (mB & 3);
                float radA = s->Rad[mA], radB = s->Rad[mB];
                s->G[oA + BowlState.GOI] = radA * qr; s->G[oA + BowlState.GOR] = -FILL * (radA * qi);
                s->G[oB + BowlState.GOI] = radB * qi; s->G[oB + BowlState.GOR] = FILL * (radB * qr);
            }
            s->PickDirty = 0;
        }

        // the pole table of (damp level, finger on the rim): switching is click-free (the state carries over)
        static void SetPoles(BowlState* s)
        {
            int L = s->Level + (s->On != 0 ? BowlsConst.NLEVEL : 0);
            if (L == s->PoleSet) return;
            s->PoleSet = L;
            for (int m = 0; m < NM; m++)
            {
                int o = (m >> 2) * BowlState.STRIDE + (m & 3);
                s->G[o + BowlState.PR] = s->Poles[(L * NM + m) * 2];
                s->G[o + BowlState.PI] = s->Poles[(L * NM + m) * 2 + 1];
            }
        }

        public static void ResetBowl(BowlState* s)
        {
            for (int g = 0; g < NG; g++) for (int l = 0; l < 4; l++) { s->G[g * BowlState.STRIDE + BowlState.ZR + l] = 0f; s->G[g * BowlState.STRIDE + BowlState.ZI + l] = 0f; }
            s->On = 0; s->Rel = 0; s->Lane = 0; s->N = 0; s->Fc = 0; s->XP = 0; s->VP = 0; s->NEnv = 0; s->PadF = 0; s->ToneEnv = 0;
            for (int k = 0; k < NPULSE; k++) s->POn[k] = 0;
            s->PActive = 0;
            s->Nz1 = 0; s->Nz2 = 0; s->Lz1 = 0; s->Lz2 = 0; s->Hz1 = 0; s->Hz2 = 0;
            s->Awake = 0;
            SetPoles(s);
        }

        // ------------------------------------------------------------------------------------------------ events

        static void Apply(BowlsState* u, BowlState* s, UnitEvent* ev)
        {
            switch ((byte)ev->C.Op)
            {
                case OpStrike: ApplyStrike(u, s, ev); break;
                case OpContact: ApplyContact(s, ref ev->C); break;
                case OpRelease:
                    if (s->On != 0 && s->Rel == 0 && ev->C.N == s->Lane)
                    {
                        s->Rel = 1; s->Ntar = 0; s->NN = BowlsConst.RELEASE_RAMP; s->DN = (float)(-(double)s->N / BowlsConst.RELEASE_RAMP);
                    }
                    break;
                case OpDamp:
                {
                    int L = math.clamp(ev->C.N, 0, BowlsConst.NLEVEL - 1);
                    if (L != s->Level) { s->Level = L; SetPoles(s); }
                    break;
                }
                case OpPickup:
                    s->Ptc = ev->C.C; s->Pts = ev->C.D;
                    if (ev->C.N != 0) { s->Pc = s->Ptc; s->Ps = s->Pts; s->PickDirty = 1; }
                    break;
                case OpSeed: BowlsDesign.SeedBowl(s, ev->C.Seed); break;
                case OpSilence: ResetBowl(s); break;
            }
        }

        static void ApplyStrike(BowlsState* u, BowlState* s, UnitEvent* ev)
        {
            // per-mode weights gi * phi_r(theta_s) * hs^n (shared by the strike's two components)
            float* w = stackalloc float[NM];
            float hr = ev->C.C, hi = ev->C.D, hs = ev->C.E;
            float cr = hr * hr - hi * hi, ci = hr * hi + hi * hr;
            float hp = hs * hs;
            for (int p = 0; p < NP; p++)
            {
                if (p > 0) { float nr = cr * hr - ci * hi, ni = cr * hi + ci * hr; cr = nr; ci = ni; hp = hp * hs; }
                float qr = cr * s->Oc[p] - ci * s->Os[p], qi = cr * s->Os[p] + ci * s->Oc[p];
                for (int mbr = 0; mbr < 2; mbr++)
                {
                    int m = 2 * p + mbr;
                    w[m] = s->G[(m >> 2) * BowlState.STRIDE + BowlState.GI + (m & 3)] * (hp * (mbr != 0 ? qi : qr));
                }
            }
            for (int comp = 0; comp < 2; comp++)
            {
                int slot = -1;
                for (int k = 0; k < NPULSE; k++) if (s->POn[k] == 0) { slot = k; break; }
                if (slot < 0) { slot = 0; for (int k = 1; k < NPULSE; k++) if (s->PStart[k] < s->PStart[slot]) slot = k; }
                int K = math.clamp(comp == 0 ? ev->C.N : ev->C.Id, 2, BowlsConst.PulseKMax);
                s->POn[slot] = 1; s->PK[slot] = 0; s->PLen[slot] = K; s->PStart[slot] = ev->At + comp * 0.5;
                s->PAmp[slot] = comp == 0 ? ev->C.A : ev->C.B;
                s->PRc[slot] = u->PulseCos[K]; s->PRs[slot] = u->PulseSin[K];
                s->PSc[slot] = 1f; s->PSs[slot] = 0f;
                for (int m = 0; m < NM; m++) s->PW[slot * NM + m] = w[m];
            }
            int act = 0;
            for (int k = 0; k < NPULSE; k++) act += s->POn[k];
            s->PActive = act;
            s->Awake = 1;
        }

        static void ApplyContact(BowlState* s, ref GraphCommand c)
        {
            int kind = c.N & 0xff, lane = (c.N >> 8) & 0xff, r = c.Id;
            if (s->On != 0 && lane != s->Lane)
            {
                if (lane == 0 && s->Lane != 0)
                {   // the live hand takes the bowl from a playback lane: the stick is re-seated where the live finger is
                    s->StPreempts++; s->Init = 1; s->Rel = 0; s->Lane = 0;
                    s->Ec = c.C; s->Es = c.D; s->N = 0; s->Fc = 0; s->PadF = 0;
                }
                else { s->StLaneDrops++; return; }   // one stick per bowl: the other lane waits
            }
            if (s->On == 0) { s->On = 1; s->Init = 1; s->Lane = lane; s->N = 0; s->Vf = c.B; s->Ec = c.C; s->Es = c.D; s->Fc = 0; s->PadF = 0; SetPoles(s); }
            int ramp = r > 0 ? math.clamp(r, BowlsConst.RAMP_MIN, BowlsConst.RAMP_MAX) : s->Init != 0 ? BowlsConst.ONSET_RAMP : BowlsConst.NORMAL_RAMP;
            s->Rel = 0; s->Kind = kind;
            s->Ntar = c.A; s->NN = ramp; s->DN = (float)(((double)s->Ntar - s->N) / ramp);
            s->VfTar = c.B; s->NVf = r > 0 ? ramp : BowlsConst.NORMAL_RAMP; s->DVf = (float)(((double)s->VfTar - s->Vf) / s->NVf);
            s->Tc = c.C; s->Ts = c.D; s->Omega = c.E;
            s->Trc = c.G; s->Trs = math.asfloat(c.Seed);
            if (s->Hs != c.F || s->Init != 0)
            {
                s->Hs = c.F;
                float hp = s->Hs * s->Hs;
                for (int p = 0; p < NP; p++) { if (p > 0) hp = hp * s->Hs; s->Hx[p] = hp; }
            }
            s->Awake = 1;
        }

        // ------------------------------------------------------------------------------------------------ render paths

        static void Segment(BowlsState* u, BowlState* s, float* y, int from, int to)
        {
            if (s->Awake == 0) { for (int n = from; n < to; n++) y[n] = 0f; return; }
            if (s->On != 0 || s->PActive != 0 || s->Nz1 != 0f || s->Nz2 != 0f || s->Lz1 != 0f || s->Lz2 != 0f) RenderActive(s, y, from, to);
            else RenderFree(s, (float4*)u->Acc, y, from, to);
        }

        static void RenderFree(BowlState* s, float4* acc, float* y, int from, int to)
        {
            int hi = s->FadeHi, keep = s->Keep;
            for (int n = from; n < to; n++) acc[n] = float4.zero;
            float4* g4 = (float4*)s->G;
            for (int gi = 0; gi < hi; gi++)
            {
                float4* g = g4 + gi * 8;
                float4 zr = g[BowlState.F4ZR], zi = g[BowlState.F4ZI];
                int lanes = math.countbits(math.bitmask((zr != 0f) | (zi != 0f)));
                if (lanes == 0) continue;                       // retired lanes (exactly zero state adds exactly zero)
                s->Work += lanes * (to - from);
                float4 pr = g[BowlState.F4PR], pi = g[BowlState.F4PI], npi = -pi, goI = g[BowlState.F4GOI], goR = g[BowlState.F4GOR];
                if (gi < keep)
                {
                    for (int n = from; n < to; n++)
                    {
                        float4 nr = math.mad(pr, zr, npi * zi);
                        float4 ni = math.mad(pr, zi, pi * zr);
                        zr = nr; zi = ni;
                        acc[n] = math.mad(goR, zr, math.mad(goI, zi, acc[n]));
                    }
                }
                else
                {   // a group dropped by the work cap fades over this block, then is zeroed at block end
                    int last = s->Frames - 1;
                    float inv = s->InvFrames;
                    for (int n = from; n < to; n++)
                    {
                        float4 nr = math.mad(pr, zr, npi * zi);
                        float4 ni = math.mad(pr, zi, pi * zr);
                        zr = nr; zi = ni;
                        float gf = (float)(last - n) * inv;
                        acc[n] = math.mad(gf, math.mad(goR, zr, goI * zi), acc[n]);
                    }
                }
                g[BowlState.F4ZR] = zr; g[BowlState.F4ZI] = zi;
            }
            for (int n = from; n < to; n++) { float4 a = acc[n]; y[n] = (a.x + a.y) + (a.z + a.w); }
        }

        static void RenderActive(BowlState* s, float* y, int from, int to)
        {
            float4* g4 = (float4*)s->G;
            int NGA = s->FadeHi, NGK = s->Keep;
            float T = s->T, Y0 = s->Y0t, NENV = s->NEnvK;
            int pump = s->Kind == (int)BowlSurface.Inner ? 0 : 1;
            float4* phr = stackalloc float4[NG];
            float4* pht = stackalloc float4[NG];
            float4* zpr = stackalloc float4[NG];
            float4* zpi = stackalloc float4[NG];
            float* PF = stackalloc float[NPULSE];
            float* PHR = (float*)phr;
            float* PHT = (float*)pht;
            float4* pw4 = (float4*)s->PW;
            float nb0 = s->Nb[0], nb1 = s->Nb[1], nb2 = s->Nb[2], nb3 = s->Nb[3], nb4 = s->Nb[4];
            float lb0 = s->Lb[0], lb1 = s->Lb[1], lb2 = s->Lb[2], lb3 = s->Lb[3], lb4 = s->Lb[4];
            for (int n = from; n < to; n++)
            {
                // ---- contact shapes (only while a finger is on): e^{j k theta_c} e^{-j k theta0}, k = 2..9
                float vt = 0f, wr = 0f, wdr = 0f;
                int on = s->On;
                if (on != 0)
                {
                    float hr = s->Ec, hi = s->Es;
                    float cr = hr * hr - hi * hi, ci = hr * hi + hi * hr;
                    for (int p = 0; p < NP; p++)
                    {
                        if (p > 0) { float nr = cr * hr - ci * hi, ni = cr * hi + ci * hr; cr = nr; ci = ni; }
                        float hx = s->Hx[p];
                        float qr = hx * (cr * s->Oc[p] - ci * s->Os[p]), qi = hx * (cr * s->Os[p] + ci * s->Oc[p]);
                        float iv = s->InvN[p];
                        PHR[2 * p] = qr; PHR[2 * p + 1] = qi;               // radial: A cos, B sin
                        PHT[2 * p] = -iv * qi; PHT[2 * p + 1] = iv * qr;    // tangential: A -sin/n, B cos/n
                    }
                }
                // ---- free evolution of every rendered mode + rim quantities at the contact (kept groups only)
                for (int g = 0; g < NGA; g++)
                {
                    float4* gp = g4 + g * 8;
                    float4 zr = gp[BowlState.F4ZR], zi = gp[BowlState.F4ZI], pr = gp[BowlState.F4PR], pi = gp[BowlState.F4PI];
                    float4 zp = math.mad(pr, zr, -pi * zi), zq = math.mad(pr, zi, pi * zr);
                    zpr[g] = zp; zpi[g] = zq;
                    if (on != 0 && g < NGK)
                    {
                        float4 vr = gp[BowlState.F4OM] * zp;                // modal velocity
                        float4 a = pht[g] * vr, b = phr[g] * zq, c = phr[g] * vr;
                        vt = vt + a.x; wr = wr + b.x; wdr = wdr + c.x;
                        vt = vt + a.y; wr = wr + b.y; wdr = wdr + c.y;
                        vt = vt + a.z; wr = wr + b.z; wdr = wdr + c.z;
                        vt = vt + a.w; wr = wr + b.w; wdr = wdr + c.w;
                    }
                }
                s->Work += 4 * NGA;
                // ---- contact forces
                float Ft = 0f, Fr = 0f, noiseG = 0f;
                if (on != 0)
                {
                    s->Work += BowlsConst.CONTACT_WORK;
                    if (s->Init != 0) { s->XP = -wr; s->VP = -wdr; s->Init = 0; }
                    if (s->NN > 0) { s->N = s->N + s->DN; if (--s->NN == 0) s->N = s->Ntar; }
                    if (s->NVf > 0) { s->Vf = s->Vf + s->DVf; if (--s->NVf == 0) s->Vf = s->VfTar; }
                    float Nn = s->N > 0f ? s->N : 0f;
                    float Fc;
                    if (s->Kind == (int)BowlSurface.Outer)
                    {
                        float dl = s->XP + wr, dd = s->VP + wdr;
                        float Nd = Nn, gp = 1f;
                        if (s->PadK != 0f && Nn < s->PadN) { Nd = s->PadN; gp = Nn / s->PadN; }
                        Fc = 0f;
                        if (dl > 0f) Fc = math.mad(KC, dl, CC * dd);
                        if (Fc < 0f) Fc = 0f;
                        s->VP = math.mad(T, (Nd - Fc) * INV_MT, s->VP);
                        s->XP = math.mad(T, s->VP, s->XP);
                        if ((double)Nn > 0.05 && Fc == 0f) { s->StLostSamples++; s->StBlockLost++; }
                        if (s->PadK != 0f)
                        {   // the soft pad (the app): the bowl feels the bouncing stick's force through a leather / skin pad,
                            // never an on / off friction switch or a hard re-impact (the "broken cassette" chatter)
                            float fp = math.mad(s->PadMix, Nd - Fc, Fc);
                            s->PadF = math.mad(s->PadK, fp - s->PadF, s->PadF);
                            Fc = gp * s->PadF;
                        }
                        Fr = Fc;                                            // pushes the wall inward
                    }
                    else Fc = Nn;                                           // lip / inner: no radial stick dynamics
                    s->StContactSamples++; s->StBlockContact++;
                    // friction (implicit, closed form). u0 = finger - rim; F_t drags the rim toward the finger.
                    float u0 = s->Vf - vt, U0 = math.abs(u0), sg = u0 >= 0f ? 1f : -1f;
                    if (Fc > 0f)
                    {
                        float muS = pump != 0 ? MUS : MUI;
                        float yF = Y0 * Fc;
                        float lim = math.mad(yF, muS, EPS);
                        float x;
                        if (U0 <= lim)
                        {   // pseudo-adherence (linear, [I06] eq. 13)
                            float k = (yF * muS) / EPS;
                            float uu = u0 / (1f + k);
                            Ft = (Fc * (muS * uu)) / EPS; x = math.abs(uu); s->StStickSamples++;
                        }
                        else if (pump == 0) { x = U0 - yF * MUI; Ft = sg * (Fc * MUI); }
                        else
                        {   // hyperbolic law: the slip root of x = U0 - Y Fc mu(x), closed form
                            float a = yF * MUI, b = yF * DMU;
                            float B = (VC + a) - U0, Cq = VC * ((a + b) - U0);
                            float disc = math.mad(B, B, -4f * Cq), sq = math.sqrt(disc);
                            if (B >= 0f) x = (-2f * Cq) / (B + sq); else x = 0.5f * (sq - B);
                            float mu = MUI + (DMU * VC) / (VC + x);
                            Ft = sg * (Fc * mu);
                        }
                        s->NEnv = math.mad(NENV, Fc * x - s->NEnv, s->NEnv);   // friction power envelope (10 ms)
                        noiseG = NOISEK * s->NEnv;
                        s->StFc = Fc; s->StU = x;
                        s->StPumpAcc += (double)Ft * ((double)vt + (double)Y0 * Ft); s->StPumpN++;
                    }
                    s->Fc = Fc;
                    // advance the contact angle phasor (it locks onto the tracked target phasor)
                    float sinD = s->Ts * s->Ec - s->Tc * s->Es;
                    float xa = (s->Omega + KL * sinD) * T;
                    float rc = 1f - 0.5f * (xa * xa), rs = xa - SIXTH * (xa * (xa * xa));
                    float ner = s->Ec * rc - s->Es * rs, nei = s->Ec * rs + s->Es * rc; s->Ec = ner; s->Es = nei;
                    float ntr = s->Tc * s->Trc - s->Ts * s->Trs, nti = s->Tc * s->Trs + s->Ts * s->Trc; s->Tc = ntr; s->Ts = nti;
                    if (s->Rel != 0 && s->NN == 0) { s->On = 0; s->Rel = 0; s->N = 0; s->Fc = 0; SetPoles(s); }
                }
                // ---- strike pulses (half-sines by rotation)
                int anyPulse = 0;
                if (s->PActive != 0)
                {
                    for (int k = 0; k < NPULSE; k++)
                    {
                        if (s->POn[k] == 0) { PF[k] = 0f; continue; }
                        PF[k] = s->PAmp[k] * s->PSs[k];
                        float a = s->PSc[k] * s->PRc[k] - s->PSs[k] * s->PRs[k], b = s->PSc[k] * s->PRs[k] + s->PSs[k] * s->PRc[k];
                        s->PSc[k] = a; s->PSs[k] = b;
                        if (++s->PK[k] >= s->PLen[k]) { s->POn[k] = 0; s->PActive--; }
                        anyPulse = 1;
                    }
                }
                // ---- inject and output (canonical lane accumulation; fading groups take no force)
                float4 acc = float4.zero;
                float gf = 0f;
                if (NGA > NGK) gf = (float)(s->Frames - 1 - n) * s->InvFrames;
                for (int g = 0; g < NGA; g++)
                {
                    float4* gp = g4 + g * 8;
                    float4 zr = zpr[g];
                    if (g < NGK)
                    {
                        float4 f = float4.zero;
                        if (on != 0) f = pht[g] * Ft - phr[g] * Fr;
                        zr = math.mad(gp[BowlState.F4GI], f, zr);
                        if (anyPulse != 0)
                            for (int k = 0; k < NPULSE; k++) if (PF[k] != 0f) zr = math.mad(pw4[k * NG + g], PF[k], zr);
                    }
                    float4 zi = zpi[g];
                    gp[BowlState.F4ZR] = zr; gp[BowlState.F4ZI] = zi;
                    float4 goI = gp[BowlState.F4GOI], goR = gp[BowlState.F4GOR];
                    if (g < NGK) acc = math.mad(goR, zr, math.mad(goI, zi, acc));
                    else acc = math.mad(gf, math.mad(goR, zr, goI * zi), acc);
                }
                float outv = (acc.x + acc.y) + (acc.z + acc.w);
                if (s->NoiseRel != 0f)
                {   // the tonal rub noise (the app): the friction hiss follows the bowl's own tone, never louder than a faint
                    // texture on it, so a rub that does not (yet) sing is quiet instead of gritty
                    s->ToneEnv = math.mad(s->ToneK, math.abs(outv) - s->ToneEnv, s->ToneEnv);
                    noiseG = math.min(noiseG * s->NoiseScale, s->NoiseRel * s->ToneEnv);
                }
                // ---- rub noise: xorshift32 -> band-pass -> low-pass, scaled by the friction power envelope
                if (noiseG != 0f || s->Nz1 != 0f || s->Nz2 != 0f || s->Lz1 != 0f || s->Lz2 != 0f)
                {
                    uint x = s->Nz; x ^= x << 13; x ^= x >> 17; x ^= x << 5; s->Nz = x;
                    float w = noiseG * ((float)(int)x * TWO31);
                    float yb = math.mad(nb0, w, s->Nz1);
                    s->Nz1 = math.mad(nb1, w, math.mad(-nb3, yb, s->Nz2));
                    s->Nz2 = math.mad(nb2, w, -nb4 * yb);
                    float yl = math.mad(lb0, yb, s->Lz1);
                    s->Lz1 = math.mad(lb1, yb, math.mad(-lb3, yl, s->Lz2));
                    s->Lz2 = math.mad(lb2, yb, -lb4 * yl);
                    outv = outv + yl;
                }
                y[n] = outv;
            }
        }

        // ------------------------------------------------------------------------------------------------ block end

        static void Highpass(BowlState* s, float* y, int frames)
        {
            float h0 = s->Hb[0], h1 = s->Hb[1], h2 = s->Hb[2], h3 = s->Hb[3], h4 = s->Hb[4];
            float z1 = s->Hz1, z2 = s->Hz2;
            for (int n = 0; n < frames; n++)
            {
                float x = y[n];
                float o = math.mad(h0, x, z1);
                z1 = math.mad(h1, x, math.mad(-h3, o, z2));
                z2 = math.mad(h2, x, -h4 * o);
                y[n] = o;
            }
            s->Hz1 = z1; s->Hz2 = z2;
        }

        static void EndBlock(BowlState* s, float* y, int frames)
        {
            for (int g = s->Keep; g < s->FadeHi; g++) for (int l = 0; l < 4; l++) { s->G[g * BowlState.STRIDE + BowlState.ZR + l] = 0f; s->G[g * BowlState.STRIDE + BowlState.ZI + l] = 0f; }
            s->FadeHi = s->Keep;
            float e = 0f;
            for (int p = 0; p < NP; p++) s->StPairAmp[p] = 0f;
            for (int m = 0; m < NM; m++)
            {
                int o = (m >> 2) * BowlState.STRIDE + (m & 3);
                float zr = s->G[o + BowlState.ZR], zi = s->G[o + BowlState.ZI];
                if (math.abs(zr) < FL) zr = 0f;
                if (math.abs(zi) < FL) zi = 0f;
                s->G[o + BowlState.ZR] = zr; s->G[o + BowlState.ZI] = zi;
                float a2 = zr * zr + zi * zi;
                s->StPairAmp[m >> 1] = s->StPairAmp[m >> 1] + a2;
                float el = a2 * (s->Rad[m] * s->Rad[m]);
                if (el > e) e = el;
            }
            for (int p = 0; p < NP; p++) s->StPairAmp[p] = math.sqrt(s->StPairAmp[p]);
            s->StSingVel = s->StPairAmp[0] * s->G[BowlState.OM];        // (2,0) radial velocity amplitude at the antinode (m/s)
            s->StP3 = s->StPairAmp[1] * s->G[BowlState.OM + 2];         // (3,0): the "wrong mode" check
            s->StPump = s->StPumpN != 0 ? (float)(s->StPumpAcc / s->StPumpN) : 0f;
            s->StPumpAcc = 0; s->StPumpN = 0;
            s->StP2[0] = s->G[BowlState.ZR]; s->StP2[1] = s->G[BowlState.ZI]; s->StP2[2] = s->G[BowlState.ZR + 1]; s->StP2[3] = s->G[BowlState.ZI + 1];
            s->StLane = s->On != 0 ? s->Lane : -1;
            if (math.abs(s->Nz1) < FL) s->Nz1 = 0f;
            if (math.abs(s->Nz2) < FL) s->Nz2 = 0f;
            if (math.abs(s->Lz1) < FL) s->Lz1 = 0f;
            if (math.abs(s->Lz2) < FL) s->Lz2 = 0f;
            if (math.abs(s->Hz1) < FL) s->Hz1 = 0f;
            if (math.abs(s->Hz2) < FL) s->Hz2 = 0f;
            s->StEnergy = e;
            double r = 0; float pk = 0f;
            for (int n = 0; n < frames; n++) { r += (double)y[n] * y[n]; float a = math.abs(y[n]); if (a > pk) pk = a; }
            s->StRms = (float)math.sqrt(r / frames); s->StPeak = pk; s->StBlocks++; s->StWork = s->Work;
            if (s->On == 0 && s->PActive == 0 && (double)e < BowlsConst.CULL2 && (double)pk < 1e-6)
            {   // retire: zero everything (no denormals, no CPU)
                for (int g = 0; g < NG; g++) for (int l = 0; l < 4; l++) { s->G[g * BowlState.STRIDE + BowlState.ZR + l] = 0f; s->G[g * BowlState.STRIDE + BowlState.ZI + l] = 0f; }
                s->Nz1 = 0; s->Nz2 = 0; s->Lz1 = 0; s->Lz2 = 0; s->Hz1 = 0; s->Hz2 = 0; s->Awake = 0;
            }
        }
    }
}
