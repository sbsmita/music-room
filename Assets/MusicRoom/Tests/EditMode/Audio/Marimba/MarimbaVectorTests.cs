// The marimba sound unit against the lab's own vectors (spec 9.1; the marimba lab's test-vectors.json, copied to
// Reference~/). Following review item 16:
//   - the audio thread (MarimbaDsp) is checked BIT-EXACT per block (FNV-1a of Lo / Mid / Hi) on the lab's own strike and
//     damp records (marimba-tv-records.json: float32 bit patterns), through the Burst entry with FloatMode.Default; the
//     engine's FloatMode.Fast entry within 1e-5 absolute per sample of that;
//   - the main thread (MarimbaContact, double) is checked per sample against every record the lab made (1e-6 of the
//     record's peak), and the number of records that also match to the last bit is reported;
//   - the whole chain (C# contact solve + Burst render) against the vectors within 1e-5.
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;
using static MusicRoom.Tests.Audio.MarimbaRef;

namespace MusicRoom.Tests.Audio
{
    public unsafe class MarimbaVectorTests
    {
        static Dictionary<string, object> TV => Obj(Load("test-vectors.json"));
        static Dictionary<string, object> Records => Obj(Load("marimba-tv-records.json"));

        // ------------------------------------------------------------------ TV1: the shipped tables are the lab's design

        [Test]
        public void TV1_TablesAreTheLabDesign()
        {
            var tv1 = Obj(TV, "TV1_design");
            foreach (var (name, i) in new[] { ("C3", 0), ("C4", 12), ("C6", 36) })
            {
                var r = Obj(tv1[name]);
                void Rel(double got, string key, double tol = 6e-9) => Assert.That(got, Is.EqualTo(Num(r, key)).Within(Math.Abs(Num(r, key)) * tol + 1e-12), $"{name} {key}");
                Rel(MarimbaTables.F1[i], "f1"); Rel(MarimbaTables.Length[i], "L"); Rel(MarimbaTables.Width[i], "b"); Rel(MarimbaTables.H0[i], "h0");
                Rel(MarimbaTables.Mass[i], "mass"); Rel(MarimbaTables.X[i], "x"); Rel(MarimbaTables.Z[i], "z"); Rel(MarimbaTables.BarGain[i], "barGain");
                Assert.That(MarimbaTables.Source[i], Is.EqualTo((int)Num(r, "source")), name + " source");
                var tube = Obj(r, "tube");
                Assert.That(MarimbaTables.TubeRadius[i], Is.EqualTo(Num(tube, "a")).Within(1e-9 * Num(tube, "a")));
                Assert.That(MarimbaTables.TubeFt[i], Is.EqualTo(Num(tube, "ft")).Within(1e-7 * Num(tube, "ft")));
                Assert.That(MarimbaTables.TubeLacoustic[i], Is.EqualTo(Num(tube, "Lac")).Within(1e-8 * Num(tube, "Lac")));
                Assert.That(MarimbaTables.TubeQ[i], Is.EqualTo(Num(tube, "Q")).Within(1e-7 * Num(tube, "Q")));
                var pk = Arr(r, "pairK");
                for (int e = 0; e < 2; e++)
                {
                    Assert.That(MarimbaTables.PairKRe[i * 2 + e], Is.EqualTo(Num(Arr(pk[e])[0])).Within(1e-8), $"{name} K{e}");
                    Assert.That(MarimbaTables.PairKIm[i * 2 + e], Is.EqualTo(Num(Arr(pk[e])[1])).Within(1e-8), $"{name} K{e}");
                }
                var lanes = Arr(r, "lanes");
                for (int l = 0; l < lanes.Count; l++)
                {
                    var ln = Obj(lanes[l]);
                    Assert.That(MarimbaTables.P0r[i * 8 + l], Is.EqualTo((float)Num(ln, "pr")), $"{name} lane {l} pr (float32 exact)");
                    Assert.That(MarimbaTables.P0i[i * 8 + l], Is.EqualTo((float)Num(ln, "pi")), $"{name} lane {l} pi (float32 exact)");
                    Assert.That(MarimbaTables.LaneLive[i * 8 + l], Is.EqualTo(Num(ln, "live") != 0), $"{name} lane {l} live");
                    Assert.That(MarimbaTables.LaneAlpha[i * 8 + l], Is.EqualTo(Num(ln, "alpha")).Within(1e-7 * Num(ln, "alpha")));
                }
            }
            // spec 9.1 spot values
            Assert.That(MarimbaTables.P0r[12 * 8], Is.EqualTo(0.99927455f).Within(1e-8f));
            Assert.That(MarimbaTables.P0i[12 * 8], Is.EqualTo(0.034232136f).Within(1e-9f));
            Assert.That(MarimbaTables.P0r[12 * 8 + 2], Is.EqualTo(0.99022371f).Within(1e-8f));
            Assert.That(MarimbaTables.BarGain[12], Is.EqualTo(0.943289987).Within(1e-9));
            var ml = Obj(TV, "TV1_mallets");
            string[] names = { "medium", "soft", "hard", "finger" };
            for (int m = 0; m < 4; m++)
            {
                var mk = Obj(ml[names[m]]);
                Assert.That(MarimbaTables.MalletK[m], Is.EqualTo(Num(mk, "K")).Within(1e-8 * Num(mk, "K")), names[m] + " K");
                Assert.That(MarimbaContact.ImpactSpeed(m, 0.6f), Is.EqualTo(Num(mk, "speedAtVel06")).Within(1e-8), names[m] + " speed at vel .6");
            }
            Assert.That(MarimbaTables.RatiosFeLo[1], Is.EqualTo(4.026).Within(0.0005));
            Assert.That(MarimbaTables.RatiosFeLo[2], Is.EqualTo(9.981).Within(0.0005));
            Assert.That(MarimbaTables.Nodes1Lo[0], Is.EqualTo(0.1883).Within(0.00005));
        }

