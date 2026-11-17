// The palm menu (spec 08 section 1, ARCHITECTURE.md sections 9 and 15.6). Order 60.
// Registers IMenuPanel, the panel as an IInstrument (Kind Menu, TickOrder 50: the last instrument, so its pokes and
// summon run in the Instruments step and are skipped while its rig is held) and its Rig (Lessons).
// Frame: Instruments step -> summon gesture, then the open/close animation, then (shown) pokes (index tips) and the
// desktop mouse, in that order (so whatever freezes the menu's tick, a held panel or MR
// placing, freezes its fade too); Hover -> one glow on the face; EndFrame -> ring, relayout when a
// service changed, repaint the canvas only when dirty, hover highlight uniforms on the face material.
// Desktop model (ARCHITECTURE.md section 0): the mouse hand never summons, pokes or glows (HandState.IsMouse /
// Pointer.FromMouse); while !App.IsPresenting a left click whose nearest Layers.PlaySurface hit is the face is the
// menu's (desktopDown), and M toggles the panel.
// Since the first headset test the same canvas, rows and pokes also make three more things:
//   - the start panel (the landing page): at launch the panel opens in front of the player (not at the palm) with
//     "VR Music Room", "Play here" and, where passthrough and the scene work, "Play in my room"; the answer closes it and
//     Sunny's first-launch tour may begin (IMenuPanel.StartPending);
//   - the Done / Reset buttons of "Move instruments" (IArrangeMode): a second MenuView at half size with its own
//     controller, floating 35 cm ahead of the eyes, below them and to the left, poked with either index finger;
//   - Exit, the home page's last row: poke, "Tap again to exit" for 3 s, poke again -> the sound fades out over 150 ms,
//     the settings are saved and App.Quit closes the app (tests replace it through BootOptions.Quit).
// The many-instrument platform (12-new-instruments/platform.md 5): the controller reads App.Registry; the module owns the
// portrait studio (Core/Render/PortraitStudio: once the first arrangement is done, one portrait per frame while the
// start panel is up, else from the first time a tile grid is shown; the grid repaints once when they are in); it
// publishes the menu's MenuRowEvent, InstrumentSignalEvent and InstrumentOptionEvent for the controller; it answers
// Sunny's anchors "menu.tile.<id>" / "menu.option.<id>.<Key>" (an IGuideAnchors of Kind Table); and it opens the panel
// (and moves the lesson screen) to the panel home of the instrument the player faces when that instrument has one
// (SeatSpec.PanelHome; none today), which is also the menu rig's home for Layout.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace MusicRoom.Menu
{
    public sealed partial class MenuModule : AppModule, IMenuPanel, IInstrument, IMenuHost, IContactSurface
    {
        public const int ModuleOrder = 60;
        public const int MenuTickOrder = 50;
        const string RedrawSample = "[Menu] redraw";
        public override int Order => ModuleOrder;

        /// <summary>The start panel: this far ahead of the eyes and this much below them.</summary>
        public const float StartDistance = 0.55f, StartDrop = 0.06f;

        App _app;
        Transform _root;
        MenuView _view;
        MenuController _ctl;
        readonly SummonGesture _gesture = new SummonGesture();
        /// <summary>Rig.Handles: the back plate and the face, filled once in Init (ARCHITECTURE.md section 8). Layout measures
        /// only what is shown (enabled and active in the hierarchy), so the hidden panel has no footprint.</summary>
        readonly List<Renderer> _handles = new List<Renderer>(2);
        static readonly List<Renderer> s_played = new List<Renderer>(0);
        Zone _zone;
        float _animK;
        bool _closing, _tickedThisFrame, _presenting;

        // The Done / Reset buttons of arrange mode.
        Transform _barRoot;
        MenuView _barView;
        MenuController _bar;
        float _barAnimK;
        bool _barClosing;

        // The start panel.
        bool _startPending, _startShowing;

        // Exit.
        bool _quitting, _quitDone;
        float _quitT, _volume0 = 1f;

        // lazily resolved (Layout initialises at Order 100, MR at 70)
        IPlacementService _placement;
        IGrabService _grab;
        IPianoApi _piano;

        // event handlers created once (no per-publish allocation)
        System.Action<TutorialEvent> _onTutorial;
        System.Action<RecorderStateEvent> _onRecorder;
        System.Action<GuidePresenceEvent> _onGuide;
        System.Action<LayoutChangedEvent> _onLayout;
        System.Action<LibraryChangedEvent> _onLibrary;
        System.Action<MixedRealityEvent> _onMr;
        System.Action<HmdMountEvent> _onMount;
        System.Action<ArrangeModeEvent> _onArrange;
        System.Action<InstrumentMoveEvent> _onMove;

        // The portraits (platform.md 5.2) and Sunny's menu anchors.
        PortraitStudio _studio;
        int _studioPainted;
        Anchors _anchors;
        const string PortraitSample = "[Menu] portrait";

        /// <summary>The panel's logic (tests, tools).</summary>
        public MenuController Controller => _ctl;
        /// <summary>The panel's scene objects (renderers exist even headless; the canvas only with a GPU).</summary>
        public MenuView View => _view;
        public SummonGesture Gesture => _gesture;
        /// <summary>The painted 1024 x 768 canvas (null without a GPU or when headless).</summary>
        public RenderTexture FaceTexture => _view?.FaceTexture;
        public float AnimK => _animK;
        public bool Closing => _closing;
        /// <summary>Canvas repaints so far (tests: idle frames must not repaint).</summary>
        public int Paints { get; private set; }

        /// <summary>The Done / Reset buttons (their controller, scene objects and root) and their repaints.</summary>
        public MenuController ArrangeButtons => _bar;
        public MenuView ArrangeView => _barView;
        public Transform ArrangeRoot => _barRoot;
        public bool ArrangeButtonsShown => _bar != null && _bar.Shown;
        public int ArrangePaints { get; private set; }
        /// <summary>The start panel is open (it is the panel's Start screen, in front of the player).</summary>
        public bool StartShowing => _startShowing && _ctl != null && _ctl.Shown;
        /// <summary>Exit: the sound is fading out (then App.Quit); and whether App.Quit was called.</summary>
        public bool Quitting => _quitting;
        public bool QuitCalled => _quitDone;

        sealed class Zone : IPlayZone
        {
            readonly MenuModule _m;
            public Zone(MenuModule m) { _m = m; }
            public bool Contains(Vector3 world) { var z = _m.GrabZone; return z.HasValue && z.Value.Contains(world); }
        }

        /// <summary>The Done / Reset buttons' host: they never publish MenuEvents (they are not the palm panel).</summary>
        sealed class BarHost : IMenuHost
        {
            readonly MenuModule _m;
            public BarHost(MenuModule m) { _m = m; }
            public void Hide() { }
            public void ScreenChanged(MenuScreen screen) { }
            public void RowPressed(in MenuRowEvent e) => _m._app.Events.Publish(e);
        }

        /// <summary>Sunny's demo anchors on the panel (platform.md 8.4): "menu.tile.&lt;id&gt;" (the tile's portrait centre)
        /// while the Instruments grid is shown, "menu.option.&lt;id&gt;.&lt;Key&gt;" (the row's centre) while that options page is
        /// shown; up = the face's normal toward the player. Kind Table: the menu is nobody's instrument.</summary>
        sealed class Anchors : IGuideAnchors
        {
            readonly MenuModule _m;
            public Anchors(MenuModule m) { _m = m; }
            public InstrumentKind Kind => InstrumentKind.Table;
            public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
            {
                world = default;
                up = Vector3.up;
                MenuController ctl = _m._ctl;
                if (ctl == null || _m._root == null || !ctl.TryGetAnchorRow(anchor, out MenuRow r)) return false;
                bool tile = r.Id == RowKind.Tile;
                if (tile ? ctl.Screen != MenuScreen.Instruments || ctl.OnOptions : !ctl.OnOptions) return false;
                float cx = (r.X0 + r.X1) * 0.5f, cy = tile ? r.Y0 + MenuStyle.PortraitH * 0.5f : r.CentreY;
                MenuMath.FromCanvas(cx, cy, out float u, out float y);
                world = _m._root.TransformPoint(MenuMath.SurfacePoint(u, y, 0f));
                up = _m._root.TransformDirection(MenuMath.SurfaceNormal(u));
                return true;
            }
        }

        /// <summary>The portrait studio (null until the first arrangement is done).</summary>
        public PortraitStudio Portraits => _studio;

        public override void Init(App app)
        {
            _app = app;
            var go = new GameObject("MenuPanel");
            go.transform.SetParent(app.WorldRoot, false);
            _root = go.transform;
            bool paint = !app.Headless && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            _view = new MenuView(app, _root, paint);
            _handles.Add(_view.Back);
            _handles.Add(_view.Face);
            _view.SetCollider(false);

            _ctl = new MenuController(this)
            {
                Tutorial = app.Get<ITutorial>(),
                Lessons = app.Get<ILessonLibrary>(),
                Recorder = app.Get<IRecorder>(),
                Takes = app.Get<ITakeLibrary>(),
                Guide = app.Get<IGuide>(),
                Sound = app.Get<MusicRoom.Audio.IAudioEngine>(),
                Registry = app.Registry,
                Settings = app.Settings,
            };
            _piano = app.Get<IPianoApi>();

            var bar = new GameObject("ArrangeButtons");
            bar.transform.SetParent(app.WorldRoot, false);
            _barRoot = bar.transform;
            _barView = new MenuView(app, _barRoot, paint, MenuStyle.ArrangeScale, withRing: false);
            _barView.SetCollider(false);
            _bar = new MenuController(new BarHost(this));
            _bar.ResetScreen(MenuScreen.Arrange);
            ResolveLate();

            _zone = new Zone(this);
            Rig = new Rig
            {
                Id = RigId.Lessons, Group = _root, Zone = _zone, Priority = 10,
                HomeAll = MenuConstants.HomeSeat, HomeYawAll = 0f, HomeSingle = MenuConstants.HomeSeat, HomeYawSingle = 0f,
                Handles = _handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IMenuPanel>(this);
            app.Register<IInstrument>(this);
            app.Register<IContactSurface>(this);   // 10-upgrade/experience.md E1 / E3: the face stops the poking finger
            app.Register<IGazeMenu>(this);         // look and pinch (MenuModule.Gaze.cs)
            _anchors = new Anchors(this);
            app.Register<IGuideAnchors>(_anchors);
            app.Register<IMenuGuideHooks>(this);   // first five: Sunny points at rows and lights them

            _onTutorial = e => _ctl.ObserveTutorial(e.State, e.LessonId);
            _onRecorder = e => _ctl.Changed();
            _onGuide = e => _ctl.Changed();
            _onLayout = e => _ctl.Changed();
            _onLibrary = e => _ctl.RefreshTakes();
            _onMr = e => _ctl.Changed();
            _onMount = e => { if (!e.Mounted) Hide(); };
            _onArrange = OnArrange;
            _onMove = e => { if (_ctl.Screen == MenuScreen.Instruments) _ctl.Changed(); };
            app.Events.Subscribe(_onTutorial);
            app.Events.Subscribe(_onRecorder);
            app.Events.Subscribe(_onGuide);
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onLibrary);
            app.Events.Subscribe(_onMr);
            app.Events.Subscribe(_onMount);
            app.Events.Subscribe(_onArrange);
            app.Events.Subscribe(_onMove);
            // (A desktop press on the face never turns the desktop camera: the face collider is on Layers.PlaySurface,
            // which AppRoot puts in DesktopRigFallback.dragBlockMask. Nothing to wire here.)
            _presenting = app.IsPresenting;
            _startPending = app.StartPanel;
            Debug.Log($"[Menu] ready: canvas={(paint ? "gpu" : "off")}, lessons={(_ctl.Lessons != null ? _ctl.Lessons.Lessons.Count : 0)}, takes={(_ctl.Takes == null ? "none" : _ctl.Takes.Available == false ? "off" : "on")}, start panel={_startPending}");
        }

        public override void Shutdown()
        {
            if (_app == null) return;
            _app.Events.Unsubscribe(_onTutorial);
            _app.Events.Unsubscribe(_onRecorder);
            _app.Events.Unsubscribe(_onGuide);
            _app.Events.Unsubscribe(_onLayout);
            _app.Events.Unsubscribe(_onLibrary);
            _app.Events.Unsubscribe(_onMr);
            _app.Events.Unsubscribe(_onMount);
            _app.Events.Unsubscribe(_onArrange);
            _app.Events.Unsubscribe(_onMove);
            if (_quitting) AudioListener.volume = _volume0;   // tests and the editor go on after a quit request
            _view?.Dispose();
            _barView?.Dispose();
            _studio?.Dispose();
            _studio = null;
        }

        /// <summary>Services of modules initialised after the menu: resolved on first use, then cached.</summary>
        void ResolveLate()
        {
            if (_ctl.Layout == null) _ctl.Layout = _app.Get<ILayoutService>();
            if (_ctl.Mr == null) _ctl.Mr = _app.Get<IMixedReality>();
            if (_ctl.Hands == null) _ctl.Hands = _app.Get<IHandLook>();
            if (_ctl.Arrange == null) _ctl.Arrange = _bar.Arrange = _app.Get<IArrangeMode>();
            if (_placement == null) _placement = _app.Get<IPlacementService>();
            if (_piano == null) _piano = _app.Get<IPianoApi>();
            if (_ctl.Tutorial == null) _ctl.Tutorial = _app.Get<ITutorial>();
            if (_ctl.Guide == null) _ctl.Guide = _app.Get<IGuide>();
            ResolveFirstFive();   // first five (MenuModule.FirstFive.cs)
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.InputDone)
            {
                if (_ctl.Layout == null || _ctl.Mr == null || _ctl.Arrange == null || _placement == null || _ctl.Tour == null || _ctl.Progress == null) ResolveLate();
                // The panel is put away when an XR session starts or ends.
                bool presenting = _app.IsPresenting;
                if (presenting != _presenting) { _presenting = presenting; Hide(); }
                // The desktop path (no XR session presenting): along the look direction. The start panel
                // waits for its answer.
                if (!presenting && DesktopInput.KeyDown(DKey.M) && !StartShowing) Toggle();
            }
            else if (phase == FramePhase.Instruments)
            {
                // MR placing skips every instrument, the menu too: the summon gesture still runs, and firing it ends the
                // placing (the hidden rigs and the menu come back) and opens the menu, so the palm always gets out.
                if (!_tickedThisFrame && _ctl.Mr != null && _ctl.Mr.IsPlacing && _app.IsPresenting) PlacingSummon(dt);
            }
            else if (phase == FramePhase.EndFrame)
            {
                // Not ticked this frame (the panel is held, or MR is placing the piano: Core skips the menu): no ring.
                if (!_tickedThisFrame) _gesture.Pause();
                _tickedThisFrame = false;
                _view.SetRing(_gesture.RingVisible, _gesture.RingPosition, _gesture.RingRotation, _gesture.RingScale, _gesture.RingOpacity);
                // The start panel opens once the room is ready (and again if something put it away unanswered).
                if (_startPending && !_ctl.Shown && StartReady()) ShowStart();
                // The Done / Reset buttons follow arrange mode (the event shows them; this catches a missed one).
                bool arranging = _ctl.Arrange != null && _ctl.Arrange.Arranging;
                if (arranging != _bar.Shown) { if (arranging) ShowBar(); else HideBar(); }
                UpdatePanelHome();
                StepPortraits();
                _ctl.Sync();
                if (_ctl.Shown && _view.CanPaint && _view.TexturesLost) _ctl.Dirty = true;
                if (_ctl.Dirty && _ctl.Shown && _view.CanPaint && _view.VisualActive)
                {
                    // At most one repaint per frame, only when what the canvas shows changed (ARCHITECTURE.md 15.6).
                    Profiler.BeginSample(RedrawSample);
                    _ctl.Dirty = false;
                    _view.Paint(_ctl, _app.Clock.NowMs);
                    Paints++;
                    Profiler.EndSample();
                }
                // Hover highlights and their 120 ms fades: uniforms of the face material, every frame, no repaint.
                _view.SetHover(_ctl, _app.Clock.NowMs);
                _bar.Sync();
                if (_bar.Shown && _barView.CanPaint && _barView.TexturesLost) _bar.Dirty = true;
                if (_bar.Dirty && _bar.Shown && _barView.CanPaint && _barView.VisualActive)
                {
                    Profiler.BeginSample(RedrawSample);
                    _bar.Dirty = false;
                    _barView.Paint(_bar, _app.Clock.NowMs);
                    ArrangePaints++;
                    Profiler.EndSample();
                }
                _barView.SetHover(_bar, _app.Clock.NowMs);
                TickQuit(dt);
            }
        }

        /// <summary>animate(dt): animK toward its target by dt/0.15; scale 0.9 + 0.1k and opacity k with k = easeOut;
        /// a closed panel leaves the scene.</summary>
        void Animate(float dt)
        {
            float target = _ctl.Shown ? 1f : 0f;
            if (_animK == target && !_closing) return;
            float step = dt / MenuStyle.OpenSeconds;
            _animK = target > 0f ? Mathf.Min(1f, _animK + step) : Mathf.Max(0f, _animK - step);
            float k = MenuMath.EaseOut(_animK);
            _view.Apply(0.9f + 0.1f * k, k);
            if (!_ctl.Shown && _animK <= 0f)
            {
                _closing = false;
                _view.SetVisible(false);
                _view.Apply(1f, 0f);
            }
        }

        /// <summary>The Done / Reset buttons open and close with the panel's own animation.</summary>
        void AnimateBar(float dt)
        {
            float target = _bar.Shown ? 1f : 0f;
            if (_barAnimK == target && !_barClosing) return;
            float step = dt / MenuStyle.OpenSeconds;
            _barAnimK = target > 0f ? Mathf.Min(1f, _barAnimK + step) : Mathf.Max(0f, _barAnimK - step);
            float k = MenuMath.EaseOut(_barAnimK);
            _barView.Apply(0.9f + 0.1f * k, k);
            if (!_bar.Shown && _barAnimK <= 0f)
            {
                _barClosing = false;
                _barView.SetVisible(false);
                _barView.Apply(1f, 0f);
            }
        }

        // ---------------------------------------------------------------- IMenuPanel

        public bool Shown => _ctl != null && _ctl.Shown;
        public MenuScreen Screen => _ctl != null ? _ctl.Screen : MenuScreen.Home;
        public Rig Rig { get; private set; }
        public Transform Root => _root;
        /// <summary>The start panel has not been answered yet: Sunny's first-launch tour waits.</summary>
        public bool StartPending => _startPending;

        /// <summary>World AABB of the face and back plate (at scale 1) plus 0.12 m while shown; null while hidden.</summary>
        public Bounds? GrabZone => Shown ? MenuMath.GrabZone(_root.localToWorldMatrix) : (Bounds?)null;

        /// <summary>Show(hand): in front of the summoning hand (or the look direction), then the clear-out
        /// and MenuEvent{Open}. Showing a shown panel only moves it. While the start panel is up it stays the start panel.</summary>
        public void Show(HandState hand = null)
        {
            if (_startShowing) PlaceStart();
            else
            {
                // The start panel was answered: the palm menu opens on its home page (silently: it is still hidden).
                if (_ctl.Screen == MenuScreen.Start) _ctl.ResetScreen(MenuScreen.Home);
                // The instrument the player faces may keep the panel out of its playing space (SeatSpec.PanelHome).
                Vector3? home = PanelHome();
                if (!home.HasValue || !PlaceAtHome(home.Value)) PlaceFor(hand);
            }
            Open();
        }

        void Open()
        {
            _closing = false;
            if (!_ctl.Shown)
            {
                _ctl.SetShown(true);
                _view.SetVisible(true);
                _view.SetCollider(true);
                float k = MenuMath.EaseOut(_animK);
                _view.Apply(_animK > 0f ? 0.9f + 0.1f * k : 1f, k);
                if (_placement == null) _placement = _app.Get<IPlacementService>();
                _placement?.ClearPanel(Rig);
                Physics.SyncTransforms();   // the desktop mouse ray must find the face where it now is
                _app.Events.Publish(new MenuEvent { Change = MenuChange.Open, Screen = _ctl.Screen });
            }
            _ctl.Dirty = true;
        }

        /// <summary>hide(): at once for everything that matters (zone, collider, hover, pokes); the face then fades out
        /// over 150 ms and leaves the scene. An unanswered start panel comes back when the room is ready again.</summary>
        public void Hide()
        {
            if (!_ctl.Shown) return;
            _startShowing = false;
            _ctl.SetShown(false);
            _closing = true;
            _view.SetCollider(false);
            _app.Events.Publish(new MenuEvent { Change = MenuChange.Close, Screen = _ctl.Screen });
        }

        public void Toggle(HandState hand = null)
        {
            if (_ctl.Shown) Hide();
            else Show(hand);
        }

        public void Go(MenuScreen screen) => _ctl.Go(screen);

        public void Redraw() => _ctl.Changed();

        void IMenuHost.ScreenChanged(MenuScreen screen) =>
            _app.Events.Publish(new MenuEvent { Change = MenuChange.Screen, Screen = screen });

        void PlaceFor(HandState hand)
        {
            Transform head = _app.Head;
            if (head == null) return;
            bool hasPalm = false;
            Vector3 c = default;
            if (hand != null) hasPalm = _app.Input.TryPalm(hand, out c, out _);
            Transform piano = PianoRoot();
            MenuMath.PlaceFor(head.position, head.forward, hasPalm, c, piano, out Vector3 pos, out Quaternion rot);
            _root.SetPositionAndRotation(pos, rot);
        }

        // ---------------------------------------------------------------- the panel home (SeatSpec.PanelHome)

        /// <summary>The seat-frame panel home now: the running lesson's instrument's, else the faced instrument's (One alone:
        /// the current one), when its descriptor has one; null: MenuConstants.HomeSeat and the palm placement.</summary>
        public Vector3? PanelHome()
        {
            IInstrumentRegistry reg = _app.Registry;
            if (reg == null) return null;
            ITutorial tut = _ctl.Tutorial;
            if (tut != null && tut.State != TutorialState.Idle)
            {
                Vector3? lesson = reg.Get(tut.Instrument)?.Seat?.PanelHome;
                if (lesson.HasValue) return lesson;
            }
            ILayoutService layout = _ctl.Layout;
            if (layout == null) return null;
            InstrumentKind? k = layout.Mode == LayoutMode.Single ? layout.Current : layout.Facing;
            return k.HasValue ? reg.Get(k.Value)?.Seat?.PanelHome : null;
        }

        /// <summary>The menu rig's home for Layout follows the panel home (Layout poses the hidden panel there).</summary>
        void UpdatePanelHome()
        {
            if (Rig == null) return;
            Vector3 home = PanelHome() ?? MenuConstants.HomeSeat;
            if (home == Rig.HomeAll && home == Rig.HomeSingle) return;
            Rig.HomeAll = home;
            Rig.HomeSingle = home;
        }

        /// <summary>The panel at a seat-frame home, upright, facing the head (SeatSpec.PanelHome).</summary>
        bool PlaceAtHome(Vector3 home)
        {
            Transform head = _app.Head;
            if (head == null) return false;
            ILayoutService layout = _ctl.Layout;
            SeatFrame seat = layout != null ? layout.Seat : default;
            if (seat.Head == Vector3.zero) seat = SeatFrame.From(head);
            Vector3 pos = seat.Place(home);
            _root.SetPositionAndRotation(pos, MenuMath.FacingFrom(head.position, pos));
            return true;
        }

        void IMenuHost.LessonShown(InstrumentKind kind)
        {
            if (!_ctl.Shown || _startShowing) return;
            Vector3? home = _app.Registry?.Get(kind)?.Seat?.PanelHome;
            if (!home.HasValue || !PlaceAtHome(home.Value)) return;
            Physics.SyncTransforms();   // the desktop ray and the grab zone see the panel where it now is
        }

        // ---------------------------------------------------------------- the menu's events

        void IMenuHost.RowPressed(in MenuRowEvent e) => _app.Events.Publish(e);

        void IMenuHost.Signal(string signal) =>
            _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Table, Signal = signal });

        void IMenuHost.OptionChanged(InstrumentKind kind, string key, int value) =>
            _app.Events.Publish(new InstrumentOptionEvent { Kind = kind, Key = key, Value = value });

        // ---------------------------------------------------------------- the portraits (platform.md 5.2)

        /// <summary>Made once the first arrangement is done (the rigs are where they will be shown); one portrait per frame
        /// while the start panel waits, else once a tile grid was shown. When nothing is left to render the grid repaints
        /// once with the portraits in (until then the tiles show the descriptors' line drawings).</summary>
        void StepPortraits()
        {
            if (_studio == null)
            {
                ILayoutService layout = _ctl.Layout;
                if (layout == null || !layout.FirstArrangeDone) return;
                _studio = new PortraitStudio(_app, MenuStyle.PhotoPaper);
                _ctl.Portraits = _studio;
                _studioPainted = _studio.Version;
            }
            if (!_studio.Available || !(_startPending || _ctl.GridSeen)) return;
            Profiler.BeginSample(PortraitSample);
            bool rendered = _studio.Step();
            Profiler.EndSample();
            if (rendered || _studio.Version == _studioPainted) return;
            _studioPainted = _studio.Version;
            if (_ctl.Screen == MenuScreen.Instruments || _ctl.Screen == MenuScreen.Lessons) _ctl.Dirty = true;
        }

        Transform PianoRoot()
        {
            if (_piano == null) _piano = _app.Get<IPianoApi>();
            return _piano != null && _piano.Root != null && _piano.Root.gameObject.activeInHierarchy ? _piano.Root : null;
        }

        // ---------------------------------------------------------------- the start panel

        /// <summary>The room is ready around the player: the rig is up (XR or the desktop camera), the headset is on, the
        /// first arrangement is done and the head has a real pose.</summary>
        bool StartReady()
        {
            RigBootstrap rig = _app.Rig;
            if (rig == null || rig.State == XrState.Starting || !rig.HmdMounted) return false;
            ILayoutService layout = _ctl.Layout;
            if (layout != null && !layout.FirstArrangeDone) return false;
            IMixedReality mr = _ctl.Mr;
            if (mr != null && mr.IsPlacing) return false;
            Transform head = _app.Head;
            return head != null && head.position.y > 0.2f;
        }

        void ShowStart()
        {
            _startShowing = true;
            if (_ctl.Screen != MenuScreen.Start) _ctl.ResetScreen(MenuScreen.Start);
            PlaceStart();
            Open();
            Debug.Log($"[Menu] start panel (Play in my room {(_ctl.RoomAvailable ? "offered" : "not available")})");
        }

        /// <summary>The start panel in front of the eyes along the look direction (not at a palm), nearly at eye level; over
        /// the keyboard it stands just in front of the key fronts (MenuMath.PlaceFor's piano rule).</summary>
        void PlaceStart()
        {
            Transform head = _app.Head;
            if (head == null) return;
            Transform piano = PianoRoot();
            Vector3 f = head.forward;
            f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            // Along the look direction at StartDistance (the palm path's own placement, with its piano rule, when the
            // keyboard is in the way), nearly at eye level.
            MenuMath.PlaceFor(head.position, f, false, default, piano, out Vector3 pos, out _);
            Vector3 at = head.position + f * StartDistance;
            if ((new Vector2(pos.x - head.position.x, pos.z - head.position.z)).magnitude >= MenuMath.OpenDistance - 1e-3f) pos = at;
            pos.y = head.position.y - StartDrop;
            _root.SetPositionAndRotation(pos, MenuMath.FacingFrom(head.position, pos));
        }

        void IMenuHost.StartChosen(bool mixedReality)
        {
            if (!_startPending) return;
            _startPending = false;
            Debug.Log("[Menu] start: " + (mixedReality ? "Play in my room" : "Play here"));
            PublishStartChoice(mixedReality);   // first five
            Hide();
            _ctl.ResetScreen(MenuScreen.Home);   // hidden now: no repaint, no screen event
        }

        // ---------------------------------------------------------------- the Done / Reset buttons

        void OnArrange(ArrangeModeEvent e)
        {
            if (e.Active)
            {
                Hide();       // the room is clear to arrange
                ShowBar();
            }
            else HideBar();
            _ctl.Changed();
        }

        void ShowBar()
        {
            PlaceBar();
            if (!_bar.Shown)
            {
                _bar.SetShown(true);
                _barClosing = false;
                _barView.SetVisible(true);
                _barView.SetCollider(true);
                float k = MenuMath.EaseOut(_barAnimK);
                _barView.Apply(_barAnimK > 0f ? 0.9f + 0.1f * k : 1f, k);
                Physics.SyncTransforms();
            }
            _bar.Changed();
        }

        void HideBar()
        {
            if (!_bar.Shown) return;
            _bar.SetShown(false);
            _barClosing = true;
            _barView.SetCollider(false);
        }

        /// <summary>Within easy seated reach: 35 cm ahead of the eyes along the look direction, 15 cm below them, 10 cm to
        /// the left, turned to face the eyes (ArrangeConstants).</summary>
        void PlaceBar()
        {
            Transform head = _app.Head;
            if (head == null) return;
            Vector3 f = head.forward;
            f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 eye = head.position;
            Vector3 pos = eye + f * ArrangeConstants.ButtonsAhead - right * ArrangeConstants.ButtonsLeft - Vector3.up * ArrangeConstants.ButtonsBelow;
            _barRoot.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - eye, Vector3.up));
        }

        void BarPokes(IReadOnlyList<Pointer> pointers, double now)
        {
            _bar.Sync();
            _bar.BeginPokes(now);
            for (int i = 0; i < pointers.Count; i++)
            {
                Pointer p = pointers[i];
                if (p.External || p.FromMouse || p.Joint != (int)HandJoint.IndexTip || !(p.Owner is HandState h)) continue;
                _bar.Poke(h.Index, p.Active, p.WasActive, MenuMath.ScaledToPanel(_barRoot.InverseTransformPoint(p.Tip), MenuStyle.ArrangeScale));
            }
            if (!_app.IsPresenting) MousePokes(_bar, _barView);
            _bar.EndPokes();
        }

        // ---------------------------------------------------------------- Exit

        void IMenuHost.RequestQuit()
        {
            if (_quitting) return;
            _quitting = true;
            _quitT = 0f;
            _volume0 = AudioListener.volume;
            Debug.Log("[Menu] exit: fading out, then quitting");
        }

        /// <summary>The 150 ms fade of the whole mix (AudioListener.volume), then the settings are saved and App.Quit runs.</summary>
        void TickQuit(float dt)
        {
            if (!_quitting || _quitDone) return;
            _quitT += dt;
            AudioListener.volume = _volume0 * Mathf.Clamp01(1f - _quitT / MenuStyle.QuitFadeSeconds);
            if (_quitT < MenuStyle.QuitFadeSeconds) return;
            _quitDone = true;
            AudioListener.volume = 0f;
            _app.Settings.Save();
            _app.Quit();
        }

        // ---------------------------------------------------------------- IInstrument

        public InstrumentKind Kind => InstrumentKind.Menu;
        public int TickOrder => MenuTickOrder;
        public bool AcceptsStickPointers => false;
        /// <summary>Always: the summon gesture must run while the panel is hidden (the panel ticks every frame).</summary>
        public bool IsPresent => true;
        Transform IInstrument.Root => _root;
        /// <summary>None: a pinch on the panel is a grab of the panel (its GrabZone beats every rig), never play.</summary>
        public PlaySurface PlaySurfaceAt(Vector3 world) => PlaySurface.None;
        public IReadOnlyList<Renderer> PlayedParts => s_played;

        /// <summary>The summon gesture while MR is placing (Core ticks no instrument then): a fired gesture cancels the
        /// placing (IMixedReality.CancelPlacing) and shows the menu in front of the summoning hand.</summary>
        void PlacingSummon(float dt)
        {
            _tickedThisFrame = true;   // the ring shows while it charges
            PointerInput input = _app.Input;
            double now = _app.Clock.NowMs;
            SummonSample l = SummonSample.From(input, input.Hands[0], now);
            SummonSample r = SummonSample.From(input, input.Hands[1], now);
            int fired = _gesture.Step(l, r, _app.Head.position, dt);
            if (fired < 0) return;
            _ctl.Mr.CancelPlacing("menu summon");
            if (!_ctl.Shown) Show(input.Hands[fired]);
        }

        /// <summary>update(pointers, dt): the summon gesture first, then the animation, then (shown) the pokes of each
        /// hand's index tip. The Done / Reset buttons poke before the panel's own Shown guard.</summary>
        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            _tickedThisFrame = true;
            double now = _app.Clock.NowMs;
            // updateGesture: the mouse hand never summons (only a tracked hand does); M is the desktop's summon. While the
            // start panel waits for its answer the gesture does nothing (the panel is already in front of the player).
            PointerInput input = _app.Input;
            SummonSample l = SummonSample.From(input, input.Hands[0], now);
            SummonSample r = SummonSample.From(input, input.Hands[1], now);
            int fired = _gesture.Step(l, r, _app.Head.position, dt);
            if (fired >= 0 && !StartShowing) Toggle(input.Hands[fired]);
            // animate(dt) sits between the gesture and the Shown guard (ARCHITECTURE.md 15.6):
            // a toggle this frame starts its fade at once, and a hidden panel still fades out.
            Animate(dt);
            AnimateBar(dt);
            if (_bar.Shown) BarPokes(pointers, now);
            if (!_ctl.Shown) return;
            _ctl.Sync();
            _ctl.BeginPokes(now);
            GazeFirePending();   // look and pinch (MenuModule.Gaze.cs): a pinch on a gazed row fires as a poke would
            // Each hand's index tip (Pointers[0]); the mouse hand's pointers never poke (the desktop click path below
            // owns the mouse).
            for (int i = 0; i < pointers.Count; i++)
            {
                Pointer p = pointers[i];
                if (p.External || p.FromMouse || p.Joint != (int)HandJoint.IndexTip || !(p.Owner is HandState h)) continue;
                _ctl.Poke(h.Index, p.Active, p.WasActive, _root.InverseTransformPoint(p.Tip));
            }
            if (!_app.IsPresenting) MousePokes(_ctl, _view);
            GazeHoverPending(now);   // look and pinch: the gazed row lights unless a fingertip hovers one
            _ctl.EndPokes();
        }

        /// <summary>The desktop click path (spec 08 section 1.4, desktopDown, ARCHITECTURE.md section 0):
        /// the nearest Layers.PlaySurface hit under the mouse; only when it is this face, canvas px from its uv. A press
        /// with Shift / Alt belongs to Layout's rig drag, never to the panel.</summary>
        void MousePokes(MenuController ctl, MenuView view)
        {
            Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
            if (cam == null || !view.Collider.enabled) return;
            Ray ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            bool hit = Physics.Raycast(ray, out RaycastHit rh, cam.farClipPlane, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)
                && rh.collider == view.Collider;
            float px = 0f, py = 0f;
            if (hit)
            {
                px = rh.textureCoord.x * MenuConstants.CanvasW;
                py = (1f - rh.textureCoord.y) * MenuConstants.CanvasH;
            }
            bool down = DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt;
            ctl.Mouse(hit, px, py, down, DesktopInput.Mouse(0));
        }

        // ---------------------------------------------------------------- IContactSurface (10-upgrade/experience.md 2.1)

        /// <summary>The panel face under a fingertip while the panel is shown: inside the 0.32 x 0.24 m face on the R = 0.8 m
        /// cylinder, normal toward the player, Use Clamp (the drawn finger stops on the face: poke limiting; the pokes keep
        /// reading the raw tip). No contact shadow: the opaque face draws after the shadows (queue 2999 over 2991), so it
        /// could never be seen, yet it halved the hover glow under the fingertip. False while hidden or held.</summary>
        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (BarContact(world, above, below, ref hit)) return true;   // first five: the Done / Reset buttons stop the finger too
            if (_ctl == null || !_ctl.Shown || _root == null) return false;
            // The panel's matrices and grab state once per app frame (Clock.NowMs is frame-constant).
            if (_app.Clock.NowMs != _contactStamp)
            {
                _contactStamp = _app.Clock.NowMs;
                if (_grab == null) _grab = _app.Get<IGrabService>();
                _contactHeld = _grab != null && Rig != null && ReferenceEquals(_grab.HeldRig, Rig);
                _contactW2L = _root.worldToLocalMatrix;
                _contactL2W = _root.localToWorldMatrix;
            }
            if (_contactHeld) return false;
            MenuMath.ToSurface(_contactW2L.MultiplyPoint3x4(world), out float u, out float y, out float z);
            if (Mathf.Abs(u) > MenuMath.W / 2f || Mathf.Abs(y) > MenuMath.H / 2f || z > above || z < -below) return false;
            Vector3 p = _contactL2W.MultiplyPoint3x4(MenuMath.SurfacePoint(u, y, 0f));
            Vector3 n = _contactL2W.MultiplyVector(MenuMath.SurfaceNormal(u)).normalized;
            float h = Vector3.Dot(n, world - p);
            hit.Point = world - n * h;
            hit.Normal = n;
            hit.Height = h;
            hit.Use = ContactUse.Clamp;
            return true;
        }

        double _contactStamp = double.NaN;
        bool _contactHeld;
        Matrix4x4 _contactW2L, _contactL2W;

        /// <summary>hover(): one glow, 7 mm out of the face under the first index tip within -5..10 mm of it (the palm
        /// panel's, else the Done / Reset buttons').</summary>
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            if (glow == null) return;
            if (_ctl.Shown && _ctl.HoverRow >= 0 && HoverGlow(pointers, glow, _root, 1f)) return;
            if (_bar.Shown && _bar.HoverRow >= 0) HoverGlow(pointers, glow, _barRoot, MenuStyle.ArrangeScale);
        }

        static bool HoverGlow(IReadOnlyList<Pointer> pointers, HoverGlowPool glow, Transform root, float scale)
        {
            for (int i = 0; i < pointers.Count; i++)
            {
                Pointer p = pointers[i];
                if (!p.Active || p.External || p.FromMouse || p.Joint != (int)HandJoint.IndexTip) continue;
                Vector3 local = MenuMath.ScaledToPanel(root.InverseTransformPoint(p.Tip), scale);
                MenuMath.ToSurface(local, out float u, out float y, out float z);
                if (z <= MenuStyle.GlowFrom || z >= MenuConstants.Hover || Mathf.Abs(u) > MenuMath.W / 2f || Mathf.Abs(y) > MenuMath.H / 2f) continue;
                Vector3 pos = root.TransformPoint(MenuMath.SurfacePoint(u * scale, y * scale, MenuStyle.GlowOut));
                Vector3 normal = root.TransformDirection(MenuMath.SurfaceNormal(u));
                glow.Add(HoverGlowSpec.At(pos, normal, MenuStyle.GlowSize * scale, MenuStyle.GlowSize * scale, 1f - Mathf.Max(0f, z) / MenuConstants.Hover));
                return true;
            }
            return false;
        }
    }
}
