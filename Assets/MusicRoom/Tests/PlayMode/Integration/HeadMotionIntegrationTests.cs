// "Turning the head should not move instruments" end to end, with every real module (simulated headset, manual frames at
// 72 Hz, scripted hands), after the 2026-11-10 headset test ("instruments are changing with my neck position. it should be
// changed with hands"):
//   - the head alone (fast turns, looking up and down, leaning) with both hands open and upright in front moves, turns and
//     switches nothing: One alone and My set, Move instruments off and on;
//   - arrange mode is Move instruments On, always: bringing instruments, page turns and mode changes never turn it on by
//     themselves, and with the setting on it comes back after each of them;
//   - a new instrument (One alone) and a changed set land in front of the player as they stand and face at that moment,
//     once: afterwards the head moving moves nothing.
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
    public class HeadMotionIntegrationTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;

        IEnumerator BootApp()
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
            Assert.That(_layout, Is.Not.Null);
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(3);
            Assert.IsTrue(_layout.FirstArrangeDone);
        }

        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); }

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

        void Step(int n = 1) => _boot.Step(Dt, n);

        void Settle()
        {
            for (int i = 0; i < 400 && (_layout.AnyMoving || _layout.Settling || _layout.IsTurning); i++) Step();
            Step(3);
        }

        Transform Head => _app.Head;

        // ---- every rig of the scene, before and after
        readonly List<Rig> _rigs = new List<Rig>();
        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<Quaternion> _rot = new List<Quaternion>();

        void Snap()
        {
            _rigs.Clear(); _pos.Clear(); _rot.Clear();
            var reg = _app.Get<IRigRegistry>();
            for (int i = 0; i < reg.Rigs.Count; i++)
            {
                Rig r = reg.Rigs[i];
                if (r?.Group == null || r.Id == RigId.Lessons || !r.Group.gameObject.activeInHierarchy) continue;
                _rigs.Add(r);
                _pos.Add(r.Group.position);
                _rot.Add(r.Group.rotation);
            }
            Assert.That(_rigs.Count, Is.GreaterThan(1));
        }

        void AssertUnmoved(string why)
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                Transform g = _rigs[i].Group;
                Assert.That(Vector3.Distance(g.position, _pos[i]), Is.LessThan(1e-4f), $"{_rigs[i].Id} moved: {why}");
                Assert.That(Quaternion.Angle(g.rotation, _rot[i]), Is.LessThan(0.01f), $"{_rigs[i].Id} turned: {why}");
            }
        }

        void OpenPalmsStill(Vector3 head, Quaternion facing)
        {
            Vector3 right = facing * Vector3.right, fwd = facing * Vector3.forward;
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, head + right * 0.25f + fwd * 0.3f + Vector3.down * 0.2f, Vector3.up, right));
            _boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, head - right * 0.25f + fwd * 0.3f + Vector3.down * 0.2f, Vector3.up, -right));
        }

        /// <summary>The head alone: turns of +-80 degrees (up to ~420 degrees/s), up and down 30, leaning 25 cm; the seat stays.</summary>
        void HeadOnly(float seconds, Vector3 head0, float yaw0)
        {
            int n = Mathf.RoundToInt(seconds / Dt);
            Quaternion facing = Quaternion.Euler(0f, yaw0, 0f);
            for (int i = 0; i < n; i++)
            {
                float t = i * Dt;
                float yaw = 80f * Mathf.Sin(2f * Mathf.PI * t / 1.2f), pitch = 30f * Mathf.Sin(2f * Mathf.PI * t / 1.7f);
                Vector3 lean = facing * new Vector3(0.25f * Mathf.Sin(2f * Mathf.PI * t / 2.1f), 0.05f * Mathf.Sin(2f * Mathf.PI * t / 0.9f), 0.2f * Mathf.Sin(2f * Mathf.PI * t / 1.3f));
                Head.SetPositionAndRotation(head0 + lean, Quaternion.Euler(pitch, yaw0 + yaw, 0f));
                Step();
            }
            Head.SetPositionAndRotation(head0, facing);
        }

        void HeadOnlyCheck(string why)
        {
            Vector3 head0 = Head.position;
            float yaw0 = _app.Rig.RigRoot.eulerAngles.y;
            OpenPalmsStill(head0, Quaternion.Euler(0f, yaw0, 0f));
            Step(3);
            Snap();
            InstrumentKind current = _layout.Current;
            int turns = _layout.Turns, backs = _layout.BringBacks;
            bool arranging = _layout.Arranging;
            HeadOnly(4f, head0, yaw0);
            Step(3);
            AssertUnmoved(why);
            Assert.That(_layout.Current, Is.EqualTo(current), "no instrument switched: " + why);
            Assert.That(_layout.Turns, Is.EqualTo(turns), "no page turn: " + why);
            Assert.That(_layout.BringBacks, Is.EqualTo(backs), "no reset view: " + why);
            Assert.That(_layout.Arranging, Is.EqualTo(arranging), why);
        }

        [UnityTest]
        public IEnumerator TurningTheHeadMovesTurnsAndSwitchesNoInstrument()
        {
            _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Settle();
            HeadOnlyCheck("One alone, Move instruments off");
            _layout.BeginArrange();
            Step(2);
            Assert.IsTrue(_layout.Arranging);
            HeadOnlyCheck("One alone, Move instruments on");
            _layout.EndArrange();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            HeadOnlyCheck("My set, Move instruments off");
            _layout.BeginArrange();
            Step(2);
            HeadOnlyCheck("My set, Move instruments on");
            _layout.EndArrange();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArrangeModeIsMoveInstrumentsOnAlways()
        {
            var seen = new List<bool>();
            _app.Events.Subscribe<ArrangeModeEvent>(e => seen.Add(e.Active));
            _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Settle();
            // Off: switching instruments, page turns and the layout's mode never turn it on.
            Assert.IsFalse(_layout.MoveOn);
            _layout.Bring(InstrumentKind.Piano, BringReason.Menu);
            Settle();
            _layout.Next(1);
            Settle();
            _layout.Bring(InstrumentKind.Harp, BringReason.Menu);
            Settle();
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Settle();
            Assert.IsFalse(_layout.Arranging, "never on with the setting off");
            Assert.That(seen.Contains(true), Is.False, "no ArrangeModeEvent{Active} at all");
            // On: a switch suspends it while the instrument travels, then it is back, because the setting is on.
            _layout.BeginArrange();
            Step();
            Assert.IsTrue(_layout.Arranging && _layout.MoveOn);
            _layout.Bring(InstrumentKind.Piano, BringReason.Menu);
            Settle();
            Assert.IsTrue(_layout.Arranging, "back on after the switch (the setting is on)");
            Assert.That(_layout.Arranging, Is.EqualTo(_layout.MoveOn));
            // Off again: the next switch leaves it off.
            _layout.EndArrange();
            _layout.Bring(InstrumentKind.Drums, BringReason.Menu);
            Settle();
            Assert.IsFalse(_layout.Arranging);
            Assert.IsFalse(_app.Settings.MoveInstruments);
            yield return null;
        }

        /// <summary>The flat angle (degrees) from the head's heading on the floor to the rig's footprint centre, and its distance.</summary>
        float Bearing(Rig r, float headingDeg, out float distance)
        {
            Bounds b = new Bounds(r.Group.position, Vector3.zero);
            foreach (Renderer x in r.Group.GetComponentsInChildren<Renderer>()) if (x.enabled && x.gameObject.activeInHierarchy) b.Encapsulate(x.bounds);
            Vector3 d = b.center - Head.position;
            d.y = 0f;
            distance = d.magnitude;
            return Mathf.DeltaAngle(headingDeg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        Rig RigOf(InstrumentKind k) => _app.Get<IRigRegistry>().Get(InstrumentIds.RigOf(k));

        /// <summary>The player walked to another spot and turned (the rig: where they stand and face now).</summary>
        void StandAt(Vector3 head, float yawDeg)
        {
            Transform rig = _app.Rig.RigRoot;
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            rig.rotation = rot;
            rig.position = head - rot * DesktopRigFallback.EyePosition;
            Head.rotation = rot;
            Step();
            Head.rotation = rot;
        }

        [UnityTest]
        public IEnumerator ANewInstrumentLandsInFrontOfThePlayerAsTheyStandNow()
        {
            _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Settle();
            // Walked a metre and turned 70 degrees to the right: the drums are off to the side now.
            StandAt(new Vector3(0.9f, 1.2f, -0.7f), 70f);
            Head.rotation = Quaternion.Euler(0f, 70f, 0f);
            _layout.Bring(InstrumentKind.Piano, BringReason.Menu);
            Settle();
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Piano));
            float a = Bearing(RigOf(InstrumentKind.Piano), 70f, out float dist);
            Assert.That(Mathf.Abs(a), Is.LessThan(20f), "straight in front of where the player faces now");
            Assert.That(dist, Is.LessThan(1.2f), "within reach of where the player stands now");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(RigOf(InstrumentKind.Piano).Group.eulerAngles.y, 70f)), Is.LessThan(1f), "facing the player");
            // Once: the head moving afterwards moves nothing.
            HeadOnlyCheck("after a switch");
            // Again, from another spot and heading, the next one.
            StandAt(new Vector3(-0.6f, 1.2f, 0.4f), -40f);
            _layout.Bring(InstrumentKind.Harp, BringReason.Menu);
            Settle();
            a = Bearing(RigOf(InstrumentKind.Harp), -40f, out dist);
            Assert.That(Mathf.Abs(a), Is.LessThan(35f), "the harp in front too");
            Assert.That(dist, Is.LessThan(1.2f));
            // My set: the whole set laid out about where the player stands and faces as it changes; its lead straight ahead.
            StandAt(new Vector3(0.3f, 1.2f, -0.2f), 120f);
            _layout.Set(LayoutMode.All, InstrumentKind.Piano);
            Settle();
            a = Bearing(RigOf(_layout.Lead), 120f, out dist);
            Assert.That(Mathf.Abs(a), Is.LessThan(20f), "My set's lead in front");
            HeadOnlyCheck("after a set change");
            yield return null;
        }
    }
}
