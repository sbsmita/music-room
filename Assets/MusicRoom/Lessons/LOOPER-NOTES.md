# The looper and the deck walkthrough

Built on the upgraded engine (the Burst audio engine; since 2026-11-10 the piano, drums and harp play only the classic sound). Everything a player sees is on the tape
deck on the side table: a LOOP peg beside REC / PLAY / STOP, four count lights, and a loop strip of small round push
buttons in front (row 2: UNDO, six layer lights, CLEAR; row 3: SLOW MED FAST, 2 BARS 4 BARS, CLICK TIDY GUIDE).

## 1. What the player does

| Control | What happens |
|---|---|
| SLOW / MED / FAST, 2 BARS / 4 BARS | The next loop's tempo (70 / 90 / 110 BPM, default 90) and length (default 2). Lit = chosen; while a loop runs they show the loop's own. Remembered. |
| LOOP (off) | Soft clicks start (after Sunny's one-line tip, the first time ever); the four count lights run with them. The first note played starts layer 1 on the nearest click; it records exactly one pass, then repeats without a gap. No note within 20 s: nothing happens. |
| LOOP or REC (looping) | Arms the next layer: it records exactly the next pass, from the bar; the last four beats before it light 4-3-2-1 with soft clicks. The earlier layers keep playing. Pressed again before it starts: disarmed. Up to 6 layers. |
| Layer light 1-6 | Green playing, dark green muted, red recording, blinking red armed. Poke: mute / unmute. |
| UNDO | Removes the last layer (an armed or recording one first). The only layer gone: the loop ends. |
| CLEAR | First tap: its cap blinks, its label reads TAP AGAIN for 3 s. Second tap: stops and erases (not saved). |
| STOP | Ends looping; a session with something new is saved to Recordings ("Loop N · 90 BPM · 3 layers"). |
| PLAY | The deck's last recording again: a take, or the last loop session (looped). |
| CLICK | The metronome while looping (off by default; layer 1's pass and every count-down click anyway). |
| TIDY | The timing tidy: onsets move halfway to the nearest 1/16, never out of their bar (on by default). |
| GUIDE | Sunny's walkthrough of the deck (Guide module, `DeckGuide`); again: ends it. |
| Recordings > a loop > Play | Loops it; STOP ends it and the deck's own session comes back. LOOP while it plays adds a layer; STOP saves the change into the same recording. |

## 2. Design

**Files (Lessons):** `Looper.cs` (the state machine, timing, capture, dispatch, tidy), `LoopSession.cs` (session,
layers, `LoopFormat` for the take files), `LoopHost.cs` (`AppLoopHost`: the app side), `Recorder.cs` (the shared front),
`TapeDeck.cs` + `DeckLabels.cs` + `DeckMesh.cs` (the deck), `LessonsModule.cs` (wiring, settings, saving, LoopEvent),
`TakeLibrary.cs` ("Loop N" numbering shared with takes, event counts).

**Timing.** All on the audio clock (`App.AudioTime`) in whole samples. A pass is `round(bars x 4 x 60 / bpm x sr)`
samples; pass k of a run starts at `anchor + k x pass` exactly; an event sits at a fixed sample offset in the pass. Each
frame (Recorder phase) the looper sends the events due in `[sent, now + 80 ms)` through the instruments' ordinary
playback calls (`PlaybackWiring`) inside `IAudioEngine.ScheduleAt(when)`, so today's synths and the sound units start
the sound on its sample. A long hitch drops late onsets (> 100 ms) and always sends releases; a pause (> 0.5 s behind)
lets every key up and skips ahead on the same grid. The clicks run on their own cursor (never doubled when the grid
gets its anchor). Proven by `LooperTests` (50 passes with 6-51 ms frames: every event on its sample, always sent ahead)
and `LooperPlayTests` (the real kit's stamped `When`).

**First note (coordinator decision 1).** A one-bar count-in is too short to reach the drums from the deck, so layer 1
starts at the player's first onset (piano, drum or harp; releases and the hat pedal do not count, nor notes more than
half a beat before the first click): the loop's beat 1 is the click nearest that note. Within 1/8 beat the note (and
onsets in the next 30 ms: a chord) lands exactly on beat 1; further off it keeps its place (up to half a beat early: a
pickup at the end of the pass). The window opens at the note (or 30 ms before beat 1) and is one pass long. 20 s with no
note: the session ends. Overdubs keep the bar-aligned start with count-down lights.

**Capture.** A live event is placed at `audio clock - InputLatency` (30 ms: the output ring + HAL, a device knob)
relative to its layer's pass start, wrapped into the pass. Releases of keys pressed before the window are dropped; a
note-off moves with its note-on's tidy shift (lengths kept); keys still down at the window's end get their release at
its last sample; a layer with hat moves restores the pedal it started with. Events become playable while still being
recorded (the next pass may begin within the look-ahead), and a pass with nothing in it leaves no layer.

**Playback per layer** tracks the keys it pressed, so a muted, undone or stopped layer lets exactly its keys up, and a
layer only releases what it pressed. Muted layers send no onsets.

**Saved format.** A loop session is an ordinary `TakeRecord` (no Core change, the menu's Recordings and Play rows work
as they are): `events[0]` is a header `{type "loop", midi bpm, i bars, u 4, speed pass seconds, mode record id, pos
number, kind "v1"}`, then `{type "layer", i, open = muted}` and that layer's events with `t` = offset seconds. `Recorder.
PlayEvents` recognises the header and loops it.

**Recorder front.** `IRecorder.State` is Recording while LOOP waits for the first note or layer 1 records, Playing
while looping (overdubs included). Live events during a loop are captured and published with `DuringPlayback = false`
(they are live play: lessons and Sunny may count them); the looper's sends are `DuringPlayback = true` and never
recorded. Without a loop the recorder is exactly the plain recorder (spec 08 section 4.2).

**Interplay (decided):** starting a lesson **stops** the loop and saves it (a lesson's own timing and blocks need the
player alone; pausing would leave a half state the player cannot see); LOOP during a lesson stops the lesson. (Until
2026-11-11 a Classic sound switch mid-loop made the looper re-send its look-ahead on the new path; the switch and the
re-send are gone.) Mixed
reality: the deck lives on the table rig as before, so the looper is wherever the deck is.

**The deck (draw budget).** Still 7 draws (Room's table 8 + deck 7 = 15): body, wood, tapes, chrome, lamps (the main
lamp, 4 count lights and 18 lit caps in ONE mesh with one LitEmissive material whose emission map is a 32 x 1 RGBA state
texture, a texel per lamp, re-uploaded only when a lamp changes), pegs (LOOP REC PLAY STOP UNDO, palette), labels (every
word baked into one mesh from TextMeshPro; CLEAR / TAP AGAIN by vertex alpha). The deck is 4 cm further back on the
table (`TableLocalPosition` z 0.04) and 7.5 cm deeper. The palette bake was renamed `Lessons/deck_palette2` (one new
cell); a stale `deck_palette.png` from before is unused. Calm look: lamps are bulbs behind smoked caps (dim warm white,
green, amber, red), no glow effect, no UI panel.

**Sunny.** The first LOOP press ever asks `IGuide.Tip("tip_loop_start")` "Listen to the count, then play. It will
repeat." (coordinator decision 2: the tour spec's id and wording, unchanged); the clicks start after the line. The GUIDE
walkthrough is the Guide module's `DeckGuide` (Guide/README.md): it reads `ILooper.Status`, `IRecorder.State`,
`RecorderEvent` and `LoopEvent`, points with `ILooper.TryGetControl` and does steps with `ILooper.Press`.

## 3. Measured (macOS editor, batchmode)

- `test-perf` `LooperLoadTests` (editor Mono, M-series Mac): six layers x 240 events a pass at 110 BPM (55 events/s)
  with the metronome, sent through the real piano, drums and harp: the Recorder phase costs 843 us per second of play
  (11.7 us per 72 Hz frame), 12.1 us on average in a frame that sends events, 28.6 us at most; the audio thread with
  that loop: 260 us per block (sum of the 18 sources' averages, 4.9 % of a 5.33 ms block), 384 us sum of maxima,
  0 dropped commands, 0 stolen voices, 54 voices active.
- Draws: the deck alone 7 (profiler, `test-play --gpu`), the whole seated scene 20 (`DrawBudgetTests`).
- Allocation: 700 looping frames (`Recorder` phase with sends into the real piano, drums and harp, plus the deck's Tick
  with lamp texture updates) allocate 0 B; `AudioSchedule` 0 B.

## 4. Check on the headset

1. `Looper.InputLatency` (30 ms): overdub a beat by ear on a loop; notes landing consistently late or early mean the
   value is wrong (raise it if late). Same for layer 1 against the clicks.
2. The first-note start: play the first note on a click, half a beat off, and well before the clicks.
3. The loop strip's 2.8 cm button pitch with hand tracking (mis-pokes between neighbouring layer lights); the 12.5 mm
   press cylinders; that resting a hand on the deck front does not press anything.
4. The look: lamp brightness behind the caps in the headset; label legibility (4 mm letters at ~0.6 m).
5. Visual vs sound: loop events animate keys / drums / strings when sent, up to 80 ms before the sound; acceptable?
6. Sunny's perch beside the deck from the real seat (it must not cover a button), the ghost hand pointing at controls.
7. The click on the 2D Ui source next to the spatialized loop.
8. A 70 BPM 4-bar loop (13.7 s) and six layers for CPU (the `[Audio] dsp` log line, `DspLoadLog`).

## 5. Known gaps

- Visuals of loop playback lead the sound by up to the 80 ms look-ahead (the instruments animate when called; delaying
  them needs an instrument change: `AudioSchedule.At` tells an instrument the scheduled time if one wants to).
- Two layers playing the same piano key share the piano's one playback presser (one layer's release lifts both).
- The harp's 70 ms re-pluck guard runs on frame time, so two loop plucks of one string closer than ~70 ms (only from two
  layers) may drop the second.
- The tip and walkthrough lines were rendered with the project's eSpeak pipeline (available here); no device check of
  their sound yet.

## 6. ARCHITECTURE.md section 21 entry (draft)

### 2026-11-01, the looper and the deck walkthrough (u-looper, on engine-base)

- **Audio:** `AudioSchedule` + `IAudioEngine.ScheduleAt(when)` (default method), the ambient schedule scope: inside it an
  instrument's ASAP / relative commands play at `when`, sample-accurately, on both paths (stamped in `AudioEngine`'s
  instrument calls, `SynthKit.Stage` for PianoLo..Ride, `SoundUnit.Stage` for every role but the room,
  `AudioGraphHost.Stage`); ENGINE-API.md section 10. `IAudioEngine.Click(accent, when)` (default no-op; `UiSounds.Click`)
  and `AudioCall.Click`. `GuideVoice.LineIds` 22 -> 37 (`tip_loop_start`, six `deck_guide_*`, and the tour spec's
  `tip_take`, `lay_drums`, `lay_harp`, `lay_piano`, `lay_done`, `tip_loop_mute`, `tip_loop_undo`, `again`); slots 200..236.
- **Core:** `ILooper` (+ `LoopStatus`, `LoopControl` incl. Rec / Play / Guide), `LoopEvent` / `LoopEventPhase` (as
  Docs/11-first-five/sunny-tour.md 7.4 asked, with `Layer` and `On`), `IGuide.Tip(id)`,
  `IGuide.ToggleDeckGuide()`, `IGuide.DeckGuideRunning` (default members). Settings keys owned by modules:
  `musicroom.loop.tempo|bars|click|tidy` (Lessons), `musicroom.tipsSeen` (Guide, the tour spec's key).
- **Lessons:** the looper (section 2 above); the deck with LOOP, the loop strip and GUIDE, still 7 draws; `Recorder` as
  the shared front; "Loop N" recordings; bake `Lessons/deck_palette2`; `TapeDeck.TableLocalPosition` (0.07, 0.6, 0.04).
- **Guide:** `Sunny.Tip` (state `Tip`, leaves silently, `Done` untouched); `Sunny.BeginScript / SayScript / ShowPress /
  EndScript`, state `Script`, `Perch`, `DemoKind.Press` (`SunnyDemo.EvaluatePress`, wide demo bounds); `DeckGuide`; poke
  Sunny to skip (walkthrough only).
- **Tests:** EditMode 624 (Lessons +29, Guide +3, Audio +4), PlayMode 211 / GPU 233 (Lessons +10, Guide +4),
  `test-perf` +1. GPU shots in `Builds/Shots/looper/`. The GPU shot tests of Guide, MR, Drums, Menu and Integration honour
  `MUSICROOM_SHOTS_ROOT` (default unchanged), so a clone can keep its whole run's shots in its own folder.

## 7. Merge note: every file changed outside `Assets/MusicRoom/Lessons`

New files:
- `Assets/MusicRoom/Audio/Engine/AudioSchedule.cs` (+ .meta)
- `Assets/MusicRoom/Guide/DeckGuide.cs` (+ .meta)
- `Assets/MusicRoom/Guide/Resources/Voice/` 15 WAVs (+ .meta, the same import settings as the 22): `tip_loop_start`,
  `deck_guide_intro|rec|stop|auto|saved|keep`, `tip_take`, `lay_drums`, `lay_harp`, `lay_piano`, `lay_done`,
  `tip_loop_mute`, `tip_loop_undo`, `again`
- `Assets/MusicRoom/Tests/EditMode/Audio/Burst/ScheduleScopeTests.cs`, `Tests/EditMode/Guide/SunnyTipAndScriptTests.cs`,
  `Tests/PlayMode/Guide/DeckGuideTests.cs` (+ .meta); the Lessons test files are in the Lessons test folders.

Changed (small, additive unless noted):
- Audio: `Engine/IAudioEngine.cs` (`ScheduleAt`, `Click` default members; `AudioCall.Click` appended),
  `Engine/AudioEngine.cs` (stamp `when` in Piano / Release / Pluck / Damp / Drum, Choke's `When`; `Click`),
  `Engine/SynthKit.cs` (`Stage` stamps instrument sources), `Burst/Units/SoundUnit.cs` (`Stage` stamps),
  `Burst/AudioGraphHost.cs` (`Stage` stamps), `Synths/GuideVoice.cs` (`LineIds` +15, `LineCount` 37; `UiSounds.Click`).
  The sibling sound units add new files only (ENGINE-API golden rule); if one touched these files, the stamping lines
  are 3-8 lines each and merge by hand.
- Core: `App/Events.cs` (`LoopEvent` appended), `Contracts/Lessons.cs` (`LoopEventPhase`, `LoopControl`, `LoopStatus`,
  `ILooper`; `using UnityEngine`), `Contracts/Guide.cs` (`Tip`, `ToggleDeckGuide`, `DeckGuideRunning` default members).
- Guide: `GuideLines.cs` (+15 lines, `TipLoopStart`, `GuideTiming.TipT`, `DemoKind.Press`), `GuideModule.cs` (`Tip`,
  tips seen, the walkthrough, poke-to-skip, Dismiss ends it), `Sunny.cs` (states `Tip`, `Script`; perch; press demo;
  `Leave(say)`), `SunnyDemo.cs` (`EvaluatePress`, `DemoPose.Wide`), `SunnyDemoMesh.cs` (`WideBounds`), `SunnyView.cs`
  (wide bounds), `Resources/guide-lines.json` (lineCount 37, 15 entries, `unityLines` note), `Resources/Voice/manifest.json`
  (+15), `README.md`. **Conflict risk:** the first-five tour work (Docs/11-first-five) adds many of the same
  tour-spec lines (`tip_take`, `lay_*`, `tip_loop_*`, `again`) with the same ids, text and lengths: keep one copy of each
  and one order in `GuideLines.Ids` == `GuideVoice.LineIds` (a test checks they match).
- Tests outside Lessons: `EditMode/Guide/GuideLinesTests.cs` (37 lines; Unity lines may lower-case a button name in
  `spoken`), `EditMode/Audio/AudioRecipeTests.cs` (message text), `PlayMode/Audio/AudioEngineTests.cs` and
  `ClassicSoundTests.cs` (line count from `GuideVoice.LineCount` instead of 22); `PlayMode/Guide/GuideShotsTests.cs`,
  `MR/MrShotsTests.cs`, `Drums/DrumsGraphicsTests.cs`, `Menu/MenuScreenshotTests.cs`,
  `Integration/IntegrationShotsTests.cs` (optional `MUSICROOM_SHOTS_ROOT`; safe to drop if a module rewrote its file).
- Project: `README.md` (controls table: Loop in layers, Learn the deck).
- Spec (outside the project): `Docs/10-upgrade/ENGINE-API.md` section 10 appended (the brief asked for it there).
- Generated (not merged; `bake` makes it): `Generated/Resources/Generated/Lessons/deck_palette2.png`.

Integrator order after the merge: `compile -> render-setup -> bake -> scene -> test-edit -> test-play` (bake is needed:
`deck_palette2` is new).

## 8. Merged into the upgraded project (2026-11-01)

Merged three-way into the project (after the 10-upgrade, base engine-base) together with u-arrange; the
record is ARCHITECTURE.md section 21 ("merge of u-arrange and u-looper") and 20.10. What changed on the way in:
- The 15 lines follow u-arrange's `tip_arrange` (index 22): `tip_loop_start` is index 23, 38 lines in all; voice slots
  200..237. The tip mechanism is one (`Sunny.Tip` / `GuideModule.StartTip` / `TrackTip`); the rule here (seen only when
  heard to the end) is kept. While "Move instruments" runs, the deck walkthrough holds like the tour.
- `ScheduleAt` through the shipped piano, drum and harp units (this clone only had the Example unit): every unit call the
  instruments make stages through `SoundUnit.Post` with its `when`, and `Tests/EditMode/Audio/Burst/
  ScheduleScopeSoundUnitTests` (8) proves sample-accurate starts (lag 0 at any block offset), bit-for-bit equality with
  explicit times and no drift over 14 passes. `LooperPlayTests.TheClassicThreeKeepEveryLoopedNoteOnItsSample`
  checks the classic piano and kit on their samples (the Classic switch tests went with the switch, 2026-11-10).
- `MUSICROOM_SHOTS_ROOT` now works in every shot test (`Tests/PlayMode/Core/ShotDirs.cs`).
- `bake` wrote `Lessons/deck_palette2`; the old `deck_palette.png` was deleted.

## 9. Every instrument (the many-instrument platform, phase 0)

What an event is now comes from the registry (`EventRoles`, from each descriptor's `EventTypeSpec`s; platform.md 6.4):
- **Starting and tidying:** any Onset starts layer 1 and is tidied (unknown types count as Onsets); Controls never are.
  A continuous lane's controls and its Off move with the tidy shift its onset got (the lane keeps its shape, as a piano
  note's release keeps its length), and a Control is stored at its dispatch offset (`DispatchLead(speed)` early), so a
  layer is always in dispatch order and the saved format needs nothing new. A lane joined mid-way (its onset before the
  window) keeps its controls exactly where they were.
- **Markers** (a judgement such as the theremin's held pitch) never enter a layer and never start one.
- **A window's end:** every open Held onset gets its Release at the last sample (the piano: pianoOn is declared Held,
  so this is exactly the old `Open[midi]` loop, ascending keys); every open lane a final level-0 Control (a 1/30 s
  glide ending at the window's end) and the Off (the owner's Release with key Lane); struck onsets (a marimba bar, a
  handpan field, a drum) are not damped: they ring on.
- **Sampled State types** are taken when a layer's window opens (`ILoopHost.TrySample`: the hi-hat from `HatOpen`
  exactly as before, any other from its last live event) and lead the layer when it used them.
- **Lanes:** layer k plays on `PlaybackLanes.Layer(k)` = 1 + k; a take on 7. A layer's playback keeps the keys and lanes
  it sounds (`LoopLayer.Held`): mute, undo and stop let exactly those go, a lane with a final level-0 Control and its Off.
- **Displayed instruments** are woken 0.3 s ahead of their events (`ILoopHost.Wake` -> `Recorder.WakeOwner` ->
  `IAudioPresence.Wake`), so their paused sources resume at least 250 ms before the sound.
- **Sound paths:** (deleted 2026-11-11) `RouteVersion` and the looper's re-send on a sound path change are gone: since
  the Classic sound switch was removed (2026-11-10) no instrument changes its sound path.
- Tests: `Tests/EditMode/Lessons/LooperRegistryTests.cs` on a synthetic registry; every earlier looper test unchanged.
