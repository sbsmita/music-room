// BowlsHands: the bowls' hand logic (bowls.md section 4), transliterated from the Node reference interaction, revision 2
// (double maths, the same order, so the scripted-hand cases reproduce the reference's calls). Plain C#: it gets one
// BowlsHandInput per hand per display frame and calls an IBowlsHandsSink (the sound unit's Strike / Contact / Release /
// Damp, plus classification events for lessons, Sunny and the recorder). No Unity objects; nothing allocates per frame.
//
// Rules (revision 2 + the phase-A review items):
//  - Tap speed = the peak inward normal speed over the last 3 tracker samples; TAP_SPEED 0.12 m/s (a gentle poke rings).
//  - A touching tip becomes a rub only when it moves along the rim (|v_f| >= 0.05 m/s for 0.1 s). A still fingertip never
//    damps within 0.6 s of its entry; after that it is Damp(1). The full stop is the palm (Damp 3).
//  - While a bowl is rubbed, its other fingertips damp only if still and >= 8 mm deep for > 0.4 s; the rubbing hand's
//    other tips land on it silently.
//  - A rub contact is sticky (15 mm outside the surface, then a 0.25 s lift hold); a re-entry within 0.4 s continues it.
//  - Swept entry test (2 mm steps) on the display-time positions.
//  - Review item 13: a fingertip on top of the lip is sent to the unit as an OUTER contact (LipAsOuter): the pad on a
//    3-4 mm lip wraps its outer edge, and the unit's lip model started the big bowl in 1.6-4.4 s or never (outer: 0.7-1.1 s).
//  - Review item 19: a hand that rubs one bowl does not touch the others with its other fingertips (a "ghost" contact,
//    no calls, whatever the speed: a splayed ring finger sweeping past a neighbour at 0.7 turns/s enters it at 0.66 m/s).
//    Tapping another bowl while rubbing is done with the other hand.
//  - Phase A: a fingertip that just struck one bowl is silent on the others for 0.15 s (the display-time prediction of
//    its recoil reaches 8 cm at 2 m/s, past a 3-6 cm gap), and the glitch test reads the full tip speed.
//  - Rub smoothing (RubSmoothing, on): the headset's tracker gives a new sample every 20-40 ms, so the force (from the
//    depth) and the speed along the rim moved in steps, and a tracker jump made one huge speed. Steps in the force made the
//    stick bounce off the wall and slap back, and speed spikes jerked the friction: the "broken cassette" crackle while
//    rubbing. So the angular speed estimate rejects a jump (one sample's rate is clamped to OMEGA_JUMP of the running value
//    and to VF_MAX along the rim), and what goes to the unit is conditioned per frame: |v_f| <= VF_MAX, its change limited
//    to VF_SLEW, the force a one-pole (N_TAU) with its change limited to N_SLEW, and the contact angle carried on by the
//    speed and pulled toward the measured one (A_TAU; its jitter moved the stick along the wall in steps). The unit ramps each frame's values per
//    sample (512 samples), so the force and speed it sees are continuous. Classification (rub start, the rub mean) reads
//    the raw speed.
//  - Smooth contact (SmoothContact, on): the second headset report (crackle worst on the outer side or just outside the
//    bowl, the stick hard to circle). A rub that grazes the outer edge left the wall by more than the sticky band and sat
//    in the lift hold at N_MIN against a loud bowl: the stick bounced off the vibrating wall (chatter) many times a second.
//    Now the excitation has an envelope: a contact starts at zero force and swells in (the N slew), the force eases off
//    from GRAZE_KNEE outside the wall to GRAZE_FLOOR x at the sticky band's edge, and in the lift hold it fades to zero
//    (HOLD_TAU) instead of holding N_MIN; the height on the wall (hs) is smoothed like the angle (its steps moved the
//    contact's mode shapes in steps). A touching tip leaves only EXIT_TOUCH outside (tracker jitter is +-3 mm, so at the
//    reference's -3 mm a resting or grazing finger touched and left many times a second), and a re-entry within
//    RETOUCH_QUIET of its exit that never went GRAZE_GAP away is the same touch: no tap, no tick (60 Hz jitter alone reads
//    0.3 m/s inward). The puja's shaft point is the continuous deepest point (a search along the shaft, not 11 fixed
//    samples 11 mm apart), held on the bowl and side it rubs, and kept at its last place along the shaft when nothing
//    touches (no jump to the middle).
using System;
using Unity.Mathematics;

namespace MusicRoom.Bowls
{
    public enum BowlsHandEventType : byte { Tap, Touch, RubContact, RubResume, RubStart, RubEnd, Lift, Damp, PalmStrike }

    public struct BowlsHandEvent
    {
        public double T;
        public BowlsHandEventType Type;
        public int Bowl, Finger, Hand, Level;
        /// <summary>Tap: the strike speed; RubStart: the mean |v_f| over 0.3 s.</summary>
        public double V, N;
        public MusicRoom.Audio.Burst.BowlStrikeKind StrikeKind;
        /// <summary>The surface the tip is on (Lip stays Lip here even when the unit gets an Outer contact).</summary>
        public MusicRoom.Audio.Burst.BowlSurface Surface;
        /// <summary>RubStart from the puja's shaft.</summary>
        public bool Tool;
    }

    public interface IBowlsHandsSink
    {
        void Strike(int bowl, MusicRoom.Audio.Burst.BowlStrikeKind kind, double V, double angle, double hs);
        void Contact(int bowl, MusicRoom.Audio.Burst.BowlSurface kind, double N, double vf, double angle, double omega, double hs);
        void Release(int bowl);
        void Damp(int bowl, int level);
        void Event(in BowlsHandEvent e);
    }

    /// <summary>One hand at one display frame: fingertip sphere centres (world, m; OpenXR tip joints, NOT the Pointers' index-first
    /// order: 0 thumb, 1 index, 2 middle, 3 ring, 4 little), radii, palm, the tracker sample time, optional tip velocities
    /// (HandFrame.tipVelocity) and the held puja (A = the suede end's centre, B = the plain end's centre).</summary>
    public sealed class BowlsHandInput
    {
        public bool Tracked;
        public readonly double3[] Tips = new double3[5];
        public readonly bool[] TipValid = new bool[5];
        public readonly double[] TipR = new double[5];
        public bool HasTipVel;
        public readonly double3[] TipVel = new double3[5];
        public bool HasPalm;
        public double3 PalmC, PalmN;
        public double SampleTime;
        /// <summary>The positions are already the runtime's display-time prediction (Unity HandFrame.position[]).</summary>
        public bool Predicted;
        public bool HasTool;
        public double3 ToolA, ToolB;

