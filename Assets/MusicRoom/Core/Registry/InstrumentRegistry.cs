// The instrument registry (platform.md 3.1-3.2): built by App before ModuleDiscovery.CreateModules from every parameterless
// IInstrumentDefinition in the MusicRoom.* assemblies (never MusicRoom.Tests.*), plus the built-in Table and Menu. A
// duplicate Kind, Id, Carousel index, event type, tip id or feature id is a hard error ("[Core] registry: ..." on the log,
// RegistryTests fails) and the later definition (by type name) is dropped. Instruments become IsLoaded when their module
// registers its Rig (App.Register<Rig>). Static data only: it builds in EditMode without a scene.
//
// Core/Registry is the one Core folder allowed to name instruments (RegistryLintTests): the id block lives here.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>The id block's persisted ids (platform.md section 2): the one table that maps a kind to its id string without a
    /// registry (settings keys, ArrangementCodec). Values never change.</summary>
    public static class InstrumentIds
    {
        // 8: retired (RetiredInstruments): no id, so an old save's id for it parses as nothing
        static readonly string[] s_ids = { "piano", "drums", "harp", "table", "menu", "handpan", "handdrums", "marimba", null, "bowls" };
        public const int KindCount = 10;
        /// <summary>The id of a kind, or null for a value outside the block.</summary>
        public static string Of(InstrumentKind k) => (int)k >= 0 && (int)k < s_ids.Length ? s_ids[(int)k] : null;
        public static bool TryParse(string id, out InstrumentKind kind)
        {
            if (!string.IsNullOrEmpty(id))
                for (int i = 0; i < s_ids.Length; i++) if (s_ids[i] == id) { kind = (InstrumentKind)i; return true; }
            kind = InstrumentKind.Piano;
            return false;
        }
        /// <summary>Every kind but the side table and the menu.</summary>
        public static bool IsPlayable(InstrumentKind k) => k != InstrumentKind.Table && k != InstrumentKind.Menu && Of(k) != null;
        /// <summary>RigOf(kind) = (RigId)(int)kind (Lessons = 4 = Menu): the id block's rule.</summary>
        public static RigId RigOf(InstrumentKind k) => (RigId)(int)k;
        public static InstrumentKind KindOf(RigId r) => (InstrumentKind)(int)r;
    }

    /// <summary>Audio's presence control (platform.md 10.1, AudioPresence): wake a displayed instrument's sources ahead of a
    /// playback dispatch. Registered by the Audio module; the default does nothing.</summary>
    public interface IAudioPresence
    {
        /// <summary>Resume the instrument's paused sources now (a take or loop event will be dispatched within 0.3 s).</summary>
        void Wake(InstrumentKind kind);
        /// <summary>Its sources are paused (displayed and silent for 1 s).</summary>
        bool Paused(InstrumentKind kind);
    }

    public sealed class InstrumentRegistry : IInstrumentRegistry
    {
        readonly List<InstrumentDescriptor> _all = new List<InstrumentDescriptor>(10);            // playable, carousel order
        readonly List<InstrumentKind> _carousel = new List<InstrumentKind>(10);
        readonly List<InstrumentDescriptor> _every = new List<InstrumentDescriptor>(12);          // + built-ins
        readonly Dictionary<int, InstrumentDescriptor> _byKind = new Dictionary<int, InstrumentDescriptor>();
        readonly Dictionary<string, InstrumentKind> _byId = new Dictionary<string, InstrumentKind>(StringComparer.Ordinal);
        readonly Dictionary<string, InstrumentKind> _owner = new Dictionary<string, InstrumentKind>(StringComparer.Ordinal);
        readonly Dictionary<string, EventTypeSpec> _events = new Dictionary<string, EventTypeSpec>(StringComparer.Ordinal);
        readonly Dictionary<int, Type> _definitionType = new Dictionary<int, Type>();
        readonly bool[] _loaded = new bool[64];
        readonly bool[] _filtered = new bool[64];
        readonly List<string> _errors = new List<string>();
        readonly HashSet<string> _tipIds = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _featureIds = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<InstrumentDescriptor> All => _all;
        public IReadOnlyList<InstrumentKind> Carousel => _carousel;
        /// <summary>Every descriptor, the built-in Table and Menu included (by Kind).</summary>
        public IReadOnlyList<InstrumentDescriptor> Every => _every;
        /// <summary>Validation errors of the build (empty when clean). Each was also logged as "[Core] registry: ...".</summary>
        public IReadOnlyList<string> Errors => _errors;
        /// <summary>Raised when an instrument becomes loaded.</summary>
        public event Action<InstrumentKind> Loaded;

        /// <summary>Reflection over the MusicRoom.* runtime assemblies (the module-discovery rule).</summary>
        public static InstrumentRegistry Discover(Func<InstrumentKind, bool> filter = null)
        {
            var defs = new List<(Type, InstrumentDescriptor)>();
            foreach (Type t in ModuleDiscovery.FindTypes<IInstrumentDefinition>())
            {
                try { defs.Add((t, ((IInstrumentDefinition)Activator.CreateInstance(t)).Describe())); }
                catch (Exception e) { Debug.LogError($"[Core] registry: {t.FullName} failed to describe itself: {e.Message}"); }
            }
            return Build(defs, filter);
        }

        /// <summary>Builds from descriptors (EditMode tests; Discover). The built-ins are added first.</summary>
        public static InstrumentRegistry Build(IEnumerable<InstrumentDescriptor> descriptors, Func<InstrumentKind, bool> filter = null)
        {
            var list = new List<(Type, InstrumentDescriptor)>();
            foreach (var d in descriptors) list.Add((null, d));
            return Build(list, filter);
        }

        static InstrumentRegistry Build(List<(Type type, InstrumentDescriptor d)> defs, Func<InstrumentKind, bool> filter)
        {
            var r = new InstrumentRegistry();
            r.Add(null, BuiltIns.Table());
            r.Add(null, BuiltIns.Menu());
            foreach (var (type, d) in defs) r.Add(type, d);
            r._all.Sort((a, b) => a.Carousel.CompareTo(b.Carousel));
            foreach (var d in r._all) r._carousel.Add(d.Kind);
            for (int i = 0; i < r._filtered.Length; i++) r._filtered[i] = false;
            if (filter != null)
                foreach (var d in r._all)
                    if (!filter(d.Kind)) r._filtered[(int)d.Kind] = true;
            // The built-ins are loaded by definition (the deck, the switch panel and the menu register their own rigs too).
            r._loaded[(int)InstrumentKind.Table] = r._loaded[(int)InstrumentKind.Menu] = true;
            return r;
        }

        void Error(string msg)
        {
            _errors.Add(msg);
            Debug.LogError("[Core] registry: " + msg);
        }

        void Add(Type type, InstrumentDescriptor d)
        {
            if (d == null) { Error($"{type?.FullName ?? "a definition"} returned no descriptor"); return; }
            string who = type != null ? type.Name : d.Id;
            if ((int)d.Kind < 0 || (int)d.Kind >= _loaded.Length) { Error($"{who}: kind {(int)d.Kind} is outside the id block"); return; }
            if (_byKind.ContainsKey((int)d.Kind)) { Error($"{who}: duplicate kind {d.Kind} (dropped)"); return; }
            if (string.IsNullOrEmpty(d.Id) || _byId.ContainsKey(d.Id)) { Error($"{who}: duplicate or empty id '{d.Id}' (dropped)"); return; }
            if (d.Playable)
                foreach (var o in _all)
                    if (o.Carousel == d.Carousel) { Error($"{who}: duplicate carousel index {d.Carousel} (with {o.Id}; dropped)"); return; }
            if ((int)d.Rig != (int)d.Kind) Error($"{who}: RigId {d.Rig} does not equal its kind {d.Kind} (the id block's rule)");
            // Event types, tips and features: unique across the registry.
            if (d.Events != null)
                foreach (var e in d.Events)
                    if (string.IsNullOrEmpty(e.Type) || _events.ContainsKey(e.Type)) { Error($"{who}: duplicate or empty event type '{e.Type}' (dropped)"); return; }
            if (d.Tips != null)
                foreach (var t in d.Tips)
                    if (t == null || string.IsNullOrEmpty(t.Id) || _tipIds.Contains(t.Id)) { Error($"{who}: duplicate or empty tip id '{t?.Id}' (dropped)"); return; }
            if (d.Features != null)
                foreach (var f in d.Features)
                    if (string.IsNullOrEmpty(f.Id) || _featureIds.Contains(f.Id)) { Error($"{who}: duplicate or empty feature id '{f.Id}' (dropped)"); return; }
            _byKind[(int)d.Kind] = d;
            _byId[d.Id] = d.Kind;
            if (type != null) _definitionType[(int)d.Kind] = type;
            if (d.Events != null) foreach (var e in d.Events) { _events[e.Type] = e; _owner[e.Type] = d.Kind; }
            if (d.Tips != null) foreach (var t in d.Tips) _tipIds.Add(t.Id);
            if (d.Features != null) foreach (var f in d.Features) _featureIds.Add(f.Id);
            _every.Add(d);
            if (d.Playable) _all.Add(d);
        }

        public InstrumentDescriptor Get(InstrumentKind kind) => _byKind.TryGetValue((int)kind, out var d) ? d : null;

        public bool TryParse(string id, out InstrumentKind kind)
        {
            if (id != null && _byId.TryGetValue(id, out kind)) return true;
            kind = InstrumentKind.Piano;
            return false;
        }

        public string IdOf(InstrumentKind kind) => Get(kind)?.Id;

        public bool IsLoaded(InstrumentKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < _loaded.Length && _loaded[i] && !_filtered[i] && _byKind.ContainsKey(i);
        }

        /// <summary>Kept out by BootOptions.InstrumentFilter (its module is not created).</summary>
        public bool IsFiltered(InstrumentKind kind) => (int)kind >= 0 && (int)kind < _filtered.Length && _filtered[(int)kind];

        /// <summary>The definition class a kind came from (null for the built-ins and Build(descriptors)); its assembly is the
        /// instrument's module assembly (BootOptions.InstrumentFilter keeps that assembly's modules out).</summary>
        public Type DefinitionType(InstrumentKind kind) => _definitionType.TryGetValue((int)kind, out var t) ? t : null;

        /// <summary>App.Register&lt;Rig&gt;: the module registered its rig. Logged once per kind.</summary>
        public void MarkLoaded(InstrumentKind kind)
        {
            int i = (int)kind;
            if (i < 0 || i >= _loaded.Length || _loaded[i]) return;
            _loaded[i] = true;
            if (!_byKind.ContainsKey(i)) return;
            Loaded?.Invoke(kind);
        }

        public InstrumentKind OwnerOfEvent(string type) => type != null && _owner.TryGetValue(type, out var k) ? k : InstrumentKind.Table;

        public EventRole RoleOf(string type) => type != null && _events.TryGetValue(type, out var e) ? e.Role : EventRole.Onset;

        public EventTypeSpec EventSpec(string type) => type != null && _events.TryGetValue(type, out var e) ? e : default;

        public InstrumentKind Next(InstrumentKind from, int direction)
        {
            int n = _carousel.Count;
            if (n == 0) return from;
            int i = _carousel.IndexOf(from);
            if (i < 0) i = 0;
            int step = direction >= 0 ? 1 : -1;
            for (int k = 0; k < n; k++)
            {
                i = (i + step + n) % n;
                if (IsLoaded(_carousel[i])) return _carousel[i];
            }
            return from;
        }
    }
}
