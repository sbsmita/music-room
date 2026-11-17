// The poke state machine (spec 08 section 1.4) and its acceptance cases (spec 08 section 9, the menu and tutorial
// checks): fire once, re-arm past ARM, the 250 ms debounce, never from behind, the edge margin, the curved
// surface at the edge, scrolling lists (drag scrolls, a still tap fires on release), the hover fade, the desktop mouse.
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuPokeTests
    {
        // The Move button (the "Move instruments" setting) is the toggle these pokes switch (it was the Lock button).
        static Rig OnInstruments()
        {
            var r = new Rig(withArrange: true);
            r.Host.KeepShown = true;
            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Instruments));
            return r;
        }

        [Test]
        public void TouchFiresOnceAndHoldingDoesNotRepeat()
        {
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            r.Poke(r.Over(moveRow, 0.02f));
            Assert.IsTrue(r.Ctl.PokeArmed(1), "in front past ARM: armed");
            r.Poke(r.Over(moveRow, -0.005f));
            Assert.IsTrue(r.Arrange.Arranging, "z +.02 -> -.005 fires");
            Assert.IsTrue(r.Ctl.PokeTouching(1));
            for (int i = 0; i < 30; i++) r.Poke(r.Over(moveRow, -0.005f));
            Assert.IsTrue(r.Arrange.Arranging, "holding does not fire again (it would toggle back)");
        }

        [Test]
        public void ReentryNeedsToComeBackPastArmAndWaitOutTheDebounce()
        {
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            r.Poke(r.Over(moveRow, 0.02f));
            r.Poke(r.Over(moveRow, -0.005f));
            Assert.IsTrue(r.Arrange.Arranging);
            // Back out to +2 mm, inside ARM: the touch has not ended, so pushing in again is not a new poke.
            r.Poke(r.Over(moveRow, 0.002f));
            Assert.IsTrue(r.Ctl.PokeTouching(1), "+2 mm is still within ARM: still touching");
            r.Poke(r.Over(moveRow, -0.003f));
            Assert.IsTrue(r.Arrange.Arranging, "re-entry from +.002 does not fire");
            // Out past ARM (+4 mm), straight back in: within 250 ms of the first fire the row ignores it.
            r.Poke(r.Over(moveRow, 0.004f));
            Assert.IsFalse(r.Ctl.PokeTouching(1));
            Assert.IsTrue(r.Ctl.PokeArmed(1));
            r.Poke(r.Over(moveRow, -0.003f));
            Assert.IsTrue(r.Arrange.Arranging, "debounced: under 250 ms since it fired");
            r.Poke(r.Over(moveRow, 0.004f));
            r.Now += 260;
            r.Poke(r.Over(moveRow, -0.003f));
            Assert.IsFalse(r.Arrange.Arranging, "from +.004, after 250 ms, it fires again");
        }

        [Test]
        public void NeverFromBehind()
        {
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            r.Poke(r.Over(moveRow, -0.07f));
            r.Poke(r.Over(moveRow, -0.01f));
            r.Poke(r.Over(moveRow, -0.002f));
            Assert.IsFalse(r.Arrange.Arranging, "-.07 -> -.01: a tip reaching from behind never fires");
            Assert.IsFalse(r.Ctl.PokeArmed(1));
            // An armed tip that jumps more than 6 cm behind does not touch either.
            r.Poke(r.Over(moveRow, 0.02f));
            r.Poke(r.Over(moveRow, -0.065f));
            Assert.IsFalse(r.Arrange.Arranging);
            Assert.IsFalse(r.Ctl.PokeTouching(1));
        }

        [Test]
        public void InactiveOrNewPointersDoNotFire()
        {
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            r.Poke(r.Over(moveRow, 0.02f));
            r.Poke(r.Over(moveRow, -0.005f), active: false);
            Assert.IsFalse(r.Arrange.Arranging, "an inactive pointer resets");
            Assert.IsFalse(r.Ctl.PokeArmed(1));
            r.Poke(r.Over(moveRow, 0.02f));
            r.Poke(r.Over(moveRow, -0.005f), wasActive: false);
            Assert.IsFalse(r.Arrange.Arranging, "a pointer that just appeared (not WasActive) cannot touch");
        }

        [Test]
        public void TheEdgeMarginResetsAndASideEntryDoesNotFire()
        {
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            var row = r.Ctl.Row(moveRow);
            float y = (0.5f - row.CentreY / 768f) * MenuMath.H;
            r.Poke(MenuMath.SurfacePoint(0f, y, 0.02f));
            Assert.IsTrue(r.Ctl.PokeArmed(1));
            r.Poke(MenuMath.SurfacePoint(MenuMath.W / 2f + 0.011f, y, 0.02f));
            Assert.IsFalse(r.Ctl.PokeArmed(1), "past the 1 cm margin the state resets");
            r.Poke(MenuMath.SurfacePoint(MenuMath.W / 2f + 0.011f, y, -0.002f));
            r.Poke(MenuMath.SurfacePoint(0f, y, -0.002f));
            Assert.IsFalse(r.Arrange.Arranging, "sliding in from the side, behind the surface: no fire");
            // Inside the margin but off the face: armed, and a touch there hits no row.
            r.Poke(MenuMath.SurfacePoint(MenuMath.W / 2f + 0.005f, y, 0.02f));
            Assert.IsTrue(r.Ctl.PokeArmed(1));
        }

        [Test]
        public void ThePokeFollowsTheCurvedSurfaceAtTheEdge()
        {
            // menu-check: 12 cm across the face, 3 cm in front of the flat plane is 2.1 cm in front of the curve (no
            // hover); 3 mm in front of the flat plane is 6 mm behind the curve: crossing to it fires the handpan tile (the
            // grid's last column, played alone: it comes alone).
            var r = OnInstruments();
            r.Layout.Mode = LayoutMode.Single;
            r.Frame();
            int edge = r.RowNamed(RowKind.Tile, x => x.Inst == InstrumentKind.Handpan);
            var row = r.Ctl.Row(edge);
            Assert.That(0.8f * Mathf.Sin(0.15f), Is.InRange((row.X0 / 1024f - 0.5f) * MenuMath.W, (row.X1 / 1024f - 0.5f) * MenuMath.W), "the poke lands on the tile");
            float ex = 0.8f * Mathf.Sin(0.15f), ey = (0.5f - row.CentreY / 768f) * MenuMath.H;
            r.Now += 300;
            r.Poke(new Vector3(ex, ey, -0.03f));
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(-1), "2.1 cm off the curve: no hover");
            r.Poke(new Vector3(ex, ey, -0.003f));
            Assert.That(r.Layout.Brings, Is.EqualTo(new[] { (InstrumentKind.Handpan, BringReason.Menu) }));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Handpan));
        }

        static Rig OnPianoCourses()
        {
            var r = new Rig();
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Piano));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            Assert.That(r.Ctl.MaxScroll, Is.EqualTo(1320f), "15 piano courses");
            return r;
        }

        [Test]
        public void ADragOnAScrollingListScrollsWithoutFiring()
        {
            var r = OnPianoCourses();
            r.Now += 300;
            Vector3 p = r.Over(1, 0.03f);
            r.Poke(p);
            r.Poke(r.Over(1, -0.002f));
            Assert.That(r.Tutorial.Started, Is.Empty, "a touch on a scrolling list waits for the release");
            // 9 mm up: past SLOP (8 mm), the content follows the finger: 9 / 240 x 768 = 28.8 px.
            Vector3 up = r.Over(1, -0.002f) + new Vector3(0f, 0.009f, 0f);
            r.Poke(up);
            Assert.That(r.Ctl.Scroll, Is.EqualTo(28.8f).Within(0.05f));
            Assert.That(r.Tutorial.Started, Is.Empty);
            r.Poke(up + new Vector3(0f, 0f, -0.04f));
            Assert.That(r.Tutorial.Started, Is.Empty, "a drag's release does not start the course");
            Assert.That(r.Ctl.Scroll, Is.EqualTo(28.8f).Within(0.05f));
        }

        [Test]
        public void ShortMovesStayATapAndALongDragScrollsMore()
        {
            var r = OnPianoCourses();
            r.Now += 300;
            r.Poke(r.Over(2, 0.03f));
            r.Poke(r.Over(2, -0.002f));
            r.Poke(r.Over(2, -0.002f) + new Vector3(0f, 0.007f, 0f));
            Assert.That(r.Ctl.Scroll, Is.EqualTo(0f), "7 mm is within SLOP: no scroll");
            r.Poke(r.Over(2, 0.03f));
            Assert.That(r.Tutorial.Started.Count, Is.EqualTo(1), "a still tap fires on release");
            Assert.That(r.Tutorial.Started[0], Is.EqualTo("piano-mary"), "the second course");

            // tutorial-check dragScrolls: 4 cm up scrolls > 100 px; the scroll clamps to maxScroll.
            var s = OnPianoCourses();
            s.Now += 300;
            s.Poke(s.Over(1, 0.03f));
            s.Poke(s.Over(1, -0.002f));
            s.Poke(s.Over(1, -0.002f) + new Vector3(0f, 0.04f, 0f));
            Assert.That(s.Ctl.Scroll, Is.GreaterThan(100f));
            s.Ctl.SetScroll(1e6f);
            Assert.That(s.Ctl.Scroll, Is.EqualTo(s.Ctl.MaxScroll));
            Assert.That(s.Ctl.Row(s.Ctl.RowCount - 1).Lesson.id, Is.EqualTo("piano-elise"), "the last course shows at the end");
            s.Ctl.SetScroll(-5f);
            Assert.That(s.Ctl.Row(1).Lesson.id, Is.EqualTo("piano-ode"), "back to the top");   // first five: Ode to Joy first
        }

        [Test]
        public void ALongPressDoesNotFireTheRowThatReplacedIt()
        {
            // Lessons -> Piano fires on touch and the courses list (scrolling) takes the screen; holding past the 250 ms
            // debounce and releasing must not start the first course (an earlier version could: release-fire on a reused slot).
            var r = new Rig();
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            int piano = r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Piano);
            r.Now += 300;
            r.Poke(r.Over(piano, 0.03f));
            r.Poke(r.Over(piano, -0.002f));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            for (int i = 0; i < 30; i++) r.Poke(r.Over(piano, -0.002f));
            r.Poke(r.Over(piano, 0.03f));
            Assert.That(r.Tutorial.Started, Is.Empty);
        }

        [Test]
        public void HoverLightsTheRowAndFadesForAbout120Ms()
        {
            var r = OnInstruments();
            int row = 2;
            r.Poke(r.Over(row, 0.006f));
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(row), "within HOVER (10 mm) of the face");
            // ARCHITECTURE.md 15.6: hover never repaints; the fade (HOVER_MS 120) is a face-shader uniform.
            int dirtyFrames = 0;
            float kAt60 = -1f;
            for (int i = 0; i < 20; i++)
            {
                r.Ctl.Dirty = false;
                r.Poke(r.Over(row, 0.006f), dtMs: 20f);
                if (r.Ctl.Dirty) dirtyFrames++;
                if (i == 2) kAt60 = r.Ctl.HoverK(r.Now);
            }
            Assert.That(dirtyFrames, Is.EqualTo(0), "the hover fade never marks the canvas for a repaint");
            Assert.That(kAt60, Is.EqualTo(0.5f).Within(1e-4f), "half way at 60 ms");
            Assert.That(r.Ctl.HoverK(r.Now), Is.EqualTo(1f), "rests fully on after 120 ms");
            // Away: the previous row fades out over 120 ms, then is dropped.
            r.Poke(r.Over(row, 0.03f));
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(-1));
            Assert.That(r.Ctl.HoverPrev, Is.EqualTo(row));
            Assert.IsFalse(r.Ctl.Dirty, "leaving a row does not repaint either");
            r.Poke(r.Over(row, 0.03f), dtMs: 100f);
            Assert.That(r.Ctl.HoverPrev, Is.EqualTo(row), "still fading out at 100 ms");
            r.Poke(r.Over(row, 0.03f), dtMs: 20f);
            Assert.That(r.Ctl.HoverPrev, Is.EqualTo(-1), "dropped once the fade-out is over");
            // Hover range: -5 mm .. +10 mm.
            r.Poke(r.Over(row, -0.004f));
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(row));
            r.Poke(r.Over(row, 0.011f));
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(-1));
        }

        [Test]
        public void HoverHighlightGeometryIsTheReferenceRowShape()
        {
            // drawRow: the row's rect, radius min(28, h/2), fill + 4 px stroke; drawHeader: the back row's fill only.
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            MenuRow row = r.Ctl.Row(moveRow);
            Assert.IsTrue(MenuPainter.HoverShape(r.Ctl, moveRow, out Vector4 rect, out Vector2 shape, out Vector2 clip));
            Assert.That(rect, Is.EqualTo(new Vector4(row.X0, row.Y0, row.X1, row.Y1)));
            Assert.That(shape.x, Is.EqualTo(MenuStyle.BarRadius), "the Move button's own corner (platform.md 5.1)");
            Assert.That(shape.y, Is.EqualTo(1f), "rows have the stroke");
            Assert.IsTrue(MenuPainter.HoverShape(r.Ctl, r.RowNamed(RowKind.Tile), out _, out shape, out _));
            Assert.That(shape.x, Is.EqualTo(MenuStyle.TileRadius), "a tile: the card's corner");
            Assert.That(clip.x, Is.LessThan(-1000f), "not a list row: no clip");
            Assert.IsTrue(MenuPainter.HoverShape(r.Ctl, 0, out rect, out shape, out _));
            Assert.That(rect, Is.EqualTo(new Vector4(6f, 6f, MenuStyle.CW - 6f, MenuStyle.Header)));
            Assert.That(shape, Is.EqualTo(new Vector2(MenuStyle.Corner - 6f, 0f)), "the back row: fill only, corner - 6");
            Assert.IsFalse(MenuPainter.HoverShape(r.Ctl, -1, out _, out _, out _));
            Assert.IsFalse(MenuPainter.HoverShape(r.Ctl, r.Ctl.RowCount, out _, out _, out _));
            // A course row is clipped to the list (130..750), and follows the scroll.
            var c = OnPianoCourses();
            Assert.IsTrue(MenuPainter.HoverShape(c.Ctl, 2, out rect, out _, out clip));
            Assert.That(clip, Is.EqualTo(new Vector2(MenuStyle.ListTop, MenuStyle.ListBottom)));
            float y0 = rect.y;
            c.Ctl.SetScroll(40f);   // the first course still shows 80 px, so row 2 is still the second course
            c.Frame();
            Assert.IsTrue(MenuPainter.HoverShape(c.Ctl, 2, out rect, out _, out _));
            Assert.That(rect.y, Is.EqualTo(y0 - 40f).Within(1e-3f));
            // Home row 0 is a normal row (no back header there).
            var h = new Rig();
            Assert.IsTrue(MenuPainter.HoverShape(h.Ctl, 0, out _, out shape, out _));
            Assert.That(shape.y, Is.EqualTo(1f));
        }

        [Test]
        public void ThePainterDrawsNoHoverColour()
        {
            // The canvas holds no hover state, so a hovered and an idle row paint the same (the shader adds the light).
            var r = OnInstruments();
            int row = 2;
            string idle = Fills(r.Paint());
            r.Poke(r.Over(row, 0.006f));
            r.Poke(r.Over(row, 0.006f), dtMs: 200f);
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(row));
            Assert.That(Fills(r.Paint()), Is.EqualTo(idle));
        }

        static string Fills(RecordingCanvas c)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in c.Fills) sb.Append(f.ToString()).Append(';');
            return sb.ToString() + c.Shapes;
        }

        [Test]
        public void HidingClearsEveryPokeState()
        {
            var r = OnInstruments();
            int moveRow = r.RowNamed(RowKind.Arrange);
            r.Poke(r.Over(moveRow, 0.02f));
            r.Poke(r.Over(moveRow, 0.006f));
            Assert.IsTrue(r.Ctl.PokeArmed(1));
            Assert.That(r.Ctl.HoverRow, Is.GreaterThanOrEqualTo(0));
            r.Ctl.SetShown(false);
            Assert.IsFalse(r.Ctl.PokeArmed(1));
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(-1));
            r.Ctl.SetShown(true);
            r.Poke(r.Over(moveRow, -0.002f));
            Assert.IsFalse(r.Arrange.Arranging, "after a hide the tip must arm again");
        }

        [Test]
        public void TheDesktopMouseClicksRowsAndDragsLists()
        {
            var r = new Rig();
            var row0 = r.Ctl.Row(0);
            r.Now += 300;
            r.Ctl.BeginPokes(r.Now);
            r.Ctl.Mouse(true, (row0.X0 + row0.X1) / 2f, row0.CentreY, true, true);
            r.Ctl.EndPokes();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lessons), "mouse-down fires the row (desktopDown)");

            var c = OnPianoCourses();
            var row1 = c.Ctl.Row(1);
            float x = (row1.X0 + row1.X1) / 2f, y = row1.CentreY;
            c.Now += 300;
            void M(float py, bool down, bool held) { c.Now += 16; c.Ctl.Sync(); c.Ctl.BeginPokes(c.Now); c.Ctl.Mouse(true, x, py, down, held); c.Ctl.EndPokes(); }
            M(y, true, true);
            Assert.That(c.Tutorial.Started, Is.Empty, "on a scrolling list a click waits for the release");
            Assert.IsTrue(c.Ctl.MouseTouching);
            M(y - 40f, false, true);
            Assert.That(c.Ctl.Scroll, Is.EqualTo(40f).Within(1e-3f), "dragging up 40 px scrolls 40 px");
            M(y - 40f, false, false);
            Assert.That(c.Tutorial.Started, Is.Empty, "a drag's release does not fire");
            M(y, true, true);
            M(y, false, false);
            Assert.That(c.Tutorial.Started.Count, Is.EqualTo(1), "a still click fires on release");
            Assert.That(c.Ctl.HoverRow, Is.GreaterThanOrEqualTo(0), "the row under the mouse lights");
        }
    }
}
