// The menu's material templates (ARCHITECTURE.md section 14): written by Tools/unity.sh render-setup into
// Assets/MusicRoom/Menu/Resources/MaterialTemplates/Menu/*.mat (inside this folder, so they travel with the merge) and
// cloned at runtime. The templates are also what keeps the two custom shaders in a player build.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public sealed class MenuMaterials : IMaterialTemplateProvider
    {
        public const string FaceShader = "MusicRoom/MenuFace", ShapeShader = "MusicRoom/MenuShape";
        /// <summary>The face draws just before the 3000 transparents (glass, snow, frost, hover glows) and writes depth;
        /// the back plate one step earlier. Glows added on the face then always land on top of it.</summary>
        public const int FaceQueue = 2999, BackQueue = 2998;
        public const float AlphaCutoff = 0.02f;   // alpha test: texels below this are clipped

        public string ResourcesFolder => "Assets/MusicRoom/Menu";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(MenuView.FaceTemplate, FaceShader, m => { m.renderQueue = FaceQueue; m.SetFloat("_Cutoff", AlphaCutoff); m.SetColor("_BaseColor", Color.white); });
                yield return new MaterialTemplateSpec(MenuView.BackTemplate, FaceShader, m => { m.renderQueue = BackQueue; m.SetFloat("_Cutoff", AlphaCutoff); m.SetColor("_BaseColor", Color.white); });
                yield return new MaterialTemplateSpec(MenuView.ShapeTemplate, ShapeShader, m => { m.renderQueue = 3000; });
            }
        }
    }
}
