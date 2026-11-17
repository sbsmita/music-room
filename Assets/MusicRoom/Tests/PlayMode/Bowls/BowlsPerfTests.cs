// The bowls unit's block cost (bowls.md 10, 11.2.7; platform 10.3: bowls <= 0.15 ms average / 0.25 ms worst block),
// measured in the editor with Burst on (Tools/unity.sh test-perf), per 256-frame block, for the spec's scenarios and the
// three render paths: FloatMode.Fast (a plain callee of the graph), Strict, and the Strict function pointer called from a
// Fast entry (phase B's graph path). Logs [PERF] lines; the worst case must stay under 0.25 ms per block here (the Quest 3S
// is expected 3-4x slower: the device check B7 decides).
using System;
using System.Diagnostics;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.BowlsPerf
{
    [BurstCompile]
    public static unsafe class BowlsPerfProbe
    {
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        public static void FastCallsPointer(IntPtr fn, BowlsState* s, UnitEvent* due, int n, int frames, float* outMix, int budget, float share)
            => new FunctionPointer<BowlsDsp.RenderFn>(fn).Invoke(s, due, n, frames, outMix, budget, share);
    }

    [Category("Performance")]
    public unsafe class BowlsPerfTests
    {
        const int SR = 48000, Block = 256;

        sealed class Driver : IDisposable
        {
            public readonly BowlsState* S;
            readonly UnitEvent* _due; readonly float* _mix;
            public long Now; int _seq;
            readonly double[] _ang = new double[3];
            readonly double[] _vf = new double[3]; readonly double[] _N = new double[3], _R = new double[3];
            public Driver(BowlDesign[] design)
            {
                for (int b = 0; b < 3; b++) _R[b] = design[b].R;
                S = (BowlsState*)UnsafeUtility.Malloc(sizeof(BowlsState), 64, Allocator.Persistent);
                UnsafeUtility.MemClear(S, sizeof(BowlsState));
                BowlsDesign.Build(S, design, SR, 1);
                _due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * 64, 64, Allocator.Persistent);
                _mix = (float*)UnsafeUtility.Malloc(4 * Block, 64, Allocator.Persistent);
            }
            public void Dispose() { UnsafeUtility.Free(S, Allocator.Persistent); UnsafeUtility.Free(_due, Allocator.Persistent); UnsafeUtility.Free(_mix, Allocator.Persistent); }
            public int N;
            public void Add(GraphCommand c, int offset) { _due[N++] = new UnitEvent { At = Now + offset, Offset = offset, Seq = _seq++, C = c }; }
            public void Rub(int b, double vf, double N) { _vf[b] = vf; _N[b] = N; }
            /// <summary>One block: the rubs' 90 Hz contact frames that fall in it (main-thread work, untimed), then the render.</summary>
            public void Prepare()
            {
                for (int b = 0; b < 3; b++)
                {
                    if (_vf[b] == 0) continue;
                    long f0 = (Now + 532) / 533 * 533;
                    for (long at = f0; at < Now + Block; at += 533)
                    {
                        double R = _R[b];
                        _ang[b] += _vf[b] / R / 90.0;
                        Add(BowlsCommands.Contact(b, 0, _N[b], _vf[b], _ang[b], _vf[b] / R, 1, 0, 0, SR), (int)(at - Now));
                    }
                }
                for (int i = 0; i < Block; i++) _mix[i] = 0;
            }

            public void Render(int path, IntPtr fn)
            {
                if (path == 0) BowlsDsp.RenderFast(S, _due, N, Block, _mix, 1200, 1f);
                else if (path == 1) BowlsDsp.RenderStrict(S, _due, N, Block, _mix, 1200, 1f);
                else BowlsPerfProbe.FastCallsPointer(fn, S, _due, N, Block, _mix, 1200, 1f);
                N = 0; Now += Block;
            }
        }

        static readonly string[] PathName = { "Fast", "Strict", "StrictPointer" };

        [Test]
        public void BowlsBlockCostStaysInsideTheCap()
        {
            if (!BurstCompiler.IsEnabled) Assert.Ignore("Burst is off");
            var design = BowlsDesign.Bowls(SR);
            IntPtr fn = BowlsDsp.CompileStrictPointer();
            var scenarios = new (string name, Action<Driver> setup)[]
            {
                ("idle (all asleep)", d => { }),
                ("three bowls ringing", d => { for (int b = 0; b < 3; b++) d.Add(BowlsCommands.Strike(b, BowlStrikeKind.Tap, 1.2, b, 1), b); }),
                ("one rubbed, two ringing", d => { d.Add(BowlsCommands.Strike(0, BowlStrikeKind.Tap, 1.2, 0, 1), 0); d.Add(BowlsCommands.Strike(2, BowlStrikeKind.Tap, 1.2, 0, 1), 0); d.Rub(1, 0.33, 4); }),
                ("two rubbed (two hands), one ringing", d => { d.Add(BowlsCommands.Strike(0, BowlStrikeKind.Tap, 1.2, 0, 1), 0); d.Rub(1, 0.33, 4); d.Rub(2, 0.3, 4); }),
                ("worst: all three rubbed in chatter", d => { d.Rub(0, 1.3, 2.5); d.Rub(1, 1.3, 2.5); d.Rub(2, 1.3, 2.5); }),
            };
            double worstAll = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var (name, setup) in scenarios)
                for (int path = 0; path < 3; path++)
                {
                    using var d = new Driver(design);
                    setup(d);
                    for (int k = 0; k < 200; k++) { d.Prepare(); d.Render(path, fn); }   // warm up and get the rubs going
                    int n = 1500; var t = new double[n];
                    for (int k = 0; k < n; k++) { d.Prepare(); long a = Stopwatch.GetTimestamp(); d.Render(path, fn); t[k] = (Stopwatch.GetTimestamp() - a) * 1e6 / Stopwatch.Frequency; }
                    Array.Sort(t);
                    double avg = 0; foreach (var v in t) avg += v; avg /= n;
                    double p99 = t[(int)(0.99 * n)], max = t[n - 1];
                    string line = $"[PERF] bowls {name} [{PathName[path]}]: avg {avg:F1} us, p99 {p99:F1} us, max {max:F1} us per 256-frame block (work {d.S->Work})";
                    Debug.Log(line); sb.AppendLine(line);
                    if (name.StartsWith("worst")) worstAll = Math.Max(worstAll, p99);
                }
            Assert.That(worstAll, Is.LessThan(250), "the worst case's p99 stays under 0.25 ms per block (editor, Burst)");
        }
    }
}
