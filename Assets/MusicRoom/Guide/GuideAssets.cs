// The Guide module's registered assets (ARCHITECTURE.md section 14): material templates written by
// Tools/unity.sh render-setup into Guide/Resources/MaterialTemplates/Guide/*.mat (so they travel with this folder), and
// the face / bubble textures baked by Tools/unity.sh bake. Both providers are found by reflection; nothing else changes.
// Render queues keep Sunny's draw order (corona 4, body 5, bubble 6, demo 7) after the room's glass/snow/frost.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Guide
{
    public sealed class GuideMaterialTemplates : IMaterialTemplateProvider
    {
        public const string Body = "Guide/Body", Corona = "Guide/Corona", Bubble = "Guide/Bubble", BubbleText = "Guide/BubbleText",
            Demo = "Guide/Demo", DemoDouble = "Guide/DemoDouble";
        public const string UnlitShader = "MusicRoom/SunnyUnlit", CoronaShader = "MusicRoom/SunnyCorona",
            TextShader = "TextMeshPro/Mobile/Distance Field Overlay";
        public const int QueueCorona = 3010, QueueBody = 3011, QueueBubble = 3012, QueueText = 3013, QueueDemo = 3014;

        public static readonly int AdditiveId = Shader.PropertyToID("_Additive");
        public static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend"), DstBlendId = Shader.PropertyToID("_DstBlend");
        public static readonly int SrcBlendAId = Shader.PropertyToID("_SrcBlendA"), DstBlendAId = Shader.PropertyToID("_DstBlendA");
        public static readonly int ZWriteId = Shader.PropertyToID("_ZWrite"), ZTestId = Shader.PropertyToID("_ZTest"), CullId = Shader.PropertyToID("_Cull");
        public static readonly int GlowId = Shader.PropertyToID("_Glow"), TimeId = Shader.PropertyToID("_SunnyTime");

        public string ResourcesFolder => "Assets/MusicRoom/Guide";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                // The face sphere: unlit and transparent (it fades out when Sunny leaves), depth written.
                yield return new MaterialTemplateSpec(Body, UnlitShader, m => Unlit(m, additive: false, zWrite: true, CompareFunction.LessEqual, CullMode.Back, QueueBody));
                // The corona: SrcAlpha One additive of a premultiplied output, double-sided, no depth write.
                yield return new MaterialTemplateSpec(Corona, CoronaShader, m =>
                {
                    m.SetFloat(GlowId, 1f);
                    m.SetFloat(TimeId, 0f);
                    m.renderQueue = QueueCorona;
                });
                // The bubble: drawn over everything (no depth test) so an instrument never cuts a word off.
                yield return new MaterialTemplateSpec(Bubble, UnlitShader, m => Unlit(m, additive: false, zWrite: false, CompareFunction.Always, CullMode.Back, QueueBubble));
                // The caption: TextMeshPro's overlay SDF shader (ZTest Always); the font atlas is copied in at runtime.
                yield return new MaterialTemplateSpec(BubbleText, TextShader, m => { m.renderQueue = QueueText; });
                // The ghost hand: additive soft cyan; closed shapes cull their back faces (front side only).
                yield return new MaterialTemplateSpec(Demo, UnlitShader, m => Unlit(m, additive: true, zWrite: false, CompareFunction.LessEqual, CullMode.Back, QueueDemo));
                // Ring, row plate and lines: double-sided.
                yield return new MaterialTemplateSpec(DemoDouble, UnlitShader, m => Unlit(m, additive: true, zWrite: false, CompareFunction.LessEqual, CullMode.Off, QueueDemo));
            }
        }

        static void Unlit(Material m, bool additive, bool zWrite, CompareFunction zTest, CullMode cull, int queue)
        {
            m.SetFloat(AdditiveId, additive ? 1f : 0f);
            // Alpha blend = normal blending (colour SrcAlpha/OneMinusSrcAlpha, alpha One/OneMinusSrcAlpha). Additive =
            // One One: the shader outputs the sRGB-space addition already converted (see SunnyUnlit.shader).
            m.SetFloat(SrcBlendId, (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat(DstBlendId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat(SrcBlendAId, (float)BlendMode.One);
            m.SetFloat(DstBlendAId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat(ZWriteId, zWrite ? 1f : 0f);
            m.SetFloat(ZTestId, (float)zTest);
            m.SetFloat(CullId, (float)cull);
            m.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.renderQueue = queue;
        }
    }

    /// <summary>The 8 face states and the bubble, baked to PNG so the headset does not paint them at start-up.</summary>
    public sealed class GuideTextures : ITextureBakeProvider
    {
        public IEnumerable<TextureBakeSpec> Textures
        {
            get
            {
                for (int i = 0; i < SunnyFace.StateCount; i++)
                {
                    int state = i;
                    yield return new TextureBakeSpec { Name = SunnyFace.Name(state), Generate = () => SunnyFace.Create(state, true), Mipmaps = true };
                }
                yield return new TextureBakeSpec
                {
                    Name = SunnyBubbleArt.TextureName, Generate = () => SunnyBubbleArt.Create(true), Mipmaps = true, HasAlpha = true, AlphaIsTransparency = true,
                };
            }
        }

        /// <summary>The baked texture, else painted now (session-cached by GeneratedAssets).</summary>
        public static Texture2D Face(int state) => GeneratedAssets.Texture(SunnyFace.Name(state), () => SunnyFace.Create(state, false));

        public static Texture2D Bubble() => GeneratedAssets.Texture(SunnyBubbleArt.TextureName, () => SunnyBubbleArt.Create(false));
    }
}
