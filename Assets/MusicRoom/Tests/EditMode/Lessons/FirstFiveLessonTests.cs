// The lessons' first-five-minutes parts (Docs/11-first-five/tutorials-audit.md B1 / B2 / B6, sunny-tour.md 2.5):
// drum targets toward the player and on the surface, a re-hit is no mistake, the first run's options (no countdown, the
// soft chords, quiet mistakes, any key plays the next note), the live target Sunny points at, the first run's song, and
// the remembered progress with the next lesson.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Lessons
{
    public class FirstFiveLessonTests
    {
        /// <summary>The last chord of the first run's song: 5.4 beats at 108 BPM.</summary>
        const float FinalChordSeconds = 5.4f * 60f / 108f;

        /// <summary>FakeStage with a recording accompaniment (the platform's ILessonAccompany path).</summary>
        sealed class AccompanyStage : ILessonStage
        {
            readonly FakeStage _s;
            public readonly FakeAccompany Accompany = new FakeAccompany();
            public AccompanyStage(FakeStage s) { _s = s; }
            public IPianoApi Piano => _s.Piano;
            public IDrumsApi Drums => _s.Drums;
            public IHarpApi Harp => _s.Harp;
            public Vector3 EyePosition => _s.EyePosition;
            public bool IsShown(InstrumentKind k) => _s.IsShown(k);
            public ILessonAccompany AccompanyFor(InstrumentKind instrument) => Accompany;
        }

        LessonLibrary _lib;
        FakeStage _stage;
        Tutorial _t;
        EventBus _bus;
        readonly List<TutorialHitEvent> _hits = new List<TutorialHitEvent>();

        [SetUp]
        public void SetUp()
        {
            _lib = LessonLibrary.Parse(Resources.Load<TextAsset>(LessonLibrary.ResourceName).text);
            _stage = new FakeStage();
            _bus = new EventBus();
            _hits.Clear();
            _bus.Subscribe<TutorialHitEvent>(e => _hits.Add(e));
            _t = new Tutorial(_lib, _stage, _bus);
        }

        [TearDown]
        public void TearDown() => _stage.Destroy();

        void Burn() { _t.Update(1.1f, null); _t.Update(1.1f, null); _t.Update(1.1f, null); }
        void Play(TakeEvent e) => _t.OnPlay(e, false);
        void Press(int midi) { Play(TakeEvent.PianoOn(midi, 0.7f)); Play(TakeEvent.PianoOff(midi)); }
        void Hit(string kind) => Play(TakeEvent.Drum(kind, 0.7f, 0.3f, "head", false));
        Vector3 BlockPos(int i) => _t.Matrices[i].GetColumn(3);

        // ---------------- B1: where a drum target lands ----------------

        [Test]
        public void DrumTargetsLandOffCentreTowardThePlayerOnTheSurface()
        {
            IReadOnlyList<DrumPieceInfo> pieces = _stage.FakeDrums.Pieces;
            int checkedPieces = 0;
            foreach (DrumPieceInfo p in pieces)
            {
                if (p.Transform == null || (p.Kind == "kick" && !p.Pad)) continue;   // the kick's target is the pad, never the shell
                Vector3 land = _t.DrumLanding(p);
                Vector3 eye = p.Transform.InverseTransformPoint(_stage.Eyes);
                Vector2 toward = new Vector2(eye.x, eye.z).normalized, off = new Vector2(land.x, land.z);
                if (p.Pad)
                {
                    Assert.That(off.magnitude, Is.LessThan(1e-5f), "the kick pad's target stays at its centre");
                    Assert.That(land.y, Is.EqualTo(Tutorial.DiscLift).Within(1e-6f));
                    continue;
                }
                bool hat = p.Kind == "hat";
                float frac = hat ? Tutorial.LandFractionHat : Tutorial.LandFraction;
                Assert.That(off.magnitude, Is.EqualTo(p.Radius * frac).Within(1e-4f), $"{p.Kind}: {frac} of the radius from the centre");
                Assert.That(Vector2.Dot(off.normalized, toward), Is.GreaterThan(0.999f), $"{p.Kind}: on the side facing the player");
                float lift = p.Type == DrumPieceType.Cymbal ? (hat ? Tutorial.HatLift : Tutorial.CymbalLift) : Tutorial.DiscLift;
                Assert.That(land.y, Is.EqualTo(lift).Within(1e-6f), $"{p.Kind}: on the surface (cymbals rise above their strike plane)");
                if (p.Type == DrumPieceType.Cymbal) Assert.That(off.magnitude, Is.GreaterThan(p.Radius * 0.22f), $"{p.Kind}: never on the bell (inside 0.22 r)");
                // Nearer the player than the centre was (the toms, crash and ride were at full stretch).
                Vector3 world = p.Transform.TransformPoint(land), centre = p.Transform.position;
                Assert.That(Vector3.Distance(world, _stage.Eyes), Is.LessThan(Vector3.Distance(centre, _stage.Eyes)), $"{p.Kind}: nearer");
                checkedPieces++;
            }
            Assert.That(checkedPieces, Is.GreaterThanOrEqualTo(7), "every drum and cymbal");
        }

        [Test]
        public void TheCrashTargetSitsInTheBowNotInsideTheCymbal()
        {
            var crash = new Lesson { id = "tmp-crash", instrument = "drums", title = "Crash", bpm = 90, events = new[] { new LessonEvent { beat = 0, len = 1, targets = new[] { new LessonTarget { kind = "crash", label = "Crash" } } } } };
            _t.Start(crash); Burn();
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            DrumPieceInfo p = _stage.FakeDrums.Piece("crash");
            Vector3 l = p.Transform.InverseTransformPoint(BlockPos(0));
            Assert.That(l.y, Is.EqualTo(Tutorial.CymbalLift).Within(1e-4f), "landed on the cymbal's surface, not 1 mm above its strike plane");
            Assert.That(new Vector2(l.x, l.z).magnitude, Is.EqualTo(p.Radius * Tutorial.LandFraction).Within(1e-3f));
            Assert.That(((Vector3)_t.Matrices[0].GetColumn(0)).magnitude, Is.LessThanOrEqualTo(p.Radius * (1f - Tutorial.LandFraction) + 1e-4f), "the disc stays on the cymbal");
            Assert.IsTrue(_t.TryGetLiveTarget(out Vector3 w, out Vector3 up, out InstrumentKind k, out _));
            Assert.That(k, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(Vector3.Distance(w, p.Transform.TransformPoint(_t.DrumLanding(p))), Is.LessThan(1e-4f), "Sunny points where the block lands");
            Assert.That(Vector3.Dot(up, p.Transform.up), Is.GreaterThan(0.999f));
        }

        // ---------------- B2: a re-hit is no mistake ----------------

        [Test]
        public void HittingAnAlreadyHitTargetOfTheSameEventIsNoMistake()
        {
            // drums-beat opens on hat + kick: a bouncing hand on the hat before the kick comes.
            _t.Start("drums-beat"); Burn(); _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            Hit("hat");
            Hit("hat");
            Hit("hat");
            Assert.That(_t.Mistakes, Is.EqualTo(0), "B2: the hat again is a bounce");
            Assert.That(_t.WrongTime, Is.EqualTo(0f), "no red flash");
            Hit("snare");
            Assert.That(_t.Mistakes, Is.EqualTo(1), "a target not in the event still is a mistake");
            Hit("kick");
            Assert.That(_t.CurrentEvent, Is.EqualTo(1), "the event completes");
        }

        [Test]
        public void TheLastEventsTargetStruckAgainAtOnceIsNoMistake()
        {
            _t.Start("piano-scale"); Burn(); _t.Update(5f, null);
            Press(60);                                   // C4 done: the next is D4
            Assert.That(_t.CurrentEvent, Is.EqualTo(1));
            _t.Update(0.05f, null);
            Press(60);                                   // a re-trigger 50 ms later
            Assert.That(_t.Mistakes, Is.EqualTo(0), "within 120 ms: a bounce of the last note");
            _t.Update(5f, null);
            Press(60);                                   // much later: a real wrong note
            Assert.That(_t.Mistakes, Is.EqualTo(1));
        }

        // ---------------- the first run's options ----------------

        [Test]
        public void NoCountdownStartsTheFirstBlockFallingAtOnce()
        {
            _t.Start("piano-ode", LessonOptions.NoCountdown);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing), "no 3-2-1");
            _t.Update(0.02f, null);
            Assert.That(_t.BlockCount, Is.EqualTo(1), "the first block is on its way");
            float lead = (float)_t.Lead;
            _t.Update(lead, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting), "it lands about 1.9 s in");
            Assert.That(lead, Is.LessThan(2.0f));
        }

        [Test]
        public void QuietMistakesSoundButNeitherCountNorFlashAndAreStillReported()
        {
            _t.Start("piano-ode", LessonOptions.NoCountdown | LessonOptions.QuietMistakes);
            _t.Update(3f, null);
            Press(62);
            Assert.That(_t.Mistakes, Is.EqualTo(0));
            Assert.That(_t.WrongTime, Is.EqualTo(0f));
            Assert.That(_hits.Count, Is.EqualTo(1));
            Assert.IsFalse(_hits[0].Hit, "the wrong key is reported (Sunny: Now try the glowing one)");
            Press(64);
            Assert.That(_hits[1].Hit, Is.True);
            Assert.That(_hits[1].EventIndex, Is.EqualTo(0));
        }

        [Test]
        public void ForgivingLetsAnyKeyPlayTheNextNote()
        {
            _t.Start("piano-ode", LessonOptions.NoCountdown | LessonOptions.QuietMistakes);
            _t.Update(3f, null);
            Press(72);
            Assert.That(_t.CurrentEvent, Is.EqualTo(0), "not forgiving yet");
            _t.Forgiving = true;
            Press(72);
            Assert.That(_t.CurrentEvent, Is.EqualTo(1), "any key plays the next note");
            Assert.That(_t.EventsDone, Is.EqualTo(1));
            _t.Forgiving = false;
            Assert.IsFalse(_t.Forgiving);
        }

        [Test]
        public void TheFirstRunsSongIsTheAnsweringPhraseWithSoftChordsThatFollowThePlayer()
        {
            Lesson first = LessonsFirstFive.LoadFirstRun();
            Assert.That(first, Is.Not.Null, "Lessons/Resources/lessons-firstrun.json");
            Assert.That(first.id, Is.EqualTo(LessonsFirstFive.FirstRunLessonId));
            Assert.That(_lib.Get(first.id), Is.Null, "never in the Lessons menu");
            Assert.That(first.events.Length, Is.EqualTo(15));
            var names = new List<string>();
            foreach (LessonEvent e in first.events) names.Add(e.targets[0].note);
            Assert.That(string.Join(" ", names), Is.EqualTo("E4 E4 F4 G4 G4 F4 E4 D4 C4 C4 D4 E4 D4 C4 C4"), "the answering phrase, ending home on C");
            Assert.That(first.bpm, Is.EqualTo(108f));
            Assert.That(Tutorial.ParseOptions(first.options), Is.EqualTo(LessonOptions.NoCountdown | LessonOptions.Accompany | LessonOptions.QuietMistakes));
            Assert.That(first.accompany.Length, Is.EqualTo(5));
            // The platform's accompaniment (Tutorial: the lesson's chords through the instrument's ILessonAccompany, else
            // the piano sound): here a recording accompaniment.
            var stage = new AccompanyStage(_stage);
            _t = new Tutorial(_lib, stage, _bus);
            _t.AddHidden(first);
            _t.Start(first.id, LessonOptions.Hidden);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing), "its own options: no countdown");
            int guard = 0;
            while (_t.State != TutorialState.Done && guard++ < 200)
            {
                _t.Update(0.5f, null);
                if (_t.State == TutorialState.Waiting) Press(first.events[_t.CurrentEvent].targets[0].midi);
            }
            Assert.That(_t.State, Is.EqualTo(TutorialState.Done));
            var chords = new List<string>();
            int released = 0;
            foreach (string c in stage.Accompany.Calls) { if (c == "release") released++; else chords.Add(c); }
            Assert.That(chords, Is.EqualTo(new[] { "play 48,52,55 0.28", "play 47,50,55 0.28", "play 48,52,55 0.28", "play 47,50,55 0.28", "play 36,48,52,55 0.28" }),
                "C G C G C, soft, each when the note on its beat is played");
            Assert.That(released, Is.EqualTo(4), "each chord lets the last one go");
            Assert.That(_t.ChordsPlayed, Is.EqualTo(5));
            _t.Update(FinalChordSeconds - 0.1f, null);
            Assert.That(stage.Accompany.Calls.FindAll(c => c == "release").Count, Is.EqualTo(4), "the last chord rings on (its written 5.4 beats)");
            _t.Update(0.2f, null);
            Assert.That(stage.Accompany.Calls.FindAll(c => c == "release").Count, Is.EqualTo(5), "then lets go");
            Assert.That(_t.Summary.Notes, Is.EqualTo(15));
        }

        [Test]
        public void TheLiveTargetIsTheKeysFrontInFrontOfItsBlock()
        {
            _t.Start("piano-ode", LessonOptions.NoCountdown);
            _t.Update(0.1f, null);
            Assert.IsTrue(_t.TryGetLiveTarget(out Vector3 w, out Vector3 up, out InstrumentKind k, out int midi));
            Assert.That(k, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(midi, Is.EqualTo(64), "E4 first");
            Assert.IsTrue(_stage.FakePiano.TryGetKey(64, out PianoKeyInfo key));
            Vector3 l = _stage.FakePiano.Root.InverseTransformPoint(w);
            Assert.That(l.x, Is.EqualTo(key.X).Within(1e-5f));
            Assert.That(l.z, Is.EqualTo(Tutorial.KeyFrontPress).Within(1e-5f), "the front 2 cm, in front of the block");
            Assert.That(Vector3.Dot(up, _stage.FakePiano.Root.up), Is.GreaterThan(0.999f));
            _t.Stop();
            Assert.IsFalse(_t.TryGetLiveTarget(out _, out _, out _, out _));
        }

        // ---------------- progress and the next lesson ----------------

        [Test]
        public void FinishedLessonsAreRememberedWithTheirFewestMistakes()
        {
            var store = new MemorySettingsStore();
            var p = new LessonProgress(new AppSettings(store), _lib);
            Assert.IsFalse(p.IsDone("piano-ode"));
            Assert.That(p.BestMistakes("piano-ode"), Is.EqualTo(-1));
            p.Record("piano-ode", 3);
            p.Record("piano-ode", 5);
            p.Record("drums-first", 0);
            Assert.IsTrue(p.IsDone("piano-ode"));
            Assert.That(p.BestMistakes("piano-ode"), Is.EqualTo(3), "the fewest stays");
            p.Record("piano-ode", 1);
            Assert.That(p.BestMistakes("piano-ode"), Is.EqualTo(1));
            Assert.That(store.Saves, Is.GreaterThanOrEqualTo(2), "saved at once");
            var again = new LessonProgress(new AppSettings(store), _lib);
            Assert.IsTrue(again.IsDone("piano-ode"), "remembered on the next launch");
            Assert.IsTrue(again.IsDone("drums-first"));
            Assert.That(again.BestMistakes("piano-ode"), Is.EqualTo(1));
            Assert.That(again.DoneCount, Is.EqualTo(2));
        }

        [Test]
        public void TheNextLessonIsTheNextOfTheSameInstrumentInTheMenusOrder()
        {
            var p = new LessonProgress(new AppSettings(new MemorySettingsStore()), _lib);
            Assert.That(p.NextLessonId("piano-ode"), Is.EqualTo("piano-mary"));
            Assert.That(p.NextLessonId("piano-mary"), Is.EqualTo("piano-twinkle"));
            Assert.That(p.NextLessonId("drums-first"), Is.EqualTo("drums-hat"));
            Assert.That(p.NextLessonId("harp-ode"), Is.EqualTo("harp-twinkle"));
            Assert.That(p.NextLessonId("piano-elise"), Is.Null, "the last piano lesson has none");
            Assert.That(p.NextLessonId("drums-disco"), Is.Null);
            Assert.That(p.NextLessonId("harp-silent"), Is.Null);
            Assert.That(p.NextLessonId("nope"), Is.Null);
        }
    }
}
