// The handpan unit's offline harness (phase A: the platform has not given the handpan a UnitId yet, so it cannot run
// inside the graph). It owns one HandpanState, is the HandpanSound's command port, and collects the staged commands into
// sorted due events per block exactly as AudioGraph.CollectUnit does (When &gt; 0 absolute seconds -&gt; round(When sr), &lt;= 0
// relative to the block; late events at the block start; (At, Seq) order), then renders through
//   Exact: HandpanDsp.Render called from managed code with State.Exact = 1 (the reference's two-rounding kernels:
//          bit-exact with Node), or
//   Burst: HandpanDsp.RenderDirect, the unit's own Burst entry with the engine's FloatMode.Fast (DspKernels: what the
//          graph runs on the device; within 1e-5 of the reference).
// Rounding of absolute times uses the reference's Math.round (half up); the graph's math.round only differs at an exact
// half sample, which no test uses.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Handpan;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace MusicRoom.Tests.Audio
{
    public sealed unsafe class HandpanRig : IHandpanCommandPort, IDisposable
    {
        public const int Sr = 48000;
        public readonly HandpanState* S;
        public readonly HandpanSound Sound;
        public long Frame;
        public bool Burst;
        public float Budget = 1200f;
        readonly byte* _mem;
        readonly UnitEvent* _due;
        const int DueCap = 1024;
        readonly List<GraphCommand> _staged = new List<GraphCommand>();
        readonly List<(GraphCommand c, int seq)> _ring = new List<(GraphCommand, int)>();
        readonly List<UnitEvent> _pending = new List<UnitEvent>();
        int _seq;

        public HandpanRig(HandpanVoice voice = HandpanVoiceChoice.Voice, bool burst = false, int maxFrames = 1024)
        {
            long bytes = HandpanStateBuilder.Bytes(maxFrames);
            _mem = (byte*)UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(_mem, bytes);
            S = HandpanStateBuilder.Layout(_mem, maxFrames);
            HandpanStateBuilder.Fill(S, voice);
            S->Ready = 1;
            Burst = burst;
            _due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * DueCap, 64, Allocator.Persistent);
            Sound = new HandpanSound(this, S);
        }

        public void Dispose()
        {
            UnsafeUtility.Free(_mem, Allocator.Persistent);
            UnsafeUtility.Free(_due, Allocator.Persistent);
        }

        // ---- IHandpanCommandPort
        public bool Stage(in GraphCommand c) { _staged.Add(c); return true; }
        public void Commit() { foreach (GraphCommand c in _staged) _ring.Add((c, _seq++)); _staged.Clear(); }
        public List<(GraphCommand c, int seq)> Posted => _ring;

        // ---- the reference's calls with absolute sample times (test-vectors.mjs render())
        public static double When(long at) => at <= 0 ? 0 : at / (double)Sr;
        public void Strike(long at, int field, double vel, double u, double v, HandpanTech tech = HandpanTech.Tip, double gain = 1) =>
            Sound.StrikeExact(field, vel, (float)u, (float)v, tech, When(at), gain);
        public void Tak(long at, double vel, double X, double Z)
        {
            HandpanMath.TakNeighbours(X, Z, out HandpanNeighbour n0, out HandpanNeighbour n1);
            Sound.StrikeTak(vel, n0.Field, n0.W, n1.Field, n1.W, When(at));
        }
        public void Damp(long at, int field, int slot, double strength, double u = 0, double v = 0, int kind = 0) =>
            Sound.Damp(field, slot, (float)strength, (float)u, (float)v, (HandpanDampKind)kind, When(at));
        public void Reset(long at) { Stage(new GraphCommand { Op = (GraphOp)HandpanState.OpReset, When = When(at) }); Commit(); }
        public void SetBudget(long at, float b) { Stage(HandpanCommands.SetBudget(b, When(at))); Commit(); }

        void Collect(long blockStart, long end)
        {
            foreach (var (c, seq) in _ring)
            {
                long raw = c.When > 0 ? (long)Math.Floor(c.When * Sr + 0.5) : blockStart + (long)Math.Floor(-c.When * Sr + 0.5);
                if ((c.Flags & CommandFlags.LateDrop) != 0 && raw < blockStart - 4800) continue;
                _pending.Add(new UnitEvent { At = Math.Max(blockStart, raw), Seq = seq, C = c });
            }
            _ring.Clear();
        }

        /// <summary>Renders frames samples (blocks of `block`) into a new array; onBlock(blockEndFrame) after each block.</summary>
        public float[] Render(int frames, int block = 256, Action<long> onBlock = null)
        {
            var y = new float[frames];
            Render(y, 0, frames, block, onBlock);
            return y;
        }

        /// <summary>When set, times ONLY the unit's render call of each block (not the rig's bookkeeping).</summary>
        public System.Diagnostics.Stopwatch Timer;

        public void Render(float[] y, int from, int frames, int block = 256, Action<long> onBlock = null)
        {
            S->Exact = Burst ? 0 : 1;
            fixed (float* py = y)
            {
                for (int done = 0; done < frames; done += block)
                {
                    int b = Math.Min(block, frames - done);
                    long end = Frame + b;
                    Collect(Frame, end);
                    _pending.Sort((p, q) => p.At != q.At ? p.At.CompareTo(q.At) : p.Seq.CompareTo(q.Seq));
                    int n = 0;
                    for (int k = 0; k < _pending.Count;)
                    {
                        if (_pending[k].At < end && n < DueCap)
                        {
                            UnitEvent e = _pending[k];
                            e.Offset = (int)(e.At - Frame);
                            _due[n++] = e;
                            _pending.RemoveAt(k);
                        }
                        else k++;
                    }
                    Timer?.Start();
                    if (Burst) HandpanDsp.RenderDirect(S, _due, n, py + from + done, b, Budget);
                    else HandpanDsp.Render(S, _due, n, py + from + done, b, Budget);
                    Timer?.Stop();
                    Frame = end;
                    onBlock?.Invoke(end);
                }
            }
        }

        public int ActiveGroups()
        {
            int n = 0;
            for (int g = 0; g < HandpanState.NG; g++) n += S->Active[g];
            return n;
        }

        /// <summary>Subnormal state values in the bank (TV9).</summary>
        public int Subnormals()
        {
            int n = 0;
            for (int g = 0; g < HandpanState.NG; g++)
                for (int l = 0; l < 4; l++)
                {
                    float zr = S->Bank[g].Zr[l], zi = S->Bank[g].Zi[l];
                    if (zr != 0f && Math.Abs(zr) < 1.1754944e-38f) n++;
                    if (zi != 0f && Math.Abs(zi) < 1.1754944e-38f) n++;
                }
            return n;
        }

        /// <summary>Energy (dB) of group g: sum over lanes of |z|^2 go^2, in double (test-vectors.mjs TV6).</summary>
        public double GroupEnergyDb(int g)
        {
            double e = 0;
            for (int l = 0; l < 4; l++)
            {
                double zr = S->Bank[g].Zr[l], zi = S->Bank[g].Zi[l], go = S->Bank[g].Go[l];
                e += (zr * zr + zi * zi) * go * go;
            }
            return 10 * Math.Log10(e + 1e-30);
        }

        public static double RmsDb(float[] x, int a, int b)
        {
            double e = 0;
            for (int i = a; i < b; i++) e += (double)x[i] * x[i];
            return 10 * Math.Log10(e / (b - a) + 1e-30);
        }
    }
}
