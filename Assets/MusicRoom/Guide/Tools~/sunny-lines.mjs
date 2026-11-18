// Sunny's lines for the first five minutes (Docs/11-first-five/sunny-tour.md section 5.4, route B):
// the first-run script (FirstRun.cs) and every contextual tip (TourTips.cs). The Guide's 38 base lines stay in
// GuideLines.cs (CHANGED lists the base lines this table rewords). Each caption is what the bubble shows; SPOKEN is what eSpeak reads
// (only where an all-caps deck label or "hi-hat" would be read oddly). Captions name the real controls as they are
// printed or painted in the app (deck labels, menu rows). Rules (SunnyLineLintTests): at most 12 words, a clip at most
// 4 s, plain words, no brand names, every line ends with punctuation.
//
// Since the platform merge these lines are a module line file like every instrument's (Guide/Resources/GuideLines/
// FirstFive.json + VoiceManifests/FirstFive.json, merged after the Guide's 38; GuideVoice renders each lazily when first
// asked). Order: the file's (the voice slots follow it).
//
// Render: node Assets/MusicRoom/Guide/Tools~/render-voice.mjs   (needs espeak-ng; see that file)

export const CHANGED = {
  // The replay tour's intro covers more than the menu now (sunny-tour.md 5.1).
  intro_vr: "Hi, I'm Sunny! Let me show you around.",
  intro_desk: "Hi, I'm Sunny! Let me show you around.",
  // The swipe is taught when single mode first appears: "try later" made no sense there.
  swipe_skip: 'Or pick an instrument in the menu.',
  // "Move instruments" is a setting now (2026-11-10): the first tip when it turns on says how to drag.
  tip_arrange: 'Pinch an instrument and drag it where you like.',
};

