# MusicRoom.Guide (stage 3): Sunny

Sunny is the sun-faced robot that shows a newcomer the palm menu, the page-turn swipe and the nod kick, speaking each
line in a lightly robotised voice. Spec: `Docs/08-ux-lessons-menu-guide.md` section 3.

## The first five minutes and the full tour (u-firstfive, merged onto the platform in b-firstfive, 2026-11-01)

The merge record, with every shared-file edit, the phase-B conflicts and how they were resolved: **`FIRSTFIVE-NOTES.md`**.
Specs: `Docs/11-first-five/sunny-tour.md`, `tutorials-audit.md`.

- **The first run** (`FirstRun/FirstRun.cs`, the FAST tour since 2026-11-16, notes in
  `music-room-vr-wip/unity-stage-tune/TUNE-FASTTOUR.md`; before it the quick tour of 2026-11-04, whose steps are kept but
  no longer run) SHOWS and never waits for the player, and plays no song, note or lesson: Sunny's voice only. S0 under the
  start panel (Sunny comes at 8 s and points at "Play here", never answering it), then one short line per stop, Sunny
  flying beside what it shows: Intro; the SNOW switch (FIRE and WIND in the same line); the palm menu (the palm demo,
  then Sunny opens it); Move instruments (the row lit and pressed, then a pinch on the nearest instrument); the
  instrument set (a tile, then One alone, on the Instruments screen); REC and PLAY on the deck, then the Recordings row;
  Reset view (lit and pressed) and the both-hands hold; goodbye at the deck's GUIDE. Nothing is changed (no switch flips,
  Move instruments stays off, the layout and the deck untouched); a thing that is not here is described without pointing.
  The tour takes about 35-40 s. Poking Sunny skips the step; Hide Sunny ends it; a row the player
  pokes ends it (the menu stays theirs); otherwise the menu ends as the player had it. Sunny's own menu actions are
  flagged (`TourSignals.MenuBySunny`) so no tip counts them. Saves `TourDone` and `musicroom.tourVersion` 3. "Show me
  around" replays it from the intro.
- **Poke to skip** (`FirstRun/SunnyPoke.cs`) for everything Sunny does (the first run, every tip, the deck walkthrough):
  body sphere 0.065 m or the bubble's face, armed at 0.10 m, at least 0.10 m/s toward Sunny, never from a hand that just
  played, pinches, holds a stick, is over a play surface or the menu; 0.6 s debounce; a push that springs back, a blink,
  a smile and a soft bop (`ChirpKind.Poke`).
- **One tip system.** The platform's `TipRunner` (`GuideTips.cs`) is the ONE scheduler of every tip: the tour's 65
  (`FirstRun/TourTips.cs`, handed over as TipSpecs by `FirstRun/TourTipSource.cs`, an `IGuideTipSource`), the platform's
  six (`PlatformTips.cs`) and every registry instrument's. `FirstRun/TourTipRunner.cs` is the tour's `ITipPresenter`: it
  fires the tour tips' triggers and successes from `TourSignals.cs` into the runner, shows a tour tip when the runner
  picks it (its world-anchored ghost hand, `FirstRun/SunnyDemoWorld.cs`; the 15 s success wait, the 8 s re-demo, the
  praise), and adds its rules to EVERY tip: the quiet moments (no live play for 2.5 s, no grab, no lesson, no count-in or
  recording pass, no fresh menu poke, MR not placing), "Tips while I play" (`musicroom.tipsOn`) and Hide Sunny for the
  launch. One tip at a time, one spacing, one P3 window. The tour's tips need the app's start panel
  (`TourTipRunner.ForceEnabled` in tests). The menu's **Sunny screen** (`IGuideTour`): Show me around, Tips (the list of
  every tip: the tour's, then the platform's and each instrument's; it replays any of them), Make a song in layers (= the
  deck's GUIDE walkthrough), Tips while I play.
