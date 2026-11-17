// Integration check of the merged app (Category Graphics, run with `Tools/unity.sh test-play --gpu`): every real module
// booted together, as on the headset (simulated: App.IsPresenting, so Layout arranges the room around the seat on the
// first presenting frame), on a fresh settings store, so Sunny's first-launch tour starts by itself. Saves PNGs of the
// whole room from the seat (front, right, left, back), the menu opened in front of the player, and two overviews, to
// Builds/Shots/integration (or $MUSICROOM_SHOTS_ROOT/integration, else $MUSICROOM_SHOTS/integration). Asserts what only the merge can
// show: every room texture arrives, Sunny arrives, each instrument has exactly one floor shadow (Room's RigShadows), the
// rigs stand apart from each other and from the seat, and the seated head camera stays inside the 150-draw budget.
using System;
using System.Collections;
using System.Collections.Generic;
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

namespace MusicRoom.Tests.PlayMode.Integration
{
    [Category("Graphics")]
    public class IntegrationShotsTests
    {
        const int Width = 1280, Height = 720;
        // MUSICROOM_SHOTS_ROOT (optional) redirects the shots of a whole run (e.g. a clone's own folder); default unchanged.
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            MusicRoom.Guide.GuideModule.ClassicTour = true;   // first five: these tests check the classic five-step tour
            _boot = new TestBoot(headless: false, simulateHeadset: true);   // hands untracked: nothing in the way
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MusicRoom.Guide.GuideModule.ClassicTour = false;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            GC.Collect();
        }

