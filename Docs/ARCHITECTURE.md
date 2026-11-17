# VR Music Room: ARCHITECTURE (single source of truth for stage 3)

> **2026-11-11: the upgraded piano, drum, harp and room sounds were deleted** ("I want only classic sound"): `Audio/Sound/Piano`, `Drums`, `Harp`, `Room` (with `MusicRoomDrumKit.bytes`), `Engine/RoomGroupAssigner.cs`, the listener stage (`ListenerGraph`, `AudioGraphHost.RenderListener` / `ListenerLoad` / `ListenerLatency`, `MasterBus`'s crossfade and delay line), the source taps, `SynthOp.FadeAll`, `AudioEngine.ClassicReady`, the new piano's half-pedal wiring, the physical harp's path, the modelled kit's branches, the looper's sound-path re-send, `IconKind.Sound`, `AudioSourceRow.RoomGroup` and `AudioSpec.ErOrigin`, `Tools/SoundLab/piano` and `harp`, and the tests that only exercised them. `UnitId` keeps its values. Mentions below are the historical design record; notes in `music-room-vr-wip/unity-stage-tune/TUNE-CLASSIC-CLEANUP.md`.
>
> **2026-11-10: the theremin was removed from the app** at the player's request (its folders, unit, source, lessons, Sunny lines and tests; `theremin.md` and its critique deleted). The id block value 8 stays retired and is never reused; `Assets/MusicRoom/Core/Registry/RetiredInstruments.cs` migrates an old save. Mentions of it below are the historical design record.

Project: this Unity project (Unity 6000.3.27f1, URP 17.3, Meta XR Core SDK 207, MRUK 207, Meta XR Audio 85,
OpenXR 1.16.1). Research specs:
`Docs/` (`01`..`09` and their source sketches). This document resolves every open
question in those specs; where it disagrees with a spec, this document wins.

A stage-3 agent implements ONE module folder under `Assets/MusicRoom/<Module>/` and never edits a file outside it. Everything
a module needs from the rest of the app is a Core contract (interface, struct or event) listed here with its exact signature.
The compiled skeleton (every contract implemented by a stub, every module discovered, hands visible, audio thread ticking)
is the starting point; `Tools/unity.sh compile|test-edit|test-play` must stay green after every change.

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions already made (do not reopen)

| Area | Decision |
|---|---|
| Rig | Core SDK `OVRCameraRig.prefab` instance named `PlayerRig` in `Assets/Scenes/Main.unity` + `OVRManager` (FloorLevel, passthrough initialised at start, dynamic resolution 0.7–1.6, no start-up permission prompts) + `OVRPassthroughLayer` hidden + `DesktopRigFallback` (disabled). Loader: Unity OpenXR with Meta's feature group (`MetaQuestFeature`, `MetaXRFeature`, `OculusTouchControllerProfile`, `HandInteractionProfile`, `MetaXRSubsampledLayout`) plus Unity's `FoveatedRenderingFeature` (the SRP Foveation API); Meta's Legacy foveation features (`MetaXRFoveationFeature`, `MetaXREyeTrackedFoveationFeature`) off; SpaceWarp off (both `MetaXRSpaceWarp` and Unity's `SpaceWarpFeature`). `com.unity.xr.oculus`, `com.meta.xr.sdk.interaction(.ovr)` removed. `com.unity.xr.hands` installed but its OpenXR features OFF; `XRHandSubsystem` is not used. |
| Hands | `OVRPlugin.GetHandState` path (`MetaHandSource`), `MouseHandSource` (editor), `FakeHandSource` (tests), `MetaHandVisual` (runtime mesh). Pinch starts at a 15 mm gap, ends at 25 mm; grasp 0.105/0.13 m; velocity over distinct tracker samples. No Interaction SDK. |
| Render | URP 17 Forward, Linear, MSAA 4, HDR off, no post, no depth/opaque texture, SRP foveation at level 1.0 (= Quest "High") through `XRDisplaySubsystem.foveatedRenderingLevel` (the SRP Foveation API has no dynamic mode; Meta's dynamic foveation belongs to its Legacy API, which is off), single-pass instanced. Materials ONLY from template `.mat` assets under `Resources/MaterialTemplates` (one per shader + keyword set); never a `MaterialPropertyBlock` on URP Lit/Unlit renderers (section 14). TextMeshPro (LiberationSans SDF) for text. Custom HLSL for snow/fire/frost/glow; ParticleSystems for embers/smoke. No tone-mapping shader in v1. No realtime shadows in VR. No pendant lamp. |
| Audio | Spec 04 architecture: 18 `SynthSource`s (17 spatial sources + `DrumRoom` at the kit centre), Meta XR Audio spatializer with `spatializePostEffects`, Logarithmic rolloff (min 0.6, max 30) with AudioManager Rolloff Scale 1.1, Meta shoebox room reverb replacing the convolution reverbs, `MasterBus` on the listener = the spec 06 §1.5 compressor exactly (static curve, makeup +2.137 dB, adaptive release) with NO 6 ms look-ahead, DSP buffer 256 (`AudioModule.PreferredDspBuffer` is the one device knob to try 128). Recipes from spec 06 (piano synth, live drums, OLD harp). Mixed reality lowers reverb sends by 6 dB. Randomness is per session; every DSP unit is seedable for tests. |
| Quest | `targetDeviceTypes` Quest3 + Quest3S, HandsOnly, hand-tracking frequency HIGH, passthrough Supported, scene Supported, anchors Enabled, boundary visibility Supported, 72 Hz default (`RigBootstrap.PreferredRefreshHz`, one constant to try 90), foveation High (SRP level 1.0; "dynamic" cannot be expressed with the SRP Foveation API and is dropped), no `android.permission.INTERNET`, `activeInputHandler` = Both. Project configured ONLY by `QuestSetup.ConfigureAll` (never Meta's FixAll). |
| World | Placement and layout exactly as spec 05 §7-8, with the fitSeat fix (dry-run with clearEye before computing the shift); floor rotation bug fixed (full 6.4 × 5.2 m); walls/bricks with world-scale UVs; lighting = moon + 4 point lights + Trilight ambient + one reflection probe; textures baked in the editor (`TextureBaker`) with runtime generation as fallback. |
| UX | Sunny's tour runs automatically on the FIRST launch only (`AppSettings.TourDone`), later from the "Show me around" row; layout mode, current instrument and lock persist; home menu gets "Play in my room"/"Music room" and (when no Space Setup) "Set up my room"; menu pokes with the index fingertip only; takes under `Application.persistentDataPath/takes`; 22 voice lines from `Guide/Resources/Voice/*.wav` robotised once at load. Mixed reality: passthrough underlay, MRUK loaded on entering, snow portals on WINDOW_FRAME anchors, piano on the best table (`TableFinder`) else in front of the player on its stand, manual snowy window on a wall as fallback, world lock only in MR. |
| Desktop | `MouseHandSource` + the desktop paths (click/drag on keys, drums, strings; computer-keyboard piano; M menu; Shift+H Sunny); `DesktopRigFallback` camera (0, 1.2, −0.1) looking at (0, 0.95, 0.45), fov 55. PlayMode tests and editor play work without a headset. **One desktop model:** a left-button press on a module's click target (a collider on `Layers.PlaySurface`, 29) belongs to that module's desktop path with fixed velocities (piano click .7, drag glissando .6, drum and harp clicks/drags, deck, switches, menu face); the nearest `PlaySurface` hit wins (`Physics.Raycast(ray, out hit, far, Layers.PlaySurfaceMask)`: each module acts only when the hit collider is its own); a press anywhere else looks around (`DesktopRigFallback.dragBlockMask`); Shift/Alt + drag moves/turns a rig (Layout). The mouse hand is a visual and gesture stand-in (LMB poke shape, RMB pinch, MMB fist, Tab switches the hand, `-`/`=` hover distance): its pointers NEVER strike, poke or pluck (`HandState.IsMouse`, `Pointer.FromMouse`), so a click never fires twice. Keys: piano row A W S E D F T G Y H U J K O L P ; ', Z/X octave, M menu, Shift+H Sunny; nothing else. Desktop paths run while `!App.IsPresenting`. |
| Build | Everything procedural at runtime; the only scene is `Main.unity` (rig prefab instance + `App` root). No brand names. No third-party assets beyond Meta/Unity packages. No network. |

**Changed by the 10-upgrade (2026-11-01, section 21; the user asked for the best sound and experience Unity allows):**
Rig: passthrough is NOT initialised at start and the scene has no `OVRPassthroughLayer`; MR entry starts it
(`XrMode.StartPassthrough`) and exit shuts it down. Render: SRP foveation starts at 0.5 (governor rung 0). Audio: the
piano, drums, harp and room play the classic sound only (today's synths, Meta's shoebox reverb, the drum room and the
spec 06 master; the user's choice after the headset test, 2026-11-10: the "Classic sound" switch and its menu row are
gone, and the upgrade's four sound units, modal piano, modelled drum kit, physical harp and our own room, are no longer
created at runtime). The Burst audio graph carries the unit-only instruments. Quest: hand-tracking frequency LOW, with Fast
Motion Mode requested only while drumming; 90 Hz by default with a four-rung quality governor (90/72 Hz x foveation
0.5/1.0).

---------------------------------------------------------------------------------------------------------------------------

## 1. Pipeline, tooling, verification gates

`Tools/unity.sh <cmd>` (zsh; env `UNITY`, `PROJ`, `LOGS` (default `<proj>/Logs/ci`), `ADB`). Every call is
`-batchmode -nographics -buildTarget Android -projectPath $PROJ -logFile $LOGS/<cmd>.log`. **Only one Unity instance may open a
project folder** (`Temp/UnityLockfile`); a second exits 1 within a second. Exit 134 after the success marker is treated as 0
(known shutdown crash).

| Command | Entry point | Pass criterion |
|---|---|---|
| `compile` | `-quit` | exit 0; the script prints `error CS` lines and `[warn]` lines for `Assets/MusicRoom/**` — both must be empty |
| `configure` | `MusicRoom.EditorTools.QuestSetup.ConfigureAll` (+ `AudioProjectSetup.Apply`) | exit 0, `[SETUP]`/`[AUDIO-SETUP]` lines, `[OPENXR-VALIDATION] ... 0 error(s)` |
| `tmp-import` | `TextSetup.ImportTmpEssentialsAndExit` (no `-quit`) | marker `importPackageCompleted` or `TMP already imported` |
| `render-setup` | `RenderSetup.SetupAll` (URP asset + quality levels + SRP foveation, TMP report, `MaterialTemplates.CreateAll`) | exit 0, `[RENDER]`/`[MATERIALS]` lines |
| `scene` | `SceneBuilder.BuildMain` | writes `Assets/Scenes/Main.unity`, sets the build scene list |
| `bake` | `TextureBaker.BakeAll` (providers discovered by reflection; names `<Module>/<name>`, a duplicate across providers fails the run; a spec marked `NeedsGpu` is refused under `-nographics` instead of writing a blank PNG: run `NOGFX=0 Tools/unity.sh bake`; also sets the per-machine ASTC subtarget) | `[BAKE] N texture(s) baked`, no `[BAKE-ERROR]` |
| `test-edit` / `test-play [--gpu]` | Unity Test Framework 1.6 | exit 0 AND XML `total > 0 && failed == 0 && inconclusive == 0`. PlayMode runs with `-testCategory '!Graphics;!Performance'` (`--gpu`: `'!Performance'`, with the GPU). Use `Assert.Ignore` (never `Assume`) when a GPU is missing. |
| `test-perf` | PlayMode category `Performance` (`Tests/PlayMode/Audio/AudioLoadTests`: metal-voice cost, the E12 busy groove with the periodic-wave tables ready) | exit 0, XML green; prints the `[PERF]` lines recorded in section 17 |
| `meta-report` | `QuestSetup.MetaReport`, run WITHOUT `-quit`: opens Main.unity, waits (≤ 90 s of editor updates) until Meta's `PackageList.PackageManagerListAvailable`, then `OVRProjectSetupCLI.GenerateProjectSetupReport` and exits itself (`[META-REPORT] done exit=N`); before the list is ready every rule that asks `PackageList.IsPackageInstalled` (the whole `Packages` group, several Required) silently drops out of the report | the report contains the `Packages` group AND no `Required` task is outstanding |
| `report` | `QuestSetup.Report` | prints `[REPORT] key = value` read-backs |
| `build [apk]` | `BuildPlayer.BuildApk -outputPath ...` (no `-quit`; bakes every missing texture first and refuses to build with `[BUILD-ERROR] missing baked texture <name>`) | `[BUILD] result=Succeeded apkBytes=...` and the APK file exists |
| `install`, `log` | adb | device only |
| `all` | compile → configure → tmp-import → render-setup → scene → test-edit → test-play → meta-report | all green |

Stage-3 rule: run `compile`, `test-edit` and `test-play` before handing a module back; never run `configure`/`render-setup`
again unless your module adds material templates or textures (`render-setup` / `bake` re-create them idempotently).

Generated outputs (never hand-edited; any of them may be deleted and rebuilt):
- `Assets/MusicRoom/Resources/MaterialTemplates/*.mat` (foundation templates) and `<Module>/Resources/MaterialTemplates/**`
  (a module's own templates, written INSIDE the module folder by `render-setup`, so they travel with the folder merge);
- `Assets/MusicRoom/Generated/Resources/Generated/<Module>/<name>.png` (bake output, git-ignored: it does NOT travel with a
  merge; `build` bakes whatever is missing);
- `Assets/MusicRoom/Settings/QuestURP*.asset`, `Assets/Scenes/Main.unity`, `Assets/Plugins/Android/AndroidManifest.xml`,
  `Assets/TextMesh Pro/**`, `ProjectSettings/*`.
Integrator order after every merge and before every `build`: `compile → render-setup → bake → scene → test-edit → test-play`
(`configure` and `tmp-import` only when the project settings or TMP are missing; `NOGFX=0 Tools/unity.sh bake` when a
provider marks a spec `NeedsGpu`). `build` bakes any missing texture itself and refuses to build without it, so a device
never generates textures at runtime; should a player ever do so (`GeneratedAssets` fallback), it logs
`[Render] runtime texture <name>` once and compresses the texture and drops its CPU copy.

Log conventions: prefix every `Debug.Log` with `[Module]` (e.g. `[Piano]`, `[Audio]`, `[MR]`); on device filter with
`adb logcat -s Unity:V | grep '\[Piano\]'`. Never grep logs for plain `Error` (the licensing module always logs one).

---------------------------------------------------------------------------------------------------------------------------

## 2. Folder / assembly map and allowed references

```
Assets/MusicRoom/
  Core/      MusicRoom.Core      refs: MusicRoom.Hands, Unity.InputSystem                      (foundation, done)
  Hands/     MusicRoom.Hands     refs: Oculus.VR, Unity.InputSystem                            (foundation, done)
  Audio/     MusicRoom.Audio     refs: MusicRoom.Core, Meta.XR.Audio                           (foundation core; stage 3 verifies recipes)
  Piano/     MusicRoom.Piano     refs: Core, Hands, Audio, Unity.TextMeshPro                   (stage 3)
  Drums/     MusicRoom.Drums     refs: Core, Hands, Audio                                      (stage 3)
  Harp/      MusicRoom.Harp      refs: Core, Hands, Audio                                      (stage 3)
  Room/      MusicRoom.Room      refs: Core, Hands, Audio, URP Core/Universal Runtime, TMP     (stage 3)
  Layout/    MusicRoom.Layout    refs: Core, Hands, Audio                                      (stage 3)
  Menu/      MusicRoom.Menu      refs: Core, Hands, Audio, Unity.TextMeshPro, UnityEngine.UI   (stage 3)
  Lessons/   MusicRoom.Lessons   refs: Core, Hands, Audio, Unity.TextMeshPro                   (Recorder/LessonLibrary/TakeLibrary done; stage 3: Tutorial, deck)
  Guide/     MusicRoom.Guide     refs: Core, Hands, Audio, Unity.TextMeshPro                   (stage 3)
  MR/        MusicRoom.MR        refs: Core, Hands, Audio, meta.xr.mrutilitykit, Oculus.VR     (MrScene/WindowFinder/TableFinder done; stage 3: portals, placing)
  Editor/    MusicRoom.Editor    refs: every runtime asmdef + XR/OpenXR editor + Oculus.VR(.Editor) + URP + TMP + Meta.XR.Audio (foundation)
  Tests/EditMode/  MusicRoom.Tests.EditMode  refs: all runtime asmdefs + MusicRoom.Editor + Oculus.VR + Meta.XR.Audio + mrutilitykit + InputSystem
                                             + Unity.TextMeshPro + UnityEngine.UI + Unity.RenderPipelines.Core/Universal.Runtime
  Tests/PlayMode/  MusicRoom.Tests.PlayMode  refs: all runtime asmdefs (no Editor) + the same packages
  Resources/MaterialTemplates/   shared template .mat assets (made by render-setup)
  Generated/Resources/Generated/ baked PNGs <Module>/<name>.png (made by bake; build bakes the missing ones)
  Settings/  QuestURP.asset, QuestURP_Renderer.asset (made by render-setup)
Assets/Scenes/Main.unity          (made by scene)
Tools/unity.sh
```

Rules:
1. **Feature modules never reference each other.** The foundation assemblies `MusicRoom.Core`, `MusicRoom.Hands` and
   `MusicRoom.Audio` may be referenced by every feature module (as listed above); a feature module (Piano, Drums, Harp, Room,
   Layout, Menu, Lessons, Guide, MR) may add Unity/Meta package assemblies to its own asmdef, never another feature module.
   Cross-module needs go through Core contracts, `IAudioEngine` and `App.Events`. Audio never references a feature module.
2. **Modules never edit files outside their folder.** The merge copies folders. Shared registries are reflection-based:
   `IAppModule` (modules), `IMaterialTemplateProvider` (templates), `ITextureBakeProvider` (baked textures). `Core/link.xml`
   already preserves every `MusicRoom.*` assembly for IL2CPP. **Exactly one asmdef per module folder** (the one listed
   above): never add another (`link.xml`, the Editor asmdef and the test asmdefs list these names and nothing else).
3. **A module's test files live in `Tests/EditMode/<Module>/` and `Tests/PlayMode/<Module>/`** (those folders are the only
   files a module may add outside its own folder; create your folder, do not touch other test folders or the test asmdefs).
   Everything in `Tests/*/<Module>/` belongs to that module, including the files the foundation wrote there (Audio:
   `DspTests`, `RendererTests`, `AudioEngineTests`, `AudioAllocTests`, `AudioLoadTests`; Layout: `LayoutStubTests`;
   Menu: `MenuStubTests`; Lessons: `LessonsTests`): the module updates or replaces them when its behaviour changes. The
   foundation's own folders (`Tests/*/Core`, `Hands`, `Editor`) assert only discovery, services, frame order, boot options,
   hands and the editor tools, never a module's behaviour. `Tests/PlayMode/Core/TestBoot.cs` is the shared boot helper:
   use it, never copy it. The test asmdefs already reference every runtime asmdef, TMP, UnityEngine.UI and the URP runtimes.
4. **Resources are global**: a module's `Resources/` folder is loaded by path (`Resources.Load("Voice/intro_vr")`,
   `Resources.Load<Material>("MaterialTemplates/Room/Flame")`). Prefix your template names with your module name
   (`Room/Flame`, `Piano/KeyLabel`) and your bake names likewise (`Room/plaster`; the shared set is Core's `Core/<name>`):
   `render-setup` and `bake` fail on a duplicate name across providers.
5. Each module folder has a `README.md` naming its public contract and spec pointers; keep it current.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): six more assemblies, each with its own folder and asmdef, listed once in `Core/link.xml`, `Editor/MusicRoom.Editor.asmdef` and both test asmdefs: `MusicRoom.Handpan`, `MusicRoom.HandDrums`, `MusicRoom.Marimba`, `MusicRoom.Theremin`, `MusicRoom.Bowls` (phase 0: stubs built on `Core/Registry/PlaceholderInstrument.cs`) and `MusicRoom.Sheet` (an empty `SheetModule`, Order 45). They reference Core, Hands, Audio (and TextMeshPro, Burst, Mathematics, Collections). New Core folders: `Core/Registry` (the registry, the id block, the built-ins, the placeholder; the one Core folder that may name instruments), `Core/Recording` (`ControlEncoder`, `PlayStamp`, `LaneArbiter`), `Core/Render/PortraitStudio.cs`, `Core/Util/HandToolMath.cs`. Each instrument's sound unit lives in `Audio/Sound/<X>/` (the Audio assembly).

## 3. Runtime composition

### 3.1 Scene (`Assets/Scenes/Main.unity`, written by `SceneBuilder.BuildMain`)
- `PlayerRig` = `OVRCameraRig` prefab instance: `OVRManager` (`trackingOriginType FloorLevel`, `isInsightPassthroughEnabled true`,
  dynamic resolution 0.7–1.6, `requestScenePermissionOnStartup false`), `OVRPassthroughLayer` (`hidden true`), `DesktopRigFallback`
  (disabled). `CenterEyeAnchor` camera: near 0.02, far 40, SolidColor `(0,0,0,0)` (alpha 0 for Meta's editor-time passthrough
  rule; RigBootstrap applies #05070f α 1 at start), `allowHDR false`, culling mask without `Hidden` (30) / `PanelUI` (31),
  tag MainCamera, `AudioListener`.
- `App` = `HandInput` (handMaterial = `MaterialTemplates/HandGhost`) + `AppRoot`.
Nothing else. Everything is built in `Init` by the modules under `App.WorldRoot` ("World", child of `App`).

### 3.2 Boot (`AppRoot.Awake`, execution order −40)
1. `BootOptions` consumed (`HandSource` override, `ModuleFilter`, `ExtraModules` (test probes), `Headless`, and the test hooks
   `Settings` (an `ISettingsStore`), `DataPath`, `SimulateHeadset`, `ManualFrames`, `AudioClock`: section 16).
2. `RigBootstrap.Ensure()`: binds the scene rig (or creates a code-only rig in tests); its `Start` waits ≤ 10 s for
   `OVRManager.OVRManagerinitialized` (0.5 s in the editor), then either applies the runtime knobs (refresh rate, SRP foveation
   level 1.0, SustainedHigh perf, recenter hook) → `XrState.Xr`, or enables `DesktopRigFallback` → `XrState.Desktop` and keeps
   polling, so a runtime that comes up late (XR Simulator, slow device start) still switches to `Xr` (knobs applied, fallback off,
   `StateChanged(Xr)`). Both branches apply the VR clear colour #05070f α 1 (`XrMode.ApplyBackground`).
   AppRoot removes `Layers.HeadCameraExcludeMask` from the head camera and sets `Desktop.dragBlockMask = Layers.PlaySurfaceMask`.
3. `new App(...)`: `Clock` (+ `AudioClock`), `EventBus`, `AppSettings` (over `BootOptions.Settings`, else PlayerPrefs),
   `DataPath` (`BootOptions.DataPath`, else `Application.persistentDataPath`), `MaterialLibrary`, `HoverGlowPool`, `PointerInput`.
4. `ModuleDiscovery.CreateModules()` → instances sorted by `Order`, then `Init(app)` in that order (errors are logged, not fatal).
5. Every frame: `AppRoot.Update` → `App.Frame()` (section 4); with `ManualFrames` nothing runs from Unity's Update and tests
   call `App.Step(dt)` instead.

### 3.3 `App` (Core) — services and registry

```csharp
public sealed class App {
    public static App Current { get; }
    public AppRoot Root { get; }           public Transform WorldRoot { get; }
    public RigBootstrap Rig { get; }       public Transform Head { get; }        // centre eye, never null
    public HandInput HandInput { get; }    public PointerInput Input { get; }
    public Clock Clock { get; }            public EventBus Events { get; }       public AppSettings Settings { get; }
    public string DataPath { get; }        // persistentDataPath (tests: BootOptions.DataPath); takes in DataPath/takes
    public MaterialLibrary Materials { get; }  public HoverGlowPool HoverGlow { get; }
    public IReadOnlyList<IAppModule> Modules { get; }   public IReadOnlyList<IInstrument> Instruments { get; } // sorted by TickOrder
    public bool Headless { get; }          public bool IsHeadset { get; }        // real hands (Meta source) vs mouse/fake
    public bool IsPresenting { get; }      // in XR: Rig.State == Xr, or SimulateHeadset with non-mouse hands
    public bool SimulateHeadset { get; }   public bool ManualFrames { get; }
    public Func<double> AudioClock { get; } public double AudioTime { get; }     // the audio clock (dspTime, or the test's)
    public void Step(float dt);            // ManualFrames: HandInput.Step(dt) + Clock.Step(dt) + Frame(false)
    public void Register<T>(T service) where T : class;   public void Unregister<T>(T service) where T : class;
    public T Get<T>() where T : class;     // first registered service of T, else the first module implementing T, else null
    public bool TryGet<T>(out T service) where T : class;
    public T Require<T>() where T : class; // throws with a message about module Order
    public List<T> GetAll<T>() where T : class;
    public void Frame(bool fromUnity = true);
}
```
Registration rules: register in `Init`, under the contract type (`app.Register<IInstrument>(this)`, `app.Register<IPianoApi>(this)`,
`app.Register<Rig>(rig)`). Services needed in `Init` must come from a lower `Order`; services needed later may be resolved
lazily with `Get<T>()` (cache the result in `Init` of a later phase or on first use).

XR gates: every "is the player in XR" test uses `App.IsPresenting` (grab, swivel, two-hand summon, page
turn, nod kick, MR placing, the `*_vr` voice lines); the desktop-only paths (M, Shift+H, mouse clicks, Shift/Alt-drag) run
while `!App.IsPresenting`. `App.IsHeadset` only says the hand frames come from the tracker; never use it as the XR gate.

### 3.4 Module discovery

```csharp
public interface IAppModule { int Order { get; } void Init(App app); void Tick(FramePhase phase, float dt); void Shutdown(); }
public abstract class AppModule : IAppModule { /* no-op defaults */ }
```
`ModuleDiscovery.CreateModules(Func<Type,bool> filter = null, IEnumerable<IAppModule> extra = null)` scans every loaded assembly whose name starts with `MusicRoom.` EXCEPT the test
assemblies (`MusicRoom.Tests.*`) for concrete, non-`UnityEngine.Object`, parameterless classes implementing `IAppModule`
(MonoBehaviours are built BY modules, never discovered). Order: `Order` ascending, then full type name. Tests that need a
probe module pass instances in `BootOptions.ExtraModules` (merged into the same ordering), so a test helper in one module's
test folder can never join the boots of other tests. The editor's provider discovery (`IMaterialTemplateProvider`,
`ITextureBakeProvider`) skips the test assemblies the same way.

| Module class | Order | Registers |
|---|---|---|
| `MusicRoom.Audio.AudioModule` | 10 | `IAudioEngine` |
| `MusicRoom.Room.RoomModule` | 20 | `IRoom`, `IAmbienceSwitches`, `ISnowWeather`, `ISnowPortals`, `Rig`(Table); stage 3 adds `IInstrument` (switch panel, Kind Table, TickOrder 40) |
| `MusicRoom.Piano.PianoModule` | 30 | `IInstrument`, `IPianoApi`, `Rig`(Piano) |
| `MusicRoom.Drums.DrumsModule` | 31 | `IInstrument`, `IDrumsApi`, `Rig`(Drums) |
| `MusicRoom.Harp.HarpModule` | 32 | `IInstrument`, `IHarpApi`, `Rig`(Harp) |
| `MusicRoom.Lessons.LessonsModule` | 40 | `IRecorder`, `ILessonLibrary`, `ITakeLibrary`, `ITutorial`; stage 3 adds `IInstrument` (tape deck, Kind Table, TickOrder 30) |
| `MusicRoom.Guide.GuideModule` | 50 | `IGuide` |
| `MusicRoom.Menu.MenuModule` | 60 | `IMenuPanel`, `IInstrument`(Menu), `Rig`(Lessons) |
| `MusicRoom.MR.MrModule` | 70 | `IMixedReality` |
| `MusicRoom.Layout.LayoutModule` | 100 | `IRigRegistry`, `ILayoutService`, `IGrabService`, `IPlacementService` |

Keep these class names and orders (tests assert them). A module may add further `IAppModule` classes (e.g. `SticksModule`) in
its own folder (its one assembly), with its main module's `Order` (ties sort by full type name, so a module's classes stay
together and never land between two other modules); there are no order bands.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): `new App(...)` builds the instrument registry by reflection (`InstrumentRegistry.Discover`: every parameterless `IInstrumentDefinition` in `MusicRoom.*`, never `MusicRoom.Tests.*`) before `ModuleDiscovery.CreateModules`; `App.Registry`, also `Get<IInstrumentRegistry>()`. `BootOptions.InstrumentFilter` keeps an instrument's definition assembly from creating modules (tests boot the classic three with `k => k <= InstrumentKind.Harp`); `Register<Rig>` marks an instrument loaded. Modules 16 (the ten, five stubs, Sheet).

## 4. Frame orchestration, time base, units

`App.Frame()` (spec 05 section 1.1; spec 08 section 7):

| # | Step | Who |
|---|---|---|
| 1 | `Clock.Begin()` — `Dt = min(unscaledDeltaTime, 0.05)`, `Now`/`NowMs` frame-constant, `AudioNow = dspTime` | Core |
| 2 | `Tick(FramePhase.StickPoses)` — sticks follow the hands (grip from wrist/thumb/index) BEFORE the pointers are read | Drums |
| 3 | `Input.Update(dt, Clock.Now)` — hand connection, pinch/grasp flags and timers, pointer tips/velocities (external pointers read `Next` while `Down`) | Core |
| 4 | `Tick(InputDone)` | observers (Menu: M key) |
| 5 | **Instruments**: for each `IInstrument` sorted by `TickOrder` (spec 05 §1.1 order: Piano 0, Drums 10, Harp 20, tape deck 30 (Lessons), switch panel 40 (Room), Menu 50): none at all while `IMixedReality.IsPlacing` (the menu included); skip if `!IsPresent` (the Menu is always present: it runs its summon gesture and animation while hidden and guards its pokes with `Shown`); skip instruments of `IGrabService.HeldRig`; `Tick(pts, dt)` then `Hover(pts, HoverGlow)`; drums get `Input.AllPointers` (sticks included), the rest `Input.HandPointers` | Core |
| 6 | `Tick(Instruments)`, `Tick(Tutorial)` | Lessons (blocks, hits) |
| 7 | `Tick(StickHover)` | Drums |
| 8 | `HoverGlow.Update(now)` then `Tick(HoverGlow)` | Core |
| 9 | `Tick(FirstArrange)` | Layout (first tracked frame) |
| 10 | `Tick(MixedReality)` | MR (placing reticle, portals) |
| 11 | `Tick(Grab)` | Layout: per hand the stick pick-up FIRST (`IDrumsApi.TryPickUp`), then grab guards, dwell, drag, two-hand twist, and in MR the placing pinch (`IMixedReality.StartPlacing`); returns early while `IsPlacing` |
| 12 | `Tick(StickReleases)` | Drums |
| 13 | `Tick(Listener)`, then `Tick(NodKick)` | Audio bookkeeping, Drums |
| 14 | `Tick(Recorder)` | Lessons (playback dispatch) |
| 15 | `Tick(Decor)` | Room (flicker, snow weather, smoke, plants) |
| 16 | `Tick(Guide)` | Guide |
| 17 | `Tick(Swivel)`, `Tick(TwoHandSummon)`, `Tick(PageTurn)`, `Tick(LayoutAnimation)` | Layout |
| 18 | `Input.EndFrame()` (clears `PinchStart/End`, `GraspStart/End`) then `Tick(EndFrame)` | Core, modules |

