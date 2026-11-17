// Sunny's handpan tips (handpan.md 8.2, revised; BUILD-GUIDE 7): the platform's TipSpec data for the descriptor, the feature
// ids, the signals the instrument publishes (HandpanTipCounters counts them), and the guide anchors. Every feature of the
// instrument is covered by a tip or marked passive with a reason; every line is in Resources/GuideLines/Handpan.json with its
// clip in Resources/Voice and its length in Resources/VoiceManifests/Handpan.json (the project's eSpeak voice pipeline).
// HandpanTipTruthTests checks that each line says what the instrument really does.
using MusicRoom.Core;

namespace MusicRoom.Handpan
{
    public static class HandpanTips
    {
        // feature ids (prefixed "handpan.")
        public const string FStrike = "handpan.strike", FLift = "handpan.lift", FDing = "handpan.ding", FGentle = "handpan.gentle", FSpot = "handpan.spot",
            FMute = "handpan.mute", FSlap = "handpan.slap", FTak = "handpan.tak", FLoop = "handpan.loop", FIsolation = "handpan.isolation";
        public const string IsolationReason = "Harmonic isolation (a finger resting on a mode's nodal line leaves that overtone singing) is an advanced technique that emerges from the finger rest, which tip_handpan_mute teaches; first-time users are not asked to find nodal lines.";
        public static readonly string[] FeatureIds = { FStrike, FLift, FDing, FGentle, FSpot, FMute, FSlap, FTak, FLoop, FIsolation };
        public static FeatureSpec[] Features() => new FeatureSpec[] { FStrike, FLift, FDing, FGentle, FSpot, FMute, FSlap, FTak, FLoop, new FeatureSpec(FIsolation, IsolationReason) };

        // signals (InstrumentSignalEvent.Signal): triggers ...
        public const string SignalLift = "handpan.lift", SignalHard = "handpan.hard", SignalCentreOnly = "handpan.centreOnly", SignalRinging = "handpan.ringing",
            SignalNoDing = "handpan.noDing", SignalNoSlap = "handpan.noSlap", SignalNoTak = "handpan.noTak", SignalLoop = "handpan.loopIdle";
        // ... and successes
        public const string SignalRingFree = "handpan.ringFree", SignalDingStrike = "handpan.dingStrike", SignalSoft = "handpan.soft", SignalEdge = "handpan.edgeStrike",
            SignalSlapStrike = "handpan.slapStrike", SignalTakStrike = "handpan.takStrike";

        public const string EventStrike = "handpan", EventMute = "handpanMute";

        static TipSpec Tip(string id, string title, TipClass cls, TipTrigger trigger, string triggerArg, DemoKind demo, string anchor, TipSuccess success, string successArg, string feature) =>
            new TipSpec { Id = id, Title = title, Lines = new[] { id }, Class = cls, Trigger = trigger, TriggerArg = triggerArg, Demo = demo, Anchor = anchor, Success = success, SuccessArg = successArg, Covers = new[] { feature } };