- **The ghost hand** is a soft pale hand: pale and warm (#f3ead8, alpha-blended at 0.375), a rounded palm
  (`SunnyMeshes.RoundedBox`: 2.6 cm round in its plane, fully round edges) and 16-sided fingers, drawn as one surface (depth written: a finger behind
  the palm adds nothing), its edges fading where the surface turns from the player (`SunnyDemoMesh.Eye`, alpha 0.2 at
  the rim). The platform's presets gained the tour's kinds (`DemoKind.Sweep`, `PinchPull`, `Twist`, `Drag`).
- **Lines**: the Guide's 38 (3 reworded) plus the module line file `Resources/GuideLines/FirstFive.json` +
  `Resources/VoiceManifests/FirstFive.json` (79 lines; clips in `Resources/Voice/`), merged like every instrument's and
  rendered lazily by GuideVoice. Every line under 12 words and 4 s, captions naming the real controls (the Instruments
  screen's One alone and Options, the deck's labels). The table is `Tools~/sunny-lines.mjs`;
  `node Assets/MusicRoom/Guide/Tools~/render-voice.mjs` renders the changed or missing clips with the eSpeak NG
  voice pipeline (`render()`, offline; `--all` renders every one) and writes the WAVs,
  their .meta, the two FirstFive.json files, the 38's changed entries and `FirstRun/FirstFiveLines.cs`.
  `Editor/GuideVoiceImport.cs` keeps the clips' import settings.
- `GuideModule.ClassicTour` (tests only) keeps the classic tour for the tests of its machinery (and the platform's
  TipRunner without the tour's presenter).
- Tests: `EditMode/Guide/SunnyCoverageTests.cs` (the platform's checks over the whole catalog, the first run's steps
  counting as teaching, + `FirstFiveCoverageTests` 8), `PlayMode/Integration/FirstRunWalkthroughTests.cs` (a first-timer who does nothing sees the fast tour's stops in order within 45 s with no note and every ghost-hand target real (`PlayMode/Guide/FastTourCheck.cs`; One alone too; mixed reality: `PlayMode/MR/FastTourMrTests.cs`), the SNOW stop never flips the switch (on and off), the start panel's help, poke-to-skip at every step, Hide Sunny, Show me around, a row poked ends it, no allocation, no first run without the start panel, every drum target on its own piece), `PlayMode/Guide/TourTipsTests.cs` (8, with one scheduler for every tip and the full Tips list),
  `PlayMode/Integration/FirstFiveShotsTests.cs` (Graphics: every step in pictures, `Builds/Shots/firstfive/phase-b`).


**"Move instruments" as one setting (2026-11-10):** the gold knobs and Lock instruments are gone, and with them
`tip_handle` (+ `_still`, `_drop`), `tip_handle_turn`, `tip_handle_play_first`, `tip_move_by_knob`, `tip_locked`, `tip_lock`
and `tip_arrange_lift` (a drag no longer changes the height), their clips and the triggers `HandleNear`,
`LockedHandlePinch`, `HandleDragLong`, `HandlePinchRefused`, `InPlayMoveEnded`, `ArrangeGrabLift` (anchor `Handle`,
successes `HandleDragged`, `HeldLifted`; features `layout.grab`, `layout.handle`, `layout.lock`, `arrange.lift`). New:
`tip_move_turn_on` "To move one, turn on Move instruments in the menu." (P1, a still pinch on an instrument while the
setting is off, `InstrumentBodyPinchEvent`; learned by turning it on, `TourSuccess.MoveTurnedOn`) and `tip_move_quiet`
"Instruments are quiet while moving. Poke Done to play." (P2, in arrange: a fingertip resting on a play surface for 1 s
with nothing held, `TourTrigger.MovePlayAttempt`; the ghost hand presses Done; feature `arrange.quiet`). Reworded:
`tip_arrange` "Pinch an instrument and drag it where you like.", `tip_turn` "Turn your wrist to spin it all the way
round.", `tip_arrange_done` "Poke Done to play again. Reset starts over." The quick tour never mentioned the knob.

**Stabilising (2026-11):** `tip_locked` "Instruments are locked. Turn off Lock in Instruments." (P1, once per launch: a
grab handle pinched while Lock instruments is on; the knobs show a padlock and nothing moves). `tip_mr_floor` now says
"Pinch and hold empty air..." (placing starts after a 1 s still pinch), and `tip_mr_hands` names the Instruments screen,
where the Real hands switch now lives.

**Head and snow (2026-11-10):** nothing moves with the head; `tip_mr_floor` / `tip_mr_place` are retired with the MR
empty-air placing. `tip_summon` ("Reset view") says the two-hand hold and `tip_reset_view` the menu row. (`tip_mr_window`,
`tip_mr_window_move` and `tip_mr_snow` were retired 2026-11-13 with the snowy window; `tip_mr_permission` and
`tip_mr_setup` no longer speak of windows.)

**Since 2026-11-02 (instruments in front, grab handles, real hands):** three new lines in the table
(`Tools~/sunny-lines.mjs`, rendered with `render-voice.mjs`): `tip_handle` "Pinch and hold the little handle to move an
instrument." + `tip_handle_drop` "Let go to drop it." (tip "Drag and drop": a hand near a grab handle for 0.4 s, never
dragged; the ghost hand pinches at the handle, `TourAnchor.Handle`; learned by a handle drag), and `tip_mr_hands` "In
Instruments, poke Real hands to see your own hands." (tip "Real hands": the menu's Instruments screen in mixed reality,
whose bottom row holds the switch; the ghost hand presses it; learned by poking it). `tip_mr_anchor` now says "Your instruments always open right in front of you." (feature
`layout.front`; the remembered piano anchor is gone). `tip_mr_table` waits for the piano really standing on a table. With
the instruments within a foot of the player the keyboard's left end can reach the deck's corner: the deck walkthrough's
perch and `SunnyPerch.Clear` now rise in 12 cm steps (up to 0.48 m) until they are clear of every playing surface, so a
poke on Sunny is never a note.

**Since the first headset test (2026-11-01, u-arrange):**
- The first-launch tour waits for the menu's start panel (`IMenuPanel.StartPending`) and never starts while the
  instruments are moved (`IArrangeMode.Arranging`).
