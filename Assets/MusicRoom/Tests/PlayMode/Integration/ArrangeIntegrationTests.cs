// The features added after the first headset test, end to end with every real module (simulated headset, manual frames
// at 72 Hz, scripted index-finger pokes on the real panel): the home page's "Move instruments" row turns arrange mode on
// and closes the menu, the Done / Reset buttons float within reach and work, the palm menu's row turns it off again;
// nothing plays while arranging (a finger through a piano key is silent) and plays again after Done; the piano's stand
// follows the floor when the keyboard is raised or lowered; Exit asks first, forgets after 3 s, then fades the sound,
// saves and quits through the test hook; the start panel opens at launch in front of the player, offers "Play in my
// room" only where it works, and its answer lets Sunny's first-launch tour begin.
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.Piano;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class ArrangeIntegrationTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        MenuModule _menu;
        LayoutModule _layout;
        RoomProbe _mr;
        readonly List<TakeEvent> _events = new List<TakeEvent>();

        /// An IMixedReality in front of the MR module (Order 65 &lt; 70) that says passthrough works and counts Enter.
        sealed class RoomProbe : AppModule, IMixedReality
        {
            public override int Order => 65;
            public override void Init(App app) => app.Register<IMixedReality>(this);
            public bool Available { get; set; } = true;
            public bool Active { get; private set; }
            public bool IsPlacing => false;
            public MrStatus Status => MrStatus.Idle;
            public bool HasSpaceSetup => false;
            public int Enters;
            public Task Enter() { Enters++; return Task.CompletedTask; }
            public void Exit() { }
            public Task RunSpaceSetup() => Task.CompletedTask;
            public bool TryGetPianoPose(out Pose pose, out bool onTable) { pose = default; onTable = false; return false; }
            public void StartPlacing() { }
        }

        IEnumerator BootApp(bool startPanel = false, bool roomProbe = false)
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true, startPanel: startPanel);
            _boot.Hands.SetPose(HandSide.Right, Finger(new Vector3(0.3f, 1.6f, -0.5f), Vector3.forward));   // out of the way
            if (roomProbe) { _mr = new RoomProbe(); _boot.Options.ExtraModules.Add(_mr); }
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            foreach (var m in _app.Modules)
            {
                if (m is MenuModule mm) _menu = mm;
                if (m is LayoutModule lm) _layout = lm;
            }
            Assert.That(_menu, Is.Not.Null);
            Assert.That(_layout, Is.Not.Null);
            _events.Clear();
            _app.Events.Subscribe<RecorderEvent>(e => _events.Add(e.Event));
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            _boot.Step(Dt, 2);
            Assert.IsTrue(_layout.FirstArrangeDone);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _menu = null;
            _layout = null;
            _mr = null;
        }

        static HandPose Finger(Vector3 tip, Vector3 along) =>
            HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.LookRotation(along, Vector3.up)).WithPointing().WithIndexTipAt(tip);

        /// A row's centre on a panel at height z (real metres) above its face; scale = the panel's (0.5 for the buttons).
        static Vector3 OnRow(Transform root, MenuRow r, float z, float scale = 1f) =>
            root.TransformPoint(scale * MenuMath.CanvasPoint((r.X0 + r.X1) / 2f, (r.Y0 + r.Y1) / 2f, z / scale));

        /// The poke with the right index finger: 3 cm, 1.2 cm, -2 mm, +1 mm, 3 cm, one frame each.
        void Poke(Transform root, MenuRow row, float scale = 1f)
        {
            _boot.Step(Dt, 20);   // past the 250 ms debounce of the last poke
            foreach (float z in new[] { 0.03f, 0.012f, -0.002f, 0.001f, 0.03f })
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(OnRow(root, row, z, scale), root.forward));
                _boot.Step(Dt);
            }
            _boot.Hands.SetPose(HandSide.Right, Finger(OnRow(root, row, 0.15f, scale), root.forward));
            _boot.Step(Dt, 2);
        }

        MenuRow HomeRow(RowKind id)
        {
            MenuController c = _menu.Controller;
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == id) return c.Row(i);
            Assert.Fail("no " + id + " row on " + c.Screen);
            return null;
        }

        MenuRow BarRow(RowKind id)
        {
            MenuController c = _menu.ArrangeButtons;
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == id) return c.Row(i);
            Assert.Fail("no " + id + " button");
            return null;
        }

        void OpenMenu()
        {
            _menu.Show();
            _boot.Step(Dt, 15);   // open animation, relayout
            Assert.IsTrue(_menu.Shown);
        }

        [UnityTest]
        public IEnumerator TheMenuRowStartsArrangeModeAndTheButtonsEndOrResetIt()
        {
            yield return BootApp();
            OpenMenu();
            Assert.That(_menu.Controller.Row(2).Label, Is.EqualTo("Move instruments"), "third on the home page");
            Poke(_menu.Root, HomeRow(RowKind.Arrange));
            Assert.IsTrue(_layout.Arranging, "the row turns arrange mode on");
            Assert.IsFalse(_menu.Shown, "the menu closes");
            Assert.IsTrue(_menu.ArrangeButtonsShown, "the Done / Reset buttons appear");
            // Within easy seated reach: 35 cm ahead of the eyes, 15 cm below them, 10 cm to the left, facing the eyes.
            Transform head = _app.Head;
            Vector3 f = head.forward; f.y = 0f; f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 d = _menu.ArrangeRoot.position - head.position;
            Assert.That(Vector3.Dot(d, f), Is.EqualTo(0.35f).Within(0.01f), "ahead");
            Assert.That(d.y, Is.EqualTo(-0.15f).Within(0.01f), "below the eyes");
            Assert.That(Vector3.Dot(d, right), Is.EqualTo(-0.10f).Within(0.01f), "to the left");
            Assert.That(Vector3.Dot(_menu.ArrangeRoot.forward, d.normalized), Is.GreaterThan(0.99f), "facing the eyes");
            _boot.Step(Dt, 15);
            Assert.That(_menu.ArrangeButtons.RowCount, Is.EqualTo(2));

            // Reset keeps arranging and lays the room out again.
            int resets = _layout.ArrangeResets;
            Poke(_menu.ArrangeRoot, BarRow(RowKind.Reset), MenuStyle.ArrangeScale);
            Assert.That(_layout.ArrangeResets, Is.EqualTo(resets + 1), "Reset");
            Assert.IsTrue(_layout.Arranging);
            // Done leaves.
            Poke(_menu.ArrangeRoot, BarRow(RowKind.Done), MenuStyle.ArrangeScale);
            Assert.IsFalse(_layout.Arranging, "Done");
            Assert.IsFalse(_menu.ArrangeButtonsShown);

            Assert.IsFalse(_layout.MoveOn, "Done turns the setting off");
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyMoveInstruments, 1), Is.EqualTo(0), "persisted off");

            // The palm menu's row is the setting itself (On while moving); turning it off keeps the menu open.
            OpenMenu();
            Poke(_menu.Root, HomeRow(RowKind.Arrange));
            Assert.IsTrue(_layout.Arranging);
            Assert.That(_boot.Settings.GetInt(AppSettings.KeyMoveInstruments, 0), Is.EqualTo(1), "persisted on");
            OpenMenu();
            Assert.That(HomeRow(RowKind.Arrange).Label, Is.EqualTo("Move instruments"));
            Assert.IsTrue(HomeRow(RowKind.Arrange).On, "shown On");
            Poke(_menu.Root, HomeRow(RowKind.Arrange));
            Assert.IsFalse(_layout.Arranging);
            Assert.IsTrue(_menu.Shown, "turning it off keeps the menu open");
            yield return null;
        }

        PianoInstrument Piano()
        {
            foreach (var m in _app.Modules) if (m is PianoModule pm) return pm.Piano;
            return null;
        }

        int PianoOns() { int n = 0; foreach (var e in _events) if (e.type == TakeEventType.PianoOn) n++; return n; }

        void PressKey(PianoInstrument piano, int midi)
        {
            piano.TryGetKey(midi, out var k);
            Vector3 down = Vector3.down;
            for (float y = 0.03f; y >= -0.014f; y -= 0.004f)
            {
                _boot.Hands.SetPose(HandSide.Right, Finger(piano.transform.TransformPoint(new Vector3(k.X, y, 0.03f)), Vector3.forward + down));
                _boot.Step(Dt);
            }
            _boot.Hands.SetPose(HandSide.Right, Finger(piano.transform.TransformPoint(new Vector3(k.X, 0.08f, 0.03f)), Vector3.forward));
            _boot.Step(Dt, 12);
        }

        [UnityTest]
        public IEnumerator NothingPlaysWhileArrangingAndThePianoStandFollowsTheFloor()
        {
            yield return BootApp();
            PianoInstrument piano = Piano();
            Assert.That(piano, Is.Not.Null);
            _layout.BeginArrange();
            _boot.Step(Dt, 3);
            PressKey(piano, 60);
            Assert.That(PianoOns(), Is.EqualTo(0), "a finger through a key is silent while arranging");
            _layout.EndArrange();
            _boot.Step(Dt, 3);
            _boot.Step(Dt, 80);   // past the play guard and a new phrase
            PressKey(piano, 62);
            Assert.That(PianoOns(), Is.EqualTo(1), "after Done the piano plays again");

            // A pinch on its keys picks the piano up; the hand rising 15 cm lifts it 13 cm (the 2 cm dead band, then 1:1;
            // 2026-11-11), its stand stretched so its feet stay on the floor; a drag away carries it at that height.
            _boot.Step(Dt, 80);
            _layout.BeginArrange();
            piano.TryGetKey(64, out var key);
            Vector3 at = piano.transform.TransformPoint(new Vector3(key.X, 0.04f, 0.06f));
            HandPose pinch = HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.identity).WithIndexTipAt(at).WithPinch(0f);
            for (int i = 0; i < 8; i++) { _boot.Hands.SetPose(HandSide.Right, pinch); _boot.Step(Dt); }
            Assert.That(_layout.HeldRig, Is.Not.Null);
            Assert.That(_layout.HeldRig.Id, Is.EqualTo(RigId.Piano), "the piano, held by its keys");
            float y0 = piano.transform.position.y;
            Assert.That(y0, Is.EqualTo(0.72f).Within(0.01f));
            Vector3 p0 = piano.transform.position;
            for (int i = 1; i <= 20; i++) { _boot.Hands.SetPose(HandSide.Right, pinch.Translated(Vector3.up * (0.15f * i / 20f))); _boot.Step(Dt); }
            _boot.Step(Dt, 24);   // held there: the lift's filter catches up
            yield return null;   // LateUpdate: the stand follows
            float lifted = y0 + 0.15f - MoveConstants.LiftDeadband;
            Assert.That(piano.transform.position.y, Is.EqualTo(lifted).Within(0.004f), "the hand rose 15 cm: the keys 13 cm higher");
            Assert.That(piano.StandScale, Is.GreaterThan(1.15f), "the stand stretched");
            Assert.That(piano.StandRenderer.bounds.min.y, Is.EqualTo(0f).Within(0.012f), "its feet on the floor");
            y0 = piano.transform.position.y;
            Vector3 side = _app.Head.forward; side.y = 0f; side.Normalize();   // away from the player: nothing in the way
            for (int i = 1; i <= 20; i++) { _boot.Hands.SetPose(HandSide.Right, pinch.Translated(Vector3.up * 0.15f + side * (0.2f * i / 20f))); _boot.Step(Dt); }
            _boot.Step(Dt, 10);
            Assert.That(Vector3.Dot(piano.transform.position - p0, side), Is.EqualTo(0.2f).Within(0.03f), "dragged 20 cm away");
            Assert.That(piano.transform.position.y, Is.EqualTo(y0).Within(1e-4f));
            _boot.Hands.SetPose(HandSide.Right, Finger(at + Vector3.up * 0.2f, Vector3.forward));
            _boot.Step(Dt, Mathf.CeilToInt(MoveConstants.ReleaseGrace / Dt) + 2);
            Assert.That(_layout.HeldRig, Is.Null);
            _layout.EndArrange();
        }

        [UnityTest]
        public IEnumerator ExitAsksFirstThenFadesTheSoundSavesAndQuits()
        {
            yield return BootApp();
            OpenMenu();
            MenuController c = _menu.Controller;
            Assert.That(c.Row(c.RowCount - 1).Id, Is.EqualTo(RowKind.Exit), "Exit is the home page's last row");
            Poke(_menu.Root, HomeRow(RowKind.Exit));
            _boot.Step(Dt, 2);
            Assert.That(HomeRow(RowKind.Exit).Label, Is.EqualTo("Tap again to exit"));
            Assert.That(_boot.QuitCalls, Is.EqualTo(0));
            _boot.Step(Dt, 224);   // 3.1 s
            Assert.That(HomeRow(RowKind.Exit).Label, Is.EqualTo("Exit"), "the question times out after 3 s");
            Assert.That(_boot.QuitCalls, Is.EqualTo(0));

            float volume = AudioListener.volume;
            int saves = _boot.Settings.Saves;
            Poke(_menu.Root, HomeRow(RowKind.Exit));
            Poke(_menu.Root, HomeRow(RowKind.Exit));   // about 0.4 s later
            Assert.IsTrue(_menu.Quitting, "the second poke closes the app");
            for (int i = 0; i < 12 && !_menu.QuitCalled; i++) _boot.Step(Dt);
            Assert.IsTrue(_menu.QuitCalled, "after the fade");
            Assert.That(_boot.QuitCalls, Is.EqualTo(1), "through BootOptions.Quit: the test runner keeps running");
            Assert.That(_app.QuitRequests, Is.EqualTo(1));
            Assert.That(AudioListener.volume, Is.EqualTo(0f).Within(1e-4f), "the sound faded out");
            Assert.That(_boot.Settings.Saves, Is.GreaterThan(saves), "the settings were saved");
            Assert.That(volume, Is.GreaterThan(0f));
            _boot.Step(Dt, 30);
            Assert.That(_boot.QuitCalls, Is.EqualTo(1), "once");
            yield return _boot.Stop();
            _boot = null;
            Assert.That(AudioListener.volume, Is.EqualTo(volume).Within(1e-4f), "the next boot hears again");
        }

        [UnityTest]
        public IEnumerator TheExitFadeTakesAboutAHundredAndFiftyMilliseconds()
        {
            yield return BootApp();
            OpenMenu();
            Poke(_menu.Root, HomeRow(RowKind.Exit));
            Poke(_menu.Root, HomeRow(RowKind.Exit));
            // The confirming touch plus the four frames Poke() runs after it (about 69 ms): under way, not done.
            Assert.IsTrue(_menu.Quitting);
            Assert.IsFalse(_menu.QuitCalled);
            Assert.That(AudioListener.volume, Is.InRange(0.05f, 0.95f), "fading");
            int frames = 0;
            while (!_menu.QuitCalled && frames < 20) { _boot.Step(Dt); frames++; }
            Assert.IsTrue(_menu.QuitCalled);
            Assert.That((frames + 5) * Dt, Is.InRange(0.13f, 0.19f), "about 150 ms after the confirming poke");
        }

        [UnityTest]
        public IEnumerator TheStartPanelOpensInFrontAndItsAnswerLetsTheTourBegin()
        {
            yield return BootApp(startPanel: true, roomProbe: true);
            IGuide guide = _app.Get<IGuide>();
            for (int i = 0; i < 10 && !_menu.Shown; i++) _boot.Step(Dt);
            Assert.IsTrue(_menu.StartShowing, "the start panel opens once the room is ready");
            Assert.IsTrue(((IMenuPanel)_menu).StartPending);
            Assert.That(_menu.Screen, Is.EqualTo(MenuScreen.Start));
            _boot.Step(Dt, 15);
            Assert.That(_menu.Controller.HeaderTitle, Is.EqualTo("VR Music Room"));
            Assert.That(_menu.Controller.RowCount, Is.EqualTo(2), "Play here and Play in my room");
            // In front of the player along the look direction, nearly at eye level, facing them (not at a palm: no hand is up).
            Transform head = _app.Head;
            Vector3 d = _menu.Root.position - head.position;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            Vector3 f = head.forward; f.y = 0f; f.Normalize();
            Assert.That(Vector3.Dot(flat.normalized, f), Is.GreaterThan(0.95f), "straight ahead");
            // The keyboard opens within a foot of the player, so the panel's piano rule keeps it just in front of the key
            // fronts but never nearer than MenuMath.ReachMin (0.30 m).
            Assert.That(flat.magnitude, Is.InRange(MenuMath.ReachMin - 0.005f, 0.6f));
            Assert.That(d.y, Is.InRange(-0.1f, 0.05f), "about eye level");
            // Sunny's first-launch tour waits for the answer (3 s of frames).
            _boot.Step(Dt, 216);
            Assert.IsFalse(guide.Present, "the tour waits");
            Assert.IsTrue(_menu.StartShowing, "the panel waits too");
            // "Play in my room": mixed reality is entered, the panel closes, the tour may start.
            Poke(_menu.Root, HomeRow(RowKind.PlayRoom));
            Assert.That(_mr.Enters, Is.EqualTo(1), "the existing mixed-reality entry");
            Assert.IsFalse(_menu.Shown);
            Assert.IsFalse(((IMenuPanel)_menu).StartPending);
            for (int i = 0; i < 520 && !guide.Present; i++) _boot.Step(Dt);
            Assert.IsTrue(guide.Present, "Sunny's tour starts after the answer, on the first launch");
            // The palm menu opens on its home page afterwards.
            OpenMenu();
            Assert.That(_menu.Screen, Is.EqualTo(MenuScreen.Home));
        }

        [UnityTest]
        public IEnumerator TheStartPanelOffersOnlyPlayHereWithoutPassthrough()
        {
            yield return BootApp(startPanel: true, roomProbe: true);
            _mr.Available = false;
            for (int i = 0; i < 20; i++) _boot.Step(Dt);
            Assert.IsTrue(_menu.StartShowing);
            _menu.Redraw();
            _boot.Step(Dt, 3);
            Assert.That(_menu.Controller.RowCount, Is.EqualTo(1), "Play in my room is hidden");
            Assert.That(_menu.Controller.Row(0).Id, Is.EqualTo(RowKind.PlayHere));
            // Whatever puts it away unanswered (the headset off, a session change) only hides it: it comes back.
            _menu.Toggle();
            Assert.IsFalse(_menu.Shown);
            _boot.Step(Dt, 3);
            Assert.IsTrue(_menu.StartShowing, "the unanswered start panel comes back");
            Poke(_menu.Root, HomeRow(RowKind.PlayHere));
            Assert.IsFalse(_menu.Shown);
            Assert.IsFalse(((IMenuPanel)_menu).StartPending);
            Assert.That(_mr.Enters, Is.EqualTo(0), "Play here stays in the music room");
            _boot.Step(Dt, 30);
            Assert.IsFalse(_menu.Shown, "and does not come back");
        }
    }
}
