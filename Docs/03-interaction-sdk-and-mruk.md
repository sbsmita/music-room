# 03 · Interaction SDK 207 and MR Utility Kit 207

Scope: which parts of the Meta Interaction SDK (ISDK) to use, and how "Play in my room" reads the player's real room through MRUK: windows for the snow portals, tables for the piano.

Package roots are under `Library/PackageCache/`:
**MRUK** = `com.meta.xr.mrutilitykit@751b71eb977a/`, **ISDK** = `com.meta.xr.sdk.interaction@61177ebcfd9a/`, **IOVR** = `com.meta.xr.sdk.interaction.ovr@817fd81b9adf/`, **CORE** = `com.meta.xr.sdk.core@4c2eac90db2d/`.

**Code and data behind this spec**

| File | Content |
|---|---|
| MR sketch, runtime (now `Assets/MusicRoom/MR/`) | `MusicRoom.MR.asmdef`, `MrScene.cs` (makes MRUK from code, asks permission, loads the scene), `WindowFinder.cs`, `TableFinder.cs`. They compile with no warnings in Unity 6000.3.27f1 against MRUK 207.0.1. |
| MR sketch, tests (now `Assets/MusicRoom/Tests/PlayMode/MR/`) | PlayMode tests: `MrFindersTests` (6) and `SetupProbeTests` (3). All 9 passed in batchmode on a scratch copy of the project, with no headset attached. |
| `verification-report.txt`, `setup-probe-report.txt` | The raw output quoted below. |
| Sample-room survey | All 50 sample prefab rooms, 8 JSON rooms and 15 test JSONs, with label counts, windows accepted and the best table. Also recommended fixtures and SceneLabels bit values. |

**RUN** = observed in that test run. **UNVERIFIED** = not checked on a headset or not readable in source (the native MRUK library).

---

## 0. Decisions

1. **ISDK: no interactors.** Do not use PokeInteractor, PokeInteractable, PointableCanvas, HandGrab, Grabbable, ray, distance grab, locomotion, or the comprehensive rig. All interaction stays our own port: key depth, strikes, plucks, pinch grab with dwell and stillness guards, the menu poke. Reasons are in §1.2.
2. **Hand visuals: Core SDK, not ISDK.** Put `CORE:Prefabs/OpenXRCustomHandPrefab_L.prefab` under `OVRCameraRig.leftHandAnchor` and `_R` under `rightHandAnchor`, with our own material. ISDK `HandVisual` is an optional later polish (§1.4).
3. **The rig must be `OVRCameraRig` + `OVRManager` whenever MRUK is used.**
   - `MRUK.Awake` logs an error without one (MRUK:Core/Scripts/MRUK.cs:575-578; RUN).
   - World lock drives `OVRCameraRig`'s TrackingSpace (MRUK.cs:726-757).
   - The native lib reads the tracking-space pose from `OVRCameraRig.GetTrackingSpace()` (getter and setter at MRUK.cs:686-705, registered at MRUK.Shared.cs:147-148).
   - **Cross-spec risk:** spec 05 §1.6 assumes an XR Origin with XR Hands. Pick one rig for the whole app (§4).
4. **MRUK is made from code** on the first "Play in my room". Settings: `LoadSceneOnStartup = false`, `DataSource = Device`, `EnableWorldLock = false`. World lock goes on after a successful load in mixed reality and off when MR ends. The VR room snap-turns and moves the rig, which world lock would override.
5. **Permission:** ask for `com.oculus.permission.USE_SCENE` when the player picks "Play in my room", never at app start. Never launch Space Setup automatically. Offer it from a menu row instead.
6. **Windows:** take `WINDOW_FRAME` anchors and turn each into an upright rectangle. The anchor's `transform.forward` is the plane normal and points **into the room** (RUN, 56 of 56 windows). The portal rotation is `LookRotation(outward, up)`, with local +Z behind the glass.
7. **Tables:** take `TABLE` anchors. The origin is on the top surface and local +Z points up. Score them by distance, height and edge length. The piano faces the user at the middle of the nearest edge, which is the same point as MRUK's own `PositioningMethod.EDGE` (RUN).
8. **No scene, no window or no permission:** fall back to manual placement. The player points at a wall and pinches to place a 1.0 × 1.2 m window. Walls come from MRUK `WALL_FACE` anchors, otherwise from `EnvironmentRaycastManager` depth raycasts. Both need USE_SCENE. Without the permission there is no real-surface data at all (§2.11).

---

## 1. Interaction SDK 207

### 1.1 Verdict per component

