// The tutorial's generic path (platform.md 7.2-7.3) on a scripted stage: a fake instrument's ILessonTargets (landings on
// a row of five fields, Matches on its "handpan" onsets), a fake layout (Bring, PlaceOf, the arrival), an optional
// ILessonAccompany. Blocks fall from the target's travel along the approach and land on TryGetLanding, in the purple cue
// palette (LessonCue); the cue's k reaches 1 exactly at the landing and goes back to 0 after the hit, on the event's own target
// instance; hits come from Matches on live events only; B2 (a re-hit is no mistake), B4 (the count noun), B6 (the re-demo
// signal at 8 s, any hit after 15 s); CurrentEvent; the countdown waits for Arrived; NoCountdown, QuietMistakes,
// Forgiving, the pack's "options" and the accompaniment; the hot paths allocate nothing.
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Lessons
{
    public sealed class FakeTargets : ILessonTargets
    {
        public const string Onset = "handpan";
        public InstrumentKind Kind => InstrumentKind.Handpan;
        public readonly Vector3 Origin = new Vector3(0.1f, 0.8f, 0.5f);
        public Quaternion Tilt = Quaternion.Euler(-28f, 0f, 0f);       // the fields lean toward the player
        public readonly List<(LessonTarget t, float k)> Cues = new List<(LessonTarget, float)>();
        public bool RecordCues = true;
        public int ShortTravelField = 4;

        public Vector3 Landing(int i) => Origin + Tilt * new Vector3(0.1f * (i - 2), 0f, 0f);
        public Vector3 Up => Tilt * Vector3.up;

        public bool Validate(in LessonTarget t, out string why) { why = t.i >= 0 && t.i < 5 ? null : "out of range"; return why == null; }

        public bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size)
        {
            landing = new Pose(Landing(t.i), Tilt);
            approach = -Up;
            size = new Vector2(0.06f, 0.04f);
            return t.i >= 0 && t.i < 5;
        }

        public bool Matches(in LessonTarget t, in TakeEvent e) => e.type == Onset && e.i == t.i;
        public void ShowTarget(in LessonTarget t, float k) { if (RecordCues) Cues.Add((t, k)); }
        public string Hint(IReadOnlyList<LessonTarget> targets) => targets.Count == 1 ? "Tap field " + targets[0].i : "Tap fields " + targets[0].i + " and " + targets[1].i;
        public float Travel(in LessonTarget t, float travel) => t.i == ShortTravelField ? 0.15f : travel;
    }

    public sealed class FakeAccompany : ILessonAccompany
    {
        public InstrumentKind Kind => InstrumentKind.Handpan;
        public readonly List<string> Calls = new List<string>();
        public void Play(int[] midi, float vel) => Calls.Add("play " + string.Join(",", midi) + " " + vel.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        public void Release() => Calls.Add("release");
    }

    public sealed class FakeLayout : ILayoutService
    {
        public InstrumentPlace Place = InstrumentPlace.Seat;
        public bool Moving;
        public readonly List<(InstrumentKind, BringReason)> Brings = new List<(InstrumentKind, BringReason)>();
        public bool BringResult = true;
        public LayoutMode Mode { get; set; } = LayoutMode.All;
        public InstrumentKind Current { get; set; }
        public bool IsTurning => false;
        public bool FirstArrangeDone => true;
        public SeatFrame Seat => default;
        public void Set(LayoutMode mode, InstrumentKind instrument) { Mode = mode; Current = instrument; }
        public void Next(int direction) { }
        public void ArrangeAroundPlayer(bool force, bool movePiano = true) { }
        public void PlaceAroundPiano() { }
        public void RestoreHomes() { }
        public Pose Anchor => Pose.identity;
        public bool IsInScene(InstrumentKind instrument) => Place == InstrumentPlace.Seat;
        public bool Bring(InstrumentKind instrument, BringReason reason) { Brings.Add((instrument, reason)); return BringResult; }
        public InstrumentPlace PlaceOf(InstrumentKind instrument) => Place;
        public bool IsMoving(InstrumentKind instrument) => Moving;
    }

    public sealed class GenericStage : ILessonStage
    {
        public readonly FakeTargets Targets = new FakeTargets();
        public FakeAccompany Accompany;
        public readonly FakeLayout FakeLayout = new FakeLayout();
        public readonly InstrumentRegistry Reg;
        public bool Shown = true;

        public GenericStage(string countNoun = "notes")
        {
            Reg = InstrumentRegistry.Build(new[]
            {
                new InstrumentDescriptor
                {
                    Kind = InstrumentKind.Handpan, Rig = RigId.Handpan, Id = "handpan", Name = "Handpan", Carousel = 4, Traits = InstrumentTraits.Playable,
                    Seat = new SeatSpec(), Lessons = new LessonSpec { Travel = 0.30f, LeadSeconds = 1.6666667f, Shape = LessonBlockShape.Disc, CountNoun = countNoun, Pack = "handpan" },
                    Events = new[] { new EventTypeSpec(FakeTargets.Onset, EventRole.Onset, EventKey.Index), new EventTypeSpec("handpanMute", EventRole.Release, EventKey.Index) },
                },
            });
        }

        public IPianoApi Piano => null;
        public IDrumsApi Drums => null;
        public IHarpApi Harp => null;
        public bool IsShown(InstrumentKind instrument) => Shown;
        public Vector3 EyePosition => new Vector3(0f, 1.2f, 0f);
        public IInstrumentRegistry Registry => Reg;
        public ILessonTargets TargetsFor(InstrumentKind instrument) => instrument == InstrumentKind.Handpan ? Targets : null;
        public ILessonAccompany AccompanyFor(InstrumentKind instrument) => Accompany;
        public ILayoutService Layout => FakeLayout;
        public IAudioEngine Audio => null;
    }

    public class GenericTutorialTests
    {
        GenericStage _stage;
        Tutorial _t;
        EventBus _bus;
        readonly List<InstrumentSignalEvent> _signals = new List<InstrumentSignalEvent>();

        [SetUp]
        public void SetUp()
        {
            _stage = new GenericStage("strokes");
            _bus = new EventBus();
            _signals.Clear();
            _bus.Subscribe<InstrumentSignalEvent>(e => _signals.Add(e));
            _t = new Tutorial(null, _stage, _bus);
        }

        static LessonTarget T(int i) => new LessonTarget { i = i, label = "field " + i };

        static Lesson Lesson(params (float beat, LessonTarget[] targets)[] events)
        {
            var evs = new LessonEvent[events.Length];
            for (int k = 0; k < events.Length; k++) evs[k] = new LessonEvent { k = k, beat = events[k].beat, len = 1f, targets = events[k].targets };
            return new Lesson { id = "handpan-test", instrument = "handpan", title = "Test", bpm = 60, events = evs };
        }

        static Lesson Simple() => Lesson((0f, new[] { T(1) }), (1f, new[] { T(2) }), (2f, new[] { T(1), T(3) }), (4f, new[] { T(4) }));

        void Burn() { _t.Update(1.1f, null); _t.Update(1.1f, null); _t.Update(1.1f, null); }
        void Live(int i) => _t.OnPlay(new TakeEvent { type = FakeTargets.Onset, i = i, vel = 0.6f }, false);
        Vector3 BlockPos(int n) => _t.Matrices[n].GetColumn(3);

        [Test]
        public void BlocksFallAlongTheApproachAndLandOnTheLandingInTheCuePurple()
        {
            _t.Start(Simple(), LessonOptions.None);
            Assert.IsTrue(_t.Generic);
            Assert.That(_t.Shape, Is.EqualTo(BlockShape.Disc), "LessonSpec.Shape");
            Assert.That(_t.Lead, Is.EqualTo(1.6666667).Within(1e-6), "LessonSpec.LeadSeconds");
            Burn();
            Assert.That(_t.BlockCount, Is.EqualTo(1));
            Vector3 up = _stage.Targets.Up;
            Vector3 start = _stage.Targets.Landing(1) + up * (0.30f + Tutorial.GenericLift);
            Assert.That(Vector3.Distance(BlockPos(0), start), Is.LessThan(2e-3f), "it appears its travel out along the approach");
            Assert.That(((Vector3)_t.Matrices[0].GetColumn(0)).magnitude, Is.EqualTo(0.03f).Within(1e-5f), "a disc of the landing's width");
            Assert.That(Vector3.Dot(((Vector3)_t.Matrices[0].GetColumn(1)).normalized, up), Is.GreaterThan(0.9999f), "upright on the surface");
            Color c = _t.BlockColorSrgb(0);
            Assert.That(c.r, Is.EqualTo(LessonCue.Color.r).Within(0.01f)); Assert.That(c.g, Is.EqualTo(LessonCue.Color.g).Within(0.01f)); Assert.That(c.b, Is.EqualTo(LessonCue.Color.b).Within(0.01f));
            Assert.IsTrue(LessonCue.IsPurple(c), $"the cue is purple, never white ({c})");
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            Vector3 landed = _stage.Targets.Landing(1) + up * Tutorial.GenericLift;
            Assert.That(Vector3.Distance(BlockPos(0), landed), Is.LessThan(1e-4f), "it lands on TryGetLanding");
            Assert.That(_t.Hint, Is.EqualTo("Tap field 1"), "the instrument's own hint");
        }

        [Test]
        public void ATargetsTravelComesFromItsInstrument()
        {
            _t.Start(Lesson((0f, new[] { T(4) })), LessonOptions.None);
            Burn();
            Vector3 start = _stage.Targets.Landing(4) + _stage.Targets.Up * (0.15f + Tutorial.GenericLift);
            Assert.That(Vector3.Distance(BlockPos(0), start), Is.LessThan(2e-3f), "ILessonTargets.Travel shortened it to 0.15 m");
        }

        [Test]
        public void TheCueRisesToOneExactlyWhenItLandsAndIsOffAfterTheHit()
        {
            Lesson lesson = Simple();
            LessonTarget first = lesson.events[0].targets[0];
            _t.Start(lesson, LessonOptions.None);
            Burn();
            var ks = new List<float>();
            for (int i = 0; i < 200 && _t.State != TutorialState.Waiting; i++)
            {
                _stage.Targets.Cues.Clear();
                _t.Update(0.02f, null);
                foreach (var c in _stage.Targets.Cues) if (ReferenceEquals(c.t, first)) ks.Add(c.k);
            }
            Assert.That(ks.Count, Is.GreaterThan(10));
            for (int i = 1; i < ks.Count; i++) Assert.That(ks[i], Is.GreaterThanOrEqualTo(ks[i - 1]), "k only rises while it falls");
            Assert.That(ks[0], Is.LessThan(0.2f));
            Assert.That(ks[ks.Count - 1], Is.EqualTo(1f), "1 exactly when the block lands (waiting)");
            _stage.Targets.Cues.Clear();
            Live(1);
            Assert.That(_stage.Targets.Cues.Exists(c => ReferenceEquals(c.t, first) && c.k == 0f), "back to 0 at the hit, on the event's own target");
            _stage.Targets.Cues.Clear();
            _t.Update(0.02f, null);
            Assert.That(_stage.Targets.Cues.Exists(c => ReferenceEquals(c.t, first)), Is.False, "a hit target gets no more cue");
            Assert.That(ReferenceEquals(lesson.events[2].targets[0], first), Is.False, "the same field in a later event is another instance");
        }

        [Test]
        public void HitsComeFromMatchesOnLiveEventsOnlyAndReleasesAreIgnored()
        {
            _t.Start(Simple(), LessonOptions.None);
            Burn();
            _t.Update(5f, null);
            _t.OnPlay(new TakeEvent { type = FakeTargets.Onset, i = 1 }, true);
            Assert.That(_t.EventsDone, Is.EqualTo(0), "the deck's playback never plays the lesson");
            _t.OnPlay(new TakeEvent { type = "handpanMute", i = 1 }, false);
            _t.OnPlay(TakeEvent.PianoOn(60, 0.7f), false);
            Assert.That(_t.Mistakes, Is.EqualTo(0), "a release, another instrument: ignored");
            Live(0);
            Assert.That(_t.Mistakes, Is.EqualTo(1), "a wrong field in reach is a mistake");
            Assert.That(_t.WrongTime, Is.GreaterThan(0f));
            Live(1);
            Assert.That(_t.EventsDone, Is.EqualTo(1));
            Assert.That(_t.CurrentEvent, Is.EqualTo(1));
        }

        [Test]
        public void B2AReHitOfATargetAlreadyHitIsNoMistake()
        {
            _t.Start(Simple(), LessonOptions.None);
            Burn();
            _t.Update(5f, null); Live(1);
            _t.Update(5f, null); Live(2);
            _t.Update(5f, null);
            Assert.That(_t.CurrentEvent, Is.EqualTo(2), "the two-field chord waits");
            Live(1);
            Assert.That(_t.EventsDone, Is.EqualTo(3));
            Live(1);
            Assert.That(_t.Mistakes, Is.EqualTo(0), "B2: hitting field 1 again is no mistake");
            Live(3);
            Assert.That(_t.CurrentEvent, Is.EqualTo(3), "the chord is complete");
        }

        [Test]
        public void B4TheSummaryUsesTheInstrumentsCountNoun()
        {
            _t.Start(Lesson((0f, new[] { T(2) })), LessonOptions.None);
            Assert.That(_t.CountNoun, Is.EqualTo("strokes"));
            Burn();
            _t.Update(5f, null);
            Live(2);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Done));
            Assert.That(_t.Summary.Notes, Is.EqualTo(1));
            Assert.That(_t.CurrentEvent, Is.EqualTo(-1), "no event once done");
        }

        [Test]
        public void B6SunnyRedemosAfterEightSecondsAndAnyHitCountsAfterFifteen()
        {
            _t.Start(Simple(), LessonOptions.None);
            Burn();
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            for (int i = 0; i < 7; i++) _t.Update(1f, null);
            Assert.That(_signals, Is.Empty, "not before 8 s");
            _t.Update(1.05f, null);
            Assert.That(_signals.Count, Is.EqualTo(1), "the re-demo signal at 8 s");
            Assert.That(_signals[0].Kind, Is.EqualTo(InstrumentKind.Handpan));
            Assert.That(_signals[0].Signal, Is.EqualTo(Tutorial.RedemoSignal));
            Live(0);
            Assert.That(_t.Mistakes, Is.EqualTo(1), "before 15 s a wrong field is still a mistake");
            for (int i = 0; i < 7; i++) _t.Update(1f, null);
            Assert.That(_signals.Count, Is.EqualTo(1), "once per event");
            Assert.That(_t.WaitSeconds, Is.GreaterThanOrEqualTo(Tutorial.GiveUpAfter));
            Live(4);
            Assert.That(_t.EventsDone, Is.EqualTo(1), "after 15 s any hit on the instrument counts");
            Assert.That(_t.Mistakes, Is.EqualTo(1));
            Assert.That(_t.CurrentEvent, Is.EqualTo(1));
        }

        [Test]
        public void CurrentEventIsMinusOneWhenIdleAndFollowsTheLesson()
        {
            Assert.That(_t.CurrentEvent, Is.EqualTo(-1));
            Assert.That(((ITutorial)_t).CurrentEvent, Is.EqualTo(-1));
            _t.Start(Simple(), LessonOptions.None);
            Assert.That(_t.CurrentEvent, Is.EqualTo(0), "the next to land, from the countdown on");
            Burn(); _t.Update(5f, null); Live(1);
            Assert.That(_t.CurrentEvent, Is.EqualTo(1));
            _t.Stop();
            Assert.That(_t.CurrentEvent, Is.EqualTo(-1));
        }

        [Test]
        public void StartingBringsTheInstrumentAndTheCountdownWaitsForItsArrival()
        {
            _stage.FakeLayout.Place = InstrumentPlace.Display;
            _stage.FakeLayout.BringResult = true;
            _t.Start(Simple(), LessonOptions.None);
            Assert.That(_stage.FakeLayout.Brings, Is.EqualTo(new[] { (InstrumentKind.Handpan, BringReason.Lesson) }), "Bring(kind, Lesson) first");
            _stage.FakeLayout.Place = InstrumentPlace.Gliding;
            _stage.FakeLayout.Moving = true;
            Assert.That(_t.State, Is.EqualTo(TutorialState.Countdown));
            Assert.IsTrue(_t.AwaitingArrival);
            _t.Update(2f, null); _t.Update(2f, null);
            Assert.That(_t.Countdown, Is.EqualTo(3f), "the countdown waits for the glide");
            _t.OnMove(new InstrumentMoveEvent { Kind = InstrumentKind.Drums, Phase = MovePhase.Arrived });
            Assert.IsTrue(_t.AwaitingArrival, "another instrument's arrival");
            _stage.FakeLayout.Place = InstrumentPlace.Seat;
            _stage.FakeLayout.Moving = false;
            _t.OnMove(new InstrumentMoveEvent { Kind = InstrumentKind.Handpan, Phase = MovePhase.Arrived, Reason = BringReason.Lesson });
            Assert.IsFalse(_t.AwaitingArrival);
            _t.Update(1.1f, null);
            Assert.That(_t.Digit, Is.EqualTo(2), "now it counts");
            // Already at the seat: Bring is still asked first and the countdown runs at once.
            _stage.FakeLayout.Brings.Clear();
            _stage.FakeLayout.BringResult = false;
            _t.Start(Simple(), LessonOptions.None);
            Assert.That(_stage.FakeLayout.Brings.Count, Is.EqualTo(1));
            Assert.IsFalse(_t.AwaitingArrival);
            _t.Update(1.1f, null);
            Assert.That(_t.Digit, Is.EqualTo(2));
        }

        [Test]
        public void NoCountdownQuietMistakesAndForgivingWorkAndAPacksOptionsApply()
        {
            _t.Start(Simple(), LessonOptions.NoCountdown | LessonOptions.QuietMistakes);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing), "NoCountdown: it starts at once");
            _t.Update(5f, null);
            Live(0);
            Assert.That(_t.Mistakes, Is.EqualTo(0), "QuietMistakes: no count");
            Assert.That(_t.WrongTime, Is.EqualTo(0f), "and no flash");
            _t.Start(Simple(), LessonOptions.Forgiving);
            Burn(); _t.Update(5f, null);
            Live(3);
            Assert.That(_t.EventsDone, Is.EqualTo(1), "Forgiving: any hit plays the next one");
            Lesson pack = Simple();
            pack.options = new[] { "QuietMistakes", "NoCountdown", "NoSuchOption" };
            _t.Start(pack, LessonOptions.None);
            Assert.That(_t.Options, Is.EqualTo(LessonOptions.QuietMistakes | LessonOptions.NoCountdown), "the pack's own options");
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing));
        }

        [Test]
        public void TheAccompanimentPlaysOnItsBeatsThroughTheInstrument()
        {
            _stage.Accompany = new FakeAccompany();
            Lesson lesson = Simple();
            lesson.options = new[] { "Accompany" };
            lesson.accompany = new[]
            {
                new LessonChord { beat = 0f, len = 1f, midi = new[] { 48, 52, 55 }, vel = 0.3f },
                new LessonChord { beat = 1.5f, len = 2f, midi = new[] { 47, 50 }, vel = 0.3f },
            };
            _t.Start(lesson, LessonOptions.None);
            Burn();
            _t.Update(5f, null);
            Assert.That(_stage.Accompany.Calls, Is.Empty, "waiting on beat 0: the chord waits for the note");
            Live(1);
            Assert.That(_stage.Accompany.Calls, Is.EqualTo(new[] { "play 48,52,55 0.30" }), "with the hit");
            _t.Update(5f, null); Live(2);
            Assert.That(_t.ChordsPlayed, Is.EqualTo(1), "beat 1.5 is not reached yet");
            _t.Update(0.6f, null);
            Assert.That(_stage.Accompany.Calls, Is.EqualTo(new[] { "play 48,52,55 0.30", "release", "play 47,50 0.30" }), "on its beat, letting the last go");
            _t.Stop();
            Assert.That(_stage.Accompany.Calls[_stage.Accompany.Calls.Count - 1], Is.EqualTo("release"), "a stop lets it go");
        }

        [Test]
        public void TheGenericHotPathsAllocateNothing()
        {
            _stage.Targets.RecordCues = false;
            _t.Start(Simple(), LessonOptions.None);
            Burn();
            _t.Update(5f, null);
            TakeEvent wrong = new TakeEvent { type = FakeTargets.Onset, i = 0 };
            for (int i = 0; i < 4; i++) { _t.OnPlay(wrong, false); _t.Update(0.016f, null); _t.Compose(); }
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++) { _t.OnPlay(wrong, false); _t.Update(0.016f, null); _t.Compose(); }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(_t.BlockCount, Is.GreaterThan(0));
        }
    }
}
