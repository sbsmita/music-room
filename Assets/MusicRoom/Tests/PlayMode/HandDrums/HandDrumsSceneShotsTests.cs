// Phase B pictures of the hand drums in the real app (Category Graphics, `Tools/unity.sh test-play --gpu`): the whole room,
// every module, a simulated headset, manual frames. At the seat in a real set (the piano and the harp with them), the
// heads close with a hand print of a lesson on the conga, on display by the window (and the room around them), a glide
// from the display to the seat mid-way, and the menu's portrait of them. Written to $MUSICROOM_SHOTS, else
// $MUSICROOM_SHOTS_ROOT/handdrums/phase-b, else Builds/Shots/handdrums/phase-b.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.HandDrums;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using MusicRoom.Menu;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class HandDrumsSceneShotsTests
    {
        const int Width = 1280, Height = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;

        static string OutDir()
        {
            string env = Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            string root = ShotDirs.FromRoot("handdrums");
            string dir = !string.IsNullOrEmpty(env) ? env : root != null ? Path.Combine(root, "phase-b")
                : Path.Combine(ShotDirs.Default("handdrums"), "phase-b");
            Directory.CreateDirectory(dir);
            return dir;
        }

        IEnumerator Start(string set)
        {
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "all");
            _boot.Settings.SetString(AppSettings.KeySet, set);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Modules.OfType<LayoutModule>().First();
            RoomModule room = _app.Modules.OfType<RoomModule>().FirstOrDefault();
            float t0 = Time.realtimeSinceStartup;
            while ((_app.Rig.State == XrState.Starting || !_layout.FirstArrangeDone || (room != null && (room.TexturesPending > 0 || !room.ProbeRendered)))
                   && Time.realtimeSinceStartup - t0 < 300f)
            {
                _boot.Step(Dt);
                yield return null;
            }
            yield return Frames(30);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            GC.Collect();
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; } }

        sealed class Rig3 : IDisposable
        {
            public readonly Camera Cam;
            public readonly RenderTexture Rt;
            public readonly Texture2D Read;
            readonly GameObject _go;
            public Rig3(App app)
            {
                _go = new GameObject("HandDrumsShotsCamera");
                Cam = _go.AddComponent<Camera>();
                Cam.enabled = false;
                Cam.clearFlags = CameraClearFlags.SolidColor;
                Cam.backgroundColor = XrMode.VrBackground;
                Cam.nearClipPlane = 0.02f;
                Cam.farClipPlane = 40f;
                Cam.allowHDR = false;
                Cam.allowMSAA = true;
                Cam.cullingMask = app.Rig.HeadCamera.cullingMask;
                Rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                Read = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            }
            public void Dispose() { Object.Destroy(_go); Rt.Release(); Object.Destroy(Rt); Object.Destroy(Read); }
        }

        static IEnumerator Shot(Rig3 r, string name, Vector3 eye, Vector3 look, float fov)
        {
            r.Cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            r.Cam.fieldOfView = fov;
            r.Cam.aspect = Width / (float)Height;
            r.Cam.targetTexture = r.Rt;
            yield return null;
            r.Cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = r.Rt;
            r.Read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            r.Read.Apply();
            RenderTexture.active = prev;
            r.Cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(OutDir(), name + ".png"), r.Read.EncodeToPNG());
            Debug.Log($"[HandDrumsShots] shot {name}");
        }

        HandDrumsInstrument Drums => HandDrumsFixture.Find(_app);

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AtTheSeatInASetWithALessonPrint()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            yield return Start("piano,harp,handdrums");
            Debug.Log($"[HandDrumsShots] set {string.Join(",", _layout.MySet)}; hand drums {_layout.PlaceOf(InstrumentKind.HandDrums)} slot {_layout.SlotOf(InstrumentKind.HandDrums)}");
            if (_layout.PlaceOf(InstrumentKind.HandDrums) != InstrumentPlace.Seat)
            {
                _layout.Bring(InstrumentKind.HandDrums, BringReason.Menu);
                for (int i = 0; i < 200 && _layout.AnyMoving; i++) yield return Frames(1);
            }
            Assert.That(_layout.PlaceOf(InstrumentKind.HandDrums), Is.EqualTo(InstrumentPlace.Seat));
            Debug.Log($"[HandDrumsShots] set now {string.Join(",", _layout.MySet)}; hand drums slot {_layout.SlotOf(InstrumentKind.HandDrums)}");
            HandDrumsInstrument d = Drums;
            Transform t = d.transform;
            Vector3 eye = _app.Head.position, mid = t.TransformPoint(new Vector3(0f, 0.72f, 0f));
            using var r = new Rig3(_app);
            yield return Shot(r, "seat_set", eye, mid, 80f);
            Vector3 fwd = _app.Head.forward; fwd.y = 0f; fwd.Normalize();
            yield return Shot(r, "seat_set_ahead", eye, eye + fwd - Vector3.up * 0.35f, 90f);
            yield return Shot(r, "set_overview", eye + Vector3.up * 0.85f - fwd * 1.2f, eye + fwd * 0.45f - Vector3.up * 0.85f, 80f);
            // the heads close from the seat, then a lesson's hand print on the conga (the bass print on the tumba as well)
            yield return Shot(r, "seat_heads", eye, t.TransformPoint(new Vector3(0f, 0.74f, 0.02f)), 42f);
            d.ShowTarget(new LessonTarget { kind = "conga", zone = "open", label = "Open" }, 1f);
            d.ShowTarget(new LessonTarget { kind = "tumba", zone = "bass", label = "Bass" }, 0.8f);
            yield return Frames(1);
            yield return Shot(r, "seat_lesson_print", eye, t.TransformPoint(new Vector3(0f, 0.74f, 0.02f)), 42f);
            d.Cue.HideAll();
            // a slap mid-dent on the conga, close (the hand not drawn: its pose plays the logic)
            d.Model.Strike(0, MusicRoom.Audio.Burst.HandPart.Tips, 1.0, MusicRoom.Audio.Burst.PatchShape.Points,
                new MusicRoom.Audio.Burst.PatchPoint { Rho = 0.6f, Theta = -0.3f }, new MusicRoom.Audio.Burst.PatchPoint { Rho = 0.62f, Theta = 0f },
                new MusicRoom.Audio.Burst.PatchPoint { Rho = 0.6f, Theta = 0.3f }, 3, _app.Clock.Now);
            d.Model.Update(_app.Clock.Now + 0.02);
            Vector3 conga = t.TransformPoint(HandDrumsDims.HeadPoint(0, 0.2f, 0f));
            yield return Shot(r, "seat_conga_dent", conga + t.TransformDirection(new Vector3(-0.05f, 0.30f, -0.36f)), conga, 40f);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator OnDisplayAGlideToTheSeatAndTheMenuPortrait()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            yield return Start("piano,drums,harp");
            Assert.That(_layout.PlaceOf(InstrumentKind.HandDrums), Is.EqualTo(InstrumentPlace.Display));
            HandDrumsInstrument d = Drums;
            Assert.That(d.Stowed, Is.True);
            InstrumentDescriptor desc = _app.Registry.Get(InstrumentKind.HandDrums);
            Vector3 spot = RoomFrame.ToWorld(new Vector3(desc.Stow.Pose.x, 0.55f, desc.Stow.Pose.z));   // the room faces the fire (RoomFrame): room-frame views turned
            using var r = new Rig3(_app);
            yield return Shot(r, "display", RoomFrame.ToWorld(new Vector3(0.9f, 1.55f, -1.2f)), spot, 55f);
            yield return Shot(r, "display_close", spot + RoomFrame.DirToWorld(new Vector3(-1.05f, 0.55f, 0.35f)), spot + Vector3.down * 0.05f, 50f);
            yield return Shot(r, "display_window_wall", RoomFrame.ToWorld(new Vector3(-0.6f, 1.6f, -1.4f)), RoomFrame.ToWorld(new Vector3(2.8f, 0.6f, -1.3f)), 70f);
            Vector3 eye = _app.Head.position;
            yield return Shot(r, "display_from_seat", eye, spot, 70f);

            // the glide to the seat, mid-way
            Assert.IsTrue(_layout.Bring(InstrumentKind.HandDrums, BringReason.Menu));
            RigGlide g = _layout.GlideOf(InstrumentKind.HandDrums);
            Assert.That(g, Is.Not.Null);
            int half = Mathf.RoundToInt((g.Delay + g.Duration * 0.5f) / Dt);
            yield return Frames(half);
            Transform t = d.transform;
            Debug.Log($"[HandDrumsShots] glide mid-way: at {t.position.ToString("F2")}, {g.Length:F2} m in {g.Duration:F2} s, stowed {d.Stowed}");
            yield return Shot(r, "glide_midway", RoomFrame.ToWorld(new Vector3(0.6f, 1.9f, -2.7f)), t.position + Vector3.up * 0.5f, 75f);
            for (int i = 0; i < 200 && _layout.AnyMoving; i++) yield return Frames(1);
            Assert.That(_layout.PlaceOf(InstrumentKind.HandDrums), Is.EqualTo(InstrumentPlace.Seat));
            Debug.Log($"[HandDrumsShots] arrived: set {string.Join(",", _layout.MySet)}, slot {_layout.SlotOf(InstrumentKind.HandDrums)}");
            yield return Shot(r, "arrived_seat", eye, t.TransformPoint(new Vector3(0f, 0.72f, 0f)), 80f);

            // the menu's portrait of the drums (and the grid around it)
            var m = (MenuModule)_app.Get<IMenuPanel>();
            for (int i = 0; i < 3 && m.Portraits == null; i++) yield return Frames(1);
            PortraitStudio studio = m.Portraits;
            Assert.That(studio, Is.Not.Null);
            m.Show();
            m.Go(MenuScreen.Instruments);
            float t0 = Time.realtimeSinceStartup;
            while (studio.AnyPending && Time.realtimeSinceStartup - t0 < 8f) yield return Frames(1);
            yield return Frames(6);
            int cell = PortraitStudio.CellOf(_app.Registry, InstrumentKind.HandDrums);
            Assert.That(studio.StateOf(cell), Is.EqualTo(PortraitStudio.CellState.Ready), "the drums' portrait rendered");
            var prev = RenderTexture.active;
            RenderTexture.active = studio.Atlas;
            var atlas = new Texture2D(studio.Atlas.width, studio.Atlas.height, TextureFormat.RGBA32, false, !studio.Atlas.sRGB);
            atlas.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
            atlas.Apply();
            RenderTexture.active = prev;
            RectInt band = PortraitStudio.CellPixels(cell);
            PortraitTests.Measure(atlas.GetPixels32(), atlas.width, band, MenuStyle.PhotoPaper, out float coverage, out float fill, out float off, out float lum);
            var cellTex = new Texture2D(band.width, band.height, TextureFormat.RGBA32, false);
            cellTex.SetPixels(atlas.GetPixels(band.x, band.y, band.width, band.height));
            cellTex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir(), "menu_portrait.png"), cellTex.EncodeToPNG());
            Debug.Log($"[HandDrumsShots] portrait: fill {fill:P0} coverage {coverage:P0} off-centre {off:P0} luminance {lum:F2}");
            Object.Destroy(cellTex);
            Object.Destroy(atlas);
            Transform panel = m.Root != null ? m.Root : null;
            if (panel != null) yield return Shot(r, "menu_grid", eye, panel.position, 60f);
            Assert.That(coverage, Is.GreaterThan(0.02f), "not blank");
            Assert.That(fill, Is.InRange(0.40f, 0.85f), "the drums fill 40-85 % of their portrait");
            Assert.That(off, Is.LessThan(0.08f), "centred");
            Assert.That(lum, Is.InRange(0.25f, 0.75f), "lit");
        }
    }
}
