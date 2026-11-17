// Right-handed source frame (y up, -z forward; the specs' numbers) -> Unity (left-handed, +z forward):
// (x, y, z) -> (x, y, -z);
// quaternion (x, y, z, w) -> (-x, -y, z, w); yaw -> -yaw; source YXZ Euler (a, b, c) -> Quaternion.Euler(-a, -b, c);
// cross products and triangle winding flip (spec 05 section 0, spec 07 section 0).
using UnityEngine;

namespace MusicRoom.Core
{
    public static class Coords
    {
        public static Vector3 FromRightHanded(float x, float y, float z) => new Vector3(x, y, -z);
        public static Vector3 FromRightHanded(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        public static Vector3 ToRightHanded(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        public static Quaternion FromRightHanded(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);
        public static Quaternion FromRightHanded(float x, float y, float z, float w) => new Quaternion(-x, -y, z, w);
        /// <summary>Right-handed source-frame yaw in radians (about +y) -> Unity yaw in degrees.</summary>
        public static float YawDegFromRightHandedRad(float yawRad) => -yawRad * Mathf.Rad2Deg;
        /// <summary>Source-frame YXZ Euler in radians -> Unity rotation (Unity applies z, x, y = the same YXZ product).</summary>
        public static Quaternion FromRightHandedEulerYXZ(float aRad, float bRad, float cRad) =>
            Quaternion.Euler(-aRad * Mathf.Rad2Deg, -bRad * Mathf.Rad2Deg, cRad * Mathf.Rad2Deg);
        /// <summary>Source-frame XYZ Euler in radians -> Unity rotation.</summary>
        public static Quaternion FromRightHandedEulerXYZ(float aRad, float bRad, float cRad) =>
            Quaternion.AngleAxis(-aRad * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-bRad * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(cRad * Mathf.Rad2Deg, Vector3.forward);
        /// <summary>Unity heading of a flattened forward vector in degrees: atan2(f.x, f.z).</summary>
        public static float HeadingDeg(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        /// <summary>Reverse triangle winding in place (mirrored procedural meshes).</summary>
        public static void ReverseWinding(int[] tris)
        {
            for (int i = 0; i + 2 < tris.Length; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
        }
        /// <summary>Mirror a mesh built in the right-handed source frame: negate z of positions and normals, reverse winding.</summary>
        public static void MirrorZ(Vector3[] vertices, Vector3[] normals, int[] tris)
        {
            for (int i = 0; i < vertices.Length; i++) vertices[i].z = -vertices[i].z;
            if (normals != null) for (int i = 0; i < normals.Length; i++) normals[i].z = -normals[i].z;
            ReverseWinding(tris);
        }
        public static float Smoothstep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
    }
}
