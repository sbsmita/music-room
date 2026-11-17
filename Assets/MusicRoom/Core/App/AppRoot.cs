// The one scene object besides the rig ("App" in Main.unity, created by SceneBuilder). Awake builds the App,
// discovers the modules and initialises them; Update runs the frame. Tests set BootOptions before adding it.
using System;
using System.Collections.Generic;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class BootOptions
    {
        /// <summary>Force a hand source (tests: a FakeHandSource). Null = Meta when available, else mouse (editor) or an
        /// untracked source (device player).</summary>
        public IHandSource HandSource;
        /// <summary>Keep only the modules this returns true for (null = all discovered modules).</summary>
        public Func<Type, bool> ModuleFilter;
        /// <summary>Test probes added to the discovered modules (same Order sorting). Discovery never scans the test
        /// assemblies, so this is the only way a test module joins a boot.</summary>
        public readonly List<IAppModule> ExtraModules = new List<IAppModule>();
        /// <summary>Skip renderers that only matter on screen (hover glow quads, debug hands).</summary>
        public bool Headless;
        /// <summary>Where AppSettings lives (null = PlayerPrefs). Every PlayMode boot passes a fresh MemorySettingsStore so
        /// tests never read or write the developer's real preferences (TestBoot does this).</summary>
        public ISettingsStore Settings;
        /// <summary>Root for app files (null = Application.persistentDataPath): takes go to DataPath/takes. Tests pass a
        /// temporary folder and delete it in TearDown.</summary>
        public string DataPath;
        /// <summary>Tests: behave as if an XR session were presenting (App.IsPresenting) with the scripted hands, so the
        /// XR-only paths (grab, swivel, summon, page turn, nod kick, placing) run without a headset.</summary>
        public bool SimulateHeadset;
        /// <summary>Tests: Unity's Update drives nothing (no AppRoot.Frame, no HandInput tick); the test advances the hands,
        /// the clock and one frame with App.Step(dt), so speeds, dwells and timers are exact.</summary>
        public bool ManualFrames;
        /// <summary>Tests: the main-thread audio clock (IAudioEngine.Now / FrameNow, Clock.AudioNow, the recorder, Sunny's
        /// line timing). Null = AudioSettings.dspTime. The mixer itself always runs on dspTime.</summary>
        public Func<double> AudioClock;
        /// <summary>Show the start panel ("Play here" / "Play in my room") when the app starts. The app always does; TestBoot
        /// turns it off unless a test asks for it, so the other tests boot straight into the room as before.</summary>
        public bool StartPanel = true;
        /// <summary>What App.Quit does instead of quitting (tests: a counter, so a test can never quit the test runner).
        /// Null = Application.Quit on a device, leave Play mode in the editor.</summary>
        public Action Quit;
        /// <summary>platform.md 3.2: which registry instruments boot (default all). A filtered-out instrument's module is not
        /// created and it is !IsLoaded: the menu hides it and the carousel skips it. Tests boot the classic three with
        /// k => k &lt;= InstrumentKind.Harp.</summary>
        public Func<InstrumentKind, bool> InstrumentFilter;
    }

    [DefaultExecutionOrder(-40)] // after HandInput (-100), RigBootstrap (-90), MetaHandVisual (-80), OVRCameraRig (-50)
    public sealed class AppRoot : MonoBehaviour
    {
        public const string RootName = "App";

        /// <summary>Set BEFORE the AppRoot awakes (tests, tools). Consumed and cleared in Awake.</summary>
        public static BootOptions Options;

        public App App { get; private set; }
        public bool Ready => App != null;

        void Awake()
        {
            var options = Options ?? new BootOptions();
            Options = null;
            if (options.HandSource != null) HandInput.OverrideSource = options.HandSource;
            var rig = RigBootstrap.Ensure();
            var hands = GetComponent<HandInput>() ?? gameObject.AddComponent<HandInput>();
            if (hands.handMaterial == null) hands.handMaterial = Resources.Load<Material>("MaterialTemplates/HandGhost");
            // E6 looks (templates from CoreMaterialTemplates): the lit skin hand in VR, the passthrough cut-out in MR.
            if (hands.skinMaterial == null) hands.skinMaterial = Resources.Load<Material>(MaterialLibrary.ResourcesPrefix + CoreMaterialTemplates.HandSkin);
            if (hands.cutoutMaterial == null) hands.cutoutMaterial = Resources.Load<Material>(MaterialLibrary.ResourcesPrefix + CoreMaterialTemplates.HandPassthrough);
            if (options.Headless) hands.debugJoints = false;
            hands.ManualFrames = options.ManualFrames;
            // The head camera never draws the helper layers; the desktop camera does not look around when a left-button
            // press lands on a desktop click target (the module owns that click).
            var cam = rig.HeadCamera;
            if (cam != null) cam.cullingMask &= ~Layers.HeadCameraExcludeMask;
            if (rig.Desktop != null) rig.Desktop.dragBlockMask = Layers.PlaySurfaceMask;
            App = new App(this, rig, hands, options);
            var modules = ModuleDiscovery.CreateModules(ComposeFilter(options.ModuleFilter, App.Registry), options.ExtraModules);
            App.InitModules(modules);
            Debug.Log($"[App] booted: {modules.Count} module(s), hands={hands.Source.Name}, xr={rig.IsXrActive}{(options.SimulateHeadset ? " (simulated headset)" : "")}{(options.ManualFrames ? " manual-frames" : "")}");
        }

        /// <summary>The module filter plus BootOptions.InstrumentFilter: a filtered instrument's definition assembly creates no
        /// module.</summary>
        static Func<Type, bool> ComposeFilter(Func<Type, bool> filter, InstrumentRegistry registry)
        {
            var blocked = new HashSet<System.Reflection.Assembly>();
            if (registry != null)
                foreach (var d in registry.All)
                    if (registry.IsFiltered(d.Kind) && registry.DefinitionType(d.Kind) != null) blocked.Add(registry.DefinitionType(d.Kind).Assembly);
            if (blocked.Count == 0) return filter;
            return t => !blocked.Contains(t.Assembly) && (filter == null || filter(t));
        }

        void Update()
        {
            if (App != null && !App.ManualFrames) App.Frame();
        }

        void OnDestroy()
        {
            App?.Shutdown();
            App = null;
            HandInput.OverrideSource = null;
        }
    }
}
