// URP 17 Forward for Quest 3S (specs 07 and 09): one script-created pipeline asset + renderer, MSAA 4, HDR off, no
// depth/opaque texture, no post, Linear colour, SRP foveation. Also seeds the material templates and reports TMP.
// Run: -buildTarget Android -executeMethod MusicRoom.EditorTools.RenderSetup.SetupAll -quit (after tmp-import)
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace MusicRoom.EditorTools
{
    public static class RenderSetup
    {
        public const string SettingsDir = "Assets/MusicRoom/Settings";
        public const string UrpPath = SettingsDir + "/QuestURP.asset";
        public const string RendererPath = SettingsDir + "/QuestURP_Renderer.asset";

        public static void SetupAll()
        {
            SetupUrp();
            TextSetup.ReportTmp();
            MaterialTemplates.CreateAll();
            AssetDatabase.SaveAssets();
            Debug.Log("[RENDER] SetupAll done");
        }

        public static void SetupUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, RendererPath);
                ResourceReloader.TryReloadAllNullIn(rd, UniversalRenderPipelineAsset.packagePath);
            }
            rd.renderingMode = RenderingMode.Forward;
            rd.depthPrimingMode = DepthPrimingMode.Disabled;
            rd.intermediateTextureMode = IntermediateTextureMode.Auto;   // Meta Recommended
            EditorUtility.SetDirty(rd);

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(urp, UrpPath);
            }
            urp.msaaSampleCount = 4;                     // OVRPlugin.recommendedMSAALevel
            urp.supportsHDR = false;                     // keeps eye-buffer alpha for passthrough
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.renderScale = 1f;
            urp.upscalingFilter = UpscalingFilterSelection.Auto;
            urp.shadowDistance = 4f;                     // shadows are used in MR only (spec 07 section 1)
            urp.shadowCascadeCount = 1;
            urp.maxAdditionalLightsCount = 4;            // floor lamp + fire + 2 candles
            urp.useSRPBatcher = true;
            urp.supportsDynamicBatching = false;
            var so = new SerializedObject(urp);          // internal setters in URP 17.3
            so.FindProperty("m_MainLightRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 1024;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            // E11 (experience.md section 12): box-projected reflections, so the cymbals, the chrome and the black piano reflect
            // the window and the fire where they are (the room's probe is a box the size of the room); probe blending stays off.
            so.FindProperty("m_ReflectionProbeBoxProjection").boolValue = true;
            so.FindProperty("m_ReflectionProbeBlending").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
                QualitySettings.antiAliasing = 4;
            }
            QualitySettings.SetQualityLevel(cur, false);

            // OpenXR Meta Quest: SRP foveation API (serialized field; the property is internal).
            var mq = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android)?.GetFeature<MetaQuestFeature>();
            if (mq != null)
            {
                var fso = new SerializedObject(mq);
                fso.FindProperty("m_foveatedRenderingApi").intValue = (int)OpenXRSettings.BackendFovationApi.SRPFoveation;
                fso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mq);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[RENDER] URP default = {AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline)} msaa={urp.msaaSampleCount} hdr={urp.supportsHDR} addLights={urp.maxAdditionalLightsCount}");
        }
    }
}
