// Pictures of the flowers and the butterflies (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at:
//   - the four arrangements at rest (mantel tulips and daisies, sill geraniums, bookshelf cosmos) and from the seat;
//   - a piano take playing: the butterflies around the piano from the seat and close up, the room's light warm;
//   - the take's pause: butterflies landed on the mantel's and the sill's flowers;
//   - mixed reality (the room hidden, a grey stand-in for passthrough): butterflies around the piano, no flowers;
//   - the draw cost from the seat: the petals one draw, the butterflies one (only while they are out).
// Output: $MUSICROOM_SHOTS_ROOT/butterflies, else Builds/Shots/butterflies.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Render;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class ButterflyShotsTests
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
        Butterflies Flies => Room.Butterflies;
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

        static List<TakeEvent> Song(float from, float to, float gap, float vel)
        {
            var take = new List<TakeEvent>();
            int[] tune = { 60, 64, 67, 72, 67, 64, 62, 65, 69, 74, 69, 65 };
            int i = 0;
            for (float t = from; t < to; t += gap, i++)
            {
                int m = tune[i % tune.Length];
                TakeEvent on = TakeEvent.PianoOn(m, vel * (i % 4 == 0 ? 1f : 0.8f)); on.t = t; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = t + gap * 0.9f; take.Add(off);
            }
            return take;
        }

        // The room's views are in room-frame coordinates, turned into the world (the room faces the fire: RoomFrame).
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
            Debug.Log($"[Butterflies] shot {name}: {path}");
        }

        /// <summary>The draws of the seat's view toward the piano (profiler, a few frames).</summary>
        IEnumerator Draws(System.Action<long> result)
        {
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { Step(); _cam.Render(); yield return null; }
            result(draws.Valid ? draws.LastValue : -1);
            draws.Dispose();
        }

        int Nearest(Vector3 to)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                ref readonly Butterfly b = ref Flies.Flock.Get(i);
                if (b.Mode == ButterflyMode.Off || b.Alpha < 0.9f) continue;
                float d = (b.Pos - to).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator FlowersAndButterfliesInTheRoom()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, ListenBackPulse.TipId);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            while ((!layout.FirstArrangeDone || Room.TexturesPending > 0 || !Room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            Assert.That(Room.TexturesPending, Is.EqualTo(0));
            _dir = ShotDirs.FromRoot("butterflies") ?? ShotDirs.Default("butterflies");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("ButterflyShotCamera").AddComponent<Camera>();
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

            // ---- the flowers at rest
            Vector3 seat = App.Head.position;
            Aim(RoomFrame.ToWorld(new Vector3(0.55f, 1.78f, -2.75f)), RoomFrame.ToWorld(new Vector3(0.95f, 1.55f, -3.66f)), 42f); Shot("flowers_mantel_tulips");
            Aim(RoomFrame.ToWorld(new Vector3(-0.2f, 1.7f, -2.95f)), RoomFrame.ToWorld(new Vector3(-0.5f, 1.4f, -3.66f)), 40f); Shot("flowers_mantel_daisies");
            Aim(RoomFrame.ToWorld(new Vector3(2.3f, 1.12f, -1.0f)), RoomFrame.ToWorld(new Vector3(3.06f, 0.8f, -1.2f)), 40f); Shot("flowers_sill");
            Aim(RoomFrame.ToWorld(new Vector3(-2.25f, 2.2f, -2.45f)), RoomFrame.ToWorld(new Vector3(-3.0f, 2.05f, -2.98f)), 40f); Shot("flowers_bookshelf");
            Aim(seat, RoomFrame.ToWorld(new Vector3(0.3f, 1.25f, -3.7f)), 60f); Shot("seat_fireplace_flowers");
            Aim(RoomFrame.ToWorld(new Vector3(2.85f, 2.6f, 0.95f)), RoomFrame.ToWorld(new Vector3(-0.9f, 0.9f, -2.4f)), 75f); Shot("overview_flowers");

            Bounds keys = default;
            IInstrument piano = App.Instruments.First(x => x.Kind == InstrumentKind.Piano);
            bool any = false;
            foreach (Renderer r in piano.PlayedParts) { if (r == null || !r.enabled) continue; if (!any) { keys = r.bounds; any = true; } else keys.Encapsulate(r.bounds); }
            Aim(seat, keys.center + Vector3.up * 0.2f, 70f);
            long restDraws = -1;
            yield return Draws(v => restDraws = v);

            // ---- a take plays: they come around the piano
            IRecorder rec = App.Get<IRecorder>();
            // notes for 9 s, a 9 s pause (they go to the flowers), notes again
            var song = Song(0.05f, 9f, 0.22f, 0.9f);
            song.AddRange(Song(18f, 26f, 0.22f, 0.9f));
            Assert.IsTrue(rec.PlayEvents(song, 26.5));
            StepSeconds(5.5f);
            Assert.That(Flies.Flock.Alive, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount), "they came");
            Aim(seat, keys.center + Vector3.up * 0.25f, 75f); Shot("butterflies_seat_piano");
            long playDraws = -1;
            yield return Draws(v => playDraws = v);
            Debug.Log($"[Butterflies] seat draws toward the piano: rest {restDraws}, while they fly {playDraws}");
            int n = Nearest(keys.center);
            Assert.That(n, Is.GreaterThanOrEqualTo(0));
            Vector3 bp = Flies.Flock.Get(n).Pos;
            Vector3 dir = (bp - App.Head.position); dir.y = 0f; dir = dir.normalized;
            Aim(bp - dir * 0.32f + Vector3.up * 0.06f, bp, 38f); Shot("butterflies_close");
            StepSeconds(0.08f);
            bp = Flies.Flock.Get(n).Pos;
            Aim(bp + Vector3.Cross(Vector3.up, dir) * 0.3f + Vector3.up * 0.12f, bp, 38f); Shot("butterflies_close_side");
            Aim(new Vector3(1.4f, 1.9f, 0.9f), keys.center + Vector3.up * 0.3f, 62f); Shot("butterflies_over_piano");

            // ---- the pause: to the flowers
            StepSeconds(9f);   // t = 14.5 s into the take: 5.5 s since the last note
            Debug.Log($"[Butterflies] landings by now: {Flies.Flock.Landings}, out {Flies.Flock.Alive}");
            Assert.That(Flies.Flock.Landings, Is.GreaterThan(0), "they landed on the flowers");
            Aim(RoomFrame.ToWorld(new Vector3(0.55f, 1.78f, -2.75f)), RoomFrame.ToWorld(new Vector3(0.95f, 1.55f, -3.66f)), 42f); Shot("butterflies_on_mantel_tulips");
            Aim(RoomFrame.ToWorld(new Vector3(-0.2f, 1.7f, -2.95f)), RoomFrame.ToWorld(new Vector3(-0.5f, 1.4f, -3.66f)), 40f); Shot("butterflies_on_mantel_daisies");
            Aim(RoomFrame.ToWorld(new Vector3(2.3f, 1.12f, -1.0f)), RoomFrame.ToWorld(new Vector3(3.06f, 0.82f, -1.2f)), 40f); Shot("butterflies_on_sill_flowers");
            Aim(RoomFrame.ToWorld(new Vector3(-2.25f, 2.2f, -2.45f)), RoomFrame.ToWorld(new Vector3(-3.0f, 2.08f, -2.98f)), 40f); Shot("butterflies_on_bookshelf_flowers");
            int perched = Enumerable.Range(0, ButterflyFlock.Max).Count(i => Flies.Flock.Get(i).Mode == ButterflyMode.Perched);
            if (perched > 0)
            {
                int p = Enumerable.Range(0, ButterflyFlock.Max).First(i => Flies.Flock.Get(i).Mode == ButterflyMode.Perched);
                Vector3 pp = Flies.Flock.Get(p).Pos;
                Vector3 back = (App.Head.position - pp); back.y = 0f; back = back.normalized;
                Aim(pp + back * 0.3f + Vector3.up * 0.1f, pp, 35f); Shot("butterfly_perched_close");
            }
            rec.Stop();
            StepSeconds(ButterflyFlock.LeaveAfterStop + ButterflyFlock.FadeOut + 1f);
            Assert.That(Flies.Flock.Alive, Is.EqualTo(0));

            // ---- mixed reality: the room hidden (grey stands in for passthrough), the butterflies around the piano
            Room.SetVisible(false);
            _cam.backgroundColor = new Color(0.42f, 0.4f, 0.37f, 1f);
            Assert.IsTrue(rec.PlayEvents(Song(0.05f, 12f, 0.22f, 0.9f), 12.5));
            StepSeconds(5.5f);
            Aim(App.Head.position, keys.center + Vector3.up * 0.25f, 75f); Shot("mr_butterflies_seat_piano");
            Aim(new Vector3(1.4f, 1.9f, 0.9f), keys.center + Vector3.up * 0.3f, 62f); Shot("mr_butterflies_over_piano");
            Assert.That(Flies.Flock.Alive, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount));
            rec.Stop();
            Room.SetVisible(true);
        }
    }
}
