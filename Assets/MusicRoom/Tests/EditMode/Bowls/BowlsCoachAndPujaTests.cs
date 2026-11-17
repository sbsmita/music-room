// The coach's signals and markers from scripted hands (the real hand logic and the real unit), and the puja's pick-up,
// hold and release rules (the drumsticks', with Meta's pinch holding through the ~17 mm fingertip-joint gap).
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio.Burst;
using MusicRoom.Bowls;
using MusicRoom.Tests.Audio;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsCoachAndPujaTests
    {
        sealed class RigStats : IBowlsStats
        {
            public BowlsRig Rig;
            public unsafe float SingVelocity(int b) => Rig.B(b)->StSingVel;
            public unsafe float ContactLost(int b) => Rig.Core.ContactLost(b);
            public unsafe float Level(int b) => Rig.B(b)->StRms;
        }

        /// <summary>Hand logic, unit and coach together, frame by frame (what BowlsModule.Tick will do): the coach takes the
        /// hand events of each frame and the unit's stats as the main thread would read them.</summary>
        static void LiveCoach(Placement L, double seconds, Func<double, SimHand[]> hands, out List<BowlsSingMarker> markers, out List<BowlsSignal> signals)
        {
            BowlsCoach coach = null; RigStats stats = null; int seen = 0; double last = 0;
            var r = Simulate(new Opts
            {
                Layout = L, Seconds = seconds, HandsAt = hands,
                OnFrame = (t, ix, res) =>
                {
                    if (coach == null) { coach = new BowlsCoach(ix); stats = new RigStats { Rig = res.Rig }; }
                    while (seen < res.Events.Count) coach.OnHandEvent(res.Events[seen++]);
                    res.Rig.Core.Tick(t);
                    coach.Tick(t, t - last, stats); last = t;
                },
            });
            markers = coach.Markers; signals = coach.Signals;
            r.Rig?.Dispose();
        }

        [Test]
        public void AFirstTimerRubRaisesSangAndSingMarkers()
        {
            LiveCoach(Placement.App, 6.6, Hand(Circle(Placement.App, 0)), out var markers, out var signals);
            Assert.That(signals.Any(s => s.Name == "bowls.sang" && s.Bowl == 0), Is.True, "the big bowl sang: " + string.Join(",", signals.Select(s => s.Name)));
            Assert.That(signals.Any(s => s.Name == "bowls.rubOuter"), Is.True);
            Assert.That(markers.Count(m => m.Bowl == 0), Is.GreaterThanOrEqualTo(4), "a bowlSing marker each second of singing");
            double best = markers.Where(m => m.Bowl == 0).Max(m => m.Seconds);
            Assert.That(best, Is.GreaterThanOrEqualTo(3), "a 3 s rub lesson target is reachable");
            Assert.That(signals.Any(s => s.Name == "bowls.slowStart"), Is.False, "it sang within 4 s");
        }

        [Test]
        public void RubbingTheInsideRaisesInner()
        {
            var g = Placed(Placement.App)[1]; double A0 = Approach(Placement.App, 1), w = g.Wall;
            Func<double, double3?> p = t => { double a = A0 + 2 * Math.PI * 0.7 * Math.Max(0, t - 0.3), rr = g.R - w - TIP_R + 0.004 + Math.Max(0, 0.3 - t) * -0.1; return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H + (t < 0.25 ? 0.03 - t * 0.12 : -0.012), g.C.z + rr * Math.Sin(a)); };
            LiveCoach(Placement.App, 3.5, Hand(p), out _, out var signals);
            Assert.That(signals.Any(s => s.Name == "bowls.inner" && s.Bowl == 1), Is.True, string.Join(",", signals.Select(s => s.Name)));
            Assert.That(signals.Any(s => s.Name == "bowls.sang"), Is.False);
        }

        [Test]
        public void ThreeTapsAndNoRubRaiseTapped3TwoSecondsLater()
        {
            Func<double, double3?> taps = t => { foreach (double t0 in new[] { 0.5, 1.2, 1.9 }) if (t >= t0 - 0.1 && t < t0 + 0.25) return Poke(Placement.App, 0, v: 0.6, over: 0.01, t0: t0 - 0.08)(t); return null; };
            LiveCoach(Placement.App, 4.5, Hand(taps), out _, out var signals);
            var s = signals.FirstOrDefault(q => q.Name == "bowls.tapped3");
            Assert.That(s.Name, Is.EqualTo("bowls.tapped3"), string.Join(",", signals.Select(q => q.Name)));
            Assert.That(s.T, Is.InRange(3.6, 4.2), "2 s after the 3rd tap");
        }

        [Test]
        public void ALipRubThatSingsRaisesLipSang()
        {
            var g = Placed(Placement.App)[0]; double A0 = Approach(Placement.App, 0);
            Func<double, double3?> p = t =>
            {
                if (t > 6.4) return null;
                double a = A0 + 2 * Math.PI * 0.7 * Math.Max(0, t - 0.4), rr = g.R - g.Wall * 0.5;
                return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H + TIP_R - 0.002 + (t < 0.4 ? Math.Min(0.03, (0.4 - t) * 0.1) : 0), g.C.z + rr * Math.Sin(a));
            };
            LiveCoach(Placement.App, 6.6, Hand(p), out _, out var signals);
            Assert.That(signals.Any(s => s.Name == "bowls.lipSang"), Is.True, string.Join(",", signals.Select(q => q.Name)));
        }

        // ---------------------------------------------------------------- the puja

        static BowlsPuja Puja(out Matrix4x4 rig)
        {
            var p = new BowlsPuja();
            rig = Matrix4x4.TRS(BowlsLayout.HomeSeat, Quaternion.identity, Vector3.one);
            p.SetRig(rig);
            return p;
        }

        [Test]
        public void PinchNearTheStickPicksItUp()
        {
            var p = Puja(out var rig);
            Vector3 grip = p.GripWorld;
            Assert.That(p.TryPickUp(0, false, grip + new Vector3(0, 0.15f, 0)), Is.EqualTo(BowlsPick.None), "15 cm away: nothing");
            Assert.That(p.TryPickUp(0, false, grip + new Vector3(0, 0.08f, 0)), Is.EqualTo(BowlsPick.Near), "8 cm: the pinch is consumed");
            Assert.That(p.TryPickUp(0, true, grip), Is.EqualTo(BowlsPick.None), "a hand already holding a tool");
            Assert.That(p.TryPickUp(0, false, grip + new Vector3(0, 0.03f, 0)), Is.EqualTo(BowlsPick.Grabbed), "3 cm: picked up");
            Assert.That(p.Held, Is.True);
            Assert.That(p.NearestFree(grip), Is.EqualTo(float.PositiveInfinity), "a held stick is not free");
            Assert.That(p.TryPickUp(1, false, grip), Is.EqualTo(BowlsPick.None), "the other hand cannot take it");
        }

        [Test]
        public void HeldStickFollowsTheHandAndIsLetGoOnlyWhenTheHandOpens()
        {
            var p = Puja(out _);
            p.TryPickUp(0, false, p.GripWorld);
            Vector3 wrist = new Vector3(-0.3f, 0.85f, 0.2f); Quaternion wr = Quaternion.LookRotation(Vector3.down + Vector3.forward * 0.3f, Vector3.forward);
            Vector3 thumb = wrist + new Vector3(0.0f, -0.09f, 0.03f), index = thumb + new Vector3(0.017f, 0, 0);   // tips touching: joints 17 mm apart
            for (int k = 0; k < 30; k++) Assert.That(p.UpdateHeld(1 / 72f, true, wrist, wr, thumb, index, false, false), Is.False, "a 17 mm joint gap is a closed pinch");
            Vector3 grip = (thumb + index) * 0.5f;
            Assert.That(Vector3.Distance(p.GripWorld, grip), Is.LessThan(0.001f), "the grip is at the pinch point");
            p.Ends(out Vector3 suede, out Vector3 plain);
            Assert.That(Vector3.Dot((suede - plain).normalized, (grip - wrist).normalized), Is.GreaterThan(0.9f), "suede end forward along the hand");
            // the fingers part (6 cm) but Meta's pinch still holds: kept
            Vector3 open = thumb + new Vector3(0.06f, 0, 0);
            for (int k = 0; k < 40; k++) Assert.That(p.UpdateHeld(1 / 72f, true, wrist, wr, thumb, open, true, false), Is.False, "Meta's pinch holds it");
            // open hand, no pinch, no fist: let go after 0.25 s
            bool released = false; int frames = 0;
            while (!released && frames < 100) { released = p.UpdateHeld(1 / 72f, true, wrist, wr, thumb, open, false, false); frames++; }
            Assert.That(released, Is.True);
            Assert.That(frames / 72f, Is.InRange(0.24f, 0.30f), "after 0.25 s open");
            Assert.That(p.Settle, Is.EqualTo(0f));
            for (int k = 0; k < 40; k++) p.UpdateHeld(1 / 72f, false, default, default, default, default, false, false);
            Assert.That(p.Settle, Is.EqualTo(1f), "settled back onto the stand in 0.4 s");
            Assert.That(p.PoseRigLocal.HasValue, Is.False, "at rest again");
        }

        [Test]
        public void TrackingLossKeepsTheStickForOneAndAHalfSeconds()
        {
            var p = Puja(out _);
            p.TryPickUp(1, false, p.GripWorld);
            for (int k = 0; k < 100; k++) Assert.That(p.UpdateHeld(0.01f, false, default, default, default, default, false, false), Is.False);
            Assert.That(p.Held, Is.True, "1.0 s lost: still held");
            bool rel = false; for (int k = 0; k < 60 && !rel; k++) rel = p.UpdateHeld(0.01f, false, default, default, default, default, false, false);
            Assert.That(rel, Is.True, "dropped after 1.5 s");
        }
    }
}
