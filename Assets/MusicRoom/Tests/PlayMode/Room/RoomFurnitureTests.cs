// The room's furniture as data (12-new-instruments/platform.md 4.1, IRoomFurniture; Room/World/Furniture.cs): the blockers
// the display plan names exist, are measured from the built room (checked against pieces whose size the room code states:
// the inside sill, the bench, the curtains), lie inside the room, and are logged against the reference approximations of
// the platform stow plan's FURNITURE table (the report's numbers). Then the StowPlanTests PlayMode half: every
// registry instrument's display spot (InstrumentDescriptor.Stow: its pose and boxes, or the seat boxes without the air
// boxes moved with the rig) stands 3 cm clear of every blocker; the decor on the blockers (the fern, the ivy, the
// coffee-table candle) is measured too and its contacts are logged, not failed (README, "Furniture").
// The room faces the fire (RoomFrame, 2026-11-11): IRoomFurniture publishes the WORLD boxes (the room's turned); the
// reference table, the descriptors' spots and RoomFurniture.RoomBlockers / RoomDecor are in the room frame. The bench stands
// at the seat in the world, so it is checked there.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Room;
using MusicRoom.Room.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Room
{
    public class RoomFurnitureTests
    {
        /// <summary>The stow plan's FURNITURE: name, centre x, z, half x, z, y0, y1, yaw (the platform's approximations).</summary>
        static readonly (string name, float cx, float cz, float hx, float hz, float y0, float y1, float yaw)[] Reference =
        {
            ("bookshelf", -3.03f, -2.68f, 0.17f, 0.56f, 0f, 1.93f, 0f),
            ("floor lamp", -2.40f, 0.52f, 0.16f, 0.16f, 0f, 1.80f, 0f),
            ("lamp shade", -2.40f, 0.52f, 0.22f, 0.22f, 1.50f, 1.80f, 0f),
            ("plant P1", -1.05f, -3.76f, 0.30f, 0.30f, 0f, 1.20f, 0f),
            ("plant P2", 2.80f, -3.68f, 0.30f, 0.30f, 0f, 1.20f, 0f),
            ("plant P3", 2.82f, 0.70f, 0.30f, 0.30f, 0f, 1.20f, 0f),
            ("plant P4", -2.88f, -1.88f, 0.30f, 0.30f, 0f, 1.20f, 0f),
            ("loveseat", 1.65f, -2.83f, 0.63f, 0.43f, 0f, 0.85f, 48.7f),
            ("coffee table", 0.85f, -3.13f, 0.30f, 0.30f, 0f, 0.45f, 0f),
            ("hearth", 0.30f, -3.85f, 0.80f, 0.25f, 0f, 1.40f, 0f),
            ("curtain near", 3.12f, 0.08f, 0.06f, 0.20f, 0f, 2.62f, 0f),
            ("curtain far", 3.12f, -2.44f, 0.06f, 0.20f, 0f, 2.62f, 0f),
            ("inside sill", 3.10f, -1.18f, 0.10f, 1.05f, 0.59f, 0.63f, 0f),
            ("window view", 3.15f, -1.18f, 0.05f, 0.90f, 0.85f, 2.40f, 0f),
            ("bench", 0f, -0.10f, 0.38f, 0.17f, 0f, 0.54f, 0f),
        };

        public const float Gap = 0.03f;
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            RoomModuleTests.CollectGarbage();
            _boot = new TestBoot(headless: true);
            yield return _boot.Start();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _boot.Stop();
            RoomModuleTests.CollectGarbage();
        }

        static RoomModule Room => (RoomModule)App.Current.Modules.First(m => m is RoomModule);

        [UnityTest]
        public IEnumerator TheRoomPublishesEveryBlockerOfTheDisplayPlanMeasuredFromItsGeometry()
        {
            var room = Room;
            var furniture = App.Current.Get<IRoomFurniture>();
            Assert.That(furniture, Is.SameAs(room.Furniture), "Room registers its furniture");
            var blockers = furniture.FloorBlockers;
            Assert.That(blockers.Select(b => b.Name), Is.EqualTo(RoomFurniture.BlockerNames), "every blocker of the plan, in its order");
            Bounds box = GrabConstants.RoomBox;
            var corners = new Vector2[4];
            foreach (var b in blockers.Concat(room.Furniture.Decor))
            {
                Assert.That(b.Half.x, Is.GreaterThan(0.005f), b.Name);
                Assert.That(b.Half.y, Is.GreaterThan(0.005f), b.Name);
                Assert.That(b.Y1, Is.GreaterThan(b.Y0), b.Name);
                RoomFurniture.Corners(b, corners);
                foreach (var c in corners)
                {
                    Assert.That(c.x, Is.InRange(box.min.x - 1e-3f, box.max.x + 1e-3f), b.Name + " inside the room (x)");
                    Assert.That(c.y, Is.InRange(box.min.z - 1e-3f, box.max.z + 1e-3f), b.Name + " inside the room (z)");
                }
                Assert.That(b.Y0, Is.GreaterThanOrEqualTo(-1e-3f), b.Name);
                Assert.That(b.Y1, Is.LessThanOrEqualTo(box.max.y + 1e-3f), b.Name);
            }

            // The world boxes are the room's turned (RoomFrame.ToWorld), one for one.
            var inRoom = room.Furniture.RoomBlockers;
            Assert.That(inRoom.Count, Is.EqualTo(blockers.Count));
            for (int i = 0; i < blockers.Count; i++)
            {
                FurnitureBlocker t = RoomFrame.ToWorld(inRoom[i]);
                Assert.That(Vector2.Distance(blockers[i].Centre, t.Centre), Is.LessThan(1e-5f), blockers[i].Name + " turned");
                Assert.That(blockers[i].Half, Is.EqualTo(t.Half), blockers[i].Name);
                Assert.That(blockers[i].Yaw, Is.EqualTo(t.Yaw), blockers[i].Name);
            }
            Get(furniture, RoomFurniture.Hearth, out var hearthW);
            Assert.That(hearthW.Centre.y, Is.GreaterThan(3.0f), "the fireplace ahead of the seat");
            Get(furniture, RoomFurniture.InsideSill, out var sillW);
            Assert.That(sillW.Centre.x, Is.LessThan(-3f), "the window sill on the left wall");

            // Pieces whose size the room code states outright (RoomBuilder, Curtains): the measuring is right (room frame).
            Get(inRoom, RoomFurniture.InsideSill, out var sill);
            AssertBox(sill, 3.10f, -1.18f, 0.10f, 1.05f, 0.59f, 0.63f, 2e-3f);   // 2.1 x 0.04 x 0.2 under the window, top .63
            Get(furniture, RoomFurniture.Bench, out var bench);
            AssertBox(bench, 0f, -0.10f, 0.38f, 0.17f, 0f, 0.515f, 2e-3f);       // world: seat .76 x .34 at z -.1, cushion top .515 (it stays at the seat)
            Get(inRoom, RoomFurniture.CurtainNear, out var near);
            Get(inRoom, RoomFurniture.CurtainFar, out var far);
            double w = MusicRoom.Room.World.Curtains.PanelWidth;
            Assert.That(near.Centre.y - near.Half.y, Is.EqualTo((float)MusicRoom.Room.World.Curtains.RightZ0).Within(2e-3f), "near panel at its z0");
            Assert.That(far.Centre.y - far.Half.y, Is.EqualTo((float)MusicRoom.Room.World.Curtains.LeftZ0).Within(2e-3f));
            Assert.That(near.Half.y * 2f, Is.EqualTo((float)w).Within(2e-3f));
            Assert.That(near.Y0, Is.EqualTo((float)MusicRoom.Room.World.Curtains.Bottom).Within(2e-3f));
            Assert.That(near.Y1, Is.EqualTo((float)MusicRoom.Room.World.Curtains.Top).Within(2e-3f));
            Get(furniture, RoomFurniture.Loveseat, out var sofa);
            Assert.That(sofa.Yaw, Is.EqualTo(0.85f * Mathf.Rad2Deg).Within(0.01f), "turned toward the fire as built (a box's yaw is the same after a half turn)");
            Get(inRoom, RoomFurniture.WindowView, out var view);
            Assert.That(view.Y0, Is.EqualTo(RoomFurniture.WindowViewFrom), "kept clear above 0.85 m");
            Assert.That(view.Centre.x - view.Half.x, Is.EqualTo(sill.Centre.x - sill.Half.x).Within(1e-4f), "as deep as the sill");
            Assert.That(view.Centre.x + view.Half.x, Is.EqualTo(RoomFrame.LocalBox.max.x).Within(1e-4f), "to the wall");

            // The report: each blocker against the reference approximation (logged first, asserted after).
            var log = new System.Text.StringBuilder("[Room] furniture blockers (measured vs the stow plan's FURNITURE):\n");
            var misplaced = new List<string>();
            foreach (var r in Reference)
            {
                FurnitureBlocker b;
                if (r.name == RoomFurniture.Bench) Get(furniture, r.name, out b);   // at the seat: the reference's place, in the world
                else Get(inRoom, r.name, out b);
                float dc = Vector2.Distance(b.Centre, new Vector2(r.cx, r.cz));
                log.AppendLine($"  {r.name}: centre ({b.Centre.x:F3}, {b.Centre.y:F3}) half ({b.Half.x:F3}, {b.Half.y:F3}) y {b.Y0:F3}..{b.Y1:F3} yaw {b.Yaw:F1}" +
                               $" | ref ({r.cx:F2}, {r.cz:F2}) ({r.hx:F2}, {r.hz:F2}) y {r.y0:F2}..{r.y1:F2} yaw {r.yaw:F1}" +
                               $" | centre off {dc:F3}, half {b.Half.x - r.hx:+0.000;-0.000}/{b.Half.y - r.hz:+0.000;-0.000}, y0 {b.Y0 - r.y0:+0.000;-0.000}, y1 {b.Y1 - r.y1:+0.000;-0.000}");
                // The pieces stand where the plan put them: its centre inside the measured box.
                var o = Obb.Of(b);
                float lx = (r.cx - o.Cx) * o.Ux + (r.cz - o.Cz) * o.Uz, lz = -(r.cx - o.Cx) * o.Uz + (r.cz - o.Cz) * o.Ux;
                if (Mathf.Abs(lx) > b.Half.x || Mathf.Abs(lz) > b.Half.y) misplaced.Add($"{r.name} ({dc:F3} m off)");
            }
            foreach (var d in room.Furniture.RoomDecor)
                log.AppendLine($"  decor {d.Name}: centre ({d.Centre.x:F3}, {d.Centre.y:F3}) half ({d.Half.x:F3}, {d.Half.y:F3}) y {d.Y0:F3}..{d.Y1:F3}");
            Debug.Log(log.ToString());
            Assert.That(misplaced, Is.Empty, "where the plan expects them");
            Assert.That(room.Furniture.Decor.Select(d => d.Name), Is.EquivalentTo(new[]
            {
                "plant P1 leaves", "plant P2 leaves", "plant P3 leaves", "plant P4 leaves", "fern on the bookshelf", "ivy on the sill", "coffee table candle",
                "flowers on the mantel", "flowers on the sill", "flowers on the bookshelf",
            }));
            // A floor plant's leaves spread beyond its body; the body (pot, stems) is the blocker.
            foreach (string p in new[] { "plant P1", "plant P2", "plant P3", "plant P4" })
            {
                Get(furniture, p, out var body);
                Assert.That(room.Furniture.TryGet(p + " leaves", out var leaves), Is.True, p);
                Assert.That(leaves.Half.x + leaves.Half.y, Is.GreaterThan(body.Half.x + body.Half.y), p + ": the leaves spread wider than the stems");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryDisplaySpotStandsThreeCentimetresClearOfTheFurniture()
        {
            var room = Room;
            var furniture = App.Current.Get<IRoomFurniture>();
            var registry = App.Current.Registry;
            var problems = new List<string>();
            var log = new System.Text.StringBuilder("[Room] display spots against the furniture (gap 3 cm, room frame):\n");
            int spots = 0;
            foreach (InstrumentDescriptor d in registry.All)
            {
                if (d.Stow == null) continue;
                spots++;
                List<Obb> boxes = DisplayBoxes(d);
                Assert.That(boxes, Is.Not.Empty, d.Id + ": a displayed footprint");
                float nearest = float.PositiveInfinity;
                string nearestName = null;
                foreach (FurnitureBlocker f in room.Furniture.RoomBlockers)   // the spots are in room coordinates
                {
                    Obb fo = Obb.Of(f);
                    foreach (Obb o in boxes)
                    {
                        float c = Clearance(o, fo);
                        if (c < nearest) { nearest = c; nearestName = f.Name; }
                        if (c < Gap) problems.Add($"{d.Id} ({d.Stow.SpotId}): {c * 100f:F1} cm from {f.Name}");
                    }
                }
                string decor = "";
                foreach (FurnitureBlocker f in room.Furniture.RoomDecor)
                    foreach (Obb o in boxes)
                    {
                        float c = Clearance(o, Obb.Of(f));
                        if (c < Gap) decor += $"; touches decor {f.Name} ({c * 100f:F1} cm)";
                    }
                log.AppendLine($"  {d.Id} at {d.Stow.SpotId} ({d.Stow.Pose.x:F2}, {d.Stow.Pose.z:F2}) yaw {d.Stow.Yaw:F0}: nearest blocker {nearestName} {nearest * 100f:F1} cm{decor}");
            }
            Debug.Log(log.ToString());
            Assert.That(spots, Is.GreaterThanOrEqualTo(3), "the classic three at least");
            Assert.That(problems, Is.Empty);
            yield return null;
        }

        // ------------------------------------------------------------------ helpers

        static void Get(IRoomFurniture f, string name, out FurnitureBlocker b) => Get(f.FloorBlockers, name, out b);

        static void Get(IReadOnlyList<FurnitureBlocker> list, string name, out FurnitureBlocker b)
        {
            foreach (var x in list) if (x.Name == name) { b = x; return; }
            Assert.Fail("no blocker " + name);
            b = default;
        }

        static void AssertBox(FurnitureBlocker b, float cx, float cz, float hx, float hz, float y0, float y1, float tol)
        {
            Assert.That(b.Centre.x, Is.EqualTo(cx).Within(tol), b.Name + " cx");
            Assert.That(b.Centre.y, Is.EqualTo(cz).Within(tol), b.Name + " cz");
            Assert.That(b.Half.x, Is.EqualTo(hx).Within(tol), b.Name + " hx");
            Assert.That(b.Half.y, Is.EqualTo(hz).Within(tol), b.Name + " hz");
            Assert.That(b.Y0, Is.EqualTo(y0).Within(tol), b.Name + " y0");
            Assert.That(b.Y1, Is.EqualTo(y1).Within(tol), b.Name + " y1");
        }

        /// <summary>An oriented box on the floor with a height range (the stow plan's world box): centre, unit axis of hx, halves.</summary>
        public struct Obb
        {
            public float Cx, Cz, Ux, Uz, Hx, Hz, Y0, Y1;
            public static Obb Of(in FurnitureBlocker f)
            {
                float a = f.Yaw * Mathf.Deg2Rad;
                return new Obb { Cx = f.Centre.x, Cz = f.Centre.y, Ux = Mathf.Cos(a), Uz = -Mathf.Sin(a), Hx = f.Half.x, Hz = f.Half.y, Y0 = f.Y0, Y1 = f.Y1 };
            }
            /// <summary>A rig-local footprint box at a room pose (x, z, yaw deg); heights shifted by dy.</summary>
            public static Obb At(float px, float pz, float yawDeg, in FootBox b, float dy)
            {
                float a = yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                return new Obb { Cx = px + b.Cx * c + b.Cz * s, Cz = pz - b.Cx * s + b.Cz * c, Ux = c, Uz = -s, Hx = b.Hx, Hz = b.Hz, Y0 = b.Y0 + dy, Y1 = b.Y1 + dy };
            }
        }

        /// <summary>The displayed footprint: Stow.Boxes (heights above the floor with the rig at Stow.Pose), else the seat boxes
        /// without the air boxes, carried from Seat.Home's height to Stow.Pose's (platform.md 4.1, Registry.cs StowSpec).</summary>
        public static List<Obb> DisplayBoxes(InstrumentDescriptor d)
        {
            var list = new List<Obb>();
            StowSpec s = d.Stow;
            if (s.Boxes != null && s.Boxes.Length > 0)
                foreach (FootBox b in s.Boxes) list.Add(Obb.At(s.Pose.x, s.Pose.z, s.Yaw, b, 0f));
            else
                foreach (FootBox b in d.Seat.Boxes) if (!b.Air) list.Add(Obb.At(s.Pose.x, s.Pose.z, s.Yaw, b, s.Pose.y - d.Seat.Home.y));
            return list;
        }

        /// <summary>The horizontal gap between two boxes whose height ranges overlap (m): the largest separation along the four
        /// SAT axes, negative when they overlap; +inf when one is above the other (the stow plan's overlap rule: the heights need no
        /// gap, so the bowls may stand on the sill).</summary>
        public static float Clearance(in Obb a, in Obb b)
        {
            if (a.Y1 <= b.Y0 || b.Y1 <= a.Y0) return float.PositiveInfinity;
            float best = float.NegativeInfinity;
            float dx = b.Cx - a.Cx, dz = b.Cz - a.Cz;
            Vector2[] axes = { new Vector2(a.Ux, a.Uz), new Vector2(-a.Uz, a.Ux), new Vector2(b.Ux, b.Uz), new Vector2(-b.Uz, b.Ux) };
            foreach (Vector2 n in axes)
            {
                float ra = a.Hx * Mathf.Abs(a.Ux * n.x + a.Uz * n.y) + a.Hz * Mathf.Abs(-a.Uz * n.x + a.Ux * n.y);
                float rb = b.Hx * Mathf.Abs(b.Ux * n.x + b.Uz * n.y) + b.Hz * Mathf.Abs(-b.Uz * n.x + b.Ux * n.y);
                best = Mathf.Max(best, Mathf.Abs(dx * n.x + dz * n.y) - ra - rb);
            }
            return best;
        }
    }
}
