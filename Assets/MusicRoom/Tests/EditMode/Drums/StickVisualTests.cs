// E2 of 10-upgrade/experience.md section 4: the drawn stick pivots about the grip and rebounds, against both frame tables
// of the experience lab's experience.vectors.json (stickVisual; grip fixed at (0, 0.12, -0.27), head plane y = 0, the raw
// stick turned about the grip so its tip follows the given height; dt = 1/90; tolerance 1e-4). Plus the kit's own
// surface choice for a held stick (it must come down onto a piece from above) and the Visual child that alone turns.
using System;
using MusicRoom.Core;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class StickVisualTests
    {
        const float Dt = 1f / 90f, Tol = 1e-4f;
        static readonly Vector3 Grip = new Vector3(0f, 0.12f, -0.27f);

        // rawTipY, hit speed, phiC, phi, drawn tip y (experience.vectors.json stickVisual.overshoot.frames).
        static readonly float[][] Overshoot =
        {
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
            new[] { 0.072222f, 0f, 0f, 0f, 0.072222f },
            new[] { 0.044444f, 0f, 0f, 0f, 0.044444f },
            new[] { 0.016667f, 0f, 0f, 0f, 0.016667f },
            new[] { -0.011111f, 2.5f, 0.071967f, 0.071967f, 0.0062f },
            new[] { -0.038889f, 0f, 0.194096f, 0.266003f, 0.024085f },
            new[] { -0.06f, 0f, 0.294649f, 0.378733f, 0.027165f },
            new[] { -0.06f, 0f, 0.294649f, 0.365648f, 0.023856f },
            new[] { -0.06f, 0f, 0.294649f, 0.34572f, 0.018847f },
            new[] { -0.051f, 0f, 0.250775f, 0.283514f, 0.014276f },
            new[] { -0.034333f, 0f, 0.173382f, 0.19226f, 0.010842f },
            new[] { -0.017667f, 0f, 0.099963f, 0.109596f, 0.008564f },
            new[] { -0.001f, 0f, 0.029614f, 0.033706f, 0.007203f },
            new[] { 0.015667f, 0f, 0f, 0.001124f, 0.015947f },
            new[] { 0.032333f, 0f, 0f, 0f, 0.032333f },
            new[] { 0.049f, 0f, 0f, 0f, 0.049f },
            new[] { 0.065667f, 0f, 0f, 0f, 0.065667f },
            new[] { 0.082333f, 0f, 0f, 0f, 0.082333f },
            new[] { 0.099f, 0f, 0f, 0f, 0.099f },
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
        };

        // stickVisual.reboundStroke.frames.
        static readonly float[][] Rebound =
        {
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
            new[] { 0.072222f, 0f, 0f, 0f, 0.072222f },
            new[] { 0.044444f, 0f, 0f, 0f, 0.044444f },
            new[] { 0.016667f, 0f, 0f, 0f, 0.016667f },
            new[] { -0.009111f, 2.32f, 0.063513f, 0.063513f, 0.0062f },
            new[] { 0.013111f, 0f, 0f, 0.06673f, 0.029882f },
            new[] { 0.035333f, 0f, 0f, 0.07803f, 0.055576f },
            new[] { 0.057556f, 0f, 0f, 0.065887f, 0.074986f },
            new[] { 0.079778f, 0f, 0f, 0.047393f, 0.092472f },
            new[] { 0.1f, 0f, 0f, 0.030383f, 0.108189f },
            new[] { 0.1f, 0f, 0f, 0.017518f, 0.10472f },
            new[] { 0.1f, 0f, 0f, 0.00894f, 0.102408f },
            new[] { 0.1f, 0f, 0f, 0.003797f, 0.101022f },
            new[] { 0.1f, 0f, 0f, 0.001043f, 0.100281f },
            new[] { 0.1f, 0f, 0f, 0f, 0.1f },
        };

        /// make-vectors.mjs tipAt: the raw stick turned about the grip so the tip is at height y.
        static Vector3 TipAt(float y)
        {
            float s = Mathf.Clamp((Grip.y - y) / StickVisualMath.L, -1f, 1f), a = Mathf.Asin(s);
            return new Vector3(0f, Grip.y - StickVisualMath.L * Mathf.Sin(a), Grip.z + StickVisualMath.L * Mathf.Cos(a));
        }

        static float OvershootY(float t)
        {
            const float t1 = 0.064f;
            if (t <= t1) return 0.10f - 2.5f * t;
            if (t <= t1 + 0.03f) return -0.06f;
            return Mathf.Min(0.10f, -0.06f + 1.5f * (t - t1 - 0.03f));
        }

        static float ReboundY(float t)
        {
            const float t1 = 0.044f;
            return t <= t1 ? 0.10f - 2.5f * t : Mathf.Min(0.10f, -0.01f + 2.0f * (t - t1));
        }

        static void Run(float[][] table, Func<float, float> y, string name)
        {
            var st = new StickVisualState();
            float prevY = TipAt(y(0f)).y;
            bool hitDone = false;
            for (int f = 0; f < table.Length; f++)
            {
                float t = f * Dt;
                Vector3 tip = TipAt(y(t));
                float hit = 0f;
                if (!hitDone && prevY > 0f && tip.y <= 0f) { hit = (prevY - tip.y) / Dt; hitDone = true; }
                float phi = StickVisualMath.Step(ref st, Dt, Grip, tip, true, Vector3.zero, Vector3.up, hit, out float phiC, out Vector3 axis);
                Vector3 drawn = StickVisualMath.DrawnTip(Grip, tip, axis, phi);
                float[] w = table[f];
                string at = $"{name} f{f}";
                Assert.That(tip.y, Is.EqualTo(w[0]).Within(Tol), at + " raw tip");
                Assert.That(hit, Is.EqualTo(w[1]).Within(1e-3f), at + " hit speed");
                Assert.That(phiC, Is.EqualTo(w[2]).Within(Tol), at + " phiC");
                Assert.That(phi, Is.EqualTo(w[3]).Within(Tol), at + " phi");
                Assert.That(drawn.y, Is.EqualTo(w[4]).Within(Tol), at + " drawn tip");
                prevY = tip.y;
            }
        }

        [Test]
        public void AnOvershootingStrokeIsDrawnOnTheHeadAndBouncesOff() => Run(Overshoot, OvershootY, "overshoot");

        [Test]
        public void AGoodReboundStrokeShowsTheBounce() => Run(Rebound, ReboundY, "rebound");

        [Test]
        public void VeryDeepDipsFadeTheCorrectionOut()
        {
            var st = new StickVisualState();
            // 15 cm through the head: phiC is past the 0.45 rad fade end, so the stick is drawn as tracked.
            float phi = StickVisualMath.Step(ref st, Dt, Grip, TipAt(-0.15f), true, Vector3.zero, Vector3.up, 0f, out float phiC, out _);
            Assert.That(phiC, Is.GreaterThan(StickVisualMath.FadeTo));
            Assert.That(phi, Is.EqualTo(0f));
            Assert.That(StickVisualMath.L, Is.EqualTo(0.27f).Within(1e-6f));
        }

        [Test]
        public void OnlyTheVisualChildTurnsAboutTheGripAndTheSurfaceIsTheOneTheTipCameDownOnto()
        {
            using (var k = new KitFixture())
            {
                Drumstick s = k.Sticks[0];
                Assert.That(s.Visual, Is.Not.Null);
                Assert.That(s.Visual.parent, Is.SameAs(s.Root));
                foreach (var r in s.Renderers) Assert.That(r.transform.parent, Is.SameAs(s.Visual), r.name + " hangs under Visual");

                // Hold the stick by hand: grip 22 cm above the snare head, pointing down at it.
                DrumPiece snare = k.Piece(DrumSpec.SnareId);
                var h = new HandState(Handedness.Right);
                s.Hand = h;
                h.Holding = s;
                Vector3 head = snare.Group.position;
                Vector3 axis = (new Vector3(0f, -0.25f, 1f)).normalized;
                void Pose(float tipHeight)
                {
                    // grip + axis * .27 lands at tipHeight over the head centre
                    Vector3 tip = head + Vector3.up * tipHeight;
                    Vector3 grip = tip - axis * StickVisualMath.L;
                    DrumMath.StickPose(grip, axis, out Vector3 butt, out Quaternion rot, out _, out _);
                    s.Root.SetPositionAndRotation(butt, rot);
                }
                Pose(0.05f);
                k.Sticks.UpdateVisuals(Dt, k.Kit);
                Assert.That(s.SurfacePiece, Is.EqualTo(DrumSpec.SnareId), "over the snare");
                Assert.That(s.Phi, Is.EqualTo(0f));
                Pose(-0.02f);
                Vector3 rootPos = s.Root.position;
                Quaternion rootRot = s.Root.rotation;
                k.Sticks.UpdateVisuals(Dt, k.Kit);
                Assert.That(s.PhiC, Is.GreaterThan(0f), "2 cm through the head: lifted");
                Assert.That(s.Root.position, Is.EqualTo(rootPos), "Root keeps the raw pose");
                Assert.That(Quaternion.Angle(s.Root.rotation, rootRot), Is.LessThan(1e-4f));
                float drawnH = Vector3.Dot(snare.Group.up, s.VisualTipWorld - head) - DrumSpec.HeadLift;
                Assert.That(drawnH, Is.EqualTo(StickSpec.TipRadius).Within(2e-4f), "the drawn ball sits on the head as drawn (1 mm up)");
                Assert.That(Vector3.Distance(s.Visual.TransformPoint(new Vector3(0f, StickSpec.GripFromButt, 0f)), s.GripWorld), Is.LessThan(1e-5f),
                    "turned about the grip");

                Span<Vector4> spheres = stackalloc Vector4[4];
                Assert.That(k.Kit.StickShadowSpheres(spheres), Is.EqualTo(1), "one held stick");
                Assert.That(Vector3.Distance(new Vector3(spheres[0].x, spheres[0].y, spheres[0].z), s.VisualTipWorld), Is.LessThan(1e-6f));
                Assert.That(spheres[0].w, Is.EqualTo(StickSpec.TipRadius));

                // Let go: the stick is drawn as it lies.
                s.Hand = null;
                h.Holding = null;
                k.Sticks.UpdateVisuals(Dt, k.Kit);
                Assert.That(s.Visual.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(s.Visual.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(k.Kit.StickShadowSpheres(spheres), Is.EqualTo(0), "no shadow for a resting stick");
            }
        }

        [Test]
        public void AStickUnderACymbalIsNeverLiftedThroughIt()
        {
            using (var k = new KitFixture())
            {
                Drumstick s = k.Sticks[0];
                s.Hand = new HandState(Handedness.Right);
                DrumPiece crash = k.Piece(DrumSpec.CrashId);
                Transform plate = DrumKit.SurfaceOf(crash);
                // The tip 3 cm under the crash plate, inside its footprint, with no piece under it within reach: never
                // clamped onto the crash it did not come down onto.
                Vector3 tip = plate.position - plate.up * 0.03f;
                s.SurfacePiece = -1;
                bool has = k.Kit.StickSurface(s, tip, out _, out _);
                Assert.That(has && s.SurfacePiece == DrumSpec.CrashId, Is.False, "not the cymbal above the tip");
                // Coming down from above the plate it is the crash's.
                Assert.IsTrue(k.Kit.StickSurface(s, plate.position + plate.up * 0.02f, out _, out Vector3 n));
                Assert.That(s.SurfacePiece, Is.EqualTo(DrumSpec.CrashId));
                Assert.That(Vector3.Dot(n, plate.up), Is.GreaterThan(0.9999f));
                Assert.IsTrue(k.Kit.StickSurface(s, plate.position - plate.up * 0.01f, out _, out _), "1 cm through the plate");
                Assert.That(s.SurfacePiece, Is.EqualTo(DrumSpec.CrashId), "still the crash it came down onto");
            }
        }
    }
}
