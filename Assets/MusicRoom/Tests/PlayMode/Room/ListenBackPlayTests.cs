// Listening back (the Entertainment track's music visualization), in the booted app:
//   - every instrument, alone at the seat, shows the note it replays as the live gesture did: its fixture's live onset is
//     recorded, everything settles, then a take of that one event plays and something of the instrument moves that did
//     not move while it sat still (keys go down, strings shake, heads dent, cymbals and bowls rock, bars dip, the shell
//     settles, the needle swings). Checked on the instrument's own transforms and mesh data, frame by frame;
//   - the bowls' replayed rub sways the bowl (its live visual is a stick the playback does not have);
//   - an old take with events of a retired instrument plays the rest and skips those, without an error;
//   - the room pulses on playback only: live play never lifts it; a take lifts the lamp, its shade, the fire and candles
//     and releases motes over the instrument; after STOP all of it settles to the room's own values exactly; in mixed
//     reality (the room hidden) the motes still rise and the hidden lights are left alone;
//   - Sunny is asked for tip_listen_back once a playback has sounded for a moment;
//   - a pulse frame (notes in, levels, motes, lights) allocates nothing.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Bowls;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class ListenBackPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        readonly List<RecorderEvent> _events = new List<RecorderEvent>();

        public static IEnumerable<InstrumentKind> Kinds()
        {
            foreach (InstrumentDescriptor d in InstrumentRegistry.Discover().All) if (d.Playable) yield return d.Kind;
        }

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        ListenBackPulse Pulse => Room.ListenBack;
        IRecorder Recorder => App.Get<IRecorder>();
        IInstrument Inst(InstrumentKind k) => App.Instruments.FirstOrDefault(i => i.Kind == k);
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        IEnumerator Boot(InstrumentKind current, bool headless = true)
        {
            _audio = 10.0;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(current));
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            App.Events.Subscribe<RecorderEvent>(e => _events.Add(e));
            Step(0.2f);
            ParkHands();
            Step(0.1f);
        }

        void ParkHands()
        {
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(App.Head.position + Vector3.up * 0.6f));
            _boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, App.Head.position + Vector3.up * 0.6f + Vector3.left * 0.3f, Vector3.down, Vector3.forward));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _events.Clear();
        }

        // ------------------------------------------------------------------------------------------------ the visual signature

        /// <summary>What can be seen of an instrument, one number per part: every transform's local pose and active flag, every
        /// readable mesh's vertices and its first two UV channels (the glows and lamps live there).</summary>
        static List<double> Signature(Transform root)
        {
            var sig = new List<double>(256);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                Vector3 p = t.localPosition, s = t.localScale; Quaternion q = t.localRotation;
                sig.Add(p.x * 1.1 + p.y * 2.3 + p.z * 3.7 + q.x * 5.1 + q.y * 6.7 + q.z * 7.3 + q.w * 8.9 + s.x + s.y * 1.3 + s.z * 1.7 + (t.gameObject.activeSelf ? 0 : 1000));
            }
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = f.sharedMesh;
                if (m == null || !m.isReadable) { sig.Add(0); continue; }
                double v = 0;
                Vector3[] vs = m.vertices;
                for (int i = 0; i < vs.Length; i++) v += vs[i].x * (1 + (i % 7)) + vs[i].y * (2 + (i % 5)) + vs[i].z * (3 + (i % 3));
                sig.Add(v);
                for (int ch = 0; ch < 2; ch++)
                {
                    var uv = new List<Vector4>();
                    m.GetUVs(ch, uv);
                    double u = 0;
                    for (int i = 0; i < uv.Count; i++) u += uv[i].x * (1 + (i % 7)) + uv[i].y * (2 + (i % 5)) + uv[i].z * 3 + uv[i].w * 5;
                    sig.Add(u);
                }
            }
            return sig;
        }

        static int Differs(List<double> a, List<double> b, bool[] ignore)
        {
            int n = Mathf.Min(a.Count, b.Count), c = 0;
            for (int i = 0; i < n; i++) if ((ignore == null || !ignore[i]) && System.Math.Abs(a[i] - b[i]) > 1e-6 * (1 + System.Math.Abs(a[i]))) c++;
            return c + System.Math.Abs(a.Count - b.Count);
        }

        // ------------------------------------------------------------------------------------------------ every instrument

        [UnityTest]
        public IEnumerator PlaybackShowsWhatTheLiveGestureShowed([ValueSource(nameof(Kinds))] InstrumentKind kind)
        {
            yield return Boot(kind);
            InstrumentDescriptor d = App.Registry.Get(kind);
            IInstrumentFixture fx = FixtureContext.For(kind);
            Assert.IsNotNull(fx, d.Id + ": a fixture");
            var onsets = new HashSet<string>(d.Events.Where(e => e.Role == EventRole.Onset).Select(e => e.Type));
            _events.Clear();
            fx.Strike(Ctx);
            RecorderEvent hit = _events.FirstOrDefault(e => !e.DuringPlayback && onsets.Contains(e.Event.type));
            Assert.IsNotNull(hit.Event.type, d.Id + ": the fixture's live onset");
            Assert.That(Pulse.Model.Notes, Is.EqualTo(0), d.Id + ": live play never reaches the listen-back pulse");
            ParkHands();
            // A plucked harp string rings for a long time: quiet it, as a hand laid on the strings would.
            IHarpApi harp = kind == InstrumentKind.Harp ? App.Get<IHarpApi>() : null;
            if (harp != null) for (int i = 0; i < harp.StringCount; i++) harp.PlaybackDamp(i);

            // Settle: the live gesture's motion dies out (at most 8 s).
            Transform root = Inst(kind).Root;
            List<double> prev = Signature(root);
            int still = 0;
            for (int f = 0; f < 8 * 72 && still < 36; f++)
            {
                Frame();
                List<double> now = Signature(root);
                still = Differs(prev, now, null) == 0 ? still + 1 : 0;
                prev = now;
            }
            // What moves on its own (an idle lamp, a breathing animation) does not count.
            var ignore = new bool[prev.Count];
            List<double> rest = prev;
            for (int f = 0; f < 30; f++)
            {
                Frame();
                List<double> now = Signature(root);
                for (int i = 0; i < Mathf.Min(now.Count, ignore.Length); i++) if (System.Math.Abs(now[i] - rest[i]) > 1e-6 * (1 + System.Math.Abs(rest[i]))) ignore[i] = true;
            }
            rest = Signature(root);

            TakeEvent ev = hit.Event;
            // The harp: the recorded stroke on a string three away from the one the hand plucked, which may still ring.
            if (harp != null) ev.i = (ev.i + 3) % harp.StringCount;
            ev.t = 0.03f;
            ev.lane = 0;
            Assert.IsTrue(Recorder.PlayEvents(new List<TakeEvent> { ev }, 1.5), d.Id + ": the take plays");
            int moved = 0;
            float peak = 0f;
            // The harp's strings are one mesh whose ringing follows the audio engine's measured string amplitude (sympathetic
            // ringing included, on the audio thread's own clock), so it may never sit still here: for it the replayed
            // string's own swing (the blur that is drawn) counts as well.
            MusicRoom.Harp.HarpString str = kind == InstrumentKind.Harp ? ((MusicRoom.Harp.HarpInstrument)Inst(kind)).Play.Strings[ev.i] : null;
            float swingBefore = str != null ? str.BlurSwing : 0f, swing = 0f;
            for (int f = 0; f < 45; f++)
            {
                Frame();
                moved = Mathf.Max(moved, Differs(rest, Signature(root), ignore));
                peak = Mathf.Max(peak, Pulse.Model.Level(kind));
                if (str != null) swing = Mathf.Max(swing, str.BlurSwing);
            }
            if (str != null && moved == 0)
                Assert.That(swing, Is.GreaterThan(2f * swingBefore + 1e-5f), d.Id + ": the replayed string shakes (" + swingBefore + " before)");
            else Assert.That(moved, Is.GreaterThan(0), d.Id + ": the replayed " + ev.type + " shows on the instrument");
            Assert.That(Pulse.Model.Notes, Is.GreaterThanOrEqualTo(1), d.Id + ": the pulse heard the playback");
            Assert.That(peak, Is.GreaterThan(0.02f), d.Id + ": its level rose");
            Assert.That(Pulse.MotesReleased, Is.GreaterThan(0), d.Id + ": motes rose over it");
            Debug.Log($"[ListenBack] {d.Id}: {ev.type} moved {moved} parts, level peak {peak:F2}, motes {Pulse.MotesReleased}");
        }

        [UnityTest]
        public IEnumerator AReplayedRubSwaysTheBowlAndSettles()
        {
            yield return Boot(InstrumentKind.Bowls);
            var bowls = (BowlsInstrument)Inst(InstrumentKind.Bowls);
            var pb = (IInstrumentPlayback)bowls;
            Assert.That(bowls.View.HumDegrees(1), Is.EqualTo(0f));
            Quaternion rest = bowls.View.Bowl[1].localRotation;
            var on = new TakeEvent { type = BowlsRubEncoder.On, i = 1, zone = "outer", vel = 0.8f, u = 0.25f, midi = 500, pos = 0.4f, lane = PlaybackLanes.Take };
            Assert.IsTrue(pb.Play(on));
            float most = 0f;
            for (int f = 0; f < 60; f++)
            {
                if (f % 6 == 0) { var ctl = on; ctl.type = BowlsRubEncoder.Ctl; ctl.midi = 500 + f * 40; ctl.speed = 0.03f; pb.Play(ctl); }
                Frame();
                most = Mathf.Max(most, bowls.View.HumDegrees(1));
            }
            Assert.That(most, Is.GreaterThan(0.25f).And.LessThanOrEqualTo(BowlsVisuals.HumMaxDeg), "the replayed rub sways the bowl, gently");
            Assert.That(bowls.View.HumDegrees(0), Is.EqualTo(0f), "only that bowl");
            Quaternion swayed = bowls.View.Bowl[1].localRotation;
            pb.Play(new TakeEvent { type = BowlsRubEncoder.Off, i = 1, zone = "outer", lane = PlaybackLanes.Take });
            Step(2f);
            Assert.That(bowls.View.HumDegrees(1), Is.EqualTo(0f), "the release lets it settle");
            Assert.That(Quaternion.Angle(swayed, rest), Is.GreaterThan(0.1f), "it had leaned");
            Assert.That(Quaternion.Angle(bowls.View.Bowl[1].localRotation, rest), Is.LessThan(0.001f), "back exactly at rest");
        }

        [UnityTest]
        public IEnumerator AnOldTakeWithARetiredInstrumentsEventsPlaysTheRestWithoutAnError()
        {
            // A take saved before the theremin was removed (2026-11-10): its events have no instrument now and are skipped;
            // the piano's notes in the same take still play, and nothing is logged as an error.
            yield return Boot(InstrumentKind.Piano);
            var take = new List<TakeEvent>
            {
                new TakeEvent { t = 0.02f, type = "thereminOn", u = 69, vel = 0.85f },
                new TakeEvent { t = 0.05f, type = "thereminCtl", u = 69.5f, vel = 0.8f, speed = 0.02f },
            };
            TakeEvent on = TakeEvent.PianoOn(64, 0.8f); on.t = 0.1f; take.Add(on);
            take.Add(new TakeEvent { t = 0.3f, type = "thereminHold", midi = 69 });
            TakeEvent off = TakeEvent.PianoOff(64); off.t = 0.5f; take.Add(off);
            take.Add(new TakeEvent { t = 0.6f, type = "thereminOff" });
            _events.Clear();
            var errors = new List<string>();
            Application.LogCallback onLog = (m, st, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(m); };
            Application.logMessageReceived += onLog;
            try
            {
                Assert.IsTrue(Recorder.PlayEvents(take, 1.0), "the take plays");
                Step(1.5f);
            }
            finally { Application.logMessageReceived -= onLog; }
            Assert.That(errors, Is.Empty, "nothing logged as an error");
            Assert.That(_events.Count(e => e.DuringPlayback && e.Event.type == TakeEventType.PianoOn), Is.EqualTo(1), "the piano's note played");
        }

        // ------------------------------------------------------------------------------------------------ the room

        static List<TakeEvent> Song(int notes, float gap, float vel)
        {
            var take = new List<TakeEvent>();
            for (int i = 0; i < notes; i++)
            {
                TakeEvent on = TakeEvent.PianoOn(60 + 2 * (i % 6), vel); on.t = 0.02f + i * gap; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(60 + 2 * (i % 6)); off.t = 0.02f + i * gap + gap * 0.8f; take.Add(off);
            }
            return take;
        }

        [UnityTest]
        public IEnumerator TheRoomPulsesOnPlaybackOnlyAndSettlesAfterStop()
        {
            yield return Boot(InstrumentKind.Piano, headless: false);
            ListenBackPulse pulse = Pulse;
            Light lamp = Room.Scene.FloorLamp;
            float baseI = pulse.LampBaseIntensity;
            Color baseC = pulse.LampBaseColor;
            Assert.That(lamp.intensity, Is.EqualTo(baseI));

            // Live play: nothing.
            _events.Clear();
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Assert.That(_events.Any(e => !e.DuringPlayback && e.Event.type == TakeEventType.PianoOn), Is.True, "the live note was played");
            Step(0.3f);
            Assert.That(pulse.Model.Notes, Is.EqualTo(0), "no pulse from live play");
            Assert.That(pulse.Model.Room, Is.EqualTo(0f));
            Assert.That(lamp.intensity, Is.EqualTo(baseI), "the lamp is the room's own");
            Assert.That(pulse.MotesReleased, Is.EqualTo(0));
            ParkHands();

            // A take: the room rises with it.
            Assert.IsTrue(Recorder.PlayEvents(Song(12, 0.15f, 0.9f), 2.0));
            float most = 0f, mostLamp = 0f;
            Color peakColor = baseC;
            for (int f = 0; f < 72; f++)
            {
                Frame();
                most = Mathf.Max(most, pulse.Model.Room);
                if (lamp.intensity > mostLamp) { mostLamp = lamp.intensity; peakColor = lamp.color; }
            }
            Assert.That(most, Is.GreaterThan(0.6f), "the room's level follows the notes");
            Assert.That(mostLamp, Is.GreaterThan(baseI * 1.2f).And.LessThanOrEqualTo(baseI * (1f + ListenBackPulse.LampBoost) + 1e-4f), "the lamp rises, gently");
            Assert.That(Room.Scene.Flicker.All(fl => fl.Gain > 1f), Is.True, "fire and candles rise too");
            // the playback glow is orange: the lamp leans toward it (less green and blue for its red than its own colour)
            Assert.That(peakColor.g / peakColor.r, Is.LessThan(baseC.g / baseC.r - 0.05f), "the lamp tints orange (green)");
            Assert.That(peakColor.b / peakColor.r, Is.LessThan(baseC.b / baseC.r - 0.05f), "the lamp tints orange (blue)");
            Assert.That(pulse.MotesReleased, Is.GreaterThan(5), "motes rise over the keys");
            Assert.That(pulse.Motes, Is.Not.Null);
            Assert.That(pulse.Motes.particleCount, Is.GreaterThan(0), "drawn by the one particle system");
            Bounds keys = Inst(InstrumentKind.Piano).PlayedParts.Where(r => r.enabled).Aggregate(new Bounds(), (b, r) => { if (b.size == Vector3.zero) return r.bounds; b.Encapsulate(r.bounds); return b; });
            var parts = new ParticleSystem.Particle[pulse.Motes.particleCount];
            pulse.Motes.GetParticles(parts);
            Vector3 c = parts.Aggregate(Vector3.zero, (s, p) => s + p.position) / parts.Length;
            Assert.That(new Vector2(c.x - keys.center.x, c.z - keys.center.z).magnitude, Is.LessThan(0.8f), "over the piano");
            Assert.That(c.y, Is.GreaterThan(keys.center.y), "above the keys");
            Color32 glow = ListenBackModel.Glow;
            foreach (var p in parts)
            {
                Color32 pc = p.startColor;
                Assert.That(new[] { pc.r, pc.g, pc.b }, Is.EqualTo(new[] { glow.r, glow.g, glow.b }), "every mote is the orange glow");
            }

            // STOP: back to the room's own light, exactly.
            Recorder.Stop();
            Step(5f);
            Assert.IsTrue(pulse.Model.Resting, "at rest after the stop");
            Assert.That(pulse.Shown, Is.EqualTo(0f));
            Assert.That(lamp.intensity, Is.EqualTo(baseI), "the lamp's own intensity");
            Assert.That(lamp.color, Is.EqualTo(baseC), "and colour");
            Assert.That(Room.Scene.Flicker.All(fl => fl.Gain == 1f), Is.True, "the flicker's own");
            pulse.Motes.Simulate(ListenBackPulse.LifeMax + 0.1f, true, false);   // manual frames do not age the particles
            Assert.That(pulse.Motes.particleCount, Is.EqualTo(0), "the last motes have faded");
        }

        [UnityTest]
        public IEnumerator InMixedRealityOnlyTheMotesRise()
        {
            yield return Boot(InstrumentKind.Drums);
            Room.SetVisible(false);   // what mixed reality does to the room
            Step(0.1f);
            Light lamp = Room.Scene.FloorLamp;
            float before = lamp.intensity;
            var take = new List<TakeEvent>();
            for (int i = 0; i < 8; i++) { TakeEvent e = TakeEvent.Drum(i % 2 == 0 ? "snare" : "crash", 0.8f, 0.3f, "head", false); e.t = 0.02f + 0.12f * i; take.Add(e); }
            Assert.IsTrue(Recorder.PlayEvents(take, 1.2));
            Step(0.8f);
            Assert.That(Pulse.Model.Level(InstrumentKind.Drums), Is.GreaterThan(0.1f));
            Assert.That(Pulse.MotesReleased, Is.GreaterThan(0), "the motes rise over the kit");
            Assert.That(lamp.intensity, Is.EqualTo(before), "the hidden room's lights are left alone");
            Assert.That(Pulse.Shown, Is.EqualTo(0f));
            Room.SetVisible(true);
            Recorder.Stop();
            Step(5f);
            Assert.That(lamp.intensity, Is.EqualTo(Pulse.LampBaseIntensity));
        }

        [UnityTest]
        public IEnumerator SunnyIsAskedToSayItOnceAPlaybackHasSounded()
        {
            yield return Boot(InstrumentKind.Piano);
            Assert.That(GuideLinesHas(ListenBackPulse.TipId), Is.True, "the line exists");
            Assert.IsFalse(Pulse.TipAsked);
            Assert.IsTrue(Recorder.PlayEvents(Song(30, 0.15f, 0.7f), 5.0));
            Step(0.8f);
            Assert.IsFalse(Pulse.TipAsked, "not on the first note");
            Step(1.5f);
            Assert.IsTrue(Pulse.TipAsked, "asked once it has sounded for a moment");
            Step(0.3f);
            Assert.IsTrue(App.Get<IGuide>().Present, "Sunny comes to say it");
        }

        static bool GuideLinesHas(string id) => global::MusicRoom.Guide.GuideLines.Has(id);

        [UnityTest]
        public IEnumerator APulseFrameAllocatesNothing()
        {
            yield return Boot(InstrumentKind.Piano, headless: false);
            ListenBackPulse pulse = Pulse;
            EventBus bus = App.Events;
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.8f, Lane = PlaybackLanes.Take };
            var ctl = new PlaybackNoteEvent { Kind = InstrumentKind.Bowls, Role = EventRole.Control, Level = 0.5f, Lane = PlaybackLanes.Take };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 1f, Lane = 1 };
            int frame = 0;
            // Warm: every kind placed, and Sunny asked for the tip once (a one-off with its own log line, not a frame's cost).
            for (int i = 0; i < 200; i++) { bus.Publish(on); bus.Publish(drum); pulse.Tick(Dt, true); _audio += Dt; }
            Assert.That(pulse.MotesReleased, Is.GreaterThan(0));
            Assert.IsTrue(pulse.TipAsked, "the one-off ask is behind us");
            // Warm the measured frame's own mix too (it was not before 2026-11-11): the bowl's held lane (its first motes
            // measure the bowls' played parts once, and only when the layout shows them, which varies with the boot) and a
            // note waiting in the pending ring. First uses, not a frame's cost.
            for (int i = 0; i < 200; i++)
            {
                if (i % 7 == 0) { drum.When = _audio + 0.05; bus.Publish(drum); }
                bus.Publish(ctl);
                _audio += Dt;
                pulse.Tick(Dt, true);
            }
            // Sunny's line (asked once above) renders on a background worker (GuideVoice's lazy render, OfflineRenderQueue):
            // its buffers are allocated there while it runs, and a measurement overlapping it failed now and then in full
            // runs (2 of 5 on 2026-11-10/11, never alone) although the frame itself allocates nothing (two full runs measured
            // each part of it: 0 GC.Alloc samples). So the measurement starts once the line and the boot's renders are done.
            var engine = App.Get<MusicRoom.Audio.IAudioEngine>() as MusicRoom.Audio.AudioEngine;
            float t0 = Time.realtimeSinceStartup;
            while (engine != null && (!engine.VoiceLineReady(ListenBackPulse.TipId) || !engine.Renders.IsDone) && Time.realtimeSinceStartup - t0 < 15f)
                yield return null;
            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    frame++;
                    if (frame % 4 == 0) bus.Publish(on);
                    if (frame % 7 == 0) { drum.When = _audio + 0.05; bus.Publish(drum); }
                    bus.Publish(ctl);
                    _audio += Dt;
                    pulse.Tick(Dt, true);
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
