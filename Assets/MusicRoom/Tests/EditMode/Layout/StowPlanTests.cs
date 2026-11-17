// The display plan (platform.md 4.1, 12.1 StowPlanTests / SeatAwayTests / RaySummonPickTests). Every display spot, the
// reference's and the live descriptors' alike: inside the room, 3 cm clear of stow-plan.mjs's furniture blockers (copied as
// test data), of every other spot and of every box of every set that fits at the default seat, and 1.3 m or more from the
// head. Seats at (+-0.6, 1.2, +-0.6): a spot an at-seat box comes within 3 cm of moves to a free spare or hides, and what
// stays on display is clear of everything. The ray summon's cone pick on the 24 reference rays (aim, +-3 deg).
// The reference's spots, furniture and rays are in room coordinates; since 2026-11-11 the room is turned to face the fire
// (RoomFrame), so they are turned into the world here (PlatformVectors.Spot / Furniture, the rays below), as Layout does.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using V = MusicRoom.Tests.Layout.PlatformVectors;

namespace MusicRoom.Tests.Layout
{
    public class StowPlanTests
    {
        internal static List<SeatPlanner.FloorObb> Furniture()
        {
            var f = new List<SeatPlanner.FloorObb>();
            foreach (FurnitureBlocker b in V.Furniture) f.Add(StowPlan.BlockerBox(b));
            return f;
        }

        /// <summary>Every non-air box of every set that fits, planned about a head at (hx, hz) facing +z (the seat frame
        /// follows the head), with the side table where the plan puts it.</summary>
        internal static List<KeyValuePair<string, List<SeatPlanner.FloorObb>>> SeatBoxes(SeatPlanner planner, double hx, double hz, bool withTable)
            => SeatBoxes(planner, hx, hz, withTable, null);

        /// <summary>As SeatBoxes, with the instrument each box belongs to (Table for the side table) in <paramref name="owners"/>.</summary>
        internal static List<KeyValuePair<string, List<SeatPlanner.FloorObb>>> SeatBoxes(SeatPlanner planner, double hx, double hz, bool withTable,
            List<List<InstrumentKind>> owners)
        {
            var output = new List<KeyValuePair<string, List<SeatPlanner.FloorObb>>>();
            foreach (string[] ids in SeatPlanner.AllSets(V.Carousel))
            {
                var set = new List<InstrumentKind>();
                foreach (string id in ids) set.Add(V.KindOf(id));
                SeatPlanner.Plan p = planner.PlanSet(set);
                if (!p.Fits) continue;
                var boxes = new List<SeatPlanner.FloorObb>();
                var who = new List<InstrumentKind>();
                foreach (SeatPlanner.Member m in p.Members)
                {
                    var pose = new SeatPlanner.Pose2(m.Pose.X + hx, m.Pose.Z + hz, m.Pose.Yaw);
                    foreach (SeatPlanner.Box b in planner.Get(m.Kind).Boxes) if (!b.Air) { boxes.Add(SeatPlanner.WorldBox(pose, b)); who.Add(m.Kind); }
                }
                if (withTable)
                {
                    var t = new SeatPlanner.Pose2(p.Table.X + hx, p.Table.Z + hz, p.Table.Yaw);
                    foreach (SeatPlanner.Box b in planner.TableBoxes) { boxes.Add(SeatPlanner.WorldBox(t, b)); who.Add(InstrumentKind.Table); }
                }
                owners?.Add(who);
                output.Add(new KeyValuePair<string, List<SeatPlanner.FloorObb>>(string.Join("+", ids), boxes));
            }
            return output;
        }

