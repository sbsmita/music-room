// The looper's cost (Tools/unity.sh test-perf, category Performance): six busy layers at 110 BPM (16th-note drums, piano
// chords, harp plucks: 240 events a pass, 55 a second) looping on the real audio clock in the booted app. Measured: the
// Recorder phase (the looper's Tick: clicks, look-ahead sends through ScheduleAt into the real instruments) per frame,
// and the audio thread per block (SynthSource stats) with no dropped commands. [PERF] lines go to the log.
using System.Collections;
using System.Diagnostics;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.Lessons
{
    [Category("Performance")]
    public class LooperLoadTests
    {
        sealed class Stamp : AppModule
        {
            readonly int _order;
            public readonly Stopwatch Watch;
            public long Ticks, Frames, Max, BusyTicks, BusyFrames;
            public long BusyThreshold;
            public Stamp(int order, Stopwatch w) { _order = order; Watch = w; }
            public override int Order => _order;
            public override void Tick(FramePhase phase, float dt)
            {
                if (phase != FramePhase.Recorder) return;
                if (_order < LessonsModule.ModuleOrder) { Watch.Restart(); return; }
                Watch.Stop();
                long t = Watch.ElapsedTicks;
                Ticks += t;
                Frames++;
                if (t > Max) Max = t;
                if (t > BusyThreshold) { BusyTicks += t; BusyFrames++; }
            }
        }

        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest, Category("Performance")]
        public IEnumerator SixBusyLayersLoopingCostLittlePerFrameAndPerBlock()
        {
            var watch = new Stopwatch();
            var before = new Stamp(LessonsModule.ModuleOrder - 1, watch);
            var after = new Stamp(LessonsModule.ModuleOrder + 1, watch);
            _boot = new TestBoot(headless: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(GuideModule.KeyTipsSeen, AppLoopHost.TipLoopStart);
            _boot.Options.ExtraModules.Add(before);
            _boot.Options.ExtraModules.Add(after);
            yield return _boot.Start();
            var engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            float t0 = Time.realtimeSinceStartup;
            while (engine.RenderSeconds < 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;   // tables and buffers ready
            int sr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
            var s = new LoopSession(110, 2, sr);
            double sixteenth = s.BeatSamples / 4.0;
            string[] kit = { "kick", "hat", "snare", "hat" };
            int[] chord = { 48, 55, 60, 64 };
            for (int layer = 0; layer < Looper.MaxLayers; layer++)
            {
                var l = new LoopLayer();
                for (int k = 0; k < 32; k++)
                {
                    long at = (long)(k * sixteenth);
                    if (layer < 2) l.Insert(at, TakeEvent.Drum(kit[(k + layer) % 4], 0.6f, 0.3f, "head", false));
                    else if (layer < 4)
                    {
                        if (k % 4 == 0) l.Insert(at, TakeEvent.PianoOn(chord[(k / 4 + layer) % 4] + 12 * (layer - 2), 0.5f));
                        if (k % 4 == 3) l.Insert(at, TakeEvent.PianoOff(chord[(k / 4 + layer) % 4] + 12 * (layer - 2)));
                    }
                    else l.Insert(at, TakeEvent.Harp((k * 3 + layer) % 22, 0.5f, 0.35f, 0f, "through"));
                }
                s.Layers.Add(l);
            }
            LessonsModule m = null;
            foreach (IAppModule x in App.Current.Modules) if (x is LessonsModule l) m = l;
            Assert.IsTrue(m.Recorder.PlayEvents(LoopFormat.Encode(s), s.PassSeconds));
            m.Looper.ToggleMetronome();
            float warm = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warm < 2f) yield return null;   // every path JIT-compiled, two passes sent
            engine.ResetBlockStats();
            after.BusyThreshold = (long)(2e-6 * Stopwatch.Frequency);             // frames that sent something (> 2 us)
            before.Frames = after.Frames = 0; after.Ticks = after.Max = after.BusyTicks = after.BusyFrames = 0;
            int dropped0 = engine.TotalDroppedCommands;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 6f) yield return null;
            double us = 1e6 / Stopwatch.Frequency;
            double avg = after.Frames > 0 ? after.Ticks * us / after.Frames : 0, max = after.Max * us;
            double busy = after.BusyFrames > 0 ? after.BusyTicks * us / after.BusyFrames : 0;
            double perSecond = after.Ticks * us / 6.0;
            Debug.Log($"[PERF] looper: 6 layers x 240 events/pass at 110 BPM (55 events/s + clicks): Recorder phase {perSecond:F0} us per second of play "
                + $"({perSecond / 72.0:F1} us per 72 Hz frame), {busy:F1} us avg in the {after.BusyFrames} frames that sent events, max {max:F1} us, over {after.Frames} editor frames "
                + "(clicks, look-ahead sends with ScheduleAt into the real piano / drums / harp; batchmode frames are ~0.2 ms, the audio clock steps 5.33 ms)");
            Debug.Log($"[PERF] looper audio: sum of per-source avg {engine.SumAvgBlockMicros:F0} us, sum of max {engine.SumMaxBlockMicros:F0} us per block, dropped {engine.TotalDroppedCommands - dropped0}, stolen {engine.TotalStolenVoices}, active voices {engine.TotalActiveVoices}");
            Assert.That(after.Frames, Is.GreaterThan(30));
            Assert.That(avg, Is.LessThan(1000.0), "the looper's frame work is small (editor Mono)");
            Assert.That(engine.TotalDroppedCommands - dropped0, Is.EqualTo(0), "no command lost on the way to the audio thread");
            m.Recorder.Stop();
        }
    }
}
