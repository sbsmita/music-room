// The seat planner (platform.md 4.2, 12.1 SeatPlannerTests) against the reference: every one of the 92 sets of up to three
// (seatPlanner.sets) planned on the vectors' own instruments (so the C# planner is tested independent of the live descriptors):
// fits, the side table's alternate, each member's slot, push, pose (1e-4), reach and the cost, the candidate count (the
// same loop order) and the member that failed; the default set at today's homes; each instrument alone at its Single home;
// allSets' order. Then the live registry: the classic three's descriptors equal the vectors' instruments exactly, the five
// stubs' differences are reported (their agents change them; the integrator re-runs make-vectors.mjs). Mixed reality:
// no rear slot, else the last member is put away.
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using V = MusicRoom.Tests.Layout.PlatformVectors;

namespace MusicRoom.Tests.Layout
{
    public class SeatPlannerTests
    {
        const double Tol = 1.0001e-4;

        [Test]
        public void EveryReferenceSetPlansExactlyAsTheReference()
        {
            SeatPlanner planner = V.Planner();
            List<object> sets = V.Arr(V.Obj(V.Root, "seatPlanner"), "sets");
            Assert.That(sets.Count, Is.EqualTo(63), "every set of up to three of the seven");
            int nodes = 0;
            var sw = Stopwatch.StartNew();
            double worstMs = 0;
            foreach (object v in sets)
            {
                List<InstrumentKind> set = V.Kinds(V.Arr(v, "set"));
                string name = string.Join("+", V.Arr(v, "set"));
                long t0 = sw.ElapsedTicks;
                SeatPlanner.Plan p = planner.PlanSet(set);
                worstMs = System.Math.Max(worstMs, (sw.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency);
                Assert.That(p.Fits, Is.EqualTo(V.Bool(v, "fits")), name + ": fits");
                Assert.That(p.TableAlt, Is.EqualTo(V.Bool(v, "tableAlt")), name + ": the side table's alternate");
                Assert.That(p.Nodes, Is.EqualTo((int)V.Num(v, "nodes")), name + ": candidates evaluated (the same loop order)");
                nodes += p.Nodes;
                if (!p.Fits)
                {
                    Assert.That(p.Failed.HasValue, Is.True, name);
                    Assert.That(InstrumentIds.Of(p.Failed.Value), Is.EqualTo(V.Str(v, "failed")), name + ": the member that failed");
                    continue;
                }
                Assert.That(p.Cost, Is.EqualTo(V.Num(v, "cost")).Within(Tol), name + ": cost");
                List<object> members = V.Arr(v, "members");
                Assert.That(p.Members.Count, Is.EqualTo(members.Count), name);
                for (int i = 0; i < members.Count; i++)
                {
                    object m = members[i];
                    SeatPlanner.Member got = p.Members[i];
                    string who = name + ": " + V.Str(m, "id");
                    Assert.That(InstrumentIds.Of(got.Kind), Is.EqualTo(V.Str(m, "id")), who + " (in the set's order)");
                    Assert.That(got.Slot.ToString(), Is.EqualTo(V.Str(m, "slot")), who + " slot");
                    Assert.That(got.Push, Is.EqualTo(V.Num(m, "push")).Within(1e-12), who + " push");
                    Assert.That(got.Pose.X, Is.EqualTo(V.Num(m, "x")).Within(Tol), who + " x");
                    Assert.That(got.Pose.Z, Is.EqualTo(V.Num(m, "z")).Within(Tol), who + " z");
                    Assert.That(SeatPlanner.Wrap(got.Pose.Yaw - V.Num(m, "yaw")), Is.EqualTo(0.0).Within(Tol), who + " yaw");
                    Assert.That(got.Reach, Is.EqualTo(V.Num(m, "reach")).Within(Tol), who + " reach");
                }
            }
            TestContext.WriteLine($"[Layout] seat planner: {sets.Count} sets, {nodes} candidates, worst set {worstMs:F3} ms, all {sw.Elapsed.TotalMilliseconds:F1} ms (editor)");
        }

        [Test]
        public void TheFitTableIsTheReferences()
        {
            SeatPlanner planner = V.Planner();
            object counts = V.Obj(V.Obj(V.Root, "seatPlanner"), "counts");
            var fits = new int[4];
            var total = new int[4];
            var table = new StringBuilder();
            foreach (object v in V.Arr(V.Obj(V.Root, "seatPlanner"), "sets"))
            {
                List<InstrumentKind> set = V.Kinds(V.Arr(v, "set"));
                SeatPlanner.Plan p = planner.PlanSet(set);
                total[set.Count]++;
                if (p.Fits) fits[set.Count]++;
                else table.Append(string.Join("+", V.Arr(v, "set"))).Append("; ");
            }
            for (int n = 1; n <= 3; n++)
            {
                Assert.That(fits[n], Is.EqualTo((int)V.Num(V.Obj(counts, "fits"), n.ToString())), n + " members: fit");
                Assert.That(total[n], Is.EqualTo((int)V.Num(V.Obj(counts, "total"), n.ToString())), n + " members: sets");
            }
            TestContext.WriteLine($"[Layout] fits: alone {fits[1]}/{total[1]}, pairs {fits[2]}/{total[2]}, triples {fits[3]}/{total[3]}; do not fit: {table}");
        }

        [Test]
        public void TheDefaultSetSitsExactlyAtTodaysHomes()
        {
            SeatPlanner planner = V.Planner();
            SeatPlanner.Plan p = planner.PlanSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp });
            Assert.IsTrue(p.Fits);
            Assert.IsFalse(p.TableAlt, "the side table stays at its home");
            Assert.That(p.Cost, Is.EqualTo(0.0), "canonical members cost nothing");
            foreach (SeatPlanner.Member m in p.Members)
            {
                SeatPlanner.Inst inst = planner.Get(m.Kind);
                Assert.IsTrue(m.AtHome, m.Kind + " at its canonical slot, no push");
                Assert.That(m.Slot, Is.EqualTo(inst.Canonical));
                Assert.That(m.Pose.X, Is.EqualTo(inst.Home.X), m.Kind + " x exactly");
                Assert.That(m.Pose.Z, Is.EqualTo(inst.Home.Z), m.Kind + " z exactly");
                Assert.That(m.Pose.Yaw, Is.EqualTo(inst.Home.Yaw), m.Kind + " yaw exactly");
            }
            Assert.That(p.Members[0].Slot, Is.EqualTo(SeatSlot.Front));
            Assert.That(p.Members[1].Slot, Is.EqualTo(SeatSlot.Right));
            Assert.That(p.Members[2].Slot, Is.EqualTo(SeatSlot.Left));
        }

