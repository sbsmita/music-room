// The ghost hands' material template (ARCHITECTURE.md section 14): render-setup writes
// Lessons/Resources/MaterialTemplates/Lessons/GhostHands.mat from this provider, so the shader ships in the build; the
// runtime only clones it. Discovered by reflection (no Editor edit).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons.Ghost
{
    public sealed class GhostHandsMaterials : IMaterialTemplateProvider
    {
        public const string Template = "Lessons/GhostHands";
        public const string Shader = "MusicRoom/GhostHands";
        /// <summary>After the room's transparents and the motes, before the lesson blocks.</summary>
        public const int Queue = 3002;

        public string ResourcesFolder => "Assets/MusicRoom/Lessons";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(Template, Shader, m =>
                {
                    m.enableInstancing = true;
                    m.renderQueue = Queue;
                    m.SetColor("_Color", new Color(0.56f, 0.76f, 1f, 1f));
                    m.SetColor("_RimColor", new Color(0.86f, 0.95f, 1f, 1f));
                    m.SetFloat("_RimPower", 2.2f);
                    m.SetFloat("_MaxOpacity", 0.5f);
                });
            }
        }
    }
}
