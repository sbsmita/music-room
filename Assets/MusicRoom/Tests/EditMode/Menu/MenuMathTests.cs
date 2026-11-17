// Panel geometry (spec 08 sections 1.2-1.4) against the reference numbers: the curved face and back plate, the poke surface
// coordinates (ToSurface) including the menu check's "the poke follows the curved surface" case, the canvas mapping,
// where the panel opens (placeFor) and the grab zone.
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuMathTests
    {
        static void Plane(float w, float h, out Vector3[] pos, out Vector3[] nor, out Vector2[] uv, out int[] tris)
        {
            pos = new Vector3[MenuMath.PlaneVertices];
            nor = new Vector3[MenuMath.PlaneVertices];
            uv = new Vector2[MenuMath.PlaneVertices];
            tris = new int[MenuMath.PlaneIndices];
            MenuMath.CurvedPlane(w, h, MenuMath.R, pos, nor, uv, tris);
        }

        [Test]
        public void FaceIsA32SegmentPlaneOnTheCylinder()
        {
            Plane(MenuMath.W, MenuMath.H, out var pos, out var nor, out var uv, out var tris);
            Assert.That(pos.Length, Is.EqualTo(66));
            Assert.That(tris.Length, Is.EqualTo(32 * 6));
            float minX = 1f, maxX = -1f, minZ = 1f, maxZ = -1f;
            for (int k = 0; k < pos.Length; k++)
            {
                Vector3 p = pos[k];
                // Every vertex is 0.8 m from the axis, which is 0.8 m toward the player (local z = -0.8).
                Assert.That(Mathf.Sqrt(p.x * p.x + (p.z + 0.8f) * (p.z + 0.8f)), Is.EqualTo(0.8f).Within(1e-5f));
                Assert.That(Mathf.Abs(p.y), Is.EqualTo(0.12f).Within(1e-6f));
                Assert.That(uv[k].x, Is.EqualTo((k / 2) / 32f).Within(1e-6f));
                Assert.That(uv[k].y, Is.EqualTo(k % 2));
                Assert.That(nor[k].z, Is.LessThan(-0.97f), "normals face the player (-z)");
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            // menu-check faceCurvedOnCylinder: width 2 x .8 sin .2, sag .8 (1 - cos .2); edges toward the player.
            Assert.That(maxX - minX, Is.EqualTo(2f * 0.8f * Mathf.Sin(0.2f)).Within(1e-4f));
            Assert.That(maxZ - minZ, Is.EqualTo(0.8f * (1f - Mathf.Cos(0.2f))).Within(1e-4f));
            Assert.That(maxZ, Is.EqualTo(0f).Within(1e-7f), "the face's centre line is the panel origin");
            Assert.That(pos[0].z, Is.LessThan(-0.015f), "edges sit 15.9 mm nearer the player");
            Assert.That(uv[0].x, Is.EqualTo(0f)); Assert.That(pos[0].x, Is.LessThan(0f), "u = 0 is the player's left");
        }

        [Test]
        public void TrianglesFaceThePlayer()
        {
            Plane(MenuMath.W, MenuMath.H, out var pos, out _, out _, out var tris);
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = pos[tris[t]], b = pos[tris[t + 1]], c = pos[tris[t + 2]];
                // Unity's face normal is Cross(b - a, c - a) for its clockwise front faces: it must point at the player (-z).
                Assert.That(Vector3.Cross(b - a, c - a).z, Is.LessThan(0f), "triangle " + t / 3);
            }
        }

        [Test]
        public void BackPlateIsSixMillimetresLargerAndBehind()
        {
            Assert.That(MenuMath.BackW, Is.EqualTo(0.3483f).Within(1e-4f));
            Assert.That(MenuMath.BackH, Is.EqualTo(0.2688f).Within(1e-4f));
            Assert.That(MenuMath.BackZ, Is.EqualTo(0.004f), "4 mm behind the face (+z is away from the player)");
            // The plate's own 12 px canvas margin: 488 of 512 px are the 0.332 m slab.
            Assert.That(MenuMath.BackW * 488f / 512f, Is.EqualTo(MenuMath.W + 0.012f).Within(1e-5f));
        }

        [Test]
        public void ToSurfaceFollowsTheCurve()
        {
            // On the face: z = 0 and u is the arc length.
            for (float u = -0.16f; u <= 0.16f; u += 0.02f)
                for (float y = -0.12f; y <= 0.12f; y += 0.04f)
                    for (float z = -0.05f; z <= 0.05f; z += 0.025f)
                    {
                        MenuMath.ToSurface(MenuMath.SurfacePoint(u, y, z), out float u2, out float y2, out float z2);
                        Assert.That(u2, Is.EqualTo(u).Within(1e-5f));
                        Assert.That(y2, Is.EqualTo(y).Within(1e-6f));
                        Assert.That(z2, Is.EqualTo(z).Within(1e-5f));
                    }
            // Menu check: 12 cm across, the surface has come 9 mm toward the player, so a tip 3 mm in front of the flat
            // plane is ~6 mm behind the curved face (right-handed z +.003 = Unity local z -.003).
            float ex = 0.8f * Mathf.Sin(0.15f), ey = 0.03f;
            MenuMath.ToSurface(new Vector3(ex, ey, -0.003f), out float su, out float sy, out float sz);
            Assert.That(Mathf.Abs(su - 0.12f), Is.LessThan(1.5e-3f));
            Assert.That(sz, Is.EqualTo(0.8f - Mathf.Sqrt(ex * ex + 0.797f * 0.797f)).Within(1e-6f));
            Assert.That(sz, Is.LessThan(-0.005f));
            Assert.That(sy, Is.EqualTo(ey));
            MenuMath.ToSurface(new Vector3(0.15f, 0f, 0f), out _, out _, out float edge);
            Assert.That(edge, Is.LessThan(-0.012f), "the flat plane's edge is behind the curved face");
            // In front means z > 0.
            MenuMath.ToSurface(new Vector3(0f, 0f, -0.02f), out _, out _, out float front);
            Assert.That(front, Is.EqualTo(0.02f).Within(1e-6f));
            Assert.That(MenuMath.SurfaceNormal(0f), Is.EqualTo(new Vector3(0f, 0f, -1f)));
        }

        [Test]
        public void CanvasMappingCoversTheFace()
        {
            MenuMath.ToCanvas(-MenuMath.W / 2f, MenuMath.H / 2f, out float px, out float py);
            Assert.That(px, Is.EqualTo(0f).Within(1e-3f)); Assert.That(py, Is.EqualTo(0f).Within(1e-3f));
            MenuMath.ToCanvas(MenuMath.W / 2f, -MenuMath.H / 2f, out px, out py);
            Assert.That(px, Is.EqualTo(1024f).Within(1e-3f)); Assert.That(py, Is.EqualTo(768f).Within(1e-3f));
            MenuMath.FromCanvas(512f, 384f, out float u, out float y);
            Assert.That(u, Is.EqualTo(0f).Within(1e-6f)); Assert.That(y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(MenuStyle.PxPerMetre, Is.EqualTo(3200f));
            Assert.That(MenuStyle.Corner, Is.EqualTo(64f), "2 cm corners");
            // The face mesh's uv carries the same mapping (desktop raycast: px = uv.x * 1024).
            Vector3 p = MenuMath.CanvasPoint(256f, 192f, 0f);
            MenuMath.ToSurface(p, out u, out y, out _);
            Assert.That(u / MenuMath.W + 0.5f, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(0.5f + y / MenuMath.H, Is.EqualTo(0.75f).Within(1e-5f), "uv.y = 1 - py/768");
        }

        static readonly Vector3 Head = new Vector3(0f, 1.2f, -0.1f);

        [Test]
        public void OpensInFrontOfTheSummoningHand()
        {
            // A right hand raised 0.3 m ahead: the panel opens beyond it (|v| + .35 clamped to .55), 12 cm below the eyes.
            Vector3 palm = Head + new Vector3(0.1f, -0.1f, 0.3f);
            MenuMath.PlaceFor(Head, Vector3.forward, true, palm, null, out Vector3 pos, out Quaternion rot);
            Vector3 flat = new Vector3(pos.x - Head.x, 0f, pos.z - Head.z);
            Assert.That(flat.magnitude, Is.EqualTo(0.55f).Within(1e-5f));
            Assert.That(pos.y, Is.EqualTo(Head.y - 0.12f).Within(1e-6f));
            Vector3 dirHand = new Vector3(palm.x - Head.x, 0f, palm.z - Head.z).normalized;
            Assert.That(Vector3.Dot(dirHand, flat.normalized), Is.GreaterThan(0.999f), "on the hand's side");
            Assert.That(Vector3.Dot(rot * Vector3.forward, flat.normalized), Is.GreaterThan(0.999f), "+z points away from the player");
            Assert.That((rot * Vector3.up).y, Is.EqualTo(1f).Within(1e-5f), "upright");

            // A hand close to the head (|v| = .1): .1 + .35 = .45.
            MenuMath.PlaceFor(Head, Vector3.forward, true, Head + new Vector3(0f, -0.2f, 0.1f), null, out pos, out _);
            Assert.That(new Vector2(pos.x - Head.x, pos.z - Head.z).magnitude, Is.EqualTo(0.45f).Within(1e-5f));
            // Within 5 cm (flat) of the head: the look direction at 0.5 m.
            MenuMath.PlaceFor(Head, Quaternion.Euler(20f, 30f, 0f) * Vector3.forward, true, Head + new Vector3(0.02f, -0.3f, 0.02f), null, out pos, out _);
            Assert.That(new Vector2(pos.x - Head.x, pos.z - Head.z).magnitude, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(Mathf.Atan2(pos.x - Head.x, pos.z - Head.z) * Mathf.Rad2Deg, Is.EqualTo(30f).Within(1e-3f));
            // No hand (desktop M): along the flattened look direction.
            MenuMath.PlaceFor(Head, new Vector3(1f, -1f, 0f), false, Vector3.zero, null, out pos, out rot);
            Assert.That(pos.x - Head.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(pos.z, Is.EqualTo(Head.z).Within(1e-5f));
            // Looking straight down: the +z fallback.
            MenuMath.PlaceFor(Head, Vector3.down, false, Vector3.zero, null, out pos, out _);
            Assert.That(pos.z - Head.z, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void OverTheKeyboardItStaysAtTheKeyFronts()
        {
            var go = new GameObject("PianoProbe");
            try
            {
                // The piano at its home ahead of the seat: (0, .72, head.z + .45), facing the player.
                go.transform.SetPositionAndRotation(new Vector3(0f, 0.72f, Head.z + 0.45f), Quaternion.identity);
                MenuMath.PlaceFor(Head, Vector3.forward, false, Vector3.zero, go.transform, out Vector3 pos, out _);
                Vector3 l = go.transform.InverseTransformPoint(pos);
                Assert.That(l.z, Is.EqualTo(-0.01f).Within(1e-5f), "1 cm in front of the white-key fronts");
                Assert.That(pos.y, Is.EqualTo(Head.y - 0.12f).Within(1e-6f), "height unchanged");
                // Turned 90 degrees away, the default spot is outside the piano's box: untouched.
                go.transform.SetPositionAndRotation(new Vector3(1.5f, 0.72f, 0f), Quaternion.identity);
                MenuMath.PlaceFor(Head, Vector3.forward, false, Vector3.zero, go.transform, out pos, out _);
                Assert.That(pos.z - Head.z, Is.EqualTo(0.5f).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GrabZoneIsTheMeshBoxesPlusTwelveCentimetres()
        {
            Bounds z = MenuMath.GrabZone(Matrix4x4.identity);
            float backHalfW = 0.8f * Mathf.Sin(0.5f * MenuMath.BackW / 0.8f);
            Assert.That(z.max.x, Is.EqualTo(backHalfW + 0.12f).Within(1e-5f));
            Assert.That(z.max.y, Is.EqualTo(MenuMath.BackH / 2f + 0.12f).Within(1e-5f));
            Assert.That(z.max.z, Is.EqualTo(MenuMath.BackZ + 0.12f).Within(1e-5f), "behind: the back plate");
            Assert.That(z.min.z, Is.EqualTo(-0.8f * (1f - Mathf.Cos(0.2f)) - 0.12f).Within(1e-5f), "in front: the face's edges");
            var m = Matrix4x4.TRS(new Vector3(1f, 1f, 1f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            Bounds r = MenuMath.GrabZone(m);
            Assert.That(r.center.y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(r.extents.z, Is.EqualTo(backHalfW + 0.12f).Within(1e-4f), "turned: the width lies along z");
            Assert.IsTrue(r.Contains(new Vector3(1.05f, 1.2f, 1.2f)));
        }

        [Test]
        public void SummonRingIsADoubleSidedAnnulus()
        {
            Mesh ring = MenuMath.BuildRing();
            try
            {
                Assert.That(ring.triangles.Length, Is.EqualTo(40 * 12), "40 segments, both windings");
                float rmin = 1f, rmax = 0f;
                foreach (var v in ring.vertices) { rmin = Mathf.Min(rmin, v.magnitude); rmax = Mathf.Max(rmax, v.magnitude); Assert.That(v.z, Is.EqualTo(0f)); }
                Assert.That(rmin, Is.EqualTo(0.028f).Within(1e-6f));
                Assert.That(rmax, Is.EqualTo(0.036f).Within(1e-6f));
            }
            finally { Object.DestroyImmediate(ring); }
            Assert.That(MenuMath.EaseOut(0f), Is.EqualTo(0f));
            Assert.That(MenuMath.EaseOut(0.5f), Is.EqualTo(0.75f));
            Assert.That(MenuMath.EaseOut(1f), Is.EqualTo(1f));
        }

        [Test]
        public void ContractConstantsAreTheReferenceValues()
        {
            Assert.That(MenuConstants.Arm, Is.EqualTo(0.003f));
            Assert.That(MenuConstants.Hover, Is.EqualTo(0.010f));
            Assert.That(MenuConstants.Slop, Is.EqualTo(0.008f));
            Assert.That(MenuConstants.Behind, Is.EqualTo(-0.06f));
            Assert.That(MenuConstants.EdgeMargin, Is.EqualTo(0.01f));
            Assert.That(MenuConstants.DebounceMs, Is.EqualTo(250f));
            Assert.That(MenuConstants.SummonCos, Is.EqualTo(Mathf.Cos(40f * Mathf.Deg2Rad)).Within(1e-4f));
            Assert.That(MenuModule.ModuleOrder, Is.EqualTo(60));
            Assert.That(MenuModule.MenuTickOrder, Is.EqualTo(50));
        }
    }
}
