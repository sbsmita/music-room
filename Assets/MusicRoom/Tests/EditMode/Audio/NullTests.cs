// Null tests (ARCHITECTURE section 7.5, spec 06 section 12): every recipe rendered by the engine's own code, offline, with
// the reference seeds, minus the reference render of the same synth (Audio/Reference~, mono float32 48 kHz). The residual
// must be below -60 dB; the reference DSP itself sits at -76..-142 dB, so a failure here is a recipe or timing error.
// Each case logs its residual so the margin is visible in the test log.
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Audio.Offline;
using MusicRoom.Audio.Synths;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public class NullTests
    {
        const double Limit = -60;
        const int Sr = OfflineAudio.Sr;

        static void AssertNull(string file, float[] y, float[] reference)
        {
            double r = Wav.ResidualDb(y, reference);
            Debug.Log($"[AudioNull] {file}: residual {r:F1} dB");
            Assert.That(r, Is.LessThan(Limit), file + " residual (dB)");
        }

        // ---------------- drums (drum synth; dry: direct x 1.7 + the snare wires, no drum room) ----------------

        [TestCase("drum_kick_head_p30_v80.wav", DrumKind.Kick, 0.8f, 0.3f, DrumZone.Head)]
        [TestCase("drum_snare_head_p30_v80.wav", DrumKind.Snare, 0.8f, 0.3f, DrumZone.Head)]
        [TestCase("drum_snare_head_p80_v80.wav", DrumKind.Snare, 0.8f, 0.8f, DrumZone.Head)]
        [TestCase("drum_snare_rim_p95_v90.wav", DrumKind.Snare, 0.9f, 0.95f, DrumZone.Rim)]
        [TestCase("drum_hat_top_p50_v70.wav", DrumKind.Hat, 0.7f, 0.5f, DrumZone.Top)]
        [TestCase("drum_hatOpen_top_p50_v70.wav", DrumKind.HatOpen, 0.7f, 0.5f, DrumZone.Top)]
        [TestCase("drum_hatClose_head_p30_v70.wav", DrumKind.HatClose, 0.7f, 0.3f, DrumZone.Head)]
        [TestCase("drum_tom1_head_p30_v80.wav", DrumKind.Tom1, 0.8f, 0.3f, DrumZone.Head)]
        [TestCase("drum_tom2_head_p30_v80.wav", DrumKind.Tom2, 0.8f, 0.3f, DrumZone.Head)]
        [TestCase("drum_floor_head_p30_v80.wav", DrumKind.Floor, 0.8f, 0.3f, DrumZone.Head)]
        [TestCase("drum_floor_rim_p95_v80.wav", DrumKind.Floor, 0.8f, 0.95f, DrumZone.Rim)]
        [TestCase("drum_crash_bow_p60_v80.wav", DrumKind.Crash, 0.8f, 0.6f, DrumZone.Bow)]
        [TestCase("drum_crash_bell_p10_v80.wav", DrumKind.Crash, 0.8f, 0.1f, DrumZone.Bell)]
        [TestCase("drum_ride_bow_p50_v70.wav", DrumKind.Ride, 0.7f, 0.5f, DrumZone.Bow)]
        [TestCase("drum_ride_bell_p10_v70.wav", DrumKind.Ride, 0.7f, 0.1f, DrumZone.Bell)]
        [TestCase("drum_ride_edge_p90_v70.wav", DrumKind.Ride, 0.7f, 0.9f, DrumZone.Edge)]
        public void DrumHit(string file, DrumKind kind, float vel, float pos, DrumZone zone)
        {
            var reference = Wav.Read(file);
            using (var a = new OfflineAudio())
            {
                a.Drums.Hit(kind, vel, new DrumStrike { Pos = pos, Zone = zone }, 0);
                var listen = new[] { DrumSynth.SourceFor(kind), SourceId.Snare }.Distinct().ToArray();
                AssertNull(file, a.Render(reference.Length, listen), reference);
            }
        }

        // ---------------- piano (buffer seed 1000 + midi, board seed 2, knock, release + damper thud) ----------------

        [TestCase("piano_C4_v040.wav", 60, 0.4f, 120064)]
        [TestCase("piano_C4_v080.wav", 60, 0.8f, 120064)]
        [TestCase("piano_A0_v070.wav", 21, 0.7f, 144000)]
        [TestCase("piano_C7_v070.wav", 96, 0.7f, 67200)]
        public void PianoNote(string file, int midi, float vel, int releaseFrame)
        {
            var reference = Wav.Read(file);
            using (var a = new OfflineAudio())
            {
                a.Bank.Publish(AudioEngine.PianoSlot0 + midi - 21, PianoRenderer.Render(midi, Sr, (uint)(1000 + midi)));
                var h = a.Piano.Play(midi, vel, 0);
                a.Piano.Release(h, true, releaseFrame / (double)Sr);
                // The note's own register plus the one soundboard on piano-mid (A0 and C7 reach it through resonatorTarget).
                AssertNull(file, a.Render(reference.Length, SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi), reference);
            }
        }

        // ---------------- harp, old sound (Karplus-Strong seed 2000 + midi, soundbox seed 3, thud) ----------------

        [TestCase("harp_old_C3_v070.wav", 0)]
        [TestCase("harp_old_G4_v070.wav", 11)]
        [TestCase("harp_old_C6_v070.wav", 21)]
        public void HarpPluck(string file, int stringIndex)
        {
            var reference = Wav.Read(file);
            int midi = HarpRenderer.Strings[stringIndex];
            using (var a = new OfflineAudio())
            {
                a.Bank.Publish(AudioEngine.HarpSlot0 + stringIndex, HarpRenderer.RenderOld(midi, Sr, (uint)(2000 + midi)));
                a.Strings.Pluck(stringIndex, 0.7f, 0, PluckOptions.Default);
                AssertNull(file, a.Render(reference.Length, SourceId.Harp), reference);
            }
        }

        // ---------------- one-shots ----------------

        [Test]
        public void PianoDamperThud()
        {
            var reference = Wav.Read("piano_damper_thud.wav");
            using (var a = new OfflineAudio())
            {
                a.Piano.Release(new VoiceHandle(a.S(SourceId.PianoMid), 12345), true, 0);   // no such voice: only the thud sounds
                AssertNull("piano_damper_thud.wav", a.Render(reference.Length, SourceId.PianoMid), reference);
            }
        }

        [Test]
        public void PianoHammerKnock()
        {
            var reference = Wav.Read("piano_hammer_knock_C4_v070.wav");
            using (var a = new OfflineAudio())
            {
                a.Piano.Play(60, 0.7f, 0);   // the key's buffer is not in the bank: the note waits, the knock sounds alone
                AssertNull("piano_hammer_knock_C4_v070.wav", a.Render(reference.Length, SourceId.PianoMid), reference);
            }
        }

        [Test]
        public void HarpDampShh()
        {
            var reference = Wav.Read("harp_damp_shh.wav");
            using (var a = new OfflineAudio())
            {
                a.Strings.Damp(new VoiceHandle(a.S(SourceId.Harp), 12345), 1, 0);
                AssertNull("harp_damp_shh.wav", a.Render(reference.Length, SourceId.Harp), reference);
            }
        }

        [Test]
        public void CymbalChokeSkin()
        {
            var reference = Wav.Read("cymbal_choke_skin.wav");
            using (var a = new OfflineAudio())
            {
                a.Drums.Choke(DrumKind.Crash);
                AssertNull("cymbal_choke_skin.wav", a.Render(reference.Length, SourceId.Crash), reference);
            }
        }

        [Test]
        public void PageTurnSwish()
        {
            var reference = Wav.Read("page_turn_swish.wav");
            using (var a = new OfflineAudio())
            {
                a.Ui.Swish(0);
                AssertNull("page_turn_swish.wav", a.Render(reference.Length, SourceId.Ui), reference);
            }
        }

        [TestCase("sunny_open_chirps.wav", ChirpKind.Open)]
        [TestCase("sunny_end_chirps.wav", ChirpKind.Close)]
        public void SunnyChirps(string file, ChirpKind kind)
        {
            var reference = Wav.Read(file);
            using (var a = new OfflineAudio(guide: true))
            {
                a.Guide.Chirp(kind, 0);
                AssertNull(file, a.Render(reference.Length, SourceId.Guide), reference);
            }
        }

        // ---------------- ambience (beds switched on at t = 0: bus setTarget(1, 0, 0.4)) ----------------

        [Test]
        public void WindBed()
        {
            var reference = Wav.Read("amb_wind_8s.wav");
            using (var a = new OfflineAudio(ambience: true))
            {
                a.Ambience.SetWind(true);
                AssertNull("amb_wind_8s.wav", a.Render(reference.Length, SourceId.Wind), reference);
            }
        }

        [Test]
        public void SnowHushWithAGustAtTwoSeconds()
        {
            // 06 wavIndex: the gust starts at 2.0 s with strength 0.7, attack 1.5, hold 1, release 3; f0 from mulberry32(11).
            var reference = Wav.Read("amb_snow_hush_gust_at2s_8s.wav");
            using (var a = new OfflineAudio(ambience: true))
            {
                a.Ambience.SetSnow(true);
                var y = a.Render(reference.Length, new[] { SourceId.Snow }, at =>
                {
                    if (at == 2 * Sr) a.Ambience.Gust(0.7f, 1.5f, 1f, 3f);
                });
                Assert.That(a.Ambience.GustsPlayed, Is.EqualTo(1));
                AssertNull("amb_snow_hush_gust_at2s_8s.wav", y, reference);
            }
        }

        [Test]
        public void FireBedAndPops()
        {
            // The reference harness ran the 110 ms timer for ticks 1..51 (5.61 s) of the 6 s render, with mulberry32(11)
            // draws; pops are switched off (not the bed) before tick 52 to match.
            var reference = Wav.Read("amb_fire_6s.wav");
            using (var a = new OfflineAudio(ambience: true))
            {
                a.Ambience.SetFire(true);
                var fire = a.S(SourceId.Fire);
                var y = a.Render(reference.Length, new[] { SourceId.Fire }, at =>
                {
                    if (at == 1056 * OfflineAudio.Block)   // 5.632 s: after tick 51, before tick 52 (5.72 s)
                    {
                        fire.Stage(new SynthCommand { Op = SynthOp.Custom, Kind = SynthCustom.FirePops, Level = 0 });
                        fire.Commit();
                    }
                });
                AssertNull("amb_fire_6s.wav", y, reference);
            }
        }
    }
}
