// The piano in the running app plays the classic sound (today's PianoSynth and its rendered buffers; the only piano sound
// since 2026-11-10): the real Piano module (press API, automatic sustain, a lesson's melody) plays through IAudioEngine on
// the real audio thread; AudioTestTap sees the calls, a capture filter after each piano SynthSource (before the
// spatializer: spatializePostEffects) records what the sources really output. Checked: every press reaches the engine on
// today's voices, level, no NaN / Inf, and the press path allocates nothing. [PIANO-PLAY] lines carry the numbers (the
// click metric is logged: today's dampers are the reference sound).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Piano
{
    public class PianoClassicPlayTests
    {
        /// <summary>Records channel 0 of the source's block (the SynthSource's mono output), allocation-free on the audio thread.</summary>
        sealed class Capture : MonoBehaviour
        {
            public float[] Buf = new float[48000 * 60];
            int _n, _on;
            public int Count => Volatile.Read(ref _n);
            public void Begin() { Volatile.Write(ref _n, 0); Volatile.Write(ref _on, 1); }
            public void End() => Volatile.Write(ref _on, 0);
            void OnAudioFilterRead(float[] data, int channels)
            {
                if (Volatile.Read(ref _on) == 0) return;
                int n = _n;
                for (int i = 0; i < data.Length && n < Buf.Length; i += channels) Buf[n++] = data[i];
                Volatile.Write(ref _n, n);
            }
        }

        TestBoot _boot;
        App _app;
        AudioEngine _engine;
        readonly Capture[] _cap = new Capture[3];
        int _bufFrames;

        IEnumerator Boot(Func<Type, bool> filter)
        {
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = filter;
            yield return _boot.Start();
            _app = App.Current;
            _engine = (AudioEngine)_app.Get<IAudioEngine>();
            Assert.IsNull(_engine.Unit(UnitId.Piano), "the app creates no piano unit");
            Assert.IsNull(_engine.ActiveUnit(UnitId.Piano), "today's piano plays");
            yield return Until(() => _engine.RenderSeconds >= 0, 30f);
            Assert.That(_engine.RenderSeconds, Is.GreaterThanOrEqualTo(0.0), "every piano key's buffer rendered");
            var ids = new[] { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi };
            for (int i = 0; i < 3; i++) _cap[i] = _engine.Source(ids[i]).gameObject.AddComponent<Capture>();
            AudioSettings.GetDSPBufferSize(out _bufFrames, out _);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_engine != null) _engine.TestTap = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        static IEnumerator Until(Func<bool> done, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        static IEnumerator Wait(double seconds)
        {
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - t0 < seconds) yield return null;
        }

        static void Scan(float[] y, int n, out float peak, out int bad, out double rms)
        {
            peak = 0; bad = 0; rms = 0;
            for (int i = 0; i < n; i++)
            {
                float v = y[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { bad++; continue; }
                peak = Mathf.Max(peak, Mathf.Abs(v));
                rms += (double)v * v;
            }
            rms = Math.Sqrt(rms / Math.Max(1, n));
        }

        static float MaxD2(float[] y, int from, int to)
        {
            float m = 0;
            for (int i = Math.Max(2, from); i < to; i++) m = Mathf.Max(m, Mathf.Abs(y[i] - 2 * y[i - 1] + y[i - 2]));
            return m;
        }

        static float MaxStep(float[] y, int from, int to)
        {
            float m = 0;
            for (int i = Math.Max(1, from); i < to; i++) m = Mathf.Max(m, Mathf.Abs(y[i] - y[i - 1]));
            return m;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator KeyPressesAndALessonPlayThroughTheEngineCleanly()
        {
            yield return Boot(t => t.Name == "AudioModule" || t.Name == "LessonsModule" || t.Name == "PianoModule");
            var piano = _app.Get<IPianoApi>();
            var library = _app.Get<ILessonLibrary>();
            var lesson = library.Get("piano-ode");
            Assert.IsNotNull(lesson);
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            foreach (var c in _cap) c.Begin();
            var onsets = new List<int>(); var releases = new List<int>();
            int presses = 0;
            int Pos() => _cap[1].Count;
            void Press(int m, float vel) { onsets.Add(Pos()); piano.PressMidi(m, "t" + m, vel); presses++; }
            void Lift(int m) { releases.Add(Pos()); piano.ReleaseMidi(m, "t" + m); }
            yield return Wait(0.3);
            // a held chord, released (dampers), then a scale (the step damp clears the previous step), fast re-strikes, a top key
            foreach (int m in new[] { 48, 60, 64, 67 }) Press(m, 0.7f);
            yield return Wait(0.8);
            foreach (int m in new[] { 48, 60, 64, 67 }) Lift(m);
            yield return Wait(0.4);
            foreach (int m in new[] { 60, 62, 64, 65, 67, 69, 71, 72 }) { Press(m, 0.55f); yield return Wait(0.14); Lift(m); yield return Wait(0.02); }
            for (int r = 0; r < 4; r++) { Press(72, 0.4f + 0.2f * r); yield return Wait(0.09); Lift(72); yield return Wait(0.03); }
            Press(100, 1f); yield return Wait(0.3); Lift(100);
            yield return Wait(1.0);
            // the lesson's melody as a player plays it (keys held 90 % of each note), with the tutorial running
            _app.Get<ITutorial>()?.Start(lesson.id);
            double spb = 60.0 / lesson.bpm, t0 = Time.realtimeSinceStartupAsDouble + 0.2;
            var todo = new List<(double t, bool down, int midi)>();
            foreach (var e in lesson.events)
                foreach (var tg in e.targets) { todo.Add((t0 + e.beat * spb, true, tg.midi)); todo.Add((t0 + (e.beat + e.len * 0.9) * spb, false, tg.midi)); }
            todo.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : (a.down ? 1 : 0).CompareTo(b.down ? 1 : 0));
            int next = 0;
            while (next < todo.Count)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                while (next < todo.Count && todo[next].t <= now) { var x = todo[next++]; if (x.down) Press(x.midi, 0.5f); else Lift(x.midi); }
                yield return null;
            }
            yield return Wait(1.5);
            foreach (var c in _cap) c.End();
            _app.Get<ITutorial>()?.Stop();

            Assert.That(tap.Count(AudioCall.Piano), Is.EqualTo(presses), "every press reached IAudioEngine.Piano");
            foreach (var c in tap.Calls) if (c.Method == AudioCall.Piano) Assert.IsFalse(UnitHandles.IsUnit(c.Handle), "today's piano made every handle");
            Assert.That(tap.Count(SourceId.PianoLo, SynthOp.StartBuffer) + tap.Count(SourceId.PianoMid, SynthOp.StartBuffer) + tap.Count(SourceId.PianoHi, SynthOp.StartBuffer),
                Is.GreaterThanOrEqualTo(presses), "every press started today's voice (its rendered buffer)");
            string[] names = { "lo", "mid", "hi" };
            for (int i = 0; i < 3; i++)
            {
                Scan(_cap[i].Buf, _cap[i].Count, out float pk, out int bad, out double rms);
                Debug.Log($"[PIANO-PLAY] {names[i]}: {_cap[i].Count} samples, peak {20 * Math.Log10(pk + 1e-12):F1} dBFS, rms {20 * Math.Log10(rms + 1e-12):F1} dBFS, non-finite {bad}");
                Assert.AreEqual(0, bad, names[i] + ": no NaN / Inf");
                Assert.That(pk, Is.LessThan(2f), names[i] + ": below +6 dBFS before the master");
            }
            Scan(_cap[1].Buf, _cap[1].Count, out float midPk, out _, out double midRms);
            Assert.That(midPk, Is.GreaterThan(0.01f), "the piano is heard on PianoMid (above -40 dBFS)");
            Assert.That(20 * Math.Log10(midRms), Is.GreaterThan(-55), "a sane level while playing");
            // clicks (the lab's metric): second-difference peaks after releases and everywhere else vs after onsets
            var y = _cap[1].Buf; int n = _cap[1].Count, lat = _bufFrames + 512;
            float on = 0, off = 0;
            var onWin = new bool[n];
            foreach (int p in onsets) { on = Mathf.Max(on, MaxD2(y, p, Math.Min(n, p + lat + 576))); for (int i = Math.Max(0, p - 256); i < Math.Min(n, p + lat + 2880); i++) onWin[i] = true; }
            int clean = 0;   // release windows that no onset overlaps (a release followed at once by the next press is an onset)
            foreach (int p in releases)
            {
                float m = 0; bool any = false;
                for (int i = Math.Max(2, p); i < Math.Min(n, p + lat + 1600); i++) if (!onWin[i]) { any = true; m = Mathf.Max(m, Mathf.Abs(y[i] - 2 * y[i - 1] + y[i - 2])); }
                if (any) { clean++; off = Mathf.Max(off, m); }
            }
            Assert.That(clean, Is.GreaterThan(5), "damper landings to look at");
            float rest = 0;
            for (int i = 2; i < n; i++) if (!onWin[i]) rest = Mathf.Max(rest, Mathf.Abs(y[i] - 2 * y[i - 1] + y[i - 2]));
            double relDb = 20 * Math.Log10(off / on), restDb = 20 * Math.Log10((rest + 1e-12) / on);
            Debug.Log($"[PIANO-PLAY] {presses} presses, {tap.Count(AudioCall.Release)} releases; clicks on PianoMid: dampers ({clean} windows) vs onsets {relDb:F1} dB, elsewhere vs onsets {restDb:F1} dB; " +
                      $"dsp buffer {_bufFrames}");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheLiveCallPathAllocatesNothing()
        {
            yield return Boot(t => t.Name == "AudioModule" || t.Name == "PianoModule");
            var piano = _app.Get<IPianoApi>();
            object presser = new object();
            void Play()
            {
                piano.PressMidi(60, presser, 0.6f); piano.ReleaseMidi(60, presser);    // press API -> today's voice, sustain
                piano.PressMidi(62, presser, 0.6f); piano.ReleaseMidi(62, presser);
                var h = _engine.Piano(72, 0.5f); _engine.Release(h, true);
            }
            Play(); yield return null; Play();   // warm up
            Assert.That(() => Play(), Is.Not.AllocatingGCMemory());
            yield return Wait(0.3);
        }
    }
}
