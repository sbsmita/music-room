// E4 in the running app: App.FastMotion (step 18) reads the drumming intent from the recorder stream (a drum hit that is
// not playback), the layout (single mode on the drums) and the sticks, and asks for Fast Motion Mode; without a headset
// runtime nothing reaches OVRPlugin (Sent stays 0). Manual frames: every step is exactly 1/90 s. The platform (platform.md
// 4.6, 12.3): any FastMotion instrument's live Onsets ask for it like drum hits (the hand drums and the marimba, each
// struck at the seat by its own conformance fixture), its playback does not, and the piano's never do.
using System.Collections;
using System.Linq;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class FastMotionDriverTests
    {
        const float Dt = 1f / 90f;
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest]
        public IEnumerator DrummingTurnsFastMotionOnAndFiveQuietSecondsTurnItOff()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            yield return _boot.Start();
            App app = _boot.App;
            _boot.Step(Dt, 3);
            FastMotionPolicy policy = app.FastMotion.Policy;
            Assert.IsFalse(policy.On, "not drumming: the low-jitter default");

            // A drum hit (the recorder publishes it, not during playback): on in the same frame.
            var drums = app.Get<IDrumsApi>();
            Assert.That(drums, Is.Not.Null);
            drums.PlaybackHit("snare", 0.8f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" });
            _boot.Step(Dt);
            Assert.IsTrue(policy.On, "on at once: never late for the first stroke");
            Assert.That(app.FastMotion.Sent, Is.EqualTo(0), "no headset runtime: the hint is not sent");

            // 4 s of recent-hit intent + 5 s of quiet, then off.
            _boot.Step(Dt, (int)(8.8f * 90));
            Assert.IsTrue(policy.On, "still within 9 s of the hit");
            _boot.Step(Dt, 30);
            Assert.IsFalse(policy.On, "off after 5 s without drumming intent");

            // Single mode on the drums keeps it on without a hit.
            var layout = app.Get<ILayoutService>();
            layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            _boot.Step(Dt);
            Assert.IsTrue(policy.On, "single mode on the drums");
            layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            _boot.Step(Dt, 5 * 90 + 2);
            Assert.IsFalse(policy.On, "at the piano: off again 5 s later");
        }

        [UnityTest]
        public IEnumerator TheHandDrumsLiveOnsetsAskForFastMotionLikeDrumHits() => FastMotionOnset(InstrumentKind.HandDrums, "handDrum");

        [UnityTest]
        public IEnumerator TheMarimbasLiveOnsetsAskForFastMotionToo() => FastMotionOnset(InstrumentKind.Marimba, "marimba");

        /// <summary>The instrument at the seat in My set with the piano; the piano's live notes never ask for FMM, its own
        /// fixture's stroke (its live Onset <paramref name="onset"/>) does at once, five quiet seconds later it is off,
        /// and the same event played back from a take does not.</summary>
        IEnumerator FastMotionOnset(InstrumentKind kind, string onset)
        {
            double audio = 20.0;
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true, audioClock: () => audio);
            // The piano and the marimba do not fit together (both need the front: platform.md 4.2), so the marimba sits with the harp.
            _boot.Settings.SetString(AppSettings.KeySet, (kind == InstrumentKind.Marimba ? "harp," : "piano,") + InstrumentIds.Of(kind));
            yield return _boot.Start();
            App app = _boot.App;
            void Run(int n) { for (int i = 0; i < n; i++) { audio += Dt; _boot.Step(Dt); } }
            Run(3);
            InstrumentDescriptor d = app.Registry.Get(kind);
            Assume.That(d != null && app.Registry.IsLoaded(kind), kind + " is loaded");
            Assert.That(d.Has(InstrumentTraits.FastMotion), Is.True, kind + " has the FastMotion trait");
            Assert.That(d.Events.First(e => e.Role == EventRole.Onset).Type, Is.EqualTo(onset));
            FastMotionPolicy policy = app.FastMotion.Policy;
            Assert.IsFalse(policy.On);

            // The piano's live notes: never (when it sits in the set).
            var piano = app.Get<IPianoApi>();
            var presser = new object();
            var events = new System.Collections.Generic.List<RecorderEvent>();
            app.Events.Subscribe<RecorderEvent>(e => events.Add(e));
            if (app.Get<ILayoutService>().IsInScene(InstrumentKind.Piano))
            {
                piano.PressMidi(60, presser);
                Run(1);
                piano.ReleaseMidi(60, presser);
                Run(3);
                Assert.That(events.Any(e => e.Event.type == TakeEventType.PianoOn && !e.DuringPlayback), Is.True, "a live piano note was recorded");
                Assert.IsFalse(policy.On, "the piano never asks for Fast Motion Mode");
            }

            // A live onset at the seat, played by the instrument's own conformance fixture (a scripted stroke through its real
            // contact logic; a phase-0 stub's PlaceholderFixture knocks its top): FMM on in the frame its Onset is recorded.
            IInstrument inst = app.Instruments.Single(i => i.Kind == kind);
            Assert.That(inst.IsPresent, Is.True, kind + " at the seat (My set)");
            IInstrumentFixture fx = FixtureContext.For(kind);
            Assert.That(fx, Is.Not.Null, kind + " has a conformance fixture");
            events.Clear();
            bool seen = false, onAtOnset = false;
            fx.Strike(new FixtureContext
            {
                Boot = _boot,
                Frame = () =>
                {
                    Run(1);
                    if (!seen && events.Any(e => e.Event.type == onset && !e.DuringPlayback)) { seen = true; onAtOnset = policy.On; }
                },
            });
            Assert.That(seen, Is.True, "its live Onset " + onset + " recorded");
            Assert.IsTrue(onAtOnset, kind + ": on at once, like a drum hit");
            Assert.That(app.FastMotion.Sent, Is.EqualTo(0), "no headset runtime: the hint is not sent");
            TakeEvent played = events.First(e => e.Event.type == onset && !e.DuringPlayback).Event;
            Run((int)(9.2f * 90));
            Assert.IsFalse(policy.On, "off after the recent-hit window and 5 quiet seconds");

            // The same event from a take: played back through its IInstrumentPlayback (a unit call on the test tap), not drumming.
            var recorder = app.Get<IRecorder>();
            var engine = app.Get<MusicRoom.Audio.IAudioEngine>();
            var tap = new MusicRoom.Audio.AudioTestTap();
            engine.TestTap = tap;
            played.t = 0.05f;
            var take = new System.Collections.Generic.List<TakeEvent> { played };
            Assert.That(recorder.PlayEvents(take, 0.5), Is.True);
            for (int i = 0; i < 90 && tap.Calls.Count == 0; i++) Run(1);
            engine.TestTap = null;
            Assert.That(tap.Calls.Count, Is.GreaterThan(0), "the take played it back");
            Run(3);
            Assert.IsFalse(policy.On, "a played-back " + onset + " does not ask for Fast Motion Mode");
            recorder.Stop();
        }

        [UnityTest]
        public IEnumerator ADimRoomBlocksFastMotionAndSunnyShowsOneTextHint()
        {
            // experience.md 14 item 4: when the low-light guard trips, one event, and Sunny's one caption (no voice).
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no first-launch tour (the hint would wait for its end)
            yield return _boot.Start();
            App app = _boot.App;
            MusicRoom.Guide.GuideModule guide = null;
            foreach (var m in app.Modules) if (m is MusicRoom.Guide.GuideModule g) guide = g;
            Assert.That(guide, Is.Not.Null);
            _boot.Step(Dt, 3);
            Assert.IsFalse(guide.Sunny.Present);
            int events = 0;
            app.Events.Subscribe<HandsLowLightEvent>(e => events++);
            // two tracked hands far from the instruments, at low confidence; single mode on the drums wants FMM
            _boot.Hands.HighConfidence = false;
            _boot.Hands.SetPose(MusicRoom.Hands.HandSide.Left, MusicRoom.Hands.HandPose.Open(MusicRoom.Hands.HandSide.Left, new UnityEngine.Vector3(-0.5f, 2.2f, -0.6f), UnityEngine.Quaternion.identity));
            _boot.Hands.SetPose(MusicRoom.Hands.HandSide.Right, MusicRoom.Hands.HandPose.Open(MusicRoom.Hands.HandSide.Right, new UnityEngine.Vector3(0.5f, 2.2f, -0.6f), UnityEngine.Quaternion.identity));
            _boot.Hands.SetTracked(MusicRoom.Hands.HandSide.Left, true);
            _boot.Hands.SetTracked(MusicRoom.Hands.HandSide.Right, true);
            app.Get<ILayoutService>().Set(LayoutMode.Single, InstrumentKind.Drums);
            FastMotionPolicy policy = app.FastMotion.Policy;
            _boot.Step(Dt, 3 * 90);              // 1 s after the on request, then 3 hand-seconds (two hands: 1.5 s)
            Assert.IsTrue(policy.Blocked, "the low-light guard tripped");
            Assert.IsFalse(policy.On, "FMM is off for the rest of the session");
            Assert.That(events, Is.EqualTo(1), "one HandsLowLightEvent");
            Assert.That(app.FastMotion.LowLightEvents, Is.EqualTo(1));
            Assert.IsTrue(guide.LightHintShown, "Sunny shows the hint");
            Assert.IsTrue(guide.Sunny.Hinting);
            Assert.That(guide.Sunny.Line, Is.EqualTo(MusicRoom.Guide.GuideLines.MoreLightHint));
            Assert.That(guide.Sunny.LineId, Is.EqualTo(""), "text only: no recording");
            bool doneBefore = guide.Sunny.Done;
            _boot.Step(Dt, 6 * 90);
            Assert.That(events, Is.EqualTo(1), "never twice");
            Assert.IsFalse(guide.Sunny.Present, "the hint ends by itself");
            Assert.IsFalse(guide.Sunny.Hinting);
            Assert.That(guide.Sunny.Done, Is.EqualTo(doneBefore), "the hint leaves the tour's state alone");
        }
    }
}
