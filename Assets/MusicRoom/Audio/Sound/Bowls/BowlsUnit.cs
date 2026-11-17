// The singing bowls' sound unit (bowls.md 3.4; ENGINE-API 4; BUILD-GUIDE 4.3): its unmanaged state in the host's memory,
// the design built once (about 1 ms), the Strict render compiled as a Burst function pointer so the bowls stay bit-exact
// inside the graph's FloatMode.Fast RenderBlock (review item 20, the integrator's R6), IBowlsSound through BowlsSoundCore
// with this unit's own ring as the sink, every public call recorded on the test tap (RecordUnit), idle skip, Share / Work,
// and the mf loudness reference (4 taps E4 E4 C4 G3 at 0.7 m/s, 108 bpm). Main thread only; every call is allocation-free.
using System;
using System.Threading;
using UnityEngine;

namespace MusicRoom.Audio.Burst
{
    public sealed unsafe class BowlsUnit : SoundUnit, IBowlsSound, IBowlsCommandSink, ILoudnessReference, IUnitWorstCase
    {
        readonly BowlsState* _s;
        readonly BowlsSoundCore _core;

        public BowlsUnit(SoundContext ctx) : base(ctx)
        {
            _s = Alloc<BowlsState>();
            BowlsDesign.Build(_s, BowlsDesign.Bowls(ctx.SampleRate), ctx.SampleRate, ctx.Seed);
            BowlsDesign.SetPad(_s, ctx.SampleRate, true);
            BowlsDesign.SetRubNoise(_s, ctx.SampleRate, true);
            _s->StrictFn = BowlsDsp.CompileStrictPointer();
            _core = new BowlsSoundCore(_s, this, ctx.SampleRate);
            Register(ctx, UnitId.Bowls, new[] { SourceId.Bowls }, _s, ringCapacity: 512, pendingCapacity: 256, idleSkip: true);
        }

        public BowlsState* State => _s;
        public BowlsSoundCore Core => _core;

        void IBowlsCommandSink.Post(in GraphCommand c) => Post(c);

        public override void Tick() => _core.Tick(Context.Clock());
        public override bool Ready => Volatile.Read(ref _s->Ready) != 0;
        public override int NotReadyDrops => Volatile.Read(ref _s->NotReady);
        public override void Seed(uint seed) => _core.SeedAll(seed);
        public override string Stats
        {
            get
            {
                int w = Volatile.Read(ref _s->Work);
                return $"bowls work {w} sing {_core.SingVelocity(0):F2}/{_core.SingVelocity(1):F2}/{_core.SingVelocity(2):F2} m/s lanes {_core.ContactLane(0)}/{_core.ContactLane(1)}/{_core.ContactLane(2)} strict {(_s->StrictFn != IntPtr.Zero ? "fp" : "inline")}";
            }
        }

        // ---- IBowlsSound (each call recorded on the test tap: op, bowl, the main value, when)
        public void Strike(int bowl, BowlStrikeKind kind, float speed, float angleRad, float heightS, double when = 0)
        { RecordUnit(BowlsDsp.OpStrike, bowl, speed, when); _core.Strike(bowl, kind, speed, angleRad, heightS, when); }
        public void Contact(int bowl, BowlSurface s, float newtons, float fingerSpeed, float angleRad, float omega, float heightS, int lane = 0, int rampSamples = 0, double when = 0)
        { RecordUnit(BowlsDsp.OpContact, bowl, newtons, when); _core.Contact(bowl, s, newtons, fingerSpeed, angleRad, omega, heightS, lane, rampSamples, when); }
        public void Release(int bowl, int lane = 0, double when = 0) { RecordUnit(BowlsDsp.OpRelease, bowl, lane, when); _core.Release(bowl, lane, when); }
        public void Damp(int bowl, int level, double when = 0) { RecordUnit(BowlsDsp.OpDamp, bowl, level, when); _core.Damp(bowl, level, when); }
        public void SetListener(int bowl, float angleRad, bool snap = false) => _core.SetListener(bowl, angleRad, snap);
        public void Silence(int bowl) { RecordUnit(BowlsDsp.OpSilence, bowl, 0f, 0); _core.Silence(bowl); }
        public float SingVelocity(int bowl) => _core.SingVelocity(bowl);
        public float Mode3Velocity(int bowl) => _core.Mode3Velocity(bowl);
        public float PumpPower(int bowl) => _core.PumpPower(bowl);
        public float Level(int bowl) => _core.Level(bowl);
        public float ContactLost(int bowl) => _core.ContactLost(bowl);
        public int ContactLane(int bowl) => _core.ContactLane(bowl);
        public Vector4 Pattern20(int bowl) => _core.Pattern20(bowl);
        public bool Awake(int bowl) => _core.Awake(bowl);

        // ---- ILoudnessReference: the bowls' mf phrase (bowls.md 9; -25.13 LUFS dry in the lab, -0.98 LU against -24.15)
        const double Spb = 60.0 / 108.0;
        static readonly int[] s_refBowls = { 2, 2, 1, 0 };
        public double ReferenceSeconds => 0.05 + 4 * Spb + 1.0;
        public void PlayReference(double start)
        {
            for (int b = 0; b < 3; b++) SetListener(b, 0f, true);
            for (int k = 0; k < 4; k++) Strike(s_refBowls[k], BowlStrikeKind.Tap, 0.7f, 0f, 1f, start + 0.05 + k * Spb);
        }

        /// <summary>IUnitWorstCase (the descriptor's CapWorstMs): every bowl rubbed at 4 N, 0.4 m/s on the outer wall, all three
        /// struck again every 0.5 s (the LaneArbiter lets one stick own a bowl, so three contacts is the most there can be).</summary>
        public void DriveWorst(long block, double when)
        {
            double t = block * 256.0 / 48000.0;
            if (block % 94 == 0) for (int b = 0; b < 3; b++) Strike(b, BowlStrikeKind.Tap, 1.2f, 0.4f + b, 1f, when);
            for (int b = 0; b < 3; b++)
            {
                float R = b == 0 ? 0.0875f : b == 1 ? 0.075f : 0.0625f, w = 0.4f / R;   // the rim radii (bowls.md 2.1)
                Contact(b, BowlSurface.Outer, 4f, 0.4f, (float)(w * t + b), w, 0.85f, 0, 0, when);
            }
        }
    }

    public static partial class SoundUnits
    {
        static partial void CreateBowls(SoundContext ctx, ref SoundUnit unit) => unit = new BowlsUnit(ctx);
    }

    public static unsafe partial class UnitHooks
    {
        static partial void RenderBowls(UnitSlot* u) => BowlsDsp.RenderUnit(u, u->Share);
    }
}