export const LINES = {
  // ---- the first run (FirstRun.cs, 2026-11-04: the quick tour; nothing waits for the player) ----
  // S0 under the start panel; then the tour: Sunny on/off (shown on the menu, never changed), opening the menu, One
  // alone and My set, REC and STOP, Lessons, the drum head, goodbye. Reused lines: intro_vr / intro_desk, open_vr /
  // open_desk (the Guide's 38), tip_instruments, drum_again and bye_first below.
  start_help: "Hi, I'm Sunny! Poke a button with one finger.",
  tour_quick: "Here's a quick tour.",
  tour_sunny_off: "Don't want my tips? Turn off Tips while I play.",
  tour_sunny_on: 'Turn it back on to bring me back.',
  tour_set: 'Poke My set to bring them all back.',
  tour_rec: 'Press REC to record.',
  tour_stop: 'Press STOP to finish.',
  tour_lessons: 'Lessons are here. Pick a song and play along.',
  bye_first: "That's the tour! Find me in the menu anytime.",
  // 2026-11-16, the FAST first-timer tour (FirstRun.cs Order; user: "snow buttons/menu on hand/move intruments/intruments
  // set/recordings, and how to recenter. No song playing in this. this should be very fast."). One short line per stop;
  // the menu stop reuses open_vr / open_desk. The old quick tour's tour_set, tour_rec, tour_stop, tour_lessons and
  // bye_first above are said by nothing now (FirstRun.RetiredLines; kept until the user says to delete them);
  // tour_sunny_off / tour_sunny_on are tip_sunny_switch's.
  fast_snow: 'Flip SNOW for snow all around you. FIRE and WIND too!',
  fast_move: 'Turn on Move instruments, then pinch one and drag it.',
  fast_set: 'Poke tiles to choose My set, or try One alone.',
  fast_rec: 'Press REC to record, then PLAY to hear it.',
  fast_saved: 'Your recordings are saved here.',
  fast_recenter: 'Recenter with Reset view or pinch and hold both hands.',
  // 2026-11-18: GUIDE makes a long song now (record on top), not a one-minute loop.
  fast_bye: 'Press GUIDE to make a song with me. Have fun!',
  // Said by tips (the first run's old steps, now tips only): the skip hint, the drum, the nod, the harp, the palm.
  poke_me: 'Tip: poke me anytime to skip.',
  drum_vr: 'Now tap the drum on your right.',
  drum_again: 'Tap the top of the drum, like this.',
  nod_again: 'Nod your head, like me.',
  harp_vr: 'Now sweep your fingers across the harp strings.',
  harp_again: 'Slide your fingers through the strings, like this.',
  open_again: 'Palm toward your face, like this.',
  close_again: 'Show your palm again, like this.',
  // tip_more_instruments' two lines (the platform: point at a displayed one and pinch, or the Instruments grid).
  bye_more: 'Want more? Point at an instrument and pinch.',
  bye_more_menu: 'You can also bring them from Instruments in the menu.',

  // ---- piano, drums, harp ----
  tip_middle_c: "That's middle C. Every key shows its letter.",
  tip_sticks: 'Pinch a stick on the snare to pick it up.',
  tip_sticks_drop: 'Open your hand to put the stick back.',
  tip_hihat: 'Pinch the hi-hat stand to open it.',
  tip_choke: 'Hold a ringing cymbal to stop it.',
  tip_harp_colors: 'Red strings are C. Blue strings are F.',
  tip_harp_damp: 'Rest your hand on the strings to quiet them.',

  // ---- the side table: switches, tape deck, looper ----
  tip_switches: 'Flip these switches for fire, wind and snow.',
  // Said the first time the snow has been falling in the whole room for a few seconds (Room/Decor/RoomSnow.cs asks for
  // it): with SNOW on it snows all around the player, in the music room and in their own room.
  tip_room_snow: "It's snowing inside! Flip SNOW off any time.",
  // Said a few seconds into mixed reality while FIRE is off (Room/Decor/MrFireplace.cs asks for it, Sunny pointing at the
  // switch): in the player's own room FIRE lights a fireplace against a real wall.
  tip_mr_fire: 'Press FIRE and a fireplace lights up in your room.',
  tip_deck: 'This tape deck records what you play.',
  tip_rec: 'Recording! Play something, then press STOP.',
  tip_loop_layer: 'Press LOOP again to add a layer on top.',
  tip_loop_clear: 'Press CLEAR again to erase the loop.',
  tip_loop_tempo: 'Pick a tempo and a length before you press LOOP.',
  tip_loop_click: 'Press CLICK to keep a steady beat.',
  tip_loop_tidy: 'TIDY gently evens out your timing.',
  tip_loop_saved: 'Your loop is saved in Recordings.',
  // 2026-11-18: GUIDE starts Sunny's long song (DeckGuide.LongSong, record on top); the loop show is "Loop a beat".
  tip_deck_guide: "Press GUIDE and we'll make a magic song together.",
  // The quick show's own lines (Guide/DeckGuide.cs; the rest reuses the walkthrough's): snow on, then PLAY for the show.
  quick_snow: "Let's make magic! Flip the SNOW switch.",
  quick_play: 'Saved! Now press PLAY and watch the magic.',
  quick_wow: 'Wow! Look, your song fills the whole room.',
  // The long song's own lines (Guide/DeckGuide.cs, 2026-11-18: GUIDE and "Make a long song"): REC and a part, STOP,
  // PLAY and REC on top for the next part; the PLAY between parts reuses tip_take, the finale's wonder quick_wow.
  long_intro: "Let's make a long song, one part at a time.",
  long_rec_drums: 'Press REC, then play the drums.',
  long_rec_piano: 'Now press REC and play the piano on top.',
  long_rec_harp: 'Press REC again and add the harp on top.',
  long_stop: "Press STOP when you're done.",
  long_show: 'Now press PLAY. Sit back and watch!',
  // Said once a playback has sounded for a moment (Room/Decor/ListenBackPulse.cs asks for it): the room listens back and the
  // butterflies come (only for a playback, never while recording: 2026-11-11).
  tip_listen_back: 'Sit back and watch the butterflies dance to your song.',
  // Said once a recording's live notes have sounded for a moment (Room/Decor/ListenBackPulse.cs asks for it): the lamp,
  // fire and flowers follow what you record (the butterflies wait for the playback).
  tip_record_glow: 'Keep playing! The room comes alive while you record.',
  // Said the first time a playback brings its instruments in front of the player (Layout/LayoutModule.Stage.cs asks for it).
  // 2026-11-18: the stage spreads them out in an arc a little away from the player while it plays, then brings them back.
  tip_play_brings: 'Sit back! Your instruments step out to play for you.',
  // Said the first time the snowy window joins in with a playback, the player looking at it (Room/Decor/WindowShow.cs
  // asks for it): the snow gusts on the notes and northern lights fill the sky.
  tip_window_show: 'Even the snow and the sky dance to your song.',
  // Said the first time a playback has sent a few stars up into the sky above the room (Room/Decor/SongSky.cs asks for
  // it): every played-back note becomes a star, and the song draws its own constellation.
  tip_song_stars: 'Look up! Your song is drawing its own stars.',
  // Said the first time a playback's notes have painted the walls for a moment (Room/Decor/WallPaint.cs asks for it):
  // the song paints a nature scene (round 4: flowers on a vine, trees, birds, clouds, butterflies).
  tip_paint_walls: 'Look! Your song paints flowers, trees and birds.',
  // Said the first time a painting is about to fade after a playback (WallPaint asks for it, Keep painting off): the
  // recording's page in Recordings has the Keep painting row.
  tip_paint_keep: 'Like it? Turn on Keep painting in Recordings.',
  // Said while a later playback paints (WallPaint asks for it): a quick flick of an open hand toward a wall throws a blob
  // of paint that splats where it points.
  tip_paint_throw: 'Flick your hand at a wall to throw some paint!',
  // Said when a playback's splashes have swirled into a gold frame on a wall (WallPaint asks for it): each recording
  // becomes a framed painting in the room's gallery.
  tip_paint_gallery: 'Your song is now a painting on the wall.',
  // Said the first time ghost hands have played a playback back for a moment (Lessons/Ghost/GhostHands.cs asks for it):
  // see-through hands at each instrument play the recording's notes.
  tip_ghost_hands: 'Those are your hands from before, playing your song.',
  // Said the first time a take has played to its end (Lessons/RecordOnTopTip.cs asks for it): REC while a take plays
  // records a new part on top of it, and both play together (2026-11-17).
  tip_record_on_top: 'Press REC while it plays to add a part on top.',

  // ---- layout gestures and Move instruments ----
  swipe_again: 'Swipe fast, with your palm facing sideways.',
  tip_seat_turn: 'Pinch the air and pull sideways to turn your seat.',
  // Reset view (2026-11-10): both hands pinched and held still, or the menu's Reset view row.
  tip_summon: 'Pinch both hands and hold still to bring them back.',
  tip_reset_view: 'Or poke Reset view in the menu.',
  tip_arrange_ray: 'Far away? Point at it and pinch.',
  tip_turn: 'Turn your wrist to spin it all the way round.',
  tip_turn_two: 'Or hold it with both hands and twist.',
  // Move instruments lifts and lowers too (2026-11-11): said after tip_arrange when Move instruments first turns on.
  tip_move_height: 'Lift or lower it to your height.',
  tip_arrange_done: 'Poke Done to play again. Reset starts over.',
  tip_arrange_saved: "I'll remember this setup next time.",
  tip_panel_move: "Pinch and hold the menu's edge to move it.",
  // "Move instruments" (2026-11-10: the On / Off setting replaced the gold knobs and Lock): a pinch on an instrument
  // while it is off says where the setting is; while it is on the instruments are quiet.
  tip_move_turn_on: 'To move one, turn on Move instruments in the menu.',
  tip_move_quiet: 'Instruments are quiet while moving. Poke Done to play.',

  // ---- the menu's screens ----
  tip_scroll: 'Drag the list up or down to see more.',
  tip_back: 'Poke the title to go back.',
  tip_lessons: 'Pick an instrument, then a song.',
  tip_lesson_play: 'Play each block as it lands. It waits for you.',
  tip_lesson_kick: 'For the kick, nod your head or tap the pad.',
  tip_lesson_harp: 'Pluck the string where the block lands.',
  tip_lesson_done: 'Well done! Poke Next, or play it again.',
  tip_instruments: 'Poke One alone to play just one instrument.',
  tip_options: 'Poke Options on a tile to change how it sounds.',
  // 2026-11-10: each recording has a bin; the first poke asks ("Delete?"), a second within 3 s deletes it.
  tip_recordings: 'Poke a recording to play it. The bin deletes it.',
  tip_delete: 'Poke Delete again within three seconds to remove it.',
  tip_exit: 'Tap it again to close the app.',
  tip_hide_sunny: 'You can hide me from the menu.',

  // ---- play in my room (mixed reality) ----
  tip_mr_table: 'This is your room! The piano is on your table.',
  // The snowy window and the snow behind real windows were removed (2026-11-13: snow falls in the whole room now):
  // tip_mr_snow, tip_mr_window and tip_mr_window_move retired; these two no longer speak of windows.
  tip_mr_permission: 'Allow spatial data in settings so I can see your room.',
  tip_mr_setup: 'Set up my room, in the menu, finds your walls.',
  tip_mr_anchor: 'Your instruments always open right in front of you.',
  tip_mr_hands: 'In Instruments, poke Real hands to see your own hands.',
  tip_mr_leave: 'Music room, in the menu, takes you back.',

  // ---- reserved for the singing bowls work (TourTips tip_bowl_sing, trigger Off until that agent wires it) ----
  tip_bowl_sing: 'Rub the rim slowly and steadily to make the bowl sing.',

  // ---- look and pinch (2026-11-13, the Gaze module): said the first time a gazed menu row or instrument lights ----
  tip_look_pinch: 'Look at something and pinch to choose it.',
};

