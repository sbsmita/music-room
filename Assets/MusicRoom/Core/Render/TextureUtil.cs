// Small shared generators: the soft dot (hover glows, embers, smoke, blobs), a unit quad, the texture-row convention.
// Image row 0 is the top in the generators; Texture2D row 0 is the bottom: write image row y to row h-1-y.
using UnityEngine;

namespace MusicRoom.Core
{
    public static class TextureUtil
    {
        static Mesh s_quad;

        /// <summary>The soft dot texture: radial white, alpha 1 at the centre, .55 at 35 %, 0 at the edge (ProcTex.SoftDot;
        /// the shared baked copy is SharedTextures.Get(SharedTextures.SoftDot)).</summary>
        public static Texture2D SoftDot(int size = 64) => ProcTex.SoftDot(size);

        /// <summary>A 1 x 1 quad in the XY plane facing +Z (so LookRotation(normal, up) faces it along the normal), both UV sets 0..1.</summary>
        public static Mesh UnitQuad()
        {
            if (s_quad != null) return s_quad;
            var m = new Mesh { name = "UnitQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }; // both faces
            m.RecalculateBounds();
            s_quad = m;
            return m;
        }

        /// <summary>sRGB hex ("#ffc46a") to Color (gamma values; Unity converts on upload in Linear projects).</summary>
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
