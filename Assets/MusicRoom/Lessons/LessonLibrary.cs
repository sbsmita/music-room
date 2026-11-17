// lessons.json (Lessons/Resources/lessons.json: the classic three's lessons) and every instrument's
// lesson pack (Resources/LessonPacks/<id>.json, the same LessonFile schema; platform.md 7.1), loaded with JsonUtility into
// the Core classes. Order: the carousel order of the instrument (the registry), then file order (lessons.json first, then
// the packs by file name). A lesson whose instrument is not a registry id is dropped and logged; a lesson id seen twice is
// an error (the later one is dropped and logged). Count(kind) counts any instrument's lessons; the file's counts are
// recomputed over everything loaded. Without a registry (EditMode Parse) instrument ids are the id block's.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed class LessonLibrary : ILessonLibrary
    {
        public const string ResourceName = "lessons";
        /// <summary>Resources folder of the instruments' packs (any module's Resources/LessonPacks/).</summary>
        public const string PackFolder = "LessonPacks";
        readonly List<Lesson> _lessons = new List<Lesson>();
        readonly Dictionary<string, Lesson> _byId = new Dictionary<string, Lesson>();
        readonly List<string> _errors = new List<string>();
        readonly List<string> _packs = new List<string>();
        IInstrumentRegistry _registry;

        public LessonFile File { get; private set; }
        public IReadOnlyList<Lesson> Lessons => _lessons;
        public LessonTutorialConstants Constants => File?.tutorial;
        /// <summary>Lessons dropped while loading (unknown instrument, duplicate id, a pack that would not parse).</summary>
        public IReadOnlyList<string> Errors => _errors;
        /// <summary>The pack files loaded, by name.</summary>
        public IReadOnlyList<string> Packs => _packs;

        /// <summary>lessons.json plus every Resources/LessonPacks file, ordered by the registry's carousel.</summary>
        public static LessonLibrary LoadFromResources(IInstrumentRegistry registry = null)
        {
            var files = new List<(string name, string json)>();
            var text = Resources.Load<TextAsset>(ResourceName);
            if (text == null) Debug.LogError("[Lessons] Resources/lessons.json missing");
            else files.Add((ResourceName, text.text));
            TextAsset[] packs = Resources.LoadAll<TextAsset>(PackFolder);
            var sorted = new List<TextAsset>(packs);
            sorted.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (TextAsset p in sorted) files.Add((PackFolder + "/" + p.name, p.text));
            return Load(files, registry);
        }

        /// <summary>One lessons.json-shaped file (the classic library alone; EditMode tests).</summary>
        public static LessonLibrary Parse(string json) => Load(new[] { (ResourceName, json) }, null);

        /// <summary>The first file is the main one (its tutorial constants are the library's); the others are packs.</summary>
        public static LessonLibrary Load(IEnumerable<(string name, string json)> files, IInstrumentRegistry registry)
        {
            var lib = new LessonLibrary { _registry = registry };
            var order = new List<(int carousel, int seq, Lesson lesson)>();
            int seq = 0;
            foreach (var (name, json) in files)
            {
                LessonFile f;
                try { f = JsonUtility.FromJson<LessonFile>(json); }
                catch (System.Exception e) { lib.Error($"{name}: does not parse ({e.Message})"); continue; }
                if (lib.File == null) lib.File = f ?? new LessonFile();
                else lib._packs.Add(name);
                if (f?.lessons == null) continue;
                foreach (Lesson l in f.lessons)
                {
                    if (l == null || string.IsNullOrEmpty(l.id)) { lib.Error($"{name}: a lesson without an id (dropped)"); continue; }
                    if (!lib.TryKind(l.instrument, out InstrumentKind kind))
                    {
                        lib.Error($"{name}: lesson {l.id} names instrument '{l.instrument}', which is not in the registry (dropped)");
                        continue;
                    }
                    if (lib._byId.ContainsKey(l.id)) { lib.Error($"{name}: duplicate lesson id {l.id} (dropped)"); continue; }
                    lib._byId[l.id] = l;
                    order.Add((lib.CarouselOf(kind), seq++, l));
                }
            }
            order.Sort((a, b) => a.carousel != b.carousel ? a.carousel.CompareTo(b.carousel) : a.seq.CompareTo(b.seq));
            foreach (var o in order) lib._lessons.Add(o.lesson);
            if (lib.File == null) lib.File = new LessonFile();
            if (lib.File.counts == null) lib.File.counts = new LessonCounts();
            lib.File.counts.lessons = lib._lessons.Count;
            lib.File.counts.events = lib.TotalEvents;
            lib.File.counts.targets = lib.TotalTargets;
            return lib;
        }

        void Error(string msg)
        {
            _errors.Add(msg);
            Debug.LogError("[Lessons] " + msg);
        }

        bool TryKind(string id, out InstrumentKind kind)
        {
            if (_registry != null)
            {
                if (_registry.TryParse(id, out kind)) return InstrumentIds.IsPlayable(kind);
                return false;
            }
            return InstrumentIds.TryParse(id, out kind) && InstrumentIds.IsPlayable(kind);
        }

        int CarouselOf(InstrumentKind kind)
        {
            InstrumentDescriptor d = _registry?.Get(kind);
            if (d != null) return d.Carousel;
            if (_registry != null) return int.MaxValue;
            return 0;   // no registry: file order alone (lessons.json as it is)
        }

        public Lesson Get(string id) => id != null && _byId.TryGetValue(id, out var l) ? l : null;

        /// <summary>The instrument's id for its lessons' "instrument" field.</summary>
        public string IdOf(InstrumentKind instrument) => _registry?.IdOf(instrument) ?? InstrumentIds.Of(instrument);

        public int Count(InstrumentKind instrument)
        {
            string id = IdOf(instrument);
            if (id == null) return 0;
            int n = 0;
            foreach (var l in _lessons) if (l.instrument == id) n++;
            return n;
        }

        /// <summary>The registry kind of a lesson (false for an id outside it).</summary>
        public bool KindOf(Lesson lesson, out InstrumentKind kind)
        {
            kind = default;
            return lesson != null && TryKind(lesson.instrument, out kind);
        }

        public int TotalEvents { get { int n = 0; foreach (var l in _lessons) n += l.events?.Length ?? 0; return n; } }
        public int TotalTargets { get { int n = 0; foreach (var l in _lessons) if (l.events != null) foreach (var e in l.events) n += e.targets?.Length ?? 0; return n; } }
    }
}
