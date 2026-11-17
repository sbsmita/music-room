// The page raster (sheet.md 9.2 SheetRasterTests, the lab's T3, T4, T8, DET): the Burst job against the lab's vectors.
// T8 glyph rasters (both clefs, noteheads, sharp, natural, quarter rest, flag, the labels "C4", "D#", "G"), T3 every
// pitch's head centred on its line or space with the right ledger count, T4 every notehead inked where the page map says,
// and every page of BOTH books: R8 SHA-1 equal to the lab's (the C# raster is bit-exact), ink sums, identical on every run and
// in Burst and managed code.
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Sheet.Engrave;
using MusicRoom.Sheet.Raster;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Sheet
{
    public class SheetRasterTests
    {
        const double PagePpm = 4.2;

        static RasterLane Raster(List<double[]> parts, int w, int h, out byte[] bytes, out double ink, bool burst = true)
        {
            var draws = new List<Draw> { new Draw(parts) };
            var lane = new RasterLane(w, h);
            lane.Run(PackedPage.Pack(draws, 1.0), mips: false, burst: burst);
            bytes = lane.CoverageBytes();
            ink = lane.InkSum();
            return lane;
        }

        [Test]
        public void GlyphRastersMatchTheLab()
        {
            var vg = SheetTestData.O(SheetTestData.O(SheetTestData.O(SheetTestData.Json("test-vectors.json"))["glyphRasters"])["glyphs"]);
            const double px = 12;
            var glyphs = new Dictionary<string, Glyph>
            {
                ["trebleClef"] = SheetGlyphs.TrebleClef(), ["bassClef"] = SheetGlyphs.BassClef(), ["noteheadBlack"] = SheetGlyphs.NoteheadBlack(),
                ["noteheadHalf"] = SheetGlyphs.NoteheadHalf(), ["sharp"] = SheetGlyphs.Sharp(), ["natural"] = SheetGlyphs.Natural(),
                ["restQuarter"] = SheetGlyphs.RestQuarter(), ["flag8"] = SheetGlyphs.Flag8(true, 1),
            };
            int exact = 0, total = 0;
            foreach (var kv in glyphs)
            {
                var g = kv.Value;
                int W = (int)Math.Ceiling((g.X1 - g.X0) * px) + 4, H = (int)Math.Ceiling((g.Y1 - g.Y0) * px) + 4;
                var parts = g.Parts.Select(c => { var o = new double[c.Length]; for (int i = 0; i < c.Length; i += 2) { o[i] = 2 + (c[i] - g.X0) * px; o[i + 1] = 2 + (g.Y1 - c[i + 1]) * px; } return o; }).ToList();
                using (Raster(parts, W, H, out var bytes, out double ink)) Compare(kv.Key, SheetTestData.O(vg[kv.Key]), W, H, bytes, ink, ref exact, ref total);
            }
            var font = SheetTestData.Font;
            double em = (SheetStyle.LetterCap / font.CapHeightEm) * 6;
            foreach (var lab in new[] { "C4", "D#", "G" })
            {
                var parts = new List<double[]>();
                double width = font.LayoutRuns(font.LabelRuns(lab), em, 2, Math.Ceiling(em) + 2, SheetStyle.LetterBold, parts);
                int W = (int)Math.Ceiling(width) + 4, H = (int)Math.Ceiling(em * 1.25) + 4;
                using (Raster(parts, W, H, out var bytes, out double ink)) Compare("label_" + lab, SheetTestData.O(vg["label_" + lab]), W, H, bytes, ink, ref exact, ref total);
            }
            Debug.Log($"[Sheet] glyph rasters: {exact} of {total} pixels identical to the lab");
        }

        static void Compare(string name, Dictionary<string, object> v, int W, int H, byte[] bytes, double ink, ref int exact, ref int total)
        {
            Assert.That(W, Is.EqualTo(SheetTestData.I(v["w"])), name + " width");
            Assert.That(H, Is.EqualTo(SheetTestData.I(v["h"])), name + " height");
            var want = Convert.FromBase64String((string)v["r8"]);
            Assert.That(bytes.Length, Is.EqualTo(want.Length));
            int ok = 0, same = 0;
            for (int i = 0; i < want.Length; i++) { int d = Math.Abs(want[i] - bytes[i]); if (d <= 3) ok++; if (d == 0) same++; }
            Assert.That(ok, Is.GreaterThanOrEqualTo((int)Math.Ceiling(0.995 * want.Length)), $"{name}: |delta| <= 3/255 on 99.5 % of pixels");
            double wantInk = SheetTestData.D(v["inkSum"]);
            Assert.That(ink, Is.EqualTo(wantInk).Within(0.005 * wantInk), $"{name} ink sum within 0.5 %");
            exact += same; total += want.Length;
        }

        [Test]
        public void EveryPitchSitsOnItsLineOrSpace()
        {
            var rows = SheetTestData.L(SheetTestData.O(SheetTestData.O(SheetTestData.Json("test-vectors.json"))["glyphPlacement"])["rows"]);
            var pitches = SheetTestData.L(SheetTestData.O(SheetTestData.Json("test-vectors.json"))["pitches"]);
            const double px = 12;
            for (int r = 0; r < pitches.Count; r++)
            {
                var row = SheetTestData.O(pitches[r]);
                int p = SheetTestData.I(row["p"]), ledgers = SheetTestData.I(row["ledgers"]);
                int W = (int)(6 * px), H = (int)(22 * px);
                double yBottom = 16 * px, y = yBottom - (p * px) / 2, x = 3 * px;
                var head = SheetGlyphs.NoteheadBlack().Parts.Select(c => { var o = new double[c.Length]; for (int i = 0; i < c.Length; i += 2) { o[i] = x + c[i] * px; o[i + 1] = y - c[i + 1] * px; } return o; }).ToList();
                var all = new List<Draw> { new Draw(head) };
                for (int l = -2; l >= p; l -= 2) all.Add(new Draw(new List<double[]> { Ledger(x, yBottom - (l * px) / 2, px) }));
                for (int l = 10; l <= p; l += 2) all.Add(new Draw(new List<double[]> { Ledger(x, yBottom - (l * px) / 2, px) }));
                double cy;
                using (var lane = new RasterLane(W, H))
                {
                    lane.Run(PackedPage.Pack(new List<Draw> { new Draw(head) }, 1.0), mips: false);
                    double sy = 0, sw = 0;
                    for (int j = (int)Math.Floor(y - 0.7 * px); j <= (int)Math.Ceiling(y + 0.7 * px); j++)
                        for (int i = JsMath.RoundInt(x - 0.3 * px); i <= JsMath.RoundInt(x + 0.3 * px); i++) { double c = lane.Coverage[j * W + i]; sy += (j + 0.5) * c; sw += c; }
                    cy = sy / sw;
                }
                Assert.That(Math.Abs(cy - y), Is.LessThan(0.06 * px), $"{row["note"]} head centre {cy:F2} vs {y:F2}");
                Assert.That(cy, Is.EqualTo(SheetTestData.D(SheetTestData.O(rows[r])["headCentroidPx"])).Within(1e-3), $"{row["note"]} centroid as the lab's");
                using (var lane = new RasterLane(W, H))
                {
                    lane.Run(PackedPage.Pack(all, 1.0), mips: false);
                    int col = JsMath.RoundInt(x + 0.85 * px), runs = 0;
                    bool inRun = false;
                    for (int j = 0; j < H; j++) { bool on = lane.Coverage[j * W + col] > 0.5f; if (on && !inRun) runs++; inRun = on; }
                    Assert.That(runs, Is.EqualTo(ledgers), $"{row["note"]} ledger lines");
                }
            }
        }

        static double[] Ledger(double x, double y, double px) => new[] { x - 1.0 * px, y - 0.09 * px, x + 1.0 * px, y - 0.09 * px, x + 1.0 * px, y + 0.09 * px, x - 1.0 * px, y + 0.09 * px };

        static int PageW => JsMath.RoundInt(SheetStyle.PageW * PagePpm);
        static int PageH => JsMath.RoundInt(SheetStyle.PageH * PagePpm);

        [Test]
        public void EveryPageIsBitIdenticalWithTheLab([Values("current", "proposed")] string which)
        {
            var book = SheetTestData.Book(which);
            var vec = SheetTestData.L(SheetTestData.O(SheetTestData.O(SheetTestData.Json($"book-{which}.json"))["book"])["pages"]);
            Assert.That(PageW, Is.EqualTo(1344)); Assert.That(PageH, Is.EqualTo(1025));
            int identical = 0;
            var fails = new List<string>();
            using (var lane = new RasterLane(PageW, PageH))
            {
                foreach (var o in vec)
                {
                    var v = SheetTestData.O(o);
                    int n = SheetTestData.I(v["number"]);
                    lane.Run(PackedPage.Pack(book.PageDraws(n), PagePpm, n), mips: false);
                    var bytes = lane.CoverageBytes();
                    double ink = lane.InkSum(), want = SheetTestData.D(v["inkSum"]);
                    Assert.That(ink, Is.EqualTo(want).Within(0.002 * want), $"page {n} ink within 0.2 %");
                    if (SheetTestData.Sha1(bytes) == (string)v["sha1"]) identical++; else fails.Add($"{n} ({ink:F2} vs {want:F2})");
                    // T4: every notehead inked where the page map says; hollow heads in a space are hollow
                    foreach (var note in book.Page(n).Notes)
                    {
                        int x = JsMath.RoundInt(note.X * PagePpm), y = JsMath.RoundInt(note.Y * PagePpm);
                        bool hollow = note.Type == NoteType.Half || note.Type == NoteType.Whole;
                        int rx = hollow ? JsMath.RoundInt(0.6 * SheetStyle.S * PagePpm) : 3, ry = hollow ? JsMath.RoundInt(0.35 * SheetStyle.S * PagePpm) : 1;
                        float best = 0;
                        for (int dy = -ry; dy <= ry; dy++) for (int dx = -rx; dx <= rx; dx++) best = Math.Max(best, lane.Coverage[(y + dy) * PageW + x + dx]);
                        Assert.That(best, Is.GreaterThanOrEqualTo(0.9f), $"page {n} {note.Song}#{note.K} head inked");
                        if (hollow && note.P % 2 != 0) Assert.That(lane.Coverage[y * PageW + x], Is.LessThan(0.5f), $"page {n} {note.Song}#{note.K} hollow");
                    }
                    foreach (var m in book.Page(n).Marks)
                    {
                        double sum = 0;
                        for (int j = JsMath.RoundInt((m.Y1 - 1.6 - SheetStyle.LetterCap) * PagePpm); j <= JsMath.RoundInt((m.Y1 - 1.6) * PagePpm); j++)
                            for (int i = JsMath.RoundInt(m.X0 * PagePpm); i <= JsMath.RoundInt(m.X1 * PagePpm); i++) sum += lane.Coverage[j * PageW + i];
                        Assert.That(sum, Is.GreaterThan(20), $"page {n}: the letter under {m.Song}#{m.K}");
                    }
                }
            }
            Debug.Log($"[Sheet] {which}: {identical} of {vec.Count} pages bit-identical with the lab" + (fails.Count > 0 ? "; differ: " + string.Join(", ", fails) : ""));
            Assert.That(identical, Is.EqualTo(vec.Count), "every page bit-identical with the lab: " + string.Join(", ", fails));
        }

        [Test]
        public void PagesComeOutIdenticalEveryRunInBurstAndManagedCode()
        {
            var book = SheetTestData.Book("proposed");
            using (var lane = new RasterLane(PageW, PageH))
            {
                foreach (int n in new[] { 2, 12, 26 })
                {
                    var packed = PackedPage.Pack(book.PageDraws(n), PagePpm, n);
                    lane.Run(packed, mips: true, burst: true);
                    string a = SheetTestData.Sha1(lane.CoverageBytes());
                    var pixA = lane.Pixels.ToArray();
                    lane.Run(PackedPage.Pack(book.PageDraws(n), PagePpm, n), mips: true, burst: true);
                    Assert.That(SheetTestData.Sha1(lane.CoverageBytes()), Is.EqualTo(a), $"page {n}: a second raster is identical");
                    Assert.That(lane.Pixels.ToArray(), Is.EqualTo(pixA), $"page {n}: and so is its mip chain");
                    lane.Run(packed, mips: true, burst: false);
                    Assert.That(SheetTestData.Sha1(lane.CoverageBytes()), Is.EqualTo(a), $"page {n}: managed code gives the same bytes as Burst");
                    Assert.That(lane.Pixels.ToArray(), Is.EqualTo(pixA), $"page {n}: mips too");
                }
            }
        }

        [Test]
        public void MipChainIsTheBoxFilteredCoverage()
        {
            const int W = 1024, H = 781;
            Assert.That(RasterLane.MipChainBytes(W, H), Is.EqualTo(1065855), "1.07 MB a slice: 4.26 MB for 4");
            Assert.That(RasterLane.MipCount(W, H), Is.EqualTo(11));
            var book = SheetTestData.Book("proposed");
            using (var lane = new RasterLane(W, H))
            {
                lane.Run(PackedPage.Pack(book.PageDraws(4), 3.2, 4), mips: true);
                var pix = lane.Pixels;
                int o1 = RasterLane.MipOffset(W, H, 1);
                for (int y = 100; y < 110; y++)
                    for (int x = 100; x < 110; x++)
                    {
                        double s = lane.Coverage[(2 * y) * W + 2 * x] + lane.Coverage[(2 * y) * W + 2 * x + 1] + lane.Coverage[(2 * y + 1) * W + 2 * x] + lane.Coverage[(2 * y + 1) * W + 2 * x + 1];
                        Assert.That(pix[o1 + y * (W / 2) + x], Is.EqualTo((byte)Math.Floor((float)(s * 0.25) * 255.0 + 0.5)));
                    }
                Assert.That(pix[RasterLane.MipChainBytes(W, H) - 1], Is.LessThan(64), "the 1 x 1 mip is the page's mean ink");
            }
        }
    }
}
