// The saved placement of "Move instruments" (ArrangementCodec) and the arrange-mode height ranges: a pose survives the
// round trip through the seat frame and the text, wrong or damaged text is dropped, and the ranges are the documented
// seated-comfort ones (piano key tops 0.55-0.95 m, kit -0.17..+0.15 m, harp and table on the floor).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public class ArrangementCodecTests
    {
        [Test]
        public void SeatPosesRoundTripThroughAnyHeadAndHeading()
        {
            var head = new Vector3(0.4f, 1.15f, -0.7f);
            float seatYaw = 35f * Mathf.Deg2Rad;
            var world = new Vector3(1.1f, 0.81f, 0.2f);
            SeatPose sp = ArrangementCodec.ToSeat(world, 120f, head, seatYaw);
            Assert.That(sp.Local.y, Is.EqualTo(0.81f).Within(1e-5f), "the height is absolute");
            Assert.That(sp.Yaw, Is.EqualTo(85f).Within(1e-3f), "the heading is relative to the seat");
            ArrangementCodec.FromSeat(sp, head, seatYaw, out Vector3 back, out float yawBack);
            Assert.That(Vector3.Distance(back, world), Is.LessThan(1e-5f));
            Assert.That(yawBack, Is.EqualTo(120f).Within(1e-3f));
            // The same seat pose about another seat lands at the same place relative to that seat.
            var head2 = new Vector3(-2f, 1.3f, 0.5f);
            float yaw2 = -90f * Mathf.Deg2Rad;
            ArrangementCodec.FromSeat(sp, head2, yaw2, out Vector3 moved, out float movedYaw);
            SeatPose again = ArrangementCodec.ToSeat(moved, movedYaw, head2, yaw2);
            Assert.That(Vector3.Distance(again.Local, sp.Local), Is.LessThan(1e-5f));
            Assert.That(Mathf.DeltaAngle(again.Yaw, sp.Yaw), Is.EqualTo(0f).Within(1e-3f));
            // A seat pose (x right, z ahead): ahead of a seat facing +z is +z.
            SeatPose ahead = ArrangementCodec.ToSeat(new Vector3(0f, 0.72f, 0.45f), 0f, new Vector3(0f, 1.2f, 0f), 0f);
            Assert.That(ahead.Local, Is.EqualTo(new Vector3(0f, 0.72f, 0.45f)));
        }

        [Test]
        public void TextRoundTripAndDamagedText()
        {
            var poses = new List<KeyValuePair<RigId, SeatPose>>
            {
                new KeyValuePair<RigId, SeatPose>(RigId.Piano, new SeatPose { Local = new Vector3(0.012f, 0.83f, 0.47f), Yaw = -12.5f }),
                new KeyValuePair<RigId, SeatPose>(RigId.Drums, new SeatPose { Local = new Vector3(0.61f, -0.1f, 0.05f), Yaw = 101f }),
            };
            string text = ArrangementCodec.Encode(poses);
            Assert.That(text, Does.StartWith("a1;Piano:"));
            var into = new Dictionary<RigId, SeatPose>();
            Assert.That(ArrangementCodec.Decode(text, into), Is.EqualTo(2));
            Assert.That(Vector3.Distance(into[RigId.Piano].Local, poses[0].Value.Local), Is.LessThan(1e-4f));
            Assert.That(into[RigId.Drums].Yaw, Is.EqualTo(101f).Within(1e-3f));
            Assert.That(ArrangementCodec.Decode("", into), Is.EqualTo(0));
            Assert.That(ArrangementCodec.Decode("b9;Piano:0,0,0,0", into), Is.EqualTo(0), "another version: nothing");
            Assert.That(ArrangementCodec.Decode("a1;Piano:0,x,0,0;Harp:0,0,0.4,180;Organ:1,1,1,1;Drums:1,2", into), Is.EqualTo(1), "only the harp is whole");
            Assert.IsTrue(into.ContainsKey(RigId.Harp));
            Assert.That(ArrangementCodec.Decode("a1;Piano:1e9,0,0,0", into), Is.EqualTo(0), "absurd numbers are dropped");
            Assert.That(ArrangementCodec.Encode(new List<KeyValuePair<RigId, SeatPose>>()), Is.EqualTo(""));
        }

        [Test]
        public void SettingsKeysPerLayout()
        {
            Assert.That(AppSettings.ArrangeKey(LayoutMode.All, InstrumentKind.Harp), Is.EqualTo("musicroom.arrange.all"));
            Assert.That(AppSettings.ArrangeKey(LayoutMode.Single, InstrumentKind.Piano), Is.EqualTo("musicroom.arrange.single.piano"));
            Assert.That(AppSettings.ArrangeKey(LayoutMode.Single, InstrumentKind.Drums), Is.EqualTo("musicroom.arrange.single.drums"));
            Assert.That(AppSettings.ArrangeKey(LayoutMode.Single, InstrumentKind.Harp), Is.EqualTo("musicroom.arrange.single.harp"));
            var s = new AppSettings(new MemorySettingsStore());
            Assert.That(s.GetArrangement(LayoutMode.All, InstrumentKind.Piano), Is.EqualTo(""));
            s.SetArrangement(LayoutMode.All, InstrumentKind.Piano, "a1;Piano:0,0.72,0.45,0");
            Assert.That(s.GetArrangement(LayoutMode.All, InstrumentKind.Drums), Is.EqualTo("a1;Piano:0,0.72,0.45,0"), "one key for the full room");
            Assert.That(s.GetArrangement(LayoutMode.Single, InstrumentKind.Piano), Is.EqualTo(""), "the single layouts are their own");
            s.SetArrangement(LayoutMode.All, InstrumentKind.Piano, "");
            Assert.IsFalse(s.Store.HasKey("musicroom.arrange.all"), "an empty placement removes the key");
        }

        [Test]
        public void MergeOnSaveKeepsTheInstrumentsNotAtTheSeat()
        {
            // platform.md 4.7: the full room's key keeps the entries of rigs that are not in the current set.
            var before = new List<KeyValuePair<RigId, SeatPose>>
            {
                new KeyValuePair<RigId, SeatPose>(RigId.Piano, new SeatPose { Local = new Vector3(0f, 0.72f, 0.47f), Yaw = 0f }),
                new KeyValuePair<RigId, SeatPose>(RigId.Harp, new SeatPose { Local = new Vector3(-0.8f, 0f, -0.25f), Yaw = 80f }),
                new KeyValuePair<RigId, SeatPose>(RigId.Handpan, new SeatPose { Local = new Vector3(-0.45f, 0f, 0.1f), Yaw = -90f }),
            };
            string previous = ArrangementCodec.Encode(before);
            // now the set is piano, drums, handpan: the harp is on display
            var now = new List<KeyValuePair<RigId, SeatPose>>
            {
                new KeyValuePair<RigId, SeatPose>(RigId.Piano, new SeatPose { Local = new Vector3(0.02f, 0.75f, 0.5f), Yaw = 2f }),
                new KeyValuePair<RigId, SeatPose>(RigId.Drums, new SeatPose { Local = new Vector3(0.2f, 0f, -0.26f), Yaw = 90f }),
                new KeyValuePair<RigId, SeatPose>(RigId.Handpan, new SeatPose { Local = new Vector3(-0.5f, 0f, 0.05f), Yaw = -88f }),
            };
            var into = new Dictionary<RigId, SeatPose>();
            Assert.That(ArrangementCodec.Decode(ArrangementCodec.Merge(previous, now), into), Is.EqualTo(4), "piano, drums, handpan and the kept harp");
            Assert.That(into[RigId.Piano].Local.z, Is.EqualTo(0.5f).Within(1e-4f), "the current pose replaces the saved one");
            Assert.That(into[RigId.Handpan].Yaw, Is.EqualTo(-88f).Within(1e-3f));
            Assert.That(into[RigId.Harp].Local.x, Is.EqualTo(-0.8f).Within(1e-4f), "the harp, now on display, keeps where the player put it");
            Assert.That(ArrangementCodec.Merge("", now), Is.EqualTo(ArrangementCodec.Encode(now)), "nothing saved before: the current set");
        }

        [Test]
        public void ASavedPoseThatNowCollidesIsDroppedForThePlannersPose()
        {
            SeatPlanner planner = PlatformVectors.Planner();
            var set = new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan };
            SeatPlanner.Plan plan = planner.PlanSet(set);
            Assert.IsTrue(plan.Fits);
            Assert.IsTrue(plan.TryGet(InstrumentKind.Handpan, out SeatPlanner.Member hp));
            var saved = new Dictionary<RigId, SeatPose>
            {
                // the piano where the player nudged it: fine
                [RigId.Piano] = new SeatPose { Local = new Vector3(0.01f, 0.72f, 0.46f), Yaw = 0f },
                // the handpan saved (with an older set) right where the drums now stand: it collides
                [RigId.Handpan] = new SeatPose { Local = new Vector3(0.35f, 0f, 0f), Yaw = 90f },
            };
            var usable = new HashSet<RigId>();
            ArrangementCodec.Usable(planner, plan, saved, usable);
            Assert.IsTrue(usable.Contains(RigId.Piano), "a saved pose that still fits is used");
            Assert.IsFalse(usable.Contains(RigId.Handpan), "a saved pose that now collides is dropped (the planner's pose is used)");
            // the same handpan pose with the drums out of the set is fine again
            SeatPlanner.Plan two = planner.PlanSet(new[] { InstrumentKind.Piano, InstrumentKind.Handpan });
            ArrangementCodec.Usable(planner, two, saved, usable);
            Assert.IsTrue(usable.Contains(RigId.Handpan), "with the kit on display the saved handpan pose is clear");
            // a set whose table yields to its alternate does not use a saved table pose
            saved[RigId.Table] = new SeatPose { Local = new Vector3(-0.39f, 0f, -0.21f), Yaw = -90f };
            SeatPlanner.Plan alt = planner.PlanSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan });
            Assert.IsTrue(alt.TableAlt, "piano + drums + handpan: the handpan takes the left slot, the table yields");
            ArrangementCodec.Usable(planner, alt, saved, usable);
            Assert.IsFalse(usable.Contains(RigId.Table));
        }

        [Test]
        public void HeightRangesReachFromALowSeatToStanding()
        {
            // 2026-11-11 ("allow it place instrument to their height/level"): a seated player's low elbows to a standing one's.
            Assert.That(ArrangeConstants.PianoKeyMin, Is.EqualTo(0.55f));
            Assert.That(ArrangeConstants.PianoKeyMax, Is.EqualTo(1.10f));
            var piano = new Rig { Id = RigId.Piano, HomeAll = new Vector3(0f, 0.72f, 0.45f) };
            LayoutModule.LiftRange(piano, out float lo, out float hi);
            Assert.That(lo, Is.EqualTo(0.55f).Within(1e-6f));
            Assert.That(hi, Is.EqualTo(1.10f).Within(1e-6f));
            Assert.That(0.72f, Is.InRange(lo, hi), "the home height is inside");
            // The floor instruments: from the floor (never into it: the kit's -0.17 m sank its kick through the real floor in
            // mixed reality) up to a standing height on a riser.
            var drums = new Rig { Id = RigId.Drums, HomeAll = new Vector3(0.2f, 0f, -0.26f) };
            LayoutModule.LiftRange(drums, out lo, out hi);
            Assert.That(lo, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(hi, Is.EqualTo(0.35f).Within(1e-6f));
            var harp = new Rig { Id = RigId.Harp, HomeAll = new Vector3(-0.78f, 0f, -0.2f) };
            LayoutModule.LiftRange(harp, out lo, out hi);
            Assert.That(lo, Is.EqualTo(0f));
            Assert.That(hi, Is.EqualTo(0.30f).Within(1e-6f), "the harp on a riser");
            var table = new Rig { Id = RigId.Table, HomeAll = new Vector3(-0.39f, 0f, -0.21f) };
            LayoutModule.LiftRange(table, out lo, out hi);
            Assert.That(hi - lo, Is.EqualTo(0f), "the side table keeps its height");
            Assert.That(ArrangeConstants.NearReach, Is.EqualTo(0.08f));
            Assert.That(ArrangeConstants.GrabDebounce, Is.EqualTo(0.08f));
            Assert.That(ArrangeConstants.RayPushGain, Is.EqualTo(2.5f));
        }
    }
}
