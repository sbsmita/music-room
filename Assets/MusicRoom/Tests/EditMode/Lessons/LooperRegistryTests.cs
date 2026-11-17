// The recorder and the looper on the registry's event roles (platform.md 6.1-6.5), in u-looper's EditMode harness (a
// scripted 48 kHz audio clock, FakeLoopHost) with a synthetic registry (InstrumentRegistry.Build): onsets are tidied,
// controls never are (they follow their lane's onset and sit DispatchLead early); an open control lane is closed at the
// window's end (a final level-0 control and the Off); markers never enter a layer; layer k plays on lane 1 + k; a struck
// onset without a Release is not damped while a Held piano key gets its release; Sampled State types are sampled at a
// layer's start; a muted layer closes the lanes it sounds; displayed instruments are woken ahead of their events. The
// recorder: 40000 events, Add(ev, at), markers recorded but never played, takes on lane 7, controls dispatched early, the
// generic playback wiring and the wake at Play. EventRoles agrees with the classic three's descriptors.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Harp;
using MusicRoom.Lessons;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Lessons
{
    /// <summary>A synthetic registry: the real piano, a rubbed-bowl-like continuous instrument, a marimba-like struck one and a
    /// hand-drum-like one with a sampled state.</summary>
    static class SyntheticRegistry
    {
        public const string On = "rubOn", Ctl = "rubCtl", Off = "rubOff", Hold = "rubHold";
        public const string Bar = "marimba", BarDamp = "marimbaDamp", Slap = "handDrum", Mute = "handDrumMute";

        static InstrumentDescriptor D(InstrumentKind kind, string id, int carousel, params EventTypeSpec[] events) => new InstrumentDescriptor
        {
            Kind = kind, Rig = InstrumentIds.RigOf(kind), Id = id, Name = id, Carousel = carousel, Traits = InstrumentTraits.Playable,
            Seat = new SeatSpec(), Lessons = new LessonSpec(), Events = events,
        };

        public static InstrumentRegistry Build() => InstrumentRegistry.Build(new[]
        {
            new PianoDefinition().Describe(),
            D(InstrumentKind.Bowls, "bowls", 2,
                new EventTypeSpec(On, EventRole.Onset, EventKey.Lane), new EventTypeSpec(Ctl, EventRole.Control, EventKey.Lane),
                new EventTypeSpec(Off, EventRole.Release, EventKey.Lane), new EventTypeSpec(Hold, EventRole.Marker, EventKey.Midi)),
            D(InstrumentKind.Marimba, "marimba", 5, new EventTypeSpec(Bar, EventRole.Onset, EventKey.Midi), new EventTypeSpec(BarDamp, EventRole.Release, EventKey.Midi)),
            D(InstrumentKind.HandDrums, "handdrums", 6, new EventTypeSpec(Slap, EventRole.Onset, EventKey.Kind), new EventTypeSpec(Mute, EventRole.State, EventKey.Kind, sampled: true)),
        });
    }

    /// <summary>FakeLoopHost's twin that wants wakes and samples the hand drum's state.</summary>
    public sealed class WakingLoopHost : ILoopHost
    {
        public double T = 10;
        public readonly List<(TakeEvent e, double when, double at)> Sent = new List<(TakeEvent, double, double)>();
        public readonly List<(string type, double at)> Woken = new List<(string, double)>();
        public bool MuteOpen;
        public IInstrumentRegistry Reg;
        public double Now => T;
        public int SampleRate => 48000;
        public void Dispatch(in TakeEvent e, double when) => Sent.Add((e, when, T));
        public void Click(bool accent, double when) { }
        public void ReleaseAll() { }
        public bool HatOpen => false;
        public float StartTip() => 0f;
        public IInstrumentRegistry Registry => Reg;
        public bool WantsWakes => true;
        public void Wake(in TakeEvent e) => Woken.Add((e.type, T));
        public bool TrySample(string type, out TakeEvent e)
        {
            e = new TakeEvent { type = type, kind = "low", open = MuteOpen };
            return type == SyntheticRegistry.Mute;
        }
    }

    public class LooperRegistryTests
    {
        const int Sr = 48000;
        const long Beat90 = 32000, Pass90x2 = 8 * Beat90;
        FakeLoopHost _host;
        Looper _loop;

        [SetUp]
        public void SetUp()
        {
            _host = new FakeLoopHost();
            _loop = new Looper(_host) { InputLatency = 0, Roles = EventRoles.From(SyntheticRegistry.Build()) };
        }

        void Tick(double seconds, double dt = 1.0 / 72.0)
        {
            double end = _host.T + seconds;
            while (_host.T < end - 1e-12) { _host.T = Math.Min(end, _host.T + dt); _loop.Tick(); }
        }

        void At(long sample, TakeEvent e)
        {
            double t = sample / (double)Sr;
            if (t > _host.T) Tick(t - _host.T);
            _host.T = t;
            _loop.Capture(e);
        }

        void TickTo(long sample) { double t = sample / (double)Sr; if (t > _host.T) Tick(t - _host.T); }

        static TakeEvent Ctl(float u, float vel, float speed) => new TakeEvent { type = SyntheticRegistry.Ctl, u = u, vel = vel, speed = speed };
        static TakeEvent Bar(int midi) => new TakeEvent { type = SyntheticRegistry.Bar, midi = midi, vel = 0.7f };
        static long S(double seconds) => (long)Math.Round(seconds * Sr);

        // LOOP, then the first note exactly on click 2: the anchor is that click and layer 1's pass starts there.
        long StartOnClick(TakeEvent first)
        {
            _loop.PressLoop();
            long a = _loop.ClickStart + 2 * Beat90;
            At(a, first);
            Assert.That(_loop.Anchor, Is.EqualTo(a));
            return a;
        }

        static LoopNote Note(LoopLayer l, string type, int nth = 0)
        {
            foreach (LoopNote n in l.Notes) if (n.Event.type == type && nth-- == 0) return n;
            Assert.Fail("no " + type + " in the layer");
            return default;
        }

        static int CountOf(LoopLayer l, string type) { int c = 0; foreach (LoopNote n in l.Notes) if (n.Event.type == type) c++; return c; }

        [Test]
        public void OnsetsAreTidiedAndControlsAreNot()
        {
            long a = StartOnClick(Bar(60));
            LoopLayer l = _loop.Session.Layers[0];
            // A struck onset 1000 samples after beat 1 is tidied halfway toward the sixteenth (on beat 1): 500.
            At(a + Beat90 + 1000, Bar(62));
            Assert.That(Note(l, SyntheticRegistry.Bar, 1).Offset, Is.EqualTo(Beat90 + 500), "an onset is tidied");
            // A control lane joined mid-way (its On before the window): each control exactly where it was, DispatchLead early.
            At(a + 2 * Beat90 + 1000, Ctl(57.3f, 0.4f, 0.0333f));
            At(a + 2 * Beat90 + 5000, Ctl(57.5f, 0.45f, 0.25f));
            Assert.That(Note(l, SyntheticRegistry.Ctl).Offset, Is.EqualTo(2 * Beat90 + 1000 - S(0.0333f)), "a control is never tidied");
            Assert.That(Note(l, SyntheticRegistry.Ctl, 1).Offset, Is.EqualTo(2 * Beat90 + 5000 - S(0.05)), "it leads by min(speed, 0.05)");
            Assert.That(Note(l, SyntheticRegistry.Ctl, 1).Offset % (Beat90 / 4), Is.Not.EqualTo(0), "off the grid");
        }

        [Test]
        public void ALaneKeepsItsShapeItsControlsAndOffMoveWithItsTidiedOnset()
        {
            long a = StartOnClick(Bar(60));
            LoopLayer l = _loop.Session.Layers[0];
            At(a + 3 * Beat90 + 1000, new TakeEvent { type = SyntheticRegistry.On, u = 57f, vel = 0.2f });
            long onAt = Note(l, SyntheticRegistry.On).Offset;
            Assert.That(onAt, Is.EqualTo(3 * Beat90 + 500), "the lane's onset is an Onset: tidied");
            long shift = onAt - (3 * Beat90 + 1000);
            At(a + 3 * Beat90 + 3000, Ctl(57.4f, 0.3f, 0.0333f));
            At(a + 3 * Beat90 + 9000, new TakeEvent { type = SyntheticRegistry.Off });
            Assert.That(Note(l, SyntheticRegistry.Ctl).Offset, Is.EqualTo(3 * Beat90 + 3000 + shift - S(0.0333f)));
            Assert.That(Note(l, SyntheticRegistry.Off).Offset, Is.EqualTo(3 * Beat90 + 9000 + shift), "the Off keeps the lane's length");
            Assert.That(l.OpenCount, Is.EqualTo(0), "the lane closed itself");
        }

        [Test]
        public void AControlLaneStillSoundingIsClosedAtTheWindowsEnd()
        {
            long a = StartOnClick(new TakeEvent { type = SyntheticRegistry.On, u = 60f, vel = 0.5f });
            LoopLayer l = _loop.Session.Layers[0];
            At(a + Beat90, Ctl(60.5f, 0.6f, 0.0333f));
            TickTo(a + Pass90x2 + 10);
            Assert.IsFalse(l.Recording, "the pass is done");
            long offEnd = l.WindowEnd - 1 - a;
            LoopNote close = Note(l, SyntheticRegistry.Ctl, 1);
            LoopNote off = Note(l, SyntheticRegistry.Off);
            Assert.That(close.Event.vel, Is.EqualTo(0f), "a final level-0 control");
            Assert.That(close.Event.u, Is.EqualTo(60.5f), "at the last pitch");
            Assert.That(close.Event.speed, Is.EqualTo((float)Looper.CloseSpeed).Within(1e-6f));
            Assert.That(close.Offset, Is.EqualTo(offEnd - S(Looper.CloseSpeed)), "its glide ends at the window's end");
            Assert.That(off.Offset, Is.EqualTo(offEnd), "then the Off");
            Assert.That(l.OpenCount, Is.EqualTo(0));
            // Playback: every pass sends the lane's events on the layer's own lane (layer 0 plays on lane 1).
            TickTo(a + 3 * Pass90x2 - S(0.1));                              // passes 1 and 2, the second Off 30 ms before pass 3
            int ons = 0, offs = 0;
            foreach (var x in _host.Sent)
            {
                if (x.e.type == SyntheticRegistry.On) ons++;
                if (x.e.type == SyntheticRegistry.Off) offs++;
                Assert.That(x.e.lane, Is.EqualTo(PlaybackLanes.Layer(0)).And.EqualTo(1), x.e.type + " on lane 1");
            }
            Assert.That(ons, Is.EqualTo(2)); Assert.That(offs, Is.EqualTo(ons), "every playback lane ends");
        }

        [Test]
        public void MarkersAreDroppedFromLayersAndNeverStartOne()
        {
            _loop.PressLoop();
            long t0 = _loop.ClickStart + Beat90;
            At(t0, new TakeEvent { type = SyntheticRegistry.Hold, midi = 69 });
            Assert.That(_loop.State, Is.EqualTo(LoopState.Waiting), "a marker is a judgement, not a first note");
            long a = _loop.ClickStart + 2 * Beat90;
            At(a, Bar(60));
            At(a + Beat90, new TakeEvent { type = SyntheticRegistry.Hold, midi = 69 });
            LoopLayer l = _loop.Session.Layers[0];
            Assert.That(CountOf(l, SyntheticRegistry.Hold), Is.EqualTo(0), "markers are dropped");
            Assert.That(l.Notes.Count, Is.EqualTo(1));
        }

        [Test]
        public void LayerKPlaysOnLaneOnePlusK()
        {
            long a = StartOnClick(Bar(60));
            TickTo(a + Pass90x2 + Beat90);
            _loop.Arm();
            long ps = _loop.Session.Layers[1].PassStart;
            At(ps + 2 * Beat90, Bar(72));
            TickTo(ps + 2 * Pass90x2);
            Assert.That(_loop.Layers, Is.EqualTo(2));
            bool lane1 = false, lane2 = false;
            foreach (var x in _host.Sent)
            {
                if (x.e.midi == 60) { Assert.That(x.e.lane, Is.EqualTo(1)); lane1 = true; }
                if (x.e.midi == 72) { Assert.That(x.e.lane, Is.EqualTo(2)); lane2 = true; }
            }
            Assert.IsTrue(lane1 && lane2, "layer 0 on lane 1, layer 1 on lane 2");
            Assert.That(PlaybackLanes.Layer(5), Is.EqualTo(6));
            Assert.That(PlaybackLanes.Take, Is.EqualTo(7));
        }

        [Test]
        public void AStruckOnsetIsNotDampedAtTheBoundaryButAHeldKeyIsReleased()
        {
            long a = StartOnClick(Bar(60));
            At(a + 6 * Beat90, Bar(64));                                    // a bar ringing over the end of the pass
            At(a + 6 * Beat90 + 100, TakeEvent.PianoOn(67, 0.6f));          // a piano key held over it
            LoopLayer l = _loop.Session.Layers[0];
            TickTo(a + Pass90x2 + 10);
            Assert.IsFalse(l.Recording);
            Assert.That(CountOf(l, SyntheticRegistry.BarDamp), Is.EqualTo(0), "a struck note rings on: no damp");
            LoopNote off = Note(l, TakeEventType.PianoOff);
            Assert.That(off.Event.midi, Is.EqualTo(67));
            Assert.That(off.Offset, Is.EqualTo(l.WindowEnd - 1 - a), "the piano key exactly as before: at the window's last sample");
        }

        [Test]
        public void SampledStatesAreTakenWhenALayersWindowOpens()
        {
            var host = new WakingLoopHost { Reg = SyntheticRegistry.Build(), MuteOpen = true };
            var loop = new Looper(host) { InputLatency = 0 };
            loop.PressLoop();
            host.T = (loop.ClickStart + 2 * Beat90) / (double)Sr;
            loop.Capture(new TakeEvent { type = SyntheticRegistry.Slap, kind = "low", vel = 0.7f });
            host.T += 0.5;
            loop.Capture(new TakeEvent { type = SyntheticRegistry.Mute, kind = "low", open = false });
            for (int i = 0; i < 400; i++) { host.T += 1.0 / 72.0; loop.Tick(); }
            LoopLayer l = loop.Session.Layers[0];
            Assert.IsFalse(l.Recording);
            Assert.That(l.Notes[0].Event.type, Is.EqualTo(SyntheticRegistry.Mute), "the sampled state leads the layer");
            Assert.That(l.Notes[0].Offset, Is.EqualTo(0));
            Assert.IsTrue(l.Notes[0].Event.open, "the value it had when the window opened");
        }

        [Test]
        public void MutingALayerClosesTheLanesItSounds()
        {
            long a = StartOnClick(new TakeEvent { type = SyntheticRegistry.On, u = 62f, vel = 0.5f });
            At(a + 2 * Beat90, Ctl(62.2f, 0.5f, 0.0333f));
            At(a + 5 * Beat90, new TakeEvent { type = SyntheticRegistry.Off });
            TickTo(a + Pass90x2 + 3 * Beat90);                              // pass 1 plays: the lane sounds
            int sent = _host.Sent.Count;
            _loop.ToggleMute(0);
            Assert.That(_host.Sent.Count, Is.EqualTo(sent + 2), "a final level-0 control and the Off, at once");
            Assert.That(_host.Sent[sent].e.type, Is.EqualTo(SyntheticRegistry.Ctl));
            Assert.That(_host.Sent[sent].e.vel, Is.EqualTo(0f));
            Assert.That(_host.Sent[sent + 1].e.type, Is.EqualTo(SyntheticRegistry.Off));
            Assert.That(_host.Sent[sent + 1].e.lane, Is.EqualTo(1), "on the layer's lane");
            Assert.That(_host.Sent[sent + 1].when, Is.EqualTo(0.0));
        }

        [Test]
        public void DisplayedInstrumentsAreWokenAQuarterSecondAheadOfTheirEvents()
        {
            var host = new WakingLoopHost { Reg = SyntheticRegistry.Build() };
            var loop = new Looper(host) { InputLatency = 0 };
            loop.PressLoop();
            long a = loop.ClickStart + 2 * Beat90;
            host.T = a / (double)Sr;
            loop.Capture(Bar(60));
            host.T = (a + 4 * Beat90) / (double)Sr;
            loop.Capture(Bar(62));
            for (int i = 0; i < 72 * 10; i++) { host.T += 1.0 / 72.0; loop.Tick(); }
            Assert.That(host.Sent.Count, Is.GreaterThan(2));
            foreach (var x in host.Sent)
            {
                if (x.when <= 0) continue;
                double wokenAt = double.NaN;
                foreach (var w in host.Woken) if (w.type == x.e.type && w.at <= x.when && w.at >= x.when - 0.32) { wokenAt = w.at; break; }
                Assert.That(double.IsNaN(wokenAt), Is.False, "woken before the event at " + x.when);
                Assert.That(x.when - wokenAt, Is.GreaterThanOrEqualTo(0.25 - 1e-9), "at least 250 ms ahead");
            }
        }
    }

    public class RecorderPlatformTests
    {
        sealed class FakePlayback : IInstrumentPlayback
        {
            public InstrumentKind Kind { get; set; }
            public readonly List<TakeEvent> Played = new List<TakeEvent>();
            public int ReleaseAlls;
            public readonly List<int> Closed = new List<int>();
            public bool Play(in TakeEvent e) { Played.Add(e); return true; }
            public void ReleaseAll() => ReleaseAlls++;
            public void CloseLane(int lane) => Closed.Add(lane);
        }

        sealed class FakePresence : IAudioPresence
        {
            public readonly List<(InstrumentKind kind, double at)> Wakes = new List<(InstrumentKind, double)>();
            public Func<double> Now;
            public void Wake(InstrumentKind kind) => Wakes.Add((kind, Now()));
            public bool Paused(InstrumentKind kind) => false;
        }

        double _now = 100;
        Recorder _rec;
        FakePlayback _rub;
        InstrumentRegistry _reg;

        [SetUp]
        public void SetUp()
        {
            _reg = SyntheticRegistry.Build();
            _rec = new Recorder(() => _now, null) { Roles = EventRoles.From(_reg) };
            _rub = new FakePlayback { Kind = InstrumentKind.Bowls };
            PlaybackWiring.WireInstruments(_rec, _reg, new IInstrumentPlayback[] { _rub });
        }

        [Test]
        public void ItHoldsFortyThousandEventsAndTenMinutes()
        {
            Assert.That(Recorder.MaxEvents, Is.EqualTo(40000));
            Assert.That(Recorder.MaxSeconds, Is.EqualTo(600));
            _rec.Record();
            for (int i = 0; i < 40000; i++) _rec.Add(new TakeEvent { type = SyntheticRegistry.Ctl, u = 60f, vel = 0.5f });
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Recording), "40000 fit");
            _rec.Add(new TakeEvent { type = SyntheticRegistry.Ctl });
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "the next one stops the take");
            Assert.That(_rec.Take.Count, Is.EqualTo(40000));
        }

        [Test]
        public void AddAtAnEarlierTimeStampsItThereAndKeepsTheTakeInOrder()
        {
            _rec.Record();
            _now = 101.0;
            _rec.Add(new TakeEvent { type = SyntheticRegistry.Slap, kind = "low" });
            _now = 101.2;
            _rec.Add(new TakeEvent { type = SyntheticRegistry.Bar, midi = 60 });
            _rec.Add(new TakeEvent { type = SyntheticRegistry.Mute, kind = "low" }, 101.12);   // relabelled 80 ms later
            _rec.Add(new TakeEvent { type = SyntheticRegistry.Bar, midi = 62 }, 99.0);          // before the record: t = 0
            _now = 102;
            _rec.Stop();
            List<TakeEvent> take = _rec.Take;
            Assert.That(take.Count, Is.EqualTo(4));
            Assert.That(take[0].type, Is.EqualTo(SyntheticRegistry.Bar)); Assert.That(take[0].t, Is.EqualTo(0f), "clamped to the start");
            Assert.That(take[2].type, Is.EqualTo(SyntheticRegistry.Mute));
            Assert.That(take[2].t, Is.EqualTo(1.12f).Within(1e-4f), "t = at - record start");
            for (int i = 1; i < take.Count; i++) Assert.That(take[i].t, Is.GreaterThanOrEqualTo(take[i - 1].t), "in time order");
        }

        [Test]
        public void MarkersAreRecordedButNeverPlayedBackAndTakesPlayOnLaneSeven()
        {
            int markerPlays = 0;
            _rec.SetTarget(SyntheticRegistry.Hold, e => markerPlays++);     // even with a target, a marker never plays
            var take = new List<TakeEvent>
            {
                new TakeEvent { t = 0.1f, type = SyntheticRegistry.On, u = 60f, vel = 0.4f },
                new TakeEvent { t = 0.4f, type = SyntheticRegistry.Hold, midi = 60 },
                new TakeEvent { t = 0.5f, type = SyntheticRegistry.Off },
            };
            Assert.IsTrue(_rec.PlayEvents(take, 1.0));
            for (int i = 0; i < 80; i++) { _now += 1.0 / 72.0; _rec.Update(); }
            Assert.That(markerPlays, Is.EqualTo(0));
            Assert.That(_rub.Played.ConvertAll(e => e.type), Is.EqualTo(new[] { SyntheticRegistry.On, SyntheticRegistry.Off }));
            foreach (TakeEvent e in _rub.Played) Assert.That(e.lane, Is.EqualTo(PlaybackLanes.Take), "a take plays on lane 7");
            Assert.That(_rec.State, Is.EqualTo(RecorderState.Idle), "the take ended");
            Assert.That(_rub.Closed, Does.Contain(PlaybackLanes.Take), "its stop closes the take lane");
            Assert.That(_rub.ReleaseAlls, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ControlsAreDispatchedTheirLeadEarly()
        {
            double start = _now;
            var take = new List<TakeEvent>
            {
                new TakeEvent { t = 0.2f, type = SyntheticRegistry.On, u = 60f, vel = 0.4f },
                new TakeEvent { t = 0.5f, type = SyntheticRegistry.Ctl, u = 61f, vel = 0.5f, speed = 0.25f },
                new TakeEvent { t = 0.53f, type = SyntheticRegistry.Ctl, u = 61.1f, vel = 0.5f, speed = 0.03f },
            };
            _rec.PlayEvents(take, 1.0);
            var at = new List<double>();
            int seen = 0;
            for (int i = 0; i < 1000 && _rec.State == RecorderState.Playing; i++)
            {
                _now += 0.001;
                _rec.Update();
                while (seen < _rub.Played.Count) { at.Add(_now - start); seen++; }
            }
            Assert.That(at.Count, Is.EqualTo(3));
            Assert.That(at[0], Is.EqualTo(0.2).Within(0.0015), "the onset on its time");
            Assert.That(at[1], Is.EqualTo(0.45).Within(0.0015), "a 0.25 s glide leads by 50 ms");
            Assert.That(at[2], Is.EqualTo(0.50).Within(0.0015), "a 30 ms glide leads by 30 ms");
        }

        [Test]
        public void TheGenericWiringSkipsMarkersAndClassicTypesAndReleasesEveryPlayback()
        {
            var rec = new Recorder(() => _now, null) { Roles = EventRoles.From(_reg) };
            PlaybackWiring.Wire(rec, () => null, () => null, () => null);
            int classic = rec.TargetCount;
            var piano = new FakePlayback { Kind = InstrumentKind.Piano };
            var bars = new FakePlayback { Kind = InstrumentKind.Marimba };
            int wired = PlaybackWiring.WireInstruments(rec, _reg, new IInstrumentPlayback[] { piano, _rub, bars });
            Assert.That(wired, Is.EqualTo(3 + 2), "rub On/Ctl/Off (not the Hold marker), marimba and its damp; the piano's were wired");
            Assert.IsFalse(rec.HasTarget(SyntheticRegistry.Hold));
            Assert.That(rec.TargetCount, Is.EqualTo(classic + 5));
            Assert.That(PlaybackWiring.WireInstruments(rec, _reg, new IInstrumentPlayback[] { piano, _rub, bars }), Is.EqualTo(0), "wired once");
            rec.Dispatch(new TakeEvent { type = SyntheticRegistry.Bar, midi = 60 });
            Assert.That(bars.Played.Count, Is.EqualTo(1));
            Assert.That(piano.Played.Count, Is.EqualTo(0), "the classic lines keep the piano's types");
            rec.ReleaseAll();
            Assert.That(new[] { piano.ReleaseAlls, _rub.ReleaseAlls, bars.ReleaseAlls }, Is.EqualTo(new[] { 1, 1, 1 }), "every ReleaseAll");
            rec.Dispatch(new TakeEvent { type = "nobody" });   // a missing instrument drops its events
        }

        [Test]
        public void ATakeWakesItsInstrumentsAtPlayAndAheadOfTheirEvents()
        {
            var presence = new FakePresence { Now = () => _now };
            _rec.PresenceSource = () => presence;
            double start = _now;
            var take = new List<TakeEvent>
            {
                new TakeEvent { t = 0.1f, type = SyntheticRegistry.On, u = 60f, vel = 0.4f },
                new TakeEvent { t = 2.0f, type = SyntheticRegistry.Bar, midi = 60 },
            };
            _rec.PlayEvents(take, 3.0);
            Assert.That(presence.Wakes.Count, Is.EqualTo(1), "the first 0.3 s are scanned at Play");
            Assert.That(presence.Wakes[0].kind, Is.EqualTo(InstrumentKind.Bowls));
            for (int i = 0; i < 72 * 3; i++) { _now += 1.0 / 72.0; _rec.Update(); }
            Assert.That(presence.Wakes.Count, Is.EqualTo(2));
            Assert.That(presence.Wakes[1].kind, Is.EqualTo(InstrumentKind.Marimba));
            Assert.That(2.0 - (presence.Wakes[1].at - start), Is.GreaterThanOrEqualTo(0.25), "at least 250 ms before its event");
        }
    }

    public class EventRolesTests
    {
        [Test]
        public void TheClassicTableIsWhatTheThreeDescriptorsDeclare()
        {
            var reg = InstrumentRegistry.Build(new[] { new PianoDefinition().Describe(), new DrumsDefinition().Describe(), new HarpDefinition().Describe() });
            EventRoles fromRegistry = EventRoles.From(reg), classic = EventRoles.Classic;
            string[] types = { TakeEventType.PianoOn, TakeEventType.PianoOff, TakeEventType.Drum, TakeEventType.DrumChoke, TakeEventType.Hat, TakeEventType.Harp, TakeEventType.HarpDamp };
            foreach (string t in types)
            {
                Assert.That(classic.Spec(t).ToString(), Is.EqualTo(fromRegistry.Spec(t).ToString()), t);
                Assert.That(classic.ReleaseOf(t), Is.EqualTo(fromRegistry.ReleaseOf(t)), t);
                Assert.That(classic.EndsHeld(t), Is.EqualTo(fromRegistry.EndsHeld(t)), t);
            }
            Assert.That(classic.ReleaseOf(TakeEventType.PianoOn), Is.EqualTo(TakeEventType.PianoOff), "the held key pairs with its release");
            Assert.IsTrue(classic.EndsHeld(TakeEventType.PianoOff));
            Assert.IsFalse(classic.EndsHeld(TakeEventType.DrumChoke), "a choke ends a struck note: kept where it happened");
            Assert.That(classic.Sampled, Is.EqualTo(new[] { TakeEventType.Hat }));
            Assert.That(classic.RoleOf("anything else"), Is.EqualTo(EventRole.Onset), "unknown types are Onsets");
        }

        [Test]
        public void LanesPairWithTheirOwnersReleaseAndControlByPrefix()
        {
            var roles = new EventRoles(new[]
            {
                (InstrumentKind.Bowls, new[]
                {
                    new EventTypeSpec("bowlStrike", EventRole.Onset, EventKey.Index), new EventTypeSpec("bowlRubOn", EventRole.Onset, EventKey.Lane),
                    new EventTypeSpec("bowlRubCtl", EventRole.Control, EventKey.Lane), new EventTypeSpec("bowlRubOff", EventRole.Release, EventKey.Lane),
                    new EventTypeSpec("bowlDamp", EventRole.Release, EventKey.Index),
                }),
            });
            Assert.That(roles.ReleaseOf("bowlRubOn"), Is.EqualTo("bowlRubOff"));
            Assert.That(roles.ControlOf("bowlRubOn"), Is.EqualTo("bowlRubCtl"));
            Assert.That(roles.ReleaseOf("bowlRubCtl"), Is.EqualTo("bowlRubOff"));
            Assert.That(roles.ReleaseOf("bowlStrike"), Is.Null, "a struck bowl is not Held: nothing ends it at a loop boundary");
            Assert.That(roles.KindOf("bowlDamp"), Is.EqualTo(InstrumentKind.Bowls));
        }
    }
}
