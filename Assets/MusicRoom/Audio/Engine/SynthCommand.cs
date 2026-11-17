// One main-thread -> audio-thread message. A plain struct (no references), so the ring copies it by value and
// nothing is ever allocated. Field meaning depends on Op.
namespace MusicRoom.Audio
{
    public enum SynthOp : byte
    {
        None = 0,
        StartNoise,   // noiseVoice (spec 06 section 2)
        StartTone,    // toneVoice  (spec 06 section 2)
        StartMetal,   // metalVoice (spec 06 section 2)
        StartBuffer,  // playBuffer (spec 06 section 3.2)
        Release,      // VoiceId: gain -> 0 with tau Tau, hard stop StopAfter seconds later (voice release)
        ChokeGroup,   // Group: every voice of the group ramps to 0 (drum choke)
        SetBusGain,   // Level = target, Tau = time constant (ambience ramp, spec 06 section 7)
        Custom,       // per-source extension, Kind = sub-op (SynthCustom)
        Panic,        // kill every voice at once (audio reset, scene change)
    }

    public enum Wave : byte { Sine = 0, Square = 1, Triangle = 2 }

    /// <summary>SynthCommand.Flags bits.</summary>
    public static class SynthFlags
    {
        /// <summary>Start ops: the voice bypasses the source's send to another source (the drum room). The drum synth routes
        /// the snare wires and the choke skin straight to the spatial source, not through the hit's "dest" gain, so they
        /// never reach the drum-room convolver (spec 06 section 4).</summary>
        public const byte NoSend = 1;
        /// <summary>StartTone: reproduce the reference's first render quantum. The oscillator reads its frequency automation
        /// from the quantum's frame 0, not from the start frame, so a tone starting `Buffer` frames into a 128-frame
        /// quantum plays the default 440 Hz, then a lagged glide, until that quantum ends (measured: Sunny's chirps null
        /// at -117 dB against Tests/EditMode/Audio/Reference~ with it, +3 dB without). Only the chirps use it: the guide
        /// schedules them at fixed offsets from a quantum-aligned current time.</summary>
        public const byte QuantumLead = 2;
        /// <summary>StartTone: the event time falls a hair after the voice's frame (0.02 + 0.065 in double is frame
        /// 4080.0000000000005), so the oscillator starts one frame later (ceil) while the envelope, glide and stop
        /// still count from the event. Used with QuantumLead by the chirps.</summary>
        public const byte OscLateStart = 4;
    }

    /// <summary>SynthCommand.Kind values for SynthOp.Custom (handled by the source's processors).</summary>
    public static class SynthCustom
    {
        public const byte FirePops = 1;   // Level > 0: pops on (fire on)
        public const byte SnowGust = 2;   // F0 f0, Level strength, Peak, Slide attack, StopAfter hold, Tau release
        public const byte Seed = 3;       // VoiceId = seed: re-seed the processor's PRNG on the audio thread (SeedAll)
    }

    /// <summary>When: 0 = ASAP (first sample of the next block); &gt; 0 = absolute AudioSettings.dspTime (a past time starts
    /// now, the max(t, currentTime) rule); &lt; 0 = -When seconds after the block that consumes it (exact inside one
    /// committed batch only).</summary>
    public struct SynthCommand
    {
        public SynthOp Op;
        public Wave Wave;        // StartTone
        public byte Kind;        // Custom sub-op (SynthCustom)
        public byte Flags;       // SynthFlags bits
        public short Group;      // choke group (0 = none; 1 = chokable cymbals / hats)
        public int VoiceId;      // engine-unique id (0 = anonymous)
        public double When;

        public float Peak;       // envelope peak (linear)
        public float Decay;      // seconds to 0.0005 (exponential)
        public float F0, F1;     // tone: start/end Hz; metal: F0 = ratio; buffer: F0 = comb lag (s); gust: F0 = f0
        public float Slide;      // tone glide time (s); gust: attack
        public float Hp, Lp, Bp; // filter corners in Hz (0 = off; Lp >= 20000 = off)
        public float Q;          // bandpass Q (linear) or buffer lowpass Q (dB, spec 06 section 1.2)
        public float Level;      // multiplier ("dest" gain, e.g. drum level 1.7) or bus target; gust: strength
        public float Tau;        // release / choke / bus time constant (s); gust: release
        public float StopAfter;  // release: hard stop this long after the release starts (s); gust: hold
        public float Rate;       // buffer playback rate
        public float Res;        // buffer: send into the source's resonator bank
        public float Comb;       // buffer: pluck-comb depth (0 = off)
        public int Buffer;       // buffer: SampleBank slot; tone with SynthFlags.QuantumLead: start offset in its quantum
    }
}
