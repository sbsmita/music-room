# MusicRoom.Gaze: Look and pinch (2026-11-13)

Look at a menu row or an instrument on display: after a short dwell it lights. Pinch (either hand, held away from the
instruments and the menu) and it is chosen: the row fires exactly as a poke fires it, the instrument comes to the seat
exactly as the menu's tile or the ray summon brings it. Quest 3S has no eye tracking: the gaze is the head (centre eye).

The only assembly of the app that references **Meta's Interaction SDK** (`com.meta.xr.sdk.interaction` 207.0.1, "Meta XR
Interaction SDK Essentials"; asmdef reference `Oculus.Interaction`). Nothing else in the app changed its input: the hands,
their visuals, pokes, grabs and Move instruments are the app's own pipeline as before.

## Interaction SDK components used

| SDK component | Where | What for |
|---|---|---|
| `Oculus.Interaction.Input.DataSource<HandDataAsset>` | `MusicRoomHandDataSource.cs` | an SDK hand data source fed by the app's own hand frames (HandInput / PointerInput: on Quest the same `OVRPlugin.GetHandState` data every instrument plays from; in tests the FakeHandSource). Root = wrist, 26 OpenXR joints wrist-local, radii, confidence, pointer pose, per-finger runtime pinch; the index pinch = the app's grab pinch |
| `Oculus.Interaction.Input.Hand` x2 | `GazeModule.Build` | the SDK hand over that source (pass-through, Manual update mode) |
| `Oculus.Interaction.IndexPinchSelector` x2 | `GazeModule.Build` | the SDK's index-pinch selector on each Hand |
| `Oculus.Interaction.ISelector` | `GazePinchSelector.cs` | either hand's IndexPinchSelector, accepted only from a free hand on a gazed target; its release is the unselect |
| `Oculus.Interaction.RayInteractor` | `HeadGazeInteractor.cs` (subclass) | the head gaze ray from the centre eye; the SDK's interactor state machine (Normal / Hover / Select / Disabled), driven by the module (`IsRootDriver = false`, `Drive()` once per app frame). Its candidate search adds the field-of-view rule, the dwell and the occluders and is allocation-free (the SDK's own allocates a `RayCandidateProperties` a frame) |
| `Oculus.Interaction.RayInteractable` x11 | `GazeModule.Build` | the menu panel (slot 0) and one per instrument kind; tiebreaker score puts the menu in front |
| `Oculus.Interaction.Surfaces.ISurface` | `GazeSurface.cs`, `MenuGazeSurface.cs`, `InstrumentGazeSurface.cs` | what the ray hits: the menu face's rows (through Core's `IGazeMenu`, the face's own MeshCollider), an instrument's display box. No collider is added to the scene |
| `Oculus.Interaction.IActiveState` | `GazeActiveState.cs` | the gate: off disables the interactor |
| `Oculus.Interaction.PointerEvent` | `GazeModule.OnPointer` | each RayInteractable's `WhenPointerEventRaised`: the Unselect of a valid tap is the choice |

## The rules (GazeRules.cs, EditMode-tested)

- **Dwell** 0.35 s on the same target with a steady head (smoothed angular speed under 75 deg/s); a look away under 0.15 s
  keeps it. A sweep across targets lights none.
- **Field of view**: a target counts only while its centre (the row's, the instrument box's) is within 22 deg of the view
  centre; the highlight never pops in the periphery.
- **Occluders**: an instrument at the seat hides what stands behind it (its box ends the ray); the menu floats in front.
- **A free hand**: tracked, not the mouse, holding nothing, not `ILayoutService.HandBusy` (played in the last second, on a
  play surface or the menu, the ray summon's, Move instruments'), neither the index nor the thumb tip claimed this frame
  (`PointerClaims`: an instrument's zone, the menu's grab zone, Sunny), not the system gesture.
- **A tap**: the choice fires on the release, within 0.6 s, the pinch point moved under 6 cm, the hand free throughout and
  the head still on the same target (menu: the same row of the same layout). Longer or moving pinches are the other
  gestures' (seat swivel, two-hand reset, ray summon hold), and the pinch is never consumed: everything else sees it as before.
- **Gate**: in the headset only (`App.IsPresenting`), the setting on, not while Move instruments is on, MR placing, a rig
  held, or Sunny's ghost hand showing a pinch (`IGuide.TeachingPinch`).

## Targets

- **Menu rows** (`IGazeMenu`, implemented in `Menu/MenuModule.Gaze.cs`): the shown panel's row under the head ray; the
  highlight is the poke's own (`MenuController.GazeHover`, the face shader's row highlight; a fingertip's hover wins); the
  choice goes through `MenuController.Fire` inside the menu's tick (`GazeFire`): debounce, `MenuRowEvent`, the action.
- **Instruments on display** (VR room; mixed reality shows none): `ILayoutService.TryGetDisplayPoint`, the rig's visible
  meshes' box; the highlight is one soft warm glow through its centre facing the eyes, a halo round it (`HoverGlowPool`, the hover amber, steady,
  gain .7, 1.6 x its size, 0.15 s fades); the choice is `ILayoutService.Bring(kind, BringReason.Ray)`.

## Setting, Sunny

- "Look and pinch" On / Off on the menu's Sunny page (`RowKind.LookPinch`), `AppSettings.LookAndPinch`
  (`musicroom.lookPinch`, default on), `ILookAndPinch.On`.
- Sunny: `tip_look_pinch` ("Look at something and pinch to choose it.") once the first highlight has shown 0.4 s (asked
  every 3 s while Sunny is busy, once ever; not asked after the player already chose something). TourFeatures `gaze.pinch`.

## Tests

EditMode `Tests/EditMode/Gaze/GazeRulesTests` (dwell, grace, sweep, field of view, head speed, gate, free hand, tap, keys,
0 B). PlayMode `Tests/PlayMode/Gaze/GazePlayTests` (every real module, Sunny faked): a row lights after the dwell and a
pinch fires it like a poke; either hand; 30 deg off never lights; held / dragging / look-away pinches choose nothing; a
pinch in the menu's zone is the menu's; an instrument on display lights and comes to the seat; not while playing or on an
instrument's body (Move's body pinch still reaches Sunny); Move instruments, Sunny's pinch steps and the setting (persisted)
stand it down; 0 B per frame. `GazePerfTests` ([PERF]), `GazeShotsTests` (Graphics: `Builds/Shots/gaze`).
