// Burst entries for the bowls tests: the graph's situation (a FloatMode.Fast Burst entry, as AudioGraph.RenderBlock)
// calling the bowls' Strict render through a Burst function pointer, and a float-mode probe of that mechanism
// (BurstFloatModeTests' a*a + c trick: 1 + 2^-12 squared minus (1 + 2^-11) is 2^-24 when fused, 0 when not).
using System;
using AOT;
using MusicRoom.Audio.Burst;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Tests.Audio
{
    [BurstCompile]
    public static unsafe class BowlsBurstProbe
    {
        public delegate float MadFn(float a, float c);

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        [MonoPInvokeCallback(typeof(MadFn))]
        public static float StrictMad(float a, float c) => math.mad(a, a, c);

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        [MonoPInvokeCallback(typeof(MadFn))]
        public static float FastMad(float a, float c) => math.mad(a, a, c);

        /// <summary>A Fast entry calling a function pointer: what does the callee's own FloatMode give?</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float FastEntryCallsPointer(IntPtr fn, float a, float c) => new FunctionPointer<MadFn>(fn).Invoke(a, c);

        /// <summary>A Fast entry (the graph) rendering the bowls through their Strict function pointer.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static void FastCallsRender(IntPtr fn, BowlsState* s, UnitEvent* due, int n, int frames, float* outMix, int budget, float share)
            => new FunctionPointer<BowlsDsp.RenderFn>(fn).Invoke(s, due, n, frames, outMix, budget, share);

        static void Trampoline(IntPtr fn, IntPtr s, IntPtr due, int n, int frames, IntPtr outMix, int budget, float share)
            => FastCallsRender(fn, (BowlsState*)s, (UnitEvent*)due, n, frames, (float*)outMix, budget, share);

        /// <summary>Wires BowlsRig's StrictPointer path (once per domain).</summary>
        public static void Install()
        {
            if (BowlsRig.StrictPointer == IntPtr.Zero) BowlsRig.StrictPointer = BowlsDsp.CompileStrictPointer();
            BowlsRig.ViaPointer = Trampoline;
        }
    }
}
