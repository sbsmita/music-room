// Base of every pooled voice. All times are absolute engine samples, sample = round(AudioSettings.dspTime * sampleRate),
// so a command's When maps to an exact sample. A voice adds its output into the source's mono block (and optionally
// into the resonator send). Start, release and stop are sample-accurate: Render splits the block at those samples
// (spec 04 E9). A stop ends the voice's SOURCE only: as in the reference graph (src.stop() leaves the filter and gain
// stages running) the filters ring on with zero input, times the envelope and gate, for RingSamples more; quiet one-shots
// (the damper thud, the harp's shh) only null against the reference renders with that tail (-39 dB without it, -108 dB with it).
using System;
using MusicRoom.Audio.Dsp;

namespace MusicRoom.Audio.Voices
{
    public abstract class Voice
    {
        /// <summary>Samples the filters keep ringing after the source stops: 21 ms at 48 kHz, more than 20 time constants of
        /// the slowest filter a voice uses (bandpass 260 Hz Q 0.8, tau 1 ms).</summary>
        public const int RingSamples = 1024;

        public int Id;          // engine-unique handle id (0 = anonymous)
        public short Group;     // choke group (0 = none)
        /// <summary>SynthFlags.NoSend: the source mixes this voice after its send to the drum room (set per start).</summary>
        public bool NoSend;

        public bool Active { get; private set; }
        public long StartSample { get; private set; }
        public bool Released { get; private set; }
        /// <summary>The source has stopped; only the filters' ring-out is left (stolen first).</summary>
        public bool SourceStopped { get; private set; }

        protected float SampleRate { get; private set; }

        /// <summary>Output gain shared by release, damp and choke ramps (the voice's final gain / "dest" gain).</summary>
        protected readonly Envelope Gate = new Envelope(2);

        private long _stopAt, _endAt;
        private long _releaseAt;
        private float _releaseTau;
        private float _releaseStopAfter;

        /// <summary>Audio thread: called by the source when it hands this voice a start command.</summary>
        protected void Begin(int id, short group, long startSample, float sampleRate, float gain)
        {
            Id = id;
            Group = group;
            StartSample = startSample;
            SampleRate = sampleRate;
            _stopAt = long.MaxValue;
            _endAt = long.MaxValue;
            _releaseAt = long.MaxValue;
            Released = false;
            SourceStopped = false;
            Gate.SetValue(gain);
            Active = true;
        }

        /// <summary>Hard stop of the source at an absolute sample (a scheduled source stop). The earliest stop wins;
        /// the voice ends RingSamples later.</summary>
        public void StopAt(long sample)
        {
            if (sample >= _stopAt) return;
            _stopAt = sample;
            _endAt = sample + RingSamples;
        }

        /// <summary>From sample the gain falls to 0 with time constant tau (setTargetAtTime), and the voice stops stopAfter
        /// seconds later (infinity: it ends by itself). A later call replaces an earlier one.</summary>
        public void ReleaseAt(long sample, float tauSeconds, float stopAfterSeconds)
        {
            _releaseAt = Math.Max(sample, StartSample);
            _releaseTau = tauSeconds;
            _releaseStopAfter = stopAfterSeconds;
        }

        /// <summary>Immediately silence and free (voice stealing). Clicks; the pools are sized so it is rare.</summary>
        public void Kill() { Active = false; }

        /// <summary>Adds this voice's samples for the block [blockStart, blockStart + frames) into mix (and send, which may be null).
        /// Returns false once the voice has finished.</summary>
        public bool Render(float[] mix, float[] send, long blockStart, int frames)
        {
            if (!Active) return false;
            long blockEnd = blockStart + frames;
            if (StartSample >= blockEnd) return true; // scheduled for a later block
            int from = StartSample > blockStart ? (int)(StartSample - blockStart) : 0;
            while (from < frames)
            {
                long now = blockStart + from;
                if (now >= _endAt)
                {
                    Active = false;
                    return false;
                }
                if (!SourceStopped && now >= _stopAt) SourceStopped = true;
                if (now >= _releaseAt)
                {
                    Gate.SetTarget(0.0, _releaseTau * SampleRate);
                    if (!float.IsInfinity(_releaseStopAfter)) StopAt(now + (long)(_releaseStopAfter * SampleRate));
                    _releaseAt = long.MaxValue;
                    Released = true;
                    OnRelease(now);
                }
                long next = blockEnd;
                if (_releaseAt < next) next = _releaseAt;
                if (!SourceStopped && _stopAt < next) next = _stopAt;
                if (_endAt < next) next = _endAt;
                int to = (int)(next - blockStart);
                if (to > from && !Process(mix, send, from, to))
                {
                    Active = false;
                    return false;
                }
                from = to;
            }
            return true;
        }

        /// <summary>Add samples [from, to) of this block (zero source input once SourceStopped). Return false when the voice
        /// has ended by itself.</summary>
        protected abstract bool Process(float[] mix, float[] send, int from, int to);

        /// <summary>Hook for voices that react to their release (e.g. start a damper thud).</summary>
        protected virtual void OnRelease(long atSample) { }

        protected int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);

        /// <summary>Frames a source plays for `seconds` (src.stop(t + seconds)): the stop-time-to-frame rule rounds the stop
        /// time to 1/1024 frame, then up (a 9.8 ms knock stops after 2871 frames; the floor tom's 54720.00000000001 after
        /// 54720). The 0.01-frame allowance does the same for the float noise in our decays (1.09f * 48000 = 52320.0016).</summary>
        protected long StopSamples(double seconds) => (long)Math.Ceiling(seconds * SampleRate - 0.01);
    }
}
