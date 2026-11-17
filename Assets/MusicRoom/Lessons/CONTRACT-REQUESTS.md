# Lessons: contract requests for the merge

## Integration status (2026-10-31): APPLIED

Merged into the project by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `ITutorial.Mistakes` / `Digit` | Added to Core (defaults from `Summary` / `Countdown`); `Tutorial` implements both live |
| 2 | Hover glows after the blocks | `HoverGlowPool` material `renderQueue = 3010` |
| 3 | Room fills the table handles once | Holds (Room's `SideTable` list; the deck appends in place). Room tests updated to expect the deck in the list |
| 4 | `SynthSource` race | Audio module's version merged |

---

The module is built on the revised (review round 1) foundation and uses its
contracts directly: `App.AudioTime`, `App.DataPath`, `App.IsPresenting`, `Pointer.FromMouse`, `Layers.PlaySurface`,
`Rig.Handles` as a list filled once, `TestBoot`. The earlier items 3-5 (shims for the old snapshot) are therefore gone.
Nothing below blocks the module: it compiles and its tests pass without any of them.

## 1. `ITutorial`: live mistakes and the countdown digit (Core, optional)

- **File:** `Assets/MusicRoom/Core/Contracts/Lessons.cs`, `interface ITutorial`.
- **Add:**
  ```csharp
  /// <summary>Wrong notes so far in the running lesson (the panel's "Mistakes: N").</summary>
  int Mistakes { get; }
  /// <summary>The digit the countdown shows: max(1, ceil(seconds left)).</summary>
  int Digit { get; }
  ```
- **Why:** the lesson panel shows "Mistakes: N" during the lesson and the countdown digit
  (spec 08 section 2). The contract only has `Summary` and `Countdown` (seconds).
- **Workaround (in place):** `Tutorial.Summary` is live while a lesson runs (`Notes` = targets, `Mistakes` so far,
  `Seconds` elapsed) and final once `State == Done`; the menu can show `Mathf.Max(1, Mathf.CeilToInt(Countdown))`.
  `MusicRoom.Lessons.Tutorial` already has public `Mistakes` and `Digit`, so adding them to the interface needs no code.

## 2. Hover glows draw after the lesson blocks (Core, optional, visual)

- **File:** `Assets/MusicRoom/Core/Render/HoverGlowPool.cs`, constructor, right after
  `var mat = materials.Clone(MaterialLibrary.ParticlesAdditive);`.
- **Add:**
  ```csharp
  // The design draws the tutorial blocks at render order 4 and the glows at 5: the glow goes
  // on top of a waiting block. Blocks are Lessons/Block at queue 3005 (after glass 3000, snow 3002, frost 3004).
  mat.renderQueue = 3010;
  ```
- **Why:** with the pool on the `ParticlesUnlitAdditive` template's queue 3000 the glow is drawn first, and the 0.6-opaque
  block in front of it dims it to 40 % where they overlap; the design adds the glow over the block.
- **Workaround:** none needed for correctness. The screenshots show a slightly softer glow under a waiting block than the
  design.

## 3. Merge note for Room (not a Core change)

The deck's body joins the side table's grab handles in `LessonsModule.BuildDeck` (`LessonsModule.AddHandles`): when the
`RigId.Table` rig's `Handles` is a `List<Renderer>` (Room's `SideTable` hands out its own list, filled once in `Build`)
the deck's five handle renderers are appended to it in place; otherwise a combined list replaces it once. Room
(Order 20) must fill its list in `Init` and never replace it afterwards. Room's side table is 8 draws and the deck 7
(`TapeDeck.DrawBudget`, measured by `LessonsShotTests.TheTapeDeckAloneDrawsWithinItsBudget`): 15 together, the budget.

## 4. Audio boot race (Audio module, not a Lessons change)

- **File:** `Assets/MusicRoom/Audio/Engine/SynthSource.cs` (the revised foundation still has it: `Awake` assigns
  `_voices` at line 99 and `_mix` / `_send` / `_pending` at lines 107-109; `OnAudioFilterRead` guards only
  `_voices == null` (line 206) and then clears `_mix` (line 213)).
- **Bug:** the mixer thread may call `OnAudioFilterRead` while `Awake` runs. A block between the two assignments calls
  `Array.Clear(_mix, ...)` on null: `ArgumentNullException: Value cannot be null. Parameter name: array` at
  `SynthSource.cs:213`, which Unity logs as an exception, so **any PlayMode test that boots the app at that moment fails**
  ("Unhandled log message"), headless or not. Seen before the rebase in Lessons tests (`HotPathsAllocateNothing`,
  `DrumAndHarpBlocksLandOnTheirPiecesAndStrings`), and after it in the foundation's own tests: 2 of the first 5 PlayMode
  runs on the revised foundation failed one test each this way (`FrameOrderTests.OneFrameRunsThePhasesInTheDocumentedOrder`,
  `BootOptionsTests.ManualFramesStepHandsAndClockExactly`, both under `--gpu`). A single green `test-play` therefore does
  not prove the suite is stable until this is fixed.
