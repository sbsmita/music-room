using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Lessons
{
    public class RecorderTests
    {
        double _now;

        [Test]
        public void RecordsAndPlaysBackInOrder()
        {
            _now = 100;
            var bus = new EventBus();
            int observed = 0;
            bus.Subscribe<RecorderEvent>(e => observed++);
            var rec = new Recorder(() => _now, bus);
            List<TakeEvent> saved = null; double savedSeconds = 0;
            rec.OnTake += (evs, s) => { saved = evs; savedSeconds = s; };

            rec.Add(TakeEvent.PianoOn(60, 0.5f));       // observed, not recorded (idle)
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.8f));
            _now += 0.5; rec.Add(TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false));
            _now += 0.5; rec.Add(TakeEvent.Harp(3, 0.6f, 0.35f, 0.9f, "through"));
            _now += 0.25; rec.Stop();
            Assert.That(observed, Is.EqualTo(4));
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.Count, Is.EqualTo(3));
            Assert.That(saved[1].t, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(savedSeconds, Is.EqualTo(1.25).Within(1e-9));

            var played = new List<string>();
            rec.SetTarget(TakeEventType.PianoOn, e => played.Add("piano" + e.midi));
            rec.SetTarget(TakeEventType.Drum, e => played.Add(e.kind));
            rec.SetTarget(TakeEventType.Harp, e => played.Add("harp" + e.i));
            Assert.IsTrue(rec.Play());
            rec.Update();                       // t = 0: the first event
            Assert.That(played, Is.EqualTo(new[] { "piano60" }));
            _now += 0.6; rec.Update();
            Assert.That(played.Count, Is.EqualTo(2));
            _now += 1.0; rec.Update();          // past the duration: stops
            Assert.That(played, Is.EqualTo(new[] { "piano60", "snare", "harp3" }));
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
        }

        [Test]
        public void SavedTakePlaybackRestoresTheDeckTake()
        {
            _now = 0;
            var rec = new Recorder(() => _now, null);
            rec.Record();
            rec.Add(TakeEvent.PianoOn(64, 0.5f));
            _now += 1; rec.Stop();
            var deck = rec.Take;
            var external = new List<TakeEvent> { TakeEvent.PianoOn(72, 0.5f) };
            Assert.IsTrue(rec.PlayEvents(external, 2));
            Assert.IsTrue(rec.External);
            Assert.That(rec.Take, Is.SameAs(external));
            rec.Stop();
            Assert.IsFalse(rec.External);
            Assert.That(rec.Take, Is.SameAs(deck), "the deck's own take comes back");
            Assert.IsTrue(rec.Play(), "deck PLAY plays the deck's take");
            Assert.That(rec.Take, Is.SameAs(deck));
        }
    }

    public class LessonLibraryTests
    {
        [Test]
        public void ParsesAllThirtySixLessons()
        {
            var text = Resources.Load<TextAsset>(LessonLibrary.ResourceName);
            Assert.That(text, Is.Not.Null, "Lessons/Resources/lessons.json");
            var lib = LessonLibrary.Parse(text.text);
            Assert.That(lib.Lessons.Count, Is.EqualTo(36));   // first five: the proposed lessons (tutorials-audit.md)
            Assert.That(lib.Count(InstrumentKind.Piano), Is.EqualTo(15));
            Assert.That(lib.Count(InstrumentKind.Drums), Is.EqualTo(12));
            Assert.That(lib.Count(InstrumentKind.Harp), Is.EqualTo(9));
            Assert.That(lib.TotalEvents, Is.EqualTo(1148));
            Assert.That(lib.TotalTargets, Is.EqualTo(1263));
            var scale = lib.Get("piano-scale");
            Assert.That(scale.listNote, Is.EqualTo("15 notes"));
            Assert.That(scale.events[0].due, Is.EqualTo(1.9444f).Within(1e-3f));
            Assert.That(scale.events[0].targets[0].midi, Is.EqualTo(60));
            var beat = lib.Get("drums-beat");
            Assert.That(beat.listNote, Is.EqualTo("8 beats"));
            Assert.That(beat.events[0].targets[0].kind, Is.EqualTo("hat"));
            var arp = lib.Get("harp-arpeggio");
            Assert.That(arp.events[0].targets[0].i, Is.EqualTo(7));
            Assert.That(arp.events[0].targets[0].color, Is.EqualTo("red"));
            Assert.That(lib.Constants.SPEED, Is.EqualTo(0.18f).Within(1e-6f));
            Assert.That(lib.Constants.leadSeconds.harp, Is.EqualTo(1.6667f).Within(1e-3f));
        }
    }

    public class TakeLibraryTests
    {
        readonly List<string> _dirs = new List<string>();

        string NewDir()
        {
            string d = System.IO.Path.Combine(Application.temporaryCachePath, "takes-test-" + System.Guid.NewGuid().ToString("N"));
            _dirs.Add(d);
            return d;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string d in _dirs) if (System.IO.Directory.Exists(d)) System.IO.Directory.Delete(d, true);
            _dirs.Clear();
        }

        [Test]
        public void FormatsLengthAndNames()
        {
            var lib = new TakeLibrary(NewDir());
            Assert.That(lib.Available, Is.True);
            Assert.That(lib.FormatLength(59.6), Is.EqualTo("1:00"), "no 0:60 (rounding quirk fixed)");
            Assert.That(lib.FormatLength(125), Is.EqualTo("2:05"));
            Assert.That(lib.FormatLength(0.5), Is.EqualTo("0:01"), "JS Math.round: halves round up");
            Assert.That(lib.FormatLength(-3), Is.EqualTo("0:00"));
            Assert.That(lib.NextName(7), Is.EqualTo("Take 1 · 0:07"));
            Assert.That(lib.NextName(7)[6], Is.EqualTo(' ')); Assert.That(lib.NextName(7)[7], Is.EqualTo('·'), "U+00B7 between spaces");
            Assert.That(lib.FormatDate(1729435920000), Does.Match(@"^[A-Z][a-z]{2} \d{1,2}, 20\d\d, \d{1,2}:\d{2} [AP]M$"), "\"Oct 20, 2024, 2:32 PM\" (a past year)");
            Assert.That(lib.NewId(), Does.Match("^take-[0-9a-z]+-[0-9a-z]+$"));
            Assert.That(TakeLibrary.Base36(35), Is.EqualTo("z")); Assert.That(TakeLibrary.Base36(36 * 36), Is.EqualTo("100"));
        }

        [Test]
        public void ReservingANameTakesTheNumberAtOnce()
        {
            var lib = new TakeLibrary(NewDir());
            int mirrored = 0;
            lib.NextNumberChanged += n => mirrored = n;
            Assert.That(lib.ReserveName(3.2), Is.EqualTo("Take 1 · 0:03"));
            Assert.That(lib.ReserveName(61), Is.EqualTo("Take 2 · 1:01"), "a second take stopped before the first write never repeats N");
            Assert.That(lib.NextNumber, Is.EqualTo(3));
            Assert.That(mirrored, Is.EqualTo(3), "the module mirrors this into AppSettings.KeyNextTakeNumber");
        }

        // Frames to wait for a library task (its continuation runs on the editor's main-thread loop). Used as
        // `foreach (var _ in Wait(t)) yield return null;` so no nested-coroutine support is needed in EditMode.
        static IEnumerable<object> Wait(System.Threading.Tasks.Task task)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            Assert.IsTrue(task.IsCompleted, "IO finished within 10 s");
        }

        [UnityTest]
        public IEnumerator PutListGetRemoveRoundTripAndNumbersSurviveADelete()
        {
            string dir = NewDir();
            var lib = new TakeLibrary(dir);
            int changes = 0;
            lib.Changed += () => changes++;
            var events = new List<TakeEvent> { TakeEvent.PianoOn(60, 0.8f), TakeEvent.Drum("snare", 0.7f, 0.95f, "rim", false), TakeEvent.Harp(7, 0.5f, 0.4f, 1.1f, "across") };
            events[1] = new TakeEvent { t = 0.5f, type = events[1].type, kind = events[1].kind, vel = events[1].vel, pos = events[1].pos, zone = events[1].zone };
            var first = lib.Put(new TakeRecord { name = lib.ReserveName(1.4), seconds = 1.4f, events = events, created = 1000 });
            foreach (object _ in Wait(first)) yield return null;
            var second = lib.Put(new TakeRecord { name = lib.ReserveName(9), seconds = 9f, events = new List<TakeEvent> { TakeEvent.PianoOn(72, 0.5f) }, created = 2000 });
            foreach (object _ in Wait(second)) yield return null;
            Assert.That(first.Result, Is.Not.Null); Assert.That(second.Result, Is.Not.Null);
            Assert.That(first.Result.id, Does.StartWith("take-"));
            Assert.That(changes, Is.EqualTo(2)); Assert.That(lib.Version, Is.EqualTo(2));
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(dir, "index.json")));
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(dir, first.Result.id + ".json")));
            Assert.That(System.IO.Directory.GetFiles(dir, "*.tmp").Length, Is.EqualTo(0), "atomic: no .tmp left");
            var list = lib.List();
            foreach (object _ in Wait(list)) yield return null;
            Assert.That(list.Result.Count, Is.EqualTo(2));
            Assert.That(list.Result[0].name, Is.EqualTo("Take 2 · 0:09"), "newest first");
            Assert.That(list.Result[1].count, Is.EqualTo(3));
            var got = lib.Get(first.Result.id);
            foreach (object _ in Wait(got)) yield return null;
            Assert.That(got.Result.name, Is.EqualTo("Take 1 · 0:01"));
            Assert.That(got.Result.events.Count, Is.EqualTo(3));
            Assert.That(got.Result.events[1].zone, Is.EqualTo("rim")); Assert.That(got.Result.events[1].t, Is.EqualTo(0.5f));
            Assert.That(got.Result.events[2].mode, Is.EqualTo("across")); Assert.That(got.Result.events[2].u, Is.EqualTo(0.4f));
            var gone = lib.Remove(second.Result.id);
            foreach (object _ in Wait(gone)) yield return null;
            Assert.IsTrue(gone.Result);
            Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(dir, second.Result.id + ".json")));
            // Reload: the deleted take's number is not handed out again (numbering quirk fixed, spec 08 section 8.4).
            var again = new TakeLibrary(dir);
            Assert.That(again.NextNumber, Is.EqualTo(3));
            var l2 = again.List();
            foreach (object _ in Wait(l2)) yield return null;
            Assert.That(l2.Result.Count, Is.EqualTo(1));
            var missing = again.Get("take-nope");
            foreach (object _ in Wait(missing)) yield return null;
            Assert.That(missing.Result, Is.Null, "never throws");
        }

        // Spec 08 section 4.4 "do the IO in Task.Run": a long take (20000 events, several MB of JSON) is serialised and
        // written on the worker, never in the frame STOP is pressed. The test holds the worker's work back: when Put
        // returns, nothing has been written, and a field changed afterwards still reaches the file, so the serialisation
        // itself ran inside the held work.
        [UnityTest]
        public IEnumerator ALongTakeIsSerialisedAndWrittenOnTheWorkerNotInTheCallingFrame()
        {
            string dir = NewDir();
            var lib = new TakeLibrary(dir);
            System.Action held = null;
            var gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
            lib.Worker = work => { held = work; return gate.Task; };
            var events = new List<TakeEvent>(Recorder.MaxEvents);
            for (int i = 0; i < Recorder.MaxEvents; i++) events.Add(TakeEvent.PianoOn(48 + i % 24, 0.5f));
            var take = new TakeRecord { name = lib.ReserveName(600), seconds = 600f, events = events };
            var put = lib.Put(take);
            Assert.That(held, Is.Not.Null, "the serialise-and-write work went to the worker");
            Assert.IsFalse(put.IsCompleted);
            Assert.That(System.IO.Directory.GetFiles(dir, "take-*").Length, Is.EqualTo(0), "nothing written in the calling frame");
            Assert.That(lib.Version, Is.EqualTo(0), "nor the index");
            take.seconds = 599f;
            var worker = System.Threading.Tasks.Task.Run(held);
            foreach (object _ in Wait(worker)) yield return null;
            Assert.IsFalse(worker.IsFaulted, worker.Exception?.ToString());
            gate.SetResult(true);
            foreach (object _ in Wait(put)) yield return null;
            Assert.That(put.Result, Is.Not.Null);
            var got = JsonUtility.FromJson<TakeRecord>(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, take.id + ".json")));
            Assert.That(got.seconds, Is.EqualTo(599f), "serialised in the worker, after Put had returned");
            Assert.That(got.events.Count, Is.EqualTo(Recorder.MaxEvents));
            Assert.That(lib.Version, Is.EqualTo(1));
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(dir, "index.json")));
        }

        [UnityTest]
        public IEnumerator AnUnwritableFolderTurnsSavingOff()
        {
            string dir = NewDir();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dir));
            System.IO.File.WriteAllText(dir, "a file where the folder should be");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"^\[Takes\] storage unavailable"));
            var lib = new TakeLibrary(dir);
            Assert.That(lib.Available, Is.False, "the menu says Saving off");
            var put = lib.Put(new TakeRecord { name = "Take 1", events = new List<TakeEvent>() });
            foreach (object _ in Wait(put)) yield return null;
            Assert.That(put.Result, Is.Null);
            var list = lib.List();
            foreach (object _ in Wait(list)) yield return null;
            Assert.That(list.Result.Count, Is.EqualTo(0));
            System.IO.File.Delete(dir);
        }
    }

    public class RecorderLimitTests
    {
        [Test]
        public void RecordingStopsAt20000EventsOr600Seconds()
        {
            double now = 0;
            var rec = new Recorder(() => now, null);
            List<TakeEvent> saved = null;
            rec.OnTake += (evs, s) => saved = evs;
            rec.Record();
            for (int i = 0; i < Recorder.MaxEvents; i++) rec.Add(TakeEvent.PianoOn(60, 0.5f));
            Assert.That(rec.State, Is.EqualTo(RecorderState.Recording));
            rec.Add(TakeEvent.PianoOn(61, 0.5f));
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle), "the 20001st event stops the take");
            Assert.That(saved.Count, Is.EqualTo(Recorder.MaxEvents));
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.5f));
            now += 600.5;
            rec.Add(TakeEvent.PianoOn(62, 0.5f));
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle), "past 600 s the take stops");
            Assert.That(saved.Count, Is.EqualTo(1), "the late event is not kept");
        }

        [Test]
        public void ObserversSeeEveryPlayFlaggedDuringPlaybackAndStateChangesArePublished()
        {
            double now = 0;
            var bus = new EventBus();
            var seen = new List<RecorderEvent>();
            var states = new List<RecorderState>();
            bus.Subscribe<RecorderEvent>(e => seen.Add(e));
            bus.Subscribe<RecorderStateEvent>(e => states.Add(e.State));
            var rec = new Recorder(() => now, bus);
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.5f));
            now += 1; rec.Stop();
            rec.SetTarget(TakeEventType.PianoOn, e => rec.Add(e));   // an instrument re-adds what it plays back
            Assert.IsTrue(rec.Play());
            rec.Update();
            Assert.That(seen.Count, Is.EqualTo(2));
            Assert.IsFalse(seen[0].DuringPlayback);
            Assert.IsTrue(seen[1].DuringPlayback, "lessons and Sunny ignore these");
            int released = 0;
            rec.SetReleaseAll(() => released++);
            Assert.IsTrue(rec.Play(), "PLAY while playing restarts");
            Assert.That(released, Is.EqualTo(1), "after releasing the held notes");
            rec.Record();
            Assert.That(released, Is.EqualTo(2), "REC during playback stops it first");
            // Record() = Stop() then Recording; a restart while playing publishes nothing new.
            Assert.That(states, Is.EqualTo(new[] { RecorderState.Idle, RecorderState.Recording, RecorderState.Idle, RecorderState.Playing, RecorderState.Idle, RecorderState.Recording }));
            Assert.IsFalse(rec.Play(), "PLAY ends the (empty) recording, which leaves an empty take: nothing to play");
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
        }
    }
}
