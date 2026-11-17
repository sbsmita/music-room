// Procedural geometry for the piano, built natively in Unity coordinates (left-handed, front faces = Cross(b-a, c-a)
// pointing out) from the spec's parameters: a rounded box (subdivided, pushed onto a rounded inner box), a beveled
// extrusion (the case profiles, flat shaded: one normal per triangle of the non-indexed mesh), a cylinder (the power
// button) and a plane (the grille). Build-time only: these allocate freely.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Piano
{
    /// <summary>Growable vertex/index lists that turn into a Mesh. One builder per material (merged per material).</summary>
    public sealed class PianoMeshBuilder
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Indices = new List<int>();

        public int VertexCount => Positions.Count;

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            Positions.Add(p);
            Normals.Add(n);
            Uvs.Add(uv);
            return Positions.Count - 1;
        }

        /// <summary>Adds a triangle, flipping it if needed so its front face points along <paramref name="outward"/>.</summary>
        public void AddTriangleFacing(int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
            if (Vector3.Dot(n, outward) < 0f) { int t = b; b = c; c = t; }
            Indices.Add(a); Indices.Add(b); Indices.Add(c);
        }

        public Bounds ComputeBounds()
        {
            if (Positions.Count == 0) return new Bounds();
            Vector3 min = Positions[0], max = Positions[0];
            foreach (var p in Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (Positions.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(Positions);
            m.SetNormals(Normals);
            m.SetUVs(0, Uvs);
            m.SetTriangles(Indices, 0, true);
            return m;
        }

        // ------------------------------------------------------------------------------------------------------------
        // RoundedBox(width, height, depth, segments, radius): a subdivided unit box whose vertices are pushed onto
        // an inner box plus a rounding of `radius`, normals from the normalised offset (smooth everywhere). Each face is its
        // own (2s+1)^2 grid so it can carry a planar uv (0..1 across the face, the stand's wood) or a constant one (palettes).
        // ------------------------------------------------------------------------------------------------------------

        public struct BoxUv
        {
            public bool Constant;      // every vertex gets Value (palette lookups, key shading set later)
            public Vector2 Value;
            public static BoxUv Planar => new BoxUv();
            public static BoxUv Fixed(Vector2 v) => new BoxUv { Constant = true, Value = v };
        }

        /// <summary>Adds a rounded box centred at <paramref name="centre"/> (after <paramref name="rotation"/> about its own centre
        /// offset by <paramref name="pivotOffset"/>). Returns the index of its first vertex; it adds RoundedBoxVertexCount(segments).</summary>
        public int AddRoundedBox(Vector3 size, int segments, float radius, Vector3 centre, BoxUv uv, Quaternion rotation, Vector3 preRotateOffset)
        {
            int first = Positions.Count;
            int n = segments * 2 + 1;
            radius = Mathf.Min(Mathf.Min(size.x / 2f, size.y / 2f), Mathf.Min(size.z / 2f, radius));
            Vector3 box = size / 2f - Vector3.one * radius;
            float half = 0.5f / n;
            // Faces: axis a with sign s; in-plane axes u, v.
            for (int face = 0; face < 6; face++)
            {
                int a = face / 2;
                float s = (face % 2 == 0) ? 1f : -1f;
                int u = (a + 1) % 3, v = (a + 2) % 3;
                int faceFirst = Positions.Count;
                for (int j = 0; j <= n; j++)
                {
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 unit = Vector3.zero;
                        unit[a] = 0.5f * s;
                        unit[u] = -0.5f + (float)i / n;
                        unit[v] = -0.5f + (float)j / n;
                        Vector3 nrm = unit;
                        for (int k = 0; k < 3; k++) nrm[k] -= Mathf.Sign(unit[k]) * half;
                        nrm.Normalize();
                        Vector3 p = new Vector3(box.x * Mathf.Sign(unit.x), box.y * Mathf.Sign(unit.y), box.z * Mathf.Sign(unit.z)) + nrm * radius;
                        p = rotation * (p + preRotateOffset) + centre;
                        Vector2 t = uv.Constant ? uv.Value : new Vector2(unit[u] + 0.5f, unit[v] + 0.5f);
                        AddVertex(p, rotation * nrm, t);
                    }
                }
                Vector3 outward = Vector3.zero;
                outward[a] = s;
                outward = rotation * outward;
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int i00 = faceFirst + j * (n + 1) + i, i10 = i00 + 1, i01 = i00 + (n + 1), i11 = i01 + 1;
                        AddTriangleFacing(i00, i10, i11, outward);
                        AddTriangleFacing(i00, i11, i01, outward);
                    }
                }
            }
            return first;
        }

        public int AddRoundedBox(Vector3 size, int segments, float radius, Vector3 centre, BoxUv uv) =>
            AddRoundedBox(size, segments, radius, centre, uv, Quaternion.identity, Vector3.zero);

        public static int RoundedBoxVertexCount(int segments) { int n = segments * 2 + 1; return 6 * (n + 1) * (n + 1); }

        // ------------------------------------------------------------------------------------------------------------
        // Extrude(shape, { depth, bevelEnabled, bevelThickness = bevelSize = e, bevelSegments, curveSegments })
        // followed by rotateY(PI/2) and translate(x0 + e, 0, 0): a profile drawn in (back, y) extruded across x from x0 to
        // x1. The bevel grows the profile out by e along the getBevelVec miter vectors on a quarter-circle (cos/sin),
        // so the finished outline is the shape plus e, with rounded ends. Flat normals per triangle (the extrusion is
        // non-indexed, so computeVertexNormals gives face normals).
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>A 2D path: moveTo, lineTo and quadratic curves, sampled with a fixed number of divisions per curve (getPoints(divisions)).</summary>
        public sealed class Profile
        {
            readonly List<Vector2> _pts = new List<Vector2>();
            Vector2 _cur;
            readonly int _divisions;
            public Profile(int curveDivisions) { _divisions = curveDivisions; }
            public Profile MoveTo(float x, float y) { _cur = new Vector2(x, y); Add(_cur); return this; }
            public Profile LineTo(float x, float y) { _cur = new Vector2(x, y); Add(_cur); return this; }
            public Profile QuadTo(float cx, float cy, float x, float y)
            {
                Vector2 p0 = _cur, c = new Vector2(cx, cy), p1 = new Vector2(x, y);
                for (int i = 1; i <= _divisions; i++)
                {
                    float t = (float)i / _divisions, u = 1f - t;
                    Add(u * u * p0 + 2f * u * t * c + t * t * p1);
                }
                _cur = p1;
                return this;
            }
            void Add(Vector2 p)
            {
                if (_pts.Count > 0 && _pts[_pts.Count - 1] == p) return;   // consecutive equal points are skipped
                _pts.Add(p);
            }
            /// <summary>The closed contour without the closing duplicate (removeDupEndPts), clockwise (counter-clockwise
            /// shapes are reversed before extruding).</summary>
            public List<Vector2> Contour()
            {
                var c = new List<Vector2>(_pts);
                if (c.Count > 2 && c[c.Count - 1] == c[0]) c.RemoveAt(c.Count - 1);
                if (SignedArea(c) > 0f) c.Reverse();
                return c;
            }
        }

        public static float SignedArea(List<Vector2> c)
        {
            float a = 0f;
            for (int p = c.Count - 1, q = 0; q < c.Count; p = q++) a += c[p].x * c[q].y - c[q].x * c[p].y;
            return a * 0.5f;
        }

        /// <summary>getBevelVec: the offset of a contour point for a unit outward shift of both adjacent edges
        /// (walking clockwise, the left side is outside), clamped against spikes.</summary>
        public static Vector2 BevelVec(Vector2 inPt, Vector2 inPrev, Vector2 inNext)
        {
            const float eps = 1.1920929e-7f;   // Number.EPSILON scaled to float precision
            float vtx, vty, shrink;
            float vpx = inPt.x - inPrev.x, vpy = inPt.y - inPrev.y;
            float vnx = inNext.x - inPt.x, vny = inNext.y - inPt.y;
            float vpLenSq = vpx * vpx + vpy * vpy;
            float collinear0 = vpx * vny - vpy * vnx;
            if (Mathf.Abs(collinear0) > eps)
            {
                float vpLen = Mathf.Sqrt(vpLenSq);
                float vnLen = Mathf.Sqrt(vnx * vnx + vny * vny);
                float ppx = inPrev.x - vpy / vpLen, ppy = inPrev.y + vpx / vpLen;
                float pnx = inNext.x - vny / vnLen, pny = inNext.y + vnx / vnLen;
                float sf = ((pnx - ppx) * vny - (pny - ppy) * vnx) / (vpx * vny - vpy * vnx);
                vtx = ppx + vpx * sf - inPt.x;
                vty = ppy + vpy * sf - inPt.y;
                float lenSq = vtx * vtx + vty * vty;
                if (lenSq <= 2f) return new Vector2(vtx, vty);
                shrink = Mathf.Sqrt(lenSq / 2f);
            }
            else
            {
                bool same;
                if (vpx > eps) same = vnx > eps;
                else if (vpx < -eps) same = vnx < -eps;
                else same = Mathf.Sign(vpy) == Mathf.Sign(vny);
                if (same) { vtx = -vpy; vty = vpx; shrink = Mathf.Sqrt(vpLenSq); }
                else { vtx = vpx; vty = vpy; shrink = Mathf.Sqrt(vpLenSq / 2f); }
            }
            return new Vector2(vtx / shrink, vty / shrink);
        }

        /// <summary>Adds the extrusion of a clockwise (back, y) contour from x0 to x1 with bevel e (thickness = size).</summary>
        public void AddExtrudedProfile(List<Vector2> contour, float x0, float x1, float e, int bevelSegments, Vector2 uv)
        {
            int n = contour.Count;
            var moves = new Vector2[n];
            for (int i = 0; i < n; i++) moves[i] = BevelVec(contour[i], contour[(i - 1 + n) % n], contour[(i + 1) % n]);
            float depth = x1 - x0 - 2f * e;
            // Layers: the front bevel (b = 0..segs-1), the two faces of the extrusion (steps = 1), the back bevel reversed.
            var layerZ = new List<float>();
            var layerOff = new List<float>();
            for (int b = 0; b < bevelSegments; b++)
            {
                float t = (float)b / bevelSegments;
                layerZ.Add(-e * Mathf.Cos(t * Mathf.PI / 2f));
                layerOff.Add(e * Mathf.Sin(t * Mathf.PI / 2f));
            }
            layerZ.Add(0f); layerOff.Add(e);
            layerZ.Add(depth); layerOff.Add(e);
            for (int b = bevelSegments - 1; b >= 0; b--)
            {
                float t = (float)b / bevelSegments;
                layerZ.Add(depth + e * Mathf.Cos(t * Mathf.PI / 2f));
                layerOff.Add(e * Mathf.Sin(t * Mathf.PI / 2f));
            }
            int layers = layerZ.Count;
            // Shape (sx = back, sy = y, sz = extrusion) -> Unity piano-local (x = x0 + e + sz, y = sy, z = sx).
            Vector3 P(int layer, int i)
            {
                Vector2 q = contour[i] + moves[i] * layerOff[layer];
                return new Vector3(x0 + e + layerZ[layer], q.y, q.x);
            }
            // Side walls: (A, B, C), (A, C, D) face outward for a clockwise contour. With d = P[i+1] - P[i] in (back, y),
            // B - A = (0, d.y, d.x) and C - A = (dz, d.y, d.x), so Cross(B - A, C - A) = dz (0, d.x, -d.y): the left normal of
            // d, which is the outside when walking clockwise (the getBevelVec convention).
            for (int s = 0; s + 1 < layers; s++)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 a = P(s, i), b = P(s, j), c = P(s + 1, j), d = P(s + 1, i);
                    AddFlatTriangle(a, b, c, uv);
                    AddFlatTriangle(a, c, d, uv);
                }
            }
            // Lids: the original contour at both ends (offset 0), ear-clipped.
            var tris = Triangulate(contour);
            for (int t = 0; t < tris.Count; t += 3)
            {
                // tris are counter-clockwise in shape space; in Unity that faces -x (the x0 end): see Cross derivation.
                Vector3 a0 = P(0, tris[t]), b0 = P(0, tris[t + 1]), c0 = P(0, tris[t + 2]);
                AddFlatTriangle(a0, b0, c0, uv);
                Vector3 a1 = P(layers - 1, tris[t]), b1 = P(layers - 1, tris[t + 1]), c1 = P(layers - 1, tris[t + 2]);
                AddFlatTriangle(a1, c1, b1, uv);
            }
        }

        /// <summary>A triangle with its own three vertices and its face normal (computeVertexNormals on a non-indexed mesh).</summary>
        public void AddFlatTriangle(Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            float len = nrm.magnitude;
            if (len < 1e-12f) return;   // degenerate (a zero-length bevel step): nothing to draw
            nrm /= len;
            int i = AddVertex(a, nrm, uv);
            AddVertex(b, nrm, uv);
            AddVertex(c, nrm, uv);
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
        }

        /// <summary>Ear clipping of a simple polygon; returns counter-clockwise index triples into <paramref name="poly"/>.</summary>
        public static List<int> Triangulate(List<Vector2> poly)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            if (SignedArea(poly) < 0f) idx.Reverse();   // work counter-clockwise
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ip = idx[(k - 1 + idx.Count) % idx.Count], ic = idx[k], inx = idx[(k + 1) % idx.Count];
                    Vector2 a = poly[ip], b = poly[ic], c = poly[inx];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (Mathf.Abs(cross) < 1e-12f) { idx.RemoveAt(k); clipped = true; break; }   // collinear: drop it
                    if (cross < 0f) continue;                                                     // reflex
                    bool inside = false;
                    for (int m = 0; m < idx.Count && !inside; m++)
                    {
                        int im = idx[m];
                        if (im == ip || im == ic || im == inx) continue;
                        inside = PointInTriangle(poly[im], a, b, c);
                    }
                    if (inside) continue;
                    result.Add(ip); result.Add(ic); result.Add(inx);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // malformed polygon: stop rather than loop
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Cylinder(radiusTop, radiusBottom, height, radialSegments) along +y with both caps; smooth torso normals.
        // ------------------------------------------------------------------------------------------------------------
        public void AddCylinder(float rTop, float rBottom, float height, int radial, Vector3 centre, Vector2 uv)
        {
            float slope = (rBottom - rTop) / height;
            int torso = Positions.Count;
            for (int y = 0; y <= 1; y++)
            {
                float r = y == 0 ? rTop : rBottom, py = y == 0 ? height / 2f : -height / 2f;
                for (int i = 0; i <= radial; i++)
                {
                    float th = (float)i / radial * Mathf.PI * 2f;
                    float sn = Mathf.Sin(th), cs = Mathf.Cos(th);
                    AddVertex(centre + new Vector3(r * sn, py, r * cs), new Vector3(sn, slope, cs).normalized, uv);
                }
            }
            for (int i = 0; i < radial; i++)
            {
                int a = torso + i, b = torso + (radial + 1) + i, c = b + 1, d = a + 1;
                Vector3 mid = (Positions[a] + Positions[c]) * 0.5f - centre;
                mid.y = 0f;
                AddTriangleFacing(a, b, d, mid);
                AddTriangleFacing(b, c, d, mid);
            }
            for (int capTop = 0; capTop <= 1; capTop++)
            {
                bool top = capTop == 1;
                float r = top ? rTop : rBottom, py = top ? height / 2f : -height / 2f;
                Vector3 nrm = top ? Vector3.up : Vector3.down;
                int ctr = AddVertex(centre + new Vector3(0f, py, 0f), nrm, uv);
                int ring = Positions.Count;
                for (int i = 0; i <= radial; i++)
                {
                    float th = (float)i / radial * Mathf.PI * 2f;
                    AddVertex(centre + new Vector3(r * Mathf.Sin(th), py, r * Mathf.Cos(th)), nrm, uv);
                }
                for (int i = 0; i < radial; i++) AddTriangleFacing(ctr, ring + i, ring + i + 1, nrm);
            }
        }

        /// <summary>An up-facing rectangle (a plane rotated -PI/2 about x) with uv repeating every <paramref name="uvMetres"/>.</summary>
        public void AddPlaneUp(float width, float depth, Vector3 centre, float uvMetres)
        {
            float hx = width / 2f, hz = depth / 2f;
            int a = AddVertex(centre + new Vector3(-hx, 0f, -hz), Vector3.up, new Vector2(0f, 0f));
            int b = AddVertex(centre + new Vector3(hx, 0f, -hz), Vector3.up, new Vector2(width / uvMetres, 0f));
            int c = AddVertex(centre + new Vector3(hx, 0f, hz), Vector3.up, new Vector2(width / uvMetres, depth / uvMetres));
            int d = AddVertex(centre + new Vector3(-hx, 0f, hz), Vector3.up, new Vector2(0f, depth / uvMetres));
            AddTriangleFacing(a, b, c, Vector3.up);
            AddTriangleFacing(a, c, d, Vector3.up);
        }
    }
}
