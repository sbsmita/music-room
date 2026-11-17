// The Guide module (Order 50): Sunny, the sun-faced robot that shows a newcomer the palm menu, the page-turn swipe and
// the nod kick (spec 08 section 3, ARCHITECTURE.md sections 11 and 15.8). It owns Sunny's root (the Guide audio source
// follows it), registers IGuide, publishes GuidePresenceEvent whenever Sunny arrives or leaves, starts the tour by
// itself on the first launch only (AppSettings.TourDone), toggles on Shift+H on a desktop, and cuts the voice when the
// headset comes off. It only ever watches the menu, the layout and the drums: it never takes input away from play.
// The platform (12-new-instruments/platform.md 8): the tips of every registry descriptor and IGuideTipSource (TipRunner,
// GuideTips.cs; the platform's own in PlatformTips.cs) come with their triggers, successes and demos, IGuide.Tip also
// takes a TipSpec id, every module's lines are merged (GuideLines.Merged), and a lesson waiting 8 s on the generic path
// ("lesson.redemo") brings Sunny with "Like this." and the instrument's move at the target.
using System;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Guide
{
    /// <summary>Ctrl / Cmd state for the Shift+H guard (Core's DesktopInput.Ctrl and Meta, applied at integration).</summary>
    public static class GuideKeys
    {
        /// <summary>Ctrl or Cmd / Windows key held (Core's DesktopInput.Ctrl / Meta): Shift+H is ignored then.</summary>
        public static bool CtrlOrMeta => DesktopInput.Ctrl || DesktopInput.Meta;
    }

    /// <summary>First five (GuideModule.FirstFive.cs): the first run, the tips, poke to skip, the Sunny screen.</summary>
    public sealed partial class GuideModule : AppModule, IGuide
    {
        public const int ModuleOrder = 50;
        public override int Order => ModuleOrder;

        /// <summary>Sunny's warm point light (optional on Quest: a fifth per-pixel light near the piano). Read at Init.</summary>
        public static bool LightEnabled = true;
        /// <summary>First launch: how long the auto-start waits for the intro line's robotised buffer, so the greeting is
        /// voiced rather than captioned (the engine renders the voice lines after the piano and harp; CONTRACT-REQUESTS 2).</summary>
        public const float AutoStartVoiceWait = 6f;
        /// <summary>Tips heard so far, persisted (Docs/11-first-five/sunny-tour.md 3.1): comma-separated ids.</summary>
        public const string KeyTipsSeen = "musicroom.tipsSeen";
        /// <summary>IGuide.Tip's answer adds this quiet gap after the line before the caller goes on.</summary>
        public const float TipGap = 0.3f;

        App _app;
        IAudioEngine _audio;
        EngineVoice _voice;
        AppWorld _world;
        bool _tourDoneAtBoot, _autoStarted, _lastPresent, _lastDone;
        string _tip;   // the tip Sunny is saying (marked seen once heard)
        float _autoWait;
        Action<MenuEvent> _onMenu;
        Action<LayoutChangedEvent> _onLayout;
        Action<NodEvent> _onNod;
        Action<RecorderEvent> _onRecorder;
        Action<HmdMountEvent> _onMount;
        Action<HandsLowLightEvent> _onLowLight;
        Action<InstrumentMoveEvent> _onMove;
        Action<InstrumentSignalEvent> _onSignal;
        Action<FocusEvent> _onFocus;
        Action<TutorialEvent> _onTutorial;
        bool _lightHintPending, _lightHintShown;

        /// <summary>The Sunny tip id of the B6 lesson re-demo (never persisted: it helps every time), and the signal the
        /// tutorial publishes for it (Lessons' Tutorial.RedemoSignal, the same string: GuideTipsPlayTests checks it).</summary>
        public const string RedemoTipId = "lesson.redemo", LessonRedemoSignal = "lesson.redemo";
        /// <summary>The platform's tips (descriptors and IGuideTipSources).</summary>
        public TipRunner Tips { get; private set; }
        /// <summary>Lesson re-demos shown this launch.</summary>
        public int Redemos { get; private set; }

        public Sunny Sunny { get; private set; }
        /// <summary>The tape deck walkthrough (the deck's GUIDE button).</summary>
        public DeckGuide DeckGuide { get; private set; }
        public SunnyView View { get; private set; }
        public AppWorld World => _world;
        /// <summary>Whether an XR session is presenting (start, events, Shift+H): App.IsPresenting, true in a
        /// headset session or with BootOptions.SimulateHeadset. It picks the *_vr or *_desk lines, the nod-kick rule and
        /// whether Shift+H works. Never App.IsHeadset, which only says the hand frames come from the tracker (section 3.3).</summary>
        public bool IsPresenting => _app != null && _app.IsPresenting;
        /// <summary>The persisted first-launch flag as read at boot (Sunny starts by itself only when it was false).</summary>
        public bool TourDoneAtBoot => _tourDoneAtBoot;
        public bool AutoStarted => _autoStarted;

        public override void Init(App app)
        {
            _app = app;
            _audio = app.Get<IAudioEngine>();
            _voice = new EngineVoice(_audio);
            _world = new AppWorld(app, this);
            Sunny = new Sunny(_world, _voice, Mulberry32.SessionSeed());
            View = new SunnyView(app.WorldRoot, app.Materials, LightEnabled);
            DeckGuide = new DeckGuide(app, Sunny, MarkTipSeen);
            _audio?.SetSourceParent(SourceId.Guide, View.Root, Vector3.zero);
            _tourDoneAtBoot = app.Settings.TourDone;
            Tips = new TipRunner(app, this);
            if (_tourDoneAtBoot) Tips.TourEnd = 0f;            // done on an earlier launch: from now on
            InitFirstFive(app);   // first five: the first run, the tour's tips (presented for the runner), poke to skip
            app.Register<IGuide>(this);

            _onMenu = OnMenu;
            _onLayout = OnLayout;
            _onNod = OnNod;
            _onRecorder = OnRecorder;
            _onMount = OnMount;
            _onLowLight = OnLowLight;
            _onMove = e => Tips.OnMove(e);
            _onSignal = OnSignal;
            _onFocus = e => Tips.OnFocus(e);
            _onTutorial = e => Tips.OnTutorial(e);
            app.Events.Subscribe(_onMove);
            app.Events.Subscribe(_onSignal);
            app.Events.Subscribe(_onFocus);
            app.Events.Subscribe(_onTutorial);
            app.Events.Subscribe(_onLowLight);
            app.Events.Subscribe(_onMenu);
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onNod);
            app.Events.Subscribe(_onRecorder);
            app.Events.Subscribe(_onMount);
            View.Apply(Sunny);
            Debug.Log($"[Guide] ready: tourDone={_tourDoneAtBoot}, voice={(_audio != null ? "engine" : "none")}, text={View.HasText}, light={LightEnabled}, tips={Tips.Entries.Count}, merged lines={GuideLines.Merged.Ids.Count}");
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase != FramePhase.Guide) return;
            // Shift+H toggles Sunny on a desktop (plain H stays a piano key); it is ignored with Ctrl or
            // Cmd held (system shortcuts) and while presenting.
            if (!IsPresenting && DesktopInput.Shift && DesktopInput.KeyDown(DKey.H) && !GuideKeys.CtrlOrMeta) Toggle();
            // "Move instruments": nothing plays, so Sunny never prompts to play meanwhile. A tour in progress goes quiet and
            // out of sight and holds where it was (timers, step and line) until Done. Otherwise, the first time ever, Sunny
            // comes by with the tip_arrange line (sunny-tour.md 3.2) and leaves again.
            bool arranging = Arranging;
            if (arranging != _arrangingWas)
            {
                _arrangingWas = arranging;
                if (arranging)
                {
                    if (FirstFiveTipShowing) TourTips.Stop(false);   // first five: a tip gives way (the arrange tips follow)
                    if (Sunny.Present && !Sunny.InTip && !Sunny.Hinting)   // the tour or the deck walkthrough
                    {
                        _heldForArrange = true;
                        Sunny.StopVoice();
                        if (View.Root != null) View.Root.gameObject.SetActive(false);
                        Debug.Log("[Guide] tour held while the instruments are moved");
                    }
                    else if (!TipSeen(GuideLines.TipArrange) && !TipsByRunner) StartTip(GuideLines.TipArrange);
                }
                else if (_heldForArrange)
                {
                    _heldForArrange = false;
                    if (View.Root != null) View.Root.gameObject.SetActive(true);
                }
            }
            if (_heldForArrange) { Sync(); return; }
            if (!arranging)   // the first-launch tour (and the low-light hint) never start while the instruments are moved
            {
                AutoStart(dt);
                LightHint();
            }
            if (ClassicTour) PokeSunny(dt);
            else TickFirstFive(dt);   // first five: poke to skip, the first run, the tips
            DeckGuide.Update(dt);
            Tips.Tick(dt);
            Sunny.Update(dt);
            View.Apply(Sunny);
            TrackTip(arranging);
            Sync();
        }

        IArrangeMode _arrange;
        IMenuPanel _menu;
        IGrabService _grab;
        bool _heldForArrange, _arrangingWas, _tipStarted, _tourStarted;

        /// <summary>Tips shown this launch.</summary>
        public int TipsShown { get; private set; }
        /// <summary>The tip in progress (null when none).</summary>
        public string ActiveTip => _tip;

        // Sunny says a tip: the first time "Move instruments" turns on (tip_arrange) or IGuide.Tip (the looper's
        // tip_loop_start). False while Sunny is here.
        bool StartTip(string id)
        {
            if (!Sunny.Tip(id)) return false;
            _tip = id;
            _tipStarted = true;
            TipsShown++;
            Debug.Log($"[Guide] tip {id}");
            return true;
        }

        /// <summary>Sunny is free for a tip: not here (no tour, walkthrough, tip or hint, not leaving) and the instruments are
        /// not being moved.</summary>
        public bool SunnyFree => Sunny != null && (Sunny.State == SunnyState.Idle || Sunny.State == SunnyState.Gone)
            && !(DeckGuide != null && DeckGuide.Running) && !_heldForArrange && !Arranging;

        /// <summary>A TipSpec (TipRunner): its lines (TextOnly: its Title as the caption), held at least minHold. Returns the
        /// seconds it takes, 0 when Sunny could not say it.</summary>
        public float SayTip(TipSpec spec, float minHold, bool force = false)
        {
            if (spec == null || (!force && !SunnyFree)) return 0f;   // force: the menu's Tips list (first five)
            bool said;
            float seconds;
            if (spec.TextOnly)
            {
                seconds = Mathf.Max(GuideTiming.HintT, minHold);
                said = Sunny.TipText(spec.Id, spec.Title, seconds);
            }
            else
            {
                string[] lines = spec.Lines != null && spec.Lines.Length > 0 ? spec.Lines : null;
                said = Sunny.TipLines(spec.Id, lines, minHold);
                seconds = 0f;
                if (lines == null) seconds = Sunny.Hold(spec.Id, minHold);
                else for (int i = 0; i < lines.Length; i++) seconds += Sunny.Hold(lines[i], i == lines.Length - 1 ? minHold : GuideTiming.TipT);
            }
            if (!said) return 0f;
            _tip = spec.Id;
            _tipStarted = true;
            TipsShown++;
            Debug.Log($"[Guide] tip {spec.Id}{(spec.TextOnly ? " (caption)" : "")}");
            Sync();
            return seconds + TipGap;
        }

        // B6 (platform.md 7.2): a lesson waited 8 s on its event: Sunny shows the instrument's move at the target again.
        void Redemo(InstrumentKind kind)
        {
            if (!SunnyFree || Tips == null) return;
            if (!Tips.LessonTargetAnchor(kind, out Vector3 at, out Vector3 up)) return;
            if (!Sunny.TipLines(RedemoTipId, s_again, TipRunner.DemoHold)) return;
            Sunny.ShowTipDemo(Tips.RedemoKind(kind, _app.Get<ITutorial>()?.LessonId), true, at, up);
            Redemos++;
            Debug.Log($"[Guide] lesson re-demo ({kind})");
            Sync();
        }

        static readonly string[] s_again = { "again" };

        void OnSignal(InstrumentSignalEvent e)
        {
            if (string.Equals(e.Signal, LessonRedemoSignal, StringComparison.Ordinal)) { Redemo(e.Kind); return; }
            Tips.OnSignal(e);
        }

        /// <summary>A tip is seen once its line was heard to the end (Sunny.TipHeard: Sunny then leaves), or as soon as the
        /// player does what it teaches (an arrange-mode grab for tip_arrange). A tip cut short (the headset came off, Hide
        /// Sunny, the walkthrough took over) stays unseen, so the next trigger says it again.</summary>
        void TrackTip(bool arranging)
        {
            if (_tip == null) return;
            if (arranging && string.Equals(_tip, GuideLines.TipArrange, StringComparison.Ordinal))
            {
                if (_grab == null) _grab = _app.Get<IGrabService>();
                if (_grab != null && _grab.AnyHeld) MarkTipSeen(_tip);
            }
            if (Sunny.State == SunnyState.Tip) return;   // still saying it
            if (Sunny.TipHeard && string.Equals(Sunny.TipId, _tip, StringComparison.Ordinal) && !TipSeen(_tip))
            {
                MarkTipSeen(_tip);
                Debug.Log($"[Guide] tip {_tip} heard (saved)");
            }
            _tip = null;
        }

        /// <summary>The instruments are being moved (IArrangeMode, Layout): the tour holds.</summary>
        public bool Arranging
        {
            get
            {
                if (_arrange == null && _app != null) _arrange = _app.Get<IArrangeMode>();
                return _arrange != null && _arrange.Arranging;
            }
        }

        /// <summary>The menu's start panel ("Play here" / "Play in my room") is still waiting for an answer.</summary>
        bool StartPending
        {
            get
            {
                if (_menu == null && _app != null) _menu = _app.Get<IMenuPanel>();
                return _menu != null && _menu.StartPending;
            }
        }

        public override void Shutdown()
        {
            if (_app == null) return;
            _app.Events.Unsubscribe(_onMenu);
            _app.Events.Unsubscribe(_onLayout);
            _app.Events.Unsubscribe(_onNod);
            _app.Events.Unsubscribe(_onRecorder);
            _app.Events.Unsubscribe(_onMount);
            _app.Events.Unsubscribe(_onLowLight);
            _app.Events.Unsubscribe(_onMove);
            _app.Events.Unsubscribe(_onSignal);
            _app.Events.Unsubscribe(_onFocus);
            _app.Events.Unsubscribe(_onTutorial);
            Sunny?.StopVoice();
            DeckGuide?.Dispose();
            ShutdownFirstFive();
            _app.Unregister<IGuide>(this);
        }

        // Spec 08 section 3.3 / ARCHITECTURE.md section 11: once per launch, on the first arranged frame with the headset on
        // (on a desktop, on Play), and only while the tour has never been finished or dismissed.
        void AutoStart(float dt)
        {
            if (!ClassicTour) { AutoStartFirstFive(); return; }   // first five: the first run under the start panel
            if (_autoStarted) return;
            if (Sunny.InTip) return;   // a tip first (the player moved the instruments before the tour began): then the tour
            if (_tourDoneAtBoot || _tourStarted || (Sunny.State != SunnyState.Idle && !_tipStarted))
            {
                _autoStarted = true;   // done on an earlier launch, or already started from the menu
                return;
            }
            ILayoutService layout = _world.Layout;
            bool arranged = (layout != null && layout.FirstArrangeDone) || _app.Rig.State == XrState.Desktop;
            if (!arranged || !_app.Rig.HmdMounted) return;
            // The start panel comes first: the tour begins once the player chose "Play here" or "Play in my room".
            if (StartPending) return;
            string intro = IsPresenting ? GuideLines.IntroVr : GuideLines.IntroDesk;
            if (_voice.Available && _voice.HasLine(intro) && !_voice.LineReady(intro) && _autoWait < AutoStartVoiceWait)
            {
                _autoWait += dt;
                return;
            }
            _autoStarted = true;
            Start();
        }

        // Presence for the menu row ("Show me around" / "Hide Sunny") and the persisted first-launch flag.
        void Sync()
        {
            bool present = Sunny.Present;
            if (present != _lastPresent)
            {
                _lastPresent = present;
                _app.Events.Publish(new GuidePresenceEvent { Present = present });
            }
            bool done = Sunny.Done;
            if (done && !_lastDone && Tips != null && float.IsNaN(Tips.TourEnd)) Tips.TourEnd = Tips.Time;   // P3 tips wait 90 s from here
            if (done && !_lastDone && !_app.Settings.TourDone)
            {
                _app.Settings.TourDone = true;
                _app.Settings.Save();
                Debug.Log("[Guide] tour done (saved): it will not start by itself again");
            }
            _lastDone = done;
        }

        // ---------------- IGuide ----------------
        public bool Present => Sunny != null && Sunny.Present;
        public bool Done => Sunny != null && Sunny.Done;

        public void Start()
        {
            if (Sunny == null) return;
            if (!ClassicTour) { StartFirstFive(); return; }   // first five: "Show me around" replays the first run
            _tourStarted = true;
            if (_heldForArrange)
            {
                // "Show me around" while a held tour waits: it starts again from the top, in view.
                _heldForArrange = false;
                if (View.Root != null) View.Root.gameObject.SetActive(true);
            }
            Sunny.Start();
            Debug.Log($"[Guide] tour started (desktop={Sunny.Desktop})");
            Sync();   // the scene follows in this frame's Guide phase
        }

        public void Dismiss()
        {
            if (Sunny == null) return;
            if (DeckGuide != null && DeckGuide.Running)
            {
                DeckGuide.End();          // Hide Sunny during the walkthrough ends it (deck tidied, layout restored)
                Sync();
                return;
            }
            if (DismissFirstFive()) { Sync(); return; }   // first five
            bool wasPresent = Sunny.Present;
            Sunny.Dismiss();
            if (wasPresent) Debug.Log("[Guide] dismissed");
            Sync();
        }

        public void Toggle()
        {
            if (Sunny == null) return;
            if (Sunny.Present) Dismiss();
            else Start();
        }

        /// <summary>A one-line tip, said once ever (persisted in KeyTipsSeen once heard to the end). Returns the seconds the
        /// line takes (the caller may hold its own action until then, e.g. the looper's clicks), or 0 when it is not said:
        /// already heard, not a line, or Sunny busy (the tour, another tip, leaving). A tip skipped because Sunny was busy
        /// stays unseen, so the next trigger can say it.</summary>
        public float Tip(string id)
        {
            if (Tips != null && Tips.Find(id) is TipEntry e && ManualByRunner(e)) return Tips.Manual(id);   // a TipSpec id (TipTrigger.Manual)
            if (Sunny == null || !GuideLines.Has(id) || TipSeen(id)) return 0f;
            if (Sunny.Visible && Sunny.State != SunnyState.Idle && Sunny.State != SunnyState.Gone) return 0f;
            if (!StartTip(id)) return 0f;
            Sync();
            return Sunny.LineSeconds(id) + TipGap;
        }

        // ---------------- the tape deck walkthrough ----------------

        public bool DeckGuideRunning => DeckGuide != null && DeckGuide.Running;

        /// <summary>The deck's GUIDE button: starts the quick show (the tour or a tip in progress gives way), or ends whichever
        /// deck guide runs.</summary>
        public void ToggleDeckGuide()
        {
            if (DeckGuide == null) return;
            if (DeckGuide.Running) DeckGuide.End();
            else { YieldToDeckGuide(); DeckGuide.Start(quick: true); }   // first five: the first run and a tip give way
            Sync();
        }

        /// <summary>"Make a song in layers": the full walkthrough (a quick show in progress ends first).</summary>
        public void StartDeckWalkthrough()
        {
            if (DeckGuide == null) return;
            if (DeckGuide.Running) DeckGuide.End();
            YieldToDeckGuide();
            DeckGuide.Start(quick: false);
            Sync();
        }

        readonly bool[] _pokeInside = new bool[16];
        float _pokeQuiet;

        // A fingertip poking Sunny during the walkthrough skips the step (once per entry, at most every 0.5 s).
        void PokeSunny(float dt)
        {
            _pokeQuiet -= dt;
            if (!DeckGuide.Running || !Sunny.Visible || _app.Input == null) return;
            var pts = _app.Input.HandPointers;
            float r2 = DeckGuide.PokeRadius * DeckGuide.PokeRadius;
            for (int i = 0; i < pts.Count && i < _pokeInside.Length; i++)
            {
                Pointer p = pts[i];
                bool inside = p.Active && !p.FromMouse && (p.Tip - Sunny.Position).sqrMagnitude < r2;
                if (inside && !_pokeInside[i] && _pokeQuiet <= 0f)
                {
                    _pokeQuiet = 0.5f;
                    DeckGuide.Skip();
                }
                _pokeInside[i] = inside;
            }
        }

        /// <summary>Persist a tip as seen (musicroom.tipsSeen); the tip runner forgets it.</summary>
        public void MarkTipSeen(string id)
        {
            Tips?.NoteSeen(id);
            if (string.IsNullOrEmpty(id) || TipSeen(id)) return;
            string seen = _app.Settings.GetString(KeyTipsSeen, "");
            _app.Settings.SetString(KeyTipsSeen, string.IsNullOrEmpty(seen) ? id : seen + "," + id);
            _app.Settings.Save();
            SeenAdded(id);   // first five: the cached set
        }

        /// <summary>The tip was heard before (this install).</summary>
        public bool TipSeen(string id) => SeenCached(id);   // first five: a set read once (70 tips are asked every frame)

        /// <summary>IGuide.TeachingPinch: Sunny's ghost hand shows a pinch now (look and pinch stands down).</summary>
        public bool TeachingPinch => Sunny != null && (Sunny.DemoKind == DemoKind.Pinch || Sunny.DemoKind == DemoKind.PinchPull);

        // ---------------- what Sunny watches ----------------
        // (While the instruments are moved the tour holds, so it sees none of these; the start panel's own open / close
        // happen before the tour has started.)
        void OnMenu(MenuEvent e) { if (!Arranging) Sunny.OnPanel(e.Change); }
        void OnLayout(LayoutChangedEvent e) { if (!Arranging) Sunny.OnLayoutChanged(); }
        void OnNod(NodEvent e) { if (!Arranging) Sunny.OnNod(); }

        void OnRecorder(RecorderEvent e)
        {
            Tips?.OnPlay(e);
            if (string.Equals(e.Event.type, TakeEventType.Drum, StringComparison.Ordinal) && string.Equals(e.Event.kind, "kick", StringComparison.Ordinal))
                Sunny.OnKick(e.DuringPlayback);
        }

        /// <summary>The hint as shown (tests).</summary>
        public bool LightHintShown => _lightHintShown;

        // experience.md 14 item 4: the low-light guard blocked Fast Motion Mode: one text hint per session, as soon as
        // Sunny is free (after the tour, if it is running; never on top of it).
        void OnLowLight(HandsLowLightEvent e)
        {
            if (!_lightHintShown) _lightHintPending = true;
        }

        void LightHint()
        {
            if (!_lightHintPending || !_app.Rig.HmdMounted) return;
            if (!Sunny.Hint(GuideLines.MoreLightHint, GuideTiming.HintT)) return;
            _lightHintPending = false;
            _lightHintShown = true;
            Debug.Log("[Guide] hint: " + GuideLines.MoreLightHint);
        }

        // A line is cut on session end: a line in flight stops when the headset comes off.
        void OnMount(HmdMountEvent e)
        {
            if (!e.Mounted) Sunny.StopVoice();
        }
    }
}
