// Every instrument stays seated: on the headset the handpan's shell floated in the air above its stand, which stayed on
// the floor (2026-11-10). Cause: on every Layout "Leaving" while the handpan was at the seat and moving, the module eased
// the shell toward its wall-bracket pose (1.58 m up, upright) as if it were going on display, though the move was a slide
// into the seat (mixed reality, a page turn), a reseat in My set or a slide out; the stand stayed under it on the floor.
// These tests walk every placement path with the real modules (simulated headset) and check, EVERY frame, that a shown
// stand has the shell resting on it, and after each change that each at-seat instrument stands on the floor, upright,
// out of its display configuration:
//   VR room: bring the handpan into My set (glides, the others reseat), sets that reseat it, put away / back, One alone
//   page turns through the carousel, the Move instruments drag and drop, Reset view, a lower and a higher eye height;
//   mixed reality ("Play in my room", the recorded rooms, no table): entering, switching to it alone (the slide that lifted
//   it), page turns, My set with it, Reset view and eye heights, leaving again.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Handpan;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Tests.PlayMode.Menu;
using MusicRoom.Tests.PlayMode.MR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Integration
{
    /// <summary>The checks both suites share.</summary>
    static class Seated
    {
        /// <summary>The shell's distance from its rest on the stand that still counts as on it (the strike settle is under
        /// 2 mm) and the tilt tolerance.</summary>
        public const float ShellSlack = 0.005f, ShellDeg = 1f, FloorSlack = 0.03f;

        public static HandpanInstrument Handpan(App app)
        {
            foreach (var m in app.Modules) if (m is HandpanModule hm) return hm.Instrument;
            return null;
        }

        /// <summary>Per frame: a stand that shows has the shell on it; at the seat and still, the shell is on its stand.</summary>
        public static void Frame(HandpanInstrument hp, ILayoutService layout, string when)
        {
            HandpanModel m = hp.Model;
            if (!m.Root.gameObject.activeInHierarchy) return;
            if (m.Stand.enabled)
            {
                float d = Vector3.Distance(m.Instrument.localPosition, HandpanModel.InstrumentLocal);
                float a = Quaternion.Angle(m.Instrument.localRotation, HandpanModel.InstrumentTilt);
                Assert.That(d, Is.LessThan(ShellSlack), when + ": the shell is " + (d * 100f).ToString("F1") + " cm off the stand it shows on");
                Assert.That(a, Is.LessThan(ShellDeg), when + ": the shell is turned " + a.ToString("F1") + " deg off its stand");
            }
            if (layout.PlaceOf(InstrumentKind.Handpan) == InstrumentPlace.Seat && !layout.IsMoving(InstrumentKind.Handpan) && hp.IsPresent && !m.Blending)
            {
                Assert.That(m.Current, Is.EqualTo(HandpanModel.Config.Seat), when + ": at the seat in its seat configuration");
                Assert.IsTrue(m.Stand.enabled, when + ": at the seat on its stand");
            }
        }

        /// <summary>After a change has settled: every at-seat instrument stands on the floor (its lowest drawn part within 3 cm
        /// of it), upright, unstowed and present; a displayed handpan shows no stand.</summary>
        public static void All(App app, ILayoutService layout, float floor, string when)
        {
            IRigRegistry rigs = app.Get<IRigRegistry>();
            int seated = 0;
            var log = new System.Text.StringBuilder();
            foreach (InstrumentDescriptor d in app.Registry.All)
            {
                Rig r = rigs.Get(d.Rig);
                if (r == null || r.Group == null || !app.Registry.IsLoaded(d.Kind)) continue;
                InstrumentPlace place = layout.PlaceOf(d.Kind);
                Assert.That(place, Is.Not.EqualTo(InstrumentPlace.Gliding), when + ": " + d.Id + " settled");
                if (place != InstrumentPlace.Seat) continue;
                seated++;
                Transform g = r.Group;
                Assert.That(Vector3.Angle(g.up, Vector3.up), Is.LessThan(0.5f), when + ": " + d.Id + " upright");
                Assert.That(Vector3.Distance(g.localScale, Vector3.one), Is.LessThan(1e-4f), when + ": " + d.Id + " full size");
                float low = float.PositiveInfinity;
                foreach (Renderer ren in g.GetComponentsInChildren<Renderer>())
                    if (ren.enabled && ren.gameObject.activeInHierarchy && ren is MeshRenderer) low = Mathf.Min(low, ren.bounds.min.y);
                // Raised to the player's level (Reset view at high eyes, LayoutModule.Height), it stands on its riser instead,
                // whose bottom is on the floor (the piano's stand stretches: never a riser).
                var lm = layout as LayoutModule;
                float raise = lm != null ? lm.RaiseOf(r) : 0f;
                log.Append($" {d.Id}: rig y {g.position.y - floor:F3}, lowest {low - floor:F3}{(raise > 0f ? $", riser {raise:F3}" : "")};");
                Assert.That(low - floor - raise, Is.InRange(-FloorSlack, FloorSlack), when + ": " + d.Id + " stands on the floor or its riser (lowest part " + (low - floor).ToString("F3") + " m from the floor, riser " + raise.ToString("F3") + ")");
                if (raise > 0f) Assert.That(lm.Risers.Centre((int)r.Id).y - lm.Risers.Size((int)r.Id).y * 0.5f, Is.EqualTo(floor).Within(1e-3f), when + ": " + d.Id + "'s riser on the floor");
                IStowable st = null;
                foreach (IStowable s in app.GetAll<IStowable>()) if (s.Kind == d.Kind) st = s;
                if (st != null) Assert.IsFalse(st.Stowed, when + ": " + d.Id + " out of its display configuration");
            }
            Debug.Log($"[Seated] {when}: floor {floor:F3},{log}");
            Assert.That(seated, Is.GreaterThan(0), when + ": something at the seat");
            HandpanInstrument hp = Handpan(app);
            Frame(hp, layout, when);
            InstrumentPlace hpPlace = layout.PlaceOf(InstrumentKind.Handpan);
            if (hpPlace == InstrumentPlace.Display) Assert.IsFalse(hp.Model.Stand.enabled, when + ": on the wall without its stand");
        }
    }

    /// <summary>The VR room, every real module, manual frames at 72 Hz.</summary>
    public class SeatedOnStandTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        HandpanInstrument _hp;
        int _frames;
        bool _reseated, _glidedToWall, _slid;

        [UnitySetUp]
        public IEnumerator BootApp()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            _hp = Seated.Handpan(_app);
            Assert.That(_layout, Is.Not.Null);
            Assert.That(_hp, Is.Not.Null, "the handpan module is loaded");
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            _boot.Step(Dt, 3);
            Assert.IsTrue(_layout.FirstArrangeDone);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _layout = null;
            _app = null;
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        static HandPose PinchAt(HandSide side, Vector3 p, float gap = 0f) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(p).WithPinch(gap);

        void Step(string when)
        {
            _boot.Step(Dt);
            _frames++;
            RigGlide g = _layout.GlideOf(InstrumentKind.Handpan);
            if (g != null && g.Target == RigGlide.Goal.Reseat) _reseated = true;
            if (g != null && g.Target == RigGlide.Goal.Display && g.Mode == RigGlide.Kind.Glide) _glidedToWall = true;
            if (g != null && g.Mode != RigGlide.Kind.Glide) _slid = true;
            Seated.Frame(_hp, _layout, when);
        }

        /// <summary>Frames until nothing moves or settles (checked every frame), then a few more and the at-rest checks.</summary>
        void Settle(string when)
        {
            Step(when);
            for (int i = 0; i < 600 && (_layout.AnyMoving || _layout.Settling || _hp.Model.Blending); i++) Step(when);
            for (int i = 0; i < 6; i++) Step(when);
            Seated.All(_app, _layout, 0f, when);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator EveryPlacementPathKeepsTheHandpanOnItsStandAndEveryInstrumentOnTheFloor()
        {
            Seated.All(_app, _layout, 0f, "boot");
            Assert.That(_layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display), "the default room: the handpan on its bracket");

            // My set: the handpan joins (it glides from the wall, the others make room), then sets that move it between slots.
            Assert.IsTrue(_layout.Bring(InstrumentKind.Handpan, BringReason.Menu), "brought into My set");
            Settle("bring into My set");
            Assert.That(_layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Seat));
            var sets = new[]
            {
                new[] { InstrumentKind.Handpan, InstrumentKind.Piano },
                new[] { InstrumentKind.Handpan, InstrumentKind.Piano, InstrumentKind.Drums },
                new[] { InstrumentKind.Handpan, InstrumentKind.Harp, InstrumentKind.Marimba },
                new[] { InstrumentKind.Handpan, InstrumentKind.HandDrums, InstrumentKind.Bowls },
                new[] { InstrumentKind.Handpan, InstrumentKind.Piano, InstrumentKind.Harp, InstrumentKind.Drums },
                new[] { InstrumentKind.Handpan },
            };
            foreach (InstrumentKind[] set in sets)
            {
                string name = "My set " + string.Join("+", set);
                if (!_layout.TrySetSet(set)) { Debug.Log("[Seated] " + name + " does not fit: skipped"); continue; }
                Settle(name);
            }
            Assert.IsTrue(_reseated, "a set change moved the handpan to another slot (the reseat glide that lifted it)");

            // Put away and back: the shell goes on its bracket alone and comes back down onto its stand.
            Assert.IsTrue(_layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Handpan }));
            Settle("piano + handpan");
            Assert.IsTrue(_layout.PutAway(InstrumentKind.Handpan));
            Settle("put away");
            Assert.That(_layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Display));
            Assert.IsTrue(_glidedToWall, "the glide to the wall ran");
            Assert.That(_hp.Model.Current, Is.EqualTo(HandpanModel.Config.Wall), "on its bracket");
            Assert.IsTrue(_layout.Bring(InstrumentKind.Handpan, BringReason.Ray));
            Settle("brought back");

            // One alone: page turns through the whole carousel (each instrument glides in from its place and back).
            _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Settle("one alone: piano");
            int n = _app.Registry.Carousel.Count;
            for (int i = 0; i < n + 1; i++)
            {
                _layout.Next(1);
                Settle("page turn to " + _layout.Current);
            }
            for (int i = 0; i < 3; i++)
            {
                _layout.Next(-1);
                Settle("page turn back to " + _layout.Current);
            }
            Assert.IsTrue(_layout.Bring(InstrumentKind.Handpan, BringReason.Menu));
            Settle("handpan alone");
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Handpan));

            // Move instruments: pick it up by its band, drag it 25 cm and a little round, drop it.
            yield return Drag("move drag");

            // Reset view after turning, and with the eyes lower and higher (a sofa, a bar stool).
            Transform head = _app.Head;
            Vector3 head0 = head.position;
            Quaternion rot0 = head.rotation;
            head.rotation = Quaternion.Euler(10f, rot0.eulerAngles.y + 50f, 0f);
            _layout.BringToPlayer();
            Settle("reset view, turned");
            foreach (float eyes in new[] { 0.95f, 1.45f, head0.y })
            {
                head.position = new Vector3(head0.x, eyes, head0.z);
                _layout.BringToPlayer();
                Settle($"reset view, eyes at {eyes:F2} m");
                _layout.ArrangeAroundPlayer(true);
                Settle($"arrange around the player, eyes at {eyes:F2} m");
            }
            head.rotation = rot0;

            // My set again with the eyes low: bring, reseat, put away.
            head.position = new Vector3(head0.x, 1.0f, head0.z);
            _layout.Set(LayoutMode.All, InstrumentKind.Handpan);
            Settle("my set, eyes low");
            if (_layout.TrySetSet(new[] { InstrumentKind.Handpan, InstrumentKind.Marimba, InstrumentKind.Drums }))
                Settle("my set handpan + marimba + drums, eyes low");
            head.position = head0;
            Debug.Log($"[Seated] VR room: {_frames} frames checked, reseat {_reseated}, glide to the wall {_glidedToWall}, slide {_slid}");
        }

        IEnumerator Drag(string when)
        {
            _layout.BeginArrange();
            Step(when);
            Step(when);
            Assert.IsTrue(_layout.Arranging);
            Rig rig = _app.Get<IRigRegistry>().Get(RigId.Handpan);
            Vector3 grip = _hp.Model.Band.bounds.center + Vector3.up * 0.01f;
            // the band's centre is inside the ring: the near rim, toward the head
            Vector3 toHead = _app.Head.position - grip; toHead.y = 0f;
            grip += toHead.normalized * 0.25f;
            Assert.That(_layout.NearestArrangeRig(grip, 0.08f, out _), Is.SameAs(rig), "the grip is on the handpan");
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, grip + Vector3.up * 0.03f, 0.06f));
            for (int i = 0; i < 3; i++) Step(when);
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, grip));
            for (int f = 0; f < 20 && !_layout.MoveDragging; f++) Step(when);
            Assert.IsTrue(_layout.MoveDragging, when + ": picked up");
            Vector3 side = Vector3.Cross(Vector3.up, toHead.normalized);
            for (int i = 1; i <= 48; i++)
            {
                Vector3 p = grip + side * (0.25f * i / 48f) + Vector3.up * (0.05f * i / 48f);
                _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, p));
                Step(when + " (dragging)");
                // 2026-11-11: the hand's 5 cm rise lifts it 3 cm (past the 2 cm dead band, 1:1), never more, never into
                // the floor; Seated.All then finds it on its riser.
                Assert.That(rig.Group.position.y, Is.InRange(-1e-4f, 0.05f - MoveConstants.LiftDeadband + 1e-3f), when + ": lifted only with the hand");
            }
            Vector3 end = grip + side * 0.25f + Vector3.up * 0.05f;
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, end, 0.06f));
            for (int i = 0; i < Mathf.CeilToInt(MoveConstants.ReleaseGrace / Dt) + 2; i++) Step(when + " (letting go)");
            Assert.IsFalse(_layout.MoveDragging, when + ": dropped");
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            Settle(when + ", dropped");
            _layout.EndArrange();
            Settle(when + ", done");
            yield return null;
        }
    }

    /// <summary>"Play in my room" (the build's Layout, one of the recorded rooms, no table placement as in the app).</summary>
    public class SeatedOnStandMrTests : MrTestRig
    {
        protected override bool UseContractLayout => false;
        protected override bool TableOnEnter => false;
        LayoutModule _layout;
        HandpanInstrument _hp;
        int _frames;

        protected override void Prepare(MemorySettingsStore settings) => settings.SetInt(AppSettings.KeyTourDone, 1);

        float Floor => Mr.Active ? Mr.FloorHeight : 0f;

        /// <summary>Unity frames (the app ticks itself) checked one by one until nothing moves, then the at-rest checks.</summary>
        IEnumerator Settle(string when)
        {
            float t0 = Time.realtimeSinceStartup;
            int still = 0;
            while (still < 6 && Time.realtimeSinceStartup - t0 < 30f)
            {
                yield return null;
                _frames++;
                Seated.Frame(_hp, _layout, when);
                still = _layout.AnyMoving || _layout.Settling || _hp.Model.Blending ? 0 : still + 1;
            }
            Seated.All(App, _layout, Floor, when);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator InMyRoomTheHandpanStaysOnItsStandThroughEveryChange()
        {
            _layout = App.Get<ILayoutService>() as LayoutModule;
            _hp = Seated.Handpan(App);
            Assert.That(_layout, Is.Not.Null, "the build's Layout module");
            Assert.That(_hp, Is.Not.Null);
            Fake.SetTracked(HandSide.Left, false);
            Fake.SetTracked(HandSide.Right, false);
            yield return Settle("VR room before");

            yield return EnterRoom("MeshLivingRoom2");
            yield return Settle("entered my room");

            // One alone on the piano, then the handpan alone: the slide in that lifted the shell to the bracket's height.
            _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            yield return Settle("my room: piano alone");
            Assert.IsTrue(_layout.Bring(InstrumentKind.Handpan, BringReason.Menu));
            yield return Settle("my room: handpan alone");
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Handpan));
            Assert.That(_layout.PlaceOf(InstrumentKind.Handpan), Is.EqualTo(InstrumentPlace.Seat));

            // Page turns away and back, twice (the second time it comes in again from where it was left).
            for (int round = 0; round < 2; round++)
            {
                _layout.Next(1);
                yield return Settle("my room: turned to " + _layout.Current);
                _layout.Next(-1);
                yield return Settle("my room: back to " + _layout.Current);
                Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Handpan));
            }

            // My set with the handpan (slides in and out), a set that moves it to another slot, then out of the set.
            _layout.Set(LayoutMode.All, InstrumentKind.Handpan);
            yield return Settle("my room: my set");
            foreach (InstrumentKind[] set in new[]
            {
                new[] { InstrumentKind.Handpan, InstrumentKind.Piano },
                new[] { InstrumentKind.Piano, InstrumentKind.Handpan, InstrumentKind.Harp },
                new[] { InstrumentKind.Handpan, InstrumentKind.Drums },
                new[] { InstrumentKind.Piano, InstrumentKind.Drums },
                new[] { InstrumentKind.HandDrums, InstrumentKind.Handpan },
            })
            {
                string name = "my room: " + string.Join("+", set);
                if (!_layout.TrySetSet(set)) { Debug.Log("[Seated] " + name + " does not fit here: skipped"); continue; }
                yield return Settle(name);
            }

            // Reset view turned and at other eye heights.
            Transform head = App.Head;
            Vector3 head0 = head.position;
            foreach (float eyes in new[] { 0.95f, 1.45f, head0.y })
            {
                head.position = new Vector3(head0.x, Floor + eyes, head0.z);
                _layout.BringToPlayer();
                yield return Settle($"my room: reset view, eyes {eyes:F2} m over the floor");
            }
            head.position = head0;

            Mr.Exit();
            yield return Settle("back in the music room");
            Debug.Log($"[Seated] my room: {_frames} frames checked, floor {Mr.FloorHeight:F3}");
        }
    }
}
#endif