        static void Validate(Dictionary<string, StowPlan.Spot> spots, SeatPlanner planner, string what)
        {
            List<SeatPlanner.FloorObb> furniture = Furniture();
            var owners = new List<List<InstrumentKind>>();
            var seats = SeatBoxes(planner, 0, 0, false, owners);
            var problems = new List<string>();
            foreach (KeyValuePair<string, StowPlan.Spot> kv in spots)
            {
                var mine = new List<SeatPlanner.FloorObb>();
                kv.Value.WorldBoxes(mine);
                Assert.That(mine.Count, Is.GreaterThan(0), kv.Key);
                foreach (SeatPlanner.FloorObb o in mine)
                {
                    if (!SeatPlanner.InsideRoom(o)) problems.Add(kv.Key + ": outside the room");
                    for (int f = 0; f < furniture.Count; f++)
                        if (SeatPlanner.Overlaps(o, furniture[f], StowPlan.Gap)) problems.Add(kv.Key + ": hits " + V.Furniture[f].Name);
                    foreach (KeyValuePair<string, StowPlan.Spot> other in spots)
                    {
                        if (other.Key == kv.Key) continue;
                        var theirs = new List<SeatPlanner.FloorObb>();
                        other.Value.WorldBoxes(theirs);
                        foreach (SeatPlanner.FloorObb q in theirs) if (SeatPlanner.Overlaps(o, q, StowPlan.Gap)) problems.Add(kv.Key + ": hits displayed " + other.Key);
                    }
                    // stow-plan.mjs: every box of every fitting seat plan but the instrument's own (s.id !== id)
                    for (int si = 0; si < seats.Count; si++)
                        for (int q = 0; q < seats[si].Value.Count; q++)
                        {
                            if (owners[si][q] == kv.Value.Kind) continue;
                            if (SeatPlanner.Overlaps(o, seats[si].Value[q], StowPlan.Gap)) { problems.Add(kv.Key + ": in the way of " + seats[si].Key); break; }
                        }
                }
                double d = kv.Value.Distance(0.0, 0.0);
                if (d < StowPlan.MinDistance) problems.Add($"{kv.Key}: only {d:F2} m from the head");
            }
            Assert.That(problems, Is.Empty, what + ": " + string.Join("; ", new HashSet<string>(problems)));
        }

        [Test]
        public void TheReferenceSpotsAreClearOfEverything()
        {
            var spots = new Dictionary<string, StowPlan.Spot>();
            foreach (string id in V.Carousel) spots[id] = V.Spot(id);
            Validate(spots, V.Planner(), "reference spots");
            // the reference's distances and bearings
            foreach (object s in V.Arr(V.Obj(V.Obj(V.Root, "stow"), "validation"), "spots"))
            {
                StowPlan.Spot spot = spots[V.Str(s, "id")];
                // the reference measured from its seat, the room frame's origin (RoomFrame turns and slides the room)
                RoomFrame.ToWorld(0.0, 0.0, out double rx, out double rz);
                Assert.That(spot.Distance(rx, rz), Is.EqualTo(V.Num(s, "distance")).Within(1e-9), V.Str(s, "id"));
                Assert.That(spot.Id, Is.EqualTo(V.Str(s, "spot")));
            }
        }

        [Test]
        public void TheLiveDescriptorsSpotsAreClearOfEverything()
        {
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            var spots = new Dictionary<string, StowPlan.Spot>();
            foreach (InstrumentDescriptor d in reg.All)
            {
                StowPlan.Spot s = StowPlan.Spot.From(d);
                Assert.That(s, Is.Not.Null, d.Id + " has a display spot");
                spots[d.Id] = s;
                StowPlan.Spot r = V.Spot(d.Id);
                Assert.That(s.Id, Is.EqualTo(r.Id), d.Id + " spot id");
                Assert.That(s.Mount, Is.EqualTo(r.Mount), d.Id + " mount");
                Assert.That(s.X, Is.EqualTo(r.X).Within(1e-6), d.Id + " x");
                Assert.That(s.Z, Is.EqualTo(r.Z).Within(1e-6), d.Id + " z");
                Assert.That(s.Yaw, Is.EqualTo(r.Yaw).Within(1e-6), d.Id + " yaw");
            }
            Assert.That(spots.Count, Is.EqualTo(7));
            Validate(spots, SeatPlanner.FromRegistry(reg), "live spots");
        }

