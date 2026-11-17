// The rig's geometry (review items 15 and 19): the big bowl sits nearest the left shoulder, every rim point is within the
// descriptor's reach (0.62 m) and within 5 mm of the 2 ft (0.61 m) seated reach, the rims stay inside the stand's
// footprint, the bowls never touch, and the puja rests clear of the cushions within reach.
using System;
using MusicRoom.Bowls;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsLayoutTests
    {
        static readonly Vector3 Shoulder = new Vector3(-0.18f, 1.05f, 0f);   // seat frame, left (tools/seat-check.mjs)

        static Vector3 World(Vector3 local) => BowlsLayout.HomeSeat + local;

        static void RimReach(int i, out float near, out float far)
        {
            near = 9; far = 0;
            Vector3 c = World(new Vector3(BowlsLayout.BowlXZ[i].x, BowlsLayout.RimY(i), BowlsLayout.BowlXZ[i].y));
            for (int k = 0; k < 360; k++)
            {
                float a = k * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * BowlsLayout.R(i);
                float d = Vector3.Distance(p, Shoulder);
                near = Mathf.Min(near, d); far = Mathf.Max(far, d);
            }
        }

        [Test]
        public void TheBigBowlSitsNearestAndEveryRimIsWithinReach()
        {
            var centre = new float[3];
            for (int i = 0; i < 3; i++)
            {
                RimReach(i, out float near, out float far);
                centre[i] = Vector3.Distance(World(new Vector3(BowlsLayout.BowlXZ[i].x, BowlsLayout.RimY(i), BowlsLayout.BowlXZ[i].y)), Shoulder);
                Debug.Log($"[BOWLS] {BowlsLayout.Key[i]}: rim from the left shoulder {near:F3} .. {far:F3} m");
                Assert.That(far, Is.LessThanOrEqualTo(BowlsLayout.ReachMax), BowlsLayout.Key[i] + " far rim within the descriptor's reach");
                Assert.That(far, Is.LessThanOrEqualTo(0.615f), BowlsLayout.Key[i] + " far rim within 5 mm of the 2 ft seated reach");
            }
            Assert.That(centre[0], Is.LessThan(centre[1]).And.LessThan(centre[2]), "the big bowl is the nearest (review item 15)");
            RimReach(0, out _, out float bigFar);
            Assert.That(bigFar, Is.LessThan(0.5f), "the whole big rim is comfortably reachable");
        }

        [Test]
        public void BowlsFitTheStandAndNeverTouch()
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Mathf.Abs(BowlsLayout.BowlXZ[i].x) + BowlsLayout.R(i), Is.LessThanOrEqualTo(BowlsLayout.StandHx + 1e-6f), "inside the footprint (x)");
                Assert.That(Mathf.Abs(BowlsLayout.BowlXZ[i].y) + BowlsLayout.R(i), Is.LessThanOrEqualTo(BowlsLayout.StandHz + 1e-6f), "inside the footprint (z)");
                for (int j = i + 1; j < 3; j++)
                {
                    float gap = Vector2.Distance(BowlsLayout.BowlXZ[i], BowlsLayout.BowlXZ[j]) - BowlsLayout.R(i) - BowlsLayout.R(j);
                    Debug.Log($"[BOWLS] rim gap {BowlsLayout.Key[i]}-{BowlsLayout.Key[j]} {gap * 1000:F1} mm");
                    Assert.That(gap, Is.GreaterThanOrEqualTo(0.030f), "rims at least 3 cm apart");
                }
            }
        }

        [Test]
        public void PujaRestsClearOfTheCushionsWithinReach()
        {
            Vector3 c = BowlsLayout.PujaRestCentre;
            for (int i = 0; i < 3; i++)
            {
                float cushionR = 0.31f * BowlsLayout.RimDiam[i];
                for (int k = 0; k <= 10; k++)
                {
                    float x = c.x - BowlsLayout.PujaLength / 2 + BowlsLayout.PujaLength * k / 10f;
                    float d = Vector2.Distance(new Vector2(x, c.z), BowlsLayout.BowlXZ[i]);
                    Assert.That(d, Is.GreaterThan(cushionR + BowlsLayout.PujaSuedeRadius), $"puja clear of cushion {i}");
                }
            }
            Assert.That(Mathf.Abs(c.x) + BowlsLayout.PujaLength / 2, Is.LessThanOrEqualTo(BowlsLayout.StandHx), "on the stand");
            Vector3 grip = World(c + new Vector3(BowlsLayout.PujaLength / 2 - BowlsLayout.PujaGrip, 0, 0));
            Assert.That(Vector3.Distance(grip, Shoulder), Is.LessThan(BowlsLayout.ReachMax), "the grip within reach");
        }
    }
}
