// The "Move instruments" drag's light smoothing (DragFilter, OneEuro): the first frame of a drag is exactly the hand's point (no jump
// at the grab start), a still hand's tracker jitter is calmed, a moving hand is followed within about a centimetre and
// caught up within a few frames of stopping (no rubber band, no overshoot), and the soft dead band is continuous.
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public class DragFilterTests
    {
        const float Dt = 1f / 72f;

        [Test]
        public void TheFirstFrameIsTheHandsOwnPoint()
        {
            var f = new DragFilter();
            Vector3 p = new Vector3(0.3f, 0.8f, 0.4f);
            f.Reset(p, 0.2f);
            Assert.That(Vector3.Distance(f.StepPoint(p, Dt), p), Is.LessThan(1e-6f), "no jump at the grab start");
            Assert.That(f.StepYaw(0.2f, Dt), Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(Vector3.Distance(f.StepPoint(p, 0f), p), Is.LessThan(1e-6f), "dt 0 keeps the last output");
        }

        [Test]
        public void AStillHandsJitterIsCalmed()
        {
            var f = new DragFilter();
            var rng = new System.Random(7);
            Vector3 p = new Vector3(0f, 0.8f, 0.4f);
            f.Reset(p, 0f);
            float rawMax = 0f, outMax = 0f;
            for (int i = 0; i < 300; i++)
            {
                Vector3 j = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.006f;   // +-3 mm
                Vector3 o = f.StepPoint(p + j, Dt);
                if (i < 30) continue;
                rawMax = Mathf.Max(rawMax, j.magnitude);
                outMax = Mathf.Max(outMax, Vector3.Distance(o, p));
            }
            Debug.Log($"[DragFilter] still hand: raw jitter up to {rawMax * 1000f:F1} mm, drawn {outMax * 1000f:F1} mm");
            Assert.That(outMax, Is.LessThan(0.5f * rawMax), "at least halved");
        }

        [Test]
        public void AMovingHandIsFollowedClosely()
        {
            var f = new DragFilter();
            Vector3 p = Vector3.zero, v = new Vector3(0.5f, 0f, 0f);   // 0.5 m/s
            f.Reset(p, 0f);
            float lag = 0f;
            Vector3 prev = p;
            for (int i = 1; i <= 60; i++)
            {
                p = v * (i * Dt);
                Vector3 o = f.StepPoint(p, Dt);
                Assert.That(o.x, Is.GreaterThanOrEqualTo(prev.x - 1e-6f), "never backward");
                Assert.That(o.x, Is.LessThanOrEqualTo(p.x + 1e-6f), "never ahead of the hand");
                prev = o;
                if (i > 20) lag = Mathf.Max(lag, p.x - o.x);
            }
            Debug.Log($"[DragFilter] 0.5 m/s: lag {lag * 1000f:F1} mm");
            Assert.That(lag, Is.LessThan(0.015f), "within 1.5 cm at 0.5 m/s");
            // The hand stops: caught up within 0.15 s, never past it.
            Vector3 stop = p;
            for (int i = 0; i < 11; i++)
            {
                Vector3 o = f.StepPoint(stop, Dt);
                Assert.That(o.x, Is.LessThanOrEqualTo(stop.x + 1e-6f), "no overshoot");
            }
            Assert.That(stop.x - f.Point.x, Is.LessThan(0.003f), "caught up within 3 mm after 0.15 s");
        }

        [Test]
        public void TheSoftDeadbandIsContinuous()
        {
            const float b = MoveConstants.TurnDeadband;
            Assert.That(DragFilter.SoftDeadband(0.5f * b, b), Is.EqualTo(0f));
            Assert.That(DragFilter.SoftDeadband(-b, b), Is.EqualTo(0f));
            Assert.That(DragFilter.SoftDeadband(b + 0.01f, b), Is.EqualTo(0.01f).Within(1e-6f), "no step at the edge");
            Assert.That(DragFilter.SoftDeadband(-b - 0.2f, b), Is.EqualTo(-0.2f).Within(1e-6f));
        }

        [Test]
        public void TheHoldHysteresisIsWiderThanTheStart()
        {
            Assert.That(MoveConstants.HoldGap, Is.GreaterThan(MusicRoom.Hands.HandMath.PinchEndGap), "a loosened pinch still holds");
            Assert.That(MoveConstants.HoldStrength, Is.LessThan(0.5f), "below Meta's own 0.5 end");
            Assert.That(MoveConstants.ReleaseGrace, Is.InRange(0.05f, 0.2f), "a flicker never drops it, a real release is quick");
            Assert.That(MoveConstants.LostGrace, Is.GreaterThan(MoveConstants.ReleaseGrace));
        }

        /// <summary>The spin: a comfortable wrist turn (90 degrees, about what a seated forearm turns while pinching) spins an
        /// instrument well past a half turn, so two grabs (or two hands) go all the way round; the dead band keeps the roll of
        /// a closing pinch from turning it.</summary>
        [Test]
        public void TheWristSpinsItPastAHalfTurn()
        {
            float wrist = 90f * Mathf.Deg2Rad;
            float spin = MoveConstants.TurnGain * DragFilter.SoftDeadband(wrist, MoveConstants.TurnDeadband) * Mathf.Rad2Deg;
            Assert.That(spin, Is.GreaterThan(160f), "a 90 degree wrist turn spins it " + spin + " degrees");
            Assert.That(2f * spin, Is.GreaterThanOrEqualTo(360f - 40f), "two turns go (nearly) all the way round");
            Assert.That(MoveConstants.TurnDeadband * Mathf.Rad2Deg, Is.InRange(3f, 10f), "a small roll never turns it");
            Assert.That(MoveConstants.TurnGain, Is.InRange(1.5f, 3f), "still easy to aim");
        }
    }
}
