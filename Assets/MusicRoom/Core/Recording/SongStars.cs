// The playback show's night sky, the maths both its places share (pure: no Unity objects but Color / Vector2):
//   - the room's sky (Room/Decor/SongSky.cs): while a recording plays back, every note sends a star up from the instrument
//     that played it; it settles in a sky above the room, higher for a higher note, brighter and larger for a louder one,
//     in its instrument's colour;
//   - the small constellation picture on each recording's row of the Recordings list (Menu): the same stars, laid out from
//     the take's own events (time across, pitch up), so it needs nothing saved beside the take and the same take always
//     draws the same picture.
// Pitch: a pitched instrument's note is a MIDI number (the piano's and the marimba's midi, the harp's string, the handpan's
// field, the bowl), its height within the take's own range (Range: at least an octave, centred on what was played); an
// unpitched one (the kit, the hand drums) has a fixed height per piece, low to high (kick .. crash). Only onsets make stars.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public static class SongStars
    {
        /// <summary>The range used before a take's own is known (a loop being played live): C3 .. C6.</summary>
        public const float DefaultLo = 48f, DefaultHi = 84f;
        /// <summary>A take's range is at least this many semitones (a song on three notes still spreads, but no more than an
        /// octave), with this much room above and below.</summary>
        public const float MinSpan = 12f, Pad = 1f;
        /// <summary>The most stars on a row's picture (a long take keeps an even share of each instrument's notes).</summary>
        public const int ThumbMax = 24;

        static readonly int[] HarpDegrees = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] HandpanMidi = { 50, 57, 60, 62, 64, 65, 67, 69, 72 };
        static readonly int[] BowlMidi = { 55, 60, 64 };

        // ---------------------------------------------------------------- colour

        /// <summary>Each instrument's star colour (sRGB): soft, clear and distinct; the piano's warm gold and the kit's ice
        /// blue are far apart.</summary>
        public static Color ColorOf(InstrumentKind k)
        {
            switch (k)
            {
                case InstrumentKind.Piano: return new Color(1.00f, 0.84f, 0.48f);      // warm gold
                case InstrumentKind.Drums: return new Color(0.42f, 0.78f, 1.00f);      // ice blue
                case InstrumentKind.Harp: return new Color(0.80f, 0.60f, 1.00f);       // lilac
                case InstrumentKind.Handpan: return new Color(0.40f, 1.00f, 0.82f);    // sea glass
                case InstrumentKind.HandDrums: return new Color(1.00f, 0.52f, 0.42f);  // coral
                case InstrumentKind.Marimba: return new Color(0.62f, 1.00f, 0.42f);    // spring green
                case InstrumentKind.Bowls: return new Color(1.00f, 0.60f, 0.82f);      // rose
                default: return new Color(0.92f, 0.94f, 1.00f);                         // starlight
            }
        }

        // ---------------------------------------------------------------- pitch

        /// <summary>A pitched onset's MIDI note (false: unpitched, or not a note).</summary>
        public static bool TryMidi(InstrumentKind kind, in TakeEvent e, out float midi)
        {
            midi = 0f;
            switch (kind)
            {
                case InstrumentKind.Piano:
                case InstrumentKind.Marimba:
                    if (e.midi <= 0 || e.midi > 127) return false;
                    midi = e.midi;
                    return true;
                case InstrumentKind.Harp:
                    if (e.i < 0 || e.i > 40) return false;
                    midi = 48 + 12 * (e.i / 7) + HarpDegrees[e.i % 7];
                    return true;
                case InstrumentKind.Handpan:
                    if ((uint)e.i >= (uint)HandpanMidi.Length) return false;   // the tak (the shell) is not a note
                    midi = HandpanMidi[e.i];
                    return true;
                case InstrumentKind.Bowls:
                    if ((uint)e.i >= (uint)BowlMidi.Length) return false;
                    midi = BowlMidi[e.i];
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>An unpitched onset's height 0..1 (the kit's pieces low to high; the hand drums: the low tumba under the
        /// conga, a bass stroke lower and a slap higher); -1 when it has none.</summary>
        public static float FixedHeight(InstrumentKind kind, in TakeEvent e)
        {
            switch (kind)
            {
                case InstrumentKind.Drums:
                    switch (e.kind)
                    {
                        case "kick": return 0.06f;
                        case "floor": return 0.2f;
                        case "tom2": return 0.32f;
                        case "tom1": return 0.42f;
                        case "snare": return 0.52f;
                        case "hat": case "hatOpen": case "hatClose": return 0.66f;
                        case "ride": return 0.8f;
                        case "crash": return 0.94f;
                        default: return 0.5f;
                    }
                case InstrumentKind.HandDrums:
                    float h = e.kind == "tumba" ? 0.3f : 0.55f;
                    if (e.zone == "bass") h -= 0.15f;
                    else if (e.zone == "slap") h += 0.2f;
                    else if (e.zone == "heel" || e.zone == "toe") h -= 0.08f;
                    return Mathf.Clamp01(h);
                case InstrumentKind.Handpan:
                    return 0.12f;   // the tak: a knock on the shell, low
                default:
                    return -1f;
            }
        }

        /// <summary>The pitch range a take's stars are laid out in: its lowest and highest pitched note, at least MinSpan
        /// apart (centred) with Pad either side. Default when it has no pitched note.</summary>
        public struct Range
        {
            public float Lo, Hi;
            public static Range Default => new Range { Lo = DefaultLo, Hi = DefaultHi };

            public static Range Of(float lo, float hi)
            {
                if (!(hi >= lo)) return Default;
                float span = hi - lo;
                if (span < MinSpan) { float c = (lo + hi) * 0.5f; lo = c - MinSpan * 0.5f; hi = c + MinSpan * 0.5f; }
                return new Range { Lo = lo - Pad, Hi = hi + Pad };
            }
        }

        /// <summary>The height 0..1 of an onset in a range (higher note, higher star); false when it is not a note.</summary>
        public static bool TryHeight(InstrumentKind kind, in TakeEvent e, in Range range, out float height)
        {
            if (TryMidi(kind, e, out float midi))
            {
                float span = range.Hi - range.Lo;
                height = span > 1e-3f ? Mathf.Clamp01((midi - range.Lo) / span) : 0.5f;
                return true;
            }
            height = FixedHeight(kind, e);
            return height >= 0f;
        }

        // ---------------------------------------------------------------- what each event type is

        /// <summary>Which instrument plays each event type and its role, from the registry's descriptors (the classic seven
        /// without one). Built once; lookups are a dictionary read.</summary>
        public sealed class Types
        {
            readonly Dictionary<string, (InstrumentKind kind, EventRole role)> _types = new Dictionary<string, (InstrumentKind, EventRole)>(StringComparer.Ordinal);

            public Types(IInstrumentRegistry registry)
            {
                if (registry != null)
                    foreach (InstrumentDescriptor d in registry.All)
                    {
                        if (d?.Events == null) continue;
                        foreach (EventTypeSpec s in d.Events)
                            if (!string.IsNullOrEmpty(s.Type) && !_types.ContainsKey(s.Type)) _types[s.Type] = (d.Kind, s.Role);
                    }
                Classic(TakeEventType.PianoOn, InstrumentKind.Piano, EventRole.Onset);
                Classic(TakeEventType.PianoOff, InstrumentKind.Piano, EventRole.Release);
                Classic(TakeEventType.Drum, InstrumentKind.Drums, EventRole.Onset);
                Classic(TakeEventType.DrumChoke, InstrumentKind.Drums, EventRole.Release);
                Classic(TakeEventType.Hat, InstrumentKind.Drums, EventRole.State);
                Classic(TakeEventType.Harp, InstrumentKind.Harp, EventRole.Onset);
                Classic(TakeEventType.HarpDamp, InstrumentKind.Harp, EventRole.Release);
            }

            void Classic(string type, InstrumentKind kind, EventRole role)
            {
                if (!_types.ContainsKey(type)) _types[type] = (kind, role);
            }

            /// <summary>A known onset type and its instrument.</summary>
            public bool IsOnset(string type, out InstrumentKind kind)
            {
                kind = default;
                if (type == null || !_types.TryGetValue(type, out var t) || t.role != EventRole.Onset) return false;
                kind = t.kind;
                return true;
            }
        }

        /// <summary>The take's own pitch range (its pitched onsets).</summary>
        public static Range RangeOf(List<TakeEvent> events, Types types)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            if (events != null && types != null)
                for (int i = 0; i < events.Count; i++)
                {
                    TakeEvent e = events[i];
                    if (!types.IsOnset(e.type, out InstrumentKind k) || !TryMidi(k, e, out float m)) continue;
                    if (m < lo) lo = m;
                    if (m > hi) hi = m;
                }
            return lo <= hi ? Range.Of(lo, hi) : Range.Default;
        }

        // ---------------------------------------------------------------- the row's picture

        /// <summary>A recording's constellation, normalised: x 0 (its start) .. 1 (its end), y 0 (low) .. 1 (high), a size
        /// 0..1 by loudness, its instrument, and the previous star of the same instrument it is joined to (-1: none).</summary>
        public sealed class Thumb
        {
            public int Count;
            public readonly Vector2[] Pos = new Vector2[ThumbMax];
            public readonly float[] Size = new float[ThumbMax];
            public readonly InstrumentKind[] Kind = new InstrumentKind[ThumbMax];
            public readonly int[] Prev = new int[ThumbMax];
            /// <summary>A fingerprint of the picture (tests: the same take draws the same picture).</summary>
            public uint Hash;
        }

        /// <summary>The picture of a take (a saved loop: its layers over one pass). Deterministic: the same events always give
        /// the same picture. A take with more onsets than ThumbMax shares the picture between its instruments by how much each
        /// plays (one star at least each) and keeps the first onset of each equal stretch of an instrument's onsets. Made
        /// once per take (it allocates a few small arrays).</summary>
        public static Thumb BuildThumb(List<TakeEvent> events, Types types, Thumb into = null)
        {
            var th = into ?? new Thumb();
            th.Count = 0;
            th.Hash = 2166136261u;
            if (events == null || types == null || events.Count == 0) return th;
            // a saved loop: its header's speed is the pass length; its marks are not notes
            bool loop = events[0].type == "loop";
            float pass = loop && events[0].speed > 0f ? events[0].speed : 0f;
            Range range = RangeOf(events, types);
            // each instrument's onsets, and its share of the picture (by how much it plays, at least one star each)
            var count = new int[KindSlots];
            int onsets = 0;
            float end = pass;
            for (int i = 0; i < events.Count; i++)
            {
                TakeEvent e = events[i];
                if (!types.IsOnset(e.type, out InstrumentKind k) || !TryHeight(k, e, range, out _)) continue;
                onsets++;
                count[Slot(k)]++;
                if (!loop && e.t > end) end = e.t;
            }
            if (onsets == 0) return th;
            if (!(end > 1e-3f)) end = 1f;
            var quota = new int[KindSlots];
            int total = 0;
            for (int k = 0; k < KindSlots; k++)
            {
                if (count[k] == 0) continue;
                quota[k] = Mathf.Clamp(Mathf.RoundToInt(ThumbMax * count[k] / (float)onsets), 1, count[k]);
                total += quota[k];
            }
            while (total > ThumbMax)
            {
                int big = 0;
                for (int k = 1; k < KindSlots; k++) if (quota[k] > quota[big]) big = k;
                quota[big]--;
                total--;
            }
            // per instrument: its onsets in equal stretches, the first of each (evenly through the song, so the tune keeps its
            // shape)
            var seen = new int[KindSlots];
            var bucket = new int[KindSlots];
            for (int k = 0; k < KindSlots; k++) bucket[k] = -1;
            for (int i = 0; i < events.Count; i++)
            {
                TakeEvent e = events[i];
                if (!types.IsOnset(e.type, out InstrumentKind kind) || !TryHeight(kind, e, range, out _)) continue;
                int k = Slot(kind);
                if (quota[k] <= 0) continue;
                int b = (int)((long)seen[k] * quota[k] / count[k]);
                seen[k]++;
                if (b == bucket[k]) continue;
                bucket[k] = b;
                Add(th, e, types, range, end);
            }
            SortByTime(th);
            // join each star to the one before it of the same instrument
            for (int i = 0; i < th.Count; i++)
            {
                th.Prev[i] = -1;
                for (int j = i - 1; j >= 0; j--) if (th.Kind[j] == th.Kind[i]) { th.Prev[i] = j; break; }
                th.Hash = Mix(th.Hash, th.Pos[i].x);
                th.Hash = Mix(th.Hash, th.Pos[i].y);
                th.Hash = Mix(th.Hash, th.Size[i]);
                th.Hash = (th.Hash ^ (uint)th.Kind[i]) * 16777619u;
                th.Hash = (th.Hash ^ (uint)(th.Prev[i] + 1)) * 16777619u;
            }
            return th;
        }

        const int KindSlots = 16;
        static int Slot(InstrumentKind k) => (uint)(int)k < KindSlots ? (int)k : KindSlots - 1;

        static void Add(Thumb th, in TakeEvent e, Types types, in Range range, float end)
        {
            if (th.Count >= ThumbMax || !types.IsOnset(e.type, out InstrumentKind k) || !TryHeight(k, e, range, out float h)) return;
            int i = th.Count++;
            th.Pos[i] = new Vector2(Mathf.Clamp01(e.t / end), h);
            float vel = e.vel > 0f ? Mathf.Clamp01(e.vel) : 0.6f;
            th.Size[i] = vel;
            th.Kind[i] = k;
        }

        static void SortByTime(Thumb th)
        {
            for (int i = 1; i < th.Count; i++)
            {
                Vector2 p = th.Pos[i]; float s = th.Size[i]; InstrumentKind k = th.Kind[i];
                int j = i - 1;
                while (j >= 0 && th.Pos[j].x > p.x) { th.Pos[j + 1] = th.Pos[j]; th.Size[j + 1] = th.Size[j]; th.Kind[j + 1] = th.Kind[j]; j--; }
                th.Pos[j + 1] = p; th.Size[j + 1] = s; th.Kind[j + 1] = k;
            }
        }

        static uint Mix(uint h, float v)
        {
            int q = Mathf.RoundToInt(v * 10000f);
            return (h ^ (uint)q) * 16777619u;
        }
    }
}