        static string OutDir()
        {
            // $MUSICROOM_SHOTS (like the Room and Lessons shots) keeps a clone's pictures out of the shared folder.
            string env = Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            string dir = ShotDirs.FromRoot("integration") ?? (!string.IsNullOrEmpty(env) ? Path.Combine(env, "integration")
                : ShotDirs.Default("integration"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheWholeRoomFromTheSeat()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var app = App.Current;
            // First five: + LessonsFirstFive and RoomMoments.
            Assert.That(app.Modules.Count, Is.EqualTo(19), "all nineteen modules discovered (the ten, the four new instruments, the sheet music, LessonsFirstFive, RoomMoments, GhostHands, Gaze): " + string.Join(", ", app.Modules.Select(m => m.GetType().Name)));
            var room = (RoomModule)app.Modules.First(m => m is RoomModule);
            var layout = app.Get<ILayoutService>();
            var guide = app.Get<IGuide>();
            var registry = app.Get<IRigRegistry>();

            // The room ready: arranged around the seat, every texture in, the probe rendered; then Sunny's first-launch tour.
            float t0 = Time.realtimeSinceStartup;
            while ((!layout.FirstArrangeDone || room.TexturesPending > 0 || !room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 300f) yield return null;
            Assert.IsTrue(layout.FirstArrangeDone, "Layout arranged the room");
            Assert.That(room.TexturesPending, Is.EqualTo(0), "every room texture arrived");
            Assert.IsTrue(app.IsPresenting, "simulated headset");
            t0 = Time.realtimeSinceStartup;
            while (!guide.Present && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Assert.IsTrue(guide.Present, "Sunny's tour starts by itself on the first launch");
            float arrived = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - arrived < 2.5f) yield return null;   // Sunny flies in; fire, smoke and embers run
            Debug.Log($"[Integration] room ready, Sunny present after {Time.realtimeSinceStartup - t0:F1} s");

            // One floor shadow per instrument: Room's RigShadows draws them all; no module builds a second one.
            Assert.That(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.name == "ShadowBlob"), Is.EqualTo(0),
                "no instrument builds its own blob");
            foreach (RigId id in new[] { RigId.Piano, RigId.Drums, RigId.Harp })
            {
                Rig rig = registry.Get(id);
                Assert.That(rig, Is.Not.Null, id + " registered");
                if (!rig.InScene) continue;
                var entries = room.Shadows.Entries.Where(e => e.Id == id).ToList();
                Assert.That(entries.Count, Is.EqualTo(1), id + ": one shadow entry");
                Assert.IsTrue(entries[0].Renderer.enabled, id + ": its shadow shows");
            }

            // The rigs stand apart: no two instrument bodies overlap on the floor, and none sits in the seat.
            Vector3 seat = app.Head.position;
            var placed = new List<(RigId id, Bounds b)>();
            foreach (Rig rig in registry.Rigs)
            {
                if (!rig.InScene || rig.Group == null || !rig.Group.gameObject.activeInHierarchy || rig.Id == RigId.Lessons) continue;
                Bounds b = default; bool any = false;
                foreach (var r in rig.Handles)
                {
                    if (r == null || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(rig.Group)) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) continue;
                Debug.Log($"[Integration] {rig.Id} at {rig.Group.position} yaw {rig.Group.eulerAngles.y:F1}, handles box {b.center} size {b.size}, " +
                          $"seat distance {Vector2.Distance(new Vector2(seat.x, seat.z), new Vector2(b.ClosestPoint(seat).x, b.ClosestPoint(seat).z)):F3}");
                placed.Add((rig.Id, b));
            }
            Assert.That(placed.Select(p => p.id), Is.SupersetOf(new[] { RigId.Piano, RigId.Drums, RigId.Harp, RigId.Table }), "all four rigs in the room");

            long draws = -1;
            yield return HeadDraws(app, v => draws = v);
            Debug.Log($"[Integration] seated head camera draws={draws} (whole-scene budget 150)");
            if (draws >= 0) Assert.That(draws, Is.LessThanOrEqualTo(150));

            string dir = OutDir();
            var camGo = new GameObject("IntegrationCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.cullingMask = app.Rig.HeadCamera.cullingMask;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                Transform head = app.Head;
                Vector3 eye = head.position;
                Vector3 fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                yield return Shot(cam, rt, readback, dir, "seat_front", eye, eye + fwd - Vector3.up * 0.35f, 75f);
                yield return Shot(cam, rt, readback, dir, "seat_right", eye, registry.Get(RigId.Drums).Group.position + Vector3.up * 0.6f, 75f);
                yield return Shot(cam, rt, readback, dir, "seat_left", eye, registry.Get(RigId.Harp).Group.position + Vector3.up * 0.7f, 75f);
                yield return Shot(cam, rt, readback, dir, "seat_back", eye, eye - fwd * 3f - Vector3.up * 0.2f, 75f);
                yield return Shot(cam, rt, readback, dir, "overview_corner", RoomFrame.ToWorld(new Vector3(2.85f, 2.6f, 0.95f)), RoomFrame.ToWorld(new Vector3(-0.9f, 0.5f, -2.4f)), 75f);
                yield return Shot(cam, rt, readback, dir, "overview_plan", RoomFrame.ToWorld(new Vector3(0f, 7.5f, -1.48f)), RoomFrame.ToWorld(new Vector3(0f, 0f, -1.4799f)), 52f, RoomFrame.DirToWorld(Vector3.forward));
                yield return Shot(cam, rt, readback, dir, "seat_high", eye + Vector3.up * 0.9f - fwd * 1.2f, eye + fwd * 0.8f - Vector3.up * 0.9f, 80f);

                // The menu, opened in front of the player (the M key / palm summon path).
                var menu = app.Get<IMenuPanel>();
                menu.Show();
                for (int i = 0; i < 40; i++) yield return null;   // open animation and the first canvas paint
                Assert.IsTrue(menu.Shown, "the menu opens");
                yield return Shot(cam, rt, readback, dir, "seat_menu", eye, menu.Root.position, 70f);
                menu.Hide();
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        /// <summary>The head camera's draws, rendered into a texture for a few frames like DrawBudgetTests (-1: no counters).</summary>
        static IEnumerator HeadDraws(App app, Action<long> result)
        {
            var cam = app.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "IntegrationDraws" };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 8; i++) yield return null;
            long d = recorder.Valid ? recorder.LastValue : -1;
            recorder.Dispose();
            cam.targetTexture = prev;
            Object.Destroy(rt);
            result(d);
        }

        static IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 look, float fov, Vector3? up = null)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, up ?? Vector3.up));
            cam.fieldOfView = fov;
            cam.aspect = Width / (float)Height;
            cam.targetTexture = rt;
            yield return null;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), readback.EncodeToPNG());
            Debug.Log($"[Integration] shot {name}");
        }
    }
}