        [Test]
        public void TV1_TablesMatchTheLabExportForEveryBar()
        {
            // the lab's own marimba-tables.json (10 significant digits; float32 poles exact)
            var bars = Arr(Load("marimba-tables.json"), "bars");
            Assert.That(bars.Count, Is.EqualTo(MarimbaTables.Bars));
            for (int i = 0; i < bars.Count; i++)
            {
                var b = Obj(bars[i]);
                var g = Obj(b, "geometry");
                Assert.That(MarimbaTables.Midi[i], Is.EqualTo((int)Num(b, "midi")));
                Assert.That(MarimbaTables.Natural[i], Is.EqualTo((bool)b["natural"]));
                void Rel(double got, double exp, string what) => Assert.That(got, Is.EqualTo(exp).Within(Math.Abs(exp) * 1e-9 + 1e-12), $"bar {i} {what}");
                Rel(MarimbaTables.Length[i], Num(g, "L"), "L"); Rel(MarimbaTables.X[i], Num(g, "x"), "x"); Rel(MarimbaTables.Z[i], Num(g, "z"), "z");
                Rel(MarimbaTables.YTop[i], Num(g, "yTop"), "yTop"); Rel(MarimbaTables.CordXi[i], Num(g, "cordXi"), "cordXi");
                Rel(MarimbaTables.BarGain[i], Num(b, "barGain"), "barGain");
                Assert.That(MarimbaTables.Source[i], Is.EqualTo((int)Num(b, "source")));
                var lanes = Arr(b, "lanes");
                for (int l = 0; l < lanes.Count; l++)
                {
                    var ln = Obj(lanes[l]);
                    Assert.That(MarimbaTables.P0r[i * 8 + l], Is.EqualTo((float)Num(ln, "pr")), $"bar {i} lane {l}");
                    Assert.That(MarimbaTables.P0i[i * 8 + l], Is.EqualTo((float)Num(ln, "pi")), $"bar {i} lane {l}");
                }
                var cms = Arr(b, "contactModes");
                for (int m = 0; m < cms.Count; m++)
                {
                    var c = Obj(cms[m]);
                    Rel(MarimbaTables.CmSRe[i * 6 + m], Num(c, "sRe"), "sRe"); Rel(MarimbaTables.CmSIm[i * 6 + m], Num(c, "sIm"), "sIm");
                    Rel(MarimbaTables.CmC[i * 6 + m], Num(c, "c"), "c");
                }
            }
        }

