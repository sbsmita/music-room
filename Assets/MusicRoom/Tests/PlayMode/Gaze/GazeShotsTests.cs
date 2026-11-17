// Pictures of look and pinch (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at: from the seat, a menu row
// lit by the head gaze (the poke's own row highlight), and an instrument on display lit by its soft warm glow, each beside
// the same view unlit. Output: $MUSICROOM_SHOTS_ROOT/gaze, else Builds/Shots/gaze.
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Gaze
{
    [Category("Graphics")]
    public class GazeShotsTests : GazeTestBase
    {
        const int W = 1280, H = 720;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _read;
        string _dir;

        [UnityTearDown]
        public IEnumerator TearDownShots()
        {
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            yield return null;
        }

        void Shot(string name)
        {
            _cam.transform.SetPositionAndRotation(App.Head.position, App.Head.rotation);
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(_dir, name + ".png");
            File.WriteAllBytes(path, _read.EncodeToPNG());
            Debug.Log($"[Gaze] shot {name}: {path}");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ALitRowAndALitInstrument()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            yield return BootApp(headless: false);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f) { Step(); yield return null; }
            _dir = ShotDirs.FromRoot("gaze") ?? ShotDirs.Default("gaze");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("GazeShotCamera").AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.aspect = (float)W / H;
            _cam.fieldOfView = 70f;

            // an instrument on display
            InstrumentKind? k = Displayed(out Vector3 c);
            Assert.That(k.HasValue, Is.True);
            Assert.That(Gaze.GlowLevel, Is.GreaterThan(0.9f));
            Shot("instrument-lit-" + InstrumentIds.Of(k.Value));
            Gaze.On = false;
            StepSeconds(0.4f);
            Shot("instrument-unlit-" + InstrumentIds.Of(k.Value));
            Gaze.On = true;

            // a menu row
            yield return OpenMenu();
            StepSeconds(0.3f);
            Gaze.On = false;
            Look(RowCentre("instruments"));
            StepSeconds(0.4f);
            Shot("menu-unlit");
            Gaze.On = true;
            StepSeconds(0.6f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.MenuRow));
            Shot("menu-row-lit");
            // the Sunny page with its new "Look and pinch" row
            MenuM.Controller.Go(MenuScreen.Guide);
            Looking = Quaternion.Euler(80f, 0f, 0f);
            StepSeconds(0.3f);
            Look(RowCentre(MenuRowIds.Tour));
            StepSeconds(0.05f);
            Looking = Quaternion.LookRotation(MenuM.Root.position - App.Head.position);
            Gaze.On = false;
            StepSeconds(0.3f);
            Shot("menu-sunny-page");
            Gaze.On = true;
            yield return null;
        }
    }
}
