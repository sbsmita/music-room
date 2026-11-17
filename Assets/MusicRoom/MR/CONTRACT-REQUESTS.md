# MR module: contract requests and merge notes

## Integration status (2026-10-31): APPLIED

**2026-11-13:** the snowy window and the snow portals were removed at the player's request (MR/README.md); every
`ISnowPortals` / `MrWindow` / snow-portal item below is history.

Merged into the project by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | Room's `ISnowPortals` | The real Room module is merged; MR's own drawer stays the fallback |
| 2a | Layout's StartPlacing | m-layout already calls `IMixedReality.StartPlacing` directly (no hook) |
| 2b | `_pianoHomeHeight` | Fixed in Layout: armed only when `MixedRealityEvent.Active` changes |
| 3 | `ISnowWeather.Time` | Added (default 0); Room's `SnowWeather.Time` implements it |
| 4 | `AudioModule.SnowPosition` / `WindPosition` | Added; `MrConstants.SnowSourceVr` / `WindSourceVr` forward to them |
| 5 | `SynthSource` race | Audio module's version merged |
| 7 | Section 19 device item | Added as item 29 |
| test | `MrShotsTests.NoSnowPixelOutsideTheWindow` | Every renderer outside the checked portal goes to the skipped layer (the real instruments are visible in MR now) |

---

This folder is now built against the revised foundation (ARCHITECTURE.md section 21). The round-1
contracts are used directly; the old runtime bridge (`CoreBridge`) and MR's own placing dwell (`PinchDwell`) are gone.
Nothing here blocks the merge. The items below are for other owners, each with the exact change.

## 1. Round-1 contracts: done in MR, nothing required

