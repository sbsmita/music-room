// Look and pinch (2026-11-13): look at a menu row or an instrument on display and it lights; pinch (either hand, held away
// from the instruments and the menu) and it is chosen: the row fires exactly as a poke does (IGazeMenu -> the menu's own
// Fire), the instrument comes to the seat exactly as the menu's tile or the ray summon brings it (ILayoutService.Bring).
// Built on Meta's Interaction SDK (com.meta.xr.sdk.interaction 207.0.1, the only assembly of the app that references it):
//   Hand x2                  (Oculus.Interaction.Input.Hand) fed by MusicRoomHandDataSource (the app's own hand frames)
//   IndexPinchSelector x2    (the SDK's index pinch on each Hand)
//   GazePinchSelector        (ISelector: either hand's pinch, accepted only from a free hand on a gazed target)
//   HeadGazeInteractor       (RayInteractor from the centre eye; field-of-view rule and dwell; allocation-free candidate)
//   RayInteractable x(1+10)  (the menu panel, one per instrument kind) on GazeSurfaces (ISurface: rows, display boxes)
//   GazeActiveState          (IActiveState: the gate; off disables the interactor)
//   PointerEvent             (each RayInteractable's WhenPointerEventRaised: Hover / Select / Unselect drive the choice)
// The SDK components are driven by this module once per app frame (InputDone phase, before the menu's pokes and the hover
// glows): no SDK Update runs on its own, so tests with manual frames and the headset see the same order.
// Highlights: the menu row with the poke's own hover look (the face shader's row highlight); an instrument with one soft
// warm glow (HoverGlowPool, the app's hover amber, steady) behind it, facing the eyes, fading in and out over 0.15 s.
// Never steals a pinch (GazeRules): only in the headset, never while moving instruments, MR placing, a rig held or Sunny
// teaching a pinch, and only a short still pinch of a free hand (not playing, holding, on an instrument's zone, the menu or
// Sunny), released while still looking at the target. Sunny's tip_look_pinch the first time something lights.
// Allocation-free per frame after setup.
using System;
using MusicRoom.Core;
using MusicRoom.Hands;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Profiling;

namespace MusicRoom.Gaze
{
    public sealed class GazeModule : AppModule, ILookAndPinch
    {
        public const int ModuleOrder = 90;   // after MR (70), before Layout (100), which collects the rigs last
        public override int Order => ModuleOrder;

        public const float GlowGain = 0.7f, GlowScale = 1.6f, GlowMax = 2.4f;

        App _app;
        GameObject _root;
        Transform _origin;
        MusicRoomHandDataSource[] _sources;
        Hand[] _hands;
        IndexPinchSelector[] _pinches;
        GazePinchSelector _selector;
        GazeActiveState _active;
        HeadGazeInteractor _interactor;
        RayInteractable[] _targets;          // slot 0 the menu, 1 + kind the instruments
        GazeSurface[] _surfaces;
        MenuGazeSurface _menuSurface;
        InstrumentGazeSurface[] _instruments;
        Action<PointerEvent>[] _onPointer;
        readonly Vector3[] _lastPoint = new Vector3[InstrumentIds.KindCount];
        readonly bool[] _free = new bool[2];
        // the instruments at the seat (occluders): their boxes, measured again only when their rig moves
        readonly Bounds[] _seatBox = new Bounds[InstrumentIds.KindCount];
        readonly Vector3[] _seatPos = new Vector3[InstrumentIds.KindCount];
        readonly Quaternion[] _seatRot = new Quaternion[InstrumentIds.KindCount];
        readonly bool[] _seatValid = new bool[InstrumentIds.KindCount];

        ILayoutService _layout;
        IArrangeMode _arrange;
        IMixedReality _mr;
        IGrabService _grab;
        IGuide _guide;
        IGazeMenu _menu;
        IRigRegistry _rigs;
        int _services = -1;

        bool _on = true;
        Vector3 _prevForward;
        float _headSpeed;
        GazeTap _tap;
        bool _tapOk;
        long _act;              // the choice to carry out after this frame's Drive (0: none)
        float _glowK;
        int _glowSlot = -1;
        float _shownFor, _tipAskIn;
        bool _tipDone;