        public BowlsHandInput() { for (int f = 0; f < 5; f++) TipR[f] = BowlsHands.IX.TIP_R; }
    }

    /// <summary>A bowl as the hand logic sees it: its base centre (world), yaw (rad, about +y) and shape.</summary>
    public struct BowlsHandBowl
    {
        public double3 Pos;
        public double Yaw;
        public double R, H, T;
        public int Index;
    }

    public sealed class BowlsHands
    {
        /// <summary>The reference's IX constants (interaction.mjs).</summary>
        public static class IX
        {
            public const double TIP_R = 0.0076, BAND = 0.030, ENTER = 0.0, EXIT = -0.003, EXIT_RUB = -0.015, LIFT_HOLD = 0.25;
            public const double REENTRY_QUIET = 0.4, TAP_SPEED = 0.12, TOUCH_MIN = 0.03, GLITCH_SPEED = 3.5;
            public const int VEL_HISTORY = 3, FRESH_FRAMES = 2;
            public const double CONVERT_SPEED = 0.05, CONVERT_TIME = 0.10, REST_GRACE = 0.6, REST_STILL = 0.3, STILL_RADIUS = 0.006;
            public const double EXTRA_DEPTH = 0.008, EXTRA_TIME = 0.4, TAP_DEBOUNCE = 0.12, TAP_V_MAX = 3.0, PALM_V_MAX = 2.0;
            public const double N_MIN = 2.5, N_MAX = 8.0, N_SLOPE = 250, N_DEAD = 0.002, OMEGA_TAU = 0.06, V_DEAD = 0.03;
            public const double RUB_MIN = 0.05, RUB_WINDOW = 0.3, LOSS_HOLD = 0.12, REACQUIRE = 0.25, REACQUIRE_DIST = 0.03;
            public const double SWEEP_STEP = 0.002, EXIT_BAND = 0.04, RETOUCH_QUIET = 0.3, PREDICT_MAX = 0.040, PREDICT_MIN_SPEED = 0.3;
            public const double PALM_DIST = 0.025, PALM_COS = 0.5, PALM_SPEED = 0.3, PALM_REST = 0.25;
            public const double PUJA_R = 0.011, PUJA_SUEDE_R = 0.0135, WOOD_SPEED = 0.25;
            /// <summary>Phase A: a fingertip that just struck one bowl recoils; its entries into another bowl within this
            /// time are silent (the display-time prediction of a 2 m/s recoil reaches 8 cm, past a 3-6 cm gap).</summary>
            public const double RECOIL_QUIET = 0.15;
            /// <summary>Rub smoothing: the largest finger speed along the rim sent (m/s; a fast rub is 0.6-0.8), its change per
            /// second (m/s^2), the force's time constant (s) and its change per second (N/s), and how far one tracker sample's
            /// angular rate may be from the running estimate (rad/s) before it counts as a jump and is clamped.</summary>
            public const double VF_MAX = 1.2, VF_SLEW = 3.0, N_TAU = 0.05, N_SLEW = 40.0, OMEGA_JUMP = 6.0, A_TAU = 0.05;
            /// <summary>Smooth contact: a touching tip's exit (m outside; the reference's EXIT is -3 mm), how far out a tip must go
            /// between two entries for the second to strike (m), where the rub's force starts easing off outside the wall (m)
            /// and the fraction left at the sticky band's edge (EXIT_RUB), the lift hold's fade (s), the height's smoothing (s).</summary>
            public const double EXIT_TOUCH = -0.006, GRAZE_GAP = 0.006, GRAZE_KNEE = -0.008, GRAZE_FLOOR = 0.6, HOLD_TAU = 0.05, HS_TAU = 0.08, TOOL_V_TAU = 0.02;
        }

        const int SideNone = -1, SideOutside = 0, SideInside = 1, SideAbove = 2;
        enum Mode : byte { Free, Touch, Contact, Struck, Ghost }
        enum TipKind : byte { Finger, Shaft, Suede }
        const int Slots = 7, RubCap = 256;

        sealed class Tip
        {
            public Mode Mode;
            public TipKind Kind;
            public int Bowl = -1, Tracked, HandIdx = -1;
            public double Lost;
            public bool HasLastPos; public double3 LastPos;
            public bool HasSample; public double3 Sample; public double SampleT = -1;
            public double3 Vel;
            public readonly double3[] VHist = new double3[IX.VEL_HISTORY]; public int VCount;
            public readonly int[] Side = { SideNone, SideNone, SideNone };
            public int EntrySide = SideNone; public double EntryT;
            public bool Tapped;
            public readonly double[] LastTap = { -1e9, -1e9, -1e9 }, LastExit = { -1e9, -1e9, -1e9 };
            public double Omega; public bool HasLastAngle; public double LastAngle, AngleT, MoveT, StillT, DeepT;
            public readonly double[] RubT = new double[RubCap], RubV = new double[RubCap]; public int RubHead, RubCount;
            public bool Rubbing;
            public bool HasContactPt; public double3 ContactPt;
            public double HoldSince; public bool HasHoldAngle; public double HoldAngle;
            public bool HasAnchor; public double3 Anchor;
            public int ContactKind = -1; public double ContactHs = double.NaN;
            public double Pen;
            public double LastStrikeT = -1e9; public int LastStrikeBowl = -1;
            public int SurfaceKind; public double RubMean;
            // rub smoothing: what was last sent to the unit (valid while SentOn)
            public bool SentOn; public double SentN, SentVf, SentAngle, SentHs;
            // smooth contact: per bowl, went GRAZE_GAP away since its last exit; the shaft point's place along the shaft
            public readonly bool[] FarSinceExit = { true, true, true };
            public double ShaftU = 0.6;

            public void ClearSide() { Side[0] = Side[1] = Side[2] = SideNone; }
            public void RubClear() { RubHead = 0; RubCount = 0; }
            public void RubPush(double t, double v) { int k = (RubHead + RubCount) % RubCap; RubT[k] = t; RubV[k] = v; if (RubCount < RubCap) RubCount++; else RubHead = (RubHead + 1) % RubCap; }
            public double RubFirstT => RubT[RubHead];
            public void RubShift() { RubHead = (RubHead + 1) % RubCap; RubCount--; }
        }

