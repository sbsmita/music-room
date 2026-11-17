// The Layout module end to end with the golden rigs: the first arrangement, fitSeat with the fix, Set/Next and
// their membership side effects, the page-turn sweep and animation, mixed reality.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class LayoutModeTests : LayoutPlayTestBase
    {
        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        static GoldenLayout.Scenario Find(string label)
        {
            foreach (var s in GoldenLayout.Scenarios) if (s.Label == label) return s;
            throw new System.ArgumentException(label);
        }

        void AssertRigs(GoldenLayout.RigPose[] want, string what)
        {
            for (int r = 0; r < 4; r++)
            {
                Rig rig = Probe.Rigs[r];
                Assert.That(rig.InScene, Is.EqualTo(want[r].InScene), $"{what}: {GoldenLayout.RigNames[r]} in the scene");
                Assert.That(rig.Group.gameObject.activeSelf, Is.EqualTo(want[r].InScene), $"{what}: {GoldenLayout.RigNames[r]} active");
                if (!want[r].InScene) continue;
                Assert.That(Vector3.Distance(rig.Group.position, want[r].Pos), Is.LessThan(2e-3f), $"{what}: {GoldenLayout.RigNames[r]} at {rig.Group.position.ToString("F4")}, golden {want[r].Pos.ToString("F4")}");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(rig.Group.eulerAngles.y, want[r].YawDeg)), Is.LessThan(0.05f), $"{what}: {GoldenLayout.RigNames[r]} yaw");
            }
        }

        [UnityTest]
        public IEnumerator TheFirstPresentingFrameLaysTheGoldenRoomOutAboutTheHead()
        {
            // The simulated headset presents from the first frame with a pose (the head is the desktop rig's eye here):
            // vrAuto arranges about it, with fitSeat (the full room fits: no seat move).
            var s = Find("all-desktop");
            Assert.That(Layout.XrArrangements, Is.EqualTo(1), "one arrangement for the session start");
            Assert.That(Vector3.Distance(Head.position, s.Head), Is.LessThan(1e-4f), "the eye at (0, 1.2, -0.1)");
            Assert.That(Layout.LastSeatShift, Is.EqualTo(Vector3.zero), "the full room fits: no seat move");
            AssertRigs(s.FixedAfter, "first arrangement");
            for (int r = 0; r < 4; r++) Assert.That(Layout.EyeDistance(Probe.Rigs[r]), Is.EqualTo(s.Eye[r]).Within(2e-3f));
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero);
            var menuHome = LayoutMath.SeatPlace(s.Head, 0f, MenuConstants.HomeSeat);
            Assert.That(Vector3.Distance(Probe.Menu.Root.position, menuHome), Is.LessThan(1e-4f), "the menu's home (0, 1.03, .47) about the seat");
            Assert.That(Layout.Seat.Head, Is.EqualTo(Head.position));
            Assert.That(Layout.Seat.YawDeg, Is.EqualTo(0f).Within(1e-3f));
            Assert.IsNull(Layout.RoomShift(), "IPlacementService.RoomShift: nothing to fix");
            Assert.IsFalse(Layout.MoveOn, "Move instruments is off by default");
            Assert.IsFalse(Layout.Arranging);
            Assert.That(Layout.Mode, Is.EqualTo(LayoutMode.All));
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Piano));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ATurnLandsExactlyWhereSetWouldPutIt()
        {
            // The page turn with "the head unmoved": next() to the harp, then set('single', 'harp'). The carousel of
            // eight (platform.md 4.3; the three classic ones loaded here: piano -> harp -> drums -> piano) reaches the harp
            // from the piano.
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Layout.Next(1);
            yield return Frames(26);
            Transform harp = Probe.Rigs[GoldenLayout.Harp].Group;
            Vector3 turned = harp.position;
            float turnedYaw = harp.eulerAngles.y;
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(Vector3.Distance(harp.position, turned), Is.LessThan(1e-5f));
            Assert.That(Mathf.DeltaAngle(harp.eulerAngles.y, turnedYaw), Is.EqualTo(0f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator TheIncomingInstrumentStaysOutOfPlayUntilTheTurnEnds()
        {
            // StartTurn only adds the incoming group to the scene; its instrument and rig join the lists in
            // FinishTurn's apply(). While it slides it is shown but never ticked, hovered, grabbed or "near".
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Rig harp = Probe.Rigs[GoldenLayout.Harp], kit = Probe.Rigs[GoldenLayout.Drums];
            RigEntry e = Layout.Placement.Entry(harp);
            yield return Frame();
            int harpTicks = Probe.Instruments[GoldenLayout.Harp].TickCount, kitTicks = Probe.Instruments[GoldenLayout.Drums].TickCount;
            Layout.Next(-1);   // the carousel of eight: the harp comes before the kit (piano -> harp -> drums)
            Assert.IsTrue(Layout.IsTurning);
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(harp.Group.gameObject.activeSelf, "the incoming harp is drawn while it slides");
                Assert.IsFalse(harp.InScene, "but not in the layout's lists yet");
                Assert.IsFalse(PlacementSolver.InScene(e), "nor the placement's");
                Assert.That(Layout.GrabTarget(Handle(GoldenLayout.Harp)), Is.Not.SameAs(harp), "nor a grab target");
                Assert.IsFalse(Layout.ZoneOf(harp).HasValue, "nor has it a zone");
                Assert.IsTrue(kit.InScene, "the outgoing kit stays live until the turn ends");
                yield return Frame();
                if (!Layout.IsTurning) break;
            }
            Assert.That(Probe.Instruments[GoldenLayout.Harp].TickCount, Is.EqualTo(harpTicks), "the sliding harp was never ticked");
            Assert.That(Probe.Instruments[GoldenLayout.Drums].TickCount, Is.GreaterThan(kitTicks), "the outgoing kit was");
            yield return Frames(10);
            Assert.IsFalse(Layout.IsTurning);
            Assert.IsTrue(harp.InScene, "in the layout once the turn ends");
            Assert.That(Layout.GrabTarget(Handle(GoldenLayout.Harp)), Is.SameAs(harp));
            Assert.IsTrue(Layout.ZoneOf(harp).HasValue);
            yield return Frame();
            Assert.That(Probe.Instruments[GoldenLayout.Harp].TickCount, Is.GreaterThan(harpTicks), "and played");
        }

        [UnityTest]
        public IEnumerator ThePageTurnsSettleRefreshesTheZonesOfTheRigsItMoved()
        {
            // After a swivel the side table, laid out about the old heading, can be where the incoming instrument comes to
            // rest: the turn's settle pushes the table (lowest priority) clear, and its zone must follow
            // (SettlePlacement refreshes the zone of every moved rig), or a pinch just above it reads as empty air.
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Rig harp = Probe.Rigs[GoldenLayout.Harp], table = Probe.Rigs[GoldenLayout.Table];
            Vector3 t0 = table.Group.position;
            Bounds z0 = Layout.ZoneOf(table).Value;
            // A seat facing -x (Unity yaw -90) whose single harp home lands right on the table.
            const float yaw = -90f;
            Vector3 off = LayoutMath.SeatPlace(Vector3.zero, yaw * Mathf.Deg2Rad, harp.HomeSingle);
            SeatAt(new Vector3(t0.x - off.x, 1.2f, t0.z - off.z), yaw, arrange: false);
            Layout.Next(-1);   // the carousel of eight: the harp comes before the kit (piano -> harp -> drums)
            Assert.IsTrue(Layout.IsTurning);
            Vector3 moved = table.Group.position - t0;
            Assert.That(new Vector2(moved.x, moved.z).magnitude, Is.GreaterThan(0.05f), "the settle pushed the table clear of the harp's rest");
            Bounds z1 = Layout.ZoneOf(table).Value;
            Assert.That(Vector3.Distance(z1.center, z0.center + moved), Is.LessThan(1e-3f), "the table's zone moved with it");
            Assert.That(Vector3.Distance(z1.size, z0.size), Is.LessThan(1e-3f));
            // A point the moved table's zone holds and its old zone did not.
            bool alongX = Mathf.Abs(moved.x) >= Mathf.Abs(moved.z);
            Vector3 edge = z1.center + (alongX ? new Vector3(Mathf.Sign(moved.x) * (z1.extents.x - 0.01f), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(moved.z) * (z1.extents.z - 0.01f)));
            Assert.IsFalse(z0.Contains(edge));
            Assert.IsTrue(Layout.NearInstrument(edge), "a pinch at the moved table is near an instrument");
            yield return Frames(30);
            Assert.IsFalse(Layout.IsTurning);
        }

        [UnityTest]
        public IEnumerator TheMenuOpenedInsideAnInstrumentIsClearedOut()
        {
            // ClearPanel on open: the panel shown inside the piano's body comes out of it.
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Vector3 inside = piano.TransformPoint(new Vector3(0f, 0.05f, 0.2f));
            Probe.Menu.ShowAt(inside);
            Assert.That(Vector3.Distance(Probe.Menu.Root.position, inside), Is.GreaterThan(0.02f), "moved");
            Layout.Placement.Panel.Group = Probe.Menu.Root;
            Layout.Placement.Panel.Shown = true;
            Assert.That(Layout.Placement.Overlaps3D(), Is.Zero, "out of the instruments' volume");
            Vector3 toPanel = Probe.Menu.Root.position - Head.position;
            Assert.That(Mathf.DeltaAngle(Probe.Menu.Root.eulerAngles.y, Mathf.Atan2(toPanel.x, toPanel.z) * Mathf.Rad2Deg), Is.EqualTo(0f).Within(0.1f), "still facing the player (+z away)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FitSeatMovesTheSeatBackInsteadOfSlidingTheKit()
        {
            // The cliff (spec 07 section 8): single drums at the XR origin slides the kit 0.73 m to the right. With
            // the fixed dry run the seat (the player's rig) moves back 11.9 cm and the kit stays centred.
            // The golden faced the reference room's front wall 1.12 m ahead. The room faces the fire now (RoomFrame, a half
            // turn about the seat), so that wall is behind the seat: the same scene is the seat at the origin facing it.
            var s = Find("single-drums-origin");
            SeatAt(RoomFrame.ToWorld(s.Head), RoomFrame.Yaw, arrange: false);
            int arranged = 0;
            App.Events.Subscribe<ArrangedEvent>(_ => arranged++);
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Assert.That(arranged, Is.EqualTo(1), "Set re-arranges around the player");
            Vector3 shift = RoomFrame.DirToWorld(new Vector3(s.FixedShift.x, 0f, s.FixedShift.y));
            Assert.That(Layout.LastSeatShift.x, Is.EqualTo(shift.x).Within(1e-3f));
            Assert.That(Layout.LastSeatShift.z, Is.EqualTo(shift.z).Within(1e-3f), "the seat moves away from the wall");
            Assert.That(Vector3.Distance(Head.position, RoomFrame.ToWorld(s.FixedHead)), Is.LessThan(1e-3f), $"the head moved to {Head.position.ToString("F4")}");
            var turned = new GoldenLayout.RigPose[s.FixedAfter.Length];
            for (int r = 0; r < turned.Length; r++)
                turned[r] = new GoldenLayout.RigPose { InScene = s.FixedAfter[r].InScene, Pos = RoomFrame.ToWorld(s.FixedAfter[r].Pos), YawDeg = s.FixedAfter[r].YawDeg + RoomFrame.Yaw };
            AssertRigs(turned, "single drums after fitSeat");
            Assert.That(Mathf.Abs(Probe.Rigs[GoldenLayout.Drums].Group.position.x - Head.position.x), Is.LessThan(0.01f), "kit centred");
            Assert.That(Layout.EyeDistance(Probe.Rigs[GoldenLayout.Drums]), Is.GreaterThanOrEqualTo(0.3f - 1e-4f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SetChangesMembershipPersistsAndPublishes()
        {
            int layoutEvents = 0;
            LayoutChangedEvent last = default;
            App.Events.Subscribe<LayoutChangedEvent>(e => { layoutEvents++; last = e; });
            Probe.Tutorial.State = TutorialState.Playing;
            Probe.Tutorial.LessonId = "piano-scale";
            Probe.Tutorial.Instrument = InstrumentKind.Piano;
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Assert.That(Layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Drums));
            Assert.IsTrue(Layout.IsInScene(InstrumentKind.Drums));
            Assert.IsFalse(Layout.IsInScene(InstrumentKind.Piano));
            Assert.IsFalse(Layout.IsInScene(InstrumentKind.Harp));
            Assert.IsTrue(Layout.IsInScene(InstrumentKind.Table));
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Piano].Group.gameObject.activeSelf, "out of the scene");
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Harp].Group.gameObject.activeSelf);
            Assert.IsTrue(Probe.Rigs[GoldenLayout.Drums].Group.gameObject.activeSelf);
            Assert.IsTrue(Probe.Rigs[GoldenLayout.Table].Group.gameObject.activeSelf, "the side table stays");
            Assert.That(Probe.Tutorial.StopCount, Is.EqualTo(1), "the piano lesson stops when the piano leaves");
            Assert.That(layoutEvents, Is.EqualTo(1));
            Assert.That(last.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(last.Current, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(App.Settings.LayoutMode, Is.EqualTo(LayoutMode.Single), "persisted");
            Assert.That(App.Settings.CurrentInstrument, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(Layout.GrabTarget(Handle(GoldenLayout.Piano)), Is.Not.SameAs(Probe.Rigs[GoldenLayout.Piano]), "a rig out of the scene is never a grab target");
            Assert.IsFalse(Layout.ZoneOf(Probe.Rigs[GoldenLayout.Harp]).HasValue, "nor has it a zone");

            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(Probe.Drums.ReleaseCount, Is.EqualTo(1), "the sticks go with the kit");
            Assert.That(layoutEvents, Is.EqualTo(2));
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(layoutEvents, Is.EqualTo(2), "no change, no event");
            // Move instruments is a setting of its own: it persists and is no layout change.
            Layout.BeginArrange();
            Assert.IsTrue(App.Settings.MoveInstruments, "Move instruments persists");
            Layout.EndArrange();
            Assert.IsFalse(App.Settings.MoveInstruments);
            Assert.That(layoutEvents, Is.EqualTo(2));
            Layout.Set(LayoutMode.All, InstrumentKind.Piano);
            for (int r = 0; r < 4; r++) Assert.IsTrue(Probe.Rigs[r].Group.gameObject.activeSelf, "all mode: everything back");
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HarpSingleModeStringsLandAheadOfASwivelledSeat()
        {
            // The seat moved and turned before the page turns; the harp's strings land 0.45 m
            // ahead of the new seat, centred, at yaw seat + 180.
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            SeatAt(new Vector3(0.3f, 1.2f, -0.1f), -90f, arrange: false);
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Transform harp = Probe.Rigs[GoldenLayout.Harp].Group;
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < 22; i++) mid += (GoldenLayout.HarpA[i] + GoldenLayout.HarpB[i]) * 0.5f;
            mid = harp.TransformPoint(mid / 22f);
            Vector3 d = mid - Head.position;
            Vector3 fwd = Vector3.left, right = Vector3.forward;   // yaw -90
            Assert.That(Vector3.Dot(d, fwd), Is.EqualTo(0.45f).Within(0.02f), "0.45 m ahead");
            Assert.That(Vector3.Dot(d, right), Is.EqualTo(0f).Within(0.02f), "centred across");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(harp.eulerAngles.y, -90f + 180f)), Is.LessThan(2f));
            yield return null;
        }

        internal static HandPose SweepPose(Vector3 palm) =>
            HandPose.OpenFacing(HandSide.Right, palm + Vector3.down * 0.05f, Vector3.up, Vector3.left);

        IEnumerator Sweep(Vector3 start, Vector3 step, int frames)
        {
            Vector3 p = start;
            Fake.SetPose(HandSide.Right, SweepPose(p));
            for (int i = 0; i < 6; i++) yield return Frame();   // at rest: the detector is armed
            for (int i = 0; i < frames; i++)
            {
                p += step;
                Fake.SetPose(HandSide.Right, SweepPose(p));
                yield return Frame();
                if (Layout.IsTurning) break;
            }
        }

        [UnityTest]
        public IEnumerator ASweepTurnsThePageWithASlide()
        {
            // The carousel of eight (platform.md 4.3; the three classic ones loaded here): next from the kit wraps to the piano.
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            int started = 0, ended = 0, layoutEvents = 0;
            TurnEvent turn = default;
            App.Events.Subscribe<TurnEvent>(e => { if (e.Started) { started++; turn = e; } else ended++; });
            App.Events.Subscribe<LayoutChangedEvent>(e => layoutEvents++);
            Transform kit = Probe.Rigs[GoldenLayout.Drums].Group, harp = Probe.Rigs[GoldenLayout.Piano].Group;   // the incoming one
            Vector3 kitRest = kit.position;
            Vector3 palm = Head.position + new Vector3(0.12f, -0.15f, 0.35f);
            // 3 cm a frame to the player's left at 72 Hz (2.2 m/s), palm vertical and open: next.
            yield return Sweep(palm, new Vector3(-0.03f, 0f, 0f), 10);
            Assert.IsTrue(Layout.IsTurning, "the sweep fired");
            Assert.That(started, Is.EqualTo(1));
            Assert.That(turn.Direction, Is.EqualTo(1));
            Assert.That(turn.From, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(turn.To, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Piano), "current switches at the start");
            Assert.IsTrue(harp.gameObject.activeSelf && kit.gameObject.activeSelf, "both in the scene while turning");
            // Mid-turn (12 frames = 0.167 s): out to the left, in from the right, both shrunk.
            yield return Frames(11);
            Vector3 right = Layout.TurnRight;
            Assert.That(Vector3.Dot(kit.position - Layout.TurnRestOut, right), Is.LessThan(-0.3f), "outgoing slides left");
            Assert.That(Vector3.Dot(harp.position - Layout.TurnRestIn, right), Is.GreaterThan(0.3f), "incoming comes from the right");
            Assert.That(kit.localScale.x, Is.LessThan(1f));
            Assert.That(harp.localScale.x, Is.LessThan(1f));
            Assert.That(Layout.FadeOf(Probe.Rigs[GoldenLayout.Drums]), Is.LessThan(1f));
            // 0.35 s in all: done.
            yield return Frames(14);
            Assert.IsFalse(Layout.IsTurning);
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(layoutEvents, Is.EqualTo(1), "the turn's end (Set's own event came before the subscription)");
            Assert.IsFalse(kit.gameObject.activeSelf, "the kit left");
            Assert.IsTrue(harp.gameObject.activeSelf);
            Assert.That(kit.position, Is.EqualTo(kitRest), "the kit back at its rest pose, out of the scene");
            Assert.That(harp.localScale, Is.EqualTo(Vector3.one));
            Assert.That(Vector3.Distance(harp.position, Layout.TurnRestIn), Is.LessThan(1e-5f));
            Assert.That(Probe.Drums.ReleaseCount, Is.EqualTo(1), "the sticks went with the kit");
            Assert.That(Layout.Turns, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TheFullRoomIgnoresSweepsAndNextWraps()
        {
            Vector3 palm = Head.position + new Vector3(0.12f, -0.15f, 0.35f);
            yield return Sweep(palm, new Vector3(-0.03f, 0f, 0f), 10);
            Assert.IsFalse(Layout.IsTurning, "all mode: a sweep is not a page turn");
            Assert.That(Layout.Turns, Is.Zero);
            Assert.That(Layout.Sweep.Count, Is.EqualTo(1), "the gesture itself was seen");
            // The carousel of eight (platform.md 4.3) with the three classic ones loaded: piano -> harp -> drums -> piano.
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Layout.Next(1);
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Piano), "drums -> piano wraps round");
            Layout.Next(1);
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Piano), "no new turn while one runs");
            yield return Frames(26);
            Layout.Next(1);
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Harp), "the first swipe from the piano goes to the harp");
            yield return Frames(26);
            Layout.Next(-1);
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Piano), "previous");
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Assert.IsFalse(Layout.IsTurning, "Set finishes a turn in progress");
            Assert.IsTrue(Probe.Rigs[GoldenLayout.Drums].Group.gameObject.activeSelf);
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Harp].Group.gameObject.activeSelf);
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Piano].Group.gameObject.activeSelf);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MixedRealityHasNoWallsAndNoSeatFit()
        {
            var s = Find("all-ar-far");
            Probe.Mr.Active = true;
            SeatAt(s.Head, s.YawDeg);
            Assert.That(Layout.LastSeatShift, Is.EqualTo(Vector3.zero));
            Assert.That(Vector3.Distance(Head.position, s.Head), Is.LessThan(1e-4f), "the seat never moves in mixed reality");
            AssertRigs(s.After, "around the player, far outside the virtual room");
            Assert.IsNull(Layout.RoomShift());
            Probe.Mr.Active = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecentringReArrangesAroundThePlayer()
        {
            SeatAt(new Vector3(1f, 1.2f, -1f), 45f, arrange: false);
            int arranged = 0;
            App.Events.Subscribe<ArrangedEvent>(e => { if (e.Forced) arranged++; });
            App.Events.Publish(new RecenteredEvent());
            Assert.That(arranged, Is.EqualTo(1));
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(piano.eulerAngles.y, 45f)), Is.LessThan(0.05f), "faces the new heading");
            // A recentre lays the room out about the tracking origin the system set (never the head's pose of the moment).
            Transform space = App.Rig.TrackingSpace != null ? App.Rig.TrackingSpace : App.Rig.RigRoot;
            Vector3 seat = new Vector3(space.position.x, Head.position.y, space.position.z);
            Vector3 want = LayoutMath.SeatPlace(seat, 45f * Mathf.Deg2Rad, GoldenRigsProbe.HomeAll[0]);
            Assert.That(Vector3.Distance(piano.position, want), Is.LessThan(0.05f), "the keys ahead of the new seat");
            yield return null;
        }
    }
}
