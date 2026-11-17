// Finds (or, for tests, creates) the OVRCameraRig and owns the XR-or-desktop decision (spec 01 sections 2.3, 2.6, 6).
// Without a runtime (macOS editor) OVRManager never initialises, OVRCameraRig leaves the camera on the floor and
// OVRManager.display / instance stay null: every call into them is guarded by IsXrActive, so play mode and tests
// run headless with the DesktopRigFallback camera instead.
// E5 (experience.md section 7): the display starts at 90 Hz and RefreshGovernor may step it to 72 Hz from the app's GPU
// time and the compositor's dropped frames, never stepping back up within a session. The SRP foveation level is set once
// when XR starts (RefreshGovernor.SessionFoveation) and never changed mid-session: on the headset a mid-session change
// left flickering blocks in the right eye. Dynamic resolution stays OVRManager's (0.7 .. DynamicResolutionMax); Unity OpenXR's own
// Automatic Viewport Dynamic Resolution feature stays off (QuestSetup), so the two never fight.
// Passthrough (burst.md section 10.1): the scene starts with it OFF and without an OVRPassthroughLayer, so the VR room may
// run at CPU level 4 (Meta allows it only while no passthrough feature is enabled); XrMode.StartPassthrough adds the
// layer and initialises passthrough when "Play in my room" is entered, XrMode.StopPassthrough shuts it down on exit.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace MusicRoom.Hands
{
    public enum XrState { Starting, Xr, Desktop }

    [DefaultExecutionOrder(-90)]
    public sealed class RigBootstrap : MonoBehaviour
    {
        public const string RigName = "PlayerRig";
        /// <summary>Display refresh rate to request first (E5: 90 Hz with the quality governor; 72 Hz is the fallback rung).</summary>
        public const float PreferredRefreshHz = RefreshGovernor.HighHz;
        public const float FallbackRefreshHz = RefreshGovernor.LowHz;
        /// <summary>OVRManager's dynamic resolution range on Quest 3 / 3S (SceneBuilder writes it into the scene). The eye
        /// buffer is allocated at the maximum: 1.6 costs about 100 MB more than 1.3. When the dev-build log line
        /// "[RIG] dynres ... session p95" never exceeds 1.3 on the headset, set the maximum to 1.3 (experience.md E5).</summary>
        public const float DynamicResolutionMin = 0.7f, DynamicResolutionMax = 1.6f;
        /// <summary>E5: compositor sharpening of the eye buffer (crisper key letters and menu text). UNVERIFIED under
        /// OpenXR (ARCHITECTURE.md section 19 #7): check by eye on the headset; None if it makes no visible difference.</summary>
        public const OVRPlugin.LayerSharpenType SharpenType = OVRPlugin.LayerSharpenType.Automatic;
        /// <summary>Seconds to wait for OVRManager before giving up and running the desktop camera.</summary>
        public const float XrStartTimeout = 10f;

        public static RigBootstrap Instance { get; private set; }

        public OVRCameraRig Rig { get; private set; }
        public OVRManager Manager { get; private set; }
        public OVRPassthroughLayer Passthrough { get; private set; }
        public DesktopRigFallback Desktop { get; private set; }
        public XrState State { get; private set; } = XrState.Starting;

        public Transform RigRoot => Rig != null ? Rig.transform : transform;
        public Transform TrackingSpace => Rig != null ? Rig.trackingSpace : null;
        public Transform Head => Rig != null ? Rig.centerEyeAnchor : null;
        public Camera HeadCamera => Head != null ? Head.GetComponent<Camera>() : null;
        /// <summary>Managed-only check (safe without the native plugin): the headset runtime is up and an HMD is present.</summary>
        public bool IsXrActive => OVRManager.OVRManagerinitialized && OVRManager.isHmdPresent;
        public bool HmdMounted { get; private set; } = true;
        /// <summary>The headset's position is tracked (OVRManager.tracker.isPositionTracked, polled every XR frame). After a
        /// resume the runtime reports a few untracked frames (the log's "TrackingLost" / "TrackingAcquired"), during which the
        /// head pose is stale: Layout waits for this before it trusts the eyes' height or a recentre (LayoutModule.Height).
        /// Settable for tests; without a runtime it stays as set (true).</summary>
        public bool HeadTracked { get; set; } = true;

        /// <summary>Raised when the runtime recentred the player (re-run the layout: ArrangeAroundPlayer).</summary>
        public event Action Recentered;
        /// <summary>Raised on HMD mount / unmount (menu hides and the guide's voice cuts on unmount).</summary>
        public event Action<bool> HmdMountChanged;
        /// <summary>False while the system menu (or another system overlay) has input focus.</summary>
        public bool HasInputFocus { get; private set; } = true;
        /// <summary>Raised when input focus is lost (false) or regained (true): OVRManager.InputFocusLost / Acquired.</summary>
        public event Action<bool> InputFocusChanged;
        public event Action<XrState> StateChanged;

        /// <summary>E5: the quality ladder (plain C#; ticked in Update while XR runs).</summary>
        public RefreshGovernor Governor { get; } = new RefreshGovernor();
        /// <summary>E5: the quality rung in force.</summary>
        public QualityRung Rung => Governor.Rung;
        /// <summary>E5: raised after the governor (or the system's thermal drop to 72 Hz) changed the rung.</summary>
        public event Action<QualityRung> QualityChanged;
        /// <summary>Tests and device experiments: false freezes the ladder (the rung in force stays).</summary>
        [NonSerialized] public bool GovernorEnabled = true;

        readonly FrameTiming[] _timings = new FrameTiming[1];
        int _lastDropped = -1;
        bool _ptWas;
        float _dynresT;
        readonly int[] _dynresHist = new int[DynresBins];
        int _dynresN;
        const int DynresBins = 40;          // 0.05 steps over 0 .. 2
        const float DynresBin = 0.05f;
        /// <summary>The scene's rig (from SceneBuilder) or a code-only rig for tests (spec 01 section 2.3).</summary>
        public static RigBootstrap Ensure()
        {
            if (Instance != null) return Instance;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            GameObject go;
            if (rig == null)
            {
                go = new GameObject(RigName);
                go.SetActive(false);
                var mgr = go.AddComponent<OVRManager>();   // one per scene; a duplicate DestroyImmediate's itself
                rig = go.AddComponent<OVRCameraRig>();     // Awake builds TrackingSpace + anchors + cameras
                mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                mgr.isInsightPassthroughEnabled = false;   // tests never start passthrough
                go.SetActive(true);
                var cam = rig.centerEyeAnchor.GetComponent<Camera>();
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 40f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = false;
                if (rig.centerEyeAnchor.GetComponent<AudioListener>() == null) rig.centerEyeAnchor.gameObject.AddComponent<AudioListener>();
            }
            else go = rig.gameObject;
            var boot = go.GetComponent<RigBootstrap>() ?? go.AddComponent<RigBootstrap>();
            boot.Bind();
            return boot;
        }

        void Awake()
        {
            Instance = this;
            Bind();
        }

        void Bind()
        {
            if (Rig != null) return;
            Rig = GetComponent<OVRCameraRig>();
            Manager = GetComponent<OVRManager>();
            Passthrough = GetComponent<OVRPassthroughLayer>();
            Desktop = GetComponent<DesktopRigFallback>() ?? gameObject.AddComponent<DesktopRigFallback>();
            Desktop.enabled = false;
            if (Head != null && Head.GetComponent<AudioListener>() == null) Head.gameObject.AddComponent<AudioListener>();
        }

        void OnEnable()
        {
            OVRManager.HMDMounted += OnMounted;
            OVRManager.HMDUnmounted += OnUnmounted;
            OVRManager.DisplayRefreshRateChanged += OnRefreshChanged;
            OVRManager.InputFocusLost += OnFocusLost;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
        }

        void OnDisable()
        {
            OVRManager.HMDMounted -= OnMounted;
            OVRManager.HMDUnmounted -= OnUnmounted;
            OVRManager.DisplayRefreshRateChanged -= OnRefreshChanged;
            OVRManager.InputFocusLost -= OnFocusLost;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        IEnumerator Start()
        {
            for (float t = 0; !IsXrActive && t < XrStartTimeout; t += Time.unscaledDeltaTime)
            {
                // The editor without a runtime never initialises: no point waiting the full timeout there.
                if (Application.isEditor && t > 0.5f) break;
                yield return null;
            }
            if (!IsXrActive)
            {
                Desktop.enabled = true;
                XrMode.ApplyBackground(this, false);   // the scene stores alpha 0 (Meta's editor rule); VR / desktop clear #05070f
                SetState(XrState.Desktop);
                // A runtime that comes up late (XR Simulator in the editor, a slow device start) still gets the XR state.
                while (!IsXrActive) yield return null;
                Desktop.enabled = false;
            }
            ApplyRuntimeKnobs();
            XrMode.ApplyBackground(this, XrMode.PassthroughOn);
            SetState(XrState.Xr);
        }

        /// <summary>Once XR is up (spec 01 section 2.6): refresh rate (90 Hz, or 72 Hz without it), the session's SRP
        /// foveation level (set here only), sharpening, perf levels, recenter hook.</summary>
        void ApplyRuntimeKnobs()
        {
            try
            {
                var display = OVRManager.display;
                bool has90 = false;
                if (display != null)
                {
                    float[] hz = display.displayFrequenciesAvailable;   // Quest 3/3S: 72/80/90/120 (UNVERIFIED)
                    has90 = hz != null && Array.IndexOf(hz, PreferredRefreshHz) >= 0;
                    Governor.Start(has90);
                    display.displayFrequency = Governor.Hz;
                    display.RecenteredPose -= OnRecentered;
                    display.RecenteredPose += OnRecentered;
                    Debug.Log($"[RIG] refresh {display.displayFrequency} Hz (available: {(hz == null ? "?" : string.Join("/", hz))})");
                }
                else Governor.Start(false);
                float fov = Governor.Foveation;
                Debug.Log($"[RIG] SRP foveation level {fov} (fixed for the session) on {ApplyFoveation(fov)} display subsystem(s); quality rung {(int)Governor.Rung}: {RefreshGovernor.Describe(Governor.Rung)}");
                OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh; // the synth uses CPU
                OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                if (Manager != null) Manager.sharpenType = SharpenType;
                _lastDropped = -1;
                _ptWas = XrMode.PassthroughOn;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RIG] runtime knobs: " + e.Message);
            }
        }

        // ---------------- E5: the quality governor ----------------

        void Update()
        {
            if (State != XrState.Xr || !IsXrActive) return;
            float dt = Time.unscaledDeltaTime;
            try { var tracker = OVRManager.tracker; if (tracker != null) HeadTracked = tracker.isPositionTracked; }
            catch (Exception) { HeadTracked = true; }
            try
            {
                if (XrMode.PassthroughOn != _ptWas) { _ptWas = XrMode.PassthroughOn; Governor.Settle(); }   // MR entered or left
                if (GovernorEnabled)
                {
                    float gpu = GpuMs();
                    int dropped = DroppedDelta();
                    if (Governor.Frame(dt, gpu, dropped))
                        ApplyRung(Governor.Rung < Governor.Previous ? "retry after a resume"
                            : $"gpu p95 {Governor.LastP95:F1} ms, dropped {Governor.LastDropped}");
                }
                if (Debug.isDebugBuild) LogDynamicResolution(dt);
            }
            catch (Exception e)
            {
                GovernorEnabled = false;
                Debug.LogWarning("[RIG] quality governor off: " + e.Message);
            }
        }

        /// <summary>The app's GPU time this frame in ms: XR_META_performance_metrics through OVRPlugin (values under 0.5
        /// are taken as seconds: the unit is UNVERIFIED), else Unity's FrameTimingManager (enableFrameTimingStats, set by
        /// QuestSetup); 0 when neither has a value.</summary>
        float GpuMs()
        {
            float? v = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float);
            if (v.HasValue && v.Value > 0f) return v.Value < 0.5f ? v.Value * 1000f : v.Value;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0) return (float)_timings[0].gpuFrameTime;
            return 0f;
        }

        /// <summary>Frames the compositor dropped since the last call (a counter reset is ignored).</summary>
        int DroppedDelta()
        {
            int? v = OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_DroppedFrameCount_Int);
            if (!v.HasValue) return 0;
            int d = _lastDropped < 0 ? 0 : v.Value - _lastDropped;
            _lastDropped = v.Value;
            return d > 0 ? d : 0;
        }

        /// <summary>Puts the governor's rung into force: the display rate, only when it changes. The foveation level is
        /// never touched here (it is fixed for the session).</summary>
        void ApplyRung(string why)
        {
            QualityRung r = Governor.Rung, from = Governor.Previous;
            var display = OVRManager.display;
            if (display != null && Mathf.Abs(display.displayFrequency - Governor.Hz) > 0.5f) display.displayFrequency = Governor.Hz;
            _lastDropped = -1;   // the rate change drops a frame or two: start counting again
            Debug.Log($"[RIG] quality rung {(int)r}: {RefreshGovernor.Describe(from)} -> {RefreshGovernor.Describe(r)} ({why})");
            QualityChanged?.Invoke(r);
        }

        /// <summary>The runtime changed the refresh rate. Below 90 Hz without the governor asking (thermal throttling) the
        /// ladder moves to the matching 72 Hz rung and stays there.</summary>
        void OnRefreshChanged(float from, float to)
        {
            Debug.Log($"[RIG] display refresh changed {from} -> {to} Hz");
            if (to < PreferredRefreshHz - 0.5f && Governor.SystemDroppedRefresh()) ApplyRung($"the system set {to} Hz");
        }

        void OnApplicationPause(bool paused)
        {
            // The compositor keeps counting dropped frames while the app is away: never charge them to the next window.
            _lastDropped = -1;
            if (paused) Governor.Settle();
            else Governor.Resumed();
        }

        void OnFocusLost() => SetInputFocus(false);
        void OnFocusAcquired() => SetInputFocus(true);

        void SetInputFocus(bool focused)
        {
            if (HasInputFocus == focused) return;
            HasInputFocus = focused;
            _lastDropped = -1;
            Governor.SetFocused(focused);
            Debug.Log($"[RIG] input focus {(focused ? "back" : "lost (system menu)")}");
            InputFocusChanged?.Invoke(focused);
        }

        /// <summary>Dev builds: once a second the effective eye-buffer scale (eyeTextureResolutionScale x
        /// renderViewportScale, moved by OVRManager's dynamic resolution) and its session p95, the evidence for
        /// DynamicResolutionMax.</summary>
        void LogDynamicResolution(float dt)
        {
            _dynresT += dt;
            if (_dynresT < 1f) return;
            _dynresT = 0f;
            float eye = XRSettings.eyeTextureResolutionScale, vp = XRSettings.renderViewportScale, s = eye * vp;
            int bin = Mathf.Clamp(Mathf.FloorToInt(s / DynresBin), 0, DynresBins - 1);
            _dynresHist[bin]++;
            _dynresN++;
            int target = Mathf.CeilToInt(0.95f * _dynresN), acc = 0;
            float p95 = DynresBins * DynresBin;
            for (int i = 0; i < DynresBins; i++) { acc += _dynresHist[i]; if (acc >= target) { p95 = (i + 1) * DynresBin; break; } }
            Debug.Log($"[RIG] dynres viewport {vp:F2} x eye {eye:F2} = {s:F2} (session p95 {p95:F2} over {_dynresN} s; cap 1.3 is safe while p95 <= 1.3)");
        }

        // ---------------- passthrough, created on demand (burst.md section 10.1) ----------------

        /// <summary>The underlay layer, added to the rig the first time mixed reality is entered (never in the scene:
        /// Meta allows CPU level 4 only while no passthrough feature is enabled). Hidden until XrMode shows it.</summary>
        public OVRPassthroughLayer EnsurePassthroughLayer()
        {
            if (Passthrough != null) return Passthrough;
            Passthrough = gameObject.AddComponent<OVRPassthroughLayer>();
            Passthrough.hidden = true;
            return Passthrough;
        }

        /// <summary>SRP Foveation API (OpenXR "Foveated Rendering" feature, MetaQuestFeature foveatedRenderingApi =
        /// SRPFoveation): the strength goes to every running XRDisplaySubsystem; URP reads it per XR pass. Never
        /// OVRManager.foveatedRenderingLevel / useDynamicFoveatedRendering: those drive the Meta Legacy feature, which is off.
        /// Returns the number of display subsystems set.</summary>
        public static int ApplyFoveation(float level)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                d.foveatedRenderingLevel = Mathf.Clamp01(level);
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None; // fixed (no eye tracking)
            }
            return displays.Count;
        }

        void SetState(XrState s)
        {
            State = s;
            Debug.Log($"[RIG] state={s} xr={IsXrActive} head={(Head != null ? Head.position.ToString("F2") : "-")}");
            StateChanged?.Invoke(s);
        }

        void OnRecentered()
        {
            Governor.Settle();
            Recentered?.Invoke();
        }
        void OnMounted() { HmdMounted = true; HmdMountChanged?.Invoke(true); }
        void OnUnmounted() { HmdMounted = false; HmdMountChanged?.Invoke(false); }
    }
}