| Contract | How MR uses it now |
|---|---|
| `IMixedReality.StartPlacing()` | Implemented. Layout's Grab step 5 is the only thing that starts placing (MR has no dwell of its own any more). It needs `Active` and `App.IsPresenting`. The placing hand is the one pinching longest. |
| `ILayoutService.PlaceAroundPiano()` | Called after every landing (best table, player's pinch). |
| `ILayoutService.RestoreHomes()` | Called on `Exit()`, then `ArrangeAroundPlayer(true)` (ARCHITECTURE.md 12, step 4). |
| `ILayoutService.Anchor` | Gives the catcher's and the sun's pose. The anchor rig's group only says whether that rig is shown. |
| `ISnowPortals` / `ISnowPortal` (Room) | `WindowSnow` creates every portal with `App.Get<ISnowPortals>().Create(MR root, size, true)`, poses it with `SetPose`, feeds `SetFrost(max(cover, .4 level), .1 level)`, calls `SetVisible` on a change and `Destroy` when the window goes. MR's own drawer (`MrSnowPortals`, the `MR/Snow` and `MR/Frost` templates) runs only when no `ISnowPortals` service is registered. |
| `XrMode.PassthroughPending` | `Available` includes it, and `Enter` waits up to 2 s while it is true. |
| `TestBoot`, `MemorySettingsStore`, `SimulateHeadset` | Every MR PlayMode test boots through `TestBoot(simulateHeadset: true)`. |

**Merge check:** the revised foundation's Room stub registers an `ISnowPortals` that returns an empty, posed root. If
the stub is what gets merged, the windows show no snow. The real Room module (m-room) implements it, so merge that one.

## 2. Layout module (merge notes; no Core change)

a. **Wire Grab step 5 to the contract.** m-layout calls a `StartPlacingHook` in its CONTRACT-REQUESTS 4. At the merge,
   bind it to the Core member:
   ```csharp
   StartPlacingHook = () => { var mr = _app.Get<IMixedReality>(); if (mr == null || !mr.Active) return false; mr.StartPlacing(); return mr.IsPlacing; };
   ```
   (Or call `Mr.StartPlacing()` directly where the hook is invoked.)

b. **Drop `_pianoHomeHeight`, or arm it only once per entry.** m-layout `LayoutModule.cs:118` sets the flag on *every*
   `MixedRealityEvent`. MR also publishes `MixedRealityEvent { Active = true }` after every scene load, so the menu re-reads
   `Status`. As a result, the first `ArrangeAroundPlayer` after a load (a two-hand summon, a recentre) puts a piano that
   stands on a real table back at stand height (0.72 m). The piano then sinks into the desk it is over (desk 0.74 m, body
   underside 0.628 m). ARCHITECTURE.md 12.1 gives the height reset to MR, which already does it before its first arrange.
   The fix:
   ```csharp
   void OnMixedReality(MixedRealityEvent e) { }   // MR resets the piano's height itself (ARCHITECTURE.md 12.1)
   ```
   or set the flag only on a change of `e.Active` (`if (e.Active != _mrWasActive) { _pianoHomeHeight = true; _mrWasActive = e.Active; }`).
   With the contract's "the piano keeps its height", MR handles a re-arrange in MR as follows. If the body still rests on
   a real top where Layout put it, the piano stays there with the stand hidden. Otherwise the stand comes back and the
   piano stands on the floor (`MrModule.OnArranged`).

## 3. `ISnowWeather.Time` (Core contract + Room): optional, MR's fallback drawer only

```csharp
/// <summary>The weather's own clock (weather.time): the shared uTime of every snow field.</summary>
float Time { get; }
```
MR's own fields keep their own clock (`_MrSnowTime`, the sum of frame dt). Room's portals use Room's globals, so this
matters only when MR draws.

## 4. Audio: the VR positions of the Snow and Wind sources (Audio module)

`AudioModule.Init` hard-codes `(3.1, 1.55, -1.18)` and `(3.1, 1.9, -1.18)`. MR moves those sources to the nearest window
in MR and has to put them back on exit. Please expose:
```csharp
public static readonly Vector3 SnowPosition = new Vector3(3.1f, 1.55f, -1.18f), WindPosition = new Vector3(3.1f, 1.9f, -1.18f);
```
Workaround in place: `MrConstants.SnowSourceVr` / `WindSourceVr` (the same values).

## 5. Audio: `SynthSource` start-up race. Fixed in m-audio, not yet in the revised foundation

The revised foundation's `SynthSource.Awake` still assigns `_voices` before `_mix` / `_send` / `_pending`, while
`OnAudioFilterRead` guards only on `_voices`. A mixer callback that lands in between throws `ArgumentNullException` at
`Array.Clear(_mix, ...)`. This intermittently failed foundation PlayMode SetUps in October. The Audio module's own clone
(m-audio) already allocates everything first and publishes `_voices` last (`Volatile.Read` in the callback), so this item
closes when that Audio folder is merged. This clone runs the revised foundation's Audio, as instructed, and the race
showed again on 2026-10-31 in 1 of the 11 PlayMode runs of that day (7 of them with the GPU), in the foundation's
`FrameOrderTests` SetUp (`SynthSource.cs:213`).

## 6. Rendering

- **URP's `_MainLightShadowmapSize` is only written with soft shadows on** (URP 17.3
  `MainLightShadowCasterPass.cs:423-434`; QuestURP keeps soft shadows off). Any custom shader that filters the main-light
  shadow map with that uniform reads a stale value, the empty map's `(1, 1)`. MR's shadow catcher did, and its shadow
  showed at 1/9 strength. It now reads the texel from the texture (`GetDimensions`). Other modules writing custom shadow
  receivers should do the same. Nothing to change in Core.
- The MR draw budget (≤ 80 draws) and casters limited to the instrument bodies need `shadowCastingMode = Off` on every
  renderer that is not an instrument body: menu panel, Sunny, tape deck and switch panel, hover glows, hands. MR's own
  renderers already have it off. Instrument modules should keep `ShadowCastingMode.On` on the bodies, or the bodies cast
  no shadow onto the catcher in MR. No shadow pass runs in VR, so this costs nothing there.

## 7. ARCHITECTURE.md section 19 (device checklist): one addition

> 28. Entering MR resets the player rig (`RigRoot` to the origin) before MRUK's world lock engages, with
> `MRUK.TrackingSpaceOffset` = the rig root's pose (identity). After a VR seat fit and a swivel, entering "Play in my
> room" shows no head jump when the scene loads and the lock takes over; leaving MR, the VR layout comes back around
> the player.

## 8. Menu (no change to Core; for the Menu engineer)

- The Room row calls `IMixedReality.Enter()` / `Exit()`.
- The "Set up my room" row shows when `IMixedReality.Status == MrStatus.NoRooms` and calls `RunSpaceSetup()`, which needs
  the headset (`Rig.IsXrActive`).
- MR republishes `MixedRealityEvent { Active = true }` whenever a scene load finishes in MR, so a menu that redraws on
  `MixedRealityEvent` sees the new `Status`.
- `MrModule.BeginPlacing()` is available for a "Place the piano" row if one is wanted (XR only, like `StartPlacing`).
