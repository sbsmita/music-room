// The drums in the running app (ARCHITECTURE.md sections 15.2 and 16): booted through TestBoot (scripted hands, a fresh
// settings store and data folder) with a simulated headset, so App.IsPresenting is true for the XR-only paths (the nod
// kick), and manual frames, so every step is exactly 1/72 s of hands, clock and one App.Frame. The real frame order and
// the real audio engine, observed through IAudioEngine.TestTap.Calls (the engine's API calls, not its samples, so a retuned
// recipe never breaks these tests), and the real recorder's RecorderEvent stream. The real Layout module makes the stick
// pick-up call (IDrumsApi.TryPickUp in its Grab pass, section 15.2); a probe in front of the kit's IDrumsApi records it.
// Covers registration and source parenting, a finger strike, the hi-hat stand pinch and its chick, the stick pick-up / strike / release / settle, the tracking-loss grace,
// the choke timing, the nod kick (and none without a headset), the kit leaving the layout with its sources still
// rendering, and the allocation-free hot paths.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Drums
{
    public class DrumsPlayTests
    {
        const float Dt = 1f / 72f;
        static float[] s_keep;

        /// Watches the real Layout's side of the stick pick-up (IDrumsApi.TryPickUp contract). Registered
        /// as IDrumsApi before the Drums module (Order 30 < 31: App.Get returns the first registered service), it forwards
        /// every call to the kit and records what Layout's Grab pass asked: the verdict and whether Layout consumes the
        /// pinch (Grabbed, or Near with no rig handle within .02). Enabled = false answers None without asking the kit, as if
        /// Layout's pass were off. (In the module clone a probe played Layout's part; the integrated app has the real one.)
        sealed class LayoutGrabProbe : AppModule, IDrumsApi
        {
            App _app;
            IDrumsApi _kit;
            public bool Enabled = true;
            public StickPick Last = StickPick.None;
            public bool Called, Consumed;
            public override int Order => 30;
            public override void Init(App app) { _app = app; app.Register<IDrumsApi>(this); }

            IDrumsApi Kit
            {
                get
                {
                    if (_kit != null) return _kit;
                    var all = _app.GetAll<IDrumsApi>();
                    for (int i = 0; i < all.Count; i++) if (!ReferenceEquals(all[i], this)) return _kit = all[i];
                    return null;
                }
            }

            public StickPick TryPickUp(HandState hand, Vector3 tip)
            {
                if (!Enabled) return StickPick.None;
                Last = Kit.TryPickUp(hand, tip);
                Called = true;
                var grab = _app.Get<IGrabService>();
                Consumed = Last == StickPick.Grabbed || (Last == StickPick.Near && (grab == null || grab.GrabTarget(tip, 0.02f) == null));
                return Last;
            }
            public Transform Root => Kit.Root;
            public IReadOnlyList<DrumPieceInfo> Pieces => Kit.Pieces;
            public int KickId => Kit.KickId;
            public int KickPadId => Kit.KickPadId;
            public int HatId => Kit.HatId;
            public bool HatOpen => Kit.HatOpen;
            public void SetHatOpen(bool open, HandState hand, bool playback = false) => Kit.SetHatOpen(open, hand, playback);
            public void PlaybackHit(string kind, float vel, DrumStrikeInfo strike) => Kit.PlaybackHit(kind, vel, strike);
            public void PlaybackChoke(string kind) => Kit.PlaybackChoke(kind);
            public bool AnyStickHeld => Kit.AnyStickHeld;
            public void ReleaseAllSticks() => Kit.ReleaseAllSticks();
            public bool EveryHandPointerFartherThan(float metres, int pieceId) => Kit.EveryHandPointerFartherThan(metres, pieceId);
        }

        TestBoot _boot;
        LayoutGrabProbe _probe;
        App _app;
        DrumsModule _mod;
        DrumKit _kit;
        IAudioEngine _audio;
        AudioTestTap _tap;
        readonly List<RecorderEvent> _events = new List<RecorderEvent>();
        readonly List<NodEvent> _nods = new List<NodEvent>();
        Action<RecorderEvent> _onRecorder;
        Action<NodEvent> _onNod;

        /// Boots, waits (real time) for the desktop rig to own the head, then steps until the first arrange.
        IEnumerator Boot(bool simulateHeadset = true)
        {
            _boot = new TestBoot(simulateHeadset: simulateHeadset, manualFrames: true);
            _probe = new LayoutGrabProbe();
            _boot.Options.ExtraModules.Add(_probe);
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is DrumsModule d) _mod = d;
            Assert.That(_mod, Is.Not.Null, "DrumsModule discovered");
            _kit = _mod.Kit;
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(_app.Rig.State, Is.EqualTo(XrState.Desktop), "no headset: the desktop camera is the head");
            yield return null;   // the desktop fallback places the head
            var layout = _app.Get<ILayoutService>();
            for (int i = 0; i < 10 && !layout.FirstArrangeDone; i++) _boot.Step(Dt);
            Assert.IsTrue(layout.FirstArrangeDone, "the desktop rig arranged the room");
            Assert.That(_app.IsPresenting, Is.EqualTo(simulateHeadset));
            _audio = _app.Get<IAudioEngine>();
            _tap = new AudioTestTap();
            _audio.TestTap = _tap;
            _events.Clear();
            _nods.Clear();
            _onRecorder = e => _events.Add(e);
            _onNod = e => _nods.Add(e);
            _app.Events.Subscribe(_onRecorder);
            _app.Events.Subscribe(_onNod);
            Step();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_audio != null) _audio.TestTap = null;
            _audio = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            _mod = null;
            _kit = null;
        }

        // ------------------------------------------------------------------ helpers

        HandState Right => _app.Input.Hand(Handedness.Right);

        void Step(int n = 1) => _boot.Step(Dt, n);

        void Pose(in HandPose p) => _boot.Hands.SetPose(HandSide.Right, p);

        /// A right hand in the pointing pose with its index tip at a world point (pinched: thumb on it).
        static HandPose PoseAt(Vector3 indexTip, Quaternion rot, bool pinched)
        {
            var p = HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing();
            if (pinched) p = p.WithPinch(0f);
            return p.WithIndexTipAt(indexTip);
        }

        int Count(string type, string kind = null)
        {
            int n = 0;
            foreach (var e in _events) if (e.Event.type == type && (kind == null || e.Event.kind == kind) && !e.DuringPlayback) n++;
            return n;
        }

        RecorderEvent Last(string type)
        {
            for (int i = _events.Count - 1; i >= 0; i--) if (_events[i].Event.type == type) return _events[i];
            Assert.Fail("no " + type + " event");
            return default;
        }

        /// The calls of one AudioCall method, in order.
        List<AudioTestTap.Call> Calls(AudioCall method)
        {
            var list = new List<AudioTestTap.Call>();
            foreach (var c in _tap.Calls) if (c.Method == method) list.Add(c);
            return list;
        }

        /// Where the stick tip ends up for a hand pose (the module's own grip formula).
        static Vector3 StickTipFor(in HandPose p)
        {
            DrumMath.StickGrip(p.Position[(int)HandJoint.Wrist], p.Rotation[(int)HandJoint.Wrist], p.Position[(int)HandJoint.ThumbTip],
                p.Position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
            DrumMath.StickPose(grip, axis, out _, out _, out Vector3 tip, out _);
            return tip;
        }

        /// The pose translated so the held stick's tip lands on <paramref name="tip"/>.
        static HandPose WithStickTipAt(in HandPose p, Vector3 tip) => p.Translated(tip - StickTipFor(p));

        /// Open hand at <paramref name="at"/> for two frames, then a pinch there (the PinchStart frame).
        StickPick PinchAt(Vector3 at, Quaternion rot)
        {
            Pose(PoseAt(at, rot, false));
            Step(2);
            _probe.Called = false;
            _probe.Last = StickPick.None;
            Pose(PoseAt(at, rot, true));
            Step();
            Assert.IsTrue(_probe.Called, "Layout's grab pass saw the pinch start");
            return _probe.Last;
        }

        void PickUpStick(Quaternion rot)
        {
            Drumstick s = _mod.Sticks[1];
            Assert.That(PinchAt(s.GripWorld + Vector3.up * 0.02f, rot), Is.EqualTo(StickPick.Grabbed));
            Assert.That(Right.Holding, Is.SameAs(s), "the pinch picked up a stick");
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator TheModuleRegistersTheKitItsSourcesAndTheStickPointers()
        {
            yield return Boot();
            Assert.That(_app.GetAll<IDrumsApi>(), Has.Member(_kit), "the kit is the IDrumsApi (the test's probe registers in front of it)");
            Assert.That(_app.Instruments, Has.Member(_kit));
            Assert.That(_kit.TickOrder, Is.EqualTo(10));
            Assert.IsTrue(_kit.AcceptsStickPointers);
            Assert.That(_mod.Order, Is.EqualTo(31));
            Rig rig = _app.Get<IRigRegistry>().Get(RigId.Drums);
            Assert.That(rig, Is.SameAs(_kit.Rig));
            Assert.That(rig.Group.eulerAngles.y, Is.EqualTo(90f).Within(0.01f), "arranged to the player's right");
            // At its home to the right (0.2, 0, -0.26), pulled straight toward the player into the reach band: its nearest
            // head within a foot of the chest (the instruments open within reach since 2026-11-02).
            var layout = _app.Get<ILayoutService>() as MusicRoom.Layout.LayoutModule;
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.ReachOf(rig), Is.LessThanOrEqualTo(0.6f), "near the player (a side member of My set: pulled in as far as the piano and the side table allow)");
            Vector3 seat = new Vector3(_app.Head.position.x, 0f, _app.Head.position.z);
            Assert.That(Vector3.Distance(rig.Group.position, seat + new Vector3(0.2f, 0f, -0.26f)), Is.LessThan(0.35f), "near its home: at " + rig.Group.position.ToString("F3") + ", head " + _app.Head.position.ToString("F3"));
            Assert.That(rig.Group.position.y, Is.EqualTo(0f).Within(1e-3f), "on the floor");
            Assert.That(_app.Clock.Dt, Is.EqualTo(Dt).Within(1e-6f), "manual frames drive the app");

            for (int i = 0; i < DrumSpec.PieceCount; i++)
            {
                PieceSpec spec = DrumSpec.Pieces[i];
                if (spec.Pad) continue;
                Transform src = _audio.Source(spec.Source).transform;
                Assert.That(src.parent, Is.SameAs(_kit.Piece(i).Group), spec.Kind + " source on its piece");
                Assert.That(src.localPosition, Is.EqualTo(Vector3.zero));
            }
            Transform room = _audio.Source(SourceId.DrumRoom).transform;
            Assert.That(room.parent, Is.SameAs(_kit.transform), "drum room at the kit centre");
            Assert.That(Vector3.Distance(room.localPosition, new Vector3(0.05f, 0.8f, 0.4f)), Is.LessThan(1e-6f));

            int sticks = 0;
            foreach (var p in _app.Input.ExternalPointers) if (p.Stick) sticks++;
            Assert.That(sticks, Is.EqualTo(4), "two sticks x (tip, shoulder)");
            foreach (var p in _app.Input.HandPointers) Assert.IsFalse(p.External);
            Assert.That(_kit.PlayedParts.Count, Is.EqualTo(17));
            Assert.That(rig.Handles.Count, Is.EqualTo(28));
            foreach (var r in rig.Handles) Assert.That(_kit.PlayedParts, Has.No.Member(r));
            Assert.IsTrue(_kit.IsPresent);
        }

        [UnityTest]
        public IEnumerator AScriptedFingerStrikeOnTheSnareSoundsAndRecords()
        {
            yield return Boot();
            DrumPiece snare = _kit.Piece(DrumSpec.SnareId);
            Quaternion down = Quaternion.LookRotation(Vector3.down, _kit.transform.forward);
            foreach (float y in new[] { 0.06f, 0.04f, 0.02f })
            {
                Pose(PoseAt(snare.Group.TransformPoint(new Vector3(0.03f, y, 0f)), down, false));
                Step();
            }
            Assert.That(Count(TakeEventType.Drum), Is.EqualTo(0), "hovering is silent");
            Assert.That(_app.HoverGlow.Drawn, Is.GreaterThan(0), "the head under the fingertip glows");
            _tap.Clear();
            Pose(PoseAt(snare.Group.TransformPoint(new Vector3(0.03f, -0.01f, 0f)), down, false));
            Step();
            Assert.That(Count(TakeEventType.Drum, "snare"), Is.EqualTo(1), "one hit");
            TakeEvent ev = Last(TakeEventType.Drum).Event;
            float want = DrumMath.StrikeVelocity(0.03f / Dt, false);
            Assert.That(ev.zone, Is.EqualTo("head"));
            Assert.That(ev.pos, Is.EqualTo(0.03f / 0.18f).Within(0.02f));
            Assert.That(ev.vel, Is.EqualTo(want).Within(0.02f), "3 cm in one 1/72 s sample");
            var drums = Calls(AudioCall.Drum);
            Assert.That(drums.Count, Is.EqualTo(1), "one IAudioEngine.Drum call");
            Assert.That(drums[0].Index, Is.EqualTo((int)DrumKind.Snare));
            Assert.That(drums[0].Vel, Is.EqualTo(want).Within(0.02f));
            Assert.That(drums[0].Strike.Zone, Is.EqualTo(DrumZone.Head));
            Assert.That(drums[0].Strike.Pos, Is.EqualTo(ev.pos).Within(1e-6f), "the recorded pos is the one sounded");
            Assert.That(drums[0].When, Is.EqualTo(0.0), "a live strike plays as soon as possible");
            Assert.IsTrue(PointerInput.IsPlaying(Right, _app.Clock.NowMs), "a drumming hand does not grab");
            Step(3);
            Assert.That(Count(TakeEventType.Drum), Is.EqualTo(1), "parked below the head: no retrigger");
            Assert.That(_tap.Count(AudioCall.Drum), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PinchingTheHiHatStandOpensItAndThePedalChickClosesIt()
        {
            yield return Boot();
            Vector3 pole = _kit.transform.TransformPoint(new Vector3(-0.42f, 0.5f, 0.36f));
            Quaternion rot = Quaternion.LookRotation(_kit.transform.forward, Vector3.up);
            Assert.That(_kit.PlaySurfaceAt(pole), Is.EqualTo(PlaySurface.HatStand));
            PinchAt(pole, rot);
            Assert.IsTrue(_kit.HatOpen);
            Assert.That(Count(TakeEventType.Hat), Is.EqualTo(1));
            Assert.IsTrue(Last(TakeEventType.Hat).Event.open);
            Assert.That(_probe.Last, Is.EqualTo(StickPick.None), "the hat stand is far from the sticks");
            Step(12);
            Assert.That(_kit.HatPiece.Lower.localPosition.y, Is.EqualTo(-0.05f).Within(0.002f), "the lower plate dropped open");
            Pose(PoseAt(pole, rot, false));
            Step(2);
            _tap.Clear();
            Pose(PoseAt(pole, rot, true));
            Step();
            Assert.IsFalse(_kit.HatOpen);
            Assert.IsFalse(Last(TakeEventType.Hat).Event.open);
            var chokes = Calls(AudioCall.Choke);
            Assert.That(chokes.Count, Is.EqualTo(1), "closing chokes the wash");
            Assert.That(chokes[0].Drum, Is.EqualTo(DrumKind.Hat));
            var drums = Calls(AudioCall.Drum);
            Assert.That(drums.Count, Is.EqualTo(1), "and makes the chick");
            Assert.That(drums[0].Drum, Is.EqualTo(DrumKind.HatClose));
            Assert.That(drums[0].Vel, Is.EqualTo(0.7f).Within(1e-6f));
        }

        [UnityTest]
        public IEnumerator TryPickUpAsLayoutCallsItGrabsNearOrNothingByDistance()
        {
            // ARCHITECTURE.md 15.2: a scripted stick pick-up through IDrumsApi.TryPickUp, called the way Layout calls it.
            yield return Boot();
            Quaternion rot = Quaternion.LookRotation(_kit.transform.forward, Vector3.up);
            Vector3 grip = _mod.Sticks[0].GripWorld;
            Assert.That(PinchAt(grip + Vector3.up * 0.35f, rot), Is.EqualTo(StickPick.None), "35 cm above a stick");
            Assert.IsFalse(_probe.Consumed, "the pinch stays Layout's to judge");
            Assert.That(PinchAt(grip + Vector3.up * 0.15f, rot), Is.EqualTo(StickPick.Near), "15 cm: inside the .24 m dead zone");
            Assert.IsTrue(_probe.Consumed, "no rig handle within .02: Layout consumes it");
            Assert.IsNull(Right.Holding);
            Assert.That(PinchAt(grip + Vector3.up * 0.02f, rot), Is.EqualTo(StickPick.Grabbed), "2 cm from the shaft");
            Assert.That(Right.Holding, Is.SameAs(_mod.Sticks[0]));
            Assert.That(_mod.Sticks[0].Root.parent, Is.Not.SameAs(_kit.transform), "carried in the scene, not by the kit");
            Assert.IsTrue(_mod.Sticks[0].TipPointer.Down);
            _kit.ReleaseAllSticks();
            Assert.IsNull(Right.Holding);
            // Drums never consumes a pinch itself: with Layout's pass off, nothing picks a stick up.
            _probe.Enabled = false;
            Pose(PoseAt(grip + Vector3.up * 0.02f, rot, false));
            Step(2);
            Pose(PoseAt(grip + Vector3.up * 0.02f, rot, true));
            Step();
            Assert.IsNull(Right.Holding, "the module runs no pick-up of its own");
        }

        [UnityTest]
        public IEnumerator MetaPinchAlonePicksUpAStickHoldsItAndTogglesTheHat()
        {
            // On the headset the thumb-index rule flickers (the two tip joints sit ~17 mm apart when the fingertips touch,
            // above its 15 mm start): the pick-up (Layout's grab pass), the hold and the hi-hat stand read the grab pinch
            // (HandState.GrabPinch*: the rule OR Meta's own index pinch).
            yield return Boot();
            Quaternion rot = Quaternion.LookRotation(_kit.transform.forward, Vector3.up);
            Drumstick s = _mod.Sticks[1];
            Vector3 at = s.GripWorld + Vector3.up * 0.02f;
            Pose(PoseAt(at, rot, false));
            Step(2);
            _probe.Called = false;
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.95f);
            Step();
            Assert.IsFalse(Right.Pinching, "the thumb-index rule sees no pinch (fingers apart)");
            Assert.IsTrue(_probe.Called, "Layout's grab pass saw the grab pinch start");
            Assert.That(_probe.Last, Is.EqualTo(StickPick.Grabbed));
            // (The fingers are apart, so the pinch point is the thumb-index midpoint: it may take either stick.)
            var held = Right.Holding as Drumstick;
            Assert.That(held, Is.Not.Null, "Meta's pinch picked a stick up");
            // Carried high above the kit with the fingertips apart while Meta's pinch holds: the stick stays in the hand.
            Pose(PoseAt(_kit.transform.position + Vector3.up * 1.2f, rot, false));
            Step(40);
            Assert.That(Right.Holding, Is.SameAs(held), "Meta's pinch keeps the stick");
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.2f);
            Step(22);
            Assert.IsNull(Right.Holding, "released once both pinches have parted for .25 s");
            Step(36);
            // The hi-hat stand: a Meta pinch on the pole toggles it.
            Vector3 pole = _kit.transform.TransformPoint(new Vector3(-0.42f, 0.5f, 0.36f));
            Pose(PoseAt(pole, rot, false));
            Step(2);
            bool open0 = _kit.HatOpen;
            _boot.Hands.SetMetaPinch(HandSide.Right, 0.95f);
            Step();
            Assert.That(_kit.HatOpen, Is.EqualTo(!open0), "a Meta pinch on the stand toggles the hat");
            Assert.IsFalse(Right.Pinching);
        }

        [UnityTest]
        public IEnumerator AScriptedStickPickUpStrikeReleaseAndSettle()
        {
            yield return Boot();
            Quaternion rot = Quaternion.LookRotation(_kit.transform.TransformDirection(new Vector3(0f, -0.6f, 1f).normalized), Vector3.up);
            PickUpStick(rot);
            var stick = (Drumstick)Right.Holding;

            // The stick follows the hand in the same frame: StickPoses runs before PointerInput reads the tip.
            DrumPiece snare = _kit.Piece(DrumSpec.SnareId);
            HandPose hold = PoseAt(Vector3.zero, rot, true);
            Pose(WithStickTipAt(hold, snare.Group.TransformPoint(new Vector3(0.02f, 0.08f, 0.02f))));
            Step();
            foreach (var p in Right.Pointers) Assert.IsFalse(p.Active, "a hand holding a stick is muted");
            HandFrame f = _app.Input.Frame(Right);
            DrumMath.StickGrip(f.position[(int)HandJoint.Wrist], f.rotation[(int)HandJoint.Wrist], f.position[(int)HandJoint.ThumbTip],
                f.position[(int)HandJoint.IndexTip], out Vector3 grip, out Vector3 axis);
            Assert.That(Vector3.Distance(stick.TipPointer.Tip, grip + axis * 0.27f), Is.LessThan(1e-4f), "tip pointer = this frame's grip");
            Assert.IsTrue(stick.TipPointer.Active);

            // Down through the head, moving every frame so the sample interval is one frame.
            foreach (float y in new[] { 0.06f, 0.04f, 0.01f })
            {
                Pose(WithStickTipAt(hold, snare.Group.TransformPoint(new Vector3(0.02f, y, 0.02f))));
                Step();
            }
            Assert.That(Count(TakeEventType.Drum), Is.EqualTo(0));
            _tap.Clear();
            Pose(WithStickTipAt(hold, snare.Group.TransformPoint(new Vector3(0.02f, -0.01f, 0.02f))));
            Step();
            Assert.That(Count(TakeEventType.Drum, "snare"), Is.EqualTo(1), "the stick tip struck the snare");
            float want = DrumMath.StrikeVelocity(0.02f / Dt, true);
            Assert.That(Last(TakeEventType.Drum).Event.vel, Is.EqualTo(want).Within(0.02f), "stick curve: 1.44 m/s");
            var drums = Calls(AudioCall.Drum);
            Assert.That(drums.Count, Is.EqualTo(1));
            Assert.That(drums[0].Drum, Is.EqualTo(DrumKind.Snare));
            Assert.That(drums[0].Vel, Is.EqualTo(want).Within(0.02f));
            Assert.That(drums[0].Strike.Zone, Is.EqualTo(DrumZone.Head));
            Assert.That(stick.LastPlayMs, Is.EqualTo(_app.Clock.NowMs).Within(1.0), "stick hits stamp the stick");
            Assert.IsFalse(PointerInput.IsPlaying(Right, _app.Clock.NowMs), "not the hand: it may still grab with the stick in it");

            // Open the hand (high above the kit, so the stick it still holds touches nothing): let go after .25 s, then
            // back on the snare in .4 s.
            Pose(PoseAt(snare.Group.position + Vector3.up * 0.5f, rot, false));
            Step(10);
            Assert.That(Right.Holding, Is.SameAs(stick), "a relaxed grip keeps the stick");
            Step(12);
            Assert.IsNull(Right.Holding, "released after .25 s open");
            Assert.That(stick.Root.parent, Is.SameAs(_kit.transform));
            Step(32);
            Assert.That(Vector3.Distance(stick.Root.localPosition, stick.RestPos), Is.LessThan(1e-5f), "settled onto the snare");
            Assert.That(Quaternion.Angle(stick.Root.localRotation, stick.RestRot), Is.LessThan(0.01f));
            Assert.IsTrue(Right.Pointers[0].Active, "the free hand plays again");
        }

        [UnityTest]
        public IEnumerator ALostHandFreezesItsStickAndDropsItAfterOnePointFiveSeconds()
        {
            yield return Boot();
            PickUpStick(Quaternion.LookRotation(_kit.transform.forward, Vector3.up));
            var stick = (Drumstick)Right.Holding;
            Step(2);
            Vector3 at = stick.Root.position;
            _boot.Hands.SetTracked(HandSide.Right, false);
            Step(90);
            Assert.That(Right.Holding, Is.SameAs(stick), "1.25 s: still held");
            Assert.IsFalse(stick.TipPointer.Active, "silent while lost");
            Assert.That(Vector3.Distance(stick.Root.position, at), Is.LessThan(1e-6f), "frozen");
            Step(30);
            Assert.IsNull(Right.Holding, "dropped after the 1.5 s grace");
            Assert.That(stick.Root.parent, Is.SameAs(_kit.transform));
        }

        [UnityTest]
        public IEnumerator AStillHandOnTheRingingCrashChokesItAfterSixtyMilliseconds()
        {
            yield return Boot();
            DrumPiece crash = _kit.Piece(DrumSpec.CrashId);
            Quaternion down = Quaternion.LookRotation(-crash.Group.up, _kit.transform.forward);
            foreach (float y in new[] { 0.05f, 0.03f, -0.01f })
            {
                Pose(PoseAt(crash.Group.TransformPoint(new Vector3(0.08f, y, 0f)), down, false));
                Step();
            }
            Assert.That(Count(TakeEventType.Drum, "crash"), Is.EqualTo(1));
            Assert.That(Last(TakeEventType.Drum).Event.zone, Is.EqualTo("bow"));
            Assert.That(Calls(AudioCall.Drum)[0].Strike.Zone, Is.EqualTo(DrumZone.Bow));
            _tap.Clear();
            Step(3);   // 42 ms: the stroke itself is not a choke
            Assert.That(Count(TakeEventType.DrumChoke), Is.EqualTo(0));
            Assert.That(_tap.Count(AudioCall.Choke), Is.EqualTo(0));
            Step(3);   // past 60 ms with the hand still on the plate
            Assert.That(Count(TakeEventType.DrumChoke, "crash"), Is.EqualTo(1));
            var chokes = Calls(AudioCall.Choke);
            Assert.That(chokes.Count, Is.EqualTo(1));
            Assert.That(chokes[0].Drum, Is.EqualTo(DrumKind.Crash));
            Assert.IsTrue(crash.Choked, "the hand on the plate damps its rocking (E7)");
            Step(5);
            Assert.That(Count(TakeEventType.DrumChoke), Is.EqualTo(1), "once per ring");
        }

        /// One nod frame: the desktop fallback writes the head rotation in Unity's Update, then the app steps.
        IEnumerator NodFrame(DesktopRigFallback desk, float dPitch)
        {
            desk.pitch += dPitch;
            yield return null;
            Step();
        }

        [UnityTest]
        public IEnumerator AQuickNodPlaysTheKickAndTellsSunny()
        {
            yield return Boot();
            var desk = _app.Rig.Desktop;
            Assert.IsTrue(desk.enabled, "desktop camera owns the head");
            for (int i = 0; i < 3; i++) yield return NodFrame(desk, 0f);
            Assert.That(_nods.Count, Is.EqualTo(0), "a still head never kicks");
            for (int i = 0; i < 6; i++) yield return NodFrame(desk, 3f);   // 216 deg/s = 3.8 rad/s down
            Assert.That(_nods.Count, Is.EqualTo(1), "one kick per nod");
            float want = DrumMath.NodVelocity(3f * 72f * Mathf.Deg2Rad);
            Assert.That(_nods[0].Velocity, Is.EqualTo(want).Within(0.02f));
            Assert.That(Count(TakeEventType.Drum, "kick"), Is.EqualTo(1));
            Assert.That(Last(TakeEventType.Drum).Event.pos, Is.EqualTo(0.2f).Within(1e-5f));
            var drums = Calls(AudioCall.Drum);
            Assert.That(drums.Count, Is.EqualTo(1));
            Assert.That(drums[0].Drum, Is.EqualTo(DrumKind.Kick));
            Assert.That(drums[0].Vel, Is.EqualTo(want).Within(0.02f));
            Assert.That(drums[0].Strike.Pos, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(_kit.Piece(DrumSpec.KickId).Hits, Is.EqualTo(1), "the kick shell itself (its own source)");
            Assert.That(_kit.Piece(DrumSpec.KickPadId).Hits, Is.EqualTo(0));
            for (int i = 0; i < 20; i++) yield return NodFrame(desk, -0.5f);  // slowly back up
            Assert.That(_nods.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator NoNodKickWithoutAHeadset()
        {
            yield return Boot(simulateHeadset: false);
            var desk = _app.Rig.Desktop;
            for (int i = 0; i < 3; i++) yield return NodFrame(desk, 0f);
            for (int i = 0; i < 6; i++) yield return NodFrame(desk, 3f);
            Assert.That(_nods.Count, Is.EqualTo(0), "the nod kick is gated on presenting (App.IsPresenting)");
            Assert.That(_tap.Count(AudioCall.Drum), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator TheKitLeavingTheLayoutDropsItsSticksAndKeepsItsSourcesRendering()
        {
            yield return Boot();
            PickUpStick(Quaternion.LookRotation(_kit.transform.forward, Vector3.up));
            var stick = (Drumstick)Right.Holding;
            var layout = _app.Get<ILayoutService>();
            layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            Assert.IsFalse(_kit.IsPresent);
            Assert.IsNull(Right.Holding, "sticks go with the kit (Layout apply)");
            Assert.That(stick.Root.parent, Is.SameAs(_kit.transform));
            Assert.That(_kit.TryPickUp(Right, stick.GripWorld), Is.EqualTo(StickPick.None), "no kit in the room, no sticks");
            Step();
            SynthSource snare = _audio.Source(SourceId.Snare);
            Assert.That(snare.gameObject.activeInHierarchy, "a drum take still sounds while the kit is away");
            // Since the platform (platform.md 6.2) a kit on display in the VR room keeps its sources on its pieces: a drum take
            // sounds from the kit on the left wall. A kit hidden (mixed reality, or no display) parks them as before.
            if (layout.PlaceOf(InstrumentKind.Drums) != InstrumentPlace.Hidden)   // on display, or gliding there
                Assert.That(snare.transform.parent, Is.SameAs(_kit.Piece(DrumSpec.SnareId).Group), "on display, the take sounds from the kit");
            else
                Assert.That(snare.transform.parent, Is.Not.SameAs(_kit.Piece(DrumSpec.SnareId).Group));
            _tap.Clear();
            _kit.PlaybackHit("snare", 0.6f, DrumStrikeInfo.Default);
            var drums = Calls(AudioCall.Drum);
            Assert.That(drums.Count, Is.EqualTo(1));
            Assert.That(drums[0].Drum, Is.EqualTo(DrumKind.Snare));
            Assert.That(drums[0].Vel, Is.EqualTo(0.6f));
            long blocks = snare.BlocksRendered;
            float t0 = Time.realtimeSinceStartup;
            while (snare.BlocksRendered <= blocks + 2 && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            Assert.That(snare.BlocksRendered, Is.GreaterThan(blocks + 2), "the parked source keeps rendering on the audio thread");
            layout.Set(LayoutMode.All, InstrumentKind.Piano);
            for (int i = 0; i < 200 && !_kit.IsPresent; i++) Step();   // the kit glides back from its display spot (platform.md 4.5)
            Assert.IsTrue(_kit.IsPresent);
            Step();
            Assert.That(snare.transform.parent, Is.SameAs(_kit.Piece(DrumSpec.SnareId).Group), "back on the snare");
            Assert.That(snare.transform.localPosition, Is.EqualTo(Vector3.zero));
            Transform room = _audio.Source(SourceId.DrumRoom).transform;
            Assert.That(room.parent, Is.SameAs(_kit.transform));
            Assert.That(Vector3.Distance(room.localPosition, DrumSpec.KitCentre), Is.LessThan(1e-6f));
        }

        [UnityTest]
        public IEnumerator HotPathsAllocateNothing()
        {
            yield return Boot();
            // A stick in the right hand and the left hand hovering over the snare, so every loop has work to do.
            PickUpStick(Quaternion.LookRotation(_kit.transform.forward, Vector3.up));
            DrumPiece snare = _kit.Piece(DrumSpec.SnareId);
            _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, Quaternion.LookRotation(Vector3.down, _kit.transform.forward))
                .WithPointing().WithIndexTipAt(snare.Group.TransformPoint(new Vector3(-0.05f, 0.05f, 0f))));
            Step(3);
            _audio.TestTap = null;   // the tap allocates by design
            _app.Events.Unsubscribe(_onRecorder);
            _app.Events.Unsubscribe(_onNod);
            var pts = _app.Input.AllPointers;
            var glow = _app.HoverGlow;
            var left = _app.Input.Hand(Handedness.Left);
            var strike = new DrumStrike { Pos = 0.4f, Zone = DrumZone.Bow };
            var info = new DrumStrikeInfo { Pos = 0.3f, Zone = "rim" };
            Vector3 probe = snare.Group.position + Vector3.up * 0.05f;
            Vector3 far = snare.Group.position + Vector3.up * 0.6f;
            IReadOnlyList<Renderer> handles = _kit.Rig.Handles;
            void Work()
            {
                for (int r = 0; r < 10; r++)
                {
                    _mod.Tick(FramePhase.StickPoses, Dt);
                    _kit.Tick(pts, Dt);
                    _kit.Hover(pts, glow);
                    _mod.Tick(FramePhase.StickHover, Dt);
                    _mod.Tick(FramePhase.StickReleases, Dt);
                    _mod.Tick(FramePhase.NodKick, Dt);
                    _mod.Tick(FramePhase.EndFrame, Dt);
                    _kit.TryPickUp(left, far);
                    _kit.Hit(_kit.Piece(r % 9), 0.5f, null, true, strike);
                    _kit.PlaybackHit("snare", 0.6f, info);
                    _kit.PlaybackChoke("ride");
                    _kit.PlaySurfaceAt(probe);
                    _kit.Rig.Zone.Contains(probe);
                    for (int h = 0; h < handles.Count; h++) if (handles[h] == null) s_keep = null;
                    _kit.EveryHandPointerFartherThan(0.2f, DrumSpec.KickPadId);
                    glow.Update(0f);
                }
            }
            Work();   // warm up (JIT, first-use caches)
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");
            Assert.That(Work, Is.Not.AllocatingGCMemory());
        }
    }
}