        // ------------------------------------------------------------------ TV2: the contact solve

        [Test]
        public void TV2_StrikeRecordsMatchTheVectors()
        {
            var tv2 = Obj(TV, "TV2_strike");
            var contact = new MarimbaContact();
            var mem = new MarimbaStrikeRecord[1];
            int exact = 0;
            fixed (MarimbaStrikeRecord* rec = mem)
                foreach (var kv in tv2)
                {
                    var v = Obj(kv.Value);
                    var a = Obj(v, "args");
                    contact.Solve(rec, (int)Num(a, "bar"), (float)Num(a, "vel"), Num(a, "u"), Num(a, "c"), Mallet((string)a["mallet"]), (uint)Num(a, "seed"));
                    Assert.That(rec->Nh, Is.EqualTo((int)Num(v, "nh")), kv.Key + " nh");
                    Assert.That(rec->Len, Is.EqualTo((int)Num(v, "len")), kv.Key + " len");
                    Assert.That(contact.Last.ContactMs, Is.EqualTo(Num(v, "contactMs")).Within(1e-5), kv.Key + " contact ms");
                    Assert.That(contact.Last.Fmax, Is.EqualTo(Num(v, "Fmax")).Within(1e-6 * Num(v, "Fmax") + 1e-4), kv.Key + " Fmax");
                    Assert.That(contact.Last.ImpactSpeed, Is.EqualTo(Num(v, "speed")).Within(1e-6), kv.Key + " speed");
                    float peak = (float)Num(v, "segPeak");
                    var first = Arr(v, "segFirst");
                    for (int k = 0; k < first.Count; k++) Assert.That(rec->Seg[k], Is.EqualTo((float)Num(first[k])).Within(1e-6f * peak), $"{kv.Key} seg[{k}]");
                    double s1 = 0, s2 = 0, pk = 0;
                    for (int k = 0; k < rec->Len; k++) { s1 += rec->Seg[k]; s2 += (double)rec->Seg[k] * rec->Seg[k]; pk = Math.Max(pk, Math.Abs(rec->Seg[k])); }
                    Assert.That(s1, Is.EqualTo(Num(v, "segSum")).Within(1e-5 * Math.Max(1, Math.Abs(Num(v, "segSum")))), kv.Key + " seg sum");
                    Assert.That(s2, Is.EqualTo(Num(v, "segSumSq")).Within(1e-6 * Num(v, "segSumSq")), kv.Key + " seg sum of squares");
                    Assert.That(pk, Is.EqualTo(Num(v, "segPeak")).Within(1e-6 * pk), kv.Key + " seg peak");
                    var ir = Arr(v, "injR"); var ii = Arr(v, "injI");
                    for (int l = 0; l < 8; l++)
                    {
                        Assert.That(rec->InjR[l], Is.EqualTo((float)Num(ir[l])).Within(1e-6f * (Math.Abs((float)Num(ir[l])) + 1e-3f)), $"{kv.Key} injR[{l}]");
                        Assert.That(rec->InjI[l], Is.EqualTo((float)Num(ii[l])).Within(1e-6f * (Math.Abs((float)Num(ii[l])) + 1e-3f)), $"{kv.Key} injI[{l}]");
                    }
                    uint fnv = Fnv(rec->Seg, rec->Len);
                    if (fnv == Hex((string)v["segFnv"])) exact++;
                    Debug.Log($"[MARIMBA] TV2 {kv.Key}: nh {rec->Nh} len {rec->Len} contact {contact.Last.ContactMs:F4} ms Fmax {contact.Last.Fmax:F4} N, seg FNV 0x{fnv:x8} (lab {v["segFnv"]}{(fnv == Hex((string)v["segFnv"]) ? ", bit-exact" : "")})");
                }
            Debug.Log($"[MARIMBA] TV2 strike records bit-exact with the lab: {exact} / {tv2.Count}");
        }

