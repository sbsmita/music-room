// The stabilising pass's pure checks: the one-time settings migration (Lock instruments and Real hands back off), the
// 48 kHz output and no output suspension in ProjectSettings and in what AudioProjectSetup writes, every voice clip's import
// (Load In Background), the 48 kHz-only sound units refusing another rate, the piano letting go of held keys when
// "Move instruments" or placing starts, the looper following a clock jump, Sunny's voice reusing its read buffers, and the
// classic voice pools fading a stolen voice instead of cutting it.
using System;
using System.IO;
using System.Text.RegularExpressions;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Synths;
using MusicRoom.Core;
using MusicRoom.EditorTools;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using MusicRoom.Piano;
using MusicRoom.Tests.Lessons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.Stabilise
{
    public class SettingsMigrationTests
    {
        [Test]
        public void AnOldSavesLockAndRealHandsGoBackOffOnce()
        {
            var store = new MemorySettingsStore();
            var s = new AppSettings(store);
            s.SetBool(AppSettings.KeyLockedOld, true);
            s.MrRealHands = true;
            LogAssert.Expect(LogType.Log, new Regex(@"\[Layout\] settings migrated"));
            Assert.IsTrue(ComfortCodec.MigrateSettings(s), "the first boot after the update migrates");
            Assert.IsFalse(s.GetBool(AppSettings.KeyLockedOld, true), "Lock instruments off");
            Assert.IsFalse(s.MrRealHands, "the drawn hands");
            Assert.That(store.GetInt(ComfortCodec.KeyMigratedStabilise, 0), Is.EqualTo(1));
            // The player's own choice afterwards is kept: the migration never runs again.
            s.MrRealHands = true;
            Assert.IsFalse(ComfortCodec.MigrateSettings(s), "once per install");
            Assert.IsTrue(s.MrRealHands);
        }

        /// <summary>"Lock instruments" is gone (2026-11-10): an old save's musicroom.locked is removed, whichever way it was
        /// set, and "Move instruments" stays off (the instruments start fixed, as Lock on meant); a later save never has it.</summary>
        [Test]
        public void AnOldLockSettingIsRemovedAndMoveInstrumentsStartsOff()
        {
            foreach (bool locked in new[] { true, false })
            {
                var store = new MemorySettingsStore();
                var s = new AppSettings(store);
                s.SetBool(AppSettings.KeyLockedOld, locked);
                LogAssert.Expect(LogType.Log, new Regex(@"\[Layout\] settings migrated: Lock instruments \(" + (locked ? "on" : "off") + @"\) removed; Move instruments off"));
                Assert.IsTrue(ComfortCodec.MigrateMoveSetting(s), "the old key is found");
                Assert.IsFalse(store.HasKey(AppSettings.KeyLockedOld), "and removed");
                Assert.IsFalse(s.MoveInstruments, "Move instruments off: the instruments stay put");
                Assert.That(store.Saves, Is.EqualTo(1));
                Assert.IsFalse(ComfortCodec.MigrateMoveSetting(s), "nothing left to migrate");
            }
            // A player who turned Move instruments on keeps it, even if an old Lock key is still around.
            var kept = new AppSettings(new MemorySettingsStore());
            kept.MoveInstruments = true;
            kept.SetBool(AppSettings.KeyLockedOld, true);
            LogAssert.Expect(LogType.Log, new Regex(@"Move instruments on"));
            Assert.IsTrue(ComfortCodec.MigrateMoveSetting(kept));
            Assert.IsTrue(kept.MoveInstruments);
        }
    }

    public class AudioSettingsTests
    {
        [Test]
        public void TheProjectRunsAt48kHzWithoutOutputSuspension()
        {
            string asset = File.ReadAllText("ProjectSettings/AudioManager.asset");
            Assert.That(asset, Does.Contain("m_SampleRate: " + AudioProjectSetup.SampleRate));
            Assert.That(asset, Does.Contain("m_EnableOutputSuspension: " + AudioProjectSetup.OutputSuspension));
            Assert.That(AudioProjectSetup.SampleRate, Is.EqualTo(48000), "what Tools/unity.sh configure writes back");
            Assert.That(AudioProjectSetup.OutputSuspension, Is.EqualTo(0));
            Assert.That(AudioModule.DeviceSampleRate, Is.EqualTo(AudioProjectSetup.SampleRate), "the runtime fallback asks for the same rate");
        }

        [Test]
        public void EveryVoiceClipImportsInTheBackground()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/MusicRoom" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!GuideVoiceImport.IsVoiceClip(path)) continue;
                n++;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert.IsTrue(GuideVoiceImport.Conforms(importer), path + ": Decompress On Load, preloaded, Load In Background, mono, Vorbis 0.7");
            }
            Assert.That(n, Is.GreaterThan(150), "every module's voice clips");
        }

        [Test]
        public void TheBlockBudgetIsOneNumberScaledByTheBlock()
        {
            Assert.That(AudioModule.BlockBudgetFor(256), Is.EqualTo(1500f));
            Assert.That(AudioModule.BlockBudgetFor(128), Is.EqualTo(750f));
            var meter = new MusicRoom.Audio.Burst.DspLoadMeter();
            Assert.That(meter.BudgetMicros, Is.EqualTo(1500f), "the meter's budget before a block is recorded: 256 frames");
        }

        [Test]
        public void The48kHzOnlyUnitsRefuseAnotherRateWithOneLineEach()
        {
            var ctx = new SoundContext(null, null, 24000, 1, () => 0, true);
            foreach (UnitId role in new[] { UnitId.HandDrums, UnitId.Handpan, UnitId.Marimba, UnitId.Bowls })
            {
                Assert.IsTrue(SoundUnits.NeedsUnitRate(role), role + " is 48 kHz only");
                LogAssert.Expect(LogType.Error, new Regex(@"\[Audio\] sound unit " + role + @" is built for 48000 Hz and the output runs at 24000 Hz"));
                Assert.IsNull(SoundUnits.Create(role, ctx), role + " is not started at 24 kHz");
            }
            foreach (UnitId role in new[] { UnitId.Piano, UnitId.Drums, UnitId.Harp, UnitId.Room })
                Assert.IsFalse(SoundUnits.NeedsUnitRate(role), role + " has no unit");
        }
    }

    public class HeldNotesTests
    {
        sealed class Sink : IPianoSink
        {
            public int Offs;
            public VoiceHandle NoteOn(int midi, float vel) => default;
            public void NoteOff(int midi, VoiceHandle voice, bool thud) => Offs++;
            public void Record(in TakeEvent ev) { }
        }

        [Test]
        public void ReleaseFingersLetsGoOfEveryHeldKeyButThePlaybacks()
        {
            var p = new PianoAction(new Clock(), new Sink());
            object finger = new object(), mouse = new object();
            p.PressMidi(60, finger);
            p.PressMidi(64, mouse);
            p.PlaybackOn(67, 0.7f);
            Assert.That(p.HeldKeyCount, Is.EqualTo(3));
            p.ReleaseFingers();
            Assert.That(p.HeldKeyCount, Is.EqualTo(1), "only the playback's key stays down");
            Assert.IsTrue(p.KeyByMidi(67).IsPressedBy(p.Ghost));
            Assert.That(p.KeyByMidi(60).PressedBy.Count, Is.Zero);
        }
    }

    public class LooperClockJumpTests
    {
        [Test]
        public void ALoopFollowsTheClockBackToZeroAndLetsGoOfWhatItHeld()
        {
            var host = new FakeLoopHost();
            var loop = new Looper(host) { InputLatency = 0, Tidy = false };
            void Tick(double s) { double end = host.T + s; while (host.T < end - 1e-12) { host.T = Math.Min(end, host.T + 1.0 / 72); loop.Tick(); } }
            loop.PressLoop();
            Tick(0.2);
            loop.Capture(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            Tick(6.0);
            Assert.That(loop.State, Is.Not.EqualTo(LoopState.Off));
            long anchor = loop.Anchor;
            int sent = host.Sent.Count, releases = host.ReleaseAlls;
            // The output restarted: the audio clock is back near 0.
            double before = host.T;
            host.T = 0.5;
            loop.ClockJumped(host.T - before);
            Assert.That(loop.ClockJumps, Is.EqualTo(1));
            Assert.That(host.ReleaseAlls, Is.GreaterThan(releases), "what the loop held is let go");
            Assert.That(loop.Anchor, Is.EqualTo(anchor + (long)Math.Round((host.T - before) * 48000)), "the grid moved with the clock");
            Assert.That(loop.Dispatched, Is.EqualTo((long)Math.Round(host.T * 48000)), "sending starts again from now");
            Tick(6.0);
            Assert.That(host.Sent.Count, Is.GreaterThan(sent), "the loop plays on after the jump (not silent until the old time comes back)");
            foreach (var x in host.Sent.GetRange(sent, host.Sent.Count - sent))
                Assert.That(x.when, Is.LessThan(host.T + 1.0), "every event after the jump is on the new clock");
        }
    }

    public class VoiceBufferTests
    {
        [UnityTest]
        public System.Collections.IEnumerator SunnysClipsAreReadIntoPooledBuffers()
        {
            var bank = new SampleBank(AudioEngine.BankSlots(GuideVoice.LineCount));
            var v = new GuideVoice(null, 48000);
            v.Attach(bank, AudioEngine.VoiceSlot0);
            v.ResidentLimit = 64;
            Assert.That(v.VerifyClips(), Is.GreaterThan(150), "every line's clip exists");
            string[] ids = { "intro_vr", "praise_3", "tip_move_turn_on", "tip_mr_hands", "tip_move_quiet", "tip_turn" };
            // Asked one after another (no worker: each renders as soon as its data is in): the read buffer comes back each time.
            foreach (string id in ids)
            {
                v.Prepare(id);
                float t0 = Time.realtimeSinceStartup;
                while (!v.IsRendered(v.IndexOf(id)) && Time.realtimeSinceStartup - t0 < 10f) { v.Tick(); yield return null; }
                Assert.IsTrue(v.IsRendered(v.IndexOf(id)), id + " rendered (its background load finished in Tick)");
            }
            Assert.That(v.RawBuffersAllocated, Is.LessThanOrEqualTo(1), "one read buffer served every line");
            v.Dispose();
        }
    }

    public class ClassicStealFadeTests
    {
        [Test]
        public void AStolenVoiceFadesOutInsteadOfCutting()
        {
            var go = new GameObject("steal");
            try
            {
                var src = go.AddComponent<SynthSource>();
                src.InitOffline(48000);
                var block = new float[256];
                long t = 0;
                // Fill the tone pool (8 voices) with long notes, then one more: the oldest is stolen.
                for (int i = 0; i < 8; i++)
                    src.Post(new SynthCommand { Op = SynthOp.StartTone, When = 0, Wave = Wave.Sine, F0 = 220 + 7 * i, F1 = 220 + 7 * i, Slide = 0, Peak = 0.05f, Decay = 5f, Lp = 20000, Level = 1 });
                for (int b = 0; b < 4; b++) { src.RenderBlock(block, 1, t); t += 256; }
                int stolen0 = src.StolenVoices;
                src.Post(new SynthCommand { Op = SynthOp.StartTone, When = 0, Wave = Wave.Sine, F0 = 880, F1 = 880, Slide = 0, Peak = 0.05f, Decay = 5f, Lp = 20000, Level = 1 });
                src.RenderBlock(block, 1, t);
                Assume.That(src.StolenVoices, Is.GreaterThan(stolen0), "the pool was full: a voice was stolen");
                Assert.That(src.GhostFades, Is.GreaterThan(0), "the stolen voice fades from the ghost slot");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
