// Notation -> engraved systems (the Node sheet lab's engrave module): spacing, line breaks, beams, stems, ties, ledger
// lines, the letters under the notes, the title block. Every drawable is a Draw { Parts: flat mm contours, Alpha }; text
// is contours too (the project's own font), so a page is nothing but filled paths. Page millimetres, y DOWN.
using System;
using System.Collections.Generic;

namespace MusicRoom.Sheet.Engrave
{
    /// <summary>One filled path group (a union: nonzero over all its contours), at ink density Alpha.</summary>
    public sealed class Draw
    {
        public readonly List<double[]> Parts;
        public readonly double Alpha;
        public Draw(List<double[]> parts, double alpha = 1) { Parts = parts; Alpha = alpha; }
        public int PointCount { get { int n = 0; foreach (var c in Parts) n += c.Length >> 1; return n; } }
    }

    /// <summary>Sizes in mm (engrave.mjs STYLE, PAGE). Sized for the Quest 3S from the real seat (sheet.md section 1).</summary>
    public static class SheetStyle
    {
        public const double S = 4.9;                 // staff space: 6.8-7.3 display px from the home seat, 7.4-8.2 px fitted
        public const double LetterCap = 5.8;         // note letters (cap height)
        public const double LetterBold = 0.035;      // synthetic bold (em), like TMP's bold labels
        public const double TitleCap = 7.6, TitleBold = 0.03;
        public const double SubCap = 4.2, PageNumCap = 4.2;
        public const double CueAlpha = 0.5;
        public const double PageW = 320, PageH = 244, PageTop = 13, PageBottom = 18, PageOuter = 18, PageInner = 23;
        public const double ContentW = PageW - PageOuter - PageInner;
    }

    /// <summary>A current-note mark (engrave.mjs marks): the rect the page shader tints for event K, page mm.</summary>
    public sealed class Mark { public string Song; public int K; public double X0, X1, Y0, Y1; }

    /// <summary>A placed notehead (for tests and anchors).</summary>
    public sealed class NoteInfo
    {
        public string Song; public Staff Staff; public int K, Event, Midi, P; public double X, Y, StaffTopY; public NoteType Type;
    }

    public sealed class MCol { public double T; public readonly ScoreEl[] Els = new ScoreEl[2]; public double L, R; }
    public struct Spring { public double Ideal, Min; }
    public sealed class MBar
    {
        public ScoreBar Bar;
        public readonly List<MCol> Cols = new List<MCol>();
        public readonly List<Spring> Springs = new List<Spring>();
        public double Lead;
        public bool WholeRest;
        public int Index => Bar.Index;
    }

    public sealed class Line { public List<MBar> Bars; public bool First; public double St; public bool Ragged; }

    public sealed class EngravedSystem
    {
        public Line Line;
        public List<Draw> Draws;
        public double Top, Bottom, Width;
        public List<Mark> Marks;
        public List<NoteInfo> Notes;
        public double[] StaffTop = new double[2], LetterBase = new double[2];
    }

    public sealed class TitleBlock { public List<Draw> Draws; public double Height; }

    public sealed class SheetEngraver
    {
        const double EPS = 1e-6;
        static readonly double HH = SheetGlyphs.HeadHalfWidth;   // black / half notehead half width (S)
        const double WholeHH = 0.8 + 0.02;

        public readonly SheetFont Font;
        public SheetEngraver(SheetFont font) { Font = font; }

        public static double IdealSpace(double q) => 1.9 + 1.55 * JsMath.Log2(1 + 2 * q);

        // ------------------------------------------------------------------------------------ helpers
        public static Draw Place(Glyph g, double x, double y, double S, double alpha = 1)
        {
            var parts = new List<double[]>(g.Parts.Count);
            foreach (var c in g.Parts)
            {
                var o = new double[c.Length];
                for (int i = 0; i < c.Length; i += 2) { o[i] = x + c[i] * S; o[i + 1] = y - c[i + 1] * S; }
                parts.Add(o);
            }
            return new Draw(parts, alpha);
        }
        public static Draw RectMm(double x0, double y0, double x1, double y1, double alpha = 1)
            => new Draw(new List<double[]> { new[] { x0, y0, x1, y0, x1, y1, x0, y1 } }, alpha);

        public struct TextResult { public Draw D; public double W, X0; }
        public enum Align { Left, Center, Right }

