// The book's geometry and meshes (sheet.md 1.1, 2.1, 5.3, 7.3): the rest pose is the piano's own music rest (PianoBody:
// the 5 mm board's front face, bottom centre, leaning back 0.3 rad); the pages' bottom edge 10.3 mm up the rest face; the
// arc-length mapping and its inverse; the leaf lies on the right page at theta 0 and on the left at pi, upright half way;
// the paper mesh's vertex budget and the case's; the case and paper are one mesh each (2 draws); every glyph of the book
// also renders as page images (two-staff pieces too) for a look without a GPU: Builds/Shots/sheet/two-staff-*.png.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Piano;
using MusicRoom.Sheet;
using MusicRoom.Sheet.Engrave;
using MusicRoom.Sheet.Raster;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Sheet
{
    public class SheetBookViewTests
    {
        [Test]
        public void TheRestPoseIsThePianosMusicRest()
        {
            // the platform's hook (IPianoApi.MusicRestLocal / MusicRestLedge = PianoMusicRest) and the book's defaults agree
            Assert.That(Vector3.Distance(BookGeometry.DefaultRestPosition, PianoMusicRest.Local.position), Is.LessThan(1e-6f), "(0, 0.03974, 0.23761)");
            Assert.That(Quaternion.Angle(BookGeometry.DefaultRestRotation, PianoMusicRest.Local.rotation), Is.LessThan(0.01f));
            Assert.That(BookGeometry.DefaultLedge, Is.EqualTo(PianoMusicRest.Ledge).Within(1e-6f), "6.55 mm");
            Vector3 up = BookGeometry.DefaultRestRotation * Vector3.up, fwd = BookGeometry.DefaultRestRotation * Vector3.forward;
            Assert.That(Vector3.Distance(up, new Vector3(0f, Mathf.Cos(0.3f), Mathf.Sin(0.3f))), Is.LessThan(1e-5f), "up along the face, leaning back 0.3 rad");
            Assert.That(fwd.z, Is.GreaterThan(0.9f), "forward: away from the player (piano +z is back)");
            var mesh = PianoBody.MusicRest().ToMesh("rest");
            var b = mesh.bounds;
            Assert.That(b.min.y, Is.LessThan(BookGeometry.DefaultRestPosition.y + 0.001f));
            Assert.That(b.max.z, Is.GreaterThan(BookGeometry.DefaultRestPosition.z));
            float sBottom = BookGeometry.DefaultLedge + BookGeometry.Square;
            Assert.That(sBottom * 1000f, Is.EqualTo(9.55f).Within(0.05f), "the pages' bottom edge 9.55 mm up the face (ledge 6.55 + square 3)");
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ArcLengthAndTheGutterCurl()
        {
            Assert.That(BookGeometry.Lift(0f), Is.EqualTo(0.0045f).Within(1e-6f), "cover + half the block at the spine");
            Assert.That(BookGeometry.Lift(0.32f), Is.EqualTo(0.0025f + 0.002f + 0.009f - 0.004f).Within(1e-4f));
            Assert.That(BookGeometry.WFlat, Is.InRange(0.317f, 0.3195f), "320 mm of paper reach about 318.5 mm across the curl");
            for (float s = 0f; s <= 0.32f; s += 0.01f) Assert.That(BookGeometry.ArcAt(BookGeometry.FlatReach(s)), Is.EqualTo(s).Within(2e-6f));
            Assert.That(BookGeometry.ColumnArc(0), Is.EqualTo(0f));
            Assert.That(BookGeometry.ColumnArc(BookGeometry.Columns - 1), Is.EqualTo(BookGeometry.PageW).Within(1e-6f));
            Assert.That(BookGeometry.ColumnArc(1) * 1000f, Is.LessThan(1.5f), "dense near the spine");
        }

        [Test]
        public void TheLeafLiesOnTheRightPageThenStandsThenLiesOnTheLeft()
        {
            float d = BookGeometry.FlatReach(0.2f);
            Vector3 right = BookGeometry.LeafPoint(d, BookGeometry.Phi(BookGeometry.Theta(0f, true), 0.2f, true), 0.1f, 0f);
            Assert.That(Vector3.Distance(right, new Vector3(d, 0.1f, -BookGeometry.Lift(d))), Is.LessThan(1e-6f));
            Vector3 left = BookGeometry.LeafPoint(d, BookGeometry.Phi(BookGeometry.Theta(1f, true), 0.2f, true), 0.1f, 0f);
            Assert.That(Vector3.Distance(left, new Vector3(-d, 0.1f, -BookGeometry.Lift(d))), Is.LessThan(1e-5f));
            Vector3 mid = BookGeometry.LeafPoint(d, BookGeometry.Phi(Mathf.PI / 2f, 0.2f, true), 0.1f, 0f);
            Assert.That(-mid.z, Is.GreaterThan(0.1f), "half way it stands out toward the player");
            Assert.That(BookGeometry.Phi(Mathf.PI / 2f, 0.32f, true), Is.LessThan(BookGeometry.Phi(Mathf.PI / 2f, 0.02f, true)), "the free edge trails");
            Assert.That(BookGeometry.Theta(0.5f, false), Is.EqualTo(Mathf.PI / 2f).Within(1e-5f));
            var turn = new LeafTurn();
            turn.Begin(4, 6, TurnKind.Manual, 1);
            Assert.That(turn.LeafSeconds, Is.EqualTo(LeafTurn.ManualSeconds));
            Assert.That(turn.CornerLed, Is.True);
            turn.Begin(4, 2, TurnKind.Auto, 1);
            Assert.That(turn.LeafSeconds, Is.EqualTo(LeafTurn.AutoSeconds));
            Assert.That(turn.Forward, Is.False);
            turn.Begin(2, 12, TurnKind.Auto, 5);
            Assert.That(turn.Kind, Is.EqualTo(TurnKind.Riffle));
            Assert.That(turn.LeafCount, Is.EqualTo(3));
            Assert.That(turn.Duration, Is.EqualTo(2 * LeafTurn.RiffleStagger + LeafTurn.RiffleSeconds).Within(1e-6f));
            var roles = new int[8];
            turn.Faces(roles);
            Assert.That(roles[0], Is.EqualTo(2), "the old left page stays until the last leaf lands");
            Assert.That(roles[1], Is.EqualTo(13), "the new right page shows at once under the leaves");
            Assert.That(roles[2], Is.EqualTo(3), "the first leaf's front is the old right page");
            Assert.That(roles[3], Is.EqualTo(12), "its back the new left page");
        }

        [Test]
        public void TwoMeshesWithinTheirVertexBudget()
        {
            var paper = BookMesh.BuildPaper(0.0103f, out var leaf);
            try
            {
                Assert.That(paper.subMeshCount, Is.EqualTo(1), "one draw for both pages and the leaves");
                Assert.That(paper.vertexCount, Is.EqualTo(8 * BookGeometry.Columns * BookGeometry.Rows));
                Assert.That(paper.GetIndexCount(0) / 3, Is.LessThanOrEqualTo(4200));
                var cs = BookMesh.BuildCase(BookGeometry.DefaultLedge);
                Assert.That(cs.subMeshCount, Is.EqualTo(1), "one draw for the boards, page block, ribbon and lamp");
                Assert.That(cs.triangles.Length / 3, Is.LessThan(2500));
                Debug.Log($"[Sheet] meshes: paper {paper.vertexCount} vertices / {paper.GetIndexCount(0) / 3} triangles, case {cs.vertexCount} / {cs.triangles.Length / 3}");
                UnityEngine.Object.DestroyImmediate(cs);
            }
            finally { leaf.Dispose(); UnityEngine.Object.DestroyImmediate(paper); }
        }

        static string ShotsDir()
        {
            string root = Environment.GetEnvironmentVariable("MUSICROOM_SHOTS_ROOT");
            string d = !string.IsNullOrEmpty(root) ? Path.Combine(root, "sheet") : Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            if (string.IsNullOrEmpty(d)) d = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Shots", "sheet");
            try { Directory.CreateDirectory(d); } catch (Exception) { d = Path.Combine(Application.temporaryCachePath, "sheet"); Directory.CreateDirectory(d); }
            return d;
        }

        /// <summary>Paper and ink as the shader mixes them (no lighting): a flat look at the raster itself.</summary>
        static void WritePage(string file, RasterLane lane)
        {
            var tex = new Texture2D(lane.W, lane.H, TextureFormat.RGB24, false);
            var px = new Color32[lane.W * lane.H];
            Color paper = new Color32(0xf3, 0xea, 0xd5, 255), ink = new Color32(0x2a, 0x25, 0x21, 255);
            for (int y = 0; y < lane.H; y++)
                for (int x = 0; x < lane.W; x++)
                {
                    float c = lane.Coverage[y * lane.W + x];
                    px[(lane.H - 1 - y) * lane.W + x] = Color.Lerp(paper.linear, ink.linear, c).gamma;
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        [Test]
        public void TwoStaffPagesForTwoHandPieces()
        {
            string dir = ShotsDir();
            int W = JsMath.RoundInt(SheetStyle.PageW * 4.2), H = JsMath.RoundInt(SheetStyle.PageH * 4.2);
            using (var lane = new RasterLane(W, H))
            {
                foreach (var (l, m) in SheetTestData.Extras())
                {
                    var book = new SheetBook(SheetTestData.Font, "t");
                    book.Build(new List<(SheetLesson, SheetMeta)> { (l, m) }, contents: false);
                    var page = book.Pages.Find(p => p.Song != null);
                    Assert.That(page.Notes.Exists(n => n.Staff == Staff.Bass), Is.True, $"{l.Id}: notes on the bass staff");
                    Assert.That(page.Notes.Exists(n => n.Staff == Staff.Treble), Is.True, $"{l.Id}: notes on the treble staff");
                    lane.Run(PackedPage.Pack(book.PageDraws(page.Number), 4.2, page.Number), mips: false);
                    WritePage(Path.Combine(dir, $"two-staff-{l.Id}.png"), lane);
                }
                // and one song page of each lesson file, flat, for a side-by-side look with the lab's PNGs
                foreach (var which in new[] { "current", "proposed" })
                {
                    var book = SheetTestData.Book(which);
                    var elise = book.Song("piano-elise");
                    lane.Run(PackedPage.Pack(book.PageDraws(elise.First), 4.2, elise.First), mips: false);
                    WritePage(Path.Combine(dir, $"page-{which}-elise.png"), lane);
                }
            }
        }
    }
}
