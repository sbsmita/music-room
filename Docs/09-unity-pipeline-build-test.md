# 09 — Headless Unity pipeline (compile, test, configure, build, install) and Quest rendering setup

Everything in this spec was run on this Mac, in two APFS copies of the project, `pipeline` (experiments) and `pipeline-e2e`, unless it is marked **UNVERIFIED**. `pipeline-e2e` was a fresh copy, set up only from the files below in the documented order.

- **Pipeline files (verified end to end):** `Tools/unity.sh`, `Assets/MusicRoom/Editor/{MusicRoom.Editor.asmdef, QuestSetup.cs, QuestBuild.cs, RenderingSetup.cs}`, `Assets/MusicRoom/Runtime/{MusicRoom.Runtime.asmdef, NoteMath.cs, FrameCounter.cs, RenderProbe.cs}`, `Assets/MusicRoom/Tests/{EditMode,PlayMode}/…`, `gitignore.txt`.
- **Measurements and YAML read-backs:** quoted in the sections below.
- **Related:** spec `01-meta-rig-and-project-setup.md` reaches the same OpenXR decision and covers the rig. This spec owns the pipeline and the URP decision it deferred.
- **Source abbreviations** (under `<project>/Library/PackageCache/`): CORE = `com.meta.xr.sdk.core@4c2eac90db2d`, OXR = `com.unity.xr.openxr@ef0033a586bf`, XRM = `com.unity.xr.management@df145a5bc89e`, HANDS = `com.unity.xr.hands@3e241be20642`, UTF = `com.unity.test-framework@bd7f943e9647`, URP = `com.unity.render-pipelines.universal@ee0b6bbace10`, UGUI = `com.unity.ugui@c0ef89c047f6`.

## 0. Decisions (TL;DR)

1. **OpenXR, and remove `com.unity.xr.oculus` from `Packages/manifest.json`.** Meta marks "Oculus XR Plugin and OpenXR Plugin at the same time" as **Required** to fix (CORE `Editor/OVRProjectSetup/Tasks/Implementations/OVRProjectSetupXRTasks.cs:108-127`) and says "Beginning with v74, it is recommended to use the OpenXR plugin" (`:73-91`). Removal was verified: nothing depends on it, and builds and tests pass.
2. **Android loader `UnityEngine.XR.OpenXR.OpenXRLoader`.** OpenXR features: `MetaQuestFeature`, `Meta.XR.MetaXRFeature` (OVRPlugin hands, passthrough and scene over OpenXR), `OculusTouchControllerProfile` (Meta Required even for hands-only), `HandInteractionProfile`, `MetaXRSubsampledLayout`, and `MetaXRFoveationFeature` (on by default). Unity XR Hands' OpenXR features stay **off** (§6.3).
3. **URP**, with one script-created asset `Assets/MusicRoom/Settings/QuestURP.asset`: Forward, MSAA 4×, no HDR, no depth or opaque texture, upscaling Auto, SRP foveation (§7).
4. **Runtime materials come only from template material assets**, one per shader and keyword set, under `Resources/`. `Shader.Find` then works in builds (§7.3–7.4).
5. **TextMeshPro, world-space**, with LiberationSans SDF and its dynamic fallback. Essentials are imported headlessly via `TMP_PackageResourceImporter.ImportResources` and `importPackageCompleted` (§8).
6. **Configuration is explicit code** (`QuestSetup.ConfigureAll`), gated by Meta's CLI report. No blanket "fix all": it has side effects (§5.5).
7. **Every call uses `-batchmode -nographics -buildTarget Android`.** GPU PlayMode tests drop `-nographics`.
   - One editor per project folder: the lock is `Temp/UnityLockfile`, and a second instance exits 1 within about 1 s ("It looks like another Unity instance is running with this project open").

## 1. Environment facts (verified)

- **Editor:** `/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity`. arm64, Unity Personal, batchmode works. Every log has the benign `[Licensing::Module] Error: Access token is unavailable; failed to update`, so never grep for plain `Error`.
- **Android module** under `.../6000.3.27f1/PlaybackEngines/AndroidPlayer/`:
  - `SDK`: platforms 34/35/36/37.0; build-tools 36.0.0 (`aapt2`, `apksigner`, `zipalign`); platform-tools 37.0.1 `adb` (`/opt/homebrew/bin/adb` is the same version).
  - `NDK` r27c, `OpenJDK` 17.0.18, `Tools/gradle`: Gradle 9.3.1, AGP 9.1.1. The first build fetches from Maven, so it needs internet; it was available.
- **Costs:**
  - Cold open of the original project: about 96 s.
  - First `-buildTarget Android` on a clone: about 44 s.
  - `Library` grows from 3 GB to **12 GB** after the first Android build. APFS clones share blocks only until they are written.

## 2. Batchmode compile check

```bash
U=/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity
"$U" -batchmode -nographics -quit -buildTarget Android -projectPath "$P" -logFile "$P/Logs/ci/compile.log"
echo $?    # 0 = compiled; 1 = compile errors (or any batchmode abort)
grep -E '^[^ ].*\.cs\([0-9]+,[0-9]+\): error CS[0-9]+:' "$P/Logs/ci/compile.log" | sort -u
```

- **Exit codes (verified).**
  - `0` when the project compiles.
  - `1` with compile errors. Unity prints `Aborting batchmode due to failure:` and `Scripts have compiler errors.` to **stdout** even when `-logFile` is a file. This took 3–4 s.
  - A no-op warm check takes 12–17 s.