        // ---- state for tests and diagnostics
        public bool Active => _active != null && _active.Value;
        public float HeadSpeed => _headSpeed;
        public HeadGazeInteractor Interactor => _interactor;
        public GazePinchSelector Selector => _selector;
        public Hand IsdkHand(int i) => _hands != null && i >= 0 && i < _hands.Length ? _hands[i] : null;
        public RayInteractable TargetOf(int slot) => _targets != null && slot >= 0 && slot < _targets.Length ? _targets[slot] : null;
        public InstrumentGazeSurface InstrumentSurface(InstrumentKind k) => _instruments?[(int)k];
        public bool HandFree(int i) => i >= 0 && i < 2 && _free[i];
        public long CommittedKey => _interactor != null ? _interactor.CommittedKey : 0;
        public bool TapActive => _tap.Active;
        public float GlowLevel => _glowK;
        public bool TipAsked => _tipDone;
        /// <summary>Taps that were judged at release and refused (moved, too long, busy, looked away).</summary>
        public int Refused { get; private set; }

        public GazeTargetKind Target { get; private set; }
        public InstrumentKind TargetInstrument { get; private set; }
        public int Chosen { get; private set; }

        public bool On
        {
            get => _on;
            set
            {
                if (_on == value) return;
                _on = value;
                if (_app != null) { _app.Settings.LookAndPinch = value; _app.Settings.Save(); }
                if (!value) StandDown();
            }
        }

        public override void Init(App app)
        {
            _app = app;
            _on = app.Settings.LookAndPinch;
            app.Register<ILookAndPinch>(this);
            Build(app.WorldRoot);
        }

        public override void Shutdown()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        // ---------------------------------------------------------------- the SDK objects

        void Build(Transform parent)
        {
            _root = new GameObject("LookAndPinch");
            _root.SetActive(false);   // everything is injected before Awake / OnEnable run
            _root.transform.SetParent(parent, false);
            _origin = new GameObject("Centre eye").transform;
            _origin.SetParent(_root.transform, false);

            _sources = new MusicRoomHandDataSource[2];
            _hands = new Hand[2];
            _pinches = new IndexPinchSelector[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "Hand L" : "Hand R");
                go.transform.SetParent(_root.transform, false);
                _sources[i] = go.AddComponent<MusicRoomHandDataSource>();
                _sources[i].Bind(_app.Input, _app.Input.Hands[i]);
                _sources[i].InjectUpdateMode(DataSource<HandDataAsset>.UpdateModeFlags.Manual);
                _hands[i] = go.AddComponent<Hand>();
                _hands[i].InjectAllDataModifier(DataSource<HandDataAsset>.UpdateModeFlags.Manual, null, _sources[i], false);
                _pinches[i] = go.AddComponent<IndexPinchSelector>();
                _pinches[i].InjectAllIndexPinchSelector(_hands[i]);
            }

            var sel = new GameObject("Head gaze");
            sel.transform.SetParent(_root.transform, false);
            _selector = sel.AddComponent<GazePinchSelector>();
            _selector.Bind(_pinches[0], _pinches[1]);
            _selector.TryBegin = TryBegin;
            _selector.Released = Released;
            _active = sel.AddComponent<GazeActiveState>();
            _interactor = sel.AddComponent<HeadGazeInteractor>();
            _interactor.InjectAllRayInteractor(_selector, _origin);
            _interactor.InjectOptionalActiveState(_active);
            _interactor.MaxRayLength = GazeRules.MaxRayLength;
            _interactor.IsRootDriver = false;   // driven by Tick

