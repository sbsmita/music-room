# MusicRoom.MR: "Play in my room" (mixed reality)

Module class `MusicRoom.MR.MrModule`, Order 70, registers `IMixedReality`. Assembly `MusicRoom.MR` references Core, Hands,
Audio, meta.xr.mrutilitykit, Oculus.VR and Unity.TextMeshPro. It ticks in `FramePhase.MixedReality` (all MR work) and
`EndFrame` (clears the used-pinch flag). Logs start with `[MR]`.

Specs: ARCHITECTURE.md sections 0, 3.3, 12, 14, 15.9 and 21 (these win on conflicts); `Docs/03-interaction-sdk-and-mruk.md` (with its
sample rooms); `Docs/07-world-visuals.md` section 9, with `ambient.mr`. The module covers placing the piano, the MR
lights and shadow catcher, and enter, auto-arrange and exit.

**2026-11-13: the snowy window is gone** (the player: "anytime if snow is clicked make it fall in whole room 360 degree
coverage. remove all window things"). The placeable snowy window (its ghost, depth-raycast placement, drag, kept pose and
spatial anchor), the snow portals behind real windows, `WindowFinder`, `WindowSnow`, `WindowStore`, `MrSnowPortals`,
`SnowFieldMesh`, the MR snow and frost shaders, Core's `ISnowPortals` / `MrWindow` / `IMixedReality.Windows`, their hints
and Sunny tips are removed (backup: music-room-vr-wip/unity-backups/pre-snow-paint2/deleted-by-roomsnow/). With SNOW on,
Room's room snow now falls all round the player in mixed reality too (Room/README.md). `RetiredWindow.Migrate` (MrModule
.Init) forgets an older save's window once (its pose, its anchor uuid, its failure count; the anchor is erased on the
headset). In MR the snow and wind sounds come from just above the head (`SnowSourceAbove` .9 m, wind .35 m higher).
Sections below that describe the window are history.

Built against the revised foundation (ARCHITECTURE.md 21). Notes for other owners are in
CONTRACT-REQUESTS.md.

## Public contract

Core (`Core/Contracts/MixedReality.cs`):
- `IMixedReality { Available, Active, IsPlacing, StartPlacing(), Status, HasSpaceSetup, Enter(), Exit(), RunSpaceSetup(), TryGetPianoPose() }`
- `MrStatus`
- `MixedRealityEvent { Active }`, published on enter, on exit, and again whenever a scene load finishes in MR, so the
  menu re-reads `Status`

What MR uses from other modules:
- `ILayoutService`: `ArrangeAroundPlayer`, `PlaceAroundPiano`, `RestoreHomes`, `Anchor`, `Mode`, `Current`, `IsInScene`
- `IRigRegistry`
- `IPianoApi`: `SetOnSurface`, `ResetSurface`
- `ISnowWeather`
- `IRoom`, `IAmbienceSwitches`, `IAudioEngine`

Module-public (tests, tools, a future menu row):
- `MrModule`: `PianoReticle`, `Lighting` (`MrLighting`: sun, catcher), `RetiredWindowSettings`, `Hint`, `HintVisible`, `HintTransform`, `PianoOnTable`, `PlacedByPlayer`,
  `Simulated`, `Loading`, `Loads`, `LastReticleHit`, `BeginPlacing(hand)` (XR only), `OnApplicationFocusChanged(bool)`,
  `PassthroughWaitSeconds` (4 s), `LeftTableDistance` (.02 m), `RestsOnTopTolerance` (.03 m)
- `RetiredWindow`: forgets the removed snowy window's saved settings (keys, `Migrate`, `TryEraseAnchor`)
- `MrMeshes`: small procedural meshes (reticle, catcher, hint panel); `HandRay`
- `MrDevOptions`: development switches (simulated passthrough off-headset, the JSON room fallback)
- Plumbing from the verified 03 sketch: `MrScene`, `TableFinder`. Since then:
  - `MrScene.SetWorldLock(on, trackingSpaceOffset)`
  - `TableFinder` keeps the piano body on shallow tops

## Behaviour

**Enter** (`Enter()`), in order:
1. On the headset, start passthrough now (`XrMode.StartPassthrough`: the app runs without it in VR, burst.md section
   10.1) and wait at most 4 s while its init is pending (`XrMode.PassthroughPending`; until it completes the underlay
   would be black, so the VR room stays up meanwhile). `Available` also counts `XrMode.PassthroughStartable`.
