// The bare-hands marimba interaction (spec 5.3, 5.4, 5.6; the Node reference interaction, revision 2),
// rule for rule as plain C# so it runs on pointer samples alone: mallet heads (from the held mallets' poses) and
// fingertips, in the instrument-local frame (metres; +z away from the player, y up), double precision as the reference.
// One Tick per frame. Output goes to an IMarimbaPlayListener (the module: the sound unit, the recorder, the bar's dip,
// Sunny's signals). No allocation per frame: every table is preallocated (pointers, bars, owners).
//
// Built for VR strokes (nothing stops the hand at the bar, so strokes overshoot and turn around under the plane, and
// tracking jitters; the mallet's 27 cm lever magnifies the grip's jitter at the head): arming 20 mm above the plane on
// two samples, the approach speed over the last 50 ms, tracking jumps (> max(0.15 m, 14 m/s x dt) or after a
// low-confidence sample) never strike, dead strokes and rests need a hold band (plane -25 .. +10 mm) unbroken for
// 200 / 150 ms and then stillness, a hand mute needs two fingertips for 120 ms, and a hand's index tip plays first.
// Departure from the reference (review item 19): a lost mallet's mute is released after 0.5 s of real elapsed time
// (the reference counted 1000 / 90 ms per frame, 0.625 s at 72 Hz).
using System;
using MusicRoom.Audio.Burst;
using Unity.Mathematics;

namespace MusicRoom.Marimba
{
    /// <summary>The interaction's constants (interaction.reference.mjs IX, revision 2; spec 5.2-5.4).</summary>
    public static class MarimbaIx
    {
        public const double HeadR = 0.017, FingerR = 0.008, SideReach = 0.004, EndReach = 0.010;
        public const double ArmAbove = 0.020, ArmAboveFinger = 0.012;
        public const int ArmSamples = 2;
        public const double MaxPrevMallet = 0.30, MaxPrevFinger = 0.10;
        public const double V0Mallet = 0.25, V1Mallet = 2.8, V0Finger = 0.12, V1Finger = 1.5, VelExp = 0.7, VelMin = 0.05;
        public const double ApproachMs = 50, JumpM = 0.15, JumpV = 14.0;
        public const double SameBarMs = 3, OwnerMs = 45;
        public const double BandBelow = 0.025, BandAbove = 0.010, DeadWindowMs = 200, RestMs = 150, StillWinMs = 60, DeadStill = 0.15;
        public const int StillSamples = 2, BreakSamples = 3, LiftSamples = 3;
        public const double DeadLift = 0.020;
        public const double HandBand = 0.012, HandStill = 0.30, HandMs = 120, HandLift = 0.02, HandLiftMs = 30;
        public const int HandMinTips = 2;
        public const double PlayClear = 0.020, DeferAbove = 0.015, DeferMs = 25, Tie = 0.10;
        public const int RollMin = 4;
        public const double RollGapMs = 200, LostUndampMs = 500;
        public const int FingerOnlyN = 6;
        public const double FingerOnlyMs = 20000;
        public const int OffCentreN = 10;
        public const double OffCentreShare = 0.6, CentreLo = 0.3, CentreHi = 0.7;
        public const double AccRatio = 0.35, AccMin = 0.05;

        /// <summary>vel = clamp(((vy - v0) / (v1 - v0))^0.7, 0.05, 1); -1 below v0 (a mallet's silent lowering).</summary>
        public static double VelocityOf(double vy, bool finger)
        {
            double v0 = finger ? V0Finger : V0Mallet, v1 = finger ? V1Finger : V1Mallet;
            if (vy < v0) return -1;
            return Math.Min(1, Math.Max(VelMin, Math.Pow((vy - v0) / (v1 - v0), VelExp)));
        }

        /// <summary>The lesson accompaniment's velocity (spec 7, request R6): 0.35 x the player's last live stroke, at most
        /// the written velocity, at least 0.05. No live stroke yet: the written velocity.</summary>
        public static double AccompanyVel(double lessonVel, double lastVel) =>
            Math.Min(lessonVel, Math.Max(AccMin, AccRatio * (double.IsNaN(lastVel) ? lessonVel / AccRatio : lastVel)));
    }

