// The hand drums' display spot (platform.md 4.1, the integrator's note: the room's decor touches window-far at
// (2.64, -1.85)): the displayed footprint (the seat boxes carried to Stow.Pose) stands 3 cm clear of every furniture
// blocker, of every piece of measured decor (the plants' leaves, the ivy on the sill) and of every other instrument's
// display footprint. A search over the floor by the window logs the clearance of the candidate spots (the report's table).
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicRoom.Core;
using MusicRoom.HandDrums;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Room;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class HandDrumsDisplayTests
    {
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true);
            yield return _boot.Start();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() { yield return _boot.Stop(); }

        /// <summary>The smallest clearance (m) of the drums' display boxes at (x, z, yaw) to the furniture, the decor and the
        /// other instruments' displays, and what it is.</summary>
        static float Nearest(InstrumentDescriptor d, float x, float z, float yaw, IRoomFurniture furniture, RoomModule room, out string what)
        {
            var boxes = new List<RoomFurnitureTests.Obb>();
            foreach (FootBox b in d.Seat.Boxes) if (!b.Air) boxes.Add(RoomFurnitureTests.Obb.At(x, z, yaw, b, -d.Seat.Home.y));
            float best = float.PositiveInfinity;
            string nearest = "nothing";
            void Check(RoomFurnitureTests.Obb o, string name)
            {
                foreach (var a in boxes) { float c = RoomFurnitureTests.Clearance(a, o); if (c < best) { best = c; nearest = name; } }
            }
            // in room coordinates (the descriptors' spots are; the room faces the fire: RoomFrame turns both alike)
            foreach (FurnitureBlocker f in room.Furniture.RoomBlockers) Check(RoomFurnitureTests.Obb.Of(f), f.Name);
            foreach (FurnitureBlocker f in room.Furniture.RoomDecor) Check(RoomFurnitureTests.Obb.Of(f), "decor " + f.Name);
            foreach (InstrumentDescriptor o in App.Current.Registry.All)
            {
                if (o.Kind == d.Kind || o.Stow == null) continue;
                foreach (var ob in RoomFurnitureTests.DisplayBoxes(o)) Check(ob, o.Id + " on display");
            }
            what = nearest;
            return best;
        }

        [UnityTest]
        public IEnumerator TheDisplaySpotStandsClearOfTheFurnitureTheDecorAndTheOtherDisplays()
        {
            InstrumentDescriptor d = App.Current.Registry.Get(InstrumentKind.HandDrums);
            var furniture = App.Current.Get<IRoomFurniture>();
            var room = (RoomModule)App.Current.Modules.First(m => m is RoomModule);
            var log = new StringBuilder("[HandDrums] display spot search (clearance to furniture, decor and the other displays):\n");
            foreach (FurnitureBlocker f in room.Furniture.RoomDecor.Concat(room.Furniture.RoomBlockers).Where(f => f.Centre.x > 1.8f))
                log.AppendLine($"  {f.Name}: centre ({f.Centre.x:F2}, {f.Centre.y:F2}) half ({f.Half.x:F2}, {f.Half.y:F2}) y {f.Y0:F2}..{f.Y1:F2} yaw {f.Yaw:F0}");
            var cands = new List<(float c, float x, float z, float yaw, string what)>();
            foreach (float yaw in new[] { 90f, 75f, 105f })
                for (float x = 2.30f; x <= 2.90f; x += 0.02f)
                    for (float z = -3.0f; z <= -0.3f; z += 0.02f)
                    {
                        float c = Nearest(d, x, z, yaw, furniture, room, out string w);
                        cands.Add((c, x, z, yaw, w));
                    }
            foreach (var c in cands.Where(c => c.yaw == 90f).OrderByDescending(c => c.c).Take(4))
                log.AppendLine($"  roomiest ({c.x:F2}, {c.z:F2}) yaw {c.yaw:F0}: {c.c * 100f:F1} cm to {c.what}");
            // nearest the window: per z band, the largest x that keeps 8 cm everywhere
            foreach (float yaw in new[] { 90f, 75f, 105f })
                foreach (var g in cands.Where(c => c.yaw == yaw && c.c >= 0.08f).GroupBy(c => Mathf.Round(c.z * 10f) / 10f).OrderBy(g => g.Key))
                {
                    var c = g.OrderByDescending(q => q.x).ThenByDescending(q => q.c).First();
                    log.AppendLine($"  by the window, yaw {yaw:F0}, z {g.Key:F1}: ({c.x:F2}, {c.z:F2}) {c.c * 100f:F1} cm to {c.what}");
                }
            StowSpec s = d.Stow;
            float here = Nearest(d, s.Pose.x, s.Pose.z, s.Yaw, furniture, room, out string near);
            float old = Nearest(d, 2.64f, -1.85f, 90f, furniture, room, out string oldNear);
            log.AppendLine($"  phase 0's (2.64, -1.85) yaw 90: {old * 100f:F1} cm to {oldNear}");
            log.AppendLine($"  chosen {s.SpotId} ({s.Pose.x:F2}, {s.Pose.z:F2}) yaw {s.Yaw:F0}: {here * 100f:F1} cm to {near}");
            Debug.Log(log.ToString());
            Assert.That(here, Is.GreaterThanOrEqualTo(RoomFurnitureTests.Gap), $"the display spot is {here * 100f:F1} cm from {near}");
            yield return null;
        }
    }
}
