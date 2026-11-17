// Hover glows (spec 05 section 6): a pool of 16 additive soft-dot quads. Instruments, the tutorial and the sticks add
// glows each frame in call order; overflow is dropped. Look: additive #ffc46a (or the spec's Tint), alpha (.2 + .6 strength) x
// (steady ? 1 : .9 + .1 sin 7t) x gain, double-sided, no depth write, drawn after the transparents.
// All 16 quads are ONE dynamic mesh (vertex positions + vertex colours rewritten each frame, the renderer off when no
// glow is shown): one draw call, no MaterialPropertyBlock (which would take the renderers out of the SRP Batcher).
// The material is the ParticlesUnlitAdditive template (URP Particles/Unlit multiplies the vertex colour; URP Unlit
// ignores it). Vertex colours are not converted to linear by Unity, so the glow colour is written linear.
// 10-upgrade/experience.md E3: where a contact shadow shows, ContactVisuals dims a glow (Dim, draw only: the spec the
// instrument asked for stays as it was) to HoverGlowGainWithShadow, so the realistic cue leads.
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Core
{
    public struct HoverGlowSpec
    {
        public Vector3 Position;     // world
        public Vector3 Normal;       // world, the surface normal (quad faces along it)
        public Vector3 Axis;         // world, local X of the quad (zero = pick x, or z if |x.n| > .9)
        public float Length, Width;  // metres
        public float Strength;       // 0..1
        public float Gain;           // 1 default; piano uses .45
        public bool Steady;          // no pulse
        public Color Tint;           // sRGB; alpha 0 (default) = the hover amber #ffc46a (the tutorial's cue: LessonCue purple)

        public static HoverGlowSpec At(Vector3 pos, Vector3 normal, float length, float width, float strength, float gain = 1f, bool steady = false)
            => new HoverGlowSpec { Position = pos, Normal = normal, Length = length, Width = width, Strength = strength, Gain = gain, Steady = steady };
    }

    public sealed class HoverGlowPool
    {
        public const int Capacity = 16;
        public static readonly Color GlowColor = new Color32(0xff, 0xc4, 0x6a, 0xff);

        readonly HoverGlowSpec[] _specs = new HoverGlowSpec[Capacity];
        readonly float[] _dim = new float[Capacity];
        readonly bool _headless;
        readonly Transform _root;
        readonly MeshRenderer _renderer;
        readonly Mesh _mesh;
        readonly Vector3[] _verts = new Vector3[Capacity * 4];
        readonly Color[] _colors = new Color[Capacity * 4];
        readonly Color _linear;
        int _count;
        /// <summary>Glows added this frame (tests read this before Update clears it).</summary>
        public int Pending => _count;
        public int Dropped { get; private set; }
        public int Drawn { get; private set; }
        public HoverGlowSpec Spec(int i) => _specs[i];
        /// <summary>The draw multiplier of glow i this frame (1, or less where a contact shadow dims it).</summary>
        public float DimOf(int i) => _dim[i];
        /// <summary>The one renderer of all glows (null when headless). Enabled only while a glow is shown.</summary>
        public Renderer Renderer => _renderer;

        public HoverGlowPool(MaterialLibrary materials, Transform parent, bool headless)
        {
            _headless = headless;
            _linear = GlowColor.linear;
            if (headless) return;
            var go = new GameObject("HoverGlow");
            _root = go.transform;
            _root.SetParent(parent, false);
            _mesh = BuildMesh();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            var mat = materials.Clone(MaterialLibrary.ParticlesAdditive);
            // The tutorial blocks draw before the glows (block order 4, glow order 5): the glow goes
            // on top of a waiting block. Blocks are Lessons/Block at queue 3005 (after glass 3000, snow 3002, frost 3004).
            mat.renderQueue = 3010;
            mat.SetColor(MaterialLibrary.BaseColor, Color.white);
            mat.SetTexture(MaterialLibrary.BaseMap, SharedTextures.Get(SharedTextures.SoftDot));
            _renderer.sharedMaterial = mat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        static Mesh BuildMesh()
        {
            var m = new Mesh { name = "HoverGlowQuads" };
            m.MarkDynamic();
            var v = new Vector3[Capacity * 4];
            var uv = new Vector2[Capacity * 4];
            var c = new Color[Capacity * 4];
            var tris = new int[Capacity * 12];
            for (int q = 0; q < Capacity; q++)
            {
                int b = q * 4;
                uv[b] = new Vector2(0f, 0f); uv[b + 1] = new Vector2(1f, 0f); uv[b + 2] = new Vector2(1f, 1f); uv[b + 3] = new Vector2(0f, 1f);
                int t = q * 12;
                // Both faces (as TextureUtil.UnitQuad): 0 2 1, 0 3 2 and 0 1 2, 0 2 3.
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
                tris[t + 6] = b; tris[t + 7] = b + 1; tris[t + 8] = b + 2;
                tris[t + 9] = b; tris[t + 10] = b + 2; tris[t + 11] = b + 3;
            }
            m.vertices = v;
            m.uv = uv;
            m.colors = c;
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return m;
        }

        /// <summary>Returns false when the pool is full (the glow is dropped).</summary>
        public bool Add(in HoverGlowSpec spec)
        {
            if (_count >= Capacity) { Dropped++; return false; }
            _dim[_count] = 1f;
            _specs[_count++] = spec;
            return true;
        }

        /// <summary>Multiplies the drawn strength of pending glow i by k this frame (the spec itself is unchanged).</summary>
        public void Dim(int i, float k)
        {
            if (i >= 0 && i < _count) _dim[i] = Mathf.Min(_dim[i], k);
        }

        /// <summary>Called by App in the HoverGlow phase; t = Clock.Now seconds.</summary>
        public void Update(float t)
        {
            Drawn = _count;
            if (!_headless && _mesh != null)
            {
                if (_count == 0)
                {
                    if (_renderer.enabled) _renderer.enabled = false;
                }
                else
                {
                    Matrix4x4 toLocal = _root.worldToLocalMatrix;
                    Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                    for (int i = 0; i < Capacity; i++)
                    {
                        int b = i * 4;
                        if (i >= _count)
                        {
                            _verts[b] = _verts[b + 1] = _verts[b + 2] = _verts[b + 3] = Vector3.zero; // degenerate: nothing drawn
                            _colors[b] = _colors[b + 1] = _colors[b + 2] = _colors[b + 3] = Color.clear;
                            continue;
                        }
                        ref HoverGlowSpec s = ref _specs[i];
                        Vector3 n = s.Normal.sqrMagnitude > 1e-8f ? s.Normal.normalized : Vector3.up;
                        Vector3 a = s.Axis;
                        if (a.sqrMagnitude < 1e-8f) a = Mathf.Abs(Vector3.Dot(Vector3.right, n)) > 0.9f ? Vector3.forward : Vector3.right;
                        a = a - n * Vector3.Dot(a, n); // a' = normalize(axis - n (axis . n))
                        if (a.sqrMagnitude < 1e-8f) a = Vector3.Cross(n, Vector3.up);
                        a.Normalize();
                        // The quad's local X is a', local Y is cross(n, a') and it faces along n (LookRotation(n, cross(n, a'))).
                        Vector3 y = Vector3.Cross(n, a);
                        Vector3 c = s.Position + n * 0.002f;
                        Vector3 hx = a * (0.5f * s.Length), hy = y * (0.5f * s.Width);
                        _verts[b] = toLocal.MultiplyPoint3x4(c - hx - hy);
                        _verts[b + 1] = toLocal.MultiplyPoint3x4(c + hx - hy);
                        _verts[b + 2] = toLocal.MultiplyPoint3x4(c + hx + hy);
                        _verts[b + 3] = toLocal.MultiplyPoint3x4(c - hx + hy);
                        for (int k = 0; k < 4; k++) { min = Vector3.Min(min, _verts[b + k]); max = Vector3.Max(max, _verts[b + k]); }
                        float pulse = s.Steady ? 1f : 0.9f + 0.1f * Mathf.Sin(7f * t);
                        float alpha = (0.2f + 0.6f * Mathf.Clamp01(s.Strength)) * pulse * s.Gain * _dim[i];
                        Color lin = s.Tint.a > 0f ? s.Tint.linear : _linear;
                        var col = new Color(lin.r, lin.g, lin.b, alpha);
                        _colors[b] = _colors[b + 1] = _colors[b + 2] = _colors[b + 3] = col;
                    }
                    _mesh.SetVertices(_verts);
                    _mesh.SetColors(_colors);
                    _mesh.bounds = new Bounds((min + max) * 0.5f, max - min + Vector3.one * 0.01f);
                    if (!_renderer.enabled) _renderer.enabled = true;
                }
            }
            _count = 0;
            Dropped = 0;
        }

        /// <summary>Corner i (0..3) of glow quad q as last drawn, in world space (tests).</summary>
        public Vector3 Corner(int q, int i) => _root != null ? _root.TransformPoint(_verts[q * 4 + i]) : Vector3.zero;

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            if (_mesh != null) Object.Destroy(_mesh);
        }
    }
}
