// E7 visual check (Graphics category, Tools/unity.sh test-play --gpu): the kit's motion in the real room, rendered to
// Builds/Shots/exp-world (or $MUSICROOM_SHOTS_ROOT/exp-world, else $MUSICROOM_SHOTS_DIR):
//   cymbal-crash-rest.png / cymbal-crash-peak.png / cymbal-crash-0.5s.png  the crash from its side, at rest, at the peak
//       tilt after a full-velocity hit on its near edge (frame 13 at 90 Hz, about 6.6 deg) and half a second later;
//   snare-head-rest.png / snare-head-give.png  the snare head at a grazing angle, at rest and on the frame of a hard hit;
//   kick-beater-rest.png / kick-beater-contact.png  the kick pedal from the side: the beater at rest and on the head.
// Manual frames: every app frame is an explicit 1/90 s step, so each picture shows exactly the state the test made.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Drums
{
    [Category("Graphics")]
    public class KitMotionShotsTests
    {
        const int W = 1280, H = 960;
        const float Dt = 1f / 90f;
        TestBoot _boot;

        public static string ShotDir()
        {
            string env = ShotDirs.FromRoot("exp-world") ?? Environment.GetEnvironmentVariable("MUSICROOM_SHOTS_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return ShotDirs.Default("exp-world");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        /// <summary>Renders the head camera from <paramref name="from"/> toward <paramref name="at"/> into a PNG and returns
        /// its pixels (sRGB, MSAA 4).</summary>
        public static Color32[] Shot(Camera cam, string name, Vector3 from, Vector3 at, float fov)
        {
            Transform head = cam.transform;
            head.position = from;
            head.rotation = Quaternion.LookRotation(at - from, Vector3.up);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else { var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev; }
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            Directory.CreateDirectory(ShotDir());
            string path = Path.Combine(ShotDir(), name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Color32[] px = tex.GetPixels32();
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Debug.Log($"[Drums] shot {path}");
            return name == null ? null : px;
        }

        /// <summary>Share of pixels whose colour differs by more than <paramref name="levels"/> (sum of RGB) between two shots.</summary>
        public static float Changed(Color32[] a, Color32[] b, int levels = 12)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > levels) n++;
            return n / (float)a.Length;
        }

        [UnityTest]
        public IEnumerator CymbalsRockHeadsGiveAndTheBeaterStrikes()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU: run Tools/unity.sh test-play --gpu"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no first-launch tour in the pictures
            yield return _boot.Start();
            App app = _boot.App;
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            _boot.Step(Dt);
            Assert.IsTrue(app.Get<ILayoutService>().FirstArrangeDone);
            DrumKit kit = null;
            foreach (var m in app.Modules) if (m is DrumsModule d) kit = d.Kit;
            Assert.That(kit, Is.Not.Null);
            var desk = app.Rig.Desktop;
            if (desk != null) desk.enabled = false;   // the test owns the camera
            Camera cam = app.Rig.HeadCamera;
            for (int i = 0; i < 20; i++) { _boot.Step(Dt); yield return null; }   // the room probe and textures settle
            Shot(cam, "warm-up", cam.transform.position, cam.transform.position + Vector3.forward, 60f);
            File.Delete(Path.Combine(ShotDir(), "warm-up.png"));

            // 1. The crash from its side (along its plate's x axis, so the tip of its near edge reads as the plate's tilt).
            DrumPiece crash = kit.Piece(DrumSpec.CrashId);
            Vector3 cAt = crash.Group.position;
            Vector3 cFrom = crash.Group.TransformPoint(new Vector3(-0.85f, 0.06f, 0.02f));
            Color32[] rest = Shot(cam, "cymbal-crash-rest", cFrom, cAt, 38f);
            Assert.IsTrue(kit.Hit(crash, 1f, null, true, new DrumStrike { Pos = 0.9f, Zone = DrumZone.Edge }, new Vector2(0f, -0.19f)));
            int peakFrame = 0;
            float peak = 0f;
            for (int f = 1; f <= 13; f++)
            {
                _boot.Step(Dt);
                if (crash.Tilt.MagnitudeDeg > peak) { peak = crash.Tilt.MagnitudeDeg; peakFrame = f; }
            }
            Debug.Log($"[Drums] crash tilt {crash.Tilt.Tx * Mathf.Rad2Deg:F2} / {crash.Tilt.Tz * Mathf.Rad2Deg:F2} deg (peak {peak:F2} at frame {peakFrame})");
            Assert.That(peak, Is.GreaterThan(5f), "a full-velocity edge hit tips the crash about 6 deg");
            Assert.That(crash.Tilt.Tx, Is.LessThan(0f), "the struck near edge (-z) goes down");
            Color32[] tilted = Shot(cam, "cymbal-crash-peak", cFrom, cAt, 38f);
            Assert.That(Changed(rest, tilted), Is.GreaterThan(0.002f), "the tilt is visible");
            for (int f = 0; f < 32; f++) _boot.Step(Dt);
            Shot(cam, "cymbal-crash-0.5s", cFrom, cAt, 38f);

            // 2. The snare head at a grazing angle from the player's side: at rest, then on the frame of a hard hit.
            DrumPiece snare = kit.Piece(DrumSpec.SnareId);
            Vector3 sAt = snare.Group.TransformPoint(new Vector3(0.03f, 0f, -0.03f));
            Vector3 sFrom = snare.Group.TransformPoint(new Vector3(0.05f, 0.14f, -0.42f));
            for (int f = 0; f < 4; f++) _boot.Step(Dt);
            Color32[] flat = Shot(cam, "snare-head-rest", sFrom, sAt, 32f);
            Assert.IsTrue(kit.Hit(snare, 1f, null, true, new DrumStrike { Pos = 0.3f, Zone = DrumZone.Head }, new Vector2(0.03f, -0.03f)));
            Assert.That(snare.Give.CurrentDepth, Is.EqualTo(0.004f).Within(3e-4f), "4 mm at full velocity");
            Color32[] dented = Shot(cam, "snare-head-give", sFrom, sAt, 32f);
            float give = Changed(flat, dented, 9);
            Debug.Log($"[Drums] head give changes {give * 100f:F2} % of the close-up");
            Assert.That(give, Is.GreaterThan(0.001f), "the dent shades the head");

            // 3. The kick pedal from the player's right: the beater at rest, then on the head (a pad hit plays the kick).
            Vector3 kAt = kit.transform.TransformPoint(new Vector3(0.12f, 0.14f, 0.62f));
            Vector3 kFrom = kit.transform.TransformPoint(new Vector3(0.75f, 0.32f, 0.5f));
            for (int f = 0; f < 4; f++) _boot.Step(Dt);
            Color32[] idle = Shot(cam, "kick-beater-rest", kFrom, kAt, 34f);
            Assert.IsTrue(kit.Hit(kit.Piece(DrumSpec.KickPadId), 1f, null, true, DrumStrike.Default));
            Assert.That(kit.Beater.AngleDeg, Is.EqualTo(KickBeater.ContactDeg));
            Color32[] contact = Shot(cam, "kick-beater-contact", kFrom, kAt, 34f);
            Assert.That(Changed(idle, contact), Is.GreaterThan(0.002f), "the beater swings onto the head");
        }
    }
}
