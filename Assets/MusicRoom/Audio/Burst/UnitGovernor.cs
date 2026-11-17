// The unit governor (12-new-instruments/platform.md 10.4; reference: the Node budget prototype's governor()). Every
// frame each instrument's unit gets a class from the registry and the layout:
//   Focus    played live in the last 2 s (RecorderEvent, not DuringPlayback; Registry.OwnerOfEvent) or faced
//            (ILayoutService.Facing). Its share is 1 and never drops.
//   AtSeat   at the seat (or gliding to or from it).
//   Playback on display, sounding through take / loop dispatch (a playback event or AudioPresence.Wake in the last 2 s).
//   Tail     on display, still ringing (or silent: then it is idle-skipped anyway).
// Once a second, on the graph block's DspLoadMeter p99 (the window AudioGraphHost.Tick reads for the LOD budget): above
// 1.6 ms the lowest class whose share is above 0.4 loses 0.15, all its units together (floor 0.4); below 1.0 ms the
// highest class below 1 gains 0.05. The shares go into UnitSlot.Share (main -> audio); a unit scales its own caps by
// Graph->Budget / 1200 x Share at block start (KnockDsp; the classic piano, kit and harp units read only the budget).
// Step is the decision alone, shared by the live path and the reference's cost model (Model*, GovernorTests).
using System;
using System.Threading;
using MusicRoom.Core;

namespace MusicRoom.Audio.Burst
{
    public sealed unsafe class UnitGovernor
    {
        public const double HighMs = 1.6, LowMs = 1.0, Floor = 0.4, DropStep = 0.15, RaiseStep = 0.05;
        /// <summary>Live play (Focus) and playback dispatch (Playback) count this long.</summary>
        public const float RecentSeconds = 2f;
        /// <summary>The reference's cost model (budget.mjs): a unit costs 20 % fixed + 80 % scaled by its share; p99 = 1.15 x
        /// the average block.</summary>
        public const double ModelFixedPart = 0.2, ModelP99Scale = 1.15;

        static readonly byte[] s_lowestFirst = { UnitClass.Tail, UnitClass.Playback, UnitClass.AtSeat };

        /// <summary>One once-a-second decision (budget.mjs governor(), one step) on units 0..count-1 with their classes and
        /// shares (shares rounded to 4 decimals as the reference). Focus is never changed. True when a share changed.</summary>
        public static bool Step(double p99Ms, int count, byte[] cls, double[] share)
        {
            if (p99Ms > HighMs)
            {
                for (int k = 0; k < s_lowestFirst.Length; k++)
                {
                    byte c = s_lowestFirst[k];
                    bool any = false;
                    for (int i = 0; i < count; i++)
                        if (cls[i] == c && share[i] > Floor + 1e-9) { share[i] = Math.Max(Floor, Round4(share[i] - DropStep)); any = true; }
                    if (any) return true;
                }
            }
            else if (p99Ms < LowMs)
            {
                for (int k = s_lowestFirst.Length - 1; k >= 0; k--)
                {
                    byte c = s_lowestFirst[k];
                    bool any = false;
                    for (int i = 0; i < count; i++)
                        if (cls[i] == c && share[i] < 1 - 1e-9) { share[i] = Math.Min(1.0, Round4(share[i] + RaiseStep)); any = true; }
                    if (any) return true;
                }
            }
            return false;
        }

        static double Round4(double x) => Math.Round(x, 4, MidpointRounding.AwayFromZero);

        /// <summary>The reference's average block (ms): fixed (overhead + room) + each unit's worst x (0.2 + 0.8 share).</summary>
        public static double ModelAverageMs(double fixedMs, int count, double[] ms, double[] share)
        {
            double c = fixedMs;
            for (int i = 0; i < count; i++) c += ms[i] * (ModelFixedPart + (1 - ModelFixedPart) * share[i]);
            return c;
        }

        /// <summary>The reference run: shares start at 1, each step measures the model's p99 and calls Step. Returns the p99 of
        /// each step (log[0] before any change), the final shares in share.</summary>
        public static double[] ModelRun(double fixedMs, int count, byte[] cls, double[] ms, double[] share, int steps = 30)
        {
            for (int i = 0; i < count; i++) share[i] = 1.0;
            var log = new double[steps];
            for (int s = 0; s < steps; s++)
            {
                double p99 = ModelAverageMs(fixedMs, count, ms, share) * ModelP99Scale;
                log[s] = p99;
                Step(p99, count, cls, share);
            }
            return log;
        }

        // ------------------------------------------------------------------ live (main thread)

