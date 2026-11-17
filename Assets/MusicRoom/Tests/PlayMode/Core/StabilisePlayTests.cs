// The stabilising pass in a booted app: a held tool goes back when its hand leaves the instrument or goes to another one
// (HeldToolGuard; a tool mutes its hand everywhere), the piano lets go of held keys when "Move instruments" or MR placing
// starts (Core stops ticking it), and the material clone list and the menu's text caches stay bounded.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class HeldToolGuardTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        ToolProbe _probe;

        sealed class Box : IPlayZone
        {
            public Bounds B;
            public bool Contains(Vector3 world) => B.Contains(world);
        }

        sealed class Tool : IHandTool { }

        /// <summary>Two rigs at the seat (the drums ahead, the harp to the right, each a 0.8 m box) and a drums-owned tool.</summary>
        sealed class ToolProbe : AppModule, IHandToolProvider
        {
            public override int Order => 50;
            public Rig Drums, Harp;
            public readonly Tool T = new Tool();
            public HandState Holder;
            public override void Init(App app)
            {
                Drums = MakeRig(app, RigId.Drums, new Vector3(0f, 1f, 1f));
                Harp = MakeRig(app, RigId.Harp, new Vector3(1.5f, 1f, 1f));
                app.Register<IHandToolProvider>(this);
            }
            static Rig MakeRig(App app, RigId id, Vector3 at)
            {
                var go = new GameObject("probe " + id);
                go.transform.position = at;
                var r = new Rig { Id = id, Group = go.transform, InScene = true, Zone = new Box { B = new Bounds(at, Vector3.one * 0.8f) } };
                app.Register(r);
                return r;
            }
            public void Give(HandState h) { Holder = h; h.Holding = T; }
            public InstrumentKind Owner => InstrumentKind.Drums;
            public float NearestFree(Vector3 tip) => float.PositiveInfinity;
            public StickPick TryPickUp(HandState hand, Vector3 tip) => StickPick.None;
            public bool AnyHeld => Holder != null;
            public void ReleaseAll() { if (Holder != null) Holder.Holding = null; Holder = null; }
            public bool Holds(HandState hand) => hand != null && hand == Holder && ReferenceEquals(hand.Holding, T);
            public int ShadowSpheres(System.Span<Vector4> centreRadius) => 0;
        }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Options.ModuleFilter = t => false;   // only the probe: its two rigs and its tool
            _probe = new ToolProbe();
            _boot.Options.ExtraModules.Add(_probe);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() { yield return _boot.Stop(); }

        HandState Right => App.Current.Input.Hands[1];

        void PinchAt(Vector3 p)
        {
            var pose = HandPose.Open(HandSide.Right, p + new Vector3(0.02f, -0.03f, -0.15f), Quaternion.identity).WithPointing().WithIndexTipAt(p).WithPinch(0f);
            _boot.Hands.SetPose(HandSide.Right, pose);
        }

        [UnityTest]
        public IEnumerator AToolStaysAtItsInstrumentAndGoesBackWhenTheHandLeavesOrGoesToAnother()
        {
            var guard = App.Current.HeldTools;
            Vector3 atDrums = _probe.Drums.Group.position;
            PinchAt(atDrums);
            _boot.Step(Dt, 2);
            _probe.Give(Right);
            _boot.Step(Dt, 72);
            Assert.IsNotNull(Right.Holding, "held while the hand plays the drums");
            // A little outside the drums' zone (a far cymbal's reach): still held.
            PinchAt(atDrums + new Vector3(0f, 0f, 0.5f));
            _boot.Step(Dt, 72);
            Assert.IsNotNull(Right.Holding, "within AwayMargin of the instrument");
            // Far away for longer than AwaySeconds: back.
            int released = guard.Releases;
            PinchAt(atDrums + new Vector3(-1.5f, 0f, 0f));
            _boot.Step(Dt, 18);
            Assert.IsNotNull(Right.Holding, "not at once (a tracker jump)");
            _boot.Step(Dt, 36);
            Assert.IsNull(Right.Holding, "the hand left the drums: the tool went back");
            Assert.That(guard.Releases, Is.EqualTo(released + 1));
            // Again, now going straight to the harp (which takes no stick): back after ClaimSeconds.
            PinchAt(atDrums);
            _boot.Step(Dt, 2);
            _probe.Give(Right);
            _boot.Step(Dt, 2);
            PinchAt(_probe.Harp.Group.position);
            _boot.Step(Dt, 30);
            Assert.IsNull(Right.Holding, "another instrument claimed the hand: the tool went back");
            Assert.That(guard.Releases, Is.EqualTo(released + 2));
            yield return null;
        }
    }

    public class PianoLetsGoTests
    {
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Options.ModuleFilter = t => t == typeof(PianoModule);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() { yield return _boot.Stop(); }

        [UnityTest]
        public IEnumerator MoveInstrumentsAndPlacingLetGoOfHeldKeys()
        {
            var app = App.Current;
            var piano = app.Get<IPianoApi>();
            object finger = new object();
            piano.PressMidi(60, finger);
            Assert.That(piano.HeldKeyCount, Is.EqualTo(1));
            app.Events.Publish(new ArrangeModeEvent { Active = true });
            Assert.That(piano.HeldKeyCount, Is.Zero, "Move instruments: the key is let go (nothing ticks the piano now)");
            app.Events.Publish(new ArrangeModeEvent { Active = false });
            piano.PressMidi(62, finger);
            Assert.That(piano.HeldKeyCount, Is.EqualTo(1));
            app.Events.Publish(new PlacingEvent { Active = true });
            Assert.That(piano.HeldKeyCount, Is.Zero, "MR placing: let go too");
            yield return null;
        }
    }

    public class BoundedCachesTests
    {
        [UnityTest]
        public IEnumerator TheCloneListForgetsDestroyedMaterials()
        {
            var lib = new MaterialLibrary();
            var made = new List<Material>();
            for (int i = 0; i < MaterialLibrary.PruneMin * 3; i++)
            {
                Material m = lib.Clone(MaterialLibrary.Unlit);
                if (i % 2 == 0) lib.Destroy(m);              // given back by its owner
                else { Object.Destroy(m); made.Add(m); }     // destroyed some other way
            }
            yield return null;   // the Destroy calls land
            lib.Clone(MaterialLibrary.Unlit);
            for (int i = 0; i < MaterialLibrary.PruneMin; i++) lib.Clone(MaterialLibrary.Unlit);
            Assert.That(lib.CloneCount, Is.LessThan(MaterialLibrary.PruneMin * 2 + 2), "dead clones are pruned, given-back ones never kept");
            lib.DestroyAll();
            Assert.That(lib.CloneCount, Is.Zero);
        }
    }
}
