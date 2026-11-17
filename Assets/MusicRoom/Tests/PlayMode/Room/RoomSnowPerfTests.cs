// The cost of the snow in the whole room (Category Performance: `Tools/unity.sh test-perf`): the main-thread work a frame
// adds (Room/Decor/RoomSnow.cs: the volume from the room's pose or about the head, the tint, ten material vectors, the
// Sunny check), measured on the booted app's own objects in the editor (Apple silicon), in the VR room and in mixed
// reality (the room hidden), while a recording plays back (gusts, tint) and calm; and off (nothing). Printed as a [PERF]
// line with the GPU side: one draw of RoomSnowModel.Count flakes (4 vertices each, the maths in the vertex shader); the
// flakes' share of the picture is measured by RoomSnowShotsTests (--gpu).
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
    public class RoomSnowPerfTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator TheRoomsSnowCostsLittleAFrame()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_song_stars,tip_paint_walls,tip_paint_keep,tip_paint_throw,tip_paint_gallery,tip_ghost_hands,tip_window_show," + RoomSnow.TipId);
            yield return _boot.Start();
            for (int i = 0; i < 60; i++) { _t += Dt; _boot.Step(Dt); }
            var room = App.Current.Get<IRoom>() as RoomModule;
            RoomSnow snow = room.RoomSnow;
            WindowShow show = room.WindowShow;
            const int frames = 5000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) { _t += Dt; snow.Tick(Dt, true); }
            double offUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;

            room.Set(AmbienceKind.Snow, true);
            room.Weather.Settle(1f, 0.5f, room.Weather.TimeD);
            for (int i = 0; i < 10; i++) { _t += Dt; _boot.Step(Dt); }
            Assert.IsTrue(snow.Falling);
            sw.Restart();
            for (int i = 0; i < frames; i++) { _t += Dt; snow.Tick(Dt, true); }
            double calmUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;

            App.Current.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 7 });
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f, Lane = 1, Event = TakeEvent.PianoOn(67, 0.9f) };
            int frame = 0;
            void Notes() { frame++; if (frame % 8 == 0) { show.Model.Note(on, _t); snow.Model.Note(on, _t); } _t += Dt; show.Tick(Dt, true); }
            for (int i = 0; i < 300; i++) { Notes(); snow.Tick(Dt, true); }
            Assert.That(snow.Gust, Is.GreaterThan(0f));
            sw.Restart();
            for (int i = 0; i < frames; i++) { Notes(); snow.Tick(Dt, true); }
            double vrUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;   // includes the window show's own tick
            sw.Restart();
            for (int i = 0; i < frames; i++) { Notes(); }
            double showUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            sw.Restart();
            for (int i = 0; i < frames; i++) { Notes(); snow.Tick(Dt, false); }
            double mrUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            App.Current.Events.Publish(new RecordingPlaybackEvent { Started = false });
            double vrOwn = vrUs - showUs, mrOwn = mrUs - showUs;
            Debug.Log($"[PERF] room snow: off {offUs:F2} us/frame, snowing (VR, calm) {calmUs:F2} us/frame, with a playback (VR) {vrOwn:F2} us/frame, " +
                      $"(MR, room hidden) {mrOwn:F2} us/frame (the window show's own tick {showUs:F2} us excluded); GPU: 1 draw, {RoomSnowModel.Count} flakes, " +
                      $"{RoomSnowModel.Count * 4} vertices, {RoomSnowModel.Count * 2} triangles (editor, Apple silicon)");
            Assert.That(System.Math.Max(calmUs, System.Math.Max(vrOwn, mrOwn)), Is.LessThan(60.0), "a small share of the 0.3 ms budget");
            Assert.That(offUs, Is.LessThan(10.0), "nothing to speak of when SNOW is off");
        }
    }
}
