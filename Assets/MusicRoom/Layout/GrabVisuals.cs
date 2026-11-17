// The grab's feedback (Docs/05-instruments.md section 8): the floor ring under a held rig (0.42-0.5 m, additive
// #ffc46a alpha .5; red #ff5a4a alpha .6 while it is pushed through another rig), the thinner ring that fills in round a
// rig while a pinch on it is held before it takes hold ("this will move"), an edge cage round the rig's parts, and the
// reset-view ring that fills round both pinching hands while they hold still (LayoutModule.TickSummon). The
// state is kept even when headless so tests can read it; the meshes exist only when the app renders. Each of the three
// renderers has its own clone of the foundation's UnlitAdditive template and its colour / alpha is set on that clone
// (ARCHITECTURE.md section 14: never a MaterialPropertyBlock on a URP Unlit renderer, it leaves the SRP Batcher).
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Layout
{
    public sealed class GrabVisuals
    {
        public const float RingInner = 0.42f, RingOuter = 0.5f, HintInner = 0.43f;
        public const int RingSegments = 64, HintSegments = 64;
        public const float RingY = 0.006f, HintY = 0.007f;
        public static readonly Color Gold = new Color32(0xff, 0xc4, 0x6a, 0xff);
        public static readonly Color Red = new Color32(0xff, 0x5a, 0x4a, 0xff);

        // ---- state (tests read it) ----
        public bool RingVisible { get; private set; }
        public bool RingRed { get; private set; }
        public float RingOpacity { get; private set; }
        public Vector3 RingCentre { get; private set; }
        public float RingScale { get; private set; } = 1f;
        public bool HintVisible { get; private set; }
        public Rig HintRig { get; private set; }
        public float HintK { get; private set; }
        public int HintSegmentsShown { get; private set; }
        public float HintOpacity { get; private set; }
        public bool CageVisible { get; private set; }
        public float CageOpacity { get; private set; }
        public bool CageRed { get; private set; }
        /// <summary>The reset-view ring: shown, filled (0..1), its centre (between the pinches) and radius.</summary>
        public bool BringRingVisible { get; private set; }
        public float BringRingK { get; private set; }
        public Vector3 BringRingCentre { get; private set; }
        public float BringRingRadius { get; private set; }
        public const float BringInner = 0.47f, BringOuter = 0.5f, BringPad = 0.07f, BringMinRadius = 0.12f;
        public const int BringSegments = 64;

        bool _render;
        Transform _root;
        Transform _ring, _hint, _cage, _bring;
        MeshRenderer _ringR, _hintR, _cageR, _bringR;
        Mesh _ringMesh, _hintMesh, _cageMesh, _bringMesh;   // procedural: a destroyed MeshFilter does not free them (Destroy does)
        Material _ringM, _hintM, _cageM, _bringM;

        public GrabVisuals(MaterialLibrary materials, Transform parent, bool render)
        {
            _render = render && materials != null && parent != null;
            if (!_render) return;
            _root = new GameObject("GrabVisuals").transform;
            _root.SetParent(parent, false);
            _ringM = Tinted(materials, "Layout/GrabRing");
            _hintM = Tinted(materials, "Layout/GrabHint");
            _cageM = Tinted(materials, "Layout/GrabCage");
            _ringMesh = RingMesh(RingInner, RingOuter, RingSegments);
            _ringR = Make("GrabRing", _ringMesh, _ringM, out _ring, MeshTopology.Triangles);
            _hintMesh = RingMesh(HintInner, RingOuter, HintSegments);
            _hintR = Make("GrabHint", _hintMesh, _hintM, out _hint, MeshTopology.Triangles);
            _hintR.sortingOrder = 4;   // draw order 4: drawn after the floor ring
            _cageMesh = CageMesh();
            _cageR = Make("GrabCage", _cageMesh, _cageM, out _cage, MeshTopology.Lines);
            _cageR.sortingOrder = 4;
            _bringM = Tinted(materials, "Layout/BringRing");
            _bringMesh = RingMesh(BringInner, BringOuter, BringSegments);
            _bringR = Make("BringRing", _bringMesh, _bringM, out _bring, MeshTopology.Triangles);
            _bringR.sortingOrder = 5;
        }

        static Material Tinted(MaterialLibrary materials, string name)
        {
            Material m = materials.Clone(MaterialLibrary.UnlitAdditive);
            m.name = name;
            m.SetColor(MaterialLibrary.BaseColor, Gold);
            return m;
        }

        MeshRenderer Make(string name, Mesh mesh, Material mat, out Transform t, MeshTopology topology)
        {
            var go = new GameObject(name);
            t = go.transform;
            t.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            return r;
        }

        /// <summary>A ring (inner, outer, segments) laid on the floor facing up. Segment k spans angles k..k+1 of
        /// the circle starting at +x and turning toward +z (the source frame's -z, mirrored), so a partial
        /// index range fills the ring segment by segment.</summary>
        public static Mesh RingMesh(float inner, float outer, int segments)
        {
            var v = new Vector3[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                float c = Mathf.Cos(t), s = Mathf.Sin(t);
                v[i * 2] = new Vector3(c * inner, 0f, s * inner);
                v[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
            }
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c2 = a + 2, d = a + 3;
                // Clockwise seen from above (+y), Unity's front face.
                tris[i * 6] = a; tris[i * 6 + 1] = c2; tris[i * 6 + 2] = b;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = c2; tris[i * 6 + 5] = d;
            }
            var m = new Mesh { name = "LayoutRing" };
            m.vertices = v;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The 12 edges of a unit cube centred on the origin (the edges of a 1 x 1 x 1 box).</summary>
        static Mesh CageMesh()
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
            int[] e = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
            var m = new Mesh { name = "LayoutCage" };
            m.vertices = v;
            m.SetIndices(e, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Colour and alpha on the renderer's own clone (no allocation; skipped when unchanged).</summary>
        static void Paint(Material m, Color c, float alpha)
        {
            if (m == null) return;
            c.a = alpha;
            if (m.GetColor(MaterialLibrary.BaseColor) != c) m.SetColor(MaterialLibrary.BaseColor, c);
        }

        /// <summary>The colour now on a renderer's material (tests).</summary>
        public Color RingColor => _ringM != null ? _ringM.GetColor(MaterialLibrary.BaseColor) : Color.clear;
        public Color HintColor => _hintM != null ? _hintM.GetColor(MaterialLibrary.BaseColor) : Color.clear;
        public Color CageColor => _cageM != null ? _cageM.GetColor(MaterialLibrary.BaseColor) : Color.clear;

        static void Show(MeshRenderer r, bool on)
        {
            if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        }

        /// <summary>placeRing(): the floor ring at the rig's footprint centre, scaled to its footprint.</summary>
        public void SetRing(bool visible, Vector3 centre, float scale, bool red)
        {
            RingVisible = visible;
            RingRed = red;
            RingOpacity = red ? 0.6f : 0.5f;
            RingCentre = centre;
            RingScale = scale;
            if (!_render) return;
            Show(_ringR, visible);
            if (!visible) return;
            _ring.SetPositionAndRotation(new Vector3(centre.x, RingY, centre.z), Quaternion.identity);
            _ring.localScale = new Vector3(scale, 1f, scale);
            Paint(_ringM, red ? Red : Gold, RingOpacity);
        }

        /// <summary>showHint(pending): the thin ring filling over the dwell (k 0..1), opacity .25 + .45k.</summary>
        public void SetHint(Rig rig, Vector3 centre, float scale, float k)
        {
            bool visible = rig != null;
            HintVisible = visible;
            HintRig = rig;
            k = Mathf.Clamp01(k);
            HintK = visible ? k : 0f;
            HintSegmentsShown = visible ? Mathf.Max(1, Mathf.RoundToInt(HintSegments * k)) : 0;
            HintOpacity = visible ? 0.25f + 0.45f * k : 0f;
            if (!_render) return;
            Show(_hintR, visible);
            if (!visible) return;
            _hint.SetPositionAndRotation(new Vector3(centre.x, HintY, centre.z), Quaternion.identity);
            _hint.localScale = new Vector3(scale, 1f, scale);
            _hintMesh.SetSubMesh(0, new SubMeshDescriptor(0, 6 * HintSegmentsShown, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            Paint(_hintM, Gold, HintOpacity);
        }

        /// <summary>fitCage(r, opacity, colour): the outline of the rig's parts box (in its own frame) + 0.02, carried by the
        /// rig's pose.</summary>
        public void SetCage(bool visible, Transform rig, Vector3 localCentre, Vector3 localSize, float opacity, bool red)
        {
            CageVisible = visible;
            CageOpacity = visible ? opacity : 0f;
            CageRed = red;
            if (!_render) return;
            Show(_cageR, visible);
            if (!visible || rig == null) return;
            _cage.SetPositionAndRotation(rig.TransformPoint(localCentre), rig.rotation);
            _cage.localScale = Vector3.Scale(localSize, rig.lossyScale);
            Paint(_cageM, red ? Red : Gold, opacity);
        }

        /// <summary>The reset-view ring round both pinches <paramref name="a"/> and <paramref name="b"/>, upright and facing the
        /// eyes, filling segment by segment with <paramref name="k"/> (opacity .35 + .5k). No allocation.</summary>
        public void SetBringRing(bool visible, Vector3 a, Vector3 b, Vector3 eyes, float k)
        {
            k = Mathf.Clamp01(k);
            BringRingVisible = visible;
            BringRingK = visible ? k : 0f;
            BringRingCentre = visible ? (a + b) * 0.5f : Vector3.zero;
            BringRingRadius = visible ? Mathf.Max(BringMinRadius, Vector3.Distance(a, b) * 0.5f + BringPad) : 0f;
            if (!_render) return;
            Show(_bringR, visible);
            if (!visible) return;
            Vector3 toEyes = eyes - BringRingCentre;
            if (toEyes.sqrMagnitude < 1e-6f) toEyes = Vector3.back;
            // the ring mesh lies in its local XZ plane: its +y faces the eyes
            _bring.SetPositionAndRotation(BringRingCentre, Quaternion.FromToRotation(Vector3.up, toEyes.normalized));
            float sc = BringRingRadius / BringOuter;
            _bring.localScale = new Vector3(sc, 1f, sc);
            int shown = Mathf.Max(1, Mathf.RoundToInt(BringSegments * k));
            _bringMesh.SetSubMesh(0, new SubMeshDescriptor(0, 6 * shown, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            Paint(_bringM, Gold, 0.35f + 0.5f * k);
        }

        public void HideAll()
        {
            SetRing(false, Vector3.zero, 1f, false);
            SetHint(null, Vector3.zero, 1f, 0f);
            SetCage(false, null, Vector3.zero, Vector3.one, 0f, false);
        }

        /// <summary>The three procedural meshes and the root (the materials are MaterialLibrary clones, freed with it).
        /// Nothing is left behind between boots (ARCHITECTURE.md section 16).</summary>
        public void Destroy()
        {
            DestroyMesh(ref _ringMesh);
            DestroyMesh(ref _hintMesh);
            DestroyMesh(ref _cageMesh);
            DestroyMesh(ref _bringMesh);
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _render = false;   // state calls after this only update the readable state
        }

        /// <summary>The procedural meshes (tests: check they are freed after Destroy).</summary>
        public Mesh RingMeshAsset => _ringMesh;
        public Mesh HintMeshAsset => _hintMesh;
        public Mesh CageMeshAsset => _cageMesh;

        static void DestroyMesh(ref Mesh m)
        {
            if (m != null) Object.Destroy(m);
            m = null;
        }
    }
}
