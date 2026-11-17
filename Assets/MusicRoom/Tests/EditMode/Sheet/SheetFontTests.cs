// The book's text face (sheet.md 3.4, 9.2 SheetFontTests): Resources/sheet-font.bytes is generated from the project's own
// LiberationSans.ttf (TextMesh Pro's face) by SheetFontTable.Build, and regenerating it from the TTF gives the shipped
// bytes exactly; its outlines equal the lab's (export-font.mjs, float32) within float precision; every character the book
// prints from either lesson file is present; the font's OFL licence ships beside the table.
// SHEET_WRITE=1 Tools/unity.sh test-edit rewrites the table (like the lab's tests.mjs --write).
using System;
using System.IO;
using System.Text;
using MusicRoom.Sheet.Engrave;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Sheet
{
    public class SheetFontTests
    {
        static string TablePath => Path.Combine(SheetTestData.SheetDir, "Resources/sheet-font.bytes");
        static string TtfPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), SheetFontTable.TtfAssetPath);

        /// <summary>Every string the book prints from its own data (titles, credits, notes) for the extra charset.</summary>
        static string BookStrings()
        {
            var sb = new StringBuilder();
            var f = SheetTestData.BookFile;
            sb.Append(f.book.title).Append(f.book.subtitle).Append("Contents Notes notes · = 0123456789.");
            foreach (var m in f.lessons) sb.Append(m.credit).Append(m.cueNote);
            foreach (var x in f.extraLessons) sb.Append(x.title);
            return sb.ToString();
        }

        static byte[] Generate() => SheetFontTable.Build(SheetTrueType.Load(TtfPath), SheetFontTable.Charset(BookStrings()));

        [Test]
        public void TableRegeneratedFromTheProjectTtfIsIdentical()
        {
            Assert.That(File.Exists(TtfPath), Is.True, "the project's TMP LiberationSans.ttf");
            var fresh = Generate();
            if (Environment.GetEnvironmentVariable("SHEET_WRITE") == "1")
            {
                File.WriteAllBytes(TablePath, fresh);
                Debug.Log($"[Sheet] wrote {TablePath} ({fresh.Length} bytes)");
            }
            Assert.That(File.Exists(TablePath), Is.True, "sheet-font.bytes ships (SHEET_WRITE=1 regenerates it)");
            var shipped = File.ReadAllBytes(TablePath);
            Assert.That(shipped.Length, Is.EqualTo(fresh.Length), "table size");
            Assert.That(SheetTestData.Sha1(shipped), Is.EqualTo(SheetTestData.Sha1(fresh)), "the shipped table is exactly what the TTF gives");
            var font = SheetFont.FromBytes(shipped);
            Debug.Log($"[Sheet] font table: {font.GlyphCount} glyphs, {font.KernCount} kerning pairs, {shipped.Length} bytes, cap {font.CapHeightEm}");
            Assert.That(font.CapHeightEm, Is.EqualTo(1409.0 / 2048.0), "LiberationSans cap height 1409 / 2048 em");
            Assert.That(shipped.Length, Is.LessThan(160 * 1024));
        }

        [Test]
        public void OutlinesMatchTheLabsExport()
        {
            // the lab's v1 table: 'SFNT' u32 1, f32 cap, u16 glyphs, u16 kerns; glyph: u32 cp, f32 adv, u16 n, (u16 pts, f32 x, f32 y ...); kern u32 u32 f32
            var lab = File.ReadAllBytes(Path.Combine(SheetTestData.RefDir, "sheet-font-lab-v1.bytes"));
            var font = SheetTestData.Font;
            var ttf = SheetTrueType.Load(TtfPath);
            using (var r = new BinaryReader(new MemoryStream(lab)))
            {
                Assert.That(Encoding.ASCII.GetString(r.ReadBytes(4)), Is.EqualTo("SFNT"));
                Assert.That(r.ReadUInt32(), Is.EqualTo(1u));
                Assert.That((double)r.ReadSingle(), Is.EqualTo(font.CapHeightEm).Within(1e-7));
                int glyphs = r.ReadUInt16(), kerns = r.ReadUInt16();
                int compared = 0;
                for (int g = 0; g < glyphs; g++)
                {
                    int cp = (int)r.ReadUInt32();
                    float adv = r.ReadSingle();
                    int nc = r.ReadUInt16();
                    Assert.That(font.Has((char)cp), Is.True, $"U+{cp:X4} '{(char)cp}' is in the table");
                    int gi = ttf.GlyphIndex(cp);
                    Assert.That((double)adv, Is.EqualTo(ttf.Advance(gi) / (double)ttf.UnitsPerEm).Within(1e-7), $"advance of '{(char)cp}'");
                    var outline = ttf.Outline(gi);
                    Assert.That(outline.Count, Is.EqualTo(nc), $"contours of '{(char)cp}'");
                    for (int c = 0; c < nc; c++)
                    {
                        int n = r.ReadUInt16();
                        var mine = ttf.Flatten(outline[c], SheetFont.FlattenTolerance);
                        Assert.That(mine.Count, Is.EqualTo(n), $"points of '{(char)cp}' contour {c}");
                        for (int i = 0; i < n; i++)
                        {
                            float x = r.ReadSingle(), y = r.ReadSingle();
                            Assert.That(x, Is.EqualTo((float)mine[i][0]), $"'{(char)cp}' x");
                            Assert.That(y, Is.EqualTo((float)mine[i][1]), $"'{(char)cp}' y");
                        }
                    }
                    compared++;
                }
                Assert.That(compared, Is.GreaterThanOrEqualTo(80), "the lab's 82 glyphs");
                Assert.That(kerns, Is.GreaterThan(50), "kerning pairs");
            }
        }

        [Test]
        public void EveryCharacterTheBookPrintsIsPresent([Values("current", "proposed")] string which)
        {
            var font = SheetTestData.Font;
            var book = SheetTestData.Book(which);
            var sb = new StringBuilder(book.Title).Append("Contents Notes notes · = 0123456789.");
            foreach (var s in book.Songs) sb.Append(s.Lesson.Title).Append(s.Meta.Credit).Append(s.Meta.CueNote);
            foreach (char c in sb.ToString()) Assert.That(font.Has(c), Is.True, $"'{c}' (U+{(int)c:X4}) in sheet-font.bytes");
            for (int n = book.FirstPage; n <= book.LastPage; n++) book.PageDraws(n);
            Assert.That(font.Missing, Is.Empty, "no character fell back to a space");
        }

        [Test]
        public void TheFontsLicenceShipsBesideTheTable()
        {
            string ofl = Path.Combine(SheetTestData.SheetDir, "Resources/LiberationSans - OFL.txt");
            Assert.That(File.Exists(ofl), Is.True);
            Assert.That(File.ReadAllText(ofl), Does.Contain("SIL OPEN FONT LICENSE"));
        }

        [Test]
        public void LabelsAreTheKeyLabels()
        {
            for (int midi = 21; midi <= 108; midi++) Assert.That(SheetScore.KeyLabel(midi), Is.EqualTo(MusicRoom.Piano.PianoSpec.LabelText(midi)), $"midi {midi}");
            var runs = SheetTestData.Font.LabelRuns("D#");
            Assert.That(runs.Count, Is.EqualTo(2));
            Assert.That(runs[1].Scale, Is.EqualTo(0.62));
            Assert.That(runs[1].Rise, Is.EqualTo(0.35 * 1409.0 / 2048.0).Within(1e-12));
            Assert.That(SheetTestData.Font.LabelRuns("C4")[1].Scale, Is.EqualTo(0.72));
        }
    }
}
