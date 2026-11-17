// Your song becomes a framed painting (the playback show, round 2, 2026-11-13). When a playback of a saved recording ends,
// its splashes swirl together into one picture that settles into a gold frame on a wall (the swirl is the wall paint's:
// Decor/WallPaint.cs draws copies of the splashes flying into the frame); each recording gets its own painting and the room
// keeps a small gallery of the last Max of them (the newest replacing the oldest), restored on the next launch. The
// picture is made from the take's own events (Decor/GalleryPicture.cs), so only which takes hang where is saved
// (AppSettings Key: "slot:id;slot:id", oldest first). Deleting a recording takes its painting down (LibraryChangedEvent).
// Keep painting does not change it: Keep keeps the splashes on the walls as well; the framed one always goes up.
//   - Which recording: the one the Menu noted (Core's PlayingTake: Recordings > a take or a loop > Play), else the deck's
//     own take found in the library by its events (count, then the events themselves). Nothing found (saving off, a live
//     loop session): nothing is hung.
//   - Where: Slots, found once per place on clear wall (WallCanvas: never over the window, the fireplace, the shelf, the
//     paintings, the curtains, a door; the night sky's band left free), at picture height PictureY, the ones nearest the
//     seat's view first (VR: the seat facing the fire; mixed reality: where the player looked), at least Spacing apart. A
//     hung frame reserves its wall (the notes never paint over it). VR: the room's walls; mixed reality: the scene model's
//     real walls only (no scene model: no gallery there).
//   - Drawn cheaply, nothing per frame at rest: per place one mesh of gilt frames, one of brass plaques, one of pictures (one
//     512 x 576 atlas of 256 x 192 pictures: a material and a draw for all of them) and one of the plaques' words
//     (TextMeshPro laid out once and merged: the recording's name and its local date). Rebuilt only when a frame comes or
//     goes. Pictures are painted on a worker (GalleryPicture.Compose) and uploaded once.
// Main thread (but the compose).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Room.Render;
using MusicRoom.Room.World;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public sealed class PaintGallery
    {
        public const int Max = 6;
        public const string Key = "musicroom.gallery";
        /// <summary>The picture (m), the gilt frame round it, its depth, the plaque under it and how far below.</summary>
        /// <summary>How far the picture stands off the wall, and the plaque's label off the plaque's face: clear of the
        /// headset's depth precision at a few metres (closer surfaces flickered on the device, 2026-11-17).</summary>
        public const float PictureOut = 0.02f, LabelOut = 0.0035f;
        public const float PictureW = 0.78f, PictureH = PictureW * Aspect, Border = 0.065f, Depth = 0.045f, PlaqueW = 0.28f, PlaqueH = 0.07f, PlaqueGap = 0.035f;
        /// <summary>A picture's height over its width; the widths tried at a place, largest first (as wall space allows).</summary>
        public const float Aspect = 0.75f;
        public static readonly float[] Widths = { 0.78f, 0.68f, 0.58f };
        /// <summary>The pictures' own light (a little, so they read from the seat in the evening room): emission x their colours.</summary>
        public static readonly Color SelfLight = new Color(0.3f, 0.29f, 0.27f);
        /// <summary>The picture's centre height (m), the clear wall kept round a frame (m), frames' centres at least this apart.</summary>
        public const float PictureY = 1.58f, Clear = 0.06f;
        /// <summary>The heights a picture's centre may hang at (m): eye height first, higher above what stands at a wall.</summary>
        public static readonly float[] Heights = { PictureY, 1.8f, 1.98f };
        /// <summary>Hung paintings smaller than this many notes are not worth a frame.</summary>
        public const int MinNotes = 6;
        public const int AtlasW = 512, AtlasH = 576;
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        /// <summary>A painting: the recording it is of, its slot, its name and date, its picture.</summary>
        public sealed class Entry
        {
            public string Id, Name, Date;
            public int Slot;
            public Color32[] Pixels;
            public Task Compose;
            public bool Revealed, Uploaded;
            /// <summary>Saved from an earlier session: its frame goes up only once its picture is painted (2026-11-18: "dont show
            /// empty frame in start. looks bad").</summary>
            public bool FromSave;
            public float Seconds;
            public int Notes;
        }

        /// <summary>A place on a wall for a frame (in the canvas's space: the room root's, or the world).</summary>
        public struct Slot
        {
            /// <summary>-1: no place (its frame came off a wall something now stands at, and no other clear place was found).</summary>
            public int Wall;
            public float U, V;
            /// <summary>The picture's width (m) here (its height W x Aspect).</summary>
            public float W;
            public float H => W * Aspect;
            public Vector3 Centre, Right, Up, Normal;
        }

        sealed class Place
        {
            public readonly bool Room;
            public readonly List<Slot> Slots = new List<Slot>(Max);
            public WallCanvas Canvas;
            public int CanvasVersion = -1;
            public GameObject Go;
            public Mesh Gold, Brass, Pictures, Labels;
            public Renderer GoldR, BrassR, PicturesR, LabelsR;
            public readonly List<int> Reserved = new List<int>(Max);   // slots reserved on the canvas now
            public Place(bool room) { Room = room; }
        }

        readonly App _app;
        readonly RoomScene _scene;
        readonly Place _room = new Place(true), _mr = new Place(false);
        readonly List<Entry> _entries = new List<Entry>(Max + 1);
        readonly Action<LibraryChangedEvent> _onLibrary;
        Texture2D _atlas;
        Material _pictureMat, _labelMat;
        bool _loaded, _loading, _destroyed, _mrShown;
        float _loadTry;
        // the playback being watched
        List<TakeEvent> _take;
        bool _loop;
        int _noteVersion;
        Entry _pending;          // resolved: the next frame to hang
        int _resolving;          // a lookup in flight (its playback's number)
        int _playbackNo;
        bool _dirty;

        public PaintGallery(App app, RoomScene scene, WallCanvas roomCanvas)
        {
            _app = app;
            _scene = scene;
            _room.Canvas = roomCanvas;
            _onLibrary = OnLibrary;
            app.Events.Subscribe(_onLibrary);
        }

        public IReadOnlyList<Entry> Entries => _entries;
        public int Count => _entries.Count;
        /// <summary>A recording was found for the last playback: BeginHang will hang it.</summary>
        public bool Ready => _pending != null;
        public Entry PendingEntry => _pending;
        public bool Loaded => _loaded;
        public bool Resolving => _resolving != 0;
        public Texture2D Atlas => _atlas;
        public IReadOnlyList<Slot> RoomSlots { get { EnsureSlots(_room); return _room.Slots; } }
        public IReadOnlyList<Slot> MrSlots => _mr.Slots;
        public GameObject RoomObject => _room.Go;
        public GameObject MrObject => _mr.Go;
        public bool MrShown => _mrShown;
        /// <summary>Renderers of a place (tests: draws).</summary>
        public Renderer PicturesRenderer(bool room) => (room ? _room : _mr).PicturesR;
        public Entry Find(string id) { foreach (Entry e in _entries) if (e.Id == id) return e; return null; }

        ITakeLibrary Library => _app.Get<ITakeLibrary>();

        // ---------------------------------------------------------------- slots

        /// <summary>The places a frame may hang on a canvas: clear wall at picture height, nearest the view first.</summary>
        public static void FindSlots(WallCanvas c, Vector3 seat, Vector3 forward, List<Slot> into, int max = Max)
        {
            into.Clear();
            if (c == null || !c.Finished) return;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            var cands = new List<(float score, Slot s)>(64);
            for (int w = 0; w < c.WallCount; w++)
            {
                c.WallFrame(w, out Vector3 o, out Vector3 along, out Vector3 n, out float width, out float height);
                for (float u = 0.3f; u <= width - 0.3f; u += 0.1f)
                foreach (float pv in Heights)
                {
                    // the largest picture that fits here (eye height first; above an instrument or a shelf standing there)
                    float pw = 0f;
                    foreach (float tw in Widths)
                    {
                        Rect(tw, pv, out float halfW, out float bottom, out float top);
                        if (top > height - 0.05f || u - halfW < 0f || u + halfW > width) continue;
                        if (c.RectClear(w, u - halfW, bottom, u + halfW, top)) { pw = tw; break; }
                    }
                    if (pw <= 0f) continue;
                    Vector3 p = o + along * u + Vector3.up * pv;
                    Vector3 to = p - seat; to.y = 0f;
                    float dist = to.magnitude;
                    if (dist < 0.8f) continue;
                    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(to / dist, forward), -1f, 1f));
                    // seen face on is nicer: how squarely the wall faces the seat; bigger is nicer
                    float face = Mathf.Clamp01(Vector3.Dot(-to / dist, n));
                    float score = ang + 0.5f * (1f - face) + 0.08f * Mathf.Abs(dist - 2.8f) + 0.6f * (Widths[0] - pw) + 0.5f * (pv - PictureY);
                    cands.Add((score, new Slot { Wall = w, U = u, V = pv, W = pw, Centre = p, Right = along, Up = Vector3.up, Normal = n }));
                }
            }
            cands.Sort((a, b) => a.score.CompareTo(b.score));
            if (WallPaint.DebugRigs)
                for (int w = 0; w < c.WallCount; w++)
                {
                    int n = 0; float best = 0f;
                    foreach (var (_, cs) in cands) if (cs.Wall == w) { n++; best = Mathf.Max(best, cs.W); }
                    c.WallFrame(w, out Vector3 o, out Vector3 al, out _, out float ww, out float wh);
                    Debug.Log($"[Room] gallery wall {w} at {o} along {al} {ww:F1} x {wh:F1} m: {n} places, widest {best:F2}");
                }
            foreach (var (_, s) in cands)
            {
                bool ok = true;
                foreach (Slot t in into)
                    if (TooClose(t, s)) { ok = false; break; }
                if (!ok) continue;
                into.Add(s);
                if (into.Count >= max) break;
            }
        }

        /// <summary>Two frames overlap or stand closer than a hand's span apart.</summary>
        public static bool TooClose(in Slot a, in Slot b)
        {
            if (a.Wall < 0 || b.Wall < 0) return false;
            float apart = (a.W + b.W) * 0.5f + 2f * Border + 0.3f;
            return (a.Wall == b.Wall && Mathf.Abs(a.U - b.U) < apart) || (a.Centre - b.Centre).magnitude < apart * 0.8f;
        }

        /// <summary>A frame's rectangle on its wall (picture, gilt, plaque and the clear margin round them).</summary>
        static void Rect(float w, float v, out float halfW, out float bottom, out float top)
        {
            float h = w * Aspect;
            halfW = w * 0.5f + Border + Clear;
            top = v + h * 0.5f + Border + Clear;
            bottom = v - h * 0.5f - Border - PlaqueGap - PlaqueH - Clear;
        }

        /// <summary>The slot's rectangle is clear wall (nothing reserved, nothing standing there).</summary>
        static bool SlotClear(WallCanvas c, in Slot s)
        {
            if (s.Wall < 0) return false;
            Rect(s.W, s.V, out float halfW, out float bottom, out float top);
            return c.RectClear(s.Wall, s.U - halfW, bottom, s.U + halfW, top);
        }

        void EnsureSlots(Place p)
        {
            if (p.Canvas == null || !p.Canvas.Finished || !RigsKnown) return;
            if (p.Room && p.Slots.Count > 0) return;
            if (!p.Room && p.CanvasVersion == p.Canvas.GetHashCode() && p.Slots.Count > 0) return;
            Vector3 seat, fwd;
            if (p.Room)
            {
                // the seat: the rig's origin, facing the fire (the room's own frame; it follows the turned room)
                Transform root = _scene?.Root;
                if (root == null) return;
                seat = root.InverseTransformPoint(new Vector3(0f, 1.2f, 0f));
                Vector3 fire = _scene.Fireplace != null ? root.InverseTransformPoint(_scene.Fireplace.position) : seat + Vector3.forward;
                fwd = fire - seat;
            }
            else
            {
                Transform head = _app.Head;
                seat = head != null ? head.position : new Vector3(0f, 1.2f, 0f);
                fwd = head != null ? head.forward : Vector3.forward;
            }
            // reserved cells of hung frames would hide their own slots: find them with nothing reserved
            ReleaseAll(p);
            FindSlots(p.Canvas, seat, fwd, p.Slots);
            ReserveHung(p);
        }

        void View(Place p, out Vector3 seat, out Vector3 fwd)
        {
            Transform root = _scene?.Root;
            if (p.Room && root != null)
            {
                seat = root.InverseTransformPoint(new Vector3(0f, 1.2f, 0f));
                Vector3 fire = _scene.Fireplace != null ? root.InverseTransformPoint(_scene.Fireplace.position) : seat + Vector3.forward;
                fwd = fire - seat;
                return;
            }
            Transform head = _app.Head;
            seat = head != null ? head.position : new Vector3(0f, 1.2f, 0f);
            fwd = head != null ? head.forward : Vector3.forward;
        }

        /// <summary>The instruments were read again (WallPaint: what stands at the walls, after the layout changed). The
        /// first time the slots can be found; later, a frame that something now stands at moves to the next clear place.</summary>
        public bool RigsKnown { get; private set; }
        /// <summary>Frames moved because something came to stand at them (tests, the log).</summary>
        public int Rehung { get; private set; }

        public void RigsChanged()
        {
            bool first = !RigsKnown;
            RigsKnown = true;
            if (first) { _dirty = true; return; }
            Recheck(_room);
            Recheck(_mr);
        }

        void Recheck(Place p)
        {
            WallCanvas c = p.Canvas;
            if (c == null || !c.Finished || p.Slots.Count == 0) return;
            // the slots something now stands at (each tested with its own reservation lifted); nothing allocated when none
            int bad = 0;
            for (int i = 0; i < p.Slots.Count; i++)
            {
                if (p.Slots[i].Wall < 0) continue;
                bool reserved = p.Reserved.Contains(i);
                if (reserved) Reserve(c, p.Slots[i], false);
                bool clear = SlotClear(c, p.Slots[i]);
                if (reserved) Reserve(c, p.Slots[i], true);
                if (!clear) bad |= 1 << i;
            }
            if (WallPaint.DebugRigs) Debug.Log($"[Room] gallery recheck {(p.Room ? "VR" : "MR")}: bad {bad}");
            if (bad == 0) return;
            ReleaseAll(p);
            // the good places stay taken while new ones are found for the others
            for (int i = 0; i < p.Slots.Count; i++) if ((bad & (1 << i)) == 0 && p.Slots[i].Wall >= 0) Reserve(c, p.Slots[i], true);
            View(p, out Vector3 seat, out Vector3 fwd);
            var found = new List<Slot>(1);
            var taken = new List<int>();
            for (int i = 0; i < p.Slots.Count; i++)
            {
                if ((bad & (1 << i)) == 0) continue;
                Slot old = p.Slots[i];
                FindSlots(c, seat, fwd, found, 1);
                Slot now = found.Count > 0 ? found[0] : new Slot { Wall = -1 };
                p.Slots[i] = now;
                if (now.Wall >= 0) { Reserve(c, now, true); taken.Add(i); }
                Entry e = null;
                foreach (Entry x in _entries) if (x.Slot == i) e = x;
                if (e != null)
                {
                    Rehung++;
                    Debug.Log($"[Room] gallery: '{e.Name}' had something put in front of it: moved from {old.Centre} to {(now.Wall >= 0 ? now.Centre.ToString() : "nowhere (taken down)")}");
                }
            }
            for (int i = 0; i < p.Slots.Count; i++) if (p.Slots[i].Wall >= 0 && ((bad & (1 << i)) == 0 || taken.Contains(i))) Reserve(c, p.Slots[i], false);
            ReserveHung(p);
            Rebuild(p);
        }

        void ReleaseAll(Place p)
        {
            if (p.Canvas == null) return;
            foreach (int s in p.Reserved) if (s < p.Slots.Count) Reserve(p.Canvas, p.Slots[s], false);
            p.Reserved.Clear();
        }

        void ReserveHung(Place p)
        {
            if (p.Canvas == null) return;
            foreach (Entry e in _entries)
                if (e.Slot < p.Slots.Count && p.Slots[e.Slot].Wall >= 0 && !p.Reserved.Contains(e.Slot)) { Reserve(p.Canvas, p.Slots[e.Slot], true); p.Reserved.Add(e.Slot); }
        }

        static void Reserve(WallCanvas c, in Slot s, bool on)
        {
            if (s.Wall < 0) return;
            Rect(s.W, s.V, out float halfW, out float bottom, out float top);
            if (on) c.Reserve(s.Wall, s.U - halfW, bottom, s.U + halfW, top);
            else c.Release(s.Wall, s.U - halfW, bottom, s.U + halfW, top);
        }

        /// <summary>Mixed reality's scene walls are ready (or changed): its slots and its frames.</summary>
        public void SetMrCanvas(WallCanvas canvas, int version)
        {
            if (canvas == null || !canvas.Finished) return;
            if (_mr.Canvas == canvas && _mr.CanvasVersion == version && _mr.Slots.Count > 0) return;
            _mr.Reserved.Clear();   // a rebuilt canvas has nothing reserved
            _mr.Canvas = canvas;
            _mr.CanvasVersion = version;
            Transform head = _app.Head;
            FindSlots(canvas, head != null ? head.position : new Vector3(0f, 1.2f, 0f), head != null ? head.forward : Vector3.forward, _mr.Slots);
            ReserveHung(_mr);
            Rebuild(_mr);
        }

        // ---------------------------------------------------------------- a playback

        /// <summary>A playback started: which events it plays (IRecorder.Take for a take; a loop is known by the Menu's note).</summary>
        public void OnPlaybackStarted(List<TakeEvent> take, bool loop)
        {
            _take = take;
            _loop = loop;
            _noteVersion = PlayingTake.Version;
            _pending = null;
            _playbackNo++;
        }

        /// <summary>The playback ended having painted this many notes: find its recording (async), make its picture.</summary>
        public void OnPlaybackEnded(int painted)
        {
            if (painted < MinNotes) return;
            Resolve(_playbackNo);
        }

        async void Resolve(int playback)
        {
            ITakeLibrary lib = Library;
            if (lib == null || lib.Available != true) return;
            _resolving = playback;
            try
            {
                string id = null;
                List<TakeEvent> events = null;
                if (PlayingTake.Version != _noteVersion && PlayingTake.Id != null && (_loop || ReferenceEquals(PlayingTake.Events, _take)))
                {
                    id = PlayingTake.Id;
                    events = PlayingTake.Events;
                }
                List<TakeInfo> list = await lib.List();
                if (_destroyed || playback != _playbackNo) return;
                TakeInfo info = null;
                if (id != null) foreach (TakeInfo t in list) if (t.id == id) { info = t; break; }
                if (info == null && !_loop && _take != null && _take.Count > 0)
                {
                    // the deck's own take: the library's take with the same events
                    List<TakeEvent> mine = _take;
                    foreach (TakeInfo t in list)
                    {
                        if (t.count != mine.Count) continue;
                        TakeRecord rec = await lib.Get(t.id);
                        if (_destroyed || playback != _playbackNo) return;
                        if (rec?.events != null && SameEvents(rec.events, mine)) { info = t; events = rec.events; break; }
                    }
                }
                if (info == null) { Debug.Log("[Room] gallery: the playback's recording was not found in Recordings: no painting"); return; }
                Entry e = Find(info.id) ?? new Entry { Id = info.id, Slot = -1 };
                e.Name = info.name;
                e.Date = lib.FormatDate(info.created);
                if (e.Pixels == null && e.Compose == null) StartCompose(e, events, info.seconds);
                _pending = e;
            }
            catch (Exception ex) { Debug.LogWarning("[Room] gallery: finding the recording failed: " + ex.Message); }
            finally { if (_resolving == playback) _resolving = 0; }
        }

        public static bool SameEvents(List<TakeEvent> a, List<TakeEvent> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                TakeEvent x = a[i], y = b[i];
                if (!string.Equals(x.type, y.type, StringComparison.Ordinal) || x.midi != y.midi || x.i != y.i || Mathf.Abs(x.t - y.t) > 1e-3f) return false;
            }
            return true;
        }

        void StartCompose(Entry e, List<TakeEvent> events, float seconds)
        {
            if (events == null) return;
            var notes = new List<PictureNote>(Mathf.Min(events.Count, 4096));
            float end = GalleryPicture.Notes(events, _app.Registry, notes);
            e.Notes = notes.Count;
            e.Seconds = Mathf.Max(seconds, end);
            var px = new Color32[GalleryPicture.Width * GalleryPicture.Height];
            float dur = e.Seconds;
            e.Compose = Task.Run(() => { GalleryPicture.Compose(notes, dur, BrushAtlas.Shared, px); e.Pixels = px; });
        }

        /// <summary>The swirl starts: the pending painting gets its slot (a new one, its old one, or the oldest painting's) in
        /// the place shown now, its frame goes up with blank paper, it is saved. False when there is nothing to hang or no
        /// slot. The frame's picture centre, half axes and outward normal come back in WORLD space.</summary>
        public bool BeginHang(bool room, out Vector3 centre, out Vector3 right, out Vector3 up, out Vector3 normal)
        {
            centre = right = up = normal = default;
            Entry e = _pending;
            if (e == null) return false;
            _pending = null;
            Place p = room ? _room : _mr;
            EnsureSlots(p);
            if (p.Slots.Count == 0) return false;
            if (!_loaded) LoadSaved(sync: true);
            if (!_entries.Contains(e) || e.Slot < 0 || e.Slot >= p.Slots.Count || p.Slots[e.Slot].Wall < 0)
            {
                _entries.Remove(e);
                int slot = FreeSlot(p);
                if (slot < 0)
                {
                    // the oldest painting that hangs in this place gives way
                    Entry old = null;
                    foreach (Entry x in _entries) if (x.Slot >= 0 && x.Slot < p.Slots.Count && p.Slots[x.Slot].Wall >= 0) { old = x; break; }
                    if (old == null) return false;
                    slot = old.Slot;
                    Remove(old);
                }
                e.Slot = slot;
            }
            else _entries.Remove(e);
            _entries.Add(e);   // the newest last
            while (_entries.Count > Max) Remove(_entries[0]);
            e.Revealed = false;
            e.Uploaded = false;
            e.FromSave = false;   // hung now by the swirl: its frame shows blank paper until the swirl arrives
            ClearSlot(e.Slot);
            Save();
            ReserveHung(_room);
            ReserveHung(_mr);
            Rebuild(_room);
            Rebuild(_mr);
            Slot s = p.Slots[e.Slot];
            Transform space = SpaceOf(p);
            centre = space != null ? space.TransformPoint(s.Centre + s.Normal * PictureOut) : s.Centre;
            right = space != null ? space.TransformDirection(s.Right) * (s.W * 0.5f) : s.Right * (s.W * 0.5f);
            up = space != null ? space.TransformDirection(s.Up) * (s.H * 0.5f) : s.Up * (s.H * 0.5f);
            normal = space != null ? space.TransformDirection(s.Normal) : s.Normal;
            Debug.Log($"[Room] gallery: '{e.Name}' hangs in slot {e.Slot} ({(room ? "VR" : "MR")}), {_entries.Count} paintings");
            return true;
        }

        /// <summary>The swirl has arrived: the picture shows (as soon as it is painted).</summary>
        public void Reveal(string id)
        {
            Entry e = Find(id);
            if (e == null) return;
            e.Revealed = true;
            Upload(e);
        }

        public string LastHungId => _entries.Count > 0 ? _entries[_entries.Count - 1].Id : null;

        int FreeSlot(Place p)
        {
            for (int s = 0; s < p.Slots.Count; s++)
            {
                if (p.Slots[s].Wall < 0) continue;
                bool used = false;
                foreach (Entry e in _entries) if (e.Slot == s) { used = true; break; }
                if (!used) return s;
            }
            return -1;
        }

        void Remove(Entry e)
        {
            _entries.Remove(e);
            foreach (Place p in new[] { _room, _mr })
                if (p.Canvas != null && e.Slot >= 0 && e.Slot < p.Slots.Count && p.Reserved.Remove(e.Slot)) Reserve(p.Canvas, p.Slots[e.Slot], false);
            _dirty = true;
        }

        Transform SpaceOf(Place p) => p.Room ? _scene?.Root : null;

        // ---------------------------------------------------------------- saving, loading, deleting

        void Save()
        {
            if (_app.Settings == null) return;
            var sb = new System.Text.StringBuilder(128);
            foreach (Entry e in _entries)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(e.Slot).Append(':').Append(e.Id);
            }
            _app.Settings.SetString(Key, sb.ToString());
            _app.Settings.Save();
        }

        /// <summary>The saved list ("slot:id;slot:id", oldest first).</summary>
        public static void Parse(string saved, List<(int slot, string id)> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(saved)) return;
            foreach (string part in saved.Split(';'))
            {
                int c = part.IndexOf(':');
                if (c <= 0 || c >= part.Length - 1) continue;
                if (!int.TryParse(part.Substring(0, c), out int slot) || slot < 0 || slot >= Max) continue;
                string id = part.Substring(c + 1);
                bool dup = false;
                foreach (var x in into) if (x.id == id || x.slot == slot) dup = true;
                if (!dup) into.Add((slot, id));
            }
            while (into.Count > Max) into.RemoveAt(0);
        }

        /// <summary>The paintings saved last time: their entries now, their pictures as their takes load.</summary>
        void LoadSaved(bool sync = false)
        {
            if (_loaded || _loading) return;
            ITakeLibrary lib = Library;
            if (lib == null || lib.Available == null) { if (sync) _loaded = true; return; }
            var saved = new List<(int, string)>();
            Parse(_app.Settings?.GetString(Key, "") ?? "", saved);
            foreach (var (slot, id) in saved)
                if (Find(id) == null) _entries.Add(new Entry { Id = id, Slot = slot, Revealed = true, FromSave = true, Name = "", Date = "" });
            _loaded = true;
            if (lib.Available != true || saved.Count == 0) return;
            LoadPictures(lib);
        }

        async void LoadPictures(ITakeLibrary lib)
        {
            _loading = true;
            try
            {
                List<TakeInfo> list = await lib.List();
                if (_destroyed) return;
                var gone = new List<Entry>();
                foreach (Entry e in _entries.ToArray())
                {
                    TakeInfo info = null;
                    foreach (TakeInfo t in list) if (t.id == e.Id) { info = t; break; }
                    if (info == null) { if (e.Pixels == null && e.Compose == null) gone.Add(e); continue; }
                    e.Name = info.name;
                    e.Date = lib.FormatDate(info.created);
                    if (e.Pixels != null || e.Compose != null) continue;
                    TakeRecord rec = await lib.Get(e.Id);
                    if (_destroyed) return;
                    if (rec?.events == null) { gone.Add(e); continue; }
                    StartCompose(e, rec.events, rec.seconds);
                }
                foreach (Entry e in gone) Remove(e);
                if (gone.Count > 0) Save();
                _dirty = true;
            }
            catch (Exception ex) { Debug.LogWarning("[Room] gallery: loading the paintings failed: " + ex.Message); }
            finally { _loading = false; }
        }

        void OnLibrary(LibraryChangedEvent e)
        {
            if (_entries.Count == 0 && _pending == null) return;
            Prune();
        }

        /// <summary>A recording deleted: its painting comes down (and a pending one is forgotten).</summary>
        async void Prune()
        {
            ITakeLibrary lib = Library;
            if (lib == null || lib.Available != true) return;
            try
            {
                List<TakeInfo> list = await lib.List();
                if (_destroyed) return;
                bool changed = false;
                foreach (Entry e in _entries.ToArray())
                {
                    bool there = false;
                    foreach (TakeInfo t in list) if (t.id == e.Id) { there = true; break; }
                    if (!there) { Remove(e); changed = true; Debug.Log($"[Room] gallery: '{e.Name}' was deleted: its painting comes down"); }
                }
                if (_pending != null)
                {
                    bool there = false;
                    foreach (TakeInfo t in list) if (t.id == _pending.Id) { there = true; break; }
                    if (!there) _pending = null;
                }
                if (changed) Save();
            }
            catch (Exception ex) { Debug.LogWarning("[Room] gallery: " + ex.Message); }
        }

        // ---------------------------------------------------------------- the frame

        /// <summary>Room's Decor phase (through WallPaint): load once the library is up, upload finished pictures, rebuild
        /// after a change, show mixed reality's frames only there. Nothing to do (and no allocation) at rest.</summary>
        public void Tick(float dt, bool roomVisible)
        {
            if (!_loaded)
            {
                _loadTry -= dt;
                if (_loadTry <= 0f) { _loadTry = 0.5f; LoadSaved(); }
            }
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!e.Uploaded && e.Revealed && e.Pixels != null) Upload(e);
            }
            if (_dirty)
            {
                _dirty = false;
                Rebuild(_room);
                Rebuild(_mr);
            }
            bool mr = !roomVisible && _mr.Go != null;
            if (mr != _mrShown)
            {
                _mrShown = mr;
                if (_mr.Go != null) _mr.Go.SetActive(mr);
            }
        }

        void Upload(Entry e)
        {
            if (e.Pixels == null || e.Slot < 0 || e.Uploaded) return;
            e.Uploaded = true;
            if (e.FromSave) _dirty = true;   // a saved painting's frame goes up now, with its picture
            if (_atlas == null) return;
            SlotRect(e.Slot, out int x, out int y);
            _atlas.SetPixels32(x, y, GalleryPicture.Width, GalleryPicture.Height, e.Pixels);
            _atlas.Apply(true, false);
        }

        void ClearSlot(int slot)
        {
            if (_atlas == null || slot < 0) return;
            SlotRect(slot, out int x, out int y);
            var px = new Color32[GalleryPicture.Width * GalleryPicture.Height];
            Color32 paper = GalleryPicture.Paper;
            for (int i = 0; i < px.Length; i++) px[i] = paper;
            _atlas.SetPixels32(x, y, GalleryPicture.Width, GalleryPicture.Height, px);
            _atlas.Apply(true, false);
        }

        static void SlotRect(int slot, out int x, out int y)
        {
            x = (slot % 2) * GalleryPicture.Width;
            y = (slot / 2) * GalleryPicture.Height;
        }

        bool Graphics => !_app.Headless && _scene?.Static != null && _scene.Static.ContainsKey(RoomMat.Gilt);

        void EnsureMaterials()
        {
            if (_atlas != null || !Graphics) return;
            _atlas = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, true, false)
            {
                name = "Room/gallery_pictures", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2,
            };
            var px = new Color32[AtlasW * AtlasH];
            Color32 paper = GalleryPicture.Paper;
            for (int i = 0; i < px.Length; i++) px[i] = paper;
            _atlas.SetPixels32(px);
            _atlas.Apply(true, false);
            // the pictures: lit as the room's own canvases, plus a little light of their own (the emission is the picture
            // itself, dimmed), so a song's painting reads from the seat in the evening room without glowing
            if (_app.Materials.HasTemplate(RoomTemplates.LitEmissive))
            {
                _pictureMat = _app.Materials.Clone(RoomTemplates.LitEmissive);
                _pictureMat.name = "Room gallery pictures";
                _pictureMat.SetColor(MaterialLibrary.EmissionColor, SelfLight);
                if (_pictureMat.HasProperty(EmissionMap)) _pictureMat.SetTexture(EmissionMap, _atlas);
                _pictureMat.SetFloat(MaterialLibrary.Smoothness, 0.25f);
            }
            else
            {
                Material canvas = _scene.Static.TryGetValue(RoomMat.Canvas, out Renderer cr) && cr != null ? cr.sharedMaterial : null;
                _pictureMat = canvas != null ? new Material(canvas) { name = "Room gallery pictures" } : _app.Materials.Lit(Color.white, 0.2f, 0f, _atlas);
            }
            _pictureMat.SetTexture(MaterialLibrary.BaseMap, _atlas);
            if (_pictureMat.HasProperty("_BaseMap_ST")) _pictureMat.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            if (_pictureMat.HasProperty(MaterialLibrary.BaseColor)) _pictureMat.SetColor(MaterialLibrary.BaseColor, Color.white);
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font != null && font.material != null)
            {
                Material tpl = _app.Materials.HasTemplate(RoomTemplates.LabelText) ? _app.Materials.Template(RoomTemplates.LabelText) : null;
                if (tpl != null && tpl.shader != null && tpl.shader.name.StartsWith("TextMeshPro", StringComparison.Ordinal))
                {
                    _labelMat = new Material(font.material) { name = "Room gallery plaques (mobile SDF)", shader = tpl.shader };
                    _labelMat.renderQueue = tpl.renderQueue;
                }
            }
        }

        /// <summary>A place's meshes from its hung paintings (setup-time work: a frame came or went).</summary>
        void Rebuild(Place p)
        {
            if (!Graphics) return;
            if (!p.Room && p.Canvas == null) return;
            if (p.Room) EnsureSlots(p);
            EnsureMaterials();
            if (p.Go == null)
            {
                p.Go = new GameObject(p.Room ? "Gallery" : "GalleryMr");
                p.Go.transform.SetParent(p.Room ? _scene.Root : _app.WorldRoot, false);
                if (!p.Room) p.Go.SetActive(_mrShown);
            }
            DestroyMeshes(p);
            Transform space = p.Room ? null : p.Go.transform;   // VR: the canvas is the room root's space, as the Go's
            var gold = new MeshParts(); var brass = new MeshParts(); var pics = new MeshParts();
            var labels = new List<(Slot s, string name, string date)>();
            foreach (Entry e in _entries)
            {
                if (e.Slot < 0 || e.Slot >= p.Slots.Count || p.Slots[e.Slot].Wall < 0) continue;
                if (e.FromSave && !e.Uploaded) continue;   // never an empty frame at the start: it waits for its picture
                Slot s = p.Slots[e.Slot];
                Frame(gold, brass, pics, s, e.Slot, space);
                labels.Add((s, e.Name ?? "", e.Date ?? ""));
            }
            if (gold.Count == 0) { SetRenderers(p, false); return; }
            p.Gold = gold.ToMesh("Gallery frames");
            p.Brass = brass.ToMesh("Gallery plaques");
            p.Pictures = pics.ToMesh("Gallery pictures");
            p.Labels = BuildLabels(p, labels, space);
            Material gilt = _scene.Static[RoomMat.Gilt].sharedMaterial;
            Material brassMat = _scene.Static.TryGetValue(RoomMat.Brass, out Renderer br) && br != null ? br.sharedMaterial : gilt;
            p.GoldR = Renderer(p, p.GoldR, "Frames", p.Gold, gilt);
            p.BrassR = Renderer(p, p.BrassR, "Plaques", p.Brass, brassMat);
            p.PicturesR = Renderer(p, p.PicturesR, "Pictures", p.Pictures, _pictureMat);
            if (p.Labels != null && _labelMat != null) p.LabelsR = Renderer(p, p.LabelsR, "Words", p.Labels, _labelMat);
            else if (p.LabelsR != null) p.LabelsR.enabled = false;
            SetRenderers(p, true);
        }

        static void SetRenderers(Place p, bool on)
        {
            if (p.GoldR != null) p.GoldR.enabled = on;
            if (p.BrassR != null) p.BrassR.enabled = on;
            if (p.PicturesR != null) p.PicturesR.enabled = on;
            if (p.LabelsR != null) p.LabelsR.enabled = on && p.Labels != null;
        }

        static Renderer Renderer(Place p, Renderer r, string name, Mesh mesh, Material mat)
        {
            if (r == null) r = RoomBuilder.MakeRenderer(p.Go.transform, name, mesh, mat);
            else
            {
                r.GetComponent<MeshFilter>().sharedMesh = mesh;
                r.sharedMaterial = mat;
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        static void DestroyMeshes(Place p)
        {
            if (p.Gold != null) UnityEngine.Object.Destroy(p.Gold);
            if (p.Brass != null) UnityEngine.Object.Destroy(p.Brass);
            if (p.Pictures != null) UnityEngine.Object.Destroy(p.Pictures);
            if (p.Labels != null) UnityEngine.Object.Destroy(p.Labels);
            p.Gold = p.Brass = p.Pictures = p.Labels = null;
        }

        /// <summary>A frame: a two-step gilt moulding round the picture, the picture (its atlas slot), the brass plaque.</summary>
        static void Frame(MeshParts gold, MeshParts brass, MeshParts pics, in Slot s, int slot, Transform space)
        {
            Vector3 c = s.Centre, r = s.Right, u = s.Up, n = s.Normal;
            float hw = s.W * 0.5f, hh = s.H * 0.5f;
            // the outer moulding (flat to the wall) and an inner lip standing proud
            float b = Border, d = Depth;
            Bar(gold, c + u * (hh + b * 0.5f) + n * (d * 0.5f), r, u, n, hw + b, b * 0.5f, d * 0.5f, space);
            Bar(gold, c - u * (hh + b * 0.5f) + n * (d * 0.5f), r, u, n, hw + b, b * 0.5f, d * 0.5f, space);
            Bar(gold, c + r * (hw + b * 0.5f) + n * (d * 0.5f), r, u, n, b * 0.5f, hh, d * 0.5f, space);
            Bar(gold, c - r * (hw + b * 0.5f) + n * (d * 0.5f), r, u, n, b * 0.5f, hh, d * 0.5f, space);
            float lip = 0.014f, ld = d + 0.012f;
            // the lip overlaps the picture's edge by a few mm, so its inner faces never lie in the outer moulding's (two
            // coplanar faces z-fought on the device)
            const float over = 0.004f;
            float lw = lip + over, lo = (lip - over) * 0.5f;
            Bar(gold, c + u * (hh + lo) + n * (ld * 0.5f), r, u, n, hw + lip, lw * 0.5f, ld * 0.5f, space);
            Bar(gold, c - u * (hh + lo) + n * (ld * 0.5f), r, u, n, hw + lip, lw * 0.5f, ld * 0.5f, space);
            Bar(gold, c + r * (hw + lo) + n * (ld * 0.5f), r, u, n, lw * 0.5f, hh - over, ld * 0.5f, space);
            Bar(gold, c - r * (hw + lo) + n * (ld * 0.5f), r, u, n, lw * 0.5f, hh - over, ld * 0.5f, space);
            // the picture, a little into the frame
            SlotRect(slot, out int x, out int y);
            var uv0 = new Vector2(x / (float)AtlasW, y / (float)AtlasH);
            var uv1 = new Vector2((x + GalleryPicture.Width) / (float)AtlasW, (y + GalleryPicture.Height) / (float)AtlasH);
            Vector3 pc = c + n * PictureOut;
            pics.Quad(pc - r * hw - u * hh, pc + r * hw - u * hh, pc + r * hw + u * hh, pc - r * hw + u * hh, n, uv0, uv1, space);
            // the plaque
            Vector3 plaque = c - u * (hh + b + PlaqueGap + PlaqueH * 0.5f) + n * 0.004f;
            Bar(brass, plaque, r, u, n, PlaqueW * 0.5f, PlaqueH * 0.5f, 0.004f, space);
        }

        static void Bar(MeshParts m, Vector3 c, Vector3 r, Vector3 u, Vector3 n, float hx, float hy, float hz, Transform space)
        {
            // the five faces seen from the room (no back)
            Vector3 X = r * hx, Y = u * hy, Z = n * hz;
            m.Quad(c - X - Y + Z, c + X - Y + Z, c + X + Y + Z, c - X + Y + Z, n, Vector2.zero, Vector2.one, space);
            m.Quad(c - X + Y + Z, c + X + Y + Z, c + X + Y - Z, c - X + Y - Z, u, Vector2.zero, Vector2.one, space);
            m.Quad(c - X - Y - Z, c + X - Y - Z, c + X - Y + Z, c - X - Y + Z, -u, Vector2.zero, Vector2.one, space);
            m.Quad(c + X - Y + Z, c + X - Y - Z, c + X + Y - Z, c + X + Y + Z, r, Vector2.zero, Vector2.one, space);
            m.Quad(c - X - Y - Z, c - X - Y + Z, c - X + Y + Z, c - X + Y - Z, -r, Vector2.zero, Vector2.one, space);
        }

        /// <summary>A simple list-built mesh (setup only).</summary>
        sealed class MeshParts
        {
            readonly List<Vector3> _p = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _t = new List<int>();
            public int Count => _p.Count;

            /// <summary>a b c d counter-clockwise seen from the normal's side.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 uv0, Vector2 uv1, Transform space)
            {
                if (space != null)
                {
                    a = space.InverseTransformPoint(a); b = space.InverseTransformPoint(b); c = space.InverseTransformPoint(c); d = space.InverseTransformPoint(d);
                    normal = space.InverseTransformDirection(normal);
                }
                int i = _p.Count;
                _p.Add(a); _p.Add(b); _p.Add(c); _p.Add(d);
                for (int k = 0; k < 4; k++) _n.Add(normal);
                _uv.Add(new Vector2(uv0.x, uv0.y)); _uv.Add(new Vector2(uv1.x, uv0.y)); _uv.Add(new Vector2(uv1.x, uv1.y)); _uv.Add(new Vector2(uv0.x, uv1.y));
                // Unity's front face is the side cross(b - a, c - a) points to (clockwise as seen from it): face the normal
                Vector3 face = Vector3.Cross(b - a, d - a);
                if (Vector3.Dot(face, normal) > 0f) { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3); }
                else { _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); _t.Add(i); _t.Add(i + 3); _t.Add(i + 2); }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_p);
                m.SetNormals(_n);
                m.SetUVs(0, _uv);
                m.SetTriangles(_t, 0);
                m.RecalculateTangents();
                m.RecalculateBounds();
                m.UploadMeshData(true);
                return m;
            }
        }

        /// <summary>The plaques' words: the recording's name, its date under it, laid out once by TextMeshPro and merged.</summary>
        Mesh BuildLabels(Place p, List<(Slot s, string name, string date)> labels, Transform space)
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null || _labelMat == null || labels.Count == 0) return null;
            var verts = new List<Vector3>(256); var nrm = new List<Vector3>(256); var tan = new List<Vector4>(256);
            var cols = new List<Color32>(256); var uv0 = new List<Vector4>(256); var uv1 = new List<Vector4>(256);
            var tris = new List<int>(384);
            var tv = new List<Vector3>(); var tn = new List<Vector3>(); var tt = new List<Vector4>(); var tc = new List<Color32>();
            var tu0 = new List<Vector4>(); var tu1 = new List<Vector4>();
            Transform parent = p.Go.transform;
            foreach (var (s, name, date) in labels)
            {
                var go = new GameObject("Plaque words");
                try
                {
                    go.transform.SetParent(parent, false);
                    Vector3 c = s.Centre - s.Up * (s.H * 0.5f + Border + PlaqueGap + PlaqueH * 0.5f) + s.Normal * (0.008f + LabelOut);
                    Vector3 wc = space != null ? c : (_scene.Root != null ? _scene.Root.TransformPoint(c) : c);
                    Vector3 wn = space != null ? s.Normal : (_scene.Root != null ? _scene.Root.TransformDirection(s.Normal) : s.Normal);
                    Vector3 wu = space != null ? s.Up : (_scene.Root != null ? _scene.Root.TransformDirection(s.Up) : s.Up);
                    // the text faces out of the wall (its -z toward the reader)
                    go.transform.SetPositionAndRotation(wc, Quaternion.LookRotation(-wn, wu));
                    var tmp = go.AddComponent<TextMeshPro>();
                    tmp.font = font;
                    tmp.fontSharedMaterial = _labelMat;
                    tmp.text = string.IsNullOrEmpty(date) ? name : name + "\n<size=75%>" + date + "</size>";
                    tmp.fontSize = 0.2f;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.overflowMode = TextOverflowModes.Truncate;
                    tmp.color = new Color32(0x2a, 0x1a, 0x08, 0xff);
                    tmp.rectTransform.sizeDelta = new Vector2(PlaqueW - 0.02f, PlaqueH - 0.008f);
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 0.06f;
                    tmp.fontSizeMax = 0.2f;
                    tmp.ForceMeshUpdate(true, true);
                    Mesh m = tmp.mesh;
                    if (m == null || m.vertexCount == 0) continue;
                    Matrix4x4 to = parent.worldToLocalMatrix * go.transform.localToWorldMatrix;
                    Matrix4x4 nTo = to.inverse.transpose;
                    int b0 = verts.Count;
                    m.GetVertices(tv); m.GetNormals(tn); m.GetTangents(tt); m.GetColors(tc); m.GetUVs(0, tu0); m.GetUVs(1, tu1);
                    for (int v = 0; v < tv.Count; v++)
                    {
                        verts.Add(to.MultiplyPoint3x4(tv[v]));
                        nrm.Add(v < tn.Count ? nTo.MultiplyVector(tn[v]).normalized : -Vector3.forward);
                        if (v < tt.Count) { Vector3 dd = to.MultiplyVector(tt[v]).normalized; tan.Add(new Vector4(dd.x, dd.y, dd.z, tt[v].w)); }
                        else tan.Add(new Vector4(1f, 0f, 0f, 1f));
                        cols.Add(v < tc.Count ? tc[v] : new Color32(255, 255, 255, 255));
                        uv0.Add(v < tu0.Count ? tu0[v] : Vector4.zero);
                        uv1.Add(v < tu1.Count ? tu1[v] : Vector4.zero);
                    }
                    int[] t = m.triangles;
                    for (int k = 0; k < t.Length; k++) tris.Add(b0 + t[k]);
                }
                finally
                {
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
            }
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = "Gallery plaque words" };
            mesh.SetVertices(verts);
            mesh.SetNormals(nrm);
            mesh.SetTangents(tan);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        public void Destroy()
        {
            _destroyed = true;
            _app.Events.Unsubscribe(_onLibrary);
            DestroyMeshes(_room);
            DestroyMeshes(_mr);
            if (_room.Go != null) UnityEngine.Object.Destroy(_room.Go);
            if (_mr.Go != null) UnityEngine.Object.Destroy(_mr.Go);
            if (_atlas != null) UnityEngine.Object.Destroy(_atlas);
            if (_pictureMat != null) UnityEngine.Object.Destroy(_pictureMat);
            if (_labelMat != null) UnityEngine.Object.Destroy(_labelMat);
        }
    }
}