Execution orders of MonoBehaviours: `OVRManager −100`, `HandInput −100`, `RigBootstrap −90`, `OVRHand −90`, `MetaHandVisual −80`,
`OVRSkeleton −80`, `OVRCameraRig −50`, `AppRoot −40`, everything else 0, `OVRPassthroughLayer +100`, `AudioEngine −1000` (FrameNow).
Modules should NOT rely on their own `Update()` for play logic; use `Tick(phase)` so order is explicit. Visual-only components
(animations, LateUpdate re-posing) may use Unity callbacks.

Time base: `Clock.NowMs` is the frame-constant millisecond clock; `Clock.Dt` is the only dt for game logic; the audio
clock is `IAudioEngine.Now` = `AudioSettings.dspTime` (recorder timestamps, scheduled plucks, Sunny's line timing) and `FrameNow`
(sampled at frame start, −1000 execution order) for sequences spanning several calls. Game logic never reads
`AudioSettings.dspTime` itself: `IAudioEngine.Now` / `App.AudioTime` honour `BootOptions.AudioClock` (scripted in tests). `Time.*` is for visuals only. Units: metres, seconds, degrees
in Unity transforms (`Quaternion.Euler`), radians only inside the source-frame formulas. Colours in hex are sRGB (`TextureUtil.Hex`).

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): frame step **3b** right after `Input.Update`: `PointerClaims.Update` (every active fingertip's `Pointer.ClaimedBy`: a present contact instrument whose rig zone holds it, in TickOrder; else the shown menu's zone; else an `IPointerClaimant`; else null; ~5 us). `TickInstruments` gives each instrument the pointer list of its descriptor's `ToolMask` (`PointerInput.PointersFor`, rebuilt only on external registrations). Layout's focus (`Facing`, +-30 deg / 10 deg hysteresis / 0.3 s dwell, `FocusEvent`) runs next to the swivel; glides run in `LayoutAnimation`.

## 5. Coordinate conventions (`Core/Util/Coords.cs`)

- Layouts are authored in a right-handed source frame (−z forward). Position: source `(x, y, z)` → Unity `(x, y, −z)`. Quaternion `(x, y, z, w)` → `(−x, −y, z, w)`. Yaw: `yaw_u(deg) = −yaw_src(rad)·180/π`.
- Source YXZ Euler `(α, β, γ)` → `Quaternion.Euler(−α°, −β°, γ°)`; source XYZ Euler → `AngleAxis(−α, right)·AngleAxis(−β, up)·AngleAxis(γ, forward)`.
- The mirror flips cross products and triangle winding: re-derive crosses (`HandMath.Palm` swaps the operands) and reverse
  winding of mirrored procedural meshes (`Coords.MirrorZ`). Build procedural layouts in source-frame coordinates with the spec
  formulas, then convert once.
- Examples: piano home `(0, .72, −.45)` → `(0, .72, .45)`, drums `(.2, 0, .26)` yaw `−π/2` → `(.2, 0, −.26)` yaw `+90°`, harp
  `(−.78, 0, .2)` yaw `−83°` → `(−.78, 0, −.2)` yaw `+83°`. The player faces **+z**; room x ∈ [−3.2, 3.2], z ∈ [−4.08, 1.12], y ∈ [0, 2.9];
  back wall (paintings) at z = +1.12, window on the right wall x = +3.2, fireplace behind the player at z = −4.08.
- **Piano `back`** = piano-local Unity +z = distance behind the white-key fronts (= −z_src). Piano origin = white key tops, x right.
- **Facing rule** (menu, Sunny, bubble, rings, portals): local **+z points away from the player**; the visible side is −z; +x is
  the player's right. Snow portals: `LookRotation(outward, up)`, local +z out through the glass.
- Hand joints after the flip: `rot * Vector3.forward` points toward the fingertip, `rot * Vector3.up` out of the back of the hand.
- Generated image row 0 = texture top; write image row y to `Texture2D` row `h−1−y`.
- Heading of a flattened forward: `Coords.HeadingDeg(f) = atan2(f.x, f.z)` degrees; `SeatFrame.From(head)` uses it.

---------------------------------------------------------------------------------------------------------------------------

## 6. Hands and input

### 6.1 Hand data (`MusicRoom.Hands`)

```csharp
public enum HandSide { Left = 0, Right = 1 }
public enum Finger { Thumb = 0, Index = 1, Middle = 2, Ring = 3, Little = 4 }
public enum HandJoint {  // OpenXR XrHandJointEXT order = OVRSkeleton.BoneId.XRHand_* values
    Palm = 0, Wrist = 1, ThumbMetacarpal = 2, ThumbProximal = 3, ThumbDistal = 4, ThumbTip = 5,
    IndexMetacarpal = 6, IndexProximal = 7, IndexIntermediate = 8, IndexDistal = 9, IndexTip = 10,
    MiddleMetacarpal = 11, MiddleProximal = 12, MiddleIntermediate = 13, MiddleDistal = 14, MiddleTip = 15,
    RingMetacarpal = 16, RingProximal = 17, RingIntermediate = 18, RingDistal = 19, RingTip = 20,
    LittleMetacarpal = 21, LittleProximal = 22, LittleIntermediate = 23, LittleDistal = 24, LittleTip = 25 }

public sealed class HandFrame {
    public const int JointCount = 26;  public static readonly int[] TipJoint = { 5, 10, 15, 20, 25 }; // by Finger
    public readonly HandSide side;
    public bool isTracked, isHighConfidence, isDominant, systemGesture, poseInferred;
    public float handScale, lostSeconds;
    public readonly Vector3[] position;  public readonly Quaternion[] rotation;  public readonly float[] radius;   // world; 26 each
    public readonly bool[] fingerHighConfidence; public readonly float[] pinchStrength; public readonly bool[] runtimePinch; // 5 each
    public bool pointerValid; public Pose pointer;                 // aim ray (position along forward)
    public double sampleTime, displayTime; public bool newSample; public float sampleInterval;
    public readonly Vector3[] tipVelocity;                         // 5, world m/s, between distinct tracker samples
    public Vector3 Get(HandJoint j); public Vector3 Tip(Finger f); public Pose Wrist { get; }  public void MarkLost();
}
public interface IHandSource { string Name { get; } bool IsAvailable { get; } void Tick(float deltaTime); HandFrame Get(HandSide side); }

public static class HandMath {
    public const float PinchStartGap = 0.015f, PinchEndGap = 0.025f, GraspClose = 0.105f, GraspOpen = 0.13f;
    public static float PinchGap(HandFrame h); public static Vector3 PinchPoint(HandFrame h);
    public static bool NextPinch(bool pinching, float gap); public static bool NextGrasp(bool grasping, float amount);
    public static float GraspAmount(HandFrame h);                              // mean wrist distance of middle/ring/little tips
    public static bool Palm(HandFrame h, out Vector3 center, out Vector3 normal); // normal points OUT of the palm
    public static float YawDegrees(HandFrame h);                               // wrist heading; deltas have the opposite sign of the source frame
}
public static class HandRestPose { Vector3[] Left; float[] Radius; Vector3 PointCurl, FistCurl; void Fill(HandSide, Vector3[]); void Curl(Vector3[], Finger, Vector3); }
```
Sources: `MetaHandSource` (headset; `IsAvailable = OVRManager.OVRManagerinitialized && isHmdPresent`; poses from
`OVRPlugin.GetHandState(Step.Render)` in `OVRCameraRig.trackingSpace`, velocities from `GetUnextrapolatedHandState` timestamps),
`MouseHandSource` (editor: index tip on the mouse ray; LMB poke 2 cm in 50 ms, RMB pinch, MMB fist, Tab switches the hand,
`-`/`=` hover distance; no letter keys and no Shift, which belong to the keyboard piano, Shift/Alt-drag and Shift+H; its
pointers never play: section 0), `FakeHandSource` (tests, section 16), `UntrackedHandSource` (the device player's source until
the runtime is up: both hands untracked, never the mouse).

`HandInput` (MonoBehaviour on `App`, order −100): `Instance`, `static IHandSource OverrideSource` (set before boot),
`Source`, `Meta`, `Mouse`, `Untracked`, `IsHeadset`, `Left`/`Right`/`Get(side)`, per-hand arrays `pinching, pinchStart, pinchEnd,
grasping, graspStart, graspEnd` (index `(int)HandSide`; edge flags valid until the next `HandInput.Step`), `Step(dt)` (ticks the
source and derives the flags; `Update` calls it, with `ManualFrames` only `App.Step` does), `Visual(side)` (`MetaHandVisual`,
`appAlpha` to dim a hand holding a stick), `DebugHands` (joint spheres for mouse/fake sources, editor only; players never
build them). While a hand is untracked or doing the
system gesture the pinch/grasp state is held.

### 6.2 Pointer model (`MusicRoom.Core`, spec 05 section 1)

```csharp
public enum Handedness { Left = 0, Right = 1 }
public enum PointerSlot { Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4 }   // TIPS order; Pointers[0] = pinch/grab pointer
public interface IHandTool { }                                                       // a held drumstick
public interface IPointerOwner { IReadOnlyList<Pointer> Pointers { get; } double LastPlayMs { get; set; } }
public sealed class HandState : IPointerOwner {
    public readonly Handedness Side; public readonly int Index;
    public bool Connected; public bool Pinching, PinchStart, PinchEnd; public float PinchFor;
    public bool Grasping, GraspStart, GraspEnd; public IHandTool Holding;
    public bool IsMouse;                      // frames from the desktop MouseHandSource: its pointers never strike / poke / pluck
    public double LastPlayMs { get; set; }   // −inf when never
    public readonly Pointer[] Pointers;      // 5 (TIPS order)
    public Pointer IndexPointer { get; } public Pointer ThumbPointer { get; }
}
public sealed class Pointer {
    public readonly string Id; public readonly IPointerOwner Owner; public readonly int Joint /*HandJoint, −1 external*/, Finger /*0..4, −1*/;
    public Vector3 Tip, Prev, Next, Vel; public float Speed, SampleDt; public double SampleT; public Vector3 TrackerVel;
    public bool Active, WasActive, External, Down, Stick, Shoulder;
    public readonly double[] DrumLastHit;   // 9 pieces, NowMs, −inf
    public readonly sbyte[] DrumArm;        // 9 pieces, −1 unset / 0 / 1
    public static Pointer CreateExternal(string id, IPointerOwner owner, bool stick, bool shoulder);
    public void Deactivate();
    public bool FromMouse { get; }          // owner is a HandState with IsMouse: skip it for play (desktop model, section 0)
}
public sealed class PointerInput {
    public readonly HandState[] Hands;        // [Left, Right]
    public IReadOnlyList<Pointer> AllPointers { get; }   public IReadOnlyList<Pointer> HandPointers { get; }   public IReadOnlyList<Pointer> ExternalPointers { get; }
    public bool IsHeadset { get; }  public bool MouseHand { get; }  public HandInput Source { get; }
    public HandState Hand(Handedness side); public HandFrame Frame(HandState h);
    public void RegisterExternal(Pointer p); public void UnregisterExternal(Pointer p);
    public void Update(float dt, double nowS); public void EndFrame(); public void ResetMotion();
    public bool TryIndexTip(HandState h, out Vector3 tip);                     // even while holding a stick
    public bool TryPalm(HandState h, out Vector3 center, out Vector3 normal);
    public float GraspAmount(HandState h);                                     // NaN when untracked
    public bool TryPinchPoint(HandState h, out Vector3 point);                 // midpoint of thumb and index tips
    public float WristYaw(HandState h);
    public static bool IsPlaying(IPointerOwner owner, double nowMs, double guardMs = 1000);
}
```
Velocity semantics: `Pointer.Vel`/`Speed`/`SampleDt` (gap since the last position change,
clamped ≥ 4 ms, EMA 0.25, zero when the pose repeats or the pointer was inactive) — use them with `Prev`/`Tip` deltas
(`vy = (prev.y − tip.y)/sdt` with `sdt = max(SampleDt > 0 ? SampleDt : dt, 1e-3)`). `TrackerVel` is the headset's
between-sample fingertip velocity (spec 02 section 6), for strike-strength heuristics that want the raw tracker rate.
Hand pointers are muted (inactive) while `Holding != null`. Call `Input.ResetMotion()` after a snap turn, re-arrange or rig
teleport (Layout does it in `ArrangeAroundPlayer`).

### 6.3 Rig and XR mode (`MusicRoom.Hands`)

```csharp
public enum XrState { Starting, Xr, Desktop }
public sealed class RigBootstrap : MonoBehaviour {
    public const string RigName = "PlayerRig"; public const float PreferredRefreshHz = 72f, AlternativeRefreshHz = 90f, XrStartTimeout = 10f;
    public const float FoveationLevel = 1f;   public static int ApplyFoveation(float level);   // XRDisplaySubsystem.foveatedRenderingLevel on every display
    public static RigBootstrap Instance { get; } public static RigBootstrap Ensure();   // scene rig, or a code-only rig (tests)
    public OVRCameraRig Rig { get; } public OVRManager Manager { get; } public OVRPassthroughLayer Passthrough { get; } public DesktopRigFallback Desktop { get; }
    public XrState State { get; } public Transform RigRoot { get; } public Transform TrackingSpace { get; } public Transform Head { get; } public Camera HeadCamera { get; }
    public bool IsXrActive { get; } public bool HmdMounted { get; }
    public event Action Recentered; public event Action<bool> HmdMountChanged; public event Action<XrState> StateChanged;
}
public static class XrMode {
    public static readonly Color VrBackground /* #05070f */, MrBackground /* (0,0,0,0) */;  public static bool PassthroughOn { get; }
    public static bool PassthroughAvailable(RigBootstrap rig);   // XR up AND OVRManager.IsInsightPassthroughInitialized() (not while pending)
    public static bool PassthroughPending(RigBootstrap rig);     // the start-up init is still running (MR Enter waits ≤ 2 s)
    public static bool SetPassthrough(RigBootstrap rig, bool on); public static void ApplyBackground(RigBootstrap rig, bool mixedReality);
    public static bool PassthroughRecommended(RigBootstrap rig);
    public static void SnapTurn(RigBootstrap rig, float degrees);   // RotateAround(head, up): positive = world turns clockwise from above
    public static void MoveRig(RigBootstrap rig, Vector3 delta);    // fitSeat
}
public sealed class DesktopRigFallback : MonoBehaviour { static Vector3 EyePosition (0, 1.2, −0.1), LookAt (0, 0.95, 0.45); const float DefaultFov = 55; float yaw, pitch, fov;
    int dragBlockMask /* Layers.PlaySurfaceMask, set by AppRoot */; Func<bool> dragBlocked; bool DragBlocked(Vector2 mouse, bool shiftOrAlt); }
```
Guard every `OVRManager.display/instance` and `OVRPlugin` call with `IsXrActive` (including the MR module's
`RunSpaceSetup`: `OVRScene.RequestSpaceSetup` is a real native call in the macOS editor). Foveation is the SRP API only:
never `OVRManager.foveatedRenderingLevel` / `useDynamicFoveatedRendering` (they drive Meta's Legacy feature, which is off). `Recentered` → Core publishes `RecenteredEvent`
(Layout re-arranges); `HmdMountChanged` → `HmdMountEvent` (Menu hides, Guide cuts its voice on unmount).

---------------------------------------------------------------------------------------------------------------------------

## 7. Audio (`MusicRoom.Audio`)

### 7.1 Graph and threading
```
main thread: instruments → IAudioEngine → SynthKit stages SynthCommands → per-source SpscRing(512) → Commit (one batch per API call)
audio thread (one mixer thread, 256-frame blocks, 5.33 ms @ 48 kHz):
  SynthSource.OnAudioFilterRead × 18 → drain ring → voices.Render (sample-exact) → + pending sends → processors (beds, drum room)
      → modal resonator (soundboard/soundbox) → × bus → sendTarget.AddInput → channels (mono to all)
    → Meta XR Audio spatializer (HRTF; distance = Unity Logarithmic rolloff, min 0.6, Rolloff Scale 1.1 = inverse distance law)
    → AudioMixer "MusicRoomMixer"/Master [Attenuation, Meta XR Audio Reflection (ER + shared late reverb; suspend off)]
    → AudioListener (centre eye): MasterBus.OnAudioFilterRead (×0.9 → DynamicsCompressor, no look-ahead → high shelf 7.5 kHz +1.5 dB → ceiling 0.98)
workers (2, background): OfflineRenderQueue → periodic-wave tables + MetalBank (pre-rendered six-square sums), 88 piano keys middle-out, 22 harp strings, 22 robotised voice lines → SampleBank
```
Workers: `Thread.Priority = BelowNormal` does nothing under IL2CPP on Android (libil2cpp `os/Posix/ThreadImpl.cpp`
`SetPriority` is an empty TODO), so on Android each worker attaches to the JVM and calls
`android.os.Process.setThreadPriority(10)` (THREAD_PRIORITY_BACKGROUND; detached when it exits; `OfflineRenderQueue.PriorityLowered`
counts them). Every output buffer (88 keys + 22 strings = 79.9 MiB, plus the 22 lines) is allocated on the main thread in
`AudioEngine.Init`, i.e. at boot before the intro; the workers render into them and reuse per-thread scratch (the piano's
7 s double accumulator and noise burst, the robot comb), so they create no large garbage while Sunny talks (the old path made
142 MB of short-lived arrays in ≈ 1.5 s, inviting Boehm stop-the-world collections that pause the mixer thread too).
Rules on the audio thread: no allocation, no locks, no Unity API except `AudioSettings.dspTime`, no `Debug.Log`. Main → audio
only through `SynthCommand`s; audio → main only through `volatile`/`Interlocked` fields (`Rms`, `BlocksRendered`,
`DroppedCommands`, `MaxBlockMicros`). Workers → all through `SampleBank.Publish` (`Volatile.Write`). Component order on a
source GameObject: `AudioSource`, `MetaXRAudioSource`, `SynthSource`; never add other audio filters. Sources are created inactive,
configured, then activated (`AudioRig.Build`).

### 7.2 Sources (exact names: GameObject `"Synth " + SourceId` under `World/Audio`)

| `SourceId` | Source name | Parent / position (set by) | Pools n/t/m/b | Acoustics | Reverb send VR (dB) |
|---|---|---|---|---|---|
| `PianoLo`, `PianoMid`, `PianoHi` | piano-lo/-mid/-hi | piano rig local (−0.42 / 0 / 0.42, 0.05, 0.05) (Piano) | 16/0/0/48 + soundboard resonator | on | 0 |
| `Harp` | harp | harp rig local (−0.33, 0.8, 0) (Harp) | 24/0/0/48 + soundbox resonator | on | +3 |
| `Kick`, `Snare`, `Hat`, `Tom1`, `Tom2`, `Floor`, `Crash`, `Ride` | drum:<kind> | each piece group, local (0,0,0); Kick = the shell (Drums) | see `AudioRig.Configure` | on | −3 |
| `DrumRoom` | drum:room (at the kit, not the origin) | kit centre, drums-local (0.05, 0.8, 0.4) (Drums); fed by the drum sources' `sendTarget` (kick .35, crash/ride .2, others .5), bus 0.3 | processor `DrumRoomReverb` | on | 0 |
| `Fire` / `Snow` / `Wind` | fire / snow / wind | world (0.3, 0.5, −3.85) / (3.1, 1.55, −1.18) / (3.1, 1.9, −1.18) (Audio); MR: Snow/Wind to the window portal (MR) | processors | off | – |
| `Guide` | guide | Sunny root (Guide) | 0/8/0/4, HumanVoice directivity | off | – |
| `Ui` | (master) | none, 2D | 8/8/0/0 | – | – |

Mixed reality: `SetMixedReality(true)` lowers every acoustic source's send by 6 dB; `SetRoomBox` moves the shoebox to the MRUK
room bounds (GypsumBoard/Carpet); VR box = centre (0, 1.45, −1.48), size (6.4, 2.9, 5.2), PlasterOnBrick walls, Brick back,
GypsumBoard ceiling, WoodFloor floor, clutter 0.6.

### 7.3 `IAudioEngine` (registered by `AudioModule`; `when`: 0 = ASAP, > 0 absolute dspTime, past = now)

```csharp
public enum SourceId { PianoLo, PianoMid, PianoHi, Harp, Kick, Snare, Hat, Tom1, Tom2, Floor, Crash, Ride, DrumRoom, Fire, Wind, Snow, Guide, Ui, Count }
public enum DrumKind : byte { Kick, Snare, Hat, HatOpen, HatClose, Tom1, Tom2, Floor, Crash, Ride }
public enum DrumZone : byte { Head, Rim, Bell, Bow, Edge, Top }
public struct DrumStrike { public float Pos; public DrumZone Zone; public bool Open; static DrumStrike Default /* .3, Head */; static DrumZone ParseZone(string); static string ZoneName(DrumZone); static DrumKind ParseKind(string); }
public enum PluckMode : byte { Through, Across }
public struct PluckOptions { public float U, Speed; public PluckMode Mode; public double Delay; static PluckOptions Default /* .35, 0, Through, 0 */; }
public enum ChirpKind : byte { Open, Close }
public readonly struct VoiceHandle { public readonly SynthSource Source; public readonly int Id; public bool IsValid { get; } public static VoiceHandle None; }

public interface IAudioEngine {
    bool IsReady { get; } int SampleRate { get; } double Now { get; } double FrameNow { get; } float RenderProgress { get; }
    VoiceHandle Piano(int midi, float vel, double when = 0);
    void Release(VoiceHandle h, bool thud = true, double when = 0);           // τ 60 ms, stop +0.5 s; thud = damper noise
    VoiceHandle Pluck(int stringIndex, float vel, double when = 0, PluckOptions? options = null);
    void Damp(VoiceHandle h, int hands = 1, double when = 0);                 // τ 18 ms, stop +0.25 s, "shh" × √max(1, hands)
    double HarpRingSeconds(int midi);                                         // clamp(4.5 − .08(midi − 48), .7, 4.5)
    void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0);
    void Choke(DrumKind kind);                                                // τ 12 ms + skin noise
    void SetFire(bool on); void SetWind(bool on); void SetSnow(bool on); bool FireOn { get; } bool WindOn { get; } bool SnowOn { get; }
    void SnowGust(float strength, float attack, float hold, float release);   // only while SnowOn
    SynthSource Source(SourceId id);
    void SetSourceParent(SourceId id, Transform parent, Vector3 localPosition); void SetSourcePosition(SourceId id, Vector3 world);
    void SetMixedReality(bool mixedReality); void SetRoomBox(Vector3 center, Vector3 size, bool realRoom);
    void Chirp(ChirpKind kind, double when = 0);
    bool HasVoiceLine(string id); float VoiceLineSeconds(string id);
    double PlayVoiceLine(string id, double when = 0);                         // returns the dspTime the line ends (0 if missing)
    void StopVoiceLine(); bool VoiceLinePlaying { get; } float VoiceLevel { get; }  // mouth: RMS over 21.3 ms windows
    void Swish(double when = 0);                                              // page turn, Ui source
    AudioTestTap TestTap { get; set; } void SeedAll(uint seed);
}
public enum AudioCall : byte { Piano, Release, Pluck, Damp, Drum, Choke, Fire, Wind, Snow, SnowGust, Chirp, VoiceLine, StopVoiceLine, Swish }
public sealed class AudioTestTap {                     // tests only (allocates by design)
    public struct Call { public AudioCall Method; public int Index /*midi | string | (int)kind | (int)chirp*/; public float Vel; public DrumKind Drum;
        public DrumStrike Strike; public PluckOptions Pluck; public double When; public bool Flag /*thud | on*/; public int Hands; public string Line; public VoiceHandle Handle; }
    public readonly List<Call> Calls;                  // every IAudioEngine call as the instrument made it: assert instrument output here
    public readonly List<Entry> Commands;              // the staged SynthCommands: the Audio module's own recipe detail
    public int Count(AudioCall m); public int Count(SourceId s, SynthOp op); public void Clear();
}
```
`Now` / `FrameNow` read `BootOptions.AudioClock` when a test sets it (the mixer itself always runs on `dspTime`, so absolute
`when` values from a scripted clock play at once or late; assert them on the tap).
Re-pluck of a ringing string: `Release(h, thud: false)`. Piano velocity in from strike speed: `StrikeVelocity` (spec 05 section
2.3). The engine clamps `vel` to [0.05, 1]. Piano register: midi < 52 Lo, < 76 Mid, else Hi (`PianoSynth.SourceFor`).
`SynthSource` stats for device profiling: `AvgBlockMicros`, `MaxBlockMicros`, `DroppedCommands`, `StolenVoices`, `ActiveVoices`.

### 7.4 `SynthCommand` (for stage-3 synth code inside the Audio module)
`Op` ∈ {StartNoise, StartTone, StartMetal, StartBuffer, Release, ChokeGroup, SetBusGain, Custom, Panic}; `When` as above or
`< 0` = relative seconds inside one committed batch (`SynthKit.Offset(when, s)`); `Peak`/`Decay` (exp to 0.0005, stop +50 ms);
`F0/F1/Slide` (tone glide; metal ratio; buffer comb lag; gust f0); `Hp/Lp/Bp/Q` (Hz; lp/hp Q in dB per spec 06 §1.2, bp Q linear);
`Level` (dest gain, e.g. drum 1.7); `Tau/StopAfter` (release); `Rate/Res/Comb/Buffer` (buffer voices: rate, resonator send,
pluck comb depth, SampleBank slot). Slots: `AudioEngine.PianoSlot0 + midi − 21`, `HarpSlot0 (100) + string`, `VoiceSlot0 (200) + line`.
Choke group 1 = hats and cymbals. Custom kinds: 1 fire pops on/off (`Level`), 2 snow gust.

### 7.5 Stage-3 Audio work (the recipes are already implemented from the verified 04 sources; verify and complete against spec 06)
- Null-test every drum kind/zone against `Tests/EditMode/Audio/Reference~/drum_*.wav` with `NoiseTable.BuildSeeded(sr, 1)` and `SeedAll` (jitter 0):
  residual < −60 dB; the piano WAVs (`piano_C4_v040/v080`, `A0_v070`, `C7_v070`) and harp WAVs likewise (releases at the frames in 06 §12).
- `PeriodicWaveTables` and `MetalBank` are built by the first render job (the whole 133-job queue takes 1.3–1.5 s on the Mac;
  both are static, built once per process); voices started before that use
  PolyBLEP (plateau 0.848) and live oscillators. `Oscillator` selects the table range (two tables and their weight) once per
  frequency in `Start` / `SetFrequency`, not per sample: bit-identical to the 06 reference (`PeriodicWaveTables.Sample`) for a
  constant pitch, and glides move the range by `RangeStep(factor)` per sample (one log per glide, within 1e-6 of the
  per-sample recompute). Metal hits read the ratio's pre-rendered six-square sum (spec 04 §4.9 optimisation 1; ratios 1, 1.3,
  1.7, 1.9; 2 s each, 1.5 MiB; the same floats as the live voice), so a metal voice costs one buffer read + two biquads.
  Measured (editor Mono, `test-perf`, two runs): six-square sum 326–361 ns/sample with the old per-sample range selection,
  37–45 hoisted, 56–58 PolyBLEP, 2.9–3.5 from the bank. Confirm the switch-over is inaudible or pre-bake the tables (editor asset) if start-up
  CPU on device is a problem.
- `DrumRoomReverb` is an approximation (exact early taps + FDN tail); replace with a partitioned convolution of
  `Notes.MakeRoomImpulse(0.45, .004, .3, .45, .3, .8)` if the A/B against the reference renders asks for it. `Notes.MakeRoomImpulse` /
  `MakeBodyImpulse` / `ConvolverScale` match the 06 fingerprints (tests).
- Tune on device: `ReverbSendDb` per source (start values above), `ReverbReach` 0.7 for instruments, ER +3 dB for drums,
  `SetSharedReverbWetLevel`; calibrate front loudness to the reference HRTF level (−5 dB per ear at ≤ 0.6 m).
- Log at start-up: `outputSampleRate`, `GetDSPBufferSize`, spatializer name (must be "Meta XR Audio"), `voiceLimit`.
- Per-block budget: our C# ≤ 1.5 ms per 5.33 ms block on device (`SynthSource.MaxBlockMicros`), 0 dropped commands. Measured
  with the tables and metal sums ready (`test-perf`, the E12 busy groove in the booted app, editor Mono, two runs): 310–327 µs
  total of the per-source averages (5.8–6.1 % of a block; spec 04's PolyBLEP-era E12: 428–477), 1135–1249 µs sum of maxima,
  Ride 35–37 / 52–53 µs (E12: 101–110 avg), Crash 10–11 / 237–261, Hat 9–10 / 21–22, Harp 68–70 / 124–125, piano 35–47 each,
  0 dropped, 0 stolen; with spec 04's ×2 for IL2CPP ≈ 0.65 ms average per block (the review's estimate for the un-hoisted
  table path was ≈ 2.9 ms from the cymbals alone).
- Latency knobs (section 17): `AudioModule.PreferredDspBuffer` (256; try 128 on the headset and keep it only while
  `MaxBlockMicros` stays under half a block and logcat shows no underruns: `AudioSettings.Reset` runs once in `Init`, before
  any source exists), `RigBootstrap.PreferredRefreshHz` (90 Hz cuts detection to 0–11 ms). `AudioEngine.LatencyProbe` (on in
  development device builds) logs `[Audio] latency-probe <call> <index>: in-frame <dspTime − FrameNow> ms, frame <dt> ms,
  next block ≤ 5.33 ms, ring 256x4 = 21.3 ms` for every live (ASAP) piano / drum / pluck call, so the in-app part can be
  separated from the HAL part of the filmed-tap test.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): **Audio:** 26 sources (`SourceId` 18-25: Handpan, HandDrumLo, HandDrumHi, MarimbaLo, MarimbaMid, MarimbaHi, Theremin, Bowls; the classic 0-17 never move); `UnitId` 5-9 (Count 10), 4-bit unit handles (27-bit payloads); `Render<X>` / `Create<X>` partials pre-declared; the five roles are unit-only (`SoundUnits.IsUnitOnly`: no classic synth, Classic sound never suspends them). `AudioSourceTable` (one row per source: spatial, acoustics, send, reach, ER, pools, room group) replaces the range checks; the classic rows are pinned by `AudioSourceTableTests`; descriptor overrides (`AudioSourceSpec.SendDb/ErDb`) apply at boot. `UnitSlot` Share / Class / Work / IdleBlocks, `UnitFlags.Resumed`; `Register(..., idleSkip)`: a silent unit skips its render after 8 blocks. `UnitGovernor` (classes Focus / AtSeat / Playback / Tail; p99 > 1.6 ms: the lowest class above 0.4 loses 0.15; < 1.0 ms: +0.05). `AudioPresence` (`IAudioPresence`): a displayed instrument's sources pause after 1 s of silence and wake for a bring or a playback dispatch. The room: 20 taps, 4 early-reflection groups assigned per frame by `RoomGroupAssigner` (the at-seat set, then recently sounding displayed instruments; the default set renders bit for bit as before). `GuideVoice` merges every `Resources/VoiceManifests/*.json` with the Guide's manifest (unbounded lines, rendered lazily, <= 24 resident). `AudioCall.Unit` + `SoundUnit.RecordUnit`; `IAudioEngine.AddOneShotPreset / PlayOneShot / ReleaseOneShot` for modules without a unit. `KnockUnit` is the stubs' placeholder sound.

