// Pictures of the snow in the whole room (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at:
//   - VR, from the seat: facing the fire, turned around (the wall behind), looking up, down at the piano's keys; while a
//     take plays (a gust: the flakes swirl, the near ones golden from the piano); and with SNOW off (no flake anywhere);
//   - mixed reality (the room hidden, grey for passthrough): ahead and turned around.
// Each picture's difference from the same view with SNOW off is measured: the flakes are there, all around, and gone
// again when the switch is off.
// Output: $MUSICROOM_SHOTS_ROOT/room-snow, else Builds/Shots/room-snow.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class RoomSnowShotsTests
    {
        const int W = 1280, H = 1024;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 20.0;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _read;
        string _dir;

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }
        void StepSeconds(float s) => Step(Mathf.RoundToInt(s / Dt));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        /// <summary>The head and the camera at a pose (the snow keeps clear of the head the app knows).</summary>
        void Look(Vector3 from, Vector3 dir, float fov)
        {
            Quaternion q = Quaternion.LookRotation(dir, Vector3.up);
            App.Head.SetPositionAndRotation(from, q);
            Step(2);
            App.Head.SetPositionAndRotation(from, q);
            _cam.transform.SetPositionAndRotation(from, q);
            _cam.fieldOfView = fov;
        }

        Color32[] Shot(string name)
        {
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(_dir, name + ".png");
            File.WriteAllBytes(path, _read.EncodeToPNG());
            Debug.Log($"[RoomSnow] shot {name}: {path}");
            return _read.GetPixels32();
        }

        /// <summary>The share of pixels brighter than without snow (by more than 6/255), and in which thirds of the picture.</summary>
        static float Flakes(Color32[] on, Color32[] off, out float left, out float mid, out float right)
        {
            int n = 0, l = 0, m = 0, r = 0;
            for (int i = 0; i < on.Length; i++)
            {
                int d = on[i].r + on[i].g + on[i].b - off[i].r - off[i].g - off[i].b;
                if (d <= 18) continue;
                n++;
                int x = i % W;
                if (x < W / 3) l++; else if (x < 2 * W / 3) m++; else r++;
            }
            float k = 1f / on.Length;
            left = l * k * 3f; mid = m * k * 3f; right = r * k * 3f;
            return n * k;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ItSnowsAllAroundInTheRoomAndInMixedReality()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_song_stars,tip_paint_walls,tip_paint_keep,tip_paint_throw,tip_paint_gallery,tip_ghost_hands,tip_window_show," + RoomSnow.TipId);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            while ((!layout.FirstArrangeDone || Room.TexturesPending > 0 || !Room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Assert.That(Room.TexturesPending, Is.EqualTo(0));
            _dir = ShotDirs.FromRoot("room-snow") ?? ShotDirs.Default("room-snow");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("RoomSnowShotCamera").AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 60f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.aspect = (float)W / H;

            Vector3 seat = App.Head.position;
            Vector3 toFire = Room.Scene.Fireplace.position - seat; toFire.y = 0f; toFire.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toFire);
            Vector3 down = (toFire * 0.55f + Vector3.down).normalized;
            Vector3 upDir = (toFire * 0.6f + Vector3.up).normalized;
            var views = new (string name, Vector3 dir)[] { ("fire", toFire), ("behind", -toFire), ("left", -side), ("right", side), ("up", upDir), ("keys", down) };

            // SNOW off: the room as it is
            var off = new Dictionary<string, Color32[]>();
            foreach (var v in views) { Look(seat, v.dir, 90f); off[v.name] = Shot("vr_off_" + v.name); }

            Room.Set(AmbienceKind.Snow, true);
            Room.Weather.Settle(1f, 0.6f, Room.Weather.TimeD);
            StepSeconds(12f);   // a full fall: some flakes already lie on the floor
            Assert.IsTrue(Room.RoomSnow.Falling);
            Assert.IsTrue(Room.RoomSnow.Renderer.enabled);
            foreach (var v in views)
            {
                Look(seat, v.dir, 90f);
                float f = Flakes(Shot("vr_on_" + v.name), off[v.name], out float l, out float m, out float r);
                Debug.Log($"[RoomSnow] VR {v.name}: flake pixels {f:P2} (left {l:P2}, middle {m:P2}, right {r:P2})");
                Assert.That(f, Is.GreaterThan(0.002f), v.name + ": it snows this way too");
                if (v.name != "keys") Assert.That(Mathf.Min(l, Mathf.Min(m, r)), Is.GreaterThan(0.0005f), v.name + ": across the whole view");
                Assert.That(f, Is.LessThan(0.2f), v.name + ": calm, not a white-out");
            }
            Look(seat + toFire * 1.6f + Vector3.down * 0.2f, -toFire + Vector3.down * 0.15f, 75f); Shot("vr_on_across_room");
            Look(seat, toFire, 60f); Shot("vr_on_fire_narrow");

            // a playback: the gust swirls the snow, the near flakes catch the piano's gold
            EventBus bus = App.Events;
            bus.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 1u << (int)InstrumentKind.Piano });
            for (int i = 0; i < 24; i++)
            {
                bus.Publish(new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.95f, Lane = 1, Event = TakeEvent.PianoOn(60 + (i * 5) % 24, 0.95f) });
                StepSeconds(0.25f);
            }
            Assert.That(Room.RoomSnow.Gust, Is.GreaterThan(0.2f));
            Assert.That(Room.RoomSnow.Model.Tint.w, Is.GreaterThan(0.1f));
            Look(seat, toFire, 90f); Shot("vr_playback_fire");
            Look(seat, -toFire, 90f); Shot("vr_playback_behind");
            bus.Publish(new RecordingPlaybackEvent { Started = false });
            StepSeconds(12f);
            Assert.That(Room.RoomSnow.Gust, Is.Zero);
            Assert.That(Room.RoomSnow.Model.Tint, Is.EqualTo(Vector4.zero));

            // mixed reality: the room hidden, grey for passthrough
            Room.SetVisible(false);
            _cam.backgroundColor = new Color(0.42f, 0.4f, 0.37f, 1f);
            Look(seat, toFire, 90f);
            var mrOff = new Dictionary<string, Color32[]>();
            Room.Set(AmbienceKind.Snow, false);
            Room.Weather.Settle(0f, 0f, Room.Weather.TimeD);
            Step(3);
            Assert.IsFalse(Room.RoomSnow.Renderer.enabled, "off: not drawn");
            foreach (var v in views) { Look(seat, v.dir, 90f); mrOff[v.name] = Shot("mr_off_" + v.name); }
            Room.Set(AmbienceKind.Snow, true);
            Room.Weather.Settle(1f, 0.6f, Room.Weather.TimeD);
            StepSeconds(12f);
            Assert.IsTrue(Room.RoomSnow.InMixedReality);
            foreach (var v in views)
            {
                Look(seat, v.dir, 90f);
                float f = Flakes(Shot("mr_on_" + v.name), mrOff[v.name], out float l, out float m, out float r);
                Debug.Log($"[RoomSnow] MR {v.name}: flake pixels {f:P2} (left {l:P2}, middle {m:P2}, right {r:P2})");
                Assert.That(f, Is.GreaterThan(0.002f), "MR " + v.name);
            }
            Room.SetVisible(true);
            _cam.backgroundColor = XrMode.VrBackground;

            // SNOW off again: gone
            Room.Set(AmbienceKind.Snow, false);
            Room.Weather.Settle(0f, 0f, Room.Weather.TimeD);
            Step(3);
            Assert.IsFalse(Room.RoomSnow.Falling);
            Assert.IsFalse(Room.RoomSnow.Renderer.enabled, "no flake left: not drawn at all");
            Look(seat, toFire, 90f);
            Shot("vr_off_again_fire");
        }
    }
}
