// E7 (experience.md section 9): cymbal rocking, the head's give and the kick beater, against the reference vectors
// (the experience lab's experience.vectors.json cymbalTilt, headDimple; frames of exactly 1/90 s; tolerance 0.02 deg).
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class CymbalTiltTests
    {
        const float Dt = 1f / 90f;

        /// <summary>A full-velocity hit on the +z edge, then 270 frames: the peak and the (frame, tx, tz) samples.</summary>
        static void Check(in CymbalParams p, double peakDeg, double peakAt, (int f, double tx, double tz)[] samples)
        {
            var s = new CymbalTilt();
            CymbalMotion.Strike(ref s, p, 0f, p.Radius, 1f);
            float peak = 0f;
            int peakF = 0, next = 0;
            for (int f = 1; f <= 270; f++)
            {
                CymbalMotion.Step(ref s, p, Dt, false);
                float m = s.MagnitudeDeg;
                if (m > peak) { peak = m; peakF = f; }
                if (next < samples.Length && samples[next].f == f)
                {
                    Assert.That(s.Tx * Mathf.Rad2Deg, Is.EqualTo(samples[next].tx).Within(0.02), $"tx at frame {f}");
                    Assert.That(s.Tz * Mathf.Rad2Deg, Is.EqualTo(samples[next].tz).Within(0.02), $"tz at frame {f}");
                    next++;
                }
            }
            Assert.That(next, Is.EqualTo(samples.Length));
            Assert.That(peak, Is.EqualTo(peakDeg).Within(0.02), "peak tilt");
            Assert.That(peakF * (double)Dt, Is.EqualTo(peakAt).Within(Dt * 0.51), "peak time");
        }

        [Test]
        public void TheCrashTipsTowardTheStrikeAndRocksSlowly() => Check(CymbalMotion.Crash, 6.5979, 0.1444, new[]
        {
            (5, 3.9213, 0.5095), (9, 5.8110, 1.3141), (14, 6.1816, 2.1333), (23, 2.6112, 1.3042), (45, -2.0150, -3.5268),
            (90, 1.3953, -1.3034), (180, -0.2733, -0.8156), (270, -0.2191, 0.0598),
        });

        [Test]
        public void TheRideRocksSlowerAndLess() => Check(CymbalMotion.Ride, 3.3778, 0.1889, new[]
        {
            (9, 2.4929, 0.4030), (45, -1.1404, -1.3162), (90, -0.0381, 1.8436),
        });

        [Test]
        public void TheOpenHiHatRocksBrisklyAndTheClosedOneNotAtAll()
        {
            Check(CymbalMotion.HatOpen, 2.1990, 0.0889, new[] { (9, 2.0578, 0.6332), (90, 0.0135, 0.0731) });
            var s = new CymbalTilt();
            CymbalMotion.Strike(ref s, CymbalMotion.HatClosed, 0f, 0.17f, 1f);
            CymbalMotion.Step(ref s, CymbalMotion.HatClosed, Dt, false);
            Assert.That(s.MagnitudeDeg, Is.EqualTo(0f), "the clutch clamps a closed hi-hat (maxTilt 0)");
            Assert.IsFalse(s.Moving);
        }

        [Test]
        public void TheStruckEdgeGoesDownAndAChokeStopsTheRocking()
        {
            // A hit on the +x edge at vel .5: tz -0.4382 deg after one frame (Euler(0, 0, +a) lifts +x).
            var s = new CymbalTilt();
            CymbalMotion.Strike(ref s, CymbalMotion.Crash, CymbalMotion.Crash.Radius, 0f, 0.5f);
            CymbalMotion.Step(ref s, CymbalMotion.Crash, Dt, false);
            Assert.That(s.Tz * Mathf.Rad2Deg, Is.EqualTo(-0.4382).Within(0.02));
            Assert.That(s.Tx * Mathf.Rad2Deg, Is.EqualTo(0.01437).Within(0.02));
            Vector3 edge = s.Rotation * new Vector3(CymbalMotion.Crash.Radius, 0f, 0f);
            Assert.That(edge.y, Is.LessThan(0f), "the struck +x edge is lower than the plate's plane");
            // Harder hits rock more (linear in vel), and |theta| never exceeds 1.5 x maxTilt however many hits pile up.
            var hard = new CymbalTilt();
            for (int i = 0; i < 20; i++) { CymbalMotion.Strike(ref hard, CymbalMotion.Crash, 0f, 0.21f, 1f); CymbalMotion.Step(ref hard, CymbalMotion.Crash, Dt, false); }
            Assert.That(hard.MagnitudeDeg, Is.LessThanOrEqualTo(1.5f * 8f + 1e-3f));
            // A hand on the plate: x exp(-dt / 0.03) a frame, still within a quarter second.
            for (int i = 0; i < 23; i++) CymbalMotion.Step(ref hard, CymbalMotion.Crash, Dt, true);
            Assert.That(hard.MagnitudeDeg, Is.LessThan(0.05f));
        }
    }

    public class HeadDimpleTests
    {
        [Test]
        public void TheDentIsFourMillimetresTimesVelAndLastsAFrameOrTwo()
        {
            // headDimple: R .18, vel 1.
            Assert.That(HeadGive.Dimple(0f, 0f, 1f, 0.18f), Is.EqualTo(0.004f).Within(1e-6f));
            Assert.That(HeadGive.Dimple(0f, 0.011f, 1f, 0.18f), Is.EqualTo(0.002171f).Within(1e-6f));
            Assert.That(HeadGive.Dimple(0.045f, 0f, 1f, 0.18f), Is.EqualTo(0.002426f).Within(1e-6f));
            Assert.That(HeadGive.Dimple(0.09f, 0f, 1f, 0.18f), Is.EqualTo(0.000541f).Within(1e-6f));
            Assert.That(HeadGive.Dimple(0f, 0.022f, 1f, 0.18f), Is.EqualTo(0.001178f).Within(1e-6f));
            Assert.That(HeadGive.Dimple(0f, 0f, 0.5f, 0.18f), Is.EqualTo(0.002f).Within(1e-6f), "linear in vel");
        }

        [Test]
        public void TheRingedHeadDentsWhereStruckWithNormalsFacingTheDentsCentre()
        {
            var mk = new MeshKit();
            mk.RingedDisc(0.18f, 6, 48, 0.001f);
            Mesh mesh = mk.ToMesh("head");
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(289));
                var give = new HeadGive(mesh, 0.18f);
                Vector3[] rest = mesh.vertices;
                give.Strike(0.06f, 0f, 1f);
                Vector3[] v = mesh.vertices, n = mesh.normals;
                // The ring-2 vertex at (+0.06, 0) is the strike point: 4 mm down, normal straight up.
                int at = -1;
                for (int i = 0; i < rest.Length; i++) if ((new Vector2(rest[i].x, rest[i].z) - new Vector2(0.06f, 0f)).magnitude < 1e-5f) at = i;
                Assert.That(at, Is.GreaterThan(0));
                Assert.That(rest[at].y - v[at].y, Is.EqualTo(0.004f).Within(1e-6f));
                Assert.That(Vector3.Angle(n[at], Vector3.up), Is.LessThan(0.01f));
                // Further out along +x the head slopes back up: the normal tilts toward the centre (-x).
                int outer = -1;
                for (int i = 0; i < rest.Length; i++) if ((new Vector2(rest[i].x, rest[i].z) - new Vector2(0.09f, 0f)).magnitude < 1e-5f) outer = i;
                Assert.That(n[outer].x, Is.LessThan(0f));
                Assert.That(give.CurrentDepth, Is.EqualTo(0.004f).Within(1e-6f));
                // DrumKit.Tick ages every head before the frame's hits: the next Step is the next frame
                give.Step(1f / 90f);
                Assert.That(rest[at].y - mesh.vertices[at].y, Is.EqualTo(0.004f * Mathf.Exp(-(1f / 90f) / 0.018f)).Within(1e-6f));
                give.Step(1f / 90f);
                Assert.IsTrue(give.Active, "frame 3");
                give.Step(1f / 90f);
                Assert.IsFalse(give.Active, "three frames, then flat");
                Assert.That(mesh.vertices, Is.EqualTo(rest));
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }

    public class KickBeaterTests
    {
        [Test]
        public void TheBallMeetsTheKickHeadAtContactAndReturnsCriticallyDamped()
        {
            Vector3 c = KickBeater.ContactPoint;
            Assert.That(c.z, Is.EqualTo(0.70f).Within(0.002f), "the batter head's plane (kick at z .70)");
            Assert.That(c.y, Is.EqualTo(0.26f).Within(0.005f));
            Assert.That(c.x, Is.EqualTo(DrumSpec.Pieces[DrumSpec.KickId].Pos.x));
            Assert.That(KickBeater.AngleAt(0f), Is.EqualTo(KickBeater.ContactDeg));
            // 90 % of the way back in about 0.13 s: (1 + x) e^-x = 0.1 at x = 3.89 (tau 35 ms -> 0.136 s).
            float back = (KickBeater.ContactDeg - KickBeater.AngleAt(0.136f)) / (KickBeater.ContactDeg - KickBeater.RestDeg);
            Assert.That(back, Is.EqualTo(0.9f).Within(0.005f));
            float prev = KickBeater.ContactDeg;
            for (float t = 0.005f; t < 0.4f; t += 0.005f)
            {
                float a = KickBeater.AngleAt(t);
                Assert.That(a, Is.LessThanOrEqualTo(prev + 1e-5f), "no overshoot: critically damped");
                Assert.That(a, Is.GreaterThanOrEqualTo(KickBeater.RestDeg - 1e-4f));
                prev = a;
            }
            Assert.That(KickBeater.FootPitchDeg(KickBeater.RestDeg), Is.EqualTo(DrumSpec.PedalPitchDeg).Within(1e-4f));
            Assert.That(KickBeater.FootPitchDeg(KickBeater.ContactDeg), Is.EqualTo(DrumSpec.PedalPitchDeg - 10f).Within(1e-4f));
        }
    }
}
