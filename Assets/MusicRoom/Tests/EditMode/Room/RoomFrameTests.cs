// The VR room faces the fire (2026-11-11, "in music room VR i want to inverse my position. I should see the fire and whole
// room."): Core's RoomFrame turns the room a half turn about the seat and slides it 0.5 m back (2026-11-12). One source of truth: the room box every planner and
// solver uses, the window and fire positions, the furniture conversion, the clamp for grabbed rigs and the fixed sound
// sources all come from it; the seat planner's choices for every reference set stay what they were and the default set
// lands exactly at today's homes; no instrument prefers a rear slot, and the 1.62 m behind the seat changes no plan.
using System.Collections.Generic;
using System.Text;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Layout;
using MusicRoom.Room.World;
using NUnit.Framework;
using UnityEngine;
using V = MusicRoom.Tests.Layout.PlatformVectors;

namespace MusicRoom.Tests.Room
{
    public class RoomFrameTests
    {
        static void Near(Vector3 a, Vector3 b, string what, float tol = 1e-5f) =>
            Assert.That(Vector3.Distance(a, b), Is.LessThan(tol), $"{what}: {a:F4} vs {b:F4}");

        [Test]
        public void TheRoomIsAHalfTurnAboutTheSeat()
        {
            Assert.That(RoomFrame.Yaw, Is.EqualTo(180f));
            Assert.That(RoomFrame.Position, Is.EqualTo(new Vector3(0f, 0f, -0.5f)), "slid 0.5 m back: room behind the seat for the drum kit");
            Assert.That(RoomFrame.Cos, Is.EqualTo(-1.0), "exact");
            Assert.That(RoomFrame.Sin, Is.EqualTo(0.0), "exact");
            Near(RoomFrame.Rotation * Vector3.forward, Vector3.back, "the room's forward");
            Near(RoomFrame.ToWorld(Vector3.zero), new Vector3(0f, 0f, -0.5f), "the room frame's origin, behind the seat");
            // The fireplace (room frame (0.3, 0, -4.08), behind the old seat) is now straight ahead, the window on the left.
            Vector3 fire = RoomFrame.ToWorld(new Vector3(RoomDims.FireX, 0f, -RoomDims.ZFront));
            Near(fire, new Vector3(-0.3f, 0f, 3.58f), "the fireplace ahead");
            Near(RoomDims.WindowWorld, new Vector3(-3.18f, 1.55f, 0.68f), "the window on the left wall");
            Near(RoomFrame.DirToWorld(Vector3.right), Vector3.left, "room +x (the window wall) is the world's left");
            // Round trips.
            var p = new Vector3(1.3f, 0.7f, -2.2f);
            Near(RoomFrame.ToRoom(RoomFrame.ToWorld(p)), p, "point round trip");
            RoomFrame.ToWorld(1.25, -3.5, out double wx, out double wz);
            Assert.That(wx, Is.EqualTo(-1.25)); Assert.That(wz, Is.EqualTo(3.0).Within(1e-12));
            RoomFrame.ToRoom(wx, wz, out double rx, out double rz);
            Assert.That(rx, Is.EqualTo(1.25)); Assert.That(rz, Is.EqualTo(-3.5).Within(1e-12));
            Assert.That(RoomFrame.YawToWorld(90.0), Is.EqualTo(-90.0));
            Assert.That(RoomFrame.YawToRoom(RoomFrame.YawToWorld(-37.5)), Is.EqualTo(-37.5).Within(1e-12));
        }

