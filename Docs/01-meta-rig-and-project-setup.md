# 01 · Meta XR Core SDK 207: rig, passthrough and project setup (built from code)

Scope: the XR plugin choice, the seated floor-level hands-only rig, passthrough for "Play in my room", Project Setup Tool automation, Player settings and desktop testing. Hands, MRUK, ISDK, audio and the render pipeline have their own specs.

**Path keys.** `CORE` = `Library/PackageCache/com.meta.xr.sdk.core@4c2eac90db2d`, `OXR` = `…/com.unity.xr.openxr@ef0033a586bf`, `XRM` = `…/com.unity.xr.management@df145a5bc89e`, `MRUK` = `…/com.meta.xr.mrutilitykit@751b71eb977a`, `ISDKOVR` = `…/com.meta.xr.sdk.interaction.ovr@817fd81b9adf`. Refs without a prefix are relative to `CORE`.

**Evidence.** *(src)* means read in the package source. **VERIFIED** means observed in batchmode runs on a scratch copy of the project (see §7). **UNVERIFIED** needs a headset or was not tested. Companion file: `01-project-setup-tasks.json` lists all 106 non-hidden Android Project Setup rules with their state before and after §4.3.

## 0. Decisions

1. **Unity OpenXR with Meta's "Meta XR" feature group; remove `com.unity.xr.oculus`** (§1).
2. **Rig**: an Editor scene builder instantiates `Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab` and configures it in code (option A). A code-only runtime rig (option B) serves PlayMode tests (§2).
3. **`TrackingOrigin.FloorLevel`**, a floor origin under the head at start ("local-floor"). The rig root moves only for snap turn and seat fit.
4. **Hands only**: `handTrackingSupport = HandsOnly`, `handTrackingFrequency = HIGH` (Fast Motion Mode for drum strokes; A/B on the headset).
5. **Passthrough `Supported`** (optional at install). It is initialized at startup; one `OVRPassthroughLayer` sits on the rig with `hidden = true` in the VR room. MR mode means `hidden = false`, camera alpha 0, and the room hidden (§3).
6. `sceneSupport = Supported`, `anchorSupport = Enabled` (MRUK windows), `boundaryVisibilitySupport = Supported`.
7. **Android**: IL2CPP, ARM64, min API 32, target API 34, Vulkan only, Linear, GameActivity, ASTC, LandscapeLeft, plus the custom manifest from `OVRManifestPreprocessor`. **VERIFIED**: an APK builds (§7).
8. **Setup automation**: one batch entry point that waits for Meta's `PackageList`, applies the explicit config, then runs a **filtered** blocking `FixTasks` (Required and Recommended rules with automatic fixes only). Never run an unfiltered FixAll (§4.2).
9. **Runtime knobs**, once `OVRManager.OVRManagerinitialized` is true: 90 Hz (72 Hz fallback), foveation High plus dynamic, CPU and GPU SustainedHigh, dynamic resolution 0.7 to 1.6 (§2.6).
10. **Desktop**: no Link on macOS and the XR Simulator is optional. Ship a `DesktopRigFallback`, because without XR `OVRCameraRig` leaves the camera at y = 0 (**VERIFIED**, §6).

## 1. XR plugin: OpenXR, not the Oculus XR Plugin

| Evidence *(src)* | Content |
|---|---|
| `Editor/OVRProjectSetup/Tasks/Implementations/OVRProjectSetupXRTasks.cs:72-90` (Unity 6+) | Recommended: "Beginning with v74, it is recommended to use the OpenXR plugin (com.unity.xr.openxr) instead of the OculusXR plugin (com.unity.xr.oculus)." |
| same `:108-126` | **Required**, `fixAutomatic:false`: "It's not recommended to install Oculus XR Plugin and OpenXR Plugin at the same time … OpenXR Plugin is the recommended plugin to use." **The current project fails this rule** (VERIFIED, `01b-baseline-packagelist.txt`) |
| same `:46-57` (`UNITY_EDITOR_OSX`) | Required on macOS: the OpenXR Plugin must be installed |
| `Editor/Rules/OpenXRSettings.cs:53-68` | Required: OpenXR must be an active loader. The fix removes `OculusLoader` and adds `OpenXRLoader` |
| `Editor/Rules/OpenXRSettings.cs:136-172` | Required: the "Oculus Touch Controller Profile" must be enabled (for OVRInput), even in hands-only apps |
| `Editor/MetaXRSimulator/XRSimConstants.cs:31-33` | The XR Simulator on macOS requires `com.unity.xr.openxr@>=1.13.0` |
| `Scripts/OpenXRFeatures/MetaXRFeature.cs:42-152` | The Meta XR Feature enables `XR_EXT_hand_tracking`, `XR_FB_passthrough`, `XR_FB_scene`, `XR_META_boundary_visibility`, `XR_FB_display_refresh_rate`, `XR_EXT_performance_settings`, `XR_EXT_local_floor` and more |

**Consequences** *(src)*:
- **Hands.** OVRHand/OVRSkeleton/OVRMesh run on OVRPlugin over Unity OpenXR. The skeleton must be OpenXR (`OVRRuntimeSettings.HandSkeletonVersion`, `Scripts/OVRRuntimeSettings.cs:37-49`; `OVR=0, OpenXR=1`). The ISDK makes this Required (`ISDKOVR/Editor/ProjectSetupTasks.cs`), and the project's `OculusRuntimeSettings.asset` already has `1`.
- **Passthrough** uses the same API on both plugins. **MRUK, ISDK and Audio** don't depend on the Oculus plugin (grep), and nothing in `packages-lock.json` depends on it.
- **Routing under OpenXR.** Foveation goes through `MetaXRFoveationFeature` (`Scripts/OVRManager.Features.cs:53-71`). CPU/GPU hints go through Unity `XrPerformanceSettingsFeature.SetPerformanceLevelHint` (`Scripts/OVRPlugin.cs:4216-4250`). Tracking origin goes through `XRInputSubsystem.TrySetTrackingOriginMode` plus `OpenXRSettings.SetAllowRecentering(true)` (`Scripts/OVRManager.cs:1859-1863, 1879-1891`).
- **Build gates.** `OVRGradleGeneration.OnPreprocessBuild` (`Editor/OVRGradleGeneration.cs:174-347`) fails on a non-Linear color space (`:283-289`), or when MetaXRFeature runs without OVRPlugin's OpenXR backend (`:185-188`; the backend is active, VERIFIED).

