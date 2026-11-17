// Draw-call budget helpers for the tape deck (ARCHITECTURE.md sections 14 and 17: table + tape deck + switches <= 15).
// The deck has 17 parts (one per part and material); parts that share a renderer are merged into one mesh, so
// the deck is 7 draws. Two tools do it:
// - MergedMesh: several source meshes in one mesh, each a "part" with its own pose. Static parts are posed once; moving
//   parts (the pegs' travel, the reels' spokes) are re-posed by rewriting their vertex range into preallocated arrays and
//   uploading them, which is allocation-free and only happens on frames where something moved.
// - DeckPalette: a tiny sRGB colour atlas (6 x 6 texels per colour) so parts of different colours share one URP Lit
//   material through their UVs (the SRP Batcher keeps working: no MaterialPropertyBlock). Each part's UVs point at the
//   centre of its cell, so bilinear filtering never mixes in a neighbour's colour; and each cell is exactly one ASTC 6x6
//   block of the baked texture (TextureBaker compresses every bake to ASTC 6x6), so every block is one flat colour that
//   ASTC stores exactly (a block straddling two or three colours would be approximated).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Lessons
{
    public sealed class MergedMesh
    {
        readonly List<Vector3> _srcV = new List<Vector3>(256);
        readonly List<Vector3> _srcN = new List<Vector3>(256);
        readonly List<Vector2> _uv = new List<Vector2>(256);
        readonly List<int> _tri = new List<int>(768);
        readonly List<int> _start = new List<int>(8);
        readonly List<int> _count = new List<int>(8);
        readonly List<Matrix4x4> _pose = new List<Matrix4x4>(8);
        Vector3[] _baseV, _baseN, _v, _n;
        bool _dirty;

        public Mesh Mesh { get; private set; }
        public int PartCount => _start.Count;

        /// <summary>Adds a copy of <paramref name="source"/> at <paramref name="pose"/> (rotation + translation only), every
        /// vertex at <paramref name="uv"/> (a palette cell). Returns the part index for <see cref="Pose"/>.</summary>
        public int Add(Mesh source, Matrix4x4 pose, Vector2 uv)
        {
            int start = _srcV.Count;
            Vector3[] v = source.vertices;                 // construction only: these copies are fine here
            Vector3[] n = source.normals;
            int[] t = source.triangles;
            _srcV.AddRange(v);
            if (n != null && n.Length == v.Length) _srcN.AddRange(n);
            else for (int i = 0; i < v.Length; i++) _srcN.Add(Vector3.up);
            for (int i = 0; i < v.Length; i++) _uv.Add(uv);
            for (int i = 0; i < t.Length; i++) _tri.Add(start + t[i]);
            _start.Add(start);
            _count.Add(v.Length);
            _pose.Add(pose);
            return _start.Count - 1;
        }

        /// <summary>Builds the mesh with every part at its pose. <paramref name="pad"/> grows the bounds on each side so
        /// moving parts stay inside them (the per-frame upload never recalculates bounds).</summary>
        public Mesh Build(string name, bool dynamic, Vector3 pad)
        {
            _baseV = _srcV.ToArray();
            _baseN = _srcN.ToArray();
            _v = new Vector3[_baseV.Length];
            _n = new Vector3[_baseN.Length];
            for (int p = 0; p < _start.Count; p++) Write(p);
            var m = new Mesh { name = name };
            if (_v.Length > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.SetTriangles(_tri, 0);
            m.RecalculateBounds();
            Bounds b = m.bounds;
            b.Expand(pad * 2f);
            m.bounds = b;
            if (dynamic) m.MarkDynamic();
            Mesh = m;
            _dirty = false;
            return m;
        }

        /// <summary>Moves one part (allocation-free). The change reaches the GPU on the next <see cref="Upload"/>.</summary>
        public void Pose(int part, Matrix4x4 pose)
        {
            _pose[part] = pose;
            if (_v == null) return;
            Write(part);
            _dirty = true;
        }

        public Matrix4x4 PartPose(int part) => _pose[part];
        /// <summary>First vertex of a part in <see cref="Mesh"/> (tests read a part's vertices back).</summary>
        public int PartStart(int part) => _start[part];
        public int PartVertexCount(int part) => _count[part];

        /// <summary>Uploads the rewritten vertices if any part moved since the last upload.</summary>
        public void Upload()
        {
            if (!_dirty || Mesh == null) return;
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            Mesh.SetVertices(_v, 0, _v.Length, flags);
            Mesh.SetNormals(_n, 0, _n.Length, flags);
            _dirty = false;
        }

        void Write(int part)
        {
            Matrix4x4 m = _pose[part];
            int s = _start[part], e = s + _count[part];
            for (int i = s; i < e; i++)
            {
                _v[i] = m.MultiplyPoint3x4(_baseV[i]);
                _n[i] = m.MultiplyVector(_baseN[i]);
            }
        }
    }

    /// <summary>The deck's colour atlas: one 6 x 6 cell (one ASTC 6x6 block) per colour, sRGB, no mipmaps (a texture
    /// provider bakes it as Lessons/deck_palette).</summary>
    public static class DeckPalette
    {
        /// <summary>"deck_palette2": the looper added the LOOP cap's cell. A new name, so a project that still has the old
        /// 6-colour bake (Generated/ is git-ignored and does not travel with a merge) bakes this one instead of reading the old.</summary>
        public const string TextureName = "Lessons/deck_palette2";
        public const int Cell = 6, Cells = 8;
        public const int Ebony = 0, Walnut = 1, Pearl = 2, Rec = 3, Play = 4, Stop = 5, LoopCap = 6;

        /// <summary>Ebony #15110e, the walnut map's mean #442616, pearl #eae2cf; the peg colours;
        /// the LOOP peg a warm brass-amber #b8782e (not a REC red, not a PLAY green).</summary>
        public static readonly Color32[] Colors =
        {
            new Color32(0x15, 0x11, 0x0e, 0xff), new Color32(0x44, 0x26, 0x16, 0xff), new Color32(0xea, 0xe2, 0xcf, 0xff),
            new Color32(0xb3, 0x1b, 0x1b, 0xff), new Color32(0x2b, 0x8a, 0x3e, 0xff), new Color32(0x2a, 0x2a, 0x2e, 0xff),
            new Color32(0xb8, 0x78, 0x2e, 0xff),
        };

        /// <summary>The UV at the centre of a cell (never on a cell border, so filtering cannot mix two colours).</summary>
        public static Vector2 Uv(int cell) => new Vector2((cell * Cell + Cell * 0.5f) / (Cells * Cell), 0.5f);

        public static Texture2D Texture() => GeneratedAssets.Texture(TextureName, Generate);

        public static Texture2D Generate()
        {
            var t = new Texture2D(Cells * Cell, Cell, TextureFormat.RGBA32, false, false)
            {
                name = TextureName, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[Cells * Cell * Cell];
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cells * Cell; x++)
                {
                    int c = x / Cell;
                    px[y * Cells * Cell + x] = c < Colors.Length ? Colors[c] : new Color32(0xff, 0x00, 0xff, 0xff);
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }
    }

    /// <summary>Lets the editor bake the palette (TextureBaker discovers providers by reflection; no Editor edit).</summary>
    public sealed class LessonsTextures : ITextureBakeProvider
    {
        public IEnumerable<TextureBakeSpec> Textures
        {
            get
            {
                yield return new TextureBakeSpec
                {
                    Name = DeckPalette.TextureName, Generate = DeckPalette.Generate,
                    Linear = false, Mipmaps = false, Repeat = false, HasAlpha = false,
                };
            }
        }
    }
}
