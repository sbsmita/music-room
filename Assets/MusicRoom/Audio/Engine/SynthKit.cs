// Shared main-thread primitives for the synth classes: source lookup, seedable randomness, engine-unique voice ids,
// the three generic voice builders (noiseVoice / toneVoice / metalVoice, spec 06 section 2) and batching. Every
// command of one API call is STAGED and committed together by Flush(), so all voices of one hit start in the same
// audio block (and negative When offsets inside the batch are exact).
using MusicRoom.Audio.Dsp;
using UnityEngine;

namespace MusicRoom.Audio
{
    public sealed class SynthKit
    {
        public readonly SynthSource[] Sources;
        public AudioTestTap Tap;
        private XorShift32 _rng;
        private int _nextId = 1;
        private readonly SynthSource[] _dirty = new SynthSource[8];
        private int _dirtyCount;

        public SynthKit(SynthSource[] sources, uint seed)
        {
            Sources = sources;
            _rng = new XorShift32(seed);
        }

        public void Seed(uint seed) => _rng = new XorShift32(seed);

        public SynthSource S(SourceId id) => Sources[(int)id];
        /// <summary>Tests: when not NaN, Rand() returns this. 0.5 zeroes every playBuffer jitter term, as the 06 reference
        /// renders did (Math.random = () => 0.5 during play calls).</summary>
        public float RandOverride = float.NaN;
        /// <summary>Uniform [0, 1) (the per-play random draws).</summary>
        public float Rand() => float.IsNaN(RandOverride) ? _rng.Next01() : RandOverride;
        public int NextId() { int id = _nextId++; if (_nextId == int.MaxValue) _nextId = 1; return id; }

        public void Stage(SynthSource dest, in SynthCommand c)
        {
            if (dest == null) return;
            // AudioSchedule (ENGINE-API.md section 10): inside a "schedule at" scope an instrument source's ASAP / relative
            // command plays at the scope's time. Sunny, the UI and the ambience are never stamped (AudioSourceTable.Instrument).
            if (AudioSchedule.Active && AudioSourceTable.Current[dest.id].Instrument)
            {
                SynthCommand s = c;
                s.When = AudioSchedule.Stamp(s.When);
                dest.Stage(s);
                Tap?.Commands.Add(new AudioTestTap.Entry { Source = dest.id, Command = s });
            }
            else
            {
                dest.Stage(c);
                Tap?.Commands.Add(new AudioTestTap.Entry { Source = dest.id, Command = c });
            }
            for (int i = 0; i < _dirtyCount; i++) if (_dirty[i] == dest) return;
            if (_dirtyCount < _dirty.Length) _dirty[_dirtyCount++] = dest;
            else dest.Commit();
        }

        /// <summary>Publish everything staged since the last Flush.</summary>
        public void Flush()
        {
            for (int i = 0; i < _dirtyCount; i++) { _dirty[i].Commit(); _dirty[i] = null; }
            _dirtyCount = 0;
        }

        // noiseVoice(t, {hp, lp, bp, q, peak, decay}) -> dest; level = the hit's "dest" gain (drum 1.7), group = choke group
        public void Noise(SynthSource dest, double when, float hp = 0, float lp = 20000, float bp = 0, float q = 1,
            float peak = 0.1f, float decay = 0.1f, float level = 1, short group = 0, byte flags = 0)
        {
            Stage(dest, new SynthCommand { Op = SynthOp.StartNoise, When = when, Hp = hp, Lp = lp, Bp = bp, Q = q, Peak = peak, Decay = decay, Level = level, Group = group, Flags = flags });
        }

        // toneVoice(t, {type, f0, f1, slide, peak, decay, lp}) -> dest
        public void Tone(SynthSource dest, double when, Wave wave, float f0, float f1, float slide, float peak, float decay,
            float lp = 20000, float level = 1, short group = 0)
        {
            Stage(dest, new SynthCommand { Op = SynthOp.StartTone, When = when, Wave = wave, F0 = f0, F1 = f1, Slide = slide, Peak = peak, Decay = decay, Lp = lp, Level = level, Group = group });
        }

        // metalVoice(t, {ratio, bp, hp, peak, decay}) -> dest
        public void Metal(SynthSource dest, double when, float ratio, float bp, float hp, float peak, float decay, float level = 1, short group = 0)
        {
            Stage(dest, new SynthCommand { Op = SynthOp.StartMetal, When = when, F0 = ratio, Bp = bp, Hp = hp, Peak = peak, Decay = decay, Level = level, Group = group });
        }

        public void Release(SynthSource dest, int voiceId, float tau, float stopAfter, double when = 0)
        {
            Stage(dest, new SynthCommand { Op = SynthOp.Release, VoiceId = voiceId, When = when, Tau = tau, StopAfter = stopAfter });
        }

        public void Bus(SourceId id, bool on, float tau = 0.4f)
        {
            Stage(S(id), new SynthCommand { Op = SynthOp.SetBusGain, Level = on ? 1 : 0, Tau = tau });
        }

        /// <summary>`when` shifted by `seconds` with the engine's sign conventions (0 = ASAP, &gt;0 absolute, &lt;0 relative).</summary>
        public static double Offset(double when, double seconds) => when > 0 ? when + seconds : when - seconds;
    }
}
