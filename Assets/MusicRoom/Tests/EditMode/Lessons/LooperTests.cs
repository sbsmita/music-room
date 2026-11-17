// The looper (Looper.cs) on a scripted audio clock (FakeLoopHost, 48 kHz): the clicks before the first note and the
// loop start snapped to it, the 20 s timeout, layer 1 exactly one pass long and looping without a gap, sample-exact
// restarts, 50 passes with jittery frames and zero drift, overdubs on the bar with count-down lights and clicks, held
// notes released at the window's end, mute / unmute / undo / clear (tap again), the six-layer limit, the timing tidy's
// strength and bar safety, the saved format, re-sending after a sound-path change, stalls, late onsets, and the
// recorder's front (DuringPlayback, REC arms, PLAY loops the deck's session, a saved loop from Recordings).
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Lessons;
using NUnit.Framework;

namespace MusicRoom.Tests.Lessons
{
    public sealed class FakeLoopHost : ILoopHost
    {
        public double T = 10;
        public int Sr = 48000;
        public bool Hat;
        public float TipSeconds;
        public int Tips, ReleaseAlls;
        public readonly List<(TakeEvent e, double when, double at)> Sent = new List<(TakeEvent, double, double)>();
        public readonly List<(bool accent, double when)> Clicks = new List<(bool, double)>();
        public Action<TakeEvent> OnDispatch;

        public double Now => T;
        public int SampleRate => Sr;
        public void Dispatch(in TakeEvent e, double when) { Sent.Add((e, when, T)); OnDispatch?.Invoke(e); }
        public void Click(bool accent, double when) => Clicks.Add((accent, when));
        public void ReleaseAll() => ReleaseAlls++;
        public bool HatOpen => Hat;
        public float StartTip() { Tips++; return TipSeconds; }

        public long S(double when) => (long)Math.Round(when * Sr);
        public List<long> SentSamples(string type = null, int midi = -1)
        {
            var l = new List<long>();
            foreach (var x in Sent) if ((type == null || x.e.type == type) && (midi < 0 || x.e.midi == midi) && x.when > 0) l.Add(S(x.when));
            return l;
        }
    }

    public class LooperTests
    {
        const int Sr = 48000;
        FakeLoopHost _host;
        Looper _loop;
        const long Beat90 = 32000;          // 90 BPM at 48 kHz: 2/3 s, a whole number of samples
        const long Pass90x2 = 8 * Beat90;   // 2 bars

        [SetUp]
        public void SetUp()
        {
            _host = new FakeLoopHost();
            _loop = new Looper(_host) { InputLatency = 0 };
        }

        void Tick(double seconds, double dt = 1.0 / 72.0)
        {
            double end = _host.T + seconds;
            while (_host.T < end - 1e-12)
            {
                _host.T = Math.Min(end, _host.T + dt);
                _loop.Tick();
            }
        }

        // Advance the clock to an exact sample (ticking on the way) and play an event there.
        void At(long sample, TakeEvent e)
        {
            double t = sample / (double)Sr;
            if (t > _host.T) Tick(t - _host.T);
            _host.T = t;
            _loop.Capture(e);
        }

        void TickTo(long sample) { double t = sample / (double)Sr; if (t > _host.T) Tick(t - _host.T); }

        long ClickStart => _loop.ClickStart;

        [Test]
        public void LoopStartsSoftClicksOnTheBeatUntilTheFirstNote()
        {
            _loop.PressLoop();
            Assert.That(_loop.State, Is.EqualTo(LoopState.Waiting));
            Assert.That(_host.Tips, Is.EqualTo(1), "Sunny is asked for the first-use line");
            Assert.That(ClickStart, Is.EqualTo(_host.S(10 + Looper.LeadIn)), "the first click LeadIn after the press");
            Tick(4.0);
            Assert.That(_host.Clicks.Count, Is.EqualTo(6), "90 BPM: a click every 2/3 s");
            for (int j = 0; j < _host.Clicks.Count; j++)
            {
                Assert.That(_host.S(_host.Clicks[j].when), Is.EqualTo(ClickStart + j * Beat90), $"click {j} on the beat, to the sample");
                Assert.IsFalse(_host.Clicks[j].accent, "no bar is known yet: no accent");
            }
            // The four lights run with the clicks (bright: the count).
            _host.T = (ClickStart + 2 * Beat90 + 100) / (double)Sr;
            Assert.That(_loop.CountLights(out bool bright), Is.EqualTo(3));
            Assert.IsTrue(bright);
            Assert.That(_host.Sent.Count, Is.EqualTo(0), "nothing to play yet");
            Assert.That(_loop.Layers, Is.EqualTo(0));
        }

