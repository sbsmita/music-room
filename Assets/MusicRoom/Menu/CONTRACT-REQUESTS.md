# Menu: contract requests for the merge

## Phase 0 of the many-instrument platform (workstream C, 2026-11-01): open

The Instruments / Lessons tile grids, the portraits, instrument options and the panel home need no Core change except
item 7 below (the menu works without it: the struct lives in Menu meanwhile). Item 8 is a note for the lint.

### 7. `MenuRowEvent` into Core (sunny-tour.md 7.2; platform.md 5.1)

Every row press publishes `MenuRowEvent { RowId, Screen, Confirming }` (tiles "inst.<id>", "layout.all" /
"layout.single", "lock", "classic", "options.<id>", "option.<id>.<Key>", and every sunny-tour id: "lessons",
"instruments", "recordings", "back", "course", "exit", ...; `MenuRowIds` / `MenuText` in Menu). Core has no such type
yet, and the Guide (which cannot reference Menu) will consume it, so the menu declares it in `Menu/MenuRow.cs`
(namespace `MusicRoom.Menu`) for now. Request, in one step:

1. add to `Core/App/Events.cs` (namespace `MusicRoom.Core`), exactly the sunny-tour shape:

```csharp
    /// <summary>Menu: a row was pressed (sunny-tour.md 7.2). RowId: the row's stable id ("lessons", "inst.handpan",
    /// "layout.all", "option.theremin.tone", ...); Screen: the screen it was on; Confirming: a first poke that asks again
    /// (Exit, Delete).</summary>
    public struct MenuRowEvent { public string RowId; public MenuScreen Screen; public bool Confirming; }
```

2. delete the declaration (with its doc comment) from `Assets/MusicRoom/Menu/MenuRow.cs`. Nothing else changes: the
   menu and its tests name `MenuRowEvent` unqualified with `using MusicRoom.Core;`, so they bind to Core's. (Do both
   at once: with both declared, `MusicRoom.Tests.*` files that import both namespaces would be ambiguous.)

### 8. Note for `RegistryLintTests` (no change requested)

`Menu/*.cs` names no `InstrumentKind.<X>`, `RigId.<X>`, instrument id string, `UnitId.<X>` or `SourceId.<X>`; only
`InstrumentKind.Table` (its `IGuideAnchors.Kind`) and `InstrumentKind.Menu` (its own `IInstrument.Kind`). It still
reads `IPianoApi` (an interface, not an id) for spec 08's tested key-front rule (`MenuMath.PlaceFor`: the panel never
opens inside the piano's box). If the lint grows to interface names, allowlist that one (like Layout's piano key space).

## Integration status (2026-10-31): APPLIED

Merged into the project by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `ITutorial.Mistakes` | Added (default = `Summary.Mistakes`), plus `Digit`; `MenuController` reads `Tutorial.Mistakes` and `Tutorial.Digit` |
| 3 | `SynthSource` race | Audio module's version merged. `MenuBootGuard` is kept (now a no-op; removing it is a cleanup for later) |
| 4, 5 | ARCHITECTURE 9 / 15.6 text | Recorded in ARCHITECTURE.md section 21 (integration entry); the command-buffer canvas is accepted and its device check is section 19 item 28 |
| 6 | TMP font asset upgrade | Accepted: the re-serialised `LiberationSans SDF.asset` (serializedVersion 8) is kept in the project |

---

The menu works without any of these (module-local workarounds are in place); they make the contracts and the
architecture say what the menu does instead of what it assumes. Status after the rebase onto the review-round-1
foundation (ARCHITECTURE.md section 21):

| # | Request | Status |
|---|---|---|
| 1 | `ITutorial.Mistakes` (live wrong-play count) | open |
| 2 | Desktop look-drag blocking | **done by the foundation** (`DesktopRigFallback.dragBlockMask = Layers.PlaySurfaceMask`, set by AppRoot); the menu's own `dragBlocked` chain is removed |
| 3 | `SynthSource.Awake` publishes `_voices` before `_mix` | open in the foundation; **fixed in the Audio module's clone** (`m-audio`, `Allocate()` publishes the pool last) |
| 4 | ARCHITECTURE.md section 9 text | open (text only) |
| 5 | ARCHITECTURE.md 15.6: accept the command-buffer canvas | open (text + one device checklist item) |
| 6 | One-time TextMeshPro font asset upgrade | open (foundation housekeeping) |

