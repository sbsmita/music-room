// The master chain (spec 06 section 1.6): input gain 0.9 -> DynamicsCompressor (threshold -14 dB, knee 18 dB, ratio 4,
// attack 3 ms, release 180 ms) -> high shelf 7.5 kHz +1.5 dB -> ceiling 0.98. Runs as OnAudioFilterRead on the
// AudioListener's GameObject, which receives the final mix including the mixer output and the Meta reverb (spec 04 E3').
// The compressor is the reference kernel (DynamicsCompressor) with NO 6 ms look-ahead, so the master adds no latency.
// The app's only master: the classic sound (2026-11-10); the upgraded room's listener stage was deleted on 2026-11-11.
using System;
using MusicRoom.Audio.Dsp;
using UnityEngine;

namespace MusicRoom.Audio
{
    [RequireComponent(typeof(AudioListener))]
    public sealed class MasterBus : MonoBehaviour
    {
        public float inputGain = 0.9f;
        public float thresholdDb = -14f, kneeDb = 18f, ratio = 4f;
        public float attackSeconds = 0.003f, releaseSeconds = 0.18f;
        public float shelfHz = 7500f, shelfDb = 1.5f;
        public float ceiling = 0.98f;

        public float PeakOut => _peak;
        public double GainDb => _comp != null ? _comp.GainDb : 0;
        public DynamicsCompressor Compressor => _comp;
        public long BlocksRendered => _blocks;

        private DynamicsCompressor _comp;
        private Biquad _shelfL, _shelfR;
        private volatile float _peak;
        private volatile bool _ready;
        private long _blocks;

        private void Awake() => Configure();
        private void OnValidate() { if (Application.isPlaying) Configure(); }

        /// <summary>Main thread: (re)build the compressor (the k bisection is not for the audio thread).</summary>
        public void Configure()
        {
            _ready = false;
            double sr = AudioSettings.outputSampleRate;
            _comp = new DynamicsCompressor(sr, thresholdDb, kneeDb, ratio, attackSeconds, releaseSeconds, 0.0, 2);
            _shelfL = Biquad.Create(BiquadType.Highshelf, shelfHz, 0, shelfDb, sr);
            _shelfR = Biquad.Create(BiquadType.Highshelf, shelfHz, 0, shelfDb, sr);
            _ready = true;
        }

        /// <summary>Makeup gain in dB (-14/18/4 gives +2.14 dB, the reference kernel's value).</summary>
        public double MakeupDb => _comp != null ? _comp.MakeupDb : 0;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_ready) return;
            Process(data, channels);
            _blocks++;
        }

        private void Process(float[] data, int channels)
        {
            int n = data.Length;
            int frames = n / channels;
            float pre = inputGain, ceil = ceiling, peak = 0;
            for (int i = 0; i < n; i++) data[i] *= pre;
            _comp.Process(data, channels, frames);
            for (int i = 0, j = 0; i < frames; i++, j += channels)
            {
                float yl = Clip(_shelfL.Process(data[j]), ceil);
                data[j] = yl;
                if (Math.Abs(yl) > peak) peak = Math.Abs(yl);
                if (channels > 1)
                {
                    float yr = Clip(_shelfR.Process(data[j + 1]), ceil);
                    data[j + 1] = yr;
                    if (Math.Abs(yr) > peak) peak = Math.Abs(yr);
                    for (int c = 2; c < channels; c++) data[j + c] = 0; // stereo app
                }
            }
            _peak = peak;
        }

        private static float Clip(float v, float c) => v > c ? c : v < -c ? -c : v;
    }
}
