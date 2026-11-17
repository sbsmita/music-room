// Sunny's tape deck walkthrough ("Make a song in layers"; GUIDE ends it) in the booted app, with scripted fingertips (FakeHandSource)
// poking the deck and Sunny, the scripted audio clock and manual frames: every step advances only on its own action;
// poking Sunny skips (doing the step for the player where the next one needs it); 15 s without progress gives up the
// same way; GUIDE again ends it with the deck tidy; in the single-instrument layout each loop step brings its
// instrument in and the layout comes back at the end; with nobody playing, nothing waits more than 20 s without
// progress and the walkthrough ends by itself. Sunny perches beside the deck without covering any control.
using System;
using System.Collections;
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
    public class DeckGuideTests
    {
        const float Dt = 1f / 30f;
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

        // The walkthrough starts from Sunny's menu page ("Make a song in layers"); the deck's GUIDE starts the quick show
        // (DeckQuickShowTests) and ends either.
        void StartWalkthrough()
        {
            _guide.StartDeckWalkthrough();
            Assert.IsTrue(G.Running && !G.Quick, "the walkthrough runs");
        }
        void Poke(DeckButton b) => LooperPlayTests.Poke(_boot, _m.Deck, b, () => Step());

        void StepUntil(Func<bool> done, float seconds, string what)
        {
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n; i++) { if (done()) return; Step(); }
            Assert.IsTrue(done(), $"{what}: not within {seconds} s (step {G.Step}, idle {G.Idle:F1}, loop {App.Current.Get<ILooper>().Status})");
        }

        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        // A fingertip into Sunny (from 20 cm away), then away again.
        void PokeSunny()
        {
            Vector3 c = _guide.Sunny.Position;
            Vector3 far = c + Vector3.up * 0.2f + Vector3.left * 0.1f;
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step(); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(_guide.Sunny.Position)); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(far)); Step();
            _boot.Hands.SetPose(HandSide.Right, IndexAt(new Vector3(0f, 0.3f, 0.6f))); Step();   // hand back out of the way
            Step(18);                                                                          // past the 0.5 s poke debounce
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

        // From the seat, every deck control is in plain sight: Sunny's body and bubble never cross the line of sight.
        void AssertNothingCovered(string when)
        {
            Vector3 eye = App.Current.Head.position;
            Sunny s = _guide.Sunny;
            Transform bubble = _guide.View.Bubble;
            for (int i = 0; i < TapeDeck.ControlCount; i++)
            {
                var b = (DeckButton)i;
                Vector3 top = _m.Deck.Root.TransformPoint(TapeDeck.ButtonLocal(b));
                Vector3 d = top - eye;
                float u = Mathf.Clamp01(Vector3.Dot(s.Position - eye, d) / d.sqrMagnitude);
                float dist = Vector3.Distance(eye + d * u, s.Position);
                Assert.That(dist, Is.GreaterThan(0.055f), $"{when}: Sunny's body covers {b}");
                if (bubble != null && bubble.gameObject.activeInHierarchy)
                {
                    var plane = new Plane(bubble.forward, bubble.position);
                    if (plane.Raycast(new Ray(eye, d.normalized), out float hit) && hit < d.magnitude)
                    {
                        Vector3 l = bubble.InverseTransformPoint(eye + d.normalized * hit);
                        Assert.IsFalse(Mathf.Abs(l.x) < 0.13f && Mathf.Abs(l.y) < 0.058f, $"{when}: the bubble covers {b}");
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator EveryStepWaitsForItsOwnActionToTheEnd()
        {
            yield return Boot();
            ILooper looper = App.Current.Get<ILooper>();
            IRecorder rec = App.Current.Get<IRecorder>();
            StartWalkthrough();
            Assert.IsTrue(G.Running, "GUIDE starts the walkthrough");
            Assert.IsTrue(_guide.DeckGuideRunning);
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Script));
            Step(2);
            Assert.That(_m.Deck.CapGlow(DeckButton.Guide), Is.EqualTo(TapeDeck.Selected), "GUIDE's light is on while it runs");
            StepUntil(() => G.Step == DeckGuideStep.Rec, 10f, "intro");
            Step(10);
            Assert.That(G.Pointing, Is.EqualTo(LoopControl.Rec), "the ghost hand points at REC");
            Assert.That(_guide.Sunny.DemoKind, Is.EqualTo(DemoKind.Press));
            Assert.IsTrue(looper.TryGetControl(LoopControl.Rec, 0, out Vector3 recTop, out _));
            Assert.That(Vector3.Distance(_guide.Sunny.PressAnchor, recTop), Is.LessThan(1e-4f));
            AssertNothingCovered("REC step");

            Poke(DeckButton.Play);                          // the wrong button
            Step(10);
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Rec), "PLAY does not do the REC step");
            Poke(DeckButton.Rec);
            Assert.That(rec.State, Is.EqualTo(RecorderState.Recording));
            Note(60); Note(62);
            Step(20);
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Rec), "two notes are not yet a few");
            Note(64);
            StepUntil(() => G.Step == DeckGuideStep.Stop, 6f, "REC + three notes");

            Note(65);
            Step(30);
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Stop), "playing more does not press STOP");
            Assert.That(G.Pointing, Is.EqualTo(LoopControl.Stop));
            Poke(DeckButton.Stop);
            StepUntil(() => G.Step == DeckGuideStep.Play, 6f, "STOP");
            Assert.That(rec.Take.Count, Is.GreaterThanOrEqualTo(6));

            Poke(DeckButton.Play);
            Assert.That(rec.State, Is.EqualTo(RecorderState.Playing));
            StepUntil(() => G.Step == DeckGuideStep.Drums, 14f, "PLAY and listen");
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle), "never left playing on");

            Assert.That(G.Pointing, Is.EqualTo(LoopControl.Loop).Or.Null);
            StepUntil(() => G.Pointing == LoopControl.Loop, 6f, "the LOOP demo");
            Drum("snare");                                  // drums without LOOP: no loop, no progress
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Off));
            Poke(DeckButton.Loop);
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Waiting));
            Step(15);
            Drum("kick"); Step(15); Drum("snare"); Step(15); Drum("kick");
            StepUntil(() => G.Step == DeckGuideStep.Harp, 14f, "a beat that repeats");
            Assert.That(looper.Layers, Is.EqualTo(1));

            StepUntil(() => G.Pointing == LoopControl.Loop, 6f, "LOOP again");
            Poke(DeckButton.Loop);
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Armed));
            StepUntil(() => looper.Status == LoopStatus.Overdubbing, 12f, "the harp's pass");
            for (int i = 0; i < 4; i++) { App.Current.Get<IHarpApi>().PlaybackPluck(7 + i, 0.6f); Step(10); }
            StepUntil(() => G.Step == DeckGuideStep.Piano, 14f, "the harp on top");
            Assert.That(looper.Layers, Is.EqualTo(2));

            StepUntil(() => G.Pointing == LoopControl.Loop, 6f, "LOOP for the piano");
            Poke(DeckButton.Loop);
            StepUntil(() => looper.Status == LoopStatus.Overdubbing, 12f, "the piano's pass");
            Note(67); Note(72);
            StepUntil(() => G.Step == DeckGuideStep.Mute, 16f, "the piano on top");
            Assert.That(looper.Layers, Is.EqualTo(3));

            StepUntil(() => G.Pointing == LoopControl.Layer, 8f, "the mute demo");
            AssertNothingCovered("mute step");
            Poke(DeckButton.Undo);                          // not the light
            Step(10);
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Mute), "UNDO is not the mute step");
            Assert.That(looper.Layers, Is.EqualTo(2), "(but it did undo)");
            Poke(DeckButton.Layer1);
            StepUntil(() => G.Step == DeckGuideStep.Undo, 6f, "mute");
            StepUntil(() => G.Pointing == LoopControl.Undo, 6f, "the UNDO demo");
            Poke(DeckButton.Undo);
            StepUntil(() => G.Step == DeckGuideStep.Saved || G.Step == DeckGuideStep.Keep, 6f, "undo");
            StepUntil(() => G.Step == DeckGuideStep.Keep, 8f, "saved in Recordings");
            Assert.That(G.Pointing, Is.EqualTo(LoopControl.Clear).Or.Null);
            Poke(DeckButton.Clear); Step(5); Poke(DeckButton.Clear);
            StepUntil(() => !G.Running, 12f, "cleared, bye");
            Assert.That(looper.Active, Is.False, "the player chose to clear");
            Assert.That(G.Skips + G.GiveUps + G.Assists, Is.EqualTo(0), "the player did every step");
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Leave).Or.EqualTo(SunnyState.Gone));
            Step(3);
            Assert.That(_m.Deck.CapGlow(DeckButton.Guide).r, Is.EqualTo(0), "GUIDE's light goes out");
        }

        [UnityTest]
        public IEnumerator PokingSunnySkipsGivingUpDoesTheStepAndGuideEndsItTidy()
        {
            yield return Boot();
            IRecorder rec = App.Current.Get<IRecorder>();
            ILooper looper = App.Current.Get<ILooper>();
            StartWalkthrough();
            StepUntil(() => G.Step == DeckGuideStep.Rec && G.Pointing.HasValue, 10f, "REC step");
            PokeSunny();
            Assert.That(G.Skips, Is.EqualTo(1));
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Stop), "skipped");
            Assert.That(rec.State, Is.EqualTo(RecorderState.Recording), "REC pressed for the player, so STOP makes sense");
            Assert.That(G.Assists, Is.EqualTo(1));
            // 15 s with nothing done: STOP is pressed for the player, the empty take has nothing to play, on to the drums.
            StepUntil(() => G.Step == DeckGuideStep.Drums, DeckGuide.GiveUpSeconds + 8f, "give up on STOP");
            Assert.That(G.GiveUps, Is.EqualTo(1));
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
            // The drums step's give-up presses LOOP once (the clicks wait for a note)...
            StepUntil(() => looper.Status == LoopStatus.Waiting, DeckGuide.GiveUpSeconds + 8f, "LOOP for the player");
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Drums), "still the drums: the player may play now");
            // ...and GUIDE ends it: the waiting clicks stop, nothing is left half done.
            Poke(DeckButton.Guide);
            Assert.IsFalse(G.Running);
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Off), "a loop still waiting for its first note is cancelled");
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(_guide.Sunny.State, Is.EqualTo(SunnyState.Leave));
        }

        [UnityTest]
        public IEnumerator InTheSingleLayoutEachLoopStepBringsItsInstrumentAndTheLayoutComesBack()
        {
            yield return Boot();
            ILayoutService layout = App.Current.Get<ILayoutService>();
            layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Step(5);
            StartWalkthrough();
            StepUntil(() => G.Step == DeckGuideStep.Rec, 10f, "REC step");
            PokeSunny();                                    // REC pressed for the player -> STOP
            PokeSunny();                                    // STOP pressed -> (nothing to play) -> drums
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Drums));
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Drums), "the drums come in");
            StepUntil(() => G.Pointing.HasValue, 6f, "the drums line");
            PokeSunny();
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Harp));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Harp), "then the harp");
            StepUntil(() => G.Pointing.HasValue, 6f, "the harp line");
            PokeSunny();
            Assert.That(G.Step, Is.EqualTo(DeckGuideStep.Piano));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Piano), "then the piano");
            Poke(DeckButton.Guide);
            Assert.IsFalse(G.Running);
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(layout.Current, Is.EqualTo(InstrumentKind.Harp), "the layout comes back");
        }

        [UnityTest]
        public IEnumerator WithNobodyPlayingNothingWaitsTwentySecondsAndItEndsTidy()
        {
            yield return Boot();
            IRecorder rec = App.Current.Get<IRecorder>();
            ILooper looper = App.Current.Get<ILooper>();
            StartWalkthrough();
            double last = _t, worst = 0;
            DeckGuideStep step = G.Step;
            int assists = 0, giveUps = 0, lineVersion = _guide.Sunny.LineVersion;
            LoopStatus loop = looper.Status;
            RecorderState rs = rec.State;
            int frames = 0;
            string worstAt = "";
            const float big = 1f / 20f;                     // coarse frames (the app clamps a frame to 0.05 s): 400 s in a few seconds
            while (G.Running && frames++ < (int)(400f / big))
            {
                Step(1, big);
                bool changed = G.Step != step || G.Assists != assists || G.GiveUps != giveUps || _guide.Sunny.LineVersion != lineVersion
                    || looper.Status != loop || rec.State != rs;
                if (changed)
                {
                    if (_t - last > worst) { worst = _t - last; worstAt = $"{step}/{loop}/{rs} -> {G.Step}/{looper.Status}/{rec.State} (assists {assists}->{G.Assists}, line {lineVersion}->{_guide.Sunny.LineVersion}: {_guide.Sunny.LineId})"; }
                    last = _t;
                    step = G.Step; assists = G.Assists; giveUps = G.GiveUps; lineVersion = _guide.Sunny.LineVersion;
                    loop = looper.Status; rs = rec.State;
                }
                Assert.That(_t - last, Is.LessThan(DeckGuide.GiveUpSeconds + 1.0), $"no progress for 16 s in step {G.Step} (the rule: never 20 s)");
            }
            Assert.IsFalse(G.Running, "the walkthrough ends by itself");
            Debug.Log($"[Guide] deck walkthrough with nobody playing: {_t - 50.0:F0} s, longest wait {worst:F2} s ({worstAt}), give-ups {G.GiveUps}, assists {G.Assists}");
            Assert.That(G.GiveUps, Is.GreaterThanOrEqualTo(5));
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle), "no take left recording or playing");
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Off).Or.EqualTo(LoopStatus.Looping), "no loop left waiting or armed");
        }
    }
}
