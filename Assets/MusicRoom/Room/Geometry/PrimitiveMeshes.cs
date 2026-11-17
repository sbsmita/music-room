// Primitive mesh generators (box, plane, cylinder, torus, circle, sphere, rounded box, tube) in the right-handed source
// frame (-z forward), with the golden's vertex order, normals, UVs and index order. EditMode tests compare samples with it.
using System;
using UnityEngine;

namespace MusicRoom.Room.Geometry
{
    public static class PrimitiveMeshes
    {
        // ------------------------------------------------------------------ Box

        public static Geo Box(float width, float height, float depth, int ws = 1, int hs = 1, int ds = 1)
        {
            var g = new Geo();
            BuildPlane(g, 2, 1, 0, -1, -1, depth, height, width, ds, hs);   // px
            BuildPlane(g, 2, 1, 0, 1, -1, depth, height, -width, ds, hs);   // nx
            BuildPlane(g, 0, 2, 1, 1, 1, width, depth, height, ws, ds);     // py
            BuildPlane(g, 0, 2, 1, 1, -1, width, depth, -height, ws, ds);   // ny
            BuildPlane(g, 0, 1, 2, 1, -1, width, height, depth, ws, hs);    // pz
            BuildPlane(g, 0, 1, 2, -1, -1, width, height, -depth, ws, hs);  // nz
            return g;
        }

        static void BuildPlane(Geo g, int u, int v, int w, float udir, float vdir, float width, float height, float depth, int gridX, int gridY)
        {
            float segW = width / gridX, segH = height / gridY;
            float wh = width / 2f, hh = height / 2f, dh = depth / 2f;
            int gx1 = gridX + 1, gy1 = gridY + 1;
            int start = g.P.Count;
            for (int iy = 0; iy < gy1; iy++)
            {
                float y = iy * segH - hh;
                for (int ix = 0; ix < gx1; ix++)
                {
                    float x = ix * segW - wh;
                    var vec = Vector3.zero;
                    vec[u] = x * udir; vec[v] = y * vdir; vec[w] = dh;
                    g.P.Add(vec);
                    var n = Vector3.zero;
                    n[w] = depth > 0 ? 1f : -1f;
                    g.N.Add(n);
                    g.UV.Add(new Vector2((float)ix / gridX, 1f - (float)iy / gridY));
                }
            }
            for (int iy = 0; iy < gridY; iy++)
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = start + ix + gx1 * iy;
                    int b = start + ix + gx1 * (iy + 1);
                    int c = start + (ix + 1) + gx1 * (iy + 1);
                    int d = start + (ix + 1) + gx1 * iy;
                    g.I.Add(a); g.I.Add(b); g.I.Add(d);
                    g.I.Add(b); g.I.Add(c); g.I.Add(d);
                }
        }

        // ------------------------------------------------------------------ Plane