**Migration** (VERIFIED in the clone):
1. Delete `"com.unity.xr.oculus": "4.5.2",` from `Packages/manifest.json`.
2. Delete `Assets/XR/Loaders/OculusLoader.asset` and `Assets/XR/Settings/OculusSettings.asset` if they exist. They appear when the project is opened with the plugin present; the real project has no `Assets/XR` yet.
3. Configure loaders and features in code (§4.5).

## 2. The rig

### 2.1 Prefab assets *(src; the rig is VERIFIED to load by path)*

| Path (`Packages/…`) | GUID | Content |
|---|---|---|
| `com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab` | `126d619cf4daa52469682f85c1378b4a` | `OVRCameraRig` + `OVRManager` + `OVRHeadsetEmulator` |
| `com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab` | `835e735ca71bf78459fb2cababd74112` | `OVRHand`, `OVRSkeleton`, `OVRSkeletonRenderer` (off), `OVRMesh`, `OVRMeshRenderer`; left hand by default |
| `com.meta.xr.sdk.core/Prefabs/OpenXRCustomHandPrefab_L/_R.prefab` | `deccf1bd75222ab419fef0130b2a4d9e` (L) | `OVRHand` + `OVRCustomSkeleton`, OpenXR skeleton |
| `com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRComprehensiveInteractionRig.prefab` | n/a | ISDK rig, parented under an existing `OVRCameraRig`. `…/Legacy/OVRCameraRigInteraction.prefab` nests its own OVRCameraRig; don't combine it with ours |

**Prefab defaults to override (VERIFIED):**
- `OVRManager._trackingOriginType` = **0 (EyeLevel)**.
- CenterEyeAnchor camera: near 0.1, far 1000, `Skybox`, tag `MainCamera`.
- Hierarchy: `OVRCameraRig/TrackingSpace/{LeftEyeAnchor, CenterEyeAnchor, RightEyeAnchor, TrackerAnchor, LeftHandAnchor/{LeftControllerAnchor, LeftControllerInHandAnchor/LeftHandOnControllerAnchor}, RightHandAnchor/…, Left/RightHandAnchorDetached}`.

### 2.2 Option A (recommended): Editor scene builder

The Project Setup rules check scene components (passthrough camera background, `isInsightPassthroughEnabled`, dynamic resolution: `OVRProjectSetupPassthrough.cs:49-112`, `OVRProjectSetupRenderingTasks.cs:374-412`), so they can only validate a rig that exists in the scene.

```csharp
// Assets/Editor/MusicRoomSceneBuilder.cs (Assembly-CSharp-Editor; Meta/OpenXR asmdefs are autoReferenced)
public static void Build() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(
        AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab"), scene);
    rigGo.name = "PlayerRig";                                     // holds camera and hands
    var mgr = rigGo.GetComponent<OVRManager>();
    mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel; // edit mode: stores the field (OVRManager.cs:1835-1839)
    mgr.isInsightPassthroughEnabled = true;                       // PT initialized at startup, layer hidden in VR
    mgr.enableDynamicResolution = true;                           // values from the rule's fix (RenderingTasks.cs:389-411)
    mgr.quest3MinDynamicResolutionScale = 0.7f; mgr.quest3MaxDynamicResolutionScale = 1.6f;
    var so = new SerializedObject(mgr);                           // internal startup-permission fields (OVRManager.cs:1246-1273)
    so.FindProperty("requestScenePermissionOnStartup").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
    var pt = rigGo.AddComponent<OVRPassthroughLayer>(); pt.hidden = true;  // one per scene (OVRPassthroughLayer.cs:599-627)
    var cam = rigGo.GetComponent<OVRCameraRig>().centerEyeAnchor.GetComponent<Camera>();
    cam.nearClipPlane = 0.02f; cam.farClipPlane = 40f;            // near 0.02 m, far 40 m
    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); // passes the Required PT rule
    cam.allowHDR = false;                                         // keep eye-buffer alpha (§3.3)
    EditorUtility.SetDirty(mgr);
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/MusicRoom.unity");
    EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/MusicRoom.unity", true) };
}
```

### 2.3 Option B: code-only runtime rig (VERIFIED, PlayMode test `05-playmode-noheadset.txt`)

```csharp
var go = new GameObject("PlayerRig"); go.SetActive(false);
var mgr = go.AddComponent<OVRManager>();   // one per scene; a duplicate DestroyImmediate's itself (OVRManager.cs:2166-2175)
var rig = go.AddComponent<OVRCameraRig>(); // Awake -> EnsureGameObjectIntegrity builds TrackingSpace + 15 anchors + 3 cameras (OVRCameraRig.cs:216-245, 547-670)
mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
go.SetActive(true);
var cam = rig.centerEyeAnchor.GetComponent<Camera>(); // Unity defaults (near 0.3, far 1000, Skybox): set as in 2.2
```
Results: all three cameras are tagged `MainCamera`, and only the center one is enabled (`Camera.main == center`). `stereoTargetEye = Both` under the built-in RP (`OVRCameraRig.cs:623-628`). No `OVRHeadsetEmulator` is added.

