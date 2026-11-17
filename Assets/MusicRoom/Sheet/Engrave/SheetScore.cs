// Lesson -> notation (the Node sheet lab's score module): spelling, staff positions, bars (meter + pickup), note values,
// ties, rests, beams, stems, accidentals. Pure data, no drawing. Units: lesson beats for time, quarters (q) for note
// values, half-spaces (p) for staff positions (0 = bottom line, 8 = top line).
// Unlike the lab (which throws), this code never fails on unusual lesson data: overlapping events merge or shorten, and a
// duration no note value can spell drops its remainder; each case is reported in SheetScore.Warnings.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MusicRoom.Sheet.Engrave
{
    public enum Staff : byte { Treble = 0, Bass = 1 }
    public enum NoteType : byte { Whole = 0, Half, Quarter, Eighth, Sixteenth }
    public enum Accidental : byte { None = 0, Sharp, Flat, Natural }

    /// <summary>The engraver's lesson input (the lab's lesson JSON): built from a Core.Lesson or from the book's extra pieces.</summary>
    public sealed class SheetLesson
    {
        public string Id, Title;
        public double Bpm, Beats;
        public readonly List<SheetEvent> Events = new List<SheetEvent>();
        public readonly List<SheetChord> Accompany = new List<SheetChord>();
    }
    public sealed class SheetEvent { public int K; public double Beat, Len; public readonly List<SheetTarget> Targets = new List<SheetTarget>(); }
    public sealed class SheetTarget { public int Midi = -1; public string Note; public char Hand; public double Len = double.NaN; }
    public sealed class SheetChord { public double Beat, Len; public int[] Midi; }

    /// <summary>Notation facts the lesson JSON does not carry (sheet-book.json `lessons`, the lab's lessons-meta.json).</summary>
    public sealed class SheetMeta
    {
        public int MeterN = 4, MeterD = 4, Unit = 4, Staves = 1;
        public double Pickup;
        public string Credit = "Traditional", CueNote, BookSong;
        public int EventOffset;
        public int SplitBelow = -1;
        public static SheetMeta Default => new SheetMeta();
    }

    public sealed class SpelledNote
    {
        public char Letter; public int Alter, Octave, Step, Midi;
    }

    public sealed class ScoreEl
    {
        public Staff Staff;
        public int Bar;
        public double Start, Q;
        public NoteType Type;
        public int Dots;
        public bool Rest;
        public List<SpelledNote> Notes;
        public int K = -1, Event = -1;
        public bool Cue, TieNext, TiePrev, WholeBar;
        public Accidental[] Acc = Array.Empty<Accidental>();
        public int[] Pos = Array.Empty<int>();
        public int Beam;          // 0 = none, else the group id (1-based, in order of appearance)
        public bool? Up;          // stem direction (null: rests, whole notes)
        public int MinPos { get { int m = int.MaxValue; foreach (int p in Pos) m = Math.Min(m, p); return m; } }
        public int MaxPos { get { int m = int.MinValue; foreach (int p in Pos) m = Math.Max(m, p); return m; } }
    }

    public sealed class ScoreBar { public int Index; public double Start, End; public bool Pickup, Final; }

    public sealed class ScoreStaff { public Staff Clef; public readonly List<ScoreEl> Els = new List<ScoreEl>(); }

    public sealed class Score
    {
        public string Id;
        public int MeterN, MeterD, Unit;
        public double QPerBeat, BarLen, Pickup, Total;
        public readonly List<ScoreBar> Bars = new List<ScoreBar>();
        /// <summary>In insertion order: treble, then bass (two-staff music only).</summary>
        public readonly List<ScoreStaff> Staves = new List<ScoreStaff>();
        public SheetLesson Lesson;
        public readonly List<List<ScoreEl>> BeamGroups = new List<List<ScoreEl>>();
        public readonly List<string> Warnings = new List<string>();
        public ScoreStaff StaffOf(Staff s) { foreach (var st in Staves) if (st.Clef == s) return st; return null; }
    }

    public static class SheetScore
    {
        public const string Steps = "CDEFGAB";
        public const int TrebleBottomStep = 4 * 7 + 2, BassBottomStep = 2 * 7 + 4;   // E4, G2 on the bottom lines
        static readonly int[] PC = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        const double EPS = 1e-6;
        static readonly Regex s_name = new Regex("^([A-G])(#{1,2}|b{1,2})?(-?[0-9]+)$", RegexOptions.CultureInvariant);

        public static int Flags(NoteType t) => t == NoteType.Eighth ? 1 : t == NoteType.Sixteenth ? 2 : 0;

        /// <summary>"D#5" / "Bb3" / "C4" -> letter, alter, octave, step, midi.</summary>
        public static SpelledNote Spell(string name)
        {
            var m = s_name.Match(name ?? "");
            if (!m.Success) throw new FormatException("bad note name " + name);
            string acc = m.Groups[2].Value;
            int alter = acc.Length == 0 ? 0 : acc[0] == '#' ? acc.Length : -acc.Length;
            int octave = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            char letter = m.Groups[1].Value[0];
            int li = Steps.IndexOf(letter);
            return new SpelledNote { Letter = letter, Alter = alter, Octave = octave, Step = octave * 7 + li, Midi = 12 * (octave + 1) + PC[li] + alter };
        }

        /// <summary>MIDI -> sharp spelling (the key labels' convention: C# D# F# G# A#).</summary>
        public static SpelledNote SpellMidi(int midi) => Spell(Names[midi % 12] + (FloorDiv(midi, 12) - 1));
        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

        public static int StaffPos(int step, Staff clef) => step - (clef == Staff.Treble ? TrebleBottomStep : BassBottomStep);

        /// <summary>PianoSpec.LabelText: C D E F G A B with the octave digit on every C; C# D# F# G# A# on the sharps.</summary>
        public static string KeyLabel(int midi)
        {
            int pc = midi % 12;
            return pc == 0 ? "C" + (FloorDiv(midi, 12) - 1) : Names[pc];
        }

        // note values in quarters, longest first
        struct Val { public double Q; public NoteType Type; public int Dots; public Val(double q, NoteType t, int d) { Q = q; Type = t; Dots = d; } }
        static readonly Val[] Values =
        {
            new Val(4, NoteType.Whole, 0), new Val(3, NoteType.Half, 1), new Val(2, NoteType.Half, 0), new Val(1.5, NoteType.Quarter, 1),
            new Val(1, NoteType.Quarter, 0), new Val(0.75, NoteType.Eighth, 1), new Val(0.5, NoteType.Eighth, 0),
            new Val(0.375, NoteType.Sixteenth, 1), new Val(0.25, NoteType.Sixteenth, 0),
        };

        static List<Val> SplitValue(double q, Score sc)
        {
            var o = new List<Val>();
            double r = q;
            while (r > EPS)
            {
                int i = 0;
                while (i < Values.Length && !(Values[i].Q <= r + EPS)) i++;
                if (i == Values.Length) { sc.Warnings.Add($"{sc.Id}: duration {q} q cannot be spelled; {r} q dropped"); break; }
                o.Add(Values[i]);
                r -= Values[i].Q;
            }
            return o;
        }

        /// <summary>Rests prefer beat-aligned values: split at beat boundaries first, then into values.</summary>
        static List<Val> SplitRest(double offsetBeats, double q, Score sc)
        {
            double beatQ = sc.MeterD == 8 ? 1.5 : 1;          // compound meters: the dotted-quarter beat
            var o = new List<Val>();
            double pos = offsetBeats * sc.QPerBeat, r = q;
            while (r > EPS)
            {
                double toBeat = beatQ - (pos % beatQ);
                double chunk = toBeat > EPS && toBeat < r - EPS ? toBeat : r;
                o.AddRange(SplitValue(chunk, sc));
                pos += chunk; r -= chunk;
            }
            return o;
        }

        sealed class Voice
        {
            public double Start, Len;
            public List<SpelledNote> Notes;
            public int K;
            public bool Cue, Rest;
        }

        /// <summary>Build the notation for one lesson (score.mjs buildScore).</summary>
        public static Score Build(SheetLesson lesson, SheetMeta meta)
        {
            int mn = meta.MeterN, md = meta.MeterD;
            int unit = meta.Unit != 0 ? meta.Unit : 4;
            double qPerBeat = 4.0 / unit;
            double barLen = ((double)mn / md) * unit;
            double pickup = meta.Pickup;
            bool twoStaves = meta.Staves == 2;
            var sc = new Score { Id = lesson.Id, MeterN = mn, MeterD = md, Unit = unit, QPerBeat = qPerBeat, BarLen = barLen, Pickup = pickup, Lesson = lesson };
            var voices = new[] { new List<Voice>(), new List<Voice>() };
            for (int idx = 0; idx < lesson.Events.Count; idx++)
            {
                var e = lesson.Events[idx];
                int k = e.K;
                var byStaff = new[] { new List<SpelledNote>(), new List<SpelledNote>() };
                var lenOf = new[] { e.Len, e.Len };
                foreach (var t in e.Targets)
                {
                    if (t.Midi < 0) continue;
                    SpelledNote sp;
                    try { sp = !string.IsNullOrEmpty(t.Note) ? Spell(t.Note) : SpellMidi(t.Midi); }
                    catch (FormatException) { sp = SpellMidi(t.Midi); }
                    sp = new SpelledNote { Letter = sp.Letter, Alter = sp.Alter, Octave = sp.Octave, Step = sp.Step, Midi = t.Midi };
                    bool bass = twoStaves && (t.Hand == 'L' || (t.Hand == '\0' && meta.SplitBelow >= 0 && t.Midi < meta.SplitBelow));
                    int s = bass ? 1 : 0;
                    byStaff[s].Add(sp);
                    if (!double.IsNaN(t.Len)) lenOf[s] = t.Len;
                }
                for (int s = 0; s < 2; s++)
                    if (byStaff[s].Count > 0)
                    {
                        JsMath.StableSort(byStaff[s], (a, b) => a.Step - b.Step);
                        voices[s].Add(new Voice { Start = e.Beat, Len = lenOf[s], Notes = byStaff[s], K = k });
                    }
            }
            foreach (var a in lesson.Accompany)
            {
                var notes = new List<SpelledNote>();
                foreach (int m in a.Midi) notes.Add(SpellMidi(m));
                JsMath.StableSort(notes, (x, y) => x.Step - y.Step);
                voices[1].Add(new Voice { Start = a.Beat, Len = a.Len, Notes = notes, K = -1, Cue = true });
            }
            double total = lesson.Beats > 0 ? lesson.Beats : 0;
            for (int s = 0; s < 2; s++) foreach (var v in voices[s]) total = Math.Max(total, v.Start + v.Len);
            // ---- bar boundaries
            var bounds = new List<double> { 0 };
            double t0 = pickup > 0 ? pickup : barLen;
            while (t0 < total - EPS) { bounds.Add(t0); t0 += barLen; }
            double end = t0;
            if (pickup > 0 && Math.Abs(end - total - pickup) < EPS) end = total;   // the final bar completes the pickup
            bounds.Add(end);
            for (int b = 0; b < bounds.Count - 1; b++)
                sc.Bars.Add(new ScoreBar { Index = b, Start = bounds[b], End = bounds[b + 1], Pickup = b == 0 && pickup > 0, Final = b == bounds.Count - 2 });
            sc.Total = end;
            // ---- per staff: fill rests, split at bars and into note values
            int staffCount = twoStaves ? 2 : 1;
            for (int s = 0; s < staffCount; s++)
            {
                var clef = (Staff)s;
                var v = new List<Voice>(voices[s]);
                JsMath.StableSort(v, (a, b) => JsMath.Cmp(a.Start - b.Start));
                var filled = new List<Voice>();
                double cur = 0;
                foreach (var e in v)
                {
                    if (e.Start < cur - EPS && filled.Count > 0)
                    {
                        // overlapping events (the lab throws): a chord when they start together, else the earlier note is cut short
                        var last = filled[filled.Count - 1];
                        if (!last.Rest && Math.Abs(last.Start - e.Start) < EPS)
                        {
                            var merged = new List<SpelledNote>(last.Notes);
                            merged.AddRange(e.Notes);
                            JsMath.StableSort(merged, (a, b) => a.Step - b.Step);
                            last.Notes = merged;
                            last.Len = Math.Max(last.Len, e.Len);
                            cur = last.Start + last.Len;
                            sc.Warnings.Add($"{lesson.Id} {clef}: events at beat {e.Start} merged into a chord");
                            continue;
                        }
                        last.Len = Math.Max(EPS, e.Start - last.Start);
                        sc.Warnings.Add($"{lesson.Id} {clef}: overlapping events at beat {e.Start}; the earlier one was shortened");
                        cur = e.Start;
                    }
                    if (e.Start > cur + EPS) filled.Add(new Voice { Start = cur, Len = e.Start - cur, Rest = true, K = -1 });
                    filled.Add(e);
                    cur = e.Start + e.Len;
                }
                if (cur < end - EPS) filled.Add(new Voice { Start = cur, Len = end - cur, Rest = true, K = -1 });
                var st = new ScoreStaff { Clef = clef };
                var els = st.Els;
                foreach (var e in filled)
                {
                    double a = e.Start, b = e.Start + e.Len;
                    var pieces = new List<(double s, double e, int bar)>();
                    foreach (var bar in sc.Bars)
                    {
                        if (bar.End <= a + EPS || bar.Start >= b - EPS) continue;
                        pieces.Add((Math.Max(a, bar.Start), Math.Min(b, bar.End), bar.Index));
                    }
                    for (int pi = 0; pi < pieces.Count; pi++)
                    {
                        var pc = pieces[pi];
                        double pos = pc.s;
                        var vals = e.Rest ? SplitRest(pc.s - sc.Bars[pc.bar].Start, (pc.e - pc.s) * qPerBeat, sc) : SplitValue((pc.e - pc.s) * qPerBeat, sc);
                        for (int vi = 0; vi < vals.Count; vi++)
                        {
                            var val = vals[vi];
                            bool isLast = pi == pieces.Count - 1 && vi == vals.Count - 1;
                            bool isFirst = pi == 0 && vi == 0;
                            els.Add(new ScoreEl
                            {
                                Staff = clef, Bar = pc.bar, Start = pos, Q = val.Q, Type = val.Type, Dots = val.Dots, Rest = e.Rest,
                                Notes = e.Rest ? new List<SpelledNote>() : e.Notes, K = e.Rest ? -1 : (isFirst ? e.K : -1), Event = e.Rest ? -1 : e.K,
                                Cue = e.Cue, TieNext = !e.Rest && !isLast, TiePrev = !e.Rest && !isFirst, WholeBar = false,
                            });
                            pos += val.Q / qPerBeat;
                        }
                    }
                }
                // a rest that fills a whole (non-pickup) bar is a whole-bar rest
                foreach (var el in els)
                {
                    if (!el.Rest) continue;
                    var bar = sc.Bars[el.Bar];
                    if (Math.Abs(el.Start - bar.Start) < EPS && Math.Abs(el.Q / qPerBeat - (bar.End - bar.Start)) < EPS && !bar.Pickup)
                    { el.WholeBar = true; el.Type = NoteType.Whole; el.Dots = 0; }
                }
                Accidentals(els, clef);
                Beams(els, sc);
                sc.Staves.Add(st);
            }
            return sc;
        }

        /// <summary>C major; an alteration lasts to the bar line; ties carry it.</summary>
        static void Accidentals(List<ScoreEl> els, Staff clef)
        {
            int bar = -1;
            var state = new Dictionary<int, int>();
            foreach (var el in els)
            {
                if (el.Bar != bar) { bar = el.Bar; state = new Dictionary<int, int>(); }
                el.Acc = new Accidental[el.Notes.Count];
                for (int i = 0; i < el.Notes.Count; i++)
                {
                    var n = el.Notes[i];
                    int cur = state.TryGetValue(n.Step, out int c) ? c : 0;
                    var show = Accidental.None;
                    if (n.Alter != cur && !el.TiePrev) show = n.Alter == 1 ? Accidental.Sharp : n.Alter == -1 ? Accidental.Flat : n.Alter == 0 ? Accidental.Natural : Accidental.None;
                    state[n.Step] = n.Alter;
                    el.Acc[i] = show;
                }
                el.Pos = new int[el.Notes.Count];
                for (int i = 0; i < el.Notes.Count; i++) el.Pos[i] = StaffPos(el.Notes[i].Step, clef);
            }
        }

        /// <summary>Beam groups by beat span (simple meters: the quarter; compound and 3/8: the dotted quarter), stems.</summary>
        static void Beams(List<ScoreEl> els, Score sc)
        {
            double span = sc.MeterD == 8 ? 1.5 : 1;   // in quarters
            var group = new List<ScoreEl>();
            int nextId = sc.BeamGroups.Count + 1;
            var groups = new List<List<ScoreEl>>();
            void Flush()
            {
                if (group.Count >= 2) { int id = nextId++; foreach (var g in group) g.Beam = id; groups.Add(group); }
                group = new List<ScoreEl>();
            }
            int? lastWin = null;
            int lastBar = -1;
            foreach (var el in els)
            {
                bool flagged = !el.Rest && Flags(el.Type) > 0;
                double posQ = el.Start * sc.QPerBeat;
                double barStartQ = sc.Bars[el.Bar].Start * sc.QPerBeat;
                double barQ = sc.BarLen * sc.QPerBeat;
                double notionalStart = sc.Bars[el.Bar].Pickup ? sc.Bars[el.Bar].End * sc.QPerBeat - barQ : barStartQ;
                int win = (int)Math.Floor((posQ - notionalStart) / span + EPS);
                int endWin = (int)Math.Floor((posQ + el.Q - notionalStart) / span - EPS);
                if (!flagged || el.Bar != lastBar || lastWin == null || win != lastWin.Value || endWin != win) Flush();
                if (flagged && endWin == win) group.Add(el);
                lastWin = win; lastBar = el.Bar;
                if (!flagged) { Flush(); lastWin = null; }
            }
            Flush();
            foreach (var el in els)
            {
                if (el.Rest || el.Type == NoteType.Whole) continue;
                if (el.Beam != 0) continue;
                el.Up = StemUp(el.Pos);
            }
            foreach (var g in groups)
            {
                int sum = 0;
                foreach (var el in g) foreach (int p in el.Pos) sum += p - 4;
                bool up = sum < 0;                          // mostly below the middle line: stems up
                foreach (var el in g) el.Up = up;
            }
            sc.BeamGroups.AddRange(groups);
        }

        static bool StemUp(int[] pos)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (int p in pos) { lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
            return 4 - lo > hi - 4;                         // the farther note decides; the middle line itself takes a down stem
        }
    }
}
