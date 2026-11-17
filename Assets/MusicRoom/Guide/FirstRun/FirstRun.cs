// Sunny's first run (2026-11-16, the FAST tour; user: "for first timer show sunnys tours which says about snow
// buttons/menu on hand/move intruments/intruments set/recordings, and how to recenter. No song playing in this. this
// should be very fast."). Sunny SHOWS each thing with the ghost hand and the real menu, and never waits for the player
// to do it. Each step is one to three beats; a beat is a short line (or none), a demo at its real target and, where it
// belongs, the real menu row lit by the spotlight. A beat ends when its line is over and its demo has played once; then
// the next beat or step follows. Sunny flies beside each thing it shows (beside the panel for the menu's rows, near the
// switch / the deck / the instrument otherwise, never farther than FlyMax from the head so a poke still reaches it). No
// song, lesson, note or demo music: Sunny's voice only. The whole tour takes about 35 s.
//   S0         Start      as before (below): the start panel waits for the player's answer
//   1          Intro      "Hi, I'm Sunny! Let me show you around." ("Here's a quick tour." when Sunny already helped at S0)
//   2          Snow       the ghost finger on the SNOW switch: "Flip SNOW for snow all around you. FIRE and WIND too!"
//   3          Menu       the palm demo beside Sunny, "Hold your open palm toward your face to open the menu"; then the
//                         menu opens on its home page, as the gesture does
//   4          Move       "Move instruments" lit and pressed: "Turn on Move instruments, then pinch one and drag it."; the
//                         menu closes and the ghost hand pinches the nearest instrument on display
//   5          Set        the Instruments screen: a tile pressed, "Poke tiles to choose My set, or try One alone.", then
//                         One alone pressed
//   6          Recordings the deck's REC then PLAY: "Press REC to record, then PLAY to hear it."; the menu's Recordings row
//                         lit and pressed: "Your recordings are saved here."
//   7          Recenter   Reset view lit and pressed: "Poke Reset view or pinch both hands and hold to recenter."; the menu
//                         closes and the ghost hand pinches beside Sunny
//   8          Bye        the menu back as the player had it; the ghost finger on the deck's GUIDE: "Press GUIDE for a
//                         one-minute song. Have fun!", then "Bye!"
// Nothing is ever changed: the ghost finger is not an input (no switch flips, Move instruments stays off, the layout and
// the deck untouched). A thing that is not here (no switch panel, no deck, no menu, nothing on display) is described by
// the line without pointing. The old quick tour's steps (SunnyOff, OpenMenu, Single, Record, Lessons, Drum: 2026-11-04)
// are kept below but no longer in Order (unused; TUNE-FASTTOUR.md lists them for the user's yes before any deletion).
// The old quick tour, for the record:
//   S0       Start     the start panel waits (the app needs its answer); at 8 s Sunny comes beside it, "Poke a button
//                      with one finger." and the ghost finger presses "Play here" (never answering it for the player)
//   1        Intro     "Hi, I'm Sunny! Let me show you around." ("Here's a quick tour." when Sunny already helped at S0)
//   2        SunnyOff  Sunny opens the menu on its Sunny screen and lights "Tips while I play"; the ghost finger pokes
//                      it: "Don't want my tips? Turn off Tips while I play." / "Turn it back on to bring me back." The
//                      setting is NEVER changed (the ghost finger is not an input); the menu closes again
//   3        OpenMenu  the palm demo beside Sunny, "Hold your open palm toward your face to open the menu" (desktop: "Press
//                      M ..."); then the menu opens, as the gesture does
//   4        Single    the Instruments screen: "One alone" lit and pressed ("Poke One alone to play just one
//                      instrument."), then "My set" ("Poke My set to bring them all back."); the layout is not changed
//   5        Record    the menu closes; the ghost finger presses the deck's REC ("Press REC to record."), then STOP
//                      ("Press STOP to finish."); nothing is recorded
//   6        Lessons   the menu opens on its home page, Lessons lit and pressed: "Lessons are here. Pick a song and play
//                      along."
//   7        Drum      the menu closes; two taps on the snare's head: "Tap the top of the drum, like this."
//   8        Bye       the menu goes back to how the player had it; "That's the tour! Find me in the menu anytime." and
//                      "Bye!"
// A step whose target is missing (no menu, no deck, the drums not at the seat) is passed over. Poking Sunny skips the
// step in flight (at the goodbye: Sunny leaves now). Hide Sunny (the menu's home row while Sunny is out) ends the tour.
// A row the player pokes while the tour runs ends it: the player took over (the menu stays as they have it).
// Headset off: the line is cut and the clock stops; on again the beat starts over. Sunny never speaks over a loop's
// count-in or recording. "Show me around" replays it from the intro (Replay; the start panel and TourDone untouched).
// Sunny's own menu actions are flagged (SunnyDrivesMenu) so no tip counts them as the player's.
// Plain C# on the App, ticked by GuideModule in the Guide phase before Sunny.Update. No allocation per frame.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using Aim = MusicRoom.Guide.FirstRunAim;

namespace MusicRoom.Guide
{
    /// <summary>The run's steps. Snow .. Recenter are the fast tour's (2026-11-16); SunnyOff .. Drum are the old quick tour's,
    /// kept but no longer in FirstRun.Order.</summary>
    public enum FirstRunStep : byte { Off = 0, Start, Intro, SunnyOff, OpenMenu, Single, Record, Lessons, Drum, Bye, Snow, Menu, Move, Set, Recordings, Recenter }

