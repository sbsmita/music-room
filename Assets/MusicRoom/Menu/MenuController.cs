// The panel's logic (spec 08 sections 1.4-1.6): screens and navigation, the rows a
// fingertip can poke (laid out again only when something they show changes), the poke state machine with debounce,
// slop and the curved-surface test, list scrolling, the hover-fade timing, and every row action. It holds no Unity
// objects: the module feeds it panel-local fingertip positions and owns the visuals, so EditMode tests drive it with
// fake services. Services are plain fields (the module resolves them lazily: Layout and MR initialise after the menu).
// The many-instrument platform (12-new-instruments/platform.md 5): the Instruments screen is a grid of portrait tiles
// read from the instrument registry (carousel order, unloaded instruments closed up) with My set / One alone and Move
// under it; the Lessons screen is the same grid; an instrument's own options (InstrumentDescriptor.Options)
// are a sub-page of the Instruments screen. The menu names no instrument: everything comes from the registry.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    /// <summary>What the controller needs from its owner (the module in the app, a fake in tests).</summary>
    public interface IMenuHost
    {
        /// <summary>Hide the panel (the "Show me around" row puts it away before Sunny's tour opens it again).</summary>
        void Hide();
        /// <summary>The screen changed: publish MenuEvent{Screen}.</summary>
        void ScreenChanged(MenuScreen screen);
        /// <summary>Exit was confirmed: fade the sound out, save and close the app (the module routes it through App.Quit).</summary>
        void RequestQuit() { }
        /// <summary>The start panel was answered (<paramref name="mixedReality"/>: "Play in my room").</summary>
        void StartChosen(bool mixedReality) { }
        /// <summary>A row was pressed (sunny-tour.md 7.2): the module publishes MenuRowEvent.</summary>
        void RowPressed(in MenuRowEvent e) { }
        /// <summary>A menu signal for Sunny's data tips (platform.md 8.1): the module publishes InstrumentSignalEvent{Kind =
        /// Table, Signal}: "menu.instruments" when the Instruments screen opens, "menu.instruments.set" too when it opens
        /// in My set with two or more members.</summary>
        void Signal(string signal) { }
        /// <summary>An instrument option changed (its new choice index, already stored): the module publishes
        /// InstrumentOptionEvent.</summary>
        void OptionChanged(InstrumentKind kind, string key, int value) { }
        /// <summary>A lesson screen was just shown for this instrument's lesson (the module moves the shown panel to the
        /// instrument's panel home, SeatSpec.PanelHome).</summary>
        void LessonShown(InstrumentKind kind) { }
    }

    public sealed class MenuController
    {
        public const int MaxRows = 24;

        // ---- services (null-safe everywhere) ----
        public ILayoutService Layout;
        public ITutorial Tutorial;
        public ILessonLibrary Lessons;
        public IRecorder Recorder;
        public ITakeLibrary Takes;
        public IGuide Guide;
        public IMixedReality Mr;
        /// <summary>The hands' look in "Play in my room": the home page's "Real hands" row (shown in mixed reality).</summary>
        public IHandLook Hands;
        /// <summary>The audio engine (the scroll ticks' click).</summary>
        public IAudioEngine Sound;
        /// <summary>"Move instruments" (Layout): the setting's home row and the Instruments screen's Move button. Without it
        /// neither is shown.</summary>
        public IArrangeMode Arrange;
        /// <summary>The instrument registry (App.Registry): the grids, names, places, lesson ids and options.</summary>
        public IInstrumentRegistry Registry;
        /// <summary>Where instrument options are stored ("musicroom.&lt;id&gt;.&lt;Key&gt;", ints). Null: kept in memory.</summary>
        public AppSettings Settings;
        /// <summary>The portrait atlas the tiles sample (null, or a cell not Ready: the descriptor's vector icon).</summary>
        public PortraitStudio Portraits;

        /// <summary>The signals the menu gives Sunny (Guide/PlatformTips.cs triggers on them).</summary>
        public const string SignalInstruments = "menu.instruments", SignalInstrumentsSet = "menu.instruments.set";

        /// <summary>First five: finished lessons (a "Done" note on their course row) and the Done screen's Next row.</summary>
        public ILessonProgress Progress;
        /// <summary>First five: the Sunny screen and its Tips list (Guide). Without it the home row is "Show me around".</summary>
        public IGuideTour Tour;
        /// <summary>Look and pinch (the Gaze module; null without it: no row).</summary>
        public ILookAndPinch Look;
        /// <summary>First five: the row Sunny lights with the hover highlight while no fingertip hovers (-1: none).</summary>
        public int Spotlight = -1;

        readonly IMenuHost _host;
        readonly MenuRow[] _rows = new MenuRow[MaxRows];
        readonly List<Lesson> _courses = new List<Lesson>(16);
        public readonly MenuText Text = new MenuText();

        public MenuController(IMenuHost host)
        {
            _host = host;
            for (int i = 0; i < MaxRows; i++) _rows[i] = new MenuRow();
        }

        // ---- state ----
        public bool Shown { get; private set; }
        public MenuScreen Screen { get; private set; } = MenuScreen.Home;
        /// <summary>The instrument whose courses the courses screen lists (the carousel's first until one is picked).</summary>
        public InstrumentKind Instrument { get; private set; }
        public float Scroll { get; private set; }
        public float MaxScroll { get; private set; }
        public int RowCount { get; private set; }
        public MenuRow Row(int i) => _rows[i];
        public int HoverRow { get; private set; } = -1;
        public int HoverPrev { get; private set; } = -1;
        public double HoverT { get; private set; }
        /// <summary>The canvas needs painting (set on every change of what it shows; never for hover).</summary>
        public bool Dirty = true;
        public bool Confirm { get; private set; }
        /// <summary>The Recordings list: the take whose bin was poked once and asks "Delete?" (null: none). A second poke
        /// within MenuStyle.DeleteConfirmMs deletes it; otherwise it reads as a bin again.</summary>
        public string TrashArmedId { get; private set; }
        double _trashArmedAt;
        /// <summary>The saved take this menu started playing (Play on its Take screen), while the recorder still plays it.</summary>
        public string PlayingTakeId => _playingTakeId != null && Recorder != null && Recorder.External && Recorder.State == RecorderState.Playing ? _playingTakeId : null;
        string _playingTakeId;
        public TakeInfo Take { get; private set; }
        public TakeRecord TakeRecord { get; private set; }
        /// <summary>The open recording's Play: pressed while it still loaded (it plays once loaded), its file could not be read,
        /// or the last press could not play it (the Play row says so: a press never does nothing).</summary>
        public bool PlayWhenLoaded { get; private set; }
        public bool TakeLoadFailed { get; private set; }
        public bool PlayFailed { get; private set; }
        public readonly List<TakeInfo> TakeList = new List<TakeInfo>();
        /// <summary>Times the rows were laid out (tests check that idle frames do not relayout).</summary>
        public int Relayouts { get; private set; }
        /// <summary>Clock.NowMs of the current frame (BeginPokes); the debounce clock.</summary>
        public double NowMs => _now;
        /// <summary>Exit was poked once: the row reads "Tap again to exit" until ExitConfirmMs after that poke.</summary>
        public bool ExitArmed { get; private set; }
        /// <summary>Exit was confirmed: the app is closing.</summary>
        public bool Quitting { get; private set; }
        double _exitArmedAt;

        // ---- what the painter draws (filled by LayoutRows from the services, so a repaint reads no service) ----
        public string HeaderTitle { get; private set; } = MenuStyle.HomeTitle;
        public string HeaderNote { get; private set; } = "";
        public bool HeaderBack { get; private set; }
        public string Footer { get; private set; }
        public Color FooterColor { get; private set; } = MenuStyle.Dim;
        public string BodyTitle { get; private set; }
        public string BodyLine1 { get; private set; }
        public string BodyLine2 { get; private set; }
        public TutorialState LessonState { get; private set; }
        public string Digit { get; private set; } = "";
        public string Hint { get; private set; } = "";
        public string ProgressLeft { get; private set; } = "";
        public string ProgressRight { get; private set; } = "";
        public bool ProgressMistakes { get; private set; }
        public float ProgressFrac { get; private set; }
        public string SummaryLine { get; private set; } = "";
        public string TakeLine { get; private set; } = "";

        int _uiVersion, _laidT = int.MinValue, _laidU = int.MinValue;
        int _screenStamp;
        // what the last Sync saw
        RecorderState _recState; bool _recExt;
        bool _guideHere;
        bool _mrSeen, _mrActive, _mrAvail, _realHandsSeen; MrStatus _mrStatus;
        bool _laySeen; LayoutMode _layMode; InstrumentKind _layCur; int _laySet; long _layPlaces;
        int _takesSeen = int.MinValue, _takesRequest;
        TutorialState _tutState; string _tutLesson;
        bool _arrangeSeen;
        int _tourHeard = -1; bool _tourOn;

        // ---- pokes: one state per hand's index tip, plus the desktop mouse ----
        struct PokeState { public bool Armed, Touching, Moved; public float Y0, Scroll0; public int Row, Stamp; }
        readonly PokeState[] _pokes = new PokeState[2];
        struct MouseState { public bool Touching, Moved; public float Py0, Scroll0; public int Row, Stamp; }
        MouseState _mouse;
        int _hover = -1;
        double _now;

        // ---- the grids and the options page (platform.md 5) ----
        InstrumentDescriptor _options;
        string _note;
        double _noteUntil;
        readonly Dictionary<string, int> _memOptions = new Dictionary<string, int>(8);
        /// <summary>The Instruments screen shows an instrument's options page (a sub-page: MenuScreen stays Instruments).</summary>
        public bool OnOptions => _options != null && Screen == MenuScreen.Instruments;
        /// <summary>Whose options page is shown (null: the grid).</summary>
        public InstrumentDescriptor OptionsFor => OnOptions ? _options : null;
        /// <summary>The header's short-lived note ("Harp went back to the corner", "No room: take one out first"), or null.</summary>
        public string TransientNote => _note;
        /// <summary>A tile grid (Instruments or Lessons) was shown at least once: the portraits are wanted.</summary>
        public bool GridSeen { get; private set; }

        public bool MouseTouching => _mouse.Touching;
        public bool PokeArmed(int slot) => _pokes[slot].Armed;
        public bool PokeTouching(int slot) => _pokes[slot].Touching;

        /// <summary>Mark the rows for a relayout and the canvas for a repaint.</summary>
        public void Changed()
        {
            _uiVersion++;
            Dirty = true;
        }

        /// <summary>Shown / hidden (the module poses and animates the panel). Hiding clears the hover and every poke state.</summary>
        public void SetShown(bool shown)
        {
            if (shown == Shown) return;
            Shown = shown;
            Dirty = true;
            if (shown)
            {
                if (Screen == MenuScreen.Instruments && _options == null) InstrumentsShown();
                if (Screen == MenuScreen.Lessons) GridSeen = true;
                return;
            }
            if (ExitArmed) { ExitArmed = false; _uiVersion++; }   // put away: the next Exit poke asks again
            HoverRow = -1;
            HoverPrev = -1;
            _fading = false;
            for (int i = 0; i < _pokes.Length; i++) { _pokes[i].Armed = false; _pokes[i].Touching = false; }
            _mouse.Touching = false;
        }

        // ---------------------------------------------------------------- sync and layout

        /// <summary>Sync(): notice what changed in the services, relayout (while shown) when the tutorial
        /// version or the panel's own version moved. Called every frame; allocation-free when nothing changed.</summary>
        public void Sync()
        {
            if (Tutorial != null) ObserveTutorial(Tutorial.State, Tutorial.LessonId);
            if (Recorder != null)
            {
                RecorderState rs = Recorder.State;
                bool ext = Recorder.External;
                if (rs != _recState || ext != _recExt)
                {
                    _recState = rs;
                    _recExt = ext;
                    if (Screen == MenuScreen.Take || Screen == MenuScreen.Recordings) _uiVersion++;
                }
            }
            bool here = Guide != null && Guide.Present;
            if (here != _guideHere)
            {
                _guideHere = here;
                if (Screen == MenuScreen.Home) _uiVersion++;
            }
            if (Mr != null)
            {
                bool a = Mr.Active, av = Mr.Available;
                MrStatus st = Mr.Status;
                bool rh = Hands != null && Hands.RealHandsInMr;
                if (!_mrSeen || a != _mrActive || av != _mrAvail || st != _mrStatus || rh != _realHandsSeen)
                {
                    _mrSeen = true; _mrActive = a; _mrAvail = av; _mrStatus = st; _realHandsSeen = rh;
                    if (Screen == MenuScreen.Home || Screen == MenuScreen.Instruments) _uiVersion++;   // the bottom row's Real hands in MR
                }
            }
            if (Layout != null)
            {
                LayoutMode mode = Layout.Mode;
                InstrumentKind cur = Layout.Current;
                int set = Layout.SetVersion;
                if (!_laySeen || mode != _layMode || cur != _layCur || set != _laySet)
                {
                    _laySeen = true; _layMode = mode; _layCur = cur; _laySet = set;
                    _uiVersion++;
                }
                // The grid's amber frames and Options chips follow every instrument's place (InstrumentMoveEvent also marks a
                // change; this poll catches a missed one). Eight interface reads, no allocation.
                if (Shown && Screen == MenuScreen.Instruments && _options == null)
                {
                    long places = PlacesSignature();
                    if (places != _layPlaces) { _layPlaces = places; _uiVersion++; }
                }
            }
            if (_note != null && _now >= _noteUntil)
            {
                _note = null;   // the short-lived header note is over: the set (or "Handpan alone") again
                _uiVersion++;
            }
            if (Takes != null && Takes.Version != _takesSeen)
            {
                _takesSeen = Takes.Version;
                RefreshTakes();
            }
            bool arranging = Arrange != null && Arrange.MoveOn;
            if (arranging != _arrangeSeen)
            {
                _arrangeSeen = arranging;
                if (Screen == MenuScreen.Home || Screen == MenuScreen.Instruments) _uiVersion++;
            }
            if (Tour != null && (Screen == MenuScreen.Guide || Screen == MenuScreen.Tips))
            {
                int heard = Tour.TipsHeard;
                bool on = Tour.TipsEnabled;
                if (heard != _tourHeard || on != _tourOn) { _tourHeard = heard; _tourOn = on; _uiVersion++; }
            }
            if (ExitArmed && !Quitting && _now - _exitArmedAt >= MenuStyle.ExitConfirmMs)
            {
                ExitArmed = false;   // no second poke in time: the row reads "Exit" again
                _uiVersion++;
            }
            if (TrashArmedId != null && _now - _trashArmedAt >= MenuStyle.DeleteConfirmMs)
            {
                TrashArmedId = null;   // no second poke in time: the bin is a bin again
                _uiVersion++;
            }
            if (!Shown) return;
            int tv = Tutorial != null ? Tutorial.Version : 0;
            if (tv != _laidT || _uiVersion != _laidU)
            {
                _laidT = tv;
                _laidU = _uiVersion;
                LayoutRows();
                Dirty = true;
            }
        }

        /// <summary>Tutorial.OnStart: any lesson start shows the lesson screen, however it was
        /// started. A start is the tutorial entering its countdown (or a countdown for another lesson). Fed by the
        /// module's TutorialEvent handler and by the per-frame poll, so it fires once whichever comes first.</summary>
        public void ObserveTutorial(TutorialState state, string lessonId)
        {
            bool started = state == TutorialState.Countdown && (_tutState != TutorialState.Countdown || !string.Equals(lessonId, _tutLesson));
            _tutState = state;
            _tutLesson = lessonId;
            if (started) SyncTo(lessonId);
        }

        void SyncTo(string lessonId)
        {
            Lesson l = lessonId != null && Lessons != null ? Lessons.Get(lessonId) : null;
            if (l == null && lessonId != null && Lessons != null) return;   // first five: a lesson the menu does not list (the first run's) never takes the panel
            if (l != null && TryKind(l.instrument, out InstrumentKind k)) Instrument = k;
            Go(MenuScreen.Lesson);
            _host?.LessonShown(Instrument);
        }

        // ---------------------------------------------------------------- the registry

        /// <summary>A lesson's "instrument" id as a kind (the registry's ids; without a registry, the id block's).</summary>
        bool TryKind(string id, out InstrumentKind kind) =>
            Registry != null ? Registry.TryParse(id, out kind) : InstrumentIds.TryParse(id, out kind);

        string IdOf(InstrumentKind k) => Registry?.IdOf(k) ?? InstrumentIds.Of(k) ?? "";

        /// <summary>The instrument's display name ("Singing bowls"), or its id without a registry.</summary>
        public string NameOf(InstrumentKind k) => Registry?.Get(k)?.Name ?? InstrumentIds.Of(k) ?? "";

        bool Loaded(InstrumentDescriptor d) => d != null && (Registry == null || Registry.IsLoaded(d.Kind));

        bool IsMember(InstrumentKind k)
        {
            if (Layout == null) return false;
            IReadOnlyList<InstrumentKind> set = Layout.MySet;
            for (int i = 0; set != null && i < set.Count; i++) if (set[i] == k) return true;
            return false;
        }

        /// <summary>Every loaded instrument's place, two bits each (the grid's status lines).</summary>
        long PlacesSignature()
        {
            if (Registry == null || Layout == null) return 0;
            long sig = 0;
            var all = Registry.All;
            for (int i = 0; i < all.Count && i < 30; i++)
                if (Registry.IsLoaded(all[i].Kind)) sig |= (long)((int)Layout.PlaceOf(all[i].Kind) & 3) << (2 * i);
            return sig;
        }

        /// <summary>Force a relayout now (tests, IMenuPanel.Redraw goes through Changed).</summary>
        public void Relayout()
        {
            _laidT = Tutorial != null ? Tutorial.Version : 0;
            _laidU = _uiVersion;
            LayoutRows();
            Dirty = true;
        }

        MenuRow Put(ref int n, RowKind id, float x0, float y0, float x1, float y1) => _rows[n++].Set(id, x0, y0, x1, y1);

        int BackRow(int n, string label)
        {
            _rows[n].Set(RowKind.Back, 0f, 0f, MenuStyle.CW, MenuStyle.Header).Label = label ?? "";
            HeaderTitle = Text.Prefixed(label);
            HeaderBack = true;
            return n + 1;
        }

        /// <summary>What is at the seat: My set's names in planner order ("Piano, Drums, Harp") or the instrument played alone
        /// ("Handpan alone").</summary>
        string LayoutStatus()
        {
            if (Layout == null) return "";
            if (Layout.Mode == LayoutMode.Single)
            {
                InstrumentDescriptor d = Registry?.Get(Layout.Current);
                return d != null ? d.AloneName ?? d.Name : InstrumentIds.Of(Layout.Current) ?? "";
            }
            return Text.SetNames(Layout.MySet, Registry);
        }

        /// <summary>layoutRows, with the home rows of ARCHITECTURE.md section 9.</summary>
        void LayoutRows()
        {
            Relayouts++;
            TutorialState ts = Tutorial != null ? Tutorial.State : TutorialState.Idle;
            if (Screen == MenuScreen.Lesson && ts == TutorialState.Idle)
            {
                // A lesson screen with nothing running falls back to the courses, published like every other
                // screen change (the MenuEvent contract).
                Screen = MenuScreen.Courses;
                _screenStamp++;
                _host?.ScreenChanged(Screen);
            }
            int n = 0;
            MaxScroll = 0f;
            HeaderBack = false;
            HeaderNote = "";
            Footer = null;
            FooterColor = MenuStyle.Dim;
            BodyTitle = BodyLine1 = BodyLine2 = null;
            switch (Screen)
            {
                case MenuScreen.Home: n = HomeRows(); break;
                case MenuScreen.Start: n = StartRows(); break;
                case MenuScreen.Arrange: n = ArrangeRows(); break;
                case MenuScreen.Lessons: n = LessonsRows(); break;
                case MenuScreen.Courses: n = CourseRows(); break;
                case MenuScreen.Instruments: n = InstrumentRows(); break;
                case MenuScreen.Recordings: n = RecordingRows(); break;
                case MenuScreen.Take: n = TakeRows(); break;
                case MenuScreen.Guide: n = GuideRows(); break;
                case MenuScreen.Tips: n = TipRows(); break;
                default: n = LessonRows(ts); break;
            }
            RowCount = n;
            if (HoverRow >= n) HoverRow = -1;
            if (HoverPrev >= n) HoverPrev = -1;
        }

        int HomeRows()
        {
            HeaderTitle = MenuStyle.HomeTitle;
            HeaderNote = LayoutStatus();
            Footer = MenuStyle.HomeFooter;
            bool mr = Mr != null;
            bool mrActive = mr && Mr.Active;
            bool setup = mrActive && Mr.Status == MrStatus.NoRooms;
            bool move = Arrange != null;
            // Lessons, Instruments, (Move instruments), Recordings, (Room), (Setup), Show me around, Exit. ("Real hands" is on
            // the Instruments screen's bottom row in mixed reality: on the home page a stray poke flipped a saved setting.)
            bool reset = move && Layout != null;   // Reset view sits with Move instruments: the two ways of placing them
            int count = 4 + (move ? 1 : 0) + (reset ? 1 : 0) + (mr ? 1 : 0) + (setup ? 1 : 0) + 1;
            HomeGeometry(count, out float h, out float gap);
            int n = 0;
            float y = MenuStyle.HomeTop;

            var r = Put(ref n, RowKind.Menu, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.To = MenuScreen.Lessons; r.Label = MenuStyle.LessonsTitle; r.Icon = IconKind.Lessons;
            r.Note = Text.Courses(Lessons != null ? Lessons.Lessons.Count : 0);
            y += h + gap;

            r = Put(ref n, RowKind.Menu, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.To = MenuScreen.Instruments; r.Label = MenuStyle.InstrumentsTitle; r.Icon = IconKind.Instruments; r.Note = LayoutStatus();
            y += h + gap;

            if (move)
            {
                // The "Move instruments" setting, On or Off (both layouts): a poke switches it.
                bool on = Arrange.MoveOn;
                r = Put(ref n, RowKind.Arrange, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
                r.Icon = IconKind.Move;
                r.On = on;
                r.Label = MenuStyle.MoveLabel;
                r.Note = on ? MenuStyle.MoveNoteOn : MenuStyle.MoveNoteOff;
                y += h + gap;
            }
            if (reset)
            {
                // Reset view: every instrument back in front of the player (the same as both hands pinched and held still).
                r = Put(ref n, RowKind.ResetView, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
                r.Icon = IconKind.Undo;
                r.Label = MenuStyle.ResetViewLabel;
                r.Note = MenuStyle.ResetViewNote;
                r.RowId = MenuRowIds.ResetView;
                y += h + gap;
            }

            r = Put(ref n, RowKind.Menu, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.To = MenuScreen.Recordings; r.Label = MenuStyle.RecordingsTitle; r.Icon = IconKind.Recordings;
            r.Note = Takes != null && Takes.Available == false ? MenuStyle.SavingOff : Text.Saved(TakeList.Count);
            y += h + gap;

            if (mr)
            {
                r = Put(ref n, RowKind.Room, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
                r.Icon = IconKind.Room;
                r.Label = mrActive ? MenuStyle.MusicRoomLabel : MenuStyle.PlayInMyRoom;
                r.Note = mrActive ? MenuStyle.VirtualRoom : Mr.Available ? MenuStyle.Passthrough : MenuStyle.NotAvailable;
                y += h + gap;
            }
            if (setup)
            {
                r = Put(ref n, RowKind.Setup, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
                r.Icon = IconKind.Setup; r.Label = MenuStyle.SetUpMyRoom; r.Note = MenuStyle.SpaceSetup;
                y += h + gap;
            }
            bool present = Guide != null && Guide.Present;
            r = Put(ref n, RowKind.Guide, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.Icon = IconKind.Guide;
            r.Label = present ? MenuStyle.HideSunny : Tour != null ? MenuStyle.SunnyTitle : MenuStyle.ShowMeAround;
            r.Note = present ? MenuStyle.StopTour : Tour != null ? MenuStyle.SunnyNote : MenuStyle.Sunny;
            y += h + gap;

            // Exit, last: the first poke asks ("Tap again to exit", 3 s), the second closes the app.
            r = Put(ref n, RowKind.Exit, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.Icon = IconKind.Exit;
            r.On = ExitArmed || Quitting;
            r.Label = Quitting ? MenuStyle.ExitClosing : ExitArmed ? MenuStyle.ExitConfirm : MenuStyle.ExitLabel;
            r.Note = ExitArmed || Quitting ? "" : MenuStyle.ExitNote;
            return n;
        }

        /// <summary>Row height and gap for a home page of <paramref name="count"/> rows (from y 132, ending near 684).</summary>
        public static void HomeGeometry(int count, out float h, out float gap)
        {
            switch (count)
            {
                case 4: h = MenuStyle.Home4H; gap = MenuStyle.Home4Gap; return;
                case 5: h = MenuStyle.Home5H; gap = MenuStyle.Home5Gap; return;
                case 6: h = MenuStyle.Home6H; gap = MenuStyle.Home6Gap; return;
                case 7: h = MenuStyle.Home7H; gap = MenuStyle.Home7Gap; return;
                case 8: h = MenuStyle.Home8H; gap = MenuStyle.Home8Gap; return;
                default: h = MenuStyle.Home9H; gap = MenuStyle.Home9Gap; return;
            }
        }

        /// <summary>The start panel (the landing page): the title, then "Play here" and, where passthrough and the
        /// scene are available, "Play in my room".</summary>
        int StartRows()
        {
            HeaderTitle = MenuStyle.StartTitle;
            HeaderNote = "";
            Footer = MenuStyle.StartFooter;
            int n = 0;
            float y = MenuStyle.StartTop;
            var r = Put(ref n, RowKind.PlayHere, MenuStyle.RowX0, y, MenuStyle.RowX1, y + MenuStyle.StartRowH);
            r.Label = MenuStyle.PlayHere; r.Icon = IconKind.Play;   // no note: the big label gets the whole row
            y += MenuStyle.StartRowH + MenuStyle.StartGap;
            if (RoomAvailable)
            {
                r = Put(ref n, RowKind.PlayRoom, MenuStyle.RowX0, y, MenuStyle.RowX1, y + MenuStyle.StartRowH);
                r.Label = MenuStyle.PlayInMyRoom; r.Icon = IconKind.House;
            }
            return n;
        }

        /// <summary>"Play in my room" can work here: passthrough is (or is becoming) available and the scene support is not
        /// known to be missing.</summary>
        public bool RoomAvailable => Mr != null && Mr.Available && Mr.Status != MrStatus.NotAvailable;

        /// <summary>The Done / Reset buttons of "Move instruments" (the menu's canvas at half size, floating by the player).</summary>
        int ArrangeRows()
        {
            HeaderTitle = MenuStyle.ArrangeTitle;
            HeaderNote = "";
            int n = 0;
            var r = Put(ref n, RowKind.Done, MenuStyle.ArrangeX0[0], MenuStyle.ArrangeTop, MenuStyle.ArrangeX1[0], MenuStyle.ArrangeBottom);
            r.Label = MenuStyle.DoneLabel; r.Icon = IconKind.Check;
            r = Put(ref n, RowKind.Reset, MenuStyle.ArrangeX0[1], MenuStyle.ArrangeTop, MenuStyle.ArrangeX1[1], MenuStyle.ArrangeBottom);
            r.Label = MenuStyle.ResetLabel; r.Icon = IconKind.Undo;
            return n;
        }

        /// <summary>Change the screen without telling anyone (the panel is hidden: the start panel's screen goes back to
        /// home before the palm menu first opens).</summary>
        public void ResetScreen(MenuScreen screen)
        {
            if (screen == Screen) return;
            Screen = screen;
            _options = null;
            _screenStamp++;
            Scroll = 0f;
            Confirm = false;
            TrashArmedId = null;
            HoverRow = -1;
            Changed();
        }

        /// <summary>The Lessons screen (platform.md 5.3): the Instruments grid's tiles for the loaded instruments that have at
        /// least one lesson, each with "N lessons"; a tile opens its course list.</summary>
        int LessonsRows()
        {
            int n = BackRow(0, MenuStyle.LessonsTitle);
            HeaderNote = MenuStyle.LessonsNote;
            if (Registry == null) return n;
            var all = Registry.All;
            int t = 0;
            for (int i = 0; i < all.Count && t < MenuStyle.GridTiles && n < MaxRows; i++)
            {
                InstrumentDescriptor d = all[i];
                if (!Loaded(d)) continue;
                int count = Lessons != null ? Lessons.Count(d.Kind) : 0;
                if (count <= 0) continue;
                MenuStyle.TileOrigin(t++, out float x0, out float y0);
                var r = Put(ref n, RowKind.Inst, x0, y0, x0 + MenuStyle.TileW, y0 + MenuStyle.TileH);
                r.Inst = d.Kind; r.HasInst = true; r.Desc = d; r.Cell = i;
                r.Label = d.Name ?? "";
                r.Note = Text.LessonCount(count);
                r.On = Layout != null && Layout.Mode == LayoutMode.Single && Layout.Current == d.Kind;
                r.RowId = Text.InstRowId(d);
            }
            return n;
        }

        /// <summary>listRows (794-808): maxScroll = max(0, n*130 - 630); rows clipped to the 130..750 viewport; under 30 px
        /// showing, a row is not laid out.</summary>
        void ListGeometry(int count)
        {
            float span = MenuStyle.ListBottom - MenuStyle.ListTop;
            MaxScroll = Mathf.Max(0f, count * MenuStyle.ListPitch - (MenuStyle.ListPitch - MenuStyle.ListH) - span);
            Scroll = Mathf.Clamp(Scroll, 0f, MaxScroll);
        }

        MenuRow ListSlot(ref int n, int i, RowKind id) => ListSlot(ref n, i, id, MenuStyle.RowX0, MenuStyle.RowX1);

        MenuRow ListSlot(ref int n, int i, RowKind id, float x0, float x1)
        {
            float y = MenuStyle.ListTop + i * MenuStyle.ListPitch - Scroll;
            float y0 = Mathf.Max(MenuStyle.ListTop, y), y1 = Mathf.Min(MenuStyle.ListBottom, y + MenuStyle.ListH);
            if (y1 - y0 < MenuStyle.ListMinVisible) return null;
            var r = Put(ref n, id, x0, y0, x1, y1);
            r.Scrolls = MaxScroll > 0f;
            return r;
        }

        int CourseRows()
        {
            int n = BackRow(0, NameOf(Instrument));
            _courses.Clear();
            if (Lessons != null)
            {
                string id = IdOf(Instrument);
                var all = Lessons.Lessons;
                for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].instrument == id) _courses.Add(all[i]);
            }
            ListGeometry(_courses.Count);
            HeaderNote = MaxScroll > 0f ? MenuStyle.CoursesScroll : MenuStyle.Courses;
            for (int i = 0; i < _courses.Count && n < MaxRows; i++)
            {
                var r = ListSlot(ref n, i, RowKind.Course);
                if (r == null) continue;
                r.Lesson = _courses[i];
                r.Label = _courses[i].title ?? "";
                r.Note = _courses[i].listNote ?? "";
                if (Progress != null && Progress.IsDone(_courses[i].id)) r.Note = MenuStyle.CourseDone + MenuStyle.Dot + r.Note;   // first five: remembered
            }
            return n;
        }

        /// <summary>The Instruments screen (platform.md 5.1): the header note says what is at the seat; two rows of four
        /// portrait tiles in carousel order (unloaded instruments closed up), each with its name only (no status line: the
        /// player asked for the "With you" / "Put away" lines to go, 2026-11-10), an amber frame on the at-seat ones, a
        /// printed tick on My set's members and an "Options" chip on an at-seat tile that has options; under them My set | One alone and Move (+ Real hands in mixed reality). The chips come
        /// before the tiles in the row list, so a poke on a chip is the chip's (RowAt takes the first row it finds).</summary>
        int InstrumentRows()
        {
            if (_options != null) return OptionRows();
            int n = BackRow(0, MenuStyle.InstrumentsTitle);
            HeaderNote = _note ?? LayoutStatus();
            bool single = Layout != null && Layout.Mode == LayoutMode.Single;
            bool mr = Mr != null && Mr.Active;
            if (Registry != null)
            {
                var all = Registry.All;
                for (int pass = 0; pass < 2; pass++)
                {
                    int t = 0;
                    for (int i = 0; i < all.Count && t < MenuStyle.GridTiles && n < MaxRows; i++)
                    {
                        InstrumentDescriptor d = all[i];
                        if (!Loaded(d)) continue;
                        MenuStyle.TileOrigin(t++, out float x0, out float y0);
                        InstrumentPlace place = Layout != null ? Layout.PlaceOf(d.Kind) : InstrumentPlace.Hidden;
                        bool seat = place == InstrumentPlace.Seat;
                        if (pass == 0)
                        {
                            // The "Options" chip of an at-seat tile whose instrument has options.
                            if (!seat || d.Options == null || d.Options.Length == 0) continue;
                            float cx = x0 + MenuStyle.ChipInset, cy = y0 + MenuStyle.ChipInset;
                            var c = Put(ref n, RowKind.Options, cx, cy, cx + MenuStyle.ChipW, cy + MenuStyle.ChipH);
                            c.Inst = d.Kind; c.HasInst = true; c.Desc = d; c.Label = MenuStyle.OptionsChip;
                            c.RowId = Text.OptionsRowId(d);
                            continue;
                        }
                        var r = Put(ref n, RowKind.Tile, x0, y0, x0 + MenuStyle.TileW, y0 + MenuStyle.TileH);
                        r.Inst = d.Kind; r.HasInst = true; r.Desc = d; r.Cell = i;
                        r.Label = d.Name ?? "";
                        r.On = seat;
                        r.Ticked = !single && IsMember(d.Kind);
                        r.RowId = Text.InstRowId(d);
                        r.Anchor = Text.TileAnchor(d);
                    }
                }
            }
            // The bottom row: the segmented pair, then Move (two buttons in mixed reality: + Real hands; else Move fills the row).
            bool hands = mr && Hands != null;
            float[] bx = MenuStyle.BarX0;
            float segW = MenuStyle.SegmentW, butW = hands ? MenuStyle.ButtonW : MenuStyle.ButtonWAlone;
            for (int k = 0; k < 2; k++)
            {
                float x = bx[k];
                var r = Put(ref n, RowKind.Layout, x, MenuStyle.BarTop, x + segW, MenuStyle.BarBottom);
                r.Mode = k == 0 ? LayoutMode.All : LayoutMode.Single;
                r.Label = k == 0 ? MenuStyle.MySet : MenuStyle.OneAlone;
                r.On = Layout != null && Layout.Mode == r.Mode;
                r.RowId = k == 0 ? MenuRowIds.LayoutAll : MenuRowIds.LayoutSingle;
            }
            if (Arrange != null)
            {
                // "Move instruments", the same setting as the home page's row.
                float x = bx[2];
                var r = Put(ref n, RowKind.Arrange, x, MenuStyle.BarTop, x + butW, MenuStyle.BarBottom);
                bool on = Arrange.MoveOn;
                r.Label = MenuStyle.MoveShort;
                r.Icon = IconKind.Move;
                r.On = on;
                r.Note = on ? MenuStyle.MoveOn : MenuStyle.MoveOff;
            }
            if (hands)
            {
                // "Play in my room" draws the hands by default; this switch shows the real ones through the passthrough.
                float x = bx[3];
                bool real = Hands.RealHandsInMr;
                var r = Put(ref n, RowKind.Hands, x, MenuStyle.BarTop, x + butW, MenuStyle.BarBottom);
                r.Label = MenuStyle.RealHands;
                r.Icon = IconKind.Check;
                r.On = real;
                r.Note = real ? MenuStyle.RealHandsOnShort : MenuStyle.RealHandsOffShort;
                r.RowId = MenuRowIds.Hands;
            }
            Footer = Layout != null && Layout.Mode == LayoutMode.Single ? MenuStyle.FooterSingle : MenuStyle.FooterAll;
            return n;
        }

        /// <summary>An instrument's options page (InstrumentDescriptor.Options): one row per option, its label, its small
        /// print and its current choice; a poke steps to the next choice. Back returns to the grid.</summary>
        int OptionRows()
        {
            InstrumentDescriptor d = _options;
            int n = BackRow(0, d.Name);
            HeaderNote = MenuStyle.OptionsNote;
            Footer = MenuStyle.OptionsFooter;
            var opts = d.Options;
            for (int i = 0; opts != null && i < opts.Length && i < MenuStyle.MaxOptionRows && n < MaxRows; i++)
            {
                float y = MenuStyle.MidTop + i * (MenuStyle.MidH + MenuStyle.MidGap);
                var r = Put(ref n, RowKind.Option, MenuStyle.RowX0, y, MenuStyle.RowX1, y + MenuStyle.MidH);
                InstrumentOption o = opts[i];
                r.Inst = d.Kind; r.HasInst = true; r.Desc = d; r.Option = i;
                r.Label = o.Label ?? o.Key ?? "";
                r.Small = o.Note;
                int v = OptionValue(d, i);
                r.Note = o.Choices != null && v >= 0 && v < o.Choices.Length ? o.Choices[v] ?? "" : "";
                r.RowId = Text.OptionRowId(d, i);
                r.Anchor = Text.OptionAnchor(d, i);
            }
            return n;
        }

        /// <summary>The option's stored choice ("musicroom.&lt;id&gt;.&lt;Key&gt;", default Default), clamped to its choices.</summary>
        public int OptionValue(InstrumentDescriptor d, int i)
        {
            if (d?.Options == null || i < 0 || i >= d.Options.Length) return 0;
            InstrumentOption o = d.Options[i];
            int count = o.Choices != null ? o.Choices.Length : 0;
            string key = Text.OptionKey(d, i);
            int v = Settings != null ? Settings.GetInt(key, o.Default) : _memOptions.TryGetValue(key, out int m) ? m : o.Default;
            return count == 0 ? 0 : Mathf.Clamp(v, 0, count - 1);
        }

        void SetOptionValue(InstrumentDescriptor d, int i, int v)
        {
            string key = Text.OptionKey(d, i);
            if (Settings != null) Settings.SetInt(key, v);
            else _memOptions[key] = v;
        }

        /// <summary>drawRecordings: no storage reads "Saving off" (no library at all counts as off on this screen).</summary>
        public bool RecordingsOff => Takes == null || Takes.Available == false;

        int RecordingRows()
        {
            int n = BackRow(0, MenuStyle.RecordingsTitle);
            bool off = RecordingsOff;
            int count = off ? 0 : TakeList.Count;
            ListGeometry(count);
            HeaderNote = off ? MenuStyle.SavingOff : MaxScroll > 0f ? Text.TakesScroll(TakeList.Count) : Text.Takes(TakeList.Count);
            if (off || TakeList.Count == 0)
            {
                BodyTitle = off ? MenuStyle.SavingIsOff : MenuStyle.NoRecordings;
                BodyLine1 = off ? MenuStyle.SavingOffLine : MenuStyle.EmptyLine1;
                BodyLine2 = off ? null : MenuStyle.EmptyLine2;
            }
            // The takes (name over local time), then each one's bin at the right of its row: a separate row, so a poke on
            // the bin never opens the take.
            int first = n;
            for (int i = 0; i < count && n < MaxRows; i++)
            {
                var r = ListSlot(ref n, i, RowKind.Take, MenuStyle.RowX0, MenuStyle.TakeRowX1);
                if (r == null) continue;
                r.Take = TakeList[i];
                r.Label = TakeList[i].name ?? "";
                r.Note = Text.Date(TakeList[i], Takes);
                r.Stars = StarsOf(TakeList[i]);
            }
            int takes = n;
            for (int k = first; k < takes && n < MaxRows; k++)
            {
                MenuRow t = _rows[k];
                var b = Put(ref n, RowKind.Trash, MenuStyle.TakeRowX1 + MenuStyle.TrashGap, t.Y0, MenuStyle.RowX1, t.Y1);
                b.Scrolls = t.Scrolls;
                b.Take = t.Take;
                b.Icon = IconKind.Delete;
                b.On = t.Take != null && t.Take.id == TrashArmedId;
                b.Label = b.On ? MenuStyle.DeleteAsk : "";
            }
            return n;
        }

        int TakeRows()
        {
            int n = BackRow(0, Take != null ? Take.name : MenuStyle.Recording);
            TakeLine = Take != null ? Text.TakeInfo(Take, Takes) : "";
            bool playing = Recorder != null && Recorder.External && Recorder.State == RecorderState.Playing;
            for (int i = 0; i < 3; i++)
            {
                RowKind id = i == 0 ? RowKind.Play : i == 1 ? RowKind.TStop : RowKind.Delete;
                var r = Put(ref n, id, MenuStyle.RowX0, MenuStyle.TakeTop[i], MenuStyle.RowX1, MenuStyle.TakeBottom[i]);
                if (id == RowKind.Play)
                {
                    r.Label = playing ? MenuStyle.Playing : MenuStyle.Play;
                    r.Icon = IconKind.Play;
                    r.On = playing;
                    if (TakeLoadFailed) r.Note = MenuStyle.CantOpen;
                    else if (PlayFailed) r.Note = MenuStyle.CantPlay;
                    else if (TakeRecord == null) r.Note = Takes != null && Takes.Available == false ? MenuStyle.SavingOff : MenuStyle.Loading;
                }
                else if (id == RowKind.TStop) { r.Label = MenuStyle.Stop; r.Icon = IconKind.Stop; }
                else { r.Label = Confirm ? MenuStyle.DeleteSure : MenuStyle.Delete; r.Icon = IconKind.Delete; }
            }
            if (Confirm) { Footer = MenuStyle.DeleteFooter; FooterColor = MenuStyle.Red; }
            else if (Settings != null)
            {
                // "Keep painting" (the room's wall paint): the setting, On or Off; a poke switches it.
                bool keep = Settings.KeepPaintings;
                var k = Put(ref n, RowKind.Paint, MenuStyle.RowX0, MenuStyle.KeepTop, MenuStyle.RowX1, MenuStyle.KeepBottom);
                k.Label = MenuStyle.KeepPainting;
                k.Note = keep ? MenuStyle.KeepPaintingOn : MenuStyle.KeepPaintingOff;
                k.On = keep;
            }
            return n;
        }

        int LessonRows(TutorialState ts)
        {
            Lesson lesson = CurrentLesson();
            int n = BackRow(0, lesson != null ? lesson.title : "");
            HeaderNote = lesson != null && TryKind(lesson.instrument, out InstrumentKind lk) ? NameOf(lk) : "";
            LessonState = ts;
            int hits = Tutorial != null ? Tutorial.EventsDone : 0;
            int total = Tutorial != null ? Tutorial.EventsTotal : 0;
            int mistakes = Tutorial != null ? Tutorial.Mistakes : 0;   // live (ITutorial.Mistakes, tutorial.mistakes)
            ProgressFrac = total > 0 ? Mathf.Clamp01(hits / (float)total) : 0f;
            ProgressLeft = Text.Progress(hits, total);
            ProgressRight = Text.Mistakes(mistakes);
            ProgressMistakes = mistakes > 0;
            Digit = Text.Digit(Tutorial != null ? Tutorial.Digit : 1);   // ITutorial.Digit = max(1, ceil(seconds left))
            Hint = Tutorial != null && Tutorial.Hint != null ? Tutorial.Hint : "";
            if (ts == TutorialState.Done)
            {
                var s = Tutorial != null ? Tutorial.Summary : default;
                SummaryLine = Text.Summary(s.Notes, s.Mistakes, s.Seconds, Tutorial != null ? Tutorial.CountNoun : null);
                // First five (tutorials-audit.md B6): "Next: <title>" first, the next lesson of this instrument.
                Lesson next = Progress != null && lesson != null && Lessons != null ? Lessons.Get(Progress.NextLessonId(lesson.id) ?? "") : null;
                if (next != null)
                {
                    var nr = Put(ref n, RowKind.Next, MenuStyle.RowX0, MenuStyle.NextTop[0], MenuStyle.RowX1, MenuStyle.NextBottom[0]);
                    nr.Lesson = next; nr.Label = MenuStyle.NextPrefix + (next.title ?? ""); nr.Icon = IconKind.Play;
                    for (int i = 0; i < 2; i++)
                    {
                        var r2 = Put(ref n, i == 0 ? RowKind.Replay : RowKind.Courses, MenuStyle.RowX0, MenuStyle.NextTop[i + 1], MenuStyle.RowX1, MenuStyle.NextBottom[i + 1]);
                        r2.Label = i == 0 ? MenuStyle.Replay : MenuStyle.Courses;
                        r2.Icon = i == 0 ? IconKind.Undo : IconKind.Back;
                    }
                    return n;
                }
                for (int i = 0; i < 2; i++)
                {
                    var r = Put(ref n, i == 0 ? RowKind.Replay : RowKind.Courses, MenuStyle.RowX0, MenuStyle.DoneTop[i], MenuStyle.RowX1, MenuStyle.DoneBottom[i]);
                    r.Label = i == 0 ? MenuStyle.Replay : MenuStyle.Courses;
                    r.Icon = i == 0 ? IconKind.Play : IconKind.Back;
                }
            }
            else
            {
                var r = Put(ref n, RowKind.Stop, MenuStyle.RowX0, MenuStyle.StopTop, MenuStyle.RowX1, MenuStyle.StopBottom);
                r.Label = MenuStyle.Stop;
                r.Icon = IconKind.Stop;
            }
            return n;
        }

        public Lesson CurrentLesson() =>
            Tutorial != null && Tutorial.LessonId != null && Lessons != null ? Lessons.Get(Tutorial.LessonId) : null;

        // ---------------------------------------------------------------- navigation and actions

        /// <summary>Go(): a new screen resets the scroll, the Delete confirm and the hover, refreshes the
        /// takes for recordings and raises MenuEvent{Screen}; the same screen only redraws.</summary>
        public void Go(MenuScreen screen)
        {
            if (screen == Screen)
            {
                if (_options != null && screen == MenuScreen.Instruments) CloseOptions();   // the grid again
                Changed();
                return;
            }
            Screen = screen;
            _options = null;
            _screenStamp++;
            Scroll = 0f;
            Confirm = false;
            TrashArmedId = null;
            HoverRow = -1;
            if (screen == MenuScreen.Recordings) RefreshTakes();
            Changed();
            _host?.ScreenChanged(screen);
            if (!Shown) return;
            if (screen == MenuScreen.Instruments) InstrumentsShown();
            else if (screen == MenuScreen.Lessons) GridSeen = true;
        }

        /// <summary>The Instruments grid came into view (a screen change while shown, or the panel opening on it): Sunny's
        /// signals, and the portraits are wanted from now on.</summary>
        void InstrumentsShown()
        {
            GridSeen = true;
            _host?.Signal(SignalInstruments);
            if (Layout != null && Layout.Mode == LayoutMode.All && Layout.MySet != null && Layout.MySet.Count >= 2) _host?.Signal(SignalInstrumentsSet);
        }

        /// <summary>The options page of an instrument (a sub-page of the Instruments screen: no MenuEvent; a touch that began
        /// on the grid never fires on the page, as for a screen change).</summary>
        public void OpenOptions(InstrumentDescriptor d)
        {
            if (d == null || d.Options == null || d.Options.Length == 0) return;
            if (Screen != MenuScreen.Instruments) Go(MenuScreen.Instruments);
            _options = d;
            _screenStamp++;
            Scroll = 0f;
            HoverRow = -1;
            Changed();
        }

        void CloseOptions()
        {
            _options = null;
            _screenStamp++;
            HoverRow = -1;
            Changed();
        }

        /// <summary>The header's short-lived note, for NoteMs.</summary>
        void ShowNote(string note)
        {
            _note = note;
            _noteUntil = _now + MenuStyle.NoteMs;
            Changed();
        }

        public int RowAt(float px, float py)
        {
            for (int i = 0; i < RowCount; i++) if (_rows[i].Contains(px, py)) return i;
            return -1;
        }

        /// <summary>fire(): a row ignores a second poke within DEBOUNCE (per slot, across screens).</summary>
        public void Fire(int i)
        {
            if (i < 0 || i >= RowCount) return;
            MenuRow r = _rows[i];
            if (_now - r.FiredAt < MenuConstants.DebounceMs) return;
            r.FiredAt = _now;
            Press(r);
        }

        /// <summary>press(): a row's action.</summary>
        void Press(MenuRow r)
        {
            // MenuRowEvent first (sunny-tour.md 7.2), with the screen the press happened on; a first poke that asks again
            // (Exit, Delete) is Confirming.
            bool confirming = (r.Id == RowKind.Exit && !ExitArmed && !Quitting) || (r.Id == RowKind.Delete && !Confirm)
                || (r.Id == RowKind.Trash && (r.Take == null || r.Take.id != TrashArmedId));
            _host?.RowPressed(new MenuRowEvent { RowId = r.Id == RowKind.Menu ? MenuRowIds.OfScreen(r.To) : r.RowId, Screen = Screen, Confirming = confirming });
            switch (r.Id)
            {
                case RowKind.Menu:
                    Go(r.To);
                    break;
                case RowKind.Guide:
                    if (Guide != null && Guide.Present)
                    {
                        // "Hide Sunny": its voice and the tour stop at once and it leaves; the menu stays open.
                        Guide.Dismiss();
                        Changed();
                    }
                    else if (Tour != null) Go(MenuScreen.Guide);   // first five: the Sunny screen (the tour, tips, layers)
                    else
                    {
                        // Sunny's tour again: the panel goes away first, since the tour's first step is to open it.
                        _host?.Hide();
                        Guide?.Start();
                    }
                    break;
                case RowKind.Room:
                    if (Mr != null)
                    {
                        if (Mr.Active) Mr.Exit();
                        else if (Mr.Available) Run(Mr.Enter(), "entering mixed reality");
                        else Debug.Log("[Menu] Play in my room: passthrough is not available here");
                    }
                    Changed();
                    break;
                case RowKind.Setup:
                    if (Mr != null) Run(Mr.RunSpaceSetup(), "Space Setup");
                    Changed();
                    break;
                case RowKind.Paint:
                    // Keep painting: saved at once (the wall paint reads it when a painting would fade).
                    if (Settings != null) { Settings.KeepPaintings = !Settings.KeepPaintings; Settings.Save(); }
                    Changed();
                    break;
                case RowKind.Hands:
                    // Real hands (the passthrough cut-out) or the drawn ones; saved, and switched at once.
                    if (Hands != null) Hands.RealHandsInMr = !Hands.RealHandsInMr;
                    Changed();
                    break;
                case RowKind.Back:
                    if (OnOptions) { CloseOptions(); break; }   // an options page goes back to the grid
                    if (Screen == MenuScreen.Lesson) Tutorial?.Stop();
                    Go(MenuStyle.Parent(Screen));
                    break;
                case RowKind.Tile:
                    PokeTile(r);
                    break;
                case RowKind.Options:
                    OpenOptions(r.Desc);
                    break;
                case RowKind.Option:
                    CycleOption(r);
                    break;
                case RowKind.Inst:
                    Instrument = r.Inst;
                    Go(MenuScreen.Courses);
                    break;
                case RowKind.Course:
                case RowKind.Next:   // first five: the lesson after this one
                    // The tutorial brings the lesson's instrument itself (Bring(kind, Lesson), platform.md 7.3); its start
                    // shows the lesson screen (ObserveTutorial).
                    if (r.Lesson != null) Tutorial?.Start(r.Lesson.id);
                    break;
                // ---- first five: the Sunny screen ----
                case RowKind.Tour:
                    _host?.Hide();   // the tour opens the menu itself later
                    Tour?.StartTour();
                    break;
                case RowKind.Tip:
                    if (Tour != null && r.Index >= 0 && r.Index < Tour.TipCount)
                    {
                        _host?.Hide();
                        Tour.PlayTip(Tour.TipId(r.Index));
                    }
                    break;
                case RowKind.TipsOn:
                    if (Tour != null) Tour.TipsEnabled = !Tour.TipsEnabled;
                    Changed();
                    break;
                case RowKind.LookPinch:
                    if (Look != null) Look.On = !Look.On;
                    Changed();
                    break;
                case RowKind.Layers:
                    _host?.Hide();
                    if (Guide != null && !Guide.DeckGuideRunning) Guide.StartDeckWalkthrough();   // the full walkthrough (GUIDE: the quick show)
                    break;
                case RowKind.Stop:
                case RowKind.Courses:
                    Tutorial?.Stop();
                    Go(MenuScreen.Courses);
                    break;
                case RowKind.Replay:
                    if (Tutorial != null && Tutorial.LessonId != null) Tutorial.Start(Tutorial.LessonId);
                    break;
                case RowKind.Layout:
                    // My set / One alone: the mode changes; One alone keeps the last one alone (else the lead), never the faced one.
                    if (Layout != null && Layout.Mode != r.Mode) Layout.Set(r.Mode, r.HasInst ? r.Inst : OneAloneChoice());
                    Changed();
                    break;
                case RowKind.Take:
                    if (r.Take != null) OpenTake(r.Take);
                    break;
                case RowKind.Play:
                    PlayOpenTake();
                    Changed();
                    break;
                case RowKind.TStop:
                    Recorder?.Stop();
                    Changed();
                    break;
                case RowKind.Delete:
                    if (!Confirm) { Confirm = true; Changed(); }
                    else DeleteTake(Take);
                    break;
                case RowKind.Trash:
                    // The list's bin: the first poke asks ("Delete?" in red, 3 s), the second deletes that take.
                    if (r.Take == null) break;
                    if (r.Take.id != TrashArmedId)
                    {
                        TrashArmedId = r.Take.id;
                        _trashArmedAt = _now;
                    }
                    else
                    {
                        TrashArmedId = null;
                        DeleteTake(r.Take);
                    }
                    Changed();
                    break;
                case RowKind.Arrange:
                    if (Arrange != null)
                    {
                        if (Arrange.MoveOn) Arrange.EndArrange();   // Off: the menu stays open
                        else
                        {
                            // On: the panel goes away so the room is clear to move them; the Done / Reset buttons float by
                            // the player.
                            Arrange.BeginArrange();
                            if (Arrange.Arranging) _host?.Hide();
                        }
                    }
                    Changed();
                    break;
                case RowKind.Exit:
                    if (Quitting) break;
                    if (!ExitArmed)
                    {
                        ExitArmed = true;
                        _exitArmedAt = _now;
                    }
                    else
                    {
                        Quitting = true;
                        ExitArmed = false;
                        // Move instruments stays as the player set it (every drop is saved when it lands).
                        _host?.RequestQuit();
                    }
                    Changed();
                    break;
                case RowKind.PlayHere:
                    _host?.StartChosen(false);
                    break;
                case RowKind.PlayRoom:
                    if (Mr != null && Mr.Available) Run(Mr.Enter(), "entering mixed reality");
                    _host?.StartChosen(true);
                    break;
                case RowKind.Done:
                    Arrange?.EndArrange();
                    break;
                case RowKind.Reset:
                    Arrange?.ResetArrangement();
                    break;
                case RowKind.ResetView:
                    // The menu goes away so the player sees the instruments land in front of them.
                    Layout?.BringToPlayer();
                    _host?.Hide();
                    break;
            }
            _uiVersion++;
        }

        /// <summary>My set -> One alone keeps the instrument the player last had alone when it is in My set, else My set's
        /// lead. Never the one the head happens to face (2026-11-10: nothing is chosen or moved by where the head looks).</summary>
        InstrumentKind OneAloneChoice()
        {
            var set = Layout.MySet;
            if (set == null || set.Count == 0) return Layout.Current;
            InstrumentKind lead = Layout.Lead;
            bool leadIn = false;
            for (int i = 0; i < set.Count; i++)
            {
                if (set[i] == Layout.Current) return Layout.Current;
                leadIn |= set[i] == lead;
            }
            return leadIn ? lead : set[0];
        }

        // ---------------------------------------------------------------- first five: the Sunny screen and its tips

        /// <summary>The Sunny screen (sunny-tour.md 3.3): the tour again, the Tips list, the deck walkthrough (which builds a
        /// song in layers), and "Tips while I play".</summary>
        int GuideRows()
        {
            int n = BackRow(0, MenuStyle.SunnyTitle);
            HeaderNote = MenuStyle.SunnyNote;
            Footer = MenuStyle.SunnyFooter;
            float h = MenuStyle.MidH, gap = MenuStyle.MidGap, y = MenuStyle.MidTop;
            var r = Put(ref n, RowKind.Tour, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.Label = MenuStyle.TourLabel; r.Note = MenuStyle.TourNote; r.Icon = IconKind.Play;
            y += h + gap;
            r = Put(ref n, RowKind.Menu, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.To = MenuScreen.Tips; r.Label = MenuStyle.TipsLabel; r.Icon = IconKind.Lessons;
            r.Note = Tour != null ? Tour.TipsHeard + " of " + Tour.TipCount + " heard" : "";
            y += h + gap;
            r = Put(ref n, RowKind.Layers, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            r.Label = MenuStyle.LayersLabel; r.Note = MenuStyle.LayersNote; r.Icon = IconKind.Recordings;
            y += h + gap;
            r = Put(ref n, RowKind.TipsOn, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
            bool on = Tour == null || Tour.TipsEnabled;
            r.Label = MenuStyle.TipsOnLabel; r.On = on; r.Icon = IconKind.Check;
            r.Note = on ? MenuStyle.TipsOnOn : MenuStyle.TipsOnOff;
            if (Look != null)
            {
                y += h + gap;
                r = Put(ref n, RowKind.LookPinch, MenuStyle.RowX0, y, MenuStyle.RowX1, y + h);
                bool look = Look.On;
                r.Label = MenuStyle.LookPinchLabel; r.On = look; r.Icon = IconKind.Check;
                r.Note = look ? MenuStyle.LookPinchOn : MenuStyle.LookPinchOff;
            }
            return n;
        }

        /// <summary>Every tip, in the tour's order: its title, and New or Heard. Poke one: Sunny comes and shows it again.</summary>
        int TipRows()
        {
            int n = BackRow(0, MenuStyle.TipsLabel);
            int count = Tour != null ? Tour.TipCount : 0;
            ListGeometry(count);
            HeaderNote = Tour != null ? Tour.TipsHeard + " of " + count + " heard" : "";
            for (int i = 0; i < count && n < MaxRows; i++)
            {
                var r = ListSlot(ref n, i, RowKind.Tip);
                if (r == null) continue;
                r.Index = i;
                r.Label = Tour.TipTitle(i) ?? "";
                bool heard = Tour.TipHeard(i);
                r.Note = heard ? MenuStyle.TipHeard : MenuStyle.TipNew;
            }
            return n;
        }

        /// <summary>A tile poke (platform.md 5.1). One alone: the instrument at the seat stays (its options page opens when it
        /// has options); any other comes alone (Bring, Menu). My set: a member goes back to its place (the last one stays:
        /// "One stays with you"); another joins when PreviewJoin says Add, takes the place of the member it names on
        /// Replace (the header names it for 2 s: "Harp went back to the corner"), and otherwise does not come: "No room:
        /// take one out first" with a soft wooden knock (the Ui click, never an error buzz).</summary>
        void PokeTile(MenuRow r)
        {
            InstrumentDescriptor d = r.Desc;
            if (Layout == null || d == null) return;
            InstrumentKind k = d.Kind;
            if (Layout.Mode == LayoutMode.Single)
            {
                if (Layout.Current == k) { if (d.Options != null && d.Options.Length > 0) OpenOptions(d); }
                else Layout.Bring(k, BringReason.Menu);
            }
            else if (IsMember(k))
            {
                if (Layout.MySet.Count <= 1 || !Layout.PutAway(k)) ShowNote(MenuStyle.OneStays);
            }
            else
            {
                JoinPreview p = Layout.PreviewJoin(k);
                switch (p.Action)
                {
                    case JoinAction.Add:
                        Layout.Bring(k, BringReason.Menu);
                        break;
                    case JoinAction.Replace:
                        if (Layout.Bring(k, BringReason.Menu))
                        {
                            InstrumentDescriptor gone = Registry?.Get(p.Replaced);
                            if (gone != null) ShowNote(Text.WentBack(gone));
                        }
                        break;
                    case JoinAction.Already:
                        break;
                    default:   // Alone or None: it would not fit beside anyone
                        ShowNote(MenuStyle.NoRoom);
                        Sound?.Click(false);
                        break;
                }
            }
            Changed();
        }

        /// <summary>An option row: the next choice, stored, InstrumentOptionEvent, and the option's tip said once.</summary>
        void CycleOption(MenuRow r)
        {
            InstrumentDescriptor d = r.Desc;
            int i = r.Option;
            if (d?.Options == null || i < 0 || i >= d.Options.Length) return;
            InstrumentOption o = d.Options[i];
            int count = o.Choices != null ? o.Choices.Length : 0;
            if (count == 0) return;
            int v = (OptionValue(d, i) + 1) % count;
            SetOptionValue(d, i, v);
            _host?.OptionChanged(d.Kind, o.Key, v);
            if (!string.IsNullOrEmpty(o.Tip) && Guide != null && !Guide.TipSeen(o.Tip)) Guide.Tip(o.Tip);
            Changed();
        }

        /// <summary>The IGuideAnchors point of a shown row by its anchor id ("menu.tile.&lt;id&gt;" on the grid,
        /// "menu.option.&lt;id&gt;.&lt;Key&gt;" on an options page): its canvas centre. False when no shown row answers it.</summary>
        public bool TryGetAnchorRow(string anchor, out MenuRow row)
        {
            row = null;
            if (!Shown || string.IsNullOrEmpty(anchor)) return false;
            for (int i = 0; i < RowCount; i++)
                if (_rows[i].Anchor != null && string.Equals(_rows[i].Anchor, anchor, StringComparison.Ordinal)) { row = _rows[i]; return true; }
            return false;
        }

        static async void Run(Task task, string what)
        {
            if (task == null) return;
            try { await task; }
            catch (Exception e) { Debug.LogWarning($"[Menu] {what} failed: {e.Message}"); }
        }

        /// <summary>setScroll(): clamp, relayout at once so the next poke hits the moved rows.</summary>
        public void SetScroll(float px)
        {
            float s = Mathf.Clamp(px, 0f, MaxScroll);
            if (s == Scroll) return;
            Scroll = s;
            LayoutRows();
            Dirty = true;
        }

        // ---------------------------------------------------------------- the recordings library

        // The constellation pictures on the Recordings list (SongStars.BuildThumb: the take's own notes as the night sky draws
        // them, so nothing more is saved): one per take id, made once from the take's events when its row is first laid out
        // (read from the library, which reads on its worker), then kept; the row is drawn without one meanwhile.
        readonly Dictionary<string, SongStars.Thumb> _stars = new Dictionary<string, SongStars.Thumb>(StringComparer.Ordinal);
        readonly HashSet<string> _starsLoading = new HashSet<string>(StringComparer.Ordinal);
        SongStars.Types _starTypes;

        /// <summary>A take's constellation picture (null until it is made; asking starts it).</summary>
        public SongStars.Thumb StarsOf(TakeInfo t)
        {
            if (t == null || string.IsNullOrEmpty(t.id)) return null;
            if (_stars.TryGetValue(t.id, out SongStars.Thumb th)) return th;
            if (Takes != null && _starsLoading.Add(t.id)) LoadStars(t.id);
            return null;
        }

        async void LoadStars(string id)
        {
            ITakeLibrary lib = Takes;
            TakeRecord rec = null;
            try { rec = await lib.Get(id); }
            catch (Exception e) { Debug.LogWarning("[Menu] take stars failed: " + e.Message); }
            _starsLoading.Remove(id);
            if (_starTypes == null) _starTypes = new SongStars.Types(Registry);
            _stars[id] = SongStars.BuildThumb(rec?.events, _starTypes);   // an empty picture when the take could not be read
            Changed();
        }

        public async void RefreshTakes()
        {
            ITakeLibrary lib = Takes;
            if (lib == null) return;
            int req = ++_takesRequest;
            List<TakeInfo> list;
            try { list = await lib.List(); }
            catch (Exception e) { Debug.LogWarning("[Menu] takes list failed: " + e.Message); return; }
            if (req != _takesRequest) return;   // a newer refresh is on its way
            TakeList.Clear();
            if (list != null) TakeList.AddRange(list);
            Changed();
        }

        async void OpenTake(TakeInfo entry)
        {
            Take = entry;
            TakeRecord = null;
            PlayWhenLoaded = TakeLoadFailed = PlayFailed = false;
            Go(MenuScreen.Take);
            ITakeLibrary lib = Takes;
            if (lib == null) return;
            TakeRecord rec = null;
            string why = null;
            try { rec = await lib.Get(entry.id); }
            catch (Exception e) { why = e.Message; }
            if (Take != entry) return;
            if (rec == null || rec.events == null)
            {
                // Its file is gone or unreadable: the Play row says so (a press never does nothing).
                TakeLoadFailed = true;
                Debug.LogWarning($"[Menu] '{entry.name}' could not be opened: {why ?? (rec == null ? "no readable file" : "no events in it")}");
                if (PlayWhenLoaded) { PlayWhenLoaded = false; PlayFailed = true; }
                Changed();
                return;
            }
            TakeRecord = rec;
            if (PlayWhenLoaded) { PlayWhenLoaded = false; PlayOpenTake(); }   // Play was pressed while it loaded: it plays now
            Changed();
        }

        /// <summary>Recordings > a recording > Play: it plays (a take, or a saved loop that loops), the gallery is told which
        /// recording it is, and one log line says what came of the press. Pressed while the recording still loads, it plays
        /// once loaded; one that cannot play says so on the Play row.</summary>
        void PlayOpenTake()
        {
            string name = Take != null ? Take.name : "?";
            PlayFailed = false;
            if (TakeRecord == null)
            {
                if (TakeLoadFailed || Takes == null || Takes.Available == false)
                {
                    PlayFailed = true;
                    Debug.Log($"[Menu] Play '{name}': nothing played: {(TakeLoadFailed ? "its file could not be read" : "saving is off")}");
                }
                else
                {
                    PlayWhenLoaded = true;
                    Debug.Log($"[Menu] Play '{name}': still loading, it plays once loaded");
                }
                return;
            }
            if (Recorder == null) { PlayFailed = true; Debug.Log($"[Menu] Play '{name}': nothing played: no recorder"); return; }
            if (Recorder.PlayEvents(TakeRecord.events, TakeRecord.seconds))
            {
                _playingTakeId = TakeRecord.id ?? Take?.id;
                PlayingTake.Note(_playingTakeId, TakeRecord.events);   // the Room's gallery: which recording this is
                Debug.Log($"[Menu] Play '{name}': plays {Recorder.LastPlay}");
            }
            else
            {
                PlayFailed = true;
                Debug.Log($"[Menu] Play '{name}': nothing played: {Recorder.LastPlay}");
            }
        }

        /// <summary>Deletes a saved take (its file and its index entry, persisted by the library). The library is told first
        /// (synchronously, TakeLibrary.Removing: a loop session saved under that id lets go of it), then a playback of this
        /// very take stops; any other playback goes on. The list drops it at once and again when the library changes.</summary>
        async void DeleteTake(TakeInfo entry)
        {
            ITakeLibrary lib = Takes;
            if (entry == null || lib == null) return;
            Confirm = false;
            if (TrashArmedId == entry.id) TrashArmedId = null;
            bool stop = PlayingTakeId == entry.id;
            Task<bool> removing;
            try { removing = lib.Remove(entry.id); }
            catch (Exception e) { Debug.LogWarning("[Menu] take delete failed: " + e.Message); return; }
            if (stop)
            {
                Recorder.Stop();
                _playingTakeId = null;
            }
            for (int i = TakeList.Count - 1; i >= 0; i--) if (TakeList[i] == entry || TakeList[i].id == entry.id) TakeList.RemoveAt(i);
            if (entry.id != null) _stars.Remove(entry.id);
            Changed();
            if (Take == entry || (Take != null && Take.id == entry.id))
            {
                Take = null;
                TakeRecord = null;
                if (Screen == MenuScreen.Take) Go(MenuScreen.Recordings);
            }
            try { await removing; }
            catch (Exception e) { Debug.LogWarning("[Menu] take delete failed: " + e.Message); }
        }

        // ---------------------------------------------------------------- pokes

        /// <summary>Start a frame of pokes: the debounce clock and the hover candidate.</summary>
        public void BeginPokes(double nowMs)
        {
            _now = nowMs;
            _hover = -1;
        }

        /// <summary>One fingertip (slot 0 left, 1 right index tip), in panel-local metres (spec 08 section 1.4). A tip
        /// that was in front of the surface (z &gt; ARM) and reaches it (z &lt;= 0, not from more than 6 cm behind) fires
        /// the row under it; it must come back out past ARM before it can fire again. On a scrolling list the touch fires
        /// on release unless it dragged more than SLOP, and a drag scrolls (the content follows the finger).</summary>
        public void Poke(int slot, bool active, bool wasActive, Vector3 local)
        {
            ref PokeState st = ref _pokes[slot];
            if (!active) { st.Armed = false; st.Touching = false; return; }
            MenuMath.ToSurface(local, out float u, out float y, out float z);
            if (!MenuMath.InsidePokeArea(u, y)) { st.Armed = false; st.Touching = false; return; }
            if (!Shown) return;   // a press earlier this frame put the panel away
            MenuMath.ToCanvas(u, y, out float px, out float py);
            if (st.Touching)
            {
                if (z > MenuConstants.Arm)
                {
                    st.Touching = false;
                    st.Armed = true;
                    // Release-fire only on the screen the touch began on: otherwise it could fire the row that replaced
                    // the touched one (e.g. the first course after a long press on "Piano").
                    if (st.Row >= 0 && st.Row < RowCount && st.Stamp == _screenStamp && _rows[st.Row].Scrolls && !st.Moved) Fire(st.Row);
                }
                else if (st.Row >= 0 && st.Row < RowCount && st.Stamp == _screenStamp && _rows[st.Row].Scrolls)
                {
                    float dy = y - st.Y0;
                    if (!st.Moved && Mathf.Abs(dy) > MenuConstants.Slop) st.Moved = true;
                    if (st.Moved) SetScroll(st.Scroll0 + dy / MenuConstants.Height * MenuConstants.CanvasH);
                }
            }
            else if (st.Armed && z <= 0f && z > MenuConstants.Behind && wasActive)
            {
                st.Armed = false;
                st.Touching = true;
                st.Y0 = y;
                st.Scroll0 = Scroll;
                st.Moved = false;
                st.Stamp = _screenStamp;
                st.Row = RowAt(px, py);
                if (st.Row >= 0 && !_rows[st.Row].Scrolls) Fire(st.Row);
            }
            else if (!st.Armed && z > MenuConstants.Arm) st.Armed = true;
            if (_hover < 0 && z > MenuStyle.GlowFrom && z < MenuConstants.Hover) _hover = RowAt(px, py);
        }

        /// <summary>Look and pinch (the Gaze module through IGazeMenu): the gazed row lights with the poke's own hover look,
        /// unless a fingertip already hovers a row this frame. Between BeginPokes and EndPokes.</summary>
        public void GazeHover(int row)
        {
            if (_hover < 0 && row >= 0 && row < RowCount) _hover = row;
        }

        /// <summary>Look and pinch: the row fires exactly as a poke fires it (Fire: the debounce, MenuRowEvent, the action).</summary>
        public bool GazeFire(int row)
        {
            if (!Shown || row < 0 || row >= RowCount) return false;
            Fire(row);
            return true;
        }

        /// <summary>Desktop mouse (spec 08 section 1.4 "Desktop"): a ray hit on the face in canvas px. Mouse-down fires the
        /// row under it (desktopDown); on a scrolling list it behaves like a touch instead (drag scrolls, a still
        /// click fires on release) so the long lists are reachable without a headset. The row under the mouse lights.</summary>
        public void Mouse(bool hit, float px, float py, bool down, bool held)
        {
            if (!Shown) { _mouse.Touching = false; return; }
            if (hit && _hover < 0) _hover = RowAt(px, py);
            if (down && hit)
            {
                _mouse.Touching = true;
                _mouse.Moved = false;
                _mouse.Py0 = py;
                _mouse.Scroll0 = Scroll;
                _mouse.Stamp = _screenStamp;
                _mouse.Row = RowAt(px, py);
                if (_mouse.Row >= 0 && !_rows[_mouse.Row].Scrolls) Fire(_mouse.Row);
                return;
            }
            if (!_mouse.Touching) return;
            bool scrollRow = _mouse.Row >= 0 && _mouse.Row < RowCount && _mouse.Stamp == _screenStamp && _rows[_mouse.Row].Scrolls;
            if (!held)
            {
                _mouse.Touching = false;
                if (scrollRow && !_mouse.Moved) Fire(_mouse.Row);
                return;
            }
            if (hit && scrollRow)
            {
                float d = py - _mouse.Py0;
                if (!_mouse.Moved && Mathf.Abs(d) > MenuConstants.Slop / MenuConstants.Height * MenuConstants.CanvasH) _mouse.Moved = true;
                if (_mouse.Moved) SetScroll(_mouse.Scroll0 - d);
            }
        }

        /// <summary>End of the frame's pokes: the hover row and its 120 ms fade. Hover never repaints the canvas
        /// (ARCHITECTURE.md 15.6): the face shader draws the highlight from HoverK / HoverRow / HoverPrev each frame, so
        /// the fade is smooth to k = 1 (a repaint-driven fade could rest at ~85%). The previous row is dropped once
        /// its fade-out has finished.</summary>
        public void EndPokes()
        {
            int hover = _hover >= 0 ? _hover : Spotlight >= 0 && Spotlight < RowCount ? Spotlight : -1;   // first five: Sunny's spotlight
            if (hover != HoverRow)
            {
                HoverPrev = HoverRow;
                HoverRow = hover;
                HoverT = _now;
                _fading = true;
            }
            else if (_fading)
            {
                if (_now - HoverT >= MenuStyle.HoverMs)
                {
                    _fading = false;
                    HoverPrev = -1;
                }
            }
        }

        bool _fading;

        /// <summary>The hover highlight's fade 0..1 at <paramref name="nowMs"/>.</summary>
        public float HoverK(double nowMs) => Mathf.Min(1f, (float)((nowMs - HoverT) / MenuStyle.HoverMs));
    }
}
