// What "Move instruments" draws: round every instrument of the layout a set of thin corner brackets (the outline of its
// parts' box in its own frame, padded 3 cm) and a floor ring, a soft warm white while it waits, light gold while a hand's
// pinch point or ray is on it, gold while it is held and red where it may not go; and from each hand's system pointer
// pose a thin ray with a soft dot where it lands. Calm and warm to match the lamp-lit room (the user prefers realism to
// glowing UI): the brackets, rings, rays and dot are alpha-blended (painted, not added light), the brackets 4 mm thin
// and short. Brackets and rings: one UnlitTransparent clone per renderer (colour and alpha set on the clone, never a
// MaterialPropertyBlock: ARCHITECTURE.md section 14). Rays: URP Particles/Unlit alpha-blended with vertex colours (they
// fade in from the hand). The bracket meshes are rewritten only when a rig's box changes (a piano lifted on its stand), from
// preallocated arrays. State is kept even when headless, so tests read it; meshes exist only when the app renders.
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Layout
{
    public sealed class ArrangeVisuals
    {
        /// <summary>Every rig arrange mode can show at once: My set (up to 3), the side table and the instruments on display.</summary>
        public const int Slots = 10;
        public const float Pad = 0.03f, ArmWidth = 0.004f, ArmMin = 0.035f, ArmMax = 0.10f, ArmShare = 0.18f;
        public const float RingInner = 0.45f, RingOuter = 0.5f, RingY = 0.005f;
        public const float RayWidth = 0.0045f, DotSize = 0.035f;
        /// <summary>Waiting: a soft warm white, like lamp light on paper (was a cyan glow).</summary>
        public static readonly Color Warm = new Color32(0xff, 0xf1, 0xdc, 0xff);
        /// <summary>A hand's pinch point or ray on it: a light gold, clearly warmer than the waiting white, lighter than held.</summary>
        public static readonly Color PaleGold = new Color32(0xf2, 0xcb, 0x84, 0xff);
        /// <summary>Held: the grab's gold; blocked (it would overlap or come too close): the grab's red.</summary>
        public static readonly Color Gold = GrabVisuals.Gold;
        public static readonly Color Red = GrabVisuals.Red;

        public enum Look : byte { Idle = 0, Target = 1, Held = 2, Blocked = 3 }

        // ---- state (tests) ----
        public readonly bool[] SlotVisible = new bool[Slots];
        public readonly Look[] SlotLook = new Look[Slots];
        public readonly Rig[] SlotRig = new Rig[Slots];
        public readonly bool[] SlotRing = new bool[Slots];
        public readonly bool[] RayVisible = new bool[2];
        public readonly bool[] RayHits = new bool[2];
        public readonly float[] RayLength = new float[2];
        public readonly Vector3[] RayFrom = new Vector3[2], RayTo = new Vector3[2];
        public int BracketRebuilds { get; private set; }

        bool _render;
        Transform _root;
        readonly Transform[] _bracket = new Transform[Slots], _ring = new Transform[Slots], _ray = new Transform[2], _dot = new Transform[2];
        readonly MeshRenderer[] _bracketR = new MeshRenderer[Slots], _ringR = new MeshRenderer[Slots], _rayR = new MeshRenderer[2], _dotR = new MeshRenderer[2];
        readonly Material[] _bracketM = new Material[Slots], _ringM = new Material[Slots], _rayM = new Material[2], _dotM = new Material[2];
        readonly Mesh[] _bracketMesh = new Mesh[Slots];
        Mesh _ringMesh, _rayMesh, _dotMesh;
        readonly Vector3[] _boxC = new Vector3[Slots], _boxS = new Vector3[Slots];

        // Bracket geometry: 8 corners x 3 arms, each a square tube of 4 quads (16 vertices, 24 indices).
        const int ArmVerts = 16, ArmIdx = 24, Arms = 24;
        readonly Vector3[] _verts = new Vector3[Arms * ArmVerts];

        public ArrangeVisuals(MaterialLibrary materials, Transform parent, bool render)
        {
            _render = render && materials != null && parent != null;
            if (!_render) return;
            _root = new GameObject("ArrangeVisuals").transform;
            _root.SetParent(parent, false);
            _ringMesh = GrabVisuals.RingMesh(RingInner, RingOuter, 64);
            _ringMesh.name = "ArrangeRing";
            int[] idx = BracketIndices();
            for (int i = 0; i < Slots; i++)
            {
                _bracketMesh[i] = new Mesh { name = "ArrangeBrackets" + i };
                _bracketMesh[i].MarkDynamic();
                _bracketMesh[i].vertices = _verts;
                _bracketMesh[i].triangles = idx;
                _bracketM[i] = Tinted(materials, MaterialLibrary.UnlitTransparent, "Layout/ArrangeBrackets", Warm);
                _ringM[i] = Tinted(materials, MaterialLibrary.UnlitTransparent, "Layout/ArrangeRing", Warm);
                _bracketR[i] = Make("Brackets" + i, _bracketMesh[i], _bracketM[i], out _bracket[i]);
                _ringR[i] = Make("Ring" + i, _ringMesh, _ringM[i], out _ring[i]);
                _boxS[i] = new Vector3(-1f, -1f, -1f);
            }
            _rayMesh = RayMesh();
            _dotMesh = DotMesh();
            Texture dot = SharedTextures.Get(SharedTextures.SoftDot);
            for (int h = 0; h < 2; h++)
            {
                _rayM[h] = Tinted(materials, MaterialLibrary.ParticlesAlpha, "Layout/ArrangeRay", Color.white);
                _dotM[h] = Tinted(materials, MaterialLibrary.UnlitTransparent, "Layout/ArrangeRayDot", Gold);
                if (dot != null) _dotM[h].SetTexture(MaterialLibrary.BaseMap, dot);
                _rayR[h] = Make("Ray" + h, _rayMesh, _rayM[h], out _ray[h]);
                _dotR[h] = Make("RayDot" + h, _dotMesh, _dotM[h], out _dot[h]);
                _rayR[h].sortingOrder = 5;
                _dotR[h].sortingOrder = 6;
            }
        }

        static Material Tinted(MaterialLibrary materials, string template, string name, Color c)
        {
            Material m = materials.Clone(template);
            m.name = name;
            m.SetColor(MaterialLibrary.BaseColor, c);
            return m;
        }

        MeshRenderer Make(string name, Mesh mesh, Material mat, out Transform t)
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

        static void Show(MeshRenderer r, bool on)
        {
            if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        }

        static void Paint(Material m, Color c, float alpha)
        {
            if (m == null) return;
            c.a = alpha;
            if (m.GetColor(MaterialLibrary.BaseColor) != c) m.SetColor(MaterialLibrary.BaseColor, c);
        }

        public static Color ColourOf(Look look) => look == Look.Idle ? Warm : look == Look.Target ? PaleGold : look == Look.Blocked ? Red : Gold;
        public static float BracketAlpha(Look look) => look == Look.Idle ? 0.42f : look == Look.Target ? 0.8f : 0.92f;
        public static float RingAlpha(Look look) => look == Look.Idle ? 0.16f : look == Look.Target ? 0.3f : 0.42f;
        /// <summary>The ray: warm white and faint while it points at nothing, light gold on an instrument.</summary>
        public static float RayAlpha(bool hits) => hits ? 0.7f : 0.3f;

        /// <summary>One instrument's highlight: the brackets round its parts box (rig-local centre and size, before the
        /// padding), carried by the rig's pose, and the ring on the floor under its footprint centre.</summary>
        public void SetSlot(int i, Rig rig, bool visible, Look look, Vector3 boxCentre, Vector3 boxSize, Vector3 ringCentre, float ringScale, bool ring = true)
        {
            if (i < 0 || i >= Slots) return;
            SlotVisible[i] = visible;
            SlotLook[i] = visible ? look : Look.Idle;
            SlotRig[i] = visible ? rig : null;
            SlotRing[i] = visible && ring;
            if (!_render) return;
            Show(_bracketR[i], visible);
            Show(_ringR[i], visible && ring);
            if (!visible || rig == null || rig.Group == null) return;
            if ((boxCentre - _boxC[i]).sqrMagnitude > 1e-8f || (boxSize - _boxS[i]).sqrMagnitude > 1e-8f)
            {
                _boxC[i] = boxCentre;
                _boxS[i] = boxSize;
                FillBrackets(boxCentre, boxSize + 2f * Pad * Vector3.one);
                _bracketMesh[i].SetVertices(_verts);
                _bracketMesh[i].RecalculateBounds();
                BracketRebuilds++;
            }
            Transform g = rig.Group;
            _bracket[i].SetPositionAndRotation(g.position, g.rotation);
            _ring[i].SetPositionAndRotation(new Vector3(ringCentre.x, RingY, ringCentre.z), Quaternion.identity);
            _ring[i].localScale = new Vector3(ringScale, 1f, ringScale);
            Color c = ColourOf(look);
            Paint(_bracketM[i], c, BracketAlpha(look));
            Paint(_ringM[i], c, RingAlpha(look));
        }

        public void HideSlots()
        {
            for (int i = 0; i < Slots; i++) SetSlot(i, null, false, Look.Idle, Vector3.zero, Vector3.zero, Vector3.zero, 1f);
        }

        /// <summary>A hand's ray from <paramref name="from"/> to <paramref name="to"/> (light gold with a gold dot when it lands
        /// on an instrument, a short faint warm-white beam that fades out when it does not).</summary>
        public void SetRay(int h, bool visible, Vector3 from, Vector3 to, bool hits, Vector3 head)
        {
            RayVisible[h] = visible;
            RayHits[h] = visible && hits;
            RayFrom[h] = from;
            RayTo[h] = to;
            RayLength[h] = visible ? Vector3.Distance(from, to) : 0f;
            if (!_render) return;
            Show(_rayR[h], visible);
            Show(_dotR[h], visible && hits);
            if (!visible) return;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) { Show(_rayR[h], false); Show(_dotR[h], false); return; }
            _ray[h].SetPositionAndRotation(from, Quaternion.LookRotation(d / len, Mathf.Abs(d.y / len) > 0.99f ? Vector3.right : Vector3.up));
            _ray[h].localScale = new Vector3(1f, 1f, len);
            Paint(_rayM[h], hits ? PaleGold : Warm, RayAlpha(hits));
            if (!hits) return;
            Vector3 toHead = head - to;
            _dot[h].SetPositionAndRotation(to + (toHead.sqrMagnitude > 1e-6f ? toHead.normalized * 0.01f : Vector3.zero),
                toHead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-toHead.normalized) : Quaternion.identity);
            Paint(_dotM[h], Gold, 0.8f);
        }

        public void HideAll()
        {
            HideSlots();
            for (int h = 0; h < 2; h++) SetRay(h, false, Vector3.zero, Vector3.zero, false, Vector3.zero);
        }

        // ---------------------------------------------------------------- meshes

        void FillBrackets(Vector3 c, Vector3 size)
        {
            Vector3 e = size * 0.5f;
            float lx = Mathf.Clamp(size.x * ArmShare, ArmMin, ArmMax);
            float ly = Mathf.Clamp(size.y * ArmShare, ArmMin, ArmMax);
            float lz = Mathf.Clamp(size.z * ArmShare, ArmMin, ArmMax);
            int arm = 0;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 k = c + new Vector3(sx * e.x, sy * e.y, sz * e.z);
                        Tube(arm++, k, k + new Vector3(-sx * lx, 0f, 0f), 0);
                        Tube(arm++, k, k + new Vector3(0f, -sy * ly, 0f), 1);
                        Tube(arm++, k, k + new Vector3(0f, 0f, -sz * lz), 2);
                    }
        }

        /// <summary>A square tube from a to b along one local axis (0 x, 1 y, 2 z), width ArmWidth, extended by half a
        /// width at the corner end so the three arms meet in a clean corner.</summary>
        void Tube(int arm, Vector3 a, Vector3 b, int axis)
        {
            float w = ArmWidth * 0.5f;
            Vector3 dir = (b - a).normalized;
            a -= dir * w;
            Vector3 u = axis == 0 ? Vector3.up : Vector3.right;
            Vector3 v = axis == 2 ? Vector3.up : Vector3.forward;
            if (axis == 1) { u = Vector3.right; v = Vector3.forward; }
            Vector3 p0 = (-u - v) * w, p1 = (u - v) * w, p2 = (u + v) * w, p3 = (-u + v) * w;
            int o = arm * ArmVerts;
            Quad(o + 0, a + p0, a + p1, b + p1, b + p0);
            Quad(o + 4, a + p1, a + p2, b + p2, b + p1);
            Quad(o + 8, a + p2, a + p3, b + p3, b + p2);
            Quad(o + 12, a + p3, a + p0, b + p0, b + p3);
        }

        void Quad(int o, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            _verts[o] = a; _verts[o + 1] = b; _verts[o + 2] = c; _verts[o + 3] = d;
        }

        static int[] BracketIndices()
        {
            // Both windings: additive, unlit and thin, so drawing the inside faces too costs nothing and no edge-on gaps show.
            var idx = new int[Arms * ArmIdx * 2];
            int n = 0;
            for (int arm = 0; arm < Arms; arm++)
                for (int q = 0; q < 4; q++)
                {
                    int o = arm * ArmVerts + q * 4;
                    idx[n++] = o; idx[n++] = o + 1; idx[n++] = o + 2;
                    idx[n++] = o; idx[n++] = o + 2; idx[n++] = o + 3;
                    idx[n++] = o; idx[n++] = o + 2; idx[n++] = o + 1;
                    idx[n++] = o; idx[n++] = o + 3; idx[n++] = o + 2;
                }
            return idx;
        }

        /// <summary>A unit-length square tube along +z (scaled to the ray's length), fading in over its first 12 % and
        /// slightly toward the end (vertex alpha).</summary>
        static Mesh RayMesh()
        {
            float w = RayWidth * 0.5f;
            float[] zs = { 0f, 0.12f, 1f };
            float[] alphas = { 0f, 1f, 0.7f };
            var v = new Vector3[4 * zs.Length];
            var col = new Color[v.Length];
            Vector2[] ring = { new Vector2(-w, -w), new Vector2(w, -w), new Vector2(w, w), new Vector2(-w, w) };
            for (int s = 0; s < zs.Length; s++)
                for (int k = 0; k < 4; k++)
                {
                    v[s * 4 + k] = new Vector3(ring[k].x, ring[k].y, zs[s]);
                    col[s * 4 + k] = new Color(1f, 1f, 1f, alphas[s]);
                }
            var tris = new System.Collections.Generic.List<int>();
            for (int s = 0; s < zs.Length - 1; s++)
                for (int k = 0; k < 4; k++)
                {
                    int a = s * 4 + k, b = s * 4 + (k + 1) % 4, c = (s + 1) * 4 + (k + 1) % 4, d = (s + 1) * 4 + k;
                    tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(a); tris.Add(d); tris.Add(c);
                }
            var m = new Mesh { name = "ArrangeRay" };
            m.vertices = v;
            m.colors = col;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh DotMesh()
        {
            float s = DotSize * 0.5f;
            var m = new Mesh { name = "ArrangeRayDot" };
            m.vertices = new[] { new Vector3(-s, -s, 0f), new Vector3(s, -s, 0f), new Vector3(s, s, 0f), new Vector3(-s, s, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        public void Destroy()
        {
            for (int i = 0; i < Slots; i++) if (_bracketMesh[i] != null) { Object.Destroy(_bracketMesh[i]); _bracketMesh[i] = null; }
            if (_ringMesh != null) Object.Destroy(_ringMesh);
            if (_rayMesh != null) Object.Destroy(_rayMesh);
            if (_dotMesh != null) Object.Destroy(_dotMesh);
            _ringMesh = _rayMesh = _dotMesh = null;
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _render = false;
        }
    }
}