    /// <summary>What the ghost hand of a beat points at (tests: every target is the real thing).</summary>
    public enum FirstRunAim : byte { None = 0, Sunny, Row, DeckRec, DeckStop, Snare, Switch, DeckPlay, DeckGuide, Instrument }

    public sealed class FirstRun
    {
        public const float SunnyAfter = 8f, RedemoAfter = 8f, SkipGap = 0.35f, MrSettleMax = 6f;
        /// <summary>The least time a beat shows its demo (a press lands at about 1.6 s of its loop; the palm turns by 1 s; the
        /// strike taps twice by 1.2 s); a beat without a demo only waits for its line.</summary>
        public const float PressBeat = 2.0f, PalmBeat = 1.6f, StrikeBeat = 1.4f, OpenedBeat = 0.8f;
        /// <summary>A beat never holds longer than this (a line that never finishes): the tour always moves on.</summary>
        public const float BeatMax = 7f;
        public const float OffGazeDeg = 35f, DriftMax = 0.15f;
        public const int MaxRedemos = 2;
        /// <summary>The retired first-run song (still a hidden lesson of Lessons; the tips' signals ignore its notes).</summary>
        public const string LessonId = "piano-ode-first";
        public const string KeyTourVersion = "musicroom.tourVersion";
        public const int TourVersion = 3;

        // Lines (Guide/Tools~/sunny-lines.mjs; intro_*, open_* are the Guide's 38).
        public const string LStartHelp = "start_help", LAgain = "again", LQuick = "tour_quick",
            LSunnyOff = "tour_sunny_off", LSunnyOn = "tour_sunny_on", LOpen = "open_vr", LOpenDesk = "open_desk",
            LSingle = "tip_instruments", LSet = "tour_set", LRec = "tour_rec", LStop = "tour_stop", LLessons = "tour_lessons",
            LDrum = "drum_again", LByeFirst = "bye_first";
        /// <summary>The fast tour's own lines (2026-11-16).</summary>
        public const string LFastSnow = "fast_snow", LFastMove = "fast_move", LFastSet = "fast_set", LFastRec = "fast_rec",
            LFastSaved = "fast_saved", LFastRecenter = "fast_recenter", LFastBye = "fast_bye";
        /// <summary>tip_more_instruments' two lines (the closing invitation of the old tour, a tip now).</summary>
        public const string LineMore = "bye_more", LineMoreMenu = "bye_more_menu";

        /// <summary>The menu rows the tour lights (Menu's MenuRowIds).</summary>
        public const string RowTipsOn = "tipsOn", RowSingle = "layout.single", RowSet = "layout.all", RowLessons = "lessons";
        public const string RowMove = "arrange", RowRecordings = "recordings", RowResetView = "resetView";
        /// <summary>The SNOW lever's anchor (ITourAnchors, the side table's switch panel).</summary>
        public const string SnowSwitch = "switch.snow";
        /// <summary>A pinch closes at 0.6 s of its loop and holds to 1.6 s; a second press of a beat lands by 1.1 s.</summary>
        public const float PinchBeat = 1.6f, SecondPress = 1.1f;
        /// <summary>Sunny flies near what it shows, but stays between these distances from the head (a poke reaches it).</summary>
        public const float FlyMin = 0.42f, FlyMax = 0.72f;

        /// <summary>The steps after the start panel, in order (the fast tour, 2026-11-16).</summary>
        public static readonly FirstRunStep[] Order =
        {
            FirstRunStep.Intro, FirstRunStep.Snow, FirstRunStep.Menu, FirstRunStep.Move, FirstRunStep.Set,
            FirstRunStep.Recordings, FirstRunStep.Recenter, FirstRunStep.Bye,
        };

        /// <summary>The old quick tour's order (2026-11-04), no longer run: kept for the record until the user says to delete
        /// its steps (TUNE-FASTTOUR.md).</summary>
        public static readonly FirstRunStep[] RetiredOrder =
        {
            FirstRunStep.Intro, FirstRunStep.SunnyOff, FirstRunStep.OpenMenu, FirstRunStep.Single, FirstRunStep.Record,
            FirstRunStep.Lessons, FirstRunStep.Drum, FirstRunStep.Bye,
        };

        /// <summary>The features each step teaches (SunnyCoverageTests). The old tour's Sunny switch is a tip now
        /// (tip_sunny_switch), its piano keys tip_lesson_play's.</summary>
        public static readonly (FirstRunStep step, string[] features)[] Covers =
        {
            (FirstRunStep.Start, new[] { "start.here", "start.room", "menu.poke" }),
            (FirstRunStep.Snow, new[] { "table.switches", "room.snow" }),
            (FirstRunStep.Menu, new[] { "menu.summon" }),
            (FirstRunStep.Move, new[] { "arrange.mode", "arrange.near" }),
            (FirstRunStep.Set, new[] { "layout.single", "menu.screens" }),
            (FirstRunStep.Recordings, new[] { "deck.rec", "deck.play", "recordings.list" }),
            (FirstRunStep.Recenter, new[] { "menu.resetview", "layout.summon" }),
            (FirstRunStep.Bye, new[] { "sunny.tour", "looper.guide" }),
        };

        /// <summary>Every line the script can say (SunnyCoverageTests: each has its caption, clip and manifest entry).</summary>
        public static readonly string[] AllLines =
        {
            LStartHelp, LAgain, LQuick, LOpen, LOpenDesk,
            LFastSnow, LFastMove, LFastSet, LFastRec, LFastSaved, LFastRecenter, LFastBye,
            GuideLines.IntroVr, GuideLines.IntroDesk, GuideLines.Leave,
        };

