// The marimba role's sound unit (spec 6.2; platform.md 10, BUILD-GUIDE 4.3): the main-thread core (MarimbaSound: the
// contact solve, the damp poles, the record slots) posting on the unit's own ring, and the Burst render (MarimbaDsp.Render)
// inside AudioGraph.RenderBlock. Unit-only role: no classic synth, Classic sound never suspends it, ActiveUnit(UnitId.Marimba)
// always returns it. The design is compiled in (MarimbaTables.g.cs), so the unit is Ready at construction: no worker, no
// finite-element design at boot. Idle skip: once every bar has retired and no record plays, the unit outputs nothing and the
// graph stops calling it; a resumed Render needs nothing (the state is at rest). Share and Work: MarimbaDsp.Render.
namespace MusicRoom.Audio.Burst
{
    public static unsafe partial class UnitHooks
    {
        static partial void RenderMarimba(UnitSlot* u) => MarimbaDsp.Render(u);
    }

    public static partial class SoundUnits
    {
        static partial void CreateMarimba(SoundContext ctx, ref SoundUnit unit) => unit = new MarimbaUnit(ctx);
    }

    public sealed unsafe class MarimbaUnit : SoundUnit, IMarimbaSound, IMarimbaCommandSink, ILoudnessReference, IUnitWorstCase
    {
        readonly MarimbaState* _s;
        readonly MarimbaSound _core;

        public MarimbaUnit(SoundContext ctx) : base(ctx)
        {
            byte* mem = (byte*)ctx.Host.Alloc(MarimbaStateBuilder.Bytes());   // zeroed, 64-byte aligned, freed with the host
            _s = MarimbaStateBuilder.Build(mem);                               // lane tables in, Ready = 1
            _core = new MarimbaSound(_s, this, ctx.Seed);
            Register(ctx, UnitId.Marimba, new[] { SourceId.MarimbaLo, SourceId.MarimbaMid, SourceId.MarimbaHi }, _s,
                ringCapacity: 256, pendingCapacity: 256, idleSkip: true);
        }

        public MarimbaState* State => _s;
        public MarimbaSound Core => _core;
        bool IMarimbaCommandSink.Post(in GraphCommand c) => Post(c);

        public void Strike(int bar, float vel, float u, float c, MarimbaMallet mallet, double when = 0)
        {
            RecordUnit(MarimbaConst.OpStrike, bar, vel, when);
            _core.Strike(bar, vel, u, c, mallet, when);
        }

        public void Damp(int bar, MarimbaDampKind kind, float u, float c, double when = 0)
        {
            RecordUnit(MarimbaConst.OpDamp, bar, (float)kind, when);
            _core.Damp(bar, kind, u, c, when);
        }

        public void Undamp(int bar, double when = 0)
        {
            RecordUnit(MarimbaConst.OpUndamp, bar, 0f, when);
            _core.Undamp(bar, when);
        }

        public void Silence()
        {
            RecordUnit(MarimbaConst.OpSilence, -1, 0f, 0);
            _core.Silence();
        }

        public float BarLevel(int bar) => _core.BarLevel(bar);

        /// <summary>The declared worst for the platform's load tests (IUnitWorstCase; MarimbaLoadTests' seated worst): two
        /// mallets, 15 strokes a second each (one stroke every 6 blocks at 48 kHz), walking over all 37 bars, medium yarn,
        /// vel 0.9.</summary>
        public void DriveWorst(long block, double when)
        {
            if (block % 6 != 3) return;
            Strike((int)(block / 6 % MarimbaConst.Bars), 0.9f, 0.45f, 0f, MarimbaMallet.Medium, when);
        }
        /// <summary>Review item 13, opt-in (off ships): the octave-boundary crossfade.</summary>
        public bool BoundaryCrossfade { get => _core.BoundaryCrossfade; set => _core.BoundaryCrossfade = value; }

        // ---- ILoudnessReference: the mf reference phrase, Ode to Joy bar 1 (E4 E4 F4 G4 at 108 BPM), vel 0.55, u 0.45,
        // the yarn mallet (spec 3.1: -24.14 LUFS dry, the piano -24.01)
        static readonly int[] RefBars = { 16, 16, 17, 19 };
        public double ReferenceSeconds => 0.05 + 4 * 60.0 / 108 + 1.0;
        public void PlayReference(double start)
        {
            double spb = 60.0 / 108;
            for (int k = 0; k < RefBars.Length; k++) Strike(RefBars[k], 0.55f, 0.45f, 0f, MarimbaMallet.Medium, start + 0.05 + k * spb);
        }

        public override bool Ready => System.Threading.Volatile.Read(ref _s->Ready) != 0;
        public override void Seed(uint seed) => _core.Seed(seed);   // the knock noise lives on the main thread only
        public override string Stats => _core.Stats + $" dropped {DroppedCommands}";
    }
}
