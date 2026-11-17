// The nod kick, case by case (spec 05 section 3): a downward pitch faster than 1.6 rad/s fires once per swing, as long as
// the swing is under .16 s old and the last kick .18 s ago; loudness from the peak rate (spec 05 curves.nodKick);
// re-arming needs the rate to fall under .8 rad/s; inactive frames forget the last pitch.
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class NodKickTests
    {
        const float Dt = 1f / 72f;

        static Vector3 Forward(float pitchDownRad) => new Vector3(0f, -Mathf.Sin(pitchDownRad), Mathf.Cos(pitchDownRad));

        /// Drives the head down at <paramref name="rate"/> rad/s for <paramref name="frames"/> frames from <paramref name="pitch"/>.
        static float Swing(NodKick nod, ref float pitch, ref double now, float rate, int frames, out int fired)
        {
            fired = 0;
            float last = 0f;
            for (int i = 0; i < frames; i++)
            {
                pitch += rate * Dt;
                now += Dt;
                float v = nod.Tick(Dt, true, Forward(pitch), now);
                if (v > 0f) { fired++; last = v; }
            }
            return last;
        }

        [Test]
        public void AQuickNodFiresOnceWithTheCurveVelocity()
        {
            var nod = new NodKick();
            float pitch = 0f; double now = 10;
            Assert.That(nod.Tick(Dt, true, Forward(pitch), now), Is.EqualTo(0f), "the first frame only learns the pitch");
            float v = Swing(nod, ref pitch, ref now, 4f, 6, out int fired);
            Assert.That(fired, Is.EqualTo(1), "once per swing, on the swing itself");
            Assert.That(v, Is.EqualTo(0.6173f).Within(0.01f), "curves.nodKick: 4 rad/s -> .6173");
            Assert.That(nod.LastRate, Is.EqualTo(4f).Within(0.02f));
        }

        [Test]
        public void SlowLooksDownNeverFire()
        {
            var nod = new NodKick();
            float pitch = 0f; double now = 10;
            nod.Tick(Dt, true, Forward(pitch), now);
            Swing(nod, ref pitch, ref now, 1.5f, 60, out int fired);
            Assert.That(fired, Is.EqualTo(0), "1.5 rad/s is a look down, not a nod");
            pitch = 0f;
            Swing(nod, ref pitch, ref now, -5f, 20, out fired);
            Assert.That(fired, Is.EqualTo(0), "looking up is never a kick");
        }

        [Test]
        public void TheSwingWindowAndTheDebounce()
        {
            var nod = new NodKick();
            float pitch = 0f; double now = 10;
            nod.Tick(Dt, true, Forward(pitch), now);
            Swing(nod, ref pitch, ref now, 3f, 4, out int fired);
            Assert.That(fired, Is.EqualTo(1));
            // Back up (re-arm), then down again only .1 s after the kick: debounced.
            Swing(nod, ref pitch, ref now, -1f, 2, out _);
            Swing(nod, ref pitch, ref now, 3f, 3, out fired);
            Assert.That(fired, Is.EqualTo(0), "within .18 s of the last kick");
            // Stop, rest, nod again: fires.
            Swing(nod, ref pitch, ref now, 0f, 30, out _);
            Swing(nod, ref pitch, ref now, 3f, 3, out fired);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void AFastSwingOlderThanTheWindowIsJustLookingDown()
        {
            var nod = new NodKick();
            float pitch = 0f; double now = 10;
            nod.Tick(Dt, true, Forward(pitch), now);
            Swing(nod, ref pitch, ref now, 2f, 1, out int fired);
            Assert.That(fired, Is.EqualTo(1));
            Swing(nod, ref pitch, ref now, 0f, 1, out _);   // re-armed at once (rate 0 < .8)
            // A fast downward swing that begins inside the .18 s debounce: by the time a kick may fire again the swing
            // has lasted longer than .16 s, so it is a look down, not a nod.
            Swing(nod, ref pitch, ref now, 2.5f, 25, out fired);
            Assert.That(fired, Is.EqualTo(0));
        }

        [Test]
        public void ReArmingNeedsTheRateUnderPointEight()
        {
            var nod = new NodKick();
            float pitch = 0f; double now = 10;
            nod.Tick(Dt, true, Forward(pitch), now);
            Swing(nod, ref pitch, ref now, 2f, 3, out int fired);
            Assert.That(fired, Is.EqualTo(1));
            Swing(nod, ref pitch, ref now, 1.2f, 30, out _);      // slower, but never under .8: still disarmed
            Swing(nod, ref pitch, ref now, 2.5f, 3, out fired);
            Assert.That(fired, Is.EqualTo(0), "not re-armed (the rate stayed over .8 rad/s)");
            Swing(nod, ref pitch, ref now, 0.5f, 1, out _);
            Swing(nod, ref pitch, ref now, 2.5f, 1, out fired);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void InactiveFramesResetSoReturningIsNotANod()
        {
            var nod = new NodKick();
            double now = 10;
            nod.Tick(Dt, true, Forward(0f), now);
            now += Dt;
            Assert.That(nod.Tick(Dt, false, Forward(0.5f), now), Is.EqualTo(0f));
            now += Dt;
            Assert.That(nod.Tick(Dt, true, Forward(0.5f), now), Is.EqualTo(0f), "the jump while inactive is not a rate");
            now += Dt;
            Assert.That(nod.Tick(Dt, true, Forward(0.5f), now), Is.EqualTo(0f));
            nod.Enabled = false;
            Assert.That(nod.Tick(Dt, true, Forward(1.5f), now + Dt), Is.EqualTo(0f));
            Assert.That(nod.Tick(0f, true, Forward(0f), now + Dt), Is.EqualTo(0f), "dt 0 is inactive");
        }
    }
}
