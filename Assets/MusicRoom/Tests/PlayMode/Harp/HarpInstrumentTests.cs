// The harp in the booted app (ARCHITECTURE.md 15.3, section 16 conventions): every test boots through TestBoot (fresh
// in-memory settings, temp DataPath) with manual frames (exactly 1/72 s per step) and a scripted audio clock, so speeds,
// dwells and `when` values are deterministic. Covered: registration (IInstrument, IHarpApi, Rig with 12 invisible
// handles, the harp source), the draw layout (4 frame draws + 1 strings mesh, no MaterialPropertyBlock), the seat-frame
// home, a scripted FakeHandSource sweep through the whole pipeline (HandInput -> PointerInput -> instruments phase ->
// HarpPlay -> IAudioEngine, observed on AudioTestTap.Calls and the RecorderEvent stream) with exact glissando spacing,
// an isolated poke played ASAP, mouse-hand pointers that never play, a hand laid on ringing strings, the IHarpApi
// playback / highlight calls (glow as vertex data), the desktop click on the PlaySurface collider and its drag, play
// zones and hover glows, a held rig that Core does not tick, and a harp leaving the scene. Sounds are asserted on
// AudioTestTap.Calls only (what the harp asked of IAudioEngine), never on the Audio module's recipe Commands.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Harp
{
    public class HarpInstrumentTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        HarpInstrument _harp;
        IAudioEngine _engine;
        double _audio;

        /// <summary>Marks both hands as the desktop mouse hand right after PointerInput.Update (InputDone), as the
        /// MouseHandSource would, so the instruments phase sees HandState.IsMouse.</summary>
        sealed class MouseHandProbe : AppModule
        {
            App _app;
            public bool On;
            public override int Order => 0;
            public override void Init(App app) { _app = app; }
            public override void Tick(FramePhase phase, float dt)
            {
                if (phase != FramePhase.InputDone || !On) return;
                foreach (HandState h in _app.Input.Hands) h.IsMouse = true;
            }
        }

        /// <summary>An IGrabService registered before Layout's (Order 0 < 100, and App.Get returns the first registered),
        /// so a test can say which rig a hand holds: Core skips the instruments of IGrabService.HeldRig.</summary>
        sealed class GrabProbe : AppModule, IGrabService
        {
            public Rig Held;
            public override int Order => 0;
            public override void Init(App app) { app.Register<IGrabService>(this); }
            public Rig HeldRig => Held;
            public bool AnyHeld => Held != null;
            public Rig GrabTarget(Vector3 point, float reach = 0.07f) => null;
        }

        IEnumerator Boot(bool headless, AppModule probe = null)
        {
            _audio = 50.0;
            _boot = new TestBoot(headless: headless, manualFrames: true, audioClock: () => _audio);
            if (probe != null) _boot.Options.ExtraModules.Add(probe);
            yield return _boot.Start();
            _app = App.Current;
            _harp = _app.Get<IHarpApi>() as HarpInstrument;
            _engine = _app.Get<IAudioEngine>();
            Assert.That(_harp, Is.Not.Null, "HarpModule registered its IHarpApi");
        }

        /// <summary>The desktop fallback (~0.5 s of Unity frames without a headset), then one step: Layout's first arrange.</summary>
        IEnumerator Seated()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(_app.Rig.State, Is.EqualTo(XrState.Desktop));
            Frame();
            Assert.IsTrue(_app.Get<ILayoutService>().FirstArrangeDone);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_engine != null) _engine.TestTap = null;
            _app = null; _harp = null; _engine = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        /// <summary>n manual frames of exactly 1/72 s; the scripted audio clock moves with them.</summary>
        void Frame(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                _audio += Dt;
                _boot.Step(Dt);
            }
        }

        Vector3 World(Vector3 local) => _harp.Root.TransformPoint(local);

        /// <summary>A right hand pointing at the string plane from the player's side, palm down, index tip at a harp-local point.</summary>
        HandPose Pointing(Vector3 local)
        {
            Vector3 fingers = _harp.Root.TransformDirection(Vector3.back);
            return HandPose.OpenFacing(HandSide.Right, Vector3.zero, fingers, Vector3.down).WithPointing().WithIndexTipAt(World(local));
        }

        static List<AudioTestTap.Call> Plucks(AudioTestTap tap) => tap.Calls.Where(c => c.Method == AudioCall.Pluck).ToList();

        static bool Same(VoiceHandle a, VoiceHandle b) => a.IsValid && a.Source == b.Source && a.Id == b.Id;

        /// <summary>Where along a level move from x0 to x1 (height y, constant depth) string i slips off: its line plus
        /// CATCH_MAX measured square to the string (the geometry, not the harp's code).</summary>
        static double SlipFraction(int i, float y, float x0, float x1)
        {
            StringLayout s = HarpGeometry.Strings[i];
            double dy = s.B.y - s.A.y, dx = s.B.x - s.A.x, len = System.Math.Sqrt(dx * dx + dy * dy);
            double xk = s.A.x + dx * (y - s.A.y) / dy;
            return (xk + 0.006 * len / dy - x0) / (x1 - x0);
        }

        [UnityTest]
        public IEnumerator RegistersTheHarpRigInstrumentAndSource()
        {
            yield return Boot(true);
            var module = _app.Modules.OfType<HarpModule>().Single();
            Assert.That(module.Order, Is.EqualTo(32));
            Assert.That(module.Harp, Is.SameAs(_harp));
            IInstrument inst = _app.Instruments.First(i => i.Kind == InstrumentKind.Harp);
            Assert.That(inst, Is.SameAs(_harp));
            Assert.That(inst.TickOrder, Is.EqualTo(20));
            Assert.IsFalse(inst.AcceptsStickPointers);
            Assert.IsTrue(inst.IsPresent);
            int at = _app.Instruments.ToList().IndexOf(inst);
            for (int i = 0; i < _app.Instruments.Count; i++)
                if (i < at) Assert.That(_app.Instruments[i].TickOrder, Is.LessThanOrEqualTo(20)); else if (i > at) Assert.That(_app.Instruments[i].TickOrder, Is.GreaterThanOrEqualTo(20));

            Rig rig = _app.Get<IRigRegistry>().Get(RigId.Harp);
            Assert.That(rig, Is.SameAs(_harp.Rig));
            Assert.That(rig.Group, Is.SameAs(_harp.Root));
            Assert.That(rig.Instruments, Does.Contain(inst));
            Assert.That(rig.Priority, Is.EqualTo(2));
            Assert.That(rig.HomeAll, Is.EqualTo(new Vector3(-0.78f, 0f, -0.2f)));
            Assert.That(rig.HomeYawAll, Is.EqualTo(83f));
            Assert.That(rig.HomeSingle.x, Is.EqualTo(-0.2966f).Within(1e-4f));
            Assert.That(rig.HomeSingle.z, Is.EqualTo(0.45f));
            Assert.That(rig.HomeYawSingle, Is.EqualTo(180f));

            var api = _app.Get<IHarpApi>();
            Assert.That(api.StringCount, Is.EqualTo(22));
            Assert.That(api.MidiOf(0), Is.EqualTo(48));
            Assert.That(api.MidiOf(21), Is.EqualTo(84));
            Assert.That(api.MidiOf(22), Is.EqualTo(-1));
            Assert.That(api.StringStart(10), Is.EqualTo(HarpGeometry.Strings[10].A));
            Assert.That(api.StringEnd(10), Is.EqualTo(HarpGeometry.Strings[10].B));
            // 10-upgrade harp.md 5.2: at most .40 up the string (C3: about .665 m, 28 cm under PlayY .95)
            StringLayout c3 = HarpGeometry.Strings[0];
            Assert.That(api.PlayPoint(0).y, Is.EqualTo(c3.A.y + 0.40f * (c3.B.y - c3.A.y)).Within(1e-4f));
            Assert.That(api.PlayPoint(0).y, Is.EqualTo(0.665f).Within(0.01f));

            // Handles: the 12 frame parts, filled once (the same list every read), invisible: each is one oriented box for
            // Layout and never a draw. The strings are one played part, never a handle.
            IReadOnlyList<Renderer> handles = rig.Handles;
            Assert.That(handles.Count, Is.EqualTo(12));
            Assert.That(rig.Handles, Is.SameAs(handles), "filled once in Init, never rebuilt");
            Assert.That(handles.Select(r => r.name), Does.Contain("Pillar").And.Contain("Neck").And.Contain("Soundbox").And.Contain("Base").And.Contain("Pins"));
            foreach (Renderer r in handles)
            {
                Assert.IsTrue(r.forceRenderingOff, r.name + " is a handle, not a draw");
                Assert.That(r.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
                Assert.That(r.transform.IsChildOf(_harp.Root), Is.True);
            }
            Assert.That(inst.PlayedParts.Count, Is.EqualTo(1), "all 22 strings are one mesh");
            Assert.That(inst.PlayedParts[0], Is.SameAs(_harp.StringsRenderer));
            for (int k = 0; k < handles.Count; k++) Assert.That(handles[k], Is.Not.SameAs(inst.PlayedParts[0]), "a string is never a handle");
            Assert.That(_harp.StringsMesh.vertexCount, Is.EqualTo(3 * 22 * _harp.VerticesPerString), "E8: cores + two blur edges");
            // the vertex alpha flags the cores (the shader's depth pass writes their depth only)
            var colours = new List<Color>();
            _harp.StringsMesh.GetColors(colours);
            int third = _harp.CopyVertexCount;
            for (int k = 0; k < colours.Count; k++)
                if (colours[k].a != (k < third ? 1f : 0f)) Assert.Fail($"vertex {k}: alpha {colours[k].a} ({(k < third ? "a core writes depth" : "a blur edge does not")})");
            Assert.That(_harp.VertexStart(21), Is.EqualTo(21 * _harp.VerticesPerString));
            Assert.That(rig.Zone, Is.Not.Null);

            // Drawn renderers (harp budget <= 6): maple, spruce, walnut, brass, strings (+ the blob when not headless).
            var drawn = _harp.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !r.forceRenderingOff).ToList();
            Assert.That(drawn.Count, Is.EqualTo(5), string.Join(", ", drawn.Select(r => r.name)));
            Assert.That(_harp.FrameDrawRenderers.Count, Is.EqualTo(4));
            foreach (MeshRenderer r in _harp.FrameDrawRenderers) Assert.That(drawn, Does.Contain(r));
            // The combined frame draws hold exactly the parts' vertices.
            int partVerts = handles.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            int drawVerts = _harp.FrameDrawRenderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            Assert.That(drawVerts, Is.EqualTo(partVerts));

            // The desktop click target: a collider on Layers.PlaySurface spanning the 1.0 x 1.4 string plane.
            Assert.That(_harp.HitCollider.gameObject.layer, Is.EqualTo(Layers.PlaySurface));
            Assert.That(_harp.HitCollider.size.x, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(_harp.HitCollider.size.y, Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(Vector3.Distance(_harp.HitCollider.transform.localPosition, new Vector3(-0.33f, 0.72f, 0f)), Is.LessThan(1e-5f));

            // The harp source hangs under the harp at (-.33, .8, 0).
            SynthSource src = _engine.Source(SourceId.Harp);
            Assert.That(src.transform.parent, Is.SameAs(_harp.Root));
            Assert.That(src.transform.localPosition, Is.EqualTo(new Vector3(-0.33f, 0.8f, 0f)));
        }

        [UnityTest]
        public IEnumerator TheFirstArrangePutsTheHarpOnTheSeatsLeft()
        {
            yield return Boot(true);
            yield return Seated();
            // On the desktop Layout never arranges: the rigs stay at their homes about the seat
            // ORIGIN (0, 1.2, 0) and the desktop camera sits 10 cm behind it. The seat, not the camera, is the reference.
            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Assert.That(Vector3.Distance(_app.Head.position, new Vector3(0f, 1.2f, -0.1f)), Is.LessThan(1e-3f), "desktop camera");
            Vector3 p = _harp.Root.position;
            Assert.That(p.x, Is.EqualTo(head.x - 0.78f).Within(1e-3f));
            Assert.That(p.y, Is.EqualTo(0f).Within(1e-4f), "on the floor");
            Assert.That(p.z, Is.EqualTo(head.z - 0.2f).Within(1e-3f));
            Assert.That(_harp.Root.eulerAngles.y, Is.EqualTo(83f).Within(0.01f), "strings square to the seat");
            // The strings face the player: the eye is on the harp's +z side, in front of the treble half.
            Vector3 eye = _harp.Root.InverseTransformPoint(head);
            Assert.That(eye.z, Is.EqualTo(0.798f).Within(0.01f));
            Assert.That(eye.x, Is.EqualTo(-0.104f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator AScriptedSweepPlucksTheStringsInOrderThroughTheApp()
        {
            yield return Boot(true);
            yield return Seated();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            var events = new List<TakeEvent>();
            System.Action<RecorderEvent> onEvent = e => events.Add(e.Event);
            _app.Events.Subscribe(onEvent);
            HandState hand = _app.Input.Hand(Handedness.Right);
            hand.LastPlayMs = double.NegativeInfinity;

            // Hold still beside string 7, then sweep level at y .95, 2 mm in front of the plane, x -.40 -> -.25 in 6 frames.
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.40f, 0.95f, 0.002f)));
            Frame(2);
            Assert.IsTrue(hand.Pointers[0].Active);
            Assert.That(Plucks(tap).Count, Is.EqualTo(0), "a still fingertip is not a stroke");
            for (int k = 1; k <= 6; k++)
            {
                _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(Mathf.Lerp(-0.40f, -0.25f, k / 6f), 0.95f, 0.002f)));
                Frame();
            }
            Frame(2);
            _app.Events.Unsubscribe(onEvent);
            Assert.That(Plucks(tap).Select(c => c.Index).ToArray(), Is.EqualTo(new[] { 7, 8, 9, 10, 11 }), "each string once, in order (only the index tip reaches the plane)");
            Assert.That(Plucks(tap).All(c => c.Handle.IsValid), Is.True, "the engine gave each pluck a voice");
            Assert.That(Plucks(tap).All(c => c.Pluck.Mode == PluckMode.Across), Is.True);
            Assert.That(Plucks(tap).All(c => c.Vel >= 0.2f && c.Vel <= 1f), Is.True, "velocityOf range");
            Assert.That(events.Where(e => e.type == TakeEventType.Harp).Select(e => e.i).ToArray(), Is.EqualTo(new[] { 7, 8, 9, 10, 11 }));
            Assert.That(events.All(e => e.mode == "across"), Is.True, "a level sweep along the plane");
            Assert.That(hand.LastPlayMs, Is.GreaterThan(double.NegativeInfinity), "plucks stamp the hand: a pinch now is play, not a grab");
            Assert.That(_harp.Play.Strings[9].HasVoice, Is.True);
            Assert.That(_harp.Play.Strings[9].Amp, Is.GreaterThan(0f), "it rings visibly");
        }

        [UnityTest]
        public IEnumerator AFastSweepIsAGlissandoOnTheBlockAlignedBaseAndAPokeIsAsap()
        {
            yield return Boot(true);
            yield return Seated();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            HandState hand = _app.Input.Hand(Handedness.Right);
            const float y = 0.95f, x0 = -0.40f, x1 = -0.25f;

            // One tracker sample sweeps 15 cm across strings 7..11: one move, five sounds spread by where each string slipped.
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(x0, y, 0.002f)));
            Frame(3);
            tap.Clear();
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(x1, y, 0.002f)));
            Frame();
            List<AudioTestTap.Call> plucks = Plucks(tap);
            int[] want = { 7, 8, 9, 10, 11 };
            Assert.That(plucks.Select(c => c.Index).ToArray(), Is.EqualTo(want));
            Pointer p = hand.Pointers[0];
            double sdt = Mathf.Max(p.SampleDt > 0f ? p.SampleDt : Dt, 1e-3f);
            // FrameNow is the scripted clock as the engine sampled it at its last Unity Update (manual steps do not
            // resample it); the sweep's base is one block after it.
            double frameNow = _engine.FrameNow;
            Assert.That(plucks[0].When, Is.GreaterThan(frameNow), "a sweep is scheduled on FrameNow + one block");
            Assert.That(plucks[0].When, Is.LessThanOrEqualTo(frameNow + 0.05));
            double f0 = SlipFraction(want[0], y, x0, x1);
            for (int k = 1; k < want.Length; k++)
            {
                double expected = (SlipFraction(want[k], y, x0, x1) - f0) * sdt;
                Assert.That(plucks[k].When - plucks[0].When, Is.EqualTo(expected).Within(2e-5), "glissando spacing of string " + want[k]);
                Assert.That(plucks[k].When, Is.GreaterThan(plucks[k - 1].When));
            }

            // A while later, an isolated poke through string 14: one sound, 'through', ASAP (when = 0).
            _boot.Hands.SetTracked(HandSide.Right, false);
            Frame(30);
            tap.Clear();
            _boot.Hands.SetTracked(HandSide.Right, true);
            Vector3 p14 = HarpGeometry.PlayPoint(14);
            _boot.Hands.SetPose(HandSide.Right, Pointing(p14 + new Vector3(0f, 0f, 0.03f)));
            Frame(3);
            _boot.Hands.SetPose(HandSide.Right, Pointing(p14 + new Vector3(0f, 0f, -0.01f)));
            Frame();
            plucks = Plucks(tap);
            Assert.That(plucks.Select(c => c.Index).ToArray(), Is.EqualTo(new[] { 14 }));
            Assert.That(plucks[0].When, Is.EqualTo(0.0), "an isolated poke plays ASAP (sections 15.3, 17)");
            Assert.That(plucks[0].Pluck.Mode, Is.EqualTo(PluckMode.Through));
        }

        [UnityTest]
        public IEnumerator TheMouseHandsPointersNeverPluck()
        {
            var probe = new MouseHandProbe();
            yield return Boot(true, probe);
            yield return Seated();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            probe.On = true;
            HandState hand = _app.Input.Hand(Handedness.Right);
            hand.LastPlayMs = double.NegativeInfinity;
            // The same fast sweep and poke as above, now from the desktop mouse hand.
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.40f, 0.95f, 0.002f)));
            Frame(3);
            Assert.IsTrue(hand.IsMouse);
            Assert.IsTrue(hand.Pointers[0].FromMouse);
            for (int k = 1; k <= 6; k++)
            {
                _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(Mathf.Lerp(-0.40f, -0.25f, k / 6f), 0.95f, 0.002f)));
                Frame();
            }
            _boot.Hands.SetPose(HandSide.Right, Pointing(HarpGeometry.PlayPoint(14) + new Vector3(0f, 0f, 0.03f)));
            Frame(3);
            _boot.Hands.SetPose(HandSide.Right, Pointing(HarpGeometry.PlayPoint(14) + new Vector3(0f, 0f, -0.01f)));
            Frame(3);
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(0), "a mouse-hand pointer never plucks (desktop double-fire)");
            Assert.That(_harp.Play.Plucks, Is.EqualTo(0));
            Assert.That(hand.LastPlayMs, Is.EqualTo(double.NegativeInfinity), "and is never read as playing");

            // The same hand from a tracker plays (lost for a moment first, so it does not sweep back from string 14).
            probe.On = false;
            _boot.Hands.SetTracked(HandSide.Right, false);
            Frame(2);
            _boot.Hands.SetTracked(HandSide.Right, true);
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.40f, 0.95f, 0.002f)));
            Frame(3);
            Assert.IsFalse(hand.IsMouse);
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.25f, 0.95f, 0.002f)));
            Frame();
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator AHandLaidOnRingingStringsDampsThemWithTheShh()
        {
            yield return Boot(true);
            yield return Seated();
            var api = _app.Get<IHarpApi>();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            foreach (int i in new[] { 8, 9, 10, 11 }) api.PlaybackPluck(i, 0.7f);
            List<AudioTestTap.Call> rung = Plucks(tap);
            Assert.That(rung.Count, Is.EqualTo(4));
            // Four fingertips on four strings, 2 mm in front of the plane (the player's side); the thumb well clear.
            HandPose pose = HandPose.Open(HandSide.Right, World(new Vector3(-0.3f, 0.75f, 0.12f)), Quaternion.identity);
            pose.Position[(int)HandJoint.IndexTip] = World(HarpGeometry.PlayPoint(8) + new Vector3(0f, 0f, 0.002f));
            pose.Position[(int)HandJoint.MiddleTip] = World(HarpGeometry.PlayPoint(9) + new Vector3(0f, 0f, 0.002f));
            pose.Position[(int)HandJoint.RingTip] = World(HarpGeometry.PlayPoint(10) + new Vector3(0f, 0f, 0.002f));
            pose.Position[(int)HandJoint.LittleTip] = World(HarpGeometry.PlayPoint(11) + new Vector3(0f, 0f, 0.002f));
            pose.Position[(int)HandJoint.ThumbTip] = World(HarpGeometry.PlayPoint(9) + new Vector3(0f, -0.1f, 0.07f));
            _boot.Hands.SetPose(HandSide.Right, pose);
            Frame(8);
            Assert.That(tap.Count(AudioCall.Damp), Is.EqualTo(0), "settling (150 ms dwell)");
            Frame(10);
            var damps = tap.Calls.Where(c => c.Method == AudioCall.Damp).ToList();
            Assert.That(damps.Count, Is.EqualTo(4), "the span 8..11 under the hand");
            Assert.That(damps.All(c => c.Hands == 4), Is.True, "Damp(h, 4): the shh of a whole hand (4 ringing strings)");
            for (int k = 0; k < 4; k++)
                Assert.That(damps.Count(d => Same(d.Handle, rung[k].Handle)), Is.EqualTo(1), "string " + rung[k].Index + "'s own voice is damped once");
            Assert.That(tap.Count(AudioCall.Release), Is.EqualTo(0), "a damp is not a re-pluck release");
            foreach (int i in new[] { 8, 9, 10, 11 }) Assert.That(_harp.Play.Strings[i].HasVoice, Is.False);
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(4), "a resting hand never plucks (only the 4 playback plucks)");
            Assert.That(_app.Input.Hand(Handedness.Right).LastPlayMs, Is.EqualTo(_app.Clock.NowMs), "a hand on the harp is playing");
        }

        [UnityTest]
        public IEnumerator PlaybackHighlightAndDampThroughTheApi()
        {
            yield return Boot(true);
            yield return Seated();
            var api = _app.Get<IHarpApi>();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            var events = new List<TakeEvent>();
            System.Action<RecorderEvent> onEvent = e => events.Add(e.Event);
            _app.Events.Subscribe(onEvent);
            api.PlaybackPluck(5, 0.6f, 0.4f, 0.8f, "across");
            AudioTestTap.Call call = Plucks(tap).Single();
            Assert.That(call.Index, Is.EqualTo(5));
            Assert.That(call.Vel, Is.EqualTo(0.6f));
            Assert.That(call.When, Is.EqualTo(0.0), "playback is live: ASAP");
            Assert.That(call.Pluck.U, Is.EqualTo(0.4f));
            Assert.That(call.Pluck.Speed, Is.EqualTo(0.8f));
            Assert.That(call.Pluck.Mode, Is.EqualTo(PluckMode.Across));
            Assert.That(call.Handle.IsValid, Is.True);
            Assert.That(events.Single().type, Is.EqualTo(TakeEventType.Harp));
            Assert.That(events[0].i, Is.EqualTo(5));
            Assert.That(events[0].vel, Is.EqualTo(0.6f));
            Assert.That(events[0].u, Is.EqualTo(0.4f));
            Assert.That(events[0].speed, Is.EqualTo(0.8f));
            Assert.That(events[0].mode, Is.EqualTo("across"));
            Assert.That(_harp.ShownGlow(5).r, Is.GreaterThan(0.15f), "the pluck lights the string at once (E8: a quarter of v1's flash)");
            api.PlaybackPluck(5, 0.6f);
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(1), "70 ms retrigger guard");
            // Past the guard, a re-pluck: today's harp first lets the ringing voice go without the piano's damper thud.
            Frame(6);
            api.PlaybackPluck(5, 0.5f);
            AudioTestTap.Call again = Plucks(tap).Last();
            {
                AudioTestTap.Call release = tap.Calls.Single(c => c.Method == AudioCall.Release);
                Assert.That(release.Flag, Is.False, "Release(h, thud: false)");
                Assert.That(Same(release.Handle, call.Handle), Is.True, "the first pluck's voice");
                Assert.That(tap.Calls.IndexOf(release), Is.LessThan(tap.Calls.IndexOf(again)), "released before the new pluck");
            }
            Assert.That(again.Pluck.Mode, Is.EqualTo(PluckMode.Through), "playback default mode");
            Assert.That(again.Pluck.U, Is.EqualTo(0.35f));
            api.PlaybackDamp(5);
            AudioTestTap.Call damp = tap.Calls.Last();
            Assert.That(damp.Method, Is.EqualTo(AudioCall.Damp));
            Assert.That(Same(damp.Handle, again.Handle), Is.True, "Damp(h) on the ringing voice");
            Assert.That(damp.Hands, Is.EqualTo(1));
            Assert.That(events.Last().type, Is.EqualTo(TakeEventType.HarpDamp));
            api.PlaybackDamp(5);
            Assert.That(tap.Count(AudioCall.Damp), Is.EqualTo(1), "a silent string has nothing to damp");

            // A lesson target glows blue on its own string only (spec 08 section 2), as vertex data of the strings mesh.
            api.Highlight(3, 1f);
            Frame();
            Assert.That(_harp.ShownGlow(3).b, Is.EqualTo(HarpPlay.TargetGlow.b).Within(1e-5f));
            Assert.That(_harp.ShownGlow(4).b, Is.EqualTo(0f));
            var glow = new List<Vector4>();
            _harp.StringsMesh.GetUVs(1, glow);
            int a3 = _harp.VertexStart(3), a4 = _harp.VertexStart(4), n = _harp.VerticesPerString;
            for (int k = 0; k < n; k++)
            {
                Assert.That(glow[a3 + k].z, Is.EqualTo(HarpPlay.TargetGlow.b).Within(1e-5f), "string 3's vertices carry the target glow");
                Assert.That(glow[_harp.EdgeStart(3, true) + k].z, Is.EqualTo(HarpPlay.TargetGlow.b).Within(1e-5f), "and its blur edges");
                Assert.That((Vector3)glow[a4 + k], Is.EqualTo(Vector3.zero), "string 4 is dark");
            }
            // No MaterialPropertyBlock anywhere on the harp (SRP Batcher, ARCHITECTURE.md section 14).
            foreach (Renderer r in _harp.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(r.HasPropertyBlock(), r.name + " has a property block");
            // The target fades once the lesson stops asking (this frame or last, the harp's paint).
            Frame(2);
            Assert.That(_harp.ShownGlow(3).b, Is.EqualTo(0f));
            _app.Events.Unsubscribe(onEvent);
            api.Highlight(-1); api.PlaybackPluck(99, 1f); api.PlaybackDamp(-3); // out of range: ignored
        }

        [UnityTest]
        public IEnumerator TheDesktopClickOnThePlaySurfacePlucksTheNearestStringAndTheDragSweeps()
        {
            yield return Boot(true);
            yield return Seated();
            Assert.IsFalse(_app.IsPresenting, "the desktop path runs while not presenting");
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            Vector3 eye = _app.Head.position;
            Ray RayTo(Vector3 local) { Vector3 w = World(local); return new Ray(eye, (w - eye).normalized); }
            _harp.ScriptedMouse = true;
            // A ray past the harp's plane does nothing.
            _harp.ScriptedRay = new Ray(eye, Vector3.up);
            _harp.ScriptedButton = true;
            Frame();
            Assert.IsFalse(_harp.DesktopCaptured);
            _harp.ScriptedButton = false;
            Frame();

            // Another module's play surface nearer on the ray owns the click (nearest PlaySurface hit wins).
            Vector3 p10 = HarpGeometry.PlayPoint(10);
            Ray click = RayTo(p10 + new Vector3(0.008f, 0f, 0f));
            var other = new GameObject("Test nearer play surface") { layer = Layers.PlaySurface };
            other.AddComponent<BoxCollider>().size = Vector3.one * 0.05f;
            other.transform.position = click.GetPoint(0.3f);
            _harp.ScriptedRay = click;
            _harp.ScriptedButton = true;
            Frame();
            Assert.IsFalse(_harp.DesktopCaptured);
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(0));
            _harp.ScriptedButton = false;
            Frame();
            Object.DestroyImmediate(other);

            // Click 8 mm beside string 10 at its play point: string 10 plucks (.7, through, ASAP).
            _harp.ScriptedButton = true;
            Frame();
            List<AudioTestTap.Call> plucks = Plucks(tap);
            Assert.That(plucks.Select(c => c.Index).ToArray(), Is.EqualTo(new[] { 10 }));
            Assert.That(plucks[0].Vel, Is.EqualTo(0.7f), "today's harp: .7");
            Assert.That(plucks[0].When, Is.EqualTo(0.0), "a click is live");
            Assert.That(plucks[0].Pluck.Mode, Is.EqualTo(PluckMode.Through));
            Assert.IsTrue(_harp.DesktopCaptured);
            // The look-around is blocked by the foundation's mask: the press ray meets the harp's collider on it.
            DesktopRigFallback desk = _app.Rig.Desktop;
            if (desk != null)
            {
                Assert.That(desk.dragBlockMask & (1 << _harp.HitCollider.gameObject.layer), Is.Not.EqualTo(0));
                Physics.SyncTransforms();
                Assert.IsTrue(Physics.Raycast(click, out RaycastHit hit, 20f, desk.dragBlockMask, QueryTriggerInteraction.Collide));
                Assert.That(hit.collider, Is.SameAs(_harp.HitCollider));
            }
            // Drag along the plane toward the treble: strings 11, 12, 13 sound, in order.
            Vector3 p13 = HarpGeometry.PlayPoint(13);
            for (int k = 1; k <= 8; k++)
            {
                _harp.ScriptedRay = RayTo(Vector3.Lerp(p10 + new Vector3(0.008f, 0f, 0f), p13 + new Vector3(0.01f, 0f, 0f), k / 8f));
                Frame();
            }
            Assert.That(Plucks(tap).Select(c => c.Index).ToArray(), Is.EqualTo(new[] { 10, 11, 12, 13 }));
            _harp.ScriptedButton = false;
            Frame();
            Assert.IsFalse(_harp.DesktopCaptured);
            Assert.IsFalse(_harp.MousePointer.Active);
            _harp.ScriptedMouse = false;
        }

        [UnityTest]
        public IEnumerator PlaySurfaceZoneAndHoverGlow()
        {
            yield return Boot(false);
            yield return Seated();
            IInstrument inst = _harp;
            Vector3 onString = World(HarpGeometry.PlayPoint(10) + new Vector3(0f, 0f, 0.03f));
            Assert.That(inst.PlaySurfaceAt(onString), Is.EqualTo(PlaySurface.HarpStrings));
            Assert.That(inst.PlaySurfaceAt(World(HarpGeometry.PlayPoint(10) + new Vector3(0f, 0f, 0.08f))), Is.EqualTo(PlaySurface.None));
            Assert.That(inst.PlaySurfaceAt(World(new Vector3(-0.69f, 0.66f, 0f))), Is.EqualTo(PlaySurface.None), "the pillar is a handle, not a play surface");
            Assert.IsTrue(_harp.Rig.Zone.Contains(onString));

            // A fingertip 2 cm short of string 10: one glow on the string, strength .6, along it, facing out of the plane.
            Vector3 tipLocal = HarpGeometry.PlayPoint(10) + new Vector3(0f, 0f, 0.02f);
            _boot.Hands.SetPose(HandSide.Right, Pointing(tipLocal));
            Frame(2);
            HoverGlowPool glow = _app.HoverGlow;
            Assert.That(glow.Drawn, Is.GreaterThanOrEqualTo(1));
            HoverGlowSpec spec = glow.Spec(0);
            Assert.That(Vector3.Distance(spec.Position, World(HarpGeometry.PlayPoint(10))), Is.LessThan(0.003f));
            Assert.That(spec.Strength, Is.EqualTo(0.6f).Within(1e-3f));
            Assert.That(spec.Length, Is.EqualTo(0.07f));
            Assert.That(spec.Width, Is.EqualTo(0.016f));
            Vector3 along = _harp.Root.TransformDirection((HarpGeometry.Strings[10].B - HarpGeometry.Strings[10].A).normalized);
            Assert.That(Vector3.Dot(spec.Axis.normalized, along), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(spec.Normal, _harp.Root.TransformDirection(Vector3.back)), Is.GreaterThan(0.999f));

            // The visual pieces in a rendering boot: the shared wood textures with the harp's repeats, the contact shadow
            // on the shared soft dot, one strings material on the custom vertex-colour shader.
            // The contact shadow belongs to Room (RigShadows, RigId.Harp): the harp builds none, so the floor gets one blob.
            Assert.That(_harp.Blob, Is.Null);
            Assert.That(_harp.Root.Find("ShadowBlob"), Is.Null);
            MeshRenderer maple = _harp.FrameDrawRenderers[(int)HarpWood.Maple];
            Assert.That(maple.sharedMaterial.GetTexture(MaterialLibrary.BaseMap), Is.SameAs(SharedTextures.Get(SharedTextures.WoodMaple)));
            Assert.That(maple.sharedMaterial.GetTextureScale(MaterialLibrary.BaseMap), Is.EqualTo(new Vector2(2f, 2f)));
            MeshRenderer spruce = _harp.FrameDrawRenderers[(int)HarpWood.Spruce];
            Assert.That(spruce.sharedMaterial.GetTexture(MaterialLibrary.BaseMap), Is.SameAs(SharedTextures.Get(SharedTextures.WoodSpruce)));
            Assert.That(spruce.sharedMaterial.GetTextureScale(MaterialLibrary.BaseMap), Is.EqualTo(new Vector2(4f, 4f)));
            Assert.That(_harp.FrameDrawRenderers[(int)HarpWood.Walnut].sharedMaterial.GetTexture(MaterialLibrary.BaseMap), Is.SameAs(SharedTextures.Get(SharedTextures.WoodWalnut)));
            Assert.That(_harp.StringsRenderer.sharedMaterial.shader.name, Is.EqualTo(HarpMaterials.StringsShader));
            Assert.That(_harp.StringsRenderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            Assert.That(maple.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On), "the body casts (MR only)");
            var colours = new List<Color>();
            _harp.StringsMesh.GetColors(colours);
            Assert.That(colours[_harp.VertexStart(0)], Is.EqualTo(HarpInstrument.StringColours[1].linear), "C strings red");
            Assert.That(colours[_harp.VertexStart(3)], Is.EqualTo(HarpInstrument.StringColours[2].linear), "F strings blue");
            Assert.That(colours[_harp.VertexStart(1)], Is.EqualTo(HarpInstrument.StringColours[0].linear), "the rest cream");
            var drawn = _harp.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled && !r.forceRenderingOff);
            Assert.That(drawn, Is.EqualTo(5), "4 frame draws + strings (Room's RigShadows draws the harp's floor blob)");
        }

        [UnityTest]
        public IEnumerator AHeldRigIsNotTickedSoItsStringsDoNotPlay()
        {
            var grab = new GrabProbe();
            yield return Boot(true, grab);
            yield return Seated();
            Assert.That(_app.Get<IGrabService>(), Is.SameAs(grab));
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            HandState hand = _app.Input.Hand(Handedness.Right);
            hand.LastPlayMs = double.NegativeInfinity;
            // The hand holds the harp rig (Layout's Rig.Held / HeldRig): the same fast sweep and poke make no sound and
            // no glow, and the harp is not read as being played.
            grab.Held = _harp.Rig;
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.40f, 0.95f, 0.002f)));
            Frame(3);
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.25f, 0.95f, 0.002f)));
            Frame();
            _boot.Hands.SetPose(HandSide.Right, Pointing(HarpGeometry.PlayPoint(14) + new Vector3(0f, 0f, 0.03f)));
            Frame(3);
            _boot.Hands.SetPose(HandSide.Right, Pointing(HarpGeometry.PlayPoint(14) + new Vector3(0f, 0f, -0.01f)));
            Frame(2);
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(0), "Core skips the instruments of IGrabService.HeldRig");
            Assert.That(_harp.Play.Plucks, Is.EqualTo(0));
            Assert.That(_harp.Play.Frame, Is.EqualTo(1), "not ticked since the grab (only the arrange frame ran)");
            Assert.That(hand.LastPlayMs, Is.EqualTo(double.NegativeInfinity));
            Assert.That(_app.HoverGlow.Drawn, Is.EqualTo(0), "no hover glow from a held harp");

            // Let go: the harp plays again (the hand is lost for a moment first, so it does not sweep back from string 14).
            grab.Held = null;
            _boot.Hands.SetTracked(HandSide.Right, false);
            Frame(2);
            _boot.Hands.SetTracked(HandSide.Right, true);
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.40f, 0.95f, 0.002f)));
            Frame(3);
            _boot.Hands.SetPose(HandSide.Right, Pointing(new Vector3(-0.25f, 0.95f, 0.002f)));
            Frame();
            Assert.That(Plucks(tap).Select(c => c.Index).ToArray(), Is.EqualTo(new[] { 7, 8, 9, 10, 11 }));
        }

        [UnityTest]
        public IEnumerator AHarpLeavingTheSceneLetsAHeldStringGoSilently()
        {
            yield return Boot(true);
            yield return Seated();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            // Catch string 10 (3 mm past), then deactivate the rig (single mode elsewhere): it returns without a sound.
            Vector3 n = new Vector3(-(HarpGeometry.Strings[10].B.y - HarpGeometry.Strings[10].A.y), HarpGeometry.Strings[10].B.x - HarpGeometry.Strings[10].A.x, 0f).normalized;
            Vector3 p = HarpGeometry.PlayPoint(10);
            _boot.Hands.SetPose(HandSide.Right, Pointing(p + n * 0.012f + new Vector3(0f, 0f, 0.002f)));
            Frame(2);
            _boot.Hands.SetPose(HandSide.Right, Pointing(p - n * 0.003f + new Vector3(0f, 0f, 0.002f)));
            Frame();
            Assert.That(_harp.Play.Strings[10].HeldBy, Is.Not.Null, "caught and bending");
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(0));
            _harp.Root.gameObject.SetActive(false);
            Assert.IsFalse(_harp.IsPresent);
            Assert.That(_harp.PlaySurfaceAt(World(p)), Is.EqualTo(PlaySurface.None), "not in the scene: no play surface");
            Assert.That(_harp.Play.Strings[10].HeldBy, Is.Null, "OnDisable lets held strings go");
            _harp.Root.gameObject.SetActive(true);
            Frame(3);
            Assert.That(tap.Count(AudioCall.Pluck), Is.EqualTo(0), "silently");
        }
    }
}