export const SPOKEN = {
  tour_rec: 'Press rec to record.',
  tour_stop: 'Press stop to finish.',
  tip_hihat: 'Pinch the hi hat stand to open it.',
  tip_rec: 'Recording! Play something, then press stop.',
  tip_loop_layer: 'Press loop again to add a layer on top.',
  tip_loop_clear: 'Press clear again to erase the loop.',
  tip_loop_tempo: 'Pick a tempo and a length before you press loop.',
  tip_loop_click: 'Press click to keep a steady beat.',
  tip_loop_tidy: 'Tidy gently evens out your timing.',
  tip_deck_guide: "Press guide and we'll make a magic song together.",
  quick_snow: "Let's make magic! Flip the snow switch.",
  quick_play: 'Saved! Now press play and watch the magic.',
  tip_room_snow: "It's snowing inside! Flip snow off any time.",
  tip_mr_fire: 'Press fire and a fireplace lights up in your room.',
  tip_record_on_top: 'Press rec while it plays to add a part on top.',
  fast_snow: 'Flip snow for snow all around you. Fire and wind too!',
  fast_rec: 'Press rec to record, then play to hear it.',
  fast_bye: 'Press guide to make a song with me. Have fun!',
  long_rec_drums: 'Press rec, then play the drums.',
  long_rec_piano: 'Now press rec and play the piano on top.',
  long_rec_harp: 'Press rec again and add the harp on top.',
  long_stop: "Press stop when you're done.",
  long_show: 'Now press play. Sit back and watch!',
};
