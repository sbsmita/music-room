// The side table rig (spec 07, rigDecor.sideTable and switchPanel): a walnut table to
// the player's left carrying the tape deck (the Lessons module parents it at table-local (0.07, 0.6, 0)) and the
// FIRE / WIND / SNOW panel, as one movable Rig (RigId.Table, home (-0.39, 0, -0.21) yaw -90 in the seat frame, priority 3).
// Geometry is built in the right-handed source frame with the spec's numbers and mirrored into the rig's frame. Nothing here casts a
// shadow (ARCHITECTURE.md sections 12, 14, 17: in mixed reality only the instrument bodies cast); the wood and brass
// receive the instruments' shadows there. Draws (the table + deck + switches budget is 15): wood, brass, the contact
// shadow, the three levers (they turn on their own), ONE LED mesh (colour per vertex, LedStrip) and ONE label mesh
// (the three TextMeshPro words baked into a single mesh at build time): 8, leaving 7 for the Lessons deck.
// Rig.Handles = the slab, legs and switch plate (one merged mesh), filled once here (ARCHITECTURE.md section 8: never
// built per call); levers, LEDs, labels and the contact shadow are not handles. The Lessons module may add its deck
// body to that same list in place.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Render;
using MusicRoom.Room.World;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Table
{
    public sealed class SideTable
    {
        public const float TopY = 0.6f;
        public static readonly Vector3 HomeAll = new Vector3(-0.39f, 0f, -0.21f);
        public const float HomeYaw = -90f;
        /// <summary>The panel in the rig frame: source frame (-.19, .6, .02) yaw .12 rad -> Unity (-.19, .6, -.02) yaw -6.8755.</summary>
        public static readonly Vector3 PanelLocal = new Vector3(-0.19f, TopY, -0.02f);
        public const float PanelYawDeg = -6.8755f;

        public Transform Group, Panel;
        public Rig Rig;
        public AmbiencePanel Switches;
        public Renderer Blob;
        public readonly Transform[] Levers = new Transform[3];
        /// <summary>The three LEDs: one mesh and renderer (vertex colours, MusicRoom/VertexColorUnlit).</summary>
        public LedStrip Leds;
        public Renderer LedRenderer;
        /// <summary>FIRE / WIND / SNOW: one mesh in the panel frame drawn with the mobile SDF material (null without TMP).</summary>
        public Renderer LabelRenderer;
        public Material LabelMaterial;
        public readonly Collider[] Hits = new Collider[3];
        public readonly List<Mesh> Meshes = new List<Mesh>(8);
        readonly List<Renderer> _handles = new List<Renderer>(4);

        public static SideTable Build(App app, Transform worldRoot, RoomMaterials mats, MaterialLibrary lib, Action<AmbienceKind, bool> set)
        {
            var t = new SideTable();
            var g = new GameObject("Table");
            g.transform.SetParent(worldRoot, false);
            t.Group = g.transform;

            // Slab, legs and the switch plate: walnutGloss in one mesh; the toggle bases in brass.
            var wood = new Geo();
            wood.Append(PrimitiveMeshes.Box(0.58f, 0.025f, 0.32f), Rh.T(0f, TopY - 0.0125f, 0f));
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                wood.Append(PrimitiveMeshes.Box(0.025f, TopY - 0.025f, 0.025f), Rh.T(sx * 0.265f, (TopY - 0.025f) / 2f, sz * 0.135f));
            Matrix4x4 panel3 = Rh.TRS(new Vector3(-0.19f, TopY, 0.02f), Rh.Euler(0f, 0.12f, 0f));
            wood.Append(PrimitiveMeshes.Box(0.24f, 0.012f, 0.09f), panel3);
            var brass = new Geo();
            for (int i = 0; i < 3; i++) brass.Append(PrimitiveMeshes.Cylinder(0.012f, 0.013f, 0.008f, 20), panel3 * Rh.T(AmbiencePanel.ToggleX[i], 0.01f, 0f));
            var woodR = t.Part(t.Group, "TableWood", t.Keep(wood.ToUnityMesh("TableWood")), mats[RoomMat.WalnutGloss]);
            t.Part(t.Group, "TableBrass", t.Keep(brass.ToUnityMesh("TableBrass")), mats[RoomMat.Brass]);
            // The desktop mouse hand rests on the table top.
            var top = woodR.gameObject.AddComponent<BoxCollider>();
            top.center = new Vector3(0f, TopY - 0.0125f, 0f);
            top.size = new Vector3(0.58f, 0.025f, 0.32f);

            // Contact shadow under the table, a child so it travels with the rig.
            var blob = RoomBuilder.BlobGeo(0.55f, 0.5f, 0.4f).ToUnityMesh("TableBlob");
            t.Keep(blob);
            t.Blob = RoomBuilder.MakeRenderer(t.Group, "Blob", blob, mats[RoomMat.Decal], new Vector3(0f, 0.005f, 0f));

            // The panel frame: levers, LEDs, labels and the desktop colliders hang under it.
            var p = new GameObject("AmbiencePanel").transform;
            p.SetParent(t.Group, false);
            p.localPosition = PanelLocal;
            p.localRotation = Quaternion.Euler(0f, PanelYawDeg, 0f);
            t.Panel = p;
            var lever = new Geo();
            lever.Append(PrimitiveMeshes.Cylinder(0.0035f, 0.0045f, 0.03f, 10), Rh.T(0f, 0.015f, 0f));
            lever.Append(PrimitiveMeshes.Sphere(0.006f, 12, 10), Rh.T(0f, 0.03f, 0f));
            var leverMesh = t.Keep(lever.ToUnityMesh("Lever"));
            var hitMesh = t.Keep(PrimitiveMeshes.Cylinder(AmbiencePanel.HitRadius, AmbiencePanel.HitRadius, AmbiencePanel.HitHeight, 12).ToUnityMesh("SwitchHit"));
            // The three LEDs as one mesh: spheres r .004 at panel-local (x, .012, -.03) (built in the source frame, mirrored).
            var leds = new Geo();
            Geo ledSphere = PrimitiveMeshes.Sphere(0.004f, 10, 8);
            for (int i = 0; i < 3; i++) leds.Append(ledSphere, Rh.T(AmbiencePanel.ToggleX[i], 0.012f, 0.03f));
            leds.WithColor(LedStrip.Off);
            var ledMesh = t.Keep(leds.ToUnityMesh("Leds"));
            ledMesh.MarkDynamic();
            t.LedRenderer = RoomBuilder.MakeRenderer(p, "Leds", ledMesh, mats[RoomMat.Led]);
            t.Leds = new LedStrip(ledMesh, ledSphere.VertexCount);
            for (int i = 0; i < 3; i++)
            {
                float x = AmbiencePanel.ToggleX[i];
                var lv = t.Part(p, "Lever " + AmbiencePanel.Labels[i], leverMesh, mats[RoomMat.Brass], new Vector3(x, 0.014f, 0f));
                t.Levers[i] = lv.transform;
                // Desktop click target (the hit mesh): an invisible cylinder on the PlaySurface layer, which
                // also stops the desktop camera's look-around drag there (DesktopRigFallback.dragBlockMask).
                var hit = new GameObject("Hit " + AmbiencePanel.Labels[i]);
                hit.layer = Layers.PlaySurface;
                hit.transform.SetParent(p, false);
                hit.transform.localPosition = new Vector3(x, AmbiencePanel.HitY, 0f);
                var mc = hit.AddComponent<MeshCollider>();
                mc.sharedMesh = hitMesh;
                mc.convex = true;
                t.Hits[i] = mc;
            }
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            t.LabelMaterial = MakeLabelMaterial(font, lib);
            var labelMesh = BuildLabels(p, font, t.LabelMaterial);
            if (labelMesh != null)
            {
                t.Keep(labelMesh);
                var lr = RoomBuilder.MakeRenderer(p, "Labels", labelMesh, t.LabelMaterial ?? font.material);
                lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                t.LabelRenderer = lr;
            }
            t._handles.Add(woodR);

            t.Rig = new Rig
            {
                Id = RigId.Table, Group = t.Group, Priority = 3, Zone = new TableZone(t.Group),
                HomeAll = HomeAll, HomeYawAll = HomeYaw, HomeSingle = HomeAll, HomeYawSingle = HomeYaw,
                Handles = t._handles,
            };
            t.Switches = new AmbiencePanel(app, p, t.Levers, t.Leds, t.Hits, () => t.Rig, set);
            t.Rig.Instruments.Add(t.Switches);
            return t;
        }

        Mesh Keep(Mesh m) { Meshes.Add(m); return m; }

        /// <summary>A table part: no shadow of its own (MR casters are the instrument bodies only), but it receives theirs.</summary>
        Renderer Part(Transform parent, string name, Mesh mesh, Material mat, Vector3? local = null)
        {
            var r = RoomBuilder.MakeRenderer(parent, name, mesh, mat, local);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            return r;
        }

        static Material MakeLabelMaterial(TMP_FontAsset font, MaterialLibrary lib)
        {
            if (font == null || font.material == null) return null;
            // The mobile SDF shader from the template (kept in the build by its .mat); the font's own material otherwise.
            Material tpl = lib.HasTemplate(RoomTemplates.LabelText) ? lib.Template(RoomTemplates.LabelText) : null;
            if (tpl == null || tpl.shader == null || !tpl.shader.name.StartsWith("TextMeshPro", StringComparison.Ordinal)) return null;
            var m = new Material(font.material) { name = "Room label (mobile SDF)", shader = tpl.shader };
            m.renderQueue = tpl.renderQueue;
            return m;
        }

        /// <summary>The switch labels: small up-facing quads .04 x .012 at (x, .0065, source z -.03) with bold #e8dcc4 text,
        /// text-up away from the player; the letters are 1.67x wide (a 256 x 128 label stretched over each quad), so the x
        /// scale keeps that look. The three words are laid out by TextMeshPro once (temporary objects), then their
        /// meshes are merged into one mesh in the panel frame: one draw instead of three, and no TMP work afterwards.</summary>
        public static Mesh BuildLabels(Transform panel, TMP_FontAsset font, Material mat)
        {
            if (font == null) return null;
            var verts = new List<Vector3>(64); var nrm = new List<Vector3>(64); var tan = new List<Vector4>(64);
            var cols = new List<Color32>(64); var uv0 = new List<Vector4>(64); var uv1 = new List<Vector4>(64);
            var tris = new List<int>(96);
            var tv = new List<Vector3>(); var tn = new List<Vector3>(); var tt = new List<Vector4>(); var tc = new List<Color32>();
            var tu0 = new List<Vector4>(); var tu1 = new List<Vector4>();
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Label " + AmbiencePanel.Labels[i]);
                try
                {
                    go.transform.SetParent(panel, false);
                    go.transform.localPosition = new Vector3(AmbiencePanel.ToggleX[i], 0.0065f, 0.03f);
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    go.transform.localScale = new Vector3(1.6667f, 1f, 1f);
                    var tmp = go.AddComponent<TextMeshPro>();
                    tmp.font = font;
                    if (mat != null) tmp.fontSharedMaterial = mat;
                    tmp.text = AmbiencePanel.Labels[i];
                    tmp.fontSize = 0.06f;                       // cap height ~4 mm (LiberationSans cap line 59 / 86 x 0.1 x size)
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.color = TextureUtil.Hex("#e8dcc4");
                    tmp.rectTransform.sizeDelta = new Vector2(0.04f / 1.6667f, 0.012f);
                    tmp.ForceMeshUpdate(true, true);
                    Mesh m = tmp.mesh;
                    if (m == null || m.vertexCount == 0) continue;
                    Matrix4x4 toPanel = panel.worldToLocalMatrix * go.transform.localToWorldMatrix;
                    Matrix4x4 nTo = toPanel.inverse.transpose;
                    int b0 = verts.Count;
                    m.GetVertices(tv); m.GetNormals(tn); m.GetTangents(tt); m.GetColors(tc); m.GetUVs(0, tu0); m.GetUVs(1, tu1);
                    for (int v = 0; v < tv.Count; v++)
                    {
                        verts.Add(toPanel.MultiplyPoint3x4(tv[v]));
                        nrm.Add(v < tn.Count ? nTo.MultiplyVector(tn[v]).normalized : Vector3.up);
                        if (v < tt.Count) { Vector3 d = toPanel.MultiplyVector(tt[v]).normalized; tan.Add(new Vector4(d.x, d.y, d.z, tt[v].w)); }
                        else tan.Add(new Vector4(1f, 0f, 0f, 1f));
                        cols.Add(v < tc.Count ? tc[v] : new Color32(255, 255, 255, 255));
                        uv0.Add(v < tu0.Count ? tu0[v] : Vector4.zero);
                        uv1.Add(v < tu1.Count ? tu1[v] : Vector4.zero);
                    }
                    var t = m.triangles;
                    for (int k = 0; k < t.Length; k++) tris.Add(b0 + t[k]);
                }
                finally
                {
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
            }
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = "Switch labels" };
            mesh.SetVertices(verts);
            mesh.SetNormals(nrm);
            mesh.SetTangents(tan);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Rig.Handles: the list filled once in Build (the Lessons deck may add its body to it in place).</summary>
        public IReadOnlyList<Renderer> Handles => _handles;

        public void Destroy()
        {
            if (Group != null) UnityEngine.Object.Destroy(Group.gameObject);
            if (LabelMaterial != null) UnityEngine.Object.Destroy(LabelMaterial);
            LabelMaterial = null;
            foreach (var m in Meshes) if (m != null) UnityEngine.Object.Destroy(m);
            Meshes.Clear();
        }
    }

    /// <summary>The table's zone (the table rig's world box expanded by ZONE_PAD) as a box in the rig frame:
    /// the slab's footprint and the deck height, padded by GrabConstants.ZonePad, following the rig.</summary>
    public sealed class TableZone : IPlayZone
    {
        static readonly Bounds Local = new Bounds(new Vector3(0f, 0.36f, 0f), new Vector3(0.58f + 2f * GrabConstants.ZonePad, 0.72f + 2f * GrabConstants.ZonePad, 0.32f + 2f * GrabConstants.ZonePad));
        readonly Transform _group;
        public TableZone(Transform group) { _group = group; }
        public bool Contains(Vector3 world) => _group != null && Local.Contains(_group.InverseTransformPoint(world));
    }

    /// <summary>The soft floor shadows under the at-seat instruments (spec 07 section 3.4 + rigDecor; platform.md 11:
    /// "rig shadows 1 (was 3)"): one quad per registry instrument with a ShadowSpec (rig-local centre, W x D, opacity), all
    /// in ONE mesh drawn with the room's FloorDecal material, under a root of its own (not under the rigs, so Layout's
    /// footprints never see them and a lifted rig keeps its shadow on the floor). A quad shows while its rig is at the seat:
    /// Rig.InScene, its group active, and ILayoutService.PlaceOf neither Display nor Gliding; a HiddenInMr quad (the
    /// piano's, which belongs to the room) also needs the room shown, so mixed reality hides it as it hid the
    /// room's blob. Updated in EndFrame, after Layout's page-turn animation: the vertices are rewritten only when a rig
    /// moved, turned or scaled (a page turn shrinks it to .85; held, gliding and page-turning rigs move every frame), came
    /// or went, or the room was shown or hidden; a hidden quad collapses onto a shown one. Allocation-free after Build. The
    /// default set draws exactly the three blobs of before (the descriptors' Shadow): the piano's (0, .2) 2 x 1 .55, the
    /// kit's (.1, .5) 1.9 x 1.6 .45, the harp's (-.2725, -.168) 1.1 x .7 .45.</summary>
    public sealed class RigShadows
    {
        public struct Entry
        {
            public InstrumentKind Kind;
            public RigId Id;
            public Vector2 Local;
            public float W, D, Opacity;
            public bool HiddenInMr;
            public Rig Rig;
            /// <summary>The one renderer every entry draws with (enabled while any entry is shown; Shown says whether this one is).</summary>
            public MeshRenderer Renderer;
            /// <summary>Drawn now, and where: the quad's centre on the floor (world), its yaw (degrees) and the rig's scale.</summary>
            public bool Shown;
            public Vector3 Centre;
            public float Yaw, Scale;
            internal Matrix4x4 Seen;
            internal bool Showing;
        }

        /// <summary>The quads lie this far above the floor (y 0).</summary>
        public const float FloorLift = 0.005f;

        Entry[] _e = Array.Empty<Entry>();
        Vector3[] _unit = Array.Empty<Vector3>(), _verts = Array.Empty<Vector3>();
        Mesh _mesh;
        Transform _root, _room;
        MeshRenderer _renderer;
        App _app;
        IRigRegistry _registry;
        ILayoutService _layout;
        Action _onRigsChanged;
        bool _bound, _dirty = true, _roomShown;
        Matrix4x4 _rootSeen;

        public IReadOnlyList<Entry> Entries => _e;
        /// <summary>The one renderer (one draw) and its mesh: four vertices per entry, in Entries order.</summary>
        public MeshRenderer Renderer => _renderer;
        public Mesh Mesh => _mesh;
        public Transform Root => _root;
        /// <summary>Vertex rewrites so far (tests: nothing is rewritten while nothing moves).</summary>
        public int Rebuilds { get; private set; }

        public bool TryGet(RigId id, out Entry entry)
        {
            for (int i = 0; i < _e.Length; i++) if (_e[i].Id == id) { entry = _e[i]; return true; }
            entry = default;
            return false;
        }

        /// <summary>One quad per playable descriptor with a Shadow, in the registry's carousel order. The mesh lives under its
        /// own root under <paramref name="worldRoot"/>; <paramref name="roomRoot"/> only says whether the room is shown.</summary>
        public void Build(Transform roomRoot, Transform worldRoot, Material decal, IInstrumentRegistry registry)
        {
            _room = roomRoot;
            var list = new List<Entry>(8);
            if (registry != null)
                foreach (InstrumentDescriptor d in registry.All)
                {
                    if (d == null || !d.Playable || d.Shadow == null) continue;
                    list.Add(new Entry
                    {
                        Kind = d.Kind, Id = d.Rig, Local = d.Shadow.Local, W = d.Shadow.W, D = d.Shadow.D, Opacity = d.Shadow.Opacity,
                        HiddenInMr = d.Shadow.HiddenInMr,
                    });
                }
            _e = list.ToArray();
            var geo = new Geo();
            for (int i = 0; i < _e.Length; i++) geo.Append(RoomBuilder.BlobGeo(1f, 1f, _e[i].Opacity));
            _mesh = geo.ToUnityMesh("RigShadows");
            _mesh.MarkDynamic();
            _unit = _mesh.vertices;
            _verts = new Vector3[_unit.Length];
            _root = new GameObject("RigShadows").transform;
            _root.SetParent(worldRoot, false);
            _renderer = (MeshRenderer)RoomBuilder.MakeRenderer(_root, "RigShadows", _mesh, decal);
            _renderer.enabled = false;
            for (int i = 0; i < _e.Length; i++) _e[i].Renderer = _renderer;
            _dirty = true;
        }

        /// <summary>Finds the rigs once they are registered (Layout, Order 100, or the rig list), and again whenever the rig
        /// registry changes.</summary>
        public void Bind(App app)
        {
            _app = app;
            _registry = app.Get<IRigRegistry>();
            _layout = app.Get<ILayoutService>();
            if (_registry != null && _onRigsChanged == null)
            {
                _onRigsChanged = () => _bound = false;
                _registry.Changed += _onRigsChanged;
            }
            BindRigs();
        }

        void BindRigs()
        {
            _bound = true;
            _dirty = true;
            if (_app == null) return;
            IReadOnlyList<Rig> rigs = _registry != null ? null : _app.GetAll<Rig>();
            for (int i = 0; i < _e.Length; i++)
            {
                Rig rig = _registry != null ? _registry.Get(_e[i].Id) : null;
                if (rig == null && rigs != null) for (int k = 0; k < rigs.Count; k++) if (rigs[k].Id == _e[i].Id) rig = rigs[k];
                _e[i].Rig = rig;
            }
        }

        bool AtSeat(in Entry e, bool room)
        {
            Transform g = e.Rig != null ? e.Rig.Group : null;
            if (g == null || !e.Rig.InScene || !g.gameObject.activeInHierarchy) return false;
            if (e.HiddenInMr && !room) return false;
            if (_layout == null) return true;
            InstrumentPlace p = _layout.PlaceOf(e.Kind);
            return p != InstrumentPlace.Display && p != InstrumentPlace.Gliding;
        }

        public void Update()
        {
            if (_renderer == null) return;
            if (!_bound) BindRigs();
            bool room = _room != null && _room.gameObject.activeInHierarchy;
            bool dirty = _dirty || room != _roomShown;
            if (_root.localToWorldMatrix != _rootSeen) dirty = true;
            for (int i = 0; i < _e.Length; i++)
            {
                ref Entry e = ref _e[i];
                bool show = AtSeat(e, room);
                if (show != e.Showing) { e.Showing = show; dirty = true; }
                if (!show) continue;
                Matrix4x4 m = e.Rig.Group.localToWorldMatrix;
                if (m != e.Seen) { e.Seen = m; dirty = true; }
            }
            if (!dirty) return;
            _dirty = false;
            _roomShown = room;
            Rebuild();
        }

        /// <summary>Every quad from its rig: the offset and the size scale with the rig, the height stays on the floor.</summary>
        void Rebuild()
        {
            Rebuilds++;
            _rootSeen = _root.localToWorldMatrix;
            Matrix4x4 toLocal = _root.worldToLocalMatrix;
            int shown = 0;
            Vector3 park = Vector3.zero;
            for (int i = 0; i < _e.Length; i++)
            {
                ref Entry e = ref _e[i];
                e.Shown = e.Showing;
                if (!e.Shown) continue;
                Transform g = e.Rig.Group;
                Vector3 p = g.TransformPoint(e.Local.x, 0f, e.Local.y);
                p.y = FloorLift;
                Vector3 f = g.forward; f.y = 0f;
                float yaw = f.sqrMagnitude > 1e-8f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
                float s = g.lossyScale.x;
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 u = _unit[i * 4 + k];
                    _verts[i * 4 + k] = toLocal.MultiplyPoint3x4(p + q * new Vector3(u.x * e.W * s, u.y, u.z * e.D * s));
                }
                e.Centre = p;
                e.Yaw = yaw;
                e.Scale = s;
                if (shown++ == 0) park = toLocal.MultiplyPoint3x4(p);
            }
            if (shown == 0)
            {
                if (_renderer.enabled) _renderer.enabled = false;
                return;
            }
            // A quad that is not drawn has no area: its corners sit on a drawn quad's centre (the bounds stay tight).
            for (int i = 0; i < _e.Length; i++)
                if (!_e[i].Shown) for (int k = 0; k < 4; k++) _verts[i * 4 + k] = park;
            _mesh.SetVertices(_verts);
            _mesh.RecalculateBounds();
            if (!_renderer.enabled) _renderer.enabled = true;
        }

        public void Destroy()
        {
            if (_registry != null && _onRigsChanged != null) _registry.Changed -= _onRigsChanged;
            _onRigsChanged = null;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            _mesh = null;
        }
    }
}