        /// <summary>The old quick tour's own lines that nothing says any more (their clips stay until the user says to delete
        /// them: TUNE-FASTTOUR.md). tour_sunny_off / tour_sunny_on are tip_sunny_switch's now; tip_instruments and
        /// drum_again are tips' too.</summary>
        public static readonly string[] RetiredLines = { LSet, LRec, LStop, LLessons, LByeFirst };

        // (Aim: FirstRunAim, aliased at the top of the file.)

        readonly App _app;
        readonly Sunny _sunny;
        readonly Action<string> _markSeen;
        readonly Action<string, StepOutcome> _onStep;
        /// <summary>Called with true just before Sunny opens, turns or closes the menu itself and with false right after: the
        /// tips must not count it as the player's palm or poke.</summary>
        public Action<bool> SunnyDrivesMenu;

        public FirstRunStep Step { get; private set; }
        public bool Running => Step != FirstRunStep.Off;
        public bool Replay { get; private set; }
        /// <summary>S0 only: seconds of the help without an answer (the help again at 8).</summary>
        public float Idle { get; private set; }
        public float StepTime { get; private set; }
        public int Redemos { get; private set; }
        public int Skips { get; private set; }
        public bool Paused { get; private set; }
        /// <summary>Sunny is out with the player (from the 8 s help at the start panel, or the intro on).</summary>
        public bool SunnyShown { get; private set; }
        /// <summary>The demo's anchor this frame (tests).</summary>
        public DemoAnchor Anchor { get; private set; }
        /// <summary>The menu row Sunny lights now (null: none).</summary>
        public string SpotRow { get; private set; }
        /// <summary>What the beat in flight points at (tests), and the row it means to light (whether or not it is lit).</summary>
        public FirstRunAim Aiming => _beatOn ? _aim : FirstRunAim.None;
        public string SpotWanted => _beatOn ? _spot : null;
        /// <summary>Beats begun in the step in flight (tests).</summary>
        public int Beat => _beat;
        /// <summary>The player poked a menu row while the tour ran: it ended there.</summary>
        public bool PlayerTookOver { get; private set; }
        /// <summary>Each finished step, in order (tests; also published as GuideStepEvent).</summary>
        public readonly List<GuideStepEvent> Outcomes = new List<GuideStepEvent>(16);
        public float Clock { get; private set; }
        /// <summary>The start panel's time on screen with the headset on (Sunny comes at 8 s).</summary>
        public float PanelTime { get; private set; }

        // S0
        bool _startQuiet, _mrWait, _startHelped, _startDemo, _startSaid;
        float _mrT;
        // the beat
        int _beat;
        bool _beatOn, _recorded, _spotLit;
        float _beatT, _beatMin, _gap;
        string _spot;
        Aim _aim;
        DemoKind _demoKind;
        // the menu as the player had it (restored at the end)
        bool _menuSnap, _wasShown;
        MenuScreen _wasScreen;
        // signals
        bool _startAnswered, _startInRoom, _playerRow;

        readonly Action<RecorderEvent> _onPlay;
        readonly Action<MenuRowEvent> _onRow;
        readonly Action<StartChoiceEvent> _onStart;
        readonly Action<HmdMountEvent> _onMount;

        public FirstRun(App app, Sunny sunny, Action<string> markSeen, Action<string, StepOutcome> onStep = null)
        {
            _app = app;
            _sunny = sunny;
            _markSeen = markSeen;
            _onStep = onStep;
            _onPlay = OnPlay;
            _onRow = OnRow;
            _onStart = e => { _startAnswered = true; _startInRoom = e.InMyRoom; };
            _onMount = OnMount;
            app.Events.Subscribe(_onPlay);
            app.Events.Subscribe(_onRow);
            app.Events.Subscribe(_onStart);
            app.Events.Subscribe(_onMount);
        }

        public void Dispose()
        {
            _app.Events.Unsubscribe(_onPlay);
            _app.Events.Unsubscribe(_onRow);
            _app.Events.Unsubscribe(_onStart);
            _app.Events.Unsubscribe(_onMount);
        }

        // ---------------- services ----------------
        IMenuPanel Menu => _app.Get<IMenuPanel>();
        IMenuGuideHooks Hooks => _app.Get<IMenuGuideHooks>();
        ILayoutService Layout => _app.Get<ILayoutService>();
        IDrumsApi Drums => _app.Get<IDrumsApi>();
        ILooper Looper => _app.Get<ILooper>();
        ITourAnchors Anchors => _app.Get<ITourAnchors>();
        bool Desktop => !_app.IsPresenting;
        bool Hush
        {
            get
            {
                ILooper l = Looper;
                if (l == null) return false;
                LoopStatus s = l.Status;
                return s == LoopStatus.Waiting || s == LoopStatus.RecordingFirst || s == LoopStatus.Overdubbing;
            }
        }

        // =====================================================================================================

        /// <summary>The first launch: S0 waits silently under the start panel (Sunny comes at 8 s).</summary>
        public void StartFirstLaunch()
        {
            if (Running) return;
            Replay = false;
            Reset();
            Debug.Log("[Guide] first run: waiting at the start panel");
            Go(FirstRunStep.Start);
        }

        /// <summary>"Show me around": from the intro.</summary>
        public void StartReplay()
        {
            if (Running) End(false);
            Replay = true;
            Reset();
            Debug.Log("[Guide] tour replay");
            ShowSunny();
            Go(FirstRunStep.Intro);
        }

        void Reset()
        {
            Skips = 0;
            Outcomes.Clear();
            Clock = 0f;
            PanelTime = 0f;
            SunnyShown = false;
            Paused = false;
            PlayerTookOver = false;
            _playerRow = false;
            _startHelped = false;
            _menuSnap = false;
            _gap = 0f;
        }

