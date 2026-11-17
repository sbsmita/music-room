// The Instruments screen's tile grid, the Lessons grid and the instrument options page (12-new-instruments/platform.md
// 5.1-5.3) through the controller with fake services and the real registry (every IInstrumentDefinition): the tiles in
// carousel order and their rectangles, unloaded instruments hidden with the grid closing up, no status lines, the tick
// boxes, the header notes, every poke of the table in 5.1 in both modes (the "No room" knock, the replace note, the last
// member staying), the mode switch keeping the faced instrument, MenuRowEvent ids, Sunny's two signals, the options page,
// the anchors, the summary's count noun, and the fallback icons drawn without a GPU.
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuInstrumentGridTests
    {
        /// <summary>The audio engine as the grid uses it: the Ui click (the "No room" knock).</summary>
        sealed class KnockSound : IAudioEngine
        {
            public int Clicks, AccentClicks;
            public void Click(bool accent, double when = 0) { if (accent) AccentClicks++; else Clicks++; }

            public bool IsReady => true;
            public int SampleRate => 48000;
            public double Now => 0;
            public double FrameNow => 0;
            public float RenderProgress => 1f;
            public VoiceHandle Piano(int midi, float vel, double when = 0) => VoiceHandle.None;
            public void Release(VoiceHandle h, bool thud = true, double when = 0) { }
            public VoiceHandle Pluck(int stringIndex, float vel, double when = 0, PluckOptions? options = null) => VoiceHandle.None;
            public void Damp(VoiceHandle h, int hands = 1, double when = 0) { }
            public double HarpRingSeconds(int midi) => 1;
            public void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0) { }
            public void Choke(DrumKind kind) { }
            public void SetFire(bool on) { }
            public void SetWind(bool on) { }
            public void SetSnow(bool on) { }
            public bool FireOn => false;
            public bool WindOn => false;
            public bool SnowOn => false;
            public void SnowGust(float strength, float attack, float hold, float release) { }
            public SynthSource Source(SourceId id) => null;
            public void SetSourceParent(SourceId id, Transform parent, Vector3 localPosition) { }
            public void SetSourcePosition(SourceId id, Vector3 world) { }
            public void SetMixedReality(bool mixedReality) { }
            public void SetRoomBox(Vector3 center, Vector3 size, bool realRoom) { }
            public void Chirp(ChirpKind kind, double when = 0) { }
            public bool HasVoiceLine(string id) => false;
            public float VoiceLineSeconds(string id) => -1;
            public double PlayVoiceLine(string id, double when = 0) => 0;
            public void StopVoiceLine() { }
            public bool VoiceLinePlaying => false;
            public float VoiceLevel => 0;
            public void Swish(double when = 0) { }
            public AudioTestTap TestTap { get; set; }
            public void SeedAll(uint seed) { }
        }

        static Rig OnGrid(Rig r = null)
        {
            r = r ?? new Rig();
            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            return r;
        }

        static List<MenuRow> Rows(Rig r, RowKind id)
        {
            var list = new List<MenuRow>();
            for (int i = 0; i < r.Ctl.RowCount; i++) if (r.Ctl.Row(i).Id == id) list.Add(r.Ctl.Row(i));
            return list;
        }

        static int TileOf(Rig r, InstrumentKind k) => r.RowNamed(RowKind.Tile, x => x.Inst == k);

        [Test]
        public void SevenTilesInCarouselOrderWithTheRectanglesOfTheSpec()
        {
            var r = OnGrid(new Rig(withArrange: true));
            r.Ctl.Sound = new KnockSound();
            r.Ctl.Changed();
            r.Frame();
            var tiles = Rows(r, RowKind.Tile);
            Assert.That(tiles.Count, Is.EqualTo(7), "every instrument of the registry, loaded");
            for (int i = 0; i < 7; i++)
            {
                Assert.That(tiles[i].Inst, Is.EqualTo(r.Registry.Carousel[i]), "carousel order (platform.md 4.3)");
                float x0 = 32f + (i % 4) * 244f, y0 = i < 4 ? 128f : 358f;
                Assert.That(new[] { tiles[i].X0, tiles[i].Y0, tiles[i].X1, tiles[i].Y1 }, Is.EqualTo(new[] { x0, y0, x0 + 226f, y0 + 210f }), "tile " + i);
                Assert.That(tiles[i].Label, Is.EqualTo(r.Registry.All[i].Name));
                Assert.That(tiles[i].Cell, Is.EqualTo(i), "its portrait atlas cell is its carousel index");
            }
            Assert.That(new[] { tiles[0].Inst, tiles[1].Inst, tiles[2].Inst, tiles[3].Inst },
                Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Harp, InstrumentKind.Bowls, InstrumentKind.Handpan }), "the top row: the melodic and sustained four");
            Assert.That(tiles[6].Inst, Is.EqualTo(InstrumentKind.Drums));
            // The bottom row: the segmented pair (456 px), then Move to the right edge (479 x 110); no sound switch.
            var pair = Rows(r, RowKind.Layout);
            Assert.That(pair.Count, Is.EqualTo(2));
            Assert.That(new[] { pair[0].X0, pair[0].Y0, pair[0].X1, pair[0].Y1 }, Is.EqualTo(new[] { 32f, 590f, 260f, 700f }));
            Assert.That(new[] { pair[1].X0, pair[1].Y0, pair[1].X1, pair[1].Y1 }, Is.EqualTo(new[] { 260f, 590f, 488f, 700f }));
            Assert.That(pair[1].X1 - pair[0].X0, Is.EqualTo(456f));
            Assert.That(new[] { pair[0].Label, pair[1].Label }, Is.EqualTo(new[] { "My set", "One alone" }));
            var lk = r.Ctl.Row(r.RowNamed(RowKind.Arrange));
            Assert.That(new[] { lk.X0, lk.Y0, lk.X1, lk.Y1 }, Is.EqualTo(new[] { 511f, 590f, 990f, 700f }));
            Assert.That(r.Ctl.Row(r.Ctl.RowCount - 1).Id, Is.EqualTo(RowKind.Arrange), "Move ends the bottom row (the engine is set: no Classic sound row)");
            Assert.IsFalse(r.Paint().Has("Classic sound"), "the classic sound is the only sound: no switch");
            // Nothing overlaps (a poke finds one row), and everything is on the 1024 x 768 canvas above the footer.
            for (int i = 1; i < r.Ctl.RowCount; i++)
                for (int j = i + 1; j < r.Ctl.RowCount; j++)
                {
                    MenuRow a = r.Ctl.Row(i), b = r.Ctl.Row(j);
                    bool overlap = a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1;
                    Assert.IsFalse(overlap, $"{a} and {b}");
                }
        }

        sealed class FakeHands : IHandLook
        {
            public bool RealHandsInMr { get; set; }
            public bool CutoutActive => false;
        }

        [Test]
        public void InMixedRealityTheBottomRowIsThePairMoveAndRealHands()
        {
            // "Play in my room": My set | One alone, then Move and Real hands as two 228 x 110 buttons (no sound switch).
            var r = new Rig(withArrange: true);
            r.Ctl.Sound = new KnockSound();
            r.Ctl.Hands = new FakeHands();
            r.Mr.Active = true;
            r.Ctl.Go(MenuScreen.Instruments);
            r.Ctl.Changed();
            r.Frame();
            Assert.That(r.Labels(1), Does.EndWith(",My set*,One alone,Move,Real hands"));
            var pair = Rows(r, RowKind.Layout);
            Assert.That(new[] { pair[0].X0, pair[1].X1 }, Is.EqualTo(new[] { 32f, 488f }));
            var mv = r.Ctl.Row(r.RowNamed(RowKind.Arrange));
            Assert.That(new[] { mv.X0, mv.Y0, mv.X1, mv.Y1 }, Is.EqualTo(new[] { 511f, 590f, 739f, 700f }));
            var hands = r.Ctl.Row(r.RowNamed(RowKind.Hands));
            Assert.That(new[] { hands.X0, hands.Y0, hands.X1, hands.Y1 }, Is.EqualTo(new[] { 762f, 590f, 990f, 700f }));
            Assert.That(r.Ctl.Row(r.Ctl.RowCount - 1).Id, Is.EqualTo(RowKind.Hands), "Real hands ends the row");
        }

        [Test]
        public void UnloadedInstrumentsAreNotDrawnAndTheGridClosesUp()
        {
            var r = OnGrid(new Rig(registry: TestRegistry.Make(k => k <= InstrumentKind.Harp)));
            var tiles = Rows(r, RowKind.Tile);
            Assert.That(tiles.ConvertAll(t => t.Inst), Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Harp, InstrumentKind.Drums }));
            Assert.That(tiles.ConvertAll(t => t.X0), Is.EqualTo(new[] { 32f, 276f, 520f }), "closed up in carousel order");
            Assert.That(tiles.ConvertAll(t => t.Y0), Is.EqualTo(new[] { 128f, 128f, 128f }));
            Assert.That(tiles.ConvertAll(t => t.Cell), Is.EqualTo(new[] { 0, 1, 6 }), "the atlas cell stays the carousel index");
            var c = r.Paint();
            Assert.IsFalse(c.Has("Handpan"), "an unloaded instrument's tile is not drawn");
            Assert.IsTrue(c.Has("Drums"));
            // Lessons: the same rule.
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Piano,Harp,Drums"));
        }

        [Test]
        public void TilesShowNoStatusLineOnlyTheFrameAndTheTick()
        {
            // The player asked for the "With you" / "Put away" lines under the names to go (2026-11-10): a tile shows its
            // name; the amber frame marks an instrument at the seat and the tick box My set's members.
            var r = OnGrid();
            MenuRow T(InstrumentKind k) => r.Ctl.Row(TileOf(r, k));
            Assert.IsTrue(T(InstrumentKind.Piano).On, "at the seat: the card's frame");
            Assert.IsFalse(T(InstrumentKind.Bowls).On);
            foreach (var d in r.Registry.All)
            {
                bool seat = d.Kind == InstrumentKind.Piano || d.Kind == InstrumentKind.Drums || d.Kind == InstrumentKind.Harp;
                Assert.That(T(d.Kind).Note, Is.Null.Or.Empty, d.Id + ": no status line");
                Assert.That(T(d.Kind).On, Is.EqualTo(seat), d.Id + ": the frame marks the ones at the seat");
                Assert.That(T(d.Kind).Ticked, Is.EqualTo(seat), d.Id + ": the tick box marks My set's members");
            }
            // Gliding, hidden, mixed reality: still no line.
            r.Layout.Places[InstrumentKind.Marimba] = InstrumentPlace.Gliding;
            r.Layout.Places[InstrumentKind.Handpan] = InstrumentPlace.Hidden;
            r.Mr.Active = true;
            r.Frame();
            foreach (var t in Rows(r, RowKind.Tile)) Assert.That(t.Note, Is.Null.Or.Empty, t.Inst + ": no status line in mixed reality");
            // One alone: no tick boxes; the instrument played has the frame.
            r.Mr.Active = false;
            r.Layout.Places.Clear();
            r.Layout.Mode = LayoutMode.Single;
            r.Layout.Current = InstrumentKind.Handpan;
            r.Frame();
            foreach (var t in Rows(r, RowKind.Tile)) Assert.IsFalse(t.Ticked, "no tick box in One alone");
            Assert.IsTrue(T(InstrumentKind.Handpan).On);
            Assert.IsFalse(T(InstrumentKind.Piano).On);
            var c = r.Paint();
            foreach (var t in c.Texts) Assert.That(t.Text, Does.Not.Contain("With you").And.Not.Contain("Put away"), "no status words anywhere on the grid");
        }

        [Test]
        public void TheNotesSayWhatIsWithYou()
        {
            var r = new Rig();
            Assert.That(r.Ctl.Row(1).Label, Is.EqualTo("Instruments"));
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("Piano, Drums, Harp"), "home: the set in planner order");
            r.Layout.Members.Clear();
            r.Layout.Members.AddRange(new[] { InstrumentKind.Marimba, InstrumentKind.Bowls });
            r.Layout.Version++;
            r.Frame();
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("Marimba, Singing bowls"));
            OnGrid(r);
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Marimba, Singing bowls"), "the grid's header: the set's names, no \"With you\"");
            r.Layout.Mode = LayoutMode.Single;
            r.Layout.Current = InstrumentKind.Handpan;
            r.Frame();
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Handpan alone"), "One alone: the descriptor's AloneName");
            r.Ctl.Go(MenuScreen.Home);
            r.Frame();
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("Handpan alone"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Handpan alone"));
            // The set's names are built once per set.
            Assert.That(r.Ctl.Text.SetNames(r.Layout.Members, r.Registry), Is.SameAs(r.Ctl.Text.SetNames(r.Layout.Members, r.Registry)));
        }

        [Test]
        public void OneAloneATileAtTheSeatStaysAndADisplayedOneComesAlone()
        {
            var r = OnGrid();
            r.Layout.Mode = LayoutMode.Single;
            r.Layout.Current = InstrumentKind.Harp;
            r.Frame();
            r.Tap(TileOf(r, InstrumentKind.Harp));
            Assert.That(r.Layout.Brings, Is.Empty, "the instrument at the seat: nothing (it is already here)");
            Assert.IsFalse(r.Ctl.OnOptions, "and it has no options");
            r.Tap(TileOf(r, InstrumentKind.Marimba));
            Assert.That(r.Layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Marimba, BringReason.Menu) }), "a displayed tile: Bring(kind, Menu)");
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Marimba));
            Assert.IsTrue(r.Ctl.Row(TileOf(r, InstrumentKind.Marimba)).On, "now at the seat: framed");
            Assert.That(r.Layout.PutAways, Is.Empty);
        }

        [Test]
        public void MySetPokesJoinPutAwayAndKeepTheLastMember()
        {
            var r = OnGrid();
            // A member at the seat goes back to its place.
            r.Tap(TileOf(r, InstrumentKind.Drums));
            Assert.That(r.Layout.PutAways, Is.EqualTo(new[] { InstrumentKind.Drums }));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Piano, Harp"));
            Assert.IsFalse(r.Ctl.Row(TileOf(r, InstrumentKind.Drums)).Ticked);
            // A displayed tile with room joins.
            r.Tap(TileOf(r, InstrumentKind.Handpan));
            Assert.That(r.Layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Handpan, BringReason.Menu) }));
            Assert.IsTrue(r.Ctl.Row(TileOf(r, InstrumentKind.Handpan)).Ticked);
            // Down to one member: the last one stays.
            r.Tap(TileOf(r, InstrumentKind.Piano));
            r.Tap(TileOf(r, InstrumentKind.Harp));
            Assert.That(r.Layout.Members, Is.EqualTo(new[] { InstrumentKind.Handpan }));
            int puts = r.Layout.PutAways.Count;
            r.Tap(TileOf(r, InstrumentKind.Handpan));
            Assert.That(r.Layout.PutAways.Count, Is.EqualTo(puts), "the last member is not put away");
            Assert.That(r.Layout.Members, Is.EqualTo(new[] { InstrumentKind.Handpan }));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("One stays with you"));
            r.Now += 2100;
            r.Frame();
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Handpan"), "the note lasts 2 s");
        }

        [Test]
        public void AFullSetReplacesTheMemberJoinNamesForTwoSeconds()
        {
            var r = OnGrid();
            r.Layout.Join = new JoinPreview(JoinAction.Replace, InstrumentKind.Harp);
            r.Tap(TileOf(r, InstrumentKind.Handpan));
            Assert.That(r.Layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Handpan, BringReason.Menu) }));
            Assert.That(r.Layout.Members, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Handpan }));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Harp went back to the corner"));
            r.Now += 1500;
            r.Frame();
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Harp went back to the corner"), "still at 1.5 s");
            r.Now += 600;
            r.Frame();
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Piano, Drums, Handpan"));
        }

        [Test]
        public void NoRoomSaysSoWithASoftKnockAndBringsNothing()
        {
            var r = new Rig();
            var sound = new KnockSound();
            r.Ctl.Sound = sound;
            OnGrid(r);
            foreach (var action in new[] { JoinAction.Alone, JoinAction.None })
            {
                r.Layout.Join = new JoinPreview(action);
                r.Now += 2100;
                r.Tap(TileOf(r, InstrumentKind.Marimba));
                Assert.That(r.Layout.Brings, Is.Empty, action + ": no Bring");
                Assert.That(r.Ctl.HeaderNote, Is.EqualTo("No room: take one out first"));
            }
            Assert.That(sound.Clicks, Is.EqualTo(2), "the Ui click (a soft wooden knock), once per refused poke");
            Assert.That(sound.AccentClicks, Is.EqualTo(0));
            Assert.That(r.Layout.Members, Is.EqualTo(new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp }));
        }

        [Test]
        public void TheModeSwitchKeepsTheLastOneAloneNeverTheFacedOne()
        {
            // 2026-11-10: nothing is chosen by where the head looks. My set -> One alone keeps the instrument last alone
            // when it is in My set, else My set's lead.
            var r = OnGrid();
            r.Layout.FacingKind = InstrumentKind.Drums;   // the head looks at the drums
            r.Layout.Current = InstrumentKind.Piano;
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.Single));
            Assert.That(r.Layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Piano), "the last one alone, not the faced drums");
            Assert.That(r.Layout.Sets, Is.EqualTo(1));
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.Single));
            Assert.That(r.Layout.Sets, Is.EqualTo(1), "the mode already on: nothing");
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.All));
            Assert.That(r.Layout.Mode, Is.EqualTo(LayoutMode.All));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Piano), "back to My set: the current one stays");
            // The last one alone is not in My set any more: My set's lead.
            r.Layout.Current = InstrumentKind.Marimba;
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.Single));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Piano), "the lead");
        }

        [Test]
        public void EveryPokePublishesItsRowId()
        {
            var r = new Rig(withArrange: true);
            r.Tap(r.RowNamed(RowKind.Menu, x => x.To == MenuScreen.Instruments));
            Assert.That(r.Host.LastRowId, Is.EqualTo("instruments"));
            r.Tap(TileOf(r, InstrumentKind.Bowls));
            Assert.That(r.Host.Rows[r.Host.Rows.Count - 1].RowId, Is.EqualTo("inst.bowls"));
            Assert.That(r.Host.Rows[r.Host.Rows.Count - 1].Screen, Is.EqualTo(MenuScreen.Instruments));
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.Single));
            Assert.That(r.Host.LastRowId, Is.EqualTo("layout.single"));
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.All));
            Assert.That(r.Host.LastRowId, Is.EqualTo("layout.all"));
            r.Host.KeepShown = true;   // Move on hides the panel; the test pokes on
            r.Tap(r.RowNamed(RowKind.Arrange));
            Assert.That(r.Host.LastRowId, Is.EqualTo("arrange"));
            r.Tap(r.RowNamed(RowKind.Arrange));
            r.Tap(0);
            Assert.That(r.Host.LastRowId, Is.EqualTo("back"));
            // The sunny-tour ids of the other rows are unchanged.
            r.Tap(r.RowNamed(RowKind.Menu, x => x.To == MenuScreen.Lessons));
            Assert.That(r.Host.LastRowId, Is.EqualTo("lessons"));
            r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Piano));
            Assert.That(r.Host.LastRowId, Is.EqualTo("inst.piano"));
            Assert.That(r.Host.Rows[r.Host.Rows.Count - 1].Screen, Is.EqualTo(MenuScreen.Lessons));
            r.Tap(1);
            Assert.That(r.Host.LastRowId, Is.EqualTo("course"));
            r.Ctl.Go(MenuScreen.Home);
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Exit));
            Assert.That(r.Host.LastRowId, Is.EqualTo("exit"));
            Assert.IsTrue(r.Host.Rows[r.Host.Rows.Count - 1].Confirming, "Exit's first poke asks again");
            r.Tap(r.RowNamed(RowKind.Arrange));
            Assert.That(r.Host.LastRowId, Is.EqualTo("arrange"));
            Assert.That(r.Host.Rows.TrueForAll(e => !string.IsNullOrEmpty(e.RowId)), "every press has an id");
        }

        [Test]
        public void OpeningTheGridSignalsSunny()
        {
            var r = new Rig();
            r.Ctl.Go(MenuScreen.Instruments);
            Assert.That(r.Host.Signals, Is.EqualTo(new[] { "menu.instruments", "menu.instruments.set" }), "My set with three members");
            r.Ctl.Go(MenuScreen.Home);
            r.Layout.Mode = LayoutMode.Single;
            r.Ctl.Go(MenuScreen.Instruments);
            Assert.That(r.Host.Signals, Is.EqualTo(new[] { "menu.instruments", "menu.instruments.set", "menu.instruments" }), "One alone: no set signal");
            // Opening the panel on the grid signals again; a set of one does not give the set signal.
            r.Layout.Mode = LayoutMode.All;
            r.Layout.Members.RemoveRange(1, 2);
            r.Ctl.SetShown(false);
            r.Ctl.SetShown(true);
            Assert.That(r.Host.Signals.Count, Is.EqualTo(4));
            Assert.That(r.Host.Signals[3], Is.EqualTo("menu.instruments"));
            // A hidden panel's screen change is not an opening.
            r.Ctl.SetShown(false);
            r.Ctl.Go(MenuScreen.Home);
            r.Ctl.Go(MenuScreen.Instruments);
            Assert.That(r.Host.Signals.Count, Is.EqualTo(4));
            Assert.IsTrue(r.Ctl.GridSeen, "the portraits are wanted once a grid was shown");
        }

        [Test]
        public void WentBackReadsWellForEveryPlace()
        {
            Assert.That(MenuText.Place("Front wall"), Is.EqualTo("to the front wall"));
            Assert.That(MenuText.Place("By the lamp"), Is.EqualTo("by the lamp"));
            Assert.That(MenuText.Place("Corner"), Is.EqualTo("to the corner"));
            Assert.That(MenuText.Place("Left wall"), Is.EqualTo("to the left wall"));
            Assert.That(MenuText.Place("By the window"), Is.EqualTo("by the window"));
            Assert.That(MenuText.Place("On the sill"), Is.EqualTo("to the sill"));
            Assert.That(MenuText.Place("Under the window"), Is.EqualTo("under the window"));
            Assert.That(MenuText.Place(null), Is.EqualTo("to its place"));
            var t = new MenuText();
            var reg = TestRegistry.Make();
            foreach (var d in reg.All)
            {
                string s = t.WentBack(d);
                Assert.That(s, Does.StartWith(d.Name + " went back "), d.Id);
                Assert.That(s, Does.Not.Contain("  "));
                Assert.That(s, Is.SameAs(t.WentBack(d)), "built once");
            }
            Assert.That(t.WentBack(reg.Get(InstrumentKind.Harp)), Is.EqualTo("Harp went back to the corner"));
            Assert.That(t.WentBack(reg.Get(InstrumentKind.Bowls)), Is.EqualTo("Singing bowls went back to the sill"));
        }

        [Test]
        public void TheOptionsPageCyclesStoresAndAnnouncesEachChoice()
        {
            var reg = TestRegistry.Make();
            InstrumentDescriptor th = reg.Get(InstrumentKind.Marimba);
            TestRegistry.GiveOptions(th);
            var r = OnGrid(new Rig(registry: reg));
            Assert.That(r.RowNamed(RowKind.Options), Is.EqualTo(-1), "no chip on a displayed tile");
            // One alone on the marimba: its tile opens the options page.
            r.Layout.Mode = LayoutMode.Single;
            r.Layout.Current = InstrumentKind.Marimba;
            r.Frame();
            int chip = r.RowNamed(RowKind.Options);
            Assert.That(chip, Is.GreaterThan(0), "the at-seat tile with options has its chip");
            Assert.That(chip, Is.LessThan(TileOf(r, InstrumentKind.Marimba)), "listed before the tiles: a poke on it is the chip's");
            var c = r.Ctl.Row(chip);
            var tile = r.Ctl.Row(TileOf(r, InstrumentKind.Marimba));
            Assert.That(c.X0 >= tile.X0 && c.X1 <= tile.X1 && c.Y0 >= tile.Y0 && c.Y1 <= tile.Y1, "on the tile");
            int screens = r.Host.Screens.Count;
            r.Tap(TileOf(r, InstrumentKind.Marimba));
            Assert.IsTrue(r.Ctl.OnOptions);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Instruments), "a sub-page of the Instruments screen");
            Assert.That(r.Host.Screens.Count, Is.EqualTo(screens), "no MenuEvent for the sub-page");
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Marimba"));
            Assert.That(r.Labels(1), Is.EqualTo("Marimba help,Marimba tone"));
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("On"), "the default choice");
            Assert.That(r.Ctl.Row(1).Small, Is.EqualTo("Keeps held notes in tune"));
            Assert.That(r.Ctl.Row(2).Note, Is.EqualTo("Classic"));
            r.Tap(1);
            Assert.That(r.Settings.GetInt("musicroom.marimba.help", -1), Is.EqualTo(0), "stored as musicroom.<id>.<Key>");
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("Off"));
            Assert.That(r.Host.Options.Count, Is.EqualTo(1));
            Assert.That(r.Host.Options[0].Kind, Is.EqualTo(InstrumentKind.Marimba));
            Assert.That(r.Host.Options[0].Key, Is.EqualTo("help"));
            Assert.That(r.Host.Options[0].Value, Is.EqualTo(0));
            Assert.That(r.Guide.Tips, Is.EqualTo(new[] { "tip_marimba_help" }), "the option's tip, said once");
            Assert.That(r.Host.LastRowId, Is.EqualTo("option.marimba.help"));
            r.Tap(1);
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("On"));
            Assert.That(r.Guide.Tips.Count, Is.EqualTo(1), "not again");
            r.Tap(2); r.Tap(2);
            Assert.That(r.Ctl.Row(2).Note, Is.EqualTo("Warm"), "Classic -> Bright -> Warm (wraps)");
            Assert.That(r.Settings.GetInt("musicroom.marimba.tone", -1), Is.EqualTo(0));
            Assert.That(r.Host.Options.ConvertAll(o => o.Value), Is.EqualTo(new[] { 0, 1, 2, 0 }));
            Assert.That(r.Guide.Tips.Count, Is.EqualTo(1), "the tone has no tip");
            // Back: the grid again (not home).
            r.Tap(0);
            Assert.IsFalse(r.Ctl.OnOptions);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Instruments));
            Assert.That(r.RowNamed(RowKind.Tile), Is.GreaterThan(0));
            // My set: the chip on the at-seat marimba opens the page too.
            r.Layout.Mode = LayoutMode.All;
            r.Layout.Members.Add(InstrumentKind.Marimba);
            r.Layout.Members.RemoveAt(0);
            r.Layout.Version++;
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Options));
            Assert.IsTrue(r.Ctl.OnOptions);
            Assert.That(r.Host.LastRowId, Is.EqualTo("options.marimba"));
            Assert.That(r.Layout.PutAways, Is.Empty, "the chip's poke never reaches the tile under it");
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("On"), "the stored choice is read back");
            // Going elsewhere leaves the page.
            r.Ctl.Go(MenuScreen.Home);
            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            Assert.IsFalse(r.Ctl.OnOptions);
        }

        [Test]
        public void AnchorsAnswerOnlyWhileTheirPageIsShown()
        {
            var reg = TestRegistry.Make();
            TestRegistry.GiveOptions(reg.Get(InstrumentKind.Marimba));
            var r = new Rig(registry: reg);
            Assert.IsFalse(r.Ctl.TryGetAnchorRow("menu.tile.piano", out _), "home: no tiles");
            OnGrid(r);
            Assert.IsTrue(r.Ctl.TryGetAnchorRow("menu.tile.piano", out MenuRow row));
            Assert.That(row.Inst, Is.EqualTo(InstrumentKind.Piano));
            Assert.IsTrue(r.Ctl.TryGetAnchorRow("menu.tile.handpan", out row));
            Assert.IsFalse(r.Ctl.TryGetAnchorRow("menu.option.marimba.help", out _));
            r.Ctl.OpenOptions(reg.Get(InstrumentKind.Marimba));
            r.Frame();
            Assert.IsTrue(r.Ctl.TryGetAnchorRow("menu.option.marimba.tone", out row));
            Assert.That(row.Option, Is.EqualTo(1));
            Assert.IsFalse(r.Ctl.TryGetAnchorRow("menu.tile.piano", out _), "the grid is not shown");
            r.Ctl.SetShown(false);
            Assert.IsFalse(r.Ctl.TryGetAnchorRow("menu.option.marimba.tone", out _), "hidden");
        }

        [Test]
        public void TheLessonsGridHoldsTheInstrumentsWithLessons()
        {
            var r = new Rig();
            r.Lessons.Add(new Lesson { id = "handpan-first", instrument = "handpan", title = "First tones" });
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Piano,Harp,Handpan,Drums"), "carousel order, only instruments with a lesson");
            Assert.That(r.Ctl.Row(3).Note, Is.EqualTo("1 lesson"));
            Assert.That(new[] { r.Ctl.Row(3).X0, r.Ctl.Row(3).Y0 }, Is.EqualTo(new[] { 520f, 128f }), "the grid's third slot");
            r.Tap(3);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Handpan"));
            Assert.That(r.Labels(1), Is.EqualTo("First tones"));
        }

        [Test]
        public void TheSummaryCountsWhatTheInstrumentCounts()
        {
            var r = new Rig();
            r.Tutorial.Start("piano-scale");
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));
            r.Tutorial.Noun = "strokes";
            r.Tutorial.State = TutorialState.Done;
            r.Tutorial.Summary = new TutorialSummary { Notes = 16, Mistakes = 0, Seconds = 9.04f };
            r.Tutorial.Version++;
            r.Frame();
            Assert.That(r.Ctl.SummaryLine, Is.EqualTo("16 strokes  ·  0 mistakes  ·  9.0 s"));
            Assert.That(r.Ctl.Text.Summary(16, 0, 9.04f, "notes"), Is.EqualTo("16 notes  ·  0 mistakes  ·  9.0 s"), "the noun is part of the cache key");
            Assert.That(r.Ctl.Text.Summary(16, 0, 9.04f, "strokes"), Is.SameAs(r.Ctl.SummaryLine));
        }

        [Test]
        public void FallbackIconsDrawWithoutAGpu()
        {
            // No GPU (and no portrait studio): every tile draws its descriptor's vector icon, no image.
            var r = OnGrid();
            var c = r.Paint();
            Assert.That(c.Images, Is.EqualTo(0));
            int strokes = 0;
            foreach (var d in r.Registry.All)
            {
                Assert.That(d.Portrait?.Fallback, Is.Not.Null.And.Not.Empty, d.Id + " has a fallback icon");
                var probe = new RecordingCanvas();
                Assert.That(MenuIcons.DrawStrokes(probe, d.Portrait.Fallback, 0f, 0f, MenuStyle.FallbackIconSize, MenuStyle.IconInk), Is.GreaterThan(0), d.Id);
                strokes += probe.Shapes;
                Assert.IsTrue(c.Has(d.Name), d.Id + "'s name is printed");
            }
            var blank = new RecordingCanvas();
            r.Ctl.Portraits = null;
            var plain = r.Paint();
            Assert.That(plain.Shapes, Is.GreaterThanOrEqualTo(strokes + 8), "the cards and every icon's strokes");
            // A canvas that draws images still falls back while no studio has a portrait ready.
            var gpu = new RecordingCanvas { WithImages = true };
            MenuPainter.Paint(gpu, r.Ctl, r.Now);
            Assert.That(gpu.Images, Is.EqualTo(0));
            // The descriptor stroke rules: a closed outline returns to its first point; a filled one is a fan.
            var box = new[] { new IconStroke(new[] { new Vector2(10, 10), new Vector2(90, 10), new Vector2(90, 90), new Vector2(10, 90) }, closed: true) };
            Assert.That(MenuIcons.DrawStrokes(blank, box, 0f, 0f, 100f, Color.black), Is.EqualTo(4));
            var fill = new[] { new IconStroke(new[] { new Vector2(10, 10), new Vector2(90, 10), new Vector2(90, 90), new Vector2(10, 90) }, fill: true) };
            Assert.That(MenuIcons.DrawStrokes(blank, fill, 0f, 0f, 100f, Color.black), Is.EqualTo(2));
        }

        [Test]
        public void TheGridPaintsPrintedCardsWithTheirTicks()
        {
            var r = OnGrid();
            var c = r.Paint();
            // Cards on the panel's paper, names in its dark ink, ticks only on My set's three members.
            int papers = 0;
            foreach (var f in c.Fills) if (f == MenuStyle.Paper) papers++;
            Assert.That(papers, Is.EqualTo(7 + 3), "seven cards and three tick boxes");
            var name = c.Find("Singing bowls").Value;
            Assert.That(name.Color, Is.EqualTo(MenuStyle.PaperInk));
            Assert.That(name.Size, Is.EqualTo(MenuStyle.TileNameSize));
            Assert.That(name.Y, Is.EqualTo(128f + MenuStyle.TileNameAloneY), "no status line: the name in the middle of the band");
            Assert.IsFalse(c.Has("On the window sill"), "no display place under the name");
            Assert.IsFalse(c.Has("With you"));
            Assert.IsFalse(c.Has("Put away"));
            Assert.IsTrue(c.Has("Piano, Drums, Harp"));
            Assert.That(c.Find("Piano, Drums, Harp").Value.Fit, Is.EqualTo(TextFit.Shrink), "a long set shrinks, never cut");
            Assert.That(c.Find("My set").Value.Color, Is.EqualTo(MenuStyle.Amber), "the chosen half");
            Assert.That(c.Find("One alone").Value.Color, Is.EqualTo(MenuStyle.Ink));
            // No neon: nothing on the grid uses the cyan or green accents but the back row's chevron title.
            foreach (var t in c.Texts)
                if (t.Text != "‹  Instruments") Assert.That(t.Color, Is.Not.EqualTo(MenuStyle.Cyan).And.Not.EqualTo(MenuStyle.Green), t.Text);
        }

        [Test]
        public void ATilePokeRelaysOutOnceAndAnIdleGridNever()
        {
            var r = OnGrid();
            r.Frame();
            int n = r.Ctl.Relayouts;
            for (int i = 0; i < 10; i++) r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n), "idle: the place poll finds nothing new");
            r.Tap(TileOf(r, InstrumentKind.Handpan));
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 1), "one user action, one relayout (the set change rides with it)");
            r.Layout.Places[InstrumentKind.Bowls] = InstrumentPlace.Seat;
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 2), "an instrument arriving changes its frame");
        }
    }
}
