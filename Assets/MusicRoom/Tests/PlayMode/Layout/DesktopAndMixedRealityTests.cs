// The parts of ARCHITECTURE.md 15.5 that came with the revised contracts, against the golden rigs: the desktop Shift /
// Alt rig drag, the sticks-first step of the Grab pass (the .02 handle exception), the mixed-reality placing pinch,
// Layout's PlaceAroundPiano / RestoreHomes / anchor, arrangeAroundPlayer's movePiano and force, and GrabTarget allocating nothing.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension on Is.Not
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class DesktopAndMixedRealityTests : LayoutPlayTestBase
    {
        static readonly Vector3 OriginSeat = new Vector3(0f, 1.2f, 0f);
        static object s_keep;

        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); SeatAt(OriginSeat, 0f); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        static GoldenLayout.Scenario Find(string label)
        {
            foreach (var s in GoldenLayout.Scenarios) if (s.Label == label) return s;
            throw new System.ArgumentException(label);
        }

        DesktopMouse Mouse(Vector3 at, bool pressed, bool held, bool shift = false, bool alt = false, float x = 0f) =>
            new DesktopMouse { Ray = new Ray(Head.position, (at - Head.position).normalized), Pressed = pressed, Held = held, Shift = shift, Alt = alt, X = x };

        // ---------------------------------------------------------------- desktop

        [UnityTest]
        public IEnumerator ShiftDragSlidesTheRigUnderTheMouseAcrossItsDragPlane()
        {
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Vector3 at = Handle(GoldenLayout.Piano);
            Vector3 p0 = piano.position;
            Quaternion r0 = piano.rotation;
            // A plain press is the desktop camera's (look around): no rig moves, the camera is not blocked.
            Layout.DesktopStep(Mouse(at, true, true));
            Assert.IsNull(Layout.HeldRig, "no modifier: not a rig drag");
            Assert.IsFalse(App.Rig.Desktop.dragBlocked(), "the camera may look around");
            Layout.DesktopStep(Mouse(at, false, false));
            // Shift + press on the piano's case takes it at the hit height.
            Layout.DesktopStep(Mouse(at, true, true, shift: true, x: 100f));
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Piano]), "the rig the ray hits");
            Assert.IsTrue(Layout.MouseHolding);
            Assert.IsFalse(Layout.MouseTurning);
            Assert.IsTrue(Probe.Rigs[GoldenLayout.Piano].Held);
            Assert.IsTrue(Layout.Visuals.RingVisible, "the floor ring shows for a mouse grab too");
            Assert.IsTrue(App.Rig.Desktop.dragBlocked(), "the camera does not look around during the drag");
            // Where the press met the drag plane, then 20 cm further away from the player on that plane.
            Ray ray0 = Mouse(at, true, true).Ray;
            Layout.PickRig(ray0, out Vector3 hit);
            Vector3 away = new Vector3(0f, 0f, 0.2f);
            int ticks = Probe.Instruments[GoldenLayout.Piano].TickCount;
            for (int i = 1; i <= 10; i++)
            {
                Layout.DesktopStep(Mouse(hit + away * (i / 10f), false, true, shift: true, x: 100f));
                yield return Frame();
            }
            Assert.That(Probe.Instruments[GoldenLayout.Piano].TickCount, Is.EqualTo(ticks), "a held rig does not play");
            Vector3 moved = piano.position - p0;
            Assert.That(moved.x, Is.EqualTo(0f).Within(2e-3f));
            Assert.That(moved.z, Is.EqualTo(0.2f).Within(2e-3f), "it follows the ray on the horizontal plane");
            Assert.That(moved.y, Is.EqualTo(0f).Within(1e-5f), "a mouse drag never lifts");
            Assert.That(Quaternion.Angle(piano.rotation, r0), Is.LessThan(0.01f), "and never turns");
            // Releasing the button lets go.
            Layout.DesktopStep(Mouse(hit + away, false, false));
            Assert.IsNull(Layout.HeldRig);
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Piano].Held);
            Assert.IsFalse(Layout.MouseHolding);
            Assert.IsFalse(Layout.Visuals.RingVisible);
            Assert.IsFalse(App.Rig.Desktop.dragBlocked());
        }

        [UnityTest]
        public IEnumerator AltDragTurnsTheRigInPlaceByTheReferenceRate()
        {
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Vector3 c0 = Centre(GoldenLayout.Piano);
            float yaw0 = piano.eulerAngles.y;
            Vector3 at = Handle(GoldenLayout.Piano);
            Layout.DesktopStep(Mouse(at, true, true, alt: true, x: 200f));
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Piano]));
            Assert.IsTrue(Layout.MouseTurning);
            // 25 px to the right: 25 x 0.012 rad added to the source frame's yaw (anticlockwise from above), Unity yaw -17.19 degrees.
            for (int i = 1; i <= 5; i++)
            {
                Layout.DesktopStep(Mouse(at, false, true, alt: true, x: 200f + 5f * i));
                yield return Frame();
            }
            float want = -25f * LayoutModule.MouseTurnPerPixel * Mathf.Rad2Deg;
            Assert.That(Mathf.DeltaAngle(yaw0, piano.eulerAngles.y), Is.EqualTo(want).Within(0.05f), "Unity yaw = yaw0 - dx 0.012 rad");
            Assert.That(Vector3.Distance(Centre(GoldenLayout.Piano), c0), Is.LessThan(2e-3f), "about the footprint's centre");
            Assert.That(piano.position.y, Is.EqualTo(GoldenRigsProbe.HomeAll[0].y).Within(1e-5f));
            Layout.DesktopStep(Mouse(at, false, false));
            Assert.IsNull(Layout.HeldRig);
        }

        [UnityTest]
        public IEnumerator TheNearestRigOnTheRayIsPicked()
        {
            // A ray from in front of the kit through it toward the piano: the kit is hit first.
            Vector3 kit = Centre(GoldenLayout.Drums) + Vector3.up * 0.5f;
            Vector3 from = kit + new Vector3(0.8f, 0.3f, 0f);
            var ray = new Ray(from, (kit - from).normalized);
            Assert.That(Layout.PickRig(ray, out Vector3 hit), Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            Assert.That(Vector3.Distance(hit, from), Is.LessThan(Vector3.Distance(kit, from) + 0.3f));
            Assert.IsNull(Layout.PickRig(new Ray(from, Vector3.up), out _), "the ceiling is empty");
            yield return null;
        }

        // ---------------------------------------------------------------- sticks first

        [UnityTest]
        public IEnumerator APinchThatJustMissesAStickNeverGrabsTheKit()
        {
            HandState right = Hand(HandSide.Right);
            Transform floor = Probe.Drums.Pieces[3].Transform;
            // 5 cm off the floor tom's shell: within the 7 cm handle reach but not the 2 cm of a pinch that missed a stick.
            Vector3 beside = floor.TransformPoint(new Vector3(0f, -0.2f, -(0.2f + 0.05f)));
            Vector3 on = Handle(GoldenLayout.Drums);
            Assert.That(Layout.GrabTarget(beside), Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            Assert.IsNull(Layout.GrabTarget(beside, LayoutModule.StickMissReach));
            Probe.Drums.PickAnswer = StickPick.Near;
            int calls0 = Probe.Drums.PickCalls;
            // Near a stick, off the handle: the pinch is a missed pick-up, never a grab.
            yield return PinchHold(HandSide.Right, beside, 25, 0f);
            Assert.That(Probe.Drums.PickCalls - calls0, Is.EqualTo(1), "IDrumsApi.TryPickUp asked once, on the pinch start");
            Assert.IsNull(HeldAfter, "a near miss beside the kit does nothing");
            Assert.IsFalse(Layout.IsArmed(right.Index));
            yield return Release(HandSide.Right, beside);
            // Near a stick but right on a handle: the grab still takes the kit.
            yield return PinchHold(HandSide.Right, on, 25, 0f);
            Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "on the handle the near miss does not count");
            yield return Release(HandSide.Right, on);
            SeatAt(OriginSeat, 0f);
            // A stick picked up: never a grab, even on the handle.
            Probe.Drums.PickAnswer = StickPick.Grabbed;
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Drums), 25, 0f);
            Assert.IsNull(HeldAfter, "the pinch took a stick");
            yield return Release(HandSide.Right, on);
            // No stick there: the same pinch beside the kit grabs it.
            Probe.Drums.PickAnswer = StickPick.None;
            yield return PinchHold(HandSide.Right, beside, 25, 0f);
            Assert.That(HeldAfter, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            yield return Release(HandSide.Right, beside);
        }

        // ---------------------------------------------------------------- mixed reality

        // 2026-11-10: a still pinch in empty air in mixed reality used to re-place the piano (every other rig hid and a reticle
        // followed the hand, else the head). Retired: instruments move only with Move instruments, by hand.
        [UnityTest]
        public IEnumerator AnEmptyAirPinchInMixedRealityMovesNothing()
        {
            Probe.Mr.Active = true;
            Layout.ArrangeAroundPlayer(true);
            Probe.Menu.ShowAt(Head.position + new Vector3(0.3f, -0.2f, 0.5f));
            int before = Layout.EmptyAirPinches;
            var pos = new Vector3[4];
            for (int r = 0; r < 4; r++) pos[r] = Probe.Rigs[r].Group.position;
            Vector3 air = Head.position + new Vector3(0f, 0.3f, 0.2f);
            Assert.IsFalse(Layout.NearInstrument(air));
            yield return PinchHold(HandSide.Right, air, AirFrames);
            Assert.That(Layout.EmptyAirPinches, Is.EqualTo(before + 1), "the dwell completed in empty air");
            Assert.That(Probe.Mr.StartPlacingCount, Is.Zero, "and started no placing");
            Assert.IsFalse(Probe.Mr.IsPlacing);
            for (int r = 0; r < 4; r++)
            {
                Assert.IsTrue(Probe.Rigs[r].Group.gameObject.activeSelf, GoldenLayout.RigNames[r] + " stays shown");
                Assert.That(Vector3.Distance(Probe.Rigs[r].Group.position, pos[r]), Is.LessThan(1e-4f), GoldenLayout.RigNames[r] + " stays put");
            }
            Assert.IsTrue(Probe.Menu.Shown && Probe.Menu.Root.gameObject.activeSelf, "the menu is untouched");
            yield return Release(HandSide.Right, air);
            Probe.Mr.Active = false;
        }

        // Placing that ends without a landing (MR's cancel paths, the tools' placer) brings back every rig it hid.
        [UnityTest]
        public IEnumerator ACancelledPlacingBringsTheRigsBack()
        {
            Probe.Mr.Active = true;
            Layout.ArrangeAroundPlayer(true);
            for (int r = 1; r < 4; r++) Probe.Rigs[r].Group.gameObject.SetActive(false);
            Probe.Mr.IsPlacing = false;
            ((ILayoutService)Layout).RestoreAfterPlacing();
            for (int r = 1; r < 4; r++) Assert.IsTrue(Probe.Rigs[r].Group.gameObject.activeSelf, GoldenLayout.RigNames[r] + " back");
            Probe.Mr.Active = false;
            yield return null;
        }

        // Placing that starts while a hand holds a rig ends that grab (the rig would stay silent under the hand).
        [UnityTest]
        public IEnumerator PlacingEndsAGrab()
        {
            Probe.Mr.Active = true;
            Layout.ArrangeAroundPlayer(true);
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Harp));
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Harp]));
            Probe.Mr.IsPlacing = true;
            yield return Frame();
            Assert.IsNull(Layout.HeldRig, "placing started: the grab ended");
            Probe.Mr.IsPlacing = false;
            Probe.Mr.Active = false;
            yield return Release(HandSide.Right, Handle(GoldenLayout.Harp));
        }

        [UnityTest]
        public IEnumerator PlaceAroundPianoKeepsThePianoAndLaysTheRestAtTheirOffsets()
        {
            Probe.Mr.Active = true;
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Vector3 pp = new Vector3(1.5f, 0.8f, -2f);
            const float yaw = 30f;
            piano.SetPositionAndRotation(pp, Quaternion.Euler(0f, yaw, 0f));
            Layout.PlaceAroundPiano();
            Assert.That(Vector3.Distance(piano.position, pp), Is.LessThan(1e-5f), "the piano stays where it was put");
            Assert.That(Mathf.DeltaAngle(piano.eulerAngles.y, yaw), Is.EqualTo(0f).Within(0.01f));
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            for (int r = 1; r < 4; r++)
            {
                Transform g = Probe.Rigs[r].Group;
                Vector3 d = GoldenRigsProbe.HomeAll[r] - GoldenRigsProbe.HomeAll[0];
                Vector3 want = pp + rot * new Vector3(d.x, 0f, d.z);
                want.y = GoldenRigsProbe.HomeAll[r].y;
                Assert.That(Mathf.DeltaAngle(g.eulerAngles.y, GoldenRigsProbe.HomeYawAll[r] + yaw), Is.EqualTo(0f).Within(0.05f), GoldenLayout.RigNames[r] + " turned with the piano");
                Assert.That(Vector3.Distance(g.position, want), Is.LessThan(0.35f), GoldenLayout.RigNames[r] + " near its VR-room offset (settled)");
            }
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero, "settled: nothing overlaps");
            Pose a = Layout.Anchor;
            Assert.That(Vector3.Distance(a.position, pp), Is.LessThan(1e-5f), "the anchor is the piano in the full room");
            Probe.Mr.Active = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestoreHomesPutsTheRoomBackAboutTheDefaultSeat()
        {
            Probe.Mr.Active = true;
            SeatAt(new Vector3(1f, 1.2f, -1.5f), 40f);
            Probe.Rigs[GoldenLayout.Piano].Group.SetPositionAndRotation(new Vector3(1.4f, 0.81f, -1f), Quaternion.Euler(0f, 55f, 0f));
            Probe.Mr.Active = false;
            Layout.RestoreHomes();
            var s = Find("all-origin");
            for (int r = 0; r < 4; r++)
            {
                Transform g = Probe.Rigs[r].Group;
                Assert.That(Vector3.Distance(g.position, s.After[r].Pos), Is.LessThan(2e-3f), $"{GoldenLayout.RigNames[r]} at {g.position.ToString("F4")}");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(g.eulerAngles.y, s.After[r].YawDeg)), Is.LessThan(0.05f));
            }
            Pose a = Layout.Anchor;
            Assert.That(Vector3.Distance(a.position, s.After[GoldenLayout.Piano].Pos), Is.LessThan(2e-3f));
            // Single mode: the anchor is the current instrument.
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.That(Vector3.Distance(Layout.Anchor.position, Probe.Rigs[GoldenLayout.Harp].Group.position), Is.LessThan(1e-5f));
            yield return null;
        }

        // ---------------------------------------------------------------- arrangeAroundPlayer(force, movePiano)

        [UnityTest]
        public IEnumerator MovePianoFalseKeepsThePianoAndForceIsReported()
        {
            int forced = 0, plain = 0;
            App.Events.Subscribe<ArrangedEvent>(e => { if (e.Forced) forced++; else plain++; });
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Vector3 moved = piano.position + new Vector3(0.3f, 0f, 0.2f);
            piano.position = moved;
            Layout.ArrangeAroundPlayer(false, false);
            Assert.That(Vector3.Distance(piano.position, moved), Is.LessThan(0.2f), "the piano stays (only settled)");
            Assert.That(plain, Is.EqualTo(1));
            Assert.That(forced, Is.Zero);
            Layout.ArrangeAroundPlayer(true, true);
            Assert.That(Vector3.Distance(piano.position, LayoutMath.SeatPlace(Head.position, 0f, GoldenRigsProbe.HomeAll[0])), Is.LessThan(2e-3f), "back at its home");
            Assert.That(forced, Is.EqualTo(1));
            yield return null;
        }

        // ---------------------------------------------------------------- hot path

        [UnityTest]
        public IEnumerator GrabTargetAllocatesNothing()
        {
            Vector3 a = Handle(GoldenLayout.Harp), b = Handle(GoldenLayout.Drums), c = Head.position + Vector3.up;
            Probe.Menu.ShowAt(Head.position + new Vector3(0f, -0.2f, 0.5f));
            Vector3 m = Probe.Menu.Root.position;
            Layout.GrabTarget(a); Layout.GrabTarget(m);   // warm up
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");
            Assert.That(() =>
            {
                for (int k = 0; k < 50; k++)
                {
                    Layout.GrabTarget(a);
                    Layout.GrabTarget(b, LayoutModule.StickMissReach);
                    Layout.GrabTarget(c);
                    Layout.GrabTarget(m);
                    Layout.GrabCandidate(a);
                    Layout.NearInstrument(c);
                }
            }, Is.Not.AllocatingGCMemory(), "GrabTarget / GrabCandidate / NearInstrument");
            yield return null;
        }
    }
}
