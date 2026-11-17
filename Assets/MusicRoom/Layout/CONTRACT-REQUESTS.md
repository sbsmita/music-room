# Layout: contract requests

## Integration status (2026-10-31): APPLIED

Merged into `vr-music-room-unity` by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `IsPresent` honours `Rig.InScene` | Doc applied in Core; `PianoInstrument.IsPresent` changed |
| 2 | `IPianoApi.OnSurface` | Added (default false); the piano implements it; `LayoutModule.PianoOnSurface` uses it (the 1 cm height heuristic is gone) |
| 3 | `SynthSource` race | Audio module's version merged |
| 4 | `Rig.Handles` comment | Applied in `Core/Contracts/Rigs.cs` |
| MR 2b | `_pianoHomeHeight` armed by every `MixedRealityEvent` | Fixed: armed only when `Active` changes |
| test | `LayoutStubTests.PlaceAroundPiano...` | The piano is now placed inside the VR room box (real rigs have footprints; the stubs had none) |

---

The module is rebased on the revised foundation (ARCHITECTURE.md section 21, review round 1). The v1 stand-ins are gone:
Layout now calls `App.IsPresenting`, `IDrumsApi.TryPickUp`, `IMixedReality.StartPlacing`, reads `Rig.Handles` as the
`IReadOnlyList<Renderer>` its module filled once, implements every `ILayoutService` member, and its PlayMode tests boot
through `TestBoot` with a `MemorySettingsStore`. The items below are the changes still wanted OUTSIDE this folder, with
the exact code. None blocks the merge: each has a module-local fallback, given under it.

## 1. Every `IInstrument.IsPresent` honours `Rig.InScene` (Core doc + Piano)

Why: during a page turn the incoming instrument is drawn while it slides in, but it stays out of the
instrument and rig lists until the finished turn's `Apply()` (spec 08 section 5.3), so it is never played, hovered or
grabbed while it moves. Layout now keeps `Rig.InScene = false` for the incoming rig until the turn ends (its group stays
active so it renders). Core ticks an instrument when `IsPresent`, so the instrument must read `Rig.InScene` too.
Drums (`DrumKit.IsPresent`), Harp (`HarpInstrument.IsPresent`) and Room (`AmbiencePanel.IsPresent`) already do; the Piano
does not (`PianoInstrument.cs:194: public bool IsPresent => isActiveAndEnabled;`).

- `Assets/MusicRoom/Core/Contracts/Instruments.cs`, `IInstrument.IsPresent`, replace the doc comment with:
  ```csharp
  /// <summary>In the scene, visible and playable: false while the instrument's Rig.InScene is false (its rig is out of
  /// the layout, or is the incoming instrument of a page turn, shown while it slides in but not yet in the layout).
  /// Exception: the Menu is ALWAYS present: in its update it runs its summon gesture and open/close
  /// animation at the start of Tick and guards its pokes with Shown itself.</summary>
  bool IsPresent { get; }
  ```
- `Assets/MusicRoom/Piano/PianoInstrument.cs` (Piano module):
  ```csharp
  public bool IsPresent => isActiveAndEnabled && (Rig == null || Rig.InScene);
  ```
  (`Rig` = the piano's `Rig` as the module registered it; the Drums module's `DrumKit` has the same pattern.)

Fallback until then: the incoming piano is ticked during the 0.35 s slide (only the turn TO the piano is affected);
Layout itself already treats it as absent (no grab target, no zone, not "near an instrument").

## 2. `IPianoApi.OnSurface { get; }`

- File: `Assets/MusicRoom/Core/Contracts/Instruments.cs`, interface `IPianoApi`, after `SetOnSurface`:
  ```csharp
  /// <summary>True after SetOnSurface(true) (the piano stands on a real table in MR, its stand hidden), false after
  /// ResetSurface().</summary>
  bool OnSurface { get; }
  ```
