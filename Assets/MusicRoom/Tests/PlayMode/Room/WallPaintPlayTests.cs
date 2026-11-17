// The notes paint the walls (Room/Decor/WallPaint.cs), in the booted app:
//   - plain live play, recording (REC) and played-back notes outside a playback of a recording (a live loop session's
//     layers) paint nothing; playing a recording back paints, one splash per note, on the VR room's walls;
//   - every splash lies on a wall of the room (3 mm off it, inside the room's box, wherever the room is turned), in the
//     paint band, never over the window or the furniture at the walls, never near the head;
//   - after the playback the painting holds, then fades, and the walls are exactly clean again (no splash, no draw);
//   - Keep painting keeps it after the playback until the next one, which washes it away as it paints its own; turning
//     Keep painting off lets it fade;
//   - mixed reality: on the scene model's walls (clear of its door), on the depth sensor's wall when there is no scene
//     model, and nothing at all with neither (never in the air); the VR room's paint goes with the room;
//   - Sunny is asked for tip_paint_walls while it paints and tip_paint_keep when it is about to fade (the throw and the
//     gallery lines, between them in PlaybackTips' order, heard before);
//   - a painting frame allocates nothing.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class WallPaintPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        WallPaint Paint => Room.WallPaint;
        IRecorder Recorder => App.Get<IRecorder>();
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }

        IEnumerator Boot(bool headless = false, string tipsSeen = null)
        {
            _audio = 10.0;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            if (tipsSeen != null) _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, tipsSeen);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Step(0.3f);
            Paint.FinishAtlas();   // the brush atlas is made on a worker at boot: have it before the paint shows
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        /// <summary>A song of piano notes (and a drum every fourth beat), gap seconds apart.</summary>
        static List<TakeEvent> Song(float seconds, float gap = 0.25f)
        {
            var take = new List<TakeEvent>();
            int[] tune = { 60, 64, 67, 72, 67, 64, 62, 65, 69, 74, 69, 65, 48, 84 };
            int i = 0;
            for (float t = 0.05f; t < seconds; t += gap, i++)
            {
                int m = tune[i % tune.Length];
                TakeEvent on = TakeEvent.PianoOn(m, i % 4 == 0 ? 1f : 0.55f); on.t = t; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = t + gap * 0.9f; take.Add(off);
                if (i % 4 == 2) { TakeEvent d = TakeEvent.Drum(i % 8 == 2 ? "snare" : "kick", 0.8f, 0.2f, "head", false); d.t = t; take.Add(d); }
            }
            return take;
        }

        static float TakeEnd(List<TakeEvent> take) => take[take.Count - 1].t + 0.3f;

        /// <summary>Every splash of the VR room's layer as a world point (its centre, 3 mm off the wall).</summary>
        List<Vector3> RoomSplashes()
        {
            var list = new List<Vector3>();
            WallPaint.Layer l = Paint.RoomLayer;
            Transform root = Room.Root;
            for (int s = 0; s < WallPaint.MaxQuads; s++) if (l.Gen[s] != 255) list.Add(root.TransformPoint(l.Centre[s]));
            return list;
        }

        // ------------------------------------------------------------------------------------------------ only a playback

        [UnityTest]
        public IEnumerator PlainPlayRecordingAndLiveLoopsPaintNothingAPlaybackPaints()
        {
            yield return Boot();
            IInstrumentFixture fx = FixtureContext.For(InstrumentKind.Piano);
            var ctx = new FixtureContext { Boot = _boot, Frame = Frame };
            for (int i = 0; i < 4; i++) { fx.Strike(ctx); Step(0.2f); }
            Assert.That(Paint.Painted, Is.EqualTo(0), "plain play paints nothing");
            Recorder.Record();
            for (int i = 0; i < 6; i++) { fx.Strike(ctx); Step(0.15f); }
            Assert.That(Paint.Painted, Is.EqualTo(0), "recording paints nothing");
            Recorder.Stop();
            Step(0.5f);
            // played-back notes with no playback of a recording running (a live loop session's own layers)
            for (int i = 0; i < 5; i++) { App.Events.Publish(new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.8f, Lane = 1, Event = TakeEvent.PianoOn(60 + i, 0.8f) }); Frame(); }
            Assert.That(Paint.Painted, Is.EqualTo(0), "a live loop session paints nothing");
            Assert.That(Paint.Splashes, Is.EqualTo(0));
            Assert.That(Paint.RoomLayer.MeshRenderer.enabled, Is.False);

            var song = Song(6f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(3f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Painting));
            Assert.That(Paint.Painted, Is.GreaterThan(8), "the playback paints");
            Assert.That(Paint.Splashes, Is.GreaterThanOrEqualTo(Paint.Painted), "each note one mark or more (its instrument's brush)");
            Assert.That(Paint.RoomLayer.Count, Is.EqualTo(Paint.Splashes), "on the VR room's walls");
            Assert.That(Paint.RoomLayer.MeshRenderer.enabled, Is.True, "one draw");
            Assert.That(Paint.MrLayer.MeshRenderer.enabled, Is.False);
            Assert.That(Paint.Missed, Is.EqualTo(0), $"every note found clear wall (missed: piano {Paint.MissedBy[(int)InstrumentKind.Piano]}, drums {Paint.MissedBy[(int)InstrumentKind.Drums]})");
        }

        // ------------------------------------------------------------------------------------------------ where it lands

        [UnityTest]
        public IEnumerator EverySplashLiesOnAClearWallOfTheRoom()
        {
            yield return Boot();
            var song = Song(40f, 0.12f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(41f);
            List<Vector3> splashes = RoomSplashes();
            Assert.That(splashes.Count, Is.GreaterThan(150));
            Bounds box = Room.RoomBox;   // world (the room turned to face the fire, or not)
            Vector3 head = App.Head.position;
            var furniture = App.Get<IRoomFurniture>();
            Transform window = Room.Scene.Window;
            int onWalls = 0;
            foreach (Vector3 p in splashes)
            {
                Assert.That(p.y, Is.InRange(0.45f, box.max.y - 0.25f), "in the paint band, off the ceiling");
                Assert.That((p - head).magnitude, Is.GreaterThan(WallPaint.HeadClear + 0.2f));
                float dx = Mathf.Min(Mathf.Abs(p.x - box.min.x), Mathf.Abs(p.x - box.max.x));
                float dz = Mathf.Min(Mathf.Abs(p.z - box.min.z), Mathf.Abs(p.z - box.max.z));
                float d = Mathf.Min(dx, dz);
                Assert.That(d, Is.LessThan(0.012f), $"on a wall: {p}");
                Assert.IsTrue(new Bounds(box.center, box.size + new Vector3(0.02f, 0.02f, 0.02f)).Contains(p) && d < 0.012f, $"inside the room: {p} in {box}");
                onWalls++;
                // not on the window: its frame's own space (x across, y up), the opening 1.8 x 1.7 m and its frame
                Vector3 w = window.InverseTransformPoint(p);
                if (Mathf.Abs(w.z) < 0.3f) Assert.IsFalse(Mathf.Abs(w.x) < 1.05f && Mathf.Abs(w.y) < 0.98f, $"over the window: {p}");
                // not behind furniture standing at a wall (the solid blockers; seen straight on from 0 to 0.4 m out)
                Vector3 inward = (box.center - p); inward.y = 0f;
                inward = dx < dz ? new Vector3(Mathf.Sign(inward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(inward.z));
                if (furniture == null) continue;
                foreach (FurnitureBlocker b in furniture.FloorBlockers)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(b.Yaw, Mathf.Round(b.Yaw / 90f) * 90f)) > 1f) continue;   // square to the room only
                    for (float t = 0f; t <= 0.4f; t += 0.05f)
                    {
                        Vector3 q = p + inward * t;
                        bool inside = Mathf.Abs(q.x - b.Centre.x) < b.Half.x - 0.05f && Mathf.Abs(q.z - b.Centre.y) < b.Half.y - 0.05f && q.y > b.Y0 + 0.05f && q.y < b.Y1 - 0.05f;
                        Assert.IsFalse(inside, $"splash {p} behind {b.Name}");
                    }
                }
            }
            Debug.Log($"[WallPaint] {onWalls} splashes on the walls, {Paint.Missed} notes missed (piano {Paint.MissedBy[(int)InstrumentKind.Piano]}, drums {Paint.MissedBy[(int)InstrumentKind.Drums]}), {Paint.Painted} painted");
        }

        // ------------------------------------------------------------------------------------------------ fading, keeping

        [UnityTest]
        public IEnumerator AfterThePlaybackItHoldsThenFadesToExactlyCleanWalls()
        {
            yield return Boot();
            var song = Song(4f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(TakeEnd(song) + 0.3f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Holding), "the playback ended");
            int n = Paint.Splashes;
            Assert.That(n, Is.GreaterThan(5));
            Step(WallPaint.HoldSeconds - 0.5f);
            Assert.That(Paint.Splashes, Is.EqualTo(n), "it stays a while");
            Assert.That(Paint.Fade, Is.EqualTo(1f));
            Step(0.5f + WallPaint.FadeSeconds * 0.5f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Fading));
            Assert.That(Paint.Fade, Is.InRange(0.2f, 0.8f), "fading gently");
            Step(WallPaint.FadeSeconds * 0.5f + 0.2f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Idle));
            Assert.That(Paint.Splashes, Is.EqualTo(0), "every splash gone");
            Assert.That(Paint.RoomLayer.MeshRenderer.enabled, Is.False, "no draw: the walls are their own again");
            Assert.That(Paint.MrLayer.MeshRenderer.enabled, Is.False);
            for (int s = 0; s < WallPaint.MaxQuads; s++) Assert.That(Paint.RoomLayer.Gen[s], Is.EqualTo(255));
            // the next playback paints from scratch
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(2f);
            Assert.That(Paint.Splashes, Is.GreaterThan(3));
            Assert.That(Paint.Fade, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator KeepPaintingKeepsItUntilTheNextPlayback()
        {
            yield return Boot();
            App.Settings.KeepPaintings = true;
            var song = Song(4f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(TakeEnd(song) + WallPaint.HoldSeconds + WallPaint.FadeSeconds + 5f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Kept));
            int kept = Paint.Splashes;
            Assert.That(kept, Is.GreaterThan(5), "kept on the walls");
            Assert.That(Paint.Fade, Is.EqualTo(1f));
            Assert.That(Paint.RoomLayer.MeshRenderer.enabled, Is.True);
            int oldGen = Paint.Generation;
            // the next playback washes it away while it paints its own
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(0.5f);
            Assert.That(Paint.Generation, Is.Not.EqualTo(oldGen));
            Assert.That(Paint.RoomLayer.Alive[oldGen], Is.EqualTo(kept), "the old one still washing away");
            Assert.That(Paint.FadeOf(oldGen), Is.LessThan(1f));
            Step(WallPaint.ClearSeconds);
            Assert.That(Paint.RoomLayer.Alive[oldGen], Is.EqualTo(0), "gone");
            Assert.That(Paint.RoomLayer.Alive[Paint.Generation], Is.GreaterThan(3), "the new one painting");
            // Keep painting off: the kept painting fades after all
            Step(TakeEnd(song) + WallPaint.HoldSeconds + 1f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Kept));
            App.Settings.KeepPaintings = false;
            Step(1f);
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Fading));
            Step(WallPaint.FadeSeconds + 0.5f);
            Assert.That(Paint.Splashes, Is.EqualTo(0));
            Assert.That(Paint.State, Is.EqualTo(WallPaint.Phase.Idle));
        }

        // ------------------------------------------------------------------------------------------------ mixed reality

        /// <summary>A real room for the tests: four walls of a 4 x 3.6 m room round the player, a door on the one ahead, or
        /// only a depth wall 2 m ahead, or nothing.</summary>
        sealed class FakeWalls : IRealWalls
        {
            public bool Scene, Depth;
            public int Version => 1;
            public int WallCount => Scene ? 4 : 0;
            public int BlockCount => Scene ? 1 : 0;
            public bool Refresh() => Scene;
            public int DepthAsked;

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

            /// <summary>A door on the wall ahead, left of the middle.</summary>
            public RealWallBlock Block(int i) => new RealWallBlock { Centre = new Vector3(-0.8f, 1f, 1.8f), Half = new Vector3(0.45f, 1f, 0.05f), Rotation = Quaternion.identity };

            public bool DepthWall(Ray ray, out Vector3 point, out Vector3 inward)
            {
                DepthAsked++;
                point = inward = default;
                if (!Depth || ray.direction.z < 0.2f) return false;
                float t = (2f - ray.origin.z) / ray.direction.z;
                point = ray.origin + ray.direction * t;
                inward = Vector3.back;
                return point.y > 0.2f && point.y < 2.6f;
            }
        }

        IEnumerator PlayInMr(FakeWalls walls, float seconds)
        {
            Room.SetVisible(false);
            Paint.RealWallsOverride = walls;
            var song = Song(seconds, 0.2f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(seconds + 0.2f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MixedRealityPaintsTheSceneWallsClearOfTheDoor()
        {
            yield return Boot();
            var walls = new FakeWalls { Scene = true };
            yield return PlayInMr(walls, 12f);
            Assert.That(Paint.MrHasWalls, Is.True);
            Assert.That(Paint.MrLayer.Count, Is.GreaterThan(30), "on the real walls");
            Assert.That(Paint.RoomLayer.Count, Is.EqualTo(0), "nothing on the hidden VR room");
            Assert.That(Paint.MrLayer.MeshRenderer.enabled, Is.True);
            Assert.That(Paint.RoomLayer.MeshRenderer.enabled, Is.False);
            Assert.That(walls.DepthAsked, Is.EqualTo(0), "the scene model, not the depth sensor");
            WallPaint.Layer l = Paint.MrLayer;
            for (int s = 0; s < WallPaint.MaxQuads; s++)
            {
                if (l.Gen[s] == 255) continue;
                Vector3 p = l.Centre[s];
                bool onWall = Mathf.Abs(Mathf.Abs(p.z) - 1.8f) < 0.01f || Mathf.Abs(Mathf.Abs(p.x) - 2f) < 0.01f;
                Assert.IsTrue(onWall, $"on a real wall: {p}");
                Assert.That(p.y, Is.InRange(0.45f, 2.25f));
                Assert.IsFalse(p.z > 1.7f && Mathf.Abs(p.x + 0.8f) < 0.5f && p.y < 2.05f, $"over the door: {p}");
            }
        }

        [UnityTest]
        public IEnumerator MixedRealityWithoutASceneModelPaintsTheDepthWall()
        {
            yield return Boot();
            var walls = new FakeWalls { Depth = true };
            yield return PlayInMr(walls, 8f);
            Assert.That(walls.DepthAsked, Is.GreaterThan(72 * 4), "a ray a frame while it paints");
            Assert.That(Paint.MrLayer.Count, Is.GreaterThan(10));
            WallPaint.Layer l = Paint.MrLayer;
            for (int s = 0; s < WallPaint.MaxQuads; s++)
                if (l.Gen[s] != 255) Assert.That(l.Centre[s].z, Is.EqualTo(2f).Within(0.01f), "on the wall the sensor saw");
        }

        [UnityTest]
        public IEnumerator MixedRealityWithNoWallsPaintsNothing()
        {
            yield return Boot();
            var walls = new FakeWalls();
            yield return PlayInMr(walls, 5f);
            Assert.That(Paint.Painted, Is.EqualTo(0), "never in the air");
            Assert.That(Paint.Missed, Is.GreaterThan(10));
            Assert.That(Paint.Splashes, Is.EqualTo(0));
            Assert.That(Paint.MrLayer.MeshRenderer.enabled, Is.False);
            // and the VR room's painting stays with the room: back in VR it paints the room's walls
            Room.SetVisible(true);
            var song = Song(3f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(2f);
            Assert.That(Paint.RoomLayer.Count, Is.GreaterThan(3));
        }

        // ------------------------------------------------------------------------------------------------ Sunny, allocations

        /// <summary>The other playback lines, heard before (Sunny is free for these).</summary>
        const string OtherTips = "tip_listen_back,tip_play_brings,tip_song_stars,tip_window_show,tip_ghost_hands";
        /// <summary>The paint's own lines between tip_paint_walls and tip_paint_keep in PlaybackTips' order.</summary>
        const string PaintBetween = "," + WallPaint.ThrowTipId + "," + WallPaint.GalleryTipId;

        [UnityTest]
        public IEnumerator SunnyIsAskedForTheLinesOnce()
        {
            yield return Boot(tipsSeen: OtherTips + PaintBetween);
            Assert.IsFalse(Paint.TipAsked);
            var song = Song(12f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(1f);
            Assert.IsFalse(Paint.TipAsked, "not on the first splash");
            Step(5f);
            Assert.IsTrue(Paint.TipAsked, "tip_paint_walls once it has painted a moment");
            Assert.IsFalse(Paint.KeepTipAsked, "not while it paints");
            Step(TakeEnd(song) - 6f + WallPaint.HoldSeconds * 0.6f);
            Assert.IsFalse(Paint.KeepTipAsked, "one of the show's lines per playback (PlaybackTips): not after tip_paint_walls");
            Step(WallPaint.HoldSeconds + WallPaint.FadeSeconds + 1f);
            Assert.IsTrue(Recorder.PlayEvents(song, TakeEnd(song)));
            Step(TakeEnd(song) + WallPaint.HoldSeconds * 0.6f);
            Assert.IsTrue(Paint.KeepTipAsked, "tip_paint_keep in the next playback, once it is about to fade");
        }

        [UnityTest]
        public IEnumerator APaintingFrameAllocatesNothing()
        {
            yield return Boot(tipsSeen: OtherTips + PaintBetween + "," + WallPaint.TipId + "," + WallPaint.KeepTipId);
            WallPaint paint = Paint;
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f, Lane = PlaybackLanes.Take, Event = TakeEvent.PianoOn(64, 0.9f) };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 0.7f, Lane = PlaybackLanes.Take, Event = TakeEvent.Drum("snare", 0.7f, 0.2f, "head", false) };
            int f = 0;
            void Painting(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    if (++f % 5 == 0) App.Events.Publish(on);
                    if (f % 7 == 0) App.Events.Publish(drum);
                    _audio += Dt;
                    paint.Tick(Dt, true);
                }
            }
            void Rest(int frames) { for (int i = 0; i < frames; i++) { _audio += Dt; paint.Tick(Dt, true); } }
            const int Hold = (int)((WallPaint.HoldSeconds + WallPaint.FadeSeconds + 1f) * 72f);
            // warm: a whole painting, its hold and fade (the one-off asks of Sunny behind us). The playback is the piano's alone
            // (at the seat already): Layout does not run in these frames, so a stage it brought would never go back
            App.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 1u });
            Painting(400);
            App.Events.Publish(new RecordingPlaybackEvent { Started = false });
            Rest(Hold);
            Assert.That(paint.Splashes, Is.EqualTo(0));
            paint.ReadRigsNow();   // what stands at the walls read once (the gallery's places found): a one-off at a layout change
            Rest(10);
            App.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 1u });
            Painting(20);
            Assert.That(() => Painting(300), Is.Not.AllocatingGCMemory(), "painting");
            Assert.That(paint.Painted, Is.GreaterThan(60));
            App.Events.Publish(new RecordingPlaybackEvent { Started = false });
            Assert.That(() => Rest(Hold), Is.Not.AllocatingGCMemory(), "holding and fading");
            Assert.That(paint.Splashes, Is.EqualTo(0));
        }
    }
}
