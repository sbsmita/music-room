// Explicit, idempotent, GUI-free project configuration for Quest 3 / 3S (specs 01, 04 and 09).
// Never runs Meta's unfiltered "Fix All" (it downloads the XR Simulator, enables SpaceWarp and edits only the
// current quality level); the Meta CLI report (Tools/unity.sh meta-report) is the gate instead.
// Run: Unity -batchmode -nographics -buildTarget Android -projectPath P -executeMethod MusicRoom.EditorTools.QuestSetup.ConfigureAll -quit
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MusicRoom.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace MusicRoom.EditorTools
{
    public static class QuestSetup
    {
        public const string PackageId = "com.vrmusicroom.app";
        public const string ProductName = "VR Music Room";
        public const string CompanyName = "VR Music Room";
        const BuildTargetGroup Android = BuildTargetGroup.Android;

        /// Unity XR Hands OpenXR features (XRHandSubsystem). OFF: when ON, com.unity.xr.hands rewrites the manifest to
        /// oculus.software.handtracking required="false" (spec 09 section 6.3). We read hands through OVRPlugin.
        public static bool EnableUnityXRHandsFeatures = false;

        public static void ConfigureAll()
        {
            EnsureAndroidTarget();
            ConfigurePlayer();
            ConfigureLayers();
            ConfigureXR();
            ConfigureMeta();
            AudioProjectSetup.Apply();
            BurstAudioSetup.Apply();
            AssetDatabase.SaveAssets();
            LogOpenXRValidation();
            Debug.Log("[SETUP] ConfigureAll done");
        }

        public static void EnsureAndroidTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(Android, BuildTarget.Android);
                Debug.Log($"[SETUP] SwitchActiveBuildTarget(Android) = {ok}");
            }
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC; // per machine (Library/)
            EditorUserBuildSettings.buildAppBundle = false;                             // .apk, not .aab
        }

        public static void ConfigurePlayer()
        {
            var nbt = NamedBuildTarget.Android;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(nbt, PackageId);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;   // Meta Required
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34; // Meta store rule (v207)

            PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;                       // Meta Required with OpenXR
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing; // legacy field still checked by Meta
            PlayerSettings.MTRendering = true;
            PlayerSettings.graphicsJobs = true;
            PlayerSettings.graphicsJobMode = GraphicsJobMode.Legacy;
            PlayerSettings.use32BitDisplayBuffer = true;
            PlayerSettings.gpuSkinning = true;
            // E5 (experience.md section 7): FrameTimingManager's GPU time is the quality governor's fallback when
            // XR_META_performance_metrics gives none (RigBootstrap.GpuMs).
            PlayerSettings.enableFrameTimingStats = true;

            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity; // Meta Required
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;     // OpenXR error rule
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.Android.androidTVCompatibility = false;
            PlayerSettings.Android.forceInternetPermission = false;   // offline app (MetaQuestFeature strips the rest)

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.virtualRealitySplashScreen = null;

            // Active Input Handling = Both (0 old, 1 new, 2 both): the Input System for OpenXR, legacy for safety.
            // No public API; takes effect on the next editor start.
            SetProjectSettingsInt("activeInputHandler", 2);
            Debug.Log("[SETUP] Player settings applied");
        }

        /// User layers the app relies on (Core Layers: PlaySurface 29, Hidden 30, PanelUI 31), named in TagManager.asset.
        /// Code uses the indices (they work unnamed too); the names keep the editor and the Frame Debugger readable.
        public static void ConfigureLayers()
        {
            var tm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (tm == null) { Debug.LogWarning("[SETUP] TagManager.asset not loadable"); return; }
            var so = new SerializedObject(tm);
            var layers = so.FindProperty("layers");
            foreach (var (index, name) in Layers.Names)
            {
                var p = layers.GetArrayElementAtIndex(index);
                if (p.stringValue == name) continue;
                if (!string.IsNullOrEmpty(p.stringValue)) Debug.LogWarning($"[SETUP] layer {index} was '{p.stringValue}', now '{name}'");
                p.stringValue = name;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[SETUP] layers " + string.Join(", ", Layers.Names.Select(l => $"{l.index}={l.name}")));
        }

        static void SetProjectSettingsInt(string prop, int value)
        {
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps == null) { Debug.LogWarning("[SETUP] ProjectSettings.asset not loadable"); return; }
            var so = new SerializedObject(ps);
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[SETUP] no property {prop}"); return; }
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- XR Plug-in Management + OpenXR ----------------
        public static void ConfigureXR()
        {
            var perTarget = GetOrCreatePerBuildTargetSettings();
            foreach (var g in new[] { Android, BuildTargetGroup.Standalone }) // Standalone: editor play mode / XR Simulator
            {
                if (!perTarget.HasManagerSettingsForBuildTarget(g)) perTarget.CreateDefaultManagerSettingsForBuildTarget(g);
                var general = perTarget.SettingsForBuildTarget(g);
                general.InitManagerOnStart = true;
                var manager = general.Manager;
                foreach (var l in manager.activeLoaders.ToList())
                    if (l != null && l.GetType().FullName == "Unity.XR.Oculus.OculusLoader")
                        XRPackageMetadataStore.RemoveLoader(manager, l.GetType().FullName, g);
                bool assigned = XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, g);
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(general);
                Debug.Log($"[SETUP] AssignLoader(OpenXRLoader, {g}) = {assigned}; loaders = {string.Join(",", manager.activeLoaders.Select(x => x.GetType().Name))}");

                FeatureHelpers.RefreshFeatures(g); // creates the OpenXR settings asset + feature sub-assets
                var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
                if (oxr == null) throw new Exception($"OpenXR settings for {g} not created");

                Enable<Meta.XR.MetaXRFeature>(oxr, true);             // OVRPlugin over OpenXR: hands, passthrough, scene
                Enable<OculusTouchControllerProfile>(oxr, true);      // Meta Required even for hands-only
                Enable<HandInteractionProfile>(oxr, true);            // XR_EXT_hand_interaction poses (harmless)
                Enable<UnityEngine.XR.Hands.OpenXR.HandTracking>(oxr, EnableUnityXRHandsFeatures);
                Enable<UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim>(oxr, EnableUnityXRHandsFeatures);
                if (g == Android)
                {
                    // SpaceWarp off (decided). Android-only features (absent from the Standalone feature set). With OpenXR
                    // >= 1.15 on Unity >= 6000.1 Meta's feature set uses Unity's SpaceWarpFeature (MetaXRFeatureSet.cs:42-46)
                    // and its Optional fix turns that one on, so both are pinned off here.
                    Enable<Meta.XR.MetaXRSpaceWarp>(oxr, false);      // Meta Required: off
                    Enable<SpaceWarpFeature>(oxr, false);             // Unity Application SpaceWarp: off
                    // Foveation = Unity's SRP Foveation API (spec 09; com.unity.xr.openxr foveatedrendering.md:71-79, 127-131):
                    // the "Foveated Rendering" feature on + foveatedRenderingApi = SRPFoveation here, and at runtime
                    // XRDisplaySubsystem.foveatedRenderingLevel (RigBootstrap.ApplyFoveation). Meta's Legacy features (enabled
                    // by the Meta feature set's defaults, MetaXRFeatureSet.cs:59-65) are off: they pair with the Legacy API only.
                    Enable<FoveatedRenderingFeature>(oxr, true);
                    Enable<Meta.XR.MetaXRFoveationFeature>(oxr, false);
                    Enable<Meta.XR.MetaXREyeTrackedFoveationFeature>(oxr, false);
                    var q = oxr.GetFeature<MetaQuestFeature>();
                    if (q != null)
                    {
                        q.enabled = true;
                        q.ForceRemoveInternetPermission = true;        // offline app: drop android.permission.INTERNET
                        var so = new SerializedObject(q);
                        var list = so.FindProperty("targetDevices");   // internal: quest, quest2, cambria, eureka (Quest 3), quest3s
                        if (list != null)
                            for (int i = 0; i < list.arraySize; i++)
                            {
                                var e = list.GetArrayElementAtIndex(i);
                                string name = e.FindPropertyRelative("manifestName").stringValue;
                                e.FindPropertyRelative("enabled").boolValue = name == "eureka" || name == "quest3s";
                            }
                        so.FindProperty("m_foveatedRenderingApi").intValue = (int)OpenXRSettings.BackendFovationApi.SRPFoveation;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(q);
                    }
                    // Meta recommends the subsampled layout on Vulkan, but it pairs with Meta's Legacy foveation; with Unity's SRP
                    // Foveation feature on, both drove the same extension and the headset showed flickering blocks in the right eye.
                    Enable<Meta.XR.MetaXRSubsampledLayout>(oxr, false);
                    // E5: OVRManager owns dynamic resolution (enableDynamicResolution, 0.7 .. RigBootstrap.DynamicResolutionMax);
                    // Unity's Automatic Viewport Dynamic Resolution would be a second controller fighting it: pinned off.
                    Enable<AutomaticDynamicResolutionFeature>(oxr, false);
                    oxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;   // multiview
                    oxr.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
                    // 24-bit depth (2026-11-17): at 16 bits with the 2 cm near plane two surfaces less than ~5-9 mm apart at
                    // 2-3 m z-fought on the headset (the room's painting on its inner frame, a frame's plaque label).
                    oxr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth24Bit;
                }
                EditorUtility.SetDirty(oxr);
                FeatureHelpers.RefreshFeatures(g);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[SETUP] OpenXR configured");
        }

        static void Enable<T>(OpenXRSettings s, bool on) where T : OpenXRFeature
        {
            var f = s.GetFeature<T>();
            if (f == null) { Debug.LogWarning($"[SETUP] OpenXR feature {typeof(T).Name} not found"); return; }
            f.enabled = on;
            EditorUtility.SetDirty(f);
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTargetSettings()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                var guid = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget").FirstOrDefault();
                if (guid != null) perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (perTarget == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            return perTarget;
        }

        public static void LogOpenXRValidation()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, Android);
            foreach (var i in issues)
                Debug.Log($"[OPENXR-VALIDATION] {(i.error ? "ERROR" : "warn")} autoFix={i.fixItAutomatic} :: {i.message}");
            Debug.Log($"[OPENXR-VALIDATION] {issues.Count} issue(s), {issues.Count(i => i.error)} error(s)");
        }

        // ---------------- Meta XR Core SDK project config ----------------
        public static void ConfigureMeta()
        {
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.targetDeviceTypes = new List<OVRProjectConfig.DeviceType> { OVRProjectConfig.DeviceType.Quest3, OVRProjectConfig.DeviceType.Quest3S };
            cfg.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.HandsOnly;
            // E4 (experience.md section 6): HIGH (Fast Motion Mode for the whole session). The device fallback this comment
            // used to name: on the Quest 3S with LOW the hands were sampled every 40-77 ms even while the runtime FMM hint
            // was accepted ("[Hands] FMM request on: ok", sampleInterval unchanged), so the drawn hands lagged and jumped.
            // The FastMotionPolicy hints stay (harmless with HIGH).
            cfg.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
            cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;               // MRUK windows / tables (USE_SCENE)
            cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            cfg.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;  // suppress the boundary in MR
            cfg.requiresSystemKeyboard = false;
            cfg.experimentalFeaturesEnabled = false;
            cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            OVRProjectConfig.CommitProjectConfig(cfg);

            var rt = OVRRuntimeSettings.Instance;
            rt.HandSkeletonVersion = OVRHandSkeletonVersion.OpenXR;   // joint positions only exist with this skeleton
            rt.colorSpace = OVRManager.ColorSpace.P3;
            OVRRuntimeSettings.CommitRuntimeSettings(rt);
            AssetDatabase.SaveAssets();

            // Meta's store-compatible Assets/Plugins/Android/AndroidManifest.xml (Required GameActivity task).
            OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);

            AddDefine(NamedBuildTarget.Android, "OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING");
            AddDefine(NamedBuildTarget.Standalone, "OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING");

            // Meta quality tasks check the *current* quality level only: apply to every level.
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.pixelLightCount = 1;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.antiAliasing = 4;
            }
            QualitySettings.SetQualityLevel(cur, false);

            // Orphans left by XR Management while com.unity.xr.oculus was installed.
            foreach (var p in new[] { "Assets/XR/Loaders/OculusLoader.asset", "Assets/XR/Settings/OculusSettings.asset" })
                if (Type.GetType("Unity.XR.Oculus.OculusLoader, Unity.XR.Oculus") == null && File.Exists(p))
                    AssetDatabase.DeleteAsset(p);
            AssetDatabase.SaveAssets();
            Debug.Log("[SETUP] Meta project config applied");
        }

        static void AddDefine(NamedBuildTarget t, string define)
        {
            var defs = PlayerSettings.GetScriptingDefineSymbols(t).Split(';').Where(d => d.Length > 0).ToList();
            if (!defs.Contains(define)) { defs.Add(define); PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", defs)); }
        }

        /// Seconds MetaReport waits for the Package Manager list before reporting anyway (and failing).
        public const double MetaReportPackageWait = 90;

        /// Meta's own CLI report, with Main.unity open so the scene rules (passthrough camera etc.) evaluate the real rig.
        /// Run WITHOUT -quit: it waits (editor updates) until Meta's PackageList has the Package Manager list, because
        /// every rule whose validity asks PackageList.IsPackageInstalled (the whole Packages group, several Required: OpenXR
        /// loader, Oculus Touch profile, SpaceWarp, one XR plugin) silently drops out of a report made before that
        /// (spec 01 section 4.2). Logs "[META-REPORT] done exit=N" and exits with N (1 when the list never arrived).
        public static void MetaReport()
        {
            if (File.Exists(SceneBuilder.MainScenePath)) EditorSceneManager.OpenScene(SceneBuilder.MainScenePath);
            double t0 = EditorApplication.timeSinceStartup;
            var available = Type.GetType("Meta.XR.Editor.Utils.PackageList, Oculus.VR.Editor")
                ?.GetProperty("PackageManagerListAvailable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (available == null) Debug.LogWarning("[META-REPORT] Meta.XR.Editor.Utils.PackageList.PackageManagerListAvailable not found");
            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                bool ready = available != null && (bool)available.GetValue(null);
                double waited = EditorApplication.timeSinceStartup - t0;
                if (!ready && waited < MetaReportPackageWait) return;
                EditorApplication.update -= wait;
                Debug.Log($"[META-REPORT] package list {(ready ? "ready" : "NOT READY")} after {waited:F1} s");
                int code = ready ? 0 : 1;
                try { OVRProjectSetupCLI.GenerateProjectSetupReport(); }
                catch (Exception e) { Debug.LogException(e); code = 1; }
                Debug.Log($"[META-REPORT] done exit={code}");
                if (Application.isBatchMode) EditorApplication.Exit(code);
            };
            EditorApplication.update += wait;
        }

        // ---------------- Read-back (run in a fresh editor process) ----------------
        public static void Report()
        {
            var nbt = NamedBuildTarget.Android;
            var sb = new StringBuilder();
            void L(string k, object v) => sb.AppendLine($"[REPORT] {k} = {v}");
            L("activeBuildTarget", EditorUserBuildSettings.activeBuildTarget);
            L("androidBuildSubtarget", EditorUserBuildSettings.androidBuildSubtarget);
            L("Android.textureCompressionFormats", string.Join(",", PlayerSettings.Android.textureCompressionFormats));
            L("productName", PlayerSettings.productName);
            L("applicationIdentifier", PlayerSettings.GetApplicationIdentifier(nbt));
            L("scriptingBackend", PlayerSettings.GetScriptingBackend(nbt));
            L("targetArchitectures", PlayerSettings.Android.targetArchitectures);
            L("minSdkVersion", PlayerSettings.Android.minSdkVersion);
            L("targetSdkVersion", PlayerSettings.Android.targetSdkVersion);
            L("graphicsAPIs", string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)));
            L("colorSpace", PlayerSettings.colorSpace);
            L("stereoRenderingPath", PlayerSettings.stereoRenderingPath);
            L("applicationEntry", PlayerSettings.Android.applicationEntry);
            L("defaultInterfaceOrientation", PlayerSettings.defaultInterfaceOrientation);
            L("forceInternetPermission", PlayerSettings.Android.forceInternetPermission);
            L("splash", PlayerSettings.SplashScreen.show);
            L("renderPipeline", GraphicsSettings.defaultRenderPipeline ? AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline) : "Built-in");
            L("scenes", string.Join(",", EditorBuildSettings.scenes.Select(s => s.path)));
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(Android);
            L("xr.initOnStart", general?.InitManagerOnStart);
            L("xr.loaders", general == null ? "none" : string.Join(",", general.Manager.activeLoaders.Select(x => x.GetType().FullName)));
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(Android);
            if (oxr != null)
            {
                L("openxr.renderMode", oxr.renderMode);
                L("openxr.depthSubmissionMode", oxr.depthSubmissionMode);
                L("openxr.latencyOptimization", oxr.latencyOptimization);
                foreach (var f in oxr.GetFeatures().Where(f => f.enabled)) L("openxr.enabledFeature", $"{f.GetType().FullName} ({f.name})");
                // Expected: FoveatedRenderingFeature True; MetaXRFoveationFeature, MetaXREyeTrackedFoveationFeature,
                // SpaceWarpFeature, MetaXRSpaceWarp False (SRP foveation, SpaceWarp off).
                L("openxr.feature.FoveatedRenderingFeature", oxr.GetFeature<FoveatedRenderingFeature>()?.enabled);
                L("openxr.feature.MetaXRFoveationFeature", oxr.GetFeature<Meta.XR.MetaXRFoveationFeature>()?.enabled);
                L("openxr.feature.MetaXREyeTrackedFoveationFeature", oxr.GetFeature<Meta.XR.MetaXREyeTrackedFoveationFeature>()?.enabled);
                L("openxr.feature.SpaceWarpFeature", oxr.GetFeature<SpaceWarpFeature>()?.enabled);
                L("openxr.feature.MetaXRSpaceWarp", oxr.GetFeature<Meta.XR.MetaXRSpaceWarp>()?.enabled);
                L("openxr.feature.AutomaticDynamicResolutionFeature", oxr.GetFeature<AutomaticDynamicResolutionFeature>()?.enabled);
                var q = oxr.GetFeature<MetaQuestFeature>();
                if (q != null)
                {
                    var so = new SerializedObject(q);
                    var list = so.FindProperty("targetDevices");
                    for (int i = 0; list != null && i < list.arraySize; i++)
                    {
                        var e = list.GetArrayElementAtIndex(i);
                        L("openxr.metaQuest.targetDevice", $"{e.FindPropertyRelative("manifestName").stringValue}={e.FindPropertyRelative("enabled").boolValue}");
                    }
                    L("openxr.metaQuest.foveatedRenderingApi", so.FindProperty("m_foveatedRenderingApi").intValue);
                    L("openxr.metaQuest.forceRemoveInternetPermission", q.ForceRemoveInternetPermission);
                }
            }
            var cfg = OVRProjectConfig.CachedProjectConfig;
            L("ovr.handTrackingSupport", cfg.handTrackingSupport);
            L("ovr.handTrackingFrequency", cfg.handTrackingFrequency);
            L("ovr.passthrough", cfg.insightPassthroughSupport);
            L("ovr.scene", cfg.sceneSupport);
            L("ovr.anchors", cfg.anchorSupport);
            L("ovr.boundaryVisibility", cfg.boundaryVisibilitySupport);
            L("ovr.targetDevices", string.Join(",", cfg.targetDeviceTypes));
            L("ovr.handSkeleton", OVRRuntimeSettings.Instance.HandSkeletonVersion);
            L("ovr.colorSpace", OVRRuntimeSettings.Instance.colorSpace);
            var so2 = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
            L("audio.spatializer", so2.FindProperty("m_SpatializerPlugin").stringValue);
            L("audio.ambisonic", so2.FindProperty("m_AmbisonicDecoderPlugin").stringValue);
            L("audio.dspBufferSize", so2.FindProperty("m_DSPBufferSize").intValue);
            L("audio.rolloffScale", so2.FindProperty("Rolloff Scale").floatValue);
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            L("activeInputHandler", ps.FindProperty("activeInputHandler").intValue);
            L("enableFrameTimingStats", PlayerSettings.enableFrameTimingStats);
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]).FindProperty("layers");
            foreach (var (index, _) in Layers.Names) L($"layer.{index}", tags.GetArrayElementAtIndex(index).stringValue);
            Debug.Log(sb.ToString());
            LogOpenXRValidation();
        }
    }
}
