# MusicRoom.Lessons

Guided lessons with falling blocks for the piano, the drum kit and the harp; the session recorder and its tape deck
on the side table, with a **looper** (layered loops, up to six, saved to Recordings) and the **GUIDE** button (Sunny's
one-minute quick show of the deck, in the Guide module; the full walkthrough is "Make a song in layers"); the saved-takes library. The looper: `LOOPER-NOTES.md`. Module `LessonsModule`, **Order 40**. Asmdef refs: Core, Hands, Audio,
Unity.TextMeshPro (no feature module). Specs: `08-ux-lessons-menu-guide.md` sections 2, 4, 8, 9;
`lessons.json`; `Docs/07-world-visuals.md` `tapeDeck` / `rigDecor.sideTable`; ARCHITECTURE.md sections 10, 14, 15.7.
Built on the review-round-1 foundation (ARCHITECTURE.md section 21); `CONTRACT-REQUESTS.md` has two optional Core
requests, a Room merge note and the Audio boot race.

## The first five minutes (u-firstfive, merged onto the platform in b-firstfive, 2026-11-01; merge record: Guide/FIRSTFIVE-NOTES.md)

- **36 classic lessons** (`Resources/lessons.json`, `Docs/11-first-five/tutorials-audit.md`;
  the instruments' packs follow them in carousel order): 15 piano, 12 drums, 9 harp, easiest first (Ode to Joy, First beat, Ode to Joy on the harp), every melody checked against
  a public-domain score; Scarborough Fair removed (its known tune is not safely public domain); no lesson needs three
  hands. 1148 events, 1263 targets.
- **B1** on the classic drums path (`Tutorial.FirstFive.cs` `LandOn` / `LandPoint`; the generic path lands where each
  instrument's `ILessonTargets.TryGetLanding` says): a drum target lands at **0.55 of the radius toward the
  player** (0.45 on the hi-hat), **on the surface** (cymbals 9 mm above their strike plane, the hi-hat 10 mm); the kick
  pad's at its centre. So no target is buried in a cymbal, none points at a bell, and the toms, crash and ride targets
  sit 8-12 cm nearer. The disc (0.36 r) and its glow (0.9 r) stay on the head; both follow a moved kit every frame.
- **B2** on the classic path (the platform's generic path has its own): playing a target of the current event again (a
  bounce on the hi-hat, a chord key re-triggered), or the previous event's target within 120 ms, is no mistake and does
  not flash.
- **B6**: after a lesson the panel offers **"Next: <title>"** (the next lesson of that instrument, in green) above
  Replay and Courses; finished lessons are remembered (`musicroom.lessonsDone`, fewest mistakes in
  `musicroom.lessonsBest`; `LessonProgress`, `ILessonProgress`) and their course row reads "Done  ·  <note>".
- **Purple cue blocks** on every path, the platform's one palette (`Core/Render/LessonCue.cs`, headset feedback: the
  white cue replaced with purple): violet #9e4dff on the way, pulsing toward #bf80ff while waiting, honey #e9c98a hit,
  muted coral #c48676 wrong; alpha .75 boxes and capsules, .5 drum discs, .65 / .55 generic. The classic three's glow on
  the waiting target (`HoverGlowSpec.Tint`) and the harp's target string are the same purple, and so is the hand drums'
  print. (Before: cream #f3ead8 / warm white #fff8ec; before that cyan #58dcff / #d4f6ff, green, red.)
- **Options** are the platform tutorial's (`LessonOptions`): NoCountdown, Accompany (a lesson's chords on their beats
  through the instrument's `ILessonAccompany`, else `IAudioEngine.Piano`: wait mode freezes time, so a chord follows the
  player; no key moves, nothing is recorded, never a hit; the last chord rings its written length; first five: a chord on
  the LAST note's beat plays too), QuietMistakes, Forgiving (switchable live through `ILessonLive.Forgiving`). `ILessonLive.TryGetLiveTarget`: where Sunny's
  ghost finger presses (a key's front 2 cm, in front of its block; a drum target's landing point; a string's play
  point). `TutorialHitEvent` for every play that lands on the lesson (right or wrong, quiet or not).
- **The first run's song** (`Resources/lessons-firstrun.json`, `LessonsFirstFive`, Order 41): `piano-ode-first`, the
  answering phrase of Ode to Joy (E E F G G F E D C C D E D. C C, 108 BPM, C4-G4, ends home on C) with C / G / C / G / C
  chords at velocity 0.28; a hidden lesson (the tutorial finds it, the Lessons menu never lists it, the menu's panel
  never switches to it).
- **The looper**: layer 1 starts on the first note, snapped to beat 1 (`Looper.FirstSnapBeats` 0.5: the click nearest
  the first note is beat 1 and the note lands on it exactly; was 1/8 beat).
- Tests: `EditMode/Lessons/FirstFiveLessonTests.cs` (12: B1 on every piece and the crash, B2 both ways, the options,
  the first run's song and its chords, the live target, the progress round trip, the next lesson), `TutorialTests`
  (warm colours, B1 discs and glows), `LooperTests` (the snap), `LessonsTests` (36 lessons).

## Public contract (all Core types)

| Registered as | Implementation | Notes |
|---|---|---|
| `IRecorder` | `Recorder` | The plain recorder (spec 08 section 4.2) when no loop runs. `Add` publishes `RecorderEvent{Event, DuringPlayback}` always and records only while recording (max 40000 events / 600 s since the platform). Clock: `App.AudioTime` (dspTime, or `BootOptions.AudioClock` in tests). Publishes `RecorderStateEvent`. With the looper: State is Recording (clicks waiting for the first note, layer 1's pass) or Playing (looping, overdubs included); live events go to the looper and are **not** DuringPlayback; the looper's own sends are; REC while looping arms a layer; PLAY plays the deck's last recording (a take, or the last loop session, looped); `PlayEvents` of a saved loop loops it (External). Record on top (2026-11-17): the deck's REC while a take plays calls `RecordOnTop` (the take plays on, new notes are stamped on its clock minus `InputLatency`; State stays Playing until the take's end, then Recording); STOP saves a new take, the two merged in time order, `max(original, STOP)` long (`RecordingOnTop`, `CanRecordOnTop`; Sunny's `tip_record_on_top` from `RecordOnTopTip` after a take's first end). |
| `ILooper` | `Looper` (+ `AppLoopHost`) | The looper (below). `Status`, `Layers`, `Bpm`, `Bars`, `Metronome`, `Configure(bpm, bars, metronome)`, `TryGetControl` (demo anchors), `Press(control)` (Sunny's walkthrough doing a step). Publishes `LoopEvent{Phase, Layers, Layer, On}` (CountIn, Recording, LayerDone, Muted, Undo, ClearArmed, Cleared, Stopped, Saved). |
| `ILessonLibrary` | `LessonLibrary` | `Resources/lessons.json` (29 lessons: 13 piano, 9 drums, 7 harp; 826 events; 966 targets) and every instrument's `Resources/LessonPacks/<id>.json` (platform section below). |
| `ITakeLibrary` | `TakeLibrary` | `App.DataPath/takes/index.json` + `<id>.json` (a temporary folder under `TestBoot`), atomic `.tmp` writes. Serialising a take and every file write run on a worker (`Worker`, default `Task.Run`). Never throws. `Available == false` = "Saving off". Publishes `LibraryChangedEvent` (via the module). |
| `ITutorial` | `Tutorial` | State machine, 64-block pool, hints, summary. Publishes `TutorialEvent{State, LessonId, Version}` on every version change, and only once per `Start`, so there is no transient Idle. |
| `IInstrument` | `TapeDeck` | Kind `Table`, TickOrder 30. Parented under the `RigId.Table` rig's `Group` at (0.07, 0.6, **0.04**) (4 cm back for the loop strip) and added to that rig's `Instruments`. Its four handle renderers are appended once, in place, to the rig's `Handles` list (`LessonsModule.AddHandles`). Played parts: pegs and lamps. Desktop through `IDeckDesktop` (`AppDeckDesktop`). 7 draws. |
| `IMaterialTemplateProvider` | `LessonsMaterials` | Template `Lessons/Block` (shader `MusicRoom/LessonBlock`, instancing on, queue 3005), written by `render-setup` to `Resources/MaterialTemplates/Lessons/Block.mat`. |
| `ITextureBakeProvider` | `LessonsTextures` | `Lessons/deck_palette2` (48 × 6 sRGB, no mips; renamed when the LOOP cap's colour was added, so an old bake is never read): the deck's colour atlas, baked by `bake` to `Generated/Resources/Generated/Lessons/deck_palette2.png`. |

Read-backs for the menu and Sunny (on the `Tutorial` class, beyond `ITutorial`):
- `Mistakes` and `Digit` (the countdown's max(1, ceil)).
- `Lesson`, `Running`, `HasSummary`.
- `EventsDone` / `EventsTotal` are **targets** played / targets in the lesson. They are the panel's "N of M", and `Progress` is their ratio.
- `Summary` is live while a lesson runs and final once `Done`.

Phases:
- `Tutorial` runs the state machine, spawning, fades, the red flash, glows (`HoverGlowPool.Add`) and `IHarpApi.Highlight`.
- `Recorder` dispatches playback: the take's events (frame-quantised) and the looper's Tick (look-ahead sends
  with `IAudioEngine.ScheduleAt`, sample-accurate).
- `EndFrame` composes the final block poses after grab and layout moved the instruments, then submits one `Graphics.RenderMeshInstanced` draw.
- The deck ticks in `Instruments`, through Core.

Hits come from `RecorderEvent` (`DuringPlayback == false`), so fingertips, sticks, the nod, the mouse and the computer
keyboard all count, and deck playback never does. `LayoutChangedEvent`: a lesson whose instrument left the scene stops.
Layout does this too, and `Stop` is a no-op when already idle.

## The looper (Looper.cs, LoopSession.cs, LoopHost.cs; details and design notes in LOOPER-NOTES.md)

- **LOOP** (the brass peg left of REC): soft clicks start at the tempo chosen with SLOW / MED / FAST (70 / 90 / 110 BPM,
  default 90) for the length chosen with 2 BARS / 4 BARS (default 2); the four count lights run with them. The player's
  **first note** starts layer 1: the loop starts on the click nearest that note (within 1/8 beat the note lands exactly on
  beat 1; further off it keeps its place, up to half a beat early or late). Layer 1 records exactly one pass, then the
  loop repeats without a gap. No note within 20 s: the session ends. The first LOOP press ever, Sunny says
  `tip_loop_start` first and the clicks wait for the line (IGuide.Tip, once, persisted).
- **Overdub:** LOOP (or REC) while it plays arms the next layer (its light blinks red): it records exactly the next pass,
  from the bar (count-down: the four lights show the last four beats, with soft clicks), while every earlier layer plays.
  LOOP again before it starts disarms. Up to six layers (a seventh blinks all six lights). A pass with nothing played
  leaves no layer.
- **Layer lights** (row 2, 1-6): green playing, dark green muted, red recording, blinking red armed; poke to mute /
  unmute. **UNDO** removes the last layer (an armed or recording one first). **CLEAR** stops and erases (first tap: its
  cap blinks and its label reads TAP AGAIN for 3 s). **STOP** ends looping and saves. **CLICK**: the metronome while
  looping (off by default; layer 1's pass and the count-downs always click). **TIDY**: the timing tidy (on by default).
  The tempo, length, click and tidy are remembered (`musicroom.loop.tempo|bars|click|tidy`).
- **Timing:** the audio clock in whole samples: pass k starts at anchor + k x PassSamples exactly; events are sent 80 ms
  ahead through the instruments' playback calls inside `IAudioEngine.ScheduleAt` (ENGINE-API.md section 10), so each
  sound starts on its sample whatever the frame time; no drift over any number of passes. Live notes are placed at
  (audio clock - `InputLatency`, 30 ms) in the pass; the tidy moves onsets halfway to the nearest 1/16, never out of their
  bar; a note's release moves with it. A layer's window opens 30 ms before its bar. Notes held over the end of a pass get
  their release at the window's end; the hi-hat pedal is restored at a layer's start. STOP, mute, undo let every
  playback key up. Onsets more than 100 ms late (a hitch) are skipped, releases always sent; a pause skips ahead.
- **Saved:** STOP (with something new) saves the session to Recordings as "Loop N · 90 BPM · 3 layers" (N shared with the
  takes), all layers with their mute state (`LoopFormat`: a take whose events begin with a "loop" header and "layer"
  marks). Later changes update the same record. Recordings > Play loops it (the deck's own session comes back after).
- **Interplay:** starting a lesson stops the loop (and saves it); LOOP stops a running lesson. (The re-send on a sound
  path change was deleted on 2026-11-11: the classic sound is the only one, so no path ever changes.)

### Merged into the upgraded project (2026-11-01)

- (Since 2026-11-10 the piano, drums and harp play only the classic sound; `LooperPlayTests.TheClassicThreeKeepEvery
  LoopedNoteOnItsSample` checks every pass's notes on their samples.) At the merge the loop played through the shipped
  sound units (`Tests/EditMode/Audio/Burst/ScheduleScopeSoundUnitTests`);
  `HeldKeysAreReleasedAtTheEndOfThePassAndOnStop` runs on today's piano (it releases before a re-strike)
  and `...OnTheNewPiano` checks the modelled piano (its pass-end release is a key-up and it strikes again in place, so
  there is no Release call); `MovingTheInstrumentsStopsTheLoopAndSavesIt`.
- "Move instruments" (Layout) ends a loop session as STOP does (saved when it has something new), like a lesson; the
  deck cannot be pressed while the instruments are moved.
- `tip_loop_start` is line 23 of 38 (after u-arrange's `tip_arrange`); the deck walkthrough holds while arranging.

### Play that always plays (2026-11-16, PLAYFIX)
- The deck under a resting fingertip: a deck that moved since the last tick (>= `JumpMetres` 1 cm / `JumpDegrees` 4°), or
  during a press (PLAY's playback brought its instruments and, in mixed reality, the side table moved at once), presses
  nothing: every fingertip inside a control counts as holding it until it leaves (`TapeDeck.Reseat`, `Jumps`). The
  quick show's PLAY used to press STOP under the same fingertip in that frame ("it ended after 1 s").
- `Recorder.LastPlay` (IRecorder, default ""): what the last Play / PlayEvents played or why nothing did; a recording
  none of whose events an instrument here plays is refused instead of playing silence; a refused PlayEvents no longer
  stops the current playback. Logs: "[Lessons] deck PLAY: ...", "[Lessons] deck STOP pressed: the playback stops after
  N s", "[Lessons] playback ended after N s". The deck's PLAY of its saved loop notes it in `PlayingTake` (the gallery
  hangs its painting). Recordings > Play: "[Menu] Play '<name>': ..." on every press; pressed while loading it plays
  once loaded; an unreadable file or a refusal shows on the Play row ("Can't open this one" / "Can't play this one").
  Tests: `Tests/EditMode/Lessons/DeckJumpTests`, `Tests/PlayMode/MR/MrDeckPlayTests`.

## The many-instrument platform (phase 0, 12-new-instruments/platform.md 6-7)

Every instrument records, loops and has lessons through the registry; nothing here names one outside the tutorial's
three classic code paths (and `EventRoles.Classic`, the classic seven types from Core's `TakeEventType`, used only when
no registry is given: EditMode harnesses).

- **Event roles** (`EventRoles.cs`): built once from `IInstrumentRegistry.All[*].Events`: each type's role (Onset,
  Release, Control, State, Marker; unknown = Onset), key, Held / Sampled, owner, and its pairings (a Held onset's
  Release; a lane's Control and Release, by the longest shared prefix).
- **Recorder:** 40000 events / 600 s. `Add(ev, at)` stamps an earlier audio time (`t = at - start`, at least 0, kept in
  time order; observers get it at once). Markers are recorded, never played back. A take (deck or library) plays on
  `PlaybackLanes.Take` (7); a Control is dispatched `ControlEncoder.DispatchLead(speed)` early (a second cursor). Each
  displayed instrument is woken (`IAudioPresence.Wake`, throttled 0.25 s per instrument) 0.3 s before its first event:
  the take is scanned ahead from `Play`. Sampled State types keep their last live value for the looper.
  A playback the player starts (PLAY of a take or of the deck's loop, `PlayEvents` of a take or a loop) publishes
  `RecordingPlaybackEvent{Started, Kinds, Loop}` with the instruments it plays (`Recorder.KindsOf`: one bit per
  instrument, markers left out; a loop: every layer's, muted ones too), and its end (the take's end, STOP, REC, CLEAR,
  another playback replacing it). A loop session being recorded (LOOP) is not one. Layout brings the instruments in front
  of the player for it (`Layout/LayoutModule.Stage.cs`). `PlaybackKinds` while it runs.
- **PlaybackWiring:** the seven classic lines, then `WireInstruments`: every registered `IInstrumentPlayback` gets
  each non-Marker type of its descriptor that has no target yet (one closure per type, at wiring time; the module wires
  again when registrations change, never per event); the release-all calls every playback's `ReleaseAll` and a take's
  stop calls `CloseLane(7)`. A missing instrument drops its events.
- **Looper:** Onsets start layer 1 and are tidied; Controls are not (a lane's controls and Off move with the shift its
  onset got, so its shape is kept, and a Control is stored at its dispatch offset); Markers are dropped. At a window's
  end every open Held onset gets its Release (the piano exactly as before: ascending keys at the last sample), every open
  lane a final level-0 Control (`CloseSpeed` 1/30 s glide) and its Off; struck onsets ring on. Sampled State types are
  sampled when a window opens (the hi-hat from the kit as before, others from their last live event). Layer k plays on
  `PlaybackLanes.Layer(k)` = 1 + k; mute / undo close the layer's playback keys and lanes on its lane; displayed
  instruments are woken 0.3 s ahead (`ILoopHost.Wake`). `Capture(e, at)` for stamped events.
- **Lesson packs** (`LessonLibrary`): `lessons.json` (unchanged, 29 lessons) plus every `Resources/LessonPacks/*.json`
  (by file name), in carousel order then file order; `Count(kind)` for any kind; `counts` recomputed; an unknown
  instrument or a duplicate id is dropped and logged (`Errors`).
- **The generic tutorial path** (`Tutorial`, every instrument but the classic three): shape, travel, lead and count
  noun from `LessonSpec` (`ILessonTargets.Travel` per target), landing / approach / size from `TryGetLanding` every
  frame, hits from `Matches` on live events of the hit types (the instrument's Markers when it has any, else its
  Onsets), the cue `ShowTarget(t, k)` on the event's own target with k = 1 exactly at the landing and 0 after the hit (and
  on Stop), the hint from `Hint(targets)` (else the written one), purple cue blocks (#9e4dff, waiting toward #bf80ff, hit
  #e9c98a, wrong #c48676; no hover glow), ring and bar meshes in the same instanced draw. B2: a re-hit is no mistake.
  B4: `Tutorial.CountNoun`. B6: at 8 s waiting `InstrumentSignalEvent{kind, "lesson.redemo"}` (the Guide re-demos), at
  15 s any hit counts. `CurrentEvent` is -1 when no lesson runs.
- **Every lesson:** `Start` calls `ILayoutService.Bring(kind, Lesson)` first (a layout without Bring: the single layout
  turns to it, as the menu did); the countdown runs at once when the instrument is at the seat, else waits for
  `InstrumentMoveEvent{kind, Arrived}` (`AwaitingArrival`; a lesson waiting for its instrument is not stopped by a layout
  change). Options (`Start(id, options)` and a pack lesson's `"options"` names): NoCountdown, Accompany (the
  `"accompany"` chords on their beats, a waited beat's chord with its hit, through the instrument's `ILessonAccompany`,
  else `IAudioEngine.Piano` / `Release` at the written vel, 0.35 by default; never recorded, never scored), QuietMistakes
  (no count, no flash), Forgiving (any hit counts); Hidden is the menu's (`Tutorial.Options`).
- **Takes:** serialised and written on the worker; if the worker's JsonUtility throws (UNVERIFIED on IL2CPP, device item
  52) the main thread serialises and the worker writes.
- **Tests:** `LooperRegistryTests` (9), `RecorderPlatformTests` (6), `EventRolesTests` (2), `LessonPackTests` (5),
  `GenericTutorialTests` (12, EditMode with fakes), `Tests/EditMode/Core/ControlStreamTests` (3, the reference
  vectors), `Tests/PlayMode/Lessons/GenericTutorialPlayTests` (5, the handpan stub with a scripted fingertip, Sunny's
  re-demo, the pack targets through the registered `ILessonTargets`, a stub's take played back).

## Ghost hands play it back (Ghost/, 2026-11-11)

While a recording or a loop plays back, see-through glowing hands (a cool pale blue: "you, earlier"; never the player's
lit hands nor Sunny's warm cream demo hand) play it on every instrument that is shown (ILayoutService.PlaceOf Seat or
Gliding; in mixed reality the hidden ones get none). Only a playback: the recorder's read-ahead is empty while recording
or playing live. A loop's layers looping under an overdub are a playback too (the ghost plays the earlier layers).
- `Recorder.Upcoming(from, to, events, when)` / `Looper.Upcoming`: the events the running playback will send in an audio
  time window, with their audio times and lanes (a take from its start; the looper as `Events` will send them). The ghost
  reads `GhostHands.Horizon` (1.2 s) ahead, so every stroke is planned before it sounds.
- `GhostPlan.cs` (pure): `GhostTrack` (one hand's strokes; strike hands arc from stroke to stroke, lift higher for louder
  or slower strokes, rest a hand's width above the last note between phrases and come down `Approach` before the next;
  a rub circles the rim; piano fingers press from On to Off, lift, travel in a small arc, hover when idle).
- `PianoFingering.cs` (pure): chords within 35 ms, one hand or split low/high, every ordered finger choice scored
  (stretch, palm travel, a finger reused at once, a thumb on a black key, a soft left-below / right-above middle C
  preference), held keys keep their fingers, the hands never cross, more than ten notes: the rest left to the keys.
- `GhostHandModel.cs` (pure): the jointed hand (Sunny's demo hand's rounded palm and capsule fingers, three joints a
  finger), forward kinematics and a finger IK; a held stick (the kit's 40 cm stick) or mallet.
- `GhostHands.cs`: reads ahead, plans per instrument (piano keys from IPianoApi; drum pieces from IDrumsApi, the kick left
  to its beater; harp strings from IHarpApi; handpan fields, hand drum heads, marimba bars and bowl rims from their
  IGuideAnchors, read once per playback), poses the hands each EndFrame in the instruments' own frames (they follow a
  gliding or moved instrument), fades (in 0.45 s, out 0.8 s; to `OverlapAlpha` 0.15 where the player's fingertip is
  within 7 cm, out within 0.32-0.45 m of the head), asks Sunny for `tip_ghost_hands` once they have played 4 s (after the room's listen-back line).
  Never input: no collider, no pointer.
- `GhostHandsMesh.cs`: every hand in one dynamic mesh (8 slots, ~830 vertices a hand, one array +
  SetVertexBufferData), one renderer; `Shaders/GhostHands.shader` (`MusicRoom/GhostHands`, template
  `Lessons/GhostHands`, written by render-setup from `GhostHandsMaterials`): depth pre-pass + translucent rim pass,
  per-vertex opacity, single-pass instanced. Allocation-free per frame.
- Module: `GhostHandsModule` (Lessons' order, ticks EndFrame). Tests: EditMode `Lessons/GhostHandsPlanTests`, PlayMode
  `Lessons/GhostHandsPlayTests`, `GhostHandsShotsTests` (GPU), `GhostHandsPerfTests` (Performance).

## Behaviour (tutorial / tape deck)

**Tutorial states.** `Idle → Countdown (3 s, digit 3-2-1) → Playing ⇄ Waiting → Done`. `Start` stops the running lesson
first; an unknown id just stops.
- Playing: `t` and `elapsed` advance; at `t >= due(cur)` the clock freezes at `due` and the state becomes Waiting.
- While Playing or Waiting, an event spawns its blocks once `due - t <= lead`. A full pool resumes next frame.
- Only `cur` can be hit, and only in reach: Waiting, or `due - t <= EARLY`.
  - A matching unplayed target is a **hit**: its blocks fade green; a complete event returns Waiting to Playing and advances.
  - Anything else in reach is a **mistake** (`WrongT = 0.3`).
  - Out of reach, a play does nothing.
- Only the lesson instrument's `pianoOn` / `drum` / `harp` events count; releases, chokes, hat toggles and other instruments are ignored.
- Targets the instrument lacks are skipped and dropped from the total.
- Done: summary {notes = targets, mistakes, seconds = elapsed}.

**Timing constants.** All timing runs in doubles.
- SPEED 0.18 m/s.
- TRAVEL: piano 0.35, drums 0.35, harp 0.30 m.
- lead = TRAVEL / SPEED: 1.9444 s (harp 1.6667 s).
- due_k = lead + beat_k · 60/bpm.
- EARLY 0.15 s, COUNTDOWN 3 s.
- 64 blocks, HIT_FADE 0.3 s, WRONG_FLASH 0.3 s.
- Pulse = 0.5 + 0.5 sin(8 t).

**Blocks.** One instanced draw (`Graphics.RenderMeshInstanced` with the custom instanced `MusicRoom/LessonBlock` shader;
the per-instance colour array goes in through `RenderParams.matProps`, which ARCHITECTURE.md section 14 allows for
instanced custom shaders: it is no URP Lit/Unlit renderer). Unlit, alpha-blended (normal blending, separate alpha factors), no depth
write, queue 3005 (after glass, snow and frost). Per-instance linear colour: #e9c98a hit; else #c48676 while `WrongT > 0`;
else lerp(#9e4dff, #bf80ff, pulse) when live; else #9e4dff (`LessonCue`). Alpha 0.75, or 0.5 for drum discs. Scale: hit `1 - hitT/0.3`,
live `1 + 0.08·pulse`, else 1. Height above the target: `dist = max(0, due - t)·0.18`.

| | Mesh (pivot) | Local frame | Position | Scale |
|---|---|---|---|---|
| piano | unit box, bottom face | `IPianoApi.Root` (back = +z) | `(key.X, top + dist + 0.0005, 0.05 white / 0.075 black)`, top = BTOP for black keys | `(0.6·W or BW, h, 0.03)·s`; h = clamp(len·spb·0.18·0.19, 0.012, 0.06) |
| drums | 32-sided unit cylinder, bottom | the piece's `Transform` (kick = the **pad**) | `(0, dist + 0.001, 0)` | `(r, 0.01, r)·s`; r = min(0.475·pieceR, 0.06) |
| harp | capsule r 0.006, straight 0.028 | `IHarpApi.Root` | `P + n̂·min(dist, max(0, ‖E−P‖ − 0.25))`; P = `PlayPoint(i)`, E = eyes; `FromToRotation(up, b − a)` | `s` |

**Glows** (live blocks only, strength 0.6 + 0.4·pulse):
- Piano: on the key, axis along the key; 0.07 × 0.024 (white key) or 0.016 (black key).
- Drums: a square of side min(0.24, 1.6·r), 2 mm over the head.
- Harp: at the play point along the string, normal harp −z, 0.14 × 0.014, plus `Highlight(i, strength)`.

Blocks, glows and highlights hide with their instrument (root inactive or `IMixedReality.IsPlacing`); the lesson keeps running.

**Tape deck** (Unity local z = −right-handed z: the controls toward the player (−z), reels at +0.035).
- Geometry (the deck's sizes, plus the loop strip):
  - nickel body 0.26 × 0.07 × 0.275 (z −0.175 .. 0.1: the 0.2 m deck plus a 7.5 cm loop strip in front) and ebony top;
  - walnut cheeks at x ±0.135 and a chrome head bar at (0, 0.076, 0.085);
  - two reels at (±0.065, 0.078, 0.035): rubber tape r 0.05, pearl hub r 0.016, three chrome spokes;
  - transport pegs (r 0.014/0.015, h 0.014, y 0.081, z −0.055): LOOP x −0.09 (#b8782e), REC −0.03, PLAY 0.03, STOP 0.09
    (#b31b1b / #2b8a3e / #2a2a2e); labels 6 mm em at z −0.082;
  - lamps (z −0.025): the main lamp r 0.007 behind STOP; four count lights r 4.2 mm behind LOOP;
  - loop strip, small caps r 7.5 mm, 6 mm high, 2 mm travel, 12.5 mm press cylinder: row 2 (z −0.108) UNDO, the six layer
    lights, CLEAR; row 3 (z −0.148) SLOW MED FAST, 2 BARS 4 BARS, CLICK TIDY GUIDE; labels 4 mm em 17 mm in front.
  - every word is baked into ONE label mesh (`DeckLabels`, the technique of Room's switch labels); CLEAR's "TAP AGAIN"
    shows by vertex alpha.
- **Draws: 7** (`TapeDeck.DrawBudget`; the deck has 17 parts). ARCHITECTURE.md sections 14 / 17 give "table + tape
  deck + switches ≤ 15", and Room's side table takes 8. Parts that share a renderer are merged (`DeckMesh.cs`):
  body (nickel); wood (ebony top, walnut cheeks, pearl hubs: one mesh, one material on the `Lessons/deck_palette` atlas);
  tapes (rubber); chrome (head bar and both spoke sets); lamps (the main lamp, the count lights and every lit cap: one
  mesh, one LitEmissive material whose emission map is a 32 × 1 state texture, one texel per lamp, rewritten only when a
  lamp changes); pegs (LOOP REC PLAY STOP and UNDO in one mesh, palette colours); labels (one baked mesh). Moving
  parts (a sinking peg or cap, turning spokes) are re-posed by rewriting their vertex range in preallocated arrays and
  uploading once per moved mesh per frame (nothing on an idle deck, 0 B). Tapes and hubs are rotationally symmetric, so
  only the spokes turn. No `MaterialPropertyBlock` anywhere: the parts' colours come from the palette's UVs, the lamps'
  light from the state texture.
- Palette: one 6 × 6 cell per colour (each cell is exactly one ASTC 6×6 block of the bake, so every colour survives
  compression exactly); UVs sit at the cell centres, so bilinear filtering never mixes neighbours.
- Pressing: a tip active this frame and the last, inside r < 18 mm (caps 12.5 mm) and 0.06 < y < 0.095 (caps 0.088),
  presses once per entry per pointer. REC → `Record` (arms a layer while looping; `RecordOnTop` while a take plays), PLAY → `Play`, STOP → `Stop`, LOOP →
  `Recorder.Loop`, the strip → the looper, GUIDE → `IGuide.ToggleDeckGuide`.
- Pegs sink 5 mm (`depth += (target − depth)·min(1, 18·dt)`) while held, caps 2 mm. LOOP is latched while a loop session
  runs, REC while recording (a take, layer 1's pass, an overdub), PLAY during any playback.
- Reels turn 3 rad/s recording and 4 rad/s playing, as Unity yaw −angle.
- Lamps (glass #332222; light from the state texture): main #ff2a2a recording, #2aff5a playing, blinking red while LOOP
  waits for the first note or a layer is armed; count lights amber; layer lights as above; the chosen tempo / length,
  CLICK, TIDY and GUIDE (while the walkthrough runs) a dim warm white; CLEAR blinks orange while waiting for its second tap.
- Hover: a 45 mm glow over a peg for a tip 1 cm under to 6 cm over its top (y 0.088), within 25 mm (caps: 26 mm within
  13.5 mm); strength 1 − max(0, h)/0.06.
- `PlaySurfaceAt` returns `Table` within 3.5 cm of a peg (2.5 cm of a cap) and 6–17 cm over the base, so a pinch there is
  a press, never a grab.
- `PlayedParts` = the pegs' and the lamps' renderers. Grab handles: body, wood, tapes, chrome.
- Desktop (ARCHITECTURE.md section 0, `DesktopDown`): each control has a click box (pegs
  32 × 16 × 32 mm, caps 19 × 12 × 19 mm)
  box on `Layers.PlaySurface` (a sibling of the peg, so it stays while the peg sinks). On the frame the left button goes
  down while `!App.IsPresenting`, the deck raycasts `Layers.PlaySurfaceMask` along the head camera's mouse ray; when the
  nearest hit is one of its pegs it presses it. Shift / Alt + click is Layout's rig move / turn and never presses (it
  returns before any `DesktopDown`). Pointers with `Pointer.FromMouse` never press (they still light the hover glow), so
  one click is one press. The body keeps a plain collider on the default layer for the mouse hand to rest on; a drag
  there looks around (only the pegs are click targets).

**Playback wiring.** `PlaybackWiring.Wire`, APIs resolved per dispatch:

| Event | Call |
|---|---|
| `pianoOn` / `pianoOff` | `PlaybackOn` / `PlaybackOff` |
| `drum` | `PlaybackHit(kind, vel, {pos, zone, open})`; no zone = (0.3, head) |
| `drumChoke` | `PlaybackChoke` |
| `hat` | `SetHatOpen(open, null, true)` |
| `harp` | `PlaybackPluck(i, vel, u, speed, mode)`; no mode = the default pluck |
| `harpDamp` | `PlaybackDamp` |
| releaseAll | `PlaybackReleaseAll` |

**Takes.**
- Auto-save names each take `"Take N · m:ss"` (U+00B7). `N` is reserved at once (`ReserveName`) and persisted in
  `index.json`, never reused after a delete, and mirrored to `AppSettings.KeyNextTakeNumber` in the settings store (no
  synchronous `Save()` per take: index.json is the authority and the store flushes on quit / pause).
- The frame STOP is pressed only names the take: serialising it (up to 40000 events) and writing it run on the worker;
  the small index JSON is built on the main thread and its writes are chained on the worker, so two saves never race on
  `index.json.tmp`. Continuations (index, `Changed`) return to the main thread.
- `FormatLength` = JS Math.round, so "1:00", never "0:60". `FormatDate` = "d MMM, HH:mm" in local time with invariant month names.
- Ids are `take-<base36 ms>-<base36 rand>`.

## Tests

EditMode (`Tests/EditMode/Lessons`, 80):
- `TutorialTests` (23). The tutorial reference checks plus spec 08 §9 timing:
  - countdown 3-2-1 and the first block;
  - the block 0.35 m up, falling at 0.18 m/s, 60 % key width, height encodes the length;
  - waiting at due₀ = 1.9444 on the key top, with the glow and a frozen clock;
  - other instruments are not mistakes; a wrong note flashes red for 0.3 s; a hit turns green, shrinks and is freed; the summary;
  - sharps named "C#4", black-key geometry;
  - a hit at due − 0.14; nothing at due − 0.16; early and too-early plays; a wrong note just before landing;
  - playback ignored; every due vs the JSON;
  - drum discs on the hat and kick pad (never the shell), radii and opacity, glows, chord, mistake, the shell counting by kind;
  - harp capsule on the sight line, along its string, landing on the play point, with glow and highlight, through to done;
  - pool cap 64 with resumed spawning; skipped missing targets; a hidden instrument draws nothing;
  - TutorialEvent sequence with no transient Idle; live summary; all 826 JSON hints equal `LessonHints.For`.
- `LooperTests` (25, the looper on a scripted 48 kHz clock with a fake host): LOOP's clicks on the beat to the sample, no
  accent before the bar is known, the count lights; Sunny's line holding the clicks; the first note starting layer 1 on the
  nearest click (within 1/8 beat exactly on beat 1, a chord partner moving with it; further off, keeping its place; a
  pickup before the click wrapping to the pass's end, the window opening at the note, one pass long); releases, the hat
  and notes during Sunny's line never start it; the 20 s timeout; layer 1 exactly one pass and looping without a gap
  (pass k at anchor + k x pass + offset); 50 passes with jittery 6-51 ms frames, every event exact and always sent ahead;
  70 and 110 BPM passes whole samples, restarts one pass apart; an overdub on the next bar, one pass, count-down lights
  4-3-2-1 and four clicks, notes before the bar not in it, playing from the pass after; arming in the last beat waits a
  pass, LOOP again disarms; a held key released at the window's last sample, every playback on matched by its off,
  STOP letting keys up; orphan releases dropped; mute (held keys let go at once) / unmute / UNDO / CLEAR with its second
  tap and the LoopEvent sequence; undoing the only layer ends the session; six layers at most (the lights blink); the
  tidy's strength and bar safety (every offset stays in its bar, at most half a half-sixteenth), a release moving with
  its note; the take format through JSON (offsets, mutes, number, id, names); a pause skipping ahead on the same grid; late onsets skipped, releases sent; settings only
  between sessions; the metronome; the recorder's front (live vs playback, REC arms, PLAY loops the deck's session, a
  plain take after it) and a saved loop from Recordings looping, the deck's own coming back.
- `TapeDeckTests` (14):
  - placement and parts (2 played renderers, 4 handle renderers; the loop strip on the table top);
  - 7 renderers, one material each, no `MaterialPropertyBlock`, every word in one baked label mesh, the lamps' one
    material with the state texture as its emission map;
  - palette cells are flat 6 × 6 blocks (ASTC-aligned), UVs read inside their cell, each peg's vertices sample its colour
    (LOOP its brass cell);
  - one press per entry; two active frames needed; the 18 mm × 60–95 mm cylinder;
  - peg travel and latching, with the REC peg's vertices 5 mm down in the shared mesh and STOP's not;
  - reel speeds, yaw sign, each spoke vertex turned about its reel's axis, the head bar static; lamp colours;
  - hover glow; the play surface;
  - desktop: click boxes on `Layers.PlaySurface`, a click on REC records, a reel click does nothing, a click polled in
    `Tick` presses STOP, mouse-hand pokes never press while a hand pointer does; Shift / Alt + click never presses;
    clicks count only while not presenting; a hidden deck takes no clicks.
- `PlaybackWiringTests` (2): every event type reaches its API with the default fields; missing instruments are dropped.
- `LessonMeshTests` (2): block and deck meshes have the designed sizes and outward winding.
- `TakeLibraryTests` (5): formats, names and ids; number reservation; put / list / get / remove round trip with numbers
  that survive a delete and a reload; a 20000-event take is serialised and written in the held worker work, nothing in
  the calling frame; an unwritable folder means Saving off.
- `RecorderTests`, `RecorderLimitTests`, `LessonLibraryTests` (5): order and playback, the deck take restored after a
  saved take, the 20000 / 600 s limits, `DuringPlayback`, PLAY / REC during playback, the state events, the data counts.

PlayMode (`Tests/PlayMode/Lessons`, every boot through `TestBoot`).
- `LooperPlayTests` (8, manual frames, the scripted audio clock, `AudioTestTap`, fingertip pokes on the deck): LOOP's
  clicks to the sample and three count lights on beat 3; the first live kick anchoring layer 1; the kit's own playback
  stamped by `ScheduleAt` to the exact sample of passes 1-3; REC arming an overdub on the next bar with the count-down
  lights, the piano (computer-keyboard path) recorded in it and played from the pass after; the layer light muting and
  unmuting, UNDO, CLEAR with TAP AGAIN; a STOPped session saved as "Loop 1 · 90 BPM · 1 layer" (3 events, one pass) and
  looped again from the library (External); a lesson stopping (and saving) the loop, LOOP stopping a lesson; a held key
  released inside its pass and all keys up on STOP; the classic three's looped notes on their samples; Sunny's first-use
  line once (persisted) with the clicks held for it; 700 looping frames (sends through the real piano, drums and harp)
  allocating 0 B.
- `LooperShotTests` (1, Graphics): PNGs to `Builds/Shots/looper/` (or `$MUSICROOM_SHOTS_ROOT/looper`, `$LOOPER_SHOTS`): the deck idle, counting in,
  looping with layers (one muted, one armed), TAP AGAIN, and the GUIDE walkthrough with Sunny beside the deck; pixel
  checks of the count, layer and tempo lights.
- `LooperLoadTests` (1, Performance, `test-perf`): six busy layers at 110 BPM on the real clock: the Recorder phase per
  second of play and per sending frame, the audio thread per block, 0 dropped commands. Every test that compares block or glow poses with
instrument transforms first waits for Layout's first arrange, which moves the rigs about 0.5 s after boot:
- `LessonsPlayTests` (8):
  - services, takes under `App.DataPath/takes`, the deck on the Table rig (its renderers appended in place to Room's
    handle list, never rebuilt; tick order; follows the table);
  - a scripted fingertip presses REC, then STOP: Take 1 is saved in the boot's temporary folder, LibraryChangedEvent is
    published, `KeyNextTakeNumber` is 2 in the injected store with no extra `Save()`, and `TestTap` shows no
    instrument-source commands;
  - desktop: with the editor's MouseHandSource, `Pointer.FromMouse` pointers held in the PLAY cylinder never press, and a
    mouse ray from the head camera presses REC through its play-surface collider;
  - presenting (`simulateHeadset`): `AppDeckDesktop.Presenting`, no click, scripted fingertips press;
  - the tutorial ticks between Instruments and StickHover, with its glow in the pool before the HoverGlow phase; hits and
    mistakes arrive from `IRecorder.Add`; playback is ignored;
  - drum discs and the harp capsule sit on the registered instruments' geometry;
  - a lesson stops when Layout takes its instrument away;
  - Tutorial, Recorder and EndFrame phases, deck Tick (recording: reels and pegs re-posed and uploaded) / Hover /
    PlaySurfaceAt, OnPlay, Compose and Draw allocate 0 B over 60 frames.
- `LessonsShotTests` (2, `[Category("Graphics")]`, `test-play --gpu`):
  - `TheTapeDeckAloneDrawsWithinItsBudget`: every other renderer off, the profiler's `Draw Calls Count` with the deck
    shown minus the camera alone: 7 (≤ `TapeDeck.DrawBudget`).
  - `LessonBlocksAndTheTapeDeckFromTheSeat`: PNGs to `Builds/Shots/lessons/` (or `$MUSICROOM_SHOTS_ROOT/lessons`, `$MUSICROOM_SHOTS`). It checks pixels: the
    waiting block shows on C4, the falling D4 block is cyan, the C4 block flashes red after a wrong note, and the lamp
    is red while recording with REC latched fully down. Shots: piano waiting (seat and close), wrong flash, falling;
    drums falling, and waiting (seat and close); harp falling, and waiting (seat and close); the deck from the seat, and
    recording close up. Instruments without geometry (foundation stubs) get plain reference markers at their API
    positions; real instrument modules get none.

## Known gaps / notes

- **Looper (device checks).** `Looper.InputLatency` (30 ms: ring + HAL) is a guess to tune on the headset (notes landing
  late or early against the loop); the key / drum / string animation of a loop event happens when it is sent, up to
  80 ms before its sound (the sound is exact); two layers on the same piano key share the piano's one playback presser;
  the metronome click is 2D (the Ui source). See LOOPER-NOTES.md.

- **Audio boot race (foundation).** `SynthSource` can run one audio block half configured at boot and log an
  `ArgumentNullException`, which fails whichever PlayMode test is booting (any test, headless or not; 2 of the first 5
  PlayMode runs on the revised foundation, in the foundation's `FrameOrderTests` and `BootOptionsTests`). Fixed in the
  Audio module's clone; see `CONTRACT-REQUESTS.md` 4.
- **TMP font asset.** Editor runs (render-setup / tests) re-serialise `Assets/TextMesh Pro/.../LiberationSans SDF.asset`
  in Unity 6's format (no glyph change; static atlas). It is a generated file outside this module; the clone keeps the
  foundation's copy, and a merge must not take a re-saved one from a module clone.
- **Metals.** Nickel and chrome are URP Lit metals and need the Room's reflection probe to read silver.
  Without the probe (stub room), they look blue-grey.
- **Walnut cheeks.** A plain walnut colour (#442616, the wood map's mean) from the palette, not `Core/wood_walnut`: the
  textured wood would need its own material, an eighth draw over the deck's budget, for two 1 × 7 cm faces.
- **Blending.** Unity blends in linear space, not gamma space, so translucent blocks over dark backgrounds
  look somewhat brighter than the gamma-space design colours. Over keys and heads they match closely.
- **Glow order.** The hover glow (foundation, queue 3000) draws before the blocks (3005), so a waiting block softens its
  own glow. The designed order is the reverse: `CONTRACT-REQUESTS.md` item 2.
- **Deliberate choices.**
  - A hidden instrument shows no glows or harp highlights.
  - After a 64-block overflow, a target played before its block spawned gets no block (no parked block that never fades).
  - Harp hints use the JSON text (precomputed by `hintFor`, verified in the tests); code generates hints only for events without one.
- **Untested here.** Single-pass instanced stereo for `MusicRoom/LessonBlock` is untested without a headset. The shader
  uses the standard instancing and stereo macros. A real left-button event through the Input System is not driven in
  the tests (the click path is tested from `AppDeckDesktop.ClickAllowed` / `Presenting` and the head-camera ray onward).
- **Takes.** The recorder is not stopped on shutdown, so a take that is recording when the app quits is lost.