2. Turn passthrough on with `XrMode.SetPassthrough`. Off the headset this happens only with
   `MrDevOptions.SimulatePassthrough`: the camera then clears to `MrDevOptions.SimulatedBackground` (grey by default;
   `(0,0,0,0)` reproduces the eye buffer).
3. Reset the player rig to the origin (`rig.position/rotation = 0`). The VR seat fit and the swivel move
   `RigRoot`, and MRUK's world lock writes the tracking space's world pose itself.
4. Hide the room and its candles (`IRoom.SetVisible(false)`, `SetCandles(false)`).
5. Turn fire and wind off and keep snow on, remembering their states.
6. `IAudioEngine.SetMixedReality(true)`, then the MR lights.
7. Publish `MixedRealityEvent{true}`.
8. Ask for the USE_SCENE permission and call `LoadSceneFromDevice(false, true)` (`MrScene`). In the editor a
   `NotAvailable` result falls back to a recorded MRUK room (`MrDevOptions.SceneJson`, default `MeshLivingRoom2`).

When the scene is `Loaded`:
- world lock on, with `MRUK.TrackingSpaceOffset` = the rig root's pose (identity)
- `SetRoomBox(room bounds, real)`

**Since 2026-11-02 the instruments open in front of the player here too.** Entering never brings back a saved anchor
(an anchor an earlier version saved is forgotten: `PianoAnchorStore.Forget`, its keys deleted and, on the headset,
erased) and never puts the piano on a table by itself (`TableOnEnter`, off; the older suites of TableFinder and the
piano on the desk turn it on). Layout places every instrument in front of the player on the real floor
(`IMixedReality.FloorHeight`: the scene's floor, else the tracking space's 0), in the reach band and with the player's
comfort offsets, with or without a scene room; when the scene arrives after the arrange the instruments move onto its
floor (`SeatOnRealFloor`). The point-and-pinch placer still puts the piano on a real table when asked. The hands are the
drawn Skin hands unless the menu's "Real hands" row is on (Core's `IHandLook`).

**Auto-arrange** (the first MixedReality tick, `AutoArrange`):
1. `IPianoApi.ResetSurface()`.
2. Put the piano back at stand height (floor + 0.72). Layout keeps the piano's height in every arrange, so a piano lifted
   in VR would otherwise enter MR lifted.
3. `ILayoutService.ArrangeAroundPlayer(true)`. In MR, Layout skips fitSeat and the room box.

When the scene is loaded (now or later) and the player has not placed the piano:
- `TryGetPianoPose` runs `TableFinder.TryFindBest` from the head and poses the piano with
  `PianoPoseOnTable(best, EdgeMidpoint)`: key tops at TopY + 0.092, facing the user, key fronts pushed back 0.25 m from
  the edge. On a top shallower than 0.56 m the push is shorter, so the body (0.29 m behind the keys) still ends 2 cm
  before the far edge. Tops under 0.31 m deep are refused.
- `SetOnSurface(true)`, then `ILayoutService.PlaceAroundPiano()`.
- With no table, the stand is re-seated on the real floor once (the arrange ran before the floor was known).

**Re-arranges in MR** (`ArrangedEvent`: a two-hand summon, a recentre, a layout change) while the piano is on a table:
- If the piano moved more than 2 cm but its body still rests on a real top under it (within 3 cm), it stays there with
  the stand hidden.
- Otherwise the stand comes back (`ResetSurface`) and the piano stands on the floor at stand height. Layout keeps the
  table's height, which would leave a floating piano here.
- The catcher and the sun follow every arrange.

**Placing**:
- (2026-11-10) Nothing the player does starts placing any more: the empty-air pinch that did is retired (instruments move
  only with Move instruments, by hand) and the reticle never follows the head. `StartPlacing()` / `BeginPlacing()` stay as
  a tool path. (The snowy window that a ghost-and-pinch placed was removed 2026-11-13.)
