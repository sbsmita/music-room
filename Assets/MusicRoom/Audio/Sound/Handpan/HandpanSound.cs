// The handpan unit's main-thread side (handpan.md 5.2), kept apart from SoundUnit so it is testable before the platform
// gives the handpan a UnitId: it turns IHandpanSound calls into GraphCommands (the pulse is computed here, in double,
// rounded once: no transcendental function runs on the audio thread) and stages them on a port. In the app the port is
// HandpanUnit (a SoundUnit: its Stage applies AudioSchedule and its ring feeds the graph); in the EditMode tests it is
// the offline rig. Allocation-free.
using System;
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    /// <summary>Where the handpan's commands go: SoundUnit.Stage / Commit in the app, the offline rig in the tests.</summary>
    public interface IHandpanCommandPort
    {
        bool Stage(in GraphCommand c);
        void Commit();
    }

    /// <summary>The contact pulse of one strike (strikeParams of handpan-engine.mjs).</summary>
    public struct HandpanStrikeParams
    {
        public double V, Tc;
        public int W1, W2;
        public float A1, A2, C1, S1, C2, S2;
    }

    /// <summary>The pure command maths (handpan.md 2.3, 5.2).</summary>
    public static class HandpanCommands
    {
        /// <summary>JavaScript's Math.round for the positive values the reference rounds (half up).</summary>
        public static int JsRound(double x) => (int)Math.Floor(x + 0.5);

        /// <summary>A float velocity as the decimal it was meant to be (7 significant digits: 0.6f -&gt; 0.6), so a strike
        /// from a float API computes the same pulse as the reference's double velocity (the interaction's 3-decimal values
        /// and the vectors' round numbers survive exactly). Allocation-free.</summary>
        public static double VelOf(float vel) => float.IsNaN(vel) || float.IsInfinity(vel) ? 0.0 : (double)(decimal)vel;

        public static HandpanStrikeParams StrikeParams(double vel, HandpanTech tech, double sr = 48000)
        {
            double v = Math.Max(0, Math.Min(1, vel));
            double V = HandpanK.V_MIN + (HandpanK.V_MAX - HandpanK.V_MIN) * Math.Pow(v, HandpanK.V_EXP);
            double tc = (tech == HandpanTech.Slap ? HandpanK.TC_PALM : HandpanK.TC_FINGER) * Math.Pow(V, HandpanK.TC_EXP);
            int W1 = Math.Max(8, JsRound(tc * sr)), W2 = Math.Max(6, JsRound(HandpanK.W2_FRAC * W1));
            double m = tech == HandpanTech.Slap ? HandpanK.M_PALM : tech == HandpanTech.Tak ? HandpanK.M_TAK : HandpanK.M_FINGER;
            double I = m * V;
            double A1 = (2 * HandpanK.W1_MIX * I) / W1 * 48, A2 = (2 * (1 - HandpanK.W1_MIX) * I) / W2 * 48;
            return new HandpanStrikeParams
            {
                V = V, Tc = tc, W1 = W1, W2 = W2, A1 = (float)A1, A2 = (float)A2,
                C1 = (float)Math.Cos((2 * Math.PI) / W1), S1 = (float)Math.Sin((2 * Math.PI) / W1),
                C2 = (float)Math.Cos((2 * Math.PI) / W2), S2 = (float)Math.Sin((2 * Math.PI) / W2),
            };
        }

        /// <summary>Op 1 Strike: N field; Target tech; A = A1 gain; Seed = bits(A2 gain); B u; C v; D E F G the rotations;
        /// Id = W1 | W2 &lt;&lt; 16; LateDrop. A gain of exactly 1 leaves A1 and A2 untouched (hpStrike).</summary>
        public static unsafe GraphCommand Strike(int field, double vel, float u, float v, HandpanTech tech, double when, double gain = 1)
        {
            HandpanStrikeParams p = StrikeParams(vel, tech);
            float a1 = gain == 1 ? p.A1 : (float)(p.A1 * gain), a2 = gain == 1 ? p.A2 : (float)(p.A2 * gain);
            return new GraphCommand
            {
                Op = (GraphOp)HandpanState.OpStrike, Flags = CommandFlags.LateDrop, When = when,
                N = field, Target = (int)tech, A = a1, Seed = *(uint*)&a2, B = u, C = v,
                D = p.C1, E = p.S1, F = p.C2, G = p.S2, Id = p.W1 | (p.W2 << 16),
            };
        }

        /// <summary>Op 2 Damp (never LateDrop): N field (9 = the whole shell); Target slot; A strength; B u; C v; D kind.</summary>
        public static GraphCommand Damp(int field, int slot, float strength, float u, float v, HandpanDampKind kind, double when) =>
            new GraphCommand { Op = (GraphOp)HandpanState.OpDamp, When = when, N = field, Target = slot, A = strength, B = u, C = v, D = (float)kind };

        public static GraphCommand Reset() => new GraphCommand { Op = (GraphOp)HandpanState.OpReset };
        public static GraphCommand ReleaseAll(double when) => new GraphCommand { Op = (GraphOp)HandpanState.OpReleaseAll, When = when };
        /// <summary>Op 4 (tests and tools): a fixed LOD budget for this unit (0 = the graph's again).</summary>
        public static GraphCommand SetBudget(float budget, double when) => new GraphCommand { Op = (GraphOp)HandpanState.OpSetBudget, When = when, A = budget };
    }

    /// <summary>IHandpanSound over a command port and the unit's state (levels).</summary>
    public sealed unsafe class HandpanSound : IHandpanSound
    {
        readonly IHandpanCommandPort _port;
        readonly HandpanState* _s;

        public HandpanSound(IHandpanCommandPort port, HandpanState* state) { _port = port; _s = state; }

        public HandpanState* State => _s;
        /// <summary>Commands the port refused (a full ring).</summary>
        public int Refused { get; private set; }

        void Send(in GraphCommand c) { if (!_port.Stage(c)) Refused++; }

        public void Strike(int field, float vel, float u, float v, HandpanTech tech, double when, float gain = 1f) =>
            StrikeExact(field, HandpanCommands.VelOf(vel), u, v, tech, when, gain == 1f ? 1.0 : HandpanCommands.VelOf(gain));

        /// <summary>Strike with a double velocity and gain (the reference's own types; tests and the tak's neighbours).</summary>
        public void StrikeExact(int field, double vel, float u, float v, HandpanTech tech, double when, double gain = 1)
        {
            if (field < 0 || field > HandpanState.Tak) return;
            Send(HandpanCommands.Strike(field, vel, u, v, tech, when, gain));
            _port.Commit();
        }

        /// <summary>A tak at one moment (hpTak / HandpanModule.StrikeTak): the shell pulse (field 9), then a Tak pulse on each
        /// of the two nearest fields with its weight. Three commands with one `when`, in this order, in ONE batch (they land
        /// in the same block). The neighbours come from the layout (HandpanMath.TakNeighbours).</summary>
        public void StrikeTak(double vel, int n1, double w1, int n2, double w2, double when)
        {
            Send(HandpanCommands.Strike(HandpanState.Tak, vel, 0f, 0f, HandpanTech.Tak, when));
            if ((uint)n1 < HandpanState.NF) Send(HandpanCommands.Strike(n1, vel, 0f, 0f, HandpanTech.Tak, when, w1));
            if ((uint)n2 < HandpanState.NF) Send(HandpanCommands.Strike(n2, vel, 0f, 0f, HandpanTech.Tak, when, w2));
            _port.Commit();
        }

        public void Damp(int field, int slot, float strength, float u, float v, HandpanDampKind kind, double when)
        {
            if (field < 0 || field > HandpanState.Tak || (uint)slot >= HandpanState.Slots) return;
            Send(HandpanCommands.Damp(field, slot, strength, u, v, kind, when));
            _port.Commit();
        }

        public void ReleaseAll(double when) { Send(HandpanCommands.ReleaseAll(when)); _port.Commit(); }

        public void Reset() { Send(HandpanCommands.Reset()); _port.Commit(); }

        public float Level(int field) => (uint)field < HandpanState.NF ? Volatile.Read(ref _s->Level[field]) : 0f;

        /// <summary>One diagnostics line (SoundUnit.Stats).</summary>
        public string Stats =>
            $"handpan voice {(HandpanVoice)_s->Voice} groups {Volatile.Read(ref _s->ActiveGroups)} max {Volatile.Read(ref _s->MaxGroups)} events {Volatile.Read(ref _s->Events)} merged {Volatile.Read(ref _s->Merged)} retired {Volatile.Read(ref _s->Retired)} work {Volatile.Read(ref _s->Work)} refused {Refused}";
    }
}
