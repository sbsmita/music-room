// Block cost of the handpan unit (handpan.md 4 and 9.1 Perf; platform.md 10.3): bench.mjs's scenarios through the Burst
// path (HandpanDsp.RenderDirect, FloatMode.Fast, DspKernels), timed per 256-frame block with a Stopwatch, and zero
// allocation on the audio side and in the main-thread API. [PERF] lines go to the log; Reference~/bench.json holds the
// Node numbers (Apple M5 Pro). Burst.md 10.2's ratio (Quest 3S at CPU level 3 ~ 0.10 x Node) gives the device estimate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using MusicRoom.Audio.Burst;
using MusicRoom.Handpan;
using MusicRoom.Tests.Handpan;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.Audio
{
    public unsafe class HandpanPerfTests
    {
        public struct Result { public string Name; public double Mean, P99, Max, Lanes, LanesMax; public int Blocks; }

        static long At(double t) => HandpanCommands.JsRound(t * HandpanRig.Sr);

        /// <summary>One bench.mjs scenario, best of reps (the reference's rule), Burst path.</summary>
        public static Result Run(string name, double seconds, Action<HandpanRig> schedule, HandpanVoice voice = HandpanVoice.Calm, int reps = 3)
        {
            Result best = default; best.Mean = double.MaxValue;
            int n = (int)Math.Round(seconds * HandpanRig.Sr / 256);
            var times = new double[n];
            var lanes = new int[n];
            var block = new float[256];
            for (int r = 0; r < reps; r++)
            {
                using var rig = new HandpanRig(voice, burst: true);
                schedule(rig);
                var sw = new Stopwatch();
                rig.Timer = sw;
                for (int b = 0; b < n; b++)
                {
                    Array.Clear(block, 0, 256);
                    sw.Reset();
                    rig.Render(block, 0, 256, 256);
                    times[b] = sw.Elapsed.TotalMilliseconds * 1000;
                    lanes[b] = rig.S->Work;
                }
                var sorted = (double[])times.Clone();
                Array.Sort(sorted);
                double mean = 0, lm = 0, lx = 0;
                for (int b = 0; b < n; b++) { mean += times[b]; lm += lanes[b]; lx = Math.Max(lx, lanes[b]); }
                mean /= n;
                if (mean < best.Mean)
                    best = new Result { Name = name, Blocks = n, Mean = mean, P99 = sorted[(int)Math.Floor(0.99 * n)], Max = sorted[n - 1], Lanes = lm / n / 256, LanesMax = lx / 256 };
            }
            return best;
        }

        /// <summary>The rig's own bookkeeping (lists, sort) is timed with the render; this measures the unit alone: the
        /// same blocks rendered straight from a preallocated event array.</summary>
        static Result RunWorstDirect(int reps = 3)
        {
            Result best = default; best.Mean = double.MaxValue;
            const int n = 400;
            for (int r = 0; r < reps; r++)
            {
                long bytes = HandpanStateBuilder.Bytes(256);
                byte* mem = (byte*)UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
                UnsafeUtility.MemClear(mem, bytes);
                HandpanState* s = HandpanStateBuilder.Layout(mem, 256);
                HandpanStateBuilder.Fill(s, HandpanVoice.Calm);
                s->Ready = 1;
                UnitEvent* due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * 16, 64, Allocator.Persistent);
                float* outp = (float*)UnsafeUtility.Malloc(4 * 256, 64, Allocator.Persistent);
                var times = new double[n];
                var sw = new Stopwatch();
                long frame = 0;
                for (int b = 0; b < n; b++)
                {
                    // every field ff every 60 ms (2 ms apart): bench.mjs 'worst'
                    int nd = 0;
                    for (int k = 0; k < 9; k++)
                    {
                        long t0 = (frame / 2880) * 2880 + 480 + 96 * k;
                        if (t0 >= frame && t0 < frame + 256)
                            due[nd++] = new UnitEvent { At = t0, Offset = (int)(t0 - frame), Seq = nd, C = HandpanCommands.Strike(k, 1.0, 0.35f, 0.35f, HandpanTech.Tip, 0) };
                    }
                    for (int i = 0; i < 256; i++) outp[i] = 0f;
                    sw.Restart();
                    HandpanDsp.RenderDirect(s, due, nd, outp, 256, 1200f);
                    sw.Stop();
                    times[b] = sw.Elapsed.TotalMilliseconds * 1000;
                    frame += 256;
                }
                UnsafeUtility.Free(mem, Allocator.Persistent); UnsafeUtility.Free(due, Allocator.Persistent); UnsafeUtility.Free(outp, Allocator.Persistent);
                Array.Sort(times);
                double mean = 0; foreach (double t in times) mean += t; mean /= n;
                if (mean < best.Mean) best = new Result { Name = "worst, unit alone (400 blocks)", Blocks = n, Mean = mean, P99 = times[(int)(0.99 * n)], Max = times[n - 1] };
            }
            return best;
        }

        public static List<Result> AllScenarios()
        {
            var R = new List<Result>
            {
                Run("one note ringing (A3 mf)", 6, s => s.Strike(At(0.01), 1, 0.6, 0.35, 0.35)),
                Run("lesson: a note every 0.6 s for 20 s", 20, s => { for (int k = 0; k < 33; k++) s.Strike(At(0.01 + 0.6 * k), 1 + (k * 3) % 8, 0.55, 0.3, 0.2); }),
                Run("groove: ding slaps, taks and notes, 8 per second, 12 s", 12, Groove),
                Run("worst: every field ff every 60 ms (10-finger roll), 8 s", 8, Worst),
                Run("palm mutes while ringing (damp churn), 6 s", 6, s => { for (int k = 0; k < 20; k++) { s.Strike(At(0.01 + 0.3 * k), k % 9, 0.8, 0.3, 0.3); s.Damp(At(0.15 + 0.3 * k), k % 9, 3, 1, 0, 0, 1); s.Damp(At(0.25 + 0.3 * k), k % 9, 3, 0, 0, 0, 1); } }),
                Run("idle (after everything decayed)", 2, s => { }),
                Run("measured voice: worst", 8, Worst, HandpanVoice.Measured),
                Run("measured voice: groove", 12, Groove, HandpanVoice.Measured),
            };
            return R;
        }

        static void Groove(HandpanRig s)
        {
            for (int k = 0; k < 96; k++)
            {
                double t = 0.01 + k * 0.125;
                if (k % 4 == 0) s.Strike(At(t), 0, 0.8, 0, 0, HandpanTech.Slap);
                else if (k % 4 == 2) s.Tak(At(t), 0.6, HandpanSpec.TakDefaultX, HandpanSpec.TakDefaultZ);
                else s.Strike(At(t), 1 + (k % 8), 0.7, 0.35, 0.2);
            }
        }

        static void Worst(HandpanRig s)
        {
            for (int k = 0; k < 133; k++) for (int i = 0; i < 9; i++) s.Strike(At(0.01 + 0.06 * k + 0.002 * i), i, 1, 0.35, 0.35);
        }

        [Test]
        public void BlockCostIsLoggedAndWithinTheBudget()
        {
            var bench = HandpanJson.O(HandpanJson.Load(System.IO.Path.Combine(HandpanJson.ReferenceDir("Audio/Handpan"), "bench.json")));
            List<object> node = bench.Arr("results");
            List<Result> R = AllScenarios();
            for (int k = 0; k < R.Count; k++)
            {
                Result r = R[k];
                var nr = HandpanJson.O(node[k]);
                Debug.Log($"[PERF] handpan {r.Name}: editor Burst avg {r.Mean:F1} us, p99 {r.P99:F1}, max {r.Max:F1} per 256-frame block; lanes {r.Lanes:F0} (max {r.LanesMax:F0}); Node {nr.Num("nodeMeanUs"):F1} / {nr.Num("nodeMaxUs"):F1} us (lanes {nr.Num("lanesRenderedMean"):F0}); burst {BurstCompiler.IsEnabled}");
                Assert.AreEqual(nr.Num("lanesRenderedMean"), r.Lanes, 1.0, r.Name + ": the same work as the reference");
            }
            Debug.Log($"[PERF] handpan state: {HandpanStateBuilder.Bytes(256) / 1024.0:F1} KiB at 256-frame blocks, {HandpanStateBuilder.Bytes(1024) / 1024.0:F1} KiB at 1024 (one Alloc; no baked audio)");
            Result direct = RunWorstDirect();
            Debug.Log($"[PERF] handpan {direct.Name}: avg {direct.Mean:F1} us, p99 {direct.P99:F1}, max {direct.Max:F1}");
            if (BurstCompiler.IsEnabled)
            {
                double nodeWorst = HandpanJson.O(node[3]).Num("nodeMeanUs");
                Assert.That(direct.Mean, Is.LessThan(0.5 * nodeWorst), "handpan.md 9.1: the editor's Burst worst case at most half the Node value");
            }
        }

        sealed class FixedPort : IHandpanCommandPort
        {
            public readonly GraphCommand[] Buf = new GraphCommand[64];
            public int N;
            public bool Stage(in GraphCommand c) { if (N < Buf.Length) Buf[N++] = c; return true; }
            public void Commit() { }
        }

        [Test]
        public void TheRenderAndTheApiAllocateNothing()
        {
            long bytes = HandpanStateBuilder.Bytes(256);
            byte* mem = (byte*)UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(mem, bytes);
            HandpanState* s = HandpanStateBuilder.Layout(mem, 256);
            HandpanStateBuilder.Fill(s, HandpanVoice.Calm);
            s->Ready = 1;
            UnitEvent* due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * 16, 64, Allocator.Persistent);
            float* outp = (float*)UnsafeUtility.Malloc(4 * 256, 64, Allocator.Persistent);
            var port = new FixedPort();
            var sound = new HandpanSound(port, s);
            try
            {
                for (int warm = 0; warm < 2; warm++)
                {
                    port.N = 0;
                    sound.Strike(1, 0.6f, 0.35f, 0.2f, HandpanTech.Tip, 0);
                    sound.StrikeTak(0.6, 1, 0.6, 2, 0.5, 0);
                    sound.Damp(4, 1, 1f, 0.3f, 0.2f, HandpanDampKind.Finger, 0);
                    sound.ReleaseAll(0);
                    sound.Level(1);
                    for (int k = 0; k < port.N; k++) due[k] = new UnitEvent { Offset = 0, Seq = k, C = port.Buf[k] };
                    HandpanDsp.RenderDirect(s, due, port.N, outp, 256, 1200f);
                    HandpanDsp.RenderDirect(s, due, 0, outp, 256, 1200f);
                }
                Assert.That(() =>
                {
                    port.N = 0;
                    sound.Strike(1, 0.6f, 0.35f, 0.2f, HandpanTech.Tip, 0);
                    sound.Strike(0, 0.8f, 0f, 0f, HandpanTech.Slap, 0);
                    sound.StrikeTak(0.6, 1, 0.6, 2, 0.5, 0);
                    sound.Damp(4, 1, 1f, 0.3f, 0.2f, HandpanDampKind.Finger, 0);
                    sound.Damp(9, 3, 1f, 0f, 0f, HandpanDampKind.Palm, 0);
                    sound.ReleaseAll(0);
                    sound.Level(1);
                }, Is.Not.AllocatingGCMemory(), "the main-thread API");
                Assert.That(() =>
                {
                    for (int k = 0; k < port.N; k++) due[k] = new UnitEvent { Offset = 3 * k, Seq = k, C = port.Buf[k] };
                    HandpanDsp.RenderDirect(s, due, port.N, outp, 256, 1200f);
                    for (int b = 0; b < 20; b++) HandpanDsp.RenderDirect(s, due, 0, outp, 256, 1200f);
                }, Is.Not.AllocatingGCMemory(), "the render (Burst)");
                Assert.That(() =>
                {
                    s->Exact = 1;
                    HandpanDsp.Render(s, due, 0, outp, 256, 400f);
                    s->Exact = 0;
                }, Is.Not.AllocatingGCMemory(), "the render (managed, exact)");
            }
            finally
            {
                UnsafeUtility.Free(mem, Allocator.Persistent); UnsafeUtility.Free(due, Allocator.Persistent); UnsafeUtility.Free(outp, Allocator.Persistent);
            }
        }
    }
}
