// Template material assets, one per shader + keyword set (spec 09 section 7.3). Runtime materials are ALWAYS clones of
// a template (MaterialLibrary), because URP strips shader_feature variants that no asset uses, and Shader.Find of an
// unreferenced shader returns null in a player. The foundation templates live under Assets/MusicRoom/Resources;
// every module can add its own by implementing MusicRoom.Core.IMaterialTemplateProvider in its runtime assembly
// (discovered here by reflection, so nobody edits this file).
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.EditorTools
{
    public static class MaterialTemplates
    {
        public const string SharedDir = "Assets/MusicRoom/Resources/MaterialTemplates";

        /// The foundation's own templates (Resources path relative to "MaterialTemplates/").
        public static IEnumerable<MaterialTemplateSpec> Foundation()
        {
            yield return new MaterialTemplateSpec("LitOpaque", "Universal Render Pipeline/Lit", m => m.SetColor(MaterialLibrary.BaseColor, Color.white));
            yield return new MaterialTemplateSpec("LitEmissive", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor(MaterialLibrary.EmissionColor, Color.white);
                // URP's material postprocessor keeps _EMISSION only with an emissive GI flag (BaseShaderGUI.SetMaterialKeywords);
                // with None every clone of this template ignored _EmissionColor (Room / Harp CONTRACT-REQUESTS, integration).
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.EnableKeyword("_EMISSION");
            });
            yield return new MaterialTemplateSpec("LitTransparent", "Universal Render Pipeline/Lit", m => UrpTransparent(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 0f));
            yield return new MaterialTemplateSpec("LitAlphaTest", "Universal Render Pipeline/Lit", m =>
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetFloat("_Cull", (float)CullMode.Off);      // leaves, both faces
            });
            yield return new MaterialTemplateSpec("LitMetal", "Universal Render Pipeline/Lit", m =>
            {
                m.SetFloat(MaterialLibrary.Metallic, 1f);
                m.SetFloat(MaterialLibrary.Smoothness, 0.7f);
                m.EnableKeyword("_SPECULAR_SETUP");            // keep a distinct keyword set from LitOpaque
                m.DisableKeyword("_SPECULAR_SETUP");
            });
            yield return new MaterialTemplateSpec("Unlit", "Universal Render Pipeline/Unlit", m => m.SetColor(MaterialLibrary.BaseColor, Color.white));
            yield return new MaterialTemplateSpec("UnlitTransparent", "Universal Render Pipeline/Unlit", m => UrpTransparent(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 0f));
            yield return new MaterialTemplateSpec("UnlitAdditive", "Universal Render Pipeline/Unlit", m => UrpTransparent(m, BlendMode.SrcAlpha, BlendMode.One, 2f));
            yield return new MaterialTemplateSpec("ParticlesUnlitAdditive", "Universal Render Pipeline/Particles/Unlit", m => UrpTransparent(m, BlendMode.SrcAlpha, BlendMode.One, 2f));
            yield return new MaterialTemplateSpec("ParticlesUnlitAlpha", "Universal Render Pipeline/Particles/Unlit", m => UrpTransparent(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 0f));
            yield return new MaterialTemplateSpec("HandGhost", "MusicRoom/HandGhost", m => { m.SetFloat("_Alpha", 1f); });
        }

        static void UrpTransparent(Material m, BlendMode src, BlendMode dst, float blendMode)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", blendMode);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        [MenuItem("Music Room/Render/Create Material Templates")]
        public static void CreateAll()
        {
            // Template names are global Resources paths ("MaterialTemplates/<Name>"): a duplicate across the foundation and
            // the providers would make one module silently load another's material. Refuse before writing anything.
            var owner = new Dictionary<string, string>(System.StringComparer.Ordinal);
            void Claim(string name, string who)
            {
                if (owner.TryGetValue(name, out string first))
                    throw new System.InvalidOperationException($"[MATERIALS-ERROR] duplicate template name '{name}' ({first} and {who})");
                owner[name] = who;
            }
            var providers = ProviderDiscovery.Instantiate<IMaterialTemplateProvider>();
            foreach (var spec in Foundation()) Claim(spec.Name, "foundation");
            foreach (var provider in providers) foreach (var spec in provider.Templates) Claim(spec.Name, provider.GetType().FullName);

            int made = 0;
            foreach (var spec in Foundation()) if (Make(SharedDir, spec)) made++;
            foreach (var provider in providers)
            {
                string dir = provider.ResourcesFolder.TrimEnd('/') + "/Resources/MaterialTemplates";
                foreach (var spec in provider.Templates) if (Make(dir, spec)) made++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[MATERIALS] {made} template(s) written/updated");
        }

        static bool Make(string dir, MaterialTemplateSpec spec)
        {
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{spec.Name}.mat";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find(spec.ShaderName);
                if (sh == null) { Debug.LogError($"[MATERIALS] shader not found in editor: {spec.ShaderName} (template {spec.Name})"); return false; }
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            spec.Init?.Invoke(m);
            EditorUtility.SetDirty(m);
            return true;
        }
    }

    /// Finds parameterless implementations of a provider interface (editor time). Same scan as the runtime module
    /// discovery: every MusicRoom.* assembly except MusicRoom.Tests.*, so a test helper can never write assets.
    /// Deterministic order (full type name).
    public static class ProviderDiscovery
    {
        public static List<T> Instantiate<T>() where T : class => ModuleDiscovery.CreateAll<T>();
    }
}
