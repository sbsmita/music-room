// Editor-time registries that modules fill from their RUNTIME assembly (so the Editor folder never changes):
// material templates (one .mat per shader + keyword set, under the module's Resources) and baked textures.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public struct MaterialTemplateSpec
    {
        public string Name;                 // Resources path below "MaterialTemplates/", "<Module>/<Name>", e.g. "Room/Flame"; unique across providers (CreateAll fails on a duplicate)
        public string ShaderName;           // e.g. "Universal Render Pipeline/Lit" or "MusicRoom/Snow"
        public Action<Material> Init;       // keywords, render state, defaults (set keywords HERE, never at runtime)
        public MaterialTemplateSpec(string name, string shaderName, Action<Material> init) { Name = name; ShaderName = shaderName; Init = init; }
    }

    /// <summary>Implement in a module (plain class, parameterless ctor). MaterialTemplates.CreateAll writes
    /// <ResourcesFolder>/Resources/MaterialTemplates/<Name>.mat; MaterialLibrary.Template(Name) loads it.</summary>
    public interface IMaterialTemplateProvider
    {
        /// <summary>Module folder, e.g. "Assets/MusicRoom/Room".</summary>
        string ResourcesFolder { get; }
        IEnumerable<MaterialTemplateSpec> Templates { get; }
    }

    public struct TextureBakeSpec
    {
        public string Name;                 // "<Module>/<name>" (e.g. "Room/plaster", shared ones "Core/wood_walnut"): file Generated/<Name>.png; unique across providers (BakeAll fails on a duplicate)
        public Func<Texture2D> Generate;    // runtime generator (also the fallback); CPU only (bake runs -nographics)
        /// <summary>The generator needs a GPU (camera / RenderTexture readback). The bake skips it with [BAKE-ERROR] under
        /// -nographics (run NOGFX=0 Tools/unity.sh bake), never writing a blank PNG. Prefer CPU generators.</summary>
        public bool NeedsGpu;
        public bool Linear;                 // data textures (roughness masks, normals): no sRGB
        public bool Mipmaps;
        public bool Repeat;
        public bool NormalMap;
        public bool HasAlpha;
        public bool AlphaIsTransparency;
        /// <summary>Anisotropic filtering level for the importer (0 = the importer's default). Floors, walls and rugs seen at
        /// grazing angles: they are sampled with anisotropy 4 (the night sky 8).</summary>
        public int AnisoLevel;
    }

    public interface ITextureBakeProvider
    {
        IEnumerable<TextureBakeSpec> Textures { get; }
    }
}
