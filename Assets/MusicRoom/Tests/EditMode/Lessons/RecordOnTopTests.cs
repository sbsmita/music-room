// Record on top (2026-11-17, the user: "i want to play drums for 4 seconds then piano for 30 seconds and then strings",
// "i want all to play once", "all instruments should play at same time"): the deck's REC while a take plays records on top
// of it (Recorder.RecordOnTop). On a scripted audio clock, with "instruments" that send back what they play (as the drums
// and the piano do), checked from outside:
//   - a 4 s drum take with 30 s of piano on top: one new take of about 30 s, both instruments' events in time order at
//     their times; the take's own sends are never recorded; the original is unchanged; the new take plays both together
//     once (one start, one end, no repeat) and announces both instruments (the playback show);
//   - while the take plays the state stays Playing (the show goes on for the played-back notes only); at its end Recording
//     is announced before the playback's end (Layout keeps what is in front, as for a REC); the recording goes on;
//   - a note played on a beat (heard InputLatency late) lands on that beat;
//   - STOP before the take's end keeps the rest of the original;
//   - on top of a take on top of a take (3 layers);
//   - on top of a take started from Recordings (PlayingTake: its name in the log; the deck's own take is replaced);
//   - REC without a playback (and REC while a loop plays) is exactly as before; nothing new played saves nothing;
//   - the deck: REC and PLAY both down (lamp red) while the take plays on, PLAY up at its end;
//   - no allocation per frame while recording on top;
//   - Sunny's tip_record_on_top (RecordOnTopTip): asked after a take's first end, retried while Sunny is busy, never
//     again once said or once the player recorded on top by themselves.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Lessons
{
    public class RecordOnTopTests
    {
        static readonly uint PianoBit = 1u << (int)InstrumentKind.Piano, DrumsBit = 1u << (int)InstrumentKind.Drums,
            HarpBit = 1u << (int)InstrumentKind.Harp;

        double _now;
        EventBus _bus;
        Recorder _rec;
        readonly List<(List<TakeEvent> ev, double s)> _saved = new List<(List<TakeEvent>, double)>();
        readonly List<TakeEvent> _sent = new List<TakeEvent>(4096);
        readonly List<string> _order = new List<string>();
        readonly List<RecordingPlaybackEvent> _playbacks = new List<RecordingPlaybackEvent>();
        int _livePiano, _sentDrumsDuringPlayback, _recorded, _playedNotes, _releases;
        readonly int[] _playedByKind = new int[32];

        [SetUp]
        public void SetUp()
        {
            _now = 100;
            _saved.Clear(); _sent.Clear(); _order.Clear(); _playbacks.Clear();
            _livePiano = _sentDrumsDuringPlayback = _recorded = _playedNotes = _releases = 0;
            Array.Clear(_playedByKind, 0, _playedByKind.Length);
            _bus = new EventBus();
            _rec = new Recorder(() => _now, _bus) { Roles = EventRoles.From(InstrumentRegistry.Discover()), InputLatency = 0 };
            // Each "instrument" plays what it is sent and, like the real ones, sends it back to the recorder.
            foreach (string t in new[] { TakeEventType.PianoOn, TakeEventType.PianoOff, TakeEventType.Drum, TakeEventType.Harp, TakeEventType.HarpDamp })
                _rec.SetTarget(t, e => { _sent.Add(e); _rec.Add(e); });
            _rec.OnTake += (ev, s) => _saved.Add((ev, s));
            _rec.SetReleaseAll(() => _releases++);
            _bus.Subscribe<RecorderStateEvent>(e => _order.Add("state " + e.State));
            _bus.Subscribe<RecordingPlaybackEvent>(e => { _playbacks.Add(e); _order.Add(e.Started ? "playback started" : "playback ended"); });
            _bus.Subscribe<RecorderEvent>(e =>
            {
                if (!e.DuringPlayback && e.Event.type == TakeEventType.PianoOn) _livePiano++;
                if (e.DuringPlayback && e.Event.type == TakeEventType.Drum) _sentDrumsDuringPlayback++;
            });
            _bus.Subscribe<RecordedNoteEvent>(e => _recorded++);
            _bus.Subscribe<PlaybackNoteEvent>(e => { if (e.Role == EventRole.Onset) { _playedNotes++; _playedByKind[(int)e.Kind]++; } });
        }

        [TearDown]
        public void TearDown() => PlayingTake.Clear();

        void Run(double until)
        {
            while (_now < until - 1e-9) { _now = Math.Min(until, _now + 1.0 / 72.0); _rec.Update(); }
        }

        static TakeEvent Snare => TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false);

        /// <summary>REC, a snare on every second for 4 s, STOP: the deck's take (and its saved copy).</summary>
        List<TakeEvent> RecordDrums4s()
        {
            _rec.Record();
            double t0 = _now;
            for (int b = 0; b < 4; b++) { Run(t0 + b); _rec.Add(Snare); }
            Run(t0 + 4.0);
            _rec.Stop();
            Assert.That(_saved.Count, Is.EqualTo(1));
            Assert.That(_saved[0].s, Is.EqualTo(4.0).Within(1e-9));
            return _saved[0].ev;
        }

        int Count(List<TakeEvent> l, string type) { int n = 0; foreach (var e in l) if (e.type == type) n++; return n; }

        static void AssertInOrder(List<TakeEvent> l, string what)
        {
            for (int i = 1; i < l.Count; i++) Assert.That(l[i].t, Is.GreaterThanOrEqualTo(l[i - 1].t), $"{what}: event {i} in time order");
        }

        static List<TakeEvent> Copy(List<TakeEvent> l) => new List<TakeEvent>(l);

        static void AssertSame(List<TakeEvent> a, List<TakeEvent> b, string what)
        {
            Assert.That(a.Count, Is.EqualTo(b.Count), what);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].type, Is.EqualTo(b[i].type), what);
                Assert.That(a[i].t, Is.EqualTo(b[i].t), what);
            }
        }

        // ------------------------------------------------------------------------------------------------ the user's song

        [Test]
        public void FourSecondsOfDrumsWithThirtySecondsOfPianoOnTopMakeOneTakeOfBothThatPlaysOnce()
        {
            List<TakeEvent> drums = RecordDrums4s();
            List<TakeEvent> drumsBefore = Copy(drums);
            List<TakeEvent> deckTake = _rec.Take;
            _rec.DeckTakeName = "Take 1 · 0:04";
            Assert.IsTrue(_rec.Play());
            double p0 = _now;
            Run(p0 + 1.5);
            Assert.IsTrue(_rec.CanRecordOnTop);
            _order.Clear();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Lessons\] record on top of 'Take 1 · 0:04' at 1\.5 s$"));
            Assert.IsTrue(_rec.RecordOnTop());
            Assert.IsTrue(_rec.RecordingOnTop);
            Assert.IsTrue(((IRecorder)_rec).RecordingOnTop, "IRecorder says so too (Sunny's show lines wait)");
            Assert.IsFalse(_rec.CanRecordOnTop, "once");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing), "the take plays on (as a looper overdub)");
            Assert.IsTrue(_rec.PlaybackOn);
            Assert.That(_order, Is.Empty, "nothing announced: the playback goes on");
            _recorded = 0;

            // 30 s of piano: a note every 0.5 s from the take's 2nd second, each held 0.25 s.
            int notes = 0;
            bool sawEnd = false;
            for (double T = 2.0; T < 30.0 - 1e-9; T += 0.5)
            {
                Run(p0 + T);
                if (T > 4.1 && !sawEnd)
                {
                    sawEnd = true;
                    Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording), "past the take's end the recording goes on");
                    Assert.IsTrue(_rec.RecordingOnTop);
                    Assert.IsFalse(_rec.PlaybackOn, "the take's playback ended");
                    Assert.That(_order, Is.EqualTo(new[] { "state Recording", "playback ended" }), "Recording first: Layout keeps the instruments");
                    Assert.That(_releases, Is.EqualTo(0), "nothing let go at the take's end: the player's ringing notes ring on");
                }
                _rec.Add(TakeEvent.PianoOn(60 + notes % 12, 0.7f));
                Run(p0 + T + 0.25);
                _rec.Add(TakeEvent.PianoOff(60 + notes % 12));
                notes++;
            }
            Assert.That(notes, Is.EqualTo(56));
            Assert.That(_playedByKind[(int)InstrumentKind.Drums], Is.EqualTo(4), "the take's four snares were played back");
            Assert.That(_playedByKind[(int)InstrumentKind.Piano], Is.EqualTo(0), "the new notes are not a playback (no show for them)");
            Assert.That(_recorded, Is.EqualTo(112), "they are recorded notes");
            Assert.That(_livePiano, Is.EqualTo(56), "live, never DuringPlayback");
            Assert.That(_sentDrumsDuringPlayback, Is.EqualTo(4), "what the drums sent back is playback");

            Run(p0 + 30.2);
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Lessons\] record on top of 'Take 1 · 0:04' saved: 116 events \(112 new\), 30\.2 s, piano\+drums$"));
            _rec.Stop();
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.IsFalse(_rec.RecordingOnTop);
            Assert.That(_releases, Is.EqualTo(1), "what the take held is let go at STOP, where the new take ends");
            Assert.That(_saved.Count, Is.EqualTo(2), "a NEW take was saved");
            (List<TakeEvent> both, double seconds) = _saved[1];
            Assert.That(seconds, Is.EqualTo(30.2).Within(1e-6), "as long as the new part");
            Assert.That(both.Count, Is.EqualTo(116));
            AssertInOrder(both, "the new take");
            Assert.That(Count(both, TakeEventType.Drum), Is.EqualTo(4), "the take's own sends were never recorded");
            Assert.That(Count(both, TakeEventType.PianoOn), Is.EqualTo(56));
            var drumTimes = new List<float>();
            var pianoTimes = new List<float>();
            foreach (TakeEvent e in both)
            {
                if (e.type == TakeEventType.Drum) drumTimes.Add(e.t);
                if (e.type == TakeEventType.PianoOn) pianoTimes.Add(e.t);
            }
            Assert.That(drumTimes, Is.EqualTo(new[] { 0f, 1f, 2f, 3f }), "the drums where they were");
            for (int i = 0; i < pianoTimes.Count; i++) Assert.That(pianoTimes[i], Is.EqualTo(2.0f + 0.5f * i).Within(1e-4f), $"piano note {i} on the take's clock");
            Assert.That(Recorder.KindsOf(both, _rec.Roles), Is.EqualTo(PianoBit | DrumsBit));
            AssertSame(drums, drumsBefore, "the original saved take is unchanged");
            Assert.That(deckTake.Count, Is.EqualTo(4), "and so is the deck's old take list");
            Assert.That(_rec.Take.Count, Is.EqualTo(116), "the deck's take is the new one now");
            Assert.That(_rec.TakeSeconds, Is.EqualTo(30.2).Within(1e-6));

            // PLAY: all of it together, once.
            _sent.Clear(); _playbacks.Clear(); _playedNotes = 0; Array.Clear(_playedByKind, 0, _playedByKind.Length);
            Assert.IsTrue(_rec.Play());
            double p1 = _now;
            Run(p1 + 1.0);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing));
            Run(p1 + 30.5);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "it ends after its 30.2 s");
            Run(p1 + 70);
            Assert.That(_playbacks.Count, Is.EqualTo(2), "one start, one end: played once, no repeat");
            Assert.IsTrue(_playbacks[0].Started);
            Assert.IsFalse(_playbacks[0].Loop);
            Assert.That(_playbacks[0].Kinds, Is.EqualTo(PianoBit | DrumsBit), "both instruments (the stage and the show)");
            Assert.That(_playedByKind[(int)InstrumentKind.Drums], Is.EqualTo(4));
            Assert.That(_playedByKind[(int)InstrumentKind.Piano], Is.EqualTo(56), "both instruments played back (the show for both)");
            Assert.That(Count(_sent, TakeEventType.PianoOff), Is.EqualTo(56));
            Assert.That(_rec.TakeEnds, Is.EqualTo(1), "the first take's end went on recording (not counted); the new take ended");
        }

        [Test]
        public void ANotePlayedOnTheBeatThePlayerHeardLandsOnThatBeat()
        {
            _rec.InputLatency = 0.03;   // what the player hears is this late
            RecordDrums4s();
            Assert.IsTrue(_rec.Play());
            double p0 = _now;
            Run(p0 + 0.5);
            LogAssert.Expect(LogType.Log, new Regex(@"record on top of 'the deck's take' at 0\.5 s"));
            Assert.IsTrue(_rec.RecordOnTop());
            // The snare at 2.0 is heard 30 ms after its time: the player plays with it.
            Run(p0 + 2.0 + 0.03);
            _rec.Add(TakeEvent.PianoOn(60, 0.8f));
            // An instrument that stamps its event earlier (the hand drums' relabel): stamped at the beat heard at 3.0.
            Run(p0 + 3.08);
            _rec.Add(TakeEvent.PianoOn(64, 0.8f), p0 + 3.0 + 0.03);
            Run(p0 + 4.5);
            LogAssert.Expect(LogType.Log, new Regex(@"saved: 6 events \(2 new\), 4\.5 s"));
            _rec.Stop();
            List<TakeEvent> both = _saved[1].ev;
            AssertInOrder(both, "merged");
            var on = both.FindAll(e => e.type == TakeEventType.PianoOn);
            Assert.That(on[0].t, Is.EqualTo(2.0f).Within(1e-4f), "on the beat");
            Assert.That(on[1].t, Is.EqualTo(3.0f).Within(1e-4f), "on the beat");
            Assert.That(both.IndexOf(on[0]), Is.EqualTo(3), "right after the snare it was played with (same time: the take's first)");
        }

        [Test]
        public void StopBeforeTheTakeEndsKeepsTheRestOfTheOriginal()
        {
            RecordDrums4s();
            Assert.IsTrue(_rec.Play());
            double p0 = _now;
            Run(p0 + 0.5);
            LogAssert.Expect(LogType.Log, new Regex("record on top of"));
            Assert.IsTrue(_rec.RecordOnTop());
            Run(p0 + 1.2);
            _rec.Add(TakeEvent.PianoOn(60, 0.8f));
            Run(p0 + 2.0);
            _order.Clear();
            LogAssert.Expect(LogType.Log, new Regex(@"saved: 5 events \(1 new\), 4\.0 s"));
            _rec.Stop();
            Assert.That(_order, Is.EqualTo(new[] { "state Idle", "playback ended" }), "as a STOP of a playback");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.IsFalse(_rec.PlaybackOn);
            (List<TakeEvent> both, double seconds) = _saved[1];
            Assert.That(seconds, Is.EqualTo(4.0).Within(1e-9), "the original's length");
            Assert.That(Count(both, TakeEventType.Drum), Is.EqualTo(4), "the snares after the STOP are kept");
            Assert.That(both[both.Count - 1].t, Is.EqualTo(3f));
            _sent.Clear();
            Assert.IsTrue(_rec.Play());
            Run(_now + 5);
            Assert.That(_sent.Count, Is.EqualTo(5), "all of it plays");
        }

        [Test]
        public void OnTopOfATakeOnTopOfATakeMakesThreeLayers()
        {
            List<TakeEvent> a = RecordDrums4s();
            List<TakeEvent> aBefore = Copy(a);
            Assert.IsTrue(_rec.Play());
            double p0 = _now;
            Run(p0 + 0.2);
            LogAssert.Expect(LogType.Log, new Regex("record on top of"));
            Assert.IsTrue(_rec.RecordOnTop());
            for (double T = 1.25; T < 5.5; T += 1.0) { Run(p0 + T); _rec.Add(TakeEvent.PianoOn(67, 0.6f)); }
            Run(p0 + 6.0);
            LogAssert.Expect(LogType.Log, new Regex(@"saved: 9 events \(5 new\), 6\.0 s, piano\+drums"));
            _rec.Stop();
            List<TakeEvent> b = _saved[1].ev;
            List<TakeEvent> bBefore = Copy(b);
            _rec.DeckTakeName = "Take 2 · 0:06";

            Assert.IsTrue(_rec.Play());
            double p1 = _now;
            Run(p1 + 0.1);
            LogAssert.Expect(LogType.Log, new Regex(@"record on top of 'Take 2 · 0:06' at 0\.1 s"));
            Assert.IsTrue(_rec.RecordOnTop());
            for (double T = 0.5; T < 8.0; T += 1.0) { Run(p1 + T); _rec.Add(TakeEvent.Harp(5, 0.7f, 0.5f, 0.3f, "through")); }
            Run(p1 + 8.0);
            LogAssert.Expect(LogType.Log, new Regex(@"saved: 17 events \(8 new\), 8\.0 s, piano\+drums\+harp"));
            _rec.Stop();
            (List<TakeEvent> c, double seconds) = _saved[2];
            Assert.That(seconds, Is.EqualTo(8.0).Within(1e-9));
            AssertInOrder(c, "three layers");
            Assert.That(Count(c, TakeEventType.Drum), Is.EqualTo(4));
            Assert.That(Count(c, TakeEventType.PianoOn), Is.EqualTo(5));
            Assert.That(Count(c, TakeEventType.Harp), Is.EqualTo(8));
            Assert.That(Recorder.KindsOf(c, _rec.Roles), Is.EqualTo(PianoBit | DrumsBit | HarpBit));
            AssertSame(a, aBefore, "the first take is unchanged");
            AssertSame(b, bBefore, "the second take is unchanged");

            _sent.Clear(); _playbacks.Clear();
            Assert.IsTrue(_rec.Play());
            Run(_now + 20);
            Assert.That(_sent.Count, Is.EqualTo(17), "all three layers play together");
            Assert.That(_playbacks.Count, Is.EqualTo(2), "once");
            Assert.That(_playbacks[0].Kinds, Is.EqualTo(PianoBit | DrumsBit | HarpBit));
        }

        [Test]
        public void OnTopOfATakeFromRecordings()
        {
            // The deck's own take: one piano note.
            _rec.Record();
            _rec.Add(TakeEvent.PianoOn(72, 0.5f));
            Run(_now + 1);
            _rec.Stop();
            List<TakeEvent> own = _rec.Take;
            // Recordings > a take > Play (what the Menu does): PlayEvents, then PlayingTake.Note.
            var menuTake = new List<TakeEvent>();
            for (int b = 0; b < 4; b++) { TakeEvent e = Snare; e.t = b; menuTake.Add(e); }
            List<TakeEvent> menuBefore = Copy(menuTake);
            Assert.IsTrue(_rec.PlayEvents(menuTake, 4.0));
            PlayingTake.Note("take-7", menuTake);
            _rec.NameOfId = id => id == "take-7" ? "Take 7 · 0:04" : null;
            Assert.IsTrue(_rec.External);
            double p0 = _now;
            Run(p0 + 1.0);
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Lessons\] record on top of 'Take 7 · 0:04' at 1\.0 s$"));
            Assert.IsTrue(_rec.RecordOnTop());
            Run(p0 + 1.5);
            _rec.Add(TakeEvent.PianoOn(60, 0.8f));
            Run(p0 + 5.0);
            _rec.Add(TakeEvent.PianoOn(62, 0.8f));
            Run(p0 + 5.5);
            LogAssert.Expect(LogType.Log, new Regex(@"record on top of 'Take 7 · 0:04' saved: 6 events \(2 new\), 5\.5 s"));
            _rec.Stop();
            (List<TakeEvent> both, double seconds) = _saved[1];
            Assert.That(seconds, Is.EqualTo(5.5).Within(1e-6));
            Assert.That(Count(both, TakeEventType.Drum), Is.EqualTo(4));
            Assert.That(Count(both, TakeEventType.PianoOn), Is.EqualTo(2), "the deck's own take is not in it");
            AssertSame(menuTake, menuBefore, "the recording from Recordings is unchanged");
            Assert.That(own.Count, Is.EqualTo(1));
            Assert.IsFalse(_rec.External, "the new take is the deck's (as after a REC)");
            Assert.That(_rec.Take.Count, Is.EqualTo(6));
        }

        // ------------------------------------------------------------------------------------------------ as before

        [Test]
        public void RecWithoutAPlaybackIsAsBeforeAndNothingNewSavesNothing()
        {
            Assert.IsFalse(_rec.CanRecordOnTop, "nothing plays");
            Assert.IsFalse(_rec.RecordOnTop());
            var table = new GameObject("TableRig");
            var deck = new TapeDeck(_rec, new MaterialLibrary(), table.transform, TapeDeck.TableLocalPosition);
            try
            {
                deck.Press(DeckButton.Rec);
                Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording), "REC with no playback: a new take as ever");
                Assert.IsFalse(_rec.RecordingOnTop);
                _rec.Add(Snare);
                Run(_now + 2);
                deck.Press(DeckButton.Stop);
                Assert.That(_saved.Count, Is.EqualTo(1));
                Assert.That(_saved[0].ev.Count, Is.EqualTo(1));
                Assert.That(_saved[0].s, Is.EqualTo(2.0).Within(1e-9));

                // PLAY then REC: on top of it; REC and PLAY both down, the lamp red.
                deck.Press(DeckButton.Play);
                Run(_now + 0.5);
                LogAssert.Expect(LogType.Log, new Regex("record on top of"));
                deck.Press(DeckButton.Rec);
                Assert.IsTrue(_rec.RecordingOnTop);
                Assert.IsTrue(_rec.OnTopPlaying);
                deck.Tick(new List<Pointer>(), 0.5f);
                Assert.That(deck.PegDepth(DeckButton.Rec), Is.EqualTo(1f), "REC down: recording");
                Assert.That(deck.PegDepth(DeckButton.Play), Is.EqualTo(1f), "PLAY down: the take plays on");
                Assert.That(deck.LampColor.r, Is.EqualTo(TapeDeck.LampRecording.r).Within(0.01f), "the lamp red");
                Assert.That(deck.LampColor.g, Is.EqualTo(TapeDeck.LampRecording.g).Within(0.01f));
                Run(_now + 2);   // past the take's end
                deck.Tick(new List<Pointer>(), 0.5f);
                Assert.That(deck.PegDepth(DeckButton.Play), Is.EqualTo(0f), "PLAY up: the take ended");
                Assert.That(deck.PegDepth(DeckButton.Rec), Is.EqualTo(1f), "REC still down");
                // Nothing new was played: STOP saves nothing and the deck keeps its take.
                LogAssert.Expect(LogType.Log, new Regex("nothing new was played, nothing saved"));
                deck.Press(DeckButton.Stop);
                Assert.That(_saved.Count, Is.EqualTo(1));
                Assert.That(_rec.Take.Count, Is.EqualTo(1));
                Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle));
                deck.Tick(new List<Pointer>(), 0.5f);
                Assert.That(deck.PegDepth(DeckButton.Rec), Is.EqualTo(0f));
            }
            finally
            {
                deck.Destroy();
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void PlayOrRecDuringItEndsItSavedAndALoopIsNeverRecordedOnTop()
        {
            RecordDrums4s();
            Assert.IsTrue(_rec.Play());
            Run(_now + 1);
            LogAssert.Expect(LogType.Log, new Regex("record on top of"));
            Assert.IsTrue(_rec.RecordOnTop());
            _rec.Add(TakeEvent.PianoOn(60, 0.8f));
            // PLAY: the recording on top ends (saved) and the new take plays.
            LogAssert.Expect(LogType.Log, new Regex(@"saved: 5 events \(1 new\)"));
            Assert.IsTrue(_rec.Play());
            Assert.IsFalse(_rec.RecordingOnTop);
            Assert.That(_saved.Count, Is.EqualTo(2));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing));
            Assert.That(_rec.Take.Count, Is.EqualTo(5));
            // REC() (the plain recorder call) during a playback is today's: it stops the playback and starts a new take.
            _rec.Record();
            Assert.IsFalse(_rec.RecordingOnTop);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording));
            _rec.Stop();

            // A loop playing: REC arms a layer as ever, never on top.
            var host = new FakeLoopHost { T = _now };
            var bus = new EventBus();
            var rec = new Recorder(() => host.T, bus) { Roles = EventRoles.From(InstrumentRegistry.Discover()) };
            var looper = new Looper(host) { InputLatency = 0, Tidy = false };
            rec.Looper = looper;
            rec.SetTarget(TakeEventType.PianoOn, e => { });
            host.OnDispatch = e => rec.Dispatch(e);
            var s = new LoopSession(90, 2, host.Sr);
            var layer = new LoopLayer();
            layer.Insert(0, TakeEvent.PianoOn(60, 0.8f));
            s.Layers.Add(layer);
            Assert.IsTrue(rec.PlayEvents(LoopFormat.Encode(s), 0));
            Assert.IsFalse(rec.CanRecordOnTop, "a loop is never recorded on top (it has its own overdub)");
            Assert.IsFalse(rec.RecordOnTop());
            rec.Record();
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Armed).Or.EqualTo(LoopStatus.Overdubbing), "REC arms the loop's next layer");
            Assert.IsFalse(rec.RecordingOnTop);
            rec.Stop();
        }

        // ------------------------------------------------------------------------------------------------ the frame

        [Test]
        public void RecordingOnTopAllocatesNothingPerFrame()
        {
            // A long take of drums so the playback goes on through the frames measured.
            var take = new List<TakeEvent>();
            for (int i = 0; i < 400; i++) { TakeEvent e = Snare; e.t = i * 0.05f; take.Add(e); }
            Assert.IsTrue(_rec.PlayEvents(take, 20));
            double p0 = _now;
            Run(p0 + 0.5);
            LogAssert.Expect(LogType.Log, new Regex("record on top of"));
            Assert.IsTrue(_rec.RecordOnTop());
            // Warm the paths once (first publishes of each event type).
            _now += 1.0 / 72.0; _rec.Update(); _rec.Add(TakeEvent.PianoOn(60, 0.7f)); _rec.Add(TakeEvent.PianoOff(60));
            int sentBefore = _sent.Count;
            Assert.That(() =>
            {
                for (int f = 0; f < 200; f++)
                {
                    _now += 1.0 / 72.0;
                    _rec.Update();
                    if ((f & 7) == 0) _rec.Add(TakeEvent.PianoOn(60 + (f & 15), 0.7f));
                    if ((f & 7) == 4) _rec.Add(TakeEvent.PianoOff(60 + (f & 15)));
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.IsTrue(_rec.OnTopPlaying, "the take still played");
            Assert.That(_sent.Count - sentBefore, Is.GreaterThan(40), "and sent its notes meanwhile");
            _rec.Stop();
        }

        // ------------------------------------------------------------------------------------------------ Sunny's line

        sealed class FakeGuide : IGuide
        {
            public float Seconds;
            public bool Seen;
            public readonly List<string> Asked = new List<string>();
            public bool Present => false;
            public bool Done => true;
            public void Start() { }
            public void Dismiss() { }
            public void Toggle() { }
            public float Tip(string id) { Asked.Add(id); if (Seconds > 0f) Seen = true; return Seconds; }
            public bool TipSeen(string id) => Seen;
        }

        void Frames(RecordOnTopTip tip, float seconds)
        {
            int n = Mathf.RoundToInt(seconds * 72f);
            for (int i = 0; i < n; i++) { _now += 1.0 / 72.0; _rec.Update(); tip.Tick(1f / 72f); }
        }

        [Test]
        public void SunnyMentionsItAfterATakesFirstEndAndRetriesWhileBusy()
        {
            var guide = new FakeGuide();
            var tip = new RecordOnTopTip(_rec, () => guide);
            RecordDrums4s();
            Assert.IsTrue(_rec.Play());
            Frames(tip, 3.9f);
            Assert.That(guide.Asked, Is.Empty, "not while it plays");
            Frames(tip, RecordOnTopTip.AskAfter - 0.3f);
            Assert.That(guide.Asked, Is.Empty, "a moment after the end");
            Frames(tip, 0.5f);
            Assert.That(guide.Asked, Is.EqualTo(new[] { RecordOnTopTip.TipId }), "asked once the take played to its end");
            Frames(tip, RecordOnTopTip.AskEvery + 0.1f);
            Assert.That(guide.Asked.Count, Is.EqualTo(2), "Sunny was busy: asked again");
            guide.Seconds = 3f;
            Frames(tip, RecordOnTopTip.AskEvery + 0.1f);
            Assert.That(guide.Asked.Count, Is.EqualTo(3));
            Assert.IsTrue(tip.Done, "said");
            Assert.IsTrue(_rec.Play());
            Frames(tip, 12f);
            Assert.That(guide.Asked.Count, Is.EqualTo(3), "never again");
        }

        [Test]
        public void SunnyWaitsWhileThePlayerMovesOnAndNeverAsksOnceTheyRecordedOnTop()
        {
            var guide = new FakeGuide();
            var tip = new RecordOnTopTip(_rec, () => guide);
            RecordDrums4s();
            Assert.IsTrue(_rec.Play());
            Frames(tip, 4.5f);
            _rec.Record();   // the player moved on before Sunny's moment
            _rec.Add(Snare);
            Frames(tip, 5f);
            Assert.That(guide.Asked, Is.Empty, "never over a recording");
            _rec.Stop();
            Frames(tip, 5f);
            Assert.That(guide.Asked, Is.Empty, "that end gave up; the next one asks");
            // The next take: recorded on top of before its end; that end is not a take's end, and they found it themselves.
            Assert.IsTrue(_rec.Play());
            Frames(tip, 0.5f);
            LogAssert.Expect(LogType.Log, new Regex("record on top of"));
            Assert.IsTrue(_rec.RecordOnTop());
            Frames(tip, 6f);
            Assert.That(_rec.TakeEnds, Is.EqualTo(1), "only the first take's end: one recorded on top past its end did not just end");
            LogAssert.Expect(LogType.Log, new Regex("nothing new was played"));
            _rec.Stop();
            Assert.IsTrue(tip.Done, "they know it: never asked");
            Assert.IsTrue(_rec.Play());
            Frames(tip, 10f);
            Assert.That(guide.Asked, Is.Empty);
        }
    }
}