        [Test]
        public void AssignKeepsEverySpotAtTheDefaultSeat()
        {
            SeatPlanner planner = V.Planner();
            var plan = new StowPlan();
            plan.SetFurniture(V.Furniture);
            foreach (var s in SeatBoxes(planner, 0, 0, true))
            {
                var spots = new List<StowPlan.Spot>();
                foreach (string id in V.Carousel) if (System.Array.IndexOf(s.Key.Split('+'), id) < 0) spots.Add(V.Spot(id));
                plan.Assign(spots, s.Value);
                foreach (StowPlan.Result r in plan.Results)
                    Assert.That(r.Placement, Is.EqualTo(StowPlan.Placement.Spot), s.Key + ": " + r.Kind + " keeps its own spot at the default seat");
            }
        }

        [Test]
        public void PickPointsAreTheTopCentresFiveCentimetresDown()
        {
            StowPlan.Spot bowls = V.Spot("bowls");
            double[] p = StowPlan.PickPointD(bowls);
            Assert.That(p[1], Is.EqualTo(0.735 - 0.05).Within(1e-12), "the top of the bowls on the sill, 5 cm down");
            Vector3 f = bowls.PickPoint();
            Assert.That(f.x, Is.EqualTo((float)p[0]).Within(1e-5f));
            Assert.That(f.z, Is.EqualTo((float)p[2]).Within(1e-5f));
        }
    }

    public class SeatAwayTests
    {
        [Test]
        public void AtOtherSeatsABlockedSpotUsesASpareOrHides()
        {
            SeatPlanner planner = V.Planner();
            List<SeatPlanner.FloorObb> furniture = StowPlanTests.Furniture();
            var plan = new StowPlan();
            plan.SetFurniture(V.Furniture);
            int blocked = 0, spares = 0, hidden = 0, cases = 0;
            foreach (double hx in new[] { -0.6, 0.6 })
                foreach (double hz in new[] { -0.6, 0.6 })
                    foreach (var s in StowPlanTests.SeatBoxes(planner, hx, hz, true))
                    {
                        cases++;
                        var spots = new List<StowPlan.Spot>();
                        foreach (string id in V.Carousel) if (System.Array.IndexOf(s.Key.Split('+'), id) < 0) spots.Add(V.Spot(id));
                        plan.Assign(spots, s.Value);
                        string where = $"head ({hx}, {hz}) {s.Key}";
                        var shown = new List<KeyValuePair<InstrumentKind, List<SeatPlanner.FloorObb>>>();
                        for (int i = 0; i < plan.Results.Count; i++)
                        {
                            StowPlan.Result r = plan.Results[i];
                            var own = new List<SeatPlanner.FloorObb>();
                            spots[i].WorldBoxes(own);
                            bool ownBlocked = Hits(own, s.Value);
                            Assert.That(r.Placement == StowPlan.Placement.Spot, Is.EqualTo(!ownBlocked), where + ": " + r.Kind + " keeps its spot only when nothing at the seat comes within 3 cm");
                            if (ownBlocked) blocked++;
                            if (r.Placement == StowPlan.Placement.Spare) spares++;
                            if (r.Placement == StowPlan.Placement.Hidden) { hidden++; continue; }
                            var boxes = new List<SeatPlanner.FloorObb>();
                            r.Spot.WorldBoxes(boxes);
                            Assert.IsFalse(Hits(boxes, s.Value), where + ": " + r.Kind + " clear of the seat");
                            foreach (SeatPlanner.FloorObb o in boxes) Assert.IsTrue(SeatPlanner.InsideRoom(o), where + ": " + r.Kind + " inside the room");
                            if (r.Placement == StowPlan.Placement.Spare) Assert.IsFalse(Hits(boxes, furniture), where + ": " + r.Kind + " on a spare clear of the furniture");
                            foreach (var other in shown) Assert.IsFalse(Hits(boxes, other.Value), where + ": " + r.Kind + " clear of displayed " + other.Key);
                            shown.Add(new KeyValuePair<InstrumentKind, List<SeatPlanner.FloorObb>>(r.Kind, boxes));
                        }
                    }
            TestContext.WriteLine($"[Layout] seats at (+-0.6, +-0.6): {cases} seat plans, {blocked} spots blocked: {spares} on a spare, {hidden} hidden");
            Assert.That(blocked, Is.GreaterThan(0), "another seat does block some spots (the rule is exercised)");
            Assert.That(spares + hidden, Is.EqualTo(blocked), "every blocked spot went to a spare or hid");
        }

