// The marimba unit's main-thread core (spec 6.2 "Main-thread calls"): IMarimbaSound on top of a MarimbaState. A strike
// runs the contact solve into the next strike slot (counter & 31) and posts one command; a damp writes the next damp
// record (counter & 63). The command goes to a sink: in phase B that is the SoundUnit's own ring (MarimbaUnit, see
// PhaseB~/), in phase A the offline harness (MarimbaOffline). Main thread only; no allocation per call.
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    /// <summary>Where the core's commands go (SoundUnit.Post in the app, the offline harness in tests).</summary>
    public interface IMarimbaCommandSink
    {
        bool Post(in GraphCommand c);
    }

    public sealed unsafe class MarimbaSound : IMarimbaSound
    {
        readonly MarimbaState* _s;
        readonly IMarimbaCommandSink _sink;
        readonly MarimbaContact _contact = new MarimbaContact();
        uint _seed;
        int _counter, _dampCounter;

        public MarimbaSound(MarimbaState* state, IMarimbaCommandSink sink, uint seed)
        {
            _s = state;
            _sink = sink;
            _seed = seed;
        }

        public MarimbaState* State => _s;
        /// <summary>Diagnostics of the last strike's contact solve.</summary>
        public MarimbaStrikeStats LastStrike => _contact.Last;
        public int StrikeCount => _counter;
        /// <summary>The record the last Strike wrote (tests).</summary>
        public MarimbaStrikeRecord* LastRecord
        {
            get
            {
                MarimbaStrikeRecord* r = null;   // never `cond ? pointer : null` in managed code (IL2CPP truncates it)
                if (_counter > 0) r = _s->Strikes + ((_counter - 1) & (MarimbaConst.StrikeSlots - 1));
                return r;
            }
        }

        /// <summary>IAudioEngine.SeedAll: re-seeds the knock noise (main thread only) and restarts the strike counter.</summary>
        public void Seed(uint seed) { _seed = seed; _counter = 0; }

        public void Strike(int bar, float vel, float u, float c, MarimbaMallet mallet, double when = 0) =>
            StrikeExact(bar, vel, u, c, mallet, when);

        /// <summary>Strike with double u, c (the lab passes doubles: renders and vectors use this to match it exactly).</summary>
        public void StrikeExact(int bar, float vel, double u, double c, MarimbaMallet mallet, double when = 0)
        {
            if ((uint)bar >= MarimbaConst.Bars || (uint)mallet > (uint)MarimbaMallet.Finger) return;
            if (Volatile.Read(ref _s->Ready) == 0) return;
            int slot = _counter & (MarimbaConst.StrikeSlots - 1);
            _contact.Solve(_s->Strikes + slot, bar, vel, u, c, mallet, MarimbaContact.SeedOf(_seed, _counter));
            _counter++;
            _sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpStrike, Flags = CommandFlags.LateDrop, When = when, Id = slot, N = bar, A = vel });
        }

        public void Damp(int bar, MarimbaDampKind kind, float u, float c, double when = 0) => DampExact(bar, kind, u, c, when);

        public void DampExact(int bar, MarimbaDampKind kind, double u, double c, double when = 0)
        {
            if ((uint)bar >= MarimbaConst.Bars) return;
            int slot = _dampCounter++ & (MarimbaConst.DampSlots - 1);
            MarimbaContact.DampPoles(_s->Damps + slot, bar, kind, u, c);
            _sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpDamp, When = when, Id = slot, N = bar, A = (float)u });
        }

        public void Undamp(int bar, double when = 0)
        {
            if ((uint)bar >= MarimbaConst.Bars) return;
            _sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpUndamp, When = when, N = bar });
        }

        public void Silence() => _sink.Post(new GraphCommand { Op = (GraphOp)MarimbaConst.OpSilence });

        public float BarLevel(int bar) => (uint)bar < MarimbaConst.Bars ? Volatile.Read(ref _s->Level[bar]) : 0f;

        /// <summary>Review item 13, opt-in: crossfade the four bars around each octave boundary between the neighbouring
        /// sources (constant power) instead of the hard per-octave assignment. Off by default: judge it by ear in the room (M8).</summary>
        public bool BoundaryCrossfade
        {
            get => Volatile.Read(ref _s->Crossfade) != 0;
            set => Volatile.Write(ref _s->Crossfade, value ? 1 : 0);
        }

        /// <summary>The share of the platform's governor (UnitSlot.Share in phase B), 0..1.</summary>
        public float Share
        {
            get => Volatile.Read(ref _s->Share);
            set => Volatile.Write(ref _s->Share, value < 0f ? 0f : value > 1f ? 1f : value);
        }

        public string Stats =>
            $"marimba strikes {Volatile.Read(ref _s->StrikesPlayed)} injections {Volatile.Read(ref _s->Injections)} work {Volatile.Read(ref _s->Work)} active {Volatile.Read(ref _s->ActiveBars)} " +
            $"capped {Volatile.Read(ref _s->Capped)} retired {Volatile.Read(ref _s->Retired)} droppedPlayers {Volatile.Read(ref _s->DroppedPlayers)} droppedActions {Volatile.Read(ref _s->DroppedActions)}";
    }
}
