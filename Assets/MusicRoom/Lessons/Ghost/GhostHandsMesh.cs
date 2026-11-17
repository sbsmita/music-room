// The ghost hands' drawing: every hand slot (MaxHands) in ONE dynamic mesh and ONE renderer (the GhostHands material: a
// depth pre-pass and the soft translucent pass, two passes of one draw), as Sunny's demo hand merges its parts
// (Guide/SunnyDemoMesh.cs). Each slot is a jointed hand: a rounded palm, a forearm stub fading out toward the elbow,
// three capsules a finger, and a held tool (a stick's shaft and its tip, or a mallet's head) shown only while it is held.
// Each frame the posed slots' vertices (position, normal, colour with the slot's opacity) are written into one persistent
// array and uploaded with SetVertexBufferData; a slot that is not shown is collapsed once (zero-area triangles).
// Odd slots are right hands; even slots are left hands, the same geometry mirrored (x -> -x) with the triangles' winding
// reversed, so back faces cull the same way. Allocation-free per frame.
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Lessons.Ghost
{
    public sealed class GhostHandsMesh
    {
        public const int MaxHands = 8;
        public const int Palm = 0, Forearm = 1, FirstSegment = 2, ToolShaft = 17, ToolHead = 18, PartCount = 19;

        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Color32 Color;
        }

        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        public Mesh Mesh { get; }
        public int VerticesPerHand { get; }
        public int VertexCount => _verts.Length;

        // the part templates (one hand), in part space
        readonly Vector3[][] _pos = new Vector3[PartCount][];
        readonly Vector3[][] _nrm = new Vector3[PartCount][];
        readonly byte[][] _fade = new byte[PartCount][];
        readonly int[] _start = new int[PartCount];
        Vertex[] _verts;
        readonly bool[] _collapsed = new bool[MaxHands];
        readonly byte[] _alpha = new byte[MaxHands];

        public GhostHandsMesh()
        {
            Build(Palm, PalmMesh(GhostHandModel.PalmSize), false);
            Build(Forearm, Capsule(GhostHandModel.ForearmRadius0, GhostHandModel.ForearmRadius1, GhostHandModel.ForearmLength, 10, 2), true);
            for (int f = 0; f < GhostHandModel.Fingers; f++)
                for (int k = 0; k < 3; k++)
                {
                    float r0 = Mathf.Lerp(GhostHandModel.Radius0[f], GhostHandModel.Radius1[f], k / 3f);
                    float r1 = Mathf.Lerp(GhostHandModel.Radius0[f], GhostHandModel.Radius1[f], (k + 1) / 3f);
                    Build(FirstSegment + f * 3 + k, Capsule(r0, r1, GhostHandModel.Length[f, k], 8, k == 2 ? 2 : 1), false);
                }
            // The tool: the stick's shaft (butt at 0, tip at its length; the mallet uses it too) and a unit sphere (scaled to
            // the stick's tip ball or the mallet's head).
            Build(ToolShaft, Capsule(GhostHandModel.StickRadius0, GhostHandModel.StickRadius1, GhostHandModel.StickAhead + GhostHandModel.StickBehind, 6, 1), false);
            Build(ToolHead, Capsule(1f, 1f, 0f, 8, 3), false);

            int perHand = 0;
            for (int p = 0; p < PartCount; p++) { _start[p] = perHand; perHand += _pos[p].Length; }
            VerticesPerHand = perHand;
            _verts = new Vertex[perHand * MaxHands];

            // indices: each slot's copy of every part's triangles (left slots reversed)
            int triCount = 0;
            for (int p = 0; p < PartCount; p++) triCount += _tris[p].Length;
            var idx = new ushort[triCount * MaxHands];
            int k2 = 0;
            for (int h = 0; h < MaxHands; h++)
            {
                bool left = IsLeft(h);
                int o = h * perHand;
                for (int p = 0; p < PartCount; p++)
                {
                    int[] t = _tris[p];
                    for (int j = 0; j < t.Length; j += 3)
                    {
                        idx[k2++] = (ushort)(o + _start[p] + t[j]);
                        idx[k2++] = (ushort)(o + _start[p] + (left ? t[j + 2] : t[j + 1]));
                        idx[k2++] = (ushort)(o + _start[p] + (left ? t[j + 1] : t[j + 2]));
                    }
                }
            }
            Mesh = new Mesh { name = "GhostHands" };
            Mesh.MarkDynamic();
            Mesh.SetVertexBufferParams(_verts.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            Mesh.SetVertexBufferData(_verts, 0, 0, _verts.Length, 0, Quiet);
            Mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
            Mesh.SetIndexBufferData(idx, 0, 0, idx.Length, Quiet);
            Mesh.subMeshCount = 1;
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length), Quiet);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            for (int h = 0; h < MaxHands; h++) _collapsed[h] = true;
            for (int p = 0; p < PartCount; p++) _tris[p] = null;
        }

        /// <summary>Slot h is a left hand (even slots).</summary>
        public static bool IsLeft(int h) => (h & 1) == 0;

        readonly int[][] _tris = new int[PartCount][];

        void Build(int part, (Vector3[] v, Vector3[] n, int[] t, byte[] fade) m, bool forearm)
        {
            _pos[part] = m.v;
            _nrm[part] = m.n;
            _tris[part] = m.t;
            _fade[part] = m.fade;
            if (!forearm) return;
            // the forearm runs back from the wrist (part space -z) and fades out toward the elbow
            for (int i = 0; i < m.v.Length; i++)
            {
                float u = Mathf.Clamp01(m.v[i].z / GhostHandModel.ForearmLength);
                m.v[i] = new Vector3(m.v[i].x, m.v[i].y, -m.v[i].z);
                m.n[i] = new Vector3(m.n[i].x, m.n[i].y, -m.n[i].z);
                m.fade[i] = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(1f - u) * (1f - u));
            }
            // mirrored along z: flip the winding back
            for (int j = 0; j < m.t.Length; j += 3) { int a = m.t[j + 1]; m.t[j + 1] = m.t[j + 2]; m.t[j + 2] = a; }
        }

        /// <summary>A capsule along +z from 0 to length, radius r0 at 0 and r1 at the end, with hemispherical ends (length 0:
        /// a sphere of radius r0).</summary>
        static (Vector3[], Vector3[], int[], byte[]) Capsule(float r0, float r1, float length, int sides, int capRings)
        {
            // rings: back pole, capRings back cap rings, equator at 0, equator at length, capRings front cap rings, front pole
            int ringCount = 2 * capRings + 2;
            int n = ringCount * sides + 2;
            var v = new Vector3[n];
            var nr = new Vector3[n];
            var fade = new byte[n];
            int k = 0;
            v[k] = new Vector3(0f, 0f, -r0); nr[k] = Vector3.back; k++;
            for (int r = 0; r < ringCount; r++)
            {
                bool front = r >= capRings + 1;
                float rad = front ? r1 : r0;
                float z0 = front ? length : 0f;
                float lat;   // -90 (back pole) .. +90 (front pole)
                if (!front) lat = -90f + 90f * (r + 1) / (capRings + 1);
                else lat = 90f * (r - capRings - 1) / (capRings + 1);
                float cl = Mathf.Cos(lat * Mathf.Deg2Rad), sl = Mathf.Sin(lat * Mathf.Deg2Rad);
                for (int s = 0; s < sides; s++)
                {
                    float a = 2f * Mathf.PI * s / sides;
                    var dir = new Vector3(Mathf.Cos(a) * cl, Mathf.Sin(a) * cl, sl);
                    v[k] = new Vector3(dir.x * rad, dir.y * rad, z0 + dir.z * rad);
                    nr[k] = dir;
                    k++;
                }
            }
            v[k] = new Vector3(0f, 0f, length + r1); nr[k] = Vector3.forward; k++;
            for (int i = 0; i < n; i++) fade[i] = 255;
            var t = new System.Collections.Generic.List<int>(n * 6);
            for (int s = 0; s < sides; s++) { int a = 1 + s, b = 1 + (s + 1) % sides; t.Add(0); t.Add(b); t.Add(a); }
            for (int r = 0; r < ringCount - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = 1 + r * sides + s, b = 1 + r * sides + (s + 1) % sides;
                    int c = a + sides, d = b + sides;
                    t.Add(a); t.Add(b); t.Add(c);
                    t.Add(b); t.Add(d); t.Add(c);
                }
            int last = n - 1, lr = 1 + (ringCount - 1) * sides;
            for (int s = 0; s < sides; s++) { int a = lr + s, b = lr + (s + 1) % sides; t.Add(a); t.Add(b); t.Add(last); }
            int[] tris = t.ToArray();
            Orient(v, nr, tris);
            return (v, nr, tris, fade);
        }

        /// <summary>Every triangle wound so its face normal points the way of its vertices' outward reference normals.</summary>
        static void Orient(Vector3[] v, Vector3[] reference, int[] t)
        {
            for (int j = 0; j < t.Length; j += 3)
            {
                Vector3 fn = Vector3.Cross(v[t[j + 1]] - v[t[j]], v[t[j + 2]] - v[t[j]]);
                if (Vector3.Dot(fn, reference[t[j]] + reference[t[j + 1]] + reference[t[j + 2]]) < 0f) { int a = t[j + 1]; t[j + 1] = t[j + 2]; t[j + 2] = a; }
            }
        }

        /// <summary>The palm: a rounded box (a superellipsoid), a little thinner toward the fingers.</summary>
        static (Vector3[], Vector3[], int[], byte[]) PalmMesh(Vector3 size)
        {
            const int lon = 12, lat = 8;
            const float e = 0.45f;   // squareness (1 = an ellipsoid)
            int n = (lat - 1) * lon + 2;
            var v = new Vector3[n];
            var nr = new Vector3[n];
            var fade = new byte[n];
            Vector3 h = size * 0.5f;
            int k = 0;
            v[k++] = new Vector3(0f, -h.y, 0f);
            for (int i = 1; i < lat; i++)
            {
                float th = Mathf.PI * i / lat - Mathf.PI * 0.5f;   // -90 .. 90 (bottom .. top)
                for (int j = 0; j < lon; j++)
                {
                    float ph = 2f * Mathf.PI * j / lon;
                    float cx = Sp(Mathf.Cos(th), e) * Sp(Mathf.Cos(ph), e), cz = Sp(Mathf.Cos(th), e) * Sp(Mathf.Sin(ph), e), cy = Sp(Mathf.Sin(th), e);
                    float taper = 1f - 0.25f * Mathf.Clamp01(cz);   // thinner at the knuckles
                    v[k++] = new Vector3(cx * h.x, cy * h.y * taper, cz * h.z);
                }
            }
            v[k++] = new Vector3(0f, h.y, 0f);
            var t = new System.Collections.Generic.List<int>(n * 6);
            for (int j = 0; j < lon; j++) { int a = 1 + j, b = 1 + (j + 1) % lon; t.Add(0); t.Add(a); t.Add(b); }
            for (int i = 0; i < lat - 2; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = 1 + i * lon + j, b = 1 + i * lon + (j + 1) % lon, c = a + lon, d = b + lon;
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(b); t.Add(c); t.Add(d);
                }
            int top = n - 1, lr = 1 + (lat - 2) * lon;
            for (int j = 0; j < lon; j++) { int a = lr + j, b = lr + (j + 1) % lon; t.Add(a); t.Add(top); t.Add(b); }
            int[] tris = t.ToArray();
            Orient(v, v, tris);   // outward: away from the palm's centre
            // smooth normals from the faces
            for (int j = 0; j < tris.Length; j += 3)
            {
                Vector3 fn = Vector3.Cross(v[tris[j + 1]] - v[tris[j]], v[tris[j + 2]] - v[tris[j]]);
                nr[tris[j]] += fn; nr[tris[j + 1]] += fn; nr[tris[j + 2]] += fn;
            }
            for (int i = 0; i < n; i++) { nr[i] = nr[i].normalized; fade[i] = 255; }
            return (v, nr, tris, fade);
        }

        static float Sp(float c, float e) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);

        /// <summary>Writes slot h's part p with matrix m (part space to the renderer's space) and opacity alpha.</summary>
        public void WritePart(int h, int part, in Matrix4x4 m, byte alpha)
        {
            Vector3[] src = _pos[part], nrm = _nrm[part];
            byte[] fade = _fade[part];
            Vertex[] dst = _verts;
            int o = h * VerticesPerHand + _start[part];
            // the matrix inlined (a rigid turn, a mirror, a uniform scale: the shader normalises the normals)
            float m00 = m.m00, m01 = m.m01, m02 = m.m02, m03 = m.m03;
            float m10 = m.m10, m11 = m.m11, m12 = m.m12, m13 = m.m13;
            float m20 = m.m20, m21 = m.m21, m22 = m.m22, m23 = m.m23;
            bool whole = alpha == 255;
            for (int j = 0; j < src.Length; j++)
            {
                Vector3 p = src[j], n = nrm[j];
                ref Vertex v = ref dst[o + j];
                v.Position.x = m00 * p.x + m01 * p.y + m02 * p.z + m03;
                v.Position.y = m10 * p.x + m11 * p.y + m12 * p.z + m13;
                v.Position.z = m20 * p.x + m21 * p.y + m22 * p.z + m23;
                v.Normal.x = m00 * n.x + m01 * n.y + m02 * n.z;
                v.Normal.y = m10 * n.x + m11 * n.y + m12 * n.z;
                v.Normal.z = m20 * n.x + m21 * n.y + m22 * n.z;
                v.Color.r = v.Color.g = v.Color.b = 255;
                v.Color.a = whole ? fade[j] : (byte)(alpha * fade[j] / 255);
            }
        }

        /// <summary>Hides part p of slot h (the tool while nothing is held): its vertices collapse to one point.</summary>
        public void HidePart(int h, int part, Vector3 at)
        {
            Vector3[] src = _pos[part];
            int o = h * VerticesPerHand + _start[part];
            var v = new Vertex { Position = at, Normal = Vector3.up, Color = new Color32(255, 255, 255, 0) };
            for (int j = 0; j < src.Length; j++) _verts[o + j] = v;
        }

        public void Shown(int h, byte alpha) { _collapsed[h] = false; _alpha[h] = alpha; }

        /// <summary>Slot h is not shown: collapsed once (nothing to rasterise).</summary>
        public void Collapse(int h)
        {
            if (_collapsed[h]) return;
            _collapsed[h] = true;
            int o = h * VerticesPerHand;
            var v = new Vertex { Position = Vector3.zero, Normal = Vector3.up, Color = new Color32(255, 255, 255, 0) };
            for (int j = 0; j < VerticesPerHand; j++) _verts[o + j] = v;
        }

        public bool Collapsed(int h) => _collapsed[h];

        /// <summary>Uploads the frame's vertices with these bounds (the renderer's space).</summary>
        public void Upload(Bounds bounds)
        {
            Mesh.SetVertexBufferData(_verts, 0, 0, _verts.Length, 0, Quiet);
            Mesh.bounds = bounds;
        }

        /// <summary>A vertex's position (tests).</summary>
        public Vector3 VertexAt(int h, int part, int j) => _verts[h * VerticesPerHand + _start[part] + j].Position;
        public int PartVertexCount(int part) => _pos[part].Length;

        public void Dispose()
        {
            if (Mesh != null) Object.Destroy(Mesh);
        }
    }
}
