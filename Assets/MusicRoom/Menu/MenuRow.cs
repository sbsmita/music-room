// One pokeable row of the panel. The 24 slots are reused across screens and layouts;
// FiredAt survives a relayout on purpose, so the 250 ms debounce also covers a screen change under the finger.
using MusicRoom.Core;

namespace MusicRoom.Menu
{
    /// <summary>The original row ids (menu, guide, back, inst, course, layout, lock, take, play, tstop, delete, stop, replay,
    /// courses) plus the home rows room ("Play in my room" / "Music room") and setup ("Set up my room"), and since the
    /// first headset test arrange ("Move
    /// instruments"), exit, the start panel's playHere / playRoom and the Done / Reset buttons of arrange mode (done, reset).
    /// The many-instrument platform (platform.md 5) adds Tile (an Instruments grid tile; the Lessons grid's tiles stay Inst),
    /// Option (a row of an instrument's options page) and Options (the "Options" chip on an at-seat tile).
    /// First five: Next (the lesson after this one), and the Sunny screen's Tour, Tip (one row of the Tips list), TipsOn
    /// ("Tips while I play") and Layers ("Make a long song": Sunny's long song on the tape deck, record on top; until
    /// 2026-11-18 "Make a song in layers"). Trash: the bin beside each
    /// take of the Recordings list (poke once: "Delete?", again within 3 s: the take is deleted).</summary>
    public enum RowKind : byte { None, Back, Menu, Guide, Room, Setup, Inst, Course, Layout, Take, Play, TStop, Delete, Stop, Replay, Courses,
        Arrange, Exit, PlayHere, PlayRoom, Done, Reset, Tile, Option, Options, Next, Tour, Tip, TipsOn, Layers, Hands, ResetView, Trash,
        /// <summary>A recording's page: "Keep painting" On / Off (the notes' painting on the walls stays until the next playback).</summary>
        Paint,
        /// <summary>The Sunny page: "Look and pinch" On / Off (ILookAndPinch, the Gaze module).</summary>
        LookPinch,
        /// <summary>The Sunny page: "Loop a beat" (Sunny's one-minute loop show on the tape deck).</summary>
        LoopShow }

    /// <summary>Icon kinds. Guide, Room and Setup draw nothing (the 'guide' icon is blank)
    /// but still take the icon's space, so every home label lines up. Sound, Move, Exit, House, Check and Undo came later.</summary>
    public enum IconKind : byte { None, Piano, Drums, Harp, Recordings, Instruments, Lessons, Play, Stop, Delete, Back, Lock, Unlock, Guide, Room, Setup,
        Move, Exit, House, Check, Undo }

    public sealed class MenuRow
    {
        public RowKind Id;
        public float X0, Y0, X1, Y1;
        public InstrumentKind Inst;
        public bool HasInst;
        public Lesson Lesson;
        public TakeInfo Take;
        /// <summary>A take's row: its constellation picture (SongStars.BuildThumb, cached per take by the controller; null
        /// while it loads).</summary>
        public SongStars.Thumb Stars;
        /// <summary>First five: the tip's index on the Tips list (-1 elsewhere).</summary>
        public int Index = -1;
        public MenuScreen To;
        public LayoutMode Mode;
        public string Label = "", Note = "";
        public IconKind Icon;
        public bool Scrolls, On;
        public double FiredAt = double.NegativeInfinity;
        /// <summary>The stable id MenuRowEvent carries (sunny-tour.md 7.2); instrument rows use cached "inst.&lt;id&gt;" strings.</summary>
        public string RowId = "";
        /// <summary>Tiles: the instrument's descriptor (name, portrait fallback strokes), its portrait atlas cell (-1: none),
        /// a member of My set (the printed tick box). Option rows: the option's index and its small print.</summary>
        public InstrumentDescriptor Desc;
        public int Cell = -1, Option = -1;
        public bool Ticked;
        public string Small;
        /// <summary>The IGuideAnchors id this row answers while it is shown ("menu.tile.&lt;id&gt;", "menu.option.&lt;id&gt;.&lt;Key&gt;").</summary>
        public string Anchor;

        public float Height => Y1 - Y0;
        public float CentreY => (Y0 + Y1) * 0.5f;

        /// <summary>row(i, id, x0, y0, x1, y1): everything but FiredAt is reset.</summary>
        internal MenuRow Set(RowKind id, float x0, float y0, float x1, float y1)
        {
            Id = id; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
            Inst = default; HasInst = false; Lesson = null; Take = null; Stars = null; To = MenuScreen.Home; Mode = LayoutMode.All;
            Label = ""; Note = ""; Icon = IconKind.None; Scrolls = false; On = false; Index = -1;
            RowId = MenuRowIds.Of(id); Desc = null; Cell = -1; Option = -1; Ticked = false; Small = null; Anchor = null;
            return this;
        }

        public bool Contains(float px, float py) => px >= X0 && px <= X1 && py >= Y0 && py <= Y1;

        public override string ToString() => $"{Id} '{Label}' [{X0}, {Y0}, {X1}, {Y1}]";
    }

