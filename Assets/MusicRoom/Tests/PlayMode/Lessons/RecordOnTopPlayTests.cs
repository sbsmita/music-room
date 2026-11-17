// Record on top in the booted app (every real module, simulated headset, manual frames at 72 Hz, a scripted audio clock):
// the user's "drums for 4 seconds then piano ... all instruments should play at same time", from One alone drums:
//   - a 4 s drum take is recorded and saved; the deck's PLAY plays it; the deck's REC while it plays records on top (the
//     log names the take), REC and PLAY both down; Sunny's show lines wait (PlaybackTips) while the take's notes still play
//     back with the show (ghost hands on, the room's listen-back for the drums only, never for the new notes);
//   - at the take's end the recording goes on (PLAY comes up), the instruments in front stay (as for a REC);
//   - STOP saves a NEW take in Recordings ("Take 2 · 0:10"): the drums and the piano merged, about 10 s; Take 1 unchanged;
//   - the deck's PLAY of it brings both instruments in front (the stage), plays both together once with the show for
//     both (the room's listen-back levels for piano and drums), and ends;
//   - Sunny says tip_record_on_top a moment after a take first plays to its end (the show's own lines heard before).
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using MusicRoom.Lessons.Ghost;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode
{
    public class RecordOnTopPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        double _audio = 10.0;
        readonly List<PlaybackNoteEvent> _played = new List<PlaybackNoteEvent>();
        readonly List<RecordingPlaybackEvent> _playbacks = new List<RecordingPlaybackEvent>();
        int _recordedNotes;

        Recorder Rec => (Recorder)_app.Get<IRecorder>();
        LessonsModule Lessons => _app.Get<LessonsModule>();
        TapeDeck Deck => Lessons.Deck;

        IEnumerator Boot(string tipsSeen = null)
        {
            _audio = 10.0;
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            if (tipsSeen != null) _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, tipsSeen);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Drums));
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(3);
            Settle();
            Assert.IsTrue(_layout.FirstArrangeDone);
            Lessons.Looper.InputLatency = 0;   // exact times (the device value is a tuning knob)
            _app.Events.Subscribe<PlaybackNoteEvent>(e => { if (e.Role == EventRole.Onset) _played.Add(e); });
            _app.Events.Subscribe<RecordingPlaybackEvent>(e => _playbacks.Add(e));
            _app.Events.Subscribe<RecordedNoteEvent>(e => _recordedNotes++);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            _layout = null;
            _played.Clear();
            _playbacks.Clear();
            PlayingTake.Clear();
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++) { _audio += Dt; _boot.Step(Dt); }
        }

        void Seconds(float s) => Step(Mathf.Max(1, Mathf.RoundToInt(s / Dt)));

        void Settle()
        {
            for (int i = 0; i < 600 && (_layout.AnyMoving || _layout.Settling || _layout.IsTurning); i++) Step();
            Step(3);
        }

        int Played(InstrumentKind k) => _played.Count(e => e.Kind == k);

        IEnumerator UntilSaved(int takes)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Lessons.Takes.List().Result.Count < takes && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            Assert.That(Lessons.Takes.List().Result.Count, Is.EqualTo(takes), "saved in Recordings");
        }

        static TakeEvent Snare => TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false);

        [UnityTest]
        public IEnumerator DrumsThenPianoOnTopPlayTogetherOnceWithTheShow()
        {
            yield return Boot();
            uint pianoBit = 1u << (int)InstrumentKind.Piano, drumsBit = 1u << (int)InstrumentKind.Drums;

            // 1. A 4 s drum take (the kit's own recorder call, a snare a second).
            Deck.Press(DeckButton.Rec);
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Recording));
            for (int b = 0; b < 4; b++) { Rec.Add(Snare); Seconds(1f); }
            Deck.Press(DeckButton.Stop);
            yield return UntilSaved(1);
            TakeInfo first = Lessons.Takes.List().Result[0];
            StringAssert.StartsWith("Take ", first.name);
            Assert.That(first.count, Is.EqualTo(4));
            Assert.That(Rec.DeckTakeName, Is.EqualTo(first.name));

            // 2. PLAY, then REC 1 s in: recording on top.
            _played.Clear(); _playbacks.Clear();
            Deck.Press(DeckButton.Play);
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Playing));
            Seconds(1f);
            IGuide guide = _app.Get<IGuide>();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Lessons\] record on top of '" + Regex.Escape(first.name) + @"' at 1\.0 s$"));
            Deck.Press(DeckButton.Rec);
            Assert.IsTrue(Rec.RecordingOnTop, "REC while the take plays records on top");
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Playing), "the take plays on");
            foreach (string id in PlaybackTips.Order) Assert.IsFalse(PlaybackTips.MayAsk(guide, id), id + ": Sunny's show lines wait");
            Assert.IsFalse(PlaybackTips.MayAsk(guide, PlaybackTips.StageId));
            Step(20);
            Assert.That(Deck.PegDepth(DeckButton.Rec), Is.GreaterThan(0.95f), "REC down");
            Assert.That(Deck.PegDepth(DeckButton.Play), Is.GreaterThan(0.95f), "PLAY down");
            Assert.IsTrue(GhostHandsModule.Of(_app).Playing, "the ghost hands play the take on");

            // 3. Piano on top: a note every 0.5 s from 1.5 s to 9.5 s (the piano's own recorder call), past the take's end.
            _recordedNotes = 0;
            int notes = 0;
            bool checkedEnd = false;
            for (float T = 1.5f; T < 9.7f; T += 0.5f)
            {
                Seconds(0.25f);
                Rec.Add(TakeEvent.PianoOn(60 + notes % 12, 0.7f));
                Seconds(0.25f);
                Rec.Add(TakeEvent.PianoOff(60 + notes % 12));
                notes++;
                if (T > 5.2f && !checkedEnd)
                {
                    checkedEnd = true;
                    Assert.That(Rec.State, Is.EqualTo(RecorderState.Recording), "past the take's end the recording goes on");
                    Assert.IsTrue(Rec.RecordingOnTop);
                    Assert.That(_playbacks.Count(e => !e.Started), Is.EqualTo(1), "the take's playback ended");
                    Assert.That(Deck.PegDepth(DeckButton.Play), Is.LessThan(0.05f), "PLAY up");
                    Assert.That(Deck.PegDepth(DeckButton.Rec), Is.GreaterThan(0.95f), "REC still down");
                    Assert.IsFalse(_layout.PlaybackStaged, "nothing was staged (the drums were in front); nothing moves");
                }
            }
            Assert.IsTrue(checkedEnd);
            Assert.That(Played(InstrumentKind.Drums), Is.EqualTo(4), "the take's four snares played back (three after REC)");
            Assert.That(Played(InstrumentKind.Piano), Is.EqualTo(0), "the new notes never play back as a show");
            Assert.That(_recordedNotes, Is.EqualTo(2 * notes), "they are recorded notes");
            Assert.That(Rec.Take.Count, Is.EqualTo(4), "the deck's take is untouched while recording");

            // 4. STOP: a new take, both merged.
            Deck.Press(DeckButton.Stop);
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.IsFalse(Rec.RecordingOnTop);
            yield return UntilSaved(2);
            List<TakeInfo> list = Lessons.Takes.List().Result;
            TakeInfo second = list.First(t => t.id != first.id);
            Assert.That(second.name, Does.EndWith("0:10"), "about 10 s long");
            Assert.That(second.seconds, Is.EqualTo(10f).Within(0.3f));
            Assert.That(second.count, Is.EqualTo(4 + 2 * notes));
            var task = Lessons.Takes.Get(second.id);
            float g0 = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - g0 < 5f) yield return null;
            List<TakeEvent> both = task.Result.events;
            for (int i = 1; i < both.Count; i++) Assert.That(both[i].t, Is.GreaterThanOrEqualTo(both[i - 1].t), "in time order");
            Assert.That(both.Where(e => e.type == TakeEventType.Drum).Select(e => e.t), Is.EqualTo(new[] { 0f, 1f, 2f, 3f }).Within(0.02f));
            Assert.That(both.Count(e => e.type == TakeEventType.PianoOn), Is.EqualTo(notes));
            var firstTask = Lessons.Takes.Get(first.id);
            g0 = Time.realtimeSinceStartup;
            while (!firstTask.IsCompleted && Time.realtimeSinceStartup - g0 < 5f) yield return null;
            Assert.That(firstTask.Result.events.Count, Is.EqualTo(4), "Take 1 is unchanged");

            // 5. PLAY the new take: both instruments in front, both play together once, the show for both.
            _played.Clear(); _playbacks.Clear();
            Deck.Press(DeckButton.Play);
            Assert.That(_playbacks.Count, Is.EqualTo(1));
            Assert.That(_playbacks[0].Kinds, Is.EqualTo(pianoBit | drumsBit));
            Assert.IsTrue(_layout.PlaybackStaged, "the stage brings the piano to the drums");
            Assert.That(_layout.StagedKinds, Is.EqualTo(pianoBit | drumsBit));
            ListenBackPulse pulse = (_app.Get<IRoom>() as RoomModule).ListenBack;
            float pianoLevel = 0f, drumsLevel = 0f;
            int n = Mathf.RoundToInt(10.5f / Dt);
            for (int i = 0; i < n; i++)
            {
                Step();
                pianoLevel = Mathf.Max(pianoLevel, pulse.PlaybackModel.Level(InstrumentKind.Piano));
                drumsLevel = Mathf.Max(drumsLevel, pulse.PlaybackModel.Level(InstrumentKind.Drums));
            }
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Idle), "it ended");
            Seconds(6f);
            Assert.That(_playbacks.Count(e => e.Started), Is.EqualTo(1), "played once, no repeat");
            Assert.That(_playbacks.Count(e => !e.Started), Is.EqualTo(1));
            Assert.That(Played(InstrumentKind.Drums), Is.EqualTo(4), "the drums played back");
            Assert.That(Played(InstrumentKind.Piano), Is.EqualTo(notes), "and the piano, together");
            Assert.That(pianoLevel, Is.GreaterThan(0.05f), "the room listened back to the piano");
            Assert.That(drumsLevel, Is.GreaterThan(0.05f), "and to the drums");
            Debug.Log($"[OnTop] new take {second.name}: {both.Count} events; listen-back peaks piano {pianoLevel:F2}, drums {drumsLevel:F2}");
        }

        [UnityTest]
        public IEnumerator SunnySaysItAMomentAfterATakeFirstPlaysToItsEnd()
        {
            yield return Boot(string.Join(",", PlaybackTips.Order) + "," + PlaybackTips.StageId);
            Assert.IsTrue(MusicRoom.Guide.GuideLines.Has(RecordOnTopTip.TipId), "the line exists");
            Deck.Press(DeckButton.Rec);
            for (int b = 0; b < 3; b++) { Rec.Add(Snare); Seconds(0.5f); }
            Deck.Press(DeckButton.Stop);
            Seconds(3f);
            Deck.Press(DeckButton.Play);
            Seconds(1.4f);
            Assert.That(Lessons.OnTopTip.Asks, Is.EqualTo(0), "not while it plays");
            Seconds(1f);
            Assert.That(Lessons.OnTopTip.Asks, Is.EqualTo(0), "not at once after its end");
            Seconds(RecordOnTopTip.AskAfter);
            Assert.That(Lessons.OnTopTip.Asks, Is.GreaterThanOrEqualTo(1), "asked a moment after the end");
            Assert.IsTrue(Lessons.OnTopTip.Done, "and said");
            Seconds(0.3f);
            Assert.IsTrue(_app.Get<IGuide>().Present, "Sunny comes to say it");
        }
    }
}