- "Move instruments": a tour in progress goes quiet (`StopVoice`), out of sight (Sunny's root inactive) and holds its
  step, timers and line (no `Sunny.Update`, no menu / layout / nod events) until Done; then it carries on.
- One new line, `tip_arrange` "Pinch an instrument to pick it up." (`GuideLines.TipArrange`, the 23rd id, also in
  `GuideVoice.LineIds`; Docs/11-first-five/sunny-tour.md 3.2 / 5.3). The first time arrange mode turns on with no
  tour running, Sunny comes beside the head to say it (the one tip mechanism below, `Sunny.Tip`) and leaves without
  "Bye!"; it is recorded in `musicroom.tipsSeen` once heard to the end or as soon as the player makes an arrange grab.
  A tip does not touch `TourDone`, and the first-launch tour still starts after one. While arranging, the deck
  walkthrough holds like the tour, and the low-light hint waits. Tests: `Tests/PlayMode/Guide/GuideArrangeTipTests` (4).
- None of the 22 lines of the tour described moving instruments, so none was changed.
- **Specs:** `08-ux-lessons-menu-guide.md` section 3, `guide-lines.json`,
  `06-audio-dsp.md` section 8, and ARCHITECTURE.md sections 11 and 15.8.
- **Assembly:** `MusicRoom.Guide`, which references Core, Hands, Audio and TextMeshPro (the Ctrl/Cmd guard of Shift+H
  reads Core's `DesktopInput.Ctrl` / `Meta`, added at integration), and no other feature module.
- **Foundation:** built and verified against the revised foundation (ARCHITECTURE.md section 21, review round 1):
  `App.IsPresenting` as the XR gate, `TestBoot` / `MemorySettingsStore` / manual frames / the scripted audio clock in
  the PlayMode tests, `AudioTestTap.Calls`, the new `ILayoutService` / `IDrumsApi` members in the test probe, and
  `<Module>/<name>` bake names.

## Public contract

| What | Where | Notes |
|---|---|---|
| `IGuide` (`Present`, `Done`, `Start()`, `Dismiss()`, `Toggle()`) | Core, implemented by `GuideModule` (Order 50) | `Present` is visible and not idle, gone or leaving: the menu row reads "Hide Sunny" / "Show me around". `Done` means the run finished or was dismissed since the last `Start`. |
| `GuidePresenceEvent { Present }` | published by `GuideModule` | Published on every change of `Present` (start, Hide, the start of the goodbye). |
| `AppSettings.TourDone` | written by `GuideModule` | Set to 1 (and saved) the first time a run finishes or is dismissed. Read once at boot. Lives in the injected settings store (PlayerPrefs in the app, a `MemorySettingsStore` in tests). |
| XR gate | `GuideModule.IsPresenting` = `App.IsPresenting` | Whether an XR session is presenting: picks the `*_vr` / `*_desk` lines and the headset kick rule, and turns Shift+H off. Never `App.IsHeadset` (which hand source is active). True in a headset session or with `BootOptions.SimulateHeadset`. |
| Guide audio source | `IAudioEngine.SetSourceParent(SourceId.Guide, Sunny root)` | Sunny's root stays active while Sunny is hidden. Deactivating it would cut the closing chirp. |

**Events it reads:**
- `MenuEvent`: Open, Screen and Close complete the open, poke and close steps. The panel's `Shown` and `Screen` are also polled.
- `LayoutChangedEvent` and the polled `ILayoutService`: the swipe step, in single mode only.
- `NodEvent`.
- `RecorderEvent`: a drum `kick` that is not from playback.
- `HmdMountEvent { Mounted = false }`: cuts the line in flight.
- `HandsLowLightEvent` (Core's `FastMotionDriver`, once per session when the low-light guard blocks Fast Motion Mode):
  one text hint, "More light helps your hands" (`GuideLines.MoreLightHint`, a caption only: no recording, no voice;
  experience.md 14 item 4). `Sunny.Hint(text, 4 s)` brings Sunny up beside the head with it, then she leaves without
  the goodbye line; it waits while the tour runs (never on top of it), never touches `Done` / `TourDone` (hiding the
  hint is not finishing the tour), and the menu row can hide it. `GuideModule.LightHintShown`; tested by
  `FastMotionDriverTests.ADimRoomBlocksFastMotionAndSunnyShowsOneTextHint`.
- `IDrumsApi.EveryHandPointerFartherThan(0.2, KickPadId)`: the headset kick rule.

**Frame:** `FramePhase.Guide` (step 16, between `Decor` and `Swivel`). Timing reads `IAudioEngine.Now` only (never
`AudioSettings.dspTime`). Shift+H toggles Sunny while `!App.IsPresenting` and neither Ctrl nor Cmd is held;
plain H stays a piano key.

### Classes

| Class | Role |
|---|---|
| `GuideModule` | Wiring, the first-launch rule, presence and persistence, and the event hooks. |
| `Sunny` | The brain: state machine, speech bookkeeping, mouth, blink, placement and the demo clock. It is plain C#, driven by `ISunnyWorld` and `ISunnyVoice`. |
| `SunnyView` | Builds the GameObjects and applies one frame of state. Materials and text change only when a value changes. |
| `SunnyDemo` | The ghost hand's poses as a pure function of loop time. |
| `SunnyDemoMesh` | The demo's solid parts (palm, fingers, thumb, ring, row plate, cone) merged into one dynamic mesh, opacity per part in the vertex alpha, so Sunny stays within 6 draws. |
| `SunnyFace` / `SunnyBubbleArt` / `SunnyCanvas` | The CPU painters for the 512×256 face (8 states) and the 768×340 bubble. |
| `SunnyMeshes` | Procedural meshes, built once per session. |
| `EngineVoice` / `AppWorld` | Adapters over `IAudioEngine` and over the menu, layout, drums and head contracts. |
| `GuideMaterialTemplates` / `GuideTextures` | `IMaterialTemplateProvider` and `ITextureBakeProvider`. |

**Assets:**
- Shaders: `Shaders/SunnyUnlit.shader` (`MusicRoom/SunnyUnlit`) and `Shaders/SunnyCorona.shader` (`MusicRoom/SunnyCorona`).
- Templates, written by `Tools/unity.sh render-setup` into `Resources/MaterialTemplates/Guide/`: `Body`, `Corona`,
  `Bubble`, `BubbleText` (TMP Distance Field Overlay), `Demo` and `DemoDouble`.
- Baked textures, written by `Tools/unity.sh bake` into `Generated/Resources/Generated/Guide/`:
  `Guide/sunnyFace_<calm|happy>_eyes<Open|Shut>_mouth<Shut|Open>` (8) and `Guide/sunnyBubble`. They are painted at
  runtime when no bake exists (and `build` bakes the missing ones).
- `Resources/Voice/*.wav`: 38 lines (the tour's 22, `tip_arrange`, the looper's 15), 22.05 kHz mono, imported per spec 08 section 3.5: Decompress On Load, Preload
  Audio Data, Force To Mono, Vorbis quality 0.7.
- `Resources/Voice/manifest.json` and `Resources/guide-lines.json`, which the tests check for drift. **One list:**
  `GuideLines.Ids` = `GuideVoice.LineIds` = `guide-lines.json` `lines` = the manifest's entries, in the same order (the
  tour's 22, then `tip_arrange` at index 22, then the looper's 15 from `tip_loop_start`); `GuideLinesTests` checks all four.

## Tips (one mechanism for both features, merged 2026-11-01)

u-arrange and u-looper each added a one-line tip; the merge keeps one. `Sunny.Tip(id)` (bool: false for an unknown id or
while Sunny is here) sets state `Tip`, `InTip` (true until Sunny is gone, its leave included), `TipId`, and holds for
`Hold(id, GuideTiming.TipT)` (1.5 s or the line); when the line is over it sets `TipHeard` (false when the line was cut,
e.g. the headset came off) and leaves without a word. `GuideModule` starts tips in one place (`StartTip`: `ActiveTip`,
`TipsShown`) from both triggers: the first arrange mode (`tip_arrange`) and `IGuide.Tip(id)` (the looper's
`tip_loop_start`); `TrackTip` saves a tip in `musicroom.tipsSeen` once heard, or at once on an arrange grab for
`tip_arrange`; a tip that was cut stays unseen and is said next time. `Sunny.Hint` (the low-light caption) is a third,
separate kind (text only); `Dismiss` during a hint, a tip or the walkthrough never sets `Done`.

## Tips from data (the many-instrument platform, phase 0; 12-new-instruments/platform.md 8)

- **Where tips come from:** every registry descriptor's `Tips` (owned by that instrument) and every `IGuideTipSource`
  (parameterless classes found by reflection; the platform's own six are `PlatformTips.cs`). `TipRunner`
  (`GuideTips.cs`) runs them; a duplicate tip id is dropped and logged.
- **Triggers:** FirstArrival (`InstrumentMoveEvent{kind, Arrived}`), FirstDisplayedGaze
  (`ILayoutService.TryGetDisplayPoint` within 10 deg of the head's forward for 3 s; TriggerArg empty: the tip's own
  instrument, or any for a source's tip), IdleNear (at the seat, a fingertip in its rig zone, unplayed for TriggerArg
  seconds, default 8), Signal (`InstrumentSignalEvent.Signal == TriggerArg`), Manual (`IGuide.Tip(tipId)`), LessonStart
  (a `TutorialEvent` countdown of the instrument's lesson; TriggerArg a lesson id or empty).
- **Successes:** EventType (a live `RecorderEvent` of SuccessArg; empty: any live Onset of the instrument), Signal,
  Facing (`FocusEvent`), LessonHit (the instrument's lesson's hits grew), None. A success marks the tip seen at once,
  shown or not (doing it yourself counts); while it is said, its demo ends and the line finishes.
- **Showing:** a triggered tip waits (P1 30 s, P2 6 s, P3 20 s) for Sunny to be free (`SunnyFree`: never over the
  tour, the deck walkthrough, another tip or hint, nor while the instruments are moved), then `Sunny.TipLines` says its
  lines (any line id, the merged ones too) or `Sunny.TipText` shows a TextOnly tip's Title (no voice, at least 4 s); its
  demo loops at its anchor (`IGuideAnchors`, the tip's instrument first, then every other provider; `"display"`: the
  gazed instrument's pick point), then ThenDemo at ThenAnchor; a tour demo kind without an anchor plays in front of
  Sunny. Seen once heard to the end (`musicroom.tipsSeen`, `IGuide.TipSeen`). P1 / P2: 10 s after the previous tip. P3:
  not in the first 90 s after the tour (from boot when it was done before), at most 3 per 10 minutes, 60 s apart, and
  not within 2.5 s of a live play. VrOnly tips need a headset session. A dropped tip stays unseen.
- **Phase 0's arrival tips:** each stub's TextOnly P2 FirstArrival tip goes through this, once.
- **B6, lessons:** the generic tutorial path publishes `InstrumentSignalEvent{kind, "lesson.redemo"}` after 8 s of
  waiting; Sunny comes by with `again` ("Like this.") and the instrument's arrival demo (else a tap) at the waited
  target's landing (`ILessonTargets.TryGetLanding` of the current event's first target). Never marked seen.
- **The demos** (`SunnyDemo.EvaluateAnchored`, `Sample`): every `DemoKind` beyond the tour's six as a keyframed preset
  at a world anchor (Tap, Slap, MalletStrike, Approach, Circle, Strike, Hold, PalmRest, Pinch, Point, OpenTone, PalmBass,
  CuppedSlap, HeelToe, Roll, MalletHold, OpenHand, Hover, Lift, Lower, Rock, Dip, Curl, FingerCircle; Core's `DemoKind`
  comment describes each), plus Custom (`TipSpec.Motion` keys, a `GhostHand` shape per key). The anchor frame: y along
  the anchor's up (its length, under 0.95 m, is the rim radius), z toward the player, x to their right. The pose is
  written in Sunny's frame like the deck walkthrough's press (wide culling bounds). One ghost hand: Roll alternates
  sides; a held mallet or stick is drawn as a closed hand whose implied tool tip strikes (no tool mesh). The tour's
  demos and `SunnyDemoTests` are unchanged.
- **Lines:** `GuideLines.Merged` (`GuideLineTable`) merges every `Resources/GuideLines/<Module>.json`
  (`{"lines":[{"id","text","spoken"}]}`) and `Resources/VoiceManifests/<Module>.json` (the shape of
  `Voice/manifest.json`, read by `ManifestJson`) after the Guide's own 38 (still `GuideVoice.LineIds`, in order); a
  duplicate id is an error and the later one is dropped. `GuideLines.Has / TextOf / SecondsOf` cover both. The Guide's
  own merged file holds the platform's six; the Audio module's GuideVoice merges the manifests for the voice.
- **The platform's six** (voiced; rendered with the eSpeak NG voice pipeline's `render()` from a scratch
  script, like `tip_arrange`; WAVs in `Resources/Voice` with tip_arrange's importer settings; seconds measured from the
  files): `tip_bring_grid` "Poke an instrument in Instruments to bring it here." (P2, Signal `menu.instruments`, success
  `layout.bring.menu`, 3.004 s), `tip_summon_far` "Point at an instrument and pinch to bring it here." (P3, VR,
  FirstDisplayedGaze, success `layout.bring.ray`, Point at `display`, 3.170 s), `tip_my_set` "Open Instruments to choose
  which ones sit with you." (P3, Signal `layout.bring.ray`, 3.164 s), `tip_set_full` "Only three fit. The least played
  one goes back." (P2, Signal `layout.replaced`, 3.229 s), `tip_put_away` "Poke a ticked instrument to put it back."
  (P3, Signal `menu.instruments.set`, success `layout.putaway`, 2.468 s), `tip_page_turn` "Swipe your hand sideways for
  the next instrument." (P3, Signal `layout.single`, success `layout.bring.pageturn`, 3.068 s). Features covered:
  `platform.bring.menu`, `platform.bring.ray`, `platform.bring.pageturn`, `platform.myset`, `platform.putaway`.
- **No instrument is named** in the Guide outside the tour's nod step (the kit's kick event) and the deck walkthrough's
  layered song (`DeckGuide`: drums, harp, piano); `SunnyAdapters` reads the layout's current instrument as it is.
- **Tests:** `VoiceManifestMergeTests` (4), `SunnyCoverageTests` (5), `InstrumentTipsTests` (2),
  `SunnyAnchoredDemoTests` (8), `Tests/PlayMode/Guide/GuideTipsPlayTests` (6, events and signals published by hand).

## Added for the looper (u-looper)

**`IGuide.Tip(id)`** (Core, default 0): a one-line tip outside the tour. `GuideModule.Tip` says a line of the table once
ever (persisted in `musicroom.tipsSeen`, comma-separated, the key of Docs/11-first-five/sunny-tour.md 3.1, once
the line ran its course: a line cut by the headset coming off stays unseen), returns the seconds it takes (the caller
may hold its action: the looper's clicks wait for it), or 0 when already heard or when Sunny is busy (tour, walkthrough,
another tip). `Sunny.Tip`: state `Tip`, `Hold(id, 1.5)`, then leaves **silently** (no "Bye!"); never sets `Done`. Used
by the looper's first LOOP press: `tip_loop_start` "Listen to the count, then play. It will repeat." (the tour spec's
5.3 wording).

**The deck's quick show (`DeckGuide` with `Quick`, the deck's GUIDE button, `IGuide.ToggleDeckGuide`; 2026-11-14).**
About a minute when the player follows promptly (the competition demo): `quick_snow` "Let's make magic! Flip the SNOW
switch." (the ghost hand presses the side table's SNOW lever, `ITourAnchors` "switch.snow"; passed over when it already
snows; no switch in sight: the snow just comes on) -> `lay_drums` (LOOP, then a beat; the loop is set to 2 bars at
90 BPM for the show and the player's own tempo and length come back when no loop runs) -> `lay_piano` (or `lay_harp`
when only the harp of the two is in My set) and `lay_done` -> `deck_guide_stop` (STOP: the loop is saved in Recordings)
-> `quick_play` "Saved! Now press PLAY and watch the magic." (PLAY of the saved loop raises RecordingPlaybackEvent, so
the whole playback show runs) -> `quick_wow` "Wow! Look, your song fills the whole room." 1.2 s into the playback ->
`bye` when the playback ends or after 12 s (the loop plays on). While the clicks wait for the first note, or a layer for
its bar, the ghost hand shows where to play (a Strike on the snare, a Press on middle C, a Sweep of the harp). Waits: 4 s
"Like this.", 7 s Sunny does the step (snow, LOOP, STOP, PLAY; 10 s while the clicks wait for a note). A layout restore
owed while a playback has its instruments in front (Layout's stage) waits until it ended and the stage put the player's
layout back (`LayoutSettle` 1.6 s). Tests: `Tests/PlayMode/Guide/DeckQuickShowTests.cs` (prompt player within 60 s, nobody
playing still ends, the single layout, the tempo restored).

**The tape deck walkthrough (`DeckGuide`, Sunny's menu page "Make a song in layers", `IGuide.StartDeckWalkthrough` /
`DeckGuideRunning`; GUIDE ends it).**
Sunny perches beside the deck (from STOP: 14 cm to the deck's right, 17 cm up, 3 cm toward the player; a test checks
that neither its body nor its bubble covers any control from the seat) and points at each control with the ghost hand
(`DemoKind.Press`: the index finger, the others curled, pressing the control twice per 3 s loop, posed in world space
by `SunnyDemo.EvaluatePress`; the demo mesh gets wide culling bounds). Steps, each waiting for the player's own action:

| Step | Line (id) | Points at | Done when |
|---|---|---|---|
| intro | `deck_guide_intro` "Let me show you the tape deck. Follow my hand." | - | the line ends |
| REC | `deck_guide_rec` "Press REC, then play a few notes." | REC | a take records and 3 notes were played |
| STOP | `deck_guide_stop` "Now press STOP." | STOP | the take stops |
| PLAY | `tip_take` "Saved! Press PLAY to hear it." (skipped when the take is empty) | PLAY | it played (to its end, or 6 s; then stopped) |
| drums | `lay_drums` "First a beat: press LOOP, then tap the drums." | LOOP | a layer is done |
| harp | `lay_harp` "Now press LOOP again and sweep the harp." | LOOP | another layer is done |
| piano | `lay_piano` "Last layer: press LOOP and play the piano.", then `lay_done` | LOOP | another layer is done |
| mute | `tip_loop_mute` "Touch a layer's light to mute it." (skipped without layers) | layer 1's light | a layer muted |
| undo | `tip_loop_undo` "UNDO takes away the last layer." | UNDO | a layer undone |
| saved | `deck_guide_saved` "Your songs are saved in Recordings, in your palm menu." | - | the line ends |
| keep | `deck_guide_keep` "Keep this loop, or press CLEAR twice to erase it." (only with a loop) | CLEAR | cleared, or 10 s (kept) |
| bye | `bye` "Have fun!" | - | Sunny leaves |

Each success gets a praise (`praise_k`). The tour's rules: 8 s without progress, the demo again with `again` "Like
this."; 15 s, Sunny moves on, first doing the step when the next one needs it (REC, STOP, PLAY, or LOOP once, with
`deck_guide_auto` "I'll press it for you."); poking Sunny (a fingertip within 6 cm, once per entry) skips at once;
GUIDE again (or Hide Sunny) ends it. Sunny never speaks during a count-in or a recording pass. In the single-instrument
layout each loop step brings its instrument in (`ILayoutService.Set(Single, kind)`, as a page turn) and the layout is
restored at the end. Ending tidies the deck: a take recording or playing stops (saved), a loop waiting for its first
note is cancelled, an armed layer disarmed; a running loop keeps playing. A walkthrough is not the tour (`Done` and
`TourDone` untouched). Lines: 14 more in `GuideLines` / `GuideVoice.LineIds` (38 in all with `tip_arrange`), rendered with the project's
eSpeak NG pipeline (`render()`, as `Tools~/render-voice.mjs` uses it); the reused tour-spec lines reproduce its
measured lengths exactly; `spoken` differs from `text` only for all-caps button names ("rec", "loop", "stop", "clear",
"undo", "play").

