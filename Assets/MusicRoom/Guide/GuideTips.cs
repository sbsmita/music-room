// Sunny's tips from data (12-new-instruments/platform.md 8; Docs/11-first-five/sunny-tour.md 3.1, the phase-0 form):
// every registry descriptor's TipSpecs (App.Registry.All[*].Tips, owned by that instrument) and every IGuideTipSource's
// (parameterless classes found by reflection, like the instrument definitions; the platform's own are PlatformTips).
//   Triggers: FirstArrival (the instrument's InstrumentMoveEvent Arrived), FirstDisplayedGaze (a displayed instrument's
//   pick point, ILayoutService.TryGetDisplayPoint, within GazeDegrees of the head's forward for GazeSeconds; TriggerArg an
//   instrument id, or empty: the tip's own instrument, or any for a source's tip), IdleNear (at the seat, a fingertip in
//   its zone, not played for TriggerArg seconds, default 8), Signal (InstrumentSignalEvent.Signal == TriggerArg), Manual
//   (IGuide.Tip(tipId)), LessonStart (a lesson of the instrument starts; TriggerArg a lesson id or empty).
//   Successes (a tip is learned, persisted, and never offered again, even if it was never shown: doing it yourself
//   counts): EventType (a live RecorderEvent of type SuccessArg; empty: any live Onset of the instrument), Signal, Facing
//   (FocusEvent on the instrument), LessonHit (a target of the instrument's lesson hit), None (the line is enough).
//   Showing: a triggered tip waits up to its class's wait (P1 30 s, P2 6 s, P3 20 s) for Sunny to be free (never over the
//   tour, the deck walkthrough, another tip or "Move instruments"), then Sunny comes by and says its lines (TextOnly: its
//   Title as the caption, no voice) with its demo at its anchor (IGuideAnchors: the tip's instrument first, then every
//   other provider; "display": the gazed instrument's pick point), then ThenDemo at ThenAnchor. Once heard to the end it is
//   seen (musicroom.tipsSeen). Spacing: 10 s after the previous tip for P1 / P2; P3 not in the first 90 s after the tour,
//   at most 3 per 10 minutes, 60 s apart, and not within 2.5 s of the player's last live play. A dropped tip stays unseen.
// Allocation-free per frame (Tick); the event hooks allocate nothing either.
// First five (Guide/FirstRun, FIRSTFIVE-NOTES.md): this is the ONE scheduler of every tip Sunny says. The tour's 64 tips
// are TipSpecs of TourTipSource (an IGuideTipSource) like the platform's and the instruments'; TourTipRunner is their
// presenter (ITipPresenter): it fires their triggers and successes (Trigger, Learn), shows them when this runner picks them
// (its lines, the success wait, the praise, the world-anchored demos) and adds its quiet-moment rules and the "Tips while I
// play" / Hide Sunny switches to every tip. So: one tip at a time, one spacing, one P3 window for all of them.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed class TipEntry
    {
        public TipSpec Spec;
        /// <summary>The instrument whose descriptor carries it (null: an IGuideTipSource's).</summary>
        public InstrumentDescriptor Owner;
        public bool Seen;
        /// <summary>The runner's clock until which a triggered tip waits for Sunny (-1: not pending).</summary>
        public float PendingUntil = -1f;
        /// <summary>A gaze tip: the displayed instrument looked at (its pick point is the "display" anchor).</summary>
        public bool HasDisplay;
        public InstrumentKind DisplayKind;
        public float NearT;
        public bool Pending => PendingUntil >= 0f;
        public string Id => Spec.Id;
        public override string ToString() => Spec.Id;
    }

    /// <summary>First five: shows the tour's tips (TourTipRunner) and adds its rules to every tip (TipRunner.Presenter).</summary>
    public interface ITipPresenter
    {
        /// <summary>One of the presenter's own tips (it shows them; the runner only schedules them).</summary>
        bool Owns(TipEntry e);
        /// <summary>A trigger may make the tip pending ("Tips while I play" off keeps P2 / P3 quiet; Hide Sunny for the launch).</summary>
        bool Allowed(TipEntry e);
        /// <summary>A quiet moment for this tip (no live play, no grab, no lesson, no count-in or recording pass, ...).</summary>
        bool Quiet(TipEntry e);
        /// <summary>Sunny is free for this owned tip (an arrange tip may come during "Move instruments").</summary>
        bool FreeFor(TipEntry e);
        /// <summary>Show an owned tip now: the seconds it takes, 0 when it could not.</summary>
        float Show(TipEntry e);
        /// <summary>The owned tip is still being shown.</summary>
        bool IsShowing(TipEntry e);
    }

    public sealed class TipRunner
    {
        public const float WaitP1 = 30f, WaitP2 = 6f, WaitP3 = 20f;
        public const float SpacingP12 = 10f, SpacingP3 = 60f, P3AfterTour = 90f, P3Window = 600f, QuietPlay = 2.5f;
        public const int P3PerWindow = 3;
        public const float GazeDegrees = 10f, GazeSeconds = 3f, IdleNearSeconds = 8f;
        /// <summary>Sunny holds a tip with a demo at least this long (one demo loop; two with a ThenDemo).</summary>
        public const float DemoHold = 3f;

        readonly App _app;
        readonly GuideModule _guide;
        readonly List<TipEntry> _entries = new List<TipEntry>();
        readonly List<FeatureSpec> _features = new List<FeatureSpec>();
        readonly List<string> _errors = new List<string>();
        readonly float[] _p3Shown = new float[P3PerWindow];
        int _p3Next;
        float _lastShown = float.NegativeInfinity, _lastLivePlay = float.NegativeInfinity;
        bool _anyGaze, _anyIdle;
        bool _gazing;
        InstrumentKind _gazeKind;
        float _gazeT;
        TutorialState _lastTutorialState;
        string _lastLessonId;
        int _lastHits;
        ILayoutService _layout;
        ITutorial _tutorial;
        IRigRegistry _rigs;
        int _anchorsVersion = -1;
        readonly List<IGuideAnchors> _anchors = new List<IGuideAnchors>();

        public float Time { get; private set; }
        public IReadOnlyList<TipEntry> Entries => _entries;
        /// <summary>Every feature id the registry and the sources name (descriptor Features, IGuideTipSource.Features).</summary>
        public IReadOnlyList<FeatureSpec> Features => _features;
        public IReadOnlyList<string> Errors => _errors;
        /// <summary>The tip Sunny is saying (null: none).</summary>
        public TipEntry Showing { get; private set; }
        public int Shown { get; private set; }
        /// <summary>When the tour ended on the runner's clock (NaN: not yet this launch; 0 when it was done before).</summary>
        public float TourEnd = float.NaN;
        /// <summary>First five: the tour's tips' presenter and rules (null: the platform's behaviour alone, ClassicTour boots).</summary>
        public ITipPresenter Presenter;

        public TipRunner(App app, GuideModule guide)
        {
            _app = app;
            _guide = guide;
            for (int i = 0; i < _p3Shown.Length; i++) _p3Shown[i] = float.NegativeInfinity;
            foreach (var (spec, owner) in Collect(app.Registry, ModuleDiscovery.CreateAll<IGuideTipSource>(), _features, _errors))
            {
                var e = new TipEntry { Spec = spec, Owner = owner, Seen = guide.TipSeen(spec.Id) };
                _entries.Add(e);
                _anyGaze |= spec.Trigger == TipTrigger.FirstDisplayedGaze;
                _anyIdle |= spec.Trigger == TipTrigger.IdleNear;
            }
        }

        /// <summary>Every tip of the registry's playable descriptors (with their owner) and of the sources (owner null); a
        /// duplicate tip id is dropped and logged. Features of both are added to <paramref name="features"/>.</summary>
        public static List<(TipSpec spec, InstrumentDescriptor owner)> Collect(IInstrumentRegistry registry, IEnumerable<IGuideTipSource> sources,
            List<FeatureSpec> features = null, List<string> errors = null)
        {
            var list = new List<(TipSpec, InstrumentDescriptor)>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Add(TipSpec t, InstrumentDescriptor owner)
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) return;
                if (!ids.Add(t.Id)) { string m = "duplicate tip id " + t.Id + " (dropped)"; errors?.Add(m); Debug.LogError("[Guide] tips: " + m); return; }
                list.Add((t, owner));
            }
            if (registry != null)
                foreach (InstrumentDescriptor d in registry.All)
                {
                    if (d.Tips != null) foreach (TipSpec t in d.Tips) Add(t, d);
                    if (d.Features != null && features != null) features.AddRange(d.Features);
                }
            if (sources != null)
                foreach (IGuideTipSource s in sources)
                {
                    TipSpec[] tips = null;
                    FeatureSpec[] fs = null;
                    try { tips = s.Tips(); fs = s.Features(); }
                    catch (Exception e) { Debug.LogError("[Guide] tips: " + s.GetType().Name + " failed: " + e.Message); }
                    if (tips != null) foreach (TipSpec t in tips) Add(t, null);
                    if (fs != null && features != null) features.AddRange(fs);
                }
            return list;
        }

        /// <summary>A tip added after boot (tests; a module that makes tips at run time): it joins the others, unless its id
        /// is taken (null then).</summary>
        public TipEntry Add(TipSpec spec, InstrumentDescriptor owner)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Id) || Find(spec.Id) != null) return null;
            var e = new TipEntry { Spec = spec, Owner = owner, Seen = _guide.TipSeen(spec.Id) };
            _entries.Add(e);
            _anyGaze |= spec.Trigger == TipTrigger.FirstDisplayedGaze;
            _anyIdle |= spec.Trigger == TipTrigger.IdleNear;
            return e;
        }

        /// <summary>Every pending tip dropped, unseen (the first run ended: the moments it raised were Sunny's, not the
        /// player's).</summary>
        public void DropPending()
        {
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].Pending && !ReferenceEquals(_entries[i], Showing)) _entries[i].PendingUntil = -1f;
        }

        public TipEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _entries.Count; i++) if (string.Equals(_entries[i].Spec.Id, id, StringComparison.Ordinal)) return _entries[i];
            return null;
        }

        /// <summary>The module marked a tip seen (heard, a success, or IGuide's own tips).</summary>
        public void NoteSeen(string id)
        {
            TipEntry e = Find(id);
            if (e == null) return;
            e.Seen = true;
            e.PendingUntil = -1f;
        }

        ILayoutService Layout => _layout ?? (_layout = _app.Get<ILayoutService>());
        ITutorial Tutorial => _tutorial ?? (_tutorial = _app.Get<ITutorial>());

        static InstrumentKind KindOf(TipEntry e) => e.Owner != null ? e.Owner.Kind : InstrumentKind.Table;

        static float WaitOf(TipClass c) => c == TipClass.P1 ? WaitP1 : c == TipClass.P2 ? WaitP2 : WaitP3;

        bool ManualP3Now(TipEntry e)
        {
            if (!P3Allowed() || Time - _lastLivePlay < QuietPlay) return false;
            if (Presenter != null && !Presenter.Quiet(e)) return false;
            for (int i = 0; i < _entries.Count; i++) if (_entries[i] != e && _entries[i].Pending && !_entries[i].Seen) return false;
            return true;
        }

        bool P3Allowed()
        {
            if (float.IsNaN(TourEnd) || Time - TourEnd < P3AfterTour) return false;
            if (Time - _lastShown < SpacingP3) return false;
            int recent = 0;
            for (int i = 0; i < _p3Shown.Length; i++) if (Time - _p3Shown[i] < P3Window) recent++;
            return recent < P3PerWindow;
        }

        // ---------------- triggers ----------------

        /// <summary>The tip's trigger fired: it waits for Sunny (false: already seen, VR only on a desktop, or not allowed now).</summary>
        public bool Trigger(TipEntry e, bool hasDisplay = false, InstrumentKind display = InstrumentKind.Table)
        {
            if (e == null || e.Seen) return false;
            if (e.Spec.VrOnly && !_guide.IsPresenting) return false;
            if (Presenter != null && !Presenter.Allowed(e)) return false;
            if (_guide.TipSeen(e.Spec.Id)) { e.Seen = true; return false; }
            if (e.Spec.Class == TipClass.P3 && !P3Allowed() && e.Spec.Trigger == TipTrigger.FirstDisplayedGaze) return false;
            e.HasDisplay = hasDisplay;
            e.DisplayKind = display;
            e.PendingUntil = Time + WaitOf(e.Spec.Class);
            return true;
        }

        /// <summary>IGuide.Tip(tipId): the tip now if Sunny is free; the seconds it takes (0: not said).</summary>
        public float Manual(string id)
        {
            TipEntry e = Find(id);
            if (e == null) return 0f;
            // Integration: a P3 tip (nice to know, rate limited) that a module asks for itself keeps the P3 rules and the quiet
            // moments of every triggered tip, and never jumps ahead of a tip already waiting (the sheet music's "your songbook"
            // came 1.5 s after the book was in view, over the platform's waiting tips): 0 now, the module asks again later.
            if (e.Spec.Class == TipClass.P3 && !ManualP3Now(e)) return 0f;
            if (!Trigger(e)) return 0f;
            float s = TryShow(e, manual: true);
            if (s <= 0f) e.PendingUntil = -1f;
            return s;
        }

        public void OnMove(in InstrumentMoveEvent m)
        {
            // An instrument a playback brought (BringReason.Take) came to be listened to, not played: no introduction over it.
            if (m.Phase != MovePhase.Arrived || m.Reason == BringReason.Take) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Spec.Trigger == TipTrigger.FirstArrival && e.Owner != null && e.Owner.Kind == m.Kind) Trigger(e);
            }
        }

        public void OnSignal(in InstrumentSignalEvent s)
        {
            if (string.IsNullOrEmpty(s.Signal)) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Spec.Success == TipSuccess.Signal && string.Equals(e.Spec.SuccessArg, s.Signal, StringComparison.Ordinal)) Succeed(e);
                else if (e.Spec.Trigger == TipTrigger.Signal && string.Equals(e.Spec.TriggerArg, s.Signal, StringComparison.Ordinal)) Trigger(e);
            }
        }

        public void OnPlay(in RecorderEvent r)
        {
            if (r.DuringPlayback || r.Event.type == null) return;
            _lastLivePlay = Time;
            IInstrumentRegistry reg = _app.Registry;
            InstrumentKind owner = reg != null ? reg.OwnerOfEvent(r.Event.type) : InstrumentKind.Table;
            bool onset = reg == null || reg.RoleOf(r.Event.type) == EventRole.Onset;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Owner != null && e.Owner.Kind == owner) e.NearT = 0f;
                if (e.Seen || e.Spec.Success != TipSuccess.EventType) continue;
                bool hit = string.IsNullOrEmpty(e.Spec.SuccessArg)
                    ? onset && e.Owner != null && e.Owner.Kind == owner
                    : string.Equals(e.Spec.SuccessArg, r.Event.type, StringComparison.Ordinal);
                if (hit) Succeed(e);
            }
        }

        public void OnFocus(in FocusEvent f)
        {
            if (!f.Has) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (!e.Seen && e.Spec.Success == TipSuccess.Facing && e.Owner != null && e.Owner.Kind == f.Kind) Succeed(e);
            }
        }

        public void OnTutorial(in TutorialEvent t)
        {
            ITutorial tut = Tutorial;
            if (tut == null) return;
            bool started = t.State == TutorialState.Countdown && (_lastTutorialState != TutorialState.Countdown || !string.Equals(_lastLessonId, t.LessonId, StringComparison.Ordinal));
            if (started) _lastHits = 0;
            int hits = tut.EventsDone;
            InstrumentKind kind = tut.Instrument;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Owner == null || e.Owner.Kind != kind) continue;
                if (started && e.Spec.Trigger == TipTrigger.LessonStart && (string.IsNullOrEmpty(e.Spec.TriggerArg) || string.Equals(e.Spec.TriggerArg, t.LessonId, StringComparison.Ordinal))) Trigger(e);
                if (!e.Seen && e.Spec.Success == TipSuccess.LessonHit && hits > _lastHits && t.State != TutorialState.Idle) Succeed(e);
            }
            _lastHits = hits;
            _lastTutorialState = t.State;
            _lastLessonId = t.LessonId;
        }

        void Succeed(TipEntry e)
        {
            if (e.Seen) return;
            _guide.MarkTipSeen(e.Spec.Id);   // NoteSeen comes back through the module
            e.Seen = true;
            e.PendingUntil = -1f;
            if (Showing == e && !Owned(e)) _guide.Sunny.EndTipDemo();   // learned: the line ends, nothing more to show
        }

        /// <summary>First five: the player did what the tip teaches (the presenter's own successes): learned.</summary>
        public void Learn(TipEntry e) => Succeed(e);

        bool Owned(TipEntry e) => Presenter != null && Presenter.Owns(e);

        bool StillShowing(TipEntry e) => Owned(e) ? Presenter.IsShowing(e)
            : _guide.Sunny.InTip && string.Equals(_guide.Sunny.TipId, e.Spec.Id, StringComparison.Ordinal);

        // ---------------- the frame ----------------

        public void Tick(float dt)
        {
            Time += dt;
            if (Showing != null && !StillShowing(Showing)) Showing = null;
            if (_anyGaze) PollGaze(dt);
            if (_anyIdle) PollIdle(dt);
            TipEntry best = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (!e.Pending) continue;
                if (Time > e.PendingUntil || e.Seen) { e.PendingUntil = -1f; continue; }   // the moment never came: dropped, unseen
                if (Presenter != null && !Presenter.Quiet(e)) continue;                  // first five: not now (still pending)
                if (best == null || e.Spec.Class < best.Spec.Class) best = e;
            }
            if (best != null) TryShow(best, manual: false);
        }

        // A displayed instrument within GazeDegrees of the head's forward, the same one for GazeSeconds.
        void PollGaze(float dt)
        {
            Transform head = _app.Head;
            ILayoutService layout = Layout;
            if (head == null || layout == null || !_guide.IsPresenting) { _gazing = false; _gazeT = 0f; return; }
            IInstrumentRegistry reg = _app.Registry;
            bool found = false;
            InstrumentKind kind = default;
            float best = GazeDegrees;
            Vector3 eye = head.position, fwd = head.forward;
            for (int i = 0; i < reg.All.Count; i++)
            {
                InstrumentKind k = reg.All[i].Kind;
                if (!layout.TryGetDisplayPoint(k, out Vector3 p)) continue;
                float a = Vector3.Angle(fwd, p - eye);
                if (a <= best) { best = a; kind = k; found = true; }
            }
            if (!found) { _gazing = false; _gazeT = 0f; return; }
            if (!_gazing || kind != _gazeKind) { _gazing = true; _gazeKind = kind; _gazeT = 0f; }
            _gazeT += dt;
            if (_gazeT < GazeSeconds) return;
            _gazeT = 0f;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Seen || e.Pending || e.Spec.Trigger != TipTrigger.FirstDisplayedGaze) continue;
                if (!GazeMatches(e, kind)) continue;
                Trigger(e, true, kind);
            }
        }

        bool GazeMatches(TipEntry e, InstrumentKind kind)
        {
            string arg = e.Spec.TriggerArg;
            if (!string.IsNullOrEmpty(arg)) return _app.Registry.TryParse(arg, out InstrumentKind k) && k == kind;
            return e.Owner == null || e.Owner.Kind == kind;
        }

        // At the seat, a fingertip inside the instrument's zone, not played for the tip's seconds.
        void PollIdle(float dt)
        {
            ILayoutService layout = Layout;
            if (_rigs == null) _rigs = _app.Get<IRigRegistry>();
            IReadOnlyList<Pointer> pts = _app.Input?.HandPointers;
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Seen || e.Pending || e.Owner == null || e.Spec.Trigger != TipTrigger.IdleNear) continue;
                InstrumentKind k = e.Owner.Kind;
                bool atSeat = layout != null && layout.PlaceOf(k) == InstrumentPlace.Seat;
                Rig rig = _rigs?.Get(InstrumentIds.RigOf(k));
                bool near = false;
                if (atSeat && rig?.Zone != null && pts != null)
                    for (int p = 0; p < pts.Count && !near; p++) near = pts[p].Active && !pts[p].FromMouse && rig.Zone.Contains(pts[p].Tip);
                if (!near) { e.NearT = 0f; continue; }
                e.NearT += dt;
                float wait = float.TryParse(e.Spec.TriggerArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f ? s : IdleNearSeconds;
                if (e.NearT >= wait) { e.NearT = 0f; Trigger(e); }
            }
        }

        float TryShow(TipEntry e, bool manual)
        {
            bool owned = Owned(e);
            if (owned ? !Presenter.FreeFor(e) : !_guide.SunnyFree) return 0f;
            if (!manual)
            {
                if (e.Spec.Class == TipClass.P3) { if (!P3Allowed() || Time - _lastLivePlay < QuietPlay) return 0f; }
                else if (Time - _lastShown < SpacingP12) return 0f;
            }
            float seconds = owned ? Presenter.Show(e) : Say(e, force: false);
            if (seconds <= 0f) return 0f;
            e.PendingUntil = -1f;
            Showing = e;
            Shown++;
            _lastShown = Time;
            if (e.Spec.Class == TipClass.P3) { _p3Shown[_p3Next] = Time; _p3Next = (_p3Next + 1) % _p3Shown.Length; }
            return seconds;
        }

        /// <summary>First five: the menu's Tips list plays this tip now, heard or not (no spacing, no quiet moment; a tip the
        /// presenter owns is the presenter's to play). Seconds, 0 when it could not.</summary>
        public float PlayNow(TipEntry e)
        {
            if (e == null || Owned(e)) return 0f;
            float seconds = Say(e, force: true);
            if (seconds > 0f) Showing = e;
            return seconds;
        }

        // The platform's own way: its lines through GuideModule.SayTip and its demo at its anchor.
        float Say(TipEntry e, bool force)
        {
            bool anchored = ResolveAnchor(e, e.Spec.Anchor, out Vector3 at, out Vector3 up);
            DemoKind then = e.Spec.ThenDemo;
            bool thenAnchored = false;
            Vector3 thenAt = default, thenUp = Vector3.up;
            if (then != DemoKind.None) thenAnchored = ResolveAnchor(e, e.Spec.ThenAnchor ?? e.Spec.Anchor, out thenAt, out thenUp);
            bool demo = e.Spec.Demo != DemoKind.None && (anchored || SunnyDemo.IsTourKind(e.Spec.Demo));
            float hold = demo ? (then != DemoKind.None ? 2f * DemoHold : DemoHold) : GuideTiming.TipT;
            float seconds = _guide.SayTip(e.Spec, hold, force);
            if (seconds <= 0f) return 0f;
            if (demo) _guide.Sunny.ShowTipDemo(e.Spec.Demo, anchored, at, up, e.Spec.Motion, then, thenAnchored, thenAt, thenUp);
            return seconds;
        }

        /// <summary>A demo anchor: "display" is the gazed instrument's pick point; any other id is asked of the tip's own
        /// instrument's IGuideAnchors first, then of every other provider.</summary>
        public bool ResolveAnchor(TipEntry e, string anchor, out Vector3 world, out Vector3 up)
        {
            world = default;
            up = Vector3.up;
            if (string.IsNullOrEmpty(anchor)) return false;
            if (string.Equals(anchor, PlatformTips.DisplayAnchor, StringComparison.Ordinal))
                return e.HasDisplay && Layout != null && Layout.TryGetDisplayPoint(e.DisplayKind, out world);
            if (_anchorsVersion != _app.ServicesVersion)
            {
                _anchorsVersion = _app.ServicesVersion;
                _anchors.Clear();
                _anchors.AddRange(_app.GetAll<IGuideAnchors>());
            }
            InstrumentKind owner = KindOf(e);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _anchors.Count; i++)
                {
                    IGuideAnchors a = _anchors[i];
                    if ((a.Kind == owner) != (pass == 0)) continue;
                    if (a.TryGetAnchor(anchor, out world, out up)) return true;
                }
            return false;
        }

        /// <summary>A lesson target's anchor for the B6 re-demo: the waited event's first target's landing (its up along the
        /// surface), through the instrument's ILessonTargets.</summary>
        public bool LessonTargetAnchor(InstrumentKind kind, out Vector3 world, out Vector3 up)
        {
            world = default;
            up = Vector3.up;
            ITutorial tut = Tutorial;
            ILessonLibrary lib = _app.Get<ILessonLibrary>();
            if (tut == null || lib == null || tut.Instrument != kind || tut.CurrentEvent < 0) return false;
            Lesson l = lib.Get(tut.LessonId);
            if (l?.events == null || tut.CurrentEvent >= l.events.Length) return false;
            LessonTarget[] targets = l.events[tut.CurrentEvent].targets;
            if (targets == null || targets.Length == 0) return false;
            foreach (ILessonTargets t in _app.GetAll<ILessonTargets>())
            {
                if (t.Kind != kind || !t.TryGetLanding(targets[0], out Pose landing, out _, out _)) continue;
                world = landing.position;
                up = landing.rotation * Vector3.up;
                return true;
            }
            return false;
        }

        /// <summary>The re-demo's kind: the instrument's LessonStart tip's demo for the running lesson when it has one (bowls
        /// R5: a circling finger for the rub lesson), else its arrival tip's demo when it is anchored, else a tap.</summary>
        public DemoKind RedemoKind(InstrumentKind kind, string lessonId = null)
        {
            if (lessonId != null)
                for (int i = 0; i < _entries.Count; i++)
                {
                    TipEntry e = _entries[i];
                    if (e.Owner == null || e.Owner.Kind != kind || e.Spec.Trigger != TipTrigger.LessonStart) continue;
                    if (!string.Equals(e.Spec.TriggerArg, lessonId, StringComparison.Ordinal)) continue;
                    DemoKind d = e.Spec.Demo;
                    if (d != DemoKind.None && d != DemoKind.Custom && !SunnyDemo.IsTourKind(d) && d != DemoKind.Nod && d != DemoKind.Press) return d;
                }
            for (int i = 0; i < _entries.Count; i++)
            {
                TipEntry e = _entries[i];
                if (e.Owner == null || e.Owner.Kind != kind || e.Spec.Trigger != TipTrigger.FirstArrival) continue;
                DemoKind d = e.Spec.Demo;
                if (d != DemoKind.None && d != DemoKind.Custom && !SunnyDemo.IsTourKind(d) && d != DemoKind.Nod && d != DemoKind.Press) return d;
            }
            return DemoKind.Tap;
        }
    }
}
