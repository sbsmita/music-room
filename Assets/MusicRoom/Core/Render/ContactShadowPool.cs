// E3 (10-upgrade/experience.md section 5): the soft contact shadows as ONE dynamic mesh of up to 32 blob quads (10
// fingertips + 10 distal joints + 2 stick tips need 22), uploaded with one SetVertexBufferData from a NativeArray each
// frame something shows, drawn with the MusicRoom/ContactShadow template (multiply blend, queue 2991): one draw call, the
// renderer off when no blob is active, no MaterialPropertyBlock (ARCHITECTURE.md 14). Fixed 10 m bounds around the head
// (MeshUpdateFlags.DontRecalculateBounds). Allocation-free after construction.
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Core
{
    public sealed class ContactShadowPool
    {
        public const int Capacity = 32;
        public const string Template = "ContactShadow";
        /// <summary>The template's queue (Transparent-9): after the opaques and the printed key labels (2990), so a shadow
        /// darkens the sticker under a finger too; before the menu face (2999), glass (3000) and the additive glows (3010).
        /// The spec's 2980 drew every label sticker bright inside the shadow (Builds/Shots/exp-hands/shadow-on-label).</summary>
        public const int Queue = 2991;
        public const float MaxDark = 0.6f;
        /// <summary>Blobs whose centre is higher than this over the plane are skipped (peak darkness 1.6 % of A).</summary>
        public const float HMax = 0.06f;
        public const float QuadK = 2.5f, QuadMinR = 1.5f, QuadMax = 0.08f;
        /// <summary>h_eff floor (x r): avoids the singularity when tracking puts the centre at the plane.</summary>
        public const float HMinOverR = 0.8f;
        /// <summary>The quad floats this far out of the surface (with Offset -1, -1 in the shader).</summary>
        public const float Lift = 0.0005f;
        public const float NoClip = 1e3f;
        public const float BoundsSize = 10f;

        /// <summary>One vertex: position (local to the pool's root), uv0 = rho (m) from the blob centre, uv1 = (h_eff, r, A,
        /// clipHalf), uv2 = the clip's centre line along the tangent (m from the blob centre).</summary>
        public struct Vertex
        {
            public Vector3 Position;
            public Vector2 Rho;
            public Vector4 Blob;
            public float ClipCentre;
        }

        public struct Blob
        {
            public Vector3 Centre;   // world, on the receiving plane (before the 0.5 mm lift)
            public Vector3 Normal;
            public float Half;       // quad half-size (m)
            public float HEff, Radius, Ambient, ClipHalf;
            /// <summary>The clip's centre line along the quad's tangent, metres from Centre (0 = centred on the blob).</summary>
            public float ClipCentre;
        }

        static readonly VertexAttributeDescriptor[] s_layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 1),
        };

        readonly bool _headless;
        readonly Transform _root;
        readonly MeshRenderer _renderer;
        readonly Mesh _mesh;
        NativeArray<Vertex> _verts;
        readonly Blob[] _blobs = new Blob[Capacity];
        int _count;
        bool _disposed;

        /// <summary>Blobs added since Begin (tests read it after End too).</summary>
        public int Count => _count;
        /// <summary>Blobs drawn by the last End.</summary>
        public int Drawn { get; private set; }
        /// <summary>Blobs refused because the pool was full (since Begin).</summary>
        public int Dropped { get; private set; }
        public Blob Get(int i) => _blobs[i];
        /// <summary>The one renderer of every blob (null when headless). Enabled only while a blob shows.</summary>
        public Renderer Renderer => _renderer;
        public Material Material { get; }

        public ContactShadowPool(MaterialLibrary materials, Transform parent, bool headless)
        {
            _headless = headless || materials == null;
            if (_headless) return;
            if (!materials.HasTemplate(Template))
            {
                // The library's fallback would be an opaque Lit quad: no shadows rather than grey squares.
                Debug.LogWarning("[Core] template ContactShadow missing (run Tools/unity.sh render-setup): no contact shadows");
                _headless = true;
                return;
            }
            var go = new GameObject("ContactShadows");
            _root = go.transform;
            _root.SetParent(parent, false);
            _mesh = new Mesh { name = "ContactShadowBlobs" };
            _mesh.MarkDynamic();
            _mesh.SetVertexBufferParams(Capacity * 4, s_layout);
            _mesh.SetIndexBufferParams(Capacity * 6, IndexFormat.UInt16);
            var idx = new NativeArray<ushort>(Capacity * 6, Allocator.Temp);
            for (int q = 0; q < Capacity; q++)
            {
                int b = q * 4, t = q * 6;
                idx[t] = (ushort)b; idx[t + 1] = (ushort)(b + 2); idx[t + 2] = (ushort)(b + 1);
                idx[t + 3] = (ushort)b; idx[t + 4] = (ushort)(b + 3); idx[t + 5] = (ushort)(b + 2);
            }
            _mesh.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            idx.Dispose();
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), Flags);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            _verts = new NativeArray<Vertex>(Capacity * 4, Allocator.Persistent);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            Material = materials.Clone(Template);
            Material.renderQueue = Queue;
            _renderer.sharedMaterial = Material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.enabled = false;
        }

        const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
                                      | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        /// <summary>The analytic sphere shadow darkness at horizontal distance rho (the experience reference's sphereShadow).</summary>
        public static float Darkness(float rho, float h, float r, float ambient)
        {
            float he = Mathf.Max(h, HMinOverR * r);
            return Mathf.Min(MaxDark, ambient * r * r * he / Mathf.Pow(he * he + rho * rho, 1.5f));
        }

        /// <summary>The quad half-size: max(2.5 h_eff, 1.5 r), at most 0.08 m (at 2.5 h the AO is 5 % of its peak).</summary>
        public static float QuadHalf(float h, float r) => Mathf.Min(QuadMax, Mathf.Max(QuadK * Mathf.Max(h, HMinOverR * r), QuadMinR * r));

        /// <summary>The distance where the darkness falls to half its peak: h sqrt(2^(2/3) - 1) = 0.766 h.</summary>
        public static float HalfWidth(float h) => h * Mathf.Sqrt(Mathf.Pow(2f, 2f / 3f) - 1f);

        public void Begin()
        {
            _count = 0;
            Dropped = 0;
        }

        /// <summary>Adds the shadow of a sphere of radius <paramref name="r"/> whose centre is <paramref name="h"/> over the
        /// plane at <paramref name="centre"/> (world, the foot of the sphere centre). Tangent zero = any orientation, no clip.
        /// False when it is too high (h &gt; HMax) or the pool is full.</summary>
        public bool Add(Vector3 centre, Vector3 normal, Vector3 tangent, float clipHalf, float h, float r, float ambient) =>
            Add(centre, normal, tangent, clipHalf, h, r, ambient, centre);

        /// <summary>As above, clipped to clipHalf either side of the line through <paramref name="clipOrigin"/> along the
        /// normal (a pressed key's centre line), not of the blob centre: a press off the key's middle keeps its shadow on
        /// that key, without a hard cut inside it or a spill past its other edge.</summary>
        public bool Add(Vector3 centre, Vector3 normal, Vector3 tangent, float clipHalf, float h, float r, float ambient, Vector3 clipOrigin)
        {
            if (h > HMax || r <= 0f) return false;
            if (_count >= Capacity) { Dropped++; return false; }
            float he = Mathf.Max(h, HMinOverR * r);
            ref Blob b = ref _blobs[_count];
            b.Centre = centre;
            b.Normal = normal;
            b.Half = QuadHalf(h, r);
            b.HEff = he;
            b.Radius = r;
            b.Ambient = ambient;
            b.ClipHalf = clipHalf > 0f ? clipHalf : NoClip;
            b.ClipCentre = clipHalf > 0f ? Vector3.Dot(QuadTangent(normal, tangent), clipOrigin - centre) : 0f;
            if (!_headless) WriteQuad(_count, centre, normal, tangent, b);
            _count++;
            return true;
        }

        /// <summary>The quad's in-plane x axis (the clip axis): the tangent made perpendicular to n, or any axis in the plane.</summary>
        public static Vector3 QuadTangent(Vector3 n, Vector3 tangent)
        {
            Vector3 t = tangent - n * Vector3.Dot(tangent, n);
            if (t.sqrMagnitude < 1e-8f)
            {
                t = Mathf.Abs(n.x) < 0.9f ? Vector3.Cross(n, Vector3.right) : Vector3.Cross(n, Vector3.forward);
            }
            return t.normalized;
        }

        void WriteQuad(int q, Vector3 centre, Vector3 n, Vector3 tangent, in Blob b)
        {
            Vector3 t = QuadTangent(n, tangent);
            Vector3 bt = Vector3.Cross(n, t);
            Matrix4x4 toLocal = _root.worldToLocalMatrix;
            Vector3 c = centre + n * Lift;
            float s = b.Half;
            var blob = new Vector4(b.HEff, b.Radius, b.Ambient, b.ClipHalf);
            int v = q * 4;
            float cc = b.ClipCentre;
            _verts[v] = new Vertex { Position = toLocal.MultiplyPoint3x4(c - t * s - bt * s), Rho = new Vector2(-s, -s), Blob = blob, ClipCentre = cc };
            _verts[v + 1] = new Vertex { Position = toLocal.MultiplyPoint3x4(c + t * s - bt * s), Rho = new Vector2(s, -s), Blob = blob, ClipCentre = cc };
            _verts[v + 2] = new Vertex { Position = toLocal.MultiplyPoint3x4(c + t * s + bt * s), Rho = new Vector2(s, s), Blob = blob, ClipCentre = cc };
            _verts[v + 3] = new Vertex { Position = toLocal.MultiplyPoint3x4(c - t * s + bt * s), Rho = new Vector2(-s, s), Blob = blob, ClipCentre = cc };
        }

        /// <summary>Uploads this frame's blobs (one call) and shows the renderer, or hides it when there are none.
        /// <paramref name="boundsCentre"/>: world point the fixed 10 m bounds sit around (the head).</summary>
        public void End(Vector3 boundsCentre)
        {
            Drawn = _count;
            if (_headless || _disposed) return;
            if (_count == 0)
            {
                if (_renderer.enabled) _renderer.enabled = false;
                return;
            }
            _mesh.SetVertexBufferData(_verts, 0, 0, _count * 4, 0, Flags);
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, _count * 6)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize), firstVertex = 0, vertexCount = _count * 4,
            }, Flags);
            _mesh.bounds = new Bounds(_root.InverseTransformPoint(boundsCentre), Vector3.one * BoundsSize);
            if (!_renderer.enabled) _renderer.enabled = true;
        }

        /// <summary>World corner i (0..3) of blob quad q as last written (tests).</summary>
        public Vector3 Corner(int q, int i) => _root != null ? _root.TransformPoint(_verts[q * 4 + i].Position) : Vector3.zero;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_verts.IsCreated) _verts.Dispose();
            if (_root != null) Object.Destroy(_root.gameObject);
            if (_mesh != null) Object.Destroy(_mesh);
        }
    }
}