| ISDK piece | Use? | Why |
|---|---|---|
| `OVRComprehensiveInteractionRig.prefab` (IOVR:Runtime/Prefabs/; a variant of `OVRInteraction.prefab`). The old "OVRInteractionComprehensive" is now `IOVR:Runtime/Prefabs/Legacy/OVRInteractionComprehensive_legacy.prefab`. | **No** | Measured at runtime under a bare OVRCameraRig (RUN): 1659 GameObjects, 713 MonoBehaviours, **41 interactors** of 12 types (poke, ray, hand grab, grab-use, touch grab, distance grab, teleport, turner, groups), 46 IHand, 240 renderers. It is controller, locomotion and ray UX we do not have. |
| `PokeInteractor` / `PokeInteractable` / `PointableCanvas` / `PointableCanvasModule` | **No** | Our poke is tuned on a curved panel: ARM 3 mm, HOVER 10 mm, DEBOUNCE 250 ms, SLOP 8 mm, BEHIND −60 mm, panel 0.32×0.24 m on an R = 0.8 m cylinder (spec 08). ISDK has its own model: `_radius` 0.005, `_touchReleaseThreshold` 0.002, `_equalDistanceThreshold` 0.001 (ISDK:Runtime/Scripts/Interaction/Models/Poke/PokeInteractor.cs:113-124), plus recoil, drag and pinning thresholds (PokeInteractable.cs:138-365). `PointableCanvasModule` is a `PointerInputModule` that takes over the EventSystem (Unity/PointableCanvasModule.cs:77). |
| `HandGrabInteractor` / `HandGrabInteractable` / `Grabbable` | **No** | They need a hand **Rigidbody with trigger colliders** (HandGrabInteractor.cs:52-56, asserts :261-274) and a Rigidbody with at least one collider on the object (HandGrabInteractable.cs:46-56, 215-251). Detection goes through physics. Our sticks use an analytic shaft distance with GRAB_RADIUS 0.12, DEAD_ZONE 0.24, OPEN_DWELL 0.25 s and LOST_GRACE 1.5 s (spec 05). ISDK grab rules and release do not reproduce those. |
| `Hand` / `IHand` data chain (`IOVR:Runtime/Prefabs/OVRHands.prefab`) | **Not for play logic** | The chain includes `LastKnownGoodHand`. When confidence drops, it replays the last good pose and reports it as tracked and high-confidence (ISDK:Runtime/Scripts/Input/Hands/DataModifiers/LastKnownGoodHand.cs:30-47). A frozen fingertip can hold a key down, or jump through a drum head when tracking returns. Our logic treats loss as loss (LOST_GRACE above). `HandFilter` with null `_filterParameters` does not filter (HandFilter.cs:97-100), which is the default in the prefab. |
| `HandVisual` + `OVRHandVisualLeft/Right.prefab` (ISDK:Runtime/Prefabs/Hands/) | Optional polish (§1.4) | It looks nice (`OculusHand.mat`), but it needs the IHand chain above, wired in the editor (§1.3). |
| `SyntheticHand` | Optional polish | It can curl the *visual* fingers around a held stick: `OverrideFingerRotations` :334, `LockJoint` :391, `LockWristPose` :480, `FreeAllJoints` :446 (SyntheticHand.cs). Only worth it with option B visuals. |
| `CanvasRenderTexture` + `CanvasCylinder` (ISDK:Runtime/Scripts/Unity/UnityCanvas/) | Spec 08's call | Display only. Spec 08 already lists it as an alternative. Its camera renders every frame. |