        public TextResult TextMm(IList<TextRun> runs, double capMm, double x, double yBase, double bold = 0, Align align = Align.Left, double alpha = 1)
        {
            double em = capMm / Font.CapHeightEm;
            double w = Font.RunsWidth(runs, em, bold);
            double x0 = align == Align.Center ? x - w / 2 : align == Align.Right ? x - w : x;
            var parts = new List<double[]>();
            Font.LayoutRuns(runs, em, x0, yBase, bold, parts);
            return new TextResult { D = new Draw(parts, alpha), W = w, X0 = x0 };
        }

        public static TextRun[] Runs(string s) => new[] { new TextRun(s) };

        public double LabelWidthMm(string label) => Font.RunsWidth(Font.LabelRuns(label), SheetStyle.LetterCap / Font.CapHeightEm, SheetStyle.LetterBold);

        /// <summary>The printed label: each note's key label, low to high ("C3 E G" for a chord); none for rests, ties, cues.</summary>
        public static string ElementLabel(ScoreEl el)
        {
            if (el.Rest || el.K < 0 || el.Cue) return null;
            if (el.Notes.Count == 1) return SheetScore.KeyLabel(el.Notes[0].Midi);
            var parts = new string[el.Notes.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = SheetScore.KeyLabel(el.Notes[i].Midi);
            return string.Join(" ", parts);
        }
        List<TextRun> LabelRunsFor(string str)
        {
            var runs = new List<TextRun>();
            var ls = str.Split(' ');
            for (int i = 0; i < ls.Length; i++) { if (i > 0) runs.Add(new TextRun(" ")); runs.AddRange(Font.LabelRuns(ls[i])); }
            return runs;
        }

        static bool HasSecond(ScoreEl el) { for (int i = 1; i < el.Pos.Length; i++) if (el.Pos[i] - el.Pos[i - 1] == 1) return true; return false; }

        // ------------------------------------------------------------------------------------ bar measuring
        /// <summary>Per bar: columns with left/right extents (S) and spacing springs.</summary>
        public List<MBar> MeasureBars(Score score)
        {
            const double S = SheetStyle.S;
            var bars = new List<MBar>(score.Bars.Count);
            foreach (var b in score.Bars) bars.Add(new MBar { Bar = b });
            foreach (var st in score.Staves)
                foreach (var el in st.Els)
                {
                    var b = bars[el.Bar];
                    MCol col = null;
                    foreach (var c in b.Cols) if (Math.Abs(c.T - el.Start) < EPS) { col = c; break; }
                    if (col == null) { col = new MCol { T = el.Start }; b.Cols.Add(col); }
                    col.Els[(int)st.Clef] = el;
                }
            foreach (var b in bars)
            {
                JsMath.StableSort(b.Cols, (a, c) => JsMath.Cmp(a.T - c.T));
                foreach (var col in b.Cols)
                {
                    double L = 0, R = 0;
                    foreach (var st in score.Staves)
                    {
                        var el = col.Els[(int)st.Clef];
                        if (el == null) continue;
                        double l, r;
                        if (el.Rest) { l = r = el.WholeBar ? 0.7 : 0.6; }
                        else
                        {
                            double hh = el.Type == NoteType.Whole ? WholeHH : HH;
                            bool seconds = HasSecond(el);
                            bool anyAcc = false;
                            foreach (var a in el.Acc) if (a != Accidental.None) anyAcc = true;
                            l = hh + (anyAcc ? 0.3 + 1.1 : 0) + (seconds && el.Up != true ? 2 * hh : 0);
                            r = hh + (el.Dots > 0 ? 0.32 + 0.55 : 0) + (seconds && el.Up == true ? 2 * hh : 0);
                            if (SheetScore.Flags(el.Type) > 0 && el.Beam == 0 && el.Up == true) r = Math.Max(r, hh + 1.15);
                            string lab = ElementLabel(el);
                            if (lab != null) { double lw = LabelWidthMm(lab) / S; l = Math.Max(l, lw / 2 + 0.15); r = Math.Max(r, lw / 2 + 0.15); }
                        }
                        L = Math.Max(L, l); R = Math.Max(R, r);
                    }
                    col.L = L; col.R = R;
                }
                double qpb = score.QPerBeat;
                for (int i = 0; i < b.Cols.Count; i++)
                {
                    var c = b.Cols[i];
                    double tNext = i + 1 < b.Cols.Count ? b.Cols[i + 1].T : b.Bar.End;
                    double dq = (tNext - c.T) * qpb;
                    bool wholeBarRest = AllWholeBarRests(c);
                    if (i + 1 < b.Cols.Count) b.Springs.Add(new Spring { Ideal = IdealSpace(dq), Min = c.R + b.Cols[i + 1].L + 0.45 });
                    else b.Springs.Add(new Spring { Ideal = wholeBarRest ? IdealSpace(dq) * 0.5 : IdealSpace(dq) * 0.9, Min = c.R + 0.9 });
                }
                b.Lead = (b.Cols.Count > 0 ? b.Cols[0].L : 0) + 1.0;
                if (b.Cols.Count == 1 && AllWholeBarRests(b.Cols[0])) b.WholeRest = true;
            }
            return bars;
        }

        static bool AllWholeBarRests(MCol c)
        {
            foreach (var e in c.Els) if (e != null && !(e.Rest && e.WholeBar)) return false;
            return true;
        }

        static double BarWidth(MBar b, double st)
        {
            double w = b.Lead;
            foreach (var s in b.Springs) w += Math.Max(s.Min, s.Ideal * st);
            return w;
        }
        static double PrefixWidth(bool first) => 0.7 + 2.7 + 0.8 + (first ? 2.0 + 0.8 : 0) + 0.2;

        struct Stretch { public double St, Natural, MinW; }
        static Stretch SolveStretch(List<MBar> bars, bool first, double avail)
        {
            double pre = PrefixWidth(first);
            // pre + bars.reduce((a, b) => a + barWidth(b, st), 0): the sum first, then the prefix (same rounding as the lab)
            double Total(double st) { double sum = 0; foreach (var b in bars) sum = sum + BarWidth(b, st); return pre + sum; }
            double lo = 0, hi = 6;
            if (Total(hi) < avail) return new Stretch { St = hi, Natural = Total(1), MinW = Total(0) };
            for (int i = 0; i < 50; i++) { double m = (lo + hi) / 2; if (Total(m) < avail) lo = m; else hi = m; }
            return new Stretch { St = (lo + hi) / 2, Natural = Total(1), MinW = Total(0) };
        }

        // ------------------------------------------------------------------------------------ line breaking
        public const double MinStretch = 0.6, LineCost = 2.5;
        public static readonly int[] Candidates = { 4, 2, 6, 3, 8, 5, 1 };

        public List<Line> BreakLines(Score score, List<MBar> bars, double availMm)
        {
            double avail = availMm / SheetStyle.S;
            var full = new List<MBar>(); var pick = new List<MBar>();
            foreach (var b in bars) (b.Bar.Pickup ? pick : full).Add(b);
            List<Line> best = null;
            double bestCost = 0;
            for (int ci = 0; ci < Candidates.Length; ci++)
            {
                int k = Candidates[ci];
                var groups = new List<List<MBar>>();
                for (int i = 0; i < full.Count; i += k) groups.Add(full.GetRange(i, Math.Min(k, full.Count - i)));
                if (groups.Count > 1 && groups[groups.Count - 1].Count == 1 && k > 2)
                {
                    var last = groups[groups.Count - 1];
                    groups.RemoveAt(groups.Count - 1);
                    groups[groups.Count - 1].AddRange(last);
                }
                if (pick.Count > 0)
                {
                    if (groups.Count == 0) groups.Add(new List<MBar>());
                    var g0 = new List<MBar>(pick); g0.AddRange(groups[0]); groups[0] = g0;
                }
                double cost = 0;
                bool ok = true;
                var lines = new List<Line>();
                for (int li = 0; li < groups.Count; li++)
                {
                    var line = groups[li];
                    var r = SolveStretch(line, li == 0, avail);
                    bool hasPickup = false;
                    foreach (var b in line) if (b.Bar.Pickup) hasPickup = true;
                    if (r.MinW > avail || r.St < MinStretch - (hasPickup ? 0.07 : 0)) ok = false;
                    bool lastLine = li == groups.Count - 1;
                    double st = lastLine && r.St > 1.6 ? 1.6 : r.St;
                    cost += st < 1 ? ((1 - st) * 3.5) * ((1 - st) * 3.5) : ((st - 1) * 1.2) * ((st - 1) * 1.2);
                    cost += LineCost;
                    lines.Add(new Line { Bars = line, First = li == 0, St = st, Ragged = lastLine && r.St > 1.6 });
                }
                if (!ok) continue;
                cost += ci * 0.6;
                if (k < full.Count && full.Count % k != 0) cost += 2.5;
                if (best == null || cost < bestCost) { best = lines; bestCost = cost; }
            }
            if (best == null)
            {
                // the lab throws here; this engraver prints one bar a line, squeezed, so any lesson still shows
                best = new List<Line>();
                for (int i = 0; i < bars.Count; i++)
                {
                    var one = new List<MBar> { bars[i] };
                    var r = SolveStretch(one, i == 0, avail);
                    best.Add(new Line { Bars = one, First = i == 0, St = Math.Max(0.05, r.St) });
                }
                score.Warnings.Add(score.Id + ": no line breaking fits the page; one bar a line");
            }
            return best;
        }

        // ------------------------------------------------------------------------------------ system drawing
        /// <summary>One system with its treble staff top line at y = 0 and its left edge at x = 0 (mm).</summary>
        public EngravedSystem DrawSystem(Score score, Line line, double widthMm)
        {
            var ctx = new SystemCtx(this, score, line);
            return ctx.Draw(widthMm);
        }

        sealed class SystemCtx
        {
            const double S = SheetStyle.S;
            readonly SheetEngraver _e;
            readonly Score _score;
            readonly Line _line;
            readonly List<Draw> _draws = new List<Draw>();
            readonly List<Mark> _marks = new List<Mark>();
            readonly Dictionary<int, Mark> _markByK = new Dictionary<int, Mark>();
            readonly List<NoteInfo> _notes = new List<NoteInfo>();
            readonly double[] _staffTop = new double[2], _letterBase = new double[2];
            readonly Dictionary<ScoreEl, double> _headX = new Dictionary<ScoreEl, double>();

            public SystemCtx(SheetEngraver e, Score score, Line line) { _e = e; _score = score; _line = line; }

            double YOf(Staff sn, double p) => _staffTop[(int)sn] + 4 * S - (p * S) / 2;

            bool InLine(int barIndex) { foreach (var b in _line.Bars) if (b.Index == barIndex) return true; return false; }

            public EngravedSystem Draw(double widthMm)
            {
                bool two = _score.Staves.Count == 2;
                var lowest = new double[2]; var highest = new double[2];
                foreach (var st in _score.Staves)
                {
                    double lo = 0, hi = 8;
                    foreach (var b in _line.Bars)
                        foreach (var el in st.Els)
                        {
                            if (el.Bar != b.Index || el.Rest) continue;
                            foreach (int p in el.Pos) { lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
                            if (el.Up == false) lo = Math.Min(lo, el.MinPos - 7);
                            if (el.Up == true && el.Type != NoteType.Whole) hi = Math.Max(hi, el.MaxPos + 7);
                        }
                    lowest[(int)st.Clef] = lo; highest[(int)st.Clef] = hi;
                }
                double LetterGap(int sn)
                {
                    double lowInk = Math.Min(-1.2, lowest[sn] / 2 - 0.55) * S;
                    return Math.Max(2.3 * S + SheetStyle.LetterCap, -lowInk + 0.65 * S + SheetStyle.LetterCap);
                }
                _staffTop[0] = 0;
                _letterBase[0] = _staffTop[0] + 4 * S + LetterGap(0);
                if (two)
                {
                    double bassHigh = Math.Max(8, highest[1]);
                    _staffTop[1] = _letterBase[0] + 1.3 * S + Math.Max(0, (bassHigh - 8) / 2 + 0.6) * S;
                    _letterBase[1] = _staffTop[1] + 4 * S + LetterGap(1);
                }
                double st0 = _line.St;
                // horizontal positions
                double x = PrefixWidth(_line.First);
                var colX = new Dictionary<MCol, double>();
                var barLines = new List<(double x, MBar bar)>();
                foreach (var b in _line.Bars)
                {
                    x += b.Lead;
                    for (int i = 0; i < b.Cols.Count; i++)
                    {
                        var c = b.Cols[i];
                        var sp = b.Springs[i];
                        double cx = x;
                        if (b.WholeRest) cx = x + Math.Max(sp.Min, sp.Ideal * st0) / 2 - b.Lead / 2 + 0.5;
                        colX[c] = cx;
                        x += Math.Max(sp.Min, sp.Ideal * st0);
                    }
                    barLines.Add((x, b));
                }
                double sysWidth = x;
                // staff lines
                foreach (var st in _score.Staves)
                    for (int i = 0; i < 5; i++)
                    {
                        double y = _staffTop[(int)st.Clef] + i * S;
                        _draws.Add(RectMm(0, y - (SheetGlyphs.StaffLine * S) / 2, sysWidth * S, y + (SheetGlyphs.StaffLine * S) / 2));
                    }
                if (two)
                {
                    double yA = _staffTop[0], yB = _staffTop[1] + 4 * S;
                    _draws.Add(RectMm(0, yA - (SheetGlyphs.StaffLine * S) / 2, SheetGlyphs.Bar * S, yB + (SheetGlyphs.StaffLine * S) / 2));
                    _draws.Add(Place(SheetGlyphs.Brace((yB - yA) / S + 0.4), -0.35 * S, (yA + yB) / 2, S));
                }
                // clefs and time signature
                foreach (var st in _score.Staves)
                {
                    var sn = st.Clef;
                    if (sn == Staff.Treble) _draws.Add(Place(SheetGlyphs.TrebleClef(), 0.7 * S + 1.25 * S, YOf(sn, 2), S));
                    else _draws.Add(Place(SheetGlyphs.BassClef(), 0.7 * S + 0.95 * S, YOf(sn, 6), S));
                    if (_line.First)
                    {
                        double tx = (0.7 + 2.7 + 0.8 + 1.0) * S;
                        double em = (2.0 * S) / 0.716;
                        Digit(_score.MeterN.ToString(System.Globalization.CultureInfo.InvariantCulture), YOf(sn, 4), tx, em);
                        Digit(_score.MeterD.ToString(System.Globalization.CultureInfo.InvariantCulture), YOf(sn, 0), tx, em);
                    }
                }
                // bar lines (through both staves in a grand staff)
                foreach (var (bx, bar) in barLines)
                {
                    double yA = _staffTop[(int)_score.Staves[0].Clef] - (SheetGlyphs.StaffLine * S) / 2;
                    double yB = _staffTop[(int)_score.Staves[_score.Staves.Count - 1].Clef] + 4 * S + (SheetGlyphs.StaffLine * S) / 2;
                    if (bar.Bar.Final)
                    {
                        _draws.Add(RectMm((bx - SheetGlyphs.BarThick) * S, yA, bx * S, yB));
                        _draws.Add(RectMm((bx - SheetGlyphs.BarThick - SheetGlyphs.BarGap - SheetGlyphs.Bar) * S, yA, (bx - SheetGlyphs.BarThick - SheetGlyphs.BarGap) * S, yB));
                    }
                    else _draws.Add(RectMm((bx - SheetGlyphs.Bar) * S, yA, bx * S, yB));
                }
                // ---- notes and rests
                foreach (var b in _line.Bars)
                    foreach (var c in b.Cols)
                        foreach (var st in _score.Staves) { var el = c.Els[(int)st.Clef]; if (el != null) _headX[el] = colX[c]; }
                var beamGroups = new List<(Staff sn, List<ScoreEl> els, int id)>();
                foreach (var st in _score.Staves)
                {
                    var sn = st.Clef;
                    var els = new List<ScoreEl>();
                    foreach (var el in st.Els) if (InLine(el.Bar)) els.Add(el);
                    foreach (var el in els)
                    {
                        double hx = _headX[el];
                        double alpha = el.Cue ? SheetStyle.CueAlpha : 1;
                        if (el.Rest) { DrawRest(el, hx, sn, alpha); continue; }
                        double hh = el.Type == NoteType.Whole ? WholeHH : HH;
                        var side = HeadSides(el);
                        for (int i = 0; i < el.Pos.Length; i++)
                        {
                            int p = el.Pos[i];
                            var glyph = el.Type == NoteType.Whole ? SheetGlyphs.NoteheadWhole() : el.Type == NoteType.Half ? SheetGlyphs.NoteheadHalf() : SheetGlyphs.NoteheadBlack();
                            double dx = side[i] * (2 * hh - SheetGlyphs.Stem);
                            _draws.Add(Place(glyph, (hx + dx) * S, YOf(sn, p), S, alpha));
                            for (int l = -2; l >= p; l -= 2)
                                _draws.Add(RectMm((hx + dx - hh - SheetGlyphs.LedgerExt) * S, YOf(sn, l) - (SheetGlyphs.Ledger * S) / 2, (hx + dx + hh + SheetGlyphs.LedgerExt) * S, YOf(sn, l) + (SheetGlyphs.Ledger * S) / 2, alpha));
                            for (int l = 10; l <= p; l += 2)
                                _draws.Add(RectMm((hx + dx - hh - SheetGlyphs.LedgerExt) * S, YOf(sn, l) - (SheetGlyphs.Ledger * S) / 2, (hx + dx + hh + SheetGlyphs.LedgerExt) * S, YOf(sn, l) + (SheetGlyphs.Ledger * S) / 2, alpha));
                            if (el.Acc[i] != Accidental.None)
                            {
                                var g = el.Acc[i] == Accidental.Sharp ? SheetGlyphs.Sharp() : el.Acc[i] == Accidental.Flat ? SheetGlyphs.FlatSign() : SheetGlyphs.Natural();
                                _draws.Add(Place(g, (hx - hh - 0.3 - 0.58) * S, YOf(sn, p), S, alpha));
                            }
                            if (el.Dots > 0)
                            {
                                int dp = p % 2 == 0 ? p + 1 : p;
                                _draws.Add(Place(SheetGlyphs.Dot(), (hx + hh + 0.32 + SheetGlyphs.DotR) * S, YOf(sn, dp), S, alpha));
                            }
                            _notes.Add(new NoteInfo { Staff = sn, K = el.K, Event = el.Event, Midi = el.Notes[i].Midi, P = p, X = (hx + dx) * S, Y = YOf(sn, p), Type = el.Type });
                        }
                        if (el.Type != NoteType.Whole)
                        {
                            if (el.Beam != 0)
                            {
                                int gi = beamGroups.FindIndex(g => g.id == el.Beam);
                                if (gi < 0) { beamGroups.Add((sn, new List<ScoreEl>(), el.Beam)); gi = beamGroups.Count - 1; }
                                beamGroups[gi].els.Add(el);
                            }
                            else DrawStem(el, hx, sn, alpha, null);
                        }
                        string lab = ElementLabel(el);
                        if (lab != null)
                        {
                            var t = _e.TextMm(_e.LabelRunsFor(lab), SheetStyle.LetterCap, hx * S, _letterBase[(int)sn], SheetStyle.LetterBold, Align.Center, alpha);
                            _draws.Add(t.D);
                            var m = new Mark
                            {
                                K = el.K, X0 = Math.Min(t.X0, (hx - hh) * S) - 1.2, X1 = Math.Max(t.X0 + t.W, (hx + hh) * S) + 1.2,
                                Y0 = _staffTop[0] - 1.4 * S, Y1 = _letterBase[(int)sn] + 1.6,
                            };
                            if (_markByK.TryGetValue(el.K, out var prev)) { prev.X0 = Math.Min(prev.X0, m.X0); prev.X1 = Math.Max(prev.X1, m.X1); prev.Y1 = Math.Max(prev.Y1, m.Y1); }
                            else { _markByK[el.K] = m; _marks.Add(m); }
                        }
                    }
                    // ties
                    for (int i = 0; i < els.Count; i++)
                    {
                        var el = els[i];
                        if (!el.TieNext) continue;
                        var nx = i + 1 < els.Count ? els[i + 1] : null;
                        double x0 = _headX[el] + HH + 0.12;
                        double x1 = nx != null ? _headX[nx] - HH - 0.12 : sysWidth - 0.3;
                        foreach (int p in el.Pos)
                        {
                            bool below = el.Up != false;
                            double y = YOf(sn, p) + (below ? 0.62 : -0.62) * S;
                            var g = SheetGlyphs.Tie(0, x1 - x0, 0, below ? -0.62 : 0.62, 0.17);
                            _draws.Add(Place(g, x0 * S, y, S, el.Cue ? SheetStyle.CueAlpha : 1));
                        }
                    }
                    if (els.Count > 0 && els[0].TiePrev)
                    {
                        var el = els[0];
                        foreach (int p in el.Pos)
                        {
                            bool below = el.Up != false;
                            double x1 = _headX[el] - HH - 0.12, x0 = Math.Max(PrefixWidth(_line.First) - 0.4, x1 - 2.2);
                            _draws.Add(Place(SheetGlyphs.Tie(0, x1 - x0, 0, below ? -0.5 : 0.5, 0.15), x0 * S, YOf(sn, p) + (below ? 0.62 : -0.62) * S, S));
                        }
                    }
                }
                foreach (var g in beamGroups) DrawBeamGroup(g.els, g.sn);
                double top = double.PositiveInfinity, bottom = double.NegativeInfinity;
                foreach (var d in _draws) foreach (var c in d.Parts) for (int i = 1; i < c.Length; i += 2) { top = Math.Min(top, c[i]); bottom = Math.Max(bottom, c[i]); }
                return new EngravedSystem
                {
                    Line = _line, Draws = _draws, Top = top, Bottom = bottom, Width = sysWidth * S, Marks = _marks, Notes = _notes,
                    StaffTop = _staffTop, LetterBase = _letterBase,
                };
            }

            void Digit(string digit, double baseY, double tx, double em)
            {
                var runs = Runs(digit);
                double w = _e.Font.RunsWidth(runs, em, 0.045);
                var parts = new List<double[]>();
                _e.Font.LayoutRuns(runs, em, tx - w / 2, baseY, 0.045, parts);
                _draws.Add(new Draw(parts, 1));
            }

            int[] HeadSides(ScoreEl el)
            {
                var side = new int[el.Pos.Length];
                if (!HasSecond(el)) return side;
                if (el.Up != false) { for (int i = 1; i < el.Pos.Length; i++) if (el.Pos[i] - el.Pos[i - 1] == 1 && side[i - 1] == 0) side[i] = 1; }
                else { for (int i = el.Pos.Length - 2; i >= 0; i--) if (el.Pos[i + 1] - el.Pos[i] == 1 && side[i + 1] == 0) side[i] = -1; }
                return side;
            }

            double StemEndP(ScoreEl el)
            {
                int flags = SheetScore.Flags(el.Type);
                double extra = flags >= 2 ? (flags - 1) * 1.6 : 0;
                if (el.Up == true) return Math.Max(el.MaxPos + 7 + extra, 4);
                return Math.Min(el.MinPos - 7 - extra, 4);
            }
            static double StemX(ScoreEl el, double hx) => el.Up == true ? hx + HH - SheetGlyphs.Stem / 2 : hx - HH + SheetGlyphs.Stem / 2;

            void DrawStem(ScoreEl el, double hx, Staff sn, double alpha, double? endY)
            {
                double sx = StemX(el, hx);
                double from = el.Up == true ? YOf(sn, el.MinPos) - 0.16 * S : YOf(sn, el.MaxPos) + 0.16 * S;
                double to = endY ?? YOf(sn, StemEndP(el));
                _draws.Add(RectMm((sx - SheetGlyphs.Stem / 2) * S, Math.Min(from, to), (sx + SheetGlyphs.Stem / 2) * S, Math.Max(from, to), alpha));
                if (endY == null && SheetScore.Flags(el.Type) > 0)
                {
                    bool up = el.Up == true;
                    double fx = up ? (sx + SheetGlyphs.Stem / 2) * S : (sx - SheetGlyphs.Stem / 2) * S;
                    _draws.Add(Place(SheetGlyphs.Flag8(up, SheetScore.Flags(el.Type)), fx - (up ? 0.02 : -0.02) * S, to, S, alpha));
                }
            }

            void DrawRest(ScoreEl el, double hx, Staff sn, double alpha)
            {
                double yMid = YOf(sn, 4);
                Glyph g;
                if (el.WholeBar || el.Type == NoteType.Whole) g = SheetGlyphs.RestWhole();
                else if (el.Type == NoteType.Half) g = SheetGlyphs.RestHalf();
                else if (el.Type == NoteType.Quarter) g = SheetGlyphs.RestQuarter();
                else g = SheetGlyphs.RestEighth(SheetScore.Flags(el.Type));
                _draws.Add(Place(g, hx * S, yMid, S, alpha));
                if (el.Dots > 0) _draws.Add(Place(SheetGlyphs.Dot(), (hx + 0.85) * S, YOf(sn, 5), S, alpha));
            }

            void DrawBeamGroup(List<ScoreEl> els, Staff sn)
            {
                bool up = els[0].Up == true;
                var xs = new double[els.Count];
                var ext = new double[els.Count];
                int maxFlags = 0;
                for (int i = 0; i < els.Count; i++)
                {
                    xs[i] = StemX(els[i], _headX[els[i]]);
                    ext[i] = up ? els[i].MaxPos : els[i].MinPos;
                    maxFlags = Math.Max(maxFlags, SheetScore.Flags(els[i].Type));
                }
                double dp = ext[ext.Length - 1] - ext[0];
                double slantP = JsMath.Sign(dp) * Math.Min(Math.Abs(dp) * 0.5, Math.Abs(dp) <= 2 ? 1 : 2);
                double span = xs[xs.Length - 1] - xs[0];
                if (span == 0) span = 1;
                double slope = slantP / span;
                double extra = maxFlags >= 2 ? 1.4 : 0;
                double c;
                if (up)
                {
                    c = double.NegativeInfinity;
                    for (int i = 0; i < els.Count; i++) c = Math.Max(c, Math.Max(ext[i] + 7 + extra - slope * (xs[i] - xs[0]), 4 - slope * (xs[i] - xs[0])));
                }
                else
                {
                    c = double.PositiveInfinity;
                    for (int i = 0; i < els.Count; i++) c = Math.Min(c, Math.Min(ext[i] - 7 - extra - slope * (xs[i] - xs[0]), 4 - slope * (xs[i] - xs[0])));
                }
                double BeamP(double xx) => c + slope * (xx - xs[0]);
                double alpha = els[0].Cue ? SheetStyle.CueAlpha : 1;
                for (int i = 0; i < els.Count; i++) DrawStem(els[i], _headX[els[i]], sn, alpha, YOf(sn, BeamP(xs[i])));
                double bx0 = xs[0] - SheetGlyphs.Stem / 2, bx1 = xs[xs.Length - 1] + SheetGlyphs.Stem / 2;
                void BeamAt(int level, double xa, double xb)
                {
                    double off = (up ? 1 : -1) * level * (SheetGlyphs.Beam + SheetGlyphs.BeamGap) * 2;
                    double ya = YOf(sn, BeamP(xa) - off), yb = YOf(sn, BeamP(xb) - off);
                    double t = SheetGlyphs.Beam * S, dir = up ? 1 : -1;
                    _draws.Add(new Draw(new List<double[]> { new[] { xa * S, ya, xb * S, yb, xb * S, yb + dir * t, xa * S, ya + dir * t } }, alpha));
                }
                BeamAt(0, bx0, bx1);
                if (maxFlags >= 2)
                {
                    for (int i = 0; i < els.Count; i++)
                    {
                        if (SheetScore.Flags(els[i].Type) < 2) continue;
                        bool nextIs16 = i + 1 < els.Count && SheetScore.Flags(els[i + 1].Type) >= 2;
                        bool prevIs16 = i > 0 && SheetScore.Flags(els[i - 1].Type) >= 2;
                        if (nextIs16) BeamAt(1, xs[i] - SheetGlyphs.Stem / 2, xs[i + 1] + SheetGlyphs.Stem / 2);
                        else if (!prevIs16)
                        {
                            bool left = i == els.Count - 1 || (i > 0 && els[i - 1].Dots > 0);
                            if (left) BeamAt(1, xs[i] - 1.1, xs[i] + SheetGlyphs.Stem / 2); else BeamAt(1, xs[i] - SheetGlyphs.Stem / 2, xs[i] + 1.1);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------ title block
        public TitleBlock DrawTitle(string title, string credit, double bpm, int unit, double widthMm)
        {
            var draws = new List<Draw>();
            var t = TextMm(Runs(title), SheetStyle.TitleCap, widthMm / 2, SheetStyle.TitleCap, SheetStyle.TitleBold, Align.Center);
            draws.Add(t.D);
            double y2 = SheetStyle.TitleCap + 4.0 + SheetStyle.SubCap + 2.5;
            if (!string.IsNullOrEmpty(credit)) draws.Add(TextMm(Runs(credit), SheetStyle.SubCap, widthMm, y2, 0.02, Align.Right).D);
            double s = 0.62 * SheetStyle.S;
            const double nx = 1.0;
            draws.Add(Place(SheetGlyphs.NoteheadBlack(), nx + HH * s, y2 - 0.5 * s, s));
            double sx = nx + (2 * HH - SheetGlyphs.Stem / 2) * s;
            draws.Add(RectMm(sx - (SheetGlyphs.Stem / 2) * s, y2 - 0.5 * s - 3.3 * s, sx + (SheetGlyphs.Stem / 2) * s, y2 - 0.5 * s - 0.1 * s));
            if (unit == 8) draws.Add(Place(SheetGlyphs.Flag8(true, 1), sx + (SheetGlyphs.Stem / 2) * s, y2 - 0.5 * s - 3.3 * s, s));
            draws.Add(TextMm(Runs("= " + ((long)JsMath.Round(bpm)).ToString(System.Globalization.CultureInfo.InvariantCulture)), SheetStyle.SubCap, nx + 2 * HH * s + (unit == 8 ? 1.4 * s : 0) + 1.6, y2, 0.02).D);
            return new TitleBlock { Draws = draws, Height = y2 + 3.5 };
        }
    }
}
