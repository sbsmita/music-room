// The main-thread side of a sound unit (ENGINE-API.md). A unit object owns its instrument's recipe on the main thread:
// it builds its tables (possibly on a worker), allocates its unmanaged state through the host, turns IAudioEngine calls
// into GraphCommands on its own ring, and reads its stats back. The audio-thread side is the unit's UnitHooks partial
// (UnitSlot.cs). AudioEngine creates the unit-only roles' units through SoundUnits (one partial factory per role,
// SoundUnits.LiveRoles); the piano, drums, harp and room play today's synths and Meta's room (the classic sound, the only
// one since 2026-11-10; their upgraded units were deleted on 2026-11-11).
using System;
using System.Threading;
using UnityEngine;

namespace MusicRoom.Audio.Burst
{
    /// <summary>What a unit gets when it is created (main thread).</summary>
    public sealed class SoundContext
    {
        readonly SynthSource[] _sources;

        public SoundContext(AudioGraphHost host, SynthSource[] sources, int sampleRate, uint seed, Func<double> clock, bool offline)
        {
            Host = host; _sources = sources; SampleRate = sampleRate; Seed = seed; Clock = clock ?? (() => AudioSettings.dspTime); Offline = offline;
        }

        public AudioGraphHost Host { get; }
        public int SampleRate { get; }
        /// <summary>The session seed (AudioEngine.Seed); derive the unit's own streams from it.</summary>
        public uint Seed { get; }
        /// <summary>The main-thread audio clock (IAudioEngine.Now): AudioSettings.dspTime, or BootOptions.AudioClock in tests.</summary>
        public Func<double> Clock { get; }
        /// <summary>True in the EditMode harness (OfflineEngine): no audio device; the test renders the blocks itself.</summary>
        public bool Offline { get; }
        /// <summary>The SynthSource a unit plays on (VoiceHandle.Source; null for an id the context has no source for).</summary>
        public SynthSource Source(SourceId id) => _sources != null && (int)id < _sources.Length ? _sources[(int)id] : null;

        /// <summary>The engine's test tap (AudioEngine.TestTap), null outside tests: SoundUnit.RecordUnit writes to it.</summary>
        public Func<AudioTestTap> TapSource { get; set; }
        public AudioTestTap TestTap => TapSource?.Invoke();

        /// <summary>Runs boot work (tables, physics) on a background thread with the render workers' priority rules (lowered on
        /// Android); poll IsDone. The job must not call Unity APIs.</summary>
        public OfflineRenderQueue StartWorker(Action work)
        {
            var q = new OfflineRenderQueue(new SampleBank(1), 1);
            q.Add(-1, _ => { work(); return null; });
            q.Start(1);
            return q;
        }
    }

    /// <summary>Base class of a unit's main-thread object. A subclass calls Register once from its factory, then posts commands
    /// with Stage / Commit. Everything here is main-thread only.</summary>
    public abstract unsafe class SoundUnit : IDisposable
    {
        int _dropped;

        protected SoundUnit() { }

        /// <summary>Keeps the context at once, so Alloc works before Register (allocate the state, then register it).</summary>
        protected SoundUnit(SoundContext ctx) { Context = ctx; }

        public UnitId Id { get; private set; }
        public SoundContext Context { get; private set; }
        public AudioGraphHost Host => Context?.Host;
        /// <summary>The unit's slot in the graph (valid after Register, until the host is disposed).</summary>
        public UnitSlot* Slot { get; private set; }
        /// <summary>The sources the unit's channels play on (channel c -> Outputs[c]).</summary>
        public SourceId[] Outputs { get; private set; } = Array.Empty<SourceId>();
        /// <summary>Commands lost to a full ring (main thread count).</summary>
        public int DroppedCommands => Volatile.Read(ref _dropped);

        /// <summary>Claims the role's slot: output channels (one per source), a command ring and a pending queue, the unit's
        /// state pointer (allocate it first with Host.Alloc). kernel: the hook that renders it (default: the role's own).
        /// idleSkip (platform.md 10.2): the graph skips the unit's Render while it has nothing due or pending, no fade runs
        /// and it has output nothing for AudioGraph.IdleSkipBlocks blocks (UnitSlot.IdleBlocks counts them); its first
        /// Render after a skip gets UnitFlags.Resumed. For units whose silent state is at rest (no clocks inside).</summary>
        protected void Register(SoundContext ctx, UnitId role, SourceId[] outputs, void* state, int ringCapacity = 256,
            int pendingCapacity = 256, UnitId kernel = UnitId.Count, bool idleSkip = false)
        {
            if (Slot != null) throw new InvalidOperationException("Register once");
            Context = ctx;
            Id = role;
            Outputs = outputs ?? Array.Empty<SourceId>();
            Slot = ctx.Host.RegisterUnit(role, kernel == UnitId.Count ? role : kernel, Outputs, state, ringCapacity, pendingCapacity, idleSkip);
        }

