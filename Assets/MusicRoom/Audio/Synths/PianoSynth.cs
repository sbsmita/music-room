// Piano play stage (through playBuffer; spec 06 section 3.2). Nulled against Tests/EditMode/Audio/Reference~/
// piano_*.wav (knock + buffer + the one soundboard on piano-mid + release and damper thud at frame 120064,
// -75..-80 dB) and tested for the jitter draw order (rate, cutoff, level); board return 0.22 x send (0.6 + 0.4 vel).
using MusicRoom.Audio.Dsp;
using UnityEngine;

namespace MusicRoom.Audio.Synths
{
    public sealed class PianoSynth
    {
        public const float Jitter = 0.25f; // PIANO_JITTER
        readonly SynthKit _kit;

        public PianoSynth(SynthKit kit) { _kit = kit; }

        public static SourceId SourceFor(int midi) => midi < 52 ? SourceId.PianoLo : midi < 76 ? SourceId.PianoMid : SourceId.PianoHi;

        public VoiceHandle Play(int midi, float vel, double when)
        {
            vel = Mathf.Clamp(vel, 0.05f, 1f);
            midi = Mathf.Clamp(midi, 21, 108);
            SynthSource src = _kit.S(SourceFor(midi));
            float f = (float)Notes.Mtof(midi);
            // Hammer knock: bp clamp(1.6 f + 900, 600, 3200) q 1.1, peak .05 + .3 vel^2, decay .007 + .004 vel.
            _kit.Noise(src, when, bp: Mathf.Clamp(f * 1.6f + 900, 600, 3200), q: 1.1f, peak: 0.05f + 0.3f * vel * vel, decay: 0.007f + 0.004f * vel);
            int id = _kit.NextId();
            float level = 0.08f + 0.8f * vel * vel;
            float cutoff = Mathf.Clamp(f * 1.5f + 300 + Mathf.Pow(vel, 1.6f) * 11000, 500, 16000);
            // Jitter draw order: rate, cutoff, level (06 section 3.2).
            float rate = 1 + (_kit.Rand() - 0.5f) * 0.0032f;
            float cut = cutoff * (1 + (_kit.Rand() - 0.5f) * 0.1f * Jitter);
            float lvl = level * (1 + (_kit.Rand() - 0.5f) * 0.12f * Jitter);
            _kit.Stage(src, new SynthCommand
            {
                Op = SynthOp.StartBuffer, VoiceId = id, When = when, Buffer = AudioEngine.PianoSlot0 + midi - 21,
                Rate = rate, Lp = cut, Q = 0.4f, Level = lvl,
                Res = 0.6f + 0.4f * vel, // soundboard send (x (0.6 + 0.4 vel) -> board convolver -> x 0.22)
            });
            _kit.Flush();
            return new VoiceHandle(src, id);
        }

        /// <summary>Release: gain -> 0 (tau 60 ms), stop 0.5 s later; thud = the damper (bp 260, q .8, peak .028, decay .03).</summary>
        public void Release(VoiceHandle h, bool thud, double when)
        {
            if (!h.IsValid) return;
            _kit.Release(h.Source, h.Id, 0.06f, 0.5f, when);
            if (thud) _kit.Noise(h.Source, when, bp: 260, q: 0.8f, peak: 0.028f, decay: 0.03f);
            _kit.Flush();
        }
    }
}