Tests: `Tests/EditMode/Guide/SunnyTipAndScriptTests.cs` (3), `Tests/PlayMode/Guide/DeckGuideTests.cs` (4: every step
on its own action to the end; poke-to-skip, give-up assists and GUIDE ending it tidy; the single layout; with nobody
playing nothing waits 20 s and it ends tidy), `GuideLinesTests` (38 lines; the deck lines may lower-case a button name).

**The deck guide's watchdog (2026-11-16, PLAYFIX; "sunny just pauses and forget to guide on buttons").** Both runs:
what the ghost hand shows is worked out every frame from the step and the deck (`DeckGuide.Want`: a cancelled loop
points at LOOP again; waiting clicks or an armed layer show where to play in both runs; the instrument is brought back
once if it went away, else the hand stays at the deck); notes are progress in a loop step only while they record (a
player strumming the harp while Sunny waits for LOOP kept it silent before); the step's line waits for Sunny to be free
without holding the hand back; "Like this." is owed until it can be said; after the first re-demo the hand restarts every
re-demo time while nothing new happens (`SinceNudge`, `Nudges`); a praise waits at most `PraiseWaitMax` 4 s; every step
ends within `StepMax` 60 s (`StepMaxQuick` 30 s). One log line per step and per give-up. Tests:
`Tests/PlayMode/Guide/DeckGuideWatchdogTests.cs` (7 players x both runs: nobody, notes but no buttons, wrong controls,
cancels every loop, ends every playback, puts the instrument away, slow; Sunny speaks or points again within the
re-demo time + 0.75 s, measured from outside, and every run finishes).