- Formerly: Layout's Grab step 5 called `StartPlacing()` for a dwell-complete pinch in empty air, after hiding the other rigs.
  MR has no dwell of its own. `StartPlacing` needs mixed reality and an XR session (`App.IsPresenting`), and the placing
  hand is the one pinching longest. Every placing pinch is a grab pinch (`HandState.GrabPinch*`: the thumb-index rule
  OR Meta's own index pinch, since the u-arrange merge, like the Layout grab that starts it; the rule alone flickers on
  the headset), and a pinch made like the OS system gesture (`PointerInput.SystemGestureLike`) lands nothing.
- While `IsPlacing`, Core ticks no instrument (the menu included). Every other rig in the layout is hidden and
  remembered, so all of them come back when placing ends.
- Each tick the reticle lies where the placing hand's aim ray meets an upward surface, else the other hand's ray, else
  the head ray:
  - with a scene: `TableFinder.TryPointAtSurface` (TABLE / STORAGE / BED / COUCH / OTHER / FLOOR, normal.y >= .9, 6 m)
  - without a scene model: `EnvironmentRaycastManager` on the headset, normal.y > .7
  - looks: ring r .06-.08 alpha .9, footprint 1.326 x .294 at local z +.393 alpha .12 with edges .6, #ffc46a, facing
    away from the head
- The next pinch places the piano with `PianoPlacement.FromHit`:
  - it faces the player
  - on a table (a hit more than 0.3 m above the floor) the key tops go at hit + 0.092 with the stand hidden; on the floor
    at hit + 0.72
  - the key fronts go 0.25 m beyond the hit, away from the player
  - then `PlaceAroundPiano()` and a pointer-motion reset
- That pinch is used up: `IsPlacing` stays true until `EndFrame`.
- A pinch before any surface was seen ends placing instead of trapping the player in placing.

