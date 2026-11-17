// The hand drums' main-thread side: IHandDrumSound turned into the unit's GraphCommands (handdrums.md 2.12 command
// table), over any command sink: the SoundUnit's own ring in the app (HandDrumsUnit), HandDrumsOffline in tests. The
// payload carries the stroke value u and the points exactly; the canonical strokes and rests (playback, lessons,
// demos) are expanded on the audio thread from the reference's double constants (ops 5 and 6, u and theta packed as
// doubles), so a canonical stroke renders bit for bit like the reference's playStroke. Nothing here allocates.
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    /// <summary>Where the commands go: SoundUnit.Stage / Commit (HandDrumsUnit) or the offline host.</summary>
    public interface IHandDrumsCommandSink
    {
        bool StageCommand(in GraphCommand c);
        void CommitCommands();
    }

    public sealed unsafe class HandDrumsSound : IHandDrumSound
    {
        /// <summary>playStroke's mute: the rest 12 ms after the stroke, lifted 0.25 s later (handdrums.md 2.10).</summary>
        public const double MuteAfter = 0.012, MuteFor = 0.25;
        /// <summary>The bass's trailing pads and the slap's tips land this long after the stroke's first patch.</summary>
        public const double BassPadsDelay = 0.004, SlapTipsDelay = 0.0008;

        readonly IHandDrumsCommandSink _sink;
        readonly HandDrumsState* _s;

        public HandDrumsSound(IHandDrumsCommandSink sink, HandDrumsState* state) { _sink = sink; _s = state; }

        public HandDrumsState* State => _s;
        public int Strokes => _s != null ? Volatile.Read(ref _s->Strokes) : 0;
        public int PatchesStolen => _s != null ? Volatile.Read(ref _s->PatchesStolen) : 0;
        public int Work => _s != null ? Volatile.Read(ref _s->Work) : 0;

        /// <summary>The time of a patch that lands delay after a stroke at when (absolute: later; ASAP / relative: further).</summary>
        public static double After(double when, double delay) => delay == 0 ? when : when > 0 ? when + delay : when - delay;

        /// <summary>How many canonical patches a stroke kind has, and the delay of patch idx.</summary>
        public static int PatchCount(HandStroke k) => k == HandStroke.Bass || k == HandStroke.Slap || k == HandStroke.SlapClosed ? 2 : 1;
        public static double PatchDelay(HandStroke k, int idx) => idx == 0 ? 0 : k == HandStroke.Bass ? BassPadsDelay : SlapTipsDelay;

        public void Patch(int drum, int hand, HandPart part, float u, PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count, double when, float delay = 0f)
        {
            if ((uint)drum > 1) return;
            int cnt = shape == PatchShape.Line ? 2 : (count < 1 ? 1 : count > 3 ? 3 : count);
            var c = new GraphCommand
            {
                Op = (GraphOp)HandDrumsConst.OpPatch, Flags = CommandFlags.LateDrop, Target = drum, When = After(when, delay),
                N = (hand & 1) | ((int)part & 15) << 4 | ((int)shape & 15) << 8 | (cnt & 15) << 12,
                A = u < 0f ? 0f : u > 1f ? 1f : u,
                B = p0.Rho, C = p0.Theta, D = p1.Rho, E = p1.Theta, F = p2.Rho, G = p2.Theta,
            };
            _sink.StageCommand(c);
            _sink.CommitCommands();
        }

        public void Stroke(int drum, int hand, HandStroke kind, double u, double thetaOffset, double when, float rhoOffset = 0f)
        {
            if ((uint)drum > 1) return;
            int n = PatchCount(kind);
            for (int idx = 0; idx < n; idx++)
            {
                var c = new GraphCommand
                {
                    Op = (GraphOp)HandDrumsConst.OpStrokePatch, Flags = CommandFlags.LateDrop, Target = drum,
                    When = After(when, PatchDelay(kind, idx)), N = (hand & 1) | ((int)kind & 15) << 4 | (idx & 15) << 8,
                    E = rhoOffset,
                };
                double* dp = (double*)&c.A;   // A:B the stroke's u, C:D the theta offset (8-byte aligned in GraphCommand)
                dp[0] = u;
                dp[1] = thetaOffset;
                _sink.StageCommand(c);
            }
            if (kind == HandStroke.Mute || kind == HandStroke.SlapClosed)
            {
                double r0 = After(when, MuteAfter);
                StageRestCanon(drum, hand, kind == HandStroke.Mute ? HandRest.Mute : HandRest.SlapClosed, thetaOffset, r0);
                StageLift(drum, hand, After(r0, MuteFor));
            }
            _sink.CommitCommands();
        }

        public void Rest(int drum, int hand, RestPoint p0, RestPoint p1, RestPoint p2, int count, double when)
        {
            if ((uint)drum > 1) return;
            int np = count < 1 ? 1 : count > 3 ? 3 : count;
            var c = new GraphCommand
            {
                Op = (GraphOp)HandDrumsConst.OpRest, Target = drum, When = when,
                N = (hand & 1) | np << 4 | ((int)p0.Part & 15) << 8 | ((int)p1.Part & 15) << 12 | ((int)p2.Part & 15) << 16,
                A = p0.Rho, B = p0.Theta, C = p1.Rho, D = p1.Theta, E = p2.Rho, F = p2.Theta,
            };
            _sink.StageCommand(c);
            _sink.CommitCommands();
        }

        public void RestCanonical(int drum, int hand, HandRest kind, double thetaOffset, double when)
        {
            if ((uint)drum > 1) return;
            StageRestCanon(drum, hand, kind, thetaOffset, when);
            _sink.CommitCommands();
        }

        public void Lift(int drum, int hand, double when)
        {
            if ((uint)drum > 1) return;
            StageLift(drum, hand, when);
            _sink.CommitCommands();
        }

        /// <summary>Both drums silent, both hands lifted (op 4); reseed: the drums' RNGs restart from this session seed.</summary>
        public void Reset(double when, bool reseed = false, uint seed = 1)
        {
            _sink.StageCommand(new GraphCommand { Op = (GraphOp)HandDrumsConst.OpReset, When = when, N = reseed ? 1 : 0, Seed = seed });
            _sink.CommitCommands();
        }

        public float Level(int drum)
        {
            if (_s == null) return 0f;
            return drum == 0 ? Volatile.Read(ref _s->Level0) : Volatile.Read(ref _s->Level1);
        }

        /// <summary>Tests on the standalone host: the unit's own LOD share (multiplied with UnitSlot.Share, which the governor
        /// writes in the app).</summary>
        public void SetShare(float share) { if (_s != null) Volatile.Write(ref _s->Share, share); }

        void StageRestCanon(int drum, int hand, HandRest kind, double thetaOffset, double when)
        {
            var c = new GraphCommand { Op = (GraphOp)HandDrumsConst.OpRestCanon, Target = drum, When = when, N = (hand & 1) | ((int)kind & 15) << 4 };
            ((double*)&c.C)[0] = thetaOffset;
            _sink.StageCommand(c);
        }

        void StageLift(int drum, int hand, double when) =>
            _sink.StageCommand(new GraphCommand { Op = (GraphOp)HandDrumsConst.OpLift, Target = drum, When = when, N = hand & 1 });
    }

    /// <summary>The unit (SoundUnits.CreateHandDrums, HandDrumsHooks.cs): role UnitId.HandDrums, outputs HandDrumLo (channel
    /// 0, the tumba) and HandDrumHi (channel 1, the conga), idle skip on (silent, the drums are at rest: the control grid is
    /// absolute, so a Resumed block needs nothing). Every public method records itself on the test tap (platform 10.1).
    /// The tables (0.16 MB) are parsed and the state built in the constructor, about 1 ms on the main thread.</summary>
    public sealed unsafe class HandDrumsUnit : SoundUnit, IHandDrumSound, IHandDrumsCommandSink, ILoudnessReference, IUnitWorstCase
    {
        public static readonly SourceId[] Outputs2 = { SourceId.HandDrumLo, SourceId.HandDrumHi };
        readonly HandDrumsState* _s;
        readonly HandDrumsSound _sound;
        public long StateBytes { get; }

        public HandDrumsUnit(SoundContext ctx, byte[] tables = null) : base(ctx)
        {
            var t = HandDrumsTables.Parse(tables ?? HandDrumsTables.LoadDefaultBytes());
            long before = ctx.Host.AllocatedBytes;
            _s = HandDrumsBuilder.Build(t, bytes => (System.IntPtr)ctx.Host.Alloc(bytes), ctx.Seed == 0 ? 1u : ctx.Seed, ctx.SampleRate);
            StateBytes = ctx.Host.AllocatedBytes - before;
            _sound = new HandDrumsSound(this, _s);
            Register(ctx, UnitId.HandDrums, Outputs2, _s, ringCapacity: 256, pendingCapacity: 256, idleSkip: true);
        }

        public HandDrumsState* State => _s;
        public HandDrumsSound Sound => _sound;

        bool IHandDrumsCommandSink.StageCommand(in GraphCommand c) => Stage(c);
        void IHandDrumsCommandSink.CommitCommands() => Commit();

        public void Patch(int drum, int hand, HandPart part, float u, PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count, double when, float delay = 0f)
        {
            RecordUnit(HandDrumsConst.OpPatch, drum, u, when);
            _sound.Patch(drum, hand, part, u, shape, p0, p1, p2, count, when, delay);
        }

        public void Stroke(int drum, int hand, HandStroke kind, double u, double thetaOffset, double when, float rhoOffset = 0f)
        {
            RecordUnit(HandDrumsConst.OpStrokePatch, drum, (float)u, when);
            _sound.Stroke(drum, hand, kind, u, thetaOffset, when, rhoOffset);
        }

        public void Rest(int drum, int hand, RestPoint p0, RestPoint p1, RestPoint p2, int count, double when)
        {
            RecordUnit(HandDrumsConst.OpRest, drum, 0f, when);
            _sound.Rest(drum, hand, p0, p1, p2, count, when);
        }

        public void RestCanonical(int drum, int hand, HandRest kind, double thetaOffset, double when)
        {
            RecordUnit(HandDrumsConst.OpRestCanon, drum, 0f, when);
            _sound.RestCanonical(drum, hand, kind, thetaOffset, when);
        }

        public void Lift(int drum, int hand, double when)
        {
            RecordUnit(HandDrumsConst.OpLift, drum, 0f, when);
            _sound.Lift(drum, hand, when);
        }

        public float Level(int drum) => _sound.Level(drum);

        /// <summary>IAudioEngine.SeedAll: the drums' per-stroke variation restarts from the session seed.</summary>
        public override void Seed(uint seed) => _sound.Reset(0, true, seed == 0 ? 1u : seed);

        // ---- ILoudnessReference (platform 10.3): the mf phrase, open, open, slap, open on the conga at u 0.55, 108 bpm, the
        // piano reference's tempo and onsets (0.2 s + i x 60 / 108), alternating hands; 4 s. Phase A measured it 0.00 LU
        // from the piano's mf reference through the real piano unit (HandDrumsBehaviourTests).
        public const double ReferenceU = 0.55, ReferenceBpm = 108;
        public static readonly HandStroke[] ReferencePhrase = { HandStroke.Open, HandStroke.Open, HandStroke.Slap, HandStroke.Open };
        public double ReferenceSeconds => 4.0;

        public void PlayReference(double start)
        {
            double spb = 60.0 / ReferenceBpm;
            for (int i = 0; i < ReferencePhrase.Length; i++)
                Stroke(0, i % 2, ReferencePhrase[i], ReferenceU, 0, start + 0.2 + i * spb);
        }

        // ---- IUnitWorstCase (the descriptor's CapWorstMs): the live maximum, the most the stroke logic can produce
        // (HandDrumsLoadTests "live max"): every hand on every drum at its 40 ms window, full slaps, the four hands 10 ms apart.
        // The inhuman adversarial case (every 12 ms) is beyond any player and stays in HandDrumsLoadTests.
        public const double WorstEveryS = 0.040, WorstSpreadS = 0.010;

        public void DriveWorst(long block, double when)
        {
            int every = System.Math.Max(1, (int)System.Math.Round(WorstEveryS * Context.SampleRate / HandDrumsConst.MaxFrames));
            if (block % every != 0) return;
            for (int d = 0; d < 2; d++)
                for (int h = 0; h < 2; h++)
                {
                    double dt = WorstSpreadS * (2 * d + h);
                    Stroke(d, h, HandStroke.Slap, 1.0, 0, when > 0 ? when + dt : dt > 0 ? -dt : 0);
                }
        }

        public override string Stats =>
            $"handdrums strokes {_sound.Strokes} stolen {_sound.PatchesStolen} work {_sound.Work} share {Share:F2} state {StateBytes / 1024} KiB";
    }
}
