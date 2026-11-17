// The snowy window joins in (Room/Decor/WindowShow.cs), in the booted app:
//   - live play and recording (REC) leave the window alone: no note reaches it, no global moves; REC during a playback
//     ends the show;
//   - a playback gusts the snow and lights the sky (the shader globals), and after STOP every global is exactly 0 again,
//     the room's own snow globals and materials untouched;
//   - the window rect for what keeps off it (TryGetWindow / Covers) follows the built room; in mixed reality (the room
//     hidden) there is no window (the snowy window was removed 2026-11-13), the globals still settle;
//   - Sunny is asked for tip_window_show once the lights have been up a moment while the player looks at the window;
//   - a show frame allocates nothing.
using System.Collections;
using System.Collections.Generic;
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
    public class WindowShowPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;
        Quaternion? _look;

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        WindowShow Show => Room.WindowShow;
        IRecorder Recorder => App.Get<IRecorder>();
        void Frame() { _audio += Dt; if (_look.HasValue) App.Head.rotation = _look.Value; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        static Vector4 G(int id) => Shader.GetGlobalVector(id);
        static readonly int[] Ids = { WindowShow.SwirlId, WindowShow.AuroraId, WindowShow.AuroraAId, WindowShow.AuroraBId, WindowShow.AuroraCId, WindowShow.SnowTintId };

        static void AssertGlobalsZero(string why)
        {
            foreach (int id in Ids) Assert.That(G(id), Is.EqualTo(Vector4.zero), why);
        }

        IEnumerator Boot(bool headless = true, bool tipSeen = true)
        {
            _audio = 10.0;
            _look = null;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            // the other lines a playback asks for (the room's, the layout's and the other playback-show parts') are heard
            // already, so Sunny is free for this one
            string seen = "tip_listen_back,tip_play_brings,tip_song_stars,tip_paint_walls,tip_paint_keep,tip_ghost_hands," + RoomSnow.TipId + (tipSeen ? "," + WindowShow.TipId : "");
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, seen);
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
            for (int i = 0; i < notes; i++)
            {
                int m = 48 + 7 * (i % 7);
                TakeEvent on = TakeEvent.PianoOn(m, vel); on.t = 0.02f + i * gap; take.Add(on);
                TakeEvent off = TakeEvent.PianoOff(m); off.t = 0.02f + i * gap + gap * 0.8f; take.Add(off);
            }
            return take;
        }

        [UnityTest]
        public IEnumerator LivePlayAndRecordingLeaveTheWindowAlone()
        {
            yield return Boot();
            Room.Set(AmbienceKind.Snow, true);
            Step(0.5f);
            AssertGlobalsZero("at rest");
            // plain live play
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Step(0.5f);
            ParkHands();
            Assert.That(Show.Model.Notes, Is.EqualTo(0), "live play never reaches the window");
            Assert.That(Show.Pushes, Is.EqualTo(0));
            AssertGlobalsZero("live play: the window is its own");
            // recording
            Recorder.Record();
            Step(0.2f);
            Assert.That(Recorder.State, Is.EqualTo(RecorderState.Recording));
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Step(0.5f);
            ParkHands();
            Assert.That(Show.Model.Notes, Is.EqualTo(0), "recording never reaches the window");
            Assert.IsFalse(Show.Model.Playing);
            AssertGlobalsZero("recording: the window is its own");
            Recorder.Stop();
            Step(0.2f);
            // REC during a playback ends the show
            Assert.IsTrue(Recorder.PlayEvents(Song(20, 0.2f, 0.8f), 4.5));
            Step(1f);
            Assert.IsTrue(Show.Model.Playing);
            Assert.That(G(WindowShow.AuroraId).x, Is.GreaterThan(0f));
            Recorder.Record();
            Step(0.1f);
            Assert.IsFalse(Show.Model.Playing, "recording ends the show");
            Step(9f);
            Assert.IsTrue(Show.Model.Resting);
            AssertGlobalsZero("back to rest while recording");
            Recorder.Stop();
        }

        [UnityTest]
        public IEnumerator APlaybackGustsTheSnowAndLightsTheSkyThenSettlesExactly()
        {
            yield return Boot(headless: false);
            RoomModule room = Room;
            room.Set(AmbienceKind.Snow, true);
            Step(9f);   // the snow level has settled at 1 (the sky's tint follows it)
            Material sky = room.Scene.SkyMaterial, frost = room.Scene.FrostMaterial, snow = room.Scene.SnowMaterial;
            Vector4 skyTint = sky.GetVector(RoomMaterials.Tint);

            Assert.IsTrue(Recorder.PlayEvents(Song(24, 0.2f, 0.9f), 5.2));
            float gust = 0f, aurora = 0f, phase = 0f;
            Vector4 band = default;
            for (int f = 0; f < 72 * 3; f++)
            {
                Frame();
                gust = Mathf.Max(gust, G(WindowShow.SwirlId).x);
                phase = Mathf.Max(phase, G(WindowShow.SwirlId).y);
                aurora = Mathf.Max(aurora, G(WindowShow.AuroraId).x);
                for (int b = 0; b < 3; b++) { Vector4 v = G(Ids[2 + b]); if (v.w > band.w) band = v; }
            }
            Assert.That(gust, Is.GreaterThan(0.7f), "every note blows through the snow");
            Assert.That(phase, Is.GreaterThan(0f), "the flakes swirl");
            Assert.That(aurora, Is.GreaterThan(0.5f), "the northern lights are up");
            Assert.That(band.w, Is.GreaterThan(0.5f), "a curtain flares with its notes");
            Assert.That(band.y, Is.GreaterThan(band.x), "in a night colour (green/teal/violet, not warm)");
            Assert.That(G(WindowShow.SnowTintId).y, Is.GreaterThan(0f), "the flakes catch the light");

            Recorder.Stop();
            Step(1f);
            Assert.That(G(WindowShow.AuroraId).x, Is.GreaterThan(0f).And.LessThan(aurora), "the lights fade");
            Step(9f);
            Assert.IsTrue(Show.Model.Resting, "at rest");
            AssertGlobalsZero("after the end every global is exactly 0");
            int pushes = Show.Pushes;
            Step(1f);
            Assert.That(Show.Pushes, Is.EqualTo(pushes), "nothing written at rest");
            // the room's own snow and sky are untouched
            Assert.That(Shader.GetGlobalFloat(RoomModule.SnowWindVelId), Is.EqualTo(room.Weather.WindVel), "the weather's own wind");
            Assert.That(Shader.GetGlobalFloat(RoomModule.SnowTimeId), Is.EqualTo(room.Weather.Time));
            Assert.That(sky.GetVector(RoomMaterials.Tint), Is.EqualTo(skyTint), "the sky's own tint");
            Assert.That(frost.HasProperty("_AuroraOn"), Is.False, "the room's frost has no lights of its own");
            Assert.That(room.Scene.SnowMaterial, Is.SameAs(snow));
        }

        [UnityTest]
        public IEnumerator TheRoomWindowIsKnownAndMixedRealityHasNone()
        {
            yield return Boot(headless: false);
            RoomModule room = Room;
            room.Set(AmbienceKind.Snow, true);
            Step(3f);
            // the VR window: first while the room shows
            Assert.IsTrue(Show.TryGetWindow(0, out Vector3 c0, out Quaternion r0, out Vector2 s0));
            Assert.That(Vector3.Distance(c0, room.Scene.Window.position), Is.LessThan(1e-4f), "the room's window, from the built room");
            Assert.That(s0.x, Is.GreaterThan(1.8f));
            Assert.IsTrue(Show.Covers(room.Scene.Window.position + room.Scene.Window.up * 0.5f, 0f), "a point on its glass");
            Assert.IsFalse(Show.Covers(room.Scene.Window.position + room.Scene.Window.right * 2f, 0.1f), "the wall beside it");
            Assert.IsFalse(Show.TryGetWindow(1, out _, out _, out _), "one window in VR");

            // mixed reality: the room hidden, no window at all (the snowy window was removed 2026-11-13)
            room.SetVisible(false);
            Step(0.5f);
            Assert.IsFalse(Show.TryGetWindow(0, out _, out _, out _), "no window in mixed reality");
            Assert.IsFalse(Show.Covers(c0, 0.5f), "nothing to keep off");
            Assert.IsFalse(Show.WindowInView());

            Assert.IsTrue(Recorder.PlayEvents(Song(16, 0.2f, 0.9f), 3.5));
            Step(2f);
            Assert.That(G(WindowShow.SwirlId).x, Is.GreaterThan(0f), "the model still gusts (the room snow reads it)");
            Recorder.Stop();
            Step(10f);
            AssertGlobalsZero("settled after the end in mixed reality");
            room.SetVisible(true);
            Step(0.1f);
        }

        [UnityTest]
        public IEnumerator SunnyIsAskedOnceTheLightsAreUpWhileTheWindowIsInView()
        {
            yield return Boot(tipSeen: false);
            Room.Set(AmbienceKind.Snow, true);
            Assert.That(global::MusicRoom.Guide.GuideLines.Has(WindowShow.TipId), Is.True, "the line exists");
            Vector3 head = App.Head.position;
            Vector3 away = head - Room.Scene.Window.position; away.y = 0f;
            _look = Quaternion.LookRotation(away.normalized, Vector3.up);   // the back to the window
            Assert.IsTrue(Recorder.PlayEvents(Song(60, 0.2f, 0.8f), 12.5));
            Step(5f);
            Assert.IsFalse(Show.WindowInView());
            Assert.IsFalse(Show.TipAsked, "not while the window is behind the player");
            _look = Quaternion.LookRotation(Room.Scene.Window.position - head, Vector3.up);
            Step(1f);
            Assert.IsTrue(Show.WindowInView());
            Assert.IsFalse(Show.TipAsked, "not at the first glance");
            Step(WindowShow.TipAfter + 0.5f);
            Assert.IsTrue(Show.TipAsked, "asked once the lights have been up a moment in view");
            Recorder.Stop();
        }

        [UnityTest]
        public IEnumerator AShowFrameAllocatesNothing()
        {
            yield return Boot(headless: false);
            Room.Set(AmbienceKind.Snow, true);
            Step(0.2f);
            _look = Quaternion.LookRotation(Room.Scene.Window.position - App.Head.position, Vector3.up);
            App.Head.rotation = _look.Value;   // looking at the room's window (the frames below are the show's own)
            WindowShow show = Show;
            EventBus bus = App.Events;
            bus.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 7, Loop = true });
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.8f, Lane = 1, Event = TakeEvent.PianoOn(64, 0.8f) };
            var drum = new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 1f, Lane = 1, Event = TakeEvent.Drum("crash", 1f, 0.3f, "edge", false) };
            // warm: every path, and Sunny's one-off ask (TipSeen reads the settings) behind us
            for (int i = 0; i < 500; i++)
            {
                if (i % 6 == 0) bus.Publish(on);
                if (i % 9 == 0) { drum.When = _audio + 0.05; bus.Publish(drum); }
                _audio += Dt;
                show.Tick(Dt, true);
                show.Covers(App.Head.position, 0.1f);
            }
            Assert.IsTrue(show.TipAsked, "the one-off ask is behind us");
            // The notes go straight to the show's model here: the bus would also run the other playback watchers' handlers
            // (the paint, the sky, the ghost hands, the pulse), which are not this frame's cost.
            int frame = 0;
            bool covered = false;
            Assert.That(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    frame++;
                    if (frame % 5 == 0) show.Model.Note(on, _audio);
                    if (frame % 9 == 0) { drum.When = _audio + 0.05; show.Model.Note(drum, _audio); }
                    _audio += Dt;
                    show.Tick(Dt, frame % 2 == 0);
                    covered |= show.Covers(App.Head.position, 0.1f);
                    show.WindowInView();
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(show.Model.Aurora, Is.GreaterThan(0.3f));
            bus.Publish(new RecordingPlaybackEvent { Started = false });
        }
    }
}