**MR lighting** (`MrLighting`):
- Trilight ambient #b1b1b1 / #969696 / #686868.
- One white directional sun, intensity 1.3/pi, at (0.6, 2.4, -1.2), aimed at `ILayoutService.Anchor` (the piano, or the
  one instrument in single mode). Hard shadows, one 1024 map. It is `RenderSettings.sun` (URP's main light).
- The VR ambient and sun are restored on exit.
- Shadow catcher (`MusicRoom/MR/ShadowCatcher`): 2.6 x 1.8 m, alpha .32 x shadow, 2 mm over the real surface under the
  anchor, following the anchor's x/z and heading.
  - The shadow is a 3 x 3 PCF on URP's main-light map. The PCF step is read from the texture: URP 17.3 leaves
    `_MainLightShadowmapSize` stale with soft shadows off, which made the shadow 1/9 strength.
  - The surface is probed downward from just above the piano body's underside, so objects beside it on the desk are
    skipped.

**Hints** (`MrHint`, a TextMeshPro card 0.65 m ahead, above the gaze, with a lazy follow that snaps after 60 degrees):
- placing: "Point at a table or the floor..." (shown for the whole placing; placing ends by itself after 20 s)
- otherwise, for 8 s after entering or placing: the short informational hints of `MrConstants`

**Focus reload**: `OnApplicationFocus(true)` (`MrRuntime`) reloads the scene in MR unless a load is running, and only
when the room may have changed: with a room loaded or none (NoRooms, no Space Setup) the device is asked first
(`AskDeviceOnFocus`, `SceneChangedAsync`: a room added / removed / captured again, or one where there was none), so the
headset off and on in a room without Space Setup reloads nothing (2026-11-11 log: every focus return reloaded an empty
room); no permission, failed or not available reload at once. A piano the player has not placed goes onto the best
table if one appeared.

**Space Setup**: `RunSpaceSetup()` runs the system Space Setup, then reloads. It needs the headset (`Rig.IsXrActive`);
otherwise it does nothing (no load, status unchanged).

**Exit**:
- Placing ends (every rig in the layout shown).
- The MR lights go off (VR ambient and sun back), the hint hides, the world lock goes off (offset back to identity).
- The MRUK scene is cleared. If a load is still running, the clear is deferred to its end, because MRUK refuses to clear
  mid-discovery.
- Passthrough off and shut down (`XrMode.StopPassthrough`); room and candles back; fire and wind back as they were.
- Audio back to VR (room box, snow and wind positions).
- `ResetSurface()`, `MixedRealityEvent{false}`, `ILayoutService.RestoreHomes()`, `ArrangeAroundPlayer(true)`.

**Shutdown inside MR**: `App.Shutdown` runs in reverse Order, so Layout (100) is already gone. MR restores only what
outlives the App: lights, MRUK world lock and scene, passthrough or the clear colour. It calls no other module and
publishes nothing.

## Materials and shaders

Templates (`MrTemplates : IMaterialTemplateProvider`, written by `Tools/unity.sh render-setup` into
`MR/Resources/MaterialTemplates/MR/`):

| Template | Queue | Note |
|---|---|---|
| `MR/ShadowCatcher` | 2999 | |
| `MR/Reticle` | 3000 | depth tested |
| `MR/Panel` | 3008 | |

Shaders (`MR/Shaders`): `MusicRoom/MR/ShadowCatcher` and `MusicRoom/MR/Unlit`.
- They use URP's UniversalForward pass and the stereo-instancing macros.
- They blend with `Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha`, so the eye-buffer alpha is right for
  passthrough.

No MaterialPropertyBlock anywhere. Every MR renderer
has `shadowCastingMode` Off.

## Tests

EditMode, `Tests/EditMode/MR/`:
- `MrLogicTests`, against reference numbers for placing (frozen fixtures): `place()` on 4 table/floor cases,
  floor-relative onTable; the piano on a real table (full push on deep tops, a shorter push on shallow ones); reticle
  meshes, constants, templates. (Its window-snow and portal checks and `MrPortalDrawerTests` went with the window.)
- `RetiredWindowTests` (3): an older save's window settings forgotten once, a clean save left alone, the keys.

PlayMode, `Tests/PlayMode/MR/`. Every boot goes through `TestBoot(simulateHeadset: true)` (`MrTestRig`), which adds:
- the probes: weather, frame phase, instrument tick counters, and a stand recorder in front of `IPianoApi`
- `ContractLayout`: `ILayoutService` exactly as the contract states it (the piano keeps its height), in place of the
  build's Layout. The foundation stub resets every height, which hid MR's own height duties.

The PlayMode suites:
- `MrFindersTests` (7):
  - the verified scene tests (tables in 8 JSON rooms, MeshOffice1's 0.74 m desk; room events; floor and table surfaces;
    the editor's NotAvailable; multi-room)
- `MrModuleTests` (18):
  - enter: room hidden, fire and wind off, snow kept, audio MR mode, no window or snow portal, snow and wind audio
    above the head, MR lights
  - tables: the piano on the best table with the rigs around it and the catcher on the desk; no table, so in front on
    its stand at floor + .72
  - heights: a lifted (+.4) or lowered (-.3) piano gets stand height back on entering; a re-arrange that slides it
    along the desk keeps it there, one that carries it off puts the stand back on the floor
  - placing: `StartPlacing` freezes every instrument (the menu included) and the next pinch lands it on the desk (used
    up in its frame); a pinch at nothing ends placing; rigs Layout hid come back
  - focus reload; exit restores everything
  - the player rig reset and `TrackingSpaceOffset`; single-mode anchor (catcher and sun under the drums); Space Setup
    refused without a headset; a shutdown in MR restores only global state
  - zero GC allocation of the MixedReality tick (steady and placing); leaving and re-entering during a load; no MR
    without simulation off-headset
- `MrWithTheBuildsLayoutTests` (1): enter and exit with the build's own Layout module (the stub here, the real one after
  the merge).
- `MrShotsTests` and `MrDrawBudgetTests` (`[Category("Graphics")]`, 2 + 1):
  - renders to `Builds/Shots/mr/`: seated view, the table reticle, the piano placed and its shadow, the piano's floor
    shadow (MeshBedroom3)
  - the MR draw budget: at most 80 draws with the catcher and hint
- `MrRetiredWindowTests` (1): a save with an old window boots clean, and no window ever comes in a room without one.

### Verification record (2026-10-31, this clone)

| Step | Result |
|---|---|
| `render-setup` | 17 templates |
| `bake` | 14 textures (`Core/*`) |
| `compile` | 15 assemblies, 0 errors, 0 warnings in `Assets/MusicRoom` |
| `test-edit` | 100/100 |
| `test-play` | 46/46 |
| `test-play --gpu` (3 runs) | run 1: 50/51; runs 2 and 3: 51/51. MR draws 9 against the 80 budget; 9336 bright flake pixels in the window close-up; alpha check 77267 pixels inside the window, 0 outside |

Run 1's one failure was the foundation's `FrameOrderTests` SetUp: `ArgumentNullException` at `Audio/Engine/SynthSource.cs:213`.
This is the revised foundation Audio's start-up race (CONTRACT-REQUESTS 5), not MR code; the Audio module's own clone
already fixes it.

The compile check was a full rebuild: the cached MusicRoom DLLs and Bee artifacts were deleted first.

## Known gaps / UNVERIFIED

- **Device only, not run:**
  - real passthrough (`XrMode`), the USE_SCENE dialog, `LoadSceneFromDevice` results other than NotAvailable
  - world lock under the OpenXR loader, including the rig reset before it (CONTRACT-REQUESTS 7)
  - `EnvironmentRaycastManager` (no scene model), Space Setup, focus events from the system menu
  - the hand aim rays (`HandFrame.pointer` from OVRPlugin)
  - `GetCurrentRoom` in multi-room homes, and `GetDimensions` on the shadow map under Vulkan
- **Auto placement and furniture:** it takes TableFinder's best table and does not avoid objects standing on it (the
  recorded desks carry monitors). The player re-places the piano with a pinch.
