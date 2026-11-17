using System.Linq;
using MusicRoom.Core;
using MusicRoom.EditorTools;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusicRoom.Tests.EditorTools
{
    public class SceneBuilderTests
    {
        [Test]
        public void PopulateBuildsTheConfiguredRigAndAppRoot()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = SceneBuilder.Populate(scene);
            try
            {
                var roots = scene.GetRootGameObjects();
                var rigGo = roots.FirstOrDefault(g => g.name == RigBootstrap.RigName);
                Assert.That(rigGo, Is.Not.Null, "PlayerRig");
                var mgr = rigGo.GetComponent<OVRManager>();
                Assert.That(mgr, Is.Not.Null);
                Assert.That(mgr.trackingOriginType, Is.EqualTo(OVRManager.TrackingOrigin.FloorLevel));
                // burst.md section 10.1: passthrough starts only on entering "Play in my room" (CPU level 4 in the VR room);
                // the underlay layer is added at runtime then (XrMode.StartPassthrough / RigBootstrap.EnsurePassthroughLayer).
                Assert.IsFalse(mgr.isInsightPassthroughEnabled, "passthrough is not initialised at start");
                Assert.That(rigGo.GetComponentInChildren<OVRPassthroughLayer>(true), Is.Null, "no passthrough layer in the scene");
                Assert.IsTrue(mgr.enableDynamicResolution);
                Assert.That(mgr.quest3MinDynamicResolutionScale, Is.EqualTo(RigBootstrap.DynamicResolutionMin));
                Assert.That(mgr.quest3MaxDynamicResolutionScale, Is.EqualTo(RigBootstrap.DynamicResolutionMax));
                var rig = rigGo.GetComponent<OVRCameraRig>();
                var cam = rig.centerEyeAnchor.GetComponent<Camera>();
                Assert.That(cam.nearClipPlane, Is.EqualTo(0.02f).Within(1e-6f));
                Assert.That(cam.farClipPlane, Is.EqualTo(40f).Within(1e-6f));
                Assert.That(cam.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(cam.backgroundColor.a, Is.LessThan(1f), "Meta's Required passthrough rule");
                Assert.IsFalse(cam.allowHDR);
                Assert.That(cam.cullingMask & Layers.HeadCameraExcludeMask, Is.EqualTo(0), "the head camera skips Hidden / PanelUI");
                Assert.That(rig.centerEyeAnchor.GetComponent<AudioListener>(), Is.Not.Null);
                Assert.That(rigGo.GetComponent<DesktopRigFallback>(), Is.Not.Null);
                Assert.That(app.name, Is.EqualTo(AppRoot.RootName));
                Assert.That(app.GetComponent<AppRoot>(), Is.Not.Null);
                Assert.That(app.GetComponent<HandInput>(), Is.Not.Null);
                Assert.That(roots.Length, Is.EqualTo(2), "only the rig and the App root");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void BakeSpecsAreUniqueModulePrefixedAndIncludeTheSharedSet()
        {
            var specs = TextureBaker.CollectSpecs();   // throws on a duplicate name across providers
            var names = specs.Select(s => s.spec.Name).ToList();
            Assert.That(names, Is.Unique);
            foreach (var n in names) Assert.That(n.IndexOf('/'), Is.GreaterThan(0), n + " is '<Module>/<name>'");
            Assert.That(names, Does.Contain(SharedTextures.WoodOak).And.Contain(SharedTextures.SoftDot));
            Assert.That(TextureBaker.PathOf("Core/x"), Is.EqualTo("Assets/MusicRoom/Generated/Resources/Generated/Core/x.png"));
        }

        [Test]
        public void FoundationMaterialTemplatesCoverEveryKeywordSet()
        {
            var names = MaterialTemplates.Foundation().Select(s => s.Name).ToList();
            foreach (var n in new[] { MaterialLibrary.LitOpaque, MaterialLibrary.LitEmissive, MaterialLibrary.LitTransparent, MaterialLibrary.LitAlphaTest, MaterialLibrary.Unlit, MaterialLibrary.UnlitAdditive, MaterialLibrary.ParticlesAdditive, MaterialLibrary.HandGhost })
                Assert.That(names, Does.Contain(n));
            Assert.That(Shader.Find("MusicRoom/HandGhost"), Is.Not.Null, "the hand shader imports");
        }
    }
}