## Behaviour (exact rules)

**The tour:** `intro`, then five steps (each `step`, `wait`, then `praise` or `skip`), then `bye`, `leave` and `gone`.

| Step | Demo | Lines (VR / desktop) | Completes when | Timeout |
|---|---|---|---|---|
| open | palm | `open_vr` / `open_desk` | the panel opens (event or polled) | none |
| poke | poke | `poke_vr` / `poke_desk` | the panel's screen changes from the one at step start | none |
| close | palm | `close_vr` / `close_desk` | the panel closes | none |
| swipe | swipe | `swipe_vr` / `swipe_desk` | in single mode, the current instrument differs from the first one seen | 12 s, then `swipe_skip` |
| nod | nod (Sunny nods) | `nod_vr` / `nod_desk` | `NodEvent`, or a kick not from playback (on a desktop any kick; in a headset only with every hand pointer more than 0.2 m from the pad) | 12 s, then `nod_skip` |

**Timing:**
- `Hold(id, t) = max(t, lineSeconds + 0.45)`, where `lineSeconds = dry seconds / 1.0 + 0.14`.
- Intro 3.2 s, praise 1.7 s, skip 3.2 s, bye 2.6 s, leave 0.8 s.
- Each timed state also waits until Sunny has been quiet for 0.3 s.