**Execution order** (`.meta` files): `OVRManager -100`, `OVRHand -90`, `OVRSkeleton -80`, `OVRCameraRig -50`, `OVRPassthroughLayer +100`; our scripts run at 0 and so see fresh anchors in `Update`. For render-time poses use `OVRCameraRig.UpdatedAnchors` (`:162`); anchors are updated again in `onBeforeRender` (`:497-511`).

### 2.4 Seated and floor-level: mapping the rig

- **Rig model.** The floor origin sits under the head ("local-floor"). The rig holds camera and hands, and placements are relative to the head at the XR origin. The rig moves only for snap turn and the seat shift (`fitSeat`).
- **Unity** uses `FloorLevel`, which maps to `TrackingOriginModeFlags.Floor` with recentering on (`OVRManager.cs:1859-1863`). Recentering uses local-floor (`OXR/Runtime/OpenXRSpaceSettings.cs:9-27`), so the floor origin sits under the head at start and after a recenter. Keep `AllowRecenter = true`.
- **Avoid** `Stage` (`OVRProjectSetupXRRuntimeTasks.cs:30-55`), `Stationary` (experimental only, `OVRProjectSetupMiscTasks.cs:30-52`) and `EyeLevel`, which loses the real seated height (the keys sit at y = 0.72).
- **Recenter**: `OVRManager.display.RecenteredPose += …` (`OVRDisplay.cs:167`; `display` exists only after init) re-runs the layout (`arrangeAroundPlayer`).
- **Coordinates**: layouts are authored in a right-handed source frame (-z forward); Unity `(x, y, z) = (x, y, -z_src)`, `yawDeg = -yawRad_src * Mathf.Rad2Deg`. Procedural meshes must also flip local z, or they come out mirrored. Examples: piano `(0, 0.72, -0.45)` becomes `(0, 0.72, 0.45)`; drums `(0.2, 0, 0.26)` with `rotation.y = -π/2` become `(0.2, 0, -0.26)` at yaw +90°; harp `(-0.78, 0, 0.2)` at -83° becomes `(-0.78, 0, -0.2)` at +83°.
- **Snap turn**: rotate the rig, then restore the head position. That equals a rotation about the head:
  `rigRoot.RotateAround(rig.centerEyeAnchor.position, Vector3.up, -srcRad * Mathf.Rad2Deg);`

### 2.5 Settings map: build-time assets vs runtime properties

**Build-time.** `Assets/Oculus/OculusProjectConfig.asset` (`Editor/OVRProjectConfig.cs:152-249`), `Assets/Resources/OculusRuntimeSettings.asset`, OpenXR and XR Management assets, PlayerSettings:

| Setting | Exact name *(src ref)* | Value → manifest |
|---|---|---|
| Devices | `OVRProjectConfig.targetDeviceTypes` (`:152`) | `{Quest3, Quest3S}` → `com.oculus.supportedDevices=quest3\|quest3s` |
| Hands | `handTrackingSupport` (`:157`, enum `:64-69`) | `HandsOnly` → `oculus.software.handtracking required=true` + `HAND_TRACKING` |
| Hand frequency | `handTrackingFrequency` (`:158`, `LOW/HIGH/MAX` `:74-91`) | `HIGH` → `com.oculus.handtracking.frequency=HIGH` |
| Passthrough | `insightPassthroughSupport` (`:199-206`) | `Supported` → `com.oculus.feature.PASSTHROUGH required=false` |
| Loading screen | `systemLoadingScreenBackground` (`:242-249`) | `ContextualPassthrough` (Required when PT ≠ None) |
| Scene, anchors, boundary | `sceneSupport`, `anchorSupport`, `boundaryVisibilitySupport` (`:161-171`) | `Supported`/`Enabled`/`Supported` → `USE_SCENE`, `USE_ANCHOR_API`, `BOUNDARY_VISIBILITY` |
| Misc | `requiresSystemKeyboard`, `experimentalFeaturesEnabled`, `processorFavor` (`:193-218`) | `false`, `false`, `FavorEqually` |
| Skeleton, gamut | `OVRRuntimeSettings.HandSkeletonVersion`, `.colorSpace` (read at init, `OVRManager.cs:2405-2408, 2330-2331`) | `OpenXR`, `P3` |
| Stereo, latency | `OpenXRSettings.renderMode`, `.latencyOptimization` (`OXR/Runtime/Settings/OpenXRRenderSettings.cs:16-90`) | `SinglePassInstanced`, `PrioritizeInputPolling` (`MetaQuestFeature.cs:728-743`) |

**Runtime** (OVRManager component, serialized or set in code; static members are marked):

| Setting | Exact name *(src ref)* | Value |
|---|---|---|
| Origin | `trackingOriginType` (`OVRManager.cs:1823-1903`) | `FloorLevel` |
| Passthrough | `isInsightPassthroughEnabled` (`:1215`, polled each `Update` `:3048`) | `true` from start |
| Boundary | `shouldBoundaryVisibilityBeSuppressed` (`:1229`, applied only while PT runs, `:3780-3810`) | MR `true`, VR `false` |
| Dynamic resolution | `enableDynamicResolution`, `quest3Min/MaxDynamicResolutionScale` (`:638-669`, applied in init `:2367-2399`) | `true`, 0.7/1.6 |
| Refresh rate | `OVRManager.display.displayFrequency`, `.displayFrequenciesAvailable` (`OVRDisplay.cs:348-360`) | 90 if offered, else 72 |
| Foveation (static) | `OVRManager.foveatedRenderingLevel`, `useDynamicFoveatedRendering` (`:1570-1605`) | `High` + `true` (foveation level 1 = full); `HighTop` is clamped to `High` under OpenXR |
| Perf (static) | `suggestedCpuPerfLevel`, `suggestedGpuPerfLevel` (`:1399-1435`; no-op without HMD; `cpuLevel`/`gpuLevel` obsolete) | `SustainedHigh` both (the synth uses CPU) |
| Sharpen | `sharpenType` (`OVRPlugin.LayerSharpenType`, `:589-597`) | try `Quality` (UNVERIFIED) |
| FMM/WMM | `fastMotionModeHandPosesEnabled`, `wideMotionModeHandPosesEnabled` (`:1965-1974`) | `false` (UNVERIFIED) |
| Startup permissions | `requestScenePermissionOnStartup` etc. (internal, `:1246-1273`) | all `false` |

