// Formatted panel strings, built once per distinct value: relayouts happen on every tutorial hit and every hover
// fade repaints the canvas, so "3 of 15", "Mistakes: 1" or "29 courses" must not be rebuilt each time (ARCHITECTURE.md
// section 17: no string building per frame). Lookups use long keys, so they never allocate.
using System.Collections.Generic;
using System.Globalization;
using MusicRoom.Core;

namespace MusicRoom.Menu
{
    public sealed class MenuText
    {
        enum Kind : long { Courses = 1, Saved, Takes, TakesScroll, Progress, Mistakes, Summary, Digit, Prefixed, LessonCount, Set }

        // Per-instrument strings (platform.md 5): built once per instrument (and option), never per relayout.
        enum Per { Inst = 1, TileAnchor, Options, WentBack, Option, OptionAnchor, OptionKey }
        readonly Dictionary<long, string> _per = new Dictionary<long, string>(64);
        readonly Dictionary<string, Dictionary<long, string>> _summaries = new Dictionary<string, Dictionary<long, string>>(4);
        static long PerKey(Per p, InstrumentKind k, int option = 0) => ((long)p << 40) | ((long)(option & 0xffff) << 16) | (long)((int)k & 0xffff);

        readonly Dictionary<long, string> _cache = new Dictionary<long, string>(64);
        readonly Dictionary<string, string> _prefixed = new Dictionary<string, string>(32);
        readonly Dictionary<string, string> _takeInfo = new Dictionary<string, string>(16);
        readonly Dictionary<string, string> _dates = new Dictionary<string, string>(32);
        // "Today 3:42 PM" turns into "Yesterday 3:42 PM" at local midnight: the take dates are kept for one local day.
        long _datesDay = long.MinValue;

        static long Key(Kind k, int a, int b = 0) => ((long)k << 56) | ((long)(a & 0xffffff) << 28) | (long)(b & 0xfffffff);

        bool TryGet(Kind k, int a, int b, out string s) => _cache.TryGetValue(Key(k, a, b), out s);
        string Put(Kind k, int a, int b, string s) { _cache[Key(k, a, b)] = s; return s; }

        /// <summary>"29 courses" (home), "13 courses" (lessons screen).</summary>
        public string Courses(int n) => TryGet(Kind.Courses, n, 0, out var s) ? s : Put(Kind.Courses, n, 0, n.ToString(CultureInfo.InvariantCulture) + " courses");

        /// <summary>"N saved" (home, Recordings row).</summary>
        public string Saved(int n) => TryGet(Kind.Saved, n, 0, out var s) ? s : Put(Kind.Saved, n, 0, n.ToString(CultureInfo.InvariantCulture) + " saved");

        /// <summary>"1 take" / "N takes" (recordings header).</summary>
        public string Takes(int n) => TryGet(Kind.Takes, n, 0, out var s) ? s : Put(Kind.Takes, n, 0, n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " take" : " takes"));

        /// <summary>"N takes  ·  drag to scroll".</summary>
        public string TakesScroll(int n) => TryGet(Kind.TakesScroll, n, 0, out var s) ? s : Put(Kind.TakesScroll, n, 0, n.ToString(CultureInfo.InvariantCulture) + " takes" + MenuStyle.Dot + "drag to scroll");

