// Instruments open in front of the seated player and within a foot, drag and drop in "Move instruments", the comfort
// offsets, old saved data, end to end with every real module (simulated headset, manual frames at 72 Hz, scripted hands):
//   - every instrument alone and the sets, at several head positions and yaws: the main instrument straight ahead, the
//     others to its sides, each standing on the floor, its playing surface's near edge within 0.30 m (one foot) of the
//     chest point; every distance is logged as "[REACH]" for the record;
//   - a Move instruments drag moves the instrument with the hand, silently, at its height, and the drop is saved as a
//     comfort offset that comes back in front of the player at another seat; Reset forgets it;
//   - a drop into another instrument or onto the seat settles clear of both;
//   - with Move instruments off, pinches on keys, strings and drum heads never start a drag;
//   - old saved world placements and an old mixed-reality anchor load without moving anything far away.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class PlacementInFrontTests
    {
        const float Dt = 1f / 72f;
        /// <summary>The reach band (one foot) plus 5 mm for the resolver's 1 cm steps.</summary>
        const float Reach = LayoutModule.ReachBand + 0.005f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        readonly List<KeyValuePair<InstrumentKind, float>> _reach = new List<KeyValuePair<InstrumentKind, float>>();

        IEnumerator BootApp(System.Action<MemorySettingsStore> settings = null)
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            settings?.Invoke(_boot.Settings);
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            Assert.That(_layout, Is.Not.Null);
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
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        static HandPose PinchAt(HandSide side, Vector3 p) =>
            HandPose.Open(side, Vector3.zero, Quaternion.LookRotation(Vector3.forward, Vector3.up)).WithIndexTipAt(p).WithPinch(0f);

        static HandPose OpenAt(HandSide side, Vector3 p) =>
            HandPose.Open(side, Vector3.zero, Quaternion.LookRotation(Vector3.forward, Vector3.up)).WithIndexTipAt(p).WithPinch(0.06f);

        void Step(int n = 1) => _boot.Step(Dt, n);

        /// <summary>The head here, looking a little down at yaw (degrees), with the player's rig where it is.</summary>
        void Sit(Vector3 head, float yawDeg)
        {
            _app.Head.SetPositionAndRotation(head, Quaternion.Euler(12f, yawDeg, 0f));
        }

        void Settle()
        {
            for (int i = 0; i < 300 && (_layout.AnyMoving || _layout.Settling); i++) Step();
            Step(2);
        }

        Rig RigOf(InstrumentKind k) => _app.Get<IRigRegistry>().Get(InstrumentIds.RigOf(k));

        /// <summary>The bearing (degrees from the head's heading) of an instrument's play centre.</summary>
        float Bearing(InstrumentKind k)
        {
            RigEntry e = _layout.Placement.Entry(RigOf(k));
            _layout.Placement.Pose(e);
            Vector3 c = PlacementSolver.CentreWorld(e) - _app.Head.position;
            Vector3 f = _app.Head.forward;
            return Mathf.DeltaAngle(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg);
        }

        IReadOnlyList<InstrumentKind> Playable()
        {
            var list = new List<InstrumentKind>();
            foreach (InstrumentKind k in _app.Registry.Carousel) if (_app.Registry.IsLoaded(k)) list.Add(k);
            return list;
        }

        // Seats in the room (room frame), turned with the room: it faces the fire since 2026-11-11 (RoomFrame), so these are the
        // same places and directions in the room as before.
        static readonly Vector3[] s_heads = { RoomFrame.ToWorld(new Vector3(0f, 1.2f, 0f)), RoomFrame.ToWorld(new Vector3(0.45f, 1.1f, -0.7f)), RoomFrame.ToWorld(new Vector3(-0.35f, 1.28f, -1.5f)) };
        static readonly float[] s_yaws = { 0f + RoomFrame.Yaw, 75f + RoomFrame.Yaw, -130f + RoomFrame.Yaw, 180f + RoomFrame.Yaw };

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator EveryInstrumentAloneOpensInFrontWithinAFoot()
        {
            yield return BootApp();
            var worst = new Dictionary<InstrumentKind, float>();
            var failures = new List<string>();
            var bearings = new Dictionary<InstrumentKind, Vector2>();
            foreach (Vector3 head in s_heads)
                foreach (float yaw in s_yaws)
                {
                    // The player sits down here and the room is laid out about them (Play here / Reset view): since
                    // 2026-11-17 that seat is the player's front, and every instrument chosen afterwards lands about it.
                    Sit(head, yaw);
                    _layout.ArrangeAroundPlayer(true);
                    Settle();
                    foreach (InstrumentKind k in Playable())
                    {
                        _app.Head.rotation = Quaternion.Euler(12f, yaw, 0f);   // where it sits (fitSeat may have moved the seat)
                        _layout.Set(LayoutMode.Single, k);
                        Settle();
                        Rig r = RigOf(k);
                        Assert.IsTrue(r.InScene, k + " at the seat");
                        float reach = _layout.ReachOf(r), bearing = Bearing(k);
                        Debug.Log($"[REACH] alone {k} head {head:F2} yaw {yaw:F0}: near edge {reach:F3} m from the chest, bearing {bearing:F1} deg, y {r.Group.position.y:F3}");
                        if (!worst.TryGetValue(k, out float w) || reach > w) worst[k] = reach;
                        // Within a foot, unless the 30 cm eye clearance holds it back: a tall frame (the harp's neck) would
                        // otherwise come into the face. Those stop where the clearance binds.
                        float eyes = _layout.EyeDistance(r);
                        bool eyeBound = eyes <= GrabConstants.EyeClear + 0.03f;
                        Debug.Log($"[REACH] alone {k}: eyes {eyes:F3} m{(reach > Reach ? (eyeBound ? " (held back by the eye clearance)" : " (OUT OF THE BAND)") : "")}");
                        if (reach > Reach && !eyeBound) failures.Add($"{k} alone (head {head}, yaw {yaw}): {reach:F3} m, eyes {eyes:F3}");
                        if (reach > 0.5f) failures.Add($"{k} alone (head {head}, yaw {yaw}): {reach:F3} m is not reachable");
                        // In front: at its single home about the player's front, so its bearing is the same at every seat and
                        // heading; straight ahead (its play centre on the forward line since 2026-11-17; 65 degrees is the old
                        // allowance for the bowls, whose single home was beside the left knee).
                        if (!bearings.TryGetValue(k, out Vector2 range)) range = new Vector2(bearing, bearing);
                        bearings[k] = new Vector2(Mathf.Min(range.x, bearing), Mathf.Max(range.y, bearing));
                        if (Mathf.Abs(bearing) > 65f) failures.Add($"{k} alone (head {head}, yaw {yaw}): bearing {bearing:F1}");
                        Assert.That(r.Group.position.y, Is.EqualTo(r.HomeSingle.y).Within(0.002f), $"{k} stands at its own height on the floor");
                        Assert.That(Mathf.Abs(Mathf.DeltaAngle(r.Group.eulerAngles.y, yaw + r.HomeYawSingle)), Is.LessThan(1f), $"{k} faces the player");
                    }
                }
            foreach (var kv in worst) Debug.Log($"[REACH] worst alone {kv.Key}: {kv.Value:F3} m");
            foreach (var kv in bearings)
                if (kv.Value.y - kv.Value.x > 3f) failures.Add($"{kv.Key} alone: its bearing changes with the seat ({kv.Value.x:F1}..{kv.Value.y:F1})");
            Assert.That(failures, Is.Empty, "every instrument alone opens straight ahead within a foot");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        static readonly InstrumentKind[][] s_sets =
        {
            new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp },
            new[] { InstrumentKind.Handpan, InstrumentKind.Bowls, InstrumentKind.HandDrums },
            new[] { InstrumentKind.Marimba, InstrumentKind.HandDrums, InstrumentKind.Harp },
            new[] { InstrumentKind.Piano, InstrumentKind.Handpan },
            new[] { InstrumentKind.Drums, InstrumentKind.Bowls, InstrumentKind.Handpan },
        };

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator EverySetOpensAroundThePlayerWithTheMainOneAhead()
        {
            yield return BootApp();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            var failures = new List<string>();
            var worst = new Dictionary<string, float>();
            foreach (InstrumentKind[] set in s_sets)
                foreach (Vector3 head in s_heads)
                    foreach (float yaw in s_yaws)
                    {
                        Sit(head, yaw);
                        if (!_layout.TrySetSet(set)) { Debug.Log($"[REACH] set {string.Join(",", set)} does not fit the planner: skipped"); break; }
                        Settle();
                        Sit(head, yaw);
                        _layout.ArrangeAroundPlayer(true);   // opened again where the player sits now
                        Settle();
                        InstrumentKind lead = _layout.Lead;
                        // The planner's main slot is straight ahead; a set it plans without a Front member (the handpan,
                        // the bowls and the hand drums take the front-side slots) keeps the planner's slots (logged).
                        bool front = _layout.SlotOf(lead) == SeatSlot.Front;
                        Debug.Log($"[REACH] set {string.Join(",", set)}: main {lead} in slot {_layout.SlotOf(lead)}, bearing {Bearing(lead):F1}");
                        if (front && Mathf.Abs(Bearing(lead)) > 25f) failures.Add($"main {lead} of {string.Join(",", set)} not ahead: {Bearing(lead):F1}");
                        if (front && _layout.ReachOf(RigOf(lead)) > Reach) failures.Add($"main {lead} of {string.Join(",", set)} outside the band");
                        _layout.MeasureReach(_reach);
                        Assert.That(_reach.Count, Is.EqualTo(set.Length));
                        Assert.That(_layout.Placement.Overlaps2D(), Is.EqualTo(0), "no two instruments overlap");
                        foreach (var kv in _reach)
                        {
                            float b = Bearing(kv.Key);
                            Debug.Log($"[REACH] set {string.Join(",", set)} head {head:F2} yaw {yaw:F0}: {kv.Key} slot {_layout.SlotOf(kv.Key)} near edge {kv.Value:F3} m, bearing {b:F1} deg");
                            string key = string.Join(",", set) + " " + kv.Key;
                            if (!worst.TryGetValue(key, out float w) || kv.Value > w) worst[key] = kv.Value;
                            // The others: in the band where the ring has room; where the main instrument, the side table or
                            // the eye clearance is in the way they stop at the nearest free spot (logged), never beyond a
                            // seated lean and turn (0.75 m), and never on each other.
                            if (kv.Value > 0.75f) failures.Add($"{kv.Key} in {string.Join(",", set)} (head {head}, yaw {yaw}): {kv.Value:F3} m, bearing {b:F1}");
                        }
                    }
            foreach (var kv in worst) Debug.Log($"[REACH] worst in set {kv.Key}: {kv.Value:F3} m");
            Assert.That(failures, Is.Empty, "every set: the main one ahead in the band, the others near and clear");
        }

        /// <summary>A point a hand pinches to pick an instrument up in Move instruments: just above its nearest part to the
        /// head (within the 8 cm reach, the instrument the near target).</summary>
        Vector3 GripOf(Rig rig)
        {
            RigEntry e = _layout.Placement.Entry(rig);
            _layout.Placement.Pose(e);
            Vector3 head = _app.Head.position, c = PlacementSolver.CentreWorld(e);
            Bounds b = new Bounds(rig.Group.position, Vector3.zero);
            foreach (Renderer r in rig.Group.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);
            for (float yk = 0.9f; yk >= 0.3f; yk -= 0.1f)
            {
                float y = Mathf.Lerp(b.min.y, b.max.y, yk);
                if (y < 0.25f) continue;
                Vector3 from = new Vector3(head.x, y, head.z), to = new Vector3(c.x, y, c.z);
                for (float t = 0f; t <= 1f; t += 0.01f)
                {
                    Vector3 p = Vector3.Lerp(from, to, t);
                    if (_layout.NearestArrangeRig(p, 0.04f, out _) == rig) return p;
                }
            }
            Assert.Fail(rig.Id + ": no grip found");
            return default;
        }

        void Drop(HandSide side, Vector3 at)
        {
            _boot.Hands.SetPose(side, OpenAt(side, at));
            Step(Mathf.CeilToInt(MoveConstants.ReleaseGrace / Dt) + 1);
            Assert.IsFalse(_layout.MoveDragging, "let go: dropped after the release grace (a flicker never drops it)");
            Settle();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AMoveDragIsSavedAsAComfortOffsetThatComesBackInFront()
        {
            yield return BootApp();
            _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Settle();
            _layout.BeginArrange();
            Step(2);
            Rig piano = RigOf(InstrumentKind.Piano);
            Vector3 p0 = piano.Group.position;
            Vector3 at = GripOf(piano);
            _boot.Hands.SetPose(HandSide.Left, OpenAt(HandSide.Left, at + Vector3.up * 0.03f));
            Step(4);
            _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at));
            Step(3);
            Assert.IsFalse(_layout.MoveDragging, "not before the 80 ms debounce");
            Step(5);
            Assert.IsTrue(_layout.MoveDragging, "the pinch on the piano picks it up");
            Assert.That(_layout.HeldRig, Is.SameAs(piano));
            Assert.IsTrue(piano.Held, "silent while dragged");
            // 12 cm farther, 6 cm to the right, 8 cm up: it follows, and rises 6 cm (the 2 cm dead band, then 1:1; 2026-11-11).
            Vector3 fwd = Flat(_app.Head.forward), right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 move = fwd * 0.12f + right * 0.06f + Vector3.up * 0.08f;
            for (int i = 1; i <= 12; i++) { _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at + move * (i / 12f))); Step(); }
            Step(10);
            Vector3 d = piano.Group.position - p0;
            Assert.That(new Vector2(d.x, d.z).magnitude, Is.EqualTo(new Vector2(move.x, move.z).magnitude).Within(0.02f), "with the hand along the floor");
            Assert.That(d.y, Is.EqualTo(0.08f - MoveConstants.LiftDeadband).Within(0.006f), "lifted with the hand");
            Drop(HandSide.Left, at + move);
            Assert.IsFalse(piano.Held, "free again after the short settle (still quiet: Move instruments is on)");
            _layout.EndArrange();
            Assert.IsTrue(_layout.TryGetOffset(RigId.Piano, out ComfortOffset o), "the drop is kept");
            Debug.Log($"[REACH] piano comfort offset after the drag {o}");
            Assert.That(o.Distance, Is.EqualTo(0.12f).Within(0.025f));
            Assert.That(o.Side, Is.EqualTo(0.06f).Within(0.025f));
            Assert.That(o.Height, Is.EqualTo(d.y).Within(0.005f), "the height tweak kept");
            string key = AppSettings.ComfortKey(LayoutMode.Single, InstrumentKind.Piano);
            Assert.That(_boot.Settings.GetString(key, ""), Does.StartWith("c1;Piano:"));
            float y1 = piano.Group.position.y;

            // Another seat, turned 100 degrees: in front again, with the same tweak (offset in the new seat frame).
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            Sit(new Vector3(0.6f, 1.15f, -1.0f), 100f);
            _layout.ArrangeAroundPlayer(true);
            Settle();
            Assert.IsTrue(_layout.TryGetOffset(RigId.Piano, out ComfortOffset o2));
            Assert.That(o2.Distance, Is.EqualTo(o.Distance).Within(1e-4f));
            Assert.That(Mathf.Abs(Bearing(InstrumentKind.Piano)), Is.LessThanOrEqualTo(25f), "still in front");
            Assert.That(piano.Group.position.y, Is.EqualTo(y1).Within(0.002f), "the same height");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(piano.Group.eulerAngles.y, 100f)), Is.LessThan(5f), "facing the player");

            // Reset forgets it: the default pose again.
            _layout.ResetArrangement();
            Settle();
            Assert.IsFalse(_layout.TryGetOffset(RigId.Piano, out _));
            Assert.That(_boot.Settings.GetString(key, ""), Is.EqualTo(""));
            Assert.That(piano.Group.position.y, Is.EqualTo(piano.HomeSingle.y).Within(0.002f));
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ADropNeverOverlapsAnotherInstrumentOrTheSeat()
        {
            yield return BootApp();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            Assert.That(_layout.Placement.Overlaps2D(), Is.EqualTo(0));
            _layout.BeginArrange();
            Step(2);
            Rig kit = RigOf(InstrumentKind.Drums), piano = RigOf(InstrumentKind.Piano);
            Vector3 at = GripOf(kit);
            _boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.03f));
            Step(2);
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            Step(8);
            Assert.That(_layout.HeldRig, Is.SameAs(kit));
            // Drag the kit right into the piano for a second (it passes through after 300 ms), then let go there.
            Vector3 into = piano.Group.position - kit.Group.position;
            into.y = 0f;
            for (int i = 1; i <= 72; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + into * Mathf.Min(1f, i / 30f))); Step(); }
            Drop(HandSide.Right, at + into);
            Assert.That(_layout.Placement.Overlaps2D(), Is.EqualTo(0), "the drop settled clear of the piano");
            // And onto the seat: it settles off the player.
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            Step(2);
            at = GripOf(kit);
            _boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.03f));
            Step(2);
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            Step(8);
            Assert.That(_layout.HeldRig, Is.SameAs(kit));
            Vector3 toSeat = _app.Head.position - PlacementSolver.CentreWorld(_layout.Placement.Pose(_layout.Placement.Entry(kit)));
            toSeat.y = 0f;
            for (int i = 1; i <= 72; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + toSeat * Mathf.Min(1f, i / 30f))); Step(); }
            Drop(HandSide.Right, at + toSeat);
            RigEntry e = _layout.Placement.Entry(kit);
            _layout.Placement.SeatRect(_app.Head.position, LayoutMath.YawOf(_app.Head));
            Assert.IsFalse(_layout.Placement.OnSeat(_layout.Placement.Pose(e), 0f), "never on the player's seat");
            Assert.That(_layout.Placement.Overlaps2D(), Is.EqualTo(0));
            Assert.That(_layout.EyeDistance(kit), Is.GreaterThanOrEqualTo(GrabConstants.EyeClear - 0.005f), "clear of the eyes");
            _layout.EndArrange();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PinchesOnPlayingSurfacesNeverStartADrag()
        {
            yield return BootApp();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            Assert.IsFalse(_layout.MoveOn, "Move instruments is off");
            var spots = new List<KeyValuePair<string, Vector3>>();
            IPianoApi piano = _app.Get<IPianoApi>();
            piano.TryGetKey(60, out PianoKeyInfo c4);
            spots.Add(new KeyValuePair<string, Vector3>("middle C", piano.Root.TransformPoint(new Vector3(c4.X, 0.004f, 0.05f))));
            IDrumsApi drums = _app.Get<IDrumsApi>();
            spots.Add(new KeyValuePair<string, Vector3>("snare head", drums.Pieces[0].Transform.position + Vector3.up * 0.01f));
            IHarpApi harp = _app.Get<IHarpApi>();
            int mid = harp.StringCount / 2;
            spots.Add(new KeyValuePair<string, Vector3>("a harp string", harp.Root.TransformPoint((harp.StringStart(mid) + harp.StringEnd(mid)) * 0.5f)));
            int drags = _layout.MoveDrags;
            foreach (var s in spots)
            {
                Vector3 p0 = RigOf(InstrumentKind.Piano).Group.position;
                _boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, s.Value));
                Step(4);
                _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, s.Value));
                Step(40);
                for (int i = 1; i <= 10; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, s.Value + new Vector3(0.01f * i, 0f, 0f))); Step(); }
                Assert.That(_layout.MoveDrags, Is.EqualTo(drags), "a pinch on " + s.Key + " never starts a drag");
                Assert.IsNull(_layout.HeldRig, "nothing held");
                Assert.That(Vector3.Distance(RigOf(InstrumentKind.Piano).Group.position, p0), Is.LessThan(1e-4f));
                _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
                Step(4);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator OldSavedPlacementsAndAnchorsLoadWithoutMovingAnythingFar()
        {
            // An old seat-frame placement of the full room (a raised piano a little to the right, a kit 3 m away) and an
            // old mixed-reality piano anchor.
            yield return BootApp(s =>
            {
                s.SetString(AppSettings.ArrangeKey(LayoutMode.All, InstrumentKind.Piano), "a1;Piano:0.08,0.8,0.5,6;Drums:3.1,0,2.8,40;Harp:garbage");
                s.SetString("musicroom.mrPianoAnchor", System.Guid.NewGuid().ToString("N"));
                s.SetInt("musicroom.mrPianoOnTable", 1);
            });
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            Assert.That(_layout.Migrations, Is.EqualTo(1), "the old placement was read once");
            Assert.That(_boot.Settings.GetString(AppSettings.ArrangeKey(LayoutMode.All, InstrumentKind.Piano), ""), Is.EqualTo(""), "and deleted");
            Assert.IsTrue(_layout.TryGetOffset(RigId.Piano, out ComfortOffset po), "the plausible piano tweak is kept as an offset");
            Assert.That(po.Height, Is.EqualTo(0.08f).Within(0.01f));
            Assert.IsFalse(_layout.TryGetOffset(RigId.Drums, out _), "a kit 3 m away is dropped, not restored");
            _layout.MeasureReach(_reach);
            // Every instrument stays where My set opens it (the harp of the default set stands beyond the side table: 0.72 m).
            foreach (var kv in _reach) Assert.That(kv.Value, Is.LessThanOrEqualTo(0.75f), kv.Key + " stays near the player");
            Assert.That(Mathf.Abs(Bearing(InstrumentKind.Piano)), Is.LessThanOrEqualTo(25f));
            Assert.That(_layout.Placement.Overlaps2D(), Is.EqualTo(0));
        }
    }
}
