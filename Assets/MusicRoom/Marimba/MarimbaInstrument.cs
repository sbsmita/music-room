// The marimba in the room (BUILD-GUIDE 4.2; spec 4-8): the model (MarimbaView), the per-frame glue (MarimbaController: the
// bare-hands rules of MarimbaPlay, the mallets, the drawn mallets) and every platform interface: IInstrument, IStowable,
// IInstrumentPlayback, IGuideAnchors, ILessonTargets, ILessonAccompany, IContactSurface, and the mallets' IHandToolProvider
// (MalletToolProvider). Live strokes go to the sound unit (audio.ActiveUnit(UnitId.Marimba) as IMarimbaSound), the bar's
// dip, the recorder (marimba / marimbaDamp / marimbaUndamp: struck bars are not Held, so they ring through loop
// boundaries), PlayStamp, and Sunny's signals (InstrumentSignalEvent{Marimba, "marimba.*"}). The tip counters Sunny's
// triggers need are counted here (noRoll, noSharps, ringing; the dead-stroke tip is Manual and turns the setting on).
// Allocation-free per frame.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public sealed class MarimbaInstrument : MonoBehaviour, IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors,
        ILessonTargets, ILessonAccompany, IContactSurface, IMarimbaHandFrames
    {
        public const string DeadStrokeKey = MarimbaController.DeadStrokeKey;
        public const float H = MarimbaGeometry.HSeat;
        /// <summary>Tip counters (spec 8): strokes before the roll tip / the sharps tip; strokes after the first roll before
        /// the dead-stroke tip; seconds of silence with a bar still ringing (the hand-mute tip, the second time).</summary>
        public const int NoRollStrokes = 30, NoSharpStrokes = 20, DeadAfterRollStrokes = 40;
        public const double RingingSilenceMs = 2000;
        public const float RingingLevel = 1e-3f;

        App _app;
        InstrumentDescriptor _d;
        IAudioEngine _audio;
        IRecorder _recorder;
        IGuide _guide;
        ITutorial _tutorial;
        bool _stowed;
        Action<ArrangeModeEvent> _onArrange;
        // the tip counters
        int _strokes, _strokesSinceRoll, _ringingCount;
        bool _rollSeen, _sharpSeen, _noRollSent, _noSharpSent, _ringingSent, _ringingCounted, _deadTipTried;
        double _lastStrikeMs = double.NegativeInfinity;

        public Rig Rig { get; private set; }
        public MarimbaView View { get; private set; }
        public MarimbaController Controller { get; private set; }
        public MalletToolProvider Tools { get; private set; }
        public InstrumentDescriptor Descriptor => _d;
        /// <summary>Tests: a scripted desktop click (button + world ray + shift) instead of the live mouse.</summary>
        public bool ScriptedMouse, ScriptedButton, ScriptedShift;
        public Ray ScriptedRay;
        bool _scriptedWasDown;

        public static MarimbaInstrument Create(App app, InstrumentDescriptor d)
        {
            var go = new GameObject("Marimba");
            go.transform.SetParent(app.WorldRoot, false);
            var m = go.AddComponent<MarimbaInstrument>();
            m.Setup(app, d);
            return m;
        }

        void Setup(App app, InstrumentDescriptor d)
        {
            _app = app;
            _d = d;
            _audio = app.Get<IAudioEngine>();
            transform.localPosition = d.Seat.Home;
            transform.localRotation = Quaternion.Euler(0f, d.Seat.HomeYaw, 0f);
            View = MarimbaView.Build(transform, app.Materials, H);
            View.Root.transform.localPosition = Vector3.zero;
            Controller = new MarimbaController(null, this, H, app.Settings.GetInt(DeadStrokeKey, 0) != 0) { View = View };
            Controller.Record = OnRecord;
            Controller.Signal = OnSignal;
            Controller.SetRigPose(transform.position, transform.rotation);
            Controller.Mallets.SnapRestingToRig();
            Tools = new MalletToolProvider(this);
            Rig = new Rig
            {
                Id = d.Rig, Group = transform, Zone = new Zone(this), Priority = d.Seat.Priority,
                HomeAll = d.Seat.Home, HomeYawAll = d.Seat.HomeYaw, HomeSingle = d.Seat.Single, HomeYawSingle = d.Seat.SingleYaw,
                Handles = View.Handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IStowable>(this);
            app.Register<IInstrumentPlayback>(this);
            app.Register<IGuideAnchors>(this);
            app.Register<ILessonTargets>(this);
            app.Register<ILessonAccompany>(this);
            app.Register<IContactSurface>(this);
            app.Register<IHandToolProvider>(Tools);
            for (int s = 0; s < 2; s++) app.Input.RegisterExternal(Controller.Mallets[s].HeadPointer);
            _onArrange = e => { if (e.Active) Quiet(); };
            app.Events.Subscribe(_onArrange);
            _onPlacing = e => { if (e.Active) Quiet(); };   // MR placing ticks no instrument either
            app.Events.Subscribe(_onPlacing);
        }

        Action<PlacingEvent> _onPlacing;

        public void Shutdown()
        {
            if (_app == null) return;
            if (_onArrange != null) _app.Events.Unsubscribe(_onArrange);
            if (_onPlacing != null) _app.Events.Unsubscribe(_onPlacing);
            for (int s = 0; s < 2; s++) _app.Input.UnregisterExternal(Controller.Mallets[s].HeadPointer);
        }

        void OnDestroy() => View?.Dispose();

        IMarimbaSound Sound => _audio != null && _audio.IsReady ? _audio.ActiveUnit(UnitId.Marimba) as IMarimbaSound : null;
        HandFrame IMarimbaHandFrames.Frame(HandState hand) => _app.Input.Frame(hand);

        // ---------------------------------------------------------------- frame phases (MarimbaModule)

        /// <summary>FramePhase.StickPoses: held mallets follow their hands before PointerInput reads their heads.</summary>
        public void UpdateMalletPoses(float dt)
        {
            MarimbaMallets m = Controller.Mallets;
            Controller.SetRigPose(transform.position, transform.rotation);
            m.Present = IsPresent;
            if (m.AnyHeld && !IsPresent) m.ReleaseAll();   // the marimba left the seat this frame: no mallet stays in a hand
            m.SnapRestingToRig();
            m.UpdatePoses(dt);
        }

        /// <summary>FramePhase.StickReleases: a hand that stayed open lets its mallet go.</summary>
        public void UpdateMalletReleases(float dt) => Controller.Mallets.UpdateReleases(dt);

        public void EndFrame(float dt)
        {
            if (!IsPresent) return;
            Controller.ApplyMallets(View);
        }

        // ---------------------------------------------------------------- IInstrument

        public InstrumentKind Kind => InstrumentKind.Marimba;
        public int TickOrder => _d.TickOrder;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => isActiveAndEnabled && Rig != null && Rig.InScene && !_stowed;
        public Transform Root => transform;
        public IReadOnlyList<Renderer> PlayedParts => View.PlayedParts;
        public bool Stowed => _stowed;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            double now = _app.Clock.NowMs;
            Controller.Sound = Sound;
            Controller.InLesson = InMarimbaLesson();
            Controller.SetRigPose(transform.position, transform.rotation);
            Controller.TickPointers(pointers, dt, now);
            View.Tick(dt);
            TipCounters(now);
            DesktopClick();
        }

        bool InMarimbaLesson()
        {
            if (_tutorial == null) _tutorial = _app.Get<ITutorial>();
            if (_tutorial == null) return false;
            TutorialState s = _tutorial.State;
            return s != TutorialState.Idle && s != TutorialState.Done && _tutorial.Instrument == InstrumentKind.Marimba;
        }

        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }   // no glows (platform.md 3.3)

        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (!IsPresent) return PlaySurface.None;
            Vector3 l = transform.InverseTransformPoint(world);
            int b = MarimbaMalletVisual.BarUnder(l, H, out float top);
            return b >= 0 && l.y <= top + 0.08f && l.y >= top - 0.06f ? PlaySurface.Instrument : PlaySurface.None;
        }

        void DesktopClick()
        {
            if (_app.IsPresenting) { _scriptedWasDown = ScriptedMouse && ScriptedButton; return; }
            bool pressed, shift;
            Ray ray;
            if (ScriptedMouse)
            {
                pressed = ScriptedButton && !_scriptedWasDown;
                _scriptedWasDown = ScriptedButton;
                ray = ScriptedRay;
                shift = ScriptedShift;
            }
            else
            {
                pressed = DesktopInput.MouseDown(0) && !DesktopInput.Alt;
                if (!pressed) return;
                shift = DesktopInput.Shift;
                Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
                if (cam == null) return;
                ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            }
            if (!pressed) return;
            if (!Physics.Raycast(ray, out RaycastHit hit, 20f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return;
            for (int b = 0; b < MarimbaGeometry.Bars; b++)
                if (hit.collider == View.BarCollider(b)) { Controller.Click(b, hit.point, shift); return; }
        }

        // ---------------------------------------------------------------- live play out

        void OnRecord(TakeEvent e)
        {
            if (_recorder == null) _recorder = _app.Get<IRecorder>();   // Lessons (Order 40) registers it after us
            _recorder?.Add(e);
            if (e.type == MarimbaEvents.Strike) CountStroke(e.midi - MarimbaGeometry.Midi0);
        }

        void OnSignal(MarimbaSignal s)
        {
            if (s == MarimbaSignal.Roll) { _rollSeen = true; _strokesSinceRoll = 0; }
            if (s == MarimbaSignal.BackRow) _sharpSeen = true;
            Publish(MarimbaSignals.Name(s));
        }

        void Publish(string signal) => _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Marimba, Signal = signal });

        void CountStroke(int bar)
        {
            _strokes++;
            _lastStrikeMs = _app.Clock.NowMs;
            _ringingCounted = false;
            if (_rollSeen) _strokesSinceRoll++;
            if (!_noRollSent && !_rollSeen && _strokes >= NoRollStrokes) { _noRollSent = true; Publish("marimba.noRoll"); }
            if (!_noSharpSent && !_sharpSeen && _strokes >= NoSharpStrokes) { _noSharpSent = true; Publish("marimba.noSharps"); }
            if (!_deadTipTried && _rollSeen && _strokesSinceRoll >= DeadAfterRollStrokes && !Controller.InLesson && !Controller.DeadStrokeSetting)
            {
                _deadTipTried = true;
                if (_guide == null) _guide = _app.Get<IGuide>();
                // Manual tip: showing it turns dead strokes on (spec 5.4); a tip heard before (persisted) turns them on too
                if (_guide != null && (_guide.Tip(MarimbaTips.DeadStrokeTip) > 0f || _guide.TipSeen(MarimbaTips.DeadStrokeTip))) EnableDeadStrokes();
            }
        }

        /// <summary>Dead strokes on (the stored setting musicroom.marimba.deadStroke = 1).</summary>
        public void EnableDeadStrokes()
        {
            Controller.DeadStrokeSetting = true;
            _app.Settings.SetInt(DeadStrokeKey, 1);
        }

        void TipCounters(double now)
        {
            if (_ringingSent || _ringingCounted || now - _lastStrikeMs < RingingSilenceMs) return;
            IMarimbaSound s = Controller.Sound;
            if (s == null) return;
            bool ringing = false;
            for (int b = 0; b < MarimbaGeometry.Bars && !ringing; b++) ringing = s.BarLevel(b) > RingingLevel;
            if (!ringing) return;
            _ringingCounted = true;
            if (++_ringingCount >= 2) { _ringingSent = true; Publish("marimba.ringing"); }
        }

        /// <summary>Arrange mode, a stow: every mute dropped, the bars silenced, the mallets back on the tray.</summary>
        void Quiet()
        {
            Controller.Sound = Sound;
            Controller.Quiet(_app.Clock.NowMs, layMalletsDown: true);
            View.SetDisplayMode(_stowed);
        }

        // ---------------------------------------------------------------- IStowable

        public void SetStowed(bool stowed, StowMount mount)
        {
            _stowed = stowed;
            Controller.View = stowed ? null : View;   // a displayed marimba plays back without moving its bars
            if (stowed) Quiet();
            View.SetDisplayMode(stowed);
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            Controller.Sound = Sound;
            return Controller.PlayRecorded(e);
        }

        public void ReleaseAll()
        {
            Controller.Sound = Sound;
            Controller.ReleaseAllPlayback();
        }

        public void CloseLane(int lane) { }   // struck bars: no continuous lane

        // ---------------------------------------------------------------- IGuideAnchors

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up) =>
            MarimbaAnchors.TryGetAnchor(anchor, transform.position, transform.rotation, H, out world, out up);

        // ---------------------------------------------------------------- ILessonTargets / ILessonAccompany

        public bool Validate(in LessonTarget t, out string why) => MarimbaLessons.Validate(t, out why);

        public bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size) =>
            MarimbaLessons.TryGetLanding(t, transform.position, transform.rotation, H, out landing, out approach, out size);

        public bool Matches(in LessonTarget t, in TakeEvent e) => MarimbaLessons.Matches(t, e);

        public void ShowTarget(in LessonTarget t, float k)
        {
            if (t != null && t.i >= 0 && t.i < MarimbaGeometry.Bars) View.SetTargetLift(t.i, k);
        }

        public string Hint(IReadOnlyList<LessonTarget> targets) => MarimbaLessons.Hint(targets);

        void ILessonAccompany.Play(int[] midi, float vel)
        {
            Controller.Sound = Sound;
            Controller.Accompany(midi, vel);
        }

        void ILessonAccompany.Release() { }   // the bars ring out

        // ---------------------------------------------------------------- IContactSurface

        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (!IsPresent || Rig.Held) return false;
            Vector3 l = transform.InverseTransformPoint(world);
            int b = MarimbaMalletVisual.BarUnder(l, H, out float top);
            if (b < 0) return false;
            float height = l.y - top;
            if (height > above || height < -below) return false;
            Vector3 n = transform.up;
            hit = new ContactHit
            {
                Point = transform.TransformPoint(new Vector3(l.x, top, l.z)), Normal = n, Tangent = transform.right,
                ClipHalf = MarimbaGeometry.Width(b) * 0.5f, ClipOrigin = transform.TransformPoint(new Vector3(MarimbaGeometry.X(b), top, l.z)),
                Height = height, Use = ContactUse.Both,
            };
            return true;
        }

        /// <summary>The rig zone: the instrument's local bounds grown by GrabConstants.ZonePad.</summary>
        sealed class Zone : IPlayZone
        {
            static readonly Bounds Local = new Bounds(new Vector3(0f, 0.40f, 0.18f), new Vector3(1.30f, 0.80f, 0.94f));
            readonly MarimbaInstrument _m;
            public Zone(MarimbaInstrument m) { _m = m; }
            public bool Contains(Vector3 world)
            {
                if (_m == null || !_m.isActiveAndEnabled) return false;
                Bounds b = Local;
                b.Expand(GrabConstants.ZonePad * 2f);
                return b.Contains(_m.transform.InverseTransformPoint(world));
            }
        }
    }

    /// <summary>The mallets as a hand tool (platform.md 4.6): Layout's grab pass asks NearestFree and picks up through the
    /// nearest provider.</summary>
    public sealed class MalletToolProvider : IHandToolProvider
    {
        readonly MarimbaInstrument _m;
        public MalletToolProvider(MarimbaInstrument m) { _m = m; }
        public InstrumentKind Owner => InstrumentKind.Marimba;
        MarimbaMallets M => _m.Controller.Mallets;
        public float NearestFree(Vector3 tip) => _m.IsPresent ? M.NearestFree(tip) : float.PositiveInfinity;
        public StickPick TryPickUp(HandState hand, Vector3 tip) => _m.IsPresent ? M.TryPickUp(hand, tip) : StickPick.None;
        public bool AnyHeld => M.AnyHeld;
        public void ReleaseAll() => M.ReleaseAll();
        public bool Holds(HandState hand) => hand != null && hand.Holding is MarimbaMalletTool m && ReferenceEquals(m.Hand, hand) && (ReferenceEquals(M[0], m) || ReferenceEquals(M[1], m));
        public void Release(HandState hand) { if (Holds(hand)) M.Release((MarimbaMalletTool)hand.Holding); }
        public int ShadowSpheres(Span<Vector4> centreRadius) => M.ShadowSpheres(centreRadius);
    }
}