- **Log markers (verified).**
  - `*** Tundra build success (…)` or `*** Tundra build failed (…)`
  - `## Script Compilation Error for: Csc Library/Bee/artifacts/…/Assembly-CSharp.dll`
  - Error lines look like `Assets/X.cs(4,44): error CS0029: …`. Warnings look like `… warning CS0219: …`.
  - Each error is printed **3 times**, so use `sort -u`.
  - A syntax error (CS1002) hides semantic errors in the same assembly until it is fixed.
- **Always pass `-buildTarget Android`.** The editor compiles with the active target's defines, so code under `#if UNITY_ANDROID` is only checked on the Android target.
- `-logFile -` streams the log to stdout (verified).
- `-stackTraceLogType None` removes the 8-line stack after every `Debug.Log`. It also removes exception stacks, so drop it when debugging an exception.
- **Shutdown crash:** in about 45 runs, exit **134** happened twice, and the same last line `libc++abi: terminate_handler unexpectedly returned` appeared once with exit 0. It always came *after* `Exiting batchmode successfully now!` or our own success marker, so the work had finished. `Tools/unity.sh` maps 134 to 0 only when the success marker (`OK_MARK`) is present.
- **`-executeMethod` failures (verified).** An exception gives exit 1 and prints `executeMethod method X threw exception.` on stdout.
  - With `-stackTraceLogType None` only `NullReferenceException: …` is shown, without a stack.
  - Methods that call `EditorApplication.Exit(code)` must run **without** `-quit`.

## 3. Unity Test Framework 1.6.0 in batchmode

### 3.1 asmdefs (verified files)

- `MusicRoom.Runtime.asmdef`: `{"name":"MusicRoom.Runtime","rootNamespace":"MusicRoom","references":["Unity.TextMeshPro"],"includePlatforms":[],"autoReferenced":true}`
- `Tests/EditMode/MusicRoom.Tests.EditMode.asmdef`:

```json
{ "name": "MusicRoom.Tests.EditMode",
  "references": ["MusicRoom.Runtime", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"], "overrideReferences": true, "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false, "defineConstraints": ["UNITY_INCLUDE_TESTS"] }
```

- `Tests/PlayMode/MusicRoom.Tests.PlayMode.asmdef`: the same with `"includePlatforms": []`. `UnityEditor.TestRunner` is silently dropped for players.
- One `MonoBehaviour` per file with a matching name (`FrameCounter.cs`, `DspProbe.cs`).
- Editor tooling: `MusicRoom.Editor.asmdef` (`includePlatforms:["Editor"]`) references `MusicRoom.Runtime`, `Unity.XR.Management(.Editor)`, `Unity.XR.OpenXR(.Editor)`, `Unity.XR.OpenXR.Features.MetaQuestSupport`, `Unity.XR.Hands`, `Oculus.VR`, `Oculus.VR.Editor` (`OVRProjectSetup`, `OVRProjectConfig`, `OVRManifestPreprocessor`), `Unity.RenderPipelines.Core.Runtime`, `Unity.RenderPipelines.Universal.Runtime` and `Unity.TextMeshPro`.

### 3.2 Commands and verified results

```bash
"$U" -batchmode -nographics -buildTarget Android -projectPath "$P" -runTests -testPlatform EditMode -testResults "$P/Logs/ci/editmode.xml" -logFile "$P/Logs/ci/test-edit.log"
"$U" -batchmode -nographics -buildTarget Android -projectPath "$P" -runTests -testPlatform PlayMode -testCategory '!Graphics' -testResults "$P/Logs/ci/playmode.xml" -logFile "$P/Logs/ci/test-play.log"
"$U" -batchmode            -buildTarget Android -projectPath "$P" -runTests -testPlatform PlayMode -testResults "$P/Logs/ci/playmode.xml" -logFile …   # with GPU (Metal)
```

- **No `-quit` with `-runTests`.** UTF warns "Running tests from command line arguments will not work when "quit" is specified" (UTF `UnityEditor.TestRunner/CommandLineTest/SettingsBuilder.cs:42`).
- **EditMode:** 2/2 pass, exit 0, 13–24 s.
- **PlayMode under `-nographics`:** works. `[UnityTest]` coroutines run, `SystemInfo.graphicsDeviceType == Null`, 16–31 s. A GPU is needed only for rendering tests. Without `-nographics` (Metal), `Camera.Render()` → `RenderTexture` → `ReadPixels` = `RGBA(1,0,0,1)` under Built-in and URP.
- **Audio DSP runs in batchmode, with or without `-nographics`:** `AudioSettings.outputSampleRate=48000`; `OnAudioFilterRead` was called 24–25 times in 0.5 s. Synth code can be tested headlessly.
- **Exit codes:** `Ok=0`, `Failed=2`, `RunError=3`, `PlatformNotFound=4` (`Executer.cs:91-97`). Compile errors give exit 1 and **no XML**. A deliberate failure gave exit 2 and XML `result="Failed(Child)"`, with `<failure><message>` plus a `file.cs:line` stack.
- **Traps (source + runs):**
  - **Inconclusive counts as a failure (exit 2)** (`RunStateCallbacks.cs:35`), so `Assume.That(gpu)` breaks CI. Use `Assert.Ignore("no GPU")` (skipped, exit 0; top-level XML result `Skipped:Ignored`) or `[Category("Graphics")]` with `-testCategory '!Graphics'`.
  - **Zero matched tests → exit 0** (`Executer.cs:164`). `unity.sh check_xml` also requires `total > 0`.
  - Results are written to `-testResults` and also to `~/Library/Application Support/<company>/<product>/TestResults.xml`.
- **Filters:** `-testFilter` takes `;`-separated full names or regexes (full class name verified); `-testCategory` takes `;` lists with `!` to exclude (`RuntimeTestRunnerFilter.cs:68-90`, split at `CommandLineOption.cs:41`); `-assemblyNames A;B` also exists.
- **UNVERIFIED (needs a headset):** `-testPlatform Android` builds and deploys a test player; `-playerHeartbeatTimeout` exists (`SettingsBuilder.cs:49`).
- **Editor PlayMode runs without XR.** Only Android XR settings exist (`XRGeneralSettingsPerBuildTarget` Keys = Android), so there is a desktop camera, which is good for headless tests.

