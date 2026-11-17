// Layout module (Order 100, after every rig owner): which instruments are in the room and where (spec 08 section 5),
// clear of the eyes and of each other (PlacementSolver, spec 07 section 8), grab-to-move with the guards that keep a
// playing pinch from ever grabbing (spec 05 section 8), the seat swivel, the two-hand summon and the page-turn sweep
// (spec 08 section 5.2). This file: the rig registry, modes and membership, the page-turn animation and the frame hooks;
// LayoutModule.Arrange.cs places, LayoutModule.Grab.cs grabs, LayoutModule.Desktop.cs drags rigs with the mouse,
// LayoutModule.Gestures.cs swivels / summons / sweeps, LayoutModule.Set.cs keeps My set, the display and the glides
// (12-new-instruments/platform.md 4), RaySummon.cs brings a displayed instrument by pointing at it.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using Unity.Profiling;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule : AppModule, IRigRegistry, ILayoutService, IGrabService, IPlacementService, IArrangeMode
    {
        public const int ModuleOrder = 100;
        public override int Order => ModuleOrder;

        /// <summary>The seat the rigs are built around before the first arrangement.</summary>
        public static readonly Vector3 OriginSeat = new Vector3(0f, 1.2f, 0f);
        /// <summary>The side table's play centre in its own frame: the tape deck (spec 07 rigDecor.sideTable.deck).</summary>
        public static readonly Vector3 TableCentreLocal = new Vector3(0.07f, 0.6f, 0f);

        App _app;
        readonly List<Rig> _rigs = new List<Rig>(8);
        public event Action Changed;

        /// <summary>The placement resolver over the placement rigs (every rig but the menu panel).</summary>
        public PlacementSolver Placement { get; } = new PlacementSolver();
        /// <summary>Grab feedback (ring, dwell ring, cage); state readable even when headless.</summary>
        public GrabVisuals Visuals { get; private set; }
        /// <summary>The page-turn sweep detector.</summary>
        public PageTurnGesture Sweep { get; } = new PageTurnGesture();

        bool _initialised;
        readonly List<Renderer> _rendererScratch = new List<Renderer>(128);

        // Per-phase profiler markers (ARCHITECTURE.md section 17: layout/grab <= 0.3 ms per frame on device).
        static readonly ProfilerMarker s_grabMarker = new ProfilerMarker("[Layout] Grab");
        static readonly ProfilerMarker s_gestureMarker = new ProfilerMarker("[Layout] Gestures");
        static readonly ProfilerMarker s_turnMarker = new ProfilerMarker("[Layout] Turn");
        static readonly ProfilerMarker s_summonMarker = new ProfilerMarker("[Layout] RaySummon");
        static readonly ProfilerMarker s_focusMarker = new ProfilerMarker("[Layout] Focus");

        /// <summary>Point and pinch at a displayed instrument to bring it (platform.md 4.4 c).</summary>
        public RaySummon Summon { get; private set; }

        public override void Init(App app)
        {
            _app = app;
            // Once per install: the spots an older build saved by itself (not the player's) are forgotten (TUNE-DECK-FRONT).
            ComfortCodec.MigrateAutoSavedSpots(app.Settings);
            // Once per install: an old save's Lock instruments and Real hands back off (they silently hid features).
            if (ComfortCodec.MigrateSettings(app.Settings) && app.Get<IHandLook>() is IHandLook look && look.RealHandsInMr) look.RealHandsInMr = false;
            _mode = app.Settings.LayoutMode;
            _current = Normalize(app.Settings.CurrentInstrument);
            LoadSet();
            LoadMoveSetting();   // "Move instruments" (and the old Lock setting removed)
            InitSet();
            InitStage();   // a recording brings its instruments (LayoutModule.Stage)
            Placement.Skip = SkipHeldTool;
            for (int i = 0; i < _homeY.Length; i++) _homeY[i] = float.NaN;
            var found = app.GetAll<Rig>();
            for (int i = 0; i < found.Count; i++) AddRig(found[i]);
            app.Register<IRigRegistry>(this);
            app.Register<ILayoutService>(this);
            app.Register<IGrabService>(this);
            app.Register<IPlacementService>(this);
            app.Register<IArrangeMode>(this);
            ReachBandOn = ReachBandDefault;
            app.Events.Subscribe<RecenteredEvent>(OnRecentered);
            app.Events.Subscribe<MixedRealityEvent>(OnMixedReality);
            app.Events.Subscribe<HmdMountEvent>(OnMount);
            app.Events.Subscribe<InputFocusEvent>(OnInputFocus);
            app.Events.Subscribe<StartChoiceEvent>(OnStartChoice);
            Visuals = new GrabVisuals(app.Materials, app.WorldRoot, !app.Headless);
            ArrangeView = new ArrangeVisuals(app.Materials, app.WorldRoot, !app.Headless);
            Summon = new RaySummon(app.Materials, app.WorldRoot, !app.Headless);
            Risers = new RiserVisuals(app.Materials, app.WorldRoot, !app.Headless);
            HookDesktopCamera();
            // Every rig stands at its home about the default seat ORIGIN, as built. That is
            // the desktop room (the app arranges only in XR); in XR the first presenting frame lays it out around the head
            // and keeps the piano's height, so the piano must start at its home height.
            ApplyMembership();
            PlaceAt(OriginSeat, 0f, true, settle: false, pianoAtHome: true);
            // A piano out of the layout still gets its home height for when it comes back.
            Rig piano = Get(RigId.Piano);
            if (piano != null && piano.Group != null && _mode == LayoutMode.Single && _current != InstrumentKind.Piano)
                piano.Group.SetPositionAndRotation(LayoutMath.SeatPlace(OriginSeat, 0f, piano.HomeAll), Quaternion.Euler(0f, piano.HomeYawAll, 0f));
            _initialised = true;
            CollectAll();
            for (int i = 0; i < Placement.Entries.Count; i++) RefreshZone(Placement.Entries[i]);
            RefreshDisplay();
            Debug.Log($"[Layout] ready: {_rigs.Count} rig(s) ({Placement.Entries.Count} placed), mode={_mode}, current={_current}, " +
                      $"set={string.Join(",", _set)}{(_plan != null && _plan.TableAlt ? " (side table behind-left)" : "")}, move={(_moveWanted ? "on" : "off")}");
        }

        public override void Shutdown()
        {
            if (_app == null) return;
            EndGrab();
            _app.Events.Unsubscribe<RecenteredEvent>(OnRecentered);
            _app.Events.Unsubscribe<MixedRealityEvent>(OnMixedReality);
            _app.Events.Unsubscribe<HmdMountEvent>(OnMount);
            _app.Events.Unsubscribe<InputFocusEvent>(OnInputFocus);
            _app.Events.Unsubscribe<StartChoiceEvent>(OnStartChoice);
            ShutdownSet();
            ShutdownStage();
            UnhookDesktopCamera();
            Visuals?.Destroy();
            ArrangeView?.Destroy();
            Summon?.Destroy();
            Risers?.Destroy();
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (_app == null) return;
            switch (phase)
            {
                case FramePhase.FirstArrange: TickFirstArrange(); TickHeight(dt); break;
                case FramePhase.Grab:
                    using (s_grabMarker.Auto())
                    {
                        if (!_arranging) TryResumeMove();   // "Move instruments" on: back once the app allows it
                        if (_arranging) { Summon.Cancel(this); TickArrange(dt); }
                        else
                        {
                            using (s_summonMarker.Auto()) Summon.Tick(this, dt);   // a summon pinch is consumed before the grab sees it
                            TickGrab();
                        }
                    }
                    break;
                // "Move instruments" owns every one-hand pinch: no swivel or page turn while it is on.
                case FramePhase.Swivel:
                    using (s_gestureMarker.Auto()) { if (_arranging) _swivelHand = null; else TickSwivel(); }
                    using (s_focusMarker.Auto()) TickFocus(dt);   // step 17: the instrument the head faces
                    break;
                // Reset view by hand works with Move instruments on or off (it never takes a pinch on or aimed at an instrument).
                case FramePhase.TwoHandSummon: using (s_gestureMarker.Auto()) TickSummon(dt); break;
                case FramePhase.PageTurn: using (s_gestureMarker.Auto()) { if (_arranging) Sweep.ResetMotion(); else TickSweep(dt); } break;
                case FramePhase.LayoutAnimation: using (s_turnMarker.Auto()) { TickTurn(dt); TickGlides(dt); TickSettle(dt); TickStage(dt); UpdateRisers(); } break;
            }
        }

        // Set on every XR session start, consumed by the first presenting frame with a pose.
        bool _wasPresenting, _xrArrangePending;

        /// <summary>The app arranges only in XR: each session start lays the room out around the
        /// head on its first presenting frame with a pose (spec 08 section 5.4). The desktop never
        /// arranges: the rigs stay at their homes about ORIGIN (0, 1.2, 0) where Init put them, with the desktop camera
        /// 10 cm behind, and FirstArrangeDone only says the room is ready. A runtime that comes up after the desktop
        /// fallback (RigBootstrap keeps polling) is a session start too, so the late headset still gets its arrangement.</summary>
        void TickFirstArrange()
        {
            if (_app.Head == null) return;
            bool presenting = Presenting;
            if (presenting && !_wasPresenting) _xrArrangePending = true;   // renderer.xr 'sessionstart'
            _wasPresenting = presenting;
            if (presenting)
            {
                if (_xrArrangePending && _app.Head.position.y > 0.2f)
                {
                    _xrArrangePending = false;
                    ArrangeAroundPlayer(true);
                    XrArrangements++;
                }
                return;
            }
            _xrArrangePending = false;   // 'sessionend'
            if (FirstArrangeDone || _app.Rig.State != XrState.Desktop) return;
            // The app always boots in the full room; a single-instrument layout restored from the settings is a
            // layout Set(...) on the desktop, which arranges about the desktop camera (no fitSeat: not presenting).
            if (_mode == LayoutMode.Single) ArrangeAroundPlayer(true);
            FirstArrangeDone = true;
        }

        /// <summary>Arrangements made by an XR session start, for tests.</summary>
        public int XrArrangements { get; private set; }

        /// <summary>The system recentred (Reset view, or the headset settling after it came back on: these come in bursts of
        /// two or three a second or two apart): one reset view about the tracking origin the system set, never about the
        /// head's pose in that moment, so a head turning between the events moves nothing. While the headset is away (off,
        /// the system menu, tracking lost) or settling after it, the head pose is stale or untracked (the 2026-11-11 log:
        /// recentres at 06:37:06 before tracking came back, 06:38:42 during a tracking loss, 06:46:19 with the headset off,
        /// laid the room out about a pose the head was not in): it waits for the return (LayoutModule.Height) and runs once.</summary>
        void OnRecentered(RecenteredEvent e)
        {
            if (AwayOrSettling)
            {
                if (!_recentrePending) Debug.Log("[Layout] recentre while the headset is away or settling: applied once it is back");
                _recentrePending = true;
                DeferredRecentres++;
                return;
            }
            BringBack(BringBackSource.Recentre);
        }

        /// <summary>The start panel's answer: "Play here" lays the room out in front of the player as they sit now (the first
        /// presenting frame arranged it while they were still looking at the panel); "Play in my room" is arranged by MR.</summary>
        void OnStartChoice(StartChoiceEvent e)
        {
            _startAnswered = true;   // a saved "Move instruments: on" may take effect now (TryResumeMove)
            if (e.InMyRoom || !Presenting || _app.Head == null || MrActive) return;
            ArrangeAroundPlayer(true);
        }

        // Entering or leaving mixed reality puts the piano back at its home height on the next arrangement (the MR
        // auto-arrange / RestoreHomes); the MR module calls ArrangeAroundPlayer itself. Only a CHANGE of Active arms it:
        // MR republishes Active = true after every scene load (so the menu re-reads Status), and a re-arrange then must
        // not drop a table-placed piano back to stand height (integration, MR CONTRACT-REQUESTS 2b).
        void OnMixedReality(MixedRealityEvent e)
        {
            if (e.Active == _mrWasActive) return;
            _mrWasActive = e.Active;
            LeaveStage("mixed reality " + (e.Active ? "on" : "off"));   // another room: the playback's layout is the player's now
            _pianoHomeHeight = true;
            // Arrange mode ends with the room it was started in; what was placed in one room is never saved for the other.
            if (_arranging) StopMoving(save: false);   // the setting stays: it comes back on in the new room
        }

        void OnMount(HmdMountEvent e)
        {
            _mounted = e.Mounted;
            if (e.Mounted) return;
            GoAway("headset off");
            if (_arranging) StopMoving(save: true);   // the headset came off; the setting comes back on with it
            EndGrab();   // session end: the grab ends and the swivel lets go
            _swivelHand = null;
        }

        /// <summary>The system menu took input focus: the hands are the system's now, so a grab lets go.</summary>
        void OnInputFocus(InputFocusEvent e)
        {
            _focused = e.Focused;
            if (e.Focused) return;
            GoAway("system menu");
            EndGrab();
            _swivelHand = null;
        }

        // ---------------------------------------------------------------- services the layout talks to

        IMenuPanel Menu => _app.Get<IMenuPanel>();
        IMixedReality Mr => _app.Get<IMixedReality>();
        IPianoApi PianoApi => _app.Get<IPianoApi>();
        IDrumsApi DrumsApi => _app.Get<IDrumsApi>();
        IHarpApi HarpApi => _app.Get<IHarpApi>();
        bool MrActive { get { var mr = Mr; return mr != null && mr.Active; } }
        bool MrPlacing { get { var mr = Mr; return mr != null && mr.Active && mr.IsPlacing; } }
        internal App AppRef => _app;
        internal bool MrIsActive => MrActive;
        internal bool MrIsPlacing => MrPlacing;

        /// <summary>Whether an XR session is presenting (ARCHITECTURE.md section 3.3: App.IsPresenting): grab, swivel, summon,
        /// page turn, MR placing, fitSeat and the first arrangement run only then; the desktop Shift / Alt drag only when not.</summary>
        bool Presenting => _app.IsPresenting;

        /// <summary>A stick in a player's hand never counts toward its rig's footprint (the placement skip list). The Drums
        /// module reparents a held stick out of the kit's group, which PlacementSolver.Shows(r, group) already leaves out;
        /// this also covers a held tool that is a Component left under its rig.</summary>
        bool SkipHeldTool(Renderer r)
        {
            HandState[] hands = _app.Input.Hands;
            for (int i = 0; i < hands.Length; i++)
                if (hands[i].Holding is Component c && c != null && r.transform.IsChildOf(c.transform)) return true;
            return false;
        }

        // ---------------------------------------------------------------- IRigRegistry

        public IReadOnlyList<Rig> Rigs => _rigs;

        public Rig Get(RigId id)
        {
            for (int i = 0; i < _rigs.Count; i++) if (_rigs[i].Id == id) return _rigs[i];
            return null;
        }

        public void Register(Rig rig)
        {
            if (!AddRig(rig)) return;
            if (_initialised)
            {
                FinishMotion();   // membership mid-turn would take the outgoing rig out while it slides
                ApplyMembership();
                RigEntry e = Placement.Entry(rig);
                if (e != null) RefreshZone(e);
                RefreshDisplay();
            }
            Changed?.Invoke();
        }

        public void Unregister(Rig rig)
        {
            if (rig == null || !_rigs.Remove(rig)) return;
            if (_held == rig) EndGrab();
            int gi = (int)rig.Id;
            if (gi >= 0 && gi < _glides.Length && _glides[gi] != null && _glides[gi].Rig == rig && _glides[gi].Active)
            {
                _glides[gi].Clear();
                _glidesActive = Mathf.Max(0, _glidesActive - 1);
            }
            RigEntry e = Placement.Entry(rig);
            if (e != null) Placement.Entries.Remove(e);
            Changed?.Invoke();
        }

        bool AddRig(Rig rig)
        {
            if (rig == null || _rigs.Contains(rig)) return false;
            _rigs.Add(rig);
            // The menu panel is not a placement rig: the solver clears it out of the instruments separately.
            if (rig.Id != RigId.Lessons && Placement.Entry(rig) == null)
            {
                var e = new RigEntry(rig);
                Placement.Entries.Add(e);
                if (_initialised) Collect(e);
            }
            return true;
        }

        /// <summary>A playable instrument's rig (the registry; the side table and the menu are not).</summary>
        bool IsRowRig(RigId id) { InstrumentDescriptor d = _app?.Registry?.Get(KindOf(id)); return d != null && d.Playable; }

        /// <summary>The id block's rule: RigId value = InstrumentKind value.</summary>
        static RigId RigOf(InstrumentKind k) => InstrumentIds.RigOf(k);

        static InstrumentKind KindOf(RigId id) => InstrumentIds.KindOf(id);

        /// <summary>A playable, loaded registry instrument, else the first loaded one in the carousel (the piano).</summary>
        InstrumentKind Normalize(InstrumentKind k)
        {
            IInstrumentRegistry reg = _app?.Registry;
            if (reg == null) return k;
            InstrumentDescriptor d = reg.Get(k);
            if (d != null && d.Playable && reg.IsLoaded(k)) return k;
            for (int i = 0; i < reg.Carousel.Count; i++) if (reg.IsLoaded(reg.Carousel[i])) return reg.Carousel[i];
            return k;
        }

        /// <summary>My set (platform.md 4.2): the at-seat members of the full layout, in planner order.</summary>
        readonly List<InstrumentKind> _set = new List<InstrumentKind>(MySetRules.Max);
        public IReadOnlyList<InstrumentKind> MySet => _set;
        public int SetVersion { get; private set; }

        /// <summary>musicroom.set: registry ids, unknown or unloaded ones dropped, at most MySetRules.Max; empty -> the first
        /// loaded classic member.</summary>
        void LoadSet()
        {
            _set.Clear();
            string ids = _app.Settings.SetIds;
            IInstrumentRegistry reg = _app.Registry;
            foreach (string raw in (ids ?? "").Split(','))
            {
                string id = raw.Trim();
                if (reg == null || !reg.TryParse(id, out InstrumentKind k)) continue;
                InstrumentDescriptor d = reg.Get(k);
                if (d == null || !d.Playable || !reg.IsLoaded(k) || _set.Contains(k) || _set.Count >= MySetRules.Max) continue;
                _set.Add(k);
            }
            if (_set.Count == 0 && reg != null && reg.Carousel.Count > 0) _set.Add(Normalize(reg.Carousel[0]));   // the first loaded one
        }

        // ---------------------------------------------------------------- ILayoutService: state

        LayoutMode _mode;
        InstrumentKind _current;
        Vector3 _seatHead = OriginSeat;
        float _seatYaw;          // radians, Unity heading
        bool _pianoHomeHeight, _mrWasActive;

        public LayoutMode Mode => _mode;
        public InstrumentKind Current => _current;
        public bool FirstArrangeDone { get; private set; }
        public SeatFrame Seat => new SeatFrame { Head = _seatHead, YawDeg = _seatYaw * Mathf.Rad2Deg };
        /// <summary>Page turns started since boot.</summary>
        public int Turns { get; private set; }

        /// <summary>The instrument is part of the current layout (the table and the menu always are).</summary>
        public bool IsInScene(InstrumentKind instrument)
        {
            if (instrument == InstrumentKind.Table || instrument == InstrumentKind.Menu) return true;
            return _mode == LayoutMode.All ? _set.Contains(instrument) : instrument == _current;
        }

        /// <summary>Finishes a turn in progress, applies membership, re-arranges around the
        /// player and raises LayoutChangedEvent when anything changed.</summary>
        public void Set(LayoutMode mode, InstrumentKind instrument)
        {
            mode = mode == LayoutMode.Single ? LayoutMode.Single : LayoutMode.All;
            InstrumentDescriptor want = _app.Registry?.Get(instrument);
            instrument = want != null && want.Playable && _app.Registry.IsLoaded(instrument) ? instrument : _current;
            FinishMotion();
            // The player chose a layout during a playback that brought its instruments: theirs from now on, with their own
            // My set (not the recording's).
            bool ownSet = LeaveStage("a layout was chosen", ownSet: true);
            bool same = mode == _mode && instrument == _current && !ownSet;
            bool toSingle = mode == LayoutMode.Single && _mode != LayoutMode.Single;
            // Re-placed at once around the player (fitSeat included), as today; an instrument that changes place between
            // the seat and its display spot in the VR room travels there (RigGlide), every other rig is placed at once.
            Animate(BringReason.Menu, () =>
            {
                if (ownSet) TakeOwnSet();
                _mode = mode;
                _current = instrument;
                Persist();
                ApplyMembership();
                // About the player's front (the last recentre or Reset view), not where the head looks while choosing on
                // the menu or the deck (2026-11-17: "make it appear at one place in front of eyes only").
                ArrangeAt(true, true, SeatSource.Front);
            }, false);
            if (!same) PublishLayout();
            if (toSingle) Signal(InstrumentKind.Table, SignalSingle);
        }

        /// <summary>Single mode only, no turn running; the page turns to ROW[i +- 1].</summary>
        public void Next(int direction)
        {
            if (_mode != LayoutMode.Single || IsTurning || direction == 0) return;
            InstrumentKind to = _app.Registry != null ? _app.Registry.Next(_current, direction > 0 ? 1 : -1) : _current;
            if (to == _current) return;
            LeaveStage("page turn");
            TurnTo(to, direction > 0 ? 1 : -1, BringReason.PageTurn);
            Signal(to, BringSignal(BringReason.PageTurn));
        }

        void Persist()
        {
            if (_staged) return;   // a playback's layout is never saved (LayoutModule.Stage)
            _app.Settings.LayoutMode = _mode;
            _app.Settings.CurrentInstrument = _current;
            _app.Settings.Save();
        }

        void PublishLayout() => _app.Events.Publish(new LayoutChangedEvent { Mode = _mode, Current = _current, SetVersion = SetVersion });

        /// <summary>The placed instruments in the scene and the lists, the others out. An instrument that
        /// leaves has its tools released (IHandToolProvider.ReleaseAll; the kit's sticks as today) and is let go if held. Out
        /// of the layout, an instrument with a display configuration stands on display in the VR room (rig active, not
        /// present, IStowable.SetStowed(true, mount)); one without, and every one in mixed reality, is hidden as today. A
        /// rig that glides is left to its glide. A lesson whose instrument left is stopped, but not while it glides in.</summary>
        void ApplyMembership()
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id)) { r.InScene = true; continue; }
                InstrumentKind kind = KindOf(r.Id);
                if (GlideOf(kind) != null) { r.InScene = false; continue; }
                bool keep = IsInScene(kind);
                if (!keep && r.InScene)
                {
                    ReleaseTools(r);
                    if (_held == r) EndGrab();
                }
                r.InScene = keep;
                if (r.Group != null)
                {
                    if (keep)
                    {
                        if (!r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
                        Unstow(kind);
                    }
                    else if (Displayable(r)) ShowDisplayed(r);
                    else if (r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(false);
                }
                if (!keep)
                {
                    RigEntry e = Placement.Entry(r);
                    if (e != null) e.HasZone = false;   // pianoZone.makeEmpty()
                }
            }
            var tutorial = _app.Get<ITutorial>();
            if (tutorial != null && tutorial.State != TutorialState.Idle && tutorial.LessonId != null && !IsInScene(tutorial.Instrument)
                && !IsMoving(tutorial.Instrument)) tutorial.Stop();
        }

        // ---------------------------------------------------------------- the page turn (spec 08 section 5.3)

        bool _turning;
        float _turnT;
        int _turnDir;
        InstrumentKind _turnFrom, _turnTo;
        Rig _turnIn, _turnOut;
        Vector3 _restIn, _restOut, _turnRight;

        /// <summary>A page turn runs: today's slide, or the glide of One alone in the VR room.</summary>
        public bool IsTurning => _turning || _glideTurn;
        /// <summary>0..1 through the running turn (eased), for instruments that fade.</summary>
        public float TurnProgress => _turning ? LayoutMath.Ease(Mathf.Clamp01(_turnT / GrabConstants.TurnSeconds)) : 0f;
        /// <summary>The opacity the turn gives a rig's materials right now (1 outside a turn).</summary>
        public float FadeOf(Rig rig)
        {
            if (!_turning || rig == null) return 1f;
            float e = TurnProgress;
            if (rig == _turnOut) return 1f - e;
            if (rig == _turnIn) return e;
            return 1f;
        }
        public Vector3 TurnRestIn => _restIn;
        public Vector3 TurnRestOut => _restOut;
        public Vector3 TurnRight => _turnRight;

        void StartTurn(InstrumentKind from, InstrumentKind to, int dir)
        {
            Rig rin = Get(RigOf(to)), rout = Get(RigOf(from));
            if (rin == null || rin.Group == null)
            {
                // Nothing to slide in (its module is not loaded): change membership at once.
                _current = to;
                Persist();
                ApplyMembership();
                RefreshDisplay();
                PublishLayout();
                return;
            }
            _current = to;
            Persist();
            // The incoming instrument's resting pose is about the player's front (the last recentre or Reset view, turned
            // with a swivel), never the head's look at this moment; both slide across that heading. The side table and the
            // menu stay where they are; a piano on a real table (MR) keeps its surface height.
            ReadFront(out Vector3 head, out float yaw);
            _seatHead = head;
            _seatYaw = yaw;
            Transform gin = rin.Group;
            if (!gin.gameObject.activeSelf) gin.gameObject.SetActive(true);
            gin.localScale = Vector3.one;
            // Its single home in front of the player (pulled into the reach band) with the player's comfort offset.
            if (TrySaved(rin, out SeatPose saved)) PoseSaved(rin, saved, head, yaw);
            else
            {
                SingleHome(rin, out Vector3 single);
                float y = to == InstrumentKind.Piano && PianoOnSurface() ? gin.position.y : DefaultY(rin, rin.HomeSingle.y);
                Vector3 rest = LayoutMath.SeatPlace(head, yaw, single);
                rest.y = y;
                gin.SetPositionAndRotation(rest, Quaternion.Euler(0f, rin.HomeYawSingle + yaw * Mathf.Rad2Deg, 0f));
            }
            // Where it comes to rest is settled before it slides in: clear of the eyes and of what stays (the side table,
            // the menu), the outgoing instrument left out (settled about head and yaw, skipping it). Like
            // settlePlacement, the zones of the rigs it moved (the side table) are brought up to date.
            CollectAll();
            rin.InScene = true;
            Settle(head, yaw, null, rout != null ? Placement.Entry(rout) : null);
            // The incoming group is only added to the scene: its instrument and rig join the lists in the finished
            // turn's Apply(), so while it slides it is shown but never played, hovered, grabbed or
            // counted as "near an instrument". Rig.InScene is what the instruments' IsPresent and the grab read.
            rin.InScene = false;
            RigEntry ein = Placement.Entry(rin);
            if (ein != null) ein.HasZone = false;
            _restIn = gin.position;
            _restOut = rout != null && rout.Group != null ? rout.Group.position : Vector3.zero;
            _turnRight = LayoutMath.SeatRight(yaw);
            _turnIn = rin;
            _turnOut = rout != rin ? rout : null;
            _turnFrom = from;
            _turnTo = to;
            _turnDir = dir;
            _turnT = 0f;
            _turning = true;
            Turns++;
            StepTurn(0f);
            // Spec 06 section 9: a band-passed noise swish, then a brighter tail 40 ms later (IAudioEngine.Swish).
            _app.Get<IAudioEngine>()?.Swish();
            _app.Events.Publish(new TurnEvent { Started = true, Direction = dir, From = from, To = to });
            if (_turnOut != null) Move(from, MovePhase.Leaving, _turnReason);
            Move(to, MovePhase.Leaving, _turnReason);
        }

        void StepTurn(float k)
        {
            float e = LayoutMath.Ease(k);
            float slide = GrabConstants.TurnSlide;
            float shrink = 1f - GrabConstants.TurnScale;   // 0.15
            if (_turnOut != null && _turnOut.Group != null)
            {
                _turnOut.Group.position = _restOut - _turnRight * (_turnDir * slide * e);
                _turnOut.Group.localScale = Vector3.one * (1f - shrink * e);
            }
            _turnIn.Group.position = _restIn + _turnRight * (_turnDir * slide * (1f - e));
            _turnIn.Group.localScale = Vector3.one * (GrabConstants.TurnScale + shrink * e);
        }

        void TickTurn(float dt)
        {
            if (!_turning) return;
            _turnT += dt;
            if (_turnT >= GrabConstants.TurnSeconds) { FinishTurn(); return; }
            StepTurn(_turnT / GrabConstants.TurnSeconds);
        }

        /// <summary>Snap to the rest poses, take the outgoing instrument out, raise the events.</summary>
        void FinishTurn()
        {
            if (!_turning) return;
            _turning = false;
            if (_turnOut != null && _turnOut.Group != null)
            {
                _turnOut.Group.localScale = Vector3.one;
                _turnOut.Group.position = _restOut;
            }
            _turnIn.Group.localScale = Vector3.one;
            _turnIn.Group.position = _restIn;
            _current = _turnTo;
            bool hadOut = _turnOut != null;
            ApplyMembership();
            RigEntry e = Placement.Entry(_turnIn);
            if (e != null) RefreshZone(e);
            _turnIn = _turnOut = null;
            RefreshDisplay();
            _app.Events.Publish(new TurnEvent { Started = false, Direction = _turnDir, From = _turnFrom, To = _turnTo });
            if (hadOut) Move(_turnFrom, MovePhase.Displayed, _turnReason);
            Move(_turnTo, MovePhase.Arrived, _turnReason);
            _turnReason = BringReason.PageTurn;
            PublishLayout();
        }

        /// <summary>The piano sits on a real table (MR): mixed reality active and the stand hidden
        /// (IPianoApi.OnSurface).</summary>
        bool PianoOnSurface()
        {
            if (!MrActive) return false;
            IPianoApi api = PianoApi;
            return api != null && api.OnSurface;
        }
    }
}
