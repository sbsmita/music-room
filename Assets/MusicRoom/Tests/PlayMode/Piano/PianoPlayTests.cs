// The piano in the running app (ARCHITECTURE.md 15.1 / 16): scripted FakeHandSource fingers press keys through the real
// pointer layer, IAudioEngine.TestTap sees the notes, the recorder stream sees pianoOn / pianoOff, the automatic sustain
// keeps released notes ringing until the next phrase, the hover glow, the play zone, the stand on a table, the rig
// registration, the desktop click target, the instrument's phase timing and a zero-allocation hot path. Every fixture
// boots through TestBoot (section 16: a fresh MemorySettingsStore and a temporary DataPath, never the developer's
// PlayerPrefs or takes). Most tests use manualFrames and TestBoot.Step so strike speeds are exact; one fixture drives
// real Unity frames end to end. Only the modules the piano talks to are booted (Audio, Lessons for the IRecorder stream,
// Piano): no Layout moves the piano mid-test and no first-launch tour (Guide) runs during the assertions.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.TestTools.Utils;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Piano
{
    public class PianoPlayTests
    {
        /// Logs the phases and the recorder stream of one frame (Order 5: before Audio).
        sealed class Probe : AppModule
        {
            public readonly List<string> Log = new List<string>(64);
            public bool Recording;
            App _app;
            public override int Order => 5;
            public override void Init(App app)
            {
                _app = app;
                app.Events.Subscribe<RecorderEvent>(e => { if (Recording) Log.Add(e.Event.type + ":" + e.Event.midi); });
            }
            public override void Tick(FramePhase phase, float dt) { if (Recording) Log.Add(phase.ToString()); }
        }

        TestBoot _boot;
        FakeHandSource _fake;
        App _app;
        PianoInstrument _piano;
        Probe _probe;
        readonly List<TakeEvent> _events = new List<TakeEvent>(4096);
        static readonly Quaternion Down = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        static HandPose FingerAt(Vector3 world) => HandPose.Open(HandSide.Right, Vector3.zero, Down).WithPointing().WithIndexTipAt(world);

        /// The piano's collaborators only (see the header). The probe joins through ExtraModules.
        internal static bool PianoAndFriends(System.Type t) => t.Name == "AudioModule" || t.Name == "LessonsModule" || t.Name == "PianoModule";

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, manualFrames: true);
            _fake = _boot.Hands;
            _fake.SetPose(HandSide.Right, FingerAt(new Vector3(0.3f, 1.2f, 0.1f)));   // away from the keys
            _probe = new Probe();
            _boot.Options.ModuleFilter = PianoAndFriends;
            _boot.Options.ExtraModules.Add(_probe);
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is PianoModule pm) _piano = pm.Piano;
            Assert.That(_piano, Is.Not.Null);
            _events.Clear();
            _app.Events.Subscribe<RecorderEvent>(e => _events.Add(e.Event));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var engine = _app?.Get<IAudioEngine>();
            if (engine != null) engine.TestTap = null;
            yield return _boot.Stop();
        }

        /// One manual frame: the scripted hands, the pinch / grasp derivation, the clock and every phase advance by dt.
        void Frame(float dt = 1f / 90f) => _boot.Step(dt);

        Vector3 OnPiano(int midi, float y, float back)
        {
            _piano.TryGetKey(midi, out var k);
            return _piano.transform.TransformPoint(new Vector3(k.X, y, back));
        }

        /// The right index tip from 3 cm above a key top straight down past the felt, 4 mm per 90 Hz frame (0.36 m/s).
        void PressWithFinger(int midi, float back = 0.03f)
        {
            float top = PianoSpec.IsBlack(midi) ? PianoSpec.BTOP : 0f;
            for (float y = top + 0.03f; y >= top - 0.014f; y -= 0.004f)
            {
                _fake.SetPose(HandSide.Right, FingerAt(OnPiano(midi, y, back)));
                Frame();
            }
        }

        void LiftFinger(int midi, float back = 0.03f)
        {
            _fake.SetPose(HandSide.Right, FingerAt(OnPiano(midi, 0.06f, back)));
            for (int i = 0; i < 12; i++) Frame();
        }

        int Count(List<TakeEvent> evs, string type, int midi = -1) { int n = 0; foreach (var e in evs) if (e.type == type && (midi < 0 || e.midi == midi)) n++; return n; }

        // The sound is asserted on the IAudioEngine calls the piano made (section 16: AudioTestTap.Calls), never on the
        // Audio module's own recipe (its staged commands), so a retuned synth cannot break these tests.
        internal static int Calls(AudioTestTap tap, AudioCall method, int midi = -1, SynthSource source = null)
        {
            int n = 0;
            foreach (var c in tap.Calls)
                if (c.Method == method && (midi < 0 || c.Index == midi) && (source == null || c.Handle.Source == source)) n++;
            return n;
        }

        internal static AudioTestTap.Call Last(AudioTestTap tap, AudioCall method)
        {
            var last = default(AudioTestTap.Call);
            foreach (var c in tap.Calls) if (c.Method == method) last = c;
            return last;
        }

        [UnityTest]
        public IEnumerator ScriptedFingerPressFiresOnceAndSustains()
        {
            var engine = _app.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            var hand = _app.Input.Hand(Handedness.Right);
            Frame();
            Assert.That(hand.Pointers[0].Active, Is.True);

            PressWithFinger(64);
            Assert.That(tap.Count(AudioCall.Piano), Is.EqualTo(1), "one note");
            var note = Last(tap, AudioCall.Piano);
            Assert.That(note.Index, Is.EqualTo(64), "E4");
            Assert.That(note.When, Is.EqualTo(0.0), "a live strike plays as soon as possible (section 17)");
            Assert.That(note.Handle.IsValid, Is.True);
            Assert.That(note.Handle.Source, Is.SameAs(engine.Source(SourceId.PianoMid)), "E4 sounds from the middle source");
            Assert.That(Count(_events, TakeEventType.PianoOn, 64), Is.EqualTo(1));
            float vel = 0f; foreach (var e in _events) if (e.type == TakeEventType.PianoOn) vel = e.vel;
            Assert.That(vel, Is.EqualTo(PianoSpec.StrikeVelocity(0.36f)).Within(0.06f), "velocity from the downward speed");
            Assert.That(note.Vel, Is.EqualTo(vel), "the recorder and the engine hear the same strike");
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(1));
            Assert.That(PointerInput.IsPlaying(hand, _app.Clock.NowMs), Is.True, "the hand counts as playing (grab guard)");

            // Holding still on the felt does not re-trigger; lifting lets the damper fall: pianoOff, but the note rings on.
            for (int i = 0; i < 20; i++) Frame();
            Assert.That(tap.Count(AudioCall.Piano), Is.EqualTo(1));
            LiftFinger(64);
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(0));
            Assert.That(Count(_events, TakeEventType.PianoOff, 64), Is.EqualTo(1));
            Assert.That(tap.Count(AudioCall.Release), Is.EqualTo(0), "automatic sustain: still ringing");

            // A new phrase (> 0.6 s later, nothing held) lifts the pedal: the ringing E4 is released with the damper thud.
            for (int i = 0; i < 60; i++) Frame();
            PressWithFinger(67);
            Assert.That(tap.Count(AudioCall.Release), Is.EqualTo(1), "the old note is damped");
            var off = Last(tap, AudioCall.Release);
            Assert.That(off.Handle.Id, Is.EqualTo(note.Handle.Id), "E4's own voice");
            Assert.That(off.Handle.Source, Is.SameAs(note.Handle.Source));
            Assert.That(off.Flag, Is.True, "with the damper thud");
            Assert.That(tap.Count(AudioCall.Piano), Is.EqualTo(2));
            Assert.That(Last(tap, AudioCall.Piano).Index, Is.EqualTo(67));
            LiftFinger(67);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoverGlowOverAKeyTop()
        {
            _fake.SetPose(HandSide.Right, FingerAt(OnPiano(64, 0.02f, 0.03f)));
            Frame();
            Frame();
            var glow = _app.HoverGlow;
            Assert.That(glow.Drawn, Is.GreaterThanOrEqualTo(1), "a glow under the hovering fingertip");
            var s = glow.Spec(0);
            Assert.That(s.Gain, Is.EqualTo(0.45f));
            Assert.That(s.Steady, Is.True);
            Assert.That(s.Width, Is.EqualTo(0.024f));
            Assert.That(s.Strength, Is.EqualTo(0.6f).Within(0.02f));
            Assert.That(Vector3.Dot(s.Normal, _piano.transform.up), Is.GreaterThan(0.999f));
            Assert.That(s.Position.y, Is.EqualTo(_piano.transform.position.y + 0.001f).Within(1e-4f));
            // On the key (pressing) there is no glow for that finger (the curled middle finger may glow over F4).
            PressWithFinger(64);
            _piano.TryGetKey(64, out var e4);
            for (int i = 0; i < glow.Drawn; i++)
                Assert.That(Mathf.Abs(_piano.transform.InverseTransformPoint(glow.Spec(i).Position).x - e4.X), Is.GreaterThan(0.005f), "no glow on the pressed key");
            LiftFinger(64);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RegistrationZonesAndSurfaces()
        {
            var api = _app.Get<IPianoApi>();
            Assert.That(api, Is.SameAs(_piano));
            Assert.That(_app.Instruments[0], Is.SameAs(_piano), "TickOrder 0: the piano ticks first");
            Assert.That(_piano.Kind, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(_piano.AcceptsStickPointers, Is.False);
            Assert.That(new PianoModule().Order, Is.EqualTo(30));
            Assert.That(api.TryGetKey(60, out var c4), Is.True);
            Assert.That(c4.X, Is.EqualTo(-0.05875f).Within(1e-5f));
            Assert.That(c4.Label, Is.EqualTo("C"));
            Assert.That(api.TryGetKey(61, out var cs4) && cs4.Black, Is.True);
            Assert.That(api.TryGetKey(109, out _), Is.False);
            Assert.That(api.W, Is.EqualTo(0.0235f));
            Assert.That(api.BTOP, Is.EqualTo(0.012f));

            Rig rig = null;
            foreach (var r in _app.GetAll<Rig>()) if (r.Id == RigId.Piano) rig = r;
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.Group, Is.SameAs(_piano.transform));
            Assert.That(rig.Priority, Is.EqualTo(0));
            Assert.That(rig.HomeAll, Is.EqualTo(new Vector3(0f, 0.72f, 0.45f)));
            Assert.That(rig.Instruments, Has.Member(_piano));
            var handles = rig.Handles;
            Assert.That(handles, Is.EquivalentTo(new Renderer[] { _piano.CaseRenderer, _piano.StandRenderer, _piano.RestRenderer }));
            foreach (var p in _piano.PlayedParts) Assert.That(handles, Has.No.Member(p), "played parts are never handles");
            Assert.That(_piano.PlayedParts, Is.EquivalentTo(new Renderer[] { _piano.WhiteKeys, _piano.BlackKeys, _piano.Labels }));

            // Audio sources across the keyboard's width.
            var engine = _app.Get<IAudioEngine>();
            Assert.That(engine.Source(SourceId.PianoLo).transform.parent, Is.SameAs(_piano.transform));
            Assert.That(engine.Source(SourceId.PianoLo).transform.localPosition, Is.EqualTo(new Vector3(-0.42f, 0.05f, 0.05f)));
            Assert.That(engine.Source(SourceId.PianoHi).transform.localPosition, Is.EqualTo(new Vector3(0.42f, 0.05f, 0.05f)));

            // Zones (the piano's InPlayZone and PlaySurfaceAt over the keys).
            Vector3 L(float x, float y, float back) => _piano.transform.TransformPoint(new Vector3(x, y, back));
            Assert.That(_piano.PlaySurfaceAt(L(0f, 0.02f, 0.05f)), Is.EqualTo(PlaySurface.PianoKeys), "over the keys");
            Assert.That(_piano.InPlayZone(L(0f, 0.1f, -0.2f)), Is.True, "the apron in front is play zone");
            Assert.That(_piano.PlaySurfaceAt(L(0f, 0.1f, -0.2f)), Is.EqualTo(PlaySurface.None), "but not over the keys");
            Assert.That(_piano.InPlayZone(L(0f, 0.1f, 0.25f)), Is.False, "the music rest is a handle");
            Assert.That(_piano.InPlayZone(L(0.645f, 0.02f, 0.05f)), Is.False, "beyond the end blocks' inner edge + 3 cm");
            Assert.That(_piano.InPlayZone(L(0.635f, 0.02f, 0.05f)), Is.True);
            Assert.That(_piano.InKeySpace(L(0f, -0.04f, 0.05f)), Is.True);
            Assert.That(_piano.InKeySpace(L(0f, -0.06f, 0.05f)), Is.False, "the face under the keybed is grabbable");
            Assert.That(rig.Zone.Contains(L(0f, 0.02f, 0.05f)), Is.True);

            // Mixed reality on a real table: the stand hides, the handles follow, and it comes back.
            api.SetOnSurface(true);
            Assert.That(_piano.StandRenderer.gameObject.activeSelf, Is.False);
            Assert.That(rig.Handles, Has.No.Member(_piano.StandRenderer));
            Assert.That(rig.Handles, Is.SameAs(handles), "the same list, mutated in place (section 8)");
            float bottom = _piano.CaseRenderer.bounds.min.y;
            Assert.That(_piano.transform.position.y - bottom, Is.EqualTo(PianoSpec.TableLift).Within(0.001f), "the body sits tableLift below the key tops");
            api.ResetSurface();
            Assert.That(_piano.StandRenderer.gameObject.activeSelf, Is.True);
            Assert.That(rig.Handles, Has.Member(_piano.StandRenderer));
            Assert.That(_piano.StandRenderer.bounds.min.y, Is.EqualTo(_piano.transform.position.y - 0.72f).Within(0.002f), "the stand reaches the floor");

            // The piano's renderers: three for keys and labels, five for the body (budget: <= 22 calls, <= 17 case meshes).
            int renderers = _piano.GetComponentsInChildren<MeshRenderer>(true).Length;
            Assert.That(renderers, Is.EqualTo(8));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PressApiPlaybackAndDesktopKeyboard()
        {
            var engine = _app.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            var a = new object(); var b = new object();
            _piano.PressMidi(48, a);
            _piano.PressMidi(52, b);
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(2));
            Assert.That(Calls(tap, AudioCall.Piano, 48, engine.Source(SourceId.PianoLo)), Is.EqualTo(1), "midi < 52 plays on PianoLo");
            Assert.That(Calls(tap, AudioCall.Piano, 52, engine.Source(SourceId.PianoMid)), Is.EqualTo(1));
            Assert.That(Last(tap, AudioCall.Piano).Vel, Is.EqualTo(0.75f), "the API's default velocity");
            _piano.ReleaseMidi(48, a);
            _piano.ReleaseMidi(52, b);
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(0));
            Assert.That(Count(_events, TakeEventType.PianoOff), Is.EqualTo(2));

            _piano.PlaybackOn(84, 0.8f);
            Assert.That(Calls(tap, AudioCall.Piano, 84, engine.Source(SourceId.PianoHi)), Is.EqualTo(1));
            for (int i = 0; i < 6; i++) Frame();
            Assert.That(_piano.Action.KeyByMidi(84).Depth, Is.EqualTo(1f), "a playback press drives the key down in 45 ms");
            _piano.PlaybackOff(84);
            _piano.PlaybackReleaseAll();
            Assert.That(Calls(tap, AudioCall.Release, -1, engine.Source(SourceId.PianoHi)), Is.EqualTo(1), "releaseAll lifts the pedal");
            Assert.That(tap.Count(AudioCall.Release), Is.EqualTo(3), "on every ringing note (48, 52, 84)");

            // The computer keyboard row (DesktopInput.PianoRow): A = base 60, Z / X move the octave.
            var d = _piano.Desktop;
            d.KeyDown(0);
            d.KeyDown(7);
            Assert.That(Count(_events, TakeEventType.PianoOn, 60) + Count(_events, TakeEventType.PianoOn, 67), Is.EqualTo(2));
            d.KeyUp(0); d.KeyUp(7);
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePianoTicksInTheInstrumentsStep()
        {
            // A press made by a fingertip arrives on the recorder stream after InputDone and before the Instruments phase.
            _fake.SetPose(HandSide.Right, FingerAt(OnPiano(64, 0.03f, 0.03f)));
            Frame();
            bool seen = false;
            for (float y = 0.026f; y >= -0.014f && !seen; y -= 0.004f)
            {
                _fake.SetPose(HandSide.Right, FingerAt(OnPiano(64, y, 0.03f)));
                _probe.Log.Clear();
                _probe.Recording = true;
                Frame();
                _probe.Recording = false;
                seen = _probe.Log.Contains("pianoOn:64");
            }
            Assert.IsTrue(seen);
            int input = _probe.Log.IndexOf(nameof(FramePhase.InputDone)), on = _probe.Log.IndexOf("pianoOn:64"), inst = _probe.Log.IndexOf(nameof(FramePhase.Instruments));
            Assert.That(input, Is.LessThan(on));
            Assert.That(on, Is.LessThan(inst));
            LiftFinger(64);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DesktopClicksBelongToTheNearestPlaySurface()
        {
            // Section 0 / 15.1: the keys are one BoxCollider on Layers.PlaySurface over the resting keys; a press is the
            // piano's only when that collider is the nearest PlaySurface hit, and helpers on other layers never steal it.
            var box = _piano.KeysCollider;
            Assert.That(box, Is.Not.Null);
            Assert.That(box.gameObject.layer, Is.EqualTo(Layers.PlaySurface));
            Assert.That(box.GetComponent<Renderer>(), Is.Null, "a click target, nothing drawn");
            Assert.That(box.center, Is.EqualTo(new Vector3(0f, -0.004f, 0.075f)).Using(Vector3EqualityComparer.Instance));
            Assert.That(box.size.x, Is.EqualTo(52 * PianoSpec.W).Within(1e-6f));
            Assert.That(box.size.y, Is.EqualTo(0.032f).Within(1e-6f));
            Assert.That(box.size.z, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(_piano.PlayedParts, Has.No.Member(box.GetComponent<Renderer>()));
            Physics.SyncTransforms();

            var d = _piano.Desktop;
            _piano.TryGetKey(64, out var e4);
            Vector3 top = _piano.transform.TransformPoint(new Vector3(e4.X, 0f, 0.05f));
            var ray = new Ray(top + Vector3.up * 0.5f, Vector3.down);
            Assert.That(Physics.Raycast(ray, out var hit, 2f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide) && hit.collider == box, Is.True);
            Assert.That(d.OwnsPress(ray, 2f), Is.True, "nothing in front: the piano's click");
            Assert.That(d.Raycast(_piano.transform, ray, out _), Is.SameAs(_piano.Action.KeyByMidi(64)), "the key under the ray");

            // A helper collider on Hidden in front never takes the click; another module's PlaySurface target does.
            var helper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                helper.layer = Layers.Hidden;
                helper.transform.position = top + Vector3.up * 0.2f;
                helper.transform.localScale = Vector3.one * 0.1f;
                Physics.SyncTransforms();
                Assert.That(d.OwnsPress(ray, 2f), Is.True, "a Hidden helper in front does not block");
                helper.layer = Layers.PlaySurface;
                Physics.SyncTransforms();
                Assert.That(d.OwnsPress(ray, 2f), Is.False, "a nearer PlaySurface target (a drum, the menu face) owns the press");
                helper.transform.position = top - Vector3.up * 0.2f;
                Physics.SyncTransforms();
                Assert.That(d.OwnsPress(ray, 2f), Is.True, "one behind the keys does not");
            }
            finally { Object.DestroyImmediate(helper); }   // now: a deferred Destroy would leave it in the raycasts below

            // The collider leaves with the piano.
            _piano.gameObject.SetActive(false);
            Physics.SyncTransforms();
            Assert.That(d.OwnsPress(ray, 2f), Is.False);
            Assert.That(Physics.Raycast(ray, 2f, Layers.PlaySurfaceMask), Is.False);
            _piano.gameObject.SetActive(true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DesktopLookAroundStopsOnTheKeys()
        {
            // Section 0: AppRoot sets DesktopRigFallback.dragBlockMask = PlaySurfaceMask, so a left press on the keys plays
            // instead of turning the desktop camera, with no dragBlocked hook of the piano's own; a press beside the piano
            // still looks around, and Shift / Alt always belongs to the rig drag.
            var rig = _app.Rig;
            float t0 = Time.realtimeSinceStartup;
            while (rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(rig.State, Is.EqualTo(XrState.Desktop), "no headset: the desktop rig");
            var look = rig.Desktop;
            Assert.That(look.enabled, Is.True);
            Assert.That(look.dragBlockMask, Is.EqualTo(Layers.PlaySurfaceMask));
            Assert.That(look.dragBlocked, Is.Null, "the piano adds no drag-block hook");
            var cam = rig.HeadCamera;
            Assert.That(cam.pixelWidth, Is.GreaterThan(16), "a screen to point at");
            Physics.SyncTransforms();
            _piano.TryGetKey(60, out var c4);
            Vector3 onKey = cam.WorldToScreenPoint(_piano.transform.TransformPoint(new Vector3(c4.X, 0f, 0.05f)));
            Vector3 beside = cam.WorldToScreenPoint(_piano.transform.TransformPoint(new Vector3(1.2f, 0f, 0.05f)));
            Assert.That(onKey.z, Is.GreaterThan(0f), "middle C is in front of the desktop eye");
            Assert.That(look.DragBlocked(onKey, false), Is.True, "a press on the keys plays");
            Assert.That(_piano.Desktop.OwnsPress(cam.ScreenPointToRay(onKey), cam.farClipPlane), Is.True, "and it is the piano's");
            Assert.That(_piano.Desktop.Raycast(_piano.transform, cam.ScreenPointToRay(onKey), out _), Is.SameAs(_piano.Action.KeyByMidi(60)));
            Assert.That(look.DragBlocked(beside, false), Is.False, "a press beside the piano looks around");
            Assert.That(look.DragBlocked(beside, true), Is.True, "Shift / Alt: the rig drag");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LeavingTheRoomLetsGoOfEverything()
        {
            var engine = _app.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            _piano.PressMidi(60, "held");
            _piano.PressMidi(62, "ring"); _piano.ReleaseMidi(62, "ring");
            Frame();
            _events.Clear();
            tap.Clear();
            _piano.gameObject.SetActive(false);   // what Layout does when the piano leaves a single-instrument layout
            Assert.That(_piano.IsPresent, Is.False);
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(0));
            Assert.That(tap.Count(AudioCall.Release), Is.EqualTo(2), "both notes stop");
            foreach (var c in tap.Calls) if (c.Method == AudioCall.Release) Assert.That(c.Flag, Is.False, "without damper thuds");
            Assert.That(_piano.Action.PresserOf("held"), Is.Null, "no presser state left behind");
            Assert.That(Count(_events, TakeEventType.PianoOff, 60), Is.EqualTo(1), "the held key's release reaches the recorder stream");
            Assert.That(Count(_events, TakeEventType.PianoOff, 62), Is.EqualTo(0), "the ringing one was already released");
            Assert.That(_piano.PlaySurfaceAt(_piano.transform.TransformPoint(new Vector3(0f, 0.02f, 0.05f))), Is.EqualTo(PlaySurface.None));
            _piano.gameObject.SetActive(true);
            Assert.That(_piano.IsPresent, Is.True);
            Assert.That(_piano.Action.KeyByMidi(60).Depth, Is.EqualTo(0f));
            yield return null;
        }
    }

    /// The hot path alone: only the audio engine and the piano are booted, so nothing else can allocate in the measured region.
    public class PianoAllocTests
    {
        TestBoot _boot;
        App _app;
        PianoInstrument _piano;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, manualFrames: true);
            _boot.Options.ModuleFilter = t => t.Name == "AudioModule" || t.Name == "PianoModule";
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is PianoModule pm) _piano = pm.Piano;
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        [UnityTest]
        public IEnumerator TickAndHoverAllocateNothing()
        {
            Assert.That(_app.Get<IAudioEngine>().TestTap, Is.Null, "the test tap allocates by design");
            // Our own fingertips, fed straight to the instrument: presses, damper, sustain, glissando-free strokes, hover;
            // then a chord from the computer-keyboard row (the press API with long-lived presser objects).
            var hand = new HandState(Handedness.Right);
            var pts = new List<Pointer> { new Pointer("t-index", hand, 10), new Pointer("t-middle", hand, 15) };
            var glow = _app.HoverGlow;
            var t = _piano.transform;
            int[] keys = { 60, 61, 64, 66, 67, 72 };
            void Stroke(int midi)
            {
                _piano.TryGetKey(midi, out var k);
                float top = k.Black ? PianoSpec.BTOP : 0f, back = k.Black ? 0.09f : 0.03f;
                var p = pts[0];
                p.Active = p.WasActive = true;
                p.SampleDt = 1f / 90f;
                p.Tip = t.TransformPoint(new Vector3(k.X, top + 0.03f, back));
                for (int i = 0; i < 14; i++)
                {
                    p.Prev = p.Tip;
                    p.Tip = t.TransformPoint(new Vector3(k.X, top + 0.03f - 0.004f * (i + 1), back));
                    p.Speed = 0.36f;
                    pts[1].Active = true;
                    pts[1].Prev = pts[1].Tip;
                    pts[1].Tip = t.TransformPoint(new Vector3(k.X + 0.05f, 0.02f, 0.05f));   // a hovering neighbour
                    _app.Clock.Step(1f / 90f);
                    _piano.Tick(pts, 1f / 90f);
                    _piano.Hover(pts, glow);
                    glow.Update(0f);
                }
                p.Prev = p.Tip;
                p.Tip = t.TransformPoint(new Vector3(k.X, 0.08f, back));
                for (int i = 0; i < 12; i++) { _app.Clock.Step(1f / 90f); _piano.Tick(pts, 1f / 90f); _piano.Hover(pts, glow); glow.Update(0f); p.Prev = p.Tip; }
            }
            // The computer-keyboard row: four pressers down at once, then up (pooled presser entries, no new ones).
            var row = _piano.Desktop;
            void Chord()
            {
                for (int i = 0; i < 4; i++) row.KeyDown(14 + i);   // D5 D#5 E5 F5 (base 60): keys the strokes never touch
                _app.Clock.Step(1f / 90f);
                _piano.Tick(pts, 1f / 90f);
                for (int i = 0; i < 4; i++) row.KeyUp(14 + i);
            }
            // Warm up twice: presser entries, JIT, and the paths a first stroke never takes (a re-strike of a ringing key
            // releases its old voice; the second pass's notes leave the sustained set).
            foreach (int m in keys) Stroke(m);
            Chord();
            foreach (int m in keys) Stroke(m);
            Chord();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.1f) yield return null;
            foreach (int m in keys) _piano.Action.KeyByMidi(m).LastVel = 0f;
            for (int m = 74; m <= 77; m++) _piano.Action.KeyByMidi(m).LastVel = 0f;
            Assert.That(() => { var a = new float[512]; a[0] = 1f; }, Is.AllocatingGCMemory(), "the recorder works here");
            Assert.That(() => { foreach (int m in keys) Stroke(m); Chord(); }, Is.Not.AllocatingGCMemory());
            foreach (int m in keys) Assert.That(_piano.Action.KeyByMidi(m).LastVel, Is.GreaterThan(0.1f), $"key {m} really played in the measured strokes");
            for (int m = 74; m <= 77; m++) Assert.That(_piano.Action.KeyByMidi(m).LastVel, Is.EqualTo(0.75f), $"key {m} really played from the row");
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(0));
        }
    }

    /// Real Unity frames end to end (the default TestBoot: one App.Frame per yield, FakeHandSource driving HandInput).
    public class PianoRealFrameTests
    {
        TestBoot _boot;
        App _app;
        PianoInstrument _piano;
        FakeHandSource _fake;
        readonly List<TakeEvent> _events = new List<TakeEvent>(256);
        static readonly Quaternion Down = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        static HandPose FingerAt(Vector3 world) => HandPose.Open(HandSide.Right, Vector3.zero, Down).WithPointing().WithIndexTipAt(world);
        Vector3 OnPiano(int midi, float y, float back) { _piano.TryGetKey(midi, out var k); return _piano.transform.TransformPoint(new Vector3(k.X, y, back)); }
        static int Count(List<TakeEvent> evs, string type, int midi) { int n = 0; foreach (var e in evs) if (e.type == type && e.midi == midi) n++; return n; }

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true);
            _fake = _boot.Hands;
            _fake.SetPose(HandSide.Right, FingerAt(new Vector3(0.3f, 1.2f, 0.1f)));
            _boot.Options.ModuleFilter = PianoPlayTests.PianoAndFriends;
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is PianoModule pm) _piano = pm.Piano;
            _app.Events.Subscribe<RecorderEvent>(e => _events.Add(e.Event));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var engine = _app?.Get<IAudioEngine>();
            if (engine != null) engine.TestTap = null;
            yield return _boot.Stop();
        }

        [UnityTest]
        public IEnumerator RealFramesPressAKeyEndToEnd()
        {
            // The documented pattern: one App.Frame per yield, real frame times, FakeHandSource driving HandInput.
            var engine = _app.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            for (float y = 0.03f; y >= -0.015f; y -= 0.003f)
            {
                _fake.SetPose(HandSide.Right, FingerAt(OnPiano(60, y, 0.04f)));
                yield return null;
            }
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Count(_events, TakeEventType.PianoOn, 60), Is.EqualTo(1), "middle C sounded once");
            Assert.That(PianoPlayTests.Calls(tap, AudioCall.Piano, 60, engine.Source(SourceId.PianoMid)), Is.EqualTo(1));
            Assert.That(_piano.Action.KeyByMidi(60).Depth, Is.GreaterThan(0.9f), "the key is down under the finger");
            _fake.SetPose(HandSide.Right, FingerAt(OnPiano(60, 0.06f, 0.04f)));
            float t0 = Time.realtimeSinceStartup;
            while (_piano.HeldKeyCount > 0 && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            Assert.That(_piano.HeldKeyCount, Is.EqualTo(0));
            Assert.That(Count(_events, TakeEventType.PianoOff, 60), Is.EqualTo(1));
        }

    }
}