- Reason: the piano is on a surface when mixed reality is active and any of its lower (stand) parts is hidden: a page
  turn back to a table-placed piano keeps the table height instead of the stand height.
- Fallback now (`LayoutModule.PianoOnSurface`): in mixed reality, a piano more than 1 cm from its home height counts as on a
  surface. Once the member exists: `bool PianoOnSurface() => MrActive && PianoApi != null && PianoApi.OnSurface;`.

## 3. Audio: `SynthSource` start-up race (Audio module; also in the revised foundation)

- File: `Assets/MusicRoom/Audio/Engine/SynthSource.cs`, `OnAudioFilterRead`, first guard:
  ```csharp
  if (frames > MaxFrames || _voices == null || _mix == null || _send == null || _pending == null)
  ```
  or, in `Awake`, assign `_voices` last (after `_mix`, `_send`, `_pending`, `_resonator`).
- Reason: `Awake` assigns `_voices` (line 99) before `_mix` / `_send` / `_pending` (107-109). The mixer thread can enter
  `OnAudioFilterRead` between them, pass the `_voices == null` guard and throw `ArgumentNullException` at
  `Array.Clear(_mix, ...)`; the test framework then fails whichever test logged it. Last round it hit 3 of 6 `test-play`
  runs (always in foundation tests that boot the Audio module); this round's runs did not hit it.

## 4. Documentation only: `Rig.Handles` comment

- File: `Assets/MusicRoom/Core/Contracts/Rigs.cs`, field `Rig.Handles`. Suggested comment:
  ```csharp
  /// <summary>Visible meshes whose bounds are grab handles (played parts excluded by the module). Filled ONCE by the
  /// instrument in Init (mutate the same list in place if parts change); readers never allocate. The placement
  /// footprint, eye clearance and the desktop Shift / Alt pick use these plus the PlayedParts of the rig's instruments
  /// (not every visible mesh of a rig).</summary>
  ```

## No longer requested

- `IHandTool.Root` (last round's item 8): the Drums module reparents a held stick out of the kit's group
  (`DrumSticks.cs:204`), and `PlacementSolver.Shows(renderer, group)` now leaves any part that is no longer under its rig
  out of the footprint, the eye clearance and the desktop pick, with no Core change.
- Last round's items 1-6 (the merge wiring onto `ILayoutService`, `App.IsPresenting`, `IDrumsApi.TryPickUp`,
  `IMixedReality.StartPlacing`, `Rig.Handles` as a list, `TestBoot`): done in this folder.
- `ILayoutService.FadeOf(Rig)` / `TurnProgress`, `IGrabService.Disarm(HandState)`: not needed (ARCHITECTURE.md 15.5 fixes
  the page turn as slide and scale only; Layout calls `TryPickUp` itself). `LayoutModule.FadeOf`, `TurnProgress` and
  `Disarm` stay public.

## Notes for other modules (no Core change)

- Menu: while the MR placing pinch runs, Layout deactivates the menu rig's `Group` (the menu stays `Shown`; only the
  panel's group is hidden) and activates it again when the rigs come back (`PlaceAroundPiano`,
  `ArrangeAroundPlayer`, `RestoreHomes`). The real Menu keeps its root active and toggles a child visual, so the two do
  not fight; it should not re-activate its root itself while `IMixedReality.IsPlacing` (nothing ticks then anyway).
- Contact shadows that live under a rig (the drums, harp and table blobs) should stay on the floor when a grab lifts
  the rig (they are lowered on release); the owning module can do this in LateUpdate from
  `Rig.Group.position.y`.
- Desktop: Layout chains a "do not look around while a mouse rig drag runs" test into `DesktopRigFallback.dragBlocked` and
  restores the previous delegate in `Shutdown`; a module that sets `dragBlocked` later should chain the existing one.
  A desktop press with Shift / Alt held belongs to the rig drag (it never reaches the instruments'
  desktop press handlers); the Piano, Drums, Harp, Room and Lessons clones already ignore their clicks then.