    public enum MarimbaSignal : byte { None, BackRow, FingerOnly, OffCentre, DeadStroke, Mute, Roll, Pickup, Release }

    public static class MarimbaSignals
    {
        /// <summary>The signal's name for InstrumentSignalEvent{Marimba, ...} and the tips (spec 5.6), interned.</summary>
        public static string Name(MarimbaSignal s)
        {
            switch (s)
            {
                case MarimbaSignal.BackRow: return "marimba.backRow";
                case MarimbaSignal.FingerOnly: return "marimba.fingerOnly";
                case MarimbaSignal.OffCentre: return "marimba.offCentre";
                case MarimbaSignal.DeadStroke: return "marimba.deadStroke";
                case MarimbaSignal.Mute: return "marimba.mute";
                case MarimbaSignal.Roll: return "marimba.roll";
                case MarimbaSignal.Pickup: return "marimba.pickup";
                case MarimbaSignal.Release: return "marimba.release";
                default: return "";
            }
        }
    }

    /// <summary>What MarimbaPlay decides (the module routes it to the unit, the recorder, the visuals and Sunny).</summary>
    public interface IMarimbaPlayListener
    {
        /// <summary>A stroke: vel 0..1, u along the bar (0 near end), c across (-1..1). deferred: an index-first wait.</summary>
        void OnStrike(int bar, double vel, double u, double c, MarimbaPointer by, bool deferred, double nowMs);
        void OnDamp(int bar, MarimbaDampKind kind, double u, bool dead, int owner, double nowMs);
        /// <summary>why: "restrike", "lift", "hand lifted", "tracking lost".</summary>
        void OnUndamp(int bar, string why, double nowMs);
        void OnRoll(int bar, double nowMs);
        void OnSignal(MarimbaSignal signal, double nowMs);
    }

    /// <summary>One pointer the interaction measures: a mallet head (Finger false, TipKind -1) or a fingertip (TipKind 0 thumb,
    /// 1 index, 2 middle, 3 ring, 4 little). The caller sets Tip / Active / Holding / LowConf / Speed each frame and calls
    /// Advance() first (Prev = Tip, WasActive = Active), as the reference's per-frame update.</summary>
    public sealed class MarimbaPointer
    {
        internal const int HistCap = 64, ApprCap = 64;

        public readonly string Id;
        /// <summary>A small dense index: the mallet or the hand that owns this pointer (debounce, rolls, mutes).</summary>
        public readonly int Owner;
        public readonly bool Finger;
        public readonly int TipKind;

        public double3 Tip, Prev;
        public bool HasTip, HasPrev, Active, WasActive, Holding, LowConf;
        /// <summary>This tick carries a new tracker sample. At the build's LOW hand-tracking rate the app frame can repeat a
        /// sample; a repeat (Fresh false) is skipped by the stroke, arming, approach and rest rules, which count samples
        /// (the hand mute is time-based and still runs). The caller keeps Tip / Prev as they were on a repeat.</summary>
        public bool Fresh = true;
        /// <summary>|tip - prev| / sample dt (m/s): the hand mute's stillness.</summary>
        public double Speed;

        internal bool PrevLowConf;
        internal readonly byte[] Arm = new byte[MarimbaGeometry.Bars], Above = new byte[MarimbaGeometry.Bars];
        internal int PlayBar = -1;
        // the mallet's hold (dead stroke / rest): HoldBar < 0 = none
        internal int HoldBar = -1, HoldBreakRun, HoldStillRun;
        internal bool HoldStruck, HoldDamped;
        internal double HoldSince, HoldU;
        internal int LiftRun;
        internal double LostMs;
        // height history: (t, y) for the stillness window, and the approach ring
        internal readonly double[] HistT = new double[HistCap], HistY = new double[HistCap];
        internal int HistStart, HistCount;
        internal readonly double[] Appr = new double[ApprCap];
        internal int ApprStart, ApprCount;

        public MarimbaPointer(string id, int owner, bool finger, int tipKind)
        {
            Id = id; Owner = owner; Finger = finger; TipKind = finger ? tipKind : -1;
            for (int i = 0; i < Arm.Length; i++) Arm[i] = 1;
        }