        /// <summary>The nine tips; Title is the line's caption (= its text in GuideLines/Handpan.json).</summary>
        public static TipSpec[] All() => new[]
        {
            Tip("tip_handpan_arrive", "Tap the round notes gently with your fingertips.", TipClass.P2, TipTrigger.FirstArrival, null, DemoKind.Tap, HandpanAnchors.Above + "1", TipSuccess.EventType, EventStrike, FStrike),
            Tip("tip_handpan_lift", "Tap, then lift quickly so the note rings.", TipClass.P2, TipTrigger.Signal, SignalLift, DemoKind.Tap, HandpanAnchors.Above + "2", TipSuccess.Signal, SignalRingFree, FLift),
            Tip("tip_handpan_ding", "The deep note in the middle is the ding.", TipClass.P3, TipTrigger.Signal, SignalNoDing, DemoKind.Tap, HandpanAnchors.Above + "0", TipSuccess.Signal, SignalDingStrike, FDing),
            Tip("tip_handpan_gentle", "Gentle taps sound sweeter, hard taps brighter.", TipClass.P3, TipTrigger.Signal, SignalHard, DemoKind.Tap, HandpanAnchors.Above + "2", TipSuccess.Signal, SignalSoft, FGentle),
            Tip("tip_handpan_spot", "Tap near a dimple's edge for a richer note.", TipClass.P3, TipTrigger.Signal, SignalCentreOnly, DemoKind.Tap, HandpanAnchors.Edge + "1", TipSuccess.Signal, SignalEdge, FSpot),
            Tip("tip_handpan_mute", "Rest your hand on the steel to quiet it.", TipClass.P3, TipTrigger.Signal, SignalRinging, DemoKind.PalmRest, HandpanAnchors.Field + "0", TipSuccess.EventType, EventMute, FMute),
            Tip("tip_handpan_slap", "Slap the middle with a flat hand for a boom.", TipClass.P3, TipTrigger.Signal, SignalNoSlap, DemoKind.Slap, HandpanAnchors.Field + "0", TipSuccess.Signal, SignalSlapStrike, FSlap),
            Tip("tip_handpan_tak", "Tap between the notes for a soft click.", TipClass.P3, TipTrigger.Signal, SignalNoTak, DemoKind.Tap, HandpanAnchors.TakPoint, TipSuccess.Signal, SignalTakStrike, FTak),
            Tip("tip_handpan_loop", "Play the handpan over your loop. It's in tune.", TipClass.P3, TipTrigger.Signal, SignalLoop, DemoKind.None, HandpanAnchors.Field + "0", TipSuccess.EventType, EventStrike, FLoop),
        };

        /// <summary>Every anchor the tips and the lessons name (the descriptor's Anchors; conformance 13 resolves each).</summary>
        public static string[] Anchors()
        {
            var a = new System.Collections.Generic.List<string> { HandpanAnchors.TakPoint };
            for (int i = 0; i < HandpanSpec.FieldCount; i++) { a.Add(HandpanAnchors.Field + i); a.Add(HandpanAnchors.Above + i); a.Add(HandpanAnchors.Edge + i); }
            return a.ToArray();
        }
    }

    /// <summary>Sunny's demo targets on the handpan (IGuideAnchors ids, documented in the README):
    ///   "handpan.field.&lt;i&gt;" the dimple apex of field i (up = the field normal);
    ///   "handpan.above.&lt;i&gt;" 8 cm above it (a hovering demo hand); "handpan.edge.&lt;i&gt;" the dimple edge (u 0.35);
    ///   "handpan.tak" the steel between A3 and C4.</summary>
    public static class HandpanAnchors
    {
        public const string Field = "handpan.field.", Above = "handpan.above.", Edge = "handpan.edge.", TakPoint = "handpan.tak";

        public static bool TryGet(string anchor, in HandpanPlace place, out D3 world, out D3 up)
        {
            world = default; up = new D3(0, 1, 0);
            if (anchor == null) return false;
            if (anchor == TakPoint)
            {
                D3 p = HandpanMath.CapPoint(HandpanSpec.TakDefaultX, HandpanSpec.TakDefaultZ, true, out D3 n);
                world = place.ToWorld(p); up = place.DirToWorld(n).Normalized;
                return true;
            }
            string prefix = anchor.StartsWith(Field, System.StringComparison.Ordinal) ? Field : anchor.StartsWith(Above, System.StringComparison.Ordinal) ? Above
                : anchor.StartsWith(Edge, System.StringComparison.Ordinal) ? Edge : null;
            if (prefix == null || !int.TryParse(anchor.Substring(prefix.Length), out int i) || (uint)i >= HandpanSpec.FieldCount) return false;
            if (prefix == Edge)
            {
                HandpanMath.MapAt(i, 0.35, 0, out double X, out double Z);
                D3 p = HandpanMath.CapPoint(X, Z, true, out D3 n);
                world = place.ToWorld(p); up = place.DirToWorld(n).Normalized;
                return true;
            }
            world = HandpanMath.FieldApexWorld(i, place, out up);
            if (prefix == Above) world = world + up * 0.08;
            return true;
        }
    }

