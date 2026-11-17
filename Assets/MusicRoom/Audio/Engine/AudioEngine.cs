// The IAudioEngine facade: owns the sources, the sample bank and the render queue, and delegates the recipes to the
// synth classes in Audio/Synths (each nulled against the 06 reference renders). Nothing here touches audio-thread state.
// The piano, the drums, the harp and the room always play the classic sound (today's synths, Meta's room acoustics and
// MasterBus's chain): the user's choice after the headset test of 2026-11-10 ("classic sound original sound best"). The
// Burst graph (AudioGraphHost) carries the unit-only roles (handpan, hand drums, marimba, bowls), created
// through SoundUnits.LiveRoles. The upgraded piano / drums / harp / room units are never created here.
using System;
using System.Diagnostics;
using System.Threading;
using MusicRoom.Audio.Burst;
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Offline;
using MusicRoom.Audio.Synths;
using MusicRoom.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Audio
{
    [DefaultExecutionOrder(-1000)]
    public sealed class AudioEngine : MonoBehaviour, IAudioEngine
    {
        public const int PianoSlot0 = 0;    // MIDI 21..108 -> 0..87
        public const int HarpSlot0 = 100;   // harp strings by index (22)
        public const int VoiceSlot0 = 200;  // Sunny's lines by GuideVoice index (the merged manifests: 38 of the Guide's + the modules')
        /// <summary>The bank's slots at least (platform.md 8.2: voice slots 200..511 hold 312 lines); a larger merged line
        /// table grows the bank to VoiceSlot0 + its line count (BankSlots).</summary>
        public const int SlotCount = 512;
        /// <summary>The bank's slot count for a voice of `lines` lines.</summary>
        public static int BankSlots(int lines) => Math.Max(SlotCount, VoiceSlot0 + lines);
        /// <summary>Piano keys rendered before Sunny's intro lines: C4 +- this many semitones (the first key must be ready
        /// within 0.15 s, section 17; the intro is asked for only after the opening chirp plus the 0.14 s lead).</summary>
        public const int PianoUrgentSpan = 2;
        /// <summary>Piano keys rendered before the voice lines other than the intro: C4 +- this many semitones.</summary>
        public const int PianoFirstSpan = 12;
        /// <summary>Middle C, the first key rendered (the bank renders outward from it, spec 06 section 3).</summary>
        public const int FirstKeyMidi = 60;

        public SynthSource[] Sources { get; private set; }
        public SampleBank Bank { get; private set; }
        public OfflineRenderQueue Renders { get; private set; }
        public SynthKit Kit { get; private set; }
        public PianoSynth PianoSynth { get; private set; }
        public DrumSynth DrumSynth { get; private set; }
        public StringSynth StringSynth { get; private set; }
        public Ambience Ambience { get; private set; }
        public GuideVoice GuideVoice { get; private set; }
        public UiSounds Ui { get; private set; }
        public DrumRoomReverb DrumRoom { get; private set; }
        /// <summary>The Burst graph (null when UseGraph is off or before Init).</summary>
        public AudioGraphHost Graph { get; private set; }
        public int renderWorkers = 2;
        /// <summary>Create the Burst graph and the unit-only roles' sound units (off: today's engine only).</summary>
        public static bool UseGraph = true;
        /// <summary>The session seed every random unit derives from (one uniform stream per session).</summary>
        public uint Seed { get; private set; }
        /// <summary>Seconds from Init until every offline render was done (-1 while rendering).</summary>
        public double RenderSeconds { get; private set; } = -1;
        /// <summary>Seconds from the start of Init until middle C could first be heard (-1 before; section 17 budget 0.15 s):
        /// when its buffer rendered.</summary>
        public double FirstKeySeconds => Volatile.Read(ref _firstKeySeconds);

        /// <summary>Dev device builds (set by AudioModule): one "[Audio] latency-probe" line per live (ASAP) piano / drum /
        /// pluck call, logged from Update, with the in-frame wait (dspTime at the call - FrameNow), the frame time and the
        /// output ring, so the in-app part of fingertip-to-ear can be separated from the HAL part (filmed-tap test).</summary>
        public static bool LatencyProbe;

        public bool IsReady { get; private set; }
        public int SampleRate { get; private set; }
        /// <summary>One DSP block in seconds (the mixer buffer read once in Init / SampleRate).</summary>
        public double BlockSeconds { get; private set; }
        /// <summary>The main-thread audio clock: AudioSettings.dspTime, or BootOptions.AudioClock in tests (the mixer itself
        /// always runs on dspTime, so absolute When values from a scripted clock play at once or late).</summary>
        public double Now => _clock();
        public double FrameNow { get; private set; }
        public float RenderProgress => Renders != null ? Renders.Progress : 0f;
        public bool FireOn => Ambience != null && Ambience.FireOn;
        public bool WindOn => Ambience != null && Ambience.WindOn;
        public bool SnowOn => Ambience != null && Ambience.SnowOn;
        public bool MixedReality { get; private set; }
        public AudioTestTap TestTap { get => Kit?.Tap; set { if (Kit != null) Kit.Tap = value; } }
        public bool VoiceLinePlaying => GuideVoice != null && GuideVoice.Playing;
        public float VoiceLevel => GuideVoice != null ? GuideVoice.Level : 0f;

        public int AddOneShotPreset(in OneShotPreset preset) => Graph != null ? Graph.AddOneShotPreset(preset) : -1;
        public bool PlayOneShot(int preset, float gain, SourceId source, int voiceId = 0, double when = 0) =>
            Graph != null && preset >= 0 && Graph.PlayOneShot(preset, gain, source, voiceId, AudioSchedule.Stamp(when));
        public bool ReleaseOneShot(int voiceId, float tau, double when = 0) => Graph != null && Graph.ReleaseOneShot(voiceId, tau, when);

        /// <summary>The role's sound unit (null when the role has none: the piano, drums, harp and room never do).</summary>
        public SoundUnit Unit(UnitId role) => role < UnitId.Count ? _units[(int)role] : null;
        /// <summary>The role's unit (the unit-only roles; null for the classic three and the room, which play today's synths).</summary>
        public SoundUnit ActiveUnit(UnitId role) => Unit(role);

        private MetaXRAudioRoomAcousticProperties _room;
        private int _roomFramesLeft;
        private Func<double> _clock = () => AudioSettings.dspTime;
        private readonly Stopwatch _renderClock = new Stopwatch();
        private long _initTicks;
        private double _firstKeySeconds = -1;   // written once by a render worker
        private MetaXRAudioSource[] _meta;   // per source, null for the 2D Ui source (cached: no GetComponent later)
        private readonly SoundUnit[] _units = new SoundUnit[(int)UnitId.Count];

        private struct ProbeSample { public AudioCall Call; public int Index; public double InFrameMs; public float FrameMs; }
        private readonly ProbeSample[] _probe = new ProbeSample[64];
        private int _probeCount;

        /// <summary>Total bytes of the note / line buffers allocated up front for the render workers (diagnostics).</summary>
        public long PreallocatedBytes { get; private set; }

        // ---------------- the output's configuration, pauses (main thread) ----------------

        /// <summary>Raised after the output's configuration changed (AudioSettings.Reset, a device change) and SampleRate /
        /// BlockSeconds were read again: AudioModule re-syncs the master bus and the room.</summary>
        public event Action ConfigurationChanged;
        /// <summary>Raised when the audio clock jumped (seconds, new minus old: negative when the output restarted from 0):
        /// the looper re-bases its schedule and lets go of what it held.</summary>
        public event Action<double> ClockJumped;
        /// <summary>Output configuration changes seen, and times the rate had to be forced back to 48 kHz (diagnostics).</summary>
        public int ConfigurationChanges { get; private set; }
        public int RateFixes { get; private set; }
        /// <summary>AudioListener.pause is on (input focus lost or the headset off).</summary>
        public bool Paused { get; private set; }
        /// <summary>Carriers started again by ResumeCarriers since Init (diagnostics, tests).</summary>
        public int CarriersRestarted { get; private set; }
        /// <summary>A clock step at least this big counts as a jump.</summary>
        public const double ClockJumpSeconds = 0.25;
        private bool _fixRatePending;

        /// <summary>The output restarted (Reset, a device change). Another rate than 48 kHz is forced back on the next
        /// frame (a Reset from inside this callback would re-enter it); the rate and block are read again, the master bus
        /// and the room re-synced, and a clock jump reported.</summary>
        private void OnConfigurationChanged(bool deviceWasChanged)
        {
            ConfigurationChanges++;
            double before = FrameNow;
            int sr = AudioSettings.outputSampleRate;
            if (!Application.isEditor && sr != AudioModule.DeviceSampleRate && RateFixes < 3) _fixRatePending = true;
            if (sr != SampleRate) Debug.LogWarning($"[Audio] the output changed rate {SampleRate} -> {sr} Hz{(deviceWasChanged ? " (a new device)" : "")}");
            if (sr > 0) SampleRate = sr;
            AudioSettings.GetDSPBufferSize(out int blockFrames, out _);
            BlockSeconds = SampleRate > 0 ? blockFrames / (double)SampleRate : 0.0;
            if (_room != null) { _room.enabled = true; _roomFramesLeft = 2; }   // push the room box again
            ConfigurationChanged?.Invoke();
            double now = _clock();
            FrameNow = now;
            if (Math.Abs(now - before) >= ClockJumpSeconds)
            {
                Debug.Log($"[Audio] the audio clock jumped {before:F3} -> {now:F3} s");
                ClockJumped?.Invoke(now - before);
            }
            ResumeCarriers();
        }

        private void FixRate()
        {
            _fixRatePending = false;
            var c = AudioSettings.GetConfiguration();
            if (c.sampleRate == AudioModule.DeviceSampleRate) return;
            RateFixes++;
            int was = c.sampleRate;
            c.sampleRate = AudioModule.DeviceSampleRate;
            bool ok = AudioSettings.Reset(c);
            int now = AudioSettings.outputSampleRate;
            if (ok && now == AudioModule.DeviceSampleRate) Debug.Log($"[Audio] the output was at {was} Hz: back to {now} Hz");
            else Debug.LogError($"[Audio] the output runs at {now} Hz and would not go back to {AudioModule.DeviceSampleRate} Hz: the 48 kHz instruments play out of tune");
        }

        /// <summary>Input focus lost or the headset off: AudioListener.pause stops everything at once; back: it resumes and
        /// every carrier plays again (ResumeCarriers), so the app never comes back silent.</summary>
        public void SetPaused(bool paused)
        {
            if (paused == Paused) return;
            Paused = paused;
            AudioListener.pause = paused;
            Debug.Log($"[Audio] {(paused ? "paused (input focus lost or the headset off)" : "resumed")}");
            if (!paused) ResumeCarriers();
        }

        /// <summary>Main thread: every synth source's carrier that should play and does not (after a real app pause, or the
        /// output restarting) is started again. Returns how many were.</summary>
        public int ResumeCarriers()
        {
            int n = 0;
            if (Sources != null) for (int i = 0; i < Sources.Length; i++) if (Sources[i] != null && Sources[i].EnsurePlaying()) n++;
            CarriersRestarted += n;
            if (n > 0) Debug.Log($"[Audio] {n} synth carrier(s) were stopped: playing again");
            return n;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused && IsReady) ResumeCarriers();
        }

        private void Update()
        {
            if (_fixRatePending) FixRate();
            FrameNow = _clock();
            if (Graph != null)
            {
                Graph.Tick(Time.unscaledDeltaTime);
                for (int i = 0; i < _units.Length; i++) _units[i]?.Tick();
            }
            GuideVoice?.Tick();
            if (_room != null && _roomFramesLeft > 0 && --_roomFramesLeft == 0) _room.enabled = false; // 6 allocations per frame otherwise (spec 04 section 3.4)
            if (_probeCount > 0) FlushProbe();
            if (RenderSeconds < 0 && Renders != null && Renders.IsDone)
            {
                RenderSeconds = _renderClock.Elapsed.TotalSeconds;
                _renderClock.Stop();
            }
        }

        /// <summary>Main thread, once, after AudioRig.Build (the sources' Awake has run). clock: the main-thread audio clock
        /// (null = AudioSettings.dspTime; tests pass BootOptions.AudioClock). settings: the app's settings (null in bare
        /// tests); a save that still has the old "Classic sound" switch loses it here, once.</summary>
        public void Init(SynthSource[] sources, uint seed = 0, Func<double> clock = null, AppSettings settings = null)
        {
            AudioSettings.OnAudioConfigurationChanged -= OnConfigurationChanged;
            AudioSettings.OnAudioConfigurationChanged += OnConfigurationChanged;
            if (settings != null && settings.Store.HasKey(AppSettings.KeyClassicSoundOld))
            {
                settings.Store.DeleteKey(AppSettings.KeyClassicSoundOld);
                settings.Save();
            }
            _initTicks = Stopwatch.GetTimestamp();
            _renderClock.Restart();
            if (clock != null) _clock = clock;
            FrameNow = _clock();
            Sources = sources;
            _meta = new MetaXRAudioSource[sources.Length];
            for (int i = 0; i < sources.Length; i++) if (sources[i] != null) sources[i].TryGetComponent(out _meta[i]);
            SampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int blockFrames, out _);
            BlockSeconds = SampleRate > 0 ? blockFrames / (double)SampleRate : 0.0;
            int sr = SampleRate;
            if (seed == 0) seed = Mulberry32.SessionSeed();
            Seed = seed;
            NoiseTable.Build(sr, seed);
            Kit = new SynthKit(sources, seed);
            GuideVoice = new GuideVoice(Kit, sr, _clock);
            Bank = new SampleBank(BankSlots(GuideVoice.Lines));
            foreach (var s in sources) if (s != null) s.Bank = Bank;

            PianoSynth = new PianoSynth(Kit);
            DrumSynth = new DrumSynth(Kit);
            StringSynth = new StringSynth(Kit);
            Ambience = new Ambience(Kit, sr, seed ^ 0x2545F491u);
            GuideVoice.Attach(Bank, VoiceSlot0);
            GuideVoice.StartWorker();
            Ui = new UiSounds(Kit);
            DrumRoom = new DrumRoomReverb(sr, seed ^ 0x5bd1e995u);
            Kit.S(SourceId.DrumRoom).AddProcessor(DrumRoom);

            // The Burst graph and the unit-only roles' sound units (the piano, drums, harp and room have none: they play
            // today's synths). The graph goes live on the sources last.
            if (UseGraph && sr > 0)
            {
                Graph = new AudioGraphHost(GraphConfig.QuestDefault(sr));
                var ctx = new SoundContext(Graph, sources, sr, seed, _clock, false) { TapSource = () => Kit?.Tap };
                foreach (var role in SoundUnits.LiveRoles)
                {
                    SoundUnit u = null;
                    try { u = SoundUnits.Create(role, ctx); }
                    catch (Exception e) { Debug.LogError($"[Audio] sound unit {role} failed to start ({e.Message}): it stays silent"); }
                    if (u != null) _units[(int)role] = u;
                }
            }

            // Render order. First the oscillator tables and the metal sums (tone and metal voices switch to the exact
            // periodic-table path when ready) and middle C with its nearest neighbours (the first key must be ready in 0.15 s),
            // then Sunny's two intro lines (the first-launch tour starts with one, after the opening chirp and the 0.14 s
            // lead), then the rest of the piano outward from middle C (the reference loop stops at d = 44 and never
            // precomputes MIDI 105..108; we use d <= 48), and the harp last (the reference renders it 1.5 s after start).
            // Every other voice line renders lazily when it is first asked for (GuideVoice: the voice worker, at most 24
            // resident), so the line table can grow with the instruments.
            // Every output buffer is allocated HERE, on the main thread at start-up (one burst before the intro); the
            // workers fill them in place and reuse per-thread scratch, so they create no large garbage while audio plays.
            Renders = new OfflineRenderQueue(Bank, 1 + 88 + HarpRenderer.Strings.Length + GuideVoice.BootLines.Length);
            Renders.Add(-1, _ => { PeriodicWaveTables.Build(); MetalBank.Build(sr); return null; });
            long bytes = 0;
            uint session = seed;
            for (int d = 0; d <= 48; d++)
            {
                if (FirstKeyMidi + d <= 108) bytes += QueuePiano(Renders, FirstKeyMidi + d, sr, session);
                if (d > 0 && FirstKeyMidi - d >= 21) bytes += QueuePiano(Renders, FirstKeyMidi - d, sr, session);
                if (d == PianoUrgentSpan)
                    foreach (string line in GuideVoice.BootLines) bytes += GuideVoice.QueueRender(Renders, GuideVoice.IndexOf(line));
            }
            bytes += QueueHarp(Renders, sr, session);
            PreallocatedBytes = bytes;
            Renders.Start(renderWorkers);

            if (Graph != null)
            {
                foreach (var s in sources) if (s != null) s.Graph = Graph;
                GraphBridge.Host = Graph;
            }
            IsReady = true;
        }

        private long QueueHarp(OfflineRenderQueue queue, int sr, uint session)
        {
            long bytes = 0;
            for (int i = 0; i < HarpRenderer.Strings.Length; i++)
            {
                int midi = HarpRenderer.Strings[i];
                var dst = new float[HarpRenderer.Length(midi, sr)];
                bytes += 4L * dst.Length;
                uint hs = (uint)(2000 + midi) ^ session;
                queue.Add(HarpSlot0 + i, _ => HarpRenderer.RenderOldInto(midi, sr, hs, dst));
            }
            return bytes;
        }

        private long QueuePiano(OfflineRenderQueue queue, int midi, int sr, uint session)
        {
            var dst = new float[PianoRenderer.Length(midi, sr)];
            uint ps = (uint)(1000 + midi) ^ session;
            if (midi == FirstKeyMidi)
            {
                long t0 = _initTicks;
                queue.Add(PianoSlot0 + midi - 21, _ =>
                {
                    var y = PianoRenderer.RenderInto(midi, sr, ps, dst);
                    // The bank publishes y right after this returns: the gap is a few instructions.
                    Volatile.Write(ref _firstKeySeconds, (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency);
                    return y;
                });
            }
            else queue.Add(PianoSlot0 + midi - 21, _ => PianoRenderer.RenderInto(midi, sr, ps, dst));
            return 4L * dst.Length;
        }

        /// <summary>Main thread, at shutdown: take the graph off the sources and the listener, then free it and the units.</summary>
        public void ShutdownGraph()
        {
            GuideVoice?.Dispose();
            var g = Graph;
            if (g == null) return;
            Graph = null;
            if (Sources != null) foreach (var s in Sources) if (s != null) s.Graph = null;
            if (GraphBridge.Host == g) GraphBridge.Host = null;
            for (int i = 0; i < _units.Length; i++)
            {
                try { _units[i]?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
                _units[i] = null;
            }
            g.Dispose();
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnConfigurationChanged;
            ShutdownGraph();
        }

        // ---------------- test tap + latency probe (main thread, allocation-free unless a tap is set) ----------------

        private void Record(in AudioTestTap.Call c) { var tap = Kit?.Tap; if (tap != null) tap.Calls.Add(c); }

        private void Probe(AudioCall call, int index, double when)
        {
            if (!LatencyProbe || when != 0 || _probeCount >= _probe.Length) return;
            _probe[_probeCount++] = new ProbeSample
            {
                Call = call, Index = index, InFrameMs = (AudioSettings.dspTime - FrameNow) * 1000.0, FrameMs = Time.unscaledDeltaTime * 1000f,
            };
        }

        private void FlushProbe()
        {
            AudioSettings.GetDSPBufferSize(out int frames, out int buffers);
            double blockMs = SampleRate > 0 ? frames * 1000.0 / SampleRate : 0;
            for (int i = 0; i < _probeCount; i++)
            {
                var p = _probe[i];
                Debug.Log($"[Audio] latency-probe {p.Call} {p.Index}: in-frame {p.InFrameMs:F2} ms, frame {p.FrameMs:F1} ms, next block <= {blockMs:F2} ms, ring {frames}x{buffers} = {blockMs * buffers:F1} ms");
            }
            _probeCount = 0;
        }

        public void AttachRoom(MetaXRAudioRoomAcousticProperties room)
        {
            _room = room;
            _roomFramesLeft = 2;
        }

        // ---------------- IAudioEngine ----------------
        public VoiceHandle Piano(int midi, float vel, double when = 0)
        {
            when = AudioSchedule.Stamp(when);   // inside a ScheduleAt scope (the looper): its time, not ASAP
            Probe(AudioCall.Piano, midi, when);
            var h = PianoSynth.Play(midi, vel, when);
            Record(new AudioTestTap.Call { Method = AudioCall.Piano, Index = midi, Vel = vel, When = when, Handle = h });
            return h;
        }

        public void Release(VoiceHandle h, bool thud = true, double when = 0)
        {
            when = AudioSchedule.Stamp(when);
            Record(new AudioTestTap.Call { Method = AudioCall.Release, Flag = thud, When = when, Handle = h });
            PianoSynth.Release(h, thud, when);
        }

        public VoiceHandle Pluck(int stringIndex, float vel, double when = 0, PluckOptions? options = null)
        {
            var o = options ?? PluckOptions.Default;
            when = AudioSchedule.Stamp(when);
            Probe(AudioCall.Pluck, stringIndex, when);
            var h = StringSynth.Pluck(stringIndex, vel, when, o);
            Record(new AudioTestTap.Call { Method = AudioCall.Pluck, Index = stringIndex, Vel = vel, When = when, Pluck = o, Handle = h });
            return h;
        }

        public void Damp(VoiceHandle h, int hands = 1, double when = 0)
        {
            when = AudioSchedule.Stamp(when);
            Record(new AudioTestTap.Call { Method = AudioCall.Damp, Index = -1, Hands = hands, When = when, Handle = h });
            StringSynth.Damp(h, hands, when);
        }

        public double HarpRingSeconds(int midi) => HarpRenderer.BaseSeconds(midi);

        public void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0)
        {
            when = AudioSchedule.Stamp(when);
            Probe(AudioCall.Drum, (int)kind, when);
            Record(new AudioTestTap.Call { Method = AudioCall.Drum, Index = (int)kind, Drum = kind, Vel = vel, Strike = strike, When = when });
            DrumSynth.Hit(kind, vel, strike, when);
        }

        public void Choke(DrumKind kind)
        {
            // No `when` of its own: inside a ScheduleAt scope both paths stamp its commands at the scope's time.
            Record(new AudioTestTap.Call { Method = AudioCall.Choke, Index = (int)kind, Drum = kind, When = AudioSchedule.At });
            DrumSynth.Choke(kind);
        }

        public void SetFire(bool on) { Record(new AudioTestTap.Call { Method = AudioCall.Fire, Flag = on }); Ambience.SetFire(on); }
        public void SetWind(bool on) { Record(new AudioTestTap.Call { Method = AudioCall.Wind, Flag = on }); Ambience.SetWind(on); }
        public void SetSnow(bool on) { Record(new AudioTestTap.Call { Method = AudioCall.Snow, Flag = on }); Ambience.SetSnow(on); }

        public void SnowGust(float strength, float attack, float hold, float release)
        {
            Record(new AudioTestTap.Call { Method = AudioCall.SnowGust, Vel = strength });
            Ambience.Gust(strength, attack, hold, release);
        }

        /// <summary>Convenience for SnowWeather.OnGust subscribers (same as the four-float overload).</summary>
        public void SnowGust(in Core.SnowGust g) => SnowGust(g.Strength, g.Attack, g.Hold, g.Release);
        public SynthSource Source(SourceId id) => Sources != null ? Sources[(int)id] : null;

        public void SetSourceParent(SourceId id, Transform parent, Vector3 localPosition)
        {
            var s = Source(id);
            if (s == null) return;
            s.transform.SetParent(parent, false);
            s.transform.localPosition = localPosition;
            s.transform.localRotation = Quaternion.identity;
        }

        public void SetSourcePosition(SourceId id, Vector3 world)
        {
            var s = Source(id);
            if (s != null) s.transform.position = world;
        }

        public void SetMixedReality(bool mixedReality)
        {
            MixedReality = mixedReality;
            for (int i = 0; i < Sources.Length; i++)
            {
                var meta = _meta[i];
                if (meta == null || !meta.EnableAcoustics) continue;
                meta.ReverbSendDb = AudioRig.VrReverbSendDb(Sources[i].id) - (mixedReality ? AudioRig.MrSendDropDb : 0f);
            }
        }

        public void SetRoomBox(Vector3 center, Vector3 size, float yawDeg, bool realRoom) => SetRoomBox(center, size, realRoom);

        public void SetRoomBox(Vector3 center, Vector3 size, bool realRoom)
        {
            if (_room == null) return;
            _room.transform.position = center;
            _room.width = size.x; _room.height = size.y; _room.depth = size.z;
            _room.lockPositionToListener = false;
            var wall = realRoom ? MetaXRAudioRoomAcousticProperties.MaterialPreset.GypsumBoard : MetaXRAudioRoomAcousticProperties.MaterialPreset.PlasterOnBrick;
            _room.leftMaterial = _room.rightMaterial = _room.frontMaterial = wall;
            _room.backMaterial = realRoom ? wall : MetaXRAudioRoomAcousticProperties.MaterialPreset.Brick;
            _room.ceilingMaterial = MetaXRAudioRoomAcousticProperties.MaterialPreset.GypsumBoard;
            _room.floorMaterial = realRoom ? MetaXRAudioRoomAcousticProperties.MaterialPreset.Carpet : MetaXRAudioRoomAcousticProperties.MaterialPreset.WoodFloor;
            _room.clutterFactor = realRoom ? 0.5f : 0.6f;
            _room.enabled = true;
            _roomFramesLeft = 2; // one Update pushes the box, then it is disabled again
        }

        public void Chirp(ChirpKind kind, double when = 0)
        {
            Record(new AudioTestTap.Call { Method = AudioCall.Chirp, Index = (int)kind, When = when });
            GuideVoice.Chirp(kind, when);
        }

        public bool HasVoiceLine(string id) => GuideVoice.Has(id);
        /// <summary>The line's robotised buffer is resident; if it is not, its render starts (asynchronously: Sunny polls this
        /// while a line is pending, and the line is ready a few frames later, inside its 0.14 s lead).</summary>
        public bool VoiceLineReady(string id) => GuideVoice != null && GuideVoice.Has(id) && GuideVoice.Ready(id);
        /// <summary>Start rendering a line that will be said soon (a pending tip), so its first Play finds it resident.</summary>
        public void PrepareVoiceLine(string id) => GuideVoice?.Prepare(id);
        public float VoiceLineSeconds(string id) => GuideVoice.Seconds(id);

        public double PlayVoiceLine(string id, double when = 0)
        {
            Record(new AudioTestTap.Call { Method = AudioCall.VoiceLine, Line = id, When = when });
            return GuideVoice.Play(id, when);
        }

        public void StopVoiceLine() { Record(new AudioTestTap.Call { Method = AudioCall.StopVoiceLine }); GuideVoice.Stop(); }

        public void Swish(double when = 0) { Record(new AudioTestTap.Call { Method = AudioCall.Swish, When = when }); Ui.Swish(when); }

        /// <summary>The looper's metronome click (UiSounds.Click) on the 2D Ui source.</summary>
        public void Click(bool accent, double when = 0) { Record(new AudioTestTap.Call { Method = AudioCall.Click, Flag = accent, When = when }); Ui.Click(accent, when); }

        /// <summary>Re-seed the play-time randomness: jitter, fire pops (on the audio thread) and gust f0. The noise table,
        /// IRs and body phases are fixed at Init (seed them through Init / AudioRig.Build).</summary>
        public void SeedAll(uint seed)
        {
            Kit.Seed(seed);
            Ambience.Seed(seed);
            for (int i = 0; i < _units.Length; i++) _units[i]?.Seed(seed);
        }

        // ---------------- budget (ARCHITECTURE section 17: our C# <= 1.5 ms per 5.33 ms block, 0 dropped) ----------------

        /// <summary>Sum over the sources of their average block time (us): the audio thread's cost per block.</summary>
        public float SumAvgBlockMicros { get { float s = 0; foreach (var x in Sources) if (x != null) s += x.AvgBlockMicros; return s; } }
        /// <summary>Sum over the sources of their worst block (us): an upper bound for the worst block.</summary>
        public float SumMaxBlockMicros { get { float s = 0; foreach (var x in Sources) if (x != null) s += x.MaxBlockMicros; return s; } }
        public int TotalDroppedCommands { get { int n = 0; foreach (var x in Sources) if (x != null) n += x.DroppedCommands; return n; } }
        public int TotalStolenVoices { get { int n = 0; foreach (var x in Sources) if (x != null) n += x.StolenVoices; return n; } }
        public int TotalActiveVoices { get { int n = 0; foreach (var x in Sources) if (x != null) n += x.ActiveVoices; return n; } }
        public void ResetBlockStats() { foreach (var x in Sources) x?.ResetStats(); }
    }
}
