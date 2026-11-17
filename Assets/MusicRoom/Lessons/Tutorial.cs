// The tutorial (spec 08 sections 2.2-2.3, ARCHITECTURE.md section 10): guided lessons, Synthesia
// style. A glowing block comes toward each key, drum or string and the player plays it when it arrives; the lesson
// waits at every note until the right one is played, so a beginner goes at their own pace; a wrong note counts a
// mistake. Plays arrive through the recorder's observer stream (RecorderEvent), so fingertips, sticks, the nod, the mouse
// and the computer keyboard all count the same way, and the deck's playback (DuringPlayback) never plays the lesson.
//
// Plain C#: the state machine, the 64-block pool and the per-frame block poses. Rendering is LessonBlocks' job (one
// instanced draw of Matrices/Colors); geometry comes through ILessonStage. Timing runs in doubles.
// Hot paths (Update, OnPlay, Compose) allocate nothing: runs and blocks are pooled, hints come from lessons.json.
//
// The platform (12-new-instruments/platform.md 7.2-7.3). The piano, the drums and the harp keep their three code paths
// exactly (TutorialTests). Every other instrument runs the generic path through its registered ILessonTargets: the block
// shape, travel, lead and count noun from its descriptor's LessonSpec (ILessonTargets.Travel may shorten a target's
// travel), the landing, approach and size from TryGetLanding, hits from Matches on live events of the instrument's hit
// types (its Marker types when it declares any, else its Onsets), the instrument's own cue ShowTarget(t, k) with k = 1
// exactly when the block lands and 0 again after the hit, the hint from Hint(targets), purple cue blocks (LessonCue, no
// hover glow) in the same single instanced draw. The audit's fixes on this path: B2 a re-hit of a target already hit in the
// event is no mistake; B4 the summary's noun is LessonSpec.CountNoun; B6 after 8 s waiting the instrument publishes
// InstrumentSignalEvent "lesson.redemo" (Sunny shows the move at the target again), after 15 s any hit on the instrument
// counts. For every lesson: Start brings the instrument (ILayoutService.Bring(kind, Lesson)) and the countdown starts once
// it has arrived (InstrumentMoveEvent Arrived) or at once when it is at the seat; options (a pack lesson's "options", or
// Start(id, options)): NoCountdown, Accompany (the lesson's chords on their beats through the instrument's
// ILessonAccompany, else the piano sound), QuietMistakes, Forgiving; Hidden is the menu's (read Options).
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    /// <summary>Which instanced mesh a lesson draws: boxes on keys, discs on drums, capsules on strings; rings and bars for
    /// the platform's instruments (LessonSpec.Shape).</summary>
    public enum BlockShape : byte { Box = 0, Disc = 1, Capsule = 2, Ring = 3, Bar = 4 }

    /// <summary>First five (Tutorial.FirstFive.cs): the live target and the forgiving switch (ILessonLive), the hit events,
    /// the hidden first-run song; tutorials-audit.md B1 (drum targets toward the player, on the surface) and B2 (a re-hit is
    /// no mistake) on the classic path here.</summary>
    public sealed partial class Tutorial : ITutorial, ILessonLive
    {
        // ---- constants (Unity values: KEY_Z +z = behind the white-key fronts) ----
        public const double Speed = 0.18;                                    // m/s, how fast a block approaches its target
        public const double TravelPiano = 0.35, TravelDrums = 0.35, TravelHarp = 0.30;   // how far out a block first appears
        public const double Early = 0.15;                                    // s before arrival within which a play counts
        public const int MaxBlocks = 64;
        public const float HitFade = 0.3f;                                   // s a hit block takes to shrink away
        public const float WrongFlash = 0.3f;                                // s the waiting blocks show red after a wrong note
        public const double CountdownSeconds = 3;
        public const float KeyZWhite = 0.05f, KeyZBlack = 0.075f;           // where on a key the block lands (piano back)
        public const float KeyFraction = 0.6f;                               // of the key's width
        public const float KeyDepth = 0.03f;
        public const float BlockHPerMetre = 0.19f, BlockHMin = 0.012f, BlockHMax = 0.06f;
        public const float DiscR = 0.06f, DiscFraction = 0.475f, DiscH = 0.01f;
        public const float CapsuleR = 0.006f, CapsuleLen = 0.028f, CapsuleEyeGap = 0.25f;
        public const float KeyLift = 0.0005f, DiscLift = 0.001f;
        public const float PulseRate = 8f, LiveGrow = 0.08f;
        /// <summary>Block opacity: boxes and capsules, drum discs (raised from .6 / .38 with the purple cue, so a block over an
        /// ivory key reads purple, not lavender).</summary>
        public const float Opacity = 0.75f, DiscOpacity = 0.5f;
        // glows: the hover shapes, held on the waiting target
        public const float KeyGlowLength = 0.07f, KeyGlowWhite = 0.024f, KeyGlowBlack = 0.016f, KeyGlowLift = 0.001f;
        public const float DrumGlowMax = 0.24f, DrumGlowPerRadius = 1.6f, DrumGlowLift = 0.002f;
        public const float HarpGlowLength = 0.14f, HarpGlowWidth = 0.014f;

        // ---- the generic path (platform.md 7.2) ----
        /// <summary>B6: Sunny shows the move again after this long waiting on one event; after GiveUpAfter any hit counts.</summary>
        public const float RedemoAfter = 8f, GiveUpAfter = 15f;
        /// <summary>The signal the instrument's re-demo goes out on (Kind = the lesson's instrument).</summary>
        public const string RedemoSignal = "lesson.redemo";
        /// <summary>Block lift above the landing pose, and the opacity of boxes / bars and of discs / rings.</summary>
        public const float GenericLift = 0.001f, GenericOpacity = 0.65f, GenericDiscOpacity = 0.55f;
        /// <summary>The written accompaniment velocity when a chord gives none (sunny-tour 2.5).</summary>
        public const float AccompanyVel = 0.35f;

        // sRGB and their linear values: colours are lerped in linear space, the shader takes linear. The "play this next" cue
        // is purple on every path (headset feedback: the white cue replaced with purple; LessonCue): a block on its way is
        // the cue's violet, pulsing toward a lighter violet while it waits; a soft honey when hit; a muted coral after a wrong
        // note. Before: pale cream #f3ead8 / warm white #fff8ec (and earlier the glowing cyan #58dcff / #d4f6ff).
        public static readonly Color Near = LessonCue.Color, Bright = LessonCue.Bright;
        public static readonly Color HitColor = new Color32(0xe9, 0xc9, 0x8a, 0xff), WrongColor = new Color32(0xc4, 0x86, 0x76, 0xff);
        static readonly Color NearL = Near.linear, BrightL = Bright.linear, HitL = HitColor.linear, WrongL = WrongColor.linear;
        /// <summary>The generic path's palette, the same as the classic one: the purple cue, a soft honey when hit, a muted
        /// clay after a wrong note.</summary>
        public static readonly Color WarmNear = LessonCue.Color, WarmBright = LessonCue.Bright;
        public static readonly Color WarmHit = new Color32(0xe9, 0xc9, 0x8a, 0xff), WarmWrong = new Color32(0xc4, 0x86, 0x76, 0xff);
        static readonly Color WarmNearL = WarmNear.linear, WarmBrightL = WarmBright.linear, WarmHitL = WarmHit.linear, WarmWrongL = WarmWrong.linear;

        public static double TravelFor(InstrumentKind instrument) => instrument == InstrumentKind.Harp ? TravelHarp : instrument == InstrumentKind.Drums ? TravelDrums : TravelPiano;
        public static InstrumentKind KindOf(string instrument) => instrument == "drums" ? InstrumentKind.Drums : instrument == "harp" ? InstrumentKind.Harp : InstrumentKind.Piano;
        /// <summary>The three instruments whose lessons run the classic code paths.</summary>
        public static bool IsClassic(InstrumentKind k) => k == InstrumentKind.Piano || k == InstrumentKind.Drums || k == InstrumentKind.Harp;

        /// <summary>Per-event runtime state, pooled across lessons.</summary>
        sealed class Run
        {
            public double Due;
            public float Len;
            public LessonTarget[] Targets;
            public bool[] Hit = new bool[4];
            public float[] Travel = new float[4];   // generic: each target's travel (m)
            public int Remaining, Spawned;
            public float WrongT;
            public string Hint;
            public bool Redemoed;
        }

        /// <summary>One pooled block.</summary>
        sealed class Block
        {
            public bool Used, Hit;
            public Run Ev;
            public int K;
            public float HitT;
            public float KeyX; public bool Black;        // piano
            public Transform Piece;                      // drums
            public int String = -1;                      // harp
            public float W, H, D, Top, Z, R;
            public float GlowLength, GlowWidth;
            public float Land, Lift;                     // drums (B1): landing radius toward the player, height above the strike plane
            public LessonTarget Target;                  // generic
            public float Travel;
        }

        readonly ILessonLibrary _library;
        readonly ILessonStage _stage;
        readonly EventBus _events;
        readonly List<Run> _runs = new List<Run>(160);
        readonly Block[] _pool = new Block[MaxBlocks];
        int _runCount;

        // composed each frame for the one instanced draw (pool order, like the InstancedMesh)
        public readonly Matrix4x4[] Matrices = new Matrix4x4[MaxBlocks];
        public readonly Vector4[] Colors = new Vector4[MaxBlocks];

        Lesson _lesson;
        InstrumentKind _instrument;
        int _cur, _next, _digit, _hits, _total, _mistakes, _version;
        double _t, _time, _elapsed, _count, _lead, _spb;
        string _hint = "";
        bool _hasSummary;
        TutorialSummary _summary;

        // the platform
        bool _generic, _awaitArrival;
        ILessonTargets _targets;
        LessonSpec _spec;
        BlockShape _shape;
        LessonOptions _options;
        double _waitT;
        readonly List<string> _hitTypes = new List<string>(4);
        // accompaniment
        LessonChord[] _chords;
        int _chord;
        ILessonAccompany _accompany;
        readonly VoiceHandle[] _voices = new VoiceHandle[8];
        int _voiceCount;
        double _accReleaseAt = double.PositiveInfinity;
        bool _accSounding;

        public Tutorial(ILessonLibrary library, ILessonStage stage, EventBus events = null)
        {
            _library = library;
            _stage = stage;
            _events = events;
            for (int i = 0; i < MaxBlocks; i++) _pool[i] = new Block();
        }

        // ---------------- ITutorial ----------------
        public TutorialState State { get; private set; } = TutorialState.Idle;
        public string LessonId => _lesson?.id;
        public Lesson Lesson => _lesson;
        public InstrumentKind Instrument => _instrument;
        public int Version => _version;
        /// <summary>Seconds left while counting down (the menu shows <see cref="Digit"/> = max(1, ceil)).</summary>
        public float Countdown => State == TutorialState.Countdown ? (float)System.Math.Max(0, _count) : 0f;
        public int Digit => _digit;
        public string Hint => _hint;
        /// <summary>Targets played of the lesson's targets (the panel's progress bar and "N of M").</summary>
        public float Progress => _total > 0 ? (float)_hits / _total : 0f;
        public int EventsDone => _hits;
        public int EventsTotal => _total;
        public int Mistakes => _mistakes;
        /// <summary>Live while a lesson runs (notes = total, mistakes so far, seconds elapsed); final once Done.</summary>
        public TutorialSummary Summary => _hasSummary ? _summary : new TutorialSummary { Notes = _total, Mistakes = _mistakes, Seconds = (float)_elapsed };
        public bool HasSummary => _hasSummary;
        public bool Running => State == TutorialState.Countdown || State == TutorialState.Playing || State == TutorialState.Waiting;
        /// <summary>B4: what the summary's count is of ("12 notes", "16 strokes", "8 rings"): on the generic path the
        /// instrument's LessonSpec.CountNoun; the classic three keep "notes".</summary>
        public string CountNoun => _generic && !string.IsNullOrEmpty(_spec?.CountNoun) ? _spec.CountNoun : "notes";
        /// <summary>The running lesson's options (Start's and the lesson's own "options").</summary>
        public LessonOptions Options => _options;
        /// <summary>The lesson runs the generic path (an instrument other than the classic three).</summary>
        public bool Generic => _generic;
        /// <summary>Started, the countdown waits for the instrument to arrive at the seat (InstrumentMoveEvent Arrived).</summary>
        public bool AwaitingArrival => _awaitArrival && State == TutorialState.Countdown;
        /// <summary>Seconds the current event has been waited for (the generic path's B6 clock).</summary>
        public double WaitSeconds => State == TutorialState.Waiting ? _waitT : 0;
        /// <summary>Chords of the accompaniment played so far (tests).</summary>
        public int ChordsPlayed => _chord;

        // ---- extra read-backs (tests, the menu) ----
        public double LessonTime => _t;
        public double Elapsed => _elapsed;
        public double Lead => _lead;
        /// <summary>The sheet-music hook (platform.md 13.5): the event being waited for, or the next to land; -1 when no
        /// lesson runs.</summary>
        public int CurrentEvent => Running && _cur < _runCount ? _cur : -1;
        public int EventCount => _runCount;
        public double DueOf(int k) => _runs[k].Due;
        public bool TargetHit(int k, int target) => _runs[k].Hit[target];
        public float WrongTime => _cur < _runCount ? _runs[_cur].WrongT : 0f;
        /// <summary>Blocks in use (spawned and not yet faded).</summary>
        public int ActiveBlocks { get { int n = 0; for (int i = 0; i < MaxBlocks; i++) if (_pool[i].Used) n++; return n; } }
        /// <summary>Whether pool slot <paramref name="slot"/> holds a block (tutorial-check's T.pool[i].used).</summary>
        public bool IsBlockUsed(int slot) => _pool[slot].Used;
        /// <summary>Instances composed by the last Compose (what is drawn).</summary>
        public int BlockCount { get; private set; }
        public BlockShape Shape => _generic ? _shape : _instrument == InstrumentKind.Drums ? BlockShape.Disc : _instrument == InstrumentKind.Harp ? BlockShape.Capsule : BlockShape.Box;
        /// <summary>The blocks' instrument is on show (false: nothing is drawn, no glows).</summary>
        public bool Visible { get; private set; }
        public Bounds BlockBounds { get; private set; }
        /// <summary>Free-running seconds: drives the pulse.</summary>
        public double PulseTime => _time;
        public float Pulse => 0.5f + 0.5f * Mathf.Sin((float)(_time * PulseRate));

        public void Start(string lessonId) => Start(Find(lessonId), LessonOptions.None);

        public void Start(string lessonId, LessonOptions options) => Start(Find(lessonId), options);

        public void Start(Lesson lesson) => Start(lesson, LessonOptions.None);

        /// <summary>The implicit stop bumps the version but publishes nothing: a
        /// polling panel never sees that transient idle, so event subscribers (the menu) must not see it either.</summary>
        public void Start(Lesson lesson, LessonOptions options)
        {
            Clear();
            _version++;
            if (lesson?.events == null) { Publish(); return; }
            _lesson = lesson;
            _instrument = ResolveKind(lesson.instrument);
            _generic = !IsClassic(_instrument);
            _options = options | ParseOptions(lesson.options);
            InstrumentDescriptor d = _stage?.Describe(_instrument);
            _spec = d?.Lessons;
            _targets = _generic ? _stage?.TargetsFor(_instrument) : null;
            if (_generic)
            {
                if (_spec == null) _spec = new LessonSpec();
                _shape = ShapeOf(_spec.Shape);
                _lead = _spec.LeadSeconds > 0f ? _spec.LeadSeconds : _spec.Travel / Speed;
                HitTypes(d);
            }
            else _lead = TravelFor(_instrument) / Speed;
            _spb = 60.0 / lesson.bpm;
            _runCount = 0;
            _total = 0;
            foreach (LessonEvent e in lesson.events)
            {
                if (_runCount == _runs.Count) _runs.Add(new Run());
                Run r = _runs[_runCount++];
                int n = e.targets?.Length ?? 0;
                r.Due = _lead + e.beat * _spb;
                r.Len = e.len;
                r.Targets = e.targets ?? System.Array.Empty<LessonTarget>();
                if (r.Hit.Length < n) r.Hit = new bool[n];
                for (int k = 0; k < r.Hit.Length; k++) r.Hit[k] = false;
                if (r.Travel.Length < n) r.Travel = new float[n];
                r.Remaining = n;
                r.Spawned = 0;
                r.WrongT = 0f;
                r.Redemoed = false;
                if (_generic)
                {
                    for (int k = 0; k < n; k++) r.Travel[k] = _targets != null ? _targets.Travel(r.Targets[k], _spec.Travel) : _spec.Travel;
                    r.Hint = LessonHints.Generic(_targets, e);
                }
                else r.Hint = string.IsNullOrEmpty(e.hint) ? LessonHints.For(_instrument, e, _stage?.Harp) : e.hint;
                _total += n;
            }
            _cur = _next = 0;
            _t = _elapsed = _waitT = 0;
            _hits = _mistakes = 0;
            _hasSummary = false;
            _summary = default;
            _count = CountdownSeconds;
            _digit = (int)CountdownSeconds;
            _chords = (_options & LessonOptions.Accompany) != 0 ? lesson.accompany : null;
            _chord = 0;
            _accompany = _chords != null ? _stage?.AccompanyFor(_instrument) : null;
            // The instrument comes to the seat first (Bring), while this tutorial is still idle; the countdown waits for it.
            _awaitArrival = !BringInstrument();
            State = TutorialState.Countdown;
            _hint = _runCount > 0 ? _runs[0].Hint : "";
            if (!_awaitArrival && (_options & LessonOptions.NoCountdown) != 0) BeginPlaying();
            Bump();
        }

        InstrumentKind ResolveKind(string id)
        {
            IInstrumentRegistry reg = _stage?.Registry;
            if (reg != null && reg.TryParse(id, out InstrumentKind k)) return k;
            if (InstrumentIds.TryParse(id, out InstrumentKind b) && InstrumentIds.IsPlayable(b)) return b;
            return KindOf(id);
        }

        static BlockShape ShapeOf(LessonBlockShape s)
        {
            switch (s)
            {
                case LessonBlockShape.Disc: return BlockShape.Disc;
                case LessonBlockShape.Capsule: return BlockShape.Capsule;
                case LessonBlockShape.Ring: return BlockShape.Ring;
                case LessonBlockShape.Bar: return BlockShape.Bar;
                default: return BlockShape.Box;
            }
        }

        /// <summary>A pack lesson's "options": LessonOptions names (unknown names are ignored).</summary>
        public static LessonOptions ParseOptions(string[] names)
        {
            LessonOptions o = LessonOptions.None;
            if (names == null) return o;
            foreach (string n in names)
                if (!string.IsNullOrEmpty(n) && System.Enum.TryParse(n.Trim(), true, out LessonOptions v)) o |= v;
            return o;
        }

        // What can hit a target: the instrument's Marker types when it declares any (a held pitch), else its
        // Onsets. Releases, controls, states and other instruments' events never count.
        void HitTypes(InstrumentDescriptor d)
        {
            _hitTypes.Clear();
            if (d?.Events == null) return;
            foreach (EventTypeSpec s in d.Events) if (s.Role == EventRole.Marker && !string.IsNullOrEmpty(s.Type)) _hitTypes.Add(s.Type);
            if (_hitTypes.Count > 0) return;
            foreach (EventTypeSpec s in d.Events) if (s.Role == EventRole.Onset && !string.IsNullOrEmpty(s.Type)) _hitTypes.Add(s.Type);
        }

        bool IsHitType(string type)
        {
            if (type == null) return false;
            for (int i = 0; i < _hitTypes.Count; i++) if (string.Equals(_hitTypes[i], type, System.StringComparison.Ordinal)) return true;
            return false;
        }

        // ---------------- bringing the instrument (platform.md 7.3) ----------------

        /// <summary>True when the instrument is at the seat now (the countdown may run).</summary>
        bool BringInstrument()
        {
            ILayoutService layout = _stage?.Layout;
            if (layout == null) return true;
            bool brought = layout.Bring(_instrument, BringReason.Lesson);
            if (AtSeat(layout)) return true;
            // A layout without Bring (or one that refused): the single layout turns to it, as the menu did before.
            if (!brought && !layout.IsMoving(_instrument) && layout.Mode == LayoutMode.Single && layout.Current != _instrument)
                layout.Set(LayoutMode.Single, _instrument);
            return AtSeat(layout);
        }

        bool AtSeat(ILayoutService layout)
        {
            if (layout.IsMoving(_instrument)) return false;
            InstrumentPlace p = layout.PlaceOf(_instrument);
            return p == InstrumentPlace.Seat || (p != InstrumentPlace.Gliding && layout.IsInScene(_instrument));
        }

        /// <summary>Layout's InstrumentMoveEvent (the module forwards it): the lesson's instrument arrived, the countdown runs.</summary>
        public void OnMove(in InstrumentMoveEvent e)
        {
            if (!_awaitArrival || State != TutorialState.Countdown || e.Kind != _instrument || e.Phase != MovePhase.Arrived) return;
            Arrived();
        }

        void Arrived()
        {
            _awaitArrival = false;
            if ((_options & LessonOptions.NoCountdown) != 0) BeginPlaying();
            Bump();
        }

        void BeginPlaying()
        {
            _count = 0;
            State = TutorialState.Playing;
        }

        /// <summary>The lesson stays (the menu still names it); its blocks go.</summary>
        public void Stop()
        {
            Clear();
            Bump();
        }

        void Clear()
        {
            for (int i = 0; i < MaxBlocks; i++)
            {
                Block b = _pool[i];
                if (b.Used && _generic && !b.Hit && b.Target != null) _targets?.ShowTarget(b.Target, 0f);   // the cue goes with it
                b.Used = false;
                b.Ev = null;
                b.Target = null;
            }
            BlockCount = 0;
            _runCount = 0;
            State = TutorialState.Idle;
            _hint = "";
            _awaitArrival = false;
            ReleaseChord();
            _accReleaseAt = double.PositiveInfinity;
        }

        /// <summary>A play from the recorder stream. Only cur, the earliest unfinished event, can be hit,
        /// and only while in reach (waiting, or due - t &lt;= EARLY); anything else in reach is a mistake.</summary>
        public void OnPlay(in TakeEvent ev, bool duringPlayback)
        {
            if (State != TutorialState.Playing && State != TutorialState.Waiting) return;
            if (duringPlayback) return;                                    // the tape deck's playback must not play the lesson
            if (_cur >= _runCount) return;
            if (_generic) { OnPlayGeneric(ev); return; }
            Run cur = _runs[_cur];
            int k = -1;
            bool rehit = false;                                             // B2: a target of this event already played
            if (_instrument == InstrumentKind.Piano && ev.type == TakeEventType.PianoOn)
            {
                for (int i = 0; i < cur.Targets.Length; i++) if (cur.Targets[i].midi == ev.midi) { if (!cur.Hit[i]) { k = i; break; } rehit = true; }
            }
            else if (_instrument == InstrumentKind.Drums && ev.type == TakeEventType.Drum)
            {
                for (int i = 0; i < cur.Targets.Length; i++) if (cur.Targets[i].kind == ev.kind) { if (!cur.Hit[i]) { k = i; break; } rehit = true; }
            }
            else if (_instrument == InstrumentKind.Harp && ev.type == TakeEventType.Harp)
            {
                for (int i = 0; i < cur.Targets.Length; i++) if (cur.Targets[i].i == ev.i) { if (!cur.Hit[i]) { k = i; break; } rehit = true; }
            }
            else return;                                                    // another instrument, a release, a choke: ignored
            bool waiting = State == TutorialState.Waiting;
            bool inReach = waiting || cur.Due - _t <= Early;
            if (k < 0 && !rehit) rehit = RepeatOfPrevious(ev);              // B2: the last event's target struck again at once
            if (k < 0 && !rehit && inReach && (_options & LessonOptions.Forgiving) != 0) k = FirstUnhit(cur);
            if (k < 0)
            {
                if (inReach && !rehit) Mistake(cur);
                return;
            }
            if (!inReach) return;
            Remember(ev);                                                   // B2: the next event's bounce window
            HitTarget(cur, k, waiting);
        }

        // The generic path: Matches on the instrument's hit types; B2 (a re-hit is no mistake) and B6 (after 15 s any hit).
        void OnPlayGeneric(in TakeEvent ev)
        {
            if (!IsHitType(ev.type)) return;                                // a release, a control, another instrument: ignored
            Run cur = _runs[_cur];
            int k = -1;
            bool rehit = false;
            for (int i = 0; i < cur.Targets.Length; i++)
            {
                if (_targets == null || !_targets.Matches(cur.Targets[i], ev)) continue;
                if (!cur.Hit[i]) { k = i; break; }
                rehit = true;
            }
            bool waiting = State == TutorialState.Waiting;
            bool inReach = waiting || cur.Due - _t <= Early;
            if (k < 0 && !rehit && inReach && ((_options & LessonOptions.Forgiving) != 0 || (waiting && _waitT >= GiveUpAfter))) k = FirstUnhit(cur);
            if (k < 0)
            {
                if (inReach && !rehit) Mistake(cur);
                return;
            }
            if (!inReach) return;
            HitTarget(cur, k, waiting);
        }

        static int FirstUnhit(Run r)
        {
            for (int i = 0; i < r.Targets.Length; i++) if (!r.Hit[i]) return i;
            return -1;
        }

        void Mistake(Run cur)
        {
            PublishHit(false);                                              // first five: Sunny hears a wrong note too
            if ((_options & LessonOptions.QuietMistakes) != 0) return;     // no red flash, no count (the wrong note still sounds)
            _mistakes++;
            cur.WrongT = WrongFlash;
            Bump();
        }

        void HitTarget(Run cur, int k, bool waiting)
        {
            cur.Hit[k] = true;
            cur.Remaining--;
            _hits++;
            PublishHit(true);                                               // first five: TutorialHitEvent
            for (int i = 0; i < MaxBlocks; i++)
            {
                Block b = _pool[i];
                if (b.Used && b.Ev == cur && b.K == k) { b.Hit = true; b.HitT = 0f; }
            }
            if (_generic && _targets != null && k < cur.Targets.Length) _targets.ShowTarget(cur.Targets[k], 0f);   // the cue is off after the hit
            if (cur.Remaining == 0)
            {
                if (waiting) State = TutorialState.Playing;
                Accompany();                                                // first five: a chord on the LAST note's beat too (Advance may finish)
                Advance();
            }
            Accompany();                                                    // a chord on this beat plays with the hit
            Bump();
        }

        /// <summary>Once per frame in the Tutorial phase. Adds the waiting targets' glows.</summary>
        public void Update(float dt, HoverGlowPool glow)
        {
            _time += dt;
            TutorialState st = State;
            if (st == TutorialState.Idle) return;
            if (st == TutorialState.Countdown)
            {
                if (_awaitArrival)
                {
                    // The arrival event is the trigger; this is the safety net (a layout that moved it without one).
                    ILayoutService layout = _stage?.Layout;
                    if (layout == null || AtSeat(layout)) Arrived();
                }
                else
                {
                    _count -= dt;
                    int digit = System.Math.Max(1, (int)System.Math.Ceiling(_count));
                    if (digit != _digit) { _digit = digit; Bump(); }
                    if (_count <= 0) { State = TutorialState.Playing; Bump(); }
                }
            }
            else if (st == TutorialState.Playing)
            {
                _t += dt;
                _elapsed += dt;
                if (_cur < _runCount && _t >= _runs[_cur].Due)
                {
                    _t = _runs[_cur].Due;
                    State = TutorialState.Waiting;
                    _waitT = 0;
                    Bump();
                }
            }
            else if (st == TutorialState.Waiting)
            {
                _elapsed += dt;
                if (_generic) Stall(dt);
            }
            if (State == TutorialState.Playing || State == TutorialState.Waiting) Spawn();
            Accompany();
            if (_accSounding && _time >= _accReleaseAt) ReleaseChord();
            Animate(dt, glow);
        }

        // B6: after RedemoAfter of waiting on one event, Sunny is asked to show the move at the target again (once per event).
        void Stall(float dt)
        {
            _waitT += dt;
            if (_cur >= _runCount) return;
            Run cur = _runs[_cur];
            if (cur.Redemoed || _waitT < RedemoAfter) return;
            cur.Redemoed = true;
            _events?.Publish(new InstrumentSignalEvent { Kind = _instrument, Signal = RedemoSignal });
        }

        // Move on to the next event that still has something to play, or finish.
        void Advance()
        {
            while (_cur < _runCount && _runs[_cur].Remaining == 0) _cur++;
            _waitT = 0;
            if (_cur >= _runCount) Finish();
            else _hint = _runs[_cur].Hint;
        }

        void Finish()
        {
            State = TutorialState.Done;
            _hint = "";
            _hasSummary = true;
            _summary = new TutorialSummary { Notes = _total, Mistakes = _mistakes, Seconds = (float)_elapsed };
            // The last chord rings on for its written length, then lets go.
            if (_accSounding && _chords != null && _chord > 0)
            {
                float len = _chords[_chord - 1].len;
                _accReleaseAt = _time + System.Math.Max(0.5, (len > 0f ? len : 2f) * _spb);
            }
        }

        // ---------------- the accompaniment (sunny-tour 2.5; marimba.md R6) ----------------

        // Each chord plays when the lesson clock reaches its beat while playing: wait mode freezes the clock, so a chord on a
        // waited note's beat plays at the moment that note is hit, and it lets the previous chord go.
        void Accompany()
        {
            if (_chords == null || State != TutorialState.Playing) return;
            while (_chord < _chords.Length)
            {
                LessonChord c = _chords[_chord];
                if (c == null) { _chord++; continue; }
                if (_t < _lead + c.beat * _spb) break;
                _chord++;
                PlayChord(c);
            }
        }

        void PlayChord(LessonChord c)
        {
            ReleaseChord();
            if (c.midi == null || c.midi.Length == 0) return;
            float vel = c.vel > 0f ? c.vel : AccompanyVel;
            if (_accompany != null) _accompany.Play(c.midi, vel);
            else
            {
                IAudioEngine audio = _stage?.Audio;
                if (audio == null) return;
                _voiceCount = 0;
                for (int i = 0; i < c.midi.Length && _voiceCount < _voices.Length; i++) _voices[_voiceCount++] = audio.Piano(c.midi[i], vel);
            }
            _accSounding = true;
        }

        void ReleaseChord()
        {
            if (!_accSounding) return;
            _accSounding = false;
            if (_accompany != null) { _accompany.Release(); return; }
            IAudioEngine audio = _stage?.Audio;
            for (int i = 0; i < _voiceCount; i++) if (_voices[i].IsValid) audio?.Release(_voices[i]);
            _voiceCount = 0;
        }

        // Blocks appear once they are within the travel distance of their target.
        void Spawn()
        {
            while (_next < _runCount && _runs[_next].Due - _t <= _lead)
            {
                Run ev = _runs[_next];
                for (; ev.Spawned < ev.Targets.Length; ev.Spawned++) if (!AddBlock(ev, ev.Spawned)) return;   // pool full: next frame
                _next++;
            }
        }

        bool AddBlock(Run ev, int k)
        {
            // Only reachable when the 64-block pool overflowed and the player hit this target before its block could
            // spawn: a played target simply gets no block (rather than a parked block that never fades).
            if (ev.Hit[k]) return true;
            Block b = null;
            for (int i = 0; i < MaxBlocks; i++) if (!_pool[i].Used) { b = _pool[i]; break; }
            if (b == null) return false;
            LessonTarget target = ev.Targets[k];
            b.Piece = null;
            b.String = -1;
            b.Target = null;
            if (_generic)
            {
                if (_targets == null || target == null || !_targets.TryGetLanding(target, out _, out _, out _)) return Skip(ev, k);
                b.Target = target;
                b.Travel = ev.Travel[k];
                b.H = Shape == BlockShape.Disc || Shape == BlockShape.Ring ? DiscH : Mathf.Clamp(ev.Len * (float)_spb * b.Travel / (float)_lead * BlockHPerMetre, BlockHMin, BlockHMax);
            }
            else if (_instrument == InstrumentKind.Piano)
            {
                IPianoApi piano = _stage?.Piano;
                if (piano == null || !piano.TryGetKey(target.midi, out PianoKeyInfo key)) return Skip(ev, k);
                b.KeyX = key.X;
                b.Black = key.Black;
                b.Top = key.Black ? piano.BTOP : 0f;
                b.Z = key.Black ? KeyZBlack : KeyZWhite;
                b.W = (key.Black ? piano.BW : piano.W) * KeyFraction;
                b.H = Mathf.Clamp((float)(ev.Len * _spb * Speed) * BlockHPerMetre, BlockHMin, BlockHMax);
                b.D = KeyDepth;
                b.GlowLength = KeyGlowLength;
                b.GlowWidth = key.Black ? KeyGlowBlack : KeyGlowWhite;
            }
            else if (_instrument == InstrumentKind.Drums)
            {
                if (!TryPiece(target.kind, out DrumPieceInfo piece)) return Skip(ev, k);
                b.Piece = piece.Transform;
                // B1: every target but the pad lands off centre toward the player, on the surface (Land, Lift), so it is
                // never buried in a cymbal, never plays a cymbal's bell, and sits 8-12 cm nearer. The disc and glow shrink
                // to stay on the head.
                LandOn(piece, out b.Land, out b.Lift);
                b.R = Mathf.Min(piece.Radius * (piece.Pad ? DiscFraction : LandDiscFraction), DiscR);
                b.GlowLength = b.GlowWidth = Mathf.Min(DrumGlowMax, piece.Radius * (piece.Pad ? DrumGlowPerRadius : LandGlowPerRadius));
            }
            else
            {
                IHarpApi harp = _stage?.Harp;
                if (harp == null || target.i < 0 || target.i >= harp.StringCount) return Skip(ev, k);
                b.String = target.i;
                b.GlowLength = HarpGlowLength;
                b.GlowWidth = HarpGlowWidth;
            }
            b.Used = true;
            b.Ev = ev;
            b.K = k;
            b.Hit = false;
            b.HitT = 0f;
            return true;
        }

        // The kick target is the hand pad beside the snare, never the big shell.
        bool TryPiece(string kind, out DrumPieceInfo piece)
        {
            piece = default;
            IDrumsApi drums = _stage?.Drums;
            if (drums == null || kind == null) return false;
            IReadOnlyList<DrumPieceInfo> list = drums.Pieces;
            bool kick = kind == "kick";
            for (int i = 0; i < list.Count; i++)
            {
                DrumPieceInfo p = list[i];
                if (p.Kind == kind && (!kick || p.Pad)) { piece = p; return p.Transform != null; }
            }
            return false;
        }

        // A target the instrument does not have (bad lesson data) is passed over rather than stalling.
        bool Skip(Run ev, int k)
        {
            ev.Hit[k] = true;
            ev.Remaining--;
            _total--;
            if (ev.Remaining == 0 && _cur < _runCount && _runs[_cur] == ev)
            {
                Advance();
                Bump();
            }
            return true;
        }

        // Animation (logic half): fades, the red flash, harp highlights and glows on the live targets.
        void Animate(float dt, HoverGlowPool glow)
        {
            bool visible = _stage != null && _stage.IsShown(_instrument);
            bool waiting = State == TutorialState.Waiting;
            Run cur = _cur < _runCount ? _runs[_cur] : null;
            if (cur != null && cur.WrongT > 0f) cur.WrongT -= dt;
            float strength = 0.6f + 0.4f * Pulse;
            for (int i = 0; i < MaxBlocks; i++)
            {
                Block b = _pool[i];
                if (!b.Used) continue;
                if (b.Hit)
                {
                    b.HitT += dt;
                    if (b.HitT >= HitFade) { b.Used = false; b.Ev = null; b.Target = null; continue; }
                }
                if (_generic)
                {
                    // The instrument's own cue (no glow): k rises to 1 exactly as the block lands, 0 after the hit.
                    if (!b.Hit && _targets != null) _targets.ShowTarget(b.Target, visible ? CueK(b.Ev) : 0f);
                    continue;
                }
                bool live = b.Ev == cur && waiting && !b.Hit;
                // Hidden instrument (MR placing, out of the layout): no glow or highlight on it either.
                if (!live || !visible) continue;
                if (b.String >= 0) _stage.Harp?.Highlight(b.String, strength);        // the string itself glows
                if (glow != null && TryGlow(b, strength, out HoverGlowSpec spec)) { spec.Tint = LessonCue.Color; glow.Add(spec); }   // a purple glow
            }
            Compose();
        }

        /// <summary>The cue's k for an event: 1 - (due - t) / lead, clamped: 0 when its block appears, 1 when it lands.</summary>
        float CueK(Run ev)
        {
            if (_lead <= 0) return 1f;
            double k = 1.0 - System.Math.Max(0, ev.Due - _t) / _lead;
            return k <= 0 ? 0f : k >= 1 ? 1f : (float)k;
        }

        // The same glow shapes the instruments use for a hovering fingertip, held on the target.
        bool TryGlow(Block b, float strength, out HoverGlowSpec spec)
        {
            spec = default;
            if (_instrument == InstrumentKind.Piano)
            {
                Transform root = _stage.Piano?.Root;
                if (root == null) return false;
                spec = HoverGlowSpec.At(root.TransformPoint(b.KeyX, b.Top + KeyGlowLift, b.Z), root.up, b.GlowLength, b.GlowWidth, strength);
                spec.Axis = root.forward;
                return true;
            }
            if (_instrument == InstrumentKind.Drums)
            {
                if (b.Piece == null) return false;
                spec = HoverGlowSpec.At(b.Piece.TransformPoint(LandPoint(b, b.Lift + DrumGlowLift - DiscLift)), b.Piece.up, b.GlowLength, b.GlowWidth, strength);
                return true;
            }
            IHarpApi harp = _stage.Harp;
            Transform h = harp?.Root;
            if (h == null) return false;
            Vector3 a = harp.StringStart(b.String), e = harp.StringEnd(b.String);
            // right-handed normal (0, 0, 1) = Unity harp -z; the axis runs along the string.
            spec = HoverGlowSpec.At(h.TransformPoint(harp.PlayPoint(b.String)), -h.forward, b.GlowLength, b.GlowWidth, strength);
            spec.Axis = h.TransformDirection(e - a);
            return true;
        }

        /// <summary>Animation (pose half): the world matrix and colour of every live block, in pool order, for the
        /// one instanced draw. Called by Update and again at the end of the frame so blocks follow an instrument moved later
        /// in the frame (grab, page turn) without a frame of lag.</summary>
        public int Compose()
        {
            int n = 0;
            Visible = State != TutorialState.Idle && _stage != null && _stage.IsShown(_instrument);
            if (!Visible) { BlockCount = 0; return 0; }
            if (_generic) return ComposeGeneric();
            bool waiting = State == TutorialState.Waiting;
            Run cur = _cur < _runCount ? _runs[_cur] : null;
            float pulse = Pulse;
            float alpha = _instrument == InstrumentKind.Drums ? DiscOpacity : Opacity;
            Matrix4x4 parent = Matrix4x4.identity;
            Vector3 eyes = Vector3.zero;
            IHarpApi harp = null;
            if (_instrument == InstrumentKind.Piano)
            {
                Transform root = _stage.Piano?.Root;
                if (root == null) { BlockCount = 0; return 0; }
                parent = root.localToWorldMatrix;
            }
            else if (_instrument == InstrumentKind.Harp)
            {
                harp = _stage.Harp;
                Transform root = harp?.Root;
                if (root == null) { BlockCount = 0; return 0; }
                parent = root.localToWorldMatrix;
                eyes = root.InverseTransformPoint(_stage.EyePosition);    // capsules come in from the player's eyes
            }
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < MaxBlocks; i++)
            {
                Block b = _pool[i];
                if (!b.Used) continue;
                Run ev = b.Ev;
                float dist = (float)(System.Math.Max(0, ev.Due - _t) * Speed);
                bool live = ev == cur && waiting && !b.Hit;
                float scale = b.Hit ? 1f - b.HitT / HitFade : live ? 1f + LiveGrow * pulse : 1f;
                Color c = b.Hit ? HitL : ev.WrongT > 0f ? WrongL : live ? Color.Lerp(NearL, BrightL, pulse) : NearL;
                Matrix4x4 local;
                if (_instrument == InstrumentKind.Piano)
                    local = Matrix4x4.TRS(new Vector3(b.KeyX, b.Top + dist + KeyLift, b.Z), Quaternion.identity, new Vector3(b.W * scale, b.H * scale, b.D * scale));
                else if (_instrument == InstrumentKind.Drums)
                {
                    if (b.Piece == null) continue;
                    parent = b.Piece.localToWorldMatrix;
                    local = Matrix4x4.TRS(LandPoint(b, dist + b.Lift), Quaternion.identity, new Vector3(b.R * scale, DiscH * scale, b.R * scale));
                }
                else
                {
                    // On the string where a seated player plucks it, coming in along the line of sight; it stops 0.25 m short
                    // of the eyes, so from the seat it always sits over its own string while it approaches.
                    Vector3 p = harp.PlayPoint(b.String);
                    Vector3 v = eyes - p;
                    float len = v.magnitude;
                    float room = Mathf.Max(0f, len - CapsuleEyeGap);
                    Vector3 pos = len > 1e-6f ? p + v * (Mathf.Min(dist, room) / len) : p;
                    Vector3 along = harp.StringEnd(b.String) - harp.StringStart(b.String);
                    Quaternion q = along.sqrMagnitude > 1e-12f ? Quaternion.FromToRotation(Vector3.up, along) : Quaternion.identity;
                    local = Matrix4x4.TRS(pos, q, new Vector3(scale, scale, scale));
                }
                Matrix4x4 m = parent * local;
                Matrices[n] = m;
                Colors[n] = new Vector4(c.r, c.g, c.b, alpha);
                Vector3 at = m.GetColumn(3);
                min = Vector3.Min(min, at);
                max = Vector3.Max(max, at);
                n++;
            }
            BlockCount = n;
            if (n > 0)
            {
                var bounds = new Bounds((min + max) * 0.5f, max - min);
                bounds.Expand(0.2f);                                       // the block meshes reach at most 6 cm from their pivots
                BlockBounds = bounds;
            }
            return n;
        }

        // The generic block: at its landing pose (its up along the surface normal), lifted back along the approach by the
        // distance still to travel; the footprint from TryGetLanding, the height encoding the length (discs and rings flat).
        int ComposeGeneric()
        {
            int n = 0;
            bool waiting = State == TutorialState.Waiting;
            Run cur = _cur < _runCount ? _runs[_cur] : null;
            float pulse = Pulse;
            BlockShape shape = _shape;
            float alpha = shape == BlockShape.Disc || shape == BlockShape.Ring ? GenericDiscOpacity : GenericOpacity;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < MaxBlocks; i++)
            {
                Block b = _pool[i];
                if (!b.Used || _targets == null || b.Target == null) continue;
                if (!_targets.TryGetLanding(b.Target, out Pose landing, out Vector3 approach, out Vector2 size)) continue;
                Run ev = b.Ev;
                float dist = _lead > 0 ? (float)(System.Math.Max(0, ev.Due - _t) / _lead * b.Travel) : 0f;
                bool live = ev == cur && waiting && !b.Hit;
                float scale = b.Hit ? 1f - b.HitT / HitFade : live ? 1f + LiveGrow * pulse : 1f;
                Color c = b.Hit ? WarmHitL : ev.WrongT > 0f ? WarmWrongL : live ? Color.Lerp(WarmNearL, WarmBrightL, pulse) : WarmNearL;
                Vector3 dir = approach.sqrMagnitude > 1e-12f ? approach.normalized : landing.rotation * Vector3.down;
                Vector3 up = landing.rotation * Vector3.up;
                Vector3 pos = landing.position - dir * dist + up * GenericLift;
                Vector3 s;
                switch (shape)
                {
                    case BlockShape.Disc:
                    case BlockShape.Ring: s = new Vector3(size.x * 0.5f, DiscH, size.y * 0.5f); break;          // unit radius meshes
                    case BlockShape.Capsule: s = new Vector3(size.x / (2f * CapsuleR), size.y / (CapsuleLen + 2f * CapsuleR), size.x / (2f * CapsuleR)); break;
                    default: s = new Vector3(size.x, b.H, size.y); break;
                }
                Matrix4x4 m = Matrix4x4.TRS(pos, landing.rotation, s * scale);
                Matrices[n] = m;
                Colors[n] = new Vector4(c.r, c.g, c.b, alpha);
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
                n++;
            }
            BlockCount = n;
            if (n > 0)
            {
                var bounds = new Bounds((min + max) * 0.5f, max - min);
                bounds.Expand(0.4f);                                       // a generic block's footprint is at most ~0.2 m
                BlockBounds = bounds;
            }
            return n;
        }

        /// <summary>The composed colour of instance i in sRGB (tests compare against the hex values).</summary>
        public Color BlockColorSrgb(int i) { Vector4 v = Colors[i]; return new Color(v.x, v.y, v.z, v.w).gamma; }

        // version++ "whenever something the panel shows has changed", published as TutorialEvent at once.
        void Bump()
        {
            _version++;
            Publish();
        }

        void Publish() => _events?.Publish(new TutorialEvent { State = State, LessonId = LessonId, Version = _version });
    }
}
