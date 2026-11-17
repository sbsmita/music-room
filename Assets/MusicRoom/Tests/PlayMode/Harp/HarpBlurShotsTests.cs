// E8 visual check (Graphics category, Tools/unity.sh test-play --gpu): the harp strings' blur in the real room, rendered
// to Builds/Shots/exp-world (or $MUSICROOM_SHOTS_ROOT/exp-world, else $MUSICROOM_SHOTS_DIR):
//   harp-still-close.png          strings 9..13 close up, at rest;
//   harp-blur-close.png           string 11 (G4) one frame after a hard pluck across the plane at u .35: a soft spindle,
//                                 widest near the pluck, its core faded (no alternating bow, no 45 Hz shimmer);
//   harp-blur-close-0.5s.png      the same string half a second later: narrower, as its sound has decayed;
//   harp-blur-low-c-0.4s.png / harp-blur-high-c-0.4s.png  a low C and a high C plucked together, 0.4 s later: the bass
//                                 still kinked and wide, the treble already narrow (each follows its own partial decays);
//   harp-blur-glissando-seat.png  a glissando over 12 strings, from the seat.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Harp
{
    [Category("Graphics")]
    public class HarpBlurShotsTests
    {
        const int W = 1280, H = 960;
        const float Dt = 1f / 90f;
        TestBoot _boot;

        static string ShotDir()
        {
            string env = ShotDirs.FromRoot("exp-world") ?? Environment.GetEnvironmentVariable("MUSICROOM_SHOTS_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return ShotDirs.Default("exp-world");
        }

        static Color32[] Shot(Camera cam, string name, Vector3 from, Vector3 at, float fov)
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
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            Debug.Log($"[Harp] shot {path}");
            return px;
        }

        static float Changed(Color32[] a, Color32[] b, int levels = 12)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > levels) n++;
            return n / (float)a.Length;
        }

        /// <summary>Manual app frames for exactly this much app time, one per player loop. The harp plays the classic sound
        /// (2026-11-10), so the blur ages with app time alone; waiting for real time too would age it by however many frames
        /// the machine draws in that time.</summary>
        IEnumerator Frames(float seconds, Action each)
        {
            int frames = Mathf.CeilToInt(seconds / Dt);
            for (int f = 0; f < frames; f++)
            {
                _boot.Step(Dt);
                each?.Invoke();
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest]
        public IEnumerator APluckedStringBlursIntoASpindleThatNarrowsAsItDies()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU: run Tools/unity.sh test-play --gpu"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no first-launch tour in the pictures
            yield return _boot.Start();
            App app = _boot.App;
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            _boot.Step(Dt);
            var harp = (HarpInstrument)app.Get<IHarpApi>();
            IHarpApi api = harp;
            var desk = app.Rig.Desktop;
            if (desk != null) desk.enabled = false;
            Camera cam = app.Rig.HeadCamera;
            for (int i = 0; i < 20; i++) { _boot.Step(Dt); yield return null; }
            Shot(cam, "warm-up", cam.transform.position, cam.transform.position + Vector3.forward, 60f);
            File.Delete(Path.Combine(ShotDir(), "warm-up.png"));

            HarpString s = harp.Play.Strings[11];
            Vector3 local = Vector3.Lerp(s.A, s.B, 0.42f);
            Vector3 at = harp.Root.TransformPoint(local);
            Vector3 from = harp.Root.TransformPoint(local + new Vector3(0.02f, 0f, -0.38f));   // the player's side of the plane
            Color32[] still = Shot(cam, "harp-still-close", from, at, 34f);

            api.PlaybackPluck(11, 1f, 0.35f, 1.5f, HarpPlay.ModeAcross);
            _boot.Step(Dt);
            Assert.IsTrue(s.Blurring);
            Debug.Log($"[Harp] blur G4 widest {s.BlurSwing * 1000f:F2} mm, core alpha at ring 6 {harp.ShownAlpha(11, 6, true):F2}");
            Assert.That(harp.ShownAlpha(11, 6, true), Is.LessThan(0.6f), "the core fades where it swings");
            Color32[] blur = Shot(cam, "harp-blur-close", from, at, 34f);
            Assert.That(Changed(still, blur), Is.GreaterThan(0.002f), "the blur is visible");
            // The blur's envelope ages with the string's own partial decays (classic sound: no engine measurement).
            float wide = s.BlurSwing;
            yield return Frames(0.15f, () => wide = Mathf.Max(wide, s.BlurSwing));
            yield return Frames(0.5f, null);
            Assert.That(s.BlurSwing, Is.LessThan(wide * 0.6f), "narrower half a second later");
            Shot(cam, "harp-blur-close-0.5s", from, at, 34f);

            // A low C (string 0) and a high C (string 21) together, seen 0.4 s later.
            yield return Frames(2.2f, null);
            api.PlaybackPluck(0, 1f, 0.3f, 1.5f, HarpPlay.ModeAcross);
            api.PlaybackPluck(21, 1f, 0.3f, 1.5f, HarpPlay.ModeAcross);
            yield return Frames(0.4f, null);
            HarpString lo = harp.Play.Strings[0], hi = harp.Play.Strings[21];
            Debug.Log($"[Harp] after 0.4 s: C3 swing {lo.BlurSwing * 1000f:F2} mm, C6 swing {hi.BlurSwing * 1000f:F2} mm");
            Assert.That(lo.BlurSwing, Is.GreaterThan(hi.BlurSwing * 2f), "the bass rings on, the treble narrows fast");
            foreach (HarpString c in new[] { lo, hi })
            {
                Vector3 cl = Vector3.Lerp(c.A, c.B, 0.45f);
                Shot(cam, c.Index == 0 ? "harp-blur-low-c-0.4s" : "harp-blur-high-c-0.4s",
                    harp.Root.TransformPoint(cl + new Vector3(0.02f, 0f, -0.38f)), harp.Root.TransformPoint(cl), 34f);
            }

            // A glissando over strings 5..16 from the seat (the desktop eye the layout arranged around).
            yield return Frames(2.2f, null);
            for (int i = 5; i <= 16; i++) { api.PlaybackPluck(i, 0.8f, 0.38f, 1.2f, HarpPlay.ModeAcross); _boot.Step(Dt); }
            Vector3 seat = app.Rig.RigRoot.TransformPoint(DesktopRigFallback.EyePosition);
            Shot(cam, "harp-blur-glissando-seat", seat, harp.Root.TransformPoint(HarpGeometry.PlayPoint(10)), 70f);
        }
    }
}
