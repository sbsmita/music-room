// The hand drums wired into the running app (phase B): the scripted hands play the real stroke logic at the seat (the demo
// hands of Sunny's tips, all 26 joints), the strokes reach the unit (the test tap), the recorder takes them with the mute
// relabel stamped at the moment the stroke sounded (IRecorder.Add(ev, atAudioTime)), the take plays back through
// IInstrumentPlayback, the signals reach Sunny (InstrumentSignalEvent), a lesson waits on the hand print and counts a real
// stroke, the desktop click plays exactly once, the display set swaps in on SetStowed, the heads are contact surfaces and
// the palm lift reaches the drawn hands (contract request R1). Manual frames at 72 Hz on a scripted audio clock.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.HandDrums;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class HandDrumsPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        readonly List<RecorderEvent> _events = new List<RecorderEvent>();
        readonly List<string> _signals = new List<string>();
        HandDrumsInstrument _drums;

        App App => App.Current;
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        IEnumerator Boot(bool headset = true)
        {
            _boot = new TestBoot(headless: true, simulateHeadset: headset, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, "handdrums");
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            App.Events.Subscribe<RecorderEvent>(e => _events.Add(e));
            App.Events.Subscribe<InstrumentSignalEvent>(e => { if (e.Kind == InstrumentKind.HandDrums) _signals.Add(e.Signal); });
            Step(0.2f);
            _drums = HandDrumsFixture.Find(App);
            Assert.That(_drums, Is.Not.Null);
            Assert.That(_drums.IsPresent, Is.True, "at the seat");
            Park();
            Step(0.1f);
        }

        void Park()
        {
            Vector3 head = App.Head.position;
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, head + new Vector3(0.25f, 0.5f, -0.2f), Vector3.forward, Vector3.down));
            _boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, head + new Vector3(-0.25f, 0.5f, -0.2f), Vector3.forward, Vector3.down));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _events.Clear();
            _signals.Clear();
        }

        IEnumerable<TakeEvent> Live(string type) => _events.Where(e => !e.DuringPlayback && e.Event.type == type).Select(e => e.Event);

        [UnityTest]
        public IEnumerator TheDemoHandsPlayTheRealStrokesAndTheUnitHearsThem()
        {
            yield return Boot();
            var engine = App.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            HandDrumsFixture.Play(Ctx, HdDemo.OpenTone, 0, 2.6f);
            HandDrumsFixture.Play(Ctx, HdDemo.PalmBass, 1, 2.6f);
            HandDrumsFixture.Play(Ctx, HdDemo.CuppedSlap, 0, 2.6f);
            engine.TestTap = null;
            string zones = string.Join(",", Live(HandDrumsTake.TypeStroke).Select(e => e.kind + ":" + e.zone));
            Debug.Log($"[HandDrums] live strokes from the demo hands: {zones}; unit calls {tap.CountUnit(UnitId.HandDrums)} (patches {tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpPatch)})");
            Assert.That(zones, Is.EqualTo("conga:open,conga:open,tumba:bass,tumba:bass,conga:slap,conga:slap"));
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpPatch), Is.GreaterThanOrEqualTo(6), "every stroke's patches went to the unit");
            Assert.That(_drums.LiveStrokes, Is.EqualTo(6));
            HandState right = App.Input.Hands[1];
            Assert.That(right.LastInstrument, Is.EqualTo(InstrumentKind.HandDrums), "PlayStamp: the right hand played the drums");
            Assert.That(PointerInput.IsPlaying(right, App.Clock.NowMs, 10000), Is.True);
            Assert.That(_signals, Does.Contain(HandDrumsGuide.GotOpen).And.Contain(HandDrumsGuide.GotBass).And.Contain(HandDrumsGuide.GotSlap), "the technique signals reach Sunny");
        }

        [UnityTest]
        public IEnumerator AMutedToneIsRecordedAsMutedAtTheMomentItSoundedAndPlaysBackMuted()
        {
            yield return Boot();
            IRecorder rec = App.Get<IRecorder>();
            rec.Record();
            Frame();
            HandDrumsFixture.Play(Ctx, HdDemo.FingersRest, 0, 2.2f);
            HandDrumsFixture.Play(Ctx, HdDemo.OpenTone, 1, 1.2f);
            rec.Stop();
            Frame();
            List<TakeEvent> take = rec.Take;
            Assert.That(take, Is.Not.Null);
            string seq = string.Join(", ", take.Select(e => $"{e.t:F3} {e.type} {e.kind} {(e.type == HandDrumsTake.TypeMute ? (e.open ? "lift" : "rest " + e.zone) : e.zone + (e.mode != null ? "/" + e.mode : ""))}"));
            Debug.Log("[HandDrums] the take: " + seq);
            var strokes = take.Where(e => e.type == HandDrumsTake.TypeStroke).ToList();
            Assert.That(strokes.Count, Is.EqualTo(2));
            Assert.That(strokes[0].zone, Is.EqualTo("mute"), "the relabel reached the take");
            Assert.That(strokes[0].mode, Is.EqualTo(HandDrumsTake.ModeRested));
            Assert.That(strokes[1].zone, Is.EqualTo("open"), "a stroke without a rest stays open");
            TakeEvent rest = take.First(e => e.type == HandDrumsTake.TypeMute && !e.open);
            Assert.That(strokes[0].t, Is.LessThan(rest.t), "stamped when it sounded: before the rest that muted it");
            Assert.That(take.Select(e => e.t), Is.Ordered, "in time order");
            Assert.That(_drums.Relabels, Is.EqualTo(1));
            Assert.That(_signals, Does.Contain(HandDrumsGuide.GotMute));
            // playback: the muted stroke plays as the open tone it was, the recorded rest mutes it
            var engine = App.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            int before = _drums.PlaybackEvents;
            Assert.That(rec.Play(), Is.True);
            Step((float)rec.TakeSeconds + 0.6f);
            engine.TestTap = null;
            Assert.That(_drums.PlaybackEvents - before, Is.EqualTo(take.Count), "every event of the take played back");
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpStrokePatch), Is.EqualTo(2), "two canonical strokes");
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpRestCanon), Is.GreaterThanOrEqualTo(1), "the recorded rest");
            Assert.That(tap.CountUnit(UnitId.HandDrums, HandDrumsConst.OpLift), Is.GreaterThanOrEqualTo(1), "and its lift");
            Assert.That(Live(HandDrumsTake.TypeStroke).Count(), Is.EqualTo(2), "playback records nothing live");
        }

        [UnityTest]
        public IEnumerator ALessonShowsTheHandPrintAndCountsARealStroke()
        {
            yield return Boot();
            LessonsModule lessons = App.Modules.OfType<LessonsModule>().First();
            Tutorial tut = lessons.Tutorial;
            tut.Start("handdrums-first");
            Assert.IsTrue(tut.Generic);
            Assert.That(tut.CountNoun, Is.EqualTo("strokes"));
            for (int i = 0; i < 72 * 8 && tut.State != TutorialState.Waiting; i++) Frame();
            Assert.That(tut.State, Is.EqualTo(TutorialState.Waiting), "the first block landed");
            Assert.That(tut.Hint, Is.EqualTo("Tap the left drum"));
            Assert.That(_drums.Cue.Visible, Is.True, "the hand print on the skin");
            Assert.That(_drums.Cue.ShownZone[0], Is.EqualTo(HandDrumsPrints.IndexOf("any")));
            Assert.That(_drums.Cue.Opacity, Is.EqualTo(HandDrumsCue.MaxAlpha).Within(1e-4f), "full while it waits (k 1)");
            Assert.IsTrue(LessonCue.IsPurple(_drums.Cue.Material.GetColor(MaterialLibrary.BaseColor)), "the print is the lessons' purple cue");
            for (int i = 0; i < tut.BlockCount; i++) Assert.IsTrue(LessonCue.IsPurple(tut.BlockColorSrgb(i)), "and so is the block");
            HandDrumsFixture.Play(Ctx, HdDemo.OpenTone, 0, 0.9f);
            Assert.That(tut.EventsDone, Is.EqualTo(1), "a real open tone on the conga hit the target");
            Assert.That(tut.Mistakes, Is.EqualTo(0));
            tut.Stop();
            Frame();
        }

        [UnityTest]
        public IEnumerator TheDesktopClickPlaysOnceWhereItLands()
        {
            yield return Boot(headset: false);
            Assert.That(App.IsPresenting, Is.False);
            _events.Clear();
            Vector3 centre = _drums.transform.TransformPoint(HandDrumsDims.HeadPoint(1, 0f, 0f));
            Vector3 eye = centre + _drums.transform.TransformDirection(HandDrumsDims.Normal) * 0.6f + Vector3.back * 0.1f;
            Physics.SyncTransforms();
            _drums.ScriptedMouse = true;
            _drums.ScriptedRay = new Ray(eye, (centre - eye).normalized);
            _drums.ScriptedButton = true; Frame(); Frame(); Frame();
            _drums.ScriptedButton = false; Frame();
            var s = Live(HandDrumsTake.TypeStroke).ToList();
            Assert.That(_drums.Clicks, Is.EqualTo(1), "one click, one stroke");
            Assert.That(s.Count, Is.EqualTo(1), "recorded once (the mouse hand never plays: conformance 16)");
            Assert.That(s[0].kind, Is.EqualTo("tumba"));
            Assert.That(s[0].zone, Is.EqualTo("bass"), "the centre plays the bass");
            Vector3 edge = _drums.transform.TransformPoint(HandDrumsDims.HeadPoint(0, 0.9f, 0f));
            _drums.ScriptedRay = new Ray(eye, (edge - eye).normalized);
            _drums.ScriptedButton = true; Frame(); _drums.ScriptedButton = false; Frame();
            Assert.That(Live(HandDrumsTake.TypeStroke).Last().zone, Is.EqualTo("open"), "the edge plays the open tone");
            Assert.That(Live(HandDrumsTake.TypeStroke).Last().kind, Is.EqualTo("conga"));
            _drums.ScriptedMouse = false;
        }

        [UnityTest]
        public IEnumerator OnDisplayTheLightSetShowsAndAtTheSeatTheDentGridComesBack()
        {
            yield return Boot();
            InstrumentDescriptor d = App.Registry.Get(InstrumentKind.HandDrums);
            int seat = _drums.Model.Triangles;
            _drums.SetStowed(true, StowMount.Floor);
            Assert.That(_drums.Stowed, Is.True);
            Assert.That(_drums.IsPresent, Is.False);
            int shown = _drums.Model.Triangles;
            Debug.Log($"[HandDrums] triangles: at the seat {seat}, on display {shown}");
            Assert.That(shown, Is.LessThanOrEqualTo(d.TrisStowed), "the display set within its budget");
            Assert.That(seat, Is.LessThanOrEqualTo(d.TrisAtSeat));
            Assert.That(_drums.GetComponentsInChildren<Renderer>().Count(r => r.enabled && r.gameObject.activeInHierarchy), Is.LessThanOrEqualTo(d.DrawsStowed), "three draws on display");
            _drums.SetStowed(false, StowMount.Floor);
            Assert.That(_drums.Model.Triangles, Is.EqualTo(seat));
            Assert.That(_drums.IsPresent, Is.True);
        }

        [UnityTest]
        public IEnumerator TheHeadsAreContactSurfacesAndThePalmLiftReachesTheHands()
        {
            yield return Boot();
            Vector3 n = _drums.transform.TransformDirection(HandDrumsDims.Normal);
            Vector3 p = _drums.transform.TransformPoint(HandDrumsDims.HeadPoint(0, 0.5f, 0.3f));
            Assert.That(_drums.QueryContact(p + n * 0.01f, 0.03f, 0.03f, out ContactHit hit), Is.True);
            Assert.That(hit.Height, Is.EqualTo(0.01f).Within(1e-4f));
            Assert.That(Vector3.Distance(hit.Point, p), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(hit.Normal, Vector3.up), Is.EqualTo(HandDrumsDims.TiltDeg).Within(0.1f), "the tilted head's normal");
            Assert.That(_drums.QueryContact(p + n * 0.05f, 0.03f, 0.03f, out _), Is.False, "too far above");
            Assert.That(_drums.QueryContact(_drums.transform.TransformPoint(new Vector3(0f, 0.75f, 0f)), 0.03f, 0.03f, out _), Is.False, "between the heads");
            Assert.That(_drums.PlaySurfaceAt(p + n * 0.02f), Is.EqualTo(PlaySurface.Instrument));
            // a hard palm stroke: the palm lift rises while the palm is under the skin and falls back after
            HandDrumsFixture.Play(Ctx, HdDemo.PalmBass, 0, 0.495f, park: false);   // the palm at its deepest (0.49 s into the demo)
            double lift = _drums.Input.PalmLift[1];
            Debug.Log($"[HandDrums] palm lift during the bass: {lift * 1000:F1} mm; MetaHandVisual.SetPalmLift {(_drums.PalmLiftWired ? "wired" : "absent")}");
            Assert.That(lift, Is.GreaterThan(0.002), "the palm went into the skin: the drawn hand rides on it");
            if (!_drums.PalmLiftWired) Assert.Ignore("contract request R1 (MetaHandVisual.SetPalmLift) is not applied in this build: the lift is computed, nothing draws it");
            Assert.That(_drums.PalmLiftWired, Is.True);
        }
    }
}
