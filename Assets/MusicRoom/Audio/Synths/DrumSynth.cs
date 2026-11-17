// Live drum voices (spec 06 section 4). Every layer sums into the hit's "dest" gain (drumLevel
// 1.7) on the piece's source; the piece's source feeds the DrumRoom source (room send per kind) through
// SynthSource.sendTarget; metal layers get bp x (0.85 + 0.3 vel); a layer's own reverb send is the Meta reverb send of
// the source. Every kind/zone of Tests/EditMode/Audio/Reference~/drum_*.wav nulls below -60 dB with noise seed 1 (NullTests), including the
// snare wires (+4 ms to the snare source, no 1.7, SynthFlags.NoSend: no room send).
using UnityEngine;

namespace MusicRoom.Audio.Synths
{
    public sealed class DrumSynth
    {
        public const float DrumLevel = 1.7f;     // spec 06 section 4
        public const short ChokeGroup = 1;       // cymbals and hats
        readonly SynthKit _kit;

        public DrumSynth(SynthKit kit) { _kit = kit; }

        public static SourceId SourceFor(DrumKind kind)
        {
            switch (kind)
            {
                case DrumKind.Kick: return SourceId.Kick;
                case DrumKind.Snare: return SourceId.Snare;
                case DrumKind.Tom1: return SourceId.Tom1;
                case DrumKind.Tom2: return SourceId.Tom2;
                case DrumKind.Floor: return SourceId.Floor;
                case DrumKind.Crash: return SourceId.Crash;
                case DrumKind.Ride: return SourceId.Ride;
                default: return SourceId.Hat; // hat, hatOpen, hatClose share the hat source
            }
        }

