// GPU look check of the handpan on the platform (handpan.md 7; Category Graphics: Tools/unity.sh test-play --gpu). Phase B:
// the real module in the whole app (a simulated headset, manual frames), placed by Layout. Pictures go to
// $MUSICROOM_SHOTS_ROOT/handpan/phase-b, else Builds/Shots/handpan/phase-b:
//   display-*        on its wall bracket on the left wall (the default room: the handpan is not in the default set), from
//                    the room, close, from the side, and from the seat looking left; the decor around the spot
//   portrait-*       its menu-grid portrait (rendered where it is: on display), the Instruments grid canvas and seated view
//   glide-midway*    piano + harp + handpan: half-way from the wall to the seat (the shell easing down onto its stand)
//   set-*            at the seat in that set: from the eyes, wide, three-quarter, behind, the side; a finger on A3
// It also counts the draws (3 at the seat, 2 on display), logs the display's bounds (the descriptor's box) and checks the
// slap settle of the real instrument.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Handpan;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Handpan
{
    [Category("Graphics")]
    public class HandpanShotsTests
    {
        const int Width = 1600, Height = 1000;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        HandpanInstrument _hp;

        public static string OutDir()
        {
            string dir = ShotDirs.FromRoot("handpan");
            dir = dir != null ? Path.Combine(dir, "phase-b")
                : Path.Combine(ShotDirs.Default("handpan"), "phase-b");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            RoomModule room = null;
            foreach (var m in _app.Modules)
            {
                if (m is LayoutModule lm) _layout = lm;
                if (m is HandpanModule hm) _hp = hm.Instrument;
                if (m is RoomModule rm) room = rm;
            }
            Assert.That(_hp, Is.Not.Null, "the handpan module is loaded");
            float t0 = Time.realtimeSinceStartup;
            while ((_app.Rig.State == XrState.Starting || !_layout.FirstArrangeDone || (room != null && (room.TexturesPending > 0 || !room.ProbeRendered))) && Time.realtimeSinceStartup - t0 < 300f)
            {
                _boot.Step(Dt);
                yield return null;
            }
            Assert.IsTrue(_layout.FirstArrangeDone, "Layout arranged the room");
            yield return Frames(20);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _app = null; _layout = null; _hp = null;
            GC.Collect();
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; }
        }

        Vector3 SeatPoint(float x, float y, float z) => _layout.Seat.Place(new Vector3(x, y, z));

        void Shoot(string name, Vector3 eye, Vector3 target, float fov)
        {
            var go = new GameObject("HandpanShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.cullingMask = _app.Rig.HeadCamera.cullingMask;
            cam.aspect = Width / (float)Height;
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(OutDir(), name + ".png"), tex.EncodeToPNG());
            Debug.Log($"[Handpan] shot {name}");
            Object.Destroy(rt);
            Object.Destroy(go);
            Object.Destroy(tex);
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

        static void Save(Texture2D t, string name) => File.WriteAllBytes(Path.Combine(OutDir(), name + ".png"), t.EncodeToPNG());

        int Draws()
        {
            int n = 0;
            foreach (var r in _hp.Root.GetComponentsInChildren<MeshRenderer>()) if (r.enabled && r.gameObject.activeInHierarchy) n += r.sharedMaterials.Length;
            return n;
        }

        HandPose PointingHand(HandSide side, Vector3 indexTip, float downDeg)
        {
            Vector3 fwd = _layout.Seat.Forward;
            Vector3 dir = Quaternion.AngleAxis(downDeg, Vector3.Cross(Vector3.up, fwd).normalized) * fwd;
            return HandPose.Open(side, Vector3.zero, Quaternion.LookRotation(dir, Vector3.up)).WithPointing().WithIndexTipAt(indexTip);
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator OnDisplayInTheGridGlidingAndAtTheSeatInASet()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            InstrumentDescriptor d = _app.Registry.Get(InstrumentKind.Handpan);

            // 1. On display: the default room (the handpan is not in the default set)
            Assert.That(_layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display), "on display at boot");
            Assert.IsTrue(_hp.Stowed);
            Assert.That(_hp.Model.Current, Is.EqualTo(HandpanModel.Config.Wall));
            Assert.That(Draws(), Is.EqualTo(d.DrawsStowed), "on display: shell + band with its bracket");
            Transform g = _hp.Root;
            Debug.Log($"[Handpan] display: rig at {g.position.ToString("F3")} yaw {g.eulerAngles.y:F1}, {_hp.Model.DisplayTriangles} triangles");
            var all = new Bounds();
            bool first = true;
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled) continue;
                if (first) { all = r.bounds; first = false; } else all.Encapsulate(r.bounds);
            }
            Debug.Log($"[Handpan] display world bounds x {all.min.x:F3}..{all.max.x:F3} y {all.min.y:F3}..{all.max.y:F3} z {all.min.z:F3}..{all.max.z:F3} (the left wall at x -3.2)");
            Assert.That(all.min.x, Is.GreaterThan(-3.2f), "nothing through the left wall");
            Vector3 spot = _hp.Model.Instrument.position;
            Shoot("display-room", new Vector3(-0.9f, 1.55f, -0.4f), spot, 55f);
            Shoot("display-close", spot + new Vector3(0.85f, 0.10f, 0.25f), spot, 45f);
            Shoot("display-wall-wide", new Vector3(0.6f, 1.6f, -0.9f), new Vector3(-2.8f, 1.1f, -0.6f), 80f);
            Shoot("display-from-seat", _app.Head.position, spot, 70f);
            Shoot("display-side", spot + new Vector3(0.55f, -0.05f, 0.95f), spot, 45f);

            // 2. The menu's Instruments grid: the portrait is rendered where the instrument is (on display)
            var menu = (MenuModule)_app.Get<IMenuPanel>();
            for (int i = 0; i < 3 && menu.Portraits == null; i++) yield return Frames(1);
            PortraitStudio studio = menu.Portraits;
            if (studio != null && studio.Available)
            {
                menu.Show();
                menu.Go(MenuScreen.Instruments);
                float t0 = Time.realtimeSinceStartup;
                while (studio.AnyPending && Time.realtimeSinceStartup - t0 < 8f) yield return Frames(1);
                yield return Frames(30);
                int cell = PortraitStudio.CellOf(_app.Registry, InstrumentKind.Handpan);
                Assert.That(studio.StateOf(cell), Is.EqualTo(PortraitStudio.CellState.Ready), "the handpan's portrait rendered");
                Texture2D atlas = Read(studio.Atlas);
                RectInt band = PortraitStudio.CellPixels(cell);
                PortraitTests.Measure(atlas.GetPixels32(), atlas.width, band, MenuStyle.PhotoPaper, out float coverage, out float fill, out float off, out float lum);
                Debug.Log($"[Handpan] portrait: fill {fill:P0} coverage {coverage:P0} off-centre {off:P0} lum {lum:F2}");
                var cellTex = new Texture2D(band.width, band.height, TextureFormat.RGBA32, false);
                cellTex.SetPixels(atlas.GetPixels(band.x, band.y, band.width, band.height));
                cellTex.Apply();
                Save(cellTex, "portrait-handpan");
                Object.Destroy(cellTex);
                Object.Destroy(atlas);
                Assert.That(fill, Is.InRange(0.40f, 0.85f), "the handpan fills 40-85 % of its portrait");
                Assert.That(lum, Is.InRange(0.25f, 0.75f), "mean luminance");
                menu.Redraw();
                yield return Frames(3);
                Texture2D canvas = Read(menu.FaceTexture);
                Save(canvas, "portrait-menu-grid-canvas");
                Object.Destroy(canvas);
                Texture2D view = MenuScreenshotTests.SeatedView(_app.Rig.HeadCamera, 1280, 960);
                Save(view, "portrait-menu-grid-seated");
                Object.Destroy(view);
                menu.Hide();
                yield return Frames(30);
            }
            else Debug.Log("[Handpan] portrait: no studio (headless or render requests off)");

            // 3. Piano + harp + handpan: the glide from the wall to the seat
            var set = new[] { InstrumentKind.Piano, InstrumentKind.Harp, InstrumentKind.Handpan };
            bool fits = _layout.TrySetSet(set);
            Debug.Log($"[Handpan] set piano + harp + handpan: {(fits ? "fits" : "does not fit; bringing the handpan alone")}");
            if (!fits) Assert.IsTrue(_layout.Bring(InstrumentKind.Handpan, BringReason.Menu), "Bring");
            RigGlide glide = _layout.GlideOf(InstrumentKind.Handpan);
            int waited = 0;
            while (glide == null && waited < 30) { yield return Frames(1); waited++; glide = _layout.GlideOf(InstrumentKind.Handpan); }
            Assert.That(glide, Is.Not.Null, "the handpan glides");
            Debug.Log($"[Handpan] glide {glide.Length:F2} m in {glide.Duration:F2} s (delay {glide.Delay:F2})");
            yield return Frames(Mathf.Max(1, Mathf.RoundToInt((glide.Delay + glide.Duration * 0.5f) / Dt) - waited));
            Assert.IsTrue(_hp.Model.Blending, "mid-glide the shell eases toward the stand");
            Vector3 mid = _hp.Model.Instrument.position;
            Debug.Log($"[Handpan] mid-glide: shell at {mid.ToString("F2")}, stand {(_hp.Model.Stand.enabled ? "shown" : "hidden")}");
            Shoot("glide-midway", RoomFrame.ToWorld(new Vector3(0.9f, 1.9f, -2.4f)), mid, 70f);   // room-frame views, turned (RoomFrame)
            Shoot("glide-midway-close", mid + (RoomFrame.ToWorld(new Vector3(0.9f, 1.6f, -1.8f)) - mid).normalized * 1.3f, mid, 50f);
            for (int i = 0; i < 200 && _layout.AnyMoving; i++) yield return Frames(1);
            yield return Frames(20);

            // 4. At the seat in the set
            Assert.That(_layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Seat));
            Assert.IsTrue(_hp.IsPresent);
            Assert.That(_hp.Model.Current, Is.EqualTo(HandpanModel.Config.Seat));
            Assert.IsFalse(_hp.Model.Blending);
            Assert.That(Draws(), Is.EqualTo(d.DrawsAtSeat), "at the seat: shell, band, stand");
            Debug.Log($"[Handpan] at the seat: slot {_layout.SlotOf(InstrumentKind.Handpan)}, rig at {g.position.ToString("F3")} yaw {g.eulerAngles.y:F1}; set {string.Join(", ", _layout.MySet)}; {_hp.Model.Triangles} triangles");
            Vector3 eye = _app.Head.position;
            Vector3 ding = _hp.FieldCentre(0);
            Shoot("set-seat", eye, ding + new Vector3(0, -0.05f, 0f), 75f);
            Shoot("set-seat-wide", eye, eye + _layout.Seat.Forward * 1f - Vector3.up * 0.5f, 95f);
            Shoot("set-three-quarter", SeatPoint(1.2f, 1.55f, 1.0f), SeatPoint(0f, 0.6f, 0.2f), 65f);
            Shoot("set-behind", SeatPoint(0.35f, 1.75f, -1.1f), SeatPoint(0f, 0.6f, 0.45f), 75f);
            Shoot("set-handpan-side", g.TransformPoint(new Vector3(1.1f, 0.85f, 0.1f)), g.TransformPoint(new Vector3(0f, 0.5f, 0.05f)), 50f);

            // a fingertip resting in A3's dimple: stopped on the steel, with its contact shadow
            Vector3 a3 = _hp.FieldCentre(1), n3 = _hp.FieldNormal(1);
            _boot.Hands.SetTracked(HandSide.Right, true);
            for (float h = 0.03f; h >= -0.006f; h -= 0.004f) { _boot.Hands.SetPose(HandSide.Right, PointingHand(HandSide.Right, a3 + n3 * h, 50f)); yield return Frames(1); }
            yield return Frames(6);
            Shoot("set-finger-on-a3", eye, a3, 50f);
            _boot.Hands.SetTracked(HandSide.Right, false);
            yield return Frames(30);

            // the settle: a slap dips the shell 1-2 mm on its felt
            _hp.Model.Struck(1.0, HandpanTech.Slap);
            float maxDip = 0f;
            for (int i = 0; i < 30; i++) { _hp.Model.Tick(Dt); maxDip = Mathf.Max(maxDip, (float)_hp.Model.Motion.Offset); }
            Debug.Log($"[Handpan] slap settle: peak dip {maxDip * 1000f:F2} mm");
            Assert.That(maxDip, Is.InRange(0.0008f, 0.002f), "a palm slap dips the shell 1-2 mm");
        }
    }
}