        public static Geo Plane(float width, float height, int gridX = 1, int gridY = 1)
        {
            var g = new Geo();
            float wh = width / 2f, hh = height / 2f;
            int gx1 = gridX + 1, gy1 = gridY + 1;
            float sw = width / gridX, sh = height / gridY;
            for (int iy = 0; iy < gy1; iy++)
            {
                float y = iy * sh - hh;
                for (int ix = 0; ix < gx1; ix++)
                {
                    float x = ix * sw - wh;
                    g.P.Add(new Vector3(x, -y, 0f));
                    g.N.Add(new Vector3(0f, 0f, 1f));
                    g.UV.Add(new Vector2((float)ix / gridX, 1f - (float)iy / gridY));
                }
            }
            for (int iy = 0; iy < gridY; iy++)
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = ix + gx1 * iy, b = ix + gx1 * (iy + 1), c = (ix + 1) + gx1 * (iy + 1), d = (ix + 1) + gx1 * iy;
                    g.I.Add(a); g.I.Add(b); g.I.Add(d);
                    g.I.Add(b); g.I.Add(c); g.I.Add(d);
                }
            return g;
        }

        // ------------------------------------------------------------------ Cylinder

        public static Geo Cylinder(float radiusTop, float radiusBottom, float height, int radialSegments = 32, int heightSegments = 1,
            bool openEnded = false, float thetaStart = 0f, float thetaLength = Mathf.PI * 2f)
        {
            var g = new Geo();
            float halfHeight = height / 2f;
            var rows = new int[heightSegments + 1, radialSegments + 1];
            int index = 0;
            float slope = (radiusBottom - radiusTop) / height;
            for (int y = 0; y <= heightSegments; y++)
            {
                float v = (float)y / heightSegments;
                float radius = v * (radiusBottom - radiusTop) + radiusTop;
                for (int x = 0; x <= radialSegments; x++)
                {
                    float u = (float)x / radialSegments;
                    float theta = u * thetaLength + thetaStart;
                    float s = Mathf.Sin(theta), c = Mathf.Cos(theta);
                    g.P.Add(new Vector3(radius * s, -v * height + halfHeight, radius * c));
                    g.N.Add(new Vector3(s, slope, c).normalized);
                    g.UV.Add(new Vector2(u, 1f - v));
                    rows[y, x] = index++;
                }
            }
            for (int x = 0; x < radialSegments; x++)
                for (int y = 0; y < heightSegments; y++)
                {
                    int a = rows[y, x], b = rows[y + 1, x], c = rows[y + 1, x + 1], d = rows[y, x + 1];
                    if (radiusTop > 0f || y != 0) { g.I.Add(a); g.I.Add(b); g.I.Add(d); }
                    if (radiusBottom > 0f || y != heightSegments - 1) { g.I.Add(b); g.I.Add(c); g.I.Add(d); }
                }
            if (!openEnded)
            {
                if (radiusTop > 0f) Cap(g, true, radiusTop, halfHeight, radialSegments, thetaStart, thetaLength, ref index);
                if (radiusBottom > 0f) Cap(g, false, radiusBottom, halfHeight, radialSegments, thetaStart, thetaLength, ref index);
            }
            return g;
        }

        static void Cap(Geo g, bool top, float radius, float halfHeight, int radialSegments, float thetaStart, float thetaLength, ref int index)
        {
            int centerStart = index;
            float sign = top ? 1f : -1f;
            for (int x = 1; x <= radialSegments; x++)
            {
                g.P.Add(new Vector3(0f, halfHeight * sign, 0f));
                g.N.Add(new Vector3(0f, sign, 0f));
                g.UV.Add(new Vector2(0.5f, 0.5f));
                index++;
            }
            int centerEnd = index;
            for (int x = 0; x <= radialSegments; x++)
            {
                float u = (float)x / radialSegments;
                float theta = u * thetaLength + thetaStart;
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                g.P.Add(new Vector3(radius * s, halfHeight * sign, radius * c));
                g.N.Add(new Vector3(0f, sign, 0f));
                g.UV.Add(new Vector2(c * 0.5f + 0.5f, s * 0.5f * sign + 0.5f));
                index++;
            }
            for (int x = 0; x < radialSegments; x++)
            {
                int c = centerStart + x, i = centerEnd + x;
                if (top) { g.I.Add(i); g.I.Add(i + 1); g.I.Add(c); }
                else { g.I.Add(i + 1); g.I.Add(i); g.I.Add(c); }
            }
        }

        // ------------------------------------------------------------------ Torus

        public static Geo Torus(float radius, float tube, int radialSegments = 12, int tubularSegments = 48, float arc = Mathf.PI * 2f)
        {
            var g = new Geo();
            for (int j = 0; j <= radialSegments; j++)
                for (int i = 0; i <= tubularSegments; i++)
                {
                    float u = (float)i / tubularSegments * arc;
                    float v = (float)j / radialSegments * Mathf.PI * 2f;
                    var p = new Vector3((radius + tube * Mathf.Cos(v)) * Mathf.Cos(u), (radius + tube * Mathf.Cos(v)) * Mathf.Sin(u), tube * Mathf.Sin(v));
                    g.P.Add(p);
                    var center = new Vector3(radius * Mathf.Cos(u), radius * Mathf.Sin(u), 0f);
                    g.N.Add((p - center).normalized);
                    g.UV.Add(new Vector2((float)i / tubularSegments, (float)j / radialSegments));
                }
            for (int j = 1; j <= radialSegments; j++)
                for (int i = 1; i <= tubularSegments; i++)
                {
                    int a = (tubularSegments + 1) * j + i - 1;
                    int b = (tubularSegments + 1) * (j - 1) + i - 1;
                    int c = (tubularSegments + 1) * (j - 1) + i;
                    int d = (tubularSegments + 1) * j + i;
                    g.I.Add(a); g.I.Add(b); g.I.Add(d);
                    g.I.Add(b); g.I.Add(c); g.I.Add(d);
                }
            return g;
        }

        // ------------------------------------------------------------------ Circle

        public static Geo Circle(float radius, int segments = 32, float thetaStart = 0f, float thetaLength = Mathf.PI * 2f)
        {
            var g = new Geo();
            g.P.Add(Vector3.zero); g.N.Add(Vector3.forward); g.UV.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float a = thetaStart + (float)s / segments * thetaLength;
                var p = new Vector3(radius * Mathf.Cos(a), radius * Mathf.Sin(a), 0f);
                g.P.Add(p); g.N.Add(Vector3.forward);
                g.UV.Add(new Vector2((p.x / radius + 1f) / 2f, (p.y / radius + 1f) / 2f));
            }
            for (int i = 1; i <= segments; i++) { g.I.Add(i); g.I.Add(i + 1); g.I.Add(0); }
            return g;
        }

        // ------------------------------------------------------------------ Sphere

        public static Geo Sphere(float radius, int widthSegments = 32, int heightSegments = 16,
            float phiStart = 0f, float phiLength = Mathf.PI * 2f, float thetaStart = 0f, float thetaLength = Mathf.PI)
        {
            var g = new Geo();
            float thetaEnd = Mathf.Min(thetaStart + thetaLength, Mathf.PI);
            var grid = new int[heightSegments + 1, widthSegments + 1];
            int index = 0;
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                float v = (float)iy / heightSegments;
                float uOffset = 0f;
                if (iy == 0 && thetaStart == 0f) uOffset = 0.5f / widthSegments;
                else if (iy == heightSegments && thetaEnd == Mathf.PI) uOffset = -0.5f / widthSegments;
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    float u = (float)ix / widthSegments;
                    var p = new Vector3(
                        -radius * Mathf.Cos(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength),
                        radius * Mathf.Cos(thetaStart + v * thetaLength),
                        radius * Mathf.Sin(phiStart + u * phiLength) * Mathf.Sin(thetaStart + v * thetaLength));
                    g.P.Add(p);
                    g.N.Add(p.normalized);
                    g.UV.Add(new Vector2(u + uOffset, 1f - v));
                    grid[iy, ix] = index++;
                }
            }
            for (int iy = 0; iy < heightSegments; iy++)
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    int a = grid[iy, ix + 1], b = grid[iy, ix], c = grid[iy + 1, ix], d = grid[iy + 1, ix + 1];
                    if (iy != 0 || thetaStart > 0f) { g.I.Add(a); g.I.Add(b); g.I.Add(d); }
                    if (iy != heightSegments - 1 || thetaEnd < Mathf.PI) { g.I.Add(b); g.I.Add(c); g.I.Add(d); }
                }
            return g;
        }

        // ------------------------------------------------------------------ Rounded box

        /// <summary>Rounded box (w, h, d, segments, radius clamped to min(r, w/2, h/2, d/2); the room's rbox uses 2 segments).
        /// Non-indexed, like the golden.</summary>
        public static Geo RoundedBox(float width, float height, float depth, int segments, float radius)
        {
            int total = segments * 2 + 1;
            radius = Mathf.Min(width / 2f, height / 2f, depth / 2f, radius);
            Geo box = Box(1f, 1f, 1f, total, total, total);
            // toNonIndexed
            var g = new Geo();
            for (int t = 0; t < box.I.Count; t++)
            {
                int k = box.I[t];
                g.P.Add(box.P[k]); g.N.Add(box.N[k]); g.UV.Add(box.UV[k]);
                g.I.Add(t);
            }
            var half = new Vector3(width / 2f - radius, height / 2f - radius, depth / 2f - radius);
            int faceVerts = g.P.Count / 6;      // golden: positions.length / 6 floats per face, i counts floats (3 per vertex)
            float halfSeg = 0.5f / total;
            for (int vi = 0; vi < g.P.Count; vi++)
            {
                Vector3 pos = g.P[vi];
                Vector3 n = pos;
                n.x -= Math.Sign(n.x) * halfSeg;
                n.y -= Math.Sign(n.y) * halfSeg;
                n.z -= Math.Sign(n.z) * halfSeg;
                n.Normalize();
                g.P[vi] = new Vector3(half.x * Math.Sign(pos.x) + n.x * radius, half.y * Math.Sign(pos.y) + n.y * radius, half.z * Math.Sign(pos.z) + n.z * radius);
                g.N[vi] = n;
                int side = vi * 3 / (faceVerts * 3);
                float u, v;
                switch (side)
                {
                    case 0: u = RbUv(Vector3.right, n, 2, 1, radius, depth); v = 1f - RbUv(Vector3.right, n, 1, 2, radius, height); break;
                    case 1: u = 1f - RbUv(Vector3.left, n, 2, 1, radius, depth); v = 1f - RbUv(Vector3.left, n, 1, 2, radius, height); break;
                    case 2: u = 1f - RbUv(Vector3.up, n, 0, 2, radius, width); v = RbUv(Vector3.up, n, 2, 0, radius, depth); break;
                    case 3: u = 1f - RbUv(Vector3.down, n, 0, 2, radius, width); v = 1f - RbUv(Vector3.down, n, 2, 0, radius, depth); break;
                    case 4: u = 1f - RbUv(Vector3.forward, n, 0, 1, radius, width); v = 1f - RbUv(Vector3.forward, n, 1, 0, radius, height); break;
                    default: u = RbUv(Vector3.back, n, 0, 1, radius, width); v = 1f - RbUv(Vector3.back, n, 1, 0, radius, height); break;
                }
                g.UV[vi] = new Vector2(u, v);
            }
            return g;
        }

        static float RbUv(Vector3 faceDir, Vector3 normal, int uvAxis, int projectionAxis, float radius, float sideLength)
        {
            float totArc = 2f * Mathf.PI * radius / 4f;
            float centerLength = Mathf.Max(sideLength - 2f * radius, 0f);
            float halfArc = Mathf.PI / 4f;
            Vector3 t = normal;
            t[projectionAxis] = 0f;
            t.Normalize();
            float arcUvRatio = 0.5f * totArc / (totArc + centerLength);
            float arcAngleRatio = 1f - AngleTo(t, faceDir) / halfArc;
            if (Math.Sign(t[uvAxis]) == 1) return arcAngleRatio * arcUvRatio;
            float lenUv = centerLength / (totArc + centerLength);
            return lenUv + arcUvRatio + arcUvRatio * (1f - arcAngleRatio);
        }

        /// <summary>Angle between vectors: acos(clamp(dot / sqrt(lenSq a * lenSq b))), pi/2 for a zero vector.</summary>
        static float AngleTo(Vector3 a, Vector3 b)
        {
            float den = Mathf.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
            if (den == 0f) return Mathf.PI / 2f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b) / den, -1f, 1f));
        }

        // ------------------------------------------------------------------ Tube

        public static Geo Tube(CatmullRom3 path, int tubularSegments = 64, float radius = 1f, int radialSegments = 8)
        {
            var g = new Geo();
            path.FrenetFrames(tubularSegments, out Vector3[] tangents, out Vector3[] normals, out Vector3[] binormals);
            for (int i = 0; i <= tubularSegments; i++)
            {
                Vector3 p = path.GetPointAt((float)i / tubularSegments);
                Vector3 nn = normals[i], bb = binormals[i];
                for (int j = 0; j <= radialSegments; j++)
                {
                    float v = (float)j / radialSegments * Mathf.PI * 2f;
                    float sin = Mathf.Sin(v), cos = -Mathf.Cos(v);
                    Vector3 n = new Vector3(cos * nn.x + sin * bb.x, cos * nn.y + sin * bb.y, cos * nn.z + sin * bb.z).normalized;
                    g.N.Add(n);
                    g.P.Add(p + radius * n);
                    g.UV.Add(new Vector2((float)i / tubularSegments, (float)j / radialSegments));
                }
            }
            for (int j = 1; j <= tubularSegments; j++)
                for (int i = 1; i <= radialSegments; i++)
                {
                    int a = (radialSegments + 1) * (j - 1) + (i - 1);
                    int b = (radialSegments + 1) * j + (i - 1);
                    int c = (radialSegments + 1) * j + i;
                    int d = (radialSegments + 1) * (j - 1) + i;
                    g.I.Add(a); g.I.Add(b); g.I.Add(d);
                    g.I.Add(b); g.I.Add(c); g.I.Add(d);
                }
            return g;
        }
    }
}
