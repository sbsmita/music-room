// Bakes procedural textures to PNGs under Assets/MusicRoom/Generated/Resources/Generated/<Module>/<name>.png so the
// headset does not spend seconds generating them (spec 07 section 7). Each module registers its generators by implementing
// MusicRoom.Core.ITextureBakeProvider in its runtime assembly (the foundation's shared set: Core's CoreTextureProvider);
// at runtime GeneratedAssets.Texture(name, generate) loads the PNG and falls back to the generator (tests, missing bake).
// Names are unique across providers (a duplicate fails the bake), generators are CPU-only (bake runs -nographics; a spec
// marked NeedsGpu is refused there instead of writing a blank PNG). Run: Tools/unity.sh bake (BuildPlayer bakes the
// missing ones before every build).
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MusicRoom.EditorTools
{
    public static class TextureBaker
    {
        public const string OutDir = "Assets/MusicRoom/Generated/Resources/" + GeneratedAssets.ResourcesPrefix;

        /// Every provider's specs, refusing duplicate names across providers (a name must mean one generator: the runtime
        /// cache and the baked PNG are keyed by it). Throws, so a batchmode bake / build exits non-zero.
        public static List<(ITextureBakeProvider provider, TextureBakeSpec spec)> CollectSpecs()
        {
            var all = new List<(ITextureBakeProvider, TextureBakeSpec)>();
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var provider in ProviderDiscovery.Instantiate<ITextureBakeProvider>())
                foreach (var spec in provider.Textures)
                {
                    if (string.IsNullOrEmpty(spec.Name) || spec.Name.IndexOf('/') <= 0)
                        Debug.LogWarning($"[BAKE] {provider.GetType().FullName}: name '{spec.Name}' should be '<Module>/<name>'");
                    string who = provider.GetType().FullName;
                    if (owner.TryGetValue(spec.Name ?? "", out string first))
                        throw new InvalidOperationException($"[BAKE-ERROR] duplicate texture name '{spec.Name}' ({first} and {who})");
                    owner[spec.Name ?? ""] = who;
                    all.Add((provider, spec));
                }
            return all;
        }

        public static string PathOf(string name) => $"{OutDir}/{name}.png";

        [MenuItem("Music Room/Bake Textures")]
        public static void BakeAll() => Bake(onlyMissing: false);

        /// Bakes the specs whose PNG is missing (BuildPlayer runs this before every APK build).
        public static void BakeMissing() => Bake(onlyMissing: true);

        /// Spec names without a baked PNG.
        public static List<string> Missing()
        {
            var missing = new List<string>();
            foreach (var (_, spec) in CollectSpecs()) if (!File.Exists(PathOf(spec.Name))) missing.Add(spec.Name);
            return missing;
        }

        static void Bake(bool onlyMissing)
        {
            // ASTC is a per-machine setting (Library/): set it so the imports below already use the Android format.
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            Directory.CreateDirectory(OutDir);
            bool noGpu = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
            int count = 0, skipped = 0, errors = 0;
            foreach (var (_, spec) in CollectSpecs())
            {
                string path = PathOf(spec.Name);
                if (onlyMissing && File.Exists(path)) { skipped++; continue; }
                if (spec.NeedsGpu && noGpu)
                {
                    // Never write the blank image a GPU generator produces without a device: GeneratedAssets would prefer it.
                    Debug.LogError($"[BAKE-ERROR] {spec.Name} needs a GPU: run NOGFX=0 Tools/unity.sh bake");
                    errors++;
                    continue;
                }
                var tex = spec.Generate != null ? spec.Generate() : null;
                if (tex == null) { Debug.LogWarning($"[BAKE] {spec.Name}: generator returned null"); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.sRGBTexture = !spec.Linear;
                imp.mipmapEnabled = spec.Mipmaps;
                if (spec.AnisoLevel > 0) imp.anisoLevel = spec.AnisoLevel;
                imp.wrapMode = spec.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.textureType = spec.NormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.alphaIsTransparency = spec.AlphaIsTransparency;
                imp.isReadable = false;
                var android = imp.GetPlatformTextureSettings("Android");
                android.overridden = true;
                // ASTC carries alpha in the same block format, so one setting serves both (spec 07 section 7).
                android.format = TextureImporterFormat.ASTC_6x6;
                imp.alphaSource = spec.HasAlpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                android.maxTextureSize = 2048;
                imp.SetPlatformTextureSettings(android);
                imp.SaveAndReimport();
                count++;
                Debug.Log($"[BAKE] {spec.Name} -> {path}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[BAKE] {count} texture(s) baked{(onlyMissing ? $", {skipped} already baked" : "")}{(errors > 0 ? $", {errors} error(s)" : "")}");
            if (errors > 0 && Application.isBatchMode) throw new InvalidOperationException($"[BAKE-ERROR] {errors} texture(s) could not be baked");
        }
    }
}
