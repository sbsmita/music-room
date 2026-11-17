// The ghost hands' cost (Category Performance: `Tools/unity.sh test-perf`): the main-thread work a frame adds while a
// recording plays back, measured on the booted app's own objects in the editor (Apple silicon):
//   - six hands playing at once (the piano's two with chords and a melody, the drums' two with sticks, the harp's two):
//     reading the playback ahead, planning (fingering, strokes), posing (finger IK, sticks), the player-hand and head
//     fades, and writing the one mesh (about 1000 vertices a hand) and uploading it;
//   - at rest (no playback): nothing to do.
// Printed as [PERF] lines. Budget: well under 0.3 ms a frame. The GPU side is one renderer (a depth pass and the
// translucent pass) while hands are shown, none at rest.
using System.Collections;
using System.Diagnostics;
using MusicRoom.Core;
using MusicRoom.Lessons.Ghost;
using NUnit.Framework;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Performance")]
    public class GhostHandsPerfTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 10;

        [UnityTearDown] public IEnumerator TearDown() { if (_boot != null) yield return _boot.Stop(); _boot = null; }

        [UnityTest]
        public IEnumerator GhostHandsCostLittleAFrame()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "all");
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_ghost_hands");
            yield return _boot.Start();
            for (int i = 0; i < 60; i++) { _t += Dt; _boot.Step(Dt); }
            var take = GhostHandsPlayTests.PianoSong(0.3f, 60);
            take.AddRange(GhostHandsPlayTests.DrumBeat(0.3f, 60));
            take.AddRange(GhostHandsPlayTests.HarpRun(0.3f, 200));
            take.Sort((a, c) => a.t.CompareTo(c.t));
            IRecorder rec = App.Current.Get<IRecorder>();
            Assert.IsTrue(rec.PlayEvents(take, 80));
            for (int i = 0; i < 72 * 3; i++) { _t += Dt; _boot.Step(Dt); }   // the instruments arrive, every hand has played
            GhostHands g = GhostHandsModule.Of(App.Current);
            for (int i = 0; i < 200; i++) { _t += Dt; g.Tick(Dt); }
            Assert.That(g.ShownHands, Is.GreaterThanOrEqualTo(4), "the hands of the instruments the playback brought (as many as fit together)");
            const int frames = 2000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) { _t += Dt; g.Tick(Dt); }
            double playUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            int shown = g.ShownHands;
            rec.Stop();
            for (int i = 0; i < 72 * 2; i++) { _t += Dt; g.Tick(Dt); }
            Assert.That(g.ShownHands, Is.EqualTo(0));
            sw.Restart();
            for (int i = 0; i < frames; i++) { _t += Dt; g.Tick(Dt); }
            double restUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] ghost hands: {shown} hands playing {playUs:F1} us/frame ({playUs / shown:F1} us a hand); at rest {restUs:F1} us/frame " +
                      $"(editor, Apple silicon; GPU: 1 renderer, depth + translucent pass, {g.Mesh.VerticesPerHand} vertices a hand, none at rest)");
            Assert.That(playUs, Is.LessThan(150.0), "well under 0.3 ms a frame (measured 71 us for 4 hands)");
            Assert.That(restUs, Is.LessThan(15.0), "nothing to speak of at rest");
        }
    }
}