        [Test]
        public void TV2_EveryLabRecordIsReproduced()
        {
            // every strike the lab made for TV4..TV8, re-solved in C# from the same arguments, compared per sample
            var contact = new MarimbaContact();
            var mem = new MarimbaStrikeRecord[1];
            int n = 0, exact = 0;
            double worst = 0;
            fixed (MarimbaStrikeRecord* rec = mem)
                foreach (var sc in Records)
                    foreach (var eo in Arr(Obj(sc.Value), "log"))
                    {
                        var e = Obj(eo);
                        if ((string)e["op"] != "strike") continue;
                        n++;
                        contact.Solve(rec, (int)Num(e, "bar"), (float)Num(e, "vel"), Num(e, "u"), Num(e, "c"), (MarimbaMallet)(int)Num(e, "mallet"), (uint)Num(e, "seed"));
                        Assert.That(rec->Nh, Is.EqualTo((int)Num(e, "nh")), sc.Key + " nh");
                        Assert.That(rec->Len, Is.EqualTo((int)Num(e, "len")), sc.Key + " len");
                        var seg = Arr(e, "seg");
                        float peak = 0;
                        for (int k = 0; k < seg.Count; k++) peak = Math.Max(peak, Math.Abs(Bits(seg[k])));
                        bool same = true;
                        for (int k = 0; k < seg.Count; k++)
                        {
                            float want = Bits(seg[k]);
                            if (rec->Seg[k] != want) same = false;
                            double d = Math.Abs(rec->Seg[k] - want) / peak;
                            worst = Math.Max(worst, d);
                            Assert.That(d, Is.LessThanOrEqualTo(1e-6), $"{sc.Key} strike {n} seg[{k}]");
                        }
                        var ir = Arr(e, "injR"); var ii = Arr(e, "injI");
                        for (int l = 0; l < 8; l++)
                        {
                            float wr = Bits(ir[l]), wi = Bits(ii[l]);
                            if (rec->InjR[l] != wr || rec->InjI[l] != wi) same = false;
                            Assert.That(rec->InjR[l], Is.EqualTo(wr).Within(1e-6f * (Math.Abs(wr) + 1e-4f)), $"{sc.Key} injR[{l}]");
                            Assert.That(rec->InjI[l], Is.EqualTo(wi).Within(1e-6f * (Math.Abs(wi) + 1e-4f)), $"{sc.Key} injI[{l}]");
                        }
                        if (same) exact++;
                    }
            Assert.That(n, Is.GreaterThan(20));
            Debug.Log($"[MARIMBA] contact solve vs the lab's {n} records: {exact} bit-exact, worst sample deviation {worst:E2} of the record's peak");
        }

        // ------------------------------------------------------------------ TV3: damp poles

