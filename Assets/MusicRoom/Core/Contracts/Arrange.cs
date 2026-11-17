// "Move instruments": ONE setting, On or Off (2026-11-10, replacing the gold grab knobs, the old one-shot arrange mode and
// "Lock instruments"). Off (the default) the instruments stay where they are and play; no pinch ever moves one. On, the
// instruments are quiet, wear a soft outline, and a pinch anywhere on one picks it up: drag it along the floor, turn your
// wrist (or twist with two hands) to spin it all the way round, open the fingers to drop it. Layout implements
// IArrangeMode, persists the choice (AppSettings.MoveInstruments) and publishes ArrangeModeEvent; the Menu shows the
// "Move instruments" row (home page) and the "Move" button (Instruments screen), and the floating Done / Reset buttons
// while it is on; Core ticks no instrument but the menu while it is on (App.TickInstruments), Drums turns the nod kick and
// the stick glows off, Guide holds Sunny's tour.
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>The "Move instruments" setting. <see cref="MoveOn"/> is the player's choice (persisted); <see cref="Arranging"/>
    /// is whether it is in effect right now (the setting comes back on by itself after the headset was off, after mixed
    /// reality placed the piano, after a glide, once the start panel was answered).</summary>
    public interface IArrangeMode
    {
        /// <summary>Moving is in effect now: the instruments are outlined and quiet, and a pinch on one picks it up.</summary>
        bool Arranging { get; }
        /// <summary>The player's setting (persisted). Fakes and older hosts: the same as Arranging.</summary>
        bool MoveOn => Arranging;
        /// <summary>Turns the setting on (persisted) and moving starts at once where it can: outlines on, instruments silent,
        /// held sticks dropped, a running lesson or take playback stopped; publishes ArrangeModeEvent{Active = true}.</summary>
        void BeginArrange();
        /// <summary>Turns the setting off (persisted): lets go of anything held, saves the placement and plays again;
        /// ArrangeModeEvent{Active = false}.</summary>
        void EndArrange();
        void ToggleArrange() { if (MoveOn) EndArrange(); else BeginArrange(); }
        /// <summary>Forgets the saved placement of the current layout and lays it out around the player at the homes again
        /// (the piano at its home height). The setting stays as it is.</summary>
        void ResetArrangement();
    }

    /// <summary>Layout: moving started (Active) or stopped.</summary>
    public struct ArrangeModeEvent { public bool Active; }

    /// <summary>Layout: a still pinch on an instrument's body while "Move instruments" is off, which moves nothing (Sunny: turn
    /// on Move instruments in the menu to move it). Once per pinch.</summary>
    public struct InstrumentBodyPinchEvent { public InstrumentKind Kind; }

    /// <summary>Arrange-mode numbers (ARRANGE-NOTES.md has the reasoning behind each).</summary>
    public static class ArrangeConstants
    {
        /// <summary>A pinch this close to any part of an instrument (keys, heads and strings included) picks it up.</summary>
        public const float NearReach = 0.08f;
        /// <summary>The pinch must last this long before it takes hold (a debounce, not a dwell: no stillness test).</summary>
        public const float GrabDebounce = 0.08f;
        /// <summary>Ray grabs: pushing (or pulling) the hand along the ray moves the instrument this many times as far, in
        /// the room (the hit distance from the hand grows by (gain - 1) x the push).</summary>
        public const float RayPushGain = 2.5f;
        public const float RayMaxLength = 6f, RayMinDistance = 0.2f, RayIdleLength = 1.2f;
        /// <summary>Vertical follow of the in-play grab (the menu panel) starts after the hand moved this far up or down.</summary>
        public const float LiftDeadband = 0.02f;
        /// <summary>Piano key tops above the floor (home 0.72 m): from a low seat's elbows (0.55 m) to a standing player's
        /// (1.10 m, 2026-11-11: "allow it to place instruments to their height"); the stand stretches to the floor.</summary>
        public const float PianoKeyMin = 0.55f, PianoKeyMax = 1.10f;
        /// <summary>The drum kit's lift off the floor (its snare at 0.72 m + this): snare 0.72 .. 1.07 m, a standing kit at
        /// the top. Raised, it stands on a riser (Layout/RiserVisuals); it never goes into the floor (was -0.17 m: the
        /// kick and the stand feet sank through the real floor in mixed reality).</summary>
        public const float DrumsLiftMin = 0f, DrumsLiftMax = 0.35f;
        /// <summary>The Done / Reset buttons: ahead of the eyes, below them, to the left (seat frame, metres).</summary>
        public const float ButtonsAhead = 0.35f, ButtonsBelow = 0.15f, ButtonsLeft = 0.10f;
        /// <summary>A palm turned this far toward the head (cos 50 degrees) with a pinch is the OS system gesture, never a grab.</summary>
        public const float SystemPalmCos = 0.643f;
        public const float SystemPalmMaxDistance = 0.8f;
    }

    /// <summary>The feel of a drag in "Move instruments" (Layout/README.md, "Move instruments" has the reasoning).</summary>
    public static class MoveConstants
    {
        /// <summary>A drag holds while the grab pinch is on OR the thumb and index tips stay within HoldGap OR Meta's pinch
        /// strength stays at HoldStrength or more (the end side of the pinch hysteresis: the start needs the full grab
        /// pinch), and lets go only after it has been off for ReleaseGrace (a one-frame flicker never drops the instrument).
        /// A hand that loses tracking keeps the instrument where it is for LostGrace.</summary>
        public const float HoldGap = 0.045f, HoldStrength = 0.3f, ReleaseGrace = 0.12f, LostGrace = 0.3f;
        /// <summary>The dragged point's filter (One Euro: cutoff MinCutoff Hz at rest, + Beta Hz per m/s of hand speed, so it
        /// calms the tracker's millimetre jitter at rest and adds about a centimetre of lag at 0.5 m/s, never a rubber band).</summary>
        public const float SmoothMinCutoff = 2.5f, SmoothBeta = 10f, SmoothDerivCutoff = 1f;
        /// <summary>The wrist turn: filtered (One Euro, YawMinCutoff Hz + YawBeta Hz per rad/s), the first TurnDeadband
        /// radians (6 degrees) ignored, so the wrist roll of a closing pinch never swings a big instrument; beyond it the
        /// instrument spins TurnGain times the wrist about its own centre (yaw only), so a comfortable 90 degree wrist turn
        /// spins it about 170 degrees and two turns (or two hands) go all the way round.</summary>
        public const float YawMinCutoff = 1.5f, YawBeta = 2f, TurnDeadband = 0.1047198f, TurnGain = 2f;
        /// <summary>The settle after a drop: the instrument moves from where it was let go to its free spot in this time
        /// (ease out).</summary>
        public const float SettleSeconds = 0.25f;
        /// <summary>The height follows the hand 1:1 (its own filter, the drag's cutoffs) once the hand has moved more than
        /// LiftDeadband up or down from where it took hold (a level drag never bobs; the anchor then moves by the band, so it
        /// starts without a jump, and an end of the range reverses at once); a ray grab lifts with the hand itself, never with
        /// the ray's far end.</summary>
        public const float LiftDeadband = 0.02f;
    }

    /// <summary>The player's level (Layout/LayoutModule.Height.cs): every instrument's default height follows the player's
    /// eye height above the floor, kept across the headset coming off and on.</summary>
    public static class HeightConstants
    {
        /// <summary>Eyes this high above the floor are a seated player (the instruments' homes are seated heights): a low
        /// chair to a high one. Above, the excess lifts them (a bar stool, standing); below, the shortfall lowers them.</summary>
        public const float SeatedEyeMin = 1.05f, SeatedEyeMax = 1.30f;
        /// <summary>The player lift's range (each instrument clamps it to its own Seat.LiftMin / LiftMax).</summary>
        public const float PlayerLiftMin = -0.30f, PlayerLiftMax = 0.45f;
        /// <summary>A plausible eye height above the floor (outside it the head pose is not a worn, tracked headset).</summary>
        public const float EyeMin = 0.5f, EyeMax = 2.3f;
        /// <summary>The eyes' smoothing while playing (seconds, an exponential mean).</summary>
        public const float EyeTau = 1.0f;
        /// <summary>Back from the headset off / the system menu / a tracking loss: the eyes must be worn, tracked and within
        /// ReturnStill (max - min) for ReturnSettle seconds before they are compared with how they were.</summary>
        public const float ReturnSettle = 0.5f, ReturnStill = 0.08f;
        /// <summary>The eyes came back FollowMin..FollowMax higher or lower than they left (the system's floor estimate moved
        /// under a seated player, or they sit a little differently): every instrument moves by as much, so it stays at the
        /// player's level. More than FollowMax is a new posture (sat down / stood up): the lift for the eyes now.</summary>
        public const float FollowMin = 0.03f, FollowMax = 0.35f;
    }
}
