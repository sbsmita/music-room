// Sunny's chirps and voice lines (spec 06 section 8, spec 08 section 3.4).
// Lines are eSpeak NG renders (<module>/Resources/Voice/<id>.wav, 22.05 kHz mono), robotised ONCE (ARCHITECTURE section 0)
// the way the reference renders them: the line is resampled to the output rate, and the robot chain runs at that rate
// (ring 0.92/0.08 x 80 Hz sine, 5 ms = 240-frame comb fb .3 level .12, HP 120 Hz Q .7 dB, peak 2.5 kHz Q .8 +2 dB). So each
// line is resampled to the output rate (windowed sinc), given 60 ms of tail for the comb and filters to ring out, run
// through RobotVoice, and stored in the SampleBank; a BufferVoice plays it at rate 1 on the Guide source.
// The line table is data (12-new-instruments/platform.md 8.2, VoiceManifest): the Guide's manifest merged with every
// module's, so it grows with the instruments (200+ lines). Rendering is lazy (sunny-tour 7.1): at boot only the intro lines
// (on the render workers, after middle C's neighbours, as before); any other line when it is first asked for: VoiceLineReady
// (Sunny polls it while a line is pending) or PrepareVoiceLine start an asynchronous clip load, then the line renders on
// the voice worker and is ready a few frames later (inside Sunny's 0.14 s lead). Play of a line that is not rendered yet
// renders it on the spot (main thread), as before. At most MaxResident (24) robotised lines stay resident, least recently
// asked first out; a line asked in the last ProtectSeconds is kept even past that (a caller waiting on many lines at once).
// The output and mouth envelope of a line are allocated on the main thread when its render is requested, so the workers
// fill buffers in place. The mouth level is the RMS of the last 1024 output samples (a level analyser), precomputed
// every 256 frames. Time is the engine's audio clock (dspTime, or BootOptions.AudioClock in tests).
// A clip's samples are read into a pooled buffer (the worker gives it back after the render), so asking for lines makes
// no garbage beyond the line's own output; the clips import with Load In Background, so their decode runs off the main
// thread and a clip still loading is finished in Tick (never waited for on the main thread: a Play of a line whose data
// is still loading plays nothing, so callers ask Ready / Prepare first, as Sunny does). The voice worker runs at Android's background priority
// (Process.setThreadPriority through JNI, as the sample bank's workers: Thread.Priority does nothing under IL2CPP).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Voices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Audio.Synths
{
    /// <summary>A voice line's recording: mono samples at Rate (null Samples = no clip).</summary>
    public struct VoiceClip
    {
        public float[] Samples;
        public int Rate;
        public bool Valid => Samples != null && Rate > 0;
    }

    public sealed class GuideVoice : IDisposable
    {
        /// <summary>The app's line ids in table order (VoiceManifest.Default: the Guide's lines, then the modules'); voice
        /// slot AudioEngine.VoiceSlot0 + index.</summary>
        public static string[] LineIds => VoiceManifest.Default.Ids;
        /// <summary>The app's line count (VoiceManifest.Default).</summary>
        public static int LineCount => VoiceManifest.Default.Count;
        /// <summary>Rendered at boot (the first-launch tour starts with one of them), right after middle C's neighbours.</summary>
        public static readonly string[] BootLines = { "intro_vr", "intro_desk" };
        /// <summary>Robotised lines resident at most (sunny-tour 7.1: about 12 MB at 2.4 s a line).</summary>
        public const int MaxResident = 24;
        public const string ResourcesFolder = "Voice/";
        public const float Lead = 0.14f;          // robot lead: the voice starts asked + 0.14 s
        public const int MouthWindow = 1024;      // mouth analyser window (samples)
        public const int MouthHop = 256;
        public const double TailSeconds = 0.06;   // the comb (fb .3 every 5 ms) and the filters ringing past the words

        // Per line: Idle -> (Loading: the clip loads asynchronously) -> Queued (clip copied, buffers allocated) -> Rendering
        // (claimed by one thread, a worker or the main thread) -> Done (in the bank). Evicted lines go back to Idle.
        const int Idle = 0, Loading = 1, Queued = 2, Rendering = 3, Done = 4, Missing = 5;

        static readonly Stopwatch s_clock = Stopwatch.StartNew();

        readonly SynthKit _kit;
        readonly int _sr;
        readonly Func<double> _now;      // the engine's audio clock (dspTime, or a test's scripted clock)
        readonly VoiceManifest _manifest;
        readonly Func<string, VoiceClip> _clips;   // tests: the line's recording; null = Resources ("Voice/<id>")
        readonly Func<double> _realTime;           // the LRU's clock (real seconds)
        readonly int _count;
        readonly float[][] _out, _raw, _level;
        readonly int[] _state, _clipRate, _rawLength;
        readonly bool[] _rawPooled;
        readonly AudioClip[] _pendingClip;   // loaded, its audio data still decoding in the background
        // Read buffers: a line's raw samples (taken from the pool, given back after its render on any thread) and the
        // interleaved scratch of a multi-channel clip (main thread only).
        readonly Stack<float[]> _rawPool = new Stack<float[]>(4);
        readonly object _poolLock = new object();
        float[] _interleaved;
        /// <summary>Raw read buffers ever allocated (tests: the pool keeps this small).</summary>
        public int RawBuffersAllocated { get; private set; }
        /// <summary>A pooled raw buffer holds at least this many samples (4.5 s at 22.05 kHz: every line fits one).</summary>
        public const int RawBufferMin = 100000;
        readonly double[] _asked;
        readonly ResourceRequest[] _loads;
        int _loading;
        SampleBank _bank;
        int _slot0 = -1;
        VoiceHandle _current;
        int _currentLine = -1;
        double _currentStart, _currentEnd;
        int _resident;
        // the voice worker (StartWorker): lines waiting for their render
        Thread _worker;
        readonly object _queueLock = new object();
        readonly int[] _queue;
        int _queueHead, _queueCount;
        AutoResetEvent _signal;
        volatile bool _stop;

        /// <summary>Lines asked for in the last this many seconds are not evicted.</summary>
        public float ProtectSeconds = 2f;
        /// <summary>The resident limit (MaxResident; tests may lower it).</summary>
        public int ResidentLimit = MaxResident;

        /// <summary>manifest: the line table (null = the app's, VoiceManifest.Default). clips: where a line's recording comes
        /// from (null = Resources.Load("Voice/&lt;id&gt;"), asynchronously once StartWorker ran). realTime: the LRU's clock in
        /// seconds (null = a stopwatch).</summary>
        public GuideVoice(SynthKit kit, int sr, Func<double> now = null, VoiceManifest manifest = null,
            Func<string, VoiceClip> clips = null, Func<double> realTime = null)
        {
            _kit = kit;
            _sr = sr;
            _now = now ?? (() => AudioSettings.dspTime);
            _manifest = manifest ?? VoiceManifest.Default;
            _clips = clips;
            _realTime = realTime ?? (() => s_clock.Elapsed.TotalSeconds);
            _count = _manifest.Count;
            _out = new float[_count][];
            _raw = new float[_count][];
            _level = new float[_count][];
            _state = new int[_count];
            _clipRate = new int[_count];
            _rawLength = new int[_count];
            _rawPooled = new bool[_count];
            _pendingClip = new AudioClip[_count];
            _asked = new double[_count];
            _loads = new ResourceRequest[_count];
            _queue = new int[Math.Max(1, _count)];
            for (int i = 0; i < _count; i++) _asked[i] = double.NegativeInfinity;
        }

        /// <summary>This voice's line count (its manifest's).</summary>
        public int Lines => _count;
        public VoiceManifest Manifest => _manifest;
        /// <summary>Lines whose recording exists (or was not looked for yet: a missing clip is found when it first loads).</summary>
        public int LoadedLines { get { int n = 0; for (int i = 0; i < _count; i++) if (Volatile.Read(ref _state[i]) != Missing) n++; return n; } }
        public bool Has(string id) { int i = IndexOf(id); return i >= 0 && Volatile.Read(ref _state[i]) != Missing; }
        /// <summary>The manifest's dry seconds of the recording, -1 when unknown.</summary>
        public float Seconds(string id) { int i = IndexOf(id); return i >= 0 ? _manifest.Seconds(i) : -1f; }
        public bool Playing => _currentLine >= 0 && _now() < _currentEnd;
        public double CurrentEnd => _currentEnd;
        public int IndexOf(string id) => _manifest.IndexOf(id);
        /// <summary>Lines whose robotised render is in the bank (resident).</summary>
        public int RenderedLines { get { int n = 0; for (int i = 0; i < _count; i++) if (IsRendered(i)) n++; return n; } }
        public bool IsRendered(int line) => _bank != null && (uint)line < (uint)_count && _bank.Get(_slot0 + line) != null;
        /// <summary>Lines rendered on the main thread because they were played before their render (tools, a cold Play).</summary>
        public int SyncRenders { get; private set; }
        /// <summary>Renders done (any thread) and evictions (LRU), since construction.</summary>
        public int Renders => Volatile.Read(ref _renders);
        public int Evictions { get; private set; }
        int _renders;
        /// <summary>The robotised samples resident now (all lines), for the memory line.</summary>
        public long ResidentSamples { get { long n = 0; for (int i = 0; i < _count; i++) { var y = _bank?.Get(_slot0 + i); if (y != null) n += y.Length; } return n; } }
        /// <summary>The voice worker runs (StartWorker): asked lines load asynchronously and render on it.</summary>
        public bool Async => _worker != null;

        /// <summary>Main thread, before the queue starts: where the renders go (slots slot0 .. slot0 + Lines - 1).</summary>
        public void Attach(SampleBank bank, int slot0)
        {
            _bank = bank;
            _slot0 = slot0;
            if (bank != null && slot0 + _count > bank.SlotCount)
                Debug.LogError($"[Audio] voice: the bank has {bank.SlotCount} slots, {slot0 + _count} needed for {_count} lines");
        }

        /// <summary>Main thread, once (AudioEngine.Init): asked lines load asynchronously and render on a background worker.</summary>
        public void StartWorker()
        {
            if (_worker != null) return;
            _signal = new AutoResetEvent(false);
            _androidPriority = Application.platform == RuntimePlatform.Android;   // main thread
            _worker = new Thread(WorkerRun) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "MusicRoom.Voice" };
            _worker.Start();
        }

        public void Dispose()
        {
            _stop = true;
            _signal?.Set();
            _worker = null;
        }

        // ------------------------------------------------------------------ requests (main thread)

        double RealNow => _realTime();

        /// <summary>The line is rendered (resident); if not, its render is started (VoiceLineReady: Sunny polls this while a
        /// line is pending). Allocation-free once asked.</summary>
        public bool Ready(string id)
        {
            int i = IndexOf(id);
            if (i < 0) return false;
            _asked[i] = RealNow;
            if (IsRendered(i)) return true;
            Request(i);
            return IsRendered(i);
        }

        /// <summary>Start the line's render if it is not resident (a tip about to play). False for an unknown or missing line.</summary>
        public bool Prepare(string id)
        {
            int i = IndexOf(id);
            if (i < 0) return false;
            _asked[i] = RealNow;
            if (!IsRendered(i)) Request(i);
            return Volatile.Read(ref _state[i]) != Missing;
        }

        void Request(int i)
        {
            if (_bank == null || Volatile.Read(ref _state[i]) != Idle) return;
            if (_worker != null && _clips == null)
            {
                // the clip loads off the main thread (Decompress On Load decodes it there); Tick copies it and queues the render
                _loads[i] = Resources.LoadAsync<AudioClip>(ResourcesFolder + _manifest.Id(i));
                Volatile.Write(ref _state[i], Loading);
                _loading++;
                Trim();   // room for it before it lands
                return;
            }
            if (!LoadNow(i)) return;
            Trim();
            if (_worker != null) { Enqueue(i); return; }
            Render(i, publish: true);   // no worker (tools, tests): at once
        }

        /// <summary>Main thread: the clip's samples, the line's output and envelope allocated; state Queued. False: no clip.</summary>
        bool LoadNow(int i)
        {
            if (_clips != null)
            {
                VoiceClip c = _clips(_manifest.Id(i));
                if (!c.Valid) return NoClip(i);
                _raw[i] = c.Samples;
                _rawLength[i] = c.Samples.Length;
                _rawPooled[i] = false;
                _clipRate[i] = c.Rate;
                return Loaded(i);
            }
            AudioClip clip = Resources.Load<AudioClip>(ResourcesFolder + _manifest.Id(i));
            StartAudioData(clip);
            if (clip != null && clip.loadState == AudioDataLoadState.Loading)
            {
                // Load In Background: the decode is still running. Never wait on the main thread for it: Tick finishes the
                // line once its data is in (the render then runs on the worker, or at once without one).
                if (_loads[i] == null && _pendingClip[i] == null) _loading++;   // not counted yet
                _pendingClip[i] = clip;
                Volatile.Write(ref _state[i], Loading);
                return false;
            }
            return ReadInto(i, clip) ? Loaded(i) : NoClip(i);
        }

        bool NoClip(int i)
        {
            Volatile.Write(ref _state[i], Missing);
            Debug.LogWarning($"[Audio] voice: no recording for line {_manifest.Id(i)} (Resources/{ResourcesFolder}{_manifest.Id(i)})");
            return false;
        }

        /// <summary>The line's raw samples are in _raw[i] (length _rawLength[i]): its output and envelope allocated, Queued.</summary>
        bool Loaded(int i)
        {
            int n = RenderLength(_rawLength[i], _clipRate[i], _sr);
            _out[i] = new float[n];
            _level[i] = new float[LevelLength(n, MouthHop)];
            Volatile.Write(ref _state[i], Queued);
            return true;
        }

        /// <summary>Main thread: the clip's audio data is asked for (preloaded on import; again after an unload). With Load In
        /// Background it may still be loading when this returns.</summary>
        static void StartAudioData(AudioClip clip)
        {
            if (clip == null) return;
            if (clip.loadState == AudioDataLoadState.Unloaded || clip.loadState == AudioDataLoadState.Failed) clip.LoadAudioData();
        }

        /// <summary>The clip's samples as mono into a pooled buffer, _raw[i] (main thread). The decoded clip stays with
        /// Resources in the editor (other code reads it); in a player it is unloaded once copied, so lines that were heard do
        /// not keep their audio twice.</summary>
        bool ReadInto(int i, AudioClip clip)
        {
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0) return false;
            int frames = clip.samples, ch = Math.Max(1, clip.channels);
            float[] raw = TakeRaw(frames);
            bool ok;
            if (ch == 1) ok = clip.GetData(new Span<float>(raw, 0, frames), 0);
            else
            {
                if (_interleaved == null || _interleaved.Length < frames * ch) _interleaved = new float[frames * ch];
                ok = clip.GetData(new Span<float>(_interleaved, 0, frames * ch), 0);
                if (ok) for (int n = 0; n < frames; n++) raw[n] = _interleaved[n * ch];
            }
            // Read the rate before the unload: an unloaded clip is a destroyed object in a player (NullReference on the
            // headset, so no line was ever voiced; the editor keeps it, so the tests passed).
            int rate = clip.frequency;
            if (!Application.isEditor) Resources.UnloadAsset(clip);
            if (!ok) { GiveRaw(raw); return false; }
            _raw[i] = raw;
            _rawLength[i] = frames;
            _rawPooled[i] = true;
            _clipRate[i] = rate;
            return true;
        }

        float[] TakeRaw(int frames)
        {
            lock (_poolLock)
            {
                while (_rawPool.Count > 0)
                {
                    float[] b = _rawPool.Pop();
                    if (b.Length >= frames) return b;
                }
            }
            RawBuffersAllocated++;
            return new float[Math.Max(frames, RawBufferMin)];
        }

        void GiveRaw(float[] b)
        {
            if (b == null) return;
            lock (_poolLock) { if (_rawPool.Count < 4) _rawPool.Push(b); }
        }

        void Enqueue(int i)
        {
            lock (_queueLock)
            {
                if (_queueCount < _queue.Length) _queue[(_queueHead + _queueCount++) % _queue.Length] = i;
            }
            _signal?.Set();
        }

        bool _androidPriority;
        int _priorityLowered;
        /// <summary>The voice worker lowered its OS priority (Android only). Diagnostics.</summary>
        public bool PriorityLowered => Volatile.Read(ref _priorityLowered) > 0;

        void WorkerRun()
        {
            // Thread.Priority is a no-op under IL2CPP: on Android the worker lowers itself through JNI like the render workers.
            bool attached = _androidPriority && OfflineRenderQueue.LowerThisThread(ref _priorityLowered);
            try { WorkerLoop(); }
            finally
            {
                if (attached) { try { AndroidJNI.DetachCurrentThread(); } catch (Exception) { } }
            }
        }

        void WorkerLoop()
        {
            while (!_stop)
            {
                _signal.WaitOne(250);
                while (!_stop)
                {
                    int i;
                    lock (_queueLock)
                    {
                        if (_queueCount == 0) break;
                        i = _queue[_queueHead];
                        _queueHead = (_queueHead + 1) % _queue.Length;
                        _queueCount--;
                    }
                    try { Render(i, publish: true); }
                    catch (Exception) { Volatile.Write(ref _state[i], Missing); }
                }
            }
        }

        /// <summary>Queue one line's render on a boot render queue (the engine orders them: the intro lines early). Main
        /// thread: loads the clip and allocates the line's output and envelope here. Returns the bytes allocated.</summary>
        public long QueueRender(OfflineRenderQueue queue, int line)
        {
            if (_bank == null || (uint)line >= (uint)_count || Volatile.Read(ref _state[line]) != Idle) return 0;
            _asked[line] = RealNow;
            if (!LoadNow(line)) return 0;
            queue.Add(_slot0 + line, _ => Render(line, publish: false));
            return 4L * (_out[line].Length + _level[line].Length);
        }

        /// <summary>Queue every line (in table order; tools that want them all, past the resident limit). Returns the bytes.</summary>
        public long QueueRenders(OfflineRenderQueue queue, int slot0)
        {
            if (_bank == null) Attach(queue.Bank, slot0);
            long bytes = 0;
            for (int i = 0; i < _count; i++) bytes += QueueRender(queue, i);
            return bytes;
        }

        /// <summary>Any thread: resample, robotise and measure the mouth envelope into the preallocated buffers. Returns the
        /// samples (published to the bank when publish), or null when another thread claimed the line (it publishes it).</summary>
        float[] Render(int line, bool publish)
        {
            if (Interlocked.CompareExchange(ref _state[line], Rendering, Queued) != Queued) return null;
            float[] raw = _raw[line], y = _out[line];
            RenderLineInto(raw, _rawLength[line], _clipRate[line], _sr, y);
            RobotVoice.LevelEnvelopeInto(y, MouthWindow, MouthHop, _level[line]);
            _raw[line] = null;                       // the clip copy is no longer needed: its buffer goes back to the pool
            if (_rawPooled[line]) GiveRaw(raw);
            if (publish) _bank.Publish(_slot0 + line, y);
            Interlocked.Increment(ref _resident);
            Interlocked.Increment(ref _renders);
            Volatile.Write(ref _state[line], Done);  // publishes the level envelope to the main thread
            return y;
        }

        // ------------------------------------------------------------------ the frame (main thread)

        /// <summary>Main thread, every frame (AudioEngine.Update): finished clip loads go to the worker; the resident lines are
        /// trimmed to ResidentLimit. Allocation-free when nothing loads.</summary>
        public void Tick()
        {
            if (_loading > 0)
                for (int i = 0; i < _count; i++)
                {
                    ResourceRequest r = _loads[i];
                    AudioClip clip = _pendingClip[i];
                    if (clip == null)
                    {
                        if (r == null || !r.isDone) continue;
                        clip = r.asset as AudioClip;
                        // Load In Background: the asset is in, its audio data may still be decoding off the main thread.
                        if (clip != null && (clip.loadState == AudioDataLoadState.Unloaded || clip.loadState == AudioDataLoadState.Failed)) clip.LoadAudioData();
                    }
                    if (clip != null && clip.loadState == AudioDataLoadState.Loading) { _pendingClip[i] = clip; continue; }
                    _pendingClip[i] = null;
                    _loads[i] = null;
                    _loading--;
                    if (Volatile.Read(ref _state[i]) != Loading) continue;   // a Play loaded it meanwhile
                    Volatile.Write(ref _state[i], Idle);
                    if (!(ReadInto(i, clip) ? Loaded(i) : NoClip(i))) continue;
                    if (_worker != null) Enqueue(i);
                    else Render(i, publish: true);   // no worker (tools, tests): at once
                }
            if (_deferLine >= 0) StartDeferred();
            if (Volatile.Read(ref _resident) > ResidentLimit) Trim();
        }

        /// <summary>Evict least recently asked lines until the resident lines and the renders in flight (loading, queued,
        /// rendering) are at most ResidentLimit, so a render that lands never takes the count past it. Never the current
        /// line, a line asked in the last ProtectSeconds, or a render in flight.</summary>
        public void Trim()
        {
            double now = RealNow;
            int inFlight = 0;
            for (int i = 0; i < _count; i++)
            {
                int st = Volatile.Read(ref _state[i]);
                if (st == Loading || st == Queued || st == Rendering) inFlight++;
            }
            int target = Math.Max(0, ResidentLimit - inFlight);
            while (Volatile.Read(ref _resident) > target)
            {
                int victim = -1;
                for (int i = 0; i < _count; i++)
                {
                    if (i == _currentLine || Volatile.Read(ref _state[i]) != Done || !IsRendered(i)) continue;
                    if (now - _asked[i] < ProtectSeconds) continue;
                    if (victim < 0 || _asked[i] < _asked[victim]) victim = i;
                }
                if (victim < 0) return;
                Evict(victim);
            }
        }

        void Evict(int i)
        {
            _bank.Publish(_slot0 + i, null);   // a voice still playing it keeps its own reference
            _out[i] = null;
            _level[i] = null;
            Volatile.Write(ref _state[i], Idle);
            Interlocked.Decrement(ref _resident);
            Evictions++;
        }

        /// <summary>Tests and tools: count the lines whose recording exists (the clip and its length; its audio data is asked
        /// for too, and may still be loading in the background when this returns).</summary>
        public int VerifyClips()
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_clips != null) { if (_clips(_manifest.Id(i)).Valid) n++; continue; }
                AudioClip clip = Resources.Load<AudioClip>(ResourcesFolder + _manifest.Id(i));
                StartAudioData(clip);
                if (clip != null && clip.samples > 0) n++;
            }
            return n;
        }

        /// <summary>Samples in a rendered line: the clip resampled to the output rate plus TailSeconds.</summary>
        public static int RenderLength(int rawLength, int clipRate, int sr) =>
            Resampler.OutputLength(rawLength, clipRate, sr) + (int)Math.Ceiling(TailSeconds * sr);

        /// <summary>Entries in the mouth envelope of an n-sample line.</summary>
        public static int LevelLength(int n, int hop) => n / hop + 1;

        /// <summary>The robotised line at the output rate (pure; tests).</summary>
        public static float[] RenderLine(float[] raw, int clipRate, int sr) =>
            RenderLineInto(raw, clipRate, sr, new float[RenderLength(raw.Length, clipRate, sr)]);

        /// <summary>RenderLine into y (length RenderLength): resample into y, then the robot chain in place. Allocation-free.</summary>
        public static float[] RenderLineInto(float[] raw, int clipRate, int sr, float[] y) => RenderLineInto(raw, raw.Length, clipRate, sr, y);

        /// <summary>RenderLineInto of the first <paramref name="count"/> samples of raw (a pooled read buffer).</summary>
        public static float[] RenderLineInto(float[] raw, int count, int clipRate, int sr, float[] y)
        {
            Resampler.ResampleInto(raw, count, clipRate, sr, y);
            return RobotVoice.ProcessInto(y, sr, y);
        }

        /// <summary>Two square tones through lp 5000 (Q 1 dB). chime(): t = now + 0.01, then t + 0.065;
        /// endChirp(): t = now + 0.02, then t + 0.065. A tone starts at ceil(t * sr) of the offsets added in double
        /// (0.02 + 0.065 is frame 4081, not 4080), and each tone's first quantum follows SynthFlags.QuantumLead.</summary>
        public void Chirp(ChirpKind kind, double when)
        {
            var s = _kit.S(SourceId.Guide);
            if (kind == ChirpKind.Poke)
            {
                // First five (sunny-tour.md 4.4): one short, soft falling tone, quieter than the open chirp.
                ChirpTone(s, when, 0.005, 740, 520, 0.05f, 0.012f, 0.08f);
            }
            else if (kind == ChirpKind.Open)
            {
                const double t = 0.01;
                ChirpTone(s, when, t, 880, 1320, 0.04f, 0.022f, 0.06f);
                ChirpTone(s, when, t + 0.065, 1320, 700, 0.05f, 0.018f, 0.07f);
            }
            else
            {
                const double t = 0.02;
                ChirpTone(s, when, t, 1100, 560, 0.05f, 0.018f, 0.06f);
                ChirpTone(s, when, t + 0.065, 700, 420, 0.05f, 0.015f, 0.07f);
            }
            _kit.Flush();
        }

        /// <summary>The start frame of a source scheduled `seconds` after a quantum-aligned now (ceil to the next frame).</summary>
        public static long QuantumStartFrame(double seconds, int sr) => (long)Math.Ceiling(seconds * sr);

        /// <summary>A tone `offset` seconds (offsets added in double) after `when`: the voice starts at the event's
        /// frame (envelope, glide and stop count from it), the oscillator at the ceil of it (one frame later when the
        /// event falls a hair past a frame), with the first-quantum frequency read (both measured: -111 / -118 dB).</summary>
        private void ChirpTone(SynthSource s, double when, double offset, float f0, float f1, float slide, float peak, float decay)
        {
            double exact = offset * _sr;
            long frame = (long)Math.Floor(exact);
            long oscStart = QuantumStartFrame(offset, _sr);
            int lead = (int)(oscStart % ToneVoice.Quantum);
            byte flags = 0;
            if (lead != 0) flags |= SynthFlags.QuantumLead;
            if (oscStart > frame) flags |= SynthFlags.OscLateStart;
            _kit.Stage(s, new SynthCommand
            {
                Op = SynthOp.StartTone, When = SynthKit.Offset(when, frame / (double)_sr), Wave = Wave.Square,
                F0 = f0, F1 = f1, Slide = slide, Peak = peak, Decay = decay, Lp = 5000, Level = 1, Flags = flags, Buffer = lead,
            });
        }

        /// <summary>Plays the processed line; returns the dspTime it ends (0 when the line is missing). Cuts a running line
        /// first. A line that is not rendered yet is rendered now, on this thread (a worker already on it: the BufferVoice
        /// waits for the slot, up to 300 ms).</summary>
        public double Play(string id, double when)
        {
            int i = IndexOf(id);
            if (i < 0 || _bank == null || Volatile.Read(ref _state[i]) == Missing) return 0;
            _asked[i] = RealNow;
            if (!IsRendered(i))
            {
                int st = Volatile.Read(ref _state[i]);
                if ((st == Idle || st == Loading) && !LoadNow(i))
                {
                    if (Volatile.Read(ref _state[i]) != Loading) return 0;
                    // Its clip is still loading in the background: the line starts as soon as its render is in (Tick), and
                    // its timing (the end returned, Playing) counts from now, as for a line that plays at once.
                    Stop();
                    _deferLine = i;
                    _deferWhen = when;
                    _currentLine = i;
                    double now0 = _now();
                    _currentStart = when > 0 ? Math.Max(when, now0) : now0;
                    _currentEnd = _currentStart + Math.Max(0f, _manifest.Seconds(i));
                    DeferredStarts++;
                    return _currentEnd;
                }
                Trim();
                float[] y = Render(i, publish: true);
                if (y != null) SyncRenders++;
            }
            Stop();
            StartVoice(i, when);
            return _currentEnd;
        }

        int _deferLine = -1;
        double _deferWhen;
        /// <summary>Lines asked to play while their clip was still loading (they started once rendered).</summary>
        public int DeferredStarts { get; private set; }

        /// <summary>Main thread (Tick): a line asked to play while it loaded starts now that it is rendered, unless it was
        /// stopped or its time is over meanwhile.</summary>
        void StartDeferred()
        {
            int i = _deferLine;
            if (i < 0 || !IsRendered(i)) return;
            _deferLine = -1;
            if (_currentLine != i || _now() >= _currentEnd) return;
            double start = _currentStart, end = _currentEnd;
            StartVoice(i, _deferWhen);
            _currentStart = start; _currentEnd = end;   // the timing the caller was given
        }

        void StartVoice(int i, double when)
        {
            var s = _kit.S(SourceId.Guide);
            int vid = _kit.NextId();
            _kit.Stage(s, new SynthCommand
            {
                Op = SynthOp.StartBuffer, VoiceId = vid, When = when, Buffer = _slot0 + i,
                Rate = 1f, Level = 1f, Lp = 0f, Q = 0f, Res = 0f,
            });
            _kit.Flush();
            _current = new VoiceHandle(s, vid);
            _currentLine = i;
            double now = _now();
            _currentStart = when > 0 ? Math.Max(when, now) : now;
            _currentEnd = _currentStart + Math.Max(0f, _manifest.Seconds(i));
        }

        /// <summary>Stop: setTarget(0, now, .004), stop +0.03.</summary>
        public void Stop()
        {
            if (_deferLine >= 0) { _deferLine = -1; _currentLine = -1; _currentEnd = 0; }
            if (!_current.IsValid) return;
            _kit.Release(_current.Source, _current.Id, 0.004f, 0.03f);
            _kit.Flush();
            _current = VoiceHandle.None;
            _currentLine = -1;
            _currentEnd = 0;
        }

        /// <summary>Mouth level now: RMS of the last 1024 output samples of the line (0 before it starts, after it ends
        /// or once cut), from the envelope precomputed every 256 frames.</summary>
        public float Level
        {
            get
            {
                if (_currentLine < 0) return 0f;
                if (Volatile.Read(ref _state[_currentLine]) != Done) return 0f;   // envelope still being written
                var env = _level[_currentLine];
                if (env == null) return 0f;
                double t = _now() - _currentStart;
                if (t < 0 || t >= _manifest.Seconds(_currentLine)) return 0f;
                int k = (int)(t * _sr / MouthHop);
                return k < env.Length ? env[k] : 0f;
            }
        }
    }

    /// <summary>The robot chain (spec 06 section 8), applied offline at the rate it runs live (the output rate).</summary>
    public static class RobotVoice
    {
        public const double Dry = 0.92, Ring = 0.08, RingHz = 80, CombSeconds = 0.005, Feedback = 0.3, Comb = 0.12;
        public const double HighPassHz = 120, HighPassQ = 0.7, BandHz = 2500, BandQ = 0.8, BandDb = 2;

        [ThreadStatic] private static double[] s_comb; // 5 ms of comb state per worker, reused

        public static float[] Process(float[] x, int sr) => ProcessInto(x, sr, new float[x.Length]);

        /// <summary>Processes x into y (same length; y may be x: each sample is read before it is written) and returns y.</summary>
        public static float[] ProcessInto(float[] x, int sr, float[] y)
        {
            int n = x.Length;
            if (y == null || y.Length != n) throw new ArgumentException("destination must match the input length", nameof(y));
            int combLen = Math.Max(1, (int)Math.Round(CombSeconds * sr)); // 5 ms (240 frames at 48 k)
            if (s_comb == null || s_comb.Length < combLen) s_comb = new double[combLen];
            var comb = s_comb;
            Array.Clear(comb, 0, combLen);
            int ci = 0;
            var hp = Biquad.Create(BiquadType.Highpass, HighPassHz, HighPassQ, 0, sr);
            var pk = Biquad.Create(BiquadType.Peaking, BandHz, BandQ, BandDb, sr);
            double w = 2 * Math.PI * RingHz / sr;
            for (int i = 0; i < n; i++)
            {
                double s = Dry * x[i] + Ring * x[i] * Math.Sin(w * i);    // ring modulator: a gain at 0 driven by the sine, phase 0
                double d = comb[ci];                                      // d[n] = s[n-240] + 0.3 d[n-240]
                comb[ci] = s + Feedback * d;
                ci = ci + 1 == combLen ? 0 : ci + 1;
                y[i] = pk.Process(hp.Process((float)(s + Comb * d)));
            }
            return y;
        }

        /// <summary>The mouth input: at frame k * hop, the RMS of the window [k hop - window, k hop) (zeros before the
        /// line), as a level analyser of `window` samples returns it.</summary>
        public static float[] LevelEnvelope(float[] y, int window, int hop) =>
            LevelEnvelopeInto(y, window, hop, new float[GuideVoice.LevelLength(y.Length, hop)]);

        /// <summary>LevelEnvelope into env (length y.Length / hop + 1). Allocation-free.</summary>
        public static float[] LevelEnvelopeInto(float[] y, int window, int hop, float[] env)
        {
            int count = GuideVoice.LevelLength(y.Length, hop);
            if (env == null || env.Length != count) throw new ArgumentException("envelope length must be y.Length / hop + 1", nameof(env));
            double sum = 0;
            int added = 0, removed = 0;
            for (int k = 0; k < count; k++)
            {
                int end = Math.Min(y.Length, k * hop), start = Math.Max(0, k * hop - window);
                for (; added < end; added++) sum += (double)y[added] * y[added];
                for (; removed < start; removed++) sum -= (double)y[removed] * y[removed];
                env[k] = (float)Math.Sqrt(Math.Max(0, sum) / window);
            }
            return env;
        }
    }

    /// <summary>Band-limited resampling for the voice lines (each line is resampled to the output rate with
    /// a windowed-sinc resampler): Kaiser-windowed sinc, 32 taps at the input rate, cutoff 0.9 of the lower Nyquist,
    /// kernel tabulated at 64 sub-sample steps with linear interpolation. Pure and thread-safe.</summary>
    public static class Resampler
    {
        public const int HalfTaps = 16, Steps = 64;
        public const double Cutoff = 0.9, Beta = 7.0;
        private static readonly float[] Kernel = BuildKernel();

        private static float[] BuildKernel()
        {
            int n = HalfTaps * Steps;
            var k = new float[n + 2];
            double i0b = BesselI0(Beta);
            for (int i = 0; i <= n + 1; i++)
            {
                double u = (double)i / Steps;            // distance in input samples
                if (u >= HalfTaps) { k[i] = 0; continue; }
                double x = Math.PI * Cutoff * u;
                double sinc = u == 0 ? 1 : Math.Sin(x) / x;
                double r = u / HalfTaps;
                k[i] = (float)(Cutoff * sinc * BesselI0(Beta * Math.Sqrt(1 - r * r)) / i0b);
            }
            return k;
        }

        private static double BesselI0(double x)
        {
            double sum = 1, term = 1, q = x * x / 4;
            for (int k = 1; k < 50; k++) { term *= q / (k * k); sum += term; if (term < 1e-12 * sum) break; }
            return sum;
        }

        private static float K(double u)
        {
            u = Math.Abs(u) * Steps;
            int i = (int)u;
            if (i >= HalfTaps * Steps) return 0f;
            float f = (float)(u - i);
            return Kernel[i] + f * (Kernel[i + 1] - Kernel[i]);
        }

        /// <summary>Resampled length of an n-sample input (without a tail).</summary>
        public static int OutputLength(int n, int inRate, int outRate) =>
            inRate == outRate ? n : (int)Math.Ceiling((double)n * outRate / inRate);

        /// <summary>x at inRate -> outRate, plus `tail` zero samples. Same rate: a copy (plus the tail).</summary>
        public static float[] Resample(float[] x, int inRate, int outRate, int tail = 0) =>
            ResampleInto(x, inRate, outRate, new float[OutputLength(x.Length, inRate, outRate) + tail]);

        /// <summary>Resample into y (length >= OutputLength; the rest is zeroed as the tail) and return y. Allocation-free.</summary>
        public static float[] ResampleInto(float[] x, int inRate, int outRate, float[] y) => ResampleInto(x, x.Length, inRate, outRate, y);

        /// <summary>ResampleInto of the first <paramref name="count"/> samples of x.</summary>
        public static float[] ResampleInto(float[] x, int count, int inRate, int outRate, float[] y)
        {
            int n = OutputLength(count, inRate, outRate);
            if (y == null || y.Length < n) throw new ArgumentException("destination is shorter than the resampled input", nameof(y));
            Array.Clear(y, n, y.Length - n);
            if (inRate == outRate)
            {
                Array.Copy(x, y, n);
                return y;
            }
            double step = (double)inRate / outRate;
            // Downsampling: the kernel must cut at the output's Nyquist, so it is stretched (and scaled) by the ratio.
            double stretch = step > 1 ? 1 / step : 1;
            for (int m = 0; m < n; m++)
            {
                double t = m * step;
                int c = (int)Math.Floor(t);
                double frac = t - c, acc = 0;
                int span = (int)Math.Ceiling(HalfTaps / stretch);
                for (int j = -span + 1; j <= span; j++)
                {
                    int i = c + j;
                    if ((uint)i >= (uint)count) continue;
                    acc += x[i] * K((j - frac) * stretch);
                }
                y[m] = (float)(acc * stretch);
            }
            return y;
        }
    }

    /// <summary>Non-spatial UI sounds on the Ui source: the page-turn swish (spec 06 section 9), nulled against
    /// Tests/EditMode/Audio/Reference~/page_turn_swish.wav. The reference's reverb sends (.25, .2) have no 2D equivalent.</summary>
    public sealed class UiSounds
    {
        readonly SynthKit _kit;
        public UiSounds(SynthKit kit) { _kit = kit; }

        public void Swish(double when)
        {
            var s = _kit.S(SourceId.Ui);
            _kit.Noise(s, when, bp: 1400, q: 0.7f, lp: 7000, peak: 0.09f, decay: 0.32f);
            _kit.Noise(s, SynthKit.Offset(when, 0.04), hp: 2500, lp: 9000, peak: 0.035f, decay: 0.22f);
            _kit.Flush();
        }

        /// <summary>The looper's metronome (Lessons' Looper): a soft wooden "tock", a short sine body with a faint noise
        /// tick on top, quiet next to the instruments; the bar's first beat a fifth higher and a little louder.</summary>
        public void Click(bool accent, double when)
        {
            var s = _kit.S(SourceId.Ui);
            float f = accent ? ClickAccentHz : ClickHz;
            _kit.Tone(s, when, Wave.Sine, f, f * 0.94f, 0.02f, accent ? 0.05f : 0.034f, 0.045f, 6000);
            _kit.Noise(s, when, bp: f * 2.2f, q: 1.4f, lp: 9000, peak: accent ? 0.022f : 0.015f, decay: 0.007f);
            _kit.Flush();
        }

        public const float ClickHz = 1320f, ClickAccentHz = 1980f;
    }
}