        [Test]
        public void TV3_DampPoles()
        {
            var tv3 = Obj(TV, "TV3_damp");
            var mem = new MarimbaDampRecord[1];
            fixed (MarimbaDampRecord* rec = mem)
            {
                MarimbaContact.DampPoles(rec, 12, MarimbaDampKind.Hand, 0.5, 0);
                var pr = Arr(tv3, "hand_C4_u05"); var pi = Arr(tv3, "handPi");
                for (int l = 0; l < 8; l++)
                {
                    Assert.That(rec->Pr[l], Is.EqualTo((float)Num(pr[l])).Within(2e-7f), $"hand pr[{l}]");
                    Assert.That(rec->Pi[l], Is.EqualTo((float)Num(pi[l])).Within(2e-7f), $"hand pi[{l}]");
                }
                Assert.That(rec->Pr[0], Is.EqualTo(0.99859953f).Within(1e-7f), "spec 9.1 spot value");
                MarimbaContact.DampPoles(rec, 12, MarimbaDampKind.Mallet, 0.45, 0.3);
                var mp = Arr(tv3, "mallet_C4_u045_pr");
                for (int l = 0; l < 8; l++) Assert.That(rec->Pr[l], Is.EqualTo((float)Num(mp[l])).Within(2e-7f), $"mallet pr[{l}]");
            }
            // every damp record of the TV scenarios: float32 exact or within one ulp
            foreach (var sc in Records)
                foreach (var eo in Arr(Obj(sc.Value), "log"))
                {
                    var e = Obj(eo);
                    if ((string)e["op"] != "damp") continue;
                    fixed (MarimbaDampRecord* rec = mem)
                    {
                        MarimbaContact.DampPoles(rec, (int)Num(e, "bar"), (MarimbaDampKind)(int)Num(e, "kind"), Num(e, "u"), Num(e, "c"));
                        var pr = Arr(e, "pr"); var pi = Arr(e, "pi");
                        for (int l = 0; l < 8; l++)
                        {
                            Assert.That(rec->Pr[l], Is.EqualTo(Bits(pr[l])).Within(1.2e-7f), sc.Key + " damp pr");
                            Assert.That(rec->Pi[l], Is.EqualTo(Bits(pi[l])).Within(1.2e-7f), sc.Key + " damp pi");
                        }
                    }
                }
        }

        // ------------------------------------------------------------------ TV4..TV7: the audio thread, bit-exact

