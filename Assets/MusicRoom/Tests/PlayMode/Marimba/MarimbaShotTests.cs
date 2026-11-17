// The marimba in the real app (Category Graphics: Tools/unity.sh test-play --gpu): every module, a simulated headset, manual
// frames. The marimba on display under the window (the default set is piano, drums, harp), mid-glide when it is brought
// to the seat, at the seat in a real set (it takes Front; the drums and the harp keep theirs), a played bar, and its
// menu portrait in the Instruments grid. Pictures: $MUSICROOM_SHOTS, else $MUSICROOM_SHOTS_ROOT/marimba/phase-b, else
// Builds/Shots/marimba/phase-b.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Marimba;
using MusicRoom.Menu;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Marimba
{
    [Category("Graphics")]
    public class MarimbaShotTests
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
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
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
            string root = ShotDirs.FromRoot("marimba");
            string dir = !string.IsNullOrEmpty(env) ? env : root != null ? Path.Combine(root, "phase-b") : Path.Combine(ShotDirs.Default("marimba"), "phase-b");
            Directory.CreateDirectory(dir);
            return dir;
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheMarimbaInTheRoom()
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
            MarimbaInstrument mar = null;
            foreach (IInstrument i in _app.Instruments) if (i is MarimbaInstrument mi) mar = mi;
            Assert.That(mar, Is.Not.Null);
            Assert.That(_layout.PlaceOf(InstrumentKind.Marimba), Is.EqualTo(InstrumentPlace.Display), "on display at first");
            Assert.That(mar.Stowed, Is.True);
            Assert.That(mar.View.DrawCalls, Is.LessThanOrEqualTo(3), "3 draws on display");

            string dir = OutDir();
            var camGo = new GameObject("MarimbaShotsCamera");
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
                Vector3 disp = mar.transform.position;
                Debug.Log($"[MarimbaShots] on display at {disp.ToString("F2")} yaw {mar.transform.eulerAngles.y:F0}");

                // 1. On display under the window: from the seat (the player turns right), and close.
                yield return Shot(cam, rt, readback, dir, "display_from_seat", eye, eye + Quaternion.Euler(0f, 112f, 0f) * fwd - Vector3.up * 0.35f, 85f);
                yield return Shot(cam, rt, readback, dir, "display_close", disp + RoomFrame.DirToWorld(new Vector3(-1.5f, 1.25f, -1.1f)), disp + Vector3.up * 0.45f, 55f);
                yield return Shot(cam, rt, readback, dir, "display_window_wall", RoomFrame.ToWorld(new Vector3(-0.6f, 1.6f, -1.4f)), RoomFrame.ToWorld(new Vector3(2.8f, 0.6f, -1.3f)), 70f);   // room-frame views, turned (RoomFrame)

                // 2. Brought to the seat (the menu's grid) beside the harp: the piano and the kit go back first (the menu
                //    says No room for the marimba in the default room: the piano wants the front, the kit's cymbals stand in
                //    its hands' field). Then the marimba's glide, mid-way.
                Assert.That(_layout.PreviewJoin(InstrumentKind.Marimba).Action, Is.EqualTo(JoinAction.Alone));
                Assert.IsTrue(_layout.PutAway(InstrumentKind.Piano));
                Assert.IsTrue(_layout.PutAway(InstrumentKind.Drums));
                for (int i = 0; i < 300 && _layout.AnyMoving; i++) yield return Frames(1);
                yield return Frames(5);
                Assert.IsTrue(_layout.Bring(InstrumentKind.Marimba, BringReason.Menu));
                RigGlide g = _layout.GlideOf(InstrumentKind.Marimba);
                Assert.That(g, Is.Not.Null);
                int half = Mathf.RoundToInt((g.Delay + g.Duration * 0.5f) / Dt);
                yield return Frames(half);
                Debug.Log($"[MarimbaShots] glide mid-way: marimba at {mar.transform.position.ToString("F2")}, {g.Length:F2} m in {g.Duration:F2} s; present {mar.IsPresent}");
                yield return Shot(cam, rt, readback, dir, "glide_midway", RoomFrame.ToWorld(new Vector3(0.9f, 1.9f, -2.6f)), mar.transform.position + Vector3.up * 0.4f, 80f);
                for (int i = 0; i < 200 && _layout.AnyMoving; i++) yield return Frames(1);
                yield return Frames(10);

                // 3. At the seat in a real set.
                string set = string.Join(", ", _layout.MySet);
                Debug.Log($"[MarimbaShots] my set: {set}; marimba {_layout.PlaceOf(InstrumentKind.Marimba)} slot {_layout.SlotOf(InstrumentKind.Marimba)}; present {mar.IsPresent}; draws {mar.View.DrawCalls}");
                Assert.That(mar.IsPresent, Is.True);
                Assert.That(mar.View.DrawCalls, Is.LessThanOrEqualTo(5), "5 draws at the seat");
                eye = _app.Head.position;
                yield return Shot(cam, rt, readback, dir, "seat_view", eye, eye + fwd - Vector3.up * 0.55f, 80f);
                yield return Shot(cam, rt, readback, dir, "seat_view_wide", eye, eye + fwd - Vector3.up * 0.35f, 100f);
                yield return Shot(cam, rt, readback, dir, "set_overview", eye + Vector3.up * 0.9f - fwd * 1.3f, eye + fwd * 0.4f - Vector3.up * 0.9f, 80f);
                Vector3 mp = mar.transform.position;
                yield return Shot(cam, rt, readback, dir, "set_three_quarter", mp + mar.transform.rotation * new Vector3(-1.3f, 1.1f, -1.0f), mp + Vector3.up * 0.55f + mar.transform.forward * 0.2f, 60f);

                // 4. A fingertip plays E4: the bar dips, the sound unit rings.
                _boot.Hands.SetTracked(HandSide.Right, true);
                mar.TryGetLanding(new LessonTarget { i = 16, midi = 64, note = "E4" }, out Pose land, out _, out _);
                Vector3 up = land.rotation * Vector3.up;
                _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(land.position + up * 0.05f));
                yield return Frames(3);
                _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(land.position - up * 0.004f));
                yield return Frames(1);
                Debug.Log($"[MarimbaShots] fingertip on E4: bar offset {mar.View.BarOffset(16) * 1000:F2} mm, live strikes {mar.Controller.LiveStrikes}");
                yield return Shot(cam, rt, readback, dir, "fingertip_on_e4", eye, land.position, 40f);
                _boot.Hands.SetTracked(HandSide.Right, false);
                yield return Frames(20);

                // 5. The menu's Instruments grid with the marimba's portrait.
                var menu = (MenuModule)_app.Get<IMenuPanel>();
                for (int i = 0; i < 3 && menu.Portraits == null; i++) yield return Frames(1);
                if (menu.Portraits != null && menu.Portraits.Available)
                {
                    menu.Show();
                    menu.Go(MenuScreen.Instruments);
                    t0 = Time.realtimeSinceStartup;
                    while (menu.Portraits.AnyPending && Time.realtimeSinceStartup - t0 < 8f) yield return Frames(1);
                    while (menu.AnimK < 1f && Time.realtimeSinceStartup - t0 < 10f) yield return Frames(1);
                    menu.Redraw();
                    yield return Frames(2);
                    var atlas = Read(menu.Portraits.Atlas);
                    RectInt band = PortraitStudio.CellPixels(PortraitStudio.CellOf(_app.Registry, InstrumentKind.Marimba));
                    var cell = new Texture2D(band.width, band.height, TextureFormat.RGBA32, false);
                    cell.SetPixels(atlas.GetPixels(band.x, band.y, band.width, band.height));
                    cell.Apply();
                    File.WriteAllBytes(Path.Combine(dir, "menu_portrait.png"), cell.EncodeToPNG());
                    PortraitTests.Measure(atlas.GetPixels32(), atlas.width, band, MenuStyle.PhotoPaper, out float coverage, out float fill, out float off, out float lum);
                    Debug.Log($"[MarimbaShots] portrait fill {fill:P0} coverage {coverage:P0} off-centre {off:P0} lum {lum:F2}");
                    Assert.That(fill, Is.InRange(0.40f, 0.85f), "the marimba fills 40-85 % of its portrait");
                    Assert.That(off, Is.LessThan(0.08f), "centred");
                    Assert.That(lum, Is.InRange(0.25f, 0.75f), "mean luminance");
                    var view = MenuScreenshotTests.SeatedView(_app.Rig.HeadCamera, 1280, 960);
                    File.WriteAllBytes(Path.Combine(dir, "menu_instruments_grid.png"), view.EncodeToPNG());
                    Object.Destroy(view); Object.Destroy(cell); Object.Destroy(atlas);
                    menu.Hide();
                }
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, !rt.sRGB);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
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
            Debug.Log($"[MarimbaShots] shot {name}");
        }
    }
}
