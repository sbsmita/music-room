// The book's assets (ARCHITECTURE.md 14): two material templates written by Tools/unity.sh render-setup into
// Sheet/Resources/MaterialTemplates/Sheet/ ("Sheet/Paper": MusicRoom/SheetPaper; "Sheet/Case": the room's lit shader with
// its metal / gloss map, for the boards, page block, ribbon and lamp) and two small textures, both generated in code
// (registered for the bake, generated at run time when not baked): "Sheet/paper_detail" (256 x 256, tileable value noise of
// the lab's paperDetail octaves, about 9, 2.7 and 0.6 mm, linear) and the case palette (32 x 8: cloth, page edges, ribbon,
// brass, shade; plus its metallic / smoothness map).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Sheet
{
    public sealed class SheetMaterialTemplates : IMaterialTemplateProvider
    {
        public const string Paper = "Sheet/Paper", Case = "Sheet/Case";
        public const string PaperShader = "MusicRoom/SheetPaper", CaseShader = "MusicRoom/RoomLit";
        public string ResourcesFolder => "Assets/MusicRoom/Sheet";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(Paper, PaperShader, m => { m.renderQueue = 2000; });
                yield return new MaterialTemplateSpec(Case, CaseShader, m =>
                {
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.SetFloat(MaterialLibrary.Smoothness, 1f);
                    m.SetFloat(MaterialLibrary.Metallic, 0f);
                    m.SetColor(MaterialLibrary.BaseColor, Color.white);
                });
            }
        }
    }

    public sealed class SheetTextures : ITextureBakeProvider
    {
        public const string DetailName = "Sheet/paper_detail";
        public const int DetailSize = 256;
        /// <summary>One tile of the mottle, mm (the octaves fit it exactly: 8, 27 and 120 cells).</summary>
        public const float DetailTileMm = 72f;

        public IEnumerable<TextureBakeSpec> Textures
        {
            get { yield return new TextureBakeSpec { Name = DetailName, Generate = () => PaperDetail(true), Linear = true, Mipmaps = true, Repeat = true, AnisoLevel = 2 }; }
        }

        public static Texture2D Detail() => GeneratedAssets.Texture(DetailName, () => PaperDetail(false));

        // ---- paper mottle (book.mjs paperDetail: vnoise at 9, 2.7 and 0.6 mm, weights .6 .3 .1), tileable
        static uint Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return (uint)(h ^ (int)((uint)h >> 16));
            }
        }
        static float Lattice(int x, int y, int period) { x %= period; y %= period; if (x < 0) x += period; if (y < 0) y += period; return Hash(x, y) / 4294967296f; }
        static float VNoise(float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Lattice(xi, yi, period), b = Lattice(xi + 1, yi, period), c = Lattice(xi, yi + 1, period), d = Lattice(xi + 1, yi + 1, period);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        /// <summary>0.5 + the lab's mottle (its +-3 % is applied in the shader: 1 + (t - 0.5) x 0.06).</summary>
        public static Texture2D PaperDetail(bool forBake)
        {
            const int n = DetailSize;
            var tex = new Texture2D(n, n, TextureFormat.R8, true, true) { name = "sheet_paper_detail", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            var px = new byte[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x / (float)n, fy = y / (float)n;
                    float m = VNoise(fx * 8, fy * 8, 8) * 0.6f + VNoise(fx * 27, fy * 27, 27) * 0.3f + VNoise(fx * 120, fy * 120, 120) * 0.1f;
                    px[y * n + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(m * 255f), 0, 255);
                }
            tex.SetPixelData(px, 0);
            tex.Apply(true, false);
            return tex;
        }

        // ---- the case palette: 8 swatches of 4 x 8 texels (u = (i + .5) / 8)
        public enum Swatch { Cloth = 0, ClothEdge, PageEdge, Ribbon, Brass, Shade, ShadeInside, Clamp }
        public const int SwatchCount = 8;
        public static Vector2 Uv(Swatch s, float v = 0.5f) => new Vector2(((int)s + 0.5f) / SwatchCount, Mathf.Clamp(v, 0.07f, 0.93f));

        static readonly Color32[] Albedo =
        {
            new Color32(0x2c, 0x45, 0x3a, 0xff),   // dark green bookcloth
            new Color32(0x24, 0x38, 0x2f, 0xff),   // its worn edges
            new Color32(0xe6, 0xdc, 0xc4, 0xff),   // cream fore-edges
            new Color32(0x7a, 0x1f, 0x23, 0xff),   // deep red silk
            new Color32(0xb8, 0x93, 0x52, 0xff),   // brass
            new Color32(0x1f, 0x3d, 0x2c, 0xff),   // the lamp's green shade
            new Color32(0xf2, 0xe8, 0xd2, 0xff),   // its cream inside
            new Color32(0x2a, 0x26, 0x22, 0xff),   // the clamp's dark felt
        };
        // metallic (r) and smoothness (a) per swatch
        static readonly Vector2[] MetalGloss =
        {
            new Vector2(0f, 0.18f), new Vector2(0f, 0.15f), new Vector2(0f, 0.10f), new Vector2(0f, 0.55f),
            new Vector2(1f, 0.62f), new Vector2(0f, 0.45f), new Vector2(0f, 0.2f), new Vector2(0f, 0.1f),
        };

        public static Texture2D CasePalette()
        {
            var tex = new Texture2D(SwatchCount * 4, 8, TextureFormat.RGBA32, false, false) { name = "sheet_case_palette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[SwatchCount * 4 * 8];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < SwatchCount * 4; x++)
                {
                    var c = Albedo[x / 4];
                    // the page block's fore-edge: faint lines of the leaves across v
                    if (x / 4 == (int)Swatch.PageEdge && (y % 2) == 1) c = new Color32((byte)(c.r - 14), (byte)(c.g - 14), (byte)(c.b - 16), 255);
                    px[y * SwatchCount * 4 + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        public static Texture2D CaseMetal()
        {
            var tex = new Texture2D(SwatchCount * 4, 8, TextureFormat.RGBA32, false, true) { name = "sheet_case_metal", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[SwatchCount * 4 * 8];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < SwatchCount * 4; x++)
                {
                    var mg = MetalGloss[x / 4];
                    px[y * SwatchCount * 4 + x] = new Color32((byte)(mg.x * 255), 0, 0, (byte)(mg.y * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
