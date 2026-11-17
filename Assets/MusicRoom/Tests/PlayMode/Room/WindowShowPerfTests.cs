// The cost of the snowy window joining in (Category Performance: `Tools/unity.sh test-perf`): the main-thread work a frame
// adds while a recording plays back (Room/Decor/WindowShow.cs: the notes into the model, its envelopes, six shader globals,
// the gaze test for Sunny), measured on the booted app's own objects in the editor (Apple silicon), with the room shown
// (VR) and hidden (mixed reality: no window there since 2026-11-13, the model still runs); and at rest (nothing written).
// Printed as a [PERF] line. The GPU side adds no draw: the flakes' swirl is a few more vertex instructions in the snow's
// one draw, the lights a branch-guarded term in the night sky's fragments while the show runs, nothing at rest.
using System.Collections;
using System.Diagnostics;
using MusicRoom.Core;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Performance")]
    public class WindowShowPerfTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator TheWindowJoiningInCostsLittleAFrame()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_song_stars,tip_paint_walls,tip_paint_keep,tip_ghost_hands," + WindowShow.TipId);
            yield return _boot.Start();
            for (int i = 0; i < 60; i++) { _t += Dt; _boot.Step(Dt); }
            var room = App.Current.Get<IRoom>() as RoomModule;
            room.Set(AmbienceKind.Snow, true);
            WindowShow show = room.WindowShow;
            EventBus bus = App.Current.Events;
            bus.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 7, Loop = true });
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f, Lane = 1, Event = TakeEvent.PianoOn(67, 0.9f) };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 0.8f, Lane = 1, Event = TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false) };
            int frame = 0;
            void Notes() { frame++; if (frame % 8 == 0) bus.Publish(on); if (frame % 11 == 0) bus.Publish(drum); _t += Dt; }
            for (int i = 0; i < 600; i++) { Notes(); show.Tick(Dt, true); }
            Assert.That(show.Model.Aurora, Is.GreaterThan(0.5f), "the lights are up for the measurement");
            const int frames = 5000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) { Notes(); show.Tick(Dt, true); }
            double vrUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            sw.Restart();
            for (int i = 0; i < frames; i++) { Notes(); show.Tick(Dt, false); }
            double mrUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            bus.Publish(new RecordingPlaybackEvent { Started = false });
            for (int i = 0; i < 72 * 12; i++) { _t += Dt; show.Tick(Dt, true); }
            Assert.IsTrue(show.Model.Resting);
            sw.Restart();
            for (int i = 0; i < frames; i++) { _t += Dt; show.Tick(Dt, true); }
            double restUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] window show: playing (VR) {vrUs:F2} us/frame, playing (MR, room hidden) {mrUs:F2} us/frame, at rest {restUs:F2} us/frame " +
                      "(editor, Apple silicon; GPU: no extra draw)");
            Assert.That(System.Math.Max(vrUs, mrUs), Is.LessThan(60.0), "a small share of the 0.3 ms budget");
            Assert.That(restUs, Is.LessThan(10.0), "nothing to speak of at rest");
        }
    }
}
