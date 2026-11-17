// The palm menu in the running app with scripted hands (ARCHITECTURE.md section 16): registration and the rig, the
// summon gesture end to end (FakeHandSource palm -> panel in front of the hand, MenuEvent Open, the ring while charging),
// an index-fingertip poke on a row, the hover glow on the curved face, hide on unmount and the fade, the grab zone
// following the panel, the clear-out call on opening, a lesson start showing the lesson screen, and the per-frame path
// allocating nothing. Every boot goes through TestBoot (fresh MemorySettingsStore, temporary DataPath). A placement
// probe (Order 5) stands in for Layout's IPlacementService: it counts ClearPanel calls and moves nothing, so these tests
// check the menu's own placement whatever Layout's clear-out does.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Menu
{
    public class MenuPlayModeTests
    {
        /// Records what the instruments step left in the hover-glow pool (Order 5; ticks before the HoverGlow phase draws).
        sealed class GlowProbe : AppModule
        {
            public int Pending;
            public HoverGlowSpec Spec;
            App _app;
            public override int Order => 5;
            public override void Init(App app) { _app = app; }
            public override void Tick(FramePhase phase, float dt)
            {
                if (phase != FramePhase.Instruments) return;
                Pending = _app.HoverGlow.Pending;
                if (Pending > 0) Spec = _app.HoverGlow.Spec(0);
            }
        }

        /// IPlacementService registered before Layout (Order 5 &lt; 100): App.Get returns the first registered service.
        sealed class PlacementProbe : AppModule, IPlacementService
        {
            public readonly List<Rig> Cleared = new List<Rig>();
            public override int Order => 5;
            public override void Init(App app) => app.Register<IPlacementService>(this);
            public bool Resolve(bool useRoomBox) => false;
            public void SettleOne(Rig rig) { }
            public void ClearPanel(Rig panel) => Cleared.Add(panel);
            public Vector3? RoomShift() => null;
            public float EyeDistance(Rig rig) => 0f;
        }

        /// A tutorial registered before the Lessons module (Order 35 &lt; 40), so app.Get&lt;ITutorial&gt; returns it.
        sealed class TutorialProbe : AppModule, ITutorial
        {
            App _app;
            public override int Order => 35;
            public override void Init(App app) { _app = app; app.Register<ITutorial>(this); }
            public TutorialState State { get; set; }
            public string LessonId { get; set; }
            public InstrumentKind Instrument { get; set; }
            public int Version { get; set; }
            public float Countdown { get; set; }
            public string Hint { get; set; }
            public float Progress => 0f;
            public int EventsDone { get; set; }
            public int EventsTotal { get; set; }
            public TutorialSummary Summary { get; set; }
            public void Start(string lessonId)
            {
                LessonId = lessonId; State = TutorialState.Countdown; Countdown = 3f; Version++;
                _app.Events.Publish(new TutorialEvent { State = State, LessonId = lessonId, Version = Version });
            }
            public void Stop() { State = TutorialState.Idle; Version++; _app.Events.Publish(new TutorialEvent { State = State, LessonId = LessonId, Version = Version }); }
        }

        static float[] s_keep;
        TestBoot _boot;
        FakeHandSource _fake;
        GlowProbe _glow;
        TutorialProbe _tutorial;
        PlacementProbe _placement;
        readonly List<MenuEvent> _events = new List<MenuEvent>();

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            _fake = _boot.Hands;
            _glow = new GlowProbe();
            _tutorial = new TutorialProbe();
            _placement = new PlacementProbe();
            _boot.Options.ExtraModules.Add(_glow);
            _boot.Options.ExtraModules.Add(_tutorial);
            _boot.Options.ExtraModules.Add(_placement);
            MenuBootGuard.Begin();   // the known SynthSource boot race only (CONTRACT-REQUESTS.md 3)
            yield return _boot.Start();
            MenuBootGuard.End();
            var app = App.Current;
            _events.Clear();
            app.Events.Subscribe<MenuEvent>(e => _events.Add(e));
            // The desktop rig places the head at (0, 1.2, -0.1) after ~0.5 s; the layout arranges once it has.
            float t0 = Time.realtimeSinceStartup;
            var layout = app.Get<ILayoutService>();
            while ((app.Rig.State == XrState.Starting || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Assert.That(app.Rig.State, Is.EqualTo(XrState.Desktop));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            yield return _boot.Stop();
        }

        static MenuModule Menu => (MenuModule)App.Current.Get<IMenuPanel>();

        /// The right hand pointing at the panel (fingers along the panel's +z) with its index tip at a canvas pixel,
        /// z metres in front of the curved face.
        HandPose PointAt(MenuModule m, float px, float py, float z)
        {
            Vector3 world = m.Root.TransformPoint(MenuMath.CanvasPoint(px, py, z));
            var rot = Quaternion.LookRotation(m.Root.forward, Vector3.up);
            var pose = HandPose.Open(HandSide.Right, world - m.Root.forward * 0.18f, rot).WithPointing().WithIndexTipAt(world);
            _fake.SetPose(HandSide.Right, pose);
            return pose;
        }

        static void RowCentre(MenuModule m, int row, out float px, out float py)
        {
            var r = m.Controller.Row(row);
            px = (r.X0 + r.X1) / 2f;
            py = (r.Y0 + r.Y1) / 2f;
        }

        [UnityTest]
        public IEnumerator RegistersThePanelAsAnInstrumentAndARig()
        {
            var app = App.Current;
            var m = Menu;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Order, Is.EqualTo(60));
            var last = app.Instruments[app.Instruments.Count - 1];
            Assert.That(last, Is.SameAs(m), "TickOrder 50: the last instrument (spec 05 section 1.1)");
            Assert.That(m.Kind, Is.EqualTo(InstrumentKind.Menu));
            Assert.IsTrue(m.IsPresent, "ticked while hidden: the summon must run");
            Assert.IsFalse(m.AcceptsStickPointers);
            Assert.That(m.PlaySurfaceAt(m.Root.position), Is.EqualTo(PlaySurface.None), "a pinch on the panel is a grab, never play");
            var rig = app.Get<IRigRegistry>().Get(RigId.Lessons);
            Assert.That(rig, Is.SameAs(m.Rig));
            Assert.That(rig.Group, Is.SameAs(m.Root));
            Assert.That(rig.Instruments, Does.Contain(m));
            Assert.That(rig.HomeAll, Is.EqualTo(MenuConstants.HomeSeat));
            Assert.That(m.GrabZone.HasValue, Is.False, "hidden: no zone");
            // Rig.Handles is filled once (ARCHITECTURE.md section 8): the back plate and the face; Layout measures only
            // shown renderers, and the hidden panel's are out of the scene.
            IReadOnlyList<Renderer> handles = rig.Handles;
            Assert.That(handles.Count, Is.EqualTo(2), "face and back plate");
            CollectionAssert.Contains(handles, m.View.Face);
            CollectionAssert.Contains(handles, m.View.Back);
            foreach (var h in handles) Assert.IsFalse(h.gameObject.activeInHierarchy, "hidden: no footprint");
            Assert.IsFalse(rig.Zone.Contains(m.Root.position));
            m.Show();
            Assert.That(rig.Handles, Is.SameAs(handles), "never rebuilt");
            foreach (var h in handles) Assert.IsTrue(h.enabled && h.gameObject.activeInHierarchy, "shown: measured by Layout");
            Assert.IsTrue(rig.Zone.Contains(m.Root.position));
            Assert.That(m.Root.localScale, Is.EqualTo(Vector3.one), "the rig's Group keeps scale 1 (the animation scales a child)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnOpenPalmTowardTheFaceSummonsThePanelInFrontOfIt()
        {
            var app = App.Current;
            var m = Menu;
            Vector3 head = app.Head.position;
            Vector3 wrist = head + new Vector3(0.05f, -0.17f, 0.3f);
            Vector3 toHead = (head - (wrist + new Vector3(0f, 0.07f, 0f))).normalized;
            _fake.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, Vector3.up, toHead));
            bool sawRing = false;
            float t0 = Time.realtimeSinceStartup;
            while (!m.Shown && Time.realtimeSinceStartup - t0 < 3f)
            {
                yield return null;
                sawRing |= m.View.Ring.gameObject.activeSelf;
            }
            Assert.IsTrue(m.Shown, "summoned");
            Assert.IsTrue(sawRing, "the ring showed while charging");
            Assert.IsFalse(m.View.Ring.gameObject.activeSelf, "and went away when it fired");
            Assert.That(_events.FindAll(e => e.Change == MenuChange.Open).Count, Is.EqualTo(1));
            Vector3 pos = m.Root.position;
            Assert.That(pos.y, Is.EqualTo(head.y - 0.12f).Within(1e-4f), "12 cm below the eyes");
            float d = new Vector2(pos.x - head.x, pos.z - head.z).magnitude;
            Assert.That(d, Is.InRange(0.28f, 0.56f), "within reach (menu-check summonedInFront)");
            Vector3 l = app.Get<IPianoApi>().Root.InverseTransformPoint(pos);
            Assert.IsFalse(Mathf.Abs(l.x) < 0.8f && l.y > -0.2f && l.y < 0.75f && l.z > -0.009f && l.z < 0.9f, "never inside the piano's box: at its key fronts");
            Vector3 away = new Vector3(pos.x - head.x, 0f, pos.z - head.z).normalized;
            Assert.That(Vector3.Dot(m.Root.forward, away), Is.GreaterThan(0.999f), "+z points away from the player");
            // Holding the palm does not toggle it again.
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.7f) yield return null;
            Assert.IsTrue(m.Shown, "a held palm toggles once");
            Assert.That(_events.FindAll(e => e.Change == MenuChange.Close).Count, Is.EqualTo(0));
            // Hand gone: no ring.
            _fake.SetTracked(HandSide.Right, false);
            yield return null;
            yield return null;
            Assert.IsFalse(m.View.Ring.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator AnIndexTipPokeOnARowNavigates()
        {
            var m = Menu;
            m.Toggle();
            yield return null;
            Assert.IsTrue(m.Shown);
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Home));
            RowCentre(m, 0, out float px, out float py);
            Assert.That(m.Controller.Row(0).Label, Is.EqualTo("Lessons"));
            PointAt(m, px, py, 0.03f);
            yield return null;
            yield return null;
            PointAt(m, px, py, -0.003f);
            yield return null;
            yield return null;
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Lessons), "the poke fired the Lessons row");
            Assert.That(_events.Exists(e => e.Change == MenuChange.Screen && e.Screen == MenuScreen.Lessons));
            // Out, then the back row (the whole header).
            PointAt(m, 512f, 55f, 0.03f);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;   // the 250 ms debounce of slot 0
            PointAt(m, 512f, 55f, -0.003f);
            yield return null;
            yield return null;
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Home), "back to home");
        }

        [UnityTest]
        public IEnumerator AHoveringTipLightsTheRowAndAddsOneGlowOnTheFace()
        {
            var app = App.Current;
            var m = Menu;
            m.Show();
            yield return null;
            RowCentre(m, 1, out float px, out float py);
            PointAt(m, px, py, 0.005f);
            yield return null;
            yield return null;
            Assert.That(m.Controller.HoverRow, Is.EqualTo(1));
            Assert.That(_glow.Pending, Is.EqualTo(1), "one glow from the panel");
            Vector3 want = m.Root.TransformPoint(MenuMath.CanvasPoint(px, py, 0.007f));
            Assert.That(Vector3.Distance(_glow.Spec.Position, want), Is.LessThan(2e-3f), "7 mm out of the face under the tip");
            Assert.That(Vector3.Dot(_glow.Spec.Normal, (app.Head.position - _glow.Spec.Position).normalized), Is.GreaterThan(0.9f), "the glow faces the player");
            Assert.That(_glow.Spec.Length, Is.EqualTo(0.045f));
            Assert.That(_glow.Spec.Width, Is.EqualTo(0.045f));
            Assert.That(_glow.Spec.Strength, Is.EqualTo(0.5f).Within(0.05f), "1 - z/0.01 at 5 mm");
            // The row highlight is a uniform of the face material (ARCHITECTURE.md 15.6), at rest after 120 ms.
            Material mat = m.View.Face.sharedMaterial;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.2f) yield return null;
            MenuRow row = m.Controller.Row(1);
            Assert.That(mat.GetVector("_HoverRect0"), Is.EqualTo(new Vector4(row.X0, row.Y0, row.X1, row.Y1)));
            Vector4 prm = mat.GetVector("_HoverParam0");
            Assert.That(prm.y, Is.EqualTo(1f), "fully lit after the 120 ms fade");
            Assert.That(prm.z, Is.EqualTo(1f), "a row highlight has the stroke");
            Assert.That(prm.x, Is.EqualTo(Mathf.Min(28f, (row.Y1 - row.Y0) / 2f)));
            Assert.That(mat.GetVector("_HoverParam1").y, Is.EqualTo(0f), "nothing fading out");
            PointAt(m, px, py, 0.03f);
            yield return null;
            yield return null;
            Assert.That(m.Controller.HoverRow, Is.EqualTo(-1));
            Assert.That(_glow.Pending, Is.EqualTo(0));
            Assert.That(mat.GetVector("_HoverParam0").y, Is.EqualTo(0f));
            Assert.That(mat.GetVector("_HoverRect1"), Is.EqualTo(new Vector4(row.X0, row.Y0, row.X1, row.Y1)), "the left row fades out");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.2f) yield return null;
            Assert.That(mat.GetVector("_HoverParam1").y, Is.EqualTo(0f), "and is gone after 120 ms");
        }

        [UnityTest]
        public IEnumerator HiddenOnUnmountAtOnceThenFadesOut()
        {
            var app = App.Current;
            var m = Menu;
            m.Show();
            float t0 = Time.realtimeSinceStartup;
            while (m.AnimK < 1f && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            Assert.That(m.AnimK, Is.EqualTo(1f), "opened over 150 ms");
            Assert.That(m.View.Visual.localScale.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.IsTrue(m.View.Collider.enabled);
            Assert.That(m.View.Collider.gameObject.layer, Is.EqualTo(Layers.PlaySurface), "the desktop click target is on Layers.PlaySurface");
            Assert.That(app.Rig.Desktop.dragBlockMask & (1 << m.View.Collider.gameObject.layer), Is.Not.Zero, "a press on the face never turns the desktop camera");
            app.Events.Publish(new HmdMountEvent { Mounted = false });
            Assert.IsFalse(m.Shown, "hidden at once");
            Assert.IsFalse(m.GrabZone.HasValue);
            Assert.IsFalse(m.View.Collider.enabled, "no longer clickable");
            Assert.IsTrue(m.Closing);
            Assert.IsTrue(m.View.VisualActive, "still fading");
            Assert.That(_events[_events.Count - 1].Change, Is.EqualTo(MenuChange.Close));
            t0 = Time.realtimeSinceStartup;
            while (m.View.VisualActive && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            Assert.IsFalse(m.View.VisualActive, "out of the scene after the fade");
            Assert.IsFalse(m.Closing);
        }

        [UnityTest]
        public IEnumerator TheGrabZoneFollowsThePanelAndHidingEmptiesIt()
        {
            var app = App.Current;
            var m = Menu;
            m.Show();
            yield return null;
            Bounds z = m.GrabZone.Value;
            Assert.IsTrue(z.Contains(m.Root.position));
            Assert.That(z.size.x, Is.GreaterThan(0.5f), "panel plus 12 cm each side");
            // Layout drags the rig's Group: the zone is where the panel now is (menu-check grabMoves).
            Vector3 moved = m.Root.position + new Vector3(0.2f, 0.1f, 0f);
            m.Root.position = moved;
            Assert.IsTrue(m.GrabZone.Value.Contains(moved));
            Assert.IsTrue(m.Rig.Zone.Contains(moved));
            m.Hide();
            Assert.IsFalse(m.GrabZone.HasValue, "hidden: empty zone");
            m.Show();
            Vector3 head = app.Head.position;
            Assert.That(Vector3.Distance(m.Root.position, new Vector3(head.x, head.y - 0.12f, head.z)), Is.LessThan(0.6f), "shown again in front of the player");
        }

        [UnityTest]
        public IEnumerator OpeningClearsThePanelOutOnceAndMovingItDoesNot()
        {
            var m = Menu;
            Assert.That(App.Current.Get<IPlacementService>(), Is.SameAs(_placement));
            Assert.That(_placement.Cleared, Is.Empty, "nothing while hidden");
            int opens = 0, clearedAtOpen = -1;
            App.Current.Events.Subscribe<MenuEvent>(e => { if (e.Change == MenuChange.Open) { opens++; if (clearedAtOpen < 0) clearedAtOpen = _placement.Cleared.Count; } });
            m.Show();
            Assert.That(_placement.Cleared.Count, Is.EqualTo(1), "opening from hidden clears the panel out once (placement.clearPanel on 'open')");
            Assert.That(_placement.Cleared[0], Is.SameAs(m.Rig));
            Assert.That(opens, Is.EqualTo(1));
            Assert.That(clearedAtOpen, Is.EqualTo(1), "the clear-out runs before MenuEvent{Open} reaches its listeners");
            yield return null;
            m.Show();
            Assert.That(_placement.Cleared.Count, Is.EqualTo(1), "showing a shown panel only moves it");
            Assert.That(opens, Is.EqualTo(1));
            m.Hide();
            Assert.That(_placement.Cleared.Count, Is.EqualTo(1), "hiding clears nothing");
            m.Show();
            Assert.That(_placement.Cleared.Count, Is.EqualTo(2), "every open clears out again");
            Assert.That(opens, Is.EqualTo(2));
            m.Toggle();
            m.Toggle();
            Assert.That(_placement.Cleared.Count, Is.EqualTo(3), "Toggle opens through Show");
        }

        [UnityTest]
        public IEnumerator ALessonStartShowsTheLessonScreen()
        {
            var m = Menu;
            Assert.That(App.Current.Get<ITutorial>(), Is.SameAs(_tutorial));
            m.Show();
            yield return null;
            _tutorial.EventsTotal = 15;
            _tutorial.Start("harp-arpeggio");
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Lesson), "TutorialEvent: any lesson start shows the lesson screen");
            yield return null;
            Assert.That(m.Controller.HeaderTitle, Is.EqualTo("‹  C major arpeggio"));
            Assert.That(m.Controller.HeaderNote, Is.EqualTo("Harp"));
            _tutorial.Stop();
            yield return null;
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Courses), "an idle lesson screen falls back to the courses");
            Assert.That(m.Controller.Instrument, Is.EqualTo(InstrumentKind.Harp));
        }

        [UnityTest]
        public IEnumerator ThePerFramePathAllocatesNothing()
        {
            var app = App.Current;
            var m = Menu;
            m.Show();
            yield return null;
            RowCentre(m, 2, out float px, out float py);
            PointAt(m, px, py, 0.005f);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;   // hover fade settled
            var pts = app.Input.HandPointers;
            var glow = app.HoverGlow;
            for (int i = 0; i < 5; i++) { m.Tick(pts, 0.016f); m.Hover(pts, glow); m.Tick(FramePhase.InputDone, 0.016f); m.Tick(FramePhase.EndFrame, 0.016f); }
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "calibration: the recorder sees allocations here");
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    m.Tick(pts, 0.016f);
                    m.Hover(pts, glow);
                    m.Tick(FramePhase.InputDone, 0.016f);
                    m.Tick(FramePhase.EndFrame, 0.016f);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(m.Controller.HoverRow, Is.EqualTo(2), "the measured frames really hovered");
            // Hidden, charging the summon: also allocation-free.
            m.Hide();
            Vector3 head = app.Head.position;
            Vector3 wrist = head + new Vector3(0.05f, -0.17f, 0.3f);
            _fake.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, wrist, Vector3.up, (head - wrist).normalized));
            yield return null;
            yield return null;
            Assert.That(() =>
            {
                for (int i = 0; i < 10; i++) { m.Tick(pts, 0.001f); m.Tick(FramePhase.EndFrame, 0.001f); }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
