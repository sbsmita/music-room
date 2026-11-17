// A standalone host for the hand-drum unit (phase A: UnitId.HandDrums, its sources and its UnitHooks partial do not exist
// in this base yet, so the unit cannot be registered in the graph). It does for the unit exactly what
// AudioGraph.RenderUnits / CollectUnit do for a registered one: commands to pending events (When: > 0 absolute seconds,
// else blockStart + round(-When sr); LateDrop more than 100 ms late), the block's due events sorted by (At, Seq) with
// their offsets, the channels cleared, then HandDrumsDsp.Render(u) through the chosen entry. Used by the EditMode tests,
// the offline renders and the perf test; phase B renders the real unit through OfflineEngine instead.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    public enum HandDrumsEntry { Managed = 0, BurstStrict = 1, BurstFast = 2 }

    public sealed unsafe class HandDrumsOffline : IHandDrumsCommandSink, IDisposable
    {
        public const int Block = HandDrumsConst.MaxFrames, Sr = HandDrumsConst.Sr;
        readonly List<IntPtr> _mem = new List<IntPtr>();
        readonly GraphCommand* _staged;
        readonly UnitEvent* _pending;
        readonly int _stagedCap, _pendingCap;
        int _stagedCount, _pendingCount, _seq;
        public UnitSlot* Slot { get; }
        public GraphState* Graph { get; }
        public HandDrumsState* State { get; }
        public HandDrumsSound Sound { get; }
        public HandDrumsEntry Entry = HandDrumsEntry.BurstFast;
        /// <summary>The next block's absolute start sample.</summary>
        public long Now { get; private set; }
        public int LateDropped { get; private set; }
        public int Dropped { get; private set; }
        public long Bytes { get; private set; }
        /// <summary>Stopwatch ticks of the last block's HandDrumsDsp.Render alone (the perf test).</summary>
        public long LastRenderTicks { get; private set; }
        /// <summary>Commands staged or pending (not yet due).</summary>
        public int Pending => _stagedCount + _pendingCount;

        public HandDrumsOffline(byte[] tables = null, uint seed = 1, int capacity = 16384)
        {
            var t = HandDrumsTables.Parse(tables ?? HandDrumsTables.LoadDefaultBytes());
            State = HandDrumsBuilder.Build(t, bytes => (IntPtr)Alloc(bytes), seed);
            _stagedCap = capacity; _pendingCap = capacity;
            _staged = (GraphCommand*)Alloc((long)sizeof(GraphCommand) * capacity);
            _pending = (UnitEvent*)Alloc((long)sizeof(UnitEvent) * capacity);
            Graph = (GraphState*)Alloc(sizeof(GraphState));
            Graph->Budget = 1200;
            Graph->SampleRate = Sr;
            Slot = (UnitSlot*)Alloc(sizeof(UnitSlot));
            Slot->Id = UnitId.Count;
            Slot->Kernel = UnitId.Count;
            Slot->Registered = 1;
            Slot->State = State;
            Slot->Channels = 2;
            Slot->MaxFrames = Block;
            Slot->SampleRate = Sr;
            Slot->Out = (float*)Alloc(2L * Block * 4);
            Slot->Due = (UnitEvent*)Alloc((long)sizeof(UnitEvent) * capacity);
            Slot->Graph = Graph;
            Slot->Share = 1f;   // the governor's share in the app (UnitSlot.Share); tests scale HandDrumsState.Share
            Sound = new HandDrumsSound(this, State);
        }

        void* Alloc(long bytes)
        {
            bytes = Math.Max(1, bytes);
            void* p = UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(p, bytes);
            _mem.Add((IntPtr)p);
            Bytes += bytes;
            return p;
        }

        public void Dispose()
        {
            foreach (var p in _mem) UnsafeUtility.Free((void*)p, Allocator.Persistent);
            _mem.Clear();
        }

        public bool StageCommand(in GraphCommand c)
        {
            if (_stagedCount >= _stagedCap) { Dropped++; return false; }
            _staged[_stagedCount++] = c;
            return true;
        }

        public void CommitCommands() { }

        /// <summary>The graph's LOD budget (400..2048, default 1200).</summary>
        public int Budget { get => Graph->Budget; set => Graph->Budget = value; }

        /// <summary>Renders one block of frames (&lt;= 256) into the two drums' outputs at [offset, offset + frames):
        /// conga (drum 0, channel 1) and tumba (drum 1, channel 0).</summary>
        public void RenderBlock(float[] conga, float[] tumba, int offset, int frames)
        {
            long blockStart = Now, end = blockStart + frames;
            for (int i = 0; i < _stagedCount; i++)
            {
                GraphCommand c = _staged[i];
                if (_pendingCount >= _pendingCap) { Dropped++; continue; }
                long raw = c.When > 0 ? (long)math.round(c.When * Sr) : blockStart + (long)math.round(-c.When * Sr);
                if ((c.Flags & CommandFlags.LateDrop) != 0 && raw < blockStart - 4800) { LateDropped++; continue; }
                _pending[_pendingCount++] = new UnitEvent { At = math.max(blockStart, raw), Seq = _seq++, C = c };
            }
            _stagedCount = 0;
            int due = 0, keep = 0;
            for (int i = 0; i < _pendingCount; i++)
            {
                UnitEvent e = _pending[i];
                if (e.At < end) Slot->Due[due++] = e; else _pending[keep++] = e;
            }
            _pendingCount = keep;
            for (int i = 1; i < due; i++)
            {
                UnitEvent x = Slot->Due[i];
                int j = i - 1;
                while (j >= 0 && (Slot->Due[j].At > x.At || (Slot->Due[j].At == x.At && Slot->Due[j].Seq > x.Seq))) { Slot->Due[j + 1] = Slot->Due[j]; j--; }
                Slot->Due[j + 1] = x;
            }
            for (int i = 0; i < due; i++) Slot->Due[i].Offset = (int)(Slot->Due[i].At - blockStart);
            Slot->DueCount = due;
            Slot->BlockStart = blockStart;
            Slot->Frames = frames;
            for (int i = 0; i < 2 * Block; i++) Slot->Out[i] = 0f;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            switch (Entry)
            {
                case HandDrumsEntry.Managed: HandDrumsDsp.Render(Slot); break;
                case HandDrumsEntry.BurstStrict: HandDrumsBurst.RenderStrict(Slot); break;
                default: HandDrumsBurst.RenderFast(Slot); break;
            }
            LastRenderTicks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Slot->Flags = 0;
            Slot->Blocks++;
            float* hi = Slot->Channel(1), lo = Slot->Channel(0);
            if (conga != null) for (int n = 0; n < frames; n++) conga[offset + n] = hi[n];
            if (tumba != null) for (int n = 0; n < frames; n++) tumba[offset + n] = lo[n];
            Now = end;
        }

        /// <summary>Renders frames samples; beforeBlock(blockStart) may post the commands that block needs.</summary>
        public void Render(float[] conga, float[] tumba, int frames, Action<long> beforeBlock = null)
        {
            for (int s = 0; s < frames; s += Block)
            {
                int m = Math.Min(Block, frames - s);
                beforeBlock?.Invoke(Now);
                RenderBlock(conga, tumba, s, m);
            }
        }

        /// <summary>The two drums' outputs over frames samples (conga, tumba).</summary>
        public float[][] Render(int frames, Action<long> beforeBlock = null)
        {
            var y = new[] { new float[frames], new float[frames] };
            Render(y[0], y[1], frames, beforeBlock);
            return y;
        }

        /// <summary>A sample index as an absolute When (the Node references' round(t * 48000)).</summary>
        public static double At(long sample) => sample / (double)Sr;
    }
}