        [Test]
        public void EachInstrumentAloneSitsAtItsSingleHome()
        {
            SeatPlanner planner = V.Planner();
            foreach (string id in V.InstrumentIdsInFile)
            {
                SeatPlanner.Inst inst = planner.Get(V.KindOf(id));
                SeatPlanner.Plan p = planner.PlanSet(new[] { inst.Kind });
                Assert.IsTrue(p.Fits && p.Single, id);
                Assert.That(p.Nodes, Is.Zero, id + ": no search for a set of one");
                Assert.That(p.Members[0].Pose.X, Is.EqualTo(inst.Single.X), id);
                Assert.That(p.Members[0].Pose.Z, Is.EqualTo(inst.Single.Z), id);
                Assert.That(p.Members[0].Pose.Yaw, Is.EqualTo(inst.Single.Yaw), id);
                Assert.That(p.Members[0].Slot, Is.EqualTo(SeatSlot.Front), id);
            }
        }

        [Test]
        public void AllSetsComesInTheReferencesOrder()
        {
            List<string[]> mine = SeatPlanner.AllSets(V.Carousel);
            List<object> sets = V.Arr(V.Obj(V.Root, "seatPlanner"), "sets");
            Assert.That(mine.Count, Is.EqualTo(sets.Count));
            for (int i = 0; i < mine.Count; i++)
            {
                var want = new List<string>();
                foreach (object o in V.Arr(sets[i], "set")) want.Add((string)o);
                Assert.That(string.Join(",", mine[i]), Is.EqualTo(string.Join(",", want)), "set " + i);
            }
        }

