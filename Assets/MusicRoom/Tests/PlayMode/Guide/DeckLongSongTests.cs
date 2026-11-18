// The deck's GUIDE button starts Sunny's long song (Guide/DeckGuide.cs, DeckRun.LongSong; 2026-11-18: "update tape deck guide
// sunny tour to play long songs"): REC and the drums -> STOP -> PLAY -> REC on top and the piano -> STOP -> PLAY -> REC on top
// and the harp -> STOP -> PLAY: one take with all three plays back once with the show. In the booted app with the scripted
// audio clock, manual frames and real fingertip presses on the deck (LooperPlayTests.Poke: it lifts 7 cm, past the deck's
// re-press guard):
//   - a prompt player is through it within two minutes with no skip, give-up or assist; Sunny asks for STOP only once a
//     part has notes and time; the last take holds drums, piano and harp, and its playback plays all three;
//   - from One alone, the song is made in My set (all three in front) and the player's layout comes back afterwards;
//   - a player who does nothing: Sunny's assists and give-ups end it within a bounded time, the deck left idle;
//   - poking Sunny does each step (REC, STOP, PLAY) and the run ends after a bounded number of pokes; GUIDE again ends it
//     mid-part with the part saved.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using MusicRoom.Tests.PlayMode.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class DeckLongSongTests
    {
        const float Dt = 1f / 30f;
        const float React = 0.6f;
        TestBoot _boot;
        double _t;
        GuideModule _guide;
        LessonsModule _m;
        int _playbackStarts, _playbackEnds;
        readonly HashSet<InstrumentKind> _playedBack = new HashSet<InstrumentKind>();
        Action<RecordingPlaybackEvent> _onPlayback;
        Action<PlaybackNoteEvent> _onNote;
        DeckGuideStep _traceStep;
        readonly System.Text.StringBuilder _trace = new System.Text.StringBuilder();

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

        IEnumerator Boot(bool single = false)
        {
            _t = 50.0;
            _boot = new TestBoot(headless: true, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(GuideModule.KeyTipsSeen, "tip_loop_start");
            if (single)
            {
                _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
                _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Drums));
            }
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Current.Get<ILayoutService>();
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            Assert.IsTrue(layout.FirstArrangeDone);
            _guide = App.Current.Get<IGuide>() as GuideModule;
            foreach (IAppModule x in App.Current.Modules) if (x is LessonsModule l) _m = l;
            _m.Looper.InputLatency = 0;
            Assert.That(_guide, Is.Not.Null);
            _playbackStarts = _playbackEnds = 0;
            _playedBack.Clear();
            _trace.Clear();
            _traceStep = DeckGuideStep.Off;
            _onPlayback = e => { if (e.Started) _playbackStarts++; else _playbackEnds++; };
            _onNote = e => { if (e.Role == EventRole.Onset && e.Level > 0f) _playedBack.Add(e.Kind); };
            App.Current.Events.Subscribe(_onPlayback);
            App.Current.Events.Subscribe(_onNote);
        }

        DeckGuide G => _guide.DeckGuide;
        IRecorder Rec => App.Current.Get<IRecorder>();
        ILooper Looper => App.Current.Get<ILooper>();

        void Step(int n = 1, float dt = Dt)
        {
            for (int i = 0; i < n; i++)
            {
                _t += dt;
                _boot.Step(dt);
                if (_guide == null) continue;
                if (G.Step != _traceStep) { _traceStep = G.Step; _trace.Append($" {G.Step}{(G.LongSong ? G.Part.ToString() : "")}@{_t - 50.0:F1}"); }
            }
        }

        void Wait(float seconds) => Step(Mathf.Max(1, Mathf.RoundToInt(seconds / Dt)));
        void Poke(DeckButton b) => LooperPlayTests.Poke(_boot, _m.Deck, b, () => Step());

        void StepUntil(Func<bool> done, float seconds, string what)
        {
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n; i++) { if (done()) return; Step(); }
            Assert.IsTrue(done(), $"{what}: not within {seconds} s (step {G.Step} part {G.Part}, idle {G.Idle:F1}, rec {Rec.State}, line {_guide.Sunny.LineId}; steps:{_trace})");
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

        void PlayPart(int part, int notes)
        {
            for (int i = 0; i < notes; i++)
            {
                if (part == 0) Drum(i % 2 == 0 ? "kick" : "snare");
                else if (part == 1) Note(60 + 2 * (i % 5));
                else Pluck(7 + i % 5);
                Wait(0.8f);
            }
        }

        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        // A fingertip into Sunny (from 20 cm away), then away again (as DeckGuideTests does).
        void PokeSunny()
        {
            Vector3 c = _guide.Sunny.Position;
            Vector3 far = c + Vector3.up * 0.2f + Vector3.left * 0.1f;
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step(); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(_guide.Sunny.Position)); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(new Vector3(0f, 0.3f, 0.6f))); Step();
            Step(18);
        }

        static string Kinds(List<TakeEvent> take)
        {
            bool d = false, p = false, h = false;
            if (take != null)
                foreach (TakeEvent e in take)
                {
                    if (e.type == TakeEventType.Drum) d = true;
                    else if (e.type == TakeEventType.PianoOn) p = true;
                    else if (e.type == TakeEventType.Harp) h = true;
                }
            return (d ? "drums " : "") + (p ? "piano " : "") + (h ? "harp" : "");
        }

        // The prompt player: does what Sunny shows about React s later; plays each part ~9 s.
        double PlayPromptly()
        {
            double start = _t;
            Poke(DeckButton.Guide);
            Assert.IsTrue(G.Running, "GUIDE starts it");
            Assert.IsTrue(G.LongSong, "GUIDE starts the long song");
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.LongIntro));
            Step(2);
            Assert.That(_m.Deck.CapGlow(DeckButton.Guide), Is.EqualTo(TapeDeck.Selected), "GUIDE's light is on while it runs");
            int takes = 0;
            for (int part = 0; part < 3; part++)
            {
                if (part > 0)
                {
                    StepUntil(() => G.Step == DeckGuideStep.LongPlay && G.Pointing == LoopControl.Play, 10f, $"PLAY before part {part + 1}");
                    Wait(React);
                    int starts = _playbackStarts;
                    Poke(DeckButton.Play);
                    Assert.That(_playbackStarts, Is.EqualTo(starts + 1), "PLAY plays the take so far");
                }
                StepUntil(() => G.Step == DeckGuideStep.LongRec && G.Part == part && G.Pointing == LoopControl.Rec, 10f, $"REC for part {part + 1}");
                Assert.That(_guide.Sunny.DemoKind, Is.EqualTo(DemoKind.Press), "the ghost hand presses REC");
                Assert.IsTrue(App.Current.Get<ILayoutService>().IsInScene(DeckGuide.LongKinds[part]), $"part {part + 1}'s instrument is in front");
                Wait(React);
                Poke(DeckButton.Rec);
                if (part > 0) Assert.IsTrue(Rec.RecordingOnTop, $"part {part + 1} records on top of the take playing");
                else Assert.That(Rec.State, Is.EqualTo(RecorderState.Recording));
                StepUntil(() => G.Step == DeckGuideStep.LongPart, 2f, "the part");
                Wait(React);
                PlayPart(part, 6);
                Assert.IsFalse(G.StopAsked && G.StepTime < DeckGuide.PartSeconds, "STOP is asked only after PartSeconds");
                StepUntil(() => G.StopAsked && G.Pointing == LoopControl.Stop, 6f, $"\"Press STOP when you're done.\" in part {part + 1}");
                Assert.That(_guide.Sunny.LineId, Is.EqualTo(DeckGuide.LLongStop));
                Wait(React);
                Poke(DeckButton.Stop);
                Assert.That(Rec.State, Is.Not.EqualTo(RecorderState.Recording));
                Assert.IsFalse(Rec.RecordingOnTop);
                takes++;
                StepUntil(() => G.PartsSaved == takes, 2f, $"part {part + 1} saved");
            }
            Assert.That(Kinds(Rec.Take), Is.EqualTo("drums piano harp"), "one take holds all three parts");
            StepUntil(() => G.Step == DeckGuideStep.Watch && G.Pointing == LoopControl.Play, 10f, "PLAY for the finale");
            Wait(React);
            int s0 = _playbackStarts;
            _playedBack.Clear();
            Poke(DeckButton.Play);
            Assert.That(_playbackStarts, Is.EqualTo(s0 + 1), "PLAY of the whole song is a recording playing back (the show runs on it)");
            Assert.IsTrue(G.ShowStarted);
            StepUntil(() => !G.Running, DeckGuide.ShowSeconds + 8f, "the show and goodbye");
            return _t - start;
        }

        [UnityTest]
        public IEnumerator APromptPlayerMakesALongSongInThreePartsAndHearsThemAllTogether()
        {
            yield return Boot();
            Step(5);
            double took = PlayPromptly();
            Debug.Log($"[Guide] long song with a prompt player: {took:F1} s, take {Rec.Take.Count} events ({Kinds(Rec.Take)}), played back {string.Join(",", _playedBack)}, steps:{_trace}");
            Assert.That(took, Is.LessThanOrEqualTo(120.0), "about a minute and a half");
            Assert.That(G.Skips + G.GiveUps + G.Assists, Is.EqualTo(0), "the player did every step");
            Assert.That(G.PartsSaved, Is.EqualTo(3));
            Assert.IsTrue(G.WowSaid, "a line of wonder over the show");
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Leave).Or.EqualTo(SunnyState.Gone));
            // The song plays on for the player: every part sounds back (the playback show runs on these).
            Wait(6f);
            Assert.IsTrue(_playedBack.Contains(InstrumentKind.Drums) && _playedBack.Contains(InstrumentKind.Piano) && _playedBack.Contains(InstrumentKind.Harp),
                $"all three play back together (heard: {string.Join(",", _playedBack)})");
            Assert.IsTrue(App.Current.Get<IGuide>().TipSeen(DeckGuide.RecordOnTopTip), "record on top is taught: its tip is marked heard");
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Off), "no loop was involved");
            StepUntil(() => Rec.State == RecorderState.Idle, 30f, "the song ends once");
            Assert.That(_playbackEnds, Is.EqualTo(_playbackStarts), "every playback ended (once, no repeat)");
        }

        [UnityTest]
        public IEnumerator FromOneAloneTheSongIsMadeInMySetAndTheLayoutComesBack()
        {
            yield return Boot(single: true);
            ILayoutService layout = App.Current.Get<ILayoutService>();
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Step(10);
            double took = PlayPromptly();
            Debug.Log($"[Guide] long song from One alone: {took:F1} s, steps:{_trace}");
            Assert.That(G.PartsSaved, Is.EqualTo(3));
            Assert.That(Kinds(Rec.Take), Is.EqualTo("drums piano harp"));
            Poke(DeckButton.Stop);                          // the player ends the song
            StepUntil(() => !G.RestorePending, 8f, "the layout comes back after the playback");
            for (int i = 0; i < 300 && layout.AnyMoving; i++) Step();
            Step(10);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single), "the player's own layout is back");
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Drums));
        }

        [UnityTest]
        public IEnumerator WithNobodyPlayingItNeverStallsAndEndsTidy()
        {
            yield return Boot();
            Step(5);
            double start = _t;
            Poke(DeckButton.Guide);
            Assert.IsTrue(G.Running && G.LongSong);
            const float big = 1f / 20f;
            int frames = 0;
            float quiet = 0f, worst = 0f;
            int line = _guide.Sunny.LineVersion;
            float demoT = _guide.Sunny.DemoT;
            DeckGuideStep step = G.Step;
            while (G.Running && frames++ < (int)(300f / big))
            {
                Step(1, big);
                bool nudge = _guide.Sunny.LineVersion != line || _guide.Sunny.LineBusy() || _guide.Sunny.DemoT < demoT - 1e-4f || G.Step != step
                    || (G.Step == DeckGuideStep.Watch && Rec.State == RecorderState.Playing);
                line = _guide.Sunny.LineVersion; demoT = _guide.Sunny.DemoT; step = G.Step;
                quiet = nudge ? 0f : quiet + big;
                worst = Mathf.Max(worst, quiet);
                Assert.That(quiet, Is.LessThanOrEqualTo(DeckGuide.NudgeLimit(DeckRun.LongSong) + 0.25f), $"Sunny went quiet in {G.Step} (steps:{_trace})");
            }
            double took = _t - start;
            Debug.Log($"[Guide] long song with nobody playing: {took:F1} s, longest quiet {worst:F1} s, give-ups {G.GiveUps}, assists {G.Assists}, steps:{_trace}");
            Assert.IsFalse(G.Running, "it ends by itself");
            Assert.That(took, Is.LessThan(150.0), "bounded");
            Assert.That(G.Assists, Is.GreaterThanOrEqualTo(3), "Sunny pressed REC and STOP for the player");
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Idle), "no take left recording or playing");
            Assert.IsFalse(Rec.RecordingOnTop);
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Off));
        }

        [UnityTest]
        public IEnumerator PokingSunnyDoesEachStepAndGuideAgainEndsIt()
        {
            yield return Boot();
            Step(5);
            Poke(DeckButton.Guide);
            StepUntil(() => G.Step == DeckGuideStep.LongRec, 8f, "REC");
            PokeSunny();                                    // REC pressed for the player
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.LongPart), "a poke on REC: Sunny presses it");
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Recording));
            PlayPart(0, 4);
            PokeSunny();                                    // STOP pressed for the player: the part saved
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(G.PartsSaved, Is.EqualTo(1));
            StepUntil(() => G.Step == DeckGuideStep.LongPlay, 6f, "PLAY");
            PokeSunny();                                    // PLAY pressed for the player
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.LongRec));
            Assert.That(G.Part, Is.EqualTo(1));
            Assert.IsTrue(Rec.State == RecorderState.Playing, "the take plays");
            PokeSunny();                                    // REC on top for the player
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.LongPart));
            Assert.IsTrue(Rec.RecordingOnTop, "Sunny's REC records on top");
            PlayPart(1, 3);
            Poke(DeckButton.Guide);                         // GUIDE again: it ends, the part saved
            Assert.IsFalse(G.Running);
            Assert.IsFalse(Rec.RecordingOnTop);
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(Kinds(Rec.Take), Is.EqualTo("drums piano "), "the piano part went on top of the drums");
            Assert.That(G.Skips, Is.EqualTo(4));

            // Again, poking through every step: it ends after a bounded number of pokes, the deck idle.
            Step(60);
            Poke(DeckButton.Guide);
            Assert.IsTrue(G.Running && G.LongSong);
            int pokes = 0;
            while (G.Running && pokes < 20) { PokeSunny(); pokes++; Step(30); }
            Debug.Log($"[Guide] long song poked through in {pokes} pokes, steps:{_trace}");
            if (G.Running) StepUntil(() => !G.Running, DeckGuide.ShowSeconds + 8f, "goodbye");
            Assert.That(pokes, Is.LessThan(20), "every poke moves it on");
            Step(5);
            Assert.That(Looper.Status, Is.EqualTo(LoopStatus.Off));
        }
    }
}