**Finishing the sentence:**
- A completed gesture ends the demo and makes Sunny smile at once.
- The praise waits until the line in flight has ended, plus 0.3 s, so its voice starts at least 0.44 s after that line.
- Repeated gestures queue nothing more.
- An explicit `Skip()` cuts the line. A timeout waits for it.

**Leaving:**
- Hide Sunny (`Dismiss`) sets `done`, ends the demo, cuts the voice and says "Bye!".
- Over 0.8 s it shrinks to 5 %, its body, bubble and glow fade, it rises 0.2 m and its light fades. Then it is gone
  (hushed and hidden).

**Speech:**
- `Say` captions the line, plays the open chirp, then voices the line at `Now + 0.14`.
- A line whose robotised buffer is not ready waits for up to 1.2 s (`LATE`). After that it is a caption only.
- A missing clip or free text is a caption only.
- `LineBusy` and `Speaking` follow the line's scheduled end (`endT`).
- A natural end plays the closing chirp. A cut line gets none.

**Mouth:**
- The level comes from `IAudioEngine.VoiceLevel` (RMS over 21.3 ms).
- The peak follower is `peak = max(level, peak·e^(−1.2 dt))`.
- The mouth is shut below 0.01. Otherwise it is open while `level > peak × (open ? 0.2 : 0.35)`.