## 1. `ITutorial`: a live mistake count (Core/Contracts/Lessons.cs)

The lesson screen draws the progress line, `"H of T"` and `"Mistakes: N"`, live while the lesson runs
(from the tutorial's hits, total and mistakes). `ITutorial` has no live
mistake count, only `Summary` (filled at the end).

Request, in `public interface ITutorial`:

```csharp
/// <summary>Wrong plays in reach so far (the tutorial's mistakes), live during the lesson.</summary>
int Mistakes { get; }
```

and document that `EventsDone` / `EventsTotal` are the tutorial's `hits` / `total`, which count **targets** (a chord is
several), not events.

Workaround now: `MenuController.LessonRows` reads `EventsDone` / `EventsTotal` as hits / targets and
`Summary.Mistakes` as the live count. The Lessons module should keep `Summary.Mistakes` current while the lesson runs
until the property above exists; then change that one line to `Tutorial.Mistakes`.

## 2. Desktop look-drag blocking: done

The revised foundation wires `DesktopRigFallback.dragBlockMask = Layers.PlaySurfaceMask` in `AppRoot.Awake`, and the
menu face's MeshCollider is on `Layers.PlaySurface`, so a press on the face (a click, or the start of a list drag)
never turns the desktop camera. The menu no longer touches `dragBlocked` (PlayMode
`HiddenOnUnmountAtOnceThenFadesOut` asserts the face's layer is in `dragBlockMask`).

## 3. Audio: `SynthSource.Awake` publishes `_voices` before `_mix` (Audio/Engine/SynthSource.cs:97-107)

Not a contract, a foundation race: `OnAudioFilterRead` guards on `_voices == null`, but `Awake` assigns `_voices`
before `_mix`, `_send`, `_pending`. When the mixer thread runs the filter in between, `Array.Clear(_mix, ...)` throws
`ArgumentNullException` and the Test Framework fails whichever test is booting. Seen again in this round: the first
`test-play --gpu` after the rebase failed the foundation's own
`AppBootTests.HoverGlowsAreOneDynamicMeshWithoutPropertyBlocks` in its SetUp with exactly this exception.

Request (the Audio module's clone already does this in `SynthSource.Allocate`): build every buffer and the resonator
first, then publish the pool last, and read it with a barrier on the audio thread:

```csharp
// Awake, after ConfigureAudioSource:
_ring = new SpscRing<SynthCommand>(512);
_toneFrom = noiseVoices; _metalFrom = _toneFrom + toneVoices; _bufferFrom = _metalFrom + metalVoices; _poolEnd = _bufferFrom + bufferVoices;
_mix = new float[MaxFrames]; _send = new float[MaxFrames]; _pending = new float[MaxFrames];
if (resonatorModes != null && resonatorModes.Length > 0)
{
    var resonator = new ModalResonator();
    resonator.Build(resonatorModes, resonatorSeconds, (float)_sampleRate, resonatorSeed);
    _resonator = resonator;
}
_bus.SetValue(busGain);
var voices = new Voice[_poolEnd];
for (int i = 0; i < _poolEnd; i++)
    voices[i] = i < _toneFrom ? new NoiseVoice() : i < _metalFrom ? new ToneVoice() : i < _bufferFrom ? (Voice)new MetalVoice() : new BufferVoice();
Volatile.Write(ref _voices, voices);

// OnAudioFilterRead:
if (frames > MaxFrames || Volatile.Read(ref _voices) == null) { Array.Clear(data, 0, data.Length); return; }
```

In this clone only, `Assets/MusicRoom/Audio/Engine/SynthSource.cs` carries exactly that change (marked "m-menu clone
only"), so the whole suite is green on every run here; the merge takes the Audio folder from the Audio module, not
from this clone. The menu's test boots also keep `Tests/PlayMode/Menu/MenuBootGuard.cs`, which drops only this exact
exception (ArgumentNullException with `SynthSource.OnAudioFilterRead` in its stack) during the boot frame and fails on
anything else; delete it (and its `Begin/End/Stop` calls) once the Audio fix is merged.

## 4. ARCHITECTURE.md section 9: notes for the text (no code change)

- Six home rows: the menu puts **Set up my room** right after the Room row: Lessons, Instruments, Recordings,
  Music room, Set up my room, Show me around (rows h 84, gap 10, from y 132, the last ending at 686).
- `IMenuPanel` owner returns `PlaySurface.None` from `PlaySurfaceAt`: a pinch on the panel is a grab of the panel
  (the grab checks the menu zone before anything else and the play surfaces have no menu entry). Layout should use
  `IMenuPanel.GrabZone` / `Rig.Zone` (both provided) for "the menu zone first" and for "outside every zone".
- `Rig.Handles` of the Lessons rig is the fixed list `{ Back, Face }`; while the panel is hidden both renderers are
  inactive in the hierarchy, so Layout's "only shown renderers" rule gives the hidden panel no footprint.
- With no passthrough (desktop, or a failed init) the Room row reads "Play in my room" · "Not available" and does
  nothing when poked.
- The panel is put away when `App.IsPresenting` changes (XR session start and end;
  spec 08 section 1.2).

## 5. ARCHITECTURE.md 15.6: accept the command-buffer canvas (text + device checklist)

15.6 asks for a disabled camera on `Layers.PanelUI` driven by `RenderPipeline.SubmitRenderRequest` with uGUI/TMP. The
menu instead paints its 1024 x 768 render texture with one `CommandBuffer` (signed-distance shapes from
`MusicRoom/MenuShape` and the TextMeshPro meshes of parked, scale-static text objects) run by
`Graphics.ExecuteCommandBuffer` in its EndFrame phase. It keeps every 15.6 rule, now asserted by GPU tests
(`MenuScreenshotTests`): at most one repaint per frame inside `Profiler.BeginSample("[Menu] redraw")`, none while idle
or for hover, exactly one per user action (`TheCanvasRepaintsOnlyWhenItsContentChanges`); hover as face-material
uniforms (`TheFaceShaderLightsTheHoveredRowInTheRenderedView`); 2 draws in the room for the open panel, 3 with the
summon ring (`TheOpenPanelStaysInsideTheMenuDrawBudget`, budget 4). It is cheaper than a URP camera request (no
culling, no pipeline setup, no render-target switch per camera), and it needs no PanelUI layer.

Request, 15.6 "Panel rendering", add after the first sentence:

> Alternative (the Menu module's choice, accepted): paint the texture with one `CommandBuffer` executed by
> `Graphics.ExecuteCommandBuffer` outside the camera loop (orthographic canvas-pixel projection, `_ScreenParams` set to
> the target's size, TMP meshes drawn with their font material), under the same redraw rules.

and in section 19 (device checklist), "World / render":

> 28. The menu canvas painted through `Graphics.ExecuteCommandBuffer` from `Update` renders correctly on Quest 3S
>     (Vulkan, single-pass instanced): text and shapes upright and crisp, no flicker on repaint, and URP's per-camera
>     globals (`_ScreenParams`, view-projection) are restored for the next camera render (the desktop Metal shots are a
>     proxy, not proof).

## 6. One-time TextMeshPro font asset upgrade (foundation housekeeping)

The first editor run that builds TextMeshPro text (any GPU test that draws text, e.g. the menu's screenshots)
re-serialises `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` in TMP's newer format
(`serializedVersion` 6 -> 8, new material and font-asset fields, about 675 diff lines; no glyph or atlas change, the
atlas stays static). The menu restores the foundation's copy in its clone after each GPU run, so the merge never sees
it; the integrator will see the same diff after the first GPU run of the merged project. Request: accept the upgraded
asset once in the foundation (run one GPU test that draws text, then keep the re-serialised file), so module runs stop
producing an unexplained change outside their folders.
