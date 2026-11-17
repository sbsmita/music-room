// Visual check (Graphics category, Tools/unity.sh test-play --gpu) of the space between the kit's drums: the kit in the
// real room from the player's seat, plus close-ups of the spots where the snare meets the two rack toms and where the toms
// meet each other, rendered to Builds/Shots/drum-space (or $MUSICROOM_SHOTS_ROOT/drum-space):
//   seat.png       the kit from the drummer's eyes (kit-local, behind the snare);
//   snare-tom1.png the snare's far rim and the small rack tom, from the player's left, under the hi-hat;
//   snare-tom2.png the snare's far rim and the big rack tom, from above the player's right shoulder;
//   toms.png       the two rack toms side by side, from behind the snare;
//   top.png        the whole kit from above.
// The numbers (the gaps themselves) are DrumSpacingTests' (EditMode); these pictures are for the eye.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Drums
{
    [Category("Graphics")]
    public class DrumSpaceShotsTests
    {
        const float Dt = 1f / 90f;
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest]
        public IEnumerator TheDrumsStandApart()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU: run Tools/unity.sh test-play --gpu"); yield break; }
            System.Environment.SetEnvironmentVariable("MUSICROOM_SHOTS_DIR", ShotDirs.FromRoot("drum-space") ?? ShotDirs.Default("drum-space"));
            try
            {
                _boot = new TestBoot(headless: false, manualFrames: true);
                _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no first-launch tour in the pictures
                yield return _boot.Start();
                App app = _boot.App;
                float t0 = Time.realtimeSinceStartup;
                while (app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
                _boot.Step(Dt);
                DrumKit kit = null;
                foreach (var m in app.Modules) if (m is DrumsModule d) kit = d.Kit;
                Assert.That(kit, Is.Not.Null);
                var desk = app.Rig.Desktop;
                if (desk != null) desk.enabled = false;   // the test owns the camera
                Camera cam = app.Rig.HeadCamera;
                for (int i = 0; i < 20; i++) { _boot.Step(Dt); yield return null; }   // the room probe and textures settle
                Transform k = kit.transform;
                KitMotionShotsTests.Shot(cam, "warm-up", cam.transform.position, cam.transform.position + Vector3.forward, 60f);
                System.IO.File.Delete(System.IO.Path.Combine(KitMotionShotsTests.ShotDir(), "warm-up.png"));

                Vector3 tom1 = kit.Piece(DrumSpec.Tom1Id).Group.localPosition, tom2 = kit.Piece(DrumSpec.Tom2Id).Group.localPosition;
                Vector3 snare = kit.Piece(DrumSpec.SnareId).Group.localPosition;
                KitMotionShotsTests.Shot(cam, "seat", k.TransformPoint(new Vector3(0.02f, 1.22f, -0.16f)), k.TransformPoint(new Vector3(0.08f, 0.70f, 0.48f)), 72f);
                Vector3 j1 = (snare + tom1) * 0.5f + new Vector3(0f, -0.06f, 0f);
                KitMotionShotsTests.Shot(cam, "snare-tom1", k.TransformPoint(new Vector3(-0.56f, 0.68f, 0.45f)), k.TransformPoint(j1), 40f);   // under the hi-hat
                Vector3 j2 = (snare + tom2) * 0.5f + new Vector3(0f, -0.05f, 0f);
                KitMotionShotsTests.Shot(cam, "snare-tom2", k.TransformPoint(new Vector3(0.30f, 1.30f, -0.05f)), k.TransformPoint(j2), 34f);
                KitMotionShotsTests.Shot(cam, "toms", k.TransformPoint(new Vector3(0.12f, 1.10f, 0.08f)), k.TransformPoint((tom1 + tom2) * 0.5f + new Vector3(0f, -0.08f, 0f)), 42f);
                KitMotionShotsTests.Shot(cam, "top", k.TransformPoint(new Vector3(0.05f, 2.05f, 0.30f)), k.TransformPoint(new Vector3(0.05f, 0.7f, 0.36f)), 48f);
            }
            finally { System.Environment.SetEnvironmentVariable("MUSICROOM_SHOTS_DIR", null); }
        }
    }
}
