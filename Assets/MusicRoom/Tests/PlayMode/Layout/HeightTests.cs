// The player's level (2026-11-11 headset test in "Play in my room", no Space Setup: "i was in front of that instrument and
// when off my meta and came back that instrument become low. and then later i was unable to bring that instrument to my
// level"), with the Layout module and the reference rigs (scripted hands, manual frames at 72 Hz, a simulated headset):
//   - the remount as the headset log shows it: the system menu takes the hands, the headset comes off, tracking is lost,
//     the system's floor estimate moves under the player (their eyes read 12 cm higher) and the space is recentred, with
//     recentre events in a burst while the headset is off and untracked. Nothing is laid out about that stale pose; once
//     the headset is back, worn, tracked and still, the burst is applied ONCE about the new origin and every instrument is
//     where it was for the player (the same height below the eyes; the kit and the harp on risers);
//   - standing up while it was off: a new posture, the lift for the eyes now (the keys at a standing player's 1.02 m);
//   - a trip to the system menu that changes nothing moves nothing;
//   - Reset view (the menu's row) at standing eyes lifts them, at seated eyes brings them back to their homes, and forgets
//     the height tweaks of the layout while keeping their spots.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class HeightTests : LayoutPlayTestBase
    {
        static readonly int[] Instruments = { GoldenLayout.Piano, GoldenLayout.Drums, GoldenLayout.Harp };
        readonly Vector3[] _pos = new Vector3[4];

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return BootApp();
            Fake.SetTracked(HandSide.Left, false);
            Fake.SetTracked(HandSide.Right, false);
            yield return Frames(100);   // the eyes' height is measured while playing
            Assert.That(Layout.EyeHeight, Is.EqualTo(Head.position.y).Within(0.01f), "the eyes measured");
            Assert.That(Layout.PlayerLift, Is.EqualTo(0f), "a seated player: the homes");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (App != null && App.Rig != null) App.Rig.HeadTracked = true;
            yield return TearDownApp();
        }

        void Snap() { for (int i = 0; i < 4; i++) _pos[i] = Probe.Rigs[i].Group.position; }

        void AssertUnmoved(string why)
        {
            for (int i = 0; i < 4; i++)
                Assert.That(Vector3.Distance(Probe.Rigs[i].Group.position, _pos[i]), Is.LessThan(1e-4f), GoldenLayout.RigNames[i] + " moved: " + why);
        }

        float BelowEyes(int rig) => Head.position.y - Probe.Rigs[rig].Group.position.y;

        IEnumerator Away()
        {
            App.Events.Publish(new InputFocusEvent { Focused = false });
            App.Events.Publish(new HmdMountEvent { Mounted = false });
            App.Rig.HeadTracked = false;
            yield return Frames(5);
            Assert.IsTrue(Layout.AwayOrSettling, "away");
        }

        IEnumerator BackOn()
        {
            App.Events.Publish(new HmdMountEvent { Mounted = true });
            App.Events.Publish(new InputFocusEvent { Focused = true });
            App.Rig.HeadTracked = true;
            yield return Frames(Mathf.CeilToInt(HeightConstants.ReturnSettle / DT) + 4);
            Assert.IsFalse(Layout.AwayOrSettling, "back and settled");
        }

        [UnityTest]
        public IEnumerator ARemountKeepsThemAtThePlayersLevelAndLaysThemOutOnce()
        {
            var below = new float[4];
            foreach (int n in Instruments) below[n] = BelowEyes(n);
            int backs = Layout.BringBacks, returns = Layout.Returns, deferred = Layout.DeferredRecentres;
            yield return Away();

            // While it is off the system re-estimates its floor (the eyes read 12 cm higher) and recentres the space 30
            // degrees round and 0.5 m away; its recentre events come in a burst while the head is off and untracked.
            Transform rig = App.Rig.RigRoot;
            rig.SetPositionAndRotation(rig.position + new Vector3(0.4f, 0.12f, 0.3f), Quaternion.Euler(0f, 30f, 0f));
            Snap();
            App.Events.Publish(new RecenteredEvent());
            AssertUnmoved("a recentre with the headset off");
            Assert.That(Layout.BringBacks, Is.EqualTo(backs), "held back");
            App.Events.Publish(new HmdMountEvent { Mounted = true });
            App.Events.Publish(new InputFocusEvent { Focused = true });
            yield return Frames(30);   // on, but tracking not back yet
            App.Events.Publish(new RecenteredEvent());
            AssertUnmoved("a recentre with the head untracked");
            Assert.That(Layout.BringBacks, Is.EqualTo(backs));
            Assert.That(Layout.DeferredRecentres, Is.EqualTo(deferred + 2));
            Assert.IsTrue(Layout.AwayOrSettling);

            App.Rig.HeadTracked = true;
            yield return Frames(Mathf.CeilToInt(HeightConstants.ReturnSettle / DT) + 4);
            Assert.That(Layout.Returns, Is.EqualTo(returns + 1));
            Assert.That(Layout.LastReturnEyeShift, Is.EqualTo(0.12f).Within(0.005f), "the eyes read 12 cm higher");
            Assert.That(Layout.PlayerLift, Is.EqualTo(0.12f).Within(0.005f), "the instruments follow the player's level");
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 1), "the burst laid them out once");
            Assert.That(Layout.LastBringBack, Is.EqualTo(BringBackSource.Recentre));
            foreach (int n in Instruments)
            {
                Rig r = Probe.Rigs[n];
                Debug.Log($"[Height] after the remount: {r.Id} {BelowEyes(n):F3} m below the eyes (was {below[n]:F3}), riser {Layout.RaiseOf(r):F3}");
                Assert.That(BelowEyes(n), Is.EqualTo(below[n]).Within(0.01f), r.Id + ": at the player's level as before");
                Assert.That(Vector3.Angle(r.Group.up, Vector3.up), Is.LessThan(0.01f));
                if (n == GoldenLayout.Piano) Assert.That(Layout.RaiseOf(r), Is.EqualTo(0f), "the piano's stand stretches");
                else Assert.That(Layout.RaiseOf(r), Is.EqualTo(0.12f).Within(0.006f), r.Id + ": on a riser");
            }
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(piano.eulerAngles.y, 30f)), Is.LessThan(0.05f), "in front of the new origin");

            // The burst's tail, now that the head is back: the same place (the same origin), nothing new.
            Snap();
            App.Events.Publish(new RecenteredEvent());
            Assert.That(Layout.BringBacks, Is.EqualTo(backs + 2));
            for (int i = 0; i < 3; i++)
                Assert.That(Vector3.Distance(Probe.Rigs[Instruments[i]].Group.position, _pos[Instruments[i]]), Is.LessThan(0.002f), "the tail lands in the same place");
            yield return Frames(3);
        }

        [UnityTest]
        public IEnumerator StandingUpWhileItWasOffRaisesThemOnRisers()
        {
            yield return Away();
            Transform rig = App.Rig.RigRoot;
            rig.position += new Vector3(0f, 0.40f, 0f);   // the player came back standing: eyes 1.60 m
            Snap();
            yield return BackOn();
            Assert.That(Layout.LastReturnEyeShift, Is.EqualTo(0.40f).Within(0.005f));
            Assert.That(Layout.PlayerLift, Is.EqualTo(LayoutModule.EyeLift(Head.position.y)).Within(0.005f), "a new posture: the lift for the eyes now");
            Assert.That(Layout.PlayerLift, Is.EqualTo(0.30f).Within(0.005f));
            Transform piano = Probe.Rigs[GoldenLayout.Piano].Group;
            Assert.That(piano.position.y, Is.EqualTo(0.72f + 0.30f).Within(0.006f), "the keys at a standing player's elbows");
            foreach (int n in Instruments)
            {
                Rig r = Probe.Rigs[n];
                Vector3 p = r.Group.position;
                Assert.That(new Vector2(p.x - _pos[n].x, p.z - _pos[n].z).magnitude, Is.LessThan(1e-4f), r.Id + ": only up, never across");
                if (n != GoldenLayout.Piano) Assert.That(Layout.RaiseOf(r), Is.EqualTo(0.30f).Within(0.006f), r.Id + ": on a riser");
            }
        }

        [UnityTest]
        public IEnumerator ATripToTheSystemMenuMovesNothing()
        {
            Snap();
            int returns = Layout.Returns, backs = Layout.BringBacks;
            App.Events.Publish(new InputFocusEvent { Focused = false });
            yield return Frames(30);
            App.Events.Publish(new InputFocusEvent { Focused = true });
            yield return Frames(Mathf.CeilToInt(HeightConstants.ReturnSettle / DT) + 4);
            Assert.That(Layout.Returns, Is.EqualTo(returns + 1));
            Assert.That(Layout.LastReturnLiftShift, Is.EqualTo(0f));
            Assert.That(Layout.BringBacks, Is.EqualTo(backs));
            AssertUnmoved("the system menu and back");
        }

        [UnityTest]
        public IEnumerator ResetViewBringsThemToTheEyesNowAndForgetsHeightTweaks()
        {
            // The drums lifted 15 cm and slid 20 cm to the side by hand.
            Layout.BeginArrange();
            Rig drums = Probe.Rigs[GoldenLayout.Drums];
            Vector3 at = Handle(GoldenLayout.Drums), to = at + Head.right * 0.2f + Vector3.up * 0.17f;
            Fake.SetTracked(HandSide.Right, true);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.04f));
            yield return Frame();
            for (int i = 0; i < 8; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at)); yield return Frame(); }
            Assert.That(Layout.HeldRig, Is.SameAs(drums));
            for (int i = 1; i <= 24; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, Vector3.Lerp(at, to, i / 24f))); yield return Frame(); }
            for (int i = 0; i < 24; i++) { Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, to)); yield return Frame(); }
            yield return ReleaseHands(to + Vector3.up * 0.3f);
            yield return Frames(40);
            Layout.EndArrange();
            Assert.That(Layout.TryGetOffset(RigId.Drums, out ComfortOffset tweak), Is.True);
            Assert.That(tweak.Height, Is.EqualTo(0.15f).Within(0.01f), "the lift kept");
            Assert.That(Mathf.Abs(tweak.Side) + Mathf.Abs(tweak.Distance), Is.GreaterThan(0.1f), "and the spot");
            Vector3 spot = drums.Group.position;

            // Reset view standing (eyes 1.60 m): every instrument at a standing player's level, the height tweak forgotten,
            // the spot kept.
            Transform rig = App.Rig.RigRoot;
            rig.position += new Vector3(0f, 0.40f, 0f);
            ((ILayoutService)Layout).BringToPlayer();
            Assert.That(Layout.PlayerLift, Is.EqualTo(0.30f).Within(0.005f));
            Assert.That(Layout.TryGetOffset(RigId.Drums, out tweak), Is.True, "the spot is still a tweak");
            Assert.That(tweak.Height, Is.EqualTo(0f), "the height tweak forgotten");
            Assert.That(drums.Group.position.y, Is.EqualTo(0.30f).Within(0.006f), "the kit at a standing level, not 15 cm above it");
            Assert.That(new Vector2(drums.Group.position.x - spot.x, drums.Group.position.z - spot.z).magnitude, Is.LessThan(0.02f), "at the player's spot");
            Assert.That(Probe.Rigs[GoldenLayout.Piano].Group.position.y, Is.EqualTo(1.02f).Within(0.006f));
            yield return Frames(2);
            Assert.That(Layout.RaiseOf(drums), Is.EqualTo(0.30f).Within(0.006f), "on its riser");

            // Seated again (eyes 1.20 m), Reset view: the homes.
            rig.position -= new Vector3(0f, 0.40f, 0f);
            ((ILayoutService)Layout).BringToPlayer();
            Assert.That(Layout.PlayerLift, Is.EqualTo(0f));
            Assert.That(drums.Group.position.y, Is.EqualTo(0f).Within(1e-4f), "the kit back on the floor");
            Assert.That(Probe.Rigs[GoldenLayout.Piano].Group.position.y, Is.EqualTo(0.72f).Within(1e-4f), "the keys at their seated home");
            yield return Frames(2);
            Assert.That(Layout.Risers.ShownCount, Is.EqualTo(0), "no riser on the floor");
        }
    }
}
