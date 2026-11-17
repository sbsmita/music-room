// The piano's textures and its editor registrations (ARCHITECTURE.md sections 2 and 14):
//   - key shade palette (runtime, 2 x 2 linear): URP Lit ignores vertex colours, so the per-vertex side shading and
//     per-key darkening (material x vertex grey x instance tint, all linear) ride in uv instead: u encodes the side grey
//     c in [0.3, 1], v the tint t in [0.7, 1], and the texel values are c * t. A bilinear function is reproduced exactly
//     by bilinear filtering between texel centres, so 2 x 2 texels give exactly c * t at every fragment.
//   - panel palette (runtime, 4 x 1 sRGB, point): the four vertex greys of the top panel.
//   - the grille and the stand wood are Core's shared set (SharedTextures Core/grille, Core/wood_stand: ProcTex, baked by
//     CoreTextureProvider), so the piano bakes nothing of its own (ARCHITECTURE 15.1).
// Templates: "Piano/Label" (URP Particles/Lit: lit, vertex-colour tinted, alpha-tested at .02, alpha-blended, no depth write,
// queue 2990 so the names are painted before every other transparent), created by Tools/unity.sh render-setup.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Piano
{
    public static class PianoTextures
    {
        public const float ShadeMin = 0.3f, TintMin = 0.7f;   // clamp of shadeKeySides; 1 - DARKEN black at full depth

        /// <summary>uv for a side grey c (0.3..1) and a key tint t (0.7..1) on the 2 x 2 shade palette (texel centres .25/.75).</summary>
        public static Vector2 ShadeUv(float c, float t)
        {
            float u = Mathf.Clamp01((c - ShadeMin) / (1f - ShadeMin));
            float v = Mathf.Clamp01((t - TintMin) / (1f - TintMin));
            return new Vector2(0.25f + 0.5f * u, 0.25f + 0.5f * v);
        }

        /// <summary>The 2 x 2 linear palette: texel (i, j) holds c_i * t_j for c in {0.3, 1}, t in {0.7, 1}.</summary>
        public static Texture2D ShadePalette()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "piano_keyShade", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0 };
            var px = new Color32[4];
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    float c = i == 0 ? ShadeMin : 1f, t = j == 0 ? TintMin : 1f;
                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(c * t) * 255f);
                    px[j * 2 + i] = new Color32(g, g, g, 255);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);   // 16 bytes: kept readable (tests sample it)
            return tex;
        }

        public static Texture2D PanelPalette()
        {
            var tex = new Texture2D(PianoBody.PanelColours.Length, 1, TextureFormat.RGBA32, false, false)
            { name = "piano_panel", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            tex.SetPixels32(PianoBody.PanelColours);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>shadeKeySides: sides fall to 45 % at the bottom, the front face a little, the underside
        /// to 40 %, so the gap beside a key that goes down reads dark. y range is the key body's own.</summary>
        public static float SideShade(float y, float ymin, float ymax, Vector3 n)
        {
            float t = (y - ymin) / (ymax - ymin);
            float side = Mathf.Abs(n.x), front = Mathf.Max(0f, -n.z), down = Mathf.Max(0f, -n.y);
            float c = 1f;
            c += (0.45f + 0.55f * t - 1f) * side;
            c += (0.82f + 0.18f * t - 1f) * front * (1f - side);
            c += (0.4f - 1f) * down;
            return Mathf.Clamp(c, 0.3f, 1f);
        }
    }

    /// <summary>Editor registry (discovered by reflection in this runtime assembly): the label template. The piano bakes no
    /// texture of its own (grille and stand wood are Core's; the label atlas is drawn by TextMeshPro at runtime).</summary>
    public sealed class PianoAssets : IMaterialTemplateProvider
    {
        public const string LabelTemplate = "Piano/Label";
        public const int LabelQueue = 2990;

        public string ResourcesFolder => "Assets/MusicRoom/Piano";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(LabelTemplate, "Universal Render Pipeline/Particles/Lit", ConfigureLabel);
            }
        }

        /// <summary>The label material: lit, textured, vertex colours, transparent, alpha test .02, no depth write,
        /// roughness .8, render order -1. Alpha blends over the key and leaves the eye buffer opaque for passthrough.</summary>
        public static void ConfigureLabel(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.02f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetFloat("_ColorMode", 0f);                     // multiply: albedo x vertex colour (the key's tint)
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Metallic", 0f);
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = LabelQueue;
        }
    }
}
