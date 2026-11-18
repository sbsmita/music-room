// Fake services, host and canvas for the menu's EditMode tests: the controller is driven exactly as the module drives
// it (Sync, BeginPokes / Poke / EndPokes), with panel-local fingertip positions from the curved-surface math.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Menu;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    sealed class FakeHost : IMenuHost
    {
        public MenuController Ctl;
        public int Hides, Quits;
        public readonly List<bool> StartChoices = new List<bool>();
        public readonly List<MenuScreen> Screens = new List<MenuScreen>();
        public readonly List<MenuRowEvent> Rows = new List<MenuRowEvent>();
        public readonly List<string> Signals = new List<string>();
        public readonly List<InstrumentOptionEvent> Options = new List<InstrumentOptionEvent>();
        public readonly List<InstrumentKind> LessonsShown = new List<InstrumentKind>();
        /// <summary>Hide counts but keeps the panel shown (a test that pokes the Move button again and again).</summary>
        public bool KeepShown;
        public void Hide() { Hides++; if (!KeepShown) Ctl?.SetShown(false); }
        public void ScreenChanged(MenuScreen screen) => Screens.Add(screen);
        public void RequestQuit() => Quits++;
        public void StartChosen(bool mixedReality) => StartChoices.Add(mixedReality);
        public void RowPressed(in MenuRowEvent e) => Rows.Add(e);
        public void Signal(string signal) => Signals.Add(signal);
        public void OptionChanged(InstrumentKind kind, string key, int value) => Options.Add(new InstrumentOptionEvent { Kind = kind, Key = key, Value = value });
        public void LessonShown(InstrumentKind kind) => LessonsShown.Add(kind);
        public string LastRowId => Rows.Count > 0 ? Rows[Rows.Count - 1].RowId : null;
    }

    /// <summary>The instrument registry as the app builds it (every IInstrumentDefinition), every instrument loaded unless
    /// <paramref name="loaded"/> says otherwise. Each call returns fresh descriptors, so a test may change one.</summary>
    static class TestRegistry
    {
        public static InstrumentRegistry Make(Func<InstrumentKind, bool> loaded = null)
        {
            var reg = InstrumentRegistry.Discover();
            foreach (var d in reg.All) if (loaded == null || loaded(d.Kind)) reg.MarkLoaded(d.Kind);
            return reg;
        }

        /// <summary>Two options on a descriptor (a tone and a help row), for the options page.</summary>
        public static void GiveOptions(InstrumentDescriptor d)
        {
            d.Options = new[]
            {
                new InstrumentOption { Key = "help", Label = d.Name + " help", Choices = new[] { "Off", "On" }, Default = 1, Note = "Keeps held notes in tune", Tip = "tip_" + d.Id + "_help" },
                new InstrumentOption { Key = "tone", Label = d.Name + " tone", Choices = new[] { "Warm", "Classic", "Bright" }, Default = 1 },
            };
        }
    }

    sealed class FakeArrange : IArrangeMode
    {
        public bool Arranging { get; set; }
        public int Begins, Ends, Resets;
        public void BeginArrange() { Begins++; Arranging = true; }
        public void EndArrange() { Ends++; Arranging = false; }
        public void ResetArrangement() => Resets++;
    }

    /// <summary>Layout as the menu sees it (platform.md 4.2-4.4): My set (default piano, drums, harp), One alone, and the
    /// calls the grid makes. PreviewJoin answers Add while the set has room, else Replace (the first member) unless the test
    /// sets Join; Bring / PutAway apply at once (no glide) and are recorded.</summary>
    sealed class FakeLayout : ILayoutService
    {
        public LayoutMode Mode { get; set; } = LayoutMode.All;
        public InstrumentKind Current { get; set; } = InstrumentKind.Piano;
        public bool IsTurning => false;
        public bool FirstArrangeDone => true;
        public SeatFrame Seat => default;
        public int Sets;
        public readonly List<InstrumentKind> Members = new List<InstrumentKind> { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp };
        public readonly List<(InstrumentKind kind, BringReason reason)> Brings = new List<(InstrumentKind, BringReason)>();
        public readonly List<InstrumentKind> PutAways = new List<InstrumentKind>();
        /// <summary>Overrides PreviewJoin's answer when set.</summary>
        public JoinPreview? Join;
        public InstrumentKind? FacingKind;
        public readonly Dictionary<InstrumentKind, InstrumentPlace> Places = new Dictionary<InstrumentKind, InstrumentPlace>();
        public int Version;
        public void Set(LayoutMode mode, InstrumentKind instrument) { Sets++; Mode = mode; Current = instrument; }
        public void Next(int direction) { }
        public void ArrangeAroundPlayer(bool force, bool movePiano = true) { }
        /// <summary>Reset view (the menu row): counted.</summary>
        public int BringBacks;
        public void BringToPlayer() => BringBacks++;
        public void PlaceAroundPiano() { }
        public void RestoreHomes() { }
        public Pose Anchor => Pose.identity;
        public bool IsInScene(InstrumentKind instrument) => Mode == LayoutMode.All ? Members.Contains(instrument) : instrument == Current;
        public IReadOnlyList<InstrumentKind> MySet => Members;
        public int SetVersion => Version;
        public InstrumentPlace PlaceOf(InstrumentKind k) => Places.TryGetValue(k, out var p) ? p : IsInScene(k) ? InstrumentPlace.Seat : InstrumentPlace.Display;
        public InstrumentKind? Facing => Mode == LayoutMode.Single ? Current : FacingKind;
        public JoinPreview PreviewJoin(InstrumentKind k)
        {
            if (Join.HasValue) return Join.Value;
            if (Members.Contains(k)) return new JoinPreview(JoinAction.Already);
            return Members.Count < MySetRules.Max ? new JoinPreview(JoinAction.Add) : new JoinPreview(JoinAction.Replace, Members[0]);
        }
        public bool Bring(InstrumentKind k, BringReason reason)
        {
            Brings.Add((k, reason));
            if (Mode == LayoutMode.Single) { Current = k; Version++; return true; }
            JoinPreview p = PreviewJoin(k);
            if (p.Action == JoinAction.Add) Members.Add(k);
            else if (p.Action == JoinAction.Replace) Members[Members.IndexOf(p.Replaced)] = k;
            else return false;
            Version++;
            return true;
        }
        public bool PutAway(InstrumentKind k)
        {
            if (Mode != LayoutMode.All || Members.Count <= 1 || !Members.Remove(k)) return false;
            PutAways.Add(k);
            Version++;
            return true;
        }
    }

    sealed class FakeTutorial : ITutorial
    {
        public TutorialState State { get; set; }
        public string LessonId { get; set; }
        public InstrumentKind Instrument { get; set; }
        public int Version { get; set; }
        public float Countdown { get; set; }
        public string Hint { get; set; }
        public float Progress => EventsTotal > 0 ? EventsDone / (float)EventsTotal : 0f;
        public int EventsDone { get; set; }
        public int EventsTotal { get; set; }
        public TutorialSummary Summary { get; set; }
        public readonly List<string> Started = new List<string>();
        public int Stops;
        public string Noun { get; set; } = "notes";
        public string CountNoun => Noun;
        public void Start(string lessonId) { Started.Add(lessonId); LessonId = lessonId; State = TutorialState.Countdown; Countdown = 3f; Version++; }
        public void Stop() { Stops++; State = TutorialState.Idle; Version++; }
    }

    sealed class FakeLessons : ILessonLibrary
    {
        readonly List<Lesson> _lessons = new List<Lesson>();
        public LessonTutorialConstants Constants { get; private set; }
        public IReadOnlyList<Lesson> Lessons => _lessons;
        public Lesson Get(string id) { foreach (var l in _lessons) if (l.id == id) return l; return null; }
        public int Count(InstrumentKind k) { string id = InstrumentIds.Of(k); int n = 0; foreach (var l in _lessons) if (l.instrument == id) n++; return n; }
        public void Add(Lesson l) => _lessons.Add(l);

        /// <summary>The real lessons.json (Lessons/Resources), read with the Core contract classes.</summary>
        public static FakeLessons Load()
        {
            var ta = Resources.Load<TextAsset>("lessons");
            var f = new FakeLessons();
            if (ta == null) return f;
            var file = JsonUtility.FromJson<LessonFile>(ta.text);
            f.Constants = file.tutorial;
            f._lessons.AddRange(file.lessons);
            return f;
        }
    }

    sealed class FakeRecorder : IRecorder
    {
        public RecorderState State { get; set; }
        public bool External { get; set; }
        public List<TakeEvent> Take { get; set; }
        public double TakeSeconds { get; set; }
        public List<TakeEvent> Played;
        public int Stops;
#pragma warning disable 67
        public event Action<List<TakeEvent>, double> OnTake;
#pragma warning restore 67
        public void Add(in TakeEvent ev) { }
        public void Record() { State = RecorderState.Recording; External = false; }
        public bool Play() { State = RecorderState.Playing; External = false; return true; }
        public bool PlayEvents(List<TakeEvent> events, double seconds) { Played = events; State = RecorderState.Playing; External = true; return true; }
        public void Stop() { Stops++; State = RecorderState.Idle; External = false; }
    }

    sealed class FakeTakes : ITakeLibrary
    {
        public bool? Available { get; set; } = true;
        public int Version { get; private set; }
        public event Action Changed;
        public readonly List<TakeRecord> Records = new List<TakeRecord>();

        public TakeRecord Add(string name, long created, float seconds, int events)
        {
            var r = new TakeRecord { id = "take-" + Records.Count, name = name, created = created, seconds = seconds, events = new List<TakeEvent>() };
            for (int i = 0; i < events; i++) r.events.Add(TakeEvent.PianoOn(60 + i, 0.7f));
            Records.Add(r);
            Version++;
            Changed?.Invoke();
            return r;
        }

        public Task<List<TakeInfo>> List()
        {
            var list = new List<TakeInfo>();
            foreach (var r in Records) list.Add(new TakeInfo { id = r.id, name = r.name, created = r.created, seconds = r.seconds, count = r.events.Count });
            list.Sort((a, b) => b.created.CompareTo(a.created));
            return Task.FromResult(list);
        }
        public Task<TakeRecord> Get(string id) { foreach (var r in Records) if (r.id == id) return Task.FromResult(r); return Task.FromResult<TakeRecord>(null); }
        public Task<TakeRecord> Put(TakeRecord take) { Records.Add(take); Version++; Changed?.Invoke(); return Task.FromResult(take); }
        public Task<bool> Remove(string id)
        {
            int n = Records.RemoveAll(r => r.id == id);
            Version++;
            Changed?.Invoke();
            return Task.FromResult(n > 0);
        }
        public string FormatLength(double seconds) => MenuText.FormatLength(seconds);
        public string FormatDate(long createdMs) => "20 Oct, 14:32";
    }

    sealed class FakeGuide : IGuide
    {
        public bool Present { get; set; }
        public bool Done { get; set; }
        public int Starts, Dismisses;
        public readonly List<string> Tips = new List<string>();
        public readonly HashSet<string> Seen = new HashSet<string>();
        public void Start() { Starts++; Present = true; }
        public void Dismiss() { Dismisses++; Present = false; Done = true; }
        public void Toggle() { if (Present) Dismiss(); else Start(); }
        public float Tip(string id) { Tips.Add(id); Seen.Add(id); return 2f; }
        public bool TipSeen(string id) => Seen.Contains(id);
        public int DeckGuides, LongSongs, LoopShows;
        public void ToggleDeckGuide() => DeckGuides++;
        public void StartLongSong() => LongSongs++;
        public void StartLoopShow() => LoopShows++;
    }

    sealed class FakeMr : IMixedReality
    {
        public bool Available { get; set; } = true;
        public bool Active { get; set; }
        public bool IsPlacing => false;
        public MrStatus Status { get; set; }
        public bool HasSpaceSetup => Status == MrStatus.Loaded;
        public int Enters, Exits, Setups;
        public Task Enter() { Enters++; Active = true; return Task.CompletedTask; }
        public void Exit() { Exits++; Active = false; }
        public Task RunSpaceSetup() { Setups++; return Task.CompletedTask; }
        public bool TryGetPianoPose(out Pose pose, out bool onTable) { pose = default; onTable = false; return false; }
        public void StartPlacing() { }
    }

    /// <summary>Records what the painter draws (texts with their style; shapes counted). Text width: 0.5 em per char.</summary>
    sealed class RecordingCanvas : IMenuCanvas
    {
        public struct TextOp { public string Text; public float X, Y, Size, MaxWidth; public bool Bold; public Color Color; public TextAlign Align; public TextFit Fit; }
        public readonly List<TextOp> Texts = new List<TextOp>();
        public int Shapes, Clips, Images;
        public readonly List<Color> Fills = new List<Color>();
        /// <summary>Pretend to be a GPU canvas (the painter then draws ready portraits as images).</summary>
        public bool WithImages;
        public bool DrawsImages => WithImages;
        public void Image(Texture texture, Rect uv, float x, float y, float w, float h, float r, bool linearSource) => Images++;

        public void FillRoundRect(float x, float y, float w, float h, float r, Color c) { Shapes++; Fills.Add(c); }
        public void FillRoundRect(float x, float y, float w, float h, float r, in MenuGradient g) { Shapes++; Fills.Add(g.C0); }
        public void StrokeRoundRect(float x, float y, float w, float h, float r, float lineWidth, Color c) => Shapes++;
        public void ShadowRoundRect(float x, float y, float w, float h, float r, float blur, Color c) => Shapes++;
        public void Line(float x0, float y0, float x1, float y1, float lineWidth, Color c) => Shapes++;
        public void StrokeEllipse(float cx, float cy, float rx, float ry, float lineWidth, Color c, int half = 0) => Shapes++;
        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color col) => Shapes++;
        public void Text(string text, float x, float y, float size, bool bold, Color c, TextAlign align, float maxWidth = 0f, TextFit fit = TextFit.Ellipsis)
            => Texts.Add(new TextOp { Text = text, X = x, Y = y, Size = size, Bold = bold, Color = c, Align = align, MaxWidth = maxWidth, Fit = fit });
        public float MeasureText(string text, float size, bool bold) => (text?.Length ?? 0) * size * 0.5f;
        public void SetClip(float x0, float y0, float x1, float y1) => Clips++;
        public void ClearClip() { }

        public TextOp? Find(string text) { foreach (var t in Texts) if (t.Text == text) return t; return null; }
        public bool Has(string text) => Find(text).HasValue;
    }

    /// <summary>A controller with every fake service bound, on the given screen, shown and laid out.</summary>
    sealed class Rig
    {
        public readonly FakeHost Host = new FakeHost();
        public readonly MenuController Ctl;
        public readonly FakeLayout Layout = new FakeLayout();
        public readonly FakeTutorial Tutorial = new FakeTutorial();
        public readonly FakeLessons Lessons = FakeLessons.Load();
        public readonly FakeRecorder Recorder = new FakeRecorder();
        public readonly FakeTakes Takes = new FakeTakes();
        public readonly FakeGuide Guide = new FakeGuide();
        public readonly FakeMr Mr = new FakeMr();
        public readonly FakeArrange Arrange = new FakeArrange();
        public readonly InstrumentRegistry Registry;
        public readonly AppSettings Settings = new AppSettings(new MemorySettingsStore());
        public double Now = 10000;

        public Rig(bool withMr = true, bool withArrange = false, InstrumentRegistry registry = null)
        {
            Registry = registry ?? TestRegistry.Make();
            Ctl = new MenuController(Host)
            {
                Layout = Layout, Tutorial = Tutorial, Lessons = Lessons, Recorder = Recorder, Takes = Takes, Guide = Guide, Mr = withMr ? Mr : null,
                Arrange = withArrange ? Arrange : null, Registry = Registry, Settings = Settings,
            };
            Host.Ctl = Ctl;
            Ctl.SetShown(true);
            Ctl.Sync();
        }

        /// <summary>One frame: sync, then the given pokes (slot 1 = right index), then the hover bookkeeping.</summary>
        public void Frame(float dtMs = 16f) { Now += dtMs; Ctl.BeginPokes(Now); Ctl.EndPokes(); Ctl.Sync(); }

        public void Poke(Vector3 local, float dtMs = 16f, bool active = true, bool wasActive = true, int slot = 1)
        {
            Now += dtMs;
            Ctl.Sync();
            Ctl.BeginPokes(Now);
            Ctl.Poke(slot, active, wasActive, local);
            Ctl.EndPokes();
        }

        /// <summary>The panel-local point over row i's centre at height z above the curved face.</summary>
        public Vector3 Over(int row, float z)
        {
            var r = Ctl.Row(row);
            return MenuMath.CanvasPoint((r.X0 + r.X1) / 2f, (r.Y0 + r.Y1) / 2f, z);
        }

        /// <summary>The menu check's poke(): wait out the debounce, then 3 cm, 1.2 cm, -2 mm, +1 mm, 3 cm.</summary>
        public void Tap(int row)
        {
            Now += 260;
            Poke(Over(row, 0.03f)); Poke(Over(row, 0.012f)); Poke(Over(row, -0.002f)); Poke(Over(row, 0.001f)); Poke(Over(row, 0.03f));
            Frame();
        }

        public int RowNamed(RowKind id, Func<MenuRow, bool> pred = null)
        {
            for (int i = 0; i < Ctl.RowCount; i++) if (Ctl.Row(i).Id == id && (pred == null || pred(Ctl.Row(i)))) return i;
            return -1;
        }

        public string Labels(int from = 0)
        {
            var parts = new List<string>();
            for (int i = from; i < Ctl.RowCount; i++) parts.Add(Ctl.Row(i).Label + (Ctl.Row(i).On ? "*" : ""));
            return string.Join(",", parts);
        }

        public RecordingCanvas Paint()
        {
            var c = new RecordingCanvas();
            MenuPainter.Paint(c, Ctl, Now);
            return c;
        }
    }
}
