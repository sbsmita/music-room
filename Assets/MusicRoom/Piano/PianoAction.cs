// The piano's playing logic (spec 05 sections 2.3-2.6): where a press may
// start, following a finger on a key, the escapement, strike velocity, glissando, repetition, the damper, automatic
// sustain, the key spring and the hover glows. Plain C# (no MonoBehaviour) so EditMode tests drive it with synthetic
// pointers and a scripted Clock; PianoInstrument owns one and feeds it the frame's pointers in the Instruments phase.
// All geometry is in the Unity piano-local frame (x right, y up, white tops y = 0, z = back = distance behind the
// white-key fronts), so every predicate the spec writes with right-handed z = -back is converted once, here.
// Hot paths (Tick, Hover, the press API) allocate nothing after warm-up: fingertips keep their state, API pressers borrow
// a pooled entry only while they hold a key.
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Piano
{
    /// <summary>Where the logic sends its sound and its play events (the instrument: IAudioEngine + IRecorder; tests: a fake).</summary>
    public interface IPianoSink
    {
        /// <summary>A key went down: start its note (IAudioEngine.Piano).</summary>
        VoiceHandle NoteOn(int midi, float vel);
        /// <summary>A note is released (IAudioEngine.Release); thud = the damper noise (only the first of a pedal lift).</summary>
        void NoteOff(int midi, VoiceHandle voice, bool thud);
        /// <summary>pianoOn / pianoOff for the recorder and its observers (lessons, Sunny).</summary>
        void Record(in TakeEvent ev);
    }

    /// <summary>One key of the 88.</summary>
    public sealed class PianoKey
    {
        public int Midi, Index, Inst;              // Index in Keys; Inst within Whites / Blacks
        public bool Black;
        public float X;
        public float Depth, V;                      // 0 rest .. 1 felt, and the spring's velocity (depth / s)
        public float Finger;                        // the deepest contact depth this frame
        public int FingerFrame = -1;
        public bool DamperPending;
        public readonly List<object> PressedBy = new List<object>(4);
        /// <summary>Set semantics of pressedBy (presser identity by Equals) without List.Contains / Remove, whose
        /// EqualityComparer&lt;T&gt;.Default is created lazily on first use (a GC allocation in the middle of play).</summary>
        public bool IsPressedBy(object p)
        {
            for (int i = 0; i < PressedBy.Count; i++) if (Equals(PressedBy[i], p)) return true;
            return false;
        }
        public void AddPresser(object p) { if (!IsPressedBy(p)) PressedBy.Add(p); }
        public void RemovePresser(object p)
        {
            for (int i = 0; i < PressedBy.Count; i++) if (Equals(PressedBy[i], p)) { PressedBy.RemoveAt(i); return; }
        }
        public bool HasVoice;                       // the key has a voice (a struck note that has not been released yet)
        public VoiceHandle Voice;
        public float LastVel;
        public double RingSince;
        public bool Held, Sustained;
        /// <summary>Set when Depth changed; the view rewrites the key's vertices and clears it.</summary>
        public bool Dirty;
        public PianoKey[] BlacksNear;               // whites only: the blacks within one pitch
        public float MaxAngle => PianoSpec.MaxAngle(Black);
        public float Top => Black ? PianoSpec.BTOP : 0f;
        /// <summary>1 - DARKEN * depth: multiplies the key and its label (writeKey).</summary>
        public float Shade => 1f - PianoSpec.Darken(Black) * Depth;
        public override string ToString() => $"key {Midi}";
    }

    public sealed class PianoAction
    {
        /// <summary>A fingertip's contact with the keys ({key, s, hist}).</summary>
        public sealed class Contact
        {
            public PianoKey Key;
            public float S;
            public readonly History Hist = new History();
        }

        /// <summary>The fingertip's recent key-local heights on the piano clock.</summary>
        public sealed class History
        {
            public const int Capacity = 16;
            public readonly double[] T = new double[Capacity];
            public readonly Vector3[] P = new Vector3[Capacity];
            public int Count;
            public void Clear() => Count = 0;
            public void Push(double t, Vector3 p)
            {
                if (Count == Capacity) Shift();
                T[Count] = t;
                P[Count] = p;
                Count++;
            }
            public void Shift()
            {
                for (int i = 1; i < Count; i++) { T[i - 1] = T[i]; P[i - 1] = P[i]; }
                Count--;
            }
        }

        /// <summary>Per presser object (fingertip pointer, computer key, mouse, playback ghost): its state + contact.</summary>
        public sealed class Presser
        {
            public PianoKey Pressing;   // the key this presser struck
            public Contact Contact;     // fingertips only
        }

        public readonly PianoKey[] Keys = new PianoKey[PianoSpec.KeyCount];
        public readonly PianoKey[] Whites = new PianoKey[PianoSpec.WhiteCount];
        public readonly PianoKey[] Blacks = new PianoKey[PianoSpec.BlackCount];
        /// <summary>The playback presser (the ghost): recorder takes hold keys down exactly as the player did.</summary>
        public readonly object Ghost = new GhostPresser();

        readonly Clock _clock;
        IPianoSink _sink;
        readonly Dictionary<object, Presser> _pressers = new Dictionary<object, Presser>(32);
        // Presser state of an API presser (computer key, mouse, playback ghost, a caller's own object) lives only while it
        // holds a key, then goes back to this pool: a caller passing a fresh object per note never grows the table, and the
        // press path allocates nothing once the pool has warmed up. Fingertips keep theirs (contact and history persist).
        readonly Stack<Presser> _presserPool = new Stack<Presser>(16);
        readonly List<object> _dropBuffer = new List<object>(16);
        readonly List<PianoKey> _sustained = new List<PianoKey>(PianoSpec.KeyCount);
        double _pianoClock;      // the piano clock: sum of the dt the piano was ticked with (fingertip history)
        int _frame;
        int _held;               // keys held down
        double _lastRelease = double.NegativeInfinity;

        sealed class GhostPresser { public override string ToString() => "playback"; }

        public PianoAction(Clock clock, IPianoSink sink)
        {
            _clock = clock;
            _sink = sink;
            int white = 0, black = 0;
            for (int m = PianoSpec.FirstMidi; m <= PianoSpec.LastMidi; m++)
            {
                bool isBlack = PianoSpec.IsBlack(m);
                var key = new PianoKey { Midi = m, Index = m - PianoSpec.FirstMidi, Black = isBlack };
                if (isBlack)
                {
                    key.X = PianoSpec.X0 + white * PianoSpec.W + PianoSpec.BlackOffset(m % 12);
                    key.Inst = black;
                    Blacks[black++] = key;
                }
                else
                {
                    key.X = PianoSpec.X0 + (white + 0.5f) * PianoSpec.W;
                    key.Inst = white;
                    Whites[white++] = key;
                }
                Keys[key.Index] = key;
            }
            // blacksNear[i]: the blacks whose centre is within one pitch of white i.
            var near = new List<PianoKey>(3);
            foreach (var w in Whites)
            {
                near.Clear();
                foreach (var b in Blacks) if (Mathf.Abs(b.X - w.X) < PianoSpec.W) near.Add(b);
                w.BlacksNear = near.ToArray();
            }
        }

        public void SetSink(IPianoSink sink) => _sink = sink;

        public int HeldKeyCount => _held;
        public int SustainedCount => _sustained.Count;
        public int Frame => _frame;
        public double LastRelease => _lastRelease;

        public PianoKey KeyByMidi(int midi) => midi < PianoSpec.FirstMidi || midi > PianoSpec.LastMidi ? null : Keys[midi - PianoSpec.FirstMidi];

        public Presser PresserOf(object p) => p != null && _pressers.TryGetValue(p, out var s) ? s : null;
        /// <summary>Pressers with state: the fingertips seen so far plus the API pressers holding a key right now.</summary>
        public int PresserCount => _pressers.Count;

        Presser State(object p)
        {
            if (!_pressers.TryGetValue(p, out var s))
            {
                s = _presserPool.Count > 0 ? _presserPool.Pop() : new Presser();
                _pressers.Add(p, s);
            }
            return s;
        }

        /// <summary>Forgets an API presser that holds nothing any more (its entry goes back to the pool).</summary>
        void DropIfIdle(object p, Presser s)
        {
            if (s.Contact != null || s.Pressing != null) return;
            _pressers.Remove(p);
            _presserPool.Push(s);
        }

        // ================= geometry predicates, back = Unity z =================

        static int WhiteIndex(float lx) => Mathf.FloorToInt((lx - PianoSpec.X0) / PianoSpec.W);

        /// <summary>The key under a point by the full pitch: a finger already on the keys sliding to a neighbour.</summary>
        public PianoKey KeyAt(float lx, float back)
        {
            int i = WhiteIndex(lx);
            if (i < 0 || i >= Whites.Length) return null;
            if (back > PianoSpec.L - PianoSpec.BL)
                foreach (var b in Whites[i].BlacksNear) if (Mathf.Abs(lx - b.X) < PianoSpec.BW / 2f + 0.0015f) return b;
            return Whites[i];
        }

        /// <summary>10-upgrade/experience.md 2.1 (E1 / E3, visual only): the key whose top a fingertip at piano-local
        /// <paramref name="l"/> rests on, from KeyAt (the function the contact logic slides with) widened by 2 mm, back in
        /// [0, L], with that top at the key's CURRENT depth: a plane point and normal (piano-local) and how far the top
        /// under the point has gone down. A point beside a black key's visible width and below its top is on the white key
        /// there (it came down onto the white beside the black), never lifted onto the black. Null outside every key.</summary>
        public PianoKey ContactKey(Vector3 l, out Vector3 point, out Vector3 normal, out float drop)
        {
            point = normal = Vector3.zero;
            drop = 0f;
            if (l.z < 0f || l.z > PianoSpec.L) return null;
            PianoKey key = KeyAt(l.x, l.z);
            if (key == null) return null;
            if (key.Black && Mathf.Abs(l.x - key.X) > PianoSpec.BW / 2f && l.y < KeyTopY(key, l.z)) key = Whites[WhiteIndex(l.x)];
            float half = key.Black ? PianoSpec.BW / 2f : PianoSpec.WW / 2f;
            if (Mathf.Abs(l.x - key.X) > half + ContactWiden) return null;
            float a = key.Depth * key.MaxAngle, c = Mathf.Cos(a), s = Mathf.Sin(a), zr = PianoSpec.L - l.z, top = key.Top;
            // The key's transform (PianoKeysView): pivot (x, 0, L), Euler(-a, 0, 0): (0, y, z) -> (0, c y + s z, -s y + c z).
            point = new Vector3(l.x, c * top - s * zr, PianoSpec.L - s * top - c * zr);
            normal = new Vector3(0f, c, -s);
            drop = top - point.y;
            return key;
        }

        /// <summary>The 2 mm a contact footprint is widened by (E1 / E3).</summary>
        public const float ContactWiden = 0.002f;

        /// <summary>A key's top height at a distance behind the fronts, at its current depth (piano-local y).</summary>
        public static float KeyTopY(PianoKey key, float back)
        {
            float a = key.Depth * key.MaxAngle;
            return Mathf.Cos(a) * key.Top - Mathf.Sin(a) * (PianoSpec.L - back);
        }

        /// <summary>Inside a black key's top, shrunk by inset (or grown, for a negative inset).</summary>
        public static bool OnBlackTop(PianoKey b, float lx, float back, float inset) =>
            Mathf.Abs(lx - b.X) < PianoSpec.BW / 2f - inset && back < PianoSpec.L - inset && back > (PianoSpec.L - PianoSpec.BL) + inset;

        /// <summary>The key whose top, shrunk by the start inset, holds the point: a black key's own top, or a white key's
        /// visible top (the full key in front of the blacks, the part between them behind), never a gap or the case.</summary>
        public PianoKey KeyTopAt(float lx, float back, bool black, float inset = PianoSpec.START_INSET)
        {
            int i = WhiteIndex(lx);
            if (i < 0 || i >= Whites.Length) return null;
            var near = Whites[i].BlacksNear;
            if (black)
            {
                foreach (var b in near) if (OnBlackTop(b, lx, back, inset)) return b;
                return null;
            }
            var w = Whites[i];
            if (Mathf.Abs(lx - w.X) > PianoSpec.WW / 2f - inset || back < inset || back > PianoSpec.L - inset) return null;
            foreach (var b in near) if (OnBlackTop(b, lx, back, -inset)) return null;
            return w;
        }

        /// <summary>Is a local point still within this key's footprint, widened by a margin (the hold once a finger is on it)?</summary>
        public static bool WithinKey(PianoKey key, float lx, float back, float margin)
        {
            if (key.Black)
                return Mathf.Abs(lx - key.X) < PianoSpec.BW / 2f + margin && back < PianoSpec.L + margin && back > (PianoSpec.L - PianoSpec.BL) - margin;
            return Mathf.Abs(lx - key.X) < PianoSpec.WW / 2f + margin && back < PianoSpec.L + margin && back > -margin;
        }

        /// <summary>Key depth per metre the fingertip sinks below the key top at this distance behind the fronts: the key
        /// turns about its hinge, so a finger further back moves it further (down to LEVER_MIN from the hinge).</summary>
        public static float DepthPerMetre(PianoKey k, float back)
        {
            float front = k.Black ? PianoSpec.L - PianoSpec.BL : 0f;
            float lever = Mathf.Max(PianoSpec.L - Mathf.Max(back, front), PianoSpec.LEVER_MIN);
            return 1f / (lever * k.MaxAngle);
        }

        /// <summary>How far a fingertip at local (y, back) pushes this key down: 0 at rest, 1 on the felt.</summary>
        public static float FingerDepth(PianoKey k, float y, float back) => (k.Top - y) * DepthPerMetre(k, back);

        /// <summary>The key a fingertip lands on this frame, coming down through a key top inside the start inset (black tops
        /// first). Arriving any other way (through a front, from below, a side, a gap, the case) lands on nothing.</summary>
        public PianoKey EntryKey(Vector3 a, Vector3 b)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool black = pass == 0;
                float top = black ? PianoSpec.BTOP : 0f;
                if (!(a.y > top && b.y <= top)) continue;
                float t = (a.y - top) / (a.y - b.y);
                float x = a.x + (b.x - a.x) * t, z = a.z + (b.z - a.z) * t;
                var key = KeyTopAt(x, z, black);
                if (key != null) return key;
            }
            return null;
        }

        /// <summary>inPlayZone, piano-local: over the keyboard or the working space in front of and above it.</summary>
        public static bool InPlayZoneLocal(Vector3 l) =>
            l.x > PianoSpec.X0 - 0.03f && l.x < -PianoSpec.X0 + 0.03f && l.z > -0.30f && l.z < PianoSpec.L + 0.06f && l.y > -0.15f && l.y < 0.28f;

        /// <summary>overKeys: not on the apron 12 cm or more in front of the keys, and above their bottoms.</summary>
        public static bool OverKeysLocal(Vector3 l) => l.z > -0.12f && l.y > -0.02f;

        /// <summary>pianoKeySpace: inside the play zone above the bottom of the keys.</summary>
        public static bool KeySpaceLocal(Vector3 l) => InPlayZoneLocal(l) && l.y > -0.05f;

        // ================= notes and automatic sustain =================

        /// <summary>A key goes down: a new phrase lifts the automatic pedal first; the key's old voice is released.</summary>
        public void PressKey(PianoKey key, object p, IPointerOwner owner, float vel)
        {
            double now = _clock.Now;
            if (_held == 0 && now - _lastRelease > PianoSpec.PHRASE_GAP) Damp();
            key.AddPresser(p);
            key.DamperPending = false;
            if (!key.Held) { key.Held = true; _held++; }
            State(p).Pressing = key;
            if (owner != null) owner.LastPlayMs = _clock.NowMs;
            // a re-strike releases the old note first (its own damper thud)
            if (key.HasVoice) _sink?.NoteOff(key.Midi, key.Voice, true);
            RemoveSustained(key);
            key.Voice = _sink != null ? _sink.NoteOn(key.Midi, vel) : default;
            key.HasVoice = true;
            key.LastVel = vel;
            _sink?.Record(TakeEvent.PianoOn(key.Midi, vel));
        }

        /// <summary>A finger lets go of a key: it starts back up and the damper meets the string 70 % of the way up.</summary>
        public void FingerUp(PianoKey key, object p)
        {
            key.RemovePresser(p);
            if (_pressers.TryGetValue(p, out var s))
            {
                if (s.Pressing == key) s.Pressing = null;
                DropIfIdle(p, s);
            }
            if (key.PressedBy.Count == 0 && key.Held) key.DamperPending = true;
        }

        /// <summary>Release from the mouse, the computer keyboard, a playback or a lesson: the damper falls at once.</summary>
        public void ReleaseKey(PianoKey key, object p)
        {
            FingerUp(key, p);
            if (key.PressedBy.Count == 0 && key.Held) Damper(key);
        }

        /// <summary>The key is back up far enough: the note is released and, the pedal being down, rings on until the next phrase.</summary>
        public void Damper(PianoKey key)
        {
            key.DamperPending = false;
            if (key.Held) { key.Held = false; _held--; }
            _lastRelease = _clock.Now;
            if (key.HasVoice)
            {
                _sink?.Record(TakeEvent.PianoOff(key.Midi));
                key.RingSince = _lastRelease;
                AddSustained(key);
            }
        }

        /// <summary>The automatic pedal lifts: every released note is damped, with one damper thud, not one per note.</summary>
        public void Damp()
        {
            bool first = true;
            for (int i = 0; i < _sustained.Count; i++)
            {
                var key = _sustained[i];
                if (key.PressedBy.Count > 0 || !key.HasVoice) continue;
                _sink?.NoteOff(key.Midi, key.Voice, first);
                first = false;
                key.HasVoice = false;
                key.Voice = default;
            }
            for (int i = 0; i < _sustained.Count; i++) _sustained[i].Sustained = false;
            _sustained.Clear();
        }

        void AddSustained(PianoKey key)
        {
            if (key.Sustained) return;
            key.Sustained = true;
            _sustained.Add(key);
        }

        void RemoveSustained(PianoKey key)
        {
            if (!key.Sustained) return;
            key.Sustained = false;
            // Order-preserving (the first released note keeps the pedal's thud in Damp), by reference, no comparer.
            for (int i = 0; i < _sustained.Count; i++) if (ReferenceEquals(_sustained[i], key)) { _sustained.RemoveAt(i); return; }
        }

        // ================= the frame =================

        /// <summary>One frame of contacts, the sustain timers and the key motion. worldToLocal maps world points into the
        /// piano's frame (its InverseTransformPoint, scale included).</summary>
        public void Tick(IReadOnlyList<Pointer> pointers, float dt, in Matrix4x4 worldToLocal)
        {
            _pianoClock += dt;
            _frame++;
            double now = _pianoClock;
            int n = pointers != null ? pointers.Count : 0;
            for (int i = 0; i < n; i++)
            {
                Pointer p = pointers[i];
                _pressers.TryGetValue(p, out var st);
                // The desktop mouse hand is a visual stand-in (ARCHITECTURE section 0): its LMB poke would cross the key top
                // right after PianoDesktop played the same click, so its fingertips never strike (handled as inactive).
                if (!p.Active || p.FromMouse)
                {
                    if (st != null)
                    {
                        if (st.Contact != null) Leave(p, st.Contact);
                        else if (st.Pressing != null) FingerUp(st.Pressing, p);
                    }
                    continue;
                }
                if (st == null) { st = new Presser(); _pressers.Add(p, st); }
                if (st.Contact == null) st.Contact = new Contact();
                Contact c = st.Contact;
                Vector3 tip = worldToLocal.MultiplyPoint3x4(p.Tip);
                Vector3 prev = worldToLocal.MultiplyPoint3x4(p.Prev);
                float sdt = Mathf.Max(p.SampleDt > 0f ? p.SampleDt : dt, 1e-3f);
                Track(c, prev, tip, sdt, now);
                if (c.Key != null) Follow(p, c, prev, tip, sdt);
                if (c.Key == null)
                {
                    var key = EntryKey(prev, tip);
                    if (key != null)
                    {
                        c.Key = key;
                        Follow(p, c, prev, tip, sdt);
                    }
                }
                if (c.Key != null)
                {
                    var k = c.Key;
                    if (k.FingerFrame != _frame) { k.FingerFrame = _frame; k.Finger = 0f; }
                    k.Finger = Mathf.Max(k.Finger, c.S);
                }
            }
            // Ringing notes fall silent once they have had their time, the pedal being only automatic (wall time).
            if (_sustained.Count > 0)
            {
                double t = _clock.Now;
                for (int i = 0; i < _sustained.Count;)
                {
                    var key = _sustained[i];
                    if (key.PressedBy.Count > 0) { key.Sustained = false; _sustained.RemoveAt(i); continue; }
                    if (t - key.RingSince > PianoSpec.MAX_RING)
                    {
                        if (key.HasVoice) { _sink?.NoteOff(key.Midi, key.Voice, true); key.HasVoice = false; key.Voice = default; }
                        key.Sustained = false;
                        _sustained.RemoveAt(i);
                        continue;
                    }
                    i++;
                }
            }
            AnimateKeys(dt);
        }

        /// <summary>A pointer seen for the first time, or one that jumped since its last sample, starts over from where it was
        /// one sample ago; one entry per new sample, at most 12, none older than 0.1 s beyond the second.</summary>
        static void Track(Contact c, Vector3 a, Vector3 b, float sdt, double now)
        {
            var h = c.Hist;
            if (h.Count == 0)
                h.Push(now - sdt, a);
            else
            {
                Vector3 last = h.P[h.Count - 1];
                if (Mathf.Abs(last.y - a.y) > 1e-4f || Mathf.Abs(last.x - a.x) > 1e-4f || Mathf.Abs(last.z - a.z) > 1e-4f)
                {
                    h.Clear();
                    h.Push(now - sdt, a);
                }
            }
            Vector3 end = h.P[h.Count - 1];
            if (end.x != b.x || end.y != b.y || end.z != b.z) h.Push(now, b);
            while (h.Count > 2 && (h.Count > 12 || h.T[1] < now - 0.1)) h.Shift();
        }

        /// <summary>Downward speed (m/s) of the fingertip over the last VEL_WINDOW: its height then, between samples, against
        /// its height now.</summary>
        public static float DownSpeed(History h, float sdt)
        {
            int n = h.Count;
            if (n < 2) return 0f;
            double endT = h.T[n - 1];
            float endY = h.P[n - 1].y;
            double t0 = endT - PianoSpec.VEL_WINDOW;
            int i = n - 2;
            while (i > 0 && h.T[i] > t0) i--;
            double y0 = h.P[i].y, tt = h.T[i];
            if (h.T[i] < t0 && i + 1 < n - 1)
            {
                double f = (t0 - h.T[i]) / (h.T[i + 1] - h.T[i]);
                y0 = h.P[i].y + (h.P[i + 1].y - h.P[i].y) * f;
                tt = t0;
            }
            double span = endT - tt;
            return (float)(span > 1e-4 ? (y0 - endY) / span : (h.P[n - 2].y - endY) / sdt);
        }

        /// <summary>A finger on a key: follow it, strike at the escapement, let go when it rises, slide on to a neighbour (a
        /// glissando) or come off.</summary>
        void Follow(Pointer p, Contact c, Vector3 a, Vector3 b, float sdt)
        {
            var key = c.Key;
            if (!WithinKey(key, b.x, b.z, PianoSpec.HOLD_MARGIN))
            {
                bool struck = key.IsPressedBy(p);
                PianoKey next = b.z > -0.002f && b.z < PianoSpec.L ? KeyAt(b.x, b.z) : null;
                // Sliding along the keys onto a neighbour of the same height, still pressing into it.
                if (next != null && next != key && next.Black == key.Black && FingerDepth(next, b.y, b.z) > 0f)
                {
                    if (struck) FingerUp(key, p);
                    c.Key = next;
                    c.S = FingerDepth(next, b.y, b.z);
                    if (struck && c.S >= PianoSpec.S_ESC && p.Speed > PianoSpec.GlissSpeed)
                        PressKey(next, p, p.Owner, Mathf.Clamp(Mathf.Pow(p.Speed / 2f, 0.8f), 0.2f, 0.9f));
                    return;
                }
                Leave(p, c);
                return;
            }
            float s = FingerDepth(key, b.y, b.z);
            if (s < 0f)
            {
                Leave(p, c);
                return;
            }
            float s0 = FingerDepth(key, a.y, a.z);
            c.S = s;
            if (!key.IsPressedBy(p))
            {
                if (s0 >= PianoSpec.S_ESC) return;                 // still past the escapement from an earlier stroke
                float v = DownSpeed(c.Hist, sdt);
                if (v < PianoSpec.MIN_PRESS_SPEED) return;        // a finger settling onto a key, not a press
                bool crossed = s >= PianoSpec.S_ESC;
                // Sparse tracking: where the stroke will be by the next pose, at this speed.
                bool early = !crossed && sdt >= PianoSpec.SPARSE_DT && s >= PianoSpec.EARLY_S && v >= PianoSpec.FAST_PRESS_SPEED
                             && s + v * sdt * DepthPerMetre(key, b.z) >= PianoSpec.S_ESC;
                if (crossed || early) PressKey(key, p, p.Owner, PianoSpec.StrikeVelocity(v));
            }
            else if (s < PianoSpec.S_RESET)
                FingerUp(key, p);                                 // repetition: half travel re-arms the key
        }

        /// <summary>The finger comes off its key.</summary>
        void Leave(object p, Contact c)
        {
            if (c.Key != null && c.Key.IsPressedBy(p)) FingerUp(c.Key, p);
            c.Key = null;
            c.S = 0f;
        }

        /// <summary>Under a finger the key is where the finger puts it, at once; pressed from the mouse, the keyboard or a
        /// playback it goes down in PRESS_TIME; free, the spring brings it back up. The damper falls 70 % of the way up.</summary>
        void AnimateKeys(float dt)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                var k = Keys[i];
                bool fingered = k.FingerFrame == _frame;
                bool driven = !fingered && k.PressedBy.Count > 0;
                if (!fingered && !driven && k.Depth == 0f && k.V == 0f)
                {
                    if (k.DamperPending) Damper(k);
                    continue;
                }
                float d0 = k.Depth;
                if (driven)
                {
                    k.Depth = Mathf.Min(1f, k.Depth + dt / PianoSpec.PRESS_TIME);
                    k.V = 0f;
                }
                else
                {
                    if (k.Depth > 0f || k.V != 0f) PianoSpec.Spring(ref k.Depth, ref k.V, dt);
                    if (fingered)
                    {
                        float d = PianoSpec.KeyShape(k.Finger, PianoSpec.S_ESC);
                        if (d >= k.Depth) { k.Depth = d; k.V = 0f; }
                    }
                }
                if (k.DamperPending && k.PressedBy.Count == 0 && k.Depth <= PianoSpec.S_DAMPER) Damper(k);
                if (k.Depth != d0) k.Dirty = true;
            }
        }

        // ================= hover =================

        /// <summary>A faint steady glow on the key each free fingertip hovers just above (only over a key top where a press
        /// would start, never once the finger is on a key or the key is moving), kept behind the printed name.</summary>
        public int Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow, in Matrix4x4 worldToLocal, in Matrix4x4 localToWorld)
        {
            int added = 0;
            int n = pointers != null ? pointers.Count : 0;
            for (int i = 0; i < n; i++)
            {
                Pointer p = pointers[i];
                if (!p.Active) continue;
                if (_pressers.TryGetValue(p, out var st) && st.Contact != null && st.Contact.Key != null) continue;
                if (HoverSpec(worldToLocal.MultiplyPoint3x4(p.Tip), localToWorld, out var spec))
                {
                    if (glow == null || glow.Add(spec)) added++;
                }
            }
            return added;
        }

        /// <summary>The glow for a fingertip at a piano-local point, if any (the hover rule without the pointer bookkeeping).</summary>
        public bool HoverSpec(Vector3 tip, in Matrix4x4 localToWorld, out HoverGlowSpec spec)
        {
            spec = default;
            PianoKey key = tip.y > PianoSpec.BTOP ? KeyTopAt(tip.x, tip.z, true) : null;
            if (key == null && tip.y > 0f) key = KeyTopAt(tip.x, tip.z, false);
            if (key == null || key.Depth > 0.02f || key.PressedBy.Count > 0) return false;
            float top = key.Top;
            float h = tip.y - top;
            if (h > PianoSpec.HoverHeight) return false;
            float back = Mathf.Min(PianoSpec.L - 0.03f, Mathf.Max(key.Black ? 0.1f : 0.045f, tip.z));
            Vector3 pos = localToWorld.MultiplyPoint3x4(new Vector3(key.X, top + 0.001f, back));
            Vector3 normal = localToWorld.MultiplyVector(Vector3.up).normalized;
            Vector3 axis = localToWorld.MultiplyVector(Vector3.forward).normalized;
            spec = HoverGlowSpec.At(pos, normal, PianoSpec.HoverLength, key.Black ? PianoSpec.HoverWidthBlack : PianoSpec.HoverWidthWhite,
                1f - Mathf.Max(0f, h) / PianoSpec.HoverHeight, PianoSpec.HoverGain, true);
            spec.Axis = axis;
            return true;
        }

        // ================= the API =================

        /// <summary>Computer keyboard / desktop: one presser object per physical key so chords hold.</summary>
        public void PressMidi(int midi, object p, float vel = PianoSpec.KeyboardVelocity)
        {
            var key = KeyByMidi(midi);
            if (key != null && p != null && !key.IsPressedBy(p)) PressKey(key, p, null, vel);
        }

        public void ReleaseMidi(int midi, object p)
        {
            var key = KeyByMidi(midi);
            if (key != null && p != null && key.IsPressedBy(p)) ReleaseKey(key, p);
        }

        /// <summary>Recorder playback: the ghost presser holds the key down exactly as the player did (a repeat re-strikes).</summary>
        public void PlaybackOn(int midi, float vel)
        {
            var key = KeyByMidi(midi);
            if (key != null) PressKey(key, Ghost, null, vel);
        }

        public void PlaybackOff(int midi)
        {
            var key = KeyByMidi(midi);
            if (key != null && key.IsPressedBy(Ghost)) ReleaseKey(key, Ghost);
        }

        public void PlaybackReleaseAll()
        {
            for (int i = 0; i < Keys.Length; i++) if (Keys[i].IsPressedBy(Ghost)) ReleaseKey(Keys[i], Ghost);
            Damp();
        }

        /// <summary>Desktop click on a key: pressed at 0.7.</summary>
        public void DesktopDown(PianoKey key, object p)
        {
            if (key != null) PressKey(key, p, null, PianoSpec.ClickVelocity);
        }

        /// <summary>Dragging across the keys plays each one in turn: a glissando at 0.6.</summary>
        public void DesktopMove(PianoKey key, object p)
        {
            var held = PresserOf(p)?.Pressing;
            if (key == null || key == held) return;
            if (held != null) ReleaseKey(held, p);
            PressKey(key, p, null, PianoSpec.DragVelocity);
        }

        public void DesktopUp(object p)
        {
            var held = PresserOf(p)?.Pressing;
            if (held != null) ReleaseKey(held, p);
        }

        /// <summary>"Move instruments" or MR placing started: Core stops ticking the piano, so the fingertips holding keys
        /// could never lift them. Every key a hand (or the mouse, or a computer key) holds is let go now, as on a release:
        /// the damper falls and a pedalled note rings on. The playback ghost keeps its keys (a take plays on).</summary>
        public void ReleaseFingers()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                PianoKey k = Keys[i];
                for (int j = k.PressedBy.Count - 1; j >= 0; j--)
                {
                    if (j >= k.PressedBy.Count) continue;
                    object p = k.PressedBy[j];
                    if (!ReferenceEquals(p, Ghost)) ReleaseKey(k, p);
                }
            }
            foreach (var kv in _pressers)
                if (kv.Value.Contact != null) { kv.Value.Contact.Key = null; kv.Value.Contact.S = 0f; kv.Value.Contact.Hist.Clear(); }
        }

        /// <summary>The piano left the room (its rig went out of the layout): every presser lets go, every note stops without a
        /// thud, the keys snap back up. A key that was still down records its pianoOff first, so a take recorded across the
        /// layout change never plays back a stuck key and the lesson / Sunny observers see every pianoOn matched.</summary>
        public void ResetAll()
        {
            for (int i = 0; i < Keys.Length; i++)
                if (Keys[i].Held && Keys[i].HasVoice) _sink?.Record(TakeEvent.PianoOff(Keys[i].Midi));
            _dropBuffer.Clear();
            foreach (var kv in _pressers)
            {
                kv.Value.Pressing = null;
                if (kv.Value.Contact != null) { kv.Value.Contact.Key = null; kv.Value.Contact.S = 0f; kv.Value.Contact.Hist.Clear(); }
                else _dropBuffer.Add(kv.Key);
            }
            for (int i = 0; i < _dropBuffer.Count; i++) DropIfIdle(_dropBuffer[i], _pressers[_dropBuffer[i]]);
            _dropBuffer.Clear();
            for (int i = 0; i < Keys.Length; i++)
            {
                var k = Keys[i];
                k.PressedBy.Clear();
                if (k.HasVoice) _sink?.NoteOff(k.Midi, k.Voice, false);
                k.HasVoice = false;
                k.Voice = default;
                k.Held = k.Sustained = k.DamperPending = false;
                if (k.Depth != 0f || k.V != 0f) k.Dirty = true;
                k.Depth = k.V = k.Finger = 0f;
            }
            _sustained.Clear();
            _held = 0;
        }
    }
}
