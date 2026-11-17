// 10-upgrade/experience.md section 2.2 in the running app (ARCHITECTURE.md 16; TestBoot, scripted FakeHandSource hands,
// manual frames): the visual contact layer runs as App.Frame step 8a (after StickHover, before HoverGlow.Update), a
// fingertip driven 15 mm into a key is lifted (E1) while the piano still fires exactly one pianoOn (the play logic reads
// the raw joints: unchanged), the contact shadows follow the drawn finger (E3) and dim the hover glow under them, the
// mouse hand is ignored, and the update allocates nothing with ten tracked fingers over the keys.
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
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class ContactVisualsPlayTests
    {
        /// Records where step 8a runs: the first hover glow's draw multiplier as StickHover and HoverGlow see it.
        sealed class Probe : AppModule
        {
            public readonly List<string> Log = new List<string>(64);
            public bool Recording;
            App _app;
            public override int Order => 5;
            public override void Init(App app) => _app = app;
            public override void Tick(FramePhase phase, float dt)
            {
                if (Recording && (phase == FramePhase.StickHover || phase == FramePhase.HoverGlow))
                    Log.Add(phase + ":" + _app.HoverGlow.DimOf(0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        const float Dt = 1f / 90f;
        TestBoot _boot;
        FakeHandSource _fake;
        App _app;
        PianoInstrument _piano;
        Probe _probe;
        readonly List<TakeEvent> _events = new List<TakeEvent>(256);
        // Fingers forward over the keys and 35 degrees down, palm down: a playing hand (a finger along the key normal has no
        // axis to turn about).
        static readonly Quaternion Playing = Quaternion.LookRotation(new Vector3(0f, -0.7f, 1f).normalized, new Vector3(0f, 1f, 0.7f).normalized);

        static bool PianoAndFriends(System.Type t) => t.Name == "AudioModule" || t.Name == "LessonsModule" || t.Name == "PianoModule";

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, manualFrames: true);
            _fake = _boot.Hands;
            _fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, new Vector3(0.3f, 1.2f, 0.1f), Playing).WithPointing());
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

        Vector3 OnPiano(int midi, float y, float back)
        {
            _piano.TryGetKey(midi, out var k);
            return _piano.transform.TransformPoint(new Vector3(k.X, y, back));
        }

        HandPose IndexAt(HandSide side, Vector3 world) => HandPose.Open(side, Vector3.zero, Playing).WithPointing().WithIndexTipAt(world);

        int Count(string type, int midi) { int n = 0; foreach (var e in _events) if (e.type == type && e.midi == midi) n++; return n; }

        static int PianoCalls(AudioTestTap tap, int midi) { int n = 0; foreach (var c in tap.Calls) if (c.Method == AudioCall.Piano && c.Index == midi) n++; return n; }

        /// The blob of this frame nearest to a world point (the fingers' blobs come in Finger order, two each, but a finger
        /// with nothing under it adds none).
        ContactShadowPool.Blob NearestBlob(Vector3 world)
        {
            var pool = _app.Contacts.Shadows;
            Assert.That(pool.Count, Is.GreaterThan(0), "a shadow this frame");
            int best = 0;
            for (int i = 1; i < pool.Count; i++)
                if ((pool.Get(i).Centre - world).sqrMagnitude < (pool.Get(best).Centre - world).sqrMagnitude) best = i;
            return pool.Get(best);
        }

        Vector3 IndexTip => _app.HandInput.Get(HandSide.Right).position[(int)HandJoint.IndexTip];

        [UnityTest]
        public IEnumerator AFingerDrivenFifteenMillimetresIntoAKeyIsLiftedWhileThePianoPlaysExactlyOnce()
        {
            Assert.That(_app.Contacts.SurfaceCount, Is.EqualTo(0), "cached on the first frame, not at boot");
            var tap = new AudioTestTap();
            _app.Get<IAudioEngine>().TestTap = tap;
            // From 3 cm above C4 down to 15 mm into it, 4 mm per 90 Hz frame (0.36 m/s), then held there.
            for (float y = 0.03f; y >= -0.015f - 1e-4f; y -= 0.004f)
            {
                _fake.SetPose(HandSide.Right, IndexAt(HandSide.Right, OnPiano(60, y, 0.05f)));
                _boot.Step(Dt);
            }
            _fake.SetPose(HandSide.Right, IndexAt(HandSide.Right, OnPiano(60, -0.015f, 0.05f)));
            _boot.Step(Dt, 4);
            Assert.That(_app.Contacts.SurfaceCount, Is.EqualTo(2), "the piano's keys and the tape deck (Lessons)");
            float lift = _app.Contacts.Lift(HandSide.Right, Finger.Index);
            Debug.Log($"[Contact] index 15 mm into C4: lift {lift * 1000f:F1} mm, {_app.Contacts.ShadowCount} blobs, key depth {_piano.Action.KeyByMidi(60).Depth:F2}");
            Assert.That(lift, Is.GreaterThan(0.005f), "the drawn finger is lifted onto the key top");
            Assert.That(Count(TakeEventType.PianoOn, 60), Is.EqualTo(1), "exactly one pianoOn: the play logic is unchanged");
            Assert.That(PianoCalls(tap, 60), Is.EqualTo(1), "one IAudioEngine.Piano call for C4");
            Assert.That(_app.Contacts.ShadowCount, Is.GreaterThanOrEqualTo(2), "the index tip and distal joint shadow the key");
            Assert.That(Vector3.Dot(_app.Contacts.LiftNormal(HandSide.Right, Finger.Index), _piano.transform.up), Is.GreaterThan(0.99f), "along the pressed key's up axis");

            // The same press with the clamp off plays the same: one more note (the finger rose first), still one per press.
            _fake.SetPose(HandSide.Right, IndexAt(HandSide.Right, OnPiano(60, 0.06f, 0.05f)));
            _boot.Step(Dt, 30);
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f), "released (25 ms) once the finger is up");
            _app.Contacts.ClampEnabled = false;
            for (float y = 0.03f; y >= -0.015f - 1e-4f; y -= 0.004f)
            {
                _fake.SetPose(HandSide.Right, IndexAt(HandSide.Right, OnPiano(60, y, 0.05f)));
                _boot.Step(Dt);
            }
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f));
            Assert.That(Count(TakeEventType.PianoOn, 60), Is.EqualTo(2), "the same strike with and without the clamp");
            yield return null;
        }

        [UnityTest]
        public IEnumerator StepEightARunsAfterStickHoverAndDimsTheHoverGlowUnderTheShadow()
        {
            // A fingertip 2 cm above E4: a hover glow (gain .45) and the finger's soft shadow on the key.
            _fake.SetPose(HandSide.Right, IndexAt(HandSide.Right, OnPiano(64, 0.02f, 0.05f)));
            _boot.Step(Dt, 2);
            _probe.Recording = true;
            _boot.Step(Dt);
            _probe.Recording = false;
            Assert.That(_probe.Log, Is.EqualTo(new[] { "StickHover:1.00", "HoverGlow:0.50" }),
                "the glow added by the piano this frame is dimmed after StickHover and before HoverGlow.Update draws it");
            HoverGlowPool glow = _app.HoverGlow;
            Assert.That(glow.Drawn, Is.GreaterThanOrEqualTo(1));
            Assert.That(glow.Spec(0).Gain, Is.EqualTo(0.45f), "the piano's own glow spec is unchanged");
            Assert.That(glow.DimOf(0), Is.EqualTo(ContactVisuals.HoverGlowGainWithShadow), "drawn at half strength under the shadow");
            Assert.That(_app.Contacts.ShadowCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f), "2 cm above: nothing to lift");
            ContactShadowPool.Blob b = NearestBlob(IndexTip);
            Assert.That(_piano.transform.InverseTransformPoint(b.Centre).y, Is.EqualTo(0f).Within(1e-4f), "on the key tops");
            Assert.That(b.ClipHalf, Is.EqualTo(ContactShadowPool.NoClip), "resting keys share one plane: no clip");
            yield return null;
        }

        [UnityTest]
        public IEnumerator APressedKeyClipsItsShadowAndAnUntrackedHandHasNone()
        {
            // pressed 6 mm right of the key's centre line: the clip stays on the key (centred on its centre line, not the tip)
            const float off = 0.006f;
            for (float y = 0.03f; y >= -0.012f - 1e-4f; y -= 0.004f)
            {
                _fake.SetPose(HandSide.Right, IndexAt(HandSide.Right, OnPiano(62, y, 0.03f) + _piano.transform.right * off * _piano.transform.lossyScale.x));
                _boot.Step(Dt);
            }
            Assert.That(_piano.Action.KeyByMidi(62).Depth, Is.GreaterThan(0.5f), "D4 is down");
            ContactShadowPool.Blob tip = NearestBlob(IndexTip);
            float scale = _piano.transform.lossyScale.x;
            Assert.That(tip.ClipHalf, Is.EqualTo(PianoSpec.WW / 2f * scale).Within(1e-6f), "clipped to the pressed key's visible width");
            Debug.Log($"[Contact] pressed 6 mm off centre: clip centre {tip.ClipCentre * 1000f:F2} mm from the blob, half width {tip.ClipHalf * 1000f:F2} mm");
            Assert.That(tip.ClipCentre, Is.EqualTo(-off * scale).Within(0.0015f), "the clip is centred on the key's centre line, 6 mm left of the blob");
            Vector3 keyCentre = OnPiano(62, 0f, 0.03f), t = _piano.transform.right;
            float keyAlong = Vector3.Dot(t, keyCentre - tip.Centre);
            Assert.That(tip.ClipCentre, Is.EqualTo(keyAlong).Within(0.0015f), "on the key's own centre line");
            _fake.SetTracked(HandSide.Right, false);
            _boot.Step(Dt);
            Assert.That(_app.Contacts.ShadowCount, Is.EqualTo(0), "no hand, no shadow");
            Assert.That(_app.Contacts.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheContactUpdateWithTenFingersOverTheKeysAllocatesNothing()
        {
            _fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, Playing).WithIndexTipAt(OnPiano(64, -0.002f, 0.06f)));
            _fake.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, Playing).WithIndexTipAt(OnPiano(55, -0.002f, 0.06f)));
            _boot.Step(Dt, 3);
            var contacts = _app.Contacts;
            Debug.Log($"[Contact] ten fingers over the keys: {contacts.ShadowCount} blobs, {contacts.LiftedFingers} lifted");
            Assert.That(contacts.ShadowCount, Is.GreaterThanOrEqualTo(8), "the fingers shadow the keys");
            void Work() { for (int i = 0; i < 30; i++) contacts.Update(Dt); }
            Work();
            Assert.That(() => { var a = new float[1024]; a[0] = 1f; }, Is.AllocatingGCMemory(), "calibration");
            Assert.That(Work, Is.Not.AllocatingGCMemory());
            // Editor timing for the report (Mono, Mac; the device check is the [Core] contact marker, <= 0.05 ms).
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Work();
            sw.Stop();
            Debug.Log($"[Contact] step 8a with {contacts.ShadowCount} blobs: {sw.Elapsed.TotalMilliseconds * 1000.0 / 300.0:F1} us per update (editor)");
            yield return null;
        }
    }
}
