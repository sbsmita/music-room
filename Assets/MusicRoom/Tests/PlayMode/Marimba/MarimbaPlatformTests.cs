// The real marimba in the platform's Layout (phase B): the five shared tests that use the marimba as a phase-0 stub
// (SetLayoutTests x2, RaySummonTests, DisplayArrangeTests, FastMotionDriverTests: CONTRACT-REQUESTS RB3) re-checked here
// on MarimbaInstrument. On display at its spot, stowed and not played; from the default room the menu says No room and the
// ray or a desktop click brings it alone (the piano wants the front; the kit's cymbals stand in its hands' field); beside
// the harp the menu seats it at Front with nothing overlapping; it slides on the floor on display and the spot is saved;
// its live strokes ask for Fast Motion Mode, a played-back one does not.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Marimba;
using MusicRoom.Tests.PlayMode;
using MusicRoom.Tests.PlayMode.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Marimba
{
    public class MarimbaPlatformTests : LayoutPlayTestBase
    {
        const HandSide Side = HandSide.Right;

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        MarimbaInstrument Marimba()
        {
            foreach (IStowable s in App.GetAll<IStowable>()) if (s.Kind == InstrumentKind.Marimba) return s as MarimbaInstrument;
            return null;
        }

        [UnityTest]
        public IEnumerator OnDisplayAtItsSpot_StowedAndNotPlayed()
        {
            yield return BootApp(withStubs: true);
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }));
            MarimbaInstrument m = Marimba();
            Assert.That(m, Is.Not.Null, "the real marimba loaded (not the stub)");
            Assert.That(Layout.PlaceOf(InstrumentKind.Marimba), Is.EqualTo(InstrumentPlace.Display));
            Assert.IsTrue(m.gameObject.activeInHierarchy, "the rig stays active in the VR room");
            Assert.IsFalse(m.Rig.InScene);
            Assert.IsFalse(m.IsPresent);
            Assert.IsTrue(m.Stowed);
            Assert.IsTrue(m.View.Stowed, "the display version of the model");
            StowSpec spot = App.Registry.Get(InstrumentKind.Marimba).Stow;
            Assert.That(spot.Pose, Is.EqualTo(new Vector3(2.30f, 0f, -0.80f)));
            Assert.That(Vector3.Distance(m.transform.position, RoomFrame.ToWorld(spot.Pose)), Is.LessThan(1e-4f), "at " + spot.SpotId + " (room frame, turned)");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(m.transform.eulerAngles.y, spot.Yaw + RoomFrame.Yaw)), Is.LessThan(0.01f));
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Marimba, out Vector3 pick));
            Assert.That(Layout.SlotOf(InstrumentKind.Marimba), Is.Null);
            // the displayed frame stays inside the room: its back (rig-local z +0.623 at yaw 90 = world +x) short of the
            // window wall (x 3.20), its ends (local x +-0.624 = world z) clear of the hand drums' spot
            var b = new Bounds(m.transform.position, Vector3.zero);
            foreach (Renderer r in m.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Debug.Log($"[MARIMBA] on display: bounds x {b.min.x:F3}..{b.max.x:F3} y {b.min.y:F3}..{b.max.y:F3} z {b.min.z:F3}..{b.max.z:F3}; pick {pick.ToString("F2")}");
            // the window wall is the world's left (x -3.20) now that the room faces the fire
            Assert.That(b.min.x, Is.GreaterThan(-3.20f + 0.05f), "clear of the window wall");
            yield return Frames(10);
            Assert.That(m.Controller.LiveStrikes, Is.Zero, "a displayed instrument is never played");
            Assert.That(Layout.Placement.Overlaps2D(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator FromTheDefaultRoomTheMenuSaysNoRoomAndTheRayBringsItAlone()
        {
            yield return BootApp(withStubs: true);
            JoinPreview p = Layout.PreviewJoin(InstrumentKind.Marimba);
            Debug.Log("[MARIMBA] menu join preview from the default set: " + p);
            Assert.That(p.Action, Is.EqualTo(JoinAction.Alone), "it fits beside none of them: the piano wants the front, the kit's cymbals stand in the marimba's hands' field");
            Assert.IsFalse(Layout.Bring(InstrumentKind.Marimba, BringReason.Menu), "the menu says: No room");
            Assert.That(Layout.MySet.Count, Is.EqualTo(3));
            Assert.IsTrue(Layout.Bring(InstrumentKind.Marimba, BringReason.Ray));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Marimba }), "it comes alone");
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) yield return Frame();
            Assert.IsFalse(Layout.AnyMoving);
            MarimbaInstrument m = Marimba();
            Vector3 single = LayoutMath.SeatPlace(Layout.Seat.Head, Layout.Seat.YawDeg * Mathf.Deg2Rad, App.Registry.Get(InstrumentKind.Marimba).Seat.Single);
            Assert.That(Vector3.Distance(m.transform.position, single), Is.LessThan(0.02f), "a set of one: its Single home");
            Assert.IsTrue(m.IsPresent);
            Assert.IsFalse(m.View.Stowed, "the full model at the seat");
            Assert.That(Layout.Placement.Overlaps3D(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator BesideTheHarpTheMenuSeatsItAtFront()
        {
            yield return BootApp(withStubs: true, settings: st => st.SetString(AppSettings.KeySet, "harp"));
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Harp }));
            JoinPreview p = Layout.PreviewJoin(InstrumentKind.Marimba);
            Assert.That(p.Action, Is.EqualTo(JoinAction.Add), "the marimba fits beside the harp: " + p);
            Assert.IsTrue(Layout.Bring(InstrumentKind.Marimba, BringReason.Menu));
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) yield return Frame();
            Assert.IsFalse(Layout.AnyMoving);
            Assert.That(Layout.MySet, Is.EquivalentTo(new[] { InstrumentKind.Marimba, InstrumentKind.Harp }));
            Assert.That(Layout.SlotOf(InstrumentKind.Marimba), Is.EqualTo(SeatSlot.Front));
            Assert.That(Layout.SlotOf(InstrumentKind.Harp), Is.EqualTo(SeatSlot.Left));
            Assert.That(Layout.Lead, Is.EqualTo(InstrumentKind.Marimba), "the lead: the member at Front");
            MarimbaInstrument m = Marimba();
            Assert.IsTrue(m.IsPresent);
            Assert.IsFalse(m.Stowed);
            Vector3 home = LayoutMath.SeatPlace(Layout.Seat.Head, Layout.Seat.YawDeg * Mathf.Deg2Rad, App.Registry.Get(InstrumentKind.Marimba).Seat.Home);
            Debug.Log($"[MARIMBA] beside the harp: {m.transform.position.ToString("F3")} (home {home.ToString("F3")})");
            Assert.That(Vector3.Distance(m.transform.position, home), Is.LessThan(0.02f), "at its home");
            int o2 = Layout.Placement.Overlaps2D(), o3 = Layout.Placement.Overlaps3D();
            Debug.Log($"[MARIMBA] with the harp: {o2} floor overlaps, {o3} 3D overlaps; " + Touching());
            Assert.That(o3, Is.Zero, "no rig's parts inside another's");
            Assert.That(o2, Is.Zero, "the floor footprints apart");
        }

        /// <summary>The whole app (every module: the real piano, kit and harp, not the layout tests' golden probes): from the
        /// default room the menu cannot bring it (Alone); beside the real harp it comes to Front and no part of either is
        /// inside the other's.</summary>
        [UnityTest]
        public IEnumerator InTheWholeAppItSitsClearOfTheRealHarp()
        {
            Boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            Boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            Fake = Boot.Hands;
            yield return Boot.Start();
            App = Boot.App;
            Layout = App.Get<ILayoutService>() as LayoutModule;
            float t0 = Time.realtimeSinceStartup;
            while ((App.Rig.State == XrState.Starting || !Layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 10f) yield return Frame();
            yield return Frames(5);
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }));
            Assert.That(Layout.Placement.Overlaps3D(), Is.Zero, "the default room");
            Assert.That(Layout.PreviewJoin(InstrumentKind.Marimba).Action, Is.EqualTo(JoinAction.Alone));
            Assert.IsTrue(Layout.PutAway(InstrumentKind.Piano), "the piano goes back");
            Assert.IsTrue(Layout.PutAway(InstrumentKind.Drums), "the kit goes back");
            for (int i = 0; i < 300 && Layout.AnyMoving; i++) yield return Frame();
            Assert.That(Layout.PreviewJoin(InstrumentKind.Marimba).Action, Is.EqualTo(JoinAction.Add), "beside the harp");
            Assert.IsTrue(Layout.Bring(InstrumentKind.Marimba, BringReason.Menu));
            for (int i = 0; i < 300 && Layout.AnyMoving; i++) yield return Frame();
            yield return Frames(5);
            Assert.That(Layout.SlotOf(InstrumentKind.Marimba), Is.EqualTo(SeatSlot.Front));
            int o2 = Layout.Placement.Overlaps2D(), o3 = Layout.Placement.Overlaps3D();
            Debug.Log($"[MARIMBA] whole app, marimba + harp: {o2} floor overlaps, {o3} 3D overlaps; " + Touching());
            Assert.That(o3, Is.Zero, "no part of the marimba inside the harp");
            Assert.That(o2, Is.Zero);
        }

        /// <summary>Which in-scene rigs' floor rectangles touch, by how much, and the rectangles' world extents.</summary>
        string Touching()
        {
            var es = Layout.Placement.Entries;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < es.Count; i++)
                for (int j = i + 1; j < es.Count; j++)
                {
                    RigEntry a = es[i], b = es[j];
                    if (!PlacementSolver.Active(a) || !PlacementSolver.Active(b)) continue;
                    if (!PlacementSolver.Touch(a, b, 0f, out float depth, out _, out _)) continue;
                    sb.Append($"{a.Name} x {b.Name} by {depth * 100:F1} cm; ");
                    for (int p = 0; p < a.Local.Count; p++)
                        for (int q = 0; q < b.Local.Count; q++)
                        {
                            Obb wa = a.Local[p].Moved(a.M), wb = b.Local[q].Moved(b.M);
                            if (!Obb.Intersect(wa, wb)) continue;
                            sb.Append($"3D: {a.Name} box {p} c {wa.C.ToString("F3")} h {wa.H.ToString("F3")} meets {b.Name} box {q} c {wb.C.ToString("F3")} h {wb.H.ToString("F3")}; ");
                        }
                    foreach (RigEntry e in new[] { a, b })
                        for (int k = 0; k < e.Rects.Count; k++)
                        {
                            WorldRect w = e.WR[k];
                            sb.Append($"[{e.Name} rect {k} at ({w.X:F3}, {w.Z:F3}) half {w.Hu:F3} x {w.Hv:F3}] ");
                        }
                }
            return sb.Length == 0 ? "none touch" : sb.ToString();
        }

        [UnityTest]
        public IEnumerator ADesktopClickOnTheDisplayedMarimbaBringsIt()
        {
            yield return BootApp(withStubs: true, presenting: false);
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Marimba, out Vector3 at));
            Vector3 eye = Head.position;
            var ray = new Ray(eye, (at - eye).normalized);
            Layout.DesktopStep(new DesktopMouse { Ray = ray, Pressed = true, Held = true });
            Assert.That(Layout.DesktopSummons, Is.EqualTo(1));
            Assert.IsTrue(Layout.IsInScene(InstrumentKind.Marimba), "the marimba comes (alone: it fits beside none of the three)");
            Assert.That(Layout.MySet, Is.EqualTo(new[] { InstrumentKind.Marimba }));
            Layout.DesktopStep(new DesktopMouse { Ray = ray, Held = false });
            yield return null;
        }

        // A raised hand, above every instrument at the seat: since the room faces the fire (RoomFrame) the window's displays
        // are on the left, and a ray from shoulder height toward them passes the harp's crest (1.48 m) at the seat.
        Vector3 HandPoint => Head.position + new Vector3(0.25f, 0.9f, 0.1f);

        IEnumerator Point(Vector3 target, bool pinch, int frames)
        {
            Vector3 o = HandPoint;
            for (int i = 0; i < frames; i++)
            {
                Fake.SetPointer(Side, new Pose(o, Quaternion.LookRotation(target - o)));
                Fake.SetPose(Side, pinch ? PinchAt(Side, o) : OpenAt(Side, o));
                yield return Frame();
            }
        }

        [UnityTest]
        public IEnumerator OnDisplayItSlidesOnTheFloorAndTheSpotIsSaved()
        {
            yield return BootApp(withStubs: true);
            Layout.BeginArrange();
            yield return Frame();
            MarimbaInstrument m = Marimba();
            Transform t = m.transform;
            Vector3 home = t.position;
            Assert.IsTrue(Layout.Arrangeable(m.Rig), "a displayed instrument is arrangeable in the VR room");
            Assert.IsTrue(Layout.TryGetDisplayPoint(InstrumentKind.Marimba, out Vector3 at));
            Vector3 to = at + RoomFrame.DirToWorld(new Vector3(-0.15f, 0f, 0.35f));   // a room-frame direction, turned (RoomFrame)
            yield return Point(at, false, 3);
            yield return Point(at, true, 10);
            Assert.That(Layout.DisplayHeld, Is.SameAs(m.Rig), "taken by the ray");
            Vector3 o = HandPoint;
            for (int i = 1; i <= 30; i++)
            {
                Vector3 aim = Vector3.Lerp(at, to, i / 30f);
                Fake.SetPointer(Side, new Pose(o, Quaternion.LookRotation((aim - o).normalized)));
                Fake.SetPose(Side, PinchAt(Side, o));
                yield return Frame();
            }
            yield return Point(to, false, 2);
            Assert.That(Layout.DisplayHeld, Is.Null, "let go");
            Assert.That(t.position.y, Is.EqualTo(home.y).Within(1e-5f), "it slides on the floor");
            Assert.That(Vector3.Distance(t.position, home), Is.GreaterThan(0.1f), "it moved");
            string stow = App.Settings.GetStow("marimba");
            Assert.IsTrue(LayoutModule.TryParseStow(stow, out double rx, out double rz, out _), "musicroom.stow.marimba = " + stow);
            RoomFrame.ToWorld(rx, rz, out double x, out double z);   // saved in room coordinates
            Assert.That((float)x, Is.EqualTo(t.position.x).Within(1e-3f));
            Assert.That((float)z, Is.EqualTo(t.position.z).Within(1e-3f));
            Assert.IsTrue(m.Stowed, "still on display");
            Layout.BeginArrange();
            Layout.ResetArrangement();
            Layout.EndArrange();
            Assert.That(Vector3.Distance(t.position, home), Is.LessThan(1e-4f), "Reset: the plan's spot");
        }

        [UnityTest]
        public IEnumerator ItsLiveStrokesAskForFastMotion_APlayedBackOneDoesNot()
        {
            double audio = 20.0;
            Boot = new TestBoot(simulateHeadset: true, manualFrames: true, audioClock: () => audio);
            Boot.Settings.SetString(AppSettings.KeySet, "harp,marimba");
            yield return Boot.Start();
            App = Boot.App;
            void Run(int n) { for (int i = 0; i < n; i++) { audio += DT; Boot.Step(DT); } }
            Run(3);
            InstrumentDescriptor d = App.Registry.Get(InstrumentKind.Marimba);
            Assert.That(d.Has(InstrumentTraits.FastMotion), Is.True);
            Assert.That(d.Events.First(e => e.Role == EventRole.Onset).Type, Is.EqualTo(MarimbaEvents.Strike));
            FastMotionPolicy policy = App.FastMotion.Policy;
            Assert.IsFalse(policy.On);
            MarimbaInstrument m = null;
            foreach (IInstrument i in App.Instruments) if (i is MarimbaInstrument mi) m = mi;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.IsPresent, Is.True, "at the seat (My set)");

            var events = new List<RecorderEvent>();
            App.Events.Subscribe<RecorderEvent>(e => events.Add(e));
            var c = new FixtureContext { Boot = Boot, Frame = () => Run(1) };
            m.TryGetLanding(new LessonTarget { i = 16, midi = 64, note = "E4" }, out Pose landing, out _, out _);
            Vector3 up = landing.rotation * Vector3.up;
            int live = m.Controller.LiveStrikes;
            c.Index(landing.position + up * 0.05f); Run(3);
            c.Index(landing.position - up * 0.005f); Run(1);
            Assert.That(m.Controller.LiveStrikes, Is.EqualTo(live + 1), "a fingertip strike on E4");
            Assert.That(events.Any(e => e.Event.type == MarimbaEvents.Strike && !e.DuringPlayback), Is.True, "its live onset recorded");
            Run(1);
            Assert.IsTrue(policy.On, "on at once, like a drum hit");
            c.Index(landing.position + up * 0.3f);
            Run((int)(9.2f * 90));
            Assert.IsFalse(policy.On, "off after the recent-hit window and 5 quiet seconds");

            var recorder = App.Get<IRecorder>();
            TakeEvent ev = MarimbaEvents.StrikeEvent(16, 0.7f, 0.45f, 0f, MarimbaMallet.Medium);
            ev.t = 0.05f;
            var take = new List<TakeEvent> { ev };
            int strikes = m.Controller.LiveStrikes;
            Assert.That(recorder.PlayEvents(take, 0.5), Is.True);
            Run(90);
            Assert.That(m.Controller.LiveStrikes, Is.EqualTo(strikes), "playback is not a live strike");
            Assert.IsFalse(policy.On, "a played-back marimba onset does not ask for Fast Motion Mode");
            recorder.Stop();
        }
    }
}
