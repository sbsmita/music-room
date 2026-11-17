// Sunny's procedural meshes (Docs/08-ux-lessons-menu-guide.md 3.1): the low-poly face sphere, the corona / bubble / row quads, the ghost
// hand's palm box, capsule fingers and cone, the charging ring, and the line meshes of the ghost panel and the dotted
// arrow. Built once per session and shared (they never change). Shapes are generated in Unity coordinates directly;
// closed solids are wound outward with OrientOutward because the source frame's index order flips under the z mirror.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Guide
{
    public static class SunnyMeshes
    {
        public const int SphereWidthSegments = 20, SphereHeightSegments = 14;
        // Capsule (r, l, 3 cap segments, 8 sides): 2 x 3 points per quarter arc. First five: 16 sides (was 8) and a
        // rounded palm, so the ghost hand reads as a soft pale hand up close instead of a box and octagonal sticks.
        public const int CapsuleCapDivisions = 6, CapsuleRadialSegments = 16;
        /// <summary>The palm's rounding (first five): 2.6 cm in the palm's plane (a soft hand outline, not a box) and the
        /// palm's half thickness through it (fully round edges), and the subdivisions per face edge.</summary>
        public static readonly Vector3 PalmRounding = new Vector3(0.026f, 0.026f, 0.011f);
        public const int PalmDivisions = 8;

        static Mesh s_body, s_corona, s_bubble, s_palm, s_thumb, s_cone, s_ring, s_ghost, s_row, s_dashes;
        static readonly Mesh[] s_fingers = new Mesh[4];

        public static Mesh Body => s_body != null ? s_body : (s_body = Sphere(GuideTiming.Radius, SphereWidthSegments, SphereHeightSegments));
        public static Mesh Corona => s_corona != null ? s_corona : (s_corona = Quad(SunnyView.CoronaSize, SunnyView.CoronaSize, "SunnyCorona"));
        public static Mesh Bubble => s_bubble != null ? s_bubble : (s_bubble = Quad(SunnyView.BubbleSize.x, SunnyView.BubbleSize.y, "SunnyBubble"));
        public static Mesh Palm => s_palm != null ? s_palm : (s_palm = RoundedBox(SunnyDemo.PalmBox, PalmRounding, PalmDivisions, "SunnyPalm"));
        public static Mesh Thumb => s_thumb != null ? s_thumb : (s_thumb = Capsule(SunnyDemo.FingerRadius, SunnyDemo.StraightLength(SunnyDemo.ThumbLen), "SunnyThumb"));
        public static Mesh Cone => s_cone != null ? s_cone : (s_cone = ConeMesh(SunnyDemo.ConeR, SunnyDemo.ConeH, SunnyDemo.ConeSides));
        public static Mesh ChargeRing => s_ring != null ? s_ring : (s_ring = Ring(SunnyDemo.RingInner, SunnyDemo.RingOuter, SunnyDemo.RingSegments));
        public static Mesh GhostPanel => s_ghost != null ? s_ghost : (s_ghost = Lines(GhostPanelPoints(), "SunnyGhostPanel"));
        public static Mesh RowPlate => s_row != null ? s_row : (s_row = Quad(SunnyDemo.RowPlate.x, SunnyDemo.RowPlate.y, "SunnyRowPlate"));
        public static Mesh Dashes => s_dashes != null ? s_dashes : (s_dashes = Lines(DashPoints(), "SunnyDashes"));

        public static Mesh Finger(int i)
        {
            if (s_fingers[i] == null) s_fingers[i] = Capsule(SunnyDemo.FingerRadius, SunnyDemo.StraightLength(SunnyDemo.FingerLen[i]), "SunnyFinger" + i);
            return s_fingers[i];
        }

        /// <summary>UV sphere (r, 20 x 14 segments) mirrored: vertex (-r cos(phi) sin(theta), r cos(theta), -r sin(phi) sin(theta)),
        /// phi = 2 pi u, theta = pi v (v from the top), uv (u, 1 - v), so the face painted at u = 0.25 looks along local -z.</summary>
        public static Mesh Sphere(float r, int ws, int hs)
        {
            var v = new Vector3[(ws + 1) * (hs + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int iy = 0, k = 0; iy <= hs; iy++)
            {
                float vv = (float)iy / hs;
                // The pole rows shift u by half a segment so the pole triangles map sensibly.
                float uOffset = iy == 0 ? 0.5f / ws : iy == hs ? -0.5f / ws : 0f;
                for (int ix = 0; ix <= ws; ix++, k++)
                {
                    float u = (float)ix / ws;
                    float phi = u * Mathf.PI * 2f, theta = vv * Mathf.PI;
                    var p = new Vector3(-r * Mathf.Cos(phi) * Mathf.Sin(theta), r * Mathf.Cos(theta), -r * Mathf.Sin(phi) * Mathf.Sin(theta));
                    v[k] = p;
                    n[k] = p / r;
                    uv[k] = new Vector2(u + uOffset, 1f - vv);
                }
            }
            var tris = new List<int>(ws * hs * 6);
            for (int iy = 0; iy < hs; iy++)
                for (int ix = 0; ix < ws; ix++)
                {
                    int a = iy * (ws + 1) + ix + 1, b = iy * (ws + 1) + ix, c = (iy + 1) * (ws + 1) + ix, d = (iy + 1) * (ws + 1) + ix + 1;
                    if (iy != 0) { tris.Add(a); tris.Add(b); tris.Add(d); }
                    if (iy != hs - 1) { tris.Add(b); tris.Add(c); tris.Add(d); }
                }
            int[] t = tris.ToArray();
            OrientOutward(v, t, Vector3.zero);
            return Build("SunnyBody", v, n, uv, t);
        }

        /// <summary>A w x h quad in the XY plane whose front faces -z (the player's side), uv (0,0) bottom-left.</summary>
        public static Mesh Quad(float w, float h, string name)
        {
            float x = w * 0.5f, y = h * 0.5f;
            var v = new[] { new Vector3(-x, -y, 0f), new Vector3(-x, y, 0f), new Vector3(x, y, 0f), new Vector3(x, -y, 0f) };
            var n = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            return Build(name, v, n, uv, new[] { 0, 1, 2, 0, 2, 3 });
        }

        /// <summary>Box (sx, sy, sz): 24 vertices, outward faces.</summary>
        public static Mesh Box(Vector3 size, string name)
        {
            Vector3 h = size * 0.5f;
            var v = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var t = new List<int>(36);
            Face(v, n, uv, t, Vector3.right, Vector3.forward, Vector3.up, h.x, h.z, h.y);
            Face(v, n, uv, t, Vector3.left, Vector3.back, Vector3.up, h.x, h.z, h.y);
            Face(v, n, uv, t, Vector3.up, Vector3.right, Vector3.forward, h.y, h.x, h.z);
            Face(v, n, uv, t, Vector3.down, Vector3.right, Vector3.back, h.y, h.x, h.z);
            Face(v, n, uv, t, Vector3.forward, Vector3.left, Vector3.up, h.z, h.x, h.y);
            Face(v, n, uv, t, Vector3.back, Vector3.right, Vector3.up, h.z, h.x, h.y);
            var va = v.ToArray();
            var ta = t.ToArray();
            OrientOutward(va, ta, Vector3.zero);
            return Build(name, va, n.ToArray(), uv.ToArray(), ta);
        }

        /// <summary>A box of this size with its edges and corners rounded by the ellipsoid radii r (per axis; first five: the
        /// ghost hand's palm): a cube subdivided d x d per face whose points are pulled onto the rounded surface (the
        /// Minkowski sum of the inner box and the ellipsoid), with smooth normals.</summary>
        public static Mesh RoundedBox(Vector3 size, Vector3 r, int d, string name)
        {
            Vector3 h = size * 0.5f;
            r = new Vector3(Mathf.Min(r.x, h.x), Mathf.Min(r.y, h.y), Mathf.Min(r.z, h.z));
            Vector3 inner = h - r;
            var v = new List<Vector3>(6 * (d + 1) * (d + 1));
            var n = new List<Vector3>(v.Capacity);
            var uv = new List<Vector2>(v.Capacity);
            var t = new List<int>(6 * d * d * 6);
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 f in normals)
            {
                Vector3 a = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(f, a);
                int o = v.Count;
                for (int i = 0; i <= d; i++)
                    for (int j = 0; j <= d; j++)
                    {
                        float u = -1f + 2f * i / d, w = -1f + 2f * j / d;
                        Vector3 c = f + a * u + b * w;                                   // on the unit cube
                        Vector3 p = Vector3.Scale(c, h);                                 // on the box
                        Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 dir = new Vector3((p.x - q.x) / Mathf.Max(r.x, 1e-6f), (p.y - q.y) / Mathf.Max(r.y, 1e-6f), (p.z - q.z) / Mathf.Max(r.z, 1e-6f));
                        Vector3 unit = dir.sqrMagnitude > 1e-12f ? dir.normalized : f;
                        v.Add(q + Vector3.Scale(unit, r));
                        Vector3 nn = new Vector3(unit.x / Mathf.Max(r.x, 1e-6f), unit.y / Mathf.Max(r.y, 1e-6f), unit.z / Mathf.Max(r.z, 1e-6f)).normalized;
                        n.Add(nn);
                        uv.Add(new Vector2((float)i / d, (float)j / d));
                    }
                for (int i = 0; i < d; i++)
                    for (int j = 0; j < d; j++)
                    {
                        int k0 = o + i * (d + 1) + j, k1 = k0 + 1, k2 = k0 + d + 1, k3 = k2 + 1;
                        t.Add(k0); t.Add(k1); t.Add(k3);
                        t.Add(k0); t.Add(k3); t.Add(k2);
                    }
            }
            var va = v.ToArray();
            var ta = t.ToArray();
            OrientOutward(va, ta, Vector3.zero);
            return Build(name, va, n.ToArray(), uv.ToArray(), ta);
        }

        static void Face(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 normal, Vector3 a, Vector3 b, float dn, float da, float db)
        {
            int o = v.Count;
            Vector3 c = normal * dn;
            v.Add(c - a * da - b * db); v.Add(c - a * da + b * db); v.Add(c + a * da + b * db); v.Add(c + a * da - b * db);
            for (int i = 0; i < 4; i++) n.Add(normal);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(1f, 0f));
            t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3);
        }

        /// <summary>Capsule (radius, length, 3 cap segments, 8 sides) centred on its origin along y: a lathe of two quarter
        /// arcs (6 steps each) joined by the straight side.</summary>
        public static Mesh Capsule(float radius, float length, string name)
        {
            int prof = 2 * (CapsuleCapDivisions + 1);
            var px = new float[prof];
            var py = new float[prof];
            for (int k = 0; k <= CapsuleCapDivisions; k++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * k / CapsuleCapDivisions;
                px[k] = radius * Mathf.Cos(a);
                py[k] = -length * 0.5f + radius * Mathf.Sin(a);
                float b = Mathf.PI * 0.5f * k / CapsuleCapDivisions;
                px[CapsuleCapDivisions + 1 + k] = radius * Mathf.Cos(b);
                py[CapsuleCapDivisions + 1 + k] = length * 0.5f + radius * Mathf.Sin(b);
            }
            int seg = CapsuleRadialSegments;
            var v = new Vector3[prof * (seg + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int j = 0; j < prof; j++)
            {
                float cy = j <= CapsuleCapDivisions ? -length * 0.5f : length * 0.5f;
                for (int i = 0; i <= seg; i++)
                {
                    float phi = Mathf.PI * 2f * i / seg;
                    var p = new Vector3(px[j] * Mathf.Cos(phi), py[j], px[j] * Mathf.Sin(phi));
                    int k = j * (seg + 1) + i;
                    v[k] = p;
                    Vector3 c = new Vector3(0f, cy, 0f);
                    n[k] = (p - c).sqrMagnitude > 1e-12f ? (p - c).normalized : (cy < 0f ? Vector3.down : Vector3.up);
                    uv[k] = new Vector2((float)i / seg, (float)j / (prof - 1));
                }
            }
            var t = new List<int>((prof - 1) * seg * 6);
            for (int j = 0; j < prof - 1; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                    if (px[j] > 1e-7f) { t.Add(a); t.Add(b); t.Add(d); }      // skip the degenerate pole triangles
                    if (px[j + 1] > 1e-7f) { t.Add(a); t.Add(d); t.Add(c); }
                }
            var ta = t.ToArray();
            OrientOutward(v, ta, Vector3.zero);
            return Build(name, v, n, uv, ta);
        }

        /// <summary>Cone (r, h, sides): apex at +h/2, base (with its cap) at -h/2.</summary>
        public static Mesh ConeMesh(float r, float h, int sides)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float slope = r / h;
            for (int i = 0; i < sides; i++)
            {
                float a0 = Mathf.PI * 2f * i / sides, a1 = Mathf.PI * 2f * (i + 1) / sides, am = (a0 + a1) * 0.5f;
                int o = v.Count;
                v.Add(new Vector3(0f, h * 0.5f, 0f));
                v.Add(new Vector3(r * Mathf.Cos(a0), -h * 0.5f, r * Mathf.Sin(a0)));
                v.Add(new Vector3(r * Mathf.Cos(a1), -h * 0.5f, r * Mathf.Sin(a1)));
                n.Add(new Vector3(Mathf.Cos(am), slope, Mathf.Sin(am)).normalized);
                n.Add(new Vector3(Mathf.Cos(a0), slope, Mathf.Sin(a0)).normalized);
                n.Add(new Vector3(Mathf.Cos(a1), slope, Mathf.Sin(a1)).normalized);
                t.Add(o); t.Add(o + 1); t.Add(o + 2);
            }
            int centre = v.Count;
            v.Add(new Vector3(0f, -h * 0.5f, 0f));
            n.Add(Vector3.down);
            for (int i = 0; i <= sides; i++)
            {
                float a = Mathf.PI * 2f * i / sides;
                v.Add(new Vector3(r * Mathf.Cos(a), -h * 0.5f, r * Mathf.Sin(a)));
                n.Add(Vector3.down);
            }
            for (int i = 0; i < sides; i++) { t.Add(centre); t.Add(centre + 1 + i); t.Add(centre + 2 + i); }
            var va = v.ToArray();
            var ta = t.ToArray();
            OrientOutward(va, ta, Vector3.zero);
            return Build("SunnyCone", va, n.ToArray(), new Vector2[va.Length], ta);
        }

        /// <summary>Ring (inner, outer, segments): a flat annulus in the XY plane (drawn double-sided).</summary>
        public static Mesh Ring(float inner, float outer, int segments)
        {
            var v = new Vector3[2 * (segments + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i] = new Vector3(inner * c, inner * s, 0f);
                v[segments + 1 + i] = new Vector3(outer * c, outer * s, 0f);
                n[i] = n[segments + 1 + i] = Vector3.back;
                uv[i] = new Vector2((inner / outer * c + 1f) * 0.5f, (inner / outer * s + 1f) * 0.5f);
                uv[segments + 1 + i] = new Vector2((c + 1f) * 0.5f, (s + 1f) * 0.5f);
            }
            var t = new int[segments * 6];
            for (int i = 0, k = 0; i < segments; i++)
            {
                int a = i, b = i + 1, c = segments + 1 + i, d = c + 1;
                t[k++] = a; t[k++] = c; t[k++] = d;
                t[k++] = a; t[k++] = d; t[k++] = b;
            }
            return Build("SunnyRing", v, n, uv, t);
        }

        /// <summary>A line-segment mesh (pairs of points, 1 px lines).</summary>
        public static Mesh Lines(Vector3[] points, string name)
        {
            var m = new Mesh { name = name };
            m.vertices = points;
            var idx = new int[points.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            m.colors32 = White(points.Length);
            m.SetIndices(idx, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The ghost panel outline (0.13 x 0.095) and its three row lines.</summary>
        public static Vector3[] GhostPanelPoints()
        {
            float x = SunnyDemo.GhostW * 0.5f, y = SunnyDemo.GhostH * 0.5f;
            var p = new List<Vector3>(14)
            {
                new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f),
                new Vector3(x, -y, 0f), new Vector3(x, y, 0f),
                new Vector3(x, y, 0f), new Vector3(-x, y, 0f),
                new Vector3(-x, y, 0f), new Vector3(-x, -y, 0f),
            };
            for (int i = 0; i < SunnyDemo.GhostRowY.Length; i++)
            {
                p.Add(new Vector3(-SunnyDemo.GhostRowHalf, SunnyDemo.GhostRowY[i], 0f));
                p.Add(new Vector3(SunnyDemo.GhostRowHalf, SunnyDemo.GhostRowY[i], 0f));
            }
            return p.ToArray();
        }

        /// <summary>The dotted arrow's dashes: LineDashedMaterial dash .01 gap .012 measured from x = .15 toward -.13.</summary>
        public static Vector3[] DashPoints()
        {
            var p = new List<Vector3>(32);
            float length = SunnyDemo.DashFrom - SunnyDemo.DashTo;
            for (float d = 0f; d < length - 1e-6f; d += SunnyDemo.Dash + SunnyDemo.DashGap)
            {
                float e = Mathf.Min(length, d + SunnyDemo.Dash);
                p.Add(new Vector3(SunnyDemo.DashFrom - d, 0f, 0f));
                p.Add(new Vector3(SunnyDemo.DashFrom - e, 0f, 0f));
            }
            return p.ToArray();
        }

        /// <summary>Winds every triangle so its front (Unity: clockwise as seen, Cross(b - a, c - a) toward the viewer) faces
        /// away from `centre`: right for the convex solids here, whatever order the generator produced.</summary>
        public static void OrientOutward(Vector3[] v, int[] tris, Vector3 centre)
        {
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(cross, (a + b + c) / 3f - centre) < 0f)
                {
                    int tmp = tris[i + 1];
                    tris[i + 1] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
            }
        }

        static Mesh Build(string name, Vector3[] v, Vector3[] n, Vector2[] uv, int[] t)
        {
            var m = new Mesh { name = name };
            m.vertices = v;
            m.normals = n;
            m.uv = uv;
            m.colors32 = White(v.Length);   // SunnyUnlit multiplies by the vertex colour (the merged demo mesh carries per-part opacity)
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        static Color32[] White(int n)
        {
            var c = new Color32[n];
            for (int i = 0; i < n; i++) c[i] = new Color32(255, 255, 255, 255);
            return c;
        }
    }
}
