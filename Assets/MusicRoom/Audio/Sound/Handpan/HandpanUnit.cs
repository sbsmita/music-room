// The handpan role's SoundUnit (ENGINE-API.md 4.2; BUILD-GUIDE 4.3): the unmanaged state in the host's memory, the voice
// table the user picked (HandpanVoiceChoice), and HandpanSound staging on the unit's own ring (SoundUnit.Stage applies
// AudioSchedule, so the looper's ScheduleAt reaches every strike and damp). Registered with idle skip; every public method
// records its call on the test tap (RecordUnit); the mf reference phrase for the loudness parity (ILoudnessReference).
// A unit-only role: there is no classic synth, so Classic sound never suspends it.
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    public sealed unsafe class HandpanUnit : SoundUnit, IHandpanSound, IHandpanCommandPort, ILoudnessReference, IUnitWorstCase
    {
        readonly HandpanState* _s;
        readonly HandpanSound _sound;

        public HandpanUnit(SoundContext ctx) : this(ctx, UnitId.Handpan, SourceId.Handpan) { }

        public HandpanUnit(SoundContext ctx, UnitId role, SourceId source) : base(ctx)
        {
            // scratch for blocks of up to 1024 frames (the device renders 256; RenderUnit chunks longer offline blocks)
            int maxFrames = System.Math.Min(ctx.Host.Config.MaxFrames, 1024);
            byte* mem = (byte*)ctx.Host.Alloc(HandpanStateBuilder.Bytes(maxFrames));
            _s = HandpanStateBuilder.Layout(mem, maxFrames);
            HandpanStateBuilder.Fill(_s, HandpanVoiceChoice.Voice);   // the tables are data: no boot-time design work
            Volatile.Write(ref _s->Ready, 1);
            _sound = new HandpanSound(this, _s);
            Register(ctx, role, new[] { source }, _s, ringCapacity: 256, pendingCapacity: 256, idleSkip: true);
        }

        public HandpanState* State => _s;
        public HandpanSound Sound => _sound;
        public long StateBytes => HandpanStateBuilder.Bytes(_s->MaxFrames);

        bool IHandpanCommandPort.Stage(in GraphCommand c) => Stage(c);
        void IHandpanCommandPort.Commit() => Commit();

        // ---- IHandpanSound (each records its call on the test tap: AudioCall.Unit, op = the command's op)
        public void Strike(int field, float vel, float u, float v, HandpanTech tech, double when, float gain = 1f)
        {
            RecordUnit(HandpanState.OpStrike, field, vel, when);
            _sound.Strike(field, vel, u, v, tech, when, gain);
        }
        public void StrikeTak(double vel, int n1, double w1, int n2, double w2, double when)
        {
            RecordUnit(HandpanState.OpStrike, HandpanState.Tak, (float)vel, when);
            _sound.StrikeTak(vel, n1, w1, n2, w2, when);
        }
        public void Damp(int field, int slot, float strength, float u, float v, HandpanDampKind kind, double when)
        {
            RecordUnit(HandpanState.OpDamp, field, strength, when);
            _sound.Damp(field, slot, strength, u, v, kind, when);
        }
        public void ReleaseAll(double when)
        {
            RecordUnit(HandpanState.OpReleaseAll, -1, 0f, when);
            _sound.ReleaseAll(when);
        }
        public float Level(int field) => _sound.Level(field);

        // ---- ILoudnessReference: the platform's mf phrase (E E F G, quarters at 108 bpm from 0.2 s, vel 0.55), here on
        // E4 E4 F4 G4 at the dimple's edge (the sweet spot every lesson asks for)
        public double ReferenceSeconds => 4.0;
        static readonly int[] s_referenceFields = { 4, 4, 5, 6 };
        public void PlayReference(double start)
        {
            const double spb = 60.0 / 108.0;
            for (int i = 0; i < s_referenceFields.Length; i++) Strike(s_referenceFields[i], 0.55f, 0.35f, 0.2f, HandpanTech.Tip, start + 0.2 + i * spb);
        }

        /// <summary>IUnitWorstCase (the descriptor's CapWorstMs; HandpanPerfTests / bench.mjs 'worst'): every field struck ff at
        /// the dimple's edge every 60 ms (11 blocks of 256 frames at 48 kHz), 2 ms apart: the 10-finger roll.</summary>
        public void DriveWorst(long block, double when)
        {
            if (block % 11 != 3) return;
            for (int f = 0; f < HandpanState.Tak; f++) Strike(f, 1f, 0.35f, 0.15f, HandpanTech.Tip, when > 0 ? when + f * 0.002 : 0);
        }

        public override string Stats => _sound.Stats;
    }
}
