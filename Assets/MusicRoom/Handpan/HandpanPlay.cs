// Bare-hand play logic (handpan.md section 6): the Node reference interaction line for line, allocation-free (fixed
// rings, struct slots, a preallocated event buffer). Plain C#: no Unity objects, so the EditMode tests drive it with the
// lab's scripted frames (Reference~/interaction-vectors.json) and the module feeds it the real hands.
//
// What to feed Step each frame (6.2; review item 8): t = the frame clock (Clock.NowS), each fingertip's world position =
// the predicted pose of this frame (Pointer.Tip, which moves every frame in Unity) with its capsule radius, the palm
// centre and normal (HandMath on HandInput's HandFrame), and the hand's flags (tracked, high confidence, inferred, system
// gesture). NEVER a sample interval: speeds are least-squares slopes over the DISTINCT pose samples (moved > 10 um) of the
// last 45 ms (loudness) and 70 ms (the strike decision), stamped with t. HandFrame.sampleInterval is the tracker's
// interval (about 33 ms at the LOW rate) and would make every speed about 3 times too low.
using System;
using MusicRoom.Audio.Burst;

namespace MusicRoom.Handpan
{
    /// <summary>handpan-interaction.mjs IK (all constants of the play logic).</summary>
    public static class HandpanPlayK
    {
        public const double ARM_ABOVE = 0.010; public const int ARM_SAMPLES = 2;
        public const double MAX_PREV = 0.08, NEW_SAMPLE_M = 1e-5;
        public const int HIST = 8;
        public const double VEL_WIN_S = 0.045, GATE_WIN_S = 0.070;
        public const int MIN_SAMPLES = 3; public const double MAX_WIN_S = 0.100;
        public const double V0 = 0.12; public const int FALL_STEPS = 2;
        public const double V1 = 1.0, VEXP = 0.75, VMIN = 0.06, TANG_RATIO = 2.5;
        public const double SAME_FIELD_MS = 3, HAND_FIELD_MS = 40;
        public const double REST_BAND = 0.006, REST_MOVE = 0.004, REST_S = 0.10, REST_STRUCK_S = 0.40, REST_STRUCK_LO = -0.008, LIFT = 0.012;
        public const double PALM_R = 0.023, PALM_FACING = 0.6, PALM_SLAP_V0 = 0.25, PALM_SLAP_V1 = 1.6, PALM_SLAP_MIN = 0.35, PALM_SLAP_BAND = 0.012;
        public const double FLAT_PALM_NEAR = 0.020, FLAT_TIPS_NEAR = 0.015; public const int FLAT_TIPS_MIN = 3;
        public const double DEFER_MAX_S = 0.060, SLAP_OWN_S = 0.060;
        public const double PALM_REST_BAND = 0.010, PALM_REST_MOVE = 0.006, PALM_REST_S = 0.12, PALM_LIFT = 0.04;
        public const double PALM_COVER_FULL = 0.075, PALM_COVER_ZERO = 0.11;
        public const double LOST_GRACE_S = 0.30; public const int REACQUIRE_FRAMES = 2;
        public const double JUMP = 0.12, TAK_GAIN = 0.8;
        public const int LIFT_HINT_MUTES = 3; public const double LIFT_HINT_WITHIN_S = 0.5;
        public const string HintLift = "tip_handpan_lift";
    }

    /// <summary>One hand's input for one frame (world metres). Left = hand 0, right = hand 1.</summary>
    public sealed class HandpanHandInput
    {
        public bool Tracked, HighConfidence, Inferred, SystemGesture;
        /// <summary>Thumb is index 4 (the reference's order: index, middle, ring, little, thumb).</summary>
        public readonly D3[] Tips = new D3[5];
        public readonly double[] Radii = new double[5];
        public D3 PalmCentre, PalmNormal;
        public bool Usable => Tracked && HighConfidence && !Inferred && !SystemGesture;
    }

    public sealed class HandpanFrame
    {
        public double T, Dt;
        public readonly HandpanHandInput[] Hands = { new HandpanHandInput(), new HandpanHandInput() };
    }

