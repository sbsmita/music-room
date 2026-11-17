// A small procedural mesh builder for the marimba (the Drums MeshKit idea, kept in the marimba's own assembly: the
// marimba must not reference Drums). Unity coordinates, metres. Every triangle's winding is chosen so its front face
// agrees with the vertex normals ("outward winding as in MeshKit", spec 4.2), so a generator can never leave a part
// inside out. UVs are atlas regions: every part maps "along" its length to texture u inside a region [u0, u1] and
// "around" / "across" to v. Build time only: allocates freely.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Marimba
{
    /// <summary>A rectangle of an atlas: u0..u1 along the part, v0..v1 across it.</summary>
    public struct UvRegion
    {
        public float U0, U1, V0, V1;
        public UvRegion(float u0, float u1, float v0, float v1) { U0 = u0; U1 = u1; V0 = v0; V1 = v1; }
        public Vector2 At(float along, float across) => new Vector2(U0 + (U1 - U0) * along, V0 + (V1 - V0) * across);
    }

    public sealed class MarimbaMeshKit
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Vector3> N = new List<Vector3>(4096);
        public readonly List<Vector2> UV = new List<Vector2>(4096);
        public readonly List<int> T = new List<int>(8192);

        public int VertexCount => V.Count;
        public int TriangleCount => T.Count / 3;

        public void Clear() { V.Clear(); N.Clear(); UV.Clear(); T.Clear(); }

        public int Vert(Vector3 p, Vector3 n, Vector2 uv)
        {
            V.Add(p);
            N.Add(n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up);
            UV.Add(uv);
            return V.Count - 1;
        }

        /// <summary>A triangle wound so its front face (Unity: clockwise seen from the front) points along the normals.</summary>
        public void Tri(int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            Vector3 n = N[a] + N[b] + N[c];
            if (Vector3.Dot(face, n) < 0f) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }

        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>A box with corners lo..hi (axis-aligned in the frame of rot about centre), each face its own normals; UVs
        /// map the box's longest axis to u.</summary>
        public void Box(Vector3 centre, Quaternion rot, Vector3 size, UvRegion uv)
        {
            Vector3 h = size * 0.5f;
            int along = size.x >= size.y && size.x >= size.z ? 0 : size.z >= size.y ? 2 : 1;
            for (int axis = 0; axis < 3; axis++)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 n = Vector3.zero; n[axis] = s;
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    var idx = new int[4];
                    for (int k = 0; k < 4; k++)
                    {
                        float c1 = (k == 1 || k == 2) ? 1f : -1f, c2 = k >= 2 ? 1f : -1f;
                        Vector3 p = Vector3.zero; p[axis] = s * h[axis]; p[a1] = c1 * h[a1]; p[a2] = c2 * h[a2];
                        float ua = (p[along] / Mathf.Max(1e-6f, size[along])) + 0.5f;
                        int other = along == a1 ? a2 : a1;
                        float va = axis == along ? (c1 * 0.5f + 0.5f) : (p[other] / Mathf.Max(1e-6f, size[other]) + 0.5f);
                        idx[k] = Vert(centre + rot * p, rot * n, uv.At(Mathf.Clamp01(ua), Mathf.Clamp01(va)));
                    }
                    Quad(idx[0], idx[1], idx[2], idx[3]);
                }
        }

        /// <summary>A box spanning from a to b (its length), with the given cross-section (width along side, height along up).</summary>
        public void Beam(Vector3 a, Vector3 b, Vector3 up, float width, float height, UvRegion uv)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-6f) return;
            Quaternion rot = Quaternion.LookRotation(d / len, up);   // +z along the beam, +y up
            Box((a + b) * 0.5f, rot, new Vector3(width, height, len), uv);
        }

        /// <summary>A closed cylinder of radius r from a to b (sides around it), u along its length.</summary>
        public void Rod(Vector3 a, Vector3 b, float r, int sides, UvRegion uv, bool capA = true, bool capB = true)
        {
            Sweep(new[] { a, b }, r, sides, uv, capA, capB, 0f);
        }

        /// <summary>A tube of radius r swept along a polyline (parallel-transport frames, smooth normals); u along the arc
        /// length (uvLength metres per region, 0 = the whole path), v around. Optional flat caps.</summary>
        public void Sweep(IReadOnlyList<Vector3> path, float r, int sides, UvRegion uv, bool capStart, bool capEnd, float uvLength, bool inward = false)
        {
            int n = path.Count;
            if (n < 2) return;
            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = i == 0 ? path[1] - path[0] : i == n - 1 ? path[n - 1] - path[n - 2] : (path[i + 1] - path[i]).normalized + (path[i] - path[i - 1]).normalized;
                tangents[i] = t.normalized;
            }
            Vector3 t0 = tangents[0];
            Vector3 normal = Mathf.Abs(Vector3.Dot(t0, Vector3.up)) < 0.9f ? Vector3.Cross(t0, Vector3.up).normalized : Vector3.Cross(t0, Vector3.right).normalized;
            float total = 0f;
            for (int i = 1; i < n; i++) total += (path[i] - path[i - 1]).magnitude;
            float s = 0f;
            int start = V.Count;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    s += (path[i] - path[i - 1]).magnitude;
                    // parallel transport: rotate the normal by the rotation that takes the previous tangent to this one
                    normal = Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * normal;
                }
                Vector3 binormal = Vector3.Cross(tangents[i], normal).normalized;
                float along = uvLength > 0f ? (s / uvLength) % 1f : s / Mathf.Max(1e-6f, total);
                if (uvLength > 0f && i > 0 && along < 1e-4f) along = 1f;
                for (int k = 0; k <= sides; k++)
                {
                    float a = k / (float)sides * Mathf.PI * 2f;
                    Vector3 dir = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    Vert(path[i] + dir * r, inward ? -dir : dir, uv.At(along, k / (float)sides));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = start + i * row + k, b = a + 1, c = a + row + 1, d = a + row;
                    Quad(a, b, c, d);
                }
            if (capStart) Cap(path[0], -tangents[0], start, sides, r, uv);
            if (capEnd) Cap(path[n - 1], tangents[n - 1], start + (n - 1) * row, sides, r, uv);
        }

        void Cap(Vector3 centre, Vector3 n, int ring, int sides, float r, UvRegion uv)
        {
            int c = Vert(centre, n, uv.At(0.02f, 0.5f));
            int first = V.Count;
            for (int k = 0; k < sides; k++) Vert(V[ring + k], n, uv.At(0.02f, k / (float)sides));
            for (int k = 0; k < sides; k++) Tri(c, first + k, first + (k + 1) % sides);
        }

        /// <summary>A flat annulus (the brass rim of a resonator mouth) in the plane normal to n at centre.</summary>
        public void Annulus(Vector3 centre, Vector3 n, float rIn, float rOut, int segs, UvRegion uv)
        {
            Vector3 a = Mathf.Abs(n.y) < 0.9f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.Cross(n, Vector3.right).normalized;
            Vector3 b = Vector3.Cross(n, a);
            int start = V.Count;
            for (int k = 0; k <= segs; k++)
            {
                float t = k / (float)segs * Mathf.PI * 2f;
                Vector3 d = a * Mathf.Cos(t) + b * Mathf.Sin(t);
                Vert(centre + d * rIn, n, uv.At(k / (float)segs, 0f));
                Vert(centre + d * rOut, n, uv.At(k / (float)segs, 1f));
            }
            for (int k = 0; k < segs; k++) Quad(start + 2 * k, start + 2 * k + 1, start + 2 * k + 3, start + 2 * k + 2);
        }

        /// <summary>A surface of revolution about +y at base (a turned leg): profile (radius, height), u along the height.</summary>
        public void Lathe(Vector3 base_, Vector2[] profile, int segs, UvRegion uv)
        {
            int np = profile.Length;
            var pn = new Vector2[np];
            for (int j = 0; j < np; j++)
            {
                Vector2 d = profile[Mathf.Min(np - 1, j + 1)] - profile[Mathf.Max(0, j - 1)];
                pn[j] = new Vector2(d.y, -d.x).normalized;   // outward for a profile traced bottom -> top
                if (pn[j].x < 0f) pn[j] = -pn[j];
            }
            float h0 = profile[0].y, h1 = profile[np - 1].y;
            int start = V.Count;
            for (int i = 0; i <= segs; i++)
            {
                float phi = i / (float)segs * Mathf.PI * 2f, s = Mathf.Sin(phi), c = Mathf.Cos(phi);
                for (int j = 0; j < np; j++)
                {
                    Vector2 p = profile[j];
                    Vert(base_ + new Vector3(p.x * s, p.y, p.x * c), new Vector3(pn[j].x * s, pn[j].y, pn[j].x * c), uv.At((p.y - h0) / Mathf.Max(1e-6f, h1 - h0), i / (float)segs));
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < np - 1; j++)
                {
                    int a = start + j + i * np, b = a + np, c = b + 1, d = a + 1;
                    Quad(a, b, c, d);
                }
        }

        /// <summary>An ellipsoid centred at c with radii (rx, ry, rz) in the frame rot; u around the equator, v pole to pole.</summary>
        public void Ellipsoid(Vector3 c, Quaternion rot, Vector3 radii, int wSegs, int hSegs, UvRegion uv)
        {
            int start = V.Count;
            for (int y = 0; y <= hSegs; y++)
            {
                float v = y / (float)hSegs, theta = v * Mathf.PI;
                for (int x = 0; x <= wSegs; x++)
                {
                    float u = x / (float)wSegs, phi = u * Mathf.PI * 2f;
                    Vector3 unit = new Vector3(-Mathf.Cos(phi) * Mathf.Sin(theta), Mathf.Cos(theta), Mathf.Sin(phi) * Mathf.Sin(theta));
                    Vector3 p = Vector3.Scale(unit, radii);
                    Vector3 n = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
                    Vert(c + rot * p, rot * n, uv.At(u, v));
                }
            }
            int row = wSegs + 1;
            for (int y = 0; y < hSegs; y++)
                for (int x = 0; x < wSegs; x++)
                {
                    int a = start + y * row + x, b = a + 1, cc = a + row + 1, d = a + row;
                    if (y != 0) Tri(a, d, b);
                    if (y != hSegs - 1) Tri(b, d, cc);
                }
        }

        /// <summary>A convex polygon (outline in the plane u-v at origin o, points counter-clockwise seen along -n) extruded
        /// along n by depth: both faces and the side strip (an end board). UVs: the outline's first axis to u.</summary>
        public void ExtrudeConvex(Vector3 o, Vector3 axisU, Vector3 axisV, Vector3 n, IReadOnlyList<Vector2> outline, float depth, UvRegion uv, Vector2 uvSize)
        {
            int m = outline.Count;
            Vector2 lo = outline[0], hi = outline[0];
            foreach (var q in outline) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            for (int face = 0; face < 2; face++)
            {
                Vector3 off = n * (face == 0 ? 0f : depth);
                Vector3 fn = face == 0 ? -n : n;
                int c0 = V.Count;
                foreach (var q in outline)
                {
                    Vector3 p = o + off + axisU * q.x + axisV * q.y;
                    Vert(p, fn, uv.At((q.x - lo.x) / uvSize.x, (q.y - lo.y) / uvSize.y));
                }
                for (int k = 1; k < m - 1; k++) Tri(c0, c0 + k, c0 + k + 1);
            }
            // the side strip: smooth normals along the outline
            float per = 0f;
            for (int k = 0; k < m; k++) per += (outline[(k + 1) % m] - outline[k]).magnitude;
            int s0 = V.Count;
            float acc = 0f;
            for (int k = 0; k <= m; k++)
            {
                Vector2 q = outline[k % m], prev = outline[(k - 1 + m) % m], next = outline[(k + 1) % m];
                Vector2 t = (next - prev).normalized;
                Vector2 out2 = new Vector2(t.y, -t.x);
                Vector3 wn = axisU * out2.x + axisV * out2.y;
                Vector3 p = o + axisU * q.x + axisV * q.y;
                if (k > 0) acc += (q - outline[k - 1]).magnitude;
                Vert(p, wn, uv.At(acc / per, 0f));
                Vert(p + n * depth, wn, uv.At(acc / per, depth / uvSize.y));
            }
            for (int k = 0; k < m; k++) Quad(s0 + 2 * k, s0 + 2 * k + 1, s0 + 2 * k + 3, s0 + 2 * k + 2);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (V.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(V);
            mesh.SetNormals(N);
            mesh.SetUVs(0, UV);
            mesh.SetTriangles(T, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
