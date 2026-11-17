// The instrument-facing audio API (the play calls + the spatial sources, spec 04 section 4.3, spec 06).
// Every call is main-thread, allocation-free, and turns into SynthCommands for the audio thread. `when` is absolute
// AudioSettings.dspTime; 0 = as soon as possible (the next audio block). Live strikes use 0; only the harp schedules.
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using UnityEngine;

namespace MusicRoom.Audio
{
    /// <summary>The synth sources: the 18 classic ones (17 spatial sources + the drum room at the kit centre; values 0-17
    /// are persisted and tested and never move) and the many-instrument platform's 7 unit-only sources (12-new-instruments/
    /// platform.md 10.1; MarimbaMid is the marimba spec's request R1): 25. Each has one AudioSourceTable row (AudioRig.cs).
    /// Names are the exact GameObject names "Synth <SourceId>" under the audio host.</summary>
    public enum SourceId
    {
        PianoLo, PianoMid, PianoHi, Harp,
        Kick, Snare, Hat, Tom1, Tom2, Floor, Crash, Ride,
        DrumRoom,
        Fire, Wind, Snow, Guide, Ui,
        Handpan, HandDrumLo, HandDrumHi, MarimbaLo, MarimbaMid, MarimbaHi, Bowls,
        Count
    }

    public enum DrumKind : byte { Kick, Snare, Hat, HatOpen, HatClose, Tom1, Tom2, Floor, Crash, Ride }

    /// <summary>Where a stroke lands. CrossStick (10-upgrade drums.md section 2.3, appended): the stick's shoulder on the
    /// snare's rim with the tip on the head; the classic kit plays it as a head stroke (the Drums module never sends it now).</summary>
    public enum DrumZone : byte { Head, Rim, Bell, Bow, Edge, Top, CrossStick = 6 }

    /// <summary>What strikes (10-upgrade drums.md section 2.3): the stick tip (default), the stick's shoulder, a fingertip.
    /// Only the modelled kit uses it.</summary>
    public enum DrumImplement : byte { Tip = 0, Shoulder = 1, Finger = 2 }

    /// <summary>A drum strike { pos: 0 centre .. 1 edge, zone, open } (spec 06 section 4).</summary>
    public struct DrumStrike
    {
        public float Pos;
        public DrumZone Zone;
        public bool Open;
        /// <summary>The modelled kit (drums.md 2.3): what strikes; Tip by default.</summary>
        public DrumImplement Implement;
        /// <summary>The modelled kit (drums.md 2.3): hi-hat openness of a HatOpen stroke, 0 = use the hat's state, 0 &lt; x &lt; 1 = half-open.</summary>
        public float Openness;
        public static DrumStrike Default => new DrumStrike { Pos = 0.3f, Zone = DrumZone.Head };
        public static DrumZone ParseZone(string zone)
        {
            switch (zone)
            {
                case "rim": return DrumZone.Rim;
                case "bell": return DrumZone.Bell;
                case "bow": return DrumZone.Bow;
                case "edge": return DrumZone.Edge;
                case "top": return DrumZone.Top;
                case "xstick": return DrumZone.CrossStick;
                default: return DrumZone.Head;
            }
        }
        public static string ZoneName(DrumZone z) => z == DrumZone.Rim ? "rim" : z == DrumZone.Bell ? "bell" : z == DrumZone.Bow ? "bow" : z == DrumZone.Edge ? "edge" : z == DrumZone.Top ? "top" : z == DrumZone.CrossStick ? "xstick" : "head";
        public static DrumKind ParseKind(string kind)
        {
            switch (kind)
            {
                case "kick": return DrumKind.Kick;
                case "snare": return DrumKind.Snare;
                case "hat": return DrumKind.Hat;
                case "hatOpen": return DrumKind.HatOpen;
                case "hatClose": return DrumKind.HatClose;
                case "tom1": return DrumKind.Tom1;
                case "tom2": return DrumKind.Tom2;
                case "floor": return DrumKind.Floor;
                case "crash": return DrumKind.Crash;
                default: return DrumKind.Ride;
            }
        }
    }