    public enum HandpanEventType : byte { Strike = 0, Damp = 1, Release = 2, Hint = 3 }
    public enum HandpanDampSource : byte { Finger = 0, Palm = 1, Shell = 2 }

    /// <summary>One play event (the reference's emitted objects). Strike: Field 0..8 or 9 (tak: U, V = map X, Z and the two
    /// neighbours); Damp / Release: Field, Slot (0 L fingers, 1 R fingers, 2 L palm, 3 R palm), Kind, Strength.</summary>
    public struct HandpanEvent
    {
        public HandpanEventType Type;
        public double T;
        public int Field, Hand, Finger, Slot;
        public double Vel, U, V, Vn, Strength;
        public HandpanTech Tech;
        public HandpanDampSource Kind;
        public bool AfterStrike;
        public int N0, N1;
        public double W0, W1;
        public string Hint;

        public override string ToString() =>
            Type == HandpanEventType.Strike ? $"{T:F4}s strike f{Field} v{Vel:F3} {Tech} h{Hand}" + (Field == HandpanSpec.Tak ? $" nb {N0}:{W0:F6}/{N1}:{W1:F6}" : "")
            : Type == HandpanEventType.Damp ? $"{T:F4}s damp f{Field} {Kind} s{Slot} {Strength:F3}" + (AfterStrike ? " afterStrike" : "")
            : Type == HandpanEventType.Release ? $"{T:F4}s release f{Field} s{Slot}" : $"{T:F4}s hint {Hint}";
    }

    /// <summary>The bare-hand play logic: one per instrument. Step(frame) returns this frame's events in Events[0..Count).</summary>
    public sealed class HandpanPlay
    {
        const int NF = HandpanSpec.FieldCount, TAK = HandpanSpec.Tak, HIST = HandpanPlayK.HIST;

        sealed class Hist
        {
            public int N, K;
            public readonly double[] T = new double[HIST], H = new double[HIST], X = new double[HIST], Z = new double[HIST];
            public readonly D3[] W = new D3[HIST];
            public int Idx(int back) => (K - 1 - back + HIST * 4) % HIST;
            public void Push(double t, in HandpanContact c, D3 w)
            {
                int j = K; T[j] = t; H[j] = c.H; X[j] = c.X; Z[j] = c.Z; W[j] = w;
                K = (K + 1) % HIST; if (N < HIST) N++;
            }
            public bool Moved(D3 w) => N == 0 || D3.Dist(w, W[Idx(0)]) > HandpanPlayK.NEW_SAMPLE_M;
            public bool Jump(D3 w) => N != 0 && D3.Dist(w, W[Idx(0)]) > HandpanPlayK.JUMP;
            /// <summary>Least-squares slope over the samples within win of the newest (at least MIN_SAMPLES within MAX_WIN_S).</summary>
            public double Slope(double[] A, double win)
            {
                if (N < 2) return 0;
                double tN = T[Idx(0)];
                int n = 0; double st = 0, sv = 0;
                for (int b = 0; b < N; b++)
                {
                    int j = Idx(b); double age = tN - T[j];
                    if (age > win + 1e-9 && (n >= HandpanPlayK.MIN_SAMPLES || age > HandpanPlayK.MAX_WIN_S + 1e-9)) break;
                    n++; st += T[j] - tN; sv += A[j];
                }
                if (n < 2) return 0;
                double mt = st / n, mv = sv / n, sxy = 0, sxx = 0;
                for (int b = 0; b < n; b++) { int j = Idx(b); double dt = T[j] - tN - mt; sxy += dt * (A[j] - mv); sxx += dt * dt; }
                return sxx > 0 ? sxy / sxx : 0;
            }
            public bool Falling(int steps)
            {
                if (N < steps + 1) return false;
                for (int b = 0; b < steps; b++) if (!(H[Idx(b)] < H[Idx(b + 1)])) return false;
                return true;
            }
        }

