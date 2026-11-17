// Mixed reality module (Order 70): "Play in my room" (spec 03, spec 07 section 9, ARCHITECTURE.md section 12).
//   Enter: passthrough on (simulated off-headset, MrDevOptions) -> the player rig back at the origin (ResetPlayerRig)
//   -> virtual room, its lights and candles hidden -> fire and wind off, snow stays -> audio MR mode -> MR lights ->
//   USE_SCENE permission + device scene load (editor: a sample JSON room) -> world lock when Loaded -> the audio room
//   box on the real room. On the next MixedReality tick (AutoArrange) the piano gets its stand and its home height
//   back and Layout lays everything out in front of the player on the real floor (no room box, no fitSeat in MR; no saved
//   anchor and no automatic table: TableOnEnter, off, keeps the old best-table placement for the tests of TableFinder).
//   Then (tools and tests only since 2026-11-10: the empty-air pinch that started it is retired, instruments move only
//   with Move instruments) StartPlacing: a reticle follows where the hand points across tables and floor, the next pinch
//   puts the piano there (PlaceLead). The snow (the SNOW switch) falls all round the player: Room's room snow, which
//   reads FloorHeight (the snowy window and the snow behind real windows were removed 2026-11-13). The MR
//   sun looks at the layout anchor and the shadow catcher lies on the real surface under it. Focus regained reloads the
//   scene only when it may have changed (no room was loaded, or the device's rooms differ from the loaded ones).
//   Placing never traps the player: it ends by itself after PlacingTimeoutSeconds, on HMD unmount, input focus loss,
//   recentre and the menu's summon gesture (CancelPlacing), and every end without a landing has Layout show the hidden
//   rigs and the menu again (ILayoutService.RestoreAfterPlacing).
//   Exit: everything above undone, the VR layout restored (ILayoutService.RestoreHomes, then ArrangeAroundPlayer).
// The many-instrument platform (12-new-instruments/platform.md 9): "the piano" above is the LEAD instrument,
// ILayoutService.Lead (the My set member in slot Front, else the first in carousel order; One alone: Current). The
// auto-arrange stand height, the best table (a lead whose descriptor has Mr.OnTable, lifted by Mr.SurfaceLift, its
// IInstrumentSurface hiding its stand; a floor-only lead stays on the floor in front of the player), the placing pinch,
// PlaceAroundLead, the light and the shadow catcher all use the lead's rig and descriptor. The E9 anchor
// (PianoAnchorStore) and the piano's tested table pose stay the piano's: another lead is placed fresh each visit. MR
// never shows a rig Layout keeps out of the scene (displayed instruments stay hidden), and its draw guard puts the last
// member of My set away while the at-seat instruments' DrawsAtSeat + 37 exceed 80. MR names no instrument but that
// one (AnchorInstrument).
// Never moves MRUKRoom / MRUKAnchor transforms: the reticle and the catcher live under World/MixedReality.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.MR
{
    public sealed class MrModule : AppModule, IMixedReality
    {
        public const int ModuleOrder = 70;
        public override int Order => ModuleOrder;
        /// <summary>ARCHITECTURE.md 12: Enter waits at most this long while the passthrough init it started is still pending
        /// (until it completes the underlay is not submitted, and the hidden room would show black). 4 s since passthrough
        /// is initialised on entering instead of at start-up (burst.md section 10.1; the init time is UNVERIFIED #27).</summary>
        public const float PassthroughWaitSeconds = 4f;
        /// <summary>A Layout re-arrange that moves the piano more than this off its table pose took it off the table.</summary>
        public const float LeftTableDistance = 0.02f;
        /// <summary>After such a move the piano still rests on a real top when one lies this close under its body.</summary>
        public const float RestsOnTopTolerance = 0.03f;

        App _app;
        Transform _root;
        MrRuntime _runtime;
        MrMaterials _materials;
        PianoReticle _reticle;
        MrLighting _lighting;
        MrHint _hint;
        EnvironmentRaycastManager _env;
        readonly List<Rig> _hiddenRigs = new List<Rig>();
        readonly HandRay[] _rays = new HandRay[2];
        Action<ArrangedEvent> _onArranged;
        Action<LayoutChangedEvent> _onLayout;
        Action<InstrumentMoveEvent> _onMove;
        Action<MrSceneStatus> _onStatus;
        Action<HmdMountEvent> _onMount;
        Action<InputFocusEvent> _onFocus;
        Action<RecenteredEvent> _onRecentered;
        double _placingSince;

        bool _simulated, _createdMruk, _fireWas, _windWas, _entering, _shutDown;
        int _session;
        bool _loading, _arrangePending, _tablePending, _ownArrange, _standFloorPending;
        bool _placing, _consumedThisFrame, _hasHit, _envUsed;
        int _placingHand = -1;
        Vector3 _hit;
        /// <summary>The lead stands on a real table (its stand hidden): which one, and where.</summary>
        bool _leadOnTable, _userPlaced;
        InstrumentKind _tableLead;
        Pose _tablePose;
        string _transientHint;
        double _transientUntil;
        Vector3 _anchorPos;
        float _anchorYaw = float.NaN;
        Transform _anchor;
        MRUKRoom _room;
        // E9: the remembered piano pose (local spatial anchor), headset only.
        PianoAnchorStore _anchors;
        /// <summary>E9: the piano anchor store (tests, diagnostics).</summary>
        public PianoAnchorStore PianoAnchors => _anchors;
        /// <summary>Saved settings of the removed snowy window cleared on this boot (RetiredWindow.Migrate; tests).</summary>
        public int RetiredWindowSettings { get; private set; }

        // ---------------- lifecycle ----------------

        public override void Init(App app)
        {
            _app = app;
            var go = new GameObject("MixedReality");
            go.transform.SetParent(app.WorldRoot, false);
            _root = go.transform;
            _runtime = go.AddComponent<MrRuntime>();
            _runtime.FocusChanged = OnApplicationFocusChanged;
            _materials = new MrMaterials(app.Materials);
            // The snowy window is gone (2026-11-13): an older save's kept window (its pose, its spatial anchor) is forgotten.
            RetiredWindowSettings = RetiredWindow.Migrate(app.Settings, app.Rig != null && app.Rig.IsXrActive);
            _reticle = new PianoReticle(_root, _materials);
            _lighting = new MrLighting(_root, _materials);
            _anchors = new PianoAnchorStore(app.Settings);
            _onArranged = OnArranged;
            app.Events.Subscribe(_onArranged);
            // My set changing in mixed reality (a tile, a lesson) without a re-arrange: the draw guard looks again.
            _onLayout = _ => { if (Active) GuardDraws(); };
            _onMove = e => { if (Active && e.Phase == MovePhase.Arrived) GuardDraws(); };
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onMove);
            // Placing ends when the player cannot finish it: the headset off, the system menu up, a recentre.
            _onMount = e => { if (!e.Mounted) CancelPlacing("headset off"); };
            _onFocus = e => { if (!e.Focused) CancelPlacing("input focus lost"); };
            _onRecentered = _ => CancelPlacing("recentred");
            app.Events.Subscribe(_onMount);
            app.Events.Subscribe(_onFocus);
            app.Events.Subscribe(_onRecentered);
            // MrScene is static and outlives this App (tests boot many): the handler is kept to remove it in Shutdown.
            _onStatus = OnSceneStatus;
            MrScene.StatusChanged += _onStatus;
            app.Register<IMixedReality>(this);
            // The real walls for the Room's wall paint (the scene model's, else the depth sensor's): MrPaintWalls.cs.
            app.Register<IRealWalls>(new MrPaintWalls(CurrentRoom, PaintDepthWall));
            string pt = XrMode.PassthroughAvailable(app.Rig) ? "available" : XrMode.PassthroughPending(app.Rig) ? "pending"
                : MrDevOptions.SimulatePassthrough ? "simulated" : "n/a";
            Debug.Log($"[MR] ready: passthrough={pt}");
        }

        public override void Shutdown()
        {
            _shutDown = true;
            // App.Shutdown runs in reverse Order: Layout (100) is already gone, so only the state that outlives the App is
            // put back here (lights, MRUK world lock and scene, passthrough / clear colour); no service calls, no events.
            if (Active) ExitCore(notify: false);
            if (_onStatus != null) MrScene.StatusChanged -= _onStatus;
            _onStatus = null;
            if (_onArranged != null) _app.Events.Unsubscribe(_onArranged);
            if (_onLayout != null) _app.Events.Unsubscribe(_onLayout);
            if (_onMove != null) _app.Events.Unsubscribe(_onMove);
            if (_onMount != null) _app.Events.Unsubscribe(_onMount);
            if (_onFocus != null) _app.Events.Unsubscribe(_onFocus);
            if (_onRecentered != null) _app.Events.Unsubscribe(_onRecentered);
            _reticle?.Destroy();
            _lighting?.Destroy();
            _hint?.Destroy();
            _materials?.DestroyOwn();
            if (_createdMruk && MRUK.Instance != null) MrUtil.Destroy(MRUK.Instance.gameObject);
        }

        // ---------------- IMixedReality ----------------

        public bool Available => XrMode.PassthroughAvailable(_app.Rig) || XrMode.PassthroughPending(_app.Rig)
                                 || XrMode.PassthroughStartable(_app.Rig) || MrDevOptions.SimulatePassthrough;
        public bool Active { get; private set; }
        /// <summary>Placing the piano, or the frame a pinch was used to place it.</summary>
        public bool IsPlacing => Active && (_placing || _consumedThisFrame);
        public MrStatus Status => (MrStatus)(int)MrScene.Status;
        public bool HasSpaceSetup => MrScene.Status == MrSceneStatus.Loaded;

        public async Task Enter()
        {
            if (Active || _entering) return;
            // Passthrough starts only now (burst.md section 10.1: the VR room runs without it, at CPU level 4).
            XrMode.StartPassthrough(_app.Rig);
            if (XrMode.PassthroughPending(_app.Rig))
            {
                // Passthrough is initialised asynchronously; entering now would hide the room over a black underlay. Wait
                // for it (bounded: a stuck init must not swallow the menu tap). The VR room stays up meanwhile.
                _entering = true;
                float t0 = Time.realtimeSinceStartup;
                try
                {
                    while (!_shutDown && XrMode.PassthroughPending(_app.Rig) && Time.realtimeSinceStartup - t0 < PassthroughWaitSeconds)
                        await Task.Yield();
                }
                finally { _entering = false; }
                if (_shutDown || Active) return;
                Debug.Log($"[MR] passthrough init {(XrMode.PassthroughPending(_app.Rig) ? "still pending" : "done")} after {Time.realtimeSinceStartup - t0:F2}s");
            }
            bool real = XrMode.SetPassthrough(_app.Rig, true);
            if (!real)
            {
                if (!MrDevOptions.SimulatePassthrough) { XrMode.StopPassthrough(_app.Rig); Debug.Log("[MR] passthrough unavailable"); return; }
                SetSimulatedBackground(true);
            }
            _simulated = !real;
            ResetPlayerRig();
            Active = true;
            int session = ++_session;
            _placing = _consumedThisFrame = _hasHit = _userPlaced = _leadOnTable = _tablePending = _standFloorPending = false;
            _arrangePending = true;
            _anchorYaw = float.NaN;
            _transientHint = null;

            var room = _app.Get<IRoom>();
            if (room != null) { room.SetVisible(false); room.SetCandles(false); }
            var switches = _app.Get<IAmbienceSwitches>();
            if (switches != null)
            {
                // Fire and wind belong to the virtual room; snow stays and falls all round the player (Room's room snow).
                _fireWas = switches.Get(AmbienceKind.Fire);
                _windWas = switches.Get(AmbienceKind.Wind);
                switches.Set(AmbienceKind.Fire, false);
                switches.Set(AmbienceKind.Wind, false);
            }
            var audio = _app.Get<IAudioEngine>();
            if (audio != null)
            {
                audio.SetMixedReality(true);
                // The snow falls all round the player now: its sound from just above the head, the wind a little higher.
                Vector3 snow = (_app.Head != null ? _app.Head.position : Vector3.zero) + Vector3.up * MrConstants.SnowSourceAbove;
                audio.SetSourcePosition(SourceId.Snow, snow);
                audio.SetSourcePosition(SourceId.Wind, snow + Vector3.up * MrConstants.WindSourceRise);
            }
            _lighting.Enter();
            Debug.Log($"[MR] enter: passthrough={(real ? "on" : "simulated")}");
            _app.Events.Publish(new MixedRealityEvent { Active = true });
            _createdMruk |= MRUK.Instance == null;
            // The instruments open in front of the player (AutoArrange): a piano anchor saved by an earlier version is never
            // brought back (it put the piano wherever it was last time, often far across the room); it is forgotten.
            if (_anchors.HasSaved) _anchors.Forget(_app.Rig.IsXrActive);
            await LoadSceneAsync(session);
        }

        public void Exit() => ExitCore(notify: true);

        /// <summary>Leaves mixed reality. <paramref name="notify"/> false (module shutdown): only the state that outlives
        /// the App goes back; the other modules, which are shutting down too, are neither called nor notified.</summary>
        void ExitCore(bool notify)
        {
            if (!Active) return;
            Active = false;
            _session++;
            EndPlacing(restoreRigs: notify);
            _consumedThisFrame = _arrangePending = _tablePending = false;
            _lighting.Exit();
            _transientHint = null;
            Hint = null;
            _hint?.Set(null, null);
            if (_env != null) _env.enabled = false;
            MrScene.SetWorldLock(false);
            // MRUK must not be cleared while its discovery runs (its native lib then loses track of the rooms); a load in
            // flight clears the scene itself when it lands (LoadSceneAsync).
            if (!_loading) MrScene.Clear();
            _room = null;
            _anchors?.ReleaseBound();
            if (_simulated) SetSimulatedBackground(false);
            else XrMode.SetPassthrough(_app.Rig, false);
            XrMode.StopPassthrough(_app.Rig);   // shut it down: the VR room runs without passthrough (burst.md section 10.1)
            _simulated = false;
            _leadOnTable = _userPlaced = false;
            if (!notify)
            {
                Debug.Log("[MR] exit (shutdown)");
                return;
            }

            var room = _app.Get<IRoom>();
            if (room != null) { room.SetVisible(true); room.SetCandles(true); }
            var switches = _app.Get<IAmbienceSwitches>();
            if (switches != null) { switches.Set(AmbienceKind.Fire, _fireWas); switches.Set(AmbienceKind.Wind, _windWas); }
            var audio = _app.Get<IAudioEngine>();
            if (audio != null)
            {
                audio.SetMixedReality(false);
                audio.SetRoomBox(AudioModule.VrRoomCenter, AudioModule.VrRoomSize, false);
                audio.SetSourcePosition(SourceId.Snow, MrConstants.SnowSourceVr);
                audio.SetSourcePosition(SourceId.Wind, MrConstants.WindSourceVr);
            }
            ResetSurfaces();
            Debug.Log("[MR] exit");
            _app.Events.Publish(new MixedRealityEvent { Active = false });
            // Session end: layout.restoreHomes; Unity stays in XR, so the layout then comes to the player.
            var layout = _app.Get<ILayoutService>();
            layout?.RestoreHomes();
            layout?.ArrangeAroundPlayer(true);
        }

        public async Task RunSpaceSetup()
        {
            if (_loading) return;
            // OVRScene.RequestSpaceSetup is a real native call in the macOS editor too: headset only.
            if (_app.Rig == null || !_app.Rig.IsXrActive) { Debug.Log("[MR] Space Setup needs the headset"); return; }
            _loading = true;
            Loads++;
            MrSceneStatus s;
            try { s = await MrScene.RunSpaceSetupThenReloadAsync(); }
            catch (Exception e) { Debug.LogException(e); s = MrSceneStatus.Failed; }
            finally { _loading = false; }
            if (Active) OnLoaded(s);
            else MrScene.Clear(); // left mixed reality meanwhile: the room comes back on the next entry
        }

        public bool TryGetPianoPose(out Pose pose, out bool onTable)
        {
            pose = default;
            onTable = false;
            var room = CurrentRoom();
            if (room == null || _app.Head == null) return false;
            if (!TableFinder.TryFindBest(room, _app.Head.position, _app.Head.forward, TableQuery.Default, out var best)) return false;
            pose = TableFinder.PianoPoseOnTable(best, best.EdgeMidpoint);
            onTable = true;
            return true;
        }

        /// <summary>platform.md 9.2-9.3: the piano keeps its tested table pose (TryGetPianoPose). Another lead whose descriptor
        /// has Mr.OnTable goes on the best real table that holds its footprint (TableFinder with its width and depth), its
        /// play centre PushBack in from the near edge as far as the top allows, lifted by Mr.SurfaceLift, facing the player:
        /// onTable true; false when there is no such table. A floor-only lead's pose is its seat home about the head on the
        /// real floor (onTable false, true returned).</summary>
        public bool TryGetLeadPose(InstrumentKind lead, out Pose pose, out bool onTable)
        {
            if (lead == AnchorInstrument) return TryGetPianoPose(out pose, out onTable);
            pose = Pose.identity;
            onTable = false;
            InstrumentDescriptor d = Describe(lead);
            if (d?.Seat == null || _app.Head == null) return false;
            if (d.Mr != null && d.Mr.OnTable)
            {
                var room = CurrentRoom();
                if (room == null) return false;
                Footprint(d, out float minX, out float maxX, out float minZ, out float maxZ);
                TableQuery q = TableQuery.Default;
                q.wantedEdgeLength = maxX - minX;
                q.minEdgeLength = Mathf.Min(q.minEdgeLength, maxX - minX + 2f * TableFinder.EdgeMargin);
                q.minDepth = maxZ - minZ + TableFinder.EdgeMargin;
                if (!TableFinder.TryFindBest(room, _app.Head.position, _app.Head.forward, q, out var best)) return false;
                pose = LeadPoseOnTable(d, best, best.EdgeMidpoint, minZ, maxZ);
                onTable = true;
                return true;
            }
            var layout = Layout;
            bool single = layout != null && layout.Mode == LayoutMode.Single;
            Vector3 home = single ? d.Seat.Single : d.Seat.Home;
            SeatFrame seat = SeatFrame.From(_app.Head);
            Vector3 p = seat.Place(home);
            p.y = FloorY() + home.y;
            pose = new Pose(p, Quaternion.Euler(0f, seat.Yaw(single ? d.Seat.SingleYaw : d.Seat.HomeYaw), 0f));
            return true;
        }

        /// <summary>A table-top lead on a top (TableFinder.PianoPoseOnTable for any footprint): its play centre PushBack
        /// beyond the chosen point, less on a shallow top so its far side stays EdgeMargin before the far edge (never toward
        /// the player past its near side), lifted by its SurfaceLift, facing the player.</summary>
        static Pose LeadPoseOnTable(InstrumentDescriptor d, in TableSurface s, Vector3 chosen, float minZ, float maxZ)
        {
            Vector3 dd = chosen - s.EdgeMidpoint;
            dd.y = 0f;
            float inset = Mathf.Max(0f, -Vector3.Dot(dd, s.TowardUser));   // how far the point lies in from the near edge
            float want = Mathf.Max(MrConstants.PushBack - d.Seat.PlayCentre.y, -minZ + TableFinder.EdgeMargin - inset);
            float room = s.Depth - inset - maxZ - TableFinder.EdgeMargin;
            float push = Mathf.Clamp(want, 0f, Mathf.Max(0f, room));
            Vector3 p = new Vector3(chosen.x, s.TopY + d.Mr.SurfaceLift, chosen.z) - s.TowardUser * push;
            return new Pose(p, s.FacingUser);
        }

        /// <summary>The descriptor's footprint (its non-air seat boxes) on the rig-local floor plane.</summary>
        static void Footprint(InstrumentDescriptor d, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = minZ = float.PositiveInfinity;
            maxX = maxZ = float.NegativeInfinity;
            if (d?.Seat?.Boxes != null)
                foreach (FootBox b in d.Seat.Boxes)
                {
                    if (b.Air) continue;
                    minX = Mathf.Min(minX, b.Cx - b.Hx); maxX = Mathf.Max(maxX, b.Cx + b.Hx);
                    minZ = Mathf.Min(minZ, b.Cz - b.Hz); maxZ = Mathf.Max(maxZ, b.Cz + b.Hz);
                }
            if (minX > maxX) { minX = minZ = -0.2f; maxX = maxZ = 0.2f; }
        }

        /// <summary>IMixedReality.StartPlacing: Layout's Grab step 5 calls it for a dwell-complete
        /// pinch in empty air, having hidden the other rigs. The placing hand is the one pinching longest. Unconditional
        /// apart from needing mixed reality and an XR session (an isPresenting path, ARCHITECTURE.md 3.3).</summary>
        public void StartPlacing()
        {
            if (!Active || _placing || !_app.IsPresenting) return;
            BeginPlacingWith(PinchingHand());
        }

        /// <summary>IMixedReality.CancelPlacing: placing ends without a landing (the timeout, the headset off, input focus
        /// lost, a recentre, the menu's summon gesture, a pinch with no surface): the rigs MR hid come back, and Layout shows
        /// what it hid (the menu's group too). The frame is used up like a landing's.</summary>
        public void CancelPlacing(string why)
        {
            if (!_placing) return;
            EndPlacing();
            _consumedThisFrame = true;
            PlacingCancels++;
            Layout?.RestoreAfterPlacing();
            _app.Input.ResetMotion();
            Debug.Log($"[MR] placing cancelled: {why}");
        }

        /// <summary>Placing ends by itself after this long (MrConstants.PlacingTimeoutSeconds; tests shorten it).</summary>
        public float PlacingTimeout = MrConstants.PlacingTimeoutSeconds;

        /// <summary>Placings that ended without a landing (CancelPlacing).</summary>
        public int PlacingCancels { get; private set; }

        // ---------------- inspection (tests, tools) ----------------

        public PianoReticle PianoReticle => _reticle;
        public MrLighting Lighting => _lighting;
        /// <summary>The hint shown in the headset now (null: none).</summary>
        public string Hint { get; private set; }
        public bool HintVisible => _hint != null && _hint.Visible;
        public Transform HintTransform => _hint?.Transform;
        /// <summary>The lead stands on a real table (the piano in the default set; the name is the tests' since ARCHITECTURE 12).</summary>
        public bool PianoOnTable => _leadOnTable;
        public bool LeadOnTable => _leadOnTable;
        /// <summary>Which instrument stands on the real table (valid while LeadOnTable).</summary>
        public InstrumentKind TableLead => _tableLead;
        public bool PlacedByPlayer => _userPlaced;
        public bool Simulated => _simulated;
        public bool Loading => _loading;
        /// <summary>Scene loads started (entry, focus reloads, Space Setup).</summary>
        public int Loads { get; private set; }
        public Vector3 LastReticleHit => _hit;

        /// <summary>Starts placing the lead as StartPlacing does, if it can land somewhere (a menu row could call it).</summary>
        public bool BeginPlacing(int hand = -1)
        {
            if (!Active || _placing || !_app.IsPresenting || !CanPlace() || !LeadInLayout()) return false;
            BeginPlacingWith(hand);
            return true;
        }

        /// <summary>OnApplicationFocus (MrRuntime): back from the system menu. The room is reloaded only when it may have
        /// changed meanwhile: the device's rooms are not the ones loaded (SceneChangedAsync: a room added, removed or captured
        /// again; with no room loaded, one that appeared because the player ran Space Setup), or the load had not got that far
        /// (no permission: the player may have allowed spatial data; failed or not available). A plain trip to the system menu
        /// or the headset off and on reloads nothing: the reload rebuilt every portal and re-arranged the table, a hitch each
        /// time; and with no Space Setup (NoRooms) every focus return of the 2026-11-11 log reloaded an empty room for nothing.
        /// "Set up my room" reloads by itself.</summary>
        public void OnApplicationFocusChanged(bool focused)
        {
            if (!focused || !Active || _loading || _checkingScene) return;
            _ = ReloadIfChangedAsync(_session);
        }

        /// <summary>Tests and tools: decides whether the scene changed while the app was away (null: the device check,
        /// SceneChangedAsync).</summary>
        public Func<Task<bool>> SceneChangedProbe;
        bool _checkingScene;

        async Task ReloadIfChangedAsync(int session)
        {
            bool changed;
            _checkingScene = true;
            try
            {
                if (!AskDeviceOnFocus(MrScene.Status)) changed = true;
                else if (SceneChangedProbe != null) changed = await SceneChangedProbe();
                else changed = await SceneChangedAsync();
            }
            catch (Exception e) { Debug.LogException(e); changed = false; }
            finally { _checkingScene = false; }
            if (!Active || session != _session || _loading) return;
            if (!changed) { Debug.Log("[MR] focus back: the room is unchanged, no reload"); return; }
            Debug.Log("[MR] focus back: the room changed, reloading it");
            _ = LoadSceneAsync(_session);
        }

        /// <summary>On a focus return, whether the device is asked if its rooms changed (Loaded: one or more rooms; NoRooms:
        /// none, the same as before unless Space Setup made one) instead of reloading at once (no permission, failed, not
        /// available, idle: the load never got its answer).</summary>
        public static bool AskDeviceOnFocus(MrSceneStatus status) => status == MrSceneStatus.Loaded || status == MrSceneStatus.NoRooms;

        static readonly List<OVRAnchor> s_roomAnchors = new List<OVRAnchor>();

        /// <summary>The device's room anchors (OVRRoomLayout) against the rooms MRUK has loaded: a room added, removed or
        /// captured again (a new anchor) is a change. Headset only (false in the editor, where the room is a recorded one).</summary>
        async Task<bool> SceneChangedAsync()
        {
            var mruk = MRUK.Instance;
            // No room was loaded (NoRooms): the device answering with any room means Space Setup made one meanwhile.
            bool none = MrScene.Status == MrSceneStatus.NoRooms;
            if (_app.Rig == null || !_app.Rig.IsXrActive || (!none && (mruk == null || !mruk.IsInitialized))) return false;
            var result = await OVRAnchor.FetchAnchorsAsync(s_roomAnchors, new OVRAnchor.FetchOptions { SingleComponentType = typeof(OVRRoomLayout) });
            if (!result.Success) return false;
            if (none) return s_roomAnchors.Count > 0;
            var rooms = mruk.Rooms;
            if (rooms.Count != s_roomAnchors.Count) return true;
            for (int i = 0; i < s_roomAnchors.Count; i++)
            {
                Guid id = s_roomAnchors[i].Uuid;
                bool found = false;
                for (int k = 0; k < rooms.Count && !found; k++) found = rooms[k] != null && rooms[k].Anchor.Uuid == id;
                if (!found) return true;
            }
            return false;
        }

        // ---------------- the frame ----------------

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.MixedReality)
            {
                if (Active) Frame(dt);
            }
            else if (phase == FramePhase.EndFrame)
            {
                // The landing frame's Grab pass has seen IsPlacing; the next frame plays again.
                _consumedThisFrame = false;
                if (_env != null && _env.enabled && !_envUsed) _env.enabled = false;
                _envUsed = false;
            }
        }

        void Frame(float dt)
        {
            Transform head = _app.Head;
            _room = CurrentRoom();
            int pinchHand = PinchStartHand();
            GatherRays();

            if (_arrangePending)
            {
                _arrangePending = false;
                AutoArrange();
            }
            if (_tablePending && !_placing)
            {
                _tablePending = false;
                if (!_userPlaced && !_leadOnTable && !(TableOnEnter && TryPlaceOnBestTable()) && _standFloorPending)
                {
                    // Arranged before the scene was known: every instrument goes on the real floor now (FloorAnchors[0]).
                    SeatOnRealFloor();
                    _anchorYaw = float.NaN;
                }
                _standFloorPending = false;
            }

            if (_placing && _app.Clock.Now - _placingSince >= PlacingTimeout) CancelPlacing("timed out");
            if (_placing) UpdatePlacing(pinchHand >= 0, head);

            UpdateAnchor();
            UpdateHint(dt, head);
        }

        // ---------------- loading ----------------

        async Task LoadSceneAsync(int session)
        {
            if (_loading) return;
            _loading = true;
            Loads++;
            MrSceneStatus status;
            try
            {
                status = await MrScene.LoadDeviceSceneAsync();
                if (status == MrSceneStatus.NotAvailable && (_simulated || Application.isEditor))
                {
                    // Editor without Link: one of MRUK's recorded rooms instead (spec 03 section 2.12).
                    string json = MrDevOptions.ResolveSceneJson();
                    if (!string.IsNullOrEmpty(json)) status = await MrScene.LoadJsonSceneAsync(json);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                status = MrSceneStatus.Failed;
            }
            finally { _loading = false; }
            if (!Active)
            {
                MrScene.Clear(); // left mixed reality while loading: drop what arrived
                return;
            }
            // Still (or again) in mixed reality: the scene that arrived is the current one, even when the player left and
            // came back meanwhile (that second Enter found this load running and did not start another).
            if (session != _session) Debug.Log("[MR] a load started before re-entering finished: using it");
            OnLoaded(status);
        }

        void OnLoaded(MrSceneStatus status)
        {
            _room = CurrentRoom();
            if (status == MrSceneStatus.Loaded)
            {
                var rigRoot = _app.Rig != null ? _app.Rig.RigRoot : null;
                MrScene.SetWorldLock(true, rigRoot != null ? rigRoot.localToWorldMatrix : Matrix4x4.identity);
                if (_room != null)
                {
                    // The room's own axes (floor anchor) for the room sound (10-upgrade room.md 4.4); the world AABB otherwise.
                    Bounds b = _room.GetRoomBounds();
                    Vector3 center = b.center, size = b.size;
                    float yaw = 0f;
                    var floors = _room.FloorAnchors;
                    var floor = floors != null && floors.Count > 0 ? floors[0] : null;
                    if (floor != null && floor.PlaneRect.HasValue)
                    {
                        Rect pr = floor.PlaneRect.Value;
                        Vector3 right = floor.transform.right;
                        right.y = 0f;
                        if (right.sqrMagnitude > 1e-6f && pr.width > 0.5f && pr.height > 0.5f)
                        {
                            yaw = Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;
                            Vector3 c = floor.transform.TransformPoint(new Vector3(pr.center.x, pr.center.y, 0f));
                            center = new Vector3(c.x, b.center.y, c.z);
                            size = new Vector3(pr.width, b.size.y, pr.height);
                        }
                    }
                    _app.Get<IAudioEngine>()?.SetRoomBox(center, size, yaw, true);
                }
                _tablePending = true;
                _anchorYaw = float.NaN; // re-seat the catcher on the real surfaces
            }
            Debug.Log($"[MR] scene {status}: rooms={(MRUK.Instance != null ? MRUK.Instance.Rooms.Count : 0)}");
            _app.Events.Publish(new MixedRealityEvent { Active = true }); // the menu re-reads Status (Set up my room)
        }

        void OnSceneStatus(MrSceneStatus s)
        {
            Debug.Log($"[MR] scene status {s}");
        }

        // ResetPlayerRig: rig.position / rotation = 0. The VR seat fit (XrMode.MoveRig) and the swivel
        // (XrMode.SnapTurn) move the rig root; MRUK's world lock then writes the tracking space's world pose with
        // TrackingSpaceOffset (identity) and would make the head jump by that offset.
        void ResetPlayerRig()
        {
            var rr = _app.Rig != null ? _app.Rig.RigRoot : null;
            if (rr == null) return;
            if (rr.position == Vector3.zero && rr.rotation == Quaternion.identity) return;
            Debug.Log($"[MR] player rig reset from {rr.position:F2} yaw {rr.eulerAngles.y:F0} (enterAR)");
            rr.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _app.Input.ResetMotion();
        }

        // ---------------- layout ----------------

        // AutoArrange: every instrument in front of the player on the real floor, as in the music room: Layout places each at
        // its default pose about the head now (in the reach band, with the player's comfort offsets), standing on
        // FloorHeight. No saved anchor and no table: the point-and-pinch placer (StartPlacing) still puts the piano on a
        // real table when the player asks, and TableOnEnter (off) brings back the automatic best table.
        void AutoArrange()
        {
            ResetSurfaces();
            _leadOnTable = false;
            LeadToStandHeight();
            _standFloorPending = _room == null;
            _ownArrange = true;
            try { Layout?.ArrangeAroundPlayer(true); }
            finally { _ownArrange = false; }
            _arrangedFloor = FloorY();
            GuardDraws();
            if (TableOnEnter && MrScene.Status == MrSceneStatus.Loaded) TryPlaceOnBestTable();
            _app.Input.ResetMotion();
            _anchorYaw = float.NaN;
            LeadHints();
            if (!_leadOnTable) SetTransientHint(_hintEnter);
            Debug.Log($"[MR] arranged around the player ({Describe(Lead)?.Id ?? "no lead"} {(_leadOnTable ? "on a table" : "on its stand")}, draws {DrawEstimate()})");
        }

        /// <summary>The lead's rig origin at its home height above the real floor (the piano's key tops 0.72 m: its stand).</summary>
        void LeadToStandHeight()
        {
            var lead = LeadRig();
            if (lead?.Group == null) return;
            Vector3 p = lead.Group.position;
            p.y = FloorY() + StandHeight(lead);
            lead.Group.position = p;
        }

        /// <summary>Entering puts the lead on the best real table by itself (the behaviour before the instruments opened in
        /// front of the player). Off: the instruments open in front on the floor; the placer still reaches a table.</summary>
        public bool TableOnEnter;
        float _arrangedFloor;

        /// <summary>The scene arrived after the arrange: every instrument of the layout moves up or down onto the real floor.</summary>
        void SeatOnRealFloor()
        {
            float dy = FloorY() - _arrangedFloor;
            _arrangedFloor = FloorY();
            if (Mathf.Abs(dy) < 1e-4f) return;
            var reg = _app.Get<IRigRegistry>();
            if (reg == null) return;
            var layout = Layout;
            for (int i = 0; i < reg.Rigs.Count; i++)
            {
                Rig r = reg.Rigs[i];
                if (r?.Group == null || r.Id == RigId.Lessons || !r.InScene) continue;
                if (layout != null && InstrumentIds.KindOf(r.Id) != InstrumentKind.Table && !layout.IsInScene(InstrumentIds.KindOf(r.Id))) continue;
                r.Group.position += new Vector3(0f, dy, 0f);
            }
            Debug.Log($"[MR] the instruments stand on the real floor ({dy:+0.000;-0.000} m)");
        }

        /// <summary>IMixedReality.FloorHeight: the scene's floor, else the tracking space's floor.</summary>
        public float FloorHeight => FloorY();

        float StandHeight(Rig r) => Layout != null && Layout.Mode == LayoutMode.Single ? r.HomeSingle.y : r.HomeAll.y;

        // ARCHITECTURE.md section 12: the lead (the piano) on the best real table, the rest around it. A floor-only lead
        // stays where the arrange put it.
        bool TryPlaceOnBestTable()
        {
            InstrumentKind kind = Lead;
            var lead = LeadRig();
            if (lead?.Group == null || !LeadInLayout() || !CanStandOnTable(kind)) return false;
            if (!TryGetLeadPose(kind, out var pose, out bool onTable) || !onTable) return false;
            lead.Group.SetPositionAndRotation(pose.position, pose.rotation);
            SetSurface(kind, true);
            _leadOnTable = true;
            _tableLead = kind;
            _tablePose = pose;
            Layout?.PlaceAroundLead();
            _app.Input.ResetMotion();
            _anchorYaw = float.NaN;
            LeadHints();
            SetTransientHint(_hintEnterOnTable);
            Debug.Log($"[MR] {Describe(kind)?.Id} on the table at {pose.position:F2}");
            return true;
        }

        // Layout re-arranged (two-hand summon, recentre, a layout change). It keeps the piano's height, so a lead carried
        // off its table would hang at the table's height with its stand hidden (a floating piano): unless its body
        // still rests on a real top there, it gets its stand back and stands on the floor. A lead that changed (a set
        // changed in MR) takes the old one off its table.
        void OnArranged(ArrangedEvent e)
        {
            if (!Active || _ownArrange) return;
            _anchorYaw = float.NaN;
            GuardDraws();
            if (!_leadOnTable) return;
            Rig onTable = Rig(InstrumentIds.RigOf(_tableLead));
            if (_tableLead != Lead || onTable?.Group == null)
            {
                SetSurface(_tableLead, false);
                _leadOnTable = false;
                LeadToStandHeight();
                Debug.Log($"[MR] the lead changed: {Describe(_tableLead)?.Id} is off its table");
                return;
            }
            if ((onTable.Group.position - _tablePose.position).sqrMagnitude <= LeftTableDistance * LeftTableDistance) return;
            if (RestsOnRealTop(onTable.Group.position, SurfaceLiftOf(_tableLead)))
            {
                _tablePose = new Pose(onTable.Group.position, onTable.Group.rotation);
                return;
            }
            SetSurface(_tableLead, false);
            _leadOnTable = false;
            LeadToStandHeight();
            _app.Input.ResetMotion();
            Debug.Log($"[MR] the {Describe(_tableLead)?.Id} left its table: back on its stand at {onTable.Group.position:F2}");
        }

        // A real table-like top right under the lead's body (its origin - its surface lift: the piano's key tops - tableLift),
        // within a few centimetres.
        bool RestsOnRealTop(Vector3 origin, float surfaceLift)
        {
            if (_room == null) return false;
            float underside = origin.y - surfaceLift;
            var ray = new Ray(new Vector3(origin.x, underside + MrConstants.CatcherProbePiano, origin.z), Vector3.down);
            if (!TableFinder.TryPointAtSurface(_room, ray, out Vector3 hit, out _, out bool onTable)) return false;
            return onTable && Mathf.Abs(hit.y - underside) <= RestsOnTopTolerance;
        }

        // ---------------- the lead instrument (platform.md 9) ----------------

        /// <summary>The one instrument MR names (RegistryLintTests' allowlisted "MR's piano anchor"): E9 (PianoAnchorStore)
        /// remembers the piano's pose only, and the piano keeps its tested table pose (TryGetPianoPose) and reticle.</summary>
        const InstrumentKind AnchorInstrument = InstrumentKind.Piano;

        ILayoutService Layout => _app.Get<ILayoutService>();

        /// <summary>The instrument mixed reality arranges around now: ILayoutService.Lead (the first loaded instrument of the
        /// carousel without a Layout).</summary>
        public InstrumentKind Lead
        {
            get
            {
                var layout = Layout;
                if (layout != null) return layout.Lead;
                var reg = _app.Registry;
                if (reg != null) for (int i = 0; i < reg.Carousel.Count; i++) if (reg.IsLoaded(reg.Carousel[i])) return reg.Carousel[i];
                return AnchorInstrument;
            }
        }

        bool LeadIsAnchor => Lead == AnchorInstrument;
        Rig LeadRig() => Rig(InstrumentIds.RigOf(Lead));
        InstrumentDescriptor Describe(InstrumentKind k) => _app.Registry?.Get(k);
        bool LeadInLayout() { var layout = Layout; return layout == null || layout.IsInScene(Lead); }
        bool CanStandOnTable(InstrumentKind k) { var d = Describe(k); return d?.Mr != null && d.Mr.OnTable; }
        float SurfaceLiftOf(InstrumentKind k) { var d = Describe(k); return d?.Mr != null ? d.Mr.SurfaceLift : 0f; }

        // The instruments that can stand on a real table (IInstrumentSurface), refreshed when the services change.
        readonly List<IInstrumentSurface> _surfaces = new List<IInstrumentSurface>(4);
        int _surfacesVersion = -1;

        List<IInstrumentSurface> Surfaces()
        {
            if (_surfacesVersion != _app.ServicesVersion)
            {
                _surfacesVersion = _app.ServicesVersion;
                _surfaces.Clear();
                _surfaces.AddRange(_app.GetAll<IInstrumentSurface>());
            }
            return _surfaces;
        }

        /// <summary>The kind's stand hidden (on a real table) or shown: its first registered IInstrumentSurface.</summary>
        void SetSurface(InstrumentKind kind, bool onSurface)
        {
            var all = Surfaces();
            for (int i = 0; i < all.Count; i++) if (all[i].Kind == kind) { all[i].SetOnSurface(onSurface); return; }
        }

        /// <summary>Every instrument off its real table (its stand back).</summary>
        void ResetSurfaces()
        {
            var all = Surfaces();
            for (int i = 0; i < all.Count; i++) all[i].SetOnSurface(false);
        }

        /// <summary>platform.md 9.6: the at-seat instruments' DrawsAtSeat + the fixed MR draws (37).</summary>
        public int DrawEstimate()
        {
            int n = MrConstants.FixedDraws;
            var reg = _app.Registry;
            if (reg == null) return n;
            var layout = Layout;
            var all = reg.All;
            for (int i = 0; i < all.Count; i++)
            {
                InstrumentDescriptor d = all[i];
                if (!reg.IsLoaded(d.Kind) || (layout != null && !layout.IsInScene(d.Kind))) continue;
                n += d.DrawsAtSeat;
            }
            return n;
        }

        /// <summary>platform.md 9.6: while the estimate is over budget, the last member of My set is put away
        /// (ILayoutService.PutAway) with the note "Put away: no room here" (logged; InstrumentSignalEvent mr.noroom for the
        /// menu).</summary>
        void GuardDraws()
        {
            var layout = Layout;
            if (layout == null || _guarding) return;
            _guarding = true;   // PutAway re-arranges and publishes: the events it raises do not guard again meanwhile
            try
            {
                for (int guard = 0; guard < MySetRules.Max && DrawEstimate() > MrConstants.DrawBudget; guard++)
                {
                    var set = layout.MySet;
                    if (layout.Mode != LayoutMode.All || set == null || set.Count <= 1) break;
                    InstrumentKind last = set[set.Count - 1];
                    bool ok = layout.PutAway(last);
                    Debug.Log($"[MR] {MrConstants.NoRoomNote}: {Describe(last)?.Id} (draws {DrawEstimate()}, budget {MrConstants.DrawBudget}){(ok ? "" : "; Layout kept it")}");
                    _app.Events.Publish(new InstrumentSignalEvent { Kind = last, Signal = MrConstants.NoRoomSignal });
                    if (!ok) break;
                }
            }
            finally { _guarding = false; }
        }

        bool _guarding;

        // The hints name the lead ("the piano" in MrConstants); a lead that cannot stand on a table is moved on the floor.
        InstrumentKind _hintsFor = (InstrumentKind)(-1);
        string _hintEnter = MrConstants.EnterHint, _hintEnterOnTable = MrConstants.EnterOnTableHint, _hintPlacing = MrConstants.PlacingHint,
            _hintPlaced = MrConstants.PlacedHint;

        void LeadHints()
        {
            InstrumentKind k = Lead;
            if (k == _hintsFor) return;
            _hintsFor = k;
            InstrumentDescriptor d = Describe(k);
            string name = d != null && !string.IsNullOrEmpty(d.Name) ? "the " + d.Name.ToLowerInvariant() : MrConstants.LeadName;
            bool table = CanStandOnTable(k);
            _hintEnter = Named(table ? MrConstants.EnterHint : MrConstants.EnterHintFloor, name);
            _hintPlacing = Named(table ? MrConstants.PlacingHint : MrConstants.PlacingHintFloor, name);
            _hintPlaced = Named(MrConstants.PlacedHint, name);
            _hintEnterOnTable = Named(MrConstants.EnterOnTableHint, name);
            if (!ReferenceEquals(_hintEnterOnTable, MrConstants.EnterOnTableHint)) _hintEnterOnTable = Named(MrConstants.EnterOnTableHintLead, name);
        }

        /// <summary>The text with the lead's name; the very same string when nothing changes (the piano).</summary>
        static string Named(string text, string name)
        {
            string capital = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
            string s = text.Replace(MrConstants.LeadName, name).Replace(MrConstants.LeadNameCapital, capital).Replace(MrConstants.LeadSlot, name);
            return s == text ? text : s;
        }

        // ---------------- placing ----------------

        bool CanPlace() => _room != null || CurrentRoom() != null || EnvUsable();

        void BeginPlacingWith(int hand)
        {
            _placing = true;
            _placingHand = hand;
            _placingSince = _app.Clock.Now;
            _hasHit = false;
            // The other rigs hide while the lead is placed (Layout hides them before calling
            // StartPlacing). Every rig in the layout is remembered, hidden here or not, so ending placing without a
            // landing (no surface) still brings back what Layout hid; a rig out of the scene (on display) is never touched.
            _hiddenRigs.Clear();
            RigId leadId = InstrumentIds.RigOf(Lead);
            var reg = _app.Get<IRigRegistry>();
            if (reg != null)
            {
                var rigs = reg.Rigs;
                for (int i = 0; i < rigs.Count; i++)
                {
                    var r = rigs[i];
                    if (r.Id == leadId || r.Id == RigId.Lessons || r.Group == null || !r.InScene) continue;
                    if (r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(false);
                    _hiddenRigs.Add(r);
                }
            }
            // The reticle shows the lead's footprint where it will stand (the piano's body outline).
            if (LeadIsAnchor) _reticle.SetFootprint(MrConstants.FootprintWidth, MrConstants.FootprintDepth, MrConstants.FootprintZ);
            else
            {
                Footprint(Describe(Lead), out float minX, out float maxX, out float minZ, out float maxZ);
                _reticle.SetFootprint(maxX - minX, maxZ - minZ, MrConstants.PushBack + (minZ + maxZ) * 0.5f, (minX + maxX) * 0.5f);
            }
            LeadHints();
            Debug.Log($"[MR] placing the {Describe(Lead)?.Id} (hand {hand})");
            _app.Events.Publish(new PlacingEvent { Active = true });
        }

        void EndPlacing(bool restoreRigs = true)
        {
            bool was = _placing;
            _placing = false;
            _placingHand = -1;
            _reticle.Hide();
            // Layout's membership decides what comes back (a rig may have left the layout meanwhile).
            if (restoreRigs)
                for (int i = 0; i < _hiddenRigs.Count; i++)
                {
                    var r = _hiddenRigs[i];
                    if (r.Group != null) r.Group.gameObject.SetActive(r.InScene);
                }
            _hiddenRigs.Clear();
            if (was && restoreRigs) _app.Events.Publish(new PlacingEvent { Active = false });
        }

        // ar.update while placing: the reticle on the best hit this frame, a pinch drops the piano there.
        void UpdatePlacing(bool pinch, Transform head)
        {
            if (FindSurface(head, out Vector3 point))
            {
                _hit = point;
                _hasHit = true;
                _reticle.Show(point, PianoPlacement.ReticleRotation(point, head.position, head.forward));
            }
            else _reticle.Hide();
            if (!pinch) return;
            if (_hasHit) PlaceLead(_hit, head);
            else
            {
                // Nothing to put it on yet (no surface seen this time): the pinch ends placing instead of trapping the player.
                CancelPlacing("no surface");
            }
        }

        // FindSurface: a pointing hand's ray (the placing hand's first). Never where the head looks: turning the head moves
        // nothing (2026-11-10 headset test).
        bool FindSurface(Transform head, out Vector3 point)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < 2; i++)
                {
                    var r = _rays[i];
                    if (!r.Valid) continue;
                    bool first = _placingHand < 0 || i == _placingHand;
                    if (first != (pass == 0)) continue;
                    if (SurfaceAlong(r.Ray, out point)) return true;
                }
            point = default;
            return false;
        }

        bool SurfaceAlong(Ray ray, out Vector3 point)
        {
            bool found;
            if (_room != null) found = TableFinder.TryPointAtSurface(_room, ray, out point, out _, out _, MrConstants.SurfaceRayMax);
            else
            {
                point = default;
                found = EnvRaycast(ray, out Vector3 p, out Vector3 n) && n.y > 0.7f; // spec 07: upward surfaces only
                if (found) point = p;
            }
            // A floor-only lead (the drums, the harp) lands on the floor only: a table under the ray is no place for it.
            if (found && !CanStandOnTable(Lead) && point.y - FloorY() > MrConstants.OnTableMinHeight) return false;
            return found;
        }

        // PlaceLead, for the lead: its surface lift on a table (the piano's tableLift), its stand height on
        // the floor (the piano's 0.72 m), PushBack beyond the point, facing away from the player.
        void PlaceLead(Vector3 hit, Transform head)
        {
            InstrumentKind kind = Lead;
            var lead = LeadRig();
            EndPlacing();
            _consumedThisFrame = true;
            if (lead?.Group == null) return;
            Pose pose = PianoPlacement.FromHit(hit, head.position, FloorY(), SurfaceLiftOf(kind), StandHeight(lead), CanStandOnTable(kind), out bool onTable);
            lead.Group.SetPositionAndRotation(pose.position, pose.rotation);
            SetSurface(kind, onTable);
            _leadOnTable = onTable;
            _tableLead = kind;
            _tablePose = pose;
            _userPlaced = true;
            Layout?.PlaceAroundLead();
            _app.Input.ResetMotion();
            _anchorYaw = float.NaN;
            LeadHints();
            SetTransientHint(_hintPlaced);
            Debug.Log($"[MR] {Describe(kind)?.Id} placed {(onTable ? "on a table" : "on the floor")} at {pose.position:F2}");
        }

        // ---------------- anchor: catcher and sun ----------------

        void UpdateAnchor()
        {
            // The lead's rig (Layout's anchor: the piano in the default set, the one instrument alone); its group says
            // whether it shows, its pose is ILayoutService.Anchor's (ARCHITECTURE.md 12.1).
            InstrumentKind kind = Lead;
            Rig rig = Rig(InstrumentIds.RigOf(kind));
            Transform a = rig?.Group;
            if (a == null || !a.gameObject.activeInHierarchy)
            {
                _lighting.HideCatcher();
                _anchor = null;
                return;
            }
            Vector3 p = a.position;
            float yaw = a.rotation.eulerAngles.y;
            if (a == _anchor && !float.IsNaN(_anchorYaw) && (p - _anchorPos).sqrMagnitude < 1e-6f && Mathf.Abs(Mathf.DeltaAngle(yaw, _anchorYaw)) < 0.1f) return;
            _anchor = a;
            _anchorPos = p;
            _anchorYaw = yaw;
            bool tableTop = CanStandOnTable(kind);
            _lighting.PlaceCatcher(p, yaw, SurfaceBelow(p, tableTop, tableTop ? SurfaceLiftOf(kind) : 0f));
            _lighting.Aim(p);
        }

        // The real surface under the anchor (a table / bed / floor of the scene, else the floor), probed from just above
        // where it stands so a box beside the lead on the same desk is not taken for its surface: 2 cm above the underside
        // of a table-top instrument's body (the piano's key tops - tableLift), 5 cm above a floor rig's origin.
        float SurfaceBelow(Vector3 p, bool tableTop, float surfaceLift)
        {
            float startY = tableTop ? p.y - surfaceLift + MrConstants.CatcherProbePiano : p.y + MrConstants.CatcherProbeFloorRig;
            if (_room != null && TableFinder.TryPointAtSurface(_room, new Ray(new Vector3(p.x, startY, p.z), Vector3.down), out Vector3 hit, out _, out _))
                return hit.y;
            return FloorY();
        }

        // ---------------- hints ----------------

        void SetTransientHint(string text)
        {
            _transientHint = text;
            _transientUntil = _app.Clock.Now + MrConstants.HintSeconds;
        }

        void UpdateHint(float dt, Transform head)
        {
            string want = null;
            if (_placing) want = _hintPlacing;
            else if (_transientHint != null && _app.Clock.Now < _transientUntil) want = _transientHint;
            // An actionable hint replaces the informational one for good (it would be stale when the action is done).
            if (_placing) _transientHint = null;
            if (!ReferenceEquals(want, Hint))
            {
                Hint = want;
                if (want != null) Debug.Log("[MR] hint: " + want);
            }
            if (want == null && _hint == null) return;
            if (_hint == null) _hint = new MrHint(_root, _materials);
            _hint.Set(want, head, sticky: _placing);   // the placing hint stays for as long as placing lasts
            _hint.Follow(head, dt);
        }

        // ---------------- input helpers ----------------

        // The pinches here are grab pinches (HandState.GrabPinch*: the thumb-index rule OR Meta's own index pinch), like
        // Layout's grab that starts the placing: on the headset the thumb-index rule alone flickers (the two tip joints sit
        // ~17 mm apart when the fingertips touch, above its 15 mm start).

        // The hand whose pinch has been held longest (the one whose dwell Layout just completed).
        int PinchingHand()
        {
            int hand = -1;
            double best = -1;
            var hands = _app.Input.Hands;
            for (int i = 0; i < 2; i++)
                if (hands[i].Connected && hands[i].GrabPinching && hands[i].GrabPinchFor > best) { best = hands[i].GrabPinchFor; hand = i; }
            return hand;
        }

        // The hand whose pinch starts this frame (it lands the piano); a pinch that starts like the
        // OS system gesture (palm toward the headset) is not one.
        int PinchStartHand()
        {
            int hand = -1;
            var input = _app.Input;
            var hands = input.Hands;
            Transform head = _app.Head;
            for (int i = 0; i < 2; i++)
                if (hands[i].Connected && hands[i].GrabPinchStart && (head == null || !input.SystemGestureLike(hands[i], head.position, head.forward))) hand = i;
            return hand;
        }

        void GatherRays()
        {
            var input = _app.Input;
            for (int i = 0; i < 2; i++)
            {
                HandState h = input.Hands[i];
                HandFrame f = input.Frame(h);
                bool ok = h.Connected && f.pointerValid && !f.systemGesture;
                _rays[i] = new HandRay { Valid = ok, Hand = i, Ray = ok ? new Ray(f.pointer.position, f.pointer.rotation * Vector3.forward) : default };
            }
        }

        bool EnvUsable() => _app.Rig.IsXrActive && MrScene.HasScenePermission && EnvironmentRaycastManager.IsSupported;

        /// <summary>The wall paint's depth wall (no scene model): a PlaceBox check (a flat, clear, upright patch round the hit,
        /// so a chair back, a lamp or the edge of a shelf is never taken for a wall). Asked once a frame while a playback paints in mixed reality, which keeps the sensor awake.</summary>
        bool PaintDepthWall(Ray ray, out Vector3 point, out Vector3 inward)
        {
            point = inward = default;
            if (!Active || _room != null || !EnvUsable()) return false;
            if (_env == null) _env = _root.gameObject.AddComponent<EnvironmentRaycastManager>();
            if (!_env.enabled) _env.enabled = true;
            _envUsed = true;
            if (!_env.PlaceBox(ray, MrConstants.DepthWallBox, Vector3.up, out EnvironmentRaycastHit hit)) return false;
            if (Mathf.Abs(hit.normal.y) >= MrConstants.EnvWallMaxNormalY) return false;
            point = hit.point;
            inward = hit.normal;
            return true;
        }

        bool EnvRaycast(Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (!EnvUsable()) return false;
            if (_env == null) _env = _root.gameObject.AddComponent<EnvironmentRaycastManager>();
            if (!_env.enabled) _env.enabled = true;
            _envUsed = true;
            if (!_env.Raycast(ray, out EnvironmentRaycastHit hit, MrConstants.WallRayMax)) return false;
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        // ---------------- small helpers ----------------

        Rig Rig(RigId id) => _app.Get<IRigRegistry>()?.Get(id);

        static MRUKRoom CurrentRoom()
        {
            var m = MRUK.Instance;
            if (m == null || !m.IsInitialized) return null;
            return m.GetCurrentRoom() ?? (m.Rooms.Count > 0 ? m.Rooms[0] : null);
        }

        float FloorY() => _room != null ? TableFinder.FloorY(_room) : 0f;

        void SetSimulatedBackground(bool on)
        {
            var rig = _app.Rig;
            var cam = rig != null ? rig.HeadCamera : null; // the rig may already be gone at shutdown
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = on ? MrDevOptions.SimulatedBackground : XrMode.VrBackground;
        }
    }
}