        [Test]
        public void SunnysFirstUseLineHoldsTheClicksUntilItEnds()
        {
            _host.TipSeconds = 3.856f;
            _loop.PressLoop();
            Assert.That(ClickStart, Is.EqualTo(_host.S(10 + Looper.LeadIn + 3.856f)), "the clicks start after the line");
            Tick(3.5);
            Assert.That(_host.Clicks.Count, Is.EqualTo(0), "Sunny never talks over a count");
        }

        [Test]
        public void TheFirstNoteStartsLayerOneOnTheNearestClick()
        {
            _loop.Tidy = false;
            _loop.PressLoop();
            // 0.05 beat after click 3: within 1/8 beat, so the loop starts on click 3 and the note lands on beat 1 exactly.
            long t = ClickStart + 3 * Beat90 + (long)(0.05 * Beat90);
            At(t, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            Assert.That(_loop.State, Is.EqualTo(LoopState.Recording));
            Assert.That(_loop.Anchor, Is.EqualTo(ClickStart + 3 * Beat90));
            LoopLayer l = _loop.Session.Layers[0];
            Assert.That(l.Notes[0].Offset, Is.EqualTo(0), "on beat 1");
            // A chord partner 20 ms later moves with it (keeps its spread).
            At(t + 960, TakeEvent.Drum("hat", 0.6f, 0.4f, "bow", false));
            Assert.That(l.Notes[1].Offset, Is.EqualTo(960));
            // A later note keeps its timing (no tidy here).
            At(t + 40000, TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false));
            Assert.That(l.Notes[2].Offset, Is.EqualTo(t + 40000 - _loop.Anchor));
        }

        [Test]
        public void AFirstNoteBetweenClicksStartsTheLoopOnTheNearestBeatSnappedToBeatOne()
        {
            // First five (the integrator's decision): layer 1 starts on the first note, snapped to beat 1, however far
            // (at most half a beat) it was from the click nearest it. Before: past 1/8 beat it kept its place.
            _loop.Tidy = false;
            _loop.PressLoop();
            long t = ClickStart + 2 * Beat90 + (long)(0.3 * Beat90);   // 0.3 beat after click 2: more than 1/8
            At(t, TakeEvent.PianoOn(60, 0.7f));
            Assert.That(_loop.Anchor, Is.EqualTo(ClickStart + 2 * Beat90), "nearest click");
            Assert.That(_loop.Session.Layers[0].Notes[0].Offset, Is.EqualTo(0L), "snapped to beat 1");
            SetUp();
            _loop.Tidy = false;
            _loop.PressLoop();
            long early = ClickStart + 5 * Beat90 - (long)(0.3 * Beat90);   // 0.3 beat BEFORE click 5
            At(early, TakeEvent.PianoOn(64, 0.7f));
            Assert.That(_loop.Anchor, Is.EqualTo(ClickStart + 5 * Beat90), "the click after it");
            LoopLayer l = _loop.Session.Layers[0];
            Assert.That(l.Notes[0].Offset, Is.EqualTo(0L), "snapped onto beat 1 too (no pickup)");
            Assert.That(l.WindowStart, Is.EqualTo(early), "the window opens at the note");
            Assert.That(l.WindowEnd - l.WindowStart, Is.EqualTo(Pass90x2), "and is exactly one pass long");
        }

        [Test]
        public void NotesBeforeTheClicksAndReleasesDoNotStartTheLoop()
        {
            _host.TipSeconds = 2f;       // Sunny's line first: the clicks start 2.12 s after the press
            _loop.PressLoop();
            _loop.Capture(TakeEvent.PianoOff(60));
            _loop.Capture(TakeEvent.Hat(true));
            Assert.That(_loop.State, Is.EqualTo(LoopState.Waiting), "only an onset starts it");
            _loop.Capture(TakeEvent.PianoOn(60, 0.5f));   // during Sunny's line: long before the first click
            Assert.That(_loop.State, Is.EqualTo(LoopState.Waiting), "noodling before the clicks does not count");
        }

        [Test]
        public void TwentySecondsWithoutANoteEndTheSession()
        {
            var phases = new List<LoopEventPhase>();
            _loop.Changed += (p, l, on) => phases.Add(p);
            _loop.PressLoop();
            Tick(Looper.LeadIn + Looper.FirstNoteTimeout - 0.1);
            Assert.That(_loop.State, Is.EqualTo(LoopState.Waiting));
            Tick(0.2);
            Assert.That(_loop.State, Is.EqualTo(LoopState.Off), "timed out");
            Assert.That(_loop.Session, Is.Null, "nothing kept");
            Assert.That(phases, Is.EqualTo(new[] { LoopEventPhase.CountIn, LoopEventPhase.Stopped }));
        }