        /// <summary>A rest anchor: the running mean of the samples since the pad settled; restarts after two samples beyond move.</summary>
        struct Anchor
        {
            public double Sx, Sy, Sz, T0; public int N, Out;
            public void Reset(double t, D3 w) { Sx = w.X; Sy = w.Y; Sz = w.Z; N = 1; T0 = t; Out = 0; }
            public void Add(double t, D3 w, double move)
            {
                if (N == 0) { Reset(t, w); return; }
                double d = HandpanMath.Hypot(w.X - Sx / N, w.Y - Sy / N, w.Z - Sz / N);
                if (d > move) { if (++Out >= 2) Reset(t, w); }
                else { Out = 0; Sx += w.X; Sy += w.Y; Sz += w.Z; N++; }
            }
        }

        struct Contact
        {
            public bool Has, Struck, Damping;
            public int Field;
            public double U, V, StrikeT;
            public Anchor Rest;
        }

        sealed class PointerState
        {
            public readonly Hist Hist = new Hist();
            public int ArmRun;
            public bool Armed;
            public Contact Contact;
            public bool HasLast, LastOnTop; public double LastH;
            public bool HasPend; public double PendT; public HandpanContact PendC; public D3 PendW;
        }

        sealed class HandState
        {
            public int Seen; public double LostT;
            public readonly PointerState[] Pointers = { new PointerState(), new PointerState(), new PointerState(), new PointerState(), new PointerState() };
            public readonly Hist PalmHist = new Hist();
            public bool PalmArmed; public Anchor PalmRest;
            public bool PalmDamping; public readonly bool[] PalmCovered = new bool[NF];
            public double SlapT = -1e9; public int JumpRun;
            public readonly HandpanEvent[] Held = new HandpanEvent[8]; public int HeldCount; public double HeldSince = -1;
            public readonly double[] LastHit = new double[NF + 1];
            public readonly int[] RestSlots = new int[NF + 1];
            public bool LandingFlat;
            public readonly HandpanContact[] TipC = new HandpanContact[5];
        }

        readonly HandState[] _hands = { new HandState(), new HandState() };
        readonly double[] _lastField = new double[NF + 1];
        double _t;
        int _liftMutes;
        bool _liftHinted;
        HandpanPlace _place = HandpanPlace.Default;

        /// <summary>This frame's events (Events[0..Count)); overwritten by the next Step.</summary>
        public readonly HandpanEvent[] Events = new HandpanEvent[64];
        public int Count { get; private set; }
        /// <summary>Events dropped because a frame produced more than Events.Length (never in practice).</summary>
        public int Overflow { get; private set; }
        public bool LiftHinted => _liftHinted;
        public HandpanPlace Place { get => _place; set => _place = value; }

        public HandpanPlay() { Reset(); }

        /// <summary>Forget every hand (rig moved, tracking restarted). Damping slots are NOT released here: call
        /// ReleaseHeld first if the sound unit holds any.</summary>
        public void Reset()
        {
            for (int k = 0; k < _lastField.Length; k++) _lastField[k] = -1e9;
            foreach (HandState H in _hands)
            {
                H.Seen = 0; H.LostT = 0; H.PalmHist.N = 0; H.PalmHist.K = 0; H.PalmArmed = false; H.PalmRest = default; H.PalmDamping = false;
                Array.Clear(H.PalmCovered, 0, NF); H.SlapT = -1e9; H.JumpRun = 0; H.HeldCount = 0; H.HeldSince = -1; H.LandingFlat = false;
                for (int k = 0; k < H.LastHit.Length; k++) H.LastHit[k] = -1e9;
                Array.Clear(H.RestSlots, 0, H.RestSlots.Length);
                foreach (PointerState p in H.Pointers) { p.Hist.N = 0; p.Hist.K = 0; p.ArmRun = 0; p.Armed = false; p.Contact = default; p.HasLast = false; p.HasPend = false; }
            }
            _liftMutes = 0; _liftHinted = false; Count = 0;
        }

        void Emit(in HandpanEvent e)
        {
            if (Count >= Events.Length) { Overflow++; return; }
            HandpanEvent x = e; x.T = _t;
            Events[Count++] = x;
        }