        /// <summary>Ends the run (the goodbye, Hide Sunny, another mode taking over). The first launch counts as done
        /// (TourDone). The menu goes back to how the player had it, unless the player is using it.</summary>
        public void End(bool sayBye)
        {
            if (!Running) return;
            if (!_recorded) RecordStep(StepOutcome.Skipped);
            ClearSpot();
            RestoreMenu();
            Step = FirstRunStep.Off;
            if (SunnyShown && _sunny.State == SunnyState.Script && ReferenceEquals(_sunny.Owner, this)) _sunny.EndScript(sayBye);
            SunnyShown = false;
            SaveDone();
            Debug.Log($"[Guide] first run ended at {Clock:F1} s (skips {Skips})");
        }

        void SaveDone()
        {
            if (Replay) return;
            AppSettings s = _app.Settings;
            if (!s.TourDone) s.TourDone = true;
            s.SetInt(KeyTourVersion, TourVersion);
            s.Save();
        }

        /// <summary>A poke on Sunny: the step in flight is skipped (the next one follows 0.35 s after the bop); at the
        /// goodbye Sunny leaves now. At the start panel the help only goes quiet (the panel still waits for the player).</summary>
        public void Skip()
        {
            if (!Running) return;
            Skips++;
            _sunny.CutLine();
            Debug.Log($"[Guide] skip {Step} at {Clock:F1}");
            _app.Events.Publish(new SunnyPokeEvent { StepOrTipId = Step.ToString() });
            if (Step == FirstRunStep.Start)
            {
                // Never an answer for the player: Sunny stops pointing and keeps quiet; the panel waits.
                _startQuiet = true;
                _sunny.StopDemo();
                _startDemo = false;
                return;
            }
            RecordStep(StepOutcome.Skipped);
            if (Step == FirstRunStep.Bye) { End(true); return; }
            _gap = SkipGap;
            Go(NextOf(Step));
        }

        static FirstRunStep NextOf(FirstRunStep s)
        {
            for (int i = 0; i < Order.Length - 1; i++) if (Order[i] == s) return Order[i + 1];
            return s == FirstRunStep.Start ? FirstRunStep.Intro : FirstRunStep.Bye;
        }

        // =====================================================================================================

        /// <summary>The step can be shown here: its target exists.</summary>
        bool CanShow(FirstRunStep step)
        {
            switch (step)
            {
                case FirstRunStep.SunnyOff:
                case FirstRunStep.OpenMenu:
                case FirstRunStep.Lessons:
                    return Menu != null && Hooks != null;
                case FirstRunStep.Single:
                    return Menu != null && Hooks != null && Layout != null;
                case FirstRunStep.Record:
                {
                    ILooper l = Looper;
                    return l != null && l.TryGetControl(LoopControl.Rec, 0, out _, out _) && l.TryGetControl(LoopControl.Stop, 0, out _, out _);
                }
                case FirstRunStep.Drum:
                {
                    IDrumsApi d = Drums;
                    ILayoutService l = Layout;
                    return d?.Root != null && (l == null || l.IsInScene(InstrumentKind.Drums)) && SnareAnchor().Has;
                }
            }
            return true;
        }

        void Go(FirstRunStep step)
        {
            while (step != FirstRunStep.Start && step != FirstRunStep.Bye && !CanShow(step)) step = NextOf(step);   // passed over
            Step = step;
            StepTime = 0f;
            Idle = 0f;
            Redemos = 0;
            _recorded = false;
            _beat = 0;
            _beatOn = false;
            _beatT = 0f;
            ClearSpot();
            _aim = Aim.None;
            if (SunnyShown) _sunny.StopDemo();
            if (step == FirstRunStep.Start)
            {
                _startQuiet = _mrWait = _startDemo = _startSaid = false;
                _startAnswered = false;
                _mrT = 0f;
            }
            else ShowSunny();
        }

        void ShowSunny()
        {
            if (SunnyShown && _sunny.State == SunnyState.Script && ReferenceEquals(_sunny.Owner, this)) return;
            _sunny.BeginScript();
            _sunny.Owner = this;
            SunnyShown = true;
        }

        void RecordStep(StepOutcome outcome)
        {
            if (_recorded || Step == FirstRunStep.Off) return;
            _recorded = true;
            var e = new GuideStepEvent { StepId = Step.ToString(), Outcome = outcome, At = Clock };
            Outcomes.Add(e);
            _app.Events.Publish(e);
            _onStep?.Invoke(e.StepId, outcome);
            Debug.Log($"[Guide] step {Step}: {outcome} at {Clock:F1}");
        }

        void Say(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            ShowSunny();
            _sunny.SayScript(id, SunnyMood.Happy);
        }

        bool Quiet => SunnyShown ? _sunny.Quiet : true;

        // =====================================================================================================
        // The frame (Guide phase, before Sunny.Update)

        public void Update(float dt)
        {
            if (!Running) return;
            Clock += dt;
            if (Paused) return;
            if (SunnyShown && (_sunny.State != SunnyState.Script || !ReferenceEquals(_sunny.Owner, this)))
            {
                // Sunny was hidden or another script took it (the deck walkthrough): the run ends here.
                SunnyShown = false;
                End(false);
                return;
            }
            StepTime += dt;
            if (Step == FirstRunStep.Start) { UpdateStart(dt); return; }
            if (_playerRow)
            {
                // The player poked a menu row: the menu is theirs now, the tour ends (OnRow already let go of the menu).
                PlayerTookOver = true;
                RecordStep(StepOutcome.Done);
                End(true);
                return;
            }
            UpdatePerch();
            if (_gap > 0f) { _gap -= dt; return; }
            if (!_beatOn)
            {
                if (!Quiet || Hush) return;
                if (!BeginBeat()) StepDone();
                return;
            }
            _beatT += dt;
            Demo();
            if ((_beatT >= _beatMin && _beatT >= 0.2f && Quiet) || _beatT >= BeatMax) EndBeat();
        }

