// The harp's own material template (ARCHITECTURE.md section 14: keywords and render state live on templates that
// render-setup writes through IMaterialTemplateProvider; runtime materials are clones). The 22 strings are ONE mesh in
// the custom shader MusicRoom/HarpStrings (Shaders/HarpStrings.shader): base colour per vertex, glow in TEXCOORD1, so
// a pluck flash or a lesson target never needs a MaterialPropertyBlock and the strings stay one SRP-Batcher draw
// (section 15.3). Written to Assets/MusicRoom/Harp/Resources/MaterialTemplates/Harp/Strings.mat by render-setup.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Harp
{
    public sealed class HarpMaterials : IMaterialTemplateProvider
    {
        public const string StringsTemplate = "Harp/Strings";
        public const string StringsShader = "MusicRoom/HarpStrings";
        public static readonly int GlowGain = Shader.PropertyToID("_GlowGain");

        public string ResourcesFolder => "Assets/MusicRoom/Harp";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(StringsTemplate, StringsShader, InitStrings);
            }
        }

        /// <summary>White tint (the vertex colours carry cream / red / blue) and the glow at full strength. E8: the
        /// strings are alpha-blended (the blur's edges), so the template sits in the transparent queue.</summary>
        public static void InitStrings(Material m)
        {
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.SetFloat(GlowGain, 1f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
