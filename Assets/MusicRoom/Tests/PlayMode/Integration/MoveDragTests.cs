// "Move instruments" (2026-11-10: one On / Off setting in place of the gold knobs, the one-shot arrange mode and Lock),
// end to end with every real module (simulated headset, manual frames at 72 Hz, scripted hands):
//   - the setting persists, an old Lock setting is removed, a saved "on" comes back once the start panel is answered, and
//     the app suspends it while the headset is off (back on by itself when it is on again);
//   - Off: a still pinch on an instrument's body, then a pull, moves nothing (Sunny hears InstrumentBodyPinchEvent);
//   - On: pinches and pokes on the keys play no note; a pinch anywhere on an instrument picks it up;
//   - the drag starts without a jump, keeps the grab offset, calms a still hand's jitter, follows a moving hand within
//     ~1.5 cm without overshoot, never tilts it, and a level drag never bobs (the lift itself: Layout/HeightTests);
//   - a loosened or flickering pinch and a short tracking gap do not drop it (no jump when the hand picks it up again);
//   - the wrist spins it (yaw only, about its centre, TurnGain past a small dead band) all the way round over a few
//     grabs, and two hands twist it about its centre;
//   - a drop into another instrument eases out of it (the settle) and is saved.
// (Zero GC per frame while dragging with a moving, turning hand: ArrangeModeTests.TheFrameStaysAllocationFreeWhileArranging,
// with the Layout module alone, so another module's own periodic work cannot blur it.)
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Piano;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class MoveDragTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        readonly List<TakeEvent> _events = new List<TakeEvent>();
        System.Action<RecorderEvent> _onRec;

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
            Assert.IsFalse(_layout.InPlayGrab, "in play no pinch moves an instrument");
            _events.Clear();
            _onRec = e => _events.Add(e.Event);
            _app.Events.Subscribe(_onRec);
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            _boot.Step(Dt, 3);
            Assert.IsTrue(_layout.FirstArrangeDone);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_app != null && _onRec != null) _app.Events.Unsubscribe(_onRec);
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _layout = null;
            _app = null;
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        static HandPose PinchAt(HandSide side, Vector3 p, float yawDeg = 0f, float gap = 0f) =>
            HandPose.Open(side, Vector3.zero, Quaternion.Euler(0f, yawDeg, 0f)).WithIndexTipAt(p).WithPinch(gap);

        static HandPose OpenAt(HandSide side, Vector3 p, float yawDeg = 0f) => PinchAt(side, p, yawDeg, 0.06f);

        void Step(int n = 1) => _boot.Step(Dt, n);

        void Settle()
        {
            for (int i = 0; i < 300 && (_layout.AnyMoving || _layout.Settling); i++) Step();
            Step(2);
        }

        Rig RigOf(InstrumentKind k) => _app.Get<IRigRegistry>().Get(InstrumentIds.RigOf(k));

        int PianoOns() { int n = 0; foreach (var e in _events) if (e.type == TakeEventType.PianoOn) n++; return n; }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>A point a hand pinches to pick an instrument up: just above its part nearest the head (the instrument the
        /// near target, within the 8 cm reach).</summary>
        Vector3 GripOf(Rig rig)
        {
            // From the head toward the instrument's centre, at a few heights over its parts: the first point within 4 cm of
            // one of its parts (as the pick-up measures them) that no other instrument is nearer to.
            Vector3 head = _app.Head.position, c = CentreOf(rig);
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

        /// <summary>A point just outside one of the rig's body meshes (Rig.Handles: case, shells, frame) that the old in-play
        /// grab would take: no play surface there, the rig the grab target.</summary>
        bool BodySpot(Rig rig, out Vector3 spot)
        {
            Vector3 head = _app.Head.position;
            for (int i = 0; i < rig.Handles.Count; i++)
            {
                Renderer r = rig.Handles[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                Vector3 c = r.bounds.ClosestPoint(head);
                Vector3 p = c + (head - c).normalized * 0.01f;
                if (_layout.PlaySurfaceAt(p) != PlaySurface.None || _layout.GrabTarget(p) != rig) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        /// <summary>The footprint centre the drag carries (and the wrist spins it about).</summary>
        Vector3 CentreOf(Rig rig)
        {
            RigEntry e = _layout.Placement.Entry(rig);
            _layout.Placement.Pose(e);
            return PlacementSolver.CentreWorld(e);
        }

        IEnumerator PickUp(HandSide side, Vector3 at, float yawDeg = 0f)
        {
            _boot.Hands.SetPose(side, OpenAt(side, at + Vector3.up * 0.03f, yawDeg));
            Step(3);
            _boot.Hands.SetPose(side, PinchAt(side, at, yawDeg));
            for (int f = 0; f < 20 && !_layout.MoveDragging; f++) Step();
            Assert.IsTrue(_layout.MoveDragging, "the pinch picks it up");
            yield return null;
        }

        void Drop(HandSide side, Vector3 at, float yawDeg = 0f)
        {
            _boot.Hands.SetPose(side, OpenAt(side, at, yawDeg));
            Step(Mathf.CeilToInt(MoveConstants.ReleaseGrace / Dt) + 1);
            Assert.IsFalse(_layout.MoveDragging, "dropped after the release grace");
            Settle();
        }

        IEnumerator StartedAlone(InstrumentKind k)
        {
            _layout.Set(LayoutMode.Single, k);
            Settle();
            _layout.BeginArrange();
            Step(2);
            Assert.IsTrue(_layout.Arranging);
            yield return null;
        }

        // ---------------------------------------------------------------- the setting

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheSettingPersistsComesBackAfterTheStartPanelAndWaitsForTheHeadset()
        {
            // An old save with Lock instruments on: the key goes, Move instruments starts off.
            yield return BootApp(s => s.SetInt(AppSettings.KeyLockedOld, 1));
            Assert.IsFalse(_boot.Settings.HasKey(AppSettings.KeyLockedOld), "the old Lock setting is removed");
            Assert.IsFalse(_layout.MoveOn);
            Assert.IsFalse(_layout.Arranging);
            _layout.BeginArrange();
            Assert.IsTrue(_layout.MoveOn && _layout.Arranging);
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyMoveInstruments, 0), Is.EqualTo(1), "persisted on");
            yield return TearDown();

            // The next launch with it on: nothing moves before the start panel is answered, then it is on by itself.
            yield return BootApp(s => s.SetInt(AppSettings.KeyMoveInstruments, 1));
            Assert.IsTrue(_layout.MoveOn, "the setting is read");
            Step(10);
            Assert.IsFalse(_layout.Arranging, "not while the start panel waits");
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Step(3);
            Assert.IsTrue(_layout.Arranging, "on once the player is in the room");
            int resumes = _layout.MoveResumes;

            // The headset comes off: moving stops (the instruments play when it is back) but the setting stays on.
            _app.Events.Publish(new HmdMountEvent { Mounted = false });
            Step(5);
            Assert.IsFalse(_layout.Arranging, "suspended while the headset is off");
            Assert.IsTrue(_layout.MoveOn, "the setting is the player's");
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyMoveInstruments, 0), Is.EqualTo(1));
            _app.Events.Publish(new HmdMountEvent { Mounted = true });
            Step(3);
            Assert.IsTrue(_layout.Arranging, "back on by itself");
            Assert.That(_layout.MoveResumes, Is.EqualTo(resumes + 1));

            // Off by the player: off for good.
            _layout.EndArrange();
            Step(5);
            Assert.IsFalse(_layout.Arranging);
            Assert.IsFalse(_layout.MoveOn);
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyMoveInstruments, 1), Is.EqualTo(0), "persisted off");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator OffAPinchOnAnInstrumentMovesNothing()
        {
            yield return BootApp();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            int bodyEvents = 0;
            System.Action<InstrumentBodyPinchEvent> onBody = _ => bodyEvents++;
            _app.Events.Subscribe(onBody);
            foreach (InstrumentKind k in new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp })
            {
                Rig rig = RigOf(k);
                if (!BodySpot(rig, out Vector3 at)) at = GripOf(rig);
                Vector3 p0 = rig.Group.position;
                Quaternion r0 = rig.Group.rotation;
                _boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.04f));
                Step(2);
                for (int i = 0; i < 40; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + new Vector3(0.002f * Mathf.Sin(i), 0f, 0.002f * Mathf.Cos(i)))); Step(); }
                for (int i = 1; i <= 20; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + new Vector3(0.01f * i, 0f, 0f), 2f * i)); Step(); }
                Assert.IsNull(_layout.HeldRig, k + ": Move instruments off, the pinch grabs nothing");
                Assert.That(Vector3.Distance(rig.Group.position, p0), Is.LessThan(1e-4f), k + " did not move");
                Assert.That(Quaternion.Angle(rig.Group.rotation, r0), Is.LessThan(0.01f), k + " did not turn");
                _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
                Step(30);
            }
            Assert.That(_layout.MoveDrags, Is.EqualTo(0));
            Assert.That(bodyEvents, Is.GreaterThanOrEqualTo(1), "Sunny hears that a body was pinched (tip_move_turn_on)");
            _app.Events.Unsubscribe(onBody);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator OnPinchesAndPokesOnTheKeysPlayNoNote()
        {
            yield return BootApp();
            yield return StartedAlone(InstrumentKind.Piano);
            PianoInstrument piano = null;
            foreach (var m in _app.Modules) if (m is PianoModule pm) piano = pm.Piano;
            Assert.That(piano, Is.Not.Null);
            // A finger pressed through middle C, then a pinch on the keys dragged away.
            PressKey(piano, 60);
            piano.TryGetKey(64, out var e4);
            Vector3 at = piano.transform.TransformPoint(new Vector3(e4.X, 0.04f, 0.06f));
            yield return PickUp(HandSide.Right, at);
            for (int i = 1; i <= 20; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + Flat(_app.Head.forward) * (0.01f * i))); Step(); }
            Drop(HandSide.Right, at + Flat(_app.Head.forward) * 0.2f);
            PressKey(piano, 62);
            Assert.That(PianoOns(), Is.EqualTo(0), "Move instruments on: no note");
            _layout.EndArrange();
            Step(80);   // past the play guard and a new phrase
            PressKey(piano, 62);
            Assert.That(PianoOns(), Is.EqualTo(1), "off again: the piano plays");
        }

        static HandPose Finger(Vector3 tip, Vector3 along) =>
            HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(along, Vector3.up)).WithPointing().WithIndexTipAt(tip);

        /// <summary>A pointing finger pressed down through a key and lifted again (ArrangeIntegrationTests' press).</summary>
        void PressKey(PianoInstrument piano, int midi)
        {
            piano.TryGetKey(midi, out var k);
            for (float y = 0.03f; y >= -0.014f; y -= 0.004f)
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(piano.transform.TransformPoint(new Vector3(k.X, y, 0.03f)), Vector3.forward + Vector3.down));
                Step();
            }
            _boot.Hands.SetPose(HandSide.Right, Finger(piano.transform.TransformPoint(new Vector3(k.X, 0.08f, 0.03f)), Vector3.forward));
            Step(12);
        }

        // ---------------------------------------------------------------- the feel

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ADragStartsWithoutAJumpKeepsItsOffsetAndFollowsSmoothly()
        {
            yield return BootApp();
            yield return StartedAlone(InstrumentKind.Piano);
            Rig piano = RigOf(InstrumentKind.Piano);
            Vector3 grip = GripOf(piano);
            Vector3 p0 = piano.Group.position;
            float yaw0 = piano.Group.eulerAngles.y;
            Vector3 offset = CentreOf(piano) - grip;
            yield return PickUp(HandSide.Left, grip);
            Step(2);
            Assert.That(Vector3.Distance(piano.Group.position, p0), Is.LessThan(0.001f), "no jump at the pick-up");
            Assert.IsTrue(piano.Held, "quiet while held");

            // A still hand with +-3 mm of tracker jitter: the piano stays calmer than the hand.
            var rng = new System.Random(3);
            float maxDev = 0f;
            for (int i = 0; i < 60; i++)
            {
                Vector3 j = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.006f;
                _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, grip + j));
                Step();
                if (i > 10) maxDev = Mathf.Max(maxDev, Vector3.Distance(piano.Group.position, p0));
            }
            Debug.Log($"[MoveDrag] still hand: the piano wandered {maxDev * 1000f:F1} mm (hand jitter up to 5.2 mm)");
            Assert.That(maxDev, Is.LessThan(0.0025f), "jitter calmed");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(piano.Group.eulerAngles.y, yaw0)), Is.LessThan(0.2f), "no turn without a wrist turn");

            // 0.4 m/s away from the player (and the hand drifting 1.5 cm up, inside the lift's dead band) for 0.5 s: followed
            // closely along the floor, at its height (a level drag never bobs; lifting: HeightTests).
            Vector3 fwd = Flat(_app.Head.forward);
            Vector3 start = piano.Group.position;
            float prevAlong = 0f, lag = 0f;
            for (int i = 1; i <= 36; i++)
            {
                float hand = 0.4f * i * Dt;
                _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, grip + fwd * hand + Vector3.up * (0.015f * i / 36f)));
                Step();
                float along = Vector3.Dot(piano.Group.position - start, fwd);
                Assert.That(along, Is.GreaterThanOrEqualTo(prevAlong - 1e-4f), "frame " + i + ": never backward");
                Assert.That(along, Is.LessThanOrEqualTo(hand + 0.002f), "frame " + i + ": never ahead of the hand");
                Assert.That(along - prevAlong, Is.LessThan(0.4f * Dt * 2f), "frame " + i + ": no jump");
                Assert.That(piano.Group.position.y, Is.EqualTo(p0.y).Within(1e-5f), "frame " + i + ": at its stand height");
                prevAlong = along;
                if (i > 12) lag = Mathf.Max(lag, hand - along);
            }
            Debug.Log($"[MoveDrag] 0.4 m/s: lag {lag * 1000f:F1} mm");
            Assert.That(lag, Is.LessThan(0.015f), "within 1.5 cm");
            Step(12);
            float total = 0.4f * 36 * Dt;
            Assert.That(Vector3.Dot(piano.Group.position - start, fwd), Is.EqualTo(total).Within(0.004f), "caught up, 1:1, after the hand stops");
            Vector3 hNow = grip + fwd * total;
            Assert.That(FlatDistance(CentreOf(piano) - hNow, offset), Is.LessThan(0.01f), "the grab offset is kept: the spot pinched stays in the hand");
            Assert.That(Vector3.Angle(piano.Group.up, Vector3.up), Is.LessThan(0.01f), "never tilted");
            Drop(HandSide.Left, hNow);
            _layout.EndArrange();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AFlickerALoosePinchOrATrackingGapDoNotDropIt()
        {
            yield return BootApp();
            yield return StartedAlone(InstrumentKind.Piano);
            Rig piano = RigOf(InstrumentKind.Piano);
            Vector3 at = GripOf(piano);
            yield return PickUp(HandSide.Right, at);
            Step(4);
            int drags = _layout.MoveDrags;
            // two frames of an open hand (a flicker)
            _boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, at));
            Step(2);
            Assert.IsTrue(_layout.MoveDragging, "a two-frame flicker keeps the drag");
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            Step(3);
            // a loosened pinch (thumb 3.5 cm off the index tip: past the 25 mm end of the pinch rule) for 0.4 s
            for (int i = 0; i < 30; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at, 0f, 0.035f)); Step(); }
            Assert.IsTrue(_layout.MoveDragging, "a loosened pinch still holds");
            // a 0.15 s tracking gap; the hand comes back 4 cm away: no jump, it carries on from there
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            Step(3);
            Vector3 before = piano.Group.position;
            _boot.Hands.SetTracked(HandSide.Right, false);
            Step(11);
            Assert.IsTrue(_layout.MoveDragging, "a short tracking gap keeps the drag");
            Assert.That(Vector3.Distance(piano.Group.position, before), Is.LessThan(1e-4f), "and the piano waits");
            Vector3 back = at + new Vector3(0.04f, 0f, 0f);
            _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, back));
            _boot.Hands.SetTracked(HandSide.Right, true);
            Step();
            Assert.IsTrue(_layout.MoveDragging);
            Assert.That(Vector3.Distance(piano.Group.position, before), Is.LessThan(0.002f), "no jump when the hand picks it up again");
            Vector3 fwd = Flat(_app.Head.forward);
            for (int i = 1; i <= 10; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, back + fwd * (0.005f * i))); Step(); }
            Step(10);
            Assert.That(Vector3.Dot(piano.Group.position - before, fwd), Is.EqualTo(0.05f).Within(0.006f), "and follows from there");
            Assert.That(_layout.MoveDrags, Is.EqualTo(drags), "the same drag all along");
            Drop(HandSide.Right, back + fwd * 0.05f);
            // a long tracking loss drops it too
            at = GripOf(piano);
            yield return PickUp(HandSide.Right, at);
            _boot.Hands.SetTracked(HandSide.Right, false);
            Step(30);
            Assert.IsFalse(_layout.MoveDragging, "a lost hand drops it after 0.3 s");
            _boot.Hands.SetTracked(HandSide.Right, true);
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            Settle();
            _layout.EndArrange();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheWristSpinsItYawOnlyAllTheWayRound()
        {
            yield return BootApp();
            yield return StartedAlone(InstrumentKind.Handpan);
            Rig pan = RigOf(InstrumentKind.Handpan);
            Vector3 c0 = CentreOf(pan);
            Vector3 at = GripOf(pan);
            yield return PickUp(HandSide.Left, at);
            float yaw0 = pan.Group.eulerAngles.y;
            // A 5 degree roll of the wrist (a closing pinch): no turn.
            for (int i = 1; i <= 10; i++) { _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at, 0.5f * i)); Step(); }
            Step(20);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(pan.Group.eulerAngles.y, yaw0)), Is.LessThan(0.5f), "a 5 degree roll: no turn");
            // 36 degrees: it spins TurnGain times the turn past the dead band.
            for (int i = 1; i <= 31; i++) { _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at, 5f + i)); Step(); }
            Step(40);
            float turned = Mathf.DeltaAngle(yaw0, pan.Group.eulerAngles.y);
            float want = MoveConstants.TurnGain * (36f - MoveConstants.TurnDeadband * Mathf.Rad2Deg);
            Debug.Log($"[MoveDrag] wrist 36 degrees: the handpan turned {turned:F1} degrees (want {want:F1})");
            Assert.That(Mathf.Abs(turned), Is.EqualTo(want).Within(5f), "a clear turn spins it");
            Assert.That(Vector3.Angle(pan.Group.up, Vector3.up), Is.LessThan(0.01f), "yaw only");
            Assert.That(FlatDistance(CentreOf(pan), c0), Is.LessThan(0.03f), "about its own centre");
            Drop(HandSide.Left, at, 36f);

            // All the way round: three grabs, each a 90 degree wrist turn, add up to more than a full turn.
            float total = 0f, prev = pan.Group.eulerAngles.y;
            for (int g = 0; g < 3; g++)
            {
                _boot.Hands.SetPose(HandSide.Left, OpenAt(HandSide.Left, at + Vector3.up * 0.05f));
                Step(4);
                at = GripOf(pan);
                yield return PickUp(HandSide.Left, at);
                Vector3 cg = CentreOf(pan);
                for (int i = 1; i <= 45; i++)
                {
                    _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, at, 2f * i));
                    Step();
                    float y = pan.Group.eulerAngles.y;
                    total += Mathf.DeltaAngle(prev, y);
                    prev = y;
                }
                for (int i = 0; i < 30; i++) { Step(); float y = pan.Group.eulerAngles.y; total += Mathf.DeltaAngle(prev, y); prev = y; }
                Assert.That(FlatDistance(CentreOf(pan), cg), Is.LessThan(0.03f), "grab " + g + ": spun in place, about its centre");
                Drop(HandSide.Left, at, 90f);
                float yd = pan.Group.eulerAngles.y;
                total += Mathf.DeltaAngle(prev, yd);
                prev = yd;
            }
            Debug.Log($"[MoveDrag] three 90 degree wrist turns: {total:F0} degrees in all");
            Assert.That(Mathf.Abs(total), Is.GreaterThanOrEqualTo(360f), "all the way round");
            Assert.That(Vector3.Angle(pan.Group.up, Vector3.up), Is.LessThan(0.01f), "still upright");
            _layout.EndArrange();
            Step(2);
            Assert.IsTrue(_layout.TryGetOffset(RigId.Handpan, out ComfortOffset o), "the turn is kept");
            Assert.That(Mathf.Abs(o.Yaw), Is.LessThanOrEqualTo(180f + 1e-3f), "as an angle in (-180, 180]");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TwoHandsTwistItAboutItsCentre()
        {
            yield return BootApp();
            yield return StartedAlone(InstrumentKind.Handpan);
            Rig pan = RigOf(InstrumentKind.Handpan);
            Vector3 c = CentreOf(pan);
            Vector3 a = GripOf(pan);
            yield return PickUp(HandSide.Right, a);
            Vector3 ra = a - c; ra.y = 0f;
            Vector3 b = c - ra; b.y = a.y;
            _boot.Hands.SetPose(HandSide.Left, OpenAt(HandSide.Left, b + Vector3.up * 0.03f));
            Step(2);
            _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, b));
            Step(2);
            Assert.That(_layout.SecondHand, Is.SameAs(_app.Input.Hand(Handedness.Left)), "the second hand joins");
            float y0 = pan.Group.eulerAngles.y;
            for (int i = 1; i <= 60; i++)
            {
                Quaternion q = Quaternion.Euler(0f, 120f * i / 60f, 0f);
                Vector3 pa = c + q * ra; pa.y = a.y;
                Vector3 pb = c - q * ra; pb.y = a.y;
                _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, pa));
                _boot.Hands.SetPose(HandSide.Left, PinchAt(HandSide.Left, pb));
                Step();
            }
            Step(10);
            Assert.That(Mathf.DeltaAngle(y0, pan.Group.eulerAngles.y), Is.EqualTo(120f).Within(6f), "a two-hand twist, 1:1");
            Assert.That(FlatDistance(CentreOf(pan), c), Is.LessThan(0.04f), "about its centre");
            Assert.That(Vector3.Angle(pan.Group.up, Vector3.up), Is.LessThan(0.01f), "yaw only");
            // The second hand lets go: the first carries on without a jump.
            Vector3 hold = pan.Group.position;
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            Step(3);
            Assert.IsTrue(_layout.MoveDragging);
            Assert.That(Vector3.Distance(pan.Group.position, hold), Is.LessThan(0.003f), "no jump when one hand lets go");
            Quaternion qe = Quaternion.Euler(0f, 120f, 0f);
            Vector3 pe = c + qe * ra; pe.y = a.y;
            Drop(HandSide.Right, pe);
            _layout.EndArrange();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ADropIntoAnotherInstrumentEasesOutOfItAndIsSaved()
        {
            yield return BootApp();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            _layout.BeginArrange();
            Step(2);
            Rig kit = RigOf(InstrumentKind.Drums), piano = RigOf(InstrumentKind.Piano);
            Vector3 at = GripOf(kit);
            yield return PickUp(HandSide.Right, at);
            Vector3 into = piano.Group.position - kit.Group.position;
            into.y = 0f;
            for (int i = 1; i <= 72; i++) { _boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, at + into * Mathf.Min(1f, i / 30f))); Step(); }
            Assert.IsTrue(_layout.PassingThrough, "pushed on, it went through (red)");
            int settles = _layout.Settles, saves = _layout.ArrangeSaves;
            _boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + into));
            Step(Mathf.CeilToInt(MoveConstants.ReleaseGrace / Dt) + 1);
            Assert.IsFalse(_layout.MoveDragging);
            Assert.IsTrue(_layout.Settling, "it eases out instead of jumping");
            Assert.That(_layout.Settles, Is.EqualTo(settles + 1));
            Assert.That(_layout.ArrangeSaves, Is.EqualTo(saves + 1), "the drop is saved");
            Vector3 from = kit.Group.position;
            var path = new List<Vector3> { from };
            for (int i = 0; i < 40 && _layout.Settling; i++) { Step(); path.Add(kit.Group.position); }
            Assert.IsFalse(_layout.Settling, "settled within " + MoveConstants.SettleSeconds + " s");
            Vector3 end = kit.Group.position;
            Assert.That(path.Count, Is.InRange(Mathf.FloorToInt(MoveConstants.SettleSeconds / Dt) - 6, Mathf.CeilToInt(MoveConstants.SettleSeconds / Dt) + 3), "about " + MoveConstants.SettleSeconds + " s");
            for (int i = 1; i < path.Count; i++)
                Assert.That(Vector3.Distance(path[i], end), Is.LessThanOrEqualTo(Vector3.Distance(path[i - 1], end) + 1e-5f), "frame " + i + ": always closer");
            Assert.That(Vector3.Distance(path[1], path[0]), Is.LessThan(Vector3.Distance(from, end) * 0.25f), "a gentle start, no jump");
            Assert.That(_layout.Placement.Overlaps2D(), Is.EqualTo(0), "clear of the piano");
            _layout.EndArrange();
        }
    }
}
