// The Room module's material templates (ARCHITECTURE.md section 14): one .mat per custom shader or URP Lit keyword set
// the foundation lacks, written by `Tools/unity.sh render-setup` into Room/Resources/MaterialTemplates/Room/ so the
// shader variants survive the build. Keywords and render state are set here; runtime clones only get colours,
// textures and floats. The MR shadow catcher is MR's own shader and template (ARCHITECTURE.md section 12).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Render
{
    public sealed class RoomTemplates : IMaterialTemplateProvider
    {
        public const string LitGlossMap = "Room/LitGlossMap", LitNormal = "Room/LitNormal", Flame = "Room/Flame", Coals = "Room/Coals",
            Snow = "Room/Snow", Frost = "Room/Frost", NightSky = "Room/NightSky", NightGround = "Room/NightGround",
            FloorDecal = "Room/FloorDecal", Billboard = "Room/Billboard", LabelText = "Room/LabelText",
            LitSway = "Room/LitSway", LitSwayLeaf = "Room/LitSwayLeaf", LitEmissive = "Room/LitEmissive", LedUnlit = "Room/LedUnlit",
            LitSwayPetal = "Room/LitSwayPetal", Butterfly = "Room/Butterfly", SongStars = "Room/SongStars",
            RoomSnow = "Room/RoomSnow";

        /// <summary>URP's Lit forward pass without a ShaderGUI (so template keywords stay; _SWAY adds the plants' sway) and
        /// the LEDs' vertex-colour unlit shader.</summary>
        public const string RoomLitShader = "MusicRoom/RoomLit", VertexColorUnlitShader = "MusicRoom/VertexColorUnlit";

        /// <summary>Custom shader names (Room/Shaders/*.shader) and their template.</summary>
        public static readonly string[,] Custom =
        {
            { Flame, "MusicRoom/Flame" }, { Coals, "MusicRoom/Coals" }, { Snow, "MusicRoom/Snow" }, { Frost, "MusicRoom/Frost" },
            { NightSky, "MusicRoom/NightSky" }, { NightGround, "MusicRoom/NightGround" }, { FloorDecal, "MusicRoom/FloorDecal" },
            { Billboard, "MusicRoom/Billboard" }, { Butterfly, "MusicRoom/Butterfly" },
            { SongStars, "MusicRoom/SongStars" }, { RoomSnow, "MusicRoom/RoomSnow" },
        };

        public string ResourcesFolder => "Assets/MusicRoom/Room";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                // Wood with its roughness map: URP's metallic-gloss map (R metallic, A smoothness), smoothness scale 1.
                // MusicRoom/RoomLit (URP's Lit pass, no ShaderGUI): URP Lit's ValidateMaterial would drop the keyword from
                // a template that has no _MetallicGlossMap texture yet.
                yield return new MaterialTemplateSpec(LitGlossMap, RoomLitShader, m =>
                {
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.SetFloat(MaterialLibrary.Smoothness, 1f);
                    m.SetFloat(MaterialLibrary.Metallic, 0f);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                });
                // The loveseat's woven fabric: the weave as a tangent-space normal map (RoomLit: the keyword stays).
                yield return new MaterialTemplateSpec(LitNormal, RoomLitShader, m =>
                {
                    m.EnableKeyword("_NORMALMAP");
                    m.SetFloat("_BumpScale", 1f);
                    m.SetFloat(MaterialLibrary.Smoothness, 0f);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                });
                // The plants (spec 07 section 3.2): all stems in one renderer, all leaves (alpha clip .5, both faces, the
                // back faces lit from behind, normal flipped) in another, swayed in the vertex stage.
                yield return new MaterialTemplateSpec(LitSway, RoomLitShader, m =>
                {
                    m.EnableKeyword("_SWAY");
                    m.SetFloat("_Cull", (float)CullMode.Back);
                    m.SetFloat(MaterialLibrary.Smoothness, 0.2f);
                    m.SetFloat(MaterialLibrary.Metallic, 0f);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                    m.renderQueue = (int)RenderQueue.Geometry;
                });
                yield return new MaterialTemplateSpec(LitSwayLeaf, RoomLitShader, m =>
                {
                    m.EnableKeyword("_SWAY");
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.EnableKeyword("_SWAY_TWO_SIDED");
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat(MaterialLibrary.Cutoff, 0.5f);
                    m.SetFloat("_Cull", (float)CullMode.Off);
                    m.SetFloat(MaterialLibrary.Smoothness, 0.25f);
                    m.SetFloat(MaterialLibrary.Metallic, 0f);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                    m.SetOverrideTag("RenderType", "TransparentCutout");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                });
                // The flowers' petals (World/FlowerBuilder.cs): swayed like the plants, both faces lit (no clip: the petals are
                // geometry), emissive with the palette as the emission map (the music lifts the colour).
                yield return new MaterialTemplateSpec(LitSwayPetal, RoomLitShader, m =>
                {
                    m.EnableKeyword("_SWAY");
                    m.EnableKeyword("_SWAY_TWO_SIDED");
                    m.EnableKeyword("_EMISSION");
                    m.SetFloat("_Cull", (float)CullMode.Off);
                    m.SetFloat(MaterialLibrary.Smoothness, 0.35f);
                    m.SetFloat(MaterialLibrary.Metallic, 0f);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                    m.SetColor(MaterialLibrary.EmissionColor, Color.black);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.renderQueue = (int)RenderQueue.Geometry;
                });
                // Emissive surfaces (lamp shade, sill mound). The foundation's LitEmissive template loses _EMISSION to URP
                // Lit's ValidateMaterial (its GI flags are None, so URP sees no emission: CONTRACT-REQUESTS.md C); this
                // one keeps it.
                yield return new MaterialTemplateSpec(LitEmissive, RoomLitShader, m =>
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor(MaterialLibrary.EmissionColor, Color.black);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                });
                // The three switch LEDs in one mesh: colour per vertex, rewritten on a flip.
                yield return new MaterialTemplateSpec(LedUnlit, VertexColorUnlitShader, m => m.renderQueue = (int)RenderQueue.Geometry);
                for (int i = 0; i < Custom.GetLength(0); i++)
                {
                    string shader = Custom[i, 1];
                    int queue = shader == "MusicRoom/Snow" ? (int)RenderQueue.Transparent + 2
                        : shader == "MusicRoom/Frost" ? (int)RenderQueue.Transparent + 4
                        : shader == "MusicRoom/RoomSnow" ? (int)RenderQueue.Transparent + 5   // the room's flakes over the window's frost
                        : shader == "MusicRoom/FloorDecal" ? (int)RenderQueue.Transparent - 50
                        : shader == "MusicRoom/NightSky" || shader == "MusicRoom/NightGround" ? (int)RenderQueue.Geometry
                        : (int)RenderQueue.Transparent;
                    yield return new MaterialTemplateSpec(Custom[i, 0], shader, m => m.renderQueue = queue);
                }
                // Switch labels: TMP's mobile SDF shader (the default font material uses the desktop one).
                yield return new MaterialTemplateSpec(LabelText, "TextMeshPro/Mobile/Distance Field", m => m.renderQueue = 2990);
            }
        }
    }
}
