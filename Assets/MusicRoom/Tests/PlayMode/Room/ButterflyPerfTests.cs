// The cost of the room coming alive (Category Performance: `Tools/unity.sh test-perf`): the main-thread work a frame adds
// while music is recorded or played back, measured on the booted app's own objects in the editor (Apple silicon):
//   - the listen-back pulse alone (levels, motes, lamp, flicker gains, the flowers' glow and sway global) with notes coming;
//   - the same plus the butterflies: twelve out, their flock (steering, the head / hand / keys constraints, perches) and
//     their mesh (96 vertices written into one NativeArray, SetVertexBufferData, bounds);
//   - the butterflies at rest (none out: the renderer off, the flock idle).
// Printed as [PERF] lines; the budgets are a small share of an 11 ms frame. The GPU side is one draw of 24 alpha-blended
// quads while they are out (none at rest) and one static draw for every petal (ButterflyShotsTests logs the seat's draws).
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
    public class ButterflyPerfTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator TheRoomComingAliveCostsLittleAFrame()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, ListenBackPulse.TipId);
            yield return _boot.Start();
            for (int i = 0; i < 60; i++) { _t += Dt; _boot.Step(Dt); }
            var room = App.Current.Get<IRoom>() as RoomModule;
            ListenBackPulse pulse = room.ListenBack;
            Butterflies flies = room.Butterflies;
            EventBus bus = App.Current.Events;
            // A loop playing the piano and the drums back (the butterflies' notes), an overdub's live notes on top (the lights').
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 1f, Lane = 1 };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 0.9f, Lane = 1 };
            var live = new RecordedNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.8f };
            int frame = 0;
            void Notes() { frame++; if (frame % 8 == 0) bus.Publish(on); if (frame % 11 == 0) bus.Publish(drum); if (frame % 13 == 0) bus.Publish(live); _t += Dt; }
            // warm: all twelve out
            for (int i = 0; i < 1200; i++) { Notes(); pulse.Tick(Dt, true); flies.Tick(Dt, true); }
            Assert.That(flies.Flock.Alive, Is.EqualTo(ButterflyFlock.Max), "twelve out for the measurement");
            const int frames = 3000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) { Notes(); pulse.Tick(Dt, true); }
            double pulseUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            sw.Restart();
            for (int i = 0; i < frames; i++) { Notes(); pulse.Tick(Dt, true); flies.Tick(Dt, true); }
            double bothUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Assert.That(flies.Flock.Alive, Is.EqualTo(ButterflyFlock.Max));
            // at rest: no notes for long enough that they have gone
            for (int i = 0; i < 72 * 12; i++) { _t += Dt; pulse.Tick(Dt, true); flies.Tick(Dt, true); }
            Assert.That(flies.Flock.Alive, Is.EqualTo(0));
            sw.Restart();
            for (int i = 0; i < frames; i++) { _t += Dt; flies.Tick(Dt, true); }
            double restUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] room alive: pulse {pulseUs:F1} us/frame; pulse + 12 butterflies {bothUs:F1} us/frame (butterflies {bothUs - pulseUs:F1} us); " +
                      $"butterflies at rest {restUs:F1} us/frame (editor, Apple silicon; GPU: 1 draw of 24 quads while out, 0 at rest)");
            Assert.That(bothUs - pulseUs, Is.LessThan(150.0), "twelve butterflies: a small share of the 11 ms frame");
            Assert.That(restUs, Is.LessThan(30.0), "nothing to speak of at rest");
        }
    }
}