        // Layer 1 records one pass from the anchor (2 bars at 90 = 256000 samples) and plays from the next pass, gapless.
        long RecordLayerOne(params (double beat, TakeEvent e)[] notes)
        {
            _loop.Tidy = false;
            _loop.PressLoop();
            long a = ClickStart + 4 * Beat90;
            foreach (var (beat, e) in notes) At(a + (long)Math.Round(beat * Beat90), e);
            return a;
        }

        [Test]
        public void LayerOneIsExactlyOnePassAndLoopsWithoutAGap()
        {
            long a = RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)), (2, TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false)),
                (7.5, TakeEvent.Drum("hat", 0.5f, 0.4f, "bow", false)));
            Assert.That(_loop.Anchor, Is.EqualTo(a));
            LoopLayer l = _loop.Session.Layers[0];
            Assert.That(l.WindowStart, Is.EqualTo(a - _host.S(Looper.EarlyGrace)));
            Assert.That(l.WindowEnd - l.WindowStart, Is.EqualTo(Pass90x2), "layer 1 is exactly the loop length");
            // A note after the window belongs to no layer.
            At(l.WindowEnd + 10, TakeEvent.Drum("crash", 0.9f, 0.5f, "edge", false));
            TickTo(a + Pass90x2 + 10);
            Assert.That(_loop.State, Is.EqualTo(LoopState.Looping));
            Assert.That(l.Recording, Is.False);
            Assert.That(l.Notes.Count, Is.EqualTo(3));
            TickTo(a + 3 * Pass90x2 - 5000);          // short of pass 3 (its first event goes out 80 ms ahead)
            List<long> sent = _host.SentSamples();
            Assert.That(sent, Is.EqualTo(new[]
            {
                a + Pass90x2, a + Pass90x2 + 2 * Beat90, a + Pass90x2 + 7 * Beat90 + Beat90 / 2,
                a + 2 * Pass90x2, a + 2 * Pass90x2 + 2 * Beat90, a + 2 * Pass90x2 + 7 * Beat90 + Beat90 / 2,
            }), "pass k plays at anchor + k x pass + offset, to the sample: no gap at the restart");
            foreach (var x in _host.Sent) Assert.That(x.when - x.at, Is.GreaterThan(0.0), "sent ahead of its time (scheduled, never late)");
        }

        [Test]
        public void FiftyPassesOfJitteryFramesNeverDrift()
        {
            long a = RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)), (1.25, TakeEvent.Drum("hat", 0.5f, 0.4f, "bow", false)),
                (3.75, TakeEvent.PianoOn(60, 0.6f)), (5.5, TakeEvent.PianoOff(60)));
            var rng = new System.Random(7);
            long end = a + 51 * Pass90x2 - 8000;   // short of pass 51 by more than a frame + the look-ahead
            while (_host.T < end / (double)Sr)
            {
                _host.T += 0.006 + rng.NextDouble() * 0.045;   // 6..51 ms frames
                _loop.Tick();
            }
            long[] offs = { 0, Beat90 + Beat90 / 4, 3 * Beat90 + 3 * Beat90 / 4, 5 * Beat90 + Beat90 / 2 };
            List<long> sent = _host.SentSamples();
            Assert.That(sent.Count, Is.EqualTo(50 * offs.Length), "every event of 50 passes, once");
            for (int k = 1; k <= 50; k++)
                for (int i = 0; i < offs.Length; i++)
                    Assert.That(sent[(k - 1) * offs.Length + i], Is.EqualTo(a + k * Pass90x2 + offs[i]), $"pass {k} event {i}");
            foreach (var x in _host.Sent) Assert.That(x.when, Is.GreaterThan(x.at), "always scheduled ahead (the look-ahead covers 51 ms frames)");
        }

        [Test]
        public void SeventyAndOneTenBpmPassesAreWholeSamplesAndStayExact()
        {
            var s70 = new LoopSession(70, 2, Sr);
            Assert.That(s70.PassSamples, Is.EqualTo((long)Math.Round(8 * 60.0 / 70 * Sr)), "6.857 s rounded once");
            var s110 = new LoopSession(110, 4, Sr);
            Assert.That(s110.PassSamples, Is.EqualTo((long)Math.Round(16 * 60.0 / 110 * Sr)));
            _loop.SetTempo(2);
            _loop.SetBars(4);
            _loop.Tidy = false;
            _loop.PressLoop();
            long a = ClickStart + 4 * (long)Math.Round(s110.BeatSamples);
            At(a, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            Assert.That(_loop.Session.PassSamples, Is.EqualTo(s110.PassSamples));
            TickTo(_loop.Anchor + 6 * s110.PassSamples - 5000);
            List<long> sent = _host.SentSamples();
            Assert.That(sent.Count, Is.EqualTo(5));
            for (int k = 1; k < sent.Count; k++) Assert.That(sent[k] - sent[k - 1], Is.EqualTo(s110.PassSamples), "restart exactly one pass later");
        }

        [Test]
        public void AnOverdubArmsForTheNextBarAndRecordsExactlyOnePass()
        {
            long a = RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)));
            TickTo(a + Pass90x2 + 3 * Beat90);   // pass 1, beat 3
            _loop.PressLoop();
            Assert.IsTrue(_loop.Armed);
            Assert.That(_loop.Lamp(1), Is.EqualTo(LayerLamp.Armed));
            LoopLayer l2 = _loop.Session.Layers[1];
            long ps = a + 2 * Pass90x2;
            Assert.That(l2.PassStart, Is.EqualTo(ps), "starts at the next loop start");
            Assert.That(l2.WindowStart, Is.EqualTo(ps - _host.S(Looper.EarlyGrace)));
            Assert.That(l2.WindowEnd - l2.WindowStart, Is.EqualTo(Pass90x2), "one pass");
            // Count-down: the last four beats before it light 4, 3, 2, 1 and click.
            int clicks0 = _host.Clicks.Count;
            for (int left = 4; left >= 1; left--)
            {
                TickTo(ps - left * Beat90 + 100);
                Assert.That(_loop.CountLights(out bool bright), Is.EqualTo(left), $"{left} beats left");
                Assert.IsTrue(bright);
            }
            TickTo(ps - 200);
            Assert.That(_host.Clicks.Count - clicks0, Is.EqualTo(4), "four count-down clicks");
            // A note before the window is not the layer's; at the bar it is.
            At(ps - _host.S(0.2), TakeEvent.PianoOn(72, 0.6f));
            At(ps + 100, TakeEvent.PianoOn(67, 0.6f));
            Assert.That(_loop.Overdubbing, Is.True);
            Assert.That(_loop.Lamp(1), Is.EqualTo(LayerLamp.Recording));
            At(ps + 2 * Beat90, TakeEvent.PianoOff(67));
            Assert.That(l2.Notes.Count, Is.EqualTo(2), "the note before the bar and its (missing) release are not in it");
            Assert.That(l2.Notes[0].Offset, Is.EqualTo(100));
            TickTo(ps + Pass90x2 + 10);
            Assert.That(l2.Recording, Is.False);
            Assert.That(_loop.Layers, Is.EqualTo(2));
            Assert.That(_loop.Lamp(1), Is.EqualTo(LayerLamp.On));
            TickTo(ps + 2 * Pass90x2 - 5000);
            Assert.That(_host.SentSamples(TakeEventType.PianoOn), Is.EqualTo(new[] { ps + Pass90x2 + 100 }), "layer 2 plays from the pass after its own");
            Assert.That(_host.SentSamples(TakeEventType.Drum).Count, Is.EqualTo(3), "layer 1 kept playing all along (passes 1, 2, 3)");
        }

        [Test]
        public void ArmingInTheLastBeatWaitsForTheFollowingPass()
        {
            long a = RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)));
            TickTo(a + 2 * Pass90x2 - Beat90 / 2);
            _loop.Arm();
            Assert.That(_loop.Session.Layers[1].PassStart, Is.EqualTo(a + 3 * Pass90x2), "less than a beat left: the next one");
            _loop.Arm();
            Assert.That(_loop.Session.Layers.Count, Is.EqualTo(1), "pressed again before it starts: disarmed");
        }

        [Test]
        public void ANoteHeldOverTheEndOfItsPassIsReleasedThere()
        {
            long a = RecordLayerOne((0, TakeEvent.PianoOn(60, 0.7f)), (6, TakeEvent.PianoOn(64, 0.7f)));   // 64 never released
            LoopLayer l = _loop.Session.Layers[0];
            At(a + 7 * Beat90, TakeEvent.PianoOff(60));
            TickTo(a + Pass90x2 + 10);
            Assert.IsFalse(l.Recording);
            int offs = 0;
            long offAt = -1;
            foreach (var n in l.Notes) if (n.Event.type == TakeEventType.PianoOff && n.Event.midi == 64) { offs++; offAt = n.Offset; }
            Assert.That(offs, Is.EqualTo(1), "a release was added for the held key");
            Assert.That(offAt, Is.EqualTo(l.WindowEnd - 1 - a), "at the window's last sample");
            TickTo(a + 3 * Pass90x2);
            List<long> on64 = _host.SentSamples(TakeEventType.PianoOn, 64), off64 = _host.SentSamples(TakeEventType.PianoOff, 64);
            Assert.That(off64.Count, Is.EqualTo(on64.Count), "every playback note-on gets its note-off");
            for (int k = 0; k < on64.Count; k++) Assert.That(off64[k], Is.GreaterThan(on64[k]).And.LessThan(on64[k] + Pass90x2), "inside its pass");
            int before = _host.ReleaseAlls;
            _loop.Stop();
            Assert.That(_host.ReleaseAlls, Is.EqualTo(before + 1), "STOP lets every playback key up");
        }

        [Test]
        public void AReleaseOfAKeyPressedBeforeTheLayerIsNotRecorded()
        {
            long a = RecordLayerOne((0.5, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)), (1, TakeEvent.PianoOff(48)));
            LoopLayer l = _loop.Session.Layers[0];
            Assert.That(l.Notes.Count, Is.EqualTo(1), "an orphan release is dropped");
            _ = a;
        }

        [Test]
        public void MuteUnmuteUndoAndClearWithItsSecondTap()
        {
            long a = RecordLayerOne((0, TakeEvent.PianoOn(60, 0.7f)), (4, TakeEvent.PianoOff(60)));
            TickTo(a + Pass90x2 + 2 * Beat90);    // pass 1: the note is down
            _loop.Arm();
            long ps = a + 2 * Pass90x2;
            At(ps + Beat90, TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false));
            TickTo(ps + Pass90x2 + Beat90 / 2);   // layer 2 done; pass 3, layer 1's key down again
            Assert.That(_loop.Layers, Is.EqualTo(2));
            var phases = new List<(LoopEventPhase, int, bool)>();
            _loop.Changed += (p, i, on) => phases.Add((p, i, on));
            int sent = _host.Sent.Count;
            _loop.ToggleMute(0);
            Assert.That(_loop.Lamp(0), Is.EqualTo(LayerLamp.Muted));
            Assert.That(_host.Sent.Count, Is.EqualTo(sent + 1), "the muted layer's held key is let go at once");
            Assert.That(_host.Sent[sent].e.type, Is.EqualTo(TakeEventType.PianoOff));
            Assert.That(_host.Sent[sent].when, Is.EqualTo(0.0), "now, not scheduled");
            TickTo(ps + 3 * Pass90x2 - 5000);
            Assert.That(_host.SentSamples(TakeEventType.PianoOn).FindAll(s => s > ps + Pass90x2 + Beat90).Count, Is.EqualTo(0), "muted: no notes");
            Assert.That(_host.SentSamples(TakeEventType.Drum).FindAll(s => s >= ps + Pass90x2 + Beat90).Count, Is.EqualTo(2), "layer 2 plays on");
            _loop.ToggleMute(0);
            Assert.That(_loop.Lamp(0), Is.EqualTo(LayerLamp.On));
            TickTo(ps + 4 * Pass90x2 - 5000);
            Assert.That(_host.SentSamples(TakeEventType.PianoOn).FindAll(s => s >= ps + 3 * Pass90x2).Count, Is.EqualTo(1), "unmuted: back");
            _loop.Undo();
            Assert.That(_loop.Layers, Is.EqualTo(1), "UNDO took the last layer away");
            Assert.That(_loop.Lamp(1), Is.EqualTo(LayerLamp.Off));
            _loop.Clear();
            Assert.IsTrue(_loop.ClearArmed, "the first tap only arms");
            Assert.That(_loop.Active, Is.True);
            Tick(Looper.ClearConfirmSeconds + 0.1);
            Assert.IsFalse(_loop.ClearArmed, "the tap-again window closed");
            _loop.Clear();
            Tick(0.5);
            _loop.Clear();
            Assert.That(_loop.Active, Is.False, "the second tap stops");
            Assert.That(_loop.Session, Is.Null, "and erases");
            Assert.That(phases.ConvertAll(x => x.Item1), Is.EqualTo(new[]
            {
                LoopEventPhase.Muted, LoopEventPhase.Muted, LoopEventPhase.Undo, LoopEventPhase.ClearArmed, LoopEventPhase.ClearArmed,
                LoopEventPhase.Stopped, LoopEventPhase.Cleared,
            }));
            Assert.IsTrue(phases[0].Item3, "Muted: on"); Assert.IsFalse(phases[1].Item3, "then unmuted");
        }

        [Test]
        public void UndoingTheOnlyLayerEndsTheSession()
        {
            RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)));
            _loop.Undo();
            Assert.That(_loop.Active, Is.False);
            Assert.That(_loop.Session, Is.Null);
        }

        [Test]
        public void SixLayersAtMost()
        {
            long a = RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)));
            long p = 1;
            for (int n = 2; n <= 6; n++)
            {
                TickTo(a + p * Pass90x2 + Beat90);
                _loop.Arm();
                long ps = _loop.Session.Layers[n - 1].PassStart;
                At(ps + Beat90 * n / 2, TakeEvent.Drum("snare", 0.5f, 0.3f, "head", false));
                TickTo(ps + Pass90x2 + 10);
                p = (ps - a) / Pass90x2 + 1;
            }
            Assert.That(_loop.Layers, Is.EqualTo(6));
            _loop.Arm();
            Assert.That(_loop.Session.Layers.Count, Is.EqualTo(6), "no seventh");
            Assert.IsTrue(_loop.LimitFlash, "the six lights blink instead");
        }

        [Test]
        public void TheTidyMovesHalfwayToTheSixteenthAndNeverLeavesTheBar()
        {
            var s = new LoopSession(90, 2, Sr);     // a 1/16 is a quarter beat: 8000 samples; a bar 128000
            Assert.That(Looper.Quantise(s, 1000, 0.5f), Is.EqualTo(500));
            Assert.That(Looper.Quantise(s, 7000, 0.5f), Is.EqualTo(7500));
            Assert.That(Looper.Quantise(s, 1000, 0f), Is.EqualTo(1000), "strength 0: untouched");
            Assert.That(Looper.Quantise(s, 1000, 1f), Is.EqualTo(0), "strength 1: on the grid");
            // Bar safety: a note just before bar 2's downbeat may move toward it but stays in bar 1, even at full strength.
            Assert.That(Looper.Quantise(s, 128000 - 300, 1f), Is.EqualTo(128000 - 1), "the bar's last sample at most");
            Assert.That(Looper.Quantise(s, 128000 - 300, 0.5f), Is.EqualTo(128000 - 150));
            Assert.That(Looper.Quantise(s, s.PassSamples - 300, 1f), Is.EqualTo(s.PassSamples - 1), "the last bar never wraps to beat 1");
            Assert.That(Looper.Quantise(s, 128000 + 300, 1f), Is.EqualTo(128000), "and a note just after a downbeat stays in its bar");
            for (long o = 0; o < s.PassSamples; o += 977)
            {
                long q = Looper.Quantise(s, o, 0.5f);
                Assert.That(q / 128000, Is.EqualTo(o / 128000), $"offset {o} stays in its bar");
                Assert.That(Math.Abs(q - o), Is.LessThanOrEqualTo(2000), "half of at most half a sixteenth");
            }
            // In a recording: tidy on, a hand-loose note 1200 samples late of beat 2 lands 600 late; its release moves with it.
            _loop.Tidy = true;
            _loop.PressLoop();
            long a = ClickStart + 4 * Beat90;
            At(a, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            At(a + Beat90 + 1200, TakeEvent.PianoOn(62, 0.6f));
            At(a + 2 * Beat90 + 1200, TakeEvent.PianoOff(62));
            LoopLayer l = _loop.Session.Layers[0];
            Assert.That(l.Notes[1].Offset, Is.EqualTo(Beat90 + 600));
            Assert.That(l.Notes[2].Offset, Is.EqualTo(2 * Beat90 + 600), "the note keeps its length");
        }

        [Test]
        public void ASessionSurvivesTheTakeFormat()
        {
            var s = new LoopSession(110, 4, Sr) { Id = "take-abc-1", Number = 7 };
            var l1 = new LoopLayer(); l1.Insert(0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)); l1.Insert(12345, TakeEvent.Harp(7, 0.6f, 0.4f, 1.2f, "across"));
            var l2 = new LoopLayer { Muted = true }; l2.Insert(s.PassSamples - 1, TakeEvent.PianoOff(60)); l2.Insert(500, TakeEvent.PianoOn(60, 0.5f));
            s.Layers.Add(l1); s.Layers.Add(l2);
            List<TakeEvent> evs = LoopFormat.Encode(s);
            Assert.IsTrue(LoopFormat.IsLoop(evs));
            Assert.That(TakeLibrary.EventCount(evs), Is.EqualTo(4), "the header and layer marks are not events");
            // Through JSON, as the library writes it.
            var rec = UnityEngine.JsonUtility.FromJson<TakeRecord>(UnityEngine.JsonUtility.ToJson(new TakeRecord { id = s.Id, name = "x", events = evs }));
            LoopSession back = LoopFormat.Decode(rec.events, Sr);
            Assert.That(back.Bpm, Is.EqualTo(110)); Assert.That(back.Bars, Is.EqualTo(4));
            Assert.That(back.PassSamples, Is.EqualTo(s.PassSamples));
            Assert.That(back.Id, Is.EqualTo("take-abc-1")); Assert.That(back.Number, Is.EqualTo(7));
            Assert.That(back.Layers.Count, Is.EqualTo(2));
            Assert.IsFalse(back.Layers[0].Muted); Assert.IsTrue(back.Layers[1].Muted);
            for (int i = 0; i < 2; i++)
                for (int k = 0; k < s.Layers[i].Notes.Count; k++)
                {
                    Assert.That(back.Layers[i].Notes[k].Offset, Is.EqualTo(s.Layers[i].Notes[k].Offset), $"layer {i} event {k}: the same sample");
                    Assert.That(back.Layers[i].Notes[k].Event.type, Is.EqualTo(s.Layers[i].Notes[k].Event.type));
                }
            Assert.That(LoopFormat.Name(7, 110, 2), Is.EqualTo("Loop 7 · 110 BPM · 2 layers"));
            Assert.That(LoopFormat.Name(1, 90, 1), Is.EqualTo("Loop 1 · 90 BPM · 1 layer"));
            Assert.That(TakeLibrary.ParseNumber("Loop 12 · 90 BPM · 3 layers"), Is.EqualTo(12), "loops share the take numbering");
        }

        [Test]
        public void APauseSkipsAheadInsteadOfABurst()
        {
            long a = RecordLayerOne((0, TakeEvent.PianoOn(60, 0.7f)), (1, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)));
            TickTo(a + Pass90x2 + Beat90 / 2);
            int before = _host.ReleaseAlls, sent = _host.Sent.Count;
            _host.T += 7.0;                           // headset off: no frames for 7 s
            _loop.Tick();
            Assert.That(_host.ReleaseAlls, Is.EqualTo(before + 1), "everything held is let go");
            foreach (var x in _host.Sent.GetRange(sent, _host.Sent.Count - sent)) Assert.That(x.when, Is.GreaterThanOrEqualTo(x.at), "nothing from the missed seconds");
            long now = _host.S(_host.T);
            TickTo(now + 2 * Pass90x2);
            List<long> kicks = _host.SentSamples(TakeEventType.Drum);
            for (int k = 1; k < kicks.Count; k++) Assert.That((kicks[k] - a - Beat90) % Pass90x2, Is.EqualTo(0), "still on the original grid");
        }

        [Test]
        public void LateOnsetsAreDroppedButReleasesStillPlay()
        {
            long a = RecordLayerOne((0, TakeEvent.PianoOn(60, 0.7f)), (6, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)), (6.1, TakeEvent.PianoOff(60)));
            TickTo(a + Pass90x2 + 6 * Beat90 - _host.S(0.1));
            // A 350 ms hitch over the kick (shorter than the stall reset): the kick is 250 ms late, so it is skipped; the
            // release after it is late too, but a release always plays (no stuck key).
            _host.T = (a + Pass90x2 + 6 * Beat90) / (double)Sr + 0.25;
            int kicks = _host.SentSamples(TakeEventType.Drum).Count, offs = _host.SentSamples(TakeEventType.PianoOff).Count;
            _loop.Tick();
            Assert.That(_host.SentSamples(TakeEventType.Drum).Count, Is.EqualTo(kicks), "a 250 ms late kick never plays in a burst");
            Assert.That(_host.SentSamples(TakeEventType.PianoOff).Count, Is.EqualTo(offs + 1), "the release still goes out");
        }

        [Test]
        public void SettingsChangeOnlyBetweenSessions()
        {
            int saved = 0;
            _loop.SettingsChanged += () => saved++;
            _loop.SetTempo(0);
            _loop.SetBars(4);
            Assert.That(_loop.Bpm, Is.EqualTo(70)); Assert.That(_loop.Bars, Is.EqualTo(4));
            Assert.That(saved, Is.EqualTo(2));
            _loop.PressLoop();
            _loop.SetTempo(2);
            Assert.That(_loop.Bpm, Is.EqualTo(70), "the running session keeps its tempo");
            _loop.ToggleMetronome();
            Assert.IsTrue(_loop.Metronome, "the click can change any time");
            _loop.Configure(95, 2, false);
            Assert.That(_loop.TempoIndex, Is.EqualTo(0), "Configure waits for the session to end too");
        }

        [Test]
        public void TheMetronomeClicksWhileLoopingOnlyWhenOn()
        {
            long a = RecordLayerOne((0, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false)));
            TickTo(a + Pass90x2 - 100);
            int c0 = _host.Clicks.Count;
            Assert.That(_host.Clicks.FindAll(c => _host.S(c.when) >= a).Count, Is.EqualTo(8), "layer 1's pass is clicked (accent on the bars)");
            // Beat 1's click went out (80 ms ahead) as a plain waiting click before the first note made it beat 1; bar 2's is accented.
            Assert.That(_host.Clicks.FindAll(c => _host.S(c.when) >= a && c.accent).Count, Is.EqualTo(1));
            Assert.IsTrue(_host.Clicks.Find(c => _host.S(c.when) == a + 4 * Beat90).accent, "the second bar's downbeat");
            TickTo(a + 2 * Pass90x2 - 100);
            Assert.That(_host.Clicks.Count, Is.EqualTo(c0), "off by default while looping");
            _loop.ToggleMetronome();
            TickTo(a + 3 * Pass90x2 - 100);
            Assert.That(_host.Clicks.Count - c0, Is.EqualTo(8), "on: every beat");
        }

        // ---- the recorder's front ----

        [Test]
        public void TheRecorderRoutesLiveEventsToTheLoopAndFlagsItsOwnSendsAsPlayback()
        {
            var bus = new EventBus();
            var seen = new List<RecorderEvent>();
            bus.Subscribe<RecorderEvent>(e => seen.Add(e));
            var states = new List<RecorderState>();
            bus.Subscribe<RecorderStateEvent>(e => states.Add(e.State));
            var rec = new Recorder(() => _host.T, bus);
            rec.Looper = _loop;
            // The instruments' playback calls come back into the recorder (as the piano's ghost presser does).
            _host.OnDispatch = e => rec.Add(e);
            _loop.Tidy = false;
            rec.Loop();
            Assert.That(rec.State, Is.EqualTo(RecorderState.Recording), "waiting for the first note counts as recording");
            long a = ClickStart + 4 * Beat90;
            TickTo(a);
            rec.Add(TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            Assert.That(seen[0].DuringPlayback, Is.False, "the player's note is live");
            TickTo(a + Pass90x2 + Beat90);
            Assert.That(rec.State, Is.EqualTo(RecorderState.Playing), "looping");
            Assert.That(seen.Count, Is.EqualTo(2));
            Assert.That(seen[1].DuringPlayback, Is.True, "the loop's own kick is playback");
            Assert.That(_loop.Session.Layers[0].Notes.Count, Is.EqualTo(1), "and never recorded again");
            rec.Add(TakeEvent.Drum("snare", 0.7f, 0.3f, "head", false));
            Assert.That(seen[2].DuringPlayback, Is.False, "live play during the loop still counts as live (lessons, Sunny)");
            rec.Record();
            Assert.IsTrue(_loop.Armed, "REC while looping arms a layer");
            rec.Stop();
            Assert.That(rec.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(states, Is.EqualTo(new[] { RecorderState.Recording, RecorderState.Playing, RecorderState.Idle }));
            Assert.IsTrue(rec.CurrentIsLoop);
            int kicks = _host.SentSamples(TakeEventType.Drum).Count;
            Assert.IsTrue(rec.Play(), "PLAY loops the deck's last session");
            Assert.That(rec.State, Is.EqualTo(RecorderState.Playing));
            Tick(Looper.LeadIn + 0.05);
            Assert.That(_host.SentSamples(TakeEventType.Drum).Count, Is.EqualTo(kicks + 1), "from its first pass");
            rec.Stop();
            // A plain take still works exactly as before, and PLAY then plays it.
            rec.Record();
            rec.Add(TakeEvent.PianoOn(60, 0.5f));
            _host.T += 1; rec.Stop();
            Assert.IsFalse(rec.CurrentIsLoop);
            Assert.That(rec.Take.Count, Is.EqualTo(1));
        }

        [Test]
        public void ASavedLoopFromRecordingsLoopsAndTheDecksOwnComesBack()
        {
            var rec = new Recorder(() => _host.T, null);
            rec.Looper = _loop;
            LoopSession saved = null;
            _loop.Save += s => saved = s;
            _loop.Tidy = false;
            rec.Loop();
            long a = ClickStart + 4 * Beat90;
            At(a, TakeEvent.Drum("kick", 0.8f, 0.3f, "head", false));
            TickTo(a + Pass90x2 + 10);
            rec.Stop();
            Assert.That(saved, Is.Not.Null, "STOP saved the session");
            LoopSession deck = _loop.DeckSession;
            var other = new LoopSession(70, 2, Sr);
            var l = new LoopLayer(); l.Insert(1000, TakeEvent.PianoOn(72, 0.5f)); other.Layers.Add(l);
            Assert.IsTrue(rec.PlayEvents(LoopFormat.Encode(other), other.PassSeconds));
            Assert.IsTrue(rec.External); Assert.That(rec.State, Is.EqualTo(RecorderState.Playing));
            Assert.That(_loop.Session.Bpm, Is.EqualTo(70));
            long a2 = _loop.Anchor;
            TickTo(a2 + 3 * other.PassSamples - 5000);
            List<long> on = _host.SentSamples(TakeEventType.PianoOn);
            Assert.That(on, Is.EqualTo(new[] { a2 + 1000, a2 + other.PassSamples + 1000, a2 + 2 * other.PassSamples + 1000 }), "it loops");
            rec.Stop();
            Assert.IsFalse(rec.External);
            Assert.That(_loop.DeckSession, Is.SameAs(deck), "the deck's own session comes back");
        }
    }
}