        /// <summary>Start of a frame: the last sample becomes Prev.</summary>
        public void Advance()
        {
            WasActive = Active;
            Prev = Tip; HasPrev = HasTip;
        }

        /// <summary>This frame's sample (null = untracked).</summary>
        public void Sample(bool tracked, double3 tip, double speed = 0, bool holding = false, bool lowConf = false)
        {
            Active = tracked; HasTip = tracked; if (tracked) Tip = tip;
            Speed = speed; Holding = holding; LowConf = lowConf;
        }

        public bool Holds => HoldBar >= 0;
        public bool HoldsDamped => HoldBar >= 0 && HoldDamped;

        internal void ClearHold() { HoldBar = -1; HoldBreakRun = 0; HoldStillRun = 0; HoldStruck = false; HoldDamped = false; }
        internal void SetHold(int bar, bool struck, double now, double u)
        {
            HoldBar = bar; HoldStruck = struck; HoldSince = now; HoldBreakRun = 0; HoldStillRun = 0; HoldDamped = false; HoldU = u;
        }
        internal void HistClear() { HistStart = 0; HistCount = 0; }
        internal void HistPush(double t, double y)
        {
            if (HistCount == HistCap) { HistStart = (HistStart + 1) % HistCap; HistCount--; }
            int k = (HistStart + HistCount) % HistCap;
            HistT[k] = t; HistY[k] = y; HistCount++;
        }
        internal double HT(int i) => HistT[(HistStart + i) % HistCap];
        internal double HY(int i) => HistY[(HistStart + i) % HistCap];
        internal void HistShift() { HistStart = (HistStart + 1) % HistCap; HistCount--; }
        internal void ApprClear() { ApprStart = 0; ApprCount = 0; }
        internal void ApprPush(double y)
        {
            if (ApprCount == ApprCap) { ApprStart = (ApprStart + 1) % ApprCap; ApprCount--; }
            Appr[(ApprStart + ApprCount) % ApprCap] = y; ApprCount++;
        }
        internal double A(int i) => Appr[(ApprStart + i) % ApprCap];
        internal void ApprShift() { ApprStart = (ApprStart + 1) % ApprCap; ApprCount--; }
    }

    public sealed class MarimbaPlay
    {
        public const int MaxOwners = 8, MaxCands = 64, NB = MarimbaGeometry.Bars;
        const byte DampNone = 0, DampMallet = 1, DampHand = 2;

        struct Cand { public MarimbaPointer P; public int Bar; public double Vy, Vel, U, C; }

        // bars (instrument-local, metres, at the current height)
        readonly double[] _x = new double[NB], _z = new double[NB], _len = new double[NB], _w = new double[NB], _top = new double[NB];
        readonly bool[] _natural = new bool[NB];
        readonly double[] _barLastMs = new double[NB];
        readonly double[] _ownerLast = new double[MaxOwners];
        readonly byte[] _dampedKind = new byte[NB];
        readonly int[] _dampedOwner = new int[NB];
        // hand rests per (bar, owner), kept in insertion order like the reference's Map
        readonly bool[] _hrOn = new bool[NB * MaxOwners], _hrDamped = new bool[NB * MaxOwners];
        readonly double[] _hrSince = new double[NB * MaxOwners], _hrAway = new double[NB * MaxOwners];
        readonly int[] _hrOrder = new int[NB * MaxOwners];
        int _hrCount;
        readonly int[] _bbN = new int[NB * MaxOwners];
        readonly double[] _bbU = new double[NB * MaxOwners];
        readonly int[] _bbOrder = new int[NB * MaxOwners];
        int _bbCount;
        // rolls per bar
        readonly int[] _rollN = new int[NB], _rollOwner = new int[NB];
        readonly double[] _rollLast = new double[NB];
        // per-tick candidates, the best per owner, deferred strokes per owner
        readonly Cand[] _cands = new Cand[MaxCands];
        int _nc;
        readonly int[] _best = new int[MaxOwners];
        readonly bool[] _defOn = new bool[MaxOwners];
        readonly Cand[] _def = new Cand[MaxOwners];
        readonly double[] _defT = new double[MaxOwners];
        readonly long[] _defSeq = new long[MaxOwners];
        long _defCounter;
        readonly int[] _defIter = new int[MaxOwners];
        // Sunny's signals (once per session)
        readonly double[] _fingerTimes = new double[16];
        int _ftStart, _ftCount;
        bool _malletSeen, _fingerOnly, _offCentre, _backRow;
        readonly byte[] _offWin = new byte[MarimbaIx.OffCentreN];
        int _offStart, _offCount;
        double _lastNow = double.NaN;

