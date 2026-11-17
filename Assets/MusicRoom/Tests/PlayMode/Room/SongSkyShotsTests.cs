// Pictures of the song's night sky (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at:
//   - VR: a piano and drums take playing, from the seat looking ahead and up at the sky (stars rising, settled ones, the
//     lines), wider, and once the take is over (the whole constellation); a side view from a corner of the room;
//   - mixed reality (the room hidden, a grey stand-in for passthrough): the stars over the instruments in open air;
//   - after the fade: the room as it was;
//   - the draws from the seat (the sky is one).
// The camera is aimed from the seat at the stars themselves (nothing assumes where the room's walls are).
// Output: $MUSICROOM_SHOTS_ROOT/sky, else Builds/Shots/sky.
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
    public class SongSkyShotsTests
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
        SongSky Sky => Room.SongSky;
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

        /// <summary>A little tune on the piano with the kit under it (about 12 s).</summary>
        static List<TakeEvent> Song()
        {
            var take = new List<TakeEvent>();
            int[] tune = { 60, 64, 67, 72, 71, 67, 64, 65, 69, 72, 76, 74, 72, 67, 64, 62, 60, 64, 67, 72, 79, 76, 72, 67 };
            for (int i = 0; i < tune.Length; i++)
            {
                float t = 0.1f + 0.5f * i;
                TakeEvent on = TakeEvent.PianoOn(tune[i], i % 4 == 0 ? 0.95f : 0.6f + 0.05f * (i % 3)); on.t = t; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(tune[i]); off.t = t + 0.45f; take.Add(off);
                if (i % 2 == 0) { TakeEvent d = TakeEvent.Drum(i % 4 == 0 ? "kick" : "snare", 0.85f, 0.3f, "head", false); d.t = t; take.Add(d); }
                if (i % 8 == 7) { TakeEvent c = TakeEvent.Drum("crash", 0.9f, 0.3f, "edge", false); c.t = t; take.Add(c); }
            }
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
            Debug.Log($"[Sky] shot {name}: {path}");
        }

        IEnumerator Draws(System.Action<long> result)
        {
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { Step(); _cam.Render(); yield return null; }
            result(draws.Valid ? draws.LastValue : -1);
            draws.Dispose();
        }

        /// <summary>The middle of the settled stars (or the frame's head + forward when there are none).</summary>
        Vector3 SkyCentre()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < SongSkyModel.Max; i++)
                if (Sky.StarAt(i, out Vector3 p)) { sum += p; n++; }
            return n > 0 ? sum / n : App.Head.position + Vector3.up + Sky.Model.Current.Forward;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheSongDrawsANightSky()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "all");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, SongSky.TipId + "," + ListenBackPulse.TipId);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            while ((!layout.FirstArrangeDone || Room.TexturesPending > 0 || !Room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Assert.That(Room.TexturesPending, Is.EqualTo(0));
            _dir = ShotDirs.FromRoot("sky") ?? ShotDirs.Default("sky");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("SkyShotCamera").AddComponent<Camera>();
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
            Aim(seat, seat + fwd * 2f + Vector3.up * 0.9f, 80f);
            Shot("vr_seat_up_before");
            long restDraws = -1;
            yield return Draws(v => restDraws = v);

            // ---- VR: a take plays
            IRecorder rec = App.Get<IRecorder>();
            Assert.IsTrue(rec.PlayEvents(Song(), 12.4));
            StepSeconds(5.3f);
            Assert.That(Sky.Model.Live, Is.GreaterThan(8));
            Vector3 c = SkyCentre();
            Aim(seat, Vector3.Lerp(seat + fwd * 1.5f + Vector3.up * 0.3f, c, 0.6f), 90f); Shot("vr_seat_rising");
            long playDraws = -1;
            yield return Draws(v => playDraws = v);
            Debug.Log($"[Sky] seat draws: rest {restDraws}, while the sky shows {playDraws}");
            StepSeconds(7.6f);   // the take is over: the whole constellation, lingering
            c = SkyCentre();
            Aim(seat, c, 80f); Shot("vr_seat_constellation");
            Aim(seat, c, 105f); Shot("vr_seat_constellation_wide");
            Aim(seat, seat + fwd * 3f, 90f); Shot("vr_seat_ahead");
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Aim(seat - fwd * 0.8f + side * 1.2f + Vector3.up * 0.3f, c, 75f); Shot("vr_behind_side");
            Assert.That(Sky.Model.State, Is.Not.EqualTo(SongSkyModel.Phase.Resting));

            // ---- after the fade: the room as it was
            StepSeconds(SongSkyModel.Linger + SongSkyModel.FadeOut + 1f);
            Assert.IsTrue(Sky.Model.Resting);
            Aim(seat, seat + fwd * 2f + Vector3.up * 0.9f, 80f); Shot("vr_seat_up_after");

            // ---- mixed reality: the room hidden, the stars in open air over the instruments
            Room.SetVisible(false);
            _cam.backgroundColor = new Color(0.42f, 0.4f, 0.37f, 1f);
            StepSeconds(0.2f);
            Assert.IsTrue(rec.PlayEvents(Song(), 12.4));
            StepSeconds(6f);
            c = SkyCentre();
            Aim(seat, Vector3.Lerp(seat + fwd * 1.5f, c, 0.7f), 90f); Shot("mr_seat_rising");
            StepSeconds(7f);
            c = SkyCentre();
            Aim(seat, c, 85f); Shot("mr_seat_constellation");
            Aim(seat - fwd * 0.9f + side * 1.3f + Vector3.up * 0.2f, c, 75f); Shot("mr_side");
            Room.SetVisible(true);
            rec.Stop();
        }
    }
}