**Blink:** first after 2–5 s, then every 2.5–6 s, shut for 0.13 s, from a seeded `Mulberry32`.

**Placement:**
- The goal is `head + 0.55 fwd − 0.25 right` at `y = head.y − 0.10`, eased with `1 − e^(−3 dt)`.
- It snaps on the first frame and is frozen while leaving.
- It bobs 0.012 m at 1.4 Hz.
- `LookRotation(pos − head)`: local −z, the face, looks at the player.
- The nod dips 0.04 m and tilts the face down 0.45 rad.

**First launch:**
- `GuideModule` starts the tour once per launch when `TourDone` was false at boot (read from `App.Settings`, the
  injected store).
- It waits for the first arranged frame (`ILayoutService.FirstArrangeDone`, or the desktop rig on Play) with the
  HMD mounted.
- It waits up to 6 s of app time for the intro line's robotised buffer, so the greeting is voiced. It never restarts by
  itself.

**Look:**
- The sphere is r 0.045 (20×14), with u = 0.25 facing the player.
- The corona is a 0.234 m plane, 0.027 m behind the body.
- The bubble is 0.26 × 0.115 m at (0.0832, 0.1163, −0.01), drawn with ZTest Always. The caption is TMP with the
  overlay SDF. It fits from 54 px down to 30 px in steps of 4 until it wraps into 2 lines or fewer.
- The light is a point light, #ffb866, range 1.4, intensity (0.7 + 0.2 sin 2.1t)/π.
- The demo is cyan #5fd8ff at 0.5 and additive. The palm box and capsule fingers sit at (0.07, −0.05, −0.25), the
  ring 0.028–0.036 m, the ghost panel 0.13 × 0.095 with three rows, and the dotted arrow 0.15 → −0.13 with a cone.
- The finger x positions are mirrored so that the index sits beside the thumb (spec 08 section 8).
- Render queues keep Sunny's draw order: corona 3010, body 3011, bubble 3012, caption 3013, demo 3014.
- **Draws (budget 6, ARCHITECTURE.md 14/17):** body, corona, bubble and caption are 4. The demo adds at most 2: the
  merged solid mesh (`SunnyDemoMesh`, one draw for the hand, ring, row plate and cone) plus one line mesh (the ghost
  panel in the poke step, or the dashes in the swipe step; never both). The parts keep their own Transforms as anchors,
  posed exactly like separate meshes; their vertices are written into the merged mesh each demo frame (about 800
  vertices, no allocation). The ring and row plate, double-sided, carry a reversed copy of their triangles.
  `SunnyUnlit` multiplies by the vertex colour (every Sunny mesh has white vertex colours).

All constants live in `GuideLines` / `GuideTiming` (spec 08 sections 3.1-3.4), `SunnyDemo` (the demo) and `SunnyView` (the bubble, the light).

**Deliberate differences:**
- Additive parts (the corona and the ghost hand) add the sRGB-encoded colour × opacity as a linear value, with
  Blend One One. A linear framebuffer cannot reproduce an encoded-space sum for every background. This choice is
  the closest match over the room's walls: in the wall renders the corona's radial profile matches the encoded-space
  design within a few levels. Over black it is brighter than the design.
- The caption uses LiberationSans SDF with a slight face dilate (0.15) to come near a heavy system bold. Line
  breaks can differ by a word.
- The demo hand's fingers are mirrored (spec 08 section 8). The light is converted with ÷π (spec 07 section 6.1).

## Tests

### EditMode (`Tests/EditMode/Guide`, 48 tests)

**`GuideLinesTests`:**
- The 22 lines, steps, praise, timing, robot lead and chirp table against `guide-lines.json`.
- Line order matches `GuideVoice.LineIds`.
- WAVs are mono, 22.05 kHz, and as long as the manifest says.

**`SunnyTourTests`:** a scripted clock with `FakeVoice` and `FakeWorld` (`SunnyTestRig.cs`). The reference tour
cases:
- intro hold and caption
- VR lines
- closing chirp on a natural end only
- mouth hysteresis
- praise at least 0.3 s after the line, with no stacking
- poke, close and menu-event completion
- `swipe_skip` at 12 s
- swipe page turn in single mode
- nod dip and tilt
- desktop, headset and playback kicks, and `NodEvent`
- bye, leave and gone within 0.8 s
- Hide mid-step cuts the voice
- Toggle and Skip
- a missing clip or free text is a caption only
- `LATE` drop and late start
- no-audio tour
- blink timing over 12 seeds
- placement, follow, bob, facing and frozen-while-leaving
- `Hold`
- the whole desktop tour in order

**`SunnyDemoTests`:** palm, poke, swipe and nod poses at the reference sample times, curl directions, the mirrored
index, and the hand's distance.

**`SunnyArtTests`:**
- face gradient, eyes, shine, cheeks and mouths at canvas coordinates
- 8 distinct states and the row flip
- bubble fill, outline, corner and tail
- sphere winding and face uv
- demo mesh sizes
- robot chain on the real `intro_vr` recording: speech band within 3 dB, no clip, audibly changed, mouth envelope has gaps
  (the envelope is computed in the test, so only `RobotVoice.Process` of the Audio module is used)

### PlayMode (`Tests/PlayMode/Guide`, 16 tests plus 1 Graphics test)

Every boot goes through `TestBoot` (ARCHITECTURE.md section 16): scripted hands, a fresh `MemorySettingsStore` (TourDone
set in it for the "done earlier" cases; never PlayerPrefs), a temporary data folder, manual frames (`App.Step(1/72)`) and
a scripted audio clock (`audioClock: () => t`, advanced with every step). Nothing timed waits real time; real frames are
spent only while the audio workers render the robotised lines. Speech is asserted on `AudioTestTap.Calls` (`Chirp` with
its kind, `VoiceLine` with the line id and `When`, `StopVoiceLine`), never on the Audio module's `Commands`.