        [Test]
        public void CostOfIsRankRearAndPush()
        {
            // the handpan: Prefs FrontLeft, FrontRight, Left, Right, Front (the rank is the index)
            SeatPlanner.Inst handpan = V.Inst("handpan");
            Assert.That(SeatPlanner.CostOf(handpan, SeatSlot.FrontLeft, 0.0), Is.EqualTo(0.0));
            Assert.That(SeatPlanner.CostOf(handpan, SeatSlot.FrontRight, 0.15), Is.EqualTo(1.75), "rank 1 + 3 pushes");
            Assert.That(SeatPlanner.CostOf(handpan, SeatSlot.RightRear, 0.0), Is.EqualTo(handpan.PrefRank(SeatSlot.RightRear) + 2.0), "+2 for a rear slot");
            Assert.That(SeatPlanner.CostOf(handpan, SeatSlot.RightRear, 0.0, mixedReality: true), Is.EqualTo(handpan.PrefRank(SeatSlot.RightRear) + 100.0), "+100 in mixed reality");
        }

        [Test]
        public void MixedRealityNeverUsesARearSlot()
        {
            SeatPlanner planner = V.Planner();
            int rearInVr = 0, putAway = 0;
            foreach (object v in V.Arr(V.Obj(V.Root, "seatPlanner"), "sets"))
            {
                List<InstrumentKind> set = V.Kinds(V.Arr(v, "set"));
                SeatPlanner.Plan vr = planner.PlanSet(set);
                if (!vr.Fits) continue;
                SeatPlanner.Plan mr = planner.PlanMixedReality(set);
                Assert.IsTrue(mr.Fits, string.Join("+", set));
                Assert.IsFalse(mr.UsesRear, string.Join("+", set) + ": no rear slot without the swivel");
                if (!vr.UsesRear) { Assert.That(mr.PutAway, Is.Empty); continue; }
                rearInVr++;
                if (mr.PutAway.Count > 0)
                {
                    putAway++;
                    Assert.That(mr.PutAway[0], Is.EqualTo(set[set.Count - 1]), "the last member is put away first");
                }
            }
            TestContext.WriteLine($"[Layout] mixed reality: {rearInVr} fitting sets use a rear slot in VR; {putAway} lose a member");
            // since 2026-11-10 no instrument lists a rear slot: no set uses one in VR either (the cost rule: CostOfIsRankRearAndPush)
        }

        // ---------------------------------------------------------------- the live registry

        static bool Same(double a, float b) => System.Math.Abs(a - SeatPlanner.D(b)) < 1e-9;

