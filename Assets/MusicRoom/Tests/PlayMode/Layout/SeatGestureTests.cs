// The seat gestures (spec 08 section 5.4): the swivel snap turn and the two-hand summon, plus the
// phase they run in (FrameOrderTests pattern) and the allocation rule for the layout's per-frame work.
using System.Collections;
using System.Collections.Generic;
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
    public class SeatGestureTests : LayoutPlayTestBase
    {
        /// <summary>Records, per phase, whether a rig is held and whether a turn runs. Order 200: it ticks after the Layout
        /// module (100) in every phase, so it sees what the layout did in that phase.</summary>
        sealed class PhaseProbe : AppModule
        {
            public readonly List<string> Log = new List<string>(64);
            public bool Recording;
            public LayoutModule Layout;
            public override int Order => 200;
            public override void Tick(FramePhase phase, float dt)
            {
                if (!Recording || Layout == null) return;
                Log.Add(phase + (Layout.AnyHeld ? ":held" : "") + (Layout.IsTurning ? ":turning" : ""));
            }
        }

        PhaseProbe _phases;
        static float[] s_keep;

        [UnitySetUp] public IEnumerator SetUp() { _phases = new PhaseProbe(); yield return BootApp(extra: _phases); _phases.Layout = Layout; }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        /// <summary>A point in empty air in front of the head, above every instrument's zone.</summary>
        Vector3 Air(float x) => Head.position + new Vector3(x, 0.3f, 0.25f);

        [UnityTest]
        public IEnumerator APinchInTheAirPulledSidewaysSnapsTheSeat45Degrees()
        {
            Vector3 p = Air(0f);
            Assert.IsFalse(Layout.NearInstrument(p), "empty air");
            Vector3 head0 = Head.position;
            float yaw0 = Head.eulerAngles.y;
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, p));
            yield return Frame();
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, p));
            yield return Frame();
            Assert.That(Layout.SwivelHand, Is.SameAs(Hand(HandSide.Right)), "the pinch took the swivel");
            Vector3 r = Head.right;
            for (int i = 1; i <= 10 && Layout.SnapTurns == 0; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, p + r * (0.016f * i)));
                yield return Frame();
            }
            Assert.That(Layout.SnapTurns, Is.EqualTo(1), "past 14 cm: one snap");
            Assert.That(Mathf.DeltaAngle(yaw0, Head.eulerAngles.y), Is.EqualTo(-45f).Within(0.01f), "pulling right turns the view left (like dragging the world)");
            Assert.That(Vector3.Distance(Head.position, head0), Is.LessThan(1e-4f), "about the head");
            Assert.IsFalse(Hand(HandSide.Right).Pointers[0].WasActive, "the hands' motion was reset");
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, p));
            yield return Frame();
            Assert.IsNull(Layout.SwivelHand, "it ends on PinchEnd");
        }

        [UnityTest]
        public IEnumerator NoSwivelNearAnInstrumentOrWhilePlaying()
        {
            Vector3 nearPiano = Probe.Rigs[GoldenLayout.Piano].Group.TransformPoint(new Vector3(0.2f, 0.25f, -0.1f));
            Assert.IsTrue(Layout.NearInstrument(nearPiano));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, nearPiano));
            yield return Frame();
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, nearPiano));
            yield return Frame();
            Assert.IsNull(Layout.SwivelHand, "a pinch near an instrument never swivels");
            yield return Release(HandSide.Right, nearPiano);
            Hand(HandSide.Right).LastPlayMs = App.Clock.NowMs - 500;
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Air(0f)));
            yield return Frame();
            Assert.IsNull(Layout.SwivelHand, "nor a hand that played in the last second");
        }

        [UnityTest]
        public IEnumerator BothHandsPinchedInTheAirForAMomentSummonEverything()
        {
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            yield return Frame();
            // Turn the seat first (the rig rotates; the instruments stay behind), then summon them back.
            SeatAt(Head.position, 90f, arrange: false);
            l = Air(-0.15f); r = Air(0.15f);
            int arranged = 0;
            App.Events.Subscribe<ArrangedEvent>(e => { if (e.Forced) arranged++; });
            Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, r));
            int frames = 0;
            while (Layout.Summons == 0 && frames < 80) { yield return Frame(); frames++; }
            Assert.That(Layout.Summons, Is.EqualTo(1));
            Assert.That(arranged, Is.EqualTo(1));
            Assert.That(frames * DT, Is.EqualTo(GrabConstants.TwoHandSummonSeconds).Within(2f * DT), "after the hold (1 s)");
            Assert.IsNull(Layout.SwivelHand, "the summon cancels the swivel");
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Assert.That(Mathf.DeltaAngle(piano.eulerAngles.y, 90f), Is.EqualTo(0f).Within(0.05f), "laid out along the new heading");
            yield return Frames(80);
            Assert.That(Layout.Summons, Is.EqualTo(1), "once per hold");
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            yield return Frame();
            Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, l));
            for (int i = 0; i < 80; i++) yield return Frame();   // the 1 s hold again
            Assert.That(Layout.Summons, Is.EqualTo(2), "it re-arms when fewer than two hands count");
        }

        [UnityTest]
        public IEnumerator NoSummonWithAHandOnAnInstrument()
        {
            Vector3 nearPiano = Probe.Rigs[GoldenLayout.Piano].Group.TransformPoint(new Vector3(0.2f, 0.25f, -0.1f));
            Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, Air(-0.15f)));
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, nearPiano));
            yield return Frames(80);
            Assert.That(Layout.Summons, Is.Zero);
        }

        [UnityTest]
        public IEnumerator TheLayoutActsInItsOwnPhases()
        {
            // The grab takes hold in the Grab phase (after the instruments ticked the frame) and a turn animates in
            // LayoutAnimation, after the sweep fired in PageTurn (ARCHITECTURE.md section 4 steps 11 and 17).
            Vector3 at = Handle(GoldenLayout.Table);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.04f));
            yield return Frame();
            int frames = 0;
            while (!Layout.AnyHeld && frames < 40)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
                _phases.Log.Clear();
                _phases.Recording = true;
                yield return Frame();
                _phases.Recording = false;
                frames++;
            }
            Assert.IsTrue(Layout.AnyHeld);
            int grab = _phases.Log.IndexOf(nameof(FramePhase.Grab) + ":held");
            Assert.That(grab, Is.GreaterThan(0), string.Join(",", _phases.Log));
            Assert.That(_phases.Log[grab - 1], Does.Not.Contain(":held"), "held from the Grab phase on");
            Assert.That(_phases.Log.IndexOf(nameof(FramePhase.Instruments)), Is.LessThan(grab));
            yield return Release(HandSide.Right, at);
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            _phases.Log.Clear();
            _phases.Recording = true;
            Layout.Next(1);
            yield return Frame();
            _phases.Recording = false;
            Assert.That(_phases.Log[0], Does.Contain(":turning"), "the turn started outside the frame (Next)");
            Assert.That(_phases.Log, Has.Member(nameof(FramePhase.LayoutAnimation) + ":turning"));
        }

        [UnityTest]
        public IEnumerator PerFrameLayoutWorkAllocatesNothing()
        {
            // The hot paths of ARCHITECTURE.md section 17: grab guards with a dwell running, a held rig dragged and
            // constrained, the sweep reading two palms, the swivel and summon checks and a page-turn step.
            Vector3 at = Handle(GoldenLayout.Drums);
            yield return PinchHold(HandSide.Right, at, 25, 0f);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, Air(-0.2f)));
            for (int i = 1; i <= 10; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + Vector3.left * (0.02f * i)));
                yield return Frame();
            }
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");
            Assert.That(() =>
            {
                for (int k = 0; k < 20; k++)
                {
                    Layout.Tick(FramePhase.FirstArrange, DT);
                    Layout.Tick(FramePhase.Grab, DT);
                    Layout.Tick(FramePhase.Swivel, DT);
                    Layout.Tick(FramePhase.TwoHandSummon, DT);
                    Layout.Tick(FramePhase.PageTurn, DT);
                    Layout.Tick(FramePhase.LayoutAnimation, DT);
                }
            }, Is.Not.AllocatingGCMemory(), "a dragged, constrained rig");
            yield return ReleaseHands(at);
            // A pinch dwelling on a handle (guards, dwell samples, hint ring and cage).
            Vector3 h = Handle(GoldenLayout.Harp);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, h + Vector3.up * 0.04f));
            yield return Frame();
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, h));
            yield return Frames(6);
            Assert.That(Layout.Visuals.HintVisible, Is.True);
            Assert.That(() =>
            {
                for (int k = 0; k < 20; k++) { Layout.Tick(FramePhase.Grab, DT); Layout.Tick(FramePhase.PageTurn, DT); Layout.Tick(FramePhase.Swivel, DT); }
            }, Is.Not.AllocatingGCMemory(), "a dwell running");
            yield return ReleaseHands(h);
            // A page turn in progress.
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Layout.Next(1);
            Assert.That(() => { Layout.Tick(FramePhase.LayoutAnimation, DT / 8f); }, Is.Not.AllocatingGCMemory(), "a page-turn step");
            Assert.IsTrue(Layout.IsTurning);
        }
    }
}
