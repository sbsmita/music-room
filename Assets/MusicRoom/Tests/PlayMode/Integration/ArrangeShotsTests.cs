// Screenshots of the features added after the first headset test (Category Graphics, `Tools/unity.sh test-play --gpu`):
// every real module, a simulated headset, manual frames, scripted hands. The start panel at launch; "Move instruments"
// with the highlighted instruments and the Done / Reset buttons; a hand's ray landing on an instrument; an instrument
// held (gold) and the piano dragged with the hand rising (since 2026-11-11 the hand lifts it, its stand stretched to the floor); the Exit row asking "Tap again to exit". Written to
// Builds/Shots/arrange (or $MUSICROOM_SHOTS_ROOT/arrange). Also checks the head camera's draws in
// arrange mode stay inside the whole-scene budget (150).
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.Piano;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Integration
{
    [Category("Graphics")]
    public class ArrangeShotsTests
    {
        const int Width = 1280, Height = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        MenuModule _menu;
        LayoutModule _layout;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, startPanel: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);                        // no tour in the way of the views
            _boot.Hands.SetPose(HandSide.Right, Finger(new Vector3(0.3f, 1.6f, -0.6f), Vector3.forward));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            foreach (var m in _app.Modules)
            {
                if (m is MenuModule mm) _menu = mm;
                if (m is LayoutModule lm) _layout = lm;
            }
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
            string dir = ShotDirs.FromRoot("arrange") ?? ShotDirs.Default("arrange");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static HandPose Finger(Vector3 tip, Vector3 along) =>
            HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(along, Vector3.up)).WithPointing().WithIndexTipAt(tip);

        static Vector3 OnRow(Transform root, MenuRow r, float z, float scale = 1f) =>
            root.TransformPoint(scale * MenuMath.CanvasPoint((r.X0 + r.X1) / 2f, (r.Y0 + r.Y1) / 2f, z / scale));

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; }
        }

        IEnumerator Poke(Transform root, MenuRow row, float scale = 1f)
        {
            yield return Frames(20);
            foreach (float z in new[] { 0.03f, 0.012f, -0.002f, 0.001f, 0.03f })
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(OnRow(root, row, z, scale), root.forward));
                yield return Frames(1);
            }
            _boot.Hands.SetPose(HandSide.Right, Finger(new Vector3(0.3f, 1.6f, -0.6f), Vector3.forward));
            yield return Frames(4);
        }

        static MenuRow Find(MenuController c, RowKind id)
        {
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == id) return c.Row(i);
            Assert.Fail("no " + id + " row");
            return null;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator StartPanelArrangeModeRayButtonsAndExit()
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
            yield return Frames(30);   // the start panel opens and paints, the fire runs a little
            Assert.IsTrue(_menu.StartShowing, "the start panel at launch");

            string dir = OutDir();
            var camGo = new GameObject("ArrangeShotsCamera");
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
                Transform head = _app.Head;
                Vector3 eye = head.position;
                Vector3 fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);

                // 1. The start panel, from the seat.
                yield return Shot(cam, rt, readback, dir, "start_panel", eye, _menu.Root.position, 70f);
                yield return Poke(_menu.Root, Find(_menu.Controller, RowKind.PlayHere));
                Assert.IsFalse(_menu.Shown, "Play here closes it");

                // 2. Move instruments from the palm menu: the highlighted room and the Done / Reset buttons.
                _menu.Show();
                yield return Frames(20);
                yield return Shot(cam, rt, readback, dir, "menu_home", eye, _menu.Root.position, 70f);
                yield return Poke(_menu.Root, Find(_menu.Controller, RowKind.Arrange));
                Assert.IsTrue(_layout.Arranging);
                yield return Frames(20);
                yield return Shot(cam, rt, readback, dir, "arrange_seat", eye, eye + fwd - Vector3.up * 0.45f, 80f);
                yield return Shot(cam, rt, readback, dir, "arrange_buttons", eye, _menu.ArrangeRoot.position, 45f);
                yield return Shot(cam, rt, readback, dir, "arrange_overview", eye + Vector3.up * 1.1f - fwd * 1.4f, eye + fwd * 0.6f - Vector3.up * 1.0f, 75f);
                long draws = -1;
                yield return HeadDraws(v => draws = v);
                Debug.Log($"[ArrangeShots] head camera draws in arrange mode = {draws} (budget 150)");
                if (draws >= 0) Assert.That(draws, Is.LessThanOrEqualTo(150));

                // 3. A ray from the right hand's pointer pose landing on the harp (gold), seen from behind the shoulder.
                Rig harp = _app.Get<IRigRegistry>().Get(RigId.Harp);
                Vector3 harpAt = harp.Group.position + Vector3.up * 0.8f;
                Vector3 hand = eye - right * 0.12f - Vector3.up * 0.3f + fwd * 0.15f;
                _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(harpAt - hand)).WithIndexTipAt(hand));
                _boot.Hands.SetPointer(HandSide.Right, new Pose(hand, Quaternion.LookRotation(harpAt - hand)));
                yield return Frames(4);
                Debug.Log($"[ArrangeShots] ray target {_layout.RayTarget(1)}");
                Assert.That(_layout.RayTarget(1), Is.Not.Null, "the ray lands on an instrument");
                yield return Shot(cam, rt, readback, dir, "arrange_ray", eye + Vector3.up * 0.25f - fwd * 0.45f + right * 0.25f, (hand + harpAt) * 0.5f, 75f);

                // 4. The kit held with a pinch by its snare and dragged a little: gold brackets, the floor ring.
                _boot.Hands.SetPointer(HandSide.Right, null);
                Rig kit = _app.Get<IRigRegistry>().Get(RigId.Drums);
                IDrumsApi drums = _app.Get<IDrumsApi>();
                Vector3 snare = drums.Pieces[0].Transform.position + Vector3.up * 0.04f;
                HandPose pinch = HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.identity).WithIndexTipAt(snare).WithPinch(0f);
                for (int i = 0; i < 8; i++) { _boot.Hands.SetPose(HandSide.Right, pinch); yield return Frames(1); }
                Assert.That(_layout.HeldRig, Is.SameAs(kit), "the kit, held by its snare");
                for (int i = 1; i <= 10; i++) { _boot.Hands.SetPose(HandSide.Right, pinch.Translated(fwd * (0.1f * i / 10f))); yield return Frames(1); }
                yield return Frames(8);
                Bounds kitBox = Box(kit.Group);
                yield return Shot(cam, rt, readback, dir, "arrange_held", eye + Vector3.up * 0.25f - right * 0.55f + fwd * 0.1f, kitBox.center, 70f);
                _boot.Hands.SetPose(HandSide.Right, Finger(snare + Vector3.up * 0.3f, Vector3.forward));
                yield return Frames(30);   // the release grace and the settle

                // 5. The piano dragged 15 cm away with the hand rising 15 cm: lifted 13 cm (the 2 cm dead band, then 1:1), its
                // stand stretched to the floor.
                PianoInstrument piano = null;
                foreach (var m in _app.Modules) if (m is PianoModule pm) piano = pm.Piano;
                piano.TryGetKey(64, out var key);
                Vector3 at = piano.transform.TransformPoint(new Vector3(key.X, 0.04f, 0.06f));
                pinch = HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.identity).WithIndexTipAt(at).WithPinch(0f);
                for (int i = 0; i < 8; i++) { _boot.Hands.SetPose(HandSide.Right, pinch); yield return Frames(1); }
                float pianoY = piano.transform.position.y;
                for (int i = 1; i <= 15; i++) { _boot.Hands.SetPose(HandSide.Right, pinch.Translated((Vector3.up + fwd) * (0.15f * i / 15f))); yield return Frames(1); }
                yield return Frames(16);
                Assert.That(piano.transform.position.y, Is.EqualTo(pianoY + 0.15f - MoveConstants.LiftDeadband).Within(0.006f), "lifted with the hand");
                Assert.That(piano.StandScale, Is.GreaterThan(1.1f), "the stand stretched to the floor");
                pianoY = piano.transform.position.y;
                _boot.Hands.SetPose(HandSide.Right, Finger(new Vector3(0.3f, 1.6f, -0.6f), Vector3.forward));
                yield return Frames(30);   // the release grace and the settle

                // 6. Done; the piano stays where it was put, on its stand (seen from behind the seat).
                yield return Poke(_menu.ArrangeRoot, Find(_menu.ArrangeButtons, RowKind.Done), MenuStyle.ArrangeScale);
                Assert.IsFalse(_layout.Arranging);
                Assert.That(piano.transform.position.y, Is.EqualTo(pianoY).Within(1e-4f), "it stays where it was put");
                yield return Frames(15);   // the buttons fade out
                Transform pt = piano.transform;
                // From behind the seat, a little left and above the bench (a camera at the seat, 0.5 m up, saw only the bench
                // and the piano's underside; the kit stands to the right), so the stretched stand and its feet are in view.
                Vector3 front = pt.position - pt.forward * 1.6f - pt.right * 0.3f;
                front.y = 1.15f;
                yield return Shot(cam, rt, readback, dir, "arrange_piano_moved", front, pt.position - Vector3.up * 0.38f, 62f);

                // 7. The Exit row asking for its second poke.
                _menu.Show();
                yield return Frames(20);
                yield return Poke(_menu.Root, Find(_menu.Controller, RowKind.Exit));
                yield return Frames(10);
                Assert.IsTrue(_menu.Controller.ExitArmed);
                yield return Shot(cam, rt, readback, dir, "exit_confirm", eye, _menu.Root.position, 70f);
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        static Bounds Box(Transform group)
        {
            Bounds b = new Bounds(group.position, Vector3.zero);
            foreach (var r in group.GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
            return b;
        }

        IEnumerator HeadDraws(Action<long> result)
        {
            var cam = _app.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "ArrangeDraws" };
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
            Debug.Log($"[ArrangeShots] shot {name}");
        }
    }
}
