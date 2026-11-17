// The rows added after the first headset test, through the controller with fake services: "Move instruments" on the
// home page (the On / Off setting, both layouts), Exit (first poke asks for 3 s, a second poke within that time
// quits, a poke inside the 250 ms debounce does not count, a hidden panel forgets the first poke), the start panel's
// "Play here" / "Play in my room" (hidden where passthrough or the scene is unavailable) and the Done / Reset buttons
// of arrange mode, plus what the painter draws for them and the poke mapping of the half-size buttons panel.
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuArrangeExitStartTests
    {
        [Test]
        public void HomeHasMoveInstrumentsAndResetViewAfterInstrumentsAndExitLast()
        {
            var r = new Rig(withArrange: true);
            Assert.That(r.Labels(), Is.EqualTo("Lessons,Instruments,Move instruments,Reset view,Recordings,Play in my room,Show me around,Exit"));
            Assert.That(r.Ctl.Row(2).Id, Is.EqualTo(RowKind.Arrange));
            Assert.That(r.Ctl.Row(2).Note, Is.EqualTo("Off: they stay put"));
            Assert.IsFalse(r.Ctl.Row(2).On, "off by default");
            Assert.That(r.Ctl.Row(2).Icon, Is.EqualTo(IconKind.Move));
            Assert.That(r.Ctl.Row(3).Id, Is.EqualTo(RowKind.ResetView));
            Assert.That(r.Ctl.Row(3).Note, Is.EqualTo("Instruments in front of you"));
            Assert.That(r.Ctl.Row(7).Id, Is.EqualTo(RowKind.Exit));
            Assert.That(r.Ctl.Row(7).Icon, Is.EqualTo(IconKind.Exit));
            for (int i = 0; i < 8; i++)
            {
                Assert.That(r.Ctl.Row(i).Y0, Is.EqualTo(132f + 70f * i), "eight rows: h 62, gap 8");
                Assert.That(r.Ctl.Row(i).Height, Is.EqualTo(62f));
            }
            // In MR with no Space Setup: nine rows (h 56, gap 6), the last still above the footer.
            r.Mr.Active = true;
            r.Mr.Status = MrStatus.NoRooms;
            r.Frame();
            Assert.That(r.Labels(), Is.EqualTo("Lessons,Instruments,Move instruments,Reset view,Recordings,Music room,Set up my room,Show me around,Exit"));
            for (int i = 0; i < 9; i++) Assert.That(r.Ctl.Row(i).Y0, Is.EqualTo(132f + 62f * i));
            Assert.That(r.Ctl.Row(8).Y1, Is.EqualTo(684f));
            // It is there in the single-instrument layout too.
            r.Layout.Mode = LayoutMode.Single;
            r.Layout.Current = InstrumentKind.Drums;
            r.Frame();
            Assert.That(r.RowNamed(RowKind.Arrange), Is.EqualTo(2));
            Assert.That(r.RowNamed(RowKind.ResetView), Is.EqualTo(3));
        }

        /// <summary>Reset view (2026-11-10): the layout's one bring-back path (the same as both hands held still in a
        /// pinch); the menu goes away so the instruments are seen landing; Move instruments is left as it is.</summary>
        [Test]
        public void ResetViewBringsTheInstrumentsInFrontAndClosesTheMenu()
        {
            var r = new Rig(withArrange: true);
            r.Tap(r.RowNamed(RowKind.ResetView));
            Assert.That(r.Layout.BringBacks, Is.EqualTo(1), "ILayoutService.BringToPlayer");
            Assert.That(r.Host.Hides, Is.EqualTo(1), "the menu closes");
            Assert.That(r.Arrange.Begins + r.Arrange.Ends + r.Arrange.Resets, Is.Zero, "Move instruments untouched, no saved spot forgotten");
            Assert.That(MenuRowIds.Of(RowKind.ResetView), Is.EqualTo("resetView"));
            Assert.That(MenuRowIds.FeatureOf(RowKind.ResetView), Is.EqualTo("menu.resetview"));
        }

        /// <summary>My set -> One alone keeps the instrument last alone (or the lead), never the one the head faces.</summary>
        [Test]
        public void OneAloneNeverPicksTheInstrumentTheHeadFaces()
        {
            var r = new Rig(withArrange: true);
            r.Layout.Current = InstrumentKind.Drums;
            r.Layout.FacingKind = InstrumentKind.Harp;   // the head looks at the harp
            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            int single = -1;
            for (int i = 0; i < r.Ctl.RowCount; i++) if (r.Ctl.Row(i).Id == RowKind.Layout && r.Ctl.Row(i).Mode == LayoutMode.Single) single = i;
            Assert.That(single, Is.GreaterThanOrEqualTo(0));
            r.Tap(single);
            Assert.That(r.Layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Drums), "the last one alone, not the faced harp");
        }

        [Test]
        public void MoveInstrumentsIsAnOnOffSettingThatClosesTheMenuWhenTurnedOn()
        {
            var r = new Rig(withArrange: true);
            r.Tap(r.RowNamed(RowKind.Arrange));
            Assert.That(r.Arrange.Begins, Is.EqualTo(1), "Move instruments on");
            Assert.That(r.Host.Hides, Is.EqualTo(1), "the menu closes");
            Assert.IsFalse(r.Ctl.Shown);
            // Opened again while it is on: the same row, On, and a poke turns it off; the menu stays open.
            r.Ctl.SetShown(true);
            r.Frame();
            MenuRow row = r.Ctl.Row(r.RowNamed(RowKind.Arrange));
            Assert.That(row.Label, Is.EqualTo("Move instruments"));
            Assert.That(row.Note, Is.EqualTo("On: pinch one to move it"));
            Assert.IsTrue(row.On);
            r.Tap(r.RowNamed(RowKind.Arrange));
            Assert.That(r.Arrange.Ends, Is.EqualTo(1), "off");
            Assert.IsTrue(r.Ctl.Shown, "the menu stays open");
            row = r.Ctl.Row(r.RowNamed(RowKind.Arrange));
            Assert.That(row.Label, Is.EqualTo("Move instruments"));
            Assert.That(row.Note, Is.EqualTo("Off: they stay put"));
            Assert.IsFalse(row.On);
        }

        /// <summary>Exit keeps the setting as the player left it (it is persisted; every drop was saved when it landed).</summary>
        [Test]
        public void ExitLeavesMoveInstrumentsAsItIs()
        {
            var r = new Rig(withArrange: true);
            r.Arrange.Arranging = true;
            r.Frame();
            int exit = r.RowNamed(RowKind.Exit);
            r.Tap(exit);
            r.Tap(exit);
            Assert.That(r.Host.Quits, Is.EqualTo(1));
            Assert.That(r.Arrange.Ends, Is.EqualTo(0), "not switched off on the way out");
        }

        [Test]
        public void ExitAsksFirstAndQuitsOnTheSecondPokeWithinThreeSeconds()
        {
            var r = new Rig();
            int exit = r.RowNamed(RowKind.Exit);
            Assert.That(exit, Is.EqualTo(r.Ctl.RowCount - 1), "the last row");
            Assert.That(r.Ctl.Row(exit).Label, Is.EqualTo("Exit"));
            r.Tap(exit);
            Assert.IsTrue(r.Ctl.ExitArmed);
            Assert.That(r.Ctl.Row(exit).Label, Is.EqualTo("Tap again to exit"));
            Assert.That(r.Ctl.Row(exit).Note, Is.EqualTo(""));
            Assert.That(r.Host.Quits, Is.EqualTo(0), "one poke never quits");
            r.Tap(exit);   // about 0.36 s later
            Assert.That(r.Host.Quits, Is.EqualTo(1), "the second poke quits");
            Assert.IsTrue(r.Ctl.Quitting);
            Assert.That(r.Ctl.Row(exit).Label, Is.EqualTo("Closing…"));
            r.Tap(exit);
            Assert.That(r.Host.Quits, Is.EqualTo(1), "once");
        }

        [Test]
        public void ExitForgetsTheFirstPokeAfterThreeSecondsOrWhenTheMenuCloses()
        {
            var r = new Rig();
            int exit = r.RowNamed(RowKind.Exit);
            r.Tap(exit);
            Assert.IsTrue(r.Ctl.ExitArmed);
            for (int i = 0; i < 150; i++) r.Frame(16f);   // 2.4 s: still asking
            Assert.IsTrue(r.Ctl.ExitArmed, "still within 3 s");
            for (int i = 0; i < 50; i++) r.Frame(16f);    // 3.2 s
            Assert.IsFalse(r.Ctl.ExitArmed, "3 s without a second poke");
            Assert.That(r.Ctl.Row(exit).Label, Is.EqualTo("Exit"));
            r.Tap(exit);
            Assert.That(r.Host.Quits, Is.EqualTo(0), "a poke after the timeout asks again");
            Assert.IsTrue(r.Ctl.ExitArmed);
            // Putting the menu away forgets it too.
            r.Ctl.SetShown(false);
            Assert.IsFalse(r.Ctl.ExitArmed);
            r.Ctl.SetShown(true);
            r.Frame();
            r.Tap(exit);
            Assert.That(r.Host.Quits, Is.EqualTo(0));
        }

        [Test]
        public void TwoPokesInsideTheDebounceCountOnce()
        {
            var r = new Rig();
            int exit = r.RowNamed(RowKind.Exit);
            r.Now += 300;
            // In, out past ARM, in again: 48 ms between the two touches (< 250 ms debounce per row).
            r.Poke(r.Over(exit, 0.02f)); r.Poke(r.Over(exit, -0.002f)); r.Poke(r.Over(exit, 0.01f)); r.Poke(r.Over(exit, -0.002f));
            r.Frame();
            Assert.IsTrue(r.Ctl.ExitArmed);
            Assert.That(r.Host.Quits, Is.EqualTo(0), "a bounce is not a second poke");
        }

        [Test]
        public void StartPanelOffersPlayHereAndPlayInMyRoom()
        {
            var r = new Rig();
            r.Ctl.ResetScreen(MenuScreen.Start);
            r.Frame();
            Assert.That(r.Host.Screens, Is.Empty, "a silent screen change");
            Assert.That(r.Labels(), Is.EqualTo("Play here,Play in my room"));
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("VR Music Room"));
            Assert.That(r.Ctl.Row(0).Height, Is.EqualTo(MenuStyle.StartRowH), "two big rows");
            Assert.That(r.Ctl.Row(1).Y0, Is.EqualTo(MenuStyle.StartTop + MenuStyle.StartRowH + MenuStyle.StartGap));
            Assert.That(r.Ctl.Row(1).Y1, Is.LessThan(MenuStyle.FooterY - 20f));
            r.Tap(r.RowNamed(RowKind.PlayHere));
            Assert.That(r.Host.StartChoices, Is.EqualTo(new[] { false }));
            Assert.That(r.Mr.Enters, Is.EqualTo(0));
            r.Tap(r.RowNamed(RowKind.PlayRoom));
            Assert.That(r.Mr.Enters, Is.EqualTo(1), "Play in my room enters mixed reality");
            Assert.That(r.Host.StartChoices, Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public void StartPanelHidesPlayInMyRoomWithoutPassthroughOrScene()
        {
            var r = new Rig();
            r.Ctl.ResetScreen(MenuScreen.Start);
            r.Mr.Available = false;
            r.Frame();
            Assert.That(r.Labels(), Is.EqualTo("Play here"), "no passthrough");
            r.Mr.Available = true;
            r.Mr.Status = MrStatus.NotAvailable;
            r.Ctl.Changed();
            r.Frame();
            Assert.That(r.Labels(), Is.EqualTo("Play here"), "no scene support");
            var w = new Rig(withMr: false);
            w.Ctl.ResetScreen(MenuScreen.Start);
            w.Frame();
            Assert.That(w.Labels(), Is.EqualTo("Play here"), "no mixed-reality service");
        }

        [Test]
        public void ArrangeButtonsAreDoneAndReset()
        {
            var r = new Rig(withMr: false, withArrange: true);
            r.Ctl.ResetScreen(MenuScreen.Arrange);
            r.Frame();
            Assert.That(r.Labels(), Is.EqualTo("Done,Reset"));
            Assert.That(new[] { r.Ctl.Row(0).X0, r.Ctl.Row(0).X1, r.Ctl.Row(1).X0, r.Ctl.Row(1).X1 }, Is.EqualTo(new[] { 40f, 492f, 532f, 984f }));
            Assert.That(r.Ctl.Row(0).Height, Is.GreaterThan(300f), "big targets: at the panel's half scale still 7 x 6 cm");
            r.Arrange.Arranging = true;
            r.Tap(r.RowNamed(RowKind.Reset));
            Assert.That(r.Arrange.Resets, Is.EqualTo(1));
            Assert.IsTrue(r.Arrange.Arranging, "Reset keeps arranging");
            r.Tap(r.RowNamed(RowKind.Done));
            Assert.That(r.Arrange.Ends, Is.EqualTo(1));
        }

        [Test]
        public void ThePainterDrawsTheNewScreens()
        {
            var r = new Rig();
            r.Ctl.ResetScreen(MenuScreen.Start);
            r.Frame();
            var c = r.Paint();
            Assert.IsTrue(c.Has("VR Music Room"));
            Assert.IsTrue(c.Has(MenuStyle.StartLine));
            Assert.IsTrue(c.Has("Play here"));
            Assert.IsTrue(c.Has("Play in my room"));
            Assert.That(c.Find("Play here").Value.Color, Is.EqualTo(MenuStyle.Green));

            var b = new Rig(withArrange: true);
            b.Ctl.ResetScreen(MenuScreen.Arrange);
            b.Frame();
            c = b.Paint();
            Assert.IsTrue(c.Has("Move instruments"));
            Assert.IsTrue(c.Has(MenuStyle.ArrangeLine1));
            Assert.IsTrue(c.Has(MenuStyle.ArrangeLine2));
            Assert.That(c.Find("Done").Value.Color, Is.EqualTo(MenuStyle.Green));
            Assert.IsTrue(c.Has("Reset"));

            var h = new Rig();
            h.Tap(h.RowNamed(RowKind.Exit));
            c = h.Paint();
            Assert.That(c.Find("Tap again to exit").Value.Color, Is.EqualTo(MenuStyle.Red), "the asking Exit row is red");
        }

        [Test]
        public void TheHalfSizeButtonsPanelPokesLikeTheMenu()
        {
            const float s = MenuStyle.ArrangeScale;
            // A fingertip 3 mm in front of the face of the half-size panel reads 3 mm (ARM), not 6.
            Vector3 onFace = s * MenuMath.CanvasPoint(300f, 500f, 0f);
            MenuMath.FromCanvas(300f, 500f, out float u0, out _);
            Vector3 normal = MenuMath.SurfaceNormal(u0);
            Vector3 mapped = MenuMath.ScaledToPanel(onFace + normal * 0.003f, s);
            MenuMath.ToSurface(mapped, out float u, out float y, out float z);
            Assert.That(z, Is.EqualTo(0.003f).Within(2e-4f));
            MenuMath.ToCanvas(u, y, out float px, out float py);
            Assert.That(px, Is.EqualTo(300f).Within(1.5f));
            Assert.That(py, Is.EqualTo(500f).Within(1.5f));
            // Scale 1 is the identity.
            var p = new Vector3(0.05f, -0.02f, -0.01f);
            Assert.That(MenuMath.ScaledToPanel(p, 1f), Is.EqualTo(p));
        }
    }
}
