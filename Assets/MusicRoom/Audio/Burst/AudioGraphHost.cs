// Owner of the Burst graph on the Unity side (spec 10-upgrade/burst.md section 3). Main thread: allocate the arena,
// add banks and one-shot presets, register sound units, post commands, run the adaptive CPU budget. Audio thread:
// EnsureRendered(blockStart, frames) from the FIRST SynthSource callback of each block renders the whole graph with one
// Burst direct call; every source then copies its own slice (SourceSlice). Callbacks are serial on the one mixer
// thread (spec 04 E4) and share dspTime per block, so "first caller renders" needs no lock.
// Teardown: the audio thread enters the host (Enter / Exit, two interlocked counters, no lock) for every touch of the
// arena; Dispose marks the host and waits for the audio thread to leave before freeing, so a callback that raced the
// shutdown never reads freed memory.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Audio.Burst
{
    public sealed unsafe class AudioGraphHost : IDisposable
    {
        static readonly ProfilerMarker s_Render = new ProfilerMarker(ProfilerCategory.Audio, "MusicRoom.AudioGraph.Render");

        /// <summary>Adaptive budget (section 5.4): floor and ceiling in groups, the p99 thresholds as fractions of the target.</summary>
        public const int BudgetFloor = 400, BudgetCeiling = 2048;
        public const float LowerAbove = 0.6f, RaiseBelow = 0.3f;

        public readonly GraphConfig Config;
        public GraphState* State { get; private set; }
        public DspLoadMeter Load { get; } = new DspLoadMeter();
        /// <summary>Adapt the LOD budget to the measured load once a second (Tick). On in the app; tests switch it off.</summary>
        public bool AdaptiveBudget = true;
        /// <summary>The load window Tick read at its last once-a-second step while the graph ran (Seconds counts them): the
        /// unit governor steps on its p99 (UnitGovernor), the same window the LOD budget uses.</summary>
        public DspLoadMeter.Snapshot LastSecond { get; private set; }
        public int Seconds { get; private set; }

        byte* _arena;
        long _arenaBytes, _allocBytes;
        readonly List<IntPtr> _allocs = new List<IntPtr>();
        int _dropped;
        int _busy, _disposing;
        float _budgetClock;
        int _lastLod;
        long _lastBlocks;

        public long ArenaBytes => _arenaBytes;
        /// <summary>Unit state, unit outputs and rings (Alloc), in bytes.</summary>
        public long AllocatedBytes => _allocBytes;
        public int DroppedCommands => Volatile.Read(ref _dropped);
        public bool IsDisposed => State == null;
        public int SampleRate => Config.SampleRate;
        public int Budget => State != null ? Volatile.Read(ref State->Budget) : 0;
        public int ActiveGroups => State != null ? Volatile.Read(ref State->ActiveGroups) : 0;
        public int LodDropped => State != null ? Volatile.Read(ref State->LodDropped) : 0;
        public long BlocksRendered => State != null ? Interlocked.Read(ref State->Blocks) : 0;

        public AudioGraphHost(GraphConfig config)
        {
            Config = config;
            _arenaBytes = GraphArena.Bytes(config);
            _arena = (byte*)UnsafeUtility.Malloc(_arenaBytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(_arena, _arenaBytes);
            var state = (GraphState*)UnsafeUtility.Malloc(sizeof(GraphState), 64, Allocator.Persistent);
            GraphArena.Layout(state, _arena, config);
            State = state;
            WarmUp();
        }

        /// <summary>Resolve every Burst direct-call pointer the audio thread uses on the MAIN thread (the first call of a
        /// direct-call method looks the function pointer up and, in the editor, compiles it synchronously; never let that
        /// happen on the mixer thread). frames = 0: no work.</summary>
        void WarmUp()
        {
            float* f = State->Exc; float4* a = State->Acc;
            DspKernels.ModalFree(State->Groups, 0, a, 0, 0);
            DspKernels.ModalDriven(State->Groups, 0, f, a, 0, 0);
            DspKernels.AccResolve(a, f, 0, 0);
            DspKernels.MixRamp(f, f, 0, 0, 1f, 0f);
            DspKernels.MonoToInterleaved(f, f, 0, 2);
            DspKernels.PeakAbs(f, 0);
            AudioGraph.RenderBlock(State, long.MinValue + 1, 0);
        }

        // ---------------------------------------------------------------- main thread: memory, banks, presets, units

        /// <summary>Main thread: zeroed, 64-byte aligned unmanaged memory owned by the host (freed by Dispose).</summary>
        public void* Alloc(long bytes, int align = 64)
        {
            bytes = Math.Max(1, bytes);
            void* p = UnsafeUtility.Malloc(bytes, Math.Max(16, align), Allocator.Persistent);
            UnsafeUtility.MemClear(p, bytes);
            _allocs.Add((IntPtr)p);
            _allocBytes += bytes;
            return p;
        }

        /// <summary>Main thread. See AudioGraph.AddBank (arrays of one length = the mode count; null = defaults).</summary>
        public int AddBank(int source, float[] hz, float[] tau, float[] go, float[] gi, float[] strikeA, float[] strikeB, float[] velExp, float[] phase, float[] weight)
        {
            fixed (float* ph = hz, pt = tau, pgo = go, pgi = gi, pa = strikeA, pb = strikeB, pe = velExp, pp = phase, pw = weight)
                return AudioGraph.AddBank(State, source, hz.Length, ph, pt, pgo, pgi, pa, pb, pe, pp, pw);
        }

        /// <summary>Main thread: a one-shot recipe (AudioGraph.OneShotPreset); returns its id for PlayOneShot, -1 when full.</summary>
        public int AddOneShotPreset(in OneShotPreset p)
        {
            OneShotPreset copy = p;
            return AudioGraph.AddOneShotPreset(State, &copy);
        }

        /// <summary>Main thread: claim a role's slot (SoundUnit.Register calls this). outputs: the source of each channel.</summary>
        public UnitSlot* RegisterUnit(UnitId role, UnitId kernel, SourceId[] outputs, void* state, int ringCapacity, int pendingCapacity,
            bool idleSkip = false)
        {
            if (role >= UnitId.Count) throw new ArgumentOutOfRangeException(nameof(role));
            UnitSlot* u = State->Units + (int)role;
            if (u->Registered != 0) throw new InvalidOperationException($"unit {role} is registered already");
            int channels = outputs?.Length ?? 0;
            if (channels > UnitSlot.MaxChannels) throw new ArgumentException($"at most {UnitSlot.MaxChannels} channels", nameof(outputs));
            int cap = Math.Max(16, Mathf.NextPowerOfTwo(ringCapacity));
            u->Id = role;
            u->Kernel = kernel;
            u->State = state;
            u->Channels = channels;
            for (int c = 0; c < channels; c++) u->Source[c] = (int)outputs[c];
            u->MaxFrames = Config.MaxFrames;
            u->SampleRate = Config.SampleRate;
            // (No "cond ? pointer : null" in managed code: IL2CPP 6000.3 merged such a ternary as int32 and truncated the
            // pointer on ARM64; measured in the first APK build. Plain if / else instead.)
            u->Out = null;
            if (channels > 0) u->Out = (float*)Alloc(4L * channels * Config.MaxFrames);
            u->Ring.Slots = (GraphCommand*)Alloc((long)cap * sizeof(GraphCommand));
            u->Ring.Mask = cap - 1;
            u->Ring.Head = (int*)Alloc(64);
            u->Ring.Tail = (int*)Alloc(64);
            u->Ring.Staged = 0;
            u->PendingCapacity = Math.Max(16, pendingCapacity);
            u->Pending = (UnitEvent*)Alloc((long)u->PendingCapacity * sizeof(UnitEvent));
            u->Due = (UnitEvent*)Alloc((long)u->PendingCapacity * sizeof(UnitEvent));
            u->Gain = 1f; u->Target = 1f; u->Step = 0f; u->GainStart = 1f; u->GainEnd = 1f;
            u->Graph = State;
            u->Share = 1f;
            u->Class = UnitClass.AtSeat;
            u->IdleSkip = (byte)(idleSkip ? 1 : 0);
            Volatile.Write(ref u->Registered, (byte)1);   // publish last
            return u;
        }

        public UnitSlot* Unit(UnitId id)
        {
            if (State == null || id >= UnitId.Count) return null;
            return State->Units + (int)id;
        }
        public bool IsRegistered(UnitId id) { var u = Unit(id); return u != null && Volatile.Read(ref u->Registered) != 0; }

        /// <summary>Main thread: fade a unit out over samples (it then suspends: no render, events dropped, reset on resume)
        /// or resume it (at once when suspended, else ramping back up). Applied at the start of the next block.</summary>
        public void FadeUnit(UnitId id, bool on, int samples) =>
            Post(new GraphCommand { Op = GraphOp.UnitFade, Target = (int)id, A = on ? 1f : 0f, N = Math.Max(1, samples) });

        /// <summary>The unit is suspended (faded out completely). Main thread read of an audio-thread flag.</summary>
        public bool IsSuspended(UnitId id) { var u = Unit(id); return u != null && Volatile.Read(ref u->Suspended) != 0; }

        // ---------------------------------------------------------------- main thread: graph commands

        /// <summary>Main thread: stage a graph command (published by Commit, so every command of one hit lands in one block).</summary>
        public bool Stage(in GraphCommand c)
        {
            // AudioSchedule (ENGINE-API.md section 10): a bank strike or one-shot staged inside a "schedule at" scope plays at
            // the scope's time when it was ASAP or relative.
            GraphCommand s = c;
            if (AudioSchedule.Active) s.When = AudioSchedule.Stamp(s.When);
            if (State != null && State->Ring.TryStage(s)) return true;
            Interlocked.Increment(ref _dropped);
            return false;
        }

        public void Commit() { if (State != null) State->Ring.Commit(); }

        public bool Post(in GraphCommand c) { bool ok = Stage(c); Commit(); return ok; }

        public void SetBudget(int groups) => Post(new GraphCommand { Op = GraphOp.SetBudget, N = groups });

        /// <summary>Main thread: start a pooled one-shot (preset, gain, the source it plays on, a voice id for Release).</summary>
        public bool PlayOneShot(int preset, float gain, SourceId source, int voiceId = 0, double when = 0) =>
            Post(new GraphCommand { Op = GraphOp.OneShot, Target = preset, A = gain, N = (int)source, Id = voiceId, When = when });

        public bool ReleaseOneShot(int voiceId, float tau, double when = 0) =>
            Post(new GraphCommand { Op = GraphOp.Release, Target = voiceId, A = tau, When = when });

        // ---------------------------------------------------------------- audio thread

        /// <summary>Audio thread: enter before touching the arena; false when the host is being disposed.</summary>
        public bool Enter()
        {
            Interlocked.Increment(ref _busy);
            if (Volatile.Read(ref _disposing) != 0) { Interlocked.Decrement(ref _busy); return false; }
            return true;
        }

        public void Exit() => Interlocked.Decrement(ref _busy);

        /// <summary>Audio thread (inside Enter / Exit). Renders the block once; later callers of the same block return at once.</summary>
        public void EnsureRendered(long blockStart, int frames)
        {
            if (State->LastBlock == blockStart) return;
            long t0 = Stopwatch.GetTimestamp();
            s_Render.Begin();
            AudioGraph.RenderBlock(State, blockStart, frames);
            s_Render.End();
            Load.Record(Stopwatch.GetTimestamp() - t0, frames, State->SampleRate, blockStart);
        }

        /// <summary>Audio thread: the mono block of one source (valid after EnsureRendered for this block).</summary>
        public float* SourceSlice(int source) => State->Out + source * State->MaxFrames;

        /// <summary>Audio thread: something was mixed into this source's slice this block.</summary>
        public bool SourceUsed(int source) => (uint)source < (uint)State->SourceCount && State->SourceUsed[source] != 0;

        /// <summary>Main thread, tests and tools: render one block now (no audio device; the same call the mixer makes).</summary>
        public void RenderOffline(long blockStart, int frames)
        {
            if (!Enter()) return;
            EnsureRendered(blockStart, frames);
            Exit();
        }

        // ---------------------------------------------------------------- main thread: adaptive budget, stats

        /// <summary>Main thread, every frame: once a second, adapt the LOD budget to the measured p99 (section 5.4): above 60 %
        /// of the 2 ms target, budget x 0.9; below 30 % with LOD drops since the last check, x 1.05; floor 400, ceiling 2048.
        /// Below 30 % without drops a lowered budget also climbs x 1.05, back to the configured one (Config.Budget) and no
        /// further: the units scale their own caps by it (the piano's modes, the harp's active strings) and drop no graph bank,
        /// so without this one heavy second would leave them degraded for the rest of the session.</summary>
        public void Tick(float dt)
        {
            if (State == null) return;
            _budgetClock += dt;
            if (_budgetClock < 1f) return;
            _budgetClock = 0f;
            var s = Load.Read();
            int lod = LodDropped;
            int drops = lod - _lastLod;
            _lastLod = lod;
            bool running = s.Blocks != _lastBlocks;
            _lastBlocks = s.Blocks;
            if (running)
            {
                // The window this second's decisions use (the unit governor steps on the same p99, UnitGovernor).
                LastSecond = s;
                Seconds++;
            }
            if (!AdaptiveBudget || !running) return;
            int budget = Budget, next = AdaptBudget(budget, s.P99Micros, Load.BudgetMicros, drops, Config.Budget);
            if (next != budget) SetBudget(next);
            Load.ResetPeaks();
        }

        /// <summary>The adaptive rule alone (tests). restore: the budget a quiet second climbs back to without LOD drops (the
        /// configured one; 0 = only drops raise it, the section 5.4 rule alone).</summary>
        public static int AdaptBudget(int budget, float p99Micros, float targetMicros, int lodDrops, int restore = 0)
        {
            int next = budget;
            if (p99Micros > LowerAbove * targetMicros) next = (int)Math.Round(budget * 0.9);
            else if (p99Micros < RaiseBelow * targetMicros && (lodDrops > 0 || budget < restore))
            {
                next = Math.Max(budget + 1, (int)Math.Round(budget * 1.05));
                if (lodDrops == 0) next = Math.Min(next, restore);   // the units' own caps (no bank dropped): back to the default only
            }
            return Math.Max(BudgetFloor, Math.Min(BudgetCeiling, next));
        }

        public void Dispose()
        {
            if (State == null) return;
            Interlocked.Exchange(ref _disposing, 1);
            // Wait for an audio callback that entered before the flag (a render takes well under a millisecond).
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref _busy) != 0 && sw.ElapsedMilliseconds < 200) Thread.Sleep(0);
            if (Volatile.Read(ref _busy) != 0)
            {
                Debug.LogWarning("[Audio] graph host still in use by the audio thread after 200 ms: its memory is leaked, not freed");
                return;
            }
            var st = State;
            State = null;
            foreach (var p in _allocs) UnsafeUtility.Free((void*)p, Allocator.Persistent);
            _allocs.Clear();
            if (_arena != null) { UnsafeUtility.Free(_arena, Allocator.Persistent); _arena = null; }
            UnsafeUtility.Free(st, Allocator.Persistent);
        }
    }
}
