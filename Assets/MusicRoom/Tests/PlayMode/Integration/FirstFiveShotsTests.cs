// Pictures of the first five minutes (Graphics: Tools/unity.sh test-play --gpu), in the whole textured room with a
// simulated headset and the scripted player of FirstRunWalkthroughTests, written to
// $MUSICROOM_SHOTS_ROOT/phase-b (default Builds/Shots/firstfive/phase-b; the platform merge):
//   s0-start-help      Sunny beside the start panel, its ghost finger on Play here
//   (the fast first-timer tour, 2026-11-16)
//   f1-snow            Sunny by the side table, the ghost finger on the SNOW switch
//   f2-palm            the palm demo beside Sunny (how to open the menu)
//   f3-move            the home page, Move instruments lit and pressed; f3-pinch the ghost hand pinching an instrument
//   f4-set             the Instruments screen, a tile pressed; f4-one-alone One alone lit and pressed
//   f5-rec             the ghost finger on the deck's REC; f5-recordings the home page, Recordings lit and pressed
//   f6-reset-view      the home page, Reset view lit and pressed
//   f7-bye             the ghost finger on the deck's GUIDE, the goodbye
//   instruments-grid   the Instruments grid with the platform's tip (one scheduler: the tour's own waits its turn)
//   menu-tips          the Sunny screen's Tips list (the tour's, the platform's and every instrument's); menu-tips-end
//   lesson-drum-target a drum lesson waiting: the discs at 0.55 r toward the player, on the snare and the pad;
//                      lesson-tom-target a rack tom's target landed on that tom only
//   lesson-next        a finished lesson's panel with "Next: Mary Had a Little Lamb"
//   tip-hihat          a tip from the list: the pinch at the hi-hat stand
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Hands;
using MusicRoom.Menu;
using MusicRoom.Room;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Integration
{
    [Category("Graphics")]
    public class FirstFiveShotsTests
    {
        const float Dt = 1f / 72f;
        const int W = 1440, H = 1000;
        TestBoot _boot;
        App _app;
        MenuModule _menu;
        GuideModule _guide;
        double _t;
        string _dir;

        static string OutDir()
        {
            string dir = ShotDirs.FromRoot("phase-b") ?? Path.Combine(ShotDirs.Default("firstfive"), "phase-b");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            GC.Collect();
        }

        FirstRun Run => _guide.FirstRun;
        Sunny S => _guide.Sunny;

        void Step(int n = 1) { for (int i = 0; i < n; i++) { _t += Dt; _boot.Step(Dt); } }

        void StepUntil(Func<bool> done, float seconds, string what)
        {
            int n = (int)(seconds / Dt);
            for (int i = 0; i < n; i++) { if (done()) return; Step(); }
            Assert.IsTrue(done(), $"{what}: not within {seconds} s (step {Run.Step}, line {S.LineId})");
        }

        static HandPose Rest() =>
            HandPose.Open(HandSide.Right, new Vector3(0.6f, 2.4f, -1.2f), Quaternion.LookRotation(Vector3.forward, Vector3.up)).WithPointing();

        static HandPose Finger(Vector3 tip, Vector3 along) =>
            HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(along, Vector3.up)).WithPointing().WithIndexTipAt(tip);

        void PokeRow(RowKind id)
        {
            MenuController c = _menu.Controller;
            MenuRow row = null;
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == id) row = c.Row(i);
            Assert.That(row, Is.Not.Null);
            Transform root = _menu.Root;
            Step(20);
            foreach (float z in new[] { 0.03f, 0.012f, -0.002f, 0.001f, 0.03f })
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(root.TransformPoint(MenuMath.CanvasPoint((row.X0 + row.X1) / 2f, (row.Y0 + row.Y1) / 2f, z)), root.forward));
                Step();
            }
            _boot.Hands.SetPose(HandSide.Right, Rest());
            Step(2);
        }

        void Note(int midi)
        {
            object k = new object();
            IPianoApi p = _app.Get<IPianoApi>();
            p.PressMidi(midi, k, 0.6f); Step(3); p.ReleaseMidi(midi, k); Step(3);
        }

        void PressLive()
        {
            Assert.IsTrue(_app.Get<ILessonLive>().TryGetLiveTarget(out _, out _, out _, out int midi));
            Note(midi);
        }

        void Palm()
        {
            Vector3 head = _app.Head.position;
            Vector3 wrist = head + new Vector3(0.05f, -0.17f, 0.3f);
            _boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, Vector3.up, (head - (wrist + new Vector3(0f, 0.07f, 0f))).normalized));
            Step(50);
            _boot.Hands.SetPose(HandSide.Right, Rest());
            Step(10);
        }

        // =====================================================================================================

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator TheFirstFiveMinutesInPictures()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            _t = 40.0;
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true, audioClock: () => _t, startPanel: true);
            _boot.Hands.SetPose(HandSide.Right, Rest());
            _boot.Hands.SetTracked(HandSide.Left, false);
            Menu.MenuBootGuard.Begin();
            yield return _boot.Start();
            Menu.MenuBootGuard.End();
            _app = _boot.App;
            RoomModule room = null;
            foreach (var m in _app.Modules)
            {
                if (m is MenuModule mm) _menu = mm;
                if (m is GuideModule g) _guide = g;
                if (m is RoomModule r) room = r;
            }
            // The room ready: the rig up, every texture in, the probe rendered (real frames; the app itself steps by hand).
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Step(2);
            t0 = Time.realtimeSinceStartup;
            while ((room.TexturesPending > 0 || !room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 300f) { Step(); yield return null; }
            _dir = OutDir();
            Transform head = _app.Head;

            // S0: at 8 s Sunny comes beside the start panel and points at Play here.
            StepUntil(() => _menu.StartShowing, 3f, "the start panel");
            StepUntil(() => S.Visible && S.DemoKind == DemoKind.Press, 12f, "Sunny's help");
            Step(30);
            S.DemoT = 1.6f; Step();
            yield return Snap("s0-start-help", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.35f)));

            // The fast tour: every stop shown with the ghost hand at the real target and the real menu row lit.
            PokeRow(RowKind.PlayHere);
            StepUntil(() => Run.Step == FirstRunStep.Snow && S.DemoKind == DemoKind.Press, 12f, "1: the SNOW switch");
            Step(60);
            S.DemoT = 1.6f; Step();
            yield return Snap("f1-snow", head.position, Look(head.position, Vector3.Lerp(Run.Anchor.Point, S.Position, 0.3f)));
            StepUntil(() => Run.Step == FirstRunStep.Menu && S.DemoKind == DemoKind.Palm, 12f, "2: the palm demo");
            Step(36);
            S.DemoT = 0.95f; Step();
            yield return Snap("f2-palm", head.position, Look(head.position, S.Position + Vector3.down * 0.03f));
            StepUntil(() => Run.Step == FirstRunStep.Move && Run.SpotRow == FirstRun.RowMove && S.DemoKind == DemoKind.Press, 12f, "3: Move instruments");
            Step(30);
            S.DemoT = 1.6f; Step();
            yield return Snap("f3-move", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.25f)));
            StepUntil(() => Run.Step == FirstRunStep.Move && S.DemoKind == DemoKind.Pinch, 12f, "3: the pinch");
            Step(50);
            S.DemoT = 1.2f; Step();
            yield return Snap("f3-pinch", head.position, Look(head.position, Vector3.Lerp(Run.Anchor.Point, S.Position, 0.3f)));
            StepUntil(() => Run.Step == FirstRunStep.Set && S.DemoKind == DemoKind.Press && Run.SpotRow != null, 12f, "4: a tile");
            Step(30);
            S.DemoT = 1.6f; Step();
            yield return Snap("f4-set", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.25f)));
            StepUntil(() => Run.SpotRow == FirstRun.RowSingle && S.DemoKind == DemoKind.Press, 8f, "4: One alone");
            Step(20);
            S.DemoT = 0.8f; Step();
            yield return Snap("f4-one-alone", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.25f)));
            StepUntil(() => Run.Step == FirstRunStep.Recordings && Run.Aiming == FirstRunAim.DeckRec, 8f, "5: REC");
            Step(60);
            S.DemoT = 1.6f; Step();
            yield return Snap("f5-rec", head.position, Look(head.position, Vector3.Lerp(Run.Anchor.Point, S.Position, 0.3f)));
            StepUntil(() => Run.SpotRow == FirstRun.RowRecordings && S.DemoKind == DemoKind.Press, 12f, "5: Recordings");
            Step(30);
            S.DemoT = 1.6f; Step();
            yield return Snap("f5-recordings", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.25f)));
            StepUntil(() => Run.SpotRow == FirstRun.RowResetView && S.DemoKind == DemoKind.Press, 12f, "6: Reset view");
            Step(30);
            S.DemoT = 1.6f; Step();
            yield return Snap("f6-reset-view", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.25f)));
            StepUntil(() => Run.Step == FirstRunStep.Bye && Run.Aiming == FirstRunAim.DeckGuide && S.BubbleVisible, 12f, "the goodbye at GUIDE");
            Step(60);
            S.DemoT = 1.6f; Step();
            yield return Snap("f7-bye", head.position, Look(head.position, Vector3.Lerp(Run.Anchor.Point, S.Position, 0.3f)));
            StepUntil(() => !Run.Running && !S.Present, 12f, "Sunny leaves");
            ITutorial tut = _app.Get<ITutorial>();
            ILessonLive live = _app.Get<ILessonLive>();
            int guard;

            // The Instruments grid: the platform's tip, through the one scheduler.
            _menu.Show();
            Step(10);
            _menu.Go(MenuScreen.Instruments);
            StepUntil(() => _guide.ActiveTip == "tip_bring_grid" || _guide.TourTips.ShowingId == "tip_instruments", 6f, "an Instruments tip");
            StepUntil(() => S.BubbleVisible, 2f, "its caption");
            Step(40);
            yield return Snap("instruments-grid", head.position, Look(head.position, Vector3.Lerp(_menu.Root.position, S.Position, 0.3f)));
            StepUntil(() => !S.Present, 12f, "the tip ends");
            // The Tips list: every tip, the tour's first.
            _menu.Go(MenuScreen.Guide);
            Step(10);
            _menu.Go(MenuScreen.Tips);
            Step(20);
            yield return Snap("menu-tips", head.position, Look(head.position, _menu.Root.position));
            _menu.Controller.SetScroll(_menu.Controller.MaxScroll);
            Step(20);
            yield return Snap("menu-tips-end", head.position, Look(head.position, _menu.Root.position));
            _menu.Hide();
            Step(20);

            // A drum lesson waiting: the targets toward the player, on the surface.
            tut.Start("drums-first");
            Step(72 * 3 + 20);
            StepUntil(() => live.WaitingForPlayer, 6f, "the first drum target down (the kick pad)");
            _app.Get<IDrumsApi>().PlaybackHit("kick", 0.7f, new DrumStrikeInfo { Pos = 0.3f, Zone = "head" });
            Step(3);
            StepUntil(() => live.WaitingForPlayer, 6f, "the snare target down");
            Assert.IsTrue(live.TryGetLiveTarget(out Vector3 snareTarget, out _, out _, out _));
            Vector3 snare = snareTarget;
            yield return Snap("lesson-drum-target", head.position, Look(head.position, snare));
            Vector3 drumNear = snare + (head.position - snare).normalized * 0.45f + Vector3.up * 0.05f;
            yield return Snap("lesson-drum-target-close", drumNear, Look(drumNear, snare), 50f);
            tut.Stop();
            // A rack tom's target, landed (the integrator's question: it sits on that one tom, toward the player).
            global::MusicRoom.Lessons.Tutorial t2 = null;
            foreach (var m in _app.Modules) if (m is global::MusicRoom.Lessons.LessonsModule lm) t2 = lm.Tutorial;
            t2.Start(new Lesson
            {
                id = "tmp-toms", instrument = "drums", title = "Toms", bpm = 60,
                events = new[]
                {
                    new LessonEvent { beat = 0, len = 1, targets = new[] { new LessonTarget { kind = "tom1", label = "Tom 1" } } },
                    new LessonEvent { beat = 2, len = 1, targets = new[] { new LessonTarget { kind = "tom2", label = "Tom 2" } } },
                },
            });
            StepUntil(() => live.WaitingForPlayer, 8f, "the tom target down");
            Assert.IsTrue(live.TryGetLiveTarget(out Vector3 tom, out _, out _, out _));
            yield return Snap("lesson-tom-target", head.position, Look(head.position, tom));
            Vector3 tomNear = tom + (head.position - tom).normalized * 0.45f + Vector3.up * 0.05f;
            yield return Snap("lesson-tom-target-close", tomNear, Look(tomNear, tom), 50f);
            t2.Stop();

            // A finished lesson's panel: Next.
            tut.Start("piano-ode");
            _menu.Show();
            Step(10);
            guard = 0;
            while (tut.State != TutorialState.Done && guard++ < 72 * 120)
            {
                if (live.WaitingForPlayer) { Step(3); PressLive(); } else Step();
            }
            Step(20);
            _menu.Show();
            Step(20);
            Assert.That(_menu.Controller.Row(1).Id, Is.EqualTo(RowKind.Next), "the Next row");
            yield return Snap("lesson-next", head.position, Look(head.position, _menu.Root.position));
            _menu.Hide();
            Step(20);

            // A tip from the list: the pinch at the hi-hat stand.
            ((IGuideTour)_guide).PlayTip("tip_hihat");
            StepUntil(() => S.DemoKind == DemoKind.Pinch, 4f, "the pinch demo");
            Step(10);
            S.DemoT = 0.9f; Step();
            yield return Snap("tip-hihat", head.position, Look(head.position, Vector3.Lerp(S.Anchor.Point, S.Position, 0.3f)));

            foreach (string n in new[] { "s0-start-help", "f1-snow", "f2-palm", "f3-move", "f3-pinch", "f4-set", "f4-one-alone", "f5-rec",
                "f5-recordings", "f6-reset-view", "f7-bye", "instruments-grid", "menu-tips", "lesson-drum-target", "lesson-tom-target", "lesson-next", "tip-hihat" })
                Assert.IsTrue(File.Exists(Path.Combine(_dir, n + ".png")), n);
            yield return null;
        }

        static Quaternion Look(Vector3 from, Vector3 at) => Quaternion.LookRotation(at - from, Vector3.up);

        // One real Unity frame first: the lesson blocks are drawn with Graphics.RenderMeshInstanced, queued once per app
        // frame, and many manual app frames inside one Unity frame would all show (a falling block as a column). Then one
        // app frame, so exactly this frame's blocks are queued for the shot.
        IEnumerator Snap(string name, Vector3 position, Quaternion rotation, float fov = 70f)
        {
            yield return null;
            Step();
            Shot(name, position, rotation, fov);
        }

        void Shot(string name, Vector3 position, Quaternion rotation, float fov = 70f)
        {
            var go = new GameObject("FirstFiveShotCamera");
            var cam = go.AddComponent<Camera>();
            Camera main = _app.Rig.HeadCamera;
            cam.CopyFrom(main);
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.allowHDR = false;
            var rt = new RenderTexture(new RenderTextureDescriptor(W, H, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 });
            rt.Create();
            cam.targetTexture = rt;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(go);
            Debug.Log($"[FirstFive] shot {name}");
        }
    }
}
