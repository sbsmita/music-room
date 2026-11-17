// The handpan unit against the Node reference's DSP vectors (handpan.md 9.1; the handpan lab's test-vectors.mjs,
// copied to Reference~/test-vectors.json and vec-*.f32). Every vector runs twice:
//   Exact - HandpanDsp.Render from managed code with the reference's two-rounding kernels: bit for bit (FNV hashes);
//   Burst - HandpanDsp.RenderDirect (FloatMode.Fast, DspKernels, as inside the graph): within 1e-5 per sample.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Handpan;
using MusicRoom.Tests.Handpan;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class HandpanDspTests
    {
        static Dictionary<string, object> s_tv;
        static Dictionary<string, object> TV => s_tv ?? (s_tv = HandpanJson.O(HandpanJson.Load(System.IO.Path.Combine(HandpanJson.ReferenceDir("Audio/Handpan"), "test-vectors.json"))));
        static string Ref(string file) => System.IO.Path.Combine(HandpanJson.ReferenceDir("Audio/Handpan"), file);
        const float BurstTol = 1e-5f;

        static HandpanTech Tech(object o) => (HandpanTech)HandpanJson.I(o);

        /// <summary>Plays a vector's command list (test-vectors.mjs render()) and returns the render.</summary>
        static float[] RenderCmds(List<object> cmds, int frames, bool burst, HandpanVoice voice = HandpanVoice.Calm, int block = 256, Action<HandpanRig, long> onBlock = null)
        {
            using var rig = new HandpanRig(voice, burst);
            foreach (object co in cmds)
            {
                var c = HandpanJson.O(co);
                long at = (long)c.Num("at");
                switch (c.Text("op"))
                {
                    case "tak": rig.Tak(at, c.Num("vel"), c.Num("X"), c.Num("Z")); break;
                    case "strike": rig.Strike(at, HandpanJson.I(c["field"]), c.Num("vel"), c.Num("u"), c.Num("v"), Tech(c["tech"]), c.Num("gain", 1)); break;
                    case "damp": rig.Damp(at, HandpanJson.I(c["field"]), HandpanJson.I(c["slot"]), c.Num("strength"), c.Num("u"), c.Num("v"), HandpanJson.I(c["kind"])); break;
                    case "reset": rig.Reset(at); break;
                    case "budget": rig.SetBudget(at, (float)c.Num("value")); break;
                }
            }
            return rig.Render(frames, block, onBlock == null ? (Action<long>)null : f => onBlock(rig, f));
        }

        static void Stats(float[] x, out double sum, out double sumSq, out float peak)
        {
            sum = 0; sumSq = 0; peak = 0;
            foreach (float v in x) { sum += v; sumSq += (double)v * v; peak = Math.Max(peak, Math.Abs(v)); }
        }

        static float MaxDiff(float[] a, float[] b, int n = -1)
        {
            if (n < 0) n = Math.Min(a.Length, b.Length);
            float m = 0;
            for (int i = 0; i < n; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
            return m;
        }

        static void AssertBurstClose(float[] exact, float[] burst, string what)
        {
            float d = MaxDiff(exact, burst);
            Debug.Log($"[Handpan] {what}: Burst vs exact max |diff| {d:E3} (burst {BurstCompiler.IsEnabled})");
            Assert.That(d, Is.LessThanOrEqualTo(BurstTol), what + ": Burst within 1e-5 per sample");
        }

        // ------------------------------------------------------------------ tables and pure functions

        [Test]
        public void TablesMatchTheReferenceHashAndSamples()
        {
            foreach (HandpanVoice v in new[] { HandpanVoice.Calm, HandpanVoice.Measured })
            {
                float[] pr = HandpanTables.PoleRe(v), pi = HandpanTables.PoleIm(v), go = HandpanTables.Go(v), gi = HandpanTables.Gi(v);
                Assert.AreEqual(HandpanState.NLanes, pr.Length);
                var all = new float[4 * pr.Length];
                for (int k = 0; k < pr.Length; k++) { all[4 * k] = pr[k]; all[4 * k + 1] = pi[k]; all[4 * k + 2] = go[k]; all[4 * k + 3] = gi[k]; }
                string want = TV.Text(v == HandpanVoice.Calm ? "tableFnv" : "tableFnvMeasured");
                Assert.AreEqual(want, HandpanJson.Fnv(all), v + " table FNV");
                foreach (object so in TV.Arr(v == HandpanVoice.Calm ? "tableSamples" : "tableSamplesMeasured"))
                {
                    var s = HandpanJson.O(so);
                    int k = 4 * HandpanJson.I(s["g"]) + HandpanJson.I(s["l"]);
                    Assert.AreEqual((float)s.Num("pr"), pr[k], $"{v} lane {k} pr");
                    Assert.AreEqual((float)s.Num("pi"), pi[k], $"{v} lane {k} pi");
                    Assert.AreEqual((float)s.Num("go"), go[k], $"{v} lane {k} go");
                    Assert.AreEqual((float)s.Num("gi"), gi[k], $"{v} lane {k} gi");
                }
            }
            Assert.AreEqual(HandpanVoice.Calm, HandpanVoiceChoice.Voice, "the default stays calm until the user picks (handpan-README A/B)");
        }

        [Test]
        public void StrikeParamsAndShapesMatchTheReference()
        {
            foreach (object po in TV.Arr("strikeParams"))
            {
                var p = HandpanJson.O(po);
                var tech = (HandpanTech)Enum.Parse(typeof(HandpanTech), p.Text("tech"));
                HandpanStrikeParams q = HandpanCommands.StrikeParams(p.Num("vel"), tech);
                string w = $"vel {p.Num("vel")} {tech}";
                Assert.AreEqual(HandpanJson.I(p["W1"]), q.W1, w + " W1");
                Assert.AreEqual(HandpanJson.I(p["W2"]), q.W2, w + " W2");
                Assert.AreEqual((float)p.Num("A1"), q.A1, w + " A1");
                Assert.AreEqual((float)p.Num("A2"), q.A2, w + " A2");
                Assert.AreEqual((float)p.Num("c1"), q.C1, w + " c1");
                Assert.AreEqual((float)p.Num("s1"), q.S1, w + " s1");
                Assert.AreEqual((float)p.Num("c2"), q.C2, w + " c2");
                Assert.AreEqual((float)p.Num("s2"), q.S2, w + " s2");
                Assert.AreEqual(p.Num("V"), q.V, 1e-8, w + " V");
            }
            using var rig = new HandpanRig();
            float* sh = stackalloc float[12];
            foreach (object so in TV.Arr("shapes"))
            {
                var s = HandpanJson.O(so);
                HandpanDsp.Shapes(rig.S, 1, s.Num("u"), s.Num("v"), (int)HandpanTech.Tip, sh);
                List<object> want = s.Arr("values");
                for (int l = 0; l < 12; l++) Assert.AreEqual((float)HandpanJson.D(want[l]), sh[l], $"A3 shape {l} at ({s.Num("u")}, {s.Num("v")})");
            }
            // the float API recovers the reference's double velocity (0.6f -> 0.6, 0.578f -> 0.578)
            Assert.AreEqual(0.6, HandpanCommands.VelOf(0.6f));
            Assert.AreEqual(0.578, HandpanCommands.VelOf(0.578f));
        }

        [Test]
        public void TakNeighboursMatchTV12()
        {
            foreach (object po in TV.Arr("TV12"))
            {
                var p = HandpanJson.O(po);
                HandpanMath.TakNeighbours(p.Num("X"), p.Num("Z"), out HandpanNeighbour n0, out HandpanNeighbour n1);
                List<object> nb = p.Arr("neighbours");
                var a = HandpanJson.O(nb[0]); var b = HandpanJson.O(nb[1]);
                string w = $"({p.Num("X")}, {p.Num("Z")})";
                Assert.AreEqual(HandpanJson.I(a["field"]), n0.Field, w + " first");
                Assert.AreEqual(HandpanJson.I(b["field"]), n1.Field, w + " second");
                Assert.AreEqual(a.Num("w"), n0.W, 1e-8, w + " w0");
                Assert.AreEqual(b.Num("w"), n1.W, 1e-8, w + " w1");
                Assert.AreEqual(a.Num("d"), n0.D, 1e-9, w + " d0");
            }
        }

        // ------------------------------------------------------------------ renders

        [Test]
        public void TV1_A3TipMatchesTheReferenceBitForBit()
        {
            var tv = TV.Obj("TV1");
            float[] y = RenderCmds(tv.Arr("cmds"), 96000, burst: false);
            float[] node = HandpanJson.ReadF32(Ref("vec-TV1-A3-tip.f32"));
            for (int i = 0; i < 100; i++) Assert.AreEqual(0f, y[i], "silent before the onset");
            List<object> first = tv.Arr("first16afterOnset");
            for (int k = 0; k < first.Count; k++) Assert.AreEqual((float)HandpanJson.D(first[k]), y[100 + k], $"frame {100 + k}");
            foreach (var kv in tv.Obj("at")) Assert.AreEqual((float)HandpanJson.D(kv.Value), y[int.Parse(kv.Key)], "frame " + kv.Key);
            Assert.AreEqual(0f, MaxDiff(y, node, node.Length), "the first second equals vec-TV1-A3-tip.f32");
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y), "TV1 FNV");
            Stats(y, out double sum, out double sumSq, out float peak);
            Assert.AreEqual((float)tv.Num("peak"), peak);
            Assert.AreEqual(tv.Num("rmsDb_0_4800"), HandpanRig.RmsDb(y, 0, 4800), 1e-4);
            Assert.AreEqual(tv.Num("rmsDb_48000_96000"), HandpanRig.RmsDb(y, 48000, 96000), 1e-4);
            float[] b = RenderCmds(tv.Arr("cmds"), 96000, burst: true);
            AssertBurstClose(y, b, "TV1");
            if (BurstCompiler.IsEnabled) Assert.That(MaxDiff(y, b), Is.GreaterThan(0f), "RenderDirect really ran Burst (fused: not bit-identical)");
        }

        [Test]
        public void TV2_DingSlapMatchesTheReference()
        {
            var tv = TV.Obj("TV2");
            var cmds = new List<object> { new Dictionary<string, object> { ["op"] = "strike", ["field"] = 0.0, ["vel"] = 0.8, ["u"] = 0.0, ["v"] = 0.0, ["tech"] = 1.0, ["at"] = 0.0 } };
            float[] y = RenderCmds(cmds, 48000, false);
            float[] node = HandpanJson.ReadF32(Ref("vec-TV2-ding-slap.f32"));
            Assert.AreEqual(0f, MaxDiff(y, node), "equals vec-TV2-ding-slap.f32");
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y));
            Stats(y, out _, out _, out float peak);
            Assert.AreEqual((float)tv.Num("peak"), peak);
            Assert.AreEqual(tv.Num("rmsDb_0_2400"), HandpanRig.RmsDb(y, 0, 2400), 1e-4);
            AssertBurstClose(y, RenderCmds(cmds, 48000, true), "TV2");
        }

        [Test]
        public void TV3_TakPostsThreeCommandsAndMatches()
        {
            var tv = TV.Obj("TV3");
            var cmd = tv.Obj("cmd");
            using (var rig = new HandpanRig())
            {
                rig.Tak((long)cmd.Num("at"), cmd.Num("vel"), cmd.Num("X"), cmd.Num("Z"));
                List<object> posted = tv.Arr("posted");
                Assert.AreEqual(posted.Count, rig.Posted.Count, "three commands, one batch");
                for (int k = 0; k < posted.Count; k++)
                {
                    var p = HandpanJson.O(posted[k]);
                    GraphCommand c = rig.Posted[k].c;
                    uint seed = c.Seed;
                    Assert.AreEqual(HandpanJson.I(p["N"]), c.N, $"#{k} N");
                    Assert.AreEqual(HandpanJson.I(p["T"]), c.Target, $"#{k} tech");
                    Assert.AreEqual((float)p.Num("A"), c.A, $"#{k} A");
                    Assert.AreEqual((float)p.Num("A2"), *(float*)&seed, $"#{k} A2");
                    Assert.AreEqual(HandpanJson.I(p["W1"]) | (HandpanJson.I(p["W2"]) << 16), c.Id, $"#{k} W1 W2");
                    Assert.AreEqual(rig.Posted[0].c.When, c.When, "one when");
                    Assert.AreEqual(CommandFlags.LateDrop, c.Flags & CommandFlags.LateDrop, "onsets are LateDrop");
                }
            }
            var cmds = new List<object> { cmd };
            float[] y = RenderCmds(cmds, 24000, false);
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y), "TV3 FNV");
            Assert.AreEqual(tv.Num("rmsDb_14400_24000"), HandpanRig.RmsDb(y, 14400, 24000), 1e-3, "the neighbours' hum");
            AssertBurstClose(y, RenderCmds(cmds, 24000, true), "TV3");
            var shell = new List<object> { new Dictionary<string, object> { ["op"] = "strike", ["field"] = 9.0, ["vel"] = 0.6, ["u"] = 0.0, ["v"] = 0.0, ["tech"] = 2.0, ["at"] = 37.0 } };
            float[] s = RenderCmds(shell, 24000, false);
            var so = tv.Obj("shellOnly");
            Assert.AreEqual(so.Text("fnv"), HandpanJson.Fnv(s), "Strike(9) alone");
            Assert.AreEqual(so.Num("rmsDb_14400_24000"), HandpanRig.RmsDb(s, 14400, 24000), 1e-3);
        }

        [TestCase("TV4")]
        [TestCase("TV8")]
        public void CommandListVectorsMatchBitForBit(string name)
        {
            var tv = TV.Obj(name);
            int frames = 48000;
            float[] y = RenderCmds(tv.Arr("cmds"), frames, false);
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y), name + " FNV");
            Stats(y, out double sum, out double sumSq, out float peak);
            Assert.AreEqual((float)tv.Num("peak"), peak, name + " peak");
            if (name == "TV4")
            {
                Assert.AreEqual(tv.Num("rmsDb_23040_24000"), HandpanRig.RmsDb(y, 23040, 24000), 1e-4);
                Assert.AreEqual(tv.Num("rmsDb_26400_28800"), HandpanRig.RmsDb(y, 26400, 28800), 1e-4, "the palm stopped it");
            }
            AssertBurstClose(y, RenderCmds(tv.Arr("cmds"), frames, true), name);
        }

        [Test]
        public void TV5_AHitDuringContactWaitsAndAThirdMerges()
        {
            var tv = TV.Obj("TV5");
            List<object> cmds = tv.Arr("cmds");
            float[] y = RenderCmds(cmds, 9600, false);
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y), "TV5 FNV");
            var three = new List<object>(cmds) { new Dictionary<string, object> { ["op"] = "strike", ["field"] = 2.0, ["vel"] = 0.3, ["u"] = 0.0, ["v"] = 0.0, ["tech"] = 0.0, ["at"] = 1012.0 } };
            long merged = 0;
            float[] y3 = RenderCmds(three, 9600, false, onBlock: (r, f) => merged = r.S->Merged);
            Assert.AreEqual(1, merged, "the third hit merged into the pending one");
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y3), "the louder pending hit wins: the same render");
            // the second pulse starts exactly at 1000 + W (48): no output change before, a new derivative after
            var one = new List<object> { cmds[0] };
            float[] y1 = RenderCmds(one, 9600, false);
            int first = -1;
            for (int i = 0; i < y.Length; i++) if (y[i] != y1[i]) { first = i; break; }
            // the pulse drives the modes from frame 1048; a mode's output is Im z, which the real drive reaches one sample later
            Assert.AreEqual(HandpanJson.I(tv["secondStartsAtFrame"]) + 1, first, "the pending hit starts on the sample after the contact ends");
            AssertBurstClose(y, RenderCmds(cmds, 9600, true), "TV5");
        }

        [Test]
        public void TV6_TheDingRingsItsMarshallPairsSympathetically()
        {
            var tv = TV.Obj("TV6");
            var cmds = new List<object> { new Dictionary<string, object> { ["op"] = "strike", ["field"] = 0.0, ["vel"] = 0.7, ["u"] = 0.35, ["v"] = 0.35, ["tech"] = 0.0, ["at"] = 0.0 } };
            var got = new double[9];
            RenderCmds(cmds, 24064 + 256, false, onBlock: (r, f) => { if (f == 24064) for (int i = 0; i < 9; i++) got[i] = r.GroupEnergyDb(i * 5 + 4); });
            List<object> want = tv.Arr("symEnergyDb_at24064");
            for (int i = 0; i < 9; i++) Assert.AreEqual(HandpanJson.D(want[i]), got[i], 0.01, "SYM energy of field " + i);
            Assert.Less(got[0], -290, "no self-drive");
            Assert.Greater(got[3], got[1] + 20, "D4 (the ding octave's pair) rings far more than A3");
        }

        [Test]
        public void TV7_ResetSilencesEverything()
        {
            var cmds = new List<object>
            {
                new Dictionary<string, object> { ["op"] = "strike", ["field"] = 5.0, ["vel"] = 0.8, ["u"] = 0.3, ["v"] = 0.2, ["tech"] = 0.0, ["at"] = 0.0 },
                new Dictionary<string, object> { ["op"] = "reset", ["at"] = 12000.0 },
            };
            int active = -1;
            float[] y = RenderCmds(cmds, 24000, false, onBlock: (r, f) => active = r.ActiveGroups());
            var tv = TV.Obj("TV7");
            for (int i = 12000; i < 24000; i++) Assert.AreEqual(0f, y[i], "frame " + i);
            Assert.AreEqual(0, active);
            Assert.AreEqual(tv.Num("beforeResetRmsDb"), HandpanRig.RmsDb(y, 9600, 12000), 1e-4);
        }

        [Test]
        public void TV9_SixtySecondsAfterEveryFieldNothingIsLeft()
        {
            var tv = TV.Obj("TV9");
            using var rig = new HandpanRig();
            for (int k = 0; k < 9; k++) rig.Strike(100 + 480 * k, k, 0.9, 0.35, 0.2);
            int sub = 0;
            var buf = new float[60 * 48000];
            rig.Render(buf, 0, buf.Length, 256, f => sub += rig.Subnormals());
            Assert.AreEqual(0, rig.ActiveGroups(), "every group retired");
            Assert.AreEqual(0, sub, "no subnormal state in 60 s");
            Assert.AreEqual((long)tv.Num("retired"), rig.S->Retired, "the same retirements as the reference");
            foreach (float v in buf) Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
        }

        [Test]
        public void TV10_BlockSizeChangesOnlyThePoleUpdateTimes()
        {
            var cmds = new List<object> { new Dictionary<string, object> { ["op"] = "strike", ["field"] = 3.0, ["vel"] = 0.6, ["u"] = 0.35, ["v"] = 0.2, ["tech"] = 0.0, ["at"] = 300.0 } };
            float[] a = RenderCmds(cmds, 4800, false, block: 256), b = RenderCmds(cmds, 4800, false, block: 64);
            Assert.AreEqual(TV.Obj("TV10").Num("maxAbsDiff_256_vs_64"), MaxDiff(a, b), 1e-7);
        }

        [Test]
        public void TV11_TheMeasuredVoiceMatches()
        {
            var tv = TV.Obj("TV11");
            float[] y = RenderCmds(tv.Arr("cmds"), 96000, false, HandpanVoice.Measured);
            List<object> first = tv.Arr("first8afterOnset");
            for (int k = 0; k < first.Count; k++) Assert.AreEqual((float)HandpanJson.D(first[k]), y[100 + k], $"frame {100 + k}");
            Assert.AreEqual(tv.Text("fnv"), HandpanJson.Fnv(y), "TV11 FNV");
            Assert.AreEqual(tv.Num("rmsDb_48000_96000"), HandpanRig.RmsDb(y, 48000, 96000), 1e-4);
            AssertBurstClose(y, RenderCmds(tv.Arr("cmds"), 96000, true, HandpanVoice.Measured), "TV11");
        }

        [Test]
        public void LevelsAreFinitePublishedAndSettleToZero()
        {
            using var rig = new HandpanRig(burst: true);
            rig.Strike(0, 1, 0.7, 0.35, 0.2);
            rig.Render(4800);
            Assert.Greater(rig.Sound.Level(1), 1e-4f, "A3's level is published");
            Assert.AreEqual(0f, rig.Sound.Level(5), "an unstruck field reads 0");
            Assert.AreEqual(0f, rig.Sound.Level(12), "a bad index reads 0");
            rig.Render(48000 * 12);
            Assert.AreEqual(0f, rig.Sound.Level(1), "after the decay");
        }
    }
}

