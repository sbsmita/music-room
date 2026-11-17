// AudioSourceTable (12-new-instruments/platform.md 10.1-10.2): one row per SourceId replaces AudioRig's range checks. The
// 18 classic rows must give exactly what the old functions gave; Legacy below is those functions as they were before the
// table (AudioRig.VrReverbSendDb, DrumRoomSend, Build's acoustics / reach / early reflections, Configure's pools and
// bodies, the instrument sources), copied verbatim. The platform's 7 rows: spatial, unit-only (no pools), Meta
// acoustics on, reach 0.7, the final instrument specs' sends. The registry's per-source overrides.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Audio
{
    public class AudioSourceTableTests
    {
        /// <summary>The functions the table replaced, as they were (2026-11-01, before the platform).</summary>
        static class Legacy
        {
            public static float VrReverbSendDb(SourceId id)
            {
                if (id == SourceId.Harp) return 3f;
                if (id >= SourceId.Kick && id <= SourceId.Ride) return -3f;
                if (id == SourceId.DrumRoom) return 0f;
                return 0f; // piano
            }
            public static bool Spatial(SourceId id) => id != SourceId.Ui;
            public static bool Acoustics(SourceId id) => id <= SourceId.DrumRoom;
            public static bool Tuned(SourceId id) => id <= SourceId.DrumRoom;   // MetaXRAudioSourceExperimentalFeatures with reach 0.7
            public static float ErDb(SourceId id) => id >= SourceId.Kick && id <= SourceId.Ride ? 3f : 0f;
            public static float DrumRoomSend(SourceId id)
            {
                switch (id)
                {
                    case SourceId.Kick: return 0.35f;
                    case SourceId.Crash: case SourceId.Ride: return 0.2f;
                    case SourceId.Snare: case SourceId.Hat: case SourceId.Tom1: case SourceId.Tom2: case SourceId.Floor: return 0.5f;
                    default: return 0f;
                }
            }
            public static void Configure(SynthSource s, SourceId id)
            {
                s.noiseVoices = 0; s.toneVoices = 0; s.metalVoices = 0; s.bufferVoices = 0;
                switch (id)
                {
                    case SourceId.PianoLo:
                    case SourceId.PianoHi:
                        s.noiseVoices = 16; s.bufferVoices = 48;
                        break;
                    case SourceId.PianoMid:
                        s.noiseVoices = 16; s.bufferVoices = 48;
                        s.resonatorModes = AudioRig.PianoBoardModes; s.resonatorSeconds = 0.28f; s.resonatorGain = 0.22f;
                        break;
                    case SourceId.Harp:
                        s.noiseVoices = 24; s.bufferVoices = 48;
                        s.resonatorModes = AudioRig.HarpBoxModesOld; s.resonatorSeconds = 0.3f; s.resonatorGain = 0.24f;
                        break;
                    case SourceId.Kick: s.toneVoices = 4; s.noiseVoices = 6; break;
                    case SourceId.Snare: s.toneVoices = 8; s.noiseVoices = 16; break;
                    case SourceId.Hat: s.metalVoices = 8; s.noiseVoices = 10; break;
                    case SourceId.Tom1:
                    case SourceId.Tom2:
                    case SourceId.Floor: s.toneVoices = 12; s.noiseVoices = 6; break;
                    case SourceId.Crash: s.toneVoices = 4; s.metalVoices = 10; s.noiseVoices = 24; break;
                    case SourceId.Ride: s.toneVoices = 12; s.metalVoices = 12; s.noiseVoices = 14; break;
                    case SourceId.DrumRoom: s.busGain = 0.3f; break;
                    case SourceId.Fire:
                    case SourceId.Wind:
                    case SourceId.Snow: s.busGain = 0f; break;
                    case SourceId.Guide: s.toneVoices = 8; s.bufferVoices = 4; break;
                    case SourceId.Ui: s.noiseVoices = 8; s.toneVoices = 8; break;
                }
            }
            public static readonly SourceId[] TapSources =
            {
                SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi, SourceId.Harp,
                SourceId.Kick, SourceId.Snare, SourceId.Hat, SourceId.Tom1, SourceId.Tom2, SourceId.Floor, SourceId.Crash, SourceId.Ride,
            };
        }

        static readonly SourceId[] Platform =
        {
            SourceId.Handpan, SourceId.HandDrumLo, SourceId.HandDrumHi, SourceId.MarimbaLo, SourceId.MarimbaMid, SourceId.MarimbaHi,
            SourceId.Bowls,
        };

        [TearDown]
        public void TearDown() => AudioSourceTable.Current = null;

        [Test]
        public void TheClassicSourceIdsDoNotMoveAndThePlatformAddsEight()
        {
            Assert.That((int)SourceId.Ui, Is.EqualTo(17), "values 0-17 are persisted and tested");
            Assert.That((int)SourceId.DrumRoom, Is.EqualTo(12));
            Assert.That((int)SourceId.Handpan, Is.EqualTo(18), "appended after Ui");
            Assert.That((int)SourceId.Bowls, Is.EqualTo(24));
            Assert.That((int)SourceId.Count, Is.EqualTo(25));
            Assert.That(AudioSourceTable.Defaults.Count, Is.EqualTo(25), "one row per source");
            for (int i = 0; i < (int)SourceId.Count; i++) Assert.That(AudioSourceTable.Defaults[i].Id, Is.EqualTo((SourceId)i));
        }

        [Test]
        public void TheClassicRowsGiveExactlyWhatTheOldFunctionsGave()
        {
            var t = AudioSourceTable.Defaults;
            for (var id = SourceId.PianoLo; id <= SourceId.Ui; id++)
            {
                AudioSourceRow r = t[id];
                Assert.That(r.Spatial, Is.EqualTo(Legacy.Spatial(id)), id + " spatial");
                Assert.That(r.MetaAcoustics, Is.EqualTo(Legacy.Acoustics(id)), id + " Meta acoustics");
                Assert.That(r.SendDb, Is.EqualTo(Legacy.VrReverbSendDb(id)), id + " VR reverb send");
                Assert.That(AudioRig.VrReverbSendDb(id), Is.EqualTo(Legacy.VrReverbSendDb(id)), id + " AudioRig.VrReverbSendDb");
                Assert.That(r.Reach > 0f, Is.EqualTo(Legacy.Tuned(id)), id + " tuned reach");
                if (Legacy.Tuned(id)) Assert.That(r.Reach, Is.EqualTo(AudioRig.InstrumentReverbReach), id + " reach 0.7");
                Assert.That(r.ErDb, Is.EqualTo(Legacy.ErDb(id)), id + " early reflections");
                Assert.That(r.HumanVoice, Is.EqualTo(id == SourceId.Guide), id + " directivity");
                Assert.That(r.DrumRoomSend, Is.EqualTo(Legacy.DrumRoomSend(id)), id + " drum-room send");
                Assert.That(AudioRig.DrumRoomSend(id), Is.EqualTo(Legacy.DrumRoomSend(id)), id + " AudioRig.DrumRoomSend");
                Assert.That(r.Instrument, Is.EqualTo(Array.IndexOf(Legacy.TapSources, id) >= 0), id + " an instrument source");
            }
        }

        [Test]
        public void ConfigureAndConnectBuildTheClassicSourcesAsBefore()
        {
            var host = new GameObject("AudioSourceTableTests") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var now = new SynthSource[(int)SourceId.Count];
                for (int i = 0; i < now.Length; i++)
                {
                    var id = (SourceId)i;
                    var a = new GameObject("now " + id).AddComponent<SynthSource>();
                    var b = new GameObject("was " + id).AddComponent<SynthSource>();
                    a.transform.SetParent(host.transform); b.transform.SetParent(host.transform);
                    AudioRig.Configure(a, id);
                    if (id <= SourceId.Ui) Legacy.Configure(b, id);
                    now[i] = a;
                    if (id > SourceId.Ui)
                    {
                        Assert.That(a.noiseVoices + a.toneVoices + a.metalVoices + a.bufferVoices, Is.EqualTo(0), id + ": unit-only, no pools");
                        Assert.That(a.resonatorModes, Is.Empty, id + ": no body");
                        Assert.That(a.busGain, Is.EqualTo(1f));
                        continue;
                    }
                    Assert.That(a.noiseVoices, Is.EqualTo(b.noiseVoices), id + " noise voices");
                    Assert.That(a.toneVoices, Is.EqualTo(b.toneVoices), id + " tone voices");
                    Assert.That(a.metalVoices, Is.EqualTo(b.metalVoices), id + " metal voices");
                    Assert.That(a.bufferVoices, Is.EqualTo(b.bufferVoices), id + " buffer voices");
                    Assert.That(a.resonatorModes, Is.EqualTo(b.resonatorModes), id + " body modes");
                    Assert.That(a.resonatorSeconds, Is.EqualTo(b.resonatorSeconds), id + " body seconds");
                    Assert.That(a.resonatorGain, Is.EqualTo(b.resonatorGain), id + " body gain");
                    Assert.That(a.busGain, Is.EqualTo(b.busGain), id + " bus");
                }
                AudioRig.Connect(now);
                for (int i = 0; i < now.Length; i++)
                {
                    var id = (SourceId)i;
                    bool piece = id >= SourceId.Kick && id <= SourceId.Ride;
                    Assert.That(now[i].sendTarget, Is.SameAs(piece ? now[(int)SourceId.DrumRoom] : null), id + " send target");
                    Assert.That(now[i].sendGain, Is.EqualTo(piece ? Legacy.DrumRoomSend(id) : 0f), id + " send gain");
                    bool board = id == SourceId.PianoLo || id == SourceId.PianoHi;
                    Assert.That(now[i].resonatorTarget, Is.SameAs(board ? now[(int)SourceId.PianoMid] : null), id + " soundboard");
                }
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ThePlatformRowsAreUnitOnlyWithMetaAcousticsOn()
        {
            var t = AudioSourceTable.Defaults;
            var send = new Dictionary<SourceId, float>
            {
                { SourceId.Handpan, 0f }, { SourceId.HandDrumLo, -3f }, { SourceId.HandDrumHi, -3f }, { SourceId.MarimbaLo, -2f },
                { SourceId.MarimbaMid, 0f }, { SourceId.MarimbaHi, -2f }, { SourceId.Bowls, -1f },
            };
            foreach (SourceId id in Platform)
            {
                AudioSourceRow r = t[id];
                Assert.That(r.Spatial, Is.True, id + " spatial (MetaXRAudioSource, HRTF calibration +2.4 dB)");
                Assert.That(r.MetaAcoustics, Is.True, id + ": Meta's shoebox, the room every instrument plays in (the classic room, the only one since 2026-11-10)");
                Assert.That(r.NoiseVoices + r.ToneVoices + r.MetalVoices + r.BufferVoices, Is.EqualTo(0), id + ": unit-only (no pools)");
                Assert.That(r.Reach, Is.EqualTo(0.7f), id + " reach");
                Assert.That(r.SendDb, Is.EqualTo(send[id]), id + " VR send");
                bool hand = id == SourceId.HandDrumLo || id == SourceId.HandDrumHi;
                Assert.That(r.ErDb, Is.EqualTo(hand ? 3f : 0f), id + " early reflections (+3 for the hand drums, like the kit's pieces)");
                Assert.That(r.DrumRoomSend, Is.EqualTo(0f), id + ": no classic drum room");
                Assert.That(r.Instrument, Is.True, id + " is an instrument source");
            }
        }

        static InstrumentDescriptor Desc(InstrumentKind kind, int carousel, params AudioSourceSpec[] sources) => new InstrumentDescriptor
        {
            Kind = kind, Rig = (RigId)(int)kind, Id = "t" + (int)kind, Name = "T", Carousel = carousel, Traits = InstrumentTraits.Playable,
            Audio = new AudioSpec { Unit = (byte)(int)kind, Sources = sources },
        };

        [Test]
        public void ADescriptorsFiniteSendsOverrideItsSourcesRows()
        {
            var reg = InstrumentRegistry.Build(new[]
            {
                Desc(InstrumentKind.Handpan, 50, new AudioSourceSpec((int)SourceId.Handpan, Vector3.zero, sendDb: -7f, erDb: 1.5f)),
                Desc(InstrumentKind.Marimba, 51, new AudioSourceSpec((int)SourceId.MarimbaLo, Vector3.zero, sendDb: -4f),
                    new AudioSourceSpec((int)SourceId.MarimbaHi, Vector3.zero)),
            });
            var t = AudioSourceTable.Build(reg);
            Assert.That(t[SourceId.Handpan].SendDb, Is.EqualTo(-7f));
            Assert.That(t[SourceId.Handpan].ErDb, Is.EqualTo(1.5f));
            Assert.That(t[SourceId.MarimbaLo].SendDb, Is.EqualTo(-4f));
            Assert.That(t[SourceId.MarimbaLo].ErDb, Is.EqualTo(AudioSourceTable.Defaults[SourceId.MarimbaLo].ErDb), "NaN keeps the row's value");
            Assert.That(t[SourceId.MarimbaHi].SendDb, Is.EqualTo(AudioSourceTable.Defaults[SourceId.MarimbaHi].SendDb), "no override");
            for (int i = 0; i <= (int)SourceId.Ui; i++) Assert.That(t[i].SendDb, Is.EqualTo(AudioSourceTable.Defaults[i].SendDb), (SourceId)i + " untouched");
            Assert.That(AudioSourceTable.Defaults[SourceId.Handpan].SendDb, Is.EqualTo(0f), "the defaults never change");
            AudioSourceTable.Current = t;
            Assert.That(AudioRig.VrReverbSendDb(SourceId.Handpan), Is.EqualTo(-7f), "the boot's table (AudioModule sets it from the registry)");
            AudioSourceTable.Current = null;
            Assert.That(AudioRig.VrReverbSendDb(SourceId.Handpan), Is.EqualTo(0f), "back to the defaults");

            // the app's registry: every descriptor's sources are table rows, and its overrides are the table's own values today
            var app = InstrumentRegistry.Discover();
            var live = AudioSourceTable.Build(app);
            foreach (var d in app.All)
                foreach (var s in d.Audio.Sources)
                {
                    Assert.That(s.Source, Is.InRange(0, (int)SourceId.Count - 1), d.Id + ": a SourceId");
                    Assert.That(live[s.Source].Instrument || (SourceId)s.Source == SourceId.DrumRoom, Is.True, d.Id + ": an instrument source");
                    if (live[s.Source].SendDb != AudioSourceTable.Defaults[s.Source].SendDb)
                        Debug.Log($"[AudioSourceTable] {d.Id} overrides {(SourceId)s.Source} send: {AudioSourceTable.Defaults[s.Source].SendDb} -> {live[s.Source].SendDb} dB");
                }
        }
    }
}