        [Test]
        public void EveryRoomBoxIsTheTurnedOne()
        {
            // The room frame keeps the reference's box; the world's is the turned one, and everything reads it.
            Assert.That(RoomFrame.LocalBox.min, Is.EqualTo(new Vector3(-3.2f, 0f, -4.08f)).Using(new Vec3Tol(1e-5f)));
            Assert.That(RoomFrame.LocalBox.max, Is.EqualTo(new Vector3(3.2f, 2.9f, 1.12f)).Using(new Vec3Tol(1e-5f)));
            Assert.That(RoomFrame.WorldBox.min, Is.EqualTo(new Vector3(-3.2f, 0f, -1.62f)).Using(new Vec3Tol(1e-5f)), "1.62 m behind the seat");
            Assert.That(RoomFrame.WorldBox.max, Is.EqualTo(new Vector3(3.2f, 2.9f, 3.58f)).Using(new Vec3Tol(1e-5f)), "the fire wall 3.58 m ahead");
            Assert.That(GrabConstants.RoomBox, Is.EqualTo(RoomFrame.WorldBox), "GrabConstants.RoomBox");
            Assert.That(SeatPlanner.RoomX0, Is.EqualTo(-3.2)); Assert.That(SeatPlanner.RoomX1, Is.EqualTo(3.2));
            Assert.That(SeatPlanner.RoomZ0, Is.EqualTo(-1.62).Within(1e-12), "the planner's box (ROOM_SEAT)");
            Assert.That(SeatPlanner.RoomZ1, Is.EqualTo(3.58).Within(1e-12));
            var solver = new PlacementSolver();
            Assert.That(solver.RoomZMin, Is.EqualTo(-1.62f).Within(1e-5f)); Assert.That(solver.RoomZMax, Is.EqualTo(3.58f).Within(1e-5f));
            Rect clamp = GrabConstants.OriginClamp;
            Assert.That(clamp.xMin, Is.EqualTo(-2.8f).Within(1e-5f)); Assert.That(clamp.xMax, Is.EqualTo(2.8f).Within(1e-5f));
            Assert.That(clamp.yMin, Is.EqualTo(-1.3f).Within(1e-5f), "grabbed rigs stop 0.32 m from the wall behind");
            Assert.That(clamp.yMax, Is.EqualTo(3.1f).Within(1e-5f), "and 0.48 m from the fireplace wall");
            Near(AudioModule.VrRoomCenter, RoomFrame.WorldBox.center, "the acoustic box");
            Near(AudioModule.VrRoomSize, RoomFrame.WorldBox.size, "its size");
            Assert.That(AudioModule.FirePosition.z, Is.GreaterThan(3.0f), "the fire sounds from ahead");
            Assert.That(AudioModule.SnowPosition.x, Is.LessThan(-3f), "the snow at the window, on the left");
            Assert.That(AudioModule.WindPosition.x, Is.LessThan(-3f));
        }

        [Test]
        public void FurnitureTurnsAsABox()
        {
            var loveseat = new FurnitureBlocker("loveseat", 1.65f, -2.83f, 0.63f, 0.43f, 0f, 0.85f, 48.7f);
            FurnitureBlocker w = RoomFrame.ToWorld(loveseat);
            Assert.That(w.Centre.x, Is.EqualTo(-1.65f).Within(1e-6f)); Assert.That(w.Centre.y, Is.EqualTo(2.33f).Within(1e-5f));
            Assert.That(w.Yaw, Is.EqualTo(48.7f).Within(1e-4f), "a box turned by a half turn is itself: yaw kept modulo 180");
            Assert.That(w.Half, Is.EqualTo(loveseat.Half)); Assert.That(w.Y0, Is.EqualTo(0f)); Assert.That(w.Y1, Is.EqualTo(0.85f));
            FurnitureBlocker shelf = RoomFrame.ToWorld(new FurnitureBlocker("bookshelf", -3.03f, -2.68f, 0.17f, 0.56f, 0f, 1.93f));
            Assert.That(shelf.Yaw, Is.EqualTo(0f), "axis-aligned stays axis-aligned");
            Assert.That(Vector2.Distance(shelf.Centre, new Vector2(3.03f, 2.18f)), Is.LessThan(1e-5f));
            // The fallback reference furniture: turned, but for the bench, which stays at the seat.
            foreach (FurnitureBlocker b in StowPlan.ReferenceFurnitureWorld)
                if (b.Name == "bench") Assert.That(b.Centre, Is.EqualTo(new Vector2(0f, -0.10f)), "the bench at the seat");
                else if (b.Name == "hearth") Assert.That(b.Centre.y, Is.GreaterThan(3.0f), "the hearth ahead");
        }

