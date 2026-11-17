// Screenshots of the Layout for eight instruments (Category Graphics, `Tools/unity.sh test-play --gpu`): every real module, a
// simulated headset, manual frames. The default room from the seat (as today at the seat; the five stubs on display at their
// spots as neutral wood placeholders), the room from behind the seat with the displayed instruments, a glide mid-way, a set
// of three with a stub, and the ray summon's card. Written to $MUSICROOM_SHOTS, else $MUSICROOM_SHOTS_ROOT/platform-layout,
// else Builds/Shots/platform-layout. Also logs the head
// camera's draws with all eight in the room (budget 150).
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Layout
{
    [Category("Graphics")]
    public class LayoutShotsTests
    {
        const int Width = 1280, Height = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);                        // no tour in the way of the views
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is LayoutModule lm) _layout = lm;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            GC.Collect();
        }

        static string OutDir()
        {
            string env = Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            string dir = !string.IsNullOrEmpty(env) ? env : ShotDirs.FromRoot("platform-layout")
                ?? ShotDirs.Default("platform-layout");
            Directory.CreateDirectory(dir);
            return dir;
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheRoomWithEightInstruments()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            RoomModule room = null;
            foreach (var m in _app.Modules) if (m is RoomModule rm) room = rm;
            float t0 = Time.realtimeSinceStartup;
            while ((_app.Rig.State == XrState.Starting || !_layout.FirstArrangeDone || (room != null && (room.TexturesPending > 0 || !room.ProbeRendered)))
                   && Time.realtimeSinceStartup - t0 < 300f)
            {
                _boot.Step(Dt);
                yield return null;
            }
            yield return Frames(30);
            foreach (InstrumentDescriptor d in _app.Registry.All)
            {
                InstrumentPlace place = _layout.PlaceOf(d.Kind);
                Debug.Log($"[LayoutShots] {d.Id}: {place}{(_layout.TryGetDisplayPoint(d.Kind, out Vector3 p) ? " pick " + p.ToString("F2") : "")}");
                if (d.Placeholder) Assert.That(place, Is.EqualTo(InstrumentPlace.Display), d.Id + " on display");
            }

            string dir = OutDir();
            var camGo = new GameObject("LayoutShotsCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.cullingMask = _app.Rig.HeadCamera.cullingMask;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                Vector3 eye = _app.Head.position;
                Vector3 fwd = _app.Head.forward; fwd.y = 0f; fwd.Normalize();

                // 1. The default room from the seat: ahead (the fire, since the room faces it: RoomFrame), to the left (the window:
                //    the marimba, the hand drums, the bowls on the sill) and to the right (the kit and the handpan on that wall).
                yield return Shot(cam, rt, readback, dir, "seat_ahead", eye, eye + fwd - Vector3.up * 0.35f, 80f);
                yield return Shot(cam, rt, readback, dir, "seat_left", eye, eye + Quaternion.Euler(0f, -75f, 0f) * fwd - Vector3.up * 0.3f, 85f);
                yield return Shot(cam, rt, readback, dir, "seat_right", eye, eye + Quaternion.Euler(0f, 112f, 0f) * fwd - Vector3.up * 0.35f, 85f);

                // 2. From the fireplace end, looking back at the seat: the displayed instruments around the room (room-frame views,
                //    turned with the room by RoomFrame).
                yield return Shot(cam, rt, readback, dir, "behind_seat", RoomFrame.ToWorld(new Vector3(0.2f, 2.05f, -3.55f)), RoomFrame.ToWorld(new Vector3(0f, 0.7f, -0.4f)), 95f);
                yield return Shot(cam, rt, readback, dir, "window_wall", RoomFrame.ToWorld(new Vector3(-0.6f, 1.6f, -1.4f)), RoomFrame.ToWorld(new Vector3(2.8f, 0.6f, -1.3f)), 70f);
                yield return Shot(cam, rt, readback, dir, "left_wall", RoomFrame.ToWorld(new Vector3(0.6f, 1.6f, -0.9f)), RoomFrame.ToWorld(new Vector3(-2.8f, 0.9f, -0.5f)), 75f);
                long draws = -1;
                yield return HeadDraws(v => draws = v);
                Debug.Log($"[LayoutShots] head camera draws with the default set and four on display = {draws} (budget 150)");
                if (draws >= 0) Assert.That(draws, Is.LessThanOrEqualTo(150));

                // 3. A glide mid-way: the bowls leave the sill for the seat (the harp makes room).
                Assert.IsTrue(_layout.Bring(InstrumentKind.Bowls, BringReason.Menu));
                RigGlide g = _layout.GlideOf(InstrumentKind.Bowls);
                Assert.That(g, Is.Not.Null);
                int half = Mathf.RoundToInt((g.Delay + g.Duration * 0.5f) / Dt);
                yield return Frames(half);
                Transform bowls = _layout.Get(RigId.Bowls).Group;
                Debug.Log($"[LayoutShots] glide mid-way: bowls at {bowls.position.ToString("F2")}, {g.Length:F2} m in {g.Duration:F2} s");
                yield return Shot(cam, rt, readback, dir, "glide_midway", RoomFrame.ToWorld(new Vector3(0.9f, 1.9f, -2.6f)), bowls.position, 80f);
                for (int i = 0; i < 150 && _layout.AnyMoving; i++) yield return Frames(1);

                // 4. A set of three with a stub: piano, drums and the bowls in the pocket left of the knees.
                Assert.That(_layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Bowls }));
                yield return Shot(cam, rt, readback, dir, "set_with_stub_seat", eye, eye + Quaternion.Euler(0f, -30f, 0f) * fwd - Vector3.up * 0.7f, 85f);
                yield return Shot(cam, rt, readback, dir, "set_with_stub_overview", eye + Vector3.up * 0.9f - fwd * 1.3f, eye + fwd * 0.4f - Vector3.up * 0.9f, 80f);

                // 5. The ray summon's card on the marimba (the right hand points at it from beside the head).
                Assert.IsTrue(_layout.TryGetDisplayPoint(InstrumentKind.Marimba, out Vector3 th));
                Vector3 hand = eye + new Vector3(0.25f, 0.2f, 0.1f);
                _boot.Hands.SetTracked(HandSide.Right, true);
                for (int i = 0; i < 30; i++)
                {
                    _boot.Hands.SetPointer(HandSide.Right, new Pose(hand, Quaternion.LookRotation(th - hand)));
                    _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(th - hand)).WithIndexTipAt(hand));
                    yield return Frames(1);
                }
                Debug.Log($"[LayoutShots] summon card {_layout.Summon.CardVisible} '{_layout.Summon.CardText}'");
                yield return Shot(cam, rt, readback, dir, "summon_card", eye + new Vector3(0.05f, 0.05f, 0f), th + Vector3.up * 0.06f, 50f);
                yield return Shot(cam, rt, readback, dir, "summon_card_close", eye + new Vector3(0.05f, 0.05f, 0f), th + Vector3.up * RaySummon.CardAbove, 9f);
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        IEnumerator HeadDraws(Action<long> result)
        {
            var cam = _app.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "LayoutDraws" };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 8; i++) { _boot.Step(Dt); yield return null; }
            long d = recorder.Valid ? recorder.LastValue : -1;
            recorder.Dispose();
            cam.targetTexture = prev;
            Object.Destroy(rt);
            result(d);
        }

        IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 look, float fov)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
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
            Debug.Log($"[LayoutShots] shot {name}");
        }
    }
}
