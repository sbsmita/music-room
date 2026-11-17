// The congas in the living room (Category Graphics: Tools/unity.sh test-play --gpu). Boots the whole app as on the
// headset (simulated, every real module, the room lit by its own lamps and fire, Sunny's tour marked done so it stays out
// of the pictures), then builds the pair with HandDrumsModel and places it TEST-ONLY at the seat (phase A has no registry
// slot for it): (1) at the Front home (rig 0.57 m ahead of the eyes) with the piano put away, (2) at the Right slot the
// platform gives the hand drums beside the piano, with the drum kit put away. Saves PNGs to
// $MUSICROOM_SHOTS_ROOT/handdrums, else $MUSICROOM_SHOTS/handdrums, else Builds/Shots/handdrums.
// Asserts the draw budget (3 renderers at the seat, the spec's 3 of 6) and the triangle budget (about 9 k).
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.HandDrums;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.HandDrums
{
    [Category("Graphics")]
    public class HandDrumsShotsTests
    {
        const int Width = 1280, Height = 720;
        TestBoot _boot;
        HandDrumsModel _model;

        static string OutDir()
        {
            string env = Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            string dir = ShotDirs.FromRoot("handdrums") ?? (!string.IsNullOrEmpty(env) ? Path.Combine(env, "handdrums") : ShotDirs.Default("handdrums"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _model?.Dispose(); _model = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            GC.Collect();
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheCongasAtTheSeatInTheRoom()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            var app = App.Current;
            var room = (RoomModule)app.Modules.First(m => m is RoomModule);
            var layout = app.Get<ILayoutService>();
            var registry = app.Get<IRigRegistry>();
            float t0 = Time.realtimeSinceStartup;
            while ((!layout.FirstArrangeDone || room.TexturesPending > 0 || !room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 300f) yield return null;
            Assert.IsTrue(layout.FirstArrangeDone, "the room arranged around the seat");
            for (int i = 0; i < 30; i++) yield return null;   // the fire and the lamps settle

            _model = new HandDrumsModel(null, app.Materials);
            int drawing = _model.Root.GetComponentsInChildren<Renderer>().Count(r => r.enabled);
            int tris = _model.Meshes.Triangles;
            Debug.Log($"[HandDrums] model: {drawing} renderers (draws at the seat; budget 6), {tris} triangles (spec ~9 k), {_model.Meshes.Heads.vertexCount} head vertices");
            Assert.That(drawing, Is.EqualTo(3), "shells, heads, hardware: 3 draws");
            Assert.That(tris, Is.LessThan(25000), "the platform's ceiling for a new instrument at the seat (platform 11: 25 k)");

            SeatFrame seat = layout.Seat;
            Rig piano = registry.Get(RigId.Piano), kit = registry.Get(RigId.Drums);
            string dir = OutDir();
            var camGo = new GameObject("HandDrumsCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 40f;
            cam.allowHDR = false; cam.allowMSAA = true;
            cam.cullingMask = app.Rig.HeadCamera.cullingMask;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                Vector3 eye = seat.Head;
                Vector3 fwd = seat.Forward, right = seat.Right;
                Transform rig = _model.Root.transform;

                // ---- 1. Front home: the drums 0.57 m ahead (the piano put away for the picture)
                bool pianoWas = piano.Group.gameObject.activeSelf;
                piano.Group.gameObject.SetActive(false);
                rig.SetPositionAndRotation(seat.Place(new Vector3(0, 0, 0.57f)), Quaternion.Euler(0, seat.Yaw(0), 0));
                Vector3 mid = (_model.HeadPoint(0, 0, 0) + _model.HeadPoint(1, 0, 0)) * 0.5f;
                yield return Shot(cam, rt, readback, dir, "seat_front", eye, mid + fwd * 0.05f, 75f);
                yield return Shot(cam, rt, readback, dir, "seat_hands", eye, mid - Vector3.up * 0.05f, 55f);
                yield return Shot(cam, rt, readback, dir, "front_three_quarter", mid + fwd * 1.15f - right * 0.85f + Vector3.up * 0.25f, mid - Vector3.up * 0.25f, 50f);
                yield return Shot(cam, rt, readback, dir, "side", mid + right * 1.45f + Vector3.up * 0.05f + fwd * 0.05f, mid - Vector3.up * 0.32f, 45f);
                yield return Shot(cam, rt, readback, dir, "low_stands", mid + fwd * 0.95f + right * 0.35f + Vector3.up * -0.45f, mid - Vector3.up * 0.55f, 55f);
                Vector3 conga = _model.HeadPoint(0, 0, 0);
                yield return Shot(cam, rt, readback, dir, "close_conga_head", conga - fwd * 0.22f + Vector3.up * 0.32f, conga, 45f);
                yield return Shot(cam, rt, readback, dir, "close_shell", _model.HeadPoint(1, 0, 0) - fwd * 0.42f + right * 0.30f - Vector3.up * 0.25f, _model.HeadPoint(1, 0, 0) - Vector3.up * 0.30f, 40f);
                // a u .8 bass on the conga and an open tone on the tumba, 6 ms after landing: the heads dented
                var bass = new PatchPoint { Rho = 0.1f, Theta = 0f };
                _model.Strike(0, HandPart.Palm, 0.8, PatchShape.Points, bass, default, default, 1, 0);
                _model.Strike(1, HandPart.Fingers, 0.8, PatchShape.Line, new PatchPoint { Rho = 0.97f, Theta = 0.35f }, new PatchPoint { Rho = 0.5f, Theta = 0.35f }, default, 2, 0);
                Assert.IsTrue(_model.Update(0.006), "the dents write the head grid");
                yield return Shot(cam, rt, readback, dir, "close_dents", mid - fwd * 0.30f + Vector3.up * 0.30f, mid, 50f);
                _model.Update(1.0);
                Assert.IsFalse(_model.Update(1.1), "the heads are static again once the dents recovered");
                // review item 9.6: the dent's cost per frame on the main thread (two strokes and a resting hand moving the grid)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int frames = 0;
                for (int i = 0; i < 120; i++)
                {
                    double tt = 2 + i / 72.0;
                    if (i % 18 == 0) { _model.Strike(0, HandPart.Palm, 0.8, PatchShape.Points, bass, default, default, 1, tt); _model.Strike(1, HandPart.Tips, 0.9, PatchShape.Points, new PatchPoint { Rho = 0.55f, Theta = 0.3f }, new PatchPoint { Rho = 0.58f, Theta = 0.05f }, new PatchPoint { Rho = 0.58f, Theta = 0.55f }, 3, tt); }
                    if (i == 30) _model.Rest(1, 0, new RestPoint { Part = HandPart.Palm, Rho = 0.15f, Theta = 3.1f }, default, 1, tt);
                    if (_model.Update(tt)) frames++;
                }
                Debug.Log($"[HandDrums] head dents: {sw.Elapsed.TotalMilliseconds * 1000 / 120:F0} us per frame on the main thread ({frames}/120 frames wrote the grid of {_model.Meshes.Heads.vertexCount} vertices)");
                _model.LiftAll(); _model.Update(10);
                piano.Group.gameObject.SetActive(pianoWas);

                // ---- 2. Beside the piano: the hand drums take the Right slot (pair table), the kit put away for the picture
                bool kitWas = kit.Group.gameObject.activeSelf;
                kit.Group.gameObject.SetActive(false);
                Quaternion r90 = Quaternion.Euler(0, 90, 0);
                rig.SetPositionAndRotation(seat.Place(r90 * new Vector3(0, 0, 0.57f)), Quaternion.Euler(0, seat.Yaw(90), 0));
                Vector3 mid2 = (_model.HeadPoint(0, 0, 0) + _model.HeadPoint(1, 0, 0)) * 0.5f;
                yield return Shot(cam, rt, readback, dir, "seat_right_with_piano", eye, mid2 + Vector3.up * 0.05f, 75f);
                yield return Shot(cam, rt, readback, dir, "overview_with_piano", eye + Vector3.up * 0.9f - fwd * 1.3f - right * 0.4f, eye + fwd * 0.45f + right * 0.3f - Vector3.up * 0.75f, 70f);
                kit.Group.gameObject.SetActive(kitWas);
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release(); Object.Destroy(rt); Object.Destroy(readback);
            }
        }

        static IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 look, float fov)
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
            Debug.Log($"[HandDrums] shot {name}");
        }
    }
}
