// The ray summon (platform.md 4.4 c, 12.1 RaySummonTests) with scripted hands: the system pointer pose aimed at a stub on
// display shows its card after 0.25 s; a pinch held 0.35 s brings it (it glides in and arrives), the target lifting 2 cm
// while held; a release at 0.3 s does nothing and it settles back; the summon pinch is never a grab or a swivel; and the
// guards: the palm toward the headset, a hand that just played, a held rig, arrange mode, mixed reality, a hand in a zone.
// Then the desktop: a left click on a displayed stub's click target brings it. Allocation-free while hovering.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class RaySummonTests : LayoutPlayTestBase
    {
        const HandSide Side = HandSide.Right;
        const int HandIndex = 1;

        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(withStubs: true); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        /// <summary>Where the right hand rests: above every play zone, beside the head.</summary>
        Vector3 HandPoint => Head.position + new Vector3(0.25f, 0.2f, 0.1f);

        InstrumentView Stub(InstrumentKind k) => InstrumentView.Of(App, k);

        /// <summary>The hand open (or pinching) at HandPoint, its system pointer aimed at the instrument's pick point.</summary>
        void Aim(InstrumentKind k, bool pinch, float offDeg = 0f)
        {
            Assert.IsTrue(Layout.TryGetDisplayPoint(k, out Vector3 target), k + " is on display");
            Vector3 o = HandPoint;
            Quaternion look = Quaternion.AngleAxis(offDeg, Vector3.up) * Quaternion.LookRotation(target - o);
            Fake.SetPointer(Side, new Pose(o, look));
            Fake.SetPose(Side, pinch ? PinchAt(Side, o) : OpenAt(Side, o));
        }

        IEnumerator Hover(InstrumentKind k, float seconds)
        {
            int n = Mathf.CeilToInt(seconds / DT);
            for (int i = 0; i < n; i++) { Aim(k, false); yield return Frame(); }
        }

        [UnityTest]
        public IEnumerator AHeldPinchBringsTheDisplayedInstrument()
        {
            Assert.IsFalse(Layout.InPlayZone(HandPoint), "the hand rests outside every play zone");
            InstrumentKind k = InstrumentKind.Handpan;
            yield return Hover(k, 0.1f);
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.EqualTo(k), "the cone pick has it");
            Assert.IsTrue(Layout.Summon.RayVisible, "the ray shows while it points at a target");
            Assert.IsFalse(Layout.Summon.CardVisible, "no card before 0.25 s");
            yield return Hover(k, 0.2f);
            Assert.IsTrue(Layout.Summon.CardVisible, "the card after 0.25 s");
            Assert.That(Layout.Summon.CardText, Is.EqualTo(App.Registry.Get(k).Name));
            Layout.TryGetDisplayPoint(k, out Vector3 pick);
            Assert.That(Layout.Summon.CardPosition.y - pick.y, Is.EqualTo(RaySummon.CardAbove).Within(1e-4f), "12 cm above the pick point");
            Transform t = Stub(k).transform;
            Vector3 rest = t.position;
            int snaps = Layout.SnapTurns;
            for (int i = 0; i < 18; i++) { Aim(k, true); yield return Frame(); }   // 0.25 s held
            Assert.IsTrue(Layout.Summon.Holding);
            Assert.IsTrue(Layout.Summon.Owns(HandIndex), "the summon owns this pinch");
            Assert.That(t.position.y - rest.y, Is.EqualTo(RaySummon.PreviewLift).Within(2e-3f), "the target lifts 2 cm while held");
            Assert.IsNull(Layout.HeldRig, "a summon pinch is never a grab");
            for (int i = 0; i < 10 && Layout.Summon.Summons == 0; i++) { Aim(k, true); yield return Frame(); }   // past 0.35 s
            Assert.That(Layout.Summon.Summons, Is.EqualTo(1));
            Assert.IsTrue(Layout.IsInScene(k), "Bring(kind, Ray)");
            Assert.That(Layout.SnapTurns, Is.EqualTo(snaps), "and never a swivel");
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) { Fake.SetPose(Side, PinchAt(Side, HandPoint)); yield return Frame(); }
            Assert.That(Layout.PlaceOf(k), Is.EqualTo(InstrumentPlace.Seat), "it arrived");
            Assert.IsTrue(Stub(k).IsPresent);
            yield return Release(Side, HandPoint);
            Assert.IsFalse(Layout.Summon.Owns(HandIndex));
        }

        [UnityTest]
        public IEnumerator AReleaseBeforeTheHoldCancelsAndItSettlesBack()
        {
            InstrumentKind k = InstrumentKind.Handpan;
            yield return Hover(k, 0.3f);
            Transform t = Stub(k).transform;
            Vector3 rest = t.position;
            for (int i = 0; i < 21; i++) { Aim(k, true); yield return Frame(); }   // 0.29 s
            Assert.IsTrue(Layout.Summon.Holding);
            yield return Hover(k, DT);
            Assert.IsFalse(Layout.Summon.Holding, "released before 0.35 s: cancelled");
            Assert.That(Layout.Summon.Cancels, Is.EqualTo(1));
            Assert.That(Vector3.Distance(t.position, rest), Is.LessThan(1e-5f), "settled back");
            Assert.That(Layout.PlaceOf(k), Is.EqualTo(InstrumentPlace.Display), "nothing came");
            Assert.That(Layout.Summon.Summons, Is.Zero);
        }

        [UnityTest]
        public IEnumerator WhenTheSetIsFullTheMemberThatMakesRoomLiftsToo()
        {
            InstrumentKind k = InstrumentKind.Handpan;
            Assert.That(Layout.ReplaceCandidate(k), Is.EqualTo(InstrumentKind.Harp), "the harp would make room");
            Transform harp = Probe.Rigs[GoldenLayout.Harp].Group;
            Vector3 rest = harp.position;
            yield return Hover(k, 0.3f);
            for (int i = 0; i < 15; i++) { Aim(k, true); yield return Frame(); }
            Assert.That(Layout.Summon.PreviewReplace, Is.EqualTo(InstrumentKind.Harp));
            Assert.That(harp.position.y - rest.y, Is.EqualTo(RaySummon.PreviewLift).Within(2e-3f), "the harp lifts 2 cm as a preview");
            yield return Hover(k, DT);
            Assert.That(Vector3.Distance(harp.position, rest), Is.LessThan(1e-5f), "and settles back on a release");
        }

        IEnumerator AssertNoHover(InstrumentKind k, string why)
        {
            for (int i = 0; i < 30; i++) { Aim(k, false); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Null, why);
            Assert.IsFalse(Layout.Summon.CardVisible, why);
            for (int i = 0; i < 30; i++) { Aim(k, true); yield return Frame(); }
            Assert.IsFalse(Layout.Summon.Holding, why);
            Assert.That(Layout.PlaceOf(k), Is.EqualTo(InstrumentPlace.Display), why + ": nothing came");
            yield return Release(Side, HandPoint);
        }

        [UnityTest]
        public IEnumerator TheGuardsHoldIt()
        {
            InstrumentKind k = InstrumentKind.Handpan;
            // a hand that played less than a second ago
            Hand(Side).LastPlayMs = App.Clock.NowMs + 5000;
            yield return AssertNoHover(k, "within 1 s of playing");
            Hand(Side).LastPlayMs = double.NegativeInfinity;
            // the palm toward the headset (the system gesture), the hand in front of the face
            Assert.IsTrue(Layout.TryGetDisplayPoint(k, out Vector3 target), "on display");
            Vector3 o = Head.position + new Vector3(0.1f, -0.05f, 0.3f), toHead = (Head.position - o).normalized;
            Assert.IsFalse(Layout.InPlayZone(o), "in front of the face, outside every play zone");
            HandPose facing = HandPose.OpenFacing(Side, o, Vector3.ProjectOnPlane(Vector3.up, toHead).normalized, toHead).WithIndexTipAt(o);
            for (int i = 0; i < 30; i++)
            {
                Fake.SetPointer(Side, new Pose(o, Quaternion.LookRotation(target - o)));
                Fake.SetPose(Side, i < 20 ? facing : facing.WithPinch(0f));
                yield return Frame();
            }
            Assert.IsTrue(App.Input.SystemGestureLike(Hand(Side), Head.position, Head.forward), "the palm faces the headset");
            Assert.IsFalse(Layout.Summon.Holding, "palm to the face");
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Null);
            yield return Release(Side, HandPoint);
            o = HandPoint;
            // arrange mode has its own ray grab
            Layout.BeginArrange();
            for (int i = 0; i < 30; i++) { Aim(k, false); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Null, "not in arrange mode");
            Layout.EndArrange();
            // a held rig
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Harp));
            Assert.That(Layout.HeldRig, Is.Not.Null, "the right hand holds the harp");
            for (int i = 0; i < 30; i++) { Aim(k, false); Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Handle(GoldenLayout.Harp))); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Null, "not while a rig is held");
            yield return ReleaseHands(Head.position + Vector3.forward * 0.3f);
            // mixed reality: nothing is on display
            Probe.Mr.Active = true;
            Layout.ArrangeAroundPlayer(true);
            Assert.IsFalse(Layout.TryGetDisplayPoint(k, out _), "hidden in mixed reality");
            for (int i = 0; i < 30; i++) { Fake.SetPointer(Side, new Pose(o, Quaternion.LookRotation(target - o))); Fake.SetPose(Side, OpenAt(Side, o)); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Null, "not in mixed reality");
            Probe.Mr.Active = false;
            Layout.ArrangeAroundPlayer(true);
            // a hand inside a zone (the piano's)
            Vector3 inZone = Probe.Rigs[GoldenLayout.Piano].Group.TransformPoint(new Vector3(0f, 0.05f, 0.05f));
            Assert.IsTrue(Layout.InPlayZone(inZone), "over the keys");
            for (int i = 0; i < 30; i++) { Fake.SetPointer(Side, new Pose(inZone, Quaternion.LookRotation(target - inZone))); Fake.SetPose(Side, OpenAt(Side, inZone)); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Null, "not with the hand in a zone");
            // and after all that it still works
            yield return Release(Side, HandPoint);
            yield return Hover(k, 0.3f);
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.EqualTo(k));
        }

        [UnityTest]
        public IEnumerator TheConePickFindsTheBowlsBehindTheHandDrumsAndAllowsThreeDegrees()
        {
            // a first-hit ray at the bowls' pick point from the seat would stop on the hand drums under the window
            for (int i = 0; i < 30; i++) { Aim(InstrumentKind.Bowls, false); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.EqualTo(InstrumentKind.Bowls), "the cone pick finds the bowls on the sill");
            InstrumentKind k = InstrumentKind.Handpan;
            for (int i = 0; i < 30; i++) { Aim(k, false, 3f); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.EqualTo(k), "3 degrees off: inside the 6 degree cone");
            for (int i = 0; i < 30; i++) { Aim(k, false, 9f); yield return Frame(); }
            Assert.That(Layout.Summon.PickOf(HandIndex), Is.Not.EqualTo(k), "9 degrees off: not it");
        }

        [UnityTest]
        public IEnumerator HoveringAllocatesNothing()
        {
            yield return Hover(InstrumentKind.Handpan, 0.3f);
            Assert.IsTrue(Layout.Summon.CardVisible);
            for (int i = 0; i < 5; i++) Boot.Step(DT);   // the hand stays where Hover left it
            Assert.That(() => { for (int i = 0; i < 20; i++) Boot.Step(DT); },
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "a hover allocates nothing per frame");
            Assert.IsTrue(Layout.Summon.CardVisible);
        }
    }

    public class DesktopSummonTests : LayoutPlayTestBase
    {
        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(withStubs: true, presenting: false); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        [UnityTest]
        public IEnumerator ALeftClickOnADisplayedInstrumentBringsIt()
        {
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Marimba, out Vector3 at));
            Vector3 eye = Head.position;
            var ray = new Ray(eye, (at - eye).normalized);
            Layout.DesktopStep(new DesktopMouse { Ray = ray, Pressed = true, Held = true });
            Assert.That(Layout.DesktopSummons, Is.EqualTo(1));
            Assert.IsTrue(Layout.IsInScene(InstrumentKind.Marimba), "the marimba comes (alone: it fits beside none of the three)");
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Marimba }));
            Layout.DesktopStep(new DesktopMouse { Ray = ray, Held = false });
            // a click on empty air brings nothing
            Layout.DesktopStep(new DesktopMouse { Ray = new Ray(eye, Vector3.up), Pressed = true, Held = true });
            Assert.That(Layout.DesktopSummons, Is.EqualTo(1));
            yield return null;
        }
    }
}