        /// <summary>One app frame (step()).</summary>
        public int Step(HandpanFrame frame)
        {
            _t = frame.T; Count = 0;
            for (int hi = 0; hi < 2; hi++)
            {
                HandState H = _hands[hi];
                HandpanHandInput F = frame.Hands[hi];
                if (F == null || !F.Usable)
                {
                    // tracking loss: no new strikes; damping held for LOST_GRACE then released; pointers disarmed, histories dropped
                    H.Seen = 0; H.LostT += frame.Dt;
                    foreach (PointerState p in H.Pointers) { p.Armed = false; p.ArmRun = 0; p.Hist.N = 0; p.HasLast = false; p.HasPend = false; }
                    H.PalmHist.N = 0; H.PalmArmed = false; H.JumpRun = 0; H.LandingFlat = false; H.HeldCount = 0; H.HeldSince = -1;
                    if (H.LostT >= HandpanPlayK.LOST_GRACE_S) ReleaseAll(H, hi);
                    continue;
                }
                H.LostT = 0; H.Seen++;
                bool acquired = H.Seen > HandpanPlayK.REACQUIRE_FRAMES;
                // ---- palm
                HandpanContact pc = HandpanMath.ContactOf(F.PalmCentre, HandpanPlayK.PALM_R, _place);
                D3 nW = HandpanMath.SurfaceNormalWorld(pc.X, pc.Z, _place);
                double facing = -(F.PalmNormal.X * nW.X + F.PalmNormal.Y * nW.Y + F.PalmNormal.Z * nW.Z);
                Hist PH = H.PalmHist;
                bool palmGlitch = false;
                if (PH.Jump(F.PalmCentre)) { if (H.JumpRun == 0) { H.JumpRun = 1; palmGlitch = true; } else { PH.N = 0; H.PalmArmed = false; } }
                if (!palmGlitch) H.JumpRun = 0;
                bool palmNew = !palmGlitch && PH.Moved(F.PalmCentre);
                bool hasPrevPalm = PH.N > 0;
                double prevPalmH = hasPrevPalm ? PH.H[PH.Idx(0)] : 0;
                if (palmNew) PH.Push(frame.T, pc, F.PalmCentre);
                double palmVn = -PH.Slope(PH.H, HandpanPlayK.VEL_WIN_S);
                bool slapped = false;
                if (palmNew && hasPrevPalm && acquired && pc.OnTop && facing > HandpanPlayK.PALM_FACING && prevPalmH > HandpanPlayK.PALM_SLAP_BAND
                    && pc.H <= HandpanPlayK.PALM_SLAP_BAND && H.PalmArmed)
                {
                    double v = HandpanMath.PalmVel(palmVn);
                    if (v > 0 && pc.Hit.Field >= 0)
                    {
                        int field = pc.Hit.Field;
                        if (frame.T - _lastField[field] >= HandpanPlayK.SAME_FIELD_MS / 1000)
                        {
                            bool tak = field == TAK;
                            var e = new HandpanEvent
                            {
                                Type = HandpanEventType.Strike, Field = field, Vel = HandpanMath.Round3(tak ? v * HandpanPlayK.TAK_GAIN : v),
                                U = HandpanMath.Round3(tak ? pc.X : pc.Hit.U), V = HandpanMath.Round3(tak ? pc.Z : pc.Hit.V),
                                Tech = tak ? HandpanTech.Tak : HandpanTech.Slap, Hand = hi, Finger = -1, Vn = HandpanMath.Round3(palmVn),
                            };
                            if (tak) SetNeighbours(ref e, pc.X, pc.Z);
                            Emit(e);
                            _lastField[field] = frame.T; H.LastHit[field] = frame.T; slapped = true; H.SlapT = frame.T;
                        }
                        H.HeldCount = 0; H.HeldSince = -1;   // the fingers that landed first belong to this slap
                    }
                }
                if (!palmGlitch)
                {
                    if (pc.H > HandpanPlayK.PALM_SLAP_BAND + 0.01) H.PalmArmed = true;
                    else if (slapped || pc.H <= HandpanPlayK.PALM_SLAP_BAND) H.PalmArmed = false;
                }
                // palm rest -> damp the covered fields + a light whole-shell damp (a glitch frame changes nothing)
                bool palmInBand = !palmGlitch && pc.OnTop && facing > HandpanPlayK.PALM_FACING && pc.H < HandpanPlayK.PALM_REST_BAND;
                if (palmInBand) H.PalmRest.Add(frame.T, F.PalmCentre, HandpanPlayK.PALM_REST_MOVE);
                else if (!palmGlitch) H.PalmRest.N = 0;
                if (palmInBand && frame.T - H.PalmRest.T0 >= HandpanPlayK.PALM_REST_S - 1e-9 && !H.PalmDamping)
                {
                    H.PalmDamping = true;
                    for (int f = 0; f < NF; f++)
                    {
                        double k = HandpanMath.PalmCover(f, pc.X, pc.Z);
                        H.PalmCovered[f] = k > 0;
                        if (k > 0) Emit(new HandpanEvent { Type = HandpanEventType.Damp, Field = f, Slot = 2 + hi, Kind = HandpanDampSource.Palm, Strength = HandpanMath.Round3(k), Hand = hi });
                    }
                    Emit(new HandpanEvent { Type = HandpanEventType.Damp, Field = TAK, Slot = 2 + hi, Kind = HandpanDampSource.Shell, Strength = 1, Hand = hi });
                }
                if (!palmGlitch && H.PalmDamping && (pc.H > HandpanPlayK.PALM_LIFT || facing < HandpanPlayK.PALM_FACING - 0.1 || !pc.OnTop)) ReleasePalm(H, hi);
                // is this hand landing flat? (its fingertip strikes wait for the palm)
                int tipsNear = 0;
                for (int k = 0; k < 5; k++)
                {
                    H.TipC[k] = HandpanMath.ContactOf(F.Tips[k], F.Radii[k], _place);
                    if (H.TipC[k].OnTop && H.TipC[k].H < HandpanPlayK.FLAT_TIPS_NEAR) tipsNear++;
                }
                bool landingFlat = palmGlitch ? H.LandingFlat
                    : pc.OnTop && facing > HandpanPlayK.PALM_FACING && palmVn > HandpanPlayK.PALM_SLAP_V0 && (pc.H < HandpanPlayK.FLAT_PALM_NEAR || tipsNear >= HandpanPlayK.FLAT_TIPS_MIN);
                H.LandingFlat = landingFlat;
                // ---- fingertips
                for (int k = 0; k < 5; k++)
                {
                    PointerState p = H.Pointers[k];
                    D3 tip = F.Tips[k];
                    HandpanContact c = H.TipC[k];
                    Hist Hs = p.Hist;
                    if (Hs.Jump(tip))
                    {
                        if (!p.HasPend) { p.HasPend = true; p.PendT = frame.T; p.PendC = c; p.PendW = tip; continue; }   // a lone far sample: held back
                        // a second far sample: near the held one, the hand really moved (history restarts from it); else hold this one
                        if (D3.Dist(tip, p.PendW) > HandpanPlayK.JUMP) { p.PendT = frame.T; p.PendC = c; p.PendW = tip; continue; }
                        Hs.N = 0; p.Armed = false; p.ArmRun = 0; p.HasLast = false;
                        TipSample(H, hi, k, p, p.PendT, p.PendC, p.PendW, acquired, landingFlat, slapped);
                        p.HasLast = true; p.LastOnTop = p.PendC.OnTop; p.LastH = p.PendC.H;
                    }
                    p.HasPend = false;
                    if (Hs.Moved(tip)) TipSample(H, hi, k, p, frame.T, c, tip, acquired, landingFlat, slapped);
                    p.HasLast = true; p.LastOnTop = c.OnTop; p.LastH = c.H;
                    // rest -> finger damp; lift -> release
                    if (p.Contact.Has)
                    {
                        ref Contact C = ref p.Contact;
                        double lo = C.Struck ? HandpanPlayK.REST_STRUCK_LO : double.NegativeInfinity, need = C.Struck ? HandpanPlayK.REST_STRUCK_S : HandpanPlayK.REST_S;
                        bool inBand = c.OnTop && c.H < HandpanPlayK.REST_BAND && c.H > lo;
                        if (inBand) C.Rest.Add(frame.T, tip, HandpanPlayK.REST_MOVE); else C.Rest.N = 0;
                        if (!C.Damping && inBand && C.Rest.N != 0 && frame.T - C.Rest.T0 >= need - 1e-9 && C.Field >= 0 && C.Field < NF)
                        {
                            C.Damping = true; AddFingerRest(H, hi, ref C);
                            if (C.Struck && frame.T - C.StrikeT <= HandpanPlayK.LIFT_HINT_WITHIN_S + 1e-9 && !_liftHinted && ++_liftMutes >= HandpanPlayK.LIFT_HINT_MUTES)
                            {
                                _liftHinted = true;
                                Emit(new HandpanEvent { Type = HandpanEventType.Hint, Hint = HandpanPlayK.HintLift, Hand = hi, Field = -1 });
                            }
                        }
                        if (c.H > HandpanPlayK.LIFT || !c.OnTop) { if (C.Damping) RemoveFingerRest(H, hi, C.Field); C = default; }
                    }
                }
                // deferred fingertip strikes: play when the hand stops landing flat (or after DEFER_MAX_S); a slap dropped them
                if (H.HeldCount > 0 && (!landingFlat || frame.T - H.HeldSince >= HandpanPlayK.DEFER_MAX_S - 1e-9))
                {
                    for (int j = 0; j < H.HeldCount; j++) FingerStrike(H, H.Held[j]);
                    H.HeldCount = 0; H.HeldSince = -1;
                }
            }
            return Count;
        }