        sealed class Palm { public int OnBowl = -1; public double Since, LastT = -1, V; public bool HasLastPos; public double3 LastPos; }
        sealed class Tool { public bool HasMid; public double3 Mid; public double T = -1; public double3 V; }

        struct Q { public int Kind; public double Pen, Angle, Rho, Hs; public double3 Normal; public int Side; }

        readonly BowlsHandBowl[] _bowls;
        readonly IBowlsHandsSink _sink;
        readonly Tip[] _tips = new Tip[2 * Slots];
        readonly Tip[] _primary = new Tip[3];
        readonly int[] _damp = new int[3];
        readonly double[] _lastRelease = { -1e9, -1e9, -1e9 };
        readonly Palm[] _palm = { new Palm(), new Palm() };
        readonly Tool[] _tool = { new Tool(), new Tool() };
        double _t;

        /// <summary>Review item 13 (default on): a lip contact goes to the unit as an Outer contact. Off = the reference.</summary>
        public bool LipAsOuter = true;
        /// <summary>Review item 19 (default on): ghost contacts for a rubbing hand's other tips on other bowls. Off = the reference.</summary>
        public bool QuietNeighbours = true;
        /// <summary>Phase A (default on): the recoil guard (RECOIL_QUIET) and the glitch test on the full tip speed (a
        /// tracking jump that crosses a bowl tangentially is not a tap). Off = the reference.</summary>
        public bool RecoilGuard = true;
        /// <summary>Rub smoothing (see the header): on by default; off sends the raw per-frame values (the reference's).</summary>
        public bool RubSmoothing = true;
        /// <summary>Smooth contact (see the header): on by default; off = the rub-smoothing build's contact rules.</summary>
        public bool SmoothContact = true;

        public BowlsHands(BowlsHandBowl[] bowls, IBowlsHandsSink sink)
        {
            _bowls = bowls; _sink = sink;
            for (int k = 0; k < _tips.Length; k++) _tips[k] = new Tip();
        }

        /// <summary>The damping level last sent per bowl.</summary>
        public int DampLevel(int bowl) => _damp[bowl];
        /// <summary>The hand (0 left, 1 right) rubbing bowl i, -1 if none; and whether it is rubbing by the lesson rule.</summary>
        public int RubbingHand(int bowl) => _primary[bowl] != null ? _primary[bowl].HandIdx : -1;
        public bool Rubbing(int bowl) => _primary[bowl] != null && _primary[bowl].Rubbing;
        /// <summary>The surface the rubbing tip of bowl i is on (Outer when none), and its mean |v_f| over the last 0.3 s.</summary>
        public MusicRoom.Audio.Burst.BowlSurface RubSurface(int bowl) => _primary[bowl] != null ? (MusicRoom.Audio.Burst.BowlSurface)_primary[bowl].SurfaceKind : MusicRoom.Audio.Burst.BowlSurface.Outer;
        public double RubSpeed(int bowl) => _primary[bowl] != null ? _primary[bowl].RubMean : 0;
        /// <summary>The normal force (N) last sent for bowl i's rub, 0 when none.</summary>
        public double RubForce(int bowl) => _primary[bowl] != null && _primary[bowl].SentOn ? _primary[bowl].SentN : 0;
        public bool AnyTouch(int bowl) { for (int k = 0; k < _tips.Length; k++) if (_tips[k].Mode != Mode.Free && _tips[k].Mode != Mode.Ghost && _tips[k].Bowl == bowl) return true; return false; }

        /// <summary>The puja's shaft rubbing a bowl for hand h (the drawn stick rides the rim): the shaft point the hand logic
        /// used this frame and the point on the bowl's surface it is held to (the smoothed contact angle and height, the
        /// shaft's radius off the wall), both in the hands' frame. False when the shaft is not in a rub contact.</summary>
        public bool ShaftOnRim(int h, out double3 shaftPoint, out double3 surfacePoint)
        {
            shaftPoint = surfacePoint = default;
            if (h < 0 || h > 1) return false;
            Tip st = _tips[h * Slots + 5];
            if (st.Mode != Mode.Contact || st.Bowl < 0 || !st.HasLastPos || !st.SentOn) return false;
            BowlsHandBowl b = _bowls[st.Bowl];
            double hs = st.SentHs > 0 ? st.SentHs : 1, y = Math.Max(0, Math.Min(b.H, b.H - 2 * b.H * (1 - hs)));
            double a = st.SentAngle, rho;
            if (st.SurfaceKind == 1) { rho = b.R - 0.5 * b.T; y = b.H + IX.PUJA_R; }
            else if (st.SurfaceKind == 2) rho = ROut(b, y) - b.T - IX.PUJA_R;
            else rho = ROut(b, y) + IX.PUJA_R;
            surfacePoint = ToWorld(b, new double3(rho * Math.Cos(a), y, rho * Math.Sin(a)));
            shaftPoint = st.LastPos;
            return true;
        }

        /// <summary>Lets go of everything (system gesture, arrange mode, glide, stowed): rub contacts release.</summary>
        public void ReleaseAll(double t)
        {
            for (int k = 0; k < _tips.Length; k++) { Tip st = _tips[k]; if (st.Mode != Mode.Free) { EndTouch(st, t); st.Mode = Mode.Free; st.ClearSide(); st.HasLastPos = false; } }
            for (int h = 0; h < 2; h++) _palm[h].OnBowl = -1;
            DampFrame(t);
        }

        // ------------------------------------------------------------------------------------------ geometry

        public static double ROut(in BowlsHandBowl b, double y) => b.R * (0.55 + 0.45 * Math.Sin(0.5 * Math.PI * Math.Max(0, Math.Min(1, y / b.H))));

        double3 ToLocal(in BowlsHandBowl b, double3 p)
        {
            double3 d = p - b.Pos; double c = Math.Cos(-b.Yaw), s = Math.Sin(-b.Yaw);
            return new double3(c * d.x + s * d.z, d.y, -s * d.x + c * d.z);
        }
        static double3 ToWorldDir(in BowlsHandBowl b, double3 d)
        {
            double c = Math.Cos(b.Yaw), s = Math.Sin(b.Yaw);
            return new double3(c * d.x + s * d.z, d.y, -s * d.x + c * d.z);
        }
        static double3 ToWorld(in BowlsHandBowl b, double3 p) { double3 d = ToWorldDir(b, p); return d + b.Pos; }

