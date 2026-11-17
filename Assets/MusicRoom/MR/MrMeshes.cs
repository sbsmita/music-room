// Small procedural meshes of the MR visuals (the piano's reticle, the shadow catcher, the hint panel) and a hand's aim ray.
// Kept from the snowy window's file when the window was removed (2026-11-13, the player's request: snow now falls in the
// whole room, Room/Decor).
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.MR
{
    /// <summary>A hand's aim ray this frame (HandFrame.pointer).</summary>
    public struct HandRay { public bool Valid; public Ray Ray; public int Hand; }

    /// <summary>Small procedural meshes of the MR visuals (flat on local XY unless noted).</summary>
    public static class MrMeshes
    {
        public static MeshRenderer AddPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = material != null;
            return r;
        }

        /// <summary>A w x h rectangle in local XY centred on the origin (a plane mesh; culling is off in its material).</summary>
        public static Mesh Rect(float w, float h)
        {
            float x = w * 0.5f, y = h * 0.5f;
            var m = new Mesh { name = "MR rect" };
            m.vertices = new[] { new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f), new Vector3(x, y, 0f), new Vector3(-x, y, 0f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The rectangle's four edges as a line mesh.</summary>
        public static Mesh RectOutline(float w, float h)
        {
            float x = w * 0.5f, y = h * 0.5f;
            var m = new Mesh { name = "MR rect outline" };
            m.vertices = new[] { new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f), new Vector3(x, y, 0f), new Vector3(-x, y, 0f) };
            m.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A flat ring on local XZ (facing up): a ring mesh rotated -pi/2 about x.</summary>
        public static Mesh RingXZ(float inner, float outer, int segments)
        {
            var v = new Vector3[(segments + 1) * 2];
            var t = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * inner, 0f, s * inner);
                v[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, k = i * 6;
                t[k] = a; t[k + 1] = a + 1; t[k + 2] = a + 3;
                t[k + 3] = a; t[k + 4] = a + 3; t[k + 5] = a + 2;
            }
            var m = new Mesh { name = "MR ring" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A w (x) by d (z) rectangle on local XZ centred on (0, 0, z0), facing up.</summary>
        public static Mesh RectXZ(float w, float d, float z0)
        {
            float x = w * 0.5f, z = d * 0.5f;
            var m = new Mesh { name = "MR rect XZ" };
            m.vertices = new[] { new Vector3(-x, 0f, z0 - z), new Vector3(x, 0f, z0 - z), new Vector3(x, 0f, z0 + z), new Vector3(-x, 0f, z0 + z) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>That rectangle's edges as lines.</summary>
        public static Mesh RectOutlineXZ(float w, float d, float z0)
        {
            float x = w * 0.5f, z = d * 0.5f;
            var m = new Mesh { name = "MR rect XZ outline" };
            m.vertices = new[] { new Vector3(-x, 0f, z0 - z), new Vector3(x, 0f, z0 - z), new Vector3(x, 0f, z0 + z), new Vector3(-x, 0f, z0 + z) };
            m.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
