// The bare-hand stroke logic of the hand drums (handdrums.md 3): a 1:1 transliteration of the Node
// reference interaction (v2: HandDrumInput.step / drumStep / vel / classify, palmLiftTarget,
// stepPalmLift), with the same constants and the same order of operations. Plain C#, driven by hand samples (14 skin
// points per hand from the joint centres, the back-of-hand axis, confidences and the tracker's sample time), so it runs
// in EditMode with scripted or seeded-noise hands and on the device from HandFrame (HandDrumsHandSample.FromFrame).
//
// What it guarantees (tested on the lab's 18 scripted cases at 4 rate pairs and its 88 seeded-noise cases):
//  - velocity is a least-squares slope over >= 50 ms of DISTINCT tracker samples, timed by their own timestamps;
//  - a stroke needs a real approach: its group armed from 20 mm up within 100 ms, its points descending, fast enough;
//  - the stroke is classified on the pose interpolated to the first crossing instant;
//  - one landing is one stroke (both groups disarm), and a hand resting, hovering or laid down slowly never strikes:
//    it rests (damps) instead; OpenXR fingertip joint centres sit ~7-8 mm inside the skin, so contact is measured on
//    the skin points (joint centre minus its radius along the back-of-hand axis), never on joint distances.
// Nothing allocates per frame: events go into a preallocated buffer that the caller drains.
using System;
using MusicRoom.Audio.Burst;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    /// <summary>interaction.mjs K (handdrums.md 3.2).</summary>
    public static class HdK
    {
        public const double Band = 0.004, Arm = 0.020, ApproachMs = 100, GroupV = 0.7, Off = 1.25, Reach = 1.08;
        public const int ArmN = 2;
        public const double V0Palm = 0.30, V0Fing = 0.25, V1 = 2.0, UExp = 0.7;
        public const double VelSpanMs = 50; public const int VelMinN = 3, VelMaxN = 6;
        public const double GapMin = 0.004, GapMax = 0.2;
        public const int DescN = 2;
        public const double LandMs = 8, LandNear = 0.012, LandPalmMs = 20, LandPalmNear = 0.020, LandFloor = 0.006;
        public const double HeelTilt = 0.015, HeelTips = 0.025, ToeGraceMs = 80;
        public const int RestVelN = 3;
        public const double CurlDeg = 30, CurlMin = 12, PalmMax = 1.0, BassRho = 0.8, TouchU = 0.25, RimRho = 0.95;
        public const int FingersOk = 3;
        public const double VRest = 0.12, RestMs = 14, RestOut = 0.010, LiftV = 0.3, LiftMs = 25, WindowMs = 40, ToeAfterMs = 60;
        public const double MuteWithinMs = 90, RestDepth = 0.030, LostHoldMs = 150, TrackedMs = 25;
        public const double LiftMax = 0.060, LiftFade = 0.090, LiftTau = 0.025, LiftRho = 1.05;
        public const double BassPadsU = 0.6;
        /// <summary>Skin radii at hand scale 1 (Docs/02-meta-hands.md): palm, wrist, then per finger index..little.</summary>
        public const double RPalm = 0.0232, RWrist = 0.0182;
        public static readonly double[] RProx = { 0.0103, 0.0112, 0.0099, 0.0085 }, RMid = { 0.008, 0.008, 0.0076, 0.0068 }, RTip = { 0.0076, 0.0076, 0.0072, 0.0064 };
        public const int NP = 14;
        public static readonly int[] Tips = { 4, 7, 10, 13 }, Mids = { 3, 6, 9, 12 }, Prox = { 2, 5, 8, 11 };
        /// <summary>u from the measured speed of the lead crossing point (palm group or fingers group).</summary>
        public static double UOf(double v, bool palm)
        {
            double v0 = palm ? V0Palm : V0Fing;
            return Math.Min(1, Math.Max(0.05, Math.Pow(Math.Max(0, (v - v0) / (V1 - v0)), UExp)));
        }
        public static double PalmLiftTarget(double d) => d <= 0 ? 0 : d <= LiftMax ? d : d < LiftFade ? (LiftMax * (LiftFade - d)) / (LiftFade - LiftMax) : 0;
        public static double StepPalmLift(double prev, double target, double dt) => target >= prev ? target : target + (prev - target) * Math.Exp(-dt / LiftTau);
        public static bool IsPalmGroup(int i) => i <= 1;
        public static bool IsStriking(int i) => i <= 1 || (i - 2) % 3 != 0;   // knuckles (2, 5, 8, 11) never arm or strike
    }

    /// <summary>Double-precision vector (the reference's arithmetic: no float rounding inside the logic).</summary>
    public struct D3
    {
        public double X, Y, Z;
        public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        /// <summary>|v| as V8's Math.hypot (the reference's norm and lengths): values over their max, Kahan-summed squares.</summary>
        public double Length => Hypot3(X, Y, Z);
        public static double Hypot2(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(a / max, ref sum, ref comp); Kahan(b / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }
        public static double Hypot3(double a, double b, double c)
        {
            a = Math.Abs(a); b = Math.Abs(b); c = Math.Abs(c);
            double max = Math.Max(Math.Max(a, b), c);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(a / max, ref sum, ref comp); Kahan(b / max, ref sum, ref comp); Kahan(c / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }
        static void Kahan(double n, ref double sum, ref double comp)
        {
            double summand = n * n - comp, pre = sum + summand;
            comp = (pre - sum) - summand;
            sum = pre;
        }
        public D3 Normalized { get { double l = Length; return new D3(X / l, Y / l, Z / l); } }
        public static D3 From(Vector3 v) => new D3(v.x, v.y, v.z);
        public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
    }

    /// <summary>A head's frame (handdrums.md 3.1): centre, radius, normal (up, tilted toward the player), e0 toward the
    /// player in the head plane (theta 0), e1 = n x e0.</summary>
    public struct HdHead
    {
        public D3 C, N, E0, E1;
        public double A;
        public static readonly double Tilt = 15 * Math.PI / 180;

        /// <summary>interaction.mjs head(drum): the seat-frame heads with the rig at its home (0, 0, 0.57).</summary>
        public static HdHead Reference(int drum)
        {
            var c = drum == 0 ? new D3(-0.185, 0.75, 0.57) : new D3(0.19, 0.755, 0.57);
            double a = drum == 0 ? 0.146 : 0.156;
            return Make(c, new D3(0, Math.Cos(Tilt), -Math.Sin(Tilt)), a);
        }

        /// <summary>A head with centre c, normal n (unit) and radius a; e0 toward -z (the player) projected on the plane.</summary>
        public static HdHead Make(D3 c, D3 n, double a, D3? toward = null)
        {
            D3 t = toward ?? new D3(0, 0, -1);
            D3 e0 = (t - n * D3.Dot(t, n)).Normalized;
            return new HdHead { C = c, N = n, A = a, E0 = e0, E1 = D3.Cross(n, e0) };
        }

        /// <summary>A world head from the rig (the module): centre and axes transformed by the rig's pose.</summary>
        public static HdHead FromRig(Transform rig, Vector3 localCentre, double a)
        {
            D3 c = D3.From(rig.TransformPoint(localCentre));
            D3 n = D3.From(rig.TransformDirection(new Vector3(0, Mathf.Cos((float)Tilt), -Mathf.Sin((float)Tilt))));
            D3 t = D3.From(rig.TransformDirection(Vector3.back));
            return Make(c, n.Normalized, a, t);
        }

        public void InHead(D3 p, out double h, out double rho, out double theta)
        {
            D3 d = p - C;
            h = D3.Dot(d, N);
            double x = D3.Dot(d, E0), y = D3.Dot(d, E1);
            rho = D3.Hypot2(x, y) / A;
            theta = Math.Atan2(y, x);
        }
    }

    /// <summary>One hand's tracker sample as the stroke logic reads it (the reference's frame.hands[h]).</summary>
    public sealed class HdHandSample
    {
        /// <summary>false = untracked (the reference's null hand).</summary>
        public bool Tracked;
        public bool HighConfidence = true;
        public readonly bool[] FingerConfidence = { true, true, true, true };
        /// <summary>A new tracker sample this frame (HandFrame.newSample; the reference: a new pose object).</summary>
        public bool NewSample = true;
        /// <summary>The time the positions are for (tracker sample time, s).</summary>
        public double SampleTime;
        public D3 Wrist, Palm, Back;
        public readonly D3[] Prox = new D3[4], Mid = new D3[4], Tip = new D3[4];
        public double HandScale = 1;
        /// <summary>OpenXR joints: the fingers' proximal, intermediate and tip (index .. little).</summary>
        static readonly int[] ProxJ = { 7, 12, 17, 22 }, MidJ = { 8, 13, 18, 23 }, TipJ = { 10, 15, 20, 25 };

        /// <summary>The device: joint centres (world), the back-of-hand axis normalize(rotation[Palm] * up) (HandFrame: +Y
        /// out of the back of the hand), the confidences and the tracker's sample time and flag.</summary>
        public void FromFrame(HandFrame f)
        {
            Tracked = f != null && f.isTracked && !f.poseInferred;
            if (!Tracked) return;
            HighConfidence = f.isHighConfidence;
            for (int k = 0; k < 4; k++) FingerConfidence[k] = f.fingerHighConfidence[k + 1];
            NewSample = f.newSample;
            SampleTime = f.sampleTime;
            HandScale = f.handScale > 0 ? f.handScale : 1;
            Wrist = D3.From(f.position[(int)HandJoint.Wrist]);
            Palm = D3.From(f.position[(int)HandJoint.Palm]);
            Back = D3.From(f.rotation[(int)HandJoint.Palm] * Vector3.up).Normalized;
            for (int k = 0; k < 4; k++) { Prox[k] = D3.From(f.position[ProxJ[k]]); Mid[k] = D3.From(f.position[MidJ[k]]); Tip[k] = D3.From(f.position[TipJ[k]]); }
        }
    }

    public enum HdOp : byte { Stroke = 0, Rest = 1, Lift = 2, Relabel = 3 }
    /// <summary>classify's kinds. Mute and SlapClosed are labels (a rest soon after an open / slap), sent as Relabel.</summary>
    public enum HdKind : byte { None = 0, Open, Bass, Slap, Heel, Toe, Touch }
    public enum HdLabel : byte { None = 0, Mute, SlapClosed }

    /// <summary>One patch of a stroke (classify's patches): part, u scale, delay, shape and up to 4 points.</summary>
    public struct HdPatch
    {
        public HandPart Part;
        public double UScale, Delay;
        public PatchShape Shape;
        public int Count;
        public double R0, T0, R1, T1, R2, T2, R3, T3;
        public double Rho(int k) => k == 0 ? R0 : k == 1 ? R1 : k == 2 ? R2 : R3;
        public double Theta(int k) => k == 0 ? T0 : k == 1 ? T1 : k == 2 ? T2 : T3;
        public void Set(int k, double r, double t) { if (k == 0) { R0 = r; T0 = t; } else if (k == 1) { R1 = r; T1 = t; } else if (k == 2) { R2 = r; T2 = t; } else { R3 = r; T3 = t; } }
        /// <summary>The mean rho of the patch's points (the recorder's pos).</summary>
        public double MeanRho { get { double s = 0; for (int k = 0; k < Count; k++) s += Rho(k); return Count > 0 ? s / Count : 0; } }
    }

    /// <summary>An event of the stroke logic (the reference's emit()).</summary>
    public struct HdEvent
    {
        public HdOp Op;
        public double Ms;
        public int Drum, Hand;
        // stroke
        public HdKind Kind;
        public HdLabel Label;
        public double U, V, TStar;
        public int PatchCount;
        public HdPatch P0, P1;
        public bool MidZone, LowFingerFallback;
        // rest (palm or heel point, finger centroid)
        public int RestCount;
        public HandPart RestPart0, RestPart1;
        public double RestRho0, RestTheta0, RestRho1, RestTheta1;
        public bool TrackingLost;
        public HdPatch Patch(int i) => i == 0 ? P0 : P1;
    }

    /// <summary>The stroke logic: two hands, two heads. Call Step once per app frame; read Events [0, Count) and Clear.</summary>
    public sealed class HandDrumsInput
    {
        public const int Capacity = 4096;
        public readonly HdEvent[] Events = new HdEvent[Capacity];
        public int Count { get; private set; }
        /// <summary>Events lost to a full buffer (the caller did not drain).</summary>
        public int Overflow { get; private set; }
        /// <summary>The visual palm lift per hand (m), handdrums.md 4.5: MetaHandVisual.SetPalmLift (phase B).</summary>
        public readonly double[] PalmLift = new double[2];
        public readonly HdHead[] Heads = { HdHead.Reference(0), HdHead.Reference(1) };

        readonly HandState[] _s = { new HandState(), new HandState() };
        readonly D3[] _world = new D3[HdK.NP];
        readonly double[] _v = new double[HdK.NP], _vr = new double[HdK.NP];
        readonly Pt[] _at = new Pt[HdK.NP];
        readonly bool[] _lands = new bool[HdK.NP];
        readonly int[] _tmpA = new int[HdK.NP], _tmpB = new int[HdK.NP], _tmpC = new int[HdK.NP];
        readonly int[] _resting = new int[HdK.NP];

        struct Pt { public double H, Rho, Theta, Vs; }

        sealed class DrumState
        {
            public bool HasPrev;
            public readonly Pt[] Prev = new Pt[HdK.NP];
            public readonly Pt[] Cur = new Pt[HdK.NP];
            public double PrevT;
            public bool ArmedPalm, ArmedFing, Lockout;
            public double ArmedAtPalm = -1e9, ArmedAtFing = -1e9;
            public double LastMs = -1e9;
            public HdKind LastKind;
            // the rest being sent (pts) and the points resting (ids, a bit mask)
            public bool Rest;
            public int RestCount;
            public HandPart RestPart0, RestPart1;
            public double RestRho0, RestTheta0, RestRho1, RestTheta1;
            public int RestIds;
            public double RestMsAcc, LiftMsAcc, ChangeMs, LastFrameMs, HeelLiftMs = -1e9;
            // the last stroke (for the mute / closed relabel)
            public bool HasLastStroke;
            public double LastStrokeMs;
            public HdKind LastStrokeKind;
            public int LastStrokeEvent = -1;
            public void Reset() { HasPrev = false; }
        }

        sealed class HandState
        {
            public double TrackedMs, LostMs;
            public int Ns;
            public readonly double[] St = new double[HdK.VelMaxN];
            public readonly double[][] Sh = { new double[HdK.VelMaxN * HdK.NP], new double[HdK.VelMaxN * HdK.NP] };
            public readonly DrumState[] D = { new DrumState(), new DrumState() };
        }

        public void Clear() => Count = 0;

        /// <summary>Forget both hands (the drums went on display, a long gap): no rests, nothing armed, no lift. Emits
        /// nothing (the caller lifts the hands on the unit). Allocates (rare).</summary>
        public void ResetHands()
        {
            Count = 0;
            for (int hd = 0; hd < 2; hd++) { _s[hd] = new HandState(); PalmLift[hd] = 0; }
        }

        /// <summary>One app frame at time t (s), hands[0] left, hands[1] right (null = untracked), dt the frame time.</summary>
        public void Step(double t, HdHandSample left, HdHandSample right, double dt)
        {
            double ms = t * 1000;
            for (int hd = 0; hd < 2; hd++)
            {
                HdHandSample H = hd == 0 ? left : right;
                HandState S = _s[hd];
                if (H == null || !H.Tracked || !H.HighConfidence)
                {
                    S.TrackedMs = 0; S.LostMs += dt * 1000; S.Ns = 0;
                    PalmLift[hd] = HdK.StepPalmLift(PalmLift[hd], 0, dt);
                    for (int di = 0; di < 2; di++)
                    {
                        DrumState D = S.D[di];
                        D.ArmedPalm = D.ArmedFing = false; D.HasPrev = false;
                        if (D.Rest && S.LostMs > HdK.LostHoldMs) { Emit(ms, HdOp.Lift, di, hd).TrackingLost = true; D.Rest = false; }
                    }
                    continue;
                }
                S.LostMs = 0;
                bool isNew = H.NewSample;
                double tS = H.SampleTime;
                SkinPoints(H);
                for (int di = 0; di < 2; di++)
                {
                    Pt[] cur = S.D[di].Cur;
                    for (int i = 0; i < HdK.NP; i++) { Heads[di].InHead(_world[i], out cur[i].H, out cur[i].Rho, out cur[i].Theta); cur[i].Vs = 0; }
                }
                if (isNew)
                {
                    double lastT = S.Ns > 0 ? S.St[(S.Ns - 1) % HdK.VelMaxN] : double.NaN;
                    if (S.Ns == 0 || tS < lastT || tS - lastT > HdK.GapMax) S.Ns = 0;
                    if (S.Ns == 0 || tS - lastT >= 1e-9)
                    {
                        int slot = S.Ns % HdK.VelMaxN;
                        S.St[slot] = S.Ns > 0 && tS - lastT < HdK.GapMin ? lastT + HdK.GapMin : tS;
                        for (int di = 0; di < 2; di++) for (int i = 0; i < HdK.NP; i++) S.Sh[di][slot * HdK.NP + i] = S.D[di].Cur[i].H;
                        S.Ns++;
                    }
                }
                S.TrackedMs += dt * 1000;
                int fingConf = 0;
                for (int k = 0; k < 4; k++) if (H.FingerConfidence[k]) fingConf++;
                double curl = TipCurlDeg(H);
                double depth = 0;
                for (int di = 0; di < 2; di++)
                    for (int i = 0; i < 2; i++) { Pt q = S.D[di].Cur[i]; if (q.Rho <= HdK.LiftRho && -q.H > depth) depth = -q.H; }
                PalmLift[hd] = HdK.StepPalmLift(PalmLift[hd], HdK.PalmLiftTarget(depth), dt);
                for (int di = 0; di < 2; di++) DrumStep(ms, tS * 1000, hd, S, S.D[di], di, fingConf, curl);
            }
        }

        void SkinPoints(HdHandSample H)
        {
            D3 back = H.Back;
            double sc = H.HandScale;
            _world[0] = H.Palm - back * (HdK.RPalm * sc);
            _world[1] = (H.Wrist + (H.Palm - H.Wrist) * 0.38) - back * (HdK.RWrist * sc);
            for (int f = 0; f < 4; f++)
            {
                _world[2 + 3 * f] = H.Prox[f] - back * (HdK.RProx[f] * sc);
                _world[3 + 3 * f] = H.Mid[f] - back * (HdK.RMid[f] * sc);
                _world[4 + 3 * f] = H.Tip[f] - back * (HdK.RTip[f] * sc);
            }
        }

        /// <summary>The mean angle (degrees) of the fingertip segments (middle -> tip joint centres) below the palm plane.</summary>
        static double TipCurlDeg(HdHandSample H)
        {
            double s = 0;
            for (int f = 0; f < 4; f++)
            {
                D3 d = H.Tip[f] - H.Mid[f];
                double l = d.Length;
                s += Math.Asin(Math.Max(-1, Math.Min(1, -D3.Dot(d, H.Back) / l)));
            }
            return (s / 4) * 180 / Math.PI;
        }

        /// <summary>The least-squares fall speed (m/s, + toward the head) of point i on drum di over the newest distinct samples:
        /// the smallest n in 3..6 spanning >= 50 ms, or exactly fixedN.</summary>
        double Vel(HandState S, int di, int i, int fixedN = 0)
        {
            if (S.Ns < HdK.VelMinN) return 0;
            int N = Math.Min(S.Ns, HdK.VelMaxN), last = (S.Ns - 1) % HdK.VelMaxN;
            double tl = S.St[last];
            int n = HdK.VelMinN;
            if (fixedN > 0) n = Math.Min(fixedN, N);
            else while (n < N && tl - S.St[(S.Ns - n) % HdK.VelMaxN] < HdK.VelSpanMs / 1000) n++;
            double mt = 0, mh = 0;
            double[] sh = S.Sh[di];
            for (int k = 0; k < n; k++) { int s = (S.Ns - 1 - k) % HdK.VelMaxN; mt += S.St[s]; mh += sh[s * HdK.NP + i]; }
            mt /= n; mh /= n;
            double a = 0, b = 0;
            for (int k = 0; k < n; k++) { int s = (S.Ns - 1 - k) % HdK.VelMaxN; double dt = S.St[s] - mt; a += dt * (sh[s * HdK.NP + i] - mh); b += dt * dt; }
            return b > 0 ? -a / b : 0;
        }

        double HMean(HandState S, int di, int i, int n, double h)
        {
            if (S.Ns < n) return h;
            double a = 0;
            for (int k = 0; k < n; k++) a += S.Sh[di][((S.Ns - 1 - k) % HdK.VelMaxN) * HdK.NP + i];
            return a / n;
        }

        bool Descending(HandState S, int di, int i)
        {
            if (S.Ns < HdK.DescN + 1) return false;
            for (int k = 0; k < HdK.DescN; k++)
            {
                int a = (S.Ns - 1 - k) % HdK.VelMaxN, b = (S.Ns - 2 - k) % HdK.VelMaxN;
                if (!(S.Sh[di][a * HdK.NP + i] < S.Sh[di][b * HdK.NP + i])) return false;
            }
            return true;
        }

        void DrumStep(double ms, double pMs, int hd, HandState S, DrumState D, int di, int fingConf, double curl)
        {
            Pt[] cur = D.Cur, prev = D.Prev;
            for (int i = 0; i < HdK.NP; i++) _v[i] = Vel(S, di, i);
            double gvPalm = 0, gvFing = 0; int np = 0, nf = 0;
            for (int i = 0; i < HdK.NP; i++)
            {
                if (!HdK.IsStriking(i)) continue;
                if (HdK.IsPalmGroup(i)) { gvPalm += _v[i]; np++; } else { gvFing += _v[i]; nf++; }
            }
            gvPalm /= np; gvFing /= nf;
            bool changed = !D.HasPrev;
            if (!changed) for (int i = 0; i < HdK.NP; i++) if (cur[i].H != prev[i].H || cur[i].Rho != prev[i].Rho) { changed = true; break; }
            bool heelResting = (D.Rest && RestHasHeel(D)) || ms - D.HeelLiftMs <= HdK.ToeGraceMs;
            // arming: after a stroke (lockout) a group re-arms only when ALL the hand's striking points are above ARM (the
            // toe's exception: while this hand's heel rests here, the fingers re-arm on their own points)
            bool groupAbovePalm = true, groupAboveFing = true;
            for (int i = 0; i < HdK.NP; i++)
            {
                if (!HdK.IsStriking(i)) continue;
                bool ab = cur[i].Rho > HdK.Off || HMean(S, di, i, HdK.ArmN, cur[i].H) > HdK.Arm;
                if (ab) continue;
                if (HdK.IsPalmGroup(i)) groupAbovePalm = false; else groupAboveFing = false;
            }
            if (groupAbovePalm && groupAboveFing) D.Lockout = false;
            if (S.TrackedMs >= HdK.TrackedMs)
            {
                if (groupAbovePalm && !D.Lockout) { D.ArmedPalm = true; D.ArmedAtPalm = pMs; }
                if (groupAboveFing && (!D.Lockout || heelResting)) { D.ArmedFing = true; D.ArmedAtFing = pMs; }
            }
            // crossings: from above the band to inside it, on the head, fast enough, descending, armed, approached
            int first = -1, lead = -1; double firstS = 0, leadV = 0;
            if (D.HasPrev && changed)
            {
                for (int i = 0; i < HdK.NP; i++)
                {
                    if (!HdK.IsStriking(i)) continue;
                    Pt p = prev[i], q = cur[i];
                    bool palm = HdK.IsPalmGroup(i);
                    if (!(p.H > HdK.Band && q.H <= HdK.Band)) continue;
                    double s = (p.H - HdK.Band) / Math.Max(1e-9, p.H - q.H), rho = p.Rho + (q.Rho - p.Rho) * s;
                    double v0 = palm ? HdK.V0Palm : HdK.V0Fing;
                    if (rho > HdK.Reach || _v[i] < v0 || !Descending(S, di, i)) continue;
                    bool armed = palm ? D.ArmedPalm : D.ArmedFing;
                    double armedAt = palm ? D.ArmedAtPalm : D.ArmedAtFing, gv = palm ? gvPalm : gvFing;
                    if (!armed || pMs - armedAt > HdK.ApproachMs || gv < HdK.GroupV * v0) continue;
                    if (first < 0 || s < firstS) { first = i; firstS = s; }
                    if (lead < 0 || _v[i] > leadV) { lead = i; leadV = _v[i]; }
                }
            }
            if (first >= 0)
            {
                double segMs = Math.Max(1e-6, pMs - D.PrevT), tStar = D.PrevT + firstS * segMs;
                // the gate used the 50 ms slope (noise); the strength uses the lead point's current speed (last 3 samples)
                double vNow = Math.Max(leadV, Vel(S, di, lead, HdK.RestVelN));
                double u = HdK.UOf(vNow, HdK.IsPalmGroup(lead));
                var ev = new HdEvent();
                HdKind kind = Classify(prev, cur, firstS, segMs, fingConf, curl, heelResting, u, ref ev);
                double since = tStar - D.LastMs;
                bool toeOk = kind == HdKind.Toe && D.LastKind == HdKind.Heel && since >= HdK.ToeAfterMs;
                if (kind != HdKind.None && (since >= HdK.WindowMs || toeOk))
                {
                    ref HdEvent e = ref Emit(ms, HdOp.Stroke, di, hd);
                    int idx = Count - 1;
                    e.Kind = kind; e.U = Round(u, 3); e.V = Round(vNow, 3); e.TStar = Round(tStar, 2);
                    e.PatchCount = ev.PatchCount; e.P0 = ev.P0; e.P1 = ev.P1; e.MidZone = ev.MidZone; e.LowFingerFallback = ev.LowFingerFallback;
                    D.LastMs = tStar; D.LastKind = kind;
                    D.HasLastStroke = true; D.LastStrokeMs = tStar; D.LastStrokeKind = kind; D.LastStrokeEvent = idx;
                }
                D.ArmedPalm = D.ArmedFing = false; D.Lockout = true;   // any pass disarms both groups, sounding or not
            }
            // rests: slow points in contact for REST_MS -> damp; nothing in contact for LIFT_MS -> lift
            double dtMs = ms - D.LastFrameMs; D.LastFrameMs = ms;
            for (int i = 0; i < HdK.NP; i++) _vr[i] = Vel(S, di, i, HdK.RestVelN);
            int nRest = 0; bool inContact = false;
            for (int i = 0; i < HdK.NP; i++)
            {
                Pt q = cur[i];
                if (q.Rho >= 1 || q.H < -HdK.RestDepth) continue;
                bool was = D.Rest && (D.RestIds & (1 << i)) != 0;
                bool rest = (q.H <= HdK.Band && Math.Abs(_vr[i]) < HdK.VRest) || (was && q.H <= HdK.RestOut && _vr[i] > -HdK.LiftV);
                if (rest) _resting[nRest++] = i;
                if (q.H <= HdK.Band || (was && q.H <= HdK.RestOut)) inContact = true;
            }
            if (nRest > 0) { D.RestMsAcc += dtMs; D.LiftMsAcc = 0; } else { D.RestMsAcc = 0; if (!inContact) D.LiftMsAcc += dtMs; }
            if (nRest > 0 && D.RestMsAcc >= HdK.RestMs)
            {
                RestPoints(cur, nRest, out int rc, out HandPart p0, out double r0, out double t0, out HandPart p1, out double r1, out double t1);
                int ids = 0; for (int k = 0; k < nRest; k++) ids |= 1 << _resting[k];
                if (D.Rest && !Moved(D, rc, p0, r0, p1, r1)) { D.RestIds |= ids; D.ChangeMs = 0; }
                else if (D.Rest && (D.ChangeMs += dtMs) < HdK.RestMs) { /* a different rest must hold REST_MS before it is sent */ }
                else
                {
                    D.ChangeMs = 0;
                    ref HdEvent e = ref Emit(ms, HdOp.Rest, di, hd);
                    e.RestCount = rc; e.RestPart0 = p0; e.RestRho0 = r0; e.RestTheta0 = t0; e.RestPart1 = p1; e.RestRho1 = r1; e.RestTheta1 = t1;
                    D.Rest = true; D.RestCount = rc; D.RestPart0 = p0; D.RestRho0 = r0; D.RestTheta0 = t0; D.RestPart1 = p1; D.RestRho1 = r1; D.RestTheta1 = t1; D.RestIds = ids;
                    if (D.HasLastStroke && ms - D.LastStrokeMs <= HdK.MuteWithinMs + 1e-6 && (D.LastStrokeKind == HdKind.Open || D.LastStrokeKind == HdKind.Slap))
                    {
                        HdLabel label = D.LastStrokeKind == HdKind.Open ? HdLabel.Mute : HdLabel.SlapClosed;
                        // the stroke went out already: a Relabel event tells the lessons / Sunny / the recorder's label
                        ref HdEvent r = ref Emit(ms, HdOp.Relabel, di, hd);
                        r.Label = label; r.TStar = Round(D.LastStrokeMs, 2); r.Kind = D.LastStrokeKind;
                        if (D.LastStrokeEvent >= 0 && D.LastStrokeEvent < Count && Events[D.LastStrokeEvent].Op == HdOp.Stroke) Events[D.LastStrokeEvent].Label = label;
                    }
                }
            }
            if (D.Rest && D.LiftMsAcc >= HdK.LiftMs)
            {
                if (RestHasHeel(D)) D.HeelLiftMs = ms;
                Emit(ms, HdOp.Lift, di, hd);
                D.Rest = false;
            }
            if (changed || !D.HasPrev)
            {
                Array.Copy(cur, prev, HdK.NP);
                D.HasPrev = true; D.PrevT = pMs;
            }
        }

        static bool RestHasHeel(DrumState D) => (D.RestCount > 0 && D.RestPart0 == HandPart.Heel) || (D.RestCount > 1 && D.RestPart1 == HandPart.Heel);

        static bool Moved(DrumState D, int rc, HandPart p0, double r0, HandPart p1, double r1)
        {
            if (rc != D.RestCount) return true;
            if (rc > 0 && (p0 != D.RestPart0 || Math.Abs(r0 - D.RestRho0) > 0.07)) return true;
            if (rc > 1 && (p1 != D.RestPart1 || Math.Abs(r1 - D.RestRho1) > 0.07)) return true;
            return false;
        }

        /// <summary>The rest's points: the palm (else the heel), and the mean of the resting finger points (tips if all are tips).</summary>
        void RestPoints(Pt[] cur, int nRest, out int rc, out HandPart p0, out double r0, out double t0, out HandPart p1, out double r1, out double t1)
        {
            rc = 0; p0 = p1 = HandPart.Palm; r0 = t0 = r1 = t1 = 0;
            int palmish = -1; bool has0 = false;
            for (int k = 0; k < nRest; k++) { int i = _resting[k]; if (i <= 1) { if (i == 0) has0 = true; if (palmish < 0) palmish = i; } }
            if (palmish >= 0)
            {
                int i = has0 ? 0 : palmish;
                p0 = i == 0 ? HandPart.Palm : HandPart.Heel; r0 = Round(cur[i].Rho, 3); t0 = Round(cur[i].Theta, 3); rc = 1;
            }
            int nf = 0; double sr = 0, st = 0; bool allTips = true;
            for (int k = 0; k < nRest; k++)
            {
                int i = _resting[k];
                if (i <= 1) continue;
                sr += cur[i].Rho; st += cur[i].Theta; nf++;
                if (Array.IndexOf(HdK.Tips, i) < 0) allTips = false;
            }
            if (nf > 0)
            {
                HandPart part = allTips ? HandPart.Tips : HandPart.Fingers;
                double rr = Round(sr / nf, 3), tt = Round(st / nf, 3);
                if (rc == 0) { p0 = part; r0 = rr; t0 = tt; rc = 1; }
                else { p1 = part; r1 = rr; t1 = tt; rc = 2; }
            }
        }

        ref HdEvent Emit(double ms, HdOp op, int drum, int hand)
        {
            int i = Count;
            if (i >= Capacity) { Overflow++; i = Capacity - 1; } else Count++;
            Events[i] = new HdEvent { Op = op, Ms = Round(ms, 1), Drum = drum, Hand = hand };
            return ref Events[i];
        }

        /// <summary>JS +x.toFixed(d) for the magnitudes here (round half away from zero on the decimal value).</summary>
        public static double Round(double x, int d) => Math.Round(x, d, MidpointRounding.AwayFromZero);

        // ---------------------------------------------------------------- classify (handdrums.md 3.4)

        /// <summary>The stroke from the pose at the first crossing instant (fraction s of the prev -> cur segment of segMs).</summary>
        HdKind Classify(Pt[] prev, Pt[] cur, double s, double segMs, int fingConf, double curl, bool heelResting, double u, ref HdEvent ev)
        {
            for (int i = 0; i < HdK.NP; i++)
            {
                Pt p = prev[i], q = cur[i];
                double vseg = (p.H - q.H) / (segMs / 1000);
                _at[i] = new Pt { H = p.H + (q.H - p.H) * s, Rho = p.Rho + (q.Rho - p.Rho) * s, Theta = p.Theta + (q.Theta - p.Theta) * s, Vs = Math.Max(vseg, _v[i]) };
                double near = i == 0 ? HdK.LandPalmNear : HdK.LandNear, win = i == 0 ? HdK.LandPalmMs : HdK.LandMs;
                Pt a = _at[i];
                _lands[i] = a.Rho < HdK.Reach && (a.H <= HdK.Band + HdK.LandFloor || (a.H <= near && a.Vs > 0 && ((a.H - HdK.Band) / a.Vs) * 1000 <= win));
            }
            int nTips = Filter(HdK.Tips, _tmpA), nMids = Filter(HdK.Mids, _tmpB);
            // fingL: [...MIDS, ...TIPS] that land, in that order
            int nFing = 0;
            for (int k = 0; k < 4; k++) if (_lands[HdK.Mids[k]]) _tmpC[nFing++] = HdK.Mids[k];
            for (int k = 0; k < 4; k++) if (_lands[HdK.Tips[k]]) _tmpC[nFing++] = HdK.Tips[k];
            double proxRho = 0, proxTh = 0;
            for (int k = 0; k < 4; k++) { proxRho += _at[HdK.Prox[k]].Rho; proxTh += _at[HdK.Prox[k]].Theta; }
            proxRho /= 4; proxTh /= 4;
            double kn = Math.Min(0.97, proxRho);
            bool fingersOk = fingConf >= HdK.FingersOk;
            Pt palm = _at[0], heel = _at[1];
            ev.PatchCount = 0;
            // 1 toe: the heel rests here and the fingers land
            if (heelResting && fingersOk && (nTips >= 2 || nMids >= 2))
            {
                AddPoints(ref ev, HandPart.Pads, 1, 0, nTips >= 2 ? _tmpA : _tmpB, nTips >= 2 ? nTips : nMids);
                return HdKind.Toe;
            }
            // 2 heel: the heel lands near the centre while the palm and the fingers are up
            if (_lands[1] && !_lands[0] && heel.Rho < 0.75 && palm.H - heel.H >= HdK.HeelTilt && MeanH(HdK.Tips) >= HdK.HeelTips)
            {
                AddPoint(ref ev, HandPart.Heel, 1, 0, heel.Rho, heel.Theta);
                return HdKind.Heel;
            }
            // 3 palm: wherever it lands on the head; the landing fingers ride along
            if (_lands[0] && palm.Rho <= HdK.PalmMax)
            {
                AddPoint(ref ev, HandPart.Palm, 1, 0, palm.Rho, palm.Theta);
                if (nFing >= 2)
                {
                    if (palm.Rho <= HdK.BassRho) { if (fingersOk) { if (nTips >= 2) AddPoints(ref ev, HandPart.Pads, HdK.BassPadsU, 0.004, _tmpA, nTips); else AddPoints(ref ev, HandPart.Pads, HdK.BassPadsU, 0.004, _tmpC, nFing); } }
                    else { if (nTips > 0) AddLine(ref ev, kn, proxTh, _tmpA, nTips); else AddLine(ref ev, kn, proxTh, _tmpB, nMids); }
                }
                return palm.Rho <= HdK.BassRho ? HdKind.Bass : HdKind.Open;
            }
            // 4 slap: the fingertips land first (cupped), the knuckles at the rim
            if (nTips >= 2 && proxRho >= 0.7 && ((nMids <= 1 && curl >= HdK.CurlMin) || curl >= HdK.CurlDeg))
            {
                if (fingersOk)
                {
                    AddPoint(ref ev, HandPart.Base, 0.8, 0, kn, proxTh);
                    AddPoints(ref ev, HandPart.Tips, 1, 0.0008, _tmpA, nTips);
                    return HdKind.Slap;
                }
                AddLine(ref ev, kn, proxTh, _tmpA, nTips);
                ev.LowFingerFallback = true;
                return HdKind.Open;
            }
            // 5 flat fingers: a fingers line from the knuckles to the landing fingers (rim knock: touch)
            int inside = 0;
            for (int k = 0; k < nFing; k++) if (_at[_tmpC[k]].Rho < HdK.RimRho) inside++;
            if (nTips + nMids >= 3 && proxRho <= 1.35)
            {
                if (nTips > 0) AddLine(ref ev, kn, proxTh, _tmpA, nTips); else AddLine(ref ev, kn, proxTh, _tmpB, nMids);
                ev.MidZone = proxRho < 0.7;
                return inside >= 2 ? HdKind.Open : HdKind.Touch;
            }
            // 6 anything else that lands: a soft touch, or the landing part itself
            int nOn = 0, nF = 0;
            for (int i = 0; i < HdK.NP; i++) if (_lands[i]) { _tmpB[nOn++] = i; if (i > 1) _tmpC[nF++] = i; }
            if (nOn == 0) return HdKind.None;
            if (u < HdK.TouchU)
            {
                int i = nF > 0 ? _tmpC[0] : _tmpB[0];
                AddPoint(ref ev, HandPart.Pads, 1, 0, _at[i].Rho, _at[i].Theta);
                return HdKind.Touch;
            }
            if (nF >= 2)
            {
                int ins = 0;
                for (int k = 0; k < nF; k++) if (_at[_tmpC[k]].Rho < HdK.RimRho) ins++;
                // f.sort((a, b) => at[b].rho - at[a].rho): stable, descending rho
                for (int a = 1; a < nF; a++) { int x = _tmpC[a], j = a - 1; while (j >= 0 && _at[_tmpC[j]].Rho < _at[x].Rho) { _tmpC[j + 1] = _tmpC[j]; j--; } _tmpC[j + 1] = x; }
                AddPoints(ref ev, HandPart.Fingers, 1, 0, _tmpC, nF);
                return ins >= 2 ? HdKind.Open : HdKind.Touch;
            }
            if (nF == 1)
            {
                AddPoint(ref ev, HandPart.Pads, 1, 0, _at[_tmpC[0]].Rho, _at[_tmpC[0]].Theta);
                return HdKind.Touch;
            }
            int on0 = _tmpB[0];
            AddPoint(ref ev, on0 == 0 ? HandPart.Palm : HandPart.Heel, 1, 0, _at[on0].Rho, _at[on0].Theta);
            return _at[on0].Rho <= HdK.BassRho ? (on0 == 1 ? HdKind.Heel : HdKind.Bass) : HdKind.Open;
        }

        int Filter(int[] ids, int[] dst) { int n = 0; foreach (int i in ids) if (_lands[i]) dst[n++] = i; return n; }
        double MeanH(int[] ids) { double s = 0; foreach (int i in ids) s += _at[i].H; return s / ids.Length; }

        static ref HdPatch NewPatch(ref HdEvent ev)
        {
            if (ev.PatchCount == 0) { ev.PatchCount = 1; ev.P0 = default; return ref ev.P0; }
            ev.PatchCount = 2; ev.P1 = default; return ref ev.P1;
        }

        static void AddPoint(ref HdEvent ev, HandPart part, double uScale, double delay, double rho, double theta)
        {
            ref HdPatch p = ref NewPatch(ref ev);
            p.Part = part; p.UScale = uScale; p.Delay = delay; p.Shape = PatchShape.Points; p.Count = 1;
            p.Set(0, Round(rho, 3), Round(theta, 3));
        }

        /// <summary>P3(ids): at most 3 explicit points, in the given order.</summary>
        void AddPoints(ref HdEvent ev, HandPart part, double uScale, double delay, int[] ids, int n)
        {
            ref HdPatch p = ref NewPatch(ref ev);
            p.Part = part; p.UScale = uScale; p.Delay = delay; p.Shape = PatchShape.Points; p.Count = Math.Min(3, n);
            for (int k = 0; k < p.Count; k++) p.Set(k, Round(_at[ids[k]].Rho, 3), Round(_at[ids[k]].Theta, 3));
        }

        /// <summary>line(ids): 4 points from the knuckles (rho &lt;= .97) to the mean of the landing fingers.</summary>
        void AddLine(ref HdEvent ev, double kn, double proxTh, int[] ids, int n)
        {
            double r1 = 0, th = 0;
            for (int k = 0; k < n; k++) { r1 += _at[ids[k]].Rho; th += _at[ids[k]].Theta; }
            r1 /= n; th /= n;
            ref HdPatch p = ref NewPatch(ref ev);
            p.Part = HandPart.Fingers; p.UScale = 1; p.Delay = 0; p.Shape = PatchShape.Line; p.Count = 4;
            for (int k = 0; k < 4; k++) p.Set(k, Round(kn + (r1 - kn) * (k / 3.0), 3), Round(proxTh + (th - proxTh) * (k / 3.0), 3));
        }

        // ---------------------------------------------------------------- sending a stroke (the module's job; sendStroke)

        /// <summary>sendStroke(eng, at, drum, hand, st): one Patch per patch, u x uScale (at most 1), the patch delay kept.
        /// A line goes as its two ends (the unit spreads 4 points between them, as classify's line does).</summary>
        public static void Send(IHandDrumSound sound, in HdEvent e, double when)
        {
            if (sound == null || e.Op != HdOp.Stroke) return;
            for (int i = 0; i < e.PatchCount; i++)
            {
                HdPatch p = e.Patch(i);
                float u = (float)Math.Min(1, e.U * p.UScale);
                var q0 = new PatchPoint { Rho = (float)p.R0, Theta = (float)p.T0 };
                if (p.Shape == PatchShape.Line)
                    sound.Patch(e.Drum, e.Hand, p.Part, u, PatchShape.Line, q0, new PatchPoint { Rho = (float)p.R3, Theta = (float)p.T3 }, default, 2, when, (float)p.Delay);
                else
                    sound.Patch(e.Drum, e.Hand, p.Part, u, PatchShape.Points, q0, new PatchPoint { Rho = (float)p.R1, Theta = (float)p.T1 }, new PatchPoint { Rho = (float)p.R2, Theta = (float)p.T2 }, p.Count, when, (float)p.Delay);
            }
        }

        /// <summary>A rest event onto the unit: up to 2 points (palm or heel; the finger centroid).</summary>
        public static void SendRest(IHandDrumSound sound, in HdEvent e, double when)
        {
            if (sound == null) return;
            if (e.Op == HdOp.Lift) { sound.Lift(e.Drum, e.Hand, when); return; }
            if (e.Op != HdOp.Rest) return;
            var a = new RestPoint { Part = e.RestPart0, Rho = (float)e.RestRho0, Theta = (float)e.RestTheta0 };
            var b = new RestPoint { Part = e.RestPart1, Rho = (float)e.RestRho1, Theta = (float)e.RestTheta1 };
            sound.Rest(e.Drum, e.Hand, a, b, default, e.RestCount, when);
        }
    }
}
