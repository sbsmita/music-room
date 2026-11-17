# Drums: contract requests and merge notes

## Integration status (2026-10-31): APPLIED

Merged into `vr-music-room-unity` by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `IPlayZone` comment | Applied in `Core/Contracts/Rigs.cs` |
| A | `SynthSource` race | Audio module's version merged (see Piano 2) |
| B | One contact shadow, drawn by Room | Kept: Room's `RigShadows` draws the kit's blob; the harp's blob moved to Room too. `IntegrationShotsTests` asserts one entry per instrument and no instrument-built blob |
| test | `DrumsPlayTests` | The probe now sits in front of the kit's `IDrumsApi` (Order 30) and records the REAL Layout's `TryPickUp` calls, since the integrated app has the real Layout |

---

The module is built and tested against the round-1 foundation (ARCHITECTURE.md section 21): `IDrumsApi.TryPickUp`, `Rig.Handles` as a list filled once, `App.IsPresenting`, `Layers.PlaySurface`
click colliders, `Pointer.FromMouse`, `SharedTextures`, `TestBoot`, `AudioTestTap.Calls`. Nothing below blocks the merge.

## Withdrawn

- (old) `IGrabService.IsGrabbing(hand)`: Layout calls `TryPickUp` itself, only for hands that are neither grabbing nor
  holding (section 15.5), so Drums needs no hand-of-the-grab query.
- (old) "the stick pick-up consumes the pinch": section 15.2 decided the opposite (Layout consumes on `Grabbed`, or on
  `Near` unless a rig handle is within .02). Drums runs no pick-up pass of its own and never writes `PinchStart`.
- (old) `Rig.Footprint`: the Layout clone (`LayoutModule.Arrange.cs` `Collect`) already builds footprints from
  `Rig.Handles` plus the rig's `IInstrument.PlayedParts` filtered by `IsChildOf(rig.Group)`, which covers the cymbals and
  heads. No Core change needed. Layout reads `enabled` / `activeInHierarchy` (never `isVisible`), which
  is what the kit's `forceRenderingOff` handle renderers need.
- (old) "Drums builds the kit's contact shadow, Room must not": reversed. The Room module now draws every floor decal,
  the kit's included (see merge note B), so Drums builds none.

## 1. `IPlayZone` comment: what a non-piano rig zone is

- File: `Assets/MusicRoom/Core/Contracts/Rigs.cs`, the summary of `interface IPlayZone`.
- Replace

  ```csharp
  /// <summary>A play zone for the grab guards (world test). The piano's is its InPlayZone box; others use play surfaces.</summary>
  ```

  with

  ```csharp
  /// <summary>A rig's zone for the grab guards and "near an instrument" (world test). The piano's is its InPlayZone box;
  /// the other rigs' is the rig zone: the group's world box (every mesh, the contact
  /// shadow's floor rectangle included) expanded by GrabConstants.ZonePad (.12). Play surfaces are
  /// IInstrument.PlaySurfaceAt.</summary>
  ```

- Why: Layout uses zones for "near an instrument" (swivel start, two-hand summon). With play surfaces
  only, a pinch on a stand would be "outside every zone", and a swivel could snap-turn the seat on a rig grab. The kit's
  `Rig.Zone` already is that box (cached per frame; `DrumKit.ZoneBounds()`), and the Layout clone's
  `NearInstrument` already consults `Rig.Zone`.

## Merge notes (no foundation change)

### A. `SynthSource` published `_voices` before its buffers (Audio, foundation bug)

- Seen in foundation-v1 and the round-1 foundation: `ArgumentNullException ... array` at
  `SynthSource.OnAudioFilterRead` (`Array.Clear(_mix, ...)`) during a test boot, because `Awake` assigned `_voices`
  before `_mix`, `_send` and `_pending`, and the audio thread treats a non-null `_voices` as ready.
- Covered by the Audio module's round-2 clone (`m-audio`, `SynthSource.Allocate()`: buffers first, the voice pool
  published last). Keep that version at the merge. In this clone (round-1 Audio) the drums PlayMode tests ran green
  twice (31/31 and 33/33 with the GPU), but the race is still possible here.

### B. One contact shadow under the kit, drawn by Room

- The kit's floor blob (Docs/07-world-visuals.md, `rigDecor.drumsBlob`: kit-local (.1, .5), 1.9 x 1.6 m, opacity .45) is
  the Room module's `RigShadows` entry `RigId.Drums` (`Room/Table/SideTable.cs`), drawn with Room's
  `MusicRoom/FloorDecal` shader (Offset -1,-1, depth write off, the linear-framebuffer alpha `1 - (1 - a)^2.2`), and
  following the rig on the floor. That entry must stay in Room. Drums draws no shadow (it would darken the floor twice
  and had no depth offset); its `Rig.Zone` still includes the blob's rectangle.
- Check at the merge: the full-room screenshot shows exactly one soft dark blob under the kit.
