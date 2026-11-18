// Ghost hands play it back (Lessons/Ghost), in the booted app:
//   - they come only for a playback: plain live play and REC show none and plan nothing; a take brings them;
//   - they reach the right key / drum / string at the moment it sounds (a finger on the piano key, the stick's tip on the
//     piece's face, a fingertip at the harp string), frame by frame through a take;
//   - they never play: no collider, no pointer of theirs, and the instrument plays exactly the take's notes;
//   - they fade out after STOP and are gone (nothing drawn, nothing planned);
//   - the player's own hand on the same place turns the ghost hand faint;
//   - an instrument that is not shown (put away during the playback) loses its hands; the others keep theirs;
//   - Sunny is asked for tip_ghost_hands once they have played for a moment;
//   - a ghost frame (read ahead, plan, pose, mesh) allocates nothing.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons.Ghost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class GhostHandsPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        readonly List<RecorderEvent> _events = new List<RecorderEvent>();

        App App => App.Current;
        GhostHands Ghost => GhostHandsModule.Of(App);
        IRecorder Recorder => App.Get<IRecorder>();
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }

        IEnumerator Boot(InstrumentKind current, bool headless = true, string mode = "single", string seen = null)
        {
            _audio = 10.0;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, mode);
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(current));
            if (seen != null) _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, seen);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            App.Events.Subscribe<RecorderEvent>(e => _events.Add(e));
            Step(0.2f);
            ParkHands();
            Step(0.1f);
        }

        /// <summary>The player's hands up out of the way, above the head.</summary>
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

        // ------------------------------------------------------------------------------------------------ takes

        public static List<TakeEvent> PianoSong(float from, int bars = 4)
        {
            var take = new List<TakeEvent>();
            int[] tune = { 60, 64, 67, 72, 71, 67, 65, 62, 64, 60, 62, 59 };
            int[] bass = { 48, 43, 45, 41 };
            float t = from;
            for (int b = 0; b < bars; b++)
            {
                Add(take, bass[b % bass.Length], t, 1.1f, 0.6f);
                Add(take, bass[b % bass.Length] + 7, t, 1.1f, 0.5f);
                for (int i = 0; i < 4; i++) Add(take, tune[(b * 4 + i) % tune.Length], t + 0.3f * i, 0.26f, 0.8f);
                t += 1.2f;
            }
            take.Sort((a, c) => a.t.CompareTo(c.t));
            return take;
        }

        static void Add(List<TakeEvent> take, int midi, float t, float len, float vel)
        {
            TakeEvent on = TakeEvent.PianoOn(midi, vel); on.t = t; take.Add(on);
            TakeEvent off = TakeEvent.PianoOff(midi); off.t = t + len; take.Add(off);
        }

        public static List<TakeEvent> DrumBeat(float from, int bars = 3)
        {
            var take = new List<TakeEvent>();
            float t = from;
            for (int b = 0; b < bars * 8; b++, t += 0.25f)
            {
                TakeEvent hat = TakeEvent.Drum("hat", 0.6f, 0.5f, "bow", false); hat.t = t; take.Add(hat);
                if (b % 4 == 2) { TakeEvent sn = TakeEvent.Drum("snare", 0.9f, 0.3f, "head", false); sn.t = t; take.Add(sn); }
                if (b % 8 == 7) { TakeEvent tom = TakeEvent.Drum("floor", 0.8f, 0.3f, "head", false); tom.t = t + 0.125f; take.Add(tom); }
                if (b % 8 == 0) { TakeEvent k = TakeEvent.Drum("kick", 0.9f, 0f, "head", false); k.t = t; take.Add(k); }
            }
            take.Sort((a, c) => a.t.CompareTo(c.t));
            return take;
        }

        public static List<TakeEvent> HarpRun(float from, int notes = 12)
        {
            var take = new List<TakeEvent>();
            int[] strings = { 7, 9, 11, 14, 11, 9, 4, 6, 8, 11, 13, 15 };
            for (int i = 0; i < notes; i++)
            {
                TakeEvent p = TakeEvent.Harp(strings[i % strings.Length], 0.7f, 0.35f, 0.4f, "through"); p.t = from + 0.35f * i; take.Add(p);
            }
            return take;
        }

        // ------------------------------------------------------------------------------------------------ playback only

        [UnityTest]
        public IEnumerator TheyComeOnlyForAPlayback()
        {
            yield return Boot(InstrumentKind.Piano);
            GhostHands g = Ghost;
            Assert.IsNotNull(g, "the ghost hands module runs");
            var piano = App.Get<IPianoApi>();
            // plain live play
            for (int i = 0; i < 6; i++) { piano.PressMidi(60 + 2 * i, this, 0.8f); Step(0.1f); piano.ReleaseMidi(60 + 2 * i, this); Step(0.1f); }
            Assert.That(g.ShownHands, Is.EqualTo(0), "live play: no ghost hands");
            // REC
            Recorder.Record();
            for (int i = 0; i < 6; i++) { piano.PressMidi(64 + i, this, 0.8f); Step(0.15f); Assert.That(g.ShownHands, Is.EqualTo(0), "recording: no ghost hands"); piano.ReleaseMidi(64 + i, this); Step(0.1f); }
            Recorder.Stop();
            Step(0.5f);
            Assert.That(g.ShownHands, Is.EqualTo(0));
            Assert.That(g.Planned, Is.EqualTo(0), "nothing was planned for live play or a recording");
            // PLAY the deck's take
            Assert.IsTrue(Recorder.Play());
            Step(0.6f);
            Assert.That(g.Planned, Is.GreaterThan(0), "the playback is read ahead");
            Assert.That(g.ShownHands, Is.GreaterThan(0), "a ghost hand plays it");
            Assert.That(g.Alpha(InstrumentKind.Piano, 1), Is.GreaterThan(0.5f), "the right hand (the notes are above middle C)");
        }

        // ------------------------------------------------------------------------------------------------ on time

        [UnityTest]
        public IEnumerator AGhostFingerIsOnEachPianoKeyWhenItSounds()
        {
            yield return Boot(InstrumentKind.Piano);
            var piano = App.Get<IPianoApi>();
            List<TakeEvent> take = PianoSong(1.0f);
            double start = _audio;
            Assert.IsTrue(Recorder.PlayEvents(take, 6.5));
            var ons = take.Where(e => e.type == TakeEventType.PianoOn).ToList();
            int checkedNotes = 0;
            float worst = 0f;
            while (_audio < start + 6.0)
            {
                double before = _audio;
                Frame();
                foreach (TakeEvent e in ons)
                {
                    double on = start + e.t;
                    if (!(on > before && on <= _audio)) continue;
                    Assert.IsTrue(piano.TryGetKey(e.midi, out PianoKeyInfo key));
                    float best = float.PositiveInfinity;
                    for (int side = 0; side < 2; side++)
                        for (int f = 0; f < 5; f++)
                        {
                            Vector3 local = piano.Root.InverseTransformPoint(Ghost.PianoTip(side, f));
                            float half = key.Black ? piano.BW * 0.5f : piano.W * 0.5f;
                            bool onKey = Mathf.Abs(local.x - key.X) < half + 0.003f && local.z > -0.005f && local.z < piano.L
                                && local.y > (key.Black ? piano.BTOP : 0f) - 0.012f && local.y < (key.Black ? piano.BTOP : 0f) + 0.02f;
                            float d = Mathf.Abs(local.x - key.X);
                            if (onKey) best = Mathf.Min(best, d);
                        }
                    Assert.That(best, Is.LessThan(0.02f), $"a ghost fingertip on key {e.midi} when it sounds at t={e.t:F2}");
                    worst = Mathf.Max(worst, best);
                    checkedNotes++;
                }
            }
            Assert.That(checkedNotes, Is.EqualTo(ons.Count), "every note checked");
            Debug.Log($"[Ghost] piano: {checkedNotes} notes, a fingertip on each key as it sounds (worst x off-centre {worst * 1000f:F1} mm)");
        }

        [UnityTest]
        public IEnumerator AGhostStickIsOnEachDrumAndAFingerOnEachHarpStringWhenItSounds()
        {
            yield return Boot(InstrumentKind.Drums);
            var drums = App.Get<IDrumsApi>();
            List<TakeEvent> beat = DrumBeat(1.6f);
            double start = _audio;
            Assert.IsTrue(Recorder.PlayEvents(beat, 8.5));
            int checkedHits = 0;
            float worst = 0f;
            while (_audio < start + 7.5)
            {
                double before = _audio;
                Frame();
                foreach (TakeEvent e in beat)
                {
                    double on = start + e.t;
                    if (!(on > before && on <= _audio) || e.kind == "kick") continue;
                    DrumPieceInfo pc = drums.Pieces.First(p => !p.Pad && p.Kind == e.kind);
                    float best = float.PositiveInfinity;
                    for (int side = 0; side < 2; side++)
                    {
                        if (Ghost.Alpha(InstrumentKind.Drums, side) <= 0f) continue;
                        Vector3 local = pc.Transform.InverseTransformPoint(Ghost.Contact(InstrumentKind.Drums, side));
                        float plane = Mathf.Abs(local.y), r = new Vector2(local.x, local.z).magnitude;
                        if (r <= pc.Radius + 0.01f) best = Mathf.Min(best, plane);
                    }
                    Assert.That(best, Is.LessThan(0.025f), $"a ghost stick's tip on the {e.kind} when it sounds at t={e.t:F2}");
                    worst = Mathf.Max(worst, best);
                    checkedHits++;
                }
            }
            Assert.That(checkedHits, Is.GreaterThan(20));
            Debug.Log($"[Ghost] drums: {checkedHits} hits, a stick tip on each piece's face (worst {worst * 1000f:F1} mm off it)");
            Recorder.Stop();
            Step(1.5f);

            // the harp (playback brings it in front)
            var harp = App.Get<IHarpApi>();
            List<TakeEvent> run = HarpRun(2.0f);
            start = _audio;
            Assert.IsTrue(Recorder.PlayEvents(run, 7.0));
            int plucks = 0;
            worst = 0f;
            while (_audio < start + 6.5)
            {
                double before = _audio;
                Frame();
                foreach (TakeEvent e in run)
                {
                    double on = start + e.t;
                    if (!(on > before && on <= _audio)) continue;
                    Vector3 a = harp.Root.TransformPoint(harp.StringStart(e.i)), b = harp.Root.TransformPoint(harp.StringEnd(e.i));
                    float best = float.PositiveInfinity;
                    for (int side = 0; side < 2; side++)
                    {
                        if (Ghost.Alpha(InstrumentKind.Harp, side) <= 0f) continue;
                        Vector3 c = Ghost.Contact(InstrumentKind.Harp, side);
                        Vector3 ab = b - a;
                        float u = Mathf.Clamp01(Vector3.Dot(c - a, ab) / ab.sqrMagnitude);
                        best = Mathf.Min(best, (a + u * ab - c).magnitude);
                    }
                    Assert.That(best, Is.LessThan(0.02f), $"a ghost fingertip at harp string {e.i} when it sounds");
                    worst = Mathf.Max(worst, best);
                    plucks++;
                }
            }
            Assert.That(plucks, Is.EqualTo(run.Count));
            Debug.Log($"[Ghost] harp: {plucks} plucks, a fingertip at each string (worst {worst * 1000f:F1} mm)");
        }

        /// <summary>2026-11-18, headset: "drums shows sometimes only one hand in recordings". Playback stages the kit out to
        /// one side of the player; the hands were chosen from the player's head, so every piece was on the same side and
        /// one hand played every hit. They follow the kit's own stool now (GhostHands.DrumSeat): the hat goes to the left
        /// hand, the floor tom to the right, the snare to whichever is free, and both stay up together while the kit plays.</summary>
        [UnityTest]
        public IEnumerator AKitStagedOffToOneSidePlaysWithBothHands()
        {
            yield return Boot(InstrumentKind.Drums);
            var drums = App.Get<IDrumsApi>();
            Transform head = App.Head;
            Vector3 aside = head.position + head.right * 1.4f + head.forward * 1.2f;
            aside.y = drums.Root.position.y;
            List<TakeEvent> beat = DrumBeat(1.0f, 3);
            double start = _audio;
            Assert.IsTrue(Recorder.PlayEvents(beat, 7.5));
            int both = 0, frames = 0;
            while (_audio < start + 6.5)
            {
                drums.Root.position = aside;   // held off to the right, still facing the way it faced
                Frame();
                if (_audio < start + 1.6 || _audio > start + 6.0) continue;
                frames++;
                if (Ghost.Alpha(InstrumentKind.Drums, 0) > 0.5f && Ghost.Alpha(InstrumentKind.Drums, 1) > 0.5f) both++;
            }
            Assert.That((drums.Root.position - aside).magnitude, Is.LessThan(0.01f), "the kit stayed off to the side");
            Assert.That(both, Is.GreaterThan(frames * 0.9f), $"both drum hands up while the kit plays ({both}/{frames} frames)");
            Debug.Log($"[Ghost] staged kit: both hands up {both}/{frames} frames");
        }

        // ------------------------------------------------------------------------------------------------ never input

        [UnityTest]
        public IEnumerator TheyNeverPlayAndAreGoneAfterStop()
        {
            yield return Boot(InstrumentKind.Piano, headless: false, mode: "all");
            int pointers = App.Input.AllPointers.Count;
            var take = PianoSong(0.8f, 3);
            take.AddRange(DrumBeat(0.8f, 2));
            take.Sort((a, c) => a.t.CompareTo(c.t));
            _events.Clear();
            Assert.IsTrue(Recorder.PlayEvents(take, 4.6));
            int shownMost = 0;
            for (int i = 0; i < (int)(4.0f / Dt); i++) { Frame(); shownMost = Mathf.Max(shownMost, Ghost.ShownHands); }
            Assert.That(shownMost, Is.GreaterThanOrEqualTo(3), "piano and drum hands played");
            Assert.That(Ghost.Renderer.GetComponentsInChildren<Collider>(true), Is.Empty, "no collider");
            Assert.That(Ghost.Renderer.gameObject.GetComponents<Component>().Length, Is.EqualTo(3), "a transform, a mesh filter and a renderer: nothing else");
            Assert.That(App.Input.AllPointers.Count, Is.EqualTo(pointers), "no pointer of theirs");
            int sentOn = take.Count(e => e.type == TakeEventType.PianoOn && e.t < 4.0f), sentDrum = take.Count(e => e.type == TakeEventType.Drum && e.t < 4.0f);
            int pianoOn = _events.Count(e => e.Event.type == TakeEventType.PianoOn);
            int drum = _events.Count(e => e.Event.type == TakeEventType.Drum);
            Assert.That(pianoOn, Is.EqualTo(sentOn), "the piano played exactly the take's notes");
            Assert.That(drum, Is.EqualTo(sentDrum), "the drums played exactly the take's hits");
            Assert.That(_events.Any(e => !e.DuringPlayback), Is.False, "nothing live");
            Assert.IsTrue(Ghost.Renderer.enabled);

            Recorder.Stop();
            Step(0.3f);
            Assert.That(Ghost.ShownHands, Is.GreaterThan(0), "they fade, they do not vanish");
            Step(1.2f);
            Assert.That(Ghost.ShownHands, Is.EqualTo(0), "gone after the fade");
            Assert.IsFalse(Ghost.Renderer.enabled, "nothing drawn");
            Assert.That(Ghost.Track(InstrumentKind.Piano, 1).Count + Ghost.Track(InstrumentKind.Drums, 0).Count + Ghost.Track(InstrumentKind.Drums, 1).Count, Is.EqualTo(0), "nothing planned");
        }

        // ------------------------------------------------------------------------------------------------ the player's hand

        [UnityTest]
        public IEnumerator ThePlayersOwnHandThereTurnsTheGhostHandFaint()
        {
            yield return Boot(InstrumentKind.Piano);
            List<TakeEvent> take = PianoSong(0.5f, 4);
            Assert.IsTrue(Recorder.PlayEvents(take, 5.5));
            Step(1.0f);
            Assert.That(Ghost.Overlap(InstrumentKind.Piano, 1), Is.EqualTo(1f), "full while the player's hands are away");
            // the player's right hand just above the ghost right hand's palm
            for (int i = 0; i < 30; i++)
            {
                _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(Ghost.PalmWorld(InstrumentKind.Piano, 1) + Vector3.up * 0.03f));
                Frame();
            }
            Assert.That(Ghost.Overlap(InstrumentKind.Piano, 1), Is.EqualTo(GhostHands.OverlapAlpha).Within(0.02f), "faint where the player's hand is");
            ParkHands();
            Step(0.6f);
            Assert.That(Ghost.Overlap(InstrumentKind.Piano, 1), Is.EqualTo(1f), "back once the hand has gone");
        }

        // ------------------------------------------------------------------------------------------------ shown instruments only

        [UnityTest]
        public IEnumerator AnInstrumentPutAwayLosesItsHands()
        {
            yield return Boot(InstrumentKind.Piano, mode: "all");
            var layout = App.Get<ILayoutService>();
            var take = PianoSong(1.5f, 6);
            take.AddRange(DrumBeat(1.5f, 6));
            take.Sort((a, c) => a.t.CompareTo(c.t));
            Assert.IsTrue(Recorder.PlayEvents(take, 9.0));
            Step(2.5f);
            Assert.That(Ghost.Alpha(InstrumentKind.Drums, 0) + Ghost.Alpha(InstrumentKind.Drums, 1), Is.GreaterThan(0.5f), "the drums' hands play");
            Assert.That(layout.PlaceOf(InstrumentKind.Drums), Is.EqualTo(InstrumentPlace.Seat));
            if (!layout.PutAway(InstrumentKind.Drums)) Assert.Inconclusive("the layout would not put the drums away here");
            Step(3f);
            Assert.That(layout.PlaceOf(InstrumentKind.Drums), Is.Not.EqualTo(InstrumentPlace.Seat));
            Assert.That(Ghost.Alpha(InstrumentKind.Drums, 0) + Ghost.Alpha(InstrumentKind.Drums, 1), Is.EqualTo(0f), "no hands at an instrument that is not shown");
            Assert.That(Ghost.Alpha(InstrumentKind.Piano, 1), Is.GreaterThan(0.5f), "the piano's keep playing");
        }

        // ------------------------------------------------------------------------------------------------ Sunny

        [UnityTest]
        public IEnumerator SunnyIsAskedOnceTheyHavePlayedForAMoment()
        {
            // the other playback tips said before (Sunny says one at a time; ours would wait for her)
            yield return Boot(InstrumentKind.Piano, seen: "tip_listen_back,tip_play_brings,tip_window_show,tip_song_stars,tip_paint_walls,tip_paint_keep");
            Assert.That(global::MusicRoom.Guide.GuideLines.Has(GhostHands.TipId), Is.True, "the line exists");
            Assert.IsFalse(Ghost.TipAsked);
            Assert.IsTrue(Recorder.PlayEvents(PianoSong(0.3f, 6), 7.5));
            Step(0.8f);
            Assert.IsFalse(Ghost.TipAsked, "not at once");
            Step(GhostHands.TipAfter);
            Assert.IsTrue(Ghost.TipAsked, "asked once they have played for a moment");
            Step(0.3f);
            Assert.IsTrue(App.Get<IGuide>().Present, "Sunny comes to say it");
        }

        // ------------------------------------------------------------------------------------------------ allocation

        [UnityTest]
        public IEnumerator AGhostFrameAllocatesNothing()
        {
            yield return Boot(InstrumentKind.Piano, headless: false, mode: "all", seen: "tip_listen_back,tip_play_brings,tip_ghost_hands");
            var take = PianoSong(0.3f, 30);
            take.AddRange(DrumBeat(0.3f, 30));
            take.AddRange(HarpRun(0.4f, 90));
            take.Sort((a, c) => a.t.CompareTo(c.t));
            Assert.IsTrue(Recorder.PlayEvents(take, 40));
            Step(3f);   // the instruments arrive, every hand has played
            GhostHands g = Ghost;
            Assert.That(g.ShownHands, Is.GreaterThanOrEqualTo(4));
            // measured: the ghost hands' own frame (the audio clock runs on; the recorder's dispatch is not the ghost's cost)
            for (int i = 0; i < 100; i++) { _audio += Dt; g.Tick(Dt); }
            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++) { _audio += Dt; g.Tick(Dt); }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(g.ShownHands, Is.GreaterThanOrEqualTo(4));
        }
    }
}