### 2.6 Runtime init (once XR is up)

`InitOVRManager` (`OVRManager.cs:2158-2472`) runs in `Awake` if `OVRPlugin.initialized`, otherwise in a later `Update` once the display subsystem exists (`:2512-2522, 2619-2632`). Until then `OVRManager.instance` and `OVRManager.display` are **null** (VERIFIED), so keep a direct component reference.

```csharp
IEnumerator Start() {
    for (float t = 0; !OVRManager.OVRManagerinitialized && t < 10f; t += Time.unscaledDeltaTime) yield return null;
    if (!OVRManager.OVRManagerinitialized) { desktopFallback.enabled = true; yield break; }      // §6
    var hz = OVRManager.display.displayFrequenciesAvailable;   // Quest 3/3S: 72/80/90/120 (UNVERIFIED list)
    OVRManager.display.displayFrequency = System.Array.IndexOf(hz, 90f) >= 0 ? 90f : 72f;
    OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High;
    OVRManager.useDynamicFoveatedRendering = true;
    OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
    OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
    OVRManager.display.RecenteredPose += layout.ArrangeAroundPlayer;
}
```
The perf hint needs `XR_EXT_performance_settings` (`OXR/Runtime/Features/Extensions/XrPerformanceSettingsFeature.cs:93-100`), which MetaXRFeature requests. Also enabling Unity's "XR Performance Settings" feature is harmless; whether it is needed is UNVERIFIED.

### 2.7 Hands under the rig (hands spec has the details)
Parent hand objects under `Left/RightHandAnchor`. `OVRHand.HandType` (`Scripts/Util/OVRHand.cs:82`), `OVRSkeleton._skeletonType` (`OVRSkeleton.cs:394`) and `OVRMesh._meshType` (`OVRMesh.cs:59`) are non-public serialized fields: set them with `SerializedObject` in the builder (`HandType` 0/1; `XRHandLeft/Right` for the OpenXR skeleton) or by reflection at runtime. Pinch: `OVRHand.GetFingerIsPinching`. FixTasks adds the define `OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING` (`HandTrackingSetupRules.cs:62-91`, VERIFIED).

## 3. Passthrough ("Play in my room")

### 3.1 Build-time (Required chain)
- `insightPassthroughSupport = Supported` (`Required` would hide the app from devices without passthrough).
- `systemLoadingScreenBackground = ContextualPassthrough`, Required (`OVRProjectSetupPassthrough.cs:148-162`).
- ARM64, Required (`:71-81`).
- Recommended for MR: `PlayerSettings.SplashScreen.show = false` and `PlayerSettings.virtualRealitySplashScreen = null` (`:115-143`).

### 3.2 Components and their rules *(src)*
- **OVRManager.** `isInsightPassthroughEnabled` must be true when an `OVRPassthroughLayer` exists (Required, `OVRProjectSetupPassthrough.cs:49-69`). `UpdateInsightPassthrough` initializes or shuts passthrough down asynchronously every frame (`OVRManager.cs:3658-3755`). States are `Unspecified/Pending/Initialized/Failed`, and a failure is never retried (`:3729-3731`). Static queries: `IsInsightPassthroughInitialized()`, `HasInsightPassthroughInitFailed()`, `IsInsightPassthroughInitPending()`, `IsInsightPassthroughSupported()`, `IsPassthroughRecommended()` (`:3824-3922`); event `OnPassthroughInitializedStateChange`.
- **OVRPassthroughLayer.** In 207 it is **always an underlay** at `compositionDepth = int.MaxValue`; there are no `overlayType`/`projectionSurfaceType` fields (`OVRPassthroughLayer.cs:528-554`). `OnEnable` adds a child `OVROverlay` (shape `ReconstructionPassthrough`, `:687-724`), which is submitted only while `isInsightPassthroughEnabled && IsInsightPassthroughInitialized()`. Knobs: `hidden` (`:50`; when no layer is visible, system passthrough pauses), `textureOpacity` (`:75`), `edgeRenderingEnabled`, `edgeColor`, `SetBrightnessContrastSaturation(...)`, `SetColorLut(...)`.
- **Camera** (Required, `OVRProjectSetupPassthrough.cs:83-112`; check in `Editor/Utils/OVRPassthroughHelper.cs:51-61`): `clearFlags == SolidColor && backgroundColor.a < 1`. Meta's MR sample uses `SolidColor` with `(0,0,0,0)` (`Samples~/MixedReality/MixedReality.unity`: `m_ClearFlags: 2`, `m_BackGroundColor.a: 0`).

### 3.3 Toggle VR room ↔ MR at runtime
Meta's sample flips `layer.enabled` and `isInsightPassthroughEnabled` together (`Samples~/TrackingOrigin/TrackingOriginSample.cs:89-90`). For an instant switch with no re-init, use `hidden` instead:

