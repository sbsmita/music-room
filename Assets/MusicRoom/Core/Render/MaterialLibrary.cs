// Runtime materials are clones of template .mat assets under Resources/MaterialTemplates (spec 09 section 7.3):
// the templates keep their shader variants in the build. Keywords and render state come from the template; only
// colours, textures and floats are set on clones. URP Lit property names: _BaseColor, _BaseMap, _EmissionColor,
// _Smoothness, _Metallic, _BumpMap, _Cutoff; URP Unlit: _BaseColor, _BaseMap.
// Every clone is tracked for DestroyAll. An owner that makes and drops clones during the session (the snowy windows in
// mixed reality) gives them back with Destroy(material), and clones destroyed some other way are pruned from the list as
// it grows, so the list never keeps a dead material for every clone the session ever made.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class MaterialLibrary
    {
        public const string ResourcesPrefix = "MaterialTemplates/";

        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        public static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        public static readonly int Metallic = Shader.PropertyToID("_Metallic");
        public static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        public static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        public static readonly int Alpha = Shader.PropertyToID("_Alpha");
        public static readonly int LegacyColor = Shader.PropertyToID("_Color");

        // Foundation template names (Assets/MusicRoom/Resources/MaterialTemplates/*.mat, made by MaterialTemplates.CreateAll).
        public const string LitOpaque = "LitOpaque", LitEmissive = "LitEmissive", LitTransparent = "LitTransparent",
            LitAlphaTest = "LitAlphaTest", LitMetal = "LitMetal", Unlit = "Unlit", UnlitTransparent = "UnlitTransparent",
            UnlitAdditive = "UnlitAdditive", ParticlesAdditive = "ParticlesUnlitAdditive", ParticlesAlpha = "ParticlesUnlitAlpha",
            HandGhost = "HandGhost";

        readonly Dictionary<string, Material> _templates = new Dictionary<string, Material>();
        readonly List<Material> _clones = new List<Material>();
        int _pruneAt = PruneMin;
        /// <summary>The clone list is pruned of destroyed materials once it reaches this many, then at twice what was left.</summary>
        public const int PruneMin = 256;
        /// <summary>Clones tracked now (tests, diagnostics).</summary>
        public int CloneCount => _clones.Count;
        readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>The template asset (never modify it). Missing templates log once and fall back to a URP Lit/Unlit material.</summary>
        public Material Template(string name)
        {
            if (_templates.TryGetValue(name, out var m) && m != null) return m;
            m = Resources.Load<Material>(ResourcesPrefix + name);
            if (m == null)
            {
                if (_warned.Add(name)) Debug.LogWarning($"[Materials] template '{name}' missing (run Tools/unity.sh render-setup); using a fallback");
                bool unlit = name.Contains("Unlit") || name.Contains("Particles");
                var sh = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader");
                m = new Material(sh) { name = name + " (fallback)" };
                _clones.Add(m);
            }
            _templates[name] = m;
            return m;
        }

        public bool HasTemplate(string name) => Resources.Load<Material>(ResourcesPrefix + name) != null;

        /// <summary>A fresh clone of a template, tracked for DestroyAll.</summary>
        public Material Clone(string template)
        {
            var m = new Material(Template(template)) { name = template + " (clone)" };
            if (_clones.Count >= _pruneAt) Prune();
            _clones.Add(m);
            return m;
        }

        /// <summary>Destroys a clone this library made and stops tracking it (an owner that drops its materials mid-session).</summary>
        public void Destroy(Material m)
        {
            if (m == null) return;
            int i = _clones.IndexOf(m);
            if (i >= 0)
            {
                int last = _clones.Count - 1;
                _clones[i] = _clones[last];
                _clones.RemoveAt(last);
            }
            Object.Destroy(m);
        }

        /// <summary>Drops destroyed clones from the list (Unity's null check).</summary>
        void Prune()
        {
            int w = 0;
            for (int r = 0; r < _clones.Count; r++) if (_clones[r] != null) _clones[w++] = _clones[r];
            _clones.RemoveRange(w, _clones.Count - w);
            _pruneAt = System.Math.Max(PruneMin, 2 * _clones.Count);
        }

        public Material Lit(Color color, float smoothness = 0.5f, float metallic = 0f, Texture map = null)
        {
            var m = Clone(LitOpaque);
            m.SetColor(BaseColor, color);
            m.SetFloat(Smoothness, smoothness);
            m.SetFloat(Metallic, metallic);
            if (map != null) m.SetTexture(BaseMap, map);
            return m;
        }

        public Material LitEmissiveMaterial(Color color, Color emission, float smoothness = 0.5f)
        {
            var m = Clone(LitEmissive);
            m.SetColor(BaseColor, color);
            m.SetColor(EmissionColor, emission);
            m.SetFloat(Smoothness, smoothness);
            return m;
        }

        public Material UnlitMaterial(Color color, Texture map = null)
        {
            var m = Clone(Unlit);
            m.SetColor(BaseColor, color);
            if (map != null) m.SetTexture(BaseMap, map);
            return m;
        }

        public Material Additive(Texture map, Color color)
        {
            var m = Clone(UnlitAdditive);
            m.SetColor(BaseColor, color);
            if (map != null) m.SetTexture(BaseMap, map);
            return m;
        }

        public void DestroyAll()
        {
            foreach (var m in _clones) if (m != null) Object.Destroy(m);
            _clones.Clear();
            _templates.Clear();
        }
    }
}
