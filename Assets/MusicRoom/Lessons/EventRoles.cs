// What every TakeEvent type means to the recorder and the looper (platform.md 6.1, 6.4), precomputed once from the
// registry's descriptors (IInstrumentRegistry.All[*].Events) so the per-event lookups in the frame are a dictionary read:
// the type's EventTypeSpec (role, key, Held, Sampled), its owner, and the pairings the looper needs at a window's end:
//   a Held onset ("pianoOn") -> the owner's Release with the same key ("pianoOff");
//   a lane onset ("bowlRubOn") and a Control ("bowlRubCtl") -> the owner's Release with key Lane ("bowlRubOff") and its
//   Control type, so an open lane can be closed with a final level-0 Control and the Off;
// when an owner has several candidates, the one sharing the longest prefix with the type wins ("bowlRubOn" pairs with
// "bowlRubOff", not "bowlDamp"). Unknown types are Onsets (never quantised away, never dropped), as the registry says.
// Without a registry (EditMode harnesses, a recorder built alone) Classic holds the three classic instruments' seven
// types, the same values their descriptors declare (EventRolesTests checks them against the definitions).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public sealed class EventRoles
    {
        struct Entry
        {
            public EventTypeSpec Spec;
            public int Owner;
            public InstrumentKind Kind;
            /// <summary>Held onset / lane onset / Control: the Release type that ends it (null: none).</summary>
            public string Release;
            /// <summary>Lane onset / Control: the lane's Control type (null: none).</summary>
            public string Control;
            /// <summary>Release: it ends a Held onset (a key that must be down for its release to count).</summary>
            public bool EndsHeld;
        }

        readonly Dictionary<string, Entry> _types = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly List<string> _sampled = new List<string>(4);

        /// <summary>The seven classic types (the piano's, the kit's, the harp's), as their descriptors declare them.</summary>
        public static readonly EventRoles Classic = new EventRoles(new[]
        {
            (InstrumentKind.Table, new[]
            {
                new EventTypeSpec(TakeEventType.PianoOn, EventRole.Onset, EventKey.Midi, held: true),
                new EventTypeSpec(TakeEventType.PianoOff, EventRole.Release, EventKey.Midi),
            }),
            (InstrumentKind.Table, new[]
            {
                new EventTypeSpec(TakeEventType.Drum, EventRole.Onset, EventKey.Kind),
                new EventTypeSpec(TakeEventType.DrumChoke, EventRole.Release, EventKey.Kind),
                new EventTypeSpec(TakeEventType.Hat, EventRole.State, EventKey.None, sampled: true),
            }),
            (InstrumentKind.Table, new[]
            {
                new EventTypeSpec(TakeEventType.Harp, EventRole.Onset, EventKey.Index),
                new EventTypeSpec(TakeEventType.HarpDamp, EventRole.Release, EventKey.Index),
            }),
        });

        /// <summary>Every playable descriptor's types (null registry: Classic).</summary>
        public static EventRoles From(IInstrumentRegistry registry)
        {
            if (registry == null) return Classic;
            var groups = new List<(InstrumentKind, EventTypeSpec[])>(registry.All.Count);
            foreach (InstrumentDescriptor d in registry.All)
                if (d?.Events != null && d.Events.Length > 0) groups.Add((d.Kind, d.Events));
            return new EventRoles(groups);
        }

        /// <summary>Groups of one owner's types each (EditMode tests build synthetic owners this way).</summary>
        public EventRoles(IEnumerable<(InstrumentKind kind, EventTypeSpec[] events)> owners)
        {
            int owner = 0;
            foreach (var (kind, events) in owners)
            {
                foreach (EventTypeSpec s in events)
                {
                    if (string.IsNullOrEmpty(s.Type) || _types.ContainsKey(s.Type)) continue;   // the registry already logged it
                    _types[s.Type] = new Entry { Spec = s, Owner = owner, Kind = kind };
                    if (s.Role == EventRole.State && s.Sampled) _sampled.Add(s.Type);
                }
                foreach (EventTypeSpec s in events)
                {
                    if (string.IsNullOrEmpty(s.Type) || !_types.TryGetValue(s.Type, out Entry e) || e.Owner != owner) continue;
                    bool lane = s.Key == EventKey.Lane;
                    if (s.Role == EventRole.Onset && (s.Held || lane))
                    {
                        e.Release = Best(events, s.Type, EventRole.Release, s.Key);
                        if (lane) e.Control = Best(events, s.Type, EventRole.Control, EventKey.None, anyKey: true);
                    }
                    else if (s.Role == EventRole.Control)
                    {
                        e.Release = Best(events, s.Type, EventRole.Release, EventKey.Lane);
                        e.Control = s.Type;
                    }
                    _types[s.Type] = e;
                }
                foreach (EventTypeSpec s in events)
                {
                    if (s.Role != EventRole.Onset || !s.Held || !_types.TryGetValue(s.Type, out Entry on) || on.Release == null) continue;
                    if (_types.TryGetValue(on.Release, out Entry off)) { off.EndsHeld = true; _types[on.Release] = off; }
                }
                owner++;
            }
        }

        static string Best(EventTypeSpec[] events, string type, EventRole role, EventKey key, bool anyKey = false)
        {
            string best = null;
            int bestLen = -1;
            foreach (EventTypeSpec c in events)
            {
                if (c.Role != role || (!anyKey && c.Key != key) || string.IsNullOrEmpty(c.Type)) continue;
                int n = 0;
                while (n < c.Type.Length && n < type.Length && c.Type[n] == type[n]) n++;
                if (n > bestLen) { best = c.Type; bestLen = n; }
            }
            return best;
        }

        public bool Knows(string type) => type != null && _types.ContainsKey(type);

        /// <summary>Unknown types are Onsets (the registry's rule).</summary>
        public EventRole RoleOf(string type) => type != null && _types.TryGetValue(type, out Entry e) ? e.Spec.Role : EventRole.Onset;

        /// <summary>The type's spec; an unknown type reads as an Onset with no key (Type null).</summary>
        public EventTypeSpec Spec(string type) => type != null && _types.TryGetValue(type, out Entry e) ? e.Spec : default;

        /// <summary>A group index per owner (two lanes 0 of two instruments never mix); -1 when unknown.</summary>
        public int OwnerOf(string type) => type != null && _types.TryGetValue(type, out Entry e) ? e.Owner : -1;

        /// <summary>The recording instrument (Table when unknown or built without a registry).</summary>
        public InstrumentKind KindOf(string type) => type != null && _types.TryGetValue(type, out Entry e) ? e.Kind : InstrumentKind.Table;

        /// <summary>The Release type that ends this Held onset, lane onset or Control (null: none).</summary>
        public string ReleaseOf(string type) => type != null && _types.TryGetValue(type, out Entry e) ? e.Release : null;

        /// <summary>The Control type of this lane onset or Control (null: none).</summary>
        public string ControlOf(string type) => type != null && _types.TryGetValue(type, out Entry e) ? e.Control : null;

        /// <summary>This Release ends a Held onset (its key must be open for it to count).</summary>
        public bool EndsHeld(string type) => type != null && _types.TryGetValue(type, out Entry e) && e.EndsHeld;

        /// <summary>The Sampled State types (the hi-hat pedal), in declaration order.</summary>
        public IReadOnlyList<string> Sampled => _sampled;

        // ---- the key fields of a TakeEvent (EventKey: midi, i, kind, lane) ----

        /// <summary>Whether the key value can be tracked (a midi outside 0..127 is not, like the looper's old arrays).</summary>
        public static bool Trackable(EventKey key, in TakeEvent e) => key != EventKey.Midi || (uint)e.midi < 128u;

        public static void KeyOf(EventKey key, in TakeEvent e, out int value, out string text)
        {
            text = null;
            switch (key)
            {
                case EventKey.Midi: value = e.midi; break;
                case EventKey.Index: value = e.i; break;
                case EventKey.Kind: value = 0; text = e.kind; break;
                case EventKey.Lane: value = e.lane; break;
                default: value = 0; break;
            }
        }

        public static void SetKey(ref TakeEvent e, EventKey key, int value, string text)
        {
            switch (key)
            {
                case EventKey.Midi: e.midi = value; break;
                case EventKey.Index: e.i = value; break;
                case EventKey.Kind: e.kind = text; break;
                case EventKey.Lane: e.lane = value; break;
            }
        }
    }
}