        public void Hit(DrumKind kind, float vel, DrumStrike strike, double when)
        {
            vel = Mathf.Clamp(vel, 0.05f, 1f);
            float v2 = vel * vel;
            float edge = Mathf.Clamp01(strike.Pos), centre = 1 - edge;
            DrumZone zone = strike.Zone;
            SynthSource s = _kit.S(SourceFor(kind));
            short g = kind == DrumKind.Crash || kind == DrumKind.Ride || kind == DrumKind.Hat || kind == DrumKind.HatOpen || kind == DrumKind.HatClose ? ChokeGroup : (short)0;
            float L = DrumLevel;
            float mb = 0.85f + 0.3f * vel; // metal bandpass scale
            switch (kind)
            {
                case DrumKind.Kick:
                    _kit.Tone(s, when, Wave.Sine, 165, 46, 0.09f, 1.0f * vel, 0.42f + 0.1f * vel, 2500, L, g);
                    _kit.Noise(s, when, bp: 3200, q: 0.7f, peak: 0.34f * v2, decay: 0.02f, level: L, group: g);
                    break;
                case DrumKind.Snare:
                    if (zone == DrumZone.Rim)
                    {
                        _kit.Noise(s, when, bp: 3400, q: 1.4f, peak: 0.9f * vel, decay: 0.05f, level: L, group: g);
                        _kit.Tone(s, when, Wave.Sine, 1180, 950, 0.012f, 0.55f * vel, 0.11f, 20000, L, g);
                        _kit.Tone(s, when, Wave.Triangle, 195, 150, 0.05f, 0.3f * vel, 0.16f, 20000, L, g);
                        _kit.Noise(s, when, hp: 1400, lp: 9000, peak: 0.4f * Mathf.Pow(vel, 1.3f), decay: 0.18f, level: L, group: g);
                        break;
                    }
                    _kit.Tone(s, when, Wave.Triangle, 195, 150, 0.05f, 0.5f * vel * (0.7f + 0.3f * centre), 0.18f + 0.06f * edge, 20000, L, g);
                    _kit.Tone(s, when, Wave.Sine, 330, 280, 0.04f, 0.3f * vel * (0.5f + 0.9f * edge), 0.1f + 0.1f * edge, 20000, L, g);
                    _kit.Noise(s, when, hp: 1400, lp: 9000, peak: 0.66f * Mathf.Pow(vel, 1.3f) * (1 - 0.35f * edge), decay: 0.2f + 0.08f * vel + 0.06f * edge, level: L, group: g);
                    break;
                case DrumKind.Hat:
                    _kit.Metal(s, when, 1, 8500 * mb, 5000, 1.3f * vel, 0.08f + 0.03f * vel + 0.05f * edge, L, g);
                    _kit.Noise(s, when, hp: 7000, peak: 0.75f * vel * (1 + 0.3f * edge), decay: 0.07f + 0.05f * edge, level: L, group: g);
                    break;
                case DrumKind.HatOpen:
                    _kit.Metal(s, when, 1, 8500 * mb, 5000, 1.05f * vel, 0.42f + 0.15f * edge, L, g);
                    _kit.Noise(s, when, hp: 7000, peak: 0.6f * vel, decay: 0.35f + 0.15f * edge, level: L, group: g);
                    break;
                case DrumKind.HatClose:
                    _kit.Metal(s, when, 1, 7000 * mb, 4500, 0.6f * vel, 0.05f, L, g);
                    _kit.Noise(s, when, hp: 6000, peak: 0.45f * vel, decay: 0.035f, level: L, group: g);
                    break;
                case DrumKind.Tom1:
                case DrumKind.Tom2:
                case DrumKind.Floor:
                {
                    float f0, f1, slide, decay, bp, rim, peak;
                    if (kind == DrumKind.Tom1) { f0 = 210; f1 = 140; slide = 0.12f; decay = 0.62f; bp = 2500; rim = 980; peak = 0.9f; }
                    else if (kind == DrumKind.Tom2) { f0 = 165; f1 = 105; slide = 0.14f; decay = 0.78f; bp = 2200; rim = 860; peak = 0.9f; }
                    else { f0 = 115; f1 = 72; slide = 0.16f; decay = 1.0f; bp = 1800; rim = 720; peak = 1.0f; }
                    if (zone == DrumZone.Rim)
                    {
                        _kit.Noise(s, when, bp: 3000, q: 1.4f, peak: 0.8f * vel, decay: 0.05f, level: L, group: g);
                        _kit.Tone(s, when, Wave.Sine, rim, rim * 0.8f, 0.012f, 0.5f * vel, 0.12f, 20000, L, g);
                        _kit.Tone(s, when, Wave.Sine, f0, f1, slide, 0.45f * vel, decay * 0.8f, 20000, L, g);
                        break;
                    }
                    _kit.Tone(s, when, Wave.Sine, f0, f1, slide, peak * vel * (0.75f + 0.25f * centre), decay * (1 + 0.3f * edge), 20000, L, g);
                    _kit.Noise(s, when, bp: bp, q: 0.6f, peak: 0.26f * vel * (0.6f + 0.8f * edge), decay: 0.03f, level: L, group: g);
                    break;
                }
                case DrumKind.Crash:
                    if (zone == DrumZone.Bell)
                    {
                        _kit.Tone(s, when, Wave.Sine, 2400, 2400, 0, 0.6f * vel, 0.7f, 20000, L, g);
                        _kit.Metal(s, when, 1.9f, 5200 * mb, 2800, 0.5f * vel, 0.6f, L, g);
                        _kit.Noise(s, when, bp: 5500, q: 0.5f, hp: 2800, peak: 0.3f * vel, decay: 0.5f, level: L, group: g);
                        break;
                    }
                    _kit.Noise(s, when, bp: 5500, q: 0.4f, hp: 2800, peak: 0.95f * vel * (0.75f + 0.25f * edge), decay: (1.6f + 0.6f * vel) * (0.8f + 0.3f * edge), level: L, group: g);
                    _kit.Metal(s, when, 1.7f, 6000 * mb, 3000, 0.4f * vel, 1.2f, L, g);
                    break;
                case DrumKind.Ride:
                    if (zone == DrumZone.Bell)
                    {
                        _kit.Tone(s, when, Wave.Sine, 2900, 2900, 0, 0.9f * vel, 0.8f, 20000, L, g);
                        _kit.Tone(s, when, Wave.Sine, 1850, 1850, 0, 0.4f * vel, 0.5f, 20000, L, g);
                        _kit.Metal(s, when, 1.3f, 4800 * mb, 2600, 0.7f * vel, 0.7f, L, g);
                        _kit.Noise(s, when, hp: 6000, peak: 0.12f * vel, decay: 0.3f, level: L, group: g);
                        break;
                    }
                    if (zone == DrumZone.Edge)
                    {
                        _kit.Noise(s, when, bp: 6000, q: 0.5f, hp: 3000, peak: 0.7f * vel, decay: 1.4f, level: L, group: g);
                        _kit.Metal(s, when, 1.3f, 7000 * mb, 4000, 0.6f * vel, 1.2f, L, g);
                        _kit.Tone(s, when, Wave.Sine, 2900, 2900, 0, 0.25f * vel, 0.5f, 20000, L, g);
                        break;
                    }
                    _kit.Metal(s, when, 1.3f, 7000 * mb, 4000, 0.8f * vel, 1.1f, L, g);
                    _kit.Tone(s, when, Wave.Sine, 2900, 2900, 0, 0.45f * vel, 0.6f, 20000, L, g);
                    _kit.Noise(s, when, hp: 6000, peak: 0.3f * vel, decay: 0.6f, level: L, group: g);
                    break;
            }
            // Snare wires answer the kick and toms: +4 ms, straight to drum:snare, no 1.7, no room send.
            if (kind == DrumKind.Kick || kind == DrumKind.Tom1 || kind == DrumKind.Tom2 || kind == DrumKind.Floor)
                _kit.Noise(_kit.S(SourceId.Snare), SynthKit.Offset(when, 0.004), hp: 2800, lp: 9000, peak: 0.07f * vel, decay: 0.08f, flags: SynthFlags.NoSend);
            _kit.Flush();
        }

        /// <summary>Choke: every ringing hit of the cymbal fades (tau 12 ms to 1e-4) plus the skin-on-metal noise
        /// (lp 1500, peak .05, decay .03, not x 1.7, no room send: it goes straight to the spatial source). The Meta reverb
        /// tail rings on, like the reference's main-reverb sends.</summary>
        public void Choke(DrumKind kind)
        {
            SynthSource s = _kit.S(SourceFor(kind));
            _kit.Stage(s, new SynthCommand { Op = SynthOp.ChokeGroup, Group = ChokeGroup, Tau = 0.012f, StopAfter = float.PositiveInfinity });
            _kit.Noise(s, 0, lp: 1500, peak: 0.05f, decay: 0.03f, flags: SynthFlags.NoSend);
            _kit.Flush();
        }
    }
}