### 1.2 Why minimal (summary)
- **Feel:** every guard listed above was tuned on raw joints, including pinch start ≤15 mm and end >25 mm (spec 02). ISDK models would re-tune the app.
- **Determinism and tests:** our logic is one ordered pass per frame over plain joint arrays, so it runs headless on recorded joints. ISDK interactors run their own hover/select state machines inside interactor groups, plus physics triggers for grab.
- **Cost:** see the measured counts above. A seated, hands-only app needs two hand meshes and our pointers.
- **Package side effects** even when unused: native telemetry starts at `BeforeSceneLoad` (ISDK:Runtime/Telemetry/ISDKTelemetryInitializer.cs:31), and 614 runtime + 138 editor scripts compile. MRUK compiles without ISDK (its `Oculus.Interaction` reference is behind the `META_INTERACTION_SDK_DEFINED` version define, MRUK:meta.xr.mrutilitykit.asmdef), so dropping `com.meta.xr.sdk.interaction.ovr` from the manifest is possible if option B is never adopted (orchestrator's call).

### 1.3 Can ISDK be assembled from code at runtime?
- Most components expose `Inject*` methods, for example `PokeInteractor.InjectAllPokeInteractor(Transform, float)` (PokeInteractor.cs:1184), `PokeInteractable.InjectAllPokeInteractable(ISurfacePatch)` (:655), `PointableCanvas.InjectAllPointableCanvas(Canvas)` (PointableCanvas.cs:98), `HandVisual.InjectAllHandSkeletonVisual(IHand, SkinnedMeshRenderer)` (HandVisual.cs:355) and `OVRCameraRigRef.InjectAllOVRCameraRigRef(OVRCameraRig, bool requireHands)` (IOVR:Runtime/Scripts/Input/OVRCameraRigRef.cs:144). The pattern: create the GameObject inactive, add the components, inject, then activate, because dependencies are asserted in `Start`.
- **Blocker:** `FromOVRHandDataSource` has a serialized `_cameraRigRef` (FromOVRHandDataSource.cs:34-35) and **no injector** for it. Its injectors cover updateMode, updateAfter, handedness, transformer, skeleton and the optional OVRHand (:310-340), but not the camera-rig ref. It asserts the field in `Start` (:98) and dereferences it in `OnEnable` (:128).
  - `IOVR:Runtime/Prefabs/OVRHands.prefab` ships with `_cameraRigRef: {fileID: 0}` and `_trackingToWorldTransformer: {fileID: 0}` (lines 48, 51). Building Blocks wire them in the editor.
  - So the hand chain needs an editor-authored prefab: an editor script that sets the field via `SerializedObject.FindProperty("_cameraRigRef")`. Reflection would also work.
- Instantiating the comprehensive rig at runtime and calling `InjectAllOVRCameraRigRef` still raises 3 `AssertionException`s (RUN): `FirstPersonLocomotor` missing *Player Origin* and two `TunnelingEffect` missing *Center Eye Camera*.
- ISDK forces the OpenXR hand skeleton. `ISDK_OPENXR_HAND` is always defined (IOVR:Runtime/Oculus.Interaction.OVR.asmdef, versionDefine `"name": "Unity"`), and `FromOVRHandDataSource.Start` asserts `HandSkeletonVersion == OpenXR` (:114). The project already has `handSkeletonVersion: 1` (= OpenXR, CORE:Scripts/OVRHandSkeletonVersions.cs) in `Assets/Resources/OculusRuntimeSettings.asset`.
- Core hands cannot be configured from code either. `OVRHand.HandType` is `[SerializeField] internal` (CORE:Scripts/Util/OVRHand.cs:81-82), and so are `OVRMesh.SetMeshType` and `OVRSkeleton.SetSkeletonType`. Use the shipped left and right prefabs (§1.4).

### 1.4 Hand visuals

**Option A (recommended)**
- Instantiate `Packages/com.meta.xr.sdk.core/Prefabs/OpenXRCustomHandPrefab_L.prefab` (HandType 0) and `_R.prefab` (HandType 1, `_skeletonType` 1). They contain `[OVRHand, OVRCustomSkeleton]` plus a child skinned mesh `OpenXRLeftHand`/`OpenXRRightHand` with the `XRHand_*` bone hierarchy, and have `_updateRootPose: 0`.
- Parent them to the anchors and swap the `SkinnedMeshRenderer` material for ours.
- A package prefab is only built if it is referenced. Keep both in a bootstrap ScriptableObject under `Assets/Resources`, assigned by an editor script.
- Not `CORE:Prefabs/OVRHandPrefab.prefab` (OVRSkeleton + OVRMesh + OVRMeshRenderer): it is **left-only** (HandType 0, `_skeletonType` 0, `_meshType` 0 at lines 51/65/99).
- Useful `OVRHand` API (OVRHand.cs): `IsTracked` :140, `IsDataHighConfidence` :134, `HandConfidence` :176, `IsSystemGestureInProgress` :147 (suppress the summon gesture while the system menu gesture runs), `IsPointerPoseValid` :153, `PointerPose` :160 (the hand pointing ray for §2.8).
- Bones are `OVRSkeleton.BoneId.XRHand_*` (OVRSkeleton.cs:193-222).
- Joint *data* for play logic comes from spec 05 §1.6. Do not use the runtime's pinch (`GetFingerIsPinching` :342).

**Option B:** ISDK `OVRHands.prefab` (Hands + 2× `FromOVRHandDataSource` + `HandSkeletonOVR` + `OVRHandVisualLeft/Right`) under `OVRInteraction.prefab` (`OVRCameraRigRef`, `TrackingToWorldTransformerOVR` + `OVRHmd`). Wire it in an editor script as in §1.3. Use it for visuals only; play logic must not read the `LastKnownGoodHand` output. UNVERIFIED in this run.

---

## 2. MR Utility Kit 207

### 2.1 Project prerequisites
Current values are from the project's `Assets/Oculus/OculusProjectConfig.asset`. Enums are in CORE:Editor/OVRProjectConfig.cs:64-140.

| Field | Now | Set to | Effect (CORE:Editor/OVRManifestPreprocessor.cs) |
|---|---|---|---|
| `handTrackingSupport` | 0 ControllersOnly | **2 HandsOnly** | `oculus.software.handtracking` required=true + `HAND_TRACKING` permission (:692-733) |
| `sceneSupport` | 0 None | **1 Supported** | `USE_SCENE` permission when ≠ None (:1013-1024). Supported and Required add the same entry. MRUK's setup check asks for Required (MRUK:Editor/PST/OVRProjectSetupMRUK.cs). |
| `anchorSupport` | 0 | 1 (optional) | `USE_ANCHOR_API` is added anyway when sceneSupport ≠ None (:758-770) |
| `_insightPassthroughSupport` | 0 | 1 or 2 | `com.oculus.feature.PASSTHROUGH` (:805-811). Passthrough itself is another spec. |
| `OVRManager.requestScenePermissionOnStartup` | false | **keep false** | Startup request (OVRManager.cs:1265, 2494-2497). We ask on MR entry instead (§2.3). |

### 2.2 The MRUK component: facts that shape the bootstrap
- **Singleton:** `MRUK.Instance` (MRUK.cs:565). A second instance destroys itself (:579-584).
- **Native global context:** loaded at `BeforeSplashScreen` (MRUK.Shared.cs:56-149) and ticked every frame by a hidden `MRUKGlobalContext` made at `BeforeSceneLoad` (MRUKGlobalContext.cs:35-45), whether or not we add MRUK. Event callbacks arrive during that tick, on the main thread.
- **`SceneSettings` must be set before the first `Update`:**
  - `Update` reads `SceneSettings.LoadSceneOnStartup` (MRUK.cs:709) and `SceneSettings.TrackerConfiguration` (MRUK.Trackers.cs:241) every frame. `MRUKRoom` reads `SceneSettings.SeatWidth` while building each room (MRUKRoom.cs:490).
  - RUN: `AddComponent<MRUK>()` on an active GameObject left `SceneSettings == null` and threw `NullReferenceException` at MRUK.cs:709 every frame.
  - Create the GameObject inactive, add the component, set `SceneSettings`, then activate.
- **World lock defaults to `true`** (field initializer :314; RUN). When on, MRUK writes the TrackingSpace *world* pose each frame and warns if someone else moved it. Rotate via `TrackingSpaceOffset` instead (:326-330, 735-746). Turning it off resets the TrackingSpace local pose to identity (:750-756).
  - `IsWorldLockActive` (:323) turns false when the headset cannot localize the room. Show "return to your room" then (optional).
  - The config sets `useScenelessWorldLocking = true` (MRUK.Shared.cs:81), so MRUK may world-lock even with no scene loaded. UNVERIFIED: native code.
- **`OVRCameraRig.Instance` is cached** after the first `FindAnyObjectByType` (CORE:Scripts/OVRCameraRig.cs:137-151). Create the rig **before** MRUK, or MRUK logs "OVRCameraRig is not present, but MRUK requires it" (RUN).
- **Never move an `MRUKRoom`/`MRUKAnchor` transform** (Editor/dev builds log an error every frame, MRUKAnchor.cs:228-240, MRUKRoom.cs:349-359). Removed rooms/anchors are `Destroy`ed with their children (Utilities.cs:429-444): keep portals and pianos under our own roots and copy poses.

The bootstrap (full code in `Assets/MusicRoom/MR/MrScene.cs`):
```csharp
var go = new GameObject("MRUK"); go.SetActive(false);
var mruk = go.AddComponent<MRUK>();
mruk.SceneSettings = new MRUK.MRUKSettings { DataSource = MRUK.SceneDataSource.Device, RoomIndex = -1,
    RoomPrefabs = Array.Empty<GameObject>(), SceneJsons = Array.Empty<TextAsset>(),
    LoadSceneOnStartup = false, SeatWidth = 0.6f };
mruk.EnableWorldLock = false;            // MrScene.SetWorldLock(true) after a successful MR load
Object.DontDestroyOnLoad(go); go.SetActive(true);
```
`MRUKSettings` fields (MRUK.cs:481-545): `DataSource` (`Device`, `Prefab`, `DeviceWithPrefabFallback`, `Json`, `DeviceWithJsonFallback`; :71-99), `RoomIndex` (−1 = random), `RoomPrefabs`, `SceneJsons`, `LoadSceneOnStartup`, `EnableHighFidelityScene` (**disabled in 207**: V1 is always loaded, :523-532, 778-783), `SeatWidth`. The package prefab `MRUK:Core/Tools/MRUK.prefab` defaults to `DeviceWithPrefabFallback` with 10 sample rooms and `LoadSceneOnStartup: 1`, which is unsuitable as is.

### 2.3 Scene permission (USE_SCENE)
- Constant: `OVRPermissionsRequester.ScenePermission = "com.oculus.permission.USE_SCENE"` (CORE:Scripts/OVRPermissionsRequester.cs:96). The enum value is `OVRPermissionsRequester.Permission.Scene` (:65). `IsPermissionGranted` returns true off-device (:159-166).
- MRUK's own pattern (MRUK.cs:592-620): `Permission.HasUserAuthorizedPermission(...)`, then `Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks)` (UnityEngine.Android).
  - "If the permission request dialog is already active then this call will silently fail and we won't receive the callbacks" (MRUK.cs:616-619). MRUK polls in `Update` (:711-721).
  - `MrScene.RequestScenePermissionAsync` therefore awaits the callbacks **and** polls `HasUserAuthorizedPermission` each frame, with a 120 s cap.
- On denial, `LoadSceneFromDevice` returns `NoScenePermission`. MRUK checks the permission only after discovery finds no rooms (MRUK.Shared.cs:250-259). `EnvironmentRaycastManager` waits for the same permission (EnvironmentRaycastManager.cs:342-366).

### 2.4 Loading
```csharp
Task<LoadDeviceResult> LoadSceneFromDevice(bool requestSceneCaptureIfNoDataFound = true, bool removeMissingRooms = true,
                                           SceneModel sceneModel = SceneModel.V1)            // MRUK.cs:1050
Task<LoadDeviceResult> LoadSceneFromJsonString(string json, bool removeMissingRooms = true)  // :1143
Task<LoadDeviceResult> LoadSceneFromPrefab(GameObject scenePrefab, bool clearSceneFirst = true) // :1093
void ClearScene()                                   // :854  native ClearRooms, IsInitialized = false
static Task<bool> HasSceneModel()                   // :458  any room anchors at all (no MRUK needed)
OVRTask<bool> OVRScene.RequestSpaceSetup()          // CORE:Scripts/OVRAnchor/OVRScene.cs:49, pauses the app
```
- **Always pass `requestSceneCaptureIfNoDataFound: false`.** With `true`, no rooms means MRUK launches Space Setup, which pauses the app, then retries once (MRUK.Shared.cs:271-276).
- **Results** (`LoadDeviceResult`, MRUK.cs:115-204; mapping in MRUK.Shared.cs:1275-1304), with the `MrSceneStatus` each maps to:
  - `Success` → Loaded
  - `NoScenePermission`, `FailurePermissionInsufficient` → NoPermission
  - `NoRoomsFound` → NoRooms (Space Setup never run)
  - `DiscoveryOngoing` → Loading. Only one load runs at a time (:194-197), and that covers JSON and prefab loads too.
  - `NotInitialized` → NotAvailable. `OVRPlugin.initialized` is false, as in the editor without Link (:199-202). RUN: `NotInitialized`, `ovrPluginInit=False`.
  - Any other value (`Failure`, `StorageAtCapacity`, `FailureInsufficientView`, `FailureTooDark`, ...) → Failed: "Couldn't read your room" plus retry.
- Over Link, scene data needs *Settings > Beta > Spatial Data over Meta Quest Link* (warning text at MRUK.Shared.cs:263-268).
- **Load time** (editor, RUN): 18-72 ms for the sample JSON rooms (21-40 anchors). `IsInitialized` (:250) is true after the first successful load.

### 2.5 Events, order and refreshing
- MRUK-level events (`UnityEvent` properties): `SceneLoadedEvent` :263; `RoomCreatedEvent` :275, `RoomUpdatedEvent` :288, `RoomRemovedEvent` :300 (`UnityEvent<MRUKRoom>`). Room-level: `MRUKRoom.AnchorCreatedEvent`, `AnchorUpdatedEvent` and `AnchorRemovedEvent` (MRUKRoom.cs:256-267). The `Register*Callback` helpers are obsolete except `RegisterSceneLoadedCallback` (MRUK.cs:375-382), which also fires at once if the scene is already loaded.
- **Order inside one load** (MRUK.Shared.cs:1035-1201):
  1. A room GameObject is created and added to `Rooms`.
  2. For every anchor: the anchor is created, added to `room.Anchors` and the wall/floor/ceiling lists, then **`AnchorCreatedEvent`** fires.
  3. **`RoomCreatedEvent`** fires, after room info is computed.
  4. Discovery finishes, then **`SceneLoadedEvent`** fires.

  Consequence (RUN): a listener added to `AnchorCreatedEvent` inside `RoomCreatedEvent` sees 0 of the initial anchors. Handle the whole room on RoomCreated.
- **Reloads** (RUN, test JSONs):
  - **Same room UUID with 2 extra tables:** `RoomUpdatedEvent` fires once and `AnchorCreatedEvent` fires twice (MRUK's CRUDTests assert the same). The `MRUKRoom` object is reused. `RoomUpdatedEvent` fires only on a "significant change" (:1077-1082).
  - **Different room UUID:** `RoomRemovedEvent` fires (before the room leaves `Rooms` and is destroyed, :1097-1099), then `RoomCreatedEvent`.
  - `SceneLoadedEvent` fires on **every** successful load (3 loads, 3 events).
  - `ClearScene()` fires `RoomRemovedEvent` per room and sets `IsInitialized` to false.
- **MRUK never reloads by itself.** No C# code reacts to focus or Space Setup; the only `OnApplicationFocus` hooks are in `PassthroughCameraAccess`. In MR mode, call `LoadSceneFromDevice(false, true)` again on `OnApplicationFocus(true)` (the player may have edited Space Setup from the system menu) and after `RequestSpaceSetup`. The events above then carry the changes. Whether the native lib also auto-updates is UNVERIFIED.
- **`WindowFinder` pattern:** bind lazily (MRUK may be created later), call `RegisterSceneLoadedCallback`, listen to Room Created/Updated/Removed and each room's Anchor events, set a dirty flag, and rebuild once in `LateUpdate`.

### 2.6 Data model
- **`MRUKRoom`:**
  - Anchor lists: `Anchors` (MRUKRoom.cs:139), `WallAnchors` (:147, WALL_FACE or INVISIBLE_WALL_FACE per MRUK.Shared.cs:1124-1128), `FloorAnchors` (:162), `CeilingAnchors` (:177), `GlobalMeshAnchor` (:185). `IsLocal` (:110) is false for JSON and prefab rooms.
  - Queries: `GetRoomBounds()` :852, `GetRoomOutline()` :550, `IsPositionInRoom(Vector3, bool testVerticalBounds = true)` :843, `IsPositionInSceneVolume` :932/1008/1023, `GetKeyWall` :564, `FindLargestSurface(SceneLabels)` :1222, `TryGetClosestSurfacePosition` :1168/1180.
  - `MRUK.GetCurrentRoom()` (MRUK.cs:442) returns the room the headset is in, else the last one, else the first. RUN: with the multi-room `ThreeRooms` and no headset, it returned the first room.
- **`MRUKAnchor`:**
  - Shape and identity: `Label` (`SceneLabels` flags, MRUKAnchor.cs:114), `PlaneRect` (`Rect?`, local XY, not necessarily centred, :120), `VolumeBounds` (`Bounds?`, local, :126), `PlaneBoundary2D` (`List<Vector2>`, local XY polygon, :133), `Anchor.Uuid` (`Guid`, :139), `HasValidHandle` (:182).
  - Relations: `Room` (:146), `ParentAnchor` (:157; windows, doors and wall art point to their wall), `ChildAnchors` (:164).
  - Queries: `HasAnyLabel(SceneLabels)` :721, `Raycast(Ray, float, out RaycastHit, ComponentType)` :263, `IsPositionInBoundary(Vector2)` :308, `GetClosestSurfacePosition` :366, `GetAnchorCenter()` :459.
- **`SceneLabels`** = `1 << OVRSemanticLabels.Classification` (MRUKAnchor.cs:58-78; CORE:Scripts/OVRAnchor/OVRAnchorComponents/OVRSemanticLabels.cs:43-63). Values: FLOOR 1, CEILING 2, WALL_FACE 4, TABLE 8, COUCH 16, DOOR_FRAME 32, WINDOW_FRAME 64, OTHER 128, STORAGE 256, BED 512, SCREEN 1024, LAMP 2048, PLANT 4096, WALL_ART 8192, GLOBAL_MESH 16384, INVISIBLE_WALL_FACE 32768, UNKNOWN 131072, INNER_WALL_FACE 262144.
  - Unsupported labels, including the old DESK, become OTHER (OVRSemanticLabels.cs:108-109).
  - The C# path adds WALL_FACE to an invisible wall (:101-105). To be safe, filter on both WALL_FACE and INVISIBLE_WALL_FACE.
- **`LabelFilter`** (struct, MRUK:Core/Scripts/LabelFilter.cs:40): `new LabelFilter(SceneLabels? labelFlags = null, ComponentType? componentTypes = null)` (:57). A null label set means all labels (:123-131). For exclusion use `new LabelFilter(~flags)`; `Included`/`Excluded` are obsolete (:105-116).

### 2.7 Orientation conventions (Unity space; MRUK already converted from OpenXR via `FlipZRotateY180`, MRUK.Shared.cs:710-727, 849-873)
- **Every plane anchor's normal is its local +Z (`transform.forward`), facing into the room's free space.**
  - The plane raycast rejects rays with local `direction.z >= 0` and returns `normal = transform.forward` (MRUKAnchor.cs:503-519); `GetClosestSurfacePosition` uses forward as the normal (:449); `GetKeyWall` expects the other walls in front of a wall's forward (MRUKRoom.cs:583-589). Floors face up and ceilings face down (prefab loader MRUK.Shared.cs:512-518; up-facing test `forward.y >= InvSqrt2` at MRUKRoom.cs:1345).
  - **RUN, windows:** 56 of 56 (40 prefab rooms, 8 captured JSON rooms, 3-room `ThreeRooms`): `IsPositionInRoom(c + 0.3·forward)` true and `(c − 0.3·forward)` false; a ray from 0.5 m inside hits with `hit.normal = forward`; a ray from 0.5 m outside misses.
  - **RUN, walls:** 8 of 8 walls hit from the room centre had `hit.normal` pointing into the room.
- **Plane extents:** the plane lies in local XY at z = 0. Window frames have local +Y ≈ world up (sample rotations have x ≈ z ≈ 0). `WindowFinder` still projects the polygon onto an upright frame.
- **Volumes (TABLE, COUCH, STORAGE, BED, ...):**
  - The anchor origin is the **top centre** and local **+Z points up** (MRUKAnchor.cs:603: "anchor transform.position is at top of volume"; MRUKRoom.cs:971-972: "forward actually points upwards"). `VolumeBounds` z runs from −height to 0. TABLE, COUCH, BED and STORAGE also carry a top plane (`PlaneRect`) at z = 0 (MRUK.Shared.cs:441-448).
  - RUN, all 17 sample JSON tables: `forward = (0,1,0)`, volume max z = 0, `PlaneRect` equal to the volume's XY, heights 0.32-0.78 m.
- **Mapping the portal frame.** The portal layout is authored in a right-handed frame: x along the window, y up and z toward the player, with snow along −z.
  - Under the (x,y,z)→(x,y,−z) rule, the Unity portal rotation is `Quaternion.LookRotation(outward, Vector3.up)`: local +X is the same physical direction as the source x, +Y is up, and **+Z points out through the glass**. This follows from `Cross(Fa,Fb) = −F(a×b)` for the mirror F.
  - Snow volumes and offsets along source −z go along Unity local +Z. The frost offset +0.004 (source frame) becomes local −0.004.

### 2.8 Raycasts and pointing
- **`MRUKRoom.Raycast(Ray, float maxDist, LabelFilter, out RaycastHit, out MRUKAnchor)`** (MRUKRoom.cs:672). Also `RaycastAll` (:645) and three shorter overloads (:702-732). It tests each anchor's plane and volume analytically, without colliders or layers (MRUKAnchor.cs:243-301). `RaycastHit.collider` is null.
- **`GetBestPoseFromRaycast(Ray, float, LabelFilter, out MRUKAnchor, out Vector3 normal, MRUK.PositioningMethod)`** (MRUKRoom.cs:758-817).
  - On a volume top with `EDGE`: the position is the middle of the top edge nearest the ray origin and +Z points from the table toward the user. `CENTER` gives the top centre. MRUK's own comment names this case: "the edge may be more important (piano or pong)" (MRUK.cs:56-66).
  - On vertical surfaces, +Z is the surface normal. With no hit it returns `new Pose()` and logs.
  - RUN: `TableFinder.TryDescribe` gives the same point and direction as `EDGE` on all 17 tables, to 0.01 m. It does not need a ray.
- **Rays:**
  - Hand pointing ray → `OVRHand.PointerPose` (+Z forward) when `IsPointerPoseValid`.
  - The viewer ray → a centre-eye forward ray.
  - Wall rays are limited to t ∈ (0.05, 12] m in `TryManualWindowOnWall`.
- **Depth raycasts without a scene model:** `Meta.XR.EnvironmentRaycastManager` (MRUK:Core/Scripts/EnvironmentRaycastManager.cs:34).
  - API: `static bool IsSupported` (:187), `bool Raycast(Ray, out EnvironmentRaycastHit, float maxDistance = 100f)` (:203), `PlaceBox(...)` (:263). `EnvironmentRaycastHit` has `status`, `point`, `normal` and `normalConfidence` (:481-491).
  - It needs USE_SCENE. It takes several frames to become ready (status `NotReady`). It uses `OVRCameraRig`'s TrackingSpace unless `CustomTrackingSpace` is set (:310-319).
  - Enable it only while aiming at a wall or surface, because it adds cost on top of the Depth API (class doc).
  - For walls, accept `|normal.y| < 0.35`.

### 2.9 Windows → snow portals (`WindowFinder.cs`)
```csharp
public readonly struct WindowRect { Guid Id; MRUKAnchor Anchor; Vector3 Center; Vector2 Size /*x along, y up*/;
    Vector3 OutwardNormal; Quaternion Rotation /*LookRotation(Outward, up)*/; Vector3 InwardNormal, Right, Up; }
public class WindowFinder : MonoBehaviour {
    public bool currentRoomOnly = true;           // windows of other rooms sit behind real walls
    public IReadOnlyList<WindowRect> Windows { get; }  public bool HasScene { get; }  // MRUK.IsInitialized
    public event Action<IReadOnlyList<WindowRect>> WindowsChanged;   public void Rebuild();
    public static int  Collect(MRUKRoom room, List<WindowRect> results);
    public static bool TryGetWindowRect(MRUKAnchor anchor, out WindowRect w);
    public static bool TryManualWindowOnWall(MRUKRoom room, Ray ray, out WindowRect w);
    public static WindowRect ManualWindowAt(Vector3 pointOnWall, Vector3 inwardNormal, float floorY); }
```
- **Acceptance:**
  - Label `WINDOW_FRAME` with a `PlaneRect`.
  - `|forward.y| ≤ 0.5`.
  - Width and height each > 0.15 m, width < 8 m, height < 5 m.
  - The rectangle is the extent of `PlaneBoundary2D` (or the rect corners) along `right`/`up`, where `up = ProjectOnPlane(Vector3.up, inward)` and `right = rotation * Vector3.right`.
- **Key** portals by `WindowRect.Id` (stable across reloads); drop portals whose Id disappears.
- **Manual window:** 1.0 × 1.2 m. The centre height above the floor is clamped to [0.75, 2.1] m and the centre sits 5 mm off the wall toward the player. `Id = Guid.Empty`, `Anchor = null`.
  - RUN: from the centre of a room with no windows, 8 of 8 rays at 45° steps placed a window on a wall, inside the room, with the outward normal pointing away from the player.
  - Optional upgrade: persist the manual window with `OVRSpatialAnchor` (`SaveAnchorAsync` CORE:Scripts/OVRSpatialAnchor.cs:607, `LoadUnboundAnchorsAsync` :1136).
- **Search grace:** keep `SEARCH_GRACE = 2.5 s`. Map "no plane detection" to `MrScene.Status ∈ {NoPermission, NoRooms, NotAvailable, Failed}`, or Loaded with 0 windows. In those cases offer manual placement at once.

### 2.10 Tables → piano on a real table (`TableFinder.cs`)
```csharp
public struct TableSurface { MRUKAnchor Anchor; Vector3 TopCenter; Vector2 TopSize; float TopY, TopHeight /*above floor*/;
    Vector3 EdgeMidpoint, TowardUser /*horizontal unit*/; float EdgeLength, Depth, Distance, Score;
    Quaternion FacingUser /* LookRotation(-TowardUser, up): piano local +Z away from the user */; }
public static bool TryFindBest(MRUKRoom, Vector3 headPos, Vector3 headFwd, in TableQuery, out TableSurface best, List<TableSurface> all = null);
public static bool TryDescribe(MRUKAnchor table, Vector3 headPos, float floorY, out TableSurface s);
public static bool TryPointAtTable(MRUKRoom, Ray, Vector3 headPos, out TableSurface s, out Vector3 hitPoint, float maxDistance = 6f);
public static bool TryPointAtSurface(MRUKRoom, Ray, out Vector3 point, out MRUKAnchor a, out bool onTable, float maxDistance = 6f);
public static Pose PianoPoseOnTable(in TableSurface s, Vector3 chosenPoint);   public static float FloorY(MRUKRoom);
```
- **Geometry:**
  - The top is `PlaneRect` at local z = 0, falling back to the `VolumeBounds` XY at `max.z`. The table must have `forward·up ≥ 0.9`.
  - The nearest point is the head position clamped into the top rect, giving the horizontal `Distance`.
  - The **near edge** is the side whose sector, split by the rect diagonals, contains the head: `|dx|·hy ≥ |dy|·hx` → an x side. This is the same choice as MRUK's EDGE (MRUKRoom.cs:774-800).
  - `TopHeight = TopY − FloorAnchors[0].y`.
- **`TableQuery.Default`:** maxDistance 2.5, minHeight 0.30 (the `onTable` threshold), maxHeight 1.20, preferred band 0.55-0.95 with heightWeight 3, minEdgeLength 0.50, minDepth 0.25, wantedEdgeLength **1.326** (piano body width) with edgeWeight 0.5, behindWeight 0.5.
- **Score** (lower is better): `Distance + 3·(metres outside 0.55-0.95) + 0.5·max(0, 1.326 − EdgeLength) + 0.5·(1 − facing)/2`.
- **RUN results** with the head at room centre and y = floor + 1.2:
  - MeshOffice1 picks its 0.74 m desk (score 1.34).
  - MeshLivingRoom2 picks the 0.76 m table with a 1.32 m edge over a 0.36 m coffee table.
  - MeshBedroom3 finds none.
  - Per-room details are in the sample-room survey (table at the top).
- **Piano pose (`place()`):** at the chosen point (`EdgeMidpoint` for auto placement, or the pinch hit point), height `TopY + 0.092` (`tableLift = −BOT`), pushed 0.25 m away from the user (`PUSH_BACK`), rotation `FacingUser`. RUN: y matched to 1e-4 and forward·TowardUser < −0.99.
  - The piano's stand is hidden on a table (`setOnSurface`). On the floor (`onTable` false) it stands at hit + 0.72.
- **Improvements:**
  - On MR entry with a best table, start the placing reticle snapped to `EdgeMidpoint`, so one pinch confirms. Pointing elsewhere still uses `TryPointAtSurface`.
  - The `UpwardSurfaces` filter (TABLE | STORAGE | BED | COUCH | OTHER | FLOOR) keeps the hit-anything behaviour. RUN: a downward ray in a bedroom hit a BED at 0.67 m, which counts as a table.

### 2.11 No scene, no windows, no permission

| Situation | Detect | Windows | Piano |
|---|---|---|---|
| Permission denied | `MrSceneStatus.NoPermission` | No real data at all (MRUK and depth raycasts both need USE_SCENE). Show the placement hint. Pointing finds no wall, so no reticle. Suggest the permission in the hint text. | Place in front of the player on its stand, as in the VR layout. |
| No Space Setup | `NoRooms` (or `MRUK.HasSceneModel()` false) | Manual window via `EnvironmentRaycastManager` walls, if `IsSupported`. Menu row "Set up my room" → `MrScene.RunSpaceSetupThenReloadAsync()`. | Pointing via `EnvironmentRaycastManager.Raycast`, `onTable = y − floor > 0.3`. Floor y is 0 with the FloorLevel origin (UNVERIFIED). |
| Room but no windows | `Loaded` and `WindowFinder.Windows.Count == 0` | Manual window via `TryManualWindowOnWall` (MRUK walls) | `TryFindBest` / pointing |
| Editor without Link | `NotAvailable` | Dev only: `MrScene.LoadJsonSceneAsync(sampleJson.text)` | Same |
| Room changed (edited in Space Setup) | Room events on reload (§2.5) | Rebuild. Keep the manual window. | Keep the placed piano. Re-run `TryFindBest` only if not yet placed. |

### 2.12 Editor and batchmode testing with sample rooms
- **Sample data**, recorded on real headsets:
  - `MRUK:Core/Rooms/Json/Mesh{Bedroom1-3,LivingRoom1-3,Office1-2}.json` (`"CoordinateSystem": "Unity"`, with global mesh).
  - 50 prefab rooms in `MRUK:Core/Rooms/Prefabs/{Bedroom,LivingRoom,Office,Other,LargeScale}/`. LargeScale ones are multi-room homes.
  - CRUD fixtures in `MRUK:Tests/JSON/`. Counts and fixture picks are in the sample-room survey (top of this spec).
- **In a PlayMode test** (code under `#if UNITY_EDITOR`):
  ```csharp
  var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json/MeshLivingRoom2.json");
  var t = MrScene.LoadJsonSceneAsync(json.text);              // or LoadPrefabSceneAsync(AssetDatabase.LoadAssetAtPath<GameObject>(
  while (!t.IsCompleted) yield return null;                   //    ".../Rooms/Prefabs/LivingRoom/LivingRoom00.prefab"))
  Assert.AreEqual(MrSceneStatus.Loaded, t.Result);
  ```
  - The callbacks need frames: `MRUKGlobalContext.Update` ticks the native lib, so `yield`, never block.
  - Create an `OVRCameraRig` first.
  - Test assembly: see the MR test assembly's `.asmdef` (runners + `nunit.framework.dll`, `UNITY_INCLUDE_TESTS`).
  - Meta's own tests do the same through `MRUKTestBase` (MRUK:Tests/PlayMode/MRUKTestBase.cs:739-781).
- **Command** (one Unity per project folder; run on a clone):
  `Unity -batchmode -projectPath <clone> -runTests -testPlatform PlayMode -testFilter MusicRoom.MR.Tests -testResults <xml> -logFile <log>`
  RUN: about 36 s wall time including editor start. The 9 tests take 0.94 s. macOS editor dylibs exist for Mac and MacArm (`MRUK:Plugins/`).
- **Play mode in the editor (no Link):** a dev-only component calls `MrScene.LoadJsonSceneAsync(serializedTextAsset.text)` when the device load returns `NotAvailable`. (`DataSource = DeviceWithJsonFallback` + `SceneJsons` also works; it falls back only when 0 rooms load, MRUK.cs:787-830.) The Meta XR Simulator is not installed.

---

## 3. Sketch (excerpts; full files in `Assets/MusicRoom/MR/`)
```csharp
// WindowFinder.TryGetWindowRect: core of the rectangle fit
Transform t = anchor.transform; Vector3 inward = t.forward;            // into the room (verified)
if (Mathf.Abs(inward.y) > MaxNormalY) return false;                    // 0.5
Vector3 outward = -inward, up = Vector3.ProjectOnPlane(Vector3.up, inward).normalized;
Quaternion rotation = Quaternion.LookRotation(outward, up); Vector3 right = rotation * Vector3.right;
// x0..x1 / y0..y1 = extent of t.TransformPoint(p.x, p.y, 0) - t.position along right / up, over PlaneBoundary2D
Vector3 center = t.position + right * (0.5f * (x0 + x1)) + up * (0.5f * (y0 + y1));
window = new WindowRect(anchor.Anchor.Uuid, anchor, center, new Vector2(x1 - x0, y1 - y0), outward, rotation);

// TableFinder.TryDescribe: near edge, the same choice as MRUK EDGE
Vector3 local = t.InverseTransformPoint(headPosition);  Vector2 c = top.center, half = top.size * 0.5f;
float dx = local.x - c.x, dy = local.y - c.y;
bool xSide = Mathf.Abs(dx) * Mathf.Max(half.y, 1e-3f) >= Mathf.Abs(dy) * Mathf.Max(half.x, 1e-3f);
// xSide: edge at x = c.x ± half.x, EdgeLength = top.height, TowardUser = ±t.right (flattened); else ± along t.up

```
Integration:
1. The MR entry code calls `MrScene.LoadDeviceSceneAsync()`, then `MrScene.SetWorldLock(true)` if Loaded.
2. Add `WindowFinder` to the MR root and subscribe the portal manager to `WindowsChanged`.
3. Call `TableFinder.TryGetBestTable` (head = centre eye) when piano placement starts.
4. On MR exit: `SetWorldLock(false)`, then `MrScene.Clear()`.

## 4. Risks and UNVERIFIED items
- **Rig conflict with spec 05** (XR Origin + XR Hands) — orchestrator decision.
  - MRUK expects `OVRCameraRig`: error log, world lock, and the tracking-space pose for anchors (MRUK.cs:686-705 uses identity when there is no rig).
  - With an XR Origin, keep world lock off and the origin at identity in MR, or transform the anchor poses yourself. The anchor-pose behaviour without OVRCameraRig is UNVERIFIED.
  - Recommendation: OVRCameraRig + OVRManager, with joints from Core `OVRSkeleton`/`OVRCustomSkeleton` bones, or from XR Hands if it coexists with OVRPlugin under the OpenXR loader (UNVERIFIED).
- **Sceneless world lock** when `EnableWorldLock` is true without a loaded room is UNVERIFIED (native). This is why it stays off in VR.
- **Device behaviour** is not run on a headset: permission dialog flow, `RoomUpdatedEvent` after editing Space Setup mid-session, `EnvironmentRaycastManager` readiness time, and floor y vs `FloorAnchors[0]`.
- **`GetCurrentRoom()` in multi-room homes** picks by headset position (native). Seated play makes this stable. Without a headset it returns the first room (RUN).
- **Prefab rooms are synthetic.** The prefab loader rotates planes 180° itself (MRUK.Shared.cs:383, 470-473), so orientation claims rest on both sources: prefab rooms and captured JSON rooms both passed (RUN).
- The ISDK option B wiring (§1.4) is not run.
