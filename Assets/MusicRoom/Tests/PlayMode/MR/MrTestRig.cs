// Shared harness of the MR PlayMode tests: boots the whole app through TestBoot (scripted hands, a fresh
// MemorySettingsStore, a temporary DataPath, a simulated headset so App.IsPresenting is true and the XR-only placing
// paths run), enters mixed reality with passthrough simulated and one of MRUK's recorded JSON rooms. Probes:
//   WeatherProbe      a settable ISnowWeather, registered before Room's (Order 15), so App.Get<ISnowWeather>() finds it;
//   PhaseProbe        records IsPlacing as Layout's Grab phase sees it, one entry per frame;
//   InstrumentProbe   two instruments (Kind Table and Kind Menu) counting their ticks;
//   PianoSurfaceProbe an IPianoApi and the piano's IInstrumentSurface in front of the Piano module's (Order 29) that
//                     record the stand (SetOnSurface / ResetSurface; MR reaches the lead's stand through
//                     IInstrumentSurface since the platform, platform.md 9.3) and forward everything else;
//   ContractLayout    ILayoutService exactly as ARCHITECTURE.md sections 8 and 12 state it (the piano KEEPS its height in
//                     every ArrangeAroundPlayer; PlaceAroundPiano, RestoreHomes, Anchor), in place of whatever Layout module
//                     the build has (the foundation stub resets every height, which hid MR's own height duties);
// Errors and exceptions logged with an [MR] prefix (or thrown from MusicRoom.MR code) fail the test; MRUK's own
// editor-only errors are tolerated.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    /// <summary>A settable ISnowWeather (the Room stub's is quiet: level 0).</summary>
    public sealed class WeatherProbe : AppModule, ISnowWeather
    {
        public override int Order => 15;
        public float Target { get; set; } = 1f;
        public float Level { get; set; } = 1f;
        public float Cover { get; set; }
        public float Wind { get; set; }
        public float WindVel { get; set; } = 0.32f;
        public event Action<SnowGust> OnGust { add { } remove { } }
        public override void Init(App app) => app.Register<ISnowWeather>(this);
        public override void Tick(FramePhase phase, float dt) { if (phase == FramePhase.Decor) Wind += WindVel * dt; }
    }

    /// <summary>Records IMixedReality.IsPlacing as Layout's Grab phase would see it, one entry per frame.</summary>
    public sealed class PhaseProbe : AppModule
    {
        public override int Order => 99;
        public readonly List<bool> PlacingAtGrab = new List<bool>();
        App _app;
        public override void Init(App app) => _app = app;
        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.Grab) PlacingAtGrab.Add(_app.Get<IMixedReality>()?.IsPlacing ?? false);
        }
    }

    /// <summary>Two instruments that count their ticks: one of Kind Table, one of Kind Menu (always present, like the menu).</summary>
    public sealed class InstrumentProbe : AppModule
    {
        public override int Order => 98;
        public readonly Counter Table = new Counter(InstrumentKind.Table, 97);
        public readonly Counter Menu = new Counter(InstrumentKind.Menu, 98);
        public override void Init(App app) { app.Register<IInstrument>(Table); app.Register<IInstrument>(Menu); }

        public sealed class Counter : IInstrument
        {
            public int Ticks;
            readonly List<Renderer> _none = new List<Renderer>();
            public Counter(InstrumentKind kind, int order) { Kind = kind; TickOrder = order; }
            public InstrumentKind Kind { get; }
            public int TickOrder { get; }
            public bool AcceptsStickPointers => false;
            public bool IsPresent => true;
            public Transform Root => null;
            public void Tick(IReadOnlyList<Pointer> pointers, float dt) => Ticks++;
            public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }
            public PlaySurface PlaySurfaceAt(Vector3 world) => PlaySurface.None;
            public IReadOnlyList<Renderer> PlayedParts => _none;
        }
    }

    /// <summary>Stands in front of the Piano module's IPianoApi and its IInstrumentSurface (App.Get returns the first
    /// registered service; MR takes the first surface of the lead's Kind) and records what MR asks of the stand; everything
    /// else goes to the piano. IInstrumentSurface.SetOnSurface(false) counts as a ResetSurface.</summary>
    public sealed class PianoSurfaceProbe : AppModule, IPianoApi, IInstrumentSurface
    {
        public override int Order => 29;
        App _app;
        IPianoApi _piano;
        /// <summary>The stand is hidden (SetOnSurface(true)); ResetSurface shows it.</summary>
        public bool OnSurface { get; private set; }
        public int Resets { get; private set; }
        public override void Init(App app) { _app = app; app.Register<IPianoApi>(this); app.Register<IInstrumentSurface>(this); }
        InstrumentKind IInstrumentSurface.Kind => InstrumentKind.Piano;
        void IInstrumentSurface.SetOnSurface(bool onSurface) { if (onSurface) SetOnSurface(true); else ResetSurface(); }

        IPianoApi Piano
        {
            get
            {
                if (_piano != null) return _piano;
                var all = _app.GetAll<IPianoApi>();
                for (int i = 0; i < all.Count; i++) if (!ReferenceEquals(all[i], this)) return _piano = all[i];
                return null;
            }
        }

        public float W => Piano.W;
        public float BW => Piano.BW;
        public float BTOP => Piano.BTOP;
        public float L => Piano.L;
        public Transform Root => Piano.Root;
        public bool InPlayZone(Vector3 world) => Piano.InPlayZone(world);
        public bool OverKeys(Vector3 world) => Piano.OverKeys(world);
        public bool TryGetKey(int midi, out PianoKeyInfo key) => Piano.TryGetKey(midi, out key);
        public void PressMidi(int midi, object pointerKey, float vel = 0.75f) => Piano.PressMidi(midi, pointerKey, vel);
        public void ReleaseMidi(int midi, object pointerKey) => Piano.ReleaseMidi(midi, pointerKey);
        public void PlaybackOn(int midi, float vel) => Piano.PlaybackOn(midi, vel);
        public void PlaybackOff(int midi) => Piano.PlaybackOff(midi);
        public void PlaybackReleaseAll() => Piano.PlaybackReleaseAll();
        public void SetOnSurface(bool onTable) { OnSurface = onTable; Piano.SetOnSurface(onTable); }
        public void ResetSurface() { OnSurface = false; Resets++; Piano.ResetSurface(); }
        public int HeldKeyCount => Piano.HeldKeyCount;
    }

    /// <summary>The Layout contract (ARCHITECTURE.md 8, 12) without the resolver or any grabbing: seat-frame homes, the
    /// piano keeping its height in ArrangeAroundPlayer (placement keeps it; MR resets it before its first arrange),
    /// Layout's PlaceAroundPiano / RestoreHomes / anchor, membership by mode. Order 100 like Layout, so every rig is in.</summary>
    public sealed class ContractLayout : AppModule, IRigRegistry, ILayoutService, IGrabService, IPlacementService
    {
        public static readonly Vector3 OriginSeat = new Vector3(0f, 1.2f, 0f);
        public override int Order => 100;
        App _app;
        readonly List<Rig> _rigs = new List<Rig>();
        public event Action Changed;
        public int Arranges { get; private set; }

        public override void Init(App app)
        {
            _app = app;
            _rigs.AddRange(app.GetAll<Rig>());
            Mode = app.Settings.LayoutMode;
            Current = app.Settings.CurrentInstrument;
            app.Register<IRigRegistry>(this);
            app.Register<ILayoutService>(this);
            app.Register<IGrabService>(this);
            app.Register<IPlacementService>(this);
            // As the real Layout: every rig at its home about the default seat, the piano at its home height.
            ApplyMembership();
            foreach (var r in _rigs) PoseRig(r, new SeatFrame { Head = OriginSeat }, keepPianoY: false);
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.FirstArrange && !FirstArrangeDone && _app.Head != null && _app.Rig.State != XrState.Starting) ArrangeAroundPlayer(true);
        }

        // IRigRegistry
        public IReadOnlyList<Rig> Rigs => _rigs;
        public Rig Get(RigId id) { for (int i = 0; i < _rigs.Count; i++) if (_rigs[i].Id == id) return _rigs[i]; return null; }
        public void Register(Rig rig) { if (rig != null && !_rigs.Contains(rig)) { _rigs.Add(rig); Changed?.Invoke(); } }
        public void Unregister(Rig rig) { if (_rigs.Remove(rig)) Changed?.Invoke(); }

        // ILayoutService
        public LayoutMode Mode { get; private set; }
        public InstrumentKind Current { get; private set; }
        public bool IsTurning => false;
        public bool FirstArrangeDone { get; private set; }
        public SeatFrame Seat { get; private set; }

        public void Set(LayoutMode mode, InstrumentKind instrument)
        {
            Mode = mode;
            Current = instrument == InstrumentKind.Drums || instrument == InstrumentKind.Harp ? instrument : InstrumentKind.Piano;
            ApplyMembership();
            ArrangeAroundPlayer(false);
            _app.Events.Publish(new LayoutChangedEvent { Mode = Mode, Current = Current });
        }

        public void Next(int direction) { }
        public bool IsInScene(InstrumentKind instrument) => Mode == LayoutMode.All || instrument == Current || instrument == InstrumentKind.Table;

        public void ArrangeAroundPlayer(bool force, bool movePiano = true)
        {
            if (_app.Head == null) return;
            Arranges++;
            ApplyMembership();
            Seat = SeatFrame.From(_app.Head);
            foreach (var r in _rigs)
                if (r.Id != RigId.Piano || movePiano) PoseRig(r, Seat, keepPianoY: true);
            FirstArrangeDone = true;
            _app.Input.ResetMotion();
            _app.Events.Publish(new ArrangedEvent { Forced = force });
        }

        public void PlaceAroundPiano()
        {
            var piano = Get(RigId.Piano);
            if (piano?.Group == null) return;
            ApplyMembership();
            float yaw = piano.Group.eulerAngles.y - piano.HomeYawAll;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (var r in _rigs)
            {
                if (r == piano || r.Group == null || !r.InScene) continue;
                Vector3 d = r.HomeAll - piano.HomeAll;
                Vector3 p = piano.Group.position + rot * new Vector3(d.x, 0f, d.z);
                p.y = r.HomeAll.y;
                r.Group.SetPositionAndRotation(p, Quaternion.Euler(0f, r.HomeYawAll + yaw, 0f));
                if (r.Id != RigId.Lessons && !r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
            }
            _app.Input.ResetMotion();
        }

        public void RestoreHomes()
        {
            ApplyMembership();
            foreach (var r in _rigs) PoseRig(r, new SeatFrame { Head = OriginSeat }, keepPianoY: false);
            _app.Input.ResetMotion();
        }

        public Pose Anchor
        {
            get
            {
                RigId id = Mode == LayoutMode.All || Current == InstrumentKind.Piano ? RigId.Piano : Current == InstrumentKind.Drums ? RigId.Drums : RigId.Harp;
                var g = Get(id)?.Group;
                return g != null ? new Pose(g.position, g.rotation) : Pose.identity;
            }
        }

        void PoseRig(Rig r, SeatFrame seat, bool keepPianoY)
        {
            if (r.Group == null || !r.InScene) return;
            bool single = Mode == LayoutMode.Single && r.Id != RigId.Table && r.Id != RigId.Lessons;
            Vector3 p = seat.Place(single ? r.HomeSingle : r.HomeAll);
            if (keepPianoY && r.Id == RigId.Piano) p.y = r.Group.position.y;
            r.Group.SetPositionAndRotation(p, Quaternion.Euler(0f, seat.Yaw(single ? r.HomeYawSingle : r.HomeYawAll), 0f));
            if (r.Id != RigId.Lessons && !r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
        }

        void ApplyMembership()
        {
            foreach (var r in _rigs)
            {
                if (r.Group == null) continue;
                bool inScene = r.Id == RigId.Table || r.Id == RigId.Lessons || Mode == LayoutMode.All
                    || (r.Id == RigId.Piano && Current == InstrumentKind.Piano)
                    || (r.Id == RigId.Drums && Current == InstrumentKind.Drums)
                    || (r.Id == RigId.Harp && Current == InstrumentKind.Harp);
                r.InScene = inScene;
                if (r.Id != RigId.Lessons && r.Group.gameObject.activeSelf != inScene) r.Group.gameObject.SetActive(inScene);
            }
        }

        // IGrabService / IPlacementService: nothing held, nothing to resolve.
        public Rig HeldRig => null;
        public bool AnyHeld => false;
        public Rig GrabTarget(Vector3 point, float reach = 0.07f) => null;
        public bool Resolve(bool useRoomBox) => true;
        public void SettleOne(Rig rig) { }
        public void ClearPanel(Rig panel) { }
        public Vector3? RoomShift() => null;
        public float EyeDistance(Rig rig) => float.PositiveInfinity;
    }

    public abstract class MrTestRig
    {
        public const string RoomsJson = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json/";
        protected TestBoot Boot;
        protected FakeHandSource Fake => Boot.Hands;
        protected WeatherProbe Weather;
        protected PhaseProbe Phases;
        protected InstrumentProbe Probes;
        protected PianoSurfaceProbe Stand;
        protected ContractLayout Layout;
        protected App App;
        protected MrModule Mr;
        readonly List<string> _errors = new List<string>();
        readonly List<MixedRealityEvent> _mrEvents = new List<MixedRealityEvent>();
        protected IReadOnlyList<MixedRealityEvent> MrEvents => _mrEvents;

        protected virtual bool Headless => true;
        /// <summary>Boot with the contract Layout above instead of the build's Layout module.</summary>
        protected virtual bool UseContractLayout => true;
        /// <summary>A Layout stand-in of the test's own in place of ContractLayout (UseContractLayout must be true); null: the
        /// ContractLayout.</summary>
        protected virtual AppModule MakeLayoutStandIn() => null;
        /// <summary>MrModule.TableOnEnter for this suite. Entering mixed reality opens the instruments in front of the player
        /// on the floor; the suites written for the automatic best-table placement (TableFinder and the piano on the desk)
        /// keep it on to go on testing that path. The in-front suite (MrInFrontTests) turns it off, as the app has it.</summary>
        protected virtual bool TableOnEnter => true;
        /// <summary>Values put in the fresh settings store before the boot.</summary>
        protected virtual void Prepare(MemorySettingsStore settings) { }

        [UnitySetUp]
        public IEnumerator BootApp()
        {
            LogAssert.ignoreFailingMessages = true; // MRUK's editor-only errors; ours are caught by OnLog below
            _errors.Clear();
            _mrEvents.Clear();
            Application.logMessageReceived += OnLog;
            MrDevOptions.Reset();
            MrDevOptions.SimulatePassthroughOverride = true;
            Boot = new TestBoot(headless: Headless, simulateHeadset: true);
            Prepare(Boot.Settings);
            Weather = new WeatherProbe();
            Phases = new PhaseProbe();
            Probes = new InstrumentProbe();
            Stand = new PianoSurfaceProbe();
            Boot.Options.ExtraModules.Add(Weather);
            Boot.Options.ExtraModules.Add(Phases);
            Boot.Options.ExtraModules.Add(Probes);
            Boot.Options.ExtraModules.Add(Stand);
            Layout = null;
            if (UseContractLayout)
            {
                AppModule standIn = MakeLayoutStandIn() ?? (Layout = new ContractLayout());
                Boot.Options.ModuleFilter = t => t.Namespace == null || !t.Namespace.StartsWith("MusicRoom.Layout", StringComparison.Ordinal);
                Boot.Options.ExtraModules.Add(standIn);
            }
            yield return Boot.Start();
            App = App.Current;
            Mr = (MrModule)App.Get<IMixedReality>();
            Assert.That(Mr, Is.Not.Null);
            Mr.TableOnEnter = TableOnEnter;
            Assert.That(App.IsPresenting, Is.True, "simulated headset: the XR-only paths run");
            App.Events.Subscribe<MixedRealityEvent>(OnMrEvent);
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            yield return null; // first arrange
        }

        [UnityTearDown]
        public IEnumerator TearDownApp()
        {
            // Never destroy MRUK mid-discovery: its native context outlives the component and the next test would inherit it.
            yield return WaitFor(() => Mr == null || !Mr.Loading);
            if (Mr != null && Mr.Active) Mr.Exit();
            if (App != null && App.Current == App) App.Events.Unsubscribe<MixedRealityEvent>(OnMrEvent);
            yield return Boot.Stop();
            if (MRUK.Instance != null) UnityEngine.Object.Destroy(MRUK.Instance.gameObject);
            yield return null;
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = false;
            MrDevOptions.Reset();
            var errors = new List<string>(_errors);
            _errors.Clear();
            Assert.That(errors, Is.Empty, "errors logged by the MR module");
        }

        void OnLog(string message, string stack, LogType type)
        {
            bool ours = message.StartsWith("[MR]") || (stack != null && stack.Contains("MusicRoom.MR."));
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && ours)
                _errors.Add(type + ": " + message.Split('\n')[0]);
        }

        void OnMrEvent(MixedRealityEvent e) => _mrEvents.Add(e);

        protected static string RoomJson(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<TextAsset>(RoomsJson + name + ".json");
            Assert.That(t, Is.Not.Null, name);
            return t.text;
        }

        /// <summary>Enters mixed reality in one of MRUK's recorded rooms and waits for the load and the arrange.</summary>
        protected IEnumerator EnterRoom(string name)
        {
            MrDevOptions.SceneJson = RoomJson(name);
            var task = Mr.Enter();
            yield return WaitFor(() => !Mr.Loading && task.IsCompleted);
            Assert.That(task.IsCompleted, "the scene load finished");
            Assert.That(Mr.Status, Is.EqualTo(MrStatus.Loaded), name + " loaded");
            yield return null; // MixedReality tick: arrange + best table
            yield return null;
            yield return null;
        }

        /// <summary>Waits by time, not frames (batchmode frames are short; MRUK loads finish on worker threads that slow
        /// down when other editors share the CPU).</summary>
        protected static IEnumerator WaitFor(Func<bool> done, float seconds = 20f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        /// <summary>Runs app frames on a scripted clock (no Unity frame): only while no pinch edge is pending.</summary>
        protected void StepFrames(int frames, float dt = 0.05f)
        {
            for (int i = 0; i < frames; i++)
            {
                App.Clock.Step(dt);
                App.Frame(false);
            }
        }

        protected static MRUKRoom Room => MRUK.Instance != null ? (MRUK.Instance.GetCurrentRoom() ?? MRUK.Instance.Rooms[0]) : null;

        /// <summary>The right hand with its aim ray (FakeHandSource: from the index knuckle along the wrist's +z) toward a point.</summary>
        protected static HandPose AimRightAt(Vector3 wrist, Vector3 target, float pinchGap = 0.05f)
        {
            // The ray starts at the index knuckle: re-aim twice so the knuckle's own offset is accounted for.
            Quaternion rot = Quaternion.LookRotation((target - wrist).normalized, Vector3.up);
            for (int k = 0; k < 3; k++)
            {
                var probe = HandPose.Open(HandSide.Right, wrist, rot);
                Vector3 knuckle = probe.Position[(int)HandJoint.IndexProximal];
                rot = Quaternion.LookRotation((target - knuckle).normalized, Vector3.up);
            }
            var pose = HandPose.Open(HandSide.Right, wrist, rot).WithPointing();
            return pose.WithPinch(pinchGap);
        }

        /// <summary>A still right hand at the index tip <paramref name="tip"/>, pinched or open (gap 0 / 5 cm).</summary>
        protected static HandPose RightAt(Vector3 tip, bool pinch)
        {
            var pose = HandPose.Open(HandSide.Right, tip + new Vector3(0.02f, -0.03f, -0.15f), Quaternion.identity).WithPointing();
            pose = pose.WithIndexTipAt(tip);
            return pose.WithPinch(pinch ? 0f : 0.05f);
        }

        protected Rig Rig(RigId id) => App.Get<IRigRegistry>().Get(id);
    }
}
#endif
