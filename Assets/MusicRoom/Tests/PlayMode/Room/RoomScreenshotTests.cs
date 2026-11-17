// Visual check (Category Graphics, run with `Tools/unity.sh test-play --gpu`): boots the app desktop-style, waits until
// every room texture is generated (or baked) and the reflection probe has rendered, then saves PNGs of the views to
// compare with the reference views (spec 07): the seated view toward the piano, the fireplace, the
// window at full snow cover, the side table with the switches, the seat's left and right, a plan and a corner overview.
// Output: $MUSICROOM_SHOTS_ROOT/room, else $MUSICROOM_SHOTS, else Builds/Shots/room. Each shot logs how many renderers
// are in view, and the
// seated head camera's draw count (profiler "Draw Calls Count", as the round-1 DrawBudgetTests) is logged and kept
// inside the whole-scene budget of 150 (ARCHITECTURE.md section 17).
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Room
{
    [Category("Graphics")]
    public class RoomScreenshotTests
    {
        const int Width = 1280, Height = 656;   // the reference renders' size
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            RoomModuleTests.CollectGarbage();
            _boot = new TestBoot(headless: false);   // no hands posed (untracked): nothing in the way
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            RoomModuleTests.CollectGarbage();
        }

        static string OutDir()
        {
            string dir = ShotDirs.FromRoot("room") ?? Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            if (string.IsNullOrEmpty(dir)) dir = ShotDirs.Default("room");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SavesTheRoomViews()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var app = App.Current;
            var room = (RoomModule)app.Modules.First(m => m is RoomModule);
            var layout = app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while ((!layout.FirstArrangeDone || room.TexturesPending > 0 || !room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 300f) yield return null;
            Assert.That(room.TexturesPending, Is.EqualTo(0), "every room texture arrived");
            Debug.Log($"[Room] screenshots: textures ready after {Time.realtimeSinceStartup - t0:F1} s");
            for (int i = 0; i < 30; i++) yield return null;   // let the fire, smoke and embers run

            long seatDraws = -1, seatDrawsSnow = -1;
            yield return SeatDraws(app, v => seatDraws = v);

            string dir = OutDir();
            var camGo = new GameObject("ScreenshotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                Vector3 seat = app.Head.position;
                yield return Shot(cam, rt, readback, dir, "seat_piano", seat, seat + (new Vector3(0f, 0.95f, 0.45f) - DesktopRigFallback.EyePosition), 55f);
                yield return Shot(cam, rt, readback, dir, "seat_fireplace", new Vector3(0f, 1.2f, 0f), W(0f, 1.1f, -3f), 55f);
                yield return Shot(cam, rt, readback, dir, "seat_right", new Vector3(0f, 1.2f, 0f), W(3f, 1.2f, -1f), 55f);
                yield return Shot(cam, rt, readback, dir, "seat_left", new Vector3(0f, 1.2f, 0f), W(-3f, 1.1f, -1.3f), 55f);
                Vector3 panel = room.Switches.Panel.position;
                room.Set(AmbienceKind.Fire, true);   // FIRE's LED lit, WIND's and SNOW's dark (one vertex-coloured LED mesh)
                yield return Shot(cam, rt, readback, dir, "side_table_switches", panel + new Vector3(0.32f, 0.38f, 0.12f), panel + new Vector3(0f, 0.02f, 0f), 50f);
                yield return Shot(cam, rt, readback, dir, "overview_plan", W(0f, 8.6f, -1.48f), W(0f, 0f, -1.4799f), 52f, RoomFrame.DirToWorld(Vector3.forward));
                yield return Shot(cam, rt, readback, dir, "overview_corner", W(2.85f, 2.6f, 0.95f), W(-0.9f, 0.5f, -2.4f), 75f);

                // The window at full snow cover (the reference full-cover view): snowing, everything settled.
                room.Set(AmbienceKind.Snow, true);
                room.Weather.Settle(1f, 1f, 30.0);
                t0 = Time.realtimeSinceStartup;
                for (int i = 0; i < 5; i++) yield return null;
                while (room.TexturesPending > 0 && Time.realtimeSinceStartup - t0 < 120f) yield return null;
                for (int i = 0; i < 10; i++) yield return null;
                yield return Shot(cam, rt, readback, dir, "window_snow_full_cover", W(0.3f, 1.45f, -1.18f), W(3.2f, 1.55f, -1.18f), 55f);
                yield return Shot(cam, rt, readback, dir, "seat_right_snow", new Vector3(0f, 1.2f, 0f), W(3f, 1.2f, -1f), 55f);
                yield return SeatDraws(app, v => seatDrawsSnow = v);
                Debug.Log($"[Room] seat draws: {seatDraws} clear, {seatDrawsSnow} snowing (snow field + frost + mound add 3)");
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        /// <summary>The head camera's draws from the desktop seat (toward the piano), rendered into a texture for a few
        /// frames like DrawBudgetTests. -1 when the counters are unavailable.</summary>
        static IEnumerator SeatDraws(App app, Action<long> result)
        {
            var cam = app.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "RoomDraws" };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            for (int i = 0; i < 8; i++) yield return null;
            long d = draws.Valid ? draws.LastValue : -1, sp = setPass.Valid ? setPass.LastValue : -1;
            draws.Dispose(); setPass.Dispose();
            cam.targetTexture = prev;
            Object.Destroy(rt);
            Debug.Log($"[Room] seat draws={d} setPass={sp} (whole-scene budget 150; VR room target ~34, 37 with snow)");
            if (d >= 0) Assert.That(d, Is.LessThanOrEqualTo(150));
            result(d);
        }

        /// <summary>A room-frame point in the world: the views keep their reference names and framing of the room's parts
        /// (seat_right looks at the window, now on the seat's left) while the room faces the fire (RoomFrame).</summary>
        static Vector3 W(float x, float y, float z) => RoomFrame.ToWorld(new Vector3(x, y, z));

        static IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 target, float fov, Vector3? up = null)
        {
            yield return null;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, up ?? Vector3.up));
            cam.fieldOfView = fov;
            cam.aspect = (float)Width / Height;
            cam.targetTexture = rt;
            // The flakes are sized against the target's half height, like a screen-space point size.
            Shader.SetGlobalFloat(RoomModule.SnowPxId, Height * 0.5f);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, readback.EncodeToPNG());
            // A rendered room is not one flat colour.
            var px = readback.GetPixels32();
            double mean = 0, var = 0;
            for (int i = 0; i < px.Length; i += 31) mean += px[i].r + px[i].g + px[i].b;
            mean /= px.Length / 31 * 3.0;
            for (int i = 0; i < px.Length; i += 31) { double d = (px[i].r + px[i].g + px[i].b) / 3.0 - mean; var += d * d; }
            var /= px.Length / 31;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int inView = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.enabled && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds));
            Debug.Log($"[Room] shot {name}: {path} mean {mean:F1} sd {Math.Sqrt(var):F1}, {inView} renderers in view");
            Assert.That(File.Exists(path), Is.True);
            Assert.That(Math.Sqrt(var), Is.GreaterThan(2.0), name + " has detail");
        }
    }
}
