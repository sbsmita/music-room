// Pictures of the snowy window joining in (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at:
//   - the VR room's window from the seat and close up: at rest (snowing), while a take plays (gusting snow, northern lights
//     in the notes' colours: a low piano line, then drums, then high harp strings), and after the end (back to its own);
//   (the mixed reality snowy window was removed 2026-11-13: in mixed reality the snow falls in the whole room.)
// Each moment's window area is measured too: the lights brighten it and turn it toward green / teal / violet, and after
// the end it is back to the rest picture's colour (within the snowfall's own flicker).
// Output: $MUSICROOM_SHOTS_ROOT/window-show, else Builds/Shots/window-show.
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
    public class WindowShowShotsTests
    {
        const int W = 1280, H = 720;
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

        /// <summary>A take in three parts: a low piano line, drums (kick, snare, crash), high harp strings.</summary>
        static List<TakeEvent> Song()
        {
            var take = new List<TakeEvent>();
            int[] low = { 36, 43, 48, 43, 40, 47, 52, 47 };
            for (int i = 0; i < 24; i++)
            {
                int m = low[i % low.Length];
                TakeEvent on = TakeEvent.PianoOn(m, i % 4 == 0 ? 1f : 0.75f); on.t = 0.05f + 0.25f * i; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = on.t + 0.22f; take.Add(off);
            }
            string[] kit = { "kick", "hat", "snare", "hat", "kick", "kick", "snare", "crash" };
            for (int i = 0; i < 32; i++)
            {
                TakeEvent d = TakeEvent.Drum(kit[i % kit.Length], 0.9f, 0.3f, "head", false); d.t = 6.2f + 0.2f * i; take.Add(d);
            }
            for (int i = 0; i < 30; i++)
            {
                TakeEvent h = TakeEvent.Harp(12 + (i * 3) % 10, 0.85f, 0.5f, 0.3f, "through"); h.t = 12.8f + 0.18f * i; take.Add(h);
            }
            return take;
        }

        void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = fov;
        }

        /// <summary>Renders, saves, and returns the mean colour of the picture's middle (x .3-.7, y .3-.75).</summary>
        Color Shot(string name)
        {
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(_dir, name + ".png");
            File.WriteAllBytes(path, _read.EncodeToPNG());
            double r = 0, g = 0, b = 0;
            int n = 0;
            for (int y = (int)(H * 0.3f); y < (int)(H * 0.75f); y += 2)
                for (int x = (int)(W * 0.3f); x < (int)(W * 0.7f); x += 2)
                {
                    Color c = _read.GetPixel(x, y);
                    r += c.r; g += c.g; b += c.b; n++;
                }
            var mean = new Color((float)(r / n), (float)(g / n), (float)(b / n));
            Debug.Log($"[WindowShow] shot {name}: {path} middle mean {mean}");
            return mean;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheWindowJoinsInVrAndMixedReality()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_song_stars,tip_paint_walls,tip_paint_keep,tip_ghost_hands," + WindowShow.TipId);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            while ((!layout.FirstArrangeDone || Room.TexturesPending > 0 || !Room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Assert.That(Room.TexturesPending, Is.EqualTo(0));
            _dir = ShotDirs.FromRoot("window-show") ?? ShotDirs.Default("window-show");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("WindowShowShotCamera").AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 60f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.aspect = (float)W / H;
            Room.Set(AmbienceKind.Snow, true);
            Room.Weather.Settle(1f, 1f, Room.Weather.TimeD);   // snowing hard, fully settled: the weather holds still from here
            StepSeconds(2f);

            Transform win = Room.Scene.Window;
            Vector3 seat = App.Head.position;
            // inside the room, 1.4 m from the glass, the eye at the window's middle: the view through it
            Vector3 near = win.position - win.forward * 1.4f + Vector3.down * 0.1f;
            void Vr(string tag, out Color seatMean, out Color nearMean)
            {
                Aim(seat, win.position + Vector3.up * 0.2f, 60f); seatMean = Shot("vr_seat_" + tag);
                Aim(near, win.position + win.forward * 3f + Vector3.up * 0.6f, 70f); nearMean = Shot("vr_near_" + tag);
            }

            Vr("rest", out Color restSeat, out Color restNear);

            IRecorder rec = App.Get<IRecorder>();
            Assert.IsTrue(rec.PlayEvents(Song(), 19f));
            StepSeconds(3f);
            Vr("piano_low", out _, out Color pianoNear);
            StepSeconds(0.1f);
            Aim(near, win.position + win.forward * 3f + Vector3.up * 0.6f, 70f); Shot("vr_near_piano_low_b");
            StepSeconds(5.5f);
            Vr("drums", out _, out Color drumsNear);
            StepSeconds(6.3f);
            Vr("harp_high", out Color harpSeat, out Color harpNear);
            Debug.Log($"[WindowShow] VR window middle: rest {restNear}, piano {pianoNear}, drums {drumsNear}, harp {harpNear}; seat rest {restSeat} harp {harpSeat}");
            Assert.That(pianoNear.g + pianoNear.b, Is.GreaterThan(restNear.g + restNear.b + 0.02f), "the lights brighten the window (piano)");
            Assert.That(harpNear.g + harpNear.b, Is.GreaterThan(restNear.g + restNear.b + 0.02f), "and with the harp");
            rec.Stop();
            StepSeconds(10f);
            Assert.IsTrue(Room.WindowShow.Model.Resting);
            Vr("after", out _, out Color afterNear);
            Assert.That(Mathf.Abs(afterNear.g - restNear.g) + Mathf.Abs(afterNear.b - restNear.b), Is.LessThan(0.02f), "back to the window's own look");
        }
    }
}
