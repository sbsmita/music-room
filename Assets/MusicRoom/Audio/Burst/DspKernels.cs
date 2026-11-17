// Burst-compiled static DSP kernels (spec 10-upgrade/burst.md sections 3-5). Target: Assets/MusicRoom/Audio/Burst/DspKernels.cs.
//
// Calling convention: plain static methods on a [BurstCompile] static class, called DIRECTLY from C# (Burst's
// direct-call transform turns each call into a cached function-pointer call into lib_burst_generated.so; no
// FunctionPointer<T> boilerplate, no delegate, no allocation). Every parameter is a pointer or a primitive: Burst
// requires structs to cross the managed/native boundary by reference, and NativeArray cannot be passed to a
// function pointer, so the host passes raw pointers into one unmanaged arena (AudioGraphHost).
//
// Canonical arithmetic: every expression is written with math.mad(a, b, c) in the order of lab/kernels.mjs. Managed
// (Mono, IL2CPP without Burst, .NET) evaluates mad as a*b + c with two roundings: bit-identical to the Node reference.
// Burst with FloatMode.Fast contracts mad into one fused multiply-add (AArch64 fmla / fmls): 2x fewer vector ops on the
// two 128-bit FP pipes of the Quest core, results within ~1e-6 relative of the reference (tests use a tolerance).
// No transcendental function is called per sample: coefficients come from the main thread (double math).
//
// Loop order (MB4): group-outer, sample-inner, FOUR groups interleaved in registers (16 modes per sample step). One
// group alone is latency-bound (fmul 3 + fmla 4 = 7 cycles per sample on Cortex-A78-class cores); four hide it.
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    [BurstCompile]
    public static unsafe class DspKernels
    {
        /// <summary>|state| below this becomes 0 at block end (denormal guard; FLT_MIN = 1.18e-38).</summary>
        public const float Flush = 1e-20f;
        /// <summary>A modal group whose loudest lane energy |z|^2 go^2 is below this (-110 dBFS) retires.</summary>
        public const float CullEnergy = 1e-11f;
        const float Inv2Pow31 = 1f / 2147483648f;

        // ------------------------------------------------------------------ MB4: modal banks

        /// <summary>Free decay of groups [0, count): acc[n] += go * Im(z) lane-wise, n in [from, to).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void ModalFree(ModalGroup* g, int count, float4* acc, int from, int to)
        {
            int i = 0;
            for (; i + 4 <= count; i += 4) ModalFree4(g + i, g + i + 1, g + i + 2, g + i + 3, acc, from, to);
            for (; i < count; i++) ModalFree1(g + i, acc, from, to);
        }

        /// <summary>Free decay of the groups bankBase[list[0..count)] (ascending list = canonical accumulation order).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void ModalFreeList(ModalGroup* bankBase, int* list, int count, float4* acc, int from, int to)
        {
            int i = 0;
            for (; i + 4 <= count; i += 4) ModalFree4(bankBase + list[i], bankBase + list[i + 1], bankBase + list[i + 2], bankBase + list[i + 3], acc, from, to);
            for (; i < count; i++) ModalFree1(bankBase + list[i], acc, from, to);
        }

        /// <summary>Driven by a mono excitation x[n] (real input into every mode): z = p z + gi x.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void ModalDriven(ModalGroup* g, int count, float* x, float4* acc, int from, int to)
        {
            int i = 0;
            for (; i + 4 <= count; i += 4) ModalDriven4(g + i, g + i + 1, g + i + 2, g + i + 3, x, acc, from, to);
            for (; i < count; i++) ModalDriven1(g + i, x, acc, from, to);
        }

        /// <summary>Driven variant over an index list.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void ModalDrivenList(ModalGroup* bankBase, int* list, int count, float* x, float4* acc, int from, int to)
        {
            int i = 0;
            for (; i + 4 <= count; i += 4) ModalDriven4(bankBase + list[i], bankBase + list[i + 1], bankBase + list[i + 2], bankBase + list[i + 3], x, acc, from, to);
            for (; i < count; i++) ModalDriven1(bankBase + list[i], x, acc, from, to);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ModalFree1(ModalGroup* g, float4* acc, int from, int to)
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
        static void ModalFree4(ModalGroup* g0, ModalGroup* g1, ModalGroup* g2, ModalGroup* g3, float4* acc, int from, int to)
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
        static void ModalDriven1(ModalGroup* g, float* x, float4* acc, int from, int to)
        {
            float4 zr = g->Zr, zi = g->Zi, pr = g->Pr, pi = g->Pi, go = g->Go, gi = g->Gi, npi = -pi;
            for (int n = from; n < to; n++)
            {
                float xn = x[n];
                float4 t = math.mad(pr, zi, pi * zr);
                zr = math.mad(pr, zr, math.mad(npi, zi, gi * xn));
                zi = t;
                acc[n] = math.mad(go, zi, acc[n]);
            }
            g->Zr = zr; g->Zi = zi;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ModalDriven4(ModalGroup* g0, ModalGroup* g1, ModalGroup* g2, ModalGroup* g3, float* x, float4* acc, int from, int to)
        {
            float4 zr0 = g0->Zr, zi0 = g0->Zi, pr0 = g0->Pr, pi0 = g0->Pi, go0 = g0->Go, gi0 = g0->Gi, np0 = -pi0;
            float4 zr1 = g1->Zr, zi1 = g1->Zi, pr1 = g1->Pr, pi1 = g1->Pi, go1 = g1->Go, gi1 = g1->Gi, np1 = -pi1;
            float4 zr2 = g2->Zr, zi2 = g2->Zi, pr2 = g2->Pr, pi2 = g2->Pi, go2 = g2->Go, gi2 = g2->Gi, np2 = -pi2;
            float4 zr3 = g3->Zr, zi3 = g3->Zi, pr3 = g3->Pr, pi3 = g3->Pi, go3 = g3->Go, gi3 = g3->Gi, np3 = -pi3;
            for (int n = from; n < to; n++)
            {
                float xn = x[n];
                float4 t0 = math.mad(pr0, zi0, pi0 * zr0); zr0 = math.mad(pr0, zr0, math.mad(np0, zi0, gi0 * xn)); zi0 = t0;
                float4 t1 = math.mad(pr1, zi1, pi1 * zr1); zr1 = math.mad(pr1, zr1, math.mad(np1, zi1, gi1 * xn)); zi1 = t1;
                float4 t2 = math.mad(pr2, zi2, pi2 * zr2); zr2 = math.mad(pr2, zr2, math.mad(np2, zi2, gi2 * xn)); zi2 = t2;
                float4 t3 = math.mad(pr3, zi3, pi3 * zr3); zr3 = math.mad(pr3, zr3, math.mad(np3, zi3, gi3 * xn)); zi3 = t3;
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

        /// <summary>Block end for groups [0, count): flush |z| &lt; Flush to 0, write each group's loudest-lane energy
        /// |z|^2 go^2 to energy[i]; returns how many groups are above CullEnergy.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static int ModalEndBlock(ModalGroup* g, int count, float* energy)
        {
            int alive = 0;
            for (int i = 0; i < count; i++)
            {
                float4 zr = g[i].Zr, zi = g[i].Zi, go = g[i].Go;
                zr = math.select(zr, 0f, math.abs(zr) < Flush);
                zi = math.select(zi, 0f, math.abs(zi) < Flush);
                g[i].Zr = zr; g[i].Zi = zi;
                float e = math.cmax((zr * zr + zi * zi) * (go * go));
                energy[i] = e;
                if (e >= CullEnergy) alive++;
            }
            return alive;
        }

        /// <summary>out[n] += csum(acc[n]) ((x + y) + (z + w)), then acc[n] = 0.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void AccResolve(float4* acc, float* output, int from, int to)
        {
            for (int n = from; n < to; n++)
            {
                output[n] += math.csum(acc[n]);
                acc[n] = float4.zero;
            }
        }

        /// <summary>out[n] += csum(acc[n]) * (g0 + (n - from) dg), then acc[n] = 0 (LOD and steal fades).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static void AccResolveRamp(float4* acc, float* output, int from, int to, float g0, float dg)
        {
            float g = g0;
            for (int n = from; n < to; n++)
            {
                output[n] += math.csum(acc[n]) * g;
                acc[n] = float4.zero;
                g += dg;
            }
        }

        // ------------------------------------------------------------------ WG4: waveguide strings

        /// <summary>Four strings, lanes in parallel (4 scalar gathers + 4 scatters per sample; the loop-carried chains
        /// are the allpass and loss states, so interleaving two groups halves the latency cost).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void Waveguide4(WaveguideGroup* s, float* lines, float4* exc4, float4* acc, int from, int to)
        {
            int len = s->Len, mask = s->Mask, w = s->W;
            int4 L = s->L;
            float4 c = s->C, x1 = s->X1, y1 = s->Y1, b0 = s->B0, a1 = s->A1, s1 = s->S1, go = s->Go;
            float* l0 = lines; float* l1 = lines + len; float* l2 = lines + 2 * len; float* l3 = lines + 3 * len;
            for (int n = from; n < to; n++)
            {
                int4 rd = (w - L) & mask;
                float4 xv = new float4(l0[rd.x], l1[rd.y], l2[rd.z], l3[rd.w]);
                float4 y = math.mad(c, xv - y1, x1);
                x1 = xv; y1 = y;
                float4 sv = math.mad(b0, y, a1 * s1);
                s1 = sv;
                float4 v = sv + exc4[n];
                l0[w] = v.x; l1[w] = v.y; l2[w] = v.z; l3[w] = v.w;
                acc[n] = math.mad(go, v, acc[n]);
                w = (w + 1) & mask;
            }
            s->W = w;
            s->X1 = math.select(x1, 0f, math.abs(x1) < Flush);
            s->Y1 = math.select(y1, 0f, math.abs(y1) < Flush);
            s->S1 = math.select(s1, 0f, math.abs(s1) < Flush);
        }

        // ------------------------------------------------------------------ BQ4, NZ4, EXP4, utilities

        /// <summary>Four TDF-II biquads in place on lane-interleaved io[n] (one filter per lane).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void Biquad4Process(Biquad4* q, float4* io, int from, int to)
        {
            float4 b0 = q->B0, b1 = q->B1, b2 = q->B2, na1 = q->NA1, na2 = q->NA2, z1 = q->Z1, z2 = q->Z2;
            for (int n = from; n < to; n++)
            {
                float4 xv = io[n];
                float4 y = math.mad(b0, xv, z1);
                z1 = math.mad(b1, xv, math.mad(na1, y, z2));
                z2 = math.mad(b2, xv, na2 * y);
                io[n] = y;
            }
            q->Z1 = math.select(z1, 0f, math.abs(z1) < Flush);
            q->Z2 = math.select(z2, 0f, math.abs(z2) < Flush);
        }

        /// <summary>Four xorshift32 lanes: o[n] = (int)x * 2^-31 in [-1, 1).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void Noise4Process(Noise4* s, float4* o, int from, int to)
        {
            uint4 x = s->State;
            for (int n = from; n < to; n++)
            {
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                o[n] = (float4)math.asint(x) * Inv2Pow31;
            }
            s->State = x;
        }

        /// <summary>io[n] *= g (lane-wise), then g *= m.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static void Exp4Process(Exp4* e, float4* io, int from, int to)
        {
            float4 g = e->G, m = e->M;
            for (int n = from; n < to; n++) { io[n] *= g; g *= m; }
            e->G = math.select(g, 0f, math.abs(g) < Flush);
        }

        /// <summary>dst[n] += src[n] * g, g += dg (mono mix with a linear ramp; dg = 0 for a constant gain).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void MixRamp(float* dst, float* src, int from, int to, float g, float dg)
        {
            for (int n = from; n < to; n++) { dst[n] += src[n] * g; g += dg; }
        }

        /// <summary>The same mono block to every channel of an interleaved Unity buffer (OnAudioFilterRead data).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static void MonoToInterleaved(float* mono, float* data, int frames, int channels)
        {
            if (channels == 2) { for (int n = 0, j = 0; n < frames; n++, j += 2) { float v = mono[n]; data[j] = v; data[j + 1] = v; } return; }
            for (int n = 0, j = 0; n < frames; n++) { float v = mono[n]; for (int c = 0; c < channels; c++) data[j++] = v; }
        }

        [BurstCompile]
        public static void Clear(float* p, int count)
        {
            for (int i = 0; i < count; i++) p[i] = 0f;
        }

        /// <summary>Engine addition (one-shot voices): dst[n] += csum(io[n] * w) * g, g += dg. The four lanes of a one-shot
        /// voice (up to four noise bands) collapse into one mono source; w holds the per-lane output gains.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static void Lane4Mix(float4* io, float4* w, float* dst, int from, int to, float g, float dg)
        {
            float4 wv = *w;
            for (int n = from; n < to; n++) { dst[n] += math.csum(io[n] * wv) * g; g += dg; }
        }

        /// <summary>Engine addition (unit fades): dst[n] += src[n] * g, g moving by step per sample and stopping at target (a
        /// ramp that ends inside the block holds its target for the rest). Returns g after the block.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float MixToward(float* dst, float* src, int frames, float g, float step, float target)
        {
            if (step == 0f || g == target)
            {
                for (int n = 0; n < frames; n++) dst[n] += src[n] * target;
                return target;
            }
            for (int n = 0; n < frames; n++)
            {
                dst[n] += src[n] * g;
                g += step;
                if ((step > 0f && g >= target) || (step < 0f && g <= target)) { g = target; step = 0f; }
            }
            return g;
        }

        /// <summary>Engine addition: max |p[i]| over [0, count) (silence tests for the idle skip and the unit outputs).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float PeakAbs(float* p, int count)
        {
            float m = 0f;
            for (int i = 0; i < count; i++) m = math.max(m, math.abs(p[i]));
            return m;
        }

        /// <summary>Device probe (spec section 7.4 T-DENORM): 16 dependent y = y a + b per iteration; run once with
        /// (1e-30, 0.5, 1e-30) and once with (1e-40, 0.5, 1e-40) and compare the times. Strict mode: no contraction,
        /// so the subnormal range is really exercised.</summary>
        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        public static float DenormProbe(int iterations, float y0, float a, float b)
        {
            float y = y0;
            for (int i = 0; i < iterations; i++)
            {
                y = y * a + b; y = y * a + b; y = y * a + b; y = y * a + b;
                y = y * a + b; y = y * a + b; y = y * a + b; y = y * a + b;
                y = y * a + b; y = y * a + b; y = y * a + b; y = y * a + b;
                y = y * a + b; y = y * a + b; y = y * a + b; y = y * a + b;
            }
            return y;
        }
    }
}
