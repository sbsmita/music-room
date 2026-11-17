// One spatial sound source of the room (one named source, spec 06 sections 1.4 and 10): an AudioSource that plays a
// silent looping carrier clip, plus this OnAudioFilterRead mixer of pooled voices. spatializePostEffects = true puts
// the spatializer (Meta XR Audio) AFTER this filter, so the synthesized signal is what gets spatialized (spec 04 E1).
// The main thread only calls Post/Stage/Commit; everything else runs on the audio thread and never allocates.
// Component order matters: AudioSource first, then MetaXRAudioSource, then this component, no other audio filters.
// The block renderer (RenderBlock) is the same code offline: EditMode null tests call InitOffline + RenderBlock with a
// sample clock of their own, so what they compare with the 06 reference WAVs is exactly what the audio thread runs.
using System;
using System.Diagnostics;
using System.Threading;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Voices;
using UnityEngine;

namespace MusicRoom.Audio
{
    /// <summary>A per-source extension that runs on the audio thread (ambience beds, the drum room reverb).</summary>
    public interface ISynthProcessor
    {
        /// <summary>Audio thread: a Custom command addressed to this source, effective at atSample.</summary>
        void OnCommand(in SynthCommand c, long atSample);

        /// <summary>Audio thread: read / add / replace this block's samples in mix[0..frames).</summary>
        void Process(float[] mix, long blockStart, int frames);

