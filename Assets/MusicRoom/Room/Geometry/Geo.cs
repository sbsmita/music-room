// Triangle geometry in the right-handed source frame (-z forward), built with the PrimitiveMeshes generators so a
// layout is vertex for vertex the golden (spec 07 section 0: every procedural layout is computed in the source frame
// with the original formulas, then converted once at the end). ToUnityMesh mirrors once: z and normal z negated,
// triangle winding reversed. Quaternion / Matrix4x4 algebra does not depend on handedness (AngleAxis(a, axis) builds the
// standard axis-angle matrix), so source-frame transforms are composed with Unity types (Rh).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Geometry
{
    public sealed class Geo
    {
        public readonly List<Vector3> P = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        /// <summary>Optional per-vertex data (flame seed + intensity, decal kind): TEXCOORD1. Empty = none.</summary>
        public readonly List<Vector2> UV1 = new List<Vector2>();
        /// <summary>Optional vertex colours (decal opacity in alpha). Empty = none.</summary>
        public readonly List<Color> C = new List<Color>();
        /// <summary>Optional per-vertex data in TEXCOORD2 / TEXCOORD3 (the plant sway: pivot and its parameters, see
        /// RoomBuilder.Plant and MusicRoom/LitSway). Not transformed by Append: callers store final values. Empty = none.</summary>
        public readonly List<Vector4> UV2 = new List<Vector4>(), UV3 = new List<Vector4>();
        public readonly List<int> I = new List<int>();

        public int VertexCount => P.Count;

        public void Clear() { P.Clear(); N.Clear(); UV.Clear(); UV1.Clear(); C.Clear(); UV2.Clear(); UV3.Clear(); I.Clear(); }

        public Geo Clone()
        {
            var g = new Geo();
            g.P.AddRange(P); g.N.AddRange(N); g.UV.AddRange(UV); g.UV1.AddRange(UV1); g.C.AddRange(C);
            g.UV2.AddRange(UV2); g.UV3.AddRange(UV3); g.I.AddRange(I);
            return g;
        }

        /// <summary>One optional channel during Append: pad whichever side lacks it, so indices stay aligned.</summary>
        static void Merge<T>(List<T> mine, int myCount, List<T> theirs, int theirCount, T pad)
        {
            bool has = theirs.Count == theirCount && theirCount > 0, mineHas = mine.Count == myCount && myCount > 0;
            if (!has && !mineHas) return;
            if (has && !mineHas) for (int i = mine.Count; i < myCount; i++) mine.Add(pad);
            if (has) mine.AddRange(theirs);
            else for (int i = 0; i < theirCount; i++) mine.Add(pad);
        }

        /// <summary>Append <paramref name="g"/> transformed by the source-frame matrix <paramref name="m"/> (normals by its
        /// inverse transpose, so the blobs' non-uniform scale keeps them right).</summary>
        public void Append(Geo g, Matrix4x4 m)
        {
            int baseIndex = P.Count, n0 = g.P.Count;
            Matrix4x4 nm = m.inverse.transpose;
            // Optional channels are all-or-nothing per batch: pad whichever side lacks one so indices stay aligned.
            Merge(UV1, baseIndex, g.UV1, n0, Vector2.zero);
            Merge(C, baseIndex, g.C, n0, Color.white);
            Merge(UV2, baseIndex, g.UV2, n0, Vector4.zero);
            Merge(UV3, baseIndex, g.UV3, n0, Vector4.zero);
            for (int i = 0; i < n0; i++)
            {
                P.Add(m.MultiplyPoint3x4(g.P[i]));
                Vector3 n = nm.MultiplyVector(g.N[i]);
                N.Add(n.sqrMagnitude > 1e-20f ? n.normalized : n);
                UV.Add(g.UV[i]);
            }
            for (int i = 0; i < g.I.Count; i++) I.Add(baseIndex + g.I[i]);
        }

        public void Append(Geo g) => Append(g, Matrix4x4.identity);

        /// <summary>Fill TEXCOORD1 of every vertex with one value (flame seed / intensity, decal kind).</summary>
        public Geo WithUv1(Vector2 v)
        {
            UV1.Clear();
            for (int i = 0; i < P.Count; i++) UV1.Add(v);
            return this;
        }

        public Geo WithColor(Color c)
        {
            C.Clear();
            for (int i = 0; i < P.Count; i++) C.Add(c);
            return this;
        }

        /// <summary>Fill TEXCOORD2 and TEXCOORD3 of every vertex (stored as given: Append does not transform them).</summary>
        public Geo WithUv23(Vector4 uv2, Vector4 uv3)
        {
            UV2.Clear(); UV3.Clear();
            for (int i = 0; i < P.Count; i++) { UV2.Add(uv2); UV3.Add(uv3); }
            return this;
        }

        /// <summary>Remap UV0 into a sub-rectangle of an atlas: uv' = offset + uv x scale.</summary>
        public Geo RemapUv(Vector2 scale, Vector2 offset)
        {
            for (int i = 0; i < UV.Count; i++) UV[i] = offset + Vector2.Scale(UV[i], scale);
            return this;
        }

        public Geo Translate(float x, float y, float z)
        {
            var d = new Vector3(x, y, z);
            for (int i = 0; i < P.Count; i++) P[i] += d;
            return this;
        }

        /// <summary>Rotate every position and normal about +y (radians, source frame).</summary>
        public Geo RotateY(float rad)
        {
            Quaternion q = Rh.AxisAngle(Vector3.up, rad);
            for (int i = 0; i < P.Count; i++) { P[i] = q * P[i]; N[i] = q * N[i]; }
            return this;
        }

        /// <summary>Vertex normals for indexed triangles: area-weighted face normals
        /// cross(C - B, A - B), summed per vertex and normalised.</summary>
        public void ComputeVertexNormals()
        {
            for (int i = 0; i < N.Count; i++) N[i] = Vector3.zero;
            while (N.Count < P.Count) N.Add(Vector3.zero);
            for (int t = 0; t + 2 < I.Count; t += 3)
            {
                int a = I[t], b = I[t + 1], c = I[t + 2];
                Vector3 cb = Vector3.Cross(P[c] - P[b], P[a] - P[b]);
                N[a] += cb; N[b] += cb; N[c] += cb;
            }
            for (int i = 0; i < N.Count; i++) N[i] = N[i].sqrMagnitude > 0f ? N[i].normalized : N[i];
        }

        /// <summary>Copy each triangle's vertices so every face also exists facing the other way (inward normals,
        /// reversed winding): a two-sided Lit surface (lamp shade) without a Cull Off template, lit correctly inside.</summary>
        public Geo DoubleSided()
        {
            int n = P.Count, ni = I.Count;
            for (int i = 0; i < n; i++)
            {
                P.Add(P[i]); N.Add(-N[i]); UV.Add(UV[i]);
                if (UV1.Count == n) UV1.Add(UV1[i]);
                if (C.Count == n) C.Add(C[i]);
                if (UV2.Count == n) UV2.Add(UV2[i]);
                if (UV3.Count == n) UV3.Add(UV3[i]);
            }
            for (int t = 0; t + 2 < ni; t += 3) { I.Add(n + I[t]); I.Add(n + I[t + 2]); I.Add(n + I[t + 1]); }
            return this;
        }

        // ------------------------------------------------------------------ conversion

        /// <summary>The geometry as a Unity mesh: z mirrored (positions, normals), winding reversed (spec 07 section 0).</summary>
        public Mesh ToUnityMesh(string name, bool tangents = false)
        {
            int n = P.Count;
            var pos = new Vector3[n];
            var nor = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = P[i]; p.z = -p.z; pos[i] = p;
                Vector3 q = N[i]; q.z = -q.z; nor[i] = q;
            }
            var tris = I.ToArray();
            MusicRoom.Core.Coords.ReverseWinding(tris);
            var m = new Mesh { name = name };
            if (n > 65535) m.indexFormat = IndexFormat.UInt32;
            m.vertices = pos;
            m.normals = nor;
            m.SetUVs(0, UV);
            if (UV1.Count == n && n > 0) m.SetUVs(1, UV1);
            if (C.Count == n && n > 0) m.SetColors(C);
            if (UV2.Count == n && n > 0) m.SetUVs(2, UV2);
            if (UV3.Count == n && n > 0) m.SetUVs(3, UV3);
            m.triangles = tris;
            if (tangents) m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Overwrite UV0 from the Unity-space position of each vertex (after the mirror): world-scale tiling
        /// for the floor, walls and bricks (spec 07 sections 2 and 3.6).</summary>
        public delegate Vector2 UvFromUnity(Vector3 unityPos, Vector3 unityNormal);

        public void SetUvFromUnity(UvFromUnity f)
        {
            for (int i = 0; i < P.Count; i++)
            {
                Vector3 p = P[i]; p.z = -p.z;
                Vector3 q = N[i]; q.z = -q.z;
                UV[i] = f(p, q);
            }
        }
    }

    /// <summary>Transform helpers in the right-handed source frame (-z forward), with Unity types.</summary>
    public static class Rh
    {
        public static Quaternion AxisAngle(Vector3 axis, float rad) => Quaternion.AngleAxis(rad * Mathf.Rad2Deg, axis);

        /// <summary>Source-frame Euler (radians) to a quaternion: order "XYZ" = Rx Ry Rz (the default), "YXZ" = Ry Rx Rz.</summary>
        public static Quaternion Euler(float x, float y, float z, string order = "XYZ")
        {
            Quaternion qx = AxisAngle(Vector3.right, x), qy = AxisAngle(Vector3.up, y), qz = AxisAngle(Vector3.forward, z);
            switch (order)
            {
                case "YXZ": return qy * qx * qz;
                case "ZXY": return qz * qx * qy;
                case "ZYX": return qz * qy * qx;
                case "YZX": return qy * qz * qx;
                case "XZY": return qx * qz * qy;
                default: return qx * qy * qz;
            }
        }

        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale) => Matrix4x4.TRS(pos, rot, scale);
        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot) => Matrix4x4.TRS(pos, rot, Vector3.one);
        public static Matrix4x4 T(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
        public static Matrix4x4 TRy(float x, float y, float z, float yawRad) => Matrix4x4.TRS(new Vector3(x, y, z), AxisAngle(Vector3.up, yawRad), Vector3.one);

        /// <summary>A source-frame pose as a Unity pose (position z negated; quaternion (-x, -y, z, w)).</summary>
        public static Vector3 ToUnity(Vector3 p) => new Vector3(p.x, p.y, -p.z);
        public static Quaternion ToUnity(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);

        /// <summary>A source-frame pose (in the room frame) as the transform's LOCAL pose: its parent is the room root (or at
        /// the root's origin), which carries the room's turn (RoomFrame).</summary>
        public static void Apply(Transform t, Matrix4x4 rhWorld)
        {
            t.SetLocalPositionAndRotation(ToUnity((Vector3)rhWorld.GetColumn(3)), ToUnity(rhWorld.rotation));
        }
    }
}
