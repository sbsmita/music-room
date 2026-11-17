// The handpan module (Order 33; BUILD-GUIDE 4.2): builds the instrument (HandpanModel) at its seat home under the world,
// registers it with the platform (Rig, IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets,
// IContactSurface, IHandpanApi), parents its one source to the shell, and every frame turns the real hands into play
// (HandpanHandFeed -> HandpanPlay -> the unit, the recorder, the settle, Sunny's signals).
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Handpan
{
    public sealed class HandpanModule : AppModule
    {
        public const int ModuleOrder = 33;
        public const int InstrumentTickOrder = 21;
        public override int Order => ModuleOrder;
        public HandpanInstrument Instrument { get; private set; }

        public override void Init(App app)
        {
            InstrumentDescriptor d = app.Registry.Get(InstrumentKind.Handpan) ?? new HandpanDefinition().Describe();
            Instrument = new HandpanInstrument(app, d);
            Debug.Log($"[Handpan] ready: 9 fields D3 A3 C4 D4 E4 F4 G4 A4 C5, {Instrument.Model.Triangles} triangles at the seat, {Instrument.Model.DisplayTriangles} on display, voice {HandpanVoiceChoice.Voice}");
        }

        /// <summary>After Layout moved the rig: the shell's settle and its glide between the wall and the stand; Sunny's polls.</summary>
        public override void Tick(FramePhase phase, float dt)
        {
            if (phase == FramePhase.LayoutAnimation) Instrument?.LateTick(dt);
        }

        public override void Shutdown() { Instrument?.Dispose(); Instrument = null; }
    }

    public sealed class HandpanInstrument : IInstrument, IHandpanApi, IInstrumentPlayback, ILessonTargets, IGuideAnchors, IStowable, IContactSurface, System.IDisposable
    {
        readonly App _app;
        readonly InstrumentDescriptor _d;
        public readonly HandpanModel Model;
        public readonly Rig Rig;
        readonly HandpanPlay _play = new HandpanPlay();
        readonly HandpanFrame _frame = new HandpanFrame();
        readonly HandpanTipCounters _tips = new HandpanTipCounters();
        readonly IAudioEngine _audio;
        IRecorder _recorder;
        ILooper _looper;
        ILayoutService _layout;
        bool _stowed;
        // tip bookkeeping: sounded notes waiting to see whether their own finger mutes them within 0.5 s
        readonly double[] _strikeAt = new double[HandpanSpec.FieldCount];
        readonly bool[] _watch = new bool[HandpanSpec.FieldCount];
        double _ringingSince = -1, _pollAt, _lastLoopPlay = -1e9;
        // desktop click edge
        bool _scriptedWasDown;
        /// <summary>Tests: a scripted desktop click (button + world ray) instead of the live mouse.</summary>
        public bool ScriptedMouse, ScriptedButton;
        public Ray ScriptedRay;
        /// <summary>Live strikes so far (tests, diagnostics).</summary>
        public int LiveStrikes { get; private set; }
        public int PlaybackStrikes { get; private set; }

        public HandpanInstrument(App app, InstrumentDescriptor d)
        {
            _app = app;
            _d = d;
            _audio = app.Get<IAudioEngine>();
            Model = HandpanModel.Build(app.WorldRoot, app.Materials, desktopCollider: true, textures: !app.Headless);
            Transform g = Model.Root;
            g.name = "Handpan";
            g.localPosition = d.Seat.Home;
            g.localRotation = Quaternion.Euler(0f, d.Seat.HomeYaw, 0f);
            Rig = new Rig
            {
                Id = RigId.Handpan, Group = g, Zone = new HandpanZone(Model), Priority = d.Seat.Priority,
                HomeAll = d.Seat.Home, HomeYawAll = d.Seat.HomeYaw, HomeSingle = d.Seat.Single, HomeYawSingle = d.Seat.SingleYaw,
                Handles = Model.Handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IStowable>(this);
            app.Register<IInstrumentPlayback>(this);
            app.Register<IGuideAnchors>(this);
            app.Register<ILessonTargets>(this);
            app.Register<IContactSurface>(this);
            app.Register<IHandpanApi>(this);
            // the source follows the shell (at the seat, gliding, on the wall): its centre, 6 cm over the rim plane
            _audio?.SetSourceParent(SourceId.Handpan, Model.Instrument, new Vector3(0f, 0.06f, 0f));
            app.Events.Subscribe<InstrumentMoveEvent>(OnMove);
            for (int i = 0; i < _strikeAt.Length; i++) _strikeAt[i] = -1e9;
        }

        IHandpanSound Sound => _audio != null && _audio.IsReady ? _audio.ActiveUnit(UnitId.Handpan) as IHandpanSound : null;
        HandpanUnit Unit => Sound as HandpanUnit;

        // ---------------------------------------------------------------- IInstrument (review item 15)

        public InstrumentKind Kind => InstrumentKind.Handpan;
        public int TickOrder => HandpanModule.InstrumentTickOrder;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => Model.Root.gameObject.activeInHierarchy && Rig.InScene && !_stowed;
        public Transform Root => Model.Root;
        public IReadOnlyList<Renderer> PlayedParts => Model.Played;
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }   // no glows: contact shadows only (E3)

        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (!IsPresent) return PlaySurface.None;
            HandpanContact c = HandpanMath.ContactOf(D3.From(world), 0, Model.Place);
            return c.OnTop && c.H > -0.03 && c.H < 0.08 ? PlaySurface.Instrument : PlaySurface.None;
        }

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            if (!IsPresent || Rig.Held || Model.Current != HandpanModel.Config.Seat || Model.Blending) return;
            _play.Place = Model.Place;
            HandpanHandFeed.Fill(_frame, _app.Input, _app.Clock.Now, dt);   // the mouse hand never plays (the feed skips it)
            int n = _play.Step(_frame);
            for (int k = 0; k < n; k++) Dispatch(_play.Events[k], live: true);
            DesktopClick();
        }

        /// <summary>Per frame after Layout (the module's LayoutAnimation phase): the settle and the place blend; Sunny's polls.</summary>
        public void LateTick(float dt)
        {
            Model.Tick(dt);
            if (!IsPresent) return;
            // At the seat, unstowed and still, the shell always rests on its stand: a move toward the wall that never became
            // a display (the glide was turned back, a slide, a reseat) eases it back down instead of leaving it in the air.
            if (!Model.Blending && Model.Current != HandpanModel.Config.Seat) Model.BeginMove(toSeat: true, seconds: 0.5f);
            double now = _app.Clock.Now;
            for (int i = 0; i < HandpanSpec.FieldCount; i++)
                if (_watch[i] && now - _strikeAt[i] > 0.5) { _watch[i] = false; Publish(_tips.RangFree(true)); }
            if (now < _pollAt) return;
            _pollAt = now + 0.5;
            int ringing = 0;
            IHandpanSound s = Sound;
            if (s != null) for (int i = 0; i < HandpanSpec.FieldCount; i++) if (s.Level(i) > 1e-3f) ringing++;   // about -40 dB re a mf note's peak level
            if (ringing >= 4) { if (_ringingSince < 0) _ringingSince = now; } else _ringingSince = -1;
            _looper ??= _app.Get<ILooper>();
            bool loopIdle = _looper != null && _looper.Active && _looper.Layers > 0 && now - _lastLoopPlay > 8.0;
            Publish(_tips.Poll(_ringingSince >= 0 && now - _ringingSince >= 3.0, loopIdle));
            if (_ringingSince >= 0 && now - _ringingSince >= 3.0) _ringingSince = -1;
        }

        void Publish(int n)
        {
            for (int i = 0; i < n; i++) _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Handpan, Signal = _tips.Out[i] });
        }

        /// <summary>One play event: the sound, the shell's settle, the take event (live only), the stamp, Sunny's counters.</summary>
        void Dispatch(in HandpanEvent e, bool live, int lane = 0)
        {
            IHandpanSound s = Sound;
            double now = _app.Clock.Now;
            switch (e.Type)
            {
                case HandpanEventType.Strike:
                    if (e.Field == HandpanSpec.Tak)
                    {
                        HandpanMath.TakNeighbours(e.U, e.V, out HandpanNeighbour n0, out HandpanNeighbour n1);
                        Unit?.StrikeTak(e.Vel, n0.Field, n0.W, n1.Field, n1.W, 0);
                    }
                    else s?.Strike(e.Field, (float)e.Vel, (float)e.U, (float)e.V, e.Tech, 0);
                    if (!_stowed) Model.Struck(e.Vel, e.Tech);
                    if (!live) { PlaybackStrikes++; break; }
                    LiveStrikes++;
                    Record(TakeEventHandpan.Strike(e), e.Hand, now);
                    if (_looper != null && _looper.Active) _lastLoopPlay = now;
                    double rho = e.Field < HandpanSpec.FieldCount ? System.Math.Sqrt(e.U * e.U + e.V * e.V) : 1;
                    var tech = e.Tech == HandpanTech.Tak ? HandpanTechKind.Tak : e.Tech == HandpanTech.Slap ? HandpanTechKind.Slap : HandpanTechKind.Tip;
                    Publish(_tips.Strike(e.Field, e.Vel, rho, tech, now));
                    if (e.Field < HandpanSpec.FieldCount && e.Tech == HandpanTech.Tip) { _strikeAt[e.Field] = now; _watch[e.Field] = true; }
                    break;
                case HandpanEventType.Damp:
                case HandpanEventType.Release:
                    float strength = e.Type == HandpanEventType.Release ? 0f : (float)e.Strength;
                    s?.Damp(e.Field, e.Slot, strength, (float)e.U, (float)e.V, e.Kind == HandpanDampSource.Finger ? HandpanDampKind.Finger : HandpanDampKind.Palm, 0);
                    if (!live) break;
                    Record(TakeEventHandpan.Mute(e), e.Hand, now);
                    if (e.Type == HandpanEventType.Damp && e.AfterStrike && e.Field < HandpanSpec.FieldCount && _watch[e.Field] && now - _strikeAt[e.Field] <= 0.5)
                    { _watch[e.Field] = false; Publish(_tips.RangFree(false)); }
                    break;
                case HandpanEventType.Hint:
                    if (live) _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Handpan, Signal = HandpanTips.SignalLift });
                    break;
            }
        }

        void Record(in TakeEvent ev, int hand, double now)
        {
            _recorder ??= _app.Get<IRecorder>();   // Lessons (Order 40) registers it after this module
            _recorder?.Add(ev);
            if (hand >= 0 && hand < 2) PlayStamp.Mark(_app.Input.Hands[hand], InstrumentKind.Handpan, now * 1000.0);
        }

        void DesktopClick()
        {
            // the desktop path (ARCHITECTURE 0): a left click on the top cap's collider (Layers.PlaySurface) plays vel 0.75
            // at the hit point; the mouse hand's pointers never strike, so a click never fires twice (conformance 16)
            if (_app.IsPresenting) { _scriptedWasDown = ScriptedMouse && ScriptedButton; return; }
            bool pressed;
            Ray ray;
            if (ScriptedMouse) { pressed = ScriptedButton && !_scriptedWasDown; _scriptedWasDown = ScriptedButton; ray = ScriptedRay; }
            else
            {
                pressed = DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt;
                if (!pressed) return;
                Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
                if (cam == null) return;
                ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            }
            if (!pressed || Model.DesktopCollider == null) return;
            Physics.SyncTransforms();   // a press is rare: bring the collider up to date with the rig Layout moved (as the harp)
            if (!Physics.Raycast(ray, out RaycastHit hit, 20f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide) || hit.collider != Model.DesktopCollider) return;
            HandpanContact c = HandpanMath.ContactOf(D3.From(hit.point), 0, Model.Place);
            if (c.Hit.Field < 0) return;
            bool tak = c.Hit.Field == HandpanSpec.Tak;
            var e = new HandpanEvent
            {
                Type = HandpanEventType.Strike, Field = c.Hit.Field, Vel = 0.75, Tech = tak ? HandpanTech.Tak : HandpanTech.Tip,
                U = tak ? c.X : c.Hit.U, V = tak ? c.Z : c.Hit.V, Hand = -1, Finger = -1,
            };
            Dispatch(e, live: true);
        }

        // ---------------------------------------------------------------- IStowable and the glide

        public bool Stowed => _stowed;

        public void SetStowed(bool stowed, StowMount mount)
        {
            if (stowed && !_stowed)
            {
                int n = _play.ReleaseHeld(_app.Clock.Now);   // a hand resting on it: its damps go up with it
                for (int k = 0; k < n; k++) Dispatch(_play.Events[k], live: false);
            }
            _stowed = stowed;
            _play.Reset();
            for (int i = 0; i < _watch.Length; i++) _watch[i] = false;
            Model.SetConfig(!stowed ? HandpanModel.Config.Seat : mount == StowMount.Wall ? HandpanModel.Config.Wall
                : mount == StowMount.Surface ? HandpanModel.Config.Surface : HandpanModel.Config.Seat);
        }

        void OnMove(InstrumentMoveEvent e)
        {
            if (e.Kind != InstrumentKind.Handpan || e.Phase != MovePhase.Leaving) return;
            // leaving the wall for the seat: the shell eases off the bracket down to the seat pose during the glide and the
            // stand shows once it rests on it; leaving the seat for the wall: the shell leaves the stand (hidden at once) and
            // eases into its hanging pose
            // (0.85 s: inside the shortest glide, RigGlide.BaseSeconds 0.9 s, so the pose is reached before Arrived / Displayed)
            if (_stowed || Model.Current != HandpanModel.Config.Seat) Model.BeginMove(toSeat: true, seconds: 0.85f);
            else
            {
                // only on a real glide to the wall (ILayoutService.IsGoingToDisplay). Every other Leaving keeps the shell on
                // its stand: a page turn or a mixed-reality slide that brings it IN, a reseat to another slot of My set, a
                // slide out (it is hidden when it lands). Asking only "is it moving" here lifted the shell to the bracket's
                // height (1.58 m) above a stand left on the floor.
                _layout ??= _app.Get<ILayoutService>();
                if (_d.Stow != null && _d.Stow.Mount == StowMount.Wall && _layout != null && _layout.IsGoingToDisplay(InstrumentKind.Handpan))
                    Model.BeginMove(toSeat: false, seconds: 0.85f);
            }
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            if (!TakeEventHandpan.TryParse(e, out HandpanEvent h)) return false;
            Dispatch(h, live: false, lane: e.lane);
            return true;
        }

        public void ReleaseAll() => Sound?.ReleaseAll(0);
        public void CloseLane(int lane) { }

        // ---------------------------------------------------------------- IContactSurface (E1 / E3)

        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            if (!IsPresent || Rig.Held) { hit = default; return false; }
            return Model.Contact.QueryContact(world, above, below, out hit);
        }

        // ---------------------------------------------------------------- IHandpanApi

        public int FieldCount => HandpanSpec.FieldCount;
        public int MidiOf(int field) => HandpanSpec.MidiOf(field);
        public Vector3 FieldCentre(int field) => HandpanMath.FieldApexWorld(field, Model.Place, out _).ToVector3();
        public Vector3 FieldNormal(int field) { HandpanMath.FieldApexWorld(field, Model.Place, out D3 n); return n.ToVector3(); }
        public Vector3 SoundOrigin => Model.Instrument.TransformPoint(0f, 0.06f, 0f);
        public float FieldLevel(int field) => Sound?.Level(field) ?? 0f;

        // ---------------------------------------------------------------- ILessonTargets (handpan.md 8.1)

        public bool Validate(in LessonTarget t, out string why)
        {
            why = t.i < 0 || t.i >= HandpanSpec.FieldCount ? $"field {t.i} is outside 0..8"
                : t.midi >= 0 && t.midi != HandpanSpec.MidiOf(t.i) ? $"field {t.i} is midi {HandpanSpec.MidiOf(t.i)}, not {t.midi}" : null;
            return why == null;
        }

        HandpanBlockPath Path(in LessonTarget t)
        {
            Transform head = _app.Head;
            D3 eyes = head != null ? D3.From(head.position) : HandpanLessonPath.SeatEyes;
            return HandpanLessonPath.Of(Mathf.Clamp(t.i, 0, HandpanSpec.FieldCount - 1), Model.Place, eyes);
        }

        public bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size)
        {
            int i = Mathf.Clamp(t.i, 0, HandpanSpec.FieldCount - 1);
            HandpanBlockPath b = Path(t);
            Vector3 n = b.Normal.ToVector3();
            landing = new Pose(b.Apex.ToVector3(), Quaternion.LookRotation(Vector3.ProjectOnPlane(-b.Dir.ToVector3(), n).sqrMagnitude > 1e-8f ? Vector3.ProjectOnPlane(-b.Dir.ToVector3(), n) : Vector3.forward, n));
            approach = (-b.Dir.ToVector3()).normalized;   // from the start (toward the eyes) to the apex
            HandpanField F = HandpanSpec.Fields[i];
            float d = (float)(F.DimpleFrac * F.Short * 0.6);   // a disc 60 % of the dimple's width
            size = new Vector2(d, d);
            return true;
        }

        /// <summary>The block's travel: the line of sight with the forward clamp (handpan.md 8.1: the near fields' is 0.135 m).</summary>
        public float Travel(in LessonTarget t, float travel) => Mathf.Min(travel, (float)Path(t).Room);

        public bool Matches(in LessonTarget t, in TakeEvent e) => e.type == HandpanDefinition.Onset && e.i == t.i;
        /// <summary>The disc block is the cue: nothing glows on the steel. The last cue is kept for tests (as the stub's).</summary>
        public void ShowTarget(in LessonTarget t, float k) { if ((uint)t.i < (uint)_cue.Length) _cue[t.i] = k; }
        /// <summary>Tests: the last cue k the tutorial gave field i (0 = off).</summary>
        public float CueOf(int field) => (uint)field < (uint)_cue.Length ? _cue[field] : 0f;
        readonly float[] _cue = new float[HandpanSpec.FieldCount];
        public string Hint(IReadOnlyList<LessonTarget> targets)
        {
            if (targets == null || targets.Count == 0) return "Tap the handpan";
            return targets[0].i == 0 ? "Tap the ding" : "Tap " + (string.IsNullOrEmpty(targets[0].note) ? HandpanSpec.Fields[Mathf.Clamp(targets[0].i, 0, 8)].Note : targets[0].note);
        }

        // ---------------------------------------------------------------- IGuideAnchors

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
        {
            bool ok = HandpanAnchors.TryGet(anchor, Model.Place, out D3 w, out D3 u);
            world = w.ToVector3(); up = u.ToVector3();
            return ok;
        }

        public void Dispose()
        {
            _app.Events.Unsubscribe<InstrumentMoveEvent>(OnMove);
            Model.Dispose();
        }
    }

    /// <summary>The handpan's take events (handpan.md 5.5; BUILD-GUIDE 6.1: existing TakeEvent fields only):
    ///   "handpan" (Onset): i field 0..8 (9 = a tak), midi, vel, u and pos = the strike point (u, v) in field coordinates (a tak:
    ///   the map point X, Z; playback recomputes its neighbours), mode "tip" | "slap" | "tak", zone "ding" | "tone" | "tak";
    ///   "handpanMute" (Release): i field (9 = the whole shell), vel = strength (0 = lifted), zone = slot "0".."3" (L fingers,
    ///   R fingers, L palm, R palm), mode "finger" | "palm" | "shell", u and pos = the resting point.</summary>
    public static class TakeEventHandpan
    {
        static readonly string[] Slots = { "0", "1", "2", "3" };
        public static TakeEvent Strike(in HandpanEvent e) => new TakeEvent
        {
            type = HandpanDefinition.Onset, i = e.Field, midi = HandpanSpec.MidiOf(e.Field), vel = (float)e.Vel, u = (float)e.U, pos = (float)e.V,
            mode = e.Tech == HandpanTech.Tak ? "tak" : e.Tech == HandpanTech.Slap ? "slap" : "tip", zone = e.Field == 0 ? "ding" : e.Field == HandpanSpec.Tak ? "tak" : "tone",
        };
        public static TakeEvent Mute(in HandpanEvent e) => new TakeEvent
        {
            type = HandpanDefinition.Mute, i = e.Field, vel = e.Type == HandpanEventType.Release ? 0f : (float)e.Strength, zone = Slots[e.Slot & 3],
            mode = e.Kind == HandpanDampSource.Finger ? "finger" : e.Kind == HandpanDampSource.Palm ? "palm" : "shell", u = (float)e.U, pos = (float)e.V,
        };
        public static bool TryParse(in TakeEvent t, out HandpanEvent e)
        {
            e = default;
            if (t.type == HandpanDefinition.Onset)
            {
                e = new HandpanEvent { Type = HandpanEventType.Strike, Field = t.i, Vel = t.vel, U = t.u, V = t.pos, Hand = -1, Finger = -1,
                    Tech = t.mode == "tak" ? HandpanTech.Tak : t.mode == "slap" ? HandpanTech.Slap : HandpanTech.Tip };
                if (t.i == HandpanSpec.Tak) e.Tech = HandpanTech.Tak;
                return t.i >= 0 && t.i <= HandpanSpec.Tak;
            }
            if (t.type == HandpanDefinition.Mute)
            {
                int slot = t.zone == "1" ? 1 : t.zone == "2" ? 2 : t.zone == "3" ? 3 : 0;
                e = new HandpanEvent { Type = t.vel > 0f ? HandpanEventType.Damp : HandpanEventType.Release, Field = t.i, Slot = slot, Strength = t.vel, U = t.u, V = t.pos, Hand = -1,
                    Kind = t.mode == "palm" ? HandpanDampSource.Palm : t.mode == "shell" ? HandpanDampSource.Shell : HandpanDampSource.Finger };
                return t.i >= 0 && t.i <= HandpanSpec.Tak;
            }
            return false;
        }
    }

    /// <summary>The rig's zone: the shell's sphere of reach plus GrabConstants.ZonePad.</summary>
    sealed class HandpanZone : IPlayZone
    {
        readonly HandpanModel _m;
        public HandpanZone(HandpanModel m) { _m = m; }
        public bool Contains(Vector3 world) => _m.Root.gameObject.activeInHierarchy && Vector3.Distance(world, _m.Instrument.position) < (float)HandpanSpec.RimR + GrabConstants.ZonePad;
    }
}
