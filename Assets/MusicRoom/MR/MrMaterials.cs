// Materials of the MR visuals. Runtime materials are clones of this module's template .mat assets
// (Assets/MusicRoom/MR/Resources/MaterialTemplates/MR/*.mat, written by Tools/unity.sh render-setup from MrTemplates),
// which is what keeps the custom shaders and their variants in a player build (ARCHITECTURE.md section 14). In the
// editor before render-setup has run (and in EditMode tests) the shader is found by name and the template's own
// render state is applied, so a missing template never turns a visual into the URP Lit fallback's opaque quads.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.MR
{
    /// <summary>The module's template provider (discovered by the editor's MaterialTemplates.CreateAll).</summary>
    public sealed class MrTemplates : IMaterialTemplateProvider
    {
        public string ResourcesFolder => "Assets/MusicRoom/MR";
        public IEnumerable<MaterialTemplateSpec> Templates => All;

        /// <summary>Queues follow ARCHITECTURE.md section 14: the catcher just before the transparents; the piano's reticle with
        /// them; the hint panel last. (The snowy window's snow, frost and reticle overlay went with it, 2026-11-13.)</summary>
        public static readonly MaterialTemplateSpec[] All =
        {
            new MaterialTemplateSpec(MrMaterials.ShadowCatcher, MrMaterials.CatcherShader, m =>
            {
                m.SetFloat(MrMaterials.OpacityId, MrConstants.CatcherOpacity);
                m.renderQueue = (int)RenderQueue.Transparent - 1;
            }),
            new MaterialTemplateSpec(MrMaterials.Reticle, MrMaterials.UnlitShader, m =>
            {
                m.SetFloat(MrMaterials.ZTestId, (float)CompareFunction.LessEqual);
                m.SetFloat(MrMaterials.CullId, (float)CullMode.Off);   // double-sided
                m.renderQueue = (int)RenderQueue.Transparent;
            }),
            new MaterialTemplateSpec(MrMaterials.Panel, MrMaterials.UnlitShader, m =>
            {
                m.SetFloat(MrMaterials.ZTestId, (float)CompareFunction.LessEqual);
                m.SetFloat(MrMaterials.CullId, (float)CullMode.Back);
                m.renderQueue = (int)RenderQueue.Transparent + 8;
            }),
        };

        public static bool TryFind(string name, out MaterialTemplateSpec spec)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Name == name) { spec = All[i]; return true; }
            spec = default;
            return false;
        }
    }

    public sealed class MrMaterials
    {
        public const string ShadowCatcher = "MR/ShadowCatcher", Reticle = "MR/Reticle", Panel = "MR/Panel";
        public const string CatcherShader = "MusicRoom/MR/ShadowCatcher", UnlitShader = "MusicRoom/MR/Unlit";

        // Catcher / unlit.
        public static readonly int OpacityId = Shader.PropertyToID("_Opacity"), ZTestId = Shader.PropertyToID("_ZTest"),
            CullId = Shader.PropertyToID("_Cull"), BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly MaterialLibrary _lib;
        readonly List<Material> _own = new List<Material>();
        readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary><paramref name="library"/> null: shaders by name only (EditMode tests).</summary>
        public MrMaterials(MaterialLibrary library) { _lib = library; }

        /// <summary>A new material for one of the MR templates, or null when neither the template nor its shader exists
        /// (the caller then leaves that visual out).</summary>
        public Material Create(string template)
        {
            if (_lib != null && _lib.HasTemplate(template)) return _lib.Clone(template);
            if (!MrTemplates.TryFind(template, out var spec)) return null;
            var shader = Shader.Find(spec.ShaderName);
            if (shader == null)
            {
                if (_warned.Add(template)) Debug.LogWarning($"[MR] material template '{template}' and shader '{spec.ShaderName}' missing (run Tools/unity.sh render-setup); that visual is off");
                return null;
            }
            var m = new Material(shader) { name = template + " (shader)" };
            spec.Init?.Invoke(m);
            _own.Add(m);
            return m;
        }

        /// <summary>An unlit colour material (reticle parts, hint panel).</summary>
        public Material Unlit(string template, Color color, float alpha)
        {
            var m = Create(template);
            if (m != null) m.SetColor(BaseColorId, new Color(color.r, color.g, color.b, alpha));
            return m;
        }

        /// <summary>Destroys the materials made here from shaders (library clones die with the MaterialLibrary).</summary>
        public void DestroyOwn()
        {
            foreach (var m in _own) MrUtil.Destroy(m);
            _own.Clear();
        }
    }

    public static class MrUtil
    {
        /// <summary>Destroy that also works in EditMode tests.</summary>
        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Horizontal unit direction (zero-safe: falls back to <paramref name="fallback"/>).</summary>
        public static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-10f ? v.normalized : fallback;
        }
    }
}
