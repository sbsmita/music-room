// The space between the kit's pieces (user, 2026-11-14: "its just two drums intersect little. can you create little
// space"). Every pair of pieces (snare, toms, floor tom, kick, pad, hi-hat, crash, ride) keeps DrumSpec.MinPieceGap
// (2 cm) between them, measured on the kit AS BUILT: points on every mesh of one piece (its shell, heads, hoops and lugs;
// the pad's body, rim and top; a cymbal's plate and nut, the lower hi-hat both closed and open), triangles subdivided to
// 1 cm, against the other piece's solid (DrumKitBuilder's own primitives from DrumSpec: the shell cylinder with its heads,
// the two hoop tori, the eight lug boxes; the pad's body and rim; a cymbal's plate, bell and nut), both ways round.
// Stands are not pieces: a tripod's legs may pass under the kick (they always did) and a pole meets its own drum.
using System.Collections.Generic;
using System.Text;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class DrumSpacingTests
    {
        const float Step = 0.01f;   // triangle sampling: no two samples on a face more than 1 cm apart

        [Test]
        public void NoTwoPiecesTouchAndEachKeepsAGap()
        {
            using var k = new KitFixture();
            var points = new List<Vector3>[DrumSpec.PieceCount];
            for (int i = 0; i < DrumSpec.PieceCount; i++) points[i] = Surface(k.Piece(i));

            var log = new StringBuilder("[Drums] gaps (cm):");
            float worst = float.PositiveInfinity;
            string worstPair = null;
            var failures = new List<string>();
            for (int a = 0; a < DrumSpec.PieceCount; a++)
                for (int b = a + 1; b < DrumSpec.PieceCount; b++)
                {
                    DrumPiece pa = k.Piece(a), pb = k.Piece(b);
                    float gap = Mathf.Min(Clearance(points[a], pb), Clearance(points[b], pa));
                    string name = Name(pa) + "-" + Name(pb);
                    log.Append($" {name} {gap * 100f:F2};");
                    if (gap < worst) { worst = gap; worstPair = name; }
                    if (gap < DrumSpec.MinPieceGap) failures.Add($"{name} {gap * 100f:F2} cm");
                }
            Debug.Log(log.ToString());
            Assert.That(failures, Is.Empty, $"every two pieces at least {DrumSpec.MinPieceGap * 100f:F0} cm apart (closest: {worstPair} {worst * 100f:F2} cm)");
        }

        [Test]
        public void TheSnareAndTheRackTomsHaveRoomBetweenThem()
        {
            // The pairs that touched before the 2026-11-14 spacing (built: the small tom's shell 5.4 cm into the snare's far
            // hoop, the big tom's 2.2 cm, the toms' lugs touching, the pad on the snare's bottom hoop), named so a regression
            // says which.
            using var k = new KitFixture();
            (int, int)[] pairs = { (DrumSpec.SnareId, DrumSpec.Tom1Id), (DrumSpec.SnareId, DrumSpec.Tom2Id), (DrumSpec.Tom1Id, DrumSpec.Tom2Id), (DrumSpec.SnareId, DrumSpec.KickPadId) };
            foreach (var (a, b) in pairs)
            {
                DrumPiece pa = k.Piece(a), pb = k.Piece(b);
                float gap = Mathf.Min(Clearance(Surface(pa), pb), Clearance(Surface(pb), pa));
                Assert.That(gap, Is.GreaterThanOrEqualTo(DrumSpec.MinPieceGap), $"{Name(pa)} and {Name(pb)}: {gap * 100f:F2} cm");
                Assert.That(gap, Is.LessThan(0.06f), $"{Name(pa)} and {Name(pb)} stay a close kit ({gap * 100f:F2} cm): a little space, not a gulf");
            }
        }

        static string Name(DrumPiece p) => p.Pad ? "pad" : p.Kind;

        /// <summary>Smallest signed distance from any of <paramref name="pts"/> to <paramref name="other"/>'s solid (negative inside).</summary>
        static float Clearance(List<Vector3> pts, DrumPiece other)
        {
            Matrix4x4 w2l = other.Group.worldToLocalMatrix;
            float best = float.PositiveInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = Solid(other, w2l.MultiplyPoint3x4(pts[i]));
                if (d < best) best = d;
            }
            return best;
        }

        // ---- the solid of a piece, in its group's frame (what DrumKitBuilder builds) ----

        static float Solid(DrumPiece p, Vector3 q)
        {
            float r = p.R;
            if (p.IsCymbal)
            {
                float d = Cylinder(q, r, -0.0015f, 0.0015f);                       // the plate's rim band
                d = Mathf.Min(d, Cylinder(q, r * 0.6f, -0.0015f, 0.006f));         // the bow
                d = Mathf.Min(d, Cylinder(q, 0.06f, -0.0015f, 0.016f));            // the bell
                d = Mathf.Min(d, Cylinder(q, DrumSpec.NutRadius, DrumSpec.NutY - DrumSpec.NutHeight * 0.5f, DrumSpec.NutY + DrumSpec.NutHeight * 0.5f));
                if (p.IsHat) d = Mathf.Min(d, Cylinder(q, r, DrumSpec.HatOpenY - 0.016f, DrumSpec.HatClosedY + 0.0015f));   // the lower plate, closed .. open
                return d;
            }
            if (p.Pad)
            {
                float d = Cylinder(q, r, -DrumSpec.PadBodyHeight, DrumSpec.HeadLift);
                return Mathf.Min(d, Torus(q, r, DrumSpec.PadRimTube, 0f));
            }
            float depth = p.Spec.Depth;
            float s = Cylinder(q, r, -depth - DrumSpec.HeadLift, DrumSpec.HeadLift);
            s = Mathf.Min(s, Torus(q, r + DrumSpec.RimOffset, DrumSpec.RimTube, 0f));
            s = Mathf.Min(s, Torus(q, r + DrumSpec.RimOffset, DrumSpec.RimTube, -depth));
            Vector3 h = DrumSpec.LugSize * 0.5f;
            for (int i = 0; i < DrumSpec.LugCount; i++)
            {
                float a = (float)i / DrumSpec.LugCount * Mathf.PI * 2f, rl = r + DrumSpec.LugOffset;
                Vector3 c = new Vector3(Mathf.Cos(a) * rl, -depth * 0.5f, Mathf.Sin(a) * rl);
                Vector3 l = Quaternion.Inverse(Quaternion.AngleAxis(-a * Mathf.Rad2Deg, Vector3.up)) * (q - c);
                s = Mathf.Min(s, Box(l, h));
            }
            return s;
        }

        static float Cylinder(Vector3 q, float r, float y0, float y1)
        {
            float dx = Mathf.Sqrt(q.x * q.x + q.z * q.z) - r, dy = Mathf.Abs(q.y - (y0 + y1) * 0.5f) - (y1 - y0) * 0.5f;
            return Mathf.Min(Mathf.Max(dx, dy), 0f) + new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
        }

        static float Torus(Vector3 q, float major, float tube, float y)
            => new Vector2(Mathf.Sqrt(q.x * q.x + q.z * q.z) - major, q.y - y).magnitude - tube;

        static float Box(Vector3 l, Vector3 h)
        {
            Vector3 d = new Vector3(Mathf.Abs(l.x) - h.x, Mathf.Abs(l.y) - h.y, Mathf.Abs(l.z) - h.z);
            return Mathf.Min(Mathf.Max(d.x, Mathf.Max(d.y, d.z)), 0f) + Vector3.Max(d, Vector3.zero).magnitude;
        }

        // ---- the surface of a piece as built: every mesh under its group, faces sampled to 1 cm ----

        static List<Vector3> Surface(DrumPiece p)
        {
            var pts = new List<Vector3>(8192);
            foreach (var mf in p.Group.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                Matrix4x4 l2w = mf.transform.localToWorldMatrix;
                Sample(m, l2w, pts);
                // The lower hi-hat as built hangs closed; the pedal opens it to HatOpenY: sample that pose too.
                if (p.IsHat && mf.transform == p.Lower)
                    Sample(m, p.Group.localToWorldMatrix * Matrix4x4.Translate(new Vector3(0f, DrumSpec.HatOpenY - DrumSpec.HatClosedY, 0f)) * p.Group.worldToLocalMatrix * l2w, pts);
            }
            Assert.That(pts.Count, Is.GreaterThan(100), Name(p) + " has meshes");
            return pts;
        }

        static void Sample(Mesh m, Matrix4x4 l2w, List<Vector3> into)
        {
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            for (int i = 0; i < v.Length; i++) into.Add(l2w.MultiplyPoint3x4(v[i]));
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = l2w.MultiplyPoint3x4(v[t[i]]), b = l2w.MultiplyPoint3x4(v[t[i + 1]]), c = l2w.MultiplyPoint3x4(v[t[i + 2]]);
                float longest = Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                int n = Mathf.CeilToInt(longest / Step);
                if (n < 2) continue;
                for (int u = 0; u <= n; u++)
                    for (int w = 0; u + w <= n; w++)
                    {
                        float fu = (float)u / n, fw = (float)w / n;
                        into.Add(a + (b - a) * fu + (c - a) * fw);
                    }
            }
        }
    }
}