    public enum PluckMode : byte { Through, Across }

    /// <summary>Harp pluck options: position u along the string, stroke speed, mode, delay.</summary>
    public struct PluckOptions
    {
        public float U;        // 0..1 from the soundboard end (default .35)
        public float Speed;    // m/s
        public PluckMode Mode;
        public double Delay;   // seconds after `when` (glissando spread)
        /// <summary>The stroke's direction (10-upgrade/harp.md 5.4): across the string in its plane (Ud) and through the plane
        /// (Uz). Both 0 = unknown: the mode decides (Across = (1, 0), Through = (0, 1)). Today's harp ignores them.</summary>
        public float Ud, Uz;
        public static PluckOptions Default => new PluckOptions { U = 0.35f, Speed = 0f, Mode = PluckMode.Through, Delay = 0 };
    }

    /// <summary>Poke: the soft "bop" when a fingertip pokes Sunny (first five: poke to skip, sunny-tour.md 4.4).</summary>
    public enum ChirpKind : byte { Open, Close, Poke }

    /// <summary>What the instruments keep to release a note later (a voice handle).</summary>
    public readonly struct VoiceHandle
    {
        public readonly SynthSource Source;
        public readonly int Id;
        public VoiceHandle(SynthSource source, int id) { Source = source; Id = id; }
        public bool IsValid => Source != null && Id != 0;
        public static VoiceHandle None => default;
    }

    public interface IAudioEngine
    {
        /// <summary>Sources exist and the engine accepts commands (buffers may still be rendering: see RenderProgress).</summary>
        bool IsReady { get; }
        /// <summary>Seconds of one DSP block (the mixer's buffer / SampleRate): a sweep's block-aligned base is FrameNow + this.
        /// The default assumes the 256-frame buffer (section 0).</summary>
        double BlockSeconds => SampleRate > 0 ? 256.0 / SampleRate : 0.0;
        int SampleRate { get; }
        /// <summary>AudioSettings.dspTime (the audio clock): on the main thread the start of the NEXT block, stepping 5.33 ms.</summary>
        double Now { get; }
        /// <summary>Now sampled once at frame start: use FrameNow + offset (+ one block) for strums / sequences across calls.</summary>
        double FrameNow { get; }
        /// <summary>Offline renders done (0..1): piano keys, harp strings, voice lines.</summary>
        float RenderProgress { get; }

        // ---- piano (spec 06 section 3.2) ----
        VoiceHandle Piano(int midi, float vel, double when = 0);
        /// <summary>Release: gain -> 0 (tau 60 ms), stop +0.5 s; thud = the damper noise (spec 06 section 3.2).</summary>
        void Release(VoiceHandle h, bool thud = true, double when = 0);

        // ---- harp (old sound, spec 06 section 5.1) ----
        VoiceHandle Pluck(int stringIndex, float vel, double when = 0, PluckOptions? options = null);
        /// <summary>StringVoice.damp: tau 18 ms, stop +0.25 s, plus the hand's "shh" (n = strings under the hand).</summary>
        void Damp(VoiceHandle h, int hands = 1, double when = 0);
        /// <summary>clamp(4.5 - 0.08 (midi - 48), 0.7, 4.5).</summary>
        double HarpRingSeconds(int midi);
        /// <summary>The amplitude decay time constant (s) of partial n (1 = the fundamental) of harp string i (0..21; a harp
        /// string's midi number is accepted too), for the string blur (10-upgrade/experience.md E8): today's Karplus-Strong
        /// recipe (HarpDecay.ClassicPartialTau, HarpPartialDecay.OldTau), the app's only harp sound. 0 when out of range.
        /// The v2 design decay without an engine is HarpPartialDecay.V2Tau (the harp module's test doubles).</summary>
        double HarpPartialTau(int stringIndex, int partial) => HarpDecay.PartialTau(stringIndex, partial, SampleRate);

        // ---- drums (spec 06 section 4) ----
        void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0);
        /// <summary>Choke(kind): ringing hits fade (tau 12 ms) plus the hand-on-metal skin noise.</summary>
        void Choke(DrumKind kind);

