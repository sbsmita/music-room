// Visual check (Graphics category: Tools/unity.sh test-play --gpu). Boots the whole app desktop-style through TestBoot
// with manual frames (so nothing moves between the shots), poses Sunny by hand (start, BeginStep, demoT, one tiny update)
// and renders the seat camera into PNGs under Builds/Shots/guide/ (or $MUSICROOM_SHOTS_ROOT/guide):
//   guide-intro-seat.png  the default desktop view from the seat with Sunny's first speech bubble
//   guide-intro.png       the same seat, looking at Sunny and its bubble
//   guide-palm.png        the palm demo with the ghost hand and the charging ring (step 0, demoT .95)
//   guide-swipe.png       the swipe demo with the dotted arrow (step 3, demoT .32)
// and each again as *-wall.png in front of a flat back wall in the room's plaster colour (#c8aa8c, sampled from a room
// render), since the stub room has no walls yet and Sunny's additive corona and ghost hand are tuned for that backdrop.
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Guide
{
    [Category("Graphics")]
    public class GuideShotsTests
    {
        // MUSICROOM_SHOTS_ROOT (optional) redirects the shots of a whole run (e.g. a clone's own folder); default unchanged.
        static string ShotRoot(string m) => ShotDirs.FromRoot(m) ?? ShotDirs.Default(m);
        public static readonly string OutDir = ShotRoot("guide");
        const int W = 1280, H = 800;   // the shot window size

        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MusicRoom.Guide.GuideModule.ClassicTour = false;
            AudioBootGuard.Reset();
            if (_wall != null) Object.Destroy(_wall);
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest]
        public IEnumerator SunnyFromTheSeat()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            MusicRoom.Guide.GuideModule.ClassicTour = true;   // first five: these tests check the classic five-step tour
            _boot = new TestBoot(headless: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no first-launch tour: Sunny is posed by hand below
            AudioBootGuard.Begin();
            yield return _boot.Start();
            yield return null;
            AudioBootGuard.End();
            App app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 5f) yield return null;   // the desktop rig (real time)
            Assert.That(app.Rig.State, Is.EqualTo(XrState.Desktop));
            var layout = app.Get<ILayoutService>();
            for (int i = 0; i < 30 && layout != null && !layout.FirstArrangeDone; i++) _boot.Step(1f / 72f);
            _boot.Step(1f / 72f, 5);   // manual frames from here on: the poses below stay exactly as set

            var guide = (GuideModule)app.Get<IGuide>();
            Sunny s = guide.Sunny;
            Directory.CreateDirectory(OutDir);
            Transform head = app.Head;

            // The back wall (z = +1.12, 6.4 x 2.9 m) as a flat unlit plane for the *-wall variants.
            _wall = new GameObject("GuideShotWall");
            _wall.AddComponent<MeshFilter>().sharedMesh = TextureUtil.UnitQuad();
            _wall.AddComponent<MeshRenderer>().sharedMaterial = app.Materials.UnlitMaterial(TextureUtil.Hex("#c8aa8c"));
            _wall.transform.SetPositionAndRotation(new Vector3(0f, 1.45f, 1.12f), Quaternion.identity);
            _wall.transform.localScale = new Vector3(6.4f, 2.9f, 1f);

            // 1. The first line, from the seat.
            guide.Start();
            s.Update(0.016f);
            guide.View.Apply(s);
            Both("guide-intro-seat", head.position, head.rotation);
            Vector3 bubble = guide.View.Bubble.position;
            Both("guide-intro", head.position, Quaternion.LookRotation(Vector3.Lerp(bubble, guide.View.Body.position, 0.5f) - head.position));

            // 2. The palm demo (BeginStep(0), demoT .95: palm toward the player, ring charging).
            s.BeginStep(0);
            s.DemoT = 0.95f;
            s.Update(0.0001f);
            guide.View.Apply(s);
            Assert.IsTrue(s.Demo.Visible && s.Demo.Ring, "palm demo with the ring");
            Both("guide-palm", head.position, LookAtDemo(guide, head.position));

            // 3. The swipe demo (BeginStep(3), demoT .32: mid-sweep along the dotted arrow).
            s.BeginStep(3);
            s.DemoT = 0.32f;
            s.Update(0.0001f);
            guide.View.Apply(s);
            Assert.IsTrue(s.Demo.Visible && s.Demo.Arrow, "swipe demo with the arrow");
            Both("guide-swipe", head.position, LookAtDemo(guide, head.position));

            foreach (string n in new[] { "guide-intro-seat", "guide-intro", "guide-palm", "guide-swipe" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(OutDir, n + ".png")), n);
                Assert.IsTrue(File.Exists(Path.Combine(OutDir, n + "-wall.png")), n + "-wall");
            }
            yield return null;
        }

        GameObject _wall;

        void Both(string name, Vector3 position, Quaternion rotation)
        {
            _wall.SetActive(false);
            Shot(name, position, rotation);
            _wall.SetActive(true);
            Shot(name + "-wall", position, rotation);
            _wall.SetActive(false);
        }

        // The shots look between the hand and the body (lerp .45).
        static Quaternion LookAtDemo(GuideModule guide, Vector3 eye)
        {
            Vector3 target = Vector3.Lerp(guide.View.Hand.position, guide.View.Body.position, 0.45f);
            return Quaternion.LookRotation(target - eye);
        }

        static void Shot(string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("GuideShotCamera");
            var cam = go.AddComponent<Camera>();
            Camera main = App.Current.Rig.HeadCamera;
            cam.CopyFrom(main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = DesktopRigFallback.DefaultFov;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.allowHDR = false;
            var rt = new RenderTexture(new RenderTextureDescriptor(W, H, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 1 });
            rt.Create();
            cam.targetTexture = rt;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Debug.Log($"[Guide] shot {name} -> {OutDir}/{name}.png");
            cam.targetTexture = null;
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(go);
        }
    }
}
