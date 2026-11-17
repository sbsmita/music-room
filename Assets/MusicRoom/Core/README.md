# MusicRoom.Core (foundation, implemented)

References `MusicRoom.Hands` and `Unity.InputSystem` only. Every other module references Core (and Hands, Audio).

- `App` / `AppRoot` / `BootOptions`: the application object, module discovery (`ModuleDiscovery`: every `MusicRoom.*`
  assembly except `MusicRoom.Tests.*`; tests inject probes with `BootOptions.ExtraModules`), the frame orchestration
  (`FramePhase` order, spec 05 section 1.1, asserted by `Tests/PlayMode/Core/FrameOrderTests`), service registry
  (`Register<T>`, `Get<T>`, `Require<T>`), `IsPresenting` (the XR-presenting gate), the test hooks
  (`BootOptions.Settings`, `DataPath`, `SimulateHeadset`, `ManualFrames` + `App.Step(dt)`, `AudioClock`; PlayMode
  tests boot through `Tests/PlayMode/Core/TestBoot`).
- `IAppModule` / `AppModule`: what every module implements (`Order`, `Init(app)`, `Tick(phase, dt)`, `Shutdown`).
- `Clock` (+ injectable audio clock), `EventBus` (per-handler isolation) + `Events.cs`, `AppSettings` over an
  `ISettingsStore` (`PlayerPrefsSettingsStore`, `MemorySettingsStore`). Keys: `musicroom.tourDone`, `.layoutMode`,
  `.current`, `.locked`, `.nextTakeNumber`. The old `musicroom.classicSound` (`KeyClassicSoundOld`) is deleted once at
  start-up (`AudioEngine.Init`): the piano, drums, harp and room always play the classic sound since 2026-11-10.
- Input layer: `Pointer`, `HandState`, `IPointerOwner`, `IHandTool`, `PointerInput` (spec 05 section 1).
- Contracts: `IInstrument`, `IPianoApi`, `IDrumsApi`, `IHarpApi`, `Rig`, `IRigRegistry`, `ILayoutService`,
  `IGrabService`, `IPlacementService`, `GrabConstants`, `IMenuPanel`, `MenuConstants`, `IRecorder`, `TakeEvent`,
  `ITutorial`, lesson JSON classes, `ITakeLibrary`, `IGuide`, `IRoom`, `IAmbienceSwitches`, `ISnowWeather`,
  `IMixedReality`, `IMaterialTemplateProvider`, `ITextureBakeProvider`.
- Contact visuals (10-upgrade/experience.md section 2): `Contracts/Contact.cs` (`IContactSurface.QueryContact(world,
  above, below, out ContactHit)`, `ContactHit {Point, Normal, Tangent, ClipHalf, Height, Use}`, `ContactUse` Shadow / Clamp
  / Both; Piano, Drums and Menu implement it), `Render/ContactVisuals` (`App.Contacts`, App.Frame step 8a: after
  `Tick(StickHover)`, before `HoverGlow.Update`), `Render/ContactShadowPool` + `Render/ContactShadow.shader`
  (`MusicRoom/ContactShadow`), `Render/CoreMaterialTemplates` (templates `ContactShadow`, `HandSkin`, `HandPassthrough`
  under `Assets/MusicRoom/Resources/MaterialTemplates`, written by `render-setup`). See "Contact visuals" below.
- Rendering helpers: `HoverGlowPool` (one dynamic 16-quad mesh; `Dim(i, k)` draws a glow under a contact shadow at half
  strength without touching its spec), `MaterialLibrary`, `GeneratedAssets`, `TextureUtil`,
  `ProcTex` (Hash / VNoise / Fbm, wood, drum head, cymbal, grille, soft dot; spec 07 section 7) + `SharedTextures` /
  `CoreTextureProvider` (the shared `Core/<name>` textures every module uses).
- Utilities: `Coords` (right-handed -z-forward source frame to Unity), `DesktopInput`, `Mulberry32`, `Layers` (PlaySurface 29, Hidden 30, PanelUI 31).
- `link.xml` preserves every MusicRoom assembly for IL2CPP (reflection discovery).

## Contact visuals (10-upgrade/experience.md E1, E3, E6 switch)

Visual only: the play logic (pointers, strikes, pokes) keeps reading the raw joints, so no note changes.

- **Step 8a** (`ContactVisuals.Update`, profiler marker `[Core] contact`): for each connected, non-mouse hand and each
  finger the tip joint is queried (above 0.06 m, below 0.04 m) against every `IContactSurface`; the smallest `Height >=
  -below` wins. The surface list is `App.GetAll<IContactSurface>()`, cached on the first frame and on
  `LayoutChangedEvent` (never per frame). Allocation-free per frame (EditMode and PlayMode `Is.Not.AllocatingGCMemory`).
