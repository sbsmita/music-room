// The calm room's materials, mapped to URP (spec 07 section 3.5): roughness r -> smoothness 1 - r, roughness
// maps -> the metallic-gloss map, metals on the LitMetal template, colours as sRGB hex (Material.SetColor linearises
// them). Exact linear values (emission x intensity, custom shader uniforms) go through
// SetLinear / SetVector so Unity's gamma-to-linear conversion is not applied twice. Every material is a clone of a
// template (MaterialLibrary); textures are bound through the TextureLoader (baked or generated in the background).
using MusicRoom.Core;
using MusicRoom.Room.Textures;
using UnityEngine;

namespace MusicRoom.Room.Render
{
    /// <summary>One material per merged static renderer (spec 07 section 11), plus the animated parts' own.</summary>
    public enum RoomMat
    {
        Floor, Wall, Ceiling, Trim, Rug, Glass, Walnut, Books, WalnutGloss, Fabric, Gilt, Frame, Canvas,
        Brass, LampShade, Brick, Soot, Stone, Log, Sofa, Terracotta, Soil, Stem, Leaf, Wax,
        Mound, Sky, Ground, Decal, Flame, CandleFlame, Coals, Glow, Snow, Frost, Led, Smoke, Ember,
        /// <summary>Every flower's petals (World/FlowerBuilder.cs): one static renderer, glowing with the music.</summary>
        Petal,
        /// <summary>The butterflies (Decor/Butterflies.cs): their own dynamic mesh, outside the room root.</summary>
        Butterfly,
        Count
    }

    /// <summary>The loveseat's three fabrics in ONE material (spec 07 section 11 "loveseat (palette)"): URP Lit samples
    /// the base map and the weave normal map with the same tiled UV (x 8), so a plain palette lookup per part is not
    /// possible. Instead the palette is a 2 x 2 CLAMPED texture and each part's UVs are shifted by a whole number of
    /// weave tiles into its own quadrant far outside [0, 1]: clamping returns that quadrant's texel exactly, while the
    /// weave (Repeat) is unchanged by whole-tile shifts.</summary>
    public static class SofaPalette
    {
        public enum Part { Frame = 0, Cushion = 1, Pillow = 2 }
        /// <summary>The weave's repeat (8): URP tiles every map by _BaseMap_ST.</summary>
        public const float Tiling = 8f;
        /// <summary>UV0 shift per part: 16 UV = 128 whole weave tiles.</summary>
        public const float Shift = 16f;
        public static readonly string[] Hex = { "#66795f", "#7a8c70", "#c9a46a" };

        public static Vector2 Offset(Part p) => p == Part.Frame ? new Vector2(-Shift, -Shift) : p == Part.Cushion ? new Vector2(Shift, -Shift) : new Vector2(-Shift, Shift);

        /// <summary>Texel (0,0) frame, (1,0) cushion, (0,1) pillow, (1,1) frame; sRGB, point, clamp, no mips.</summary>
        public static Texture2D Make()
        {
            Color32 f = TextureUtil.Hex(Hex[0]), c = TextureUtil.Hex(Hex[1]), p = TextureUtil.Hex(Hex[2]);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = "Room sofa palette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point,
            };
            t.SetPixels32(new[] { f, c, p, f });
            t.Apply(false, true);
            return t;
        }

