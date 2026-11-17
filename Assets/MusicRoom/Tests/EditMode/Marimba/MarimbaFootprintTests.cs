// The marimba's real footprint (the integrator's phase-B item 3): the descriptor's boxes cover the built model's handles
// (conformance check 3's rule: the union within 3 cm), nothing of the model below 0.62 m reaches the seat planner's knee box,
// the back stays inside the front wall, and the live planner seats the marimba at Front alone and with the drums or the
// harp. Also its display spot. The planner verdicts are logged for the integrator's vectors re-run.
using System.Collections.Generic;
using System.Text;
using MusicRoom.Core;
using MusicRoom.Layout;
using MusicRoom.Marimba;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Marimba
{
    public class MarimbaFootprintTests
    {
        static MarimbaView Build()
        {
            var lib = new MaterialLibrary();
            return MarimbaView.Build(null, lib, MarimbaGeometry.HSeat);
        }

        [Test]
        public void TheBoxesCoverTheModelAndKeepClearOfTheKnees()
        {
            MarimbaView v = Build();
            try
            {
                InstrumentDescriptor d = new MarimbaDefinition().Describe();
                Vector3 home = d.Seat.Home;
                var hMin = Vector3.positiveInfinity; var hMax = Vector3.negativeInfinity;
                var log = new StringBuilder();
                float kneeClear = float.PositiveInfinity;
                string kneeWorst = null;
                foreach (Renderer r in v.Handles)
                {
                    Bounds b = r.bounds;   // the view's root is at the origin: world = rig-local
                    hMin = Vector3.Min(hMin, b.min); hMax = Vector3.Max(hMax, b.max);
                    // the planner's knees (seat frame): x +-0.22, z 0.09..0.41, y 0.40..0.62, kept 3 cm apart
                    bool xIn = b.min.x < 0.22f + 0.03f && b.max.x > -0.22f - 0.03f;
                    if (xIn && b.min.y < 0.62f && b.max.y > 0.40f)
                    {
                        float zHead = home.z + b.min.z;   // its nearest point, seat frame
                        if (zHead - 0.41f < kneeClear) { kneeClear = zHead - 0.41f; kneeWorst = r.name; }
                    }
                }
                foreach (Renderer r in v.PlayedParts) { hMin = Vector3.Min(hMin, r.bounds.min); hMax = Vector3.Max(hMax, r.bounds.max); }
                var fMin = Vector3.positiveInfinity; var fMax = Vector3.negativeInfinity;
                foreach (FootBox f in d.Seat.Boxes)
                {
                    fMin = Vector3.Min(fMin, new Vector3(f.Cx - f.Hx, f.Y0, f.Cz - f.Hz));
                    fMax = Vector3.Max(fMax, new Vector3(f.Cx + f.Hx, f.Y1, f.Cz + f.Hz));
                }
                log.Append($"[MARIMBA] footprint at H {MarimbaGeometry.HSeat}: model x {hMin.x:F3}..{hMax.x:F3} y {hMin.y:F3}..{hMax.y:F3} z {hMin.z:F3}..{hMax.z:F3}; ");
                log.Append($"boxes x {fMin.x:F3}..{fMax.x:F3} y {fMin.y:F3}..{fMax.y:F3} z {fMin.z:F3}..{fMax.z:F3}; ");
                log.Append($"nearest low handle to the knees: {kneeWorst} {kneeClear * 100:F1} cm beyond the knee box (needs > 3); back at {home.z + hMax.z:F3} m from the eyes (wall 1.10)");
                Debug.Log(log.ToString());
                Vector3 tol = Vector3.one * 0.03f;
                Assert.That(hMin.x >= fMin.x - 0.03f && hMin.y >= fMin.y - 0.03f && hMin.z >= fMin.z - 0.03f, Is.True, "the boxes cover the model (min)");
                Assert.That(hMax.x <= fMax.x + 0.03f && hMax.y <= fMax.y + 0.03f && hMax.z <= fMax.z + 0.03f, Is.True, "the boxes cover the model (max)");
                Assert.That(kneeClear, Is.GreaterThan(0.03f), "nothing below 0.62 m within 3 cm of the knees");
                Assert.That(home.z + hMax.z, Is.LessThanOrEqualTo(1.10f), "inside the front wall");
                // every box below 0.62 m near the knees is clear by the planner's rule (3 cm)
                foreach (FootBox f in d.Seat.Boxes)
                {
                    if (f.Y0 >= 0.62f || f.Cx + f.Hx < -0.25f || f.Cx - f.Hx > 0.25f) continue;
                    Assert.That(home.z + f.Cz - f.Hz, Is.GreaterThan(0.41f + 0.03f), $"box {f} keeps clear of the knees");
                }
            }
            finally { v.Dispose(); }
        }

        static string Plan(SeatPlanner p, params InstrumentKind[] set)
        {
            SeatPlanner.Plan plan = p.PlanSet(set);
            var sb = new StringBuilder();
            sb.Append(string.Join("+", System.Array.ConvertAll(set, k => k.ToString()))).Append(": ");
            if (!plan.Fits) return sb.Append("does not fit (").Append(plan.Failed).Append(' ').Append(plan.FailedWhy).Append(')').ToString();
            foreach (var m in plan.Members) sb.Append(m.Kind).Append('@').Append(m.Slot).Append(m.Push > 0 ? $"+{m.Push * 100:F0}" : "").Append(' ');
            if (plan.TableAlt) sb.Append("(table at its alternate pose)");
            return sb.ToString();
        }

        /// <summary>Diagnostics: which of the marimba's boxes (at Front) touch which box of another instrument at its own
        /// canonical home, or of the side table.</summary>
        static string Clashes(InstrumentRegistry reg, InstrumentKind other)
        {
            InstrumentDescriptor m = reg.Get(InstrumentKind.Marimba), o = reg.Get(other);
            var mp = new SeatPlanner.Pose2(m.Seat.Home.x, m.Seat.Home.z, m.Seat.HomeYaw);
            var op = new SeatPlanner.Pose2(o.Seat.Home.x, o.Seat.Home.z, o.Seat.HomeYaw);
            var tp = new SeatPlanner.Pose2(BuiltIns.TableHome.x, BuiltIns.TableHome.z, BuiltIns.TableHomeYaw);
            var sb = new StringBuilder();
            for (int i = 0; i < m.Seat.Boxes.Length; i++)
            {
                FootBox a = m.Seat.Boxes[i];
                var wa = SeatPlanner.WorldBox(mp, new SeatPlanner.Box(a.Cx, a.Cz, a.Hx, a.Hz, a.Y0, a.Y1));
                for (int j = 0; j < o.Seat.Boxes.Length; j++)
                {
                    FootBox b = o.Seat.Boxes[j];
                    if (SeatPlanner.Overlaps(wa, SeatPlanner.WorldBox(op, new SeatPlanner.Box(b.Cx, b.Cz, b.Hx, b.Hz, b.Y0, b.Y1, b.Air)), SeatPlanner.Gap))
                        sb.Append($"marimba box {i} {a} x {other} box {j} {b}; ");
                }
                FootBox t = BuiltIns.TableBoxes[0];
                if (SeatPlanner.Overlaps(wa, SeatPlanner.WorldBox(tp, new SeatPlanner.Box(t.Cx, t.Cz, t.Hx, t.Hz, t.Y0, t.Y1)), SeatPlanner.Gap)) sb.Append($"marimba box {i} x table; ");
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        [Test]
        public void ThePlannerSeatsItAtFront()
        {
            SeatPlanner p = SeatPlanner.FromRegistry(InstrumentRegistry.Discover());
            var lines = new List<string>
            {
                Plan(p, InstrumentKind.Marimba),
                Plan(p, InstrumentKind.Marimba, InstrumentKind.Drums),
                Plan(p, InstrumentKind.Marimba, InstrumentKind.Harp),
                Plan(p, InstrumentKind.Marimba, InstrumentKind.Drums, InstrumentKind.Harp),
                Plan(p, InstrumentKind.Marimba, InstrumentKind.HandDrums),
                Plan(p, InstrumentKind.Marimba, InstrumentKind.Handpan),
                Plan(p, InstrumentKind.Marimba, InstrumentKind.Bowls),
            };
            var reg = InstrumentRegistry.Discover();
            lines.Add("clashes at the homes with the drums: " + Clashes(reg, InstrumentKind.Drums));
            lines.Add("clashes at the homes with the harp: " + Clashes(reg, InstrumentKind.Harp));
            Debug.Log("[MARIMBA] planner:\n  " + string.Join("\n  ", lines));
            SeatPlanner.Plan alone = p.PlanSet(new[] { InstrumentKind.Marimba });
            Assert.That(alone.Fits, Is.True, lines[0]);
            Assert.That(alone.TryGet(InstrumentKind.Marimba, out var a) && a.Slot == SeatSlot.Front, Is.True, lines[0]);
            var withHarp = new[] { InstrumentKind.Marimba, InstrumentKind.Harp };
            SeatPlanner.Plan harp = p.PlanSet(withHarp);
            Assert.That(harp.Fits, Is.True, Plan(p, withHarp));
            Assert.That(harp.TryGet(InstrumentKind.Marimba, out var mh) && mh.Slot == SeatSlot.Front, Is.True, "the marimba at Front: " + Plan(p, withHarp));
            // With the kit (at Right, placed first: Priority 1) the solid parts clear each other, but the kit's hi-hat
            // cymbal and the cymbal beside it stand in the mallets' and hands' field (the air boxes): over the top octave
            // and under the right hand's grip. The planner keeps them apart, so the two never share a set.
            var withDrums = new[] { InstrumentKind.Marimba, InstrumentKind.Drums };
            Assert.That(p.PlanSet(withDrums).Fits, Is.False, Plan(p, withDrums));
            string clash = Clashes(reg, InstrumentKind.Drums);
            Assert.That(clash, Does.Contain("air x Drums box 8"), "the hi-hat in the hands' field");
            Assert.That(clash, Does.Not.Contain("y 0.62..0.76 x Drums"), "the top's solid boxes clear the kit");
            Assert.That(clash, Does.Not.Contain("y 0.00..0.62 x Drums"), "the low boxes clear the kit");
        }
    }
}
