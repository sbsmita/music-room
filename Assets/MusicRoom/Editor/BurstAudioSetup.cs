// Project settings for the Burst audio path (spec 10-upgrade/burst.md section 8). Batch entry: -executeMethod
// MusicRoom.EditorTools.BurstAudioSetup.Apply; QuestSetup.ConfigureAll calls it after AudioProjectSetup.Apply, so
// Tools/unity.sh configure applies it. Idempotent; prints [BURST-SETUP] lines.
// IL2CPP: ARM64 only, Release C++ configuration (Master for store builds), "Faster runtime" code generation, method-only
// stack traces. Burst AOT for Android (ProjectSettings/BurstAotSettings_Android.json, written directly because the Burst
// settings API is internal; in the exact field set and order Burst 1.8.31 serialises for Android, so Burst never rewrites
// it): Burst on, optimisations on, safety checks off, no forced debug info, Armv8-A plus the Armv8.2 half-float variant
// (Burst's runtime dispatch picks one), OptimizeFor Performance, FloatMode Default (the kernels opt into Fast per method).
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MusicRoom.EditorTools
{
    public static class BurstAudioSetup
    {
        public const string AotPath = "ProjectSettings/BurstAotSettings_Android.json";
        /// <summary>BitsetArm64Targets: ARMV8A = 1 &lt;&lt; 9 (512), ARMV8A_HALFFP = 1 &lt;&lt; 11 (2048) (Burst 1.8.31 BurstPlatformAotSettings.cs).</summary>
        public const int Arm64Targets = 512 | 2048;
        /// <summary>OptimizeFor: Default 0, Performance 1, Size 2, FastCompilation 3, Balanced 4.</summary>
        public const int OptimizeForPerformance = 1;
        /// <summary>The settings file format Burst 1.8.31 writes (BurstPlatformAotSettings.DefaultVersion).</summary>
        public const int FormatVersion = 5;

        /// <summary>The JSON exactly as Burst serialises it for Android (debug: true for symbolised simpleperf runs only).</summary>
        public static string AotJson(bool debug = false) =>
            "{\n  \"MonoBehaviour\": {\n" +
            $"    \"Version\": {FormatVersion},\n" +
            "    \"EnableBurstCompilation\": true,\n" +
            "    \"EnableOptimisations\": true,\n" +
            "    \"EnableSafetyChecks\": false,\n" +
            $"    \"EnableDebugInAllBuilds\": {(debug ? "true" : "false")},\n" +
            "    \"DebugDataKind\": 1,\n" +
            "    \"EnableArmv9SecurityFeatures\": false,\n" +
            "    \"CpuMinTargetX32\": 0,\n" +
            "    \"CpuMaxTargetX32\": 0,\n" +
            "    \"CpuMinTargetX64\": 0,\n" +
            "    \"CpuMaxTargetX64\": 0,\n" +
            $"    \"CpuTargetsArm64\": {Arm64Targets},\n" +
            $"    \"OptimizeFor\": {OptimizeForPerformance},\n" +
            "    \"FloatMode\": 0,\n" +
            "    \"StackProtector\": 0,\n" +
            "    \"StackProtectorBufferSize\": 8\n" +
            "  }\n}\n";

        [MenuItem("Music Room/Audio/Apply Burst + IL2CPP settings")]
        public static void Apply()
        {
            var nbt = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Release); // Master for store builds
            PlayerSettings.SetIl2CppCodeGeneration(nbt, Il2CppCodeGeneration.OptimizeSpeed);
            PlayerSettings.SetIl2CppStacktraceInformation(nbt, Il2CppStacktraceInformation.MethodOnly);
            Debug.Log($"[BURST-SETUP] il2cpp {PlayerSettings.GetScriptingBackend(nbt)} arch {PlayerSettings.Android.targetArchitectures} " +
                      $"cfg {PlayerSettings.GetIl2CppCompilerConfiguration(nbt)} codegen {PlayerSettings.GetIl2CppCodeGeneration(nbt)} " +
                      $"stacktrace {PlayerSettings.GetIl2CppStacktraceInformation(nbt)}");

            string json = AotJson();
            string old = File.Exists(AotPath) ? File.ReadAllText(AotPath) : null;
            if (old != json) { File.WriteAllText(AotPath, json); Debug.Log($"[BURST-SETUP] wrote {AotPath} (arm64 targets {Arm64Targets}, optimize-for performance, safety checks off)"); }
            else Debug.Log($"[BURST-SETUP] {AotPath} already current");

            string manifest = File.ReadAllText("Packages/manifest.json");
            foreach (var pkg in new[] { "\"com.unity.burst\"", "\"com.unity.mathematics\"" })
                Debug.Log(manifest.Contains(pkg) ? $"[BURST-SETUP] manifest has {pkg}" : $"[BURST-SETUP] WARNING manifest lacks direct dependency {pkg} (resolved only through URP core)");
            Debug.Log($"[BURST-SETUP] editor Burst compilation {(Unity.Burst.BurstCompiler.IsEnabled ? "on" : "OFF (Jobs > Burst > Enable Compilation)")}");
        }
    }
}