        public IMarimbaPlayListener Listener;
        /// <summary>Dead strokes (spec 5.4): off in lessons and until tip_marimba_dead turned them on.</summary>
        public bool DeadStrokes = true;

        public MarimbaPlay(IMarimbaPlayListener listener = null, double naturalTopH = MarimbaTables.HDefault)
        {
            Listener = listener;
            SetHeight(naturalTopH);
            for (int i = 0; i < NB; i++) { _barLastMs[i] = -1e9; _rollLast[i] = -1e9; _rollOwner[i] = -1; }
            for (int o = 0; o < MaxOwners; o++) _ownerLast[o] = -1e9;
        }

        /// <summary>The bars at natural-top height h (the tables' tops are at 0.70; the bars move with the stand's lift).</summary>
        public void SetHeight(double h)
        {
            double dy = h - MarimbaTables.HDefault;
            for (int i = 0; i < NB; i++)
            {
                _x[i] = MarimbaTables.X[i]; _z[i] = MarimbaTables.Z[i]; _len[i] = MarimbaTables.Length[i]; _w[i] = MarimbaTables.Width[i];
                _top[i] = dy == 0 ? MarimbaTables.YTop[i] : MarimbaTables.YTop[i] + dy;
                _natural[i] = MarimbaTables.Natural[i];
            }
        }

        public double Plane(int bar, bool finger) => _top[bar] + (finger ? MarimbaIx.FingerR : MarimbaIx.HeadR);
        public bool IsDamped(int bar) => _dampedKind[bar] != DampNone;
        public bool FingerOnlySent => _fingerOnly;

        bool InFoot(int b, double3 p, double side, double front) =>
            Math.Abs(p.x - _x[b]) <= _w[b] / 2 + side && p.z >= _z[b] - _len[b] / 2 - front && p.z <= _z[b] + _len[b] / 2;
        double FrontOf(int b) => _natural[b] ? 0 : MarimbaIx.EndReach;

        // ------------------------------------------------------------------ the frame

