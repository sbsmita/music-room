// The panel's geometry (spec 08 sections 1.2-1.4): the curved face and back
// plate meshes, the poke surface coordinates, where the panel opens, and its grab zone. Pure functions, so EditMode
// tests can check them against the spec's numbers. Unity facing rule: local +z points away from the player, the visible
// side is -z, +x is the player's right; the cylinder's axis is 0.8 m toward the player (local z = -0.8).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public static class MenuMath
    {
        public const float R = MenuConstants.Radius, W = MenuConstants.Width, H = MenuConstants.Height;
        public const int Segments = 32;

        // Back plate: 6 mm larger than the face all round, its 512 x 384 canvas keeps a
        // 12 px margin for the glow, so the mesh is scaled by 512/488 and 384/360; 4 mm behind the face.
        public const float BackW = (W + 0.012f) * 512f / 488f;    // 0.3483
        public const float BackH = (H + 0.012f) * 384f / 360f;    // 0.2688
        public const float BackZ = 0.004f;

        // Where the panel opens.
        public const float OpenDistance = 0.5f, OpenBeyondHand = 0.35f, ReachMin = 0.30f, ReachMax = 0.55f, Drop = 0.12f;

        /// <summary>Vertex count of a curved plane with <see cref="Segments"/> segments.</summary>
        public const int PlaneVertices = (Segments + 1) * 2, PlaneIndices = Segments * 6;

        /// <summary>curvedPlane(w, h, r): u = i/32, a = (u - .5) w/r, vertex (r sin a, +-h/2, -r(1 - cos a)), uv (u, 0|1),
        /// normal (-sin a, 0, -cos a) toward the player, triangles (b, b+1, b+2), (b+1, b+3, b+2): clockwise seen from -z.</summary>
        public static void CurvedPlane(float w, float h, float r, Vector3[] pos, Vector3[] nor, Vector2[] uv, int[] tris)
        {
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                float a = (u - 0.5f) * (w / r);
                float x = r * Mathf.Sin(a), z = -r * (1f - Mathf.Cos(a));
                var n = new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a));
                for (int j = 0; j < 2; j++)
                {
                    int k = 2 * i + j;
                    pos[k] = new Vector3(x, (j - 0.5f) * h, z);
                    nor[k] = n;
                    uv[k] = new Vector2(u, j);
                }
                if (i < Segments)
                {
                    int b = 2 * i, t = 6 * i;
                    tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                    tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
                }
            }
        }

        public static Mesh BuildCurvedPlane(string name, float w, float h)
        {
            var pos = new Vector3[PlaneVertices];
            var nor = new Vector3[PlaneVertices];
            var uv = new Vector2[PlaneVertices];
            var tris = new int[PlaneIndices];
            CurvedPlane(w, h, R, pos, nor, uv, tris);
            var m = new Mesh { name = name };
            m.vertices = pos;
            m.normals = nor;
            m.uv = uv;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Local bounds of a curved plane: x +-r sin(w/2r), y +-h/2, z from -r(1 - cos(w/2r)) to 0.</summary>
        public static Bounds PlaneBounds(float w, float h, float zOffset)
        {
            float a = 0.5f * w / R;
            float sag = R * (1f - Mathf.Cos(a));
            return new Bounds(new Vector3(0f, 0f, zOffset - sag * 0.5f), new Vector3(2f * R * Mathf.Sin(a), h, sag));
        }

        /// <summary>ToSurface: panel-local point -> (u across the face in metres of arc, y, z out of the
        /// surface toward the player: z &gt; 0 in front, z &lt; 0 behind).</summary>
        public static void ToSurface(Vector3 l, out float u, out float y, out float z)
        {
            float dz = R + l.z;
            u = Mathf.Atan2(l.x, dz) * R;
            y = l.y;
            z = R - Mathf.Sqrt(l.x * l.x + dz * dz);
        }

        /// <summary>Inverse of <see cref="ToSurface"/>: (u, y, z) -> panel-local point.</summary>
        public static Vector3 SurfacePoint(float u, float y, float z)
        {
            float a = u / R, d = R - z;
            return new Vector3(d * Mathf.Sin(a), y, d * Mathf.Cos(a) - R);
        }

        /// <summary>The face's normal at arc position u (panel-local, toward the player).</summary>
        public static Vector3 SurfaceNormal(float u)
        {
            float a = u / R;
            return new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a));
        }

        /// <summary>(u, y) on the face -> canvas px (origin top-left, y down).</summary>
        public static void ToCanvas(float u, float y, out float px, out float py)
        {
            px = (u / W + 0.5f) * MenuConstants.CanvasW;
            py = (0.5f - y / H) * MenuConstants.CanvasH;
        }

        /// <summary>Canvas px -> (u, y) on the face.</summary>
        public static void FromCanvas(float px, float py, out float u, out float y)
        {
            u = (px / MenuConstants.CanvasW - 0.5f) * W;
            y = (0.5f - py / MenuConstants.CanvasH) * H;
        }

        /// <summary>The panel-local point of a canvas pixel at height z above the face (tests, demos).</summary>
        public static Vector3 CanvasPoint(float px, float py, float z)
        {
            FromCanvas(px, py, out float u, out float y);
            return SurfacePoint(u, y, z);
        }

        /// <summary>A point in the frame of a panel drawn at <paramref name="scale"/> (the Done / Reset buttons: the menu's own
        /// face scaled about its root) as the full-size panel's poke code reads it: the position on the face in full-size
        /// canvas terms, the height above the face kept in real metres, so ARM (3 mm), HOVER (10 mm) and BEHIND (-6 cm)
        /// mean what they mean on the palm menu.</summary>
        public static Vector3 ScaledToPanel(Vector3 local, float scale)
        {
            if (Mathf.Abs(scale - 1f) < 1e-6f) return local;
            ToSurface(local / scale, out float u, out float y, out float z);
            return SurfacePoint(u, y, z * scale);
        }

        /// <summary>Within the face plus the 1 cm edge margin (outside it a poke state resets).</summary>
        public static bool InsidePokeArea(float u, float y) =>
            Mathf.Abs(u) < W / 2f + MenuConstants.EdgeMargin && Mathf.Abs(y) < H / 2f + MenuConstants.EdgeMargin;

        /// <summary>PlaceFor: in front of the summoning hand (or the way the player looks), 0.12 m below
        /// the eyes, facing the player; over the keyboard it moves to 1 cm in front of the key fronts so the panel is
        /// never inside the piano and the lesson blocks fall behind it. <paramref name="piano"/> is null when the piano
        /// is not shown.</summary>
        public static void PlaceFor(Vector3 head, Vector3 headForward, bool hasPalm, Vector3 palmCenter, Transform piano,
            out Vector3 position, out Quaternion rotation)
        {
            Vector3 dir = headForward;
            dir.y = 0f;
            float len = dir.magnitude;
            dir = len < 1e-3f ? Vector3.forward : dir / len;
            float dist = OpenDistance;
            if (hasPalm)
            {
                Vector3 v = palmCenter - head;
                v.y = 0f;
                float l = v.magnitude;
                if (l > 0.05f)
                {
                    dir = v / l;
                    dist = Mathf.Clamp(l + OpenBeyondHand, ReachMin, ReachMax);
                }
            }
            position = head + dir * dist;
            position.y = head.y - Drop;
            if (piano != null)
            {
                Vector3 lp = piano.InverseTransformPoint(position);
                // Piano-local (Unity): +z is behind the white-key fronts.
                if (Mathf.Abs(lp.x) < 0.8f && lp.y > -0.2f && lp.y < 0.75f && lp.z > -0.01f && lp.z < 0.9f)
                {
                    lp.z = -0.01f;
                    Vector3 w = piano.TransformPoint(lp);
                    position.x = w.x;
                    position.z = w.z;
                    // A keyboard within a foot of the player (the reach band) would bring the panel into the face: it
                    // stays at least ReachMin away, over the keys.
                    Vector3 h = new Vector3(position.x - head.x, 0f, position.z - head.z);
                    if (h.magnitude < ReachMin)
                    {
                        Vector3 along = h.sqrMagnitude > 1e-6f ? h.normalized : dir;
                        position.x = head.x + along.x * ReachMin;
                        position.z = head.z + along.z * ReachMin;
                    }
                }
            }
            rotation = FacingFrom(head, position);
        }

        /// <summary>Yaw so the panel's +z points away from the head (Euler(0, atan2(pos.x - head.x, pos.z - head.z), 0)).</summary>
        public static Quaternion FacingFrom(Vector3 head, Vector3 position) =>
            Quaternion.Euler(0f, Mathf.Atan2(position.x - head.x, position.z - head.z) * Mathf.Rad2Deg, 0f);

        static readonly Bounds s_faceBounds = PlaneBounds(W, H, 0f);
        static readonly Bounds s_backBounds = PlaneBounds(BackW, BackH, BackZ);

        /// <summary>The grab zone: world AABB of the face and back plate (at scale 1) padded by ZonePad (Box3.expandByScalar(.12)).</summary>
        public static Bounds GrabZone(Matrix4x4 localToWorld)
        {
            Bounds b = new Bounds(localToWorld.MultiplyPoint3x4(s_faceBounds.center), Vector3.zero);
            Encapsulate(ref b, localToWorld, s_faceBounds);
            Encapsulate(ref b, localToWorld, s_backBounds);
            b.Expand(2f * GrabConstants.ZonePad);
            return b;
        }

        static void Encapsulate(ref Bounds b, Matrix4x4 m, Bounds local)
        {
            Vector3 c = local.center, e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                b.Encapsulate(m.MultiplyPoint3x4(p));
            }
        }

        /// <summary>The summon ring (08-ux-visuals.json summonRing): annulus .028-.036 m, 40 segments, both faces.</summary>
        public static Mesh BuildRing(float inner = 0.028f, float outer = 0.036f, int segments = 40)
        {
            var pos = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[pos.Length];
            var tris = new int[segments * 12];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                pos[2 * i] = new Vector3(c * inner, s * inner, 0f);
                pos[2 * i + 1] = new Vector3(c * outer, s * outer, 0f);
                uv[2 * i] = new Vector2(i / (float)segments, 0f);
                uv[2 * i + 1] = new Vector2(i / (float)segments, 1f);
                if (i < segments)
                {
                    int b = 2 * i, t = 12 * i;
                    // front and back windings: the ring is double-sided (no material Cull state needed)
                    tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                    tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
                    tris[t + 6] = b; tris[t + 7] = b + 2; tris[t + 8] = b + 1;
                    tris[t + 9] = b + 1; tris[t + 10] = b + 2; tris[t + 11] = b + 3;
                }
            }
            var m = new Mesh { name = "MenuRing" };
            m.vertices = pos;
            m.uv = uv;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>easeOut(k) = 1 - (1 - k)^2 (the open / close animation).</summary>
        public static float EaseOut(float k) => 1f - (1f - k) * (1f - k);
    }
}