        static string Diff(SeatPlanner.Inst want, InstrumentDescriptor d)
        {
            var sb = new StringBuilder();
            SeatSpec s = d.Seat;
            if (s.CanonicalSlot != want.Canonical) sb.Append($"canonical {s.CanonicalSlot} vs {want.Canonical}; ");
            if (string.Join(",", s.Prefs) != string.Join(",", want.Prefs)) sb.Append($"prefs {string.Join(",", s.Prefs)} vs {string.Join(",", want.Prefs)}; ");
            if (s.Priority != want.Priority) sb.Append($"priority {s.Priority} vs {want.Priority}; ");
            if (!Same(want.Home.X, s.Home.x) || !Same(want.Home.Z, s.Home.z) || !Same(want.Home.Yaw, s.HomeYaw)) sb.Append($"home ({s.Home.x}, {s.Home.z}, {s.HomeYaw}) vs {want.Home}; ");
            if (!Same(want.Single.X, s.Single.x) || !Same(want.Single.Z, s.Single.z) || !Same(want.Single.Yaw, s.SingleYaw)) sb.Append($"single ({s.Single.x}, {s.Single.z}, {s.SingleYaw}) vs {want.Single}; ");
            if (!Same(want.ReachMax, s.ReachMax)) sb.Append($"reach {s.ReachMax} vs {want.ReachMax}; ");
            if (!Same(want.PlayX, s.PlayCentre.x) || !Same(want.PlayZ, s.PlayCentre.y)) sb.Append($"play centre {s.PlayCentre} vs ({want.PlayX}, {want.PlayZ}); ");
            if (s.Boxes.Length != want.Boxes.Length) sb.Append($"{s.Boxes.Length} boxes vs {want.Boxes.Length}; ");
            else
                for (int i = 0; i < s.Boxes.Length; i++)
                {
                    FootBox b = s.Boxes[i];
                    SeatPlanner.Box w = want.Boxes[i];
                    if (!Same(w.Cx, b.Cx) || !Same(w.Cz, b.Cz) || !Same(w.Hx, b.Hx) || !Same(w.Hz, b.Hz) || !Same(w.Y0, b.Y0) || !Same(w.Y1, b.Y1) || w.Air != b.Air)
                        sb.Append($"box {i} {b} vs ({w.Cx}, {w.Cz}) +-({w.Hx}, {w.Hz}) y {w.Y0}..{w.Y1}; ");
                }
            return sb.ToString();
        }

        [Test]
        public void TheLiveDescriptorsAreTheReferenceForTheClassicThree()
        {
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            foreach (string id in new[] { "piano", "drums", "harp" })
            {
                InstrumentDescriptor d = reg.Get(V.KindOf(id));
                Assert.That(d, Is.Not.Null, id);
                string diff = Diff(V.Inst(id), d);
                Assert.That(diff, Is.Empty, id + ": the descriptor must equal registry.mjs exactly");
            }
            // The live table data equals the reference's.
            SeatPlanner live = SeatPlanner.FromRegistry(reg), refp = V.Planner();
            Assert.That(live.TableHome.X, Is.EqualTo(refp.TableHome.X));
            Assert.That(live.TableHome.Z, Is.EqualTo(refp.TableHome.Z));
            Assert.That(live.TableAlternate.X, Is.EqualTo(refp.TableAlternate.X));
            Assert.That(live.TableAlternate.Yaw, Is.EqualTo(refp.TableAlternate.Yaw));
            Assert.That(live.TableBoxes[0].Hx, Is.EqualTo(refp.TableBoxes[0].Hx));
        }

        /// <summary>Since the integration every new instrument is real: its descriptor must equal registry.mjs too (the
        /// integrator re-runs make-vectors.mjs after any footprint change). Phase 0 only reported these differences.</summary>
        [Test]
        public void TheLiveDescriptorsAreTheReferenceForTheFiveNewToo()
        {
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            var report = new StringBuilder();
            bool same = true;
            foreach (string id in new[] { "handpan", "handdrums", "marimba", "bowls" })
            {
                InstrumentDescriptor d = reg.Get(V.KindOf(id));
                if (d == null) { report.Append(id).Append(": not in the registry\n"); same = false; continue; }
                string diff = Diff(V.Inst(id), d);
                if (diff.Length > 0) same = false;
                report.Append(id).Append(diff.Length == 0 ? ": as the vectors\n" : ": " + diff + "\n");
            }
            TestContext.WriteLine("[Layout] the new instruments against platform.vectors.json:\n" + report);
            UnityEngine.Debug.Log("[Layout] the new instruments against platform.vectors.json:\n" + report);
            Assert.That(same, Is.True, "re-run make-vectors.mjs with the descriptors' data:\n" + report);
        }

        [Test]
        public void TheLivePlannerReproducesTheDefaultSet()
        {
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            SeatPlanner live = SeatPlanner.FromRegistry(reg);
            SeatPlanner.Plan p = live.PlanSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp });
            Assert.IsTrue(p.Fits);
            Assert.IsFalse(p.TableAlt);
            foreach (SeatPlanner.Member m in p.Members) Assert.IsTrue(m.AtHome, m.Kind + ": exactly at its home");
        }
    }
}
