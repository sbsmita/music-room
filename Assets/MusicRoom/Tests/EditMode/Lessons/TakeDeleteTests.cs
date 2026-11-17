// Deleting a recording keeps the looper and the take library consistent: TakeLibrary.Remove tells its listeners first
// (Removing, in the caller's frame, before the index changes), and the Lessons module lets go of a loop session saved under
// that id (LessonsModule.DetachLoop: no id, no number, nothing unsaved), so stopping a deleted loop or changing it never
// writes the deleted take back; a later change saves it as a new recording.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Lessons
{
    public class TakeDeleteTests
    {
        const int Sr = 48000;
        const long Beat90 = 32000, Pass90x2 = 8 * Beat90;
        FakeLoopHost _host;
        Looper _loop;
        Recorder _rec;
        readonly List<LoopSession> _saved = new List<LoopSession>();

        [SetUp]
        public void SetUp()
        {
            _host = new FakeLoopHost();
            _loop = new Looper(_host) { InputLatency = 0, Tidy = false };
            _rec = new Recorder(() => _host.T, null) { Looper = _loop };
            _saved.Clear();
            // As the module saves: the first save gives the session its record id and number.
            _loop.Save += s =>
            {
                if (string.IsNullOrEmpty(s.Id)) { s.Id = "take-" + (_saved.Count + 1); s.Number = _saved.Count + 3; }
                _saved.Add(s);
            };
        }

        void TickTo(long sample)
        {
            double end = sample / (double)Sr;
            while (_host.T < end - 1e-12) { _host.T = System.Math.Min(end, _host.T + 1.0 / 72.0); _loop.Tick(); }
        }

        /// One layer of kick, looped, stopped: saved once as "take-1".
        LoopSession SavedLoop()
        {
            _rec.Loop();
            long a = _loop.ClickStart + 4 * Beat90;
            TickTo(a);
            _loop.Capture(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            TickTo(a + Pass90x2 + 10);
            _rec.Stop();
            Assert.That(_saved.Count, Is.EqualTo(1), "STOP saved the session");
            return _saved[0];
        }

        [Test]
        public void ADeletedDeckLoopIsNeverWrittenBackAndAChangeSavesItAsNew()
        {
            LoopSession s = SavedLoop();
            Assert.That(s.Id, Is.EqualTo("take-1"));
            Assert.That(_loop.DeckSession, Is.SameAs(s));
            LessonsModule.DetachLoop(_loop, "take-other");
            Assert.That(s.Id, Is.EqualTo("take-1"), "another id leaves it alone");

            // PLAY loops it again, then it is deleted (from Recordings) while it plays; STOP saves nothing.
            Assert.IsTrue(_rec.Play());
            _loop.ToggleMute(0);                 // an unsaved change
            Assert.IsTrue(s.Dirty);
            LessonsModule.DetachLoop(_loop, "take-1");
            Assert.That(s.Id, Is.Null);
            Assert.That(s.Number, Is.EqualTo(0));
            Assert.IsFalse(s.Dirty, "nothing unsaved: the deleted take does not come back");
            _rec.Stop();
            Assert.That(_saved.Count, Is.EqualTo(1), "stopping it writes nothing");

            // A later change (unmute, then stop) is a new recording with a new id and number.
            Assert.IsTrue(_rec.Play());
            _loop.ToggleMute(0);
            _rec.Stop();
            Assert.That(_saved.Count, Is.EqualTo(2));
            Assert.That(s.Id, Is.EqualTo("take-2"), "a new record, never the deleted one");
        }

        [Test]
        public void ADeletedSavedLoopPlayingFromRecordingsLetsGoToo()
        {
            var other = new LoopSession(70, 2, Sr) { Id = "take-9", Number = 9 };
            var l = new LoopLayer(); l.Insert(1000, TakeEvent.PianoOn(72, 0.5f)); other.Layers.Add(l);
            Assert.IsTrue(_rec.PlayEvents(LoopFormat.Encode(other), other.PassSeconds));
            Assert.That(_loop.ExternalSession.Id, Is.EqualTo("take-9"));
            _loop.ToggleMute(0);
            LessonsModule.DetachLoop(_loop, "take-9");
            Assert.That(_loop.ExternalSession.Id, Is.Null);
            _rec.Stop();
            Assert.That(_saved.Count, Is.EqualTo(0), "the deleted take is not saved back on STOP");
        }

        [UnityTest]
        public IEnumerator RemoveTellsItsListenersBeforeTheIndexChanges()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mr-takedelete-" + System.Guid.NewGuid().ToString("N"));
            var lib = new TakeLibrary(dir);
            var put = lib.Put(new TakeRecord { name = lib.ReserveName(1), seconds = 1f, events = new List<TakeEvent> { TakeEvent.PianoOn(60, 0.5f) }, created = 1000 });
            float t0 = Time.realtimeSinceStartup;
            while (!put.IsCompleted && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            string id = put.Result.id;
            var seen = new List<string>();
            int countThen = -1, version = lib.Version;
            lib.Removing += x => { seen.Add(x); countThen = lib.List().Result.Count; };
            var gone = lib.Remove(id);
            Assert.That(seen, Is.EqualTo(new[] { id }), "told at once, in the caller's frame");
            Assert.That(countThen, Is.EqualTo(1), "before anything changed");
            t0 = Time.realtimeSinceStartup;
            while (!gone.IsCompleted && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            Assert.IsTrue(gone.Result);
            Assert.That(lib.Version, Is.GreaterThan(version));
            Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(dir, id + ".json")), "the file is gone");
            var again = new TakeLibrary(dir);
            var list = again.List();
            Assert.That(list.Result.Count, Is.EqualTo(0), "and stays gone after a reload");
            try { System.IO.Directory.Delete(dir, true); } catch (System.Exception) { }
        }
    }
}
