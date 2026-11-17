// The cost of the notes painting the walls (Category Performance: `Tools/unity.sh test-perf`): the main-thread work a
// frame adds, measured on the booted app's own WallPaint in the editor (Apple silicon):
//   - painting: a busy playback (piano and drums, about 25 onsets a second, the paint's budget of 14 a second: the piano's
//     flowering vine (stem pieces, a stalk, a flower and its heart, a leaf per note) and the drums' grass and pebbles on the
//     room's canvas, 4 vertices per quad, one partial vertex upload, the uniforms, both hands read for a throw);
//   - holding and fading (no notes: the fade uniforms only);
//   - at rest (nothing on the walls: the renderers off).
// Printed as [PERF] lines; the budget is well under 0.3 ms a frame. The GPU side: one draw of small alpha quads (at most
// 2400) while a painting shows (two while it swirls into a frame), none at rest; the room's canvas is read once at boot (logged by [Room] wall paint).
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
    public class WallPaintPerfTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator PaintingTheWallsCostsLittleAFrame()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, WallPaint.TipId + "," + WallPaint.KeepTipId + "," + WallPaint.ThrowTipId + "," + WallPaint.GalleryTipId);
            yield return _boot.Start();
            for (int i = 0; i < 60; i++) { _t += Dt; _boot.Step(Dt); }
            var room = App.Current.Get<IRoom>() as RoomModule;
            WallPaint paint = room.WallPaint;
            paint.FinishAtlas();
            EventBus bus = App.Current.Events;
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f, Lane = PlaybackLanes.Take, Event = TakeEvent.PianoOn(64, 0.9f) };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 0.8f, Lane = PlaybackLanes.Take, Event = TakeEvent.Drum("snare", 0.8f, 0.2f, "head", false) };
            int frame = 0;
            void Notes()
            {
                frame++;
                if (frame % 3 == 0) { on.Event.midi = 48 + frame % 37; bus.Publish(on); }
                if (frame % 5 == 0) bus.Publish(drum);
                _t += Dt;
            }
            bus.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 3u });
            for (int i = 0; i < 1200; i++) { Notes(); paint.Tick(Dt, true); }   // warm: the ring full
            const int frames = 3000;
            int painted = paint.Painted;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) { Notes(); paint.Tick(Dt, true); }
            double paintUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            int perSecond = (int)((paint.Painted - painted) / (frames * Dt));
            bus.Publish(new RecordingPlaybackEvent { Started = false });
            int fadeFrames = (int)((WallPaint.HoldSeconds + WallPaint.FadeSeconds) * 72f);
            sw.Restart();
            for (int i = 0; i < fadeFrames; i++) { _t += Dt; paint.Tick(Dt, true); }
            double fadeUs = sw.Elapsed.TotalMilliseconds * 1000.0 / fadeFrames;
            for (int i = 0; i < 72; i++) { _t += Dt; paint.Tick(Dt, true); }
            Assert.That(paint.Splashes, Is.EqualTo(0));
            sw.Restart();
            for (int i = 0; i < frames; i++) { _t += Dt; paint.Tick(Dt, true); }
            double restUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] wall paint: painting {paintUs:F1} us/frame ({perSecond} notes painted a second, {WallPaint.MaxQuads} quads in the ring); " +
                      $"holding + fading {fadeUs:F1} us/frame; at rest {restUs:F1} us/frame (editor, Apple silicon; GPU: 1 draw while a painting shows, 0 at rest)");
            Assert.That(paintUs, Is.LessThan(150.0), "a busy playback: well under the 0.3 ms budget");
            Assert.That(fadeUs, Is.LessThan(30.0));
            Assert.That(restUs, Is.LessThan(10.0), "nothing to speak of at rest");
        }
    }
}
