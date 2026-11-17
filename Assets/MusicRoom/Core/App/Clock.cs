// The app's time base (spec 05 section 0): wall time sampled ONCE per frame so every "3 ms since the last hit"
// test in a frame means the same thing; dt is unscaled and capped at 50 ms. The audio clock is
// AudioSettings.dspTime (see IAudioEngine.Now); Time.* is for visuals only.
using System;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class Clock
    {
        public const float MaxDt = 0.05f;

        /// <summary>Milliseconds since the app started, frame-constant (the app's millisecond clock).</summary>
        public double NowMs { get; private set; }
        /// <summary>Seconds since the app started, frame-constant.</summary>
        public double Now { get; private set; }
        /// <summary>This frame's step: min(unscaledDeltaTime, 0.05).</summary>
        public float Dt { get; private set; }
        public int Frame { get; private set; }
        /// <summary>The audio clock sampled at the frame start: AudioSettings.dspTime (the next block to be mixed), or the
        /// test's scripted clock (BootOptions.AudioClock).</summary>
        public double AudioNow { get; private set; }

        /// <summary>The audio clock source (null = AudioSettings.dspTime). Set once by App from BootOptions.AudioClock.</summary>
        public Func<double> AudioClock { get; set; }

        double _start = double.NaN;

        double ReadAudio() => AudioClock != null ? AudioClock() : AudioSettings.dspTime;

        public void Begin()
        {
            double t = Time.unscaledTimeAsDouble;
            if (double.IsNaN(_start)) _start = t;
            Now = t - _start;
            NowMs = Now * 1000.0;
            Dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
            AudioNow = ReadAudio();
            Frame++;
        }

        /// <summary>Tests: drive the clock by hand (no Time.* dependency). App.Step calls it with ManualFrames.</summary>
        public void Step(float dt)
        {
            if (double.IsNaN(_start)) _start = 0;
            Dt = Mathf.Min(dt, MaxDt);
            Now += Dt;
            NowMs = Now * 1000.0;
            AudioNow = ReadAudio();
            Frame++;
        }
    }
}
