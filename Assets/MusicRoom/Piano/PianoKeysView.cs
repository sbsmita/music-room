// The 88 keys and their labels on screen (the key meshes, writeKey, writeLabel).
// Draw-call budget (ARCHITECTURE.md 17: whole scene <= 150) rules out 88 renderers with MaterialPropertyBlocks (they
// also break the SRP Batcher), so the keys are two dynamic meshes (white keys, black keys) and
// one 88-quad label mesh: 3 draw calls. Each key keeps its spec transform exactly (pivot at its hinge (x, 0, back L),
// rotation Euler(-theta, 0, 0), theta = depth * MAX_ANGLE), applied on the CPU to that key's vertices only when its depth
// changed, and uploaded per run of dirty keys (Mesh.SetVertexBufferData): neighbours share one call, keys far apart (two
// hands, A0 and C8 springing back) get one call each instead of re-sending every key between them. The side shading and the
// per-key darkening ride in uv on the 2 x 2 shade palette (PianoTextures); the label tint rides in vertex colour
// (Particles/Lit multiplies it in).
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Piano
{
    public sealed class PianoKeysView : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct KeyVertex { public Vector3 Pos; public Vector3 Normal; public Vector2 Uv; }

        [StructLayout(LayoutKind.Sequential)]
        public struct LabelVertex { public Vector3 Pos; public Vector3 Normal; public Color32 Color; public Vector2 Uv; }

        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        public readonly Mesh WhiteMesh, BlackMesh, LabelMesh;
        readonly PianoAction _action;
        readonly KeyShape _white, _black;
        NativeArray<KeyVertex> _wv, _bv;
        NativeArray<LabelVertex> _lv;
        // The open dirty run of each mesh (first..last vertex, -1 = none). A run is flushed when the next dirty key is more
        // than one clean key away: re-sending a single clean key costs less than another call, a long gap does not.
        // The labels are 36 B a vertex (12.7 KB for all 88), so they always go as one span.
        int _wMin = int.MaxValue, _wMax = -1, _bMin = int.MaxValue, _bMax = -1, _lMin = int.MaxValue, _lMax = -1;
        /// <summary>SetVertexBufferData calls since creation.</summary>
        public int Uploads { get; private set; }
        /// <summary>Vertices sent by the last Sync (all three meshes): the bytes the bus carries are this times 32 / 36.</summary>
        public int UploadedVertices { get; private set; }

        /// <summary>One key body in its pivot frame: positions, normals and the side grey of every vertex (shadeKeySides).</summary>
        sealed class KeyShape
        {
            public Vector3[] Pos, Nrm;
            public float[] Grey;
            public int[] Tris;
            public int Count => Pos.Length;
        }

        public PianoKeysView(PianoAction action)
        {
            _action = action;
            // Pivot frame = the hinge at the key's rear top; the key reaches forward (toward the player, -z) from it.
            // White: RoundedBox(WW, H, L, 2 segments, 2.2 mm) spanning y -0.02..0, z -L..0.
            _white = MakeShape(new Vector3(PianoSpec.WW, PianoSpec.H, PianoSpec.L), PianoSpec.WhiteRound, new Vector3(0f, -PianoSpec.H / 2f, -PianoSpec.L / 2f));
            // Black: RoundedBox(BW, 0.016, BL, 2, 2.5 mm) spanning y -0.004..0.012, z -BL..0.
            _black = MakeShape(new Vector3(PianoSpec.BW, PianoSpec.BlackH, PianoSpec.BL), PianoSpec.BlackRound, new Vector3(0f, PianoSpec.BTOP - PianoSpec.BlackH / 2f, -PianoSpec.BL / 2f));
            WhiteMesh = MakeKeyMesh("PianoWhiteKeys", _white, PianoSpec.WhiteCount, out _wv);
            BlackMesh = MakeKeyMesh("PianoBlackKeys", _black, PianoSpec.BlackCount, out _bv);
            LabelMesh = MakeLabelMesh(out _lv);
            for (int i = 0; i < action.Keys.Length; i++) WriteKey(action.Keys[i]);
            Upload();
        }

        static KeyShape MakeShape(Vector3 size, float radius, Vector3 centre)
        {
            var b = new PianoMeshBuilder();
            b.AddRoundedBox(size, 2, radius, centre, PianoMeshBuilder.BoxUv.Fixed(Vector2.zero));
            var s = new KeyShape { Pos = b.Positions.ToArray(), Nrm = b.Normals.ToArray(), Tris = b.Indices.ToArray() };
            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in s.Pos) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            s.Grey = new float[s.Pos.Length];
            for (int i = 0; i < s.Pos.Length; i++) s.Grey[i] = PianoTextures.SideShade(s.Pos[i].y, ymin, ymax, s.Nrm[i]);
            return s;
        }

        static Mesh MakeKeyMesh(string name, KeyShape shape, int keys, out NativeArray<KeyVertex> verts)
        {
            int vc = shape.Count * keys;
            verts = new NativeArray<KeyVertex>(vc, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vc,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            var tris = new ushort[shape.Tris.Length * keys];
            for (int k = 0; k < keys; k++)
                for (int i = 0; i < shape.Tris.Length; i++) tris[k * shape.Tris.Length + i] = (ushort)(k * shape.Count + shape.Tris[i]);
            mesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(tris, 0, 0, tris.Length, Quiet);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), Quiet);
            // Room for the key travel: fixed bounds 2 cm larger, never recomputed.
            mesh.bounds = new Bounds(new Vector3(0f, -0.01f, PianoSpec.L / 2f), new Vector3(2f * (-PianoSpec.X0) + 0.04f, 0.06f, PianoSpec.L + 0.04f));
            return mesh;
        }

        Mesh MakeLabelMesh(out NativeArray<LabelVertex> verts)
        {
            int n = PianoSpec.KeyCount;
            verts = new NativeArray<LabelVertex>(n * 4, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var mesh = new Mesh { name = "PianoKeyLabels" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(n * 4,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            var tris = new ushort[n * 6];
            for (int i = 0; i < n; i++)
            {
                // Corners: front-left, front-right, rear-right, rear-left; (0, 2, 1), (0, 3, 2) face up in Unity's winding.
                int v = i * 4, o = i * 6;
                tris[o] = (ushort)v; tris[o + 1] = (ushort)(v + 2); tris[o + 2] = (ushort)(v + 1);
                tris[o + 3] = (ushort)v; tris[o + 4] = (ushort)(v + 3); tris[o + 5] = (ushort)(v + 2);
            }
            mesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(tris, 0, 0, tris.Length, Quiet);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), Quiet);
            mesh.bounds = new Bounds(new Vector3(0f, 0f, PianoSpec.L / 2f), new Vector3(2f * (-PianoSpec.X0) + 0.04f, 0.06f, PianoSpec.L + 0.04f));
            // uv: every quad points at its key's cell, once (mapLabels); text top toward the hinge.
            for (int i = 0; i < n; i++)
            {
                var key = _action.Keys[i];
                Vector4 uv = PianoLabelAtlas.CellUv(PianoLabelAtlas.CellOf(key.Midi));
                int v = i * 4;
                verts[v] = new LabelVertex { Uv = new Vector2(uv.x, uv.y), Normal = Vector3.up, Color = new Color32(255, 255, 255, 255) };
                verts[v + 1] = new LabelVertex { Uv = new Vector2(uv.z, uv.y), Normal = Vector3.up, Color = new Color32(255, 255, 255, 255) };
                verts[v + 2] = new LabelVertex { Uv = new Vector2(uv.z, uv.w), Normal = Vector3.up, Color = new Color32(255, 255, 255, 255) };
                verts[v + 3] = new LabelVertex { Uv = new Vector2(uv.x, uv.w), Normal = Vector3.up, Color = new Color32(255, 255, 255, 255) };
            }
            return mesh;
        }

        /// <summary>The label quad of a key in the piano frame at a hinge angle (writeLabel): corners swing about the hinge.</summary>
        public static void LabelCorners(PianoKey k, float angle, Vector3[] out4)
        {
            var q = k.Black ? PianoLabelAtlas.BlackQuad : PianoLabelAtlas.WhiteQuad;
            float top = k.Top + PianoLabelAtlas.LabelLift;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            for (int i = 0; i < 4; i++)
            {
                float zr = i < 2 ? q.Front : q.Rear;
                float x = k.X + (i == 0 || i == 3 ? -q.W : q.W) / 2f;
                // pivot frame (x, top, -zr) rotated by Euler(-angle) about x, then + (0, 0, L): y = c top - s zr, back = L - s top - c zr.
                out4[i] = new Vector3(x, c * top - s * zr, PianoSpec.L - s * top - c * zr);
            }
        }

        readonly Vector3[] _corners = new Vector3[4];

        /// <summary>writeKey: the key's transform and tint, and its label's corners and tint, into the vertex arrays.</summary>
        public void WriteKey(PianoKey k)
        {
            float angle = k.Depth * k.MaxAngle;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float shade = k.Shade;
            var shape = k.Black ? _black : _white;
            var arr = k.Black ? _bv : _wv;
            int first = k.Inst * shape.Count;
            for (int i = 0; i < shape.Count; i++)
            {
                Vector3 p = shape.Pos[i], n = shape.Nrm[i];
                // Euler(-theta, 0, 0): y' = y cos + z sin, z' = -y sin + z cos (the front goes down), pivot at (x, 0, L).
                arr[first + i] = new KeyVertex
                {
                    Pos = new Vector3(k.X + p.x, p.y * c + p.z * s, PianoSpec.L - p.y * s + p.z * c),
                    Normal = new Vector3(n.x, n.y * c + n.z * s, -n.y * s + n.z * c),
                    Uv = PianoTextures.ShadeUv(shape.Grey[i], shade),
                };
            }
            if (k.Black) Mark(BlackMesh, _bv, ref _bMin, ref _bMax, first, first + shape.Count - 1, shape.Count);
            else Mark(WhiteMesh, _wv, ref _wMin, ref _wMax, first, first + shape.Count - 1, shape.Count);

            LabelCorners(k, angle, _corners);
            byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
            var col = new Color32(g, g, g, 255);
            Vector3 up = new Vector3(0f, c, -s);   // the key top's normal after the same rotation
            int v = k.Index * 4;
            for (int i = 0; i < 4; i++)
            {
                var lv = _lv[v + i];
                lv.Pos = _corners[i];
                lv.Normal = up;
                lv.Color = col;
                _lv[v + i] = lv;
            }
            _lMin = Mathf.Min(_lMin, v);
            _lMax = Mathf.Max(_lMax, v + 3);
            k.Dirty = false;
        }

        /// <summary>Writes every key the logic marked dirty this frame and uploads the touched vertex ranges.</summary>
        public void Sync()
        {
            UploadedVertices = 0;
            var keys = _action.Keys;
            for (int i = 0; i < keys.Length; i++) if (keys[i].Dirty) WriteKey(keys[i]);
            Upload();
        }

        /// <summary>Adds first..last to the mesh's open run, first sending the open run when the new range is more than
        /// <paramref name="gap"/> clean vertices away from it (either side: WriteKey may be called in any order).</summary>
        void Mark<T>(Mesh mesh, NativeArray<T> arr, ref int min, ref int max, int first, int last, int gap) where T : struct
        {
            if (max >= 0 && (first > max + 1 + gap || last < min - 1 - gap)) Flush(mesh, arr, ref min, ref max);
            if (first < min) min = first;
            if (last > max) max = last;
        }

        void Flush<T>(Mesh mesh, NativeArray<T> arr, ref int min, ref int max) where T : struct
        {
            if (max < 0) return;
            mesh.SetVertexBufferData(arr, min, min, max - min + 1, 0, Quiet);
            Uploads++;
            UploadedVertices += max - min + 1;
            min = int.MaxValue;
            max = -1;
        }

        void Upload()
        {
            Flush(WhiteMesh, _wv, ref _wMin, ref _wMax);
            Flush(BlackMesh, _bv, ref _bMin, ref _bMax);
            Flush(LabelMesh, _lv, ref _lMin, ref _lMax);
        }

        /// <summary>A key vertex as written (tests: the key transform).</summary>
        public Vector3 KeyVertexPosition(PianoKey k, int vertex) => (k.Black ? _bv : _wv)[k.Inst * (k.Black ? _black : _white).Count + vertex].Pos;
        public Vector2 KeyVertexUv(PianoKey k, int vertex) => (k.Black ? _bv : _wv)[k.Inst * (k.Black ? _black : _white).Count + vertex].Uv;
        public int VerticesPerKey(bool black) => (black ? _black : _white).Count;
        public Vector3 LabelCorner(PianoKey k, int corner) => _lv[k.Index * 4 + corner].Pos;
        public Color32 LabelColour(PianoKey k) => _lv[k.Index * 4].Color;
        public Vector2 LabelUv(PianoKey k, int corner) => _lv[k.Index * 4 + corner].Uv;

        public void Dispose()
        {
            if (_wv.IsCreated) _wv.Dispose();
            if (_bv.IsCreated) _bv.Dispose();
            if (_lv.IsCreated) _lv.Dispose();
            DestroyMesh(WhiteMesh);
            DestroyMesh(BlackMesh);
            DestroyMesh(LabelMesh);
        }

        static void DestroyMesh(Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(m);
            else UnityEngine.Object.DestroyImmediate(m);   // EditMode tests
        }
    }
}
