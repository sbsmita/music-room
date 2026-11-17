// The book's own data (Resources/sheet-book.json, from the lab's lessons-meta.json): the notation facts the lesson JSON
// does not carry (meter, the note value of one beat, pickup, credit, staves, a lesson shown on another song's page), the
// book's title, and the two-staff pieces that prove the grand staff (no two-hand lesson exists yet). JsonUtility shapes.
// A piano lesson with no entry is printed in 4/4 from beat 0, credited "Traditional" (SheetDataTests lists them).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Sheet.Engrave
{
    [Serializable] public class SheetBookFile { public SheetBookTitle book; public SheetMetaEntry[] lessons; public SheetExtraLesson[] extraLessons; }
    [Serializable] public class SheetBookTitle { public string title, subtitle; }
    [Serializable]
    public class SheetMetaEntry
    {
        public string id, credit, cueNote, bookSong;
        public int[] meter;
        public int unit, staves, eventOffset, splitBelow;
        public float pickup;
    }
    [Serializable] public class SheetExtraLesson { public string id, instrument, title, tune, melodyFrom; public float bpm, beats; public SheetExtraChord[] accompany; public SheetExtraHand[] leftHand; }
    [Serializable] public class SheetExtraChord { public float beat, len; public int[] midi; }
    [Serializable] public class SheetExtraHand { public float beat, len; public string note; }

    public static class SheetBookData
    {
        public const string ResourceName = "sheet-book";

        public static SheetBookFile Parse(string json) => UnityEngine.JsonUtility.FromJson<SheetBookFile>(json);

        public static Dictionary<string, SheetMeta> MetaTable(SheetBookFile f)
        {
            var d = new Dictionary<string, SheetMeta>();
            if (f?.lessons == null) return d;
            foreach (var e in f.lessons)
            {
                if (string.IsNullOrEmpty(e.id)) continue;
                d[e.id] = new SheetMeta
                {
                    MeterN = e.meter != null && e.meter.Length == 2 && e.meter[0] > 0 ? e.meter[0] : 4,
                    MeterD = e.meter != null && e.meter.Length == 2 && e.meter[1] > 0 ? e.meter[1] : 4,
                    Unit = e.unit > 0 ? e.unit : 4,
                    Pickup = e.pickup,
                    Credit = string.IsNullOrEmpty(e.credit) ? "Traditional" : e.credit,
                    Staves = e.staves == 2 ? 2 : 1,
                    CueNote = string.IsNullOrEmpty(e.cueNote) ? null : e.cueNote,
                    BookSong = string.IsNullOrEmpty(e.bookSong) ? null : e.bookSong,
                    EventOffset = e.eventOffset,
                    SplitBelow = e.splitBelow > 0 ? e.splitBelow : -1,
                };
            }
            return d;
        }

        /// <summary>A Core lesson as engraver input (event k = its index, which is what ITutorial counts).</summary>
        public static SheetLesson FromLesson(Lesson l)
        {
            var s = new SheetLesson { Id = l.id, Title = l.title ?? l.id, Bpm = l.bpm, Beats = l.beats };
            if (l.events == null) return s;
            for (int i = 0; i < l.events.Length; i++)
            {
                var e = l.events[i];
                var se = new SheetEvent { K = i, Beat = e.beat, Len = e.len };
                if (e.targets != null)
                    foreach (var t in e.targets)
                        se.Targets.Add(new SheetTarget
                        {
                            Midi = t.midi, Note = t.note, Hand = string.IsNullOrEmpty(t.hand) ? '\0' : char.ToUpperInvariant(t.hand[0]),
                            Len = t.len > 0f ? t.len : double.NaN,   // a hand holding longer than its event (two-hand lessons)
                        });
                s.Events.Add(se);
            }
            if (l.accompany != null)
                foreach (var a in l.accompany) if (a?.midi != null) s.Accompany.Add(new SheetChord { Beat = a.beat, Len = a.len, Midi = a.midi });
            return s;
        }

        /// <summary>A lesson with no sheet-book.json entry: its own notation fields (packs: meter "3/4", unit "q" / "e" / "h" or
        /// a number, pickup beats, credit), else 4/4 from beat 0, "Traditional". Two staves when a target names a hand.</summary>
        public static SheetMeta MetaOf(Lesson l)
        {
            var m = SheetMeta.Default;
            if (!string.IsNullOrEmpty(l.meter))
            {
                var parts = l.meter.Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out int n) && int.TryParse(parts[1], out int d) && n > 0 && d > 0) { m.MeterN = n; m.MeterD = d; }
            }
            if (!string.IsNullOrEmpty(l.unit))
            {
                string u = l.unit.Trim().ToLowerInvariant();
                m.Unit = u == "q" ? 4 : u == "e" ? 8 : u == "h" ? 2 : u == "s" ? 16 : int.TryParse(u, out int un) && un > 0 ? un : 4;
            }
            m.Pickup = l.pickup > 0f ? l.pickup : 0;
            if (!string.IsNullOrEmpty(l.credit)) m.Credit = l.credit;
            if (l.events != null)
                foreach (var e in l.events)
                    if (e.targets != null) foreach (var t in e.targets) if (!string.IsNullOrEmpty(t.hand)) m.Staves = 2;
            return m;
        }

        /// <summary>The lab's expandExtra: the first-run song (tune + accompaniment) and the two-hand demo as lesson data.</summary>
        public static SheetLesson ExpandExtra(SheetExtraLesson x, Func<string, SheetLesson> lessonById)
        {
            var s = new SheetLesson { Id = x.id, Title = x.title, Bpm = x.bpm, Beats = x.beats };
            if (!string.IsNullOrEmpty(x.tune))
            {
                double beat = 0;
                var toks = x.tune.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int k = 0; k < toks.Length; k++)
                {
                    var parts = toks[k].Split(':');
                    double len = parts.Length > 1 ? double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
                    var e = new SheetEvent { K = k, Beat = beat, Len = len };
                    e.Targets.Add(new SheetTarget { Midi = SheetScore.Spell(parts[0]).Midi, Note = parts[0] });
                    s.Events.Add(e);
                    beat += len;
                }
            }
            else if (!string.IsNullOrEmpty(x.melodyFrom))
            {
                var src = lessonById(x.melodyFrom);
                if (src != null)
                    foreach (var e in src.Events)
                    {
                        var ne = new SheetEvent { K = e.K, Beat = e.Beat, Len = e.Len };
                        foreach (var t in e.Targets) ne.Targets.Add(new SheetTarget { Midi = t.Midi, Note = t.Note, Hand = 'R' });
                        s.Events.Add(ne);
                    }
                if (x.leftHand != null)
                    foreach (var lh in x.leftHand)
                    {
                        var t = new SheetTarget { Midi = SheetScore.Spell(lh.note).Midi, Note = lh.note, Hand = 'L', Len = lh.len };
                        SheetEvent hit = null;
                        foreach (var ev in s.Events) if (Math.Abs(ev.Beat - lh.beat) < 1e-6) { hit = ev; break; }
                        if (hit != null) hit.Targets.Add(t);
                        else { var ne = new SheetEvent { K = -1, Beat = lh.beat, Len = lh.len }; ne.Targets.Add(t); s.Events.Add(ne); }
                    }
                JsMath.StableSort(s.Events, (a, b) => JsMath.Cmp(a.Beat - b.Beat));
            }
            if (x.accompany != null)
                foreach (var a in x.accompany) s.Accompany.Add(new SheetChord { Beat = a.beat, Len = a.len, Midi = a.midi ?? Array.Empty<int>() });
            return s;
        }
    }
}
