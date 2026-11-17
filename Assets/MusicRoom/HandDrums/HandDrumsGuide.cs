// Sunny for the hand drums (handdrums.md 5.2, platform 8, review item 10): the tips as data, the features they cover,
// the anchors Sunny demonstrates at, the signals that trigger the technique tips (with their counters), and the demo
// hands: each tip's gesture as a pose over time that the stroke logic itself recognises as that stroke (tested), so
// Sunny never shows a gesture the drums would not play. Plain C#: phase B turns HdTip into the platform's TipSpec, the
// signals into InstrumentSignalEvent, and the demos into the requested demo kinds (CONTRACT-REQUESTS.md).
using System;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    public enum HdTipClass : byte { P1 = 1, P2 = 2, P3 = 3 }
    public enum HdTipTrigger : byte { FirstArrival, Signal, FirstLessonBlock }
    public enum HdTipSuccess : byte { None, EventType, Signal }
    /// <summary>The demo variants the hand drums need (review item 10.1): platform 8.3's Slap / Strike / Tap show other
    /// gestures (a palm at the edge then the centre; a finger tap; a 2 cm fingertip tap).</summary>
    public enum HdDemo : byte { None, OpenTone, PalmBass, CuppedSlap, FingersRest, HeelToe, PalmRest, Roll, SoftLoud }

    public sealed class HdTip
    {
        public string Id, Title, Line;
        public HdTipClass Class;
        public HdTipTrigger Trigger;
        public string TriggerArg;
        public HdDemo Demo;
        public string Anchor;
        public HdTipSuccess Success;
        public string SuccessArg;
        public string[] Covers;
    }

    public static class HandDrumsGuide
    {
        // ---- signals (platform 8.1: InstrumentSignalEvent { Kind, Signal }, "<id>.<signal>")
        public const string NeedOpen = "handdrums.needOpen", NeedBass = "handdrums.needBass", NeedSlap = "handdrums.needSlap", NeedMute = "handdrums.needMute",
            NeedHeel = "handdrums.needHeel", Ringing = "handdrums.ringing", NeedRoll = "handdrums.needRoll", NeedDynamics = "handdrums.needDynamics";
        public const string GotOpen = "handdrums.open", GotBass = "handdrums.bass", GotSlap = "handdrums.slap", GotMute = "handdrums.mute",
            GotHeelToe = "handdrums.heeltoe", GotQuiet = "handdrums.quiet", GotRoll = "handdrums.roll", GotDynamics = "handdrums.dynamics";

        // ---- anchors (IGuideAnchors): each head's centre; up = the head normal x the vibrating radius a (Sunny's presets read
        // the rim radius from |up|: OpenTone lands at 0.7 a toward the player, CuppedSlap at 0.8 a, PalmBass at the centre)
        public static readonly string[] Anchors = { HandDrumsDefinition.AnchorConga, HandDrumsDefinition.AnchorTumba };

        public static bool TryGetAnchor(Transform rig, string id, out Vector3 world, out Vector3 up)
        {
            world = up = default;
            int d = id == HandDrumsDefinition.AnchorConga ? 0 : id == HandDrumsDefinition.AnchorTumba ? 1 : -1;
            if (d < 0) return false;
            world = HandDrumsDims.HeadPoint(d, 0f, 0f);
            up = HandDrumsDims.Normal * HandDrumsDims.A[d];
            if (rig != null) { world = rig.TransformPoint(world); up = rig.TransformDirection(up); }
            return true;
        }

        // ---- the tips (lines in Resources/GuideLines/HandDrums.json, voices in Resources/Voice, made by tools/voice.mjs)
        public static readonly HdTip[] Tips =
        {
            new HdTip { Id = "tip_handdrums_arrive", Title = "Play the hand drums", Line = "tip_handdrums_arrive", Class = HdTipClass.P2, Trigger = HdTipTrigger.FirstArrival,
                Demo = HdDemo.OpenTone, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.EventType, SuccessArg = HandDrumsTake.TypeStroke, Covers = new[] { "handdrums.play" } },
            new HdTip { Id = "tip_handdrums_open", Title = "The open tone", Line = "tip_handdrums_open", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedOpen,
                Demo = HdDemo.OpenTone, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.Signal, SuccessArg = GotOpen, Covers = new[] { "handdrums.open" } },
            new HdTip { Id = "tip_handdrums_bass", Title = "The bass", Line = "tip_handdrums_bass", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedBass,
                Demo = HdDemo.PalmBass, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.Signal, SuccessArg = GotBass, Covers = new[] { "handdrums.bass" } },
            new HdTip { Id = "tip_handdrums_slap", Title = "The slap", Line = "tip_handdrums_slap", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedSlap,
                Demo = HdDemo.CuppedSlap, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.Signal, SuccessArg = GotSlap, Covers = new[] { "handdrums.slap" } },
            new HdTip { Id = "tip_handdrums_mute", Title = "Muted tones", Line = "tip_handdrums_mute", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedMute,
                Demo = HdDemo.FingersRest, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.Signal, SuccessArg = GotMute, Covers = new[] { "handdrums.mute" } },
            new HdTip { Id = "tip_handdrums_heeltoe", Title = "Heel and toe", Line = "tip_handdrums_heeltoe", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedHeel,
                Demo = HdDemo.HeelToe, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.Signal, SuccessArg = GotHeelToe, Covers = new[] { "handdrums.heeltoe" } },
            new HdTip { Id = "tip_handdrums_quiet", Title = "Quiet a drum", Line = "tip_handdrums_quiet", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = Ringing,
                Demo = HdDemo.PalmRest, Anchor = HandDrumsDefinition.AnchorTumba, Success = HdTipSuccess.Signal, SuccessArg = GotQuiet, Covers = new[] { "handdrums.rest" } },
            new HdTip { Id = "tip_handdrums_roll", Title = "A roll", Line = "tip_handdrums_roll", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedRoll,
                Demo = HdDemo.Roll, Anchor = HandDrumsDefinition.AnchorTumba, Success = HdTipSuccess.Signal, SuccessArg = GotRoll, Covers = new[] { "handdrums.roll" } },
            new HdTip { Id = "tip_handdrums_dynamics", Title = "Soft and loud", Line = "tip_handdrums_dynamics", Class = HdTipClass.P3, Trigger = HdTipTrigger.Signal, TriggerArg = NeedDynamics,
                Demo = HdDemo.SoftLoud, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.Signal, SuccessArg = GotDynamics, Covers = new[] { "handdrums.dynamics" } },
            new HdTip { Id = "tip_handdrums_lesson", Title = "Hand-drum lessons", Line = "tip_handdrums_lesson", Class = HdTipClass.P2, Trigger = HdTipTrigger.FirstLessonBlock,
                Demo = HdDemo.None, Anchor = HandDrumsDefinition.AnchorConga, Success = HdTipSuccess.EventType, SuccessArg = HandDrumsTake.TypeStroke, Covers = new[] { "handdrums.lessons" } },
        };

        /// <summary>Every feature of the drums (platform 8.5): covered by a tip, or Passive with a reason.</summary>
        public static readonly string[] Features = { "handdrums.play", "handdrums.open", "handdrums.bass", "handdrums.slap", "handdrums.mute", "handdrums.heeltoe",
            "handdrums.rest", "handdrums.roll", "handdrums.dynamics", "handdrums.lessons", "handdrums.glide", "handdrums.dent" };
        public static readonly (string feature, string reason)[] Passive =
        {
            ("handdrums.glide", "the head's physical response to a hard stroke (pitch settles); nothing to learn"),
            ("handdrums.dent", "the head gives under the hand; nothing to learn"),
        };

        /// <summary>The platform's TipSpecs (BUILD-GUIDE 7.1) from the tips above: the demos are the platform's hand-drum
        /// presets (OpenTone, PalmBass, CuppedSlap, HeelToe, Roll, PalmRest) at a head's centre anchor; the two variants the
        /// platform has no preset for (a muted tone: the fingers stay down; soft then loud) are Custom keyframes. The P3 tips
        /// fire on the module's signals (HandDrumsSignals), the lesson tip on the first hand-drum lesson's start.</summary>
        public static TipSpec[] TipSpecs()
        {
            var o = new TipSpec[Tips.Length];
            for (int i = 0; i < Tips.Length; i++)
            {
                HdTip t = Tips[i];
                var s = new TipSpec
                {
                    Id = t.Id, Title = t.Title, Lines = new[] { t.Line }, Class = (TipClass)(byte)t.Class, Anchor = t.Anchor,
                    Trigger = t.Trigger == HdTipTrigger.FirstArrival ? TipTrigger.FirstArrival : t.Trigger == HdTipTrigger.Signal ? TipTrigger.Signal : TipTrigger.LessonStart,
                    TriggerArg = t.Trigger == HdTipTrigger.Signal ? t.TriggerArg : t.Trigger == HdTipTrigger.FirstLessonBlock ? "" : null,
                    Success = t.Trigger == HdTipTrigger.FirstLessonBlock ? TipSuccess.LessonHit : t.Success == HdTipSuccess.Signal ? TipSuccess.Signal : t.Success == HdTipSuccess.EventType ? TipSuccess.EventType : TipSuccess.None,
                    SuccessArg = t.Trigger == HdTipTrigger.FirstLessonBlock ? null : t.SuccessArg,
                    Covers = (string[])t.Covers.Clone(),
                };
                s.Demo = DemoOf(t.Demo, out s.Motion);
                o[i] = s;
            }
            return o;
        }

        /// <summary>A demo variant as the platform's DemoKind (Custom with keys for the two it has no preset for).</summary>
        public static DemoKind DemoOf(HdDemo d, out DemoMotion motion)
        {
            motion = null;
            switch (d)
            {
                case HdDemo.OpenTone: return DemoKind.OpenTone;
                case HdDemo.PalmBass: return DemoKind.PalmBass;
                case HdDemo.CuppedSlap: return DemoKind.CuppedSlap;
                case HdDemo.HeelToe: return DemoKind.HeelToe;
                case HdDemo.PalmRest: return DemoKind.PalmRest;
                case HdDemo.Roll: return DemoKind.Roll;
                case HdDemo.FingersRest: motion = MutedTone; return DemoKind.Custom;
                case HdDemo.SoftLoud: motion = SoftThenLoud; return DemoKind.Custom;
                default: return DemoKind.None;
            }
        }

        // The custom demos in the anchor frame (y along the head normal, z toward the player): the flat hand over the near
        // edge at 0.7 a of the conga (where OpenTone lands), as the open-tone preset.
        const float EdgeZ = 0.10f;
        /// <summary>A muted tone: the flat fingers drop onto the edge and stay there 1 s, then lift.</summary>
        public static readonly DemoMotion MutedTone = new DemoMotion
        {
            Loop = 3f,
            Keys = new[]
            {
                new DemoKey(0.0f, new Vector3(0f, 0.04f, EdgeZ), GhostHand.Flat), new DemoKey(0.5f, new Vector3(0f, 0.04f, EdgeZ), GhostHand.Flat),
                new DemoKey(0.62f, new Vector3(0f, 0f, EdgeZ), GhostHand.Flat), new DemoKey(1.7f, new Vector3(0f, 0f, EdgeZ), GhostHand.Flat),
                new DemoKey(2.2f, new Vector3(0f, 0.04f, EdgeZ), GhostHand.Flat),
            },
        };
        /// <summary>Soft then loud: a small 1.5 cm bounce at the edge, then a big 8 cm one.</summary>
        public static readonly DemoMotion SoftThenLoud = new DemoMotion
        {
            Loop = 3f,
            Keys = new[]
            {
                new DemoKey(0.0f, new Vector3(0f, 0.015f, EdgeZ), GhostHand.Flat), new DemoKey(0.45f, new Vector3(0f, 0.015f, EdgeZ), GhostHand.Flat),
                new DemoKey(0.55f, new Vector3(0f, 0f, EdgeZ), GhostHand.Flat), new DemoKey(0.75f, new Vector3(0f, 0.015f, EdgeZ), GhostHand.Flat),
                new DemoKey(1.35f, new Vector3(0f, 0.08f, EdgeZ), GhostHand.Flat), new DemoKey(1.5f, new Vector3(0f, 0f, EdgeZ), GhostHand.Flat),
                new DemoKey(1.9f, new Vector3(0f, 0.05f, EdgeZ), GhostHand.Flat),
            },
        };

        /// <summary>The platform's FeatureSpecs: every feature, the two passive ones with their reason.</summary>
        public static FeatureSpec[] FeatureSpecs()
        {
            var o = new FeatureSpec[Features.Length];
            for (int i = 0; i < Features.Length; i++)
            {
                string reason = null;
                foreach (var p in Passive) if (p.feature == Features[i]) reason = p.reason;
                o[i] = new FeatureSpec(Features[i], reason);
            }
            return o;
        }
    }

    /// <summary>The technique tips' triggers and successes from the live strokes (review item 10.3). Feed every input event
    /// of a live hand and the drums' levels each frame; signals fire once each (until Reset), through the callback.
    /// Allocation-free (the signal strings are constants).</summary>
    public sealed class HandDrumsSignals
    {
        public const float RingLevel = 0.004f;     // output rms over a block: a drum audibly ringing
        public const double RingSeconds = 1.0;
        public int Strokes { get; private set; }
        public int RingEpisodes { get; private set; }
        public int Rests { get; private set; }
        public int AlternatingRun { get; private set; }
        public int Fired { get; private set; }
        readonly Action<string> _raise;
        bool _open, _bass, _slap, _mute, _heel, _toe, _soft, _loud, _roll4, _roll6;
        double _lastHeelMs = -1e9, _lastStrokeMs = -1e9, _softMs = -1e9, _loudMs = -1e9;
        int _lastHand = -1, _runAt;
        readonly double[] _runMs = new double[6];
        readonly double[] _ringFor = new double[2];
        readonly bool[] _ringing = new bool[2];
        int _flags;

        public HandDrumsSignals(Action<string> raise) { _raise = raise; }

        void Fire(int bit, string s) { if ((_flags & (1 << bit)) != 0) return; _flags |= 1 << bit; Fired++; _raise?.Invoke(s); }

        public void Reset() { _flags = 0; Fired = 0; Strokes = 0; RingEpisodes = 0; Rests = 0; _open = _bass = _slap = _mute = _heel = _toe = _soft = _loud = _roll4 = _roll6 = false; AlternatingRun = 0; }

        /// <summary>One event of the stroke logic (live only).</summary>
        public void Feed(in HdEvent e)
        {
            switch (e.Op)
            {
                case HdOp.Stroke:
                {
                    Strokes++;
                    double ms = e.TStar;
                    switch (e.Kind)
                    {
                        case HdKind.Open: _open = true; Fire(0, HandDrumsGuide.GotOpen); break;
                        case HdKind.Bass: _bass = true; Fire(1, HandDrumsGuide.GotBass); break;
                        case HdKind.Slap: _slap = true; Fire(2, HandDrumsGuide.GotSlap); break;
                        case HdKind.Heel: _heel = true; _lastHeelMs = ms; break;
                        case HdKind.Toe: _toe = true; if (ms - _lastHeelMs <= 1000) Fire(3, HandDrumsGuide.GotHeelToe); break;
                    }
                    if (e.U < 0.3) { _soft = true; _softMs = ms; }
                    if (e.U > 0.65) { _loud = true; _loudMs = ms; }
                    if (_soft && _loud && Math.Abs(_softMs - _loudMs) <= 4000) Fire(4, HandDrumsGuide.GotDynamics);
                    // alternating hands, each stroke within 0.25 s of the last: 4 in a row = "can roll"; 6 within 1.2 s = a roll
                    if (e.Hand != _lastHand && ms - _lastStrokeMs < 250) AlternatingRun++; else AlternatingRun = 1;
                    _runMs[_runAt++ % 6] = ms;
                    if (AlternatingRun >= 4) _roll4 = true;
                    if (AlternatingRun >= 6 && ms - _runMs[_runAt % 6] <= 1200) { _roll6 = true; Fire(5, HandDrumsGuide.GotRoll); }
                    _lastHand = e.Hand; _lastStrokeMs = ms;
                    // the needs, in the order of handdrums.md 5.2
                    if (Strokes >= 3 && !_open) Fire(8, HandDrumsGuide.NeedOpen);
                    if (Strokes >= 6 && !_bass) Fire(9, HandDrumsGuide.NeedBass);
                    if (Strokes >= 10 && !_slap) Fire(10, HandDrumsGuide.NeedSlap);
                    if (_slap && _open && !_mute) Fire(11, HandDrumsGuide.NeedMute);
                    if (Strokes >= 20 && !_heel) Fire(12, HandDrumsGuide.NeedHeel);
                    if (Strokes >= 30 && !_roll4) Fire(13, HandDrumsGuide.NeedRoll);
                    if (Strokes >= 12 && !(_soft && _loud)) Fire(14, HandDrumsGuide.NeedDynamics);
                    break;
                }
                case HdOp.Relabel:
                    _mute = true; Fire(6, HandDrumsGuide.GotMute);
                    break;
                case HdOp.Rest:
                    Rests++;
                    if (_ringing[e.Drum]) Fire(7, HandDrumsGuide.GotQuiet);
                    break;
            }
        }

        /// <summary>The drums' output levels (IHandDrumSound.Level) once a frame: two ringing episodes of 1 s with no rest
        /// yet raise "ringing".</summary>
        public void Tick(float level0, float level1, double dt)
        {
            for (int d = 0; d < 2; d++)
            {
                float l = d == 0 ? level0 : level1;
                if (l >= RingLevel)
                {
                    _ringFor[d] += dt;
                    if (!_ringing[d] && _ringFor[d] >= RingSeconds) { _ringing[d] = true; RingEpisodes++; }
                }
                else { _ringFor[d] = 0; _ringing[d] = false; }
            }
            if (RingEpisodes >= 2 && Rests == 0) Fire(15, HandDrumsGuide.Ringing);
        }

        public bool IsRinging(int drum) => _ringing[drum];
        /// <summary>A roll was played (6 alternating strokes within 1.2 s); a toe followed a heel.</summary>
        public bool Rolled => _roll6;
        public bool PlayedToe => _toe;
    }

    /// <summary>The demo hands (review item 10.1): each variant is the scripted-hand motion of the stroke it teaches, over
    /// a 3 s loop, in a drum's head frame; Pose gives the ghost hand's 26 joints (OpenXR order) for the guide's renderer.</summary>
    public static class HandDrumsDemos
    {
        /// <summary>One key: time (s in the loop), the palm centre over the head (rho, theta), lift (m), finger direction,
        /// curl 0..1, pitch (rad, + = fingers up).</summary>
        public struct Key
        {
            public float T, Rho, Theta, Lift, Dir, Curl, Pitch;
            public Key(float t, float rho, float theta, float lift, float curl = 0.05f, float pitch = 0f, float dir = Mathf.PI) { T = t; Rho = rho; Theta = theta; Lift = lift; Curl = curl; Pitch = pitch; Dir = dir; }
        }

        public const float Loop = 3f;

        /// <summary>A demo hand as world joints for a scripted hand source (the conformance fixture, the shots): the key's
        /// pose over drum d of the rig, positions and rotations (OpenXR order, 26 each).</summary>
        public static void WorldPose(Transform rig, int drum, int hand, in Key k, Vector3[] positions, Quaternion[] rotations)
        {
            var sample = new HdHandSample();
            Pose(drum, hand, k, sample, positions, rotations);
            if (rig == null) return;
            for (int j = 0; j < positions.Length; j++) { positions[j] = rig.TransformPoint(positions[j]); rotations[j] = rig.rotation * rotations[j]; }
        }

        static Key[] Stroke(float t0, float rho, float theta, float curl, float pitch, float from, float depth, float v, float stay = 0f, float up = 0.6f)
        {
            float down = (from + depth) / v;
            return stay > 0
                ? new[] { new Key(t0, rho, theta, from, curl, pitch), new Key(t0 + down, rho, theta, -depth, curl, pitch), new Key(t0 + down + stay, rho, theta, -depth, curl, pitch), new Key(t0 + down + stay + (from + depth) / up, rho, theta, from, curl, pitch) }
                : new[] { new Key(t0, rho, theta, from, curl, pitch), new Key(t0 + down, rho, theta, -depth, curl, pitch), new Key(t0 + down + (from + depth) / up, rho, theta, from, curl, pitch) };
        }

        static Key[] Cat(params Key[][] k) { int n = 0; foreach (var a in k) n += a.Length; var o = new Key[n]; int i = 0; foreach (var a in k) foreach (var x in a) o[i++] = x; return o; }

        /// <summary>The keys of a variant for one hand (null: that hand is not shown). The right hand demonstrates; the roll
        /// uses both. Speeds are the stroke logic's natural ones (1.2-1.6 m/s), so the demo is also a test fixture.</summary>
        public static Key[] Keys(HdDemo demo, int hand)
        {
            switch (demo)
            {
                case HdDemo.OpenTone: return hand == 1 ? Cat(Stroke(0.4f, 1.25f, 0.35f, 0.05f, 0f, 0.08f, 0.012f, 1.2f), Stroke(1.6f, 1.25f, 0.35f, 0.05f, 0f, 0.08f, 0.012f, 1.2f)) : null;
                case HdDemo.PalmBass: return hand == 1 ? Cat(Stroke(0.4f, 0.05f, 0f, 0.1f, 0f, 0.08f, 0.012f, 1.0f), Stroke(1.6f, 0.05f, 0f, 0.1f, 0f, 0.08f, 0.012f, 1.0f)) : null;
                case HdDemo.CuppedSlap: return hand == 1 ? Cat(Stroke(0.4f, 1.30f, 0.35f, 0.55f, 0f, 0.13f, -0.02f, 1.6f), Stroke(1.6f, 1.30f, 0.35f, 0.55f, 0f, 0.13f, -0.02f, 1.6f)) : null;
                case HdDemo.FingersRest: return hand == 1 ? Stroke(0.4f, 1.25f, 0.35f, 0.05f, 0f, 0.08f, 0.006f, 1.2f, 0.6f) : null;
                case HdDemo.HeelToe:
                    return hand == 0 ? new[] { new Key(0.3f, 0.42f, -0.35f, 0.06f, 0.2f, 0.55f), new Key(0.4f, 0.42f, -0.35f, 0.010f, 0.2f, 0.55f), new Key(0.65f, 0.42f, -0.35f, 0.010f, 0.2f, 0.55f),
                        new Key(0.75f, 0.42f, -0.35f, 0.010f, 0.25f, -0.12f), new Key(1.0f, 0.42f, -0.35f, 0.06f, 0.25f, -0.12f) } : null;
                case HdDemo.PalmRest: return hand == 1 ? new[] { new Key(0.3f, 0.15f, 0f, 0.06f, 0.1f), new Key(1.1f, 0.15f, 0f, -0.004f, 0.1f), new Key(2.4f, 0.15f, 0f, -0.004f, 0.1f), new Key(2.9f, 0.15f, 0f, 0.06f, 0.1f) } : null;
                case HdDemo.Roll:
                {
                    var k = new Key[6][];
                    for (int i = 0; i < 6; i++) k[i] = Stroke(0.4f + hand / 12f + i / 6f, 1.25f, hand == 1 ? 0.4f : -0.4f, 0.05f, 0f, 0.05f, 0.012f, 1.5f, 0f, 1.5f);
                    return Cat(k);
                }
                case HdDemo.SoftLoud: return hand == 1 ? Cat(Stroke(0.3f, 1.25f, 0.35f, 0.05f, 0f, 0.04f, 0.012f, 0.45f), Stroke(1.5f, 1.25f, 0.35f, 0.05f, 0f, 0.09f, 0.012f, 1.8f)) : null;
                default: return null;
            }
        }

        /// <summary>The pose parameters at loop time t (linear between keys, held outside them; null keys = hidden).</summary>
        public static bool At(Key[] keys, float t, out Key p)
        {
            p = default;
            if (keys == null || keys.Length == 0) return false;
            if (t <= keys[0].T) { p = keys[0]; return true; }
            for (int i = 0; i + 1 < keys.Length; i++)
            {
                Key a = keys[i], b = keys[i + 1];
                if (t > b.T) continue;
                float s = b.T > a.T ? (t - a.T) / (b.T - a.T) : 1f;
                p = new Key(t, Mathf.Lerp(a.Rho, b.Rho, s), Mathf.Lerp(a.Theta, b.Theta, s), Mathf.Lerp(a.Lift, b.Lift, s), Mathf.Lerp(a.Curl, b.Curl, s), Mathf.Lerp(a.Pitch, b.Pitch, s), Mathf.Lerp(a.Dir, b.Dir, s));
                return true;
            }
            p = keys[keys.Length - 1];
            return true;
        }

        static readonly float[,] ProxOff = { { 0.096f, 0.023f }, { 0.096f, 0.002f }, { 0.089f, -0.017f }, { 0.078f, -0.035f } };
        static readonly float[] Seg1 = { 0.038f, 0.042f, 0.038f, 0.03f }, Seg2 = { 0.046f, 0.052f, 0.049f, 0.04f };

        /// <summary>The hand of a key over drum d's head (rig-local), as the 14 points the stroke logic reads (HdHandSample) and
        /// as the 26 joints (OpenXR order) a ghost hand draws: metacarpals, distals and the thumb filled in along the bones.</summary>
        public static void Pose(int drum, int hand, in Key k, HdHandSample sample, Vector3[] joints26 = null, Quaternion[] rotations26 = null)
        {
            HdHead hd = HdHead.Make(D3.From(HandDrumsDims.HeadCentre[drum]), D3.From(HandDrumsDims.Normal), HandDrumsDims.A[drum]);
            D3 InPlane(double r, double th) => hd.C + (hd.E0 * (r * hd.A * Math.Cos(th)) + hd.E1 * (r * hd.A * Math.Sin(th)));
            D3 F = (hd.E0 * Math.Cos(k.Dir) + hd.E1 * Math.Sin(k.Dir)).Normalized;
            F = (F * Math.Cos(k.Pitch) + hd.N * Math.Sin(k.Pitch)).Normalized;
            D3 back = (hd.N - F * D3.Dot(hd.N, F)).Normalized;
            double side = hand == 1 ? 1 : -1;
            D3 S = D3.Cross(back, F) * side;
            D3 palm = InPlane(k.Rho, k.Theta) + hd.N * k.Lift + back * HdK.RPalm;
            D3 wrist = palm - F * 0.065;
            for (int f = 0; f < 4; f++)
            {
                D3 p = (wrist + F * ProxOff[f, 0]) + (S * ProxOff[f, 1] + back * (-HdK.RPalm + HdK.RProx[f]));
                double a1 = k.Curl * 0.6, a2 = k.Curl * 1.2;
                D3 m = p + (F * (Seg1[f] * Math.Cos(a1)) + back * (-Seg1[f] * Math.Sin(a1)));
                D3 t = m + (F * (Seg2[f] * Math.Cos(a2)) + back * (-Seg2[f] * Math.Sin(a2)));
                sample.Prox[f] = p; sample.Mid[f] = m; sample.Tip[f] = t;
                if (joints26 != null)
                {
                    int b0 = 6 + 5 * f;   // metacarpal, proximal, intermediate, distal, tip
                    joints26[b0] = (wrist + (p - wrist) * 0.35).ToVector3();
                    joints26[b0 + 1] = p.ToVector3(); joints26[b0 + 2] = m.ToVector3();
                    joints26[b0 + 3] = (m + (t - m) * 0.55).ToVector3(); joints26[b0 + 4] = t.ToVector3();
                }
            }
            sample.Wrist = wrist; sample.Palm = palm; sample.Back = back;
            if (rotations26 != null)
            {
                // every joint as the hand's frame: +Z along the fingers, +Y out of the back of the hand (HandFrame's convention)
                Quaternion q = Quaternion.LookRotation(F.ToVector3(), back.ToVector3());
                for (int j = 0; j < rotations26.Length; j++) rotations26[j] = q;
            }
            sample.Tracked = true; sample.HighConfidence = true; sample.HandScale = 1;
            for (int i = 0; i < 4; i++) sample.FingerConfidence[i] = true;
            if (joints26 != null)
            {
                joints26[(int)HandJoint.Palm] = palm.ToVector3(); joints26[(int)HandJoint.Wrist] = wrist.ToVector3();
                // the thumb: along the index side, relaxed beside the palm
                D3 tb = wrist + S * 0.028 + F * 0.03 - back * 0.012;
                for (int j = 0; j < 4; j++) joints26[2 + j] = (tb + (S * 0.012 + F * 0.03) * j).ToVector3();
            }
        }
    }
}