        void StepDone()
        {
            RecordStep(StepOutcome.Done);
            if (Step == FirstRunStep.Single) _markSeen?.Invoke("tip_instruments");   // its own line was just said here
            if (Step == FirstRunStep.Snow) _markSeen?.Invoke("tip_switches");        // the same switches, just shown
            if (Step == FirstRunStep.Bye) { End(true); return; }
            Go(NextOf(Step));
        }

        void EndBeat()
        {
            _beatOn = false;
            if (SunnyShown && _aim != Aim.None) _sunny.StopDemo();
            _aim = Aim.None;
        }

        // The beats of each step: what Sunny does to the menu, says, lights and shows. False: the step has no more beats.
        bool BeginBeat()
        {
            int b = _beat;
            string line = null;
            string spot = null;
            Aim aim = Aim.None;
            DemoKind kind = DemoKind.None;
            float min = 0f;
            switch (Step)
            {
                case FirstRunStep.Intro:
                    if (b > 0) return false;
                    line = !Replay && _startHelped ? LQuick : Desktop ? GuideLines.IntroDesk : GuideLines.IntroVr;
                    break;
                case FirstRunStep.SunnyOff:
                    if (b > 1) { MenuClose(); return false; }
                    if (b == 0) MenuOpen(MenuScreen.Guide);
                    spot = RowTipsOn; aim = Aim.Row; kind = DemoKind.Press; min = PressBeat;
                    line = b == 0 ? LSunnyOff : LSunnyOn;
                    break;
                case FirstRunStep.OpenMenu:
                    if (b == 0)
                    {
                        MenuClose();
                        aim = Aim.Sunny; kind = DemoKind.Palm; min = PalmBeat;
                        line = Desktop ? LOpenDesk : LOpen;
                    }
                    else if (b == 1)
                    {
                        MenuOpen(MenuScreen.Home);   // the menu opens, as the gesture does
                        min = OpenedBeat;
                    }
                    else return false;
                    break;
                case FirstRunStep.Single:
                    if (b > 1) return false;
                    if (b == 0) MenuOpen(MenuScreen.Instruments);
                    spot = b == 0 ? RowSingle : RowSet; aim = Aim.Row; kind = DemoKind.Press; min = PressBeat;
                    line = b == 0 ? LSingle : LSet;
                    break;
                case FirstRunStep.Record:
                    if (b > 1) return false;
                    if (b == 0) MenuClose();
                    aim = b == 0 ? Aim.DeckRec : Aim.DeckStop; kind = DemoKind.Press; min = PressBeat;
                    line = b == 0 ? LRec : LStop;
                    break;
                case FirstRunStep.Lessons:
                    if (b > 0) return false;
                    MenuOpen(MenuScreen.Home);
                    spot = RowLessons; aim = Aim.Row; kind = DemoKind.Press; min = PressBeat;
                    line = LLessons;
                    break;
                case FirstRunStep.Drum:
                    if (b > 0) return false;
                    MenuClose();
                    aim = Aim.Snare; kind = DemoKind.Strike; min = StrikeBeat;
                    line = LDrum;
                    break;
                // ---- the fast tour (2026-11-16) ----
                case FirstRunStep.Snow:
                    if (b > 0) return false;
                    MenuClose();
                    aim = Aim.Switch; kind = DemoKind.Press; min = PressBeat;
                    line = LFastSnow;
                    break;
                case FirstRunStep.Menu:
                    // (The next stop opens the menu on its home page, as the gesture does: no beat of its own, it is fast.)
                    if (b > 0) return false;
                    MenuClose();
                    aim = Aim.Sunny; kind = DemoKind.Palm; min = PalmBeat;
                    line = Desktop ? LOpenDesk : LOpen;
                    break;
                case FirstRunStep.Move:
                    if (b == 0)
                    {
                        MenuOpen(MenuScreen.Home);
                        spot = RowMove; aim = Aim.Row; kind = DemoKind.Press; min = PressBeat;
                        line = LFastMove;
                    }
                    else if (b == 1)
                    {
                        if (!InstrumentAnchor().Has) return false;   // nothing on display: the line said it
                        MenuClose();
                        aim = Aim.Instrument; kind = DemoKind.Pinch; min = PinchBeat;
                    }
                    else return false;
                    break;
                case FirstRunStep.Set:
                    if (b > 1) return false;
                    if (b == 0) MenuOpen(MenuScreen.Instruments);
                    spot = b == 0 ? TileRow() : RowSingle; aim = Aim.Row; kind = DemoKind.Press;
                    min = b == 0 ? PressBeat : SecondPress;
                    if (b == 0) line = LFastSet;
                    break;
                case FirstRunStep.Recordings:
                    if (b == 0)
                    {
                        MenuClose();
                        aim = Aim.DeckRec; kind = DemoKind.Press; min = PressBeat;
                        line = LFastRec;
                    }
                    else if (b == 1)
                    {
                        if (!DeckAt(LoopControl.Play, out _, out _)) { _beat = 2; return BeginBeat(); }
                        aim = Aim.DeckPlay; kind = DemoKind.Press; min = SecondPress;
                    }
                    else if (b == 2)
                    {
                        MenuOpen(MenuScreen.Home);
                        spot = RowRecordings; aim = Aim.Row; kind = DemoKind.Press; min = PressBeat;
                        line = LFastSaved;
                    }
                    else return false;
                    break;
                case FirstRunStep.Recenter:
                    if (b == 0)
                    {
                        MenuOpen(MenuScreen.Home);
                        spot = RowResetView; aim = Aim.Row; kind = DemoKind.Press; min = PressBeat;
                        line = LFastRecenter;
                    }
                    else if (b == 1)
                    {
                        if (Desktop) return false;   // a headset gesture
                        MenuClose();
                        aim = Aim.Sunny; kind = DemoKind.Pinch; min = PinchBeat;   // both hands pinched and held still
                    }
                    else return false;
                    break;
                case FirstRunStep.Bye:
                    if (b > 0) return false;
                    ClearSpot();
                    RestoreMenu();
                    aim = Aim.DeckGuide; kind = DemoKind.Press; min = PressBeat;
                    line = LFastBye;   // (the old quick tour said LByeFirst here)
                    break;
                default:
                    return false;
            }
            _beat = b + 1;
            _beatOn = true;
            _beatT = 0f;
            _beatMin = min;
            _aim = aim;
            _demoKind = kind;
            Anchor = default;   // the new target's, from Demo below (none when it is not here)
            if (spot != _spot) { ClearSpot(); _spot = spot; }
            if (line != null) Say(line);
            if (_aim != Aim.None && SunnyShown) _sunny.RestartDemo();
            Demo();
            return true;
        }

