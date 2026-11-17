// The harp's sound side, narrowed to what HarpPlay uses (audio.pluck / voice.release / voice.damp / ringSeconds /
// audio.now / the partials' decay for the blur). In the app it is the IAudioEngine (EngineSound); EditMode tests
// substitute a recorder of calls. Keeping this module-local (rather than faking IAudioEngine in tests) means new engine
// members never break the harp tests.
using MusicRoom.Audio;
using UnityEngine;

namespace MusicRoom.Harp
{
    public interface IHarpSound
    {
        /// <summary>The audio clock (AudioSettings.dspTime in the app).</summary>
        double Now { get; }
        /// <summary>The base a sweep's plucks are scheduled from: FrameNow plus one DSP block, so a glissando's spread
        /// (base + delay) survives the audio thread draining the batch one block later. Only sweeps use it; an isolated
        /// pluck is sent with when = 0 (ASAP, ARCHITECTURE.md 15.3 and 17).</summary>
        double PluckBase { get; }
        /// <summary>How long a note rings (s to -60 dB): the voice's lifetime.</summary>
        double RingSeconds(int midi);
        /// <summary>E8: the 1/e time of partial n (1-based) of string i as the sound decays it (the visible blur follows
        /// it): IAudioEngine.HarpPartialTau in the app; the v2 harp's design decay for test doubles.</summary>
        float PartialTau(int stringIndex, int partial) => HarpPartialDecay.V2Tau(stringIndex, partial);
        VoiceHandle Pluck(int stringIndex, float vel, double when, in PluckOptions options);
        /// <summary>Re-pluck of a ringing string: Voice.release without the piano's damper thud.</summary>
        void Release(in VoiceHandle voice);
        /// <summary>A hand laid on the string: tau 18 ms plus the "shh" for n strings under the hand.</summary>
        void Damp(in VoiceHandle voice, int strings);
    }

    /// <summary>IHarpSound over the app's IAudioEngine (allocation-free; nothing is sent before the engine is ready).</summary>
    public sealed class EngineSound : IHarpSound
    {
        readonly IAudioEngine _engine;
        readonly double _block;

        public EngineSound(IAudioEngine engine)
        {
            _engine = engine;
            AudioSettings.GetDSPBufferSize(out int length, out _);
            int sr = engine != null && engine.SampleRate > 0 ? engine.SampleRate : AudioSettings.outputSampleRate;
            _block = sr > 0 && length > 0 ? (double)length / sr : 256.0 / 48000.0;
        }

        public IAudioEngine Engine => _engine;
        /// <summary>One DSP block in seconds (256 frames at 48 kHz = 5.33 ms on the headset).</summary>
        public double BlockSeconds => _block;
        public double Now => _engine.Now;
        public double PluckBase => _engine.FrameNow + _block;
        public double RingSeconds(int midi) => _engine.HarpRingSeconds(midi);
        public float PartialTau(int stringIndex, int partial) => (float)_engine.HarpPartialTau(stringIndex, partial);

        public VoiceHandle Pluck(int stringIndex, float vel, double when, in PluckOptions options)
            => _engine.IsReady ? _engine.Pluck(stringIndex, vel, when, options) : VoiceHandle.None;

        public void Release(in VoiceHandle voice)
        {
            if (_engine.IsReady && voice.IsValid) _engine.Release(voice, thud: false);
        }

        public void Damp(in VoiceHandle voice, int strings)
        {
            if (_engine.IsReady && voice.IsValid) _engine.Damp(voice, strings);
        }
    }
}