        /// <summary>One distinct fingertip sample: push, arm, and the crossing test (a strike or a silent contact).</summary>
        void TipSample(HandState H, int hi, int k, PointerState p, double t, in HandpanContact c, D3 w, bool acquired, bool landingFlat, bool slapped)
        {
            Hist Hs = p.Hist;
            bool hasPrev = Hs.N > 0;
            double prevH = 0, prevX = 0, prevZ = 0; bool prevOnTop = false;
            if (hasPrev) { int j = Hs.Idx(0); prevH = Hs.H[j]; prevX = Hs.X[j]; prevZ = Hs.Z[j]; prevOnTop = p.HasLast && p.LastOnTop; }
            Hs.Push(t, c, w);
            p.ArmRun = c.OnTop && c.H > HandpanPlayK.ARM_ABOVE ? p.ArmRun + 1 : 0;
            if (p.ArmRun >= HandpanPlayK.ARM_SAMPLES && acquired) p.Armed = true;
            if (!(hasPrev && prevOnTop && c.OnTop && prevH > 0 && c.H <= 0 && prevH <= HandpanPlayK.MAX_PREV)) return;
            // the crossing point
            double a = prevH / (prevH - c.H), X = prevX + (c.X - prevX) * a, Z = prevZ + (c.Z - prevZ) * a;
            HandpanHit hit = HandpanMath.HitField(X, Z);
            double vnGate = -Hs.Slope(Hs.H, HandpanPlayK.GATE_WIN_S), vn = Math.Max(-Hs.Slope(Hs.H, HandpanPlayK.VEL_WIN_S), HandpanPlayK.V0);
            double vt = HandpanMath.Hypot(Hs.Slope(Hs.X, HandpanPlayK.VEL_WIN_S), Hs.Slope(Hs.Z, HandpanPlayK.VEL_WIN_S));
            bool was = p.Armed; p.Armed = false; p.ArmRun = 0;
            bool sounds = was && hit.Field >= 0 && vnGate >= HandpanPlayK.V0 && Hs.Falling(HandpanPlayK.FALL_STEPS) && vt <= HandpanPlayK.TANG_RATIO * vn;
            // A new crossing while the old contact still damps (the pad rose less than LIFT and came down again): the old
            // contact's damping is released first, or its slot would stay held (a leak in the reference, fixed in both).
            if (p.Contact.Has && p.Contact.Damping) RemoveFingerRest(H, hi, p.Contact.Field);
            p.Contact = new Contact { Has = true, Field = hit.Field, U = hit.U, V = hit.V, Struck = false, StrikeT = t };
            if (!sounds || t - H.SlapT <= HandpanPlayK.SLAP_OWN_S || slapped) return;
            double v = HandpanMath.FingerVel(vn);
            bool tak = hit.Field == TAK;
            var e = new HandpanEvent
            {
                Type = HandpanEventType.Strike, Field = hit.Field, Vel = HandpanMath.Round3(tak ? v * HandpanPlayK.TAK_GAIN : v),
                U = HandpanMath.Round3(tak ? X : hit.U), V = HandpanMath.Round3(tak ? Z : hit.V),
                Tech = tak ? HandpanTech.Tak : HandpanTech.Tip, Hand = hi, Finger = k, Vn = HandpanMath.Round3(vn),
            };
            if (tak) SetNeighbours(ref e, X, Z);
            p.Contact.Struck = true;
            if (landingFlat)
            {
                if (H.HeldSince < 0) H.HeldSince = t;
                if (H.HeldCount < H.Held.Length) H.Held[H.HeldCount++] = e;
            }
            else FingerStrike(H, e);
        }

