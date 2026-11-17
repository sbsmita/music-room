// Every contextual tip of Sunny's tour (Docs/11-first-five/sunny-tour.md 3.2), as data: what makes it pending (the
// trigger), what it says (line ids of the voice table; captions checked against the real controls), what the ghost hand
// shows and where, what counts as doing it (success: the tip is learned even when it was never shown), its class (P1
// blocking, P2 first use, P3 discovery) and the features it teaches (TourFeatures; SunnyCoverageTests). The order is the
// Tips list's in the menu's Sunny screen (the platform's and the instruments' tips follow). TourTipSource hands them to
// the platform's TipRunner as TipSpecs (the one scheduler of every tip); TourTipRunner fires their triggers and shows them.
//
// Maintenance rule (sunny-tour.md 6.1): every new feature adds its feature id (TourFeatures), its tip here with a line
// (Guide/Tools~/sunny-lines.mjs, rendered by render-voice.mjs) and the signal it waits for (TourSignals).
using MusicRoom.Core;

namespace MusicRoom.Guide
{
    public enum TourTipClass : byte { P1 = 1, P2 = 2, P3 = 3 }

    /// <summary>What makes a tip pending (TourSignals evaluates each; Manual: only from the Tips list).</summary>
    public enum TourTrigger : byte
    {
        Manual = 0,
        MenuNeverOpened,      // after the tour: the palm never opened the menu, 20 s without playing
        MenuOpenNoPoke,       // the menu opened by the palm and not poked for 6 s
        MenuOpenLong,         // the menu open 25 s without a poke
        GazeKit, GazeHarp, GazeSwitches, GazeDeck,   // looked at (25 degrees) for 1.5 s, never played / used
        DrumHitsNoNod,        // 5 drum hits, the nod never used
        MiddleC,              // the first middle C outside lessons and the tour
        FingerDrumHits,       // 10 finger drum hits within 20 s, no stick ever held
        StickPicked,          // the first stick picked up
        HatClosedHits,        // the third hi-hat hit with the hat closed, never opened
        CymbalLeftRinging,    // a crash or ride left ringing 2 s, the second time
        HarpPlucks,           // the fifth harp pluck outside lessons and the tour
        HarpSweepRinging,     // a sweep of 6 strings left ringing 3 s, the second time
        RecordingStarted, TakeSaved,
        LoopLayerDone, LoopLayers2, LoopLayers3, LoopClearArmed, LoopSaved, LoopSavedAgain,
        DeckGuideNever,       // a take was played back and the walkthrough never ran
        SingleLayout,         // the first one-instrument layout
        HeadTurnedAway,       // the head 50 degrees off the seat for 3 s, not playing
        FarFromInstruments,   // an instrument 1.1 m from the head for 3 s, or the head 0.6 m from the seat
        GrabStarted,          // the first grab of an instrument (arrange mode or in play)
        TurnLearned,          // tip_turn was learned 5 s ago
        PalmOpenings,         // the fourth palm opening of the menu, the panel never moved
        ArrangeOn,            // arrange mode turned on
        ArrangeNoGrab,        // tip_arrange learned or 8 s without a grab, an instrument 0.8 m away
        ArrangeRelease,       // the first arrange release
        ArrangeEnded,         // the first arrange that saved a placement
        SubScreen,            // the first sub-screen shown 4 s without a poke
        ListScrolls,          // the first list that scrolls
        Screen,               // the first time screen TriggerArg is shown
        LessonStarted,        // the first lesson outside the tour
        DrumLessonKick,       // a drum lesson's kick target is waiting
        HarpLessonStarted,
        LessonDone,
        RowConfirming,        // row TriggerArg asks for its second poke (delete, exit)
        MrEntered,            // mixed reality: entered (TriggerArg "table": the piano landed on a table; "floor": not)
        MrPlacing, MrNoPermission, MrNoRooms, MrLongOrMenu,   // (MrNoWindow, MrPortalInView: the snowy window, removed 2026-11-13)
        Appended,             // said after another tip (tip_hide_sunny, tip_poke_sunny)
        Off,                  // switched off: a tip waiting on other work (tip_bowl_sing)
        OptionsChip,          // the Instruments screen shows an at-seat tile's "Options" chip (an instrument with options)
        MoreInstruments,      // 3 minutes after the tour, no instrument ever brought, one on display in the room
        MrHandsRow,           // the menu's Instruments screen in mixed reality (its bottom row shows "Real hands")
        InstrumentBodyPinch,  // a still pinch on an instrument's body with Move instruments off, which moves nothing (InstrumentBodyPinchEvent)
        MovePlayAttempt,      // Move instruments on, nothing held: a fingertip rests on a play surface for 1 s (they are quiet)
    }

