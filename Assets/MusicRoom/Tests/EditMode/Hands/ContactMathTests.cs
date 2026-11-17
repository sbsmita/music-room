// E1 of 10-upgrade/experience.md section 3: the visual finger clamp against the vectors of
// the experience lab's experience.vectors.json (fingerClamp; tolerance 1e-5 m / 1e-4 rad).
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Hands
{
    public class ContactMathTests
    {
        const float Dt = 1f / 90f;
        const float TolM = 1e-5f, TolRad = 1e-4f;

        static void Near(Vector3 got, Vector3 want, float tol, string what)
        {
            Assert.That(got.x, Is.EqualTo(want.x).Within(tol), what + ".x");
            Assert.That(got.y, Is.EqualTo(want.y).Within(tol), what + ".y");
            Assert.That(got.z, Is.EqualTo(want.z).Within(tol), what + ".z");
        }

        [Test]
        public void TheIndexFingerOnAKeyEightMillimetresDownRestsItsPadOnTheKeyTop()
        {
            // Piano-local: the key 8 mm down at the touch point, the tip joint 4 mm above it, tip radius 7.6 mm.
            Vector3 pivot = new Vector3(0f, 0.045f, -0.06f), tip = new Vector3(0f, -0.004f, 0.02f), s = new Vector3(0f, -0.008f, 0f), n = Vector3.up;
            float lift = 0f;
            Vector3 normal = Vector3.zero;
            Vector3 drawn = ContactMath.ClampStep(ref lift, ref normal, Dt, pivot, tip, s, n, 0.0076f, true, out float theta, out Vector3 axis);
            Assert.That(lift, Is.EqualTo(0.0036f).Within(TolM), "lift");
            Assert.That(theta, Is.EqualTo(0.044411f).Within(TolRad), "theta (2.5445 deg)");
            Assert.That(theta * Mathf.Rad2Deg, Is.EqualTo(2.544544f).Within(0.006f));
            Near(axis, new Vector3(-1f, 0f, 0f), 1e-5f, "axis");
            Near(drawn, new Vector3(0f, -0.0004f, 0.022097f), TolM, "corrected tip");
            Assert.That(Vector3.Dot(n, drawn - s), Is.EqualTo(0.0076f).Within(TolM), "exactly rc above the plane");
        }

        [Test]
        public void ThePenetrationLiftRisesThenFadesOut()
        {
            float[,] rows = { { -0.002f, 0f }, { 0f, 0f }, { 0.0036f, 0.0036f }, { 0.010f, 0.010f }, { 0.025f, 0.025f }, { 0.030f, 0.016667f }, { 0.040f, 0f }, { 0.05f, 0f } };
            for (int i = 0; i < rows.GetLength(0); i++)
                Assert.That(ContactMath.PenetrationLift(rows[i, 0]), Is.EqualTo(rows[i, 1]).Within(TolM), "pen " + rows[i, 0]);
        }

        [Test]
        public void TheLiftReleasesWithoutAPopWhenTheSurfaceDisappears()
        {
            Vector3 pivot = new Vector3(0f, 0.045f, -0.06f), tip = new Vector3(0f, -0.004f, 0.02f), s = new Vector3(0f, -0.008f, 0f);
            float lift = 0f;
            Vector3 normal = Vector3.zero;
            ContactMath.ClampStep(ref lift, ref normal, Dt, pivot, tip, s, Vector3.up, 0.0076f, true, out _, out _);
            float[] want = { 0.002308f, 0.00148f, 0.000949f, 0.000608f, 0.00039f, 0.00025f };
            for (int k = 0; k < want.Length; k++)
            {
                Vector3 drawn = ContactMath.ClampStep(ref lift, ref normal, Dt, pivot, tip, s, Vector3.up, 0.0076f, false, out float theta, out _);
                Assert.That(lift, Is.EqualTo(want[k]).Within(TolM), "frame " + (k + 1));
                Assert.That(normal, Is.EqualTo(Vector3.up), "the last normal is kept while the lift decays");
                Assert.That(theta, Is.GreaterThan(0f), "still turned, a little less each frame");
                Assert.That(drawn.y, Is.GreaterThan(tip.y));
            }
        }

        [Test]
        public void TheThumbTurnsSixteenAndAHalfDegrees()
        {
            float lift = 0f;
            Vector3 normal = Vector3.zero;
            Vector3 drawn = ContactMath.ClampStep(ref lift, ref normal, Dt, new Vector3(0.03f, 0.03f, -0.05f), new Vector3(0.035f, -0.006f, 0f),
                Vector3.zero, Vector3.up, 0.0098f, true, out float theta, out _);
            Assert.That(lift, Is.EqualTo(0.0158f).Within(TolM));
            Assert.That(theta * Mathf.Rad2Deg, Is.EqualTo(16.545037f).Within(TolRad * Mathf.Rad2Deg));
            Near(drawn, new Vector3(0.035813f, 0.0098f, 0.008131f), TolM, "thumb tip");
        }

        [Test]
        public void AngleAxisIsTheReferencesRightHandRodrigues()
        {
            // The spec's check: Euler(90, 0, 0) (0, 0, 1) = (0, -1, 0), and Rodrigues about +x by 90 degrees gives the same.
            Near(Quaternion.Euler(90f, 0f, 0f) * Vector3.forward, new Vector3(0f, -1f, 0f), 1e-6f, "Euler");
            Vector3 k = Vector3.right, v = Vector3.forward;
            float th = Mathf.PI / 2f;
            Vector3 rod = v * Mathf.Cos(th) + Vector3.Cross(k, v) * Mathf.Sin(th) + k * Vector3.Dot(k, v) * (1f - Mathf.Cos(th));
            Near(ContactMath.RotateAbout(v, Vector3.zero, k, th), rod, 1e-6f, "AngleAxis = Rodrigues");
        }

        [Test]
        public void TheTurnIsCappedAtThirtyDegreesAndNothingHappensWithoutALift()
        {
            Assert.IsFalse(ContactMath.FingerRotation(Vector3.zero, new Vector3(0f, -0.05f, 0.05f), Vector3.up, 0f, out float t0, out _));
            Assert.That(t0, Is.EqualTo(0f));
            Assert.IsTrue(ContactMath.FingerRotation(Vector3.zero, new Vector3(0f, -0.05f, 0.05f), Vector3.up, 0.06f, out float t1, out _));
            Assert.That(t1, Is.EqualTo(ContactMath.ThetaMax).Within(1e-6f), "30 degrees at most");
            Assert.IsFalse(ContactMath.FingerRotation(Vector3.zero, new Vector3(0f, -0.05f, 0f), Vector3.up, 0.01f, out _, out _),
                "a finger along the normal has no axis");
            Assert.That(ContactMath.PivotJoint, Is.EqualTo(new[] { 3, 7, 12, 17, 22 }), "the proximal (MCP) bones");
        }

        [Test]
        public void AStraightPokeIsNotBentSidewaysByTrackingJitter()
        {
            // A finger poking the menu face (normal +z) almost head-on: pivot 9 cm behind the tip, the tip joint on the face,
            // a 7.6 mm pad radius. Tilted +-0.3 degrees, the unfaded clamp turned it 23 degrees about opposite axes (a 72 mm
            // snap of the drawn tip); within 15 degrees of the normal there is no turn at all.
            Vector3 n = Vector3.forward;
            foreach (float tiltDeg in new[] { 0.3f, -0.3f, 5f, -14f })
            {
                Vector3 dir = Quaternion.AngleAxis(tiltDeg, Vector3.up) * -n;   // pivot -> tip, into the face
                Vector3 pivot = -dir * 0.09f, tip = Vector3.zero;
                float lift = ContactMath.PenetrationLift(0.0076f - Vector3.Dot(n, tip));
                Assert.That(lift, Is.GreaterThan(0f), "the pad is in the face");
                bool turned = ContactMath.FingerRotation(pivot, tip, n, lift, out float theta, out _);
                Assert.IsFalse(turned, $"tilt {tiltDeg} degrees: no turn");
                Assert.That(theta, Is.EqualTo(0f));
                float liftNow = lift;
                Vector3 nn = n;
                Vector3 drawn = ContactMath.ClampStep(ref liftNow, ref nn, 1f / 90f, pivot, tip, Vector3.zero, n, 0.0076f, true, out _, out _);
                Assert.That(Vector3.Distance(drawn, tip), Is.LessThan(1e-6f), $"tilt {tiltDeg}: the drawn tip stays on the poke point");
            }
            // the fade: none at 15 degrees, half way at about 22, all of it at 30 degrees and beyond
            Vector3 Dir(float deg) => Quaternion.AngleAxis(deg, Vector3.up) * -n;
            Assert.That(ContactMath.NormalWeight(Dir(15f), n), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(ContactMath.NormalWeight(Dir(22.5f), n), Is.InRange(0.4f, 0.65f));
            Assert.That(ContactMath.NormalWeight(Dir(30f), n), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ContactMath.NormalWeight(Dir(55f), n), Is.EqualTo(1f), "the 35-degree finger of the PlayMode test and the vectors' fingers (s 0.81-0.85) are untouched");
            // and continuous in between: a 0.3 degree change never moves theta by more than a degree
            float prev = -1f;
            for (float deg = 10f; deg <= 40f; deg += 0.3f)
            {
                Vector3 d = Dir(deg), pivot = -d * 0.09f;
                ContactMath.FingerRotation(pivot, Vector3.zero, n, 0.006f, out float th, out _);
                if (prev >= 0f) Assert.That(Mathf.Abs(th - prev) * Mathf.Rad2Deg, Is.LessThan(1f), $"smooth at {deg:F1} degrees");
                prev = th;
            }
        }
    }
}