        /// <summary>Audio thread, instead of Process while the source is idle (its bus at and heading to zero, no voice, no
        /// pending input: nothing can be heard). Advance the clocks a later Process depends on (LFO phase, noise position,
        /// timers and their random draws) by frames, without producing samples. Filter state may restart from rest: the
        /// bus ramps up from zero (tau 0.4 s) when the source comes back on.</summary>
        void Skip(long blockStart, int frames);
    }

    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class SynthSource : MonoBehaviour
    {
        public const int MaxFrames = 8192;

        [Tooltip("Spatialize with the project spatializer. Off: a 2D source (UI sounds).")]
        public bool spatial = true;
        public SourceId id;

        [Header("Voice pools (allocated once)")]
        public int noiseVoices = 16;
        public int toneVoices = 8;
        public int metalVoices;
        public int bufferVoices;

        [Header("Body resonator (piano soundboard / harp soundbox); empty = none")]
        public float[] resonatorModes = Array.Empty<float>();
        public float resonatorSeconds = 0.28f;
        public float resonatorGain = 0.22f;
        public uint resonatorSeed = 1;
        [Tooltip("Send the buffer voices' resonator input to this source's body instead (the room has ONE piano soundboard, " +
                 "heard from piano-mid)")]
        public SynthSource resonatorTarget;

        [Header("Bus")]
        public float busGain = 1f;

        [Header("Send to another source (drum room): mix * sendGain is added to the target's next block")]
        public SynthSource sendTarget;
        public float sendGain;

        /// <summary>Note buffers for BufferVoices. Set by the engine before the first StartBuffer command.</summary>
        [NonSerialized] public SampleBank Bank;

        /// <summary>The Burst graph (burst.md section 3; ENGINE-API.md): the first source of each block renders it, every source
        /// adds its own slice. Set by AudioEngine.Init (or an offline harness), null = no graph.</summary>
        public AudioGraphHost Graph { get => Volatile.Read(ref _graph); set => Volatile.Write(ref _graph, value); }
        private AudioGraphHost _graph;

        public float Rms => _rms;                                    // last block's output rms (Sunny's mouth, tests)
        /// <summary>Sum of squares of the mono output since start (the spatializer's input; level calibration tests).</summary>
        public double OutputEnergy => Volatile.Read(ref _energy);
        public int DroppedCommands => Volatile.Read(ref _dropped);   // ring full or no pool for the op
        public int StolenVoices => Volatile.Read(ref _stolen);
        public long BlocksRendered => Interlocked.Read(ref _blocks); // audio thread ticks (tests)
        public double SampleRate => _sampleRate;
        public float LastBlockMicros => _lastMicros;
        public float MaxBlockMicros => _maxMicros;                   // worst block since ResetStats()
        /// <summary>Mean block time since ResetStats() (the stats window has its own block count; BlocksRendered never resets).</summary>
        public float AvgBlockMicros => _statBlocks > 0 ? (float)(_sumMicros / _statBlocks) : 0f;
        public int ActiveVoices { get { int n = 0; for (int i = 0; i < _poolEnd; i++) if (_voices[i].Active) n++; return n; } }
        /// <summary>Restart Avg/MaxBlockMicros (main thread; a benign race with the audio thread, stats only).</summary>
        public void ResetStats() { _maxMicros = 0; _sumMicros = 0; _statBlocks = 0; }
        /// <summary>Blocks rendered as idle (bus silent, nothing playing: processors skipped, output zero). Tests, profiling.</summary>
        public long IdleBlocks => Interlocked.Read(ref _idleBlocks);
        /// <summary>Off: never skip idle blocks (tests compare the skipped and the always-processed renders).</summary>
        [NonSerialized] public bool idleSkip = true;
        /// <summary>AudioPresence paused this source (a displayed instrument, silent for 1 s: AudioSource.Pause). Unity keeps
        /// calling a paused source's filter, so while nothing is pending or playing the block only keeps time (no voices,
        /// processors or bus): main -> audio.</summary>
        public bool PresencePaused { get => Volatile.Read(ref _presencePaused) != 0; set => Volatile.Write(ref _presencePaused, value ? 1 : 0); }
        private int _presencePaused;
        /// <summary>Blocks a paused source skipped (tests, profiling).</summary>
        public long PausedBlocks => Interlocked.Read(ref _pausedBlocks);
        private long _pausedBlocks;
        /// <summary>The bus level below which a bus heading to zero counts as silent (-120 dB).</summary>
        public const double SilentBus = 1e-6;
        /// <summary>The body resonator (null when this source has none).</summary>
        public ModalResonator Resonator => _resonator;

        private static AudioClip s_carrier;

        private AudioSource _source;
        private SpscRing<SynthCommand> _ring;
        private Voice[] _voices;
        private int _toneFrom, _metalFrom, _bufferFrom, _poolEnd;
        private readonly ISynthProcessor[] _processors = new ISynthProcessor[4];
        private int _processorCount;
        private float[] _mix, _send, _dry, _pending, _pendingRes;
        private int _pendingFrames, _pendingResFrames;
        private ModalResonator _resonator;
        private readonly Envelope _bus = new Envelope(2);
        private double _sampleRate;
        private volatile float _rms, _lastMicros, _maxMicros;
        private static readonly double TicksToMicros = 1e6 / Stopwatch.Frequency;
        private double _sumMicros, _energy;
        private long _blocks;
        private long _statBlocks;     // blocks in the current stats window (ResetStats)
        private long _idleBlocks;
        private int _dropped, _stolen;

        private void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _source = GetComponent<AudioSource>();
            ConfigureAudioSource(_source, spatial, (int)_sampleRate);
            Allocate();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        /// <summary>Offline use (EditMode null tests, tools): allocate everything Awake would, without touching the
        /// AudioSource or the audio device, then drive the source with Stage/Commit and RenderBlock. Main thread, once.</summary>
        public void InitOffline(int sampleRate)
        {
            _sampleRate = sampleRate;
            Allocate();
        }

        private void Allocate()
        {
            // The mixer thread may call OnAudioFilterRead while Awake is still here (measured in batchmode), so everything
            // is built first and the voice pool, which RenderBlock checks, is published last.
            _ring = new SpscRing<SynthCommand>(512);
            _mix = new float[MaxFrames];
            _send = new float[MaxFrames];
            _dry = new float[MaxFrames];
            _pending = new float[MaxFrames];
            _pendingRes = new float[MaxFrames];
            if (resonatorModes != null && resonatorModes.Length > 0)
            {
                var resonator = new ModalResonator();
                resonator.Build(resonatorModes, resonatorSeconds, (float)_sampleRate, resonatorSeed);
                _resonator = resonator;
            }
            _bus.SetValue(busGain);
            _toneFrom = noiseVoices;
            _metalFrom = _toneFrom + toneVoices;
            _bufferFrom = _metalFrom + metalVoices;
            _poolEnd = _bufferFrom + bufferVoices;
            var voices = new Voice[_poolEnd];
            for (int i = 0; i < _poolEnd; i++)
            {
                voices[i] = i < _toneFrom ? new NoiseVoice()
                    : i < _metalFrom ? new ToneVoice()
                    : i < _bufferFrom ? (Voice)new MetalVoice()
                    : new BufferVoice();
            }
            _ghosts = new Voice[] { new NoiseVoice(), new ToneVoice(), new MetalVoice(), new BufferVoice() };
            _ghostLeft = new int[_ghosts.Length];
            _ghostMix = new float[MaxFrames];
            Volatile.Write(ref _voices, voices);
        }

        // Voice stealing (Take) fades the victim out over StealFadeSamples instead of cutting it (a click), like the Burst
        // pools' ghost slots: the victim's object moves to its pool's ghost slot and keeps rendering under a linear fade from
        // the block's start; the slot's previous object (silent by then) takes the new note. One ghost per pool (noise, tone,
        // metal, buffer): a second steal in the same pool within the fade cuts the older ghost.
        public const int StealFadeSamples = 128;
        private Voice[] _ghosts;
        private int[] _ghostLeft;
        private float[] _ghostMix;
        /// <summary>Stolen voices that faded out instead of being cut (audio thread count; tests).</summary>
        public int GhostFades => Volatile.Read(ref _ghostFades);
        private int _ghostFades;

        private void OnEnable()
        {
            if (_source != null && !_source.isPlaying) _source.Play();
        }

        private void OnDestroy() => AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

        /// <summary>Main thread: the carrier plays again if something stopped it (an app pause, the output restarting) while
        /// this source is enabled and not paused on display (PresencePaused). True when it had to be started.</summary>
        public bool EnsurePlaying()
        {
            if (_source == null || !isActiveAndEnabled || PresencePaused || _source.isPlaying) return false;
            _source.Play();
            return true;
        }

        /// <summary>The settings every synth source needs (spec 04 section 2.1 / 4.2).</summary>
        public static void ConfigureAudioSource(AudioSource s, bool spatialize, int sampleRate)
        {
            s.clip = Carrier(sampleRate);
            s.loop = true;
            s.playOnAwake = false;
            s.spatialize = spatialize;
            s.spatializePostEffects = true;      // spatializer AFTER OnAudioFilterRead
            s.spatialBlend = spatialize ? 1f : 0f;
            s.dopplerLevel = 0f;
            s.priority = 0;                      // never the one culled
            s.rolloffMode = AudioRolloffMode.Logarithmic; // = inverse distance law, AudioManager Rolloff Scale 1.1 = rolloff factor
            s.minDistance = 0.6f;                // reference distance (spec 06 section 1.4)
            s.maxDistance = 30f;                 // max distance (spec 06 section 1.4)
            s.bypassReverbZones = true;
            s.reverbZoneMix = 0f;
        }

        /// <summary>Shared silent mono carrier (1 s). Main thread.</summary>
        public static AudioClip Carrier(int sampleRate)
        {
            if (s_carrier == null || s_carrier.frequency != sampleRate)
            {
                s_carrier = AudioClip.Create("SynthCarrier", sampleRate, 1, sampleRate, false);
                s_carrier.SetData(new float[sampleRate], 0);
            }
            return s_carrier;
        }

        /// <summary>Main thread: queue a command and publish it (with anything staged before it).</summary>
        public bool Post(in SynthCommand c)
        {
            bool ok = Stage(c);
            Commit();
            return ok;
        }

        /// <summary>Main thread: queue without publishing; Commit() releases the batch so it starts in one audio block.</summary>
        public bool Stage(in SynthCommand c)
        {
            if (_ring != null && _ring.TryStage(c)) return true;
            Interlocked.Increment(ref _dropped);
            return false;
        }

        public void Commit() => _ring?.Commit();

        /// <summary>Main thread, before playback (or at any time; publication is ordered).</summary>
        public void AddProcessor(ISynthProcessor p)
        {
            int n = _processorCount;
            if (n >= _processors.Length) return;
            _processors[n] = p;
            Volatile.Write(ref _processorCount, n + 1);
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            // AudioSettings.Reset or a device change restarts the output; restart the carrier and drop old voices.
            _sampleRate = AudioSettings.outputSampleRate;
            Post(new SynthCommand { Op = SynthOp.Panic });
            if (isActiveAndEnabled && !_source.isPlaying) _source.Play();
        }

        // ---------------- audio thread ----------------

        /// <summary>Audio thread, called by a sending source: accumulate into this source's next block. Callbacks are
        /// serial on one mixer thread (spec 04 E4), so a plain add is safe; if the target already ran this block the
        /// send lands one block (5.3 ms) later, which is inaudible for reverb.</summary>
        private void AddInput(float[] src, int frames, float gain)
        {
            if (Volatile.Read(ref _voices) == null) return;   // this source is not allocated yet (start-up)
            int n = Math.Min(frames, MaxFrames);
            for (int i = 0; i < n; i++) _pending[i] += src[i] * gain;
            if (n > _pendingFrames) _pendingFrames = n;
        }

        /// <summary>Audio thread, called by a source whose resonatorTarget is this one: its buffer voices' body send joins
        /// this source's resonator input (same block, or the next one if this source already ran).</summary>
        private void AddResonatorInput(float[] src, int frames)
        {
            if (Volatile.Read(ref _voices) == null) return;   // this source is not allocated yet (start-up)
            int n = Math.Min(frames, MaxFrames);
            for (int i = 0; i < n; i++) _pendingRes[i] += src[i];
            if (n > _pendingResFrames) _pendingResFrames = n;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            long t0 = Stopwatch.GetTimestamp();
            long blockStart = (long)Math.Round(AudioSettings.dspTime * _sampleRate);   // same value for every source in the block
            if (!RenderBlock(data, channels, blockStart)) return;
            float us = (float)((Stopwatch.GetTimestamp() - t0) * TicksToMicros);
            _lastMicros = us;
            if (us > _maxMicros) _maxMicros = us;
            _sumMicros += us;
            _statBlocks++;
            Interlocked.Increment(ref _blocks);
        }

        /// <summary>Audio thread (or an offline caller that owns this source): drain the commands, render the block that
        /// starts at engine sample blockStart into data (interleaved, the mono mix in every channel). Returns false when
        /// the block could not be rendered (it is then silent).</summary>
        public bool RenderBlock(float[] data, int channels, long blockStart)
        {
            int frames = data.Length / channels;
            if (frames > MaxFrames || Volatile.Read(ref _voices) == null)
            {
                Array.Clear(data, 0, data.Length);
                return false;
            }
            double sr = _sampleRate;
            Array.Clear(_mix, 0, frames);
            Array.Clear(_dry, 0, frames);
            bool bodySend = _resonator != null || resonatorTarget != null;
            if (bodySend) Array.Clear(_send, 0, frames);

            Drain(blockStart, (float)sr);
            // The graph renders once per block (the first source to get here); a source with graph signal is never idle.
            AudioGraphHost graph = Graph;
            bool graphSignal = graph != null && GraphBridge.Render(graph, (int)id, blockStart, frames);

            if (!graphSignal && Volatile.Read(ref _presencePaused) != 0 && Silent())
            {
                // Paused on display and silent: keep the processors' clocks, nothing else (the bus keeps its level, so the
                // first note after the resume is not faded in).
                int pcPaused = Volatile.Read(ref _processorCount);
                for (int p = 0; p < pcPaused; p++) _processors[p].Skip(blockStart, frames);
                Array.Clear(data, 0, frames * channels);
                _rms = 0f;
                Interlocked.Increment(ref _pausedBlocks);
                return true;
            }
            if (idleSkip && !graphSignal && IsIdle())
            {
                // Nothing can be heard (the ambience beds while their switch is off): the processors only keep time.
                _bus.SetValue(0);
                int pcIdle = Volatile.Read(ref _processorCount);
                for (int p = 0; p < pcIdle; p++) _processors[p].Skip(blockStart, frames);
                Array.Clear(data, 0, frames * channels);
                _rms = 0f;
                Interlocked.Increment(ref _idleBlocks);
                return true;
            }

            float[] send = bodySend ? _send : null;
            for (int i = 0; i < _poolEnd; i++)
            {
                Voice v = _voices[i];
                if (v.Active) v.Render(v.NoSend ? _dry : _mix, send, blockStart, frames);
            }
            RenderGhosts(blockStart, frames);
            if (_pendingFrames > 0)
            {
                int n = Math.Min(_pendingFrames, frames);
                for (int i = 0; i < n; i++) _mix[i] += _pending[i];
                Array.Clear(_pending, 0, _pendingFrames);
                _pendingFrames = 0;
            }
            int pc = Volatile.Read(ref _processorCount);
            for (int p = 0; p < pc; p++) _processors[p].Process(_mix, blockStart, frames);
            if (resonatorTarget != null) resonatorTarget.AddResonatorInput(_send, frames);
            if (_resonator != null)
            {
                if (_pendingResFrames > 0)
                {
                    int n = Math.Min(_pendingResFrames, frames);
                    for (int i = 0; i < n; i++) _send[i] += _pendingRes[i];
                    Array.Clear(_pendingRes, 0, _pendingResFrames);
                    _pendingResFrames = 0;
                }
                _resonator.ProcessAdd(_send, _mix, frames, resonatorGain);
            }
            // The graph's slice (the new instruments) joins after the classic voices and bodies, before the send and the bus.
            if (graphSignal) GraphBridge.AddSlice(graph, (int)id, _mix, frames);
            if (sendTarget != null && sendGain != 0f) sendTarget.AddInput(_mix, frames, sendGain);
            for (int i = 0; i < frames; i++) _mix[i] += _dry[i];   // NoSend voices: after the drum-room send

            // The input (silent carrier) is replaced; the same mono signal goes to every channel.
            double sum = 0;
            int j = 0;
            for (int i = 0; i < frames; i++)
            {
                float s = _mix[i] * _bus.Next();
                sum += s * s;
                for (int c = 0; c < channels; c++) data[j++] = s;
            }
            _rms = (float)Math.Sqrt(sum / Math.Max(1, frames));
            Volatile.Write(ref _energy, _energy + sum);
            return true;
        }

        /// <summary>Audio thread, after Drain: the bus is silent and heading to zero (switched off long enough ago: 5.5 s
        /// at tau 0.4 s), no voice is active and no input is pending, so this block is silence whatever the processors do.
        /// Sources with a body resonator (bus 1, always on) never count as idle.</summary>
        /// <summary>Audio thread, after Drain: no voice is active and no input is pending (the bus aside).</summary>
        private bool Silent()
        {
            if (_pendingFrames > 0 || _pendingResFrames > 0 || GhostsActive()) return false;
            for (int i = 0; i < _poolEnd; i++) if (_voices[i].Active) return false;
            return true;
        }

        private bool IsIdle()
        {
            if (_resonator != null || _pendingFrames > 0 || _pendingResFrames > 0 || GhostsActive()) return false;
            if (!_bus.IsQuiet(SilentBus)) return false;
            for (int i = 0; i < _poolEnd; i++) if (_voices[i].Active) return false;
            return true;
        }

        private void Drain(long blockStart, float sr)
        {
            while (_ring.TryDequeue(out SynthCommand c))
            {
                long at = c.When > 0 ? Math.Max(blockStart, (long)Math.Round(c.When * sr))
                    : blockStart + (long)Math.Round(-c.When * sr);
                bool noSend = (c.Flags & SynthFlags.NoSend) != 0;
                switch (c.Op)
                {
                    case SynthOp.StartNoise:
                        if (Take(0, _toneFrom) is NoiseVoice nv) { nv.Start(c, at, sr); nv.NoSend = noSend; } else Interlocked.Increment(ref _dropped);
                        break;
                    case SynthOp.StartTone:
                        if (Take(_toneFrom, _metalFrom) is ToneVoice tv) { tv.Start(c, at, sr); tv.NoSend = noSend; } else Interlocked.Increment(ref _dropped);
                        break;
                    case SynthOp.StartMetal:
                        if (Take(_metalFrom, _bufferFrom) is MetalVoice mv) { mv.Start(c, at, sr); mv.NoSend = noSend; } else Interlocked.Increment(ref _dropped);
                        break;
                    case SynthOp.StartBuffer:
                        if (Bank != null && Take(_bufferFrom, _poolEnd) is BufferVoice bv) { bv.Start(c, at, sr, Bank); bv.NoSend = noSend; } else Interlocked.Increment(ref _dropped);
                        break;
                    case SynthOp.Release:
                        for (int i = 0; i < _poolEnd; i++)
                        {
                            Voice v = _voices[i];
                            if (v.Active && v.Id == c.VoiceId) v.ReleaseAt(at, c.Tau, c.StopAfter);
                        }
                        break;
                    case SynthOp.ChokeGroup:
                        for (int i = 0; i < _poolEnd; i++)
                        {
                            Voice v = _voices[i];
                            if (v.Active && v.Group == c.Group && v.StartSample <= at) v.ReleaseAt(at, c.Tau, c.StopAfter);
                        }
                        break;
                    case SynthOp.SetBusGain:
                        _bus.SetTarget(c.Level, c.Tau * sr);
                        break;
                    case SynthOp.Custom:
                    {
                        int pc = Volatile.Read(ref _processorCount);
                        for (int p = 0; p < pc; p++) _processors[p].OnCommand(c, at);
                        break;
                    }
                    case SynthOp.Panic:
                        for (int i = 0; i < _poolEnd; i++) _voices[i].Kill();
                        if (_ghosts != null) for (int g = 0; g < _ghosts.Length; g++) { _ghosts[g].Kill(); _ghostLeft[g] = 0; }
                        break;
                }
            }
        }

        /// <summary>A free voice from [from, to), else steal: the oldest released or ringing-out one, else the oldest. A stolen
        /// note fades out from its pool's ghost slot (StealFadeSamples); a mere ring-out is cut as before.</summary>
        private Voice Take(int from, int to)
        {
            Voice best = null;
            int bestIndex = -1;
            bool bestReleased = false;
            long bestStart = long.MaxValue;
            for (int i = from; i < to; i++)
            {
                Voice v = _voices[i];
                if (!v.Active) return v;
                bool rel = v.Released || v.SourceStopped;
                if (best == null || (rel && !bestReleased) || (rel == bestReleased && v.StartSample < bestStart))
                {
                    best = v;
                    bestIndex = i;
                    bestReleased = rel;
                    bestStart = v.StartSample;
                }
            }
            if (best == null) return null;
            // A voice whose source already stopped is only its filters' ring-out (x 0.0005): not a stolen note.
            if (best.SourceStopped) { best.Kill(); return best; }
            Interlocked.Increment(ref _stolen);
            int g = from == 0 ? 0 : from == _toneFrom ? 1 : from == _metalFrom ? 2 : 3;
            Voice ghost = _ghosts != null && g < _ghosts.Length ? _ghosts[g] : null;
            if (ghost == null || ghost.GetType() != best.GetType()) { best.Kill(); return best; }
            ghost.Kill();                 // an older ghost still fading is cut (rare: two steals in a pool within 2.7 ms)
            _voices[bestIndex] = ghost;   // the silent object takes the new note
            _ghosts[g] = best;            // the victim fades out from the ghost slot
            _ghostLeft[g] = StealFadeSamples;
            Interlocked.Increment(ref _ghostFades);
            return ghost;
        }

        private bool GhostsActive()
        {
            if (_ghosts == null) return false;
            for (int g = 0; g < _ghosts.Length; g++) if (_ghostLeft[g] > 0 && _ghosts[g].Active) return true;
            return false;
        }

        /// <summary>Audio thread: each fading ghost renders into the scratch and joins the mix under its linear fade (gain
        /// left / StealFadeSamples falling one step a sample); a finished fade frees it.</summary>
        private void RenderGhosts(long blockStart, int frames)
        {
            if (_ghosts == null) return;
            for (int g = 0; g < _ghosts.Length; g++)
            {
                int left = _ghostLeft[g];
                if (left <= 0) continue;
                Voice v = _ghosts[g];
                if (!v.Active) { _ghostLeft[g] = 0; continue; }
                Array.Clear(_ghostMix, 0, frames);
                v.Render(_ghostMix, null, blockStart, frames);
                float[] dst = v.NoSend ? _dry : _mix;
                int n = Math.Min(left, frames);
                const float step = 1f / StealFadeSamples;
                for (int i = 0; i < n; i++) dst[i] += _ghostMix[i] * ((left - i) * step);
                left -= n;
                _ghostLeft[g] = left;
                if (left <= 0) v.Kill();
            }
        }
    }
}