- **Catcher blending:** its 0.32 is blended in linear space (the design value assumed sRGB-encoded blending). The passthrough
  compositor's own blend space is UNVERIFIED; tune `CatcherOpacity` on the headset.
- **Other modules' shadow casters:** they must turn casting off on everything but the instrument bodies for the MR
  budget (CONTRACT-REQUESTS 6).
- **The hint card** and its texts are written for the headset: short cards instead of one line of flat text.
- **Layout dependencies:**
  - Placing in the app starts only through Layout's Grab step 5, which m-layout reaches through its `StartPlacingHook`.
    The merge wires it (CONTRACT-REQUESTS 2a).
  - m-layout resets the piano's height on every `MixedRealityEvent` (CONTRACT-REQUESTS 2b).
- **Room portals:** the snow in the app is Room's (m-room implements `ISnowPortals`). This clone's Room is the stub,
  so its renders use MR's fallback drawer.

## 10-upgrade (burst.md section 10.1; experience.md E9)

- **Passthrough on demand.** `Enter` starts passthrough (`XrMode.StartPassthrough`) and waits for it (at most
  `PassthroughWaitSeconds` 4 s, the VR room staying up meanwhile); leaving MR shuts it down (`XrMode.StopPassthrough`), so
  the VR room never runs with passthrough enabled (CPU level 4 allowed). `Available` counts `PassthroughStartable`.
  Device: the `[RIG] passthrough ready X s after the start request` line is the extra MR entry delay (UNVERIFIED #27).
- **E9 the piano remembered (`PianoAnchorStore`, headset only).** Placing the piano by pinch, or letting it go after an MR
  grab, saves a local `OVRSpatialAnchor` at its pose (`WhenLocalizedAsync` -> `SaveAnchorAsync`), keeps
  `musicroom.mrPianoAnchor` (Guid "N"), `musicroom.mrPianoOnTable`, resets `musicroom.anchorFailures`, and erases the
  previous anchor. On the next entry `LoadUnboundAnchorsAsync` -> `LocalizeAsync(3 s)` -> `TryGetPose` -> `PoseOk`
  (within 3 m of the head horizontally, up.y >= .98, -0.1 <= y <= 1.6) -> `BindTo`: the piano goes back there (stand
  or table as saved), the others around it, no auto-placement hint; while MR runs the piano follows the anchor when they
  drift more than 2 mm apart (relocalization). Any failure falls back to the table flow; 3 in a row erase the anchor.
  Not done: the manual snowy window's anchor (`musicroom.mrWindowAnchor` / `mrWindowSize`). Tests:
  `EditMode/MR/AnchorPoseTests` (the anchorPose vectors, the limits); the anchor calls need the headset.


## The lead instrument (12-new-instruments/platform.md 9; phase 0)

"The piano" of ARCHITECTURE 12 is now the LEAD: `ILayoutService.Lead` (the My set member in slot Front, else the first
member in carousel order; One alone: Current; with the default set, the piano). `MrModule.Lead` reads it.

- **Auto-arrange**: every `IInstrumentSurface` off its table (`SetOnSurface(false)`: the piano's `ResetSurface`), the
  lead's rig at its home height above the real floor (the piano's key tops 0.72 m: its stand), Layout's arrange, the draw
  guard, then the best table.
- **The table** (`TryGetLeadPose(kind, out pose, out onTable)`): the piano keeps its tested pose (`TryGetPianoPose`,
  `TableFinder.PianoPoseOnTable`). Another lead whose descriptor has `Mr.OnTable` goes on the best table that holds its
  footprint (TableFinder with its width and depth), its play centre `PushBack` in from the near edge as far as the top
  allows, its origin `Mr.SurfaceLift` above the top, facing the player, and its `IInstrumentSurface` (the first
  registered of its Kind) hides its stand. A floor-only lead (`Mr.OnTable` false: the drums, the harp, the marimba, the
  hand drums) is never put on a table: its pose is its seat home on the real floor in front of the player.
- **Placing** (`StartPlacing`): the other rigs in the scene hide (a rig out of the scene, on display, is never touched,
  so nothing displayed shows in passthrough); the reticle shows the lead's footprint (the piano's body outline;
  another lead's from its descriptor); the pinch puts the lead `PushBack` beyond the point (`PianoPlacement.FromHit` with
  its surface lift and stand height: the piano's are TableLift and StandHeight, so its pose is unchanged); a floor-only
  lead's reticle shows only on the floor. Then `ILayoutService.PlaceAroundLead` (the default body is today's
  PlaceAroundPiano).