## 4. Changes to make in the project

1. Remove `"com.unity.xr.oculus": "4.5.2"` from `Packages/manifest.json`.
2. Add the pipeline files (`Assets/MusicRoom/**`, `Tools/unity.sh`) to the project, and use `gitignore.txt` as `.gitignore`. It adds `.utmp/`, `Builds/`, `*_BurstDebugInformation_DoNotShip/` and `*.apk`.
3. Run `compile → configure → tmp-import → render-setup → test-edit → test-play → meta-report → build`. On the e2e clone this took about 7 min: 74 + 55 + 30 + 19 + 24 + 31 + 14 + 175 s.
4. Commit `Assets/**` and `ProjectSettings/**` after the first build. The build rewrites the URP asset's `m_Prefiltering*` fields and adds `m_BuildTargetBatching`.

## 5. Configuring the project from `-executeMethod` (`MusicRoom.EditorTools.QuestSetup.ConfigureAll`)

`ConfigureAll()` = `EnsureAndroidTarget()` → `ConfigurePlayer()` → `ConfigureXR()` → `ConfigureMeta()` → `AssetDatabase.SaveAssets()` → `LogOpenXRValidation()`.

It is idempotent: a re-run takes 12–29 s. The first run took 55–56 s.

### 5.1 Player and build settings (fresh-process read-back in the JSON, `readback_projectsettings_yaml`)

| Setting | API (all verified) | Persisted (`ProjectSettings.asset`) | Why |
|---|---|---|---|
| ids | `companyName/productName = "VR Music Room"`; `SetApplicationIdentifier(NamedBuildTarget.Android, "com.vrmusicroom.app")`; `bundleVersion="0.1.0"`; `Android.bundleVersionCode=1` | `applicationIdentifier: Android: com.vrmusicroom.app` | task |
| IL2CPP | `SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP)`; `SetIl2CppCompilerConfiguration(…, Release)` | `scriptingBackend: Android: 1` | Meta Required with ARM64 (CORE `Editor/Rules/AndroidCompatibility.cs:82-101`) |
| ARM64 | `Android.targetArchitectures = AndroidArchitecture.ARM64` | `AndroidTargetArchitectures: 2` | Meta Required (`:103-112`) |
| API levels | `Android.minSdkVersion = AndroidApiLevel32`; `Android.targetSdkVersion = AndroidApiLevel34` | `AndroidMinSdkVersion: 32`, `AndroidTargetSdkVersion: 34` | Meta **Required min 32** (`:44-53`); target 34 is Recommended "to upload to the Meta Quest Store" (`:55-67`) |
| Vulkan | `SetUseDefaultGraphicsAPIs(BuildTarget.Android,false)`; `SetGraphicsAPIs(BuildTarget.Android, new[]{GraphicsDeviceType.Vulkan})` | `m_APIs: 15000000`, `m_Automatic: 0` | Meta Recommended (`OVRProjectSetupRenderingTasks.cs:134`) |
| linear | `colorSpace = ColorSpace.Linear` | `m_ActiveColorSpace: 1` | Meta **Required** with OpenXR (`…RenderingTasks.cs:105-116`) |
| stereo | `stereoRenderingPath = StereoRenderingPath.Instancing` (+ OpenXR `renderMode`, §5.2) | `m_StereoRenderingPath: 2` | Meta Recommended (`:248-256`) |
| ASTC | `Android.textureCompressionFormats = new[]{TextureCompressionFormat.ASTC}` + `EditorUserBuildSettings.androidBuildSubtarget = ASTC` | `m_Formats: 03000000`; the subtarget lives in `Library/` (read back `Generic` after a platform switch) | Meta Recommended |
| GameActivity | `Android.applicationEntry = AndroidApplicationEntry.GameActivity` | `androidApplicationEntry: 2` | Meta **Required** + checks the custom manifest (`AndroidCompatibility.cs:125-168`); OpenXR rule (`MetaQuestFeature.cs:686`) |
| orientation | `defaultInterfaceOrientation = UIOrientation.LandscapeLeft` | `defaultScreenOrientation: 3` | OpenXR **error** rule (`MetaQuestFeature.cs:697`) |
| misc Android | `Android.preferredInstallLocation=Auto`; `androidTVCompatibility=false`; `forceInternetPermission=false` | | Meta (`AndroidCompatibility.cs:69-123`) |
| perf | `MTRendering=true`; `graphicsJobs=true` + `graphicsJobMode=GraphicsJobMode.Legacy`; `gpuSkinning=true`; `use32BitDisplayBuffer=true` | | Meta Recommended (`RenderingTasks.cs:119-131,192-227,330-339`) |
| splash | `SplashScreen.show=false`; `virtualRealitySplashScreen=null` | `m_ShowUnitySplashScreen: 0` | Unity 6 Personal allows it |
| input | no public API: `SerializedObject(LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]).FindProperty("activeInputHandler").intValue = 2` (Both; next start) | `activeInputHandler: 2` | OpenXR uses the Input System |
| define | `SetScriptingDefineSymbols(NamedBuildTarget.Android/Standalone, "OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING")` | `scriptingDefineSymbols` | Meta Recommended |
| quality (BiRP only) | loop `QualitySettings.SetQualityLevel(i,false)`; `pixelLightCount=1`; `anisotropicFiltering=Enable` | `QualitySettings.asset` | Meta's own fix touches only the *current* level |

