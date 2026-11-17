// The room comes alive for a playback only (2026-11-11 headset test: "butterfly should not appear while recording", then
// "why its glowing when i started recording?"), in the booted app:
//   - plain live play and recording (the tape deck's REC) leave the room calm: no lamp, no motes, no flowers' glow or sway,
//     no butterfly;
//   - playing the take back lifts the lamp, its shade, the fire and candles, the motes, the flowers and brings the
//     butterflies; after its end everything settles to the room's own values exactly and the butterflies are gone;
//   - a looper overdub over a playing loop announces each sound once (played back or recorded, never both) and only the
//     played-back ones reach the room;
//   - the butterflies never come into the head's 0.35 m sphere (their drawn wings included) and land on the room's
//     flowers between the phrases of a playback;
//   - the flowers stand in the room (one petal renderer, their perches, their decor) and go with it in mixed reality,
//     where the butterflies still come around the instruments and the hidden room's lights are left alone;
//   - Sunny says nothing about the room while recording;
//   - a frame of the pulse and the butterflies allocates nothing.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Render;
using MusicRoom.Room.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class RecordingGlowPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        readonly List<RecordedNoteEvent> _recorded = new List<RecordedNoteEvent>();
        readonly List<PlaybackNoteEvent> _played = new List<PlaybackNoteEvent>();
        float _minHead = float.MaxValue;
        bool _collect = true;
        readonly Vector3[] _corners = new Vector3[Butterflies.VertsPer];

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        ListenBackPulse Pulse => Room.ListenBack;
        Butterflies Flies => Room.Butterflies;
        IRecorder Recorder => App.Get<IRecorder>();
        void Frame()
        {
            _audio += Dt;
            _boot.Step(Dt);
            if (Flies == null) return;
            Vector3 head = App.Head.position;
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                ref readonly Butterfly b = ref Flies.Flock.Get(i);
                if (b.Mode == ButterflyMode.Off) continue;
                _minHead = Mathf.Min(_minHead, (b.Pos - head).magnitude);
                if (Flies.WingCorners(i, _corners)) foreach (Vector3 c in _corners) _minHead = Mathf.Min(_minHead, (c - head).magnitude);
            }
        }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        IEnumerator Boot(InstrumentKind current, bool headless = false, string tipsSeen = null)
        {
            _audio = 10.0;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            if (tipsSeen != null) _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, tipsSeen);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(current));
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            _collect = true;
            App.Events.Subscribe<RecordedNoteEvent>(e => { if (_collect) _recorded.Add(e); });
            App.Events.Subscribe<PlaybackNoteEvent>(e => { if (_collect) _played.Add(e); });
            Step(0.2f);
            ParkHands();
            Step(0.1f);
            _minHead = float.MaxValue;
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
            _recorded.Clear();
            _played.Clear();
        }

        void Strikes(int n, float gap)
        {
            IInstrumentFixture fx = FixtureContext.For(InstrumentKind.Piano);
            for (int i = 0; i < n; i++) { fx.Strike(Ctx); Step(Mathf.Max(0f, gap - 16 * Dt)); }
        }

        // ------------------------------------------------------------------------------------------------ the lights

        [UnityTest]
        public IEnumerator RecordingLeavesTheRoomCalmAndThePlaybackLightsIt()
        {
            yield return Boot(InstrumentKind.Piano);
            ListenBackPulse pulse = Pulse;
            Light lamp = Room.Scene.FloorLamp;
            float baseI = pulse.LampBaseIntensity;
            Color baseC = pulse.LampBaseColor;
            Material petals = Room.Scene.Static[RoomMat.Petal].sharedMaterial;
            Color petalBase = petals.GetColor(MaterialLibrary.EmissionColor);

            // Plain live play: calm.
            Strikes(4, 0.3f);
            Step(0.5f);
            Assert.That(_recorded, Is.Empty, "live play outside a recording announces nothing");
            Assert.That(pulse.Model.Notes, Is.EqualTo(0));
            Assert.That(lamp.intensity, Is.EqualTo(baseI), "the lamp is the room's own");
            Assert.That(pulse.MotesReleased, Is.EqualTo(0));
            Assert.That(Flies.Flock.Alive, Is.EqualTo(0), "no butterflies for plain play");
            Assert.That(Flies.Renderer.enabled, Is.False);

            // REC: the room stays calm.
            Recorder.Record();
            Step(0.1f);
            float mostLamp = 0f, mostSway = 0f;
            IInstrumentFixture fx = FixtureContext.For(InstrumentKind.Piano);
            for (int i = 0; i < 12; i++)
            {
                fx.Strike(Ctx);
                mostLamp = Mathf.Max(mostLamp, lamp.intensity);
                mostSway = Mathf.Max(mostSway, Shader.GetGlobalFloat(ListenBackPulse.MusicSwayId));
                Step(0.1f);
            }
            Assert.That(_recorded.Count(e => e.Role == EventRole.Onset), Is.EqualTo(12), "every live onset went into the take, announced once");
            Assert.That(_played, Is.Empty, "nothing was played back");
            Assert.That(pulse.Model.Notes, Is.EqualTo(0), "recording does not reach the room");
            Assert.IsTrue(pulse.Model.Resting);
            Assert.That(mostLamp, Is.EqualTo(baseI), "the lamp is the room's own while recording");
            Assert.That(lamp.color, Is.EqualTo(baseC));
            Assert.That(Room.Scene.Flicker.All(fl => fl.Gain == 1f), Is.True, "fire and candles as they are");
            Assert.That(pulse.MotesReleased, Is.EqualTo(0), "no motes");
            Assert.That(mostSway, Is.EqualTo(0f), "the flowers keep still");
            Assert.That(petals.GetColor(MaterialLibrary.EmissionColor), Is.EqualTo(petalBase), "and do not glow");
            Assert.That(Flies.Flock.Alive, Is.EqualTo(0), "no butterfly while recording");
            Assert.That(Flies.Flock.Spawned, Is.EqualTo(0));
            Assert.That(Flies.Renderer.enabled, Is.False);

            Recorder.Stop();
            Assert.That(Recorder.Take.Count, Is.GreaterThanOrEqualTo(24), "the take has the notes");
            Step(1f);

            // PLAY: the take comes back, and the room comes alive with it.
            Assert.IsTrue(Recorder.Play());
            int most = 0;
            Color peak = baseC;
            for (int i = 0; i < 72 * 2; i++)
            {
                Frame();
                most = Mathf.Max(most, Flies.Flock.Alive);
                if (lamp.intensity > mostLamp) { mostLamp = lamp.intensity; peak = lamp.color; }
                mostSway = Mathf.Max(mostSway, Shader.GetGlobalFloat(ListenBackPulse.MusicSwayId));
            }
            Assert.That(_played.Count, Is.GreaterThan(0), "the take was played back");
            Assert.That(mostLamp, Is.GreaterThan(baseI * 1.2f).And.LessThanOrEqualTo(baseI * (1f + ListenBackPulse.LampBoost) + 1e-4f), "the lamp rises");
            Assert.That(peak.g / peak.r, Is.LessThan(baseC.g / baseC.r - 0.05f), "and leans orange");
            Assert.That(pulse.MotesReleased, Is.GreaterThan(5), "motes over the keys");
            Assert.That(mostSway, Is.GreaterThan(0.2f), "the flowers sway with it");
            Assert.That(most, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount), "the butterflies come for the playback");
            Assert.That(Flies.Renderer.enabled, Is.True, "drawn (one renderer)");
            Recorder.Stop();
            Step(ButterflyFlock.LeaveAfterStop + ButterflyFlock.FadeOut + 1f);
            Assert.IsTrue(pulse.Model.Resting);
            Assert.That(lamp.intensity, Is.EqualTo(baseI), "the lamp's own intensity");
            Assert.That(lamp.color, Is.EqualTo(baseC), "and colour");
            Assert.That(Room.Scene.Flicker.All(fl => fl.Gain == 1f), Is.True);
            Color petalRest = petals.GetColor(MaterialLibrary.EmissionColor);
            Assert.That(Mathf.Abs(petalRest.r - petalBase.r) + Mathf.Abs(petalRest.g - petalBase.g) + Mathf.Abs(petalRest.b - petalBase.b), Is.LessThan(1e-4f), "the petals' own glow");
            Assert.That(Shader.GetGlobalFloat(ListenBackPulse.MusicSwayId), Is.EqualTo(0f), "and their own sway");
            Assert.That(Flies.Flock.Alive, Is.EqualTo(0), "the butterflies are gone");
            Assert.That(Flies.Renderer.enabled, Is.False, "no draw at rest");
            Assert.That(_minHead, Is.GreaterThanOrEqualTo(ButterflyFlock.HeadClear), "never in the head's sphere, wings included");
        }

        [UnityTest]
        public IEnumerator AnOverdubOverAPlayingLoopPulsesEachNoteOnce()
        {
            // Sunny's first-use line for LOOP holds the clicks while she speaks (heard before here).
            yield return Boot(InstrumentKind.Piano, tipsSeen: MusicRoom.Lessons.AppLoopHost.TipLoopStart);
            ILooper looper = App.Get<ILooper>();
            Assert.IsNotNull(looper);
            looper.Configure(90, 2, false);
            var recorder = (MusicRoom.Lessons.Recorder)Recorder;
            recorder.Loop();
            Step(0.4f);
            Strikes(4, 0.6f);                 // layer 1 (its first note starts it)
            int layer1 = _recorded.Count;
            Assert.That(layer1, Is.GreaterThanOrEqualTo(8), "layer 1's notes are recorded");
            // let layer 1 close and loop
            float t0 = 0f;
            while (Recorder.State != RecorderState.Playing && t0 < 10f) { Step(0.1f); t0 += 0.1f; }
            Step(1f);
            Assert.That(_played.Count, Is.GreaterThan(0), "the loop plays layer 1 back");
            int recordedBefore = _recorded.Count;
            Strikes(2, 0.4f);                 // playing along, no layer armed: calm
            Assert.That(_recorded.Count, Is.EqualTo(recordedBefore), "not recorded, not announced");
            // overdub
            Recorder.Record();
            t0 = 0f;
            int before = _recorded.Count;
            while (_recorded.Count == before && t0 < 12f) { Strikes(1, 0.5f); t0 += 0.5f; }
            Assert.That(_recorded.Count, Is.GreaterThan(before), "the overdub's notes are recorded");
            Step(2f);
            Assert.That(Pulse.Model.Notes, Is.EqualTo(_played.Count), "only the played-back sounds reach the room, each once");
            Assert.That(_played.Count, Is.GreaterThan(0));
            Recorder.Stop();
        }

        // ------------------------------------------------------------------------------------------------ the butterflies

        /// <summary>A take of piano phrases: <paramref name="first"/> notes 0.25 s apart, a 7 s rest, then 3 more.</summary>
        static List<TakeEvent> Phrases(int first, out double seconds)
        {
            var take = new List<TakeEvent>();
            float t = 0.2f;
            for (int i = 0; i < first + 3; i++)
            {
                if (i == first) t += 7f;
                TakeEvent on = TakeEvent.PianoOn(60 + (i * 5) % 12, 0.85f); on.t = t; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(on.midi); off.t = t + 0.2f; take.Add(off);
                t += 0.25f;
            }
            seconds = t + 1.0;
            return take;
        }

        [UnityTest]
        public IEnumerator ButterfliesLandOnTheFlowersBetweenPhrasesAndStayClearOfTheHead()
        {
            yield return Boot(InstrumentKind.Piano);
            Assert.IsTrue(Recorder.PlayEvents(Phrases(14, out double seconds), seconds));
            Step(14 * 0.25f + 0.2f);
            Assert.That(Flies.Flock.Alive, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount));
            // a rest in the playback: to the flowers, metres away
            int landed = 0;
            for (int i = 0; i < 6 * 72 && landed == 0; i++) { Frame(); landed = Flies.Flock.Landings; }
            Assert.That(landed, Is.GreaterThan(0), "they land on the flowers between phrases");
            for (int i = 0; i < ButterflyFlock.Max; i++)
            {
                Butterfly b = Flies.Flock.Get(i);
                if (b.Mode != ButterflyMode.Perched) continue;
                Vector3 at = b.Pos;
                float near = Room.Scene.FlowerPerches.Min(p => (p - at).magnitude);
                Assert.That(near, Is.LessThan(1e-3f), $"on a flower head (butterfly {i} at {at}, perch {b.Perch})");
            }
            for (int i = 0; i < 8 * 72 && Pulse.PlaybackModel.SincePlayback > 0.3f; i++) Frame();   // the next phrase
            Step(0.6f);
            Assert.That(Enumerable.Range(0, ButterflyFlock.Max).Count(i => Flies.Flock.Get(i).Mode == ButterflyMode.Perched), Is.EqualTo(0), "the music lifts them off");
            Assert.That(_minHead, Is.GreaterThanOrEqualTo(ButterflyFlock.HeadClear), "never in the head's sphere, wings included");
            Recorder.Stop();
        }

        [UnityTest]
        public IEnumerator FlowersStandInTheRoomAndInMixedRealityOnlyTheButterfliesCome()
        {
            yield return Boot(InstrumentKind.Drums);
            RoomScene s = Room.Scene;
            Assert.IsTrue(s.Static.ContainsKey(RoomMat.Petal), "every petal in one renderer");
            Renderer petals = s.Static[RoomMat.Petal];
            Assert.That(petals.sharedMaterial.IsKeywordEnabled("_SWAY"), Is.True);
            Assert.That(s.Bouquets.Count, Is.EqualTo(4), "tulips, daisies, geraniums, cosmos");
            int flowers = FlowerBuilder.Tulips.Count + FlowerBuilder.Daisies.Count + FlowerBuilder.Geraniums.Count + FlowerBuilder.CosmosJar.Count;
            Assert.That(s.FlowerPerches.Count, Is.EqualTo(flowers), "a perch per flower");
            Bounds box = GrabConstants.RoomBox;
            foreach (Vector3 p in s.FlowerPerches) Assert.IsTrue(box.Contains(p), "inside the room: " + p);
            foreach (string decor in new[] { RoomFurniture.MantelFlowers, RoomFurniture.SillFlowers, RoomFurniture.ShelfFlowers })
                Assert.IsTrue(Room.Furniture.TryGet(decor, out _), decor + " is published as decor");
            Assert.That(petals.bounds.size.magnitude, Is.GreaterThan(3f), "spread over the room");

            // Mixed reality: the room (and its flowers) hidden; the butterflies still come, around the kit.
            Room.SetVisible(false);
            Step(0.1f);
            Assert.That(petals.gameObject.activeInHierarchy, Is.False, "the flowers go with the room");
            Light lamp = s.FloorLamp;
            float before = lamp.intensity;
            var take = new List<TakeEvent>();
            for (int i = 0; i < 40; i++) { TakeEvent e = TakeEvent.Drum(i % 2 == 0 ? "snare" : "kick", 0.85f, 0.3f, "head", false); e.t = 0.02f + 0.15f * i; take.Add(e); }
            Assert.IsTrue(Recorder.PlayEvents(take, 6.2));
            Step(4f);
            Assert.That(Flies.Flock.Alive, Is.GreaterThanOrEqualTo(ButterflyFlock.MinCount), "they come in mixed reality too");
            Assert.That(Flies.Flock.PerchCount, Is.EqualTo(0), "no flowers to land on");
            Assert.That(Flies.Renderer.gameObject.activeInHierarchy && Flies.Renderer.enabled, Is.True, "drawn outside the hidden room");
            Assert.IsTrue(Pulse.TryPlace(InstrumentKind.Drums, out Vector3 kit, out _));
            int near = Enumerable.Range(0, ButterflyFlock.Max).Count(i => Flies.Flock.Get(i).Mode != ButterflyMode.Off && (Flies.Flock.Get(i).Pos - kit).magnitude < 2f);
            Assert.That(near, Is.GreaterThanOrEqualTo(Flies.Flock.Alive / 2), "around the kit");
            Assert.That(lamp.intensity, Is.EqualTo(before), "the hidden room's lights are left alone");
            Recorder.Stop();
            Step(ButterflyFlock.LeaveAfterStop + ButterflyFlock.FadeOut + 0.5f);
            Assert.That(Flies.Flock.Alive, Is.EqualTo(0));
            Assert.That(_minHead, Is.GreaterThanOrEqualTo(ButterflyFlock.HeadClear));
            Room.SetVisible(true);
        }

        // ------------------------------------------------------------------------------------------------ Sunny, allocation

        [UnityTest]
        public IEnumerator SunnySaysNothingAboutTheRoomWhileRecording()
        {
            yield return Boot(InstrumentKind.Piano, headless: true);
            Recorder.Record();
            Strikes(11, 0.3f);
            Assert.IsFalse(Pulse.TipAsked, "the listen-back line is for playback");
            Assert.IsTrue(Pulse.Model.Resting, "the room is calm");
            Recorder.Stop();
        }

        [UnityTest]
        public IEnumerator APulseAndButterflyFrameAllocatesNothing()
        {
            // Sunny's line heard before (asking her is a one-off with its own log line, not a frame's cost; the ask itself is
            // ListenBackPlayTests'). The recorded notes still go out on the bus: they must cost nothing either.
            yield return Boot(InstrumentKind.Piano, tipsSeen: ListenBackPulse.TipId);
            ListenBackPulse pulse = Pulse;
            Butterflies flies = Flies;
            EventBus bus = App.Events;
            var on = new RecordedNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f };
            var off = new RecordedNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Release, Level = 0f };
            var played = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.7f, Lane = 1 };
            bool visible = true;
            void Tick() { _audio += Dt; pulse.Tick(Dt, visible); flies.Tick(Dt, visible); }
            // Warm: butterflies out, Sunny asked (a one-off with its own log line), pauses, landings, MR.
            for (int i = 0; i < 72 * 40 && !pulse.TipAsked; i++) { if (i % 9 == 0) bus.Publish(on); if (i % 9 == 4) bus.Publish(off); if (i % 13 == 0) bus.Publish(played); Tick(); }
            for (int i = 0; i < 300; i++) { if (i % 9 == 0) bus.Publish(on); if (i % 13 == 0) bus.Publish(played); Tick(); }
            for (int i = 0; i < 300; i++) Tick();
            visible = false;
            for (int i = 0; i < 200; i++) { if (i % 9 == 0) bus.Publish(on); Tick(); }
            visible = true;
            Assert.IsTrue(pulse.TipAsked, "the one-off ask is behind us");
            Assert.That(flies.Flock.Spawned, Is.GreaterThan(0));
            var engine = App.Get<MusicRoom.Audio.IAudioEngine>() as MusicRoom.Audio.AudioEngine;
            float t0 = Time.realtimeSinceStartup;
            while (engine != null && (!engine.VoiceLineReady(ListenBackPulse.TipId) || !engine.Renders.IsDone)
                   && Time.realtimeSinceStartup - t0 < 15f) yield return null;
            _collect = false;   // this test's own event lists would grow (and allocate) while measuring
            int frame = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 400; i++)
                {
                    frame++;
                    if (frame % 9 == 0) bus.Publish(on);
                    if (frame % 9 == 4) bus.Publish(off);
                    if (frame % 13 == 0) bus.Publish(played);
                    Tick();
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(flies.Flock.Alive, Is.GreaterThan(0), "measured with butterflies out");
        }
    }
}
