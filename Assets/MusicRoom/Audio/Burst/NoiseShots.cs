// A short filtered-noise one-shot for a module without a sound unit and without unsafe code of its own (the Room's thrown
// paint's soft "splat"): up to four noise lanes, each through a biquad (Dsp/Biquad.cs's formulas) with its own decay and
// mix, registered once as an IAudioEngine one-shot preset (the graph's pool, as the sheet music's paper sounds) and
// played with IAudioEngine.PlayOneShot. Main thread, setup only (Register allocates nothing per call but the preset).
namespace MusicRoom.Audio.Burst
{
    public static unsafe class NoiseShots
    {
        /// <summary>One noise lane: its filter, centre / corner frequency (Hz), Q, decay time constant (s) and output weight.</summary>
        public struct Lane
        {
            public Dsp.BiquadType Type;
            public float Hz, Q, Tau, Mix;
            public Lane(Dsp.BiquadType type, float hz, float q, float tau, float mix) { Type = type; Hz = hz; Q = q; Tau = tau; Mix = mix; }
        }

        /// <summary>Registers the one-shot (lanes: 1 to 4) and returns its preset id for PlayOneShot, or -1 (no graph: a test
        /// fake, or the engine is not up).</summary>
        public static int Register(IAudioEngine audio, Lane[] lanes, float seconds, float levelDb, uint seed)
        {
            if (audio == null || lanes == null || lanes.Length == 0) return -1;
            int sr = audio.SampleRate > 0 ? audio.SampleRate : 48000;
            var p = new OneShotPreset { Length = (int)System.Math.Floor(seconds * sr + 0.5), LevelDb = levelDb, Seed = seed };
            for (int l = 0; l < 4 && l < lanes.Length; l++)
            {
                Lane ln = lanes[l];
                DspSetup.SetBiquad(&p.Filter, l, ln.Type, ln.Hz, ln.Q, 0.0, sr);
                p.EnvGain[l] = 1f;
                p.EnvMul[l] = DspSetup.DecayMultiplier(ln.Tau, sr);
                p.Mix[l] = ln.Mix;
            }
            return audio.AddOneShotPreset(p);
        }
    }
}
