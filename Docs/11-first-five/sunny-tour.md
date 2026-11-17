# 11 · First Five Minutes: Sunny's guided tour and contextual tips (implementation-ready spec)

> **Update 2026-11-16 (the fast tour).** The first run after the start panel is now the FAST first-timer tour
> (`Guide/FirstRun/FirstRun.cs`, `FirstRun.Order`): Intro, the SNOW / FIRE / WIND switches, the palm menu, Move
> instruments, the instrument set (My set / One alone), REC / PLAY and Recordings, Reset view and the both-hands hold,
> goodbye at GUIDE; one short line each, about 35-40 s, no song or lesson. The guided steps described below (and the
> 2026-11-04 quick tour after them) are history; the contextual tips are unchanged, plus `tip_sunny_switch` (the Sunny
> screen's "Tips while I play", which the tour used to show). Notes: `music-room-vr-wip/unity-stage-tune/TUNE-FASTTOUR.md`.

Key: `first-five/sunny`. Target: this Unity project (Unity 6000.3.27f1,
Meta XR SDK 207). This is a spec only. Nothing in any project was changed to write it. Clip lengths were measured by
rendering every proposed line with the project's own voice pipeline into a scratch folder (section 5.4).

**Sources read** (2026-11-01, about 03:50). The clones were read-only and several are still in progress.

| Where | What it gave |
|---|---|
| `Assets/MusicRoom/Guide` (`GuideModule`, `Sunny`, `SunnyDemo`, `SunnyAdapters`, `GuideLines`, `Resources/guide-lines.json`, `Resources/Voice/*.wav` + `manifest.json`, README, CONTRACT-REQUESTS) | Today's tour: intro, then open, poke, close, swipe and nod, then bye. 22 lines. Sunny has **no hit test**, so poking it does nothing today. (An earlier tour had "poke to repeat"; the current one no longer has it.) |
| `Menu` (README, `MenuRow`, `MenuStyle`, `MenuController`) | Screens, rows, pokes, hover-highlight uniforms |
| `Layout` (README) | Grab (pinch and hold for 0.3 s), turns, two-hand summon (0.8 s), seat swivel (14 cm per 45°), page-turn sweep |
| `Lessons` (README, `Resources/lessons.json`) | Wait-mode tutorial. `piano-ode` = 15 notes on C4..G4, 108 BPM |
| `MR` (README, `MrConstants`) | Enter / place / snow portals / manual window and the text-card hints |
| `Audio` (`GuideVoice`, `AudioEngine`) | `GuideVoice.LineIds` is hard-coded (22). `VoiceSlot0 = 200`, `SlotCount = 240`. `ChirpKind { Open, Close }` |
| Project `README.md` | The controls table |
| `Docs/08-ux-lessons-menu-guide.md`, `ARCHITECTURE.md` (sections 4, 8–12) | Contracts, frame order, homes. Seat frame: piano ahead, kit right, harp and side table left |
| Work copy `u-arrange` (in progress, files written 03:25–03:43) | `Core/Contracts/Arrange.cs` (`IArrangeMode`, `ArrangeModeEvent`, `ArrangeConstants`), `MenuScreen.Start` and `Arrange`, `IMenuPanel.StartPending` ("Sunny's first-launch tour waits for it"), `App.Quit`, the grab pinch (thumb-index OR Meta's index pinch, 0.85 / 0.5 hysteresis), `GrabStill` 0.04. Menu work so far: home = Lessons, Instruments, **Move instruments** ("Done moving" while on), Recordings, (Play in my room), (Set up my room), Show me around, **Exit** ("Tap again to exit", 3 s). Start panel "VR Music Room" / "Where would you like to play?" / "Play here · Music room" / "Play in my room · Passthrough" / footer "Later: show your palm to open the menu", 0.55 m ahead, 0.06 m below the eyes. Done / Reset buttons 0.16 × 0.12 m, 0.35 m ahead, 0.15 m below, 0.10 m left. **Not yet written:** any Guide change, including "the updated Sunny line about moving instruments" |
| Work copy `u-engine` (and the u-piano, u-drums, u-harp, u-room copies) | Instruments screen row **Classic sound** ("On: the original" / "Off: the new sound" / "Preparing…"), `IAudioEngine.ClassicSound` |
| Work copies `u-exp-hands`, `u-exp-world` | E1 fingers stop on keys and the menu, E2 sticks bounce, E3 contact shadows, E4 Fast Motion Mode while drumming plus a low-light guard (`FastMotionPolicy.Blocked`), E5 90 Hz governor, E6 skin hands in VR and real hands in MR, E7 cymbal motion, E8 harp string blur, E9 MR piano spatial anchor (`PianoAnchorStore`), passthrough on demand (4 s wait) |
| Work copy `u-looper` (created 03:35, **no looper code yet**) | Coordinator's brief only: LOOP with a one-bar count-in, 70 / 90 / 110 BPM presets, 2 or 4 bars, up to 6 overdub layers, per-layer mute lights, UNDO, CLEAR (tap again), STOP, optional metronome, loops saved to Recordings, one first-use Sunny line on the first LOOP press |
| `Docs/10-upgrade/*.md` | experience.md 14.4 asks for one new guide line, "More light helps your hands", shown when `FastMotionPolicy.Blocked`. E10 warms up shaders during Sunny's intro |
| The voice render script (`make-voice.mjs`, `render()`), the line table, `export-voice-wav.mjs`, `export-guide-lines.mjs` | The voice pipeline. eSpeak NG 1.52.0, `en-us+Annie`, 150 wpm, pitch 55, range 70, then trim, peak 0.89 and 5 ms fades. The robot effect is added in the app, not in the files |

**The user's decisions this spec follows**
- Poking Sunny **skips** the current step, at any time.
- Sunny tours or tips **every** feature, and stays current as features are added.
- The agreed first-run flow: start panel → fit → glowing key → Ode to Joy → room celebration → drum + nod → harp sweep →
  palm menu and its contents → Sunny leaves at about 3:30.
- No dead ends: an 8 s re-demo, and a give-up after about 15 s.
- Every other gesture is taught in context the first time it matters.
- Calm and realistic: no confetti, no glowing pop-ups.
- Lines: English, under about 12 words, plain words for non-musicians, no brand names. Hands only, from start to end.

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions in this spec (summary)

1. **The first run is one guided script, `FirstRun`, of 12 steps (S0–S11).** Its spine is "play music first, learn the
   menu last":
   - first piano sound in about 6 s for a prompt player (under 10 s);
   - a whole song (the answering phrase of Ode to Joy, five neighbouring keys, wait mode, soft chords) in about 15–60 s;
   - a quiet room celebration;
   - drum, nod and harp, each in one try;
   - then the single essential gesture (the palm menu) and a spoken walk down its rows;
   - Sunny leaves at 1:00–3:30 depending on the player, and at worst about 3:10 for a player who does nothing.
2. **Every step has the same safety net.**
   - At 8 s with no progress: a short "again" line and the ghost-hand demo again.
   - At 15 s: Sunny gives up on the step, says where to find the feature later, and moves on.
   - A step that cannot be skipped without cost (the first key, the palm menu) changes its success test instead of
     stalling. The first key falls back to "any key plays the next note". The palm menu falls back to "I'll open it for
     you this time".
3. **Poking Sunny = skip** (section 4).
   - The line in flight is cut, a soft "bop" plays, the next step begins.
   - Three skips in a row jump to the menu step, so the one essential gesture is still offered.
   - There is **no repeat gesture**. Repeating is automatic (the 8 s re-demo) and on demand (the menu's Tips list, and
     "Show me around" for the whole tour).
4. **Contextual tips: 61 tips**, triggered by the player's own actions (section 3).
   - Shown once, persisted, rate-limited, never over active playing.
   - Each one can be replayed from a new **Sunny** menu screen: Show me around · Tips · Make a song in layers ·
     Tips while I play.
5. **Lines:** 86 new and 3 changed (section 5). 108 in all, every one under 12 words and under 3.6 s. They are rendered
   with the existing eSpeak NG "Annie" pipeline.
6. **Coverage:** every feature has a `FeatureId`, and every gesture, menu row and screen must map to a tour step or a tip,
   or be marked passive with a reason. Tests enforce this (section 6).

---------------------------------------------------------------------------------------------------------------------------

## 1. Feature inventory

**Columns**
- **Src** (where the feature comes from):
  - main = the integrated project;
  - arr = u-arrange;
  - eng = u-engine;
  - xh / xw = u-exp-hands / u-exp-world;
  - loop = u-looper;
  - new = introduced by this spec.
- **Today** (does Sunny cover it in the main project now): Yes / Partly / No / Passive (nothing to learn) / card (MR's
  text-card hint only, not Sunny).
- **After:** which first-run step (S#) or tip (`tip_*`, section 3) covers it.

Uncertain items (clones still in progress) are marked ⚠.

### A. Start, app, Sunny

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| A1 | Start panel "Play here" | arr | No | S0 (Sunny helps only if the panel waits 8 s; `start_help`, `start_auto`) |
| A2 | Start panel "Play in my room" | arr | No | S0, then the MR tips (J) after the tour |
| A3 | The app fits the piano to eye height and reach | new | No | S1 (silent; the piano eases into place during the greeting) |
| A4 | Exit row, "Tap again to exit" within 3 s | arr | No | `menu_tour_3`, `tip_exit` |
| A5 | Sunny's first-launch tour | main | Yes | Replaced by the `FirstRun` script S0–S11 |
| A6 | "Show me around" (replay the tour) | main | No | `bye_first` ("Find me in the menu anytime"), the Sunny screen |
| A7 | "Hide Sunny" | main | No | `tip_hide_sunny` |
| A8 | Poke Sunny to skip | new | No | `poke_me` (after the song), `tip_poke_sunny` |
| A9 | Tips list, "Tips while I play" | new | No | `bye_first`, `tip_hide_sunny`, and the Sunny screen's own rows |
| A10 | Headset off cuts Sunny's line, the menu hides | main | Passive | (no tip by design) |
| A11 | 90 Hz with a quality governor (E5) | xw | Passive | (no tip by design) |

### B. Piano

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| B1 | Press keys with the fingertips | main | No | S2, S3 |
| B2 | Note letters on the keys, middle C mark | main | No | `tip_middle_c` |
| B3 | Automatic sustain | main | Passive | (no tip) |
| B4 | Fingers stop on the keys (E1) | xh | Passive | (no tip) |
| B5 | Soft contact shadows under the fingertips (E3) | xh | Passive | (no tip) |
| B6 | "Classic sound" row (Instruments screen) | eng | No | `tip_classic` |

### C. Drums

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| C1 | Hit heads and cymbals with the fingers | main | No | S5, `tip_drum` |
| C2 | Pick up a stick (pinch or fist within 0.12 m of it) | main | No | `tip_sticks` |
| C3 | Put a stick back (open hand for 0.25 s) | main | No | `tip_sticks_drop` |
| C4 | Pinch the hi-hat stand to open or close it | main | No | `tip_hihat` |
| C5 | Hold a ringing cymbal to choke it | main | No | `tip_choke` |
| C6 | Nod kick | main | Yes | S6, `tip_nod`, `tip_lesson_kick` |
| C7 | Kick pad beside the snare | main | Yes (`nod_skip`) | S6 give-up, `tip_lesson_kick` |
| C8 | Strike zones (rim, bell, edge), ghost notes | main | Passive | (no tip) |
| C9 | Sticks bounce (E2), cymbal rocking, head give, kick beater (E7) | xh/xw | Passive | (no tip) |
| C10 | Fast drum tracking (E4) and the low-light guard | xw | No | `tip_more_light` |

### D. Harp

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| D1 | Pluck or sweep the strings | main | No | S7, `tip_harp` |
| D2 | Red C strings, blue F strings | main | No | `tip_harp_colors` |
| D3 | Palm damping (two resting tips on two strings) | main | No | `tip_harp_damp` |
| D4 | String blur (E8) | xw | Passive | (no tip) |

### E. Side table

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| E1 | FIRE switch | main | No | `tip_switches` |
| E2 | WIND switch | main | No | `tip_switches` |
| E3 | SNOW switch | main | No | `tip_switches` |
| E4 | Tape deck REC | main | No | `tip_deck`, `tip_rec` |
| E5 | Tape deck STOP | main | No | `tip_rec` |
| E6 | Tape deck PLAY | main | No | `tip_take` |

### F. Looper ⚠

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| F1 | LOOP, one-bar count-in | loop ⚠ | No | `tip_loop_start` (merges the looper builder's first-use line) |
| F2 | Tempo presets 70 / 90 / 110 BPM | loop ⚠ | No | `tip_loop_tempo` |
| F3 | Loop length 2 or 4 bars | loop ⚠ | No | `tip_loop_tempo` |
| F4 | Overdub layers, up to 6, one pass at a time | loop ⚠ | No | `tip_loop_layer` |
| F5 | Per-layer mute lights | loop ⚠ | No | `tip_loop_mute` |
| F6 | UNDO | loop ⚠ | No | `tip_loop_undo` |
| F7 | CLEAR (tap again) | loop ⚠ | No | `tip_loop_clear` |
| F8 | Looper STOP | loop ⚠ | No | `tip_loop_saved` |
| F9 | Metronome | loop ⚠ | No | `tip_loop_click` |
| F10 | Loops saved to Recordings | loop ⚠ | No | `tip_loop_saved` |
| F11 | "Make a song in layers" mini-tour | new | No | `Layers` script (section 3.6) |

### G. Layout gestures

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| G1 | Page-turn swipe (one-instrument layout) | main | Yes | `tip_swipe` (on the first single layout; `swipe_vr`, `swipe_again`, `swipe_skip`) |
| G2 | Seat turn (pinch the air, pull sideways) | main | No | `tip_seat_turn` |
| G3 | Two-hand summon (bring everything back) | main | No | `tip_summon` |
| G4 | Move an instrument while playing (pinch and hold 0.3 s) | main | No | `menu_tour_2` points to Move instruments; `tip_lock` after the first in-play move |
| G5 | Turn a held instrument with the wrist | main | No | `tip_turn` |
| G6 | Second-hand 45° pull, two-hand twist | main | No | `tip_turn_two` |
| G7 | Move the menu panel (pinch and hold it) | main | No | `tip_panel_move` |
| G8 | Lock instruments row | main | No | `tip_lock` |
| G9 | All instruments / one alone (Instruments rows) | main | Partly (`swipe_skip` names Instruments) | `tip_instruments`, `swipe_skip` (changed) |

### H. Move instruments (arrange mode)

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| H1 | Move instruments row, arrange mode | arr | No | `menu_tour_2`, `tip_arrange` |
| H2 | Near grab (pinch within 8 cm) | arr | No | `tip_arrange` |
| H3 | Ray grab, push and pull along the ray | arr | No | `tip_arrange_ray` |
| H4 | Height (piano key tops 0.55–0.95 m, kit −0.17..+0.15 m) | arr | No | `tip_arrange_lift` |
| H5 | Turn (wrist, two hands) | arr | No | `tip_turn`, `tip_turn_two` |
| H6 | Done | arr | No | `tip_arrange_done` |
| H7 | Reset | arr | No | `tip_arrange_done` |
| H8 | Placement remembered per layout | arr | No | `tip_arrange_saved` |
| H9 | Meta pinch fallback, system-gesture guard | arr | Passive | (no tip) |

### I. Palm menu

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| I1 | Palm summon | main | Yes | S8, `tip_menu` |
| I2 | Poke a row | main | Yes | S0 helper, `menu_tour_3`, `tip_poke_row` |
| I3 | Palm again to close | main | Yes | S10, `tip_close` |
| I4 | Back (the header row) | main | No | `tip_back` |
| I5 | Drag a list to scroll | main | No | `tip_scroll` |
| I6 | Lessons row, pick an instrument | main | No | `menu_tour_1`, `tip_lessons` |
| I7 | Course list | main | No | `tip_lessons`, `tip_scroll` |
| I8 | Playing a lesson (countdown, blocks, wait mode, red flash) | main | No | S3 (first run), `tip_lesson_play` |
| I9 | Lesson Stop, Replay, Courses | main | No | `tip_lesson_done` |
| I10 | Drum lessons: the kick target is the pad | main | No | `tip_lesson_kick` |
| I11 | Harp lessons | main | No | `tip_lesson_harp` |
| I12 | Recordings row, take list | main | No | `menu_tour_1`, `tip_recordings` |
| I13 | Take Play / Stop | main | No | `tip_recordings` |
| I14 | Take Delete (poke twice) | main | No | `tip_delete` |
| I15 | "Saving off" notice | main | Passive | (the panel text) |
| I16 | Show me around / Hide Sunny row | main | No | `bye_first`, `tip_hide_sunny` |
| I17 | Play in my room / Music room row | main | No | `menu_tour_2`, `tip_mr_leave` |
| I18 | Set up my room row (MR without a room) | main | No | `tip_mr_setup` |

### J. Mixed reality ("Play in my room")

| # | Feature | Src | Today | After |
|---|---|---|---|---|
| J1 | Enter: passthrough, the room hides, everything is laid out around you | main | card | `tip_mr_table` or `tip_mr_floor` |
| J2 | Piano lands on a real table | main | card | `tip_mr_table` |
| J3 | Place the piano: pinch empty air, point, pinch | main | card | `tip_mr_floor`, `tip_mr_place` |
| J4 | Two-hand summon in MR | main | card | `tip_summon` |
| J5 | Snow behind real windows | main | No | `tip_mr_snow` |
| J6 | Manual snowy window (point at a wall, pinch) | main | card | `tip_mr_window` |
| J7 | Spatial data permission denied | main | card | `tip_mr_permission` |
| J8 | Real hands over the instruments (E6) | xh | Passive | (no tip) |
| J9 | Piano remembered with a spatial anchor (E9) | xw | No | `tip_mr_anchor` |
| J10 | Fire and wind off in MR, snow stays | main | Passive | (no tip) |

**Desktop-only controls** are out of scope: they exist only in the editor, and Sunny keeps its `*_desk` lines for
them. They are M, Shift+H, the computer-keyboard piano, Shift / Alt drag and mouse clicks.

### Counts

**94 features**: 82 to learn, 12 passive.

| | Before (main project today) | After (this spec) |
|---|---|---|
| Covered | 7 fully (A5, C6, C7, G1, I1, I2, I3) + 1 partly (G9), so 8 / 82 | 82 / 82 |
| Passive, recorded with a reason | none | 12 |

---------------------------------------------------------------------------------------------------------------------------

## 2. The first-run script (`FirstRun`)

### 2.1 Timers and rules shared by every step

- **Line end (`L`).** The moment the step's line (or its latest "again" line) stops being busy (`LineBusy() == false`,
  `quietT ≥ 0.3`). Timers count from `L`, never from the step's start, so a long line never eats the player's time.
- **Progress.** A signal that the player is doing the step: listed per step, for example any piano key in S2. Progress
  resets the idle timer.
- **Re-demo at 8 s.** `idle ≥ 8 s` (no progress since `max(L, last progress)`) restarts the ghost-hand demo from loop
  time 0 and says the step's "again" line. It happens at most twice per step (at 8 s and at 16 s of idle); the give-up
  usually comes first.
- **Give-up at 15 s.** `fail ≥ 15 s` (no success since `L` of the step's first line; time while Sunny talks is not
  counted). The step's give-up line is said, its give-up action runs, and the script moves on.
  - Multi-part steps (the song) reset `fail` on real progress (a correct note).
  - The step is recorded as `GaveUp`. Its tip stays unseen, so it comes back in context later.
- **Success.**
  - The demo ends and Sunny smiles at once. This is today's `Complete()`: praise waits for the line in flight plus 0.3 s
    (finish-the-sentence).
  - Praise is `praise_k` (k = step % 5), unless the step's next line already starts with praise ("Lovely! …").
  - The step is recorded as `Done`, and the tips it teaches are marked seen.
- **Poke Sunny** = `Skip` (section 4). The step is recorded as `Skipped`. No line is said for a skip: the next step's
  line follows 0.35 s after the poke "bop".
- **Pauses.**
  - Headset off (`HmdMountEvent{false}`): the line is cut and every timer freezes. On mount the step restarts from its
    line.
  - "Hide Sunny" ends the run (today's `Dismiss`).
  - Sunny never speaks during a looper count-in or recording pass.
- **Perch** (section 4.6).
  - Sunny follows the gaze, as today: 0.55 m ahead, 0.25 m left, 0.10 m below the eyes.
  - It stays at least 0.12 m clear of every play zone and at least 0.08 m clear of the shown menu, start panel or
    Done / Reset buttons.
  - For a step whose target is more than 35° off the gaze (the drum on the right, the harp on the left), Sunny turns its
    face toward the target and drifts up to 0.15 m that way.
- **World-anchored demos.** For the key, drum and harp steps the ghost hand plays **at the target** (above E4, above
  the snare, across the strings), not beside Sunny. Sunny stays where the player looks. The demo kinds are in
  section 7.3.

### 2.2 Timed table

Target times are for a typical first-timer, who reads for about 3 s and needs one re-demo on about three steps. Times
for the scripted "prompt" player, who reacts 1.0 s after each line, are in section 2.4. Line lengths are the measured
clip seconds plus the 0.14 s lead.

| Step · target time | Trigger | Sunny's line (id, s) | Ghost-hand demo | Success | 8 s re-demo | ~15 s give-up | Poke (skip) |
|---|---|---|---|---|---|---|---|
| **S0 Start** · 0:00 | Room ready: first presenting frame, HMD mounted, `IMenuPanel.StartPending`. The start panel opens 0.55 m ahead | None: Sunny is not shown. If the panel is unanswered after 8 s, Sunny arrives beside it with `start_help` "Hi, I'm Sunny! Poke a button with one finger." (3.71) | Poke, anchored on "Play here" (`MenuScreen.Start` row rect) | Either row poked (`StartChoiceEvent`) | 8 s after the panel opened: Sunny's arrival above. Again 8 s after `start_help` (demo only) | 15 s after `start_help`: `start_auto` "Let's play here for now." (1.78); the panel answers **Play here** | = "Play here" |
| **S1 Fit** · 0:03–0:05 | Start answered (VR). For MR see 2.3 | First line of S2 (no separate line) | None | Automatic: `ILayoutService.FitToPlayer()` eases the piano over 0.6 s (section 7.5) | n/a | n/a | n/a |
| **S2 First key** · 0:05–0:10 | Fit started. The first-run lesson `piano-ode-first` starts with **no countdown**; block 0 falls onto **E4** and the key glows when the block is live (1.8 s) | `first_key` "Hi, I'm Sunny! Touch the glowing key." (3.15). In a replay: `intro_vr`, then `key_again` | Press: ghost index finger presses E4 twice per 3 s loop, on the front 2 cm of the key (in front of the resting block) | E4 played (`TutorialHitEvent` index 0). **Any piano key counts as the first sound** for the 10 s goal | `key_again` "Touch the glowing key, like this." (2.38). If a different key was played first: `key_other` "Nice sound! Now try the glowing one." (2.95), at most once, 1.5 s after the wrong key | `key_any` "Any key will play it now." (1.75). The lesson goes **forgiving** (any piano key plays the target note) and the step completes on the next key | Skips S2–S4 (the whole song): `Tutorial.Stop()`, go to S5 |
| **S3 Song** · 0:10–1:20 | E4 hit | `song_go` "Lovely! Play each key as its block lands." (3.27). The praise is built in. The C chord sounds under the first note | Press, re-anchored on the current target key at each re-demo | Lesson `Done` (15 notes). Progress = each correct note (resets idle and fail). Wrong notes sound, with no red flash and no mistake count (`QuietMistakes`) | `key_again` + press on the current key | At 15 s without a correct note: `song_any` "Any key plays the next note now." (2.32), forgiving on. 15 s more with no key at all: `song_skip` "Let's try the drums. Finish the song in Lessons." (3.54), `Tutorial.Stop()`, go to S5 | Same as S2 |
| **S4 Celebration** · ~1:20 (6 s) | First-run lesson `Done` | +1.0 s: `song_done` "You just played Ode to Joy!" (1.96), then `poke_me` "Tip: poke me anytime to skip." (2.46) | None. Sunny rises 2 cm and settles (a happy bob) | Timed, 6 s | n/a | n/a | Skips the lines; the room effects run on |
| **S5 Drum** · ~1:30 | S4 ended | `drum_vr` "Now tap the drum on your right." (1.93) | Strike: two quick taps on the snare's head (`IDrumsApi.Pieces[0]`) | Any non-kick `drum` event, not playback (finger or stick). Progress = a hand within 0.25 m of the kit | `drum_again` "Tap the top of the drum, like this." (2.63) | `drum_skip` "The drums will wait on your right." (2.01) | Go to S6 |
| **S6 Nod** · ~1:45 | S5 ended | `nod_vr` "Nod your head to play the kick drum" (2.34, kept) | Nod (Sunny nods itself, as today) | `NodEvent`, or a kick with every hand pointer more than 0.2 m from the pad (today's rule) | `nod_again` "Nod your head, like me." (1.89) | `nod_skip` "The kick pad by the snare plays it too" (2.51, kept) | Go to S7 |
| **S7 Harp** · ~2:00 | S6 ended | `harp_vr` "Now sweep your fingers across the harp strings." (3.10) | Sweep: an open ghost hand slides through 6 strings at their play points (`IHarpApi.PlayPoint`), 0.4 s per pass | A sweep: ≥ 3 distinct strings plucked by one hand within 0.8 s. **After the first re-demo, any pluck succeeds** | `harp_again` "Slide your fingers through the strings, like this." (3.23) | `harp_skip` "The harp will wait on your left." (2.05) | Go to S8 |
| **S8 Palm menu** · ~2:30 | S7 ended | `open_vr` "Hold your open palm toward your face to open the menu" (3.60, kept) | Palm, with the summon ring (today's demo) | The panel opens from the player's palm (`MenuEvent{Open}`) | `open_again` "Palm toward your face, like this." (2.57) | `open_auto` "I'll open it for you this time." (1.88). Sunny calls `IMenuPanel.Show()`, so the panel opens along the gaze. `tip_menu` stays unseen | Skips S8–S10; go to S11 |
| **S9 What's in it** · ~2:40 (≈10 s) | Panel open | `menu_tour_1` "Lessons, instruments and your recordings are here." (3.30), then `menu_tour_2` "Move instruments, or play in your own room." (2.90), then `menu_tour_3` "Exit is at the bottom. Poke any row to try." (3.39) | None. **Row spotlight**: the menu's own hover highlight moves down the rows the line names, at fixed fractions of each clip (line 1: Lessons 0.00, Instruments 0.30, Recordings 0.62; line 2: Move instruments 0.00, Play in my room 0.45; line 3: Exit 0.00, cleared at 0.45). No repaint (section 7.2) | Timed. A row the player pokes acts normally (see the S9 notes below) | n/a | n/a | Go to S10 |
| **S10 Close** · ~2:55 | `menu_tour_3` ended | `close_vr` "Show your palm again to hide it" (2.18, kept) | Palm | The panel closes from the palm, **or** the screen changed because the player poked a row (they are exploring) | `close_again` "Show your palm again, like this." (2.41) | `close_auto` "I'll close it for you." (1.38). Sunny calls `IMenuPanel.Hide()` | Go to S11 |
| **S11 Goodbye** · ~3:00–3:30 | S10 ended | `bye_first` "That's the tour! Find me in the menu anytime." (3.26), then `leave` "Bye!" (0.61, kept) | None. The leave animation as today (0.8 s) | `TourDone = 1` and `tourVersion = 2` saved; Sunny gone | n/a | n/a | Leaves at once (`leave` line, no `bye_first`) |

**S9 notes** (a row poked during the walk down the rows)
- A row that **starts a mode** (Move instruments, Play in my room, a lesson, Show me around, Exit's confirm) ends the
  script at S11 with the short goodbye (`leave` only). The mode's own tips follow, as section 3 allows.
- **Any other row** (opening a sub-screen) jumps to S10 with praise.

### 2.3 Variants

**"Play in my room" chosen at S0**
- S1 waits for MR to settle: `IMixedReality.Phase` is OnTable or OnFloor and the piano has been still for 0.5 s. It waits
  at most 6 s, then goes on.
- The OS spatial-data dialog may appear first. While it is up, nothing runs and no timer counts.
- On a table there is no height fit. On the floor stand only the height fit runs.
- S4 in MR: the room's fire and lamps are hidden. The celebration is a snow gust at the real-window portals (if any) plus
  the harp chord.
- MR tips (`tip_mr_*`) are queued until after S11. The exceptions are `tip_mr_permission` and `tip_mr_setup`, which may
  play right after S4.

**"Show me around" (replay, from the Sunny screen)**
- No S0 or S1. The first line is `intro_vr` "Hi, I'm Sunny! Let me show you around.", then S2 with `key_again`.
- Steps whose instrument is not in the scene (one-instrument layout) are skipped. With no piano, S2–S4 are skipped.
- `TourDone` is unchanged.
- Steps that succeed mark their tips seen.

**Desktop (editor)**
- The same script. Clicks and the keyboard satisfy the success tests (a mouse-clicked key, drum or string; M for the
  menu; the kick pad click for the nod, as today).
- New steps show the VR caption with the VR clip (no new `*_desk` recordings). The kept menu steps use `open_desk` and
  `close_desk`.

**Returning players** (`TourDone == 1` from the old tour, `tourVersion` < 2)
- The tour does not start by itself.
- Every tip starts unseen, so the new features are taught in context.

### 2.4 Time budget (what the tests assert)

| Player model (scripted hands, section 7.8) | First sound | Song done | Sunny gone | Notes |
|---|---|---|---|---|
| **Prompt**: pokes "Play here" at 2.0 s; acts 1.0 s after each line; hits each block 0.1 s after it is live | ≈ 6.2 s | ≈ 15.4 s | ≈ 62 s | Assert first sound < 10 s, song < 120 s, gone < 90 s |
| **Typical** (simulated): acts 3 s after a line; fails the first try on S5, S6 and S8 (re-demo at 8 s); about 3.5 s per note | ≈ 10 s | ≈ 1:05 | ≈ 3:00 | Assert song < 120 s, gone < 4:00 |
| **Passive**: never moves | none | none (gives up) | ≈ 3:10 | Every step `GaveUp`, the menu opened and closed by Sunny. Assert gone < 4:00 and no state held more than 20 s (watchdog) |
| **Poker**: pokes Sunny 0.5 s into each step | n/a | skipped | < 40 s | 3-skip rule (section 4.5): S2 skip, S5 skip, S6 skip, then a jump to S8, then a poke ends at S11 |

Passive-path arithmetic, using line lengths with lead plus 0.3 s gaps:

| Steps | Passive time |
|---|---|
| S0 | 8 + 3.7 + 15 + 1.8 = 28.5 s |
| S2 + S3 | 3.1 + 15 + 1.8 + 15 + 3.5 = 38.4 s |
| S5, S6, S7, S8 | each about 19–20 s, so ≈ 78.6 s |
| S9 | ≈ 10.5 s |
| S10 | ≈ 18.6 s |
| S11 | ≈ 4.7 s |
| Gaps | ≈ 9 s |
| **Total** | **≈ 188 s ≈ 3:08** |

### 2.5 The first-run song: `piano-ode-first` (new Unity-side lesson)

The existing `piano-ode` is the first phrase. It ends on D (unfinished) and has no harmony. The first run needs a
resolved ending, so it uses the **answering phrase**:
- The same five keys, C4–G4. One hand, one finger per key. It ends on C.
- Tune (lesson token syntax, `NOTE:len`): `E4 E4 F4 G4 G4 F4 E4 D4 C4 C4 D4 E4 D4:1.5 C4:0.5 C4:2`.
- 108 BPM, 16 beats, 15 notes.

**Accompaniment** (new lesson field `accompany`)

| Beat | Chord | Notes (MIDI) |
|---|---|---|
| 0 | C | C3 E3 G3 (48, 52, 55) |
| 4 | G | B2 D3 G3 (47, 50, 55) |
| 8 | C | C3 E3 G3 |
| 12 | G | B2 D3 G3 |
| 14 | C (final) | C2 C3 E3 G3 (36, 48, 52, 55); rings 3 s |

- **Wait mode freezes time,** so each chord plays at the moment the player hits the note on its beat, and releases the
  previous chord.
- **Velocity 0.28.**
- **How it plays.** Directly through `IAudioEngine.Piano` / `Release`:
  - no key moves (no ghost-piano look);
  - no `RecorderEvent`, so it never counts as a hit and is never recorded.
- **First-run lesson options:** `NoCountdown`, `Accompany`, `QuietMistakes` (no red flash, no mistake count),
  `Forgiving` (set live by S2 and S3).
- **Where the data lives.** A Unity resource `Lessons/Resources/lessons-firstrun.json` in the lessons.json schema plus
  `accompany: [{beat, midi[], vel}]`. It is kept out of `lessons.json`, so the lesson export and its drift tests stay
  untouched. It is not listed in the Lessons menu.

### 2.6 The celebration (S4): calm, real, no pop-ups

All effects start when the last note is hit. The Room module owns them through a new `IRoomMoments.Celebrate()`
(section 7.4).

| Effect | Numbers |
|---|---|
| Fire flares | Fire light 2.07 → 3.0 over 0.6 s, back over 4 s. Flame height +30 % for 2.5 s. One extra crackle burst at the fire source |
| Snow gust outside (only if SNOW is on) | `ISnowWeather.Gust(0.9)`: the flakes swirl past the window with the existing soft gust sound |
| Lamps glow warmer | Floor lamp and candles: intensity ×1.15, colour about 150 K warmer (#ffd6a0 → #ffc98c), over 1.5 s; hold 4 s; back over 8 s |
| Harp chord | A rolled C major chord, C3 E3 G3 C4 (string indices 0, 2, 4, 7), 70 ms apart, vel 0.35, through `IHarpApi.PlaybackPluck`, so the strings visibly ring and blur. If the harp is not in the scene, the same chord plays on the piano, audio only |
| Sunny | Happy face, a 2 cm rise and settle, `song_done` at +1.0 s |

Not used: confetti, fireworks (the app has no fireworks effect), glowing text, and the menu's "Lesson complete!"
screen (the menu is closed in the first run).

---------------------------------------------------------------------------------------------------------------------------

## 3. Contextual tips

### 3.1 How a tip runs

1. **Trigger.** A signal (section 7.6) marks the tip *pending*, if it is unseen, allowed this launch, and its conditions
   hold.
2. **Wait for a quiet moment.** The tip waits up to its class's `waitMax` for all of these:
   - no play event for 2.5 s;
   - no grab held (arrange tips excepted);
   - no menu poke in the last 1.5 s (screen tips wait 0.8 s after the screen appears);
   - no lesson in countdown or playing (lesson tips excepted);
   - no take or loop playback, no looper count-in or recording pass;
   - MR not placing (`tip_mr_place` excepted);
   - Sunny not running a script.

   If the moment never comes, the tip is dropped this time and is **not** marked seen.
3. **Appear.**
   - If Sunny is hidden, it grows in at its perch over 0.4 s with the open chirp. There is no "Hi" line.
   - Its line plays, with the demo looping at the tip's anchor.
4. **Wait for success** (if the tip has a success test), for up to 15 s:
   - at 8 s: the generic `again` "Like this." (0.92 s) and the demo again;
   - on success: `praise_k`;
   - after that, Sunny leaves silently (shrink and fade, no "Bye!"), unless another tip is pending.
5. **Marked seen** (persisted) when any of these happens:
   - the line finished naturally (heard);
   - the success test passed (even with no tip shown: doing it yourself counts);
   - the player poked Sunny during the tip.

   A cut by headset-off, by Hide Sunny or by a higher-priority script leaves it unseen.
6. **Classes:**

| Class | When | `waitMax` | Spacing to the previous unsolicited tip | Repeats |
|---|---|---|---|---|
| **P1 blocking** (the player is stuck: no permission, no window, low light, instruments far away, never opened the menu) | Any time after the tour | 30 s | 10 s | Once per launch while the condition holds (P1 tips may repeat across launches; `tip_menu` until the palm has opened the menu once) |
| **P2 first use** (the player just entered a mode or screen) | On the trigger | 6 s | 10 s | Once, ever |
| **P3 discovery** (something nearby they have not tried) | Not in the first 90 s after the tour; at most 3 per 10 min | 20 s | 60 s | Once, ever |

7. **Gates.**
   - "Tips while I play" = Off (persisted) stops P2 and P3. P1 still plays, since it unblocks the player.
   - "Hide Sunny" stops all unsolicited tips for the rest of the launch.
   - Tips never interrupt the first-run script. The script suppresses them, and the steps it teaches mark their tips seen.
8. **Housekeeping.**
   - The 3rd unsolicited tip ever appends `tip_hide_sunny`.
   - The first unsolicited tip ever appends `tip_poke_sunny`, if the player never heard `poke_me`.

**Persistence** (`AppSettings`, the injected store):
- `musicroom.tourDone` (existing);
- `musicroom.tourVersion` (int, 2 = this script);
- `musicroom.tipsSeen` (comma-separated tip ids);
- `musicroom.tipsOn` (1 by default);
- `musicroom.p1Launches` (comma-separated `tipId:count`).

### 3.2 The tips

**Columns**
- **Demo @ anchor:** the demo kinds of section 7.3. "@ Sunny" means in front of Sunny, as today; otherwise the ghost hand
  plays at that world target.
- **Title:** the row text in the Tips list.

**Tour steps given again as tips** (used when the step was skipped or gave up, or the player never did it)

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_menu` | Open the menu | Tour over, and the palm has never opened the menu; the player idle 20 s (no play) | `open_vr`, again `open_again` | Palm @ Sunny | Menu opened by the palm | P1 |
| `tip_poke_row` | Poke a row | Menu opened by the palm and no poke for 6 s (first time) | `poke_vr` (kept) | Poke @ the first home row | `MenuEvent{Screen}` | P2 |
| `tip_close` | Close the menu | Menu open 25 s with no poke (first time) | `close_vr` | Palm @ Sunny | Closed by the palm | P2 |
| `tip_drum` | Play the drums | All layout; gaze on the kit for 1.5 s; drums never played | `drum_vr` | Strike @ snare | Drum event | P3 |
| `tip_nod` | Nod for the kick | Kit in the scene; ≥ 5 drum hits; the nod never succeeded | `nod_vr`, again `nod_again` | Nod (Sunny) | `NodEvent` | P3 |
| `tip_harp` | Sweep the harp | Gaze on the harp for 1.5 s; harp never played | `harp_vr` | Sweep @ strings | ≥ 3 strings in 0.8 s | P3 |

**Piano, drums, harp**

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_middle_c` | Middle C | First `pianoOn` 60 outside lessons and the tour | `tip_middle_c` | Press @ C4 (one loop) | none (information) | P3 |
| `tip_sticks` | Pick up a stick | ≥ 10 finger drum hits within 20 s, no stick ever held | `tip_sticks` | Pinch @ the nearer stick on the snare | `StickPick.Grabbed` | P3 |
| `tip_sticks_drop` | Put a stick back | First stick pick-up | `tip_sticks_drop` | Open hand @ Sunny | Stick released | P2 |
| `tip_hihat` | Open the hi-hat | 3rd hi-hat hit with the hat closed; never toggled | `tip_hihat` | Pinch @ the hat stand's pinch spot | `hat` event | P3 |
| `tip_choke` | Stop a cymbal | Crash or ride left ringing ≥ 2 s, the 2nd time | `tip_choke` | Hold (flat fingers rest) @ that cymbal | `drumChoke` | P3 |
| `tip_more_light` | More light | `FastMotionPolicy.Blocked` rises (E4 low-light guard) ⚠ | `tip_more_light` | none | none | P1 |
| `tip_harp_colors` | Harp colours | 5th harp pluck outside lessons and the tour | `tip_harp_colors` | none (Sunny faces the harp) | none | P3 |
| `tip_harp_damp` | Quiet the harp | A sweep of ≥ 6 strings left ringing ≥ 3 s, the 2nd time | `tip_harp_damp` | PalmRest @ the strings' middle | `harpDamp` | P3 |

**Side table** (switches and tape deck)

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_switches` | Fire, wind and snow | Gaze on the switch panel (≤ 25°) for 1.5 s, or a fingertip within 6 cm of a lever; never flipped | `tip_switches` | Poke (flip) @ the FIRE lever | `AmbienceEvent` | P3 |
| `tip_deck` | The tape deck | Gaze on the deck for 1.5 s or a fingertip hover; REC never pressed | `tip_deck` | Poke @ REC | Recording | P3 |
| `tip_rec` | Record | First `RecorderState.Recording` | `tip_rec` | Poke @ STOP (only after 6 s of recording) | Stop with a take | P2 |
| `tip_take` | Play it back | First `OnTake` | `tip_take` | Poke @ PLAY | Playing | P2 |

**Looper** ⚠ (no looper code yet; controls assumed on the tape deck)

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_loop_start` | Start a loop | First LOOP press (count-in begins). **This replaces the looper builder's own first-use line**; reconcile the id | `tip_loop_start`, said **before** the count-in (the press is held until the line ends) or on the press if the builder cannot defer | none | First layer recorded | P2 |
| `tip_loop_layer` | Add a layer | First layer finished | `tip_loop_layer` | Poke @ LOOP | Layer 2 recording | P2 |
| `tip_loop_mute` | Mute a layer | Layers ≥ 2, first time | `tip_loop_mute` | Poke @ layer 1's light | Mute toggled | P3 |
| `tip_loop_undo` | Undo a layer | Layers ≥ 3, first time | `tip_loop_undo` | Poke @ UNDO | Undo | P3 |
| `tip_loop_clear` | Clear the loop | First CLEAR press (armed, waiting for the second tap) | `tip_loop_clear` | Poke @ CLEAR | Cleared | P2 |
| `tip_loop_tempo` | Tempo and length | 2nd loop session about to start (looper idle, a fingertip near its controls); tempo and length never changed | `tip_loop_tempo` | Poke @ the 90 preset | Tempo or length changed | P3 |
| `tip_loop_click` | The click | 2nd loop session; metronome never on | `tip_loop_click` | Poke @ the click button | Metronome on | P3 |
| `tip_loop_saved` | Saved loops | First looper STOP that saved a loop | `tip_loop_saved` | none | none | P2 |

**Layout gestures**

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_swipe` | Turn the page | First `LayoutMode.Single` | `swipe_vr`, again `swipe_again`, give-up `swipe_skip` | Swipe @ Sunny (today's arrow demo) | Page turn | P2 |
| `tip_seat_turn` | Turn your seat | VR room; head yaw ≥ 50° from the seat heading for 3 s, not playing | `tip_seat_turn` | PinchPull @ Sunny | `SeatTurnEvent` | P3 |
| `tip_summon` | Bring everything back | Any instrument of the layout more than 1.1 m from the head for 3 s, or the head more than 0.6 m from the seat; not arranging | `tip_summon` | TwoHandPinch @ Sunny (0.8 s hold shown) | `ArrangedEvent{Source = Summon}` | P1 |
| `tip_lock` | Lock instruments | First in-play move ended (outside arrange mode) | `tip_lock` | none | none | P3 |
| `tip_turn` | Turn an instrument | First grab started (in arrange mode or in play) | `tip_turn` | WristTurn @ the held rig | Yaw change ≥ 15° | P2 |
| `tip_turn_two` | Twist with two hands | `tip_turn` succeeded, 5 s later | `tip_turn_two` | Two hands twisting @ the held rig | Two-hand twist | P3 |
| `tip_panel_move` | Move the menu | 4th palm opening of the menu; panel never moved | `tip_panel_move` | Pinch @ the panel's left edge | Panel grabbed | P3 |

**Move instruments (arrange mode)**

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_arrange` | Move instruments | First `ArrangeModeEvent{Active}` (this is where u-arrange's "Sunny line about moving instruments" belongs) | `tip_arrange` | Pinch @ the nearest instrument's case | Arrange grab | P2 |
| `tip_arrange_ray` | Grab from afar | `tip_arrange` succeeded, or 8 s with no grab; an instrument more than 0.8 m away | `tip_arrange_ray` | PointPinch @ Sunny, aimed at the farthest instrument | Ray grab | P2 |
| `tip_arrange_lift` | Set the height | First arrange grab of the piano or drums | `tip_arrange_lift` | Lift @ the held rig | Height change ≥ 3 cm | P2 |
| `tip_arrange_done` | Done and Reset | First arrange release | `tip_arrange_done` | Poke @ Done | `EndArrange` | P2 |
| `tip_arrange_saved` | Remembered spots | First `EndArrange` that saved a placement | `tip_arrange_saved` | none | none | P2 |

**Menu screens**

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_back` | Go back | First sub-screen shown 4 s with no poke | `tip_back` | Poke @ the header | Back | P2 |
| `tip_scroll` | Scroll a list | First list with `maxScroll > 0` | `tip_scroll` | Drag (index down 6 cm) @ the list | Scroll ≥ 30 px | P2 |
| `tip_lessons` | Lessons | First Lessons screen | `tip_lessons` | Poke @ the Piano row | Instrument picked | P2 |
| `tip_lesson_play` | Play a lesson | First lesson countdown outside the tour | `tip_lesson_play` | none | none | P2 |
| `tip_lesson_kick` | Kick in lessons | First drum lesson's first kick target is live | `tip_lesson_kick` | Nod (Sunny) | Kick hit in the lesson | P2 |
| `tip_lesson_harp` | Harp lessons | First harp lesson start | `tip_lesson_harp` | Press (pluck) @ the target string | Hit | P2 |
| `tip_lesson_done` | After a lesson | First lesson `Done` outside the tour | `tip_lesson_done` | none | none | P2 |
| `tip_instruments` | Instruments | First Instruments screen | `tip_instruments` | Poke @ the Piano row | none | P2 |
| `tip_classic` | Classic sound | First Classic sound turned On | `tip_classic` | none | none | P2 |
| `tip_recordings` | Recordings | First Recordings screen | `tip_recordings` | Poke @ the first take, if any | Take opened | P2 |
| `tip_delete` | Delete a take | First Delete confirm state | `tip_delete` | none | none | P2 |
| `tip_exit` | Exit | First Exit confirm state ("Tap again to exit"); Sunny speaks over the 3 s window. The confirm window must last 3 s **after** the line ends, or the line is dropped (section 7.2) | `tip_exit` | none | none | P2 |
| `tip_hide_sunny` | Hide Sunny | Appended to the 3rd unsolicited tip ever | `tip_hide_sunny` | none | none | P3 |
| `tip_poke_sunny` | Skip my tips | Appended to the first unsolicited tip, if `poke_me` was never heard | `poke_me` | none | none | P2 |

**Mixed reality** (after the tour)

| Tip id | Title | Trigger and conditions | Line(s) | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_mr_table` | Your room | First MR entry; the piano landed on a table | `tip_mr_table` | none | none | P2 |
| `tip_mr_floor` | Piano on a table | First MR entry; no table found | `tip_mr_floor` | Pinch @ empty air beside Sunny | `StartPlacing` | P2 |
| `tip_mr_place` | Place the piano | First placing mode | `tip_mr_place` | PointPinch @ the reticle | Placed | P2 |
| `tip_mr_snow` | Snowy windows | First portal in view (≤ 30°) | `tip_mr_snow` | none (Sunny faces the window) | none | P3 |
| `tip_mr_window` | Add a window | First aiming mode (no window found) | `tip_mr_window` | PointPinch @ the wall outline | Manual window | P1 |
| `tip_mr_permission` | Allow spatial data | `MrStatus.NoPermission` | `tip_mr_permission` | none | none | P1 |
| `tip_mr_setup` | Set up your room | `MrStatus.NoRooms` | `tip_mr_setup` | none | Space Setup run | P1 |
| `tip_mr_anchor` | Piano remembered | Piano restored from its spatial anchor (E9) | `tip_mr_anchor` | none | none | P2 |
| `tip_mr_leave` | Back to the music room | First menu opening in MR, or 90 s in MR | `tip_mr_leave` | none | none | P3 |

**Total: 61 tips** (6 tour tips, 8 piano / drums / harp, 4 side table, 8 looper, 7 layout, 5 arrange, 14 menu, 9 MR).

**MR text cards (`MrHint`) and Sunny.**
- Today the MR module shows its own text cards for 8 s.
- When Sunny voices the matching tip (`tip_mr_floor`, `tip_mr_place`, `tip_mr_window`, `tip_mr_permission`,
  `tip_mr_setup`), the card stays up as the written detail. The informational 8 s cards (EnterHint, PlacedHint) are
  suppressed while Sunny speaks.
- Open question 9 asks whether the cards should go.

### 3.3 The Sunny menu screen and the Tips list (replay anything later)

- **Home row** (today `RowKind.Guide`):
  - While Sunny is present it reads **Hide Sunny · Stop the tour**, and one poke hides it, as today.
  - Otherwise it reads **Sunny · Tour and tips** and opens the new `MenuScreen.Guide`.
- **`MenuScreen.Guide`** (header "‹  Sunny"), five rows:

| Row | Label · note | Action |
|---|---|---|
| `tour` | Show me around · About 3 minutes | Starts the replay script (2.3) |
| `tips` | Tips · "12 of 61 heard" | Opens `MenuScreen.Tips` |
| `layers` | Make a song in layers · With the looper (shown only when the looper exists) | Starts the `Layers` script (3.6) |
| `tipsOn` | Tips while I play · On: Sunny helps / Off: only from here | Toggles `musicroom.tipsOn` (amber "on" style, like Lock) |
| `hide` | Hide Sunny | Shown only while Sunny is present |

- **`MenuScreen.Tips`**: a scrolling list (the courses-list geometry: pitch 130, rows 120, drag to scroll).
  - One row per tip, in the order of section 3.2, with its **Title** and the note "New" (cyan) or "Heard" (dim).
  - Poking a row hides the menu, brings Sunny, and plays that tip: its line, its demo, and its success wait if the
    context exists. For example, `tip_hihat` with the kit in the scene demos at the stand; otherwise the demo plays
    @ Sunny.
  - The tip is then marked seen.
  - Poking Sunny stops it.
- **Footer:** "Poke a tip to hear it again".
- **Row strings** (Menu's `MenuStyle`): "Sunny", "Tour and tips", "Show me around", "About 3 minutes", "Tips",
  "{n} of {m} heard", "Make a song in layers", "With the looper", "Tips while I play", "On: Sunny helps",
  "Off: only from here", "New", "Heard", "Poke a tip to hear it again".

### 3.4 How "repeat" works now (poking no longer repeats)

| Want | How |
|---|---|
| Hear the step again | Wait: after 8 s with no progress, Sunny re-demos with the short "again" line (twice at most) |
| Hear a tip again | Menu → Sunny → Tips → poke it |
| The whole tour again | Menu → Sunny → Show me around |
| A gesture shown again while stuck | P1 tips come back once per launch while the player is stuck |

There is no repeat gesture on Sunny. One touch means one thing (skip). A hold or double-poke repeat would collide with
skip and would be hard to time with hand tracking.

### 3.5 Tip budget and line economy

- 61 tips use 53 tip-only lines plus 8 lines shared with the tour. One generic `again` line serves every tip's re-demo.
- No tip has a "skip" line. A tip that gives up just leaves, and a P1 tip comes back next launch.

### 3.6 Optional mini-tour: "Make a song in layers" (`Layers` script) ⚠ looper

Started only from the Sunny screen. The looper is preset by Sunny through `ILooper.Configure(90 BPM, 2 bars,
metronome on)`. **Sunny never speaks during a count-in or a recording pass.**

| Step | Line (id, s) | Demo | Success | 8 s / 15 s |
|---|---|---|---|---|
| L0 | `lay_intro` "Let's build a song in layers." (1.91) | none | timed | n/a |
| L1 Beat | `lay_drums` "First a beat: press LOOP, then tap the drums." (3.67) | Poke @ LOOP, then Strike @ snare | Layer 1 recorded with ≥ 4 drum events | `again` / `skip` "Let us move on" (kept line) → L2 |
| L2 Harp | `lay_harp` "Now press LOOP again and sweep the harp." (2.84) | Poke @ LOOP, then Sweep @ strings | Layer 2 with ≥ 2 harp events | same → L3 |
| L3 Piano | `lay_piano` "Last layer: press LOOP and play the piano." (3.18) | Poke @ LOOP, then Press @ C4 | Layer 3 with ≥ 3 piano notes | same → L4 |
| L4 | `lay_done` "That's your own song, in layers!" (2.29), then `tip_loop_mute` if unseen | none | timed | n/a |

**Looper-builder risk.** The tape deck is on the left table and the drums are on the right. At 90 BPM a one-bar count-in
is 2.67 s, which is barely enough to reach across. The mini-tour needs either "recording starts on the first note after
LOOP" or a two-bar count-in option. See open question 8.

---------------------------------------------------------------------------------------------------------------------------

## 4. Poke to skip, in detail

### 4.1 Hit area

| Volume | Shape | Active when |
|---|---|---|
| **Body** | Sphere, radius **0.065 m**, centred on Sunny's body (`Sunny.Position`, bob included). That is the body's r 0.045 plus 0.02 for tracker jitter and E1's visual clamp | `Sunny.Present`, scale > 0.5 (not while arriving or leaving) |
| **Bubble** | The bubble quad 0.26 × 0.115 m (+0.01 m margin), from 0.03 m in front to 0.01 m behind; a "front-plane crossing" test like the menu's poke | Bubble visible with opacity > 0.8 |
| **Ghost hand** | **Never** a hit volume | n/a |

**Desktop:** a sphere collider (r 0.065) and a box collider for the bubble on `Layers.PlaySurface`. A left click whose
nearest PlaySurface hit is Sunny is a poke.

### 4.2 Arming, firing, debounce

- **Pokers.** Each tracked hand's index tip (`HandState.Pointers[0]`). Never a stick pointer, never the mouse hand's
  pointers.
- **Armed.** The tip is at least **0.10 m** from the body centre and outside the bubble box grown by 0.03 m.
- **Fires** when an armed tip, in one frame:
  - enters the body sphere, or crosses the bubble's front plane inside its rectangle;
  - with the tip's speed toward Sunny ≥ **0.10 m/s** (a resting or drifting hand never fires);
  - with the pointer active this frame and the last.
- **Guards.** The poke is ignored, but the hand is still disarmed, when any of these holds:
  - the hand played anything in the last **400 ms** (`HandState.LastPlayMs`);
  - it is pinching (the grab pinch, `GrabPinching`) or holding a stick;
  - the tip's `PlaySurfaceAt` is anything other than None for an instrument in the scene;
  - the tip is over the shown menu face, or within 1 cm of the start panel or the Done / Reset buttons (**the menu
    wins**);
  - MR is placing.
- **Re-arm.** The tip must go back past 0.10 m.
- **Debounce.** **0.6 s** between pokes across both hands, so one deliberate poke is one skip.
- **Frame order.** The test runs in `FramePhase.Guide` (step 16), after the instruments (step 5) and the grab (step 11).
  So `LastPlayMs` and the grab state of the same frame are already stamped, and a same-frame play always wins.

### 4.3 Never confused with playing

1. **Placement** (4.6). Sunny's two hit volumes are kept at least **0.12 m** from every instrument's play zone and
   at least **0.08 m** from the menu, start panel and Done / Reset buttons. So a fingertip that could poke Sunny cannot
   also be on a key, head, string, lever or row.
   - Two known overlaps today, both handled by the perch rule:
     - In the one-harp layout the default spot (0.25 m left) lies inside the harp strings' zone (harp at x −0.33,
       z 0.45). Sunny takes the mirrored right-hand spot.
     - The start panel (0.55 m ahead, ±0.16 m wide) leaves only 4.5 cm to Sunny's body. Sunny moves 0.10 m up and left
       during S0.
2. **The guards in 4.2.** A hand that just played, pinches or holds a stick never pokes.
3. **The ghost hand is not touchable.** A player copying the demo does not hit Sunny: the demo hand sits 0.25 m toward the
   player from Sunny, or at the instrument.
4. **Sunny is not an instrument.** It is never in `IGrabService` or arrange-mode targets, and an arrange ray that hits
   Sunny passes through.

### 4.4 Feedback: calm and physical

| Cue | What |
|---|---|
| **Approach** | An armed tip within 0.15 m: Sunny's face turns toward the fingertip by up to 12° (eased 0.15 s). It "notices" the finger, a hint that it can be touched. No glow |
| **Contact push** | The body is pushed 1.5 cm along the tip's direction and springs back (ω 25 rad/s, ζ 0.7, about 0.25 s); a 0.92 squash along the push for 0.08 s |
| **Blink** | Eyes shut 0.13 s (the existing blink, forced) |
| **Mood** | Happy |
| **Sound** | A soft "bop", new `ChirpKind.Poke`: sine 740 → 520 Hz over 0.05 s, peak 0.016, decay 0.08 s, low-pass 5 kHz, on the Guide source (quieter than the open chirp) |
| **Bubble** | The caption fades out in 0.1 s and the next line's caption appears when that line starts. Stale text is never left up |

Not used: flashes, rings, particles, a colour change of the corona.

### 4.5 What a skip does, by state

| Sunny's state | Effect of a poke |
|---|---|
| A line in flight (any) | **Cut** at once: `StopVoice` (4 ms fade, stop at +30 ms), **no closing chirp**, then the bop. The cut line is never resumed |
| Intro / S0 helper | S0: = "Play here". Replay intro: go to the first step |
| Step (S2–S10) | The step is `Skipped`; the next step's line starts **0.35 s** after the bop. Skip targets are in the table in 2.2 |
| Wait / praise queued | The praise is dropped and the next step begins |
| S4 celebration | Sunny's lines are dropped. The room effects finish naturally (they are the room's, not Sunny's) |
| S11 goodbye | Leave now (`leave` "Bye!" and the 0.8 s leave animation) |
| A contextual tip | The tip is marked seen. Sunny leaves at once (silent shrink), or the next pending tip starts after 0.35 s |
| A Tips-list replay | Stops it; Sunny leaves |
| `Layers` mini-tour | Skips the current layer step. Never during a count-in or recording pass: there the poke is ignored, so a reach for the deck that passes Sunny never cancels a take |
| Arriving (scale < 0.5) or leaving | Ignored |

**Three skips in a row** (three pokes with no step success between them):
- If S8 has not been reached, the script jumps to S8 (the palm menu).
- If a poke then also skips S8–S10, the script goes to S11.
- So a player skipping everything still hears the one essential gesture, and is told where Sunny lives.

**Record keeping.** Each skip publishes `SunnyPokeEvent { StepOrTipId }` and logs `[Guide] skip <id> at <t>`. Skipped
steps leave their tips unseen.

### 4.6 Perch: where Sunny floats

- **Order of candidate spots.** For each frame's goal, try these in order and take the first where both hit volumes clear
  every play zone by 0.12 m and the shown panels by 0.08 m:
  1. today's spot: head + 0.55·fwd − 0.25·right, y = head − 0.10;
  2. the mirrored spot (+0.25·right; the bubble flips to Sunny's left so it stays toward the centre of view);
  3. the default spot raised 0.12 m;
  4. the default spot 0.10 m farther.

  The search is analytic: sphere-vs-box tests against `Rig.Zone` boxes and panel bounds, with no physics and no
  allocation.
- **Off-gaze targets.** For a step or tip target more than 35° off the gaze, the goal shifts up to 0.15 m toward the
  target.
- **Easing.** Today's 1 − e^(−3·dt): Sunny glides; it never jumps.

---------------------------------------------------------------------------------------------------------------------------

## 5. Voice lines

### 5.1 Changed lines (3)

| Id | Today | New | Why |
|---|---|---|---|
| `intro_vr` | "Hi, I'm Sunny! Let me show you the hand menu." (3.573 s) | "Hi, I'm Sunny! Let me show you around." (3.102 s) | It is now the replay tour's intro, which covers more than the menu |
| `intro_desk` | "Hi, I'm Sunny! Let me show you the menu." (3.216 s) | "Hi, I'm Sunny! Let me show you around." (3.102 s) | Same |
| `swipe_skip` | "You can try that later from Instruments in the menu" (3.062 s) | "Or pick an instrument in the menu." (1.966 s) | The swipe is now taught when single mode first appears, and "try later" made no sense there |

### 5.2 Kept unchanged (19)

| Lines | Used by |
|---|---|
| `open_vr`, `close_vr`, `nod_vr`, `nod_skip` | The first run |
| `poke_vr` | `tip_poke_row` |
| `swipe_vr` | `tip_swipe` |
| `skip` | The `Layers` give-ups |
| `praise_0`–`praise_4`, `leave` | Everywhere |
| `bye` | The replay tour's end |
| `open_desk`, `poke_desk`, `close_desk`, `swipe_desk`, `nod_desk` | Desktop only |

### 5.3 New and changed entries in the `guide-lines.json` schema (89)

- **Same keys as today's `lines[]`:** `id`, `text` (caption), `spoken` (what eSpeak reads; differs only where noted),
  `file`, `seconds` (tidied clip), `lineSeconds` (= seconds + 0.14 lead).
- **`file`.** The original export writes `<id>.m4a`. For lines added in this project (route B in 5.4) `file` names the WAV.
- **`seconds`.** Measured with the pipeline in 5.4 on this Mac. The same `render()` reproduces today's manifest exactly:
  intro_vr 3.573, nod_vr 2.199, leave 0.471.
- **`spoken` differs from `text`** for:
  - the all-caps button names, because eSpeak spells "REC" as letters and reads the others oddly in caps;
  - "hi-hat", which it reads as "hi hat".
  - The captions keep the deck's printed labels.

```json
[
{"id": "intro_vr", "text": "Hi, I'm Sunny! Let me show you around.", "spoken": "Hi, I'm Sunny! Let me show you around.", "file": "intro_vr.wav", "seconds": 3.102, "lineSeconds": 3.242},
{"id": "intro_desk", "text": "Hi, I'm Sunny! Let me show you around.", "spoken": "Hi, I'm Sunny! Let me show you around.", "file": "intro_desk.wav", "seconds": 3.102, "lineSeconds": 3.242},
{"id": "swipe_skip", "text": "Or pick an instrument in the menu.", "spoken": "Or pick an instrument in the menu.", "file": "swipe_skip.wav", "seconds": 1.966, "lineSeconds": 2.106},
{"id": "start_help", "text": "Hi, I'm Sunny! Poke a button with one finger.", "spoken": "Hi, I'm Sunny! Poke a button with one finger.", "file": "start_help.wav", "seconds": 3.57, "lineSeconds": 3.71},
{"id": "start_auto", "text": "Let's play here for now.", "spoken": "Let's play here for now.", "file": "start_auto.wav", "seconds": 1.644, "lineSeconds": 1.784},
{"id": "first_key", "text": "Hi, I'm Sunny! Touch the glowing key.", "spoken": "Hi, I'm Sunny! Touch the glowing key.", "file": "first_key.wav", "seconds": 3.006, "lineSeconds": 3.146},
{"id": "key_again", "text": "Touch the glowing key, like this.", "spoken": "Touch the glowing key, like this.", "file": "key_again.wav", "seconds": 2.239, "lineSeconds": 2.379},
{"id": "key_other", "text": "Nice sound! Now try the glowing one.", "spoken": "Nice sound! Now try the glowing one.", "file": "key_other.wav", "seconds": 2.814, "lineSeconds": 2.954},
{"id": "key_any", "text": "Any key will play it now.", "spoken": "Any key will play it now.", "file": "key_any.wav", "seconds": 1.611, "lineSeconds": 1.751},
{"id": "song_go", "text": "Lovely! Play each key as its block lands.", "spoken": "Lovely! Play each key as its block lands.", "file": "song_go.wav", "seconds": 3.134, "lineSeconds": 3.274},
{"id": "song_any", "text": "Any key plays the next note now.", "spoken": "Any key plays the next note now.", "file": "song_any.wav", "seconds": 2.18, "lineSeconds": 2.32},
{"id": "song_skip", "text": "Let's try the drums. Finish the song in Lessons.", "spoken": "Let's try the drums. Finish the song in Lessons.", "file": "song_skip.wav", "seconds": 3.397, "lineSeconds": 3.537},
{"id": "song_done", "text": "You just played Ode to Joy!", "spoken": "You just played Ode to Joy!", "file": "song_done.wav", "seconds": 1.819, "lineSeconds": 1.959},
{"id": "poke_me", "text": "Tip: poke me anytime to skip.", "spoken": "Tip: poke me anytime to skip.", "file": "poke_me.wav", "seconds": 2.319, "lineSeconds": 2.459},
{"id": "drum_vr", "text": "Now tap the drum on your right.", "spoken": "Now tap the drum on your right.", "file": "drum_vr.wav", "seconds": 1.785, "lineSeconds": 1.925},
{"id": "drum_again", "text": "Tap the top of the drum, like this.", "spoken": "Tap the top of the drum, like this.", "file": "drum_again.wav", "seconds": 2.485, "lineSeconds": 2.625},
{"id": "drum_skip", "text": "The drums will wait on your right.", "spoken": "The drums will wait on your right.", "file": "drum_skip.wav", "seconds": 1.869, "lineSeconds": 2.009},
{"id": "nod_again", "text": "Nod your head, like me.", "spoken": "Nod your head, like me.", "file": "nod_again.wav", "seconds": 1.751, "lineSeconds": 1.891},
{"id": "harp_vr", "text": "Now sweep your fingers across the harp strings.", "spoken": "Now sweep your fingers across the harp strings.", "file": "harp_vr.wav", "seconds": 2.958, "lineSeconds": 3.098},
{"id": "harp_again", "text": "Slide your fingers through the strings, like this.", "spoken": "Slide your fingers through the strings, like this.", "file": "harp_again.wav", "seconds": 3.086, "lineSeconds": 3.226},
{"id": "harp_skip", "text": "The harp will wait on your left.", "spoken": "The harp will wait on your left.", "file": "harp_skip.wav", "seconds": 1.911, "lineSeconds": 2.051},
{"id": "open_again", "text": "Palm toward your face, like this.", "spoken": "Palm toward your face, like this.", "file": "open_again.wav", "seconds": 2.425, "lineSeconds": 2.565},
{"id": "open_auto", "text": "I'll open it for you this time.", "spoken": "I'll open it for you this time.", "file": "open_auto.wav", "seconds": 1.741, "lineSeconds": 1.881},
{"id": "menu_tour_1", "text": "Lessons, instruments and your recordings are here.", "spoken": "Lessons, instruments and your recordings are here.", "file": "menu_tour_1.wav", "seconds": 3.162, "lineSeconds": 3.302},
{"id": "menu_tour_2", "text": "Move instruments, or play in your own room.", "spoken": "Move instruments, or play in your own room.", "file": "menu_tour_2.wav", "seconds": 2.76, "lineSeconds": 2.9},
{"id": "menu_tour_3", "text": "Exit is at the bottom. Poke any row to try.", "spoken": "Exit is at the bottom. Poke any row to try.", "file": "menu_tour_3.wav", "seconds": 3.252, "lineSeconds": 3.392},
{"id": "close_again", "text": "Show your palm again, like this.", "spoken": "Show your palm again, like this.", "file": "close_again.wav", "seconds": 2.265, "lineSeconds": 2.405},
{"id": "close_auto", "text": "I'll close it for you.", "spoken": "I'll close it for you.", "file": "close_auto.wav", "seconds": 1.242, "lineSeconds": 1.382},
{"id": "bye_first", "text": "That's the tour! Find me in the menu anytime.", "spoken": "That's the tour! Find me in the menu anytime.", "file": "bye_first.wav", "seconds": 3.123, "lineSeconds": 3.263},
{"id": "again", "text": "Like this.", "spoken": "Like this.", "file": "again.wav", "seconds": 0.783, "lineSeconds": 0.923},
{"id": "tip_middle_c", "text": "That's middle C. Every key shows its letter.", "spoken": "That's middle C. Every key shows its letter.", "file": "tip_middle_c.wav", "seconds": 3.109, "lineSeconds": 3.249},
{"id": "tip_sticks", "text": "Pinch a stick on the snare to pick it up.", "spoken": "Pinch a stick on the snare to pick it up.", "file": "tip_sticks.wav", "seconds": 2.325, "lineSeconds": 2.465},
{"id": "tip_sticks_drop", "text": "Open your hand to put the stick back.", "spoken": "Open your hand to put the stick back.", "file": "tip_sticks_drop.wav", "seconds": 2.235, "lineSeconds": 2.375},
{"id": "tip_hihat", "text": "Pinch the hi-hat stand to open it.", "spoken": "Pinch the hi hat stand to open it.", "file": "tip_hihat.wav", "seconds": 2.289, "lineSeconds": 2.429},
{"id": "tip_choke", "text": "Hold a ringing cymbal to stop it.", "spoken": "Hold a ringing cymbal to stop it.", "file": "tip_choke.wav", "seconds": 2.062, "lineSeconds": 2.202},
{"id": "tip_more_light", "text": "More light helps the headset see your hands.", "spoken": "More light helps the headset see your hands.", "file": "tip_more_light.wav", "seconds": 2.852, "lineSeconds": 2.992},
{"id": "tip_harp_colors", "text": "Red strings are C. Blue strings are F.", "spoken": "Red strings are C. Blue strings are F.", "file": "tip_harp_colors.wav", "seconds": 2.862, "lineSeconds": 3.002},
{"id": "tip_harp_damp", "text": "Rest your hand on the strings to quiet them.", "spoken": "Rest your hand on the strings to quiet them.", "file": "tip_harp_damp.wav", "seconds": 2.738, "lineSeconds": 2.878},
{"id": "tip_switches", "text": "Flip these switches for fire, wind and snow.", "spoken": "Flip these switches for fire, wind and snow.", "file": "tip_switches.wav", "seconds": 3.146, "lineSeconds": 3.286},
{"id": "tip_deck", "text": "This tape deck records what you play.", "spoken": "This tape deck records what you play.", "file": "tip_deck.wav", "seconds": 2.251, "lineSeconds": 2.391},
{"id": "tip_rec", "text": "Recording! Play something, then press STOP.", "spoken": "Recording! Play something, then press stop.", "file": "tip_rec.wav", "seconds": 3.49, "lineSeconds": 3.63},
{"id": "tip_take", "text": "Saved! Press PLAY to hear it.", "spoken": "Saved! Press play to hear it.", "file": "tip_take.wav", "seconds": 2.394, "lineSeconds": 2.534},
{"id": "tip_loop_start", "text": "Listen to the count, then play. It will repeat.", "spoken": "Listen to the count, then play. It will repeat.", "file": "tip_loop_start.wav", "seconds": 3.416, "lineSeconds": 3.556},
{"id": "tip_loop_layer", "text": "Press LOOP again to add a layer on top.", "spoken": "Press loop again to add a layer on top.", "file": "tip_loop_layer.wav", "seconds": 2.402, "lineSeconds": 2.542},
{"id": "tip_loop_mute", "text": "Touch a layer's light to mute it.", "spoken": "Touch a layer's light to mute it.", "file": "tip_loop_mute.wav", "seconds": 1.807, "lineSeconds": 1.947},
{"id": "tip_loop_undo", "text": "UNDO takes away the last layer.", "spoken": "Undo takes away the last layer.", "file": "tip_loop_undo.wav", "seconds": 1.963, "lineSeconds": 2.103},
{"id": "tip_loop_clear", "text": "Press CLEAR again to erase the loop.", "spoken": "Press clear again to erase the loop.", "file": "tip_loop_clear.wav", "seconds": 2.123, "lineSeconds": 2.263},
{"id": "tip_loop_tempo", "text": "Pick a tempo and a length before you press LOOP.", "spoken": "Pick a tempo and a length before you press loop.", "file": "tip_loop_tempo.wav", "seconds": 2.881, "lineSeconds": 3.021},
{"id": "tip_loop_click", "text": "Turn on the click to help you keep time.", "spoken": "Turn on the click to help you keep time.", "file": "tip_loop_click.wav", "seconds": 2.504, "lineSeconds": 2.644},
{"id": "tip_loop_saved", "text": "Your loop is saved in Recordings.", "spoken": "Your loop is saved in Recordings.", "file": "tip_loop_saved.wav", "seconds": 1.997, "lineSeconds": 2.137},
{"id": "lay_intro", "text": "Let's build a song in layers.", "spoken": "Let's build a song in layers.", "file": "lay_intro.wav", "seconds": 1.774, "lineSeconds": 1.914},
{"id": "lay_drums", "text": "First a beat: press LOOP, then tap the drums.", "spoken": "First a beat: press loop, then tap the drums.", "file": "lay_drums.wav", "seconds": 3.534, "lineSeconds": 3.674},
{"id": "lay_harp", "text": "Now press LOOP again and sweep the harp.", "spoken": "Now press loop again and sweep the harp.", "file": "lay_harp.wav", "seconds": 2.695, "lineSeconds": 2.835},
{"id": "lay_piano", "text": "Last layer: press LOOP and play the piano.", "spoken": "Last layer: press loop and play the piano.", "file": "lay_piano.wav", "seconds": 3.038, "lineSeconds": 3.178},
{"id": "lay_done", "text": "That's your own song, in layers!", "spoken": "That's your own song, in layers!", "file": "lay_done.wav", "seconds": 2.151, "lineSeconds": 2.291},
{"id": "swipe_again", "text": "Swipe fast, with your palm facing sideways.", "spoken": "Swipe fast, with your palm facing sideways.", "file": "swipe_again.wav", "seconds": 3.055, "lineSeconds": 3.195},
{"id": "tip_seat_turn", "text": "Pinch the air and pull sideways to turn your seat.", "spoken": "Pinch the air and pull sideways to turn your seat.", "file": "tip_seat_turn.wav", "seconds": 3.091, "lineSeconds": 3.231},
{"id": "tip_summon", "text": "Pinch and hold with both hands to bring everything back.", "spoken": "Pinch and hold with both hands to bring everything back.", "file": "tip_summon.wav", "seconds": 3.52, "lineSeconds": 3.66},
{"id": "tip_lock", "text": "To keep them still, lock instruments in the menu.", "spoken": "To keep them still, lock instruments in the menu.", "file": "tip_lock.wav", "seconds": 3.281, "lineSeconds": 3.421},
{"id": "tip_arrange", "text": "Pinch an instrument to pick it up.", "spoken": "Pinch an instrument to pick it up.", "file": "tip_arrange.wav", "seconds": 2.086, "lineSeconds": 2.226},
{"id": "tip_arrange_ray", "text": "Far away? Point at it and pinch.", "spoken": "Far away? Point at it and pinch.", "file": "tip_arrange_ray.wav", "seconds": 2.784, "lineSeconds": 2.924},
{"id": "tip_arrange_lift", "text": "Raise or lower your hand to set its height.", "spoken": "Raise or lower your hand to set its height.", "file": "tip_arrange_lift.wav", "seconds": 2.481, "lineSeconds": 2.621},
{"id": "tip_turn", "text": "Turn your wrist to turn it.", "spoken": "Turn your wrist to turn it.", "file": "tip_turn.wav", "seconds": 1.511, "lineSeconds": 1.651},
{"id": "tip_turn_two", "text": "Or hold it with both hands and twist.", "spoken": "Or hold it with both hands and twist.", "file": "tip_turn_two.wav", "seconds": 2.594, "lineSeconds": 2.734},
{"id": "tip_arrange_done", "text": "Poke Done when it feels right. Reset starts over.", "spoken": "Poke Done when it feels right. Reset starts over.", "file": "tip_arrange_done.wav", "seconds": 3.563, "lineSeconds": 3.703},
{"id": "tip_arrange_saved", "text": "I'll remember this setup next time.", "spoken": "I'll remember this setup next time.", "file": "tip_arrange_saved.wav", "seconds": 2.381, "lineSeconds": 2.521},
{"id": "tip_panel_move", "text": "Pinch and hold the menu's edge to move it.", "spoken": "Pinch and hold the menu's edge to move it.", "file": "tip_panel_move.wav", "seconds": 2.583, "lineSeconds": 2.723},
{"id": "tip_scroll", "text": "Drag the list up or down to see more.", "spoken": "Drag the list up or down to see more.", "file": "tip_scroll.wav", "seconds": 2.372, "lineSeconds": 2.512},
{"id": "tip_back", "text": "Poke the title to go back.", "spoken": "Poke the title to go back.", "file": "tip_back.wav", "seconds": 1.663, "lineSeconds": 1.803},
{"id": "tip_lessons", "text": "Pick an instrument, then a song.", "spoken": "Pick an instrument, then a song.", "file": "tip_lessons.wav", "seconds": 2.193, "lineSeconds": 2.333},
{"id": "tip_lesson_play", "text": "Play each block as it lands. It waits for you.", "spoken": "Play each block as it lands. It waits for you.", "file": "tip_lesson_play.wav", "seconds": 3.205, "lineSeconds": 3.345},
{"id": "tip_lesson_kick", "text": "For the kick, nod your head or tap the pad.", "spoken": "For the kick, nod your head or tap the pad.", "file": "tip_lesson_kick.wav", "seconds": 2.939, "lineSeconds": 3.079},
{"id": "tip_lesson_harp", "text": "Pluck the string where the block lands.", "spoken": "Pluck the string where the block lands.", "file": "tip_lesson_harp.wav", "seconds": 2.377, "lineSeconds": 2.517},
{"id": "tip_lesson_done", "text": "Well done! Replay it, or pick another song.", "spoken": "Well done! Replay it, or pick another song.", "file": "tip_lesson_done.wav", "seconds": 3.356, "lineSeconds": 3.496},
{"id": "tip_instruments", "text": "Play them all, or pick one to play alone.", "spoken": "Play them all, or pick one to play alone.", "file": "tip_instruments.wav", "seconds": 2.798, "lineSeconds": 2.938},
{"id": "tip_classic", "text": "That's the original sound. Poke again for the new one.", "spoken": "That's the original sound. Poke again for the new one.", "file": "tip_classic.wav", "seconds": 3.417, "lineSeconds": 3.557},
{"id": "tip_recordings", "text": "Your recordings show up here. Poke one to play.", "spoken": "Your recordings show up here. Poke one to play.", "file": "tip_recordings.wav", "seconds": 3.523, "lineSeconds": 3.663},
{"id": "tip_delete", "text": "Poke Delete again to remove it.", "spoken": "Poke Delete again to remove it.", "file": "tip_delete.wav", "seconds": 1.875, "lineSeconds": 2.015},
{"id": "tip_exit", "text": "Poke Exit again to leave. See you soon!", "spoken": "Poke Exit again to leave. See you soon!", "file": "tip_exit.wav", "seconds": 3.07, "lineSeconds": 3.21},
{"id": "tip_hide_sunny", "text": "You can hide me from the menu.", "spoken": "You can hide me from the menu.", "file": "tip_hide_sunny.wav", "seconds": 1.772, "lineSeconds": 1.912},
{"id": "tip_mr_table", "text": "This is your room! The piano is on your table.", "spoken": "This is your room! The piano is on your table.", "file": "tip_mr_table.wav", "seconds": 2.96, "lineSeconds": 3.1},
{"id": "tip_mr_floor", "text": "Pinch empty air to put the piano on a table.", "spoken": "Pinch empty air to put the piano on a table.", "file": "tip_mr_floor.wav", "seconds": 2.708, "lineSeconds": 2.848},
{"id": "tip_mr_place", "text": "Point at a table and pinch.", "spoken": "Point at a table and pinch.", "file": "tip_mr_place.wav", "seconds": 1.921, "lineSeconds": 2.061},
{"id": "tip_mr_snow", "text": "Look, it's snowing outside your window.", "spoken": "Look, it's snowing outside your window.", "file": "tip_mr_snow.wav", "seconds": 2.514, "lineSeconds": 2.654},
{"id": "tip_mr_window", "text": "Point at a wall and pinch to add a window.", "spoken": "Point at a wall and pinch to add a window.", "file": "tip_mr_window.wav", "seconds": 2.717, "lineSeconds": 2.857},
{"id": "tip_mr_permission", "text": "Allow spatial data in settings to find your windows.", "spoken": "Allow spatial data in settings to find your windows.", "file": "tip_mr_permission.wav", "seconds": 3.157, "lineSeconds": 3.297},
{"id": "tip_mr_setup", "text": "Set up my room, in the menu, finds your windows.", "spoken": "Set up my room, in the menu, finds your windows.", "file": "tip_mr_setup.wav", "seconds": 3.591, "lineSeconds": 3.731},
{"id": "tip_mr_anchor", "text": "Your piano is right where you left it.", "spoken": "Your piano is right where you left it.", "file": "tip_mr_anchor.wav", "seconds": 2.13, "lineSeconds": 2.27},
{"id": "tip_mr_leave", "text": "Music room, in the menu, takes you back.", "spoken": "Music room, in the menu, takes you back.", "file": "tip_mr_leave.wav", "seconds": 3.05, "lineSeconds": 3.19}
]
```

**Line rules these meet** (and the lint test of section 6 checks):
- at most 12 words (the longest is 10);
- clip at most 4 s (the longest is 3.59 s);
- plain words; no brand or platform names ("headset" and "spatial data" are generic);
- every line ends with punctuation;
- `spoken` differs from `text` only by case or hyphens.

**Proposed extension keys for `guide-lines.json`.** Additive, so today's `GuideLinesTests` keep reading `lines`, `praise`,
`steps`, `timing`, `robot`, `chirps` and `voice`:
- `firstRun[]` (the S0–S11 rows of 2.2): `{id, line, again, giveUp, demo, anchor, success, idle: 8, fail: 15, onGiveUp, onSkip}`;
- `tips[]` (section 3.2): `{id, title, lines[], again, demo, anchor, trigger, class, success, covers[]}`.

The tour's `steps` stay as the exporter writes them.

### 5.4 Rendering the clips with the existing pipeline

**The pipeline.**
- The voice render script `make-voice.mjs` exports `render(text, wavPath, VOICE)`. It runs eSpeak NG
  `-v en-us+Annie -s 150 -p 55 -P 70 -g 0 -a 120`, then trims silence below 2 % of the peak (keeping 30 ms), normalises
  the peak to 0.89 and adds 5 ms fades.
- `export-voice-wav.mjs` calls that same `render()` per line of the line table, and
  writes 22.05 kHz mono WAVs plus `manifest.json` (`{ id: { file, seconds, rate } }`).
- `export-guide-lines.mjs` writes `guide-lines.json`.
- The **light robot effect is not baked in**. `GuideVoice` / `RobotVoice` add it once at load, in the app: ring
  0.92 / 0.08 at 80 Hz, a 5 ms comb, high-pass 120 Hz, +2 dB at 2.5 kHz.
- Tools needed: `brew install espeak-ng` (1.52.0 on this Mac) and, for `.m4a` files only, macOS `afconvert`.

**Two routes. Pick one (open question 12).**

**Route A: the original line table stays the master.**
1. Add the 86 new ids and the 3 changed texts to `LINES`, and the 10 `SPOKEN` overrides, in the original line table.
2. Re-render its `.m4a` clips and the manifest; lines the table never says are ignored.
3. `node export-guide-lines.mjs`
4. `node export-voice-wav.mjs`. This writes `voice-wav/`.
5. Copy `voice-wav/*.wav` and `manifest.json` to `Assets/MusicRoom/Guide/Resources/Voice/`,
   and `guide-lines.json` to `Guide/Resources/guide-lines.json`.

**Route B (recommended): a project-owned table.**
- Keep all 108 lines in `Docs/11-first-five/sunny-lines.mjs` (`export const LINES = {...}; export const SPOKEN = {...}`,
  the line table's shape).
- Render them with a 25-line wrapper that imports the voice script's `render` and `VOICE` unchanged. This is what produced the
  seconds above:

```js
// export-voice-wav-unity.mjs  [outDir] [linesModule]
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
const OUT = process.argv[2] || 'voice-wav';
const TABLE = process.argv[3] || 'Docs/11-first-five/sunny-lines.mjs';
const { render, VOICE } = await import(pathToFileURL('make-voice.mjs').href);
const { LINES, SPOKEN } = await import(pathToFileURL(TABLE).href);
mkdirSync(OUT, { recursive: true });
const manifest = {}, lines = [];
for (const [id, text] of Object.entries(LINES)) {
  const spoken = SPOKEN[id] || text;
  const { rate, seconds } = render(spoken, join(OUT, `${id}.wav`), VOICE);   // same voice, same tidy
  manifest[id] = { file: `${id}.wav`, seconds: +seconds.toFixed(3), rate };
  lines.push({ id, text, spoken, file: `${id}.wav`, seconds: +seconds.toFixed(3), lineSeconds: +(seconds + 0.14).toFixed(3) });
}
writeFileSync(join(OUT, 'manifest.json'), JSON.stringify(manifest, null, 1) + '\n');
writeFileSync(join(OUT, 'lines.json'), JSON.stringify(lines, null, 1) + '\n');   // merge into guide-lines.json "lines"
console.log(`${lines.length} lines in ${OUT}`);
```

**Then, in either route:**
1. Copy the WAVs and the manifest into `Guide/Resources/Voice/`, and merge `lines.json` into `Guide/Resources/guide-lines.json`
   (`lineCount` 108).
2. Every new WAV needs today's import settings: Decompress On Load, Preload Audio Data, Force To Mono, Vorbis quality 0.7.
   Today these live in each WAV's `.meta`, and no AssetPostprocessor exists. Add one:
   `Editor/GuideVoiceImport.cs` (`OnPreprocessAudio` for paths under `Guide/Resources/Voice/`), so new lines cannot be
   imported wrong.
3. Run `Tools/unity.sh compile → test-edit`. `GuideLinesTests` re-checks WAV lengths against the manifest.

**Size.**
- 108 clips, 270.7 s, about 11.9 MB as 22.05 kHz 16-bit.
- Robotised and resident at 48 kHz float, all of them would be **53 MB**. Section 7.1 renders lazily instead.

---------------------------------------------------------------------------------------------------------------------------

## 6. Maintenance rule: every feature brings its Sunny tip

### 6.1 Checklist (copy into every feature task, and into each module's README "Adding a feature" section)

1. **Name the feature.** Add a `FeatureId` to `Core/Guide/FeatureCatalog.cs`: id, owner module, kind (Gesture / Row /
   Screen / Device control / Mode / Passive), and for Passive a one-line reason the user approved.
2. **Tag it in code.**
   - `[Feature("drums.hihat")]` on the recogniser or control class;
   - a menu row's `RowKind` maps to its id in `MenuRow.FeatureOf(RowKind)`;
   - a new gesture adds its `GestureKind` value and publishes `GestureEvent` when it fires.
3. **Write the tip** in `Guide/TipBook.cs`: trigger signal, conditions, class, line ids, demo kind and anchor, success
   test, Tips-list title, and `Covers = { FeatureId }`. If the feature changes something the tour shows (a home row, the
   start panel), update `Guide/TourScript.cs` and section 2 of this spec.
4. **Write the line(s).**
   - ≤ 12 words, plain English, no brand names;
   - `SPOKEN` for all-caps labels and hyphens;
   - add them to the line table (route A or B, 5.4).
5. **Render and copy** the clips (5.4). Never hand-edit a WAV or the manifest.
6. **Publish the signal** the tip waits for (an event, or a polled state in `GuideSignals`), so the trigger and success
   test can see it.
7. **Run** `test-edit` and `test-play`. `SunnyCoverageTests` and `FirstRunWalkthroughTests` must pass.
8. **Update this spec's inventory** (section 1) and the count in the Tips row note.

### 6.2 Tests that fail when a feature has no tip

| Test (EditMode unless noted) | Fails when |
|---|---|
| `SunnyCoverageTests.EveryRowKindHasATip` | Any `RowKind` value except None and Back has no tour step or tip whose `Covers` includes `MenuRow.FeatureOf(kind)`. A new row added to the enum without a tip fails here |
| `SunnyCoverageTests.EveryScreenHasATip` | Same, over `MenuScreen` (Home, Lessons, Courses, Lesson, Instruments, Recordings, Take, Start, Arrange, Guide, Tips) |
| `SunnyCoverageTests.EveryGestureHasATip` | Same, over `GestureKind` |
| `SunnyCoverageTests.EveryFeatureAttributeIsCovered` | Any `[Feature]` id found by reflection over the `MusicRoom.*` assemblies is missing from `FeatureCatalog`, or is neither covered nor Passive with a reason |
| `SunnyCoverageTests.EveryLineExists` | A line referenced by `TourScript` or `TipBook` is missing from `guide-lines.json`, from `Resources/Voice/<id>.wav`, or from the manifest, or its length differs by more than 1 ms |
| `SunnyCoverageTests.NoOrphanLines` | A line in `guide-lines.json` is used by nothing (a removed feature left its line behind) |
| `SunnyLineLintTests` | A line has more than 12 words, a clip is longer than 4.0 s, a word comes from the brand list (Meta, Quest, Oculus, Horizon, Apple, Vision, Google, Android, Unity, …), or a WAV is not 22.05 kHz mono |
| `MenuRowSweepTests` (PlayMode) | It drives the real menu through every screen in every state (it reuses `MenuScreenTests`' helpers: MR / NoRooms, Sunny present / absent, arrange on / off, a lesson in countdown / done, takes / none, Exit armed, Delete armed) and collects every painted row's `RowKind`. Any kind not covered fails, including a row the enum test cannot see because it reuses an existing kind with a new label (each label maps to a feature id) |
| `GestureCatalogTests` (PlayMode) | It runs the scripted-hand gesture fixtures already in Layout, Menu, Drums and MR tests and records every `GestureEvent` kind fired. Any kind without a tip fails |

---------------------------------------------------------------------------------------------------------------------------

## 7. Implementation notes (Unity)

### 7.1 Guide module (`Assets/MusicRoom/Guide`)

| File | Change |
|---|---|
| `GuideLines.cs` | The 22-line table becomes generated data: `GuideLines.Data.cs`, written by the export (ids, texts, manifest seconds). Keep the timing constants. Today's `StepId` / `Steps` move to `TourScript`. Constants added: `RedemoAfter = 8`, `GiveUpAfter = 15`, `SkipGap = 0.35`, `PokeDebounce = 0.6` |
| `Sunny.cs` | Keeps speech, face, mouth, blink, placement and the leave animation. **The run moves out.** New public methods: `Say(id)`, `Demo(kind, anchor)`, `EndDemo()`, `Arrive()`, `LeaveQuietly()`, `Poked(Vector3 dir)` (push, blink, bop), `NoticeFinger(Vector3 tip)`. Its existing `Skip()` / `Complete()` / `Timeout()` / `After()` / `SayNext()` logic becomes `ScriptRunner`'s |
| **new** `ScriptRunner.cs` | A plain-C# state machine that runs a `Script` (FirstRun, Replay, Layers, or a one-tip script). Per step: line → demo → wait (idle and fail timers, 2.1) → success / again / give-up / skip → praise. It keeps finish-the-sentence (today's `Wait` and `Queued`). Driven by `ISunnyWorld` signals and a scripted clock, so it is EditMode-testable like `SunnyTourTests` |
| **new** `TourScript.cs` | The S0–S11 table of 2.2 as readonly structs: id, line, again, giveUp, demo, anchor, success predicate (an enum), give-up action, skip target. Also the Replay and Layers variants |
| **new** `TipBook.cs` | The 61 tips of 3.2: id, title, lines, again, demo, anchor, trigger signal, conditions (a small predicate enum plus parameters), class, success predicate, `Covers`. Static readonly; no allocation |
| **new** `TipScheduler.cs` | 3.1: pending set, `waitMax`, spacing, the 90 s and 3-per-10-min rules, gates (tipsOn, hidden this launch, script running), persistence (`tipsSeen`, `p1Launches`). Plain C# |
| **new** `SunnyPoke.cs` | 4.1–4.2: per-hand arming, the sphere and bubble tests, speed, guards, debounce. Plain C# fed `HandState`s, the play-surface query and the panel rectangles |
| **new** `SunnyPerch.cs` | 4.6: the candidate search against `Rig.Zone`, `IInstrument.PlaySurfaceAt` boxes and panel bounds. Bubble side flip. The off-gaze drift |
| **new** `GuideSignals.cs` | Subscribes to every event of 7.6 and polls the states. Each frame it presents a compact signal set (a bitfield plus counters) to the runner and the scheduler. 0 B per frame |
| `SunnyDemo.cs`, `SunnyDemoMesh.cs` | The new demo kinds (7.3) and **world anchors**: the demo pose is computed in world space and written into Sunny's merged mesh after `InverseTransformPoint`, which already happens every demo frame. A second hand for TwoHandPinch and the two-hand twist adds about 400 vertices to the same merged mesh, so the draw budget stays at 6. PointPinch reuses the dashed-arrow line mesh as the ray |
| `SunnyView.cs` | Bubble side flip; push and squash; face turn toward a finger; caption fade on skip |
| `SunnyAdapters.cs` | `AppWorld` gains reads for: the tutorial's live target, drum piece and harp play-point poses, panel rectangles and the spotlight, arrange state, MR phase, switch and deck positions, looper state, the head pose for gaze dwell. `EngineVoice.Prepare(id)` (lazy render) |
| `GuideModule.cs` | First launch: start `FirstRun` on the first presenting frame with the HMD mounted **while the start panel is up** (S0 waits silently). Wire `SunnyPoke` (and the desktop collider click), `TipScheduler`, `GuideSignals`, persistence. `IGuide` additions (7.2). Shift+H stays |
| `Resources/guide-lines.json`, `Resources/Voice/` | 108 lines (5.3–5.4) |
| README, CONTRACT-REQUESTS.md | Document the above; list the requests of 7.2–7.5 |

**Allocation and frame order.**
- Everything runs in `FramePhase.Guide`, step 16, after the instruments, the grab and the recorder, so play and grab
  stamps of the same frame are visible.
- The 120-tick zero-allocation test is extended to cover a running script, a pending tip and a poke.

**Voice memory.**
- Make `GuideVoice` data-driven: line ids come from `Voice/manifest.json` (a TextAsset), so adding a line never needs
  Audio code again.
- Render robotised buffers **lazily**:
  - at boot, only the first-run set (`start_help`, `first_key`, `key_*`, `song_*`, `praise_*`, `leave`), right after
    middle C's neighbours, as the intro lines are today;
  - every other line through `PrepareLine(id)` when its tip becomes pending; the chain costs a few ms, and the existing
    1.2 s LATE rule covers a miss;
  - at most 24 rendered buffers resident (LRU, about 12 MB);
  - `GetData` from the clip only at render time, then drop the raw copy.
- Grow the bank: `VoiceSlot0 = 200` with 108 lines needs `SlotCount ≥ 308`, or a separate voice bank. Today `SlotCount` is
  240, which leaves room for 40 lines.

### 7.2 New and changed contracts (Core)

```csharp
// Core/Contracts/Guide.cs
public interface IGuide {
    bool Present { get; } bool Done { get; }
    void Start(); void Dismiss(); void Toggle();                   // unchanged
    void StartScript(GuideScript script);                          // FirstRun, Replay, Layers
    void PlayTip(string tipId);                                    // the Tips list
    bool TipsEnabled { get; set; }                                 // musicroom.tipsOn
    int TipsHeard { get; } int TipsTotal { get; }
    bool IsTipSeen(string tipId);
    IReadOnlyList<TipInfo> Tips { get; }                           // id, title, seen (the Tips screen rows)
    bool BusyScript { get; }                                       // a script is running (tips and Exit confirm wait)
}
public enum GuideScript : byte { FirstRun, Replay, Layers }
public readonly struct TipInfo { public readonly string Id, Title; public readonly bool Seen; }
public enum StepOutcome : byte { Done, GaveUp, Skipped }
public struct GuideStepEvent { public string StepId; public StepOutcome Outcome; public float At; }
public struct GuideTipEvent { public string TipId; public bool Started; public bool Seen; }
public struct SunnyPokeEvent { public string StepOrTipId; }

// Core/Guide/FeatureCatalog.cs (new): the inventory of section 1 as data.
public enum FeatureKind : byte { Gesture, Row, Screen, Control, Mode, Passive }
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field, AllowMultiple = true)]
public sealed class FeatureAttribute : Attribute { public FeatureAttribute(string id) { Id = id; } public string Id { get; } }
public enum GestureKind : byte { PalmSummon, MenuPoke, PalmClose, ListDrag, PanelMove, PageTurn, SeatTurn, TwoHandSummon,
    InPlayGrab, WristTurn, OffHandTurn, TwoHandTwist, ArrangeNear, ArrangeRay, ArrangeLift, StickPickUp, StickDrop,
    HiHatPinch, CymbalChoke, NodKick, HarpSweep, HarpDamp, MrPlacePinch, MrWindowPinch, SunnyPoke }
public struct GestureEvent { public GestureKind Kind; public HandSide Hand; }

// Core/Contracts/Menu.cs
public enum MenuScreen { Home = 0, Lessons, Courses, Lesson, Instruments, Recordings, Take, Start, Arrange, Guide, Tips }  // + Guide, Tips
public interface IMenuPanel {   // additions
    bool StartPending { get; }                                     // u-arrange
    bool Spotlight(string rowId);                                  // light one home row with the hover highlight (no repaint); null clears
    bool TryGetRowRect(string rowId, out Vector3 center, out Vector3 normal, out Vector2 size);  // demo anchors
    void Show(HandState hand = null);                              // already there: open_auto uses Show(null)
}
public struct MenuRowEvent { public string RowId; public MenuScreen Screen; public bool Confirming; }   // every press
public struct StartChoiceEvent { public bool InMyRoom; }
```

**Row ids.** Stable strings, mapped from `RowKind` in `MenuRow.IdOf`: lessons, instruments, arrange, recordings, room,
setup, guide, exit, inst.piano, inst.drums, inst.harp, course, layout.all, layout.piano, layout.drums, layout.harp, lock,
classic, take, play, tstop, delete, stop, replay, courses, back, playHere, playRoom, done, reset, tour, tips, tip, layers,
tipsOn, hide. Core cannot see `RowKind` (it lives in the Menu assembly), hence the strings.

**Menu changes.**
- **`Spotlight`** drives the existing `_HoverRect0/1` uniforms while no finger hovers. A finger hover wins.
- **The Guide screen and the Tips screen** (3.3).
- **The guide row's two behaviours** (Hide when Sunny is present, open the Sunny screen when absent).
- **`MenuRowEvent`.**
- **Exit confirm.** Exit's 3 s window counts from the end of Sunny's line when `tip_exit` speaks; `ExitConfirmMs` gets a
  `+ GuideLineRemaining` grace. Otherwise the tip is dropped rather than eat the window.
- **The start panel's 8 s stall.** Sunny's helper is Guide-side. Menu only publishes `StartChoiceEvent` and keeps the
  panel up. At give-up, Guide calls `IMenuPanel` "answer Play here", a new `AnswerStart(bool inMyRoom)`.

### 7.3 Demo kinds (Guide, `SunnyDemo`)

| Kind | Pose (all loop on 3 s, ease u²(3−2u)) | Anchor |
|---|---|---|
| Palm, Poke, Swipe, Nod | Today's | @ Sunny (Poke may be world-anchored at a row or button rect) |
| **Press** | Index out, others curled (`Curl(.9, idx 0, thumb .5)`); two 1 cm pushes down at t0 = 0.3 and 1.2 s | The key's front 2 cm (piano-local `(key.X, top, 0.02)`); a harp play point for plucks |
| **Strike** | Fingers together, wrist flick: two taps 6 cm down at t0 = 0.3 and 0.9 s | 8 cm above the snare's centre |
| **Sweep** | Open hand, fingers spread, palm toward the strings; slides through 6 strings in 0.4 s at t0 = 0.3 and 1.5 s; fades like Swipe | `IHarpApi.PlayPoint` of strings 8..13, offset 3 cm toward the player |
| **Pinch** | Thumb and index meet (gap 0) at t = 0.6, held to 1.6 | The stick shaft, hat stand pinch spot, instrument case, panel edge or empty air |
| **PointPinch** | Index points along a dashed ray (the arrow line mesh, 0.6 m), then pinches at t = 1.0 | @ Sunny, ray toward the target |
| **PinchPull** | Pinch, then a 14 cm sideways pull over 0.6 s, with a 45° tick | @ Sunny |
| **TwoHandPinch** | Both ghost hands pinch at t = 0.4, hold 0.8 s (a dwell ring fills), release | @ Sunny, hands ±0.12 m |
| **WristTurn** | Pinching hand rolls 40° about the forearm, there and back | The held rig's grab point |
| **Lift** | Pinching hand rises 6 cm and returns | The held rig |
| **Hold** | Flat fingers rest on the target for 1 s | The cymbal's edge |
| **PalmRest** | Flat palm settles onto the strings for 1 s | The strings' middle |
| **Drag** | Index touches and moves 6 cm down | The list's centre |

**Ghost-hand look:** today's cyan `#5fd8ff` additive at 0.5. See open question 1: the user rejects glowing UI.

### 7.4 Lessons, Room, Harp, Audio

**Lessons**
- `ITutorial.Start(string lessonId, LessonOptions options)` with
  `LessonOptions { bool NoCountdown, Accompany, QuietMistakes, Forgiving; bool Hidden }`. `Hidden` = the lesson is not
  in the menu's lists.
- `bool Forgiving { get; set; }`, live.
- `bool TryGetLiveTarget(out Vector3 world, out Vector3 up, out InstrumentKind kind, out int midiOrString)`.
- `TutorialHitEvent { LessonId, EventIndex, Hit }`.
- Accompaniment playback (2.5) through `IAudioEngine.Piano` / `Release`.
- Load `lessons-firstrun.json`.
- Tests: the chords fire on hit in wait mode, never record, never count; forgiving mode; no red flash with `QuietMistakes`.

**Looper ⚠.** Publish
`LoopEvent { Phase: CountIn | Recording | LayerDone | Muted | Undo | ClearArmed | Cleared | Stopped | Saved; int Layers }`,
and offer `ILooper.Configure(bpm, bars, metronome)` plus control rects for demo anchors.

**Room**
- `IRoomMoments.Celebrate()` (2.6).
- `ISnowWeather.Gust(float strength)`.
- `IAmbienceSwitches.TryGetLeverPose(kind, out Pose)` for anchors and gaze.

**Harp.** Nothing new. `PlaybackPluck` for the chord and `PlayPoint` for anchors exist today.

**Audio**
- `ChirpKind.Poke` (4.4).
- Data-driven `GuideVoice` and lazy render (7.1).
- `PrepareLine(id)`.
- Render the first-run notes early: C4–G4 (60–67) and the chord notes (36, 47, 48, 50, 52, 55), if the active piano
  renders buffers. The Classic piano does; the new live piano needs nothing.

### 7.5 Layout and MR

**Layout**
- `ILayoutService.FitToPlayer()` and `FitEvent { KeyTopY, KeyFrontDistance }`. First run only, and only with no saved
  arrangement for the layout (u-arrange's `ArrangementCodec` wins).
  - `eyeY` = the median head height over the last 0.5 s.
  - **Key-top height** = clamp(eyeY − 0.48, 0.55, 0.95) (`ArrangeConstants.PianoKeyMin` / `Max`).
  - **Key-front distance from the eyes** = clamp(0.36 + 0.4·(eyeY − 1.20), 0.30, 0.42). It becomes the smaller of that
    and 0.55 × the farthest forward index-tip reach seen while the start panel was up (the poke reach).
  - The piano eases 0.6 s (only if it moves more than 2 cm), then the resolver settles the others.
  - All numbers are **UNVERIFIED** until a device session.
- `SeatTurnEvent`.
- `ArrangedEvent.Source { Summon, Recentre, Layout, Fit, MixedReality }`.
- `GrabEvent { Rig, Started, InArrange, ViaRay, HeightDelta, YawDelta }`.
- `GestureEvent` from the sweep, swivel, summon, grab, twist and arrange recognisers.

**MR**
- `IMixedReality.Phase { Idle, Entering, OnTable, OnFloor, Placing, Aiming }`.
- `bool PianoFromAnchor`.
- `int VisiblePortals`.
- `MrPhaseEvent`.
- `MrModule.SuppressInfoHint(seconds)` (3.2 note).

### 7.6 Signals Sunny listens to

| Signal | Source |
|---|---|
| Existing | `MenuEvent`, `LayoutChangedEvent`, `TurnEvent`, `ArrangedEvent`, `NodEvent`, `RecorderEvent` (every play: piano / drum / harp / hat / choke / damp), `RecorderStateEvent`, `TutorialEvent`, `LibraryChangedEvent`, `AmbienceEvent`, `MixedRealityEvent`, `HmdMountEvent`, `ArrangeModeEvent` (u-arrange), `GuidePresenceEvent` |
| New | `StartChoiceEvent`, `MenuRowEvent`, `FitEvent`, `TutorialHitEvent`, `SeatTurnEvent`, `GrabEvent`, `GestureEvent`, `MrPhaseEvent`, `LoopEvent`, `FastMotionPolicy.Blocked` (polled) |
| Polled every frame | Head pose (gaze dwell: the angle to a target ≤ 25° for 1.5 s), hand-to-kit distance, ringing cymbals and strings (via the recorder stream plus the hit time), `IDrumsApi.AnyStickHeld`, `ILayoutService.Locked`, `IAudioEngine.ClassicSound` |

### 7.7 Tests to add or change

**EditMode** (`Tests/EditMode/Guide`)

| Test | Checks |
|---|---|
| `GuideLinesTests` | Updated to 108 lines, plus the extension keys |
| `ScriptRunnerTests` | Scripted clock with `FakeWorld` / `FakeVoice`. Per step: success mid-line (praise ≥ 0.3 s after the line), re-demo at exactly 8 s after `L` (±1 frame) with its again line, give-up at 15 s with its line and action, progress resetting idle (S3 resets fail too), the forgiving switch in S2 / S3, `open_auto` calling `Show`, `close_auto` calling `Hide`, the S9 spotlight times, a mode row in S9 ending at S11, unmount freezing timers and restarting the step, Hide ending the run, the 3-skip rule, the passive path finishing within 200 s of script time, the Replay variant skipping absent instruments |
| `SunnyPokeTests` | Fires on entry at ≥ 0.10 m/s, never at 0.05 m/s; arming from 0.10 m, no re-fire without leaving; 0.6 s debounce across hands; played 0.3 s ago never, 0.5 s ago yes; pinching, stick and play-surface guards; menu face wins; bubble front-plane crossing; arriving and leaving ignored; the cut line gets no closing chirp, then the bop, and the next line at +0.35 s |
| `SunnyPerchTests` | The single-harp layout picks the mirrored spot, with both volumes ≥ 0.12 m from the strings' zone; the start-panel spot; easing; the off-gaze drift ≤ 0.15 m |
| `TipSchedulerTests` | Once-ever for P2 / P3, once per launch for P1; spacing; `waitMax` drop without marking seen; the 90 s and 3-per-10-min rules; tipsOn Off; Hide for the launch; a cut tip stays unseen; persistence round trip through `MemorySettingsStore`; `tip_hide_sunny` appended to the 3rd tip |
| `SunnyCoverageTests`, `SunnyLineLintTests` | 6.2 |
| `SunnyDemoTests` | Extended with each new demo kind's poses at sample times, and world-anchor conversion |

**PlayMode**
- `GuideModuleTests` is extended: the real start panel (`BootOptions.StartPanel = true`), poke → Sunny ticks → a desktop
  click on Sunny skips.
- `TipsPlayModeTests`: one test per tip family. Fire the trigger through the real module event, check it is shown once
  and persisted, then replay it from the Tips screen.
- `MenuRowSweepTests`, `GestureCatalogTests` (6.2).

### 7.8 The automated first-run walkthrough (`Tests/PlayMode/Integration/FirstRunWalkthroughTests.cs`)

**Boot**
- The whole app through `TestBoot`: simulated headset (`App.IsPresenting`), `BootOptions.StartPanel = true`, a fresh
  `MemorySettingsStore`.
- Manual frames at exactly 1/72 s (`App.Step`) and the scripted audio clock (`audioClock: () => t`), as in
  `GuideModuleTests`.
- Real frames are spent only while the audio workers render voice lines.
- Sounds are asserted on `AudioTestTap.Calls`: `Piano` with its time, `VoiceLine` ids, `Chirp` kinds.

**The scripted player.** A small agent drives a `FakeHandSource` (`HandPose.Open`, `WithIndexTipAt`, `WithPinch`,
`OpenFacing`) from Sunny's public state:

| On | The player |
|---|---|
| S0 | Pokes "Play here" via `IMenuPanel.TryGetRowRect` |
| S2 / S3 | Moves the right index tip onto the live target from `ITutorial.TryGetLiveTarget` and presses 1 cm |
| S5 | Taps the snare head |
| S6 | Nods: drives the head pose through the rig's fake head |
| S7 | Sweeps the index across strings 8..13 in 0.3 s |
| S8 / S10 | Holds the open-palm summon pose 0.3 m from the face for 0.6 s |

Reaction delay, misses and pokes are parameters, giving the four models of 2.4.

**Assertions**
1. **Prompt player:**
   - the first `AudioCall.Piano` happens **< 10.0 s** after the start panel's `MenuEvent{Open}`;
   - the first-run lesson `Done` happens **< 120 s** after it;
   - `IRoomMoments.Celebrate` is called once;
   - every `GuideStepEvent` has Outcome `Done`;
   - `GuidePresenceEvent{false}` comes **< 90 s** after it;
   - `TourDone == 1` and `tourVersion == 2`.
2. **Typical player:** song **< 120 s**, Sunny gone **< 240 s**. The re-demo `VoiceLine`s appear 8 s (±1 frame) after the
   preceding line's end on the failed steps.
3. **Passive player:**
   - every step `GaveUp`;
   - `IMenuPanel.Show` and `Hide` are called by Sunny;
   - gone **< 240 s**;
   - **watchdog:** Sunny's (script step, state) never stays the same for more than 20 s of frame time, so the script
     can never get stuck;
   - no exception and no "Unhandled log message".
4. **Poker:**
   - each poke cuts the line (`StopVoiceLine`, no `Chirp(Close)`) and plays `Chirp(Poke)`;
   - the 3-skip jump to S8, and S11 within 40 s;
   - no instrument note is triggered by a poke.
5. **Wrong-key player** (hits D4 first): `key_other` exactly once, no red flash, the song completes.
6. **MR variant:**
   - "Play in my room" with `MrDevOptions.SceneJson` (MeshLivingRoom2);
   - S2 waits for the piano to land, and the first sound comes < 10 s after the landing;
   - S4 runs without the room effects;
   - `tip_mr_table` comes after S11.
7. **Unmount mid-S3:** the line is cut and the timers freeze; on mount S3 restarts from `song_go` and the lesson keeps
   its progress.
8. **Allocation:** 600 frames of a running script and a pending tip allocate 0 B (`Is.Not.AllocatingGCMemory()` around
   the Guide tick).

---------------------------------------------------------------------------------------------------------------------------

## 8. Open questions and uncertain items

1. **Ghost-hand look.** Today it is a cyan additive glow (`#5fd8ff`). Given "no glowing, gamey UI", should it become a pale
   warm translucent hand (for example `#f3ead8` at 0.35 alpha-blended, not additive)? The same question applies to the
   lesson blocks' cyan pulse and green / red colours in the first run.
2. **Forgiving mode** ("any key plays the next note", after 15 s without a correct note). Acceptable, or too much like a
   game?
3. **The first-run song.** The answering phrase of Ode to Joy (ends on C, 15 notes) instead of the existing `piano-ode`
   (ends on D), with soft piano chords that sound but do not move any key. Or should the harp accompany?
4. **Start panel stall.** Sunny appears at 8 s and the panel answers "Play here" by itself at 15 s + 3.7 s. Is
   auto-choosing acceptable?
5. **MR first run.** The OS spatial-data dialog stops the clock. The 10 s first-sound target is measured from the dialog
   closing and the piano landing. OK?
6. **u-arrange is still writing Menu.**
   - The home row order assumed here (Lessons, Instruments, Move instruments, Recordings, Play in my room, Set up my
     room, Show me around, Exit) comes from its `MenuController` at 03:43 and may change.
   - Its planned "updated Sunny line about moving instruments" does not exist yet. It should become `tip_arrange` /
     `menu_tour_2`, not a change to the old tour.
   - Its `IMenuPanel.StartPending` comment says the tour waits for the panel; this spec instead starts the script under
     the panel, so Sunny can help at 8 s.
7. **Palm-summon hotfix at risk.** Every upgrade clone except u-arrange still has the `SummonGesture.cs` from before the
   2026-11-01 palm hotfix: u-engine, u-piano and its sound copies, u-exp-hands, u-exp-world and u-looper. That version
   blocks the summon on `systemGesture` again. If any of them is copied over the project as is, the palm menu will not
   open on the device. S8 would then always end in `open_auto`, and `tip_menu` would never succeed. The integrator must
   keep the hotfix (`SystemPinch`).
8. **Looper (no code yet).**
   - Control names and positions are assumed (LOOP, UNDO, CLEAR, STOP, tempo, length, click, mute lights).
   - The builder's first-use line should be `tip_loop_start` (one id, one clip).
   - It needs `LoopEvent` and `ILooper.Configure`.
   - Reaching from the left table to the right-hand drums inside a one-bar count-in (2.67 s at 90 BPM) needs "record
     from the first note" or a two-bar count-in.
9. **MR text cards.** Keep them as written detail beside Sunny's spoken tip (proposed), or let Sunny replace them?
10. **Passive features** (12, listed in section 1): no tip by design. Does the user want a one-line mention instead, for
    example fingers resting on keys or real hands in MR?
11. **Returning players** from the old tour (`TourDone = 1`) do not get the new tour automatically, only the tips. OK?
12. **Source of truth for lines.** Route B (a project-owned `sunny-lines.mjs`) or route A (the original
    line table). This app is the competition entry, so route B.
13. **Fit numbers.** Key height eye − 0.48 and key-front 0.30–0.42 m are UNVERIFIED and need a seated device check with two
    or three body sizes.
14. **Desktop.** New steps play the VR clips with VR captions on the desktop (no new `*_desk` recordings). OK for an
    editor-only mode?
15. **Exit.** Should Sunny also say a short goodbye when the app closes? A "See you soon!" exists inside `tip_exit`.
    Proposed only for the first Exit, as specified.
16. **Voice memory.** Lazy robotised renders (≤ 24 resident) instead of the current eager 22. Audio-module owners to
    confirm, including the `SlotCount` growth from 240.

## Addendum (2026-11-01, user request): GUIDE button on the tape deck

The user asked for a GUIDE button on the recorder deck: poking it makes Sunny walk the player through the recorder hands-on, ending with a song played on all instruments. It is being built in the `u-looper` work copy with the looper, with line ids `deck_guide_*`. The steps wait for the player each time: REC, play, STOP, PLAY; then LOOP and a drum beat; LOOP and harp; then piano; mute a layer light and UNDO; and finally "saved in Recordings". It follows the tour rules: re-demo after 8 s, move on after about 15 s, poke Sunny to skip, poke GUIDE to end. In the single-instrument layout it brings in each instrument it needs. The first-five-minutes implementation must keep it, route it through the same tour engine, and list it in the Sunny menu screen next to "Make a song in layers" (or merge the two if they duplicate).
