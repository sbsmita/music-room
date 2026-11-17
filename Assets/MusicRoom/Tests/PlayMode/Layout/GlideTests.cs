// The glide (platform.md 4.5, 12.1 GlideTests). The durations (0.9 s + 0.1 s per metre beyond 2 m, at most 1.4 s); the
// path from every display spot to every seat slot and back stays 0.6 m or more from the head (sampled every 2 cm) and its
// arc at 0.75 m or more; while gliding an instrument is not present (no tick, no zone, PlaceOf Gliding); the
// InstrumentMoveEvent order (the outgoing one Leaving first, the incoming one 0.15 s later, then Displayed and Arrived);
// one swish per bring; a lesson waits for Arrived and is not stopped while its instrument glides in.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class GlideTests : LayoutPlayTestBase
    {
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        [Test]
        public void DurationsAreNineTenthsPlusATenthPerMetreAtMostOnePointFour()
        {
            Assert.That(RigGlide.DurationFor(0.5f), Is.EqualTo(0.9f));
            Assert.That(RigGlide.DurationFor(2f), Is.EqualTo(0.9f));
            Assert.That(RigGlide.DurationFor(3f), Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(RigGlide.DurationFor(6f), Is.EqualTo(1.3f).Within(1e-6f));
            Assert.That(RigGlide.DurationFor(20f), Is.EqualTo(1.4f), "at most 1.4 s");
        }

        [Test]
        public void EveryPathFromEverySpotToEverySlotKeepsClearOfTheHead()
        {
            InstrumentRegistry reg = InstrumentRegistry.Discover();
            SeatPlanner planner = SeatPlanner.FromRegistry(reg);
            var head = new Vector3(0f, 1.2f, 0f);
            var g = new RigGlide();
            var rig = new Rig { Id = RigId.Handpan };
            int paths = 0;
            float closest = float.PositiveInfinity, longest = 0f, slowest = 0f;
            foreach (InstrumentDescriptor d in reg.All)
            {
                StowPlan.Spot spot = StowPlan.Spot.From(d);
                SeatPlanner.Inst inst = planner.Get(d.Kind);
                var display = new Vector3((float)spot.X, (float)spot.Y, (float)spot.Z);
                for (int s = 0; s < SeatSlots.Count; s++)
                {
                    var slot = (SeatSlot)s;
                    double turn = SeatSlots.Heading(slot) - SeatSlots.Heading(inst.Canonical);
                    SeatPlanner.Pose2 p = SeatPlanner.RotatePose(inst.Home, turn);
                    var seat = new Vector3((float)p.X, d.Seat.Home.y, (float)p.Z);
                    for (int dir = 0; dir < 2; dir++)
                    {
                        Vector3 from = dir == 0 ? display : seat, to = dir == 0 ? seat : display;
                        float yf = dir == 0 ? (float)spot.Yaw : (float)p.Yaw, yt = dir == 0 ? (float)p.Yaw : (float)spot.Yaw;
                        g.Begin(rig, d.Kind, RigGlide.Goal.Seat, BringReason.Menu, from, yf, to, yt, head, 0f);
                        paths++;
                        string what = $"{d.Id} {(dir == 0 ? "to" : "from")} {slot}";
                        Assert.That(g.Duration, Is.EqualTo(RigGlide.DurationFor(g.Length)), what);
                        Assert.That(g.Duration, Is.InRange(RigGlide.BaseSeconds, RigGlide.MaxSeconds), what);
                        float min = g.MinHeadDistance(head, 0.02f);
                        Assert.That(min, Is.GreaterThanOrEqualTo(RigGlide.HeadClear), what + ": never inside the head's 0.6 m sphere");
                        // the arc itself (everything between the radial legs) is at 0.75 m or more, and inside the walls
                        for (int i = 0; i <= 200; i++)
                        {
                            g.Sample(g.Duration * i / 200f, out Vector3 q, out _, out _);
                            Assert.That(GrabConstants.RoomBox.Contains(new Vector3(q.x, 1f, q.z)), Is.True, what + ": inside the room at " + q.ToString("F2"));
                            float r = new Vector2(q.x - head.x, q.z - head.z).magnitude;
                            float ra = new Vector2(from.x, from.z).magnitude, rb = new Vector2(to.x, to.z).magnitude;
                            Assert.That(r, Is.GreaterThanOrEqualTo(Mathf.Min(RigGlide.MinRadius, Mathf.Min(ra, rb)) - 1e-3f), what + ": never nearer the head than its ends or 0.75 m");
                        }
                        // it starts and ends exactly at its poses
                        g.Sample(0f, out Vector3 a, out float ya, out _);
                        g.Sample(g.Duration, out Vector3 b, out float yb, out _);
                        Assert.That(Vector3.Distance(a, from), Is.LessThan(1e-5f), what);
                        Assert.That(Vector3.Distance(b, to), Is.LessThan(1e-5f), what);
                        Assert.That(Mathf.DeltaAngle(yb, yt), Is.EqualTo(0f).Within(1e-3f), what + " yaw");
                        closest = Mathf.Min(closest, min);
                        longest = Mathf.Max(longest, g.Length);
                        slowest = Mathf.Max(slowest, g.Duration);
                    }
                }
            }
            Debug.Log($"[Layout] glides: {paths} paths, closest to the head {closest:F3} m, longest {longest:F2} m, slowest {slowest:F2} s");
        }

        InstrumentView Stub(InstrumentKind k) => InstrumentView.Of(App, k);

        [UnityTest]
        public IEnumerator WhileGlidingItIsNotPresentAndTheEventsComeInOrder()
        {
            yield return BootApp(withStubs: true, withAudio: true);
            Assert.IsTrue(Layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan }));
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) yield return Frame();
            var moves = new List<KeyValuePair<float, InstrumentMoveEvent>>();
            float t = 0f;
            System.Action<InstrumentMoveEvent> onMove = e => moves.Add(new KeyValuePair<float, InstrumentMoveEvent>(t, e));
            App.Events.Subscribe(onMove);
            var engine = App.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            // full: the bowls replace the handpan (never played, the smaller one)
            Assert.That(Layout.ReplaceCandidate(InstrumentKind.Bowls), Is.EqualTo(InstrumentKind.Handpan));
            InstrumentView bowls = Stub(InstrumentKind.Bowls), handpan = Stub(InstrumentKind.Handpan);
            Assert.IsTrue(Layout.Bring(InstrumentKind.Bowls, BringReason.Ray));
            RigGlide gin = Layout.GlideOf(InstrumentKind.Bowls), gout = Layout.GlideOf(InstrumentKind.Handpan);
            Assert.That(gin, Is.Not.Null, "the bowls glide in");
            Assert.That(gout, Is.Not.Null, "the handpan glides back");
            Assert.That(gin.Delay, Is.EqualTo(RigGlide.OutgoingLead).Within(1e-6f), "the incoming one waits 0.15 s");
            Assert.That(gout.Delay, Is.Zero);
            Assert.That(gin.Duration, Is.EqualTo(RigGlide.DurationFor(gin.Length)));
            RigGlide table = Layout.GlideOf(InstrumentKind.Table);
            Assert.That(table, Is.Not.Null, "the side table goes home from its alternate");
            float total = Mathf.Max(Mathf.Max(gin.Delay + gin.Duration, gout.Delay + gout.Duration), table.Delay + table.Duration);
            int frames = 0, bowlsTicked = 0;
            bool bowlsLanded = false;
            while (Layout.AnyMoving && frames < 150)
            {
                // The bowls' own glide may end before the handpan's or the table's (their paths differ since the room faces the
                // fire, RoomFrame): the rules hold until the bowls land.
                bowlsLanded |= Layout.GlideOf(InstrumentKind.Bowls) == null;
                if (!bowlsLanded)
                {
                    Assert.IsFalse(bowls.IsPresent, "not present while gliding");
                    Assert.IsFalse(bowls.Rig.InScene);
                    Assert.IsFalse(Layout.ZoneOf(bowls.Rig).HasValue, "no zone while gliding");
                    Assert.That(Layout.GrabTarget(bowls.transform.position, 0.2f), Is.Not.SameAs(bowls.Rig), "never a grab target while gliding");
                }
                if (Layout.IsMoving(InstrumentKind.Bowls)) Assert.That(Layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Gliding));
                bowlsTicked += bowls.LiveKnocks;
                t += DT;
                yield return Frame();
                frames++;
            }
            engine.TestTap = null;
            App.Events.Unsubscribe(onMove);
            Assert.That(frames * DT, Is.EqualTo(total).Within(2.5f * DT), "the change takes the glides' time");
            Assert.That(total, Is.LessThanOrEqualTo(RigGlide.OutgoingLead + RigGlide.MaxSeconds + 1e-4f));
            Assert.IsTrue(bowls.IsPresent, "present on arrival");
            Assert.IsTrue(handpan.Stowed && !handpan.IsPresent, "the handpan back on its bracket");
            // the order: handpan Leaving, bowls Leaving 0.15 s later, then the landings
            Assert.That(moves.Count, Is.EqualTo(4), string.Join(", ", moves.ConvertAll(m => m.Value.Kind + " " + m.Value.Phase)));
            Assert.That(moves[0].Value.Kind, Is.EqualTo(InstrumentKind.Handpan));
            Assert.That(moves[0].Value.Phase, Is.EqualTo(MovePhase.Leaving));
            Assert.That(moves[1].Value.Kind, Is.EqualTo(InstrumentKind.Bowls));
            Assert.That(moves[1].Value.Phase, Is.EqualTo(MovePhase.Leaving));
            Assert.That(moves[1].Key - moves[0].Key, Is.EqualTo(RigGlide.OutgoingLead).Within(1.5f * DT), "the incoming one leaves its spot 0.15 s later");
            var handpanDone = moves.Find(m => m.Value.Kind == InstrumentKind.Handpan && m.Value.Phase == MovePhase.Displayed);
            var bowlsDone = moves.Find(m => m.Value.Kind == InstrumentKind.Bowls && m.Value.Phase == MovePhase.Arrived);
            Assert.That(handpanDone.Value.Phase, Is.EqualTo(MovePhase.Displayed));
            Assert.That(bowlsDone.Value.Phase, Is.EqualTo(MovePhase.Arrived));
            foreach (var m in moves) Assert.That(m.Value.Reason, Is.EqualTo(BringReason.Ray));
            Assert.That(tap.Count(SourceId.Ui, SynthOp.StartNoise), Is.EqualTo(2), "one swish (two noise bursts) for the bring");
        }

        [UnityTest]
        public IEnumerator APageTurnGlidesFromTheDisplaySpot()
        {
            yield return BootApp(withStubs: true);
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            int started = 0, ended = 0;
            App.Events.Subscribe<TurnEvent>(e => { if (e.Started) started++; else ended++; });
            Layout.Next(1);   // harp -> bowls
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Bowls));
            Assert.IsTrue(Layout.IsTurning);
            Assert.That(started, Is.EqualTo(1));
            RigGlide g = Layout.GlideOf(InstrumentKind.Bowls);
            Assert.That(g, Is.Not.Null, "it glides (the VR room, a display spot to come from)");
            Assert.That(Vector3.Distance(g.From, RoomFrame.ToWorld(App.Registry.Get(InstrumentKind.Bowls).Stow.Pose)), Is.LessThan(1e-3f), "from its spot on the sill");
            for (int i = 0; i < 150 && Layout.IsTurning; i++) yield return Frame();
            Assert.IsFalse(Layout.IsTurning);
            Assert.That(ended, Is.EqualTo(1), "TurnEvent still says when the page turn ends");
            Assert.IsTrue(Stub(InstrumentKind.Bowls).IsPresent);
            Assert.IsFalse(Probe.Rigs[GoldenLayout.Harp].Group.gameObject.activeSelf, "the probe's harp (no display configuration) is hidden as today");
            // Back to a classic instrument with no display spot: today's 0.35 s slide.
            Layout.Next(-1);
            Assert.That(Layout.GlideOf(InstrumentKind.Harp), Is.Null);
            Assert.IsTrue(Layout.IsTurning);
            yield return Frames(26);
            Assert.IsFalse(Layout.IsTurning, "0.35 s");
            Assert.That(Layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Display), "the bowls went back to the sill");
        }

        [UnityTest]
        public IEnumerator MixedRealitySlidesInAtTheSeat()
        {
            yield return BootApp(withStubs: true);
            Probe.Mr.Active = true;
            App.Events.Publish(new MixedRealityEvent { Active = true });
            Layout.ArrangeAroundPlayer(true);
            foreach (InstrumentKind k in new[] { InstrumentKind.Handpan, InstrumentKind.Bowls })
            {
                Assert.That(Layout.PlaceOf(k), Is.EqualTo(InstrumentPlace.Hidden), k + ": hidden in mixed reality");
                Assert.IsFalse(Stub(k).gameObject.activeSelf);
            }
            Assert.IsTrue(Layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Drums }));
            for (int i = 0; i < 60 && Layout.AnyMoving; i++) yield return Frame();
            Assert.IsTrue(Layout.Bring(InstrumentKind.Bowls, BringReason.Menu));
            RigGlide g = Layout.GlideOf(InstrumentKind.Bowls);
            Assert.That(g, Is.Not.Null);
            Assert.That(g.Mode, Is.EqualTo(RigGlide.Kind.SlideIn), "no display spot to come from: today's slide");
            Assert.That(g.Duration, Is.EqualTo(GrabConstants.TurnSeconds));
            yield return Frames(40);
            Assert.IsTrue(Stub(InstrumentKind.Bowls).IsPresent);
            Probe.Mr.Active = false;
        }
    }

    /// <summary>A lesson waits for its instrument (platform.md 7.3): the real Lessons module, a stub's pack lesson.</summary>
    public class GlideLessonTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;

        [UnityTearDown]
        public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator ALessonWaitsForArrivedAndIsNotStoppedWhileItsInstrumentGlidesIn()
        {
            _boot = new TestBoot(headless: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Current.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Step(Dt);
            LessonsModule lessons = null;
            foreach (IAppModule m in App.Current.Modules) if (m is LessonsModule l) lessons = l;
            Assert.That(lessons, Is.Not.Null);
            var layout = App.Current.Get<ILayoutService>();
            Assert.That(layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Display), "the bowls on the sill");
            Tutorial tut = lessons.Tutorial;
            tut.Start("bowls-first");
            Assert.That(tut.State, Is.EqualTo(TutorialState.Countdown));
            Assert.IsTrue(layout.IsMoving(InstrumentKind.Bowls), "the lesson brought the bowls (Bring, Lesson): they glide");
            Assert.IsTrue(tut.AwaitingArrival, "the countdown waits for Arrived");
            int frames = 0;
            while (layout.IsMoving(InstrumentKind.Bowls) && frames < 150)
            {
                Assert.That(tut.State, Is.EqualTo(TutorialState.Countdown), "not stopped while its instrument glides in");
                Assert.IsTrue(tut.AwaitingArrival);
                Step(Dt);
                frames++;
            }
            Assert.That(layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Seat));
            Assert.IsFalse(tut.AwaitingArrival, "Arrived: the countdown runs");
            Step(3.2f);
            Assert.That(tut.State, Is.Not.EqualTo(TutorialState.Countdown), "the 3 s countdown ran after the arrival");
            Assert.That(tut.State, Is.Not.EqualTo(TutorialState.Idle));
        }

        void Step(float seconds)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < n; i++) { _audio += Dt; _boot.Step(Dt); }
        }
    }
}
