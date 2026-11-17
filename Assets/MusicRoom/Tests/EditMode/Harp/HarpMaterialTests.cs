// The harp's render contract (ARCHITECTURE.md sections 2.4, 14 and 15.3): its one material template is "Harp/Strings"
// on the module's own vertex-colour shader (written by Tools/unity.sh render-setup inside the module folder), the shader
// is SRP-Batcher shaped and single-pass-instanced ready, and the module bakes no textures of its own (the woods and the
// soft dot are Core's shared set).
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Harp
{
    public class HarpMaterialTests
    {
        [Test]
        public void TheStringsTemplateIsTheModulesOwnVertexColourShader()
        {
            var provider = new HarpMaterials();
            Assert.That(provider.ResourcesFolder, Is.EqualTo("Assets/MusicRoom/Harp"));
            MaterialTemplateSpec[] specs = provider.Templates.ToArray();
            Assert.That(specs.Length, Is.EqualTo(1));
            Assert.That(specs[0].Name, Is.EqualTo("Harp/Strings"), "<Module>/<Name> (rule 2.4)");
            Assert.That(specs[0].ShaderName, Is.EqualTo("MusicRoom/HarpStrings"));

            Shader shader = Shader.Find(HarpMaterials.StringsShader);
            Assert.That(shader, Is.Not.Null, "Assets/MusicRoom/Harp/Shaders/HarpStrings.shader compiles and is found");
            var mat = Resources.Load<Material>("MaterialTemplates/" + HarpMaterials.StringsTemplate);
            Assert.That(mat, Is.Not.Null, "render-setup wrote Harp/Resources/MaterialTemplates/Harp/Strings.mat");
            Assert.That(mat.shader, Is.SameAs(shader));
            Assert.That(mat.GetFloat(HarpMaterials.GlowGain), Is.EqualTo(1f));
            Assert.That(mat.GetColor(MaterialLibrary.BaseColor), Is.EqualTo(Color.white), "the colours are vertex data");
        }

        [Test]
        public void TheStringsShaderIsSrpBatcherShapedAndStereoReady()
        {
            string src = File.ReadAllText("Assets/MusicRoom/Harp/Shaders/HarpStrings.shader");
            StringAssert.Contains("CBUFFER_START(UnityPerMaterial)", src, "every material property in UnityPerMaterial");
            StringAssert.Contains("\"LightMode\" = \"UniversalForward\"", src);
            StringAssert.Contains("UNITY_VERTEX_INPUT_INSTANCE_ID", src);
            StringAssert.Contains("UNITY_VERTEX_OUTPUT_STEREO", src);
            StringAssert.Contains("UNITY_SETUP_INSTANCE_ID", src);
            StringAssert.Contains("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO", src);
            StringAssert.Contains("#pragma multi_compile_instancing", src);
            StringAssert.Contains("TEXCOORD1", src, "the glow rides in uv1");
        }

        [Test]
        public void TheStringCoresWriteDepthAndTheBlurEdgesDoNot()
        {
            // E8 (experience.md 10): "ZWrite Off for the edge copies, core first". A depth-only pass before the colour pass
            // writes the cores' depth (COLOR.a = 1), so fingers behind a string stay hidden in VR and the MR hand cut-out
            // (queue 3999) does not punch through a string in front of the real hand.
            Shader shader = Shader.Find(HarpMaterials.StringsShader);
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.passCount, Is.EqualTo(2), "a depth pass and the colour pass");
            var mat = new Material(shader);
            try
            {
                int depth = mat.FindPass("StringsDepth"), colour = mat.FindPass("ForwardStrings");
                Assert.That(depth, Is.EqualTo(0), "the depth pass comes first");
                Assert.That(colour, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(mat); }
            string src = File.ReadAllText("Assets/MusicRoom/Harp/Shaders/HarpStrings.shader");
            int dp = src.IndexOf("Name \"StringsDepth\"", System.StringComparison.Ordinal), fp = src.IndexOf("Name \"ForwardStrings\"", System.StringComparison.Ordinal);
            Assert.That(dp, Is.GreaterThan(0)); Assert.That(fp, Is.GreaterThan(dp));
            string depthPass = src.Substring(dp, fp - dp), forward = src.Substring(fp);
            StringAssert.Contains("\"LightMode\" = \"SRPDefaultUnlit\"", depthPass);
            StringAssert.Contains("ZWrite On", depthPass);
            StringAssert.Contains("ColorMask 0", depthPass);
            StringAssert.Contains("clip(i.core - 0.5h)", depthPass, "only the cores (COLOR.a 1)");
            StringAssert.Contains("ZWrite Off", forward, "the colour pass blends without writing depth");
            StringAssert.Contains("ZTest LEqual", forward);
        }

        [Test]
        public void TheHarpBakesNoTexturesOfItsOwn()
        {
            var bakers = typeof(HarpModule).Assembly.GetTypes().Where(t => typeof(ITextureBakeProvider).IsAssignableFrom(t)).ToArray();
            Assert.That(bakers, Is.Empty, "woods and the soft dot come from Core's SharedTextures (15.3)");
            Assert.That(HarpInstrument.SharedWood(HarpWood.Maple), Is.EqualTo(SharedTextures.WoodMaple));
            Assert.That(HarpInstrument.SharedWood(HarpWood.Spruce), Is.EqualTo(SharedTextures.WoodSpruce));
            Assert.That(HarpInstrument.SharedWood(HarpWood.Walnut), Is.EqualTo(SharedTextures.WoodWalnut));
            Assert.That(HarpInstrument.Repeat(HarpWood.Maple), Is.EqualTo(2f), "harpMaple repeat");
            Assert.That(HarpInstrument.Repeat(HarpWood.Spruce), Is.EqualTo(4f));
            Assert.That(HarpInstrument.Repeat(HarpWood.Walnut), Is.EqualTo(2f));
        }
    }
}
