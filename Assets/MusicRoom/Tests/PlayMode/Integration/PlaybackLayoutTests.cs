// A recording brings its instruments (2026-11-11 headset test: "if i played piano with recordings and moved to drums then
// played drums then stopped. but when played recording it doesnt play as piano is not in front. so either bring the single
// instrument playing or bring all the instruments and let recording play."), with every real module (simulated headset,
// manual frames at 72 Hz, a scripted audio clock), in the VR room:
//   - the reproduction: a piano + drums take from One alone drums, with nothing brought (Move instruments on), plays every
//     event (no event is dropped) but the piano plays on display, metres away, its keys never move;
//   - a piano + drums take played from One alone drums brings both in front of the player as My set (laid out about the
//     player's seat, not where the head looks when PLAY is pressed), both sound and the piano's keys go down; nothing of it
//     is saved; when it ends the player's own One alone drums comes back exactly (pose included) and the piano goes back on
//     display;
//   - a one-instrument take comes alone (One alone) from My set, and My set comes back after;
//   - what the player changes meanwhile stays: a layout chosen in the menu (with their own My set), an instrument moved,
//     Reset view, REC;
//   - a looping playback keeps its instruments until it stops, an overdub onto it too;
//   - a recording whose instruments are all in front already keeps the layout; they step back StageFarther for the show
//     (2026-11-18) and stand exactly where they stood after it;
//   - Sunny is asked for tip_play_brings;
//   - the frames of a stage (its glides in, the wait, the glides back) allocate nothing.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using MusicRoom.Piano;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class PlaybackLayoutTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        double _audio = 10.0;
        AudioTestTap _tap;
        readonly List<PlaybackNoteEvent> _played = new List<PlaybackNoteEvent>();
        int _liveDrums;   // a live drum (the head's turn can nod the kick): its sound is not the take's
        bool _collect = true;

        Recorder Rec => (Recorder)_app.Get<IRecorder>();

        IEnumerator Boot(string mode, InstrumentKind current, string set = null, string tipsSeen = null)
        {
            _audio = 10.0;
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            if (tipsSeen != null) _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, tipsSeen);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, mode);
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(current));
            if (set != null) _boot.Settings.SetString(AppSettings.KeySet, set);
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            Assert.That(_layout, Is.Not.Null);
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(3);
            Settle();
            Assert.IsTrue(_layout.FirstArrangeDone);
            _collect = true;
            _app.Events.Subscribe<PlaybackNoteEvent>(e => { if (_collect) _played.Add(e); });
            _liveDrums = 0;
            _app.Events.Subscribe<RecorderEvent>(e => { if (!e.DuringPlayback && e.Event.type == TakeEventType.Drum) _liveDrums++; });
            _tap = new AudioTestTap();
            if (_app.Get<IAudioEngine>() is AudioEngine engine) engine.TestTap = _tap;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_app?.Get<IAudioEngine>() is AudioEngine engine) engine.TestTap = null;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _layout = null;
            _app = null;
            _played.Clear();
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++) { _audio += Dt; _boot.Step(Dt); }
        }

        void Seconds(float s) => Step(Mathf.Max(1, Mathf.RoundToInt(s / Dt)));

        void Settle()
        {
            for (int i = 0; i < 600 && (_layout.AnyMoving || _layout.Settling || _layout.IsTurning); i++) Step();
            Step(3);
        }

        /// <summary>Runs until the playback has ended and the player's layout is back (or the stage was kept).</summary>
        void UntilBack(float most = 30f)
        {
            int n = Mathf.RoundToInt(most / Dt);
            for (int i = 0; i < n && (Rec.State != RecorderState.Idle || _layout.PlaybackStaged); i++) Step();
            Settle();
        }

        Rig RigOf(InstrumentKind k) => _app.Get<IRigRegistry>().Get(InstrumentIds.RigOf(k));
        PianoInstrument Piano => _app.Get<IPianoApi>() as PianoInstrument;

        /// <summary>A take of the piano (a note every 0.4 s, held 0.3 s) and the drums (snare / kick every 0.4 s), seconds long.</summary>
        static List<TakeEvent> PianoAndDrums(float seconds, bool piano = true, bool drums = true)
        {
            var take = new List<TakeEvent>();
            for (float t = 0.3f; t < seconds; t += 0.4f)
            {
                if (piano)
                {
                    TakeEvent on = TakeEvent.PianoOn(60 + ((int)(t * 5f) % 12), 0.8f); on.t = t; take.Add(on);
                    TakeEvent off = TakeEvent.PianoOff(on.midi); off.t = t + 0.3f; take.Add(off);
                }
                if (drums)
                {
                    TakeEvent d = TakeEvent.Drum(((int)(t * 5f)) % 2 == 0 ? "snare" : "kick", 0.8f, 0.3f, "head", false); d.t = t + 0.05f; take.Add(d);
                }
            }
            take.Sort((a, b) => a.t.CompareTo(b.t));
            return take;
        }

        struct Pose2 { public Vector3 P; public Quaternion R; }

        Dictionary<RigId, Pose2> Poses()
        {
            var d = new Dictionary<RigId, Pose2>();
            foreach (Rig r in _app.Get<IRigRegistry>().Rigs)
                if (r.Group != null && r.Id != RigId.Lessons) d[r.Id] = new Pose2 { P = r.Group.position, R = r.Group.rotation };
            return d;
        }

        void AssertAt(Dictionary<RigId, Pose2> before, RigId id, string why)
        {
            Rig r = _app.Get<IRigRegistry>().Get(id);
            Assert.That(Vector3.Distance(r.Group.position, before[id].P), Is.LessThan(1e-3f), $"{id} back exactly where it stood: {why}");
            Assert.That(Quaternion.Angle(r.Group.rotation, before[id].R), Is.LessThan(0.1f), $"{id} turned as it stood: {why}");
        }

        string Saved => _app.Settings.Store.GetString(AppSettings.KeyLayoutMode, "") + "|" + _app.Settings.Store.GetString(AppSettings.KeyCurrentInstrument, "")
                        + "|" + _app.Settings.Store.GetString(AppSettings.KeySet, "");

        /// <summary>Round the seat the layout was laid out about (within reach); <paramref name="ahead"/>: straight in front
        /// of it (the lead: My set's front slot, or One alone), not turned toward where the head looked.</summary>
        void AssertInFront(InstrumentKind k, string why, bool ahead = true)
        {
            SeatFrame seat = _layout.Seat;
            Rig r = RigOf(k);
            Vector3 d = r.Group.position - seat.Head;
            d.y = 0f;
            if (ahead)
            {
                Assert.That(Vector3.Dot(d, seat.Forward), Is.GreaterThan(0.1f), $"{k} in front of the seat: {why}");
                // in view ahead of the seat (the show's arc since 2026-11-18 puts several side by side within 35 deg)
                Assert.That(Mathf.Abs(Vector3.SignedAngle(seat.Forward, d, Vector3.up)), Is.LessThan(35f), $"{k} ahead, not toward the gaze: {why}");
            }
            Assert.That(d.magnitude, Is.LessThan(_layout.PlaybackStaged ? 3f : 1.6f), $"{k} within reach of the seat (the show: in the room before it): {why}");
        }

        /// <summary>The show's arc (2026-11-18): every staged instrument's play centre within 35 deg of the seat's forward line
        /// and at least 0.6 m farther than where it stood to be played, all side by side (none touches another, none in front of
        /// another's line), the whole arc inside the view (its bodies' edges within 50 deg).</summary>
        void AssertShowArc(RigId[] ids, Dictionary<RigId, Pose2> own, string why)
        {
            SeatFrame seat = _layout.Seat;
            var angles = new List<float>();
            foreach (RigId id in ids)
            {
                Rig r = _app.Get<IRigRegistry>().Get(id);
                RigEntry e = _layout.Placement.Entry(r);
                _layout.Placement.Pose(e);
                Vector3 c = PlacementSolver.CentreWorld(e) - seat.Head;
                c.y = 0f;
                float ang = Vector3.SignedAngle(seat.Forward, c, Vector3.up);
                Vector3 o = own[id].P - seat.Head; o.y = 0f;
                Debug.Log($"[Farther] show {why}: {id} play centre {ang:+0.0;-0.0} deg, {c.magnitude:F2} m (group {o.magnitude:F2} -> {Vector3.Distance(new Vector3(r.Group.position.x, 0f, r.Group.position.z), new Vector3(seat.Head.x, 0f, seat.Head.z)):F2} m)");
                Assert.That(Mathf.Abs(ang), Is.LessThan(35f), $"{id} in view without turning ({ang:F1} deg): {why}");
                Assert.That(Vector3.Distance(new Vector3(r.Group.position.x, 0f, r.Group.position.z), new Vector3(seat.Head.x, 0f, seat.Head.z)) - o.magnitude, Is.GreaterThan(0.5f), $"{id} farther out: {why}");
                foreach (RigId other in ids)
                    if (other != id) Assert.IsFalse(PlacementSolver.Touch(e, _layout.Placement.Entry(_app.Get<IRigRegistry>().Get(other)), 0f), $"{id} clear of {other}: {why}");
                angles.Add(ang);
            }
            angles.Sort();
            for (int i = 1; i < angles.Count; i++) Assert.That(angles[i] - angles[i - 1], Is.GreaterThan(12f), "side by side, not one behind another: " + why);
        }

        // ------------------------------------------------------------------------------------------------ the reproduction

        [UnityTest]
        public IEnumerator BeforeTheFixATakeNeverDroppedNotesButThePianoPlayedFarAwayOnDisplay()
        {
            // Move instruments on: a playback moves nothing (the old behaviour for every playback). One alone drums.
            yield return Boot("single", InstrumentKind.Drums);
            _layout.BeginArrange();
            Step(2);
            Assert.IsTrue(_layout.Arranging);
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Display), "the piano stands on display");
            var take = PianoAndDrums(3f);
            Assert.IsTrue(Rec.PlayEvents(take, 3.5));
            Assert.IsFalse(_layout.PlaybackStaged, "nothing is brought while the player arranges");
            Seconds(1.2f);
            float depth = 0f;
            for (int i = 0; i < 2.2f / Dt; i++) { Step(); depth = Mathf.Max(depth, Piano.Action.Keys.Max(k => k.Depth)); }
            UntilBack();
            int pianoNotes = take.Count(e => e.type == TakeEventType.PianoOn), drumNotes = take.Count(e => e.type == TakeEventType.Drum);
            Assert.That(_played.Count(e => e.Kind == InstrumentKind.Piano && e.Role == EventRole.Onset), Is.EqualTo(pianoNotes), "every piano note was sent (none dropped)");
            Assert.That(_played.Count(e => e.Kind == InstrumentKind.Drums && e.Role == EventRole.Onset), Is.EqualTo(drumNotes));
            Assert.That(_tap.Count(AudioCall.Piano), Is.EqualTo(pianoNotes), "the piano's voice was started for each");
            Transform src = (_app.Get<IAudioEngine>() as AudioEngine).Source(SourceId.PianoMid).transform;
            float far = Vector3.Distance(src.position, _app.Head.position);
            Debug.Log($"[PlayLayout] before: the piano played on display, its sound {far:F2} m from the head, deepest key {depth:F2}");
            Assert.That(far, Is.GreaterThan(1.5f), "but from the display spot, metres away");
            Assert.That(depth, Is.LessThan(0.05f), "and no key moved (a displayed instrument is not ticked)");
            _layout.EndArrange();
        }

        // ------------------------------------------------------------------------------------------------ the stage

        [UnityTest]
        public IEnumerator ATakeOfPianoAndDrumsFromOneAloneBringsBothThenPutsOneAloneBack()
        {
            yield return Boot("single", InstrumentKind.Drums);
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single));
            var own = Poses();
            string saved = Saved;
            SeatFrame seat0 = _layout.Seat;
            var piano = RigOf(InstrumentKind.Piano);
            Vector3 display = piano.Group.position;
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Display));
            // PLAY while looking at the deck, far to the side: the stage is laid out about the seat, not the gaze.
            Quaternion look = _app.Head.rotation;
            _app.Head.rotation = Quaternion.Euler(0f, 70f, 0f) * look;   // a turn, no nod (a nod plays the kick)
            var take = PianoAndDrums(5f);
            Assert.IsTrue(Rec.PlayEvents(take, 5.5));
            Assert.IsTrue(_layout.PlaybackStaged, "the playback brought its instruments");
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.All), "two instruments: as My set");
            CollectionAssert.AreEquivalent(new[] { InstrumentKind.Piano, InstrumentKind.Drums }, _layout.MySet.ToArray());
            Assert.IsTrue(_layout.IsMoving(InstrumentKind.Piano), "the piano glides in from its display spot");
            Assert.That(Saved, Is.EqualTo(saved), "nothing of the stage is saved");
            Assert.That(_layout.StagedKinds, Is.EqualTo((1u << (int)InstrumentKind.Piano) | (1u << (int)InstrumentKind.Drums)));
            float depth = 0f;
            int n = Mathf.RoundToInt(4.5f / Dt);
            for (int i = 0; i < n; i++)
            {
                Step();
                if (_layout.PlaceOf(InstrumentKind.Piano) == InstrumentPlace.Seat) depth = Mathf.Max(depth, Piano.Action.Keys.Max(k => k.Depth));
            }
            _app.Head.rotation = look;
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Seat), "the piano arrived");
            Assert.That(_layout.PlaceOf(InstrumentKind.Drums), Is.EqualTo(InstrumentPlace.Seat));
            Assert.That(Vector3.Distance(piano.Group.position, display), Is.GreaterThan(0.5f), "it left its display spot");
            AssertInFront(InstrumentKind.Piano, "laid out about the seat, not the gaze");
            AssertInFront(InstrumentKind.Drums, "My set's slot about the seat", ahead: false);
            Assert.That(Vector3.Distance(_layout.Seat.Head, seat0.Head), Is.LessThan(1e-4f), "the seat did not move");
            Assert.That(depth, Is.GreaterThan(0.3f), "the piano's keys go down by themselves");
            Assert.That(Saved, Is.EqualTo(saved), "still nothing saved");
            UntilBack();
            int pianoNotes = take.Count(e => e.type == TakeEventType.PianoOn), drumNotes = take.Count(e => e.type == TakeEventType.Drum);
            Assert.That(_tap.Count(AudioCall.Piano), Is.EqualTo(pianoNotes), "every piano note sounded");
            Assert.That(_tap.Count(AudioCall.Drum) - _liveDrums, Is.EqualTo(drumNotes), "every drum hit sounded");
            Assert.IsTrue((_app.Get<IAudioEngine>() as AudioEngine).Source(SourceId.PianoMid).gameObject.activeInHierarchy);
            // the end: One alone drums exactly as it was
            Assert.IsFalse(_layout.PlaybackStaged);
            Assert.That(_layout.StageRestores, Is.EqualTo(1));
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Drums));
            AssertAt(own, RigId.Drums, "after the playback");
            AssertAt(own, RigId.Table, "after the playback");
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Display), "the piano is back on display");
            Assert.That(Vector3.Distance(piano.Group.position, display), Is.LessThan(0.01f), "at its spot");
            Assert.That(Saved, Is.EqualTo(saved));
            Assert.That(Vector3.Distance(_layout.Seat.Head, seat0.Head), Is.LessThan(1e-4f));
        }

        [UnityTest]
        public IEnumerator AOneInstrumentTakeComesAloneAndMySetComesBack()
        {
            yield return Boot("all", InstrumentKind.Drums, set: "drums,harp");
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.All));
            CollectionAssert.AreEquivalent(new[] { InstrumentKind.Drums, InstrumentKind.Harp }, _layout.MySet.ToArray());
            var own = Poses();
            string saved = Saved;
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(3f, drums: false), 3.5));
            Assert.IsTrue(_layout.PlaybackStaged);
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single), "one instrument: alone");
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Piano));
            Settle();
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Seat));
            Assert.That(_layout.PlaceOf(InstrumentKind.Drums), Is.EqualTo(InstrumentPlace.Display), "the others go to their places");
            Assert.That(_layout.PlaceOf(InstrumentKind.Harp), Is.EqualTo(InstrumentPlace.Display));
            AssertInFront(InstrumentKind.Piano, "One alone");
            Assert.That(Saved, Is.EqualTo(saved));
            UntilBack();
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.All));
            CollectionAssert.AreEquivalent(new[] { InstrumentKind.Drums, InstrumentKind.Harp }, _layout.MySet.ToArray());
            AssertAt(own, RigId.Drums, "My set back");
            AssertAt(own, RigId.Harp, "My set back");
            AssertAt(own, RigId.Table, "My set back");
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Display));
            Assert.That(Saved, Is.EqualTo(saved));
        }

        [UnityTest]
        public IEnumerator WhatThePlayerChangesDuringThePlaybackStays()
        {
            yield return Boot("single", InstrumentKind.Drums);
            // 1. They choose One alone harp in the menu: theirs, with their own My set (not the recording's).
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(3f), 3.5));
            Assert.IsTrue(_layout.PlaybackStaged);
            Seconds(1.5f);
            _layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Assert.IsFalse(_layout.PlaybackStaged, "taken over");
            UntilBack();
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Harp), "what they chose stays");
            Assert.That(Saved, Is.EqualTo("single|harp|piano,drums,harp"), "saved, with their own My set");
            Assert.That(_tap.Count(AudioCall.Piano), Is.GreaterThan(0));
            Assert.That(_layout.StageRestores, Is.EqualTo(0));

            // 2. They move an instrument of the stage: the stage stays and becomes their layout.
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(3f), 3.5));
            Assert.IsTrue(_layout.PlaybackStaged);
            Settle();
            Rig pianoRig = RigOf(InstrumentKind.Piano);
            pianoRig.Group.position += _layout.Seat.Right * 0.1f;   // as a Move instruments drag leaves it
            Vector3 moved = pianoRig.Group.position;
            UntilBack();
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.All), "kept");
            CollectionAssert.AreEquivalent(new[] { InstrumentKind.Piano, InstrumentKind.Drums }, _layout.MySet.ToArray());
            Assert.That(Vector3.Distance(pianoRig.Group.position, moved), Is.LessThan(1e-4f), "where they put it");
            Assert.That(Saved, Is.EqualTo("all|harp|piano,drums"), "theirs now");
            Assert.That(_layout.StageKeeps, Is.EqualTo(2));

            // 3. Reset view during a playback: kept.
            _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Settle();
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(3f, drums: false), 3.5));
            Assert.IsTrue(_layout.PlaybackStaged);
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Piano));
            Settle();
            _layout.BringToPlayer();
            Assert.IsFalse(_layout.PlaybackStaged);
            UntilBack();
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Piano), "Reset view keeps the piano in front");
            Assert.That(_layout.StageKeeps, Is.EqualTo(3));

            // 4. REC during a playback: they record with what is in front of them.
            _layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            Settle();
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(3f), 3.5));
            Assert.IsTrue(_layout.PlaybackStaged);
            Seconds(0.5f);
            Rec.Record();
            Assert.That(Rec.State, Is.EqualTo(RecorderState.Recording));
            Assert.IsFalse(_layout.PlaybackStaged, "the instruments stay for the recording");
            Seconds(StageGraceAndGlides);
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.All));
            Assert.IsTrue(_layout.IsInScene(InstrumentKind.Piano) && _layout.IsInScene(InstrumentKind.Drums));
            Rec.Stop();
            Assert.That(_layout.StageKeeps, Is.EqualTo(4));
            Assert.That(_layout.StageRestores, Is.EqualTo(0));
        }

        const float StageGraceAndGlides = LayoutModule.StageEndGrace + 2.5f;

        [UnityTest]
        public IEnumerator ALoopKeepsItsInstrumentsUntilItStopsAndAnOverdubKeepsThem()
        {
            yield return Boot("single", InstrumentKind.Drums, tipsSeen: AppLoopHost.TipLoopStart);
            var own = Poses();
            // A saved loop of one piano layer (90 BPM, 2 bars: 5.33 s a pass).
            var s = new LoopSession(90, 2, 48000);
            var layer = new LoopLayer();
            for (int b = 0; b < 8; b++)
            {
                long at = (long)(s.BeatSamples * b);
                layer.Insert(at, TakeEvent.PianoOn(60 + b, 0.8f));
                layer.Insert(at + (long)(s.BeatSamples * 0.5), TakeEvent.PianoOff(60 + b));
            }
            s.Layers.Add(layer);
            Assert.IsTrue(Rec.PlayEvents(LoopFormat.Encode(s), 0));
            ILooper looper = _app.Get<ILooper>();
            Assert.IsTrue(looper.Active, "it loops");
            Assert.IsTrue(_layout.PlaybackStaged);
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Piano), "one instrument: the piano alone");
            Seconds(12f);   // two passes and more
            Assert.IsTrue(_layout.PlaybackStaged, "a looping playback keeps its instruments");
            Assert.That(_layout.PlaceOf(InstrumentKind.Piano), Is.EqualTo(InstrumentPlace.Seat));
            // an overdub: REC arms the next pass, the loop goes on
            Rec.Record();
            Assert.That(looper.Status, Is.EqualTo(LoopStatus.Armed).Or.EqualTo(LoopStatus.Overdubbing));
            Seconds(7f);
            Assert.IsTrue(looper.Active);
            Assert.IsTrue(_layout.PlaybackStaged, "overdubbing keeps them");
            Assert.That(_played.Count(e => e.Kind == InstrumentKind.Piano && e.Role == EventRole.Onset), Is.GreaterThanOrEqualTo(16), "the loop played on");
            Rec.Stop();
            Assert.IsFalse(looper.Active);
            Seconds(0.5f);
            Assert.IsTrue(_layout.PlaybackStaged, "the last notes ring out first");
            UntilBack();
            Assert.IsFalse(_layout.PlaybackStaged);
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Drums));
            AssertAt(own, RigId.Drums, "after the loop");
        }

        /// <summary>The playing distance and the show's (2026-11-18): One alone piano opens with the keys' near edge about 0.39 m
        /// from the chest; a piano take steps it StageFarther (0.45 m) out while it plays by itself; recording on top of it brings
        /// it back to the playing distance at once (the player plays along); the player's own layout is back after.</summary>
        [UnityTest]
        public IEnumerator AShowStandsFartherAndRecordingOnTopBringsItBackToPlay()
        {
            yield return Boot("single", InstrumentKind.Piano);
            Rig piano = RigOf(InstrumentKind.Piano);
            var own = Poses();
            float play = _layout.ReachOf(piano);
            Debug.Log($"[Farther] piano alone: keys' near edge {play:F3} m from the chest");
            Assert.That(play, Is.InRange(LayoutModule.PlayRoomNear - 0.03f, LayoutModule.ReachBand), "the playing distance");
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(12f, drums: false), 12.5));
            Assert.IsTrue(_layout.PlaybackStaged, "the show");
            Assert.IsTrue(_layout.StageFar);
            Settle();
            float show = _layout.ReachOf(piano);
            Debug.Log($"[Farther] piano while its take plays: {show:F3} m");
            Assert.That(show - play, Is.EqualTo(LayoutModule.StageFarther).Within(0.05f), "StageFarther out for the show");
            AssertShowArc(new[] { RigId.Piano }, own, "the piano alone");
            Seconds(2f);
            Assert.IsTrue(Rec.CanRecordOnTop, "record on top is possible");
            Assert.IsTrue(Rec.RecordOnTop());
            Step(2);
            Assert.IsFalse(_layout.StageFar, "the player plays along");
            Assert.That(_layout.StageNears, Is.EqualTo(1));
            Settle();
            float along = _layout.ReachOf(piano);
            Debug.Log($"[Farther] piano while recording on top: {along:F3} m");
            Assert.That(along, Is.EqualTo(play).Within(0.02f), "back at the playing distance");
            Rec.Stop();
            UntilBack();
            Assert.That(_layout.ReachOf(piano), Is.EqualTo(play).Within(0.02f), "at the playing distance after");
        }

        [UnityTest]
        public IEnumerator ARecordingWhoseInstrumentsAreInFrontStepsBackAndSunnyIsAsked()
        {
            yield return Boot("all", InstrumentKind.Piano);   // the default My set: piano, drums, harp
            var own = Poses();
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(2f), 2.5));
            Assert.IsTrue(_layout.PlaybackStaged, "everything it plays is in front already: they step back for the show");
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.All), "the layout stays");
            Assert.That(_layout.MySet, Is.EquivalentTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }), "the set stays");
            Settle();
            AssertShowArc(new[] { RigId.Piano, RigId.Drums, RigId.Harp }, own, "My set's three");
            AssertAt(own, RigId.Table, "the side table stays");
            UntilBack();
            foreach (RigId id in new[] { RigId.Piano, RigId.Drums, RigId.Harp, RigId.Table }) AssertAt(own, id, "back where it stood");
            Assert.That(_layout.Stagings, Is.EqualTo(1));
            // One alone harp: a piano take brings the piano, and Sunny says it once.
            _layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            Settle();
            Assert.That(MusicRoom.Guide.GuideLines.Has(LayoutModule.StageTipId), Is.True, "the line exists");
            Assert.IsTrue(Rec.PlayEvents(PianoAndDrums(10f, drums: false), 10.5));
            Assert.IsTrue(_layout.PlaybackStaged);
            Seconds(9f);   // Sunny may be saying the listen-back line first
            Assert.IsTrue(_layout.StageTipAsked, "Sunny was asked for tip_play_brings");
            UntilBack();
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Harp));
        }

        [UnityTest]
        public IEnumerator TheFramesOfAStageAllocateNothing()
        {
            yield return Boot("single", InstrumentKind.Drums, tipsSeen: LayoutModule.StageTipId);
            // Warm a whole stage once (first glides, first riser checks).
            uint kinds = (1u << (int)InstrumentKind.Piano) | (1u << (int)InstrumentKind.Drums);
            _app.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = kinds });
            Settle();
            _app.Events.Publish(new RecordingPlaybackEvent { Started = false });
            UntilBack();
            Assert.That(_layout.StageRestores, Is.EqualTo(1));
            _collect = false;
            // The stage, measured on Layout's own frame (every phase it ticks; the app's other modules have their own tests,
            // and the sheet music rasters its pages in the background after a boot): the glides in, the wait, the grace after
            // the end, the glides back.
            _app.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = kinds });
            Assert.IsTrue(_layout.AnyMoving);
            Assert.That(() => { for (int i = 0; i < 150; i++) LayoutFrame(); }, Is.Not.AllocatingGCMemory(), "the glides in and the wait");
            Assert.IsFalse(_layout.AnyMoving, "landed");
            _app.Events.Publish(new RecordingPlaybackEvent { Started = false });
            int grace = Mathf.FloorToInt(LayoutModule.StageEndGrace / Dt) - 3;
            Assert.That(() => { for (int i = 0; i < grace; i++) LayoutFrame(); }, Is.Not.AllocatingGCMemory(), "the grace");
            Assert.IsTrue(_layout.PlaybackStaged);
            for (int i = 0; i < 6 && _layout.PlaybackStaged; i++) LayoutFrame();   // the frame that lays the player's layout out again (once)
            Assert.IsFalse(_layout.PlaybackStaged);
            Assert.IsTrue(_layout.AnyMoving, "gliding back");
            Assert.That(() => { for (int i = 0; i < 150; i++) LayoutFrame(); }, Is.Not.AllocatingGCMemory(), "the glides back");
            Assert.IsFalse(_layout.AnyMoving);
            Assert.That(_layout.StageRestores, Is.EqualTo(2));
        }

        static readonly FramePhase[] LayoutPhases =
            { FramePhase.FirstArrange, FramePhase.Grab, FramePhase.Swivel, FramePhase.TwoHandSummon, FramePhase.PageTurn, FramePhase.LayoutAnimation };

        void LayoutFrame()
        {
            _audio += Dt;
            for (int p = 0; p < LayoutPhases.Length; p++) _layout.Tick(LayoutPhases[p], Dt);
        }
    }
}
