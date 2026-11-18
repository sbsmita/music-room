// Sunny's hands-on guides of the tape deck. Sunny perches beside the deck, out of the way of every control, points at each
// control with the ghost hand (the Press demo) and waits for the player to really do each step. Three runs share it:
//
// The long song (the deck's GUIDE button, IGuide.ToggleDeckGuide, and Sunny's menu page "Make a long song",
// IGuide.StartLongSong; 2026-11-18: "update tape deck guide sunny tour to play long songs"): record on top of a take, a
// part at a time, about a minute and a half. "Let's make a long song, one part at a time." -> REC ("Press REC, then play
// the drums.") -> the player plays (the ghost hand shows where to play when nothing comes; after PartSeconds and PartNotes
// notes "Press STOP when you're done." and the hand on STOP) -> STOP (saved) -> PLAY ("Saved! Press PLAY to hear it.") ->
// REC while it plays ("Now press REC and play the piano on top.") -> the part -> STOP (a new take with both) -> PLAY -> REC
// ("Press REC again and add the harp on top.") -> the part -> STOP -> PLAY ("Now press PLAY. Sit back and watch!"): every
// layer plays once with the playback show, a line of wonder, "Have fun!". A loop running at the start is stopped (REC
// during a loop would arm a loop layer). When the player's My set holds all three instruments, the song is made in My set
// (all three in front the whole time, nothing glides between parts); otherwise each part brings its instrument (during a
// playback through ILayoutService.Bring, so the playing instruments stay where they can). The take ending before REC: the
// hand goes back to PLAY. Waits: "Like this." after 5 s, Sunny does the step after 10 s (12 s while a part waits for its
// first note); the notes of a part are progress. Poking Sunny does the step (REC, PLAY) or ends the part (STOP).
//
// The loop show (Sunny's menu page "Loop a beat", IGuide.StartLoopShow; about a minute when the player follows promptly, for a
// first try and the demo): SNOW on (Sunny points at the side table's switch; passed over when it already snows) -> LOOP and
// a drum beat (a 2-bar loop at 90 BPM) -> LOOP again and a second instrument on top (the piano, or the harp when only the
// harp is in front) -> STOP (the loop is saved in Recordings) -> PLAY: the saved loop plays back, so the whole playback show
// runs (stars, ghost hands, paint, the room's snow), Sunny says one line of wonder and leaves with "Have
// fun!" when the playback ends or after ShowSeconds (the loop plays on until the player stops it). While the clicks wait
// for the first note, or a layer waits for its bar, the ghost hand shows where to play (a drum hit on the snare, middle C,
// a sweep of the harp). Shorter waits than the walkthrough: "Like this." after 4 s, Sunny does the step after 7 s (10 s
// while the clicks wait for the first note). The loop's tempo and length are set for the show and the player's own come
// back when no loop runs any more.
//
// The walkthrough ("Make a song in layers", IGuide.StartDeckWalkthrough; no longer a menu row since 2026-11-18: its REC /
// PLAY part is the long song's, its loop part the loop show's):
//   intro -> REC and a few notes -> STOP -> PLAY -> LOOP and a drum beat -> LOOP again and the harp -> the piano on top
//   -> mute a layer -> UNDO -> "saved in Recordings" -> keep or clear the loop -> bye.
// The rules of the main tour (Docs/11-first-five/sunny-tour.md 2.1): after 8 s without progress the demo starts
// again with "Like this."; after 15 s Sunny moves on, first doing the step itself when the next one needs it (it presses
// REC, STOP, PLAY or LOOP, saying "I'll press it for you."); poking Sunny skips the step; GUIDE again ends it.
//
// The watchdog (2026-11-16 headset test: "sunny just pauses and forget to guide on buttons. then i have to poke him"):
// whatever the player does (nothing, the wrong thing, cancelling the loop, ending a playback early, putting the instrument
// away), Sunny is never left with no line, no ghost hand and no clock. What the ghost hand shows is worked out every frame
// from the step and the deck (a cancelled loop points at LOOP again; while the clicks or an armed layer wait it shows
// where to play, in both runs); only real progress resets the wait (a loop's count-in being answered, a recording pass, the
// step's own action: notes played while nothing records are not progress); a step's line waits for Sunny to be free
// without holding the hand back; "Like this." is said once Sunny may speak, never dropped; a praise waits at most
// PraiseWaitMax; and every step ends within StepMax whatever happens (StepMaxQuick in the quick show).
//
// Both: Sunny never speaks during a loop's count-in or recording pass. In the single-instrument layout each loop step
// brings in the instrument it needs through ILayoutService (in My set too, when that instrument is not in it), and the
// layout is restored at the end; while a recording plays back (Layout's stage brought its instruments in front) the
// restore waits until the playback has ended and the stage has put the player's layout back. Ending never leaves the deck
// half done: a take still recording is stopped (and saved), a take playing stopped, a loop still waiting for its first
// note cancelled, an armed layer disarmed; a running loop keeps playing.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public enum DeckGuideStep : byte
    {
        Off = 0, Intro, Rec, Stop, Play, Drums, Harp, Piano, Mute, Undo, Saved, Keep, Bye,
        // the quick show (Bye is shared)
        Snow, Beat, Layer, Save, Watch,
        // the long song (Watch and Bye are shared)
        LongIntro, LongRec, LongPart, LongPlay,
    }

    /// <summary>Which deck guide runs: the layered walkthrough, the loop show (Quick), the long song (record on top).</summary>
    public enum DeckRun : byte { Walkthrough = 0, Loop, LongSong }

    public sealed class DeckGuide
    {
        /// <summary>The long song's waits: "Like this." after 5 s, Sunny does the step after 10 s (a part waiting for its
        /// first note: 12 s); a part asks for STOP after PartSeconds once it has PartNotes notes.</summary>
        public const float LongRedemoSeconds = 5f, LongGiveUpSeconds = 10f, LongPartGiveUpSeconds = 12f, PartSeconds = 8f;
        public const int PartNotes = 4;
        public const string LLongIntro = "long_intro", LLongRecDrums = "long_rec_drums", LLongRecPiano = "long_rec_piano",
            LLongRecHarp = "long_rec_harp", LLongStop = "long_stop", LLongShow = "long_show";
        public static readonly string[] LongLines = { LLongIntro, LLongRecDrums, LLongRecPiano, LLongRecHarp, LLongStop, LLongShow };
        /// <summary>The tip the long song teaches (Lessons/RecordOnTopTip.cs): marked heard once a part was recorded on top.</summary>
        public const string RecordOnTopTip = "tip_record_on_top";

        public const float RedemoSeconds = 8f, GiveUpSeconds = 15f, ListenSeconds = 6f, KeepSeconds = 10f;
        /// <summary>The quick show's waits: "Like this." after 4 s, Sunny does the step after 7 s (10 s while the clicks wait
        /// for the first note: the player has to reach the instrument).</summary>
        public const float QuickRedemoSeconds = 4f, QuickGiveUpSeconds = 7f, QuickNoteSeconds = 10f;
        /// <summary>The finale: the line of wonder once the instruments had a moment to arrive; Sunny leaves after ShowSeconds
        /// of playback at most (the loop plays on).</summary>
        public const float WowDelay = 1.2f, ShowSeconds = 12f;
        /// <summary>The watchdog: a step ends within this long whatever happens (the walkthrough; the quick show), a praise
        /// waits at most PraiseWaitMax for Sunny to be free, and Sunny speaks or (re)starts the ghost hand at least every
        /// NudgeLimit seconds while the run waits for the player (RedemoAt plus a frame or two; tests read it).</summary>
        public const float StepMax = 60f, StepMaxQuick = 30f, PraiseWaitMax = 4f;
        public static float NudgeLimit(bool quick) => (quick ? QuickRedemoSeconds : RedemoSeconds) + 0.5f;
        public static float NudgeLimit(DeckRun run) => (run == DeckRun.Loop ? QuickRedemoSeconds : run == DeckRun.LongSong ? LongRedemoSeconds : RedemoSeconds) + 0.5f;
        /// <summary>The quick show's loop: 2 bars at 90 BPM (a 5.3 s pass).</summary>
        public const int QuickBpm = 90, QuickBars = 2;
        /// <summary>After a playback ends, the layout is put back only once the stage had this long to bring the player's own
        /// back (Layout's StageEndGrace 1 s and its glide).</summary>
        public const float LayoutSettle = 1.6f;
        /// <summary>Notes that make "a few notes" (step REC).</summary>
        public const int FewNotes = 3;
        /// <summary>A fingertip this close to Sunny's centre pokes it (skips the step).</summary>
        public const float PokeRadius = 0.06f;
        /// <summary>Where Sunny perches, from the deck's STOP peg: to the deck's right, above it, a little toward the player.</summary>
        public const float PerchRight = 0.14f, PerchUp = 0.17f, PerchToward = 0.03f;

        public const string LIntro = "deck_guide_intro", LRec = "deck_guide_rec", LStop = "deck_guide_stop", LAuto = "deck_guide_auto",
            LSaved = "deck_guide_saved", LKeep = "deck_guide_keep", LPlay = "tip_take", LDrums = "lay_drums", LHarp = "lay_harp",
            LPiano = "lay_piano", LLayers = "lay_done", LMute = "tip_loop_mute", LUndo = "tip_loop_undo", LAgain = "again", LBye = "bye";
        /// <summary>The quick show's own lines (Tools~/sunny-lines.mjs); it reuses the walkthrough's for the rest.</summary>
        public const string LQuickSnow = "quick_snow", LQuickPlay = "quick_play", LQuickWow = "quick_wow";
        public static readonly string[] QuickLines = { LQuickSnow, LQuickPlay, LQuickWow };
        /// <summary>The snow switch Sunny points at (ITourAnchors).</summary>
        public const string SnowAnchor = "switch.snow";

        // The classic three the loop steps play (the walkthrough's song; the quick show's beat and second layer).
        static readonly InstrumentKind KDrums = InstrumentKind.Drums, KHarp = InstrumentKind.Harp, KPiano = InstrumentKind.Piano;
        /// <summary>The long song's parts, in order (the user's song: a beat, then the keys on top, then the strings).</summary>
        public static readonly InstrumentKind[] LongKinds = { KDrums, KPiano, KHarp };

        readonly App _app;
        readonly Sunny _sunny;
        readonly Action<string> _markSeen;
        IRecorder _recorder;
        ILooper _looper;
        ILayoutService _layout;

        public DeckGuideStep Step { get; private set; }
        public bool Running => Step != DeckGuideStep.Off;
        /// <summary>Which run this is.</summary>
        public DeckRun Run { get; private set; }
        /// <summary>The run is the loop show ("Loop a beat").</summary>
        public bool Quick => Run == DeckRun.Loop;
        /// <summary>The run is the long song (GUIDE, "Make a long song").</summary>
        public bool LongSong => Run == DeckRun.LongSong;
        /// <summary>The long song's part (0..2: LongKinds).</summary>
        public int Part { get; private set; }
        /// <summary>The long song: parts that ended with a new take saved (on top of the one before after the first).</summary>
        public int PartsSaved { get; private set; }
        /// <summary>The long song's part asked for STOP ("Press STOP when you're done.").</summary>
        public bool StopAsked => _stopAsked;
        /// <summary>Seconds since the last progress in this step (re-demo at 8, give up at 15; the quick show 4 and 7).</summary>
        public float Idle { get; private set; }
        /// <summary>Seconds in the current step.</summary>
        public float StepTime { get; private set; }
        public int Skips { get; private set; }
        public int GiveUps { get; private set; }
        public int Assists { get; private set; }
        /// <summary>The control the ghost hand points at (null: none).</summary>
        public LoopControl? Pointing { get; private set; }
        /// <summary>The ghost hand points at the SNOW switch (the quick show's first step).</summary>
        public bool PointingSnow { get; private set; }
        /// <summary>The quick show's ghost hand shows where to play (the clicks wait for the first note, a layer for its bar).</summary>
        public bool ShowingInstrument { get; private set; }
        /// <summary>The quick show's second layer: the piano, or the harp when only the harp is in front.</summary>
        public InstrumentKind LayerKind { get; private set; }
        /// <summary>The quick show's playback started (the finale) and the line of wonder was said.</summary>
        public bool ShowStarted => _showStarted;
        public bool WowSaid => _wowSaid;
        /// <summary>A layout restore waits for a playback (and the stage's own restore) to end.</summary>
        public bool RestorePending => _restorePending;
        /// <summary>Seconds since Sunny last said a line, (re)started the ghost hand, did a step for the player, or the player
        /// made progress (the watchdog: never more than NudgeLimit while the run waits for the player).</summary>
        public float SinceNudge { get; private set; }
        /// <summary>Lines, demos (new or restarted) and assists so far in this run (tests).</summary>
        public int Nudges { get; private set; }
        /// <summary>The instrument the ghost hand shows where to play (ShowingInstrument).</summary>
        public InstrumentKind PlayKind { get; private set; }

        float RedemoAt => Quick ? QuickRedemoSeconds : LongSong ? LongRedemoSeconds : RedemoSeconds;
        float MaxStep => Quick ? StepMaxQuick : StepMax;
        float GiveUpAt => LongSong ? (Step == DeckGuideStep.LongPart && _partNotes == 0 ? LongPartGiveUpSeconds : LongGiveUpSeconds)
            : !Quick ? GiveUpSeconds : Loop == LoopStatus.Waiting ? QuickNoteSeconds : QuickGiveUpSeconds;
        string RunName => Quick ? "quick show" : LongSong ? "long song" : "walkthrough";

        // the long song
        bool _stopAsked;
        int _partNotes, _takeCountAtPart;
        List<TakeEvent> _takeAtPart;

        // per step
        bool _said, _assisted, _redemoed, _praising, _sawState, _layoutMoved, _againOwed, _rebrought;
        int _onsets, _drumNotes, _harpNotes, _pianoNotes, _layerDone, _muted, _undone, _cleared, _countIn, _layerRec;
        string _pendingLine, _praiseLine;
        float _pendingFor, _praiseWait;
        // what the ghost hand showed last frame (a new target is a nudge)
        int _shownKey = -1;
        LayoutMode _savedMode;
        InstrumentKind _savedCurrent;
        bool _hasSavedLayout;
        // the quick show
        bool _showStarted, _showEnded, _wowSaid;
        bool _playbackOn, _restorePending;
        float _sincePlayback = 1e6f;
        LayoutMode _leftMode;
        InstrumentKind _leftCurrent;
        bool _tempoSaved, _savedClick;
        int _savedBpm, _savedBars;
        readonly Action<RecorderEvent> _onPlay;
        readonly Action<LoopEvent> _onLoop;
        readonly Action<RecordingPlaybackEvent> _onPlayback;

        public DeckGuide(App app, Sunny sunny, Action<string> markSeen)
        {
            _app = app;
            _sunny = sunny;
            _markSeen = markSeen;
            _onPlay = OnPlay;
            _onLoop = OnLoop;
            _onPlayback = OnPlayback;
            app.Events.Subscribe(_onPlay);
            app.Events.Subscribe(_onLoop);
            app.Events.Subscribe(_onPlayback);
        }

        public void Dispose()
        {
            _app.Events.Unsubscribe(_onPlay);
            _app.Events.Unsubscribe(_onLoop);
            _app.Events.Unsubscribe(_onPlayback);
        }

        IRecorder Recorder => _recorder ?? (_recorder = _app.Get<IRecorder>());
        ILooper Looper => _looper ?? (_looper = _app.Get<ILooper>());
        ILayoutService Layout => _layout ?? (_layout = _app.Get<ILayoutService>());
        LoopStatus Loop => Looper != null ? Looper.Status : LoopStatus.Off;
        bool TakeRecording => Recorder != null && Recorder.State == RecorderState.Recording && Loop == LoopStatus.Off;
        bool TakePlaying => Recorder != null && Recorder.State == RecorderState.Playing && Loop == LoopStatus.Off;
        /// <summary>A take records, on its own or on top of the one playing (the long song's part).</summary>
        bool RecordingNow => Recorder != null && Loop == LoopStatus.Off && (Recorder.State == RecorderState.Recording || Recorder.RecordingOnTop);
        /// <summary>A take plays back and nothing records on top of it.</summary>
        bool PlayingBack => TakePlaying && !Recorder.RecordingOnTop;
        bool HasTake => Recorder != null && Recorder.Take != null && Recorder.Take.Count > 0;
        /// <summary>A count-in or a recording pass: Sunny keeps quiet.</summary>
        bool Hush => Loop == LoopStatus.Waiting || Loop == LoopStatus.RecordingFirst || Loop == LoopStatus.Overdubbing;
        bool LoopStep => Step == DeckGuideStep.Drums || Step == DeckGuideStep.Harp || Step == DeckGuideStep.Piano
            || Step == DeckGuideStep.Beat || Step == DeckGuideStep.Layer;

        // =====================================================================================================

        /// <summary>The walkthrough ("Make a song in layers").</summary>
        public void Start() => Start(quick: false);

        /// <summary>Start the loop show (quick) or the walkthrough.</summary>
        public void Start(bool quick) => Start(quick ? DeckRun.Loop : DeckRun.Walkthrough);

        /// <summary>Start a run: the walkthrough, the loop show or the long song.</summary>
        public void Start(DeckRun run)
        {
            if (Running) return;
            Run = run;
            bool quick = run == DeckRun.Loop;
            ILayoutService layout = Layout;
            if (_restorePending)
            {
                // A restore still waits from the last run: this run owes it too (the first saved layout is the player's).
                _restorePending = false;
                _layoutMoved = true;
            }
            else
            {
                _hasSavedLayout = layout != null;
                if (layout != null) { _savedMode = layout.Mode; _savedCurrent = layout.Current; }
                _layoutMoved = false;
            }
            Skips = GiveUps = Assists = Nudges = 0;
            SinceNudge = 0f;
            _pendingLine = null;
            _showStarted = _showEnded = _wowSaid = false;
            Part = PartsSaved = 0;
            _takeAtPart = null;
            _takeCountAtPart = -1;
            if (quick) PrepareDeck();
            else if (run == DeckRun.LongSong) PrepareLongSong();
            _sunny.BeginScript();
            Debug.Log($"[Guide] deck {RunName} started");
            Go(quick ? DeckGuideStep.Snow : run == DeckRun.LongSong ? DeckGuideStep.LongIntro : DeckGuideStep.Intro);
        }

        // The long song starts from a free deck: a loop (waiting, armed or playing) or a take in progress is stopped (and
        // saved, as the deck's STOP does), so REC records a take and never arms a loop layer. When My set holds all three
        // parts' instruments, the song is made in My set: all three in front for the whole song.
        void PrepareLongSong()
        {
            ILooper l = Looper;
            IRecorder rec = Recorder;
            if (l != null && l.Active) l.Press(LoopControl.Stop);
            if (rec != null && rec.State != RecorderState.Idle) rec.Stop();
            ILayoutService layout = Layout;
            if (layout == null || layout.Mode == LayoutMode.All && AllInScene(layout)) return;
            IReadOnlyList<InstrumentKind> set = layout.MySet;
            if (set == null) return;
            for (int i = 0; i < LongKinds.Length; i++) if (!Contains(set, LongKinds[i])) return;
            layout.Set(LayoutMode.All, layout.Current);
            _layoutMoved = true;
            _leftMode = layout.Mode;
            _leftCurrent = layout.Current;
            Debug.Log("[Guide] the long song is made in My set (all three parts' instruments in front)");
        }

        static bool AllInScene(ILayoutService layout)
        {
            for (int i = 0; i < LongKinds.Length; i++) if (!layout.IsInScene(LongKinds[i])) return false;
            return true;
        }

        static bool Contains(IReadOnlyList<InstrumentKind> set, InstrumentKind k)
        {
            for (int i = 0; i < set.Count; i++) if (set[i] == k) return true;
            return false;
        }

        // A part's instrument in front: during a playback (the stage brought the take's instruments) it joins them as a bring
        // does (My set: it joins or replaces one; One alone: it replaces); otherwise as the other runs bring theirs.
        void BringPart(InstrumentKind kind)
        {
            ILayoutService layout = Layout;
            if (layout == null) return;
            if (layout.Mode == LayoutMode.Single ? layout.Current == kind : layout.IsInScene(kind)) return;
            if (_playbackOn && layout.Bring(kind, BringReason.Sunny))
            {
                _layoutMoved = true;
                _leftMode = layout.Mode;
                _leftCurrent = layout.Current;
                return;
            }
            Bring(kind);
        }

        // The quick show starts from a free deck (a take or loop in progress is stopped and saved, a playback stopped) with a
        // short loop: 2 bars at 90 BPM (the player's own tempo and length come back afterwards).
        void PrepareDeck()
        {
            IRecorder rec = Recorder;
            if (rec != null && rec.State != RecorderState.Idle) rec.Stop();
            ILooper l = Looper;
            if (l == null || l.Active) return;
            if (l.Bpm == QuickBpm && l.Bars == QuickBars) return;
            if (!_tempoSaved) { _tempoSaved = true; _savedBpm = l.Bpm; _savedBars = l.Bars; _savedClick = l.Metronome; }
            l.Configure(QuickBpm, QuickBars, l.Metronome);
        }

        void RestoreTempo()
        {
            ILooper l = Looper;
            if (!_tempoSaved || l == null || l.Active) return;
            _tempoSaved = false;
            l.Configure(_savedBpm, _savedBars, _savedClick);
        }

        /// <summary>GUIDE again (or Hide Sunny): the run ends now, the deck tidied, the layout restored.</summary>
        public void End(bool tidy = true)
        {
            if (!Running) return;
            if (tidy) TidyDeck();
            Finish(bye: true);
        }

        /// <summary>A poke on Sunny: the current step is skipped (done for the player when the next one needs it).</summary>
        public void Skip()
        {
            if (!Running) return;
            Skips++;
            MoveOn(assist: false);   // a skip goes on at once (it never starts a loop for the player)
        }

        void Finish(bool bye)
        {
            RestoreLayout();
            RestoreTempo();
            Pointing = null;
            PointingSnow = ShowingInstrument = false;
            Step = DeckGuideStep.Off;
            if (_sunny.State == SunnyState.Script) _sunny.EndScript(bye);
            Debug.Log($"[Guide] deck {RunName} ended (skips {Skips}, give-ups {GiveUps}, assists {Assists})");
        }

        void Go(DeckGuideStep step)
        {
            Step = step;
            Idle = StepTime = _praiseWait = 0f;
            _said = _assisted = _redemoed = _praising = _sawState = _againOwed = _rebrought = false;
            _onsets = _drumNotes = _harpNotes = _pianoNotes = _layerDone = _muted = _undone = _cleared = _countIn = _layerRec = 0;
            _stopAsked = false;
            _partNotes = 0;
            _praiseLine = null;                                // a pending "I'll press it for you." carries over
            Pointing = null;
            PointingSnow = ShowingInstrument = false;
            _shownKey = -1;
            EndDemos();
            if (step != DeckGuideStep.Off) Debug.Log($"[Guide] deck {RunName} step {step} (loop {Loop}, recorder {(Recorder != null ? Recorder.State : RecorderState.Idle)})");
            // Steps that would make no sense now are passed over at once.
            if (step == DeckGuideStep.Stop && !TakeRecording) { Go(DeckGuideStep.Play); return; }   // no take records (a loop ran instead)
            if (step == DeckGuideStep.Play && (Recorder == null || Recorder.Take == null || Recorder.Take.Count == 0)) { Go(DeckGuideStep.Drums); return; }
            if ((step == DeckGuideStep.Mute || step == DeckGuideStep.Undo) && (Looper == null || Looper.Layers == 0)) { Go(DeckGuideStep.Saved); return; }
            if (step == DeckGuideStep.Keep && (Looper == null || (!Looper.Active && Looper.Layers == 0))) { Go(DeckGuideStep.Bye); return; }
            switch (step)
            {
                case DeckGuideStep.Snow:
                    if (SnowOn) { Go(DeckGuideStep.Beat); return; }
                    if (!TrySnowSwitch(out _, out _))
                    {
                        // No switch in sight (the side table away): the snow comes on by itself, the show goes on.
                        Switches?.Set(AmbienceKind.Snow, true);
                        Go(DeckGuideStep.Beat);
                        return;
                    }
                    break;
                case DeckGuideStep.Save:
                    // STOP saves a running loop; none running: PLAY what the deck has, or nothing to show.
                    if (Loop == LoopStatus.Off) { Go(Playable ? DeckGuideStep.Watch : DeckGuideStep.Bye); return; }
                    break;
                case DeckGuideStep.Watch:
                    if (_playbackOn) { _showStarted = true; Progress(); }   // the player pressed PLAY already: the show is on
                    else if (!Playable) { Go(DeckGuideStep.Bye); return; }
                    break;
                // ---- the long song ----
                case DeckGuideStep.LongRec:
                    BringPart(LongKinds[Part]);
                    break;
                case DeckGuideStep.LongPart:
                    // REC did not start a recording (a poke with no deck, a loop in the way): this part is passed over.
                    if (!RecordingNow) { Go(Next(DeckGuideStep.LongPart)); return; }
                    _said = true;                                       // the REC line asked for the part already
                    _takeAtPart = Recorder.Take;
                    _takeCountAtPart = _takeAtPart != null ? _takeAtPart.Count : 0;
                    break;
                case DeckGuideStep.LongPlay:
                    // The next part: nothing to play over yet (no note so far) -> it records on its own; the take plays
                    // already (the player pressed PLAY) -> straight to REC.
                    Part = Mathf.Min(Part + 1, LongKinds.Length - 1);
                    if (!HasTake || PlayingBack) { Go(DeckGuideStep.LongRec); return; }
                    break;
            }
            if (step == DeckGuideStep.Drums || step == DeckGuideStep.Beat) Bring(KDrums);
            else if (step == DeckGuideStep.Harp) Bring(KHarp);
            else if (step == DeckGuideStep.Piano) Bring(KPiano);
            else if (step == DeckGuideStep.Layer) { LayerKind = PickLayer(); Bring(LayerKind); }
        }

        /// <summary>The deck has something PLAY plays: the loop just made (or the deck's last loop), else a take.</summary>
        bool Playable => (!LongSong && Looper != null && Looper.Layers > 0) || HasTake;

        string PartLine(int part) => part <= 0 ? LLongRecDrums : part == 1 ? LLongRecPiano : LLongRecHarp;

        // The second layer: the piano, unless only the harp of the two is in front in My set (nothing has to move).
        InstrumentKind PickLayer()
        {
            ILayoutService layout = Layout;
            if (layout != null && layout.Mode != LayoutMode.Single && !layout.IsInScene(KPiano) && layout.IsInScene(KHarp)) return KHarp;
            return KPiano;
        }

        string LineOf(DeckGuideStep s)
        {
            switch (s)
            {
                case DeckGuideStep.Intro: return LIntro;
                case DeckGuideStep.Rec: return LRec;
                case DeckGuideStep.Stop: return LStop;
                case DeckGuideStep.Play: return LPlay;
                case DeckGuideStep.Drums: return LDrums;
                case DeckGuideStep.Harp: return LHarp;
                case DeckGuideStep.Piano: return LPiano;
                case DeckGuideStep.Mute: return LMute;
                case DeckGuideStep.Undo: return LUndo;
                case DeckGuideStep.Saved: return LSaved;
                case DeckGuideStep.Keep: return LKeep;
                case DeckGuideStep.Snow: return LQuickSnow;
                case DeckGuideStep.Beat: return LDrums;
                case DeckGuideStep.Layer: return LayerKind == KHarp ? LHarp : LPiano;
                case DeckGuideStep.Save: return LStop;
                case DeckGuideStep.Watch: return LongSong ? LLongShow : LQuickPlay;
                case DeckGuideStep.LongIntro: return LLongIntro;
                case DeckGuideStep.LongRec: return PartLine(Part);
                case DeckGuideStep.LongPart: return _stopAsked ? LLongStop : PartLine(Part);
                case DeckGuideStep.LongPlay: return LPlay;
                default: return LBye;
            }
        }

        static LoopControl? AnchorOf(DeckGuideStep s)
        {
            switch (s)
            {
                case DeckGuideStep.Rec: case DeckGuideStep.LongRec: return LoopControl.Rec;
                case DeckGuideStep.Stop: case DeckGuideStep.Save: case DeckGuideStep.LongPart: return LoopControl.Stop;
                case DeckGuideStep.Play: case DeckGuideStep.Watch: case DeckGuideStep.LongPlay: return LoopControl.Play;
                case DeckGuideStep.Drums: case DeckGuideStep.Harp: case DeckGuideStep.Piano:
                case DeckGuideStep.Beat: case DeckGuideStep.Layer: return LoopControl.Loop;
                case DeckGuideStep.Mute: return LoopControl.Layer;
                case DeckGuideStep.Undo: return LoopControl.Undo;
                case DeckGuideStep.Keep: return LoopControl.Clear;
                default: return null;
            }
        }

        // In the single-instrument layout, the instrument a step plays is brought in (as a page turn would); in My set too
        // when it is not a member (then alone, and My set comes back at the end).
        void Bring(InstrumentKind kind)
        {
            ILayoutService layout = Layout;
            if (layout == null) return;
            if (layout.Mode == LayoutMode.Single ? layout.Current == kind : layout.IsInScene(kind)) return;
            layout.Set(LayoutMode.Single, kind);
            _layoutMoved = true;
            _leftMode = LayoutMode.Single;
            _leftCurrent = kind;
        }

        void RestoreLayout()
        {
            ILayoutService layout = Layout;
            if (layout == null || !_hasSavedLayout || !_layoutMoved) return;
            // A recording playing back has its instruments in front (Layout's stage): the player's layout comes back once
            // it ended and the stage put back what was there before it (changing it now would make the stage the player's).
            if (_playbackOn || _sincePlayback < LayoutSettle)
            {
                _restorePending = true;
                Debug.Log("[Guide] the layout comes back after the playback");
                return;
            }
            if (layout.Mode != _savedMode || layout.Current != _savedCurrent) layout.Set(_savedMode, _savedCurrent);
            _layoutMoved = false;
        }

        // Between runs: the tempo and the layout owed to the player, once no loop / playback holds them.
        void TickOwed()
        {
            if (_tempoSaved) RestoreTempo();
            if (!_restorePending) return;
            ILayoutService layout = Layout;
            if (layout == null) { _restorePending = false; return; }
            if (_playbackOn || _sincePlayback < LayoutSettle || layout.AnyMoving) return;
            _restorePending = false;
            _layoutMoved = false;
            // Only when the layout is still the one the guide left (the player may have chosen another meanwhile: theirs stays).
            if (layout.Mode != _leftMode || layout.Current != _leftCurrent) { Debug.Log("[Guide] the layout was changed meanwhile: kept"); return; }
            if (layout.Mode != _savedMode || layout.Current != _savedCurrent) layout.Set(_savedMode, _savedCurrent);
            Debug.Log("[Guide] the layout is back after the playback");
        }

        // =====================================================================================================
        // The frame (Guide phase, before Sunny.Update)
        // =====================================================================================================

        public void Update(float dt)
        {
            _sincePlayback = _playbackOn ? 0f : Mathf.Min(_sincePlayback + dt, 1e6f);
            if (!Running) { TickOwed(); return; }
            if (_sunny.State != SunnyState.Script) { Finish(bye: false); return; }   // Sunny was hidden or cut away
            StepTime += dt;
            if (!_sunny.LineBusy()) SinceNudge += dt;   // Sunny talking is guiding
            UpdatePerch();

            // "I'll press it for you." waiting for its moment (Sunny finishing a line); never over a count-in or a
            // recording pass, and dropped when the moment does not come soon. It never holds the step up.
            if (_pendingLine != null)
            {
                _pendingFor += dt;
                if (Hush || _pendingFor > 2f) _pendingLine = null;
                else if (_sunny.Quiet) { Say(_pendingLine, SunnyMood.Happy); _pendingLine = null; }
            }
            // The step is done: its praise when Sunny may speak, then the next step once Sunny is quiet again; never held up
            // longer than PraiseWaitMax (a count-in the player started meanwhile, a line that would not end): the praise is
            // dropped and the next step's ghost hand starts at once (its line follows when Sunny may speak).
            if (_praising)
            {
                _praiseWait += dt;
                if (_sunny.Quiet && !Hush)
                {
                    if (_praiseLine != null) { Say(_praiseLine, SunnyMood.Happy); _praiseLine = null; _praiseWait = 0f; return; }
                    Go(Next(Step));
                }
                else if (_praiseWait >= PraiseWaitMax) { _praiseLine = null; Go(Next(Step)); }
                return;
            }
            if (!_said)
            {
                // The player pressed PLAY before Sunny asked: the show is on, no need to ask.
                if (Step == DeckGuideStep.Watch && _showStarted) _said = true;
                else if (_sunny.Quiet && !Hush && _pendingLine == null)
                {
                    // The step's line (the ghost hand already shows the step: it never waits for the line).
                    _said = true;
                    _againOwed = false;
                    Idle = 0f;
                    Say(LineOf(Step), Step == DeckGuideStep.Intro || Step == DeckGuideStep.Saved || Step == DeckGuideStep.Snow
                        || Step == DeckGuideStep.Watch || Step == DeckGuideStep.LongIntro ? SunnyMood.Happy : SunnyMood.Calm);
                }
            }
            Idle += dt;
            Want();
            if (Check()) return;
            Point();

            // No progress for a while: the demo again, with "Like this." once Sunny may speak (never over a count-in or a
            // recording pass, and never dropped: owed until said). With nothing to show, the step's line again.
            if (!_redemoed && Idle >= RedemoAt)
            {
                _redemoed = true;
                _sunny.RestartDemo();
                Nudge();
                _againOwed = true;
            }
            else if (SinceNudge >= RedemoAt)
            {
                // The watchdog: never longer than that with nothing new from Sunny. The ghost hand again; with nothing to show,
                // the step's line again as soon as Sunny may speak.
                if (_sunny.DemoKind != DemoKind.None) { _sunny.RestartDemo(); Nudge(); }
                else _againOwed = true;
            }
            if (_againOwed && !Hush && _sunny.Quiet && _pendingLine == null)
            {
                _againOwed = false;
                bool showing = _sunny.DemoKind != DemoKind.None;
                Say(_said && showing ? LAgain : LineOf(Step));
                _said = true;
            }
            // Then move on (doing the step first when the next one needs it). StepMax: whatever happens, the step ends.
            if (Idle >= GiveUpAt || StepTime >= MaxStep)
            {
                GiveUps++;
                Debug.Log($"[Guide] deck {RunName}: {(Idle >= GiveUpAt ? "no progress for " + Idle.ToString("F0") + " s" : "step over " + MaxStep.ToString("F0") + " s")} in {Step} (loop {Loop}): moving on");
                MoveOn(assist: Idle >= GiveUpAt);   // over StepMax: on at once, without doing the step again
            }
        }

        void Say(string id, SunnyMood mood = SunnyMood.Calm)
        {
            _sunny.SayScript(id, mood);
            Nudge();
        }

        void Nudge()
        {
            Nudges++;
            SinceNudge = 0f;
        }

        // What the ghost hand shows now, from the step and the deck's state (never a stale target: a loop cancelled points at
        // LOOP again, an armed layer that recorded nothing and went away too). Pointing: a deck control; PointingSnow: the
        // SNOW switch; ShowingInstrument: where to play (PlayKind).
        void Want()
        {
            LoopStatus st = Loop;
            LoopControl? point = null;
            bool snow = false, play = false;
            InstrumentKind kind = PlayKind;
            switch (Step)
            {
                case DeckGuideStep.Rec:
                    if (st != LoopStatus.Off) point = LoopControl.Stop;                   // a loop runs instead: STOP first
                    else if (!TakeRecording) point = LoopControl.Rec;
                    else if (Idle >= RedemoAt) { play = true; kind = FrontKind(); }     // recording, nothing played: where to play
                    break;
                case DeckGuideStep.Stop: point = LoopControl.Stop; break;
                case DeckGuideStep.Play: point = _sawState ? (LoopControl?)null : LoopControl.Play; break;
                case DeckGuideStep.Drums:
                case DeckGuideStep.Harp:
                case DeckGuideStep.Piano:
                case DeckGuideStep.Beat:
                case DeckGuideStep.Layer:
                    if (st == LoopStatus.Off || st == LoopStatus.Looping) point = LoopControl.Loop;
                    else if (st == LoopStatus.Waiting || st == LoopStatus.Armed) { play = true; kind = LoopKind(Step); }
                    break;
                case DeckGuideStep.Mute: point = LoopControl.Layer; break;
                case DeckGuideStep.Undo: point = LoopControl.Undo; break;
                case DeckGuideStep.Keep: point = LoopControl.Clear; break;
                case DeckGuideStep.Snow: snow = true; break;
                case DeckGuideStep.Save: point = LoopControl.Stop; break;
                case DeckGuideStep.Watch: point = _showStarted ? (LoopControl?)null : LoopControl.Play; break;
                // ---- the long song ----
                case DeckGuideStep.LongRec:
                    if (st != LoopStatus.Off) point = LoopControl.Stop;                  // a loop in the way: STOP first
                    else if (RecordingNow) point = null;
                    else if (Part > 0 && HasTake && !PlayingBack) point = LoopControl.Play;   // the take ended: PLAY it again
                    else point = LoopControl.Rec;
                    break;
                case DeckGuideStep.LongPart:
                    if (!RecordingNow) break;
                    if (_stopAsked) point = LoopControl.Stop;
                    else if (Idle >= RedemoAt) { play = true; kind = LongKinds[Part]; }   // nothing played for a while: where to play
                    break;
                case DeckGuideStep.LongPlay: point = PlayingBack ? (LoopControl?)null : LoopControl.Play; break;
            }
            Pointing = point;
            PointingSnow = snow;
            ShowingInstrument = play;
            PlayKind = kind;
        }

        InstrumentKind LoopKind(DeckGuideStep s) =>
            s == DeckGuideStep.Drums || s == DeckGuideStep.Beat ? KDrums : s == DeckGuideStep.Harp ? KHarp : s == DeckGuideStep.Piano ? KPiano : LayerKind;

        // The instrument in front (a take records whatever the player plays): one of the three the ghost hand can show.
        InstrumentKind FrontKind()
        {
            ILayoutService layout = Layout;
            if (layout == null) return KPiano;
            if (layout.Mode == LayoutMode.Single) return layout.Current == KDrums || layout.Current == KHarp ? layout.Current : KPiano;
            return layout.IsInScene(KPiano) ? KPiano : layout.IsInScene(KDrums) ? KDrums : layout.IsInScene(KHarp) ? KHarp : KPiano;
        }

        void UpdatePerch()
        {
            ILooper l = Looper;
            if (l == null || !l.TryGetControl(LoopControl.Stop, 0, out Vector3 stop, out Vector3 up)
                || !l.TryGetControl(LoopControl.Loop, 0, out Vector3 loop, out _))
            {
                _sunny.Perch = null;
                return;
            }
            Vector3 right = stop - loop;
            right -= Vector3.Dot(right, up) * up;
            right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
            Vector3 away = Vector3.Cross(right, up);          // the deck's +z: away from the player
            Vector3 perch = stop + right * PerchRight + up * PerchUp - away * PerchToward;
            // An instrument within a foot of the player can reach over the deck's corner (the keyboard's left end): the perch
            // moves clear of every playing surface, so a poke on Sunny is never a note (SunnyPerch.Clear, the menu aside).
            _sunny.Perch = _app != null && SunnyPerch.Free(_app, perch) ? perch : _app != null ? ClearOfPlay(perch) : perch;
        }

        Vector3 ClearOfPlay(Vector3 goal)
        {
            for (int k = 1; k <= 4; k++)
            {
                Vector3 up = goal + Vector3.up * (SunnyPerch.Clearance * k);
                if (SunnyPerch.Free(_app, up)) return up;
            }
            return goal + Vector3.up * SunnyPerch.Clearance;
        }

        // The ghost hand: a deck control or the SNOW switch (the Press demo), or where to play (the quick show).
        void Point()
        {
            int key = -1;
            if (PointingSnow)
            {
                if (TrySnowSwitch(out Vector3 sw, out Vector3 swUp)) { PressAt(sw, swUp); key = 100; }
                else EndDemos();
            }
            else if (Pointing.HasValue)
            {
                if (PressControl(Pointing.Value)) key = (int)Pointing.Value;
            }
            else if (ShowingInstrument)
            {
                if (!TryPlayDemo(PlayKind, out DemoKind kind, out DemoAnchor a) && !_rebrought)
                {
                    // The instrument is not in sight (put away, the layout changed, hidden in mixed reality): brought back once.
                    _rebrought = true;
                    Bring(PlayKind);
                }
                if (TryPlayDemo(PlayKind, out kind, out a)) { _sunny.ShowDemo(kind, a); key = 200 + (int)PlayKind; }
                // Still not in sight: the hand stays at the deck (the loop's control), never gone.
                else if (PressControl(Step == DeckGuideStep.Rec || Step == DeckGuideStep.LongPart ? LoopControl.Stop : LoopControl.Loop)) key = 300;
            }
            else EndDemos();
            if (key != _shownKey)
            {
                _shownKey = key;
                if (key >= 0) Nudge();
            }
        }

        bool PressControl(LoopControl c)
        {
            ILooper l = Looper;
            if (l != null && l.TryGetControl(c, 0, out Vector3 w, out Vector3 up)) { PressAt(w, up); return true; }
            EndDemos();
            return false;
        }

        void PressAt(Vector3 w, Vector3 up)
        {
            if (_sunny.WorldDemo) _sunny.StopDemo();   // from a play demo back to the deck's press
            _sunny.ShowPress(w, up);
        }

        void EndDemos()
        {
            if (_sunny.WorldDemo) _sunny.StopDemo();
            else _sunny.EndPress();
        }

        // Where to play: a drum hit on the snare, middle C, a sweep across the harp's middle strings (only an instrument in
        // sight: in mixed reality one not on display is hidden).
        bool TryPlayDemo(InstrumentKind kind, out DemoKind demo, out DemoAnchor anchor)
        {
            demo = DemoKind.None;
            anchor = default;
            if (_app == null) return false;
            if (kind == KDrums)
            {
                IDrumsApi d = _app.Get<IDrumsApi>();
                if (d?.Pieces == null) return false;
                for (int i = 0; i < d.Pieces.Count; i++)
                {
                    DrumPieceInfo p = d.Pieces[i];
                    if (p.Kind != "snare" || p.Pad || p.Transform == null || !p.Transform.gameObject.activeInHierarchy) continue;
                    Vector3 eye = _app.Head != null ? _app.Head.position : p.Transform.position + Vector3.up;
                    Vector3 toward = Vector3.ProjectOnPlane(eye - p.Transform.position, p.Transform.up);
                    Vector3 at = p.Transform.position + (toward.sqrMagnitude > 1e-6f ? toward.normalized * (p.Radius * 0.35f) : Vector3.zero);
                    demo = DemoKind.Strike;
                    anchor = DemoAnchor.At(at, p.Transform.up);
                    return true;
                }
                return false;
            }
            if (kind == KHarp)
            {
                IHarpApi h = _app.Get<IHarpApi>();
                if (h?.Root == null || !h.Root.gameObject.activeInHierarchy || h.StringCount <= 11) return false;
                Vector3 a = h.Root.TransformPoint(h.PlayPoint(7)), b = h.Root.TransformPoint(h.PlayPoint(11));
                demo = DemoKind.Sweep;
                anchor = DemoAnchor.At((a + b) * 0.5f, -h.Root.forward, b - a);
                return true;
            }
            IPianoApi piano = _app.Get<IPianoApi>();
            if (piano?.Root == null || !piano.Root.gameObject.activeInHierarchy || !piano.TryGetKey(60, out PianoKeyInfo k)) return false;
            demo = DemoKind.Press;
            anchor = DemoAnchor.At(piano.Root.TransformPoint(k.X, 0f, 0.02f), piano.Root.up);
            return true;
        }

        IAmbienceSwitches Switches => _app?.Get<IAmbienceSwitches>();
        bool SnowOn => Switches != null && Switches.Get(AmbienceKind.Snow);

        bool TrySnowSwitch(out Vector3 world, out Vector3 up)
        {
            world = default; up = Vector3.up;
            ITourAnchors anchors = _app?.Get<ITourAnchors>();
            return anchors != null && anchors.TryGetAnchor(SnowAnchor, out world, out up);
        }

        // Progress resets the step's wait. busy: the deck or the player is at it right now (a recording pass, an armed layer
        // counting down, the show or the take playing back, notes going into a take): nothing new from Sunny is needed then.
        // A one-off event (a count-in started, CLEAR armed, a layer muted) is progress but not busy: if nothing follows,
        // Sunny shows the step again within the re-demo time.
        void Progress(bool busy = true)
        {
            Idle = 0f;
            if (busy) SinceNudge = 0f;
        }

        void Praise(bool layers = false)
        {
            Pointing = null;
            PointingSnow = ShowingInstrument = false;
            EndDemos();
            Progress();                                    // the step is done: that is progress
            _praising = true;
            _praiseWait = 0f;
            _praiseLine = layers ? LLayers : Step == DeckGuideStep.Save ? null   // the PLAY line starts with "Saved!"
                : GuideLines.Praise[(int)Step % GuideLines.Praise.Length];
        }

        DeckGuideStep Next(DeckGuideStep s)
        {
            switch (s)
            {
                case DeckGuideStep.Snow: return DeckGuideStep.Beat;
                case DeckGuideStep.Beat: return DeckGuideStep.Layer;
                case DeckGuideStep.Layer: return DeckGuideStep.Save;
                case DeckGuideStep.Save: return DeckGuideStep.Watch;
                case DeckGuideStep.Watch: return DeckGuideStep.Bye;
                case DeckGuideStep.LongIntro: return DeckGuideStep.LongRec;
                case DeckGuideStep.LongRec: return DeckGuideStep.LongPart;
                case DeckGuideStep.LongPart: return Part < LongKinds.Length - 1 ? DeckGuideStep.LongPlay : DeckGuideStep.Watch;
                case DeckGuideStep.LongPlay: return DeckGuideStep.LongRec;
            }
            return s >= DeckGuideStep.Bye ? DeckGuideStep.Off : (DeckGuideStep)((int)s + 1);
        }

        // Success tests and the progress that resets the idle clock. Returns true when the step finished this frame.
        bool Check()
        {
            switch (Step)
            {
                case DeckGuideStep.Intro:
                case DeckGuideStep.Saved:
                case DeckGuideStep.LongIntro:
                    if (_sunny.Quiet) { Go(Next(Step)); return true; }
                    return false;
                // ---- the long song ----
                case DeckGuideStep.LongRec:
                    if (RecordingNow) { Progress(); Go(DeckGuideStep.LongPart); return true; }   // REC: straight to the part
                    return false;
                case DeckGuideStep.LongPart:
                    if (!RecordingNow)
                    {
                        // STOP (or PLAY) ended the part: a new take when anything was played.
                        PartEnded();
                        Praise();
                        return true;
                    }
                    if (!_stopAsked && StepTime >= PartSeconds && _partNotes >= PartNotes && _sunny.Quiet && _pendingLine == null)
                    {
                        _stopAsked = true;
                        Say(LLongStop);
                    }
                    return false;
                case DeckGuideStep.LongPlay:
                    if (PlayingBack) { Progress(); Go(DeckGuideStep.LongRec); return true; }
                    return false;
                case DeckGuideStep.Rec:
                    if (TakeRecording && !_sawState) { _sawState = true; Pointing = null; Progress(); }
                    if (TakeRecording && _onsets >= FewNotes) { Praise(); return true; }
                    return false;
                case DeckGuideStep.Stop:
                    if (Recorder == null || !TakeRecording) { Praise(); return true; }
                    return false;
                case DeckGuideStep.Play:
                    if (TakePlaying && !_sawState) { _sawState = true; Pointing = null; Progress(); StepTime = 0f; }
                    if (_sawState && (!TakePlaying || StepTime >= ListenSeconds))
                    {
                        if (TakePlaying) Recorder.Stop();              // never left playing on
                        Praise();
                        return true;
                    }
                    if (_sawState) Progress();                         // listening counts as progress
                    return false;
                case DeckGuideStep.Drums:
                case DeckGuideStep.Harp:
                case DeckGuideStep.Piano:
                case DeckGuideStep.Beat:
                case DeckGuideStep.Layer:
                {
                    LoopStatus st = Loop;
                    // (Want: while the clicks wait for the first note, or the layer for its bar, the ghost hand shows where to
                    // play, in both runs; once the pass records, the player's hands are free of it.)
                    // An armed layer waits for its bar (at most a pass): that, and recording, are progress.
                    if (st == LoopStatus.RecordingFirst || st == LoopStatus.Overdubbing || st == LoopStatus.Armed) Progress();
                    if (_layerDone > 0)
                    {
                        Praise(layers: Step == DeckGuideStep.Piano || Step == DeckGuideStep.Layer);
                        if (Step == DeckGuideStep.Drums || Step == DeckGuideStep.Beat) _markSeen?.Invoke("tip_loop_start");
                        return true;
                    }
                    return false;
                }
                case DeckGuideStep.Mute:
                    if (_muted > 0) { Praise(); return true; }
                    return false;
                case DeckGuideStep.Undo:
                    if (_undone > 0) { Praise(); return true; }
                    return false;
                case DeckGuideStep.Keep:
                    if (_cleared > 0) { Praise(); return true; }
                    if (Idle >= KeepSeconds) { Go(DeckGuideStep.Bye); return true; }   // kept: the loop plays on
                    return false;
                case DeckGuideStep.Snow:
                    if (SnowOn) { Praise(); return true; }
                    return false;
                case DeckGuideStep.Save:
                    if (Loop == LoopStatus.Off) { Praise(); return true; }   // stopped: saved in Recordings
                    return false;
                case DeckGuideStep.Watch:
                    if (_showStarted && !_sawState) { _sawState = true; Pointing = null; EndDemos(); StepTime = 0f; }
                    if (!_sawState) return false;
                    Progress();                                        // watching counts as progress
                    if (!_wowSaid && StepTime >= WowDelay && _sunny.Quiet) { _wowSaid = true; Say(LQuickWow, SunnyMood.Happy); }
                    if ((_showEnded || StepTime >= ShowSeconds) && _sunny.Quiet && (_wowSaid || _showEnded)) { Go(Next(Step)); return true; }
                    return false;
                case DeckGuideStep.Bye:
                    if (_sunny.Quiet) { Finish(bye: false); return true; }
                    return false;
            }
            return false;
        }

        // Give up or a poke: do the step for the player where the run needs it, else just go on.
        void MoveOn(bool assist)
        {
            ILooper l = Looper;
            switch (Step)
            {
                case DeckGuideStep.Rec:
                    if (!TakeRecording && !_assisted && Loop == LoopStatus.Off) { Assisted(LoopControl.Rec); Go(DeckGuideStep.Stop); return; }
                    if (Loop == LoopStatus.Waiting) l?.Press(LoopControl.Stop);        // LOOP instead of REC: its clicks stop
                    else if (Loop == LoopStatus.Armed) l?.Press(LoopControl.Loop);
                    break;
                case DeckGuideStep.Stop:
                    if (TakeRecording) Assisted(LoopControl.Stop);
                    break;
                case DeckGuideStep.Play:
                    if (TakePlaying) Recorder.Stop();
                    break;
                case DeckGuideStep.Drums:
                case DeckGuideStep.Harp:
                case DeckGuideStep.Piano:
                case DeckGuideStep.Beat:
                case DeckGuideStep.Layer:
                {
                    LoopStatus st = Loop;
                    // Nothing started yet: LOOP for the player, once (a running loop arms its next layer), then wait again.
                    if (!_assisted && assist && (st == LoopStatus.Off || st == LoopStatus.Looping))
                    {
                        Assisted(LoopControl.Loop);
                        _assisted = true;
                        Idle = 0f;
                        _redemoed = false;
                        return;
                    }
                    if (st == LoopStatus.Waiting) l?.Press(LoopControl.Stop);       // no note came: the clicks stop
                    else if (st == LoopStatus.Armed) l?.Press(LoopControl.Loop);    // the armed layer is disarmed
                    break;
                }
                case DeckGuideStep.Snow:
                    // Waited long enough: the snow comes on for the player (a poke just goes on).
                    if (assist && !SnowOn && Switches != null) { Assists++; Switches.Set(AmbienceKind.Snow, true); SayAssist(); }
                    break;
                case DeckGuideStep.Save:
                {
                    LoopStatus st = Loop;
                    if (st == LoopStatus.Armed) l?.Press(LoopControl.Loop);          // an armed layer first goes
                    if (Loop != LoopStatus.Off) Assisted(LoopControl.Stop);          // PLAY needs the loop saved
                    break;
                }
                case DeckGuideStep.Watch:
                    // PLAY for the player, once, and the show is watched as if they had pressed it; a poke goes on.
                    if (!_showStarted && !_assisted && assist)
                    {
                        Assisted(LoopControl.Play);
                        _assisted = true;
                        Idle = 0f;
                        _redemoed = false;
                        if (_showStarted) return;
                    }
                    break;
                // ---- the long song: a poke or a give-up does the step for the player (the next one needs it) ----
                case DeckGuideStep.LongRec:
                    if (Loop != LoopStatus.Off) { l?.Press(LoopControl.Stop); break; }
                    if (!RecordingNow && !_assisted)
                    {
                        _assisted = true;
                        if (Part > 0 && HasTake && !PlayingBack) l?.Press(LoopControl.Play);   // the take first: REC goes on top
                        Assisted(LoopControl.Rec);
                    }
                    break;
                case DeckGuideStep.LongPart:
                    if (RecordingNow) Assisted(LoopControl.Stop);
                    PartEnded();
                    break;
                case DeckGuideStep.LongPlay:
                    if (!PlayingBack && HasTake) Assisted(LoopControl.Play);
                    break;
                case DeckGuideStep.Bye:
                    Finish(bye: false);
                    return;
            }
            Go(Next(Step));
        }

        void Assisted(LoopControl c)
        {
            Assists++;
            Looper?.Press(c);
            Nudge();
            SayAssist();
        }

        void SayAssist()
        {
            if (!Hush && _sunny.Quiet) Say(LAuto);
            else if (!Hush) { _pendingLine = LAuto; _pendingFor = 0f; }   // said as soon as Sunny is quiet
        }

        // A long song part ended (once per part): a new take saved means the part is in the song.
        void PartEnded()
        {
            if (_takeAtPart == null && _takeCountAtPart < 0) return;
            List<TakeEvent> take = Recorder != null ? Recorder.Take : null;
            bool saved = take != null && take.Count > 0 && (take != _takeAtPart || take.Count != _takeCountAtPart);
            _takeAtPart = null;
            _takeCountAtPart = -1;
            if (!saved) { Debug.Log($"[Guide] long song: part {Part + 1} added nothing"); return; }
            PartsSaved++;
            Debug.Log($"[Guide] long song: part {Part + 1} saved ({take.Count} events)");
            if (Part > 0) _markSeen?.Invoke(RecordOnTopTip);   // recorded on top: the tip is taught
        }

        void TidyDeck()
        {
            if (TakeRecording || TakePlaying) Recorder.Stop();
            LoopStatus st = Loop;
            if (st == LoopStatus.Waiting) Looper.Press(LoopControl.Stop);
            else if (st == LoopStatus.Armed) Looper.Press(LoopControl.Loop);
        }

        // ---------------- what the guide watches ----------------

        void OnPlay(RecorderEvent e)
        {
            if (!Running || e.DuringPlayback) return;
            string t = e.Event.type;
            bool onset = t == TakeEventType.PianoOn || t == TakeEventType.Drum || t == TakeEventType.Harp;
            if (!onset) return;
            if (Step == DeckGuideStep.Rec && TakeRecording) { _onsets++; Progress(); }
            if (Step == DeckGuideStep.LongPart && RecordingNow) { _partNotes++; Progress(); }   // the part's notes are progress
            if (t == TakeEventType.Drum) _drumNotes++;
            else if (t == TakeEventType.Harp) _harpNotes++;
            else _pianoNotes++;
            // Notes are progress in a loop step only while they record (the count-in answered, a pass): played while the step
            // waits for LOOP they are not, so Sunny shows LOOP again and moves on as it should (they kept it silent before).
            if (LoopStep && (Loop == LoopStatus.RecordingFirst || Loop == LoopStatus.Overdubbing)) Progress();
        }

        void OnLoop(LoopEvent e)
        {
            if (!Running) return;
            switch (e.Phase)
            {
                case LoopEventPhase.CountIn: _countIn++; Progress(false); break;
                case LoopEventPhase.Recording: _layerRec++; Progress(); break;
                case LoopEventPhase.LayerDone: if (e.Layer >= 0) { _layerDone++; Progress(false); } break;
                case LoopEventPhase.Muted: _muted++; Progress(false); break;
                case LoopEventPhase.Undo: _undone++; Progress(false); break;
                case LoopEventPhase.ClearArmed: Progress(false); break;
                case LoopEventPhase.Cleared: _cleared++; Progress(false); break;
            }
        }

        // A recording playing back (the deck's PLAY of the loop just made: the playback show runs on these).
        void OnPlayback(RecordingPlaybackEvent e)
        {
            _playbackOn = e.Started;
            if (e.Started) _sincePlayback = 0f;
            if (!Running || Step != DeckGuideStep.Watch) return;
            if (e.Started) { _showStarted = true; Progress(); }
            else if (_showStarted) _showEnded = true;
        }
    }
}
