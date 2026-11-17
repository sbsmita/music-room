// The ghost hand's solid parts (palm, four fingers, thumb, charging ring, ghost row plate, arrow cone) merged into ONE
// dynamic mesh, so Sunny stays inside its draw budget (ARCHITECTURE.md sections 14 and 17: Sunny <= 6 draws;
// eight separate meshes would make 11-12 draws during a demo). The parts keep their own Transforms as anchors (SunnyView
// poses them exactly as before); each frame the demo is shown, their vertices are written into the shared mesh in the
// Demo group's space, with each part's opacity in the vertex alpha (SunnyUnlit multiplies by the vertex colour).
// Additive blending makes the merge invisible: the sum does not depend on draw order, and overlaps still add up as they
// did with separate meshes. First five: the hand is alpha-blended pale and warm (SunnyView), drawn as ONE surface (depth
// written, so a finger behind the palm adds nothing), and its edges fade (Eye: each hand vertex's alpha falls off where
// the surface turns away from the player), so it reads as a soft pale hand up close rather than a box with sticks. The ring and row plate are double-sided; here they carry a reversed copy
// of their triangles so the whole mesh can cull back faces like the closed solids.
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Guide
{
    public sealed class SunnyDemoMesh
    {
        public const int PartCount = 9;
        public const int Palm = 0, Finger0 = 1, Thumb = 5, Ring = 6, Row = 7, Cone = 8;
        /// <summary>Generous fixed bounds in the Demo group's space (the hand sweeps 0.3 m, the arrow sits below it), so the
        /// per-frame vertex upload never recalculates them.</summary>
        public static readonly Bounds LocalBounds = new Bounds(new Vector3(0.07f, -0.06f, -0.17f), new Vector3(0.6f, 0.4f, 0.4f));
        /// <summary>The world demos reach a deck control, the drums on the right or the harp on the left, up to ~1 m from
        /// Sunny (DemoPose.Wide; first five: 2.6 m, was 1.4).</summary>
        public static readonly Bounds WideBounds = new Bounds(Vector3.zero, new Vector3(2.6f, 2.6f, 2.6f));

        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        public Mesh Mesh { get; }
        public int VertexCount => _vertices.Length;
        /// <summary>The alpha each part was last written with (0..1), for the tests.</summary>
        public float PartAlpha(int part) => _alpha[part] / 255f;

        readonly Transform _space;
        readonly Transform[] _anchors = new Transform[PartCount];
        readonly Vector3[][] _source = new Vector3[PartCount][];
        readonly int[] _start = new int[PartCount];
        readonly byte[] _alpha = new byte[PartCount];
        readonly Vector3[] _vertices;
        readonly Color32[] _colors;
        readonly Vector3[][] _normals = new Vector3[PartCount][];

        /// <summary>First five: the player's eye (world) for the soft edges; null: flat alpha.</summary>
        public Vector3? Eye;
        /// <summary>The alpha a hand vertex keeps where the surface is seen edge-on (1 where it faces the player).</summary>
        public const float EdgeAlpha = 0.2f;

        /// <param name="space">The Demo group: the mesh's renderer sits on it.</param>
        /// <param name="anchors">Palm, finger 0..3, thumb, ring, row plate, cone (each posed as its own part).</param>
        /// <param name="meshes">The part shapes (SunnyMeshes), in the same order.</param>
        public SunnyDemoMesh(Transform space, Transform[] anchors, Mesh[] meshes)
        {
            _space = space;
            int vertexCount = 0, indexCount = 0;
            for (int i = 0; i < PartCount; i++)
            {
                _anchors[i] = anchors[i];
                _source[i] = meshes[i].vertices;   // once per session
                _normals[i] = meshes[i].normals;
                _start[i] = vertexCount;
                vertexCount += _source[i].Length;
                indexCount += meshes[i].triangles.Length * (DoubleSided(i) ? 2 : 1);
            }
            _vertices = new Vector3[vertexCount];
            _colors = new Color32[vertexCount];
            var uv = new Vector2[vertexCount];   // the material's map is white: any uv reads the same
            var tris = new int[indexCount];
            int k = 0;
            for (int i = 0; i < PartCount; i++)
            {
                int[] t = meshes[i].triangles;
                int o = _start[i];
                for (int j = 0; j < t.Length; j++) tris[k++] = t[j] + o;
                if (!DoubleSided(i)) continue;
                for (int j = 0; j < t.Length; j += 3)
                {
                    tris[k++] = t[j] + o;
                    tris[k++] = t[j + 2] + o;
                    tris[k++] = t[j + 1] + o;
                }
            }
            for (int i = 0; i < vertexCount; i++) _colors[i] = new Color32(255, 255, 255, 0);
            Mesh = new Mesh { name = "SunnyDemoSolid" };
            Mesh.MarkDynamic();
            Mesh.vertices = _vertices;
            Mesh.uv = uv;
            Mesh.colors32 = _colors;
            Mesh.triangles = tris;
            Mesh.bounds = LocalBounds;
        }

        static bool DoubleSided(int part) => part == Ring || part == Row;

        /// <summary>Writes every part at its anchor's current pose with its opacity (0 hides it). Allocation-free.</summary>
        public void Write(float handOpacity, float ringOpacity, float rowOpacity, float coneOpacity)
        {
            Matrix4x4 toSpace = _space.worldToLocalMatrix;
            bool soft = Eye.HasValue;
            Vector3 eye = soft ? toSpace.MultiplyPoint3x4(Eye.Value) : default;
            for (int i = 0; i < PartCount; i++)
            {
                float a = i == Ring ? ringOpacity : i == Row ? rowOpacity : i == Cone ? coneOpacity : handOpacity;
                byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                Vector3[] src = _source[i];
                int o = _start[i];
                bool edges = soft && i < Ring && alpha > 0 && _normals[i] != null && _normals[i].Length == src.Length;
                if (alpha != _alpha[i] || edges)
                {
                    _alpha[i] = alpha;
                    var c = new Color32(255, 255, 255, alpha);
                    if (!edges) for (int j = 0; j < src.Length; j++) _colors[o + j] = c;
                }
                if (alpha == 0) continue;   // hidden: its old vertices add nothing
                Matrix4x4 m = toSpace * _anchors[i].localToWorldMatrix;
                if (!edges)
                {
                    for (int j = 0; j < src.Length; j++) _vertices[o + j] = m.MultiplyPoint3x4(src[j]);
                    continue;
                }
                Vector3[] nrm = _normals[i];
                for (int j = 0; j < src.Length; j++)
                {
                    Vector3 p = m.MultiplyPoint3x4(src[j]);
                    _vertices[o + j] = p;
                    Vector3 nn = m.MultiplyVector(nrm[j]);
                    Vector3 to = eye - p;
                    float d = Vector3.Dot(nn, to);
                    float l = nn.sqrMagnitude * to.sqrMagnitude;
                    float facing = l > 1e-16f ? Mathf.Abs(d) / Mathf.Sqrt(l) : 1f;
                    facing = facing * (2f - facing);                                    // a soft shoulder, not a hard rim
                    _colors[o + j] = new Color32(255, 255, 255, (byte)(alpha * (EdgeAlpha + (1f - EdgeAlpha) * facing)));
                }
            }
            Mesh.SetVertices(_vertices, 0, _vertices.Length, Quiet);
            Mesh.SetColors(_colors, 0, _colors.Length, Quiet);
        }
    }
}
