// Layout (sheet.md 9.2 SheetLayoutTests, the lab's T6, T7): the Ode to Joy vectors (x, y of all 30 notes to 0.01 mm,
// stretch, system heights); every song's bars per line, stretch and pages, every page's marks and noteheads equal the lab's
// page map for BOTH lesson files (book-current.json, book-proposed.json from vectors-for.mjs); two-page songs start on a
// left page; one mark per event; contents rows separate and at least 20 x 200 mm.
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Sheet.Engrave;
using NUnit.Framework;

namespace MusicRoom.Tests.Sheet
{
    public class SheetLayoutTests
    {
        [Test]
        public void OdeToJoyMatchesTheLabsLayoutVectors()
        {
            var v = SheetTestData.O(SheetTestData.O(SheetTestData.Json("test-vectors.json"))["odeLayout"]);
            var book = SheetTestData.Book("proposed");
            var song = book.Songs.First(s => s.Lesson.Id == "piano-ode");
            var stretch = SheetTestData.L(v["stretch"]);
            Assert.That(song.Stretch.Length, Is.EqualTo(stretch.Count));
            for (int i = 0; i < stretch.Count; i++) Assert.That(song.Stretch[i], Is.EqualTo(SheetTestData.D(stretch[i])).Within(1e-6), $"line {i} stretch");
            var es = book.Engrave(song.Lesson, song.Meta);
            var heights = SheetTestData.L(v["systemHeights"]);
            for (int i = 0; i < heights.Count; i++) Assert.That(es.Systems[i].Bottom - es.Systems[i].Top, Is.EqualTo(SheetTestData.D(heights[i])).Within(1e-3), $"system {i} height");
            var notes = SheetTestData.L(v["notes"]);
            Assert.That(notes.Count, Is.EqualTo(30));
            foreach (var o in notes)
            {
                var n = SheetTestData.O(o);
                int sys = SheetTestData.I(n["sys"]), k = SheetTestData.I(n["k"]);
                var mine = es.Systems[sys].Notes.First(x => x.K == k);
                Assert.That(mine.Midi, Is.EqualTo(SheetTestData.I(n["midi"])));
                Assert.That(mine.P, Is.EqualTo(SheetTestData.I(n["p"])));
                Assert.That(mine.X, Is.EqualTo(SheetTestData.D(n["x"])).Within(0.01), $"note {k} x (mm)");
                Assert.That(mine.Y, Is.EqualTo(SheetTestData.D(n["y"])).Within(0.01), $"note {k} y (mm)");
            }
            foreach (var s in es.Systems)
            {
                var xs = s.Notes.Where(n => n.K >= 0).Select(n => n.X).ToList();
                for (int i = 1; i < xs.Count; i++) Assert.That(xs[i], Is.GreaterThan(xs[i - 1] + 0.8 * SheetStyle.S), "notes never crowd");
            }
        }

