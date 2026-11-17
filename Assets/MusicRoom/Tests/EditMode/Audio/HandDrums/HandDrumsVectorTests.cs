// The hand-drum unit against the Node reference (handdrums.md 6.1, 6.2 tests 1-4, 12, 18): the shipped table against
// handdrums-tables.json and the FNVs of test-vectors.json, the RNG and its draw order, the speed laws, the contact
// probes, the 3 s test-vector scenario at Share 1 and 0.4 (bit-exact FNV on the managed path and the Burst Strict entry;
// the spec's tolerances on Burst FloatMode.Fast, the graph's mode), sample by sample against the Node render, the
// channel map and determinism. All on the standalone host (HandDrumsOffline).
using System;
using System.Text;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class HandDrumsVectorTests
    {
        const int Sr = 48000;
        static HdTv s_tv;
        static HdTv Tv => s_tv ??= HdRef.Json<HdTv>("test-vectors.json");

        // ---------------------------------------------------------------- 1. tables

        [Test]
        public void TheShippedTableIsTheReferenceIdentity()
        {
            var t = HandDrumsTables.Parse(HandDrumsTables.LoadDefaultBytes());
            var json = HdRef.Json<HdTables>("handdrums-tables.json");
            Assert.That(json.version, Is.EqualTo(2));
            int[] n = { 170, 194 }, G = { 43, 49 }, gCav = { 42, 48 }, shed = { 38, 41 }, glide = { 16, 27 };
            for (int d = 0; d < 2; d++)
            {
                string k = HandDrumsNames.Drums[d] + ".";
                var tv = Tv.tables[d];
                var jd = json.drums[d];
                Assert.That(t.I1(k + "n"), Is.EqualTo(n[d]), k + "modes");
                Assert.That(t.I1(k + "G"), Is.EqualTo(G[d]), k + "groups");
                Assert.That(t.I1(k + "gCav"), Is.EqualTo(gCav[d]), k + "cavity group");
                Assert.That(t.I(k + "shedOrder").Length, Is.EqualTo(shed[d]), k + "shed order");
                Assert.That(t.I(k + "glideList").Length, Is.EqualTo(glide[d]), k + "glide list");
                Assert.That(tv.n, Is.EqualTo(n[d])); Assert.That(tv.groups, Is.EqualTo(G[d])); Assert.That(tv.gCav, Is.EqualTo(gCav[d]));
                Assert.That(t.I(k + "gClass"), Is.EqualTo(jd.gClass), k + "group classes as exported");
                Assert.That(t.I(k + "shedOrder"), Is.EqualTo(jd.shedOrder), k + "shed order as exported");
                Assert.That(t.I(k + "glideList"), Is.EqualTo(jd.glideList), k + "glide list as exported");
                // the FNVs of the float32 tables (test-vectors.json tables)
                Assert.That(HdRef.Hex(HdRef.Fnv(t.F(k + "shape"))), Is.EqualTo(tv.fnvShape), k + "shape FNV");
                Assert.That(HdRef.Hex(HdRef.Fnv(t.F(k + "go"))), Is.EqualTo(tv.fnvGo), k + "go FNV");
                Assert.That(HdRef.Hex(HdRef.Fnv(t.F(k + "avgTab"))), Is.EqualTo(tv.fnvAvg), k + "avgTab FNV");
                // tension and wave speed; the first modes (B 78 Hz, T 261.6 Hz, A2 300 Hz, (1,1) x 2 ...)
                Assert.That(t.D1(k + "T"), Is.EqualTo(tv.tensionNperM).Within(tv.tensionNperM * 1e-6));
                Assert.That(t.D1(k + "cm"), Is.EqualTo(tv.waveSpeed).Within(tv.waveSpeed * 1e-6));
                double[] f = t.D(k + "fHz"), M = t.D(k + "Mk"), t60 = t.D(k + "t60");
                int[] kind = t.I(k + "kind");
                for (int m = 0; m < tv.firstModes.Length; m++)
                {
                    var fm = tv.firstModes[m];
                    Assert.That(kind[m], Is.EqualTo(fm.kind), $"{k}mode {m} kind");
                    Assert.That(f[m], Is.EqualTo(fm.f).Within(fm.f * 1e-7), $"{k}mode {m} f");
                    Assert.That(M[m], Is.EqualTo(fm.M).Within(fm.M * 1e-5), $"{k}mode {m} M");
                    Assert.That(t60[m], Is.EqualTo(fm.t60).Within(fm.t60 * 1e-5), $"{k}mode {m} t60");
                }
                // every mode against the JSON (9 significant digits there)
                double[] r0 = t.D(k + "r0"), w0 = t.D(k + "w0"), kap = t.D(k + "kap");
                Assert.That(jd.modes.Length, Is.EqualTo(n[d]));
                for (int m = 0; m < n[d]; m++)
                {
                    var jm = jd.modes[m];
                    Assert.That(f[m], Is.EqualTo(jm.f).Within(Math.Abs(jm.f) * 1e-8 + 1e-12), $"{k}f {m}");
                    Assert.That(r0[m], Is.EqualTo(jm.r0).Within(1e-8), $"{k}r0 {m}");
                    Assert.That(w0[m], Is.EqualTo(jm.w0).Within(Math.Abs(jm.w0) * 1e-8 + 1e-15), $"{k}w0 {m}");
                }
                // each avgTab entry is the Hertz-like pressure average 3 (sin x - x cos x) / x^3 at x = kappa_k b (handdrums.md 2.3)
                float[] avg = t.F(k + "avgTab");
                int nPad = t.I1(k + "nPad");
                double worst = 0;
                for (int p = 0; p < 6; p++)
                    for (int m = 0; m < n[d]; m++)
                    {
                        int kd = kind[m];
                        double want = kd == HandDrumsConst.KindShell || kd == HandDrumsConst.KindCav || kd < 0 ? 1 : HertzAvg(kap[m] * HandDrumsConst.PartB[p]);
                        worst = Math.Max(worst, Math.Abs(avg[p * nPad + m] - want));
                    }
                Assert.That(worst, Is.LessThan(1e-6), k + "avgTab = 3 (sin x - x cos x) / x^3");
                // each dense mode's radiation carries sqrt(nReal) (Weyl count of the real head modes in its slot)
                double[] radW = t.D(k + "radW"), nReal = t.D(k + "nReal"), fh = t.D(k + "fHz");
                double a = t.D1(k + "a");
                int dense = 0;
                for (int m = 0; m < n[d]; m++)
                {
                    if (kind[m] != HandDrumsConst.KindDense) continue;
                    dense++;
                    double ka = (2 * Math.PI * fh[m] / 343) * a, want = 0.5 * Math.Min(1, ka / 3) * Math.Sqrt(nReal[m]);
                    Assert.That(radW[m], Is.EqualTo(want).Within(1e-6), $"{k}dense mode {m} sqrt(nReal)");
                    Assert.That(nReal[m], Is.EqualTo(jd.modes[m].nReal).Within(Math.Abs(jd.modes[m].nReal) * 1e-8 + 1e-9));
                }
                Assert.That(dense, Is.EqualTo(120), k + "120 dense slots");
            }
            // the M1 ratios: head modes re the head's own (0,1) vs the ideal membrane (R92), within 2 %
            double[] ideal = { 1.594, 2.136, 2.296, 2.653, 2.917, 3.156, 3.501, 3.600 };
            int[,] mn = { { 1, 1 }, { 2, 1 }, { 0, 2 }, { 3, 1 }, { 1, 2 }, { 4, 1 }, { 2, 2 }, { 0, 3 } };
            double err = 0;
            for (int d = 0; d < 2; d++)
            {
                string k = HandDrumsNames.Drums[d] + ".";
                double own = t.D1(k + "f01m");
                int[] kind = t.I(k + "kind"), mm = t.I(k + "mm"), nn = t.I(k + "modeN"), orient = t.I(k + "orient");
                double[] f = t.D(k + "fHz");
                for (int r = 0; r < ideal.Length; r++)
                    for (int m = 0; m < t.I1(k + "n"); m++)
                        if (kind[m] == HandDrumsConst.KindMem && mm[m] == mn[r, 0] && nn[m] == mn[r, 1] && orient[m] == 0)
                        {
                            // the split orientation is the measured frequency x (1 -/+ split); the reference's ratio uses the listed f
                            double fl = json.drums[d].modes[m].f;
                            err = Math.Max(err, Math.Abs(fl / own / ideal[r] - 1));
                            break;
                        }
            }
            Assert.That(err, Is.LessThan(0.02), "M1: head modes within 2 % of the ideal membrane ratios");
            // the constants the unit hard-codes are the reference's
            Assert.That(t.D1("k.OUTPUT_TRIM_DB"), Is.EqualTo(HandDrumsConst.OutputTrimDb));
            Assert.That(t.D1("k.POP_K"), Is.EqualTo(HandDrumsConst.PopK));
            Assert.That(t.D1("k.Y2_REL"), Is.EqualTo(HandDrumsConst.Y2Rel));
            Assert.That(t.D1("k.GLIDE_EPS"), Is.EqualTo(HandDrumsConst.GlideEps));
            Assert.That(t.D1("k.CULL2"), Is.EqualTo((double)HandDrumsConst.Cull2));
            Assert.That(t.D1("k.FLIGHT_S"), Is.EqualTo(HandDrumsConst.FlightS));
            Assert.That(t.D1("k.CONTACT_MAX_S"), Is.EqualTo(HandDrumsConst.ContactMaxS));
            Assert.That(t.D1("k.BASS_PADS_U"), Is.EqualTo(HandDrumsConst.BassPadsU));
            Assert.That(t.I1("k.POP_MIN"), Is.EqualTo(HandDrumsConst.PopMin)); Assert.That(t.I1("k.POP_MAX"), Is.EqualTo(HandDrumsConst.PopMax));
            Assert.That(t.I1("k.CAP_MIN"), Is.EqualTo(HandDrumsConst.CapMin)); Assert.That(t.D1("k.CORE_HZ"), Is.EqualTo(HandDrumsConst.CoreHz));
            Assert.That(t.D1("k.LOUDER_HYST"), Is.EqualTo(HandDrumsConst.LouderHyst)); Assert.That(t.D1("k.FADE_DB"), Is.EqualTo(HandDrumsConst.FadeDb));
            Assert.That(t.D("k.U_V0"), Is.EqualTo(new[] { HandDrumsConst.UV0Palm, HandDrumsConst.UV0Fingers }));
            Assert.That(t.D1("k.U_V1"), Is.EqualTo(HandDrumsConst.UV1)); Assert.That(t.D1("k.U_EXP"), Is.EqualTo(HandDrumsConst.UExp));
            for (int p = 0; p < 6; p++)
            {
                double[] v = t.D("part." + HandDrumsNames.Parts[p]);
                double[] want = { HandDrumsConst.PartM[p], HandDrumsConst.PartK[p], HandDrumsConst.PartAlpha[p], HandDrumsConst.PartLam[p], HandDrumsConst.PartB[p],
                    HandDrumsConst.PartR[p], HandDrumsConst.PartCup[p], HandDrumsConst.PartVmin[p], HandDrumsConst.PartVmax[p], HandDrumsConst.PartCap[p] };
                Assert.That(v, Is.EqualTo(want), "PARTS." + HandDrumsNames.Parts[p]);
            }
            Debug.Log($"[HandDrums] table: {t.Count} records, {HandDrumsTables.LoadDefaultBytes().Length} bytes; state {HandDrumsBuilder.Bytes(t) / 1024} KiB; M1 worst {err:P2}");
        }

        static double HertzAvg(double x) => x < 0.05 ? 1 - (x * x) / 10 + (x * x * x * x) / 280 : (3 * (Math.Sin(x) - x * Math.Cos(x))) / (x * x * x);

        // ---------------------------------------------------------------- 2. RNG, laws

        [Test]
        public void RngDrawOrderAndLawsMatchTheReference()
        {
            // mulberry32 (the reference's Rng): seed 1
            uint a = 1;
            double[] rng = { 0.6270739405881613, 0.002735721180215478, 0.5274470399599522, 0.9810509674716741 };
            foreach (double v in rng) Assert.That(HandDrumsDsp.RngNext(&a), Is.EqualTo(v), "mulberry32 seed 1");
            Assert.That(HandDrumsBuilder.DrumSeed(1, 0), Is.EqualTo(1013904234u), "conga seed of HandDrums(48000, { seed: 1 })");
            Assert.That(HandDrumsBuilder.DrumSeed(1, 1), Is.EqualTo((uint)((1 * 2654435761.0 + 2654435769.0 * 2) % 4294967296.0)));
            // partV and handSpeed (laws in test-vectors.json, 7 significant digits)
            double[] us = { 0, 0.25, 0.5, 0.75, 1 };
            for (int p = 0; p < 6; p++)
                for (int i = 0; i < us.Length; i++)
                    Assert.That(HandDrumsDsp.PartV(p, us[i]), Is.EqualTo(Tv.laws.partV[p][i]).Within(Math.Abs(Tv.laws.partV[p][i]) * 1e-6), $"partV {HandDrumsNames.Parts[p]} u {us[i]}");
            double[] uh = { 0, 0.5, 1 };
            for (int i = 0; i < 3; i++)
            {
                Assert.That(HandDrumsDsp.HandSpeed(uh[i], true), Is.EqualTo(Tv.laws.handSpeed.palm[i]).Within(1e-6));
                Assert.That(HandDrumsDsp.HandSpeed(uh[i], false), Is.EqualTo(Tv.laws.handSpeed.fingers[i]).Within(1e-6));
            }
            Assert.That(HandDrumsNames.ChannelOfDrum, Is.EqualTo(Tv.laws.channelOfDrum), "drum d -> channel 1 - d");
            Assert.That(HandDrumsNames.Sources, Is.EqualTo(Tv.laws.sources), "Register order: HandDrumLo, HandDrumHi");
            // the canonical open tone's first per-mode gains and the conga RNG after it (the draw order of 2.9)
            using var h = new HandDrumsOffline();
            h.Entry = HandDrumsEntry.Managed;
            h.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, 0);
            h.Render(32);
            for (int k = 0; k < Tv.openToneGi12.Length; k++)
                Assert.That(h.State->D0.P0.Gi[k], Is.EqualTo((float)Tv.openToneGi12[k]).Within(Math.Abs(Tv.openToneGi12[k]) * 1e-7), "open-tone gi " + k);
            Assert.That(h.State->D0.Rng, Is.EqualTo((uint)Tv.rngAfterOpen), "conga RNG after the open tone");
        }

        // ---------------------------------------------------------------- 3. contact probes

        [Test]
        public void ContactProbesMatchTheReference([Values(HandDrumsEntry.Managed, HandDrumsEntry.BurstFast)] HandDrumsEntry entry)
        {
            var log = new StringBuilder($"[HandDrums] contact probes ({entry}, burst {BurstCompiler.IsEnabled}):");
            for (int p = 0; p < 6; p++)
            {
                var want = Tv.contact[p];
                using var h = new HandDrumsOffline();
                h.Entry = entry;
                HandDrumsDsp.ApplyPatchNow(h.State, 0, 1, (HandPart)p, 0.8, 0.5, 0);
                var dF = new float[1024];
                for (int s = 0; s < 1024; s += 32)
                {
                    h.RenderBlock(null, null, 0, 32);
                    for (int i = 0; i < 32; i++) dF[s + i] = h.State->D0.P0.DF[i];
                }
                HandDrumsPatch* S = &h.State->D0.P0;
                HandDrumsDsp.HeadReaction(&h.State->D0, 0.5, HandDrumsConst.PartB[p], out double kh, out double zh);
                Assert.That(HandDrumsDsp.PartV(p, 0.8), Is.EqualTo(want.V).Within(want.V * 1e-6), HandDrumsNames.Parts[p] + " V");
                Assert.That(kh, Is.EqualTo(want.Kh).Within(want.Kh * 1e-6), HandDrumsNames.Parts[p] + " Kh");
                Assert.That(zh, Is.EqualTo(want.Zh).Within(want.Zh * 1e-6), HandDrumsNames.Parts[p] + " Zh");
                Assert.That(S->Fmax, Is.EqualTo(want.FmaxN).Within(want.FmaxN * 1e-3), HandDrumsNames.Parts[p] + " F max +-0.1 %");
                int samples = S->TLast - S->TFirst + 1;
                bool exact = entry != HandDrumsEntry.BurstFast || !BurstCompiler.IsEnabled;
                if (exact) Assert.That(samples, Is.EqualTo(want.contactSamples), HandDrumsNames.Parts[p] + " contact samples");
                else Assert.That(samples, Is.EqualTo(want.contactSamples).Within(2), HandDrumsNames.Parts[p] + " contact samples +-2");
                for (int i = 0; i < 8; i++) Assert.That(dF[i], Is.EqualTo((float)want.dF_first8[i]).Within(Math.Abs(want.dF_first8[i]) * 1e-6 + 1e-9), HandDrumsNames.Parts[p] + " dF " + i);
                string fnv = HdRef.Hex(HdRef.Fnv(dF));
                log.Append($" {HandDrumsNames.Parts[p]} F {S->Fmax:F4} N, {samples} samples, dF FNV {fnv} ({(fnv == want.fnvDF ? "bit-exact" : "differs")});");
                if (exact) Assert.That(fnv, Is.EqualTo(want.fnvDF), HandDrumsNames.Parts[p] + " dF FNV (double contact solve, IEEE)");
            }
            Debug.Log(log.ToString());
        }

        // ---------------------------------------------------------------- 4. the scenario vectors (Share 1 and 0.4)

        [Test]
        public void TheScenarioIsBitExactThroughTheStrictEntry([Values(HandDrumsEntry.Managed, HandDrumsEntry.BurstStrict)] HandDrumsEntry entry)
        {
            var y = HdRender.Scenario(entry);
            var log = new StringBuilder($"[HandDrums] scenario FNV ({entry}, burst {BurstCompiler.IsEnabled}):");
            bool all = true;
            for (int d = 0; d < 2; d++)
            {
                string fnv = HdRef.Hex(HdRef.Fnv(y[d])), want = Tv.drums[d].fnv1a;
                all &= fnv == want;
                float[] r = HdRef.ReadF32(HdRef.File($"vec-scenario-{HandDrumsNames.Drums[d]}.f32"), 0, 3 * Sr);
                int diff = 0, first = -1; double mx = 0;
                for (int i = 0; i < r.Length; i++) if (r[i] != y[d][i]) { diff++; if (first < 0) first = i; mx = Math.Max(mx, Math.Abs(r[i] - y[d][i])); }
                log.Append($" {HandDrumsNames.Drums[d]} {fnv} (want {want}), {diff} samples differ (first {first}, max {mx:E2});");
            }
            Debug.Log(log.ToString());
            // Burst FloatMode.Strict is the reference's arithmetic exactly (IEEE float32 mads in the canonical order, the
            // double contact solve): bit-exact. The editor's managed (Mono) path evaluates float expressions in double before
            // rounding, so it is held to the spec's tolerances instead (measured: 1 ulp-level differences, -140 dB).
            if (entry == HandDrumsEntry.BurstStrict && BurstCompiler.IsEnabled) Assert.IsTrue(all, "bit-exact FNV through the Burst Strict entry (see the log line)");
            else if (!all) AssertTolerances(y, entry.ToString());
        }

        [Test]
        public void TheScenarioMeetsTheSpecTolerancesUnderBurstFast()
        {
            var y = HdRender.Scenario(HandDrumsEntry.BurstFast);
            AssertTolerances(y, "Fast");
        }

        static void AssertTolerances(float[][] y, string what)
        {
            double[] W = { 0, 0.05, 0.1, 0.25, 0.5, 1, 1.5, 2, 2.5, 3 };
            var log = new StringBuilder($"[HandDrums] scenario under Burst {what}:");
            for (int d = 0; d < 2; d++)
            {
                var v = Tv.drums[d];
                float[] x = y[d];
                int first = Array.FindIndex(x, s => s != 0f);
                Assert.That(first, Is.EqualTo(v.firstNonZero), HandDrumsNames.Drums[d] + " first non-zero sample");
                double fe = 0;
                for (int i = 0; i < 8; i++) fe = Math.Max(fe, Math.Abs(x[first + i] - v.first8[i]));
                Assert.That(fe, Is.LessThan(1e-4), HandDrumsNames.Drums[d] + " first 8 samples within 1e-4");
                double pd = HdAnalysis.Db(HdAnalysis.PeakAbs(x)) - HdAnalysis.Db(v.peak);
                Assert.That(Math.Abs(pd), Is.LessThan(0.3), HandDrumsNames.Drums[d] + " peak within 0.3 dB");
                double worst = 0;
                for (int w = 0; w + 1 < W.Length; w++)
                {
                    double e = v.rmsWindows.rms[w];
                    int a = (int)Math.Round(W[w] * Sr), b = (int)Math.Round(W[w + 1] * Sr);
                    double r = HdAnalysis.Rms(x, a, b);
                    if (e < v.peak * 1e-4) { Assert.That(r, Is.LessThan(v.peak * 2e-4), $"{HandDrumsNames.Drums[d]} window {w}: quiet"); continue; }
                    double dd = HdAnalysis.Db(r) - HdAnalysis.Db(e);
                    worst = Math.Max(worst, Math.Abs(dd));
                    Assert.That(Math.Abs(dd), Is.LessThan(0.3), $"{HandDrumsNames.Drums[d]} window {W[w]}-{W[w + 1]} s rms within 0.3 dB");
                }
                float[] r3 = HdRef.ReadF32(HdRef.File($"vec-scenario-{HandDrumsNames.Drums[d]}.f32"), 0, 3 * Sr);
                double res = MusicRoom.Tests.Audio.NullTest.ResidualDb(x, r3);
                log.Append($" {HandDrumsNames.Drums[d]} first8 {fe:E1}, peak {pd:F4} dB, rms windows {worst:F4} dB, null residual {res:F1} dB;");
                Assert.That(res, Is.LessThan(-60), HandDrumsNames.Drums[d] + " null residual over 3 s");
            }
            Debug.Log(log.ToString());
        }

        [Test]
        public void BudgetScalingAtShare04MatchesTheReference([Values(HandDrumsEntry.BurstStrict, HandDrumsEntry.BurstFast)] HandDrumsEntry entry)
        {
            int cap = 0, shed = 0; long work = 0, work1 = 0;
            var y = HdRender.Scenario(entry, 0.4f, h => { cap = h.State->Cap; shed = h.State->ShedGroups; work = h.State->WorkTotal; });
            HdRender.Scenario(entry, 1f, h => work1 = h.State->WorkTotal);
            var want = Tv.scenarioShare04;
            string f0 = HdRef.Hex(HdRef.Fnv(y[0])), f1 = HdRef.Hex(HdRef.Fnv(y[1]));
            Debug.Log($"[HandDrums] Share 0.4 ({entry}): cap {cap}, shed {shed}, work {work} vs {work1} ({(double)work / work1:P1}; Node {want.work} vs {Tv.scenarioShare1Work}), FNV {f0} {f1} (want {want.fnv1a.conga} {want.fnv1a.tumba})");
            Assert.That(cap, Is.EqualTo(want.cap), "cap 37 groups");
            Assert.That(shed, Is.EqualTo(want.shedGroups), "9 group fades");
            bool exact = entry == HandDrumsEntry.BurstStrict && BurstCompiler.IsEnabled;
            if (exact)
            {
                Assert.That(work, Is.EqualTo(want.work), "Work at Share 0.4 (group sub-blocks)");
                Assert.That(work1, Is.EqualTo(Tv.scenarioShare1Work), "Work at Share 1");
                Assert.That(f0, Is.EqualTo(want.fnv1a.conga)); Assert.That(f1, Is.EqualTo(want.fnv1a.tumba));
            }
            else
            {
                // a 1e-7 difference moves a group's -110 dB cull by a control step now and then: Work within 0.2 %
                Assert.That(work, Is.EqualTo(want.work).Within(want.work * 0.002), "Work at Share 0.4 within 0.2 %");
                Assert.That(work1, Is.EqualTo(Tv.scenarioShare1Work).Within(Tv.scenarioShare1Work * 0.002), "Work at Share 1 within 0.2 %");
                Assert.That(Math.Abs(HdAnalysis.Db(HdAnalysis.PeakAbs(y[0])) - HdAnalysis.Db(want.peak.conga)), Is.LessThan(0.3));
                Assert.That(Math.Abs(HdAnalysis.Db(HdAnalysis.PeakAbs(y[1])) - HdAnalysis.Db(want.peak.tumba)), Is.LessThan(0.3));
            }
        }

        // ---------------------------------------------------------------- 12, 18. determinism, channel map

        [Test]
        public void TwoRendersAreBitIdentical()
        {
            var a = HdRender.Scenario(HandDrumsEntry.BurstFast);
            var b = HdRender.Scenario(HandDrumsEntry.BurstFast);
            for (int d = 0; d < 2; d++) for (int i = 0; i < a[d].Length; i++) if (a[d][i] != b[d][i]) Assert.Fail($"drum {d} differs at {i}");
        }

        [Test]
        public void DrumZeroPlaysOnChannelOneAndDrumOneOnChannelZero()
        {
            using var h = new HandDrumsOffline();
            h.Sound.Stroke(0, 1, HandStroke.Open, 0.8, 0, 0);
            h.RenderBlock(null, null, 0, 256);
            Assert.That(DspPeak(h.Slot->Channel(1)), Is.GreaterThan(1e-4f), "the conga (drum 0) sounds on channel 1 (HandDrumHi)");
            Assert.That(DspPeak(h.Slot->Channel(0)), Is.EqualTo(0f), "nothing on channel 0");
            using var h2 = new HandDrumsOffline();
            h2.Sound.Stroke(1, 1, HandStroke.Open, 0.8, 0, 0);
            h2.RenderBlock(null, null, 0, 256);
            Assert.That(DspPeak(h2.Slot->Channel(0)), Is.GreaterThan(1e-4f), "the tumba (drum 1) sounds on channel 0 (HandDrumLo)");
            Assert.That(DspPeak(h2.Slot->Channel(1)), Is.EqualTo(0f));
        }

        static float DspPeak(float* p) { float m = 0; for (int i = 0; i < 256; i++) m = Math.Max(m, Math.Abs(p[i])); return m; }
    }
}