### 5.2 XR Plug-in Management + OpenXR (`QuestSetup.ConfigureXR`, `QuestSetup.cs:114-153`)

```csharp
// XRGeneralSettingsPerBuildTarget.GetOrCreate() is internal (XRM Editor/XRGeneralSettingsPerBuildTarget.cs:67) -> create + register:
EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget per); // or FindAssets("t:XRGeneralSettingsPerBuildTarget")
if (per == null) { AssetDatabase.CreateFolder("Assets","XR"); per = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                   AssetDatabase.CreateAsset(per, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset"); }
EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, per, true);
if (!per.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) per.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android); // :204
var general = per.SettingsForBuildTarget(BuildTargetGroup.Android); general.InitManagerOnStart = true;
XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android); // XRM Editor/Metadata/XRPackageMetadataStore.cs:336 (RemoveLoader :395)
FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);   // OXR Editor/FeatureSupport/FeatureHelpers.cs:28; creates the OpenXR settings asset (:145)
var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android); // OXR Runtime/Settings/OpenXRSettings.cs:68
oxr.GetFeature<MetaQuestFeature>().enabled = true;              // com.unity.openxr.feature.metaquest (MetaQuestFeature.cs:48)
oxr.GetFeature<Meta.XR.MetaXRFeature>().enabled = true;         // com.meta.openxr.feature.metaxr (CORE Scripts/OpenXRFeatures/MetaXRFeature.cs:162)
oxr.GetFeature<OculusTouchControllerProfile>().enabled = true;  // Meta Required (CORE Editor/Rules/OpenXRSettings.cs:135-172)
oxr.GetFeature<HandInteractionProfile>().enabled = true;        // com.unity.openxr.feature.input.handinteraction
oxr.GetFeature<Meta.XR.MetaXRSubsampledLayout>().enabled = true;// Meta Recommended on Vulkan (:71-94)
oxr.GetFeature<Meta.XR.MetaXRSpaceWarp>().enabled = false;      // Meta Required off (:176-212)
oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced; // multiview (OpenXRRenderSettings.cs:16,38)
oxr.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling; // MetaQuestFeature rule :728
oxr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;             // :209
EditorUtility.SetDirty(oxr); FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android); AssetDatabase.SaveAssets();
```

- `OpenXRPackageSettings` is an **internal** class (`Editor/OpenXRPackageSettings.cs:15`), so its `GetOrCreateInstance()` (`:57`) is unreachable; `RefreshFeatures` is the public route. The asset was named `OpenXR Package Settings.asset` in one clone and `OpenXRPackageSettings.asset` in the other: never hard-code its path.
- Use `GetFeature<T>()`, not ids: XR Hands `HandTracking` and OpenXR `MicrosoftHandInteraction` share the id `com.unity.openxr.feature.input.handtracking`.
- Internal `MetaQuestFeature` fields go through `new SerializedObject(feature).FindProperty("m_foveatedRenderingApi" | "m_symmetricProjection" | "m_optimizeBufferDiscards")` (the last defaults to true).
- Validation gate: `UnityEditor.XR.OpenXR.OpenXRProjectValidation.GetCurrentValidationIssues(List<OpenXRFeature.ValidationRule>, BuildTargetGroup)` (`Runtime/OpenXRProjectValidation.cs:477`; fields `message`, `error`, `fixIt`, `fixItAutomatic`). Any `error` rule fails the build with `BuildFailedException("OpenXR Build Failed.")` (`Editor/OpenXRProjectValidationRulesSetup.cs:226-235`), e.g. "At least one interaction profile must be added" (`OpenXRProjectValidation.cs:121`). After `ConfigureAll`: **0 errors**, 2–3 optional warnings.
- XR Management created `Assets/XR/Settings/OculusSettings.asset` and `Assets/XR/Loaders/OculusLoader.asset` by itself on the first editor update while `com.unity.xr.oculus` was installed. They are orphans once it is removed; `ConfigureMeta` deletes them.

### 5.3 Meta XR Core project config (`QuestSetup.ConfigureMeta`, `QuestSetup.cs:192-234`; spec 01 covers the rig side)

```csharp
var cfg = OVRProjectConfig.CachedProjectConfig;                                   // CORE Editor/OVRProjectConfig.cs:260
cfg.targetDeviceTypes = new List<OVRProjectConfig.DeviceType>{ OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S };
cfg.handTrackingSupport   = OVRProjectConfig.HandTrackingSupport.HandsOnly;   // manifest handtracking required="true" (OVRManifestPreprocessor.cs:694-714)
cfg.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;      // fast-motion mode (drums)
cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
cfg.sceneSupport  = OVRProjectConfig.FeatureSupport.Supported;                // MRUK windows (USE_SCENE)
cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough; // Required with passthrough (OVRProjectSetupPassthrough.cs:145-158)
OVRProjectConfig.CommitProjectConfig(cfg);    // :441 only SetDirty -> AssetDatabase.SaveAssets() writes Assets/Oculus/OculusProjectConfig.asset
OVRRuntimeSettings.Instance.HandSkeletonVersion = OVRHandSkeletonVersion.OpenXR; OVRRuntimeSettings.CommitRuntimeSettings(OVRRuntimeSettings.Instance); // ISDK Required
OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(silentMode: true);       // CORE Editor/OVRManifestPreprocessor.cs:223
```

The last call writes Meta's store-compatible `Assets/Plugins/Android/AndroidManifest.xml` (contents in the JSON). Without it, the **Required** GameActivity task stays open: `ValidateManifestApplicationEntry` reads that file (`AndroidCompatibility.cs:142-168`). `OVRGradleGeneration.OnPostGenerateGradleAndroidProject` (`OVRGradleGeneration.cs:353`) patches the Gradle manifest again at build time.