## 8. Instruments, rigs, layout, grab (Core contracts)

```csharp
public enum InstrumentKind { Piano = 0, Drums = 1, Harp = 2, Table = 3, Menu = 4 }
public enum PlaySurface { None = 0, PianoKeys, Drums, HatStand, HarpStrings, Table, Menu }
public interface IInstrument {
    InstrumentKind Kind { get; } int TickOrder { get; } bool AcceptsStickPointers { get; } bool IsPresent { get; } Transform Root { get; }
    void Tick(IReadOnlyList<Pointer> pointers, float dt); void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow);
    PlaySurface PlaySurfaceAt(Vector3 world); IReadOnlyList<Renderer> PlayedParts { get; }   // never grab handles
}
public struct PianoKeyInfo { public int Midi; public float X; public bool Black; public string Label; }
public interface IPianoApi { float W, BW, BTOP, L; Transform Root; bool InPlayZone(Vector3 world); bool OverKeys(Vector3 world); bool TryGetKey(int midi, out PianoKeyInfo key);
    void PressMidi(int midi, object pointerKey, float vel = 0.75f); void ReleaseMidi(int midi, object pointerKey); void PlaybackOn(int midi, float vel); void PlaybackOff(int midi); void PlaybackReleaseAll();
    void SetOnSurface(bool onTable); void ResetSurface(); int HeldKeyCount { get; } }
public enum DrumPieceType { Drum, Cymbal }
public struct DrumPieceInfo { public int Id; public string Kind; public DrumPieceType Type; public float Radius; public bool Pad; public Transform Transform; }
public struct DrumStrikeInfo { public float Pos; public string Zone; public bool Open; static Default }
public enum StickPick { None, Near, Grabbed }
public interface IDrumsApi { Transform Root; IReadOnlyList<DrumPieceInfo> Pieces; int KickId /*4*/, KickPadId /*5*/, HatId /*6*/; bool HatOpen;
    StickPick TryPickUp(HandState hand, Vector3 tip);   // stick grab; Layout calls it FIRST in its Grab pass (section 15.5)
    void SetHatOpen(bool open, HandState hand, bool playback = false); void PlaybackHit(string kind, float vel, DrumStrikeInfo strike); void PlaybackChoke(string kind);
    bool AnyStickHeld { get; } void ReleaseAllSticks(); bool EveryHandPointerFartherThan(float metres, int pieceId); }
public interface IHarpApi { Transform Root; int StringCount /*22*/; int MidiOf(int i) /*static degree table: allocation-free*/; Vector3 StringStart(int i), StringEnd(int i), PlayPoint(int i) /*harp-local*/;
    void Highlight(int i, float k = 1f); void PlaybackPluck(int i, float vel, float u = 0.35f, float speed = 0f, string mode = "through"); void PlaybackDamp(int i); }
```
Piece ids: 0 snare, 1 tom1, 2 tom2, 3 floor, 4 kick (shell), 5 kick pad, 6 hat, 7 crash, 8 ride (spec 05 section 3 table).
Play events: every instrument calls `app.Get<IRecorder>().Add(TakeEvent.X(...))` for pianoOn/pianoOff/drum/drumChoke/hat/harp/harpDamp
(recording or not: lessons and Sunny observe the `RecorderEvent` stream) and stamps `owner.LastPlayMs = Clock.NowMs`.

```csharp
public enum RigId { Piano = 0, Drums = 1, Harp = 2, Table = 3, Lessons = 4 }
public enum LayoutMode { All = 0, Single = 1 }
public interface IPlayZone { bool Contains(Vector3 world); }
public sealed class Rig { public RigId Id; public Transform Group; public readonly List<IInstrument> Instruments; public IPlayZone Zone;
    public IReadOnlyList<Renderer> Handles = Array.Empty<Renderer>();   // filled ONCE in Init (mutate that list in place); never built per call
    public Vector3 HomeAll; public float HomeYawAll; public Vector3 HomeSingle; public float HomeYawSingle;
    public int Priority; public bool InScene = true; public bool Held; }
public interface IRigRegistry { IReadOnlyList<Rig> Rigs { get; } Rig Get(RigId id); void Register(Rig rig); void Unregister(Rig rig); event Action Changed; }
public struct SeatFrame { public Vector3 Head; public float YawDeg; Vector3 Forward, Right; Vector3 Place(Vector3 home); float Yaw(float homeYawDeg); static SeatFrame From(Transform head); }
public interface ILayoutService { LayoutMode Mode { get; } InstrumentKind Current { get; } bool Locked { get; set; } bool IsTurning { get; } bool FirstArrangeDone { get; } SeatFrame Seat { get; }
    void Set(LayoutMode mode, InstrumentKind instrument); void Next(int direction); bool IsInScene(InstrumentKind instrument);
    void ArrangeAroundPlayer(bool force, bool movePiano = true); // VR: fitSeat + room box; MR (IMixedReality.Active): neither; keeps the piano's height
    void PlaceAroundPiano();   // MR, piano just placed: rigs at their VR offsets from it, settled with the piano fixed (placeAroundPiano)
    void RestoreHomes();       // leaving MR (restoreHomes)
    Pose Anchor { get; } }     // anchor(): the piano (All) or the current instrument (Single)
public interface IGrabService { Rig HeldRig { get; } bool AnyHeld { get; } Rig GrabTarget(Vector3 point, float reach = 0.07f); }
public interface IPlacementService { bool Resolve(bool useRoomBox); void SettleOne(Rig rig); void ClearPanel(Rig panel); Vector3? RoomShift(); float EyeDistance(Rig rig); }
public static class GrabConstants { PlayGuardMs 1000; GrabDwell .3; GrabStill .025; GrabSpeed .5; GrabSmooth .06; SpeedWindow .1; HintAfter .05; DwellSamples 48; HandleReach .07; ZonePad .12; UnstickMs 300;
    LiftMin −.45, LiftMax .6, LiftDeadband .03; OffHandTurnPull .12, OffHandTurnDeg 45; EyeClear .30, EyeMargin .02, Gap .01, WallSlop .005, Step .02, MaxPush 1.6; SeatHalf .2, SeatFront .1, SeatBack .3;
    PanelMinEye .3, PanelMaxRise .45; SwivelPull .14, SwivelDeg 45; TwoHandSummonSeconds .8; TurnSeconds .35, TurnSlide .9, TurnScale .85; Bounds RoomBox; Rect OriginClamp (x ±2.8, z −3.6..0.8) }
```
Rig homes (seat frame, Unity): piano `(0, .72, .45)` yaw 0 (single: same); drums `(.2, 0, −.26)` yaw 90 (single `(0, 0, .1)` yaw 0);
harp `(−.78, 0, −.2)` yaw 83 (single `(mx, 0, .45)` yaw 180, mx = mean string-midpoint x, −0.33 fallback); table `(−.39, 0, −.21)`
yaw −90; panel home `(0, 1.03, .47)` yaw 0. Instrument modules register their `Rig` with `app.Register<Rig>(rig)` in `Init`;
Layout (Order 100) collects `app.GetAll<Rig>()` and also accepts later `IRigRegistry.Register`. `Rig.Handles` lists the
visible meshes whose bounds are grab handles (played parts excluded): piano case/stand/rest; kit shells/stands (not heads,
cymbals, sticks); harp frame (not strings); table slab/legs (not controls). The instrument fills the list once in `Init`;
Layout's per-frame `GrabTarget` and the placement footprints only read it (0 B per frame). `Rig.Group` is what Layout moves; instrument
geometry hangs under it. Instruments are muted while their rig is held (Core skips `Tick`) and should fade hover.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): **the registry** (`Core/Contracts/Registry.cs`): each instrument module ships one `IInstrumentDefinition` returning an `InstrumentDescriptor` (identity, `SeatSpec`, `StowSpec`, tools, `AudioSpec`, `EventTypeSpec`s, `LessonSpec`, `TipSpec`s, features, anchors, options, portrait, MR, shadow, budgets). The id block: `InstrumentKind` / `RigId` 5-9 = Handpan, HandDrums, Marimba, Theremin, Bowls (`RigOf(kind) = (RigId)(int)kind`). New contracts: `IStowable`, `IInstrumentPlayback`, `IGuideAnchors`, `IHandToolProvider`, `IInstrumentSurface`, `IPointerClaimant`, `PlaySurface.Instrument`, `IPianoApi.MusicRestPose / MusicRestLocal / MusicRestLedge`; `ILayoutService` gains (default-bodied) `MySet`, `SetVersion`, `CanAdd`, `PreviewJoin`, `ReplaceCandidate`, `TrySetSet`, `Bring(kind, reason)`, `PutAway`, `PlaceOf`, `SlotOf`, `Facing`, `IsMoving`, `AnyMoving`, `Lead`, `TryGetDisplayPoint`, `PlaceAroundLead`. **Layout:** My set (up to 3 + the side table, `musicroom.set`, default piano + drums + harp at exactly today's homes) planned by `Layout/SeatPlanner.cs` (it follows the seat-planner Node reference; the vectors in `Tests/EditMode/Layout/Reference~/platform.vectors.json`); the side table yields to its alternate pose behind-left only when needed. Every instrument not at the seat stands on display in the VR room at its descriptor's spot (`Layout/StowPlan.cs`; `IStowable.SetStowed`; not present, not ticked, silent; hidden in MR; blocked spots use the spares). `Bring` from the menu grid, the page turn (carousel piano, harp, theremin, bowls, handpan, marimba, hand drums, drums), point-and-pinch (`Layout/RaySummon.cs`, a 6 deg cone pick, 0.35 s hold) and lessons; instruments glide 0.9-1.4 s on an arc >= 0.6 m from the head (`Layout/RigGlide.cs`) with `InstrumentMoveEvent { Leaving, Arrived, Displayed }`. Tools through `IHandToolProvider` (the kit's `DrumToolProvider`). Arrange mode lifts by the descriptor's `LiftMin/LiftMax` and moves displayed instruments (`musicroom.stow.<id>`). Arrangements merge on save. Layout publishes the "layout.*" signals Sunny's platform tips listen to.

## 9. Menu (Core contract; Menu module implements)

```csharp
public enum MenuScreen { Home = 0, Lessons, Courses, Lesson, Instruments, Recordings, Take }
public enum MenuChange { Open, Close, Screen }
public interface IMenuPanel { bool Shown { get; } MenuScreen Screen { get; } void Show(HandState hand = null); void Hide(); void Toggle(HandState hand = null); void Go(MenuScreen screen); void Redraw(); Bounds? GrabZone { get; } Rig Rig { get; } Transform Root { get; } }
public static class MenuConstants { Radius .8; Width .32; Height .24; CanvasW 1024; CanvasH 768; Arm .003; Hover .010; Slop .008; Behind −.06; EdgeMargin .01; DebounceMs 250; SummonSeconds .5; SummonMinGrasp .15; SummonMinDist .08; SummonMaxDist .8; SummonCos .7660; SummonQuietMs 400; Vector3 HomeSeat (0, 1.03, .47) }
public struct MenuEvent { public MenuChange Change; public MenuScreen Screen; }   // published on every open / close / screen change
```
Home rows (spec 08 section 1.5, modified): Lessons · "29 courses"; Instruments · status; Recordings · "N saved"/"Saving off";
**Room** · "Play in my room" · "Passthrough" (VR) / "Music room" · "Virtual room" (MR), calls `IMixedReality.Enter()/Exit()`;
"Show me around" · "Sunny" / "Hide Sunny" · "Stop the tour" (`IGuide.Toggle`). With five rows: h 100, gap 12, from y 132. In MR
with `IMixedReality.Status == NoRooms` a sixth row **Setup** · "Set up my room" · "Space Setup" (`RunSpaceSetup()`): six rows h 84,
gap 10 from y 132. All other screens, lists, pokes, scrolling, animation, clear-out: spec 08 sections 1.1–1.7 verbatim; pokes use
`HandState.Pointers[0]` only. Row data sources: `ILayoutService`, `ILessonLibrary`, `ITutorial`, `IRecorder`, `ITakeLibrary`,
`IGuide`, `IMixedReality`. Redraw triggers: `TutorialEvent`, `RecorderStateEvent`, `GuidePresenceEvent`, `LayoutChangedEvent`,
`LibraryChangedEvent`, `MixedRealityEvent`. Hidden on `HmdMountEvent{Mounted=false}`. Desktop: M toggles while
`!App.IsPresenting` (already in the stub); a desktop click on the face collider (`Layers.PlaySurface`) is the mouse path
(desktopDown), and the pokes skip `Pointer.FromMouse`. As an `IInstrument` the Menu is ALWAYS present
(`IsPresent` true): its `Tick` runs the summon gesture and the open/close animation first and
then returns while hidden (`if (!Shown) return;` before the pokes), so the summon works and the close animation finishes.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): the Instruments screen is a tile grid of the registry in carousel order (portrait, name, "With you" / the display spot's `Where` / "Put away" in MR, My set ticks; My set | One alone, Lock, Classic sound), pokes per platform.md 5.1 (Bring, PutAway, the replace note, "No room: take one out first"); runtime portraits (`Core/Render/PortraitStudio.cs`: a 1024 x 512 atlas rendered on layer 30 with `SubmitRenderRequest`, one per frame during the start panel; vector fallback from the descriptor); the Lessons screen is the same grid; instrument option rows (`InstrumentOption`, `InstrumentOptionEvent`); `SeatSpec.PanelHome`; `MenuRowEvent` (Core) carries "inst.<id>"; the menu publishes "menu.instruments" / "menu.instruments.set".

## 10. Lessons, recorder, takes (Core contracts; Lessons module)

```csharp
[Serializable] public struct TakeEvent { public float t; public string type; public int midi; public float vel; public string kind; public float pos; public string zone; public bool open; public int i; public float u; public float speed; public string mode;
    static PianoOn(midi, vel), PianoOff(midi), Drum(kind, vel, pos, zone, open), DrumChoke(kind), Hat(open), Harp(i, vel, u, speed, mode), HarpDamp(i) }
public static class TakeEventType { "pianoOn", "pianoOff", "drum", "drumChoke", "hat", "harp", "harpDamp" }
public enum RecorderState { Idle, Recording, Playing }
public interface IRecorder { RecorderState State; bool External; void Add(in TakeEvent ev); void Record(); bool Play(); bool PlayEvents(List<TakeEvent> events, double seconds); void Stop(); List<TakeEvent> Take; double TakeSeconds; event Action<List<TakeEvent>, double> OnTake; }
public enum TutorialState { Idle, Countdown, Playing, Waiting, Done }
public struct TutorialSummary { public int Notes, Mistakes; public float Seconds; }
public interface ITutorial { TutorialState State; string LessonId; InstrumentKind Instrument; int Version; float Countdown; string Hint; float Progress; int EventsDone, EventsTotal; TutorialSummary Summary; void Start(string lessonId); void Stop(); }
public interface ILessonLibrary { IReadOnlyList<Lesson> Lessons; Lesson Get(string id); int Count(InstrumentKind instrument); LessonTutorialConstants Constants; }
public interface ITakeLibrary { bool? Available; int Version; event Action Changed; Task<List<TakeInfo>> List(); Task<TakeRecord> Get(string id); Task<TakeRecord> Put(TakeRecord take); Task<bool> Remove(string id); string FormatLength(double seconds); string FormatDate(long createdMs); }
```
Lesson data (`Lessons/Resources/lessons.json`, JsonUtility classes in `Core/Contracts/Lessons.cs`): `LessonFile { tutorial { SPEED .18,
TRAVEL { piano .35, drums .35, harp .30 }, leadSeconds { 1.9444, 1.9444, 1.6667 } }, counts { 29, 13, 9, 7, 826, 966 }, lessons[] }`;
`Lesson { id, instrument ("piano"|"drums"|"harp"), title, bpm, secondsPerBeat, leadSeconds, beats, seconds, eventCount, notes, listNote, events[] }`;
`LessonEvent { k, beat, len, time, duration, due, hint, targets[] }`; `LessonTarget { midi (piano/harp, −1 absent), note, kind (drums), label, i (harp), color ("red"|"blue"|null) }`.
Recorder (implemented, exactly per spec 08): `Add` publishes `RecorderEvent { Event, DuringPlayback }` always; records only while
recording (≤ 20000 events, ≤ 600 s); `PlayEvents` plays a saved take and `Stop` restores the deck's own take; `Update()` is called
in the Recorder phase. Stage 3 wires playback targets (`Recorder.SetTarget(type, handler)`, `SetReleaseAll`) to the instrument APIs
and builds the deck. Takes: `persistentDataPath/takes/index.json` + `<id>.json` (atomic `.tmp` rename), ids `take-<base36 ms>-<base36 rand>`,
names `"Take N · m:ss"` (U+00B7), `nextNumber` persisted. "Saving off" when `Available == false`.

Tutorial rules (spec 08 section 2): SPEED .18 m/s, TRAVEL .35/.35/.30, `due_k = lead + beat_k·60/bpm`, EARLY .15 s, COUNTDOWN 3 s,
pool 64 blocks, HIT_FADE .3, WRONG_FLASH .3; hits come from `RecorderEvent` with `DuringPlayback == false`; ignore events of other
instruments; publish `TutorialEvent` on state/version changes; block geometry per instrument from `IPianoApi.TryGetKey`,
`IDrumsApi.Pieces`, `IHarpApi.PlayPoint`; glows via `HoverGlowPool.Add` in the Tutorial phase; `Layout` stops a lesson whose
instrument left (`LayoutChangedEvent`).

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): `TakeEvent.lane`; event roles from the registry (`EventRole` Onset / Release / Control / State / Marker, `EventTypeSpec.Held` and `Sampled`); `Recorder.MaxEvents` 40000, `IRecorder.Add(ev, atAudioTime)`; `PlaybackWiring` routes every descriptor type to `IInstrumentPlayback` (lanes: 1..6 looper layers, 7 takes; Controls dispatched up to 50 ms early; `IAudioPresence.Wake` ahead); the looper quantises onsets only, releases only Held onsets at a window end, closes Control lanes; lesson packs `Resources/LessonPacks/<id>.json` loaded after `lessons.json`; the generic tutorial path through `ILessonTargets` (B2, B4 `CountNoun`, B6), `LessonOptions`, `ILessonAccompany`, `ITutorial.CurrentEvent` / `CountNoun`; a lesson brings its instrument (`Bring(kind, Lesson)`) and counts down on Arrived.

## 11. Guide (Sunny)

`public interface IGuide { bool Present { get; } bool Done { get; } void Start(); void Dismiss(); void Toggle(); }` +
`GuidePresenceEvent { bool Present }`. Voice line ids (22, `GuideVoice.LineIds`): intro_vr, intro_desk, open_vr, open_desk, poke_vr,
poke_desk, close_vr, close_desk, swipe_vr, swipe_desk, swipe_skip, nod_vr, nod_desk, nod_skip, skip, praise_0..praise_4, bye, leave.
Lines are 22.05 kHz mono WAVs in `Guide/Resources/Voice/` with `manifest.json` (seconds); `IAudioEngine.VoiceLineSeconds(id)`
gives the dry length (`lineSeconds = seconds + 0.14`). Speech: `Chirp(Open)` then `PlayVoiceLine(id, Now + 0.14)`; `LineBusy`
while `Now < end + 0.05`; natural end → `Chirp(Close)`; `StopVoiceLine()` on Dismiss/Vanish/unmount. Mouth: `VoiceLevel` with the
peak follower of spec 08 section 3.4. First launch: `GuideModule` starts the tour when `ILayoutService.FirstArrangeDone` and
`!AppSettings.TourDone`; `Dismiss`/finish sets `TourDone`. Tour steps complete on `MenuEvent` (open/screen/close),
`LayoutChangedEvent` (swipe), `NodEvent` or a kick `RecorderEvent` with `IDrumsApi.EveryHandPointerFartherThan(0.2, KickPadId)`.
The Guide audio source follows Sunny's root (`SetSourceParent(SourceId.Guide, root, zero)`).

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): tips are data (`TipSpec` in each descriptor, plus `IGuideTipSource` classes: the platform's own six in `Guide/PlatformTips.cs`, with clips: `tip_bring_grid`, `tip_summon_far`, `tip_my_set`, `tip_set_full`, `tip_put_away`, `tip_page_turn`); triggers FirstArrival, FirstDisplayedGaze, IdleNear, Signal, Manual, LessonStart; successes EventType, Signal, None, Facing, LessonHit; the merged line files (`Resources/GuideLines/*.json`); `DemoKind` moved to Core with the platform's keyframed presets and `Custom` (`DemoMotion`); anchors through `IGuideAnchors` (the tip's instrument first).

## 12. Mixed reality

```csharp
public enum MrStatus { Idle, Loading, Loaded, NoPermission, NoRooms, NotAvailable, Failed }
public struct MrWindow { public Guid Id; public Vector3 Center; public Vector2 Size; public Quaternion Rotation; public Vector3 Outward; public bool Manual; }
public interface IMixedReality { bool Available; bool Active; bool IsPlacing; MrStatus Status; bool HasSpaceSetup; IReadOnlyList<MrWindow> Windows; Task Enter(); void Exit(); Task RunSpaceSetup(); bool TryGetPianoPose(out Pose pose, out bool onTable);
    void StartPlacing(); }   // called by Layout (Grab step 5); IsPlacing until the piano lands
public struct MixedRealityEvent { public bool Active; }
```
Module-internal APIs already in place (verified sketch): `MrScene` (`Status`, `StatusChanged`, `EnsureMruk()`, `HasScenePermission`,
`RequestScenePermissionAsync`, `LoadDeviceSceneAsync`, `RunSpaceSetupThenReloadAsync`, `LoadJsonSceneAsync(json)`,
`LoadPrefabSceneAsync(prefab)`, `SetWorldLock(bool)`, `Clear()`), `WindowFinder` (`Windows`, `HasScene`, `WindowsChanged`, `Rebuild`,
`Collect`, `TryGetWindowRect`, `TryManualWindowOnWall(room, ray, out)`, `ManualWindowAt(point, inwardNormal, floorY)`),
`TableFinder` (`TryFindBest(room, head, fwd, TableQuery.Default, out best)`, `TryDescribe`, `TryPointAtTable`, `TryPointAtSurface`,
`PianoPoseOnTable(best, point)`, `FloorY(room)`). Flow (implemented in `MrModule.Enter/Exit`): passthrough on → room hidden →
fire/wind off (snow stays) → `SetMixedReality(true)` → permission + `LoadSceneFromDevice(false, true)` → world lock on if Loaded →
`SetRoomBox(room bounds)`. `Enter` first waits (≤ 2 s, `MrModule.PassthroughWaitSeconds`) while the start-up passthrough init is
still pending (until it completes the underlay is not submitted and the hidden room would show black); `RunSpaceSetup` needs
the headset (a no-op without XR). Static events (`MrScene.StatusChanged`, `WindowFinder.WindowsChanged`) are unsubscribed in
`Shutdown`.

**MR and Layout** (the MR layout lives inside Layout and the grab code):
MR decides when, Layout moves the rigs.
1. *Auto-arrange* (autoArrangeAR): on the first posed frame after `Enter` (MixedReality phase) MR calls `IPianoApi.ResetSurface()`,
   puts the piano back at its home height, and calls `ILayoutService.ArrangeAroundPlayer(true)`; while `IMixedReality.Active`
   Layout skips fitSeat and the room box (placement `inRoom = !ar.active`). MR then poses the shadow catcher and the main light's
   target at `ILayoutService.Anchor` (and again after every later arrange, `ArrangedEvent`).
2. *Placing*: Layout's Grab step 5 — a dwell-complete pinch with no rig under it, while `Active`, the piano in the layout and the
   point outside every zone — hides the other rigs and calls `IMixedReality.StartPlacing()`. While `IsPlacing`: no instrument
   ticks (the menu included), Layout's grab pass returns at once, two-hand summon and the page turn pause, the nod kick is off.
   MR runs the reticle and the placing pinch in its MixedReality phase: the best table (`TryGetPianoPose`: `TopY + 0.092`, pushed
   0.25 m away, facing the user, stand hidden via `IPianoApi.SetOnSurface(true)`) else the floor on its stand (y + .72), poses the
   piano, its catcher and light, and calls `ILayoutService.PlaceAroundPiano()` (the rigs reappear at their VR-room offsets from
   the piano, settled with the piano fixed). It clears `IsPlacing` only in its `EndFrame` phase, so the Grab phase of the landing
   frame still sees it and the placing pinch never arms a grab (`placedThisFrame`).
3. The seat swivel is off while `Active`; the two-hand summon stays on in MR (it re-arranges around the player)
   except while placing or with the piano hidden.
4. *Exit*: `ILayoutService.RestoreHomes()`, then `ArrangeAroundPlayer(true)` (Unity stays in XR after leaving MR).

Stage 3 also adds: portals keyed by `MrWindow.Id`, created through Room's `ISnowPortals.Create(mrRoot, size)` (Room owns the snow
field mesh and shader, SnowField N 3000, depth [.06, 8], #f4f7ff, and the frost, reach .12 at local −.004; MR poses each portal with
`SetPose` and feeds `SetFrost(max(cover, .4 level), .1 level)` every frame from `ISnowWeather`; Room keeps SnowWeather and the
`_Snow*` globals running while the room is hidden); the manual 1.0 × 1.2 m window (wall ray t ∈ (.05, 12], centre y clamped
[0.75, 2.1], 5 mm off the wall; `EnvironmentRaycastManager` fallback with |normal.y| < .35) after 2.5 s of snow without a window;
MR lighting (Trilight #b1b1b1/#969696/#686868, white main light 0.414 at (0.6, 2.4, −1.2) looking at the anchor, one 1024 **hard**
shadow map (soft shadows stay off in QuestURP), `shadowCastingMode = Off` on every renderer except the instrument bodies, shadow
catcher quad 2.6 × 1.8 α .32 with MR's own `MusicRoom/ShadowCatcher` shader (`MR/Shaders/`, URP `_MAIN_LIGHT_SHADOWS*`,
Transparent, ZWrite Off) and template `MR/ShadowCatcher` from MR's `IMaterialTemplateProvider`); reload on
`OnApplicationFocus(true)`. Never move `MRUKRoom`/`MRUKAnchor` transforms; keep portals
under the MR root and copy poses. Editor testing: `MrScene.LoadJsonSceneAsync(AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json/MeshLivingRoom2.json").text)`.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): "the lead" (`ILayoutService.Lead`: the My set member in slot Front, else the first in carousel order; One alone: Current) replaces "the piano" in the MR flow: auto-arrange, placing, `PlaceAroundLead`, the catcher and the light. `IMixedReality.TryGetLeadPose`: an `Mr.OnTable` lead stands on a real table lifted by `SurfaceLift` (`IInstrumentSurface.SetOnSurface`), else on the floor. Displayed instruments are hidden in MR. Draw guard: sum of `DrawsAtSeat` + 37 <= 80, else the last member is put away ("mr.noroom"). The piano's E9 anchors stay piano-only.

## 13. Room (Core contract; Room module)

```csharp
public enum AmbienceKind { Fire, Wind, Snow }
public interface IRoom { Transform Root; bool Visible; void SetVisible(bool visible); void SetCandles(bool on); Bounds RoomBox; }
public interface IAmbienceSwitches { bool Get(AmbienceKind kind); void Set(AmbienceKind kind, bool on); }   // flips lever + IAudioEngine.SetX + AmbienceEvent
public struct SnowGust { public float Strength, Attack, Hold, Release; float Duration; }
public interface ISnowWeather { float Target, Level, Cover, Wind, WindVel; event Action<SnowGust> OnGust; }  // Room subscribes the audio: engine.SnowGust(...)
public interface ISnowPortals { ISnowPortal Create(Transform parent, Vector2 size, bool portalPreset = true); }  // MR's snow behind real windows
public interface ISnowPortal { Transform Root { get; } void SetPose(Vector3 center, Quaternion rotation, Vector2 size); void SetFrost(float frost, float veil); void SetVisible(bool visible); void Destroy(); }
```
Room keeps its Decor phase (SnowWeather, the `_Snow*` globals, the snow audio gusts) running while `SetVisible(false)` hides the
room in MR: the portals are drawn with the same shader and globals. The stub returns an empty, posed portal root.
Lighting (spec 07 section 6): main light = moon `#a9bbff` 0.175 Euler (24.05, −68.40, 0), no shadows; points: floor lamp `#ffd6a0`
1.08 range 5 at (−2.4, 1.6, 0.52); fire `#ff8f3c` 2.07 range 6 at (0.3, 0.5, −3.63) flicker .22; candles `#ffc070` 0.13 range 1.8
(table (0.93, 0.435+h+.08, −3.15), mantel M (0.10, 1.27+.14+.08, −3.78)) flicker .3. `RenderSettings.ambientMode = Trilight`, sky
#747578, equator #69696a, ground #4c4c4b. One `ReflectionProbe` (realtime, via scripting, 128 px) at (0, 1.2, −1.48), rendered once.
Materials → URP Lit: roughness r → smoothness 1 − r; metals via `LitMetal` template. Custom shaders (`Room/Shaders/*.shader`,
names `MusicRoom/Flame`, `MusicRoom/Coals`, `MusicRoom/Snow`, `MusicRoom/Frost`, `MusicRoom/NightSky`, `MusicRoom/NightGround`,
`MusicRoom/FloorDecal`, `MusicRoom/Billboard`; the MR shadow catcher is MR's own shader): URP `UniversalForward` pass, stereo instancing macros
(`UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID`, `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO`),
`float` precision for hashes, blending per spec 07 section 4.1 (`Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha` or
`SrcAlpha One, One One`), ZWrite Off, outputs through `SRGBToLinear` for colours written without colour conversion, globals `_FireTime`,
`_SnowTime`, `_SnowIntensity`, `_SnowWind`, `_SnowWindVel`, `_SnowCover`, `_SnowPx`. Each custom shader gets a template via the
module's `IMaterialTemplateProvider`. Textures: the shared set is the foundation's (`Core/Render/ProcTex.cs`, `SharedTextures.Get`:
`Core/wood_oak`, `Core/wood_oak_rough`, `Core/wood_walnut`, `Core/wood_walnut_rough`, `Core/wood_maple`, `Core/wood_maple_rough`,
`Core/wood_spruce`, `Core/wood_rosewood`, `Core/wood_stand`, `Core/drum_head`, `Core/cymbal`, `Core/cymbal_rough`, `Core/grille`,
`Core/soft_dot`; baked through `CoreTextureProvider`); the room's own through its `ITextureBakeProvider` +
`GeneratedAssets.Texture(name, generate)` on `ProcTex.Hash/VNoise/Fbm` (names `Room/plaster`, `Room/rug`, `Room/leaf_light`,
`Room/leaf_dark`, `Room/brick`, `Room/weave_normal`, `Room/gradient_strip`, `Room/scenery_1..3`, `Room/night_sky`, `Room/hill_snow`).
Draw-call target: VR room ≈ 34 draws (37 with snow); merge static geometry per material (`Mesh.CombineMeshes`).

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): `IRoomFurniture` (`Room/World/Furniture.cs`): the furniture blockers measured from the built geometry (display spots keep 3 cm clear); the rig shadows are ONE combined mesh from the descriptors' `ShadowSpec` (was 3 draws).

## 14. Rendering conventions