```csharp
public void SetMixedReality(bool mr) {
    if (mr && OVRManager.HasInsightPassthroughInitFailed()) { ShowHint("Passthrough unavailable"); return; }
    mgr.isInsightPassthroughEnabled = true;     // already on since startup
    pt.hidden = !mr;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = mr ? new Color(0, 0, 0, 0) : (Color)new Color32(0x05, 0x07, 0x0f, 0xff); // VR background #05070f
    room.SetActive(!mr);                        // walls/fireplace/window off; instruments stay
    mgr.shouldBoundaryVisibilityBeSuppressed = mr;
}
```
- The cost of keeping passthrough initialized but hidden is UNVERIFIED. The alternative is `isInsightPassthroughEnabled = mr`, with an async re-init: wait for `IsInsightPassthroughInitialized()` before hiding the room.
- **UNVERIFIED on device**: that the OpenXR projection layer blends over the underlay by eye-buffer alpha under the built-in RP. Keep `allowHDR = false`, since an HDR intermediate without alpha would drop it (likely, not verified). Write no alpha = 1 in post effects, and keep transparent materials from leaving alpha < 1 over solid geometry.
- `OVRManager.IsPassthroughRecommended()` reports the user's "passthrough by default" preference; use it to preselect the homepage choice.

### 3.4 Scene permission
The scene permission is `com.oculus.permission.USE_SCENE` (`OVRPermissionsRequester.ScenePermission`, `Scripts/OVRPermissionsRequester.cs:96`); `horizonos.permission.USE_SCENE` occurs in no package. MRUK requests it itself in `Awake` on Android when `SceneSettings.LoadSceneOnStartup` uses a device data source (`MRUK/Core/Scripts/MRUK.cs:588-625`). So keep `requestScenePermissionOnStartup = false` and create MRUK only when the user picks "Play in my room". An unfiltered FixAll sets that flag to true through an Optional rule (VERIFIED).

## 4. Project setup automation

### 4.1 Rules: where they live and how to read them
- **Where.** `Editor/OVRProjectSetup/OVRProjectSetup.cs` (`TaskLevel`/`TaskGroup`/`TaskTags`, `:54-90`). Rules are in `Editor/OVRProjectSetup/Tasks/Implementations/*.cs`, `Editor/Rules/*.cs`, `Editor/FeatureActiveRules.cs`, `Editor/BuildingBlocks/**/*SetupRules.cs`, `Editor/MetaXRSimulator/ToolbarItem.cs`, `MRUK/Editor/PST/OVRProjectSetupMRUK.cs` and `ISDKOVR/Editor/ProjectSetupTasks.cs`.
- **Public API.** `OVRProjectSetup.AddTask(...)` (`:483-562`), `OVRProjectSetup.FixAllAsync(BuildTargetGroup)` (`:584-587`), `OVRProjectSetupCLI.GenerateProjectSetupReport()` (`OVRProjectSetupCli.cs:39-54`; reads `-reportFile`; writes JSON `tasksStatus[{uid,group,message,level,isDone}]`).
- **Internal (reflection).** There is no `OVRProjectSetupFixer`; the fixer is the internal `OVRConfigurationTaskFixer`. `OVRProjectSetup.GetTasks(BuildTargetGroup)` (`:314-317`). `FixTasks(BuildTargetGroup, Func<IEnumerable<OVRConfigurationTask>,List<OVRConfigurationTask>> filter, LogMessages, bool blocking, Action<OVRConfigurationTaskProcessor>, bool allowHiddenGroupTasks)` (`:589-600`). `OVRConfigurationTask` members (`Tasks/OVRConfigurationTask.cs:61-345`): `Level`/`Message`/`FixMessage` (`OptionalLambdaType<,>.GetValue(btg)`), `IsDone` (a Func), `FixAction`, `AsyncFixAction`, `FixAutomatic`, `Group`, `Tags`, `IsIgnored(btg)`, `SetIgnored(btg, bool)`, `InvalidateCache(btg)`. Ignore flags persist in `Assets/Oculus/OculusProjectSetupSettings.asset` (VERIFIED).
- **Counts (VERIFIED, Android).** Fresh project: 99 non-hidden valid rules, 23 open. After §4.3: 105 valid, 3 open, all manual (Data Use Checkup, app-id/package match, XR Simulator download).

### 4.2 Batchmode behaviour (VERIFIED)
- **Meta `EditorReady` is true at `-executeMethod`** (`Editor/Utils/EditorCallbacks/InitializeOnLoad.cs:29`), so `CachedProjectConfig` is usable (it returns null before then, `OVRProjectConfig.cs:260-277`).
- **`Meta.XR.Editor.Utils.PackageList.PackageManagerListAvailable` is false at `-executeMethod`** (`Editor/Utils/PackageList.cs:48-56, 96`), so the Packages rules are invalid and Linear drops to Recommended. A `-quit` run misses the loader, Touch and both-plugins rules (91/19 instead of 99/23). Run **without `-quit`**, poll on `EditorApplication.update` (ready after 0.4 to 3 s), then call `EditorApplication.Exit(code)`.
- **`FixAllAsync`** returns `Task.Run(fixer.WaitForCompletion)`, which spins until the main thread pumps updates (`OVRProjectSetup.cs:602-610`, `Tasks/Processors/OVRConfigurationTaskProcessor.cs:143-151`, `OVRConfigurationTaskProcessorQueue.cs:70-95`). Without `-quit` it completed in 1.8 s over 4 updates; with `-quit` and a synchronous method it never runs. Reflection `FixTasks(..., blocking: true)` runs synchronously, up to 4 passes (`OVRConfigurationTaskFixer.cs:46, 139-169`).
- **Unfiltered FixAll hazards:**
  1. The fixer ignores `FixAutomatic` (`OVRConfigurationTaskFixer.cs:38-44`). "Newest Meta XR Simulator not installed" is registered whenever the app is missing (`ToolbarItem.cs:93-133`). Its async fix would download `~/Downloads/meta_xr_simulator.dmg` and open Finder (`MetaXRSimulator/Installer.cs:38-128`; src only, not seen in our runs, which exited first). Filter it out or `SetIgnored` it.
  2. It enables the **Optional** Application SpaceWarp feature (`FeatureActiveRules.cs:68-122`), which is useless without URP motion vectors.
  3. Quality rules (`pixelLightCount = 1`, anisotropic, MSAA) write the **current editor quality level** (Ultra, index 5), not Android's default "Medium" (`m_PerPlatformDefaultQuality.Android: 2`; `OVRProjectSetupQualityTasks.cs:40-76`). With URP installed but the built-in RP active, the MSAA rule checks only URP assets and passes vacuously (`OVRProjectSetupRenderingTasks.cs:590-627`).
  4. Fixes modify scene objects without saving them: call `EditorSceneManager.SaveOpenScenes()`.

