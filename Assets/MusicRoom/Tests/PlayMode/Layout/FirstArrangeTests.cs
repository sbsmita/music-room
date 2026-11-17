// When the room is laid out around the player (vrAuto set on every session start; spec 08 section
// 5.4 "on the first tracked VR frame"): never on the desktop, where the rigs stay at their homes about ORIGIN (0, 1.2, 0)
// with the desktop camera 10 cm behind; on the first presenting frame of every XR session, including a runtime that
// comes up after the desktop fallback (RigBootstrap keeps polling). A single-instrument layout restored from the
// settings is Layout's Set(...) on the desktop: arranged about the desktop camera, without fitSeat.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class FirstArrangeTests : LayoutPlayTestBase
    {
        static readonly Vector3 OriginSeat = new Vector3(0f, 1.2f, 0f);

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        static GoldenLayout.Scenario Find(string label)
        {
            foreach (var s in GoldenLayout.Scenarios) if (s.Label == label) return s;
            throw new System.ArgumentException(label);
        }

        void AssertRigs(GoldenLayout.RigPose[] want, string what) => AssertRigs(want, what, Vector3.zero, 0f);

        /// <summary>Each rig at the golden pose <paramref name="want"/> (laid out about ORIGIN), carried to a seat moved by
        /// <paramref name="seat"/> - ORIGIN and turned by <paramref name="yawDeg"/> (the zero seat = as recorded).</summary>
        void AssertRigs(GoldenLayout.RigPose[] want, string what, Vector3 seat, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            for (int r = 0; r < 4; r++)
            {
                Rig rig = Probe.Rigs[r];
                Assert.That(rig.InScene, Is.EqualTo(want[r].InScene), $"{what}: {GoldenLayout.RigNames[r]} in the scene");
                Assert.That(rig.Group.gameObject.activeSelf, Is.EqualTo(want[r].InScene), $"{what}: {GoldenLayout.RigNames[r]} active");
                if (!want[r].InScene) continue;
                Vector3 p = want[r].Pos;
                if (seat != Vector3.zero)
                {
                    Vector3 d = rot * new Vector3(p.x - OriginSeat.x, 0f, p.z - OriginSeat.z);
                    p = new Vector3(seat.x + d.x, p.y, seat.z + d.z);
                }
                Assert.That(Vector3.Distance(rig.Group.position, p), Is.LessThan(2e-3f), $"{what}: {GoldenLayout.RigNames[r]} at {rig.Group.position.ToString("F4")}, want {p.ToString("F4")}");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(rig.Group.eulerAngles.y, want[r].YawDeg + yawDeg)), Is.LessThan(0.05f), $"{what}: {GoldenLayout.RigNames[r]} yaw");
            }
        }

        [UnityTest]
        public IEnumerator OnTheDesktopTheRoomStaysAtTheGoldenHomesAboutTheOrigin()
        {
            yield return BootApp(presenting: false);
            Assert.IsFalse(App.IsPresenting, "the desktop is not an XR session");
            Assert.That(Layout.XrArrangements, Is.Zero, "Layout never arranges on the desktop");
            var s = Find("all-origin");
            AssertRigs(s.After, "desktop room");
            Assert.That(Vector3.Distance(Head.position, DesktopRigFallback.EyePosition), Is.LessThan(1e-4f));
            Assert.That(Probe.Rigs[GoldenLayout.Piano].Group.position.z - Head.position.z, Is.EqualTo(0.55f).Within(1e-3f), "the piano 0.55 m ahead of the camera, as in the golden layout");
            Vector3 menuHome = LayoutMath.SeatPlace(OriginSeat, 0f, MenuConstants.HomeSeat);
            Assert.That(Vector3.Distance(Probe.Menu.Root.position, menuHome), Is.LessThan(1e-4f), "the menu's home about ORIGIN");
            Assert.That(Layout.Seat.Head, Is.EqualTo(OriginSeat));
            Vector3 piano0 = Probe.Rigs[GoldenLayout.Piano].Group.position;
            yield return Frames(10);
            Assert.That(Probe.Rigs[GoldenLayout.Piano].Group.position, Is.EqualTo(piano0), "nothing re-arranges later on the desktop");
            Assert.That(Layout.XrArrangements, Is.Zero);
            // The hand gestures are XR-only: a still pinch on a handle is no grab here.
            yield return PinchHold(HandSide.Right, Handle(GoldenLayout.Piano));
            Assert.IsNull(HeldAfter, "no hand grab while not presenting");
        }

        [UnityTest]
        public IEnumerator ASingleLayoutRestoredOnTheDesktopIsArrangedAboutTheCamera()
        {
            yield return BootApp(presenting: false, settings: st =>
            {
                st.SetString(AppSettings.KeyLayoutMode, "single");
                st.SetString(AppSettings.KeyCurrentInstrument, "drums");
            });
            Assert.That(Layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Drums));
            var s = Find("single-drums-desktop");
            // The golden was recorded with the reference room's front wall 1.12 m ahead, which slid the kit sideways. The room
            // faces the fire now (RoomFrame): nothing ahead within 4 m, so the kit stays centred where the fixed run put it
            // before its wall shift (FixedAfter carried back by FixedShift); the other rigs are the golden's.
            var want = (GoldenLayout.RigPose[])s.After.Clone();
            GoldenLayout.RigPose kit = s.FixedAfter[GoldenLayout.Drums];
            kit.Pos -= new Vector3(s.FixedShift.x, 0f, s.FixedShift.y);
            want[GoldenLayout.Drums] = kit;
            AssertRigs(want, "single drums about the desktop camera (Set on the desktop: no fitSeat; no wall ahead)");
            Assert.That(Layout.LastSeatShift, Is.EqualTo(Vector3.zero), "not presenting: the seat never moves");
            Assert.That(Layout.XrArrangements, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ALateXrStartArrangesAroundTheRealHead()
        {
            // The editor's mouse hand first (the desktop), then the headset's hands come up: App.IsPresenting turns true,
            // as when the runtime starts after RigBootstrap's desktop fallback.
            yield return BootApp(presenting: true, mouseHands: true);
            Assert.IsFalse(App.IsPresenting, "the mouse hand: not presenting yet");
            Assert.That(Layout.XrArrangements, Is.Zero);
            var s = Find("all-origin");
            AssertRigs(s.After, "desktop room before XR");
            // The reference seat 1.2 m toward the fireplace (room frame (0.4, -1.2), yaw 30), turned with the room (RoomFrame).
            Vector3 head = RoomFrame.ToWorld(new Vector3(0.4f, 1.2f, -1.2f));
            const float yaw = 30f + RoomFrame.Yaw;
            SeatAt(head, yaw, arrange: false);
            yield return Frames(3);
            Assert.That(Layout.XrArrangements, Is.Zero, "moving the camera alone arranges nothing");
            int arranged = 0;
            App.Events.Subscribe<ArrangedEvent>(e => { if (e.Forced) arranged++; });
            HandInput.OverrideSource = Fake;   // the session starts
            yield return Frame();
            Assert.IsTrue(App.IsPresenting);
            Assert.That(Layout.XrArrangements, Is.EqualTo(1), "the first presenting frame arranges");
            Assert.That(arranged, Is.EqualTo(1));
            Assert.That(Vector3.Distance(Head.position, head), Is.LessThan(1e-4f), "the full room fits there: no seat move");
            AssertRigs(s.After, "re-laid about the real head", head, yaw);
            Assert.That(Layout.Seat.Head, Is.EqualTo(Head.position));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(Layout.Seat.YawDeg, yaw)), Is.LessThan(0.01f));
            yield return Frames(5);
            Assert.That(Layout.XrArrangements, Is.EqualTo(1), "once per session");
            // The session ends and a new one starts: arranged again about the head as it is then.
            HandInput.OverrideSource = null;
            yield return Frames(2);
            Assert.IsFalse(App.IsPresenting);
            SeatAt(OriginSeat, 0f, arrange: false);
            HandInput.OverrideSource = Fake;
            yield return Frame();
            Assert.That(Layout.XrArrangements, Is.EqualTo(2), "every session start arranges (session start sets vrAuto)");
            AssertRigs(Find("all-origin").FixedAfter, "about the head at ORIGIN now");
        }
    }
}