namespace MusicRoom.Tests.Audio
{
    /// <summary>Review items 13 and 16 measured on the C# unit (see Assets/MusicRoom/Handpan/README.md, review items).</summary>
    public class HandpanReviewTests
    {
        [Test]
        public void TenFingersAndASlapAtOnceNeverPassFullScale()
        {
            // handpan.md 3.8's worst case: all 9 fields ff + a ding slap within 4 ms (+11 dBFS without the clip)
            using var rig = new HandpanRig(burst: true);
            for (int i = 0; i < 9; i++) rig.Strike(100 + 20 * i, i, 1.0, 0.35, 0.35);
            rig.Strike(300, 0, 1.0, 0, 0, MusicRoom.Audio.Burst.HandpanTech.Slap);
            float[] y = rig.Render(48000);
            float peak = NullTest.Peak(y);
            float step = NullTest.MaxStep(y);
            UnityEngine.Debug.Log($"[Handpan] worst case through the soft clip: peak {peak:F3} (limit 1.0), largest sample step {step:F3}");
            Assert.That(peak, Is.LessThanOrEqualTo(1.0f), "never above full scale");
            Assert.That(peak, Is.GreaterThan(0.95f), "the case does reach the clip (it is +11 dBFS raw)");
            foreach (float v in y) Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
        }

