// Screenshots for TUNE-DECK-FRONT Part 3 (Category Graphics, `Tools/unity.sh test-play --gpu`): from the seated player's
// eyes and from above, One alone drums, One alone piano, My set (piano, harp, hand drums) at the playing distance, and One
// alone piano while a piano take plays (the show: StageFarther out). Builds/Shots/deckfront.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using System.Collections.Generic;
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
    public class FartherShotsTests
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
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, "piano");
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
        public IEnumerator PlayingDistanceAndTheShow()
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

            string dir = ShotDirs.FromRoot("deckfront") ?? ShotDirs.Default("deckfront");
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("FartherShotsCamera");
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
                Vector3 above = head + new Vector3(0f, 0.95f, -0.75f);
                Quaternion down = Quaternion.LookRotation(new Vector3(0f, 0.45f, 0.25f) - above, Vector3.up);
                string[] names = { "farther_single_piano", "farther_single_drums", "farther_myset" };
                for (int c = 0; c < names.Length; c++)
                {
                    if (c == 1) _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
                    if (c == 2) _layout.Set(LayoutMode.All, InstrumentKind.Piano);
                    Hold(head, Quaternion.identity, 120);
                    _layout.BringToPlayer();
                    Hold(head, Quaternion.identity, 60);
                    yield return Shot(cam, rt, readback, dir, names[c], head, Quaternion.Euler(18f, 0f, 0f), 100f);
                    yield return Shot(cam, rt, readback, dir, names[c] + "_overview", above, down, 85f);
                }
                // the show: One alone piano, a piano take playing by itself
                _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
                Hold(head, Quaternion.identity, 120);
                var take = new List<TakeEvent>();
                for (float t = 0.3f; t < 8f; t += 0.4f)
                {
                    TakeEvent on = TakeEvent.PianoOn(60 + ((int)(t * 5f) % 12), 0.8f); on.t = t; take.Add(on);
                    TakeEvent off = TakeEvent.PianoOff(on.midi); off.t = t + 0.3f; take.Add(off);
                }
                Assert.IsTrue(((Recorder)_app.Get<IRecorder>()).PlayEvents(take, 8.5));
                Hold(head, Quaternion.identity, 150);
                Assert.IsTrue(_layout.StageFar, "the show");
                yield return Shot(cam, rt, readback, dir, "farther_show_piano", head, Quaternion.Euler(18f, 0f, 0f), 100f);
                yield return Shot(cam, rt, readback, dir, "farther_show_piano_overview", above, down, 85f);
                // the show of three: a piano + drums + harp take from One alone piano
                var rec = (Recorder)_app.Get<IRecorder>();
                rec.Stop();
                Hold(head, Quaternion.identity, 200);
                var three = new List<TakeEvent>();
                for (float t = 0.3f; t < 8f; t += 0.4f)
                {
                    TakeEvent on = TakeEvent.PianoOn(60 + ((int)(t * 5f) % 12), 0.8f); on.t = t; three.Add(on);
                    TakeEvent off = TakeEvent.PianoOff(on.midi); off.t = t + 0.3f; three.Add(off);
                    TakeEvent d = TakeEvent.Drum(((int)(t * 5f)) % 2 == 0 ? "snare" : "kick", 0.8f, 0.3f, "head", false); d.t = t + 0.05f; three.Add(d);
                    TakeEvent h = TakeEvent.Harp(8 + ((int)(t * 5f) % 6), 0.6f, 0.35f, 0f, "through"); h.t = t + 0.1f; three.Add(h);
                }
                three.Sort((x, y) => x.t.CompareTo(y.t));
                Assert.IsTrue(rec.PlayEvents(three, 8.5));
                Hold(head, Quaternion.identity, 150);
                Assert.IsTrue(_layout.StageFar, "the show of three");
                yield return Shot(cam, rt, readback, dir, "farther_show_three", head, Quaternion.Euler(14f, 0f, 0f), 96f);
                yield return Shot(cam, rt, readback, dir, "farther_show_three_overview", head + new Vector3(0f, 1.6f, -1.2f), Quaternion.LookRotation(new Vector3(0f, 0.3f, 1.4f) - (head + new Vector3(0f, 1.6f, -1.2f)), Vector3.up), 85f);
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
            Debug.Log($"[FartherShots] shot {name}");
        }
    }
}
