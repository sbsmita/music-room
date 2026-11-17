// The draw-call budget of ARCHITECTURE section 17 as a GPU test (category "Graphics": Tools/unity.sh test-play --gpu).
// The head camera renders into a RenderTexture for a few frames; the profiler's render counters give the draw and
// SetPass counts of the booted scene (single-pass instanced draws each object once for both eyes, so the mono count is
// the budgeted number). Stage-3 modules keep this green: whole scene <= 150 draws. 10-upgrade/experience.md E3: the contact
// shadows add ONE draw with two hands over the keys and none with the hands away.
using System;
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class DrawBudgetTests
    {
        public const int WholeSceneDraws = 150;
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator BootedSceneStaysInsideTheDrawBudget()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            _boot = new TestBoot(headless: false);
            yield return _boot.Start();
            var cam = App.Current.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "DrawBudget" };
            cam.targetTexture = rt;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            for (int i = 0; i < 8; i++) yield return null;
            long d = draws.LastValue, s = setPass.LastValue;
            bool valid = draws.Valid;
            draws.Dispose(); setPass.Dispose();
            cam.targetTexture = null;
            Object.Destroy(rt);
            if (!valid) Assert.Ignore("render counters unavailable in this player");
            Debug.Log($"[DrawBudget] draws={d} setPass={s} (budget {WholeSceneDraws})");
            Assert.That(d, Is.LessThanOrEqualTo(WholeSceneDraws));
        }

        [UnityTest]
        public IEnumerator ContactShadowsAreOneDrawWithHandsOverTheKeysAndNoneWithout()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (-nographics): run Tools/unity.sh test-play --gpu");
            _boot = new TestBoot(headless: false, manualFrames: true);
            yield return _boot.Start();
            var app = App.Current;
            app.HandInput.debugJoints = false;   // the editor's joint spheres are not part of the headset's budget
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            var layout = app.Get<ILayoutService>();
            for (int i = 0; i < 20 && (layout == null || !layout.FirstArrangeDone); i++) { _boot.Step(1f / 72f); yield return null; }
            IPianoApi piano = app.Get<IPianoApi>();
            Transform root = piano.Root;
            Vector3 Key(int midi, float y, float back) { piano.TryGetKey(midi, out var k); return root.TransformPoint(new Vector3(k.X, y, back)); }
            Quaternion playing = Quaternion.LookRotation(root.TransformDirection(new Vector3(0f, -0.65f, 1f).normalized), root.up);
            var cam = app.Rig.HeadCamera;
            var rt = new RenderTexture(1024, 1024, 24) { name = "ContactDraws" };
            cam.targetTexture = rt;
            long away = -1, without = -1, with = -1;
            IEnumerator Measure(Action<long> set)
            {
                var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                for (int i = 0; i < 6; i++) yield return null;
                set(draws.Valid ? draws.LastValue : -1);
                draws.Dispose();
            }
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            _boot.Step(1f / 72f, 2);
            var shadows = app.Contacts.Shadows;
            Assert.IsFalse(shadows.Renderer.enabled, "hands away: the shadow renderer is off");
            yield return Measure(v => away = v);
            _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, playing).WithIndexTipAt(Key(64, 0.03f, 0.06f)));
            _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, playing).WithIndexTipAt(Key(55, 0.03f, 0.06f)));
            app.Contacts.ShadowsEnabled = false;
            _boot.Step(1f / 72f, 2);
            Assert.IsFalse(shadows.Renderer.enabled);
            yield return Measure(v => without = v);
            app.Contacts.ShadowsEnabled = true;
            _boot.Step(1f / 72f, 2);
            Assert.IsTrue(shadows.Renderer.enabled, "hands over the keys: the shadows draw");
            Assert.That(app.Contacts.ShadowCount, Is.GreaterThanOrEqualTo(10), "both hands' fingers");
            yield return Measure(v => with = v);
            cam.targetTexture = null;
            Object.Destroy(rt);
            Debug.Log($"[DrawBudget] contact shadows: hands away {away}, hands over the keys without shadows {without}, with shadows {with} ({app.Contacts.ShadowCount} blobs)");
            if (away < 0 || without < 0 || with < 0) Assert.Ignore("render counters unavailable in this player");
            Assert.That(with - without, Is.EqualTo(1), "every blob in one draw");
            Assert.That(with, Is.LessThanOrEqualTo(WholeSceneDraws));
        }
    }
}