- **Re-arranges** (`ArrangedEvent`): a lead carried off its table gets its stand back on the floor, as the piano did; a
  lead that changed takes the old one off its table.
- **Light and catcher** follow the lead's rig; the catcher's downward probe starts 2 cm over the underside of a table-top
  lead's body (origin - SurfaceLift: the piano's key tops - tableLift), 5 cm over a floor rig's origin.
- **Hints** name the lead (`MrConstants`: the piano's words with "the singing bowls"; floor wordings for a floor lead).
- **E9** (`PianoAnchorStore`) stays the piano's: restored, followed and saved only while the piano is the lead; another
  lead is placed fresh each visit. `AnchorInstrument` is the only instrument MR names (RegistryLintTests' "MR's piano
  anchor").
- **Draw guard** (`DrawEstimate`, `GuardDraws`): while the at-seat descriptors' `DrawsAtSeat` + 37 exceed 80, the last
  member of My set is put away (`ILayoutService.PutAway`), logged "[MR] Put away: no room here: <id>", and
  `InstrumentSignalEvent { Kind, Signal = "mr.noroom" }` is published for the menu's note. The default set is 73.
- **Not MR's**: hiding the displayed instruments on entry and the swivel's rear-slot cost (+100) are Layout's.

Tests: `Tests/PlayMode/MR/MrLeadTests` (a stand-in Layout with the platform's members: the singing bowls on the desk of
MeshOffice1, the marimba stub on the floor and placed on the floor only, a set without the piano around the bowls stub,
the draw guard; the default set with the build's real Layout keeps the piano as the lead at 73 draws). The MR test rig's
`PianoSurfaceProbe` also stands in as the piano's `IInstrumentSurface` (MR reaches the stand through it now).
UNVERIFIED (device item 56): MR with a non-piano lead on the headset.

## Real walls for the wall paint (2026-11-11, the playback show)

`MrPaintWalls` (MR/MrPaintWalls.cs) implements Core's `IRealWalls`, registered in `MrModule.Init`: `Refresh()` reads the
current MRUK room's `WallAnchors` (plane rects; the anchor's +Z faces into the room) and, as boxes to keep clear, every
WINDOW_FRAME / DOOR_FRAME / WALL_ART plane (12 cm deep) and every other anchor's volume (walls, floor, ceiling and the
global mesh excepted); `Version` bumps when they change. With no scene model, `DepthWall(ray)` is `MrModule.PaintDepthWall`:
the snowy window's depth check (`EnvironmentRaycastManager.PlaceBox` with `DepthWallBox`, upright normals only), only while
MR is active and has no MRUK room; the Room's wall paint asks it once a frame while a playback paints, which keeps the
manager enabled (`_envUsed`). Room/Decor/WallPaint.cs paints on these walls (Room README "The notes paint the walls").
