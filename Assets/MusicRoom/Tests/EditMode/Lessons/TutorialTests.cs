// Tutorial state machine, timing and block poses (spec 08 sections 2 and 9; the tutorial reference checks, which
// drive tutorial.update(dt) and recorder plays synchronously).
// Plays are fed through OnPlay exactly as the RecorderEvent stream delivers them; the stage is FakeStage (instruments on
// posed transforms with the foundation's key / piece / string tables).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Lessons
{
    public class TutorialTests
    {
        LessonLibrary _lib;
        FakeStage _stage;
        Tutorial _t;
        EventBus _bus;
        List<TutorialEvent> _events;
        HoverGlowPool _glow;

        [SetUp]
        public void SetUp()
        {
            _lib = LessonLibrary.Parse(Resources.Load<TextAsset>(LessonLibrary.ResourceName).text);
            _stage = new FakeStage();
            _bus = new EventBus();
            _events = new List<TutorialEvent>();
            _bus.Subscribe<TutorialEvent>(e => _events.Add(e));
            _t = new Tutorial(_lib, _stage, _bus);
            _glow = new HoverGlowPool(new MaterialLibrary(), null, headless: true);
        }

        [TearDown]
        public void TearDown() => _stage.Destroy();

        void Burn() { _t.Update(1.1f, null); _t.Update(1.1f, null); _t.Update(1.1f, null); }   // tutorial-check burnCountdown
        void Play(TakeEvent e, bool playback = false) => _t.OnPlay(e, playback);
        void Press(int midi) { Play(TakeEvent.PianoOn(midi, 0.7f)); Play(TakeEvent.PianoOff(midi)); }   // pressKey + releaseKey
        void Hit(string kind) => Play(TakeEvent.Drum(kind, 0.7f, 0.3f, "head", false));
        void Pluck(int i) => Play(TakeEvent.Harp(i, 0.7f, 0.35f, 0f, "through"));
        Vector3 BlockPos(int i) => _t.Matrices[i].GetColumn(3);
        static float ScaleX(Matrix4x4 m) => ((Vector3)m.GetColumn(0)).magnitude;
        static float ScaleY(Matrix4x4 m) => ((Vector3)m.GetColumn(1)).magnitude;
        Transform PianoRoot => _stage.FakePiano.Root;
        int CurMidi(string id) => _lib.Get(id).events[_t.CurrentEvent].targets[0].midi;

        // ---------------- piano: C major scale ----------------

        [Test]
        public void CountdownShowsThreeTwoOneThenPlaysWithTheFirstBlock()
        {
            _t.Start("piano-scale");
            Assert.That(_t.State, Is.EqualTo(TutorialState.Countdown));
            Assert.That(_t.Digit, Is.EqualTo(3));
            Assert.That(_t.Countdown, Is.EqualTo(3f).Within(1e-6f));
            Assert.That(_t.Hint, Is.EqualTo("Play C4"));
            Assert.That(_t.Instrument, Is.EqualTo(InstrumentKind.Piano));
            Assert.That(_t.EventsTotal, Is.EqualTo(15));
            _t.Update(1.1f, null); Assert.That(_t.Digit, Is.EqualTo(2), "digitCountsDown");
            _t.Update(1.1f, null); Assert.That(_t.Digit, Is.EqualTo(1), "digitOne");
            Assert.That(_t.BlockCount, Is.EqualTo(0), "no blocks during the countdown");
            _t.Update(1.1f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing), "playingAfterCountdown");
            Assert.That(_t.LessonTime, Is.EqualTo(0.0), "t = 0 when the countdown ends");
            Assert.That(_t.BlockCount, Is.EqualTo(1), "firstBlockSpawned (spawned in the same frame)");
        }

        [Test]
        public void BlockStartsTravelAboveItsKeyAndFallsAt18CmPerSecond()
        {
            _t.Start("piano-scale");
            Burn();
            Assert.IsTrue(_stage.FakePiano.TryGetKey(60, out PianoKeyInfo c4));
            Vector3 l0 = PianoRoot.InverseTransformPoint(BlockPos(0));
            Assert.That(l0.y, Is.EqualTo(0.35f + Tutorial.KeyLift).Within(1e-4f), "blockStartsHigh: TRAVEL .35 over the key top");
            Assert.That(l0.x, Is.EqualTo(c4.X).Within(1e-5f), "blockXMatchesKey");
            Assert.That(l0.z, Is.EqualTo(Tutorial.KeyZWhite).Within(1e-5f), "white key: 5 cm behind the front");
            Vector3 p0 = BlockPos(0);
            _t.Update(0.2f, null); Vector3 p1 = BlockPos(0);
            _t.Update(0.2f, null); Vector3 p2 = BlockPos(0);
            Assert.That(p1.y, Is.LessThan(p0.y)); Assert.That(p2.y, Is.LessThan(p1.y));
            Assert.That((p0.y - p2.y) / 0.4f, Is.EqualTo(0.18f).Within(0.005f), "blockSpeedRight");
            // Shrunken blocks: 60 % of the key's width; height encodes the length (a quarter at 84 bpm), capped.
            Matrix4x4 m = _t.Matrices[0];
            Assert.That(ScaleX(m), Is.EqualTo(FakePiano.KeyW * 0.6f).Within(1e-6f), "blockWidth60pc");
            float quarter = Mathf.Clamp(1f * (60f / 84f) * 0.18f * 0.19f, 0.012f, 0.06f);
            Assert.That(ScaleY(m), Is.EqualTo(quarter).Within(1e-6f), "blockHeightEncodesLength");
            Assert.That(ScaleY(m), Is.LessThanOrEqualTo(0.025f).And.GreaterThanOrEqualTo(0.012f), "quarterUnder25mm");
            Assert.That(((Vector3)m.GetColumn(2)).magnitude, Is.EqualTo(0.03f).Within(1e-6f), "0.03 deep");
        }

        [Test]
        public void WaitsAtTheFirstNoteOnItsKeyWithAGlowAndAFrozenClock()
        {
            _t.Start("piano-scale");
            Burn();
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting), "waitsAtFirstNote");
            Assert.That(_t.CurrentEvent, Is.EqualTo(0));
            Assert.That(_t.LessonTime, Is.EqualTo(_t.DueOf(0)).Within(1e-9));
            Assert.That(_t.DueOf(0), Is.EqualTo(1.9444).Within(1e-3), "spec 08 section 9: due0 = 1.9444 s");
            Assert.That(_t.Hint, Is.EqualTo("Play C4"), "hintSaysPlayC4");
            Vector3 l = PianoRoot.InverseTransformPoint(BlockPos(0));
            Assert.That(l.y, Is.EqualTo(Tutorial.KeyLift).Within(1e-5f), "blockLandsOnKeyTop (pivot on its bottom face)");
            Assert.That(ScaleX(_t.Matrices[0]), Is.GreaterThanOrEqualTo(FakePiano.KeyW * 0.6f - 1e-6f), "the live block pulses 1 .. 1.08");
            _t.Update(0.016f, _glow);
            Assert.That(_glow.Pending, Is.EqualTo(1), "glowWhileWaiting");
            HoverGlowSpec g = _glow.Spec(0);
            _stage.FakePiano.TryGetKey(60, out PianoKeyInfo c4);
            Assert.That(Vector3.Distance(g.Position, PianoRoot.TransformPoint(c4.X, 0.001f, Tutorial.KeyZWhite)), Is.LessThan(1e-4f));
            Assert.That(g.Strength, Is.GreaterThanOrEqualTo(0.6f).And.LessThanOrEqualTo(1f));
            Assert.That(g.Length, Is.EqualTo(0.07f)); Assert.That(g.Width, Is.EqualTo(0.024f), "white key glow");
            Assert.That(Vector3.Dot(g.Normal, PianoRoot.up), Is.GreaterThan(0.9999f));
            double t0 = _t.LessonTime, e0 = _t.Elapsed;
            _t.Update(1f, null);
            Assert.That(_t.LessonTime, Is.EqualTo(t0), "clockFrozenWhileWaiting");
            Assert.That(_t.Elapsed, Is.EqualTo(e0 + 1).Within(1e-6));
        }

        [Test]
        public void OtherInstrumentsReleasesAndHatTogglesAreNotMistakes()
        {
            _t.Start("piano-scale"); Burn(); _t.Update(5f, null);
            Pluck(0);
            Play(TakeEvent.Hat(true)); Play(TakeEvent.Hat(false));
            Hit("snare");
            Play(TakeEvent.PianoOff(62));
            Play(TakeEvent.DrumChoke("crash"));
            Assert.That(_t.Mistakes, Is.EqualTo(0), "otherInstrumentNotMistake");
            Assert.That(_t.EventsDone, Is.EqualTo(0));
        }

        [Test]
        public void ScaleLessonWrongNoteFlashesRedRightNoteGreenThenTheSummary()
        {
            _t.Start("piano-scale"); Burn(); _t.Update(5f, null);
            Press(62);
            Assert.That(_t.Mistakes, Is.EqualTo(1), "wrongKeyCountsMistake");
            Assert.That(_t.CurrentEvent, Is.EqualTo(0)); Assert.That(_t.EventsDone, Is.EqualTo(0));
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            Assert.That(_t.WrongTime, Is.EqualTo(0.3f).Within(1e-6f), "WRONG_FLASH .3 s");
            _t.Update(0.016f, null);
            // First five: the blocks are pale and warm (the earlier cyan / green / red were glowing UI).
            Color red = _t.BlockColorSrgb(0);
            AssertColor(red, Tutorial.WrongColor, "wrongFlashes muted coral #e08c7c (was red #ff4646)");
            _t.Update(0.5f, null);
            Assert.That(Dist(_t.BlockColorSrgb(0), Tutorial.WrongColor), Is.GreaterThan(0.1f), "the flash ends");
            Press(60);
            Assert.That(_t.EventsDone, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(1));
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing)); Assert.That(_t.Hint, Is.EqualTo("Play D4"), "rightKeyAdvances");
            _t.Update(0.016f, null);
            Color green = _t.BlockColorSrgb(0);
            AssertColor(green, Tutorial.HitColor, "a hit turns sage #b8d8a0 (was green #46ff7a)");
            float hitScale = ScaleY(_t.Matrices[0]);
            _t.Update(0.15f, null);
            Assert.That(ScaleY(_t.Matrices[0]), Is.LessThan(hitScale), "hitShrinks");
            Assert.IsTrue(_t.IsBlockUsed(0));
            _t.Update(0.2f, null);
            Assert.IsFalse(_t.IsBlockUsed(0), "hitBlockFreed after HIT_FADE .3 s");
            int guard = 0;
            while (_t.State != TutorialState.Done && guard++ < 400)
            {
                _t.Update(0.5f, null);
                if (_t.State == TutorialState.Waiting) Press(CurMidi("piano-scale"));
            }
            Assert.That(_t.State, Is.EqualTo(TutorialState.Done), "scaleDone");
            Assert.IsTrue(_t.HasSummary);
            Assert.That(_t.Summary.Notes, Is.EqualTo(15)); Assert.That(_t.Summary.Mistakes, Is.EqualTo(1));
            Assert.That(_t.Summary.Seconds, Is.GreaterThan(5f)); Assert.That(_t.EventsDone, Is.EqualTo(15), "summaryRight");
            Assert.That(_t.Progress, Is.EqualTo(1f));
            Assert.That(_t.Hint, Is.EqualTo(""));
            _t.Stop();
            Assert.That(_t.State, Is.EqualTo(TutorialState.Idle)); Assert.That(_t.BlockCount, Is.EqualTo(0)); Assert.That(_t.ActiveBlocks, Is.EqualTo(0), "stopClearsBlocks");
            Assert.That(_t.LessonId, Is.EqualTo("piano-scale"), "the lesson stays named after Stop");
        }

        [Test]
        public void SharpsAreNamedInEnglishWithTheOctave()
        {
            var sharp = new Lesson { id = "tmp-sharp", instrument = "piano", title = "Sharp", bpm = 90, events = new[] { new LessonEvent { beat = 0, len = 1, targets = new[] { new LessonTarget { midi = 61 } } } } };
            _t.Start(sharp); Burn();
            Assert.That(_t.Hint, Is.EqualTo("Play C#4"), "sharpHintEnglish");
            Vector3 l = PianoRoot.InverseTransformPoint(BlockPos(0));
            Assert.That(l.z, Is.EqualTo(Tutorial.KeyZBlack).Within(1e-5f), "black key: 7.5 cm behind the white fronts");
            Assert.That(l.y, Is.EqualTo(FakePiano.KeyBTOP + 0.35f + Tutorial.KeyLift).Within(1e-4f), "black key top = BTOP");
            Assert.That(ScaleX(_t.Matrices[0]), Is.EqualTo(FakePiano.KeyBW * 0.6f).Within(1e-6f));
        }

        // spec 08 section 9 timing: a correct note at due - .14 hits; at due - .16 it is neither hit nor mistake.
        [Test]
        public void ACorrectNoteAt140msBeforeItsDueHits()
        {
            _t.Start("piano-scale"); Burn();
            _t.Update((float)(_t.DueOf(0) - 0.14), null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Playing));
            Press(60);
            Assert.That(_t.EventsDone, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(1)); Assert.That(_t.Mistakes, Is.EqualTo(0));
        }

        [Test]
        public void ANoteAt160msBeforeItsDueIsNeitherHitNorMistake()
        {
            _t.Start("piano-scale"); Burn();
            _t.Update((float)(_t.DueOf(0) - 0.16), null);
            Press(60);
            Press(62);
            Assert.That(_t.EventsDone, Is.EqualTo(0)); Assert.That(_t.Mistakes, Is.EqualTo(0)); Assert.That(_t.CurrentEvent, Is.EqualTo(0));
        }

        [Test]
        public void EarlyPlaysCountWithinTheWindowAndTooEarlyOnesAreIgnored()
        {
            _t.Start("piano-twinkle"); Burn();
            _t.Update((float)(_t.DueOf(0) - 0.1), null);
            Press(60);
            Assert.That(_t.EventsDone, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(1)); Assert.That(_t.State, Is.EqualTo(TutorialState.Playing), "earlyPlayCounts");
            Press(60);
            Assert.That(_t.EventsDone, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(1)); Assert.That(_t.Mistakes, Is.EqualTo(0), "tooEarlyIgnored");
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting)); Assert.That(_t.CurrentEvent, Is.EqualTo(1), "waitsAtSecond");
        }

        [Test]
        public void AWrongKeyJustBeforeTheBlockLandsIsAMistake()
        {
            _t.Start("piano-scale"); Burn();
            _t.Update((float)(_t.Lead - 0.1), null);
            Press(62);
            Assert.That(_t.Mistakes, Is.EqualTo(1)); Assert.That(_t.State, Is.EqualTo(TutorialState.Playing)); Assert.That(_t.CurrentEvent, Is.EqualTo(0), "earlyWrongCounts");
        }

        [Test]
        public void TapePlaybackDoesNotPlayTheLesson()
        {
            _t.Start("piano-scale"); Burn(); _t.Update(5f, null);
            Play(TakeEvent.PianoOn(60, 0.8f), playback: true);
            Play(TakeEvent.PianoOn(62, 0.8f), playback: true);
            Assert.That(_t.EventsDone, Is.EqualTo(0)); Assert.That(_t.Mistakes, Is.EqualTo(0), "playbackDoesNotPlayLesson");
        }

        [Test]
        public void DuesFollowLeadPlusBeatTimesSecondsPerBeatForEveryLesson()
        {
            foreach (Lesson l in _lib.Lessons)
            {
                _t.Start(l);
                double lead = Tutorial.TravelFor(Tutorial.KindOf(l.instrument)) / Tutorial.Speed;
                Assert.That(_t.Lead, Is.EqualTo(lead).Within(1e-12), l.id);
                Assert.That(_t.Lead, Is.EqualTo(l.leadSeconds).Within(1e-5), l.id + " lead vs JSON");
                for (int k = 0; k < l.events.Length; k++)
                    Assert.That(_t.DueOf(k), Is.EqualTo(l.events[k].due).Within(1e-4), $"{l.id} event {k} due vs the lesson export");
            }
        }

        // Headset feedback: "which key to press" was white; the cue is purple (LessonCue) on its way, while it waits (with
        // its pulse) and in the glow held on the waiting key. Hit and wrong keep their own colours (the test above).
        [Test]
        public void TheNextKeyCueIsPurpleOnTheBlocksAndTheGlow()
        {
            Assert.IsTrue(LessonCue.IsPurple(LessonCue.Color) && LessonCue.IsPurple(LessonCue.Bright), "the cue's two ends are purple");
            Assert.That(Dist(LessonCue.Color, new Color(0.62f, 0.30f, 1f)), Is.LessThan(1e-4f), "sRGB #9e4dff");
            Assert.IsFalse(LessonCue.IsPurple(Color.white), "white is not the cue");
            Assert.IsFalse(LessonCue.IsPurple(new Color32(0xf3, 0xea, 0xd8, 0xff)), "nor the old cream");
            Assert.That(Tutorial.Near, Is.EqualTo(LessonCue.Color)); Assert.That(Tutorial.WarmNear, Is.EqualTo(LessonCue.Color));
            _t.Start("piano-scale"); Burn();
            Assert.That(_t.BlockCount, Is.GreaterThan(0));
            for (int i = 0; i < _t.BlockCount; i++)
            {
                Color c = _t.BlockColorSrgb(i);
                Assert.IsTrue(LessonCue.IsPurple(c), $"block {i} on its way is purple ({c})");
                Assert.That(_t.Colors[i].w, Is.EqualTo(Tutorial.Opacity), "opaque enough to read purple over an ivory key");
            }
            AssertColor(_t.BlockColorSrgb(0), LessonCue.Color, "a falling block is the cue's violet");
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            for (int f = 0; f < 30; f++)
            {
                _t.Update(0.016f, _glow);
                Color live = _t.BlockColorSrgb(0);
                Assert.IsTrue(LessonCue.IsPurple(live), $"the waiting block pulses in purple, never white (frame {f}: {live})");
                Assert.That(_glow.Pending, Is.EqualTo(1));
                Assert.That(_glow.Spec(0).Tint, Is.EqualTo(LessonCue.Color), "the glow on the key is the same purple");
                _glow.Update(0f);
            }
            _t.Start("drums-beat"); Burn(); _t.Update(5f, null);
            _t.Update(0.016f, _glow);
            Assert.That(_glow.Pending, Is.GreaterThan(0));
            for (int i = 0; i < _glow.Pending; i++) Assert.That(_glow.Spec(i).Tint, Is.EqualTo(LessonCue.Color), "drum glows too");
            for (int i = 0; i < _t.BlockCount; i++) Assert.IsTrue(LessonCue.IsPurple(_t.BlockColorSrgb(i)), "drum discs too");
            _glow.Update(0f);
        }

        static float Dist(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        static void AssertColor(Color got, Color want, string what) =>
            Assert.That(Dist(got, want), Is.LessThan(0.02f), $"{what}: {got} vs {want}");

        // ---------------- drums: Basic beat ----------------

        [Test]
        public void DrumDiscsSitOverTheHatAndTheKickPadNeverTheShell()
        {
            _t.Start("drums-beat"); Burn();
            Assert.That(_t.BlockCount, Is.EqualTo(2), "drumDiscsSpawned (hat + kick)");
            Assert.That(_t.Shape, Is.EqualTo(BlockShape.Disc));
            DrumPieceInfo hat = _stage.FakeDrums.Piece("hat"), pad = _stage.FakeDrums.Piece("kick", pad: true), shell = _stage.FakeDrums.Piece("kick");
            int onHat = 0, onPad = 0, onShell = 0;
            // First five (tutorials-audit.md B1): the hi-hat's disc lands 0.45 r from its centre toward the player, on the
            // cymbal's top (not buried at the centre, not on the bell); the kick pad's stays at its centre.
            Vector3 eyeOnHat = hat.Transform.InverseTransformPoint(_stage.Eyes);
            Vector2 towardEye = new Vector2(eyeOnHat.x, eyeOnHat.z).normalized;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = BlockPos(i);
                Vector3 lh = hat.Transform.InverseTransformPoint(p), lp = pad.Transform.InverseTransformPoint(p), ls = shell.Transform.InverseTransformPoint(p);
                Vector2 off = new Vector2(lh.x, lh.z);
                if (Mathf.Abs(off.magnitude - hat.Radius * Tutorial.LandFractionHat) < 1e-3f)
                {
                    onHat++;
                    Assert.That(Vector2.Dot(off.normalized, towardEye), Is.GreaterThan(0.999f), "toward the player");
                    Assert.That(lh.y, Is.EqualTo(0.35f + Tutorial.HatLift).Within(1e-4f), "on the hi-hat's top");
                    Assert.That(ScaleX(_t.Matrices[i]), Is.EqualTo(Mathf.Min(hat.Radius * Tutorial.LandDiscFraction, 0.06f)).Within(1e-6f));
                }
                if (Mathf.Abs(lp.x) < 1e-4f && Mathf.Abs(lp.z) < 1e-4f) { onPad++; Assert.That(ScaleX(_t.Matrices[i]), Is.EqualTo(Mathf.Min(pad.Radius * 0.475f, 0.06f)).Within(1e-6f)); }
                if (Mathf.Abs(ls.x) < 1e-3f && Mathf.Abs(ls.z) < 1e-3f) onShell++;
                Assert.That(ScaleY(_t.Matrices[i]), Is.EqualTo(0.01f).Within(1e-6f), "discs are 1 cm thick");
                Assert.That(_t.Colors[i].w, Is.EqualTo(Tutorial.DiscOpacity), "drum discs are thinner in colour (.5)");
            }
            Assert.That(onHat, Is.EqualTo(1)); Assert.That(onPad, Is.EqualTo(1), "discOverHatAndPad");
            Assert.That(onShell, Is.EqualTo(0), "noDiscOnKickShell");
        }

        [Test]
        public void DrumLessonWaitsGlowsAndCountsKickChordAndMistakes()
        {
            _t.Start("drums-beat"); Burn();
            _t.Update(5f, null);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting)); Assert.That(_t.Hint, Is.EqualTo("Hi-hat + Kick"), "drumsWaitFirst");
            _t.Update(0.016f, _glow);
            Assert.That(_glow.Pending, Is.EqualTo(2), "two live discs, two glows");
            DrumPieceInfo hat = _stage.FakeDrums.Piece("hat"), pad = _stage.FakeDrums.Piece("kick", pad: true);
            Vector3 glowLift = new Vector3(0f, Tutorial.DrumGlowLift - Tutorial.DiscLift, 0f);
            for (int i = 0; i < 2; i++)
            {
                HoverGlowSpec g = _glow.Spec(i);
                // B1: the glow follows the disc to its landing point.
                bool atHat = Vector3.Distance(g.Position, hat.Transform.TransformPoint(_t.DrumLanding(hat) + glowLift)) < 1e-4f;
                bool atPad = Vector3.Distance(g.Position, pad.Transform.TransformPoint(0f, 0.002f, 0f)) < 1e-4f;
                Assert.IsTrue(atHat || atPad);
                Assert.That(g.Length, Is.EqualTo(Mathf.Min(0.24f, atHat ? hat.Radius * Tutorial.LandGlowPerRadius : pad.Radius * 1.6f)).Within(1e-6f), "the glow stays on the head");
                Assert.That(g.Width, Is.EqualTo(g.Length));
                Assert.That(g.Axis, Is.EqualTo(Vector3.zero), "no axis: the pool picks one");
            }
            Hit("kick");
            Assert.That(_t.EventsDone, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(0)); Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting), "kickPadCounts");
            Hit("hat");
            Assert.That(_t.EventsDone, Is.EqualTo(2)); Assert.That(_t.CurrentEvent, Is.EqualTo(1)); Assert.That(_t.State, Is.EqualTo(TutorialState.Playing), "chordCompletes");
            _t.Update(5f, null);
            Hit("snare");
            Assert.That(_t.Mistakes, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(1), "wrongDrumMistake");
            Hit("hat");
            Assert.That(_t.CurrentEvent, Is.EqualTo(2), "hatAdvances");
        }

        [Test]
        public void TheKickShellCountsForAKickTargetBecausePlaysMatchByKind()
        {
            _t.Start("drums-beat"); Burn();
            int guard = 0;
            while (_t.CurrentEvent < 4 && guard++ < 50)
            {
                _t.Update(5f, null);
                foreach (LessonTarget t in _lib.Get("drums-beat").events[_t.CurrentEvent].targets) Hit(t.kind);
            }
            _t.Update(5f, null);
            Assert.That(_t.CurrentEvent, Is.EqualTo(4)); Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting), "atSecondKick");
            LessonTarget[] targets = _lib.Get("drums-beat").events[4].targets;
            int k = System.Array.FindIndex(targets, x => x.kind == "kick");
            Hit("kick");   // the drums record the shell as kind "kick" too
            Assert.IsTrue(_t.TargetHit(4, k), "kickShellCounts");
        }

        // ---------------- harp: C major arpeggio ----------------

        [Test]
        public void HarpCapsuleComesInAlongTheSightLineAndLandsOnTheString()
        {
            _t.Start("harp-arpeggio"); Burn();
            Assert.That(_t.BlockCount, Is.EqualTo(1), "capsuleSpawned");
            Assert.That(_t.Shape, Is.EqualTo(BlockShape.Capsule));
            IHarpApi harp = _stage.FakeHarp;
            Transform hr = harp.Root;
            Vector3 p = harp.PlayPoint(7);
            Vector3 eye = hr.InverseTransformPoint(_stage.Eyes);
            Vector3 sight = eye - p;
            float want = Mathf.Min(0.3f, sight.magnitude - 0.25f);
            Vector3 cl0 = hr.InverseTransformPoint(BlockPos(0));
            Vector3 off = cl0 - p;
            Assert.That(off.magnitude, Is.EqualTo(want).Within(0.002f), "capsuleStartsOut: TRAVEL .30 (or 25 cm short of the eyes)");
            Assert.That(Vector3.Dot(off.normalized, sight.normalized), Is.GreaterThan(0.9999f), "along the line of sight");
            Assert.That(Mathf.Sign(cl0.z), Is.EqualTo(Mathf.Sign(eye.z)), "capsuleFromPlayerSide");
            Vector3 along = (harp.StringEnd(7) - harp.StringStart(7)).normalized;
            Vector3 up = hr.InverseTransformDirection(((Vector3)_t.Matrices[0].GetColumn(1)).normalized);
            Assert.That(Vector3.Dot(up, along), Is.GreaterThan(0.9999f), "the capsule lies along its string");
            Assert.That(ScaleY(_t.Matrices[0]), Is.EqualTo(1f).Within(1e-5f), "unit scale: the mesh is the 6 mm x 28 mm capsule");
            _t.Update(5f, null);
            Vector3 cl1 = hr.InverseTransformPoint(BlockPos(0));
            Assert.That(Vector3.Distance(cl1, p), Is.LessThan(0.002f), "capsuleReachesString");
            Assert.That(_t.State, Is.EqualTo(TutorialState.Waiting));
            Assert.That(_t.Hint, Is.EqualTo("Pluck C4 (red)"), "harpHintNamesString");
        }

        [Test]
        public void HarpLessonGlowsHighlightsCountsAndFinishes()
        {
            _t.Start("harp-arpeggio"); Burn(); _t.Update(5f, null);
            _t.Update(0.016f, _glow);
            Assert.That(_glow.Pending, Is.EqualTo(1));
            HoverGlowSpec g = _glow.Spec(0);
            IHarpApi harp = _stage.FakeHarp;
            Assert.That(Vector3.Distance(g.Position, harp.Root.TransformPoint(harp.PlayPoint(7))), Is.LessThan(1e-4f), "harpGlowOnString");
            Assert.That(g.Length, Is.EqualTo(0.14f)); Assert.That(g.Width, Is.EqualTo(0.014f));
            Assert.That(Vector3.Dot(g.Axis.normalized, harp.Root.TransformDirection(harp.StringEnd(7) - harp.StringStart(7)).normalized), Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Dot(g.Normal, -harp.Root.forward), Is.GreaterThan(0.9999f), "normal harp -z (right-handed +z)");
            Assert.That(_stage.FakeHarp.Highlights[7], Is.InRange(0.6f, 1f), "the string itself glows: Highlight(i, .6 + .4 pulse)");
            Pluck(8);
            Assert.That(_t.Mistakes, Is.EqualTo(1)); Assert.That(_t.CurrentEvent, Is.EqualTo(0), "wrongStringMistake");
            Pluck(7);
            Assert.That(_t.CurrentEvent, Is.EqualTo(1)); Assert.That(_t.EventsDone, Is.EqualTo(1), "pluckAdvances");
            int guard = 0;
            while (_t.State != TutorialState.Done && guard++ < 100)
            {
                _t.Update(5f, null);
                if (_t.State == TutorialState.Waiting) Pluck(_lib.Get("harp-arpeggio").events[_t.CurrentEvent].targets[0].i);
            }
            Assert.That(_t.State, Is.EqualTo(TutorialState.Done));
            Assert.That(_t.Summary.Notes, Is.EqualTo(13)); Assert.That(_t.Summary.Mistakes, Is.EqualTo(1), "harpDone");
        }

        // ---------------- pool, bad data, visibility, events ----------------

        [Test]
        public void ThePoolCapsAt64AndResumesSpawningAsBlocksFree()
        {
            var events = new LessonEvent[70];
            for (int i = 0; i < events.Length; i++) events[i] = new LessonEvent { k = i, beat = 0, len = 1, targets = new[] { new LessonTarget { midi = 60 } } };
            var dense = new Lesson { id = "dense", instrument = "piano", bpm = 120, events = events };
            _t.Start(dense); Burn();
            Assert.That(_t.ActiveBlocks, Is.EqualTo(Tutorial.MaxBlocks), "blocksCapped");
            _t.Update(5f, null);
            int guard = 0;
            while (_t.State != TutorialState.Done && guard++ < 500)
            {
                Press(60);
                _t.Update(0.31f, null);   // the hit block fades out and frees its slot; spawning resumes
            }
            Assert.That(_t.State, Is.EqualTo(TutorialState.Done), "every event spawned and played despite the cap");
            Assert.That(_t.Summary.Notes, Is.EqualTo(70));
            _t.Update(0.31f, null);
            Assert.That(_t.ActiveBlocks, Is.EqualTo(0), "no block is left parked for a target played before it spawned");
        }

        [Test]
        public void TargetsTheInstrumentDoesNotHaveAreSkippedAndDroppedFromTheTotal()
        {
            var bad = new Lesson
            {
                id = "bad", instrument = "piano", bpm = 60,
                events = new[]
                {
                    new LessonEvent { beat = 0, len = 1, targets = new[] { new LessonTarget { midi = 10 } } },
                    new LessonEvent { beat = 1, len = 1, targets = new[] { new LessonTarget { midi = 60 } } },
                },
            };
            _t.Start(bad);
            Assert.That(_t.EventsTotal, Is.EqualTo(2));
            Burn();
            Assert.That(_t.EventsTotal, Is.EqualTo(1), "skip: total--");
            Assert.That(_t.CurrentEvent, Is.EqualTo(1), "the empty event is passed over");
            Assert.That(_t.Hint, Is.EqualTo("Play C4"));
            _t.Update(5f, null);
            Press(60);
            Assert.That(_t.State, Is.EqualTo(TutorialState.Done)); Assert.That(_t.Summary.Notes, Is.EqualTo(1));
            // drums without a ride: the waltz's ride targets are skipped, the kick and snare still play
            var stage = new FakeStage(withoutRide: true);
            try
            {
                var t = new Tutorial(_lib, stage);
                t.Start("drums-waltz");
                int total0 = t.EventsTotal;
                t.Update(1.1f, null); t.Update(1.1f, null); t.Update(1.1f, null);
                Assert.That(t.EventsTotal, Is.LessThan(total0));
                Assert.That(t.State, Is.Not.EqualTo(TutorialState.Done));
            }
            finally { stage.Destroy(); }
        }

        [Test]
        public void AHiddenInstrumentDrawsNoBlocksAndNoGlowsButTheLessonRuns()
        {
            _t.Start("piano-scale"); Burn(); _t.Update(5f, null);
            _stage.PianoShown = false;
            _t.Update(0.016f, _glow);
            Assert.That(_t.Visible, Is.False);
            Assert.That(_t.BlockCount, Is.EqualTo(0));
            Assert.That(_glow.Pending, Is.EqualTo(0));
            Press(60);
            Assert.That(_t.EventsDone, Is.EqualTo(1), "hits still count while the blocks are hidden");
            _stage.PianoShown = true;
            _t.Update(0.016f, null);
            Assert.That(_t.BlockCount, Is.GreaterThan(0));
        }

        [Test]
        public void TutorialEventsFollowTheStateMachineWithoutATransientIdle()
        {
            _t.Start("piano-scale");
            Assert.That(_events.Count, Is.EqualTo(1), "Start publishes once");
            Assert.That(_events[0].State, Is.EqualTo(TutorialState.Countdown)); Assert.That(_events[0].LessonId, Is.EqualTo("piano-scale"));
            Burn();
            _t.Update(5f, null);
            Press(62);
            Press(60);
            _t.Stop();
            var states = new List<TutorialState>();
            for (int i = 0; i < _events.Count; i++)
            {
                if (i > 0) Assert.That(_events[i].Version, Is.GreaterThan(_events[i - 1].Version), "versions only grow");
                if (states.Count == 0 || states[states.Count - 1] != _events[i].State) states.Add(_events[i].State);
            }
            Assert.That(states, Is.EqualTo(new[] { TutorialState.Countdown, TutorialState.Playing, TutorialState.Waiting, TutorialState.Playing, TutorialState.Idle }));
            Assert.That(_events[_events.Count - 1].Version, Is.EqualTo(_t.Version));
            int n = _events.Count;
            _t.Start("piano-scale"); _t.Start("drums-beat");
            Assert.That(_events.Count, Is.EqualTo(n + 2));
            Assert.That(_events[n + 1].State, Is.EqualTo(TutorialState.Countdown), "a restart goes straight to the new countdown");
            _t.Start("no-such-lesson");
            Assert.That(_t.State, Is.EqualTo(TutorialState.Idle), "an unknown lesson stops the running one");
        }

        [Test]
        public void TheSummaryIsLiveWhileRunning()
        {
            _t.Start("piano-scale"); Burn(); _t.Update(5f, null);
            Press(62); Press(64);
            Assert.That(_t.Summary.Mistakes, Is.EqualTo(2));
            Assert.That(_t.Summary.Notes, Is.EqualTo(15));
            Assert.That(_t.Summary.Seconds, Is.GreaterThan(1.9f));
            Assert.IsFalse(_t.HasSummary);
        }

        [Test]
        public void EveryJsonHintIsWhatTheHintForSays()
        {
            int n = 0;
            foreach (Lesson l in _lib.Lessons)
                foreach (LessonEvent e in l.events)
                {
                    Assert.That(LessonHints.For(Tutorial.KindOf(l.instrument), e), Is.EqualTo(e.hint), $"{l.id} event {e.k}");
                    Assert.That(LessonHints.For(Tutorial.KindOf(l.instrument), e, _stage.FakeHarp), Is.EqualTo(e.hint), "same through the harp API");
                    n++;
                }
            Assert.That(n, Is.EqualTo(1148));   // first five: the 36 proposed lessons
            Assert.That(LessonHints.NoteName(21), Is.EqualTo("A0")); Assert.That(LessonHints.NoteName(108), Is.EqualTo("C8"));
            Assert.That(LessonHints.For(InstrumentKind.Harp, new LessonEvent { targets = new[] { new LessonTarget { i = 30 } } }), Is.EqualTo("Pluck string 31"));
        }
    }
}