        // The demo follows its target every frame (the menu placed, an instrument moved), and the spotlight its row.
        void Demo()
        {
            if (!SunnyShown) return;
            IMenuGuideHooks hooks = Hooks;
            if (_spot != null && hooks != null)
            {
                // Every frame: the rows are laid out again on a screen change (a stale index would light another row).
                _spotLit = hooks.Spotlight(_spot);
                SpotRow = _spotLit ? _spot : null;
            }
            switch (_aim)
            {
                case Aim.None:
                    if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo();
                    break;
                case Aim.Sunny:
                    Show(_demoKind, default);
                    break;
                case Aim.Row:
                    if (hooks != null && _spot != null && hooks.TryGetRowRect(_spot, out Vector3 c, out Vector3 n, out _)) Show(_demoKind, DemoAnchor.At(c, n));
                    else if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo();
                    break;
                case Aim.DeckRec:
                case Aim.DeckStop:
                case Aim.DeckPlay:
                case Aim.DeckGuide:
                {
                    LoopControl ctl = _aim == Aim.DeckRec ? LoopControl.Rec : _aim == Aim.DeckStop ? LoopControl.Stop
                        : _aim == Aim.DeckPlay ? LoopControl.Play : LoopControl.Guide;
                    if (DeckAt(ctl, out Vector3 w, out Vector3 up)) Show(_demoKind, DemoAnchor.At(w, up));
                    else if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo();
                    break;
                }
                case Aim.Snare:
                {
                    DemoAnchor a = SnareAnchor();
                    if (a.Has) Show(_demoKind, a);
                    else if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo();
                    break;
                }
                case Aim.Switch:
                {
                    ITourAnchors anchors = Anchors;
                    if (anchors != null && anchors.TryGetAnchor(SnowSwitch, out Vector3 w, out Vector3 up)) Show(_demoKind, DemoAnchor.At(w, up));
                    else if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo();   // no switch panel here: the line says it
                    break;
                }
                case Aim.Instrument:
                {
                    DemoAnchor a = InstrumentAnchor();
                    if (a.Has) Show(_demoKind, a);
                    else if (_sunny.DemoKind != DemoKind.None) _sunny.StopDemo();
                    break;
                }
            }
        }

        bool DeckAt(LoopControl c, out Vector3 world, out Vector3 up)
        {
            world = default; up = Vector3.up;
            ILooper l = Looper;
            return l != null && l.TryGetControl(c, 0, out world, out up);
        }

        /// <summary>The ghost hand never pinches closer than this to the eyes (a kit around the seat: a farther part of it).</summary>
        public const float PinchClear = 0.35f;

        /// <summary>Where the ghost hand pinches an instrument on display (its rig in the scene and shown): the nearest point
        /// of its grab handles (the visible body Move instruments drags by: the piano's case, a drum shell, the harp's frame)
        /// to the eyes, never within PinchClear of them; the nearest such point of any instrument. Without handles, the top
        /// of the instrument's footprint (TourTipRunner's NearestInstrument anchor). None: the line describes it.</summary>
        public DemoAnchor InstrumentAnchor()
        {
            Transform head = _app.Head;
            IRigRegistry rigs = _app.Get<IRigRegistry>();
            IInstrumentRegistry reg = _app.Registry;
            ILayoutService layout = Layout;
            if (head == null || rigs == null || reg == null) return default;
            Vector3 eye = head.position;
            float min2 = PinchClear * PinchClear, bd = float.MaxValue;
            Vector3 best = default;
            bool found = false;
            for (int i = 0; i < reg.All.Count; i++)
            {
                InstrumentDescriptor d = reg.All[i];
                if (!InstrumentIds.IsPlayable(d.Kind)) continue;
                Rig rig = rigs.Get(InstrumentIds.RigOf(d.Kind));
                Transform t = rig?.Group;
                if (t == null || !t.gameObject.activeInHierarchy || !rig.InScene || (layout != null && !layout.IsInScene(d.Kind))) continue;
                IReadOnlyList<Renderer> handles = rig.Handles;
                bool any = false;
                for (int h = 0; handles != null && h < handles.Count; h++)
                {
                    Renderer r = handles[h];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    any = true;
                    Vector3 p = r.bounds.ClosestPoint(eye);
                    float d2 = (p - eye).sqrMagnitude;
                    if (d2 < min2 || d2 >= bd) continue;
                    bd = d2; best = p; found = true;
                }
                if (any) continue;
                float y = 0f;
                FootBox[] boxes = d.Seat?.Boxes;
                if (boxes != null) foreach (FootBox fb in boxes) if (!fb.Air) y = Mathf.Max(y, fb.Y1);
                y = y > 0.05f ? Mathf.Min(y, 1.3f) : 0.85f;
                Vector3 top = t.position + Vector3.up * y;
                float td2 = (top - eye).sqrMagnitude;
                if (td2 < min2 || td2 >= bd) continue;
                bd = td2; best = top; found = true;
            }
            return found ? DemoAnchor.At(best, Vector3.up) : default;
        }