        public void Tick(MarimbaPointer[] pointers, int count, double dtSample, double now)
        {
            double frameMs = double.IsNaN(_lastNow) ? 1000.0 / 90 : Math.Max(0, now - _lastNow);
            _lastNow = now;
            _nc = 0;
            for (int pi = 0; pi < count; pi++)
            {
                MarimbaPointer p = pointers[pi];
                if (!p.Active || !p.WasActive || !p.HasPrev || !p.HasTip)
                {
                    if (p.Active && p.HasTip) Appear(p); else Lost(p, now, frameMs);
                    p.PrevLowConf = p.LowConf;
                    continue;
                }
                if (!p.Fresh) continue;   // a repeated tracker sample: nothing moved, no sample-counting rule advances
                p.LostMs = 0;
                if (!p.Finger) _malletSeen = true;
                double3 pr = p.Prev, t = p.Tip;
                double dt = Math.Max(dtSample, 0.001);
                double3 d = t - pr;
                bool jump = Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z) > Math.Max(MarimbaIx.JumpM, MarimbaIx.JumpV * dt) || p.PrevLowConf;
                p.PrevLowConf = p.LowConf;
                p.HistPush(now, t.y);
                while (p.HistCount > 2 && now - p.HT(1) >= MarimbaIx.StillWinMs) p.HistShift();
                p.ApprPush(t.y);
                int apprMax = Math.Max(3, (int)Math.Floor(MarimbaIx.ApproachMs / 1000 / dt + 0.5) + 1);
                while (p.ApprCount > apprMax) p.ApprShift();
                if (jump) { p.HistClear(); p.HistPush(now, t.y); p.ApprClear(); continue; }
                double armH = p.Finger ? MarimbaIx.ArmAboveFinger : MarimbaIx.ArmAbove;
                for (int b = 0; b < NB; b++)
                {
                    double y0 = Plane(b, p.Finger);
                    if (t.y > y0 + armH) { if (++p.Above[b] >= MarimbaIx.ArmSamples) p.Arm[b] = 1; } else p.Above[b] = 0;
                    if (p.PlayBar == b && t.y > y0 + MarimbaIx.PlayClear) p.PlayBar = -1;
                    if (!(pr.y > y0 && t.y <= y0)) continue;
                    if (pr.y - y0 > (p.Finger ? MarimbaIx.MaxPrevFinger : MarimbaIx.MaxPrevMallet)) continue;
                    double k = (pr.y - y0) / (pr.y - t.y);
                    var X = new double3(pr.x + (t.x - pr.x) * k, y0, pr.z + (t.z - pr.z) * k);
                    if (!InFoot(b, X, MarimbaIx.SideReach, FrontOf(b))) continue;
                    if (p.Arm[b] == 0) continue;
                    p.Arm[b] = 0; p.Above[b] = 0;          // any pass disarms, sounding or not
                    double vy = Math.Max((pr.y - t.y) / dt, Approach(p, dt)), vel = MarimbaIx.VelocityOf(vy, p.Finger);
                    double u = Math.Min(1, Math.Max(0, (X.z - (_z[b] - _len[b] / 2)) / _len[b]));
                    double c = Math.Min(1, Math.Max(-1, (X.x - _x[b]) / (_w[b] / 2)));
                    if (vel < 0) { if (!p.Finger) p.SetHold(b, false, now, u); continue; }   // a silent lowering
                    if (_nc < MaxCands) _cands[_nc++] = new Cand { P = p, Bar = b, Vy = vy, Vel = vel, U = u, C = c };
                }
            }
            // fingertips of one hand: one candidate per hand per sample (the faster; within TIE the index; then nearer the centre line)
            for (int o = 0; o < MaxOwners; o++) _best[o] = -1;
            for (int k = 0; k < _nc; k++)
            {
                ref Cand cd = ref _cands[k];
                if (!cd.P.Finger) continue;
                int ob = _best[cd.P.Owner];
                bool better;
                if (ob < 0) better = true;
                else
                {
                    ref Cand o = ref _cands[ob];
                    better = cd.Vy > o.Vy * (1 + MarimbaIx.Tie)
                        || (cd.Vy >= o.Vy * (1 - MarimbaIx.Tie) && ((cd.P.TipKind == 1) != (o.P.TipKind == 1) ? cd.P.TipKind == 1 : Math.Abs(cd.C) < Math.Abs(o.C)));
                }
                if (better) _best[cd.P.Owner] = k;
            }
            // deferred non-index strokes, in the order they were deferred: dropped if the hand's index plays now; kept while
            // the index is still coming down (at most DEFER_MS); otherwise they sound now with their own vel and point
            int nd = 0;
            for (int o = 0; o < MaxOwners; o++) if (_defOn[o]) _defIter[nd++] = o;
            for (int a = 1; a < nd; a++) { int x = _defIter[a], j = a - 1; while (j >= 0 && _defSeq[_defIter[j]] > _defSeq[x]) { _defIter[j + 1] = _defIter[j]; j--; } _defIter[j + 1] = x; }
            for (int a = 0; a < nd; a++)
            {
                int owner = _defIter[a];
                if (!_defOn[owner]) continue;
                int now2 = _best[owner];
                if (now2 >= 0 && _cands[now2].P.TipKind == 1) { _defOn[owner] = false; continue; }
                if (now - _defT[owner] < MarimbaIx.DeferMs && IndexAbout(pointers, count, _def[owner])) continue;
                _defOn[owner] = false;
                Strike(_def[owner], now, true);
            }
            for (int k = 0; k < _nc; k++)
            {
                Cand cd = _cands[k];
                if (cd.P.Finger && _best[cd.P.Owner] != k) continue;      // a sibling tip of a hand that played this sample
                if (cd.P.Finger && _defOn[cd.P.Owner]) continue;           // that hand already has a stroke waiting
                if (cd.P.Finger && cd.P.TipKind != 1 && IndexAbout(pointers, count, cd))
                {
                    _defOn[cd.P.Owner] = true; _def[cd.P.Owner] = cd; _defT[cd.P.Owner] = now; _defSeq[cd.P.Owner] = ++_defCounter;
                    continue;
                }
                Strike(cd, now, false);
            }
            for (int pi = 0; pi < count; pi++)
            {
                MarimbaPointer p = pointers[pi];
                if (p.Active && p.WasActive && p.HasPrev && p.HasTip && p.Fresh) RestCheck(p, now);
            }
            HandCheck(pointers, count, now);
        }

