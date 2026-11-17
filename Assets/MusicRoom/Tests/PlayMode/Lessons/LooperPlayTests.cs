// The looper inside the booted app (TestBoot, manual frames, the scripted audio clock, AudioTestTap, FakeHandSource pokes
// on the deck): LOOP's clicks to the sample, the first note anchoring layer 1, the real instruments' playback scheduled
// sample-accurately through IAudioEngine.ScheduleAt (Calls carry the stamped When), restarts exactly one pass apart,
// REC arming an overdub on the bar, the layer lights muting, UNDO, CLEAR with its second tap, a session saved to the
// library and looped again from Recordings, a lesson stopping the loop, the classic three's looped notes on their samples
// (today's synths: the only sound of the piano, drums and harp since 2026-11-10), held keys released at the loop end, Sunny's first-use line (once, persisted), and no allocation per frame.
using System;
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    public class LooperPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t;
        LessonsModule _m;
        AudioTestTap _tap;
        IAudioEngine _engine;
        int _sr;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SoundUnits.Override = null;
            AudioSchedule.Reset();
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        IEnumerator Boot(bool tipSeen = true, Action<TestBoot> configure = null)
        {
            _t = 50.0;
            _boot = new TestBoot(headless: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            if (tipSeen) _boot.Settings.SetString(GuideModule.KeyTipsSeen, AppLoopHost.TipLoopStart);
            configure?.Invoke(_boot);
            yield return _boot.Start();
            // The desktop rig comes up after ~0.5 s of real time; Layout's first arrange follows on the next frame.
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Current.Get<ILayoutService>();
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) { Step(); yield return null; }
            Assert.IsTrue(layout.FirstArrangeDone);
            foreach (IAppModule x in App.Current.Modules) if (x is LessonsModule l) _m = l;
            _m.Looper.InputLatency = 0;                      // exact sample positions (the device value is a tuning knob)
            _engine = App.Current.Get<IAudioEngine>();
            _sr = _engine.SampleRate > 0 ? _engine.SampleRate : 48000;
            _tap = new AudioTestTap();
            _engine.TestTap = _tap;
        }

        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }

        long S(double seconds) => (long)Math.Round(seconds * _sr);
        long Now => S(_t);

        /// <summary>Frames until the audio clock reaches `sample` (the last frame lands on it exactly).</summary>
        void StepTo(long sample)
        {
            double target = sample / (double)_sr;
            while (_t + Dt < target) Step();
            if (target > _t) { float dt = (float)(target - _t); _t = target; _boot.Step(dt); }
        }

        static HandPose IndexAt(Vector3 tip)
        {
            HandPose p = HandPose.OpenFacing(HandSide.Right, tip + Vector3.up * 0.15f, Vector3.down, Vector3.forward).WithPointing().WithIndexTipAt(tip);
            foreach (HandJoint j in new[] { HandJoint.ThumbTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip }) p.Position[(int)j] += Vector3.up * 0.3f;
            return p;
        }

        /// <summary>A fingertip pokes a deck control: from 6 cm over it, down into its press zone, and back up.</summary>
        public static void Poke(TestBoot boot, TapeDeck deck, DeckButton b, Action step)
        {
            Vector3 l = TapeDeck.ButtonLocal(b);
            bool peg = deck.IsPeg(b);
            Vector3 above = deck.Root.TransformPoint(new Vector3(l.x, 0.15f, l.z));
            Vector3 at = deck.Root.TransformPoint(new Vector3(l.x, peg ? 0.08f : 0.082f, l.z));
            boot.Hands.SetPose(HandSide.Right, IndexAt(above)); step(); step();
            boot.Hands.SetPose(HandSide.Right, IndexAt(at)); step();
            boot.Hands.SetPose(HandSide.Right, IndexAt(above)); step();
        }

        void Poke(DeckButton b) => Poke(_boot, _m.Deck, b, () => Step());

        List<long> Scheduled(AudioCall m, int index = -1)
        {
            var l = new List<long>();
            foreach (var c in _tap.Calls) if (c.Method == m && c.When > 0 && (index < 0 || c.Index == index)) l.Add(S(c.When));
            return l;
        }

        IDrumsApi Drums => App.Current.Get<IDrumsApi>();
        IPianoApi Piano => App.Current.Get<IPianoApi>();
        static readonly DrumStrikeInfo Head = new DrumStrikeInfo { Pos = 0.3f, Zone = "head" };

        /// <summary>LOOP, then a live kick on click 4 (layer 1's beat 1), a snare on beat 3 and a hat on beat 5.5.</summary>
        long RecordBeat()
        {
            Poke(DeckButton.Loop);
            Assert.That(_m.Looper.State, Is.EqualTo(LoopState.Waiting));
            double beat = _m.Looper.Session.BeatSamples;
            long a = _m.Looper.ClickStart + (long)Math.Round(4 * beat);
            StepTo(a);
            Drums.PlaybackHit("kick", 0.8f, Head);            // a live hit, as the kit makes it (recorded through IRecorder.Add)
            Assert.That(_m.Looper.State, Is.EqualTo(LoopState.Recording));
            Assert.That(_m.Looper.Anchor, Is.EqualTo(a));
            StepTo(a + (long)Math.Round(2 * beat));
            Drums.PlaybackHit("snare", 0.7f, Head);
            StepTo(a + (long)Math.Round(5.5 * beat));
            Drums.PlaybackHit("hat", 0.5f, Head);
            return a;
        }

        [UnityTest]
        public IEnumerator LoopClicksTheCountAndLayerOnePlaysBackOnTheSampleEveryPass()
        {
            yield return Boot();
            LoopSession s = null;
            Poke(DeckButton.Loop);
            s = _m.Looper.Session;
            Assert.That(s.Bpm, Is.EqualTo(90)); Assert.That(s.Bars, Is.EqualTo(2), "the defaults");
            long start = _m.Looper.ClickStart;
            Assert.That(start - Now, Is.LessThanOrEqualTo(S(Looper.LeadIn) + 1), "the first click right after the press");
            double beat = s.BeatSamples;
            StepTo(start + (long)Math.Round(2.5 * beat));
            List<long> clicks = Scheduled(AudioCall.Click);
            Assert.That(clicks.Count, Is.GreaterThanOrEqualTo(3));
            for (int j = 0; j < clicks.Count; j++) Assert.That(clicks[j], Is.EqualTo(start + (long)Math.Round(j * beat)), $"click {j}");
            Assert.That(_m.Deck.LampGlow(TapeDeck.CountLamp0 + 2), Is.EqualTo(TapeDeck.CountBright), "three count lights on beat 3");
            Assert.That(_m.Deck.LampGlow(TapeDeck.CountLamp0 + 3).r, Is.EqualTo(0), "the fourth not yet");
            Assert.That(_m.Deck.PegDepth(DeckButton.Loop), Is.GreaterThan(0.5f), "LOOP stays down");

            // The first note, on click 4: layer 1 starts there.
            long a = start + (long)Math.Round(4 * beat);
            StepTo(a);
            Drums.PlaybackHit("kick", 0.8f, Head);
            Step();
            Assert.That(_m.Looper.Anchor, Is.EqualTo(a));
            Assert.That(_m.Deck.LampColor.r, Is.GreaterThan(0.9f), "the lamp is red while layer 1 records");
            Assert.That(_m.Deck.CapGlow(DeckButton.Layer1), Is.EqualTo(TapeDeck.LayerRec));
            StepTo(a + (long)Math.Round(3 * beat));
            Drums.PlaybackHit("snare", 0.7f, Head);
            long L = s.PassSamples;
            StepTo(a + L + S(0.1));
            Assert.That(_m.Looper.State, Is.EqualTo(LoopState.Looping));
            Assert.That(_m.Deck.CapGlow(DeckButton.Layer1), Is.EqualTo(TapeDeck.LayerOn));
            Assert.That(_m.Recorder.State, Is.EqualTo(RecorderState.Playing));
            StepTo(a + 4 * L - S(0.2));
            List<long> drums = Scheduled(AudioCall.Drum);
            long snareOff = (long)Math.Round(3 * beat);
            Assert.That(drums, Is.EqualTo(new[] { a + L, a + L + snareOff, a + 2 * L, a + 2 * L + snareOff, a + 3 * L, a + 3 * L + snareOff }),
                "the kit's own playback, stamped by ScheduleAt to the exact sample of every pass: no gap, no drift");
            Assert.That(_m.Looper.Session.Layers[0].Notes.Count, Is.EqualTo(2), "playback is never recorded again");
            Poke(DeckButton.Stop);
            Assert.That(_m.Looper.Active, Is.False);
        }

        [UnityTest]
        public IEnumerator RecArmsAnOverdubOnTheBarAndTheLightsMuteUndoAndClear()
        {
            yield return Boot();
            long a = RecordBeat();
            LoopSession s = _m.Looper.Session;
            long L = s.PassSamples;
            double beat = s.BeatSamples;
            StepTo(a + L + (long)Math.Round(2 * beat));
            Poke(DeckButton.Rec);
            Assert.IsTrue(_m.Looper.Armed, "REC while looping arms the next layer");
            long ps = a + 2 * L;
            Assert.That(s.Layers[1].PassStart, Is.EqualTo(ps), "on the next loop start");
            StepTo(ps - (long)Math.Round(2.5 * beat));
            Assert.That(_m.Deck.LampGlow(TapeDeck.CountLamp0 + 2), Is.EqualTo(TapeDeck.CountBright), "three beats to go");
            Assert.That(_m.Deck.LampGlow(TapeDeck.CountLamp0 + 3).r, Is.EqualTo(0));
            // The piano over it, on the computer-keyboard path (a live presser), in layer 2's pass.
            object key = new object();
            StepTo(ps + (long)Math.Round(beat));
            Piano.PressMidi(64, key, 0.7f);
            StepTo(ps + (long)Math.Round(2 * beat));
            Piano.ReleaseMidi(64, key);
            StepTo(ps + L + S(0.1));
            Assert.That(_m.Looper.Layers, Is.EqualTo(2));
            StepTo(ps + 2 * L - S(0.2));
            Assert.That(Scheduled(AudioCall.Piano, 64), Is.EqualTo(new[] { ps + L + (long)Math.Round(beat) }), "layer 2 plays from the pass after its own");

            // Layer 1's light mutes it; poked again, unmutes.
            Poke(DeckButton.Layer1);
            Assert.That(_m.Looper.Lamp(0), Is.EqualTo(LayerLamp.Muted));
            Assert.That(_m.Deck.CapGlow(DeckButton.Layer1), Is.EqualTo(TapeDeck.LayerMuted));
            int kicks = Scheduled(AudioCall.Drum, (int)DrumKind.Kick).Count;
            StepTo(ps + 3 * L - S(0.2));
            Assert.That(Scheduled(AudioCall.Drum, (int)DrumKind.Kick).Count, Is.EqualTo(kicks), "muted: no kick");
            Assert.That(Scheduled(AudioCall.Piano, 64).Count, Is.EqualTo(2), "layer 2 plays on");
            Poke(DeckButton.Layer1);
            Assert.That(_m.Looper.Lamp(0), Is.EqualTo(LayerLamp.On));
            StepTo(ps + 4 * L - S(0.2));
            Assert.That(Scheduled(AudioCall.Drum, (int)DrumKind.Kick).Count, Is.EqualTo(kicks + 1), "unmuted: back");

            Poke(DeckButton.Undo);
            Assert.That(_m.Looper.Layers, Is.EqualTo(1), "UNDO removed the last layer");
            Assert.That(_m.Deck.CapGlow(DeckButton.Layer2).r + _m.Deck.CapGlow(DeckButton.Layer2).g, Is.EqualTo(0), "its light is out");
            Poke(DeckButton.Clear);
            Step(3);
            Assert.IsTrue(_m.Looper.ClearArmed);
            Assert.IsTrue(_m.Deck.TapAgainShown, "CLEAR's label reads TAP AGAIN");
            Assert.That(_m.Looper.Active, Is.True, "one tap only arms");
            Poke(DeckButton.Clear);
            Step();
            Assert.That(_m.Looper.Active, Is.False, "the second tap stops");
            Assert.That(_m.Looper.Session, Is.Null, "and clears");
            Assert.IsFalse(_m.Deck.TapAgainShown);
        }

        [UnityTest]
        public IEnumerator AStoppedSessionIsSavedToRecordingsAndLoopsAgainFromThere()
        {
            yield return Boot();
            int changes = 0;
            App.Current.Events.Subscribe<LibraryChangedEvent>(e => changes++);
            long a = RecordBeat();
            long L = _m.Looper.Session.PassSamples;
            StepTo(a + L + S(0.1));
            Poke(DeckButton.Stop);
            Assert.That(_m.Looper.Active, Is.False);
            float t0 = Time.realtimeSinceStartup;
            while (changes == 0 && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            ITakeLibrary lib = App.Current.Get<ITakeLibrary>();
            var list = lib.List();
            while (!list.IsCompleted) yield return null;
            Assert.That(list.Result.Count, Is.EqualTo(1));
            TakeInfo info = list.Result[0];
            Assert.That(info.name, Is.EqualTo("Loop 1 · 90 BPM · 1 layer"));
            Assert.That(info.count, Is.EqualTo(3), "its three events (the header and layer mark are not counted)");
            Assert.That(info.seconds, Is.EqualTo((float)(L / (double)_sr)).Within(1e-4f), "one pass long");
            var get = lib.Get(info.id);
            while (!get.IsCompleted) yield return null;
            TakeRecord rec = get.Result;
            Assert.That(rec, Is.Not.Null);
            // Recordings > Play: the menu's call. It loops (External), on the sample.
            _tap.Clear();
            IRecorder r = App.Current.Get<IRecorder>();
            Assert.IsTrue(r.PlayEvents(rec.events, rec.seconds));
            Assert.IsTrue(r.External);
            Assert.That(r.State, Is.EqualTo(RecorderState.Playing));
            long a2 = _m.Looper.Anchor;
            StepTo(a2 + 3 * L - S(0.2));
            List<long> kicks = Scheduled(AudioCall.Drum, (int)DrumKind.Kick);
            Assert.That(kicks, Is.EqualTo(new[] { a2, a2 + L, a2 + 2 * L }), "the saved loop repeats");
            r.Stop();
            Assert.IsFalse(r.External);
            Assert.That(_m.Looper.Active, Is.False);
        }

        [UnityTest]
        public IEnumerator StartingALessonStopsTheLoopAndSavesIt()
        {
            yield return Boot();
            LoopSession saved = null;
            _m.Looper.Save += x => saved = x;
            long a = RecordBeat();
            StepTo(a + _m.Looper.Session.PassSamples + S(0.3));
            Assert.That(_m.Looper.Active, Is.True);
            App.Current.Get<ITutorial>().Start("piano-scale");
            Assert.That(_m.Looper.Active, Is.False, "a lesson stops looping");
            Assert.That(saved, Is.Not.Null, "and the session is saved");
            Assert.That(App.Current.Get<IRecorder>().State, Is.EqualTo(RecorderState.Idle));
            // The other way round: LOOP during a lesson stops the lesson.
            Poke(DeckButton.Loop);
            Assert.That(App.Current.Get<ITutorial>().State, Is.EqualTo(TutorialState.Idle));
            Assert.That(_m.Looper.State, Is.EqualTo(LoopState.Waiting));
        }

        [UnityTest]
        public IEnumerator MovingTheInstrumentsStopsTheLoopAndSavesIt()
        {
            // "Move instruments" (u-arrange, merged 2026-11-01) silences everything: a loop session ends as STOP ends it.
            yield return Boot();
            LoopSession saved = null;
            _m.Looper.Save += x => saved = x;
            long a = RecordBeat();
            StepTo(a + _m.Looper.Session.PassSamples + S(0.3));
            Assert.That(_m.Looper.Active, Is.True);
            var arrange = App.Current.Get<IArrangeMode>();
            Assert.That(arrange, Is.Not.Null, "Layout's arrange mode");
            arrange.BeginArrange();
            Assert.IsTrue(arrange.Arranging);
            Assert.That(_m.Looper.Active, Is.False, "moving the instruments stops looping");
            Assert.That(saved, Is.Not.Null, "and the session is saved");
            Assert.That(App.Current.Get<IRecorder>().State, Is.EqualTo(RecorderState.Idle));
            int sent = _tap.Calls.Count;
            Step(72);
            foreach (var c in _tap.Calls.GetRange(sent, _tap.Calls.Count - sent))
                Assert.That(c.Method, Is.Not.EqualTo(AudioCall.Drum), "no loop hit while arranging");
            arrange.EndArrange();
            // A loop waiting for its first note ends too.
            Poke(DeckButton.Loop);
            Assert.That(_m.Looper.State, Is.EqualTo(LoopState.Waiting));
            arrange.BeginArrange();
            Assert.That(_m.Looper.Active, Is.False, "the waiting loop is cancelled");
            arrange.EndArrange();
        }

        [UnityTest]
        public IEnumerator HeldKeysAreReleasedAtTheEndOfThePassAndOnStop()
        {
            // Today's piano (the classic sound): a re-strike releases the old voice first, so the release is an engine call.
            yield return Boot();
            var engine = (AudioEngine)_engine;
            float t0 = Time.realtimeSinceStartup;
            while (engine.RenderSeconds < 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Assert.IsNull(engine.ActiveUnit(UnitId.Piano), "today's piano");
            Poke(DeckButton.Loop);
            double beat = _m.Looper.Session.BeatSamples;
            long a = _m.Looper.ClickStart + (long)Math.Round(4 * beat);
            object key = new object();
            StepTo(a);
            Piano.PressMidi(60, key, 0.6f);                   // held past the end of layer 1's pass
            long L = _m.Looper.Session.PassSamples;
            StepTo(a + L + S(0.5));
            Assert.That(_m.Looper.Layers, Is.EqualTo(1));
            Piano.ReleaseMidi(60, key);                       // the live key goes up during pass 1: not this layer's
            StepTo(a + 3 * L - S(0.2));
            int on = 0, off = 0;
            var strikes = new List<AudioTestTap.Call>();
            foreach (var c in _tap.Calls)
            {
                if (c.When <= 0) continue;
                if (c.Method == AudioCall.Piano && c.Index == 60) { on++; strikes.Add(c); }
                if (c.Method == AudioCall.Release) off++;
            }
            Assert.That(on, Is.EqualTo(2), "passes 1 and 2");
            Assert.That(S(strikes[0].When), Is.EqualTo(a + L), "pass 1 on its sample");
            Assert.That(S(strikes[1].When), Is.EqualTo(a + 2 * L), "pass 2 on its sample");
            Assert.That(off, Is.GreaterThanOrEqualTo(1), "the playback note's release is scheduled (the piano's own sustain lifts at the next phrase)");
            Assert.That(Piano.HeldKeyCount, Is.EqualTo(0).Or.EqualTo(1), "between the pass's note and its release at most one key down");
            Poke(DeckButton.Stop);
            Assert.That(Piano.HeldKeyCount, Is.EqualTo(0), "STOP lets every playback key up");
        }

        [UnityTest]
        public IEnumerator TheClassicThreeKeepEveryLoopedNoteOnItsSample()
        {
            // A loop with a piano note and a kick on today's synths: every pass's notes are scheduled for their samples,
            // with today's handles (no unit plays the piano or the drums).
            yield return Boot();
            var engine = (AudioEngine)_engine;
            float t0 = Time.realtimeSinceStartup;
            while (engine.RenderSeconds < 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            yield return null; yield return null;
            Assert.That(engine.RenderSeconds, Is.GreaterThanOrEqualTo(0), "today's piano buffers are rendered");
            Assert.IsNull(engine.ActiveUnit(UnitId.Piano), "today's piano");
            Assert.IsNull(engine.ActiveUnit(UnitId.Drums), "and today's drums");
            Poke(DeckButton.Loop);
            double beat = _m.Looper.Session.BeatSamples;
            long a = _m.Looper.ClickStart + (long)Math.Round(4 * beat);
            object key = new object();
            StepTo(a); Piano.PressMidi(60, key, 0.6f);
            StepTo(a + (long)Math.Round(0.5 * beat)); Piano.ReleaseMidi(60, key);
            StepTo(a + (long)Math.Round(2 * beat)); Drums.PlaybackHit("kick", 0.8f, Head);
            long L = _m.Looper.Session.PassSamples;
            StepTo(a + 3 * L + (long)Math.Round(3 * beat));
            List<AudioTestTap.Call> c4 = PianoCalls(60);
            Assert.That(c4.Count, Is.GreaterThanOrEqualTo(3), "passes 1, 2 and 3");
            for (int k = 1; k <= 3; k++)
            {
                Assert.That(S(c4[c4.Count - 4 + k].When), Is.EqualTo(a + k * L), $"pass {k}'s C4 on its sample");
                Assert.IsFalse(UnitHandles.IsUnit(c4[c4.Count - 4 + k].Handle), "today's piano");
                Assert.That(Scheduled(AudioCall.Drum, (int)DrumKind.Kick), Does.Contain(a + k * L + (long)Math.Round(2 * beat)), $"pass {k}'s kick on its sample");
            }
        }

        List<AudioTestTap.Call> PianoCalls(int midi)
        {
            var l = new List<AudioTestTap.Call>();
            foreach (var c in _tap.Calls) if (c.Method == AudioCall.Piano && c.Index == midi && c.When > 0) l.Add(c);
            return l;
        }

        [UnityTest]
        public IEnumerator TheFirstLoopPressAsksSunnyOnceAndTheClicksWaitForTheLine()
        {
            yield return Boot(tipSeen: false);
            yield return MusicRoom.Tests.PlayMode.Guide.GuideModuleTests.WaitForLines(_engine, AppLoopHost.TipLoopStart);
            Poke(DeckButton.Loop);
            int lines = 0;
            Step(20);
            foreach (var c in _tap.Calls) if (c.Method == AudioCall.VoiceLine && c.Line == AppLoopHost.TipLoopStart) lines++;
            Assert.That(lines, Is.EqualTo(1), "Sunny says the first-use line");
            float seconds = _engine.VoiceLineSeconds(AppLoopHost.TipLoopStart);
            Assert.That(_m.Looper.ClickStart - Now, Is.GreaterThan(S(seconds - 0.4)), "the clicks wait until it is said");
            Step((int)(6 / Dt));
            Assert.That(App.Current.Settings.GetString(GuideModule.KeyTipsSeen, ""), Does.Contain(AppLoopHost.TipLoopStart), "heard: persisted");
            Poke(DeckButton.Stop);
            _tap.Clear();
            Poke(DeckButton.Loop);
            Step(30);
            foreach (var c in _tap.Calls) Assert.That(c.Method == AudioCall.VoiceLine && c.Line == AppLoopHost.TipLoopStart, Is.False, "never again");
            Assert.That(_m.Looper.ClickStart - Now, Is.LessThan(S(Looper.LeadIn)), "and no wait");
        }

        [UnityTest]
        public IEnumerator TheLoopingFramesAllocateNothing()
        {
            yield return Boot();
            long a = RecordBeat();
            object key = new object();
            Piano.PressMidi(62, key, 0.6f);
            Step(4);
            Piano.ReleaseMidi(62, key);
            App.Current.Get<IHarpApi>()?.PlaybackPluck(7, 0.6f);
            long L = _m.Looper.Session.PassSamples;
            StepTo(a + L + S(0.2));                           // looping: every frame sends what is due, with ScheduleAt
            _m.Deck.Tick(App.Current.Input.HandPointers, Dt);
            // Warm up a whole pass (every event type through its instrument once), then measure two passes of frames.
            for (int i = 0; i < 400; i++) { _t += Dt; _m.Tick(FramePhase.Recorder, Dt); _m.Deck.Tick(App.Current.Input.HandPointers, Dt); }
            yield return null;
            _tap.Clear();
            _engine.TestTap = null;                           // the tap allocates by design
            float[] keep = null;
            Assert.That(() => { keep = new float[1024]; }, Is.AllocatingGCMemory(), "the GC.Alloc recorder works here");
            int calls = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 700; i++)
                {
                    _t += Dt;
                    _m.Tick(FramePhase.Recorder, Dt);        // the looper's Tick: clicks, sends, ScheduleAt, the instruments
                    _m.Deck.Tick(App.Current.Input.HandPointers, Dt);
                    calls++;
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(calls, Is.EqualTo(700));
            Assert.That(_m.Looper.Active, Is.True);
            _ = keep;
        }
    }
}
