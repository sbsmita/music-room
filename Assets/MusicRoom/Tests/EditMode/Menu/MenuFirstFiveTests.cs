// The menu's first-five-minutes parts: the Done screen's "Next: <title>" row and the remembered courses (tutorials-audit.md
// B6), the Sunny screen and its Tips list (sunny-tour.md 3.3), Sunny's spotlight on a row, the row events Sunny listens
// to, and a lesson the menu does not list (the first run's song) never taking the panel.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuFirstFiveTests
    {
        sealed class FakeProgress : ILessonProgress
        {
            readonly ILessonLibrary _lib;
            public readonly HashSet<string> Done = new HashSet<string>();
            public FakeProgress(ILessonLibrary lib) { _lib = lib; }
            public bool IsDone(string id) => id != null && Done.Contains(id);
            public int BestMistakes(string id) => IsDone(id) ? 0 : -1;
            public int DoneCount => Done.Count;
            public string NextLessonId(string id)
            {
                Lesson cur = _lib.Get(id);
                if (cur == null) return null;
                bool after = false;
                foreach (Lesson l in _lib.Lessons)
                {
                    if (after && l.instrument == cur.instrument) return l.id;
                    if (l == cur) after = true;
                }
                return null;
            }
        }

        sealed class FakeTour : IGuideTour
        {
            public readonly List<string> Played = new List<string>();
            public readonly HashSet<int> Heard = new HashSet<int> { 1 };
            public int Starts;
            public void StartTour() => Starts++;
            public int TipCount => 12;
            public string TipId(int i) => "tip_" + i;
            public string TipTitle(int i) => "Tip number " + i;
            public bool TipHeard(int i) => Heard.Contains(i);
            public int TipsHeard => Heard.Count;
            public void PlayTip(string id) => Played.Add(id);
            public bool TipsEnabled { get; set; } = true;
        }

        sealed class RowHost : IMenuHost
        {
            public MenuController Ctl;
            public readonly List<string> Rows = new List<string>();
            public readonly List<bool> Confirming = new List<bool>();
            public int Hides;
            public void Hide() { Hides++; Ctl?.SetShown(false); Ctl?.SetShown(true); }
            public void ScreenChanged(MenuScreen screen) { }
            public void RowPressed(in MenuRowEvent e) { Rows.Add(e.RowId); Confirming.Add(e.Confirming); }
        }

        static Rig WithFirstFive(out FakeProgress progress, out FakeTour tour)
        {
            var r = new Rig();
            progress = new FakeProgress(r.Lessons);
            tour = new FakeTour();
            r.Ctl.Progress = progress;
            r.Ctl.Tour = tour;
            r.Ctl.Changed();
            r.Frame();
            return r;
        }

        [Test]
        public void ALessonDoneShowsNextWithTheNextSongAndStartsIt()
        {
            Rig r = WithFirstFive(out FakeProgress progress, out _);
            r.Tutorial.Start("piano-ode");
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));
            r.Tutorial.State = TutorialState.Done;
            r.Tutorial.EventsDone = 30;
            r.Tutorial.Summary = new TutorialSummary { Notes = 30, Mistakes = 2, Seconds = 21f };
            r.Tutorial.Version++;
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Next: Mary Had a Little Lamb,Replay,Courses"));
            Assert.That(r.Ctl.Row(1).Id, Is.EqualTo(RowKind.Next));
            Assert.That(r.Ctl.Row(1).Y0, Is.EqualTo(MenuStyle.NextTop[0]));
            Assert.That(r.Ctl.Row(3).Y1, Is.LessThanOrEqualTo(MenuConstants.CanvasH - 20f), "all three rows on the face");
            RecordingCanvas c = r.Paint();
            Assert.That(c.Find("Next: Mary Had a Little Lamb").Value.Color, Is.EqualTo(MenuStyle.Green), "Next is the one in green");
            Assert.That(c.Find("Replay").Value.Color, Is.EqualTo(MenuStyle.Ink));
            r.Tap(r.RowNamed(RowKind.Next));
            Assert.That(r.Tutorial.Started, Is.EqualTo(new[] { "piano-ode", "piano-mary" }), "Next starts the next song");
        }

        [Test]
        public void TheLastLessonOfAnInstrumentKeepsReplayAndCourses()
        {
            Rig r = WithFirstFive(out _, out _);
            r.Tutorial.Start("piano-elise");
            r.Frame();
            r.Tutorial.State = TutorialState.Done;
            r.Tutorial.Version++;
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Replay,Courses"));
            Assert.That(r.Ctl.Row(1).Y0, Is.EqualTo(MenuStyle.DoneTop[0]));
        }

        [Test]
        public void FinishedCoursesAreMarkedDoneInTheList()
        {
            Rig r = WithFirstFive(out FakeProgress progress, out _);
            progress.Done.Add("piano-ode");
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Piano));
            int ode = r.RowNamed(RowKind.Course, x => x.Lesson.id == "piano-ode");
            int mary = r.RowNamed(RowKind.Course, x => x.Lesson.id == "piano-mary");
            Assert.That(r.Ctl.Row(ode).Note, Is.EqualTo("Done  ·  30 notes"));
            Assert.That(r.Ctl.Row(mary).Note, Is.EqualTo("26 notes"));
        }

        [Test]
        public void TheSunnyRowOpensTheSunnyScreenWhileSunnyIsAway()
        {
            Rig r = WithFirstFive(out _, out FakeTour tour);
            int g = r.RowNamed(RowKind.Guide);
            Assert.That(r.Ctl.Row(g).Label, Is.EqualTo("Sunny"));
            Assert.That(r.Ctl.Row(g).Note, Is.EqualTo("Tour and tips"));
            r.Tap(g);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Guide));
            Assert.That(r.Labels(1), Is.EqualTo("Show me around,Tips,Make a song in layers,Tips while I play*"));
            Assert.That(r.Ctl.Row(2).Note, Is.EqualTo("1 of 12 heard"));
            Assert.That(r.Ctl.Footer, Is.EqualTo("Poke Sunny anytime to skip"));
            r.Tap(r.RowNamed(RowKind.TipsOn));
            Assert.IsFalse(tour.TipsEnabled, "Tips while I play: off");
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.TipsOn)).Note, Is.EqualTo("Off: only from here"));
            r.Tap(r.RowNamed(RowKind.Tour));
            Assert.That(tour.Starts, Is.EqualTo(1), "Show me around replays the tour");
            // While Sunny is here the same row hides it (the tour put the panel away: shown again).
            r.Ctl.SetShown(true);
            r.Ctl.Go(MenuScreen.Home);
            r.Guide.Present = true;
            r.Frame();
            int h = r.RowNamed(RowKind.Guide);
            Assert.That(r.Ctl.Row(h).Label, Is.EqualTo("Hide Sunny"));
            r.Tap(h);
            Assert.That(r.Guide.Dismisses, Is.EqualTo(1));
        }

        [Test]
        public void TheSunnyScreenPaintsEveryRowAndEachOneWorks()
        {
            // Headset report 2026-11-10: "it shows empty screen when clicked on sunny". The screen was painted as the
            // Instruments grid, which draws only tiles and the grid's bottom row: the header, the footer and nothing else.
            Rig r = WithFirstFive(out _, out FakeTour tour);
            r.Tap(r.RowNamed(RowKind.Guide));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Guide));
            RecordingCanvas c = r.Paint();
            Assert.That(r.Ctl.RowCount, Is.EqualTo(5));
            for (int i = 1; i < r.Ctl.RowCount; i++)
            {
                MenuRow row = r.Ctl.Row(i);
                Assert.IsTrue(c.Has(row.Label), $"row {i} '{row.Label}' is painted");
                Assert.IsTrue(c.Has(row.Note), $"row {i}'s note '{row.Note}' is painted");
                RecordingCanvas.TextOp label = c.Find(row.Label).Value, note = c.Find(row.Note).Value;
                Assert.That(label.Y, Is.InRange(row.Y0, row.Y1), "the label inside its row");
                Assert.That(note.Y, Is.GreaterThan(label.Y), "the note under the label (a long label and note never collide)");
                Assert.That(label.X + label.MaxWidth, Is.LessThanOrEqualTo(row.X1), "the label fits its row");
            }
            Assert.That(c.Find("On: Sunny helps").Value.Color, Is.EqualTo(MenuStyle.Amber), "Tips while I play: On, in amber");
            Assert.IsTrue(c.Has("Poke Sunny anytime to skip"), "the footer");
            Assert.IsTrue(c.Has("‹  Sunny"), "the back row");

            // Tips while I play: Off (Sunny stays quiet), painted dim; and On again.
            r.Tap(r.RowNamed(RowKind.TipsOn));
            Assert.IsFalse(tour.TipsEnabled);
            c = r.Paint();
            Assert.That(c.Find("Off: only from here").Value.Color, Is.EqualTo(MenuStyle.Dim));
            r.Tap(r.RowNamed(RowKind.TipsOn));
            Assert.IsTrue(tour.TipsEnabled);
            // Tips: the list of every tip, painted.
            r.Tap(r.RowNamed(RowKind.Menu, x => x.To == MenuScreen.Tips));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Tips));
            Assert.IsTrue(r.Paint().Has("Tip number 0"));
            r.Tap(0);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Guide), "back to the Sunny screen");
            // Make a song in layers: Sunny's deck walkthrough (the panel goes away).
            int hides = r.Host.Hides;
            r.Tap(r.RowNamed(RowKind.Layers));
            Assert.That(r.Guide.DeckGuides, Is.EqualTo(1), "the tape deck walkthrough starts");
            r.Ctl.SetShown(true);
            r.Ctl.Go(MenuScreen.Guide);
            r.Frame();
            // Show me around: the quick tour again (the panel goes away first; the tour opens it itself).
            r.Tap(r.RowNamed(RowKind.Tour));
            Assert.That(tour.Starts, Is.EqualTo(1), "Show me around replays the tour");
            Assert.That(r.Host.Hides, Is.EqualTo(hides + 2));
        }

        [Test]
        public void TheTipsListScrollsAndPlaysATip()
        {
            Rig r = WithFirstFive(out _, out FakeTour tour);
            r.Ctl.Go(MenuScreen.Guide);
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Menu, x => x.To == MenuScreen.Tips));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Tips));
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Tips"));
            Assert.That(r.Ctl.MaxScroll, Is.EqualTo(12 * MenuStyle.ListPitch - (MenuStyle.ListPitch - MenuStyle.ListH) - (MenuStyle.ListBottom - MenuStyle.ListTop)));
            Assert.That(r.Ctl.Row(1).Label, Is.EqualTo("Tip number 0"));
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("New"));
            Assert.That(r.Ctl.Row(2).Note, Is.EqualTo("Heard"));
            // A still tap on a list row fires on release.
            r.Tap(2);
            Assert.That(tour.Played, Is.EqualTo(new[] { "tip_1" }));
            Assert.That(MenuStyle.Parent(MenuScreen.Tips), Is.EqualTo(MenuScreen.Guide));
            Assert.That(MenuStyle.Parent(MenuScreen.Guide), Is.EqualTo(MenuScreen.Home));
            Assert.That(MenuPainter.IsListRow(r.Ctl, 1), Is.True, "drawn inside the list's clip");
        }

        [Test]
        public void SunnysSpotlightLightsARowUntilAFingerHovers()
        {
            Rig r = WithFirstFive(out _, out _);
            int lessons = r.RowNamed(RowKind.Menu, x => x.To == MenuScreen.Lessons);
            r.Ctl.Spotlight = lessons;
            r.Frame();
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(lessons), "the menu's own hover highlight, no repaint");
            int exit = r.RowNamed(RowKind.Exit);
            r.Poke(r.Over(exit, 0.006f));   // a fingertip hovering 6 mm over Exit wins
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(exit));
            r.Ctl.Spotlight = -1;
            r.Frame();
            Assert.That(r.Ctl.HoverRow, Is.EqualTo(-1));
        }

        [Test]
        public void EveryPokeIsReportedWithItsRowIdAndWhetherItAsks()
        {
            var host = new RowHost();
            var lessons = FakeLessons.Load();
            var ctl = new MenuController(host) { Lessons = lessons, Tutorial = new FakeTutorial() };
            host.Ctl = ctl;
            ctl.SetShown(true);
            ctl.Sync();
            ctl.Relayout();
            int exit = -1;
            for (int i = 0; i < ctl.RowCount; i++) if (ctl.Row(i).Id == RowKind.Exit) exit = i;
            ctl.BeginPokes(1000);
            ctl.Fire(exit);
            ctl.EndPokes();
            Assert.That(host.Rows, Is.EqualTo(new[] { "exit" }));
            Assert.That(host.Confirming, Is.EqualTo(new[] { true }), "Exit now asks for a second poke");
            Assert.That(MenuRowIds.IdOf(ctl.Row(0)), Is.EqualTo("lessons"));
        }

        [Test]
        public void ALessonTheMenuDoesNotListNeverTakesThePanel()
        {
            var r = new Rig();
            r.Tutorial.Start("piano-ode-first");   // the first run's song: not in the library
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Home), "the menu stays where the player left it");
            r.Tutorial.Start("piano-ode");
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));
        }
    }
}