    /// <summary>When a tip counts as learned (it ends early, praised; it is never offered again).</summary>
    public enum TourSuccess : byte
    {
        None = 0, MenuOpened, MenuScreenChanged, MenuClosed, DrumHit, Nod, HarpSweep, StickHeld, StickReleased, HatToggled,
        Choke, HarpDamp, Ambience, Recording, TakeStopped, TakePlaying, LayerRecording, LayerMuted, LayerUndone, LoopCleared,
        TempoChanged, MetronomeOn, PageTurn, SeatTurned, Summoned, HeldYawTurned, PanelGrabbed, ArrangeGrab,
        ArrangeEnded, Back, Scrolled, Row, KickHit, LessonHit, DeckGuideStarted, MrPlacingStarted, MrPlaced,
        Brought,              // an instrument brought to the seat from the menu, by pointing, or by turning the page
        MoveTurnedOn,         // Move instruments turned on
    }

    /// <summary>Where the ghost hand plays (TourTipRunner resolves it to a world anchor; Sunny when it cannot).</summary>
    public enum TourAnchor : byte
    {
        Sunny = 0, MiddleC, LessonTarget, Snare, Stick, HatStand, RingingCymbal, StringsMid, Strings, SwitchFire,
        DeckRec, DeckStop, DeckPlay, DeckLoop, DeckLayer1, DeckUndo, DeckClear, DeckMedium, DeckClick, DeckTidy, DeckGuide,
        Row, Header, List, NearestInstrument, FarthestInstrument, HeldRig, PanelEdge,
        DisplayedInstrument,  // the nearest instrument on display in the room (ILayoutService.TryGetDisplayPoint)
    }

    public sealed class TourTip
    {
        public string Id, Title;
        public string[] Lines = System.Array.Empty<string>();
        /// <summary>The re-demo line at 8 s (null: "again").</summary>
        public string Again;
        public TourTipClass Class = TourTipClass.P3;
        public TourTrigger Trigger;
        public string TriggerArg;
        public TourSuccess Success;
        public string SuccessArg;
        public DemoKind Demo;
        public TourAnchor Anchor;
        /// <summary>The row id for Anchor Row (Menu's MenuRowIds).</summary>
        public string AnchorArg;
        public string[] Covers = System.Array.Empty<string>();
        /// <summary>A caption only (the low-light hint): no voice.</summary>
        public string TextOnly;
        /// <summary>Said during "Move instruments" (the others wait until it is over).</summary>
        public bool InArrange;
        /// <summary>A headset gesture only (pointing at a far instrument): never on a desktop.</summary>
        public bool VrOnly;
        public override string ToString() => Id;
    }

    public static class TourTips
    {
        public const string Again = "again", PokeMe = "poke_me", HideSunny = "tip_hide_sunny", PokeSunny = "tip_poke_sunny";

        static TourTip T(string id, string title, TourTipClass c, TourTrigger trigger, TourSuccess success, DemoKind demo, TourAnchor anchor,
            string[] covers, string line = null, string again = null, string triggerArg = null, string anchorArg = null, bool inArrange = false,
            string successArg = null)
            => new TourTip
            {
                Id = id, Title = title, Class = c, Trigger = trigger, TriggerArg = triggerArg, Success = success, SuccessArg = successArg, Demo = demo, Anchor = anchor,
                AnchorArg = anchorArg, Covers = covers, Lines = new[] { line ?? id }, Again = again, InArrange = inArrange,
            };