            int n = 1 + InstrumentIds.KindCount;
            _targets = new RayInteractable[n];
            _surfaces = new GazeSurface[n];
            _instruments = new InstrumentGazeSurface[InstrumentIds.KindCount];
            _onPointer = new Action<PointerEvent>[n];
            for (int slot = 0; slot < n; slot++)
            {
                var go = new GameObject(slot == 0 ? "Menu rows" : "Instrument " + (slot - 1));
                go.transform.SetParent(_root.transform, false);
                GazeSurface s;
                if (slot == 0) s = _menuSurface = go.AddComponent<MenuGazeSurface>();
                else
                {
                    var inst = go.AddComponent<InstrumentGazeSurface>();
                    inst.Kind = (InstrumentKind)(slot - 1);
                    _instruments[slot - 1] = inst;
                    s = inst;
                }
                s.Slot = slot;
                _surfaces[slot] = s;
                var target = go.AddComponent<RayInteractable>();
                target.InjectAllRayInteractable(s);
                target.TiebreakerScore = slot == 0 ? 1 : 0;   // the menu is in front of whatever stands behind it
                int captured = slot;
                _onPointer[slot] = evt => OnPointer(captured, evt);
                target.WhenPointerEventRaised += _onPointer[slot];
                _targets[slot] = target;
            }
            _root.SetActive(true);
        }

        // ---------------------------------------------------------------- the frame

        public override void Tick(FramePhase phase, float dt)
        {
            if (phase != FramePhase.InputDone || _root == null) return;
            Profiler.BeginSample("[Gaze] look and pinch");
            try { Step(dt); }
            finally { Profiler.EndSample(); }
        }

        void Resolve()
        {
            if (_services == _app.ServicesVersion) return;
            _services = _app.ServicesVersion;
            _layout = _app.Get<ILayoutService>();
            _arrange = _app.Get<IArrangeMode>();
            _mr = _app.Get<IMixedReality>();
            _grab = _app.Get<IGrabService>();
            _guide = _app.Get<IGuide>();
            _menu = _app.Get<IGazeMenu>();
            _rigs = _app.Get<IRigRegistry>();
            _menuSurface.Menu = _menu;
        }

        void Step(float dt)
        {
            Resolve();
            Transform head = _app.Head;
            bool presenting = _app.IsPresenting && head != null;
            if (head != null)
            {
                _origin.SetPositionAndRotation(head.position, head.rotation);
                Vector3 f = head.forward;
                _headSpeed = GazeRules.Smooth(_headSpeed, GazeRules.AngularSpeed(_prevForward, f, dt), dt);
                _prevForward = f;
            }
            bool enabled = GazeRules.Enabled(_on, presenting,
                _arrange != null && _arrange.Arranging,
                _mr != null && _mr.IsPlacing,
                _grab != null && _grab.AnyHeld,
                _guide != null && _guide.TeachingPinch);
            if (!enabled && (_active.Value || _tap.Active)) StandDown();
            _active.Value = enabled;

            if (enabled) { RefreshInstruments(); RefreshOccluders(); }
            else for (int k = 0; k < _instruments.Length; k++) _instruments[k].Live = false;

            // the hands: who is free this frame, the held tap, then the SDK hands (their pinch edges reach the selector)
            PointerInput input = _app.Input;
            double now = _app.Clock.NowMs;
            for (int i = 0; i < 2; i++)
            {
                HandState h = input.Hands[i];
                bool free = enabled && GazeRules.HandFree(h.Connected, h.IsMouse, h.Holding != null,
                    _layout != null && _layout.HandBusy(i),
                    Claimed(h.Pointers[0]), Claimed(h.Pointers[4]),
                    head != null && input.SystemGestureLike(h, head.position, head.forward),
                    PointerInput.IsPlaying(h, now, GrabConstants.PlayGuardMs));
                _free[i] = free;
                if (_tap.Active && _tap.Hand == i) _tap.Step(dt, PinchPoint(h), free);
            }
            for (int i = 0; i < 2; i++)
            {
                _sources[i].MarkInputDataRequiresUpdate();
                _hands[i].MarkInputDataRequiresUpdate();
            }

            // the head gaze
            _interactor.BeginFrame(dt, GazeRules.HeadSteady(_headSpeed));
            _act = 0;
            _interactor.Drive();
            _tapOk = false;
            if (!enabled) _interactor.ResetGaze();

            Show(dt);
            if (_act != 0) Choose(_act);
            _act = 0;
            AskSunny(dt);
        }