        /// <summary>"H of T" (progress, left).</summary>
        public string Progress(int hits, int total) => TryGet(Kind.Progress, hits, total, out var s) ? s
            : Put(Kind.Progress, hits, total, hits.ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture));

        /// <summary>"Mistakes: N" (progress, right).</summary>
        public string Mistakes(int n) => TryGet(Kind.Mistakes, n, 0, out var s) ? s : Put(Kind.Mistakes, n, 0, "Mistakes: " + n.ToString(CultureInfo.InvariantCulture));

        /// <summary>"N notes  ·  M mistake(s)  ·  T.T s" (summary; seconds to one decimal like toFixed(1)). The noun is the
        /// lesson instrument's ITutorial.CountNoun ("notes", "strokes", "rings"); it is part of the cache key.</summary>
        public string Summary(int notes, int mistakes, float seconds, string noun = "notes")
        {
            if (string.IsNullOrEmpty(noun)) noun = "notes";
            int tenths = (int)System.Math.Round(seconds * 10.0, System.MidpointRounding.AwayFromZero);
            long key = Key(Kind.Summary, notes | (mistakes << 12), tenths);
            if (!_summaries.TryGetValue(noun, out var cache)) { cache = new Dictionary<long, string>(8); _summaries[noun] = cache; }
            if (cache.TryGetValue(key, out var s)) return s;
            s = notes.ToString(CultureInfo.InvariantCulture) + " " + noun + MenuStyle.Dot
              + mistakes.ToString(CultureInfo.InvariantCulture) + (mistakes == 1 ? " mistake" : " mistakes") + MenuStyle.Dot
              + (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
            cache[key] = s;
            return s;
        }

        /// <summary>"1 lesson" / "N lessons" (a Lessons grid tile's status line).</summary>
        public string LessonCount(int n) => TryGet(Kind.LessonCount, n, 0, out var s) ? s
            : Put(Kind.LessonCount, n, 0, n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " lesson" : " lessons"));

        /// <summary>My set's names in planner order, "Piano, Drums, Harp". Cached per set (up to 6 members of 4 bits each).</summary>
        public string SetNames(IReadOnlyList<InstrumentKind> set, IInstrumentRegistry registry)
        {
            if (set == null || set.Count == 0) return "";
            long key = 0;
            int n = System.Math.Min(set.Count, 6);
            for (int i = 0; i < n; i++) key |= (long)(((int)set[i] & 0xf) + 1) << (4 + 5 * i);
            if (TryGet(Kind.Set, (int)(key >> 28), (int)(key & 0xfffffff), out var s)) return s;
            var sb = new System.Text.StringBuilder(48);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(MenuStyle.NameSeparator);
                InstrumentDescriptor d = registry?.Get(set[i]);
                sb.Append(d != null ? d.Name : InstrumentIds.Of(set[i]));
            }
            return Put(Kind.Set, (int)(key >> 28), (int)(key & 0xfffffff), sb.ToString());
        }

        string PerGet(Per p, InstrumentKind k, int option, out long key)
        {
            key = PerKey(p, k, option);
            return _per.TryGetValue(key, out var s) ? s : null;
        }

        string PerPut(long key, string s) { _per[key] = s; return s; }

        /// <summary>The tile's MenuRowEvent id, "inst.&lt;id&gt;".</summary>
        public string InstRowId(InstrumentDescriptor d) => PerGet(Per.Inst, d.Kind, 0, out long k) ?? PerPut(k, "inst." + d.Id);
        /// <summary>The tile's IGuideAnchors id, "menu.tile.&lt;id&gt;".</summary>
        public string TileAnchor(InstrumentDescriptor d) => PerGet(Per.TileAnchor, d.Kind, 0, out long k) ?? PerPut(k, "menu.tile." + d.Id);
        /// <summary>The "Options" chip's id, "options.&lt;id&gt;".</summary>
        public string OptionsRowId(InstrumentDescriptor d) => PerGet(Per.Options, d.Kind, 0, out long k) ?? PerPut(k, "options." + d.Id);
        /// <summary>An option row's id, "option.&lt;id&gt;.&lt;Key&gt;".</summary>
        public string OptionRowId(InstrumentDescriptor d, int i) => PerGet(Per.Option, d.Kind, i, out long k) ?? PerPut(k, "option." + d.Id + "." + d.Options[i].Key);
        /// <summary>An option row's IGuideAnchors id, "menu.option.&lt;id&gt;.&lt;Key&gt;".</summary>
        public string OptionAnchor(InstrumentDescriptor d, int i) => PerGet(Per.OptionAnchor, d.Kind, i, out long k) ?? PerPut(k, "menu.option." + d.Id + "." + d.Options[i].Key);
        /// <summary>The option's AppSettings int key, "musicroom.&lt;id&gt;.&lt;Key&gt;" (platform.md 4.7).</summary>
        public string OptionKey(InstrumentDescriptor d, int i) => PerGet(Per.OptionKey, d.Kind, i, out long k) ?? PerPut(k, "musicroom." + d.Id + "." + d.Options[i].Key);

        /// <summary>"Harp went back to the corner": the header note naming the member that made room (platform.md 5.1).</summary>
        public string WentBack(InstrumentDescriptor d) => PerGet(Per.WentBack, d.Kind, 0, out long k) ?? PerPut(k, d.Name + MenuStyle.WentBack + Place(d.Stow?.Where));

        /// <summary>Where a display place reads after "went back": a place that starts with its own preposition keeps it
        /// ("By the lamp" -> "by the lamp", "Under the window" -> "under the window"), except "On the sill" -> "to the sill";
        /// a bare place takes "to the" ("Corner" -> "to the corner", "Front wall" -> "to the front wall"); "Its place" when
        /// the descriptor names none.</summary>
        public static string Place(string where)
        {
            if (string.IsNullOrEmpty(where)) return "to its place";
            string w = char.ToLowerInvariant(where[0]) + where.Substring(1);
            if (w.StartsWith("on ", System.StringComparison.Ordinal)) return "to " + w.Substring(3);
            string[] preps = { "by ", "under ", "near ", "beside ", "in ", "at ", "above ", "below ", "behind ", "to " };
            foreach (string p in preps) if (w.StartsWith(p, System.StringComparison.Ordinal)) return w;
            return w.StartsWith("the ", System.StringComparison.Ordinal) ? "to " + w : "to the " + w;
        }

        /// <summary>The countdown digit.</summary>
        public string Digit(int d) => TryGet(Kind.Digit, d, 0, out var s) ? s : Put(Kind.Digit, d, 0, d.ToString(CultureInfo.InvariantCulture));

        /// <summary>A sub-screen title with the back chevron: "‹  " + title.</summary>
        public string Prefixed(string title)
        {
            title = title ?? "";
            if (_prefixed.TryGetValue(title, out var s)) return s;
            s = MenuStyle.BackPrefix + title;
            _prefixed[title] = s;
            return s;
        }

        /// <summary>The take screen's line "date  ·  m:ss  ·  N event(s)", per take id.</summary>
        public string TakeInfo(Core.TakeInfo t, Core.ITakeLibrary lib)
        {
            if (t == null) return "";
            string key = t.id ?? "";
            CheckDay();
            if (_takeInfo.TryGetValue(key, out var s)) return s;
            string date = Date(t, lib);
            string len = lib != null ? lib.FormatLength(t.seconds) : FormatLength(t.seconds);
            s = date + MenuStyle.Dot + len + MenuStyle.Dot + t.count.ToString(CultureInfo.InvariantCulture) + (t.count == 1 ? " event" : " events");
            _takeInfo[key] = s;
            return s;
        }

        /// <summary>The recordings row note: the library's local time ("Today 3:42 PM", "Nov 8, 4:05 PM"), per take id,
        /// built again once the local day changes.</summary>
        public string Date(Core.TakeInfo t, Core.ITakeLibrary lib)
        {
            if (t == null) return "";
            string key = t.id ?? "";
            CheckDay();
            if (_dates.TryGetValue(key, out var s)) return s;
            s = lib != null ? lib.FormatDate(t.created) : LocalTime.Friendly(t.created);
            _dates[key] = s;
            return s;
        }

        void CheckDay()
        {
            long day = LocalTime.TodayStamp();
            if (day == _datesDay) return;
            _datesDay = day;
            _dates.Clear();
            _takeInfo.Clear();
        }

        /// <summary>formatLength: s = round(sec), "m:ss" (the fallback when no library is bound).</summary>
        public static string FormatLength(double seconds)
        {
            int s = (int)System.Math.Round(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