        static string[] F(params string[] f) => f;

        /// <summary>Every tip, in the Tips list's order (sunny-tour.md 3.2).</summary>
        public static readonly TourTip[] All =
        {
            // ---- the tour's steps again (a step skipped, given up, or never done) ----
            T("tip_menu", "Open the menu", TourTipClass.P1, TourTrigger.MenuNeverOpened, TourSuccess.MenuOpened, DemoKind.Palm, TourAnchor.Sunny, F("menu.summon"), "open_vr", "open_again"),
            T("tip_poke_row", "Poke a row", TourTipClass.P2, TourTrigger.MenuOpenNoPoke, TourSuccess.MenuScreenChanged, DemoKind.Press, TourAnchor.Row, F("menu.poke"), "poke_vr", anchorArg: "lessons"),
            T("tip_close", "Close the menu", TourTipClass.P2, TourTrigger.MenuOpenLong, TourSuccess.MenuClosed, DemoKind.Palm, TourAnchor.Sunny, F("menu.close"), "close_vr", "close_again"),
            T("tip_drum", "Play the drums", TourTipClass.P3, TourTrigger.GazeKit, TourSuccess.DrumHit, DemoKind.Strike, TourAnchor.Snare, F("drums.hit"), "drum_vr", "drum_again"),
            T("tip_nod", "Nod for the kick", TourTipClass.P3, TourTrigger.DrumHitsNoNod, TourSuccess.Nod, DemoKind.Nod, TourAnchor.Sunny, F("drums.nod", "drums.pad"), "nod_vr", "nod_again"),
            T("tip_harp", "Sweep the harp", TourTipClass.P3, TourTrigger.GazeHarp, TourSuccess.HarpSweep, DemoKind.Sweep, TourAnchor.Strings, F("harp.pluck"), "harp_vr", "harp_again"),

            // ---- piano, drums, harp ----
            T("tip_middle_c", "Middle C", TourTipClass.P3, TourTrigger.MiddleC, TourSuccess.None, DemoKind.Press, TourAnchor.MiddleC, F("piano.labels")),
            T("tip_sticks", "Pick up a stick", TourTipClass.P3, TourTrigger.FingerDrumHits, TourSuccess.StickHeld, DemoKind.Pinch, TourAnchor.Stick, F("drums.sticks")),
            T("tip_sticks_drop", "Put a stick back", TourTipClass.P2, TourTrigger.StickPicked, TourSuccess.StickReleased, DemoKind.Palm, TourAnchor.Sunny, F("drums.sticks.drop")),
            T("tip_hihat", "Open the hi-hat", TourTipClass.P3, TourTrigger.HatClosedHits, TourSuccess.HatToggled, DemoKind.Pinch, TourAnchor.HatStand, F("drums.hihat")),
            T("tip_choke", "Stop a cymbal", TourTipClass.P3, TourTrigger.CymbalLeftRinging, TourSuccess.Choke, DemoKind.Hold, TourAnchor.RingingCymbal, F("drums.choke")),
            T("tip_harp_colors", "Harp colours", TourTipClass.P3, TourTrigger.HarpPlucks, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("harp.colors")),
            T("tip_harp_damp", "Quiet the harp", TourTipClass.P3, TourTrigger.HarpSweepRinging, TourSuccess.HarpDamp, DemoKind.PalmRest, TourAnchor.StringsMid, F("harp.damp")),
            new TourTip
            {
                Id = "tip_more_light", Title = "More light", Class = TourTipClass.P1, Trigger = TourTrigger.Manual, Success = TourSuccess.None,
                Covers = F("drums.fastmotion"), TextOnly = GuideLines.MoreLightHint,   // the low-light guard's own hint (GuideModule.LightHint)
            },

            // ---- the side table: switches, tape deck, looper ----
            T("tip_switches", "Fire, wind and snow", TourTipClass.P3, TourTrigger.GazeSwitches, TourSuccess.Ambience, DemoKind.Press, TourAnchor.SwitchFire, F("table.switches")),
            // Asked for by the room (Room/Decor/RoomSnow.cs) the first time the snow has fallen in the whole room a few seconds.
            T("tip_room_snow", "Snow in the room", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("room.snow")),
            // Asked for by the room (Room/Decor/MrFireplace.cs) a few seconds into mixed reality while FIRE is off; Sunny shows the press.
            T("tip_mr_fire", "A fireplace in your room", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.Press, TourAnchor.SwitchFire, F("room.mrfire")),
            T("tip_deck", "The tape deck", TourTipClass.P3, TourTrigger.GazeDeck, TourSuccess.Recording, DemoKind.Press, TourAnchor.DeckRec, F("deck.rec")),
            T("tip_rec", "Record", TourTipClass.P2, TourTrigger.RecordingStarted, TourSuccess.TakeStopped, DemoKind.Press, TourAnchor.DeckStop, F("deck.rec", "deck.stop")),
            T("tip_take", "Play it back", TourTipClass.P2, TourTrigger.TakeSaved, TourSuccess.TakePlaying, DemoKind.Press, TourAnchor.DeckPlay, F("deck.play")),
            // Asked for by the room itself (Room/Decor/ListenBackPulse.cs) once a playback has sounded for a moment.
            T("tip_listen_back", "Sit back and watch", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.listen")),
            // Asked for by the room (ListenBackPulse) once a recording's live notes have sounded for a moment.
            T("tip_record_glow", "The room comes alive", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.alive")),
            // Asked for by Layout (LayoutModule.Stage) the first time a playback brings its instruments in front of the player.
            T("tip_play_brings", "Your recording's instruments", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.bring")),
            // Asked for by the room (Room/Decor/WindowShow.cs) once a playback's northern lights are up and the player looks at a window.
            T("tip_window_show", "The window joins in", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.window")),
            // Asked for by the room (Room/Decor/SongSky.cs) once a playback has sent a few stars up into the sky.
            T("tip_song_stars", "Your song's stars", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.stars")),
            // Asked for by the room (Room/Decor/WallPaint.cs) once a playback's splashes have shown on the walls for a moment.
            T("tip_paint_walls", "The notes paint the walls", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.paint")),
            // Asked for by WallPaint when a painting is about to fade with Keep painting off (the row on a recording's page).
            T("tip_paint_keep", "Keep painting", TourTipClass.P3, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("recordings.paint")),
            // Asked for by WallPaint while a playback paints (after tip_paint_walls): flick an open hand at a wall to throw paint.
            T("tip_paint_throw", "Throw some paint", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.paintThrow")),
            // Asked for by WallPaint when a playback's splashes have swirled into a gold frame (Room/Decor/PaintGallery.cs).
            T("tip_paint_gallery", "Your song's painting", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.gallery")),
            // Lessons/Ghost/GhostHands.cs asks for it once ghost hands have played a playback for a moment.
            T("tip_ghost_hands", "Your hands from before", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("deck.ghost")),
            // Lessons/RecordOnTopTip.cs asks for it the first time a take has played to its end: REC while a take plays records on top.
            T("tip_record_on_top", "Record on top", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.Press, TourAnchor.DeckRec, F("deck.ontop")),
            T("tip_loop_start", "Start a loop", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.Press, TourAnchor.DeckLoop, F("looper.loop")),
            T("tip_loop_layer", "Add a layer", TourTipClass.P2, TourTrigger.LoopLayerDone, TourSuccess.LayerRecording, DemoKind.Press, TourAnchor.DeckLoop, F("looper.layers")),
            T("tip_loop_mute", "Mute a layer", TourTipClass.P3, TourTrigger.LoopLayers2, TourSuccess.LayerMuted, DemoKind.Press, TourAnchor.DeckLayer1, F("looper.mute")),
            T("tip_loop_undo", "Undo a layer", TourTipClass.P3, TourTrigger.LoopLayers3, TourSuccess.LayerUndone, DemoKind.Press, TourAnchor.DeckUndo, F("looper.undo")),
            T("tip_loop_clear", "Clear the loop", TourTipClass.P2, TourTrigger.LoopClearArmed, TourSuccess.LoopCleared, DemoKind.Press, TourAnchor.DeckClear, F("looper.clear")),
            T("tip_loop_tempo", "Tempo and length", TourTipClass.P3, TourTrigger.LoopSaved, TourSuccess.TempoChanged, DemoKind.Press, TourAnchor.DeckMedium, F("looper.tempo", "looper.bars")),
            T("tip_loop_click", "The click", TourTipClass.P3, TourTrigger.LoopSavedAgain, TourSuccess.MetronomeOn, DemoKind.Press, TourAnchor.DeckClick, F("looper.click")),
            T("tip_loop_tidy", "Tidy timing", TourTipClass.P3, TourTrigger.LoopSavedAgain, TourSuccess.None, DemoKind.Press, TourAnchor.DeckTidy, F("looper.tidy")),
            T("tip_loop_saved", "Saved loops", TourTipClass.P2, TourTrigger.LoopSaved, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("looper.stop", "looper.saved")),
            T("tip_deck_guide", "A song in one minute", TourTipClass.P3, TourTrigger.DeckGuideNever, TourSuccess.DeckGuideStarted, DemoKind.Press, TourAnchor.DeckGuide, F("looper.guide")),

            // ---- layout gestures ----
            T("tip_swipe", "Turn the page", TourTipClass.P2, TourTrigger.SingleLayout, TourSuccess.PageTurn, DemoKind.Swipe, TourAnchor.Sunny, F("layout.pageturn"), "swipe_vr", "swipe_again"),
            T("tip_seat_turn", "Turn your seat", TourTipClass.P3, TourTrigger.HeadTurnedAway, TourSuccess.SeatTurned, DemoKind.PinchPull, TourAnchor.Sunny, F("layout.seat")),
            // Look and pinch (2026-11-13): asked by the Gaze module the first time a gazed menu row or instrument lights.
            T("tip_look_pinch", "Look and pinch", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("gaze.pinch")),
            // Reset view (2026-11-10): both hands pinched and held still (a ring fills round them), or the menu's Reset view row.
            new TourTip
            {
                Id = "tip_summon", Title = "Reset view", Class = TourTipClass.P1, Trigger = TourTrigger.FarFromInstruments,
                Success = TourSuccess.Summoned, Demo = DemoKind.Pinch, Anchor = TourAnchor.Sunny,
                Lines = new[] { "tip_summon", "tip_reset_view" }, Covers = F("layout.summon", "mr.summon", "menu.resetview", "layout.level"),
            },
            T("tip_turn", "Spin an instrument", TourTipClass.P2, TourTrigger.GrabStarted, TourSuccess.HeldYawTurned, DemoKind.Twist, TourAnchor.HeldRig, F("layout.turn", "arrange.turn"), inArrange: true),
            T("tip_turn_two", "Twist with two hands", TourTipClass.P3, TourTrigger.TurnLearned, TourSuccess.None, DemoKind.Twist, TourAnchor.HeldRig, F("layout.twist"), inArrange: true),
            // "Move instruments" is a setting (2026-11-10, the gold knobs and Lock are gone): a still pinch on an instrument
            // while it is off moves nothing, so Sunny says where to turn it on.
            new TourTip
            {
                Id = "tip_move_turn_on", Title = "Move instruments", Class = TourTipClass.P1, Trigger = TourTrigger.InstrumentBodyPinch,
                Success = TourSuccess.MoveTurnedOn, Demo = DemoKind.None, Anchor = TourAnchor.Sunny, VrOnly = true,
                Lines = new[] { "tip_move_turn_on" }, Covers = F("arrange.mode"),
            },
            T("tip_panel_move", "Move the menu", TourTipClass.P3, TourTrigger.PalmOpenings, TourSuccess.PanelGrabbed, DemoKind.Pinch, TourAnchor.PanelEdge, F("layout.panel")),

            // ---- Move instruments ----
            // 2026-11-11: a drag lifts and lowers too ("allow it place instrument to their height/level").
            new TourTip
            {
                Id = "tip_arrange", Title = "Move instruments", Class = TourTipClass.P2, Trigger = TourTrigger.ArrangeOn,
                Success = TourSuccess.ArrangeGrab, Demo = DemoKind.Pinch, Anchor = TourAnchor.NearestInstrument, InArrange = true,
                Lines = new[] { "tip_arrange", "tip_move_height" }, Covers = F("arrange.mode", "arrange.near", "arrange.height"),
            },
            T("tip_arrange_ray", "Grab from afar", TourTipClass.P2, TourTrigger.ArrangeNoGrab, TourSuccess.ArrangeGrab, DemoKind.Point, TourAnchor.FarthestInstrument, F("arrange.ray"), inArrange: true),
            T("tip_move_quiet", "Quiet while moving", TourTipClass.P2, TourTrigger.MovePlayAttempt, TourSuccess.ArrangeEnded, DemoKind.Press, TourAnchor.Row, F("arrange.quiet"), anchorArg: "done", inArrange: true),
            T("tip_arrange_done", "Done and Reset", TourTipClass.P2, TourTrigger.ArrangeRelease, TourSuccess.ArrangeEnded, DemoKind.Press, TourAnchor.Row, F("arrange.done"), anchorArg: "done", inArrange: true),
            T("tip_arrange_saved", "Remembered spots", TourTipClass.P2, TourTrigger.ArrangeEnded, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("arrange.saved")),

            // ---- the menu's screens ----
            T("tip_back", "Go back", TourTipClass.P2, TourTrigger.SubScreen, TourSuccess.Back, DemoKind.Press, TourAnchor.Header, F("menu.back")),
            T("tip_scroll", "Scroll a list", TourTipClass.P2, TourTrigger.ListScrolls, TourSuccess.Scrolled, DemoKind.Drag, TourAnchor.List, F("menu.scroll")),
            T("tip_lessons", "Lessons", TourTipClass.P2, TourTrigger.Screen, TourSuccess.Row, DemoKind.Press, TourAnchor.Row, F("lessons.instrument", "lessons.course"), triggerArg: "Lessons", anchorArg: "inst.piano", successArg: "inst.*"),
            // A lesson's falling blocks show which key to press (piano.keys: the old quick tour's Lessons step taught it).
            T("tip_lesson_play", "Play a lesson", TourTipClass.P2, TourTrigger.LessonStarted, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("lessons.play", "lessons.stop", "piano.keys")),
            T("tip_lesson_kick", "Kick in lessons", TourTipClass.P2, TourTrigger.DrumLessonKick, TourSuccess.KickHit, DemoKind.Nod, TourAnchor.Sunny, F("lessons.kick", "drums.pad")),
            T("tip_lesson_harp", "Harp lessons", TourTipClass.P2, TourTrigger.HarpLessonStarted, TourSuccess.LessonHit, DemoKind.Press, TourAnchor.LessonTarget, F("lessons.harp")),
            T("tip_lesson_done", "After a lesson", TourTipClass.P2, TourTrigger.LessonDone, TourSuccess.None, DemoKind.Press, TourAnchor.Row, F("lessons.after"), anchorArg: "next"),
            T("tip_instruments", "Instruments", TourTipClass.P2, TourTrigger.Screen, TourSuccess.None, DemoKind.Press, TourAnchor.Row, F("layout.single"), triggerArg: "Instruments", anchorArg: "layout.piano"),
            T("tip_recordings", "Recordings", TourTipClass.P2, TourTrigger.Screen, TourSuccess.Row, DemoKind.Press, TourAnchor.Row, F("recordings.list", "recordings.take", "recordings.play"), triggerArg: "Recordings", anchorArg: "take"),
            T("tip_delete", "Delete a take", TourTipClass.P2, TourTrigger.RowConfirming, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("recordings.delete"), triggerArg: "delete"),
            T("tip_exit", "Exit", TourTipClass.P2, TourTrigger.RowConfirming, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("app.exit"), triggerArg: "exit"),
            // The Sunny screen's "Tips while I play" switch (2026-11-16: the fast first-timer tour no longer shows it, so the
            // first time the player opens the Sunny screen, Sunny lights the switch and says what it does; never changed).
            new TourTip
            {
                Id = "tip_sunny_switch", Title = "Tips while I play", Class = TourTipClass.P3, Trigger = TourTrigger.Screen, TriggerArg = "Guide",
                Success = TourSuccess.None, Demo = DemoKind.Press, Anchor = TourAnchor.Row, AnchorArg = "tipsOn",
                Lines = new[] { FirstRun.LSunnyOff, FirstRun.LSunnyOn }, Covers = F("sunny.screen", "sunny.tips"),
            },
            T(HideSunny, "Hide Sunny", TourTipClass.P3, TourTrigger.Appended, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("sunny.hide")),
            T(PokeSunny, "Skip my tips", TourTipClass.P2, TourTrigger.Appended, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("sunny.poke"), PokeMe),

            // ---- play in my room ----
            T("tip_mr_table", "Your room", TourTipClass.P2, TourTrigger.MrEntered, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("mr.enter", "mr.table"), triggerArg: "table"),
            // (tip_mr_snow, tip_mr_window, tip_mr_window_move retired 2026-11-13 with the snowy window: the snow falls in the
            // whole room now, its own tip.)
            T("tip_mr_permission", "Allow spatial data", TourTipClass.P1, TourTrigger.MrNoPermission, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("mr.permission")),
            T("tip_mr_setup", "Set up your room", TourTipClass.P1, TourTrigger.MrNoRooms, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("menu.setup")),
            T("tip_mr_anchor", "In front of you", TourTipClass.P2, TourTrigger.Manual, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("layout.front")),
            T("tip_mr_hands", "Real hands", TourTipClass.P3, TourTrigger.MrHandsRow, TourSuccess.Row, DemoKind.Press, TourAnchor.Row, F("mr.hands"), anchorArg: "hands"),
            T("tip_mr_leave", "Back to the music room", TourTipClass.P3, TourTrigger.MrLongOrMenu, TourSuccess.None, DemoKind.None, TourAnchor.Sunny, F("menu.room")),

            // ---- the other instruments (the platform: the Instruments grid, point and pinch) ----
            new TourTip
            {
                Id = "tip_more_instruments", Title = "More instruments", Class = TourTipClass.P3, Trigger = TourTrigger.MoreInstruments,
                Success = TourSuccess.Brought, Demo = DemoKind.Point, Anchor = TourAnchor.DisplayedInstrument, VrOnly = true,
                Lines = new[] { FirstRun.LineMore, FirstRun.LineMoreMenu }, Covers = F("instruments.bring"),
            },
            // Reserved for the singing bowls work (its own tips live in the bowls descriptor): Trigger Off until wired.
            T("tip_bowl_sing", "Rub slowly", TourTipClass.P2, TourTrigger.Off, TourSuccess.None, DemoKind.FingerCircle, TourAnchor.Sunny,
                F("bowls.rubSpeed")),
            T("tip_options", "Instrument options", TourTipClass.P3, TourTrigger.OptionsChip, TourSuccess.Row, DemoKind.Press, TourAnchor.Row,
                F("instruments.options"), anchorArg: "options.*", successArg: "options.*"),
        };

        public static TourTip Get(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return -1;
        }

        /// <summary>The line said again at a re-demo.</summary>
        public static string AgainOf(TourTip t) => t.Again ?? Again;
    }
}
