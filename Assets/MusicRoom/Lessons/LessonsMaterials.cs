// The Lessons module's material templates (ARCHITECTURE.md section 14). render-setup writes
// Lessons/Resources/MaterialTemplates/Lessons/Block.mat from this provider, so the block shader's instancing variant is
// referenced by an asset and ships in the build; runtime code only clones it. Discovered by reflection (no Editor edit).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed class LessonsMaterials : IMaterialTemplateProvider
    {
        /// <summary>MaterialLibrary template name (Resources path below "MaterialTemplates/").</summary>
        public const string Block = "Lessons/Block";
        public const string BlockShader = "MusicRoom/LessonBlock";
        /// <summary>After the room's transparents (glass 3000, snow 3002, frost 3004): render order 4, drawn last.</summary>
        public const int BlockQueue = 3005;

        public string ResourcesFolder => "Assets/MusicRoom/Lessons";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(Block, BlockShader, m =>
                {
                    // Render state lives on the template: instancing for RenderMeshInstanced, the queue after the room.
                    m.enableInstancing = true;
                    m.renderQueue = BlockQueue;
                    m.SetColor("_Color", new Color(0.0976f, 0.7157f, 1f, 0.6f));
                });
            }
        }
    }
}