        // V8's Math.hypot (Kahan-compensated), so the reference's decisions reproduce to the last bit
        public static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double max = Math.Max(a, b); if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(a / max, ref sum, ref comp); Kahan(b / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }
        public static double Hypot(double a, double b, double c)
        {
            a = Math.Abs(a); b = Math.Abs(b); c = Math.Abs(c);
            double max = Math.Max(Math.Max(a, b), c); if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(a / max, ref sum, ref comp); Kahan(b / max, ref sum, ref comp); Kahan(c / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }
        static void Kahan(double n, ref double sum, ref double comp) { double summand = n * n - comp; double pre = sum + summand; comp = (pre - sum) - summand; sum = pre; }
        static double Len(double3 a) => Hypot(a.x, a.y, a.z);
        static double Dot(double3 a, double3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        /// <summary>surfaceQuery(): which surface a sphere centre p (bowl frame) is on, its penetration (> 0 inside the
        /// solid), outward normal, angle, radius, height factor, and which side of the bowl it is on now.</summary>
        static Q Surface(in BowlsHandBowl b, double3 p, double tipR, int side, double band)
        {
            double R = b.R, H = b.H, t = b.T;
            double rho = Hypot(p.x, p.z), ang = Math.Atan2(p.z, p.x), y = p.y;
            double yc = Math.Min(H, Math.Max(0, y));
            int here = y > H + 0.002 ? SideAbove : rho > ROut(b, yc) - 0.5 * t ? SideOutside : SideInside;
            var q = new Q { Kind = -1, Pen = -1, Angle = ang, Rho = rho, Hs = 1, Side = here };
            int from = side != SideNone ? side : here;
            if (y > H - band - tipR && y < H + tipR + 0.004 && rho < R + 2 * tipR + 0.03)
            {
                if (from == SideAbove && rho > R - t - tipR * 0.5 && rho < R + tipR * 0.5) { q.Kind = 1; q.Pen = (H + tipR) - y; q.Normal = new double3(0, 1, 0); }
                else if (from == SideInside || (from == SideAbove && rho < R - t)) { q.Kind = 2; q.Pen = rho - ((ROut(b, yc) - t) - tipR); q.Normal = new double3(-Math.Cos(ang), 0, -Math.Sin(ang)); }
                else { q.Kind = 0; q.Pen = ROut(b, yc) + tipR - rho; q.Normal = new double3(Math.Cos(ang), 0, Math.Sin(ang)); }
                q.Hs = 1 - 0.5 * Math.Max(0, H - yc) / H;
            }
            return q;
        }
        static int SideOfKind(int k) => k == 1 ? SideAbove : k == 2 ? SideInside : SideOutside;

        int UnitKind(int surfaceKind) => LipAsOuter && surfaceKind == 1 ? 0 : surfaceKind;

        // ------------------------------------------------------------------------------------------ frame

        public void Frame(double t, BowlsHandInput[] hands)
        {
            double dt = Math.Max(0, t - _t); _t = t;
            for (int h = 0; h < hands.Length && h < 2; h++)
            {
                BowlsHandInput hand = hands[h];
                if (hand == null) continue;
                for (int f = 0; f < 5; f++)
                    TipFrame(h, f, hand, hand.TipValid[f], hand.Tips[f], hand.TipR[f], t, dt, hand.HasTipVel, hand.TipVel[f], TipKind.Finger);
                if (hand.HasTool) ToolFrame(h, hand, t, dt);
                else for (int k = 5; k <= 6; k++) { Tip st = _tips[h * Slots + k]; if (st.Mode != Mode.Free) { EndTouch(st, t); st.Mode = Mode.Free; st.ClearSide(); st.HasLastPos = false; } }
                PalmFrame(h, hand, t);
            }
            DampFrame(t);
        }

        // the puja: its shaft is tip slot 5 (the point of the shaft deepest in the nearest bowl surface), its suede end slot 6
        void ToolFrame(int h, BowlsHandInput hand, double t, double dt)
        {
            double3 a = hand.ToolA, b = hand.ToolB;
            double3 shaftP;
            if (SmoothContact) shaftP = ShaftPoint(_tips[h * Slots + 5], a, b);
            else
            {
                bool found = false; double bestPen = 0; double3 bestP = default;
                for (int k = 0; k <= 10; k++)
                {
                    double u = 0.25 + 0.75 * k / 10.0;
                    double3 p = a + (b - a) * u;
                    for (int i = 0; i < _bowls.Length; i++)
                    {
                        Q q = Surface(_bowls[i], ToLocal(_bowls[i], p), IX.PUJA_R, SideNone, IX.BAND);
                        if (q.Kind >= 0 && (!found || q.Pen > bestPen)) { found = true; bestPen = q.Pen; bestP = p; }
                    }
                }
                shaftP = found ? bestP : a + (b - a) * 0.6;
            }
            Tool tl = _tool[h];
            double3 mid = (a + b) * 0.5;
            if (hand.SampleTime != tl.T)
            {
                if (tl.HasMid && hand.SampleTime > tl.T)
                {
                    double sdt = Math.Max(0.004, hand.SampleTime - tl.T);
                    double3 v = (mid - tl.Mid) * (1 / sdt);
                    // smooth contact: the stick's speed over a few samples (60 Hz jitter alone read as a 0.25 m/s wood strike)
                    tl.V = SmoothContact ? tl.V + (1 - Math.Exp(-sdt / IX.TOOL_V_TAU)) * (v - tl.V) : v;
                }
                tl.Mid = mid; tl.HasMid = true; tl.T = hand.SampleTime;
            }
            TipFrame(h, 5, hand, true, shaftP, IX.PUJA_R, t, dt, true, tl.V, TipKind.Shaft);
            TipFrame(h, 6, hand, true, a, IX.PUJA_SUEDE_R, t, dt, true, tl.V, TipKind.Suede);
        }

        /// <summary>Smooth contact: the shaft's deepest point (u in [0.25, 1] from the suede end): the 11 samples pick the bowl
        /// and the bracket, a ternary search refines it (the penetration is unimodal along a straight shaft near one bowl). A
        /// shaft in contact or touching searches only its bowl, from the side it entered; nothing in reach keeps the last u.</summary>
        double3 ShaftPoint(Tip st, double3 a, double3 b)
        {
            bool held = st.Mode != Mode.Free && st.Bowl >= 0;
            int bi = -1; double bestPen = double.NegativeInfinity, bu = st.ShaftU;
            for (int k = 0; k <= 10; k++)
            {
                double u = 0.25 + 0.75 * k / 10.0;
                double3 p = a + (b - a) * u;
                for (int i = 0; i < _bowls.Length; i++)
                {
                    if (held && i != st.Bowl) continue;
                    Q q = Surface(_bowls[i], ToLocal(_bowls[i], p), IX.PUJA_R, held ? st.EntrySide : SideNone, held ? IX.BAND + IX.EXIT_BAND : IX.BAND);
                    if (q.Kind >= 0 && q.Pen > bestPen) { bi = i; bestPen = q.Pen; bu = u; }
                }
            }
            if (bi < 0) return a + (b - a) * st.ShaftU;
            double lo = Math.Max(0.25, bu - 0.075), hi = Math.Min(1.0, bu + 0.075);
            int side = held ? st.EntrySide : SideNone; double band = held ? IX.BAND + IX.EXIT_BAND : IX.BAND;
            for (int it = 0; it < 14; it++)
            {
                double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                Q q1 = Surface(_bowls[bi], ToLocal(_bowls[bi], a + (b - a) * m1), IX.PUJA_R, side, band);
                Q q2 = Surface(_bowls[bi], ToLocal(_bowls[bi], a + (b - a) * m2), IX.PUJA_R, side, band);
                double p1 = q1.Kind >= 0 ? q1.Pen : double.NegativeInfinity, p2 = q2.Kind >= 0 ? q2.Pen : double.NegativeInfinity;
                if (p1 < p2) lo = m1; else hi = m2;
            }
            double ur = 0.5 * (lo + hi);
            Q qr = Surface(_bowls[bi], ToLocal(_bowls[bi], a + (b - a) * ur), IX.PUJA_R, side, band);
            if (qr.Kind < 0 || qr.Pen < bestPen) ur = bu;
            st.ShaftU = ur;
            return a + (b - a) * ur;
        }

        void TipFrame(int h, int slot, BowlsHandInput hand, bool valid, double3 sample, double r, double t, double dt, bool hasVel, double3 tipVel, TipKind kind)
        {
            Tip st = _tips[h * Slots + slot];
            st.Kind = kind;
            if (!hand.Tracked || !valid) { Lost(st, t); return; }
            if (st.Lost > 0 && t - st.Lost > IX.LOSS_HOLD) EndTouch(st, t);
            double wasLost = st.Lost > 0 ? t - st.Lost : 0;
            st.Lost = 0; st.Tracked++;
            // velocity over distinct tracker samples (HandFrame.tipVelocity when given), and a history for the peak speed
            bool newSample = hand.SampleTime != st.SampleT;
            if (newSample)
            {
                double3 v;
                if (hasVel) v = tipVel;
                else v = st.HasSample && hand.SampleTime > st.SampleT ? (sample - st.Sample) * (1 / Math.Max(0.004, hand.SampleTime - st.SampleT)) : double3.zero;
                if (st.HasSample && hand.SampleTime - st.SampleT > 0.2) v = double3.zero;
                st.Vel = v;
                if (st.VCount < IX.VEL_HISTORY) st.VHist[st.VCount++] = v;
                else { for (int k = 1; k < IX.VEL_HISTORY; k++) st.VHist[k - 1] = st.VHist[k]; st.VHist[IX.VEL_HISTORY - 1] = v; }
                st.Sample = sample; st.HasSample = true; st.SampleT = hand.SampleTime;
            }
            // the display-time position (Unity: position[] is already predicted; the reference predicts held samples)
            double3 p = sample;
            if (!hand.Predicted && Len(st.Vel) >= IX.PREDICT_MIN_SPEED) p = sample + st.Vel * Math.Min(IX.PREDICT_MAX, Math.Max(0, t - hand.SampleTime));
            double3 prev = st.HasLastPos ? st.LastPos : p;
            st.LastPos = p; st.HasLastPos = true;
            if (st.Mode == Mode.Free) FreeTip(st, h, slot, prev, p, r, t, wasLost);
            else TouchTip(st, h, slot, p, r, t, dt, newSample);
        }

        double PeakInward(Tip st, in BowlsHandBowl b, double3 normal)
        {
            double3 nW = ToWorldDir(b, normal);
            double vn = 0;
            for (int k = 0; k < st.VCount; k++) vn = Math.Max(vn, -Dot(st.VHist[k], nW));
            return vn;
        }

        void FreeTip(Tip st, int h, int slot, double3 prev, double3 p, double r, double t, double wasLost)
        {
            // swept entry: the first point on prev -> p inside a bowl surface (side remembered per bowl while free)
            double L = Len(p - prev);
            int steps = (int)Math.Min(24, Math.Max(1, Math.Ceiling(L / IX.SWEEP_STEP)));
            int hitI = -1; Q hitQ = default;
            for (int k = 1; k <= steps && hitI < 0; k++)
            {
                double3 q = prev + (p - prev) * ((double)k / steps);
                for (int i = 0; i < _bowls.Length; i++)
                {
                    Q s = Surface(_bowls[i], ToLocal(_bowls[i], q), r, st.Side[i], IX.BAND);
                    if (s.Kind >= 0 && s.Pen > IX.ENTER && (hitI < 0 || s.Pen > hitQ.Pen)) { hitI = i; hitQ = s; }
                }
            }
            if (hitI < 0)
            {
                for (int i = 0; i < _bowls.Length; i++)
                {
                    Q s = Surface(_bowls[i], ToLocal(_bowls[i], p), r, SideNone, IX.BAND);
                    st.Side[i] = s.Side;
                    if (s.Kind < 0 || s.Pen < -IX.GRAZE_GAP) st.FarSinceExit[i] = true;
                }
                return;
            }
            int bi = hitI; Q hq = hitQ; BowlsHandBowl b = _bowls[bi];
            double vn = PeakInward(st, b, hq.Normal);
            // review item 19: this hand is rubbing another bowl; its other tips rest on this one silently
            if (QuietNeighbours)
            {
                for (int j = 0; j < _bowls.Length; j++)
                    if (j != bi && _primary[j] != null && _primary[j] != st && _primary[j].HandIdx == h)
                    {
                        st.Bowl = bi; st.EntrySide = SideOfKind(hq.Kind); st.EntryT = t; st.Mode = Mode.Ghost;
                        return;
                    }
            }
            // a recoil from a strike on another bowl: silent (it leaves like a touch)
            if (RecoilGuard && st.LastStrikeBowl >= 0 && st.LastStrikeBowl != bi && t - st.LastStrikeT < IX.RECOIL_QUIET)
            {
                st.Bowl = bi; st.EntrySide = SideOfKind(hq.Kind); st.EntryT = t; st.Mode = Mode.Ghost;
                return;
            }
            st.Bowl = bi; st.EntrySide = SideOfKind(hq.Kind); st.EntryT = t; st.Omega = 0; st.LastAngle = hq.Angle; st.HasLastAngle = true; st.AngleT = st.SampleT;
            st.MoveT = 0; st.StillT = 0; st.DeepT = 0; st.RubClear(); st.Rubbing = false;
            double lastTap = st.LastTap[bi];
            // a continuation: back within 0.25 s / 3 cm of where it lost tracking, or within 0.4 s of a rub's release here
            bool cont = (wasLost > 0 && wasLost < IX.REACQUIRE && st.HasContactPt && Len(p - st.ContactPt) < IX.REACQUIRE_DIST)
                        || (t - _lastRelease[bi] < IX.REENTRY_QUIET && vn < 0.5 && st.Kind != TipKind.Suede);
            bool fresh = st.Tracked <= IX.FRESH_FRAMES;
            // another fingertip of the hand that is rubbing this bowl lands silently
            bool sameHandRub = _primary[bi] != null && _primary[bi] != st && _primary[bi].HandIdx == h;
            // smooth contact: back on the same bowl soon after leaving it, never far: the same touch, no tap and no tick
            bool graze = SmoothContact && !st.FarSinceExit[bi] && t - st.LastExit[bi] < IX.RETOUCH_QUIET;
            st.FarSinceExit[bi] = false;
            if (cont && st.Kind != TipKind.Suede && _primary[bi] == null) { BeginContact(st, h, slot, bi, hq, t, true); return; }
            bool struck = false;
            double vGlitch = vn;
            if (RecoilGuard) for (int k = 0; k < st.VCount; k++) vGlitch = Math.Max(vGlitch, Len(st.VHist[k]));
            if (!cont && !fresh && !sameHandRub && !graze && vGlitch < IX.GLITCH_SPEED && t - lastTap > IX.TAP_DEBOUNCE)
            {
                var sk = st.Kind == TipKind.Suede ? MusicRoom.Audio.Burst.BowlStrikeKind.Suede : st.Kind == TipKind.Shaft ? MusicRoom.Audio.Burst.BowlStrikeKind.Wood : MusicRoom.Audio.Burst.BowlStrikeKind.Tap;
                double tapAt = st.Kind == TipKind.Shaft ? IX.WOOD_SPEED : IX.TAP_SPEED;
                if (vn >= tapAt)
                {
                    _sink.Strike(bi, sk, Math.Min(IX.TAP_V_MAX, vn), hq.Angle, hq.Hs);
                    _sink.Event(new BowlsHandEvent { T = t, Type = BowlsHandEventType.Tap, Bowl = bi, V = vn, Finger = slot, Hand = h, StrikeKind = sk });
                    st.LastTap[bi] = t; struck = true;
                    st.LastStrikeT = t; st.LastStrikeBowl = bi;
                }
                else if (st.Kind != TipKind.Suede && !(t - st.LastExit[bi] < IX.RETOUCH_QUIET))
                    _sink.Strike(bi, MusicRoom.Audio.Burst.BowlStrikeKind.Tap, Math.Max(IX.TOUCH_MIN, vn), hq.Angle, hq.Hs);   // the touch-down tick (it also seeds a rub)
            }
            st.Mode = st.Kind == TipKind.Suede ? Mode.Struck : Mode.Touch; st.Tapped = struck;
            if (!struck && st.Kind != TipKind.Suede)
                _sink.Event(new BowlsHandEvent { T = t, Type = BowlsHandEventType.Touch, Bowl = bi, Surface = (MusicRoom.Audio.Burst.BowlSurface)hq.Kind, Finger = slot, Hand = h });
        }

        void TouchTip(Tip st, int h, int slot, double3 p, double r, double t, double dt, bool newSample)
        {
            int i = st.Bowl; BowlsHandBowl b = _bowls[i];
            Q q = Surface(b, ToLocal(b, p), r, st.EntrySide, IX.BAND + IX.EXIT_BAND);
            if (st.Mode == Mode.Ghost)
            {   // a silent rest on a neighbour of the bowl this hand rubs: it only leaves
                if (q.Kind < 0 || q.Pen < IX.EXIT) { st.Mode = Mode.Free; st.ClearSide(); st.Side[i] = Surface(b, ToLocal(b, p), r, SideNone, IX.BAND).Side; st.Bowl = -1; }
                return;
            }
            double exitAt = st.Mode == Mode.Contact ? IX.EXIT_RUB : SmoothContact ? IX.EXIT_TOUCH : IX.EXIT;
            if (q.Kind < 0 || q.Pen < exitAt)
            {
                if (st.Mode == Mode.Contact && (st.HoldSince == 0 || t - st.HoldSince < IX.LIFT_HOLD))
                {
                    // the lift hold: the stick stays on at its floor force and keeps turning with the finger's last speed
                    if (st.HoldSince == 0) st.HoldSince = t;
                    st.HoldAngle = (st.HasHoldAngle ? st.HoldAngle : st.LastAngle) + st.Omega * dt; st.HasHoldAngle = true;
                    double vfH = Math.Abs(st.Omega * b.R) < IX.V_DEAD ? 0 : st.Omega * b.R;
                    // smooth contact: off the wall, the stick's force fades out (HOLD_TAU) instead of holding N_MIN on a loud bowl
                    double nHold = SmoothContact ? st.SentN * Math.Exp(-dt / IX.HOLD_TAU) : IX.N_MIN;
                    SendContact(st, i, (MusicRoom.Audio.Burst.BowlSurface)(st.ContactKind >= 0 ? st.ContactKind : 0), nHold, vfH, st.HoldAngle, vfH == 0 ? 0 : st.Omega, double.IsNaN(st.ContactHs) ? 1 : st.ContactHs, dt);
                    return;
                }
                EndTouch(st, t); st.Mode = Mode.Free; st.ClearSide(); st.Side[i] = Surface(b, ToLocal(b, p), r, SideNone, IX.BAND).Side;
                return;
            }
            st.HoldSince = 0; st.HasHoldAngle = false;
            if (st.Mode == Mode.Struck) return;                     // the suede end only strikes; it leaves like a fingertip
            // angular velocity about the bowl axis (new samples only), the finger's speed along the rim
            if (newSample && st.HasLastAngle)
            {
                double da = q.Angle - st.LastAngle;
                while (da > Math.PI) da -= 2 * Math.PI;
                while (da < -Math.PI) da += 2 * Math.PI;
                double sdt = Math.Max(1e-3, st.SampleT - st.AngleT);
                if (st.SampleT > st.AngleT)
                {
                    double w = da / sdt, k = 1 - Math.Exp(-sdt / IX.OMEGA_TAU);
                    if (RubSmoothing)
                    {
                        // a tracker jump is one sample's huge rate: clamp it near the running value and to VF_MAX along the rim
                        double wMax = IX.VF_MAX / Math.Max(0.02, b.R);
                        w = Math.Max(st.Omega - IX.OMEGA_JUMP, Math.Min(st.Omega + IX.OMEGA_JUMP, w));
                        w = Math.Max(-wMax, Math.Min(wMax, w));
                    }
                    st.Omega += k * (w - st.Omega);
                }
                st.LastAngle = q.Angle; st.AngleT = st.SampleT;
            }
            double rc = Math.Min(b.R + 0.01, Math.Max(b.R - 0.01, q.Rho));
            double vRim = st.Omega * rc;
            st.MoveT = Math.Abs(vRim) >= IX.CONVERT_SPEED ? st.MoveT + dt : 0;
            // still = within 6 mm of an anchor (tracker jitter of a resting finger is +-2-3 mm)
            if (!st.HasAnchor || Len(p - st.Anchor) > IX.STILL_RADIUS) { st.Anchor = p; st.HasAnchor = true; st.StillT = 0; } else st.StillT += dt;
            st.DeepT = q.Pen >= IX.EXTRA_DEPTH && st.StillT > 0 ? st.DeepT + dt : 0;
            st.Pen = q.Pen;
            if (st.Mode == Mode.Touch)
            {
                // a touching tip becomes the rub when it moves along the rim (and nobody else rubs this bowl)
                if (st.MoveT >= IX.CONVERT_TIME && _primary[i] == null && st.Kind != TipKind.Suede) BeginContact(st, h, slot, i, q, t, false);
                else return;
            }
            // ---- the rub contact: one Contact per frame
            double vf = vRim; if (Math.Abs(vf) < IX.V_DEAD) vf = 0;
            double N = Math.Min(IX.N_MAX, Math.Max(IX.N_MIN, IX.N_MIN + IX.N_SLOPE * (q.Pen - IX.N_DEAD)));
            if (SmoothContact && q.Pen < IX.GRAZE_KNEE)
                N *= 1 - (1 - IX.GRAZE_FLOOR) * Math.Min(1, (IX.GRAZE_KNEE - q.Pen) / (IX.GRAZE_KNEE - IX.EXIT_RUB));
            int uk = UnitKind(q.Kind);
            SendContact(st, i, (MusicRoom.Audio.Burst.BowlSurface)uk, N, vf, q.Angle, vf == 0 ? 0 : st.Omega, q.Hs, dt);
            st.ContactPt = p; st.HasContactPt = true; st.ContactKind = uk; st.ContactHs = q.Hs; st.SurfaceKind = q.Kind;
            st.RubPush(t, Math.Abs(vf));
            while (st.RubCount > 0 && st.RubFirstT < t - IX.RUB_WINDOW) st.RubShift();
            double sum = 0;
            for (int k = 0; k < st.RubCount; k++) sum += st.RubV[(st.RubHead + k) % RubCap];
            double mv = sum / st.RubCount;
            st.RubMean = mv;
            bool rubbing = st.RubCount > 3 && t - st.RubFirstT > IX.RUB_WINDOW * 0.8 && mv >= IX.RUB_MIN;
            if (rubbing != st.Rubbing)
            {
                st.Rubbing = rubbing;
                _sink.Event(new BowlsHandEvent { T = t, Type = rubbing ? BowlsHandEventType.RubStart : BowlsHandEventType.RubEnd, Bowl = i, V = mv, N = N, Surface = (MusicRoom.Audio.Burst.BowlSurface)q.Kind, Tool = st.Kind == TipKind.Shaft, Hand = h, Finger = slot });
            }
        }

        /// <summary>One rub frame to the unit, conditioned (RubSmoothing): |v_f| at most VF_MAX and slewed by VF_SLEW, the
        /// force smoothed (N_TAU) and slewed by N_SLEW; the first frame of a contact goes as it is (the unit ramps it in).</summary>
        void SendContact(Tip st, int i, MusicRoom.Audio.Burst.BowlSurface kind, double N, double vf, double angle, double omega, double hs, double dt)
        {
            if (RubSmoothing)
            {
                vf = Math.Max(-IX.VF_MAX, Math.Min(IX.VF_MAX, vf));
                if (SmoothContact && !st.SentOn)
                {   // the attack: a new contact swells in from zero force (the N slew) at the measured angle and height
                    st.SentOn = true; st.SentN = 0; st.SentVf = vf; st.SentAngle = angle; st.SentHs = hs;
                }
                if (SmoothContact && dt > 0) { hs = st.SentHs + (1 - Math.Exp(-dt / IX.HS_TAU)) * (hs - st.SentHs); st.SentHs = hs; }
                if (st.SentOn && dt > 0)
                {
                    double dv = IX.VF_SLEW * dt;
                    vf = Math.Max(st.SentVf - dv, Math.Min(st.SentVf + dv, vf));
                    double nk = 1 - Math.Exp(-dt / IX.N_TAU), dn = IX.N_SLEW * dt;
                    double nTarget = st.SentN + nk * (N - st.SentN);
                    N = Math.Max(st.SentN - dn, Math.Min(st.SentN + dn, nTarget));
                    // the contact angle: ahead of the last one by the speed's turn, pulled toward the measured one (A_TAU)
                    double pred = st.SentAngle + omega * dt, da = angle - pred;
                    while (da > Math.PI) da -= 2 * Math.PI;
                    while (da < -Math.PI) da += 2 * Math.PI;
                    angle = pred + (1 - Math.Exp(-dt / IX.A_TAU)) * da;
                }
                if (vf == 0) omega = 0;
                st.SentOn = true; st.SentN = N; st.SentVf = vf; st.SentAngle = angle;
            }
            _sink.Contact(i, kind, N, vf, angle, omega, hs);
        }

        void BeginContact(Tip st, int h, int slot, int i, in Q q, double t, bool continued)
        {
            st.SentOn = false;
            st.Mode = Mode.Contact; _primary[i] = st; st.HandIdx = h; st.EntrySide = SideOfKind(q.Kind);
            if (continued)
            {
                st.Rubbing = true; st.RubClear();
                st.RubPush(t - IX.RUB_WINDOW, IX.RUB_MIN); st.RubPush(t - 0.2, IX.RUB_MIN); st.RubPush(t - 0.1, IX.RUB_MIN); st.RubPush(t - 0.05, IX.RUB_MIN);
            }
            _sink.Event(new BowlsHandEvent { T = t, Type = continued ? BowlsHandEventType.RubResume : BowlsHandEventType.RubContact, Bowl = i, Surface = (MusicRoom.Audio.Burst.BowlSurface)q.Kind, Finger = slot, Hand = h });
        }

        void Lost(Tip st, double t)
        {
            if (st.Lost == 0) st.Lost = t;
            st.Tracked = 0; st.HasLastPos = false; st.VCount = 0;
            if (st.Mode != Mode.Free && t - st.Lost > IX.LOSS_HOLD) { EndTouch(st, t); st.Mode = Mode.Free; st.ClearSide(); }
        }

        void EndTouch(Tip st, double t)
        {
            if (st.Mode == Mode.Contact)
            {
                _sink.Release(st.Bowl); _lastRelease[st.Bowl] = t;
                if (st.Rubbing) _sink.Event(new BowlsHandEvent { T = t, Type = BowlsHandEventType.RubEnd, Bowl = st.Bowl });
                _sink.Event(new BowlsHandEvent { T = t, Type = BowlsHandEventType.Lift, Bowl = st.Bowl });
                if (_primary[st.Bowl] == st) _primary[st.Bowl] = null;
            }
            if (st.Mode != Mode.Free && st.Mode != Mode.Ghost && st.Bowl >= 0) st.LastExit[st.Bowl] = t;
            st.SentOn = false;
            st.Mode = Mode.Free; st.Rubbing = false; st.StillT = 0; st.DeepT = 0; st.MoveT = 0; st.HasAnchor = false; st.HoldSince = 0; st.HasHoldAngle = false;
        }

        // damping: still fingertips after their grace, extra tips on a rubbed bowl only when still and deep, palms
        void DampFrame(double t)
        {
            for (int i = 0; i < _bowls.Length; i++)
            {
                bool rubbed = _primary[i] != null && _primary[i].Rubbing;
                int n = 0;
                for (int k = 0; k < _tips.Length; k++)
                {
                    Tip st = _tips[k];
                    if (st.Mode != Mode.Touch || st.Bowl != i || st.Kind == TipKind.Suede) continue;
                    if (rubbed) { if (st.DeepT > IX.EXTRA_TIME) n++; }
                    else if (t - st.EntryT >= IX.REST_GRACE && st.StillT >= IX.REST_STILL) n++;
                }
                int L = n >= 2 ? 2 : n;
                for (int h = 0; h < 2; h++) if (_palm[h].OnBowl == i && t - _palm[h].Since >= IX.PALM_REST) L = 3;
                if (L != _damp[i])
                {
                    _damp[i] = L; _sink.Damp(i, L);
                    _sink.Event(new BowlsHandEvent { T = t, Type = BowlsHandEventType.Damp, Bowl = i, Level = L });
                }
            }
        }

        void PalmFrame(int h, BowlsHandInput hand, double t)
        {
            Palm pm = _palm[h];
            if (!hand.HasPalm || !hand.Tracked) { pm.OnBowl = -1; return; }
            double3 c = hand.PalmC, n = hand.PalmN;
            int on = -1;
            for (int i = 0; i < _bowls.Length; i++)
            {
                BowlsHandBowl b = _bowls[i];
                double3 lp = ToLocal(b, c);
                double rho = Hypot(lp.x, lp.z);
                bool nearRim = lp.y > b.H * 0.4 && lp.y < b.H + IX.PALM_DIST && rho < b.R + IX.PALM_DIST;
                double3 toBowl = ToWorld(b, new double3(0, b.H * 0.6, 0)) - c;
                double L = Len(toBowl);
                if (nearRim && Dot(n, toBowl * (1 / L)) > IX.PALM_COS) { on = i; break; }
            }
            // the palm's speed over distinct tracker samples (frames repeat a sample between new ones)
            if (hand.SampleTime != pm.LastT)
            {
                if (pm.HasLastPos && hand.SampleTime > pm.LastT) pm.V = Len(c - pm.LastPos) / Math.Max(0.004, hand.SampleTime - pm.LastT);
                pm.LastPos = c; pm.HasLastPos = true; pm.LastT = hand.SampleTime;
            }
            double vn = pm.V;
            if (on >= 0 && pm.OnBowl != on)
            {
                pm.OnBowl = on; pm.Since = t;
                if (vn >= IX.PALM_SPEED)
                {
                    _sink.Strike(on, MusicRoom.Audio.Burst.BowlStrikeKind.Palm, Math.Min(IX.PALM_V_MAX, vn), 0, 1);
                    _sink.Event(new BowlsHandEvent { T = t, Type = BowlsHandEventType.PalmStrike, Bowl = on, V = vn, Hand = h });
                }
            }
            else if (on < 0) pm.OnBowl = -1;
        }
    }
}
