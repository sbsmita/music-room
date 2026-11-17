// The harp in the scene (frame, 22 strings, hover, desktop paths). Builds the meshes once, owns the
// HarpPlay logic and draws its result. Draw budget harp <= 6 (ARCHITECTURE.md sections 14, 15.3, 17):
//   - the frame is drawn as 4 meshes combined per material (maple, spruce, walnut, brass pins); the 12 frame parts stay
//     as invisible renderers (forceRenderingOff) so Rig.Handles still gives Layout one oriented box per part;
//   - the 22 strings are ONE mesh in the custom MusicRoom/HarpStrings shader: base colour per vertex, glow and alpha in
//     TEXCOORD1, CPU deformation of the moving strings written into one NativeArray and uploaded with ONE SetVertices per
//     frame; no MaterialPropertyBlock anywhere (SRP Batcher);
//   - E8 (experience.md section 10): the mesh holds THREE copies of every string, the cores first (so they draw first),
//     then the upper and the lower edge of each string's blur; a ringing string's core fades and its edges spread to the
//     envelope of its motion (HarpBlur), alpha-blended in the transparent queue: still one draw;
//   - the contact shadow is NOT built here: the Room module draws every rig's floor blob (RigShadows, the harp's
//     07 rigDecor.harpBlob entry, MusicRoom/FloorDecal), so there is exactly one under the harp (integration).
// Desktop (section 0): a box collider on Layers.PlaySurface spans the 1.0 x 1.4 hit plane; a left press whose
// nearest PlaySurface hit is that collider plucks the nearest string within 2 cm and the drag sweeps the strings
// (DesktopRigFallback.dragBlockMask already keeps the camera from looking around on that press). The mouse HAND's
// pointers never play (HarpPlay skips Pointer.FromMouse). Implements IInstrument (Kind Harp, TickOrder 20) and IHarpApi;
// Core calls Tick and Hover in the Instruments phase, so play logic never runs from Unity callbacks.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Harp
{
    public sealed class HarpInstrument : MonoBehaviour, IInstrument, IHarpApi
    {
        public const int HarpTickOrder = 20;
        /// <summary>Rig homes in the seat frame (ARCHITECTURE.md section 8).</summary>
        public static readonly Vector3 HomeAll = new Vector3(-0.78f, 0f, -0.2f);
        public const float HomeYawAll = 83f, HomeYawSingle = 180f, SingleFront = 0.45f, SingleFallbackX = -0.33f;
        public const int RigPriority = 2;
        /// <summary>The desktop hit plane's thickness (the plane has none; a box needs some).</summary>
        public const float HitThickness = 0.002f;
        /// <summary>How far a desktop click ray looks for play surfaces (the room is 6.4 x 5.2 m).</summary>
        public const float ClickReach = 20f;

        static readonly Color Brass = new Color32(0xcf, 0xa5, 0x4e, 0xff);
        /// <summary>harpString / harpStringC / harpStringF (sRGB hex; written to the vertices as linear).</summary>
        public static readonly Color[] StringColours = { new Color32(0xf1, 0xe9, 0xd6, 0xff), new Color32(0xc5, 0x23, 0x23, 0xff), new Color32(0x1f, 0x3f, 0xa6, 0xff) };
        /// <summary>Frame draw groups, one combined mesh each (HarpWood order).</summary>
        public const int FrameDraws = 4;
        // The strings move a few millimetres: the mesh bounds are fixed 2 cm larger, so uploads skip the bounds pass and
        // the strings are never culled while they swing (the same margin as a len/2 + .02 bounding sphere).
        const MeshUpdateFlags Upload = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        App _app;
        HarpPlay _play;
        EngineSound _sound;
        IRecorder _recorder;
        bool _torndown;

        readonly List<Renderer> _handles = new List<Renderer>(12);
        readonly List<Renderer> _played = new List<Renderer>(1);
        readonly List<Mesh> _meshes = new List<Mesh>(20);
        readonly MeshRenderer[] _frameDraws = new MeshRenderer[FrameDraws];
        Material _maple, _spruce, _walnut, _brass, _stringsMaterial;
        BoxCollider _hit;

        // The one strings mesh: per string its vertex range, string-local rest shape and placement (rotation about z).
        Mesh _stringsMesh;
        MeshRenderer _stringsRenderer;
        NativeArray<Vector3> _verts;
        NativeArray<Vector4> _glowUv;   // xyz the glow (linear RGB), w the vertex alpha (E8 blur)
        readonly int[] _start = new int[HarpGeometry.StringCount];
        readonly Vector3[][] _rest = new Vector3[HarpGeometry.StringCount][];
        readonly int[][] _ringOf = new int[HarpGeometry.StringCount][];   // each rest vertex's envelope ring (0 .. 16)
        readonly Vector3[] _pos = new Vector3[HarpGeometry.StringCount];
        readonly Quaternion[] _rot = new Quaternion[HarpGeometry.StringCount];
        readonly Color[] _shownGlow = new Color[HarpGeometry.StringCount];
        int _vertexCount;
        bool _glowDirty;

        // Desktop: the mouse becomes a pointer that lives on the harp's plane while the button is held.
        sealed class MouseOwner : IPointerOwner
        {
            public Pointer[] Own;
            public IReadOnlyList<Pointer> Pointers => Own;
            public double LastPlayMs { get; set; } = double.NegativeInfinity;
        }
        MouseOwner _mouseOwner;
        Pointer _mouse;
        bool _mouseCaptured;
        Camera _camera;

        public Rig Rig { get; private set; }
        /// <summary>The play logic (tests, diagnostics).</summary>
        public HarpPlay Play => _play;
        /// <summary>The desktop mouse pointer (inactive unless a click on the strings is being dragged).</summary>
        public Pointer MousePointer => _mouse;
        /// <summary>Always null in the integrated app: Room's RigShadows draws the harp's contact shadow.</summary>
        public Transform Blob => null;
        /// <summary>The desktop click target (Layers.PlaySurface).</summary>
        public BoxCollider HitCollider => _hit;
        /// <summary>The one mesh holding all 22 strings (harp-local vertices) and its renderer.</summary>
        public Mesh StringsMesh => _stringsMesh;
        public MeshRenderer StringsRenderer => _stringsRenderer;
        /// <summary>The 4 renderers that draw the frame (maple, spruce, walnut, brass); the handles are not drawn.</summary>
        public IReadOnlyList<MeshRenderer> FrameDrawRenderers => _frameDraws;
        /// <summary>String i's first CORE vertex in the strings mesh (its edge copies: EdgeStart).</summary>
        public int VertexStart(int i) => i >= 0 && i < _start.Length ? _start[i] : -1;
        /// <summary>E8: string i's first vertex of its upper (true) or lower blur edge in the strings mesh.</summary>
        public int EdgeStart(int i, bool upper) => i >= 0 && i < _start.Length ? _start[i] + (upper ? 1 : 2) * _vertexCount : -1;
        /// <summary>E8: vertices of one copy of all 22 strings (the mesh holds three).</summary>
        public int CopyVertexCount => _vertexCount;
        /// <summary>E8: the alpha last written to string i's core (true) or edge vertices of ring j (0 .. 16).</summary>
        public float ShownAlpha(int i, int ring, bool core)
        {
            if (i < 0 || i >= _start.Length || !_glowUv.IsCreated) return 0f;
            int[] rings = _ringOf[i];
            for (int k = 0; k < rings.Length; k++)
                if (rings[k] == ring) return _glowUv[core ? _start[i] + k : _start[i] + k + _vertexCount].w;
            return 0f;
        }
        public int VerticesPerString => _rest[0] != null ? _rest[0].Length : 0;
        /// <summary>String i's rest vertex k in harp-local space (what the mesh holds when the string is still).</summary>
        public Vector3 RestLocal(int i, int k) => _pos[i] + _rot[i] * _rest[i][k];
        /// <summary>The glow last written to string i's vertices (linear RGB).</summary>
        public Color ShownGlow(int i) => i >= 0 && i < _shownGlow.Length ? _shownGlow[i] : Color.clear;

        // ---------------- build ----------------

        public void Setup(App app)
        {
            _app = app;
            _play = new HarpPlay();
            IAudioEngine audio = app.Get<IAudioEngine>();
            if (audio != null)
            {
                _sound = new EngineSound(audio);
                _play.Sound = _sound;
            }
            BuildMaterials();
            BuildFrame();
            BuildStrings();
            // No BuildBlob(): Room's RigShadows draws the harp's contact shadow (one blob, on the floor, FloorDecal shader).
            BuildHitPlane();
            _mouseOwner = new MouseOwner();
            _mouse = Pointer.CreateExternal("harp-mouse", _mouseOwner, false, false);
            _mouseOwner.Own = new[] { _mouse };

            float mx = HarpGeometry.MeanStringMidX();
            if (float.IsNaN(mx)) mx = SingleFallbackX;
            Rig = new Rig
            {
                Id = RigId.Harp,
                Group = transform,
                Zone = new StringsZone(this),
                Priority = RigPriority,
                HomeAll = HomeAll, HomeYawAll = HomeYawAll,
                // Single mode: the strings centred on the player (mean string-midpoint x), FRONT .45 ahead, turned 180.
                HomeSingle = new Vector3(mx, 0f, SingleFront), HomeYawSingle = HomeYawSingle,
                // The 12 frame parts (pillar, neck, soundbox, soundboard, rib, cap, base, feet, pins), filled once in
                // BuildFrame; never the strings, the blob or the hit plane.
                Handles = _handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IHarpApi>(this);
            audio?.SetSourceParent(SourceId.Harp, transform, HarpGeometry.SourceLocal);
        }

        void BuildMaterials()
        {
            MaterialLibrary lib = _app.Materials;
            bool textured = !_app.Headless;
            // URP Lit: smoothness = 1 - roughness (maple .35, spruce .3, walnut .8; clear coat has no Quest
            // equivalent). The grain is the foundation's shared set (Core/wood_*, baked by Tools/unity.sh bake).
            _maple = Wood(lib, HarpWood.Maple, 0.65f, textured);
            _walnut = Wood(lib, HarpWood.Walnut, 0.2f, textured);
            _spruce = Wood(lib, HarpWood.Spruce, 0.7f, textured);
            _brass = lib.Clone(MaterialLibrary.LitMetal);
            _brass.name = "Harp brass";
            _brass.SetColor(MaterialLibrary.BaseColor, Brass);
            _brass.SetFloat(MaterialLibrary.Metallic, 1f);
            _brass.SetFloat(MaterialLibrary.Smoothness, 0.7f);
            // One material for all 22 strings: their colours and glows are vertex data (HarpMaterials).
            if (!lib.HasTemplate(HarpMaterials.StringsTemplate))
                Debug.LogWarning("[Harp] template '" + HarpMaterials.StringsTemplate + "' missing (run Tools/unity.sh render-setup): strings draw untinted");
            _stringsMaterial = lib.Clone(lib.HasTemplate(HarpMaterials.StringsTemplate) ? HarpMaterials.StringsTemplate : MaterialLibrary.LitOpaque);
            _stringsMaterial.name = "Harp strings";
        }

        /// <summary>The harp woods: the wood texture repeats 2 (maple, walnut) or 4 (spruce) times.</summary>
        public static float Repeat(HarpWood wood) => wood == HarpWood.Spruce ? 4f : 2f;

        public static string SharedWood(HarpWood wood) =>
            wood == HarpWood.Spruce ? SharedTextures.WoodSpruce : wood == HarpWood.Walnut ? SharedTextures.WoodWalnut : SharedTextures.WoodMaple;

        static Material Wood(MaterialLibrary lib, HarpWood wood, float smoothness, bool textured)
        {
            Texture2D tex = textured ? SharedTextures.Get(SharedWood(wood)) : null;
            Material m = lib.Lit(tex != null ? Color.white : FlatWood(wood), smoothness, 0f, tex);
            m.name = "Harp " + wood;
            if (tex != null)
            {
                float r = Repeat(wood);
                m.SetTextureScale(MaterialLibrary.BaseMap, new Vector2(r, r));
            }
            return m;
        }

        /// <summary>The mid tone of each wood, used untextured (headless boots skip textures).</summary>
        static Color FlatWood(HarpWood wood) =>
            wood == HarpWood.Spruce ? new Color(0.88f, 0.74f, 0.52f) : wood == HarpWood.Walnut ? new Color(0.27f, 0.15f, 0.085f) : new Color(0.80f, 0.62f, 0.40f);

        Material MaterialFor(HarpWood wood) =>
            wood == HarpWood.Maple ? _maple : wood == HarpWood.Spruce ? _spruce : wood == HarpWood.Walnut ? _walnut : _brass;

        void BuildFrame()
        {
            var root = new GameObject("Frame").transform;
            root.SetParent(transform, false);
            HarpPart[] parts = HarpGeometry.BuildFrame();
            var groups = new MeshData[FrameDraws];
            var casts = new bool[FrameDraws];
            for (int i = 0; i < parts.Length; i++)
            {
                HarpPart p = parts[i];
                // The part itself: an invisible renderer whose transform and mesh bounds are one grab handle / footprint
                // box for Layout (forceRenderingOff: no draw, but bounds, transform and mesh stay valid).
                var go = new GameObject(p.Name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = p.LocalPosition;
                go.transform.localRotation = p.LocalRotation;
                go.transform.localScale = p.LocalScale;
                Mesh mesh = p.Mesh.ToMesh("Harp " + p.Name);
                _meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MaterialFor(p.Material);
                r.forceRenderingOff = true;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _handles.Add(r);
                // Its copy in the material's combined draw mesh, placed in frame-local space (the frame root is at the
                // harp's origin), normals by the inverse transpose (the soundboard is scaled 4x through the plane).
                int w = (int)p.Material;
                MeshData placed = p.Mesh.Clone().Transform(p.LocalPosition, p.LocalRotation, p.LocalScale);
                groups[w] = groups[w] == null ? placed : groups[w].Append(placed);
                // A group casts (MR only; no realtime shadows in VR) if any of its parts casts (HarpPart.CastShadows).
                casts[w] |= p.CastShadows;
            }
            for (int w = 0; w < FrameDraws; w++)
            {
                if (groups[w] == null) continue;
                var go = new GameObject("Draw " + (HarpWood)w);
                go.transform.SetParent(root, false);
                Mesh mesh = groups[w].ToMesh("Harp frame " + (HarpWood)w);
                _meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MaterialFor((HarpWood)w);
                r.shadowCastingMode = casts[w] ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = false;
                _frameDraws[w] = r;
            }
        }

        void BuildStrings()
        {
            int total = 0;
            var datas = new MeshData[HarpGeometry.StringCount];
            for (int i = 0; i < datas.Length; i++)
            {
                datas[i] = HarpGeometry.StringMesh(i, out _pos[i], out _rot[i]);
                _rest[i] = datas[i].Positions.ToArray();
                _start[i] = total;
                total += _rest[i].Length;
            }
            _vertexCount = total;
            int all = total * 3;   // E8: cores, upper edges, lower edges
            var positions = new Vector3[all];
            var normals = new Vector3[all];
            var colours = new Color[all];
            var glow = new Vector4[all];
            var indices = new List<int>(all * 6);
            for (int copy = 0; copy < 3; copy++)
            {
                int off = copy * total;
                for (int i = 0; i < datas.Length; i++)
                {
                    MeshData d = datas[i];
                    Color c = StringColours[(int)HarpGeometry.ColourOf(i)].linear;
                    for (int k = 0; k < d.Positions.Count; k++)
                    {
                        positions[off + _start[i] + k] = _pos[i] + _rot[i] * d.Positions[k];
                        normals[off + _start[i] + k] = _rot[i] * d.Normals[k];
                        colours[off + _start[i] + k] = copy == 0 ? c : new Color(c.r, c.g, c.b, 0f);   // alpha: the core flag (depth pass)
                        glow[off + _start[i] + k] = new Vector4(0f, 0f, 0f, copy == 0 ? 1f : 0f);   // a still string: core only
                    }
                    for (int k = 0; k < d.Indices.Count; k++) indices.Add(d.Indices[k] + _start[i] + off);
                }
            }
            for (int i = 0; i < datas.Length; i++)
            {
                _shownGlow[i] = Color.black;
                float invLen = 1f / HarpGeometry.Strings[i].Length;
                _ringOf[i] = new int[_rest[i].Length];
                for (int k = 0; k < _rest[i].Length; k++) _ringOf[i][k] = HarpPlay.RingOf(_rest[i][k], invLen);
            }
            _verts = new NativeArray<Vector3>(positions, Allocator.Persistent);
            _glowUv = new NativeArray<Vector4>(glow, Allocator.Persistent);

            _stringsMesh = new Mesh { name = "Harp strings" };
            _stringsMesh.MarkDynamic();
            _stringsMesh.SetVertices(_verts);
            _stringsMesh.SetNormals(normals);
            _stringsMesh.SetColors(colours);
            _stringsMesh.SetUVs(1, _glowUv);
            _stringsMesh.SetTriangles(indices, 0, true);
            Bounds b = _stringsMesh.bounds;
            b.Expand(0.04f);
            _stringsMesh.bounds = b;
            _meshes.Add(_stringsMesh);

            var go = new GameObject("Strings");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _stringsMesh;
            _stringsRenderer = go.AddComponent<MeshRenderer>();
            _stringsRenderer.sharedMaterial = _stringsMaterial;
            _stringsRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _stringsRenderer.receiveShadows = false;
            _played.Add(_stringsRenderer);
        }

        /// <summary>The invisible 1.0 x 1.4 plane at (-.33, .72) in the string plane, as the desktop click
        /// target on Layers.PlaySurface (section 0). Not drawn (no renderer), so it is neither a handle nor a played part.</summary>
        void BuildHitPlane()
        {
            var go = new GameObject("StringsHitPlane") { layer = Layers.PlaySurface };
            go.transform.SetParent(transform, false);
            Rect r = HarpGeometry.DesktopPlane;
            go.transform.localPosition = new Vector3(r.center.x, r.center.y, 0f);
            _hit = go.AddComponent<BoxCollider>();
            _hit.size = new Vector3(r.width, r.height, HitThickness);
        }

        // ---------------- IInstrument ----------------

        public InstrumentKind Kind => InstrumentKind.Harp;
        public int TickOrder => HarpTickOrder;
        public bool AcceptsStickPointers => false;
        /// <summary>In the scene and visible (Layout deactivates the group when the harp leaves the layout).</summary>
        public bool IsPresent => this != null && isActiveAndEnabled && (Rig == null || Rig.InScene);
        public Transform Root => transform;
        /// <summary>The strings renderer (one mesh); never a handle.</summary>
        public IReadOnlyList<Renderer> PlayedParts => _played;
        /// <summary>The frame parts' invisible renderers (Rig.Handles): the grab handles and the placement footprint.</summary>
        public IReadOnlyList<Renderer> FrameRenderers => _handles;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            if (_play == null) return;
            PrepareClocks();
            Pointer mouse = UpdateDesktopPointer();
            _play.Update(pointers, mouse, transform.worldToLocalMatrix, dt);
            Present();
        }

        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            if (_play == null || glow == null) return;
            Matrix4x4 w2l = transform.worldToLocalMatrix;
            if (pointers != null) for (int k = 0; k < pointers.Count; k++) HoverPointer(pointers[k], w2l, glow);
            HoverPointer(_mouse, w2l, glow);
        }

        void HoverPointer(Pointer p, in Matrix4x4 w2l, HoverGlowPool glow)
        {
            // The mouse hand's fingertip glows too (visual feedback only: it never plucks).
            if (p == null || !p.Active) return;
            if (!_play.HoverPick(w2l.MultiplyPoint3x4(p.Tip), out int i, out float u, out float strength)) return;
            HarpString s = _play.Strings[i];
            // Like a key or a drum head: a glow on the string under the tip, brighter as it comes to the strings.
            Vector3 pos = transform.TransformPoint(Vector3.LerpUnclamped(s.A, s.B, u));
            Vector3 axis = transform.TransformDirection((s.B - s.A).normalized);
            Vector3 normal = transform.TransformDirection(Vector3.back); // right-handed normal (0, 0, 1) = Unity -z
            HoverGlowSpec spec = HoverGlowSpec.At(pos, normal, HarpPlay.HoverLength, HarpPlay.HoverWidth, strength);
            spec.Axis = axis;
            glow.Add(spec);
        }

        public PlaySurface PlaySurfaceAt(Vector3 world) => IsPresent && OverStrings(world) ? PlaySurface.HarpStrings : PlaySurface.None;

        /// <summary>OverStrings: |z| &lt;= .05 and within .04 (in the plane) of a string segment.</summary>
        public bool OverStrings(Vector3 world) => _play != null && _play.OverStrings(transform.InverseTransformPoint(world));

        void PrepareClocks()
        {
            _play.NowMs = _app.Clock.NowMs;
            if (_recorder == null) _recorder = _app.Get<IRecorder>(); // Lessons (Order 40) registers after the harp
            _play.Recorder = _recorder;
        }

        /// <summary>Writes the shapes Animate changed (core and both blur edges) into the one vertex array and uploads it
        /// once, then the glows and the blur's alphas.</summary>
        void Present()
        {
            if (_stringsMesh == null) return;
            HarpString[] strings = _play.Strings;
            bool shape = false;
            for (int i = 0; i < strings.Length; i++)
            {
                HarpString s = strings[i];
                if (s.ShapeDirty)
                {
                    s.ShapeDirty = false;
                    HarpPlay.Deform(s, _rest[i], _verts, _start[i], _pos[i], _rot[i]);
                    HarpPlay.DeformEdge(s, _rest[i], _verts, _start[i] + _vertexCount, _pos[i], _rot[i], true);
                    HarpPlay.DeformEdge(s, _rest[i], _verts, _start[i] + 2 * _vertexCount, _pos[i], _rot[i], false);
                    shape = true;
                }
                if (s.BlurDirty)
                {
                    s.BlurDirty = false;
                    WriteAlpha(i, s);
                }
                Paint(i);
            }
            if (shape) _stringsMesh.SetVertices(_verts, 0, 3 * _vertexCount, Upload);
            FlushGlow();
        }

        /// <summary>E8: per ring, the core fades and the edges appear as the swing widens (HarpBlur.Alpha).</summary>
        void WriteAlpha(int i, HarpString s)
        {
            int a = _start[i];
            int[] rings = _ringOf[i];
            for (int k = 0; k < rings.Length; k++)
            {
                int j = rings[k];
                HarpBlur.Alpha(s.Up[j], s.Lo[j], s.Radius, out float core, out float edge);
                SetAlpha(a + k, core);
                SetAlpha(a + k + _vertexCount, edge);
                SetAlpha(a + k + 2 * _vertexCount, edge);
            }
            _glowDirty = true;
        }

        void SetAlpha(int v, float alpha)
        {
            Vector4 g = _glowUv[v];
            g.w = alpha;
            _glowUv[v] = g;
        }

        /// <summary>Paint: string i's glow into the vertices of its three copies (CPU side; FlushGlow uploads once
        /// per frame), keeping their alphas.</summary>
        void Paint(int i)
        {
            if (i < 0 || i >= _shownGlow.Length || !_glowUv.IsCreated) return;
            Color g = _play.Glow(_play.Strings[i]);
            if (g == _shownGlow[i]) return;
            _shownGlow[i] = g;
            int n = _rest[i].Length;
            for (int copy = 0; copy < 3; copy++)
            {
                int a = _start[i] + copy * _vertexCount;
                for (int k = 0; k < n; k++)
                {
                    Vector4 v = _glowUv[a + k];
                    v.x = g.r; v.y = g.g; v.z = g.b;
                    _glowUv[a + k] = v;
                }
            }
            _glowDirty = true;
        }

        void FlushGlow()
        {
            if (!_glowDirty || _stringsMesh == null) return;
            _glowDirty = false;
            _stringsMesh.SetUVs(1, _glowUv, 0, 3 * _vertexCount, Upload);
        }

        /// <summary>Glows set after the harp's Tick (a lesson's Highlight in the Tutorial phase, a recorder playback pluck)
        /// reach the GPU in the same frame. Visual only, so a Unity callback is allowed (section 4).</summary>
        void LateUpdate() => FlushGlow();

        // ---------------- IHarpApi ----------------

        public int StringCount => HarpGeometry.StringCount;
        public int MidiOf(int i) => i >= 0 && i < HarpGeometry.StringCount ? HarpGeometry.MidiOf(i) : -1;
        public Vector3 StringStart(int i) => i >= 0 && i < HarpGeometry.StringCount ? _play.Strings[i].A : Vector3.zero;
        public Vector3 StringEnd(int i) => i >= 0 && i < HarpGeometry.StringCount ? _play.Strings[i].B : Vector3.zero;
        public Vector3 PlayPoint(int i) => HarpGeometry.PlayPoint(i);

        public void Highlight(int i, float k = 1f)
        {
            if (_play == null) return;
            _play.Highlight(i, k);
            Paint(i);
        }

        public void PlaybackPluck(int i, float vel, float u = 0.35f, float speed = 0f, string mode = HarpPlay.ModeThrough)
        {
            if (_play == null) return;
            PrepareClocks();
            if (_play.PlaybackPluck(i, vel, u, speed, HarpPlay.ParseMode(mode))) Paint(i);
        }

        public void PlaybackDamp(int i)
        {
            if (_play == null) return;
            PrepareClocks();
            _play.PlaybackDamp(i);
        }

        // ---------------- desktop ----------------

        /// <summary>Section 3.3: desktop-only paths run while the app is not presenting (never gated on IsHeadset).</summary>
        bool DesktopMode => _app != null && !_app.IsPresenting;

        /// <summary>Tests: a scripted mouse (button and world ray) replaces the live one for the desktop path, which
        /// batchmode cannot press. The press edge is the button going down between two Ticks.</summary>
        public bool ScriptedMouse;
        public bool ScriptedButton;
        public Ray ScriptedRay;
        bool _scriptedWasDown;

        Pointer UpdateDesktopPointer()
        {
            if (!DesktopMode)
            {
                // Presenting (headset or simulated): the mouse never plays.
                if (_mouseCaptured) EndDesktopDrag();
                _scriptedWasDown = ScriptedMouse && ScriptedButton;
                StepMouse();
                return _mouse;
            }
            bool held, pressed;
            if (ScriptedMouse)
            {
                held = ScriptedButton;
                pressed = held && !_scriptedWasDown;
                _scriptedWasDown = held;
            }
            else
            {
                held = DesktopInput.Mouse(0);
                // Pointer down: Shift/Alt drags move or turn a rig (Layout), never play.
                pressed = DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt;
            }
            if (!_mouseCaptured)
            {
                if (pressed && TryMouseRay(out Ray ray)) DesktopPress(ray);
            }
            else if (!held) EndDesktopDrag();
            else if (TryMouseRay(out Ray ray) && RayOnPlane(ray, out Vector3 world, out _)) _mouse.Next = world;
            StepMouse();
            return _mouse;
        }

        bool TryMouseRay(out Ray ray)
        {
            if (ScriptedMouse)
            {
                ray = ScriptedRay;
                return true;
            }
            if (_camera == null)
            {
                _camera = _app.Rig != null ? _app.Rig.HeadCamera : null;
                if (_camera == null) _camera = Camera.main;
            }
            if (_camera == null) { ray = default; return false; }
            ray = _camera.ScreenPointToRay(DesktopInput.MousePosition);
            return true;
        }

        /// <summary>Where a world ray meets the harp's string plane (local z = 0) inside the 1.0 x 1.4 hit rectangle: the
        /// drag follows the mouse on this plane, even past the strings' ends.</summary>
        public bool RayOnPlane(Ray worldRay, out Vector3 world, out float distance)
        {
            world = default;
            distance = 0f;
            Vector3 o = transform.InverseTransformPoint(worldRay.origin);
            Vector3 d = transform.InverseTransformVector(worldRay.direction);
            if (Mathf.Abs(d.z) < 1e-7f) return false;
            float t = -o.z / d.z;
            if (t <= 0f) return false;
            Vector3 l = o + d * t;
            if (!HarpGeometry.DesktopPlane.Contains(new Vector2(l.x, l.y))) return false;
            world = transform.TransformPoint(l);
            distance = Vector3.Distance(worldRay.origin, world);
            return true;
        }

        /// <summary>Pointer down on the harp + HarpPlay.DesktopDown: when the nearest PlaySurface collider under the
        /// ray is the harp's own hit plane, the click plucks the nearest string within 2 cm and the mouse then sweeps the
        /// strings like a fingertip on the plane until the button is released. Returns false when another module's
        /// play surface is nearer (that module owns the click) or nothing of the harp is hit.</summary>
        public bool DesktopPress(Ray worldRay)
        {
            if (_play == null || _hit == null) return false;
            // A press is rare: bring the colliders up to date with rigs Layout moved this frame.
            Physics.SyncTransforms();
            if (!Physics.Raycast(worldRay, out RaycastHit hit, ClickReach, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return false;
            if (hit.collider != _hit) return false;
            Vector3 local = transform.InverseTransformPoint(hit.point);
            local.z = 0f; // the plane itself, not the face of its 2 mm box
            Vector3 world = transform.TransformPoint(local);
            PrepareClocks();
            int plucked = _play.DesktopDown(local);
            if (plucked >= 0) Paint(plucked);
            _mouse.Next = _mouse.Tip = _mouse.Prev = world;
            _mouse.Active = false;
            _mouse.Down = true;
            _mouseCaptured = true;
            return true;
        }

        /// <summary>Moves the captured desktop pointer to where a ray meets the plane (tests; the drag does this each frame).</summary>
        public bool DesktopDrag(Ray worldRay)
        {
            if (!_mouseCaptured || !RayOnPlane(worldRay, out Vector3 world, out _)) return false;
            _mouse.Next = world;
            return true;
        }

        public void EndDesktopDrag()
        {
            _mouseCaptured = false;
            if (_mouse != null) _mouse.Down = false;
        }

        public bool DesktopCaptured => _mouseCaptured;

        /// <summary>The external mouse pointer: tip from Next while down, speed over the learned gap.</summary>
        void StepMouse()
        {
            Pointer p = _mouse;
            double nowS = _app.Clock.Now;
            float dt = _app.Clock.Dt;
            p.WasActive = p.Active;
            p.Prev = p.Tip;
            p.Active = false;
            if (!p.Down)
            {
                p.Vel = Vector3.zero;
                p.Speed = 0f;
                return;
            }
            p.Tip = p.Next;
            p.Active = true;
            if (p.WasActive && (p.Tip - p.Prev).sqrMagnitude > 1e-10f)
            {
                double gap = Math.Max(nowS - p.SampleT, 0.004);
                if (gap < 0.2) p.SampleDt = p.SampleDt > 0f ? p.SampleDt + 0.25f * ((float)gap - p.SampleDt) : (float)gap;
                p.SampleT = nowS;
                p.Vel = (p.Tip - p.Prev) / Mathf.Max(p.SampleDt > 0f ? p.SampleDt : dt, 1e-3f);
            }
            else
            {
                if (!p.WasActive) { p.SampleT = nowS; p.Prev = p.Tip; }
                p.Vel = Vector3.zero;
            }
            p.Speed = p.Vel.magnitude;
        }

        // ---------------- lifetime ----------------

        void OnDisable()
        {
            // Leaving the scene: held strings go back silently and the next visit starts every dwell afresh.
            _play?.LeaveAll();
            if (_mouseCaptured) EndDesktopDrag();
        }

        /// <summary>HarpModule.Shutdown: release the meshes and the vertex buffers this instrument made.</summary>
        public void Teardown()
        {
            if (_torndown) return;
            _torndown = true;
            for (int i = 0; i < _meshes.Count; i++)
            {
                if (_meshes[i] == null) continue;
                if (Application.isPlaying) Destroy(_meshes[i]);
                else DestroyImmediate(_meshes[i]);
            }
            _meshes.Clear();
            _stringsMesh = null;
            if (_verts.IsCreated) _verts.Dispose();
            if (_glowUv.IsCreated) _glowUv.Dispose();
        }

        void OnDestroy() => Teardown();

        sealed class StringsZone : IPlayZone
        {
            readonly HarpInstrument _harp;
            public StringsZone(HarpInstrument harp) { _harp = harp; }
            public bool Contains(Vector3 world) => _harp != null && _harp.OverStrings(world);
        }
    }
}
