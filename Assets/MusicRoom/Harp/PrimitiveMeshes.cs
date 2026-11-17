// The mesh generators the harp uses (cylinder, sphere, box, tube, non-indexed copy, vertex normals) with a fixed
// vertex order, normals and UVs, so the wood grain runs along each part as designed. Everything is built in a
// right-handed frame (-z forward, CCW front faces) and converted ONCE by ToUnity(): z -> -z for positions and
// normals and reversed winding (ARCHITECTURE.md section 5: the mirror flips triangle winding). Quaternion/vector
// math needs no change: Unity's operators use the same component formulas in either frame, so right-handed
// transforms are applied before the conversion.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Harp
{
    public sealed class MeshData
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Indices = new List<int>();
        public bool UnitySpace { get; private set; }

        public int VertexCount => Positions.Count;

        /// <summary>A position / rotation / scale transform applied to the mesh: v' = position + rotation * (scale . v).</summary>
        public MeshData Transform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 v = Positions[i];
                v = new Vector3(v.x * scale.x, v.y * scale.y, v.z * scale.z);
                Positions[i] = position + rotation * v;
                // Normals transform by the inverse transpose: divide by the scale, rotate, renormalise.
                Vector3 n = Normals[i];
                n = new Vector3(n.x / scale.x, n.y / scale.y, n.z / scale.z);
                Normals[i] = (rotation * n).normalized;
            }
            return this;
        }

        public MeshData Transform(Vector3 position, Quaternion rotation) => Transform(position, rotation, Vector3.one);

        /// <summary>Moves every vertex by d.</summary>
        public MeshData Translate(Vector3 d)
        {
            for (int i = 0; i < Positions.Count; i++) Positions[i] += d;
            return this;
        }

        /// <summary>Rotates every vertex and normal about +x by angle (radians, right-handed).</summary>
        public MeshData RotateX(double angle)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 p = Positions[i];
                Positions[i] = new Vector3(p.x, p.y * c - p.z * s, p.y * s + p.z * c);
                Vector3 n = Normals[i];
                Normals[i] = new Vector3(n.x, n.y * c - n.z * s, n.y * s + n.z * c);
            }
            return this;
        }

        /// <summary>A deep copy (same coordinate space), so a part's data can be moved into a combined draw mesh while the
        /// part keeps its own for its invisible grab handle.</summary>
        public MeshData Clone()
        {
            var r = new MeshData();
            r.Positions.AddRange(Positions);
            r.Normals.AddRange(Normals);
            r.Uvs.AddRange(Uvs);
            r.Indices.AddRange(Indices);
            r.UnitySpace = UnitySpace;
            return r;
        }

        /// <summary>mergeGeometries for indexed geometries with the same attributes.</summary>
        public MeshData Append(MeshData other)
        {
            int offset = Positions.Count;
            Positions.AddRange(other.Positions);
            Normals.AddRange(other.Normals);
            Uvs.AddRange(other.Uvs);
            for (int i = 0; i < other.Indices.Count; i++) Indices.Add(other.Indices[i] + offset);
            return this;
        }

        /// <summary>Non-indexed copy: one vertex per triangle corner, attributes copied in index order.</summary>
        public MeshData ToNonIndexed()
        {
            var r = new MeshData();
            for (int i = 0; i < Indices.Count; i++)
            {
                int k = Indices[i];
                r.Positions.Add(Positions[k]);
                r.Normals.Add(Normals[k]);
                r.Uvs.Add(Uvs[k]);
                r.Indices.Add(i);
            }
            return r;
        }

        /// <summary>Vertex normals: per triangle (cb x ab) summed into its corners, then normalised.
        /// On a non-indexed geometry this is flat shading.</summary>
        public MeshData ComputeVertexNormals()
        {
            var acc = new Vector3[Positions.Count];
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                int a = Indices[i], b = Indices[i + 1], c = Indices[i + 2];
                Vector3 pA = Positions[a], pB = Positions[b], pC = Positions[c];
                Vector3 n = Vector3.Cross(pC - pB, pA - pB);
                acc[a] += n; acc[b] += n; acc[c] += n;
            }
            for (int i = 0; i < acc.Length; i++) Normals[i] = acc[i].sqrMagnitude > 0f ? acc[i].normalized : Vector3.up;
            return this;
        }

        /// <summary>Right-handed frame to Unity: (x, y, z) -> (x, y, -z) for positions and normals, winding reversed.</summary>
        public MeshData ToUnity()
        {
            if (UnitySpace) return this;
            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 p = Positions[i]; p.z = -p.z; Positions[i] = p;
                Vector3 n = Normals[i]; n.z = -n.z; Normals[i] = n;
            }
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                int t = Indices[i + 1]; Indices[i + 1] = Indices[i + 2]; Indices[i + 2] = t;
            }
            UnitySpace = true;
            return this;
        }

        public Bounds Bounds()
        {
            if (Positions.Count == 0) return new Bounds();
            Vector3 min = Positions[0], max = Positions[0];
            for (int i = 1; i < Positions.Count; i++) { min = Vector3.Min(min, Positions[i]); max = Vector3.Max(max, Positions[i]); }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        /// <summary>A Unity mesh from Unity-space data (call ToUnity first).</summary>
        public Mesh ToMesh(string name)
        {
            if (!UnitySpace) throw new InvalidOperationException("MeshData.ToMesh needs Unity-space data: call ToUnity() first");
            var m = new Mesh { name = name };
            if (Positions.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(Positions);
            m.SetNormals(Normals);
            m.SetUVs(0, Uvs);
            m.SetTriangles(Indices, 0, true);
            return m;
        }
    }

    /// <summary>The generators, with their parameter order and defaults.</summary>
    public static class PrimitiveMeshes
    {
        /// <summary>Cylinder(radiusTop, radiusBottom, height, radialSegments, heightSegments, openEnded), along +y,
        /// centred on the origin; vertex rows run top (v = 0) to bottom.</summary>
        public static MeshData Cylinder(float radiusTop, float radiusBottom, float height, int radialSegments = 32, int heightSegments = 1, bool openEnded = false)
        {
            var g = new MeshData();
            const double thetaStart = 0, thetaLength = Math.PI * 2;
            float halfHeight = height / 2f;
            int index = 0;
            var indexArray = new List<int[]>();
            // generateTorso
            float slope = (radiusBottom - radiusTop) / height;
            for (int y = 0; y <= heightSegments; y++)
            {
                var row = new int[radialSegments + 1];
                float v = (float)y / heightSegments;
                float radius = v * (radiusBottom - radiusTop) + radiusTop;
                for (int x = 0; x <= radialSegments; x++)
                {
                    float u = (float)x / radialSegments;
                    double theta = u * thetaLength + thetaStart;
                    float sinTheta = (float)Math.Sin(theta), cosTheta = (float)Math.Cos(theta);
                    g.Positions.Add(new Vector3(radius * sinTheta, -v * height + halfHeight, radius * cosTheta));
                    g.Normals.Add(new Vector3(sinTheta, slope, cosTheta).normalized);
                    g.Uvs.Add(new Vector2(u, 1f - v));
                    row[x] = index++;
                }
                indexArray.Add(row);
            }
            for (int x = 0; x < radialSegments; x++)
            {
                for (int y = 0; y < heightSegments; y++)
                {
                    int a = indexArray[y][x], b = indexArray[y + 1][x], c = indexArray[y + 1][x + 1], d = indexArray[y][x + 1];
                    if (radiusTop > 0 || y != 0) { g.Indices.Add(a); g.Indices.Add(b); g.Indices.Add(d); }
                    if (radiusBottom > 0 || y != heightSegments - 1) { g.Indices.Add(b); g.Indices.Add(c); g.Indices.Add(d); }
                }
            }
            if (!openEnded)
            {
                if (radiusTop > 0) Cap(g, true, radiusTop, halfHeight, radialSegments, thetaStart, thetaLength, ref index);
                if (radiusBottom > 0) Cap(g, false, radiusBottom, halfHeight, radialSegments, thetaStart, thetaLength, ref index);
            }
            return g;
        }

        static void Cap(MeshData g, bool top, float radius, float halfHeight, int radialSegments, double thetaStart, double thetaLength, ref int index)
        {
            int centerIndexStart = index;
            float sign = top ? 1f : -1f;
            for (int x = 1; x <= radialSegments; x++)
            {
                g.Positions.Add(new Vector3(0f, halfHeight * sign, 0f));
                g.Normals.Add(new Vector3(0f, sign, 0f));
                g.Uvs.Add(new Vector2(0.5f, 0.5f));
                index++;
            }
            int centerIndexEnd = index;
            for (int x = 0; x <= radialSegments; x++)
            {
                float u = (float)x / radialSegments;
                double theta = u * thetaLength + thetaStart;
                float cosTheta = (float)Math.Cos(theta), sinTheta = (float)Math.Sin(theta);
                g.Positions.Add(new Vector3(radius * sinTheta, halfHeight * sign, radius * cosTheta));
                g.Normals.Add(new Vector3(0f, sign, 0f));
                g.Uvs.Add(new Vector2(cosTheta * 0.5f + 0.5f, sinTheta * 0.5f * sign + 0.5f));
                index++;
            }
            for (int x = 0; x < radialSegments; x++)
            {
                int c = centerIndexStart + x, i = centerIndexEnd + x;
                if (top) { g.Indices.Add(i); g.Indices.Add(i + 1); g.Indices.Add(c); }
                else { g.Indices.Add(i + 1); g.Indices.Add(i); g.Indices.Add(c); }
            }
        }

        /// <summary>Sphere(radius, widthSegments, heightSegments) (full sphere).</summary>
        public static MeshData Sphere(float radius, int widthSegments, int heightSegments)
        {
            var g = new MeshData();
            const double phiStart = 0, phiLength = Math.PI * 2, thetaStart = 0, thetaLength = Math.PI;
            double thetaEnd = Math.Min(thetaStart + thetaLength, Math.PI);
            int index = 0;
            var grid = new List<int[]>();
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                var row = new int[widthSegments + 1];
                float v = (float)iy / heightSegments;
                float uOffset = 0f;
                if (iy == 0 && thetaStart == 0) uOffset = 0.5f / widthSegments;
                else if (iy == heightSegments && thetaEnd == Math.PI) uOffset = -0.5f / widthSegments;
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    float u = (float)ix / widthSegments;
                    double phi = phiStart + u * phiLength, theta = thetaStart + v * thetaLength;
                    var vertex = new Vector3((float)(-radius * Math.Cos(phi) * Math.Sin(theta)), (float)(radius * Math.Cos(theta)), (float)(radius * Math.Sin(phi) * Math.Sin(theta)));
                    g.Positions.Add(vertex);
                    g.Normals.Add(vertex.normalized);
                    g.Uvs.Add(new Vector2(u + uOffset, 1f - v));
                    row[ix] = index++;
                }
                grid.Add(row);
            }
            for (int iy = 0; iy < heightSegments; iy++)
            {
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    int a = grid[iy][ix + 1], b = grid[iy][ix], c = grid[iy + 1][ix], d = grid[iy + 1][ix + 1];
                    if (iy != 0 || thetaStart > 0) { g.Indices.Add(a); g.Indices.Add(b); g.Indices.Add(d); }
                    if (iy != heightSegments - 1 || thetaEnd < Math.PI) { g.Indices.Add(b); g.Indices.Add(c); g.Indices.Add(d); }
                }
            }
            return g;
        }

        /// <summary>Box(width, height, depth) with one segment per side.</summary>
        public static MeshData Box(float width, float height, float depth)
        {
            var g = new MeshData();
            // buildPlane(u, v, w, udir, vdir, width, height, depth): axes by index 0 = x, 1 = y, 2 = z.
            Plane(g, 2, 1, 0, -1, -1, depth, height, width);   // px
            Plane(g, 2, 1, 0, 1, -1, depth, height, -width);   // nx
            Plane(g, 0, 2, 1, 1, 1, width, depth, height);     // py
            Plane(g, 0, 2, 1, 1, -1, width, depth, -height);   // ny
            Plane(g, 0, 1, 2, 1, -1, width, height, depth);    // pz
            Plane(g, 0, 1, 2, -1, -1, width, height, -depth);  // nz
            return g;
        }

        static void Plane(MeshData g, int u, int v, int w, float udir, float vdir, float width, float height, float depth)
        {
            int start = g.Positions.Count;
            float widthHalf = width / 2f, heightHalf = height / 2f, depthHalf = depth / 2f;
            for (int iy = 0; iy < 2; iy++)
            {
                float y = iy * height - heightHalf;
                for (int ix = 0; ix < 2; ix++)
                {
                    float x = ix * width - widthHalf;
                    var p = new float[3];
                    p[u] = x * udir; p[v] = y * vdir; p[w] = depthHalf;
                    g.Positions.Add(new Vector3(p[0], p[1], p[2]));
                    var n = new float[3];
                    n[w] = depth > 0 ? 1f : -1f;
                    g.Normals.Add(new Vector3(n[0], n[1], n[2]));
                    g.Uvs.Add(new Vector2(ix, 1 - iy));
                }
            }
            int a = start, b = start + 2, c = start + 3, d = start + 1;
            g.Indices.Add(a); g.Indices.Add(b); g.Indices.Add(d);
            g.Indices.Add(b); g.Indices.Add(c); g.Indices.Add(d);
        }

        /// <summary>Tube(path, tubularSegments, radius, radialSegments, closed = false) for a curve in the z = 0 plane.
        /// computeFrenetFrames on a planar curve gives the constant normal (0, 0, -1) (its first normal is t0 x (t0 x z)
        /// and every later frame turns about z), so the binormal is t x n; this is that result without the general
        /// parallel-transport loop.</summary>
        public static MeshData Tube(CatmullRomCurve path, int tubularSegments, float radius, int radialSegments)
        {
            var g = new MeshData();
            var nrm = new Vector3(0f, 0f, -1f);
            for (int i = 0; i <= tubularSegments; i++)
            {
                double u = (double)i / tubularSegments;
                path.GetPointAt(u, out double px, out double py, out double pz);
                path.GetTangentAt(u, out double tx, out double ty, out double tz);
                if (Math.Abs(tz) > 1e-6) throw new InvalidOperationException("PrimitiveMeshes.Tube expects a curve in the z = 0 plane");
                var t = new Vector3((float)tx, (float)ty, (float)tz);
                Vector3 bin = Vector3.Cross(t, nrm);
                var P = new Vector3((float)px, (float)py, (float)pz);
                for (int j = 0; j <= radialSegments; j++)
                {
                    double v = (double)j / radialSegments * Math.PI * 2;
                    float sin = (float)Math.Sin(v), cos = (float)-Math.Cos(v);
                    Vector3 n = (cos * nrm + sin * bin).normalized;
                    g.Normals.Add(n);
                    g.Positions.Add(P + radius * n);
                }
            }
            for (int i = 0; i <= tubularSegments; i++)
                for (int j = 0; j <= radialSegments; j++)
                    g.Uvs.Add(new Vector2((float)i / tubularSegments, (float)j / radialSegments));
            for (int j = 1; j <= tubularSegments; j++)
            {
                for (int i = 1; i <= radialSegments; i++)
                {
                    int a = (radialSegments + 1) * (j - 1) + (i - 1);
                    int b = (radialSegments + 1) * j + (i - 1);
                    int c = (radialSegments + 1) * j + i;
                    int d = (radialSegments + 1) * (j - 1) + i;
                    g.Indices.Add(a); g.Indices.Add(b); g.Indices.Add(d);
                    g.Indices.Add(b); g.Indices.Add(c); g.Indices.Add(d);
                }
            }
            return g;
        }

        /// <summary>The shortest-arc rotation taking from onto to (both unit vectors).</summary>
        public static Quaternion FromUnitVectors(Vector3 from, Vector3 to)
        {
            double r = (double)from.x * to.x + (double)from.y * to.y + (double)from.z * to.z + 1;
            double x, y, z, w;
            if (r < 2.220446049250313e-16)
            {
                r = 0;
                if (Math.Abs(from.x) > Math.Abs(from.z)) { x = -from.y; y = from.x; z = 0; w = r; }
                else { x = 0; y = -from.z; z = from.y; w = r; }
            }
            else
            {
                x = (double)from.y * to.z - (double)from.z * to.y;
                y = (double)from.z * to.x - (double)from.x * to.z;
                z = (double)from.x * to.y - (double)from.y * to.x;
                w = r;
            }
            double l = Math.Sqrt(x * x + y * y + z * z + w * w);
            return new Quaternion((float)(x / l), (float)(y / l), (float)(z / l), (float)(w / l));
        }

        /// <summary>The rotation by angle (radians) about a unit axis.</summary>
        public static Quaternion AxisAngle(Vector3 axis, double angle)
        {
            double s = Math.Sin(angle / 2);
            return new Quaternion((float)(axis.x * s), (float)(axis.y * s), (float)(axis.z * s), (float)Math.Cos(angle / 2));
        }
    }
}
