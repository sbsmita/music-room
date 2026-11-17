// The Meta XR Audio HRTF's level for white noise at 0.5 m (inside the 0.6 m reference distance, so no distance gain)
// against the reference HRTF levels (Docs/06-audio-dsp.md, white noise at 0.6 m: front -5.01 per ear, 90 deg right L -13.4 /
// R -1.32, behind -8.9). ARCHITECTURE 7.5 asks to calibrate the front loudness to the reference; this measures it in the editor's
// plugin (the same algorithm as the device's) through a tap on the listener with the master bus bypassed, and checks the
// spatializer is really in the path (an ILD for a source on the right).
using System.Collections;
using System.Linq;
using System.Threading;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Audio
{
    public class HrtfLevelTests
    {
        /// <summary>Accumulates the listener's output energy per channel (audio thread).</summary>
        sealed class ListenerTap : MonoBehaviour
        {
            double _l, _r;
            public double Left => Volatile.Read(ref _l);
            public double Right => Volatile.Read(ref _r);

            void OnAudioFilterRead(float[] data, int channels)
            {
                double l = 0, r = 0;
                for (int i = 0; i + 1 < data.Length; i += channels) { l += data[i] * data[i]; r += data[i + 1] * data[i + 1]; }
                Volatile.Write(ref _l, _l + l);
                Volatile.Write(ref _r, _r + r);
            }
        }

        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            // TestBoot (fresh settings store, temp data folder) with only the Audio module: the fresh store has the tour
            // flag cleared, and Sunny's first-launch intro would leak its chirps and line into the listener measurement
            // (it made the HRTF front read 2.5 dB loud).
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        static IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        /// <summary>One measurement: white noise at 0.5 m along dir, 1 s window after a 0.3 s settle; ear energy over
        /// source energy in dB (the source's own energy is mono, before the spatializer).</summary>
        static IEnumerator Measure(AudioEngine engine, SynthSource src, ListenerTap tap, Transform head, Vector3 dir, double[] lr)
        {
            src.Post(new SynthCommand { Op = SynthOp.Panic });
            src.transform.position = head.position + dir * 0.5f;
            engine.Kit.Noise(src, 0, peak: 0.3f, decay: 30f);   // white: no filters; -0.3 dB/s over the window
            engine.Kit.Flush();
            yield return Wait(0.3f);
            double s0 = src.OutputEnergy, l0 = tap.Left, r0 = tap.Right;
            yield return Wait(1.0f);
            double ds = src.OutputEnergy - s0, dl = tap.Left - l0, dr = tap.Right - r0;
            Assert.That(ds, Is.GreaterThan(0), "the source rendered");
            Assert.That(Vector3.Distance(head.position, src.transform.position), Is.EqualTo(0.5f).Within(1e-3f), "the head moved during the window");
            lr[0] = 10 * System.Math.Log10(dl / ds);
            lr[1] = 10 * System.Math.Log10(dr / ds);
        }

        [UnityTest]
        public IEnumerator MetaHrtfLevelsAgainstTheReference()
        {
            var app = App.Current;
            var engine = (AudioEngine)app.Get<IAudioEngine>();
            // Let the rig settle first: in the editor RigBootstrap gives the XR runtime 0.5 s, then enables the desktop
            // fallback, which moves the head 1.2 m and turns it. The first calibration measured the front across that
            // jump (the source ended up ~1.3 m away) and read -9.4 dB instead of the steady -7.4 (head pose itself does
            // not change the result: level, pitched and yawed heads all measure the same front level).
            float t0 = Time.realtimeSinceStartup;
            while (RigBootstrap.Instance != null && RigBootstrap.Instance.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 5f)
                yield return null;
            yield return null;
            var head = app.Head;
            var master = head.GetComponent<MasterBus>();
            master.enabled = false;                                  // measure the spatializer, not the compressor
            var tap = head.gameObject.AddComponent<ListenerTap>();
            var src = engine.Source(SourceId.PianoMid);              // a point source with noise voices; its room send off for this
            var meta = src.GetComponent<MetaXRAudioSource>();
            meta.EnableAcoustics = false;
            Assert.That(meta.GainBoostDb, Is.EqualTo(AudioRig.HrtfCalibrationDb), "every spatial source carries the calibration");

            // The plugin's own front level (no boost): the calibration is the gap to the reference.
            var lr = new double[2];
            meta.GainBoostDb = 0f;                                   // MetaXRAudioSource pushes it to the plugin every Update
            yield return Measure(engine, src, tap, head, head.forward, lr);
            double rawFront = 0.5 * (lr[0] + lr[1]);
            meta.GainBoostDb = AudioRig.HrtfCalibrationDb;

            var dirs = new[] { ("front", head.forward, -5.01, -5.01), ("right", head.right, -13.4, -1.32), ("behind", -head.forward, -8.9, -8.9) };
            var results = new (string name, double l, double r)[dirs.Length];
            for (int d = 0; d < dirs.Length; d++)
            {
                yield return Measure(engine, src, tap, head, dirs[d].Item2, lr);
                results[d] = (dirs[d].Item1, lr[0], lr[1]);
            }
            master.enabled = true;
            string log = string.Join("; ", results.Select((x, i) => $"{x.name} L {x.l:F1} R {x.r:F1} dB (ref {dirs[i].Item3:F1} / {dirs[i].Item4:F1})"));
            Debug.Log($"[AudioTest] HRTF white-noise gain at 0.5 m: uncalibrated front {rawFront:F2} dB; with +{AudioRig.HrtfCalibrationDb} dB: " + log);
            // The spatializer is in the path: a source on the right is louder in the right ear.
            Assert.That(results[1].r - results[1].l, Is.GreaterThan(6), "interaural level difference for a source on the right");
            // The constant is the measured gap (the boost is plain dB), and with it the front matches the reference -5.0 dB per ear.
            Assert.That(AudioRig.HrtfCalibrationDb, Is.EqualTo(-5.01 - rawFront).Within(0.5), "HrtfCalibrationDb vs the measured front gap");
            Assert.That(results[0].l, Is.EqualTo(-5.01).Within(1.0), "front, left ear");
            Assert.That(results[0].r, Is.EqualTo(-5.01).Within(1.0), "front, right ear");
        }
    }
}
