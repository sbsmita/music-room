// What the hand-drum unit must do, measured on the C# unit (handdrums.md 6.2 tests 5-11 and 13, 2.11 checks S1-S4,
// H1-H3, T1, T3, G1, F1, BS, Q1-Q6): no low-frequency push, the strokes' character (open-tone dominance, the bass partial,
// the slap's crack), rests, the glide (and its s(t) trajectory, review item 12), flams, budget scaling, clicks, denormals,
// allocation, and loudness parity with the real Unity piano. Burst FloatMode.Fast (the graph's mode) throughout.
using System;
using System.Collections.Generic;
using System.Text;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Audio
{
    public unsafe class HandDrumsBehaviourTests
    {
        const int Sr = 48000, At = 96;
        static readonly double[] FT = { 261.6, 196.0 }, FB = { 78, 70 };
        static readonly string[] Nm = { "conga", "tumba" };

        // ---------------------------------------------------------------- 5. DC and low-frequency push (Q1, Q2, platform 10.3)

        [Test]
        public void NoStrokeHasALowFrequencyPushOrDc()
        {
            var log = new StringBuilder("[HandDrums] low-frequency push (u 1):");
            foreach (var k in new[] { HandStroke.Open, HandStroke.Bass, HandStroke.Slap, HandStroke.Heel, HandStroke.Toe })
                for (int d = 0; d < 2; d++)
                {
                    float[] y = HdRender.Stroke(k, d, 1.0, 2.0);
                    var p = HdAnalysis.PowerHann(y, At, 65536, 1 << 17);
                    double df = Sr / (double)(1 << 17), lo = 0, all = 0;
                    for (int i = 1; i < p.Length; i++) { all += p[i]; if (i * df < 60) lo += p[i]; }
                    double below60 = 10 * Math.Log10(lo / all);
                    double m = 0, e = 0;
                    for (int i = At; i < y.Length; i++) { m += y[i]; e += (double)y[i] * y[i]; }
                    int n = y.Length - At;
                    double dc = Math.Abs(m / n) / Math.Sqrt(e / n);
                    log.Append($" {Nm[d]} {k} {below60:F1} dB, |mean|/rms {dc:F4};");
                    Assert.That(below60, Is.LessThanOrEqualTo(-20), $"{Nm[d]} {k}: energy below 60 Hz re the hit");
                    Assert.That(dc, Is.LessThanOrEqualTo(0.01), $"{Nm[d]} {k}: |mean| / rms over the hit");
                }
            Debug.Log(log.ToString());
        }

        // ---------------------------------------------------------------- 6. stroke character (S1-S4, H1-H3)

        [Test]
        public void TheStrokesHaveTheirCharacter([Values(0, 1)] int d)
        {
            int L = (int)Math.Round(0.4 * Sr);
            float[] op = HdRender.Stroke(HandStroke.Open, d), ba = HdRender.Stroke(HandStroke.Bass, d), sl = HdRender.Stroke(HandStroke.Slap, d);
            double fT = FT[d], fB = FB[d];
            // S1: the open tone is one pitch, many dB louder than the rest
            var pkO = HdAnalysis.SpectralPeaks(op, At, L, 10, 40, 8000);
            var tone = pkO.Find(q => Math.Abs(q.F / fT - 1) < 0.04);
            var other = pkO.Find(q => q.F != tone.F);
            double s1 = tone.Db - other.Db;
            // S2: the bass is the low partial, its tone partial a little under it
            var top = HdAnalysis.PeakIn(ba, At, L, 40, 8000);
            var bB = HdAnalysis.PeakIn(ba, At, L, 0.8 * fB, 1.2 * fB);
            var hB = HdAnalysis.PeakIn(ba, At, L, 180, 360);
            double s2 = hB.Db - bB.Db;
            // S3: the slap shares the open tone's fundamental
            double s3 = HdAnalysis.PeakIn(sl, At, L, 0.96 * fT, 1.04 * fT).Db - HdAnalysis.PeakIn(op, At, L, 0.96 * fT, 1.04 * fT).Db;
            // S4: the slap's 1-8 kHz energy re the open tone (first 50 ms) and its centroid ratio
            double s4 = HdAnalysis.BandRect(sl, At, 2400, 1000, 8000) - HdAnalysis.BandRect(op, At, 2400, 1000, 8000);
            double c50 = HdAnalysis.CentroidRect(sl, At, 2400) / HdAnalysis.CentroidRect(op, At, 2400);
            // H1: the crack above 4 kHz; H2: noise-like, not ringing lines
            double h1 = HdAnalysis.RectShare(sl, At, 2400, 4000);
            var p2m = HdAnalysis.PeakToMedian(sl, At + 240, (int)Math.Round(0.055 * Sr), 2000, 7000);
            double p2mMean = 0, p2mMax = 0; foreach (var v in p2m) { p2mMean += v / p2m.Count; p2mMax = Math.Max(p2mMax, v); }
            // H3: the air pop is a small smack at the start, not the slap's peak
            var h3 = new double[2];
            for (int i = 0; i < 2; i++)
            {
                double u = i == 0 ? 0.8 : 1.0;
                float[] s = HdRender.Stroke(HandStroke.Slap, d, u), pop = HdRender.Stroke(HandStroke.Slap, d, u, setup: h => HdRender.Solo(h, HandDrumsConst.KindCav));
                h3[i] = 20 * Math.Log10(HdAnalysis.PeakAbs(pop) / HdAnalysis.PeakAbs(s));
            }
            Debug.Log($"[HandDrums] {Nm[d]} strokes: S1 {s1:F1} dB; S2 top {top.F:F1} Hz, tone partial {s2:F1} dB ({hB.F:F0} Hz); S3 {s3:F1} dB; S4 {s4:F1} dB, c50 x{c50:F2}; " +
                      $"H1 {h1:F1} dB; H2 mean {p2mMean:F1} max {p2mMax:F1} dB; H3 {h3[0]:F1} / {h3[1]:F1} dB");
            Assert.That(s1, Is.GreaterThanOrEqualTo(10), "S1: open tone dominance");
            Assert.That(top.F, Is.LessThan(100), "S2: the bass's strongest partial");
            Assert.That(s2, Is.LessThanOrEqualTo(0).And.GreaterThan(-8), "S2: the tone partial 0..-8 dB under the bass");
            Assert.That(Math.Abs(s3), Is.LessThanOrEqualTo(3), "S3: slap fundamental within 3 dB of the open tone's");
            Assert.That(s4, Is.GreaterThanOrEqualTo(8), "S4: slap 1-8 kHz >= 8 dB over the open tone");
            Assert.That(c50, Is.GreaterThanOrEqualTo(1.6), "S4: slap centroid >= 1.6x");
            Assert.That(h1, Is.GreaterThanOrEqualTo(-22), "H1: > 4 kHz share of the slap's first 50 ms");
            Assert.That(p2mMean, Is.LessThanOrEqualTo(10.5), "H2: crack peak-to-median band mean");
            Assert.That(p2mMax, Is.LessThanOrEqualTo(14), "H2: crack peak-to-median band max");
            Assert.That(h3[0], Is.LessThanOrEqualTo(-6), "H3: pop share of the peak, u .8");
            Assert.That(h3[1], Is.LessThanOrEqualTo(-6), "H3: pop share of the peak, u 1");
        }

        // ---------------------------------------------------------------- 7. rests (T1, T3) and a lift while ringing

        static double Band(float[] y, double f, double t0, double t1)
        {
            int fr = (int)Math.Round(t0 * Sr), to = (int)Math.Round(t1 * Sr);
            return HdAnalysis.BandRect(y, fr, to - fr, f * 0.93, f * 1.07);
        }

        [Test]
        public void RestsMuteAndALiftNeverClicks()
        {
            float[] op = HdRender.Stroke(HandStroke.Open), mu = HdRender.Stroke(HandStroke.Mute);
            double dOpen = Band(op, 261.6, 0.01, 0.04) - Band(op, 261.6, 0.15, 0.18), dMute = Band(mu, 261.6, 0.01, 0.04) - Band(mu, 261.6, 0.15, 0.18);
            Assert.That(dMute, Is.GreaterThan(dOpen + 15), "T1: a closed tone stops (drop 10-40 ms -> 150-180 ms)");
            var palm = new RestPoint { Part = HandPart.Palm, Rho = 0.2f, Theta = (float)Math.PI };
            float[] t3;
            using (var h = new HandDrumsOffline())
            {
                h.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, HandDrumsOffline.At(At));
                h.Sound.Rest(0, 0, palm, default, default, 1, HandDrumsOffline.At(At + (long)Math.Round(0.3 * Sr)));
                t3 = h.Render((int)(0.8 * Sr))[0];
            }
            double drop = Band(t3, 261.6, 0.25, 0.29) - Band(t3, 261.6, 0.40, 0.44);
            Assert.That(drop, Is.GreaterThan(30), "T3: a palm laid on a ringing drum");
            // a lift while it rings: no step larger than the signal's own (handdrums.md 6.2 test 7)
            float[] y;
            long restAt = (long)Math.Round(0.20 * Sr), liftAt = (long)Math.Round(0.23 * Sr);
            using (var h = new HandDrumsOffline())
            {
                h.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, HandDrumsOffline.At(At));
                h.Sound.Rest(0, 0, palm, default, default, 1, HandDrumsOffline.At(restAt));
                h.Sound.Lift(0, 0, HandDrumsOffline.At(liftAt));
                y = h.Render((int)(0.5 * Sr))[0];
            }
            float before = NullTest.MaxStep(y, (int)liftAt - 960, (int)liftAt), after = NullTest.MaxStep(y, (int)liftAt, (int)liftAt + 960);
            Debug.Log($"[HandDrums] rests: T1 open drop {dOpen:F1} dB, closed {dMute:F1} dB; T3 palm {drop:F1} dB in 100 ms; lift while ringing: max step {after:E2} after vs {before:E2} before");
            Assert.That(after, Is.LessThanOrEqualTo(before * 1.01f), "the lift adds no step (state continuous)");
        }

        [Test]
        public void ABusyPassageHasNoClicksNoNanAndNoDenormals()
        {
            var kinds = new[] { HandStroke.Open, HandStroke.Slap, HandStroke.Bass, HandStroke.Mute, HandStroke.Heel, HandStroke.Toe, HandStroke.Open, HandStroke.SlapClosed };
            var onsets = new List<long>();
            float[][] o;
            using (var h = new HandDrumsOffline())
            {
                for (int i = 0; i < 48; i++)
                {
                    long at = 960 + (long)Math.Floor(i * 0.11 * Sr + 0.5);
                    onsets.Add(at); onsets.Add(at + (long)Math.Round(0.004 * Sr));
                    h.Sound.Stroke(i % 3 == 2 ? 1 : 0, i % 2, kinds[i % kinds.Length], 0.35 + 0.6 * ((i * 7) % 10) / 10.0, 0, HandDrumsOffline.At(at));
                }
                h.Sound.Rest(0, 0, new RestPoint { Part = HandPart.Palm, Rho = 0.2f, Theta = (float)Math.PI }, default, default, 1, HandDrumsOffline.At((long)Math.Round(6.0 * Sr)));
                h.Sound.Lift(0, 0, HandDrumsOffline.At((long)Math.Round(6.3 * Sr)));
                o = h.Render(7 * Sr);
            }
            var y = HdAnalysis.Sum(o[0], o[1]);
            int bad = 0; foreach (var v in y) if (float.IsNaN(v) || float.IsInfinity(v)) bad++;
            double cs = HdAnalysis.ClickScore(y, onsets, 0.012);
            Assert.That(bad, Is.EqualTo(0), "Q3: finite samples");
            Assert.That(cs, Is.LessThan(6), "Q4: click score outside the onsets (white noise ~ 8)");
            // Q6: 12 s after a stroke every state is 0 or normal, and nothing is active (idle: no CPU)
            using (var h = new HandDrumsOffline())
            {
                h.Sound.Stroke(0, 1, HandStroke.Open, 1.0, 0, HandDrumsOffline.At(At));
                h.Render(12 * Sr);
                int sub = 0, act = 0;
                for (int d = 0; d < 2; d++)
                {
                    HandDrumsDrum* dr = d == 0 ? &h.State->D0 : &h.State->D1;
                    for (int g = 0; g < dr->G; g++)
                    {
                        float* b = (float*)(dr->Bank + g);
                        for (int l = 0; l < 8; l++) { float v = Math.Abs(b[l]); if (v > 0 && v < 1.1754944e-38f) sub++; }
                        if (dr->Act[g] != 0) act++;
                    }
                    Assert.That(dr->Idle, Is.EqualTo(1), Nm[d] + " idle");
                }
                Debug.Log($"[HandDrums] busy passage: click score {cs:F2}, non-finite {bad}; after 12 s: subnormal {sub}, active groups {act}");
                Assert.That(sub, Is.EqualTo(0), "Q6: no subnormal state");
                Assert.That(act, Is.EqualTo(0), "Q6: no active group");
            }
        }

        // ---------------------------------------------------------------- 8. glide (G1, G2, review item 12: s(t))

        [Test]
        public void TheHeadGlidesAfterAHardBassAndTheGlideStepTouchesOnlyTheGlideList()
        {
            double Cents(HandStroke k, double u)
            {
                float[] y = HdRender.Stroke(k, 0, u, 1.0, setup: h => HdRender.Solo(h, HandDrumsConst.KindT));
                double f1 = HdAnalysis.ZcFreq(y, (int)(0.015 * Sr), (int)(0.045 * Sr)), f2 = HdAnalysis.ZcFreq(y, (int)(0.5 * Sr), (int)(0.8 * Sr));
                return 1200 * Math.Log(f1 / f2, 2);
            }
            double bass1 = Cents(HandStroke.Bass, 1), bass05 = Cents(HandStroke.Bass, 0.5), open1 = Cents(HandStroke.Open, 1);
            // the s(t) trajectory itself, every 32 samples (the control grid), after a bass at u .8 and u 1, both drums
            var traj = new StringBuilder();
            foreach (int d in new[] { 0, 1 })
                foreach (double u in new[] { 0.8, 1.0 })
                {
                    using var h = new HandDrumsOffline();
                    h.Sound.Stroke(d, 1, HandStroke.Bass, u, 0, 0);
                    traj.Append($" {Nm[d]} u {u}:");
                    double peak = 0; int peakAt = 0;
                    for (int b = 0; b < 160; b++)
                    {
                        h.RenderBlock(null, null, 0, 32);
                        HandDrumsDrum* dr = d == 0 ? &h.State->D0 : &h.State->D1;
                        double c = 1200 * Math.Log(dr->S, 2), ms = (b + 1) * 32 / 48.0;
                        if (c > peak) { peak = c; peakAt = b; }
                        if (b == 7 || b == 14 || b == 29 || b == 74 || b == 149) traj.Append($" {ms:F0} ms {c:F1} c;");
                    }
                    traj.Append($" peak {peak:F1} c at {(peakAt + 1) * 32 / 48.0:F1} ms;");
                }
            Debug.Log($"[HandDrums] glide: G1 tone after a u 1 bass {bass1:F1} cents (15-45 ms re 0.5-0.8 s), u .5 {bass05:F1}, open u 1 {open1:F1}; s(t) of the head (the cap of every gliding mode):{traj}");
            Assert.That(bass1, Is.InRange(10.0, 60.0), "G1: 10..60 cents after a hard bass");
            Assert.That(bass05, Is.LessThan(bass1), "G2: grows with force");
            Assert.That(open1, Is.LessThan(bass1), "G2: an open tone glides less than a bass");
            // a glide-only control step refreshes only the glide list's poles
            using (var h = new HandDrumsOffline())
            {
                HandDrumsDrum* dr = &h.State->D0;
                int n = dr->NPad;
                var pr = new float[n]; var pi = new float[n];
                for (int k = 0; k < n; k++) { float* b = (float*)(dr->Bank + (k >> 2)); pr[k] = b[8 + (k & 3)]; pi[k] = b[12 + (k & 3)]; }
                dr->S = 1.01;
                HandDrumsDsp.UpdatePoles(dr, false);
                var inList = new HashSet<int>();
                for (int i = 0; i < dr->GlideCount; i++) inList.Add(dr->GlideList[i]);
                int changed = 0;
                for (int k = 0; k < n; k++)
                {
                    float* b = (float*)(dr->Bank + (k >> 2));
                    bool same = b[8 + (k & 3)] == pr[k] && b[12 + (k & 3)] == pi[k];
                    if (!inList.Contains(k)) Assert.IsTrue(same, "mode " + k + " (not in the glide list) unchanged");
                    else if (!same) changed++;
                }
                Assert.That(changed, Is.EqualTo(14), "the 14 gliding modes moved (the 2 cavity modes stay at their detune)");
            }
        }

        // ---------------------------------------------------------------- 9. flams (F1)

        [Test]
        public void FlamsAddOrCancelWithinTheRule()
        {
            using var h0 = new HandDrumsOffline();
            h0.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, HandDrumsOffline.At(At));
            float[] one = h0.Render((int)(0.5 * Sr))[0];
            double L1 = HdAnalysis.ALevel(one, At, 19200);
            var row = new StringBuilder("[HandDrums] F1 flams re one stroke:");
            foreach (int ms in new[] { 0, 1, 3, 10, 25 })
            {
                using var h = new HandDrumsOffline();
                h.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, HandDrumsOffline.At(At));
                h.Sound.Stroke(0, 0, HandStroke.Open, 0.8, 0.5, HandDrumsOffline.At(At + ms * 48));
                float[] two = h.Render((int)(0.5 * Sr))[0];
                double dl = HdAnalysis.ALevel(two, At, 19200) - L1;
                row.Append($" {ms} ms {dl:+0.0;-0.0} dB;");
                Assert.That(dl, Is.InRange(-6.0, 6.5), $"F1 at {ms} ms");
            }
            Debug.Log(row.ToString());
        }

        // ---------------------------------------------------------------- 10. budget scaling (BS, platform UnitShareTests)

        [Test]
        public void ShareCutsTheWorkOfASlapRollWithoutClicks()
        {
            (long work, int maxAct, int cap, int shed, float[] y, bool coreKept) Run(float share)
            {
                using var h = new HandDrumsOffline();
                h.Sound.SetShare(share);
                int i = 0;
                for (double t = 0.1; t < 3; t += 1.0 / 16, i++)
                {
                    h.Sound.Stroke(0, i % 2, HandStroke.Slap, 0.8, 0, HandDrumsOffline.At(HdRef.JsAt(t)));
                    h.Sound.Stroke(1, (i + 1) % 2, HandStroke.Slap, 0.8, 0, HandDrumsOffline.At(HdRef.JsAt(t + 0.01)));
                }
                var o = new[] { new float[3 * Sr], new float[3 * Sr] };
                int maxAct = 0; bool core = true;
                for (int s = 0; s < 3 * Sr; s += 256)
                {
                    h.RenderBlock(o[0], o[1], s, Math.Min(256, 3 * Sr - s));
                    int a = 0;
                    for (int d = 0; d < 2; d++)
                    {
                        HandDrumsDrum* dr = d == 0 ? &h.State->D0 : &h.State->D1;
                        for (int g = 0; g < dr->G; g++) { a += dr->Act[g]; if (dr->GClass[g] == 0 && dr->Allowed[g] == 0) core = false; }
                    }
                    maxAct = Math.Max(maxAct, a);
                }
                return (h.State->WorkTotal, maxAct, h.State->Cap, h.State->ShedGroups, HdAnalysis.Sum(o[0], o[1]), core);
            }
            var full = Run(1f); var low = Run(0.4f);
            var onsets = new List<long>();
            for (double t = 0.1; t < 3; t += 1.0 / 16) { foreach (var a in new[] { HdRef.JsAt(t), HdRef.JsAt(t + 0.01) }) { onsets.Add(a); onsets.Add(a + (long)Math.Round(0.004 * Sr)); } }
            double cs = HdAnalysis.ClickScore(low.y, onsets, 0.012), ratio = (double)low.work / full.work;
            Debug.Log($"[HandDrums] BS slap roll: Work {full.work} -> {low.work} at Share 0.4 ({ratio:P1}), active groups {full.maxAct} -> {low.maxAct} (cap {low.cap}), {low.shed} fades, click score {cs:F2}");
            Assert.That(ratio, Is.LessThanOrEqualTo(0.5), "Share 0.4 halves the work");
            Assert.That(low.maxAct, Is.LessThanOrEqualTo(low.cap), "active groups within the cap");
            Assert.That(low.cap, Is.EqualTo(37));
            Assert.That(cs, Is.LessThan(6), "no clicks from the fades");
            Assert.IsTrue(low.coreKept && full.coreKept, "core groups (bass, tone, low head modes, shell, cavity) are never shed");
        }

        // ---------------------------------------------------------------- 11. allocation

        [Test]
        public void TheApiAndTheRenderAllocateNothing()
        {
            using var h = new HandDrumsOffline();
            var s = h.Sound;
            var c = new float[256]; var t = new float[256];
            var p0 = new PatchPoint { Rho = 0.9f, Theta = 0f }; var p1 = new PatchPoint { Rho = 0.5f, Theta = 0f };
            var r0 = new RestPoint { Part = HandPart.Fingers, Rho = 0.7f, Theta = 0f };
            // warm: Burst compiled, first calls done
            s.Stroke(0, 1, HandStroke.Slap, 0.8, 0, 0); h.RenderBlock(c, t, 0, 256); h.RenderBlock(c, t, 0, 256);
            Assert.That(() =>
            {
                s.Patch(0, 1, HandPart.Fingers, 0.7f, PatchShape.Line, p0, p1, default, 2, 0);
                s.Patch(1, 0, HandPart.Palm, 0.6f, PatchShape.Points, p1, default, default, 1, 0, 0.004f);
                s.Stroke(1, 1, HandStroke.Bass, 0.9, 0.3, 0);
                s.Rest(0, 0, r0, default, default, 1, 0);
                s.RestCanonical(1, 0, HandRest.Heel, 0, 0);
                s.Lift(0, 0, 0);
                s.Level(0); s.SetShare(0.7f);
            }, Is.Not.AllocatingGCMemory(), "IHandDrumSound calls");
            Assert.That(() => { for (int b = 0; b < 8; b++) h.RenderBlock(c, t, 0, 256); }, Is.Not.AllocatingGCMemory(), "HandDrumsDsp.Render (Burst)");
        }

        // ---------------------------------------------------------------- 13. loudness parity with the real piano (platform 10.3)

        [Test]
        public void LoudnessParityWithThePianoMfReference()
        {
            double spb = 60.0 / 108;
            // the piano's mf reference: Ode to Joy bar 1 (E4 E4 F4 G4) at vel 0.55, keys held 90 %, dry, the level the hand drums
            // were tuned to (the upgraded piano unit that measured it was deleted on 2026-11-11: Loudness.PianoMfLufs)
            double lp = Loudness.PianoMfLufs;
            double Phrase(HandStroke[] kinds, int drum)
            {
                using var h = new HandDrumsOffline();
                for (int i = 0; i < kinds.Length; i++) h.Sound.Stroke(drum, i % 2, kinds[i], 0.55, 0, HandDrumsOffline.At(HdRef.JsAt(0.2 + i * spb)));
                var y = h.Render(4 * Sr);
                return Loudness.Lufs(HdAnalysis.Sum(y[0], y[1]));
            }
            var O = HandStroke.Open;
            double tonePhrase = Phrase(new[] { O, O, HandStroke.Slap, O }, 0), openC = Phrase(new[] { O, O, O, O }, 0), openT = Phrase(new[] { O, O, O, O }, 1);
            var b = HandStroke.Bass;
            double bassC = Phrase(new[] { b, b, b, b }, 0), bassT = Phrase(new[] { b, b, b, b }, 1);
            Debug.Log($"[HandDrums] loudness parity: piano mf {lp:F2} LUFS; tone phrase {tonePhrase:F2} ({tonePhrase - lp:+0.00;-0.00} LU); open x4 conga {openC - lp:+0.00;-0.00}, tumba {openT - lp:+0.00;-0.00} LU; bass x4 conga {bassC - lp:+0.00;-0.00}, tumba {bassT - lp:+0.00;-0.00} LU");
            Assert.That(tonePhrase - lp, Is.InRange(-1.0, 1.0), "the tone phrase within 1 LU of the piano mf reference");
            Assert.That(openC - lp, Is.InRange(-2.0, 2.0), "open x4 conga within 2 LU");
            Assert.That(openT - lp, Is.InRange(-2.0, 2.0), "open x4 tumba within 2 LU");
        }
    }
}
