// Procedural meshes of the Lessons module, built directly in Unity coordinates. Every shape here is symmetric about the
// z = 0 plane, so the right-handed -> Unity mirror (Coords.MirrorZ) changes nothing but the winding, which each builder
// sets for Unity: a front face satisfies dot(cross(b - a, c - a), outward) > 0 (clockwise as seen from outside).
//   blocks: unit box and unit 32-sided cylinder with the pivot on the bottom face (a block shrinks
//   into its key / head), and a capsule (0.006, 0.028, 4, 10) centred on the origin along +y;
//   deck: boxes, tapered cylinders and a UV sphere, each centred on its own origin.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public static class LessonMeshes
    {
        // ---------------- blocks ----------------

        /// <summary>Box(1, 1, 1) moved up .5: x, z in [-.5, .5], y in [0, 1].</summary>
        public static Mesh BlockBox() => Box(Vector3.one, new Vector3(0f, 0.5f, 0f), "LessonBlockBox");

        /// <summary>Cylinder(1, 1, 1, 32) moved up .5: radius 1, y in [0, 1].</summary>
        public static Mesh BlockDisc() => Cylinder(1f, 1f, 1f, 32, new Vector3(0f, 0.5f, 0f), "LessonBlockDisc");

        /// <summary>Capsule(radius, height = straight middle, capSegments, radialSegments), centred, axis +y.</summary>
        public static Mesh BlockCapsule(float radius = 0.006f, float straight = 0.028f, int capSegments = 4, int radialSegments = 10)
        {
            var b = new Builder();
            int rings = capSegments * 2 + 2;      // top pole ring .. bottom pole ring, both cap equators included
            var ringY = new float[rings];
            var ringR = new float[rings];
            int n = 0;
            for (int i = 0; i <= capSegments; i++)   // top cap: pole -> equator
            {
                float a = Mathf.PI * 0.5f * (1f - i / (float)capSegments);
                ringY[n] = straight * 0.5f + radius * Mathf.Sin(a);
                ringR[n++] = radius * Mathf.Cos(a);
            }
            for (int i = 0; i <= capSegments; i++)   // bottom cap: equator -> pole
            {
                float a = Mathf.PI * 0.5f * (i / (float)capSegments);
                ringY[n] = -straight * 0.5f - radius * Mathf.Sin(a);
                ringR[n++] = radius * Mathf.Cos(a);
            }
            int stride = radialSegments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s <= radialSegments; s++)
                {
                    float phi = s / (float)radialSegments * Mathf.PI * 2f;
                    var p = new Vector3(ringR[r] * Mathf.Sin(phi), ringY[r], ringR[r] * Mathf.Cos(phi));
                    float cy = ringY[r] > 0f ? straight * 0.5f : -straight * 0.5f;
                    Vector3 nrm = new Vector3(p.x, p.y - cy, p.z);
                    b.V(p, nrm.sqrMagnitude > 1e-12f ? nrm.normalized : (ringY[r] > 0f ? Vector3.up : Vector3.down));
                }
            for (int r = 0; r + 1 < rings; r++)
                for (int s = 0; s < radialSegments; s++)
                {
                    int i0 = r * stride + s, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
                    b.TriOut(i0, i2, i1, Vector3.zero);
                    b.TriOut(i1, i2, i3, Vector3.zero);
                }
            return b.Build("LessonBlockCapsule");
        }

        /// <summary>The platform's ring block (LessonBlockShape.Ring, a bowl's rim): a flat annulus, outer radius 1, inner
        /// RingInner, y in [0, 1], pivot on its bottom face.</summary>
        public const float RingInner = 0.72f;
        public static Mesh BlockRing(int segments = 40)
        {
            var b = new Builder();
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 o0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), o1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                Vector3 i0 = o0 * RingInner, i1 = o1 * RingInner;
                Vector3 up = Vector3.up;
                b.Quad(o0 + up, o1 + up, i1 + up, i0 + up, Vector3.up);          // top
                b.Quad(o0, i0, i1, o1, Vector3.down);                            // bottom
                Vector3 mid = (o0 + o1).normalized;
                b.Quad(o0, o1, o1 + up, o0 + up, mid);                           // outer wall
                b.Quad(i0, i0 + up, i1 + up, i1, -mid);                          // inner wall
            }
            return b.Build("LessonBlockRing");
        }

        /// <summary>The platform's bar block (LessonBlockShape.Bar, a marimba bar): a slab with rounded
        /// corners, x and z in [-.5, .5] (corner radius .25), y in [0, 1], pivot on its bottom face.</summary>
        public static Mesh BlockBar(int cornerSegments = 6)
        {
            const float r = 0.25f, h = 0.5f;
            var outline = new List<Vector3>(4 * (cornerSegments + 1));
            Vector3[] centres = { new Vector3(h - r, 0f, h - r), new Vector3(-(h - r), 0f, h - r), new Vector3(-(h - r), 0f, -(h - r)), new Vector3(h - r, 0f, -(h - r)) };
            for (int c = 0; c < 4; c++)
                for (int k = 0; k <= cornerSegments; k++)
                {
                    float a = (c + k / (float)cornerSegments) * Mathf.PI * 0.5f;   // 0 at +x, counter-clockwise seen from above
                    outline.Add(centres[c] + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
                }
            var b = new Builder();
            int n = outline.Count;
            int top = b.V(Vector3.up, Vector3.up), bottom = b.V(Vector3.zero, Vector3.down);
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = outline[i], p1 = outline[(i + 1) % n];
                int t0 = b.V(p0 + Vector3.up, Vector3.up), t1 = b.V(p1 + Vector3.up, Vector3.up);
                b.TriOut(top, t0, t1, Vector3.up);
                int b0 = b.V(p0, Vector3.down), b1 = b.V(p1, Vector3.down);
                b.TriOut(bottom, b0, b1, Vector3.down);
                Vector3 side = Vector3.Cross(p1 - p0, Vector3.up).normalized;
                if (Vector3.Dot(side, p0 + p1) < 0f) side = -side;
                b.Quad(p0, p1, p1 + Vector3.up, p0 + Vector3.up, side);
            }
            return b.Build("LessonBlockBar");
        }

        // ---------------- deck parts ----------------

        /// <summary>Box(size) centred on <paramref name="center"/> (24 vertices, flat normals).</summary>
        public static Mesh Box(Vector3 size, Vector3 center, string name = "Box")
        {
            var b = new Builder();
            b.AddBox(size, center, Quaternion.identity);
            return b.Build(name);
        }

        /// <summary>Cylinder(rTop, rBottom, height, segments) centred on <paramref name="center"/>, both caps.</summary>
        public static Mesh Cylinder(float rTop, float rBottom, float height, int segments, Vector3 center, string name = "Cylinder")
        {
            var b = new Builder();
            b.AddCylinder(rTop, rBottom, height, segments, center);
            return b.Build(name);
        }

        /// <summary>Sphere(radius, widthSegments, heightSegments) centred on the origin.</summary>
        public static Mesh Sphere(float radius, int widthSegments, int heightSegments, string name = "Sphere")
        {
            var b = new Builder();
            int stride = widthSegments + 1;
            for (int y = 0; y <= heightSegments; y++)
            {
                float theta = y / (float)heightSegments * Mathf.PI;
                for (int x = 0; x <= widthSegments; x++)
                {
                    float phi = x / (float)widthSegments * Mathf.PI * 2f;
                    var nrm = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
                    b.V(nrm * radius, nrm);
                }
            }
            for (int y = 0; y < heightSegments; y++)
                for (int x = 0; x < widthSegments; x++)
                {
                    int i0 = y * stride + x, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
                    if (y != 0) b.TriOut(i0, i2, i1, Vector3.zero);
                    if (y != heightSegments - 1) b.TriOut(i1, i2, i3, Vector3.zero);
                }
            return b.Build(name);
        }

        /// <summary>The reel spokes: three 0.1 x 0.004 x 0.008 bars turned i/3 * pi about y, lifted 0.008, merged.</summary>
        public static Mesh Spokes(string name = "Spokes")
        {
            var b = new Builder();
            // Unity yaw = -right-handed yaw; the three bars are 60 degrees apart either way, so the set is the same.
            for (int i = 0; i < 3; i++) b.AddBox(new Vector3(0.1f, 0.004f, 0.008f), new Vector3(0f, 0.008f, 0f), Quaternion.Euler(0f, -i * 60f, 0f));
            return b.Build(name);
        }

        /// <summary>Vertex / triangle accumulator that fixes the winding of every triangle against an outward direction.</summary>
        sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<int> _t = new List<int>();

            public int V(Vector3 p, Vector3 n) { _v.Add(p); _n.Add(n); return _v.Count - 1; }

            /// <summary>Adds (a, b, c) so its front faces <paramref name="outward"/> (zero = the vertex normals' mean).</summary>
            public void TriOut(int a, int b, int c, Vector3 outward)
            {
                Vector3 pa = _v[a], pb = _v[b], pc = _v[c];
                Vector3 face = Vector3.Cross(pb - pa, pc - pa);
                if (face.sqrMagnitude < 1e-20f) return;                  // degenerate (a pole quad's collapsed half)
                if (outward == Vector3.zero) outward = _n[a] + _n[b] + _n[c];
                if (Vector3.Dot(face, outward) >= 0f) { _t.Add(a); _t.Add(b); _t.Add(c); }
                else { _t.Add(a); _t.Add(c); _t.Add(b); }
            }

            /// <summary>A flat quad (a, b, c, d around its edge) facing <paramref name="outward"/>, flat normal.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Vector3 n = outward.normalized;
                int i0 = V(a, n), i1 = V(b, n), i2 = V(c, n), i3 = V(d, n);
                TriOut(i0, i1, i2, n);
                TriOut(i0, i2, i3, n);
            }

            public void AddBox(Vector3 size, Vector3 center, Quaternion rot)
            {
                Vector3 h = size * 0.5f;
                for (int axis = 0; axis < 3; axis++)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 n = Vector3.zero; n[axis] = sign;
                        Vector3 u = Vector3.zero; u[(axis + 1) % 3] = 1f;
                        Vector3 w = Vector3.zero; w[(axis + 2) % 3] = 1f;
                        Vector3 c = Vector3.Scale(n, h);
                        Vector3 du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
                        Vector3 nn = rot * n;
                        int i0 = V(center + rot * (c - du - dw), nn);
                        int i1 = V(center + rot * (c + du - dw), nn);
                        int i2 = V(center + rot * (c + du + dw), nn);
                        int i3 = V(center + rot * (c - du + dw), nn);
                        TriOut(i0, i1, i2, nn);
                        TriOut(i0, i2, i3, nn);
                    }
            }

            public void AddCylinder(float rTop, float rBottom, float height, int segments, Vector3 center)
            {
                float hy = height * 0.5f;
                float slope = (rBottom - rTop) / height;             // side normal tilt for a tapered cylinder
                int start = _v.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float phi = s / (float)segments * Mathf.PI * 2f;
                    float sx = Mathf.Sin(phi), cz = Mathf.Cos(phi);
                    Vector3 nrm = new Vector3(sx, slope, cz).normalized;
                    V(center + new Vector3(rTop * sx, hy, rTop * cz), nrm);
                    V(center + new Vector3(rBottom * sx, -hy, rBottom * cz), nrm);
                }
                for (int s = 0; s < segments; s++)
                {
                    int t0 = start + s * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
                    Vector3 mid = new Vector3(Mathf.Sin((s + 0.5f) / segments * Mathf.PI * 2f), 0f, Mathf.Cos((s + 0.5f) / segments * Mathf.PI * 2f));
                    TriOut(t0, b0, t1, mid);
                    TriOut(t1, b0, b1, mid);
                }
                for (int cap = 0; cap < 2; cap++)
                {
                    float y = cap == 0 ? hy : -hy, r = cap == 0 ? rTop : rBottom;
                    if (r <= 0f) continue;
                    Vector3 nrm = cap == 0 ? Vector3.up : Vector3.down;
                    int c0 = V(center + new Vector3(0f, y, 0f), nrm);
                    int ring = _v.Count;
                    for (int s = 0; s <= segments; s++)
                    {
                        float phi = s / (float)segments * Mathf.PI * 2f;
                        V(center + new Vector3(r * Mathf.Sin(phi), y, r * Mathf.Cos(phi)), nrm);
                    }
                    for (int s = 0; s < segments; s++) TriOut(c0, ring + s, ring + s + 1, nrm);
                }
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
