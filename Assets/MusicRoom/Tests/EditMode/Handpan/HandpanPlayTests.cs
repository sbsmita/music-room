// HandpanPlay against the lab's 34 scripted-hand cases (handpan.md 9.2; the handpan lab's interaction-tests.mjs, frames at
// 90 Hz with both hands' 5 tips, radii, palm centre and normal and the flags: no sample interval). Two references:
//   - Reference~/interaction-replay.json: the Node logic fed exactly these stored frames: the C# code must emit the same
//     events on the same frames (velocity within 0.001: both round to 3 decimals);
//   - interaction-vectors.json `expected` (the Node logic on the unrounded frames): within the spec's tolerance
//     (time +-1 frame, velocity +-0.005), with the same fields, slots, flags and neighbours.
// Plus the lesson block paths (HandpanBlockStartsInFrontOfThePlayer, 8.1) and the allocation check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicRoom.Audio.Burst;
using MusicRoom.Handpan;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Handpan
{
    public class HandpanPlayTests
    {
        static Dictionary<string, object> s_vec, s_replay;
        static Dictionary<string, object> Vec => s_vec ?? (s_vec = HandpanJson.O(HandpanJson.Load(Path.Combine(HandpanJson.ReferenceDir("Handpan"), "interaction-vectors.json"))));
        static Dictionary<string, object> Replay => s_replay ?? (s_replay = HandpanJson.O(HandpanJson.Load(Path.Combine(HandpanJson.ReferenceDir("Handpan"), "interaction-replay.json"))));

        public static IEnumerable<string> CaseNames()
        {
            // read the names without the test context (TestCaseSource runs at discovery)
            var d = HandpanJson.O(HandpanJson.Load(Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Handpan/Reference~/interaction-replay.json")));
            foreach (object c in d.Arr("cases")) yield return HandpanJson.O(c).Text("name");
        }

        static D3 V(object o) { var a = HandpanJson.A(o); return new D3(HandpanJson.D(a[0]), HandpanJson.D(a[1]), HandpanJson.D(a[2])); }

        public static void Fill(HandpanFrame f, Dictionary<string, object> fr)
        {
            f.T = fr.Num("t"); f.Dt = fr.Num("dt");
            List<object> hands = fr.Arr("hands");
            for (int h = 0; h < 2; h++)
            {
                var H = HandpanJson.O(hands[h]);
                HandpanHandInput I = f.Hands[h];
                I.Tracked = H.Flag("tracked"); I.HighConfidence = H.Flag("highConf"); I.Inferred = H.Flag("inferred"); I.SystemGesture = H.Flag("sysGesture");
                List<object> tips = H.Arr("tips"), radii = H.Arr("radii");
                for (int k = 0; k < 5; k++) { I.Tips[k] = V(tips[k]); I.Radii[k] = HandpanJson.D(radii[k]); }
                var palm = H.Obj("palm");
                I.PalmCentre = V(palm["c"]); I.PalmNormal = V(palm["n"]);
            }
        }

        public static List<HandpanEvent> Run(Dictionary<string, object> c)
        {
            var play = new HandpanPlay();
            var frame = new HandpanFrame();
            var got = new List<HandpanEvent>();
            foreach (object fo in c.Arr("frames"))
            {
                Fill(frame, HandpanJson.O(fo));
                int n = play.Step(frame);
                for (int k = 0; k < n; k++) got.Add(play.Events[k]);
            }
            Assert.AreEqual(0, play.Overflow);
            return got;
        }

        static Dictionary<string, object> CaseOf(Dictionary<string, object> doc, string name) =>
            doc.Arr("cases").Select(HandpanJson.O).First(x => x.Text("name") == name);

        static string TypeName(HandpanEventType t) => t == HandpanEventType.Strike ? "strike" : t == HandpanEventType.Damp ? "damp" : t == HandpanEventType.Release ? "release" : "hint";
        static string TechName(HandpanTech t) => t == HandpanTech.Tip ? "tip" : t == HandpanTech.Slap ? "slap" : "tak";
        static string KindName(HandpanDampSource k) => k == HandpanDampSource.Finger ? "finger" : k == HandpanDampSource.Palm ? "palm" : "shell";

        static void Compare(string name, List<HandpanEvent> got, List<object> want, double tTol, double vTol, string what)
        {
            string Show(IEnumerable<string> x) => string.Join(" | ", x);
            string gotS = Show(got.Select(e => e.ToString()));
            Assert.AreEqual(want.Count, got.Count, $"{name} ({what}): event count; got {gotS}");
            for (int k = 0; k < want.Count; k++)
            {
                var w = HandpanJson.O(want[k]);
                HandpanEvent e = got[k];
                string at = $"{name} ({what}) event {k}: got {e}";
                Assert.AreEqual(w.Text("type"), TypeName(e.Type), at + " type");
                Assert.AreEqual(w.Num("t"), e.T, tTol, at + " time");
                if (e.Type == HandpanEventType.Hint) { Assert.AreEqual(w.Text("id"), e.Hint, at); continue; }
                Assert.AreEqual(HandpanJson.I(w["field"]), e.Field, at + " field");
                if (e.Type == HandpanEventType.Strike)
                {
                    Assert.AreEqual(w.Text("tech"), TechName(e.Tech), at + " tech");
                    Assert.AreEqual(w.Num("vel"), e.Vel, vTol, at + " vel");
                    Assert.AreEqual(HandpanJson.I(w["hand"]), e.Hand, at + " hand");
                    if (e.Field == HandpanSpec.Tak)
                    {
                        List<object> nb = w.Arr("neighbours");
                        Assert.AreEqual(HandpanJson.I(HandpanJson.O(nb[0])["field"]), e.N0, at + " neighbour 0");
                        Assert.AreEqual(HandpanJson.I(HandpanJson.O(nb[1])["field"]), e.N1, at + " neighbour 1");
                        Assert.AreEqual(HandpanJson.O(nb[0]).Num("w"), e.W0, 0.002, at + " w0");
                        Assert.AreEqual(HandpanJson.O(nb[1]).Num("w"), e.W1, 0.002, at + " w1");
                    }
                    else
                    {
                        Assert.AreEqual(w.Num("u"), e.U, Math.Max(vTol, 0.0011), at + " u");
                        Assert.AreEqual(w.Num("v"), e.V, Math.Max(vTol, 0.0011), at + " v");
                    }
                }
                else
                {
                    Assert.AreEqual(HandpanJson.I(w["slot"]), e.Slot, at + " slot");
                    if (e.Type == HandpanEventType.Damp)
                    {
                        Assert.AreEqual(w.Text("kind"), KindName(e.Kind), at + " kind");
                        Assert.AreEqual(w.Num("strength"), e.Strength, 0.0011, at + " strength");
                        Assert.AreEqual(w.Flag("afterStrike"), e.AfterStrike, at + " afterStrike");
                    }
                }
            }
        }

        [TestCaseSource(nameof(CaseNames))]
        public void ScriptedHandsGiveTheReferenceEvents(string name)
        {
            var c = CaseOf(Vec, name);
            List<HandpanEvent> got = Run(c);
            Compare(name, got, CaseOf(Replay, name).Arr("events"), 1e-6, 0.0011, "replay, exact");
            Compare(name, got, c.Arr("expected"), 1.01 / 90, 0.005, "spec expected");
        }

        [Test]
        public void ThePredictedPoseAndThe30HzTrackerGiveTheSameStrike()
        {
            // review item 8 (sampleDt): Unity's predicted pose moves every frame; a 30 Hz tracker pose repeats for 3 frames.
            // Speeds come from distinct samples and the frame clock, so both read the same stroke (within 0.05).
            HandpanEvent a = Run(CaseOf(Vec, "predicted-90hz")).Single(e => e.Type == HandpanEventType.Strike);
            HandpanEvent b = Run(CaseOf(Vec, "tracker-30hz")).Single(e => e.Type == HandpanEventType.Strike);
            Assert.AreEqual(a.Field, b.Field);
            Assert.AreEqual(a.Vel, b.Vel, 0.05, "velocity independent of the tracker rate");
            Assert.That(a.Vel, Is.GreaterThan(0.5), "a 0.6 m/s tap plays at about 0.6, not 3x too soft");
        }

        [Test]
        public void VelocityCurvesMatchTheReferenceCheckpoints()
        {
            foreach (object po in Vec.Obj("velocityCurve").Arr("finger"))
            {
                var p = HandpanJson.A(po);
                Assert.AreEqual(HandpanJson.D(p[1]), HandpanMath.FingerVel(HandpanJson.D(p[0])), 1e-4, "finger " + p[0]);
            }
            foreach (object po in Vec.Obj("velocityCurve").Arr("palm"))
            {
                var p = HandpanJson.A(po);
                Assert.AreEqual(HandpanJson.D(p[1]), HandpanMath.PalmVel(HandpanJson.D(p[0])), 1e-4, "palm " + p[0]);
            }
        }

        [Test]
        public void StepAllocatesNothing()
        {
            var c = CaseOf(Vec, "lift-hint");
            var frames = c.Arr("frames").Select(HandpanJson.O).ToList();
            var play = new HandpanPlay();
            var f = new HandpanFrame();
            var filled = new HandpanFrame[frames.Count];
            for (int k = 0; k < frames.Count; k++) { filled[k] = new HandpanFrame(); Fill(filled[k], frames[k]); }
            play.Step(filled[0]);
            int i = 1;
            Assert.That(() => { for (; i < filled.Length; i++) play.Step(filled[i]); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void HandpanBlockStartsInFrontOfThePlayer()
        {
            var doc = HandpanJson.O(HandpanJson.Load(Path.Combine(HandpanJson.ReferenceDir("Handpan"), "handpan-lessons.json")));
            List<object> paths = doc.Obj("tutorial").Arr("blockPaths");
            D3 eyes = HandpanLessonPath.SeatEyes;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
            {
                HandpanBlockPath b = HandpanLessonPath.Of(i, HandpanPlace.Default, eyes);
                string n = HandpanSpec.Fields[i].Note;
                Assert.That(b.StartToEye, Is.GreaterThanOrEqualTo(0.25), n + ": at least 0.25 m from the eyes");
                Assert.That(b.StartZ, Is.GreaterThan(0.15), n + ": more than 0.15 m in front of the eyes' plane");
                var w = HandpanJson.O(paths[i]);
                D3 ws = V(w["start"]), wa = V(w["apex"]);
                Assert.That(D3.Dist(ws, b.Start), Is.LessThan(0.001 + 0.0005), n + " start (lessons.mjs, 1 mm)");
                Assert.That(D3.Dist(wa, b.Apex), Is.LessThan(0.0015), n + " apex");
                Assert.AreEqual(w.Num("room"), b.Room, 0.0015, n + " travel");
            }
        }

        [Test]
        public void GeometryMatchesTheLayout()
        {
            Assert.AreEqual(0.3629, HandpanSpec.CapR, 1e-4);
            Assert.AreEqual(0.2971, HandpanSpec.RimArc, 5e-4);   // the spec rounds it to 0.2974
            Assert.AreEqual(91.35, HandpanMath.HelmholtzHz(), 0.05, "the cavity of the sound unit");
            for (int i = 0; i < 9; i++) Assert.AreEqual(HandpanTables.Midi[i], HandpanSpec.Fields[i].Midi, "one scale for sound and layout");
            // a point on a field is that field; the steel between A3 and C4 is the tak; beyond the rim is nothing
            HandpanMath.MapAt(4, 0.3, 0.1, out double X, out double Z);
            Assert.AreEqual(4, HandpanMath.HitField(X, Z).Field);
            Assert.AreEqual(HandpanSpec.Tak, HandpanMath.HitField(0, -0.19).Field);
            Assert.AreEqual(-1, HandpanMath.HitField(0.33, 0).Field);
            // the relief gradient is the derivative of the relief (the mesh normals)
            foreach (var (x, z) in new[] { (0.01, 0.02), (0.05, -0.17), (-0.13, -0.05), (0.07, 0.15) })
            {
                HandpanMath.ReliefGradient(x, z, out double gx, out double gz);
                double h = 1e-6, nx = (HandpanMath.Relief(x + h, z) - HandpanMath.Relief(x - h, z)) / (2 * h), nz = (HandpanMath.Relief(x, z + h) - HandpanMath.Relief(x, z - h)) / (2 * h);
                Assert.AreEqual(nx, gx, 1e-4, $"d/dX at ({x}, {z})"); Assert.AreEqual(nz, gz, 1e-4, $"d/dZ at ({x}, {z})");
            }
        }
    }
}
