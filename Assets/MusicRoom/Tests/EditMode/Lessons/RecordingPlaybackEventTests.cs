// The recorder announces the playback of a recording (RecordingPlaybackEvent) with the instruments it plays, so Layout can
// bring them in front of the player (2026-11-11: "when played recording it doesnt play as piano is not in front"):
//   - a take: Started with its instruments (markers left out) on PLAY / PlayEvents, again on a replay; Ended at its end,
//     on STOP, and when REC stops it; recording is never a playback;
//   - a loop: the deck's PLAY of its loop and a saved loop from Recordings start one (Loop, every layer's instruments, muted
//     ones too); an overdub onto it does not end it; STOP / CLEAR do; a loop session being recorded (LOOP) never starts one.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Lessons
{
    public class RecordingPlaybackEventTests
    {
        const int Sr = 48000;
        const long Beat90 = 32000, Pass = 8 * Beat90;
        static readonly uint PianoBit = 1u << (int)InstrumentKind.Piano, DrumsBit = 1u << (int)InstrumentKind.Drums;

        FakeLoopHost _host;
        Looper _loop;
        Recorder _rec;
        EventBus _bus;
        readonly List<RecordingPlaybackEvent> _events = new List<RecordingPlaybackEvent>();

        [SetUp]
        public void SetUp()
        {
            _events.Clear();
            _host = new FakeLoopHost();
            _loop = new Looper(_host) { InputLatency = 0, Tidy = false };
            _bus = new EventBus();
            _bus.Subscribe<RecordingPlaybackEvent>(e => _events.Add(e));
            _rec = new Recorder(() => _host.T, _bus) { Looper = _loop, Roles = EventRoles.From(InstrumentRegistry.Discover()) };
            foreach (string t in new[] { TakeEventType.PianoOn, TakeEventType.PianoOff, TakeEventType.Drum, TakeEventType.Harp })
                _rec.SetTarget(t, e => { });
            _host.OnDispatch = e => _rec.Dispatch(e);
        }

        void TickFor(double seconds)
        {
            double end = _host.T + seconds;
            while (_host.T < end - 1e-12) { _host.T = System.Math.Min(end, _host.T + 1.0 / 72.0); _rec.Update(); }
        }

        static List<TakeEvent> Take(params (string type, float t)[] evs)
        {
            var l = new List<TakeEvent>();
            foreach (var (type, t) in evs)
            {
                TakeEvent e = type == TakeEventType.PianoOn ? TakeEvent.PianoOn(60, 0.8f)
                    : type == TakeEventType.PianoOff ? TakeEvent.PianoOff(60)
                    : type == TakeEventType.Drum ? TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false)
                    : new TakeEvent { type = type };
                e.t = t;
                l.Add(e);
            }
            return l;
        }

        [Test]
        public void ATakeAnnouncesItsInstrumentsAndItsEnd()
        {
            // Recording is never a playback.
            _rec.Record();
            _rec.Add(TakeEvent.PianoOn(60, 0.8f));
            TickFor(0.3);
            _rec.Add(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            TickFor(0.3);
            _rec.Stop();
            Assert.That(_events, Is.Empty, "recording announces no playback");

            // PLAY: both instruments; the natural end.
            Assert.IsTrue(_rec.Play());
            Assert.That(_events.Count, Is.EqualTo(1));
            Assert.IsTrue(_events[0].Started);
            Assert.IsFalse(_events[0].Loop);
            Assert.That(_events[0].Kinds, Is.EqualTo(PianoBit | DrumsBit));
            Assert.That(_rec.PlaybackKinds, Is.EqualTo(PianoBit | DrumsBit));
            TickFor(1.0);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(_events.Count, Is.EqualTo(2));
            Assert.IsFalse(_events[1].Started, "its end");
            Assert.That(_rec.PlaybackKinds, Is.EqualTo(0u));

            // A saved take from Recordings (a marker, the bowls' "bowlHit" judgement, plays nothing: no instrument): then STOP.
            _events.Clear();
            var take = Take((TakeEventType.PianoOn, 0.1f), (TakeEventType.PianoOff, 0.3f), ("bowlHit", 0.35f));
            Assert.IsTrue(_rec.PlayEvents(take, 2.0));
            Assert.That(_events[0].Kinds, Is.EqualTo(PianoBit), "the piano only");
            // A replay while it plays: started again, no end in between.
            Assert.IsTrue(_rec.PlayEvents(Take((TakeEventType.Drum, 0.1f)), 2.0));
            Assert.That(_events.Count, Is.EqualTo(2));
            Assert.IsTrue(_events[1].Started);
            Assert.That(_events[1].Kinds, Is.EqualTo(DrumsBit));
            _rec.Stop();
            Assert.That(_events.Count, Is.EqualTo(3));
            Assert.IsFalse(_events[2].Started, "STOP ends it");

            // REC while a take plays: the playback ends, the recording is not one.
            _events.Clear();
            Assert.IsTrue(_rec.Play());
            TickFor(0.1);
            _rec.Record();
            Assert.That(_events.Count, Is.EqualTo(2));
            Assert.IsFalse(_events[1].Started);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording));
            _rec.Stop();
            Assert.That(_events.Count, Is.EqualTo(2));
        }

        [Test]
        public void ALoopAnnouncesItsInstrumentsUntilItStopsAndALiveLoopNever()
        {
            // A loop being recorded: LOOP, layer 1, looping, an overdub: never a playback of a recording.
            _rec.Loop();
            long a = _loop.ClickStart + 4 * Beat90;
            TickFor(a / (double)Sr - _host.T);
            _rec.Add(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            TickFor(Pass / (double)Sr + 0.5);
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Playing), "it loops");
            _rec.Record();
            TickFor(Pass / (double)Sr);
            Assert.That(_events, Is.Empty, "the player's own loop session is not a playback");
            _rec.Stop();
            Assert.That(_events, Is.Empty);

            // PLAY on the deck loops it again: a playback of the drums.
            Assert.IsTrue(_rec.CurrentIsLoop);
            Assert.IsTrue(_rec.Play());
            Assert.That(_events.Count, Is.EqualTo(1));
            Assert.IsTrue(_events[0].Started && _events[0].Loop);
            Assert.That(_events[0].Kinds, Is.EqualTo(DrumsBit));
            TickFor(2 * Pass / (double)Sr);
            _rec.Record();   // an overdub onto it: it goes on
            TickFor(Pass / (double)Sr);
            Assert.That(_events.Count, Is.EqualTo(1), "overdubbing does not end it");
            _rec.Stop();
            Assert.That(_events.Count, Is.EqualTo(2));
            Assert.IsFalse(_events[1].Started);
            Assert.IsTrue(_events[1].Loop);

            // A saved loop from Recordings with a piano layer and a muted drums layer: both instruments.
            _events.Clear();
            var s = new LoopSession(90, 2, Sr);
            var piano = new LoopLayer();
            piano.Insert(0, TakeEvent.PianoOn(60, 0.8f));
            piano.Insert(Beat90, TakeEvent.PianoOff(60));
            var drums = new LoopLayer { Muted = true };
            drums.Insert(Beat90 * 2, TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false));
            s.Layers.Add(piano);
            s.Layers.Add(drums);
            Assert.IsTrue(_rec.PlayEvents(LoopFormat.Encode(s), 0));
            Assert.That(_events.Count, Is.EqualTo(1));
            Assert.That(_events[0].Kinds, Is.EqualTo(PianoBit | DrumsBit), "every layer's instruments, a muted one too");
            // Another saved loop replaces it: the first ends, the second starts.
            Assert.IsTrue(_rec.PlayEvents(LoopFormat.Encode(s), 0));
            Assert.That(_events.Count, Is.EqualTo(3));
            Assert.IsFalse(_events[1].Started);
            Assert.IsTrue(_events[2].Started);
            // CLEAR (tap twice) ends it.
            _loop.Clear();
            _loop.Clear();
            Assert.That(_events.Count, Is.EqualTo(4));
            Assert.IsFalse(_events[3].Started);
        }

        [Test]
        public void KindsOfATakeAllocateNothing()
        {
            var roles = EventRoles.From(InstrumentRegistry.Discover());
            var take = Take((TakeEventType.PianoOn, 0.1f), (TakeEventType.Drum, 0.2f), (TakeEventType.PianoOff, 0.3f));
            Recorder.KindsOf(take, roles);
            Assert.That(() => { Recorder.KindsOf(take, roles); }, Is.Not.AllocatingGCMemory());
            Assert.That(Recorder.KindsOf(take, roles), Is.EqualTo(PianoBit | DrumsBit));
        }
    }
}
