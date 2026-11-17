// The harp's platform adapters (12-new-instruments/platform.md 3.3, 12.2; Harp/HarpPlatform.cs) in the booted app: the
// services registered with Kind Harp; the rig homes equal the descriptor (the single home's x is computed: -0.2966 within
// 1e-3); the descriptor's boxes cover Rig.Handles within 3 cm (conformance check 3, margins logged); IInstrumentPlayback
// makes the very calls of the classic playback path (PlaybackPluck, the whole stroke or the default pluck; PlaybackDamp);
// the anchors (string.<i>) are the play points and allocate nothing; SetStowed flags the display state.
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Harp
{
    public class HarpPlatformTests
    {
        TestBoot _boot;
        App _app;
        HarpModule _module;
        double _audio = 50.0;
        static Vector3 s_world, s_up;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, audioClock: () => _audio);
            _boot.Options.InstrumentFilter = k => k <= InstrumentKind.Harp;
            yield return _boot.Start();
            _app = App.Current;
            var layout = _app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            yield return null;
            _module = (HarpModule)_app.Modules.First(m => m is HarpModule);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var engine = _app?.Get<IAudioEngine>();
            if (engine != null) engine.TestTap = null;
            yield return _boot.Stop();
        }

        [UnityTest]
        public IEnumerator TheAdaptersAreRegisteredAndTheRigMatchesItsDescriptor()
        {
            HarpPlatform p = _module.Platform;
            Assert.That(p, Is.Not.Null);
            Assert.That(_app.GetAll<IStowable>().Single(s => s.Kind == InstrumentKind.Harp), Is.SameAs(p));
            Assert.That(_app.GetAll<IInstrumentPlayback>().Single(s => s.Kind == InstrumentKind.Harp), Is.SameAs(p));
            Assert.That(_app.GetAll<IGuideAnchors>().Single(s => s.Kind == InstrumentKind.Harp), Is.SameAs(p));
            InstrumentDescriptor d = _app.Registry.Get(InstrumentKind.Harp);
            Rig rig = _module.Harp.Rig;
            Debug.Log($"[Conformance] harp single-mode x: computed {rig.HomeSingle.x:F5}, descriptor {d.Seat.Single.x:F5}");
            Assert.That(rig.HomeSingle.x, Is.EqualTo(-0.2966f).Within(1e-3f), "the mean string-midpoint x");
            ClassicConformance.AssertHomes(d, rig, 1e-3f);
            ClassicConformance.AssertBoxesCoverHandles(d, rig);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaybackMakesTheClassicPathsCalls()
        {
            var engine = _app.Get<IAudioEngine>();
            float t0 = Time.realtimeSinceStartup;
            while (!engine.IsReady && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            HarpPlatform p = _module.Platform;
            HarpInstrument harp = _module.Harp;
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            // The whole stroke on string 5 through the adapter; the classic line on string 7: the same calls.
            Assert.That(p.Play(TakeEvent.Harp(5, 0.7f, 0.35f, 0.4f, "across")), Is.True);
            var viaAdapter = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            Assert.That(viaAdapter, Is.Not.Empty, "a pluck");
            tap.Clear();
            harp.PlaybackPluck(7, 0.7f, 0.35f, 0.4f, "across");
            Assert.That(viaAdapter, Is.EqualTo(tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList()), "the classic SetTarget line's calls");
            // An event without a stroke is the default pluck (.35, 0, through).
            tap.Clear();
            Assert.That(p.Play(new TakeEvent { type = TakeEventType.Harp, i = 9, vel = 0.6f }), Is.True);
            var plain = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            tap.Clear();
            harp.PlaybackPluck(11, 0.6f);
            Assert.That(plain, Is.EqualTo(tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList()));
            tap.Clear();
            Assert.That(p.Play(TakeEvent.HarpDamp(5)), Is.True);
            var damp = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            tap.Clear();
            harp.PlaybackDamp(7);
            Assert.That(damp, Is.EqualTo(tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList()), "harpDamp likewise");
            Assert.That(p.Play(TakeEvent.PianoOn(60, 0.5f)), Is.False);
            p.ReleaseAll();
            p.CloseLane(PlaybackLanes.Layer(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnchorsAreThePlayPointsAndAllocateNothing()
        {
            HarpPlatform p = _module.Platform;
            Transform t = _module.Harp.transform;
            for (int i = 0; i < HarpGeometry.StringCount; i++)
            {
                Assert.That(p.TryGetAnchor("string." + i, out Vector3 w, out Vector3 up), Is.True, "string " + i);
                Assert.That(Vector3.Distance(w, t.TransformPoint(_module.Harp.PlayPoint(i))), Is.LessThan(1e-5f));
                Assert.That(Vector3.Angle(up, t.forward), Is.LessThan(0.01f), "the string plane's normal");
            }
            // The normal points to the player's side of the strings (the head is in front of the plane).
            Vector3 toHead = _app.Head.position - t.position;
            Assert.That(Vector3.Dot(toHead, t.forward), Is.GreaterThan(0f), "up faces the player");
            foreach (string bad in new[] { "string.22", "string.", "string.-1", "strings.3", null })
                Assert.That(p.TryGetAnchor(bad, out _, out _), Is.False, bad ?? "null");
            foreach (string a in _app.Registry.Get(InstrumentKind.Harp).Anchors) Assert.That(p.TryGetAnchor(a, out _, out _), Is.True, a);
            // The id is made outside the measured call (a literal inside it would be interned on its first run).
            string last = "string.21";
            for (int i = 0; i < 3; i++) p.TryGetAnchor(last, out s_world, out s_up);   // warm up (JIT)
            Assert.That(() => { p.TryGetAnchor(last, out s_world, out s_up); }, Is.Not.AllocatingGCMemory());
            yield return null;
        }

        [UnityTest]
        public IEnumerator StowingFlagsTheDisplayState()
        {
            HarpPlatform p = _module.Platform;
            Assert.That(p.Stowed, Is.False);
            p.SetStowed(true, StowMount.Floor);
            Assert.That(p.Stowed, Is.True);
            p.SetStowed(true, StowMount.Floor);
            p.SetStowed(false, StowMount.Floor);
            Assert.That(p.Stowed, Is.False);
            yield return null;
        }
    }
}