        static bool Hits(List<SeatPlanner.FloorObb> a, List<SeatPlanner.FloorObb> b)
        {
            foreach (SeatPlanner.FloorObb x in a) foreach (SeatPlanner.FloorObb y in b) if (SeatPlanner.Overlaps(x, y, StowPlan.Gap)) return true;
            return false;
        }
    }

    public class RaySummonPickTests
    {
        [Test]
        public void TheTwentyOneReferenceRays()
        {
            string[] ids = V.Carousel;
            var points = new List<double[]>();
            foreach (string id in ids) points.Add(StowPlan.PickPointD(V.Spot(id)));
            var floats = new List<Vector3>();
            foreach (double[] p in points) floats.Add(new Vector3((float)p[0], (float)p[1], (float)p[2]));
            List<object> rays = V.Arr(V.Obj(V.Root, "stow"), "rays");
            Assert.That(rays.Count, Is.EqualTo(21));
            int ok = 0;
            foreach (object r in rays)
            {
                string aim = V.Str(r, "aim");
                double off = V.Num(r, "offDeg");
                object o = V.Obj(r, "origin");
                // the reference ray is in room coordinates: turned into the world with the spots (RoomFrame)
                RoomFrame.ToWorld(V.Num(o, "x"), V.Num(o, "z"), out double ox, out double oz);
                double oy = V.Num(o, "y");
                // make-vectors.mjs: the ray from a seated right hand at the aim's pick point, turned by the hand-ray error
                double[] t = points[System.Array.IndexOf(ids, aim)];
                double dx = t[0] - ox, dy = t[1] - oy, dz = t[2] - oz, n = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                dx /= n; dy /= n; dz /= n;
                double a = off * System.Math.PI / 180.0, c = System.Math.Cos(a), s = System.Math.Sin(a);
                double rx = dx * c + dz * s, rz = -dx * s + dz * c;
                object d = V.Obj(r, "dir");
                double vx = RoomFrame.Cos * V.Num(d, "x") + RoomFrame.Sin * V.Num(d, "z"), vz = -RoomFrame.Sin * V.Num(d, "x") + RoomFrame.Cos * V.Num(d, "z");
                Assert.That(rx, Is.EqualTo(vx).Within(1.0001e-4), aim + " dir x");
                Assert.That(dy, Is.EqualTo(V.Num(d, "y")).Within(1.0001e-4), aim + " dir y");
                Assert.That(rz, Is.EqualTo(vz).Within(1.0001e-4), aim + " dir z");
                int pick = StowPlan.Pick(ox, oy, oz, rx, dy, rz, points, out double angle, out _);
                string want = V.Has(r, "picked") ? V.Str(r, "picked") : null;
                Assert.That(pick >= 0 ? ids[pick] : null, Is.EqualTo(want), $"{aim} {off:+0;-0;0} deg: picked");
                if (want != null) Assert.That(angle, Is.EqualTo(V.Num(r, "angle")).Within(1.0001e-4), $"{aim} {off} deg: angle");
                if (pick >= 0 && ids[pick] == aim) ok++;
                // the float path the summon runs gives the same pick
                int fp = StowPlan.Pick(new Vector3((float)ox, (float)oy, (float)oz), new Vector3((float)rx, (float)dy, (float)rz).normalized, floats, floats.Count, out _, out _);
                Assert.That(fp, Is.EqualTo(pick), aim + ": the float pick agrees");
            }
            TestContext.WriteLine($"[Layout] cone pick: {ok} of 24 aims (+-3 deg) picked what they aimed at");
            // platform.md 4.4 gave 23 of 24 with the provisional footprints; the reference with the final ones picks every aim.
            int reference = 0;
            foreach (object r in rays) if (V.Has(r, "picked") && V.Str(r, "picked") == V.Str(r, "aim")) reference++;
            Assert.That(ok, Is.EqualTo(reference), "as many as the reference picks");
        }
    }
}
