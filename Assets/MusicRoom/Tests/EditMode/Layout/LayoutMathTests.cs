// Pure maths of placement and grabbing: oriented boxes, floor rectangles, the wall shift, the seat rectangle, the
// page-turn easing, and the dwell / wrist-yaw trackers driven sample by sample at the reference check's 72 Hz.
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public class LayoutMathTests
    {
        static Obb Box(Vector3 c, Vector3 h, float yawDeg = 0f)
        {
            Matrix4x4 m = Matrix4x4.TRS(c, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            return Obb.FromBounds(new Bounds(Vector3.zero, h * 2f), m);
        }

        [Test]
        public void ObbDistanceIsZeroInsideAndEuclideanOutside()
        {
            Obb b = Box(new Vector3(1f, 0f, 0f), new Vector3(0.5f, 0.25f, 0.1f));
            Assert.That(b.DistanceTo(new Vector3(1.2f, 0.1f, 0f)), Is.Zero);
            Assert.That(b.DistanceTo(new Vector3(2f, 0f, 0f)), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(b.DistanceTo(new Vector3(1.5f + 0.3f, 0.25f + 0.4f, 0f)), Is.EqualTo(0.5f).Within(1e-6f), "3-4-5 off a corner edge");
            Obb r = Box(Vector3.zero, new Vector3(1f, 1f, 0.1f), 90f);
            Assert.That(r.DistanceTo(new Vector3(0.3f, 0f, 0f)), Is.EqualTo(0.2f).Within(1e-5f), "turned 90 degrees, its thin side faces x");
        }

        [Test]
        public void ObbRaycastHitsTheNearFaceAndMissesBeside()
        {
            // The desktop Shift / Alt pick: the distance to the first face along the ray, 0 from inside, none beside it.
            Obb b = Box(new Vector3(0f, 1f, 2f), new Vector3(0.5f, 0.25f, 0.1f));
            Assert.IsTrue(b.Raycast(new Ray(new Vector3(0f, 1f, 0f), Vector3.forward), out float t));
            Assert.That(t, Is.EqualTo(1.9f).Within(1e-5f), "front face at z 1.9");
            Assert.IsFalse(b.Raycast(new Ray(new Vector3(0.6f, 1f, 0f), Vector3.forward), out _), "beside the box");
            Assert.IsFalse(b.Raycast(new Ray(new Vector3(0f, 1f, 0f), Vector3.back), out _), "behind the ray");
            Assert.IsTrue(b.Raycast(new Ray(new Vector3(0.1f, 1f, 2f), Vector3.up), out float inside));
            Assert.That(inside, Is.Zero, "from inside");
            // Turned 90 degrees, the thin side faces x; a diagonal ray from above enters through the top.
            Obb r = Box(Vector3.zero, new Vector3(1f, 0.2f, 0.1f), 90f);
            Assert.IsTrue(r.Raycast(new Ray(new Vector3(-1f, 0f, 0f), Vector3.right), out float tx));
            Assert.That(tx, Is.EqualTo(0.9f).Within(1e-5f));
            Vector3 d = new Vector3(0f, -1f, 1f).normalized;
            Assert.IsTrue(r.Raycast(new Ray(new Vector3(0f, 1.2f, -1.2f), d), out float td));
            Assert.That(td, Is.EqualTo(Mathf.Sqrt(2f)).Within(1e-4f), "through the top face at y 0.2");
        }

        [Test]
        public void ObbFromBoundsKeepsScaleInTheExtents()
        {
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(0f, 1f, 0f), Quaternion.Euler(0f, 30f, 0f), new Vector3(2f, 3f, 4f));
            Obb b = Obb.FromBounds(new Bounds(new Vector3(0.1f, 0f, 0f), new Vector3(1f, 1f, 1f)), m);
            Assert.That(b.H.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(b.H.y, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(b.H.z, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(b.A0.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Vector3.Distance(b.C, m.MultiplyPoint3x4(new Vector3(0.1f, 0f, 0f))), Is.LessThan(1e-5f));
        }

        [Test]
        public void SeparatingAxisTestFindsEveryKindOfSeparation()
        {
            Obb a = Box(Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f));
            Assert.IsTrue(Obb.Intersect(a, Box(new Vector3(0.9f, 0f, 0f), new Vector3(0.5f, 0.5f, 0.5f))), "overlap on x");
            Assert.IsFalse(Obb.Intersect(a, Box(new Vector3(1.1f, 0f, 0f), new Vector3(0.5f, 0.5f, 0.5f))), "apart on a face axis");
            Assert.IsFalse(Obb.Intersect(a, Box(new Vector3(0.9f, 0f, 0f), new Vector3(0.5f, 0.5f, 0.5f)), 0.2f), "within the slop");
            // Two boxes turned 45 degrees about y, corner to corner: only an edge-cross axis... face axis of b separates.
            Obb b = Box(new Vector3(1.3f, 0f, 1.3f), new Vector3(0.5f, 0.5f, 0.5f), 45f);
            Assert.IsFalse(Obb.Intersect(a, b), "a turned box beyond the corner");
            Obb c = Box(new Vector3(0.8f, 0f, 0.8f), new Vector3(0.5f, 0.5f, 0.5f), 45f);
            Assert.IsTrue(Obb.Intersect(a, c), "the turned box's face cuts a's corner");
            // A long thin tilted box whose only separating axis is an edge cross product.
            Obb thinA = Obb.FromBounds(new Bounds(Vector3.zero, new Vector3(2f, 0.02f, 0.02f)), Matrix4x4.identity);
            Obb thinB = Obb.FromBounds(new Bounds(Vector3.zero, new Vector3(0.02f, 0.02f, 2f)), Matrix4x4.Translate(new Vector3(0f, 0.05f, 0f)));
            Assert.IsFalse(Obb.Intersect(thinA, thinB), "crossed rods 5 cm apart in y");
            Obb thinC = Obb.FromBounds(new Bounds(Vector3.zero, new Vector3(0.02f, 0.02f, 2f)), Matrix4x4.Translate(new Vector3(0f, 0.01f, 0f)));
            Assert.IsTrue(Obb.Intersect(thinA, thinC), "crossed rods touching");
        }

        static WorldRect Rect(float x, float z, float hu, float hv, float yawDeg = 0f)
        {
            var o = new WorldRect();
            FloorRects.ToWorld(new LocalRect(0f, 0f, hu, hv), Matrix4x4.TRS(new Vector3(x, 0f, z), Quaternion.Euler(0f, yawDeg, 0f), Vector3.one), ref o);
            return o;
        }

        [Test]
        public void FloorRectangleOverlapGivesTheDepthAndTheNormalFromBToA()
        {
            WorldRect a = Rect(0.9f, 0f, 0.5f, 0.5f), b = Rect(0f, 0f, 0.5f, 0.5f);
            Assert.IsTrue(FloorRects.Overlap(a, b, 0f, out float depth, out float nx, out float nz));
            Assert.That(depth, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(nx, Is.EqualTo(1f).Within(1e-6f), "points from b to a");
            Assert.That(nz, Is.EqualTo(0f).Within(1e-6f));
            Assert.IsFalse(FloorRects.Overlap(Rect(1.05f, 0f, 0.5f, 0.5f), b, 0f), "5 cm apart");
            Assert.IsTrue(FloorRects.Overlap(Rect(1.005f, 0f, 0.5f, 0.5f), b, GrabConstants.Gap), "the gap grows both by 5 mm");
            Assert.IsFalse(FloorRects.Overlap(Rect(0.75f, 0.75f, 0.3f, 0.3f, 45f), Rect(0f, 0f, 0.5f, 0.5f), 0f), "a turned square beyond the corner");
        }

        [Test]
        public void ToWorldCarriesTheRigFrameAndItsScale()
        {
            var o = new WorldRect();
            FloorRects.ToWorld(new LocalRect(0.2f, 0.1f, 0.3f, 0.05f), Matrix4x4.TRS(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 0.85f), ref o);
            Assert.That(o.X, Is.EqualTo(1f + 0.85f * 0.1f).Within(1e-5f), "local z turned 90 degrees points to +x");
            Assert.That(o.Z, Is.EqualTo(2f - 0.85f * 0.2f).Within(1e-5f), "local x turned 90 degrees points to -z");
            Assert.That(o.Hu, Is.EqualTo(0.3f * 0.85f).Within(1e-5f));
            Assert.That(o.Hv, Is.EqualTo(0.05f * 0.85f).Within(1e-5f));
        }

        [Test]
        public void WallShiftIsTheLeastMoveInsideTheRoom()
        {
            // The live room faces the fire (RoomFrame, 2026-11-11/12): the wall behind the seat is at z -1.62, the fireplace wall at +3.58.
            var s = new PlacementSolver();
            Assert.That(s.RoomXMin, Is.EqualTo(-3.2f).Within(1e-5f));
            Assert.That(s.RoomXMax, Is.EqualTo(3.2f).Within(1e-5f));
            Assert.That(s.RoomZMin, Is.EqualTo(-1.62f).Within(1e-5f));
            Assert.That(s.RoomZMax, Is.EqualTo(3.58f).Within(1e-5f));
            Assert.That(s.WallShift(0f, 1f, -1.624f, -0.2f), Is.EqualTo(Vector2.zero), "within WALL_SLOP of the wall behind the seat");
            Vector2 v = s.WallShift(0f, 1f, -1.7f, -0.5f);
            Assert.That(v.y, Is.EqualTo(-1.62f + 1.7f + 0.002f).Within(1e-5f), "in off the wall behind the seat plus 2 mm");
            Assert.That(s.WallShift(0f, 1f, 3.0f, 3.7f).y, Is.EqualTo(3.58f - 3.7f - 0.002f).Within(1e-5f), "in off the fireplace wall plus 2 mm");
            Assert.That(s.WallShift(-3.3f, -2f, 0f, 1f).x, Is.EqualTo(-3.2f + 3.3f + 0.002f).Within(1e-5f), "in off the left wall");
            Assert.That(s.WallShift(-4f, 4f, 0f, 1f).x, Is.EqualTo(0f).Within(1e-5f), "too long for the room: centred");
        }

        [Test]
        public void TheSeatRectangleSitsBehindTheEyesAlongTheHeading()
        {
            var s = new PlacementSolver();
            WorldRect seat = s.SeatRect(new Vector3(1f, 1.2f, 2f), 90f * Mathf.Deg2Rad);
            Assert.That(seat.X, Is.EqualTo(1f - 0.1f).Within(1e-5f), "0.1 m behind the eyes (facing +x)");
            Assert.That(seat.Z, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(seat.Hu, Is.EqualTo(GrabConstants.SeatHalf));
            Assert.That(seat.Hv, Is.EqualTo((GrabConstants.SeatFront + GrabConstants.SeatBack) / 2f));
        }

        [Test]
        public void PageTurnEasingAndAngleWrap()
        {
            Assert.That(LayoutMath.Ease(0f), Is.EqualTo(0f));
            Assert.That(LayoutMath.Ease(0.25f), Is.EqualTo(0.125f).Within(1e-6f));
            Assert.That(LayoutMath.Ease(0.5f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(LayoutMath.Ease(0.75f), Is.EqualTo(0.875f).Within(1e-6f));
            Assert.That(LayoutMath.Ease(1f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(LayoutMath.Wrap(3f * Mathf.PI / 2f), Is.EqualTo(-Mathf.PI / 2f).Within(1e-5f));
            LayoutMath.RotY(0f, 1f, 90f * Mathf.Deg2Rad, out float x, out float z);
            Assert.That(x, Is.EqualTo(1f).Within(1e-6f), "Unity yaw +90 turns forward to the right");
            Assert.That(z, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(Vector3.Distance(LayoutMath.SeatRight(0.3f), Quaternion.Euler(0f, 0.3f * Mathf.Rad2Deg, 0f) * Vector3.right), Is.LessThan(1e-6f));
        }

        [Test]
        public void ConstantsAreTheReferenceValues()
        {
            // Grab, placement, layout and gesture constants (ARCHITECTURE.md section 8).
            Assert.That(GrabConstants.PlayGuardMs, Is.EqualTo(1000f));
            Assert.That(GrabConstants.GrabDwell, Is.EqualTo(0.3f));
            // The one deliberate change: the dwell's stillness, 0.025 m at first, is 0.04 m since the first headset test
            // (ARRANGE-NOTES.md): the index tip keeps moving while a real pinch closes.
            Assert.That(GrabConstants.GrabStillBeforeDeviceTest, Is.EqualTo(0.025f));
            Assert.That(GrabConstants.GrabStill, Is.EqualTo(0.04f));
            Assert.That(GrabConstants.GrabSpeed, Is.EqualTo(0.5f));
            Assert.That(GrabConstants.GrabSmooth, Is.EqualTo(0.06f));
            Assert.That(GrabConstants.SpeedWindow, Is.EqualTo(0.1f));
            Assert.That(GrabConstants.HintAfter, Is.EqualTo(0.05f));
            Assert.That(GrabConstants.DwellSamples, Is.EqualTo(48));
            Assert.That(GrabConstants.HandleReach, Is.EqualTo(0.07f));
            Assert.That(GrabConstants.ZonePad, Is.EqualTo(0.12f));
            Assert.That(GrabConstants.UnstickMs, Is.EqualTo(300f));
            Assert.That(GrabConstants.EyeClear, Is.EqualTo(0.3f));
            Assert.That(GrabConstants.MaxPush, Is.EqualTo(1.6f));
            Assert.That(GrabConstants.TurnSeconds, Is.EqualTo(0.35f));
            Assert.That(PageTurnGesture.SweepDistance, Is.EqualTo(0.15f));
            Assert.That(PageTurnGesture.SweepSpeed, Is.EqualTo(1.2f));
            Assert.That(PageTurnGesture.CooldownSeconds, Is.EqualTo(0.6f));
            Assert.That(LayoutModule.ModuleOrder, Is.EqualTo(100), "ARCHITECTURE.md 3.4");
        }

        // ---------------------------------------------------------------- the dwell

        const float Dt = 1f / 72f;

        /// <summary>Runs a pinch for <paramref name="frames"/> 72 Hz frames at the points <paramref name="at"/> gives; returns
        /// the last stillness sample and the speed at the dwell's end.</summary>
        static void RunDwell(DwellTracker d, int frames, System.Func<int, Vector3> at, out float still, out float speed)
        {
            float pinchFor = Dt;
            d.Start(at(0), pinchFor);
            still = 0f;
            for (int i = 1; i <= frames; i++)
            {
                pinchFor += Dt;
                still = d.Sample(at(i), pinchFor);
            }
            speed = d.Speed();
        }

        [Test]
        public void AStillButJitteryPinchPassesTheDwell()
        {
            var rnd = new Mulberry32(12345);
            Vector3 p = new Vector3(0.3f, 0.8f, 0.4f);
            var d = new DwellTracker();
            float worst = 0f;
            float pinchFor = Dt;
            d.Start(p, pinchFor);
            for (int i = 1; i <= 22; i++)   // 0.3 s at 72 Hz, +-1 cm on every axis every frame (the move check's jitter)
            {
                pinchFor += Dt;
                Vector3 j = new Vector3(rnd.NextFloat() * 2f - 1f, rnd.NextFloat() * 2f - 1f, rnd.NextFloat() * 2f - 1f) * 0.01f;
                worst = Mathf.Max(worst, d.Sample(p + j, pinchFor));
            }
            Assert.That(pinchFor, Is.GreaterThanOrEqualTo(GrabConstants.GrabDwell));
            Assert.That(worst, Is.LessThanOrEqualTo(GrabConstants.GrabStill), "the smoothed point stays where it settled");
            Assert.That(d.Speed(), Is.LessThanOrEqualTo(GrabConstants.GrabSpeed), "and is not moving at the end");
        }

        [Test]
        public void ADwellRejectsAMovingPinch()
        {
            // The move check: "a moving pinch (a stroke at 0.8 m/s) never takes hold".
            Vector3 p = new Vector3(0.3f, 0.8f, 0.4f);
            RunDwell(new DwellTracker(), 22, i => p + new Vector3(0f, 0f, 0.8f * Dt * i), out float still, out _);
            Assert.That(still, Is.GreaterThan(GrabConstants.GrabStill), "it wandered from where it settled");
            // The speed is the smoothed point's, over the last SPEED_WINDOW: a steady 1 m/s reads close to 1 m/s, a still
            // pinch about 0.
            var d = new DwellTracker();
            RunDwell(d, 22, i => p + new Vector3(0f, 0f, 1f * Dt * i), out _, out float speed);
            Assert.That(speed, Is.EqualTo(1f).Within(0.1f));
            RunDwell(d, 22, i => p, out float still0, out float speed0);
            Assert.That(still0, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(speed0, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void DwellSettlesOnTheMeanOfTheFirst80Ms()
        {
            var d = new DwellTracker();
            float t = Dt;
            d.Start(new Vector3(0f, 0f, 0f), t);
            t += Dt; d.Sample(new Vector3(0.01f, 0f, 0f), t);
            t += Dt; d.Sample(new Vector3(0.02f, 0f, 0f), t);
            Assert.That(d.SettledAt.x, Is.EqualTo(0.01f).Within(1e-6f), "running mean while PinchFor <= 0.08");
            for (int i = 0; i < 10; i++) { t += Dt; d.Sample(new Vector3(0.5f, 0f, 0f), t); }
            // Samples at PinchFor 4/72 and 5/72 (<= 0.08 s) join the mean; from 6/72 on they no longer move it.
            Assert.That(d.SettledAt.x, Is.EqualTo((0f + 0.01f + 0.02f + 0.5f + 0.5f) / 5f).Within(1e-6f), "the mean of the first 80 ms only");
            Assert.That(new DwellTracker().Sample(Vector3.zero, 0.1f), Is.EqualTo(float.PositiveInfinity), "no dwell started");
            var fresh = new DwellTracker();
            fresh.Start(Vector3.zero, 0.01f);
            Assert.That(fresh.Speed(), Is.Zero, "under 20 ms of history reads as still");
        }

        // ---------------------------------------------------------------- wrist yaw

        [Test]
        public void WristYawUnwrapsAFullTurn()
        {
            var w = new WristYawTracker();
            Vector3 Fingers(float deg) => Quaternion.Euler(0f, deg, 0f) * new Vector3(0f, -0.3f, 1f).normalized;
            Vector3 Cross(float deg) => Quaternion.Euler(0f, deg, 0f) * Vector3.right * 0.2f;   // nearly vertical cross axis: the fingers win
            w.Read(Fingers(0f), Cross(0f));
            for (int i = 1; i <= 72; i++) w.Read(Fingers(i * 5f), Cross(i * 5f));
            Assert.That(w.Accumulated, Is.EqualTo(2f * Mathf.PI).Within(1e-3f), "360 degrees, no jump at the half turn");
            float before = w.Accumulated;
            w.Read(new Vector3(0f, 1f, 0.05f), new Vector3(0.1f, 0f, 0f));   // fingers up, cross axis short: no heading
            Assert.That(w.Accumulated, Is.EqualTo(before));
        }

        [Test]
        public void WristYawSwitchesAxisWithoutAJump()
        {
            var w = new WristYawTracker();
            // Palm down: the fingers lie flat and lead.
            w.Read(new Vector3(0f, 0f, 1f), new Vector3(0.7f, 0.7f, 0f));
            float a0 = w.Accumulated;
            // Fingers turn up (their floor projection shrinks) while the cross axis flattens: the reading moves to the
            // cross axis, carrying the angle on.
            w.Read(new Vector3(0f, 0.95f, 0.3f), new Vector3(1f, 0f, 0f));
            Assert.That(w.Accumulated, Is.EqualTo(a0).Within(1e-6f), "an axis change itself never turns the rig");
            w.Read(new Vector3(0f, 0.95f, 0.3f), Quaternion.Euler(0f, 20f, 0f) * new Vector3(1f, 0f, 0f));
            Assert.That(w.Accumulated - a0, Is.EqualTo(20f * Mathf.Deg2Rad).Within(1e-4f), "then turns with the new axis");
        }
    }
}
