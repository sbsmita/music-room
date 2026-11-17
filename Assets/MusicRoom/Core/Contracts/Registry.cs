// The instrument registry (Docs/12-new-instruments/platform.md sections 2-3). Each instrument module ships ONE
// parameterless IInstrumentDefinition whose Describe() returns plain data: identity, seat, display spot, tools, audio
// role, event types, lessons, tips, portrait and budgets. Core builds the registry by reflection before any module's
// Init (App.Registry, also app.Get<IInstrumentRegistry>()). Layout, Menu, Lessons, Guide, MR, Audio and Room read it and
// never name a specific instrument (RegistryLintTests). A descriptor holds no Unity objects, so the registry builds in
// EditMode without a scene.
//
// Coordinates: Unity's (ARCHITECTURE 5). The seat frame has the head at (0, 1.2, 0) facing +z, +x right, yaw in degrees
// clockwise from above (+90 = to the right). Rig-local boxes and points are in the rig's own frame (Rig.Group), heights
// above the floor (the rig's own y applied).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>The seven seat slots, by heading about the head (platform.md 4.2): 0, +45, +90, -45, -90, +120, -120 deg.</summary>
    public enum SeatSlot : byte { Front = 0, FrontRight, Right, FrontLeft, Left, RightRear, LeftRear }

    public static class SeatSlots
    {
        public const int Count = 7;
        static readonly float[] s_heading = { 0f, 45f, 90f, -45f, -90f, 120f, -120f };
        /// <summary>The slot's heading in degrees (Unity yaw about the head).</summary>
        public static float Heading(SeatSlot s) => s_heading[(int)s];
        /// <summary>The rear slots (|heading| &gt; 100 deg) cost +2 in the planner (+100 in mixed reality, no swivel).</summary>
        public static bool IsRear(SeatSlot s) => s == SeatSlot.RightRear || s == SeatSlot.LeftRear;
    }

    [Flags]
    public enum InstrumentTraits : uint
    {
        None = 0,
        Playable = 1,
        /// <summary>Plays without touch (none today): only from unclaimed hands, only while Facing (platform.md 4.6).</summary>
        Contactless = 2,
        /// <summary>Records Control events through the ControlEncoder (bowl rubbing).</summary>
        Continuous = 4,
        UsesTools = 8,
        /// <summary>Fast Motion Mode is wanted while it is played live (E4): a held tool of this instrument, its live Onsets
        /// in the last 4 s, or the single layout on it.</summary>
        FastMotion = 16,
        Pitched = 32,
        HasStand = 64,
        /// <summary>May stand on a real table in mixed reality (IInstrumentSurface).</summary>
        TableTop = 128,
    }

    /// <summary>What a tool pointer is (Pointer.Tool). A tool with two ends that play differently (a striker that also rubs)
    /// registers one pointer per end.</summary>
    public enum ToolKind : byte { None = 0, Stick = 1, Mallet = 2, Striker = 3, RubStick = 4 }

    /// <summary>The tool pointers an instrument receives as well as the fingertips (InstrumentDescriptor.Tools).</summary>
    [Flags]
    public enum ToolMask : byte { None = 0, Stick = 1 << 1, Mallet = 1 << 2, Striker = 1 << 3, RubStick = 1 << 4 }

    public static class ToolMasks
    {
        public static ToolMask Of(ToolKind k) => k == ToolKind.None ? ToolMask.None : (ToolMask)(1 << (int)k);
        public static bool Has(ToolMask m, ToolKind k) => k != ToolKind.None && (m & Of(k)) != 0;
    }

    /// <summary>What a TakeEvent type means to the recorder, the looper, lessons and Sunny (platform.md 6.1).</summary>
    public enum EventRole : byte { Onset = 0, Release, Control, State, Marker }

    /// <summary>What pairs a Release (or a State) with its Onset: the TakeEvent field that names the note / drum / lane.</summary>
    public enum EventKey : byte { None = 0, Midi, Index, Kind, Lane }

    public enum StowMount : byte { Floor = 0, Wall, Surface }

    public enum LessonBlockShape : byte { Box = 0, Disc, Capsule, Ring, Bar }

    /// <summary>For the sheet-music module (sheet.md): how a lesson of this instrument is printed.</summary>
    public enum NotationKind : byte { None = 0, Treble, Grand, Percussion, PitchLine }

    /// <summary>Why an instrument is being brought to the seat (ILayoutService.Bring).</summary>
    public enum BringReason : byte { Menu = 0, PageTurn, Ray, Lesson, Take, Sunny }

    /// <summary>One TakeEvent type an instrument records. Sampled (State types only): the looper samples its latest value
    /// at each layer start and plays it at offset 0 (the hi-hat rule, generalised). Held (Onset types only): the note sounds
    /// until its Release (a pressed key): at a recording window's end the looper gives every open Held onset its Release
    /// (keyed by Key). A struck note (a marimba bar, a handpan field, a drum) is not Held: it rings on and is never damped at
    /// a loop boundary.</summary>
    public readonly struct EventTypeSpec
    {
        public readonly string Type;
        public readonly EventRole Role;
        public readonly EventKey Key;
        public readonly bool Sampled, Held;
        public EventTypeSpec(string type, EventRole role, EventKey key = EventKey.None, bool sampled = false, bool held = false)
        { Type = type; Role = role; Key = key; Sampled = sampled; Held = held; }
        public override string ToString() => $"{Type} ({Role}, {Key}{(Sampled ? ", sampled" : "")}{(Held ? ", held" : "")})";
    }

    /// <summary>A footprint box, rig-local: centre (Cx, Cz) and half sizes (Hx, Hz) on the floor plane, heights Y0..Y1 above
    /// the floor. Air: play space (a contactless instrument's hand field) that keeps other instruments out but not the player.</summary>
    [Serializable]
    public struct FootBox
    {
        public float Cx, Cz, Hx, Hz, Y0, Y1;
        public bool Air;
        public FootBox(float cx, float cz, float hx, float hz, float y0, float y1, bool air = false)
        { Cx = cx; Cz = cz; Hx = hx; Hz = hz; Y0 = y0; Y1 = y1; Air = air; }
        public override string ToString() => $"({Cx:F3}, {Cz:F3}) +-({Hx:F3}, {Hz:F3}) y {Y0:F2}..{Y1:F2}{(Air ? " air" : "")}";
    }

    /// <summary>Where an instrument sits at the seat (platform.md 4.2). Home / Single are seat-frame poses (x, y, z; yaw deg)
    /// of the rig origin: Home at the canonical slot in My set, Single in "One alone" (Rig.HomeAll / HomeSingle must equal
    /// them: InstrumentConformanceTests).</summary>
    public sealed class SeatSpec
    {
        public SeatSlot CanonicalSlot;
        /// <summary>Slots in preference order (the planner's search order and cost rank).</summary>
        public SeatSlot[] Prefs = Array.Empty<SeatSlot>();
        /// <summary>Planning order: lower first (big and hard-to-place first).</summary>
        public int Priority;
        public Vector3 Home; public float HomeYaw;
        public Vector3 Single; public float SingleYaw;
        /// <summary>Horizontal reach from the head to the play centre (m).</summary>
        public float ReachMax;
        /// <summary>Rig-local xz of the play centre.</summary>
        public Vector2 PlayCentre;
        /// <summary>The footprint (the union of Rig.Handles bounds must be covered within 3 cm). Heights are above the floor
        /// with the rig at Home (its rig-local y is Y - Home.y: the piano's origin is its key tops at 0.72 m).</summary>
        public FootBox[] Boxes = Array.Empty<FootBox>();
        /// <summary>Arrange-mode height range about the home (m). Both 0: it stays on the floor.</summary>
        public float LiftMin, LiftMax;
        /// <summary>Raised or lowered, its own stand stretches to the floor (the module does it); false: Layout stands a
        /// raised instrument on a riser (Layout/RiserVisuals).</summary>
        public bool StandStretches;
        /// <summary>Optional seat-frame home of the palm menu while this instrument is Facing (or Current in One alone): the
        /// menu and the lesson screen open there instead of their default (none today). Null: default.</summary>
        public Vector3? PanelHome;
        /// <summary>Optional (none today): the key of the instrument's own option row (InstrumentOption.Key) that
        /// builds it mirrored about its rig's x (a left-handed build). While the stored choice
        /// "musicroom.&lt;id&gt;.&lt;MirrorKey&gt;" is 1, the planner, the layout's at-seat boxes and conformance check 3 use
        /// BoxesFor(true) / PlayCentreFor(true): every box with Cx -&gt; -Cx, the play centre with x -&gt; -x (Home, Single and
        /// the heights unchanged). Null: never mirrored.</summary>
        public string MirrorKey;

        FootBox[] _mirrored, _mirroredOf;

        /// <summary>The footprint, mirrored about the rig's x when <paramref name="mirrored"/> (cached; Boxes when not).</summary>
        public FootBox[] BoxesFor(bool mirrored)
        {
            if (!mirrored || Boxes == null) return Boxes;
            if (_mirrored == null || !ReferenceEquals(_mirroredOf, Boxes))
            {
                var m = new FootBox[Boxes.Length];
                for (int i = 0; i < m.Length; i++) { FootBox b = Boxes[i]; m[i] = new FootBox(-b.Cx, b.Cz, b.Hx, b.Hz, b.Y0, b.Y1, b.Air); }
                _mirrored = m; _mirroredOf = Boxes;
            }
            return _mirrored;
        }

        /// <summary>The play centre, mirrored about the rig's x when <paramref name="mirrored"/>.</summary>
        public Vector2 PlayCentreFor(bool mirrored) => mirrored ? new Vector2(-PlayCentre.x, PlayCentre.y) : PlayCentre;
    }

    /// <summary>Where an instrument is on display in the VR room while it is not at the seat (platform.md 4.1). Pose: the rig
    /// origin in room coordinates (x, y = its height above the floor, z) and Yaw (deg). Boxes: the displayed configuration's
    /// footprint, heights above the floor with the rig at Pose (null = the seat boxes without the air boxes, moved with the
    /// rig). Where: the menu tile's status line ("By the window").</summary>
    public sealed class StowSpec
    {
        public string SpotId;
        public StowMount Mount;
        public Vector3 Pose; public float Yaw;
        public FootBox[] Boxes;
        public string Where;
    }

    /// <summary>One vector stroke of a fallback icon in a 100-unit box (MenuIcons style: y down, 0..100).</summary>
    public readonly struct IconStroke
    {
        public readonly Vector2[] Points;
        public readonly bool Closed, Fill;
        public IconStroke(Vector2[] points, bool closed = false, bool fill = false) { Points = points; Closed = closed; Fill = fill; }
    }

    /// <summary>The menu tile's portrait (platform.md 5.2): a camera at From looking at At (rig-local), vertical Fov (deg);
    /// Fallback: the vector icon when the portrait cannot be rendered (no GPU, a failed request).</summary>
    public sealed class PortraitSpec
    {
        public Vector3 From, At;
        public float Fov = 30f;
        public IconStroke[] Fallback = Array.Empty<IconStroke>();
    }

    /// <summary>One audio source of an instrument: its SourceId value (MusicRoom.Audio.SourceId as int), its rig-local
    /// position, and optional room-send overrides (NaN = AudioSourceTable's value).</summary>
    public readonly struct AudioSourceSpec
    {
        public readonly int Source;
        public readonly Vector3 Local;
        public readonly float SendDb, ErDb;
        public AudioSourceSpec(int source, Vector3 local, float sendDb = float.NaN, float erDb = float.NaN)
        { Source = source; Local = local; SendDb = sendDb; ErDb = erDb; }
    }

    /// <summary>The instrument's sound unit and the budgets it promises (platform.md 10.3).</summary>
    public sealed class AudioSpec
    {
        /// <summary>MusicRoom.Audio.Burst.UnitId value.</summary>
        public byte Unit;
        public float CapAvgMs, CapWorstMs, ArenaMiB, BakedMiB;
        public AudioSourceSpec[] Sources = Array.Empty<AudioSourceSpec>();
    }

    /// <summary>The instrument's lessons (platform.md 7). Pack: the file Resources/LessonPacks/&lt;Pack&gt;.json (classic three:
    /// null, they live in lessons.json). Travel (m) and LeadSeconds: how far and how long a block falls. CountNoun: the
    /// summary's "12 notes" / "16 strokes".</summary>
    public sealed class LessonSpec
    {
        public float Travel = 0.3f, LeadSeconds = 1.667f;
        public LessonBlockShape Shape;
        public NotationKind Notation;
        public string Pack;
        public string CountNoun = "notes";
    }

    /// <summary>Mixed reality: may it stand on a real table (TableTop), lifted by SurfaceLift with its stand hidden.</summary>
    public sealed class MrSpec { public bool OnTable; public float SurfaceLift; }

    /// <summary>The soft floor shadow under the rig (one combined mesh for every at-seat instrument): rig-local centre, width,
    /// depth, opacity.</summary>
    public sealed class ShadowSpec { public Vector2 Local; public float W, D, Opacity = 0.45f; public bool HiddenInMr; }

    /// <summary>A feature id ("&lt;id&gt;.&lt;feature&gt;") and, for a Passive feature that needs no tip, the reason.</summary>
    public readonly struct FeatureSpec
    {
        public readonly string Id;
        public readonly string PassiveReason;
        public bool Passive => !string.IsNullOrEmpty(PassiveReason);
        public FeatureSpec(string id, string passiveReason = null) { Id = id; PassiveReason = passiveReason; }
        public static implicit operator FeatureSpec(string id) => new FeatureSpec(id);
        public override string ToString() => Passive ? Id + " (passive: " + PassiveReason + ")" : Id;
    }

    /// <summary>An instrument-owned setting shown as a menu row on the instrument's options page (Menu > Instruments, poke the
    /// tile of the instrument you play alone, or its "Options" chip). Stored as int AppSettings key
    /// "musicroom.&lt;id&gt;.&lt;Key&gt;" (the choice index); a change publishes InstrumentOptionEvent.</summary>
    public sealed class InstrumentOption
    {
        public string Key;
        public string Label;
        public string[] Choices = Array.Empty<string>();
        public int Default;
        /// <summary>Small print under the row (&lt;= 40 characters), or null.</summary>
        public string Note;
        /// <summary>A Sunny tip id said the first time the row is changed, or null.</summary>
        public string Tip;
    }

    public sealed class InstrumentDescriptor
    {
        public InstrumentKind Kind;
        public RigId Rig;
        /// <summary>The persisted id ("handpan"), the prefix of everything it adds to a shared space.</summary>
        public string Id;
        /// <summary>"Handpan"; AloneName: the menu's home row in One alone ("Handpan alone"); Family: "Percussion".</summary>
        public string Name, AloneName, Family;
        /// <summary>Position in the carousel (page turn, the menu grid); ModuleOrder / TickOrder as platform.md section 2.</summary>
        public int Carousel, ModuleOrder, TickOrder;
        public InstrumentTraits Traits;
        public ToolMask Tools;
        public SeatSpec Seat;
        public StowSpec Stow;
        public PortraitSpec Portrait;
        public AudioSpec Audio;
        public LessonSpec Lessons;
        public MrSpec Mr;
        public ShadowSpec Shadow;
        public EventTypeSpec[] Events = Array.Empty<EventTypeSpec>();
        public TipSpec[] Tips = Array.Empty<TipSpec>();
        public FeatureSpec[] Features = Array.Empty<FeatureSpec>();
        /// <summary>The anchor ids its tips and lessons name (IGuideAnchors must resolve each: conformance 13).</summary>
        public string[] Anchors = Array.Empty<string>();
        public InstrumentOption[] Options = Array.Empty<InstrumentOption>();
        /// <summary>Budgets the instrument promises (platform.md 11; conformance 4 checks them on the GPU).</summary>
        public int DrawsAtSeat, DrawsStowed, TrisAtSeat, TrisStowed;
        /// <summary>Rig-local music-rest pose for the sheet module (the piano's; a future stand's); HasSheetAnchor false = none.</summary>
        public Pose SheetAnchor = Pose.identity;
        public bool HasSheetAnchor;
        /// <summary>A phase-0 stub (PlaceholderInstrument): one lesson, text-only tips, loudness parity exempt. False for
        /// every real instrument.</summary>
        public bool Placeholder;

        public bool Playable => (Traits & InstrumentTraits.Playable) != 0;
        public bool Has(InstrumentTraits t) => (Traits & t) == t;

        /// <summary>The store key of the option that mirrors the instrument (SeatSpec.MirrorKey), or null.</summary>
        public string MirrorStoreKey
        {
            get
            {
                if (Seat == null || string.IsNullOrEmpty(Seat.MirrorKey) || string.IsNullOrEmpty(Id)) return null;
                if (_mirrorKeyOf != Seat.MirrorKey) { _mirrorStoreKey = "musicroom." + Id + "." + Seat.MirrorKey; _mirrorKeyOf = Seat.MirrorKey; }
                return _mirrorStoreKey;
            }
        }
        string _mirrorStoreKey, _mirrorKeyOf;

        /// <summary>True while the instrument is built mirrored (SeatSpec.MirrorKey's stored choice is 1).</summary>
        public bool IsMirrored(ISettingsStore store)
        {
            string key = MirrorStoreKey;
            return key != null && store != null && store.GetInt(key, 0) == 1;
        }
        public override string ToString() => $"{Id} ({Kind})";
    }

    /// <summary>One per instrument module, parameterless (Core discovers it by reflection, never under MusicRoom.Tests.*).</summary>
    public interface IInstrumentDefinition
    {
        InstrumentDescriptor Describe();
    }

    public interface IInstrumentRegistry
    {
        /// <summary>The playable instruments, in carousel order (including ones whose module did not load: check IsLoaded).</summary>
        IReadOnlyList<InstrumentDescriptor> All { get; }
        /// <summary>The playable kinds in carousel order.</summary>
        IReadOnlyList<InstrumentKind> Carousel { get; }
        /// <summary>Null for an unknown kind (Table and Menu have built-in descriptors here too).</summary>
        InstrumentDescriptor Get(InstrumentKind kind);
        /// <summary>Persisted ids, lesson "instrument" fields ("piano" ... "bowls"). False for unknown ids.</summary>
        bool TryParse(string id, out InstrumentKind kind);
        string IdOf(InstrumentKind kind);
        /// <summary>Its module registered a Rig this boot (and it passed BootOptions.InstrumentFilter).</summary>
        bool IsLoaded(InstrumentKind kind);
        /// <summary>Which instrument records this TakeEvent type (InstrumentKind.Table when unknown).</summary>
        InstrumentKind OwnerOfEvent(string takeEventType);
        /// <summary>The type's role; unknown types are Onset (never quantised away, never dropped).</summary>
        EventRole RoleOf(string takeEventType);
        /// <summary>The type's spec (default when unknown: check Type != null).</summary>
        EventTypeSpec EventSpec(string takeEventType);
        /// <summary>Carousel step, skipping instruments that are not loaded (returns from when nothing else is loaded).</summary>
        InstrumentKind Next(InstrumentKind from, int direction);
    }

    /// <summary>Lanes of continuous control (platform.md 6.4, widened for six looper voices). Live play uses
    /// lane 0; looper layer k (0-based) plays back on Layer(k) = 1 + k (1..6); a take from the deck or the library plays back
    /// on lane Take (7). A unit with fewer lanes folds them with Fold(lane, lanes): 0 stays 0, others share 1..lanes-1.</summary>
    public static class PlaybackLanes
    {
        public const int Live = 0, Take = 7, Count = 8, MaxLayers = 6;
        public static int Layer(int k) => 1 + ((k % MaxLayers) + MaxLayers) % MaxLayers;
        public static bool IsPlayback(int lane) => lane != Live;
        public static int Fold(int lane, int lanes) => lane <= 0 || lanes <= 1 ? 0 : 1 + (lane - 1) % (lanes - 1);
    }
}
