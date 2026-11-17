// APK build entry (spec 09 section 6). Run (no -quit: the method exits itself):
//   Unity -batchmode -nographics -buildTarget Android -projectPath P -executeMethod MusicRoom.EditorTools.BuildPlayer.BuildApk
//         [-outputPath Builds/Android/VRMusicRoom.apk] [-development] -logFile L
// Markers: "[BUILD] result=... apkBytes=... time=..." and "[BUILD-STEP]" / "[BUILD-ERROR]" lines; unity.sh requires
// result=Succeeded AND the APK file.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MusicRoom.EditorTools
{
    public static class BuildPlayer
    {
        public static void BuildApk()
        {
            int code = 1;
            try
            {
                string output = Arg("-outputPath") ?? "Builds/Android/VRMusicRoom.apk";
                bool dev = Environment.GetCommandLineArgs().Contains("-development");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
                if (scenes.Length == 0)
                {
                    SceneBuilder.BuildMain();
                    scenes = new[] { SceneBuilder.MainScenePath };
                }

                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
                // Baked textures are generated outputs (git-ignored, not carried by the folder merge): bake what is missing
                // and refuse to build without them, so a device never falls back to runtime generation (section 17).
                TextureBaker.BakeMissing();
                var missing = TextureBaker.Missing();
                if (missing.Count > 0)
                {
                    foreach (var m in missing) Debug.Log($"[BUILD-ERROR] missing baked texture {m}");
                    throw new InvalidOperationException($"{missing.Count} baked texture(s) missing");
                }
                // Release signing from env (passwords are never serialized); otherwise the debug keystore.
                string ks = Environment.GetEnvironmentVariable("MR_KEYSTORE");
                PlayerSettings.Android.useCustomKeystore = !string.IsNullOrEmpty(ks);
                if (PlayerSettings.Android.useCustomKeystore)
                {
                    PlayerSettings.Android.keystoreName = ks;
                    PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("MR_KEYSTORE_PASS");
                    PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("MR_KEYALIAS");
                    PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("MR_KEYALIAS_PASS");
                }
                var opts = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = dev ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(opts);
                var s = report.summary;
                long apkBytes = File.Exists(output) ? new FileInfo(output).Length : 0;
                Debug.Log($"[BUILD] result={s.result} apkBytes={apkBytes} time={s.totalTime} errors={s.totalErrors} warnings={s.totalWarnings} output={s.outputPath}");
                foreach (var step in report.steps) Debug.Log($"[BUILD-STEP] {step.duration} {step.name}");
                foreach (var m in report.steps.SelectMany(st => st.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    Debug.Log($"[BUILD-ERROR] {m.content}");
                code = s.result == BuildResult.Succeeded ? 0 : 1;
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keyaliasName = "";
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