        /// <summary>The colour a UV (before tiling) samples: the CPU mirror of the clamped lookup (tests).</summary>
        public static int QuadrantOf(Vector2 uv)
        {
            float u = uv.x * Tiling, v = uv.y * Tiling;
            int x = u >= 1f ? 1 : 0, y = v >= 1f ? 1 : 0;
            return y * 2 + x;   // 0 frame, 1 cushion, 2 pillow, 3 frame
        }
    }

    public sealed class RoomMaterials
    {
        public static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        public static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        public static readonly int Tint = Shader.PropertyToID("_Tint");
        public static readonly int HillSnowMap = Shader.PropertyToID("_HillSnowMap");
        public static readonly int HillSnowOn = Shader.PropertyToID("_HillSnowOn");
        public static readonly int FrostAmount = Shader.PropertyToID("_Frost");
        public static readonly int Veil = Shader.PropertyToID("_Veil");
        public static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        /// <summary>The switch LED: emissive #ff8a2a, emissiveIntensity 1.4 when on.</summary>
        public const float LedOnIntensity = 1.4f;

        readonly Material[] _m = new Material[(int)RoomMat.Count];
        public Material this[RoomMat k] => _m[(int)k];
        /// <summary>The loveseat's palette (owned: destroyed with the module).</summary>
        public Texture2D SofaPaletteTexture { get; private set; }
        /// <summary>The petals' palette (owned: destroyed with the module).</summary>
        public Texture2D PetalPaletteTexture { get; private set; }
        /// <summary>The petals' resting emission (linear): a faint glow, so their colours read in the evening light.</summary>
        public static readonly Color PetalEmission = new Color(0.05f, 0.05f, 0.05f, 1f);

        /// <summary>Pass an exact linear colour to a Color property (Unity linearises SetColor values in Linear space).</summary>
        public static void SetLinear(Material m, int id, Color linear) => m.SetColor(id, linear.gamma);

        public static Color Hex(string hex) => TextureUtil.Hex(hex);

        /// <summary>The transparent overlays are specified for a non-sRGB framebuffer: colour x alpha lands as an sRGB
        /// value. URP blends linear light, so a material colour that gives the same result over black is
        /// SRGBToLinear(colour x alpha) / alpha, returned in gamma form for SetColor (which linearises it).</summary>
        public static Color OverlayColor(Color srgb, float alpha)
        {
            float a = Mathf.Max(alpha, 1e-4f);
            float F(float c) => Mathf.GammaToLinearSpace(c * a) / a;
            return new Color(F(srgb.r), F(srgb.g), F(srgb.b), 1f).gamma;
        }
        public static Color Lin(string hex) => TextureUtil.Hex(hex).linear;
        static Vector4 LinV(string hex) { Color c = Lin(hex); return new Vector4(c.r, c.g, c.b, 1f); }

        public RoomMaterials(MaterialLibrary lib, TextureLoader tex)
        {
            Material Lit(RoomMat k, string hex, float smooth) { var m = lib.Lit(Hex(hex), smooth); m.name = "Room " + k; return _m[(int)k] = m; }
            Material Metal(RoomMat k, string hex, float smooth)
            {
                var m = lib.Clone(MaterialLibrary.LitMetal);
                m.name = "Room " + k;
                m.SetColor(MaterialLibrary.BaseColor, Hex(hex));
                m.SetFloat(MaterialLibrary.Smoothness, smooth);
                m.SetFloat(MaterialLibrary.Metallic, 1f);
                return _m[(int)k] = m;
            }
            Material Clone(RoomMat k, string template) { var m = lib.Clone(template); m.name = "Room " + k; return _m[(int)k] = m; }

            // Shell
            var floor = Clone(RoomMat.Floor, RoomTemplates.LitGlossMap);
            floor.SetFloat(MaterialLibrary.Smoothness, 1f);
            tex.Bind(RoomTextures.WoodOak, floor, MaterialLibrary.BaseMap, Color.white, new Color(0.58f, 0.4f, 0.235f));
            tex.Bind(RoomTextures.WoodOakGloss, floor, MetallicGlossMap);
            var wall = Lit(RoomMat.Wall, "#ffffff", 0.05f);
            tex.Bind(RoomTextures.Plaster, wall, MaterialLibrary.BaseMap, Color.white, new Color(0.88f, 0.83f, 0.75f));
            Lit(RoomMat.Ceiling, "#f2eee6", 0f);
            Lit(RoomMat.Trim, "#f4f0e8", 0.4f);
            var rug = Lit(RoomMat.Rug, "#ffffff", 0f);
            tex.Bind(RoomTextures.Rug, rug, MaterialLibrary.BaseMap, Color.white, new Color(0.36f, 0.12f, 0.15f));
            var glass = Clone(RoomMat.Glass, MaterialLibrary.LitTransparent);
            Color g = Hex("#dfe6ee"); g.a = 0.08f;
            glass.SetColor(MaterialLibrary.BaseColor, g);
            glass.SetFloat(MaterialLibrary.Smoothness, 0.97f);

            // Wood: walnut (map + roughness .8 -> gloss map), walnutGloss (Physical r .5 + clear coat -> smoothness .55), log.
            var walnut = Clone(RoomMat.Walnut, RoomTemplates.LitGlossMap);
            walnut.SetVector(BaseMapSt, new Vector4(2f, 2f, 0f, 0f));
            tex.Bind(RoomTextures.WoodWalnut, walnut, MaterialLibrary.BaseMap, Color.white, new Color(0.27f, 0.15f, 0.085f));
            tex.Bind(RoomTextures.WoodWalnutGloss, walnut, MetallicGlossMap);
            var walnutGloss = Lit(RoomMat.WalnutGloss, "#ffffff", 0.55f);
            walnutGloss.SetVector(BaseMapSt, new Vector4(2f, 2f, 0f, 0f));
            tex.Bind(RoomTextures.WoodWalnut, walnutGloss, MaterialLibrary.BaseMap, Color.white, new Color(0.27f, 0.15f, 0.085f));
            var log = Lit(RoomMat.Log, "#6a4a34", 0f);
            log.SetVector(BaseMapSt, new Vector4(2f, 2f, 0f, 0f));
            tex.Bind(RoomTextures.WoodWalnut, log, MaterialLibrary.BaseMap);
            var books = Lit(RoomMat.Books, "#ffffff", 0.2f);
            books.SetTexture(MaterialLibrary.BaseMap, RoomTexGen.BookPalette().ToTexture());

            // Furniture, props.
            Lit(RoomMat.Fabric, "#3b3f4a", 0f);
            Metal(RoomMat.Gilt, "#d4aa4e", 0.68f);
            Lit(RoomMat.Frame, "#2b1f16", 0.5f);
            // The three canvases: one material over the scenery atlas (each painting's UVs point at its slot).
            var canvas = Lit(RoomMat.Canvas, "#ffffff", 0.25f);
            tex.Bind(RoomTextures.SceneryAtlas, canvas, MaterialLibrary.BaseMap, Color.white, new Color(0.42f, 0.43f, 0.48f));
            Metal(RoomMat.Brass, "#cfa54e", 0.7f);
            var shade = Clone(RoomMat.LampShade, RoomTemplates.LitEmissive);
            shade.SetColor(MaterialLibrary.BaseColor, Hex("#f7e6c8"));
            shade.SetFloat(MaterialLibrary.Smoothness, 0f);
            SetLinear(shade, MaterialLibrary.EmissionColor, Lin("#ffd9a0") * 0.35f);
            var brick = Lit(RoomMat.Brick, "#ffffff", 0.05f);
            tex.Bind(RoomTextures.Brick, brick, MaterialLibrary.BaseMap, Color.white, new Color(0.5f, 0.27f, 0.22f));
            Lit(RoomMat.Stone, "#4a4542", 0.1f);
            Lit(RoomMat.Soot, "#0a0806", 0f);
            // The loveseat: frame, cushions and pillow in one material (SofaPalette), the weave as its normal map.
            var sofa = Clone(RoomMat.Sofa, RoomTemplates.LitNormal);
            SofaPaletteTexture = SofaPalette.Make();
            sofa.SetColor(MaterialLibrary.BaseColor, Color.white);
            sofa.SetTexture(MaterialLibrary.BaseMap, SofaPaletteTexture);
            sofa.SetFloat(MaterialLibrary.Smoothness, 0f);
            sofa.SetVector(BaseMapSt, new Vector4(SofaPalette.Tiling, SofaPalette.Tiling, 0f, 0f));
            tex.Bind(RoomTextures.WeaveNormal, sofa, MaterialLibrary.BumpMap);
            Lit(RoomMat.Terracotta, "#b8623f", 0.15f);
            Lit(RoomMat.Soil, "#2b1d14", 0f);
            // Plants: every stem in one renderer, every leaf (both variants, one atlas) in another; sway in the shader.
            var stem = Clone(RoomMat.Stem, RoomTemplates.LitSway);
            stem.SetColor(MaterialLibrary.BaseColor, Hex("#3f6b2f"));
            stem.SetFloat(MaterialLibrary.Smoothness, 0.2f);
            var leaf = Clone(RoomMat.Leaf, RoomTemplates.LitSwayLeaf);
            leaf.SetFloat(MaterialLibrary.Smoothness, 0.25f);
            leaf.SetFloat(MaterialLibrary.Cutoff, 0.5f);
            tex.Bind(RoomTextures.LeafAtlas, leaf, MaterialLibrary.BaseMap, Color.white, new Color(0.33f, 0.53f, 0.24f));
            // Flowers: every petal in one renderer, colours from a palette (base to tip), swayed with the stems, both faces lit,
            // the palette also their emission map (the music lifts _EmissionColor: ListenBackPulse.ApplyFlowers).
            var petal = Clone(RoomMat.Petal, RoomTemplates.LitSwayPetal);
            PetalPaletteTexture = World.PetalPalette.Make();
            petal.SetColor(MaterialLibrary.BaseColor, Color.white);
            petal.SetTexture(MaterialLibrary.BaseMap, PetalPaletteTexture);
            petal.SetTexture(EmissionMap, PetalPaletteTexture);
            petal.SetFloat(MaterialLibrary.Smoothness, 0.35f);
            SetLinear(petal, MaterialLibrary.EmissionColor, PetalEmission);
            // Butterflies: wings from the generated atlas (Room/butterfly_wings), lit per vertex (MusicRoom/Butterfly).
            var wings = Clone(RoomMat.Butterfly, RoomTemplates.Butterfly);
            tex.Bind(RoomTextures.ButterflyWings, wings, MaterialLibrary.BaseMap);
            Lit(RoomMat.Wax, "#f3e9d2", 0.4f);
            var mound = Clone(RoomMat.Mound, RoomTemplates.LitEmissive);
            mound.SetColor(MaterialLibrary.BaseColor, Hex("#e4ecf8"));
            mound.SetFloat(MaterialLibrary.Smoothness, 0.05f);
            SetLinear(mound, MaterialLibrary.EmissionColor, Lin("#262e40"));
            // The switch LEDs: one mesh, colour per vertex in an unlit shader (AmbiencePanel.LedStrip), one draw for all
            // three (never a MaterialPropertyBlock on a URP Lit renderer, ARCHITECTURE.md section 14).
            Clone(RoomMat.Led, RoomTemplates.LedUnlit);

            // Outside: sky (nightSky x tint, hill snow later), ground (colour from the snow cover).
            var sky = Clone(RoomMat.Sky, RoomTemplates.NightSky);
            sky.SetVector(Tint, Vector4.one);
            sky.SetFloat(HillSnowOn, 0f);
            tex.Bind(RoomTextures.NightSky, sky, MaterialLibrary.BaseMap);
            var ground = Clone(RoomMat.Ground, RoomTemplates.NightGround);
            ground.SetVector(Tint, LinV("#05070f"));

            // Custom shaders.
            Clone(RoomMat.Decal, RoomTemplates.FloorDecal);
            Clone(RoomMat.Flame, RoomTemplates.Flame);
            Clone(RoomMat.CandleFlame, RoomTemplates.Flame);
            Clone(RoomMat.Coals, RoomTemplates.Coals).SetFloat("_Seed", 1.7f);
            var glow = Clone(RoomMat.Glow, RoomTemplates.Billboard);
            glow.SetColor("_Color", Hex("#ff5a14"));
            glow.SetVector("_Size", new Vector4(1.1f, 0.9f, 0f, 0f));
            tex.Bind(RoomTextures.SoftDot, glow, MaterialLibrary.BaseMap);
            Clone(RoomMat.Snow, RoomTemplates.Snow);
            Clone(RoomMat.Frost, RoomTemplates.Frost);

            // Particles: soft dots; alpha per particle (start colour), colour on the material (see OverlayColor).
            var smoke = Clone(RoomMat.Smoke, MaterialLibrary.ParticlesAlpha);
            smoke.SetColor(MaterialLibrary.BaseColor, OverlayColor(Decor.FireParticles.Smoke.Color, Decor.FireParticles.Smoke.Alpha));
            tex.Bind(RoomTextures.SoftDot, smoke, MaterialLibrary.BaseMap);
            var ember = Clone(RoomMat.Ember, MaterialLibrary.ParticlesAdditive);
            ember.SetColor(MaterialLibrary.BaseColor, OverlayColor(Decor.FireParticles.Embers.Color, Decor.FireParticles.Embers.Alpha));
            tex.Bind(RoomTextures.SoftDot, ember, MaterialLibrary.BaseMap);
        }
    }
}