        /// <summary>Registered with idle skip.</summary>
        public bool IdleSkip => Slot != null && Slot->IdleSkip != 0;
        /// <summary>Blocks the graph skipped while the unit was idle (audio thread count).</summary>
        public int IdleBlocks => Slot != null ? Volatile.Read(ref Slot->IdleBlocks) : 0;
        /// <summary>The unit's own work count for the last block (UnitSlot.Work; 0 when skipped).</summary>
        public int Work => Slot != null ? Volatile.Read(ref Slot->Work) : 0;
        /// <summary>The output channels that carried signal in the last rendered block (bits).</summary>
        public int ChannelsUsed => Slot != null ? Volatile.Read(ref Slot->ChannelsUsed) : 0;
        /// <summary>The governor's share (UnitSlot.Share), main -> audio.</summary>
        public float Share
        {
            get => Slot != null ? Volatile.Read(ref Slot->Share) : 1f;
            set { if (Slot != null) Volatile.Write(ref Slot->Share, Math.Max(0f, Math.Min(1f, value))); }
        }

        /// <summary>Host-owned, zeroed, 64-byte aligned unmanaged memory for count T (freed with the host).</summary>
        protected T* Alloc<T>(int count = 1) where T : unmanaged
        {
            if (Context == null) throw new InvalidOperationException("pass the SoundContext to the base constructor (or Register first)");
            return (T*)Context.Host.Alloc((long)sizeof(T) * Math.Max(1, count));
        }

        /// <summary>Queue a command for this unit's next blocks (published by Commit: one hit = one batch = one block).</summary>
        protected bool Stage(in GraphCommand c)
        {
            // AudioSchedule (ENGINE-API.md section 10): inside a "schedule at" scope an instrument unit's ASAP / relative
            // command plays at the scope's time.
            if (AudioSchedule.Active)
            {
                GraphCommand s = c;
                s.When = AudioSchedule.Stamp(s.When);
                if (Slot != null && Slot->Ring.TryStage(s)) return true;
                Interlocked.Increment(ref _dropped);
                return false;
            }
            if (Slot != null && Slot->Ring.TryStage(c)) return true;
            Interlocked.Increment(ref _dropped);
            return false;
        }

        protected void Commit() { if (Slot != null) Slot->Ring.Commit(); }

        /// <summary>platform.md 10.1: every public method of a unit-only role calls this, so instrument tests assert sounds
        /// (AudioTestTap.Calls, AudioCall.Unit). No-op outside tests.</summary>
        protected void RecordUnit(byte op, int index, float vel, double when)
        {
            AudioTestTap tap = Context?.TestTap;
            tap?.Calls.Add(new AudioTestTap.Call { Method = AudioCall.Unit, Unit = Id, Op = op, Index = index, Vel = vel, When = when });
        }

        protected bool Post(in GraphCommand c) { bool ok = Stage(c); Commit(); return ok; }

        /// <summary>True once the unit can play (its boot tables are built). Diagnostics: calls are routed to the unit either way.</summary>
        public virtual bool Ready => true;
        /// <summary>Events the unit dropped because it was not Ready yet (an early press while its tables built; -1: the
        /// unit does not count them). AudioModule logs it once per instrument, when the unit is ready.</summary>
        public virtual int NotReadyDrops => -1;
        /// <summary>Main thread, every frame (AudioEngine.Update): poll workers, publish parameters, post a pose.</summary>
        public virtual void Tick() { }
        /// <summary>IAudioEngine.SeedAll: re-seed the unit's play-time randomness (stage a command if the audio thread owns it).</summary>
        public virtual void Seed(uint seed) { }
        /// <summary>One line of diagnostics for logs and the dev HUD.</summary>
        public virtual string Stats => "";
        /// <summary>Release managed resources (unmanaged memory belongs to the host and is freed with it).</summary>
        public virtual void Dispose() { }
    }

