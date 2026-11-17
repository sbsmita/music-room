// The wall paint's material template (Decor/WallPaint.cs, Shaders/WallPaint.shader), written by `Tools/unity.sh
// render-setup` into Room/Resources/MaterialTemplates/Room/WallPaint.mat so the shader is in the build. Kept apart from
// RoomTemplates so the paint's files stay its own. The VR blend (lit paint, alpha kept) is the template's; WallPaint switches its MR clone to
// the alpha wash at runtime (render-state floats only, no keywords).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Render
{
    public sealed class PaintTemplates : IMaterialTemplateProvider
    {
        public const string WallPaint = "Room/WallPaint", Shader = "MusicRoom/WallPaint";
        /// <summary>After the opaque room and its floor decals (2950), before the glass, the snow and the butterflies.</summary>
        public const int Queue = (int)RenderQueue.Transparent - 40;

        public string ResourcesFolder => "Assets/MusicRoom/Room";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(WallPaint, Shader, m =>
                {
                    SetStain(m);
                    m.renderQueue = Queue;
                });
            }
        }

        public static readonly int SrcBlend = UnityEngine.Shader.PropertyToID("_SrcBlend"), DstBlend = UnityEngine.Shader.PropertyToID("_DstBlend"),
            SrcBlendA = UnityEngine.Shader.PropertyToID("_SrcBlendA"), DstBlendA = UnityEngine.Shader.PropertyToID("_DstBlendA"),
            Wash = UnityEngine.Shader.PropertyToID("_Wash");

        /// <summary>The VR room: lit paint blended over the wall (the alpha channel left as it was).</summary>
        public static void SetStain(Material m)
        {
            m.SetFloat(SrcBlend, (float)BlendMode.SrcAlpha);
            m.SetFloat(DstBlend, (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat(SrcBlendA, (float)BlendMode.Zero);
            m.SetFloat(DstBlendA, (float)BlendMode.One);
            m.SetFloat(Wash, 0f);
        }

        /// <summary>Mixed reality: a translucent wash over the passthrough, alpha accumulated as over.</summary>
        public static void SetWash(Material m)
        {
            m.SetFloat(SrcBlend, (float)BlendMode.SrcAlpha);
            m.SetFloat(DstBlend, (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat(SrcBlendA, (float)BlendMode.One);
            m.SetFloat(DstBlendA, (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat(Wash, 1f);
        }
    }
}
