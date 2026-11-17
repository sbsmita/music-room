// Room module data and editor-side contracts: the light conversion of spec 07 section 6.1, the switch geometry and
// touch zone, flicker / sway formulas (the plants' shader sway against a pivot turned on the CPU), the custom shaders
// (import without errors, one template each), the texture bake provider (names, flags, discovered by the editor tools),
// the atlases and the loveseat palette, the particles' lifetimes and the generated textures' shapes and determinism.
using System.Linq;
using MusicRoom.Core;
using MusicRoom.EditorTools;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Render;
using MusicRoom.Room.Table;
using MusicRoom.Room.Textures;
using MusicRoom.Room.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusicRoom.Tests.Room
{
    public class RoomDataTests
    {
        [Test]
        public void LightIntensitiesAreTheSpecsConversion()
        {
            // URP intensity = spec / pi x d_ref^(2 - decay); directional = spec / pi (spec 07 section 6.1).
            float U(float spec, float dRef, float decay) => spec / Mathf.PI * Mathf.Pow(dRef, 2f - decay);
            Assert.That(U(2.4f, 2f, 1.5f), Is.EqualTo(1.0804f).Within(1e-4f), "floor lamp");
            Assert.That(U(4.2f, 3f, 1.6f), Is.EqualTo(2.0747f).Within(1e-4f), "fire");
            Assert.That(U(0.45f, 0.6f, 1.8f), Is.EqualTo(0.1293f).Within(1e-4f), "candles");
            Assert.That(0.55f / Mathf.PI, Is.EqualTo(0.1751f).Within(1e-4f), "moon");
            // The moon's Euler (24.05, -68.40) is the direction (5.2, 3, 2.18) -> (.2, .6, .2) mirrored to Unity.
            Vector3 dir = (Coords.FromRightHanded(0.2f, 0.6f, 0.2f) - Coords.FromRightHanded(5.2f, 3f, 2.18f)).normalized;
            Assert.That(Vector3.Angle(Quaternion.Euler(24.05f, -68.40f, 0f) * Vector3.forward, dir), Is.LessThan(0.05f));
        }

        [Test]
        public void SwitchLeversAndTouchZoneAreTheReferenceValues()
        {
            Assert.That(AmbiencePanel.LeverOnDeg, Is.EqualTo(0.55f * Mathf.Rad2Deg).Within(1e-3f), "right-handed rotation.x -0.55 rad, mirrored");
            Assert.That(AmbiencePanel.ToggleX, Is.EqualTo(new[] { -0.075f, 0f, 0.075f }));
            Assert.That(AmbiencePanel.Labels, Is.EqualTo(new[] { "FIRE", "WIND", "SNOW" }));
            // (dx^2 + dz^2) < .018^2 and .005 < y < .06 in panel space.
            Assert.That(AmbiencePanel.Inside(new Vector3(-0.075f, 0.03f, 0f), -0.075f), Is.True);
            Assert.That(AmbiencePanel.Inside(new Vector3(-0.075f + 0.017f, 0.03f, 0.004f), -0.075f), Is.True);
            Assert.That(AmbiencePanel.Inside(new Vector3(-0.075f + 0.013f, 0.03f, 0.013f), -0.075f), Is.False, "outside the 18 mm radius");
            Assert.That(AmbiencePanel.Inside(new Vector3(0f, 0.061f, 0f), 0f), Is.False, "above the zone");
            Assert.That(AmbiencePanel.Inside(new Vector3(0f, 0.004f, 0f), 0f), Is.False, "below the zone");
            Assert.That(AmbiencePanel.Inside(new Vector3(0f, 0.03f, 0f), 0.075f), Is.False, "another toggle's zone");
            // Panel pose in the rig frame (JSON rigDecor.sideTable.ambiencePanel).
            Assert.That(SideTable.PanelLocal, Is.EqualTo(new Vector3(-0.19f, 0.6f, -0.02f)));
            Assert.That(SideTable.PanelYawDeg, Is.EqualTo(-0.12f * Mathf.Rad2Deg).Within(1e-3f));
            Assert.That(SideTable.HomeAll, Is.EqualTo(new Vector3(-0.39f, 0f, -0.21f)));
            Assert.That(SideTable.HomeYaw, Is.EqualTo(-90f));
            // Desktop click target: a hit cylinder (r .016, h .05) at (x, .03, 0).
            Assert.That(AmbiencePanel.HitRadius, Is.EqualTo(0.016f)); Assert.That(AmbiencePanel.HitHeight, Is.EqualTo(0.05f));
            Assert.That(AmbiencePanel.HitY, Is.EqualTo(0.03f));
            Assert.That(AmbiencePanel.LedOnIntensity, Is.EqualTo(1.4f), "emissiveIntensity when on");
        }

        [Test]
        public void FlickerAndSwayFollowTheReferenceFormulas()
        {
            // At 72 Hz the frame-rate independent walk is exactly the reference per-frame walk.
            float walk = 0f, perFrame = 0f;
            float[] r = { 0.9f, 0.1f, 0.75f, 0.3f, 0.5f };
            foreach (float x in r) { FlickerLight.Step(ref walk, x, 1f / 72f); perFrame += (x - 0.5f) * 0.08f; perFrame *= 0.92f; }
            Assert.That(walk, Is.EqualTo(perFrame).Within(1e-6f));
            float t = 2.5f, ph = 1.1f;
            float k = 1f + 0.22f * (0.5f * Mathf.Sin(t * 11f + ph) + 0.3f * Mathf.Sin(t * 17.3f + ph * 2f) + walk);
            Assert.That(FlickerLight.Intensity(2.0747f, 0.22f, ph, t, walk), Is.EqualTo(2.0747f * k).Within(1e-5f));
            // Sway: right-handed Euler XYZ (rx, 0, rz) in the plant frame, mirrored.
            float amp = 0.02f, phase = 0.7f;
            float rz = Mathf.Sin(t * 0.7f + phase) * amp, rx = Mathf.Sin(t * 0.5f + phase * 1.3f) * amp * 0.7f;
            Quaternion want = Coords.FromRightHanded(Rh.Euler(rx, 0f, rz));
            Assert.That(Quaternion.Angle(PlantSway.Rotation(t, phase, amp), want), Is.LessThan(1e-3f));
        }

        [Test]
        public void TheShaderSwayIsTheReferencePerPlantGroupRotation()
        {
            // The reference rotates each plant's group; the shader moves each vertex about its plant's base in the plant's frame
            // (MusicRoom/RoomLit _SWAY; PlantSway.Apply is the shader's maths). Compare with a pivot transform under the plant's
            // world frame turned by Rotation, for an upright plant and for the fern on the turned shelf.
            var shelf = Rh.TRS(new Vector3(-3.02f, 0f, 2.68f), Rh.Euler(0f, Mathf.PI / 2f, 0f)) * Rh.T(0.2f, 1.925f, 0f);
            foreach (var world in new[] { Rh.T(-1.05f, 0f, 3.76f), shelf })
            {
                var sway = PlantSway.From("p", world, 1.3f, 0.02f);
                var basis = new GameObject("basis").transform;
                var pivot = new GameObject("pivot").transform;
                try
                {
                    Rh.Apply(basis, world);
                    pivot.SetParent(basis, false);
                    Assert.That(Vector3.Distance(basis.position, sway.Base), Is.LessThan(1e-5f));
                    foreach (float t in new[] { 0f, 1.7f, 9.25f })
                    {
                        pivot.localRotation = PlantSway.Rotation(t, sway.Phase, sway.Amp);
                        foreach (var local in new[] { new Vector3(0.1f, 0.9f, -0.2f), new Vector3(-0.3f, 0.4f, 0.05f) })
                        {
                            Vector3 still = basis.TransformPoint(local);       // the merged mesh's vertex
                            Vector3 cpu = pivot.TransformPoint(local);         // the reference's swayed group
                            Assert.That(Vector3.Distance(sway.Apply(still, t), cpu), Is.LessThan(1e-5f), $"t {t}");
                        }
                    }
                    Vector4 d = sway.VertexData;
                    Assert.That(new Vector2(d.z, d.w).magnitude, Is.EqualTo(1f).Within(1e-5f), "cos / sin of the frame's yaw");
                }
                finally { Object.DestroyImmediate(basis.gameObject); }
            }
        }

        [Test]
        public void TheLoveseatPaletteQuadrantsSurviveTheWeaveTiling()
        {
            // Every part's UV0 lands in its own clamped quadrant after the x 8 tiling, by whole weave tiles.
            Assert.That(SofaPalette.Offset(SofaPalette.Part.Frame) * SofaPalette.Tiling, Is.EqualTo(new Vector2(-128f, -128f)));
            foreach (var part in new[] { SofaPalette.Part.Frame, SofaPalette.Part.Cushion, SofaPalette.Part.Pillow })
            {
                Vector2 o = SofaPalette.Offset(part);
                Assert.That((o.x * SofaPalette.Tiling) % 1f, Is.EqualTo(0f), "a whole number of tiles: the weave is unchanged");
                foreach (var uv in new[] { Vector2.zero, Vector2.one, new Vector2(0.37f, 0.81f) })
                    Assert.That(SofaPalette.QuadrantOf(uv + o), Is.EqualTo((int)part), part.ToString());
            }
        }

        [Test]
        public void ParticlesLiveLongEnoughToReachTheWrapHeight()
        {
            foreach (var s in new[] { FireParticles.Smoke, FireParticles.Embers })
            {
                // The slowest particle reaches the top inside its lifetime; the collision plane ends every life there.
                Assert.That(s.SpeedMin * FireParticles.MaxLifetime(s), Is.EqualTo(s.Top - s.Bottom).Within(1e-5f), s.Name);
                // E[span / v], v uniform: a numeric integral.
                double sum = 0; const int n = 20000;
                for (int i = 0; i < n; i++) { double v = s.SpeedMin + (s.SpeedMax - s.SpeedMin) * (i + 0.5) / n; sum += (s.Top - s.Bottom) / v; }
                Assert.That(FireParticles.MeanLifetime(s), Is.EqualTo(sum / n).Within(1e-4), s.Name);
                Assert.That(s.Top, Is.GreaterThan(0.78f), "the wrap is behind the lintel (its bottom at y .78)");
            }
        }

        [Test]
        public void EveryCustomShaderImportsWithoutErrorsAndHasATemplate()
        {
            var provider = new RoomTemplates();
            Assert.That(provider.ResourcesFolder, Is.EqualTo("Assets/MusicRoom/Room"));
            var specs = provider.Templates.ToList();
            string[] names = { "MusicRoom/Flame", "MusicRoom/Coals", "MusicRoom/Snow", "MusicRoom/Frost", "MusicRoom/NightSky", "MusicRoom/NightGround", "MusicRoom/FloorDecal", "MusicRoom/Billboard",
                "MusicRoom/RoomLit", "MusicRoom/VertexColorUnlit" };
            foreach (string n in names)
            {
                Shader sh = Shader.Find(n);
                Assert.That(sh, Is.Not.Null, n + " imported");
                Assert.That(ShaderUtil.ShaderHasError(sh), Is.False, n + " compiles");
                var messages = ShaderUtil.GetShaderMessages(sh);
                Assert.That(messages.Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m => m.message), Is.Empty, n);
                Assert.That(specs.Any(s => s.ShaderName == n), Is.True, n + " has a template");
            }
            // Template names are prefixed with the module (ARCHITECTURE.md section 2 rule 4).
            Assert.That(specs.All(s => s.Name.StartsWith("Room/")), Is.True);
            Assert.That(ProviderDiscovery.Instantiate<IMaterialTemplateProvider>().Any(p => p is RoomTemplates), Is.True, "render-setup finds the provider");
        }

        [Test]
        public void MaterialTemplateAssetsExistForTheRoom()
        {
            // Written by Tools/unity.sh render-setup (MaterialTemplates.CreateAll) inside the module folder.
            foreach (var spec in new RoomTemplates().Templates)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/MusicRoom/Room/Resources/MaterialTemplates/{spec.Name}.mat");
                Assert.That(m, Is.Not.Null, spec.Name + " template asset (run Tools/unity.sh render-setup)");
                Assert.That(m.shader.name, Is.EqualTo(spec.ShaderName));
            }
            // The keywords survive the editor loading the assets (RoomLit has no ShaderGUI to re-derive them).
            var gloss = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/LitGlossMap.mat");
            Assert.That(gloss.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
            var normal = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/LitNormal.mat");
            Assert.That(normal.IsKeywordEnabled("_NORMALMAP"), Is.True);
            var emissive = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/LitEmissive.mat");
            Assert.That(emissive.IsKeywordEnabled("_EMISSION"), Is.True);
            var sway = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/LitSway.mat");
            Assert.That(sway.IsKeywordEnabled("_SWAY"), Is.True);
            Assert.That(gloss.IsKeywordEnabled("_SWAY") || normal.IsKeywordEnabled("_SWAY") || emissive.IsKeywordEnabled("_SWAY"), Is.False, "only the plants sway");
            var snow = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/Snow.mat");
            var frost = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/Frost.mat");
            Assert.That(snow.renderQueue, Is.EqualTo(3002), "glass 3000, snow +2");
            Assert.That(frost.renderQueue, Is.EqualTo(3004), "frost +4");
            var leaf = AssetDatabase.LoadAssetAtPath<Material>("Assets/MusicRoom/Room/Resources/MaterialTemplates/Room/LitSwayLeaf.mat");
            Assert.That(leaf.IsKeywordEnabled("_ALPHATEST_ON") && leaf.IsKeywordEnabled("_SWAY_TWO_SIDED"), Is.True, "alpha clip, both faces lit");
            Assert.That(leaf.GetFloat("_Cull"), Is.EqualTo(0f));
            Assert.That(leaf.renderQueue, Is.EqualTo(2450));
        }

        [Test]
        public void TheBakeProviderListsEveryRoomTexture()
        {
            var specs = new RoomTextureBakeProvider().Textures.ToList();
            // The room's own names; the woods' colour maps and the soft dot are Core's shared set (SharedTextures).
            string[] want = { "Room/wood_oak_gloss", "Room/wood_walnut_gloss", "Room/plaster", "Room/rug", "Room/leaf_atlas", "Room/brick", "Room/weave_normal", "Room/scenery_atlas", "Room/night_sky", "Room/hill_snow", "Room/butterfly_wings" };
            Assert.That(specs.Select(s => s.Name), Is.EquivalentTo(want), "Room/<name> (ARCHITECTURE.md section 2 rule 4)");
            Assert.That(specs.Where(s => s.Linear).Select(s => s.Name), Is.EquivalentTo(new[] { "Room/wood_oak_gloss", "Room/wood_walnut_gloss", "Room/weave_normal" }), "gloss and normal maps are linear");
            Assert.That(specs.Single(s => s.Name == "Room/weave_normal").NormalMap, Is.True);
            Assert.That(specs.Single(s => s.Name == "Room/leaf_atlas").HasAlpha, Is.True);
            Assert.That(specs.Single(s => s.Name == "Room/butterfly_wings").HasAlpha && specs.Single(s => s.Name == "Room/butterfly_wings").AlphaIsTransparency, Is.True, "the wings' outline is their alpha");
            Assert.That(specs.Single(s => s.Name == "Room/wood_oak_gloss").HasAlpha, Is.True, "A = smoothness");
            Assert.That(specs.Single(s => s.Name == "Room/wood_oak_gloss").Repeat, Is.True);
            var core = SharedTextures.Specs().Select(s => s.Name).ToList();
            Assert.That(core, Does.Contain(RoomTextures.WoodOak).And.Contain(RoomTextures.WoodWalnut).And.Contain(RoomTextures.SoftDot), "shared ones are Core's");
            Assert.That(RoomTextures.Used.Except(RoomTextures.All).Except(core), Is.Empty, "every bound texture is baked by someone");
            Assert.That(specs.Single(s => s.Name == "Room/night_sky").Repeat, Is.False, "the sky clamps");
            Assert.That(ProviderDiscovery.Instantiate<ITextureBakeProvider>().Any(p => p is RoomTextureBakeProvider), Is.True, "bake finds the provider");
        }

        [Test]
        public void GeneratedTexturesHaveTheirShapes()
        {
            // The runtime fallback of Core's soft dot is Core's own pixels.
            var dot = RoomTexGen.CoreSoftDot(SharedTextures.SoftDot);
            Assert.That(dot.Width, Is.EqualTo(64));
            Assert.That(dot.Pixels[(63 - 31) * 64 + 31].a, Is.EqualTo(ProcTex.SoftDotAlpha(64, 31, 31)));
            Assert.That(dot.Pixels[0].a, Is.EqualTo(0), "transparent corner");
            var leaf = RoomTexGen.Leaf(false, "leaf_light");
            Assert.That(leaf.Width, Is.EqualTo(128)); Assert.That(leaf.Height, Is.EqualTo(256));
            Assert.That(leaf.Pixels[0].a, Is.EqualTo(0), "outside the outline");
            Color32 mid = leaf.Pixels[128 * 128 + 50];
            Assert.That(mid.a, Is.EqualTo(255), "inside the leaf");
            Assert.That(mid.g, Is.GreaterThan(mid.r), "green");
            var rug = RoomTexGen.Rug();
            Color32 border = rug.Pixels[(511 - 256) * 512 + 22], field = rug.Pixels[(511 - 200) * 512 + 10];
            Assert.That(border.r, Is.GreaterThan(field.r + 40), "gold border at x 22");
            var brick = RoomTexGen.Brick();
            Color32 mortar = brick.Pixels[(511 - 0) * 512 + 100], face = brick.Pixels[(511 - 16) * 512 + 40];
            Assert.That(mortar.g, Is.GreaterThan(face.g + 40), "pale mortar line at the top row, red brick face below it");
            var normal = RoomTexGen.WeaveNormal();
            foreach (var c in new[] { normal.Pixels[0], normal.Pixels[777], normal.Pixels[128 * 64 + 9] })
            {
                var n = new Vector3(c.r / 127.5f - 1f, c.g / 127.5f - 1f, c.b / 127.5f - 1f);
                Assert.That(n.magnitude, Is.EqualTo(1f).Within(0.02f), "unit normals");
                Assert.That(n.z, Is.GreaterThan(0.85f), "a gentle weave");
            }
            var hills = RoomTexGen.HillSnow();
            Assert.That(hills.Pixels.Count(p => p.a > 0), Is.GreaterThan(1024 * 20), "snow on the ridges");
            // The leaf atlas: light | dark side by side, each the generator's pixels.
            var atlas = RoomTexGen.LeafAtlas();
            var dark = RoomTexGen.Leaf(true, "leaf_dark");
            Assert.That(atlas.Width, Is.EqualTo(256)); Assert.That(atlas.Height, Is.EqualTo(256));
            for (int y = 0; y < 256; y += 37)
                for (int x = 0; x < 128; x += 13)
                {
                    Assert.That(atlas.Pixels[y * 256 + x], Is.EqualTo(leaf.Pixels[y * 128 + x]));
                    Assert.That(atlas.Pixels[y * 256 + 128 + x], Is.EqualTo(dark.Pixels[y * 128 + x]));
                }
            for (int y = 0; y < 256; y++) Assert.That(atlas.Pixels[y * 256 + 127].a + atlas.Pixels[y * 256 + 128].a, Is.EqualTo(0), "the halves never touch");
            Assert.That(RoomTextures.LeafAtlasOffset(true).x, Is.EqualTo(0.5f)); Assert.That(RoomTextures.LeafAtlasScale.x, Is.EqualTo(0.5f));
            var palette = RoomTexGen.BookPalette();
            Assert.That(palette.Width, Is.EqualTo(10)); Assert.That(palette.PointFilter, Is.True);
            Assert.That(palette.Pixels[0].r / 255.0, Is.EqualTo(RoomTexGen.LinearToSrgb(0.58)).Within(0.005), "sRGB-encoded linear HSL");
        }

        [Test]
        public void SceneryAndNightSkyAreDeterministicAndLookRight()
        {
            var a = RoomTexGen.Scenery(2);
            var b = RoomTexGen.Scenery(2);
            Assert.That(a.Pixels, Is.EqualTo(b.Pixels), "same seed, same painting");
            Assert.That(RoomTexGen.Scenery(1).Pixels, Is.Not.EqualTo(a.Pixels));
            // The scenery atlas: three slots of 512 x 384 with 16-row gutters that repeat each slot's edge row, and the
            // canvas UVs of painting `seed` cover exactly its slot.
            var atlas = RoomTexGen.SceneryAtlas();
            Assert.That(atlas.Width, Is.EqualTo(512)); Assert.That(atlas.Height, Is.EqualTo(1184));
            for (int seed = 1; seed <= 3; seed++)
            {
                var img = RoomTexGen.Scenery(seed);
                int row0 = RoomTexGen.ScenerySlotRow(seed - 1);
                for (int y = 0; y < 384; y += 23) for (int x = 0; x < 512; x += 41) Assert.That(atlas.Pixels[(row0 + y) * 512 + x], Is.EqualTo(img.Pixels[y * 512 + x]));
                if (seed > 1) Assert.That(atlas.Pixels[(row0 - 1) * 512 + 99], Is.EqualTo(img.Pixels[99]), "gutter below repeats the bottom row");
                if (seed < 3) Assert.That(atlas.Pixels[(row0 + 384 + 7) * 512 + 99], Is.EqualTo(img.Pixels[383 * 512 + 99]), "gutter above repeats the top row");
                Vector2 lo = RoomTextures.SceneryAtlasOffset(seed), hi = lo + RoomTextures.SceneryAtlasScale;
                Assert.That(lo.y * 1184f, Is.EqualTo(row0).Within(1e-3f)); Assert.That(hi.y * 1184f, Is.EqualTo(row0 + 384).Within(1e-3f));
            }
            var sky = RoomTexGen.NightSky();
            Assert.That(sky.Width, Is.EqualTo(2048)); Assert.That(sky.Height, Is.EqualTo(1024));
            // The moon disc at (.62 w, .42 h) on the canvas: bright; the sky above: deep blue.
            Color32 moon = sky.Pixels[(1023 - (int)(1024 * 0.42f)) * 2048 + (int)(2048 * 0.62f)];
            Color32 top = sky.Pixels[1023 * 2048 + 3];
            Assert.That(moon.r, Is.GreaterThan(200));
            Assert.That(top.b, Is.GreaterThan(top.r), "night blue");
            Assert.That(top.r, Is.LessThan(30));
            // The near hill at the bottom of the canvas is darker than the sky above the far hill.
            Color32 hill = sky.Pixels[3 * 2048 + 1000];
            Assert.That(hill.b, Is.LessThan(40));
        }

        [Test]
        public void RoomDimensionsAreTheReferenceAndTheFloorIsWhole()
        {
            Assert.That(RoomDims.ZFront, Is.EqualTo(4.08f).Within(1e-5f));
            Assert.That(RoomDims.Cz, Is.EqualTo(1.48f).Within(1e-5f));
            Assert.That(RoomDims.WinZ, Is.EqualTo(1.18f).Within(1e-5f));
            Assert.That(Vector3.Distance(RoomDims.WindowUnity, new Vector3(3.18f, 1.55f, -1.18f)), Is.LessThan(1e-5f));
            Assert.That(GrabConstants.RoomBox.size, Is.EqualTo(new Vector3(RoomDims.W, RoomDims.H, RoomDims.D)));
        }
    }
}