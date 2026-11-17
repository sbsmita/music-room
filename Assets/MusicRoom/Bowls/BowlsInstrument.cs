// The singing bowls in the room (bowls.md 3-8; BUILD-GUIDE 4.2): the model (BowlsVisuals), the hand logic (BowlsHands, in
// the rig's frame), the stick (BowlsStickTool, the platform's hand tool), Sunny's coach (BowlsCoach) and every platform
// interface: IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors, ILessonTargets, IContactSurface,
// IInstrumentSurface. Live play goes to the unit (audio.ActiveUnit(UnitId.Bowls) as IBowlsSound), the rocking, the
// recorder (BowlsEvents; rubs through BowlsRubEncoder on Core's ControlEncoder), PlayStamp and Sunny's signals
// (InstrumentSignalEvent { Bowls, "bowls.*" }).
// Live against playback on one bowl (review item 14): one LaneArbiter per bowl. The live hand (lane 0) takes a bowl from a
// loop or take lane (the unit re-seats the stick from the live gesture), a playback lane never takes it from the hand, a
// Release from a lane that lost the bowl is ignored, and once the hand lifts the loop's next control takes the bowl back.
// Lessons: a live strike posts a bowlHit marker, singing a bowlSing marker (u = seconds sung); a stalled event (15 s) is
// answered by an audible 0.7 m/s strike on any touch of the target bowl. Desktop: a click on a bowl strikes it, a
// circle-drag rubs it (4 N at the drag's speed), button up lifts. Allocation-free per frame.
using System;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using MusicRoom.Hands;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Bowls
{
    public sealed class BowlsInstrument : MonoBehaviour, IInstrument, IStowable, IInstrumentPlayback, IGuideAnchors,
        ILessonTargets, IContactSurface, IInstrumentSurface, IBowlsHandsSink, IBowlsStats, IBowlsHandFrames
    {
        /// <summary>The unit's lanes: the live hand on 0, any playback owner on 1 (the arbiter lets one lane own a bowl).</summary>
        public const int UnitLiveLane = 0, UnitPlaybackLane = 1;
        public const float DesktopTapSpeed = 0.8f, DesktopRubN = 4f, DesktopRubMin = 0.05f, DesktopHeightS = 0.85f;
        public const float AssistSpeed = (float)BowlsLessonRules.AssistSpeed;

        App _app;
        InstrumentDescriptor _d;
        IAudioEngine _audio;
        IRecorder _recorder;
        ITutorial _tutorial;
        ILessonLibrary _library;
        IGuide _guide;
        bool _stowed, _arranging;
        Action<ArrangeModeEvent> _onArrange;
        Action<bool> _onStick;

        public Rig Rig { get; private set; }
        public BowlsVisuals View { get; private set; }
        public BowlsHands Hands { get; private set; }
        public BowlsCoach Coach { get; private set; }
        public BowlsStickTool Stick { get; private set; }
        public BowlsToolProvider Tools { get; private set; }
        public InstrumentDescriptor Descriptor => _d;

        readonly List<Renderer> _handles = new List<Renderer>(1);
        readonly List<Renderer> _played = new List<Renderer>(3);
        readonly BowlsHandInput[] _in = { new BowlsHandInput(), new BowlsHandInput() };
        readonly BowlsHandBowl[] _bowls = new BowlsHandBowl[3];
        readonly BowlsRubEncoder[] _enc = new BowlsRubEncoder[3];
        readonly LaneArbiter[] _arb = { LaneArbiter.Free, LaneArbiter.Free, LaneArbiter.Free };
        readonly List<TakeEvent> _out = new List<TakeEvent>(16);
        readonly BoxCollider[] _click = new BoxCollider[3];
        // the playback stick's last command per bowl (a level-0 close glides from it)
        readonly BowlsPlaybackCmd[] _pb = new BowlsPlaybackCmd[3];
        readonly int[] _pbDamp = new int[3];
        /// <summary>Each bowl's rim angle toward the head (rig frame): the listener, and where an assisted strike lands.</summary>
        readonly float[] _facing = { Mathf.PI, Mathf.PI, Mathf.PI };
        // the live frame being processed (the sink's context)
        double _t, _frameDt;
        bool _desktopFrame;
        int _lastBowlRubbed = 0, _rubbingNow = -1;
        readonly BowlsRubLog _rubLog = new BowlsRubLog();
        bool _ticked;
        // lessons: the waited event, how long, its bowl
        int _lessonEvent = -1;
        double _waitT;
        // desktop
        int _dragBowl = -1;
        float _dragAngle, _dragT;
        bool _dragRub;
        string _signalScratch;
        Func<string, int> _resolve;

        /// <summary>Tests: a scripted desktop pointer (button + world ray) instead of the live mouse.</summary>
        public bool ScriptedMouse, ScriptedButton;
        public Ray ScriptedRay;
        bool _scriptedWasDown;
        /// <summary>Live strikes and contacts the hands made (tests); playback calls; the last lesson cue.</summary>
        public int LiveStrikes { get; private set; }
        public int PlaybackCalls { get; private set; }
        public int CueIndex { get; private set; } = -1;
        public float CueK { get; private set; }
        public LaneArbiter Arbiter(int bowl) => _arb[bowl];

        public static BowlsInstrument Create(App app, InstrumentDescriptor d)
        {
            var go = new GameObject("Bowls");
            go.transform.SetParent(app.WorldRoot, false);
            var b = go.AddComponent<BowlsInstrument>();
            b.Setup(app, d);
            return b;
        }

        void Setup(App app, InstrumentDescriptor d)
        {
            _app = app;
            _d = d;
            _audio = app.Get<IAudioEngine>();
            transform.localPosition = d.Seat.Home;
            transform.localRotation = Quaternion.Euler(0f, d.Seat.HomeYaw, 0f);
            View = new BowlsVisuals(transform, app.Materials);
            View.Stowed.transform.localPosition = new Vector3(0f, BowlsLayout.SillTop, 0f);
            _handles.Add(View.StandRenderer);
            for (int i = 0; i < 3; i++) _played.Add(View.BowlRenderers[i]);
            for (int i = 0; i < 3; i++)
            {
                _bowls[i] = new BowlsHandBowl { Pos = new double3(BowlsLayout.BowlX[i], BowlsLayout.BaseYD, BowlsLayout.BowlZ[i]), Yaw = 0, R = BowlsLayout.RimDiamD[i] / 2, H = BowlsLayout.HeightD[i], T = BowlsLayout.WallD[i], Index = i };
                _enc[i] = new BowlsRubEncoder(i, BowlsLayout.RimDiamD[i] / 2);
                _pb[i].bowl = i;
            }
            Hands = new BowlsHands(_bowls, this);
            Coach = new BowlsCoach(Hands);
            Stick = new BowlsStickTool(this);
            Tools = new BowlsToolProvider(Stick, () => IsPresent);
            _onStick = held => { if (held) Coach.OnPujaPicked(_app.Clock.Now); };
            Stick.Changed += _onStick;
            BuildClickTargets();
            _resolve = ResolveBowl;
            Rig = new Rig
            {
                Id = d.Rig, Group = transform, Zone = new Zone(this), Priority = d.Seat.Priority,
                HomeAll = d.Seat.Home, HomeYawAll = d.Seat.HomeYaw, HomeSingle = d.Seat.Single, HomeYawSingle = d.Seat.SingleYaw,
                Handles = _handles,
            };
            Rig.Instruments.Add(this);
            app.Register<Rig>(Rig);
            app.Register<IInstrument>(this);
            app.Register<IStowable>(this);
            app.Register<IInstrumentPlayback>(this);
            app.Register<IGuideAnchors>(this);
            app.Register<ILessonTargets>(this);
            app.Register<IContactSurface>(this);
            app.Register<IInstrumentSurface>(this);
            app.Register<IHandToolProvider>(Tools);
            app.Input.RegisterExternal(Stick.Suede);
            app.Input.RegisterExternal(Stick.Shaft);
            _onArrange = e => { _arranging = e.Active; if (e.Active) Quiet(); };
            app.Events.Subscribe(_onArrange);
            _onPlacing = e => { if (e.Active) Quiet(); };   // MR placing ticks no instrument either
            app.Events.Subscribe(_onPlacing);
        }

        Action<PlacingEvent> _onPlacing;

        void BuildClickTargets()
        {
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Click " + BowlsLayout.Key[i]);
                go.layer = Layers.PlaySurface;
                go.transform.SetParent(View.Seat.transform, false);
                var c = go.AddComponent<BoxCollider>();
                float R = BowlsLayout.R(i), H = BowlsLayout.Height[i];
                c.center = new Vector3(BowlsLayout.BowlXZ[i].x, BowlsLayout.BaseY + H * 0.5f, BowlsLayout.BowlXZ[i].y);
                c.size = new Vector3(2f * R, H, 2f * R);
                c.isTrigger = true;
                _click[i] = c;
            }
        }

        public void Shutdown()
        {
            if (_app == null) return;
            if (_onArrange != null) _app.Events.Unsubscribe(_onArrange);
            if (_onPlacing != null) _app.Events.Unsubscribe(_onPlacing);
            if (_onStick != null) Stick.Changed -= _onStick;
            _app.Input.UnregisterExternal(Stick.Suede);
            _app.Input.UnregisterExternal(Stick.Shaft);
        }

        void OnDestroy() => View?.Destroy();

        IBowlsSound Sound => _audio != null && _audio.IsReady ? _audio.ActiveUnit(UnitId.Bowls) as IBowlsSound : null;
        HandFrame IBowlsHandFrames.Frame(HandState hand) => _app.Input.Frame(hand);
        Matrix4x4 RigToWorld => transform.localToWorldMatrix;

        // ---------------------------------------------------------------- frame phases (BowlsModule)

        /// <summary>FramePhase.StickPoses: the held stick follows its hand before PointerInput reads its two pointers.</summary>
        public void UpdateStickPoses(float dt)
        {
            bool present = IsPresent && !_arranging && !Rig.Held;
            Stick.Puja.AtSeat = present;
            if (Stick.Held && !present) Stick.Release();
            Stick.UpdatePoses(dt, RigToWorld);
        }

        /// <summary>FramePhase.EndFrame: the drawn stick; contacts let go when the bowls were not ticked this frame (a glide,
        /// arrange mode, a held rig, the menu's page turn).</summary>
        public void EndFrame(float dt)
        {
            if (!_ticked && (Hands.RubbingHand(0) >= 0 || Hands.RubbingHand(1) >= 0 || Hands.RubbingHand(2) >= 0 || AnyLiveLane()))
                ReleaseLive(_app.Clock.Now);
            if (!_ticked) Stick.Puja.SetRim(false, Vector3.zero, dt);
            _ticked = false;
            if (_stowed) return;
            View.SetPujaPose(Stick.Puja.PoseRigLocal);
        }

        /// <summary>The drawn stick rides the rim while its shaft rubs (BowlsPuja.SetRim; the hand logic's frame is the rig's).</summary>
        void StickOnRim(Matrix4x4 w2r, float dt)
        {
            if (!Stick.Held) return;
            int h = ReferenceEquals(Stick.Hand, _app.Input.Hands[0]) ? 0 : ReferenceEquals(Stick.Hand, _app.Input.Hands[1]) ? 1 : -1;
            bool on = Hands.ShaftOnRim(h, out Unity.Mathematics.double3 shaft, out Unity.Mathematics.double3 surface);
            Vector3 off = Vector3.zero;
            if (on) off = RigToWorld.MultiplyVector(new Vector3((float)(surface.x - shaft.x), (float)(surface.y - shaft.y), (float)(surface.z - shaft.z)));
            Stick.Puja.SetRim(on, off, dt);
        }

        bool AnyLiveLane() { for (int i = 0; i < 3; i++) if (_arb[i].Owner == PlaybackLanes.Live) return true; return false; }

        // ---------------------------------------------------------------- IInstrument

        public InstrumentKind Kind => InstrumentKind.Bowls;
        public int TickOrder => _d.TickOrder;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => isActiveAndEnabled && Rig != null && Rig.InScene && !_stowed;
        public Transform Root => transform;
        public IReadOnlyList<Renderer> PlayedParts => _played;
        public bool Stowed => _stowed;
        public bool OnSurface => View.OnSurface;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            _ticked = true;
            double t = _app.Clock.Now;
            _frameDt = dt; _t = t;
            IBowlsSound s = Sound;
            Matrix4x4 w2r = transform.worldToLocalMatrix;
            // the stick's ends: its two tool pointers in this frame's list (Core passes them while it is held)
            bool haveStick = false; Vector3 suede = default, plain = default;
            for (int k = 0; k < pointers.Count; k++)
            {
                Pointer p = pointers[k];
                if (!p.Active || p.Owner != Stick) continue;
                if (p.Tool == ToolKind.Striker) { suede = p.Tip; haveStick = true; }
                else if (p.Tool == ToolKind.RubStick) plain = p.Tip;
            }
            PointerInput input = _app.Input;
            for (int h = 0; h < 2; h++)
            {
                HandState hs = input.Hands[h];
                bool holds = haveStick && Stick.Hand == hs;
                BowlsHandAdapter.Fill(hs.Connected ? input.Frame(hs) : null, hs.IsMouse, _in[h], w2r, holds, suede, plain);
            }
            _desktopFrame = false;
            Hands.Frame(t, _in);
            StickOnRim(w2r, dt);
            DesktopPointer(t, dt);
            FlushRecorder();
            if (s != null)
            {
                Vector3 head = w2r.MultiplyPoint3x4(_app.Head != null ? _app.Head.position : Vector3.zero);
                for (int i = 0; i < 3; i++) { _facing[i] = Mathf.Atan2(head.z - BowlsLayout.BowlXZ[i].y, head.x - BowlsLayout.BowlXZ[i].x); s.SetListener(i, _facing[i]); }
            }
            View.Tick(dt);
            _rubbingNow = -1;
            for (int i = 0; i < 3; i++) if (Hands.RubbingHand(i) >= 0) { _rubbingNow = i; _lastBowlRubbed = i; }
            if (BowlsRubLog.Enabled) RubLogFrame(dt);
            LessonClock(dt);
            Coach.SetPresence(true, GuideDone(), t);
            if (s != null) Coach.Tick(t, dt, this);
            PublishCoach();
        }

        /// <summary>Development players: the once-a-second rub line (BowlsRubLog) while a bowl is rubbed.</summary>
        void RubLogFrame(float dt)
        {
            BowlsSoundCore core = _audio != null && _audio.IsReady ? (_audio.ActiveUnit(UnitId.Bowls) as BowlsUnit)?.Core : null;
            if (core == null) return;
            int b = _rubbingNow;
            string line = _rubLog.Frame(dt, b, b >= 0 ? Hands.RubSpeed(b) : 0, b >= 0 ? Hands.RubForce(b) : 0, b >= 0 ? core.RubStats(b) : default, b >= 0 ? Hands.DampLevel(b) : 0);
            if (line != null) Debug.Log(line);
        }

        bool GuideDone()
        {
            if (_guide == null) _guide = _app.Get<IGuide>();
            return _guide == null || _guide.Done;
        }

        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow) { }   // no glows (platform.md 3.3)

        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (!IsPresent) return PlaySurface.None;
            Vector3 l = transform.InverseTransformPoint(world);
            for (int i = 0; i < 3; i++)
            {
                float dx = l.x - BowlsLayout.BowlXZ[i].x, dz = l.z - BowlsLayout.BowlXZ[i].y, R = BowlsLayout.R(i) + 0.02f;
                if (dx * dx + dz * dz <= R * R && l.y >= BowlsLayout.BaseY - 0.01f && l.y <= BowlsLayout.RimY(i) + 0.08f) return PlaySurface.Instrument;
            }
            return PlaySurface.None;
        }

        // ---------------------------------------------------------------- the live sink (BowlsHands)

        HandState HandOf(int hand) => hand >= 0 && hand < 2 ? _app.Input.Hands[hand] : null;

        void Stamp(int hand, bool tool)
        {
            double now = _app.Clock.NowMs;
            if (tool && Stick.Held) { PlayStamp.Mark(Stick, Kind, now); return; }
            HandState h = HandOf(hand);
            if (h != null && !_desktopFrame) PlayStamp.Mark(h, Kind, now);
        }


        void IBowlsHandsSink.Strike(int bowl, BowlStrikeKind kind, double V, double angle, double hs)
        {
            LiveStrike(bowl, kind, (float)V, (float)angle, (float)hs);
        }

        void LiveStrike(int bowl, BowlStrikeKind kind, float V, float angle, float hs)
        {
            Sound?.Strike(bowl, kind, V, angle, hs);
            View.Strike(bowl, angle, V, kind == BowlStrikeKind.Palm);
            LiveStrikes++;
            Record(BowlsEvents.StrikeEvent(bowl, kind, V, angle, hs));
            if (V >= BowlsLessonRules.MinStrikeSpeed && PostHitMarker(bowl)) Record(BowlsEvents.HitEvent(bowl, V, kind));
        }

        void IBowlsHandsSink.Contact(int bowl, BowlSurface kind, double N, double vf, double angle, double omega, double hs)
        {
            LiveContact(bowl, kind, N, vf, angle, omega, hs);
        }

        void LiveContact(int bowl, BowlSurface kind, double N, double vf, double angle, double omega, double hs)
        {
            LaneClaim c = _arb[bowl].Claim(PlaybackLanes.Live);
            if (c == LaneClaim.Drop) return;
            Sound?.Contact(bowl, kind, (float)N, (float)vf, (float)angle, (float)omega, (float)hs, UnitLiveLane);
            _enc[bowl].Frame(new BowlsRubFrame { t = _t, kind = (int)kind, N = N, vf = vf, angle = angle, hs = hs }, Math.Max(_frameDt, 1e-3), _out);
        }

        void IBowlsHandsSink.Release(int bowl) => LiveRelease(bowl);

        void LiveRelease(int bowl)
        {
            if (!_arb[bowl].Release(PlaybackLanes.Live)) return;
            Sound?.Release(bowl, UnitLiveLane);
            _enc[bowl].Release(_out);
        }

        void IBowlsHandsSink.Damp(int bowl, int level)
        {
            Sound?.Damp(bowl, Math.Max(level, _pbDamp[bowl]));
            if (level >= 2) View.StopRocking(bowl);
            Record(BowlsEvents.DampEvent(bowl, level));
        }

        void IBowlsHandsSink.Event(in BowlsHandEvent e)
        {
            Coach.OnHandEvent(e);
            if (e.Type == BowlsHandEventType.Tap || e.Type == BowlsHandEventType.PalmStrike || e.Type == BowlsHandEventType.RubStart) Stamp(e.Hand, e.Tool);
            if (e.Type == BowlsHandEventType.RubStart) _lastBowlRubbed = e.Bowl;
            // a stalled lesson event (review item 11): after 15 s any touch on the target bowl rings it audibly and counts
            if ((e.Type == BowlsHandEventType.Touch || e.Type == BowlsHandEventType.RubContact) && StalledTarget(out int bowl) && bowl == e.Bowl)
            {
                _waitT = 0;
                LiveStrike(bowl, BowlStrikeKind.Tap, AssistSpeed, _facing[bowl], 0.9f);
            }
        }

        /// <summary>Lets every live contact go (the bowls left the seat, arrange mode, a held rig): the lanes close.</summary>
        void ReleaseLive(double t)
        {
            Hands.ReleaseAll(t);
            for (int i = 0; i < 3; i++)
            {
                if (_arb[i].Owner == PlaybackLanes.Live) { _arb[i].Release(PlaybackLanes.Live); Sound?.Release(i, UnitLiveLane); }
                _enc[i].Close(t, _out);
            }
            FlushRecorder();
        }

        void Record(in TakeEvent e) => _out.Add(e);

        void FlushRecorder()
        {
            if (_out.Count == 0) return;
            if (_recorder == null) _recorder = _app.Get<IRecorder>();   // Lessons (Order 40) registers it after us
            for (int k = 0; k < _out.Count; k++) _recorder?.Add(_out[k]);
            _out.Clear();
        }

        // ---------------------------------------------------------------- Sunny's coach

        float IBowlsStats.SingVelocity(int b) => Sound?.SingVelocity(b) ?? 0f;
        float IBowlsStats.ContactLost(int b) => Sound?.ContactLost(b) ?? 0f;
        float IBowlsStats.Level(int b) => Sound?.Level(b) ?? 0f;

        void PublishCoach()
        {
            List<BowlsSignal> sig = Coach.Signals;
            for (int k = 0; k < sig.Count; k++)
            {
                _signalScratch = sig[k].Name;
                _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Bowls, Signal = _signalScratch });
            }
            sig.Clear();
            List<BowlsSingMarker> mk = Coach.Markers;
            for (int k = 0; k < mk.Count; k++)
                if (PostSingMarker(mk[k].Bowl, mk[k].Seconds)) Record(BowlsEvents.SingEvent(mk[k].Bowl, mk[k].Seconds));
            mk.Clear();
            FlushRecorder();
        }

        // ---------------------------------------------------------------- lessons

        bool InBowlsLesson(out Lesson lesson, out int ev)
        {
            lesson = null; ev = -1;
            if (_tutorial == null) _tutorial = _app.Get<ITutorial>();
            if (_tutorial == null) return false;
            TutorialState st = _tutorial.State;
            if ((st != TutorialState.Playing && st != TutorialState.Waiting) || _tutorial.Instrument != InstrumentKind.Bowls) return false;
            ev = _tutorial.CurrentEvent;
            if (ev < 0) return false;
            if (_library == null) _library = _app.Get<ILessonLibrary>();
            lesson = _library?.Get(_tutorial.LessonId);
            return lesson?.events != null && ev < lesson.events.Length;
        }

        void LessonClock(float dt)
        {
            if (!InBowlsLesson(out _, out int ev)) { _lessonEvent = -1; _waitT = 0; return; }
            if (ev != _lessonEvent) { _lessonEvent = ev; _waitT = 0; }
            if (_tutorial.State == TutorialState.Waiting) _waitT += dt;
        }

        /// <summary>15 s waiting on an event: its first unhit target's bowl.</summary>
        bool StalledTarget(out int bowl)
        {
            bowl = -1;
            if (_waitT < BowlsLessonRules.AnyTouchAfter || !InBowlsLesson(out Lesson l, out int ev)) return false;
            LessonTarget[] ts = l.events[ev].targets;
            if (ts == null || ts.Length == 0) return false;
            bowl = ts[0].i;
            return bowl >= 0 && bowl < 3;
        }

        static bool IsRub(LessonTarget t) => t != null && t.mode == "rub";

        /// <summary>A strike in a lesson is a hit marker unless the event waits for a rub on that bowl (tap, then circle is
        /// no mistake). Outside a lesson: always (nothing reads it).</summary>
        bool PostHitMarker(int bowl)
        {
            if (!InBowlsLesson(out Lesson l, out int ev)) return true;
            LessonTarget[] ts = l.events[ev].targets;
            if (ts != null) for (int k = 0; k < ts.Length; k++) if (IsRub(ts[k]) && ts[k].i == bowl) return false;
            return true;
        }

        /// <summary>Singing in a lesson is posted when it reaches the rub target's hold on that bowl (the seconds before it
        /// are progress, not mistakes); singing a bowl the event does not ask for is posted (a mistake) once a second.</summary>
        bool PostSingMarker(int bowl, double seconds)
        {
            if (!InBowlsLesson(out Lesson l, out int ev)) return true;
            LessonTarget[] ts = l.events[ev].targets;
            if (ts != null)
                for (int k = 0; k < ts.Length; k++)
                    if (IsRub(ts[k]) && ts[k].i == bowl) return seconds >= ts[k].hold - 1e-6;
            return seconds >= 1.0;
        }

        public bool Validate(in LessonTarget t, out string why)
        {
            why = null;
            if (t == null) why = "no target";
            else if (t.i < 0 || t.i > 2) why = $"bowl {t.i} is outside 0..2";
            else if (!string.IsNullOrEmpty(t.mode) && t.mode != "strike" && t.mode != "rub") why = $"mode '{t.mode}' is neither strike nor rub";
            else if (IsRub(t) && !(t.hold > 0f)) why = "a rub target needs hold > 0";
            return why == null;
        }

        public bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size)
        {
            int i = Mathf.Clamp(t != null ? t.i : 0, 0, 2);
            landing = new Pose(transform.TransformPoint(new Vector3(BowlsLayout.BowlXZ[i].x, BowlsLayout.RimY(i), BowlsLayout.BowlXZ[i].y)), transform.rotation);
            approach = -transform.up;
            float d = 2f * BowlsLayout.R(i);
            size = new Vector2(d, d);
            return true;
        }

        public bool Matches(in LessonTarget t, in TakeEvent e)
        {
            if (t == null || e.i != t.i) return false;
            if (IsRub(t)) return e.type == BowlsEvents.Sing && e.u >= t.hold - 1e-6f;
            return e.type == BowlsEvents.Hit && e.vel >= BowlsLessonRules.MinStrikeSpeed;
        }

        public void ShowTarget(in LessonTarget t, float k) { CueIndex = k > 0f && t != null ? t.i : -1; CueK = k; }   // the ring itself is the cue

        public string Hint(IReadOnlyList<LessonTarget> targets)
        {
            if (targets == null || targets.Count == 0 || targets[0] == null) return "Tap a bowl";
            LessonTarget t = targets[0];
            string name = t.i >= 0 && t.i < 3 ? BowlsLayout.Key[t.i] : "";
            return IsRub(t) ? "Rub the " + name + " bowl" : "Tap the " + name + " bowl";
        }

        // ---------------------------------------------------------------- IStowable / IInstrumentSurface

        public void SetStowed(bool stowed, StowMount mount)
        {
            _stowed = stowed;
            if (stowed) Quiet();
            View.SetStowed(stowed);
        }

        public void SetOnSurface(bool onSurface) => View.SetOnSurface(onSurface);

        /// <summary>Arrange mode, a stow: every live contact lets go, the stick goes back on the stand, the rocking stops.</summary>
        void Quiet()
        {
            ReleaseLive(_app.Clock.Now);
            Stick.Release();
            for (int i = 0; i < 3; i++) View.StopRocking(i);
            _dragBowl = -1;
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            IBowlsSound s = Sound;
            string type = e.type;
            if (type == BowlsEvents.Strike)
            {
                int i = Mathf.Clamp(e.i, 0, 2);
                float angle = e.midi / 1000f, hs = Mathf.Clamp01(1f - e.pos * 0.5f);
                if (e.pos <= 0f && e.midi == 0 && string.IsNullOrEmpty(e.kind)) hs = 0.9f;
                BowlStrikeKind kind = BowlsEvents.KindOf(e.kind);
                s?.Strike(i, kind, Mathf.Max(0.05f, e.vel), angle, hs);
                if (!_stowed) View.Strike(i, angle, e.vel, kind == BowlStrikeKind.Palm);
                PlaybackCalls++;
                return true;
            }
            if (type == BowlsEvents.Damp)
            {
                int i = Mathf.Clamp(e.i, 0, 2), level = BowlsEvents.DampLevel(e);
                _pbDamp[i] = level;
                s?.Damp(i, Math.Max(level, Hands.DampLevel(i)));
                if (level >= 2 && !_stowed) View.StopRocking(i);
                PlaybackCalls++;
                return true;
            }
            if (!BowlsRubEncoder.Decode(e, BowlsLayout.RimDiamD[Mathf.Clamp(e.i, 0, 2)] / 2, out BowlsPlaybackCmd c)) return false;
            int lane = e.lane == PlaybackLanes.Live ? PlaybackLanes.Take : e.lane;   // a playback call is never the live hand
            if (c.close)
            {
                // the looper's own close (a window end, a mute): every bowl this lane holds
                for (int i = 0; i < 3; i++)
                {
                    if (_arb[i].Owner != lane) continue;
                    if (c.release) PlaybackRelease(s, i, lane);
                    else { BowlsPlaybackCmd last = _pb[i]; last.N = 0; last.ramp = c.ramp > 0 ? c.ramp : (int)(0.05 * BowlsRubEncoder.Rate); PlaybackContact(s, i, last); }
                }
                PlaybackCalls++;
                return true;
            }
            int b = Mathf.Clamp(c.bowl, 0, 2);
            if (c.release) { PlaybackRelease(s, b, lane); PlaybackCalls++; return true; }
            if (c.N <= 0 && _arb[b].Owner != lane) return true;                          // a level-0 control never wakes a free bowl
            LaneClaim claim = _arb[b].Claim(lane);
            if (claim == LaneClaim.Drop) return true;                                   // the live hand (or an earlier lane) has it
            PlaybackContact(s, b, c);
            PlaybackCalls++;
            return true;
        }

        void PlaybackContact(IBowlsSound s, int b, in BowlsPlaybackCmd c)
        {
            _pb[b] = c;
            s?.Contact(b, (BowlSurface)c.kind, (float)c.N, (float)c.vf, (float)c.angle, (float)c.omega, (float)c.heightS, UnitPlaybackLane, c.ramp);
            // the replayed rub shows on the bowl (no hand, no stick): it sways with the level, round with the rub point
            if (!_stowed) View.Hum(b, (float)(c.N / BowlsRubEncoder.NFull), (float)c.angle, (float)c.omega);
        }

        void PlaybackRelease(IBowlsSound s, int b, int lane)
        {
            if (!_arb[b].Release(lane)) return;                                          // a lane that lost the bowl: ignored
            s?.Release(b, UnitPlaybackLane);
            View.Hum(b, 0f, 0f, 0f);
        }

        public void ReleaseAll()
        {
            IBowlsSound s = Sound;
            for (int i = 0; i < 3; i++)
            {
                int owner = _arb[i].Owner;
                if (owner > PlaybackLanes.Live) PlaybackRelease(s, i, owner);
                if (_pbDamp[i] != 0) { _pbDamp[i] = 0; s?.Damp(i, Hands.DampLevel(i)); }
            }
            PlaybackCalls++;
        }

        public void CloseLane(int lane)
        {
            if (lane == PlaybackLanes.Live) return;
            IBowlsSound s = Sound;
            for (int i = 0; i < 3; i++) if (_arb[i].Owner == lane) PlaybackRelease(s, i, lane);
            PlaybackCalls++;
        }

        // ---------------------------------------------------------------- IGuideAnchors

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up) =>
            BowlsAnchors.TryGet(anchor, transform, _app.Head != null ? _app.Head.position : transform.position + Vector3.up, _resolve, Stick.Puja, out world, out up);

        /// <summary>The bowl an "@" anchor names: @rubbed the one rubbed now or last, @ringing the loudest, @target the
        /// current lesson target's (the big bowl when there is none).</summary>
        int ResolveBowl(string which)
        {
            if (which == "@rubbed") return _rubbingNow >= 0 ? _rubbingNow : _lastBowlRubbed;
            if (which == "@ringing")
            {
                IBowlsSound s = Sound; int best = 0; float lv = -1f;
                if (s != null) for (int i = 0; i < 3; i++) { float l = s.Level(i); if (l > lv) { lv = l; best = i; } }
                return best;
            }
            if (which == "@target" && InBowlsLesson(out Lesson l2, out int ev))
            {
                LessonTarget[] ts = l2.events[ev].targets;
                if (ts != null && ts.Length > 0 && ts[0] != null && ts[0].i >= 0 && ts[0].i < 3) return ts[0].i;
            }
            return 0;
        }

        // ---------------------------------------------------------------- IContactSurface

        /// <summary>The stand top around the bowls (shadows only: the bowls' round walls take no clamp).</summary>
        public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
        {
            hit = default;
            if (!IsPresent || Rig.Held || View.OnSurface) return false;
            Vector3 l = transform.InverseTransformPoint(world);
            if (Mathf.Abs(l.x) > BowlsLayout.StandHx || Mathf.Abs(l.z) > BowlsLayout.StandHz) return false;
            for (int i = 0; i < 3; i++)
            {
                float dx = l.x - BowlsLayout.BowlXZ[i].x, dz = l.z - BowlsLayout.BowlXZ[i].y, R = BowlsLayout.R(i);
                if (dx * dx + dz * dz < R * R) return false;   // over a bowl: its wall, not the table
            }
            float height = l.y - BowlsLayout.StandTop;
            if (height > above || height < -below) return false;
            hit = new ContactHit
            {
                Point = transform.TransformPoint(new Vector3(l.x, BowlsLayout.StandTop, l.z)), Normal = transform.up, Tangent = Vector3.zero,
                Height = height, Use = ContactUse.Shadow,
            };
            return true;
        }

        // ---------------------------------------------------------------- desktop

        void DesktopPointer(double t, float dt)
        {
            if (_app.IsPresenting) { _scriptedWasDown = ScriptedMouse && ScriptedButton; EndDrag(); return; }
            bool down, pressed;
            Ray ray;
            if (ScriptedMouse)
            {
                down = ScriptedButton;
                pressed = down && !_scriptedWasDown;
                _scriptedWasDown = down;
                ray = ScriptedRay;
            }
            else
            {
                down = DesktopInput.Mouse(0) && !DesktopInput.Alt;
                pressed = DesktopInput.MouseDown(0) && !DesktopInput.Shift && !DesktopInput.Alt;
                if (!down) { EndDrag(); return; }
                Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
                if (cam == null) return;
                ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            }
            if (!down) { EndDrag(); return; }
            int bowl = -1; Vector3 point = default;
            if (Physics.Raycast(ray, out RaycastHit hit, 20f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide))
                for (int i = 0; i < 3; i++) if (hit.collider == _click[i]) { bowl = i; point = hit.point; break; }
            if (bowl < 0) { if (pressed) return; EndDrag(); return; }
            Vector3 l = transform.InverseTransformPoint(point);
            float angle = Mathf.Atan2(l.z - BowlsLayout.BowlXZ[bowl].y, l.x - BowlsLayout.BowlXZ[bowl].x);
            _desktopFrame = true;
            if (pressed)
            {
                EndDrag();
                float hs = Mathf.Clamp(1f - 0.5f * (BowlsLayout.RimY(bowl) - l.y) / BowlsLayout.Height[bowl], 0.5f, 1f);
                LiveStrike(bowl, BowlStrikeKind.Tap, DesktopTapSpeed, angle, hs);
                Coach.OnHandEvent(new BowlsHandEvent { T = t, Type = BowlsHandEventType.Tap, Bowl = bowl, Hand = -1, V = DesktopTapSpeed, StrikeKind = BowlStrikeKind.Tap });
                _dragBowl = bowl; _dragAngle = angle; _dragT = 0f; _dragRub = false;
                return;
            }
            if (_dragBowl != bowl) { EndDrag(); _dragBowl = bowl; _dragAngle = angle; return; }
            // a circle-drag: the angle's rate times the radius is the rub speed
            float da = Mathf.DeltaAngle(_dragAngle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _dragAngle = angle; _dragT += dt;
            float R = BowlsLayout.R(bowl), omega = dt > 0f ? da / dt : 0f, vf = omega * R;
            if (!_dragRub && Mathf.Abs(vf) < DesktopRubMin) return;
            _dragRub = true;
            LiveContact(bowl, BowlSurface.Outer, DesktopRubN, vf, angle, omega, DesktopHeightS);
        }

        void EndDrag()
        {
            if (_dragBowl >= 0 && _dragRub) { LiveRelease(_dragBowl); FlushRecorder(); }
            _dragBowl = -1; _dragRub = false;
        }

        /// <summary>The rig zone: the stand and the bowls grown by GrabConstants.ZonePad.</summary>
        sealed class Zone : IPlayZone
        {
            static readonly Bounds Local = new Bounds(new Vector3(0f, 0.37f, 0f), new Vector3(0.42f, 0.74f, 0.30f));
            readonly BowlsInstrument _b;
            public Zone(BowlsInstrument b) { _b = b; }
            public bool Contains(Vector3 world)
            {
                if (_b == null || !_b.isActiveAndEnabled) return false;
                Bounds b = Local;
                b.Expand(GrabConstants.ZonePad * 2f);
                return b.Contains(_b.transform.InverseTransformPoint(world));
            }
        }
    }
}
