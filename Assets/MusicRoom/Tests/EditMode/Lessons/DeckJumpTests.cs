// The deck under a resting fingertip (2026-11-16 headset log: the quick show's saved loop "ended" 1 s after PLAY, twice).
// A playback brings its instruments in front of the player, and in mixed reality the side table is put at its new place
// at once, in the very frame (and the very press loop) the fingertip pressed PLAY. The fingertip then sat in STOP's press
// zone and the deck pressed STOP for it: the playback ended before it began. Now a deck that moves under the fingertips
// (during a press, or between two ticks: a glide that ended) counts every fingertip inside a control as already holding
// it: only a fingertip that leaves and comes back presses. And PLAY of the deck's saved loop tells the gallery which
// recording it is (it found none: "the playback's recording was not found in Recordings").
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Lessons
{
    public class DeckJumpTests
    {
        double _now;
        GameObject _table;
        EventBus _bus;
        Recorder _rec;
        TapeDeck _deck;
        HandState _hand;
        List<Pointer> _pointers;
        int _starts, _ends;
        System.Action<RecordingPlaybackEvent> _onPlayback;

        [SetUp]
        public void SetUp()
        {
            _now = 10;
            _table = new GameObject("TableRig");
            _table.transform.SetPositionAndRotation(new Vector3(-0.39f, 0f, -0.21f), Quaternion.Euler(0f, -90f, 0f));
            _bus = new EventBus();
            _rec = new Recorder(() => _now, _bus);
            _deck = new TapeDeck(_rec, new MaterialLibrary(), _table.transform, TapeDeck.TableLocalPosition);
            _hand = new HandState(Handedness.Right);
            _pointers = new List<Pointer>(_hand.Pointers);
            _starts = _ends = 0;
            _onPlayback = e => { if (e.Started) _starts++; else _ends++; };
            _bus.Subscribe(_onPlayback);
            PlayingTake.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _bus.Unsubscribe(_onPlayback);
            _deck.Destroy();
            Object.DestroyImmediate(_table);
            PlayingTake.Clear();
        }

        Pointer Index => _hand.Pointers[0];

        // One tracker frame for the index tip at a WORLD point (it rests there whatever the deck does).
        void TipAt(Vector3 world, float dt = 0.016f)
        {
            Pointer p = Index;
            p.WasActive = p.Active;
            p.Prev = p.Tip;
            p.Tip = world;
            p.Active = true;
            _now += dt;
            _deck.Tick(_pointers, dt);
            _rec.Update();
        }

        Vector3 World(DeckButton b, float y) => _deck.Root.TransformPoint(new Vector3(TapeDeck.ButtonLocal(b).x, y, TapeDeck.ButtonLocal(b).z));

        // The table moves so that `to` comes where `from` was (deck-local x: PLAY 0.03, STOP 0.09, REC -0.03).
        void ShiftDeck(DeckButton from, DeckButton to)
        {
            Vector3 d = World(from, 0.08f) - World(to, 0.08f);
            _table.transform.position += d;
        }

        void RecordTake()
        {
            _rec.Record();
            for (int i = 0; i < 8; i++) { _now += 0.2; _rec.Add(TakeEvent.PianoOn(60 + i, 0.7f)); _now += 0.1; _rec.Add(TakeEvent.PianoOff(60 + i)); }
            _rec.Stop();
            Assert.That(_rec.Take.Count, Is.EqualTo(16));
        }

        [Test]
        public void ATableMovedByPlaysOwnPlaybackNeverPressesStopUnderTheRestingFingertip()
        {
            RecordTake();
            // What Layout's stage does in mixed reality: the playback's start puts the table somewhere else at once.
            System.Action<RecordingPlaybackEvent> stage = e => { if (e.Started) ShiftDeck(DeckButton.Play, DeckButton.Stop); };
            _bus.Subscribe(stage);
            Vector3 play = World(DeckButton.Play, 0.08f), above = World(DeckButton.Play, 0.15f);
            TipAt(above); TipAt(above);
            int presses = _deck.Presses;
            TipAt(play);                                     // PLAY, and the deck jumps: STOP is under the fingertip now
            _bus.Unsubscribe(stage);
            Assert.That(_starts, Is.EqualTo(1), "PLAY started the playback");
            for (int i = 0; i < 20; i++) TipAt(play);        // the fingertip rests a moment
            Assert.That(_deck.Presses - presses, Is.EqualTo(1), "only PLAY was pressed");
            Assert.That(_ends, Is.EqualTo(0), "no STOP nobody pressed");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing), "the take plays on");
            // The fingertip leaves and comes back: now it is a real press of what is under it (STOP).
            Vector3 stop = World(DeckButton.Stop, 0.08f), overStop = World(DeckButton.Stop, 0.15f);
            TipAt(overStop); TipAt(overStop);
            TipAt(stop);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "a real STOP still stops it");
            Assert.That(_ends, Is.EqualTo(1));
        }

        [Test]
        public void ADeckThatArrivesUnderARestingFingertipPressesNothing()
        {
            RecordTake();
            Vector3 play = World(DeckButton.Play, 0.08f), above = World(DeckButton.Play, 0.15f);
            TipAt(above); TipAt(above);
            TipAt(play);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing));
            TipAt(play);
            // A frame later the table is somewhere else (a glide that ended, a re-placement): REC is under the fingertip.
            ShiftDeck(DeckButton.Play, DeckButton.Rec);
            int presses = _deck.Presses;
            for (int i = 0; i < 10; i++) TipAt(play);
            Assert.That(_deck.Presses, Is.EqualTo(presses), "nothing pressed by the deck moving");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing), "no REC started over the playback");
        }

        // 2026-11-18 headset log: "record on top of 'Take 23' at 1.1 s" then "deck REC pressed: the playback stops" 0.2 s later,
        // and each PLAY logged twice 0.1 s apart: one poke pressed twice (the fingertip wobbled at the edge, or a second
        // finger of the same poke arrived), so record on top started and stopped at once.
        [Test]
        public void OnePokeWithAWobbleOrASecondFingerPressesOnce()
        {
            RecordTake();
            Vector3 play = World(DeckButton.Play, 0.08f), overPlay = World(DeckButton.Play, 0.15f);
            TipAt(overPlay); TipAt(overPlay);
            TipAt(play);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing));
            TipAt(overPlay); TipAt(overPlay);
            // REC while it plays: record on top. The fingertip wobbles 5 mm past the press zone's edge and back, twice.
            Vector3 rec = World(DeckButton.Rec, 0.08f), overRec = World(DeckButton.Rec, 0.15f);
            Vector3 edge = rec + _deck.Root.TransformDirection(new Vector3(TapeDeck.PressR + 0.005f, 0f, 0f));
            int presses = _deck.Presses;
            TipAt(rec);
            Assert.IsTrue(_rec.RecordingOnTop, "REC records on top of the playing take");
            TipAt(edge); TipAt(rec); TipAt(edge); TipAt(rec);
            // a second finger of the same poke arrives on REC 0.1 s later
            Pointer second = _hand.Pointers[1];
            second.WasActive = second.Active = true;
            second.Prev = second.Tip = rec;
            for (int i = 0; i < 6; i++) TipAt(rec);
            second.Active = second.WasActive = false;
            Assert.That(_deck.Presses - presses, Is.EqualTo(1), "one poke, one press");
            Assert.IsTrue(_rec.RecordingOnTop, "still recording on top");
            // The fingertip leaves, and a while later a real second poke ends it.
            for (int i = 0; i < 40; i++) TipAt(overRec);
            TipAt(rec);
            Assert.IsFalse(_rec.RecordingOnTop, "a real second REC ends it");
        }

        [Test]
        public void AHandMovingWithTheDeckStillPressesAsBefore()
        {
            // No jump: plain presses are untouched (PLAY, then STOP after leaving PLAY).
            RecordTake();
            TipAt(World(DeckButton.Play, 0.15f)); TipAt(World(DeckButton.Play, 0.15f));
            TipAt(World(DeckButton.Play, 0.08f));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing));
            TipAt(World(DeckButton.Stop, 0.15f)); TipAt(World(DeckButton.Stop, 0.15f));
            TipAt(World(DeckButton.Stop, 0.08f));
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle));
        }

        [Test]
        public void PlayOfTheDecksSavedLoopTellsTheGalleryWhichRecordingItIs()
        {
            var host = new FakeLoopHost();
            var looper = new Looper(host) { InputLatency = 0 };
            var rec = new Recorder(() => host.Now, _bus) { Looper = looper };
            string saved = null;
            looper.Save += s => { s.Id = "take-loop-1"; s.Number = 7; saved = s.Id; };
            // A one-layer loop made on the deck, then STOP (saved), then PLAY.
            rec.Loop();
            host.T += 0.5;
            looper.Tick();
            rec.Add(TakeEvent.PianoOn(60, 0.7f));
            for (int i = 0; i < 1000 && looper.Layers == 0; i++) { host.T += 0.02; looper.Tick(); }
            Assert.That(looper.Layers, Is.EqualTo(1));
            rec.Stop();
            Assert.That(saved, Is.EqualTo("take-loop-1"));
            int v = PlayingTake.Version;
            Assert.IsTrue(rec.Play(), "PLAY loops the deck's saved session");
            Assert.That(PlayingTake.Version, Is.Not.EqualTo(v), "noted after the playback began");
            Assert.That(PlayingTake.Id, Is.EqualTo("take-loop-1"), "the gallery finds the saved loop by its id");
            Assert.IsTrue(LoopFormat.IsLoop(PlayingTake.Events));
            rec.Stop();
        }
    }
}
