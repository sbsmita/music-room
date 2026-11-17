// Snow in the whole room (Room/Decor/RoomSnow.cs), in the booted app:
//   - SNOW off: nothing drawn; on: the one renderer draws, the volume is the built room's box (it holds the player), the
//     flakes (the shader's maths on the CPU, RoomSnowModel.FlakeAt, with what the material got) fall all around the head
//     and never at it; the room hidden (mixed reality): a round volume about the player; off again: gone;
//   - a playback swirls it and tints the near flakes with the instrument playing, then everything is exactly 0 again;
//     live play and recording (REC) never do;
//   - Sunny is asked for tip_room_snow once the snow has fallen a few seconds;
//   - a frame allocates nothing.
using System.Collections;
using System.Collections.Generic;
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
    public class RoomSnowPlayTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _audio = 10.0;

        App App => App.Current;
        RoomModule Room => App.Get<IRoom>() as RoomModule;
        RoomSnow Snow => Room.RoomSnow;
        IRecorder Recorder => App.Get<IRecorder>();
        void Frame() { _audio += Dt; _boot.Step(Dt); }
        void Step(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s / Dt)); for (int i = 0; i < n; i++) Frame(); }
        FixtureContext Ctx => new FixtureContext { Boot = _boot, Frame = Frame };

        IEnumerator Boot(bool headless = false, bool tipSeen = true)
        {
            _audio = 10.0;
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true, audioClock: () => _audio);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            string seen = "tip_listen_back,tip_play_brings,tip_song_stars,tip_paint_walls,tip_paint_keep,tip_paint_throw,tip_paint_gallery,tip_ghost_hands,tip_window_show,tip_switches"
                          + (tipSeen ? "," + RoomSnow.TipId : "");
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

        /// <summary>Every flake as the shader places it this frame: how many show, in which eighth around the head, the
        /// nearest to the head.</summary>
        static int Around(RoomSnow snow, int[] sectors, out float nearest)
        {
            RoomSnowModel.Frame f = snow.Frame;
            nearest = float.PositiveInfinity;
            int shown = 0;
            foreach (RoomSnowModel.Seed s in snow.Seeds)
            {
                RoomSnowModel.FlakeAt(s, f, f.Head, out Vector3 p, out float a, out _);
                if (a <= 0f) continue;
                shown++;
                Assert.IsTrue(f.Vol.Contains(p, 1e-3f), "inside the volume");
                nearest = Mathf.Min(nearest, Vector3.Distance(p, f.Head));
                Vector3 d = p - f.Head;
                if (new Vector2(d.x, d.z).magnitude < 0.3f) continue;
                float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                sectors[Mathf.Clamp((int)((ang + 180f) / 45f), 0, 7)]++;
            }
            return shown;
        }

        [UnityTest]
        public IEnumerator SnowOnFallsAllAroundTheHeadInTheRoomAndInMixedRealityOffIsGone()
        {
            yield return Boot();
            Assert.IsFalse(Snow.Falling, "SNOW starts off");
            Assert.That(Snow.Renderer, Is.Not.Null, "one renderer (the template exists)");
            Assert.IsFalse(Snow.Renderer.enabled, "off: not drawn");
            Assert.That(Snow.Mesh.vertexCount, Is.EqualTo(4 * RoomSnowModel.Count));
            Room.Set(AmbienceKind.Snow, true);
            Step(8f);   // the level eases up with the switch
            Assert.That(Room.Weather.Level, Is.GreaterThan(0.95f));
            Assert.IsTrue(Snow.Falling);
            Assert.IsTrue(Snow.Renderer.enabled);
            Assert.IsFalse(Snow.InMixedReality);
            // the volume: the built room's box under its pose (never hard-coded), with the player in it
            Transform root = Room.Scene.Root;
            RoomSnowModel.Volume want = RoomSnowModel.ForRoom(root.position, root.eulerAngles.y, RoomFrame.LocalBox);
            RoomSnowModel.Volume vol = Snow.Frame.Vol;
            Assert.That(Vector3.Distance(vol.Centre, want.Centre), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(vol.Half, want.Half), Is.LessThan(1e-4f));
            Assert.IsTrue(vol.Contains(App.Head.position, 0f), "the player is inside the snowfall");
            Assert.That(Vector3.Distance(vol.Centre, RoomFrame.WorldBox.center + Vector3.down * (RoomSnowModel.CeilingInset * 0.5f)), Is.LessThan(1e-3f), "the turned room's box");
            Assert.That(Snow.Material.GetVector(RoomSnow.BoxCId).x, Is.EqualTo(vol.Centre.x).Within(1e-5f), "the shader gets it");
            Assert.That(Snow.Material.GetVector(RoomSnow.ClockId).w, Is.EqualTo(Room.Weather.Level).Within(1e-5f));
            Assert.That(Snow.Material.GetVector(RoomSnow.FireId).w, Is.GreaterThan(0f), "VR: the fire warms and melts them");
            int[] sectors = new int[8];
            int shown = Around(Snow, sectors, out float nearest);
            Debug.Log($"[RoomSnow] VR in the app: {shown} flakes, sectors {string.Join(",", sectors)}, nearest {nearest:F2} m");
            Assert.That(shown, Is.GreaterThan(RoomSnowModel.Count / 2));
            for (int s = 0; s < 8; s++) Assert.That(sectors[s], Is.GreaterThan(40), "360 degrees (" + s + ")");
            Assert.That(nearest, Is.GreaterThan(RoomSnowModel.HeadClear), "the head stays clear");

            // the room hidden (mixed reality): round about the player
            Room.SetVisible(false);
            Step(0.5f);
            Assert.IsTrue(Snow.InMixedReality);
            vol = Snow.Frame.Vol;
            Assert.IsTrue(vol.Round);
            Assert.That(Vector2.Distance(new Vector2(vol.Centre.x, vol.Centre.z), new Vector2(App.Head.position.x, App.Head.position.z)), Is.LessThan(0.05f));
            Assert.That(Snow.Material.GetVector(RoomSnow.ModeId).x, Is.EqualTo(1f));
            Assert.That(Snow.Material.GetVector(RoomSnow.FireId).w, Is.Zero, "no virtual fire in the real room");
            sectors = new int[8];
            shown = Around(Snow, sectors, out nearest);
            Debug.Log($"[RoomSnow] MR in the app: {shown} flakes, sectors {string.Join(",", sectors)}, nearest {nearest:F2} m");
            for (int s = 0; s < 8; s++) Assert.That(sectors[s], Is.GreaterThan(40), "MR 360 degrees (" + s + ")");
            Assert.That(nearest, Is.GreaterThan(RoomSnowModel.HeadClear));
            Room.SetVisible(true);
            Step(0.2f);
            Assert.IsFalse(Snow.InMixedReality);

            Room.Set(AmbienceKind.Snow, false);
            Step(10f);
            Assert.That(Room.Weather.Level, Is.Zero);
            Assert.IsFalse(Snow.Falling);
            Assert.IsFalse(Snow.Renderer.enabled, "off: gone");
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
        public IEnumerator APlaybackSwirlsAndTintsItLivePlayAndRecordingNever()
        {
            yield return Boot();
            Room.Set(AmbienceKind.Snow, true);
            Step(6f);
            Assert.That(Snow.Gust, Is.Zero);
            Assert.That(Snow.Frame.Show, Is.EqualTo(Vector4.zero));
            // plain live play
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Step(0.4f);
            ParkHands();
            Assert.That(Snow.Gust, Is.Zero, "live play leaves the snow calm");
            Assert.That(Snow.Model.Tint, Is.EqualTo(Vector4.zero));
            // recording
            Recorder.Record();
            Step(0.2f);
            FixtureContext.For(InstrumentKind.Piano).Strike(Ctx);
            Step(0.4f);
            ParkHands();
            Assert.That(Snow.Gust, Is.Zero, "recording leaves it calm");
            Assert.That(Snow.Model.Tint, Is.EqualTo(Vector4.zero));
            Recorder.Stop();
            Step(0.3f);
            // a played-back note without a playback (nothing sends that, but the room only listens during one)
            App.Events.Publish(new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 1f, Lane = 1, Event = TakeEvent.PianoOn(60, 1f) });
            Step(0.2f);
            Assert.That(Snow.Model.Tint, Is.EqualTo(Vector4.zero), "no playback, no tint");
            // a playback
            Assert.IsTrue(Recorder.PlayEvents(Song(24, 0.2f, 0.9f), 5.2));
            float maxGust = 0f, maxTint = 0f;
            Vector4 tint = default;
            for (int i = 0; i < 72 * 3; i++)
            {
                Frame();
                maxGust = Mathf.Max(maxGust, Snow.Gust);
                if (Snow.Model.Tint.w > maxTint) { maxTint = Snow.Model.Tint.w; tint = Snow.Model.Tint; }
            }
            Assert.That(maxGust, Is.GreaterThan(0.3f), "the beats gust through the room's snow");
            Assert.That(Snow.Material.GetVector(RoomSnow.ShowId).x, Is.GreaterThan(0f), "the shader gets the swirl");
            Assert.That(maxTint, Is.GreaterThan(0.1f), "the near flakes take the instrument's colour");
            Color gold = SongStars.ColorOf(InstrumentKind.Piano).linear;
            Assert.That(Vector3.Distance(new Vector3(tint.x, tint.y, tint.z), new Vector3(gold.r, gold.g, gold.b)), Is.LessThan(1e-3f), "the piano's");
            Recorder.Stop();
            Step(12f);
            Assert.That(Snow.Gust, Is.Zero, "calm again");
            Assert.That(Snow.Frame.Show, Is.EqualTo(Vector4.zero));
            Assert.That(Snow.Model.Tint, Is.EqualTo(Vector4.zero), "white again, exactly");
            Assert.That(Snow.Material.GetVector(RoomSnow.ShowId), Is.EqualTo(Vector4.zero));
            Assert.That(Snow.Material.GetVector(RoomSnow.TintId), Is.EqualTo(Vector4.zero));
            // REC during a playback ends it
            Assert.IsTrue(Recorder.PlayEvents(Song(24, 0.2f, 0.9f), 5.2));
            Step(1f);
            Recorder.Record();
            Step(10f);
            Assert.That(Snow.Gust, Is.Zero);
            Assert.That(Snow.Model.Tint, Is.EqualTo(Vector4.zero));
            Recorder.Stop();
        }

        [UnityTest]
        public IEnumerator SunnySaysItOnceTheSnowHasFallenAMoment()
        {
            yield return Boot(tipSeen: false);
            Step(5f);
            Assert.IsFalse(Snow.TipAsked, "no snow, no line");
            Room.Set(AmbienceKind.Snow, true);
            Step(2f);
            Assert.IsFalse(Snow.TipAsked, "not at once");
            bool asked = false;
            for (int i = 0; i < 72 * 20 && !asked; i++) { Frame(); asked = Snow.TipAsked; }
            Assert.IsTrue(asked, "asked within a few seconds of the snow falling");
        }

        [UnityTest]
        public IEnumerator AFrameAllocatesNothing()
        {
            yield return Boot();
            Room.Set(AmbienceKind.Snow, true);
            Step(4f);
            WindowShow show = Room.WindowShow;
            RoomSnow snow = Snow;
            App.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 1u });
            var on = new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.8f, Lane = 1, Event = TakeEvent.PianoOn(64, 0.8f) };
            int frame = 0;
            for (int i = 0; i < 30; i++) { show.Model.Note(on, _audio); snow.Model.Note(on, _audio); _audio += Dt; show.Tick(Dt, true); snow.Tick(Dt, i % 2 == 0); }
            Assert.That(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    frame++;
                    if (frame % 6 == 0) { show.Model.Note(on, _audio); snow.Model.Note(on, _audio); }
                    _audio += Dt;
                    show.Tick(Dt, true);
                    snow.Tick(Dt, frame % 50 < 25);   // the room shown and hidden (VR and mixed reality)
                }
            }, Is.Not.AllocatingGCMemory());
            App.Events.Publish(new RecordingPlaybackEvent { Started = false });
        }
    }
}