        static void SetNeighbours(ref HandpanEvent e, double X, double Z)
        {
            HandpanMath.TakNeighbours(X, Z, out HandpanNeighbour n0, out HandpanNeighbour n1);
            e.N0 = n0.Field; e.W0 = HandpanMath.Round6(n0.W); e.N1 = n1.Field; e.W1 = HandpanMath.Round6(n1.W);
        }

        void FingerStrike(HandState H, in HandpanEvent e)
        {
            int f = e.Field;
            if (_t - _lastField[f] < HandpanPlayK.SAME_FIELD_MS / 1000) return;
            if (_t - H.LastHit[f] < HandpanPlayK.HAND_FIELD_MS / 1000) return;
            _lastField[f] = _t; H.LastHit[f] = _t;
            Emit(e);
        }

        /// <summary>One finger-damp slot per hand per field: the first resting finger sets it, the last lifted releases it.</summary>
        void AddFingerRest(HandState H, int hi, ref Contact c)
        {
            int n = ++H.RestSlots[c.Field];
            if (n == 1) Emit(new HandpanEvent { Type = HandpanEventType.Damp, Field = c.Field, Slot = hi, Kind = HandpanDampSource.Finger, Strength = 1,
                U = HandpanMath.Round3(c.U), V = HandpanMath.Round3(c.V), AfterStrike = c.Struck, Hand = hi });
        }

