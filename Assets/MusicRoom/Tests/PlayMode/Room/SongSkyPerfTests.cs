// The cost of the song's night sky (Category Performance: `Tools/unity.sh test-perf`): the main-thread work a frame adds
// while a loop plays back with the sky full (256 stars, notes coming: each new star written into the one mesh, the
// shader's values set) and at rest (renderer off). Printed as a [PERF] line; the budget is a small share of an 11 ms frame
// (the playback show's budget: well under 0.3 ms). GPU: one draw (the field, up to 256 line and 256 star quads).
using System.Collections;
using System.Diagnostics;
using MusicRoom.Core;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Performance")]
    public class SongSkyPerfTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator TheNightSkyCostsLittleAFrame()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, SongSky.TipId + "," + ListenBackPulse.TipId);
            yield return _boot.Start();
            for (int i = 0; i < 60; i++) { _t += Dt; _boot.Step(Dt); }
            var room = App.Current.Get<IRoom>() as RoomModule;
            SongSky sky = room.SongSky;
            EventBus bus = App.Current.Events;
            var piano = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f, Lane = 1, Event = TakeEvent.PianoOn(60, 0.9f) };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 0.8f, Lane = 1, Event = TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false) };
            int frame = 0;
            // about 15 notes a second: a busy song
            void Notes() { frame++; if (frame % 6 == 0) { piano.Event.midi = 48 + frame % 36; bus.Publish(piano); } if (frame % 9 == 0) bus.Publish(drum); _t += Dt; }
            for (int i = 0; i < 72 * 30; i++) { Notes(); sky.Tick(Dt, true); }
            Assert.That(sky.Model.Live, Is.GreaterThan(SongSkyModel.Max / 2), "a full sky for the measurement");
            const int frames = 3000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) { Notes(); sky.Tick(Dt, true); }
            double playUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            // the end: linger, fade, rest
            bus.Publish(new RecorderStateEvent { State = RecorderState.Idle });
            for (int i = 0; i < 72 * 10; i++) { _t += Dt; sky.Tick(Dt, true); }
            Assert.IsTrue(sky.Model.Resting);
            sw.Restart();
            for (int i = 0; i < frames; i++) { _t += Dt; sky.Tick(Dt, true); }
            double restUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] night sky: playing (full sky, ~15 notes/s) {playUs:F1} us/frame; at rest {restUs:F1} us/frame " +
                      "(editor, Apple silicon; GPU: 1 draw of up to 513 small quads while it shows, 0 at rest)");
            Assert.That(playUs, Is.LessThan(150.0), "a small share of the 11 ms frame");
            Assert.That(restUs, Is.LessThan(20.0), "nothing to speak of at rest");
        }
    }
}