        /// <summary>The fastest downward speed over two samples in the approach ring (two-sample spans halve the jitter).</summary>
        static double Approach(MarimbaPointer p, double dt)
        {
            double m = 0;
            for (int k = 2; k < p.ApprCount; k++) { double v = (p.A(k - 2) - p.A(k)) / (2 * dt); if (v > m) m = v; }
            return m;
        }

        /// <summary>A pointer that appears (new, or tracking back) is disarmed for every bar it is not clearly above.</summary>
        void Appear(MarimbaPointer p)
        {
            double armH = p.Finger ? MarimbaIx.ArmAboveFinger : MarimbaIx.ArmAbove;
            for (int b = 0; b < NB; b++) { p.Arm[b] = (byte)(p.Tip.y > Plane(b, p.Finger) + armH ? 1 : 0); p.Above[b] = 0; }
            p.HistClear(); p.ApprClear();
        }

        bool IndexAbout(MarimbaPointer[] pointers, int count, in Cand cd)
        {
            MarimbaPointer ix = null;
            for (int i = 0; i < count; i++)
            {
                MarimbaPointer q = pointers[i];
                if (q.Finger && q.Owner == cd.P.Owner && q.TipKind == 1 && q.Active && q.WasActive && q.HasPrev && q.HasTip) { ix = q; break; }
            }
            if (ix == null || ix.Prev.y - ix.Tip.y <= 0) return false;
            for (int b = 0; b < NB; b++)
                if (b != cd.Bar && InFoot(b, ix.Tip, MarimbaIx.SideReach, FrontOf(b)) && ix.Tip.y > Plane(b, true) && ix.Tip.y - Plane(b, true) <= MarimbaIx.DeferAbove)
                    return true;
            return false;
        }

        void Strike(in Cand cd, double now, bool late)
        {
            MarimbaPointer p = cd.P;
            int b = cd.Bar;
            if (now - _barLastMs[b] < MarimbaIx.SameBarMs || now - _ownerLast[p.Owner] < MarimbaIx.OwnerMs)
            {
                if (!p.Finger) p.SetHold(b, true, now, cd.U);   // debounced: still a stroke
                return;
            }
            _barLastMs[b] = now; _ownerLast[p.Owner] = now;
            // a new stroke by the muting mallet lifts its mute first (the player re-strikes the bar)
            if (_dampedKind[b] == DampMallet && _dampedOwner[b] == p.Owner) { _dampedKind[b] = DampNone; Listener?.OnUndamp(b, "restrike", now); }
            Listener?.OnStrike(b, cd.Vel, cd.U, cd.C, p, late, now);
            if (p.Finger) p.PlayBar = b; else p.SetHold(b, true, now, cd.U);
            RollStep(b, p.Owner, now);
            Signals(b, cd.U, p, now);
        }

