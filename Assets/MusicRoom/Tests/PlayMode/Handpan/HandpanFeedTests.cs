// The play logic fed by the app's REAL input path (handpan.md 9.2 PlayMode; review items 8 and 15): FakeHandSource ->
// HandInput -> PointerInput (App.Frame step 2) -> HandpanHandFeed (Pointer.Tip, the frame clock, HandMath.Palm, the flags;
// never HandFrame.sampleInterval) -> HandpanPlay. The test-only placement puts the instrument at its seat home.
//   - a 0.6 m/s index tap into A3's dimple at 90 Hz frames: one A3 strike at about vel 0.635 (the lab's tap-A3);
//   - the same stroke with the pose repeated for 3 frames (a 30 Hz tracker): the same strike within 0.05;
//   - a hand that loses tracking mid-air never strikes; the mouse hand never strikes;
//   - Fill + Step allocate nothing per frame.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Handpan;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Handpan
{
    public class HandpanFeedTests
    {
        const float Dt = 1f / 90f;
        TestBoot _boot;
        App _app;
        HandpanPlace _place;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            _app = _boot.App;
            var layout = _app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 60f) { _boot.Step(Dt); yield return null; }
            SeatFrame seat = layout.Seat;
            Vector3 c = seat.Place(new Vector3(0f, (float)HandpanSpec.RimHeight, (float)HandpanSpec.HomeZ));
            _place = new HandpanPlace(new D3(c.x, c.y, c.z), HandpanSpec.TiltDeg, seat.Yaw(0f));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _app = null;
        }

        HandPose Pointing(Vector3 indexTip)
        {
            Vector3 fwd = _app.Get<ILayoutService>().Seat.Forward;
            Vector3 dir = Quaternion.AngleAxis(50f, Vector3.Cross(Vector3.up, fwd).normalized) * fwd;
            return HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(dir, Vector3.up)).WithPointing().WithIndexTipAt(indexTip);
        }

        /// <summary>The stroke's index-tip heights (pad over the dimple edge of A3): 5 cm down to -12 mm at speed, dwell, up.</summary>
        static List<float> Stroke(float speed)
        {
            var h = new List<float>();
            for (int k = 0; k < 12; k++) h.Add(0.05f);
            for (float y = 0.05f; y > -0.012f; y -= speed * Dt) h.Add(y);
            h.Add(-0.012f);
            for (int k = 0; k < 3; k++) h.Add(-0.012f);
            for (float y = -0.012f; y < 0.05f; y += 0.5f * Dt) h.Add(y);
            return h;
        }

        IEnumerator Play(List<float> heights, int repeat, List<HandpanEvent> events, bool track = true)
        {
            var play = new HandpanPlay { Place = _place };
            var frame = new HandpanFrame();
            HandpanMath.MapAt(1, 0.35, 0.2, out double X, out double Z);
            D3 p = HandpanMath.CapPoint(X, Z, true, out D3 n);
            Vector3 surf = _place.ToWorld(p).ToVector3(), nw = _place.DirToWorld(n).Normalized.ToVector3();
            float pad = HandRestPose.Radius[(int)HandJoint.IndexTip] * _boot.Hands.HandScale;
            _boot.Hands.SetTracked(HandSide.Right, track);
            for (int k = 0; k < heights.Count; k++)
            {
                if (k % repeat == 0) _boot.Hands.SetPose(HandSide.Right, Pointing(surf + nw * (pad + heights[k])));
                if (!track) _boot.Hands.SetTracked(HandSide.Right, false);   // SetPose marks the hand tracked
                _boot.Step(Dt);
                HandpanHandFeed.Fill(frame, _app.Input, _app.Clock.Now, Dt);
                int c = play.Step(frame);
                for (int i = 0; i < c; i++) events.Add(play.Events[i]);
                if (k % 30 == 0) yield return null;
            }
            _boot.Hands.SetTracked(HandSide.Right, false);
        }

        [UnityTest]
        public IEnumerator ATapThroughTheRealInputPathPlaysA3()
        {
            var ev90 = new List<HandpanEvent>();
            yield return Play(Stroke(0.6f), 1, ev90);
            for (int i = 0; i < 20; i++) { _boot.Step(Dt); }
            var ev30 = new List<HandpanEvent>();
            yield return Play(Stroke(0.6f), 3, ev30);
            HandpanEvent a = ev90.Find(e => e.Type == HandpanEventType.Strike), b = ev30.Find(e => e.Type == HandpanEventType.Strike);
            Debug.Log($"[Handpan] real input: 90 Hz {string.Join(" | ", ev90)}; 30 Hz poses {string.Join(" | ", ev30)}");
            Assert.AreEqual(1, ev90.FindAll(e => e.Type == HandpanEventType.Strike).Count, "one strike");
            Assert.AreEqual(1, a.Field, "on A3");
            Assert.AreEqual(HandpanTech.Tip, a.Tech);
            Assert.AreEqual(0.635, a.Vel, 0.05, "0.6 m/s reads about 0.635 (not 3x too soft: no sample interval)");
            Assert.AreEqual(1, ev30.FindAll(e => e.Type == HandpanEventType.Strike).Count, "the 30 Hz poses: one strike too");
            Assert.AreEqual(a.Vel, b.Vel, 0.05, "and the same loudness");
        }

        [UnityTest]
        public IEnumerator AnUntrackedHandNeverStrikes()
        {
            var ev = new List<HandpanEvent>();
            yield return Play(Stroke(0.6f), 1, ev, track: false);
            Assert.AreEqual(0, ev.Count, "untracked: nothing");
        }

        [UnityTest]
        public IEnumerator FillAndStepAllocateNothing()
        {
            var play = new HandpanPlay { Place = _place };
            var frame = new HandpanFrame();
            _boot.Hands.SetTracked(HandSide.Right, true);
            HandpanMath.MapAt(1, 0.35, 0.2, out double X, out double Z);
            D3 p = HandpanMath.CapPoint(X, Z, true, out D3 n);
            Vector3 surf = _place.ToWorld(p).ToVector3(), nw = _place.DirToWorld(n).Normalized.ToVector3();
            _boot.Hands.SetPose(HandSide.Right, Pointing(surf + nw * 0.03f));
            for (int k = 0; k < 10; k++) { _boot.Step(Dt); HandpanHandFeed.Fill(frame, _app.Input, _app.Clock.Now, Dt); play.Step(frame); }
            yield return null;
            TestDelegate measured = () =>
            {
                for (int k = 0; k < 60; k++) { HandpanHandFeed.Fill(frame, _app.Input, _app.Clock.Now + k * Dt, Dt); play.Step(frame); }
            };
            AllocAssert.Nothing(measured);   // the code once first, then the main thread's byte counter and the recorder
        }
    }
}
