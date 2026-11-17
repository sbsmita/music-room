// The piano's platform adapters (12-new-instruments/platform.md 3.3, 12.2; Piano/PianoPlatform.cs) in the booted app: the
// four services registered with Kind Piano; the rig homes equal the descriptor; the descriptor's boxes cover Rig.Handles
// within 3 cm (conformance check 3, margins logged); IInstrumentPlayback makes the very calls of the classic playback path
// (IPianoApi.PlaybackOn / Off / ReleaseAll); the anchors (key.<midi>, keys.middle) resolve to the key tops and allocate
// nothing; IInstrumentSurface hides and shows the stand; SetStowed lets every key go; MusicRestPose follows the piano and is
// null while it is not at the seat.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Piano
{
    public class PianoPlatformTests
    {
        TestBoot _boot;
        App _app;
        PianoInstrument _piano;
        PianoPlatform _platform;
        static Vector3 s_world, s_up;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true);
            _boot.Options.InstrumentFilter = k => k <= InstrumentKind.Harp;
            yield return _boot.Start();
            _app = App.Current;
            var layout = _app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            yield return null;
            _piano = (PianoInstrument)_app.GetAll<IPianoApi>().First(p => p is PianoInstrument);
            _platform = _piano.Platform;
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
            Assert.That(_platform, Is.Not.Null, "PianoModule registered PianoPlatform");
            Assert.That(_app.GetAll<IStowable>().Single(s => s.Kind == InstrumentKind.Piano), Is.SameAs(_platform));
            Assert.That(_app.GetAll<IInstrumentPlayback>().Single(s => s.Kind == InstrumentKind.Piano), Is.SameAs(_platform));
            Assert.That(_app.GetAll<IGuideAnchors>().Single(s => s.Kind == InstrumentKind.Piano), Is.SameAs(_platform));
            Assert.That(_app.GetAll<IInstrumentSurface>().Single(s => s.Kind == InstrumentKind.Piano), Is.SameAs(_platform));
            InstrumentDescriptor d = _app.Registry.Get(InstrumentKind.Piano);
            ClassicConformance.AssertHomes(d, _piano.Rig);
            ClassicConformance.AssertBoxesCoverHandles(d, _piano.Rig);
            Assert.That(d.HasSheetAnchor, Is.True);
            Assert.That(d.SheetAnchor.position, Is.EqualTo(_piano.MusicRestLocal.position), "SheetAnchor = MusicRestLocal");
            Assert.That(Quaternion.Angle(d.SheetAnchor.rotation, _piano.MusicRestLocal.rotation), Is.LessThan(1e-3f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaybackMakesTheClassicPathsCalls()
        {
            var engine = _app.Get<IAudioEngine>();
            float t0 = Time.realtimeSinceStartup;
            while (!engine.IsReady && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            // Through the adapter on key 60; the classic line (IPianoApi.PlaybackOn) on key 62: the same calls.
            Assert.That(_platform.Play(TakeEvent.PianoOn(60, 0.7f)), Is.True);
            var viaAdapter = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            Assert.That(_piano.Action.KeyByMidi(60).Held, Is.True, "the key went down like a played key");
            tap.Clear();
            _piano.PlaybackOn(62, 0.7f);
            var classic = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            Assert.That(viaAdapter, Is.Not.Empty, "a sound call");
            Assert.That(viaAdapter, Is.EqualTo(classic), "the classic SetTarget line's calls");
            tap.Clear();
            Assert.That(_platform.Play(TakeEvent.PianoOff(60)), Is.True);
            viaAdapter = tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList();
            tap.Clear();
            _piano.PlaybackOff(62);
            Assert.That(viaAdapter, Is.EqualTo(tap.Calls.Select(c => (c.Method, c.Unit, c.Op)).ToList()), "pianoOff likewise");
            Assert.That(_piano.Action.KeyByMidi(60).Held, Is.False);
            // ReleaseAll lets every playback-held key up; an unknown type is refused; no lane to close.
            _platform.Play(TakeEvent.PianoOn(64, 0.6f));
            _platform.Play(TakeEvent.PianoOn(67, 0.6f));
            _platform.ReleaseAll();
            Assert.That(_piano.Action.KeyByMidi(64).Held || _piano.Action.KeyByMidi(67).Held, Is.False, "ReleaseAll: every playback key up");
            Assert.That(_platform.Play(TakeEvent.Drum("snare", 0.5f, 0.3f, "head", false)), Is.False);
            _platform.CloseLane(PlaybackLanes.Take);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnchorsAreTheKeyTopsAndAllocateNothing()
        {
            Transform t = _piano.transform;
            Assert.That(_platform.TryGetAnchor("key.60", out Vector3 w, out Vector3 up), Is.True);
            Assert.That(Vector3.Distance(w, t.TransformPoint(new Vector3(PianoSpec.KeyX(60), 0f, PianoSpec.L * 0.5f))), Is.LessThan(1e-5f), "middle C's top centre");
            Assert.That(Vector3.Angle(up, t.up), Is.LessThan(0.01f));
            Assert.That(_platform.TryGetAnchor("key.61", out w, out _), Is.True);
            Assert.That(Vector3.Distance(w, t.TransformPoint(new Vector3(PianoSpec.KeyX(61), PianoSpec.BTOP, PianoSpec.L - PianoSpec.BL * 0.5f))), Is.LessThan(1e-5f), "a black key's top");
            Assert.That(_platform.TryGetAnchor("key.21", out _, out _) && _platform.TryGetAnchor("key.108", out _, out _), Is.True, "every key");
            Assert.That(_platform.TryGetAnchor("keys.middle", out w, out _), Is.True);
            Assert.That(Vector3.Distance(w, t.TransformPoint(new Vector3(0f, 0f, PianoSpec.L * 0.5f))), Is.LessThan(1e-5f), "the keyboard's centre");
            foreach (string bad in new[] { "key.20", "key.109", "key.", "key.6x", "keys", "string.3", null })
                Assert.That(_platform.TryGetAnchor(bad, out _, out _), Is.False, bad ?? "null");
            // Every anchor the descriptor names resolves (conformance check 13).
            foreach (string a in _app.Registry.Get(InstrumentKind.Piano).Anchors) Assert.That(_platform.TryGetAnchor(a, out _, out _), Is.True, a);
            // The ids are made outside the measured call (a literal inside it would be interned on its first run).
            string key = "key.72", middle = "keys.middle";
            for (int i = 0; i < 3; i++) { _platform.TryGetAnchor(key, out s_world, out s_up); _platform.TryGetAnchor(middle, out s_world, out s_up); }   // warm up (JIT)
            Assert.That(() => { _platform.TryGetAnchor(key, out s_world, out s_up); _platform.TryGetAnchor(middle, out s_world, out s_up); }, Is.Not.AllocatingGCMemory());
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSurfaceHidesTheStandAndStowingLetsEveryKeyGo()
        {
            Assert.That(_platform.OnSurface, Is.False);
            _platform.SetOnSurface(true);
            Assert.That(_piano.OnSurface && _platform.OnSurface, Is.True, "on a real table: the stand hides");
            Assert.That(_piano.StandRenderer.gameObject.activeSelf, Is.False);
            _platform.SetOnSurface(false);
            Assert.That(_piano.OnSurface, Is.False);
            Assert.That(_piano.StandRenderer.gameObject.activeSelf, Is.True, "back on its stand");

            // A key held by someone when the piano goes on display: let go, so it rests static.
            var presser = new object();
            _piano.PressMidi(64, presser);
            Assert.That(_piano.Action.KeyByMidi(64).Held, Is.True);
            Assert.That(_platform.Stowed, Is.False);
            Assert.That(_piano.MusicRestPose.HasValue, Is.True, "at the seat: a music rest");
            _platform.SetStowed(true, StowMount.Floor);
            Assert.That(_platform.Stowed, Is.True);
            Assert.That(_piano.Action.KeyByMidi(64).Held, Is.False, "no key stays down on display");
            Assert.That(_piano.MusicRestPose.HasValue, Is.False, "on display: no music rest");
            _platform.SetStowed(false, StowMount.Floor);
            Assert.That(_platform.Stowed, Is.False);
            Assert.That(_piano.MusicRestPose.HasValue, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheMusicRestPoseFollowsThePianoAndIsNullAwayFromTheSeat()
        {
            Transform t = _piano.transform;
            Pose? rest = _piano.MusicRestPose;
            Assert.That(rest.HasValue, Is.True);
            Assert.That(Vector3.Distance(rest.Value.position, t.TransformPoint(PianoMusicRest.Local.position)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Angle(rest.Value.rotation * Vector3.up, t.TransformDirection(PianoMusicRest.Up)), Is.LessThan(0.01f), "up along the rest");
            Assert.That(Vector3.Dot(rest.Value.rotation * Vector3.forward, t.forward), Is.GreaterThan(0.9f), "facing away from the player");
            Assert.That(_piano.MusicRestLedge, Is.EqualTo(PianoMusicRest.Ledge));
            // Moved and turned: it follows.
            t.position += new Vector3(0.2f, 0.05f, 0.1f);
            t.rotation = Quaternion.Euler(0f, 30f, 0f);
            rest = _piano.MusicRestPose;
            Assert.That(Vector3.Distance(rest.Value.position, t.TransformPoint(PianoMusicRest.Local.position)), Is.LessThan(1e-5f));
            // Out of the layout (on display, gliding): none; back: there again.
            _piano.Rig.InScene = false;
            Assert.That(_piano.MusicRestPose.HasValue, Is.False);
            _piano.Rig.InScene = true;
            Assert.That(_piano.MusicRestPose.HasValue, Is.True);
            for (int i = 0; i < 3; i++) { var w = _piano.MusicRestPose; s_world = w.HasValue ? w.Value.position : Vector3.zero; }   // warm up (JIT)
            Assert.That(() => { var p = _piano.MusicRestPose; s_world = p.HasValue ? p.Value.position : Vector3.zero; }, Is.Not.AllocatingGCMemory());
            yield return null;
        }
    }
}