### 5.4 Meta's CLI report = the CI gate (verified)

```bash
"$U" -batchmode -nographics -buildTarget Android -projectPath "$P" -quit \
     -executeMethod OVRProjectSetupCLI.GenerateProjectSetupReport -reportFile "$P/Logs/ci/meta-setup.json"
```

- Source: CORE `Editor/OVRProjectSetup/OVRProjectSetupCLI.cs:39-54` (runs the updater with `blocking: true`; without `-reportFile` it writes `./setuptoolreport_<date>.json`). JSON: `{createdAt, buildTargetGroup, projectName, unityVersion, tasksStatus:[{uid,group,message,level,isDone}]}`.
- After `ConfigureAll`: 93 tasks, all 45 Required done. Left: Optional Application SpaceWarp; Recommended Data Use Checkup and App ID (Platform SDK only, ignore); XR Simulator not installed. `unity.sh meta-report` exits 1 on any outstanding Required task.
- Scene rules (OVRManager/OVRCameraRig passthrough flags, transparent background) evaluate the currently open scene. In the real project, wrap the call in a method that first runs `EditorSceneManager.OpenScene(main)`.
- The build-time `OVRProjectSetupBuildValidator` only *warns* on Required tasks (`RequiredThrowErrors` defaults to false). Meta rules never block a build; OpenXR `error` rules do.

### 5.5 Why not Meta's "Fix All" (verified side effects)

- Public `OVRProjectSetup.FixAllAsync(BuildTargetGroup)` (`OVRProjectSetup.cs:584`) is non-blocking and needs editor ticks: it does not finish under `-executeMethod … -quit`, and waiting on its `Task` on the main thread deadlocks.
- Internal `FixTasks(BuildTargetGroup, Func<…> filter = null, LogMessages logMessages, bool blocking = true, Action<…> onCompleted = null, bool allowHiddenGroupTasks = false)` (`:589`; runs synchronously when blocking, `OVRConfigurationTaskProcessorQueue.cs:60-95`) worked through reflection (`GetMethod("FixTasks", NonPublic|Static)`, nested internal enum `LogMessages.All = 3`). But it:
  1. started the **XR Simulator installer**. The log said "Finished saving data to ~/Downloads/meta_xr_simulator.dmg", then "does not exist" at quit; `~/Downloads` could not be listed from this sandbox to confirm. The task has `fixAutomatic: !Application.isBatchMode` (`Editor/MetaXRSimulator/ToolbarItem.cs:119-127`), but the fixer's filter ignores `fixAutomatic` (`OVRConfigurationTaskFixer.cs:38-44`);
  2. **enabled Application SpaceWarp**;
  3. edited only the *current* quality level.
- So: explicit settings (§5.1–5.3), then the report gate.

## 6. Building the APK (`MusicRoom.EditorTools.QuestBuild.BuildApk`)

```csharp
var opts = new BuildPlayerOptions { scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), // else EnsureSmokeScene()
    locationPathName = Arg("-outputPath") ?? "Builds/Android/VRMusicRoom.apk", target = BuildTarget.Android,
    targetGroup = BuildTargetGroup.Android, options = dev ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None };
EditorUserBuildSettings.buildAppBundle = false;  // .apk
BuildReport r = BuildPipeline.BuildPlayer(opts);
Debug.Log($"[BUILD] result={r.summary.result} …"); EditorApplication.Exit(r.summary.result == BuildResult.Succeeded ? 0 : 1); // no -quit
```

```bash
"$U" -batchmode -nographics -buildTarget Android -projectPath "$P" -stackTraceLogType None \
     -executeMethod MusicRoom.EditorTools.QuestBuild.BuildApk -outputPath Builds/Android/VRMusicRoom.apk -logFile "$P/Logs/ci/build.log"
```

The trivial scene comes from `QuestBuild.EnsureSmokeScene()`: `EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects)` + a cube, saved to `Assets/MusicRoom/Scenes/Smoke.unity` and set as `EditorBuildSettings.scenes`.

### 6.1 Results

| Build | Time (BuildReport) | IL2CPP C++ | Gradle | APK |
|---|---|---|---|---|
| first cold build, Built-in smoke scene | 6 m 10 s | 3 m 32 s | 24.8 s | 59,950,324 B; 0 errors, 4 warnings |
| fresh e2e clone, cold, URP | 2 m 43 s | 1 m 25 s | 17.7 s | 60,974,106 B |
| after adding a runtime script | 2 m 04 s | | 17.3 s | 60,966,402 B |
| incremental, no script change | 31–48 s (process 45–67 s) | | | |

