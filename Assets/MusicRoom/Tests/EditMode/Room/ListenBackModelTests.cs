// The listen-back pulse's numbers (Room/Decor/ListenBackModel.cs) and its source (the recorder publishes PlaybackNoteEvent
// for what it plays back, never for live play): an onset rises fast and falls back slowly to an exact rest; a note the
// looper sends ahead waits until it sounds; a continuous lane holds until its release or the stop; motes follow the
// velocity under a shared budget; a frame allocates nothing.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class ListenBackModelTests
    {
        const float Dt = 1f / 72f;

        static PlaybackNoteEvent Note(InstrumentKind k, EventRole role, float level, double when = 0) =>
            new PlaybackNoteEvent { Kind = k, Role = role, Level = level, When = when, Lane = PlaybackLanes.Take };

        static void Run(ListenBackModel m, ref double now, float seconds)
        {
            int n = (int)System.Math.Ceiling(seconds / Dt);
            for (int i = 0; i < n; i++) { now += Dt; m.Tick(Dt, now); }
        }

        [Test]
        public void AnOnsetRisesFastAndFallsBackToAnExactRest()
        {
            var m = new ListenBackModel();
            double now = 10;
            Assert.IsTrue(m.Resting);
            m.Note(Note(InstrumentKind.Drums, EventRole.Onset, 0.8f), now);
            Run(m, ref now, 0.06f);                                   // fast attack: most of the way within 60 ms
            float peak = m.Level(InstrumentKind.Drums);
            Assert.That(peak, Is.GreaterThan(0.6f), "fast attack");
            Assert.That(m.Room, Is.EqualTo(peak), "the room follows the loudest instrument");
            Assert.That(m.Level(InstrumentKind.Piano), Is.EqualTo(0f), "only the instrument that played");
            Run(m, ref now, 0.3f);
            float later = m.Level(InstrumentKind.Drums);
            Assert.That(later, Is.LessThan(peak).And.GreaterThan(0.2f), "a slow fall (not a blink)");
            Run(m, ref now, 5f);
            Assert.IsTrue(m.Resting, "it comes to rest by itself");
            Assert.That(m.Level(InstrumentKind.Drums), Is.EqualTo(0f));
            Assert.That(m.Room, Is.EqualTo(0f));
        }

        [Test]
        public void ALouderNoteGlowsBrighter()
        {
            var soft = new ListenBackModel();
            var loud = new ListenBackModel();
            double a = 0, b = 0;
            soft.Note(Note(InstrumentKind.Piano, EventRole.Onset, 0.25f), a);
            loud.Note(Note(InstrumentKind.Piano, EventRole.Onset, 0.9f), b);
            Run(soft, ref a, 0.1f);
            Run(loud, ref b, 0.1f);
            Assert.That(loud.Room, Is.GreaterThan(soft.Room * 2.5f));
        }

        [Test]
        public void ANoteSentAheadWaitsUntilItSounds()
        {
            var m = new ListenBackModel();
            double now = 5;
            m.Note(Note(InstrumentKind.Harp, EventRole.Onset, 0.7f, when: now + 0.08), now);
            Assert.That(m.PendingCount, Is.EqualTo(1));
            Run(m, ref now, 0.05f);
            Assert.That(m.Level(InstrumentKind.Harp), Is.EqualTo(0f), "not before its audio time");
            Run(m, ref now, 0.1f);
            Assert.That(m.Level(InstrumentKind.Harp), Is.GreaterThan(0.3f), "at its audio time");
            Assert.That(m.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void AContinuousLaneHoldsUntilItsReleaseOrTheStop()
        {
            var m = new ListenBackModel();
            double now = 0;
            m.Note(Note(InstrumentKind.Bowls, EventRole.Onset, 0.6f), now);
            m.Note(Note(InstrumentKind.Bowls, EventRole.Control, 0.6f), now);
            int flow = 0;
            for (int i = 0; i < 144; i++) { now += Dt; m.Tick(Dt, now); if (i >= 72) flow += m.Motes(InstrumentKind.Bowls); }
            Assert.That(m.Level(InstrumentKind.Bowls), Is.EqualTo(0.6f).Within(0.03f), "held at the replayed level");
            Assert.That(flow, Is.InRange(2, 7), "a held lane breathes a few motes a second (FlowPerSecond x level)");
            m.Note(Note(InstrumentKind.Bowls, EventRole.Release, 0f), now);
            Run(m, ref now, 5f);
            Assert.IsTrue(m.Resting, "the release lets it fall to rest");

            m.Note(Note(InstrumentKind.Bowls, EventRole.Control, 0.8f), now);
            Run(m, ref now, 1f);
            Assert.That(m.Level(InstrumentKind.Bowls), Is.GreaterThan(0.6f));
            m.Note(Note(InstrumentKind.Piano, EventRole.Onset, 0.5f, when: now + 0.05), now);
            m.Stop();                                                  // the recorder went idle
            Assert.That(m.PendingCount, Is.EqualTo(0), "what waits is dropped");
            Run(m, ref now, 5f);
            Assert.IsTrue(m.Resting, "the stop lets every hold go");
            Assert.That(m.Level(InstrumentKind.Piano), Is.EqualTo(0f));
        }

        [Test]
        public void MotesFollowTheVelocityUnderASharedBudget()
        {
            var m = new ListenBackModel();
            double now = 0;
            m.Note(Note(InstrumentKind.Marimba, EventRole.Onset, 0.05f), now);
            now += Dt; m.Tick(Dt, now);
            Assert.That(m.Motes(InstrumentKind.Marimba), Is.EqualTo(1), "a soft note: one mote");
            Run(m, ref now, 1f);
            m.Note(Note(InstrumentKind.Marimba, EventRole.Onset, 1f), now);
            now += Dt; m.Tick(Dt, now);
            Assert.That(m.Motes(InstrumentKind.Marimba), Is.EqualTo(3), "a loud one: three");
            Run(m, ref now, 1f);
            for (int i = 0; i < 100; i++) m.Note(Note(InstrumentKind.Drums, EventRole.Onset, 1f), now);
            now += Dt; m.Tick(Dt, now);
            Assert.That(m.Motes(InstrumentKind.Drums), Is.LessThanOrEqualTo((int)ListenBackModel.BudgetBurst), "a roll never floods the room");
            int total = 0;
            for (int f = 0; f < 72; f++)
            {
                for (int i = 0; i < 4; i++) m.Note(Note(InstrumentKind.Drums, EventRole.Onset, 1f), now);
                now += Dt; m.Tick(Dt, now);
                total += m.Motes(InstrumentKind.Drums);
            }
            Assert.That(total, Is.LessThanOrEqualTo((int)(ListenBackModel.BudgetPerSecond + ListenBackModel.BudgetBurst) + 1), "the budget per second");
        }

        [Test]
        public void MarkersAndOtherKindsAreIgnoredAndEveryInstrumentGlowsOrange()
        {
            var m = new ListenBackModel();
            m.Note(Note(InstrumentKind.Piano, EventRole.Marker, 1f), 0);
            m.Note(Note((InstrumentKind)42, EventRole.Onset, 1f), 0);
            Assert.IsTrue(m.Resting);
            Assert.That(m.Blend(), Is.EqualTo(UnityEngine.Color.white), "silent: no tint");
            // the playback glow is one warm orange: red full, green about half, blue low
            UnityEngine.Color g = ListenBackModel.Glow;
            Assert.That(g.r, Is.EqualTo(1f));
            Assert.That(g.g, Is.InRange(0.4f, 0.6f));
            Assert.That(g.b, Is.LessThan(0.2f));
            foreach (InstrumentKind k in new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp, InstrumentKind.Handpan,
                         InstrumentKind.HandDrums, InstrumentKind.Marimba, InstrumentKind.Bowls })
                Assert.That(ListenBackModel.ColorOf(k), Is.EqualTo(g), k + " glows orange");
            m.Note(Note(InstrumentKind.Harp, EventRole.Onset, 0.8f), 0);
            m.Tick(0.05f, 0.05);
            Assert.That(m.Blend(), Is.EqualTo(g), "what plays tints orange");
        }

        [Test]
        public void AFrameAllocatesNothing()
        {
            var m = new ListenBackModel();
            double now = 0;
            int frame = 0;
            PlaybackNoteEvent hit = Note(InstrumentKind.Piano, EventRole.Onset, 0.7f), ahead = Note(InstrumentKind.Harp, EventRole.Onset, 0.5f);
            PlaybackNoteEvent ctl = Note(InstrumentKind.Bowls, EventRole.Control, 0.5f);
            for (int i = 0; i < 30; i++) { m.Note(hit, now); now += Dt; m.Tick(Dt, now); }
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    frame++;
                    if (frame % 3 == 0) m.Note(hit, now);
                    ahead.When = now + 0.05; m.Note(ahead, now);
                    m.Note(ctl, now);
                    now += Dt;
                    m.Tick(Dt, now);
                    m.Blend();
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------- the source: the recorder

        [Test]
        public void TheRecorderAnnouncesWhatItPlaysBackAndNeverLivePlay()
        {
            double now = 100;
            var bus = new EventBus();
            var notes = new List<PlaybackNoteEvent>();
            bus.Subscribe<PlaybackNoteEvent>(e => notes.Add(e));
            var rec = new Recorder(() => now, bus);
            rec.SetTarget(TakeEventType.PianoOn, e => { });
            rec.SetTarget(TakeEventType.PianoOff, e => { });
            rec.SetTarget(TakeEventType.Drum, e => { });

            rec.Add(TakeEvent.PianoOn(60, 0.5f));                     // live, idle
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.8f));                     // live, recording
            now += 0.5; rec.Add(TakeEvent.PianoOff(60));
            now += 0.5; rec.Add(TakeEvent.Drum("snare", 0.6f, 0.3f, "head", false));
            now += 0.25; rec.Stop();
            Assert.That(notes, Is.Empty, "live play announces nothing");

            Assert.IsTrue(rec.Play());
            rec.Update();
            rec.Add(TakeEvent.PianoOn(72, 0.9f));                     // the player plays along while it plays: live
            Assert.That(notes.Count, Is.EqualTo(1), "only the take's own note");
            Assert.That(notes[0].Role, Is.EqualTo(EventRole.Onset));
            Assert.That(notes[0].Level, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(notes[0].Lane, Is.EqualTo(PlaybackLanes.Take));
            now += 0.6; rec.Update();
            Assert.That(notes.Count, Is.EqualTo(2));
            Assert.That(notes[1].Role, Is.EqualTo(EventRole.Release));
            Assert.That(notes[1].Level, Is.EqualTo(0f), "a release is silence");
            now += 1.0; rec.Update();
            Assert.That(notes.Count, Is.EqualTo(3));
            Assert.That(notes[2].Level, Is.EqualTo(0.6f).Within(1e-6f));

            // The looper's sends carry their audio time.
            rec.Dispatch(TakeEvent.Drum("kick", 0.9f, 0.3f, "head", false), now + 0.07);
            Assert.That(notes.Count, Is.EqualTo(4));
            Assert.That(notes[3].When, Is.EqualTo(now + 0.07).Within(1e-9));
            rec.Dispatch(new TakeEvent { type = "nobodysType", vel = 1f }, 0);
            Assert.That(notes.Count, Is.EqualTo(4), "a type nobody plays announces nothing");
        }
    }
}