        void RestCheck(MarimbaPointer p, double now)
        {
            if (p.Finger || p.HoldBar < 0) return;
            int b = p.HoldBar;
            double3 t = p.Tip;
            double y0 = Plane(b, false);
            if (p.HoldDamped)
            {
                bool lifted = t.y > y0 + MarimbaIx.DeadLift || !InFoot(b, t, 0, FrontOf(b));
                p.LiftRun = lifted ? p.LiftRun + 1 : 0;
                if (p.LiftRun >= MarimbaIx.LiftSamples)
                {
                    if (_dampedKind[b] == DampMallet && _dampedOwner[b] == p.Owner) { _dampedKind[b] = DampNone; Listener?.OnUndamp(b, "lift", now); }
                    p.ClearHold(); p.LiftRun = 0;
                }
                return;
            }
            bool inBand = InFoot(b, t, 0, FrontOf(b)) && t.y >= y0 - MarimbaIx.BandBelow && t.y <= y0 + MarimbaIx.BandAbove;
            if (!inBand) { if (++p.HoldBreakRun >= MarimbaIx.BreakSamples) p.ClearHold(); return; }
            p.HoldBreakRun = 0;
            if (p.HoldStruck && !DeadStrokes) return;                // dead strokes off (lessons, before tip_marimba_dead)
            bool still = p.HistCount > 0 && now - p.HT(0) >= MarimbaIx.StillWinMs - 1 && Math.Abs(t.y - p.HY(0)) / ((now - p.HT(0)) / 1000) < MarimbaIx.DeadStill;
            p.HoldStillRun = still ? p.HoldStillRun + 1 : 0;
            if (now - p.HoldSince >= (p.HoldStruck ? MarimbaIx.DeadWindowMs : MarimbaIx.RestMs) && p.HoldStillRun >= MarimbaIx.StillSamples)
            {
                p.HoldDamped = true; p.LiftRun = 0;
                if (_dampedKind[b] != DampNone) return;
                _dampedKind[b] = DampMallet; _dampedOwner[b] = p.Owner;
                Listener?.OnDamp(b, MarimbaDampKind.Mallet, Math.Min(1, Math.Max(0, p.HoldU)), p.HoldStruck, p.Owner, now);
                if (p.HoldStruck) Listener?.OnSignal(MarimbaSignal.DeadStroke, now);
            }
        }

        // a resting hand: >= 2 fingertips of a hand that holds no mallet, on a bar top, slow, for 120 ms; the tip that struck
        // this bar does not count until it has risen above plane + PLAY_CLEAR; the mute holds while any tip stays near
        void HandCheck(MarimbaPointer[] pointers, int count, double now)
        {
            _bbCount = 0;
            for (int pi = 0; pi < count; pi++)
            {
                MarimbaPointer p = pointers[pi];
                if (!p.Finger || !p.Active || p.Holding || !p.HasTip) continue;
                for (int b = 0; b < NB; b++)
                {
                    if (!InFoot(b, p.Tip, 0, 0) || p.PlayBar == b) continue;
                    int k = b * MaxOwners + p.Owner;
                    double dy = p.Tip.y - Plane(b, true);
                    bool onTop = _hrOn[k] && _hrDamped[k] ? dy <= MarimbaIx.HandLift && dy >= -MarimbaIx.HandBand * 3
                        : Math.Abs(dy) <= MarimbaIx.HandBand && p.Speed < MarimbaIx.HandStill;
                    if (!onTop) continue;
                    if (_bbN[k] == 0) { _bbOrder[_bbCount++] = k; _bbU[k] = 0; }
                    _bbN[k]++;
                    _bbU[k] += (p.Tip.z - (_z[b] - _len[b] / 2)) / _len[b];
                }
            }
            for (int a = 0; a < _bbCount; a++)
            {
                int k = _bbOrder[a], bar = k / MaxOwners, owner = k % MaxOwners, n = _bbN[k];
                if (!_hrOn[k]) { _hrOn[k] = true; _hrSince[k] = now; _hrDamped[k] = false; _hrAway[k] = 0; _hrOrder[_hrCount++] = k; }
                _hrAway[k] = 0;
                if (!_hrDamped[k] && n < MarimbaIx.HandMinTips) _hrSince[k] = now;
                if (!_hrDamped[k] && n >= MarimbaIx.HandMinTips && now - _hrSince[k] >= MarimbaIx.HandMs && _dampedKind[bar] == DampNone)
                {
                    _hrDamped[k] = true; _dampedKind[bar] = DampHand; _dampedOwner[bar] = owner;
                    Listener?.OnDamp(bar, MarimbaDampKind.Hand, Math.Min(1, Math.Max(0, _bbU[k] / n)), false, owner, now);
                    Listener?.OnSignal(MarimbaSignal.Mute, now);
                }
            }
            for (int a = 0; a < _hrCount; a++)
            {
                int k = _hrOrder[a];
                if (_bbN[k] > 0) continue;
                if (_hrAway[k] == 0) _hrAway[k] = now;
                if (now - _hrAway[k] >= MarimbaIx.HandLiftMs)
                {
                    int bar = k / MaxOwners, owner = k % MaxOwners;
                    if (_hrDamped[k] && _dampedKind[bar] == DampHand && _dampedOwner[bar] == owner) { _dampedKind[bar] = DampNone; Listener?.OnUndamp(bar, "hand lifted", now); }
                    _hrOn[k] = false;
                    for (int j = a + 1; j < _hrCount; j++) _hrOrder[j - 1] = _hrOrder[j];
                    _hrCount--; a--;
                }
            }
            for (int a = 0; a < _bbCount; a++) _bbN[_bbOrder[a]] = 0;
        }

