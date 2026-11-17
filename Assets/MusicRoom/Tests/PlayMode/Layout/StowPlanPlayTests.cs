// The display spots against the Room's real furniture (platform.md 4.1, 12.1 StowPlanTests, PlayMode): with every module
// booted, each displayed instrument's place is inside the room and 3 cm clear of IRoomFurniture.FloorBlockers. Until a
// module registers IRoomFurniture (workstream R) the test is skipped with a message; the EditMode StowPlanTests check the
// same spots against stow-plan.mjs's reference blockers.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class StowPlanPlayTests
    {
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator TheDisplayedInstrumentsClearTheRoomsFurniture()
        {
            _boot = new TestBoot(headless: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Current.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            _boot.Step(1f / 72f);
            IRoomFurniture furniture = App.Current.Get<IRoomFurniture>();
            if (furniture == null)
            {
                Assert.Ignore("no module registers IRoomFurniture yet (the Room's blockers come with workstream R): the EditMode " +
                              "StowPlanTests check the spots against stow-plan.mjs's reference furniture");
                yield break;
            }
            var layout = App.Current.Get<ILayoutService>() as LayoutModule;
            Assert.That(layout, Is.Not.Null);
            var blockers = new List<SeatPlanner.FloorObb>();
            foreach (FurnitureBlocker f in furniture.FloorBlockers) blockers.Add(StowPlan.BlockerBox(f));
            Assert.That(blockers.Count, Is.GreaterThan(0), "the room publishes its furniture");
            int shown = 0;
            foreach (StowPlan.Result r in layout.Display.Results)
            {
                if (r.Spot == null) continue;
                shown++;
                var boxes = new List<SeatPlanner.FloorObb>();
                r.Spot.WorldBoxes(boxes);
                foreach (SeatPlanner.FloorObb o in boxes)
                {
                    Assert.IsTrue(SeatPlanner.InsideRoom(o), r.Kind + " inside the room");
                    for (int i = 0; i < blockers.Count; i++)
                        Assert.IsFalse(SeatPlanner.Overlaps(o, blockers[i], StowPlan.Gap), $"{r.Kind} at {r.Spot.Id} hits {furniture.FloorBlockers[i].Name}");
                }
                Assert.That(r.Spot.Distance(0.0, 0.0), Is.GreaterThanOrEqualTo(StowPlan.MinDistance), r.Kind + " 1.3 m or more from the head");
            }
            Assert.That(shown, Is.EqualTo(4), "the four new instruments on display (the classic three are at the seat)");
        }
    }
}