        /// <summary>The Instruments screen's tile of the instrument the player has (One alone: that one; My set: its first),
        /// the row id Menu gives it ("inst." + id), made once per instrument.</summary>
        string TileRow()
        {
            ILayoutService l = Layout;
            IInstrumentRegistry reg = _app.Registry;
            if (l == null || reg == null) return RowSet;
            InstrumentKind k = l.Mode == LayoutMode.Single || l.MySet == null || l.MySet.Count == 0 ? l.Current : l.MySet[0];
            int i = (int)k;
            if (i < 0 || i >= _tileIds.Length) return RowSet;
            if (_tileIds[i] == null)
            {
                InstrumentDescriptor d = reg.Get(k);
                if (d == null || string.IsNullOrEmpty(d.Id)) return RowSet;
                _tileIds[i] = "inst." + d.Id;
            }
            return _tileIds[i];
        }

        readonly string[] _tileIds = new string[64];

        void Show(DemoKind kind, DemoAnchor anchor)
        {
            Anchor = anchor;
            _sunny.ShowDemo(kind, anchor);
        }

        void ClearSpot()
        {
            if (_spot != null || _spotLit) Hooks?.Spotlight(null);
            _spot = null;
            _spotLit = false;
            SpotRow = null;
        }

        DemoAnchor SnareAnchor()
        {
            IDrumsApi d = Drums;
            if (d == null) return default;
            IReadOnlyList<DrumPieceInfo> pieces = d.Pieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                DrumPieceInfo p = pieces[i];
                if (p.Kind != "snare" || p.Transform == null) continue;
                Vector3 eye = _app.Head != null ? _app.Head.position : p.Transform.position + Vector3.up * 0.5f;
                Vector3 toward = Vector3.ProjectOnPlane(eye - p.Transform.position, p.Transform.up);
                Vector3 at = p.Transform.position + (toward.sqrMagnitude > 1e-6f ? toward.normalized * (p.Radius * 0.35f) : Vector3.zero);
                return DemoAnchor.At(at, p.Transform.up);
            }
            return default;
        }

        // ---------------- Sunny's hands on the menu ----------------

        void SnapMenu(IMenuPanel m)
        {
            if (_menuSnap) return;
            _menuSnap = true;
            _wasShown = m.Shown && m.Screen != MenuScreen.Start;
            _wasScreen = m.Screen == MenuScreen.Start || m.Screen == MenuScreen.Arrange ? MenuScreen.Home : m.Screen;
        }

        void MenuOpen(MenuScreen screen)
        {
            IMenuPanel m = Menu;
            if (m == null) return;
            SnapMenu(m);
            SunnyDrivesMenu?.Invoke(true);
            try
            {
                if (!m.Shown) m.Show();
                if (m.Screen != screen) m.Go(screen);
            }
            finally { SunnyDrivesMenu?.Invoke(false); }
        }

        void MenuClose()
        {
            IMenuPanel m = Menu;
            if (m == null || !m.Shown) return;
            SnapMenu(m);
            ClearSpot();
            SunnyDrivesMenu?.Invoke(true);
            try { m.Hide(); }
            finally { SunnyDrivesMenu?.Invoke(false); }
        }

        /// <summary>The menu back to how the player had it before Sunny first touched it (open or closed, the same screen).</summary>
        void RestoreMenu()
        {
            if (!_menuSnap) return;
            _menuSnap = false;
            IMenuPanel m = Menu;
            if (m == null) return;
            SunnyDrivesMenu?.Invoke(true);
            try
            {
                if (_wasShown)
                {
                    if (!m.Shown) m.Show();
                    if (m.Screen != _wasScreen) m.Go(_wasScreen);
                }
                else
                {
                    if (m.Shown) m.Hide();
                    if (m.Screen != _wasScreen && m.Screen != MenuScreen.Start) m.Go(_wasScreen);
                }
            }
            finally { SunnyDrivesMenu?.Invoke(false); }
        }

        // ---------------- S0: the start panel ----------------

