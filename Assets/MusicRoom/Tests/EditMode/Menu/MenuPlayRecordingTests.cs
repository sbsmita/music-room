// Recordings > a recording > Play never does nothing (2026-11-16 headset test: "few recordings i am unable to play. i click
// on it but nothing happens"). Play pressed while the recording still loads plays it once it has loaded (it was dropped);
// a recording whose file cannot be read says so on its Play row ("Can't open this one", it read "Loading..." for ever); a
// recording the recorder refuses says so too ("Can't play this one"); every press logs one line with what came of it.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Menu
{
    public class MenuPlayRecordingTests
    {
        // A library whose Get answers only when the test says so (a slow read), or never finds the file.
        sealed class GatedTakes : ITakeLibrary
        {
            public readonly FakeTakes Inner = new FakeTakes();
            public TaskCompletionSource<TakeRecord> Gate;
            public bool Unreadable;
            public bool? Available => Inner.Available;
            public int Version => Inner.Version;
            public event Action Changed { add => Inner.Changed += value; remove => Inner.Changed -= value; }
            public Task<List<TakeInfo>> List() => Inner.List();
            public Task<TakeRecord> Get(string id)
            {
                if (Unreadable) return Task.FromResult<TakeRecord>(null);
                if (Gate != null) return Gate.Task;
                return Inner.Get(id);
            }
            public Task<TakeRecord> Put(TakeRecord take) => Inner.Put(take);
            public Task<bool> Remove(string id) => Inner.Remove(id);
            public string FormatLength(double seconds) => Inner.FormatLength(seconds);
            public string FormatDate(long createdMs) => Inner.FormatDate(createdMs);
        }

        sealed class RefusingRecorder : IRecorder
        {
            public RecorderState State => RecorderState.Idle;
            public bool External => false;
            public List<TakeEvent> Take => null;
            public double TakeSeconds => 0;
            public int Asked;
#pragma warning disable 67
            public event Action<List<TakeEvent>, double> OnTake;
#pragma warning restore 67
            public void Add(in TakeEvent ev) { }
            public void Record() { }
            public bool Play() => false;
            public bool PlayEvents(List<TakeEvent> events, double seconds) { Asked++; return false; }
            public void Stop() { }
            public string LastPlay => "no instrument here plays its notes";
        }

        SynchronizationContext _ctx;

        // The library's answer continues the menu's await at once (no editor update in between).
        [SetUp] public void SetUp() { _ctx = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null); }
        [TearDown] public void TearDown() => SynchronizationContext.SetSynchronizationContext(_ctx);

        static Rig WithTake(GatedTakes lib, out TakeRecord t)
        {
            var r = new Rig();
            t = lib.Inner.Add("Loop 29 · 90 BPM · 3 layers", 1000, 5.3f, 6);
            r.Ctl.Takes = lib;
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            return r;
        }

        static int TakeRow(Rig r, TakeRecord t) => r.RowNamed(RowKind.Take, x => x.Take != null && x.Take.id == t.id);
        static string PlayNote(Rig r) => r.Ctl.Row(r.RowNamed(RowKind.Play)).Note;

        [Test]
        public void PlayPressedWhileTheRecordingLoadsPlaysItOnceLoaded()
        {
            var lib = new GatedTakes { Gate = new TaskCompletionSource<TakeRecord>() };
            Rig r = WithTake(lib, out TakeRecord t);
            r.Tap(TakeRow(r, t));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Take));
            Assert.That(r.Ctl.TakeRecord, Is.Null, "still loading");
            LogAssert.Expect(LogType.Log, new Regex(@"\[Menu\] Play 'Loop 29.*still loading"));
            r.Tap(r.RowNamed(RowKind.Play));
            Assert.That(r.Recorder.Played, Is.Null, "nothing to play yet");
            lib.Gate.SetResult(t);                                   // the file has been read
            r.Frame();
            Assert.That(r.Recorder.Played, Is.SameAs(t.events), "the press was kept: it plays once loaded");
            Assert.That(r.Recorder.State, Is.EqualTo(RecorderState.Playing));
            Assert.That(r.Ctl.PlayingTakeId, Is.EqualTo(t.id));
            Assert.That(PlayingTake.Id, Is.EqualTo(t.id), "the gallery knows which recording plays");
        }

        [Test]
        public void ARecordingThatCannotBeReadSaysSoOnItsPlayRow()
        {
            var lib = new GatedTakes { Unreadable = true };
            Rig r = WithTake(lib, out TakeRecord t);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Menu\] 'Loop 29.*could not be opened"));
            r.Tap(TakeRow(r, t));
            Assert.That(PlayNote(r), Is.EqualTo(MenuStyle.CantOpen), "not \"Loading...\" for ever");
            LogAssert.Expect(LogType.Log, new Regex(@"\[Menu\] Play 'Loop 29.*nothing played"));
            r.Tap(r.RowNamed(RowKind.Play));
            Assert.That(r.Recorder.Played, Is.Null);
            Assert.That(PlayNote(r), Is.EqualTo(MenuStyle.CantOpen));
            Assert.IsTrue(r.Paint().Has(MenuStyle.CantOpen), "drawn on the Play row");
        }

        [Test]
        public void ARecordingTheRecorderRefusesSaysSo()
        {
            var lib = new GatedTakes();
            Rig r = WithTake(lib, out TakeRecord t);
            var refusing = new RefusingRecorder();
            r.Ctl.Recorder = refusing;
            r.Tap(TakeRow(r, t));
            Assert.That(r.Ctl.TakeRecord, Is.Not.Null);
            Assert.That(PlayNote(r), Is.Null.Or.Empty);
            LogAssert.Expect(LogType.Log, new Regex(@"\[Menu\] Play 'Loop 29.*nothing played: no instrument here plays its notes"));
            r.Tap(r.RowNamed(RowKind.Play));
            Assert.That(refusing.Asked, Is.EqualTo(1));
            Assert.That(PlayNote(r), Is.EqualTo(MenuStyle.CantPlay));
        }

        [Test]
        public void APlayThatPlaysLogsWhatItPlays()
        {
            var lib = new GatedTakes();
            Rig r = WithTake(lib, out TakeRecord t);
            r.Tap(TakeRow(r, t));
            LogAssert.Expect(LogType.Log, new Regex(@"\[Menu\] Play 'Loop 29.*plays"));
            r.Tap(r.RowNamed(RowKind.Play));
            Assert.That(r.Recorder.Played, Is.SameAs(t.events));
            Assert.That(PlayNote(r), Is.Null.Or.Empty);
        }
    }
}