- **Fix (in the Audio module, `SynthSource.Allocate`):** build every buffer first
  and publish the voice pool last, e.g.
  ```csharp
  _mix = new float[MaxFrames]; _send = new float[MaxFrames]; _pending = new float[MaxFrames];   // ... resonator, ring
  var voices = new Voice[_poolEnd];
  for (int i = 0; i < _poolEnd; i++) voices[i] = /* as today */;
  Volatile.Write(ref _voices, voices);          // the audio thread's guard: everything it touches exists now
  ```
  and read it once per block in `OnAudioFilterRead` (`var voices = Volatile.Read(ref _voices); if (voices == null || _mix == null) { clear; return; }`).
  The merge takes the Audio module's version of the file.

---

## Phase 0 of the many-instrument platform (workstream B, 2026-11-01): requests outside Lessons

Nothing below blocks the module: it compiles and its tests pass without them (the workarounds are noted).

### P1. `PlaceholderInstrument` resolves the recorder lazily (Core/Registry, the stubs' body)

The stubs look `IRecorder` up in `Setup` (their modules' Init, Order 33-37), before the Lessons module (Order 40)
registers it, so a stub's knock never reaches the recorder: no `RecorderEvent`, no lesson hit, no recording, no tip
success. In `Core/Registry/PlaceholderInstrument.cs`, `LiveKnock`:
```csharp
if (_recorder == null) _recorder = _app.Get<IRecorder>();   // Lessons (Order 40) registers it after the stubs' Init
if (_onset != null) _recorder?.Add(new TakeEvent { type = _onset, i = region, vel = vel });
```
Workaround: `Tests/PlayMode/Lessons/GenericTutorialPlayTests.StubRecorders` sets the field by reflection.

### P2. `ITutorial.CountNoun` and the menu's summary (Core, Menu; B4)

`Core/Contracts/Lessons.cs`, `interface ITutorial`:
```csharp
/// <summary>B4 (platform.md 7.2): what the summary counts: the lesson instrument's LessonSpec.CountNoun on the generic
/// path ("strokes", "rings"), "notes" for the classic three. The default: "notes".</summary>
string CountNoun => "notes";
```
(`Tutorial.CountNoun` already implements it.) `Menu/MenuText.cs` `Summary(int notes, int mistakes, float seconds,
string noun = "notes")` printing `noun` instead of the literal " notes" (and keying its cache by the noun too), and
`Menu/MenuController.cs:614` `SummaryLine = Text.Summary(s.Notes, s.Mistakes, s.Seconds, Tutorial.CountNoun);`.

### P3. Layout keeps a lesson whose instrument is gliding in (Layout)

`Tutorial.Start` calls `Bring(kind, Lesson)` first and its countdown waits for `InstrumentMoveEvent{kind, Arrived}`.
`Layout/LayoutModule.cs:382-383` stops a lesson whose instrument is not in the scene; it must not stop one whose
instrument is on its way:
```csharp
if (tutorial != null && tutorial.State != TutorialState.Idle && tutorial.LessonId != null && !IsInScene(tutorial.Instrument)
    && !IsMoving(tutorial.Instrument)) tutorial.Stop();
```
and `IsMoving(kind)` true from the moment Bring starts a glide until Arrived (platform.md 4.5). The Lessons module's own
check skips a lesson that waits for its instrument (`Tutorial.AwaitingArrival`) or whose instrument `IsMoving`.

### P4. The menu no longer needs to turn the single layout before a lesson (Menu, optional)

`Menu/MenuController.cs:715` (`Layout.Set(LayoutMode.Single, k)` before `Tutorial.Start`) is now redundant: Start brings
the instrument itself (and falls back to that same `Set` when the layout cannot `Bring`). Harmless if kept.

### P5. RegistryLintTests allowlist (Core tests)

Instrument names that remain in Lessons by design: `Lessons/Tutorial.cs` and `Lessons/LessonHints.cs` (the tutorial's
three classic paths: `IsClassic`, `KindOf`, `TravelFor`, the per-instrument hit, block and glow code). `EventRoles.Classic`
names only Core's `TakeEventType` constants. `LoopHost.RouteVersion`, `LessonStage` and `LessonLibrary` read the registry.
