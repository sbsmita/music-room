// Writes the ONLY scene, Assets/Scenes/Main.unity: the Core SDK OVRCameraRig prefab instance (spec 01 section 2.2)
// configured in code, plus the "App" root that builds everything else at runtime. Run: Tools/unity.sh scene.
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicRoom.EditorTools
{
    public static class SceneBuilder
    {
        public const string MainScenePath = "Assets/Scenes/Main.unity";
        public const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        [MenuItem("Music Room/Build Main Scene")]
        public static void BuildMain()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MainScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Populate(scene);
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[SCENE] wrote {MainScenePath}; build scenes = {EditorBuildSettings.scenes.Length}");
        }

        /// Fills a (new, empty) scene. Separated so the EditMode test can inspect the result without saving.
        public static GameObject Populate(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (prefab == null) throw new FileNotFoundException(RigPrefabPath);
            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            rigGo.name = RigBootstrap.RigName;
            var mgr = rigGo.GetComponent<OVRManager>();
            mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;   // floor-level origin: y = 0 on the floor
            // Passthrough is started only on entering "Play in my room" (burst.md section 10.1: CPU level 4 needs no passthrough
            // feature enabled): XrMode.StartPassthrough adds the layer and sets this flag at runtime.
            mgr.isInsightPassthroughEnabled = false;
            mgr.enableDynamicResolution = true;                             // OVRManager's; OpenXR's own feature stays off (QuestSetup)
            mgr.quest3MinDynamicResolutionScale = RigBootstrap.DynamicResolutionMin;
            mgr.quest3MaxDynamicResolutionScale = RigBootstrap.DynamicResolutionMax;
            mgr.shouldBoundaryVisibilityBeSuppressed = false;
            var so = new SerializedObject(mgr);
            foreach (var p in new[] { "requestScenePermissionOnStartup", "requestBodyTrackingPermissionOnStartup", "requestFaceTrackingPermissionOnStartup", "requestEyeTrackingPermissionOnStartup", "requestRecordAudioPermissionOnStartup" })
            {
                var prop = so.FindProperty(p);
                if (prop != null) prop.boolValue = false;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            // No OVRPassthroughLayer in the scene: RigBootstrap.EnsurePassthroughLayer adds the underlay when MR is entered.
            var pt = rigGo.GetComponent<OVRPassthroughLayer>();
            if (pt != null) Object.DestroyImmediate(pt, true);
            var rig = rigGo.GetComponent<OVRCameraRig>();
            var cam = rig.centerEyeAnchor.GetComponent<Camera>();
            cam.nearClipPlane = 0.02f;                                      // near 0.02 m, far 40 m
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);                // alpha < 1: Meta's Required passthrough rule
            cam.allowHDR = false;                                           // keep eye-buffer alpha
            cam.cullingMask &= ~Layers.HeadCameraExcludeMask;               // never the Hidden / PanelUI layers (AppRoot repeats it at boot)
            cam.tag = "MainCamera";
            if (rig.centerEyeAnchor.GetComponent<AudioListener>() == null) rig.centerEyeAnchor.gameObject.AddComponent<AudioListener>();
            var fallback = rigGo.AddComponent<DesktopRigFallback>();
            fallback.enabled = false;                                       // RigBootstrap enables it when no XR runtime is present
            EditorUtility.SetDirty(mgr);

            var app = new GameObject(AppRoot.RootName);
            var handInput = app.AddComponent<HandInput>();
            handInput.handMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialTemplates.SharedDir + "/HandGhost.mat");
            if (handInput.handMaterial == null) Debug.LogWarning("[SCENE] HandGhost template missing: run render-setup first (hands will not render on device)");
            app.AddComponent<AppRoot>();
            SceneManager.MoveGameObjectToScene(app, scene);
            return app;
        }
    }
}
