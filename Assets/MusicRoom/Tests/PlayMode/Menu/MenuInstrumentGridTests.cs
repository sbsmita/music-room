// The Instruments grid in the running app (12-new-instruments/platform.md 5.1): every instrument loaded, the menu
// module with its real controller and events, and GridLayoutProbe standing in for Layout's platform half (Order 5, so
// App.Get<ILayoutService> returns it; the real Layout still arranges the room). Fingertip pokes (FakeHandSource) and
// direct fires check: tile pokes in both modes, the "No room" path with its knock on the Ui source, the replace note,
// PutAway with the last member staying, the mode switch keeping Facing, MenuRowEvent ids on the bus, Sunny's two
// signals, a redraw on InstrumentMoveEvent, the menu's IGuideAnchors, the options page's InstrumentOptionEvent and
// setting, and the panel home (SeatSpec.PanelHome) the panel opens at.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
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
    public class MenuInstrumentGridTests
    {
        TestBoot _boot;
        FakeHandSource _fake;
        GridLayoutProbe _layout;
        readonly List<MenuRowEvent> _rows = new List<MenuRowEvent>();
        readonly List<InstrumentSignalEvent> _signals = new List<InstrumentSignalEvent>();
        readonly List<InstrumentOptionEvent> _options = new List<InstrumentOptionEvent>();
        double _fireAt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot();
            _fake = _boot.Hands;
            _layout = new GridLayoutProbe();
            _boot.Options.ExtraModules.Add(_layout);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            var app = App.Current;
            _rows.Clear(); _signals.Clear(); _options.Clear();
            app.Events.Subscribe<MenuRowEvent>(e => _rows.Add(e));
            app.Events.Subscribe<InstrumentSignalEvent>(e => _signals.Add(e));
            app.Events.Subscribe<InstrumentOptionEvent>(e => _options.Add(e));
            float t0 = Time.realtimeSinceStartup;
            while ((app.Rig.State == XrState.Starting || !_layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Assert.That(app.Get<ILayoutService>(), Is.SameAs(_layout), "the probe is the menu's layout");
            _fireAt = app.Clock.NowMs;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            yield return _boot.Stop();
        }

        static MenuModule Menu => (MenuModule)App.Current.Get<IMenuPanel>();

        IEnumerator OpenGrid()
        {
            var m = Menu;
            m.Show();
            m.Go(MenuScreen.Instruments);
            yield return null;
            yield return null;
            Assert.That(m.Screen, Is.EqualTo(MenuScreen.Instruments));
        }

        static int Tile(MenuController c, InstrumentKind k)
        {
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == RowKind.Tile && c.Row(i).Inst == k) return i;
            return -1;
        }

        static int RowOf(MenuController c, RowKind id, LayoutMode mode = LayoutMode.All)
        {
            for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == id && (id != RowKind.Layout || c.Row(i).Mode == mode)) return i;
            return -1;
        }

        /// <summary>A press as the poke code makes it (past the debounce), then a frame for the relayout.</summary>
        IEnumerator Fire(int row)
        {
            var c = Menu.Controller;
            Assert.That(row, Is.GreaterThanOrEqualTo(0));
            // Past the row's 250 ms debounce, as close to the app clock as that allows (a note's 2 s count from here).
            _fireAt = System.Math.Max(_fireAt + 260.0, App.Current.Clock.NowMs);
            c.BeginPokes(_fireAt);
            c.Fire(row);
            c.EndPokes();
            yield return null;
            yield return null;
        }

        /// <summary>A real fingertip poke on a row: in front (armed), then 3 mm behind the face.</summary>
        IEnumerator Poke(int row)
        {
            var m = Menu;
            var r = m.Controller.Row(row);
            float px = (r.X0 + r.X1) / 2f, py = (r.Y0 + r.Y1) / 2f;
            Point(m, px, py, 0.03f);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;
            Point(m, px, py, -0.003f);
            yield return null;
            yield return null;
            Point(m, px, py, 0.03f);
            yield return null;
        }

        void Point(MenuModule m, float px, float py, float z)
        {
            Vector3 world = m.Root.TransformPoint(MenuMath.CanvasPoint(px, py, z));
            var rot = Quaternion.LookRotation(m.Root.forward, Vector3.up);
            _fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, world - m.Root.forward * 0.18f, rot).WithPointing().WithIndexTipAt(world));
        }

        [UnityTest]
        public IEnumerator FingertipPokesBringJoinAndPutAwayInMySet()
        {
            yield return OpenGrid();
            var c = Menu.Controller;
            Assert.That(Tile(c, InstrumentKind.Bowls), Is.GreaterThan(0), "every instrument is loaded in this boot");
            yield return Poke(Tile(c, InstrumentKind.Drums));
            Assert.That(_layout.PutAways, Is.EqualTo(new[] { InstrumentKind.Drums }), "a member goes back to its place");
            Assert.That(_rows.Exists(e => e.RowId == "inst.drums" && e.Screen == MenuScreen.Instruments), "MenuRowEvent inst.drums on the bus");
            yield return Poke(Tile(c, InstrumentKind.Bowls));
            Assert.That(_layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Bowls, BringReason.Menu) }), "a displayed tile with room joins");
            Assert.IsTrue(c.Row(Tile(c, InstrumentKind.Bowls)).On, "redrawn from the layout's change: framed");
            Assert.That(c.Row(Tile(c, InstrumentKind.Bowls)).Note, Is.Null.Or.Empty, "no status line under the name");
            Assert.That(c.Row(Tile(c, InstrumentKind.Bowls)).Ticked);
            Assert.That(c.HeaderNote, Is.EqualTo("Piano, Harp, Singing bowls"));
            Assert.That(_rows.Exists(e => e.RowId == "inst.bowls"));
        }

        [UnityTest]
        public IEnumerator OneAloneATileComesAloneAndTheSeatedOneStays()
        {
            yield return OpenGrid();
            var c = Menu.Controller;
            yield return Fire(RowOf(c, RowKind.Layout, LayoutMode.Single));
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(_rows[_rows.Count - 1].RowId, Is.EqualTo("layout.single"));
            yield return Fire(Tile(c, InstrumentKind.Piano));
            Assert.That(_layout.Brings, Is.Empty, "already here");
            yield return Fire(Tile(c, InstrumentKind.Marimba));
            Assert.That(_layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Marimba, BringReason.Menu) }));
            Assert.That(c.HeaderNote, Is.EqualTo("Marimba alone"));
        }

        [UnityTest]
        public IEnumerator NoRoomKnocksSoftlyAndAReplacementIsNamed()
        {
            yield return OpenGrid();
            var c = Menu.Controller;
            var engine = App.Current.Get<IAudioEngine>();
            Assert.That(engine, Is.Not.Null);
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            _layout.Join = new JoinPreview(JoinAction.Alone);
            yield return Fire(Tile(c, InstrumentKind.Marimba));
            Assert.That(_layout.Brings, Is.Empty, "no Bring when nothing fits");
            Assert.That(c.HeaderNote, Is.EqualTo("No room: take one out first"));
            Assert.That(tap.Count(AudioCall.Click), Is.EqualTo(1), "one soft knock (the Ui click), never an error buzz");
            Assert.IsFalse(tap.Calls.Find(x => x.Method == AudioCall.Click).Flag, "the unaccented click");
            _layout.Join = new JoinPreview(JoinAction.Replace, InstrumentKind.Drums);
            yield return Fire(Tile(c, InstrumentKind.Handpan));
            Assert.That(_layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Handpan, BringReason.Menu) }));
            Assert.That(c.HeaderNote, Is.EqualTo("Drums went back to the left wall"));
            Assert.That(tap.Count(AudioCall.Click), Is.EqualTo(1), "no knock for a replacement");
            float t0 = Time.realtimeSinceStartup;
            while (c.TransientNote != null && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            yield return null;
            Assert.That(c.HeaderNote, Is.EqualTo("Piano, Handpan, Harp"), "after 2 s the set again");
            engine.TestTap = null;
        }

        [UnityTest]
        public IEnumerator TheLastMemberStaysAndTheModeSwitchNeverFollowsTheHead()
        {
            yield return OpenGrid();
            var c = Menu.Controller;
            yield return Fire(Tile(c, InstrumentKind.Piano));
            yield return Fire(Tile(c, InstrumentKind.Drums));
            Assert.That(_layout.Members, Is.EqualTo(new[] { InstrumentKind.Harp }));
            yield return Fire(Tile(c, InstrumentKind.Harp));
            Assert.That(_layout.Members, Is.EqualTo(new[] { InstrumentKind.Harp }), "the last member stays");
            Assert.That(c.HeaderNote, Is.EqualTo("One stays with you"));
            // One alone: the last one alone if it is in My set, else My set's lead (here its only member), never the
            // instrument the head faces (2026-11-10).
            _layout.FacingKind = InstrumentKind.Drums;
            _layout.Current = InstrumentKind.Piano;
            yield return Fire(RowOf(c, RowKind.Layout, LayoutMode.Single));
            Assert.That(_layout.Sets[_layout.Sets.Count - 1], Is.EqualTo((LayoutMode.Single, InstrumentKind.Harp)), "My set's member, not the faced drums");
        }

        [UnityTest]
        public IEnumerator OpeningTheGridSignalsSunnyAndAMoveRedraws()
        {
            var m = Menu;
            m.Show();
            yield return null;
            Assert.That(_signals, Is.Empty);
            m.Go(MenuScreen.Instruments);
            Assert.That(_signals.ConvertAll(s => s.Signal), Is.EqualTo(new[] { "menu.instruments", "menu.instruments.set" }));
            Assert.That(_signals.TrueForAll(s => s.Kind == InstrumentKind.Table));
            yield return null;
            yield return null;
            int n = m.Controller.Relayouts;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(m.Controller.Relayouts, Is.EqualTo(n), "idle");
            App.Current.Events.Publish(new InstrumentMoveEvent { Kind = InstrumentKind.Marimba, Phase = MovePhase.Leaving, Reason = BringReason.Ray });
            yield return null;
            Assert.That(m.Controller.Relayouts, Is.EqualTo(n + 1), "InstrumentMoveEvent redraws the grid");
            // Closed and opened on the grid: the screen opens again.
            m.Hide();
            yield return null;
            m.Show();
            Assert.That(_signals.Count, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator SunnysMenuAnchorsResolveOnTheFace()
        {
            var app = App.Current;
            var m = Menu;
            IGuideAnchors anchors = app.GetAll<IGuideAnchors>().Find(a => a.GetType().DeclaringType == typeof(MenuModule));
            Assert.That(anchors, Is.Not.Null, "the menu registers an IGuideAnchors");
            Assert.That(anchors.Kind, Is.EqualTo(InstrumentKind.Table), "of Kind Table");
            Assert.IsFalse(anchors.TryGetAnchor("menu.tile.piano", out _, out _), "hidden: no anchors");
            yield return OpenGrid();
            Assert.IsTrue(anchors.TryGetAnchor("menu.tile.handpan", out Vector3 at, out Vector3 up));
            var row = m.Controller.Row(Tile(m.Controller, InstrumentKind.Handpan));
            Vector3 want = m.Root.TransformPoint(MenuMath.CanvasPoint((row.X0 + row.X1) / 2f, row.Y0 + MenuStyle.PortraitH / 2f, 0f));
            Assert.That(Vector3.Distance(at, want), Is.LessThan(1e-3f), "the tile's portrait centre on the face");
            Assert.That(Vector3.Dot(up, (app.Head.position - at).normalized), Is.GreaterThan(0.8f), "up: out of the face, toward the player");
            Assert.IsFalse(anchors.TryGetAnchor("menu.tile.nothing", out _, out _));
            m.Go(MenuScreen.Home);
            yield return null;
            yield return null;
            Assert.IsFalse(anchors.TryGetAnchor("menu.tile.handpan", out _, out _), "only while the grid is shown");
        }

        [UnityTest]
        public IEnumerator TheOptionsPageStoresAndPublishesTheChoice()
        {
            var app = App.Current;
            InstrumentDescriptor th = app.Registry.Get(InstrumentKind.Marimba);
            InstrumentOption[] keep = th.Options;
            th.Options = new[] { new InstrumentOption { Key = "tone", Label = "Marimba tone", Choices = new[] { "Warm", "Classic", "Bright" }, Default = 1 } };
            try
            {
                _layout.Mode = LayoutMode.Single;
                _layout.Current = InstrumentKind.Marimba;
                yield return OpenGrid();
                var m = Menu;
                var c = m.Controller;
                yield return Fire(Tile(c, InstrumentKind.Marimba));
                Assert.IsTrue(c.OnOptions, "the tile of the instrument played alone opens its options");
                IGuideAnchors anchors = app.GetAll<IGuideAnchors>().Find(a => a.GetType().DeclaringType == typeof(MenuModule));
                Assert.IsTrue(anchors.TryGetAnchor("menu.option.marimba.tone", out _, out _), "the option row's anchor");
                yield return Fire(1);
                Assert.That(app.Settings.GetInt("musicroom.marimba.tone", -1), Is.EqualTo(2));
                Assert.That(_options.Count, Is.EqualTo(1));
                Assert.That(_options[0].Kind, Is.EqualTo(InstrumentKind.Marimba));
                Assert.That(_options[0].Key, Is.EqualTo("tone"));
                Assert.That(_options[0].Value, Is.EqualTo(2));
                Assert.That(c.Row(1).Note, Is.EqualTo("Bright"));
                Assert.That(_rows[_rows.Count - 1].RowId, Is.EqualTo("option.marimba.tone"));
            }
            finally { th.Options = keep; }
        }

        [UnityTest]
        public IEnumerator ThePanelOpensAtThePanelHomeOfTheFacedInstrument()
        {
            var app = App.Current;
            var m = Menu;
            // No instrument has a panel home of its own now (2026-11-10): one is given to the marimba for the test.
            InstrumentDescriptor th = app.Registry.Get(InstrumentKind.Marimba);
            Vector3? keepHome = th.Seat.PanelHome;
            th.Seat.PanelHome = new Vector3(-0.34f, 1.42f, 0.50f);
            Vector3 home = th.Seat.PanelHome.Value;
            try
            {
                Assert.That(m.Rig.HomeAll, Is.EqualTo(MenuConstants.HomeSeat), "the piano faced: today's home");
                m.Show();
                yield return null;
                Vector3 pianoOpen = m.Root.position;
                m.Hide();
                yield return null;
                _layout.Mode = LayoutMode.Single;
                _layout.Current = InstrumentKind.Marimba;
                yield return null;
                yield return null;
                Assert.That(m.PanelHome(), Is.EqualTo(home));
                Assert.That(m.Rig.HomeAll, Is.EqualTo(home), "the menu rig's home for Layout follows");
                Assert.That(m.Rig.HomeSingle, Is.EqualTo(home));
                m.Show();
                yield return null;
                SeatFrame seat = _layout.Seat;
                if (seat.Head == Vector3.zero) seat = SeatFrame.From(app.Head);
                Vector3 want = seat.Place(home);
                Assert.That(Vector3.Distance(m.Root.position, want), Is.LessThan(1e-3f), "at the seat-frame panel home");
                Assert.That(Vector3.Distance(m.Root.position, pianoOpen), Is.GreaterThan(0.1f));
                Vector3 away = new Vector3(m.Root.position.x - app.Head.position.x, 0f, m.Root.position.z - app.Head.position.z).normalized;
                Assert.That(Vector3.Dot(m.Root.forward, away), Is.GreaterThan(0.999f), "upright, facing the head");
                Assert.That(Mathf.Abs(m.Root.up.y - 1f), Is.LessThan(1e-4f));
                // My set: only when the faced instrument has one.
                m.Hide();
                _layout.Mode = LayoutMode.All;
                _layout.FacingKind = InstrumentKind.Piano;
                yield return null;
                Assert.That(m.PanelHome().HasValue, Is.False);
                Assert.That(m.Rig.HomeAll, Is.EqualTo(MenuConstants.HomeSeat));
                _layout.FacingKind = InstrumentKind.Marimba;
                yield return null;
                Assert.That(m.PanelHome(), Is.EqualTo(home));
            }
            finally { th.Seat.PanelHome = keepHome; }
        }

        static float[] s_keep;

        [UnityTest]
        public IEnumerator TheGridsFramesAllocateNothing()
        {
            // The grid shown, a fingertip hovering a tile, the place poll and the panel home running every frame: 0 B.
            var app = App.Current;
            var m = Menu;
            _layout.FacingKind = InstrumentKind.Harp;
            yield return OpenGrid();
            var r = m.Controller.Row(Tile(m.Controller, InstrumentKind.Marimba));
            Point(m, (r.X0 + r.X1) / 2f, (r.Y0 + r.Y1) / 2f, 0.005f);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;
            Assert.That(m.Controller.HoverRow, Is.EqualTo(Tile(m.Controller, InstrumentKind.Marimba)), "the frames really hover a tile");
            var pts = app.Input.HandPointers;
            var glow = app.HoverGlow;
            void Frames(int n)
            {
                for (int i = 0; i < n; i++) { m.Tick(pts, 0.016f); m.Hover(pts, glow); m.Tick(FramePhase.InputDone, 0.016f); m.Tick(FramePhase.EndFrame, 0.016f); }
            }
            Frames(5);
            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "calibration: the recorder sees allocations here");
            Assert.That(() => Frames(30), Is.Not.AllocatingGCMemory());
            // The options page too.
            InstrumentDescriptor th = app.Registry.Get(InstrumentKind.Marimba);
            InstrumentOption[] keep = th.Options;
            th.Options = new[] { new InstrumentOption { Key = "tone", Label = "Marimba tone", Choices = new[] { "Warm", "Classic", "Bright" }, Default = 1 } };
            try
            {
                m.Controller.OpenOptions(th);
                yield return null;
                yield return null;
                Frames(5);
                Assert.That(() => Frames(30), Is.Not.AllocatingGCMemory());
            }
            finally { th.Options = keep; }
        }

        [UnityTest]
        public IEnumerator HeadlessThePortraitsFallBackToTheVectorIcons()
        {
            var m = Menu;
            yield return OpenGrid();
            for (int i = 0; i < 3; i++) yield return null;
            PortraitStudio studio = m.Portraits;
            Assert.That(studio, Is.Not.Null, "made once the first arrangement is done");
            Assert.IsFalse(studio.Available, "no GPU in this run (or App.Headless): the vector icons");
            Assert.That(studio.Atlas, Is.Null);
            for (int i = 0; i < PortraitStudio.Cells; i++) Assert.That(studio.StateOf(i), Is.EqualTo(PortraitStudio.CellState.Failed));
            Assert.IsFalse(studio.Step());
            Assert.IsFalse(PortraitStudio.Rendering);
            Assert.That(m.Controller.Portraits, Is.SameAs(studio));
        }
    }
}
