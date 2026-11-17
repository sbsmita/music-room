// Hot-path rule (ARCHITECTURE.md section 17): IAudioEngine calls allocate nothing on the main thread. Measured with the
// Test Framework's AllocatingGCMemory constraint (the "GC.Alloc" profiler recorder filtered to the current thread), so
// the offline render workers, which allocate note buffers in the background, cannot disturb the result. A positive
// calibration proves the recorder works in batchmode. (GC.GetAllocatedBytesForCurrentThread returns 0 on Unity's Mono,
// measured: do not use it.) Spec 04's AllocCheck, as an assertion.
using System.Collections;
using MusicRoom.Audio;
using MusicRoom.Audio.Offline;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension on Is.Not
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class AudioAllocTests
    {
        static float[] s_keep;
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        [UnityTest]
        public IEnumerator EngineCallsAllocateNothingOnTheMainThread()
        {
            var engine = App.Current.Get<IAudioEngine>();
            Assert.That(engine, Is.Not.Null);
            Assert.That(engine.TestTap, Is.Null, "the test tap allocates by design; it must be off here");
            var h = new VoiceHandle[8];

            // Warm every path once (JIT, statics, first-use caches), then let the audio thread drain the rings.
            h[0] = engine.Piano(60, 0.5f); engine.Release(h[0]);
            engine.Drum(DrumKind.Ride, 0.5f, DrumStrike.Default); engine.Choke(DrumKind.Ride);
            engine.Release(engine.Pluck(3, 0.5f), thud: false); engine.Damp(engine.Pluck(4, 0.5f));
            engine.Pluck(5, 0.5f, 0, new PluckOptions { U = 0.4f, Speed = 0.5f, Mode = PluckMode.Across });
            engine.Chirp(ChirpKind.Open); engine.Swish(); engine.SnowGust(0.5f, 0.2f, 0.2f, 0.4f);
            engine.SetFire(true); engine.SetFire(false);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.1f) yield return null;

            // Calibration: the recorder must see a known allocation in this environment (batchmode, -nographics).
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");

            // 10 x (8 Piano + 8 Release + Drum + Choke + Pluck) = 190 engine calls, the live-play mix.
            Assert.That(() =>
            {
                for (int r = 0; r < 10; r++)
                {
                    for (int i = 0; i < h.Length; i++) h[i] = engine.Piano(40 + (i * 7 + r) % 48, 0.6f);
                    for (int i = 0; i < h.Length; i++) engine.Release(h[i], i == 0);
                    engine.Drum((DrumKind)(r % 10), 0.7f, new DrumStrike { Pos = 0.4f, Zone = DrumZone.Bow });
                    engine.Choke(DrumKind.Hat);
                    engine.Pluck(r % 22, 0.6f, 0, new PluckOptions { U = 0.35f, Speed = 0.3f, Mode = PluckMode.Through });
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [UnityTest]
        public IEnumerator HarpMidiOfAndRigHandlesAllocateNothing()
        {
            var app = App.Current;
            var harp = app.Get<IHarpApi>();
            var reg = app.Get<IRigRegistry>();
            int sum = 0;
            sum += harp.MidiOf(0) + HarpRenderer.MidiOf(1);
            foreach (var r in reg.Rigs) sum += r.Handles.Count;
            yield return null;
            TestDelegate measured = () =>
            {
                for (int i = 0; i < 22; i++) sum += harp.MidiOf(i) + HarpRenderer.MidiOf(i);
                var rigs = reg.Rigs;
                for (int i = 0; i < rigs.Count; i++) { var h = rigs[i].Handles; for (int k = 0; k < h.Count; k++) sum += h[k] != null ? 1 : 0; sum += h.Count; }
            };
            AllocAssert.Nothing(measured);   // the code once first, then the main thread's byte counter and the recorder
            Assert.That(sum, Is.GreaterThan(0));
        }
    }
}