        [Test]
        public void TheSeatPlansWhatItPlannedAndTheDefaultSetLandsAtItsHomes()
        {
            SeatPlanner live = SeatPlanner.FromRegistry(InstrumentRegistry.Discover());
            SeatPlanner.Plan p = live.PlanSet(new List<InstrumentKind> { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp });
            Assert.IsTrue(p.Fits, "the default set fits the turned room");
            Assert.IsFalse(p.TableAlt, "the side table at its home");
            foreach (SeatPlanner.Member m in p.Members)
            {
                SeatPlanner.Inst inst = live.Get(m.Kind);
                Assert.That(m.Push, Is.EqualTo(0.0), m.Kind + " not pushed");
                Assert.That(m.Pose.X, Is.EqualTo(inst.Home.X).Within(1e-9), m.Kind + " x at home");
                Assert.That(m.Pose.Z, Is.EqualTo(inst.Home.Z).Within(1e-9), m.Kind + " z at home");
                Assert.That(m.Pose.Yaw, Is.EqualTo(inst.Home.Yaw).Within(1e-9), m.Kind + " yaw at home");
            }
        }

        [Test]
        public void EveryPreferredSlotFitsAndNoInstrumentPrefersARearSlot()
        {
            // Every candidate the planner tries (each instrument, each preferred slot, each push) stays inside the turned
            // room: the side slots reach at most ~0.96 m behind the head (the drums at Right), the wall behind is at 1.62 m.
            // The rear slots (+-120 deg) are in no instrument's preferences, so the shorter space behind changes no plan
            // (the reference sets all plan as before: SeatPlannerTests; only their candidate counts changed).
            SeatPlanner live = SeatPlanner.FromRegistry(InstrumentRegistry.Discover());
            var log = new StringBuilder();
            double worstBack = 0;
            foreach (InstrumentDescriptor d in InstrumentRegistry.Discover().All)
            {
                SeatPlanner.Inst inst = live.Get(d.Kind);
                if (inst == null) continue;
                foreach (SeatSlot slot in inst.Prefs)
                {
                    Assert.IsFalse(SeatSlots.IsRear(slot), d.Id + " prefers no rear slot");
                    foreach (double push in SeatPlanner.Pushes)
                    {
                        double dh = SeatSlots.Heading(slot) - SeatSlots.Heading(inst.Canonical);
                        SeatPlanner.Pose2 pose = SeatPlanner.RotatePose(inst.Home, dh);
                        double a = SeatSlots.Heading(slot) * SeatPlanner.Deg;
                        pose.X += push * System.Math.Sin(a); pose.Z += push * System.Math.Cos(a);
                        foreach (SeatPlanner.Box b in inst.Boxes)
                        {
                            if (b.Air) continue;
                            SeatPlanner.FloorObb o = SeatPlanner.WorldBox(pose, b);
                            Assert.IsTrue(SeatPlanner.InsideRoom(o), $"{d.Id} at {slot} +{push:F2}: inside the turned room");
                            SeatPlanner.Extent(o, out _, out _, out double z0, out _);
                            worstBack = System.Math.Min(worstBack, z0);
                        }
                    }
                }
                log.Append(d.Id).Append(' ');
            }
            TestContext.WriteLine($"[Room] turned room: every preferred slot inside; the farthest box behind the head at z {worstBack:F3} (wall {SeatPlanner.RoomZ0 + SeatPlanner.RoomInset:F2}); {log}");
            Assert.That(worstBack, Is.GreaterThan(SeatPlanner.RoomZ0 + SeatPlanner.RoomInset));
        }

        [Test]
        public void TheReferenceSetsStillPlanTheSame()
        {
            // platform.vectors.json was re-made with the turned ROOM (seat-planner.mjs): every set fits or fails as before, with
            // the same members, slots, pushes and poses; SeatPlannerTests replays it. Spot check here: the default three.
            SeatPlanner planner = V.Planner();
            SeatPlanner.Plan p = planner.PlanSet(new List<InstrumentKind> { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp });
            Assert.IsTrue(p.Fits);
            Assert.That(p.Members.Count, Is.EqualTo(3));
        }

        sealed class Vec3Tol : IEqualityComparer<Vector3>
        {
            readonly float _t;
            public Vec3Tol(float t) { _t = t; }
            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= _t;
            public int GetHashCode(Vector3 v) => 0;
        }
    }
}
