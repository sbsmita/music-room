// The lesson cue on the skin (handdrums.md 5.1, ILessonTargets.ShowTarget): a matte hand print where the waited stroke
// lands, a purple mark (the lesson cue's colour, LessonCue) that fades in as the block falls (k 0..1). Six prints in one generated atlas (no third-party
// image): "any" a ring near the edge, "open" four flat finger pads from the edge inward, "slap" cupped fingertips, "bass" a
// whole palm print in the centre, "heel" the heel's crescent, "toe" four fingertip dots on the far side. Lit and
// transparent with no emission: it reads as a hand's mark on the hide, the falling purple block above it is the glow. One mesh of up to two quads (an event
// has at most two targets), one draw, shown only while a lesson waits. Allocation-free per call.
using System;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.HandDrums
{
    public static class HandDrumsPrints
    {
        public const string Name = "HandDrums/prints";
        public const int Cell = 256, Cols = 3, Rows = 2;
        public static readonly string[] Zones = { "any", "open", "slap", "bass", "heel", "toe" };
        /// <summary>Print size on the head (m): across, along (toward the centre).</summary>
        public static readonly Vector2[] Size = { new Vector2(0.075f, 0.075f), new Vector2(0.095f, 0.095f), new Vector2(0.095f, 0.085f), new Vector2(0.12f, 0.15f), new Vector2(0.095f, 0.075f), new Vector2(0.09f, 0.06f) };
        /// <summary>The print's centre on the head (rho, theta): the open tone's finger pads from the edge inward, the slap's
        /// tips at the edge, the bass and the heel in the middle, the toe on the far side.</summary>
        public static readonly Vector2[] At = { new Vector2(0.75f, 0f), new Vector2(0.60f, 0f), new Vector2(0.64f, 0f), new Vector2(0.08f, 0f), new Vector2(0.08f, 0f), new Vector2(0.5f, Mathf.PI) };

        public static int IndexOf(string zone) { int i = Array.IndexOf(Zones, zone); return i < 0 ? 0 : i; }

        static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float h = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            float qx = px - dx * h, qy = py - dy * h;
            return Mathf.Sqrt(qx * qx + qy * qy) - r;
        }

        static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float u = (x - cx) / rx, v = (y - cy) / ry;
            return (Mathf.Sqrt(u * u + v * v) - 1f) * Mathf.Min(rx, ry);
        }

        /// <summary>The signed distance of print i at cell coordinates (x, y) in -1..1 (y toward the head's centre).</summary>
        public static float Distance(int i, float x, float y)
        {
            float d = 9f;
            switch (i)
            {
                case 0: return Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.62f) - 0.11f;
                case 1:
                    for (int f = 0; f < 4; f++) { float fx = -0.54f + 0.36f * f, top = 0.62f - 0.12f * Mathf.Abs(f - 1.5f); d = Mathf.Min(d, Capsule(x, y, fx, -0.55f, fx, top, 0.15f)); }
                    return d;
                case 2:
                    for (int f = 0; f < 4; f++) { float fx = -0.5f + 0.333f * f, arch = 0.08f * (1f - Mathf.Abs(f - 1.5f) / 1.5f); d = Mathf.Min(d, Capsule(x, y, fx, 0.1f + arch, fx, 0.5f + arch, 0.13f)); }
                    return d;
                case 3:
                    d = Ellipse(x, y, 0.02f, -0.36f, 0.5f, 0.5f);
                    for (int f = 0; f < 4; f++) { float fx = -0.34f + 0.23f * f, top = 0.86f - 0.12f * Mathf.Abs(f - 1.4f); d = Mathf.Min(d, Capsule(x, y, fx, 0.18f, fx, top, 0.095f)); }
                    d = Mathf.Min(d, Capsule(x, y, -0.5f, -0.3f, -0.82f, 0.08f, 0.1f));
                    return d;
                case 4: return Mathf.Max(Ellipse(x, y, 0f, -0.1f, 0.62f, 0.5f), -Ellipse(x, y, 0f, 0.22f, 0.6f, 0.5f));
                case 5:
                    for (int f = 0; f < 4; f++) { float fx = -0.48f + 0.32f * f, fy = 0.06f * (1f - Mathf.Abs(f - 1.5f) / 1.5f); d = Mathf.Min(d, Mathf.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy)) - 0.14f); }
                    return d;
                default: return d;
            }
        }

        /// <summary>The atlas: white, alpha = the prints with soft 4 % edges.</summary>
        public static Texture2D Atlas()
        {
            int W = Cell * Cols, H = Cell * Rows;
            var px = new Color32[W * H];
            for (int i = 0; i < Zones.Length; i++)
            {
                int cx = i % Cols, cy = i / Cols;
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                    {
                        float u = (x + 0.5f) / Cell * 2f - 1f, v = (y + 0.5f) / Cell * 2f - 1f;
                        float a = Mathf.Clamp01(0.5f - Distance(i, u * 1.08f, v * 1.08f) / 0.05f);
                        px[(cy * Cell + y) * W + cx * Cell + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
            }
            var t = ProcTex.Make(W, H, px, false, true, "prints");
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        public static Texture2D Load() => GeneratedAssets.Texture(Name, Atlas);
    }

    public sealed class HandDrumsCue
    {
        /// <summary>The print's tint: the lesson cue's purple (LessonCue, every instrument's "play this next" colour; was a warm
        /// umber, and a pale cue vanished on the cream hide in the shots) and its strongest opacity.</summary>
        public static readonly Color Tint = LessonCue.Color;
        public const float MaxAlpha = 0.6f, Lift = 0.0015f;

        public GameObject Go { get; }
        public MeshRenderer Renderer { get; }
        public Material Material { get; }
        readonly Mesh _mesh;
        readonly Vector3[] _v = new Vector3[8];
        readonly Vector3[] _n = new Vector3[8];
        readonly Vector2[] _uv = new Vector2[8];
        readonly int[] _tri = { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
        readonly LessonTarget[] _slot = new LessonTarget[2];
        readonly float[] _k = new float[2];
        /// <summary>Tests: the zone index shown in each slot (-1: free).</summary>
        public readonly int[] ShownZone = { -1, -1 };

        public HandDrumsCue(Transform parent, MaterialLibrary lib, bool textures = true)
        {
            Go = new GameObject("Lesson print");
            Go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "HandDrums lesson print" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_v); _mesh.SetNormals(_n); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_tri, 0);
            _mesh.bounds = new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(0.9f, 0.4f, 0.6f));
            Go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            Renderer = Go.AddComponent<MeshRenderer>();
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = true;
            Material = lib.Clone(MaterialLibrary.LitTransparent);
            Material.name = "HandDrums/Print";
            Material.SetColor(MaterialLibrary.BaseColor, new Color(Tint.r, Tint.g, Tint.b, 0f));
            Material.SetFloat(MaterialLibrary.Smoothness, 0.08f);
            Material.SetFloat(MaterialLibrary.Metallic, 0f);
            if (textures) Material.SetTexture(MaterialLibrary.BaseMap, HandDrumsPrints.Load());
            Renderer.sharedMaterial = Material;
            Renderer.enabled = false;
        }

        public bool Visible => Renderer.enabled;
        public float Opacity => Material.GetColor(MaterialLibrary.BaseColor).a;

        /// <summary>ILessonTargets.ShowTarget: k 0 frees the target's print, else it shows at k (the stronger of the two sets
        /// the opacity).</summary>
        public void Show(LessonTarget t, float k)
        {
            if (t == null) return;
            int s = Array.IndexOf(_slot, t);
            if (k <= 0f)
            {
                if (s >= 0) { _slot[s] = null; _k[s] = 0f; ShownZone[s] = -1; Rebuild(); }
                return;
            }
            if (s < 0) s = _slot[0] == null ? 0 : _slot[1] == null ? 1 : (_k[0] <= _k[1] ? 0 : 1);
            _slot[s] = t;
            _k[s] = Mathf.Clamp01(k);
            Rebuild();
        }

        public void HideAll()
        {
            _slot[0] = _slot[1] = null; _k[0] = _k[1] = 0f; ShownZone[0] = ShownZone[1] = -1;
            Rebuild();
        }

        void Rebuild()
        {
            float k = 0f;
            Quaternion q = HandDrumsDims.HeadRotation;
            Vector3 n = q * Vector3.up, toward = q * Vector3.back;
            for (int s = 0; s < 2; s++)
            {
                int b = s * 4;
                LessonTarget t = _slot[s];
                int d = t != null ? (t.kind == "tumba" ? 1 : t.kind == "conga" ? 0 : -1) : -1;
                if (d < 0)
                {
                    ShownZone[s] = -1;
                    for (int i = 0; i < 4; i++) { _v[b + i] = Vector3.zero; _n[b + i] = n; _uv[b + i] = Vector2.zero; }
                    continue;
                }
                k = Mathf.Max(k, _k[s]);
                int z = HandDrumsPrints.IndexOf(t.zone);
                ShownZone[s] = z;
                Vector2 at = HandDrumsPrints.At[z], size = HandDrumsPrints.Size[z];
                Vector3 c = HandDrumsDims.HeadPoint(d, at.x, at.y, Lift);
                Vector3 along = -toward * (size.y * 0.5f), across = Vector3.Cross(n, -toward) * (size.x * 0.5f);   // along: toward the centre on the near side
                _v[b] = c - across - along; _v[b + 1] = c + across - along; _v[b + 2] = c + across + along; _v[b + 3] = c - across + along;
                float u0 = (z % HandDrumsPrints.Cols) / (float)HandDrumsPrints.Cols, v0 = (z / HandDrumsPrints.Cols) / (float)HandDrumsPrints.Rows;
                float du = 1f / HandDrumsPrints.Cols, dv = 1f / HandDrumsPrints.Rows;
                _uv[b] = new Vector2(u0, v0); _uv[b + 1] = new Vector2(u0 + du, v0); _uv[b + 2] = new Vector2(u0 + du, v0 + dv); _uv[b + 3] = new Vector2(u0, v0 + dv);
                for (int i = 0; i < 4; i++) _n[b + i] = n;
            }
            _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv);
            Material.SetColor(MaterialLibrary.BaseColor, new Color(Tint.r, Tint.g, Tint.b, MaxAlpha * k));
            Renderer.enabled = k > 0f;
        }
    }
}