    /// <summary>Unit voice handles: VoiceHandle.Id &lt; 0 marks a unit's handle, so a Release / Damp reaches the unit that made
    /// the voice (and today's positive ids keep reaching today's synths).
    /// Since the platform (platform.md section 2, rule 2) the unit field is 4 bits (16 roles) and the payload 27 bits.</summary>
    public static class UnitHandles
    {
        public const int UnitBits = 4, UnitMask = 15, PayloadMask = 0x07FFFFFF;
        public static VoiceHandle Make(SynthSource source, UnitId unit, int payload) =>
            new VoiceHandle(source, -(1 + (((payload & PayloadMask) << UnitBits) | (int)unit)));
        public static bool IsUnit(in VoiceHandle h) => h.Id < 0;
        public static UnitId Unit(in VoiceHandle h) => (UnitId)((-h.Id - 1) & UnitMask);
        public static int Payload(in VoiceHandle h) => (-h.Id - 1) >> UnitBits;
    }

    /// <summary>A unit-only role's mf reference phrase for loudness parity (platform.md 10.3, LoudnessParityTests): the
    /// phrase its instrument would play at mezzo-forte (the piano's is Ode to Joy bar 1 at vel 0.55), posted from `start`
    /// (absolute seconds on the unit's clock), heard within ReferenceSeconds. Dry, rendered through OfflineEngine.</summary>
    public interface ILoudnessReference
    {
        double ReferenceSeconds { get; }
        void PlayReference(double start);
    }

    /// <summary>The unit factory. Each workstream implements ONE partial method in its own file:
    ///   public static partial class SoundUnits { static partial void CreateMarimba(SoundContext ctx, ref SoundUnit unit) { unit = new MarimbaUnit(ctx); } }
    /// A role without an implementation has no unit. The piano, drums, harp and room have none (UnitId keeps their ids:
    /// tests install the Example unit in them).</summary>
    public static partial class SoundUnits
    {
        // The platform's unit-only roles (platform.md 10.2): each instrument's unit folder implements its own.
        static partial void CreateHandpan(SoundContext ctx, ref SoundUnit unit);
        static partial void CreateHandDrums(SoundContext ctx, ref SoundUnit unit);
        static partial void CreateMarimba(SoundContext ctx, ref SoundUnit unit);
        static partial void CreateBowls(SoundContext ctx, ref SoundUnit unit);

        /// <summary>Tests and tools: when set, it replaces the factory for every role (return null for "no unit"). Reset it to
        /// null afterwards. Read by AudioEngine.Init and OfflineEngine.</summary>
        public static Func<UnitId, SoundContext, SoundUnit> Override;

        /// <summary>Every role that has a unit implementation (offline tools and tests create any of them): the unit-only ones.</summary>
        public static readonly UnitId[] Roles = { UnitId.Handpan, UnitId.HandDrums, UnitId.Marimba, UnitId.Bowls };

        /// <summary>The roles AudioEngine creates, in this order: the unit-only ones. The piano, drums, harp and room always
        /// play the classic sound (the user's choice, 2026-11-10) and have no unit.</summary>
        public static readonly UnitId[] LiveRoles = Array.FindAll(Roles, IsUnitOnly);

        /// <summary>The platform's roles have no classic synth (platform.md 10.1): the only roles with a unit in the app, and
        /// ActiveUnit always returns their unit.</summary>
        public static bool IsUnitOnly(UnitId role) => role >= UnitId.Handpan && role < UnitId.Count;

        /// <summary>The only output rate these roles are built for: the hand drums' and the handpan's tunings, the marimba's
        /// contact model and the bowls' rub encoder all assume 48 kHz (at 24 kHz they played an octave low with doubled decays).</summary>
        public const int UnitRate = 48000;

        /// <summary>A role whose unit plays right only at UnitRate (every unit-only role).</summary>
        public static bool NeedsUnitRate(UnitId role) =>
            role == UnitId.HandDrums || role == UnitId.Handpan || role == UnitId.Marimba || role == UnitId.Bowls;

        public static SoundUnit Create(UnitId role, SoundContext ctx)
        {
            if (Override != null) return Override(role, ctx);
            if (ctx != null && ctx.SampleRate != UnitRate && NeedsUnitRate(role))
            {
                // Refused at any other rate, with one clear line: a unit that plays out of tune is worse than silence.
                Debug.LogError($"[Audio] sound unit {role} is built for {UnitRate} Hz and the output runs at {ctx.SampleRate} Hz: not started " +
                               "(it has no classic synth, so it stays silent)");
                return null;
            }
            SoundUnit u = null;
            switch (role)
            {
                case UnitId.Handpan: CreateHandpan(ctx, ref u); break;
                case UnitId.HandDrums: CreateHandDrums(ctx, ref u); break;
                case UnitId.Marimba: CreateMarimba(ctx, ref u); break;
                case UnitId.Bowls: CreateBowls(ctx, ref u); break;
            }
            return u;
        }
    }
}