        void RemoveFingerRest(HandState H, int hi, int field)
        {
            int n = (H.RestSlots[field] > 0 ? H.RestSlots[field] : 1) - 1;
            H.RestSlots[field] = n;
            if (n == 0) Emit(new HandpanEvent { Type = HandpanEventType.Release, Field = field, Slot = hi, Hand = hi });
        }

        void ReleasePalm(HandState H, int hi)
        {
            for (int f = 0; f < NF; f++)
            {
                if (!H.PalmCovered[f]) continue;
                H.PalmCovered[f] = false;
                Emit(new HandpanEvent { Type = HandpanEventType.Release, Field = f, Slot = 2 + hi, Hand = hi });
            }
            Emit(new HandpanEvent { Type = HandpanEventType.Release, Field = TAK, Slot = 2 + hi, Hand = hi });
            H.PalmDamping = false; H.PalmRest.N = 0;
        }

        void ReleaseAll(HandState H, int hi)
        {
            if (H.PalmDamping) ReleasePalm(H, hi);
            foreach (PointerState p in H.Pointers)
            {
                if (!p.Contact.Has) continue;
                if (p.Contact.Damping) RemoveFingerRest(H, hi, p.Contact.Field);
                p.Contact = default;
            }
        }

        /// <summary>Every damping slot this logic holds, released as events (rig picked up, instrument put away): call
        /// before Reset when a unit plays its events.</summary>
        public int ReleaseHeld(double t)
        {
            _t = t; Count = 0;
            for (int hi = 0; hi < 2; hi++) ReleaseAll(_hands[hi], hi);
            return Count;
        }
    }
}
