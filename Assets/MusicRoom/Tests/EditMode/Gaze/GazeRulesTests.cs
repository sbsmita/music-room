// Look and pinch, the rules (Gaze/GazeRules.cs): the dwell (a steady head, the grace, a new target), the field of view, the
// head speed, the gate, the free hand, the tap (a short still pinch released on the same target), the keys, no allocation.
using MusicRoom.Gaze;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace MusicRoom.Tests.EditMode.Gaze
{
    public class GazeRulesTests
    {
        const float DT = 1f / 72f;
        static readonly long A = GazeRules.Key(0, 3), B = GazeRules.Key(2, 0);

        static int FramesUntilCommitted(GazeDwell d, long key, bool steady = true, int max = 200)
        {
            for (int i = 1; i <= max; i++) if (d.Step(key, DT, steady) == key) return i;
            return -1;
        }

        [Test]
        public void ATargetCountsAfterTheDwellWithASteadyHead()
        {
            var d = new GazeDwell();
            int n = FramesUntilCommitted(d, A);
            float t = (n - 1) * DT;
            Assert.That(t, Is.EqualTo(GazeRules.DwellSeconds).Within(DT + 1e-4f), "about 0.35 s of looking");
            Assert.That(GazeRules.DwellSeconds, Is.InRange(0.3f, 0.5f));
        }

        [Test]
        public void AFastTurnPicksNothingNew()
        {
            var d = new GazeDwell();
            for (int i = 0; i < 72; i++) Assert.That(d.Step(A, DT, false), Is.EqualTo(0L), "mid fast turn: nothing counts");
            Assert.That(d.Step(A, DT, true), Is.EqualTo(A), "the head settles on it: it counts at once (it was looked at long enough)");
        }

        [Test]
        public void AShortLookAwayKeepsItALongerOneDropsIt()
        {
            var d = new GazeDwell();
            FramesUntilCommitted(d, A);
            for (int i = 0; i < 8; i++) Assert.That(d.Step(0, DT, true), Is.EqualTo(A), "within the 0.15 s grace");
            Assert.That(d.Step(A, DT, true), Is.EqualTo(A), "back on it: still it");
            int k = 0;
            while (d.Step(0, DT, true) == A && k < 100) k++;
            Assert.That((k + 1) * DT, Is.EqualTo(GazeRules.LoseGrace).Within(DT + 1e-4f), "dropped after the grace");
        }

        [Test]
        public void ANewTargetNeedsItsOwnDwell()
        {
            var d = new GazeDwell();
            FramesUntilCommitted(d, A);
            int n = FramesUntilCommitted(d, B);
            Assert.That((n - 1) * DT, Is.EqualTo(GazeRules.DwellSeconds).Within(DT + 1e-4f));
            // a sweep across many targets never lights one
            d.Reset();
            for (int i = 0; i < 200; i++) Assert.That(d.Step(GazeRules.Key(1 + (i / 10) % 8, 0), DT, true), Is.EqualTo(0L), "10 frames on each: none counts");
        }

        [Test]
        public void TheFieldOfViewRule()
        {
            Vector3 eye = new Vector3(0f, 1.2f, 0f), fwd = Vector3.forward;
            Assert.IsTrue(GazeRules.InView(eye, fwd, eye + Quaternion.Euler(0f, 20f, 0f) * Vector3.forward * 2f));
            Assert.IsTrue(GazeRules.InView(eye, fwd, eye + Quaternion.Euler(-21f, 0f, 0f) * Vector3.forward * 0.5f));
            Assert.IsFalse(GazeRules.InView(eye, fwd, eye + Quaternion.Euler(0f, 24f, 0f) * Vector3.forward * 2f), "outside ~22 degrees");
            Assert.IsFalse(GazeRules.InView(eye, fwd, eye + Vector3.back), "behind");
            Assert.That(GazeRules.MaxAngleDeg, Is.InRange(20f, 25f));
        }

        [Test]
        public void HeadSpeedIsMeasuredAndSmoothed()
        {
            Vector3 a = Vector3.forward, b = Quaternion.Euler(0f, 1f, 0f) * Vector3.forward;
            Assert.That(GazeRules.AngularSpeed(a, b, DT), Is.EqualTo(72f).Within(0.01f), "1 degree a frame at 72 Hz");
            float s = 0f;
            for (int i = 0; i < 72; i++) s = GazeRules.Smooth(s, 200f, DT);
            Assert.That(s, Is.EqualTo(200f).Within(1f));
            Assert.IsFalse(GazeRules.HeadSteady(s));
            for (int i = 0; i < 72; i++) s = GazeRules.Smooth(s, 10f, DT);
            Assert.IsTrue(GazeRules.HeadSteady(s));
        }

        [Test]
        public void TheGate()
        {
            Assert.IsTrue(GazeRules.Enabled(true, true, false, false, false, false));
            Assert.IsFalse(GazeRules.Enabled(false, true, false, false, false, false), "the setting off");
            Assert.IsFalse(GazeRules.Enabled(true, false, false, false, false, false), "not in the headset");
            Assert.IsFalse(GazeRules.Enabled(true, true, true, false, false, false), "moving instruments");
            Assert.IsFalse(GazeRules.Enabled(true, true, false, true, false, false), "MR placing");
            Assert.IsFalse(GazeRules.Enabled(true, true, false, false, true, false), "a rig held");
            Assert.IsFalse(GazeRules.Enabled(true, true, false, false, false, true), "Sunny teaching a pinch");
        }

        [Test]
        public void OnlyAFreeHandChooses()
        {
            Assert.IsTrue(GazeRules.HandFree(true, false, false, false, false, false, false, false));
            Assert.IsFalse(GazeRules.HandFree(false, false, false, false, false, false, false, false), "untracked");
            Assert.IsFalse(GazeRules.HandFree(true, true, false, false, false, false, false, false), "the mouse");
            Assert.IsFalse(GazeRules.HandFree(true, false, true, false, false, false, false, false), "holding a stick");
            Assert.IsFalse(GazeRules.HandFree(true, false, false, true, false, false, false, false), "busy (Layout)");
            Assert.IsFalse(GazeRules.HandFree(true, false, false, false, true, false, false, false), "index tip claimed");
            Assert.IsFalse(GazeRules.HandFree(true, false, false, false, false, true, false, false), "thumb tip claimed");
            Assert.IsFalse(GazeRules.HandFree(true, false, false, false, false, false, true, false), "the system gesture");
            Assert.IsFalse(GazeRules.HandFree(true, false, false, false, false, false, false, true), "just played");
        }

        [Test]
        public void TheTap()
        {
            var tap = new GazeTap();
            tap.Begin(1, Vector3.zero, A);
            for (int i = 0; i < 12; i++) tap.Step(DT, new Vector3(0.002f * i, 0f, 0f), true);
            Assert.IsTrue(tap.Release(A), "a short still pinch released on the same target chooses it");

            tap.Begin(1, Vector3.zero, A);
            tap.Step(DT, Vector3.zero, true);
            Assert.IsFalse(tap.Release(B), "looked away before letting go");

            tap.Begin(1, Vector3.zero, A);
            for (int i = 0; i < Mathf.CeilToInt(GazeRules.MaxTapSeconds / DT) + 2; i++) tap.Step(DT, Vector3.zero, true);
            Assert.IsFalse(tap.Release(A), "a held pinch is someone else's gesture");

            tap.Begin(1, Vector3.zero, A);
            tap.Step(DT, new Vector3(0.07f, 0f, 0f), true);
            Assert.IsFalse(tap.Release(A), "a moving pinch (the swivel pull) is not a choice");

            tap.Begin(1, Vector3.zero, A);
            tap.Step(DT, Vector3.zero, false);
            tap.Step(DT, Vector3.zero, true);
            Assert.IsFalse(tap.Release(A), "the hand became busy: never");
            Assert.IsFalse(tap.Release(A), "released once");
        }

        [Test]
        public void KeysRoundTrip()
        {
            long k = GazeRules.Key(0, (7 << 5) | 3);
            Assert.That(GazeRules.SlotOf(k), Is.EqualTo(0));
            Assert.That(GazeRules.SubOf(k), Is.EqualTo((7 << 5) | 3));
            Assert.That(GazeRules.SlotOf(GazeRules.Key(10, 0)), Is.EqualTo(10));
            Assert.That(GazeRules.SlotOf(0), Is.EqualTo(-1));
            Assert.That(GazeRules.Key(0, 0), Is.Not.EqualTo(0L), "a real target is never the empty key");
        }

        [Test]
        public void TheRulesAllocateNothing()
        {
            var d = new GazeDwell();
            var tap = new GazeTap();
            d.Step(A, DT, true);
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    d.Step(i % 40 < 30 ? A : B, DT, GazeRules.HeadSteady(GazeRules.Smooth(10f, 20f, DT)));
                    GazeRules.InView(Vector3.zero, Vector3.forward, Vector3.one);
                    tap.Begin(0, Vector3.zero, A); tap.Step(DT, Vector3.zero, true); tap.Release(A);
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
