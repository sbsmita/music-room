// The tutorial's generic path in the booted app (platform.md 7.2, 12.1 GenericTutorialTests) on the handpan (phase 0 ran it
// on the handpan's stub; phase B on the real instrument): the layout "single" with the handpan current (musicroom.layoutMode
// / musicroom.current), its pack lesson "handpan-first". Blocks land at its TryGetLanding; a scripted fingertip coming down
// onto the field strikes it (HandpanPlay), the strike's "handpan" event reaches the tutorial and Matches compares its i with
// the target's; the cue (HandpanInstrument.CueOf) is 1 while it waits and off after the hit; B2 on a chord; B4 the count noun of the
// instrument (a bowls lesson brings the bowls and counts rings); B6 Sunny's re-demo at the target after 8 s and any hit
// after 15 s; CurrentEvent. Also: every pack target Validate()s through the registered ILessonTargets, and a stub's take
// plays back through its IInstrumentPlayback (the generic wiring). Manual frames; the tour was done before.
// (The "stub" in the test names is historical.)
using System.Collections;
using System.Reflection;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Handpan;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    public class GenericTutorialPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;                                              // the scripted audio clock (recorder, Sunny)
        LessonsModule _lessons;
        HandpanInstrument _handpan;

        IEnumerator Boot(string current = "handpan")
        {
            _boot = new TestBoot(headless: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, current);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Current.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Frame();
            foreach (IAppModule m in App.Current.Modules) if (m is LessonsModule l) _lessons = l;
            foreach (ILessonTargets t in App.Current.GetAll<ILessonTargets>()) if (t.Kind == InstrumentKind.Handpan) _handpan = t as HandpanInstrument;
            Assert.That(_lessons, Is.Not.Null);
            Assert.That(_handpan, Is.Not.Null, "the handpan registers its ILessonTargets");
            Assert.That(App.Current.Get<ILayoutService>().Current, Is.EqualTo(InstrumentKind.Handpan));
            Assert.IsTrue(_handpan.IsPresent, "the handpan is at the seat");
            StubRecorders();
        }

        // The stubs look up IRecorder in their Init (Order 33-37), before the Lessons module (40) registers it, so their
        // knocks reach no recorder. Until PlaceholderInstrument resolves it lazily (requested in the workstream report), the
        // test hands them the recorder the way a lazy lookup would.
        static void StubRecorders()
        {
            FieldInfo f = typeof(PlaceholderInstrument).GetField("_recorder", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(f, Is.Not.Null);
            IRecorder rec = App.Current.Get<IRecorder>();
            foreach (ILessonTargets t in App.Current.GetAll<ILessonTargets>())
                if (t is PlaceholderInstrument p && f.GetValue(p) == null) f.SetValue(p, rec);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void Frame() { _audio += Dt; _boot.Step(Dt); }

        void Step(float seconds)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 0; i < n; i++) Frame();
        }

        // A right hand with the index tip at a point, the other tips 30 cm up (only the index can touch).
        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        Pose Landing(int i)
        {
            Assert.IsTrue(_handpan.TryGetLanding(new LessonTarget { i = i }, out Pose landing, out _, out _));
            return landing;
        }

        // The fingertip pad comes down onto field i's apex along its normal at about 0.9 m/s, rests three frames, lifts and
        // goes away (HandpanPlay arms above 10 mm and strikes on the way down; HandpanFixture's stroke).
        void Tap(int i)
        {
            Vector3 c = _handpan.FieldCentre(i), n = _handpan.FieldNormal(i);
            float pad = HandRestPose.Radius[(int)HandJoint.IndexTip] * _boot.Hands.HandScale;
            void At(float h) { _boot.Hands.SetPose(HandSide.Right, IndexAt(c + n * (pad + h))); Frame(); }
            for (int k = 0; k < 6; k++) At(0.05f);
            for (float y = 0.05f; y > -0.012f; y -= 0.012f) At(y);
            for (int k = 0; k < 3; k++) At(-0.012f);
            for (float y = -0.012f; y < 0.06f; y += 0.012f) At(y);
            _boot.Hands.SetPose(HandSide.Right, IndexAt(c + n * 0.3f)); Frame();
            Step(0.12f);
        }

        Tutorial Tut => _lessons.Tutorial;

        [UnityTest]
        public IEnumerator BlocksLandOnTheStubAndAFingertipTapHitsTheTarget()
        {
            yield return Boot();
            Lesson lesson = _lessons.Library.Get("handpan-first");
            Assert.That(lesson, Is.Not.Null, "the handpan's pack is in the library");
            Tut.Start("handpan-first");
            Assert.IsTrue(Tut.Generic);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Countdown));
            Assert.IsFalse(Tut.AwaitingArrival, "the handpan is at the seat: the countdown runs at once");
            Assert.That(Tut.Shape, Is.EqualTo(BlockShape.Disc), "LessonSpec.Shape");
            Assert.That(Tut.Hint, Is.EqualTo(_handpan.Hint(lesson.events[0].targets)), "the instrument's own hint");
            Assert.That(Tut.Hint, Is.EqualTo("Tap the ding"), "the first target is the ding");
            Step(3.1f);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Playing));
            Step(1.8f);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Waiting));
            Assert.That(Tut.CurrentEvent, Is.EqualTo(0));
            int target = lesson.events[0].targets[0].i;
            Pose landing = Landing(target);
            Vector3 up = landing.rotation * Vector3.up;
            Assert.That(Tut.BlockCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(Vector3.Distance(Tut.Matrices[0].GetColumn(3), landing.position + up * Tutorial.GenericLift), Is.LessThan(1e-3f), "landed on TryGetLanding");
            Assert.That(_handpan.CueOf(target), Is.EqualTo(1f), "the instrument's cue is on the target: k = 1 exactly while it waits");
            int knocks = _handpan.LiveStrikes;
            Tap(target);
            Assert.That(_handpan.LiveStrikes, Is.EqualTo(knocks + 1), "the fingertip struck the handpan once");
            Assert.That(Tut.EventsDone, Is.EqualTo(1), "its event hit the target (Matches on i)");
            Assert.That(Tut.Mistakes, Is.EqualTo(0));
            Assert.That(Tut.CurrentEvent, Is.EqualTo(1));
            Assert.That(_handpan.CueOf(target), Is.LessThan(1f), "the hit target's cue went off (a later block of the same field may be on its way)");
            // A playback event of the same field never plays the lesson.
            Step(2.2f);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Waiting));
            Tut.OnPlay(new TakeEvent { type = "handpan", i = lesson.events[1].targets[0].i, vel = 0.5f }, true);
            Assert.That(Tut.EventsDone, Is.EqualTo(1));
            Tap(lesson.events[1].targets[0].i == 0 ? 4 : 0);
            Assert.That(Tut.Mistakes, Is.EqualTo(1), "a wrong field is a mistake");
            Tap(lesson.events[1].targets[0].i);
            Assert.That(Tut.EventsDone, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator B2OnAChordB4TheCountNounAndCurrentEvent()
        {
            yield return Boot();
            var chord = new Lesson
            {
                id = "handpan-chord-test", instrument = "handpan", title = "Chord", bpm = 60,
                events = new[] { new LessonEvent { k = 0, beat = 0f, len = 1f, targets = new[] { new LessonTarget { i = 1 }, new LessonTarget { i = 3 } } } },
            };
            Assert.That(Tut.CurrentEvent, Is.EqualTo(-1), "idle");
            Tut.Start(chord);
            Assert.That(Tut.CurrentEvent, Is.EqualTo(0));
            Step(5f);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Waiting));
            Tap(1);
            Assert.That(Tut.EventsDone, Is.EqualTo(1));
            Tap(1);
            Assert.That(Tut.Mistakes, Is.EqualTo(0), "B2: the same field again is no mistake");
            Tap(3);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Done));
            Assert.That(Tut.CurrentEvent, Is.EqualTo(-1));
            Assert.That(Tut.CountNoun, Is.EqualTo(App.Current.Registry.Get(InstrumentKind.Handpan).Lessons.CountNoun), "B4");
            // A bowls lesson brings the bowls (the single layout turns to them) and counts in its own noun.
            Tut.Start("bowls-first");
            Assert.That(App.Current.Get<ILayoutService>().Current, Is.EqualTo(InstrumentKind.Bowls), "the lesson's instrument came to the seat");
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Countdown));
            Assert.That(Tut.CountNoun, Is.EqualTo("rings"));
        }

        [UnityTest]
        public IEnumerator B6SunnyShowsTheMoveAtTheTargetAfterEightSecondsAndAnyHitCountsAfterFifteen()
        {
            yield return Boot();
            var guide = App.Current.Get<IGuide>() as GuideModule;
            Assert.That(guide, Is.Not.Null);
            Lesson lesson = _lessons.Library.Get("handpan-first");
            int target = lesson.events[0].targets[0].i;
            Tut.Start("handpan-first");
            Step(5f);
            Assert.That(Tut.State, Is.EqualTo(TutorialState.Waiting));
            Step(7f);
            Assert.That(guide.Redemos, Is.EqualTo(0), "not before 8 s");
            Step(1.2f);
            Assert.That(guide.Redemos, Is.EqualTo(1), "Sunny comes by at 8 s");
            Assert.IsTrue(guide.Sunny.InTip);
            Assert.That(guide.Sunny.Line, Is.EqualTo("Like this."));
            Assert.IsTrue(guide.Sunny.TipDemoAnchored, "the demo at the lesson's target");
            Assert.That(Vector3.Distance(guide.Sunny.TipAnchor, Landing(target).position), Is.LessThan(1e-4f));
            Assert.That(guide.Sunny.DemoKind, Is.EqualTo(App.Current.Registry.Get(InstrumentKind.Handpan).Tips[0].Demo), "the instrument's arrival demo (a tap)");
            Assert.IsFalse(guide.TipSeen(GuideModule.RedemoTipId), "a re-demo is never marked seen");
            Tap(target == 0 ? 4 : 0);
            Assert.That(Tut.Mistakes, Is.EqualTo(1), "before 15 s a wrong field is still a mistake");
            Step(7f);
            Assert.That(guide.Redemos, Is.EqualTo(1), "once per event");
            Tap(target == 0 ? 4 : 0);
            Assert.That(Tut.EventsDone, Is.EqualTo(1), "after 15 s any hit on the instrument counts");
            Assert.That(Tut.CurrentEvent, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EveryPackTargetValidatesThroughItsInstrumentsTargets()
        {
            yield return Boot();
            int n = 0;
            foreach (Lesson l in _lessons.Library.Lessons)
            {
                Assert.IsTrue(App.Current.Registry.TryParse(l.instrument, out InstrumentKind k));
                if (Tutorial.IsClassic(k)) continue;
                ILessonTargets targets = null;
                foreach (ILessonTargets t in App.Current.GetAll<ILessonTargets>()) if (t.Kind == k) targets = t;
                Assert.That(targets, Is.Not.Null, l.instrument + " registers its ILessonTargets");
                foreach (LessonEvent e in l.events)
                    foreach (LessonTarget t in e.targets)
                    {
                        Assert.IsTrue(targets.Validate(t, out string why), $"{l.id} event {e.k}: {why}");
                        Assert.IsTrue(targets.TryGetLanding(t, out _, out Vector3 approach, out Vector2 size), $"{l.id} event {e.k}: a landing");
                        Assert.That(approach.magnitude, Is.EqualTo(1f).Within(1e-3f));
                        Assert.That(size.x, Is.GreaterThan(0f));
                        n++;
                    }
            }
            Assert.That(n, Is.GreaterThanOrEqualTo(5 * 6), "the five stubs' lessons");
        }

        [UnityTest]
        public IEnumerator AStubsTakePlaysBackThroughItsPlayback()
        {
            yield return Boot();
            Recorder rec = _lessons.Recorder;
            Assert.IsTrue(rec.HasTarget("handpan"), "the handpan's onset type is wired (PlaybackWiring.WireInstruments)");
            rec.Record();
            Step(0.2f);
            Tap(2);
            Step(0.3f);
            rec.Stop();
            Assert.That(rec.Take.Count, Is.EqualTo(1), "the strike was recorded");
            Assert.That(rec.Take[0].type, Is.EqualTo("handpan"));
            int played = _handpan.PlaybackStrikes;
            Assert.IsTrue(rec.Play());
            Step(1f);
            Assert.That(_handpan.PlaybackStrikes, Is.EqualTo(played + 1), "played back through IInstrumentPlayback.Play");
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
        }
    }
}
