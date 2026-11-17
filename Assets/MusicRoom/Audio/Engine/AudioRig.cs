// Builds the room's synth sources (one per classic spatial source name + the drum room, and the many-instrument
// platform's unit-only sources) under a host object (spec 04 section 4.2, decisions in the task). Every per-source value
// (spatial, Meta acoustics, reverb send, reach, early reflections, the four voice pools, the drum-room send) is one row
// of AudioSourceTable (12-new-instruments/platform.md 10.1-10.2); the classic 18 rows keep exactly the
// values the range checks used to give (AudioSourceTableTests pins them). Main thread, once. Sources are parented to
// their rigs afterwards (AudioEngine.SetSourceParent).
using System;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace MusicRoom.Audio
{
    /// <summary>One SourceId's audio settings (AudioSourceTable). Plain data: the table is the one place in Audio, beside the
    /// role folders, that names the sources.</summary>
    public struct AudioSourceRow
    {
        public SourceId Id;
        /// <summary>A 3D source (MetaXRAudioSource with the HRTF calibration); false = 2D (the Ui source).</summary>
        public bool Spatial;
        /// <summary>Meta's room acoustics (reflections and reverb): the room every instrument plays in (the classic room, the
        /// only one since 2026-11-10).</summary>
        public bool MetaAcoustics;
        /// <summary>Meta reverb send in VR (dB); mixed reality lowers it by AudioRig.MrSendDropDb.</summary>
        public float SendDb;
        /// <summary>MetaXRAudioSourceExperimentalFeatures.ReverbReach (0 = the component is not tuned for an instrument).</summary>
        public float Reach;
        /// <summary>Early-reflections send (dB, with Reach).</summary>
        public float ErDb;
        /// <summary>Sunny's voice: Meta's human-voice directivity.</summary>
        public bool HumanVoice;
        /// <summary>The four voice pools (today's synths); all 0 for a unit-only source.</summary>
        public int NoiseVoices, ToneVoices, MetalVoices, BufferVoices;
        /// <summary>Send into the drum room (the drum:room return; 0 = none).</summary>
        public float DrumRoomSend;
        /// <summary>An instrument's source: stamped by a "schedule at" scope.</summary>
        public bool Instrument;
        public override string ToString() => $"{Id}: spatial {Spatial} acoustics {MetaAcoustics} send {SendDb} reach {Reach} er {ErDb} pools {NoiseVoices}/{ToneVoices}/{MetalVoices}/{BufferVoices}";
    }

    /// <summary>One row per SourceId (platform.md 10.2). Defaults: the code's rows. Current: the rows this boot uses, the
    /// defaults with every descriptor's finite AudioSourceSpec.SendDb / ErDb applied (AudioModule.Init sets it from the
    /// registry; until then it is Defaults).</summary>
    public sealed class AudioSourceTable
    {
        readonly AudioSourceRow[] _rows;

        AudioSourceTable(AudioSourceRow[] rows) { _rows = rows; }

        public int Count => _rows.Length;
        public AudioSourceRow this[SourceId id] => _rows[(int)id];
        public AudioSourceRow this[int id] => _rows[id];

        public static readonly AudioSourceTable Defaults = new AudioSourceTable(BuildDefaults());
        static AudioSourceTable s_current;
        public static AudioSourceTable Current { get => s_current ?? Defaults; set => s_current = value; }

        /// <summary>The defaults with the registry's per-source overrides (every descriptor in All: a finite SendDb / ErDb of
        /// one of its Audio.Sources replaces that row's value).</summary>
        public static AudioSourceTable Build(IInstrumentRegistry registry)
        {
            var rows = BuildDefaults();
            if (registry == null) return new AudioSourceTable(rows);
            foreach (InstrumentDescriptor d in registry.All)
            {
                if (d?.Audio?.Sources == null) continue;
                foreach (AudioSourceSpec s in d.Audio.Sources)
                {
                    if ((uint)s.Source >= (uint)rows.Length) continue;
                    if (!float.IsNaN(s.SendDb) && !float.IsInfinity(s.SendDb)) rows[s.Source].SendDb = s.SendDb;
                    if (!float.IsNaN(s.ErDb) && !float.IsInfinity(s.ErDb)) rows[s.Source].ErDb = s.ErDb;
                }
            }
            return new AudioSourceTable(rows);
        }

        static AudioSourceRow Instrument(SourceId id, float sendDb, float erDb)
            => new AudioSourceRow { Id = id, Spatial = true, MetaAcoustics = true, SendDb = sendDb, Reach = AudioRig.InstrumentReverbReach, ErDb = erDb, Instrument = true };

        static AudioSourceRow Piece(SourceId id, float drumRoom, int tone, int noise, int metal = 0)
        {
            var r = Instrument(id, -3f, AudioRig.DrumEarlyReflectionsDb);
            r.DrumRoomSend = drumRoom; r.ToneVoices = tone; r.NoiseVoices = noise; r.MetalVoices = metal;
            return r;
        }

        /// <summary>Pools cover the reference's unbounded polyphony for 16th-note playing at 120 bpm (8 hits/s x the longest
        /// layer: floor tom 1.35 s, crash wash 2.5 s, ride edge 1.45 s) so voices are rarely stolen. Drum-room sends: the drum
        /// synth's per kind (kick .35, crash/ride .2, others .5) x the return gain 0.3 (on the drum room's bus). The new sources:
        /// unit-only (no pools), the final instrument specs' sends (handpan 0, hand drums -3 / ER +3, marimba -2 / 0 / -2,
        /// bowls -1), reach 0.7, Meta acoustics on (they play in Meta's shoebox like the classic three).</summary>
        static AudioSourceRow[] BuildDefaults()
        {
            var t = new AudioSourceRow[(int)SourceId.Count];
            void Set(AudioSourceRow r) => t[(int)r.Id] = r;
            var lo = Instrument(SourceId.PianoLo, 0f, 0f); lo.NoiseVoices = 16; lo.BufferVoices = 48; Set(lo);
            var mid = Instrument(SourceId.PianoMid, 0f, 0f); mid.NoiseVoices = 16; mid.BufferVoices = 48; Set(mid);
            var hi = Instrument(SourceId.PianoHi, 0f, 0f); hi.NoiseVoices = 16; hi.BufferVoices = 48; Set(hi);
            var harp = Instrument(SourceId.Harp, 3f, 0f); harp.NoiseVoices = 24; harp.BufferVoices = 48; Set(harp);   // 22 ringing + 22 releasing re-plucks
            Set(Piece(SourceId.Kick, 0.35f, 4, 6));
            Set(Piece(SourceId.Snare, 0.5f, 8, 16));
            Set(Piece(SourceId.Hat, 0.5f, 0, 10, 8));
            Set(Piece(SourceId.Tom1, 0.5f, 12, 6));
            Set(Piece(SourceId.Tom2, 0.5f, 12, 6));
            Set(Piece(SourceId.Floor, 0.5f, 12, 6));
            Set(Piece(SourceId.Crash, 0.2f, 4, 24, 10));
            Set(Piece(SourceId.Ride, 0.2f, 12, 14, 12));
            var room = Instrument(SourceId.DrumRoom, 0f, 0f); room.Instrument = false; Set(room);   // the classic kit's room return
            foreach (var id in new[] { SourceId.Fire, SourceId.Wind, SourceId.Snow })
                Set(new AudioSourceRow { Id = id, Spatial = true });                   // ambience: no room send
            Set(new AudioSourceRow { Id = SourceId.Guide, Spatial = true, HumanVoice = true, ToneVoices = 8, BufferVoices = 4 });
            Set(new AudioSourceRow { Id = SourceId.Ui, Spatial = false, NoiseVoices = 8, ToneVoices = 8 });
            Set(Instrument(SourceId.Handpan, 0f, 0f));
            Set(Instrument(SourceId.HandDrumLo, -3f, 3f));
            Set(Instrument(SourceId.HandDrumHi, -3f, 3f));
            Set(Instrument(SourceId.MarimbaLo, -2f, 0f));
            Set(Instrument(SourceId.MarimbaMid, 0f, 0f));
            Set(Instrument(SourceId.MarimbaHi, -2f, 0f));
            Set(Instrument(SourceId.Bowls, -1f, 0f));
            for (int i = 0; i < t.Length; i++)
                if (t[i].Id != (SourceId)i) throw new InvalidOperationException($"AudioSourceTable: no row for {(SourceId)i}");
            return t;
        }
    }

    public static class AudioRig
    {
        public static readonly float[] PianoBoardModes = { 95, 142, 187, 236, 298, 372, 450, 560 };   // spec 06 section 3
        public static readonly float[] HarpBoxModesOld = { 138, 205, 286, 377, 515, 690 };            // spec 06 section 5.1

        /// <summary>Reverb send per source in VR (dB, AudioSourceTable.Current); mixed reality lowers each by MrSendDropDb.</summary>
        public static float VrReverbSendDb(SourceId id) => AudioSourceTable.Current[id].SendDb;
        public const float MrSendDropDb = 6f;

        /// <summary>Tuning start values (spec 04 section 4.7, ARCHITECTURE 7.5): instruments keep their wet level up with
        /// distance (reverb reach 0.7, Meta's default 0.5; the reference sends are pre-panner, so its wet level never falls)
        /// and the drum pieces get +3 dB early reflections. Sources stay points, like point-source HRTF panners (no volumetric
        /// radius). To be tuned on the headset.</summary>
        public const float InstrumentReverbReach = 0.7f, DrumEarlyReflectionsDb = 3f;

        /// <summary>Front-loudness calibration to the reference renders' HRTF level (ARCHITECTURE 7.5). Measured in the editor's
        /// Meta XR Audio plugin (HrtfLevelTests, white noise at 0.5 m, master bypassed, after the rig has settled): front
        /// -7.4 dB per ear against the reference -5.0, so every spatial source gets +2.4 dB (MetaXRAudioSource.GainBoostDb).
        /// With it: 90 deg right L -22.2 / R -1.0 (reference -13.4 / -1.3), behind -11.8 (reference -8.9). (A first
        /// value of +4.4 came from a front measurement taken while the editor rig jumped to its desktop pose.) Re-check
        /// on the headset.</summary>
        public const float HrtfCalibrationDb = 2.4f;

        /// <summary>The drum synth's room send per kind (kick .35, crash/ride .2, others .5) x the return gain 0.3.</summary>
        public static float DrumRoomSend(SourceId id) => AudioSourceTable.Current[id].DrumRoomSend;

        /// <summary>seed: the body resonators' phases (0 = per session, like randomly drawn IRs).</summary>
        public static SynthSource[] Build(GameObject host, AudioMixerGroup output, uint seed = 0)
        {
            if (seed == 0) seed = Mulberry32.SessionSeed();
            s_seed = seed;
            var table = AudioSourceTable.Current;
            var sources = new SynthSource[(int)SourceId.Count];
            for (int i = 0; i < (int)SourceId.Count; i++)
            {
                var id = (SourceId)i;
                AudioSourceRow row = table[id];
                var go = new GameObject("Synth " + id);
                go.SetActive(false); // so SynthSource.Awake runs after its fields are set
                go.transform.SetParent(host.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;   // no clip-less play before SynthSource.Awake has configured the source (OnEnable plays it)
                if (row.Spatial)
                {
                    var meta = go.AddComponent<MetaXRAudioSource>();
                    meta.EnableSpatialization = true;
                    meta.GainBoostDb = HrtfCalibrationDb;
                    // Ambience and the guide have no room send; the instruments do (tune on the headset).
                    meta.EnableAcoustics = row.MetaAcoustics;
                    meta.ReverbSendDb = row.SendDb;
                    if (row.HumanVoice)
                    {
                        var x = go.AddComponent<MetaXRAudioSourceExperimentalFeatures>();
                        x.DirectivityPattern = MetaXRAudioSourceExperimentalFeatures.DirectivityPatternType.HumanVoice;
                    }
                    else if (row.Reach > 0f)
                    {
                        var x = go.AddComponent<MetaXRAudioSourceExperimentalFeatures>();
                        x.ReverbReach = row.Reach;
                        if (row.ErDb != 0f) x.EarlyReflectionsSendDb = row.ErDb;
                    }
                }
                var s = go.AddComponent<SynthSource>();
                s.id = id;
                s.spatial = row.Spatial;
                Configure(s, id);
                go.SetActive(true);
                src.outputAudioMixerGroup = output; // the group with "Meta XR Audio Reflection" keeps the room reverb alive
                sources[i] = s;
            }
            Connect(sources);
            return sources;
        }

        /// <summary>The routing between sources: drum pieces feed the drum room (the drum:room return, placed at the
        /// kit centre by the Drums module); the bass and treble piano sources feed the ONE soundboard, heard from piano-mid
        /// (every note's resonance send goes to the one board, whose return plays on 'piano-mid'; spec 06 section 3).</summary>
        public static void Connect(SynthSource[] sources)
        {
            var table = AudioSourceTable.Current;
            var room = sources[(int)SourceId.DrumRoom];
            for (int i = 0; i < sources.Length; i++)
            {
                float send = table[i].DrumRoomSend;
                if (send == 0f || sources[i] == null) continue;
                sources[i].sendTarget = room;
                sources[i].sendGain = send;
            }
            var board = sources[(int)SourceId.PianoMid];
            sources[(int)SourceId.PianoLo].resonatorTarget = board;
            sources[(int)SourceId.PianoHi].resonatorTarget = board;
        }

        private static uint s_seed = 1;

        /// <summary>Pools (AudioSourceTable) and the classic bodies and buses per source.</summary>
        public static void Configure(SynthSource s, SourceId id)
        {
            AudioSourceRow row = AudioSourceTable.Current[id];
            s.noiseVoices = row.NoiseVoices; s.toneVoices = row.ToneVoices; s.metalVoices = row.MetalVoices; s.bufferVoices = row.BufferVoices;
            switch (id)
            {
                case SourceId.PianoMid:   // its body is the one soundboard (Connect)
                    s.resonatorModes = PianoBoardModes; s.resonatorSeconds = 0.28f; s.resonatorGain = 0.22f; s.resonatorSeed = s_seed ^ 0x9E3779B9u;
                    break;
                case SourceId.Harp:
                    s.resonatorModes = HarpBoxModesOld; s.resonatorSeconds = 0.3f; s.resonatorGain = 0.24f; s.resonatorSeed = s_seed ^ 0x85EBCA6Bu;
                    break;
                case SourceId.DrumRoom: s.busGain = 0.3f; break;  // drum-room return gain 0.3 (spec 06 section 4)
                case SourceId.Fire:
                case SourceId.Wind:
                case SourceId.Snow: s.busGain = 0f; break;        // beds are processors; off until switched on
            }
        }
    }
}
