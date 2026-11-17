// Notation (sheet.md 9.2 SheetDataTests / SheetScoreTests, the lab's T1, T2, T5): every piano lesson of BOTH lesson files
// (today's lessons.json and lessons-proposed.json) and both two-staff pieces engrave; every bar adds up; every lesson event
// is exactly one onset; the staff position and ledger count of every pitch used (vectors), against an independent
// derivation; Für Elise's sharps and naturals; a tie across a bar line (Silent Night's last C4).
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Sheet.Engrave;
using NUnit.Framework;

namespace MusicRoom.Tests.Sheet
{
    public class SheetScoreTests
    {
        static IEnumerable<(SheetLesson, SheetMeta)> All(string which)
        {
            if (which == "extras") return SheetTestData.Extras();
            return SheetTestData.Songs(which == "current" ? SheetTestData.CurrentLessons() : SheetTestData.ProposedLessons());
        }

        [Test]
        public void EveryLessonHasNotationAndEveryBarAddsUp([Values("current", "proposed", "extras")] string which)
        {
            int lessons = 0;
            foreach (var (l, m) in All(which))
            {
                lessons++;
                var sc = SheetScore.Build(l, m);
                double barQ = sc.BarLen * sc.QPerBeat;
                foreach (var st in sc.Staves)
                    foreach (var bar in sc.Bars)
                    {
                        double q = st.Els.Where(e => e.Bar == bar.Index).Sum(e => e.Q);
                        double want = (bar.End - bar.Start) * sc.QPerBeat;
                        Assert.That(q, Is.EqualTo(want).Within(1e-6), $"{l.Id} {st.Clef} bar {bar.Index} sums");
                        if (!bar.Pickup && !bar.Final) Assert.That(want, Is.EqualTo(barQ).Within(1e-6), $"{l.Id} bar {bar.Index} is full");
                    }
                var ks = new HashSet<int>();
                foreach (var st in sc.Staves)
                {
                    var perStaff = new HashSet<int>();   // a two-hand event is an onset on each staff it has notes on
                    foreach (var e in st.Els) if (e.K >= 0 && !e.Cue) { Assert.That(perStaff.Add(e.K), Is.True, $"{l.Id} {st.Clef} event {e.K} once"); ks.Add(e.K); }
                }
                Assert.That(ks.Count, Is.EqualTo(l.Events.Count(e => e.K >= 0)), $"{l.Id}: every event is an onset");
                Assert.That(sc.Warnings, Is.Empty, $"{l.Id}: {string.Join("; ", sc.Warnings)}");
            }
            Assert.That(lessons, Is.GreaterThanOrEqualTo(which == "extras" ? 2 : 13));
        }

        [Test]
        public void EveryPianoLessonOfTheLibraryHasABookEntry()
        {
            var meta = SheetTestData.Meta;
            foreach (var f in new[] { SheetTestData.CurrentLessons(), SheetTestData.ProposedLessons() })
                foreach (var l in f.lessons)
                    if (l.instrument == "piano") Assert.That(meta.ContainsKey(l.id), Is.True, $"{l.id} has a sheet-book.json entry (meter, pickup, credit)");
        }

        [Test]
        public void TheFirstRunSongMapsOntoTheOdesSecondHalf()
        {
            var m = SheetTestData.Meta["piano-ode-first"];
            Assert.That(m.BookSong, Is.EqualTo("piano-ode"));
            Assert.That(m.EventOffset, Is.EqualTo(15));
            // the first-run tune is bars 5-8 of the Ode: its 15 events are the Ode's events 15..29, note for note
            var extras = SheetTestData.Extras();
            var first = extras.First(x => x.Item1.Id == "piano-ode-first").Item1;
            var ode = SheetTestData.Songs(SheetTestData.ProposedLessons()).First(x => x.Item1.Id == "piano-ode").Item1;
            Assert.That(first.Events.Count, Is.EqualTo(15));
            for (int k = 0; k < 15; k++)
            {
                Assert.That(first.Events[k].Targets[0].Midi, Is.EqualTo(ode.Events[k + 15].Targets[0].Midi), $"event {k}");
                Assert.That(first.Events[k].Len, Is.EqualTo(ode.Events[k + 15].Len), $"event {k} length");
            }
            var book = SheetTestData.Book("proposed");
            for (int k = 15; k < 30; k++) Assert.That(book.TryGetMark("piano-ode", k, out _, out int page), Is.True, $"the Ode's event {k} has a mark");
        }

        [Test]
        public void StaffPositionOfEveryPitchUsed()
        {
            var pitches = SheetTestData.L(SheetTestData.O(SheetTestData.Json("test-vectors.json"))["pitches"]);
            Assert.That(pitches.Count, Is.GreaterThanOrEqualTo(20));
            foreach (var o in pitches)
            {
                var row = SheetTestData.O(o);
                string name = (string)row["note"];
                var clef = (string)row["clef"] == "bass" ? Staff.Bass : Staff.Treble;
                var sp = SheetScore.Spell(name);
                int p = SheetScore.StaffPos(sp.Step, clef);
                Assert.That(sp.Midi, Is.EqualTo(SheetTestData.I(row["midi"])), name);
                Assert.That(p, Is.EqualTo(SheetTestData.I(row["p"])), $"{name} {clef} position");
                // independent derivation: treble bottom line E4, bass bottom line G2, one diatonic step a half-space
                int idx = "CDEFGAB".IndexOf(sp.Letter);
                int reference = clef == Staff.Treble ? (sp.Octave - 4) * 7 + idx - 2 : (sp.Octave - 2) * 7 + idx - 4;
                Assert.That(p, Is.EqualTo(reference), $"{name} derivation");
                int ledgers = p <= -2 ? -p / 2 : p >= 10 ? (p - 8) / 2 : 0;
                Assert.That(ledgers, Is.EqualTo(SheetTestData.I(row["ledgers"])), $"{name} ledger lines");
                Assert.That(SheetScore.KeyLabel(sp.Midi), Is.EqualTo((string)row["label"]), $"{name} label");
            }
        }

