// "Your song becomes a night sky", in the booted app (Room/Decor/SongSky.cs):
//   - stars come only from a playback: plain play and a recording (REC) make none; a take's notes rise from above the key
//     (or drum) that played them and settle under the room's ceiling, above the head and out of its sphere, joined into a
//     constellation; after STOP the sky lingers, fades and is gone (renderer off, no star), nothing of the room touched;
//   - mixed reality (the room hidden): the stars float in open air above the head over the instruments, no night field;
//   - Sunny is asked for tip_song_stars once a playback has made a few stars;
//   - a sky frame (notes in, stars written, the shader's values) allocates nothing.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Render;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode
{
    public class SongSkyPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        SongSky Sky => Room.SongSky;
        IRecorder Recorder => App.Get<IRecorder>();
        IInstrument Inst(InstrumentKind k) => App.Instruments.FirstOrDefault(i => i.Kind == k);
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        IEnumerator Boot(InstrumentKind current, bool headless, string tipsSeen = null)
        {
            _audio = 10.0;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(current));
            if (tipsSeen != null) _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, tipsSeen);
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
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
        }

        static List<TakeEvent> Song(int notes, float gap, float vel)
        {
            var take = new List<TakeEvent>();
            int[] tune = { 60, 64, 67, 72, 76, 72, 67, 64, 62, 65, 69, 74 };
            for (int i = 0; i < notes; i++)
            {
                int m = tune[i % tune.Length];
                TakeEvent on = TakeEvent.PianoOn(m, vel); on.t = 0.05f + gap * i; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = on.t + gap * 0.8f; take.Add(off);
            }
            return take;
        }

        Bounds Keys()
        {
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in Inst(InstrumentKind.Piano).PlayedParts)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        IEnumerable<int> Live()
        {
            for (int i = 0; i < SongSkyModel.Max; i++) if (Sky.Model.Stars[i].Alive) yield return i;
        }

        [UnityTest]
        public IEnumerator StarsComeOnlyOnPlaybackSettleUnderTheCeilingAndGoAfterStop()
        {
            yield return Boot(InstrumentKind.Piano, headless: false, tipsSeen: SongSky.TipId);
            SongSky sky = Sky;
            Assert.IsNotNull(sky.Renderer, "drawn (the Room/SongStars template exists: render-setup)");
            Assert.IsFalse(sky.Renderer.enabled, "nothing at rest");

            // plain play: no star
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Step(0.5f);
            Assert.That(sky.Model.TotalMade, Is.EqualTo(0), "plain play makes no star");
            ParkHands();
            // recording: no star either
            Recorder.Record();
            Step(0.1f);
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Step(0.4f);
            Recorder.Stop();
            ParkHands();
            Step(0.3f);
            Assert.That(sky.Model.TotalMade, Is.EqualTo(0), "recording makes no star");
            Assert.IsFalse(sky.Renderer.enabled);

            // a take plays: stars rise from the keys and settle in the sky
            Bounds keys = Keys();
            Vector3 head = App.Head.position;
            Assert.IsTrue(Recorder.PlayEvents(Song(16, 0.25f, 0.85f), 4.2));
            Step(0.4f);
            Assert.That(sky.Model.TotalMade, Is.GreaterThan(0), "a playback's notes make stars");
            Assert.IsTrue(sky.Renderer.enabled);
            int first = Live().First();
            Vector3 from = sky.Model.Stars[first].From;
            Assert.That(new Vector2(from.x - keys.center.x, from.z - keys.center.z).magnitude, Is.LessThan(keys.extents.magnitude + 0.05f), "born over the keys");
            Assert.That(from.y, Is.GreaterThan(keys.max.y - 0.01f), "above them");
            Bounds ceilingBox = Room.Scene.Static[RoomMat.Ceiling].bounds;
            float ceiling = ceilingBox.max.y;
            float minDist = float.MaxValue;
            for (int f = 0; f < 72 * 6; f++)
            {
                Frame();
                Vector3 h = App.Head.position;
                foreach (int i in Live())
                    if (sky.StarAt(i, out Vector3 p)) minDist = Mathf.Min(minDist, (p - h).magnitude);
            }
            Assert.That(minDist, Is.GreaterThanOrEqualTo(SongSkyModel.HeadClear - 1e-4f), "never in the head's sphere");
            Assert.That(sky.Model.TotalMade, Is.EqualTo(16), "one star per note");
            int joined = 0;
            foreach (int i in Live())
            {
                SongSkyModel.Star s = sky.Model.Stars[i];
                Assert.IsTrue(sky.StarAt(i, out Vector3 p));
                Assert.That(p, Is.EqualTo(s.To), "settled where it was placed");
                Assert.That(p.y, Is.LessThanOrEqualTo(ceiling - SongSkyModel.TopGap + 1e-3f), "under the ceiling");
                Assert.That(p.y, Is.GreaterThan(head.y + 0.5f), "above the head");
                Assert.That(p.x, Is.InRange(ceilingBox.min.x, ceilingBox.max.x), "inside the room (its ceiling as built)");
                Assert.That(p.z, Is.InRange(ceilingBox.min.z, ceilingBox.max.z), "inside the room (its ceiling as built)");
                if (s.Prev >= 0) joined++;
            }
            Assert.That(joined, Is.EqualTo(15), "a constellation: each star joined to the one before");
            Assert.That(sky.FieldLevel, Is.GreaterThan(0.5f), "the night field under the ceiling (VR)");

            // the end: it lingers, fades, goes
            Step(2f);
            Assert.That(Recorder.State, Is.EqualTo(RecorderState.Idle), "the take ended");
            Assert.That(sky.Model.State, Is.EqualTo(SongSkyModel.Phase.Lingering).Or.EqualTo(SongSkyModel.Phase.Fading));
            Step(SongSkyModel.Linger + SongSkyModel.FadeOut + 1f);
            Assert.IsTrue(sky.Model.Resting, "gone");
            Assert.That(sky.Model.Live, Is.EqualTo(0));
            Assert.IsFalse(sky.Renderer.enabled, "renderer off: the room as it was");
            Assert.That(sky.FieldLevel, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator InMixedRealityTheStarsFloatAboveTheInstruments()
        {
            yield return Boot(InstrumentKind.Drums, headless: false, tipsSeen: SongSky.TipId);
            Room.SetVisible(false);   // what mixed reality does to the room
            Step(0.1f);
            Vector3 head = App.Head.position;
            var take = new List<TakeEvent>();
            for (int i = 0; i < 10; i++) { TakeEvent e = TakeEvent.Drum(i % 3 == 0 ? "kick" : i % 3 == 1 ? "snare" : "crash", 0.8f, 0.3f, "head", false); e.t = 0.05f + 0.2f * i; take.Add(e); }
            Assert.IsTrue(Recorder.PlayEvents(take, 2.2));
            // the kit steps back for the show (2026-11-18, StageFarther, a glide of about a second): a star born after it
            // stands still
            Step(1.5f);
            IDrumsApi drums = App.Get<IDrumsApi>();
            int newest = Live().Last();
            Vector3 from = Sky.Model.Stars[newest].From;
            float nearPiece = drums.Pieces.Where(p => p.Transform != null).Min(p => (p.Transform.position + Vector3.up * 0.05f - from).magnitude);
            Assert.That(nearPiece, Is.LessThan(0.01f), "born from the piece that was hit");
            Step(2.3f);
            Assert.That(Sky.Model.TotalMade, Is.EqualTo(10));
            Assert.IsFalse(Sky.Model.Current.Room, "framed for mixed reality");
            foreach (int i in Live())
            {
                Assert.IsTrue(Sky.StarAt(i, out Vector3 p));
                Assert.That(p.y - head.y, Is.GreaterThanOrEqualTo(SongSkyModel.MrAbove - 1e-3f), "above the head");
                Vector3 flat = p - head; flat.y = 0f;
                Assert.That(flat.magnitude, Is.EqualTo(SongSkyModel.MrDistance).Within(0.01f), "out over the instruments");
            }
            Assert.That(Sky.FieldLevel, Is.EqualTo(0f), "no night field in mixed reality");
            Room.SetVisible(true);
            Step(SongSkyModel.Linger + SongSkyModel.FadeOut + 1f);
            Assert.IsTrue(Sky.Model.Resting);
            Assert.IsFalse(Sky.Renderer.enabled);
        }

        [UnityTest]
        public IEnumerator SunnyIsAskedOnceAPlaybackHasMadeAFewStars()
        {
            // the other playback tips already heard: Sunny is free for this one
            yield return Boot(InstrumentKind.Piano, headless: true,
                tipsSeen: ListenBackPulse.TipId + ",tip_play_brings,tip_window_show,tip_paint_walls,tip_paint_keep,tip_ghost_hands");
            Assert.That(global::MusicRoom.Guide.GuideLines.Has(SongSky.TipId), Is.True, "the line exists");
            Assert.IsFalse(Sky.TipAsked);
            Assert.IsTrue(Recorder.PlayEvents(Song(20, 0.3f, 0.7f), 6.5));
            Step(0.3f + 0.3f * (SongSky.TipStars - 2));
            Assert.IsFalse(Sky.TipAsked, "not on the first stars");
            Step(0.9f);
            Assert.IsTrue(Sky.TipAsked, "asked once a few stars are up");
            Step(0.3f);
            Assert.IsTrue(App.Get<IGuide>().Present, "Sunny comes to say it");
        }

        [UnityTest]
        public IEnumerator ASkyFrameAllocatesNothing()
        {
            yield return Boot(InstrumentKind.Piano, headless: false, tipsSeen: SongSky.TipId + "," + ListenBackPulse.TipId);
            SongSky sky = Sky;
            EventBus bus = App.Events;
            var piano = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.8f, Lane = 1, Event = TakeEvent.PianoOn(64, 0.8f) };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 1f, Lane = 1, Event = TakeEvent.Drum("snare", 1f, 0.3f, "head", false) };
            var harp = new PlaybackNoteEvent { Kind = InstrumentKind.Harp, Role = EventRole.Onset, Level = 0.6f, Lane = 1, Event = TakeEvent.Harp(8, 0.6f, 0.35f, 0f, "through") };
            int frame = 0;
            void Notes()
            {
                frame++;
                if (frame % 5 == 0) { piano.Event.midi = 55 + frame % 24; bus.Publish(piano); }
                if (frame % 7 == 0) bus.Publish(drum);
                if (frame % 11 == 0) { harp.When = _audio + 0.05; bus.Publish(harp); }
                _audio += Dt;
            }
            // warm: the sky started (a loop), every kind placed, the ring full
            for (int i = 0; i < 1500; i++) { Notes(); sky.Tick(Dt, true); }
            Assert.That(sky.Model.Live, Is.GreaterThan(100));
            Assert.That(() =>
            {
                for (int i = 0; i < 300; i++) { Notes(); sky.Tick(Dt, true); }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