    /// <summary>The sunny-tour row ids of the rows whose id does not depend on what they show (Menu rows take their
    /// screen's id, instrument rows a cached "inst.&lt;id&gt;" from MenuText). First five: the ids Sunny points at, lights and
    /// listens to (IMenuGuideHooks, MenuRowEvent) and the feature each row kind teaches (FeatureOf; SunnyCoverageTests:
    /// every row kind must be covered by the tour or a tip). The Guide cannot see RowKind (it lives in this assembly), hence
    /// the strings.</summary>
    public static class MenuRowIds
    {
        public const string Lessons = "lessons", Instruments = "instruments", Recordings = "recordings";
        public const string LayoutAll = "layout.all", LayoutSingle = "layout.single";
        public const string Arrange = "arrange", Room = "room", Setup = "setup", Guide = "guide", Exit = "exit", PlayHere = "playHere",
            PlayRoom = "playRoom", Done = "done", Reset = "reset", Back = "back", Course = "course",
            Take = "take", Play = "play", TStop = "tstop", Delete = "delete", Stop = "stop", Replay = "replay", Courses = "courses",
            Next = "next", Tour = "tour", Tips = "tips", Tip = "tip", TipsOn = "tipsOn", Layers = "layers", Hands = "hands",
            ResetView = "resetView", Paint = "paint", LookPinch = "lookPinch", LoopShow = "loopShow";

        /// <summary>The id a shown row answers to (a Menu row: the screen it opens; any other: its RowId).</summary>
        public static string IdOf(MenuRow r) => r == null ? null : r.Id == RowKind.Menu ? OfScreen(r.To) : r.RowId;

        public static string Of(RowKind id)
        {
            switch (id)
            {
                case RowKind.Back: return "back";
                case RowKind.Guide: return "guide";
                case RowKind.Room: return "room";
                case RowKind.Setup: return "setup";
                case RowKind.Inst: case RowKind.Tile: return "inst";
                case RowKind.Course: return "course";
                case RowKind.Layout: return "layout";
                case RowKind.Take: return "take";
                case RowKind.Play: return "play";
                case RowKind.TStop: return "tstop";
                case RowKind.Delete: return "delete";
                case RowKind.Stop: return "stop";
                case RowKind.Replay: return "replay";
                case RowKind.Courses: return "courses";
                case RowKind.Arrange: return "arrange";
                case RowKind.Exit: return "exit";
                case RowKind.PlayHere: return "playHere";
                case RowKind.PlayRoom: return "playRoom";
                case RowKind.Done: return "done";
                case RowKind.Reset: return "reset";
                case RowKind.Option: return "option";
                case RowKind.Options: return "options";
                case RowKind.Next: return Next;
                case RowKind.Tour: return Tour;
                case RowKind.Tip: return Tip;
                case RowKind.TipsOn: return TipsOn;
                case RowKind.Layers: return Layers;
                case RowKind.Hands: return Hands;
                case RowKind.Trash: return Delete;   // the list's bin asks like the Take screen's Delete (tip_delete)
                case RowKind.ResetView: return ResetView;
                case RowKind.Paint: return Paint;
                case RowKind.LookPinch: return LookPinch;
                case RowKind.LoopShow: return LoopShow;
                default: return "";
            }
        }

        /// <summary>A Menu row's id: the screen it opens.</summary>
        public static string OfScreen(MenuScreen s) =>
            s == MenuScreen.Lessons ? Lessons : s == MenuScreen.Instruments ? Instruments : s == MenuScreen.Recordings ? Recordings
            : s == MenuScreen.Guide ? Guide : s == MenuScreen.Tips ? Tips : "";

        /// <summary>The feature a row kind teaches (the Guide's TourFeatures ids, or the platform's; None has none).</summary>
        public static string FeatureOf(RowKind k)
        {
            switch (k)
            {
                case RowKind.Back: return "menu.back";
                case RowKind.Menu: return "menu.screens";
                case RowKind.Guide: return "sunny.screen";
                case RowKind.Room: return "menu.room";
                case RowKind.Setup: return "menu.setup";
                case RowKind.Inst: return "lessons.instrument";
                case RowKind.Course: return "lessons.course";
                case RowKind.Layout: return "layout.single";
                case RowKind.Take: return "recordings.take";
                case RowKind.Play: return "recordings.play";
                case RowKind.TStop: return "recordings.play";
                case RowKind.Delete: return "recordings.delete";
                case RowKind.Stop: return "lessons.stop";
                case RowKind.Replay: return "lessons.after";
                case RowKind.Courses: return "lessons.after";
                case RowKind.Arrange: return "arrange.mode";
                case RowKind.Exit: return "app.exit";
                case RowKind.PlayHere: return "start.here";
                case RowKind.PlayRoom: return "start.room";
                case RowKind.Done: return "arrange.done";
                case RowKind.Reset: return "arrange.done";
                case RowKind.Tile: return "platform.bring.menu";
                case RowKind.Option: return "instruments.options";
                case RowKind.Options: return "instruments.options";
                case RowKind.Next: return "lessons.after";
                case RowKind.Tour: return "sunny.tour";
                case RowKind.Tip: return "sunny.tips";
                case RowKind.TipsOn: return "sunny.tips";
                case RowKind.Layers: return "looper.guide";
                case RowKind.Hands: return "mr.hands";
                case RowKind.ResetView: return "menu.resetview";
                case RowKind.Trash: return "recordings.delete";
                case RowKind.Paint: return "recordings.paint";
                case RowKind.LookPinch: return "gaze.pinch";
                case RowKind.LoopShow: return "looper.guide";
                default: return null;
            }
        }
    }
}