        [Test]
        public void FurEliseSharpsAndNaturals()
        {
            var want = SheetTestData.L(SheetTestData.O(SheetTestData.Json("test-vectors.json"))["eliseAccidentals"]).Cast<string>().ToList();
            Assert.That(want, Is.EqualTo(new[] { "0.5:sharp", "1.5:sharp", "3:natural", "9:sharp", "12.5:sharp", "13.5:sharp", "15:natural" }));
            foreach (var f in new[] { SheetTestData.CurrentLessons(), SheetTestData.ProposedLessons() })
            {
                var (l, m) = SheetTestData.Songs(f).First(x => x.Item1.Id == "piano-elise");
                var sc = SheetScore.Build(l, m);
                var got = sc.StaffOf(Staff.Treble).Els.Where(e => e.Acc.Length > 0 && e.Acc[0] != Accidental.None)
                    .Select(e => $"{e.Start.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{e.Acc[0].ToString().ToLowerInvariant()}").ToList();
                Assert.That(got, Is.EqualTo(want), "D#5 sharps, D5 naturals (an earlier sharp in the bar is cancelled)");
                Assert.That(sc.MeterN, Is.EqualTo(3)); Assert.That(sc.MeterD, Is.EqualTo(8));
                Assert.That(sc.Bars[0].Pickup, Is.True);
                // its 0.5-beat notes are sixteenths (unit 8), beamed in groups of up to six
                Assert.That(sc.StaffOf(Staff.Treble).Els.Count(e => !e.Rest && e.Type == NoteType.Sixteenth), Is.GreaterThan(20));
                Assert.That(sc.BeamGroups.Max(g => g.Count), Is.EqualTo(6));
            }
        }

        [Test]
        public void TiesAcrossBarLines()
        {
            var (l, m) = SheetTestData.Songs(SheetTestData.ProposedLessons()).First(x => x.Item1.Id == "piano-silent");
            var sc = SheetScore.Build(l, m);
            var els = sc.StaffOf(Staff.Treble).Els.Where(e => !e.Rest).ToList();
            var last = els[els.Count - 1];
            Assert.That(last.Notes[0].Midi, Is.EqualTo(60), "Silent Night ends on C4");
            Assert.That(last.TiePrev, Is.True, "its last C4 is tied over the bar line");
            Assert.That(last.K, Is.EqualTo(-1), "a tied continuation is no onset (no letter, no mark)");
            var head = els[els.Count - 2];
            Assert.That(head.TieNext, Is.True);
            Assert.That(head.Bar, Is.LessThan(last.Bar));
            Assert.That(head.K, Is.EqualTo(l.Events.Count - 1));
        }

        [Test]
        public void TwoStaffPiecesPutTheLeftHandOnTheBassStaff()
        {
            foreach (var (l, m) in SheetTestData.Extras())
            {
                var sc = SheetScore.Build(l, m);
                Assert.That(sc.Staves.Count, Is.EqualTo(2), l.Id);
                var bass = sc.StaffOf(Staff.Bass).Els.Where(e => !e.Rest).ToList();
                Assert.That(bass.Count, Is.GreaterThan(0));
                Assert.That(bass.SelectMany(e => e.Pos).Min(), Is.GreaterThanOrEqualTo(-4), "C2 at most two ledger lines below");
                if (l.Id == "piano-ode-first") Assert.That(bass.All(e => e.Cue), Is.True, "the accompaniment prints as cue notes");
            }
        }

        [Test]
        public void OddLessonDataNeverThrows()
        {
            var l = new SheetLesson { Id = "odd", Title = "Odd", Bpm = 90, Beats = 7 };
            void Ev(int k, double beat, double len, int midi) { var e = new SheetEvent { K = k, Beat = beat, Len = len }; e.Targets.Add(new SheetTarget { Midi = midi }); l.Events.Add(e); }
            Ev(0, 0, 1.3, 60);    // not spellable exactly
            Ev(1, 1, 1, 64);      // overlaps the first
            Ev(2, 1, 1, 67);      // same start: a chord
            Ev(3, 5.5, 0.5, 73);  // C#5
            var sc = SheetScore.Build(l, SheetMeta.Default);
            Assert.That(sc.Warnings, Is.Not.Empty);
            var book = new SheetBook(SheetTestData.Font, "t");
            book.Build(new List<(SheetLesson, SheetMeta)> { (l, SheetMeta.Default) });
            Assert.That(book.PageDraws(book.Songs[0].First).Count, Is.GreaterThan(10));
        }
    }
}