`GuideModuleTests` boots Audio and Guide with `GuideProbe` (a stand-in menu, layout and drums implementing the current
`IMenuPanel`, `ILayoutService` and `IDrumsApi`). Each item below is one test:
- Order 50, `IGuide`, the Guide source under Sunny's root, and the desktop gate.
- Sunny ticks in `FramePhase.Guide`, once, with `Clock.Dt`.
- First launch (fresh store): the tour starts by itself on the first arranged frame, voiced (`OV`: open chirp, then
  `intro_desk` at `Now + 0.14`); Hide writes `TourDone` and saves; it never starts again in that launch.
- `TourDone` set in the store: 864 steps (12 s) and Sunny never appears.
- The whole desktop tour on the scripted clock: the intro hold; step 0 completed mid-line, the praise voiced at least
  0.3 s after the line in flight ended; poke and close from `MenuEvent`s; the swipe step skipping after 864–865 steps
  (12 s) with `swipe_skip`; the nod step completed by a desktop kick (`RecorderEvent`); bye (`TourDone` saved), leave, gone
  within 58 steps (0.8 s); all 13 lines in order, each `open chirp, line, closing chirp`, with at least 0.3 s of quiet
  between lines; presence events `[true, false]`.
- Hide mid-intro: the line is cut (`StopVoiceLine`), the goodbye is voiced, `TourDone` is saved, Sunny is gone within
  58 steps, and "Show me around" brings it back (presence `[true, false, true]`).
- Steps complete from the `MenuEvent`, `LayoutChangedEvent`, `NodEvent` and `RecorderEvent` contracts (playback and
  other drums never count).
- A simulated headset (`BootOptions.SimulateHeadset`, scripted hands, so `App.IsHeadset` is false): the `*_vr` lines, and
  only a kick with every hand pointer more than 0.2 m from the kick pad completes the nod step.
- The voice through the real engine: `IAudioEngine.Now` is the scripted clock, the processed line is not silent, the
  opening chirp and the line at `Now + 0.14`, `VoiceLevel` opens the mouth, the closing chirp on a natural end, and an
  unmount cuts the line (`StopVoiceLine`, `VoiceLinePlaying` false) with no chirp.
- Scene objects, queues, shaders, the light, and the demo parts following the state.
- The draw budget: 4 draws while speaking and at most 6 in every demo step. The merged mesh writes each part's opacity,
  for example the row plate's glow and a hidden ring, and puts the palm on the hand anchor.
- Every line fits the bubble in at most 2 lines. The long swipe line takes 2 lines at 38 px or more.
- 120 Guide ticks allocate 0 B (with the positive calibration).
- The whole app (every module, no probe) discovers and toggles Sunny; it leaves within 58 steps.

`GuideShiftHTests` drives a test keyboard through the Input System (routed to the player while the tests run):
- Shift+H shows and hides Sunny on the desktop; a held key does not repeat; plain H does nothing; Ctrl+Shift+H and
  Cmd+Shift+H are ignored.
- With a simulated headset (`App.IsPresenting`) Shift+H does nothing.

**Graphics:** `GuideShotsTests` (`Tools/unity.sh test-play --gpu`, whole app through `TestBoot` with manual frames)
writes the following to `Builds/Shots/guide/`, each also as `-wall.png`, in front of a flat wall in the
room's plaster colour:
- `guide-intro-seat.png`
- `guide-intro.png`
- `guide-palm.png`
- `guide-swipe.png`

`AudioBootGuard` tolerates only the known `SynthSource` audio-thread exception while a Guide test boots. The race is
fixed in the integrated Audio module (buffers first, voice pool published last), so the guard is a no-op now; any other
error still fails the test.

## Known gaps and device checks

- **Seat view on a desktop:** the default desktop camera (0, 1.2, −0.1) looks 24° down with fov 55, so the top of the
  first bubble is cut off at the frame's top edge (`guide-intro-seat.png`). This is the designed desktop camera and
  goal. In a headset the player looks straight ahead and sees the whole bubble
  (`guide-intro.png`).
- **Contract requests (`CONTRACT-REQUESTS.md`):** all four applied at integration (2026-10-31):
  `IAudioEngine.VoiceLineReady` (used by `EngineVoice.LineReady`), the intro lines render early (Audio), the
  `SynthSource.Awake` race fix (Audio), `DesktopInput.Ctrl` / `Meta` (used by `GuideKeys`). `AutoStart` still waits
  at most 6 s of app time for the intro line's buffer.
- **Light (UNVERIFIED):** its intensity, and its cost as a fifth per-pixel light near the piano (URP keeps 4 per
  object). Set `GuideModule.LightEnabled = false` if it is too costly on device.
- **Additive look (UNVERIFIED on device):** the corona and ghost hand, over the real room and over passthrough. Alpha
  accumulates opacity² like a SrcAlpha/One blend.
- **Lines:** the ghost panel and arrow are 1 px lines. If they shimmer on the headset, replace them with
  thin quads.
- **Shift+H in the editor's Game View:** tested through the Input System with a test keyboard in batchmode; the
  interactive editor's Game View focus rules are not exercised.
- **Generated outputs:** `render-setup`, `bake` and the tests rewrite outputs outside this folder (the foundation's
  templates, the baked PNGs under `Generated/`, the TMP font asset's dynamic atlas). They are generated, not merged.
- **Robot chain:** it runs at the clip rate (22.05 kHz) inside the Audio module, not at the output
  rate. The reference acceptance numbers pass; bit-level verification belongs to the Audio stage.