        void RollStep(int bar, int owner, double now)
        {
            if (now - _rollLast[bar] <= MarimbaIx.RollGapMs && owner != _rollOwner[bar]) _rollN[bar]++; else _rollN[bar] = 1;
            _rollLast[bar] = now; _rollOwner[bar] = owner;
            if (_rollN[bar] == MarimbaIx.RollMin) { Listener?.OnRoll(bar, now); Listener?.OnSignal(MarimbaSignal.Roll, now); }
        }

        // Sunny's signals (spec 5.6): backRow (the first stroke on a sharp), fingerOnly, offCentre (each once per session)
        void Signals(int b, double u, MarimbaPointer p, double now)
        {
            if (!_natural[b] && !_backRow) { _backRow = true; Listener?.OnSignal(MarimbaSignal.BackRow, now); }
            if (p.Finger && !_malletSeen && !_fingerOnly)
            {
                if (_ftCount == _fingerTimes.Length) { _ftStart = (_ftStart + 1) % _fingerTimes.Length; _ftCount--; }
                _fingerTimes[(_ftStart + _ftCount) % _fingerTimes.Length] = now; _ftCount++;
                while (_ftCount > 0 && now - _fingerTimes[_ftStart] > MarimbaIx.FingerOnlyMs) { _ftStart = (_ftStart + 1) % _fingerTimes.Length; _ftCount--; }
                if (_ftCount >= MarimbaIx.FingerOnlyN) { _fingerOnly = true; Listener?.OnSignal(MarimbaSignal.FingerOnly, now); }
            }
            if (_natural[b] && !_offCentre)
            {
                byte off = (byte)(u < MarimbaIx.CentreLo || u > MarimbaIx.CentreHi ? 1 : 0);
                if (_offCount == MarimbaIx.OffCentreN) { _offStart = (_offStart + 1) % MarimbaIx.OffCentreN; _offCount--; }
                _offWin[(_offStart + _offCount) % MarimbaIx.OffCentreN] = off; _offCount++;
                if (_offCount == MarimbaIx.OffCentreN)
                {
                    int sum = 0; for (int i = 0; i < _offCount; i++) sum += _offWin[i];
                    if (sum >= MarimbaIx.OffCentreShare * MarimbaIx.OffCentreN) { _offCentre = true; Listener?.OnSignal(MarimbaSignal.OffCentre, now); }
                }
            }
        }

        void Lost(MarimbaPointer p, double now, double frameMs)
        {
            p.HistClear(); p.ApprClear();
            if (p.Finger) return;
            if (p.HoldBar >= 0 && !p.HoldDamped) { p.ClearHold(); return; }
            if (p.HoldBar < 0) return;
            p.LostMs += frameMs;   // review item 19: the frame's real elapsed time
            if (p.LostMs >= MarimbaIx.LostUndampMs)
            {
                int b = p.HoldBar;
                if (_dampedKind[b] == DampMallet && _dampedOwner[b] == p.Owner) { _dampedKind[b] = DampNone; Listener?.OnUndamp(b, "tracking lost", now); }
                p.ClearHold();
            }
        }

        /// <summary>Drops every mute and hold (arrange mode, stow, a lesson start): the caller undamps the unit's bars.</summary>
        public void ReleaseAll(MarimbaPointer[] pointers, int count, double now)
        {
            for (int i = 0; i < count; i++) pointers[i].ClearHold();
            for (int b = 0; b < NB; b++) if (_dampedKind[b] != DampNone) { _dampedKind[b] = DampNone; Listener?.OnUndamp(b, "reset", now); }
            for (int k = 0; k < _hrCount; k++) _hrOn[_hrOrder[k]] = false;
            _hrCount = 0;
            for (int o = 0; o < MaxOwners; o++) _defOn[o] = false;
        }
    }
}
