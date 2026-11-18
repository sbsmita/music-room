// Sunny (spec 08 section 3). The Guide module implements IGuide and publishes GuidePresenceEvent. Since the
// many-instrument platform (12-new-instruments/platform.md section 8), tips are data: each instrument descriptor carries
// its TipSpecs (and any module may add more through an IGuideTipSource), the voice lines live with the module that adds
// them (Resources/GuideLines/<Module>.json, Resources/VoiceManifests/<Module>.json, Resources/Voice/<id>.wav), and the
// Guide merges them all.
using System;
using UnityEngine;

namespace MusicRoom.Core
{
    public interface IGuide
    {
        /// <summary>Visible and in a live state (not idle, gone or leaving).</summary>
        bool Present { get; }
        /// <summary>The tour finished or was dismissed this launch.</summary>
        bool Done { get; }
        void Start();
        void Dismiss();
        void Toggle();
        /// <summary>A one-line tip outside the tour (a line id of the Guide's table, e.g. "tip_loop_start" on the first LOOP
        /// press, or a TipSpec id of a descriptor): Sunny appears, says it, then leaves silently; once heard it is never said
        /// again (persisted). Returns the seconds the line takes, so a caller may hold its own action until Sunny is done, or
        /// 0 when it is not said (already heard, unknown, or Sunny busy with the tour). The default (no Guide module) says
        /// nothing. Instruments call it for tips whose trigger is TipTrigger.Manual.</summary>
        float Tip(string id) => 0f;
        /// <summary>The tape deck's GUIDE button: start Sunny's long song (about a minute and a half: REC and a part, STOP,
        /// PLAY and REC on top for the next part, three parts, then PLAY for the playback show), or end whichever deck guide
        /// runs. The default does nothing.</summary>
        void ToggleDeckGuide() { }
        /// <summary>"Make a long song" (Sunny's menu page): the long song, as GUIDE starts it. The default: ToggleDeckGuide.</summary>
        void StartLongSong() => ToggleDeckGuide();
        /// <summary>"Loop a beat" (Sunny's menu page): the one-minute loop show (snow on, a drum loop and a second layer, STOP,
        /// PLAY for the playback show). The default: ToggleDeckGuide.</summary>
        void StartLoopShow() => ToggleDeckGuide();
        /// <summary>"Make a song in layers": the full hands-on looper walkthrough (recording, playing back, looping drums, harp
        /// and piano in layers, mute, undo, clear); no menu row since 2026-11-18. The default: ToggleDeckGuide.</summary>
        void StartDeckWalkthrough() => ToggleDeckGuide();
        /// <summary>A deck guide (the long song, the loop show or the walkthrough) runs (the GUIDE button's light).</summary>
        bool DeckGuideRunning => false;
        /// <summary>The tip (or line) was said once (persisted, musicroom.tipsSeen). The default: false.</summary>
        bool TipSeen(string id) => false;
        /// <summary>Sunny is showing a pinch right now (a tip or step whose ghost hand pinches: a stick, the hi-hat stand,
        /// turning the seat, moving the menu...): the player's next pinch is for that, so look and pinch stands down. The
        /// default: false.</summary>
        bool TeachingPinch => false;
    }

    /// <summary>What the ghost hand demonstrates (the tour steps' demos, plus the platform's kinds). The first six are the
    /// tour's (Guide implements them as before). Tap .. Point are keyframed presets anchored at a TipSpec.Anchor
    /// (IGuideAnchors: world point and up): Tap fingertips together, two light 2 cm taps; Slap an open palm, a 5 cm slap at
    /// the edge, then the centre; MalletStrike a held mallet, two 6 cm strokes; Approach the right hand 25 cm toward the
    /// anchor and back; Circle a pinched stick around the anchor's rim (radius = |up|); Strike one 4 cm fingertip stroke;
    /// Hold the hand lowered onto the anchor and held 1 s; PalmRest a flat palm laid on it; Pinch a pinch at the anchor (a
    /// tool pick-up); Point the index pointing at it from 30 cm. Custom: the TipSpec's own DemoMotion keyframes.
    /// The instruments' variants (pre-declared in phase 0 so no Guide change is needed later; all keyframed presets):
    /// OpenTone flat fingers bounce off the edge; PalmBass a flat palm drops onto the centre; CuppedSlap cupped fingers snap
    /// down at the edge; HeelToe the hand rocks heel then fingertips; Roll two hands alternate quickly; MalletHold a held
    /// mallet strikes and stays down 0.6 s; OpenHand the hand opens (letting a tool go); Hover an open palm held 3 cm above
    /// the anchor for 3 s; Lift / Lower the palm rises / sinks 12 cm above the anchor; Rock the hand rocks +-15 deg; Dip a
    /// quick 4 cm dip and back; Curl the fingers curl a little and open; FingerCircle the index circles the rim 1 cm below the
    /// lip, one turn in 1.4 s.</summary>
    public enum DemoKind : byte
    {
        None = 0, Palm, Poke, Swipe, Nod, Press,
        Tap, Slap, MalletStrike, Approach, Circle,
        Strike, Hold, PalmRest, Pinch, Point,
        Custom,
        OpenTone, PalmBass, CuppedSlap, HeelToe, Roll, MalletHold, OpenHand,
        Hover, Lift, Lower, Rock, Dip, Curl, FingerCircle,
        // First five (Guide/FirstRun/SunnyDemoWorld.cs, the tour's tips): Sweep an open hand sweeps across the harp strings;
        // PinchPull a pinch pulls toward the player (turning the seat); Twist a pinched hand turns (an instrument held);
        // Drag a fingertip drags up a list (scrolling).
        Sweep, PinchPull, Twist, Drag,
    }

