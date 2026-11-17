// The looper's data: a session (tempo, length, up to six layers) and its layers (recorded events at sample offsets within
// one pass, sorted). Plain C#, no Unity objects. LoopFormat stores a session as an ordinary take (a TakeRecord whose
// events start with a "loop" header and one "layer" marker per layer), so the library, the menu's Recordings list and
// its Play row work unchanged (Recorder.PlayEvents recognises the header and loops it).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    /// <summary>One recorded event at its offset (samples) from the start of a pass. A Control sits at its dispatch offset
    /// (recorded offset minus ControlEncoder.DispatchLead(speed)), so a layer is always in dispatch order.</summary>
    public struct LoopNote
    {
        public long Offset;
        public TakeEvent Event;
    }

    /// <summary>A key a layer holds open (platform.md 6.4): while recording, a Held onset whose Release has not come yet, or a
    /// continuous lane (its onset or first Control seen, no Off yet) with the shift its onset got from the tidy; while
    /// playing, a Held onset or a lane the layer's playback has sounding. Release: the type that ends it; Control: the lane's
    /// Control type (lanes); Key / Int / Str: the key field and its value; Owner: the instrument (EventRoles.OwnerOf).</summary>
    internal struct OpenKey
    {
        public bool Lane;
        public int Owner;
        public string Release, Control;
        public EventKey Key;
        public int Int;
        public string Str;
        public long Shift;
        public float U;

        public bool Same(bool lane, int owner, EventKey key, int value, string text) =>
            Lane == lane && Owner == owner && Key == key && Int == value && string.Equals(Str, text, StringComparison.Ordinal);
    }

    public sealed class LoopLayer
    {
        /// <summary>Events of one layer, sorted by Offset (stable: equal offsets keep their capture order).</summary>
        public readonly List<LoopNote> Notes = new List<LoopNote>(LoopSession.LayerCapacity);
        public bool Muted;
        /// <summary>Capturing (its window is open or still ahead: an armed layer).</summary>
        public bool Recording;
        /// <summary>The first pass of the current run it plays in (it records the pass before).</summary>
        public long StartPass;
        /// <summary>The capture window (absolute samples on the audio clock, latency-compensated) and the pass its offsets
        /// count from (WindowStart = PassStart - early grace, or earlier for the first layer's first note).</summary>
        public long WindowStart, WindowEnd, PassStart;
        /// <summary>The Sampled State values (the hi-hat pedal) taken when its window opened; SampledAtStart: taken.</summary>
        public readonly List<TakeEvent> SampledStart = new List<TakeEvent>(2);
        public bool SampledAtStart;
        /// <summary>Its window has opened (LoopEvent Recording was raised).</summary>
        public bool Opened;

        // Keys this layer's playback holds (pressed piano keys, sounding lanes): released when the layer is muted, undone
        // or stopped, and a layer only releases what it pressed.
        internal readonly List<OpenKey> Held = new List<OpenKey>(16);
        // Recording: Held onsets whose release is not in yet and lanes still sounding (with the shift their onset got).
        internal readonly List<OpenKey> Open = new List<OpenKey>(16);

        public int Count => Notes.Count;
        /// <summary>The layer has an event of this type.</summary>
        public bool Has(string type)
        {
            for (int i = 0; i < Notes.Count; i++) if (string.Equals(Notes[i].Event.type, type, StringComparison.Ordinal)) return true;
            return false;
        }
        public bool HasHat => Has(TakeEventType.Hat);
        /// <summary>Keys / lanes the layer's playback holds now (tests).</summary>
        public int HeldCount => Held.Count;
        /// <summary>Held onsets / lanes still open while it records (tests).</summary>
        public int OpenCount => Open.Count;

        /// <summary>Insert keeping the order (after the events at the same offset). Returns false when the layer is full.</summary>
        public bool Insert(long offset, in TakeEvent e, bool first = false)
        {
            if (Notes.Count >= LoopSession.MaxLayerEvents) return false;
            int lo = 0, hi = Notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                long o = Notes[mid].Offset;
                if (first ? o < offset : o <= offset) lo = mid + 1; else hi = mid;
            }
            Notes.Insert(lo, new LoopNote { Offset = offset, Event = e });
            return true;
        }

        /// <summary>The first index whose offset is &gt;= offset.</summary>
        public int LowerBound(long offset)
        {
            int lo = 0, hi = Notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Notes[mid].Offset < offset) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        internal void ClearState()
        {
            Held.Clear();
            Open.Clear();
        }

        internal static int Find(List<OpenKey> keys, bool lane, int owner, EventKey key, int value, string text)
        {
            for (int i = 0; i < keys.Count; i++) if (keys[i].Same(lane, owner, key, value, text)) return i;
            return -1;
        }
    }

    public sealed class LoopSession
    {
        public const int BeatsPerBar = 4;
        public const int LayerCapacity = 256;
        /// <summary>Events one layer keeps (a busy pass is a few hundred; the rest are dropped).</summary>
        public const int MaxLayerEvents = 4000;

        public readonly int Bpm, Bars, SampleRate;
        /// <summary>One pass, a whole number of samples (bars x 4 beats at Bpm, rounded once): pass k starts exactly k x
        /// PassSamples after the anchor, so nothing ever drifts.</summary>
        public readonly long PassSamples;
        public readonly List<LoopLayer> Layers = new List<LoopLayer>(Looper.MaxLayers);
        /// <summary>The library record this session is saved as (null until the first save) and its number ("Loop N").</summary>
        public string Id;
        public int Number;
        /// <summary>Changed since the last save (a layer added, undone or muted).</summary>
        public bool Dirty;

        public LoopSession(int bpm, int bars, int sampleRate)
        {
            Bpm = bpm;
            Bars = bars;
            SampleRate = sampleRate;
            PassSamples = (long)Math.Round(bars * BeatsPerBar * 60.0 / bpm * sampleRate);
        }

        public int Beats => Bars * BeatsPerBar;
        /// <summary>Samples per beat (fractional; beat k of a pass is at round(k x BeatSamples)).</summary>
        public double BeatSamples => PassSamples / (double)Beats;
        public double BarSamples => PassSamples / (double)Bars;
        public double PassSeconds => PassSamples / (double)SampleRate;

        /// <summary>Layers whose recording finished.</summary>
        public int Finished { get { int n = 0; foreach (var l in Layers) if (!l.Recording) n++; return n; } }

        /// <summary>Recorded events over all finished layers.</summary>
        public int EventCount { get { int n = 0; foreach (var l in Layers) if (!l.Recording) n += l.Notes.Count; return n; } }

        public long BeatOffset(int k) => (long)Math.Round(k * BeatSamples);
    }

    /// <summary>A loop session as take events (lossless through TakeLibrary's JsonUtility files):
    ///   [0] {type "loop", midi = bpm, i = bars, u = 4 (beats per bar), speed = pass seconds, mode = record id, pos = number,
    ///   kind = "v1"}
    ///   then per layer {type "layer", i = index, open = muted} followed by its events with t = offset in seconds (sorted).
    /// Old builds without the looper would read it as a take of overlapping parts; this build loops it.</summary>
    public static class LoopFormat
    {
        public const string Header = "loop", LayerMark = "layer", Version = "v1";

        public static bool IsLoop(List<TakeEvent> events) => events != null && events.Count > 0 && events[0].type == Header;

        public static List<TakeEvent> Encode(LoopSession s)
        {
            int n = 1;
            foreach (var l in s.Layers) if (!l.Recording) n += 1 + l.Notes.Count;
            var list = new List<TakeEvent>(n);
            list.Add(new TakeEvent
            {
                type = Header, midi = s.Bpm, i = s.Bars, u = LoopSession.BeatsPerBar, speed = (float)s.PassSeconds,
                mode = s.Id ?? "", kind = Version, pos = s.Number,
            });
            int index = 0;
            foreach (var l in s.Layers)
            {
                if (l.Recording) continue;
                list.Add(new TakeEvent { type = LayerMark, i = index++, open = l.Muted });
                for (int k = 0; k < l.Notes.Count; k++)
                {
                    TakeEvent e = l.Notes[k].Event;
                    e.t = (float)(l.Notes[k].Offset / (double)s.SampleRate);
                    list.Add(e);
                }
            }
            return list;
        }

        /// <summary>The session in a saved take (null when it is not one). Offsets are re-quantised to this device's sample rate.</summary>
        public static LoopSession Decode(List<TakeEvent> events, int sampleRate)
        {
            if (!IsLoop(events)) return null;
            TakeEvent h = events[0];
            int bpm = h.midi > 0 ? h.midi : 90, bars = h.i > 0 ? h.i : 2;
            var s = new LoopSession(bpm, bars, sampleRate) { Id = string.IsNullOrEmpty(h.mode) ? null : h.mode, Number = (int)h.pos };
            LoopLayer layer = null;
            for (int k = 1; k < events.Count; k++)
            {
                TakeEvent e = events[k];
                if (e.type == LayerMark)
                {
                    if (s.Layers.Count >= Looper.MaxLayers) break;
                    layer = new LoopLayer { Muted = e.open };
                    s.Layers.Add(layer);
                    continue;
                }
                if (layer == null) continue;
                long off = (long)Math.Round(e.t * (double)sampleRate);
                if (off < 0) off = 0;
                if (off >= s.PassSamples) off = s.PassSamples - 1;
                e.t = 0f;
                layer.Insert(off, e);
            }
            return s;
        }

        /// <summary>The library name: "Loop N · 90 BPM · 3 layers" (U+00B7 between spaces, like the takes).</summary>
        public static string Name(int number, int bpm, int layers) =>
            $"Loop {number} · {bpm} BPM · {layers} {(layers == 1 ? "layer" : "layers")}";
    }
}