        // ---- ambience (spec 06 section 7) ----
        void SetFire(bool on);
        void SetWind(bool on);
        void SetSnow(bool on);
        bool FireOn { get; }
        bool WindOn { get; }
        bool SnowOn { get; }
        /// <summary>Snow gust: plays only while the snow sound is on (strength .45-1, attack/hold/release seconds).</summary>
        void SnowGust(float strength, float attack, float hold, float release);

        // ---- spatial ----
        SynthSource Source(SourceId id);
        /// <summary>Parent a source to a rig part at a local offset (table in spec 04 section 4.2).</summary>
        void SetSourceParent(SourceId id, Transform parent, Vector3 localPosition);
        void SetSourcePosition(SourceId id, Vector3 world);
        /// <summary>Mixed reality: reverb sends -6 dB (a drier mix for the real room); false restores the VR sends.</summary>
        void SetMixedReality(bool mixedReality);
        /// <summary>Shoebox room for the Meta reverb: centre and size in Unity world units (VR room or MRUK bounds).</summary>
        void SetRoomBox(Vector3 center, Vector3 size, bool realRoom);
        /// <summary>The same box with the real room's yaw (degrees about +y; size in the room's own axes). Meta's shoebox is
        /// axis-aligned, so the yaw is dropped (the default).</summary>
        void SetRoomBox(Vector3 center, Vector3 size, float yawDeg, bool realRoom) => SetRoomBox(center, size, realRoom);

        // ---- guide (spec 06 section 8, spec 08 section 3.4) ----
        void Chirp(ChirpKind kind, double when = 0);
        bool HasVoiceLine(string id);
        /// <summary>Dry seconds of a line (manifest), -1 when unknown.</summary>
        float VoiceLineSeconds(string id);
        /// <summary>The line's robotised buffer is in the sample bank (its file is loaded, decoded and rendered). The default
        /// (an engine without a render queue) trusts any line it has.</summary>
        bool VoiceLineReady(string id) => HasVoiceLine(id);
        /// <summary>Start rendering a line that is about to be said (a tip becoming pending; sunny-tour 7.1 PrepareLine), so
        /// it is resident when Sunny asks. The engine renders lines lazily (at most 24 resident) and VoiceLineReady starts a
        /// render too; this only gets it earlier. The default (an engine without a render queue) does nothing.</summary>
        void PrepareVoiceLine(string id) { }
        /// <summary>Plays the robotised line on the Guide source at `when` (0 = ASAP). Returns the dspTime it ends.</summary>
        double PlayVoiceLine(string id, double when = 0);
        /// <summary>Cut the current line: fade 4 ms, stop +30 ms.</summary>
        void StopVoiceLine();
        bool VoiceLinePlaying { get; }
        /// <summary>Mouth level: RMS of the processed line over 1024 samples at the current playback position (0 when none).</summary>
        float VoiceLevel { get; }

        // ---- ui (spec 06 section 9) ----
        void Swish(double when = 0);

        // ---- the sound (Docs/10-upgrade, ENGINE-API.md) ----
        // The piano, drums, harp and room play the classic sound only (today's synths): only the unit-only roles have a unit.
        /// <summary>The role's sound unit (null: the role has none; the piano, drums and harp never do). An instrument module
        /// casts it to the unit's own extra interface instead of growing this one.</summary>
        SoundUnit ActiveUnit(UnitId role) => null;

        // ---- scheduling and the looper's click (ENGINE-API.md section 10) ----
        /// <summary>Ambient "schedule at" scope: every instrument command issued inside it that would play as soon as possible
        /// (or relative to its batch) plays at `when` (absolute audio seconds) instead, sample-accurately, on today's synths and
        /// on the sound units alike. Wrap an ordinary instrument call in it, e.g. the looper's playback:
        /// <c>using (audio.ScheduleAt(t)) piano.PlaybackOn(60, 0.8f);</c>. Main thread; allocation-free; nests.</summary>
        AudioSchedule.Scope ScheduleAt(double when) => AudioSchedule.Begin(when);
        /// <summary>The looper's metronome: one soft synthesized click on the 2D Ui source at `when` (absolute audio seconds,
        /// 0 = ASAP); accent = a bar's first beat (a little higher). The default (a test fake) is silent.</summary>
        void Click(bool accent, double when = 0) { }