        readonly AudioGraphHost _host;
        readonly UnitId[] _role = new UnitId[(int)UnitId.Count];
        readonly InstrumentKind[] _kind = new InstrumentKind[(int)UnitId.Count];
        readonly byte[] _cls = new byte[(int)UnitId.Count];
        readonly double[] _share = new double[(int)UnitId.Count];
        readonly float[] _written = new float[(int)UnitId.Count];
        readonly byte[] _writtenCls = new byte[(int)UnitId.Count];
        int _count, _seconds;

        public UnitGovernor(AudioGraphHost host)
        {
            _host = host;
            _seconds = host != null ? host.Seconds : 0;
        }

        /// <summary>Units governed (one per instrument whose role has a registered unit).</summary>
        public int Count => _count;
        /// <summary>Once-a-second steps taken, and those that changed a share (stats, tests).</summary>
        public int Steps { get; private set; }
        public int Changes { get; private set; }
        public double LastP99Ms { get; private set; }
        public UnitId Role(int i) => _role[i];
        public InstrumentKind Kind(int i) => _kind[i];
        public byte ClassOf(int i) => _cls[i];
        public double ShareOf(int i) => _share[i];

        /// <summary>The share of a role (1 when it is not governed).</summary>
        public double ShareOf(UnitId role)
        {
            for (int i = 0; i < _count; i++) if (_role[i] == role) return _share[i];
            return 1.0;
        }

        /// <summary>Main thread, once (the registry is static): every playable descriptor whose role has a unit in the graph.</summary>
        public void Bind(IInstrumentRegistry registry)
        {
            _count = 0;
            if (registry == null || _host == null) return;
            var all = registry.All;
            for (int i = 0; i < all.Count && _count < _role.Length; i++)
            {
                InstrumentDescriptor d = all[i];
                if (d?.Audio == null || d.Audio.Unit >= (byte)UnitId.Count) continue;
                var role = (UnitId)d.Audio.Unit;
                if (!_host.IsRegistered(role) || IndexOf(role) >= 0) continue;
                _role[_count] = role;
                _kind[_count] = d.Kind;
                _cls[_count] = UnitClass.AtSeat;
                _share[_count] = 1.0;
                _written[_count] = -1f;
                _count++;
            }
        }

        int IndexOf(UnitId role) { for (int i = 0; i < _count; i++) if (_role[i] == role) return i; return -1; }

        /// <summary>The class of an instrument's unit now (see the file header).</summary>
        public static byte Classify(InstrumentKind kind, ILayoutService layout, IInstrumentActivity presence)
        {
            bool facing = layout != null && layout.Facing == kind;
            if (facing || (presence != null && presence.SecondsSinceLive(kind) < RecentSeconds)) return UnitClass.Focus;
            InstrumentPlace place = presence != null ? presence.PlaceOf(kind) : InstrumentPlace.Seat;
            if (place == InstrumentPlace.Seat || place == InstrumentPlace.Gliding) return UnitClass.AtSeat;
            if (presence != null && presence.SecondsSincePlayback(kind) < RecentSeconds) return UnitClass.Playback;
            return UnitClass.Tail;
        }

        /// <summary>Main thread, every frame: classes (Focus at full share), then once per host load window one Step on its
        /// p99, then the slots. Allocation-free.</summary>
        public void Tick(ILayoutService layout, IInstrumentActivity presence)
        {
            if (_host == null || _host.IsDisposed) return;
            Classes(layout, presence);
            if (_host.Seconds != _seconds)
            {
                _seconds = _host.Seconds;
                Decide(_host.LastSecond.P99Micros / 1000.0);
            }
            Write();
        }

        /// <summary>One decision on a given p99 now (tests and tools; the live path takes the host's once-a-second window),
        /// with the classes of this moment, and the slots written.</summary>
        public void StepNow(double p99Ms, ILayoutService layout, IInstrumentActivity presence)
        {
            if (_host == null || _host.IsDisposed) return;
            Classes(layout, presence);
            Decide(p99Ms);
            Write();
        }

        void Classes(ILayoutService layout, IInstrumentActivity presence)
        {
            for (int i = 0; i < _count; i++)
            {
                _cls[i] = Classify(_kind[i], layout, presence);
                if (_cls[i] == UnitClass.Focus) _share[i] = 1.0;
            }
        }

        void Decide(double p99Ms)
        {
            LastP99Ms = p99Ms;
            Steps++;
            if (Step(p99Ms, _count, _cls, _share)) Changes++;
        }

        void Write()
        {
            for (int i = 0; i < _count; i++)
            {
                float sh = (float)_share[i];
                if (sh == _written[i] && _cls[i] == _writtenCls[i]) continue;
                UnitSlot* u = _host.Unit(_role[i]);
                if (u == null) continue;
                Volatile.Write(ref u->Share, sh);
                u->Class = _cls[i];
                _written[i] = sh;
                _writtenCls[i] = _cls[i];
            }
        }
    }
}
