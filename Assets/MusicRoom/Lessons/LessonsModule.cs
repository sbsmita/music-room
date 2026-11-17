// Lessons module (Order 40, ARCHITECTURE.md sections 3.4, 10 and 15.7): the recorder and its playback targets, the
// looper (layered loops on the deck, saved to Recordings), the lesson library, the take library with auto-save, the
// Tutorial (falling blocks, hits and mistakes, hints, summary) and the tape deck instrument on the side table. Phases: Tutorial (state machine, glows), Recorder (playback dispatch),
// EndFrame (the blocks' final poses and their one instanced draw). The deck ticks in the Instruments phase (TickOrder 30).
// The platform (12-new-instruments/platform.md 6-7): the lesson packs of every instrument, the recorder and the looper on
// the registry's event roles, every IInstrumentPlayback wired for takes and loops (again when instruments register
// later), and the tutorial's generic path (its countdown waits for InstrumentMoveEvent Arrived).
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed class LessonsModule : AppModule
    {
        public const int ModuleOrder = 40;
        public override int Order => ModuleOrder;
        /// <summary>The looper's remembered choices (AppSettings, the injected store).</summary>
        public const string KeyLoopTempo = "musicroom.loop.tempo", KeyLoopBars = "musicroom.loop.bars",
            KeyLoopClick = "musicroom.loop.click", KeyLoopTidy = "musicroom.loop.tidy";

        App _app;
        ILayoutService _layout;
        Action<RecorderEvent> _onPlay;
        Action<LayoutChangedEvent> _onLayout;
        Action<TutorialEvent> _onTutorial;
        Action<InstrumentMoveEvent> _onMove;
        int _wiredVersion = -1;

        public Recorder Recorder { get; private set; }
        public Looper Looper { get; private set; }
        public LessonLibrary Library { get; private set; }
        public TakeLibrary Takes { get; private set; }
        public Tutorial Tutorial { get; private set; }
        public LessonBlocks Blocks { get; private set; }
        public TapeDeck Deck { get; private set; }
        /// <summary>Sunny's line about recording on top, asked after a take's first end.</summary>
        public RecordOnTopTip OnTopTip { get; private set; }
        /// <summary>The side-table rig the deck sits on (Room registers it at Order 20), or null when there is none.</summary>
        public Rig TableRig { get; private set; }

        public override void Init(App app)
        {
            _app = app;
            // The recorder runs on the app's audio clock (AudioSettings.dspTime, or a test's scripted clock: ARCHITECTURE.md
            // sections 4 and 15.7), the clock the instruments schedule on, so take timestamps and playback share it.
            Recorder = new Recorder(() => app.AudioTime, app.Events) { Roles = EventRoles.From(app.Registry), PresenceSource = () => app.Get<IAudioPresence>() };
            Library = LessonLibrary.LoadFromResources(app.Registry);
            // Takes live under app.DataPath (persistentDataPath; a temporary folder in tests: TestBoot).
            Takes = new TakeLibrary(Path.Combine(app.DataPath, "takes"));
            // AppSettings.KeyNextTakeNumber mirrors the index (section 18). Written to the store only, never saved here: a
            // synchronous PlayerPrefs.Save in the frame STOP is pressed would buy nothing, since index.json is the
            // authority (TakeLibrary recomputes nextNumber from it) and the store flushes on quit / pause.
            Takes.NextNumberChanged += n => app.Settings.SetInt(AppSettings.KeyNextTakeNumber, n);
            // Auto-save: every recording that stops with something in it becomes "Take N · m:ss". Put
            // serialises and writes on a worker, so this frame only names the take.
            Recorder.OnTake += (events, seconds) =>
            {
                var rec = new TakeRecord { name = Takes.ReserveName(seconds), seconds = (float)seconds, events = events };
                _ = Takes.Put(rec);
                Recorder.DeckTakeName = rec.name;   // the deck's take now (a recording on top logs its name)
            };
            Recorder.NameOfId = id => Takes.NameOf(id);
            Takes.Changed += () => app.Events.Publish(new LibraryChangedEvent { Version = Takes.Version });
            Takes.Removing += OnTakeRemoving;
            PlaybackWiring.Wire(Recorder, () => app.Get<IPianoApi>(), () => app.Get<IDrumsApi>(), () => app.Get<IHarpApi>());
            WireInstruments();
            BuildLooper(app);

            Tutorial = new Tutorial(Library, new AppLessonStage(app), app.Events);
            Blocks = new LessonBlocks(app.Materials, app.Headless);
            _onPlay = OnRecorderEvent;
            _onLayout = OnLayoutChanged;
            _onTutorial = OnTutorialEvent;
            _onMove = OnMove;
            app.Events.Subscribe(_onPlay);
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onTutorial);
            app.Events.Subscribe(_onMove);
            _onClockJump = e => Looper?.ClockJumped(e.Delta);   // the output restarted: the loop's schedule moves with the clock
            app.Events.Subscribe(_onClockJump);

            BuildDeck(app);
            OnTopTip = new RecordOnTopTip(Recorder, () => Guide(app));

            app.Register<IRecorder>(Recorder);
            app.Register<ILessonLibrary>(Library);
            app.Register<ITakeLibrary>(Takes);
            app.Register<ITutorial>(Tutorial);
            app.Register<ILooper>(Looper);
            Debug.Log($"[Lessons] ready: {Library.Lessons.Count} lessons ({Library.Packs.Count} packs), {Library.TotalEvents} events, playback types={Recorder.TargetCount}, takes={(Takes.Available == true ? "on" : "off")}, deck={(TableRig != null ? "table" : "world")}, blocks={(Blocks.CanDraw ? "instanced" : "headless")}");
        }

        /// <summary>platform.md 6.2: every registered IInstrumentPlayback's types (beyond the seven classic lines). Run at Init
        /// and again whenever registrations change (an instrument registering after this module), never per event.</summary>
        void WireInstruments()
        {
            if (_app == null || _wiredVersion == _app.ServicesVersion) return;
            _wiredVersion = _app.ServicesVersion;
            int n = PlaybackWiring.WireInstruments(Recorder, _app.Registry, _app.GetAll<IInstrumentPlayback>(), () => _app.Get<IPianoApi>());
            if (n > 0) Debug.Log($"[Lessons] playback wired: {n} more event types");
        }

        // The deck goes on the side table: under the Table rig's Group, one of its instruments, its body
        // added to the rig's grab handles (the table is grabbed by "the table's top, legs, the deck's body").
        void BuildDeck(App app)
        {
            foreach (Rig r in app.GetAll<Rig>()) if (r.Id == RigId.Table) { TableRig = r; break; }
            Transform parent = TableRig?.Group;
            if (parent == null)
            {
                // No Room module (a filtered test boot): stand the deck where the table would be, in the world.
                Debug.LogWarning("[Lessons] no Table rig registered: the tape deck stands alone at the table's home");
                var stand = new GameObject("TapeDeckStand").transform;
                stand.SetParent(app.WorldRoot, false);
                stand.SetPositionAndRotation(new Vector3(-0.39f, 0f, -0.21f), Quaternion.Euler(0f, -90f, 0f));
                parent = stand;
            }
            Deck = new TapeDeck(Recorder, app.Materials, parent, TapeDeck.TableLocalPosition, Looper) { Desktop = new AppDeckDesktop(app) };
            Looper.Controls = (c, layer) => (Deck.TryGetControl(c, layer, out Vector3 w, out Vector3 up), w, up);
            Looper.PressHandler = (c, layer) => Deck.Press(TapeDeck.ButtonOf(c, layer));
            // GUIDE: Sunny's walkthrough of the deck (the Guide module, Order 50, registers IGuide after us: looked up on use).
            Deck.GuidePressed = () => Guide(app)?.ToggleDeckGuide();
            Deck.GuideRunning = () => { IGuide g = Guide(app); return g != null && g.DeckGuideRunning; };
            if (TableRig != null)
            {
                TableRig.Instruments.Add(Deck);
                AddHandles(TableRig, Deck.HandleRenderers);
            }
            // The drawn fingers rest on the deck's controls instead of sinking into it (E1), with their contact shadows (E3).
            Rig tableRig = TableRig;
            Deck.Held = () =>
            {
                if (tableRig == null) return false;
                if (_grab == null) _grab = app.Get<IGrabService>();
                return _grab != null && ReferenceEquals(_grab.HeldRig, tableRig);
            };
            app.Register<IContactSurface>(Deck);
            app.Register<IInstrument>(Deck);
        }

        /// <summary>Rig.Handles is filled once in Init and only read afterwards (ARCHITECTURE.md section 8). Room (Order 20)
        /// filled its list; the deck's body joins it in place, so Layout's per-frame GrabTarget still allocates nothing.
        /// Should a rig owner hand out something other than a List (an array), a combined list replaces it once.</summary>
        public static void AddHandles(Rig rig, IReadOnlyList<Renderer> extra)
        {
            if (rig.Handles is List<Renderer> own)
            {
                own.AddRange(extra);
                return;
            }
            IReadOnlyList<Renderer> old = rig.Handles ?? Array.Empty<Renderer>();
            var all = new List<Renderer>(old.Count + extra.Count);
            all.AddRange(old);
            all.AddRange(extra);
            rig.Handles = all;
        }

        IGuide _guide;
        IGrabService _grab;
        IGuide Guide(App app) => _guide ?? (_guide = app.Get<IGuide>());

        public override void Tick(FramePhase phase, float dt)
        {
            switch (phase)
            {
                case FramePhase.Tutorial:
                    Tutorial.Update(dt, _app.HoverGlow);
                    break;
                case FramePhase.Recorder:
                    WireInstruments();
                    Recorder.Update();
                    OnTopTip?.Tick(dt);
                    break;
                case FramePhase.EndFrame:
                    // Final instrument poses are known now (grab, page turn and layout ran after the Tutorial phase).
                    if (Tutorial.State != TutorialState.Idle && Tutorial.Compose() > 0)
                        Blocks.Draw(Tutorial.Shape, Tutorial.Matrices, Tutorial.Colors, Tutorial.BlockCount, Tutorial.BlockBounds);
                    break;
            }
        }

        // The looper (Looper.cs): sends through AppLoopHost, remembers its tempo / length / click / tidy, saves finished
        // sessions to the take library ("Loop N · 90 BPM · 3 layers", the same numbering as the takes), publishes LoopEvent.
        void BuildLooper(App app)
        {
            Looper = new Looper(new AppLoopHost(app, Recorder));
            AppSettings st = app.Settings;
            Looper.Load(st.GetInt(KeyLoopTempo, Looper.DefaultTempo), st.GetInt(KeyLoopBars, Looper.DefaultBars),
                st.GetInt(KeyLoopClick, 0) != 0, st.GetInt(KeyLoopTidy, 1) != 0);
            Looper.SettingsChanged += () =>
            {
                st.SetInt(KeyLoopTempo, Looper.TempoIndex);
                st.SetInt(KeyLoopBars, Looper.BarsSetting);
                st.SetInt(KeyLoopClick, Looper.Metronome ? 1 : 0);
                st.SetInt(KeyLoopTidy, Looper.Tidy ? 1 : 0);
                st.Save();
            };
            Looper.Save += SaveLoop;
            Looper.Changed += (phase, layer, on) =>
            {
                // Starting a loop ends a lesson (they would fight over the player's attention and timing).
                if (phase == LoopEventPhase.CountIn && Tutorial != null && Tutorial.State != TutorialState.Idle) Tutorial.Stop();
                app.Events.Publish(new LoopEvent { Phase = phase, Layers = Looper.Layers, Layer = layer, On = on });
            };
            Recorder.Looper = Looper;
        }

        /// <summary>A finished loop session goes to Recordings: the first save reserves its number and id, later saves (a layer
        /// added, undone or muted) update the same record in place.</summary>
        void SaveLoop(LoopSession s)
        {
            if (Takes == null || Takes.Available != true) return;
            if (s.Number <= 0) s.Number = Takes.ReserveNumber();
            if (string.IsNullOrEmpty(s.Id)) s.Id = Takes.NewId();
            var rec = new TakeRecord
            {
                id = s.Id, name = LoopFormat.Name(s.Number, s.Bpm, s.Finished), seconds = (float)s.PassSeconds,
                events = LoopFormat.Encode(s),
            };
            _ = Takes.Put(rec);
            Debug.Log($"[Lessons] loop saved: {rec.name} ({s.EventCount} events)");
        }

        /// <summary>A recording is being deleted (Recordings' bin or Delete): a loop session saved under its id lets go of the
        /// record (no id, no number, nothing unsaved), so stopping it or changing it later never writes the deleted take back;
        /// a later change saves it as a new recording. The menu stops a playback of that take itself.</summary>
        void OnTakeRemoving(string id) => DetachLoop(Looper, id);

        public static void DetachLoop(Looper looper, string id)
        {
            if (looper == null || string.IsNullOrEmpty(id)) return;
            foreach (LoopSession s in new[] { looper.Session, looper.DeckSession, looper.ExternalSession })
            {
                if (s == null || s.Id != id) continue;
                s.Id = null;
                s.Number = 0;
                s.Dirty = false;
            }
        }

        // Starting a lesson stops looping (the session is saved), so the lesson's own timing and blocks are alone.
        void OnTutorialEvent(TutorialEvent e)
        {
            if (e.State == TutorialState.Countdown && Looper != null && Looper.Active) Looper.Stop();
        }

        // Hits come from the recorder's observer stream, so every way of playing counts the same.
        void OnRecorderEvent(RecorderEvent e) => Tutorial.OnPlay(e.Event, e.DuringPlayback);

        // platform.md 7.3: the lesson's countdown starts when its instrument has arrived at the seat.
        void OnMove(InstrumentMoveEvent e) => Tutorial.OnMove(e);

        // A lesson (running or showing its summary) stops when its instrument leaves the scene. Layout owns
        // this; doing it here as well keeps it true whatever Layout implementation runs (Stop is idempotent on Idle).
        // A lesson still waiting for its instrument (gliding in after Bring) is not stopped.
        void OnLayoutChanged(LayoutChangedEvent e)
        {
            if (Tutorial.State == TutorialState.Idle || Tutorial.AwaitingArrival) return;
            if (_layout == null) _layout = _app.Get<ILayoutService>();
            if (_layout != null && !_layout.IsMoving(Tutorial.Instrument) && !_layout.IsInScene(Tutorial.Instrument)) Tutorial.Stop();
        }

        System.Action<AudioClockJumpEvent> _onClockJump;

        public override void Shutdown()
        {
            if (_app == null) return;
            _app.Events.Unsubscribe(_onPlay);
            _app.Events.Unsubscribe(_onLayout);
            _app.Events.Unsubscribe(_onTutorial);
            _app.Events.Unsubscribe(_onMove);
            if (_onClockJump != null) _app.Events.Unsubscribe(_onClockJump);
            // The recorder is left as it is: stopping here would auto-save a half take with a file write racing the quit.
            Blocks?.Dispose();
            Deck?.Destroy();
        }
    }

    /// <summary>The deck's desktop side in the app (ARCHITECTURE.md sections 0 and 3.3): a left-button press counts only
    /// while the app is not presenting (!App.IsPresenting), the mouse ray comes from the head camera, the
    /// mouse hand's pointers are Pointer.FromMouse, and Shift / Alt make the press Layout's rig move / turn.</summary>
    public sealed class AppDeckDesktop : IDeckDesktop
    {
        readonly App _app;
        Camera _camera;

        public AppDeckDesktop(App app) { _app = app; }

        /// <summary>App.IsPresenting: an XR session, or a test's simulated headset with scripted hands.</summary>
        public bool Presenting => _app.IsPresenting;

        public bool TryClick(out Ray ray)
        {
            ray = default;
            if (!ClickAllowed(Presenting, DesktopInput.MouseDown(0))) return false;
            if (_camera == null) _camera = _app.Rig != null ? _app.Rig.HeadCamera : null;   // looked up once, on the first click
            if (_camera == null) return false;
            Vector2 m = DesktopInput.MousePosition;
            ray = _camera.ScreenPointToRay(new Vector3(m.x, m.y, 0f));
            return true;
        }

        public bool IsMouse(Pointer p) => p != null && p.FromMouse;

        public bool ModifierHeld => DesktopInput.Shift || DesktopInput.Alt;

        /// <summary>A left-button press counts only on the desktop (never while presenting in the headset).</summary>
        public static bool ClickAllowed(bool presenting, bool leftDown) => !presenting && leftDown;
    }
}