- **Materials**: `app.Materials.Clone(template)` / `Lit(color, smoothness, metallic, map)` / `LitEmissiveMaterial` / `UnlitMaterial` /
  `Additive(texture, color)`; templates by name (`MaterialLibrary.LitOpaque`, `LitEmissive`, `LitTransparent`, `LitAlphaTest`
  (both faces, cutoff .5), `LitMetal`, `Unlit`, `UnlitTransparent`, `UnlitAdditive`, `ParticlesUnlitAdditive`, `ParticlesUnlitAlpha`,
  `HandGhost`, plus `"<Module>/<Name>"`). Set keywords and render state on templates (in the provider), only colours/textures/floats
  on clones. Property ids: `MaterialLibrary.BaseColor (_BaseColor)`, `BaseMap`, `EmissionColor`, `Smoothness`, `Metallic`, `BumpMap`,
  `Cutoff`, `Alpha (_Alpha, HandGhost)`. **Never set a `MaterialPropertyBlock` on a URP Lit/Unlit renderer**: it takes the
  renderer out of the SRP Batcher (slow per-draw setup on Quest's render thread). Per-state colour is a small set of shared
  material clones (idle / pressed / target-red / target-blue / ...) swapped through `sharedMaterial`, vertex colours in a custom
  unlit shader or URP Particles/Unlit (URP Lit and Unlit ignore vertex colours), or `Graphics.RenderMeshInstanced` with a custom
  instanced shader. (The only block in the app is the hand ghost's `_Alpha` on its custom CG shader: two transparent draws that
  never batch anyway.)
- **Hover glow**: `HoverGlowSpec.At(pos, normal, length, width, strength, gain = 1, steady = false)` with optional `Axis`;
  `HoverGlowPool.Add` returns false when the 16 slots are full (call order: instruments, tutorial, sticks). The 16 quads are ONE
  dynamic mesh (vertex positions + colours rewritten each frame, `ParticlesUnlitAdditive` + the shared soft dot): one draw, and
  the renderer is off when no glow is shown.
- **Draw budgets** (one draw per renderer × material; single-pass instanced draws both eyes at once): VR room ≈ 34 (37 with
  snow); piano ≤ 10 (keys as one 88-bone `SkinnedMeshRenderer`, or two `RenderMeshInstanced` calls, white and black, with
  per-instance matrices and colour; the 19 labels in one atlas mesh; case, stand and rest combined per material); drums ≤ 25
  (static shells and stands combined per material; heads and cymbals separate because they wobble); harp ≤ 6 (all 22 strings in
  one mesh rewritten with one `SetVertices(NativeArray)` per frame, glow as vertex colour in an unlit custom shader; pins
  combined); table + tape deck + switches ≤ 15; menu ≤ 4; Sunny ≤ 6; tutorial blocks 1 (`RenderMeshInstanced` only); hover
  glows 1; hands 2 (the debug joint spheres exist in the editor only). Whole scene ≤ 150; MR ≤ 80, with `shadowCastingMode =
  Off` on everything except the instrument bodies. Guard: `Tests/PlayMode/Core/DrawBudgetTests` (`test-play --gpu`, the
  profiler's `Draw Calls Count`).
- **Text**: `TMPro.TextMeshPro` world-space (LiberationSans SDF; shader `TextMeshPro/Mobile/Distance Field`; bubble:
  `TextMeshPro/Mobile/Distance Field Overlay`); never legacy `TextMesh` except as a fallback.
- **Generated textures**: `GeneratedAssets.Texture("<Module>/<name>", () => ...)`, the shared set `SharedTextures.Get(name)`
  (Core `ProcTex`: Hash / VNoise / Fbm, Wood, DrumHead, Cymbal, Grille, SoftDot, fingerprinted against golden values); bake with
  `Tools/unity.sh bake` (`Assets/MusicRoom/Generated/Resources/Generated/<Module>/<name>.png`, ASTC 6×6, sRGB unless `Linear`,
  mips, Repeat when asked). Generators are CPU only (the bake runs `-nographics`); mark a GPU one `NeedsGpu` (refused there).
  Labels and text are TextMeshPro, never a generated texture; printed pages (the music book) rasterise the TMP font's
  own outlines (LiberationSans, generated from the TTF: `Sheet/Resources/sheet-font.bytes`) into the page texture
  (sheet R1, applied at the integration).
- **Layers** (`Core/Util/Layers.cs`; names written by `configure`): 29 `PlaySurface` (desktop click colliders, section 0),
  30 `Hidden`, 31 `PanelUI` (the menu's offscreen canvas). The head camera never draws 30 / 31.
- **URP asset** (`Settings/QuestURP.asset`): Forward, MSAA 4, HDR off, no depth/opaque texture, render scale 1, upscaling Auto,
  shadow distance 4 m, 1 cascade, 1024 main-light shadow map (MR only, hard), 4 additional per-pixel lights, no additional/soft
  shadows, SRP Batcher on. Quality levels all point at it. Foveation: `MetaQuestFeature.m_foveatedRenderingApi = SRPFoveation` +
  Unity's `FoveatedRenderingFeature` on (Meta's `MetaXRFoveationFeature` / `MetaXREyeTrackedFoveationFeature` off: they pair with
  the Legacy API) + at runtime `RigBootstrap.ApplyFoveation(1)` (`XRDisplaySubsystem.foveatedRenderingLevel = 1`, flags None).
  The SRP API has no dynamic foveation.
- **Camera**: never add post-processing; keep `allowHDR false`; VR background `#05070f` α 1, MR `(0,0,0,0)` (`XrMode.SetPassthrough`).
- **Transparent sorting**: glass 3000, snow +2, frost +4, labels ≈ 2990, decals after the floor with `Offset −1,−1`.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): portraits render on layer 30 (`Layers.Hidden`) with a camera that the head camera never sees; the phase-0 placeholder instruments are neutral wood blocks (`MaterialLibrary.Lit` with the shared maple / walnut textures). The menu's portrait cells are blitted like its other images (command buffer).

## 15. Stage-3 checklists (per module, with spec pointers)

### 15.1 Piano (`Assets/MusicRoom/Piano`, spec 05 §2 + data `piano`, `curves`)
- Geometry from `piano.build`: 88 keys (pivot at the hinge `(x, 0, .15)`, `localRotation = Euler(−θ°, 0, 0)`, θ = depth·MAX_ANGLE),
  side shading as vertex grey → bake into a texture or a palette (URP Lit ignores vertex colour), ivory #f4efe3 r .3 / ebony #0e0e10 r .24,
  case/panel/grille/music rest/stand from `bodyBoxes`/`build` (grille and stand wood: `SharedTextures` `Core/grille`,
  `Core/wood_stand`), labels (19 atlas cells drawn with TextMeshPro into one atlas texture at RUNTIME, no baked copy: the bake
  runs `-nographics`, where a camera render writes a blank PNG that would then shadow the runtime one), middle-C bar.
- Draw budget ≤ 10 (section 14): keys as one 88-bone `SkinnedMeshRenderer` or two `RenderMeshInstanced` calls with per-instance
  colour (no MaterialPropertyBlock); labels one mesh; body parts combined per material.
- Contacts (§2.3): `Track/Follow/EntryKey/WithinKey/KeyAt/FingerDepth/DownSpeed/StrikeVelocity` verbatim; glissando; early fire on sparse
  tracking; `PressKey/FingerUp/ReleaseKey/Damper/Damp`, automatic sustain (PHRASE_GAP .6, MAX_RING 6), `KeyShape`, the 54 rad/s spring.
- Audio: `Piano(midi, vel)` → keep `VoiceHandle` per key; `Release(h)` on damper (thud only for the first of a lift); re-strike releases first.
- Recorder: `pianoOn{midi, vel}` / `pianoOff{midi}`; `LastPlayMs` stamps. Hover glows (§2.6). Play zone / `OverKeys`.
- Desktop (section 0): key colliders on `Layers.PlaySurface`; click .7, drag glissando .6 when the nearest `PlaySurface` hit is a
  key; keyboard row via `DesktopInput.PianoRow` (`PressMidi(base + i, keyObject)`), Z/X octave; the contact logic skips
  `Pointer.FromMouse`.
- `PlayedParts` = keys + labels; `Rig.Handles` = case, stand, rest. Register `Rig` (RigId.Piano, priority 0). `SetOnSurface` hides the stand.
- Tests: key x table vs spec 05 `piano.keys`, `StrikeVelocity` samples (.07→.10, .2→.38, .5→.62, 1→.84), `KeyShape` samples (`curves.keyShape`),
  spring rest/damper times, a scripted finger press with `FakeHandSource` fires once and sustains.

### 15.2 Drums (`Assets/MusicRoom/Drums`, spec 05 §3-4, §7 + data `drums`, `sticks`, `sticks_build`, `curves`)
- Pieces/stands/kick extras from `drums.build`, `drums.stands`, `kickExtras_unity`; hi-hat lower cymbal y −.016/−.05 with 18·dt lerp.
- Hit test per piece × pointer (arm/disarm, crossing lerp, rim +3 cm, `v0/v1` .3/3 sticks vs .12/1.5 fingers, `^.7`), `StrikeAt`,
  `Hit` (3 ms dedupe, 45 ms per owner), choke, hi-hat stand pinch, visuals (flash, wobble), hover. `drum{kind, vel, pos, zone, open}`,
  `drumChoke`, `hat` events. Audio: `Drum(DrumStrike.ParseKind(kind), vel, strike)`; hat open → `HatOpen` kind; closing → `Choke(Hat)` + `HatClose .7`.
- Sticks (§4): mesh, rest X, external pointers (`Pointer.CreateExternal(.., stick: true/shoulder: true)` registered with
  `app.Input.RegisterExternal`), `UpdatePoses` (grip formula, TILT −.22) in `StickPoses`, pick-up = `IDrumsApi.TryPickUp(hand, tip)`
  (stick grab: `None` without a free stick near or with the kit out of the scene; `Near` within the .24 dead zone; `Grabbed`
  within .12, which sets `HandState.Holding`, reparents the stick keeping its world pose and sets its pointers `Down`). Layout calls
  it first in its Grab pass and consumes the pinch on `Grabbed`, or on `Near` unless a rig handle is within .02 (section 15.5);
  Drums never decides the consumption itself. Tracking-loss grace 1.5 s, release in `StickReleases` (open .05 for .25 s), settle
  .4 s, hover in `StickHover` (not while placing), `ReleaseAll` when the kit leaves (`LayoutChangedEvent`).
- Nod kick (§3): `NodKick.Tick` in `NodKick` phase using `app.Head.forward`, only while `App.IsPresenting`, not placing and no rig
  held; publish `NodEvent`.
- `DrumRoom` source stays at the kit centre (already parented). `PlayedParts` = heads, cymbals, sticks; `Handles` = shells, stands.
- Draw budget ≤ 25 (section 14). Desktop (section 0): drum-top and cymbal colliders on `Layers.PlaySurface`, the fixed click
  velocities; the hit test skips `Pointer.FromMouse`.
- Tests: strike zones for `pos`, velocity curves (`curves.drumFinger/drumStick`), a scripted stick pick-up and strike (call
  `TryPickUp` as Layout does), choke timing; assert the sound on `AudioTestTap.Calls` (`AudioCall.Drum` kind / vel / strike).

### 15.3 Harp (`Assets/MusicRoom/Harp`, spec 05 §5 + data `harp`, `harp.build`)
- Geometry: soundboard, neck (centripetal Catmull-Rom, tension .5, radius taper), soundbox frustum, 22 strings (`a`, `b`, colours), pins at z −.03.
- `PlayPoint`, `Side/Across`, state per pointer, `Rest` (dwell 150 ms), `Stroke` (catch/bend/slip, poke vs sweep), `Pluck` (70 ms retrigger),
  palm damping (tally ≥ 2), visuals (decay; glow as vertex colour of the one strings mesh in an unlit custom shader, no
  MaterialPropertyBlock; CPU string deformation of all 22 strings in that mesh with one `SetVertices(NativeArray)` per frame), hover.
  Woods: `SharedTextures` (`Core/wood_maple`, `Core/wood_spruce`, `Core/wood_walnut`). Draw budget ≤ 6.
- Audio: `double when = delay > 0 || sweepInProgress ? engine.FrameNow + 256.0 / engine.SampleRate + delay : 0;` then
  `Pluck(i, vel, when, new PluckOptions { U, Speed, Mode })`: an isolated poke plays ASAP (section 17); only the members of a
  multi-string sweep share the block-aligned base that keeps their spacing exact. Re-pluck `Release(h, thud: false)`; `Damp(h, n)`.
- Events `harp{i, vel, u, speed, mode}`, `harpDamp{i}`. Desktop click plucks the nearest string within 2 cm (string-plane
  collider on `Layers.PlaySurface`); the stroke logic skips `Pointer.FromMouse`.
- Tests: string table (`MidiOf`, lengths vs spec 05), `velocityOf` curve, a scripted sweep plucks in order with glissando delays.

### 15.4 Room (`Assets/MusicRoom/Room`, spec 07)
- Shell (fixed floor, world-UV walls merged, ceiling, window wall pieces), furniture (bench, shelf + books palette, paintings with
  `sceneryTexture`, floor lamp, loveseat, coffee table, rug, ivy), plants (seeded 101..106), candles (`SetCandles`), blobs/AO strips,
  fireplace + fires (one 8-quad `MusicRoom/Flame` mesh), coals, glow, smoke/embers particles, flicker lights, window (frame, glass,
  sill mound), sky/ground quads, `SnowWeather` (1:1, publishes `OnGust` → `IAudioEngine.SnowGust`), snow field shader, frost, lighting, probe.
- Room-only textures on Core's `ProcTex` (Hash / VNoise / Fbm) through the Room's own `ITextureBakeProvider` (names
  `Room/<name>`); woods, soft dot and the other shared ones are `SharedTextures` (never duplicate them).
  `IMaterialTemplateProvider` for every custom shader. `ISnowPortals` for MR (snow field with the `snow.portalField` preset +
  frost under the given parent; `SetPose`, `SetFrost`, `SetVisible`, `Destroy`). Side-table rig geometry + switch panel
  (`IAmbienceSwitches`, lever ±31.5°, LED #ff8a2a×1.4; touch zone `(dx²+dz²) < .018²`, `.005 < y < .06`) as an `IInstrument` of Kind Table (TickOrder 40);
  desktop (section 0): switch colliders on `Layers.PlaySurface` (desktopDown), the touch logic skips `Pointer.FromMouse`.
- Decor phase: flicker, smoke/embers, sway, `_FireTime`, snow globals (`_SnowPx = XRSettings.eyeTextureHeight·.5` or `Screen.height·.5`);
  it keeps running while the room is hidden in MR (SnowWeather and the globals feed the portals).
- Tests: room textures against the reference formulas (as `Tests/EditMode/Core/ProcTexTests`), `SnowWeather` timeline (level, cover,
  gust envelope), `FlakeAt` mirror of the shader, a portal from `ISnowPortals.Create` follows `SetPose`.

### 15.5 Layout (`Assets/MusicRoom/Layout`, spec 05 §7-8, spec 07 §8, spec 08 §5)
- Replace the stub's `ArrangeAroundPlayer(force, movePiano)` with spec 05's `place` + `resolve` (footprints from
  `Rig.Handles` bounds in the rig frame, `clearEye`, `clearWalls`, `findSpot`, `clearPanel`), `fitSeat` with the fix (dry-run with
  clearEye, walls off → `XrMode.MoveRig`); in MR (`IMixedReality.Active`) neither fitSeat nor the room box; the piano keeps its
  height; `PlaceAroundPiano`, `RestoreHomes`, `Anchor` per section 12; `Set/Next` membership (release sticks via
  `IDrumsApi.ReleaseAllSticks`, stop a lesson via `ITutorial.Stop`), `IsInScene`, page-turn animation (0.35 s, slide 0.9, scale .85,
  **slide and scale only in v1**: no dither / opacity fade, since rig materials are opaque URP Lit clones owned by the instrument
  modules and spec 07 §8 allows it; `Swish()` at start, `TurnEvent`), persistence (already), `LayoutChangedEvent`.
- Grab, only while `App.IsPresenting` and returning at once while `IMixedReality.IsPlacing` (MR clears it
  at the end of the frame the piano lands, so that frame is skipped too: section 12); per connected hand: (1) **sticks first**: with `PinchStart` (or `GraspStart` while not playing), pointer 0
  active, the hand neither grabbing nor holding, call `IDrumsApi.TryPickUp(h, p.Tip)`; `Grabbed`, or `Near` with
  `GrabTarget(p.Tip, .02f) == null`, disarms the hand and ends its turn; (2) `IsPlaying`, `PinchPoint`, dwell (ema/history/speed),
  `GrabTarget` (menu zone first; handles = OBB distance), `GrabCandidate` rules (play surfaces via `IInstrument.PlaySurfaceAt`,
  piano key space, lock), `StartGrab`, follow with wrist yaw, lift, off-hand turn, `constrain` (1 cm march, 6 bisections, slide,
  UNSTICK 300 ms, red ring), `settleOne` on release, two-hand twist, visuals (floor ring .42–.5 #ffc46a α .5 / red, dwell ring,
  cage), `Rig.Held`, `HeldRig`; (3) step 5 with no rig under the point, while `IMixedReality.Active`, the piano in the layout
  and the point outside every zone: hide the other rigs and call `IMixedReality.StartPlacing()` (section 12).
- Swivel (`PinchStart` outside zones, `|Δx| > .14` → `XrMode.SnapTurn(±45)` + `ResetMotion`; off while `IMixedReality.Active`),
  two-hand summon (0.8 s; off while a rig is held, while placing, or with the piano hidden in MR), sweep (ring of 16
  head-frame palm samples, 1.2 m/s, cooldown .6; suppressed while grabbing, swivelling or placing) in their phases, all gated on
  `App.IsPresenting`. Hands count as "outside every zone" using `IRigRegistry` zones + menu `GrabZone`.
- Desktop: while `!App.IsPresenting`, Shift + left-drag on a rig moves it on the horizontal plane at
  the hit height (`StartGrab(rig, point, mouse)`), Alt + drag turns it: Unity yaw = yaw0 − Δx·0.012·180/π degrees (the source-frame
  yaw gains Δx·0.012 rad); pick the nearest rig whose handle bounds the mouse ray hits; while locked only the menu
  panel moves. `DesktopRigFallback` already refuses to look around while Shift / Alt is held.
- Tests (boot with `new TestBoot(simulateHeadset: true, manualFrames: true)` and step 1/72 s; section 16): resolver cases from
  spec 07 §8 [measured] (the 07 JSON has no layout / footprint block): head (0, 1.2, 0) yaw 0 in All mode needs no moves, eye
  distances piano .628, drums .529, harp .74, table .603 (±.005), no overlaps, `RoomShift()` null; Single-mode drums with the head
  at (0, 1.2, 0) and (0, 1.1, 0): without the fix the resolver pushes the kit's back to source z −1.207 (outside the back wall), the wall shift leaves the
  nearest part .29 / .237 m from the eye, and findSpot slides the kit 0.73 m right (source x .725); with the fitSeat fix the seat
  moves back instead and the kit stays centred (|x| < .05); at (0, 1.3, 0) it stays centred with no shift; dwell rejects a moving
  pinch (sample > .025 m); a still 0.3 s pinch on a handle grabs; `GrabTarget` allocates nothing
  (`Is.Not.AllocatingGCMemory()` around it); a pinch next to a stick (`Near`) never grabs the kit; sweep gesture from scripted
  palm samples.

### 15.6 Menu (`Assets/MusicRoom/Menu`, spec 08 §1 + `08-ux-visuals.json`)
- The menu is always `IsPresent` (section 9): its `IInstrument.Tick` runs the summon gesture first (`HandFrame.systemGesture`
  excluded), then the animation, then `if (!Shown) return;` before the pokes. Ring (#5fd8ff additive), `Show/Hide` pose and
  clear-out (`IPlacementService.ClearPanel`), curved face/back plate meshes + MeshCollider on `Layers.PlaySurface` (desktop click
  path), pokes (index tip only, skip `Pointer.FromMouse`; ARM/HOVER/DEBOUNCE/SLOP/BEHIND), lists/scroll/thumb, screens and actions
  (incl. the Room/Setup rows), hover glow spec, `MenuEvent`s, grab zone, hidden on unmount.
- Panel rendering (URP; spec 08 §1.3 assumed the Built-in pipeline, but under URP a synchronous `Camera.Render()` is a full
  extra pipeline pass on the main thread with a render-target switch, and hover fades would repeat it ~9 times): a 1024×768
  RenderTexture drawn by a disabled camera through `RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest
  { destination = rt })` with `cam.stereoTargetEye = StereoTargetEyeMask.None` and culling mask `1 << Layers.PanelUI` (31, the
  uGUI/TMP canvas layer; the head camera never draws it). Redraw the STATIC content only when it changes (screen, list, scroll,
  data), at most once per frame, never for hover: hover highlights and fades are uniforms of the face material (an unlit shader
  with `_HoverRect` (uv min/max) and `_HoverAlpha` on the panel's own material clone, set each frame at no cost). Wrap the redraw
  in `Profiler.BeginSample("[Menu] redraw")`; budget ≤ 1 redraw per user action. The asmdef references `UnityEngine.UI` and
  `Unity.TextMeshPro`. Draw budget ≤ 4.
- Tests: `ToSurface` math, poke acceptance cases (spec 08 §9), row hit rects from the JSON.

### 15.7 Lessons (`Assets/MusicRoom/Lessons`, spec 08 §2, §4; JSON `tapeDeck`)
- Tutorial state machine + blocks (64, `Graphics.RenderMeshInstanced` only, colour per instance in a custom instanced shader:
  one draw), hints, summary, `TutorialEvent`; playback target wiring; takes under `app.DataPath/takes`, recorder on `app.AudioTime`;
  tape deck instrument on the Table rig (`IInstrument` Kind Table, TickOrder 30, registered with `app.Register<IInstrument>`; parent it under
  the Table rig's `Group` found via `app.GetAll<Rig>()` in `Init` (Room, Order 20, registered it) and add it to that rig's `Instruments`)
  (pegs 18 mm cylinders 60–95 mm, 5 mm travel at 18/s, reels 3/4 rad/s, lamp colours; desktop: peg colliders on
  `Layers.PlaySurface`, desktopDown; pokes skip `Pointer.FromMouse`), auto-save (already),
  `OnTake` name `"Take N · m:ss"`.
- Tests: timing cases of spec 08 §9 (due₀ 1.9444, hit at due−.14, nothing at due−.16, mistake flash), block positions for the three instruments.

### 15.8 Guide (`Assets/MusicRoom/Guide`, spec 08 §3 + `08-ux-visuals.json` `sunny`, `guide-lines.json`)
- Body/face (8 pre-rendered states), corona shader, bubble (TMP overlay, fit 54→30), light, demo rig (palm/poke/swipe/nod), placement/follow,
  tour state machine (`intro → step → wait → praise/skip → bye → leave`, `Hold(id, t) = max(t, lineSeconds + .45)`, quiet .3 s), speech via
  `IAudioEngine`, mouth, blink, Dismiss/Toggle, `GuidePresenceEvent`, first-launch rule, Shift+H.
- Timing reads `IAudioEngine.Now` only (never `AudioSettings.dspTime`); the `*_vr` / `*_desk` line choice and Shift+H use
  `App.IsPresenting`; `AppSettings.TourDone` lives in the injected settings store.
- Tests: tour timing with `new TestBoot(manualFrames: true, audioClock: () => t)`: advance `t` and `App.Step(dt)` (never wait real
  time; a 12 s timeout is 864 steps of 1/72 s, milliseconds of test time), fake events through `App.Events` (praise ≥ .3 s after
  the line; swipe_skip at 12 s; Hide leaves in .8 s); the first-launch rule with a fresh store vs one with `TourDone` set; assert
  speech on `AudioTestTap.Calls` (`AudioCall.Chirp`, `AudioCall.VoiceLine` with the line id).

### 15.9 MR (`Assets/MusicRoom/MR`, spec 03, spec 07 §9)
- See section 12 (auto-arrange through `ILayoutService`, `StartPlacing` + the table placement reticle/pinch + `PlaceAroundPiano`,
  portals through `ISnowPortals`, manual window, MR lighting (hard shadows, casters limited to the instrument bodies) and the
  shadow catcher: MR's own `MusicRoom/ShadowCatcher` shader under `MR/Shaders/` and template `MR/ShadowCatcher` from MR's
  `IMaterialTemplateProvider`, focus reload, hints). `RunSpaceSetup` only with `Rig.IsXrActive`; unsubscribe every static
  event (`MrScene.StatusChanged`, ...) in `Shutdown` (tests boot the app many times).
- Tests (PlayMode, JSON rooms): `WindowFinder.Collect` on MeshLivingRoom2 (5 windows), `TableFinder.TryFindBest` on MeshOffice1 (0.74 m desk),
  manual window placement from the room centre, portal rotation has +z outward. Start from the verified
  sketch test `MrFindersTests.cs` (6 UnityTests against the same `MrScene`/`WindowFinder`/`TableFinder` sources,
  which are byte-identical in `Assets/MusicRoom/MR`): it lives in `Tests/PlayMode/MR/` (namespace `MusicRoom.Tests.PlayMode.MR`), it
  needs no Interaction SDK (the sketch asmdef's `Oculus.Interaction*` references were never used), and reset
  `LogAssert.ignoreFailingMessages = false` in its TearDown so it cannot hide errors in other tests.

### 15.10 Audio (`Assets/MusicRoom/Audio`): section 7.5.

---------------------------------------------------------------------------------------------------------------------------

## 16. Testing conventions

- EditMode (`Tests/EditMode/<Module>/*.cs`, namespace `MusicRoom.Tests.<Module>`): pure logic, data tables, DSP against the 06
  fingerprints (spec 06 §12: `Mulberry32(1)` first five values, `NoiseTable.Generate(48000, 1)` fingerprint,
  `HarpRenderer.RenderOld(midi, 48000, 2000 + midi)` bit-exact, `PianoRenderer.Render(midi, 48000, 1000 + midi)` to ~1e-4,
  IRs with seeds 2/3/5/6, compressor static curve / impulse). Editor-only code (`MusicRoom.Editor`) is testable here (SceneBuilder).
- Measured reference tables are measurements of the RUNNING algorithm with a stated test signal; reproduce the signal, not a
  shortcut formula. Example: the 06 compressor `staticCurve` is the steady output-peak / input-peak of a 1 kHz sine after
  0.5 s; `CompressorTests.SteadySineGainMatchesTheMeasuredCurve` drives `DynamicsCompressor.Process` in 256-frame blocks and matches
  all 17 levels to 0.001 dB, while the ripple-free `StaticGainDb` helper is up to 0.08 dB low at 0 dBFS (diagnostic only).
- PlayMode (`Tests/PlayMode/<Module>/*.cs`, namespace `MusicRoom.Tests.PlayMode.<Module>`; the foundation's own PlayMode tests
  use `MusicRoom.Tests.PlayMode`): ALWAYS boot through `TestBoot` (`Tests/PlayMode/Core/TestBoot.cs`): scripted hands, a fresh
  `MemorySettingsStore` (never the developer's PlayerPrefs: tests may not depend on a previous run's layout, tour flag or take
  numbers) and a temporary `DataPath` (takes go to `<temp>/takes`, deleted in `Stop`):
  ```csharp
  TestBoot _boot;
  [UnitySetUp] public IEnumerator Boot()
  {
      _boot = new TestBoot(/* headless: true, simulateHeadset: false, manualFrames: false, audioClock: null */);
      _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, wrist, Quaternion.identity).WithPointing()); // .WithPinch(gap) .WithFist() .WithIndexTipAt(p) .Translated(d) .OpenFacing(...)
      _boot.Options.ExtraModules.Add(new MyProbe());   // optional probe module (any Order), never a discoverable runtime class
      yield return _boot.Start();                      // App.Current is ready
  }
  [UnityTearDown] public IEnumerator TearDown() => _boot.Stop();   // app, code-only rig, temp folder
  ```
  Without `manualFrames`, one `App.Frame` runs per `yield return null` (real frame times, ≈ 1.5 ms in batchmode: fine for event
  plumbing, NOT for speeds, dwells or timeouts). For anything timed use `manualFrames: true`: Unity's Update drives nothing and
  `_boot.Step(1f / 72f)` (= `App.Step`) advances the hand source, the pinch/grasp derivation, `Clock` and one frame by exactly
  dt; a finger moved 1 cm per step reads 0.72 m/s, a still pinch reaches the .3 s dwell after 22 steps. With
  `audioClock: () => t` the main-thread audio clock (`IAudioEngine.Now` / `FrameNow`, `Clock.AudioNow`, the recorder, Sunny) is
  scripted too (12 s of tour time = 864 steps, not 12 s of test time). With `simulateHeadset: true`, `App.IsPresenting` is true
  for the scripted hands, so the XR-only paths (grab, swivel, summon, page turn, nod kick, placing) run without a headset.
  HandState's `PinchStart/End` live inside one frame (cleared by `Input.EndFrame`); after a step read `HandInput.pinchStart[i]`.
  No headset: `RigBootstrap.State == Desktop` after ≈0.5 s, the head sits at (0, 1.2, −0.1). Audio runs in batchmode
  (`SynthSource.BlocksRendered` grows; `MasterBus.BlocksRendered` grows). `FrameOrderTests` (foundation) records one frame's
  phases through a probe module and an `IInstrument` probe: copy that pattern to assert your module's phase timing;
  `BootOptionsTests` shows the manual-frame and scripted-clock pattern.
- Modules unsubscribe every STATIC event (and every handler on objects that outlive the App) in `Shutdown`: a PlayMode run boots
  the app a dozen times, and a stale handler would touch the destroyed objects of an earlier boot. `App.Events` handlers die
  with their App.
- Asserting sounds: set `IAudioEngine.TestTap = new AudioTestTap()`. Instrument tests assert the API calls in `Calls` (`Method`,
  `Index` = midi / string / kind, `Vel`, `Strike`, `Pluck`, `When`, `Handle`; `Count(AudioCall)`), the instrument-level calls
  (`Piano(midi, vel, when)` and friends), so a retuned recipe never breaks them. `Commands` (`Count(SourceId, SynthOp)`, fields
  such as `Buffer`, `Level`, `When`) are the Audio module's own recipe detail. `SeedAll(seed)` for deterministic jitter;
  `Source(id).ActiveVoices/DroppedCommands/Rms` for the audio thread. Offline exactness: render with the seeded renderers and
  compare with the 06 WAVs (`Tests/EditMode/Audio/Reference~/`, mono float32 48 kHz).
- Allocation tests (hot paths): `Assert.That(() => { ...calls... }, Is.Not.AllocatingGCMemory())` with
  `using UnityEngine.TestTools.Constraints; using Is = UnityEngine.TestTools.Constraints.Is;` (the "GC.Alloc" recorder filtered
  to the main thread, so background workers do not count). Warm the path up first and add a positive calibration
  (`Is.AllocatingGCMemory()` on a `new float[1024]`). Do NOT use `GC.GetAllocatedBytesForCurrentThread()`: it returns 0 on
  Unity's Mono (measured). Pattern: `Tests/PlayMode/Audio/AudioAllocTests.cs` (190 engine calls, 0 allocations).
- GPU tests: `[Category("Graphics")]` and `Assert.Ignore` when `SystemInfo.graphicsDeviceType == Null`; run with `test-play --gpu`
  (`DrawBudgetTests` keeps the whole scene ≤ 150 draws). Benchmarks: `[Category("Performance")]`, run with `test-perf`, never
  in `test-play` (`AudioLoadTests`: several seconds).
- Never `Assume.That` (Inconclusive fails CI). Keep PlayMode tests under ~2 s each; the whole suite under 2 minutes.
- Device tests: `Tools/unity.sh build && install && log`; log `[Module]` lines; the UNVERIFIED list (section 19) is the device checklist.

---------------------------------------------------------------------------------------------------------------------------

## 17. Performance budgets (Quest 3S, 72 Hz, 13.9 ms per frame)

| Budget | Target | How to measure |
|---|---|---|
| Draw calls (single-pass instanced) | VR room ≈ 34 (37 with snow); per rig: piano ≤ 10, drums ≤ 25, harp ≤ 6, table + deck + switches ≤ 15, menu ≤ 4, Sunny ≤ 6, tutorial blocks 1, hover glows 1 (section 14); whole scene ≤ 150; MR ≤ 80 (casters: instrument bodies only); no MaterialPropertyBlock on URP Lit/Unlit (SRP Batcher) | `DrawBudgetTests` (`test-play --gpu`, profiler `Draw Calls Count`; the stub scene: 1 draw); Frame Debugger / Profiler on a development build |
| Triangles | ≤ 300 k | Profiler |
| Shadow casters | 0 in VR; one 1024 hard map in MR (soft shadows off), only the instrument bodies cast | URP asset / Light settings / `shadowCastingMode` |
| CPU main thread (our scripts) | ≤ 3 ms per frame total: input + instruments ≤ 1 ms, layout/grab ≤ 0.3 ms, menu redraw only on change, strings deform ≤ 0.3 ms, decor ≤ 0.3 ms | `Profiler.BeginSample("[Piano]")` around `Tick` |
| GC | 0 B per frame on hot paths (no LINQ/closures/strings in Tick; preallocate; no per-call lists: `Rig.Handles` is filled once, `IHarpApi.MidiOf` uses a static table) | PlayMode `Is.Not.AllocatingGCMemory()` around your `Tick`/API calls (section 16; the engine API and `MidiOf` / `Rig.Handles` pass); Profiler allocation capture on device |
| Audio thread (our C#) | ≤ 1.5 ms average per 5.33 ms block, peaks ≤ 3 ms, 0 dropped commands, no stolen voices in normal play. Measured with the periodic-wave tables and metal sums ready (E12 busy groove, `test-perf`, editor Mono, two runs): 310–327 µs total of averages (5.8–6.1 %), 1135–1249 µs sum of maxima, Ride 35–37 µs avg, 0 dropped, 0 stolen; ×2 for IL2CPP ≈ 0.65 ms | `SynthSource.AvgBlockMicros/MaxBlockMicros` (since `ResetStats`), log once a second in dev builds; `Tools/unity.sh test-perf` |
| Audio latency | fingertip → ear ≈ 26–51 ms expected (mean ≈ 37), TO BE MEASURED: detection in `Update` 0–14 ms at 72 Hz + post → block start 0–5.33 ms + Unity output ring 4 × 256 = 21.3 ms + AAudio/HAL ≈ 5–10 ms, hand-tracking latency on top. The code path adds nothing (hands sampled at −100, `App.Frame` at −40 in the same Update, commands committed per call and consumed by the next mixer block, no look-ahead). Levers: `AudioModule.PreferredDspBuffer = 128` (ring −10.7 ms, if no underruns), `RigBootstrap.PreferredRefreshHz = 90` (detection 0–11 ms). Keep ASAP `when` for live strikes (the harp too, unless sweeping), no PlayScheduled | `AudioEngine.LatencyProbe` (dev device build: in-frame wait, frame time, ring per live call) + a filmed tap for the total; `dumpsys media.audio_flinger` |
| Memory | sample bank ≈ 80 MiB (67.7 piano + 12.2 harp, allocated in one burst at boot) + voice lines ≈ 6 MiB + metal sums 1.5 MiB + render scratch 2.6 MiB per worker; textures ≤ 40 MB (ASTC); total app ≤ 600 MB | `adb shell dumpsys meminfo`; `[Audio] ... buffers=79.9MiB` |
| Start-up | first key ready < 0.15 s, all renders during Sunny's intro (≈ 4 s; 133 jobs in 1.3–1.5 s on the Mac with 2 workers), no large garbage from the workers (outputs preallocated, scratch reused, Android priority lowered), textures baked (the build bakes the missing ones; no runtime generation on device) | `RenderProgress`, `[Audio]` log, Profiler GC markers and `SynthSource.MaxBlockMicros` during the intro |
| APK | ≤ 90 MB. The foundation skeleton is already 59.3 MB (56.5 MiB), nearly all engine/SDK: `libil2cpp.so` 25.1 MB compressed (99 MB raw), `libunity.so` 9.9, `classes.dex` 6.4 (stored), IL2CPP metadata 4.0, `libOVRPlugin` 2.8, MRUK 1.9, Meta XR Audio 1.6; our content < 3 MB (voice WAVs 2.3 MB). Stage 3 adds baked ASTC textures (≈ 10–20 MB) and code. Levers if it grows: Managed Stripping Level High (keep `Core/link.xml`), drop unused SDK features | `Tools/unity.sh build` prints `apkBytes`; `unzip -lv` the APK for the breakdown |

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): the head camera with the default set and five instruments on display draws 21 (budget 150; model ceiling 120, worst VR set 121, worst MR 74); each new instrument's allowances: <= 4/6/6/5/5 draws at the seat, <= 2/3/3/3/3 on display, <= 25 k / 8 k tris; audio caps per unit (platform.md 10.3); platform main-thread additions <= 0.04 ms; the planner only on set changes (worst 0.15 ms in the editor).

## 18. Settings persistence and events

Keys (`AppSettings` over an `ISettingsStore`: PlayerPrefs in the app, `MemorySettingsStore` in tests): `musicroom.tourDone` (int 0/1), `musicroom.layoutMode` ("all"|"single"), `musicroom.current`
("piano"|"drums"|"harp"), `musicroom.locked` (int), `musicroom.nextTakeNumber` (int, mirror of the take index). Call `Settings.Save()`
after writes. Files: `App.DataPath/takes/index.json`, `takes/<id>.json` (`DataPath` = `persistentDataPath`; a temporary folder in tests).

Events (`App.Events`, structs in `Core/App/Events.cs`): `LayoutChangedEvent{Mode, Current, Locked}`, `TurnEvent{Started, Direction, From, To}`,
`ArrangedEvent{Forced}`, `MenuEvent{Change, Screen}`, `RecorderEvent{Event, DuringPlayback}`, `RecorderStateEvent{State, External}`,
`TutorialEvent{State, LessonId, Version}`, `LibraryChangedEvent{Version}`, `GuidePresenceEvent{Present}`, `MixedRealityEvent{Active}`,
`AmbienceEvent{Kind, On}`, `NodEvent{Velocity}`, `HmdMountEvent{Mounted}`, `RecenteredEvent`.

---------------------------------------------------------------------------------------------------------------------------

**Since the many-instrument platform** (12-new-instruments/platform.md, phase 0, `u-platform`; the build guide for the instrument agents is 12-new-instruments/BUILD-GUIDE.md): keys `musicroom.set` ("piano,drums,harp" by default), `musicroom.stow.<id>`, `musicroom.current` any registry id, `musicroom.arrange.single.<id>` for every id, `musicroom.<id>.*` instrument-owned; events `InstrumentMoveEvent`, `FocusEvent`, `InstrumentOptionEvent`, `InstrumentSignalEvent`, `MenuRowEvent`, `LayoutChangedEvent.SetVersion`.

## 19. UNVERIFIED assumptions (device-only; the device-test stage checks each)

Hands / rig
1. `OVRPlugin.HandState.RequestedTimeStamp`/`SampleTimeStamp` semantics and clock; tracker rates at hand-tracking frequency HIGH vs LOW (`HandFrame.sampleInterval`).
2. `GetUnextrapolatedHandState` succeeds on the current Horizon OS (else velocities fall back to the predicted pose after 300 failures: `MetaHandSource.UsingRawSamples`).
3. `BonePositions` are real size (Interaction SDK divides by `HandScale`) and the baked radius × `HandScale` choice.
4. `OVRMesh`/`GetSkeleton2` first succeed within ~2 s of launch (`MetaHandVisual.ForceRebind` safeguard); `HandGhost` look, fade timings, low-confidence alpha 0.4.
5. `GetNodePose(HandLeft)` equals `RootPose` (only relevant if a prefab-under-anchor hand is ever used).
6. `displayFrequenciesAvailable` on Quest 3S includes 72 and 90; 90 Hz frame budget with the synth and snow.
7. `suggestedCpu/GpuPerfLevel` and `sharpenType` take effect under OpenXR without Unity's "XR Performance Settings" feature; SRP
   foveation (Unity's Foveated Rendering feature + `XRDisplaySubsystem.foveatedRenderingLevel = 1`, logged as `[RIG] SRP foveation
   level 1 on N display subsystem(s)`) really foveates on Quest 3S: URP's `XRPass.foveatedRenderingInfo` non-zero, GPU time down
   (the Meta Legacy feature is off now, so `OVRManager.foveatedRenderingLevel` would do nothing).
8. Passthrough alpha compositing with URP, HDR off, MSAA 4, clear `(0,0,0,0)`; the cost of keeping passthrough initialised but hidden; `IsPassthroughRecommended` preselection.
9. ~~`ForceRemoveInternetPermission` actually drops `android.permission.INTERNET` from the merged manifest~~ CHECKED on the
   foundation APK (section 20: `aapt2 dump badging` lists only HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY and
   the package's DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION). Re-check after adding any package or plugin: it can bring INTERNET back.
10. Native OpenXR hand poses change every frame (no repeated poses), so pointer `SampleDt` converges to the frame dt.

Audio
11. Quest output sample rate (48 kHz expected), `GetDSPBufferSize` buffer count, end-to-end latency (26–51 ms expected:
    `AudioEngine.LatencyProbe` lines + a filmed tap); whether `AudioModule.PreferredDspBuffer = 128` runs without underruns.
12. Audio CPU ≈ 2× the editor's Mono numbers under IL2CPP ARM64 (budget 1.5 ms per block; the editor busy groove with the
    periodic-wave tables: 310–327 µs average total, 1135–1249 µs sum of maxima).
13. Meta shoebox "front" orientation vs Unity +z; reverb send tuning; `EnableAcoustics=false` sources with zero room send; `MetaXRAudioSettings.voiceLimit 64` applied.
14. Mixer suspension with silent carriers (suspend is off in the asset); `AudioSettings.Mobile.stopAudioOutputOnMute`; re-`Play()` after `OnApplicationPause(false)`.
15. Periodic-wave-table oscillator start-up cost on device (`PeriodicWaveTables.Build` + `MetalBank.Build` on a worker); `BufferVoice` 300 ms
    wait never bites after the intro.

World / render
16. GPU cost of 5 lights forward (1.5–3 ms est.), snow (0.2–0.4 ms), fire (0.1–0.3 ms), frost (0.2 ms); `ambientProbe` SH conventions if used.
17. Runtime `Texture2D` generation time (fallback path only) and ASTC quality of baked textures.
18. Symmetric projection and Multiview Render Regions (not enabled); try after the first on-device profile.
19. `Shader.Find` inclusion rules on Android match the macOS player probe (templates guarantee variants).

MR
20. WINDOW_FRAME `forward` points into the room on device (56/56 in sample rooms); `PlaneRect` in metres; floor y = 0 with FloorLevel vs `FloorAnchors[0]`.
21. `USE_SCENE` dialog flow, `RoomUpdatedEvent` after editing Space Setup mid-session, `EnvironmentRaycastManager` readiness time, sceneless world lock behaviour.
22. World lock with `OVRCameraRig` under the OpenXR loader; `GetCurrentRoom()` in multi-room homes while seated.

Pipeline
23. `-testPlatform Android` test player, `BuildOptions.AutoRunPlayer`, the proximity-sensor broadcast.
24. Target SDK 34 remains the Horizon Store requirement at submission time.
25. The merged manifest carries `horizonos:minSdkVersion = 60` (added by the Core SDK 207 manifest step) and the contextual
    passthrough loading screen (`com.oculus.ossplash.background`): the test headset must run Horizon OS v60 or later, and the
    loading screen should show passthrough, not black.

Added in review round 1
26. The render workers' JNI priority call works (`OfflineRenderQueue.PriorityLowered == 2`, `top -H` shows them at nice 10) and the
    intro has no GC pauses on the mixer thread (Profiler `GC.Collect` markers, `SynthSource.MaxBlockMicros` while Sunny talks).
27. The late-runtime path of `RigBootstrap` (Desktop, then Xr) with the XR Simulator; `IsInsightPassthroughInitPending` resolves
    within `MrModule.PassthroughWaitSeconds` on device.

Added at integration (2026-10-31)
28. The menu canvas painted through `Graphics.ExecuteCommandBuffer` from `Update` (the Menu module's accepted alternative to
    15.6's camera request) renders correctly on Quest 3S (Vulkan, single-pass instanced): text and shapes upright and crisp,
    no flicker on repaint, and URP's per-camera globals (`_ScreenParams`, view-projection) restored for the next camera.
29. Entering MR resets the player rig (`RigRoot` to the origin, as MR entry does) before MRUK's world lock engages, with
    `MRUK.TrackingSpaceOffset` = the rig root's pose (identity): after a VR seat fit and a swivel, "Play in my room" shows no
    head jump when the scene loads; leaving MR, the VR layout comes back around the player.

Added at the 10-upgrade integration (2026-11-01). Items 1, 6, 8, 12 and 13 above now read: hand tracking runs at LOW with
Fast Motion Mode while drumming (30-34); 90 Hz is the default under the governor (35-37); passthrough is started on MR entry
(38); the audio CPU is the Burst graph (39-44); Meta's shoebox reverb plays only with "Classic sound" (since 2026-11-10
always: the classic sound is the only sound).
30. Palm summon on device after the merge: an open palm facing the headset (while Quest raises `SystemGestureInProgress`)
    opens the menu and the hand stays visible; a pinch into the system menu does not (the 2026-11-01 hotfix, kept).
31. FMM: with the manifest at LOW, `adb logcat -e FMM` shows the runtime enabling Fast Motion Mode within a second of
    picking up a stick or entering single mode on the drums (`[Hands] FMM request on`), and off 5 s after drumming stops;
    the dev-build `[Hands] sampleInterval` line reads about 16.7 ms with FMM and 33 ms without. If the hint is ignored,
    set `QuestSetup` back to HIGH (the policy stays).
32. FMM low-light guard: in a dim room `[Hands] FMM blocked: low confidence` appears, FMM stays off for the session and
    Sunny shows "More light helps your hands" once (`[Guide] hint:`; text only, after the tour if it runs).
33. Fast strokes on the drums keep tracking with FMM (no missed hits on a fast roll); piano and harp keep the low-jitter
    default (no extra key jitter).
34. E1 / E2 / E3 / E6 look: fingers stop on keys, heads and the panel (knuckle turn looks natural; the spec's fallback is a
    60/40 split); sticks rebound on the head (slow-motion video); contact shadows visible under the lamp light, not too
    strong; `[Core] contact` <= 0.05 ms; GPU delta of the shadows < 0.1 ms; the skin warms and darkens with the fire.
    A straight poke at the menu draws the fingertip where it pokes (no sideways bend); in MR the finger shadows stay
    under the real fingers while they move (the cut-out's delayed pose).
35. 90 Hz holds in the VR room with the fire, snow and five lights: `[RIG] quality rung` lines (which rung each mode
    settles on); the unit of `App_GpuTime_Float` (values under 0.5 are taken as seconds); a thermal drop to 72 Hz moves to
    the matching rung.
36. `sharpenType = Automatic` makes a visible difference on the key letters (else set None).
37. Dynamic resolution: the dev-build `[RIG] dynres ... (session p95 ..)` line; when p95 never exceeds 1.3, set
    `RigBootstrap.DynamicResolutionMax = 1.3` (about 100 MB less eye buffer).
38. Passthrough on demand: "Play in my room" waits for `[RIG] passthrough ready X s` (bounded at 4 s, the VR room stays
    up meanwhile); the CPU level-4 gain in VR without passthrough; exit shuts passthrough down; the MR piano anchor (E9)
    localizes and puts the piano back on the next visit, and drifts follow it (`musicroom.mrPianoAnchor`).
39. Burst audio graph on the Quest (burst.md section 13): T-DENORM (subnormal ratio ~1), T-KERNEL (MB4 free <= 0.6
    ns/mode-sample), T-VEC, T-STRESS, T-LAT, T-128 (`PreferredDspBuffer = 128` without underruns), T-PAUSE (pause/resume
    with ringing units); the `[Audio] dsp avg/p99/max` logcat line of a `-development` build stays under 2 ms per block;
    the FMOD mixer thread shows the `MusicRoom.AudioGraph.Render` profiler marker.
40. All instruments + room at their worst at once (`AllSoundsLoadTests`: 0.34 ms avg / 0.51 ms p99 per block in the
    editor) stays under 2 ms per block on the Quest 3S (expected 3-4x the editor: about 1.0-1.4 ms avg).
41. Piano unit: the dev-build `[Audio] piano ...` line; lesson <= 0.2 ms, palms / forearm / 88 keys <= 0.6 ms avg and
    <= 1.0 ms max per block (else `PianoUnit.SetModeBudget(1200, 1600)`); the model's boot time on a worker (middle C
    first); the piano.md 9.5 listening list (half pedal, step damp, re-strikes, sympathetic halo, bass release).
42. Drum unit: drums.md section 12 step 2 on the device (vrWorst < 0.8 ms avg / 1.2 ms worst block); the drums.md 15
    listening list; fingertips as a stick tip (`DrumSpec.FingertipImplement`) sound right; the cross-stick on the snare
    rim; the hi-hat pedal openness.
43. Harp unit: harp.md 11.3 (dense case <= 150 us on the Quest 3S); a capture (`HarpUnit.StartCapture` / `SaveCapture`)
    through the harp lab's WAV analysis; by ear: poke vs sweep balance, the catch on re-pluck, palms, BRIGHT_DB; the strings' blur
    (E8) follows the measured swing and narrows as the note dies, sympathetic strings shimmer slightly.
44. Room unit: the room.md 10.2 reference to Meta's direct path was measured in the editor's Meta XR Audio 85
    (`RoomCalibrationTests`: 20-sample latency, no propagation delay; per band and distance, `RoomConst.ReferenceDb` /
    `ReferenceDistDb`); confirm by ear that the seat's direct-to-room balance is natural (not boomy in the bass, not dull
    or dry at 2 kHz) and, if Meta's plugin changes, re-run that test; the listener stage < 400 us per block; the dev
    `[Room]` line (0 RoomBus misses: the listener runs after every source of a block); by ear: the room amount presets
    (Off / `Faint` (stored as `web`; no early reflections) / Low / Natural / High, setting `musicroom.roomAmount`, no menu row yet); MR: the real room's box and its
    yaw from the floor anchor (wet -3 dB; 4 x 2.5 x 4 m at -6 dB without a scene), and in a large room no 84 ms slap.
45. (Obsolete since 2026-11-10: the switch is gone, the classic sound is the only sound.) "Classic sound" on the headset: switching mid-note never clicks (both ways); the first switch to Classic renders
    today's piano and harp buffers (~80 MiB, 1-3 s, "Preparing..." on the row) without a visible hitch; the setting
    persists across launches; switching back restores Meta's room reverb and the drum room. Switching to Classic, the
    level holds while Meta's reverb builds up (our room now hands over in 0.4 s; it sagged 2-5 dB for ~0.6 s before).
46. E7 / E8 look: the crash rocks toward its struck edge, the batter heads give, the kick beater swings with the nod; the
    plucked strings read as translucent spindles (no 45 Hz flicker), still hide a finger behind them, and in MR are
    not cut away where they pass in front of the real hand; shadows lie on the cymbals' tops and a stick on the ride
    bow stops on the plate.
47. APK: still no `android.permission.INTERNET`; `lib_burst_generated.so` present and loaded (no Burst fallback warning in
    logcat).

Added at the merge of u-arrange and u-looper (2026-11-01; ARRANGE-NOTES.md section 5, LOOPER-NOTES.md section 4)
48. Start panel: it opens in front at launch, readable at about 0.5 m, rows easy to poke; "Play in my room" offered on
    Quest 3 / 3S (passthrough pending counts as available) and its choice enters MR; Sunny's first-launch tour follows
    the answer.
49. Exit: a double poke within 3 s closes the app cleanly (no audio pop: 150 ms fade of `AudioListener.volume` in ~11
    frame steps); a single poke times out. Relaunch restores the layout and placement.
50. Meta's pinch: PinchStrength 0.85 / 0.5 and IsIndexPinching make grabs reliable; `[Layout] arrange mode on` and the
    pinches in the device log; the thumb-index distance at a firm pinch (expected 15-20 mm between joint centres).
51. The system pointer pose (PointerPose with InputStateValid) gives a steady ray from each hand; its origin and direction
    match where the user points; it hides while the palm faces the headset.
52. Arrange near grabs on keys / heads / strings; ray grabs from 1-3 m; the 2.5 x push feels right (else tune
    `RayPushGain`); the 80 ms debounce never takes an instrument by accident.
53. Arrange heights: piano key tops 0.55-0.95 m with the stand on the floor; kit -17..+15 cm (raised, it floats;
    lowered, it sinks into the floor); comfort of the defaults.
54. The Done / Reset panel's reach and size at 35 cm, below and left. The highlight's look (changed at the merge: 4 mm
    warm-white corner brackets at alpha .42 and a faint floor ring, light gold under a hand or ray, gold held, red
    blocked, alpha-blended rather than additive): visible enough in the lamp-lit room without reading as glowing UI; the
    4.5 mm warm-white ray on the Quest display.
55. The OS system gesture (palm toward the face, pinch) never grabs; summoning the palm menu still works.
56. The in-play pinch-and-hold grab works on the headset (grab pinch, 4 cm stillness).
57. The saved arrangement comes back on the next launch around the player (the seat frame is the head at the first
    presenting frame; a recentre re-places).
58. Looper `Looper.InputLatency` (30 ms guess): overdub a beat by ear on a loop; notes landing consistently late or early
    mean the value is wrong (raise it if late). The same for layer 1 against the clicks.
59. Looper first-note start: the first note on a click, half a beat off, and well before the clicks.
60. The loop strip's 2.8 cm button pitch with hand tracking (no mis-pokes between neighbouring layer lights); the 12.5 mm
    press cylinders; a hand resting on the deck front presses nothing.
61. The deck's look: lamp brightness behind the smoked caps in the headset; label legibility (4 mm letters at ~0.6 m).
62. Loop playback visuals lead the sound by up to the 80 ms look-ahead (keys, heads and strings animate when sent):
    acceptable or not.
63. Sunny's perch beside the deck from the real seat (it must not cover a button) and the ghost hand pointing at controls
    during the GUIDE walkthrough.
64. The metronome click on the 2D Ui source next to the spatialized loop; Classic sound switched mid-loop on the headset
    (no lost or doubled loop note: the looper re-sends one block ahead on the new path).
65. A 70 BPM 4-bar loop (13.7 s) with six layers on the new sounds for audio CPU (`[Audio] dsp avg/p99/max`, `DspLoadHud`).
66. The grab pinch everywhere (switched at the merge): a light pinch picks up a stick, toggles the hi-hat at its stand,
    takes the one-hand seat turn without it dropping mid-pull, makes the two-hand summon, and lands the piano in "Play in
    my room"; none of them fires from the palm-toward-the-face system pinch.
67. First five (u-firstfive): the first run from the start panel on a fresh install (`adb shell pm clear com.vrmusicroom.app`):
    Sunny comes beside the panel at 8 s (bubble above the panel, not over a row) and never answers it; after "Play here"
    the first block lands on E4 within ~2 s and a new player's first note comes within 10 s; the song's soft chords are
    audible but under the melody (velocity 0.28); a wrong key sounds without a red flash.
68. First five: the ghost hand (#f3ead8, alpha .375, alpha-blended) is visible enough over the keys, the snare, the harp
    strings and the menu, and over passthrough; it reads as a hand pressing / tapping / sweeping from the seat; the
    pale warm lesson blocks (ivory, warm white, sage, muted coral) are visible on the white keys and the drum heads.
69. First five: poke to skip with real hand tracking: a deliberate poke (0.065 m sphere, armed at 0.10 m, at least 0.10
    m/s) always skips; a hand reaching for the keys, the menu or the deck past Sunny never does (the perch keeps 0.12 m
    from every play zone); the push / blink / bop feedback is noticeable but calm.
70. First five: the 8 s re-demo and the ~15 s give-up feel right for first-time players (not nagging, not stuck); the
    passive path ends in under 4 minutes.
71. First five: the room celebration (fire light x1.45, flames +30 % for 2.5 s, lamps x1.15 and ~150 K warmer, SNOW on,
    a gust, the rolled harp chord) is noticeable but calm, and the room is back exactly afterwards (lamp intensity and
    colour, flame scale).
72. First five: S9's spotlight (the menu's hover highlight walking down Lessons, Instruments, Recordings, Move instruments,
    Play in my room, Exit) follows Sunny's words; a fingertip hover always wins.
73. First five: the drum lesson targets at 0.55 r toward the player land on the surface of every piece from the seat (not
    hidden in a cymbal; the crash and ride targets in the bow, not on the bell) and are within reach.
74. First five: the contextual tips come at quiet moments only (no tip over playing, never two at once), never during a
    count-in or recording pass; the Tips list replays any tip with its demo at the real control; "Tips while I play" off
    keeps the P2 / P3 tips quiet.
75. First five: voice: since the platform merge the 79 first-five lines are a module line file rendered lazily by
    GuideVoice (24 resident; phase A rendered 114 at boot, ~55 MB): each first-run line ready before it is said (`[Audio]`
    render log), no LATE caption-only lines, no hitch when a line renders on first ask.
76. First five: the Done / Reset buttons stop the drawn finger on their face like the palm menu (no finger sinking through).
77. First five: the looper's first note lands on beat 1 however far (up to half a beat) it was from a click.
78. First five: the "Next: <title>" row after a lesson and the "Done" mark on finished courses survive a relaunch
    (`musicroom.lessonsDone`).

---------------------------------------------------------------------------------------------------------------------------

79. Platform (platform.md 12.4 item 48; u-platform): the displayed instruments look right from the seat: scale, lit by the
    lamp and the fire, nothing floating (the handpan's bracket against the wall), the bowls on the sill; in phase 0 the five
    are neutral wood placeholders. The room's decor (plant leaves, the sill's ivy) touches the piano's, bowls' and hand
    drums' spots (Room's report): judge whether it reads as clutter.
80. (49) Portraits through `SubmitRenderRequest` on the Quest (Vulkan, single-pass): upright, crisp, lit, no effect on the
    next head-camera frame; else `musicroom.menu.portraits` 0 (the vector icons).
81. (50) Point and pinch: the system pointer pose steady at 2-3.5 m; the cone pick picks what the player means (bowls vs hand
    drums, kit vs handpan); the 0.25 s hover card readable; the 0.35 s hold feels intentional; the 2 cm preview lift noticed;
    the theremin's pick point (0.47 m above its cabinet) not confusing.
82. (51) Glide comfort: no unease while an instrument crosses the room in up to 1.4 s (up to 10 m, sometimes behind the
    player); glides may briefly pass through furniture.
83. (52) Take serialisation off the main thread under IL2CPP (40000 events) and the STOP press hitch.
84. (53) 26 sources: the "Meta XR Audio" profiler time with 13 instrument sources active vs 18 total before; whether
    `AudioSource.Pause` stops Meta's spatializer for displayed instruments (in the editor our filter still runs).
85. (54) Theremin focus: facing +-30 deg feels natural seated; reaching to the menu or the piano never makes it sound.
86. (55) The side table's alternate pose (behind-left) reachable for LOOP / REC from the seat.
87. (56) MR with a non-piano lead: a table-top lead on a real table, the drums on the floor, nothing displayed leaking into
    passthrough; the lazily rendered Sunny lines (61 ms in the editor) and `Resources.UnloadAsset` of voice clips on the
    device; the 4th early-reflection group's cost.
88. First five on the platform (b-firstfive): the closing invitation: the ghost hand by Sunny points at the nearest
    displayed instrument (2-3.5 m away) and pinches while "Want more? Point at an instrument and pinch." is said; the
    player understands it and the point-and-pinch works first time; "You can also bring them from Instruments in the
    menu." follows.
89. First five on the platform: the soft ghost hand (a rounded palm, 16-sided fingers, depth written, edges fading to
    alpha 0.2 where the surface turns away): it reads as a soft pale hand up close and over passthrough, with no dark
    seams; the depth it writes hides nothing that should show through it (the player's own hands, a lesson block under
    the finger).
90. First five on the platform: one scheduler for every tip: the platform's and the instruments' tips also wait for quiet
    moments (2.5 s after playing, no lesson, no count-in), never two at once; the Tips list (the tour's 65, the platform's
    six, each instrument's) scrolls and reads well on the headset.

91. All instruments (int-all, integration): the five new instruments and the music book in one build: the default room
    from the seat looks as before with the five on display around it (handpan on its wall bracket, theremin by the lamp,
    marimba and hand drums by the window, bowls on the sill); each one brought by the menu and by point-and-pinch lands
    at the seat in a set without touching the player or another instrument; audio CPU with all eight at the seat or on
    loops (`[Audio] dsp`, the governor's steps) under 2 ms per block on the Quest 3S.
92. Handpan (b-handpan, `Handpan/PHASE-B-NOTES.md` section 4 items 1-10): feel and velocity with Fast Motion Mode off and
    on (on only if it wins), aim on A4 / C5 (>= 95 %), headroom of the 10-finger roll plus a ding slap, CPU with
    passthrough, three seated testers' comfort (the near fields at 0.74 m, the cradle's legs clear of the shins), the
    lesson blocks, a looped ringing ding not cut at the loop's end, MR, the glide off the bracket, and the Calm voice's
    level against the piano and the harp by ear.
93. Hand drums (b-handdrums, `HandDrums/PHASE-B-NOTES.md`): T-HD-CPU, T-HD-LAT, T-HD-RATE, T-HD-CLASS, T-HD-JITTER,
    T-HD-REACH (open tones at the near edge 0.712 m, the palm 8.3 cm over the knee box; record through the lens that
    the drawn palm stays on the skin now that `MetaHandVisual.SetPalmLift` is in), T-HD-MR, T-HD-LISTEN (the full glide
    is the default).
94. Marimba (b-marimba, `Marimba/PHASE-B-NOTES.md` section 5, spec 9.3 M1-M9): the contact solve's cost (M2, <= 80 us),
    the resting mallet head's jitter and the press depth (M4 / M5: re-tune if the head's RMS is above 6 mm), the
    repeated-sample gate at the low tracking rate, the A versions by ear (bass bloom, treble knock), and whether keeping
    the marimba out of sets with the kit (its air boxes: the hi-hat under the right hand) is right for players.
95. Theremin (b-theremin, theremin.md 10.4): tracker noise and drift on the pitch hand, the held-note SD, CPU on the
    3S, the brass rule's letters legible (their material is the TMP default's: not seen in an IL2CPP build yet), the
    Classic tone by ear; "Pitch hand: Left" mirrors the model and the planner now plans the mirrored boxes
    (`SeatSpec.MirrorKey`): check the left-handed theremin's field sits where a left-handed player expects (the rig keeps
    the right-handed home, so its field is about 0.30 m further right than the right-handed one's).
96. Singing bowls (b-bowls, `Bowls/PHASE-B-NOTES.md` section 7, bowls.md 11.4 B1-B12): the faster circle on the big bowl
    (chatter edge), the lip-as-outer contact by ear, the sill spot (3.09, -0.62) seen from the seat behind the
    displayed marimba and picked by the ray, the reflection in a displayed bowl reads as metal, the soft rub's purity by
    ear; the lesson re-demo now circles a finger on the rub lesson (bowls R5).
97. Music book (b-sheet, `Sheet/README.md` S1-S7): readability on the lens, the lamp level, corner reach, the page sound's
    level, raster time on the Quest, shimmer, the falling blocks over the page; its "Your songbook" tip is a P3 tip and
    now waits for the P3 rules (90 s after the tour, quiet moments, no other tip waiting) like every rate-limited tip.

## 20. Foundation verification record

Run 2026-10-25 08:31–08:55 on macOS (Darwin 25.6.0, 18 cores, 64 GB), Unity 6000.3.27f1 batchmode
(`-nographics -buildTarget Android`), one editor at a time (the leftover `Temp/UnityLockfile` had no Unity process and was
ignored). Driver: `LOGS=<log folder> Tools/unity.sh <cmd>`. In that folder:
`chain5-final.txt` = the full chain + APK on the final runtime code; `chain6-alloc.txt` = compile, test-edit, test-play and
meta-report re-run after the last change (a PlayMode test file and `unity.sh`; no runtime code), whose `compile.log`,
`test-edit.log`, `test-play.log`, `editmode.xml`, `playmode.xml`, `meta-setup.json` are the current files; the other
`<cmd>.log` files come from chain 5; `chain1`–`chain4.txt` are the earlier passes; `apk-check.txt` is the aapt2 dump of the APK.

### 20.1 Results

| # | Command | Exit | Wall | Result |
|---|---|---|---|---|
| 1 | `compile` (every MusicRoom output deleted from `Library/` first, so all assemblies really rebuild) | 0 | 14 s | 15/15 MusicRoom assemblies rebuilt; 0 errors and 0 warnings in the whole log (chain 6: 0/0 again) |
| 2 | `render-setup` | 0 | 11 s | `URP default = Assets/MusicRoom/Settings/QuestURP.asset msaa=4 hdr=False addLights=4`; TMP_Settings ok (LiberationSans SDF); 11 material templates |
| 3 | `tmp-import` | 0 | 8 s | `TMP already imported` (the first import, chain 2: `importPackageCompleted`, 19 s) |
| 4 | `configure` (re-applied with URP active) | 0 | 15 s | OpenXRLoader assigned for Android and Standalone; Meta project config applied; `spatializer='Meta XR Audio' ambisonic='Meta XR Audio' dspBufferSize=256 sampleRate=48000 speakerMode=Stereo real=32 virtual=512`; `[OPENXR-VALIDATION] 2 issue(s), 0 error(s)` (both Optional: PoseControl / StickControl deprecations) |
| 5 | `scene` | 0 | 11 s | `wrote Assets/Scenes/Main.unity; build scenes = 1` (roots: `PlayerRig`, `App`) |
| 6 | `bake` | 0 | 11 s | `[BAKE] 0 texture(s) baked` (no providers before stage 3; the step itself is proven) |
| 7 | `test-edit` | 0 | 9 s | total 60, passed 60, failed 0, inconclusive 0 (0.43 s of test time) |
| 8 | `test-play` | 0 | 16 s | total 11, passed 11, failed 0, inconclusive 0 (2.05 s of test time) |
| 9 | `meta-report` | 0 | 11 s | 92 tasks, 5 outstanding, **0 Required**: Optional "Enable Application SpaceWarp" (decided off), Optional "Scene runtime permission request" (USE_SCENE is requested on entering "Play in my room", section 12), Recommended "Data Use Checkup" and "application ID" (Platform SDK only, not used), Recommended "Meta XR Simulator not installed" |
| 10 | `report` | 0 | 8 s | read-backs in 20.2 |
| 11 | `build` | 0 | 58 s | `[BUILD] result=Succeeded apkBytes=59256631 time=00:00:46.8 errors=0 warnings=3` (incremental). The first full IL2CPP build (chain 4): 59,254,543 B, 00:02:25.5, 157 s wall (`Postprocess built player` 1:54, Gradle 17 s). The 3 warnings are IL2CPP notes on large TextMeshPro methods; none in MusicRoom code |

### 20.2 Read-backs (`report`)
activeBuildTarget Android, subtarget ASTC (`EditorUserBuildSettings.androidBuildSubtarget` lives in `Library/` and is per machine:
a fresh read-back can say `Generic`; `configure`, `bake` and `build` set ASTC again before they import or build), IL2CPP ARM64, minSdk 32, targetSdk 34, Vulkan only, Linear, Instancing, GameActivity,
LandscapeLeft, splash off, forceInternetPermission false, render pipeline `QuestURP.asset`, scenes = `Main.unity`, XR init on
start, loaders = `OpenXRLoader`; OpenXR SinglePassInstanced, Depth16Bit, PrioritizeInputPolling; enabled features (at the time)
MetaQuestFeature, MetaXRFeature, MetaXRFoveationFeature, MetaXRSubsampledLayout, OculusTouchControllerProfile,
HandInteractionProfile (SpaceWarp and both XR Hands features off; the foveation features changed in review round 1, 20.7); Meta Quest target devices eureka (Quest 3) and quest3s only;
`foveatedRenderingApi = 1` (SRP foveation); forceRemoveInternetPermission true; OVR HandsOnly, hand-tracking frequency HIGH,
passthrough Supported, scene Supported, anchors Enabled, boundary visibility Supported, target devices Quest3 + Quest3S, hand
skeleton OpenXR, colour space P3; audio spatializer and ambisonic decoder "Meta XR Audio", DSP 256, rolloff scale 1.1;
`activeInputHandler = 2` (Both). Manifest: `com.unity.xr.oculus`, `com.meta.xr.sdk.interaction(.ovr)` absent.

### 20.3 APK (`apk-check.txt`, aapt2 36.0.0)
`Builds/Android/VRMusicRoom.apk`, 59,256,631 B: package `com.vrmusicroom.app` 0.1.0 (1), label "VR Music Room", minSdk 32,
targetSdk 34, `horizonos:minSdkVersion 60`, arm64-v8a only, launcher `UnityPlayerGameActivity`. Permissions: HAND_TRACKING,
USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY, DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION; **no `android.permission.INTERNET`**.
Features: `android.hardware.vr.headtracking`, `oculus.software.handtracking` (required), `com.oculus.feature.PASSTHROUGH`
(not required). Meta-data: `com.oculus.supportedDevices = quest3|quest3s`, `com.oculus.handtracking.frequency = HIGH`, focus
aware. Size breakdown: section 17.

### 20.4 Runtime evidence (PlayMode, no headset)
`[App] booted: 10 module(s), hands=Fake, xr=False` (11 in the boots with a test probe; never in the others); every stub logs
once (`[Piano] stub ready: 88 keys to implement ...`, `[Layout] stub ready: 5 rig(s), mode=All, current=Piano, locked=False`,
`[Lessons] ready: 29 lessons, 826 events, takes=on ...`, `[MR] ready: passthrough=n/a ...`, ...); `[RIG] state=Desktop xr=False
head=(0.00, 1.20, -0.10)`; `[Audio] sr=48000 dsp=256x4 spatializer='Meta XR Audio' mixer=True lines=22/22`; synth sources and the
master bus render blocks in batchmode with 0 dropped commands; the frame runs the 20 phases in the section 4 order with the
instruments between InputDone and Instruments; 190 `IAudioEngine` calls allocate 0 B on the main thread.

### 20.5 Fixes made while verifying
1. `Core/Util/DesktopInput.cs` CS0119: inside `DesktopInput` the bare name `Mouse` bound to the `Mouse(int)` method; now
   `UnityEngine.InputSystem.Mouse.current`. This was the only compile error; it aborted every step after `configure` in chain 1.
2. URP made active (`render-setup`) before `configure`: clears the OpenXR validation ERROR "Only Legacy Foveated Rendering API
   usage is possible on Built-in Render Pipeline" and the URP upscaling warning.
3. `CompressorTests`: the old test compared the measured reference table with the ripple-free `StaticGainDb` and failed at −6 dBFS (1.040 vs
   1.078 dB). The table is the running kernel's steady gain for a 1 kHz sine; the test now drives `DynamicsCompressor.Process` in
   256-frame blocks and matches all 17 levels (−60…+12 dBFS) within 0.01 dB (≤ 0.001 dB in an offline replay), with and without
   the 6 ms pre-delay. No DSP change was needed.
4. Discovery: `ModuleDiscovery` and the editor's `ProviderDiscovery` skip `MusicRoom.Tests.*`; `BootOptions.ExtraModules`
   injects test probes; the unused `OptionalModuleAttribute` was removed. New tests: EditMode
   `TestAssembliesAreNotScannedAndExtraModulesAreMergedByOrder`, PlayMode `FrameOrderTests` (2) and `AudioAllocTests` (1).
5. `QuestSetup.ConfigureXR`: SpaceWarp is switched off for Android only (the feature does not exist for Standalone; the
   "MetaXRSpaceWarp not found" note is gone). `TextureBaker`: one ASTC 6×6 setting plus the spec's alpha source (the old
   ternary had identical branches). `AudioModule`: the start-up log adds the DSP buffer (`dsp=256x4`).
6. `Tools/unity.sh`: `test-edit`, `test-play` and `meta-report` delete their previous XML/JSON first (an aborted run had
   printed the previous run's totals).
7. Removed the empty, unused `Assets/MusicRoom/Scenes` folder (and its .meta); the scene is `Assets/Scenes/Main.unity`.
8. This document: tick orders (tape deck 30, switch panel 40; spec 05 §1.1), Table instruments marked stage 3 (3.4),
   generated outputs and merge order (1), foundation vs feature references (2), discovery/probe rules (3.4, 16), compressor
   and allocation test conventions (16), APK budget (17), UNVERIFIED 9 checked and 25 added (19), MR test pointer (15.9).

### 20.6 Not verified here
`install` and `log` (no headset was connected) and every item of section 19 except 9; GPU-category PlayMode tests
(`test-play --gpu`, none exist yet).

### 20.7 Review round 1 re-verification (2026-10-25, after the changes of section 21)
Same Mac and Unity, one editor at a time; logs in `<project>/Logs/ci` (the official chain) and `<project>/Logs/ci-extra`
(the GPU and second benchmark runs).

| # | Command | Exit | Result |
|---|---|---|---|
| 1 | `compile` | 0 | 0 errors; 0 warnings in `Assets/MusicRoom/**` (all 15 MusicRoom assemblies, the two test assemblies included, rebuilt) |
| 2 | `configure` | 0 | `[SETUP] layers 29=PlaySurface, 30=Hidden, 31=PanelUI`; `[OPENXR-VALIDATION] 2 issue(s), 0 error(s)` (the same two Optional deprecations) |
| 3 | `scene` | 0 | Main.unity rewritten (head camera mask without Hidden / PanelUI) |
| 4 | `report` (fresh process) | 0 | `FoveatedRenderingFeature = True`; `MetaXRFoveationFeature`, `MetaXREyeTrackedFoveationFeature`, `SpaceWarpFeature`, `MetaXRSpaceWarp` = False (Android); `foveatedRenderingApi = 1`; layers named; `androidBuildSubtarget = ASTC`. Only two OpenXR feature flags changed in `OpenXRPackageSettings.asset` (Foveated Rendering on, Meta foveation off, Android) |
| 5 | `bake` | 0 | `[BAKE] 14 texture(s) baked` (the `Core/<name>` shared set) in 17 s |
| 6 | `test-edit` | 0 | total 78, passed 78 (was 60) |
| 7 | `test-play` | 0 | total 20, passed 20 (was 11): Core `AppBootTests` (5), `BootOptionsTests` (3), `FrameOrderTests` (2); Audio `AudioEngineTests` (3), `AudioAllocTests` (2); Layout `LayoutStubTests` (2); Menu `MenuStubTests` (1); Hands `RigTests` (2); 3.4 s of test time |
| 8 | `test-play --gpu` | 0 | total 21, passed 21; `[DrawBudget] draws=1 setPass=1 (budget 150)` |
| 9 | `test-perf` (twice) | 0 | numbers in sections 7.5 and 17 (busy groove 310–327 µs per block, 0 dropped) |
| 10 | `meta-report` | 0 | `[META-REPORT] package list ready after 0.6 s`; 100 tasks (was 92) incl. the `Packages` group (7), 5 outstanding, **0 Required** (the same five as 20.1 row 9) |
| 11 | `build` | 0 | `[BAKE] 0 texture(s) baked, 14 already baked`; `[BUILD] result=Succeeded apkBytes=61234903 time=00:01:59.2` (full IL2CPP rebuild; +2.0 MB = the 14 ASTC textures and code); aapt2: the same five permissions, no `android.permission.INTERNET` |

Still not verified (no headset connected): `install`, `log` and every device item of section 19, notably the SRP foveation
taking effect (item 7), the latency split with `AudioEngine.LatencyProbe` (11), the IL2CPP audio cost (12) and the render
workers' JNI priority and intro GC behaviour (26).

### 20.8 10-upgrade integration (2026-11-01)

macOS, Unity 6000.3.27f1 batchmode, Burst 1.8.31, Apple M5 Pro (another agent's Unity running in a sibling clone), the
real project after the merge (section 21). `compile`: 0 errors, 0 warnings in MusicRoom code, no Burst diagnostics;
`render-setup` 48 templates; `tmp-import`; `bake` 35 textures; `configure` `[BURST-SETUP]` IL2CPP ARM64 Release
OptimizeSpeed MethodOnly, `[OPENXR-VALIDATION] 2 issue(s), 0 error(s)`; `scene` written (no `OVRPassthroughLayer`);
`test-edit` 756/756 (564 + engine 28 + piano 20 + drums 35 + harp 44 + room 17 + exp-hands 23 + exp-world 24 +
FinalDemoTests 1); `test-play` 221/221; `test-play --gpu` 248/248 (`Builds/Shots/integration/` checked by eye: the whole
room, the curtains, the home menu); `test-perf` 12/12; `meta-report` 99 tasks, 5 outstanding, 0 Required (the same
five); `build` `apkBytes=66309437` (63.2 MiB, was 65,129,515), 0 errors, the known 5 warnings (MR snow `pow` x2, clang `-x c++` notes; no IL2CPP pointer-cast warning), `lib/arm64-v8a/lib_burst_generated.so` 1,136,512 bytes, permissions HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY, no INTERNET, `com.oculus.handtracking.frequency` LOW. Open item: switching Classic sound dips the level smoothly by about 7 dB for ~30 ms mid-crossfade (no click; `RoomSoundTests` level-step limit 2.5 dB, measured 1.0-2.02 dB; the room clone measured 1.92 dB against its 2 dB limit).

Audio cost (editor, Burst, per 256-frame block of 5.33 ms): all four units at their worst at once
(`AllSoundsLoadTests`) 0.342 ms avg / 0.507 ms p99 / 0.548 ms worst offline; live on the audio thread the graph block
0.283 ms avg (p99 0.61 ms, 0 over budget) plus the listener stage 0.112 ms avg (p99 0.15 ms; one 2.2 ms outlier block
under machine contention). Alone: piano 0.059-0.078 ms (palms / 88 keys), drums vrWorst 0.173 ms, harp dense 0.033 ms,
room worst 0.079 ms; the bare graph at the LOD ceiling 0.25 ms; T-DENORM ratio 1.00. Today's classic groove unchanged
(303 us total of averages, 0 dropped, 0 stolen).

Listening: the final demo set (`Builds/Listen/final/`, with README.txt and levels.txt): piano, drums and harp as the new sound, the new sound
without the room, and Classic, rendered by `FinalDemoTests` through the integrated engine; onsets checked against the
scores (all 15 melody notes and the closing chord on every piano version, within 10 ms); no clipping (new sound peaks
-1.0 ... -7.4 dBFS through MasterBusV2's limiter).

### 20.9 Review round after the 10-upgrade (2026-11-01)

The same machine and pipeline, after the fixes of section 21's last entry. `compile` 0 errors, 0 warnings in MusicRoom
code; `test-edit` 762/762 (756 + the budget recovery, the Meta reference wiring, the large real room, the straight poke,
the strings' depth pass, the post-Tick kick); `test-play` 225/225 (+ `RoomCalibrationTests` x2, the dim-room hint, the
ride plate); `test-play --gpu` 252/252 (`Builds/Shots/exp-hands/finger-on-menu.png`: the index stays at the header it
pokes); `test-perf` 12/12 (all sounds at their worst at once 0.349 ms avg / 0.523 ms p99 / 0.553 ms worst offline; live
graph block 0.279 ms avg plus the listener stage 0.110 ms with the Meta reference; room live 100 us); `meta-report` 99
tasks, 5 outstanding, 0 Required (the same five); `build` `apkBytes=66322561`, 0 errors, the known 5 warnings,
`lib_burst_generated.so` 1,140,032 bytes, permissions HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY, no
INTERNET, hand-tracking frequency LOW. The final listening
demos were rendered again by `FinalDemoTests` and are unchanged (the reference is live-only; offline rendering did not
change: the room's null tests against Node still -103 ... -128 dB).

Meta reference (`RoomCalibrationTests`, editor Meta XR Audio 85): Meta minus model per octave at 1 m, 125 Hz - 8 kHz:
-2.6 -3.2 -1.4 +2.7 +6.3 +3.6 +1.8 dB; at 0.5 m 1.8 dB lower, beyond 1.2 m 0.5 dB higher; latency 20 samples at every
distance (the model's propagation delay 70-280 samples); the band + distance split fits within 0.47 dB. Piano seat (0.66
m), white noise, per ear, direct-to-room per band (125+250 / 500+1k / 2k+4k+8k): design 4.3 / 5.6 / 7.8 dB, Meta without
the reference 0.3 / 5.8 / 9.6, with it 4.1 / 5.6 / 7.7; worst octave 3.6 dB (2 kHz, the three-band limit; 5.3 before).

### 20.10 Merge of u-arrange and u-looper (2026-11-01)

The same machine and pipeline (one Unity for this folder; no other Unity running at the time), after the merge and the
follow-ups of section 21's last entry. Logs: `Logs/ci/final-chain.txt` and the per-step logs.
`compile` 0 errors, 0 warnings in MusicRoom code; `render-setup` 48 templates; `tmp-import`; `bake` 35 textures (the
new `Lessons/deck_palette2`); `configure` `[OPENXR-VALIDATION] 2 issue(s), 0 error(s)`, `[BURST-SETUP]` as before; `scene`
written; `test-edit` **819/819** (762 + u-arrange 17 + u-looper 32 + `ScheduleScopeSoundUnitTests` 8); `test-play`
**271/271** (225 + u-arrange 26 + u-looper 12 + 8 added at the merge: `GrabPinchGestureTests` 3, the drums and MR
Meta-pinch tests, the looper's Classic switch with the shipped units, the held keys on the new piano, arrange stopping
a loop), after one test fix: `RoomCalibrationTests.TheDirectToRoomRatioAtThePianoSeatIsTheDesigns` failed in two of six
full runs on its last check, the committed band-0 value against a fresh fit (-4.80 and -7.45 dB against -6.30 +- 0.75),
while every ratio check passed (the committed values gave the design's ratio within 0.5 dB per band). Band 0 is
ill-conditioned (the room's low band ends at the 300 Hz crossover: 1 dB of its gain moves its ratio by ~0.3 dB, and each
run's new noise seed moves the fit by up to +-1.5 dB), so band 0 now checks the ratio its committed value gives (within
0.75 dB; measured -0.48 ... +0.40 dB) and bands 1 and 2 keep the value check; every run of the test since has passed. Not a merge
effect (the room, its unit and the test are untouched by both clones); `test-play --gpu` **300/300** (252 + u-arrange 27 + u-looper 13 + 8; head camera in arrange mode 30
draws, budget 150; the booted scene 21 draws; shots in `Builds/Shots/<module>/`, `MUSICROOM_SHOTS_ROOT`);
`test-perf` **13/13** (all sounds at their worst at once 0.350 ms avg / 0.529 ms p99 / 0.557 ms worst offline; live graph
block 0.293 ms avg plus the listener stage 0.115 ms; looper six layers x 240 events a pass: the Recorder phase 724 us per
second of play, 10.1 us per 72 Hz frame, 45.8 us at most); `meta-report` 99 tasks, 5 outstanding, 0 Required (the same
five); `build` `apkBytes=66703589` (63.6 MiB), 0 errors, the known 5 warnings, no IL2CPP pointer-cast warning,
`lib_burst_generated.so` 1,139,328 bytes, permissions HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY,
no INTERNET, hand-tracking frequency LOW.

Screenshots checked by eye (`Builds/Shots/<module>/`): integration (the room, the menu with "Move instruments" and
Exit), arrange (start panel, home, the warm brackets and floor rings, the light-gold ray target, the gold held kit, the
raised piano: its camera moved off the bench, where it saw only the bench and the piano's underside), looper (the strip,
count-in, layer lights, the GUIDE walkthrough), menu (Instruments: five rows, Classic sound, arrange's footer).

---------------------------------------------------------------------------------------------------------------------------

### 20.11 The first five minutes (u-firstfive, 2026-11-01; not merged yet)

Run on the same Mac in `u-firstfive` (cut from `platform-base`), one editor at a time, the GPU
runs under the shared GPU lock. Logs in that clone's `Logs/ci`. Merge record: `Assets/MusicRoom/Guide/FIRSTFIVE-NOTES.md`.

| Check | Result |
|---|---|
| `render-setup`, `tmp-import`, `bake`, `configure`, `scene`, `compile` | all exit 0; 48 templates, 35 baked textures; 0 errors, 0 warnings in our code |
| `test-edit` | 846 / 846 (819 + 27: `FirstFiveLessonTests` 11, `SunnyCoverageTests` 8, `MenuFirstFiveTests` 8) |
| `test-play` | 287 / 287 (271 + 16: `FirstRunWalkthroughTests` 9, `TourTipsTests` 7) |
| `test-play --gpu` | 317 / 317 (300 + 16 + `FirstFiveShotsTests` 1); pictures `Builds/Shots/steps/` (and every module's under `Builds/Shots/<module>/`) |
| `test-perf` | 13 / 13; boot renders 225 in 2709 ms on 2 workers with Classic baked (149 in 2006 ms before), bank 133.5 MiB (96.8) |
| `meta-report` | 0 Required outstanding (5 optional, as before) |
| `build` | 68,023,983 bytes, 0 errors, 5 known warnings, no INTERNET permission |

Evidence worth keeping: a scripted prompt player hears its first note well under 10 s after the start panel opens and
finishes the whole first run with every step Done; a player who never moves (after answering the panel) gets every step
given up in time, Sunny opening and closing the menu, in under 4 minutes; a poke skips each of S2-S11; three in a row
jump to the palm menu; a Guide frame of the first run (a world-anchored demo, the perch search, the tips' signals, the
poke test) allocates 0 B over 120 ticks. Tips learned during play are saved only at a quiet moment (no PlayerPrefs.Save
in the frame the player plays). The palm-summon hotfix files are byte-identical to the base.

Not verified here: everything on a headset (section 19 items 67-78).

### 20.12 The many-instrument platform, phase 0 (u-platform, 2026-11-01)

Cut from `platform-base` (fingerprint 7e5b6ba02833df97908be46bc15e264996e5c4a2). The whole chain in
`u-platform/Logs/ci/platform-chain.txt` (render-setup, tmp-import, bake, configure, scene, compile, test-edit, test-play,
`test-play --gpu` and `test-perf` under the shared GPU lock, meta-report, build), all rc 0:

| Check | Result |
|---|---|
| compile | 0 errors, 0 warnings in our code; 16 modules |
| render-setup, bake, configure, scene | 48 material templates, OpenXR validation 0 errors, `Main.unity` written |
| test-edit | 962 / 962 (819 before: + the registry, lint, planner / join / carousel / stow / ray-pick vectors, control stream, audio platform tests, lesson packs, looper roles, Sunny coverage, the menu grid) |
| test-play (no GPU) | 402 / 402 (271 before: + the conformance suite, 6 x 8 cases, pointer claims, displayed silence, set / glide / ray summon / focus, MR lead, the classic adapters, the menu grid, generic lessons, tips) |
| test-play --gpu | 435 / 435 (300 before); head camera with the default set and five on display 21 draws, arrange mode 30, MR 26 (budgets 150 / 80) |
| test-perf | 15 / 15 (13 before: + the eight-instrument load test (offline p99 563 us; live graph p99 600 us; the five new roles are stub knocks) and the platform audio checks) |
| meta-report | 0 Required (the same 5 optional / recommended) |
| build | `VRMusicRoom.apk` 67,358,169 bytes (66,703,589 before), 0 errors, the 5 known warnings |

Screenshots: `Builds/Shots/layout/` (the room from the seat ahead, left and right; behind the seat
with the displayed instruments; the window and left walls; a glide mid-way; a set of three with a stub; the summon card) and
`Builds/Shots/menu/` (the Instruments grid, portraits, the replace / no-room / alone / options states). Not verified
here: items 79-87 of section 19 (device).

### 20.13 The first five minutes on the platform (b-firstfive, 2026-11-01; phase B)

`b-firstfive` is a copy of `instruments-base` with the first five minutes merged in (3-way, base `platform-base`; record
and every conflict: `Assets/MusicRoom/Guide/FIRSTFIVE-NOTES.md` section 5). One editor at a time; the GPU runs under
the shared GPU lock; logs in that clone's `Logs/ci`.

| Check | Result |
|---|---|
| `render-setup`, `tmp-import`, `bake`, `configure`, `scene`, `compile` | all exit 0; 48 templates, 35 baked textures, OpenXR validation 0 errors; 0 errors, 0 warnings in our code |
| `test-edit` | 989 / 989 (962 + 27: the first five's lesson, menu and coverage tests over the whole catalog) |
| `test-play` | 420 / 420 (402 + 18: the first-run walkthrough 10, the tour's tips 8 incl. one scheduler for every tip) |
| `test-play --gpu` | 454 / 454 (435 + 18 + `FirstFiveShotsTests` 1); pictures `Builds/Shots/phase-b/` |
| `test-perf` | 15 / 15; voice boot renders 113 / 113 in 806 ms on 2 workers, bank 81.1 MiB (the platform's lazy voice: phase A rendered 225 at boot, 133.5 MiB) |
| `meta-report` | 0 Required (the same 5 optional / recommended) |
| `build` | 68,732,042 bytes (67,358,169 the platform), 0 errors, the 5 known warnings |

Pictures: `Builds/Shots/phase-b/` (every first-run step, the closing invitation pointing at a
displayed instrument, the Instruments grid with the platform's tip, the Tips list, a landed tom target, the soft ghost
hand). Not verified here: section 19 items 67-78 and 88-90 (device).

### 20.14 All the instruments (int-all, 2026-11-01; the integration)

`int-all` is a copy of `b-firstfive` with the five real instruments and the music book copied in from their phase-B clones
(`b-handpan`, `b-handdrums`, `b-marimba`, `b-theremin`, `b-bowls`, `b-sheet`; each owned folder rsync'd and `diff -rq`
equal to its source) and every CONTRACT-REQUESTS item applied once (section 21). One editor at a time; the GPU runs
under the shared GPU lock; logs in that clone's `Logs/ci`.

| Check | Result |
|---|---|
| `render-setup`, `tmp-import`, `bake`, `configure`, `scene`, `compile` | all exit 0; 47 baked textures (the theremin's body, the handpan's four, the hand drums' six, the book's paper detail added); OpenXR validation 0 errors; 0 errors, 0 warnings in our code |
| `test-edit` | 1525 / 1525 (989 + the instruments' and the book's own tests + the integration's: the mirrored planner vectors, the five new descriptors equal to the vectors, every instrument's tips counted) |
| `test-play` | 461 / 461 (the conformance suite for all eight instruments, + the mirrored footprint check and the lesson re-demo test) |
| `test-play --gpu` | 504 / 504 (the sheet's shot test fixed and re-run: 2 / 2); pictures `Builds/Shots/<module>/` |
| `test-perf` | 21 / 21; all eight real units at their declared worst offline: avg 460 us, p99 641 us per 256-frame block; live on the audio thread p99 697 us (limit 2.1 ms) |
| `meta-report` | 0 Required (the same 5 optional / recommended) |
| `build` | 75,773,168 bytes (68,732,042 the first five on the platform), 0 errors, the 5 known warnings |
| In the main project (Assets, ProjectSettings, Packages, Tools, README replaced by int-all's; Library kept; the old ones backed up, Assets fingerprint 7e5b6ba0...) | the whole chain again: 1525 / 1525, 461 / 461, 504 / 504, 21 / 21, 0 Required, build 75,768,428 bytes |

Planner (the platform's vector generator, re-run with every instrument's data): fits 8 / 8 alone, 26 / 28 pairs,
35 / 56 threes (38 before: the marimba's air boxes keep it out of sets with the kit); stow plan ok; every ray
ok / ok / ok; the left-handed theremin fits 20 of the 29 sets with it (21 right-handed; piano + theremin + drums, the
menu's default for the theremin, does not fit mirrored); self-checks pass. Pictures: the default room from the seat,
each new instrument at the seat in a set, the Instruments grid with the instruments' own portraits, the book on the
piano, the start panel and the closing invitation (`Builds/Shots/<module>/`: `integration`, `handpan/phase-b`,
`handdrums/phase-b`, `marimba/phase-b`, `theremin/phase-b`, `bowls/phase-b`, `menu`, `sheet`, `phase-b`). Seen once and
not again in four runs: `InstrumentConformanceTests.TickAllocatesNothing` allocated for the theremin (one full run) and
the kit (one filtered run); the test now logs which call allocated in a probe pass before its assertion. Not verified
here: section 19 items 91-97 (device).

## 21. Change log

### 2026-10-25, review round 1 (perf-latency, merge-safety, sdk-correctness)

Every finding of the review was checked against the code, the specs and the package sources; all high and medium ones
are resolved, and every low one too. Verification: section 20.7.

Performance and latency
- **Periodic-wave-table oscillators recomputed the range per sample (high, real).** `Oscillator` now selects the two tables and
  their weight once per frequency (`Start` / `SetFrequency`; glides add `RangeStep(factor)` per sample, one log per glide);
  `Next()` is the two-table interpolation, bit-identical to `PeriodicWaveTables.Sample` for a constant pitch (EditMode
  `HoistedRangeIsBitIdenticalToThePerSampleReference`, 6 cases; glide within 1e-6). Spec 04 §4.9 optimisation 1 done:
  `MetalBank` pre-renders the six-square sum per metal ratio (1, 1.3, 1.7, 1.9) on the first render job, and `MetalVoice` reads
  it (the same floats as the live voice, `MetalBankSumEqualsTheLiveSixOscillators`). Measured (`test-perf`): six-square sum
  326–361 ns/sample before, 37–45 hoisted, 2.9–3.5 from the bank; the E12 busy groove with the tables ready totals
  310–327 µs per block (5.8–6.1 %), Ride 35–37 µs (sections 7.5, 17). Also fixed: `SynthSource.ResetStats` did not reset the block count, so
  `AvgBlockMicros` under-reported after a reset.
- **Latency target below its own components (medium, real).** Section 17 now states ≈ 26–51 ms expected (mean ≈ 37), to be
  measured; knobs `AudioModule.PreferredDspBuffer` (256, try 128; `AudioSettings.Reset` once in `Init` before any source) and
  `RigBootstrap.PreferredRefreshHz` (90); `AudioEngine.LatencyProbe` (dev device builds) logs one line per live call with the
  in-frame wait (`dspTime − FrameNow`), the frame time and the ring.
- **Start-up render garbage (medium, real; the robot comb part was 110 doubles, negligible).** Per-thread scratch reused
  (piano accumulator and noise, robot comb); every output buffer allocated on the main thread in `AudioEngine.Init` (one burst
  at boot, before the intro, instead of 110 heap growths while Sunny talks; a single arena would have needed offsets in
  `BufferVoice` on the audio thread for no further gain); on Android the workers call `Process.setThreadPriority(10)` through
  JNI (IL2CPP's `SetPriority` is empty) and detach on exit. Section 7.1 corrected.
- **Draw budget vs MaterialPropertyBlocks (medium, real).** Section 14: no MPB on URP Lit/Unlit renderers (shared material
  clones per state, vertex colours in unlit/particle shaders, or `RenderMeshInstanced`); per-rig draw budgets; MR casters
  limited to the instrument bodies. `HoverGlowPool` is now one dynamic 16-quad mesh (one draw, no MPB). Guard
  `DrawBudgetTests` (`test-play --gpu`, profiler `Draw Calls Count`: 1 draw for the stub scene).
- **Synchronous menu redraws (medium, real for URP).** Section 15.6: `SubmitRenderRequest` with a `StandardRequest`, content
  redraws only on content change (≤ 1 per user action), hover in the face shader (`_HoverRect`, `_HoverAlpha`).
- **`Rig.Handles` allocated per call (low, real).** Now `IReadOnlyList<Renderer> Handles` filled once; stubs updated;
  allocation test added.
- **`MidiOf` allocated (low, real).** Static degree table in `HarpRenderer` and the harp stub; allocation test added.
- **Mouse source and debug spheres on the headset (low, real).** Players never build debug joints and use
  `UntrackedHandSource` until the runtime is up.
- **Unbaked device builds (low, real).** `build` bakes missing textures and refuses to build without them; the player fallback
  logs `[Render] runtime texture` once and compresses / releases the CPU copy.
- **Soft vs hard MR shadows (low, real).** Hard shadows (sections 12, 14, 17).
- **Harp one-block delay on every pluck (low, real).** Section 15.3: ASAP for isolated pokes, the block-aligned base only for sweeps.

Merge safety
- **MR layout had no owner (high, real).** `ILayoutService` gains `ArrangeAroundPlayer(force, movePiano)`, `PlaceAroundPiano`,
  `RestoreHomes`, `Anchor`; `IMixedReality` gains `StartPlacing`; section 12 states who calls what (checked against spec 05:
  the reviewer's "summon and page turn off while Active" was corrected to the spec's rules: swivel off in MR, summon off only
  while placing or with the piano hidden, page turn off while placing). Stubs implement them; `LayoutStubTests` covers
  `PlaceAroundPiano`.
- **Portals needed Room-owned shaders (high, real).** `ISnowPortals` / `ISnowPortal` (Room implements; the stub returns a posed
  root); the shadow catcher moved to MR (its own shader and template); Room's Decor keeps running while hidden.
- **Shared textures and silent bake collisions (high, real).** Core `ProcTex` (Hash / VNoise / Fbm, Wood, DrumHead, Cymbal,
  Grille, SoftDot) + `SharedTextures` / `CoreTextureProvider` (`Core/<name>`, 14 textures, baked in 17 s), fingerprinted against
  golden values (`ProcTexTests`); bake names `<Module>/<name>`; `TextureBaker` and `MaterialTemplates.CreateAll` throw on a
  duplicate name. Labels are TextMeshPro, not a generator.
- **XR-only behaviour untestable (high, real).** `App.IsPresenting` + `BootOptions.SimulateHeadset`; every XR gate
  maps to it (sections 3.3, 15); test `SimulateHeadsetMakesTheAppPresent`.
- **Tests used the real PlayerPrefs and takes (high, real).** `ISettingsStore` (`PlayerPrefsSettingsStore`,
  `MemorySettingsStore`), `BootOptions.Settings` / `DataPath`, `App.DataPath`; `TestBoot` boots every PlayMode test with a
  fresh store and a temp folder.
- **Stick "near" pick-up could not reach Layout (high, real).** `StickPick IDrumsApi.TryPickUp(hand, tip)`; Layout calls it
  first in its Grab pass and applies the .02 handle exception (sections 4, 15.2, 15.5).
- **Desktop double-fire (medium, real).** One desktop model in section 0 (modules own clicks on `Layers.PlaySurface`, mouse-hand
  pointers never play: `HandState.IsMouse` / `Pointer.FromMouse`); `DesktopRigFallback.dragBlockMask` (wired by AppRoot to the
  play-surface layer rather than `Mouse.HitSurface`, which is also true over the floor) plus Shift / Alt; G and Shift removed
  from `MouseHandSource` (MMB fist, Tab switches the hand); Shift/Alt-drag in 15.5; colliders in every checklist.
- **No deterministic stepping (medium, real).** `BootOptions.ManualFrames` + `App.Step(dt)` (`HandInput.Step` + `Clock.Step` +
  `Frame`), `BootOptions.AudioClock` used by `IAudioEngine.Now/FrameNow`, `Clock.AudioNow`, the recorder and Sunny; test
  `ManualFramesStepHandsAndClockExactly` (22 steps of 1/72 s = the .3 s dwell, 0.72 m/s exactly).
- **Hidden menu never ticked (medium, real).** The Menu is always `IsPresent`; summon and animation at the start of its Tick.
- **No PanelUI layer (medium, real).** `configure` names 29 PlaySurface, 30 Hidden, 31 PanelUI; `Core.Layers`; SceneBuilder and
  AppRoot keep 30 / 31 off the head camera; the Menu asmdef references `UnityEngine.UI`.
- **EventBus stopped at the first throwing handler (medium, real).** Per-type handler arrays rebuilt on (un)subscribe, a
  try/catch per handler; tests for both.
- **Audio tap recorded only commands (medium, real).** `AudioTestTap.Calls` (`AudioCall` + index / vel / strike / pluck / when /
  handle) filled by every engine method; section 16.
- **Layout test data pointed at missing JSON keys (medium, real).** 15.5 cites the spec 07 §8 [measured] numbers.
- **Page-turn fade had no material contract (medium, real).** Slide and scale only in v1.
- **Foundation tests pinned module behaviour (medium, real).** Recipe, layout-home and menu assertions moved to
  `Tests/PlayMode/Audio`, `Layout`, `Menu`; rule 3 says those files belong to the module.
- **TMP label bake under -nographics (medium, real).** Labels render at runtime (no baked copy); `TextureBakeSpec.NeedsGpu` is
  refused by the `-nographics` bake with `[BAKE-ERROR]` (`NOGFX=0 Tools/unity.sh bake`).
- **Listener / StickReleases order (low, real).** Swapped to the spec order (StickReleases, Listener, NodKick).
- **Placing kept the menu ticking (low, real).** Now the spec's rule: nothing ticks while placing.
- **Test asmdefs lacked TMP / UGUI / URP (low, real).** Added.
- **Order bands overlapped (low, real).** Bands dropped (extra classes share their module's Order); one asmdef per module folder.
- **MrModule leaked a static handler (low, real).** Handlers kept and removed in `Shutdown`; rule in section 16.

SDK correctness
- **Foveation configured for two APIs (high, real).** `configure` enables Unity's `FoveatedRenderingFeature`, disables
  `MetaXRFoveationFeature` / `MetaXREyeTrackedFoveationFeature` (Android); `RigBootstrap.ApplyFoveation(1)` sets
  `XRDisplaySubsystem.foveatedRenderingLevel`; no `OVRManager` foveation calls; `report` prints the five feature states. The
  SRP API has no dynamic mode, so "High + dynamic" is now "High" (sections 0 and 14, section 19 item 7).
- **meta-report with -quit missed the Packages rules (medium, real).** `MetaReport` waits for Meta's package list without
  `-quit`; `unity.sh` fails when the `Packages` group is absent. Report: 100 tasks (was 92), Packages 7, 0 Required.
- **Unity's SpaceWarpFeature not pinned off (low, real).** Pinned off next to `MetaXRSpaceWarp`; in the report.
- **RunSpaceSetup without an XR check (low, real).** Guarded with `Rig.IsXrActive`.
- **Passthrough "available" while init pending (low, real).** `PassthroughAvailable` needs `IsInsightPassthroughInitialized()`;
  `PassthroughPending`; MR `Enter` waits ≤ 2 s.
- **VR clear colour never applied (low, real).** `XrMode.ApplyBackground` at start in both branches; `SetPassthrough(false)` no
  longer forces `isInsightPassthroughEnabled` (it would have turned it on in the editor rig).
- **Late runtime stuck in Desktop (low, real).** `RigBootstrap` keeps polling and switches to `Xr` (knobs, fallback off, event).
- **Subtarget record (low, real).** `bake` sets ASTC too; 20.2 says it is per machine.

Integration impact (stage-3 clones cut before this round, e.g. `m-*`, must take the new foundation folders
`Core`, `Hands`, `Audio`, `Editor`, `Tests/*/Core|Hands|Editor`, the moved module tests `Tests/PlayMode/Audio|Layout|Menu`, both test
asmdefs, `Tools/unity.sh`, `ProjectSettings/TagManager.asset` and `Assets/XR/Settings`, and then compile against these contract
changes): `Rig.Handles` is an `IReadOnlyList<Renderer>` (all rig owners); `ILayoutService` adds `ArrangeAroundPlayer(force,
movePiano)`, `PlaceAroundPiano`, `RestoreHomes`, `Anchor` (Layout implements); `IDrumsApi.TryPickUp` (Drums); `IMixedReality.StartPlacing`
(MR); `ISnowPortals` registered by Room; the Menu is always `IsPresent` and its asmdef references `UnityEngine.UI`;
`LessonsModule` passes `app.DataPath` and `app.AudioTime`; Guide times lines on `IAudioEngine.Now`; MR unsubscribes its static
events; tests boot through `TestBoot`. The stub versions of these members are in the foundation's copies of the module folders.

### 2026-10-31, integration (all ten modules merged into `vr-music-room-unity`)

Each module folder and its `Tests/*/<Module>` folders were copied from `m-<module>` (Audio from `m-audio`, a
three-way merge that keeps every round-1 foundation change; checked identifier by identifier). The project as it was was backed
up first. Logs of the final chain: `chain-final-part1..3.txt`.

Contract changes (every module `CONTRACT-REQUESTS.md` is marked APPLIED with its resolution). New interface members carry
a default body (C# default interface methods), so the modules' test stand-ins compile unchanged:
- `IPianoApi.InKeySpace(world)` (default = InPlayZone and piano-local y > -0.05) and `IPianoApi.OnSurface` (default false);
  the piano implements both, Layout uses both. `IInstrument.IsPresent` documents `Rig.InScene`; the piano now honours it.
- `ITutorial.Mistakes` (default `Summary.Mistakes`) and `ITutorial.Digit` (default max(1, ceil(Countdown))); the Menu reads them.
  `EventsDone` / `EventsTotal` documented as hits / total (targets, not events).
- `IAudioEngine.VoiceLineReady(id)` (Guide uses it) and `IAudioEngine.BlockSeconds`; `AudioModule.SnowPosition` /
  `WindPosition` (MR uses them). `ISnowWeather.Time` (default 0; Room implements).
- `DesktopInput.Ctrl`, `Meta`, `Command`: the piano row / Z / X and Shift+H ignore Ctrl / Cmd; the Piano and Guide asmdefs no
  longer reference `Unity.InputSystem`.
- `TextureBakeSpec.AnisoLevel` (TextureBaker applies it; Room's floor, walls, rug, bricks 4, night sky 8).
- `LitEmissive` template: `RealtimeEmissive` GI flag so URP keeps `_EMISSION` (the deck lamp glows now).
- `HoverGlowPool` material at render queue 3010 (over the lesson blocks at 3005).
- `IPlayZone` and `Rig.Handles` doc comments (Drums, Layout).
- Audio start-up race: the merged `SynthSource.Allocate` publishes the voice pool last; the integrator added the same guard
  to `AddInput` / `AddResonatorInput` and `playOnAwake = false` in `AudioRig.Build`. No `ArgumentNullException` in any
  merged PlayMode run (the Menu / Guide boot guards stay as no-ops).

Integration fixes (module interactions):
- One floor shadow per instrument: Room's `RigShadows` draws the piano's, the kit's and the harp's (`FloorDecal`, on the
  floor while a rig is lifted); the harp no longer builds its own blob (it would have been drawn twice). Harp: 5 own draws.
- Layout arms its "piano back to stand height" reset only when `MixedRealityEvent.Active` changes (MR republishes
  `Active = true` after every scene load, which dropped a table-placed piano to stand height).
- Tests written against stubs, adapted to the real neighbours: the drums probe now spies on the real Layout's
  `TryPickUp` call; the harp's desktop placement is checked about the seat ORIGIN (the desktop never arranges);
  `LayoutStubTests.PlaceAroundPiano...` places the piano inside the room box; the Room table tests count the Lessons deck
  (table 8 + deck 7 = the shared budget 15); the MR alpha check hides every renderer but the checked window; the piano
  seat-view check compares ivory with ebony (the real room's lit wall is as bright as the keys).
- New `Tests/PlayMode/Integration/IntegrationShotsTests` (Graphics): every module, simulated headset, first launch; asserts
  textures, Sunny's arrival, one shadow per instrument, all four rigs placed, the seated head camera <= 150 draws; writes
  `Builds/Shots/integration/*.png`.

Documentation requests recorded here: the Menu puts "Set up my room" right after the Room row (six home rows) and returns
`PlaySurface.None` from `PlaySurfaceAt` (a pinch on the panel grabs it); the Menu paints its canvas with one `CommandBuffer`
(accepted alternative to 15.6, device item 28); Room's bake names are `Room/plaster`, `Room/rug`, `Room/leaf_atlas`,
`Room/brick`, `Room/weave_normal`, `Room/scenery_atlas`, `Room/night_sky`, `Room/hill_snow`, `Room/wood_oak_gloss`,
`Room/wood_walnut_gloss` (woods and the soft dot are `Core/*`); the re-serialised TMP font asset (serializedVersion 8) is
accepted.

Verification (macOS, Unity 6000.3.27f1 batchmode, one editor at a time): `compile` 0 errors, 0 warnings in the whole log;
`render-setup` 45 templates; `bake` 35 textures (14 Core + Drums 1 + Guide 9 + Lessons 1 + Room 10), no `[BAKE-ERROR]`;
`configure` `[OPENXR-VALIDATION] 2 issue(s), 0 error(s)`; `scene` ok; `test-edit` 564/564; `test-play` 195/195;
`test-play --gpu` 216/216 (whole-scene `[DrawBudget] draws=20`, seated room 24-25 draws, integration head camera 24);
`meta-report` 100 tasks, 5 outstanding, 0 Required (the same five as 20.7); `build` `apkBytes=65129515` (62.1 MiB, budget
90 MB), permissions HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY, no `android.permission.INTERNET`.
Not verified: anything on a headset (section 19).


### 2026-11-01, 10-upgrade integration (Burst audio engine, four new sounds, Classic sound, experience upgrades)

The user asked for the best use of Unity: a room that sounds real and the best experience. The revised specs in
`Docs/10-upgrade/` (burst, piano, drums, harp, room, experience; plug-in API `ENGINE-API.md`) were built in seven
clones and merged into the main project by per-file three-way merges (`git merge-file`): `u-engine` against
`integrated-v1`; `u-piano`, `u-drums`, `u-harp`, `u-room` against `engine-base` (= u-engine's final state); `u-exp-hands`
(E1-E3, E6) and `u-exp-world` (E4, E5, E7-E9, E11, burst.md 10.1) against `integrated-v1`. The project's palm-summon
hotfix (2026-11-01, `SummonGesture`, `MetaHandVisual`) was kept through the merge. Backup of the project as it was:
`unity-backups/pre-upgrade` (Assets, ProjectSettings, Packages, Tools, README.md, this file, the previous APK). No .meta
GUID clashes, no orphan or missing .meta files. Conflicts: 7, in 6 files (DrumKit.cs in two merges, HarpPlay.cs,
IAudioEngine.cs, the Core, Drums and Hands READMEs), each resolved keeping both sides (below).

Contract changes. Every new interface member has a default body, so test stand-ins compile unchanged.
- **Packages / settings:** `com.unity.burst` 1.8.31, `com.unity.mathematics` 1.3.3 (direct); IL2CPP ARM64 Release,
  OptimizeSpeed, method-only stack traces; `ProjectSettings/BurstAotSettings_Android.json` (Armv8-A + HALFFP,
  OptimizeFor Performance, safety checks off), applied by `BurstAudioSetup.Apply` from `QuestSetup.ConfigureAll`
  (`[BURST-SETUP]`). `QuestSetup`: hand-tracking frequency LOW, `enableFrameTimingStats` on, Unity OpenXR's Automatic
  Viewport Dynamic Resolution feature off. `MetaXRAcousticSettings.asset`: acousticModel ShoeboxRoom. Test asmdefs
  reference Burst + Mathematics with unsafe code. `Tools/unity.sh`: optional `TEST_FILTER`, `configure` greps
  `[BURST-SETUP]`.
- **Section 0 / 3.1 (rig):** 90 Hz default (`RigBootstrap.PreferredRefreshHz` 90, `FallbackRefreshHz` 72) with
  `RefreshGovernor` / `QualityRung` (rungs 90 Hz fov 0.5 -> 90/1.0 -> 72/0.5 -> 72/1.0; 2 s windows, p95 > 0.9 budget or
  > 1 % dropped, two bad windows step down, never up within a session), `FoveationLevel` 0.5, `SharpenType` Automatic,
  `DynamicResolutionMin/Max` 0.7 / 1.6 (SceneBuilder writes them). The scene has no `OVRPassthroughLayer` and
  `isInsightPassthroughEnabled` is false: `XrMode.StartPassthrough` / `StopPassthrough` / `PassthroughStartable`;
  `MrModule.PassthroughWaitSeconds` 4.
- **Section 4 (frame):** step 8a `App.Contacts.Update(dt)` (`ContactVisuals`) between StickHover and `HoverGlow.Update`;
  step 18 `App.FastMotion.Tick(dt)` after `Input.EndFrame`.
- **Section 6 (hands):** `MetaHandSource.RequestFastMotion / TryRequestFastMotion / FastMotionRequested`;
  `MetaHandVisual.Look {Ghost, Skin, PassthroughCutout}`, `SetLook`, `SetContact`, `ClearContacts`, `ContactLift`,
  `cutoutLatency` 0.039 s (`GetHandStateAtTime`), `UsingDelayedPose`; `HandInput.skinMaterial / cutoutMaterial / Look /
  SetLook`; `ContactMath`. Core: `Contracts/Contact.cs` (`IContactSurface`, `ContactHit`, `ContactUse`) registered by
  the piano, the kit and the menu; `FastMotionPolicy` / `FastMotionDriver`; `HoverGlowPool.Dim`.
- **Section 7 (audio):** the Burst graph (`Audio/Burst`, one Burst call per block from the first `SynthSource`) and the
  sound units (`Audio/Sound/<Role>`, plugged in by the `UnitHooks` / `SoundUnits` partial methods). `IAudioEngine` gains
  `ClassicSound`, `ClassicSoundPending`, `ActiveUnit(UnitId)`, `HarpPartialTau(stringIndex, partial)` (double: the
  harp unit's decays while it plays, else the old synth's; it won over E8's float `HarpPartialDecay.V2Tau` default, kept
  for the harp module's test doubles) and the overload `SetRoomBox(center, size, yawDeg, realRoom)`. `SynthOp.FadeAll`.
  `AudioEngine.Init` takes the `AppSettings`. `DrumZone.CrossStick` (= 6), `DrumImplement {Tip, Shoulder, Finger}`,
  `DrumStrike.Implement / Openness`; `PluckOptions.Ud / Uz`. Unit-specific APIs through `ActiveUnit`: `IPianoPedal`
  (PianoPedal(depth), PianoKeyUp, RestrikesInPlace), `IDrumKitSound` (SetHatOpenness, ChokeAt, ExpressiveDynamics),
  `IHarpStrings` (Touch, DampString, StringLevel/Amplitude, PartialTau, budget, BRIGHT_DB), `IRoomControl` (Amount:
  Off / `Faint` (stored as `web`; no early reflections) / Low / Natural / High) and `IRoomGeometry`. On the new path the room unit turns Meta's acoustics off on the
  instruments and the drum room, the drum sends to 0 and the drum-room bus to 0; `MasterBus` hands over to the listener
  stage (`ListenerGraph`, MasterBusV2) and, while a room unit exists, delays today's chain by 53 samples
  (`AudioGraphHost.ListenerLatency`) so the crossfade is time-aligned (`DynamicsCompressor.Settle` on hand-back).
- **Section 8 (instruments):** `IPianoSink.PedalDepth / KeyUp / RestrikesInPlace`; `PianoSpec.AUTO_PEDAL` 0.85,
  `STEP_DAMP` 2 (new piano only); `PianoAction.ContactKey / KeyTopY / ContactWiden`. `IDrumsApi.StickShadowSpheres`;
  `DrumKit.ModelledKit`, `Hit(..., Vector2? at)`; `DrumMath.ImplementOf / CrossingPoint`; `DrumSpec.FingertipImplement` =
  Tip (deliberate: the spec's Finger measured 11-29 dB quieter); `FlashScale` 0, `KitMotion` (cymbal tilt, head give,
  kick beater: drums 21 draws). Harp: `HarpGeometry.PlayUMax` .40 (was .70) for both sounds; the module-local
  `IHarpSound` gains `Physical / Touch / DampString / StringAmplitude / PartialTau`; `StringBlur` (three copies per string
  in the one transparent strings draw, alpha in uv1.w), `PluckFlashGain` 0.25.
- **Section 9 (menu):** Instruments screen row "Classic sound" (speaker icon; notes "Off: the new sound" / "On: the
  original" / "Preparing..."), six rows when the audio engine is present; the panel is an `IContactSurface`.
- **Section 12 (MR):** `PianoAnchorStore` (E9: save on place / grab release, restore on entry, follow drift, erase after 3
  failures); `MrModule` passes the floor anchor's yaw and size to `SetRoomBox`.
- **Section 13 / 14 (room, rendering):** curtains and a brass rod by the window (`Room/World/Curtains.cs`, in the Fabric
  and Brass batches; the acoustic model has the matching regions); box-projected reflection probe (QuestURP box
  projection on); 48 material templates (+ ContactShadow at queue 2991, HandSkin 3000, HandPassthrough 3999).
- **Section 18 (settings):** `musicroom.classicSound`, `musicroom.roomAmount`, `musicroom.mrPianoAnchor`,
  `musicroom.mrPianoOnTable`, `musicroom.anchorFailures`.

Integration fixes (interactions between the clones):
- `DrumKit.Tick` strike line: the striking part and the cross-stick (port:drums), the crossing point for the head dent and
  cymbal tilt (E7) and the stick's rebound speed (E2) on one call.
- Harp swing: the physical harp's measured swing (port:harp) now sets the SIZE of E8's modal blur, which keeps its SHAPE
  and per-partial decays (`BlurGain` = measured / (h e^(-t/tau1)); a sympathetic or re-excited string gets a fresh blur,
  `HarpPlay.MaxBlurRegain` 2). Today's harp keeps E8 exactly.
- `HarpBlurTests`: the contract default is now the old synth's decay when no harp unit plays.
  `HarpBlurShotsTests` waits real time as well as app frames (the physical swing follows the audio thread).
  `DrumKitPlayTests`: switching to Classic asserts that the drums switch at once (the piano and harp roles may still be
  preparing their buffers). `RoomSoundTests.ClassicSoundOnAndOffMidNoteNeverClicks` boots with the room unit only (its
  held note must be today's voice through both masters, as in the room clone) and allows a 2.5 dB level step (was 2:
  measured 1.92-2.02 dB, a pre-existing smooth dip mid-crossfade). `IntegrationShotsTests` honours `$MUSICROOM_SHOTS`
  (u-arrange, a sibling clone, writes to the same shared `Builds/Shots/integration/` folder).
- New tests: `EditMode/Audio/FinalDemoTests` (one demo per instrument, new / new without the room / Classic, through the
  integrated engine to `Builds/Listen/final/`; `OfflineAudio.RenderEach`), `PlayMode/Audio/AllSoundsLoadTests`
  (Performance: all four units at their worst at once, offline and live).

Verification: see section 20.8.

### 2026-11-01, review round (audio, experience) after the 10-upgrade integration

Two independent reviews of the merged project (`r-audio` clone; a static review of E1-E8). Every high and medium
finding is fixed and every low one too, in `vr-music-room-unity` itself. Verification: section 20.9.

Audio
- **The adaptive CPU budget could only go down (high, real).** It rose only on graph LOD drops, which the shipped units
  never cause (they scale their own caps by the budget: the piano's modes by Budget/1200, the harp's active strings by
  round(22 Budget/1200)), so one heavy second left them degraded for the session. `AudioGraphHost.AdaptBudget(..., restore)`
  now also climbs x 1.05 below 30 % of 2 ms without drops, back to `Config.Budget` and no further (`Tick` passes it).
  Tests: the rule's new cases, and `TheAdaptiveBudgetRecoversAfterALoadSpike` (piano + harp OfflineEngine, adaptive on:
  1200 -> lowered after 3 heavy seconds -> 1200, the piano's mode cap and the harp's 22 strings back after a quiet minute).
- **The room was not referenced to Meta's direct path (medium, real; room.md 6 and 10.2).** Measured in the editor's
  Meta XR Audio 85 (`PlayMode/Audio/RoomCalibrationTests`, white noise straight ahead at 0.5-2 m, per-ear octave energies
  against the model's direct of the same RoomBus tap): Meta is 2.6-4.8 dB quieter at 125-500 Hz, up to 6.3 dB louder at
  2 kHz, falls off more slowly up to 1.2 m (the model's law beyond), and has a fixed 20-sample latency and no propagation
  delay. New: `RoomConst.ReferenceDb` (per band at 1 m: -6.3 / +3.7 / +3.2 dB, fitted at the piano seat so the per-band
  direct-to-room ratio is the design's; the first-order 300 / 2400 Hz crossovers spread each band's gain over its
  neighbours, so it is not Meta minus model per band), `ReferenceDistDb` (8 points 0.5-2 m, dB re 1 m, the mean over
  the bands), `MetaLatencySeconds` (20 / 48000). The live room unit (`RoomUnit.MetaReference`, on unless offline)
  publishes them in `RoomParams` (`RefBand`, `RefDistM/Db`, `RefLatency`); per block and group `UpdateGroups` scales
  the ER band gains by RefBand x the distance part at the group's distance and takes d/c - 20 samples off its ER delays
  (`ErUpdate(..., bandGain, delayShift)`), the late send per group by the distance part, and the late input EQ by
  RefBand. Offline (reference tests, renders) it is off and bit-identical. Piano seat, white noise, per ear: direct-to-
  room per band design 4.3 / 5.6 / 7.8 dB, before 0.3 / 5.8 / 9.6, now within 0.3 dB; per octave within 3.6 dB (2 kHz
  drier, 8 kHz wetter: the three-band limit; before 5.3). Also `RoomUnit.ReadCapture(..., groups)` (the RoomBus inputs)
  and `SetReferenceBands` (calibration only). EditMode `TheMetaReferenceScalesAndAlignsTheRoomPerBandAndGroup`.
- **Switching to Classic sagged the level for ~0.6 s (low, real; the documented "7 dB dip" was misexplained).** Our
  room's reverb was cut in 21 ms while Meta's restarted from silence. `AudioEngine.RoomToClassicFadeSamples` = 19200
  (0.4 s) for the room unit only; `MasterBus` crossfades over any length.
- **Reflections past the ring piled up at 84 ms in large real rooms (low, real).** `ErUpdate` gives an image whose path
  is longer than the ring zero gains (delays held at the ring's end) instead of clamping it audibly; the tap list keeps
  its order, so the reflection fades out over a block (the review's `continue` would reorder the taps under a running
  ramp). EditMode `ALargeRealRoomDropsReflectionsBeyondTheRingInsteadOfStackingThem` (16 x 3 x 20 m).

Experience
- **E1 bent straight pokes sideways (high, real).** `ContactMath.NormalWeight`: the clamp fades out within 15-30 degrees
  of the surface normal (sin 15 .. sin 30), in `FingerRotation` and in `StickVisualMath.Step`; vectors unchanged. EditMode
  `AStraightPokeIsNotBentSidewaysByTrackingJitter` (+-0.3 degree tilts give no turn; continuous through the fade).
- **E8 strings wrote no depth (medium, real).** `HarpStrings.shader` gains a depth-only first pass `StringsDepth`
  (SRPDefaultUnlit, ZWrite On, ColorMask 0) that writes the cores only (`COLOR.a` 1 on the cores, 0 on the edge copies,
  `HarpInstrument`): fingers behind a string stay hidden in VR, the MR cut-out no longer punches through a string in
  front of the real hand. Tests: shader render state (EditMode), the cores' vertex alpha (PlayMode).
- **The kit's contact plane was y = 0, not the drawn surface (medium, real).** `DrumSpec.CymbalTopY` (the lathe
  profile's top) / `DrawnTopY`; `DrumKit.QueryContact` and `StickSurface` measure from the drawn head (+1 mm) and the
  plate's top (the under-plate limit still counts from the strike plane). The hit test is unchanged. PlayMode
  `AStickOnTheRideRestsOnThePlateItselfAndItsShadowLiesOnTop` (bow 0.6 r and 12 cm: drawn ball >= TipRadius over the top;
  the shadow on the top).
- Low: the menu is `ContactUse.Clamp` (its shadow was never visible and halved its hover glow); `ContactHit.ClipOrigin`
  (the pressed key's centre line; `ContactShadowPool` uv2, shader clip about it); `DrumKit.Tick` ages heads and the
  beater before the frame's hits (no `_fresh`: a nod, playback or click kick shows t = 0 once); MR finger shadows from
  the cut-out's delayed pose (`MetaHandVisual.DelayedJointWorld`); `App.Frame` wraps `Contacts.Update` in try/catch
  (marker in a finally); `HandsLowLightEvent` (Core, once per session when the FMM low-light guard trips) and Sunny's
  text-only hint "More light helps your hands" (`Sunny.Hint`, `GuideLines.MoreLightHint`; it waits for the tour, never
  sets `TourDone`). Contract additions, all with defaults: `ContactHit.ClipOrigin`, `ContactShadowPool.Add(..., clipOrigin)`,
  `HandsLowLightEvent`, `FastMotionDriver.LowLightEvents`, `MetaHandVisual.DelayedJointWorld`, `ContactMath.NormalWeight`.
- Not changed (a risk the review could not verify): the 90 Hz governor counts a window bad on GPU p95 > 0.9 budget
  regardless of dynamic resolution; needs a headset log (section 19 item 35).

### 2026-11-01, merge of u-arrange (start panel, Exit, "Move instruments", Meta's pinch) and u-looper (looper, deck walkthrough, ScheduleAt)

Two finished feature clones merged into the upgraded main project by per-file three-way merges (`git merge-file
--diff3`, current project / base / clone): `u-arrange` against `arrange-base` (the project
before the 10-upgrade, plus the palm-menu hotfix), then `u-looper` against `engine-base` (the
upgrade's engine stage). Every file that changed against its base (Assets, ProjectSettings, Packages, Tools, README.md)
was taken: 75 from u-arrange (30 new, 45 changed), 98 from u-looper (60 new, 36 changed, 2 deleted). Backup of the
project as it was: `unity-backups/pre-arrange-looper` (Assets, ProjectSettings, Packages, Tools, README.md, this file,
ENGINE-API.md). Not merged: `Assets/Resources/MetaXRAcousticMaterialMapping.asset` (+ .meta; Meta XR Audio wrote it in
the clone). Deleted: `Generated/Resources/Generated/Lessons/deck_palette.png` (+ .meta; the looper renamed the bake to
`Lessons/deck_palette2`, which `bake` writes; no asset referenced the old GUID). No .meta GUID clashes: the 45 new .meta
files have unique GUIDs, none used in the project. The palm-menu hotfix (`Menu/SummonGesture.cs` `SystemPinch`,
`Hands/MetaHandVisual.cs` alpha without `systemGesture`) is byte-identical to the backup: neither clone changed it
against its base (u-looper's copy is the old one, equal to engine-base, so the three-way merge kept the project's).
Feature records: `Assets/MusicRoom/Layout/ARRANGE-NOTES.md`, `Assets/MusicRoom/Lessons/LOOPER-NOTES.md`.

Contract changes, from the clones (every new interface member has a default body):
- **u-arrange** (ARRANGE-NOTES 6): `Core/Contracts/Arrange.cs` (`IArrangeMode`, `ArrangeModeEvent`, `ArrangeConstants`);
  `App.TickInstruments` ticks only the menu while arranging; `MenuScreen.Start / Arrange`, `IMenuPanel.StartPending`;
  home rows Lessons, Instruments, Move instruments, Recordings, (Room), (Setup), Show me around, Exit;
  `BootOptions.StartPanel / Quit`, `App.StartPanel`, `App.Quit()`, `App.QuitRequests`; `HandMath.MetaPinchStart/End`
  (.85 / .5), `MetaPinchHoldSeconds`, `NextMetaPinch`, `PalmFacesPoint`; `HandInput.metaPinching / grabPinching /
  grabPinchStart / grabPinchEnd`; `HandState.GrabPinching / GrabPinchStart / GrabPinchEnd / GrabPinchFor / MetaPinching
  / PinchStrength`; `PointerInput.SystemGestureLike`; `GrabConstants.GrabStill` .04; settings `musicroom.arrange.all`,
  `.single.{piano,drums,harp}`, `musicroom.tipsSeen`; Piano `FitStand` (the stand follows the floor); Drums: no nod
  kick or stick glows while arranging.
- **u-looper** (LOOPER-NOTES 6, ENGINE-API.md section 10): `AudioSchedule` + `IAudioEngine.ScheduleAt(when)`,
  `IAudioEngine.Click(accent, when)`, `AudioCall.Click`; `ILooper`, `LoopStatus`, `LoopControl`, `LoopEvent`,
  `LoopEventPhase`; `IGuide.Tip(id)`, `ToggleDeckGuide()`, `DeckGuideRunning`; settings `musicroom.loop.tempo|bars|
  click|tidy`; Lessons: the looper, the deck strip (still 7 draws), `TapeDeck.TableLocalPosition` (0.07, 0.6, 0.04), bake
  `Lessons/deck_palette2`; Guide: `Sunny` states `Tip` and `Script`, `DeckGuide`, poke-to-skip, `DemoKind.Press`.
- **Section 11 (Guide):** 38 voice lines, one list in four places, in one order: `GuideLines.Ids` = `GuideVoice.LineIds`
  = `guide-lines.json` = the voice manifest: the original 22, `tip_arrange` (index 22, as u-arrange shipped it), then the
  looper's 15 from `tip_loop_start`; voice slots 200..237 of 240 (`AudioEngine.VoiceSlot0`).

Conflicts (textual: 16 files with 22 hunks in the u-arrange merge, 9 files with 18 hunks in the u-looper merge; each
resolved by reading both intents, no feature dropped):
- `Core/App/AppSettings.cs`: the upgrade's `KeyClassicSound` and arrange's `KeyArrangePrefix` / `ArrangeKey`: both.
- `Core/App/Events.cs`: the upgrade's `HandsLowLightEvent` and the looper's `LoopEvent`: both.
- `Drums/DrumsModule.cs` (StickHover phase): the upgrade's `Kit.Grab`, `BeginContactFrame`, `UpdateVisuals` (E1/E2/E3)
  and arrange's `!Arranging` guard on the stick glow: the guard on the upgrade's lines.
- `Menu/MenuController.cs`, `MenuRow.cs`, `MenuIcons.cs`, `MenuStyle.cs`, `MenuModule.cs`: the Classic sound row (field
  `Sound`, `RowKind.Classic`, `IconKind.Sound`, its labels, the panel's `IContactSurface`) and arrange's rows (field
  `Arrange`, `RowKind.Arrange / Exit / PlayHere / PlayRoom / Done / Reset`, icons Move, Exit, House, Check, Undo, the
  footer "To move them: Move instruments, on the first page", the Done / Reset hover): all kept, the enums with the
  upgrade's members first (no value is persisted). The Instruments screen: the five rows, Classic sound sixth, arrange's
  footer; Home: arrange's rows.
- `Piano/PianoInstrument.cs`: the upgrade's `QueryContact` (E1/E3) and arrange's `FitStand`: both.
- `Guide/Sunny.cs`, `Guide/GuideModule.cs`: three features met here: the upgrade's text-only `Hint` (low light), arrange's
  `Sunny.Tip` (bool, a silent leave through `_silentLeave`, `InTip`, `TrackTip`: seen when Sunny left or on an arrange
  grab) and the looper's `Sunny.Tip` (void, state `Tip`, `TipId`, `TipHeard`, `TipDone`: seen only when heard to the
  end) plus its `Script` state and `DeckGuide`. Resolved to ONE tip: `Sunny.Tip(id)` returns bool (false for an unknown
  id or while Sunny is here), state `Tip`, `InTip` until gone, `TipId`, `TipHeard`, `Hold(id, TipT)`, a silent leave
  (`Leave(say)`: a hint keeps its caption, a tip hides it); `GuideModule.StartTip` (`ActiveTip`, `TipsShown`,
  `_tipStarted`) serves both the arrange trigger and `IGuide.Tip`; `TrackTip` marks a tip seen when heard to the end
  (the looper's stricter rule) or at once on an arrange grab (arrange's rule); one `KeyTipsSeen` / `TipSeen` /
  `MarkTipSeen`. `Dismiss` never sets `Done` for a hint, a tip or the walkthrough. Arrange's hold covers the tour and
  the deck walkthrough (a hint is not held: `!Sunny.Hinting`); the low-light hint and the first-launch tour wait while
  arranging; `_tipStarted` lets the tour auto-start after either tip.
- `Audio/Synths/GuideVoice.cs`, `Guide/GuideLines.cs` (5 hunks), `guide-lines.json` (3), `Voice/manifest.json`: the
  line lists, counts (23 vs 37 -> 38), texts and seconds in the one order above; guide-lines.json keeps arrange's
  `tips` block (plus a `tip_loop_start` entry) and the looper's `unityLines` note (plus a `tip_arrange` note).
- Tests: `GuideLinesTests` (the count from `GuideLines.Count`, 38, `tip_arrange` at 22, `tip_loop_start` at 23; new: the
  manifest's order is checked too, so all four lists are), `AudioEngineTests` (lines from `GuideVoice.LineCount`).
- READMEs (project, Core, Hands): both sides' sections.

Semantic conflicts and integration fixes (no textual conflict):
- **MR placing on Meta's pinch.** Arrange moved Layout's grab (which starts MR placing) to the grab pinch, but
  `MrModule.PinchingHand` / `PinchStartHand` still read the thumb-index `Pinching` / `PinchStart`: a light pinch could
  start placing and then never land the piano. Fixed by the grab-pinch follow-up below.
- **Screenshot folders.** The project's IntegrationShotsTests read `$MUSICROOM_SHOTS/integration`, the looper's five
  shot tests `$MUSICROOM_SHOTS_ROOT/<module>`, Room and Lessons `$MUSICROOM_SHOTS` as the exact folder, Harp and
  KitMotion `$MUSICROOM_SHOTS_DIR`, Piano `$PIANO_SHOTS`, the looper `$LOOPER_SHOTS`, ArrangeShots and ContactShots
  nothing. New `Tests/PlayMode/Core/ShotDirs.cs`: `$MUSICROOM_SHOTS_ROOT/<module>` first in every shot test (15 files),
  then each test's older variable, then its default; the merged project's own run wrote to
  `Builds/Shots/<module>/`.
- **"Move instruments" during a loop.** Arrange silences everything (sticks back, a lesson or a playing take stops), but
  a loop waiting for its first note or recording its first layer would have gone on. `LayoutModule.BeginArrange` now
  ends a loop session as STOP ends it (`ILooper.Press(LoopControl.Stop)`: saved when it has something new), like a
  lesson does (`LooperPlayTests.MovingTheInstrumentsStopsTheLoopAndSavesIt`). The deck cannot be poked while arranging
  (it is an instrument, and only the menu ticks).
- **A looper test that asserted today's piano.** `LooperPlayTests.HeldKeysAreReleasedAtTheEndOfThePassAndOnStop` expected
  an `AudioCall.Release` at the pass end; on the modelled piano (the default since the upgrade) a pass-end release is the
  unit's key-up (`IPianoPedal.PianoKeyUp`, the voice follows the half pedal) and the next pass strikes the string again
  in place (`RestrikesInPlace`), so there is no Release call. Split: the original assertion with Classic on (ENGINE-API
  8: tests of today's recipe boot with Classic), and `...OnTheNewPiano` (both passes on their samples on the unit, no
  Release, every key up at STOP). `ClassicSoundSwitchedMidLoopWithTheShippedUnitsKeepsEveryNoteOnItsSample` repeats the
  looper's Classic test (which uses the Example unit) with the shipped piano and kit.
- **A flaky calibration check** (not caused by the merge): see 20.10, `RoomCalibrationTests` band 0. Over eight runs the
  fit ranged -4.8 ... -8.4 dB (mean about -6.7) around the committed `RoomConst.ReferenceDb[0]` -6.3, and the committed
  value left the 125-250 Hz direct-to-room ratio within -0.48 ... +0.40 dB of the design; not retuned.
- Clean auto-merges checked by reading the result: `App.cs` (arrange's `TickInstruments` and `Quit` beside step 8a),
  `AppRoot.cs`, `HandInput.cs` (Meta's pinch after the upgrade's `Derive`), `AudioEngine.cs` / `IAudioEngine.cs` /
  `AudioGraphHost.cs` (the looper's stamping beside the units' routing and the review fixes), the Guide / Menu READMEs,
  `MenuScreenshotTests`, `IntegrationShotsTests`.

**ScheduleAt through the new sound units (verified at the merge).** u-looper was built on engine-base, before the
piano, drum and harp units existed, so its scope was only tested on today's synths and the Example unit. Every new-unit
call the instrument modules make (piano strike / release / `PianoPedal` / `PianoKeyUp` from `PianoAction.PressKey` and
`Damper`; the kit's `Drum`, `Choke`, `SetHatOpenness`; the harp's `Pluck` with a sweep's `Delay`, `Damp`, `HarpTouch`,
`HarpDampString`) passes its `when` into `GraphCommand.When` and stages through `SoundUnit.Post`, which stamps; no unit
reads `Context.Clock` for an onset. New `Tests/EditMode/Audio/Burst/ScheduleScopeSoundUnitTests` (8): scoped calls render
bit for bit like explicit times on all three units; each unit sounds from exactly the scope's sample (lag 0) at block
offsets 0 / 17 / 131 / 255; a 14-pass 110 BPM loop on the new kit dispatched from 6-51 ms frames up to 80 ms ahead renders
bit for bit like exact posting and every pass's kick starts on its own sample (no drift); the seconds <-> samples round
trip is exact for 3 days of audio clock. Classic mid-loop with the shipped units:
`LooperPlayTests.ClassicSoundSwitchedMidLoopWithTheShippedUnitsKeepsEveryNoteOnItsSample` (Classic at boot, then the
new sound with pass 2 already sent and back: the unheard kick is sent again for its sample on the new path, the piano
and the kick stay on their samples on both paths). The other looper PlayMode tests now run on the shipped units by
default (they ran on today's synths in the clone).

Follow-ups from the arrange work:
- **The grab pinch everywhere.** Root cause (ARRANGE-NOTES 3): OpenXR fingertip joint centres are about 17 mm apart
  when the tips touch (7.6 + 9.8 mm radii), above the thumb-index rule's 15 mm start, so pinches flickered on the
  headset. Now on `HandState.GrabPinch*` (the rule OR Meta's index pinch, strength .85 / .5 or IsIndexPinching): the
  stick pick-up (Layout's grab pass, already; it now also skips a system-gesture-like pinch), the hold of a stick
  (`DrumSticks.UpdateReleases`: Meta's pinch keeps it), the hi-hat stand pinch (`DrumKit.Tick`), the one-hand seat turn
  (`LayoutModule.TickSwivel`: starts on `GrabPinchStart`, ends when `GrabPinching` drops, so a flicker no longer ends it),
  the two-hand summon (`TickSummon`: `GrabPinching`) and the MR placing pinches (`MrModule.PinchingHand`: `GrabPinching`
  / `GrabPinchFor`; `PinchStartHand`: `GrabPinchStart`, which lands the piano and places a snowy window). The
  system-gesture exclusion is kept twice: HandInput freezes the grab pinch while `SystemGestureInProgress` is up (no
  edges), and the seat turn, the summon, the stick pick-up and the MR placing pinch also skip a hand for which
  `PointerInput.SystemGestureLike` holds (the palm already turned back toward the headset). The palm-menu summon and the
  page-turn sweep keep the thumb-index state (the hotfix, unchanged). Tests: `Tests/PlayMode/Layout/GrabPinchGestureTests`
  (3: Meta's pinch alone swivels through a flickering joint gap, two Meta pinches summon, the system gesture by flag or
  by pose does neither), `DrumsPlayTests.MetaPinchAlonePicksUpAStickHoldsItAndTogglesTheHat`,
  `MrModuleTests.AMetaPinchAloneStartsPlacingAndLandsThePianoOnTheDesk` (incl. a system-gesture pinch that lands
  nothing); `DrumKitTests` set the grab-pinch edge for the hat stand.
- **A calmer, warmer arrange highlight** (the user prefers realism to glowing UI): `ArrangeVisuals` brackets 4 mm (were
  7 mm), arms 18 % of an edge, 3.5-10 cm (were 24 %, 4-14 cm); alpha-blended (`UnlitTransparent`, the rays
  `ParticlesUnlitAlpha`) instead of additive; waiting a soft warm white #fff1dc at alpha .42 (ring .16; was cyan
  #5fd8ff .55 / .28), a hand's pinch point or ray on it a light gold #f2cb84 .8, held gold #ffc46a .92, blocked red
  #ff5a4a .92; the ray warm white .3, light gold .7 on an instrument, its dot gold .8. Same renderers, same draws.

Verification: see section 20.10.

### 2026-11-01, the first five minutes and Sunny's full tour (u-firstfive; not merged yet)

Built on `platform-base` for the First Five Minutes award (judges are first-time users). The merge record,
with every shared-file edit and the phase-B plan onto u-platform: `Assets/MusicRoom/Guide/FIRSTFIVE-NOTES.md`. Specs:
`11-first-five/sunny-tour.md`, `tutorials-audit.md`, `lessons-proposed.json`. Verified on the Mac (section 20.11);
nothing on a headset yet (section 19 items 67-78).

- **The first run** (`Guide/FirstRun/FirstRun.cs`, sunny-tour.md 2.2): S0 under the start panel (Sunny at 8 s points at
  "Play here", never answers it: integrator), S2 the first key with no countdown, S3 the answering phrase of Ode to Joy
  with soft chords, S4 the room's celebration (`IRoomMoments`: fire, lamps, snow, a harp chord), S5 drum, S6 nod, S7 harp
  sweep, S8 the palm menu, S9 its rows with the menu's own hover light walking down them, S10 close, S11 goodbye. 8 s
  re-demo, ~15 s give-up, "any key plays the next note" after 15 s (first run only), poke Sunny to skip (three in a row
  jump to S8). It starts on the first presenting frame under the start panel (the merge's known issue: it used to wait
  for the answer). TourDone + `musicroom.tourVersion` 2. "Show me around" replays it. S1 (fitting the piano) not built.
- **Poke to skip** (`SunnyPoke`) for everything Sunny does; `ChirpKind.Poke`. **The perch** keeps Sunny 0.12 m off every
  play zone and beside a shown panel.
- **Contextual tips** (`TourTips` 64, `TourTipRunner`, `TourSignals`, `TourFeatures` 94 features): every feature, menu row
  and gesture has a tour step or a tip whose words name the real control; P1 / P2 / P3, quiet moments, spacing, success
  learns a tip; the menu's Sunny screen (`MenuScreen.Guide`, `Tips`; `IGuideTour`). Tips run only with the start panel
  (the app; tests opt in). World-anchored ghost-hand demos (`SunnyDemoWorld`: Press, Strike, Sweep, Pinch, Point,
  PinchPull, Lift, Twist, Hold, PalmRest, Drag). The ghost hand is pale and warm (#f3ead8, alpha-blended .375).
- **Lines**: 114 (76 new, 3 reworded), Unity-only table `Guide/Tools~/sunny-lines.mjs`, rendered by
  `Guide/Tools~/render-voice.mjs` with eSpeak NG's `render()`; `AudioEngine.SlotCount` 240 -> 400.
- **Lessons**: `lessons-proposed.json` (36); B1 drum targets at 0.55 r toward the player on the surface; B2 a re-hit is no
  mistake; B6 "Next: <title>" and remembered courses (`ILessonProgress`); pale warm blocks; `LessonOptions` (NoCountdown,
  Accompany, QuietMistakes, Forgiving, Hidden: the platform's names), `ILessonLive`, `TutorialHitEvent`, the hidden
  first-run song `piano-ode-first`.
- **Looper**: layer 1 starts on the first note snapped to beat 1 (`FirstSnapBeats` 0.5).
- **Done / Reset** stop the finger on their face (`MenuModule.BarContact`).
- **The closing invitation** to bring other instruments (`bye_more`, `tip_more_instruments`) is rendered and covered, its
  trigger off (`FirstRun.InviteMoreInstruments`) until the new instruments merge.

Contract changes (Core): `Contracts/FirstFive.cs` (new: `IRoomMoments`, `ITourAnchors`, `ILessonProgress`, `ILessonLive`,
`TutorialHitEvent`, `IMenuGuideHooks`, `StartChoiceEvent`, `MenuRowEvent`, `IGuideTour`, `SunnyPokeEvent`,
`StepOutcome`, `GuideStepEvent`, `GuideTipEvent`); `Lessons.cs` (`LessonOptions`, `ITutorial.Start(id, options)`,
`Lesson.options`, `Lesson.accompany`, `LessonChord`: the platform's text); `Menu.cs` (`MenuScreen.Guide`, `Tips`).
Settings keys (section 18): `musicroom.tourVersion`, `musicroom.tipsOn`, `musicroom.tipsShown`, `musicroom.lessonsDone`,
`musicroom.lessonsBest` (and the existing `musicroom.tipsSeen`).

### 2026-11-01, the many-instrument platform, phase 0 (12-new-instruments/platform.md; u-platform)

- **Registry** (`IInstrumentDefinition`, `InstrumentDescriptor`, `App.Registry`) with an id block for 8 instruments
  (`InstrumentKind` / `RigId` / `UnitId` 5-9, `SourceId` 18-25 incl. `MarimbaMid`); `PianoDefinition`, `DrumsDefinition`,
  `HarpDefinition`; five stub instruments (`Handpan`, `HandDrums`, `Marimba`, `Theremin`, `Bowls`: `PlaceholderInstrument`,
  a knock unit each, one lesson, a text-only arrival tip) and an empty `Sheet` module; the six asmdef names listed once.
- **Displayed instruments** in the VR room (`StowPlan`, `IStowable`, `IRoomFurniture`); **My set** (`musicroom.set`,
  `SeatPlanner`, up to 3; the side table's alternate pose); **`Bring`** by menu tile, page turn (carousel piano, harp,
  theremin, bowls, handpan, marimba, hand drums, drums) and point-and-pinch (`RaySummon`), with glides (`RigGlide`);
  `PutAway`, `PreviewJoin`, `PlaceOf`, `Facing`, `Lead`; arrange mode for displayed instruments; merge-on-save.
- **Pointer claims** (step 3b), focus, **tool providers** (`IHandToolProvider`, `DrumToolProvider`, `HandToolMath`), per-
  instrument pointer lists by `ToolMask`, Fast Motion Mode from the `FastMotion` trait.
- **Generic recording** (`EventRole`, `EventTypeSpec.Held / Sampled`, `IInstrumentPlayback`, `ControlEncoder`, `LaneArbiter`,
  `TakeEvent.lane`, `PlaybackLanes`, 40000 events, `IRecorder.Add(ev, at)`); the looper by roles and lanes.
- **Lesson packs** + `ILessonTargets` (the generic tutorial path, B2 / B4 / B6, `LessonOptions`, `ILessonAccompany`,
  `ITutorial.CurrentEvent / CountNoun`); **per-module Sunny tips** (`TipSpec`, `IGuideTipSource`, merged line files and voice
  manifests, `DemoKind` in Core with keyframed presets and `Custom`), six platform tips with clips.
- **MR lead instrument** (`TryGetLeadPose`, `PlaceAroundLead`, the draw guard); the rig shadows as one mesh.
- **Audio:** unit-only roles, 4-bit handles, 26 sources (`AudioSourceTable`, descriptor send overrides), idle skip,
  `UnitShare` governor, `AudioPresence`, 4 dynamic early-reflection groups, `GuideVoice` merged and lazily rendered.
- **Menu:** the Instruments grid with runtime portraits (`PortraitStudio`) and vector fallbacks, the Lessons grid, instrument
  option rows, `PanelHome`, `MenuRowEvent` in Core.
- Every new interface member has a default body. Build guide for the six agents: 12-new-instruments/BUILD-GUIDE.md.
  Verification: section 20.12.

### 2026-11-01, the first five minutes merged onto the platform (phase B; b-firstfive)

The first five minutes (u-firstfive) merged onto `instruments-base`. Record, every conflict and its resolution:
`Assets/MusicRoom/Guide/FIRSTFIVE-NOTES.md` section 5. Verification: section 20.13.

- **One tip system.** The platform's `TipRunner` schedules every tip (one at a time, one spacing, one P3 window): the
  tour's 65 are TipSpecs of `Guide/FirstRun/TourTipSource.cs` (an `IGuideTipSource`) beside the platform's six and every
  instrument's; `TourTipRunner` is the runner's new `ITipPresenter` (fires the tour tips' triggers and successes, shows
  them its own way, and adds the quiet-moment rules, "Tips while I play" and Hide Sunny to EVERY tip). The menu's Tips
  list shows every tip of the runner; `TipRunner.PlayNow` plays a platform or instrument tip from it.
  `SunnyCoverageTests` run over the registry and every source (the first run's steps count as teaching).
- **Lines** moved to the module line file `Guide/Resources/GuideLines/FirstFive.json` + `VoiceManifests/FirstFive.json`
  (79, rendered lazily by the platform's GuideVoice); the Guide's 38 keep 3 reworded. My fixed `SlotCount` 400 dropped
  (their bank grows with the lines); `GuideVoiceFirstFive.cs` deleted; `ChirpKind.Poke` kept.
- **Lessons**: lessons-proposed.json in the platform's library (36 classic + the packs); B1 and B2 on their tutorial's
  classic path; `LessonOptions` and the accompaniment are theirs (a chord on the last note's beat now plays); Next / Done
  memory unchanged; `MenuRowIds` folded into theirs (`MenuRow.cs`).
- **The closing invitation is on** (`FirstRun.InviteMoreInstruments`): the ghost hand points at the nearest displayed
  instrument and pinches ("Want more? Point at an instrument and pinch."), then "You can also bring them from Instruments
  in the menu."; `tip_more_instruments` (3 minutes after the tour, nothing brought, VR) and `tip_options` (an
  Instruments tile's Options chip) added; `tip_instruments` names One alone; `harp_skip` no longer says "on your left".
- **The soft ghost hand**: a rounded palm, 16-sided fingers, depth written (one surface), edges fading from the player.
  `DemoKind` += `Sweep, PinchPull, Twist, Drag` (Core), with anchored presets in `SunnyDemo.Sample`.
- **Drum targets**: the phase-A "straddling" tom disc was a falling block; a real-kit test checks every landing on its own
  piece (`FirstRunWalkthroughTests.EveryDrumTargetLandsOnItsOwnPieceTowardThePlayer`).
- Platform tests adjusted with reasons: `LessonPackTests` (36 / 15), `VoiceManifestMergeTests` (intro 3.102),
  `SunnyAnchoredDemoTests` (28 presets), `RegistryLintTests` (the tour's classic scripts allowlisted with ceilings),
  `GuideTipsPlayTests` (its tour-overlap test on the classic tour: the first run's replay without instruments ends in 5 s),
  `GuideModuleTests` (the rounded palm's vertex count), `MenuScreenTests`, `IntegrationShotsTests` (18 modules),
  `LessonsPlayTests`, `AudioRecipeTests`.

### 2026-11-01, all the instruments (the integration; int-all)

The five real instruments and the music book merged onto the first five minutes. Verification: section 20.14. Every
CONTRACT-REQUESTS item of the six phase-B clones applied once; where two asked for different code, the generic version.

- **Owned folders** copied wholesale (rsync --delete; the stubs' identity `.meta` GUIDs are the clones' own, no GUID
  clash); `Generated/` re-baked rather than copied.
- **The shared PlayMode tests** (theremin R1 / R1b, adopted by the bowls R3, the handpan R3, the marimba RB3 and the hand
  drums R4): `Layout/InstrumentView.cs` + the `Stub(k)` view in DisplayArrangeTests, GlideTests, RaySummonTests,
  SetLayoutTests; PointerClaimsTests counts live onsets from the recorder.
- **EightInstrumentsLoadTests**: the theremin's R1c plus the bowls' R8 line; `IUnitWorstCase` moved to
  `Audio/Burst/Units/IUnitWorstCase.cs` (the bowls' file and GUID) and the copies in `Audio/Sound/Handpan`, `Marimba`,
  `HandDrums` deleted.
- **The knock** (handpan R1, its `Shared~/phase-b-shared.patch` with R2 and R4): `KnockUnit.Kernel` 200 renders a knock
  in any slot; IdleSkipTests, AudioPresenceTests and GenericTutorialPlayTests on the real handpan. The bowls' R2
  (UnitShareTests) resolved the same generic way: the knock added to an engine without the bowls' role, in its slot.
- **FastMotionDriverTests**: the hand drums' R2 (each instrument struck through its own conformance fixture) instead
  of the marimba's RB3 deletion: the marimba's case is kept and passes on the real marimba.
- **AudioEngineTests** (hand drums R5), made generic: every acoustic source's early-reflection send is its table row
  (`row.ErDb`), the kit's pieces +3 dB, the classic ones 0.
- **`MetaHandVisual.SetPalmLift`** (hand drums R1); the palm hotfix's alpha line and `SummonGesture.cs` byte-identical.
- **`SeatSpec.MirrorKey`** (theremin R3): `BoxesFor(mirrored)`, `PlayCentreFor`, `InstrumentDescriptor.IsMirrored(store)`;
  the planner (`SeatPlanner.FromRegistry(registry, store)`, `Inst.From(d, mirrored)`), the layout's at-seat boxes and
  conformance check 3 use the mirrored boxes; an `InstrumentOptionEvent` on that key re-plans the set and places it if
  it still fits. The theremin's `MirrorKey = "hand"`. make-vectors plans every set with it mirrored
  (`seatPlannerMirrored`), replayed by `SeatPlannerTests.TheMirroredBuildPlansAsTheReference`.
- **Planner data** (`Tools/SoundLab/platform/registry.mjs` and the stow plan): handpan R5 (cradle boxes, display box), bowls
  R1 (sill spot (3.09, -0.62), play centre), marimba RB1 (the built footprint, home z 0.47, two air boxes) and RB2 (the
  note), hand drums R3 (box A to 0.81, box B deeper, spot (2.42, -1.94)), theremin (boxes unchanged, `mirrorKey`).
  `SeatPlannerTests` now requires the five new descriptors to equal the vectors (it only reported them in phase 0).
- **Optional ones taken**: bowls R5 (the lesson re-demo shows the running lesson's LessonStart tip's demo), hand drums R7
  (`IRoomFurniture.Decor`, default-bodied), sheet R1 (section 14's wording), marimba RB2. Not taken: theremin R4 (the
  letters' material template; the TMP default material stays, to check in the IL2CPP build).
- **Found at the merge**: a module's Manual P3 tip (the book's "Your songbook") was shown at once, over the platform's
  waiting tips and the looper's first-use line; `TipRunner.Manual` now holds a P3 tip to the P3 rules, the quiet moments
  and the queue (0 now, the module asks again). The sheet's "current" page-map vectors re-made from today's
  lessons.json (the first five's 15 piano songs); its shot test turns back when Fur Elise, now the last song, is open;
  LessonPackTests' stub-region rule passes with no stub left.

### 2026-11-02, instruments always in front and within a foot, grab handles, "Real hands" (u-placement)

After a headset session: the instruments opened wherever an earlier visit had left them (a saved world pose, a mixed
reality anchor across the room, a table), "Move instruments" was never found, and the cut-out hands in "Play in my room"
looked like a bug. Records: `Layout/README.md` (the section of this date), `MR/README.md`, `Hands/README.md`,
`Guide/README.md`, and the stage report.
- Layout: every arrangement places each instrument at its default pose about the head now (single home, or the My set
  slot with the main one straight ahead), pulled into the reach band (its playing surface's near edge within 0.30 m of
  the chest point, `LayoutModule.Reach.cs`), on the floor; then the resolver. The saved placement is a per-instrument
  comfort offset from that pose (`ComfortCodec`, `musicroom.comfort.*`); old `musicroom.arrange.*` placements migrate
  once (implausible entries dropped) and are deleted. Grab handles (`LayoutModule.Handles.cs`, `HandleVisuals.cs`):
  pinch and hold to drag, turn, raise; drop settles clear of instruments, walls, the seat and the eyes in 0.2 s; their
  own hit zone through `IPointerClaimant`. "Play here" re-arranges in front (`StartChoiceEvent`).
- Core: `Contracts/Handles.cs` (`IGrabHandles`, `HandleDragEvent`, `HandleConstants`, `IHandLook`), `IMixedReality.FloorHeight`
  (default 0), `AppSettings.ComfortKey / GetComfort / SetComfort / MrRealHands` (`musicroom.mrRealHands`, default 0),
  `ContactVisuals` implements `IHandLook` (App registers it): Skin in mixed reality unless "Real hands"; the clamp is off
  only for the cut-out. `PlacementSolver.SettleOne(..., offSeat)`.
- MR: no anchor restore or save (an old anchor is forgotten), no automatic table (`TableOnEnter`, off), the real floor.
- Menu: the home page's "Real hands" row in mixed reality (`RowKind.Hands`, nine-row geometry `Home9H` 56 / gap 6).
- Hand drums: the palm lift runs with the drawn hands in both rooms (off only for the cut-out).
- Guide: `tip_handle` + `tip_handle_drop`, `tip_mr_hands`, `tip_mr_anchor` reworded; features `layout.handle`,
  `layout.front`, `mr.hands` (a row now).
