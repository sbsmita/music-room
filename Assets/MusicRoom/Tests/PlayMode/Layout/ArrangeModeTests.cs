// "Move instruments" (IArrangeMode) against the golden rigs (GoldenRigsProbe), scripted hands at 72 Hz with manual
// frames and a simulated headset: entry and exit (events, highlights, silence: Core ticks no instrument, sticks dropped,
// a lesson stopped), near grabs on any part (keys, heads, strings included) after the 80 ms debounce, ray grabs from
// the system pointer pose with the push gain, the lift with the hand inside the height ranges (risers), yaw with the wrist and with two hands, no overlap with
// the settle on release, persistence across a reboot of the test app, Reset, the lock, Meta's pinch with hysteresis and
// the system-gesture exclusion.
using System.Collections;
using System.Collections.Generic;
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
    public class ArrangeModeTests : LayoutPlayTestBase
    {
        static readonly Vector3 OriginSeat = new Vector3(0f, 1.2f, 0f);
        readonly List<bool> _events = new List<bool>();
        System.Action<ArrangeModeEvent> _onArrange;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return BootApp();
            SeatAt(OriginSeat, 0f);
            _events.Clear();
            _onArrange = e => _events.Add(e.Active);
            App.Events.Subscribe(_onArrange);
        }

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        Vector3 KeysPoint() => Probe.Rigs[GoldenLayout.Piano].Group.TransformPoint(new Vector3(0.1f, 0.004f, 0.07f));
        Vector3 SnarePoint() => Probe.Drums.Pieces[0].Transform.position + Vector3.up * 0.01f;
        Vector3 StringPoint() => Probe.Rigs[GoldenLayout.Harp].Group.TransformPoint(Probe.Harp.PlayPoint(11));

        /// <summary>A hand comes in open 4 cm above the point, then pinches there for <paramref name="frames"/> frames.</summary>
        IEnumerator PinchFor(HandSide side, Vector3 at, int frames, float yawDeg = 0f)
        {
            Fake.SetPose(side, OpenAt(side, at + Vector3.up * 0.04f, yawDeg));
            yield return Frame();
            for (int i = 0; i < frames; i++)
            {
                Fake.SetPose(side, PinchAt(side, at, yawDeg));
                yield return Frame();
            }
        }

        /// <summary>The hand opens: the drag holds through MoveConstants.ReleaseGrace (a flicker never drops it), then drops,
        /// and the instrument eases to its free spot (MoveConstants.SettleSeconds).</summary>
        IEnumerator Drop(HandSide side, Vector3 at)
        {
            yield return Release(side, at);
            yield return Frames(Mathf.CeilToInt((MoveConstants.ReleaseGrace + MoveConstants.SettleSeconds) / DT) + 2);
        }

        IEnumerator DragTo(HandSide side, Vector3 from, Vector3 to, int frames = 24, float yawDeg = 0f)
        {
            for (int i = 1; i <= frames; i++)
            {
                Fake.SetPose(side, PinchAt(side, Vector3.Lerp(from, to, i / (float)frames), yawDeg));
                yield return Frame();
            }
        }

        [UnityTest]
        public IEnumerator EnteringSilencesHighlightsAndLeavingPlaysAgain()
        {
            int[] before = new int[4];
            yield return Frames(2);
            for (int i = 0; i < 4; i++) before[i] = Probe.Instruments[i].TickCount;
            Assert.That(before[0], Is.GreaterThan(0), "the instruments tick before");
            Probe.Tutorial.Start("piano-scale");
            int releases = Probe.Drums.ReleaseCount;
            Layout.BeginArrange();
            Assert.IsTrue(Layout.Arranging);
            Assert.That(_events, Is.EqualTo(new[] { true }), "ArrangeModeEvent{Active}");
            Assert.That(Probe.Drums.ReleaseCount, Is.EqualTo(releases + 1), "the sticks go back on the snare");
            Assert.That(Probe.Tutorial.State, Is.EqualTo(TutorialState.Idle), "a running lesson stops");
            Assert.IsTrue(App.Get<IArrangeMode>() == Layout, "registered as IArrangeMode");
            yield return Frames(10);
            for (int i = 0; i < 4; i++) Assert.That(Probe.Instruments[i].TickCount, Is.EqualTo(before[i]), GoldenLayout.RigNames[i] + " is silent");
            int shown = 0;
            for (int s = 0; s < ArrangeVisuals.Slots; s++)
                if (Layout.ArrangeView.SlotVisible[s]) { shown++; Assert.That(Layout.ArrangeView.SlotLook[s], Is.EqualTo(ArrangeVisuals.Look.Idle)); }
            Assert.That(shown, Is.EqualTo(4), "every instrument of the layout and the side table are highlighted");
            Layout.BeginArrange();
            Assert.That(_events.Count, Is.EqualTo(1), "once");

            Layout.EndArrange();
            Assert.IsFalse(Layout.Arranging);
            Assert.That(_events, Is.EqualTo(new[] { true, false }));
            yield return Frames(3);
            Assert.That(Probe.Instruments[0].TickCount, Is.GreaterThan(before[0]), "playing works again");
            for (int s = 0; s < ArrangeVisuals.Slots; s++) Assert.IsFalse(Layout.ArrangeView.SlotVisible[s]);
            // The interface's toggle.
            ((IArrangeMode)Layout).ToggleArrange();
            Assert.IsTrue(Layout.Arranging);
            ((IArrangeMode)Layout).ToggleArrange();
            Assert.IsFalse(Layout.Arranging);
        }

        [UnityTest]
        public IEnumerator ANearPinchOnAnyPartPicksItUpAfterTheDebounce()
        {
            // Outside arrange mode a pinch on the keys plays: it never grabs.
            yield return PinchHold(HandSide.Right, KeysPoint());
            Assert.IsNull(HeldAfter, "in play, the keys are never a grab handle");
            yield return Release(HandSide.Right, KeysPoint());

            Layout.BeginArrange();
            var cases = new[] { (GoldenLayout.Piano, "keys"), (GoldenLayout.Drums, "snare head"), (GoldenLayout.Harp, "a string") };
            foreach (var (n, what) in cases)
            {
                Vector3 at = n == GoldenLayout.Piano ? KeysPoint() : n == GoldenLayout.Drums ? SnarePoint() : StringPoint();
                at += Vector3.up * 0.05f;   // 5 cm off the part: inside the 8 cm reach
                Vector3 c0 = Centre(n);
                yield return PinchFor(HandSide.Right, at, 4);
                Assert.IsNull(Layout.HeldRig, what + ": not before the debounce (4 frames = 56 ms)");
                Assert.That(Layout.NearTarget(1), Is.SameAs(Probe.Rigs[n]), what + ": the near target");
                for (int i = 0; i < 3; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at)); yield return Frame(); }
                Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[n]), what + ": held after about 80 ms");
                Assert.IsFalse(Layout.HeldByRay);
                Assert.That(Layout.ArrangeView.SlotLook[SlotOf(n)], Is.EqualTo(ArrangeVisuals.Look.Held));
                Assert.IsTrue(Layout.Visuals.RingVisible, "the floor ring while held");
                Vector3 away = c0 - Head.position; away.y = 0f; away.Normalize();
                Vector3 to = at + away * 0.2f;
                yield return DragTo(HandSide.Right, at, to);
                int saves = Layout.ArrangeSaves;
                yield return Drop(HandSide.Right, to);
                Assert.IsNull(Layout.HeldRig, what + ": let go");
                Assert.That(Layout.ArrangeSaves, Is.EqualTo(saves + 1), what + ": the release is saved");
                Assert.That(Vector3.Distance(Centre(n), c0), Is.InRange(0.15f, 0.25f), what + ": moved about 20 cm, with the hand");
                SeatAt(OriginSeat, 0f);
                Layout.Placement.MeasureAll();
            }
        }

        int SlotOf(int rig)
        {
            for (int s = 0; s < ArrangeVisuals.Slots; s++) if (Layout.ArrangeView.SlotRig[s] == Probe.Rigs[rig]) return s;
            return -1;
        }

        [UnityTest]
        public IEnumerator ARayFromThePointerPoseHighlightsGrabsAndPushes()
        {
            Layout.BeginArrange();
            yield return Frame();
            Transform harp = Probe.Rigs[GoldenLayout.Harp].Group;
            // A hand 1 m to the harp's left, pointing at its pillar (nothing else on that line; nothing within 8 cm).
            Vector3 target = harp.TransformPoint(new Vector3(-0.69f, 0.6f, 0f));
            Vector3 dir = harp.position - Head.position; dir.y = 0f; dir.Normalize();
            Vector3 hand = target + dir * 1.0f;
            var pointer = new Pose(hand, Quaternion.LookRotation(target - hand));
            Fake.SetPointer(HandSide.Right, pointer);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, hand));
            yield return Frames(2);
            Assert.IsNull(Layout.NearTarget(1), "nothing within reach of the hand");
            Assert.That(Layout.RayTarget(1), Is.SameAs(Probe.Rigs[GoldenLayout.Harp]), "the ray lands on the harp");
            Assert.IsTrue(Layout.ArrangeView.RayVisible[1]);
            Assert.IsTrue(Layout.ArrangeView.RayHits[1]);
            Assert.That(Layout.ArrangeView.RayLength[1], Is.InRange(0.3f, Vector3.Distance(hand, target) + 0.05f), "the ray ends on the harp");
            Assert.That(Layout.ArrangeView.SlotLook[SlotOf(GoldenLayout.Harp)], Is.EqualTo(ArrangeVisuals.Look.Target), "the harp lights up");
            Assert.IsFalse(Layout.ArrangeView.RayVisible[0], "the left hand is not tracked");
            Vector3 c0 = Centre(GoldenLayout.Harp);
            for (int i = 0; i < 7; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, hand)); yield return Frame(); }
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Harp]), "a pinch takes it along the ray");
            Assert.IsTrue(Layout.HeldByRay);
            Assert.That(Layout.RayGrabs, Is.EqualTo(1));
            // Pull the hand 10 cm back along the ray (away from the harp): the harp comes 25 cm along (x 2.5).
            Vector3 rayDir = (target - hand).normalized;
            for (int i = 1; i <= 20; i++)
            {
                Vector3 h = hand - rayDir * (0.1f * i / 20f);
                Fake.SetPointer(HandSide.Right, new Pose(h, pointer.rotation));
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, h));
                yield return Frame();
            }
            Vector3 moved = Centre(GoldenLayout.Harp) - c0;
            float along = Vector3.Dot(moved, -new Vector3(rayDir.x, 0f, rayDir.z).normalized);
            Assert.That(along, Is.EqualTo(0.25f * new Vector2(rayDir.x, rayDir.z).magnitude).Within(0.05f), "2.5 times the hand's 10 cm");
            // The height follows the hand itself (never the ray's far end): the hand rose rayDir.y x -10 cm.
            float rise = Mathf.Max(0f, -rayDir.y * 0.1f - MoveConstants.LiftDeadband);
            Assert.That(Probe.Rigs[GoldenLayout.Harp].Group.position.y, Is.EqualTo(rise).Within(0.006f), "the harp rises only with the hand's own height");
            Assert.IsTrue(Layout.ArrangeView.RayVisible[1] && Layout.ArrangeView.RayHits[1], "the ray stays on the held harp");
            // Aim 10 degrees to the side: it swings round the hand.
            Vector3 h1 = hand - rayDir * 0.1f;
            Vector3 c1 = Centre(GoldenLayout.Harp);
            for (int i = 1; i <= 10; i++)
            {
                Fake.SetPointer(HandSide.Right, new Pose(h1, Quaternion.AngleAxis(-10f * i / 10f, Vector3.up) * pointer.rotation));
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, h1));
                yield return Frame();
            }
            Assert.That(Vector3.Distance(Centre(GoldenLayout.Harp), c1), Is.GreaterThan(0.05f), "aiming moves it");
            yield return Drop(HandSide.Right, h1);
            Assert.IsNull(Layout.HeldRig);
            Assert.IsFalse(Layout.HeldByRay);
        }

        /// <summary>Since 2026-11-10 a drag is yaw only on the floor: the piano stays at its stand height, the kit and the harp
        /// on the floor, however the hand rises or falls (a hand drifting up or down while dragging never lifts or drops one).</summary>
        [UnityTest]
        public IEnumerator TheHandLiftsAndLowersItInsideItsRange()
        {
            // 2026-11-11: "allow it place instrument to their height/level". The hand's height moves it 1:1 past a 2 cm soft
            // dead band, inside its own range (the piano's keys 0.55-1.10 m, the kit and the harp from the floor up to a
            // standing height on a riser), never tilting it; a level drag never bobs.
            Layout.BeginArrange();
            foreach (int n in new[] { GoldenLayout.Piano, GoldenLayout.Drums, GoldenLayout.Harp })
            {
                Rig rig = Probe.Rigs[n];
                Transform g = rig.Group;
                float y0 = g.position.y;
                LayoutModule.LiftRange(rig, out float lo, out float hi);
                string name = GoldenLayout.RigNames[n];
                Vector3 at = n == GoldenLayout.Piano ? KeysPoint() + Vector3.up * 0.04f : Handle(n);
                yield return PinchFor(HandSide.Right, at, 8);
                Assert.That(Layout.HeldRig, Is.SameAs(rig));
                // level, with 1.5 cm of vertical wobble: the same height
                for (int i = 1; i <= 24; i++)
                {
                    Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + Vector3.up * (0.015f * Mathf.Sin(i * 0.7f))));
                    yield return Frame();
                }
                Assert.That(g.position.y, Is.EqualTo(y0).Within(1e-4f), name + ": a level drag stays level");
                // 20 cm up, held: 18 cm up (the dead band), 1:1 after it
                yield return DragTo(HandSide.Right, at, at + Vector3.up * 0.2f, 20);
                yield return Hold(HandSide.Right, at + Vector3.up * 0.2f, 24);
                Assert.That(g.position.y, Is.EqualTo(Mathf.Min(hi, y0 + 0.18f)).Within(0.004f), name + ": the hand 20 cm higher");
                Assert.That(Vector3.Angle(g.up, Vector3.up), Is.LessThan(0.01f), name + ": never tilted");
                if (n != GoldenLayout.Piano)
                {
                    Assert.That(Layout.RaiseOf(rig), Is.EqualTo(g.position.y - y0).Within(1e-3f), name + ": on a riser as high as it was lifted");
                    Assert.That(Layout.Risers.Centre((int)rig.Id).y - Layout.Risers.Size((int)rig.Id).y * 0.5f, Is.EqualTo(0f).Within(1e-3f), name + ": the riser on the floor");
                }
                else Assert.That(Layout.RaiseOf(rig), Is.EqualTo(0f), "the piano's stand stretches: no riser");
                // 80 cm up: its top; 80 cm down: its bottom (the piano's lowest keys, the others on the floor)
                yield return DragTo(HandSide.Right, at + Vector3.up * 0.2f, at + Vector3.up * 0.8f, 20);
                yield return Hold(HandSide.Right, at + Vector3.up * 0.8f, 24);
                Assert.That(g.position.y, Is.EqualTo(hi).Within(1e-4f), name + ": no higher than its range");
                yield return DragTo(HandSide.Right, at + Vector3.up * 0.8f, at - Vector3.up * 0.8f, 40);
                yield return Hold(HandSide.Right, at - Vector3.up * 0.8f, 24);
                Assert.That(g.position.y, Is.EqualTo(lo).Within(1e-4f), name + ": no lower than its range");
                Assert.That(lo, Is.EqualTo(n == GoldenLayout.Piano ? ArrangeConstants.PianoKeyMin : y0).Within(1e-4f), name + ": the floor (the piano's lowest keys)");
                Assert.That(Layout.RaiseOf(rig), Is.EqualTo(0f), name + ": no riser on the floor");
                // back up 10 cm and let go: the height is kept after the drop and its settle
                yield return DragTo(HandSide.Right, at - Vector3.up * 0.8f, at - Vector3.up * 0.7f, 10);
                yield return Hold(HandSide.Right, at - Vector3.up * 0.7f, 24);
                float kept = g.position.y;
                Assert.That(kept, Is.EqualTo(lo + 0.1f).Within(0.004f), name + ": 10 cm back up from the bottom, 1:1");
                yield return Drop(HandSide.Right, at - Vector3.up * 0.7f);
                Assert.That(g.position.y, Is.EqualTo(kept).Within(1e-4f), name + ": and after the drop");
                Assert.That(Layout.TryGetOffset(rig.Id, out ComfortOffset o), Is.True, name + ": kept");
                Assert.That(o.Height, Is.EqualTo(kept - y0).Within(1e-3f), name + ": the height tweak saved");
            }
        }

        /// <summary>The pinch held still at a point (the lift's filter catches up).</summary>
        IEnumerator Hold(HandSide side, Vector3 at, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Fake.SetPose(side, PinchAt(side, at));
                yield return Frame();
            }
        }

        [UnityTest]
        public IEnumerator TheWristAndTwoHandsTurnIt()
        {
            Layout.BeginArrange();
            for (int pass = 0; pass < 2; pass++)
            {
                SeatAt(OriginSeat, 0f);
                // In the open floor toward the fire (GrabTests.Open: room frame (0, -2.3), turned), so nothing refuses the turn.
                Transform g = Probe.Rigs[GoldenLayout.Drums].Group;
                Vector3 c = Centre(GoldenLayout.Drums);
                Vector3 open = RoomFrame.ToWorld(new Vector3(0f, 0f, -2.3f));
                g.position += new Vector3(open.x - c.x, 0f, open.z - c.z);
                Layout.Placement.MeasureAll();
                c = Centre(GoldenLayout.Drums);
                float y0 = g.eulerAngles.y;
                Vector3 a = Handle(GoldenLayout.Drums);
                yield return PinchFor(HandSide.Right, a, 8);
                Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
                if (pass == 0)
                {
                    // One hand: the wrist's yaw, 90 degrees, spins it TurnGain times as far past the dead band (about its centre).
                    for (int i = 1; i <= 30; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, a, 90f * i / 30f)); yield return Frame(); }
                    yield return Frames(30);
                    float want = MoveConstants.TurnGain * (90f - MoveConstants.TurnDeadband * Mathf.Rad2Deg);
                    Assert.That(Mathf.DeltaAngle(y0, g.eulerAngles.y), Is.EqualTo(Mathf.DeltaAngle(0f, want)).Within(5f), "spins with the wrist");
                    Assert.That(Vector3.Distance(Centre(GoldenLayout.Drums), c), Is.LessThan(0.06f), "about its centre");
                    yield return Drop(HandSide.Right, a);
                }
                else
                {
                    // Two hands on it twist it about their midpoint.
                    Vector3 ra = a - c; ra.y = 0f;
                    Vector3 b = c - ra; b.y = a.y;
                    yield return PinchFor(HandSide.Left, b, 2);
                    Assert.That(Layout.SecondHand, Is.SameAs(Hand(HandSide.Left)), "the second hand joins");
                    for (int i = 1; i <= 40; i++)
                    {
                        Quaternion q = Quaternion.Euler(0f, 90f * i / 40f, 0f);
                        Vector3 pa = c + q * ra; pa.y = a.y;
                        Vector3 pb = c - q * ra; pb.y = a.y;
                        Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, pa));
                        Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, pb));
                        yield return Frame();
                    }
                    Assert.That(Mathf.DeltaAngle(y0, g.eulerAngles.y), Is.EqualTo(90f).Within(5f), "a two-hand twist");
                    Assert.That(Vector3.Distance(Centre(GoldenLayout.Drums), c), Is.LessThan(0.06f), "about its centre");
                    yield return ReleaseHands(c);
                    yield return Frames(Mathf.CeilToInt((MoveConstants.LostGrace + MoveConstants.SettleSeconds) / DT) + 2);
                }
            }
        }

        [UnityTest]
        public IEnumerator NoOverlapWhileMovingAndTheReleaseSettlesClear()
        {
            Layout.BeginArrange();
            Vector3 at = Handle(GoldenLayout.Drums);
            yield return PinchFor(HandSide.Right, at, 8);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            // Straight into the piano: it stops at contact, then (pushed on for 300 ms) goes through with a red ring.
            Vector3 into = Centre(GoldenLayout.Piano) - Centre(GoldenLayout.Drums); into.y = 0f;
            bool red = false;
            for (int i = 1; i <= 50; i++)
            {
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + into * Mathf.Min(1f, i / 25f)));
                yield return Frame();
                red |= Layout.PassingThrough;
            }
            Assert.IsTrue(red, "pushed on, it passes through (red)");
            yield return Drop(HandSide.Right, at + into);
            Assert.That(Layout.Placement.Overlaps2D(), Is.EqualTo(0), "let go inside the piano, it settles clear");
            foreach (Rig r in Probe.Rigs) Assert.That(Layout.EyeDistance(r), Is.GreaterThanOrEqualTo(0.3f - 1e-4f), r.Id + ": 30 cm from the eyes");
        }

        [UnityTest]
        public IEnumerator ThePlacementSurvivesARebootAndResetForgetsIt()
        {
            Layout.BeginArrange();
            Transform kit = Probe.Rigs[GoldenLayout.Drums].Group;
            Vector3 at = Handle(GoldenLayout.Drums);
            yield return PinchFor(HandSide.Right, at, 8);
            float kitY = kit.position.y;
            Vector3 to = at + new Vector3(0.3f, 0.1f, 0f);
            yield return DragTo(HandSide.Right, at, to);
            yield return Drop(HandSide.Right, to);
            Layout.EndArrange();
            float headYaw = 0f;
            SeatPose want = ArrangementCodec.ToSeat(kit.position, kit.eulerAngles.y, Head.position, headYaw);
            Assert.That(want.Local.y, Is.EqualTo(kitY + 0.1f - MoveConstants.LiftDeadband).Within(0.015f), "the hand rose 10 cm: the kit 8 cm (past the dead band), on a riser");
            // Kept as comfort offsets from the default poses in front of the player (ComfortCodec, since 2026-11-02; the
            // seat-frame "a1;" text before), which put the kit back at the same place about any seat.
            string saved = Boot.Settings.GetString(AppSettings.ComfortKey(LayoutMode.All, InstrumentKind.Piano), "");
            Assert.That(saved, Does.StartWith("c1;"), "saved in AppSettings");
            Assert.That(Boot.Settings.Saves, Is.GreaterThan(0));

            // A new launch on a fresh store holding only that value, the player sitting somewhere else, turned 40 degrees.
            yield return TearDownApp();
            yield return BootApp(settings: s => s.SetString(AppSettings.ComfortKey(LayoutMode.All, InstrumentKind.Piano), saved));
            Vector3 seat = new Vector3(-0.6f, 1.1f, -1.0f);
            SeatAt(seat, 40f);
            kit = Probe.Rigs[GoldenLayout.Drums].Group;
            SeatPose got = ArrangementCodec.ToSeat(kit.position, kit.eulerAngles.y, Head.position, 40f * Mathf.Deg2Rad);
            Assert.That(Vector3.Distance(got.Local, want.Local), Is.LessThan(0.01f), $"the kit comes back where the player put it: {got} vs {want}");
            Assert.That(Mathf.DeltaAngle(got.Yaw, want.Yaw), Is.EqualTo(0f).Within(0.5f));
            Assert.That(Layout.Placement.Overlaps2D(), Is.EqualTo(0));

            // Reset: the homes again, the saved value gone.
            Layout.BeginArrange();
            Layout.ResetArrangement();
            Assert.IsTrue(Layout.Arranging, "Reset keeps arranging");
            Assert.That(Layout.ArrangeResets, Is.EqualTo(1));
            Assert.That(Boot.Settings.GetString(AppSettings.ComfortKey(LayoutMode.All, InstrumentKind.Piano), ""), Is.EqualTo(""));
            SeatPose home = ArrangementCodec.ToSeat(kit.position, kit.eulerAngles.y, Head.position, 40f * Mathf.Deg2Rad);
            Rig kitRig = Probe.Rigs[GoldenLayout.Drums];
            Assert.That(Vector3.Distance(home.Local, kitRig.HomeAll), Is.LessThan(0.02f), "the kit at its home about the seat");
            Assert.That(Mathf.DeltaAngle(home.Yaw, kitRig.HomeYawAll), Is.EqualTo(0f).Within(0.5f));
            Assert.That(Probe.Rigs[GoldenLayout.Piano].Group.position.y, Is.EqualTo(0.72f).Within(1e-4f), "the piano at its home height");
        }

        [UnityTest]
        public IEnumerator EachLayoutKeepsItsOwnPlacement()
        {
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Layout.BeginArrange();
            Vector3 at = Handle(GoldenLayout.Harp);
            yield return PinchFor(HandSide.Right, at, 8);
            yield return DragTo(HandSide.Right, at, at + new Vector3(0.25f, 0f, 0f));
            yield return Drop(HandSide.Right, at + new Vector3(0.25f, 0f, 0f));
            Layout.EndArrange();
            // The comfort offsets of each layout have their own key (the seat-frame "a1;" text before 2026-11-02).
            Assert.That(Boot.Settings.GetString(AppSettings.ComfortKey(LayoutMode.Single, InstrumentKind.Harp), ""), Does.StartWith("c1;"));
            Assert.That(Boot.Settings.GetString(AppSettings.ComfortKey(LayoutMode.All, InstrumentKind.Harp), ""), Is.EqualTo(""), "the full room is untouched");
            Assert.That(Boot.Settings.GetString(AppSettings.ComfortKey(LayoutMode.Single, InstrumentKind.Piano), ""), Is.EqualTo(""));
            Transform harp = Probe.Rigs[GoldenLayout.Harp].Group;
            SeatPose single = ArrangementCodec.ToSeat(harp.position, harp.eulerAngles.y, Head.position, 0f);
            // The full room uses the homes; back to the harp alone, it is where it was put.
            Layout.Set(LayoutMode.All, InstrumentKind.Harp);
            SeatPose all = ArrangementCodec.ToSeat(harp.position, harp.eulerAngles.y, Head.position, 0f);
            Assert.That(Vector3.Distance(all.Local, Probe.Rigs[GoldenLayout.Harp].HomeAll), Is.LessThan(0.02f));
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            SeatPose again = ArrangementCodec.ToSeat(harp.position, harp.eulerAngles.y, Head.position, 0f);
            Assert.That(Vector3.Distance(again.Local, single.Local), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator MoveOffNoPinchMovesItMoveOnAPinchTakesIt()
        {
            Layout.InPlayGrab = false;   // the app's rule
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Drums));
            Assert.IsNull(HeldAfter, "Move instruments off: no pinch moves an instrument");
            yield return Drop(HandSide.Right, Handle(GoldenLayout.Drums));
            Layout.BeginArrange();
            yield return PinchFor(HandSide.Right, Handle(GoldenLayout.Drums), 8);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "Move instruments on: the pinch takes it");
            yield return Drop(HandSide.Right, Handle(GoldenLayout.Drums));
            Layout.InPlayGrab = true;
        }

        [UnityTest]
        public IEnumerator MetasPinchAloneGrabsWithHysteresis()
        {
            Layout.BeginArrange();
            Vector3 at = SnarePoint() + Vector3.up * 0.03f;
            // An open hand (thumb 11 cm from the index tip: no geometric pinch) whose runtime pinch strength rises.
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at));
            Fake.SetMetaPinch(HandSide.Right, 0.8f);
            yield return Frames(3);
            HandState h = Hand(HandSide.Right);
            Assert.IsFalse(h.Pinching);
            Assert.IsFalse(h.GrabPinching, "0.8 is below the 0.85 start");
            Fake.SetMetaPinch(HandSide.Right, 0.9f);
            yield return Frame();
            Assert.IsTrue(h.GrabPinching, "0.9 starts Meta's pinch");
            Assert.IsTrue(h.MetaPinching);
            yield return Frames(7);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "Meta's pinch alone picks the kit up");
            Fake.SetMetaPinch(HandSide.Right, 0.6f);
            yield return Frames(5);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "0.6 keeps holding");
            Fake.SetMetaPinch(HandSide.Right, 0.45f);
            yield return Frames(15);
            Assert.IsFalse(h.GrabPinching, "below 0.5 the pinch has ended");
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "but the drag holds down to strength 0.3 (its own hysteresis)");
            Fake.SetMetaPinch(HandSide.Right, 0.2f);
            yield return Frames(Mathf.CeilToInt(MoveConstants.ReleaseGrace / DT) + 2);
            Assert.IsNull(Layout.HeldRig, "below 0.3 it lets go after the grace");
            Fake.SetMetaPinch(HandSide.Right, 0f);
            Layout.EndArrange();
            yield return Frame();

            // The in-play grab reads the same pinch: a still Meta pinch on a handle takes hold after the 0.3 s dwell.
            Vector3 handle = Handle(GoldenLayout.Drums);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, handle));
            yield return Frame();
            Fake.SetMetaPinch(HandSide.Right, 1f, runtimePinch: true);
            for (int i = 0; i < 26; i++) { Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, Jitter(handle, 0.005f))); yield return Frame(); }
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "the in-play dwell with Meta's pinch");
            Fake.SetMetaPinch(HandSide.Right, 0f);
            yield return Frame();
            Assert.IsNull(Layout.HeldRig);
        }

        [UnityTest]
        public IEnumerator TheSystemGestureIsNeverAGrab()
        {
            Layout.BeginArrange();
            // Palm toward the headset, pinching right by the keys: Quest's system-menu gesture.
            Vector3 at = KeysPoint() + Vector3.up * 0.04f;
            Vector3 toHead = (Head.position - at).normalized;
            // Fingers up in the plane facing the eyes, so the palm looks straight at them.
            HandPose facing = HandPose.OpenFacing(HandSide.Right, at, Vector3.ProjectOnPlane(Vector3.up, toHead).normalized, toHead);
            Fake.SetPose(HandSide.Right, facing.WithIndexTipAt(at));
            yield return Frame();
            for (int i = 0; i < 10; i++) { Fake.SetPose(HandSide.Right, facing.WithIndexTipAt(at).WithPinch(0f)); yield return Frame(); }
            Assert.IsTrue(Hand(HandSide.Right).GrabPinching, "it is a pinch");
            Assert.IsNull(Layout.HeldRig, "but never a grab");
            Assert.IsTrue(Layout.PinchIgnored(1));
            Assert.IsFalse(Layout.ArrangeView.RayVisible[1], "and no ray while the palm faces the headset");
            yield return Drop(HandSide.Right, at);
            // With Quest's SystemGestureInProgress flag up, the pinch state is held: no pinch even starts.
            Fake.SystemGesture = true;
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at));
            yield return Frame();
            for (int i = 0; i < 10; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at)); yield return Frame(); }
            Assert.IsFalse(Hand(HandSide.Right).GrabPinching);
            Assert.IsNull(Layout.HeldRig);
            Fake.SystemGesture = false;
            // The same pinch palm down, a moment later, does grab.
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at));
            yield return Frame();
            yield return PinchFor(HandSide.Right, at, 8);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Piano]));
            yield return Drop(HandSide.Right, at);
        }

        [UnityTest]
        public IEnumerator NoSwivelSummonOrPageTurnWhileArranging()
        {
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Layout.BeginArrange();
            int snaps = Layout.SnapTurns, summons = Layout.Summons, turns = Layout.Turns;
            // Both hands pinching in the air for a second, the right one pulled sideways 20 cm.
            Vector3 l = Head.position + new Vector3(-0.3f, -0.1f, 0.15f), r = Head.position + new Vector3(0.3f, -0.1f, 0.15f);
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, l));
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, r));
            yield return Frame();
            for (int i = 0; i < 72; i++)
            {
                Fake.SetPose(HandSide.Left, PinchAt(HandSide.Left, l));
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, r + Vector3.right * (0.2f * Mathf.Min(1f, i / 20f))));
                yield return Frame();
            }
            Assert.That(Layout.SnapTurns, Is.EqualTo(snaps), "no swivel");
            Assert.That(Layout.Summons, Is.EqualTo(summons), "no two-hand summon");
            Assert.That(Layout.Turns, Is.EqualTo(turns), "no page turn");
            yield return ReleaseHands(r);
            yield return Frames(Mathf.CeilToInt(MoveConstants.LostGrace / DT) + 2);
        }

        [UnityTest]
        public IEnumerator TheFrameStaysAllocationFreeWhileArranging()
        {
            Layout.BeginArrange();
            Vector3 at = Handle(GoldenLayout.Drums);
            Fake.SetPointer(HandSide.Left, new Pose(Head.position + new Vector3(-0.2f, -0.2f, 0.2f), Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.3f)));
            Fake.SetPose(HandSide.Left, OpenAt(HandSide.Left, Head.position + new Vector3(-0.2f, -0.2f, 0.2f)));
            float y0 = Probe.Rigs[GoldenLayout.Drums].Group.eulerAngles.y;
            yield return PinchFor(HandSide.Right, at, 8);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            for (int i = 0; i < 5; i++) Boot.Step(DT);
            Assert.That(() => { for (int i = 0; i < 10; i++) Boot.Step(DT); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "a held drag with a ray on the other hand allocates nothing per frame");
            // Moving, turning and lifting: the filtered drag, the wrist spin, the lift and its riser, the constraint and the
            // outline, frame by frame. (The
            // scripted hand copies each pose when it is set, so the poses are made first and set outside the measure.)
            Vector3 fwd = Head.forward; fwd.y = 0f; fwd.Normalize();
            var poses = new HandPose[30];
            for (int i = 0; i < poses.Length; i++) poses[i] = PinchAt(HandSide.Right, at - fwd * (0.004f * i) + Vector3.up * (0.008f * i), 2f * i);
            float h0 = Probe.Rigs[GoldenLayout.Drums].Group.position.y;
            for (int i = 0; i < poses.Length; i++)
            {
                Fake.SetPose(HandSide.Right, poses[i]);
                Assert.That(() => Boot.Step(DT), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "frame " + i + " of a moving, turning drag");
            }
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]), "still held");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(y0, Probe.Rigs[GoldenLayout.Drums].Group.eulerAngles.y)), Is.GreaterThan(5f), "it turned");
            Assert.That(Probe.Rigs[GoldenLayout.Drums].Group.position.y - h0, Is.GreaterThan(0.1f), "it rose with the hand (moving, turning and lifting, on its riser)");
            Assert.That(Layout.RaiseOf(Probe.Rigs[GoldenLayout.Drums]), Is.GreaterThan(0.1f), "on its riser");
            yield return Drop(HandSide.Right, at);
        }
    }
}
