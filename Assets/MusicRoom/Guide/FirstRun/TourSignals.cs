// What Sunny's tips watch (Docs/11-first-five/sunny-tour.md 7.6): the events of every module and a few polled
// states, kept as counters, timers and this frame's edges, so TourTipRunner can ask "has this tip's trigger fired?" and
// "did the player just do what it teaches?" for every tip each frame. Event hooks and Update allocate nothing.
using System;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed class TourSignals
    {
        public const float GazeDegrees = 25f, GazeSeconds = 1.5f, NeverOpenedIdle = 20f, MenuNoPoke = 6f, MenuLong = 25f;
        public const float SubScreenQuiet = 4f, HeadTurnDeg = 50f, HeadTurnSeconds = 3f, FarHead = 1.1f, FarSeat = 0.6f, FarSeconds = 3f;
        public const float CymbalRing = 2f, HarpRing = 3f, SweepWindow = 0.8f, TurnFollow = 5f, ArrangeNoGrabSeconds = 8f, ArrangeFar = 0.8f;
        public const float MrSettle = 2f, MrLong = 90f;
        /// <summary>tip_more_instruments: this long after the tour with no instrument brought (one on display in the room).</summary>
        public const float MoreAfter = 180f;
        /// <summary>tip_move_quiet: with Move instruments on and nothing held, a fingertip rests on a play surface this long.</summary>
        public const float MovePlaySeconds = 1f;
        /// <summary>The row id prefix of an Instruments tile's "Options" chip (Menu's MenuText.OptionsRowId).</summary>
        public const string OptionsChipRows = "options.*";

        static readonly string[] s_screens = Enum.GetNames(typeof(MenuScreen));   // compared without a ToString per frame
        readonly App _app;
        readonly Func<string, bool> _seen;
        readonly Func<string, float> _seenAt;
        public float Time { get; private set; }

        // ---- this frame's edges (TourSuccess) ----
        readonly bool[] _edge = new bool[64];
        string _rowThisFrame;
        public bool Edge(TourSuccess s) => _edge[(int)s];
        public string RowThisFrame => _rowThisFrame;

        // ---- counters and states ----
        public float LastLivePlay { get; private set; } = float.NegativeInfinity;
        public float LastMenuPoke { get; private set; } = float.NegativeInfinity;
        public float LastScreenAt { get; private set; } = float.NegativeInfinity;
        public int DrumHits, Nods, HarpPlucks, AmbienceFlips, Recordings, TakesSaved, TakesPlayed, HatToggles, HatClosedHits;
        public int MenuOpens, LoopSaves, Turns, CymbalLeftRinging, SweepsLeftRinging, LessonsDone;
        public bool PalmOpenedMenu, StickEver, PanelMovedEver, DeckGuideEver, SingleSeen, MiddleCHeard;
        public bool LoopLayerDoneSeen, ArrangeSeen, GrabSeen, ArrangeReleaseSeen, ArrangeEndedSeen;
        public bool ClearArmedSeen, MrPlacingSeen, LessonStartedSeen, HarpLessonSeen, SubScreenSeen, ListScrollSeen, MrNoPermission, MrNoRooms;
        public int MaxLoopLayers, RowConfirmCount;
        /// <summary>Instruments brought to the seat by the player (the menu's grid, pointing, the page turn).</summary>
        public int Brings;
        public string LastConfirmRow;
        public float TourEndedAt = float.NegativeInfinity;
        public bool TourOver;
        public bool FirstRunRunning;
        /// <summary>The menu was opened by Sunny, not the palm (the next Open only).</summary>
        public bool MenuOpenedBySunny;
        /// <summary>Sunny itself is opening, turning or closing the menu (the first run's demos): no success counts.</summary>
        public bool MenuBySunny;

        float _menuOpenFor, _sinceMenuPoke, _gazeKit, _gazeHarp, _gazeSwitches, _gazeDeck, _turnedAway, _farFor, _arrangeFor;
        float _screenFor, _mrFor, _cymbalAt = -1f, _sweepAt = -1f;
        float _fingerWindowT = float.NegativeInfinity; int _fingerHits;
        float _seatYaw; bool _seatInit;
        bool _stickHeld, _anyHeld, _arranging, _placing, _metronome, _deckGuide, _menuShown, _mrActive, _mrMenuOpened;
        bool _arrangeGrab;
        /// <summary>An instrument's body was pinched with Move instruments off, which moves nothing (tip_move_turn_on).</summary>
        public bool BodyPinchSeen;
        float _movePlayFor;
        int _bpm, _bars;
        float _heldYaw0;
        Rig _held;
        RecorderState _rec;
        MenuScreen _screen;
        float _scroll;
        readonly int[] _sweepStr = new int[8];
        readonly float[] _sweepT = new float[8];
        int _sweepRing;
        public string MrEnteredAs;   // "table" | "floor" once settled after entering

        readonly Action<RecorderEvent> _onPlay;
        readonly Action<NodEvent> _onNod;
        readonly Action<MenuEvent> _onMenu;
        readonly Action<MenuRowEvent> _onRow;
        readonly Action<AmbienceEvent> _onAmbience;
        readonly Action<RecorderStateEvent> _onRec;
        readonly Action<LoopEvent> _onLoop;
        readonly Action<LayoutChangedEvent> _onLayout;
        readonly Action<TurnEvent> _onTurn;
        readonly Action<ArrangedEvent> _onArranged;
        readonly Action<ArrangeModeEvent> _onArrange;
        readonly Action<TutorialEvent> _onTutorial;
        readonly Action<TutorialHitEvent> _onHit;
        readonly Action<MixedRealityEvent> _onMr;
        readonly Action<InstrumentMoveEvent> _onMove;
        readonly Action<InstrumentBodyPinchEvent> _onBodyPinch;

        public TourSignals(App app, Func<string, bool> seen, Func<string, float> seenAt)
        {
            _app = app;
            _seen = seen;
            _seenAt = seenAt;
            for (int i = 0; i < _sweepStr.Length; i++) _sweepStr[i] = -1;
            _onPlay = OnPlay; _onNod = _ => { Nods++; _edge[(int)TourSuccess.Nod] = true; };
            _onMenu = OnMenu; _onRow = OnRow;
            _onAmbience = _ => { AmbienceFlips++; _edge[(int)TourSuccess.Ambience] = true; };
            _onRec = OnRec; _onLoop = OnLoop;
            _onLayout = e => { if (e.Mode == LayoutMode.Single) SingleSeen = true; };
            _onTurn = e => { if (e.Started) { Turns++; _edge[(int)TourSuccess.PageTurn] = true; } };
            _onArranged = _ => _edge[(int)TourSuccess.Summoned] = true;
            _onArrange = OnArrange;
            _onTutorial = OnTutorial;
            _onHit = OnHit;
            _onMr = e => { if (e.Active) { _mrFor = 0f; MrEnteredAs = null; } };
            _onMove = e =>
            {
                if (e.Phase != MovePhase.Arrived) return;
                if (e.Reason != BringReason.Menu && e.Reason != BringReason.Ray && e.Reason != BringReason.PageTurn) return;
                Brings++;
                _edge[(int)TourSuccess.Brought] = true;
            };
            _onBodyPinch = _ => BodyPinchSeen = true;
            var ev = app.Events;
            ev.Subscribe(_onBodyPinch);
            ev.Subscribe(_onPlay); ev.Subscribe(_onNod); ev.Subscribe(_onMenu); ev.Subscribe(_onRow); ev.Subscribe(_onAmbience);
            ev.Subscribe(_onRec); ev.Subscribe(_onLoop); ev.Subscribe(_onLayout); ev.Subscribe(_onTurn); ev.Subscribe(_onArranged);
            ev.Subscribe(_onArrange); ev.Subscribe(_onTutorial); ev.Subscribe(_onHit); ev.Subscribe(_onMr); ev.Subscribe(_onMove);
        }

        public void Dispose()
        {
            var ev = _app.Events;
            ev.Unsubscribe(_onBodyPinch);
            ev.Unsubscribe(_onPlay); ev.Unsubscribe(_onNod); ev.Unsubscribe(_onMenu); ev.Unsubscribe(_onRow); ev.Unsubscribe(_onAmbience);
            ev.Unsubscribe(_onRec); ev.Unsubscribe(_onLoop); ev.Unsubscribe(_onLayout); ev.Unsubscribe(_onTurn); ev.Unsubscribe(_onArranged);
            ev.Unsubscribe(_onArrange); ev.Unsubscribe(_onTutorial); ev.Unsubscribe(_onHit); ev.Unsubscribe(_onMr); ev.Unsubscribe(_onMove);
        }

        /// <summary>Called after the runner read this frame's edges.</summary>
        public void ClearEdges()
        {
            Array.Clear(_edge, 0, _edge.Length);
            _rowThisFrame = null;
        }

        bool LessonRunning
        {
            get
            {
                ITutorial t = _app.Get<ITutorial>();
                return t != null && (t.State == TutorialState.Countdown || t.State == TutorialState.Playing || t.State == TutorialState.Waiting);
            }
        }

        // ---------------- the events ----------------

        void OnPlay(RecorderEvent e)
        {
            if (e.DuringPlayback) return;
            IRoomMoments room = _app.Get<IRoomMoments>();
            if (room != null && room.Chording) return;
            string t = e.Event.type;
            bool lesson = LessonRunning || FirstRunRunning;
            if (t == TakeEventType.PianoOn || t == TakeEventType.Drum || t == TakeEventType.Harp) LastLivePlay = Time;
            if (t == TakeEventType.PianoOn)
            {
                if (e.Event.midi == 60 && !lesson) MiddleCHeard = true;
            }
            else if (t == TakeEventType.Drum)
            {
                string k = e.Event.kind;
                if (k == "kick")
                {
                    IDrumsApi d = _app.Get<IDrumsApi>();
                    if (!_app.IsPresenting || d == null || d.EveryHandPointerFartherThan(GuideTiming.PadFar, d.KickPadId)) { Nods++; _edge[(int)TourSuccess.Nod] = true; }
                    return;
                }
                DrumHits++;
                _edge[(int)TourSuccess.DrumHit] = true;
                if (k == "hat" && !e.Event.open) HatClosedHits++;
                if (k == "crash" || k == "ride") _cymbalAt = Time;
                IDrumsApi drums = _app.Get<IDrumsApi>();
                if (drums != null && !drums.AnyStickHeld)
                {
                    if (Time - _fingerWindowT > 20f) { _fingerWindowT = Time; _fingerHits = 0; }
                    _fingerHits++;
                }
            }
            else if (t == TakeEventType.DrumChoke) { _edge[(int)TourSuccess.Choke] = true; _cymbalAt = -1f; }
            else if (t == TakeEventType.Hat) { HatToggles++; _edge[(int)TourSuccess.HatToggled] = true; }
            else if (t == TakeEventType.Harp)
            {
                if (!lesson) HarpPlucks++;
                Sweep(e.Event.i);
            }
            else if (t == TakeEventType.HarpDamp) { _edge[(int)TourSuccess.HarpDamp] = true; _sweepAt = -1f; }
        }

        void Sweep(int s)
        {
            _sweepRing = (_sweepRing + 1) % _sweepT.Length;
            _sweepStr[_sweepRing] = s;
            _sweepT[_sweepRing] = Time;
            int distinct = 0;
            for (int i = 0; i < _sweepT.Length; i++)
            {
                if (_sweepStr[i] < 0 || Time - _sweepT[i] > SweepWindow) continue;
                bool dup = false;
                for (int j = 0; j < i; j++) if (_sweepStr[j] == _sweepStr[i] && Time - _sweepT[j] <= SweepWindow) { dup = true; break; }
                if (!dup) distinct++;
            }
            if (distinct >= 3) _edge[(int)TourSuccess.HarpSweep] = true;
            if (distinct >= 6) _sweepAt = Time;
        }

        void OnMenu(MenuEvent e)
        {
            if (e.Screen == MenuScreen.Start || e.Screen == MenuScreen.Arrange) return;   // the start panel is not the palm menu
            if (e.Change == MenuChange.Open)
            {
                bool palm = !MenuOpenedBySunny && !MenuBySunny;
                if (palm) MenuOpens++;   // the player's openings (Sunny's tour opens it too)
                if (palm) PalmOpenedMenu = true;
                MenuOpenedBySunny = false;
                _menuOpenFor = 0f;
                _sinceMenuPoke = 0f;
                if (_mrActive) _mrMenuOpened = true;
                if (palm) _edge[(int)TourSuccess.MenuOpened] = true;   // Sunny opening it is not the player's palm
            }
            else if (e.Change == MenuChange.Close) { if (!MenuBySunny) _edge[(int)TourSuccess.MenuClosed] = true; }
            else
            {
                if (!MenuBySunny) _edge[(int)TourSuccess.MenuScreenChanged] = true;
                LastScreenAt = Time;
                _screenFor = 0f;
            }
        }

        void OnRow(MenuRowEvent e)
        {
            LastMenuPoke = Time;
            _sinceMenuPoke = 0f;
            _rowThisFrame = e.RowId;
            _edge[(int)TourSuccess.Row] = true;
            if (e.RowId == "back") _edge[(int)TourSuccess.Back] = true;
            if (e.Confirming) { LastConfirmRow = e.RowId; RowConfirmCount++; }
        }

        void OnRec(RecorderStateEvent e)
        {
            ILooper l = _app.Get<ILooper>();
            bool loop = l != null && l.Status != LoopStatus.Off;
            RecorderState was = _rec;
            _rec = e.State;
            if (loop) return;
            if (e.State == RecorderState.Recording) { Recordings++; _edge[(int)TourSuccess.Recording] = true; }
            else if (e.State == RecorderState.Playing) { TakesPlayed++; _edge[(int)TourSuccess.TakePlaying] = true; }
            else if (was == RecorderState.Recording)
            {
                _edge[(int)TourSuccess.TakeStopped] = true;
                IRecorder r = _app.Get<IRecorder>();
                if (r?.Take != null && r.Take.Count > 0) TakesSaved++;
            }
        }

        void OnLoop(LoopEvent e)
        {
            switch (e.Phase)
            {
                case LoopEventPhase.Recording: if (e.Layer >= 1) _edge[(int)TourSuccess.LayerRecording] = true; break;
                case LoopEventPhase.LayerDone: LoopLayerDoneSeen = true; break;
                case LoopEventPhase.Muted: _edge[(int)TourSuccess.LayerMuted] = true; break;
                case LoopEventPhase.Undo: _edge[(int)TourSuccess.LayerUndone] = true; break;
                case LoopEventPhase.ClearArmed: ClearArmedSeen = true; break;
                case LoopEventPhase.Cleared: _edge[(int)TourSuccess.LoopCleared] = true; break;
                case LoopEventPhase.Saved: LoopSaves++; break;
            }
            if (e.Layers > MaxLoopLayers) MaxLoopLayers = e.Layers;
        }

        void OnArrange(ArrangeModeEvent e)
        {
            if (e.Active) { ArrangeSeen = true; _arrangeFor = 0f; _arrangeGrab = false; _movePlayFor = 0f; _edge[(int)TourSuccess.MoveTurnedOn] = true; }
            else
            {
                _edge[(int)TourSuccess.ArrangeEnded] = true;
                if (_arrangeGrab) ArrangeEndedSeen = true;
            }
        }

        void OnTutorial(TutorialEvent e)
        {
            if (e.LessonId == FirstRun.LessonId) return;
            if (e.State == TutorialState.Countdown || e.State == TutorialState.Playing)
            {
                LessonStartedSeen = true;
                ITutorial t = _app.Get<ITutorial>();
                if (t != null && t.Instrument == InstrumentKind.Harp) HarpLessonSeen = true;
            }
            if (e.State == TutorialState.Done) LessonsDone++;
        }

        void OnHit(TutorialHitEvent e)
        {
            if (!e.Hit || e.LessonId == FirstRun.LessonId) return;
            _edge[(int)TourSuccess.LessonHit] = true;
            ITutorial t = _app.Get<ITutorial>();
            if (t != null && t.Instrument == InstrumentKind.Drums) _edge[(int)TourSuccess.KickHit] = true;
        }

        // ---------------- the frame ----------------

        public void Update(float dt)
        {
            Time += dt;
            IMenuPanel menu = _app.Get<IMenuPanel>();
            bool shown = menu != null && menu.Shown && menu.Screen != MenuScreen.Start;
            if (shown) { _menuOpenFor += dt; _sinceMenuPoke += dt; _screenFor += dt; _screen = menu.Screen; }
            else { _menuOpenFor = 0f; _sinceMenuPoke = 0f; }
            _menuShown = shown;
            IMenuGuideHooks hooks = _app.Get<IMenuGuideHooks>();
            if (shown && hooks != null)
            {
                if (hooks.ListMaxScroll > 0f) ListScrollSeen = true;
                float sc = hooks.ListScroll;
                if (Mathf.Abs(sc - _scroll) >= 30f) { _edge[(int)TourSuccess.Scrolled] = true; _scroll = sc; }
            }
            else _scroll = 0f;
            if (shown && _screen != MenuScreen.Home && _screenFor >= SubScreenQuiet && Time - LastMenuPoke >= SubScreenQuiet) SubScreenSeen = true;

            IDrumsApi drums = _app.Get<IDrumsApi>();
            bool stick = drums != null && drums.AnyStickHeld;
            if (stick && !_stickHeld) { StickEver = true; _edge[(int)TourSuccess.StickHeld] = true; }
            if (!stick && _stickHeld) _edge[(int)TourSuccess.StickReleased] = true;
            _stickHeld = stick;
            if (_cymbalAt >= 0f && Time - _cymbalAt >= CymbalRing) { CymbalLeftRinging++; _cymbalAt = -1f; }
            if (_sweepAt >= 0f && Time - _sweepAt >= HarpRing) { SweepsLeftRinging++; _sweepAt = -1f; }

            IArrangeMode arrange = _app.Get<IArrangeMode>();
            _arranging = arrange != null && arrange.Arranging;
            if (_arranging) _arrangeFor += dt;
            IGrabService grab = _app.Get<IGrabService>();
            Rig held = grab?.HeldRig;
            bool any = held != null;
            if (any && !_anyHeld)
            {
                _held = held;
                _heldYaw0 = held.Group != null ? held.Group.eulerAngles.y : 0f;
                if (held.Id == RigId.Lessons) { PanelMovedEver = true; _edge[(int)TourSuccess.PanelGrabbed] = true; }
                else
                {
                    GrabSeen = true;
                    if (_arranging)
                    {
                        _arrangeGrab = true;
                        _edge[(int)TourSuccess.ArrangeGrab] = true;
                    }
                }
            }
            if (!any && _anyHeld && _held != null && _held.Id != RigId.Lessons)
            {
                if (_arranging) ArrangeReleaseSeen = true;
            }
            if (any && held.Group != null)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(held.Group.eulerAngles.y, _heldYaw0)) >= 15f) _edge[(int)TourSuccess.HeldYawTurned] = true;
            }
            _anyHeld = any;
            if (!any) _held = null;
            // Move instruments on, nothing held, a fingertip resting on a play surface: the player is trying to play.
            if (_arranging && !any && FingertipOnPlaySurface()) _movePlayFor += dt; else _movePlayFor = 0f;

            ILooper looper = _app.Get<ILooper>();
            if (looper != null)
            {
                if ((_bpm != 0 && looper.Bpm != _bpm) || (_bars != 0 && looper.Bars != _bars)) _edge[(int)TourSuccess.TempoChanged] = true;
                _bpm = looper.Bpm;
                _bars = looper.Bars;
                if (looper.Metronome && !_metronome) _edge[(int)TourSuccess.MetronomeOn] = true;
                _metronome = looper.Metronome;
            }
            IGuide guide = _app.Get<IGuide>();
            bool dg = guide != null && guide.DeckGuideRunning;
            if (dg && !_deckGuide) { DeckGuideEver = true; _edge[(int)TourSuccess.DeckGuideStarted] = true; }
            _deckGuide = dg;

            IMixedReality mr = _app.Get<IMixedReality>();
            bool placing = mr != null && mr.IsPlacing;
            if (placing && !_placing) { MrPlacingSeen = true; _edge[(int)TourSuccess.MrPlacingStarted] = true; }
            if (!placing && _placing) _edge[(int)TourSuccess.MrPlaced] = true;
            _placing = placing;
            _mrActive = mr != null && mr.Active;
            if (mr != null)
            {
                if (mr.Status == MrStatus.NoPermission) MrNoPermission = true;
                if (mr.Status == MrStatus.NoRooms) MrNoRooms = true;
            }
            if (_mrActive)
            {
                _mrFor += dt;
                if (MrEnteredAs == null && _mrFor >= MrSettle && !placing)
                    MrEnteredAs = _app.Get<IPianoApi>() is IPianoApi piano && piano.OnSurface ? "table" : "floor";   // where it stands
            }
            else _mrFor = 0f;

            Gaze(dt);
            Position(dt);
        }

        // Looking at a thing (25 degrees) for 1.5 s.
        void Gaze(float dt)
        {
            Transform head = _app.Head;
            if (head == null) return;
            IDrumsApi drums = _app.Get<IDrumsApi>();
            IHarpApi harp = _app.Get<IHarpApi>();
            ILooper looper = _app.Get<ILooper>();
            Look(head, drums?.Root, Vector3.up * 0.7f, InstrumentKind.Drums, dt, ref _gazeKit);
            Look(head, harp?.Root, Vector3.up * 0.9f, InstrumentKind.Harp, dt, ref _gazeHarp);
            if (looper != null && looper.TryGetControl(LoopControl.Rec, 0, out Vector3 rec, out _)) LookAt(head, rec, dt, ref _gazeDeck);
            else _gazeDeck = 0f;
            if (TryGetSwitch(out Vector3 sw, out _)) LookAt(head, sw, dt, ref _gazeSwitches);
            else _gazeSwitches = 0f;
        }

        void Look(Transform head, Transform root, Vector3 lift, InstrumentKind kind, float dt, ref float t)
        {
            ILayoutService layout = _app.Get<ILayoutService>();
            if (root == null || !root.gameObject.activeInHierarchy || (layout != null && !layout.IsInScene(kind))) { t = 0f; return; }
            LookAt(head, root.position + lift, dt, ref t);
        }

        static void LookAt(Transform head, Vector3 at, float dt, ref float t)
        {
            if (Vector3.Angle(head.forward, at - head.position) <= GazeDegrees) t += dt;
            else t = 0f;
        }

        public bool TryGetSwitch(out Vector3 world, out Vector3 up)
        {
            world = default; up = Vector3.up;
            ITourAnchors anchors = _app.Get<ITourAnchors>();
            return anchors != null && anchors.TryGetAnchor("switch.fire", out world, out up);
        }

        // Head turned away from the seat, far from the instruments.
        void Position(float dt)
        {
            Transform head = _app.Head;
            ILayoutService layout = _app.Get<ILayoutService>();
            if (head == null || layout == null || !layout.FirstArrangeDone) return;
            bool playing = Time - LastLivePlay < 2.5f;
            SeatFrame seat = layout.Seat;
            // The seat turned (the swivel pinch, 45 degrees a step) without a page turn.
            if (!_seatInit) { _seatInit = true; _seatYaw = seat.YawDeg; }
            else if (Mathf.Abs(Mathf.DeltaAngle(seat.YawDeg, _seatYaw)) >= 30f) { _edge[(int)TourSuccess.SeatTurned] = true; _seatYaw = seat.YawDeg; }
            Vector3 f = head.forward; f.y = 0f;
            float yaw = f.sqrMagnitude > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : seat.YawDeg;
            if (!_mrActive && !playing && Mathf.Abs(Mathf.DeltaAngle(yaw, seat.YawDeg)) >= HeadTurnDeg) _turnedAway += dt; else _turnedAway = 0f;
            bool far = false;
            Vector3 hp = head.position;
            if (new Vector2(hp.x - seat.Head.x, hp.z - seat.Head.z).magnitude > FarSeat) far = true;
            Transform piano = _app.Get<IPianoApi>()?.Root;
            if (!far && piano != null && piano.gameObject.activeInHierarchy && layout.IsInScene(InstrumentKind.Piano)
                && new Vector2(piano.position.x - hp.x, piano.position.z - hp.z).magnitude > FarHead) far = true;
            if (far && !_arranging) _farFor += dt; else _farFor = 0f;
            _headTurnedAway = _turnedAway >= HeadTurnSeconds;
            _farFromInstruments = _farFor >= FarSeconds;
            if (!_arranging) return;
            // Arrange: an instrument farther than 0.8 m (the ray tip).
            _arrangeFarOne = false;
            Transform drums = _app.Get<IDrumsApi>()?.Root, harp = _app.Get<IHarpApi>()?.Root;
            if (FarFrom(hp, piano) || FarFrom(hp, drums) || FarFrom(hp, harp)) _arrangeFarOne = true;
        }

        static bool FarFrom(Vector3 head, Transform t) =>
            t != null && t.gameObject.activeInHierarchy && new Vector2(t.position.x - head.x, t.position.z - head.z).magnitude > ArrangeFar;

        bool _headTurnedAway, _farFromInstruments, _arrangeFarOne;

        // ---------------- triggers ----------------

        /// <summary>The tip's trigger has fired (its condition holds now, or its event happened).</summary>
        public bool Fired(TourTip t)
        {
            switch (t.Trigger)
            {
                case TourTrigger.MenuNeverOpened:
                    return TourOver && !PalmOpenedMenu && Time - TourEndedAt >= NeverOpenedIdle && Time - LastLivePlay >= NeverOpenedIdle && !_menuShown;
                case TourTrigger.MenuOpenNoPoke: return _menuShown && PalmOpenedMenu && _sinceMenuPoke >= MenuNoPoke && _screen == MenuScreen.Home;
                case TourTrigger.MenuOpenLong: return _menuShown && _menuOpenFor >= MenuLong && _sinceMenuPoke >= MenuLong;
                case TourTrigger.GazeKit: return _gazeKit >= GazeSeconds && DrumHits == 0;
                case TourTrigger.GazeHarp: return _gazeHarp >= GazeSeconds && HarpPlucks == 0;
                case TourTrigger.GazeSwitches: return _gazeSwitches >= GazeSeconds && AmbienceFlips == 0;
                case TourTrigger.GazeDeck: return _gazeDeck >= GazeSeconds && Recordings == 0;
                case TourTrigger.DrumHitsNoNod: return DrumHits >= 5 && Nods == 0;
                case TourTrigger.MiddleC: return MiddleCHeard;
                case TourTrigger.FingerDrumHits: return _fingerHits >= 10 && !StickEver;
                case TourTrigger.StickPicked: return _stickHeld;
                case TourTrigger.HatClosedHits: return HatClosedHits >= 3 && HatToggles == 0;
                case TourTrigger.CymbalLeftRinging: return CymbalLeftRinging >= 2;
                case TourTrigger.HarpPlucks: return HarpPlucks >= 5;
                case TourTrigger.HarpSweepRinging: return SweepsLeftRinging >= 2;
                case TourTrigger.RecordingStarted: return _rec == RecorderState.Recording && Recordings > 0 && LoopOff();
                case TourTrigger.TakeSaved: return TakesSaved > 0 && _rec == RecorderState.Idle;
                case TourTrigger.LoopLayerDone: return LoopLayerDoneSeen;
                case TourTrigger.LoopLayers2: return MaxLoopLayers >= 2;
                case TourTrigger.LoopLayers3: return MaxLoopLayers >= 3;
                case TourTrigger.LoopClearArmed: return ClearArmedSeen;
                case TourTrigger.LoopSaved: return LoopSaves >= 1 && LoopOff();
                case TourTrigger.LoopSavedAgain: return LoopSaves >= 2 && LoopOff();
                case TourTrigger.DeckGuideNever: return TakesPlayed > 0 && !DeckGuideEver && _rec == RecorderState.Idle && LoopOff();
                case TourTrigger.SingleLayout: return SingleSeen;
                case TourTrigger.HeadTurnedAway: return _headTurnedAway;
                case TourTrigger.FarFromInstruments: return _farFromInstruments;
                case TourTrigger.GrabStarted: return GrabSeen && _anyHeld;
                case TourTrigger.TurnLearned: return _seen("tip_turn") && Time - _seenAt("tip_turn") >= TurnFollow && _anyHeld;
                case TourTrigger.PalmOpenings: return MenuOpens >= 4 && !PanelMovedEver && _menuShown;
                case TourTrigger.ArrangeOn: return _arranging;
                case TourTrigger.ArrangeNoGrab: return _arranging && !_anyHeld && _arrangeFarOne && (_seen("tip_arrange") || _arrangeFor >= ArrangeNoGrabSeconds);
                case TourTrigger.ArrangeRelease: return _arranging && ArrangeReleaseSeen && !_anyHeld;
                case TourTrigger.ArrangeEnded: return ArrangeEndedSeen && !_arranging;
                case TourTrigger.SubScreen: return SubScreenSeen && _menuShown && _screen != MenuScreen.Home;
                case TourTrigger.ListScrolls: return ListScrollSeen && _menuShown;
                case TourTrigger.Screen: return _menuShown && (int)_screen < s_screens.Length && s_screens[(int)_screen] == t.TriggerArg;
                case TourTrigger.LessonStarted: return LessonStartedSeen && LessonRunning;
                case TourTrigger.DrumLessonKick:
                {
                    ITutorial tut = _app.Get<ITutorial>();
                    return tut != null && tut.Instrument == InstrumentKind.Drums && tut.State == TutorialState.Waiting && tut.Hint != null && tut.Hint.Contains("Kick");
                }
                case TourTrigger.HarpLessonStarted: return HarpLessonSeen && LessonRunning;
                case TourTrigger.LessonDone: return LessonsDone > 0;
                case TourTrigger.RowConfirming: return LastConfirmRow == t.TriggerArg;
                case TourTrigger.MrEntered: return _mrActive && MrEnteredAs == t.TriggerArg;
                case TourTrigger.MrPlacing: return MrPlacingSeen && _placing;
                case TourTrigger.MrNoPermission: return MrNoPermission;
                case TourTrigger.MrNoRooms: return MrNoRooms;
                case TourTrigger.MrLongOrMenu: return _mrActive && (_mrMenuOpened || _mrFor >= MrLong);
                case TourTrigger.OptionsChip:
                    return _menuShown && _screen == MenuScreen.Instruments && _app.Get<IMenuGuideHooks>() is IMenuGuideHooks h
                        && h.TryGetRowRect(OptionsChipRows, out _, out _, out _);
                case TourTrigger.MrHandsRow: return _mrActive && _menuShown && _screen == MenuScreen.Instruments;
                case TourTrigger.InstrumentBodyPinch: return BodyPinchSeen && !_arranging && !MoveOn;
                case TourTrigger.MovePlayAttempt: return _arranging && !_anyHeld && _movePlayFor >= MovePlaySeconds;
                case TourTrigger.MoreInstruments:
                    return TourOver && Brings == 0 && Time - TourEndedAt >= MoreAfter && !_menuShown && !_mrActive
                        && TourTipRunner.TryNearestDisplayed(_app, out _);
                default: return false;
            }
        }

        bool MoveOn => _app.Get<IArrangeMode>() is IArrangeMode a && a.MoveOn;

        /// <summary>A tracked hand's index tip (not pinching) is on an instrument's play surface (keys, heads, strings, bars).</summary>
        bool FingertipOnPlaySurface()
        {
            HandState[] hands = _app.Input?.Hands;
            if (hands == null) return false;
            var all = _app.Instruments;
            for (int h = 0; h < hands.Length; h++)
            {
                HandState hs = hands[h];
                if (hs == null || !hs.Connected || hs.GrabPinching || hs.Pointers.Length == 0 || !hs.Pointers[0].Active) continue;
                Vector3 tip = hs.Pointers[0].Tip;
                for (int i = 0; i < all.Count; i++)
                {
                    IInstrument inst = all[i];
                    if (inst == null || inst.Kind == InstrumentKind.Menu || inst.Root == null || !inst.Root.gameObject.activeInHierarchy) continue;
                    if (inst.PlaySurfaceAt(tip) != PlaySurface.None) return true;
                }
            }
            return false;
        }

        bool LoopOff()
        {
            ILooper l = _app.Get<ILooper>();
            return l == null || l.Status == LoopStatus.Off;
        }

        /// <summary>The player just did what the tip teaches (this frame).</summary>
        public bool Succeeded(TourTip t)
        {
            switch (t.Success)
            {
                case TourSuccess.None: return false;
                case TourSuccess.Row:
                {
                    string want = t.SuccessArg ?? t.AnchorArg;
                    if (_rowThisFrame == null || want == null) return false;
                    return want.EndsWith("*", StringComparison.Ordinal)
                        ? _rowThisFrame.StartsWith(want.Substring(0, want.Length - 1), StringComparison.Ordinal)
                        : _rowThisFrame == want;
                }
                case TourSuccess.SeatTurned: return Edge(TourSuccess.SeatTurned);
                default: return Edge(t.Success);
            }
        }

        /// <summary>The menu is shown (not the start panel), on this screen, for this long.</summary>
        public bool MenuShown => _menuShown;
        public float SinceMenuPoke => Time - LastMenuPoke;
        public bool Arranging => _arranging;
        public bool AnyHeld => _anyHeld;
    }
}