    /// <summary>The ghost hand's shape during a keyframed demo.</summary>
    public enum GhostHand : byte { Point = 0, Flat, Cupped, Pinch, Fist, Open, Mallet, Stick }

    /// <summary>One keyframe of a custom demo: at T seconds into the loop the hand's index tip (or the held tool's tip) is at
    /// Offset in the anchor frame (y along the anchor's up, z toward the player's head, x completing a right-handed frame:
    /// to the player's right). Between keys the motion eases (smoothstep).</summary>
    public readonly struct DemoKey
    {
        public readonly float T;
        public readonly Vector3 Offset;
        public readonly GhostHand Hand;
        public DemoKey(float t, Vector3 offset, GhostHand hand = GhostHand.Point) { T = t; Offset = offset; Hand = hand; }
    }

    /// <summary>A custom ghost-hand demo (DemoKind.Custom): keyframes over one loop of Loop seconds.</summary>
    public sealed class DemoMotion
    {
        public float Loop = 3f;
        public bool LeftHand;
        public DemoKey[] Keys = Array.Empty<DemoKey>();
    }

    /// <summary>When Sunny offers a tip. FirstArrival: the instrument's first InstrumentMoveEvent{Arrived} at the seat;
    /// FirstDisplayedGaze: the player looks at it on display for 3 s; IdleNear: at the seat, a hand near it, not played for
    /// TriggerArg seconds (default 8); Signal: an InstrumentSignalEvent whose Signal equals TriggerArg ("&lt;id&gt;.&lt;signal&gt;");
    /// Manual: the instrument (or any module) calls IGuide.Tip(id) itself, for any condition it detects (counters,
    /// field-filtered events, sequences: count them in the module and publish a Signal, or call Tip).
    /// LessonStart: the first lesson of this instrument starts (TriggerArg: a lesson id, or empty for any).</summary>
    public enum TipTrigger : byte { FirstArrival = 0, FirstDisplayedGaze, IdleNear, Signal, Manual, LessonStart }

    /// <summary>sunny-tour 3.1: P1 must-know (interrupts), P2 when it first matters, P3 nice to know (rate limited).</summary>
    public enum TipClass : byte { P1 = 1, P2 = 2, P3 = 3 }

    /// <summary>When a tip counts as learned (it ends early and is not offered again): EventType a live TakeEvent of type
    /// SuccessArg (empty: any live Onset of the instrument); Signal an InstrumentSignalEvent SuccessArg; None never (the line
    /// is enough). Anything finer (a zone, a level, a sequence) is the instrument's to detect: it publishes a Signal.
    /// Facing: the instrument becomes ILayoutService.Facing; LessonHit: a lesson target of the instrument is hit.</summary>
    public enum TipSuccess : byte { EventType = 0, Signal, None, Facing, LessonHit }

    /// <summary>One Sunny tip (platform.md 8.1). Id "tip_&lt;id&gt;_*" (the classic three and the platform keep their names).
    /// Lines: voice line ids said in order (each needs a GuideLines text, a VoiceManifests length and a Resources/Voice WAV
    /// unless TextOnly); Again: the line for a repeat ("again" when null). Covers: the feature ids it teaches.</summary>
    public sealed class TipSpec
    {
        public string Id, Title;
        public string[] Lines = Array.Empty<string>();
        public string Again;
        public TipTrigger Trigger;
        public string TriggerArg;
        public TipClass Class = TipClass.P3;
        public DemoKind Demo;
        public string Anchor;
        public DemoMotion Motion;
        /// <summary>An optional second demo played after the first (tap, then circle); ThenAnchor null = Anchor.</summary>
        public DemoKind ThenDemo;
        public string ThenAnchor;
        public TipSuccess Success;
        public string SuccessArg;
        public string[] Covers = Array.Empty<string>();
        /// <summary>Shown in a headset session only (the *_vr lines).</summary>
        public bool VrOnly;
        /// <summary>A caption only, no voice clip yet (the phase-0 stubs): the WAV and manifest checks skip it, and Sunny shows
        /// Title as the caption.</summary>
        public bool TextOnly;
        public override string ToString() => Id;
    }

    /// <summary>Tips that do not belong to an instrument descriptor (the platform's own, the sheet music's): any module adds
    /// a parameterless class implementing this; the Guide discovers them by reflection like IInstrumentDefinition.</summary>
    public interface IGuideTipSource
    {
        TipSpec[] Tips();
        FeatureSpec[] Features();
    }

    /// <summary>Instruments publish this for gestures that are not take events (a power switch, a tool pick-up, a technique
    /// detected), so no Core enum grows per instrument. Signal: "&lt;id&gt;.&lt;signal&gt;" ("handpan.lift"); the sheet music
    /// uses "sheet.&lt;signal&gt;" with Kind Piano.</summary>
    public struct InstrumentSignalEvent { public InstrumentKind Kind; public string Signal; }
}