- Uncompressed 188 MB, 725 entries: `libil2cpp.so` 103 MB (all the Meta SDK managed code), `libunity.so` 22 MB, `global-metadata.dat` 13.6 MB, `libOVRPlugin.so` 7.0 MB, `libmrutilitykitshared.so` 5.2 MB, `libMetaXRAudioUnity.so` 2.7 MB. `stripEngineCode: 1`; managed stripping is at the platform default.
- `BuildReport.summary.totalSize` (≈1.23 GB) is **not** the APK size; check the file.
- Side outputs: `Builds/Android/VR Music Room_BurstDebugInformation_DoNotShip/` (never ship), `.utmp/` (Gradle CMake; gitignore), and the Gradle project in `Library/Bee/Android/Prj/IL2CPP/Gradle/` (merged manifests).
- `aapt2 dump badging` (`.../SDK/build-tools/36.0.0/aapt2`): package `com.vrmusicroom.app` 1/0.1.0, min/target 32/34, `native-code: 'arm64-v8a'`, launchable `com.unity3d.player.UnityPlayerGameActivity`. Features: headtracking (required), vulkan, PASSTHROUGH (not required), `oculus.software.handtracking` (required). Permissions: `HAND_TRACKING`, `USE_ANCHOR_API`, `USE_SCENE`, plus `INTERNET`, which a library adds despite `forceInternetPermission=false` (`MetaQuestFeature.ForceRemoveInternetPermission` exists; effect UNVERIFIED).
- **Signing.** Default: `~/.android/debug.keystore`, v2 scheme (`CN=Android Debug`), fine for adb. **Release (verified):** set env `MR_KEYSTORE`, `MR_KEYSTORE_PASS`, `MR_KEYALIAS`, `MR_KEYALIAS_PASS`. `BuildApk` sets `PlayerSettings.Android.useCustomKeystore/keystoreName/keystorePass/keyaliasName/keyaliasPass`; `apksigner verify --print-certs` then showed the test certificate. Path and alias **are** serialized into `ProjectSettings.asset` (passwords are not), so `BuildApk` resets them after the build. `apksigner` needs `JAVA_HOME=/Applications/Unity/Hub/Editor/6000.3.27f1/PlaybackEngines/AndroidPlayer/OpenJDK`.
- Markers: Unity writes `##utp:{"type":"PlayerBuildInfo",…}` lines. `QuestBuild` adds `[BUILD] result=… sizeBytes=… time=…`, `[BUILD-STEP] <duration> <name>` and `[BUILD-ERROR] …`. `unity.sh` requires `[BUILD] result=Succeeded` **and** the APK file.

### 6.2 Concurrency on this Mac

- One editor per project folder (`Temp/UnityLockfile`); clones can build in parallel. Another session was also running Unity Android builds and adb on a different project on this Mac.
- **Every Unity exit runs "Killing ADB server" and does kill a running adb server.** Verified: a server started before a batch run was gone afterwards. Any `adb logcat` session drops whenever any Unity process exits; re-attach with `adb wait-for-device logcat …`.
- Builds share `~/.gradle`; other sessions isolate it with `GRADLE_USER_HOME=$PWD/Library/GradleHome` (optional; UNVERIFIED whether needed).

### 6.3 Pitfall: Unity XR Hands flips the hand-tracking requirement

With **`UnityEngine.XR.Hands.OpenXR.HandTracking`** and `MetaQuestFeature` both enabled, `com.unity.xr.hands` rewrites the manifest to `oculus.software.handtracking android:required="false"` (`ModifyAndroidManifest.UpdateHandTrackingMetaData`, HANDS `Editor/OpenXR/ModifyAndroidManifest.cs:106-121`). That overrides Meta's HandsOnly `required="true"`. Verified with two builds: `aapt2` showed `uses-feature-not-required` with the feature on, `uses-feature` with it off. OVRPlugin and ISDK-OVR hands come from **`MetaXRFeature`** (extensions include `XR_EXT_hand_tracking`, `XR_FB_hand_tracking_mesh/aim/capsules`, `MetaXRFeature.cs:42-151`) and do not need Unity's feature. Default: `QuestSetup.EnableUnityXRHandsFeatures = false`. Enable it only if the hands spec uses `XRHandSubsystem`, and accept or post-process the manifest.

## 7. Render pipeline for Quest 3S: **URP**

### 7.1 Why

- **URP-only Quest features:**
  - SRP Foveation: "The Unity SRP Foveation API does not support the Built-in Rendering Pipeline" (OXR `Documentation~/features/foveatedrendering.md:34-49`); `MetaQuestFeature.cs:121-127` says "On Scriptable Render Pipelines, it is highly recommended to use the SRPFoveation API".
  - Application SpaceWarp: URP 17.0.3+, render graph, Vulkan (`Documentation~/features/spacewarp/spacewarp-prerequisites.md`).
  - Viewport dynamic resolution: URP 17.0.3+, Vulkan (`automaticdynamicresolution.md:19-20`).
  - Multiview Render Regions: render graph (`MetaQuestFeature.cs` rules).
- **Meta's tool targets URP.** It ships URP rules (intermediate texture Auto, no SSAO, no depth or opaque texture, no HDR, no FSR, no camera stack, MSAA = `OVRPlugin.recommendedMSAALevel` = 4 in the editor) at `OVRProjectSetupRenderingTasks.cs:259-305, 425-543, 589-627` and `OVRPlugin.cs:3902-3915`.
- **Passthrough** composites through OVRPlugin layers in either pipeline. The recommended URP settings (HDR off, camera clear alpha 0, which `CreateProbeScene` sets) are the passthrough-friendly ones; spec 01 discusses the alpha path. UNVERIFIED on a headset.
- **Measured cost:** the URP APK is about 1 MB larger (60.97 vs 59.95 MB). Render graph is on (`UniversalRenderPipelineGlobalSettings.asset` `m_EnableRenderCompatibilityMode: 0`). Editor PlayMode render tests pass under URP.

### 7.2 URP asset from script (`RenderingSetup.SetupUrp`, verified)