        static bool Claimed(Pointer p) => p != null && p.Active && p.ClaimedBy != null;

        Vector3 PinchPoint(HandState h)
        {
            HandFrame f = _app.Input.Frame(h);
            return f != null && f.isTracked ? HandMath.PinchPoint(f) : h.Pointers[0].Tip;
        }

        /// <summary>The instruments on display (VR room only: mixed reality shows none) and their boxes.</summary>
        void RefreshInstruments()
        {
            for (int k = 0; k < _instruments.Length; k++)
            {
                InstrumentGazeSurface s = _instruments[k];
                var kind = (InstrumentKind)k;
                bool live = _layout != null && _layout.TryGetDisplayPoint(kind, out Vector3 p);
                if (!live) { s.Live = false; continue; }
                _layout.TryGetDisplayPoint(kind, out p);
                if (!s.Live || (p - _lastPoint[k]).sqrMagnitude > 1e-6f)
                {
                    _lastPoint[k] = p;
                    s.Box = Measure(kind, p);
                }
                s.Live = true;
            }
        }

        /// <summary>The instruments at the seat hide what stands behind them: their boxes go to the interactor.</summary>
        void RefreshOccluders()
        {
            int n = 0;
            for (int k = 0; k < InstrumentIds.KindCount && n < _interactor.Occluders.Length; k++)
            {
                var kind = (InstrumentKind)k;
                Rig rig = _rigs?.Get(InstrumentIds.RigOf(kind));
                if (_layout == null || rig == null || rig.Group == null || !_layout.IsInScene(kind) || !rig.Group.gameObject.activeInHierarchy)
                {
                    _seatValid[k] = false;
                    continue;
                }
                Transform g = rig.Group;
                if (!_seatValid[k] || (g.position - _seatPos[k]).sqrMagnitude > 1e-6f || Quaternion.Angle(g.rotation, _seatRot[k]) > 0.5f)
                {
                    _seatValid[k] = true;
                    _seatPos[k] = g.position;
                    _seatRot[k] = g.rotation;
                    _seatBox[k] = Measure(kind, g.position);
                }
                _interactor.Occluders[n++] = _seatBox[k];
            }
            _interactor.OccluderCount = n;
        }

        /// <summary>The world box of an instrument's visible meshes (its grab handles and played parts); a 0.5 m box at the
        /// pick point when none shows.</summary>
        Bounds Measure(InstrumentKind kind, Vector3 pick)
        {
            Rig rig = _rigs?.Get(InstrumentIds.RigOf(kind));
            bool any = false;
            var b = new Bounds(pick, Vector3.zero);
            if (rig != null)
            {
                Add(rig.Handles, ref b, ref any);
                for (int i = 0; i < rig.Instruments.Count; i++) if (rig.Instruments[i] != null) Add(rig.Instruments[i].PlayedParts, ref b, ref any);
            }
            if (!any) b = new Bounds(pick + Vector3.down * 0.2f, Vector3.one * 0.5f);
            return b;
        }

        static void Add(System.Collections.Generic.IReadOnlyList<Renderer> list, ref Bounds b, ref bool any)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Renderer r = list[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
        }

        // ---------------------------------------------------------------- the pinch (GazePinchSelector calls these)

        bool TryBegin(int hand)
        {
            if (!_active.Value || !_free[hand] || _interactor.State != InteractorState.Hover || _interactor.CommittedKey == 0) return false;
            HandState h = _app.Input.Hands[hand];
            if (!h.GrabPinchStart) return false;   // the app's own pinch edge this frame (never a pinch already held)
            _tap.Begin(hand, PinchPoint(h), _interactor.CommittedKey);
            return true;
        }

        void Released(int hand)
        {
            if (!_tap.Active || _tap.Hand != hand) return;
            long looking = 0;
            if (_app.Head != null)
            {
                Transform head = _app.Head;
                looking = _interactor.CastNow(new Ray(head.position, head.forward), out _, out _);
            }
            _tapOk = _tap.Release(looking);
            if (!_tapOk) Refused++;
        }

