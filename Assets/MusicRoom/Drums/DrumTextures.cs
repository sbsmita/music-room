// The kit's one module texture: the cymbal's lathe grooves packed for the Drums/Cymbal template (albedo rgb, smoothness
// in alpha), so the plate needs one texture and one sampler on Quest. The pixels come from Core's ProcTex.CymbalPixel
// (the cymbal groove texture, fingerprinted by ProcTexTests); only the packing is ours: alpha = 255 - roughness.
// The drum head and the contact shadow's soft dot are Core's shared textures as they are (SharedTextures.DrumHead,
// SharedTextures.SoftDot); ARCHITECTURE.md section 14: one shared copy of each texture.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    public static class DrumTextures
    {
        /// <summary>Bake name (section 2 rule 4: "<Module>/<name>"): Generated/Resources/Generated/Drums/cymbal.png.</summary>
        public const string CymbalName = "Drums/cymbal";
        public const int Size = 256;

        /// <summary>Canvas row y goes to Texture2D row Size-1-y (ARCHITECTURE.md section 5), as ProcTex does, so the lathe's
        /// v runs over the canvas rows top-down (flipY).</summary>
        public static Texture2D Cymbal()
        {
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                int row = (Size - 1 - y) * Size;
                for (int x = 0; x < Size; x++)
                {
                    ProcTex.CymbalPixel(x, y, out Color32 c, out byte rough);
                    c.a = (byte)(255 - rough);
                    px[row + x] = c;
                }
            }
            return ProcTex.Make(Size, Size, px, false, true, "cymbal");
        }

        /// <summary>The baked texture (Tools/unity.sh bake), else generated now and cached by GeneratedAssets.</summary>
        public static Texture2D LoadCymbal() => GeneratedAssets.Texture(CymbalName, Cymbal);
    }

    /// <summary>Registers the packed cymbal map with TextureBaker (discovered by reflection; Tools/unity.sh bake).</summary>
    public sealed class DrumsTextureProvider : ITextureBakeProvider
    {
        public IEnumerable<TextureBakeSpec> Textures
        {
            get
            {
                yield return new TextureBakeSpec { Name = DrumTextures.CymbalName, Generate = DrumTextures.Cymbal, Mipmaps = true, Repeat = true, HasAlpha = true };
            }
        }
    }

    /// <summary>The module's material template (MaterialTemplates.CreateAll writes
    /// Assets/MusicRoom/Drums/Resources/MaterialTemplates/Drums/Cymbal.mat). URP Lit with smoothness from the albedo
    /// alpha (a different keyword set from the foundation's LitOpaque / LitMetal, hence its own template).</summary>
    public sealed class DrumsMaterialTemplates : IMaterialTemplateProvider
    {
        public const string Cymbal = "Drums/Cymbal";
        public string ResourcesFolder => "Assets/MusicRoom/Drums";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(Cymbal, "Universal Render Pipeline/Lit", m =>
                {
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                    m.SetFloat(MaterialLibrary.Metallic, DrumSpec.CymbalMetallic);
                    m.SetFloat(MaterialLibrary.Smoothness, 1f);          // scale on the map's alpha (roughness 1 x map)
                    m.SetFloat("_SmoothnessTextureChannel", 1f);         // 1 = albedo alpha
                    m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                });
            }
        }
    }
}