        /// <summary>Renders a TV scenario on the lab's own records: each logged strike / damp record is copied into its slot
        /// and the command posted at its exact sample, as the reference's unit did.</summary>
        static float[][] RenderOnLabRecords(string name, MarimbaRenderMode mode, out MarimbaOffline.Snapshot stats)
        {
            var r = Obj(Records[name]);
            var log = Arr(r, "log");
            int samples = (int)Num(r, "samples");
            using var h = new MarimbaOffline(1, (int)Num(r, "budget"), mode);
            var sink = (IMarimbaCommandSink)h;
            int k = 0;
            var y = h.Render(samples, s =>
            {
                while (k < log.Count && (long)Num(Obj(log[k]), "at") < s + MarimbaOffline.B)
                {
                    var e = Obj(log[k++]);
                    long at = (long)Num(e, "at");
                    string op = (string)e["op"];
                    if (op == "strike")
                    {
                        int slot = (int)Num(e, "slot");
                        MarimbaStrikeRecord* rec = h.State->Strikes + slot;
                        var seg = Arr(e, "seg"); var ir = Arr(e, "injR"); var ii = Arr(e, "injI");
                        for (int q = 0; q < MarimbaConst.SegMax; q++) rec->Seg[q] = q < seg.Count ? Bits(seg[q]) : 0f;
                        for (int l = 0; l < 8; l++) { rec->InjR[l] = Bits(ir[l]); rec->InjI[l] = Bits(ii[l]); }
                        rec->Bar = (int)Num(e, "bar"); rec->Len = (int)Num(e, "len"); rec->Nh = (int)Num(e, "nh");
                        sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpStrike, Flags = CommandFlags.LateDrop, When = MarimbaOffline.At(at), Id = slot });
                    }
                    else if (op == "damp")
                    {
                        int slot = (int)Num(e, "slot");
                        MarimbaDampRecord* rec = h.State->Damps + slot;
                        var pr = Arr(e, "pr"); var pi = Arr(e, "pi");
                        for (int l = 0; l < 8; l++) { rec->Pr[l] = Bits(pr[l]); rec->Pi[l] = Bits(pi[l]); }
                        rec->Bar = (int)Num(e, "bar");
                        sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpDamp, When = MarimbaOffline.At(at), Id = slot });
                    }
                    else sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpUndamp, When = MarimbaOffline.At(at), N = (int)Num(e, "bar") });
                }
            });
            stats = h.Snap();
            return y;
        }

        static readonly string[] Scenarios = { "TV4_single_C4", "TV5_roll_damp_undamp", "TV6_cap_budget400", "TV7_two_mallets_same_sample" };

        [TestCaseSource(nameof(Scenarios))]
        public void TV4to7_BlockHashesBitExactOnTheLabRecords(string name)
        {
            var v = Obj(TV[name]);
            var y = RenderOnLabRecords(name, MarimbaRenderMode.Strict, out var st);
            var fnv = Arr(v, "blockFnv");
            Assert.That(y[0].Length / MarimbaOffline.B, Is.EqualTo(fnv.Count), "blocks");
            int same = 0;
            string firstDiff = null;
            for (int b = 0; b < fnv.Count; b++)
            {
                string got = $"0x{Fnv(y[0], b * 256, 256):x8}/0x{Fnv(y[1], b * 256, 256):x8}/0x{Fnv(y[2], b * 256, 256):x8}";
                if (got == (string)fnv[b]) same++;
                else if (firstDiff == null) firstDiff = $"block {b}: {got} vs lab {fnv[b]}";
            }
            Debug.Log($"[MARIMBA] {name} strict (FloatMode.Default) on the lab's records: {same} / {fnv.Count} block hashes bit-exact{(firstDiff != null ? "; first difference " + firstDiff : "")}");
            Assert.That(same, Is.EqualTo(fnv.Count), firstDiff);
            double sum = 0, peak = 0;
            for (int c = 0; c < 3; c++) for (int i = 0; i < y[c].Length; i++) { sum += y[c][i]; peak = Math.Max(peak, Math.Abs(y[c][i])); }
            Assert.That(sum, Is.EqualTo(Num(v, "sum")).Within(1e-7 * Math.Max(1, Math.Abs(Num(v, "sum")))), "sum");
            Assert.That(peak, Is.EqualTo(Num(v, "peak")).Within(1e-8), "peak");
            var labStats = Obj(v, "stats");
            Assert.That(st.Capped, Is.EqualTo((int)Num(labStats, "capped")), "capped");
            Assert.That(st.Injections, Is.EqualTo((int)Num(labStats, "injections")), "injections");
            Assert.That(st.Retired, Is.EqualTo((int)Num(labStats, "retired")), "retired");
            Assert.That(st.MaxActive, Is.EqualTo((int)Num(labStats, "maxActive")), "max active");
        }

        [TestCaseSource(nameof(Scenarios))]
        public void TV4to7_FastMathWithinTolerance(string name)
        {
            var strict = RenderOnLabRecords(name, MarimbaRenderMode.Strict, out _);
            var fast = RenderOnLabRecords(name, MarimbaRenderMode.Fast, out _);
            double md = 0;
            for (int c = 0; c < 3; c++) for (int i = 0; i < strict[c].Length; i++) md = Math.Max(md, Math.Abs(strict[c][i] - fast[c][i]));
            Debug.Log($"[MARIMBA] {name}: FloatMode.Fast vs strict max |delta| {md:E2} (lab's fused estimate {Num(Obj(TV[name]), "fusedMaxAbsDelta"):E2})");
            Assert.That(md, Is.LessThanOrEqualTo(1e-5));
        }

        [TestCaseSource(nameof(Scenarios))]
        public void TV4to7_TheWholeChainMatchesTheVectors(string name)
        {
            // the C# contact solve feeding the Burst render (what the app runs), against the lab's records-render
            var v = Obj(TV[name]);
            var score = Arr(v, "score");
            var opts = Obj(v, "opts");
            int budget = opts.ContainsKey("budget") ? (int)Num(opts, "budget") : 1200;
            int samples = (int)Math.Ceiling(Num(v, "seconds") * 48000 / 256) * 256;
            var events = new List<object>();
            // renderScore's stable sort by the event sample
            var ordered = score.Select(o => Obj(o)).OrderBy(e => Math.Floor(Num(e, "t") * 48000 + 0.5)).ToList();
            foreach (var e in ordered)
            {
                long at = (long)Math.Floor(Num(e, "t") * 48000 + 0.5);
                if (e.ContainsKey("damp")) events.Add(new List<object> { (double)at, 1.0, Num(e, "damp"), e.ContainsKey("kind") ? Num(e, "kind") : 0.0, e.ContainsKey("u") ? Num(e, "u") : 0.5, 0.0 });
                else if (e.ContainsKey("undamp")) events.Add(new List<object> { (double)at, 2.0, Num(e, "undamp") });
                else events.Add(new List<object> { (double)at, 0.0, Num(e, "bar"), (double)(float)Num(e, "vel"), e.ContainsKey("u") ? Num(e, "u") : 0.42, e.ContainsKey("c") ? Num(e, "c") : 0.0,
                    (double)(int)Mallet(e.ContainsKey("mallet") ? (string)e["mallet"] : "medium") });
            }
            var chain = RenderScore(events, samples, MarimbaRenderMode.Strict, 1, budget);
            var lab = RenderOnLabRecords(name, MarimbaRenderMode.Strict, out _);
            double md = 0;
            for (int c = 0; c < 3; c++) for (int i = 0; i < lab[c].Length; i++) md = Math.Max(md, Math.Abs(chain[c][i] - lab[c][i]));
            var fnv = Arr(v, "blockFnv");
            int same = 0;
            for (int b = 0; b < fnv.Count; b++)
                if ($"0x{Fnv(chain[0], b * 256, 256):x8}/0x{Fnv(chain[1], b * 256, 256):x8}/0x{Fnv(chain[2], b * 256, 256):x8}" == (string)fnv[b]) same++;
            Debug.Log($"[MARIMBA] {name}: C# contact solve + strict render vs the lab: max |delta| {md:E2}, {same} / {fnv.Count} block hashes bit-exact");
            Assert.That(md, Is.LessThanOrEqualTo(1e-5));
        }

        // ------------------------------------------------------------------ TV8, determinism

        [Test]
        public void TV8_RetiresToExactZeroAndResetClearsEverything()
        {
            var r = Obj(Records["TV8_C6"]);
            var y = RenderScore(new List<object> { new List<object> { 0.0, 0.0, 36.0, (double)0.6f, 0.45, 0.0, 0.0 } }, (int)Num(r, "samples"), MarimbaRenderMode.Strict);
            var mono = MarimbaOffline.Mono(y);
            int last = -1;
            for (int i = 0; i < mono.Length; i++) if (mono[i] != 0) last = i;
            Assert.That(last, Is.EqualTo((int)Num(Obj(TV, "TV8_retire_reset"), "C6lastNonZeroSample")), "C6 rings out to exact zero at the lab's sample");
            using var h = new MarimbaOffline(1, 1200, MarimbaRenderMode.Strict);
            h.Sound.Strike(12, 0.8f, 0.42f, 0f, MarimbaMallet.Medium);
            h.RenderBlock();
            h.FlagReset();
            float* o = h.RenderBlock();
            for (int i = 0; i < 3 * 256; i++) Assert.That(o[i], Is.EqualTo(0f), "after Reset: silence");
            for (int g = 0; g < 74; g++)
            {
                Assert.That(Unity.Mathematics.math.any(h.State->Groups[g].Zr != 0f), Is.False);
                Assert.That(Unity.Mathematics.math.any(h.State->Groups[g].Zi != 0f), Is.False);
            }
        }

        [Test]
        public void Determinism_TwoRendersAreIdentical()
        {
            var ev = Arr(Obj(Load("marimba-scores.json"), "techniques"), "events");
            var a = RenderScore(ev, 48000 * 8);
            var b = RenderScore(ev, 48000 * 8);
            for (int c = 0; c < 3; c++) Assert.That(Fnv(a[c]), Is.EqualTo(Fnv(b[c])), "channel " + c);
        }
    }
}
