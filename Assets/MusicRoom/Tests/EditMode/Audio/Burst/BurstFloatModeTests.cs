// Which FloatMode does Burst use for a method called from another Burst entry point? (ENGINE-API.md section 3.4: unit
// renders run inside AudioGraph.RenderBlock.) a = 1 + 2^-12: a*a - (1 + 2^-11) is 2^-24 when the multiply-add is fused
// (Fast) and 0 when it is not (two roundings, Strict / managed). Measured in the editor (Burst 1.8.31, Apple silicon):
// callees inherit the entry's mode; the test pins that and logs the values.
using NUnit.Framework;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    [BurstCompile]
    public static class FloatModeProbe
    {
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float FastEntry(float a, float c) => math.mad(a, a, c);

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        public static float StrictEntry(float a, float c) => math.mad(a, a, c);

        /// <summary>A Strict entry calling a method that asks for Fast.</summary>
        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        public static float StrictEntryCallsFast(float a, float c) => FastCallee(a, c);

        /// <summary>A Fast entry calling a method that asks for Strict.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float FastEntryCallsStrict(float a, float c) => StrictCallee(a, c);

        /// <summary>A Fast entry calling a plain (unattributed) helper.</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float FastEntryCallsPlain(float a, float c) => PlainCallee(a, c);

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static float FastCallee(float a, float c) => math.mad(a, a, c);

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        public static float StrictCallee(float a, float c) => math.mad(a, a, c);

        static float PlainCallee(float a, float c) => math.mad(a, a, c);
    }

    public class BurstFloatModeTests
    {
        [Test]
        public void ReportWhichFloatModeCalleesGet()
        {
            float a = 1f + 1f / 4096f, c = -(1f + 1f / 2048f);
            float fast = FloatModeProbe.FastEntry(a, c), strict = FloatModeProbe.StrictEntry(a, c);
            float sCallsF = FloatModeProbe.StrictEntryCallsFast(a, c), fCallsS = FloatModeProbe.FastEntryCallsStrict(a, c);
            float fCallsP = FloatModeProbe.FastEntryCallsPlain(a, c);
            Debug.Log($"[BURST-PROBE] burst={BurstCompiler.IsEnabled} fused={fast != 0} (fast entry {fast:G9}, strict entry {strict:G9}); " +
                      $"strict entry -> fast callee {sCallsF:G9}; fast entry -> strict callee {fCallsS:G9}; fast entry -> plain helper {fCallsP:G9}");
            Assert.AreEqual(0f, strict, "a Strict entry never fuses");
            if (!BurstCompiler.IsEnabled) { Assert.AreEqual(0f, fast, "managed math.mad rounds twice"); return; }
            // The contract ENGINE-API.md relies on: a callee inherits its ENTRY point's FloatMode, whatever its own attribute
            // says, so every unit render (called inside AudioGraph.RenderBlock) runs with RenderBlock's FloatMode.Fast.
            Assert.AreEqual(strict, sCallsF, "a Fast callee of a Strict entry is compiled Strict");
            Assert.AreEqual(fast, fCallsS, "a Strict callee of a Fast entry is compiled Fast");
            Assert.AreEqual(fast, fCallsP, "a plain helper of a Fast entry is compiled Fast");
        }
    }
}
