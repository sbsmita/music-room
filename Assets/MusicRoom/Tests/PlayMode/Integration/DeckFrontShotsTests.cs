// Screenshots for TUNE-DECK-FRONT (Category Graphics, `Tools/unity.sh test-play --gpu`): One alone with the kit after
// "Play here", from the seated player's eyes looking ahead (the kit in front), looking down to the left (the tape deck on
// the side table beside the left hip), and from above and behind the seat. Builds/Shots/deckfront (or
// $MUSICROOM_SHOTS_ROOT/deckfront).
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
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

namespace MusicRoom.Tests.PlayMode.Integration
{
    [Category("Graphics")]
    public class DeckFrontShotsTests
    {
        const int Width = 1280, Height = 960;
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
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, "drums");
            _boot.Settings.SetString(AppSettings.KeySet, "piano,harp,handdrums");
            _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(0.6f, 1.9f, -0.9f)));
            _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(-0.6f, 1.9f, -0.9f)));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            GC.Collect();
        }

        void Hold(Vector3 head, Quaternion rot, int n)
        {
            for (int i = 0; i < n; i++) { _app.Head.SetPositionAndRotation(head, rot); _boot.Step(Dt); }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TheKitAheadAndTheDeckAtTheLeft()
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
            Vector3 head = new Vector3(0f, 1.15f, 0f);
            Hold(head, Quaternion.identity, 10);
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Hold(head, Quaternion.identity, 60);
            _layout.BringToPlayer();
            Hold(head, Quaternion.identity, 60);
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Drums));

            string dir = ShotDirs.FromRoot("deckfront") ?? ShotDirs.Default("deckfront");
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("DeckFrontShotsCamera");
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
                yield return Shot(cam, rt, readback, dir, "single_drums_ahead", head, Quaternion.Euler(18f, 0f, 0f), 100f);
                yield return Shot(cam, rt, readback, dir, "single_drums_deck_left", head, Quaternion.Euler(40f, -75f, 0f), 100f);
                Vector3 above = head + new Vector3(0f, 0.95f, -0.75f);
                yield return Shot(cam, rt, readback, dir, "single_drums_overview", above, Quaternion.LookRotation(new Vector3(0f, 0.45f, 0.25f) - above, Vector3.up), 85f);
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Quaternion rot, float fov)
        {
            cam.transform.SetPositionAndRotation(eye, rot);
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
            Debug.Log($"[DeckFrontShots] shot {name}");
        }
    }
}
