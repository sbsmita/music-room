// Pictures of the notes painting the walls (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at:
//   - VR: a 45 s song of piano, drums and harp played back: from the seat ahead, left, right and behind, an overview of the
//     room, two close-ups of the splashes; the walls once the painting has faded (clean again);
//   - mixed reality: the room hidden, four plain beige quads standing in for real walls seen through passthrough (a
//     grey background), the paint as a wash on them, clear of a door;
//   - the draw cost from the seat: one draw while it shows.
// Output: $MUSICROOM_SHOTS_ROOT/wallpaint, else Builds/Shots/wallpaint.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class WallPaintShotsTests
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
        }

        /// <summary>A song of the three classic instruments: a piano melody with chords, drums on the beat, harp runs.</summary>
        static List<TakeEvent> Song(float seconds)
        {
            var take = new List<TakeEvent>();
            int[] tune = { 60, 64, 67, 72, 71, 67, 64, 62, 65, 69, 74, 72, 69, 65, 60, 55 };
            int i = 0;
            for (float t = 0.05f; t < seconds; t += 0.3f, i++)
            {
                int m = tune[i % tune.Length] + (i / 32 % 2 == 1 ? 12 : 0);
                float vel = 0.45f + 0.5f * Mathf.Abs(Mathf.Sin(i * 0.37f));
                TakeEvent on = TakeEvent.PianoOn(m, vel); on.t = t; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = t + 0.27f; take.Add(off);
                if (i % 8 == 0) { TakeEvent lo = TakeEvent.PianoOn(m - 24, 0.6f); lo.t = t; take.Add(lo); TakeEvent lf = TakeEvent.PianoOff(m - 24); lf.t = t + 1.1f; take.Add(lf); }
                if (i % 2 == 0) { TakeEvent d = TakeEvent.Drum(i % 4 == 0 ? "kick" : "snare", 0.75f, 0.2f, "head", false); d.t = t; take.Add(d); }
                if (i % 8 == 7) { TakeEvent c = TakeEvent.Drum("crash", 0.95f, 0.5f, "bow", false); c.t = t; take.Add(c); }
                if (i % 6 == 3) for (int k = 0; k < 4; k++) { TakeEvent h = TakeEvent.Harp((i + k * 3) % 22, 0.7f, 0.5f, 0.4f, "through"); h.t = t + k * 0.07f; take.Add(h); }
            }
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
            Debug.Log($"[WallPaint] shot {name}: {path}");
        }

        IEnumerator Draws(System.Action<long> result)
        {
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { Step(); _cam.Render(); yield return null; }
            result(draws.Valid ? draws.LastValue : -1);
            draws.Dispose();
        }

        /// <summary>The world point of the splash nearest a direction from the head (for the close-ups).</summary>
        bool SplashToward(Vector3 dir, out Vector3 at, out Vector3 inward)
        {
            at = inward = default;
            WallPaint.Layer l = Paint.RoomLayer;
            Transform root = Room.Root;
            Vector3 head = App.Head.position;
            float best = -2f;
            for (int s = 0; s < WallPaint.MaxQuads; s++)
            {
                if (l.Gen[s] == 255) continue;
                Vector3 p = root.TransformPoint(l.Centre[s]);
                float d = Vector3.Dot((p - head).normalized, dir);
                if (d > best) { best = d; at = p; }
            }
            if (best < 0f) return false;
            inward = head - at; inward.y = 0f;
            inward.Normalize();
            return true;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator TheNotesPaintTheWalls()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_song_stars,tip_window_show,tip_ghost_hands," + WallPaint.TipId + "," + WallPaint.KeepTipId);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            while ((!layout.FirstArrangeDone || Room.TexturesPending > 0 || !Room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Assert.That(Room.TexturesPending, Is.EqualTo(0));
            _dir = ShotDirs.FromRoot("wallpaint") ?? ShotDirs.Default("wallpaint");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("WallPaintShotCamera").AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.aspect = (float)W / H;
            StepSeconds(0.5f);

            Vector3 seat = App.Head.position;
            Vector3 fwd = App.Head.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 80f); Shot("before_seat_ahead");
            long restDraws = -1;
            yield return Draws(v => restDraws = v);

            // ---- a song plays back: the walls fill with colour
            IRecorder rec = App.Get<IRecorder>();
            List<TakeEvent> song = Song(45f);
            Assert.IsTrue(rec.PlayEvents(song, 45.5));
            StepSeconds(12f);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 80f); Shot("paint_12s_seat_ahead");
            StepSeconds(33.2f);
            Debug.Log($"[WallPaint] painted {Paint.Painted}, missed {Paint.Missed}, on the walls {Paint.Splashes}");
            Assert.That(Paint.Splashes, Is.GreaterThan(100));
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 80f); Shot("paint_seat_ahead");
            long paintDraws = -1;
            yield return Draws(v => paintDraws = v);
            Debug.Log($"[WallPaint] seat draws ahead: before {restDraws}, while it shows {paintDraws}");
            Aim(seat, seat - right * 3f + Vector3.up * 0.3f, 85f); Shot("paint_seat_left");
            Aim(seat, seat + right * 3f + Vector3.up * 0.3f, 85f); Shot("paint_seat_right");
            Aim(seat, seat - fwd * 3f + Vector3.up * 0.3f, 85f); Shot("paint_seat_behind");
            Bounds box = Room.RoomBox;
            Vector3 corner = new Vector3(box.center.x - fwd.x * box.extents.x * 0.9f + right.x * box.extents.x * 0.85f, 2.5f, box.center.z - fwd.z * box.extents.z * 0.9f + right.z * box.extents.z * 0.85f);
            Aim(corner, box.center + Vector3.down * 0.4f, 90f); Shot("paint_overview");
            if (SplashToward(fwd, out Vector3 at, out Vector3 inward)) { Aim(at + inward * 0.9f, at, 45f); Shot("paint_close_ahead"); }
            if (SplashToward(-right, out at, out inward)) { Aim(at + inward * 0.6f + Vector3.up * 0.1f, at, 40f); Shot("paint_close_left"); }

            // ---- the end: it holds, fades, the walls are clean
            StepSeconds(WallPaint.HoldSeconds + WallPaint.FadeSeconds * 0.5f);
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 80f); Shot("fading_seat_ahead");
            StepSeconds(WallPaint.FadeSeconds * 0.5f + 0.5f);
            Assert.That(Paint.Splashes, Is.EqualTo(0));
            Shot("after_seat_ahead");

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
            Assert.IsTrue(rec.PlayEvents(Song(30f), 30.5));
            StepSeconds(30f);
            Assert.That(Paint.MrLayer.Count, Is.GreaterThan(50));
            Aim(seat, seat + fwd * 3f + Vector3.up * 0.3f, 80f); Shot("mr_paint_ahead");
            Aim(seat, seat - right * 3f + Vector3.up * 0.3f, 85f); Shot("mr_paint_left");
            Aim(seat - fwd * 0.6f + Vector3.up * 0.6f, seat + fwd * 2f + Vector3.up * 0.2f, 95f); Shot("mr_paint_wide");
            rec.Stop();
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
