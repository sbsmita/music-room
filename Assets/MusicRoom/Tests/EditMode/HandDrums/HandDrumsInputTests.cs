// HandDrumsInput, the bare-hand stroke logic, against the lab's reference (handdrums.md 3.7, 6.2 tests 16, 17): the 18
// scripted cases at the 4 app/tracker rate pairs (judged by the reference's rules AND event by event against
// out/interaction-cases.json), the LOW tracker rate (reported), the 88 seeded-noise cases (slaps and opens at 2-4 m/s,
// 60 s noisy holds, slow placements, one landing one stroke), the palm-lift law and its C02 trace, the u law, the
// 336-pose flat-hand sweep (stroke kinds and patch parts, and through the unit: no stroke at u >= .5 more than 6 dB A
// under the open tone), and no allocation per frame.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MusicRoom.Audio.Burst;
using MusicRoom.HandDrums;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.HandDrums
{
    [Serializable] public class IcFile { public IcCase[] cases; public IcLow[] low; public IcPalmLift palmLift; }
    [Serializable] public class IcCase { public string id; public int app, tracker; public bool pass; public IcEvent[] events; }
    [Serializable] public class IcLow { public string id; public bool pass; }
    [Serializable] public class IcEvent { public double ms, u, v, tStar; public string op, kind, label, reason; public int drum, hand; public IcPatch[] patches; public IcPt[] pts; }
    [Serializable] public class IcPatch { public string part, shape; public double uScale, dly; public IcPt[] pts; }
    [Serializable] public class IcPt { public string part; public double rho, theta; }
    [Serializable] public class IcPalmLift { public IcLiftPt[] target; public IcTrace[] trace; }
    [Serializable] public class IcLiftPt { public double depthM, liftM; }
    [Serializable] public class IcTrace { public double ms, liftMm; }
    [Serializable] public class SwFile { public SwRow[] rows; public int fails; }
    [Serializable] public class SwRow { public int drum, dir; public double curl, pitch, rho, u, aReOpenDb; public string kind, parts; }

    public class HandDrumsInputTests
    {
        static string Dir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/HandDrums/Reference~");
        static T Json<T>(string f) => JsonUtility.FromJson<T>(File.ReadAllText(Path.Combine(Dir, f)));
        static IcFile s_ic;
        static IcFile Ic => s_ic ??= Json<IcFile>("interaction-cases.json");
        static readonly string[] PartNames = { "palm", "heel", "fingers", "tips", "base", "pads" };
        public static IEnumerable<string> CaseIds() => Scripted.Cases().Select(c => c.Id);

        // ---------------------------------------------------------------- the 18 scripted cases x 4 rate pairs

        [Test]
        public void ScriptedCaseMatchesTheReference([ValueSource(nameof(CaseIds))] string id)
        {
            var c = Scripted.Cases().First(q => q.Id == id);
            var log = new StringBuilder($"[HandDrums] {id}:");
            foreach (var (app, tr) in Scripted.CaseRates)
            {
                var input = Scripted.Run(c, app, tr);
                var (ok, why) = Scripted.Judge(c, input);
                var ev = Scripted.Events(input);
                log.Append($" {app}/{tr} {(ok ? "pass" : "FAIL " + why)} [{Describe(ev)}];");
                Assert.IsTrue(ok, $"{id} at {app}/{tr}: {why} | {Describe(ev)}");
                var want = Ic.cases.First(q => q.id == id && q.app == app && q.tracker == tr);
                Assert.IsTrue(want.pass, "the reference passes it too");
                CompareEvents(id + $" {app}/{tr}", ev, want.events);
            }
            Debug.Log(log.ToString());
        }

        static string Describe(List<HdEvent> ev) => string.Join(" | ", ev.Select(e => $"{e.Ms:F1} {Scripted.Op(e.Op)}{(e.Op == HdOp.Stroke ? ":" + Scripted.Kind(e.Kind) : "")}{(e.Label != HdLabel.None ? "(" + Scripted.Label(e.Label) + ")" : "")}{(e.Op == HdOp.Stroke ? $" u{e.U:F3}" : "")} h{e.Hand} d{e.Drum}"));

        /// <summary>The same events as the reference, in order: op, drum, hand, kind, label, time (0.15 ms), u (.002), the
        /// crossing time t* (0.05 ms), the patches' parts, shapes and points (.002), the rests' points.</summary>
        static void CompareEvents(string what, List<HdEvent> got, IcEvent[] want)
        {
            Assert.That(got.Count, Is.EqualTo(want.Length), what + ": event count (" + Describe(got) + ")");
            for (int i = 0; i < want.Length; i++)
            {
                var g = got[i]; var w = want[i];
                string at = $"{what} event {i} ({w.op} {w.kind} at {w.ms} ms)";
                Assert.That(Scripted.Op(g.Op), Is.EqualTo(w.op), at + " op");
                Assert.That(g.Drum, Is.EqualTo(w.drum), at + " drum");
                Assert.That(g.Hand, Is.EqualTo(w.hand), at + " hand");
                Assert.That(g.Ms, Is.EqualTo(w.ms).Within(0.15), at + " ms");
                if (w.op == "stroke")
                {
                    Assert.That(Scripted.Kind(g.Kind), Is.EqualTo(w.kind), at + " kind");
                    Assert.That(Scripted.Label(g.Label), Is.EqualTo(string.IsNullOrEmpty(w.label) ? null : w.label), at + " label");
                    AssertU(g.U, g.V, w.u, w.v, at);
                    Assert.That(g.TStar, Is.EqualTo(w.tStar).Within(0.05), at + " t*");
                    Assert.That(g.PatchCount, Is.EqualTo(w.patches.Length), at + " patch count");
                    for (int p = 0; p < w.patches.Length; p++)
                    {
                        var gp = g.Patch(p); var wp = w.patches[p];
                        Assert.That(PartNames[(int)gp.Part], Is.EqualTo(wp.part), at + $" patch {p} part");
                        Assert.That(gp.Shape == PatchShape.Line ? "line" : "points", Is.EqualTo(wp.shape), at + $" patch {p} shape");
                        Assert.That(gp.UScale, Is.EqualTo(wp.uScale).Within(1e-9), at + $" patch {p} u scale");
                        Assert.That(gp.Delay, Is.EqualTo(wp.dly).Within(1e-9), at + $" patch {p} delay");
                        Assert.That(gp.Count, Is.EqualTo(wp.pts.Length), at + $" patch {p} points");
                        for (int k = 0; k < wp.pts.Length; k++)
                        {
                            Assert.That(gp.Rho(k), Is.EqualTo(wp.pts[k].rho).Within(0.002), at + $" patch {p} point {k} rho");
                            Assert.That(gp.Theta(k), Is.EqualTo(wp.pts[k].theta).Within(0.002), at + $" patch {p} point {k} theta");
                        }
                    }
                }
                else if (w.op == "rest")
                {
                    Assert.That(g.RestCount, Is.EqualTo(w.pts.Length), at + " rest points");
                    for (int k = 0; k < w.pts.Length; k++)
                    {
                        HandPart part = k == 0 ? g.RestPart0 : g.RestPart1;
                        Assert.That(PartNames[(int)part], Is.EqualTo(w.pts[k].part), at + $" rest point {k} part");
                        Assert.That(k == 0 ? g.RestRho0 : g.RestRho1, Is.EqualTo(w.pts[k].rho).Within(0.002), at + $" rest point {k} rho");
                    }
                }
            }
        }

        /// <summary>u matches the reference, or differs only by which group led a rigid hand: palm and finger points cross
        /// at the same speed (to rounding noise), and the reference's lead (cross.reduce, strict >) is decided by that noise
        /// in Node as here; V0 is 0.30 for the palm group and 0.25 for the fingers, so the same 1 m/s gives u .537 or .553.
        /// The speeds must agree, and each u must be one group's law of that speed.</summary>
        public static int TieBreaks;
        static void AssertU(double gu, double gv, double wu, double wv, string at)
        {
            if (Math.Abs(gu - wu) <= 0.002) return;
            Assert.That(gv, Is.EqualTo(wv).Within(0.003), at + " lead speed");
            bool gLaw = Math.Abs(gu - HdK.UOf(gv, true)) <= 0.002 || Math.Abs(gu - HdK.UOf(gv, false)) <= 0.002;
            bool wLaw = Math.Abs(wu - HdK.UOf(wv, true)) <= 0.002 || Math.Abs(wu - HdK.UOf(wv, false)) <= 0.002;
            Assert.IsTrue(gLaw && wLaw, $"{at} u {gu} vs {wu}: not a palm / fingers tie at {gv} m/s");
            TieBreaks++;
            Debug.Log($"[HandDrums] tie-break {at}: u {gu:F3} vs the reference's {wu:F3} at {gv:F3} m/s (the other group led)");
        }

        [Test]
        public void TheLowTrackerRateIsReported()
        {
            var cases = Scripted.Cases();
            int pass = 0; var fails = new List<string>();
            foreach (var c in cases)
            {
                var (ok, why) = Scripted.Judge(c, Scripted.Run(c, 72, 30));
                var want = Ic.low.First(q => q.id == c.Id);
                if (ok) pass++; else fails.Add($"{c.Id} ({why})");
                Assert.That(ok, Is.EqualTo(want.pass), c.Id + " at 72/30: same verdict as the reference");
            }
            Debug.Log($"[HandDrums] LOW tracker 72/30 (reported, not required): {pass}/{cases.Count}; degraded: {string.Join(", ", fails)}");
        }

        // ---------------------------------------------------------------- the seeded-noise cases (statCases)

        [Test, Timeout(600000)]
        public void SlapsAndOpenTonesAtNaturalSpeedWithNoise()
        {
            var log = new StringBuilder("[HandDrums] C03b-d / C01b-d (sigma 2 mm per joint, 40 trials):");
            foreach (var (name, b, from, depth, want) in new[] { ("slap", Scripted.SLAP, 0.13, -0.02, HdKind.Slap), ("open", Scripted.OPEN, 0.08, 0.012, HdKind.Open) })
                foreach (var (app, tr) in Scripted.CaseRates)
                    foreach (int v in new[] { 2, 3, 4 })
                    {
                        int hit = 0;
                        for (int k = 0; k < 40; k++)
                        {
                            var c = new Scripted.Case { Id = name, Seconds = 0.6, Hands = { [1] = Scripted.HandScript.Of(Scripted.Motion(Scripted.StrokeKeys(0.1 + k * 0.0013, b, v, from, depth))) } };
                            var s = Scripted.Events(Scripted.Run(c, app, tr, 0.002, 0, (uint)(1000 + k + 97 * v))).FindAll(e => e.Op == HdOp.Stroke);
                            if (s.Count == 1 && s[0].Kind == want) hit++;
                        }
                        log.Append($" {name} {v} m/s {app}/{tr} {hit}/40;");
                        Assert.That(hit / 40.0, Is.GreaterThanOrEqualTo(0.9), $"{name} at {v} m/s, {app}/{tr}: >= 90 % {want}");
                    }
            Debug.Log(log.ToString());
        }

        [Test, Timeout(1200000)]
        public void NoisyHoldsNeverStrike()
        {
            var log = new StringBuilder("[HandDrums] C08b (sigma 2 mm per joint + 2 mm whole hand, 60 s holds):");
            int runs = 0, strokes = 0;
            foreach (var (app, tr) in Scripted.Rates)
                foreach (double lift in new[] { -0.004, 0, 0.006, 0.012 })
                    foreach (string where in new[] { "centre", "edge" })
                    {
                        HP b = where == "centre" ? HP.Of(0.1, 0, Math.PI, 0.1) : Scripted.OPEN;
                        double l = lift;
                        var c = new Scripted.Case { Id = "C08b", Seconds = 61, Hands = { [1] = new Scripted.HandScript { Fn = t => b.L(t < 1 ? 0.06 - (0.06 - l) * t : l) } } };
                        uint seed = (uint)(7 + (int)Math.Round(lift * 1e4) + (where == "edge" ? 500 : 0) + app + tr);
                        var ev = Scripted.Events(Scripted.Run(c, app, tr, 0.002, 0.002, seed));
                        int s = ev.Count(e => e.Op == HdOp.Stroke);
                        runs++; strokes += s;
                        if (s > 0) log.Append($" {lift * 1000} mm {where} {app}/{tr}: {s} strokes;");
                        Assert.That(s, Is.EqualTo(0), $"a noisy hold at {lift * 1000} mm ({where}, {app}/{tr}) never strikes");
                    }
            Debug.Log(log + $" {runs} runs, {strokes} strokes ({runs} minutes held)");
        }

        [Test, Timeout(600000)]
        public void SlowPlacementsNeverStrike()
        {
            int total = 0, placements = 0;
            foreach (var (app, tr) in Scripted.Rates)
                foreach (string where in new[] { "centre", "edge" })
                    for (int k = 0; k < 20; k++)
                    {
                        double vP = 0.05 + 0.05 * (k / 19.0), end = -0.004, tP = (0.06 - end) / vP;
                        HP b = where == "centre" ? HP.Of(0.1, 0, Math.PI, 0.1) : Scripted.OPEN;
                        var c = new Scripted.Case { Id = "C09b", Seconds = tP + 1, Hands = { [1] = new Scripted.HandScript { Fn = t => b.L(t < tP ? 0.06 - vP * t : end) } } };
                        uint seed = (uint)(300 + k + app * 7 + tr + (where == "edge" ? 50 : 0));
                        int s = Scripted.Events(Scripted.Run(c, app, tr, 0.002, 0.002, seed)).Count(e => e.Op == HdOp.Stroke);
                        total += s; placements++;
                        Assert.That(s, Is.EqualTo(0), $"a slow placement at {vP:F3} m/s ({where}, {app}/{tr}) never strikes");
                    }
            Debug.Log($"[HandDrums] C09b: {placements} slow placements (0.05-0.1 m/s, noise 2 + 2 mm), {total} strokes");
        }

        [Test]
        public void OneLandingIsOneStroke()
        {
            var log = new StringBuilder("[HandDrums] C13b:");
            foreach (var (app, tr) in new[] { (72, 30), (72, 60), (72, 72), (90, 90), (90, 60), (90, 30) })
                foreach (double v in new[] { 0.3, 0.6 })
                    foreach (var (first, pitch) in new[] { ("fingers", -0.15), ("palm", 0.15) })
                    {
                        var c = new Scripted.Case { Id = "C13b", Seconds = 1.4, Hands = { [1] = Scripted.HandScript.Of(Scripted.Motion(Scripted.StrokeKeys(0.1, HP.Of(0.1, 0, Math.PI, 0.1, pitch), v, 0.1, 0.03))) } };
                        var s = Scripted.Events(Scripted.Run(c, app, tr)).FindAll(e => e.Op == HdOp.Stroke);
                        log.Append($" {first} {v} {app}/{tr}: {string.Join("+", s.Select(e => Scripted.Kind(e.Kind)))};");
                        Assert.That(s.Count, Is.EqualTo(1), $"{first}-first at {v} m/s, {app}/{tr}: exactly one stroke");
                    }
            Debug.Log(log.ToString());
        }

        // ---------------------------------------------------------------- laws: u, palm lift (handdrums.md 4.5)

        [Test]
        public void TheULawAndThePalmLiftMatchTheReference()
        {
            double[] vf = { 0.2, 0.25, 0.5, 1, 2, 3 }, uf = { 0.05, 0.05, 0.2561129, 0.5526068, 1, 1 };
            for (int i = 0; i < vf.Length; i++) Assert.That(HdK.UOf(vf[i], false), Is.EqualTo(uf[i]).Within(1e-6), "uOf fingers " + vf[i]);
            double[] vp = { 0.3, 0.5, 1, 2 }, up = { 0.05, 0.2235665, 0.5373464, 1 };
            for (int i = 0; i < vp.Length; i++) Assert.That(HdK.UOf(vp[i], true), Is.EqualTo(up[i]).Within(1e-6), "uOf palm " + vp[i]);
            foreach (var p in Ic.palmLift.target) Assert.That(HdK.PalmLiftTarget(p.depthM), Is.EqualTo(p.liftM).Within(1e-9), "palmLiftTarget " + p.depthM);
            Assert.That(HdK.StepPalmLift(0.05, 0, 1.0 / 72), Is.EqualTo(0.02868767).Within(1e-7), "release over one 72 Hz frame");
            // the C02 bass's lift trace at 72 Hz (no new-sample flags: every frame a new pose, as the reference's trace)
            var input = new HandDrumsInput();
            var keys = Scripted.Motion(Scripted.StrokeKeys(0.1, Scripted.BASS, 1.0));
            var hand = new HdHandSample();
            var trace = new StringBuilder("[HandDrums] palm lift trace (C02 bass, mm):");
            int j = 0;
            for (int i = 0; i * (1.0 / 72) <= 0.4; i++)
            {
                double t = i * (1.0 / 72);
                Scripted.PoseHand(input.Heads[0], keys(t), hand);
                hand.SampleTime = t; hand.NewSample = true;
                input.Step(t, null, hand, 1.0 / 72);
                if (t >= 0.15 && t <= 0.3)
                {
                    var w = Ic.palmLift.trace[j++];
                    trace.Append($" {t * 1000:F1}:{input.PalmLift[1] * 1000:F2}");
                    Assert.That(input.PalmLift[1] * 1000, Is.EqualTo(w.liftMm).Within(0.01), $"lift at {w.ms} ms");
                }
            }
            Debug.Log(trace.ToString());
        }

        // ---------------------------------------------------------------- the sweep (critique H3), end to end

        [Test, Timeout(600000)]
        public void TheFlatHandSweepMatchesAndHasNoDeadRing()
        {
            var sw = Json<SwFile>("sweep.json");
            var combos = new (double curl, double pitch, double dir)[] { (0, 0, Math.PI), (0.15, -0.15, Math.PI), (0.15, 0.15, Math.PI), (0.3, 0, Math.PI), (0.1, 0, Math.PI * 2 / 3), (0.1, 0, Math.PI / 2) };
            int row = 0, fails = 0; double worst = 99; string worstAt = "";
            var tally = new Dictionary<string, int>();
            for (int drum = 0; drum < 2; drum++)
                foreach (var (curl, pitch, dir) in combos)
                    for (double rho = 0; rho <= 1.351; rho += 0.05)
                    {
                        var w = sw.rows[row++];
                        var input = new HandDrumsInput();
                        var keys = Scripted.Motion(Scripted.StrokeKeys(0.1, HP.Of(rho, 0, dir, curl, pitch), 1.5, 0.1, 0.03));
                        var hand = new HdHandSample();
                        for (int i = 0; i * (1.0 / 72) <= 0.9; i++)
                        {
                            double t = i * (1.0 / 72);
                            Scripted.PoseHand(input.Heads[drum], keys(t), hand);
                            hand.SampleTime = t; hand.NewSample = true;
                            input.Step(t, null, hand, 1.0 / 72);
                        }
                        var s = Scripted.Events(input).FindAll(e => e.Op == HdOp.Stroke);
                        string at = $"sweep drum {drum} curl {curl} pitch {pitch} dir {dir * 180 / Math.PI:F0} rho {rho:F2}";
                        Assert.That(w.drum, Is.EqualTo(drum)); Assert.That(w.rho, Is.EqualTo(Math.Round(rho, 2)).Within(1e-9));
                        if (s.Count == 0) { Assert.That(w.kind, Is.EqualTo("none"), at); tally["none"] = tally.TryGetValue("none", out int n0) ? n0 + 1 : 1; continue; }
                        var st = s[0];
                        string kind = Scripted.Kind(st.Kind), parts = string.Join("+", Enumerable.Range(0, st.PatchCount).Select(p => PartNames[(int)st.Patch(p).Part]));
                        tally[kind] = tally.TryGetValue(kind, out int n) ? n + 1 : 1;
                        Assert.That(kind, Is.EqualTo(w.kind), at + " kind");
                        Assert.That(parts, Is.EqualTo(w.parts), at + " parts");
                        AssertU(st.U, st.V, w.u, st.V, at);
                        if (Math.Abs(dir - Math.PI) > 1e-9 || st.U < 0.5) continue;
                        // through the unit: the live patches vs the canonical open tone at the same u (A-weighted, 0.4 s)
                        double db = LiveVsOpenDb(drum, st);
                        Assert.That(db, Is.EqualTo(w.aReOpenDb).Within(0.15), at + " A level re the open tone (Node " + w.aReOpenDb + ")");
                        if (db < -6) fails++;
                        if (db < worst) { worst = db; worstAt = at + " " + kind; }
                    }
            Assert.That(row, Is.EqualTo(336));
            Debug.Log($"[HandDrums] sweep: 336 strokes {string.Join(", ", tally.Select(kv => kv.Key + " " + kv.Value))}; hand pointing away, u >= .5: worst {worst:F1} dB A re the open tone ({worstAt}), {fails} more than 6 dB under");
            Assert.That(fails, Is.EqualTo(0), "no dead ring: no stroke at u >= .5 more than 6 dB A under the open tone");
        }

        static double LiveVsOpenDb(int drum, HdEvent st)
        {
            float[] Live()
            {
                using var h = new HandDrumsOffline();
                HandDrumsInput.Send(h.Sound, st, HandDrumsOffline.At(96));
                return h.Render((int)(0.6 * 48000))[drum];
            }
            float[] Open()
            {
                using var h = new HandDrumsOffline();
                h.Sound.Stroke(drum, 1, HandStroke.Open, st.U, 0, HandDrumsOffline.At(96));
                return h.Render((int)(0.6 * 48000))[drum];
            }
            return ALevel(Live(), 96, 19200) - ALevel(Open(), 96, 19200);
        }

        static double ALevel(float[] x, int from, int len)
        {
            int nfft = 1; while (nfft < 4 * len) nfft <<= 1;
            var re = new double[nfft]; var im = new double[nfft];
            for (int i = 0; i < len && from + i < x.Length; i++) re[i] = x[from + i];
            for (int i = 1, j = 0; i < nfft; i++) { int bit = nfft >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; } }
            for (int L = 2; L <= nfft; L <<= 1) { double ang = -2 * Math.PI / L; int hh = L >> 1; for (int k = 0; k < hh; k++) { double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k); for (int i = k; i < nfft; i += L) { int j = i + hh; double xr = re[j] * wr - im[j] * wi, xi = re[j] * wi + im[j] * wr; re[j] = re[i] - xr; im[j] = im[i] - xi; re[i] += xr; im[i] += xi; } } }
            double e = 0;
            for (int k = 1; k <= nfft / 2; k++)
            {
                double f = k * 48000.0 / nfft, f2 = f * f, c1 = 20.598997 * 20.598997, c2 = 107.65265 * 107.65265, c3 = 737.86223 * 737.86223, c4 = 12194.217 * 12194.217;
                double a = (c4 * f2 * f2) / ((f2 + c1) * Math.Sqrt((f2 + c2) * (f2 + c3)) * (f2 + c4)) * 1.2589412;
                e += (re[k] * re[k] + im[k] * im[k]) * a * a;
            }
            return 10 * Math.Log10(e / len + 1e-30);
        }

        // ---------------------------------------------------------------- allocation

        [Test]
        public void StepAllocatesNothing()
        {
            var input = new HandDrumsInput();
            var keys = Scripted.Motion(Scripted.StrokeKeys(0.1, Scripted.OPEN, 1.2, depth: 0.006, stay: 0.3));
            var hand = new HdHandSample();
            int i = 0;
            void Frame() { double t = i++ / 72.0; Scripted.PoseHand(input.Heads[0], keys(t), hand); hand.SampleTime = t; hand.NewSample = true; input.Step(t, hand, null, 1.0 / 72); }
            for (int k = 0; k < 8; k++) Frame();
            Assert.That(() => { for (int k = 0; k < 60; k++) Frame(); }, Is.Not.AllocatingGCMemory(), "a stroke, a rest and a lift through Step");
            Assert.That(Scripted.Events(input).Count(e => e.Op == HdOp.Stroke), Is.EqualTo(1));
        }
    }
}