        [Test]
        public void TheDingsSpectrumAsTheCriticMeasuredIt()
        {
            // review item 13: the ding struck at the apex, vel 0.6; levels re f0 in a 10-100 ms frame
            using var rig = new HandpanRig();
            rig.Strike(0, 0, 0.6, 0, 0);
            float[] y = rig.Render(48000);
            double G(double hz) { int n = 4320, from = 480; double w = 2 * System.Math.PI * hz / 48000, c = 2 * System.Math.Cos(w), s1 = 0, s2 = 0; for (int i = 0; i < n; i++) { double h = 0.5 - 0.5 * System.Math.Cos(2 * System.Math.PI * i / n); double s = y[from + i] * h + c * s1 - s2; s2 = s1; s1 = s; } return System.Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2); }
            double f0 = G(146.83), oct = G(293.66), fifth = G(440.5), four = G(587.3);
            double Db(double a) => 20 * System.Math.Log10(a / f0);
            UnityEngine.Debug.Log($"[Handpan] ding at the apex, vel 0.6, 10-100 ms: octave {Db(oct):F1} dB, fifth {Db(fifth):F1} dB, 4 f0 (D4's octave) {Db(four):F1} dB re f0");
            Assert.Less(Db(four), Db(fifth), "as the critic found: 587 Hz sits under the fifth (the proposed extra path is not built; README)");
        }
    }
}

