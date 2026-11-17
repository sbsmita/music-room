// The creative paint in the booted app (round 2, 2026-11-13): Room/Decor/WallPaint.cs with PaintGallery.cs and the throw.
//   - the gallery: a playback of a saved recording ends; after the hold its splashes swirl (the gather renderer) into a gold
//     frame on a clear wall of the turned room (not over the window), its picture shows, the walls fade clean; the frame
//     stays, and the next playback's paint keeps off it. Found from the Menu's note or, for the deck's own take, by its
//     events. Kept across a reboot (the same picture), taken down when the recording is deleted, the newest replacing the
//     oldest past Max; Keep painting keeps the walls painted and still frames the song; mixed reality: on the scene model's
//     real walls, none with only the depth sensor;
//   - throwing: a quick flick of an open hand during a playback throws a blob that splats on the wall it points at (a
//     sound); nothing outside a playback; nothing from a hand that just played the piano; mixed reality's real walls, and
//     nothing in the air with none; Sunny's throw line in its turn.
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Debug = UnityEngine.Debug;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    public class PaintShowPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        string _keepData;
        double _audio = 10.0;

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        WallPaint Paint => Room.WallPaint;
        PaintGallery Gallery => Paint.Gallery;
        IRecorder Recorder => App.Get<IRecorder>();
        ITakeLibrary Library => App.Get<ITakeLibrary>();
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }

        /// <summary>Every show line heard before but the ones a test names.</summary>
        const string AllTips = "tip_listen_back,tip_play_brings,tip_song_stars,tip_window_show,tip_ghost_hands,tip_paint_walls,tip_paint_throw,tip_paint_gallery,tip_paint_keep";

        IEnumerator Boot(string tipsSeen = AllTips, TestBoot boot = null)
        {
            _audio = 10.0;
            PlayingTake.Clear();
            _boot = boot ?? new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, tipsSeen);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            t0 = Time.realtimeSinceStartup;
            while (Library.Available == null && Time.realtimeSinceStartup - t0 < 5f) { Frame(); yield return null; }
            Step(0.3f);
            Paint.FinishAtlas();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            if (_keepData != null) { try { Directory.Delete(_keepData, true); } catch { } _keepData = null; }
            PlayingTake.Clear();
        }

        static List<TakeEvent> Song(float seconds, int seed = 1)
        {
            var take = new List<TakeEvent>();
            var rng = new System.Random(seed);
            int i = 0;
            for (float t = 0.05f; t < seconds; t += 0.22f, i++)
            {
                int m = 60 + rng.Next(14);
                TakeEvent on = TakeEvent.PianoOn(m, 0.4f + 0.6f * (float)rng.NextDouble()); on.t = t; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = t + 0.2f; take.Add(off);
                if (i % 3 == 1) { TakeEvent d = TakeEvent.Drum(i % 6 == 1 ? "kick" : "snare", 0.85f, 0.2f, "head", false); d.t = t; take.Add(d); }
                if (i % 7 == 3) for (int k = 0; k < 4; k++) { TakeEvent h = TakeEvent.Harp(6 + k, 0.7f, 0.5f, 0.4f, "through"); h.t = t + 0.06f * k; take.Add(h); }
            }
            take.Sort((a, b) => a.t.CompareTo(b.t));
            return take;
        }

        static float End(List<TakeEvent> take) => take[take.Count - 1].t + 0.3f;

        /// <summary>A recording saved in Recordings (as the auto-save does), waited for.</summary>
        IEnumerator Save(List<TakeEvent> events, string name, List<TakeRecord> into)
        {
            var task = Library.Put(new TakeRecord { name = name, seconds = End(events), events = events });
            float t0 = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < 10f) { Frame(); yield return null; }
            Assert.IsTrue(task.IsCompleted && task.Result != null, "saved");
            into.Add(task.Result);
        }

        /// <summary>Play a saved recording as Recordings > Play does (the Menu notes it), or as the deck does (not noted).</summary>
        void Play(TakeRecord rec, bool fromMenu = true)
        {
            Assert.IsTrue(Recorder.PlayEvents(rec.events, rec.seconds));
            if (fromMenu) PlayingTake.Note(rec.id, rec.events);
        }

        /// <summary>Steps until cond (real time for the workers: the library's reads, the picture's painter).</summary>
        IEnumerator Until(System.Func<bool> cond, float seconds, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) { Frame(); yield return null; }
            Assert.IsTrue(cond(), what);
        }

        /// <summary>Through a playback to its painting framed: the end, the hold (the recording found meanwhile), the swirl.</summary>
        IEnumerator PlayToFrame(TakeRecord rec, bool fromMenu = true)
        {
            Play(rec, fromMenu);
            Step(rec.seconds + 0.3f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Holding));
            yield return Until(() => !Gallery.Resolving, 10f, "the recording looked up");
            Step(WallPaint.HoldSeconds + 0.1f);
        }

        // ------------------------------------------------------------------------------------------------ the gallery

        [UnityTest]
        public IEnumerator APlaybackEndsInAFramedPaintingOnAClearWall()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(8f), "Take 1 · 0:08", recs);
            TakeRecord rec = recs[0];
            yield return PlayToFrame(rec);
            Assert.That(Paint.Gather, Is.InRange(0f, 0.2f), "the splashes swirl into the frame");
            Assert.IsTrue(Paint.RoomLayer.GatherMeshRenderer.enabled, "the copies drawn flying");
            Assert.That(Gallery.Count, Is.EqualTo(1));
            PaintGallery.Entry e = Gallery.Entries[0];
            Assert.That(e.Id, Is.EqualTo(rec.id));
            Assert.That(e.Name, Is.EqualTo("Take 1 · 0:08"));
            Assert.That(e.Date, Is.Not.Empty, "the local date on the plaque");
            Assert.IsNotNull(Gallery.RoomObject, "the frame is up");
            Assert.IsTrue(Gallery.PicturesRenderer(true).enabled);
            Assert.That(App.Settings.GetString(PaintGallery.Key, ""), Does.Contain(rec.id), "saved");
            Step(WallPaint.GatherSeconds + 0.2f);
            Assert.That(Paint.Gather, Is.EqualTo(-1f), "the swirl is over");
            Assert.IsFalse(Paint.RoomLayer.GatherMeshRenderer.enabled);
            yield return Until(() => e.Uploaded, 20f, "the picture shows in the frame");
            Step(WallPaint.FadeSeconds + 0.5f);
            Assert.That(Paint.Splashes, Is.EqualTo(0), "the walls clean again (Keep painting off)");
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Idle));
            Assert.That(Gallery.Count, Is.EqualTo(1), "the painting stays");
            // where: on a wall of the room, at picture height, not over the window
            PaintGallery.Slot s = Gallery.RoomSlots[e.Slot];
            Vector3 c = Room.Root.TransformPoint(s.Centre);
            Bounds box = Room.RoomBox;
            float dx = Mathf.Min(Mathf.Abs(c.x - box.min.x), Mathf.Abs(c.x - box.max.x)), dz = Mathf.Min(Mathf.Abs(c.z - box.min.z), Mathf.Abs(c.z - box.max.z));
            Assert.That(Mathf.Min(dx, dz), Is.LessThan(0.02f), $"on a wall: {c}");
            Assert.That(c.y, Is.InRange(PaintGallery.PictureY - 0.01f, PaintGallery.Heights[PaintGallery.Heights.Length - 1] + 0.01f));
            Vector3 w = Room.Scene.Window.InverseTransformPoint(c);
            if (Mathf.Abs(w.z) < 0.3f) Assert.IsFalse(Mathf.Abs(w.x) < 1.05f + 0.35f && Mathf.Abs(w.y) < 0.98f + 0.35f, $"over the window: {c}");
            Debug.Log($"[Gallery] the first frame hangs at {c} (slot {e.Slot} of {Gallery.RoomSlots.Count})");
            // the next playback's paint keeps off the frame
            Play(rec);
            Step(rec.seconds);
            WallPaint.Layer l = Paint.RoomLayer;
            int checkedQuads = 0;
            for (int q = 0; q < WallPaint.MaxQuads; q++)
            {
                if (l.Gen[q] == 255) continue;
                Vector3 p = l.Centre[q];
                Vector3 d = p - s.Centre;
                if (Mathf.Abs(Vector3.Dot(d, s.Normal)) > 0.05f) continue;   // another wall
                checkedQuads++;
                Assert.IsFalse(Mathf.Abs(Vector3.Dot(d, s.Right)) < s.W * 0.5f + PaintGallery.Border && Mathf.Abs(d.y) < s.H * 0.5f + PaintGallery.Border,
                    $"paint over the frame at {p}");
            }
            Debug.Log($"[Gallery] {checkedQuads} quads on the frame's wall, none over it");
        }

        /// <summary>A rig's visible box that stands within half a metre in front of a frame's wall and covers the frame.</summary>
        static string Blocker(PaintGallery.Slot s, Transform space)
        {
            Vector3 c = space.TransformPoint(s.Centre), n = space.TransformDirection(s.Normal), r = space.TransformDirection(s.Right);
            float hw = s.W * 0.5f + PaintGallery.Border, top = s.H * 0.5f + PaintGallery.Border, bottom = s.H * 0.5f + PaintGallery.Border + PaintGallery.PlaqueGap + PaintGallery.PlaqueH;
            foreach (Rig rig in App.Current.Get<IRigRegistry>().Rigs)
            {
                if (rig.Group == null || !rig.Group.gameObject.activeInHierarchy) continue;
                foreach (Renderer rr in rig.Group.GetComponentsInChildren<Renderer>(false))
                {
                    if (!rr.enabled || rr is ParticleSystemRenderer) continue;
                    Bounds b = rr.bounds;
                    float lo = float.MaxValue, hi = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        float d = Vector3.Dot(p - c, n), u = Vector3.Dot(p - c, r), v = p.y - c.y;
                        lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
                    }
                    if (lo > 0.5f || hi < -0.05f) continue;
                    if (u1 < -hw || u0 > hw || v1 < -bottom || v0 > top) continue;
                    return $"{rig.Id} ({rr.name})";
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator NoFramePlaceIsBehindAnInstrumentOrItsDisplay()
        {
            yield return Boot();
            yield return Until(() => Paint.RigReads > 0, 5f, "the instruments read");
            IReadOnlyList<PaintGallery.Slot> slots = Gallery.RoomSlots;
            Assert.That(slots.Count, Is.GreaterThanOrEqualTo(4));
            foreach (PaintGallery.Slot s in slots)
            {
                string b = Blocker(s, Room.Root);
                Assert.IsNull(b, $"the place at {Room.Root.TransformPoint(s.Centre)} is behind {b}");
                Assert.That(s.W, Is.GreaterThanOrEqualTo(PaintGallery.Widths[PaintGallery.Widths.Length - 1]));
            }
            float sum = 0f; foreach (PaintGallery.Slot s in slots) sum += s.W;
            Debug.Log($"[Gallery] {slots.Count} places clear of every instrument, mean picture width {sum / slots.Count:F2} m");
        }

        [UnityTest]
        public IEnumerator AFrameMovesWhenAnInstrumentIsPutInFrontOfIt()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(5f, 31), "Take 30 · 0:05", recs);
            yield return PlayToFrame(recs[0]);
            Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 1f);
            PaintGallery.Entry e = Gallery.Entries[0];
            PaintGallery.Slot before = Gallery.RoomSlots[e.Slot];
            Vector3 frame = Room.Root.TransformPoint(before.Centre), n = Room.Root.TransformDirection(before.Normal);
            // the player moves the harp to stand right in front of the painting
            Rig harp = App.Get<IRigRegistry>().Get(RigId.Harp);
            Assert.IsNotNull(harp);
            harp.Group.position = new Vector3(frame.x, 0f, frame.z) + n * 0.3f;
            App.Events.Publish(new ArrangedEvent { Forced = false });
            int reads = Paint.RigReads;
            Step(WallPaint.RigsSettle + 0.3f);
            Assert.That(Paint.RigReads, Is.GreaterThan(reads), "read again after the change");
            Assert.That(Gallery.Rehung, Is.EqualTo(1), "the painting moved");
            PaintGallery.Slot after = Gallery.RoomSlots[e.Slot];
            Assert.That(after.Wall, Is.GreaterThanOrEqualTo(0), "to another clear place");
            Assert.That((Room.Root.TransformPoint(after.Centre) - frame).magnitude, Is.GreaterThan(0.5f));
            Assert.IsNull(Blocker(after, Room.Root), "clear of the harp and the rest");
            for (int i = 0; i < Gallery.RoomSlots.Count; i++)
                if (i != e.Slot) Assert.IsFalse(PaintGallery.TooClose(after, Gallery.RoomSlots[i]), "not on another place");
            Assert.IsTrue(Gallery.PicturesRenderer(true).enabled);
        }

        [UnityTest]
        public IEnumerator TheDecksOwnTakeIsFoundByItsEvents()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(5f, 3), "Take 7 · 0:05", recs);
            // a copy of the events, as the deck plays its own list (the library has its own)
            var copy = new List<TakeEvent>(recs[0].events);
            var deck = new TakeRecord { id = null, events = copy, seconds = recs[0].seconds };
            Assert.IsTrue(Recorder.PlayEvents(deck.events, deck.seconds));
            Step(deck.seconds + 0.3f);
            yield return Until(() => !Gallery.Resolving, 10f, "looked up");
            Assert.IsTrue(Gallery.Ready, "found by its events");
            Assert.That(Gallery.PendingEntry.Id, Is.EqualTo(recs[0].id));
            Step(WallPaint.HoldSeconds + 0.2f);
            Assert.That(Gallery.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AShortOrUnsavedPlaybackHangsNothing()
        {
            yield return Boot();
            var song = Song(5f, 4);
            Assert.IsTrue(Recorder.PlayEvents(song, End(song)));   // never saved
            Step(End(song) + 0.3f);
            yield return Until(() => !Gallery.Resolving, 10f, "looked up");
            Step(WallPaint.HoldSeconds + WallPaint.FadeSeconds + 1f);
            Assert.That(Gallery.Count, Is.EqualTo(0));
            Assert.That(Paint.Gather, Is.EqualTo(-1f));
            Assert.That(Paint.Splashes, Is.EqualTo(0), "it simply fades");
        }

        [UnityTest]
        public IEnumerator TheGalleryIsBackAfterARebootWithTheSamePictures()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(6f, 5), "Take 2 · 0:06", recs);
            yield return Save(Song(6f, 6), "Take 3 · 0:06", recs);
            yield return PlayToFrame(recs[0]);
            Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 1f);
            yield return PlayToFrame(recs[1]);
            Step(WallPaint.GatherSeconds + 0.5f);
            yield return Until(() => Gallery.Entries.Count == 2 && Gallery.Entries[0].Uploaded && Gallery.Entries[1].Uploaded, 20f, "both shown");
            var before = new Dictionary<string, Color32[]>();
            var slots = new Dictionary<string, int>();
            foreach (PaintGallery.Entry e in Gallery.Entries) { before[e.Id] = e.Pixels; slots[e.Id] = e.Slot; }
            // shut the app down, keeping its files and settings
            TestBoot first = _boot;
            _keepData = first.DataPath;
            Object.Destroy(first.Root);
            if (RigBootstrap.Instance != null) Object.Destroy(RigBootstrap.Instance.gameObject);
            yield return null;
            yield return null;
            var again = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            again.Options.DataPath = first.DataPath;
            again.Options.Settings = first.Settings;
            yield return Boot(boot: again);
            // Never an empty frame at the start (2026-11-18): while no saved picture is painted yet, no frame is up.
            bool emptyFrame = false;
            System.Func<bool> noneShown = () =>
            {
                bool any = false;
                foreach (PaintGallery.Entry e in Gallery.Entries) any |= e.Uploaded;
                Renderer pr = Gallery.PicturesRenderer(true);
                if (!any && pr != null && pr.enabled) emptyFrame = true;
                return false;
            };
            yield return Until(() => { noneShown(); return Gallery.Loaded && Gallery.Count == 2; }, 10f, "the paintings are back");
            yield return Until(() => { noneShown(); return Gallery.Entries[0].Uploaded && Gallery.Entries[1].Uploaded; }, 30f, "their pictures painted again");
            Assert.IsFalse(emptyFrame, "a frame went up at the start before its picture was painted");
            Step(0.1f);   // the frames go up on the next Decor tick
            foreach (PaintGallery.Entry e in Gallery.Entries)
            {
                Assert.That(e.Slot, Is.EqualTo(slots[e.Id]), "in the same place");
                Assert.That(e.Name, Is.Not.Empty);
                Color32[] a = before[e.Id], b = e.Pixels;
                for (int i = 0; i < a.Length; i += 7) Assert.That(b[i], Is.EqualTo(a[i]), "the same picture");
            }
            Assert.IsTrue(Gallery.PicturesRenderer(true).enabled);
        }

        [UnityTest]
        public IEnumerator DeletingARecordingTakesItsPaintingDown()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(5f, 7), "Take 4 · 0:05", recs);
            yield return Save(Song(5f, 8), "Take 5 · 0:05", recs);
            yield return PlayToFrame(recs[0]);
            Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 1f);
            yield return PlayToFrame(recs[1]);
            Step(WallPaint.GatherSeconds + 0.5f);
            Assert.That(Gallery.Count, Is.EqualTo(2));
            var removing = Library.Remove(recs[0].id);
            yield return Until(() => removing.IsCompleted, 10f, "deleted");
            yield return Until(() => Gallery.Count == 1, 10f, "its painting comes down");
            Assert.That(Gallery.Entries[0].Id, Is.EqualTo(recs[1].id), "the other one stays");
            Assert.That(App.Settings.GetString(PaintGallery.Key, ""), Does.Not.Contain(recs[0].id));
            removing = Library.Remove(recs[1].id);
            yield return Until(() => removing.IsCompleted, 10f, "deleted");
            yield return Until(() => Gallery.Count == 0, 10f, "the last one down");
            Step(0.2f);
            Assert.IsFalse(Gallery.PicturesRenderer(true) != null && Gallery.PicturesRenderer(true).enabled, "no frame drawn");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheNewestPaintingReplacesTheOldest()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            for (int i = 0; i <= PaintGallery.Max; i++) yield return Save(Song(2.4f, 20 + i), $"Take {10 + i} · 0:02", recs);
            for (int i = 0; i <= PaintGallery.Max; i++)
            {
                yield return PlayToFrame(recs[i]);
                Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 0.5f);
            }
            int places = Mathf.Min(PaintGallery.Max, Gallery.RoomSlots.Count);
            Debug.Log($"[Gallery] {Gallery.RoomSlots.Count} places for frames in the VR room");
            Assert.That(places, Is.GreaterThanOrEqualTo(4), "room for a small gallery");
            Assert.That(Gallery.Count, Is.EqualTo(places));
            Assert.IsNull(Gallery.Find(recs[0].id), "the oldest gave way");
            Assert.IsNotNull(Gallery.Find(recs[PaintGallery.Max].id));
            var used = new HashSet<int>();
            foreach (PaintGallery.Entry e in Gallery.Entries) Assert.IsTrue(used.Add(e.Slot), "one painting per place");
        }

        [UnityTest]
        public IEnumerator KeepPaintingKeepsTheWallsAndStillFramesTheSong()
        {
            yield return Boot();
            App.Settings.KeepPaintings = true;
            var recs = new List<TakeRecord>();
            yield return Save(Song(5f, 9), "Take 6 · 0:05", recs);
            yield return PlayToFrame(recs[0]);
            int kept = Paint.Splashes;
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Kept));
            Assert.That(Gallery.Count, Is.EqualTo(1), "framed as well");
            Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 2f);
            Assert.That(Paint.Splashes, Is.EqualTo(kept), "the walls stay painted");
            Assert.That(Paint.Fade, Is.EqualTo(1f));
        }

        // ------------------------------------------------------------------------------------------------ mixed reality

        sealed class FakeWalls : IRealWalls
        {
            public bool Scene, Depth;
            public int Version => 1;
            public int WallCount => Scene ? 4 : 0;
            public int BlockCount => Scene ? 1 : 0;
            public bool Refresh() => Scene;
            public RealWall Wall(int i)
            {
                switch (i)
                {
                    case 0: return new RealWall { Centre = new Vector3(0f, 1.25f, 1.8f), Inward = Vector3.back, Size = new Vector2(4f, 2.5f) };
                    case 1: return new RealWall { Centre = new Vector3(0f, 1.25f, -1.8f), Inward = Vector3.forward, Size = new Vector2(4f, 2.5f) };
                    case 2: return new RealWall { Centre = new Vector3(2f, 1.25f, 0f), Inward = Vector3.left, Size = new Vector2(3.6f, 2.5f) };
                    default: return new RealWall { Centre = new Vector3(-2f, 1.25f, 0f), Inward = Vector3.right, Size = new Vector2(3.6f, 2.5f) };
                }
            }
            public RealWallBlock Block(int i) => new RealWallBlock { Centre = new Vector3(-0.8f, 1f, 1.8f), Half = new Vector3(0.45f, 1f, 0.05f), Rotation = Quaternion.identity };
            public bool DepthWall(Ray ray, out Vector3 point, out Vector3 inward)
            {
                point = inward = default;
                if (!Depth || ray.direction.z < 0.2f) return false;
                float t = (2f - ray.origin.z) / ray.direction.z;
                point = ray.origin + ray.direction * t;
                inward = Vector3.back;
                return point.y > 0.2f && point.y < 2.6f;
            }
        }

        [UnityTest]
        public IEnumerator MixedRealityFramesOnTheRealWallsOnlyWithASceneModel()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(6f, 11), "Take 8 · 0:06", recs);
            yield return Save(Song(6f, 12), "Take 9 · 0:06", recs);
            Room.SetVisible(false);
            Paint.RealWallsOverride = new FakeWalls { Scene = true };
            yield return PlayToFrame(recs[0]);
            Assert.That(Gallery.Count, Is.EqualTo(1));
            Assert.That(Gallery.MrSlots.Count, Is.GreaterThan(0));
            PaintGallery.Slot s = Gallery.MrSlots[Gallery.Entries[0].Slot];
            bool onWall = Mathf.Abs(Mathf.Abs(s.Centre.z) - 1.8f) < 0.01f || Mathf.Abs(Mathf.Abs(s.Centre.x) - 2f) < 0.01f;
            Assert.IsTrue(onWall, $"on a real wall: {s.Centre}");
            Assert.IsFalse(s.Centre.z > 1.7f && Mathf.Abs(s.Centre.x + 0.8f) < 0.45f + 0.4f, "not over the door");
            Assert.IsTrue(Gallery.MrShown, "shown in mixed reality");
            Assert.IsTrue(Gallery.MrObject.activeSelf);
            Assert.IsTrue(Paint.MrLayer.GatherMeshRenderer.enabled, "swirling on the real walls");
            Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 1f);
            // only the depth sensor: no frame for the next one
            Paint.RealWallsOverride = new FakeWalls { Depth = true };
            yield return PlayToFrame(recs[1]);
            Assert.That(Gallery.Count, Is.EqualTo(1), "no gallery without a scene model");
            // back in VR the first one hangs in the room too
            Room.SetVisible(true);
            Step(0.5f);
            Assert.IsFalse(Gallery.MrShown);
            Assert.IsTrue(Gallery.PicturesRenderer(true).enabled);
        }

        // ------------------------------------------------------------------------------------------------ throwing paint

        /// <summary>The right hand flicks from in front of the chest toward dir (world): speeding up to peak over 0.12 s,
        /// stopping over 0.08 s, then held still; its pose open, the palm facing down and forward.</summary>
        void Flick(Vector3 dir, float peak = 2.8f, Vector3? start = null)
        {
            Transform head = App.Head;
            Vector3 f = head.forward; f.y = 0f; f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 wrist = start ?? head.position + f * 0.22f + r * 0.18f - Vector3.up * 0.28f;
            dir.Normalize();
            // the hand comes up to where it throws from, then flicks
            for (int i = 0; i < 12; i++) { _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, dir, Vector3.down)); Frame(); }
            for (int i = 0; i < 40; i++)
            {
                float t = i * Dt;
                float s = t < 0.12f ? peak * (t / 0.12f) : t < 0.2f ? peak * (1f - (t - 0.12f) / 0.08f) : 0f;
                wrist += dir * (s * Dt);
                _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, dir, Vector3.down));
                Frame();
            }
        }

        void HandsAway()
        {
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, new Vector3(0.4f, 0.6f, -0.3f), Vector3.forward, Vector3.left));
            _boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, new Vector3(-0.4f, 0.6f, -0.3f), Vector3.forward, Vector3.right));
            for (int i = 0; i < 10; i++) Frame();
        }

        Vector3 Ahead(float up = 0.25f, float side = 0f)
        {
            Vector3 f = App.Head.forward; f.y = 0f; f.Normalize();
            return f + Vector3.up * up + Vector3.Cross(Vector3.up, f) * side;
        }

        [UnityTest]
        public IEnumerator AFlickDuringAPlaybackThrowsPaintThatSplatsOnTheWall()
        {
            yield return Boot(tipsSeen: "tip_listen_back,tip_play_brings,tip_song_stars,tip_window_show,tip_ghost_hands,tip_paint_walls");
            Paint.HandBusyOverride = _ => false;
            HandsAway();
            var song = Song(14f, 13);
            Assert.IsTrue(Recorder.PlayEvents(song, End(song)));
            Step(1f);
            int before = Paint.Splashes;
            Flick(Ahead(0.2f, 0.5f));
            Assert.That(Paint.Throws, Is.EqualTo(1), "one throw: " + Paint.Gesture.Why);
            Step(0.8f);
            Assert.IsFalse(Paint.BlobFlying, "landed");
            Assert.That(Paint.Splats, Is.EqualTo(1), "a splat on the wall");
            Assert.That(Paint.SplatSounds, Is.EqualTo(1), "a splat sound");
            Bounds box = Room.RoomBox;
            Vector3 p = Paint.LastSplat;
            float dx = Mathf.Min(Mathf.Abs(p.x - box.min.x), Mathf.Abs(p.x - box.max.x)), dz = Mathf.Min(Mathf.Abs(p.z - box.min.z), Mathf.Abs(p.z - box.max.z));
            Assert.That(Mathf.Min(dx, dz), Is.LessThan(0.02f), $"on a wall: {p}");
            Assert.That(p.y, Is.InRange(0.5f, box.max.y - 0.3f));
            Assert.That(Paint.Splashes, Is.GreaterThan(before), "on the wall's paint");
            // where it was thrown: roughly along the throw
            Vector3 to = (p - App.Head.position).normalized;
            Assert.That(Vector3.Angle(to, Ahead(0.2f, 0.5f)), Is.LessThan(30f));
            // a slow reach throws nothing
            Flick(Ahead(0.2f), 0.8f);
            Assert.That(Paint.Throws, Is.EqualTo(1), "slow: no throw");
            Step(6f);
            if (!Paint.ThrowTipAsked)
            {
                var guide = App.Get<IGuide>();
                Debug.Log($"[PaintShow] throw tip not asked: seen walls {guide.TipSeen(WallPaint.TipId)}, asked walls {Paint.TipAsked}, may ask {PlaybackTips.MayAsk(guide, WallPaint.ThrowTipId)}, pending {PlaybackTips.Pending}, state {Paint.State}, tip now {guide.Tip(WallPaint.ThrowTipId)}");
            }
            Assert.IsTrue(Paint.ThrowTipAsked, "Sunny: flick your hand at a wall");
        }

        [UnityTest]
        public IEnumerator NoThrowsOutsideAPlaybackOrFromAHandThatPlays()
        {
            yield return Boot();
            HandsAway();
            Flick(Ahead(0.2f, 0.4f));
            Step(1f);
            Assert.That(Paint.Throws + Paint.Splats, Is.EqualTo(0), "nothing plays back: no paint to throw");
            // during a playback: the hand that just played the piano is busy (Layout's HandBusy)
            var song = Song(10f, 14);
            Assert.IsTrue(Recorder.PlayEvents(song, End(song)));
            Step(1f);
            IInstrumentFixture fx = FixtureContext.For(InstrumentKind.Piano);
            var ctx = new FixtureContext { Boot = _boot, Frame = Frame };
            fx.Strike(ctx);
            Flick(Ahead(0.2f, 0.4f));
            Step(1f);
            Assert.That(Paint.Splats, Is.EqualTo(0), "never from a hand that is playing");
            // after a while the same hand throws
            HandsAway();
            Step(1.5f);
            Flick(Ahead(0.2f, 0.4f));
            Step(1f);
            Assert.That(Paint.Splats, Is.EqualTo(1), "free again: " + Paint.Gesture.Why);
        }

        [UnityTest]
        public IEnumerator MixedRealityThrowsLandOnRealWallsAndNeverInTheAir()
        {
            yield return Boot();
            Paint.HandBusyOverride = _ => false;
            HandsAway();
            Room.SetVisible(false);
            Paint.RealWallsOverride = new FakeWalls { Scene = true };
            var song = Song(10f, 15);
            Assert.IsTrue(Recorder.PlayEvents(song, End(song)));
            Step(1f);
            Flick(Ahead(0.15f, 0.2f));
            Step(1f);
            Assert.That(Paint.Splats, Is.EqualTo(1), "on the real wall: " + Paint.Gesture.Why);
            Vector3 p = Paint.LastSplat;
            bool onWall = Mathf.Abs(Mathf.Abs(p.z) - 1.8f) < 0.02f || Mathf.Abs(Mathf.Abs(p.x) - 2f) < 0.02f;
            Assert.IsTrue(onWall, $"on a real wall: {p}");
            Recorder.Stop();
            Step(0.5f);
            // no walls known at all: the blob flies and is gone, nothing sticks to the air
            Paint.RealWallsOverride = new FakeWalls();
            Assert.IsTrue(Recorder.PlayEvents(song, End(song)));
            Step(1f);
            int splashes = Paint.Splashes;
            Flick(Ahead(0.15f, -0.2f));
            Step(1f);
            Assert.That(Paint.Throws, Is.EqualTo(2));
            Assert.That(Paint.Splats, Is.EqualTo(1), "never in the air");
            Assert.That(Paint.Splashes, Is.LessThanOrEqualTo(splashes), "nothing new (the last painting washing away)");
        }

        // ------------------------------------------------------------------------------------------------ cost

        [UnityTest]
        [Category("Performance")]
        public IEnumerator TheGalleryCostsNothingAtRestAndAPictureIsQuick()
        {
            yield return Boot();
            var recs = new List<TakeRecord>();
            yield return Save(Song(30f, 16), "Take 20 · 0:30", recs);
            // the picture of a 30 s song (the worker's job)
            var notes = new List<PictureNote>();
            float len = GalleryPicture.Notes(recs[0].events, App.Registry, notes);
            var px = new Color32[GalleryPicture.Width * GalleryPicture.Height];
            GalleryPicture.Compose(notes, len, BrushAtlas.Shared, px);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) GalleryPicture.Compose(notes, len, BrushAtlas.Shared, px);
            double composeMs = sw.Elapsed.TotalMilliseconds / 5.0;
            yield return PlayToFrame(recs[0]);
            Step(WallPaint.GatherSeconds + WallPaint.FadeSeconds + 1f);
            yield return Until(() => Gallery.Entries[0].Uploaded, 20f, "shown");
            WallPaint paint = Paint;
            const int frames = 3000;
            for (int i = 0; i < 100; i++) { _audio += Dt; paint.Tick(Dt, true); }
            sw.Restart();
            for (int i = 0; i < frames; i++) { _audio += Dt; paint.Tick(Dt, true); }
            double restUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Assert.That(() => { for (int i = 0; i < 300; i++) { _audio += Dt; paint.Tick(Dt, true); } }, Is.Not.AllocatingGCMemory(), "a frame at rest with a painting hung");
            Debug.Log($"[PERF] paint gallery: a 30 s song's picture {composeMs:F1} ms on a worker ({notes.Count} notes); the paint with a framed painting at rest " +
                      $"{restUs:F2} us/frame (editor, Apple silicon; GPU per place: 4 static draws for all frames: gilt, brass, pictures, words)");
            Assert.That(restUs, Is.LessThan(10.0));
            Assert.That(composeMs, Is.LessThan(250.0));
        }
    }
}
