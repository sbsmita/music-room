// Sunny's menu page "Loop a beat" starts Sunny's loop show (Guide/DeckGuide.cs, Quick; until 2026-11-18 the deck's GUIDE did): SNOW on -> LOOP and a drum beat -> LOOP and
// a second instrument on top -> STOP (saved) -> PLAY (the playback show: RecordingPlaybackEvent + PlaybackNoteEvent) -> a
// line of wonder -> "Have fun!". In the booted app with the scripted audio clock and manual frames:
//   - a prompt player (acting ~0.6 s after each ask) is through it all within 60 s of simulated time, with no skip, give-up
//     or assist, Sunny never speaking during a count-in or a recording pass, the loop still playing after;
//   - a player who does nothing: Sunny's assists (snow, LOOP, PLAY) and give-ups still end it within a bounded time, the
//     deck left tidy;
//   - in the single-instrument layout the drums and the piano come in for their layers, the snow step is passed over when
//     it already snows, and the player's layout comes back once the show's playback has ended.
using System;
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Lessons;
using MusicRoom.Tests.PlayMode.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class DeckQuickShowTests
    {
        const float Dt = 1f / 30f;
        const float React = 0.6f;
        TestBoot _boot;
        double _t;
        GuideModule _guide;
        LessonsModule _m;
        int _playbackStarts, _playbackEnds, _playbackNotes;
        Action<RecordingPlaybackEvent> _onPlayback;
        Action<PlaybackNoteEvent> _onNote;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null && App.Current != null)
            {
                if (_onPlayback != null) App.Current.Events.Unsubscribe(_onPlayback);
                if (_onNote != null) App.Current.Events.Unsubscribe(_onNote);
            }
            _onPlayback = null;
            _onNote = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        IEnumerator Boot()
        {
            _t = 50.0;
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(GuideModule.KeyTipsSeen, "tip_loop_start");
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Current.Get<ILayoutService>();
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            Assert.IsTrue(layout.FirstArrangeDone);
            _guide = App.Current.Get<IGuide>() as GuideModule;
            foreach (IAppModule x in App.Current.Modules) if (x is LessonsModule l) _m = l;
            _m.Looper.InputLatency = 0;
            Assert.That(_guide, Is.Not.Null);
            _playbackStarts = _playbackEnds = _playbackNotes = 0;
            _trace.Clear();
            _traceStep = DeckGuideStep.Off;
            _spokeInHush = null;
            _onPlayback = e => { if (e.Started) _playbackStarts++; else _playbackEnds++; };
            _onNote = e => { if (_playbackStarts > _playbackEnds && e.Level > 0f) _playbackNotes++; };
            App.Current.Events.Subscribe(_onPlayback);
            App.Current.Events.Subscribe(_onNote);
        }

        DeckGuide G => _guide.DeckGuide;
        ILooper Looper => App.Current.Get<ILooper>();
        IAmbienceSwitches Switches => App.Current.Get<IAmbienceSwitches>();
        bool Hush => Looper.Status == LoopStatus.Waiting || Looper.Status == LoopStatus.RecordingFirst || Looper.Status == LoopStatus.Overdubbing;

        int _lineVersion;
        string _spokeInHush;
        DeckGuideStep _traceStep;
        readonly System.Text.StringBuilder _trace = new System.Text.StringBuilder();

        // One frame; a line Sunny starts during a count-in or a recording pass is remembered (the rule: never).
        void Step(int n = 1, float dt = Dt)
        {
            for (int i = 0; i < n; i++)
            {
                _t += dt;
                _boot.Step(dt);
                if (_guide == null) continue;
                if (G.Step != _traceStep) { _traceStep = G.Step; _trace.Append($" {G.Step}@{_t - 50.0:F1}"); }
                int v = _guide.Sunny.LineVersion;
                if (v != _lineVersion)
                {
                    _lineVersion = v;
                    if (G.Running && Hush && _spokeInHush == null) _spokeInHush = $"{_guide.Sunny.LineId} during {Looper.Status}";
                }
            }
        }

        void Wait(float seconds) => Step(Mathf.Max(1, Mathf.RoundToInt(seconds / Dt)));
        void Poke(DeckButton b) => LooperPlayTests.Poke(_boot, _m.Deck, b, () => Step());

        void StepUntil(Func<bool> done, float seconds, string what)
        {
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n; i++) { if (done()) return; Step(); }
            Assert.IsTrue(done(), $"{what}: not within {seconds} s (step {G.Step}, idle {G.Idle:F1}, loop {Looper.Status}, line {_guide.Sunny.LineId})");
        }

        void Note(int midi)
        {
            object k = new object();
            App.Current.Get<IPianoApi>().PressMidi(midi, k, 0.6f);
            Step(3);
            App.Current.Get<IPianoApi>().ReleaseMidi(midi, k);
            Step(3);
        }

        void Drum(string kind) { App.Current.Get<IDrumsApi>().PlaybackHit(kind, 0.7f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" }); Step(3); }
        void Pluck(int s) { App.Current.Get<IHarpApi>().PlaybackPluck(s, 0.6f); Step(3); }

        // The prompt player: does what Sunny asks about React s after the ghost hand shows it. Returns the seconds it took.
        double PlayPromptly(bool expectSnow)
        {
            double start = _t;
            _guide.StartLoopShow();
            Assert.IsTrue(G.Running, "Loop a beat starts it");
            Assert.IsTrue(G.Quick, "Loop a beat starts the loop show");
            Assert.IsTrue(_guide.DeckGuideRunning);
            Step(2);
            Assert.That(_m.Deck.CapGlow(DeckButton.Guide), Is.EqualTo(TapeDeck.Selected), "GUIDE's light is on while it runs");
            Assert.That(Looper.Bars, Is.EqualTo(DeckGuide.QuickBars), "a short loop for the show");
            Assert.That(Looper.Bpm, Is.EqualTo(DeckGuide.QuickBpm));

            if (expectSnow)
            {
                Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Snow));
                StepUntil(() => G.PointingSnow, 6f, "the snow line");
                Step(2);
                Assert.IsTrue(App.Current.Get<ITourAnchors>().TryGetAnchor(DeckGuide.SnowAnchor, out Vector3 sw, out _));
                Assert.That(_guide.Sunny.DemoKind, Is.EqualTo(DemoKind.Press));
                Assert.That(Vector3.Distance(_guide.Sunny.PressAnchor, sw), Is.LessThan(1e-3f), "the ghost hand presses the SNOW switch");
                Wait(React);
                Switches.Set(AmbienceKind.Snow, true);       // the lever flipped
            }
            StepUntil(() => G.Step == DeckGuideStep.Beat && G.Pointing == LoopControl.Loop, 8f, "LOOP for the beat");
            Assert.IsTrue(Switches.Get(AmbienceKind.Snow), "it snows for the show");
            Wait(React);
            Poke(DeckButton.Loop);
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Waiting));
            Step(3);
            Assert.IsTrue(G.ShowingInstrument, "while the clicks wait the ghost hand shows where to play");
            Assert.That(_guide.Sunny.DemoKind, Is.EqualTo(DemoKind.Strike), "a drum hit on the snare");
            Wait(React);
            for (int i = 0; i < 6; i++) { Drum(i % 2 == 0 ? "kick" : "snare"); Wait(0.5f); }
            StepUntil(() => G.Step == DeckGuideStep.Layer, 10f, "a beat that repeats");
            Assert.That(Looper.Layers, Is.EqualTo(1));

            StepUntil(() => G.Pointing == LoopControl.Loop, 8f, "LOOP for the second layer");
            Wait(React);
            Poke(DeckButton.Loop);
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Armed));
            StepUntil(() => Looper.Status == LoopStatus.Overdubbing, 8f, "the second layer's pass");
            for (int i = 0; i < 4; i++)
            {
                if (G.LayerKind == InstrumentKind.Harp) Pluck(7 + i); else Note(60 + 2 * i);
                Wait(0.6f);
            }
            StepUntil(() => G.Step == DeckGuideStep.Save, 10f, "the second layer on top");
            Assert.That(Looper.Layers, Is.EqualTo(2));

            StepUntil(() => G.Pointing == LoopControl.Stop, 8f, "STOP");
            Wait(React);
            Poke(DeckButton.Stop);
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Off), "stopped: saved");
            StepUntil(() => G.Step == DeckGuideStep.Watch && G.Pointing == LoopControl.Play, 8f, "PLAY");
            Wait(React);
            int starts = _playbackStarts;
            Poke(DeckButton.Play);
            Assert.That(_playbackStarts, Is.EqualTo(starts + 1), "PLAY of the saved loop is a recording playing back (the show runs on it)");
            Assert.IsTrue(G.ShowStarted);
            StepUntil(() => !G.Running, DeckGuide.ShowSeconds + 6f, "the show and goodbye");
            return _t - start;
        }

        [UnityTest]
        public IEnumerator APromptPlayerIsThroughTheQuickShowWithinAMinute()
        {
            yield return Boot();
            if (Switches.Get(AmbienceKind.Snow)) Switches.Set(AmbienceKind.Snow, false);
            Step(5);
            double took = PlayPromptly(expectSnow: true);
            Debug.Log($"[Guide] quick show with a prompt player: {took:F1} s, playback notes {_playbackNotes}, steps:{_trace}");
            Assert.That(took, Is.LessThanOrEqualTo(60.0), "the whole show within a minute");
            Assert.That(G.Skips + G.GiveUps + G.Assists, Is.EqualTo(0), "the player did every step");
            Assert.That(_spokeInHush, Is.Null, "Sunny never speaks during a count-in or a recording pass");
            Assert.IsTrue(G.WowSaid, "a line of wonder over the show");
            Assert.That(_playbackNotes, Is.GreaterThan(4), "the loop's notes came back as PlaybackNoteEvent");
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Leave).Or.EqualTo(SunnyState.Gone));
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Looping), "the song plays on for the player");
            Assert.That(App.Current.Get<IRecorder>().State, Is.EqualTo(RecorderState.Playing));
            Step(3);
            Assert.That(_m.Deck.CapGlow(DeckButton.Guide).r, Is.EqualTo(0), "GUIDE's light goes out");
            // STOP ends it; then the player's own loop tempo and length come back.
            Poke(DeckButton.Stop);
            Step(5);
            Assert.That(_playbackEnds, Is.EqualTo(_playbackStarts));
            Assert.That(Looper.Active, Is.False);
        }

        [UnityTest]
        public IEnumerator WithNobodyPlayingSunnysAssistsStillEndItSoonAndTidy()
        {
            yield return Boot();
            if (Switches.Get(AmbienceKind.Snow)) Switches.Set(AmbienceKind.Snow, false);
            Step(5);
            double start = _t;
            _guide.StartLoopShow();
            Assert.IsTrue(G.Running && G.Quick);
            const float big = 1f / 20f;
            int frames = 0;
            while (G.Running && frames++ < (int)(200f / big)) Step(1, big);
            double took = _t - start;
            Debug.Log($"[Guide] quick show with nobody playing: {took:F1} s, give-ups {G.GiveUps}, assists {G.Assists}, steps:{_trace}");
            Assert.IsFalse(G.Running, "it ends by itself");
            Assert.That(took, Is.LessThan(75.0), "bounded: a little over a minute at most");
            Assert.That(G.Assists, Is.GreaterThanOrEqualTo(2), "Sunny turned the snow on and pressed LOOP");
            Assert.IsTrue(Switches.Get(AmbienceKind.Snow), "the snow came on anyway");
            Assert.That(_spokeInHush, Is.Null, "Sunny never speaks during a count-in or a recording pass");
            Assert.That(App.Current.Get<IRecorder>().State, Is.EqualTo(RecorderState.Idle), "no take left recording or playing");
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Off), "no loop left waiting or armed");
        }

        [UnityTest]
        public IEnumerator InTheSingleLayoutTheInstrumentsComeInAndTheLayoutComesBackAfterTheShow()
        {
            yield return Boot();
            ILayoutService layout = App.Current.Get<ILayoutService>();
            layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            if (!Switches.Get(AmbienceKind.Snow)) Switches.Set(AmbienceKind.Snow, true);
            Step(30);
            _guide.StartLoopShow();
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Beat), "it snows already: straight to the beat");
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Drums), "the drums come in");
            StepUntil(() => G.Pointing == LoopControl.Loop, 8f, "LOOP");
            Wait(React);
            Poke(DeckButton.Loop);
            Wait(React);
            for (int i = 0; i < 6; i++) { Drum(i % 2 == 0 ? "kick" : "snare"); Wait(0.5f); }
            StepUntil(() => G.Step == DeckGuideStep.Layer, 10f, "a beat that repeats");
            Assert.That(G.LayerKind, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Piano), "then the piano");
            StepUntil(() => G.Pointing == LoopControl.Loop, 8f, "LOOP again");
            Wait(React);
            Poke(DeckButton.Loop);
            StepUntil(() => Looper.Status == LoopStatus.Overdubbing, 8f, "the piano's pass");
            for (int i = 0; i < 4; i++) { Note(60 + 2 * i); Wait(0.6f); }
            StepUntil(() => G.Pointing == LoopControl.Stop, 12f, "STOP");
            Poke(DeckButton.Stop);
            StepUntil(() => G.Pointing == LoopControl.Play, 8f, "PLAY");
            Poke(DeckButton.Play);
            StepUntil(() => !G.Running, DeckGuide.ShowSeconds + 6f, "the show and goodbye");
            Assert.IsTrue(G.RestorePending, "the playback still has its instruments in front: the layout waits");
            Assert.IsFalse(layout.Mode == LayoutMode.Single && layout.Current == InstrumentKind.Harp, "not yet back while the song plays");
            Poke(DeckButton.Stop);                          // the player ends the song
            StepUntil(() => !G.RestorePending, 6f, "the layout comes back after the playback");
            Step(10);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Harp), "the player's own layout is back");
        }

        [UnityTest]
        public IEnumerator ThePlayersOwnLoopTempoAndLengthComeBackAfterTheShow()
        {
            yield return Boot();
            _m.Looper.Configure(110, 4, false);
            Step(2);
            _guide.StartLoopShow();
            Assert.That(Looper.Bpm, Is.EqualTo(DeckGuide.QuickBpm));
            Assert.That(Looper.Bars, Is.EqualTo(DeckGuide.QuickBars));
            Poke(DeckButton.Guide);                         // GUIDE again: it ends
            Assert.IsFalse(G.Running);
            Step(3);
            Assert.That(Looper.Bpm, Is.EqualTo(110));
            Assert.That(Looper.Bars, Is.EqualTo(4));
        }
    }
}