- **E1 clamp** (`Use` has Clamp): `want = ContactMath.PenetrationLift(rc - Height)` (rc = the tip joint radius x
  HandScale; full up to 25 mm, fading to 0 at 40 mm), instant attack, 25 ms release keeping the last normal
  (`ContactMath.SmoothLift`), handed to `HandInput.Visual(side).SetContact(finger, lift, normal)` (and to the editor's
  debug hands). `Lift(side, finger)`, `LiftNormal`, `ClampEnabled` (off switch), `ClampActive` (= enabled and not MR).
- **E3 shadows** (`Use` has Shadow): per finger two blobs (the tip and distal-joint spheres of the DRAWN finger, i.e.
  after the clamp's turn, against the plane the tip query returned) plus the drawn stick tips
  (`IDrumsApi.StickShadowSpheres(Span<Vector4>)`, a default-0 interface member, implemented by Drums); `A` 0.55 in VR,
  0.35 in MR; `MaxDark` 0.6, skipped above 6 cm, quad half-size max(2.5 h_eff, 1.5 r) <= 8 cm, h_eff >= 0.8 r, clipped
  along `Tangent` to `ClipHalf` on a pressed key. `ContactShadowPool`: 32 blobs (128 vertices, `SetVertexBufferData` from
  a `NativeArray`, one sub-mesh range), one draw, the renderer off with no blob, fixed 10 m bounds around the head, no
  MaterialPropertyBlock. Shader `MusicRoom/ContactShadow`: `Blend DstColor Zero` (output alpha 1: MR keys never turn
  see-through), `ZWrite Off`, `ZTest LEqual`, `Offset -1,-1`, `discard` past the clip, darkness
  `min(maxDark, A r^2 h / (h^2 + rho^2)^1.5)`. Queue **2991** (after the key labels at 2990, so a shadow darkens the
  sticker under a finger; the spec's 2980 left every sticker bright inside the shadow: see
  `Builds/Shots/exp-hands/shadow-on-label-close*.png`). The menu is `Use` Clamp (its face, 2999, would draw over a
  shadow, and the blob only halved its hover glow). A clip runs along the tangent about `ContactHit.ClipOrigin` (a
  pressed key's centre line, uv2 of the blob quad), not about the blob centre. In MR the finger blobs are cast from the
  cut-out's delayed pose (`MetaHandVisual.DelayedJointWorld`), so they sit under the real hand the passthrough shows.
  `ShadowsEnabled` switch, `ShadowCount`. `App.Frame` calls `Contacts.Update` inside a try/catch like every module call
  (it reaches the modules' contact surfaces), and the `[Core] contact` marker ends in a finally.
- **Hover glow**: every pending glow a blob quad covers is drawn at `HoverGlowGainWithShadow` 0.5 (`HoverGlowPool.Dim`),
  so the realistic cue leads; the instrument's `HoverGlowSpec` (gain .45 on the piano) is unchanged.
- **MR** (`MixedRealityEvent`): `SetMixedReality(active)` turns the clamp off (the passthrough cut-out must match the real
  hand), switches shadows to A 0.35, and calls `HandInput.SetLook(PassthroughCutout)` (back to `Skin` on exit): material
  and look together.
- `AppRoot` loads `MaterialTemplates/HandSkin` and `MaterialTemplates/HandPassthrough` into `HandInput.skinMaterial` /
  `cutoutMaterial` when the scene did not set them (the scene keeps `handMaterial` = HandGhost, the Ghost look).
- Tests: EditMode `Tests/EditMode/Core/ContactShadowTests` (the contactShadow vectors, pool, per-frame logic on scripted
  frames, MR / off switches, stick shadows, glow dimming, 0 B with 10 fingers, templates and shader render state);
  PlayMode `Tests/PlayMode/Core/ContactVisualsPlayTests` (a finger 15 mm into C4 is lifted > 5 mm while the piano fires
  exactly one pianoOn, with and without the clamp; step 8a sits between StickHover and HoverGlow and dims the piano's
  glow; a pressed key clips its shadow; 0 B with ten fingers over the keys) and `ContactShotsTests` (Graphics: the
  pictures in `Builds/Shots/exp-hands/` and the draw count: hands away 20, hands over the keys without
  shadows 21, with shadows 22: one draw).
- Device (ARCH 19): `[Core] contact` <= 0.05 ms; the shadow's strength at the real lamp light; GPU delta < 0.1 ms.

## Fast Motion Mode (10-upgrade/experience.md E4)

- **E4 (experience.md section 6):** `FastMotionPolicy` (the reference `fmmFrame`, doubles) and `FastMotionDriver`
  (`App.FastMotion`, ticked in step 18 after `Input.EndFrame`): drumming intent = single mode on the drums, a stick held,
  or a drum / choke / hat `RecorderEvent` (not playback) in the last 4 s; on at once, off after 5 s without intent (at most
  one off request a second); while on (after 1 s), a 3 hand-second block with more than 25 % low-confidence tracking blocks
  Fast Motion Mode for the session (`[Hands] FMM blocked: low confidence 0.xx (dim room?)`) and publishes
  `HandsLowLightEvent` once (Sunny then shows "More light helps your hands", text only; experience.md 14 item 4). The hint
  reaches `MetaHandSource` only while `Rig.IsXrActive`.

- Since the first headset test (u-arrange): `IArrangeMode` / `ArrangeModeEvent` / `ArrangeConstants` ("Move instruments";
  `App.TickInstruments` ticks only the menu while arranging), `HandState.GrabPinch*` / `MetaPinching` / `PinchStrength`
  (the pinch for actions: since the merge with the upgrade also read by the seat turn, the two-hand summon, the stick
  pick-up and hold, the hi-hat stand and MR placing; the palm-menu summon and the page-turn sweep keep `Pinching`),
  `PointerInput.SystemGestureLike`, `MenuScreen.Start / Arrange`, `IMenuPanel.StartPending`, `BootOptions.StartPanel / Quit`,
  `App.Quit()`, the arrangement keys in `AppSettings`, `GrabConstants.GrabStill` .04. Details: Layout/ARRANGE-NOTES.md.

## The many-instrument platform (12-new-instruments/platform.md, phase 0, 2026-11-01)

- **The registry** (`Contracts/Registry.cs`, `Registry/InstrumentRegistry.cs`, `Registry/BuiltIns.cs`): each instrument module
  ships one parameterless `IInstrumentDefinition`; `App` builds `App.Registry` (also `Get<IInstrumentRegistry>()`) before
  any module's Init; duplicates are "[Core] registry:" errors and dropped; `Register<Rig>` marks an instrument loaded;
  `BootOptions.InstrumentFilter`. The id block: `InstrumentKind` / `RigId` 5-9 (Handpan, HandDrums, Marimba, Bowls; 8 retired
  on 2026-11-10 and never reused, `RetiredInstruments` migrates an old save), `InstrumentIds` (persisted ids). `Core/Registry` is the one Core folder that may name instruments
  (`RegistryLintTests`).
- **Contracts:** `IStowable`, `IInstrumentPlayback`, `IGuideAnchors`, `IHandToolProvider`, `IInstrumentSurface`,
  `IPointerClaimant`, `PlaySurface.Instrument`, `IPianoApi.MusicRestPose/Local/Ledge`; `ILayoutService` (My set, Bring,
  PutAway, PreviewJoin, PlaceOf, Facing, IsMoving, Lead, ...: default bodies); `TakeEvent.lane`, `LessonTarget` fields,
  `LessonOptions`, `ILessonTargets`, `ILessonAccompany`, `ITutorial.CurrentEvent / CountNoun`, `IRecorder.Add(ev, at)`;
  `TipSpec` and friends, `DemoKind` (moved from Guide), `IGuideTipSource`, `InstrumentSignalEvent`; `TryGetLeadPose`;
  `IRoomFurniture`; `IAudioPresence`; events `InstrumentMoveEvent`, `FocusEvent`, `InstrumentOptionEvent`, `MenuRowEvent`.
- **Input:** `Pointer.Tool`, `Slot`, `ClaimedBy`, `Pointer.CreateTool`; `HandState.LastInstrument`;
  `PointerInput.PointersFor(ToolMask)`, `MaxSlots`; `Input/PointerClaims.cs` (frame step 3b); `FastMotionPolicy` driven by
  the registry's `FastMotion` trait.
- **Recording:** `Recording/ControlEncoder.cs` (exact to `control-decimator.mjs`), `PlayStamp`, `LaneArbiter`,
  `PlayingTake` (the recording the Menu last started from Recordings: the Room's paint gallery knows which take a playback is);
  `PlaybackLanes`. **Util:** `HandToolMath`. **Render:** `PortraitStudio` (the menu's runtime portraits).
- `Registry/PlaceholderInstrument.cs`: the phase-0 stubs' body (neutral wood blocks at the footprint, a tap-to-knock,
  every platform interface in minimal form).
- Tests: `Tests/EditMode/Core/RegistryTests`, `RegistryLintTests`, `ControlStreamTests`; `Tests/PlayMode/Core/
  InstrumentConformanceTests` (the suite over the registry, `IInstrumentFixture` per instrument), `PointerClaimsTests`,
  `DisplayedSilenceTests`.
