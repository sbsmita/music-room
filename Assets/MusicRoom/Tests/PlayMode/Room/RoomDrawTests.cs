// The room's own draw budget on the GPU (Category Graphics: Tools/unity.sh test-play --gpu), measured like the
// foundation's DrawBudgetTests with the profiler's "Draw Calls Count": from the seat (0, 1.2, 0), a 90 x 90 degree view
// (wider than the desktop's 55, about a Quest eye) in each of the four directions, with only the room's renderers
// enabled, clear and snowing; the side-table rig on its own; and the instruments' contact shadows, one draw for all of
// them (platform.md 11). ARCHITECTURE.md sections 13, 14, 17: the VR room
// ~35 draws (38 with snow, the flowers' petals included), table + tape deck + switches <= 15 (the Room's share here, the deck being the Lessons
// module's). Each count is the frame's draws minus the same camera's draws with nothing enabled (the pipeline's own
// work for this camera, e.g. an MSAA resolve), so it is what the renderers themselves cost.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
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
    public class RoomDrawTests
    {
        /// <summary>35 / 38 since the flowers (2026-11-11): every petal is one more renderer.</summary>
        public const int RoomDraws = 35, RoomDrawsSnow = 38, TableDraws = 8, TableAndDeckDraws = 15;
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            RoomModuleTests.CollectGarbage();
            _boot = new TestBoot(headless: false);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            RoomModuleTests.CollectGarbage();
        }

        static RoomModule Room => (RoomModule)App.Current.Modules.First(m => m is RoomModule);

        /// <summary>Draw calls of one frame rendered by <paramref name="cam"/> alone, with only <paramref name="keep"/>'s
        /// renderers enabled (everything else in the scene switched off for the measurement, then restored).</summary>
        static IEnumerator Measure(Camera cam, Transform[] keep, Vector3 eye, Vector3 look, Action<long> result)
        {
            var off = new List<Renderer>();
            void SwitchOff()
            {
                // Again every frame: modules re-enable their own renderers in their Update (e.g. the rig shadows).
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.enabled && !keep.Any(k => r.transform.IsChildOf(k))) { r.enabled = false; off.Add(r); }
            }
            SwitchOff();
            var head = App.Current.Rig.HeadCamera;
            bool headWas = head != null && head.enabled;
            if (head != null) head.enabled = false;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.enabled = true;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { yield return null; SwitchOff(); }
            long d = draws.Valid ? draws.LastValue : -1;
            draws.Dispose();
            cam.enabled = false;
            if (head != null) head.enabled = headWas;
            foreach (var r in off) if (r != null) r.enabled = true;
            result(d);
        }

        /// <summary>The camera's draws with every renderer off (the pipeline's own passes for it).</summary>
        static IEnumerator Baseline(Camera cam, Action<long> result) =>
            Measure(cam, Array.Empty<Transform>(), new Vector3(0f, 1.2f, 0f), new Vector3(0f, 1.2f, 1f), result);

        static Camera MakeCamera(out RenderTexture rt)
        {
            var go = new GameObject("RoomDrawCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.allowHDR = false;
            rt = new RenderTexture(512, 512, 24) { name = "RoomDraws" };
            cam.targetTexture = rt;
            return cam;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheRoomStaysInsideItsDrawBudgetInEveryDirection()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var room = Room;
            float t0 = Time.realtimeSinceStartup;
            while ((room.TexturesPending > 0 || !room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            for (int i = 0; i < 3; i++) yield return null;   // past the probe's own frame
            var cam = MakeCamera(out var rt);
            try
            {
                Vector3 seat = new Vector3(0f, 1.2f, 0f);
                var dirs = new (string name, Vector3 look)[]
                {
                    // the room frame's directions (the room faces the fire: "back (fireplace)" is now straight ahead of the seat)
                    ("front", seat + RoomFrame.DirToWorld(Vector3.forward)), ("back (fireplace)", seat + RoomFrame.DirToWorld(Vector3.back)),
                    ("left (shelf)", seat + RoomFrame.DirToWorld(Vector3.left)), ("right (window)", seat + RoomFrame.DirToWorld(Vector3.right)),
                };
                var keep = new[] { room.Root };
                long zero = -1;
                yield return Baseline(cam, v => zero = v);
                if (zero < 0) Assert.Ignore("render counters unavailable in this player");
                Debug.Log($"[Room] draw baseline (camera alone): {zero}");
                foreach (bool snow in new[] { false, true })
                {
                    if (snow)
                    {
                        room.Set(AmbienceKind.Snow, true);
                        room.Weather.Settle(1f, 1f, room.Weather.TimeD);
                        for (int i = 0; i < 3; i++) yield return null;
                    }
                    foreach (var (name, look) in dirs)
                    {
                        long d = -1;
                        yield return Measure(cam, keep, seat, look, v => d = v);
                        d -= zero;
                        Debug.Log($"[Room] draws from the seat, {name}{(snow ? ", snowing" : "")}: {d}");
                        Assert.That(d, Is.LessThanOrEqualTo(snow ? RoomDrawsSnow : RoomDraws), name + (snow ? " (snow)" : ""));
                    }
                }
            }
            finally
            {
                Object.Destroy(cam.gameObject);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        [UnityTest]
        public IEnumerator TheSideTableRigIsEightDraws()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var room = Room;
            for (int i = 0; i < 3; i++) yield return null;
            var cam = MakeCamera(out var rt);
            try
            {
                Vector3 panel = room.Switches.Panel.position;
                long zero = -1, d = -1;
                yield return Baseline(cam, v => zero = v);
                yield return Measure(cam, new[] { room.TableRig.Group }, panel + new Vector3(0.45f, 0.5f, 0.2f), panel - new Vector3(0f, 0.25f, 0f), v => d = v);
                if (d < 0 || zero < 0) Assert.Ignore("render counters unavailable in this player");
                var names = room.TableRig.Group.GetComponentsInChildren<Renderer>().Select(r => r.name);
                d -= zero;
                Debug.Log($"[Room] side-table rig draws: {d} over a baseline of {zero} (table + deck + switches budget 15; the deck is the Lessons module's): {string.Join(", ", names)}");
                // The rig group carries the Lessons tape deck too when that module is booted (its 7 draws, TapeDeck.DrawBudget):
                // the table's own 8 plus the deck stay inside the shared table + deck + switches budget of 15.
                bool deck = App.Current.Instruments.Any(i => i.Kind == InstrumentKind.Table && i.TickOrder == 30 && i.Root != null && i.Root.IsChildOf(room.TableRig.Group));
                Assert.That(d, Is.LessThanOrEqualTo(deck ? TableAndDeckDraws : TableDraws));
            }
            finally
            {
                Object.Destroy(cam.gameObject);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        /// <summary>platform.md 11 / 12.3: the instruments' contact shadows are one draw for every at-seat instrument (they were
        /// one renderer per rig: 3 draws with the default set).</summary>
        [UnityTest]
        public IEnumerator TheRigShadowsAreOneDraw()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var room = Room;
            for (int i = 0; i < 3; i++) yield return null;
            int shown = room.Shadows.Entries.Count(e => e.Shown);
            Assert.That(shown, Is.EqualTo(3), "the default set's three quads");
            var cam = MakeCamera(out var rt);
            try
            {
                long zero = -1, d = -1;
                yield return Baseline(cam, v => zero = v);
                // From above and behind the seat, the floor around it in view (all three quads).
                yield return Measure(cam, new[] { room.Shadows.Root }, new Vector3(0f, 2.6f, -1.3f), new Vector3(0f, 0f, 0.2f), v => d = v);
                if (d < 0 || zero < 0) Assert.Ignore("render counters unavailable in this player");
                d -= zero;
                Debug.Log($"[Room] rig shadows: {d} draw(s) for {shown} quads at the seat (was 3 renderers, one per rig)");
                Assert.That(d, Is.EqualTo(1), "one combined mesh");
            }
            finally
            {
                Object.Destroy(cam.gameObject);
                rt.Release();
                Object.Destroy(rt);
            }
        }
    }
}