### 4.3 Batch entry point (condensed from the verified probe `Assets/Editor/RigProbe/RigProbe.cs` in the clone)

```csharp
// Unity -batchmode -projectPath <proj> -buildTarget Android -executeMethod MusicRoom.EditorTools.ProjectSetup.Run -logFile <log>   (no -quit)
public static void Run() { t0 = EditorApplication.timeSinceStartup; EditorApplication.update += Wait; }
static double t0;
static void Wait() {
    var ready = (bool)Type.GetType("Meta.XR.Editor.Utils.PackageList, Oculus.VR.Editor")
        .GetProperty("PackageManagerListAvailable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
    if (!ready && EditorApplication.timeSinceStartup - t0 < 90) return;
    EditorApplication.update -= Wait;
    try {
        ConfigureXR(); ConfigurePlayer(); ConfigureQuality(); ConfigureMeta();   // §4.5, §5, §4.4
        MusicRoomSceneBuilder.Build();                                           // §2.2
        FixFiltered(BuildTargetGroup.Android);
        OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).GetFeature<SpaceWarpFeature>().enabled = false;
        OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode: true);
        EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
    } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
}
static List<T> AutoFixable<T>(IEnumerable<T> tasks) => tasks.Where(t => {
    var tt = t.GetType(); var lv = tt.GetProperty("Level").GetValue(t);
    var level = lv.GetType().GetMethod("GetValue").Invoke(lv, new object[] { BuildTargetGroup.Android }).ToString();
    return (bool)tt.GetProperty("FixAutomatic").GetValue(t) && (level == "Required" || level == "Recommended"); }).ToList();
static void FixFiltered(BuildTargetGroup g) {
    var task = typeof(OVRProjectSetup).Assembly.GetType("OVRConfigurationTask");
    var fType = typeof(Func<,>).MakeGenericType(typeof(IEnumerable<>).MakeGenericType(task), typeof(List<>).MakeGenericType(task));
    var filter = Delegate.CreateDelegate(fType, typeof(ProjectSetup).GetMethod(nameof(AutoFixable), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(task));
    var logT = typeof(OVRProjectSetup).GetNestedType("LogMessages", BindingFlags.NonPublic);
    typeof(OVRProjectSetup).GetMethod("FixTasks", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new object[] { g, filter, Enum.ToObject(logT, 2), true, null, false });
}
```
VERIFIED: the filter left an Optional rule alone (`gpuSkinning` stayed false) and fixed a Required one (min SDK 29 back to 32). For a report, call `OVRProjectSetupCLI.GenerateProjectSetupReport()` with `-reportFile x.json`. Unity's own OpenXR checks: `OpenXRProjectValidation.GetCurrentValidationIssues(list, BuildTargetGroup.Android)` (`OXR/Runtime/OpenXRProjectValidation.cs:477-494`), then call `rule.fixIt()` where `rule.fixItAutomatic` (`OXR/Runtime/Features/OpenXRFeature.cs:354-411`).

### 4.4 OVRProjectConfig from an Editor script (asset path from `OVRProjectConfig.cs:286-318`)

```csharp
var cfg = OVRProjectConfig.CachedProjectConfig ?? AssetDatabase.LoadAssetAtPath<OVRProjectConfig>("Assets/Oculus/OculusProjectConfig.asset");
cfg.targetDeviceTypes = new List<OVRProjectConfig.DeviceType> { OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S };
cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
cfg.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;      // MRUK's rule accepts Supported (OVRProjectSetupMRUK.cs:34-55)
cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
cfg.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
cfg.requiresSystemKeyboard = false; cfg.experimentalFeaturesEnabled = false;
cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
OVRProjectConfig.CommitProjectConfig(cfg);                          // = SetDirty (:441-451); then AssetDatabase.SaveAssets()
var rs = OVRRuntimeSettings.GetRuntimeSettings();
rs.HandSkeletonVersion = OVRHandSkeletonVersion.OpenXR; rs.colorSpace = OVRManager.ColorSpace.P3;
OVRRuntimeSettings.CommitRuntimeSettings(rs);
```
Other fields (`:152-218`) keep their defaults: `disableBackups` (true, giving `allowBackup=false`), `min/targetHorizonOsSdkVersion` (60/207), `isPassthroughCameraAccessEnabled` (false), tracking features `None`. **There is no `handTrackingVersion` in 207** (it occurs nowhere in the package). `insightPassthroughEnabled` is obsolete.

### 4.5 XR Management and OpenXR in code (VERIFIED)