        [Test]
        public void TheBookMatchesTheLabsPageMap([Values("current", "proposed")] string which)
        {
            var vec = SheetTestData.O(SheetTestData.O(SheetTestData.Json($"book-{which}.json"))["book"]);
            var book = SheetTestData.Book(which);
            var songs = SheetTestData.L(vec["songs"]);
            Assert.That(book.Songs.Count, Is.EqualTo(songs.Count), "songs");
            for (int i = 0; i < songs.Count; i++)
            {
                var s = SheetTestData.O(songs[i]);
                var mine = book.Songs[i];
                Assert.That(mine.Lesson.Id, Is.EqualTo((string)s["id"]), "lesson order");
                Assert.That(mine.Pages, Is.EqualTo(SheetTestData.L(s["pages"]).Select(SheetTestData.I).ToList()), $"{mine.Lesson.Id} pages");
                Assert.That(mine.LineBars, Is.EqualTo(SheetTestData.L(s["lines"]).Select(SheetTestData.I).ToArray()), $"{mine.Lesson.Id} bars per line");
                var st = SheetTestData.L(s["stretch"]);
                for (int j = 0; j < st.Count; j++) Assert.That(mine.Stretch[j], Is.EqualTo(SheetTestData.D(st[j])).Within(1e-6), $"{mine.Lesson.Id} line {j} stretch");
                Assert.That(mine.BarCount, Is.EqualTo(SheetTestData.I(s["bars"])), $"{mine.Lesson.Id} bars");
                if (mine.Pages.Count > 1) Assert.That(mine.First % 2, Is.EqualTo(0), $"{mine.Lesson.Id} starts on a left page");
            }
            var pages = SheetTestData.L(vec["pages"]);
            Assert.That(book.Pages.Count, Is.EqualTo(pages.Count), "pages");
            foreach (var po in pages)
            {
                var p = SheetTestData.O(po);
                var mine = book.Page(SheetTestData.I(p["number"]));
                Assert.That(mine, Is.Not.Null);
                Assert.That(mine.Filler, Is.EqualTo((bool)p["filler"]), $"page {mine.Number} filler");
                var marks = SheetTestData.L(p["marks"]);
                Assert.That(mine.Marks.Count, Is.EqualTo(marks.Count), $"page {mine.Number} marks");
                for (int i = 0; i < marks.Count; i++)
                {
                    var m = SheetTestData.O(marks[i]);
                    var r = SheetTestData.L(m["rect"]);
                    var mm = mine.Marks[i];
                    Assert.That(mm.Song, Is.EqualTo((string)m["song"]));
                    Assert.That(mm.K, Is.EqualTo(SheetTestData.I(m["k"])));
                    Assert.That(mm.X0, Is.EqualTo(SheetTestData.D(r[0])).Within(1e-3)); Assert.That(mm.Y0, Is.EqualTo(SheetTestData.D(r[1])).Within(1e-3));
                    Assert.That(mm.X1, Is.EqualTo(SheetTestData.D(r[2])).Within(1e-3)); Assert.That(mm.Y1, Is.EqualTo(SheetTestData.D(r[3])).Within(1e-3));
                    Assert.That(mm.X0 >= 0 && mm.X1 <= SheetStyle.PageW && mm.Y0 >= 0 && mm.Y1 <= SheetStyle.PageH, Is.True, $"page {mine.Number} mark {mm.K} inside the page");
                }
                var notes = SheetTestData.L(p["notes"]);
                Assert.That(mine.Notes.Count, Is.EqualTo(notes.Count), $"page {mine.Number} noteheads");
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = SheetTestData.O(notes[i]);
                    Assert.That(mine.Notes[i].Midi, Is.EqualTo(SheetTestData.I(n["midi"])));
                    Assert.That(mine.Notes[i].X, Is.EqualTo(SheetTestData.D(n["x"])).Within(1e-3));
                    Assert.That(mine.Notes[i].Y, Is.EqualTo(SheetTestData.D(n["y"])).Within(1e-3));
                }
                if (p["contents"] is List<object> rows)
                {
                    Assert.That(mine.Rows.Count, Is.EqualTo(rows.Count), $"page {mine.Number} contents rows");
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var r = SheetTestData.O(rows[i]);
                        Assert.That(mine.Rows[i].Id, Is.EqualTo((string)r["id"]));
                        Assert.That(mine.Rows[i].FirstPage, Is.EqualTo(SheetTestData.I(r["firstPage"])));
                        Assert.That(mine.Rows[i].Y0, Is.EqualTo(SheetTestData.D(r["y0"])).Within(1e-6));
                    }
                }
            }
        }

        [Test]
        public void OneMarkPerEventAndPokeableContentsRows([Values("current", "proposed")] string which)
        {
            var book = SheetTestData.Book(which);
            foreach (var s in book.Songs)
            {
                var ks = new HashSet<int>();
                foreach (int n in s.Pages) foreach (var m in book.Page(n).Marks) Assert.That(ks.Add(m.K), Is.True, $"{s.Lesson.Id} mark {m.K} once");
                Assert.That(ks.Count, Is.EqualTo(s.Lesson.Events.Count), $"{s.Lesson.Id}: a mark for every event");
                if (s.Pages.Count > 1) Assert.That(s.Pages.Count, Is.EqualTo(2), "no song longer than a spread");
            }
            var rows = book.Pages.SelectMany(p => p.Rows).ToList();
            Assert.That(rows.Count, Is.EqualTo(book.Songs.Count));
            for (int i = 1; i < rows.Count; i++) if (rows[i].Page == rows[i - 1].Page) Assert.That(rows[i].Y0, Is.GreaterThanOrEqualTo(rows[i - 1].Y1 - 1e-6));
            foreach (var r in rows)
            {
                Assert.That(r.Y1 - r.Y0, Is.GreaterThanOrEqualTo(20));
                Assert.That(r.X1 - r.X0, Is.GreaterThanOrEqualTo(200));
                Assert.That(r.Y1, Is.LessThanOrEqualTo(SheetStyle.PageH - SheetStyle.PageBottom + 1e-6), "rows stay inside the print area");
            }
            Assert.That(book.Pages[0].Contents && book.Pages[1].Contents, Is.True, "the contents on pages 2-3");
        }

        [Test]
        public void TwoStaffPiecesLayOutAsTheLabs()
        {
            var vec = SheetTestData.L(SheetTestData.O(SheetTestData.Json("book-proposed.json"))["extras"]);
            var extras = SheetTestData.Extras();
            foreach (var o in vec)
            {
                var x = SheetTestData.O(o);
                var (l, m) = extras.First(e => e.Item1.Id == (string)x["id"]);
                var book = new SheetBook(SheetTestData.Font, "t");
                book.Build(new List<(SheetLesson, SheetMeta)> { (l, m) }, contents: false);
                Assert.That(book.Songs[0].LineBars, Is.EqualTo(SheetTestData.L(x["lines"]).Select(SheetTestData.I).ToArray()), l.Id);
                var st = SheetTestData.L(x["stretch"]);
                for (int j = 0; j < st.Count; j++) Assert.That(book.Songs[0].Stretch[j], Is.EqualTo(SheetTestData.D(st[j])).Within(1e-6));
                var pages = SheetTestData.L(x["pages"]);
                Assert.That(book.Pages.Count, Is.EqualTo(pages.Count));
                for (int i = 0; i < pages.Count; i++)
                {
                    var p = SheetTestData.O(pages[i]);
                    Assert.That(book.Pages[i].Marks.Count, Is.EqualTo(SheetTestData.I(p["marks"])), $"{l.Id} page {i} marks");
                    Assert.That(book.Pages[i].Notes.Count, Is.EqualTo(SheetTestData.I(p["notes"])), $"{l.Id} page {i} noteheads");
                }
            }
        }
    }
}
