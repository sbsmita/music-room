// E8 (experience.md section 10): the harp strings' blur. The tau table of the old synth, the closed-form (d'Alembert)
// check of the modal envelope, and the shipped G4 rows are the experience lab's experience.vectors.json harpBlur
// (tolerance 1e-3 mm); the v2 harp's partial decays are harp.md sections 3-4 (the T60 targets the loop filters are fitted
// to, e.g. C6 partials 2 / 3 / 4: 1.08 / 0.61 / 0.38 s).
using MusicRoom.Audio;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Harp
{
    public class HarpBlurTests
    {
        [Test]
        public void TheOldSynthsPartialDecaysMatchTheReferenceTable()
        {
            (int midi, int n, double tau)[] rows =
            {
                (48, 1, 0.647430), (48, 2, 0.635685), (48, 4, 0.592672), (48, 8, 0.466357),
                (60, 1, 0.501402), (60, 6, 0.285422),
                (67, 1, 0.404631), (67, 2, 0.341117), (67, 4, 0.209465), (67, 8, 0.082139),
                (72, 3, 0.134674),
                (84, 1, 0.111069), (84, 2, 0.042989), (84, 4, 0.012360), (84, 8, 0.003106),
            };
            foreach (var r in rows)
                Assert.That(HarpPartialDecay.OldTau(r.midi, r.n), Is.EqualTo(r.tau).Within(r.tau * 1e-5 + 6e-7), $"midi {r.midi} partial {r.n} (vectors rounded to 6 decimals)");
        }

        [Test]
        public void TheV2DesignDecayAndTheContractDefault()
        {
            // C6 (string 21, nylon): T60 2.019 s, partials 2..4 at their targets 1.08 / 0.61 / 0.38 s.
            Assert.That(HarpPartialDecay.V2Tau(21, 1) * HarpPartialDecay.NepersPerT60, Is.EqualTo(2.01923584).Within(1e-5));
            Assert.That(HarpPartialDecay.V2Tau(21, 2) * HarpPartialDecay.NepersPerT60, Is.EqualTo(1.08).Within(0.01));
            Assert.That(HarpPartialDecay.V2Tau(21, 3) * HarpPartialDecay.NepersPerT60, Is.EqualTo(0.61).Within(0.01));
            Assert.That(HarpPartialDecay.V2Tau(21, 4) * HarpPartialDecay.NepersPerT60, Is.EqualTo(0.38).Within(0.01));
            // C3 (wound): the fundamental rings 8.41 s; every string's highs die faster than its fundamental.
            Assert.That(HarpPartialDecay.V2Tau(0, 1) * HarpPartialDecay.NepersPerT60, Is.EqualTo(8.40819333).Within(1e-5));
            for (int i = 0; i < 22; i++)
                for (int n = 2; n <= 8; n++)
                    Assert.That(HarpPartialDecay.V2Tau(i, n), Is.LessThan(HarpPartialDecay.V2Tau(i, n - 1)), $"string {i} partial {n}");
            Assert.That(HarpPartialDecay.Khf(1), Is.EqualTo((1.1e-6 + 1.1e-6 + 1.6e-6) / 3).Within(1e-12), "3-tap smoothed like the engine");
            Assert.That(HarpPartialDecay.MidiOf(11), Is.EqualTo(HarpGeometry.MidiOf(11)));
            // The contract's default member is the harp that plays (today's harp, the classic sound only): the old synth's
            // decay, the same formula as HarpPartialDecay.OldTau.
            IAudioEngine engine = new DefaultHarpEngine();
            Assert.That(engine.HarpPartialTau(11, 3), Is.EqualTo(HarpPartialDecay.OldTau(HarpGeometry.MidiOf(11), 3)).Within(HarpPartialDecay.OldTau(HarpGeometry.MidiOf(11), 3) * 1e-5));
            Assert.That(engine.HarpPartialTau(HarpGeometry.MidiOf(11), 3), Is.EqualTo(engine.HarpPartialTau(11, 3)), "a midi number works too");
        }

        [Test]
        public void ManyModesReproduceTheDAlembertParallelogram()
        {
            // 64 modes, 256 phases, p .35, h 3 mm: max error 5 um (the reference closedFormCheck).
            const int K = 64, P = 256;
            double p = 0.35, h = 0.003, err = 0;
            for (int j = 0; j <= 16; j++)
            {
                double u = j / 16.0, mx = 0, mn = 0;
                for (int k = 0; k < P; k++)
                {
                    double y = 0;
                    for (int n = 1; n <= K; n++)
                        y += 2 * h * System.Math.Sin(n * System.Math.PI * p) / (n * n * System.Math.PI * System.Math.PI * p * (1 - p))
                             * System.Math.Cos(n * 2 * System.Math.PI * k / P) * System.Math.Sin(n * System.Math.PI * u);
                    mx = System.Math.Max(mx, y); mn = System.Math.Min(mn, y);
                }
                double up = u <= p ? h * u / p : h * (1 - u) / (1 - p);
                double q = 1 - p, lo = u <= q ? -h * u / q : -h * (1 - u) / p;
                err = System.Math.Max(err, System.Math.Max(System.Math.Abs(mx - up), System.Math.Abs(mn - lo)));
            }
            Assert.That(err, Is.LessThanOrEqualTo(6e-6), "upper envelope peaks at p, lower at 1 - p");
        }

        [Test]
        public void TheShippedEnvelopeMatchesTheG4RowsAndABruteForceSum()
        {
            // String G4 (midi 67), vel .7 (h 3.16 mm), p .35, tau from the old synth (as in the vectors), rings 4..12 by 2.
            float h = HarpPlay.VibBase + HarpPlay.VibVel * 0.7f;
            Assert.That(h, Is.EqualTo(0.00316f).Within(1e-7f));
            var b = new float[8];
            var tau = new float[8];
            HarpBlur.PluckCoeffs(0.35f, h, b);
            for (int n = 0; n < 8; n++) tau[n] = HarpPartialDecay.OldTau(67, n + 1);
            var c = new float[8];
            var up = new float[17];
            var lo = new float[17];
            int[] rings = { 4, 6, 8, 10, 12 };
            (float t, double[] up, double core, double edge)[] rows =
            {
                (0f, new[] { 2.30009, 3.005582, 2.451156, 1.834295, 1.238179 }, 0.430982, 0.393936),
                (0.1f, new[] { 1.790227, 2.270484, 1.928397, 1.446779, 0.964973 }, 0.552336, 0.309921),
                (0.5f, new[] { 0.643232, 0.786795, 0.732514, 0.569472, 0.381288 }, 0.829952, 0.117725),
                (1f, new[] { 0.179396, 0.219127, 0.212879, 0.173364, 0.118712 }, 0.950582, 0.034213),
                (2f, new[] { 0.01425, 0.017699, 0.017923, 0.015387, 0.011014 }, 0.995839, 0.002881),
            };
            foreach (var row in rows)
            {
                HarpBlur.Envelope(b, tau, row.t, 1f, c, up, lo);
                for (int i = 0; i < rings.Length; i++)
                {
                    Assert.That(up[rings[i]] * 1000.0, Is.EqualTo(row.up[i]).Within(1e-3), $"t {row.t} up ring {rings[i]}");
                    Assert.That(lo[16 - rings[i]] * 1000.0, Is.EqualTo(-row.up[i]).Within(1e-3), $"t {row.t} lo ring {16 - rings[i]} (the mirror)");
                }
                HarpBlur.Alpha(up[8], lo[8], 0.0014f, out float core, out float edge);
                Assert.That(core, Is.EqualTo(row.core).Within(1e-5), $"t {row.t} core alpha");
                Assert.That(edge, Is.EqualTo(row.edge).Within(1e-5), $"t {row.t} edge alpha");
                // The symmetry shortcuts (13 phases, rings 0..8 mirrored) equal the plain 24-phase, 17-ring sum.
                for (int j = 0; j <= 16; j++)
                {
                    double mx = 0, mn = 0;
                    for (int k = 0; k < 24; k++)
                    {
                        double y = 0;
                        for (int n = 1; n <= 8; n++)
                            y += c[n - 1] * System.Math.Cos(n * 2 * System.Math.PI * k / 24) * System.Math.Sin(n * System.Math.PI * j / 16);
                        mx = System.Math.Max(mx, y); mn = System.Math.Min(mn, y);
                    }
                    Assert.That(up[j], Is.EqualTo(mx).Within(1e-8), $"t {row.t} ring {j} up");
                    Assert.That(lo[j], Is.EqualTo(mn).Within(1e-8), $"t {row.t} ring {j} lo");
                }
            }
            Assert.That(up[0], Is.EqualTo(0f).Within(1e-9f), "the ends do not move");
            Assert.That(up[16], Is.EqualTo(0f).Within(1e-9f));
        }

        [Test]
        public void APluckedStringBlursWidestNearThePluckAndStopsWhenItDies()
        {
            var play = new HarpPlay();
            HarpString s = play.Strings[0];   // C3, the longest decay
            play.PlaybackPluck(0, 1f, 0.3f, 0f, PluckMode.Through);
            Assert.IsTrue(s.Blurring);
            for (int n = 0; n < 8; n++) Assert.That(s.BlurTau[n], Is.EqualTo(HarpPartialDecay.V2Tau(0, n + 1)), "no engine: the v2 design decay");
            // t = 0: the upper envelope peaks at the pluck point (u .3, ring ~5), the lower one at 1 - p (ring ~11).
            int top = 0, bottom = 0;
            for (int j = 1; j < 16; j++) { if (s.Up[j] > s.Up[top]) top = j; if (s.Lo[j] < s.Lo[bottom]) bottom = j; }
            Assert.That(top, Is.InRange(4, 5), "0.3 x 16 = 4.8");
            Assert.That(bottom, Is.InRange(11, 12), "0.7 x 16 = 11.2");
            Assert.That(s.Up[top], Is.EqualTo(s.Amp).Within(s.Amp * 0.1f), "about the pluck height (8 modes round the corner)");
            float t = 0f;
            int frames = 0;
            while (s.Blurring && frames < 90 * 30) { play.Update(null, null, Matrix4x4.identity, 1f / 90f); t += 1f / 90f; frames++; }
            Assert.IsFalse(s.Blurring, "it stops once the swing is under 0.05 mm");
            Assert.That(t, Is.GreaterThan(5f).And.LessThan(15f), $"C3 rings visibly for seconds ({t:F1} s)");
            Assert.IsFalse(s.Moving);
        }

        /// <summary>An engine that only takes the contract's default HarpPartialTau (everything else unused).</summary>
        sealed class DefaultHarpEngine : IAudioEngine
        {
            public bool IsReady => false;
            public int SampleRate => 48000;
            public double Now => 0;
            public double FrameNow => 0;
            public float RenderProgress => 1f;
            public VoiceHandle Piano(int midi, float vel, double when = 0) => default;
            public void Release(VoiceHandle h, bool thud = true, double when = 0) { }
            public VoiceHandle Pluck(int stringIndex, float vel, double when = 0, PluckOptions? options = null) => default;
            public void Damp(VoiceHandle h, int hands = 1, double when = 0) { }
            public double HarpRingSeconds(int midi) => 1;
            public void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0) { }
            public void Choke(DrumKind kind) { }
            public void SetFire(bool on) { }
            public void SetWind(bool on) { }
            public void SetSnow(bool on) { }
            public bool FireOn => false;
            public bool WindOn => false;
            public bool SnowOn => false;
            public void SnowGust(float strength, float attack, float hold, float release) { }
            public SynthSource Source(SourceId id) => null;
            public void SetSourceParent(SourceId id, Transform parent, Vector3 localPosition) { }
            public void SetSourcePosition(SourceId id, Vector3 world) { }
            public void SetMixedReality(bool mixedReality) { }
            public void SetRoomBox(Vector3 center, Vector3 size, bool realRoom) { }
            public void Chirp(ChirpKind kind, double when = 0) { }
            public bool HasVoiceLine(string id) => false;
            public float VoiceLineSeconds(string id) => -1;
            public double PlayVoiceLine(string id, double when = 0) => 0;
            public void StopVoiceLine() { }
            public bool VoiceLinePlaying => false;
            public float VoiceLevel => 0;
            public void Swish(double when = 0) { }
            public AudioTestTap TestTap { get; set; }
            public void SeedAll(uint seed) { }
        }
    }
}