```csharp
var perBuild = (XRGeneralSettingsPerBuildTarget)typeof(XRGeneralSettingsPerBuildTarget)
    .GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); // XRM/Editor/XRGeneralSettingsPerBuildTarget.cs:67 -> Assets/XR/XRGeneralSettingsPerBuildTarget.asset
foreach (var g in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone }) {       // Standalone = editor play mode and XR Simulator
    if (!perBuild.HasManagerSettingsForBuildTarget(g)) perBuild.CreateDefaultManagerSettingsForBuildTarget(g);
    var gs = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(g); gs.InitManagerOnStart = true;
    XRPackageMetadataStore.AssignLoader(gs.Manager, typeof(OpenXRLoader).FullName, g);      // XRM/Editor/Metadata/XRPackageMetadataStore.cs:336
    FeatureHelpers.RefreshFeatures(g);   // creates Assets/XR/Settings/OpenXR Package Settings.asset (OXR/Editor/FeatureSupport/FeatureHelpers.cs:28-31, 145)
    var fs = OpenXRFeatureSetManager.GetFeatureSetWithId(g, "com.meta.openxr.featureset.metaxr"); // MetaXRFeatureSet.cs:69
    fs.isEnabled = true; OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(g);
    var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
    s.GetFeature<OculusTouchControllerProfile>().enabled = true;
    if (g == BuildTargetGroup.Android) {
        var q = s.GetFeature<MetaQuestFeature>(); q.enabled = true;   // "Meta Quest Support" (Quest loader)
        q.ForceRemoveInternetPermission = true;                       // optional, app is offline (MetaQuestFeature.cs:210-214)
        var so = new SerializedObject(q); var list = so.FindProperty("targetDevices"); // internal: quest, quest2, cambria, eureka (=Quest 3), quest3s
        for (int i = 0; i < list.arraySize; i++) { var e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("enabled").boolValue = e.FindPropertyRelative("manifestName").stringValue is "eureka" or "quest3s"; }
        so.ApplyModifiedPropertiesWithoutUndo();
        s.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        s.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
    }
    FeatureHelpers.RefreshFeatures(g); EditorUtility.SetDirty(s);
}
```
Result (VERIFIED): the Android features are `MetaQuestFeature, MetaXRFeature, MetaXRFoveationFeature, OculusTouchControllerProfile`. FixTasks adds `MetaXRSubsampledLayout` and `optimizeBufferDiscards`, plus SpaceWarp (turn it off). `MetaXRFeatureEnabler` enables the feature set only on an editor update (`Editor/OpenXRFeatures/MetaXRFeatureEnabler.cs:38-106`), so set it explicitly.

### 4.6 AndroidManifest
- **Patched at build time.** `OVRGradleGeneration.OnPostGenerateGradleAndroidProject` patches the generated Gradle manifest on every build (`Editor/OVRGradleGeneration.cs:353-467`, `callbackOrder` 99999). **But** the Required GameActivity rule also needs a custom `Assets/Plugins/Android/AndroidManifest.xml` whose activity is `com.unity3d.player.UnityPlayerGameActivity` (`Editor/Rules/AndroidCompatibility.cs:127-168`). Its fix is tagged `RegenerateAndroidManifest`, so `Fix` calls `OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode)` (`Tasks/OVRConfigurationTask.cs:284-301`). VERIFIED: FixTasks created that manifest.
- **API.** `OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(bool silentMode)` (`OVRManifestPreprocessor.cs:223-233`; template `Editor/AndroidManifest.OVRSubmission.xml`), `IsManifestOutdated(out List<string>)` (`:248-259`), `RemoveAndroidManifest(bool)`. Regenerate after every OVRProjectConfig change (`AndroidCompatibility.cs:33-42`). The tags are written by `ApplyFeatureManifestTags` (`:687-1097`) and `ApplyTargetDevicesManifestTags` (`:1124-1199`).
- **VERIFIED merged APK manifest** (aapt2, `AndroidManifest.apk-dump.txt`):
  - `minSdk 32`, `targetSdk 34`, `installLocation auto`.
  - Permissions: `com.oculus.permission.{HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY}` and `android.permission.INTERNET` (added by Unity; `ForceRemoveInternetPermission` removes it).
  - Features: `android.hardware.vr.headtracking` (required), `oculus.software.handtracking` (required), `com.oculus.feature.PASSTHROUGH` (not required), `android.hardware.vulkan.version` (required).
  - `horizonos:uses-horizonos-sdk 60/207`.
  - Meta-data: `com.oculus.handtracking.frequency=HIGH`, `com.oculus.ossplash.background=passthrough-contextual`, `com.oculus.supportedDevices=quest3|quest3s`.
  - Activity `UnityPlayerGameActivity` with category `com.oculus.intent.category.VR` and `com.oculus.vr.focusaware=true`.
- **Runtime prompts.** `HAND_TRACKING` needs none. MRUK requests `USE_SCENE` (§3.4).

## 5. Player settings (Android, Quest 3/3S): VERIFIED in the clone's `ProjectSettings.asset`

| Setting | Unity 6000.3 API | Value / rule *(src)* |
|---|---|---|
| Backend | `PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP)` | IL2CPP, Required with ARM64 (`AndroidCompatibility.cs:83-101`) |
| ABI | `PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64` | ARM64 only, Required (`:104-112`) |
| Min / target API | `PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32`; `.targetSdkVersion = (AndroidSdkVersions)34` | 32 Required (`:45-53, 170-173`); 34 "to upload to the Meta Quest Store", which needs exactly 34 (`:55-67`). SDK platforms 34 to 37 are installed |
| Graphics | `PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false); PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]{GraphicsDeviceType.Vulkan})` | Vulkan only (`RenderingTasks.cs:135-150`); unlocks the subsampled-layout and buffer-discard rules |
| Color | `PlayerSettings.colorSpace = ColorSpace.Linear` | Required with OpenXR (`:106-116`) |
| Stereo | `OpenXRSettings.renderMode` (§2.5) and legacy `PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing` (its rule still checks it) | multiview (`:249-256`) |
| Entry | `PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity` | Required (`AndroidCompatibility.cs:127-138`) |
| Textures | `EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC`; `PlayerSettings.Android.textureCompressionFormats = new[]{TextureCompressionFormat.ASTC}` (default `[ETC]`) | ASTC (`OptimizeTextureCompression.cs:33-41, 118-119`) |
| Orientation | `PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft` | Unity MetaQuest rule |
| Other | `Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto`, `Android.androidTVCompatibility = false`, `MTRendering = true`, `SetMobileMTRendering(BuildTargetGroup.Android, true)` (obsolete, CS0618), `graphicsJobs = true` + `graphicsJobMode = GraphicsJobMode.Legacy`, `use32BitDisplayBuffer = true`, `gpuSkinning = true`, `SplashScreen.show = false`, `virtualRealitySplashScreen = null` | Recommended/Required (`RenderingTasks.cs:120-227`) |
| Identity | `productName = "VR Music Room"`; `SetApplicationIdentifier(NamedBuildTarget.Android, "com.vrmusicroom.app")` | placeholder, no brand names |
| Quality, per level | `for (i) { QualitySettings.SetQualityLevel(i, false); QualitySettings.antiAliasing = 4; }`, then restore (Android uses level 2 "Medium") | MSAA 4 (`OVRPlugin.recommendedMSAALevel`, `OVRPlugin.cs:3902-3914`); pixel lights: pipeline spec |

