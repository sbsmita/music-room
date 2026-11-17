// Pictures of the ghost hands (Category Graphics: `Tools/unity.sh test-play --gpu`), to look at and judge:
//   - a piano take: from the seat, and close up over the keys (fingers on the keys being played), and with the player's
//     own hand there (the ghost hand faint);
//   - a drum beat: the sticks over the kit from the seat and from the side;
//   - a harp run: a finger at the strings;
//   - mixed reality (the room hidden, grey standing in for passthrough): the piano's hands;
//   - the draw cost from the seat while they play (the hands are one renderer).
// Every camera is placed from the instruments' own transforms and the seat (never room coordinates). Output:
// $MUSICROOM_SHOTS_ROOT/ghost-hands, else Builds/Shots/ghost-hands.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Lessons.Ghost;
using MusicRoom.Room;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode
{
    [Category("Graphics")]
    public class GhostHandsShotsTests
    {
        const int W = 1280, H = 720;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        double _t = 20.0;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _read;
        string _dir;

        App App => App.Current;
        GhostHands Ghost => GhostHandsModule.Of(App);
        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }
        void StepSeconds(float s) => Step(Mathf.RoundToInt(s / Dt));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cam != null) Object.Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_read != null) Object.Destroy(_read);
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = fov;
        }

        void Shot(string name)
        {
            _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _read.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(_dir, name + ".png");
            File.WriteAllBytes(path, _read.EncodeToPNG());
            Debug.Log($"[Ghost] shot {name}: {path}");
        }

        IEnumerator Draws(System.Action<long> result)
        {
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 4; i++) { Step(); _cam.Render(); yield return null; }
            result(draws.Valid ? draws.LastValue : -1);
            draws.Dispose();
        }

        Bounds Played(InstrumentKind kind)
        {
            IInstrument inst = App.Instruments.First(x => x.Kind == kind);
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in inst.PlayedParts) { if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return any ? b : new Bounds(inst.Root.position + Vector3.up * 0.8f, Vector3.one * 0.4f);
        }

        /// <summary>Steps until the next frame in which the given audio time has just passed.</summary>
        void StepTo(double start, float t) { while (_t < start + t) Step(); }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator GhostHandsPlayingBack()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _boot = new TestBoot(headless: false, manualFrames: true, audioClock: () => _t);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, InstrumentIds.Of(InstrumentKind.Piano));
            _boot.Settings.SetString(MusicRoom.Guide.GuideModule.KeyTipsSeen, "tip_listen_back,tip_play_brings,tip_ghost_hands");
            yield return _boot.Start();
            float t0 = Time.realtimeSinceStartup;
            var layout = App.Get<ILayoutService>();
            var room = App.Get<IRoom>() as RoomModule;
            while ((!layout.FirstArrangeDone || room.TexturesPending > 0 || !room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 120f) { Step(); yield return null; }
            _dir = ShotDirs.FromRoot("ghost-hands") ?? ShotDirs.Default("ghost-hands");
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            _cam = new GameObject("GhostShotCamera").AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = XrMode.VrBackground;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.aspect = (float)W / H;
            // the player's hands out of the way
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(App.Head.position + Vector3.up * 0.6f));
            _boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, App.Head.position + Vector3.up * 0.6f + Vector3.left * 0.3f, Vector3.down, Vector3.forward));
            StepSeconds(0.5f);
            IRecorder rec = App.Get<IRecorder>();
            Vector3 seat = App.Head.position;

            // ---- the piano
            List<TakeEvent> song = GhostHandsPlayTests.PianoSong(0.6f, 6);
            double start = _t;
            Assert.IsTrue(rec.PlayEvents(song, 8.0));
            StepTo(start, 1.25f);   // the left hand's chord down, the right on its third note
            Bounds keys = Played(InstrumentKind.Piano);
            Aim(seat, keys.center + Vector3.up * 0.05f, 70f); Shot("piano_seat");
            Transform pr = App.Get<IPianoApi>().Root;
            Vector3 mid = (Ghost.PalmWorld(InstrumentKind.Piano, 0) + Ghost.PalmWorld(InstrumentKind.Piano, 1)) * 0.5f;
            Aim(mid + pr.TransformVector(new Vector3(0f, 0.28f, -0.32f)), mid + pr.TransformVector(new Vector3(0f, -0.03f, 0.04f)), 45f); Shot("piano_close_front");
            Vector3 rh = Ghost.PalmWorld(InstrumentKind.Piano, 1);
            Aim(rh + pr.TransformVector(new Vector3(0.22f, 0.12f, -0.08f)), rh + pr.TransformVector(new Vector3(0f, -0.03f, 0.05f)), 40f); Shot("piano_close_side_right");
            long draws = -1;
            Aim(seat, keys.center, 70f);
            yield return Draws(v => draws = v);
            Debug.Log($"[Ghost] seat draws toward the piano while the ghost hands play: {draws}");
            StepTo(start, 3.65f);
            // the player's own right hand right over the ghost's
            for (int i = 0; i < 30; i++) { _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(Ghost.PalmWorld(InstrumentKind.Piano, 1) + Vector3.up * 0.03f)); Step(); }
            rh = Ghost.PalmWorld(InstrumentKind.Piano, 1);
            Aim(rh + pr.TransformVector(new Vector3(0f, 0.3f, -0.3f)), rh, 50f); Shot("piano_players_hand_over_ghost");
            _boot.Hands.SetPose(HandSide.Right, FixtureContext.IndexAt(App.Head.position + Vector3.up * 0.6f));
            // ---- mixed reality: the room hidden, grey for passthrough
            room.SetVisible(false);
            _cam.backgroundColor = new Color(0.42f, 0.42f, 0.4f);
            StepSeconds(0.4f);
            mid = (Ghost.PalmWorld(InstrumentKind.Piano, 0) + Ghost.PalmWorld(InstrumentKind.Piano, 1)) * 0.5f;
            Aim(seat, keys.center, 60f); Shot("mr_piano_seat");
            Aim(mid + pr.TransformVector(new Vector3(0f, 0.28f, -0.32f)), mid, 45f); Shot("mr_piano_close");
            room.SetVisible(true);
            _cam.backgroundColor = XrMode.VrBackground;
            rec.Stop();
            StepSeconds(1.5f);
            Assert.That(Ghost.ShownHands, Is.EqualTo(0));

            // ---- the drums (the playback brings them)
            List<TakeEvent> beat = GhostHandsPlayTests.DrumBeat(1.8f, 4);
            start = _t;
            Assert.IsTrue(rec.PlayEvents(beat, 7.0));
            StepTo(start, 2.3f + 0.12f);   // between hits: the sticks up
            Bounds kit = Played(InstrumentKind.Drums);
            Aim(App.Head.position, kit.center, 75f); Shot("drums_seat_up");
            StepTo(start, 2.8f);           // a hat and a snare hit together
            Aim(App.Head.position, kit.center, 75f); Shot("drums_seat_hit");
            Transform dr = App.Get<IDrumsApi>().Root;
            Vector3 hands = (Ghost.PalmWorld(InstrumentKind.Drums, 0) + Ghost.PalmWorld(InstrumentKind.Drums, 1)) * 0.5f;
            Aim(hands + dr.TransformVector(new Vector3(-0.3f, 0.32f, -0.42f)), hands + dr.TransformVector(new Vector3(0f, -0.12f, 0.2f)), 55f); Shot("drums_behind_left");
            rec.Stop();
            StepSeconds(1.5f);

            // ---- the harp
            List<TakeEvent> run = GhostHandsPlayTests.HarpRun(2.0f, 10);
            start = _t;
            Assert.IsTrue(rec.PlayEvents(run, 6.0));
            StepTo(start, 2.0f + 0.35f * 3);
            Bounds strings = Played(InstrumentKind.Harp);
            Aim(App.Head.position, strings.center, 70f); Shot("harp_seat");
            Vector3 hp = Ghost.Contact(InstrumentKind.Harp, Ghost.Alpha(InstrumentKind.Harp, 0) > Ghost.Alpha(InstrumentKind.Harp, 1) ? 0 : 1);
            Vector3 back = App.Head.position - hp; back.y = 0f; back = back.normalized;
            Aim(hp + back * 0.45f + Vector3.up * 0.12f, hp, 45f); Shot("harp_close");
            rec.Stop();
            StepSeconds(1.5f);
        }
    }
}
