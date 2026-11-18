// The deck guide's watchdog (2026-11-16 headset test: "in tape deck sunny guide. sunny just pauses and forget to guide on
// buttons. then i have to poke him. but newcomer will not know this." The long walkthrough needed two pokes: a player who
// played the harp while Sunny waited for LOOP kept resetting its clock, so it never showed LOOP again nor moved on).
// Every run (the loop show, the walkthrough and, since 2026-11-18, the long song GUIDE starts) is driven by adversarial players: one who does nothing, one who plays
// notes but never presses what Sunny shows, one who presses the wrong controls, one who cancels every loop, one who ends
// every playback at once, one who puts the instrument away while the clicks wait, and a slow one. For every one, measured
// from outside the guide (Sunny's caption, the ghost hand's demo restarting or moving), whenever the run waits for the
// player Sunny speaks or points again within the step's re-demo time (+ a frame or two), and the run always finishes.
using System;
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class DeckGuideWatchdogTests
    {
        const float Dt = 1f / 20f;
        TestBoot _boot;
        double _t;
        GuideModule _guide;
        LessonsModule _m;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
        }

        void Step(int n = 1, float dt = Dt) { for (int i = 0; i < n; i++) { _t += dt; _boot.Step(dt); } }
        DeckGuide G => _guide.DeckGuide;
        ILooper Looper => App.Current.Get<ILooper>();
        IRecorder Rec => App.Current.Get<IRecorder>();
        ILayoutService Layout => App.Current.Get<ILayoutService>();

        public enum Player { Nobody, NotesNoButtons, WrongButtons, CancelsLoops, EndsPlaybacks, HidesInstrument, Slow }

        // One player's move this frame (t: seconds since the run started; acted: the last time it acted).
        double _acted;
        int _wrong;

        void Act(Player p, double t)
        {
            LoopStatus st = Looper.Status;
            switch (p)
            {
                case Player.NotesNoButtons:
                    // Plays what the step is about, never a deck control (the headset log: the harp while Sunny waits for LOOP).
                    if (t - _acted < 0.5) return;
                    _acted = t;
                    _noteAt = t;
                    switch ((int)(t * 2) % 3)
                    {
                        case 0: App.Current.Get<IDrumsApi>().PlaybackHit("snare", 0.7f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" }); break;
                        case 1: App.Current.Get<IHarpApi>().PlaybackPluck(7 + (int)(t % 4), 0.6f); break;
                        default:
                        {
                            var k = new object();
                            App.Current.Get<IPianoApi>().PressMidi(60, k, 0.6f);
                            App.Current.Get<IPianoApi>().ReleaseMidi(60, k);
                            break;
                        }
                    }
                    return;
                case Player.WrongButtons:
                {
                    if (t - _acted < 1.5) return;
                    _acted = t;
                    DeckButton[] wrong = { DeckButton.Undo, DeckButton.Slow, DeckButton.Click, DeckButton.Tidy, DeckButton.Bars4, DeckButton.Layer3, DeckButton.Medium, DeckButton.Bars2 };
                    _m.Deck.Press(wrong[_wrong++ % wrong.Length]);
                    return;
                }
                case Player.CancelsLoops:
                    // LOOP whenever Sunny shows it; then, once the clicks wait (or a layer is armed), STOP / LOOP again: cancelled.
                    if (t - _acted < 1.0) return;
                    if (G.Pointing == LoopControl.Loop && (st == LoopStatus.Off || st == LoopStatus.Looping)) { _acted = t; _m.Deck.Press(DeckButton.Loop); }
                    else if (st == LoopStatus.Waiting) { _acted = t; _m.Deck.Press(DeckButton.Stop); }
                    else if (st == LoopStatus.Armed) { _acted = t; _m.Deck.Press(DeckButton.Loop); }
                    return;
                case Player.EndsPlaybacks:
                    // PLAY whenever Sunny shows it, and STOP a moment after any playback starts.
                    if (t - _acted < 0.6) return;
                    if (Rec.State == RecorderState.Playing && (st == LoopStatus.Off || st == LoopStatus.Looping)
                        && (G.Step == DeckGuideStep.Watch || G.Step == DeckGuideStep.Play || G.Step == DeckGuideStep.LongRec))
                    { _acted = t; _m.Deck.Press(DeckButton.Stop); }
                    else if (G.Pointing == LoopControl.Play) { _acted = t; _m.Deck.Press(DeckButton.Play); }
                    return;
                case Player.HidesInstrument:
                    // While the clicks wait (or a layer is armed), the instrument goes: another one alone.
                    if (t - _acted < 2.0) return;
                    if (st == LoopStatus.Waiting || st == LoopStatus.Armed || (G.Pointing == LoopControl.Loop && st == LoopStatus.Off)
                        || (G.Step == DeckGuideStep.LongPart && G.ShowingInstrument))
                    {
                        _acted = t;
                        bool drums = G.Step == DeckGuideStep.Drums || G.Step == DeckGuideStep.Beat || (G.LongSong && G.Part == 0);
                        InstrumentKind away = drums ? InstrumentKind.Harp : InstrumentKind.Drums;
                        Layout.Set(LayoutMode.Single, away);
                        if (G.Pointing == LoopControl.Loop) _m.Deck.Press(DeckButton.Loop);
                    }
                    return;
                case Player.Slow:
                    // Does what Sunny shows, but only after 6 s, and plays a few notes when the clicks wait.
                    if (t - _acted < 6.0) return;
                    _acted = t;
                    if (G.PointingSnow) App.Current.Get<IAmbienceSwitches>().Set(AmbienceKind.Snow, true);
                    else if (G.Pointing.HasValue) _m.Deck.Press(TapeDeck.ButtonOf(G.Pointing.Value, 0));
                    else if (st == LoopStatus.Waiting || st == LoopStatus.Overdubbing || st == LoopStatus.RecordingFirst || G.Step == DeckGuideStep.LongPart)
                    {
                        _noteAt = t;
                        App.Current.Get<IDrumsApi>().PlaybackHit("kick", 0.7f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" });
                    }
                    return;
            }
        }

        // The player's last note (a long song's part is busy while the player plays it).
        double _noteAt = -100;

        // Runs one guide with one player; returns (seconds, longest stretch with nothing new from Sunny while it waited).
        void Run(DeckRun run, Player p)
        {
            bool quick = run == DeckRun.Loop;
            if (quick)
            {
                IAmbienceSwitches sw = App.Current.Get<IAmbienceSwitches>();
                if (sw.Get(AmbienceKind.Snow)) sw.Set(AmbienceKind.Snow, false);
                Step(3);
                _guide.StartLoopShow();
            }
            else if (run == DeckRun.LongSong) _guide.ToggleDeckGuide();   // the deck's GUIDE
            else _guide.StartDeckWalkthrough();
            Assert.IsTrue(G.Running);
            Assert.That(G.Run, Is.EqualTo(run));
            Sunny s = _guide.Sunny;
            float limit = DeckGuide.NudgeLimit(run) + 0.25f;
            double bound = quick ? 220 : run == DeckRun.LongSong ? 420 : 650, start = _t, stall = 0, worst = 0;
            _noteAt = -100;
            string worstAt = "";
            int line = s.LineVersion;
            float demoT = s.DemoT;
            DemoKind kind = s.DemoKind;
            Vector3 anchor = s.PressAnchor;
            DeckGuideStep step = G.Step;
            _acted = -100;
            _wrong = 0;
            var trace = new System.Text.StringBuilder();
            while (G.Running && _t - start < bound)
            {
                Act(p, _t - start);
                Step();
                if (!G.Running) break;
                LoopStatus st = Looper.Status;
                // Sunny speaks, the ghost hand starts again or moves, the step changes: something new from Sunny.
                bool nudge = s.LineVersion != line || s.LineBusy() || s.DemoT < demoT - 1e-4f
                    || (s.DemoKind != kind && s.DemoKind != DemoKind.None)
                    || (s.DemoKind == DemoKind.Press && Vector3.Distance(s.PressAnchor, anchor) > 0.01f) || G.Step != step;
                // The deck is busy for the player (a recording pass, an armed layer counting down, the show or the take
                // playing back for them to watch): the run is not waiting on them.
                bool busy = st == LoopStatus.RecordingFirst || st == LoopStatus.Overdubbing || st == LoopStatus.Armed
                    || (G.Step == DeckGuideStep.Watch && G.ShowStarted && Rec.State == RecorderState.Playing)
                    || (G.Step == DeckGuideStep.Play && Rec.State == RecorderState.Playing)
                    || (G.Step == DeckGuideStep.LongPart && _t - start - _noteAt < 1.0);   // the player is playing their part
                if (G.Step != step) trace.Append($" {G.Step}@{_t - start:F0}");
                line = s.LineVersion; demoT = s.DemoT; kind = s.DemoKind; anchor = s.PressAnchor; step = G.Step;
                if (nudge || busy) { stall = 0; continue; }
                stall += Dt;
                if (stall > worst) { worst = stall; worstAt = $"{G.Step} loop {st} rec {Rec.State} demo {s.DemoKind} line '{s.LineId}' idle {G.Idle:F1}"; }
                Assert.That(stall, Is.LessThanOrEqualTo(limit),
                    $"{run}, {p}: Sunny said and showed nothing new for {stall:F1} s in {worstAt} (steps:{trace})");
            }
            double took = _t - start;
            Debug.Log($"[Guide] watchdog {run} with {p}: {took:F0} s, longest quiet wait {worst:F1} s ({worstAt}), " +
                      $"skips {G.Skips}, give-ups {G.GiveUps}, assists {G.Assists}, steps:{trace}");
            Assert.IsFalse(G.Running, $"{p}: the run finishes by itself (still in {G.Step} after {took:F0} s; steps:{trace})");
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Leave).Or.EqualTo(SunnyState.Gone));
            Assert.That(Looper.Status, Is.Not.EqualTo(LoopStatus.Waiting).And.Not.EqualTo(LoopStatus.Armed), "no loop left waiting or armed");
        }

        IEnumerator RunBoth(Player p)
        {
            yield return Boot();
            Run(DeckRun.Loop, p);
            // Between the runs: the deck quiet again (a loop the show left playing stops; the layout back).
            if (Looper.Active || Rec.State != RecorderState.Idle) Rec.Stop();
            Step(80);
            Run(DeckRun.Walkthrough, p);
            if (Looper.Active || Rec.State != RecorderState.Idle) Rec.Stop();
            Step(80);
            Run(DeckRun.LongSong, p);
        }

        [UnityTest] public IEnumerator NobodyPlaying() => RunBoth(Player.Nobody);
        [UnityTest] public IEnumerator APlayerWhoPlaysNotesButNeverPressesWhatSunnyShows() => RunBoth(Player.NotesNoButtons);
        [UnityTest] public IEnumerator APlayerWhoPressesTheWrongControls() => RunBoth(Player.WrongButtons);
        [UnityTest] public IEnumerator APlayerWhoCancelsEveryLoop() => RunBoth(Player.CancelsLoops);
        [UnityTest] public IEnumerator APlayerWhoEndsEveryPlaybackAtOnce() => RunBoth(Player.EndsPlaybacks);
        [UnityTest] public IEnumerator APlayerWhoPutsTheInstrumentAway() => RunBoth(Player.HidesInstrument);
        [UnityTest] public IEnumerator ASlowPlayer() => RunBoth(Player.Slow);
    }
}
