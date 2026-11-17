# The first five minutes and Sunny's full tour (u-firstfive, 2026-11-01)

Built on `platform-base` (the merged project: Move instruments, the looper, the start panel, Exit, the
palm-summon hotfix). Specs: `Docs/11-first-five/sunny-tour.md`, `tutorials-audit.md`.
This file is the merge record for phase B (3-way merge onto `u-platform` with `platform-base` as the base):
every shared-file edit is listed below with its purpose. **Phase B is done** (section 5: `b-firstfive`, a
copy of `instruments-base`): sections 1-2 describe phase A; where phase B changed a thing, section 5 says so and wins. New behaviour lives in new files wherever possible.

The palm-menu hotfix is untouched: `Menu/SummonGesture.cs` (only SystemPinch blocks the summon) and the
`MetaHandVisual.cs` line `float target = !f.isTracked ? 0f : f.isHighConfidence ? 1f : lowConfidenceAlpha;`.

## 1. What the player gets

**The first run** (`Guide/FirstRun/FirstRun.cs`, steps S0-S11 of sunny-tour.md 2.2 with the user's decisions):

| Step | What happens | Success | 8 s re-demo | ~15 s give-up | Poke Sunny |
|---|---|---|---|---|---|
| S0 start panel | Sunny is hidden. At 8 s on screen Sunny comes beside the panel (left, bubble above its top edge): "Hi, I'm Sunny! Poke a button with one finger." and the ghost finger presses **Play here** | either row poked | "Like this." (twice), then it keeps pointing silently | **never answers the panel** (integrator) | Sunny stops pointing and keeps quiet; the panel waits |
| S1 fit | not built (see 6) | | | | |
| S2 first key | the first-run song starts with **no countdown**; a pale warm block falls onto E4 (1.9 s). "Hi, I'm Sunny! Touch the glowing key." (or "Touch the glowing key, like this." if Sunny already spoke at the panel); the ghost finger presses E4's front | E4 played | "Touch the glowing key, like this." A different key first: "Nice sound! Now try the glowing one." once, 1.5 s later; no mistake counted | "Any key will play it now." (forgiving on, the next key counts); 15 s more with no key: "Let's try the drums. Finish the song in Lessons." | skips the song: to S5 |
| S3 the song | "Lovely! Play each key as its block lands." The answering phrase of Ode to Joy (15 notes, C4-G4, 108 BPM), wait mode; soft C / G / C / G / C chords (vel 0.28) sound when the note on their beat is played; wrong notes sound, are not counted, never flash | the song done | "Touch the glowing key, like this." and the demo at the current key for 6 s | 15 s without a right note: "Any key plays the next note now." (forgiving); 15 s more with no key: "Let's try the drums..." | to S5 |
| S4 celebration | the room: the fire flares (light 2.07 -> 3.0, flames 30 % taller), the floor lamp and candles glow warmer and a little brighter, the SNOW switch turns on (flakes start past the window, a gust once they build), a rolled harp chord C3 E3 G3 C4. Then "You just played Ode to Joy!" and "Tip: poke me anytime to skip." | timed (6 s) | | | Sunny's lines stop; the room finishes |
| S5 drum | "Now tap the drum on your right."; the ghost hand taps the snare head (two quick taps); Sunny drifts up to 15 cm toward the kit | any drum hit (not the kick) | "Tap the top of the drum, like this." | "The drums will wait on your right." | to S6 |
| S6 nod | "Nod your head to play the kick drum" (desktop: the kick pad line); Sunny nods itself | a nod, or a kick with every hand off the pad | "Nod your head, like me." | "The kick pad by the snare plays it too" | to S7 |
| S7 harp | "Now sweep your fingers across the harp strings."; the open ghost hand sweeps strings 8-13 at their play points | 3 strings in 0.8 s; after the re-demo any pluck | "Slide your fingers through the strings, like this." | "The harp will wait on your left." | to S8 |
| S8 palm menu | "Hold your open palm toward your face to open the menu"; the palm demo | the panel opens | "Palm toward your face, like this." | "I'll open it for you this time." (Sunny opens it; tip_menu stays unseen) | to S11 |
| S9 what's in it | "Lessons, instruments and your recordings are here." / "Move instruments, or play in your own room." / "Exit is at the bottom. Poke any row to try." while the menu's own hover light walks down Lessons, Instruments, Recordings, Move instruments, Play in my room, Exit | timed; a sub-screen poke: praise, on to S10; a mode row (a lesson, Move instruments, my room, Exit's confirm, the deck walkthrough): Sunny says "Bye!" and the run ends | | | to S10 |
| S10 close | "Show your palm again to hide it" | closed, or a row poked | "Show your palm again, like this." | "I'll close it for you." | to S11 |
| S11 goodbye | "That's the tour! Find me in the menu anytime." then "Bye!" (the closing invitation to other instruments is ready, switched off: 5) | | | | leave now |

Three pokes in a row (no success between) jump to S8 (the one essential gesture); a poke in S8-S10 then goes to S11.
Headset off: the line is cut, timers freeze; on again the step starts over from its line (the song keeps its place).
Sunny never speaks over a loop's count-in or recording pass. Hide Sunny ends the run (it counts as done).
`TourDone = 1` and `musicroom.tourVersion = 2` are saved at the end. "Show me around" (menu > Sunny) replays S2-S11 with
"Hi, I'm Sunny! Let me show you around." (TourDone untouched; steps whose instrument is not in the layout are passed).
The prompt scripted player hears its first note well under 10 s after the start panel opens and is done in ~1:40;
a player who never moves (but answers the panel) is done in under 4 minutes, every step giving up in time.

**Poke to skip** (`FirstRun/SunnyPoke.cs`, 4.1-4.2): body sphere 0.065 m or the bubble's front face; armed at 0.10 m;
fires at >= 0.10 m/s toward Sunny; ignored when that hand played in the last 400 ms, pinches, holds a stick, is over a
play surface, the menu face or the Done / Reset buttons, or while MR places the piano; 0.6 s debounce. Feedback: a 1.5 cm
push that springs back, a blink, a smile and a soft "bop" (`ChirpKind.Poke`). It skips the first run's step, a tip, the
deck walkthrough's step, or a one-line tip. Desktop: a left click on Sunny.

**Contextual tips** (`FirstRun/TourTips.cs` data, `TourTipRunner.cs`, `TourSignals.cs`; sunny-tour.md 3.1-3.2): 64
tips (the spec's 61, plus TIDY, GUIDE and the closing invitation; the low-light hint is a text-only tip), each with its trigger, line, ghost-hand demo at the real target (keys, snare, hi-hat stand, cymbal, harp
strings, the FIRE lever, every deck button, menu rows, the held instrument, the panel edge), success test and the
features it teaches. P1 / P2 / P3 classes, quiet moments, spacing, "Tips while I play", Hide Sunny for the launch, the
first tip adds "poke me anytime to skip", the third adds "You can hide me from the menu", doing it yourself learns a tip
that was never shown. They run only when the app has its start panel (always in the app; tests opt in with
`TourTipRunner.ForceEnabled`), never during the first run, the deck walkthrough or another tip.

**The menu's Sunny screen**: the home row reads "Sunny · Tour and tips" (or "Hide Sunny · Stop the tour" while Sunny is
here); its screen: Show me around · Tips ("12 of 63 heard"; the switched-off invitation is not listed) · Make a song in layers (the deck's GUIDE walkthrough: the
two duplicated, so they are one row) · Tips while I play. The Tips list plays any tip at once at its target.

**Lessons**: the 36 lessons of `lessons-proposed.json`; drum targets at 0.55 r toward the player on the surface (B1); a
re-hit is no mistake (B2); "Next: <title>" after a lesson, finished courses marked "Done" and remembered (B6); pale warm
blocks (ivory, warm white live, sage hit, muted coral wrong) instead of glowing cyan / green / red.

**Ghost hand**: pale warm `#f3ead8`, alpha-blended at 0.375 (was additive cyan `#5fd8ff`).

**Looper**: layer 1 starts on the first note, snapped to beat 1 (`Looper.FirstSnapBeats` 0.125 -> 0.5).

**Done / Reset**: the buttons stop the drawn finger on their face like the palm menu (IContactSurface, Clamp).

## 2. Shared-file edits (each one surgical; no reformatting)

### Core
| File | Edit | Why |
|---|---|---|
| `Core/Contracts/Lessons.cs` | + `[Flags] enum LessonOptions { None, NoCountdown, Accompany, QuietMistakes, Forgiving, Hidden }` after `TutorialState`; + `ITutorial.Start(string, LessonOptions)` default; + `Lesson.options`, `Lesson.accompany`; + `class LessonChord { beat, len, midi[], vel = 0.35 }` | the first run's song options. **Text copied from the platform's version** (u-platform has the same enum, default member and fields; it also adds meter / unit / credit / pickup): take theirs at the merge |
| `Core/Contracts/Menu.cs` | `MenuScreen` + `Guide, Tips` (appended) | the Sunny screen and the Tips list |
| `Core/Contracts/FirstFive.cs` | **new**: `IRoomMoments`, `ITourAnchors`, `ILessonProgress`, `ILessonLive`, `TutorialHitEvent`, `IMenuGuideHooks`, `StartChoiceEvent`, `MenuRowEvent`, `IGuideTour`, `SunnyPokeEvent`, `StepOutcome`, `GuideStepEvent`, `GuideTipEvent` | the seams; names checked against the platform's Core types (no clash) |

### Audio
| File | Edit | Why |
|---|---|---|
| `Audio/Engine/AudioEngine.cs` | `SlotCount` 240 -> 400 (comment) | 114 voice lines from slot 200 (the smallest change; the platform has 512 + `BankSlots`: take theirs) |
| `Audio/Engine/IAudioEngine.cs` | `ChirpKind` + `Poke` | the poke's bop |
| `Audio/Synths/GuideVoice.cs` | `LineIds = Join(<the 38>, GuideVoiceFirstFive.Ids)`, `LineCount` const -> static readonly, a `Join` helper; `Chirp`: a `Poke` branch (one tone 740 -> 520 Hz, peak 0.012) | the new lines' slots; the bop. **The platform made GuideVoice data-driven from the manifests**: at the merge take theirs, drop `GuideVoiceFirstFive.cs`, keep the Poke branch |
| `Audio/Synths/GuideVoiceFirstFive.cs` | **new, generated** (render-voice.mjs) | the 76 ids after the 38 |

Voice memory: GuideVoice still renders every line at boot (eager): 114 lines, 286 s, about 55 MB at 48 kHz float
(was ~17 MB). The platform's lazy render (24 resident) fixes that; until the merge it is fine on a Quest 3 / 3S.

### Guide
| File | Edit | Why |
|---|---|---|
| `GuideLines.cs` | `DemoKind` + `Strike, Hold, PalmRest, Pinch, Point, Lift` (the platform's names) + `Sweep, PinchPull, Twist, Drag`; `Count` const -> `38 + FirstFiveLines.Ids.Length`; `Ids` / `Texts` / `ManifestSeconds` initialisers `new T[] {...}.Concat(FirstFiveLines.*)` + a `Concat` helper; 3 texts and seconds changed: intro_vr / intro_desk "Hi, I'm Sunny! Let me show you around." (3.102), swipe_skip "Or pick an instrument in the menu." (1.966) | the line table; the platform moved `DemoKind` to Core: add Sweep / PinchPull / Twist / Drag there |
| `GuideModule.cs` | `sealed partial`; `InitFirstFive(app)` after the DeckGuide; arrange-on: a tip gives way (`Tips.Stop`), the legacy `tip_arrange` only when `!TipsByRunner`; `Tick`: `if (ClassicTour) PokeSunny(dt); else TickFirstFive(dt);`; `Shutdown`: `ShutdownFirstFive()`; `AutoStart`: `if (!ClassicTour) { AutoStartFirstFive(); return; }`; `Start`: `if (!ClassicTour) { StartFirstFive(); return; }`; `Dismiss`: `if (DismissFirstFive()) ...`; `ToggleDeckGuide`: `YieldToDeckGuide()` before `DeckGuide.Start()`; `MarkTipSeen` -> `SeenAdded(id)`; `TipSeen` -> `SeenCached(id)` (a set read once: 64 tips are asked every frame) | the first run under the start panel (not after it: the merge's known issue), the tips, poke to skip, Show me around = the replay. `ClassicTour` (static, tests only) keeps the classic five-step tour for its own tests |
| `Sunny.cs` | `sealed partial`; `BeginScript`: `Owner = null; _worldDemo = false;`; `UpdateDemo`: `UpdateScriptDemo` hook for the Script state; `NodPose` also in the Script state; `Update`: `pos += PokeStep(dt)` and `PoseWorldDemo` for the world kinds; `EndDemo`: `_worldDemo = false` | world-anchored demos for every script, the poke's push |
| `SunnyView.cs` | `DemoColor` `#f3ead8`, `DemoAlpha` 0.75; `Additive()` sets the alpha blend (SrcAlpha / OneMinusSrcAlpha) | the warm ghost hand |
| `SunnyDemoMesh.cs` | `WideBounds` 1.4 -> 2.6 m | demos at the drums and the harp |
| `Resources/guide-lines.json`, `Resources/Voice/manifest.json` | 76 lines appended, 3 re-measured (generated by render-voice.mjs) | |
| `Resources/Voice/*.wav` | 76 new, 3 re-rendered (+ .meta with fresh GUIDs, the voice import settings) | |
| `SunnyDemo.cs`, `SunnyAdapters.cs`, `DeckGuide.cs`, `Guide.json` | **unchanged** | |

New: `Guide/FirstRun/` (`FirstRun.cs`, `TourTips.cs`, `TourTipRunner.cs`, `TourSignals.cs`, `TourFeatures.cs`,
`SunnyPoke.cs` (+ `SunnyPerch`), `SunnyDemoWorld.cs`, `FirstFiveLines.cs` generated), `Guide/Sunny.FirstFive.cs`,
`Guide/GuideModule.FirstFive.cs`, `Guide/Tools~/sunny-lines.mjs` (the first-five line table) and
`Guide/Tools~/render-voice.mjs` (renders with the voice maker's `render()`, eSpeak NG, offline).

### Lessons
| File | Edit | Why |
|---|---|---|
| `Tutorial.cs` | `sealed partial ... : ITutorial, ILessonLive`; colours `Near` #e8dcc4, `Bright` #fff4e0, `HitColor` #b8d8a0, `WrongColor` #e08c7c; `Block.Land, Lift`; `Start(string)` / `Start(string, LessonOptions)` via `Find` (hidden lessons), `Start(Lesson)` -> `Start(Lesson, LessonOptions)`; `BeginOptions` after the countdown set-up; `Clear`: `EndOptions()`; `OnPlay`: B2 (`rehit`, `RepeatOfPrevious`), `Forgiving` (`FirstUnhit`), quiet mistakes (`PublishHit(false)`, `if (!Quiet)`), `NoteHit` (chord, hit event); `AddBlock` drums: `LandOn` and the smaller off-centre disc / glow; `TryGlow` / `Compose` drums: `LandPoint`; `Finish`: `FinishOptions()`; `Update`: `UpdateOptions(dt)` | B1, B2, the first run's options, pale warm blocks. **The platform rewrote Tutorial.cs** (a generic path, the same LessonOptions, accompaniment through ILessonAccompany, B2 / B6 on its generic path only): at the merge take theirs and re-apply B1, B2 on the classic path, the colours, `ILessonLive` (live target + forgiving switch), `TutorialHitEvent`, hidden lessons; drop my chord path for their `ILessonAccompany` |
| `Looper.cs` | `FirstSnapBeats` 0.125 -> 0.5 (+ doc) | layer 1 snapped to beat 1 |
| `Resources/lessons.json` | replaced by `lessons-proposed.json` (36 lessons) | tutorials-audit.md |

New: `Lessons/Tutorial.FirstFive.cs`, `Lessons/LessonsFirstFive.cs` (Order 41: the first-run song as a hidden lesson,
the chords through `IAudioEngine.Piano`, `LessonProgress`: `musicroom.lessonsDone`, `musicroom.lessonsBest`),
`Lessons/Resources/lessons-firstrun.json`. `LessonsModule.cs`, `LessonLibrary.cs`, `LessonStage.cs`, `LessonHints.cs`,
`PlaybackWiring.cs`, `Recorder.cs`: **unchanged**.

### Menu
| File | Edit | Why |
|---|---|---|
| `MenuController.cs` | + `Progress`, `Tour`, `Spotlight` fields; `Sync`: relayout on tips heard / switched while on the Sunny screens; `SyncTo`: a lesson the library does not list never takes the panel; `LayoutRows`: `Guide` / `Tips` cases; home Guide row: "Sunny · Tour and tips" when a tour service exists; `CourseRows`: "Done  ·  " before a finished course's note; `LessonRows` Done: the Next row (3 rows at `NextTop/NextBottom`) when there is a next lesson; `Press`: `Course` + `Next`, `Tour`, `Tip`, `TipsOn`, `Layers`, Guide row -> the Sunny screen; `_host?.RowPressed(...)` at the end; new `GuideRows`, `TipRows`; `EndPokes`: the spotlight when no finger hovers; `IMenuHost.RowPressed` default | the Next button, completion memory, the Sunny screen, Sunny's spotlight and row events |
| `MenuRow.cs` | `RowKind` + `Next, Tour, Tip, TipsOn, Layers`; `Index` field (reset in `Set`) | |
| `MenuStyle.cs` | strings and geometry (Next, Sunny screen, Tips); `Accent` amber for Guide / Tips; `Parent(Tips) = Guide` | |
| `MenuPainter.cs` | `Guide` / `Tips` draw cases; `IsListRow` includes Tips; the Done screen's first row (Next or Replay) in green | |
| `MenuModule.cs` | `sealed partial`; `ResolveLate` -> `ResolveFirstFive()` (+ its condition); registers `IMenuGuideHooks`; `StartChosen` -> `PublishStartChoice`; `QueryContact`: `BarContact` first | hooks, events, the Done / Reset finger stop |

New: `Menu/MenuRowIds.cs` (row ids and each row kind's feature), `Menu/MenuModule.FirstFive.cs`.

### Room, Editor
New only: `Room/RoomMoments.cs` (Order 21: `IRoomMoments`, `ITourAnchors` for the switch levers),
`Editor/GuideVoiceImport.cs` (an AssetPostprocessor keeping the voice clips' import settings).

### Tests changed
`EditMode/Lessons/LessonsTests.cs` (36 / 15 / 12 / 9 / 1148 / 1263), `TutorialTests.cs` (1148 hints; warm colours;
B1 disc and glow placement), `LooperTests.cs` (the first note snapped to beat 1), `Menu/MenuScreenTests.cs` and
`MenuPokeTests.cs` (36 courses; 15 / 12 / 9; maxScroll 1320 / 930 / 540; Ode to Joy first, piano-mary second,
piano-elise last), `Guide/GuideLinesTests.cs` (114 lines; hi-hat spoken as two words), `SunnyTourTests.cs` (the
reworded intro and swipe_skip), `Audio/AudioRecipeTests.cs` (intro_vr 3.102 s), `PlayMode/Guide/GuideModuleTests.cs`
(the warm alpha-blended demo material; `ClassicTour`), `GuideArrangeTipTests`, `GuideShiftHTests`, `GuideShotsTests`,
`Integration/IntegrationShotsTests` (`ClassicTour`: they test the classic tour; 12 modules: + LessonsFirstFive,
RoomMoments), `PlayMode/Lessons/LessonsPlayTests` (36 lessons; the hi-hat discs at their B1 landing),
`PlayMode/Lessons/LessonsShotTests` (the rendered blocks pale and warm, the wrong flash coral, instead of cyan / red).
New: `EditMode/Lessons/FirstFiveLessonTests.cs`, `EditMode/Guide/SunnyCoverageTests.cs`,
`EditMode/Menu/MenuFirstFiveTests.cs`, `PlayMode/Integration/FirstRunWalkthroughTests.cs`,
`PlayMode/Guide/TourTipsTests.cs`, `PlayMode/Integration/FirstFiveShotsTests.cs` (Graphics).

### Verification (2026-11-01, this clone; ARCHITECTURE.md 20.11)
render-setup, tmp-import, bake, configure, scene, compile: exit 0, 0 warnings in our code. test-edit 846 / 846, test-play
287 / 287, test-play --gpu 317 / 317, test-perf 13 / 13, meta-report 0 Required, build 68,023,983 bytes.
Pictures: `Builds/Shots/firstfive/steps/`.
Note for the shot tests: the lesson blocks are drawn with `Graphics.RenderMeshInstanced`, so a test that steps many
manual app frames inside one Unity frame must yield a real frame before a shot (`FirstFiveShotsTests.Snap`), or every
intermediate block position shows (falling blocks look like columns).

## 3. Phase B merge plan (onto u-platform; as planned in phase A: what was done is section 5)

1. Core: take the platform's `Lessons.cs` (it has `LessonOptions`, `Start(id, options)`, `options`, `accompany`); keep
   my `FirstFive.cs`; `Menu.cs`: append `Guide, Tips` to their `MenuScreen`; `DemoKind` lives in their Core
   `Guide.cs`: append `Sweep, PinchPull, Twist, Drag` (Strike, Hold, PalmRest, Pinch, Point, Lift are theirs already).
2. Audio: take their data-driven `GuideVoice` / `VoiceManifest` / `SlotCount 512`; delete `GuideVoiceFirstFive.cs`;
   re-add the `ChirpKind.Poke` value and branch. Check that their `VoiceManifest.Default` reads every entry of
   `Guide/Resources/Voice/manifest.json` (my 76 are there) or move them to `Resources/VoiceManifests/FirstFive.json`.
3. Guide lines: their `GuideLines` keeps "the Guide's own 38" and merges `Resources/GuideLines/<Module>.json`: either
   keep my `Concat` (the Guide's own become 114) or move the 76 to `Resources/GuideLines/FirstFive.json` (+ manifest)
   and read captions with `GuideLines.TextOf` (it already falls back to `Merged`). `FirstFiveLines.cs` is generated:
   render-voice.mjs can write either form.
4. Tips: they built `TipRunner` / `TipSpec` / `IGuideTipSource` (registry tips of the new instruments). Two runners must
   never show at once: make each treat the other's tip as `busy` (mine reads `Sunny.Owner`; give theirs the same
   check), or port `TourTips` to `TipSpec` through an `IGuideTipSource` whose triggers are `InstrumentSignalEvent`s my
   `TourSignals` publishes. Their `PlatformTips` lines ("bring an instrument from the menu", ...) then sit beside mine.
5. Tutorial: theirs wins; re-apply (in `Tutorial.FirstFive.cs` where possible) B1 landing, B2 on the classic path, the
   pale warm colours, `ILessonLive`, `TutorialHitEvent`, `AddHidden` / `Find`; use their `ILessonAccompany` path for the
   chords (`lessons-firstrun.json` already has `options` and `accompany` in their schema).
6. Menu: merge my rows into their MenuController (they reworked the Instruments screen into tiles / My set).
7. **The closing invitation** (`bye_more` "Want more? Bring another instrument from the menu."): when the new
   instruments and "bring an instrument to your seat" are in, set `FirstRun.InviteMoreInstruments = true` and give
   `tip_more_instruments` a real trigger (e.g. after the tour, or their "menu.instruments" signal); check the caption
   still names their real control (the Instruments screen's tiles).
8. Re-run the whole chain; the walkthrough and coverage tests must pass with their instruments in the layout (the
   first run skips steps whose instrument is not in the scene; coverage needs their features in `TourFeatures` or
   their own tip coverage).

## 4. Not built, and why
- **S1 fit** (`ILayoutService.FitToPlayer`: the piano eased to eye height and reach): Layout is the platform's, the
  numbers are UNVERIFIED; "Move instruments" covers it. A candidate for after the device test.
- **Two ghost hands** (TwoHandPinch, the two-hand twist): the demo mesh has one hand; the summon and twist tips show one
  hand pinching / twisting and their lines say "both hands".
- **The MR spatial-anchor tip** (`tip_mr_anchor`) has no signal (MR does not say when the piano came back from its
  anchor): only from the Tips list.
- **Gaze-dwell targets** use the instruments' roots (+ a lift), not their play zones.

## 5. Phase B: merged onto the platform (b-firstfive, 2026-11-01)

`b-firstfive` started as a copy of `instruments-base` (the platform's frozen result). New files were copied from
u-firstfive; every shared file I had changed went through `git merge-file b-firstfive/<f> platform-base/<f>
u-firstfive/<f>` and each conflict was resolved by hand. The five instrument stub folders and `Sheet/` are untouched.
The palm hotfix (`Menu/SummonGesture.cs`, the `MetaHandVisual.cs` alpha line) is byte-identical to instruments-base.

### 5.1 Conflicts and how they were resolved
| File | Conflict | Resolution |
|---|---|---|
| `Audio/Engine/AudioEngine.cs` | my `SlotCount` 400 vs their 512 + `BankSlots(lines)` | **theirs** (the bank grows with the merged line count; my fixed 400 dropped) |
| `Audio/Synths/GuideVoice.cs` | they rewrote it (data-driven `VoiceManifest`, lazy render, 24 resident) | **theirs**, plus my `ChirpKind.Poke` branch; `GuideVoiceFirstFive.cs` not copied (deleted) |
| `Core/Contracts/Lessons.cs` | the same `LessonOptions` / `Start(id, options)` / `options` / `accompany`, theirs with meter / unit / credit / pickup | **theirs** (identical to instruments-base now) |
| `Core/Contracts/FirstFive.cs` (mine, new) | `MenuRowEvent` now also in their `Core/App/Events.cs` (same fields) | mine removed; theirs is used (published BEFORE the row's action with the screen it was poked on; Confirming the same) |
| `Guide/GuideLines.cs` | `DemoKind` moved to Core; my `Concat` of 76 lines into the 38 | **theirs** + my 3 reworded texts / seconds (intro_vr, intro_desk, swipe_skip); `DemoKind` += `Sweep, PinchPull, Twist, Drag` in `Core/Contracts/Guide.cs`; my 76 lines (now 79) moved to the module line file `Guide/Resources/GuideLines/FirstFive.json` + `VoiceManifests/FirstFive.json` |
| `Guide/Resources/Voice/manifest.json`, `guide-lines.json` | mine had the 114 | back to the 38 (the 3 re-measured) |
| `Guide/Sunny.cs` | 3 hunks next to their tip demo (`_tipDemo`, `ShowTipDemo`, `EvaluateAnchored`) | both: their Tip-state demo, then my Script-state world demo (`UpdateScriptDemo`, `PoseWorldDemo`) |
| `Guide/GuideModule.cs` | auto-merged; their `TipRunner Tips` vs my `TourTipRunner Tips` | mine renamed **`TourTips`**; `InitFirstFive` moved after `Tips = new TipRunner(...)`; `SayTip(spec, hold, force)`; `IGuide.Tip` routes a tour tip to the runner only where the tour's tips run (else the legacy one-line tip) |
| `Lessons/Tutorial.cs` | they rewrote it (the generic path, options, `ILessonAccompany`, B2 / B4 / B6 on the generic path) | **theirs**, re-applied: `partial` + `ILessonLive`, the warm colours (now their generic palette for every path), `Find` (hidden lessons), B1 (`Block.Land/Lift`, `LandOn`, `LandPoint` in AddBlock / glow / compose), B2 on the classic path (`rehit`, `RepeatOfPrevious`, `Remember`), `PublishHit` (TutorialHitEvent) in `Mistake` / `HitTarget`; their accompaniment replaces my chord path (`ChordOn/ChordOff` gone from `LessonsFirstFive`); one fix of theirs: a chord on the LAST note's beat now plays (Accompany before Advance) |
| `Lessons/Tutorial.FirstFive.cs` | duplicated `Options`, `ChordsPlayed`, `ParseOptions`, `FirstUnhit`, the option plumbing | kept only B1 / B2 / hidden lessons / ILessonLive / TutorialHitEvent; the live target also for the generic path (`ILessonTargets.TryGetLanding`) |
| `Menu/MenuController.cs` | 5 hunks: `IMenuHost`, fields, `SyncTo`, the Done screen, `Press` | their `IMenuHost` (RowPressed(in MenuRowEvent), Signal, OptionChanged, LessonShown) and my `RowPressed` call dropped; both field sets; my hidden-lesson guard before their `TryKind`; their `CountNoun` summary + my Next row; `case RowKind.Next` joins their `Course` (the tutorial brings the instrument itself) |
| `Menu/MenuRow.cs` | `RowKind` (their Tile, Option, Options vs my Next, Tour, Tip, TipsOn, Layers); `Set` | union (theirs first, mine appended); `Index = -1` in their `Set`. They added a `MenuRowIds` class here: **my `Menu/MenuRowIds.cs` deleted**, its constants, `IdOf(row)`, `FeatureOf(kind)` (+ Tile, Option, Options) and Guide / Tips screen ids folded into theirs |
| `Menu/MenuStyle.cs` | their grid geometry vs my constants (+ the base's Pretty / KindOf helpers they removed) | both, minus the removed helpers |
| `Menu/MenuModule.cs` | their `IGuideAnchors` registration vs my `IMenuGuideHooks` | both |
| `README.md` (project) | their phase-0 section vs my first-five section | both |
| tests | `AudioRecipeTests` (intro 3.102), `MenuScreenTests` (36 courses, carousel order Piano Harp Drums, "15 lessons,9 lessons,12 lessons"), `IntegrationShotsTests` (18 modules), `LessonsPlayTests` (36 + packs) | their checks with my numbers |
| `Tests/EditMode/Guide/SunnyCoverageTests.cs` | both of us added one (different GUIDs) | **theirs** (GUID kept) + the first run's steps counting as teaching (`TipCatalog.Covered`) + my checks as `FirstFiveCoverageTests` |
| `Assets/Scenes/Main.unity` | generated | theirs, rebuilt by `scene` |

Platform tests changed for the merge (their numbers or rules, with my data): `LessonPackTests` (36 classic, 15 piano),
`Audio/Platform/VoiceManifestMergeTests` (intro_vr 3.102), `SunnyAnchoredDemoTests` (28 presets: the four new kinds have
anchored presets in `SunnyDemo.Sample` too), `RegistryLintTests` (allowlisted with reasons: `FirstRun.cs` 6,
`TourSignals.cs` 7, `TourTipRunner.cs` 3, `Tutorial.FirstFive.cs` 2, `RoomMoments.cs` 1: the tour teaches the classic
three by name; the arrange tips' "nearest / farthest instrument" now reads the registry and the rigs).

### 5.2 One tip system
The platform's `TipRunner` (`Guide/GuideTips.cs`) is the one scheduler of every tip.
- The tour's 65 tips are TipSpecs of `Guide/FirstRun/TourTipSource.cs` (an `IGuideTipSource`; Trigger Signal
  "tour.<id>" or Manual, Success Signal "tour.<id>.done" or None, Anchor "tour.<anchor>"), next to the platform's six and
  every registry instrument's. The menu's Tips list (`IGuideTour`) shows them all: the tour's in their order, then the
  runner's others (the platform's, each instrument's, the sheet music's).
- `TourTipRunner` is the runner's `ITipPresenter` (new, in GuideTips.cs; `TipRunner.Presenter`): it fires the tour tips'
  triggers (`TipRunner.Trigger`) and successes from `TourSignals`; the runner picks the next tip (one at a time, the P1 /
  P2 / P3 waits, one spacing and one P3 window for all) and calls `Show` for a tour tip, which the presenter shows its
  own way (its world-anchored ghost hand, the 15 s success wait, the 8 s re-demo, the praise, "poke me anytime" and "you
  can hide me"). For EVERY tip the presenter adds `Allowed` ("Tips while I play" off keeps P2 / P3 quiet; Hide Sunny
  stops them for the launch) and `Quiet` (no live play for 2.5 s, no grab held, no lesson running, no loop count-in or
  recording pass, no recorder playback, no fresh menu poke, MR not placing; a menu signal's tip is a menu tip, a lesson
  trigger or success a lesson tip).
- The list plays a platform or instrument tip through `TipRunner.PlayNow` (`GuideModule.SayTip(spec, hold, force)`).
- `ClassicTour` boots (tests of the classic tour) keep the runner without the presenter (the platform's behaviour).
- New tour tips: `tip_options` ("Poke Options on a tile to change how it sounds.", the Instruments tile's chip,
  feature `instruments.options`) and `tip_more_instruments` (on: 3 minutes after the tour, nothing ever brought, one on
  display: the ghost hand points at the nearest displayed instrument and pinches; learned by bringing one by the menu,
  pointing or the page turn). `tip_instruments` now names One alone ("Poke One alone to play just one instrument.").

### 5.3 The closing invitation (on)
`FirstRun.InviteMoreInstruments` = true. S11 after "That's the tour!": in a headset with an instrument on display, the
ghost hand points at the nearest one (`TourTipRunner.TryNearestDisplayed`, `ILayoutService.TryGetDisplayPoint`) and
pinches while Sunny says "Want more? Point at an instrument and pinch." (`bye_more`), then "You can also bring them from
Instruments in the menu." (`bye_more_menu`); on a desktop or with nothing on display "Bring more instruments from
Instruments in the menu." (`bye_more_desk`). `harp_skip` no longer says "on your left" (the platform's rule: the side
table may have yielded the left): "The harp will wait for you."

### 5.4 The two things from the screenshots
- **The rack-tom target "straddling" both toms**: the disc in the phase-A picture was a target still falling (blocks
  approach from 0.35 m above along the piece's up; seen from above-behind, an airborne disc projects onto the next drum).
  Checked: `FirstRunWalkthroughTests.EveryDrumTargetLandsOnItsOwnPieceTowardThePlayer` (the real kit: every landing
  inside 0.75 r of its own head, nearer the player than the centre, and no other piece's disc between the eye and it);
  `phase-b/lesson-tom-target*.png` shows a landed tom target.
- **The blocky ghost hand**: a rounded palm (`SunnyMeshes.RoundedBox`: 2.6 cm round in its plane, fully round edges) instead of a box, 16-sided fingers
  (8 before), drawn as one surface (the demo material writes depth: a finger behind the palm adds nothing), and edges
  that fade where the surface turns from the player (`SunnyDemoMesh.Eye` from `Sunny.TryEye`, alpha 0.2 at the rim).

### 5.5 Lessons
`Lessons/Resources/lessons.json` is lessons-proposed.json (36 classic lessons; the platform's packs load after them in
carousel order: 36 + 5 stub lessons). B1 and B2 on the classic path of their tutorial; Next / Done memory unchanged
(`LessonsFirstFive.LessonProgress`, `musicroom.lessonsDone` / `lessonsBest`); the first run's song stays a hidden
lesson of the tutorial (not in the library, so neither the Lessons grid nor the sheet music lists it).

### 5.6 Shared-file edits made in phase B (beyond the merge itself)
| File | Edit | Why |
|---|---|---|
| `Guide/GuideTips.cs` | + `ITipPresenter`; `TipRunner.Presenter`; `Trigger` asks `Allowed`; `Tick` skips a pending tip that is not `Quiet`; `TryShow` lets the presenter show its own tips (`FreeFor`, `Show`) and keeps one spacing / P3 window for all; `StillShowing`; `Learn(e)`; `PlayNow(e)` (the Tips list); the platform's own saying split into `Say(e, force)` | one tip system |
| `Guide/GuideModule.cs` | `InitFirstFive` after the TipRunner; `SayTip(spec, hold, force = false)`; `IGuide.Tip` routes a tour tip to the runner only where the tour's tips run (`ManualByRunner`); the arrange hook calls `TourTips.Stop` | one tip system |
| `Core/Contracts/Guide.cs` | `DemoKind` + `Sweep, PinchPull, Twist, Drag` (appended) | the tour's world demos are data now |
| `Guide/SunnyDemo.cs` | anchored presets for the four new kinds in `Sample` | `SunnyAnchoredDemoTests`: every kind has a preset |
| `Guide/SunnyMeshes.cs` | `RoundedBox` (+ the palm uses it), `CapsuleRadialSegments` 8 -> 16 | the soft hand |
| `Guide/SunnyDemoMesh.cs` | `Eye`, `EdgeAlpha` 0.2, per-vertex edge fade for the hand parts | the soft hand |
| `Guide/SunnyView.cs` | the demo material writes depth; the eye passed to the demo mesh | the soft hand |
| `Lessons/Tutorial.cs` | `Accompany()` before `Advance()` when an event completes | a chord on the last note's beat was never played |
| `Menu/MenuRow.cs` | their `MenuRowIds` + my ids, `IdOf`, `FeatureOf` | one row-id table |
| `Tests/...` (theirs) | see 5.1 | their checks, my data |

### 5.7 Verification (b-firstfive)
See `Docs/ARCHITECTURE.md` 20.13: render-setup, tmp-import, bake, configure, scene, compile exit 0 (0 warnings in
our code); test-edit 989 / 989; test-play 420 / 420; the GPU run, perf, meta-report and the build there.
Pictures: `Builds/Shots/firstfive/phase-b/`.

