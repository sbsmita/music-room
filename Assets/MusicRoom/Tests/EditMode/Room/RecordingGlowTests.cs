// The room comes alive while you record (2026-11-11): the live notes that go into a recording (the tape deck's REC, a
// looper layer recording its pass) lift the listen-back model as playback does; plain live play never does; a played-back
// note and a recorded one are never the same sound, so an overdub over a playing loop announces each note once; a live
// lane holds while its controls come and lets go after LiveHoldTimeout without them.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using MusicRoom.Room.Decor;
using MusicRoom.Tests.Lessons;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class RecordingGlowTests
    {
        const float Dt = 1f / 72f;

        static void Run(ListenBackModel m, ref double now, float seconds)
        {
            int n = (int)System.Math.Ceiling(seconds / Dt);
            for (int i = 0; i < n; i++) { now += Dt; m.Tick(Dt, now); }
        }

        static RecordedNoteEvent Rec(InstrumentKind k, EventRole role, float level) => new RecordedNoteEvent { Kind = k, Role = role, Level = level };

        [Test]
        public void ARecordedNoteLiftsTheRoomAsAPlayedBackOneDoes()
        {
            var played = new ListenBackModel();
            var recorded = new ListenBackModel();
            double t1 = 10, t2 = 10;
            played.Note(new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.7f, Lane = PlaybackLanes.Take }, t1);
            recorded.Recorded(Rec(InstrumentKind.Piano, EventRole.Onset, 0.7f));
            Assert.That(recorded.RecordedNotes, Is.EqualTo(1));
            Assert.That(recorded.SinceRecorded, Is.EqualTo(0f));
            Assert.That(float.IsPositiveInfinity(recorded.SincePlayback), Is.True, "no playback yet");
            for (int i = 0; i < 40; i++)
            {
                t1 += Dt; t2 += Dt;
                played.Tick(Dt, t1);
                recorded.Tick(Dt, t2);
                Assert.That(recorded.Room, Is.EqualTo(played.Room).Within(1e-6f), "the same envelope, frame by frame");
                Assert.That(recorded.Motes(InstrumentKind.Piano), Is.EqualTo(played.Motes(InstrumentKind.Piano)), "the same motes");
                if (i == 0) Assert.That(recorded.Onset(InstrumentKind.Piano), Is.EqualTo(0.7f).Within(1e-6f), "the onset is seen in the next Tick");
                else Assert.That(recorded.Onset(InstrumentKind.Piano), Is.EqualTo(0f), "and only then");
            }
            Assert.That(recorded.SinceRecorded, Is.EqualTo(40 * Dt).Within(1e-4f));
            Run(recorded, ref t2, 5f);
            Assert.IsTrue(recorded.Resting, "it rests exactly");
        }

        [Test]
        public void ALiveLaneHoldsWhileItsControlsComeAndLetsGoWithoutThem()
        {
            var m = new ListenBackModel();
            double now = 10;
            m.Recorded(Rec(InstrumentKind.Bowls, EventRole.Onset, 0.6f));
            for (int i = 0; i < 72; i++) { if (i % 3 == 0) m.Recorded(Rec(InstrumentKind.Bowls, EventRole.Control, 0.6f)); now += Dt; m.Tick(Dt, now); }
            Assert.That(m.Level(InstrumentKind.Bowls), Is.EqualTo(0.6f).Within(0.05f), "held at its level");
            Assert.IsTrue(m.Holding);
            Assert.That(m.SinceRecorded, Is.LessThan(0.1f), "a held lane counts as sounding");
            // its release went outside the recording (the layer's window closed): the hold lets go by itself
            Run(m, ref now, ListenBackModel.LiveHoldTimeout + 0.1f);
            Assert.IsFalse(m.Holding);
            Run(m, ref now, 5f);
            Assert.IsTrue(m.Resting);
            // a release lets go at once
            m.Recorded(Rec(InstrumentKind.Bowls, EventRole.Control, 0.8f));
            Run(m, ref now, 0.3f);
            Assert.IsTrue(m.Holding);
            m.Recorded(Rec(InstrumentKind.Bowls, EventRole.Release, 0f));
            Run(m, ref now, 0.05f);
            Assert.IsFalse(m.Holding);
        }

        [Test]
        public void ARecordedFrameAllocatesNothing()
        {
            var m = new ListenBackModel();
            double now = 10;
            var on = Rec(InstrumentKind.Drums, EventRole.Onset, 0.9f);
            var ctl = Rec(InstrumentKind.Bowls, EventRole.Control, 0.4f);
            for (int i = 0; i < 50; i++) { m.Recorded(on); m.Recorded(ctl); now += Dt; m.Tick(Dt, now); }
            Assert.That(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    if (i % 5 == 0) m.Recorded(on);
                    m.Recorded(ctl);
                    now += Dt;
                    m.Tick(Dt, now);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------- the source: the recorder

        [Test]
        public void TheRecorderAnnouncesALiveNoteOnlyWhenItGoesIntoATake()
        {
            double now = 100;
            var bus = new EventBus();
            var recorded = new List<RecordedNoteEvent>();
            var played = new List<PlaybackNoteEvent>();
            bus.Subscribe<RecordedNoteEvent>(e => recorded.Add(e));
            bus.Subscribe<PlaybackNoteEvent>(e => played.Add(e));
            var rec = new Recorder(() => now, bus) { Roles = EventRoles.From(InstrumentRegistry.Discover()) };
            rec.SetTarget(TakeEventType.PianoOn, e => { });
            rec.SetTarget(TakeEventType.PianoOff, e => { });

            rec.Add(TakeEvent.PianoOn(60, 0.5f));                  // live, idle: calm
            now += 0.2; rec.Add(TakeEvent.PianoOff(60));
            Assert.That(recorded, Is.Empty, "plain live play announces nothing");

            rec.Record();
            now += 0.1; rec.Add(TakeEvent.PianoOn(64, 0.8f));
            now += 0.3; rec.Add(TakeEvent.PianoOff(64));
            Assert.That(recorded.Count, Is.EqualTo(2), "both went into the take");
            Assert.That(recorded[0].Kind, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(recorded[0].Role, Is.EqualTo(EventRole.Onset));
            Assert.That(recorded[0].Level, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(recorded[1].Role, Is.EqualTo(EventRole.Release));
            Assert.That(recorded[1].Level, Is.EqualTo(0f));
            now += 0.2; rec.Stop();
            Assert.That(played, Is.Empty, "recording is not playback");

            Assert.IsTrue(rec.Play());
            now += 0.15; rec.Update();
            rec.Add(TakeEvent.PianoOn(72, 0.9f));                  // playing along with the take: live, not recorded
            now += 0.5; rec.Update();
            Assert.That(recorded.Count, Is.EqualTo(2), "playing along with a take records nothing, so it announces nothing");
            Assert.That(played.Count, Is.EqualTo(2), "the take's own note and its release");
        }

        [Test]
        public void AnOverdubOverAPlayingLoopAnnouncesEachSoundOnce()
        {
            const int Sr = 48000;
            const long Beat90 = 32000, Pass = 8 * Beat90;
            var host = new FakeLoopHost();
            var loop = new Looper(host) { InputLatency = 0, Tidy = false };
            var bus = new EventBus();
            var recorded = new List<RecordedNoteEvent>();
            var played = new List<PlaybackNoteEvent>();
            var model = new ListenBackModel();
            bus.Subscribe<RecordedNoteEvent>(e => { recorded.Add(e); model.Recorded(e); });
            bus.Subscribe<PlaybackNoteEvent>(e => { played.Add(e); model.Note(e, host.T); });
            var rec = new Recorder(() => host.T, bus) { Looper = loop, Roles = EventRoles.From(InstrumentRegistry.Discover()) };
            // As in the app: the looper's sends go through the recorder to the instrument, and the instrument's playback
            // call comes back into the recorder (the piano's ghost presser does), flagged as the loop's own.
            rec.SetTarget(TakeEventType.Drum, e => rec.Add(e));
            rec.SetTarget(TakeEventType.PianoOn, e => rec.Add(e));
            rec.SetTarget(TakeEventType.PianoOff, e => rec.Add(e));
            host.OnDispatch = e => rec.Dispatch(e);

            void TickTo(long sample)
            {
                double end = sample / (double)Sr;
                while (host.T < end - 1e-12) { host.T = System.Math.Min(end, host.T + 1.0 / 72.0); rec.Update(); }
            }

            rec.Loop();
            long a = loop.ClickStart + 4 * Beat90;
            TickTo(a);
            rec.Add(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));           // layer 1's first note
            TickTo(a + 4 * Beat90);
            rec.Add(TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false));
            Assert.That(recorded.Count, Is.EqualTo(2), "layer 1's notes are recorded notes");
            Assert.That(played, Is.Empty);

            // pass 1 plays layer 1 back; live play now, with no layer armed, is not recorded: calm
            TickTo(a + Pass + 2 * Beat90);
            Assert.That(rec.State, Is.EqualTo(RecorderState.Playing));
            rec.Add(TakeEvent.PianoOn(60, 0.9f));
            Assert.That(recorded.Count, Is.EqualTo(2), "live play over the loop without a layer recording announces nothing");
            int playedBefore = played.Count;
            Assert.That(playedBefore, Is.GreaterThanOrEqualTo(1), "the loop's kick played back");

            // overdub: layer 2 records its pass while layer 1 plays
            rec.Record();
            Assert.IsTrue(loop.Armed);
            long ps = a + 2 * Pass;
            TickTo(ps + 100);
            rec.Add(TakeEvent.PianoOn(67, 0.6f));
            TickTo(ps + 2 * Beat90);
            rec.Add(TakeEvent.PianoOff(67));
            TickTo(ps + Pass - 1000);
            Assert.That(recorded.Count, Is.EqualTo(4), "the overdub's note and release, once each");
            int loopSends = 0;
            foreach (var s in host.Sent) if (s.e.type == TakeEventType.Drum) loopSends++;
            int drumPlayed = 0;
            foreach (var p in played) if (p.Kind == InstrumentKind.Drums) drumPlayed++;
            Assert.That(drumPlayed, Is.EqualTo(loopSends), "each of the loop's sends announced once as playback, never as a recording");
            Assert.That(model.Notes, Is.EqualTo(recorded.Count + played.Count), "the room took each sound once");
            int kicksRecorded = 0;
            foreach (var r in recorded) if (r.Kind == InstrumentKind.Drums) kicksRecorded++;
            Assert.That(kicksRecorded, Is.EqualTo(2), "only layer 1's own two strokes");
            rec.Stop();
        }
    }
}