```csharp
var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
AssetDatabase.CreateAsset(rd, "Assets/MusicRoom/Settings/QuestURP_Renderer.asset");
ResourceReloader.TryReloadAllNullIn(rd, UniversalRenderPipelineAsset.packagePath);
rd.renderingMode = RenderingMode.Forward; rd.depthPrimingMode = DepthPrimingMode.Disabled;
rd.intermediateTextureMode = IntermediateTextureMode.Auto;           // UniversalRendererData.cs:236/249/339
var urp = UniversalRenderPipelineAsset.Create(rd);                   // URP Runtime/Data/UniversalRenderPipelineAsset.cs:716
AssetDatabase.CreateAsset(urp, "Assets/MusicRoom/Settings/QuestURP.asset");
urp.msaaSampleCount = 4; urp.supportsHDR = false; urp.supportsCameraDepthTexture = false; urp.supportsCameraOpaqueTexture = false;
urp.renderScale = 1f; urp.upscalingFilter = UpscalingFilterSelection.Auto; urp.shadowDistance = 6f; urp.shadowCascadeCount = 1;
urp.maxAdditionalLightsCount = 2; urp.useSRPBatcher = true;
var so = new SerializedObject(urp);   // internal setters (…Asset.cs:1296,1305,1330,1348,1545)
so.FindProperty("m_MainLightRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
so.FindProperty("m_MainLightShadowsSupported").boolValue = true; so.FindProperty("m_MainLightShadowmapResolution").intValue = 1024;
so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;  // fireplace light
so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false; so.FindProperty("m_SoftShadowsSupported").boolValue = false;
so.ApplyModifiedPropertiesWithoutUndo();
GraphicsSettings.defaultRenderPipeline = urp;
for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }
// MetaQuestFeature: new SerializedObject(f).FindProperty("m_foveatedRenderingApi").intValue = 1 /*SRPFoveation*/
```

Not set, and UNVERIFIED on device: `m_symmetricProjection = true` (needs Vulkan + multiview; Quest 2+) and Multiview Render Regions. Try them after the first on-headset profile.

### 7.3 Shaders for procedural materials

- Use `Universal Render Pipeline/Lit` (wood, metal, instruments, robot), `…/Unlit` (glow cards, fire sprites, UI quads) and `…/Particles/Unlit` (snow, sparks; additive or alpha). Properties are `_BaseColor` and `_BaseMap`. Built-in `Standard` and `Unlit/Color` are not in a URP build.
- **Rule: one template material asset per shader + keyword combination.** `RenderingSetup.CreateMaterialTemplates()` writes `Assets/MusicRoom/Resources/MaterialTemplates/{LitOpaque, LitEmissive, LitTransparent, Unlit, ParticlesUnlitAdditive}.mat`. At runtime: `var m = new Material(template); m.SetColor("_BaseColor", c);` (better: reference the templates from a `MaterialLibrary` ScriptableObject in the scene).
  - Template references put the shaders in the build, so `Shader.Find` works for them.
  - Only keyword sets used by some material are compiled: `_EMISSION` and `_SURFACE_TYPE_TRANSPARENT` (`_Surface=1`) are `shader_feature`. A missing variant falls back to the nearest one and looks wrong rather than pink. Set keywords and render state on templates, not at runtime.
- **"Always Included Shaders"** (no public setter; `SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]).FindProperty("m_AlwaysIncludedShaders")`, as in `RenderingSetup.AddAlwaysIncludedShaders`). Measured on top of the templates: Lit 116 → 168 variants, Unlit 20 → 40, Particles/Unlit 16 → 28, total 1000 → 1088, APK +117 KB. Affordable, but it does not guarantee the `shader_feature` combinations, so templates remain the rule.
- TMP's default font material uses **`TextMeshPro/Mobile/Distance Field`**. `Shader.Find("TextMeshPro/Distance Field")` returned NULL in the player.

### 7.4 Player verification (macOS Mono player of the same URP project: `QuestBuild.BuildMacProbe` + `RenderProbe`, run with `-batchmode`, GPU Metal)

- `Shader.Find` **found**: URP Lit, Unlit, Particles/Unlit, `TextMeshPro/Mobile/Distance Field`, `GUI/Text Shader`, `Sprites/Default`. **NULL**: Simple Lit, `TextMeshPro/Distance Field`, `Standard`, `Unlit/Color`.
- Readback: template-cloned material rendered green `RGBA(0,1,0,1)`; `Shader.Find` material rendered blue `RGBA(0,0,1,1)`.
- Same inclusion rules apply on Android; not run on Quest (UNVERIFIED there).
- Gotcha: Meta's AI building blocks reference `Microphone`, so **desktop builds fail** ("Microphone Usage Description is empty") until `SerializedObject(...).FindProperty("microphoneUsageDescription")` is set. Android was unaffected (no `RECORD_AUDIO`).
- The macOS switch reset `androidBuildSubtarget` to Generic, which is why ASTC is also set at project level (§5.1).

## 8. Text rendering: **TextMeshPro** (world-space `TMPro.TextMeshPro`)

- **Headless Essentials import (verified, 21–30 s).** `TMPro.TMP_PackageResourceImporter.ImportResources(bool importEssentials, bool importExamples, bool interactive)` (UGUI `Runtime/TMP/TMP_PackageResourceImporter.cs:220`, editor-only, assembly `Unity.TextMeshPro`) calls `AssetDatabase.ImportPackage(".../TMP Essential Resources.unitypackage", false)`. That import is **deferred to a later editor tick**: with `-quit`, nothing was imported and no error was printed. Run it **without `-quit`** (`RenderingSetup.ImportTmpEssentialsAndExit`):

  ```csharp
  AssetDatabase.importPackageCompleted += n => { AssetDatabase.Refresh(); EditorApplication.Exit(0); };
  AssetDatabase.importPackageFailed += (n, e) => EditorApplication.Exit(1); AssetDatabase.importPackageCancelled += n => EditorApplication.Exit(1);
  TMP_PackageResourceImporter.ImportResources(true, false, false);
  ```

  - This creates `Assets/TextMesh Pro/{Resources/TMP Settings.asset, Resources/Fonts & Materials/LiberationSans SDF.asset (+ " - Fallback"), Fonts/LiberationSans.ttf, Shaders, Sprites}`.
  - `-importPackage <file>` is an alternative (UNVERIFIED).
