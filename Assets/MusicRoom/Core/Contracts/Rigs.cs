// Rig, layout, grab and placement contracts (spec 05 sections 7-8, spec 07 section 8, spec 08 section 5).
// Layout implements IRigRegistry / ILayoutService / IGrabService / IPlacementService; instrument modules build a
// Rig and register it; the menu registers its own Rig (RigId.Lessons) too.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>The id block (platform.md section 2): RigId value = InstrumentKind value (Lessons = 4 = Menu). Persisted by its
    /// name in ArrangementCodec, so names never change either. 8 is retired (RetiredInstruments): never reused.</summary>
    public enum RigId { Piano = 0, Drums = 1, Harp = 2, Table = 3, Lessons = 4, Handpan = 5, HandDrums = 6, Marimba = 7, Bowls = 9 }

    /// <summary>Where an instrument is (ILayoutService.PlaceOf): at the seat (played), on display in the VR room (static,
    /// silent), gliding between the two, or hidden (mixed reality, a blocked display spot, its module not loaded).</summary>
    public enum InstrumentPlace : byte { Seat = 0, Display, Gliding, Hidden }

    /// <summary>What adding an instrument to My set would do (ILayoutService.PreviewJoin, the seat planner's join()).</summary>
    public enum JoinAction : byte { None = 0, Already, Add, Replace, Alone }

    public readonly struct JoinPreview
    {
        public readonly JoinAction Action;
        /// <summary>The member that goes back to its place (Action Replace).</summary>
        public readonly InstrumentKind Replaced;
        public JoinPreview(JoinAction action, InstrumentKind replaced = InstrumentKind.Table) { Action = action; Replaced = replaced; }
        public bool Replaces => Action == JoinAction.Replace;
        public override string ToString() => Action == JoinAction.Replace ? $"Replace {Replaced}" : Action.ToString();
    }

    public enum LayoutMode { All = 0, Single = 1 }

    /// <summary>A rig's zone for the grab guards and "near an instrument" (world test). The piano's is its InPlayZone box;
    /// the other rigs' is the rig zone: the group's world box (every mesh, the contact
    /// shadow's floor rectangle included) expanded by GrabConstants.ZonePad (.12). Play surfaces are
    /// IInstrument.PlaySurfaceAt.</summary>
    public interface IPlayZone
    {
        bool Contains(Vector3 world);
    }

    /// <summary>One movable thing in the room (the classic rigs: piano, drums, harp, table, lessons panel).</summary>
    public sealed class Rig
    {
        public RigId Id;
        /// <summary>The transform the layout moves. Instrument-local origin conventions: piano = white key tops; drums and harp = floor.</summary>
        public Transform Group;
        public readonly List<IInstrument> Instruments = new List<IInstrument>();
        public IPlayZone Zone;
        /// <summary>Visible meshes whose bounds are grab handles (played parts excluded by the module). Filled ONCE by the
        /// instrument in Init (mutate the same list in place if parts change); readers never allocate (Layout's GrabTarget
        /// runs every frame during a dwell). The placement footprint, eye clearance and the desktop Shift / Alt pick use
        /// these plus the PlayedParts of the rig's instruments (placement measures every visible mesh of a rig).</summary>
        public IReadOnlyList<Renderer> Handles = Array.Empty<Renderer>();
        /// <summary>Home in the seat frame (Unity, x right, +z ahead) for LayoutMode.All, and its yaw in degrees.</summary>
        public Vector3 HomeAll; public float HomeYawAll;
        /// <summary>Home for LayoutMode.Single (instrument rigs only; the table stays put).</summary>
        public Vector3 HomeSingle; public float HomeYawSingle;
        /// <summary>Placement priority: piano 0 &gt; drums 1 &gt; harp 2 &gt; table 3; the panel is placed by clearPanel.</summary>
        public int Priority;
        public bool InScene = true;
        /// <summary>Set by Layout while held / settling; read by instruments (muted) and the audio listener bookkeeping.</summary>
        public bool Held;
        public override string ToString() => Id.ToString();
    }

    public interface IRigRegistry
    {
        IReadOnlyList<Rig> Rigs { get; }
        Rig Get(RigId id);
        void Register(Rig rig);
        void Unregister(Rig rig);
        event Action Changed;
    }

    /// <summary>The seat frame: head on the floor plus the flattened heading.</summary>
    public struct SeatFrame
    {
        public Vector3 Head;        // camera position
        public float YawDeg;        // atan2(fwd.x, fwd.z) in degrees (Unity yaw)
        public Vector3 Forward => Quaternion.Euler(0f, YawDeg, 0f) * Vector3.forward;
        public Vector3 Right => Quaternion.Euler(0f, YawDeg, 0f) * Vector3.right;
        /// <summary>World position of a seat-relative home (x right, y up, +z ahead).</summary>
        public Vector3 Place(Vector3 home) => new Vector3(Head.x, 0f, Head.z) + Quaternion.Euler(0f, YawDeg, 0f) * new Vector3(home.x, 0f, home.z) + Vector3.up * home.y;
        public float Yaw(float homeYawDeg) => homeYawDeg + YawDeg;
        public static SeatFrame From(Transform head)
        {
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            return new SeatFrame { Head = head.position, YawDeg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg };
        }
    }

    public interface ILayoutService
    {
        LayoutMode Mode { get; }
        InstrumentKind Current { get; }
        bool IsTurning { get; }
        bool FirstArrangeDone { get; }
        SeatFrame Seat { get; }
        /// <summary>Set the layout: membership, homes, re-arrange, LayoutChangedEvent.</summary>
        void Set(LayoutMode mode, InstrumentKind instrument);
        /// <summary>Page turn in single mode: +1 next, -1 previous (ROW = piano, drums, harp).</summary>
        void Next(int direction);
        /// <summary>Arrange around the player: seat-frame homes, placement, panel home. In VR also fitSeat and
        /// the room box; in mixed reality (IMixedReality.Active) neither (placement inRoom = !ar.active). movePiano false
        /// keeps the piano where it is (every caller passes true today). force: published in ArrangedEvent.
        /// The piano keeps its current height (a table in MR); MR resets it before its first arrange.</summary>
        void ArrangeAroundPlayer(bool force, bool movePiano = true);
        /// <summary>MR, the piano just placed on a real surface: the placed rigs take their
        /// VR-room positions relative to the piano, shown again, settled with the piano fixed (placement fixed: [piano]).</summary>
        void PlaceAroundPiano();
        /// <summary>Leaving MR: the piano and the rigs back at their homes about the seat.</summary>
        void RestoreHomes();
        /// <summary>The group everything is arranged around (the piano in All mode, the current
        /// instrument in Single), e.g. for MR's shadow catcher and main light.</summary>
        Pose Anchor { get; }
        bool IsInScene(InstrumentKind instrument);

        // ---- the many-instrument platform (platform.md 4.2-4.6). Every member has a default body, so stand-ins compile.

        /// <summary>My set: the at-seat members of LayoutMode.All in planner order (up to MySetMax). Persisted as
        /// musicroom.set. The default: the classic three.</summary>
        IReadOnlyList<InstrumentKind> MySet => s_defaultSet;
        /// <summary>Counts changes of My set (LayoutChangedEvent.SetVersion).</summary>
        int SetVersion => 0;
        /// <summary>The instrument would join My set without replacing anyone.</summary>
        bool CanAdd(InstrumentKind instrument) => false;
        /// <summary>What Bring(instrument) would do in My set.</summary>
        JoinPreview PreviewJoin(InstrumentKind instrument) => new JoinPreview(IsInScene(instrument) ? JoinAction.Already : JoinAction.None);
        /// <summary>The member that would go back to make room for <paramref name="adding"/> (the ray summon's preview lift).</summary>
        InstrumentKind? ReplaceCandidate(InstrumentKind adding) { JoinPreview p = PreviewJoin(adding); return p.Replaces ? p.Replaced : (InstrumentKind?)null; }
        /// <summary>Make <paramref name="set"/> My set (planner checked; false when it does not fit). Glides the changes.</summary>
        bool TrySetSet(IReadOnlyList<InstrumentKind> set) => false;
        /// <summary>Bring the instrument to the seat: in One alone it replaces the current one; in My set it joins (or replaces
        /// the member PreviewJoin names). Reason Menu refuses JoinAction.Alone (the menu says "No room: take one out
        /// first"); the other reasons let it come alone. Glides in the VR room, slides in mixed reality. False when nothing
        /// changed (already here, not loaded, refused).</summary>
        bool Bring(InstrumentKind instrument, BringReason reason) => false;
        /// <summary>My set: take a member out (it goes back to its place). False for the last member or a non-member.</summary>
        bool PutAway(InstrumentKind instrument) => false;
        InstrumentPlace PlaceOf(InstrumentKind instrument) => IsInScene(instrument) ? InstrumentPlace.Seat : InstrumentPlace.Hidden;
        /// <summary>The seat slot of an at-seat instrument (One alone: Front), null otherwise.</summary>
        SeatSlot? SlotOf(InstrumentKind instrument) => null;
        /// <summary>The at-seat instrument the player faces (its slot within +-30 deg of the gaze, 10 deg hysteresis, 0.3 s
        /// dwell); One alone: Current. Null when none. FocusEvent on change.</summary>
        InstrumentKind? Facing => Mode == LayoutMode.Single ? Current : (InstrumentKind?)null;
        /// <summary>The instrument is gliding to or from the seat (not present, not planned).</summary>
        bool IsMoving(InstrumentKind instrument) => false;
        /// <summary>The instrument is gliding from the seat to its display place in the VR room (a real glide, not a slide or
        /// a reseat): an instrument with a separate display pose (the handpan's wall bracket) eases into it only then.</summary>
        bool IsGoingToDisplay(InstrumentKind instrument) => false;
        bool AnyMoving => false;
        /// <summary>The "lead" instrument mixed reality arranges around (platform.md 9.2): the My set member in slot Front,
        /// else the first member in carousel order; One alone: Current.</summary>
        InstrumentKind Lead => Mode == LayoutMode.Single ? Current : InstrumentKind.Piano;
        /// <summary>A displayed instrument's pick point (the top centre of its displayed boxes, 5 cm down), for the ray summon
        /// and Sunny's gaze tips. False when it is not on display.</summary>
        bool TryGetDisplayPoint(InstrumentKind instrument, out Vector3 world) { world = default; return false; }
        /// <summary>MR: the lead was placed on a real surface; the other members take their planner offsets from it
        /// (generalises PlaceAroundPiano, which stays and calls this for the piano).</summary>
        void PlaceAroundLead() => PlaceAroundPiano();
        /// <summary>MR: placing ended without a landing (cancelled, timed out, no surface): every rig of the layout that
        /// placing hid shows again, the menu's group too when the menu was open (MR calls it on every cancel path).</summary>
        void RestoreAfterPlacing() { }
        /// <summary>"Reset view": every instrument of the layout back in front of the player as they sit now (the menu's
        /// Reset view row and the two-hand pinch hold share this one path). Placement only, once: nothing follows the head
        /// afterwards.</summary>
        void BringToPlayer() => ArrangeAroundPlayer(true);
        /// <summary>The hand is busy with an instrument or the menu right now (playing in the last second, on or in an
        /// instrument's zone, a play surface or the shown menu, holding or pointing at one in Move instruments): its pinch is
        /// not for anything else (the bring-back hold, a paint throw).</summary>
        bool HandBusy(int hand) => false;

        static readonly InstrumentKind[] s_defaultSet = { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp };
    }

    /// <summary>My set (platform.md 4.2).</summary>
    public static class MySetRules
    {
        /// <summary>The product rule: up to 3 instruments plus the side table (no set of 4 fits within a 2 ft reach).</summary>
        public const int Max = 3;
        /// <summary>musicroom.set when missing: today's room.</summary>
        public const string Default = "piano,drums,harp";
    }

    public interface IGrabService
    {
        /// <summary>The rig a hand currently holds (null = none). Instruments of a held rig are not ticked.</summary>
        Rig HeldRig { get; }
        bool AnyHeld { get; }
        /// <summary>The grab target at point: menu zone first, then the nearest rig handle within reach.</summary>
        Rig GrabTarget(Vector3 point, float reach = 0.07f);
    }

    public interface IPlacementService
    {
        /// <summary>Resolve placement: clearEye, clearWalls, findSpot in priority order; returns true when all rigs are free.</summary>
        bool Resolve(bool useRoomBox);
        /// <summary>Settle one rig after a pass-through release.</summary>
        void SettleOne(Rig rig);
        /// <summary>Panel clear-out: move the rigs out of the panel's way.</summary>
        void ClearPanel(Rig panel);
        /// <summary>fitSeat: the rig shift that makes the dry-run layout fit (null when none needed).</summary>
        Vector3? RoomShift();
        float EyeDistance(Rig rig);
    }

    /// <summary>Grab / placement constants (grab dwell and guards, placement, stick pick-up).</summary>
    public static class GrabConstants
    {
        public const float PlayGuardMs = 1000f;
        public const float GrabDwell = 0.3f;
        /// <summary>Mixed reality: a still pinch in empty air starts placing the lead only after this long (an ordinary
        /// grab of an instrument's handle keeps GrabDwell). At 0.3 s a resting pinch between notes started placing, which
        /// silences every instrument until the next pinch and looked like a hang.</summary>
        public const float PlacingDwell = 1.0f;
        /// <summary>How far the smoothed pinch point may wander during the dwell. The first 0.025 m was too strict on the
        /// headset (the index tip keeps moving 1-2 cm while a pinch closes, on top of the tracker's jitter): 0.04 m since
        /// the first device test (ARRANGE-NOTES.md). A stroke still fails it by far (0.8 m/s for 0.3 s is 0.24 m).</summary>
        public const float GrabStill = 0.04f;
        /// <summary>The value (0.025 m) before the device test, for reference.</summary>
        public const float GrabStillBeforeDeviceTest = 0.025f;
        public const float GrabSpeed = 0.5f;
        public const float GrabSmooth = 0.06f;
        public const float SpeedWindow = 0.1f;
        public const float HintAfter = 0.05f;
        public const int DwellSamples = 48;
        public const float HandleReach = 0.07f;
        public const float ZonePad = 0.12f;
        public const float UnstickMs = 300f;
        public const float LiftMin = -0.45f, LiftMax = 0.6f, LiftDeadband = 0.03f;
        public const float OffHandTurnPull = 0.12f, OffHandTurnDeg = 45f;
        public const float EyeClear = 0.30f, EyeMargin = 0.02f, Gap = 0.01f, WallSlop = 0.005f, Step = 0.02f, MaxPush = 1.6f;
        public const float SeatHalf = 0.2f, SeatFront = 0.1f, SeatBack = 0.3f;
        public const float PanelMinEye = 0.3f, PanelMaxRise = 0.45f;
        public const float SwivelPull = 0.14f, SwivelDeg = 45f;
        /// <summary>Reset view by hand: both hands pinched in empty air and held still this long bring everything back (the
        /// ring round the hands fills meanwhile; it shows after BringBackShowAfter so a passing two-hand pinch never flashes).</summary>
        public const float TwoHandSummonSeconds = 1.0f, BringBackShowAfter = 0.15f;
        /// <summary>How far either pinch may wander from where the hold began (else it starts again): held still.</summary>
        public const float BringBackStill = 0.06f;
        public const float TurnSeconds = 0.35f, TurnSlide = 0.9f, TurnScale = 0.85f;
        /// <summary>VR room box in the world (RoomFrame.WorldBox: the room turned to face the fire, x +-3.2, z in [-1.62, 3.58]);
        /// rig origins are clamped to the room-frame rectangle x +-2.8, z in [-3.6, 0.8], turned into the world (z in [-1.3, 3.1]).</summary>
        public static readonly Bounds RoomBox = RoomFrame.WorldBox;
        public static readonly Rect OriginClamp = RoomFrame.ToWorld(Rect.MinMaxRect(-2.8f, -3.6f, 2.8f, 0.8f));
    }
}
