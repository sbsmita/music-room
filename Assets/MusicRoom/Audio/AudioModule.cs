// The Audio module (Order 10): builds the synth sources (one per AudioSourceTable row, with the registry's per-source send
// overrides), the Meta room acoustics and the master bus, and registers IAudioEngine and IAudioPresence. Sources start
// under the audio host at the room's world positions; instrument modules re-parent theirs with IAudioEngine.SetSourceParent
// (table in ARCHITECTURE.md section 7.2). At the end of every frame (FramePhase.EndFrame) the many-instrument platform's
// main-thread parts run (12-new-instruments/platform.md 10): AudioPresence (displayed instruments' sources pause) and the
// UnitGovernor (units' shares under load).
// Logs (ARCHITECTURE 7.5, 17, 19 items 11-13): at start-up the output rate, DSP block x count, spatializer (warned when it
// is not Meta's: every synth would play 2D at full level, spec 04 E1-D), Meta voice limit and acoustic model, the
// preallocated render buffers and the latency probe; once all renders are done, their wall time and when middle C was
// ready (budget 0.15 s); in development players, once a second, the audio thread's block cost.
// The output: 48 kHz (ProjectSettings, ApplyDspBuffer as the fallback); a later change of the configuration re-syncs the
// master bus and the room and reports a clock jump (AudioClockJumpEvent). While input focus is lost (the Quest menu) or the
// headset is off, AudioListener.pause holds every sound; back, every synth carrier plays again.
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace MusicRoom.Audio
{
    public sealed class AudioModule : AppModule
    {
        public const string MixerResource = "MusicRoomMixer";
        public const int ModuleOrder = 10;
        public const string MetaSpatializer = "Meta XR Audio";
        /// <summary>The VR room's acoustic box in the world (RoomFrame.WorldBox: the room turned to face the fire).</summary>
        public static readonly Vector3 VrRoomCenter = RoomFrame.WorldBox.center;
        public static readonly Vector3 VrRoomSize = RoomFrame.WorldBox.size;
        /// <summary>The VR positions of the Snow and Wind sources (at the snowy window: room frame (3.1, 1.55 / 1.9, -1.18), turned
        /// into the world by RoomFrame). MR moves them to the nearest real window and puts them back here on exit.</summary>
        public static readonly Vector3 SnowPosition = RoomFrame.ToWorld(new Vector3(3.1f, 1.55f, -1.18f)), WindPosition = RoomFrame.ToWorld(new Vector3(3.1f, 1.9f, -1.18f));
        /// <summary>The fire source in front of the fireplace (room frame (0.3, 0.5, -3.85), spec 06 section 10), in the world.</summary>
        public static readonly Vector3 FirePosition = RoomFrame.ToWorld(new Vector3(0.3f, 0.5f, -3.85f));
        /// <summary>ARCHITECTURE section 17: our C# per 5.33 ms block (256 frames at 48 kHz) on device. The one block budget of
        /// the audio side: this module's log line, the DSP load meter's over-budget count and the graph's adaptive budget all
        /// read it, scaled by the block length (BlockBudgetFor).</summary>
        public const float BlockBudgetMicros = 1500f;
        /// <summary>The block length BlockBudgetMicros is given for.</summary>
        public const int BudgetBlockFrames = 256;
        /// <summary>The budget of a block of <paramref name="frames"/> frames (BlockBudgetMicros at 256, in proportion).</summary>
        public static float BlockBudgetFor(int frames) => BlockBudgetMicros * (frames > 0 ? frames : BudgetBlockFrames) / BudgetBlockFrames;
        /// <summary>ARCHITECTURE section 17: the first key (middle C) ready this long after start-up.</summary>
        public const double FirstKeyBudgetSeconds = 0.15;

        /// <summary>The project's DSP buffer (ProjectSettings/AudioManager, AudioProjectSetup: 256 frames = 5.33 ms).</summary>
        public const int ProjectDspBuffer = 256;
        /// <summary>The device latency knob next to RigBootstrap.PreferredRefreshHz: the DSP block to run with. 256 is the
        /// shipping value; try 128 on the headset (keep it only while SynthSource.MaxBlockMicros stays under half a block
        /// and logcat shows no underruns). When it differs from the running configuration, Init calls AudioSettings.Reset
        /// once, before any source exists.</summary>
        public const int PreferredDspBuffer = 256;

        /// <summary>Log the block cost once a second (default: development players, not the editor, where tests run).</summary>
        public static bool LogBudget = Debug.isDebugBuild && !Application.isEditor;

        public override int Order => ModuleOrder;
        public AudioEngine Engine { get; private set; }
        /// <summary>The platform's main-thread parts (null when the engine has no graph: the governor).</summary>
        public AudioPresence Presence { get; private set; }
        public Burst.UnitGovernor Governor { get; private set; }
        App _app;
        ILayoutService _layout;
        int _servicesVersion = -1;
        public MasterBus Master { get; private set; }
        public AudioMixerGroup Group { get; private set; }
        /// <summary>The start-up line (also logged), for tests and the device checklist.</summary>
        public string StartupReport { get; private set; }
        /// <summary>The "renders done" line once logged (null before), for tests and the device checklist.</summary>
        public string RenderReport { get; private set; }
        /// <summary>The development "dsp load" logger, the once-a-second [Audio] dsp line (null in release players and, by
        /// default, in the editor). Nothing on screen: the head-locked HUD text is gone.</summary>
        public Burst.DspLoadLog DspLog { get; private set; }

        float _budgetClock;

        public override void Init(App app)
        {
            ApplyDspBuffer(PreferredDspBuffer);
            AudioEngine.LatencyProbe = Debug.isDebugBuild && !Application.isEditor; // dev device builds only
            var mixer = Resources.Load<AudioMixer>(MixerResource);
            if (mixer != null)
            {
                var groups = mixer.FindMatchingGroups("Master");
                if (groups != null && groups.Length > 0) Group = groups[0];
            }
            if (Group == null) Debug.LogWarning("[Audio] mixer 'MusicRoomMixer' missing: no Meta reflections / reverb");

            _app = app;
            // one row per source; every descriptor's finite per-source send / ER override applied (platform.md 10.1)
            AudioSourceTable.Current = AudioSourceTable.Build(app.Registry);
            var host = new GameObject("Audio");
            host.transform.SetParent(app.WorldRoot, false);
            var sources = AudioRig.Build(host, Group);
            Engine = host.AddComponent<AudioEngine>();
            Engine.Init(sources, 0, app.AudioClock, app.Settings);
            Presence = new AudioPresence(Engine);
            Presence.Bind(app);
            app.Register<IAudioPresence>(Presence);
            if (Engine.Graph != null)
            {
                Governor = new Burst.UnitGovernor(Engine.Graph);
                Governor.Bind(app.Registry);
            }

            // The fixed sources (spec 06 section 10, room frame: fire (0.3,0.5,-3.85), snow (3.1,1.55,-1.18), wind (3.1,1.9,-1.18)),
            // turned with the room (RoomFrame): the fire sounds from ahead, the window from the left.
            Engine.SetSourcePosition(SourceId.Fire, FirePosition);
            Engine.SetSourcePosition(SourceId.Snow, SnowPosition);
            Engine.SetSourcePosition(SourceId.Wind, WindPosition);

            // One shoebox room (spec 04 section 3.4): the VR room's box; updated by SetRoomBox in mixed reality.
            var roomGo = new GameObject("Meta Room");
            roomGo.transform.SetParent(host.transform, false);
            var room = roomGo.AddComponent<MetaXRAudioRoomAcousticProperties>();
            Engine.AttachRoom(room);
            Engine.SetRoomBox(VrRoomCenter, VrRoomSize, false);

            // The master chain on the listener (the centre eye).
            var head = app.Head;
            if (head != null)
            {
                if (head.GetComponent<AudioListener>() == null) head.gameObject.AddComponent<AudioListener>();
                Master = head.GetComponent<MasterBus>() ?? head.gameObject.AddComponent<MasterBus>();
            }
            app.Register<IAudioEngine>(Engine);
            Engine.ConfigurationChanged += OnConfigurationChanged;
            Engine.ClockJumped += OnClockJumped;
            app.Events.Subscribe<InputFocusEvent>(OnInputFocus);
            app.Events.Subscribe<HmdMountEvent>(OnMount);

            // Development players: the once-a-second [Audio] dsp line (burst.md 13), in the log only (no HUD on screen).
            if (Engine.Graph != null && head != null && ((Debug.isDebugBuild && !Application.isEditor) || (Application.isEditor && Burst.DspLoadLog.LogInEditor)))
            {
                var log = new GameObject("DSP Load Log").AddComponent<Burst.DspLoadLog>();
                log.transform.SetParent(host.transform, false);
                log.Host = Engine.Graph;
                DspLog = log;
                // After MasterBus on the listener: records the final mix on request (Engine/AudioTap.cs).
                if (!Application.isEditor && head.GetComponent<AudioTap>() == null) head.gameObject.AddComponent<AudioTap>();
            }

            // Device check list (ARCHITECTURE section 19, items 11 and 13): output rate, DSP block size x count, spatializer.
            AudioSettings.GetDSPBufferSize(out int dspFrames, out int dspBuffers);
            string spatializer = AudioSettings.GetSpatializerPluginName();
            int voiceLimit = -1;
            string acoustics = "?";
            try
            {
                voiceLimit = MetaXRAudioSettings.Instance.voiceLimit;
                acoustics = MetaXRAcousticSettings.Instance.AcousticModel.ToString();
            }
            catch (System.Exception e) { Debug.LogWarning("[Audio] Meta XR Audio settings unreadable: " + e.Message); }
            StartupReport = $"sr={Engine.SampleRate} dsp={dspFrames}x{dspBuffers} spatializer='{spatializer}' voiceLimit={voiceLimit} " +
                            $"acoustics={acoustics} mixer={(Group != null)} lines={Engine.GuideVoice.LoadedLines}/{Synths.GuideVoice.LineCount} " +
                            $"renders={Engine.Renders.Total} buffers={Engine.PreallocatedBytes / 1048576.0:F1}MiB probe={AudioEngine.LatencyProbe} " +
                            $"seed={Engine.Seed} graph={(Engine.Graph != null ? $"{(Engine.Graph.ArenaBytes + Engine.Graph.AllocatedBytes) / 1048576.0:F2}MiB" : "off")} " +
                            $"burst={Unity.Burst.BurstCompiler.IsEnabled} units={UnitList()} " +
                            $"sources={Engine.Sources.Length} bank={Engine.Bank.SlotCount} governed={Governor?.Count ?? 0}";
            Debug.Log("[Audio] " + StartupReport);
            if (spatializer != MetaSpatializer)
                Debug.LogWarning($"[Audio] spatializer is '{spatializer}', not '{MetaSpatializer}': every synth source plays 2D at full level (run Tools/unity.sh configure)");
        }

        /// <summary>Runs the DSP at <paramref name="frames"/> per block (AudioSettings.Reset) when the current configuration
        /// differs. Called before the synth sources exist, so nothing has to restart.</summary>
        public static void ApplyDspBuffer(int frames)
        {
            var c = AudioSettings.GetConfiguration();
            // Quest 3S runs its output at 48 kHz, but the "system default" rate came up as 24 kHz on the device: half the
            // bandwidth, and the drum kit's 48 kHz tables refused to start. The device always asks for 48 kHz.
            bool fixRate = !Application.isEditor && c.sampleRate != DeviceSampleRate;
            if (c.dspBufferSize == frames && !fixRate) return;
            int was = c.dspBufferSize, wasRate = c.sampleRate;
            c.dspBufferSize = frames;
            if (fixRate) c.sampleRate = DeviceSampleRate;
            bool ok = AudioSettings.Reset(c);
            var now = AudioSettings.GetConfiguration();
            Debug.Log($"[Audio] dsp buffer {was} -> {frames}, rate {wasRate} -> {c.sampleRate}: {(ok ? "ok" : "refused")} (now {now.dspBufferSize} at {now.sampleRate} Hz)");
            // ProjectSettings asks for 48 kHz already (AudioProjectSetup); this Reset is the fallback. Still another rate: the
            // 48 kHz-only instruments will refuse to start (SoundUnits) and play the classic sound instead.
            if (fixRate && now.sampleRate != DeviceSampleRate)
                Debug.LogError($"[Audio] the output runs at {now.sampleRate} Hz after asking for {DeviceSampleRate} Hz: the 48 kHz instruments fall back to the classic sound");
        }

        /// <summary>The output rate asked for on the device (the headset's native mixer rate).</summary>
        public const int DeviceSampleRate = 48000;

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.EndFrame && Engine != null) { PlatformTick(dt); return; }
            if (phase != FramePhase.Listener || Engine == null) return;
            ReportNotReady();
            if (RenderReport == null && Engine.RenderSeconds >= 0) _reportWait += dt;
            // The line waits for middle C's buffer (FirstKeySeconds; at most 10 s).
            if (RenderReport == null && Engine.RenderSeconds >= 0 && (Engine.FirstKeySeconds >= 0 || _reportWait > 10f))
            {
                var g = Engine.GuideVoice;
                RenderReport = $"renders done in {Engine.RenderSeconds:F2} s: {Engine.Renders.Completed}/{Engine.Renders.Total} " +
                               $"(failed {Engine.Renders.Failed}), first key {Engine.FirstKeySeconds:F3} s (budget {FirstKeyBudgetSeconds:F2}), " +
                               $"wave tables {Dsp.PeriodicWaveTables.BuildMilliseconds:F0} ms, bank {Engine.Bank.TotalSamples() * 4 / (1024 * 1024)} MiB, " +
                               $"lines {g.RenderedLines}/{g.LoadedLines} (on demand {g.SyncRenders}), workers lowered {Engine.Renders.PriorityLowered}";
                Debug.Log("[Audio] " + RenderReport);
            }
            if (!LogBudget) return;
            _budgetClock += dt;
            if (_budgetClock < 1f) return;
            _budgetClock = 0f;
            // Once a second in development players only (a string per second, never per frame).
            float avg = Engine.SumAvgBlockMicros, max = Engine.SumMaxBlockMicros;
            AudioSettings.GetDSPBufferSize(out int blockFrames, out _);
            Debug.Log($"[Audio] block avg {avg:F0} us max {max:F0} us (budget {BlockBudgetFor(blockFrames):F0}) voices {Engine.TotalActiveVoices} " +
                      $"dropped {Engine.TotalDroppedCommands} stolen {Engine.TotalStolenVoices}");
            Engine.ResetBlockStats();
        }

        /// <summary>End of frame: presence (who sounds; pause / resume), the governor. Allocation-free.</summary>
        void PlatformTick(float dt)
        {
            if (_app != null && _app.ServicesVersion != _servicesVersion)
            {
                _servicesVersion = _app.ServicesVersion;
                _layout = _app.Get<ILayoutService>();
            }
            Presence?.Tick(dt);
            Governor?.Tick(_layout, Presence);
        }

        string UnitList()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var role in Burst.SoundUnits.LiveRoles)
                if (Engine.Unit(role) != null) sb.Append(sb.Length > 0 ? "," : "").Append(role.ToString().ToLowerInvariant());
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        bool _focusLost, _unmounted;
        float _reportWait;
        readonly bool[] _notReadyLogged = new bool[(int)Burst.UnitId.Count];

        /// <summary>Once per instrument, when its unit is ready: how many early events it dropped while it was building (a
        /// press before its tables were published plays nothing). One quiet line each, never per frame.</summary>
        void ReportNotReady()
        {
            for (int i = 0; i < _notReadyLogged.Length; i++)
            {
                if (_notReadyLogged[i]) continue;
                Burst.SoundUnit u = Engine.Unit((Burst.UnitId)i);
                if (u == null || u.NotReadyDrops < 0) { _notReadyLogged[i] = true; continue; }
                if (!u.Ready) continue;
                _notReadyLogged[i] = true;
                Debug.Log($"[Audio] {(Burst.UnitId)i} ready: {u.NotReadyDrops} early event(s) dropped while it was building");
            }
        }

        void OnInputFocus(InputFocusEvent e) { _focusLost = !e.Focused; Engine?.SetPaused(_focusLost || _unmounted); }
        void OnMount(HmdMountEvent e) { _unmounted = !e.Mounted; Engine?.SetPaused(_focusLost || _unmounted); }

        /// <summary>The output restarted: the master chain is rebuilt for the rate now running.</summary>
        void OnConfigurationChanged()
        {
            if (Master != null) Master.Configure();
            Debug.Log($"[Audio] output configuration changed: sr={Engine.SampleRate}, master bus and room re-synced");
        }

        void OnClockJumped(double delta) => _app?.Events.Publish(new AudioClockJumpEvent { Delta = delta });

        public override void Shutdown()
        {
            if (Engine != null)
            {
                Engine.ConfigurationChanged -= OnConfigurationChanged;
                Engine.ClockJumped -= OnClockJumped;
                Engine.SetPaused(false);   // AudioListener.pause is global: never left on after the app
            }
            if (_app != null)
            {
                _app.Events.Unsubscribe<InputFocusEvent>(OnInputFocus);
                _app.Events.Unsubscribe<HmdMountEvent>(OnMount);
            }
            Presence?.Unbind();
            Engine?.Renders?.Cancel();
            Engine?.ShutdownGraph();
        }
    }
}
