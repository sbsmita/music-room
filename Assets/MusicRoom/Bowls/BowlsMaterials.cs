// The bowls' material templates (bowls.md 5.2; ARCHITECTURE 14: keywords and render state live on templates that
// render-setup writes through IMaterialTemplateProvider, runtime materials are clones; no MaterialPropertyBlock).
// Templates: Bowls/Bronze (URP Lit, metallic 1, smoothness from the albedo's alpha, the hammer normal map) and Bowls/Wood
// (URP Lit, the atlas of walnut grain, the cushion cloth and the puja's suede: the stand, cushions and stick share it).
// render-setup writes them to Bowls/Resources/MaterialTemplates/Bowls/; without the asset (a fresh clone) Create()
// applies the same Init to a clone of the fallback, so the pictures are the templates' pictures. Nothing emits light.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public sealed class BowlsMaterials : IMaterialTemplateProvider
    {
        public const string Bronze = "Bowls/Bronze", Wood = "Bowls/Wood";
        const string Lit = "Universal Render Pipeline/Lit";
        static readonly int SmoothnessChannel = Shader.PropertyToID("_SmoothnessTextureChannel");
        static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        static readonly int SpecularHighlights = Shader.PropertyToID("_SpecularHighlights");
        static readonly int EnvironmentReflections = Shader.PropertyToID("_EnvironmentReflections");

        public string ResourcesFolder => "Assets/MusicRoom/Bowls";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(Bronze, Lit, InitBronze);
                yield return new MaterialTemplateSpec(Wood, Lit, InitWood);
            }
        }

        static void SmoothnessFromAlbedoAlpha(Material m, float scale)
        {
            m.SetFloat(SmoothnessChannel, 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat(MaterialLibrary.Smoothness, scale);
            m.SetFloat(SpecularHighlights, 1f);
            m.SetFloat(EnvironmentReflections, 1f);
            m.DisableKeyword("_EMISSION");
            m.SetColor(MaterialLibrary.EmissionColor, Color.black);
        }

        public static void InitBronze(Material m)
        {
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.SetFloat(MaterialLibrary.Metallic, 1f);
            SmoothnessFromAlbedoAlpha(m, 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat(BumpScale, 1f);
        }

        public static void InitWood(Material m)
        {
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.SetFloat(MaterialLibrary.Metallic, 0f);
            SmoothnessFromAlbedoAlpha(m, 1f);
        }

        /// <summary>The two runtime materials (clones of the templates) with the generated textures.</summary>
        public static void Create(MaterialLibrary lib, out Material bronze, out Material wood, out Texture2D[] textures)
        {
            textures = new[] { BowlsTextures.Bronze(), BowlsTextures.BronzeNormal(), BowlsTextures.Wood() };
            bronze = Make(lib, Bronze, InitBronze); bronze.SetTexture(MaterialLibrary.BaseMap, textures[0]); bronze.SetTexture(MaterialLibrary.BumpMap, textures[1]);
            wood = Make(lib, Wood, InitWood); wood.SetTexture(MaterialLibrary.BaseMap, textures[2]);
        }

        static Material Make(MaterialLibrary lib, string name, System.Action<Material> init)
        {
            bool have = lib != null && lib.HasTemplate(name);
            Material m;
            if (lib != null) m = lib.Clone(have ? name : MaterialLibrary.LitOpaque);
            else m = new Material(Shader.Find(Lit));
            m.name = name;
            if (!have) init(m);   // no template asset (a fresh clone before render-setup): the same Init the template gets
            return m;
        }
    }
}
