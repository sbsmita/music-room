// The handpan's module in the booted app (headless, manual frames; the layout "single" with the handpan at the seat):
// the desktop click on its collider plays once and never twice (conformance 16: the mouse hand never strikes), a live
// strike records a "handpan" Onset (not Held: the looper never mutes it at a loop's end) and a resting finger a
// "handpanMute"; playback of a tip, a tak and a mute reaches the unit; Sunny's signals are published; going on display
// releases a resting hand's damps and nothing plays while stowed; the source hangs under the rig.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Handpan;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Handpan
{
    public class HandpanModuleTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        HandpanInstrument _hp;
        readonly List<string> _signals = new List<string>();

        IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, "handpan");
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Current.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Frame();
            foreach (IAppModule m in App.Current.Modules) if (m is HandpanModule hm) _hp = hm.Instrument;
            Assert.That(_hp, Is.Not.Null);
            Assert.IsTrue(_hp.IsPresent, "at the seat");
            App.Current.Events.Subscribe<InstrumentSignalEvent>(OnSignal);
        }

        void OnSignal(InstrumentSignalEvent e) { if (e.Kind == InstrumentKind.Handpan) _signals.Add(e.Signal); }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null)
            {
                App.Current?.Events.Unsubscribe<InstrumentSignalEvent>(OnSignal);
                yield return _boot.Stop();
            }
            _boot = null;
        }

        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

        void Index(Vector3 tip) => _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(tip));

        /// <summary>The index pad down onto field i (along its normal), a dwell of <paramref name="dwell"/> frames, then up and away.</summary>
        void Tap(int i, int dwell = 3)
        {
            Vector3 c = _hp.FieldCentre(i), n = _hp.FieldNormal(i);
            float pad = HandRestPose.Radius[(int)HandJoint.IndexTip] * _boot.Hands.HandScale;
            void At(float h) { Index(c + n * (pad + h)); Frame(); }
            for (int k = 0; k < 6; k++) At(0.05f);
            for (float y = 0.05f; y > -0.012f; y -= 0.012f) At(y);
            for (int k = 0; k < dwell; k++) At(k < 3 ? -0.012f : -0.004f);   // then resting lightly (HandpanPlay's band after a strike: -8..+6 mm)
            for (float y = -0.012f; y < 0.06f; y += 0.012f) At(y);
            Index(c + n * 0.3f); Frame();
            Frames(6);
        }

        [UnityTest]
        public IEnumerator ADesktopClickPlaysOnceAndTheMouseHandNeverStrikes()
        {
            yield return Boot();
            var tap = new AudioTestTap();
            App.Current.Get<IAudioEngine>().TestTap = tap;
            Assert.IsFalse(App.Current.IsPresenting, "the desktop path");
            // the desktop camera is the head: a click on A3 is the ray from the eyes to its apex
            Vector3 a3 = _hp.FieldCentre(1), eye = App.Current.Head.position;
            _hp.ScriptedMouse = true;
            _hp.ScriptedRay = new Ray(eye, (a3 - eye).normalized);
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(_hp.ScriptedRay, out RaycastHit h, 20f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide) && h.collider == _hp.Model.DesktopCollider,
                "the ray meets the handpan's own play-surface collider");
            Assert.That(Vector3.Distance(h.point, a3), Is.LessThan(0.01f), "the coarse collider lies on the steel");
            Debug.Log($"[Handpan] desktop: collider normal at A3 {h.normal.ToString("F3")}, FieldNormal(1) {_hp.FieldNormal(1).ToString("F3")}, angle {Vector3.Angle(h.normal, _hp.FieldNormal(1)):F1} deg");
            int before = _hp.LiveStrikes;
            _hp.ScriptedButton = true;
            Frames(5);                                                   // held down 5 frames: one strike
            _hp.ScriptedButton = false;
            Frames(2);
            Assert.That(_hp.LiveStrikes, Is.EqualTo(before + 1), "one click, one strike");
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpStrike), Is.EqualTo(1), "one sound call");
            _hp.ScriptedButton = true;                                   // a second click elsewhere: off the instrument, nothing
            _hp.ScriptedRay = new Ray(a3 + Vector3.right * 2f + Vector3.up, Vector3.down);
            Frames(2);
            _hp.ScriptedButton = false;
            Frames(2);
            Assert.That(_hp.LiveStrikes, Is.EqualTo(before + 1), "a click beside the handpan plays nothing");
            _hp.ScriptedMouse = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiveStrikesRecordAndPlaybackReachesTheUnit()
        {
            yield return Boot();
            InstrumentDescriptor d = App.Current.Registry.Get(InstrumentKind.Handpan);
            EventTypeSpec onset = System.Array.Find(d.Events, e => e.Type == HandpanDefinition.Onset);
            Assert.That(onset.Role, Is.EqualTo(EventRole.Onset));
            Assert.IsFalse(onset.Held, "a struck field rings on: never muted at a loop's end");
            var lessons = (LessonsModule)System.Linq.Enumerable.First(App.Current.Modules, m => m is LessonsModule);
            Recorder rec = lessons.Recorder;
            rec.Record();
            Frames(3);
            Tap(1);
            Tap(0);
            Tap(3, dwell: 50);                                           // rests 0.7 s after it sounded: a finger mute
            Frames(10);
            rec.Stop();
            var types = new List<string>();
            foreach (TakeEvent e in rec.Take) types.Add(e.type + ":" + e.i);
            Debug.Log("[Handpan] take: " + string.Join(", ", types));
            Assert.That(types, Does.Contain("handpan:1"));
            Assert.That(types, Does.Contain("handpan:0"));
            Assert.That(types, Does.Contain("handpan:3"));
            Assert.That(types, Does.Contain("handpanMute:3"), "the resting finger muted D4");
            TakeEvent first = rec.Take[0];
            Assert.That(first.zone, Is.EqualTo("tone"));
            Assert.That(first.mode, Is.EqualTo("tip"));
            Assert.That(first.midi, Is.EqualTo(57), "A3");

            // playback: a tip, a tak, a mute and its lift reach the unit
            var tap = new AudioTestTap();
            App.Current.Get<IAudioEngine>().TestTap = tap;
            IInstrumentPlayback pb = null;
            foreach (IInstrumentPlayback p in App.Current.GetAll<IInstrumentPlayback>()) if (p.Kind == InstrumentKind.Handpan) pb = p;
            Assert.IsTrue(pb.Play(new TakeEvent { type = "handpan", i = 4, vel = 0.6f, u = 0.3f, pos = 0.1f, mode = "tip", zone = "tone" }));
            Assert.IsTrue(pb.Play(new TakeEvent { type = "handpan", i = 9, vel = 0.6f, u = 0.05f, pos = -0.12f, mode = "tak", zone = "tak" }));
            Assert.IsTrue(pb.Play(new TakeEvent { type = "handpanMute", i = 4, vel = 0.8f, zone = "1", mode = "finger" }));
            Assert.IsTrue(pb.Play(new TakeEvent { type = "handpanMute", i = 4, vel = 0f, zone = "1", mode = "finger" }));
            Assert.IsFalse(pb.Play(new TakeEvent { type = "drum", i = 1 }), "not ours");
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpStrike), Is.EqualTo(2), "the tip and the tak");
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpDamp), Is.EqualTo(2), "the mute and its lift");
            Assert.That(_hp.PlaybackStrikes, Is.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SunnysSignalsAndTheSourceUnderTheRig()
        {
            yield return Boot();
            for (int k = 0; k < 5; k++) Tap(1 + k % 4);                  // five ring notes, never the ding
            Assert.That(_signals, Does.Contain(HandpanTips.SignalNoDing), "five ring strikes and no ding");
            Tap(0);
            Assert.That(_signals, Does.Contain(HandpanTips.SignalDingStrike));
            SynthSource src = App.Current.Get<IAudioEngine>().Source(SourceId.Handpan);
            Assert.IsTrue(src.transform.IsChildOf(_hp.Rig.Group), "the source hangs under the rig");
            Assert.That(Vector3.Distance(src.transform.position, _hp.SoundOrigin), Is.LessThan(1e-4f), "at the shell's centre, 6 cm over the rim plane");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnDisplayARestingHandsDampsAreReleasedAndNothingPlays()
        {
            yield return Boot();
            var tap = new AudioTestTap();
            App.Current.Get<IAudioEngine>().TestTap = tap;
            // a finger resting on D4 (sounded, then left on the steel): its damp is held
            Vector3 c = _hp.FieldCentre(3), n = _hp.FieldNormal(3);
            float pad = HandRestPose.Radius[(int)HandJoint.IndexTip] * _boot.Hands.HandScale;
            for (int k = 0; k < 6; k++) { Index(c + n * (pad + 0.05f)); Frame(); }
            for (float y = 0.05f; y > -0.012f; y -= 0.012f) { Index(c + n * (pad + y)); Frame(); }
            for (int k = 0; k < 50; k++) { Index(c + n * (pad - 0.004f)); Frame(); }
            int damps = tap.CountUnit(UnitId.Handpan, HandpanState.OpDamp);
            Assert.That(damps, Is.GreaterThanOrEqualTo(1), "the resting finger damps D4");
            _hp.SetStowed(true, StowMount.Wall);
            Assert.That(tap.CountUnit(UnitId.Handpan, HandpanState.OpDamp), Is.GreaterThan(damps), "going on display lifts the damp");
            Assert.IsFalse(_hp.IsPresent);
            Assert.That(_hp.Model.Current, Is.EqualTo(HandpanModel.Config.Wall));
            int strikes = _hp.LiveStrikes;
            Tap(1);
            Assert.That(_hp.LiveStrikes, Is.EqualTo(strikes), "nothing plays while it is on display");
            Assert.That(_hp.PlaySurfaceAt(_hp.FieldCentre(1)), Is.EqualTo(PlaySurface.None));
            _hp.SetStowed(false, StowMount.Wall);
            Assert.That(_hp.Model.Current, Is.EqualTo(HandpanModel.Config.Seat));
            Assert.IsTrue(_hp.IsPresent);
            yield return null;
        }
    }
}
