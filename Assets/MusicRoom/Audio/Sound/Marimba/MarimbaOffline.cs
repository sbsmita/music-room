// A stand-alone host for the marimba unit, for phase A (the platform's UnitId.Marimba and its sources do not exist yet,
// so the unit cannot register in the graph): it owns a MarimbaState, a UnitSlot laid out as the engine lays one out, a
// pending queue with the engine's event rules, and renders blocks through the same MarimbaDsp.Render the graph will
// call (Burst, FloatMode.Fast as the engine, or FloatMode.Default for bit-exact checks). The sample clock starts at 0;
// GraphCommand.When maps as in the engine's offline harness: > 0 absolute seconds -> round(When * 48000), 0 = the next
// block, < 0 = -When seconds after the next block's start. Onsets more than 100 ms late are dropped (LateDrop).
// Main thread / test code; Render allocates only its result arrays.
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace MusicRoom.Audio.Burst
{
    public enum MarimbaRenderMode { Fast, Strict }

    public sealed unsafe class MarimbaOffline : IMarimbaCommandSink, IDisposable
    {
        public const int Sr = MarimbaTables.SampleRate, B = MarimbaConst.Block;
        const int PendingCap = 8192, DueCap = 1024;

        struct Pend { public long At; public int Seq; public GraphCommand C; }

        readonly byte* _mem;
        readonly UnitSlot* _slot;
        readonly UnitEvent* _due;
        readonly float* _out;
        readonly Pend[] _pending = new Pend[PendingCap];
        int _np, _seq;
        bool _disposed;

        public readonly MarimbaSound Sound;
        public MarimbaState* State { get; }
        public MarimbaRenderMode Mode;
        /// <summary>The next block's first sample.</summary>
        public long Frame { get; private set; }
        public int LateDropped { get; private set; }
        public int DroppedEvents { get; private set; }

        public MarimbaOffline(uint seed = 1, int budget = 1200, MarimbaRenderMode mode = MarimbaRenderMode.Fast)
        {
            long bytes = MarimbaStateBuilder.Bytes();
            _mem = (byte*)UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(_mem, bytes);
            State = MarimbaStateBuilder.Build(_mem);
            State->Budget = budget;
            _slot = (UnitSlot*)UnsafeUtility.Malloc(sizeof(UnitSlot), 64, Allocator.Persistent);
            UnsafeUtility.MemClear(_slot, sizeof(UnitSlot));
            _due = (UnitEvent*)UnsafeUtility.Malloc(sizeof(UnitEvent) * DueCap, 64, Allocator.Persistent);
            _out = (float*)UnsafeUtility.Malloc(4L * MarimbaConst.Channels * B, 64, Allocator.Persistent);
            _slot->State = State;
            _slot->Channels = MarimbaConst.Channels;
            _slot->Out = _out;
            _slot->MaxFrames = B;
            _slot->SampleRate = Sr;
            _slot->Due = _due;
            _slot->Registered = 1;
            Mode = mode;
            Sound = new MarimbaSound(State, this, seed);
        }

        public static long StateBytes => MarimbaStateBuilder.Bytes();

        /// <summary>The engine's offline time rule for an absolute sample.</summary>
        public static double At(long sample) => sample / (double)Sr;

        bool IMarimbaCommandSink.Post(in GraphCommand c)
        {
            long at;
            if (c.When > 0) at = (long)Math.Floor(c.When * Sr + 0.5);
            else if (c.When < 0) at = Frame + (long)Math.Floor(-c.When * Sr + 0.5);
            else at = Frame;
            if (_np >= PendingCap) { DroppedEvents++; return false; }
            int k = _np;
            while (k > 0 && _pending[k - 1].At > at) { _pending[k] = _pending[k - 1]; k--; }   // stable: equal times keep call order
            _pending[k] = new Pend { At = at, Seq = _seq++, C = c };
            _np++;
            return true;
        }

        /// <summary>Renders one block (frames <= 256) and returns the three channel buffers (Lo, Mid, Hi), valid until the
        /// next call.</summary>
        public float* RenderBlock(int frames = B)
        {
            if (frames > B) throw new ArgumentOutOfRangeException(nameof(frames));
            long blockStart = Frame, end = blockStart + frames;
            UnsafeUtility.MemClear(_out, 4L * MarimbaConst.Channels * B);
            int nd = 0, taken = 0;
            while (taken < _np && _pending[taken].At < end && nd < DueCap)
            {
                Pend p = _pending[taken++];
                if ((p.C.Flags & CommandFlags.LateDrop) != 0 && p.At < blockStart - MarimbaConst.LateDropSamples) { LateDropped++; continue; }
                _due[nd++] = new UnitEvent { At = p.At, Offset = (int)Math.Max(0, p.At - blockStart), Seq = p.Seq, C = p.C };
            }
            if (taken > 0) { Array.Copy(_pending, taken, _pending, 0, _np - taken); _np -= taken; }
            _slot->DueCount = nd;
            _slot->BlockStart = blockStart;
            _slot->Frames = frames;
            if (Mode == MarimbaRenderMode.Strict) MarimbaDsp.RenderStrict(_slot);
            else MarimbaDsp.RenderFast(_slot);
            _slot->Flags = 0;
            _slot->Blocks++;
            Frame = end;
            return _out;
        }

        /// <summary>The unit's audio-thread counters.</summary>
        public struct Snapshot { public int StrikesPlayed, Injections, DroppedPlayers, DroppedActions, Capped, Retired, MaxActive, Work, ActiveBars; }

        public Snapshot Snap() => new Snapshot
        {
            StrikesPlayed = State->StrikesPlayed, Injections = State->Injections, DroppedPlayers = State->DroppedPlayers, DroppedActions = State->DroppedActions,
            Capped = State->Capped, Retired = State->Retired, MaxActive = State->MaxActive, Work = State->Work, ActiveBars = State->ActiveBars,
        };

        /// <summary>Channel c of the last block.</summary>
        public float* Channel(int c) => _out + c * B;

        /// <summary>Sets UnitFlags.Reset for the next block (a resume after a suspension).</summary>
        public void FlagReset() => _slot->Flags |= UnitFlags.Reset;

        /// <summary>Renders samples (rounded up to whole blocks) into three channels; beforeBlock(blockStart) posts what the
        /// block needs, as the app's main thread does a frame ahead.</summary>
        public float[][] Render(int samples, Action<long> beforeBlock = null, Action<long> afterBlock = null)
        {
            int n = (samples + B - 1) / B * B;
            var y = new[] { new float[n], new float[n], new float[n] };
            for (int s = 0; s < n; s += B)
            {
                beforeBlock?.Invoke(Frame);
                float* o = RenderBlock(B);
                for (int c = 0; c < MarimbaConst.Channels; c++)
                {
                    float* src = o + c * B;
                    float[] dst = y[c];
                    for (int i = 0; i < B; i++) dst[s + i] = src[i];
                }
                afterBlock?.Invoke(Frame);
            }
            return y;
        }

        public static float[] Mono(float[][] ch)
        {
            var m = new float[ch[0].Length];
            // as the reference: the three float32 channels summed in double, rounded once
            for (int i = 0; i < m.Length; i++) m[i] = (float)((double)ch[0][i] + ch[1][i] + ch[2][i]);
            return m;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnsafeUtility.Free(_mem, Allocator.Persistent);
            UnsafeUtility.Free(_slot, Allocator.Persistent);
            UnsafeUtility.Free(_due, Allocator.Persistent);
            UnsafeUtility.Free(_out, Allocator.Persistent);
        }

        ~MarimbaOffline() { if (!_disposed) Dispose(); }
    }
}
