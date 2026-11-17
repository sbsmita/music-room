// The music book (the Node sheet lab's book module): lessons -> songs -> pages. Page 2-3 the contents, then the songs
// in lesson order (a song that needs two pages starts on a left page, a "Notes" page of blank staves filling in), the
// current-note mark rect of every event, the contents rows (poke targets), page numbers.
// The layout is computed once (Build); a page's draw list is rebuilt on demand (PageDraws) by engraving only that page's
// song again, so the book keeps layouts, not 30 000-point draw lists (sheet.md 7.2). Pure C#: any thread.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MusicRoom.Sheet.Engrave
{
    public sealed class ContentsRow
    {
        public string Id; public int Page, FirstPage; public double X0, X1, Y0, Y1;
    }

    public sealed class BookPage
    {
        public int Number;
        public bool Left => Number % 2 == 0;
        public double X0 => Left ? SheetStyle.PageOuter : SheetStyle.PageInner;
        public bool Filler, Contents;
        public string Song;            // the song printed here (null for contents / filler pages)
        public int SongPage;           // which of the song's pages (0 = its first)
        public readonly List<Mark> Marks = new List<Mark>();
        public readonly List<NoteInfo> Notes = new List<NoteInfo>();
        public readonly List<ContentsRow> Rows = new List<ContentsRow>();
    }

    public sealed class BookSong
    {
        public SheetLesson Lesson;
        public SheetMeta Meta;
        public int First;
        public readonly List<int> Pages = new List<int>();
        public int[] LineBars;          // bars per line
        public double[] Stretch;
        public int BarCount;
        internal SongPlan Plan;
        public int TargetCount;
    }

    internal sealed class SongPlan { public int Pages; public List<(int page, double h)> Slots; public double TitleH; }

    internal sealed class EngravedSong
    {
        public SheetLesson Lesson; public SheetMeta Meta; public Score Score; public List<EngravedSystem> Systems; public TitleBlock Title;
    }

    public sealed class SheetBook
    {
        public const double RowH = 21.5, ContentsTitleCap = 6.0, ContentsSubCap = 4.2, ContentsHeadCap = 7.6;
        public const int RowsFirstPage = 8;
        const double S = SheetStyle.S;
        static double ContentTop => SheetStyle.PageTop;
        static double ContentBottom => SheetStyle.PageH - SheetStyle.PageBottom;
        static double ContentW => SheetStyle.ContentW;

        public readonly SheetEngraver Engraver;
        public SheetFont Font => Engraver.Font;
        public readonly string Title;
        public readonly List<BookSong> Songs = new List<BookSong>();
        public readonly List<BookPage> Pages = new List<BookPage>();
        public readonly List<string> Warnings = new List<string>();
        readonly Dictionary<string, BookSong> _byId = new Dictionary<string, BookSong>();
        readonly Dictionary<int, BookPage> _byNumber = new Dictionary<int, BookPage>();
        public int FirstPage { get; private set; } = 2;
        public int LastPage => Pages.Count > 0 ? Pages[Pages.Count - 1].Number : FirstPage;
        public bool HasContents { get; private set; }

        public SheetBook(SheetFont font, string title) { Engraver = new SheetEngraver(font); Title = string.IsNullOrEmpty(title) ? "Piano Songs" : title; }

        public BookSong Song(string id) => id != null && _byId.TryGetValue(id, out var s) ? s : null;
        public BookPage Page(int number) => _byNumber.TryGetValue(number, out var p) ? p : null;

        // ------------------------------------------------------------------------------------ engraving
        internal EngravedSong Engrave(SheetLesson lesson, SheetMeta meta, bool record = false)
        {
            var score = SheetScore.Build(lesson, meta);
            var bars = Engraver.MeasureBars(score);
            var lines = Engraver.BreakLines(score, bars, ContentW);
            var systems = new List<EngravedSystem>(lines.Count);
            foreach (var line in lines) systems.Add(Engraver.DrawSystem(score, line, ContentW));
            var title = Engraver.DrawTitle(lesson.Title, meta.Credit, lesson.Bpm, score.Unit, ContentW);
            if (record) lock (Warnings) Warnings.AddRange(score.Warnings);
            return new EngravedSong { Lesson = lesson, Meta = meta, Score = score, Systems = systems, Title = title };
        }

        /// <summary>Lays the book out: songs in order, contents first (book.mjs paginate). Draw lists are not kept.</summary>
        public void Build(IList<(SheetLesson lesson, SheetMeta meta)> songs, bool contents = true, int firstPage = 2)
        {
            Songs.Clear(); Pages.Clear(); _byId.Clear(); _byNumber.Clear();
            FirstPage = firstPage;
            HasContents = contents;
            int num = firstPage;
            int contentsPages = contents ? ContentsPageCount(songs.Count) : 0;
            for (int i = 0; i < contentsPages; i++) NewPage(num++).Contents = true;
            foreach (var (lesson, meta) in songs)
            {
                var es = Engrave(lesson, meta, record: true);
                var plan = PlanSong(es);
                if (plan.Pages > 1 && num % 2 == 1) NewPage(num++).Filler = true;
                var bs = new BookSong
                {
                    Lesson = lesson, Meta = meta, First = num, Plan = plan, BarCount = es.Score.Bars.Count,
                    LineBars = new int[es.Systems.Count], Stretch = new double[es.Systems.Count],
                };
                for (int i = 0; i < es.Systems.Count; i++) { bs.LineBars[i] = es.Systems[i].Line.Bars.Count; bs.Stretch[i] = es.Systems[i].Line.St; }
                foreach (var e in lesson.Events) bs.TargetCount += e.Targets.Count;
                var ps = new List<BookPage>();
                for (int i = 0; i < plan.Pages; i++) { var p = NewPage(num++); p.Song = lesson.Id; p.SongPage = i; ps.Add(p); bs.Pages.Add(p.Number); }
                PlaceSong(es, plan, ps, null);
                Songs.Add(bs);
                _byId[lesson.Id] = bs;
            }
            if (contents) DrawContents(null, null);
        }

        BookPage NewPage(int number) { var p = new BookPage { Number = number }; Pages.Add(p); _byNumber[number] = p; return p; }

        /// <summary>Two pages hold 8 + 9 rows; more songs take more pairs.</summary>
        static int ContentsPageCount(int songs)
        {
            int perRight = RowsPerPage(false);
            int n = 2, cap = RowsFirstPage + perRight;
            while (cap < songs) { n += 2; cap += 2 * perRight; }
            return n;
        }
        static int RowsPerPage(bool first) => first ? RowsFirstPage : (int)Math.Floor((ContentBottom - (ContentTop + 2)) / RowH);

        SongPlan PlanSong(EngravedSong song)
        {
            double gapMin = 2.0 * S;
            double titleH = song.Title.Height + 2.0;
            double avail = ContentBottom - ContentTop;
            var hs = new double[song.Systems.Count];
            for (int i = 0; i < hs.Length; i++) hs[i] = song.Systems[i].Bottom - song.Systems[i].Top;
            int Fits(int i, bool first)
            {
                double y = first ? titleH : 0;
                int n = 0;
                while (i + n < hs.Length && y + hs[i + n] <= avail + 1e-6) { y += hs[i + n] + gapMin; n++; }
                return Math.Max(1, n);
            }
            var counts = new List<int>();
            for (int i = 0; i < hs.Length;) { int n = Fits(i, counts.Count == 0); counts.Add(n); i += n; }
            if (counts.Count == 2)
            {
                int n = hs.Length, cap0 = counts[0];
                int left = Math.Min(cap0, (int)Math.Ceiling(n / 2.0));
                if (n - left <= Fits(left, false)) { counts[0] = left; counts[1] = n - left; }
            }
            var slots = new List<(int, double)>();
            for (int page = 0; page < counts.Count; page++) for (int j = 0; j < counts[page]; j++) slots.Add((page, hs[slots.Count]));
            return new SongPlan { Pages = Math.Max(1, counts.Count), Slots = slots, TitleH = titleH };
        }

        /// <summary>book.mjs placeSong; with `drawsFor` (page number) it also collects that page's draws.</summary>
        void PlaceSong(EngravedSong song, SongPlan plan, List<BookPage> pages, List<Draw> drawsFor, int onlyPage = -1)
        {
            double avail = ContentBottom - ContentTop;
            int last = plan.Pages - 1;
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var p = pages[pi];
                bool collect = drawsFor != null && p.Number == onlyPage;
                bool layout = drawsFor == null;
                if (!collect && !layout) continue;
                double top0 = pi == 0 ? plan.TitleH : 0;
                double used = 0; int n = 0;
                for (int i = 0; i < plan.Slots.Count; i++) if (plan.Slots[i].page == pi) { used = used + plan.Slots[i].h; n++; }
                double gap = n > 1 ? (avail - top0 - used) / (n - 1) : 0;
                double gapCap = pi == last ? 5.0 * S : 9 * S;
                gap = Math.Max(2.0 * S, Math.Min(gap, gapCap));
                double y = ContentTop + top0;
                if (pi == 0 && collect) foreach (var d in song.Title.Draws) drawsFor.Add(Shift(d, p.X0, ContentTop));
                for (int i = 0; i < plan.Slots.Count; i++)
                {
                    if (plan.Slots[i].page != pi) continue;
                    var sys = song.Systems[i];
                    double dy = y - sys.Top;
                    if (collect) foreach (var d in sys.Draws) drawsFor.Add(Shift(d, p.X0, dy));
                    if (layout)
                    {
                        foreach (var m in sys.Marks) p.Marks.Add(new Mark { Song = song.Lesson.Id, K = m.K, X0 = m.X0 + p.X0, X1 = m.X1 + p.X0, Y0 = m.Y0 + dy, Y1 = m.Y1 + dy });
                        foreach (var nt in sys.Notes)
                            p.Notes.Add(new NoteInfo { Song = song.Lesson.Id, Staff = nt.Staff, K = nt.K, Event = nt.Event, Midi = nt.Midi, P = nt.P, X = nt.X + p.X0, Y = nt.Y + dy, StaffTopY = sys.StaffTop[(int)nt.Staff] + dy, Type = nt.Type });
                    }
                    y += plan.Slots[i].h + gap;
                }
                if (pi == 0 && collect && song.Meta.CueNote != null)
                    drawsFor.Add(TextAt(song.Meta.CueNote, SheetStyle.SubCap, p.X0, ContentBottom + 1, 0.0, SheetEngraver.Align.Left, 0.75));
            }
        }

        static Draw Shift(Draw d, double dx, double dy)
        {
            var parts = new List<double[]>(d.Parts.Count);
            foreach (var c in d.Parts)
            {
                var o = new double[c.Length];
                for (int i = 0; i < c.Length; i += 2) { o[i] = c[i] + dx; o[i + 1] = c[i + 1] + dy; }
                parts.Add(o);
            }
            return new Draw(parts, d.Alpha);
        }

        Draw TextAt(string s, double cap, double x, double yBase, double bold, SheetEngraver.Align align, double alpha = 1)
            => Engraver.TextMm(SheetEngraver.Runs(s), cap, x, yBase, bold, align, alpha).D;

        void DrawFiller(BookPage p, List<Draw> draws)
        {
            draws.Add(TextAt("Notes", SheetStyle.SubCap + 1, p.X0, ContentTop + 6, 0.02, SheetEngraver.Align.Left, 0.8));
            for (int k = 0; k < 6; k++)
            {
                double yTop = ContentTop + 18 + k * 29;
                for (int i = 0; i < 5; i++)
                    draws.Add(new Draw(new List<double[]> { new[] { p.X0, yTop + i * S - 0.33, p.X0 + ContentW, yTop + i * S - 0.33, p.X0 + ContentW, yTop + i * S + 0.33, p.X0, yTop + i * S + 0.33 } }, 0.55));
            }
        }

        /// <summary>book.mjs drawContents: rows on the contents pages (layout when draws == null, else that page's draws).</summary>
        void DrawContents(BookPage only, List<Draw> draws)
        {
            var cps = new List<BookPage>();
            foreach (var p in Pages) if (p.Contents) cps.Add(p);
            if (cps.Count == 0) return;
            var L = cps[0];
            bool D(BookPage p) => draws != null && p == only;
            if (D(L))
            {
                draws.Add(TextAt(Title, ContentsHeadCap, L.X0, ContentTop + ContentsHeadCap, 0.03, SheetEngraver.Align.Left));
                draws.Add(TextAt("Contents", ContentsSubCap + 0.6, L.X0 + ContentW, ContentTop + ContentsHeadCap, 0.015, SheetEngraver.Align.Right, 0.85));
                double ry = ContentTop + ContentsHeadCap + 4;
                draws.Add(new Draw(new List<double[]> { new[] { L.X0, ry - 0.25, L.X0 + ContentW, ry - 0.25, L.X0 + ContentW, ry + 0.25, L.X0, ry + 0.25 } }, 0.6));
            }
            if (draws == null) foreach (var p in cps) p.Rows.Clear();
            double top0L = ContentTop + ContentsHeadCap + 9, top0R = ContentTop + 2;
            int pageIndex = 0, row = 0;
            for (int i = 0; i < Songs.Count; i++)
            {
                int cap = RowsPerPage(pageIndex == 0);
                if (row >= cap) { pageIndex++; row = 0; }
                if (pageIndex >= cps.Count) break;
                var p = cps[pageIndex];
                var song = Songs[i];
                double y0 = (pageIndex == 0 ? top0L : top0R) + row * RowH;
                double bas = y0 + ContentsTitleCap + 3.0;
                if (D(p))
                {
                    draws.Add(TextAt((i + 1).ToString(CultureInfo.InvariantCulture) + ".", ContentsTitleCap, p.X0 + 9, bas, 0.02, SheetEngraver.Align.Right, 0.8));
                    draws.Add(TextAt(song.Lesson.Title, ContentsTitleCap, p.X0 + 13, bas, 0.03, SheetEngraver.Align.Left));
                    string first = song.First.ToString(CultureInfo.InvariantCulture);
                    draws.Add(TextAt(first, ContentsTitleCap, p.X0 + ContentW, bas, 0.02, SheetEngraver.Align.Right));
                    double em = ContentsTitleCap / Font.CapHeightEm;
                    double tw = Font.RunsWidth(SheetEngraver.Runs(song.Lesson.Title), em, 0.03);
                    double pw = Font.RunsWidth(SheetEngraver.Runs(first), em, 0.02);
                    for (double x = p.X0 + 13 + tw + 3; x < p.X0 + ContentW - pw - 3; x += 3.2)
                        draws.Add(new Draw(new List<double[]> { Circle(x, bas - 0.6, 0.42) }, 0.55));
                    draws.Add(TextAt($"{song.Meta.Credit}  ·  {song.TargetCount.ToString(CultureInfo.InvariantCulture)} notes", ContentsSubCap, p.X0 + 13, bas + 7.2, 0.0, SheetEngraver.Align.Left, 0.7));
                }
                if (draws == null) p.Rows.Add(new ContentsRow { Id = song.Lesson.Id, Page = p.Number, X0 = p.X0, X1 = p.X0 + ContentW, Y0 = y0, Y1 = y0 + RowH, FirstPage = song.First });
                row++;
            }
        }

        static double[] Circle(double cx, double cy, double r, int n = 12)
        {
            var c = new double[2 * n];
            for (int i = 0; i < n; i++) { double a = ((double)i / n) * 2 * Math.PI; c[2 * i] = cx + r * Math.Cos(a); c[2 * i + 1] = cy + r * Math.Sin(a); }
            return c;
        }

        // ------------------------------------------------------------------------------------ page draws (on demand)
        /// <summary>Everything printed on a page, in the lab's order (its song or contents or filler, then the page number).
        /// An unknown page number (the blank side of the last spread) gives an empty list.</summary>
        public List<Draw> PageDraws(int number)
        {
            var draws = new List<Draw>();
            var p = Page(number);
            if (p == null) return draws;
            if (p.Contents) DrawContents(p, draws);
            else if (p.Filler) DrawFiller(p, draws);
            else if (p.Song != null)
            {
                var bs = Song(p.Song);
                var es = Engrave(bs.Lesson, bs.Meta);
                var ps = new List<BookPage>();
                foreach (int n in bs.Pages) ps.Add(Page(n));
                PlaceSong(es, bs.Plan, ps, draws, number);
            }
            draws.Add(TextAt(number.ToString(CultureInfo.InvariantCulture), SheetStyle.PageNumCap, SheetStyle.PageW / 2, SheetStyle.PageH - 9, 0.01, SheetEngraver.Align.Center));
            return draws;
        }

        /// <summary>The mark of event k of a song (k already offset for a lesson shown on another song's page).</summary>
        public bool TryGetMark(string song, int k, out Mark mark, out int page)
        {
            mark = null; page = -1;
            var bs = Song(song);
            if (bs == null) return false;
            foreach (int n in bs.Pages)
            {
                var p = Page(n);
                foreach (var m in p.Marks) if (m.K == k) { mark = m; page = n; return true; }
            }
            return false;
        }

        /// <summary>The left page number of the spread a page belongs to (2-3, 4-5, ...).</summary>
        public static int SpreadOf(int page) => page % 2 == 0 ? page : page - 1;
    }
}
