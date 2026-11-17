// Pictures of the nature paint (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at and judge:
//   - VR: a song of every instrument played back from Recordings: the seat facing the fire, turned left / right / behind,
//     close-ups of each motif (the piano's flowering vine, a tree, leaves drifting, a flock of birds, a cloud, a butterfly,
//     mushrooms, grass), a thrown burst of petals; the end: the swirl into the frame, the framed landscape and its plaque;
//     a small gallery of three songs, and each song's picture itself (gallery_picture_N.png);
//   - mixed reality: beige stand-in walls round the seat (a door on the wall ahead): the paint and a frame on them.
// Output: $MUSICROOM_SHOTS_ROOT/paintshow, else Builds/Shots/paintshow.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Textures;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class PaintShowShotsTests
    {
        const int W = 1280, H = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 20.0;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _read;
        string _dir;
        readonly List<Object> _made = new List<Object>();

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        WallPaint Paint => Room.WallPaint;
        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }
        void StepSeconds(float s) => Step(Mathf.RoundToInt(s / Dt));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            foreach (Object o in _made) if (o != null) Object.Destroy(o);
            _made.Clear();
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            PlayingTake.Clear();
        }

        static TakeEvent At(TakeEvent e, float t) { e.t = t; return e; }

        /// <summary>A song of every instrument: a piano melody in phrases with chords, drums, harp glissandi, bowls, the
        /// handpan, the hand drums and marimba runs, each coming in turn.</summary>
        static List<TakeEvent> Showcase(float seconds, int seed = 3)
        {
            var take = new List<TakeEvent>();
            var rng = new System.Random(seed);
            int[] tune = { 64, 67, 69, 72, 71, 69, 67, 64, 62, 64, 67, 72, 74, 72, 69, 67 };
            int n = 0;
            for (float phrase = 0.2f; phrase < seconds; phrase += 3.6f)
            {
                for (int k = 0; k < 9; k++, n++)
                {
                    float t = phrase + k * 0.28f;
                    int m = tune[n % tune.Length] + (n / 32 % 2 == 1 ? -5 : 0);
                    float vel = 0.35f + 0.6f * Mathf.Abs(Mathf.Sin(n * 0.41f));
                    take.Add(At(TakeEvent.PianoOn(m, vel), t)); take.Add(At(TakeEvent.PianoOff(m), t + 0.25f));
                    if (k == 0 || k == 5) { take.Add(At(TakeEvent.PianoOn(m - 12, 0.5f), t)); take.Add(At(TakeEvent.PianoOn(m - 8, 0.45f), t)); take.Add(At(TakeEvent.PianoOff(m - 12), t + 0.6f)); take.Add(At(TakeEvent.PianoOff(m - 8), t + 0.6f)); }
                }
            }
            for (float t = 0.5f; t < seconds; t += 0.6f)
            {
                int beat = Mathf.RoundToInt(t / 0.6f);
                if (beat % 4 == 0) take.Add(At(TakeEvent.Drum("kick", 0.9f, 0.2f, "head", false), t));
                else if (beat % 4 == 2) take.Add(At(TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false), t));
                if (beat % 16 == 15) take.Add(At(TakeEvent.Drum("crash", 0.95f, 0.5f, "bow", false), t));
                if (beat % 8 == 5) take.Add(At(TakeEvent.Drum("tom1", 0.75f, 0.3f, "head", false), t));
            }
            for (float t = 2f; t < seconds; t += 5.5f)
                for (int k = 0; k < 7; k++) take.Add(At(TakeEvent.Harp(4 + k * 2, 0.75f, 0.5f, 0.6f, "through"), t + 0.07f * k));
            for (float t = 3.1f; t < seconds; t += 6.3f) take.Add(At(new TakeEvent { type = "bowlStrike", i = rng.Next(3), vel = 0.7f, kind = "tap", midi = 1200 }, t));
            for (float t = 4.4f; t < seconds; t += 2.4f) take.Add(At(new TakeEvent { type = "handpan", i = 1 + rng.Next(8), midi = 57 + rng.Next(12), vel = 0.7f, mode = "tip", zone = "tone" }, t));
            for (float t = 1.3f; t < seconds; t += 1.8f) take.Add(At(new TakeEvent { type = "handDrum", kind = rng.Next(2) == 0 ? "conga" : "tumba", zone = rng.Next(3) == 0 ? "slap" : "open", vel = 0.8f }, t));
            for (float t = 5.2f; t < seconds; t += 4.1f)
                for (int k = 0; k < 4; k++) take.Add(At(new TakeEvent { type = "marimba", midi = 72 + k * 2, vel = 0.75f, mode = "mallet", kind = "medium" }, t + 0.12f * k));
            take.Sort((a, b) => a.t.CompareTo(b.t));
            return take;
        }

        void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = fov;
        }

        void Shot(string name)
        {
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(_dir, name + ".png");
            File.WriteAllBytes(path, _read.EncodeToPNG());
            Debug.Log($"[PaintShow] shot {name}: {path}");
        }

        IEnumerator Draws(System.Action<long> result)
        {
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { Step(); _cam.Render(); yield return null; }
            result(draws.Valid ? draws.LastValue : -1);
            draws.Dispose();
        }

        /// <summary>A world point of a quad of this brush cell nearest a direction from the head (for the close-ups).</summary>
        bool CellToward(int cell, Vector3 dir, out Vector3 at, out Vector3 inward, bool room = true)
        {
            at = inward = default;
            WallPaint.Layer l = room ? Paint.RoomLayer : Paint.MrLayer;
            Vector3 head = App.Head.position;
            float best = -2f;
            for (int s = 0; s < WallPaint.MaxQuads; s++)
            {
                if (l.Gen[s] == 255 || (cell >= 0 && l.Cell[s] != cell)) continue;
                Vector3 p = room ? Room.Root.TransformPoint(l.Centre[s]) : l.Centre[s];
                float d = Vector3.Dot((p - head).normalized, dir);
                if (d > best) { best = d; at = p; }
            }
            if (best < -1.5f) return false;
            inward = head - at; inward.y = 0f;
            inward.Normalize();
            return true;
        }

        void CloseUp(string name, int cell, Vector3 dir, float dist = 0.8f, float fov = 45f)
        {
            if (CellToward(cell, dir, out Vector3 at, out Vector3 inward)) { Aim(at + inward * dist + Vector3.up * 0.05f, at, fov); Shot(name); }
            else Debug.Log($"[PaintShow] no {name} to show");
        }

        IEnumerator SaveTake(List<TakeEvent> events, string name, List<TakeRecord> into)
        {
            var lib = App.Get<ITakeLibrary>();
            var task = lib.Put(new TakeRecord { name = name, seconds = events[events.Count - 1].t + 0.4f, events = events });
            float t0 = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < 10f) { Step(); yield return null; }
            into.Add(task.Result);
        }

        IEnumerator Settle(System.Func<bool> cond, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) { Step(); yield return null; }
        }

        void Flick(Vector3 dir)
        {
            Transform head = App.Head;
            Vector3 f = head.forward; f.y = 0f; f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 wrist = head.position + f * 0.22f + r * 0.18f - Vector3.up * 0.28f;
            dir.Normalize();
            for (int i = 0; i < 12; i++) { _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, dir, Vector3.down)); Step(); }
            for (int i = 0; i < 40; i++)
            {
                float t = i * Dt, s = t < 0.12f ? 2.8f * (t / 0.12f) : t < 0.2f ? 2.8f * (1f - (t - 0.12f) / 0.08f) : 0f;
                wrist += dir * (s * Dt);
                _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, dir, Vector3.down));
                Step();
            }
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, new Vector3(0.4f, 0.6f, -0.3f), Vector3.forward, Vector3.left));
        }

        [UnityTest]
        [Timeout(1200000)]
        public IEnumerator EachInstrumentPaintsItsOwnWayAndTheSongIsFramed()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            PlayingTake.Clear();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_song_stars,tip_window_show,tip_ghost_hands,tip_paint_walls,tip_paint_throw,tip_paint_gallery,tip_paint_keep,tip_room_snow");
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            while ((!layout.FirstArrangeDone || Room.TexturesPending > 0 || !Room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Paint.FinishAtlas();
            _dir = ShotDirs.FromRoot("paintshow") ?? ShotDirs.Default("paintshow");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("PaintShowShotCamera").AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.aspect = (float)W / H;
            // the atlas itself, to look at
            Color32[] apx = BrushAtlas.Pixels;
            var view = new Texture2D(PaintArt.Size, PaintArt.Size, TextureFormat.RGB24, false, false);
            var vpx = new Color32[apx.Length];
            for (int i = 0; i < apx.Length; i++)
            {
                float a = apx[i].a / 255f, d = apx[i].r / 255f;
                vpx[i] = Color.Lerp(new Color(0.96f, 0.93f, 0.86f), new Color(0.55f, 0.18f, 0.32f) * (1.15f - 0.55f * d), a);
            }
            view.SetPixels32(vpx);
            view.Apply();
            File.WriteAllBytes(Path.Combine(_dir, "brush_atlas.png"), view.EncodeToPNG());
            Object.Destroy(view);
            var recs = new List<TakeRecord>();
            yield return SaveTake(Showcase(40f), "Take 1 · 0:40", recs);
            yield return SaveTake(Showcase(14f, 7), "Take 2 · 0:14", recs);
            yield return SaveTake(Showcase(18f, 11), "Take 3 · 0:18", recs);
            StepSeconds(0.5f);

            Vector3 seat = App.Head.position;
            Vector3 fwd = App.Head.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            IRecorder rec = App.Get<IRecorder>();

            // ---- the showcase plays back (from Recordings)
            Assert.IsTrue(rec.PlayEvents(recs[0].events, recs[0].seconds));
            PlayingTake.Note(recs[0].id, recs[0].events);
            StepSeconds(9f);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.35f, 80f); Shot("a_seat_ahead_9s");
            CloseUp("b_close_vine", PaintArt.Ribbon, fwd, 1.1f, 50f);
            CloseUp("b_close_flowers", PaintArt.Blossom, fwd, 0.7f, 45f);
            StepSeconds(0.5f);
            CloseUp("b_close_leaves_falling", PaintArt.Leaf, fwd, 0.8f, 40f);
            // a throw toward the wall at the left of the view
            Paint.HandBusyOverride = _ => false;
            Flick(fwd + Vector3.up * 0.2f - right * 0.55f);
            StepSeconds(0.6f);
            Debug.Log($"[PaintShow] throws {Paint.Throws}, splats {Paint.Splats} at {Paint.LastSplat}");
            if (Paint.Splats > 0)
            {
                Vector3 p = Paint.LastSplat, inward = seat - p; inward.y = 0f; inward.Normalize();
                Aim(p + inward * 0.9f + Vector3.up * 0.1f, p, 45f); Shot("c_close_thrown_petals");
            }
            StepSeconds(17f);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.35f, 80f); Shot("d_seat_ahead_27s");
            Aim(seat, seat - right * 3f + Vector3.up * 0.35f, 85f); Shot("d_seat_left");
            Aim(seat, seat + right * 3f + Vector3.up * 0.35f, 85f); Shot("d_seat_right");
            Aim(seat, seat - fwd * 3f + Vector3.up * 0.35f, 85f); Shot("d_seat_behind");
            CloseUp("e_close_flock", PaintArt.Bird, -right, 1.2f, 50f);
            CloseUp("e_close_cloud", PaintArt.Cloud, right, 1.3f, 50f);
            CloseUp("e_close_tree", PaintArt.Trunk, fwd, 1.3f, 50f);
            CloseUp("e_close_butterfly", PaintArt.Butterfly, -right, 0.6f, 40f);
            CloseUp("e_close_mushrooms", PaintArt.Cap, right, 0.6f, 40f);
            CloseUp("e_close_grass", PaintArt.Grass, fwd, 0.8f, 45f);
            CloseUp("e_close_flowers_side", PaintArt.Blossom, right, 0.8f, 45f);
            long paintDraws = -1;
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.35f, 80f);
            yield return Draws(v => paintDraws = v);
            StepSeconds(recs[0].seconds - 26.6f);
            Bounds box = Room.RoomBox;
            Vector3 corner = new Vector3(box.center.x - fwd.x * box.extents.x * 0.9f + right.x * box.extents.x * 0.85f, 2.5f, box.center.z - fwd.z * box.extents.z * 0.9f + right.z * box.extents.z * 0.85f);
            Aim(corner, box.center + Vector3.down * 0.4f, 90f); Shot("f_overview_end");
            Debug.Log($"[PaintShow] painted {Paint.Painted}, missed {Paint.Missed}, quads {Paint.Splashes}; seat draws while painting {paintDraws}");

            // ---- the end: the swirl into the frame
            yield return Settle(() => !Paint.Gallery.Resolving, 10f);
            StepSeconds(WallPaint.HoldSeconds - 0.3f + 0.1f);
            Assert.IsTrue(Paint.Gallery.Count > 0 || Paint.Gallery.Ready, "the showcase is framed");
            PaintGallery.Slot s0 = Paint.Gallery.RoomSlots[Paint.Gallery.Entries[0].Slot];
            Vector3 frame = Room.Root.TransformPoint(s0.Centre), out0 = Room.Root.TransformDirection(s0.Normal);
            Vector3 swirlEye = frame + out0 * 2.4f + Vector3.up * 0.15f;
            for (int k = 0; k < 6; k++)
            {
                Debug.Log($"[PaintShow] swirl {k}: gather {Paint.Gather:F2}, walls fade {Paint.Fade:F2}");
                Aim(seat, Vector3.Lerp(frame, seat + fwd * 2f, 0.25f), 85f); Shot($"g_swirl_{k}_seat");
                Aim(swirlEye, frame + out0 * 0.3f, 60f); Shot($"g_swirl_{k}_near");
                StepSeconds(0.45f);
            }
            yield return Settle(() => Paint.Gallery.Entries[0].Uploaded, 20f);
            StepSeconds(WallPaint.FadeSeconds + 0.5f);
            Aim(seat, frame, 70f); Shot("h_seat_toward_frame");
            Aim(frame + out0 * 1.3f + Vector3.up * 0.05f, frame + Vector3.down * 0.12f, 45f); Shot("h_frame_close");
            Aim(frame + out0 * 0.55f + Vector3.down * 0.3f, frame + Vector3.down * 0.37f, 35f); Shot("h_plaque_close");

            // ---- two more songs: a small gallery
            for (int k = 1; k < 3; k++)
            {
                Assert.IsTrue(rec.PlayEvents(recs[k].events, recs[k].seconds));
                PlayingTake.Note(recs[k].id, recs[k].events);
                StepSeconds(recs[k].seconds + 0.3f);
                yield return Settle(() => !Paint.Gallery.Resolving, 10f);
                StepSeconds(WallPaint.HoldSeconds + WallPaint.GatherSeconds + 0.3f);
                yield return Settle(() => Paint.Gallery.Entries[Paint.Gallery.Count - 1].Uploaded, 20f);
                StepSeconds(WallPaint.FadeSeconds + 0.5f);
            }
            Debug.Log($"[PaintShow] the gallery: {Paint.Gallery.Count} paintings, {Paint.Gallery.RoomSlots.Count} places");
            // each song's picture itself (3x, nearest), to judge the landscapes
            for (int k = 0; k < Paint.Gallery.Count; k++)
            {
                Color32[] pic = Paint.Gallery.Entries[k].Pixels;
                if (pic == null) continue;
                var tex = new Texture2D(GalleryPicture.Width, GalleryPicture.Height, TextureFormat.RGB24, false, false);
                tex.SetPixels32(pic);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(_dir, $"gallery_picture_{k}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.35f, 90f); Shot("i_gallery_seat_ahead");
            Aim(seat, seat - right * 3f + Vector3.up * 0.35f, 90f); Shot("i_gallery_seat_left");
            Aim(seat, seat + right * 3f + Vector3.up * 0.35f, 90f); Shot("i_gallery_seat_right");
            Aim(corner, box.center + Vector3.down * 0.2f, 95f); Shot("i_gallery_overview");
            long galleryDraws = -1;
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.35f, 90f);
            yield return Draws(v => galleryDraws = v);
            Debug.Log($"[PaintShow] seat draws with the gallery at rest {galleryDraws}");

            // ---- mixed reality: plain beige walls for the real ones, a door on the wall ahead
            Room.SetVisible(false);
            _cam.backgroundColor = new Color(0.42f, 0.4f, 0.37f, 1f);
            var walls = new ShotWalls(seat, fwd);
            Paint.RealWallsOverride = walls;
            var wallMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wallMat.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.66f));
            var doorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            doorMat.SetColor("_BaseColor", new Color(0.45f, 0.33f, 0.24f));
            _made.Add(wallMat); _made.Add(doorMat);
            for (int i = 0; i < walls.WallCount; i++)
            {
                RealWall w = walls.Wall(i);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(q.GetComponent<Collider>());
                q.transform.SetPositionAndRotation(w.Centre - w.Inward * 0.01f, Quaternion.LookRotation(-w.Inward, Vector3.up));
                q.transform.localScale = new Vector3(w.Size.x, w.Size.y, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                _made.Add(q);
            }
            RealWallBlock door = walls.Block(0);
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(dq.GetComponent<Collider>());
            dq.transform.SetPositionAndRotation(door.Centre, door.Rotation);
            dq.transform.localScale = new Vector3(door.Half.x * 2f, door.Half.y * 2f, 1f);
            dq.GetComponent<MeshRenderer>().sharedMaterial = doorMat;
            _made.Add(dq);
            Assert.IsTrue(rec.PlayEvents(recs[1].events, recs[1].seconds));
            PlayingTake.Note(recs[1].id, recs[1].events);
            StepSeconds(recs[1].seconds - 0.5f);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 85f); Shot("j_mr_paint_ahead");
            Aim(seat, seat - right * 3f + Vector3.up * 0.3f, 85f); Shot("j_mr_paint_left");
            StepSeconds(0.8f);
            yield return Settle(() => !Paint.Gallery.Resolving, 10f);
            StepSeconds(WallPaint.HoldSeconds + WallPaint.GatherSeconds + 0.3f);
            yield return Settle(() => Paint.Gallery.Count > 0 && Paint.Gallery.Entries[Paint.Gallery.Count - 1].Uploaded, 20f);
            StepSeconds(WallPaint.FadeSeconds + 0.5f);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 95f); Shot("k_mr_gallery_ahead");
            Aim(seat - fwd * 0.6f + Vector3.up * 0.6f, seat + fwd * 2f + Vector3.up * 0.2f, 100f); Shot("k_mr_gallery_wide");
            Room.SetVisible(true);
        }

        /// <summary>A 4.4 x 4 m real room round the seat, the player facing its wall ahead, a door on that wall's left.</summary>
        sealed class ShotWalls : IRealWalls
        {
            readonly Vector3 _c, _f, _r;
            public ShotWalls(Vector3 seat, Vector3 forward)
            {
                _c = new Vector3(seat.x, 0f, seat.z) + forward * 0.6f;
                _f = forward;
                _r = Vector3.Cross(Vector3.up, forward);
            }

            public int Version => 1;
            public int WallCount => 4;
            public int BlockCount => 1;
            public bool Refresh() => true;
            public bool DepthWall(Ray ray, out Vector3 point, out Vector3 inward) { point = inward = default; return false; }

            public RealWall Wall(int i)
            {
                Vector3 up = Vector3.up * 1.3f;
                switch (i)
                {
                    case 0: return new RealWall { Centre = _c + _f * 2f + up, Inward = -_f, Size = new Vector2(4.4f, 2.6f) };
                    case 1: return new RealWall { Centre = _c - _f * 2f + up, Inward = _f, Size = new Vector2(4.4f, 2.6f) };
                    case 2: return new RealWall { Centre = _c + _r * 2.2f + up, Inward = -_r, Size = new Vector2(4f, 2.6f) };
                    default: return new RealWall { Centre = _c - _r * 2.2f + up, Inward = _r, Size = new Vector2(4f, 2.6f) };
                }
            }

            public RealWallBlock Block(int i) => new RealWallBlock
            {
                Centre = _c + _f * 1.995f - _r * 1.1f + Vector3.up * 1.02f, Half = new Vector3(0.46f, 1.02f, 0.04f), Rotation = Quaternion.LookRotation(_f, Vector3.up),
            };
        }
    }
}
