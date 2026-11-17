// Audio module (owned by the Audio agent, rule 3): the engine running for real (batchmode audio thread, 256-frame blocks).
// The foundation's checks (the API-level call log of AudioTestTap that instrument tests assert, recipe details reaching
// their sources, the pre-rendered metal sums) plus the module's: the start-up facts the device checklist needs and the
// first-key time, the offline renders, Sunny's lines and mouth, the ambience switches, snow gusts and idle skip, the
// mixed-reality sends, the per-block budget under a busy groove (ARCHITECTURE section 17), the hot-path allocation rule
// for the calls AudioAllocTests does not cover, and the scripted audio clock.
// Every test boots through TestBoot (fresh MemorySettingsStore, temp DataPath) with only the Audio module: a fresh store
// has the tour flag cleared, and Sunny's first-launch intro would add its own chirps, lines and API calls to the
// measurements and the call log.
using System.Collections;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Offline;
using MusicRoom.Audio.Synths;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension on Is.Not
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Audio
{
    public class AudioEngineTests
    {
        static float[] s_keep;
        TestBoot _boot;
        AudioEngine _engine;
        AudioModule _module;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
            _engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            _module = App.Current.Modules.OfType<AudioModule>().First();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_engine != null) _engine.TestTap = null;
            return _boot.Stop();
        }

        static IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        IEnumerator WaitForRenders(float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (_engine.RenderSeconds < 0 && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        // ---------------- foundation (review round 1) ----------------

        [UnityTest]
        public IEnumerator CallLogRecordsEveryApiCallAsTheInstrumentMadeIt()
        {
            var engine = App.Current.Get<IAudioEngine>();
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            var h = engine.Piano(64, 0.55f);
            engine.Release(h, thud: false);
            engine.Drum(DrumKind.Ride, 0.4f, new DrumStrike { Pos = 0.8f, Zone = DrumZone.Edge });
            engine.Choke(DrumKind.Crash);
            var p = engine.Pluck(9, 0.7f, 0, new PluckOptions { U = 0.42f, Speed = 0.9f, Mode = PluckMode.Across });
            engine.Damp(p, 2);
            engine.SetSnow(true);
            engine.PlayVoiceLine("intro_vr");
            engine.TestTap = null;

            Assert.That(tap.Calls.Select(c => c.Method), Is.EqualTo(new[] { AudioCall.Piano, AudioCall.Release, AudioCall.Drum, AudioCall.Choke, AudioCall.Pluck, AudioCall.Damp, AudioCall.Snow, AudioCall.VoiceLine }));
            Assert.That(tap.Calls[0].Index, Is.EqualTo(64)); Assert.That(tap.Calls[0].Vel, Is.EqualTo(0.55f)); Assert.That(tap.Calls[0].Handle.Id, Is.EqualTo(h.Id));
            Assert.IsFalse(tap.Calls[1].Flag, "Release(thud: false)");
            Assert.That(tap.Calls[2].Drum, Is.EqualTo(DrumKind.Ride)); Assert.That(tap.Calls[2].Strike.Zone, Is.EqualTo(DrumZone.Edge)); Assert.That(tap.Calls[2].Strike.Pos, Is.EqualTo(0.8f));
            Assert.That(tap.Calls[3].Drum, Is.EqualTo(DrumKind.Crash));
            Assert.That(tap.Calls[4].Index, Is.EqualTo(9)); Assert.That(tap.Calls[4].Pluck.Mode, Is.EqualTo(PluckMode.Across)); Assert.That(tap.Calls[4].Pluck.U, Is.EqualTo(0.42f));
            Assert.That(tap.Calls[5].Hands, Is.EqualTo(2)); Assert.That(tap.Calls[5].Handle.Id, Is.EqualTo(p.Id));
            Assert.IsTrue(tap.Calls[6].Flag);
            Assert.That(tap.Calls[7].Line, Is.EqualTo("intro_vr"));
            Assert.That(tap.Count(AudioCall.Drum), Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PianoDrumAndHarpRecipesReachTheirSourcesAndTheAudioThreadRuns()
        {
            var app = App.Current;
            var engine = _engine;
            Assert.IsTrue(engine.IsReady);
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            var h = engine.Piano(60, 0.8f);
            Assert.IsTrue(h.IsValid);
            Assert.That(h.Source.id, Is.EqualTo(SourceId.PianoMid));
            Assert.That(tap.Count(SourceId.PianoMid, SynthOp.StartNoise), Is.EqualTo(1), "hammer knock");
            Assert.That(tap.Count(SourceId.PianoMid, SynthOp.StartBuffer), Is.EqualTo(1));
            var buf = tap.Commands.First(e => e.Command.Op == SynthOp.StartBuffer).Command;
            Assert.That(buf.Buffer, Is.EqualTo(AudioEngine.PianoSlot0 + 60 - 21));
            Assert.That(buf.Level, Is.EqualTo(0.08f + 0.8f * 0.64f).Within(0.02f));
            engine.Release(h);
            Assert.That(tap.Count(SourceId.PianoMid, SynthOp.Release), Is.EqualTo(1));
            engine.Drum(DrumKind.Snare, 0.7f, DrumStrike.Default);
            Assert.That(tap.Count(SourceId.Snare, SynthOp.StartTone), Is.EqualTo(2), "snare head: two tones");
            Assert.That(tap.Count(SourceId.Snare, SynthOp.StartNoise), Is.EqualTo(1));
            engine.Pluck(7, 0.6f);
            Assert.That(tap.Count(SourceId.Harp, SynthOp.StartBuffer), Is.EqualTo(1));
            engine.TestTap = null;

            yield return Wait(0.7f);
            var src = engine.Source(SourceId.PianoMid);
            Debug.Log($"[AudioEngineTests] blocks={src.BlocksRendered} dropped={src.DroppedCommands} master={app.Head.GetComponent<MasterBus>()?.BlocksRendered} sr={engine.SampleRate} progress={engine.RenderProgress:F2} prealloc={engine.PreallocatedBytes / 1048576.0:F1}MiB");
            Assert.That(src.BlocksRendered, Is.GreaterThan(0), "the audio thread calls OnAudioFilterRead in batchmode");
            Assert.That(src.DroppedCommands, Is.EqualTo(0));
            var master = app.Head.GetComponent<MasterBus>();
            Assert.That(master, Is.Not.Null);
            Assert.That(master.BlocksRendered, Is.GreaterThan(0), "the master chain sits on the listener");
            Assert.That(master.MakeupDb, Is.EqualTo(2.137).Within(0.01));
            Assert.That(engine.PreallocatedBytes, Is.GreaterThan(70L * 1048576), "piano + harp buffers allocated up front at start-up");
        }

        [UnityTest]
        public IEnumerator MetalHitsUseThePreRenderedSumsOnceBuilt()
        {
            var engine = _engine;
            float t0 = Time.realtimeSinceStartup;
            while (!MetalBank.Ready && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.IsTrue(PeriodicWaveTables.Ready, "the first render job builds the periodic-wave tables");
            Assert.IsTrue(MetalBank.Ready, "and the metal sums right after");
            foreach (float r in MetalBank.Ratios) Assert.That(MetalBank.Get(r, engine.SampleRate), Is.Not.Null, "ratio " + r);
            Assert.That(MetalBank.Get(1.31f, engine.SampleRate), Is.Null, "other ratios run live");
            engine.Drum(DrumKind.Ride, 0.8f, DrumStrike.Default);
            engine.Drum(DrumKind.Hat, 0.8f, DrumStrike.Default);
            yield return Wait(0.2f);
            Assert.That(engine.Source(SourceId.Ride).DroppedCommands, Is.EqualTo(0));
            Assert.That(engine.Source(SourceId.Ride).Rms, Is.GreaterThanOrEqualTo(0f));
        }

        // ---------------- the module's ----------------

        [UnityTest]
        public IEnumerator EveryEngineCallIsRecordedOnTheTap()
        {
            // AudioTestTap.Calls (section 16): every IAudioEngine method that makes or stops a sound logs one Call.
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            _engine.SetFire(true); _engine.SetWind(true); _engine.SetSnow(true);
            _engine.SnowGust(new SnowGust { Strength = 0.4f, Attack = 1f, Hold = 0.5f, Release = 2f });
            _engine.Chirp(ChirpKind.Close);
            _engine.PlayVoiceLine("bye");
            _engine.StopVoiceLine();
            _engine.Swish(0);
            _engine.SetFire(false); _engine.SetWind(false); _engine.SetSnow(false);
            _engine.TestTap = null;
            Assert.That(tap.Calls.Select(c => c.Method), Is.EqualTo(new[]
            {
                AudioCall.Fire, AudioCall.Wind, AudioCall.Snow, AudioCall.SnowGust, AudioCall.Chirp, AudioCall.VoiceLine,
                AudioCall.StopVoiceLine, AudioCall.Swish, AudioCall.Fire, AudioCall.Wind, AudioCall.Snow,
            }));
            Assert.That(tap.Calls[3].Vel, Is.EqualTo(0.4f), "gust strength");
            Assert.That(tap.Calls[4].Index, Is.EqualTo((int)ChirpKind.Close));
            Assert.That(tap.Calls[5].Line, Is.EqualTo("bye"));
            Assert.IsFalse(tap.Calls[8].Flag, "SetFire(false)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartupReportNamesTheDeviceFactsAndEveryRenderLands()
        {
            string r = _module.StartupReport;
            Debug.Log("[AudioTest] " + r);
            AudioSettings.GetDSPBufferSize(out int frames, out _);
            StringAssert.Contains("sr=" + AudioSettings.outputSampleRate + " ", r);
            StringAssert.Contains("dsp=" + frames + "x", r);
            StringAssert.Contains("spatializer='" + AudioModule.MetaSpatializer + "'", r);
            StringAssert.Contains("voiceLimit=64", r);
            StringAssert.Contains($"lines={GuideVoice.LineCount}/{GuideVoice.LineCount}", r);   // the tour's 22 lines + tip_arrange + the looper's 15 (+ the modules' manifests)
            StringAssert.Contains($"sources={(int)SourceId.Count} ", r);
            StringAssert.Contains($"bank={AudioEngine.BankSlots(GuideVoice.LineCount)} ", r);
            StringAssert.Contains("buffers=", r);
            StringAssert.Contains("probe=False", r);   // dev device builds only
            Assert.That(frames, Is.EqualTo(AudioModule.PreferredDspBuffer), "DSP buffer 256 (ARCHITECTURE section 0)");
            // Every boot output buffer is allocated on the main thread in Init (section 7.1): 88 keys, 22 strings, Sunny's two
            // intro lines (the other lines render on demand: platform.md 8.2, sunny-tour 7.1).
            int sr = _engine.SampleRate;
            long notes = 0;
            for (int m = 21; m <= 108; m++) notes += 4L * PianoRenderer.Length(m, sr);
            foreach (int m in HarpRenderer.Strings) notes += 4L * HarpRenderer.Length(m, sr);
            long linesFloor = GuideVoice.BootLines.Length * 4L * (sr / 2);   // every line is longer than 0.5 s
            Assert.That(_engine.PreallocatedBytes, Is.GreaterThan(notes + linesFloor), "notes and the boot lines allocated up front");

            yield return WaitForRenders(30f);
            Assert.That(_engine.RenderSeconds, Is.GreaterThanOrEqualTo(0), "all offline renders finished");
            Assert.That(_engine.Renders.Failed, Is.EqualTo(0));
            Assert.That(PeriodicWaveTables.Ready, Is.True);
            Assert.That(MetalBank.Ready, Is.True);
            for (int m = 21; m <= 108; m++) Assert.That(_engine.Bank.Get(AudioEngine.PianoSlot0 + m - 21), Is.Not.Null, "piano key " + m);
            for (int i = 0; i < 22; i++) Assert.That(_engine.Bank.Get(AudioEngine.HarpSlot0 + i), Is.Not.Null, "harp string " + i);
            Assert.That(_engine.GuideVoice.RenderedLines, Is.EqualTo(GuideVoice.BootLines.Length), "the intro lines at boot, nothing else yet");
            Assert.That(_engine.GuideVoice.SyncRenders, Is.EqualTo(0), "no line was rendered on the main thread");
            foreach (string id in GuideVoice.BootLines)
            {
                float[] y = _engine.Bank.Get(AudioEngine.VoiceSlot0 + _engine.GuideVoice.IndexOf(id));
                Assert.That(y, Is.Not.Null, id + " rendered at boot");
                float peak = 0;
                foreach (var v in y) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.That(peak, Is.GreaterThan(0.1f), id + " is not silent");
            }
            // Section 17: the first key (middle C) ready < 0.15 s on the device. Middle C is the second job (beside the
            // tables, on the other worker), ahead of the intro lines; the editor bound leaves room for a loaded machine.
            double firstKey = _engine.FirstKeySeconds;
            yield return null;   // AudioModule logs its "renders done" line in the Listener phase
            Debug.Log($"[AudioTest] renders {_engine.RenderSeconds:F2} s, first key {firstKey:F3} s, wave tables " +
                      $"{PeriodicWaveTables.BuildMilliseconds:F0} ms, bank {_engine.Bank.TotalSamples() * 4 / (1024 * 1024)} MiB, " +
                      $"prealloc {_engine.PreallocatedBytes / 1048576.0:F1} MiB; report: {_module.RenderReport}");
            Assert.That(firstKey, Is.GreaterThan(0).And.LessThan(1.0), "middle C ready (device budget 0.15 s)");
            Assert.That(firstKey, Is.LessThan(_engine.RenderSeconds), "middle C before the queue's end");
            Assert.That(_module.RenderReport, Does.Contain("first key"));
        }

        [UnityTest]
        public IEnumerator TheEngineBuildsTwentyFiveSourcesAndAVoiceSlotPerLine()
        {
            // platform.md 10.1 / 12.3: the 18 classic sources and the platform's 8, one AudioSourceTable row each
            Assert.That(_engine.Sources.Length, Is.EqualTo(25));
            for (int i = 0; i < _engine.Sources.Length; i++)
            {
                var s = _engine.Sources[i];
                Assert.That(s.id, Is.EqualTo((SourceId)i));
                Assert.That(s.name, Is.EqualTo("Synth " + (SourceId)i));
                var row = AudioSourceTable.Current[s.id];
                Assert.That(s.noiseVoices + s.toneVoices + s.metalVoices + s.bufferVoices,
                    Is.EqualTo(row.NoiseVoices + row.ToneVoices + row.MetalVoices + row.BufferVoices), s.id + " pools");
            }
            // the voice slots (platform.md 8.2): VoiceSlot0 + every line of the merged manifests, at least 512 slots
            Assert.That(_engine.Bank.SlotCount, Is.GreaterThanOrEqualTo(AudioEngine.VoiceSlot0 + GuideVoice.LineCount));
            Assert.That(_engine.Bank.SlotCount, Is.GreaterThanOrEqualTo(512));
            Assert.That(_engine.GuideVoice.Lines, Is.EqualTo(GuideVoice.LineCount));
            Assert.That(_engine.GuideVoice.Async, Is.True, "lines past the intro render on the voice worker");
            Assert.That(App.Current.Get<IAudioPresence>(), Is.SameAs(_module.Presence));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryVoiceLineRendersOnDemandAndAtMostTwentyFourStayResident()
        {
            // Lazy rendering (sunny-tour 7.1): each line asked for (VoiceLineReady, as Sunny asks) renders on the voice worker
            // a few frames later, is not silent, and the least recently asked go once more than 24 are resident.
            var g = _engine.GuideVoice;
            yield return WaitForRenders(30f);
            g.ProtectSeconds = 0f;   // strictly the limit (the app keeps lines asked in the last 2 s)
            int most = 0, sync = g.SyncRenders;
            float slowest = 0f;
            for (int i = 0; i < GuideVoice.LineCount; i++)
            {
                string id = GuideVoice.LineIds[i];
                float t0 = Time.realtimeSinceStartup;
                while (!_engine.VoiceLineReady(id) && Time.realtimeSinceStartup - t0 < 5f) yield return null;
                slowest = Mathf.Max(slowest, Time.realtimeSinceStartup - t0);
                Assert.That(_engine.VoiceLineReady(id), Is.True, id + " rendered on demand");
                float[] y = _engine.Bank.Get(AudioEngine.VoiceSlot0 + i);
                float peak = 0;
                foreach (var v in y) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.That(peak, Is.GreaterThan(0.1f), id + " is not silent");
                most = Mathf.Max(most, g.RenderedLines);
            }
            Debug.Log($"[AudioTest] every line on demand: slowest {slowest * 1000:F0} ms from the first ask, at most {most} resident, {g.Evictions} evicted, " +
                      $"resident now {g.ResidentSamples * 4 / 1048576.0:F1} MiB");
            Assert.That(most, Is.LessThanOrEqualTo(GuideVoice.MaxResident));
            Assert.That(g.SyncRenders, Is.EqualTo(sync), "none on the main thread");
            Assert.That(slowest, Is.LessThan(MusicRoom.Guide.GuideTiming.Late), "each within Sunny's LATE window (a line not ready 1.2 s after its caption is dropped)");
            g.ProtectSeconds = 2f;
        }

        [UnityTest]
        public IEnumerator SunnySpeaksWithAMovingMouthAndStopsOnCue()
        {
            var g = _engine.GuideVoice;
            int line = g.IndexOf("intro_desk");
            float t0 = Time.realtimeSinceStartup;
            while (!g.IsRendered(line) && Time.realtimeSinceStartup - t0 < 10f) yield return null;   // early in the queue
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            _engine.Chirp(ChirpKind.Open);
            Assert.That(tap.Count(SourceId.Guide, SynthOp.StartTone), Is.EqualTo(2), "bleep and bloop");
            double asked = _engine.Now;
            double end = _engine.PlayVoiceLine("intro_desk", asked + GuideVoice.Lead);
            Assert.That(end, Is.EqualTo(asked + GuideVoice.Lead + _engine.VoiceLineSeconds("intro_desk")).Within(0.02));
            Assert.That(tap.Count(SourceId.Guide, SynthOp.StartBuffer), Is.EqualTo(1));
            _engine.TestTap = null;

            float maxLevel = 0f;
            bool playing = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.2f)
            {
                maxLevel = Mathf.Max(maxLevel, _engine.VoiceLevel);
                playing |= _engine.VoiceLinePlaying;
                yield return null;
            }
            Assert.That(playing, Is.True);
            Assert.That(maxLevel, Is.GreaterThan(0.01f), "the mouth level follows the line");
            Assert.That(_engine.Source(SourceId.Guide).DroppedCommands, Is.EqualTo(0));
            _engine.StopVoiceLine();
            Assert.That(_engine.VoiceLinePlaying, Is.False);
            Assert.That(_engine.VoiceLevel, Is.EqualTo(0f), "a cut line closes the mouth");
            Assert.That(_engine.PlayVoiceLine("no_such_line"), Is.EqualTo(0));
            Assert.That(_engine.HasVoiceLine("bye"), Is.True);
        }

        [UnityTest]
        public IEnumerator AmbienceSwitchesRampTheBedsAndGustsFollowTheWeather()
        {
            var tap = new AudioTestTap();
            _engine.TestTap = tap;
            var gust = new SnowGust { Strength = 0.7f, Attack = 1.5f, Hold = 1f, Release = 3f };
            _engine.SnowGust(gust);
            Assert.That(tap.Count(SourceId.Snow, SynthOp.Custom), Is.EqualTo(0), "no gust while the snow sound is off");
            _engine.SetFire(true);
            _engine.SetWind(true);
            _engine.SetSnow(true);
            Assert.That(_engine.FireOn && _engine.WindOn && _engine.SnowOn, Is.True);
            _engine.SnowGust(gust);   // what the Room module does on ISnowWeather.OnGust
            Assert.That(tap.Count(SourceId.Snow, SynthOp.Custom), Is.EqualTo(1));
            var c = tap.Commands.Last(e => e.Source == SourceId.Snow && e.Command.Op == SynthOp.Custom).Command;
            Assert.That(c.Kind, Is.EqualTo(SynthCustom.SnowGust));
            Assert.That(c.Level, Is.EqualTo(0.7f));
            Assert.That(c.Slide, Is.EqualTo(1.5f));
            Assert.That(c.StopAfter, Is.EqualTo(1f));
            Assert.That(c.Tau, Is.EqualTo(3f));
            Assert.That(tap.Count(AudioCall.SnowGust), Is.EqualTo(2), "both calls are on the API log");
            _engine.TestTap = null;

            yield return Wait(0.2f);
            long idleAtOn = _engine.Source(SourceId.Wind).IdleBlocks;
            yield return Wait(1.0f);
            float fire = _engine.Source(SourceId.Fire).Rms, wind = _engine.Source(SourceId.Wind).Rms, snow = _engine.Source(SourceId.Snow).Rms;
            Debug.Log($"[AudioTest] beds on: fire {fire:F4} wind {wind:F4} snow {snow:F4}");
            Assert.That(fire, Is.GreaterThan(1e-3f));
            Assert.That(wind, Is.GreaterThan(1e-3f));
            Assert.That(snow, Is.GreaterThan(1e-3f));
            Assert.That(_engine.Source(SourceId.Wind).IdleBlocks, Is.EqualTo(idleAtOn), "never idle while on");
            _engine.SetFire(false);
            _engine.SetWind(false);
            _engine.SetSnow(false);
            yield return Wait(2.4f);   // tau 0.4 s: -52 dB after 2.4 s
            Assert.That(_engine.Source(SourceId.Fire).Rms, Is.LessThan(fire * 0.05f));
            Assert.That(_engine.Source(SourceId.Wind).Rms, Is.LessThan(wind * 0.05f));
            Assert.That(_engine.FireOn || _engine.WindOn || _engine.SnowOn, Is.False);
        }

        [UnityTest]
        public IEnumerator BedsThatWereNeverSwitchedOnSkipTheirProcessing()
        {
            // The beds start off (bus 0): from the first block the three sources are idle and cost no per-sample work.
            yield return Wait(0.3f);
            foreach (var id in new[] { SourceId.Fire, SourceId.Wind, SourceId.Snow })
            {
                var s = _engine.Source(id);
                long idle = s.IdleBlocks, blocks = s.BlocksRendered;
                Assert.That(idle, Is.GreaterThan(0), id + " idles while off");
                Assert.That(idle, Is.EqualTo(blocks).Within(2), id + ": every block so far");
                Assert.That(s.Rms, Is.EqualTo(0f));
            }
            Assert.That(_engine.Source(SourceId.PianoMid).IdleBlocks, Is.EqualTo(0), "an instrument source (bus 1) never idles");
            Assert.That(_engine.Source(SourceId.DrumRoom).IdleBlocks, Is.EqualTo(0), "the drum room (bus 0.3) never idles");
        }

        [UnityTest]
        public IEnumerator MixedRealityLowersEveryAcousticSendBySixDecibels()
        {
            Assert.That(_engine.Sources.Length, Is.EqualTo((int)SourceId.Count));
            foreach (var s in _engine.Sources)
            {
                var meta = s.GetComponent<MetaXRAudioSource>();
                if (s.id == SourceId.Ui) { Assert.That(meta, Is.Null, "the Ui source is 2D"); continue; }
                AudioSourceRow row = AudioSourceTable.Current[s.id];
                bool acoustic = s.id <= SourceId.DrumRoom || s.id >= SourceId.Handpan;   // the instruments (classic and the platform's) and the drum room
                Assert.That(row.MetaAcoustics, Is.EqualTo(acoustic), s.id + " in the table");
                Assert.That(meta.EnableAcoustics, Is.EqualTo(acoustic), s.id + ": instruments send to the room, ambience and Sunny do not");
                Assert.That(meta.ReverbSendDb, Is.EqualTo(AudioRig.VrReverbSendDb(s.id)), s.id + " VR send");
                Assert.That(meta.GainBoostDb, Is.EqualTo(AudioRig.HrtfCalibrationDb), s.id + " HRTF calibration");
                if (!acoustic) continue;
                var x = s.GetComponent<MetaXRAudioSourceExperimentalFeatures>();
                Assert.That(x, Is.Not.Null, s.id + " tuning start values");
                Assert.That(x.ReverbReach, Is.EqualTo(AudioRig.InstrumentReverbReach));
                // the table's current row (hand drums R5, made generic at integration): the kit's +3 dB, a descriptor's override
                // where it has one (AudioSourceSpec.ErDb: the hand drums' +2 dB), 0 for everything else
                Assert.That(x.EarlyReflectionsSendDb, Is.EqualTo(row.ErDb), s.id + " early reflections");
                if (s.id >= SourceId.Kick && s.id <= SourceId.Ride) Assert.That(row.ErDb, Is.EqualTo(AudioRig.DrumEarlyReflectionsDb), s.id + ": the kit's +3 dB");
                else if (s.id < SourceId.Handpan) Assert.That(row.ErDb, Is.EqualTo(0f), s.id + ": the classic instruments' early reflections at 0 dB");
            }
            _engine.SetMixedReality(true);
            foreach (var s in _engine.Sources)
            {
                var meta = s.GetComponent<MetaXRAudioSource>();
                if (meta == null || !meta.EnableAcoustics) continue;
                Assert.That(meta.ReverbSendDb, Is.EqualTo(AudioRig.VrReverbSendDb(s.id) - 6f), s.id + " MR send");
            }
            _engine.SetMixedReality(false);
            foreach (var s in _engine.Sources)
            {
                var meta = s.GetComponent<MetaXRAudioSource>();
                if (meta != null && meta.EnableAcoustics) Assert.That(meta.ReverbSendDb, Is.EqualTo(AudioRig.VrReverbSendDb(s.id)));
            }
            Assert.That(_engine.Source(SourceId.PianoLo).resonatorTarget, Is.SameAs(_engine.Source(SourceId.PianoMid)), "one soundboard");
            Assert.That(_engine.Source(SourceId.Kick).sendTarget, Is.SameAs(_engine.Source(SourceId.DrumRoom)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BusyGrooveStaysInsideTheBlockBudget()
        {
            // A heavy session: 16th-note drums with ride and crashes, four-note piano chords, a harp pluck every 125 ms,
            // all three beds; the audio thread's C# must stay under 1.5 ms per 5.33 ms block with nothing dropped or stolen.
            yield return WaitForRenders(30f);
            _engine.SetFire(true);
            _engine.SetWind(true);
            _engine.SetSnow(true);
            _engine.SnowGust(0.8f, 1.1f, 0.6f, 2.2f);
            yield return Wait(0.2f);
            _engine.ResetBlockStats();
            var held = new VoiceHandle[4];
            int step = 0;
            float t0 = Time.realtimeSinceStartup, next = 0f;
            while (Time.realtimeSinceStartup - t0 < 2.5f)
            {
                float t = Time.realtimeSinceStartup - t0;
                while (next <= t)
                {
                    _engine.Drum(step % 4 == 0 ? DrumKind.Kick : step % 4 == 2 ? DrumKind.Snare : DrumKind.Tom1, 0.8f, DrumStrike.Default);
                    _engine.Drum(DrumKind.Hat, 0.6f, new DrumStrike { Pos = 0.5f, Zone = DrumZone.Top });
                    if (step % 2 == 0) _engine.Drum(DrumKind.Ride, 0.6f, new DrumStrike { Pos = 0.5f, Zone = DrumZone.Bow });
                    if (step % 16 == 0) _engine.Drum(DrumKind.Crash, 0.8f, new DrumStrike { Pos = 0.6f, Zone = DrumZone.Bow });
                    if (step % 4 == 0)
                    {
                        for (int i = 0; i < held.Length; i++) _engine.Release(held[i], i == 0);
                        int root = 48 + (step / 4 % 5) * 5;
                        for (int i = 0; i < held.Length; i++) held[i] = _engine.Piano(root + i * 4, 0.7f);
                    }
                    _engine.Pluck(step % 22, 0.6f);
                    step++;
                    next += 0.125f;
                }
                yield return null;
            }
            float avg = _engine.SumAvgBlockMicros, max = _engine.SumMaxBlockMicros;
            Debug.Log($"[AudioTest] busy groove: {step} steps, block avg {avg:F0} us (sum over sources), max {max:F0} us, " +
                      $"drum room {_engine.DrumRoom.Steps} partitions, dropped {_engine.TotalDroppedCommands}, stolen {_engine.TotalStolenVoices}");
            Assert.That(step, Is.GreaterThan(15));
            Assert.That(avg, Is.LessThan(AudioModule.BlockBudgetMicros), "our C# per block (device budget 1.5 ms)");
            Assert.That(_engine.TotalDroppedCommands, Is.EqualTo(0));
            Assert.That(_engine.TotalStolenVoices, Is.EqualTo(0), "no stolen voices in normal play");
            Assert.That(_engine.DrumRoom.Steps, Is.GreaterThan(0), "the drum room convolved the hits");
        }

        [UnityTest]
        public IEnumerator TheRestOfTheEngineCallsAllocateNothing()
        {
            var g = _engine.GuideVoice;
            float t0 = Time.realtimeSinceStartup;
            _engine.PrepareVoiceLine("praise_1");   // lines past the intro render on demand
            while (!g.IsRendered(g.IndexOf("praise_1")) && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            Assert.That(g.IsRendered(g.IndexOf("praise_1")), Is.True, "the line must be rendered (an on-demand render allocates by design)");
            Assert.That(_engine.TestTap, Is.Null);

            // Warm every path once.
            Calls();
            yield return Wait(0.1f);
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");
            Assert.That(() => { for (int r = 0; r < 5; r++) Calls(); }, Is.Not.AllocatingGCMemory());
        }

        void Calls()
        {
            _engine.Chirp(ChirpKind.Close);
            _engine.Swish();
            _engine.SetWind(true);
            _engine.SetSnow(true);
            _engine.SnowGust(0.6f, 1.2f, 0.6f, 2.5f);
            _engine.SnowGust(new SnowGust { Strength = 0.5f, Attack = 1.1f, Hold = 0.4f, Release = 2.2f });
            _engine.SetSnow(false);
            _engine.SetWind(false);
            _engine.PlayVoiceLine("praise_1", _engine.Now + GuideVoice.Lead);
            float level = _engine.VoiceLevel;
            bool speaking = _engine.VoiceLinePlaying;
            if (level < 0 || (speaking && level > 10)) s_keep = null;   // keep the reads
            _engine.StopVoiceLine();
            for (int k = 0; k < 10; k++) _engine.Drum((DrumKind)k, 0.6f, new DrumStrike { Pos = 0.5f, Zone = (DrumZone)(k % 6) });
            _engine.Choke(DrumKind.Crash);
            var h = _engine.Pluck(3, 0.5f);
            _engine.Damp(h, 3);
            _engine.Release(_engine.Pluck(4, 0.4f), thud: false);
            _engine.SetMixedReality(true);
            _engine.SetMixedReality(false);
            _engine.SeedAll(7);
        }
    }

    /// <summary>BootOptions.AudioClock (section 16): IAudioEngine.Now / FrameNow and Sunny's line timing follow the
    /// scripted clock, so Guide and Lessons tests can time against it.</summary>
    public class AudioClockTests
    {
        TestBoot _boot;
        double _t = 100.0;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(manualFrames: true, audioClock: () => _t);
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule);
            yield return _boot.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        [UnityTest]
        public IEnumerator NowFrameNowAndVoiceLinesFollowTheScriptedClock()
        {
            var engine = (AudioEngine)App.Current.Get<IAudioEngine>();
            Assert.That(engine.Now, Is.EqualTo(100.0));
            _t = 101.25;
            Assert.That(engine.Now, Is.EqualTo(101.25), "Now reads the clock on every call");
            yield return null;   // AudioEngine.Update (order -1000) samples FrameNow at the start of the next frame
            Assert.That(engine.FrameNow, Is.EqualTo(101.25));
            Assert.That(App.Current.AudioTime, Is.EqualTo(101.25));

            float seconds = engine.VoiceLineSeconds("bye");
            Assert.That(seconds, Is.GreaterThan(0.2f));
            double end = engine.PlayVoiceLine("bye");
            Assert.That(end, Is.EqualTo(101.25 + seconds).Within(1e-6), "an ASAP line starts at the scripted now");
            Assert.That(engine.VoiceLinePlaying, Is.True);
            _t = end + 0.01;
            Assert.That(engine.VoiceLinePlaying, Is.False, "the line is over on the scripted clock");
            Assert.That(engine.VoiceLevel, Is.EqualTo(0f));
        }
    }
}