`activeInputHandler` is `0` (old Input Manager; serialized only, no API). The APK built and no Meta rule checks it. Whether the desktop fallback needs `2` (Both) is UNVERIFIED.

## 6. Desktop and editor testing without a headset
- **Link is Windows-only** (`OVRProjectSetupDeviceTasks.cs:56`).
- **Meta XR Simulator 2** is a macOS app at `/Applications/MetaXRSimulator.app` (`MacOSXRSimInstallationDetector.cs:37-75`; the UPM package `com.meta.xr.simulator` is the legacy v1, `XRSimConstants.cs:69`). Download: `XRSimConstants.DownloadURL`, app id `9961418137219995` (`:31, 44`). **Window/Meta/Meta XR Simulator/Activate** (`XRSimMenu.cs:31-41`) sets `XR_RUNTIME_JSON` (`:59`). It needs the Standalone OpenXR loader and the Meta feature set, which §4.5 configures. It is not installed here, and how well it simulates hands is UNVERIFIED.
- **Editor play mode with no runtime (VERIFIED).**
  - `Plugins/MacOpenXR/libopenxr_loader.dylib` loads, then `xrCreateInstance` fails ("failed to determine active runtime file path").
  - `activeLoader`, `OVRManager.instance` and `OVRManager.display` are all null; `XRSettings.isDeviceActive`, `OVRPlugin.initialized` and `OVRManagerinitialized` are all false.
  - `OVRCameraRig.UpdateAnchors` returns early (`OVRCameraRig.cs:284-285`), so **CenterEyeAnchor stays at local (0,0,0), on the floor**, and `headPoseRelativeOffsetTranslation` has no effect.
  - `OVRHeadsetEmulator` also waits for `OVRManagerinitialized` (`OVRHeadsetEmulator.cs:134-150`). It only helps with Link or the simulator.
- **Ship `DesktopRigFallback`.** OVRCameraRig won't touch the anchors without XR, so our script owns them. The desktop camera is `(0, 1.2, 0.1)` in the source frame, looking at `(0, 0.95, -0.45)`, fov 55:

```csharp
void OnEnable() { var ce = rig.centerEyeAnchor; ce.localPosition = new Vector3(0, 1.2f, -0.1f);
    ce.localRotation = Quaternion.LookRotation(new Vector3(0, 0.95f, 0.45f) - ce.localPosition);
    ce.GetComponent<Camera>().fieldOfView = 55; }   // Update: mouse-look; never runs while OVRManager.OVRManagerinitialized
```
Guard every `OVRManager.display`, `OVRManager.instance` and `OVRPlugin` call with `OVRManager.OVRManagerinitialized`, so play mode and tests run headless (VERIFIED: the PlayMode test passed in batchmode).

## 7. Verification log (Unity 6000.3.27f1, batchmode, `-projectPath <clone> -buildTarget Android`)

Probe source: `Assets/Editor/RigProbe/RigProbe.cs` and `Assets/Tests/RigProbe/` in the scratch copy; the file names below are its log outputs.
- **0** `RigProbe.Baseline -quit`: 91 rules, 19 open (`01-baseline.txt`).
- **0b** `BaselineWait`, no `-quit`: 99 rules, 23 open (`01b-…`).
- **1** `SetupWait`, after removing the Oculus plugin: 10 open after explicit config, 3 after FixTasks; manifest generated (`02-setup.txt`, `AndroidManifest.custom.xml`).
- **2** `Run2`: `FixAllAsync` completed in 1.8 s; the filter works (`03-…`).
- **3** `-runTests -testPlatform PlayMode -testFilter RigNoHeadsetTests`: passed (`05-…`).
- **4** `BuildApk`: **Succeeded**, 4 min 07 s, 0 errors, 59.9 MB `rigprobe.apk`. Its manifest dump is `AndroidManifest.apk-dump.txt`. The build warnings left are Unity OpenXR [Optional] items.

## 8. Open questions / UNVERIFIED
1. `handTrackingFrequency HIGH`: better drum strokes, or a cost in accuracy and CPU? The tooltip says enable it only if fast motion loses tracking (`OVRProjectConfig.cs:82-85`). A/B on the headset.
2. Passthrough alpha compositing under built-in RP + OpenXR (`allowHDR=false`, MSAA 4): check on device.
3. Keeping passthrough initialized but hidden, versus toggling `isInsightPassthroughEnabled`: cost?
4. 90 Hz vs 72 Hz frame budget with the synth and snow; what `displayFrequenciesAvailable` returns on Quest 3S.
5. Do `sharpenType` and `suggestedCpu/GpuPerfLevel` take effect without Unity's "XR Performance Settings" feature?
6. Pipeline spec: with URP, switch `MetaQuestFeature` to the SRPFoveation API and re-check the MSAA, HDR, SpaceWarp and camera-stack rules.
7. Drop `android.permission.INTERNET` (`ForceRemoveInternetPermission`)? Nothing in the app needs it.
8. Store submission: the "Data Use Checkup" and "app ID / package name" rules are manual and only matter with Platform SDK APIs (none planned).