namespace MusicRoom.Tests.Audio
{
    /// <summary>HandpanDsp.RenderUnit, the hook the graph will call (phase B), driven through a hand-built UnitSlot: the same
    /// sound as the test entry, chunked long blocks, the Reset flag, nothing before Ready.</summary>
    public unsafe class HandpanUnitHookTests
    {
        static UnitSlot* Slot(HandpanState* s, int maxFrames, out float* outp)
        {
            var u = (UnitSlot*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Malloc(sizeof(UnitSlot), 64, Unity.Collections.Allocator.Persistent);
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(u, sizeof(UnitSlot));
            outp = (float*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Malloc(4L * maxFrames, 64, Unity.Collections.Allocator.Persistent);
            u->State = s; u->Channels = 1; u->Out = outp; u->MaxFrames = maxFrames; u->SampleRate = 48000; u->Share = 1f;   // as AudioGraphHost sets a fresh slot
            u->Due = (UnitEvent*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Malloc(sizeof(UnitEvent) * 16, 64, Unity.Collections.Allocator.Persistent);
            return u;
        }

        static void Free(UnitSlot* u)
        {
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free(u->Out, Unity.Collections.Allocator.Persistent);
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free(u->Due, Unity.Collections.Allocator.Persistent);
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.Free(u, Unity.Collections.Allocator.Persistent);
        }

        [Test]
        public void TheGraphHookPlaysTheSameSoundInChunksAndHonoursReset()
        {
            // reference: the rig's Burst entry in 1024-frame blocks
            using var refRig = new HandpanRig(burst: true);
            refRig.Strike(100, 1, 0.7, 0.35, 0.2);
            refRig.Strike(1500, 0, 0.8, 0, 0, HandpanTech.Slap);
            float[] want = refRig.Render(4096, 1024);
            // the hook: 2048-frame graph blocks (chunked at the state's 1024), events handed over as the graph does
            using var rig = new HandpanRig(burst: false);
            rig.S->Exact = 0;
            UnitSlot* u = Slot(rig.S, 4096, out float* outp);
            try
            {
                var got = new float[4096];
                for (int b = 0; b < 2; b++)
                {
                    for (int i = 0; i < 2048; i++) outp[i] = 0f;
                    u->BlockStart = 2048L * b; u->Frames = 2048; u->DueCount = 0;
                    if (b == 0)
                    {
                        u->Due[0] = new UnitEvent { At = 100, Offset = 100, Seq = 0, C = HandpanCommands.Strike(1, 0.7, 0.35f, 0.2f, HandpanTech.Tip, 0) };
                        u->Due[1] = new UnitEvent { At = 1500, Offset = 1500, Seq = 1, C = HandpanCommands.Strike(0, 0.8, 0f, 0f, HandpanTech.Slap, 0) };
                        u->DueCount = 2;
                    }
                    HandpanDsp.RenderUnit(u);
                    for (int i = 0; i < 2048; i++) got[2048 * b + i] = outp[i];
                }
                float d = 0;
                for (int i = 0; i < 4096; i++) d = System.Math.Max(d, System.Math.Abs(got[i] - want[i] * HandpanDsp.GraphTrim));   // the graph path's -3 dB parity trim
                UnityEngine.Debug.Log($"[Handpan] RenderUnit (2048-frame blocks, chunked) vs RenderDirect (1024): max |diff| {d:E2}");
                Assert.That(d, Is.LessThanOrEqualTo(1e-5f), "the hook renders the reference's sound (trimmed 3 dB)");
                Assert.That(System.Math.Pow(10, HandpanDsp.GraphTrimDb / 20.0), Is.EqualTo(HandpanDsp.GraphTrim).Within(1e-6));
                // Reset (the graph resumes the unit after classic sound): silence from the next block on
                u->Flags = UnitFlags.Reset; u->DueCount = 0; u->Frames = 256;
                for (int i = 0; i < 256; i++) outp[i] = 0f;
                HandpanDsp.RenderUnit(u);
                for (int i = 0; i < 256; i++) Assert.AreEqual(0f, outp[i], "nothing rings after Reset");
                Assert.AreEqual(1, rig.S->Resets);
                // not Ready: nothing is read or played
                rig.S->Ready = 0;
                u->Flags = 0; u->Due[0] = new UnitEvent { Offset = 0, C = HandpanCommands.Strike(1, 0.7, 0.35f, 0.2f, HandpanTech.Tip, 0) }; u->DueCount = 1;
                HandpanDsp.RenderUnit(u);
                for (int i = 0; i < 256; i++) Assert.AreEqual(0f, outp[i]);
            }
            finally { Free(u); }
        }
    }
}