    /// <summary>The tips' counters (handpan.md 8.2's triggers and successes): fed every live play event, it returns the
    /// signals to publish (Out[0..Count)). Allocation-free.</summary>
    public sealed class HandpanTipCounters
    {
        public readonly string[] Out = new string[4];
        public int Count { get; private set; }
        int _ring, _strikes, _centreRun, _softRun, _freeRun, _hardN;
        bool _ding, _slapped, _tak, _noDingSent, _noSlapSent, _noTakSent, _centreSent, _ringingOnce;
        readonly double[] _hard = new double[3];

        void Emit(string s) { if (Count < Out.Length) Out[Count++] = s; }

        /// <summary>A live strike: field (9 = tak), velocity, normalised radius rho, technique, time (s).</summary>
        public int Strike(int field, double vel, double rho, HandpanTechKind tech, double t)
        {
            Count = 0;
            _strikes++;
            if (tech == HandpanTechKind.Tak) { _tak = true; Emit(HandpanTips.SignalTakStrike); }
            else if (tech == HandpanTechKind.Slap) { _slapped = true; if (field == 0) Emit(HandpanTips.SignalSlapStrike); }
            if (field == 0 && tech != HandpanTechKind.Tak) { _ding = true; Emit(HandpanTips.SignalDingStrike); }
            if (field > 0 && field < 9 && tech == HandpanTechKind.Tip) _ring++;
            if (!_ding && !_noDingSent && _ring >= 5) { _noDingSent = true; Emit(HandpanTips.SignalNoDing); }
            if (!_slapped && !_noSlapSent && _strikes >= 20) { _noSlapSent = true; Emit(HandpanTips.SignalNoSlap); }
            if (!_tak && !_noTakSent && _strikes >= 30) { _noTakSent = true; Emit(HandpanTips.SignalNoTak); }
            if (tech == HandpanTechKind.Tip && field < 9)
            {
                if (rho < 0.15) { if (++_centreRun >= 10 && !_centreSent) { _centreSent = true; Emit(HandpanTips.SignalCentreOnly); } }
                else { _centreRun = 0; if (rho > 0.25 && rho < 0.6) Emit(HandpanTips.SignalEdge); }
                if (vel > 0.9)
                {
                    _softRun = 0;
                    _hard[_hardN % 3] = t; _hardN++;
                    if (_hardN >= 3 && t - _hard[(_hardN - 3) % 3] <= 10.0) { Emit(HandpanTips.SignalHard); _hardN = 0; }
                }
                else if (vel < 0.6 && ++_softRun >= 3) { _softRun = 0; Emit(HandpanTips.SignalSoft); }
            }
            return Count;
        }

        /// <summary>A sounded note rang free for 0.5 s (true) or its own finger muted it within 0.5 s (false).</summary>
        public int RangFree(bool free)
        {
            Count = 0;
            if (!free) _freeRun = 0;
            else if (++_freeRun >= 3) { _freeRun = 0; Emit(HandpanTips.SignalRingFree); }
            return Count;
        }

        /// <summary>Polled about once a second: fields ringing above -40 dB for 3 s (the 2nd time it happens), a loop playing
        /// while the handpan has not been played during it.</summary>
        public int Poll(bool manyRinging3s, bool loopIdle)
        {
            Count = 0;
            if (manyRinging3s) { if (_ringingOnce) Emit(HandpanTips.SignalRinging); _ringingOnce = true; }
            if (loopIdle) Emit(HandpanTips.SignalLoop);
            return Count;
        }
    }

    public enum HandpanTechKind : byte { Tip = 0, Slap = 1, Tak = 2 }
}
