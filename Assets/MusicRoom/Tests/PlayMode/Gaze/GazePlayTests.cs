// Look and pinch end to end (every real module but Sunny, whom GazeFakeGuide stands in for; simulated headset; manual frames
// at 72 Hz; scripted head and hands). The head looks at the open menu's row, or at an instrument on display; a short pinch
// of a hand held up and away chooses it: the row fires as a poke does (its screen opens, MenuRowEvent), the instrument comes
// to the seat (Bring, its glide). And it never takes a pinch it should not: before the dwell, with the target outside the
// view's centre, while the hand plays or is on an instrument (Move instruments' own pinch on a body keeps working: Sunny's
// event), poking the menu, moving instruments, Sunny teaching a pinch, a long or dragging pinch, looking away before letting
// go, the setting off (the Sunny page's row, persisted). Sunny's tip the first time something lights. Allocation-free.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Gaze;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace MusicRoom.Tests.PlayMode.Gaze
{
    /// <summary>Sunny's stand-in: records the tips asked, and can be "teaching a pinch".</summary>
    public sealed class GazeFakeGuide : AppModule, IGuide
    {
        public override int Order => 50;
        public readonly List<string> Asked = new List<string>();
        public readonly HashSet<string> Seen = new HashSet<string>();
        public bool Teaching, Busy;
        public override void Init(App app) => app.Register<IGuide>(this);
        public bool Present => false;
        public bool Done => true;
        public void Start() { }
        public void Dismiss() { }
        public void Toggle() { }
        public float Tip(string id)
        {
            Asked.Add(id);
            if (Busy || Seen.Contains(id)) return 0f;
            Seen.Add(id);
            return 2.5f;
        }
        public bool TipSeen(string id) => Seen.Contains(id);
        public bool TeachingPinch => Teaching;
    }

    /// <summary>The harness: boot, scripted head and hands, the menu, an instrument on display.</summary>
    public abstract class GazeTestBase
    {
        protected const float Dt = 1f / 72f;
        protected TestBoot Boot;
        protected App App;
        protected LayoutModule Layout;
        protected MenuModule MenuM;
        protected GazeModule Gaze;
        protected GazeFakeGuide Guide;
        protected Quaternion? Looking;
        protected readonly List<MenuRowEvent> _rows = new List<MenuRowEvent>();
        protected readonly List<InstrumentBodyPinchEvent> _bodyPinches = new List<InstrumentBodyPinchEvent>();
        System.Action<MenuRowEvent> _onRow;
        System.Action<InstrumentBodyPinchEvent> _onBody;

        protected IEnumerator BootApp(bool headless = true, System.Action<MemorySettingsStore> settings = null)
        {
            Boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true);
            Boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            settings?.Invoke(Boot.Settings);
            Guide = new GazeFakeGuide();
            Boot.Options.ModuleFilter = t => t != typeof(MusicRoom.Guide.GuideModule);
            Boot.Options.ExtraModules.Add(Guide);
            Boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, Away(HandSide.Right)));
            Boot.Hands.SetPose(HandSide.Left, OpenAt(HandSide.Left, Away(HandSide.Left)));
            PointUp(HandSide.Right);
            PointUp(HandSide.Left);
            MenuBootGuard.Begin();
            yield return Boot.Start();
            MenuBootGuard.End();
            App = Boot.App;
            Layout = App.Get<ILayoutService>() as LayoutModule;
            MenuM = App.Get<IMenuPanel>() as MenuModule;
            Gaze = App.Get<ILookAndPinch>() as GazeModule;
            Assert.That(Layout, Is.Not.Null);
            Assert.That(MenuM, Is.Not.Null, "the real menu");
            Assert.That(Gaze, Is.Not.Null, "the Gaze module registered ILookAndPinch");
            float t0 = Time.realtimeSinceStartup;
            while (App.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(3);
            yield return null;   // the Interaction SDK components start (Unity's Start)
            yield return null;
            Step(3);
            Assert.IsTrue(Layout.FirstArrangeDone);
            Assert.IsTrue(App.IsPresenting);
            _rows.Clear();
            _bodyPinches.Clear();
            _onRow = e => _rows.Add(e);
            _onBody = e => _bodyPinches.Add(e);
            App.Events.Subscribe(_onRow);
            App.Events.Subscribe(_onBody);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (App != null) { App.Events.Unsubscribe(_onRow); App.Events.Unsubscribe(_onBody); }
            if (Boot != null) yield return Boot.Stop();
            Boot = null;
            App = null;
            Looking = null;
        }

        // ---------------------------------------------------------------- helpers

        protected void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                if (Looking.HasValue) App.Head.rotation = Looking.Value;
                Boot.Step(Dt);
            }
        }

        protected void StepSeconds(float s) => Step(Mathf.CeilToInt(s / Dt));

        /// <summary>A hand held up and back beside the head: outside every zone, not playing, nothing in reach.</summary>
        protected static Vector3 Away(HandSide side) => new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f);

        protected static HandPose PinchAt(HandSide side, Vector3 p, float gap = 0f) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(p).WithPinch(gap);

        protected static HandPose OpenAt(HandSide side, Vector3 p) => PinchAt(side, p, 0.06f);

        /// <summary>The hand's system pointer points at the ceiling (so the ray summon never takes the pinch).</summary>
        protected void PointUp(HandSide side) => Boot.Hands.SetPointer(side, new Pose(Away(side), Quaternion.LookRotation(Vector3.up, Vector3.forward)));

        protected void Look(Vector3 at) => Looking = Quaternion.LookRotation(at - App.Head.position, Vector3.up);

        protected void LookOffBy(Vector3 at, float deg) => Looking = Quaternion.AngleAxis(deg, Vector3.up) * Quaternion.LookRotation(at - App.Head.position, Vector3.up);

        /// <summary>A short still pinch (frames held) at a point, then open.</summary>
        protected void Tap(HandSide side, Vector3 at, int frames = 10, Vector3 drift = default)
        {
            for (int i = 0; i < frames; i++)
            {
                Boot.Hands.SetPose(side, PinchAt(side, at + drift * (i / (float)Mathf.Max(1, frames - 1))));
                Step();
            }
            Boot.Hands.SetPose(side, OpenAt(side, at + drift));
            Step(2);
        }

        protected Vector3 RowCentre(string rowId)
        {
            Assert.IsTrue(App.Get<IMenuGuideHooks>().TryGetRowRect(rowId, out Vector3 c, out _, out _), rowId + " is shown");
            return c;
        }

        protected int RowIndex(RowKind kind, MenuScreen? to = null)
        {
            MenuController c = MenuM.Controller;
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == kind && (!to.HasValue || c.Row(i).To == to.Value)) return i;
            return -1;
        }

        /// <summary>A point just outside one of the rig's body meshes (no play surface there), as MoveDragTests finds it.</summary>
        protected bool BodySpot(Rig rig, out Vector3 spot)
        {
            Vector3 head = App.Head.position;
            for (int i = 0; i < rig.Handles.Count; i++)
            {
                Renderer r = rig.Handles[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                Vector3 c = r.bounds.ClosestPoint(head);
                Vector3 p = c + (head - c).normalized * 0.01f;
                if (Layout.PlaySurfaceAt(p) != PlaySurface.None || Layout.GrabTarget(p) != rig) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        protected IEnumerator OpenMenu()
        {
            Looking = Quaternion.Euler(10f, 0f, 0f);
            Step();
            MenuM.Show();
            StepSeconds(0.3f);
            Assert.IsTrue(MenuM.Shown);
            Assert.That(MenuM.Controller.Screen, Is.EqualTo(MenuScreen.Home));
            yield return null;
        }

        /// <summary>An instrument on display that the head can see (not hidden behind an instrument at the seat).</summary>
        protected InstrumentKind? Displayed(out Vector3 centre)
        {
            centre = default;
            Step();
            foreach (InstrumentDescriptor d in App.Registry.All)
            {
                InstrumentGazeSurface s = Gaze.InstrumentSurface(d.Kind);
                if (s == null || !s.Live) continue;
                Look(s.Box.center);
                StepSeconds(0.6f);
                if (Gaze.Target == GazeTargetKind.Instrument && Gaze.TargetInstrument == d.Kind) { centre = s.Box.center; return d.Kind; }
            }
            return null;
        }

    }

    public class GazePlayTests : GazeTestBase
    {
        // ---------------------------------------------------------------- the menu

        [UnityTest]
        public IEnumerator LookingAtAMenuRowLightsItAndAPinchFiresItLikeAPoke()
        {
            yield return BootApp();
            yield return OpenMenu();
            int row = RowIndex(RowKind.Menu, MenuScreen.Instruments);
            Assert.That(row, Is.GreaterThanOrEqualTo(0));
            Look(RowCentre("instruments"));
            StepSeconds(0.15f);
            Assert.That(MenuM.Controller.HoverRow, Is.EqualTo(-1), "not before the dwell");
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.None));
            StepSeconds(0.4f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.MenuRow), "after the dwell it counts");
            Assert.That(MenuM.Controller.HoverRow, Is.EqualTo(row), "with the poke's own hover look");
            Assert.That(Gaze.HandFree(1), Is.True, "the hand up and away is free");
            Tap(HandSide.Right, Away(HandSide.Right));
            Assert.That(MenuM.Controller.Screen, Is.EqualTo(MenuScreen.Instruments), "the row fired");
            Assert.That(_rows.Count, Is.EqualTo(1), "one MenuRowEvent, as a poke publishes");
            Assert.That(_rows[0].RowId, Is.EqualTo(MenuRowIds.Instruments));
            Assert.That(Gaze.Chosen, Is.EqualTo(1));
            Assert.That(Layout.SnapTurns, Is.Zero, "never a seat turn");
        }

        [UnityTest]
        public IEnumerator EitherHandChoosesAndAHighlightNeverPopsInThePeriphery()
        {
            yield return BootApp();
            yield return OpenMenu();
            Vector3 c = RowCentre("instruments");
            LookOffBy(c, 30f);
            StepSeconds(0.8f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.None), "a row 30 degrees off the view's centre never lights");
            Look(c);
            StepSeconds(0.6f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.MenuRow));
            Tap(HandSide.Left, Away(HandSide.Left));
            Assert.That(MenuM.Controller.Screen, Is.EqualTo(MenuScreen.Instruments), "the left hand chooses too");
        }

        [UnityTest]
        public IEnumerator APinchThatIsNotATapChoosesNothing()
        {
            yield return BootApp();
            yield return OpenMenu();
            Look(RowCentre("instruments"));
            StepSeconds(0.6f);
            Tap(HandSide.Right, Away(HandSide.Right), frames: Mathf.CeilToInt(0.8f / Dt));
            Assert.That(MenuM.Controller.Screen, Is.EqualTo(MenuScreen.Home), "a held pinch (0.8 s) is someone else's gesture");
            Tap(HandSide.Right, Away(HandSide.Right), frames: 14, drift: new Vector3(0.1f, 0f, 0f));
            Assert.That(MenuM.Controller.Screen, Is.EqualTo(MenuScreen.Home), "a dragging pinch (the seat swivel's pull) is not a choice");
            // looking away before letting go
            Vector3 p = Away(HandSide.Right);
            for (int i = 0; i < 6; i++) { Boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, p)); Step(); }
            LookOffBy(RowCentre("instruments"), 35f);
            Boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, p));
            Step(2);
            Assert.That(MenuM.Controller.Screen, Is.EqualTo(MenuScreen.Home), "looked away before the release");
            Assert.That(_rows.Count, Is.Zero);
            Assert.That(Gaze.Chosen, Is.Zero);
            Assert.That(Gaze.Refused, Is.GreaterThanOrEqualTo(3));
        }

        [UnityTest]
        public IEnumerator PokingTheMenuIsNeverALookAndPinch()
        {
            yield return BootApp();
            yield return OpenMenu();
            Vector3 c = RowCentre("recordings");
            Look(RowCentre("instruments"));
            StepSeconds(0.6f);
            // the right index pinches right in front of the face (the menu's grab zone): the menu's, not the gaze's
            Vector3 near = c + (App.Head.position - c).normalized * 0.05f;
            Assert.IsTrue(MenuM.GrabZone.HasValue && MenuM.GrabZone.Value.Contains(near), "inside the menu's zone");
            Boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, near));
            Step(3);
            Assert.That(Gaze.HandFree(1), Is.False, "a hand on the menu is not free");
            Tap(HandSide.Right, near, frames: 6);
            Assert.That(Gaze.Chosen, Is.Zero, "no look and pinch from the menu's zone");
            Assert.That(MenuM.Controller.Screen, Is.Not.EqualTo(MenuScreen.Instruments), "the gazed row did not fire");
        }

        // ---------------------------------------------------------------- instruments

        [UnityTest]
        public IEnumerator LookingAtAnInstrumentOnDisplayLightsItAndAPinchBringsIt()
        {
            yield return BootApp();
            InstrumentKind? k = Displayed(out Vector3 c);
            Assert.That(k.HasValue, Is.True, "some instrument on display can be looked at");
            Assert.That(Layout.PlaceOf(k.Value), Is.EqualTo(InstrumentPlace.Display));
            Assert.That(Gaze.GlowLevel, Is.GreaterThan(0.9f), "its soft glow is on");
            Assert.That(Guide.Asked, Is.Empty, "not at the first glimpse");
            StepSeconds(GazeRules.TipAfter);
            Assert.That(Guide.Asked, Is.EqualTo(new[] { GazeRules.TipId }), "Sunny's tip once it has shown a moment");
            StepSeconds(GazeRules.TipEvery + 0.5f);
            Assert.That(Guide.Asked.Count, Is.EqualTo(1), "said once: never asked again");
            Tap(HandSide.Right, Away(HandSide.Right));
            Assert.That(Gaze.Chosen, Is.EqualTo(1));
            for (int i = 0; i < 300 && Layout.AnyMoving; i++) Step();
            Step(2);
            Assert.That(Layout.PlaceOf(k.Value), Is.EqualTo(InstrumentPlace.Seat), "it came to the seat, as the menu's tile brings it");
            Assert.IsTrue(Layout.IsInScene(k.Value));
            Assert.That(Gaze.Target, Is.Not.EqualTo(GazeTargetKind.Instrument), "nothing at the seat lights");
            StepSeconds(0.3f);
            Assert.That(Gaze.GlowLevel, Is.Zero, "the glow went out");
        }

        [UnityTest]
        public IEnumerator NoLookAndPinchWhilePlayingOrOnAnInstrumentAndMoveKeepsItsPinch()
        {
            yield return BootApp();
            InstrumentKind? k = Displayed(out _);
            Assert.That(k.HasValue, Is.True);
            // the hand just played
            HandState right = App.Input.Hand(Handedness.Right);
            right.LastPlayMs = App.Clock.NowMs;
            Step();
            Assert.That(Gaze.HandFree(1), Is.False, "a hand that just played is not free");
            Tap(HandSide.Right, Away(HandSide.Right), frames: 4);
            right.LastPlayMs = double.NegativeInfinity;
            Assert.That(Gaze.Chosen, Is.Zero, "while playing");
            // a still pinch on the piano's body (Move instruments off): Layout's, which tells Sunny; never the gaze's
            Rig piano = App.Get<IRigRegistry>().Get(RigId.Piano);
            Assert.IsTrue(BodySpot(piano, out Vector3 body), "a spot on the piano's body");
            Boot.Hands.SetPointer(HandSide.Right, null);
            Boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, body + Vector3.up * 0.04f));
            Step(2);
            Assert.That(Gaze.HandFree(1), Is.False, "a hand at an instrument is not free");
            for (int i = 0; i < 40; i++) { Boot.Hands.SetPose(HandSide.Right, PinchAt(HandSide.Right, body)); Step(); }
            Boot.Hands.SetPose(HandSide.Right, OpenAt(HandSide.Right, Away(HandSide.Right)));
            PointUp(HandSide.Right);
            Step(3);
            Assert.That(Gaze.Chosen, Is.Zero, "a pinch on an instrument's body");
            Assert.That(_bodyPinches.Count, Is.GreaterThanOrEqualTo(1), "Move instruments' own body pinch still reaches Sunny");
            Assert.That(Layout.PlaceOf(k.Value), Is.EqualTo(InstrumentPlace.Display), "nothing came");
        }

        [UnityTest]
        public IEnumerator MovingInstrumentsTheTourAndTheSettingStandItDown()
        {
            yield return BootApp();
            InstrumentKind? k = Displayed(out Vector3 c);
            Assert.That(k.HasValue, Is.True);
            // Move instruments on: off entirely (a pinch there picks instruments up)
            IArrangeMode arrange = App.Get<IArrangeMode>();
            arrange.BeginArrange();
            StepSeconds(0.6f);
            Assert.IsTrue(arrange.Arranging);
            Assert.IsFalse(Gaze.Active, "never while moving instruments");
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.None));
            Tap(HandSide.Right, Away(HandSide.Right));
            arrange.EndArrange();
            StepSeconds(0.6f);
            Assert.That(Gaze.Chosen, Is.Zero);
            // Sunny teaching a pinch
            Guide.Teaching = true;
            StepSeconds(0.6f);
            Assert.IsFalse(Gaze.Active, "never during Sunny's pinch steps");
            Tap(HandSide.Right, Away(HandSide.Right));
            Assert.That(Gaze.Chosen, Is.Zero);
            Guide.Teaching = false;
            // the setting: the Sunny page's row turns it off, persisted
            Look(c);
            StepSeconds(0.6f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.Instrument), "back on");
            yield return OpenMenu();
            MenuM.Controller.Go(MenuScreen.Guide);
            Step(2);
            int row = RowIndex(RowKind.LookPinch);
            Assert.That(row, Is.GreaterThanOrEqualTo(0), "the Sunny page has Look and pinch");
            Assert.That(MenuM.Controller.Row(row).Label, Is.EqualTo(MenuStyle.LookPinchLabel));
            Assert.That(MenuM.Controller.Row(row).On, Is.True, "on by default");
            MenuM.Controller.Fire(row);
            Step(2);
            Assert.IsFalse(Gaze.On);
            Assert.That(Boot.Settings.GetInt(AppSettings.KeyLookPinch, 1), Is.EqualTo(0), "persisted");
            MenuM.Hide();
            Look(c);
            StepSeconds(0.8f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.None), "off: nothing lights");
            Tap(HandSide.Right, Away(HandSide.Right));
            Assert.That(Gaze.Chosen, Is.Zero, "off: nothing is chosen");
            Assert.That(Layout.PlaceOf(k.Value), Is.EqualTo(InstrumentPlace.Display));
        }

        [UnityTest]
        public IEnumerator ASavedOffComesBackOff()
        {
            yield return BootApp(settings: s => s.SetInt(AppSettings.KeyLookPinch, 0));
            Assert.IsFalse(Gaze.On);
            Assert.IsFalse(Gaze.Active);
        }

        // ---------------------------------------------------------------- cost

        [UnityTest]
        public IEnumerator EveryFrameIsAllocationFree()
        {
            yield return BootApp();
            InstrumentKind? k = Displayed(out Vector3 c);
            Assert.That(k.HasValue, Is.True);
            // warm both targets and a tap that is refused (the pinch paths)
            Tap(HandSide.Right, Away(HandSide.Right), frames: Mathf.CeilToInt(0.8f / Dt));
            Quaternion at = Quaternion.LookRotation(c - App.Head.position);
            int f = 0;
            TestDelegate jitter = () =>
            {
                for (int i = 0; i < 120; i++)
                {
                    App.Head.rotation = Quaternion.AngleAxis(Mathf.Sin(f++ * 0.05f) * 2f, Vector3.up) * at;
                    Gaze.Tick(FramePhase.InputDone, Dt);
                }
            };
            // steady state: the first run of this delegate is not a frame of the app, and Sunny's once-ever tip_look_pinch
            // (asked again every GazeRules.TipEvery while Sunny is busy, e.g. with the first-run tour) is said before measuring
            for (int w = 0; w < 6 && !Gaze.TipAsked; w++) jitter();
            jitter();
            Assert.That(jitter, Is.Not.AllocatingGCMemory(), "gazing at an instrument");
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.Instrument));
            yield return OpenMenu();
            Look(RowCentre("instruments"));
            StepSeconds(0.6f);
            Assert.That(Gaze.Target, Is.EqualTo(GazeTargetKind.MenuRow));
            TestDelegate ticks = () => { for (int i = 0; i < 120; i++) Gaze.Tick(FramePhase.InputDone, Dt); };
            ticks();
            Assert.That(ticks, Is.Not.AllocatingGCMemory(), "gazing at a menu row");
            Looking = Quaternion.Euler(80f, 0f, 0f);   // at the floor: nothing
            StepSeconds(0.5f);
            ticks();
            Assert.That(ticks, Is.Not.AllocatingGCMemory(), "nothing looked at");
        }
    }
}
