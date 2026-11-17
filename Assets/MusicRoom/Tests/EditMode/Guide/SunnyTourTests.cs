// The tour and the speech on a scripted clock (ARCHITECTURE.md 15.8: "praise >= .3 s after the line; swipe_skip at 12 s;
// Hide leaves in .8 s"), with the tour's behaviour checks case by case
// (names in the comments are the check keys).
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public class SunnyTourTests
    {
        SunnyRig _r;
        Sunny S => _r.Sunny;

        [SetUp]
        public void Make() => _r = new SunnyRig();

        static float Secs(string id) => GuideLines.ManifestSeconds[GuideLines.IndexOf(id)];
        static float LineSecs(string id) => Secs(id) + GuideTiming.Lead;

        void StartAndFinishIntro()
        {
            S.Start();
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step), "the intro ends in the open step");
        }

        void ToStep(int i)
        {
            StartAndFinishIntro();
            if (i > 0) S.BeginStep(i);
        }

        [Test]
        public void IdleUntilStarted()
        {
            // idleBeforeAudio: hidden, idle, not done, no step; nothing said.
            Assert.That(S.State, Is.EqualTo(SunnyState.Idle));
            Assert.IsFalse(S.Visible);
            Assert.IsFalse(S.Present);
            Assert.IsFalse(S.Done);
            Assert.That(S.Step, Is.EqualTo(-1));
            _r.Run(1f);
            Assert.That(_r.Voice.Plays, Is.Empty);
            Assert.That(S.LightIntensity, Is.EqualTo(0f));
        }

        [Test]
        public void StartGreetsWithTheDesktopIntro()
        {
            S.Start();
            // startsOnAudio, introCaptionFromTable, chimeTwoNotes (one open chirp = the engine's two tones)
            Assert.That(S.State, Is.EqualTo(SunnyState.Intro));
            Assert.IsTrue(S.Visible && S.Present && S.Desktop && S.BubbleVisible);
            Assert.That(S.LineId, Is.EqualTo("intro_desk"));
            Assert.That(S.Line, Is.EqualTo(GuideLines.TextOf("intro_desk")));
            Assert.That(S.Line, Does.Contain("Sunny"));
            Assert.That(_r.Voice.Chirps, Is.EqualTo(new[] { ChirpKind.Open }));
            Assert.That(S.Mood, Is.EqualTo(SunnyMood.Happy));
            // introHoldsForLine: max(3.2, 3.216 / 1 + 0.14 + 0.45)
            Assert.That(S.Timer, Is.EqualTo(Mathf.Max(3.2f, LineSecs("intro_desk") + 0.45f)).Within(1e-4f));
            // The recording starts at asked + 0.14 (the opening chirp first).
            Assert.That(_r.Voice.Plays, Is.EqualTo(new[] { "intro_desk" }));
            Assert.That(_r.Voice.PlayWhen[0], Is.EqualTo(0.14).Within(1e-9));
            Assert.IsTrue(S.TalkStarted && S.LineBusy());
            Assert.That(S.TalkDuration, Is.EqualTo(Secs("intro_desk")).Within(1e-4f));
            // noDemoInIntro
            Assert.IsFalse(S.Demo.Visible);
        }

        [Test]
        public void HeadsetRunUsesTheHandsLines()
        {
            // vrIntro, vrIntroHolds, vrLineForOpen
            _r.World.Desktop = false;
            S.Start();
            Assert.IsFalse(S.Desktop);
            Assert.That(S.LineId, Is.EqualTo("intro_vr"));
            Assert.That(S.Line, Does.Contain("show you around"));   // first five: intro_vr reworded (sunny-tour.md 5.1)
            Assert.That(S.Timer, Is.GreaterThan(3.5f));
            Assert.That(S.Timer, Is.EqualTo(LineSecs("intro_vr") + 0.45f).Within(1e-4f));
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step));
            Assert.That(S.LineId, Is.EqualTo("open_vr"));
            Assert.That(S.Line, Does.Contain("open palm toward your face"));
        }

        [Test]
        public void IntroBeginsTheOpenStepOnceItsLineHasEndedAndAGapPassed()
        {
            S.Start();
            double end = _r.Voice.PlayEnd[0];
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step));
            // stepOpenBegins: the step's line names M and the palm; the palm demo runs, Sunny is calm.
            Assert.That(S.Step, Is.EqualTo(0));
            Assert.That(S.LineId, Is.EqualTo("open_desk"));
            Assert.That(S.Line, Does.Contain("Press M").And.Contain("palm"));
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Palm));
            Assert.That(S.Mood, Is.EqualTo(SunnyMood.Calm));
            // At least the hold (3.806 s) and at least 0.3 s of quiet after the line (3.216 + 0.14 + 0.3).
            Assert.That(_r.Time, Is.GreaterThanOrEqualTo(Mathf.Max(3.2f, LineSecs("intro_desk") + 0.45f) - 1e-4f));
            Assert.That(_r.Time, Is.GreaterThanOrEqualTo((float)end + GuideTiming.Gap - 1e-4f));
            _r.Step();
            Assert.IsTrue(S.Demo.Visible, "demoVisibleOpen");
        }

        [Test]
        public void ANaturalEndGetsTheClosingChirp()
        {
            S.Start();
            int chirps = _r.Voice.Chirps.Count;
            Assert.IsTrue(_r.Until(() => S.TalkEnded, 5f));
            Assert.That(S.State, Is.EqualTo(SunnyState.Intro), "the intro line ends before the intro's hold");
            _r.Step();
            // endChirpAfterLine: one closing chirp, the mouth shut, nothing speaking.
            Assert.That(_r.Voice.ChirpCount(ChirpKind.Close), Is.EqualTo(1));
            Assert.That(_r.Voice.Chirps.Count, Is.EqualTo(chirps + 1));
            Assert.IsFalse(S.MouthOpen);
            Assert.IsFalse(S.Speaking());
            Assert.That(S.Playing, Is.EqualTo(0));
            Assert.That(S.MouthLevel, Is.EqualTo(0f));
        }

        [Test]
        public void ACutLineGetsNoClosingChirp()
        {
            S.Start();
            _r.Run(0.5f);
            Assert.IsTrue(S.Speaking());
            S.StopVoice();
            Assert.IsTrue(S.TalkCancelled);
            Assert.That(S.Stopped, Is.EqualTo(1));
            Assert.That(_r.Voice.Stops, Is.EqualTo(1));
            _r.Run(2f);
            Assert.That(S.State, Is.EqualTo(SunnyState.Intro));
            Assert.That(_r.Voice.ChirpCount(ChirpKind.Close), Is.EqualTo(0), "a cut line gets no closing chirp");
            Assert.IsFalse(S.LineBusy(), "a cut line is not busy: what follows does not wait for it");
        }

        [Test]
        public void MouthFollowsTheVoiceLevelNotASyllableClock()
        {
            // mouthFollowsAmplitude: words (0.12 s loud) and gaps (0.08 s quiet) open and shut it, with hysteresis.
            _r.Voice.LevelOf = t => (t % 0.2) < 0.12 ? 0.2f : 0.005f;
            S.Start();
            int open = 0, shut = 0, flips = 0;
            bool last = S.MouthOpen;
            _r.Run(0.15f);
            for (int i = 0; i < 180 && S.Speaking(); i++)
            {
                _r.Step();
                if (S.MouthOpen) open++; else shut++;
                if (S.MouthOpen != last) { flips++; last = S.MouthOpen; }
            }
            Assert.That(open, Is.GreaterThanOrEqualTo(5));
            Assert.That(shut, Is.GreaterThanOrEqualTo(3));
            Assert.That(flips, Is.GreaterThanOrEqualTo(4));
            // Hysteresis: a level between 0.2 and 0.35 of the peak keeps an open mouth open but does not open a shut one.
            _r.Voice.LevelOf = t => t < 0.5 ? 0.2f : 0.05f;   // 0.25 of the peak
            S.Say("intro_desk");
            _r.Run(0.3f);
            Assert.IsTrue(S.MouthOpen);
            _r.Run(0.5f);
            Assert.IsTrue(S.MouthOpen, "0.25 x peak > 0.2: stays open");
            _r.Voice.LevelOf = t => t < 0.5 ? 0.2f : (t < 0.6 ? 0.001f : 0.05f);
            S.Say("intro_desk");
            _r.Run(0.75f);
            Assert.IsFalse(S.MouthOpen, "after a gap, 0.25 x peak < 0.35 does not reopen it");
        }

        [Test]
        public void CompletingMidLineSmilesAtOnceButPraisesAfterTheLinePlusAGap()
        {
            StartAndFinishIntro();
            _r.Run(0.5f);   // open_desk is being said
            Assert.IsTrue(S.Speaking());
            int seq = S.TalkSeq, lines = S.Lines, chirps = _r.Voice.Chirps.Count, stops = _r.Voice.Stops;
            double end = _r.Voice.PlayEnd[_r.Voice.PlayEnd.Count - 1];
            _r.World.Shown = true;   // the panel opens (polled)
            _r.Step();
            // openCompletesWaits, demoStopsAtOnce, lineNotCutOnEarlyCompletion
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait));
            Assert.That(S.Queued, Is.EqualTo(SunnyNext.Praise));
            Assert.That(S.Mood, Is.EqualTo(SunnyMood.Happy));
            Assert.IsFalse(S.Demo.Visible);
            Assert.That(S.TalkSeq, Is.EqualTo(seq));
            Assert.IsFalse(S.TalkCancelled);
            Assert.IsTrue(S.Speaking());
            Assert.That(_r.Voice.Stops, Is.EqualTo(stops));
            Assert.That(_r.Voice.Chirps.Count, Is.EqualTo(chirps));
            Assert.That(S.Line, Is.EqualTo(GuideLines.TextOf("open_desk")));
            // noStackedPraise: more gestures while the line plays queue nothing more.
            S.Complete();
            S.OnPanel(MenuChange.Open);
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait));
            Assert.That(S.Lines, Is.EqualTo(lines));
            // lineFinishes / stepLineRanToEnd / breathBeforePraise / openCompletes
            Assert.IsTrue(_r.Until(() => S.LineId == "praise_0", 10f));
            float saidAt = _r.Time;
            // The caption comes after 0.3 s of quiet counted in whole frames; the praise's voice (0.14 s after it) is
            // always more than 0.3 s after the step line's end.
            Assert.That(saidAt, Is.GreaterThanOrEqualTo((float)end + GuideTiming.Gap - SunnyRig.Frame - 1e-4f));
            Assert.That(saidAt, Is.LessThan((float)end + GuideTiming.Gap + 3f * SunnyRig.Frame), "and not much later");
            Assert.That(S.State, Is.EqualTo(SunnyState.Praise));
            Assert.That(S.Lines, Is.EqualTo(lines + 1));
            Assert.That(S.Stopped, Is.EqualTo(0), "the step line was never cut");
            Assert.That(_r.Voice.ChirpCount(ChirpKind.Close), Is.EqualTo(2), "intro and step lines both ended naturally");
            // praiseAfterLine, noOverlapSoFar
            Assert.That(S.TalkStartAt, Is.GreaterThanOrEqualTo(end + GuideTiming.Gap), "praise >= 0.3 s after the line");
            Assert.That(S.MaxPlaying, Is.EqualTo(1));
            Assert.IsFalse(S.Demo.Visible, "demoHiddenInPraise");
            // praise holds max(1.7, 0.539 + 0.59) then the poke step (stepPokeBegins)
            Assert.That(S.Timer, Is.EqualTo(Mathf.Max(GuideTiming.PraiseT, LineSecs("praise_0") + 0.45f)).Within(1e-4f));
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step, 3f));
            Assert.That(S.Step, Is.EqualTo(1));
            Assert.That(S.Line, Does.Contain("row"));
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Poke));
        }

        [Test]
        public void CompletingAfterTheLineIsQuietPraisesAtOnce()
        {
            StartAndFinishIntro();
            Assert.IsTrue(_r.Until(() => S.QuietT >= GuideTiming.Gap, 10f));
            S.OnPanel(MenuChange.Open);
            Assert.That(S.State, Is.EqualTo(SunnyState.Praise));
            Assert.That(S.LineId, Is.EqualTo("praise_0"));
        }

        [Test]
        public void PokeCompletesWhenTheScreenChangesAndCloseWhenThePanelCloses()
        {
            _r.World.Shown = true;
            ToStep(1);
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Step), "stillWaitingPoke");
            _r.World.Screen = MenuScreen.Lessons;   // pokeCompletes (polled)
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step && S.Step == 2, 15f), "stepCloseBegins");
            Assert.That(S.Line, Does.Contain("hide"));
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Palm));
            _r.Run(0.5f);
            Assert.That(S.State, Is.EqualTo(SunnyState.Step), "stillWaitingClose");
            _r.World.Shown = false;   // closeCompletes
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait), "the close line is still being said");
            Assert.That(S.Queued, Is.EqualTo(SunnyNext.Praise));
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Praise, 10f), "closePraised");
        }

        [Test]
        public void MenuEventsCompleteTheirSteps()
        {
            ToStep(0);
            S.OnPanel(MenuChange.Close);
            Assert.That(S.State, Is.EqualTo(SunnyState.Step), "a close does not complete the open step");
            S.OnPanel(MenuChange.Open);
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
            S.BeginStep(1);
            S.OnPanel(MenuChange.Screen);
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
            _r.World.Shown = true;
            S.BeginStep(2);
            S.OnPanel(MenuChange.Open);
            Assert.That(S.State, Is.EqualTo(SunnyState.Step));
            S.OnPanel(MenuChange.Close);
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
        }

        [Test]
        public void SwipeTimesOutAfterTwelveSecondsWithItsSkipLine()
        {
            ToStep(3);
            float began = _r.Time;
            _r.Step();
            Assert.That(S.Line, Does.Contain("swipe"));
            Assert.IsTrue(S.Demo.Visible && S.Demo.Arrow, "sweepDemo");
            _r.Run(1.5f);
            Assert.That(S.Timer, Is.InRange(9.5f, 10.6f), "swipeStillWaiting");
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Skip, 13f));
            // swipeSkipsAfter12s, skipHoldsForLine
            Assert.That(_r.Time - began, Is.EqualTo(GuideTiming.StepTimeout).Within(2f * SunnyRig.Frame));
            Assert.That(S.LineId, Is.EqualTo("swipe_skip"));
            Assert.That(S.Line, Does.Contain("pick an instrument in the menu"));   // first five: swipe_skip reworded
            Assert.IsFalse(S.Demo.Visible);
            Assert.That(S.Timer, Is.GreaterThanOrEqualTo(LineSecs("swipe_skip") + 0.4f));
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step, 5f));
            Assert.That(S.Step, Is.EqualTo(4), "stepNodBegins");
            Assert.That(S.Line, Does.Contain("kick"));
        }

        [Test]
        public void SwipeCompletesOnAPageTurnInSingleMode()
        {
            ToStep(3);
            _r.Run(0.5f);
            _r.World.Current = InstrumentKind.Drums;
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Step), "all mode: no layout, nothing completes");
            _r.World.Single = true;   // switching to single mode records the first instrument seen
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Step));
            _r.World.Current = InstrumentKind.Harp;
            S.OnLayoutChanged();
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
        }

        [Test]
        public void SunnyNodsItselfInTheNodStep()
        {
            ToStep(4);
            _r.Step();
            // nodNoHand
            Assert.That(S.DemoKind, Is.EqualTo(DemoKind.Nod));
            Assert.IsFalse(S.Demo.Visible);
            float Dip() => S.Position.y - S.Base.y - Mathf.Sin(S.Time * GuideTiming.BobRateHz * Mathf.PI * 2f) * GuideTiming.BobAmp;
            float Tilt() => (S.Rotation * Vector3.back).y;   // the face looks along local -z
            float y0 = Dip(), tilt0 = Tilt();
            S.DemoT = 0.38f;
            _r.Step();
            // nodDips
            Assert.That(S.NodAmount, Is.GreaterThan(0.5f));
            Assert.That(Dip(), Is.LessThan(y0 - 0.02f));
            Assert.That(Dip(), Is.LessThan(-0.02f));
            Assert.That(Tilt(), Is.LessThan(tilt0 - 0.2f), "the face tips down");
            // nodComesUp, nodTwice
            S.DemoT = 0.75f;
            _r.Step();
            Assert.That(S.NodAmount, Is.EqualTo(0f));
            Assert.That(Mathf.Abs(S.Position.y - S.Base.y), Is.LessThanOrEqualTo(GuideTiming.BobAmp + 1e-5f));
            S.DemoT = 1.08f;
            _r.Step();
            Assert.That(S.NodAmount, Is.GreaterThan(0.5f));
            Assert.That(S.Position.y - S.Base.y, Is.LessThan(-0.02f));
        }

        [Test]
        public void DesktopKickCompletesTheNodStepButPlaybackNeverDoes()
        {
            ToStep(4);
            S.OnKick(true);
            Assert.That(S.State, Is.EqualTo(SunnyState.Step), "a kick from playback never counts");
            Assert.That(S.Kicks, Is.EqualTo(0));
            S.OnKick(false);
            Assert.That(S.Kicks, Is.EqualTo(1));
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "kickCompletes");
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Praise, 10f), "kickPraised");
            Assert.That(S.LineId, Is.EqualTo("praise_4"));
        }

        [Test]
        public void InTheHeadsetOnlyAKickWithTheHandsAwayFromThePadOrANodCounts()
        {
            _r.World.Desktop = false;
            ToStep(4);
            _r.World.PadFar = false;
            S.OnKick(false);
            Assert.That(S.State, Is.EqualTo(SunnyState.Step), "a hand on the pad is not a nod");
            _r.World.PadFar = true;
            S.OnKick(false);
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise));
            S.BeginStep(4);
            S.OnNod();   // nodSeenByGuide
            Assert.That(S.Nodded, Is.EqualTo(1));
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait).Or.EqualTo(SunnyState.Praise), "nodCompletes");
        }

        [Test]
        public void FinishSaysByeThenShrinksFadesRisesAndVanishes()
        {
            ToStep(4);
            S.OnKick(false);
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Bye, 15f));
            // saysHaveFun
            Assert.That(S.Line, Is.EqualTo("Have fun!"));
            Assert.That(S.LineId, Is.EqualTo("bye"));
            Assert.IsTrue(S.Done);
            Assert.That(S.Step, Is.EqualTo(-1));
            Assert.IsTrue(S.Present, "saying goodbye still counts as here");
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Leave, 5f));
            // leaves
            Assert.That(S.Line, Is.EqualTo("Bye!"));
            Assert.That(S.LineId, Is.EqualTo("leave"));
            Assert.IsFalse(S.Present);
            float y0 = S.LeaveY;
            _r.Run(0.4f);
            // leaveShrinksFades
            Assert.That(S.Scale, Is.InRange(0.3f, 0.7f));
            Assert.That(S.BodyOpacity, Is.LessThan(0.7f));
            Assert.That(S.BubbleOpacity, Is.LessThan(0.7f));
            Assert.That(S.Glow, Is.LessThan(0.7f));
            Assert.That(S.Position.y, Is.GreaterThan(y0 + 0.05f));
            Assert.That(S.LightIntensity, Is.LessThan(0.5f));
            _r.Run(0.45f);
            // gone
            Assert.That(S.State, Is.EqualTo(SunnyState.Gone));
            Assert.IsFalse(S.Visible);
            Assert.That(S.LightIntensity, Is.EqualTo(0f));
            Assert.IsFalse(S.BubbleVisible);
            Assert.IsFalse(S.Demo.Visible);
            Assert.IsTrue(S.Done);
            _r.Run(0.5f);
            Assert.That(S.State, Is.EqualTo(SunnyState.Gone), "staysGone");
            Assert.That(S.LightIntensity, Is.EqualTo(0f));
        }

        [Test]
        public void HideMidStepCutsTheVoiceAndLeavesWithinPointEightSeconds()
        {
            ToStep(3);
            _r.Run(0.3f);
            Assert.IsTrue(S.Speaking(), "hideLinePlays");
            S.Complete();
            _r.Step();
            Assert.That(S.State, Is.EqualTo(SunnyState.Wait), "waitingWhenHidden");
            Assert.IsFalse(S.TalkCancelled);
            int stopped = S.Stopped, stops = _r.Voice.Stops, seq = S.TalkSeq;
            S.Dismiss();
            // rowHidesSunny, hideStopsVoice
            Assert.That(S.State, Is.EqualTo(SunnyState.Leave));
            Assert.IsFalse(S.Present);
            Assert.IsTrue(S.Done);
            Assert.That(S.Step, Is.EqualTo(-1));
            Assert.IsFalse(S.Demo.Visible);
            Assert.That(S.Stopped, Is.EqualTo(stopped + 1));
            Assert.That(_r.Voice.Stops, Is.GreaterThanOrEqualTo(stops + 1));
            Assert.That(S.TalkSeq, Is.Not.EqualTo(seq));
            Assert.That(S.LineId, Is.EqualTo("leave"));
            Assert.That(S.Queued, Is.EqualTo(SunnyNext.None));
            float t0 = _r.Time;
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Gone, 2f));
            Assert.That(_r.Time - t0, Is.LessThanOrEqualTo(GuideTiming.LeaveT + 2f * SunnyRig.Frame), "gone within 0.8 s");
            Assert.That(S.Playing, Is.EqualTo(0));
            _r.Run(5f);
            Assert.That(S.State, Is.EqualTo(SunnyState.Gone), "hiddenStaysGone");
            // rowShowsAgain
            S.Toggle();
            Assert.That(S.State, Is.EqualTo(SunnyState.Intro));
            Assert.IsTrue(S.Present);
            Assert.IsFalse(S.Done);
        }

        [Test]
        public void ToggleBringsItBackBesideTheHeadAndHidesItAgain()
        {
            S.Toggle();
            _r.Step();
            // backBesideHead
            Assert.That(Vector3.Distance(S.Base, S.Goal()), Is.LessThan(0.01f));
            Assert.That(S.Scale, Is.EqualTo(1f));
            Assert.That(S.BodyOpacity, Is.EqualTo(1f));
            Assert.That(S.LightIntensity, Is.GreaterThan(0.4f));
            S.Toggle();
            Assert.That(S.State, Is.EqualTo(SunnyState.Leave), "shiftHHides");
            Assert.IsTrue(S.TalkId == "leave" && S.Done);
            _r.Run(1.2f);
            Assert.That(S.State, Is.EqualTo(SunnyState.Gone), "goneAgain");
            S.Dismiss();
            Assert.That(S.State, Is.EqualTo(SunnyState.Gone), "dismissing a gone Sunny does nothing");
        }

        [Test]
        public void SkipInsideAStepSaysItsSkipLineAtOnceAndOutsideItFinishes()
        {
            _r.World.Desktop = false;
            StartAndFinishIntro();
            _r.Run(0.3f);
            Assert.IsTrue(S.Speaking(), "vrStepPlays");
            int stopped = S.Stopped;
            S.Skip();
            // skipSkipsStep, stopsOnSkip
            Assert.That(S.State, Is.EqualTo(SunnyState.Skip));
            Assert.IsFalse(S.Demo.Visible);
            Assert.That(S.LineId, Is.EqualTo("skip"));
            Assert.That(S.Stopped, Is.EqualTo(stopped + 1));
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step, 6f));
            Assert.That(S.Step, Is.EqualTo(1), "skipThenNext");
            S.BeginStep(3);
            S.Skip();
            Assert.That(S.LineId, Is.EqualTo("swipe_skip"), "the swipe step has its own skip line");
            // skipOutsideStepFinishes
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step, 6f));
            _r.Until(() => S.State == SunnyState.Praise || S.State == SunnyState.Skip, 0.1f);
            S.OnNod();
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Praise, 10f));
            S.Skip();
            Assert.That(S.State, Is.EqualTo(SunnyState.Bye));
            Assert.IsTrue(S.Done);
        }

        [Test]
        public void AMissingRecordingIsACaptionOnly()
        {
            S.Start();
            _r.Voice.Missing.Add("nod_vr");
            int failed = S.Failed, plays = _r.Voice.Plays.Count;
            S.Say("nod_vr");
            // missingCaptioned, missingFileCaptionsOnly
            Assert.That(S.Line, Is.EqualTo(GuideLines.TextOf("nod_vr")));
            Assert.IsTrue(S.BubbleVisible);
            Assert.IsTrue(S.TalkFailed);
            Assert.IsFalse(S.TalkStarted);
            Assert.That(S.Failed, Is.EqualTo(failed + 1));
            Assert.That(_r.Voice.Plays.Count, Is.EqualTo(plays));
            // As a caption it stays busy as long as the line would take to say (manifest seconds + lead).
            _r.Run(LineSecs("nod_vr") - 0.2f);
            Assert.IsTrue(S.LineBusy());
            _r.Run(0.3f);
            Assert.IsFalse(S.LineBusy());
            _r.Step();
            Assert.IsFalse(S.MouthOpen);
            Assert.IsFalse(S.Speaking());
        }

        [Test]
        public void FreeTextIsCaptionedButNotVoiced()
        {
            S.Start();
            int plays = _r.Voice.Plays.Count, chirps = _r.Voice.Chirps.Count;
            S.Say("Any other words");
            // freeTextCaptionOnly (it still chimes)
            Assert.That(S.Line, Is.EqualTo("Any other words"));
            Assert.That(S.LineId, Is.EqualTo(""));
            Assert.That(S.TalkId, Is.EqualTo(""));
            Assert.IsTrue(S.BubbleVisible);
            Assert.That(_r.Voice.Plays.Count, Is.EqualTo(plays));
            Assert.That(_r.Voice.Chirps.Count, Is.EqualTo(chirps + 1));
            Assert.IsFalse(S.LineBusy());
            // noThrow: hush clears the caption.
            _r.Step();
            S.Hush();
            _r.Step();
            Assert.That(S.Line, Is.EqualTo(""));
            Assert.IsFalse(S.BubbleVisible);
        }

        [Test]
        public void ARecordingLaterThanOnePointTwoSecondsIsDroppedAnEarlierOnePlaysAtOnce()
        {
            S.Start();
            _r.Voice.ReadyAt["open_desk"] = _r.Voice.Clock + 1.5;
            int plays = _r.Voice.Plays.Count;
            S.Say("open_desk");
            Assert.IsTrue(S.TalkPending);
            Assert.IsTrue(S.LineBusy(), "still on its way");
            Assert.IsTrue(_r.Until(() => S.TalkFailed, 2f));
            Assert.IsTrue(S.TalkLate);
            Assert.That(_r.Voice.Plays.Count, Is.EqualTo(plays), "late: captions only");
            // Ready 0.5 s after the caption: it plays as soon as it is ready (later than asked + 0.14).
            _r.Voice.ReadyAt["poke_desk"] = _r.Voice.Clock + 0.5;
            double asked = _r.Voice.Clock;
            S.Say("poke_desk");
            Assert.IsTrue(_r.Until(() => S.TalkStarted, 1f));
            Assert.That(_r.Voice.PlayWhen[_r.Voice.PlayWhen.Count - 1], Is.InRange(asked + 0.5 - 1e-6, asked + 0.5 + 2.0 * SunnyRig.Frame));
            Assert.IsFalse(S.TalkFailed);
        }

        [Test]
        public void WithoutAudioTheTourRunsOnCaptionsAndChirpsNothing()
        {
            _r.Voice.Up = false;
            S.Start();
            Assert.That(_r.Voice.Chirps, Is.Empty);
            Assert.That(_r.Voice.Plays, Is.Empty);
            Assert.IsTrue(S.LineBusy(), "a caption is busy for its line's length");
            Assert.IsTrue(_r.Until(() => S.State == SunnyState.Step, 6f));
            Assert.That(S.LineId, Is.EqualTo("open_desk"));
        }

        [Test]
        public void BlinksFirstAfterTwoToFiveSecondsThenEveryTwoPointFiveToSixForPointThirteen()
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                var r = new SunnyRig(seed);
                r.Sunny.Start();
                float last = -1f, shutAt = -1f;
                int blinks = 0;
                bool was = false;
                for (int i = 0; i < 30 * 72; i++)
                {
                    r.Step();
                    bool shut = r.Sunny.EyesShut;
                    if (shut && !was)
                    {
                        shutAt = r.Time;
                        if (blinks == 0) Assert.That(r.Time, Is.InRange(2f, 5f + 2f * SunnyRig.Frame), "first blink");
                        else Assert.That(r.Time - last, Is.InRange(2.5f + GuideTiming.BlinkT - 2f * SunnyRig.Frame, 6f + GuideTiming.BlinkT + 2f * SunnyRig.Frame), "between blinks");
                        last = r.Time;
                        blinks++;
                    }
                    if (!shut && was) Assert.That(r.Time - shutAt, Is.EqualTo(GuideTiming.BlinkT).Within(2f * SunnyRig.Frame), "shut for 0.13 s");
                    was = shut;
                }
                Assert.That(blinks, Is.GreaterThanOrEqualTo(4));
            }
        }

        [Test]
        public void FloatsBesideTheHeadFollowsSmoothlyBobsAndFacesThePlayer()
        {
            S.Start();
            _r.Step();
            // sidePosition / besideHead: head + 0.55 fwd - 0.25 right, 0.10 under the eyes (snapped on the first frame)
            Vector3 f = _r.World.Forward;
            f.y = 0f;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 want = _r.World.Head + f * 0.55f - right * 0.25f;
            want.y = _r.World.Head.y - 0.1f;
            Assert.That(Vector3.Distance(S.Base, want), Is.LessThan(1e-4f));
            Assert.That(want.x, Is.LessThan(_r.World.Head.x), "to the player's left");
            // facesPlayer: the face (-z) looks at the head
            Vector3 toHead = (_r.World.Head - S.Position).normalized;
            Assert.That(Vector3.Dot(S.Rotation * Vector3.back, toHead), Is.GreaterThan(0.99f));
            // bobs
            _r.Run(0.5f);
            Assert.That(Mathf.Abs(S.Position.y - S.Base.y), Is.LessThanOrEqualTo(0.0125f));
            Assert.That(S.Position.y, Is.Not.EqualTo(S.Base.y));
            // lightPulses (reference units 0.7 +- 0.2)
            Assert.That(S.LightIntensity, Is.InRange(0.5f, 0.9f));
            // Follow: 1 - e^(-3 dt) of the way per frame.
            Vector3 before = S.Base;
            _r.World.Head += new Vector3(1f, 0f, 0f);
            Vector3 goal = S.Goal();
            _r.Step(0.05f);
            float k = 1f - Mathf.Exp(-0.05f * GuideTiming.Follow);
            Assert.That(Vector3.Distance(S.Base, Vector3.Lerp(before, goal, k)), Is.LessThan(1e-5f));
            // Frozen while leaving.
            S.Dismiss();
            Vector3 frozen = S.Base;
            _r.World.Head += new Vector3(1f, 0f, 0f);
            _r.Run(0.3f);
            Assert.That(S.Base, Is.EqualTo(frozen));
        }

        [Test]
        public void HoldIsAtLeastTheStatesTimeAndTheLinePlusPad()
        {
            for (int i = 0; i < GuideLines.Count; i++)
            {
                string id = GuideLines.Ids[i];
                Assert.That(S.LineSeconds(id), Is.EqualTo(GuideLines.ManifestSeconds[i] + 0.14f).Within(1e-5f));
                Assert.That(S.Hold(id, 1.7f), Is.EqualTo(Mathf.Max(1.7f, GuideLines.ManifestSeconds[i] + 0.14f + 0.45f)).Within(1e-5f));
            }
            Assert.That(S.LineSeconds("not a line"), Is.EqualTo(0f));
        }

        [Test]
        public void TheWholeDesktopTourInOrder()
        {
            var said = new System.Collections.Generic.List<string>();
            int version = -1;
            void Watch()
            {
                if (S.LineVersion != version && S.LineId.Length > 0) said.Add(S.LineId);
                version = S.LineVersion;
            }
            S.Start();
            Watch();
            for (int i = 0; i < 120 * 72 && S.State != SunnyState.Gone; i++)
            {
                // The player does each gesture a little after its step begins (the swipe is never done: it times out).
                if (S.State == SunnyState.Step && S.DemoT > 1f)
                {
                    switch (S.CurrentStep.Id)
                    {
                        case StepId.Open: _r.World.Shown = true; break;
                        case StepId.Poke: _r.World.Screen = MenuScreen.Lessons; break;
                        case StepId.Close: _r.World.Shown = false; break;
                        case StepId.Nod: S.OnKick(false); break;
                    }
                }
                _r.Step();
                Watch();
            }
            Assert.That(S.State, Is.EqualTo(SunnyState.Gone));
            Assert.That(said, Is.EqualTo(new[]
            {
                "intro_desk", "open_desk", "praise_0", "poke_desk", "praise_1", "close_desk", "praise_2",
                "swipe_desk", "swipe_skip", "nod_desk", "praise_4", "bye", "leave",
            }));
            Assert.That(S.MaxPlaying, Is.EqualTo(1), "neverOverlapped");
            Assert.That(S.Stopped, Is.EqualTo(0), "no line was cut");
            Assert.IsTrue(S.Done);
        }
    }
}
