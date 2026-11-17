// App.Frame recorded through a probe injected with BootOptions.ExtraModules: the phase order of ARCHITECTURE.md
// section 4 (spec 05 section 1.1), where the instruments run inside it, and the lifetime of the one-frame edge flags.
// Stage-3 modules copy this pattern to assert their own phase timing.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class FrameOrderTests
    {
        /// Records phases (and its own instrument calls) while Recording; Order 5 = before Audio (10).
        sealed class PhaseProbe : AppModule, IInstrument
        {
            public readonly List<string> Log = new List<string>(64);
            public bool Recording;
            public bool SawPinchStart, PinchStartStillSetAtEndFrame;
            bool _startThisFrame;
            App _app;

            public override int Order => 5;
            public override void Init(App app) { _app = app; app.Register<IInstrument>(this); }

            public override void Tick(FramePhase phase, float dt)
            {
                if (Recording) Log.Add(phase.ToString());
                HandState right = _app.Input.Hand(Handedness.Right);
                if (phase == FramePhase.InputDone)
                {
                    _startThisFrame = right.PinchStart;
                    if (_startThisFrame) SawPinchStart = true;
                }
                else if (phase == FramePhase.EndFrame && _startThisFrame) PinchStartStillSetAtEndFrame |= right.PinchStart;
            }

            // IInstrument: TickOrder 5 sits between the piano (0) and the drums (10).
            public InstrumentKind Kind => InstrumentKind.Table;
            public int TickOrder => 5;
            public bool AcceptsStickPointers => false;
            public bool IsPresent => true;
            public Transform Root => null;
            public void Tick(IReadOnlyList<Pointer> pointers, float dt) { if (Recording) Log.Add("instrument.Tick"); }
            public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { if (Recording) Log.Add("instrument.Hover"); }
            public PlaySurface PlaySurfaceAt(Vector3 world) => PlaySurface.None;
            public IReadOnlyList<Renderer> PlayedParts => Array.Empty<Renderer>();
        }

        TestBoot _boot;
        FakeHandSource _fake;
        PhaseProbe _probe;
        HandPose _open;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            _fake = _boot.Hands;
            _open = HandPose.Open(HandSide.Right, new Vector3(0.2f, 1.0f, 0.4f), Quaternion.identity).WithPointing();
            _fake.SetPose(HandSide.Right, _open);
            _probe = new PhaseProbe();
            _boot.Options.ExtraModules.Add(_probe);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        [UnityTest]
        public IEnumerator OneFrameRunsThePhasesInTheDocumentedOrder()
        {
            var app = App.Current;
            Assert.That(app.Modules[0], Is.SameAs(_probe), "an injected Order 5 module initialises before Audio (10)");
            int at = -1;
            for (int i = 0; i < app.Instruments.Count; i++) if (ReferenceEquals(app.Instruments[i], _probe)) at = i;
            Assert.That(at, Is.GreaterThan(0), "registered as an instrument, after the piano (TickOrder 0)");
            Assert.That(app.Instruments[at - 1].TickOrder, Is.LessThanOrEqualTo(5));
            if (at + 1 < app.Instruments.Count) Assert.That(app.Instruments[at + 1].TickOrder, Is.GreaterThanOrEqualTo(5));

            // One frame, synchronously (no Unity Update can interleave): instruments run between InputDone and Instruments.
            _probe.Log.Clear();
            _probe.Recording = true;
            app.Frame(fromUnity: false);
            _probe.Recording = false;
            var want = new List<string> { nameof(FramePhase.StickPoses), nameof(FramePhase.InputDone), "instrument.Tick", "instrument.Hover" };
            foreach (FramePhase p in Enum.GetValues(typeof(FramePhase)))
                if (p != FramePhase.StickPoses && p != FramePhase.InputDone) want.Add(p.ToString());
            Assert.That(_probe.Log, Is.EqualTo(want));
            Assert.That(want[want.Count - 1], Is.EqualTo(nameof(FramePhase.EndFrame)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PinchEdgeIsSeenDuringTheFrameAndClearedBeforeEndFrame()
        {
            yield return null;
            Assert.IsFalse(_probe.SawPinchStart, "the open pointing hand does not pinch");
            _fake.SetPose(HandSide.Right, _open.WithPinch(0f));
            for (int i = 0; i < 5 && !_probe.SawPinchStart; i++) yield return null;
            Assert.IsTrue(_probe.SawPinchStart, "HandState.PinchStart is set when the modules tick (InputDone)");
            Assert.IsFalse(_probe.PinchStartStillSetAtEndFrame, "Input.EndFrame cleared the edge before the EndFrame phase");
            Assert.IsTrue(App.Current.Input.Hand(Handedness.Right).Pinching, "the pinch itself persists");
        }
    }
}
