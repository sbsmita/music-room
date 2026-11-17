// EditMode tests for the Burst kernels and graph (spec 10-upgrade/burst.md sections 7 and 9: the Burst lab's test
// vectors, frozen in BurstVectors.g.cs). MusicRoom.Tests.EditMode references Unity.Burst and Unity.Mathematics
// and allows unsafe code.
//
// Determinism rules: every input is synthetic and seeded (no dspTime, no threads, no Random); the kernels compile
// synchronously in the editor (CompileSynchronously = true), so a test never mixes managed and Burst results.
//  - Burst OFF (Jobs > Burst > Enable Compilation unticked, or the .NET harness): managed math.mad is unfused ->
//    the outputs are BIT-EXACT with the Node reference (FNV-1a of the float32 bits).
//  - Burst ON (default): FloatMode.Fast fuses mad -> outputs within Tol of the reference.
using NUnit.Framework;
using Unity.Burst;

namespace MusicRoom.Tests.Audio
{
    public class BurstKernelTests
    {
        const double Tol = 1e-5;                 // absolute, per sample (Node-measured fused deviation <= 1.5e-6)

        static bool Exact => !BurstCompiler.IsEnabled;

        static void AssertSummary(float[] got, BurstVectors.Summary exp, string name)
        {
            if (Exact) Assert.AreEqual(exp.Fnv, BurstScenarios.Fnv1a32(got), $"{name}: bit-exact with Burst off");
            for (int i = 0; i < 8; i++) Assert.AreEqual(exp.First8[i], got[i], Tol, $"{name}[{i}]");
            double s = 0, q = 0; float pk = 0;
            foreach (var v in got) { s += v; q += v * v; pk = System.Math.Max(pk, System.Math.Abs(v)); }
            Assert.AreEqual(exp.Sum, s, Tol * got.Length, $"{name} sum");
            Assert.AreEqual(exp.SumSq, q, 1e-4 * System.Math.Max(1, exp.SumSq), $"{name} sum of squares");
            Assert.AreEqual(exp.Peak, pk, Tol, $"{name} peak");
        }

        [Test] public void ModalFreeMatchesReference() => AssertSummary(BurstScenarios.Mb4Free(), BurstVectors.Mb4Free, "mb4Free");
        [Test] public void ModalDrivenMatchesReference() => AssertSummary(BurstScenarios.Mb4Driven(), BurstVectors.Mb4Driven, "mb4Driven");
        [Test] public void WaveguideMatchesReference() => AssertSummary(BurstScenarios.Wg4(), BurstVectors.Wg4, "wg4");

        [Test]
        public void NoiseAndBiquadMatchReference()
        {
            var noise = BurstScenarios.Noise(out var filtered, out var seeds);
            CollectionAssert.AreEqual(BurstVectors.LaneSeeds12345, seeds);
            Assert.AreEqual(BurstVectors.Noise4.Fnv, BurstScenarios.Fnv1a32(noise), "integer noise is exact in every mode");
            AssertSummary(filtered, BurstVectors.Biquad4, "biquad4");
        }

        [Test]
        public void SingleModeImpulseResponse()
        {
            var y = BurstScenarios.Mb4Impulse(out float pr, out float pi);
            Assert.AreEqual(0f, y[0]);
            Assert.AreEqual(pi, y[1], 1e-9, "y[1] = r sin w");
            for (int i = 0; i < 16; i++) Assert.AreEqual(BurstVectors.Mb4Impulse16[i], y[i], 1e-7, $"y[{i}]");
        }

        [Test]
        public void GraphBlocksMatchReference()
        {
            var blocks = BurstScenarios.Graph(out int lod, out int culled, out int maxActive);
            for (int k = 0; k < blocks.Length; k++) AssertSummary(blocks[k], BurstVectors.Graph[k], $"graph[{k}]");
            Assert.AreEqual(BurstVectors.GraphLodDropped, lod);
            Assert.AreEqual(BurstVectors.GraphCulled, culled);
            Assert.AreEqual(BurstVectors.GraphMaxActive, maxActive);
        }

        [Test]
        public void StealPolicyPrefersQuietAndReleasedAndProtectsAttacks()
        {
            CollectionAssert.AreEqual(new[] { 1, 4, 0, 5 }, BurstScenarios.StealOrder());
        }

        [Test]
        public void RenderTwiceIsIdentical()
        {
            Assert.AreEqual(BurstScenarios.Fnv1a32(BurstScenarios.Mb4Driven()), BurstScenarios.Fnv1a32(BurstScenarios.Mb4Driven()), "same inputs, same bits (any mode)");
        }
    }
}
