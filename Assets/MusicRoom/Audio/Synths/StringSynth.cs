// Harp, OLD sound (pluck and damp; spec 06 section 5.1). The buffer is the Karplus-Strong
// render in the bank; the pluck adds the fingertip thud, the resonator (soundbox) send and the hand damp "shh".
// Nulled against Tests/EditMode/Audio/Reference~/harp_old_*.wav (-137 dB) and harp_damp_shh.wav (-147 dB). The pluck
// options (position, speed, mode) shape only the NEW sound (spec 06 section 5.2), which nothing in the app selects: not
// implemented here.
using UnityEngine;

namespace MusicRoom.Audio.Synths
{
    public sealed class StringSynth
    {
        readonly SynthKit _kit;

        public StringSynth(SynthKit kit) { _kit = kit; }

        public VoiceHandle Pluck(int stringIndex, float vel, double when, PluckOptions o)
        {
            vel = Mathf.Clamp(vel, 0.05f, 1f);
            stringIndex = Mathf.Clamp(stringIndex, 0, 21);
            double at = o.Delay > 0 ? SynthKit.Offset(when, o.Delay) : when;
            SynthSource src = _kit.S(SourceId.Harp);
            // Fingertip thud: lp 700, peak .02 + .05 vel, decay .012.
            _kit.Noise(src, at, lp: 700, peak: 0.02f + 0.05f * vel, decay: 0.012f);
            int id = _kit.NextId();
            // playBuffer with jitter 1: +-0.16 % rate, +-5 % cutoff, +-6 % level (draw order rate, cutoff, level).
            float rate = 1 + (_kit.Rand() - 0.5f) * 0.0032f;
            float cut = (1000 + vel * 5000) * (1 + (_kit.Rand() - 0.5f) * 0.1f);
            float lvl = (0.1f + 0.6f * vel) * (1 + (_kit.Rand() - 0.5f) * 0.12f);
            _kit.Stage(src, new SynthCommand
            {
                Op = SynthOp.StartBuffer, VoiceId = id, When = at, Buffer = AudioEngine.HarpSlot0 + stringIndex,
                Rate = rate, Lp = cut, Q = 0.4f, Level = lvl,
                Res = 0.5f + 0.4f * vel, // soundbox send -> x 0.24 return (resonatorGain)
            });
            _kit.Flush();
            return new VoiceHandle(src, id);
        }

        /// <summary>StringVoice.damp: setTarget(0, tt, .018), stop +.25 s, plus "shh" bp 2400 q .7 peak .004 sqrt(max(1, n)) decay .07.</summary>
        public void Damp(VoiceHandle h, int hands, double when)
        {
            if (!h.IsValid) return;
            _kit.Release(h.Source, h.Id, 0.018f, 0.25f, when);
            _kit.Noise(h.Source, when, bp: 2400, q: 0.7f, peak: 0.004f * Mathf.Sqrt(Mathf.Max(1, hands)), decay: 0.07f);
            _kit.Flush();
        }

        /// <summary>Re-pluck: voice release (tau 60 ms, stop +0.5 s), no thud.</summary>
        public void Release(VoiceHandle h, double when)
        {
            if (!h.IsValid) return;
            _kit.Release(h.Source, h.Id, 0.06f, 0.5f, when);
            _kit.Flush();
        }
    }
}
