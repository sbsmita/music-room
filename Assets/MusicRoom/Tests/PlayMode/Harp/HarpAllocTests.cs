// Hot-path rule (ARCHITECTURE.md sections 2 and 17): the harp's Tick and Hover allocate nothing on the main thread,
// including plucks (engine calls), catches, slips, the recorder events, the one-mesh string deformation upload
// (SetVertices from a NativeArray), the glow vertex upload, lesson highlights and hover glows. Booted through TestBoot
// with manual frames, so only the stepped frames run. Same method as Tests/PlayMode/Audio/AudioAllocTests: the Test Framework's AllocatingGCMemory
// constraint (GC.Alloc recorder on this thread), warmed up first, with a positive calibration.
using System.Collections;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory()
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Harp
{
    public class HarpAllocTests
    {
        static float[] s_keep;
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: true, manualFrames: true);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest]
        public IEnumerator TickHoverAndTheApiAllocateNothing()
        {
            App app = App.Current;
            var harp = (HarpInstrument)app.Get<IHarpApi>();
            IHarpApi api = harp;
            Assert.That(app.Get<IAudioEngine>().TestTap, Is.Null, "the test tap allocates by design; it must be off here");
            var pointers = app.Input.HandPointers;
            Pointer p = app.Input.Hand(Handedness.Right).Pointers[0];
            HoverGlowPool glow = app.HoverGlow;
            const float dt = 1f / 72f;
            int frame = 0;

            // The right index tip, driven directly: a level sweep back and forth across strings 7..11 at y .95, 2 mm in
            // front of the plane (catches, holds, slips, glissandi), with a lesson highlight and a playback pluck/damp.
            void Step()
            {
                float phase = (frame % 24) / 23f;
                float x = frame / 24 % 2 == 0 ? Mathf.Lerp(-0.41f, -0.25f, phase) : Mathf.Lerp(-0.25f, -0.41f, phase);
                Vector3 next = harp.Root.TransformPoint(new Vector3(x, 0.95f, 0.002f));
                p.WasActive = p.Active;
                p.Prev = p.Active ? p.Tip : next;
                p.Tip = next;
                p.Active = true;
                p.SampleDt = dt;
                p.Vel = (p.Tip - p.Prev) / dt;
                p.Speed = p.Vel.magnitude;
                app.Clock.Step(dt);
                harp.Tick(pointers, dt);
                harp.Hover(pointers, glow);
                api.Highlight(3, 0.7f);
                if (frame % 30 == 0) api.PlaybackPluck(18, 0.6f, 0.4f, 0.5f, "through");
                if (frame % 30 == 15) api.PlaybackDamp(18);
                glow.Update((float)app.Clock.Now);
                frame++;
            }

            for (int k = 0; k < 96; k++) Step();
            Assert.That(harp.Play.Plucks, Is.GreaterThan(10), "the warm-up really plucked");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.1f) yield return null; // let the audio thread drain the rings

            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");
            int before = harp.Play.Plucks;
            Assert.That(() => { for (int k = 0; k < 96; k++) Step(); }, Is.Not.AllocatingGCMemory());
            Assert.That(harp.Play.Plucks - before, Is.GreaterThan(10), "the measured frames plucked too");
        }
    }
}
