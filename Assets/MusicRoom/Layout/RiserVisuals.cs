// The riser (2026-11-11 headset test: "drag and drop is nice but allow it place instrument to their height/level"). An
// instrument raised above its home (by a Move instruments drag, or by the player's level: LayoutModule.Height) stands on a
// plain wooden platform from the floor up to its feet, as wide as its floor-standing parts plus a lip, turned with it. The
// piano is never raised this way: its own stand stretches to the floor (PianoInstrument.FitStand).
// Why a riser and not longer legs for every instrument: the drum kit's stands, the harp's base, the marimba's frame, the
// hand drums' stands and the bowls' stand are merged meshes (one draw each) whose legs cannot be stretched without
// stretching the drums, the bowls or the cushions on them; a drum riser / stage block is what players use in a real room to
// raise a floor instrument, it reads at a glance as "raised on purpose", never as "floating", and it is one more draw for a
// raised instrument only (nothing while it stands on the floor). State is kept even when headless so tests can read it; the
// meshes exist only when the app renders. Allocation-free per frame.
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Layout
{
    public sealed class RiserVisuals
    {
        /// <summary>The platform reaches this far past the instrument's floor-standing parts on every side.</summary>
        public const float Lip = 0.04f;
        /// <summary>Raised less than this, an instrument stands on the floor (no riser).</summary>
        public const float MinRaise = 0.005f;
        /// <summary>The riser's colour: dark stained wood, a little rough.</summary>
        public static readonly Color Wood = new Color(0.19f, 0.14f, 0.105f, 1f);
        public const float WoodSmoothness = 0.25f;

        struct Slot
        {
            public bool Shown;
            public Vector3 Centre, Size;
            public float YawDeg;
            public Transform T;
        }

        readonly Slot[] _slots = new Slot[InstrumentIds.KindCount];
        readonly bool _render;
        Transform _root;
        Mesh _mesh;
        Material _mat;

        public RiserVisuals(MaterialLibrary materials, Transform parent, bool render)
        {
            _render = render && materials != null && parent != null;
            if (!_render) return;
            _root = new GameObject("Risers").transform;
            _root.SetParent(parent, false);
            _mesh = CubeMesh();
            _mat = materials.Lit(Wood, WoodSmoothness);
            _mat.name = "Layout/Riser";
        }

        /// <summary>The riser under rig id <paramref name="id"/> (RigId value) is shown.</summary>
        public bool Shown(int id) => id >= 0 && id < _slots.Length && _slots[id].Shown;
        /// <summary>Its height (0 when not shown): floor to the instrument's feet.</summary>
        public float Height(int id) => Shown(id) ? _slots[id].Size.y : 0f;
        /// <summary>Its centre (world) and size (x across, y up, z deep, in its own frame).</summary>
        public Vector3 Centre(int id) => Shown(id) ? _slots[id].Centre : Vector3.zero;
        public Vector3 Size(int id) => Shown(id) ? _slots[id].Size : Vector3.zero;
        public int ShownCount { get { int n = 0; for (int i = 0; i < _slots.Length; i++) if (_slots[i].Shown) n++; return n; } }

        /// <summary>Shows the riser of rig <paramref name="id"/> as a box centred on <paramref name="centre"/> of
        /// <paramref name="size"/> turned <paramref name="yawDeg"/>; the transform is touched only when something changed.</summary>
        public void Set(int id, Vector3 centre, Vector3 size, float yawDeg)
        {
            if (id < 0 || id >= _slots.Length) return;
            ref Slot s = ref _slots[id];
            bool same = s.Shown && (s.Centre - centre).sqrMagnitude < 1e-8f && (s.Size - size).sqrMagnitude < 1e-8f && Mathf.Abs(s.YawDeg - yawDeg) < 1e-3f;
            s.Shown = true;
            if (same) return;
            s.Centre = centre;
            s.Size = size;
            s.YawDeg = yawDeg;
            if (!_render) return;
            if (s.T == null) s.T = Make(id);
            s.T.SetPositionAndRotation(centre, Quaternion.Euler(0f, yawDeg, 0f));
            s.T.localScale = size;
            if (!s.T.gameObject.activeSelf) s.T.gameObject.SetActive(true);
        }

        public void Hide(int id)
        {
            if (id < 0 || id >= _slots.Length) return;
            ref Slot s = ref _slots[id];
            if (!s.Shown) return;
            s.Shown = false;
            if (s.T != null && s.T.gameObject.activeSelf) s.T.gameObject.SetActive(false);
        }

        public void HideAll()
        {
            for (int i = 0; i < _slots.Length; i++) Hide(i);
        }

        public void Destroy()
        {
            HideAll();
            if (_mesh != null) Object.Destroy(_mesh);
            _mesh = null;
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }

        Transform Make(int id)
        {
            var go = new GameObject("Riser " + (RigId)id);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// <summary>A unit cube centred on the origin with flat normals (24 vertices), so its top and sides shade apart.</summary>
        static Mesh CubeMesh()
        {
            var v = new Vector3[24];
            var n = new Vector3[24];
            var t = new int[36];
            Vector3[] faces = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                Vector3 nf = faces[f];
                Vector3 a = Mathf.Abs(nf.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 u = Vector3.Cross(nf, a) * 0.5f, w = Vector3.Cross(nf, u.normalized) * 0.5f, c = nf * 0.5f;
                int k = f * 4;
                v[k] = c - u - w; v[k + 1] = c + u - w; v[k + 2] = c + u + w; v[k + 3] = c - u + w;
                for (int q = 0; q < 4; q++) n[k + q] = nf;
                // wound so the face looks along its normal: Cross(v1 - v0, v2 - v0) along it is Unity's front face
                // (clockwise seen from outside), as GrabVisuals.RingMesh
                bool along = Vector3.Dot(Vector3.Cross(v[k + 1] - v[k], v[k + 2] - v[k]), nf) > 0f;
                int ti = f * 6;
                if (along) { t[ti] = k; t[ti + 1] = k + 1; t[ti + 2] = k + 2; t[ti + 3] = k; t[ti + 4] = k + 2; t[ti + 5] = k + 3; }
                else { t[ti] = k; t[ti + 1] = k + 2; t[ti + 2] = k + 1; t[ti + 3] = k; t[ti + 4] = k + 3; t[ti + 5] = k + 2; }
            }
            var m = new Mesh { name = "LayoutRiser" };
            m.vertices = v;
            m.normals = n;
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }
    }
}