        // ---- a module's own one-shot sounds (platform phase 0, for modules without a sound unit: the sheet music's paper
        //      turns). The graph's pooled one-shots (AudioGraph.OneShotPreset): register a recipe once, play it on any source.
        //      The defaults (a test fake, no graph) do nothing.
        /// <summary>Register a one-shot recipe; returns its id for PlayOneShot, or -1 (no graph, or the preset table is full).</summary>
        int AddOneShotPreset(in OneShotPreset preset) => -1;
        /// <summary>Start a one-shot on <paramref name="source"/> at `when` (0 = ASAP); voiceId names it for ReleaseOneShot.</summary>
        bool PlayOneShot(int preset, float gain, SourceId source, int voiceId = 0, double when = 0) => false;
        bool ReleaseOneShot(int voiceId, float tau, double when = 0) => false;

        // ---- test hooks ----
        /// <summary>When set, every API call (Calls) and every staged command (Commands) is also appended here (main
        /// thread); tests inspect it. Assert instrument output on Calls (the API level: the piano / drum / pluck
        /// calls); Commands are the Audio module's own recipe detail.</summary>
        AudioTestTap TestTap { get; set; }
        /// <summary>Re-seed every seedable unit (jitter, pops, phases) for deterministic tests.</summary>
        void SeedAll(uint seed);
    }

    /// <summary>Which IAudioEngine method a recorded call was (AudioTestTap.Calls).</summary>
    /// <summary>Unit (appended, platform.md 10.1): a call into a unit-only role's own sound interface (Call.Unit, Call.Op,
    /// Index, Vel, When), recorded by the unit itself through SoundUnit.RecordUnit.</summary>
    public enum AudioCall : byte { Piano, Release, Pluck, Damp, Drum, Choke, Fire, Wind, Snow, SnowGust, Chirp, VoiceLine, StopVoiceLine, Swish, Click, Unit }

    /// <summary>Main-thread capture of engine calls and staged commands (tests). Not for production use.</summary>
    public sealed class AudioTestTap
    {
        public struct Entry { public SourceId Source; public SynthCommand Command; }

        /// <summary>One IAudioEngine call as the instrument made it (before the engine clamps or adds jitter).</summary>
        public struct Call
        {
            public AudioCall Method;
            /// <summary>Piano: midi; Pluck / Damp: string index (Damp: of the handle's voice, -1 unknown); Drum / Choke: (int)DrumKind;
            /// Chirp: (int)ChirpKind; Damp also stores the hand count in Hands.</summary>
            public int Index;
            public float Vel;
            public DrumKind Drum;
            public DrumStrike Strike;
            public PluckOptions Pluck;
            public double When;
            /// <summary>Release: thud; Fire / Wind / Snow: on.</summary>
            public bool Flag;
            public int Hands;
            /// <summary>VoiceLine: the line id.</summary>
            public string Line;
            /// <summary>Piano / Pluck: the returned handle; Release / Damp: the handle passed in.</summary>
            public VoiceHandle Handle;
            /// <summary>AudioCall.Unit: the role and the unit's own op code (its public method).</summary>
            public UnitId Unit;
            public byte Op;
        }

        public readonly List<Entry> Commands = new List<Entry>(256);
        public readonly List<Call> Calls = new List<Call>(128);
        public void Clear() { Commands.Clear(); Calls.Clear(); }
        public int Count(SourceId source, SynthOp op) { int n = 0; foreach (var e in Commands) if (e.Source == source && e.Command.Op == op) n++; return n; }
        public int Count(AudioCall method) { int n = 0; foreach (var c in Calls) if (c.Method == method) n++; return n; }
        /// <summary>Unit calls of one role (any op when op &lt; 0).</summary>
        public int CountUnit(UnitId unit, int op = -1) { int n = 0; foreach (var c in Calls) if (c.Method == AudioCall.Unit && c.Unit == unit && (op < 0 || c.Op == op)) n++; return n; }
    }
}
