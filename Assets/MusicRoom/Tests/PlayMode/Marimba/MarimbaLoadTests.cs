// The marimba unit's CPU (spec 1 / 6 / 9.1 "Perf", platform 10.3: Quest Burst estimate <= 0.20 ms average / 0.30 ms worst
// per block): bench.mjs's seven schedules through the real render (MarimbaDsp, Burst, FloatMode.Fast as the engine),
// timed block by block, the fastest of three runs reported (a shared machine only ever slows a run down); and the
// main-thread contact solve per strike for each mallet over C3..C6 and vel .05..1, with the fingertip at vel 0.15 (the
// longest contact, review item 9) on its own line. Category "Performance": Tools/unity.sh test-perf prints the [PERF] lines.
// The timed block includes the phase-A host's own bookkeeping (clearing 3 x 256 floats, handing over the due events),
// which the graph does anyway. Device acceptance is M1 / M2 (spec 9.3).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Burst;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.Marimba
{
    [Category("Performance")]
    public unsafe class MarimbaLoadTests
    {
        const int Sr = 48000, B = 256;
        struct Ev { public double T; public int Bar; public float Vel; public double U; public MarimbaMallet M; }

        static List<Ev> Sorted(List<Ev> e) { var s = new List<Ev>(e); s.Sort((a, b) => a.T.CompareTo(b.T)); return s; }

        static (double avg, double p99, double max, double lanes, int maxLanes) Run(List<Ev> schedule, double seconds, int budget)
        {
            using var h = new MarimbaOffline(1, budget, MarimbaRenderMode.Fast);
            var ev = Sorted(schedule);
            int blocks = (int)Math.Ceiling(seconds * Sr / B), k = 0;
            var t = new double[blocks];
            double lanes = 0; int maxLanes = 0;
            for (int b = 0; b < blocks; b++)
            {
                long s = (long)b * B;
                while (k < ev.Count && (long)Math.Round(ev[k].T * Sr) < s + B) { var e = ev[k++]; h.Sound.StrikeExact(e.Bar, e.Vel, e.U, 0, e.M, MarimbaOffline.At((long)Math.Round(e.T * Sr))); }
                long t0 = Stopwatch.GetTimestamp();
                h.RenderBlock();
                t[b] = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
                lanes += h.State->Work; maxLanes = Math.Max(maxLanes, h.State->Work);
            }
            var srt = (double[])t.Clone(); Array.Sort(srt);
            double avg = 0; foreach (double v in t) avg += v; avg /= blocks;
            return (avg, srt[(int)(blocks * 0.99)], srt[blocks - 1], lanes / blocks, maxLanes);
        }

        [Test]
        public void TheBenchSchedulesPerBlock()
        {
            uint s = 7;
            double Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s / 4294967296.0; }
            var sched = new List<(string, List<Ev>, double, int)>();
            sched.Add(("idle", new List<Ev>(), 2, 1200));
            sched.Add(("one note (C4 mf)", new List<Ev> { new Ev { T = 0.01, Bar = 12, Vel = 0.6f, U = 0.45 } }, 2, 1200));
            { var sc = new List<Ev>(); int[] bars = { 12, 14, 16, 17, 19, 21, 23, 24 }; for (double t = 0.05; t < 10; t += 0.32) sc.Add(new Ev { T = t, Bar = bars[(int)Math.Floor(Rnd() * 8)], Vel = (float)(0.5 + Rnd() * 0.3), U = 0.4 + Rnd() * 0.15 }); sched.Add(("lesson melody (C4-C5, 3 notes/s)", sc, 10, 1200)); }
            { var sc = new List<Ev>(); for (double t = 0.05; t < 10; t += 1 / 24.0) sc.Add(new Ev { T = t, Bar = 19 + ((int)Math.Floor(t * 24) % 2) * 4, Vel = 0.55f, U = 0.45 }); sched.Add(("two-bar roll, 24 strokes/s", sc, 10, 1200)); }
            {
                var sc = new List<Ev>(); for (int k = 0; k < 40; k++) for (int i = 0; i < 37; i++) sc.Add(new Ev { T = 0.05 + k * 0.5 + i * 0.012, Bar = i, Vel = 1f, U = 0.45, M = MarimbaMallet.Hard });
                sched.Add(("glissandi, every bar ringing (budget 2048: cap 37)", sc, 20, 2048));
                sched.Add(("glissandi (default budget 1200: cap 24)", sc, 20, 1200));
            }
            {
                var sc = new List<Ev>(); MarimbaMallet[] ms = { MarimbaMallet.Soft, MarimbaMallet.Medium, MarimbaMallet.Hard, MarimbaMallet.Finger };
                for (double t = 0.05; t < 10; t += 1 / 30.0) { int bar = (int)Math.Floor(Rnd() * 37); float vel = (float)Rnd(); double u = Rnd(); sc.Add(new Ev { T = t, Bar = bar, Vel = vel, U = u, M = ms[(int)Math.Floor(Rnd() * 4)] }); }
                sched.Add(("seated worst: two mallets 15 strokes/s each, random bars (budget 2048)", sc, 10, 2048));
            }
            string report = $"[PERF] marimba (editor, burst={BurstCompiler.IsEnabled}, Burst FloatMode.Fast; us per 256-frame block, fastest of 3 runs):";
            double worstAvg = 0, worstMax = 0;
            foreach (var (name, sc, sec, budget) in sched)
            {
                (double avg, double p99, double max, double lanes, int maxLanes) best = (double.MaxValue, 0, 0, 0, 0);
                for (int rep = 0; rep < 3; rep++) { var r = Run(sc, sec, budget); if (r.avg < best.avg) best = r; }
                string row = $"{name,-72} avg {best.avg,6:F2}  p99 {best.p99,6:F2}  max {best.max,7:F2}  lanes avg {best.lanes,5:F1} max {best.maxLanes,3}";
                Debug.Log("[PERF] marimba " + row);
                report += "\n  " + row;
                worstAvg = Math.Max(worstAvg, best.avg); worstMax = Math.Max(worstMax, best.p99);
            }
            Debug.Log(report);
            if (BurstCompiler.IsEnabled)
            {
                // the Quest is about 4x slower than this Mac (burst.md 10.2): the platform's caps are 200 / 300 us there
                Assert.That(worstAvg * 4, Is.LessThan(200), "the busiest schedule's average, times the Quest factor, is inside the 0.20 ms cap");
            }
        }

        [Test]
        public void TheContactSolvePerStrike_MainThread()
        {
            var contact = new MarimbaContact();
            var mem = new MarimbaStrikeRecord[1];
            string report = "[PERF] marimba contact solve (main thread, managed Mono in the editor; IL2CPP on the device), us per strike over C3..C6, vel .05..1:";
            fixed (MarimbaStrikeRecord* rec = mem)
            {
                for (int w = 0; w < 200; w++) contact.Solve(rec, w % 37, 0.5f, 0.45, 0, (MarimbaMallet)(w & 3), 1);   // JIT warm-up
                foreach (MarimbaMallet m in new[] { MarimbaMallet.Medium, MarimbaMallet.Hard, MarimbaMallet.Soft, MarimbaMallet.Finger })
                {
                    var t = new List<double>();
                    double worstMs = 0;
                    for (int rep = 0; rep < 3; rep++)
                        for (int i = 0; i < 37; i++)
                            foreach (float vel in new[] { 0.05f, 0.15f, 0.3f, 0.6f, 0.9f, 1f })
                            {
                                long t0 = Stopwatch.GetTimestamp();
                                contact.Solve(rec, i, vel, 0.45, 0.1, m, 1);
                                t.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
                                worstMs = Math.Max(worstMs, contact.Last.ContactMs);
                            }
                    t.Sort();
                    string row = $"{m,-7} median {t[t.Count / 2],6:F1}  p90 {t[t.Count * 9 / 10],6:F1}  max {t[t.Count - 1],6:F1}  (longest contact {worstMs:F2} ms)";
                    report += "\n  " + row;
                    Debug.Log("[PERF] marimba solve " + row);
                }
                // review item 9: the fingertip at vel 0.15 (the slowest, longest contact) on every bar
                var f = new List<double>();
                for (int rep = 0; rep < 5; rep++)
                    for (int i = 0; i < 37; i++)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        contact.Solve(rec, i, 0.15f, 0.45, 0, MarimbaMallet.Finger, 1);
                        f.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
                    }
                f.Sort();
                string fr = $"finger vel 0.15 (OS 1, contact {contact.Last.ContactMs:F2} ms at C6): median {f[f.Count / 2]:F1}  p90 {f[f.Count * 9 / 10]:F1}  max {f[f.Count - 1]:F1} us per strike";
                report += "\n  " + fr;
                Debug.Log("[PERF] marimba solve " + fr);
                Debug.Log(report);
                Assert.That(f[f.Count / 2] * 4, Is.LessThan(80), "a fingertip at vel 0.15, times the Quest factor, stays under check M2's 80 us");
            }
        }
    }
}