        void OnPointer(int slot, PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Unselect && _tapOk && GazeRules.SlotOf(_tap.Key) == slot) _act = _tap.Key;
        }

        void Choose(long key)
        {
            int slot = GazeRules.SlotOf(key);
            if (slot == 0)
            {
                if (_menu != null && _menu.GazeSelect(GazeRules.SubOf(key))) { Chosen++; Debug.Log("[Gaze] look and pinch: menu row"); }
                return;
            }
            var kind = (InstrumentKind)(slot - 1);
            if (_layout != null && _layout.Bring(kind, BringReason.Ray)) { Chosen++; Debug.Log("[Gaze] look and pinch: " + InstrumentIds.Of(kind)); }
            _interactor.ResetGaze();   // it is coming to the seat: nothing stays lit on its way
        }

        /// <summary>Everything off at once: the held pinch let go without a choice, the gaze forgotten, the glow out.</summary>
        void StandDown()
        {
            _tap.Cancel();
            _tapOk = false;
            _selector?.Cancel();
            _interactor?.ResetGaze();
        }

        // ---------------------------------------------------------------- the highlight

        void Show(float dt)
        {
            long key = 0;
            if (_active.Value)
            {
                if (_interactor.State == InteractorState.Select && _tap.Active) key = _tap.Key;
                else if (_interactor.State == InteractorState.Hover) key = _interactor.CommittedKey;
            }
            int slot = GazeRules.SlotOf(key);
            Target = slot < 0 ? GazeTargetKind.None : slot == 0 ? GazeTargetKind.MenuRow : GazeTargetKind.Instrument;
            if (slot >= 1) TargetInstrument = (InstrumentKind)(slot - 1);
            if (slot == 0) _menu?.GazeHover(GazeRules.SubOf(key));

            // the instrument's glow fades in on the gazed one and out on the last one
            float step = dt / GazeRules.FadeSeconds;
            if (slot >= 1)
            {
                if (_glowSlot != slot) { _glowSlot = slot; _glowK = Mathf.Min(_glowK, 0f); }
                _glowK = Mathf.Min(1f, _glowK + step);
            }
            else _glowK = Mathf.Max(0f, _glowK - step);
            if (_glowK > 0f && _glowSlot >= 1) Glow(_instruments[_glowSlot - 1], _glowK);
            if (_glowK <= 0f) _glowSlot = -1;

            _shownFor = Target != GazeTargetKind.None ? _shownFor + dt : 0f;
        }

        /// <summary>One soft warm glow through the instrument's centre, facing the eyes, a little larger than it (the app's hover
        /// amber, steady): the instrument hides its middle, so it reads as a halo round it; never pushed back into a wall.</summary>
        void Glow(InstrumentGazeSurface s, float k)
        {
            HoverGlowPool pool = _app.HoverGlow;
            Transform head = _app.Head;
            if (pool == null || head == null || s.Box.size.sqrMagnitude <= 0f) return;
            Vector3 c = s.Box.center, e = s.Box.extents;
            Vector3 dir = c - head.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : head.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            float wide = Mathf.Min(GlowMax, 2f * (Mathf.Abs(side.x) * e.x + Mathf.Abs(side.z) * e.z) * GlowScale);
            float tall = Mathf.Min(GlowMax, 2f * e.y * GlowScale);
            var spec = HoverGlowSpec.At(c, -dir, wide, tall, k, GlowGain, steady: true);
            spec.Axis = side;
            pool.Add(spec);
        }

        // ---------------------------------------------------------------- Sunny

        /// <summary>The first time something lights: Sunny says to look and pinch (once ever; a busy Sunny is asked again).</summary>
        void AskSunny(float dt)
        {
            if (_tipDone) return;
            if (Chosen > 0) { _tipDone = true; return; }   // already found it: nothing to teach
            _tipAskIn -= dt;
            if (_shownFor < GazeRules.TipAfter || _tipAskIn > 0f) return;
            _tipAskIn = GazeRules.TipEvery;
            if (_guide == null) return;
            if (_guide.TipSeen(GazeRules.TipId) || _guide.Tip(GazeRules.TipId) > 0f) _tipDone = true;
        }
    }
}
