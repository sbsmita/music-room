// TextMeshPro Essentials (LiberationSans SDF + dynamic fallback) imported headlessly (spec 09 section 8).
// ImportPackage is deferred to a later editor tick: run WITHOUT -quit and exit from the callback:
//   Unity -batchmode -nographics -buildTarget Android -projectPath P -executeMethod MusicRoom.EditorTools.TextSetup.ImportTmpEssentialsAndExit
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MusicRoom.EditorTools
{
    public static class TextSetup
    {
        public const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public static void ImportTmpEssentialsAndExit()
        {
            if (File.Exists(TmpSettingsPath)) { Debug.Log("[RENDER] TMP already imported"); EditorApplication.Exit(0); return; }
            AssetDatabase.importPackageCompleted += name => { Debug.Log($"[RENDER] importPackageCompleted {name}"); AssetDatabase.Refresh(); EditorApplication.Exit(0); };
            AssetDatabase.importPackageFailed += (name, err) => { Debug.LogError($"[RENDER] importPackageFailed {name}: {err}"); EditorApplication.Exit(1); };
            AssetDatabase.importPackageCancelled += name => { Debug.LogError($"[RENDER] importPackageCancelled {name}"); EditorApplication.Exit(1); };
            TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
            Debug.Log("[RENDER] ImportResources requested; waiting for callback");
        }

        public static void ReportTmp()
        {
            var s = Resources.Load<TMP_Settings>("TMP Settings");
            if (s == null) Debug.LogWarning("[RENDER] TMP Essentials missing: run Tools/unity.sh tmp-import first");
            else Debug.Log($"[RENDER] TMP_Settings ok; defaultFont = {TMP_Settings.defaultFontAsset?.name}");
        }
    }
}
