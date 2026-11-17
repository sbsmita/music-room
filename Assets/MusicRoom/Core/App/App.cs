// The application object: services, module registry, service lookup and the frame orchestration
// (spec 05 section 1.1 order). One App per AppRoot; App.Current is the one in the scene.
using System;
using System.Collections.Generic;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class App
    {
        public static App Current { get; private set; }

        public AppRoot Root { get; }
        /// <summary>Parent of everything the modules build (room, rigs, Sunny, audio sources). Not under the rig.</summary>
        public Transform WorldRoot { get; }
        public RigBootstrap Rig { get; }
        public HandInput HandInput { get; }
        public Clock Clock { get; } = new Clock();
        public EventBus Events { get; } = new EventBus();
        /// <summary>Persisted settings (PlayerPrefs, or BootOptions.Settings in tests).</summary>
        public AppSettings Settings { get; }
        /// <summary>Root folder for app files (Application.persistentDataPath, or BootOptions.DataPath in tests).</summary>
        public string DataPath { get; }
        public MaterialLibrary Materials { get; }
        public HoverGlowPool HoverGlow { get; }
        /// <summary>10-upgrade/experience.md section 2.2: fingers that stop on surfaces (E1), soft contact shadows (E3), the
        /// hands' look in MR (E6). Frame step 8a.</summary>
        public ContactVisuals Contacts { get; }
        public PointerInput Input { get; }
        /// <summary>The instrument registry (platform.md 3), built before any module's Init. Also Get&lt;IInstrumentRegistry&gt;().</summary>
        public InstrumentRegistry Registry { get; }
        /// <summary>Platform step 3b: who owns each fingertip this frame (Pointer.ClaimedBy).</summary>
        public PointerClaims Claims { get; }
        /// <summary>Step 3c: a held tool goes back when its hand leaves the instrument or another one claims the hand.</summary>
        public HeldToolGuard HeldTools { get; }
        /// <summary>Counts Register / Unregister calls (caches of service lists compare it instead of polling).</summary>
        public int ServicesVersion { get; private set; }
        /// <summary>E4: Fast Motion Mode only while drumming (ticked in step 18, EndFrame).</summary>
        public FastMotionDriver FastMotion { get; }
        public IReadOnlyList<IAppModule> Modules => _modules;
        public bool Headless { get; }
        /// <summary>Head transform (centre eye). Never null after construction (code-only rig in tests).</summary>
        public Transform Head => Rig.Head;
        /// <summary>Real hands: the frames come from the headset's tracker (MetaHandSource), not the mouse or a script.</summary>
        public bool IsHeadset => HandInput.IsHeadset;
        /// <summary>The XR-presenting gate: an XR session is running (Rig.State == Xr), or a test set
        /// BootOptions.SimulateHeadset and drives scripted (non-mouse) hands. Every headset-only path checks
        /// this (grab, swivel, summon, page turn, nod kick, placing, the *_vr voice lines); the desktop-only paths (M,
        /// Shift+H, mouse clicks, Shift/Alt-drag) run while it is false.</summary>
        public bool IsPresenting => Rig.State == XrState.Xr || (SimulateHeadset && HandInput.Source != HandInput.Mouse);
        public bool SimulateHeadset { get; }
        /// <summary>BootOptions.ManualFrames: Unity's Update drives nothing; tests call Step(dt).</summary>
        public bool ManualFrames { get; }
        /// <summary>The main-thread audio clock source (null = AudioSettings.dspTime). IAudioEngine and the recorder use it.</summary>
        public Func<double> AudioClock { get; }
        /// <summary>The audio clock now (dspTime or the scripted test clock).</summary>
        public double AudioTime => AudioClock != null ? AudioClock() : AudioSettings.dspTime;
        /// <summary>BootOptions.StartPanel: the menu shows its start panel when the app starts.</summary>
        public bool StartPanel { get; }
        /// <summary>Times Quit was asked for.</summary>
        public int QuitRequests { get; private set; }

        readonly Action _quit;
        readonly List<IAppModule> _modules = new List<IAppModule>();
        readonly Dictionary<Type, List<object>> _services = new Dictionary<Type, List<object>>();
        readonly List<IInstrument> _instruments = new List<IInstrument>();
        readonly List<int> _instrumentMasks = new List<int>();   // per instrument: its ToolMask, -1 = AllPointers (no descriptor)
        bool _instrumentsDirty;
        bool _initialised;

        internal App(AppRoot root, RigBootstrap rig, HandInput handInput, BootOptions options)
        {
            options = options ?? new BootOptions();
            Root = root;
            Rig = rig;
            HandInput = handInput;
            Headless = options.Headless;
            SimulateHeadset = options.SimulateHeadset;
            ManualFrames = options.ManualFrames;
            AudioClock = options.AudioClock;
            Clock.AudioClock = options.AudioClock;
            StartPanel = options.StartPanel;
            _quit = options.Quit;
            Settings = new AppSettings(options.Settings);
            Registry = InstrumentRegistry.Discover(options.InstrumentFilter);
            Settings.Registry = Registry;
            RetiredInstruments.Migrate(Settings);   // an old save's removed instrument (current, My set, its own settings)
            DataPath = string.IsNullOrEmpty(options.DataPath) ? Application.persistentDataPath : options.DataPath;
            var world = new GameObject("World");
            world.transform.SetParent(root.transform, false);
            WorldRoot = world.transform;
            Materials = new MaterialLibrary();
            HoverGlow = new HoverGlowPool(Materials, WorldRoot, Headless);
            Contacts = new ContactVisuals(this, Materials, WorldRoot, Headless, HoverGlow);
            Input = new PointerInput(handInput);
            Claims = new PointerClaims(this);
            HeldTools = new HeldToolGuard(this);
            FastMotion = new FastMotionDriver(this);
            Current = this;
            Register<IInstrumentRegistry>(Registry);
            Register<IHandLook>(Contacts);   // the hands' look in "Play in my room" (the menu's "Real hands" row)
        }

        // ---------------- services ----------------

        /// <summary>Register an implementation under one or more contract types. Later lookups return it.</summary>
        public void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (!_services.TryGetValue(typeof(T), out var list)) _services[typeof(T)] = list = new List<object>();
            if (!list.Contains(service)) list.Add(service);
            if (typeof(T) == typeof(IInstrument)) _instrumentsDirty = true;
            if (service is Rig rig) Registry?.MarkLoaded(InstrumentIds.KindOf(rig.Id));
            ServicesVersion++;
            Claims?.Invalidate();
        }

        public void Unregister<T>(T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var list)) list.Remove(service);
            if (typeof(T) == typeof(IInstrument)) _instrumentsDirty = true;
            ServicesVersion++;
            Claims?.Invalidate();
        }

        /// <summary>First registered implementation of T, else the first module implementing T, else null.</summary>
        public T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var list) && list.Count > 0) return (T)list[0];
            for (int i = 0; i < _modules.Count; i++) if (_modules[i] is T m) return m;
            return null;
        }

        public bool TryGet<T>(out T service) where T : class { service = Get<T>(); return service != null; }

        /// <summary>Like Get but throws with a helpful message: use it in Init for hard dependencies.</summary>
        public T Require<T>() where T : class => Get<T>() ?? throw new InvalidOperationException($"No {typeof(T).Name} registered: check module Order (a lower-order module must register it)");

        public List<T> GetAll<T>() where T : class
        {
            var r = new List<T>();
            if (_services.TryGetValue(typeof(T), out var list)) foreach (var o in list) r.Add((T)o);
            for (int i = 0; i < _modules.Count; i++) if (_modules[i] is T m && !r.Contains(m)) r.Add(m);
            return r;
        }

        /// <summary>Instruments sorted by TickOrder (rebuilt when registrations change).</summary>
        public IReadOnlyList<IInstrument> Instruments
        {
            get
            {
                if (_instrumentsDirty)
                {
                    _instruments.Clear();
                    _instruments.AddRange(GetAll<IInstrument>());
                    _instruments.Sort((a, b) => a.TickOrder.CompareTo(b.TickOrder));
                    _instrumentsDirty = false;
                    _instrumentMasks.Clear();
                    for (int i = 0; i < _instruments.Count; i++)
                    {
                        InstrumentDescriptor d = Registry?.Get(_instruments[i].Kind);
                        _instrumentMasks.Add(d != null && d.Playable ? (int)d.Tools : _instruments[i].AcceptsStickPointers ? -1 : 0);
                    }
                }
                return _instruments;
            }
        }

        // ---------------- lifecycle ----------------

        internal void InitModules(List<IAppModule> modules)
        {
            _modules.Clear();
            _modules.AddRange(modules);
            foreach (var m in _modules)
            {
                try
                {
                    m.Init(this);
                    Debug.Log($"[App] module {m.GetType().Name} ready (order {m.Order})");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[App] module {m.GetType().Name} failed to init: {e}");
                }
            }
            _initialised = true;
            Rig.Recentered += OnRecentered;
            Rig.HmdMountChanged += OnMount;
            Rig.InputFocusChanged += OnInputFocus;
        }

        internal void Shutdown()
        {
            Rig.Recentered -= OnRecentered;
            Rig.HmdMountChanged -= OnMount;
            Rig.InputFocusChanged -= OnInputFocus;
            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                try { _modules[i].Shutdown(); } catch (Exception e) { Debug.LogException(e); }
            }
            Contacts.Dispose();
            HoverGlow.Dispose();
            Materials.DestroyAll();
            if (Current == this) Current = null;
        }

        /// <summary>Close the app: the BootOptions.Quit hook when one is set (tests), else Application.Quit (device) or
        /// leaving Play mode (editor). The caller fades the sound and saves the settings first (the menu's Exit row).</summary>
        public void Quit()
        {
            QuitRequests++;
            Debug.Log("[App] quit requested");
            if (_quit != null) { _quit(); return; }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnRecentered() => Events.Publish(new RecenteredEvent());
        void OnMount(bool mounted) => Events.Publish(new HmdMountEvent { Mounted = mounted });
        void OnInputFocus(bool focused) => Events.Publish(new InputFocusEvent { Focused = focused });

        // ---------------- the frame (spec 05 section 1.1) ----------------

        /// <summary>Tests with BootOptions.ManualFrames: advance the hand source and its pinch / grasp state by dt
        /// (HandInput.Step), the clock by dt (Clock.Step) and run one frame. Deterministic: a finger scripted per Step moves
        /// at exactly the speed the test means, and dwells / timers count scripted time.</summary>
        public void Step(float dt)
        {
            HandInput.Step(dt);
            Clock.Step(dt);
            Frame(fromUnity: false);
        }

        /// <summary>One frame. Called by AppRoot.Update (or App.Step with ManualFrames).</summary>
        public void Frame(bool fromUnity = true)
        {
            if (!_initialised) return;
            if (fromUnity) Clock.Begin();
            float dt = Clock.Dt;

            Run(FramePhase.StickPoses, dt);                 // 1 sticks follow the hands before the pointers are read
            Input.Update(dt, Clock.Now);                    // 2 pointers, pinch/grasp timers, velocities
            try { Claims.Update(); }                        //   (platform 3b) who owns each fingertip this frame
            catch (Exception e) { Debug.LogException(e); }
            try { HeldTools.Update(dt); }                   //   (3c) a tool its hand took elsewhere goes back
            catch (Exception e) { Debug.LogException(e); }
            Run(FramePhase.InputDone, dt);

            TickInstruments(dt);                            // 3 instruments tick + hover
            Run(FramePhase.Instruments, dt);
            Run(FramePhase.Tutorial, dt);                   // 4
            Run(FramePhase.StickHover, dt);                 // 5
            try { Contacts.Update(dt); }                    //   (ARCH step 8a) finger clamp, contact shadows, glow dimming
            catch (Exception e) { Debug.LogException(e); }  //   it calls into the modules' contact surfaces
            HoverGlow.Update((float)Clock.Now);             // 6
            Run(FramePhase.HoverGlow, dt);
            Run(FramePhase.FirstArrange, dt);
            Run(FramePhase.MixedReality, dt);
            Run(FramePhase.Grab, dt);                       // 7 (stick pick-up first, inside Layout's grab pass)
            Run(FramePhase.StickReleases, dt);              // 8
            Run(FramePhase.Listener, dt);                   //   (after the stick releases)
            Run(FramePhase.NodKick, dt);                    // 9
            Run(FramePhase.Recorder, dt);
            Run(FramePhase.Decor, dt);
            Run(FramePhase.Guide, dt);
            Run(FramePhase.Swivel, dt);
            Run(FramePhase.TwoHandSummon, dt);
            Run(FramePhase.PageTurn, dt);
            Run(FramePhase.LayoutAnimation, dt);
            Input.EndFrame();                               // 11 clear pinch/grasp edge flags
            try { FastMotion.Tick(dt); }                    //   E4: the Fast Motion Mode hint (drumming intent)
            catch (Exception e) { Debug.LogException(e); }
            Run(FramePhase.EndFrame, dt);
        }

        void TickInstruments(float dt)
        {
            var grab = Get<IGrabService>();
            var mr = Get<IMixedReality>();
            var arrange = Get<IArrangeMode>();
            bool placing = mr != null && mr.IsPlacing;
            bool arranging = arrange != null && arrange.Arranging;
            Rig held = grab?.HeldRig;
            var all = Instruments;
            if (placing) return;                                                     // MR placing skips every instrument, the menu too
            for (int i = 0; i < all.Count; i++)
            {
                IInstrument inst = all[i];
                if (!inst.IsPresent) continue;                                       // the Menu is always present (it ticks its summon gesture hidden)
                if (arranging && inst.Kind != InstrumentKind.Menu) continue;         // "Move instruments": nothing plays, the menu (and its Done / Reset buttons) still pokes
                if (held != null && held.Instruments.Contains(inst)) continue;       // a grabbed rig does not play
                var pts = PointersOf(i);
                try
                {
                    inst.Tick(pts, dt);
                    inst.Hover(pts, HoverGlow);
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>The pointer list of the i-th instrument (Instruments order): its descriptor's ToolMask (platform.md 4.6), or
        /// AllPointers / HandPointers by AcceptsStickPointers for the deck, the switch panel and the menu.</summary>
        IReadOnlyList<Pointer> PointersOf(int i)
        {
            int m = i < _instrumentMasks.Count ? _instrumentMasks[i] : 0;
            return m < 0 ? Input.AllPointers : Input.PointersFor((ToolMask)m);
        }

        /// <summary>The pointers an instrument of this kind is ticked with (tests, tools).</summary>
        public IReadOnlyList<Pointer> PointersFor(IInstrument inst)
        {
            var all = Instruments;
            for (int i = 0; i < all.Count; i++) if (ReferenceEquals(all[i], inst)) return PointersOf(i);
            return Input.HandPointers;
        }

        void Run(FramePhase phase, float dt)
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                try { _modules[i].Tick(phase, dt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