- **Fonts.** `LiberationSans SDF` is Static (`m_AtlasPopulationMode: 0`). Its fallback `LiberationSans SDF - Fallback` is Dynamic (`1`, with the source TTF referenced), so any glyph in the font renders, e.g. "‹" in lesson headers. `AddComponent<TextMeshPro>()` picks `TMP_Settings.defaultFontAsset` from `Resources` automatically. In the player: font `LiberationSans SDF`, shader `TextMeshPro/Mobile/Distance Field`.
- **Not legacy `TextMesh`.** `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` works in builds (verified, shader `GUI/Text Shader`), but it is a fixed-size bitmap atlas: blurry in VR, with no SDF or outline. Keep it as a fallback only.
- **Not `Texture2D` drawing.** Drawing labels into a texture with a 2D drawing API has no Unity equivalent; you would render TMP via a camera into a `RenderTexture`. Use that only to bake static labels (e.g. piano key letters) into one texture (benefit UNVERIFIED).

## 9. Installing on the Quest 3S and reading logs (adb)

No headset was connected (`adb devices -l` was empty). These are standard adb commands; the Quest-specific ones are marked.

```bash
ADB=/Applications/Unity/Hub/Editor/6000.3.27f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
$ADB devices -l                                    # Developer Mode (Meta Horizon app) + "Allow USB debugging" in headset
$ADB install -r -g Builds/Android/VRMusicRoom.apk   # -r keep data, -g grant runtime perms (USE_SCENE) without prompt
$ADB shell am start -n com.vrmusicroom.app/com.unity3d.player.UnityPlayerGameActivity
$ADB shell am force-stop com.vrmusicroom.app ; $ADB shell pidof com.vrmusicroom.app
$ADB uninstall com.vrmusicroom.app                 # also wipes persistentDataPath (saved takes!)
$ADB logcat -c && $ADB logcat -v time -s Unity:V AndroidRuntime:E CRASH:E DEBUG:E libc:F    # Debug.Log tag = "Unity"
$ADB logcat -v time --pid=$($ADB shell pidof -s com.vrmusicroom.app)
$ADB logcat -d -v time > Logs/device-logcat.txt ; $ADB tcpip 5555 && $ADB connect <quest-ip>:5555
```

- **Prefix app logs** (e.g. `[MR] …`) and filter with `| grep '\[MR\]'`.
- **Logcat drops whenever any Unity process exits** (§6.2); re-attach with `adb wait-for-device logcat …`. Do not run `adb kill-server` while other sessions use adb on this Mac.
- **Test the permission prompt.** Install without `-g` once to test the "Play in my room" spatial-data prompt.
- **UNVERIFIED Quest tips:**
  - `adb shell am broadcast -a com.oculus.vrpowermanager.prox_close` keeps the display on without wearing the headset; `…automation_disable` undoes it.
  - `BuildOptions.AutoRunPlayer` installs and runs from `BuildPlayer`.
- `Tools/unity.sh install [apk]` and `Tools/unity.sh log` wrap these commands.

## 10. `Tools/unity.sh` (verified on both clones)

| Command | Unity invocation | Success requires |
|---|---|---|
| `compile` | `-quit` | exit 0; error lines printed |
| `test-edit` | `-runTests -testPlatform EditMode -testResults Logs/ci/editmode.xml` | exit 0, XML `total > 0`, `failed == 0`, `inconclusive == 0` |
| `test-play [--gpu]` | PlayMode with `-testCategory '!Graphics'`; with `--gpu`, drops `-nographics` and runs everything | same as above |
| `configure` | `-executeMethod MusicRoom.EditorTools.QuestSetup.ConfigureAll -quit` | exit 0 |
| `tmp-import` | `-executeMethod MusicRoom.EditorTools.RenderingSetup.ImportTmpEssentialsAndExit` (no `-quit`) | marker `importPackageCompleted` |
| `render-setup` | `-executeMethod MusicRoom.EditorTools.RenderingSetup.SetupAll -quit` (URP, templates, probe scene; needs TMP first; seeds the build-scene list only when it is empty, verified) | exit 0 |
| `report` | `QuestSetup.Report` | prints `[REPORT]` and `[OPENXR-VALIDATION]` lines |
| `meta-report` | `OVRProjectSetupCLI.GenerateProjectSetupReport -reportFile Logs/ci/meta-setup.json` | no Required task outstanding |
| `build [apk]` | `QuestBuild.BuildApk -outputPath …` (no `-quit`) | `[BUILD] result=Succeeded` and the APK exists |
| `install`, `log` | adb | |

- Every call uses `-batchmode -nographics -buildTarget Android -projectPath $PROJ -logFile Logs/ci/<cmd>.log`. Environment overrides: `UNITY`, `PROJ`, `LOGS`, `ADB`. Exit 134 becomes 0 only when the success marker is present (§2).

## 11. Open items / UNVERIFIED

- **Nothing was run on a Quest 3S.** Untested on the headset: OpenXR + MetaXRFeature + hands-only at runtime; URP passthrough (HDR off, clear alpha 0); SRP foveation; `-testPlatform Android`; `AutoRunPlayer`; the proximity broadcast.
- **To decide with the hands and MR specs:**
  - Is `XRHandSubsystem` needed? It means enabling the Unity XR Hands features (§6.3).
  - Symmetric projection and Multiview Render Regions.
  - Whether to keep `HandInteractionProfile` (harmless; it gives Input System poke and pinch poses).
- **Target SDK 34** follows Meta SDK v207's store rule. Re-check the Horizon Store requirement before submission.
- **Size levers not tried:** managed stripping level; dropping unused Meta packages (MRUK's `libmrutilitykitshared.so` is 5 MB, Meta Audio's `libMetaXRAudioUnity.so` 2.7 MB); `Il2CppCodeGeneration.OptimizeSize`.
- **Meta XR Simulator is not installed.** Install it by hand if wanted; the setup-tool task would download a `.dmg` into `~/Downloads`.
