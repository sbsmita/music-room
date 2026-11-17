// Screenshots of the instruments opening in front of the player and of "Move instruments" (Category Graphics,
// `Tools/unity.sh test-play --gpu`): every real module, a simulated headset, manual frames, scripted hands. From the
// player's eyes after "Play here": the piano alone, a set of three (handpan, bowls, hand drums) and My set as it comes
// (piano, drums, harp); with Move instruments on, a hand near the piano (its outline lights) and the piano dragged (gold); and the
// drawn (Skin) hands over the piano in "Play in my room" (simulated passthrough). Written to Builds/Shots/placement (or
// $MUSICROOM_SHOTS_ROOT/placement).
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.MR;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Integration
{
    [Category("Graphics")]
    public class PlacementShotsTests
    {
        const int Width = 1280, Height = 960;
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
            MrDevOptions.Reset();
            MrDevOptions.SimulatePassthroughOverride = true;
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, startPanel: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
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
            if (_app != null)
            {
                var mr = _app.Get<IMixedReality>() as MrModule;
                float t0 = Time.realtimeSinceStartup;
                while (mr != null && mr.Loading && Time.realtimeSinceStartup - t0 < 20f) yield return null;
                if (mr != null && mr.Active) mr.Exit();
            }
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            MrDevOptions.Reset();
            GC.Collect();
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        static HandPose PinchAt(HandSide side, Vector3 p, float gap) =>
            HandPose.Open(side, Vector3.zero, Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.4f, Vector3.up)).WithIndexTipAt(p).WithPinch(gap);

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; }
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 300 && (_layout.AnyMoving || _layout.Settling); i++) yield return Frames(1);
            yield return Frames(6);
        }

        static string OutDir()
        {
            string dir = ShotDirs.FromRoot("placement") ?? ShotDirs.Default("placement");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator StartingViewsHandlesAndMixedRealityHands()
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
            yield return Frames(20);
            Assert.IsTrue(_menu.StartShowing);
            // "Play here", then looking a little down as a seated player does.
            _app.Head.rotation = Quaternion.Euler(18f, 0f, 0f);
            MenuController start = _menu.Controller;
            for (int i = 0; i < start.RowCount; i++) if (start.Row(i).Id == RowKind.PlayHere) { start.Fire(i); break; }
            yield return Frames(20);
            Assert.IsFalse(_menu.StartShowing, "Play here");

            string dir = OutDir();
            var camGo = new GameObject("PlacementShotsCamera");
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
                // 1. The piano alone.
                _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
                yield return Settle();
                yield return Eyes(cam, rt, readback, dir, "start_single_piano");
                // 2. A set of three.
                _layout.Set(LayoutMode.All, InstrumentKind.Handpan);
                yield return Settle();
                Assert.IsTrue(_layout.TrySetSet(new[] { InstrumentKind.Handpan, InstrumentKind.Bowls, InstrumentKind.HandDrums }));
                yield return Settle();
                _layout.ArrangeAroundPlayer(true);
                yield return Settle();
                yield return Eyes(cam, rt, readback, dir, "start_set_handpan_bowls_handdrums");
                // 3. My set as it comes ("All": piano, drums, harp).
                Assert.IsTrue(_layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }));
                yield return Settle();
                _layout.ArrangeAroundPlayer(true);
                yield return Settle();
                yield return Eyes(cam, rt, readback, dir, "start_all_piano_drums_harp");

                // 4. Move instruments: a hand near the piano (its outline lights), then the piano dragged (gold).
                _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
                yield return Settle();
                _layout.BeginArrange();
                yield return Frames(2);
                Rig pianoRig = _app.Get<IRigRegistry>().Get(RigId.Piano);
                IPianoApi pk = _app.Get<IPianoApi>();
                pk.TryGetKey(53, out PianoKeyInfo f3);
                Vector3 at = pk.Root.TransformPoint(new Vector3(f3.X, 0.05f, 0.06f));
                _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at + new Vector3(0f, 0.02f, -0.02f), 0.05f));
                yield return Frames(20);
                Assert.That(_layout.NearTarget(0), Is.SameAs(pianoRig), "the hand is near the piano: its outline lights");
                // From the player's eyes, a little to the left, toward the hand.
                Vector3 side = _app.Head.position + new Vector3(-0.12f, 0.05f, -0.05f);
                yield return Shot(cam, rt, readback, dir, "move_hovered", side, at, 60f);
                _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at, 0f));
                yield return Frames(10);
                Assert.IsTrue(_layout.MoveDragging);
                for (int i = 1; i <= 10; i++) { _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at + new Vector3(-0.08f, 0.04f, 0.06f) * (i / 10f), 0f)); yield return Frames(1); }
                yield return Frames(8);
                yield return Shot(cam, rt, readback, dir, "move_grabbed", side, at + new Vector3(-0.08f, 0f, 0.06f), 60f);
                _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
                yield return Frames(30);
                yield return Settle();
                _layout.ResetArrangement();
                _layout.EndArrange();
                yield return Settle();

                // 5. "Play in my room": the drawn (Skin) hands over the piano, simulated passthrough.
                var mr = (MrModule)_app.Get<IMixedReality>();
                var task = mr.Enter();
                float t1 = Time.realtimeSinceStartup;
                while ((!task.IsCompleted || mr.Loading) && Time.realtimeSinceStartup - t1 < 30f) { _boot.Step(Dt); yield return null; }
                yield return Frames(10);
                Assert.IsTrue(mr.Active);
                Assert.That(_app.HandInput.Look, Is.EqualTo(MetaHandVisual.Look.Skin), "drawn hands in my room");
                IPianoApi piano = _app.Get<IPianoApi>();
                piano.TryGetKey(60, out PianoKeyInfo c4);
                piano.TryGetKey(67, out PianoKeyInfo g4);
                Vector3 l = piano.Root.TransformPoint(new Vector3(c4.X, 0.03f, 0.06f)), r = piano.Root.TransformPoint(new Vector3(g4.X, 0.03f, 0.06f));
                Quaternion down = Quaternion.LookRotation(piano.Root.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), piano.Root.up);
                _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, down).WithIndexTipAt(l));
                _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, down).WithIndexTipAt(r));
                yield return Frames(10);
                yield return Eyes(cam, rt, readback, dir, "mr_skin_hands");
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        /// <summary>The player's view: from the head, along its gaze, about a headset's field of view; then the same moment
        /// from above and behind the seat (name_overview), so the instruments beside the player show too.</summary>
        IEnumerator Eyes(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name)
        {
            Transform h = _app.Head;
            yield return Shot(cam, rt, readback, dir, name, h.position, h.position + h.forward, 100f);
            Vector3 f = h.forward; f.y = 0f; f.Normalize();
            Vector3 above = h.position + Vector3.up * 0.95f - f * 0.75f;
            yield return Shot(cam, rt, readback, dir, name + "_overview", above, new Vector3(h.position.x, 0.45f, h.position.z) + f * 0.25f, 85f);
        }

        IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 look, float fov)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = fov;
            cam.aspect = Width / (float)Height;
            cam.backgroundColor = _app.Rig.HeadCamera.backgroundColor;
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
            Debug.Log($"[PlacementShots] shot {name}");
        }
    }
}
