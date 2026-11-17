// The classic sound is the only sound of the piano, drums, harp and room (the user's choice after the headset test of
// 2026-11-10). The engine with the Burst graph running for real (batchmode audio thread): every boot has the graph live on
// all 26 sources and creates only the unit-only roles' units; the piano, drums and harp play today's synths (their
// buffers rendered at start-up, as before the upgrade) and have no unit, nor has the room (their upgraded units were
// deleted on 2026-11-11: SoundUnits knows only the unit-only roles); an old save's "Classic sound" setting is deleted
// once. The calls' allocation: AudioAllocTests.
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Synths;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Audio
{
    public class ClassicSoundTests
    {
        TestBoot _boot;
        AudioEngine _engine;

        IEnumerator Boot(int oldClassic = -1)
        {
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            if (oldClassic >= 0) _boot.Settings.SetInt(AppSettings.KeyClassicSoundOld, oldClassic);
            yield return _boot.Start();
            _engine = (AudioEngine)App.Current.Get<IAudioEngine>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_engine != null) _engine.TestTap = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _engine = null;
        }

        static IEnumerator Until(System.Func<bool> done, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        bool Baked(int slot0, int count) { for (int i = 0; i < count; i++) if (_engine.Bank.Get(slot0 + i) == null) return false; return true; }

        [UnityTest]
        public IEnumerator EveryBootRunsTheGraphWithOnlyTheUnitOnlyRoles()
        {
            yield return Boot();
            var g = _engine.Graph;
            Assert.IsNotNull(g, "the Burst graph is created at start-up (the unit-only roles play through it)");
            Assert.That(GraphBridge.Host, NUnit.Framework.Is.SameAs(g));
            foreach (var s in _engine.Sources) Assert.That(s.Graph, NUnit.Framework.Is.SameAs(g), $"{s.id} renders the graph");
            long b0 = g.BlocksRendered;
            yield return Until(() => g.BlocksRendered > b0 + 20, 5f);
            Assert.That(g.BlocksRendered, NUnit.Framework.Is.GreaterThan(b0 + 20), "one render per mixer block, on the audio thread");
            foreach (var role in new[] { UnitId.Piano, UnitId.Drums, UnitId.Harp, UnitId.Room })
            {
                Assert.IsNull(_engine.Unit(role), $"{role}: no unit is created (no CPU, no memory)");
                Assert.IsNull(_engine.ActiveUnit(role), $"{role}: today's synths play it");
                Assert.IsFalse(g.IsRegistered(role), $"{role}: nothing registered in the graph");
            }
            foreach (var role in SoundUnits.LiveRoles)
            {
                Assert.IsTrue(SoundUnits.IsUnitOnly(role), role + " is a unit-only role");
                Assert.That(_engine.ActiveUnit(role), NUnit.Framework.Is.SameAs(_engine.Unit(role)), $"{role}: its unit plays it");
            }
            Assert.That(SoundUnits.LiveRoles, Has.No.Member(UnitId.Piano).And.No.Member(UnitId.Drums).And.No.Member(UnitId.Harp).And.No.Member(UnitId.Room));
            foreach (var role in new[] { UnitId.Piano, UnitId.Drums, UnitId.Harp, UnitId.Room })
                Assert.That(SoundUnits.Roles, Has.No.Member(role), $"{role}: no unit implementation exists any more");
            Assert.That(_engine.Renders.Total, NUnit.Framework.Is.EqualTo(1 + 88 + 22 + GuideVoice.BootLines.Length),
                "today's piano and harp buffers at start-up, as before the upgrade (the voice lines past the intro render on demand)");
            yield return Until(() => _engine.RenderSeconds >= 0, 30f);
            Assert.IsTrue(Baked(AudioEngine.PianoSlot0, 88), "all 88 piano keys rendered");
            Assert.IsTrue(Baked(AudioEngine.HarpSlot0, 22), "all 22 harp strings rendered");
            Assert.That(_engine.FirstKeySeconds, NUnit.Framework.Is.GreaterThanOrEqualTo(0.0), "middle C's buffer decides the first key");
            var module = App.Current.Modules.OfType<AudioModule>().First();
            StringAssert.Contains(" graph=", module.StartupReport);
            StringAssert.DoesNotContain("classic=", module.StartupReport, "no sound switch to report");
            StringAssert.DoesNotContain("piano", module.StartupReport.Substring(module.StartupReport.IndexOf(" units=")), "no piano unit");
            Debug.Log("[ClassicSoundTests] " + module.StartupReport);
        }

        [UnityTest]
        public IEnumerator TheClassicThreePlayTodaysVoices()
        {
            yield return Boot();
            yield return Until(() => _engine.RenderSeconds >= 0, 30f);
            var tap = new AudioTestTap();
            _engine.TestTap = tap;

            var h = _engine.Piano(60, 0.8f);
            Assert.IsFalse(UnitHandles.IsUnit(h), "today's piano handle");
            Assert.That(tap.Count(SourceId.PianoMid, SynthOp.StartBuffer), NUnit.Framework.Is.EqualTo(1), "today's piano voice (its rendered buffer)");
            double e0 = _engine.Source(SourceId.PianoMid).OutputEnergy;
            yield return Until(() => _engine.Source(SourceId.PianoMid).OutputEnergy > e0 + 1e-3, 2f);
            Assert.That(_engine.Source(SourceId.PianoMid).OutputEnergy, NUnit.Framework.Is.GreaterThan(e0 + 1e-3), "and is heard on PianoMid");
            _engine.Release(h, false);
            Assert.That(tap.Count(SourceId.PianoMid, SynthOp.Release), NUnit.Framework.Is.GreaterThan(0), "the release reaches today's voice");

            int before = tap.Commands.Count;
            _engine.Drum(DrumKind.Snare, 0.8f, DrumStrike.Default);
            Assert.That(tap.Commands.Skip(before).Count(c => c.Source == SourceId.Snare), NUnit.Framework.Is.GreaterThan(0), "today's snare");
            before = tap.Commands.Count;
            var p = _engine.Pluck(7, 0.8f);
            Assert.IsFalse(UnitHandles.IsUnit(p), "today's harp handle");
            Assert.That(tap.Commands.Skip(before).Count(c => c.Source == SourceId.Harp), NUnit.Framework.Is.GreaterThan(0), "today's harp string");
            Assert.That(tap.CountUnit(UnitId.Piano) + tap.CountUnit(UnitId.Drums) + tap.CountUnit(UnitId.Harp), NUnit.Framework.Is.EqualTo(0), "no unit call");
        }

        [UnityTest]
        public IEnumerator TheRoomIsMetasAcousticsAndTodaysMasterChain()
        {
            yield return Boot();
            Assert.IsNull(_engine.Unit(UnitId.Room), "no room unit");
            for (int i = 0; i <= (int)SourceId.DrumRoom; i++)
            {
                var src = _engine.Sources[i];
                var meta = src.GetComponent<MetaXRAudioSource>();
                Assert.IsTrue(meta.EnableAcoustics, src.id + ": sends to Meta's room (reflections and reverb)");
            }
            for (int i = (int)SourceId.Kick; i <= (int)SourceId.Ride; i++)
                Assert.That(_engine.Sources[i].sendGain, NUnit.Framework.Is.EqualTo(AudioRig.DrumRoomSend((SourceId)i)), (SourceId)i + " drum-room send");
            var master = App.Current.Head.GetComponent<MasterBus>();
            Assert.IsNotNull(master);
            long b0 = master.BlocksRendered;
            _engine.Kit.Noise(_engine.Source(SourceId.PianoMid), 0, peak: 0.1f, decay: 30f);
            _engine.Kit.Flush();
            yield return Until(() => master.BlocksRendered > b0 + 100, 5f);
            Assert.That(master.BlocksRendered, NUnit.Framework.Is.GreaterThan(b0 + 100), "today's master chain runs every block");
        }

        [UnityTest]
        public IEnumerator AnOldClassicSoundSettingIsDeletedOnce([Values(0, 1)] int old)
        {
            yield return Boot(old);
            Assert.IsFalse(_boot.Settings.HasKey(AppSettings.KeyClassicSoundOld), "the old switch is gone from the save");
            Assert.IsNull(_engine.Unit(UnitId.Piano), "whatever it said: the classic sound plays");
        }
    }
}
