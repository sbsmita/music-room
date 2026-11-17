// The harp in the booted app plays the classic sound (today's StringSynth and its rendered strings; the only harp sound
// since 2026-11-10): the harp module pokes strings at their play points and drives IAudioEngine on the real batchmode
// audio thread, observed on AudioTestTap (calls with today's handles, commands on the Harp source) and on the Harp
// source's output energy; and zero GC on the module's path (Tick, Hover, the engine calls).
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Harp
{
    public class HarpClassicPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        HarpInstrument _harp;
        AudioEngine _engine;
        double _audio;

        IEnumerator Boot(bool audioOnly)
        {
            _audio = 50.0;
            // the real audio clock (not a scripted one): a sweep's plucks are scheduled on it, and the unit drops onsets that
            // arrive more than 100 ms late (CommandFlags.LateDrop), as a scripted clock behind the mixer would make them
            _boot = audioOnly ? new TestBoot() : new TestBoot(headless: true, manualFrames: true);
            if (audioOnly) _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
            _app = App.Current;
            _engine = (AudioEngine)_app.Get<IAudioEngine>();
            _harp = _app.Get<IHarpApi>() as HarpInstrument;
            Assert.IsNull(_engine.Unit(UnitId.Harp), "the app creates no harp unit");
            Assert.IsNull(_engine.ActiveUnit(UnitId.Harp), "today's harp plays the harp role");
            float t0 = Time.realtimeSinceStartup;
            while (_engine.RenderSeconds < 0 && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Assert.That(_engine.RenderSeconds, NUnit.Framework.Is.GreaterThanOrEqualTo(0.0), "today's harp strings rendered");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_engine != null) _engine.TestTap = null;
            _app = null; _harp = null; _engine = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        IEnumerator Seated()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Frame();
        }

        void Frame(int n = 1) { for (int i = 0; i < n; i++) { _audio += Dt; _boot.Step(Dt); } }

        static IEnumerator Real(float seconds) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < seconds) yield return null; }

        Vector3 World(Vector3 local) => _harp.Root.TransformPoint(local);

        HandPose Pointing(Vector3 local)
        {
            Vector3 fingers = _harp.Root.TransformDirection(Vector3.back);
            return HandPose.OpenFacing(HandSide.Right, Vector3.zero, fingers, Vector3.down).WithPointing().WithIndexTipAt(World(local));
        }

        /// <summary>The in-plane unit normal of string i, pointing to where Side()'s distance is positive.</summary>
        static Vector3 Normal(int i)
        {
            StringLayout s = HarpGeometry.Strings[i];
            Vector3 d = (s.B - s.A).normalized;
            return new Vector3(-d.y, d.x, 0f);
        }

        void Poke(int i)
        {
            Vector3 p = HarpGeometry.PlayPoint(i);
            _boot.Hands.SetPose(HandSide.Right, Pointing(p + new Vector3(0f, 0f, 0.03f)));
            Frame(3);
            _boot.Hands.SetPose(HandSide.Right, Pointing(p + new Vector3(0f, 0f, -0.01f)));
            Frame();
            _boot.Hands.SetPose(HandSide.Right, Pointing(p + new Vector3(0f, 0f, -0.05f)));
            Frame();
        }

        void HandAway() { _boot.Hands.SetTracked(HandSide.Right, false); Frame(10); _boot.Hands.SetTracked(HandSide.Right, true); }

        [UnityTest]
        public IEnumerator TheHarpModulePlaysTodaysHarpThroughTheEngine()
        {
            yield return Boot(false);
            yield return Seated();
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            double e0 = _engine.Source(SourceId.Harp).OutputEnergy;
            foreach (int i in new[] { 7, 9, 11, 14 }) { Poke(i); HandAway(); }
            Assert.That(tap.Count(AudioCall.Pluck), NUnit.Framework.Is.GreaterThanOrEqualTo(4), "every poke plucked a string");
            foreach (var c in tap.Calls) if (c.Method == AudioCall.Pluck) Assert.IsFalse(UnitHandles.IsUnit(c.Handle), "today's harp made every handle");
            Assert.That(tap.Commands.Count(c => c.Source == SourceId.Harp), NUnit.Framework.Is.GreaterThan(0), "today's voices on the Harp source");
            Assert.That(tap.CountUnit(UnitId.Harp), NUnit.Framework.Is.EqualTo(0), "no unit call");
            float t0 = Time.realtimeSinceStartup;
            while (_engine.Source(SourceId.Harp).OutputEnergy <= e0 + 1e-4 && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            Assert.That(_engine.Source(SourceId.Harp).OutputEnergy, NUnit.Framework.Is.GreaterThan(e0 + 1e-4), "heard on the Harp source");
            Assert.That(_engine.HarpRingSeconds(60), NUnit.Framework.Is.EqualTo(MusicRoom.Audio.Offline.HarpRenderer.BaseSeconds(60)), "today's ring length");
        }

        [UnityTest]
        public IEnumerator TheClassicHarpPathAllocatesNothing()
        {
            yield return Boot(false);
            yield return Seated();
            IAudioEngine engine = _engine;
            var pointers = _app.Input.HandPointers;
            Pointer p = _app.Input.Hand(Handedness.Right).Pointers[0];
            HoverGlowPool glow = _app.HoverGlow;
            IHarpApi api = _harp;
            int frame = 0;
            var opt = new PluckOptions { U = 0.4f, Mode = PluckMode.Through, Ud = 0.3f, Uz = 1f };
            void Step()
            {
                // the right index tip sweeping across strings 7..11 plus the API
                float phase = (frame % 24) / 23f;
                float x = frame / 24 % 2 == 0 ? Mathf.Lerp(-0.41f, -0.25f, phase) : Mathf.Lerp(-0.25f, -0.41f, phase);
                Vector3 next = _harp.Root.TransformPoint(new Vector3(x, 0.95f, 0.002f));
                p.WasActive = p.Active; p.Prev = p.Active ? p.Tip : next; p.Tip = next; p.Active = true; p.SampleDt = Dt;
                p.Vel = (p.Tip - p.Prev) / Dt; p.Speed = p.Vel.magnitude;
                _app.Clock.Step(Dt);
                _harp.Tick(pointers, Dt);
                _harp.Hover(pointers, glow);
                if (frame % 30 == 0) api.PlaybackPluck(18, 0.6f, 0.4f, 0.5f, "through");
                if (frame % 30 == 15) api.PlaybackDamp(18);
                if (frame % 20 == 5)
                {
                    VoiceHandle h = engine.Pluck(3, 0.5f, 0, opt);
                    engine.Damp(h, 2);
                    engine.HarpRingSeconds(60);
                    engine.HarpPartialTau(3, 2);
                }
                glow.Update((float)_app.Clock.Now);
                frame++;
            }
            for (int k = 0; k < 120; k++) Step();
            Assert.That(_harp.Play.Plucks, NUnit.Framework.Is.GreaterThan(10));
            yield return Real(0.1f);
            int before = _harp.Play.Plucks;
            Assert.That(() => { for (int k = 0; k < 96; k++) Step(); }, Is.Not.AllocatingGCMemory());
            Assert.That(_harp.Play.Plucks - before, NUnit.Framework.Is.GreaterThan(10), "the measured frames plucked");
        }
    }
}
