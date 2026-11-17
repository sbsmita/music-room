// "Turning the head should not move instruments" (2026-11-10 headset test in "Play in my room": "instruments are changing
// with my neck position. it should be changed with hands"). With the Layout module and the reference rigs:
//   - the head alone (fast turns left and right, looking up and down, leaning in every direction) with both hands open and
//     upright in front of it, the old page-turn sweep's pose, moves, turns and switches nothing: Move instruments off or
//     on, My set or One alone (the sweep was measured in the head's frame, so a turning head swept a still hand);
//   - the system's recentre events come in bursts: each lays the room out about the tracking origin the system set, so the
//     head turning between them moves nothing;
//   - Reset view by hand: both hands pinched and held still in empty air for 1 s bring everything back in front of the
//     player, with a ring that fills round the hands; it works with Move instruments on as well, a hand that wanders
//     starts the hold again, and a hand on an instrument (or pointing at one while moving them) never counts;
//   - the menu's Reset view and the hold are one path (the same placement from the same pose);
//   - the head moving and the hold allocate nothing per frame.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class HeadMotionTests : LayoutPlayTestBase
    {
        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        readonly Vector3[] _pos = new Vector3[4];
        readonly Quaternion[] _rot = new Quaternion[4];

        void Snap()
        {
            for (int i = 0; i < 4; i++) { _pos[i] = Probe.Rigs[i].Group.position; _rot[i] = Probe.Rigs[i].Group.rotation; }
        }

        void AssertUnmoved(string why)
        {
            for (int i = 0; i < 4; i++)
            {
                Transform g = Probe.Rigs[i].Group;
                Assert.That(Vector3.Distance(g.position, _pos[i]), Is.LessThan(1e-4f), $"{GoldenLayout.RigNames[i]} moved: {why}");
                Assert.That(Quaternion.Angle(g.rotation, _rot[i]), Is.LessThan(0.01f), $"{GoldenLayout.RigNames[i]} turned: {why}");
            }
        }

        /// <summary>Both hands open, fingers up, palms facing out sideways (upright: the page-turn sweep's hand), still in the
        /// world in front of where the head starts.</summary>
        void OpenPalmsStill(Vector3 head, Quaternion facing)
        {
            Vector3 right = facing * Vector3.right, fwd = facing * Vector3.forward;
            Fake.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, head + right * 0.25f + fwd * 0.3f + Vector3.down * 0.2f, Vector3.up, right));
            Fake.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, head - right * 0.25f + fwd * 0.3f + Vector3.down * 0.2f, Vector3.up, -right));
            Hand(HandSide.Right).LastPlayMs = -1e9;
            Hand(HandSide.Left).LastPlayMs = -1e9;
        }

        /// <summary>The head alone for <paramref name="seconds"/>: turns of +-80 degrees at up to ~420 degrees/s, looking up and
        /// down 30 degrees, leaning 25 cm sideways and 20 cm forward and back. The rig (the player's seat) never moves.</summary>
        IEnumerator HeadOnly(float seconds, Vector3 head0, float yaw0)
        {
            int n = Mathf.RoundToInt(seconds / DT);
            Quaternion facing = Quaternion.Euler(0f, yaw0, 0f);
            for (int i = 0; i < n; i++)
            {
                float t = i * DT;
                float yaw = 80f * Mathf.Sin(2f * Mathf.PI * t / 1.2f), pitch = 30f * Mathf.Sin(2f * Mathf.PI * t / 1.7f);
                Vector3 lean = facing * new Vector3(0.25f * Mathf.Sin(2f * Mathf.PI * t / 2.1f), 0.05f * Mathf.Sin(2f * Mathf.PI * t / 0.9f), 0.2f * Mathf.Sin(2f * Mathf.PI * t / 1.3f));
                Head.SetPositionAndRotation(head0 + lean, Quaternion.Euler(pitch, yaw0 + yaw, 0f));
                yield return Frame();
            }
            Head.SetPositionAndRotation(head0, facing);
        }

        /// <summary>The head held at a pose for some frames (the desktop camera script turns it back between frames), and left
        /// there for whatever the caller does next.</summary>
        IEnumerator HoldHead(Vector3 pos, Quaternion rot, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Head.SetPositionAndRotation(pos, rot);
                yield return Frame();
            }
            Head.SetPositionAndRotation(pos, rot);
        }

        IEnumerator HeadOnlyWithOpenHands(string why)
        {
            Vector3 head0 = Head.position;
            float yaw0 = Head.eulerAngles.y;
            Quaternion facing = Quaternion.Euler(0f, yaw0, 0f);
            OpenPalmsStill(head0, facing);
            yield return Frames(3);
            // The hands are what the sweep looks for: open and upright.
            Assert.IsTrue(App.Input.TryPalm(Hand(HandSide.Right), out _, out Vector3 n), "the palm is measured");
            Assert.That(Mathf.Abs(n.y), Is.LessThan(PageTurnGesture.PalmVertical), "an upright palm (the sweep's)");
            Assert.That(App.Input.GraspAmount(Hand(HandSide.Right)), Is.GreaterThan(PageTurnGesture.OpenHand), "an open hand");
            Snap();
            InstrumentKind current = Layout.Current;
            int turns = Layout.Turns, arranged = Layout.XrArrangements, backs = Layout.BringBacks, sweeps = Layout.Sweep.Count;
            bool arranging = Layout.Arranging;
            yield return HeadOnly(4f, head0, yaw0);
            yield return Frames(3);
            AssertUnmoved(why);
            Assert.That(Layout.Current, Is.EqualTo(current), "no instrument switched: " + why);
            Assert.That(Layout.Turns, Is.EqualTo(turns), "no page turn: " + why);
            Assert.That(Layout.Sweep.Count, Is.EqualTo(sweeps), "no sweep: " + why);
            Assert.That(Layout.BringBacks, Is.EqualTo(backs), "no reset view: " + why);
            Assert.That(Layout.XrArrangements, Is.EqualTo(arranged));
            Assert.That(Layout.Arranging, Is.EqualTo(arranging), "Move instruments unchanged: " + why);
        }

        [UnityTest]
        public IEnumerator TurningTheHeadMovesTurnsAndSwitchesNothing()
        {
            yield return HeadOnlyWithOpenHands("My set, Move instruments off");
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            yield return Frames(40);
            Assert.That(Layout.Mode, Is.EqualTo(LayoutMode.Single));
            yield return HeadOnlyWithOpenHands("One alone, Move instruments off");
            Layout.BeginArrange();
            yield return Frame();
            Assert.IsTrue(Layout.Arranging);
            yield return HeadOnlyWithOpenHands("One alone, Move instruments on");
            Layout.EndArrange();
            Layout.Set(LayoutMode.All, InstrumentKind.Piano);
            yield return Frames(40);
            Layout.BeginArrange();
            yield return HeadOnlyWithOpenHands("My set, Move instruments on");
            Layout.EndArrange();
        }

        [UnityTest]
        public IEnumerator ABurstOfRecentresLandsInOnePlaceWhateverTheHeadDoes()
        {
            // The system put the player's front 30 degrees to the right: the tracking space turned.
            Transform rig = App.Rig.RigRoot;
            rig.rotation = Quaternion.Euler(0f, 30f, 0f);
            yield return Frame();
            int backs = Layout.BringBacks;
            App.Events.Publish(new RecenteredEvent());
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 1));
            Assert.That(Layout.LastBringBack, Is.EqualTo(BringBackSource.Recentre));
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(piano.eulerAngles.y, 30f)), Is.LessThan(0.05f), "faces the system's new front");
            Snap();
            // The head looks 70 degrees away and leans between the events of the same burst: nothing moves.
            Vector3 head0 = Head.position;
            yield return HoldHead(head0 + new Vector3(0.2f, -0.05f, 0.15f), Quaternion.Euler(20f, 100f, 0f), 30);
            App.Events.Publish(new RecenteredEvent());
            AssertUnmoved("the second recentre of a burst with the head turned away");
            yield return HoldHead(head0 + new Vector3(-0.25f, 0f, -0.1f), Quaternion.Euler(-15f, -60f, 0f), 30);
            App.Events.Publish(new RecenteredEvent());
            AssertUnmoved("the third");
            Head.position = head0;
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 3));
        }

        [UnityTest]
        public IEnumerator ARecentreEventWithoutANewOriginUnderThePlayerMovesNothing()
        {
            // The player walked 1.2 m away from the tracking origin; a recentre event comes (the system's count moved) but the
            // origin is not under them: not a reset, nothing moves.
            yield return Frame();
            Snap();
            int backs = Layout.BringBacks;
            Vector3 head0 = Head.position;
            Head.position = head0 + new Vector3(1.2f, 0f, 0.3f);
            App.Events.Publish(new RecenteredEvent());
            AssertUnmoved("a recentre event far from the origin");
            Assert.That(Layout.BringBacks, Is.EqualTo(backs));
            Head.position = head0;
        }

        // ---------------------------------------------------------------- Reset view by hand

        /// <summary>A point in empty air in front of the head, above every instrument's zone.</summary>
        Vector3 Air(float x) => Head.position + App.Rig.RigRoot.rotation * new Vector3(x, 0.3f, 0.25f);

        static HandPose PinchUp(HandSide side, Vector3 p) => PinchAt(side, p);

        /// <summary>Both hands' rays point at the ceiling (nothing to take in Move instruments).</summary>
        void RaysUp()
        {
            Fake.SetPointer(HandSide.Left, new Pose(Head.position + new Vector3(-0.2f, -0.3f, 0.1f), Quaternion.LookRotation(Vector3.up, Vector3.back)));
            Fake.SetPointer(HandSide.Right, new Pose(Head.position + new Vector3(0.2f, -0.3f, 0.1f), Quaternion.LookRotation(Vector3.up, Vector3.back)));
        }

        IEnumerator HoldBothUntilBack(Vector3 l, Vector3 r, int maxFrames, System.Action<int> each = null)
        {
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            yield return Frame();
            int backs = Layout.BringBacks;
            Fake.SetPose(HandSide.Left, PinchUp(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, PinchUp(HandSide.Right, r));
            for (int f = 1; f <= maxFrames && Layout.BringBacks == backs; f++)
            {
                yield return Frame();
                each?.Invoke(f);
            }
        }

        IEnumerator OpenHands(Vector3 l, Vector3 r)
        {
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator BothHandsHeldStillBringEverythingBackWithAFillingRing([Values(false, true)] bool moveOn)
        {
            if (moveOn) { Layout.BeginArrange(); Assert.IsTrue(Layout.Arranging); }
            RaysUp();
            // The player turned to face +90 (the seat turned; the instruments stayed behind).
            SeatAt(Head.position, 90f, arrange: false);
            RaysUp();
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            int arranged = 0;
            App.Events.Subscribe<ArrangedEvent>(e => { if (e.Forced) arranged++; });
            int backs = Layout.BringBacks, frames = 0;
            bool ringHalf = false, ringEarly = false;
            yield return HoldBothUntilBack(l, r, 100, f =>
            {
                frames = f;
                if (f == 5) ringEarly = Layout.Visuals.BringRingVisible;
                if (f == 36)
                {
                    ringHalf = Layout.Visuals.BringRingVisible && Mathf.Abs(Layout.Visuals.BringRingK - 0.5f) < 0.03f;
                    Assert.That(Vector3.Distance(Layout.Visuals.BringRingCentre, (l + r) * 0.5f), Is.LessThan(0.02f), "the ring round the hands");
                    Assert.That(Layout.Visuals.BringRingRadius, Is.GreaterThan(0.15f));
                }
            });
            Assert.IsFalse(ringEarly, "no ring for a passing two-hand pinch (under BringBackShowAfter)");
            Assert.IsTrue(ringHalf, "half way: the ring half full");
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 1));
            Assert.That(Layout.LastBringBack, Is.EqualTo(BringBackSource.Gesture));
            Assert.That(arranged, Is.EqualTo(1));
            Assert.That(frames * DT, Is.EqualTo(GrabConstants.TwoHandSummonSeconds).Within(2f * DT), "after 1 s held still");
            Assert.IsFalse(Layout.Visuals.BringRingVisible, "the ring goes when it fires");
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Assert.That(Mathf.DeltaAngle(piano.eulerAngles.y, 90f), Is.EqualTo(0f).Within(0.05f), "in front of the new heading");
            Assert.That(Layout.Arranging, Is.EqualTo(moveOn), "Move instruments stays as it was");
            Assert.That(Layout.HeldRig, Is.Null, "nothing picked up by the hold");
            yield return Frames(80);
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 1), "once per hold");
            yield return OpenHands(l, r);
            if (moveOn) Layout.EndArrange();
        }

        [UnityTest]
        public IEnumerator AWanderingHandStartsTheHoldAgain()
        {
            RaysUp();
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            int backs = Layout.BringBacks;
            Fake.SetPose(HandSide.Left, PinchUp(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, PinchUp(HandSide.Right, r));
            yield return Frames(50);   // 0.7 s still
            Assert.That(Layout.SummonHeld, Is.GreaterThan(0.6f));
            Vector3 r2 = r + Vector3.right * 0.1f;   // 10 cm away: past BringBackStill
            Fake.SetPose(HandSide.Right, PinchUp(HandSide.Right, r2));
            yield return Frames(50);   // 0.7 s still again
            Assert.That(Layout.BringBacks, Is.EqualTo(backs), "the hold started again where the hand moved to");
            yield return Frames(30);
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 1), "1 s after it came to rest");
            yield return OpenHands(l, r2);
        }

        [UnityTest]
        public IEnumerator AHandOnAnInstrumentOrPointingAtOneNeverBringsBack()
        {
            Vector3 nearPiano = Probe.Rigs[GoldenLayout.Piano].Group.TransformPoint(new Vector3(0.2f, 0.25f, -0.1f));
            int backs = Layout.BringBacks;
            RaysUp();
            Fake.SetPose(HandSide.Left, PinchUp(HandSide.Left, Air(-0.15f)));
            Fake.SetPose(HandSide.Right, PinchUp(HandSide.Right, nearPiano));
            yield return Frames(100);
            Assert.That(Layout.BringBacks, Is.EqualTo(backs), "a hand on the piano");
            Assert.IsFalse(Layout.Visuals.BringRingVisible);
            yield return OpenHands(Air(-0.15f), nearPiano);
            // Move instruments: a hand whose ray is on an instrument picks it up, never a reset view.
            Layout.BeginArrange();
            yield return Frame();
            Vector3 drums = Probe.Rigs[GoldenLayout.Drums].Group.position + Vector3.up * 0.5f;
            Vector3 from = Air(0.15f);
            Fake.SetPointer(HandSide.Right, new Pose(from, Quaternion.LookRotation(drums - from)));
            Fake.SetPointer(HandSide.Left, new Pose(Air(-0.15f), Quaternion.LookRotation(Vector3.up, Vector3.back)));
            Fake.SetPose(HandSide.Left, PinchUp(HandSide.Left, Air(-0.15f)));
            Fake.SetPose(HandSide.Right, PinchUp(HandSide.Right, from));
            yield return Frames(100);
            Assert.That(Layout.BringBacks, Is.EqualTo(backs), "pointing at the drums while moving them");
            yield return OpenHands(Air(-0.15f), from);
            yield return Frames(30);
            Layout.EndArrange();
        }

        [UnityTest]
        public IEnumerator TheMenuRowAndTheHoldAreOnePath()
        {
            Vector3 head = Head.position;
            SeatAt(head, 90f, arrange: false);
            int arranged = 0;
            App.Events.Subscribe<ArrangedEvent>(e => { if (e.Forced) arranged++; });
            int backs = Layout.BringBacks;
            ((ILayoutService)Layout).BringToPlayer();   // the menu's Reset view row calls exactly this
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 1));
            Assert.That(Layout.LastBringBack, Is.EqualTo(BringBackSource.Menu));
            Assert.That(arranged, Is.EqualTo(1));
            Snap();
            // Somewhere else and back to the same pose, then the hold: the very same placement.
            SeatAt(head, 0f, arrange: true);
            SeatAt(head, 90f, arrange: false);
            RaysUp();
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            yield return HoldBothUntilBack(l, r, 100);
            Assert.That(Layout.LastBringBack, Is.EqualTo(BringBackSource.Gesture));
            AssertUnmoved("the hold placed them where the menu row did");
            yield return OpenHands(l, r);
        }

        [UnityTest]
        public IEnumerator TheHeadMovingAndTheHoldAllocateNothingPerFrame()
        {
            Vector3 head0 = Head.position;
            OpenPalmsStill(head0, Quaternion.identity);
            for (int i = 0; i < 10; i++) Boot.Step(DT);
            var heads = new Quaternion[20];
            for (int i = 0; i < heads.Length; i++) heads[i] = Quaternion.Euler(10f * Mathf.Sin(i * 0.4f), 60f * Mathf.Sin(i * 0.3f), 0f);
            for (int i = 0; i < heads.Length; i++)
            {
                Head.rotation = heads[i];
                Assert.That(() => Boot.Step(DT), Is.Not.AllocatingGCMemory(), "frame " + i + " of the head turning");
            }
            Head.rotation = Quaternion.identity;
            RaysUp();
            Vector3 l = Air(-0.15f), r = Air(0.15f);
            Fake.SetPose(HandSide.Left, PinchUp(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, PinchUp(HandSide.Right, r));
            for (int i = 0; i < 15; i++) Boot.Step(DT);   // the ring is up
            Assert.IsTrue(Layout.Visuals.BringRingVisible);
            Assert.That(() => { for (int i = 0; i < 20; i++) Boot.Step(DT); }, Is.Not.AllocatingGCMemory(), "the two-hand hold with its ring");
            yield return OpenHands(l, r);
        }
    }
}