        void UpdateStart(float dt)
        {
            IMenuPanel menu = Menu;
            bool pending = menu != null && menu.StartPending;
            if (!pending || _startAnswered)
            {
                // Answered. Mixed reality first settles (the piano lands; the OS permission dialog stops the clock).
                if (!_mrWait)
                {
                    _mrWait = true;
                    _mrT = 0f;
                    if (SunnyShown) { _sunny.StopDemo(); _sunny.CutLine(); }
                    _startDemo = false;
                    RecordStep(StepOutcome.Done);
                    _markSeen?.Invoke("tip_poke_row");
                }
                IMixedReality mr = _app.Get<IMixedReality>();
                bool settling = _startInRoom && mr != null && (mr.IsPlacing || mr.Status == MrStatus.Loading || !mr.Active);
                if (settling && _mrT < MrSettleMax) { _mrT += dt; return; }
                Go(FirstRunStep.Intro);
                return;
            }
            bool up = menu.Shown && menu.Screen == MenuScreen.Start && _app.Rig.HmdMounted;
            if (up) PanelTime += dt;
            if (!SunnyShown)
            {
                if (PanelTime < SunnyAfter) return;
                ShowSunny();
                _startHelped = true;
                _startSaid = true;
                Idle = 0f;
                Say(LStartHelp);
                _startDemo = true;
            }
            UpdatePerch();
            if (_startDemo && !_startQuiet)
            {
                IMenuGuideHooks hooks = Hooks;
                if (hooks != null && hooks.TryGetRowRect("playHere", out Vector3 c, out Vector3 n, out _)) Show(DemoKind.Press, DemoAnchor.At(c, n));
                else _sunny.StopDemo();
            }
            if (Quiet) Idle += dt;
            if (!_startQuiet && _startSaid && Idle >= RedemoAfter)
            {
                Idle = 0f;
                if (Redemos < MaxRedemos) { Redemos++; Say(LAgain); }
                _sunny.RestartDemo();
            }
        }

        // ---------------- where Sunny floats ----------------

        void UpdatePerch()
        {
            if (!SunnyShown) return;
            IMenuPanel m = Menu;
            if (m?.Root != null && m.Shown)
            {
                // Beside the panel (the start panel or the palm menu), to its left and a little up, off its face.
                Transform r = m.Root;
                _sunny.Perch = r.position - r.right * 0.27f + Vector3.up * 0.08f - r.forward * 0.02f;
                return;
            }
            Vector3 spot = _sunny.HeadSpot();
            // The fast tour: Sunny flies near the switch, the deck or the instrument it shows (beside it, a little up and
            // toward the player), never closer than FlyMin nor farther than FlyMax from the head.
            if (Anchor.Has && _app.Head != null && _beatOn && (_aim == Aim.Switch || _aim == Aim.DeckRec || _aim == Aim.DeckPlay
                || _aim == Aim.DeckGuide || _aim == Aim.Instrument))
            {
                _sunny.Perch = ClearNear(NearTarget(Anchor.Point, _app.Head.position));
                return;
            }
            // An off-gaze target (the deck, the drums): Sunny drifts up to 15 cm toward it.
            if (Anchor.Has && _app.Head != null && (Step == FirstRunStep.Record || Step == FirstRunStep.Drum) && _aim != Aim.None)
            {
                Vector3 f = _app.Head.forward;
                Vector3 to = Anchor.Point - _app.Head.position;
                if (Vector3.Angle(new Vector3(f.x, 0f, f.z), new Vector3(to.x, 0f, to.z)) > OffGazeDeg)
                {
                    Vector3 d = Vector3.ProjectOnPlane(Anchor.Point - spot, Vector3.up);
                    if (d.sqrMagnitude > 1e-6f) spot += d.normalized * Mathf.Min(DriftMax, d.magnitude * 0.25f);
                }
            }
            _sunny.Perch = SunnyPerch.Clear(_app, spot);
        }

        /// <summary>A perch near a target, clear of every playing surface (a poke on Sunny is never a note): the goal, else a
        /// little higher (up to 36 cm), else SunnyPerch.Clear's choices.</summary>
        Vector3 ClearNear(Vector3 goal)
        {
            if (SunnyPerch.Free(_app, goal)) return goal;
            for (int k = 1; k <= 3; k++)
            {
                Vector3 up = goal + Vector3.up * (SunnyPerch.Clearance * k);
                if (SunnyPerch.Free(_app, up)) return up;
            }
            return SunnyPerch.Clear(_app, goal);
        }

        /// <summary>Where Sunny waits while it shows <paramref name="target"/>: 14 cm to its right (as the player sees it),
        /// 16 cm up and 12 cm toward the player, pulled into FlyMin..FlyMax from the head.</summary>
        public static Vector3 NearTarget(Vector3 target, Vector3 head)
        {
            Vector3 f = target - head;
            f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 p = target - f * 0.12f + right * 0.14f + Vector3.up * 0.16f;
            Vector3 d = p - head;
            float m = d.magnitude;
            if (m > FlyMax) p = head + d * (FlyMax / m);
            else if (m < FlyMin && m > 1e-4f) p = head + d * (FlyMin / m);
            return p;
        }

        // ---------------- what the run watches ----------------

        void OnPlay(RecorderEvent e)
        {
            // The player tried the drum while Sunny showed it: tip_drum is learned.
            if (!Running || e.DuringPlayback || Step != FirstRunStep.Drum) return;
            if (e.Event.type == TakeEventType.Drum && e.Event.kind != "kick") _markSeen?.Invoke("tip_drum");
        }

        void OnRow(MenuRowEvent e)
        {
            if (!Running || Step == FirstRunStep.Start || e.Confirming) return;   // the first Exit / Delete poke only asks
            _playerRow = true;
            _menuSnap = false;   // the player is using the menu: it stays as they have it (Hide Sunny ends the tour at once)
        }

        void OnMount(HmdMountEvent e)
        {
            if (!Running) return;
            if (!e.Mounted)
            {
                Paused = true;
                if (SunnyShown) _sunny.CutLine();
                return;
            }
            if (!Paused) return;
            Paused = false;
            // The beat in flight starts over from its line.
            if (Step != FirstRunStep.Start && _beatOn) { _beat = Mathf.Max(0, _beat - 1); EndBeat(); }
        }
    }
}
