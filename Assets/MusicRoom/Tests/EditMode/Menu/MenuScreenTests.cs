// Screens, navigation and row actions (spec 08 sections 1.5-1.6, ARCHITECTURE.md section 9) through the controller with
// fake services, and what the painter draws for each screen (texts, colours) through a recording canvas. Cases follow
// the menu and tutorial reference checks where they exist.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuScreenTests
    {
        static string Notes(Rig r, int from = 0)
        {
            var n = new List<string>();
            for (int i = from; i < r.Ctl.RowCount; i++) n.Add(r.Ctl.Row(i).Note);
            return string.Join(",", n);
        }

        [Test]
        public void LessonDataIsTheWholeLibrary()
        {
            var lib = FakeLessons.Load();
            Assert.That(lib.Lessons.Count, Is.EqualTo(36));   // first five: the proposed lessons
            Assert.That(lib.Count(InstrumentKind.Piano), Is.EqualTo(15));
            Assert.That(lib.Count(InstrumentKind.Drums), Is.EqualTo(12));
            Assert.That(lib.Count(InstrumentKind.Harp), Is.EqualTo(9));
            Assert.That(lib.Get("piano-scale").listNote, Is.EqualTo("15 notes"));
            Assert.That(lib.Get("drums-beat").listNote, Is.EqualTo("8 beats"));
        }

        [Test]
        public void HomeRowsReadTheServices()
        {
            var r = new Rig();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Home));
            Assert.That(r.Labels(), Is.EqualTo("Lessons,Instruments,Recordings,Play in my room,Show me around,Exit"));
            // platform.md 5.1: the Instruments row (and the header) say what is at the seat: My set's names in planner order.
            Assert.That(Notes(r), Is.EqualTo("36 courses,Piano, Drums, Harp,0 saved,Passthrough,Sunny,Close the app"));
            Assert.That(r.RowNamed(RowKind.Back), Is.EqualTo(-1), "home has no back row");
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("Music Room"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Piano, Drums, Harp"));
            Assert.That(r.Ctl.Footer, Is.EqualTo("Show your palm to hide"));

            r.Layout.Mode = LayoutMode.Single; r.Layout.Current = InstrumentKind.Harp;
            r.Guide.Present = true;
            r.Takes.Add("Take 1 · 0:03", 1000, 3f, 2);
            r.Mr.Available = false;
            r.Frame();
            Assert.That(r.Labels(), Is.EqualTo("Lessons,Instruments,Recordings,Play in my room,Hide Sunny,Exit"));
            Assert.That(Notes(r), Is.EqualTo("36 courses,Harp alone,1 saved,Not available,Stop the tour,Close the app"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Harp alone"));

            r.Takes.Available = false;
            r.Ctl.Changed();
            r.Frame();
            Assert.That(r.Ctl.Row(2).Note, Is.EqualTo("Saving off"), "menu-check savingOffOnHome");
        }

        [Test]
        public void HomeRowGeometryWithAndWithoutMixedReality()
        {
            // With Exit as the last row (since the first headset test) the spec's five rows are six: h 84, gap 10, from y 132.
            var r = new Rig();
            Assert.That(r.Ctl.RowCount, Is.EqualTo(6));
            for (int i = 0; i < 6; i++)
            {
                Assert.That(r.Ctl.Row(i).Y0, Is.EqualTo(132f + 94f * i), "six rows, h 84, gap 10, from y 132");
                Assert.That(r.Ctl.Row(i).Height, Is.EqualTo(84f));
            }
            Assert.That(r.Ctl.Row(5).Y1, Is.EqualTo(686f));
            // In MR without a Space Setup: "Music room" and "Set up my room": seven rows, h 72, gap 8, ending at 684.
            r.Mr.Active = true;
            r.Mr.Status = MrStatus.NoRooms;
            r.Frame();
            Assert.That(r.Labels(), Is.EqualTo("Lessons,Instruments,Recordings,Music room,Set up my room,Show me around,Exit"));
            Assert.That(Notes(r), Is.EqualTo("36 courses,Piano, Drums, Harp,0 saved,Virtual room,Space Setup,Sunny,Close the app"));
            for (int i = 0; i < 7; i++) Assert.That(r.Ctl.Row(i).Y0, Is.EqualTo(132f + 80f * i));
            Assert.That(r.Ctl.Row(6).Y1, Is.EqualTo(684f));
            // A loaded room: six rows again.
            r.Mr.Status = MrStatus.Loaded;
            r.Frame();
            Assert.That(r.Ctl.RowCount, Is.EqualTo(6));
            Assert.That(r.Ctl.Row(3).Label, Is.EqualTo("Music room"));
            // No MR service at all: the spec's four rows and Exit (h 100, gap 12).
            var w = new Rig(withMr: false);
            Assert.That(w.Labels(), Is.EqualTo("Lessons,Instruments,Recordings,Show me around,Exit"));
            for (int i = 0; i < 5; i++) Assert.That(w.Ctl.Row(i).Y0, Is.EqualTo(132f + 112f * i));
            // Every home page fits above the footer (y 724).
            for (int count = 4; count <= 8; count++)
            {
                MenuController.HomeGeometry(count, out float h, out float gap);
                float end = 132f + count * h + (count - 1) * gap;
                Assert.That(end, Is.InRange(670f, 690f), count + " rows end at " + end);
            }
        }

        [Test]
        public void RoomAndSetupRowsCallMixedReality()
        {
            var r = new Rig();
            r.Tap(r.RowNamed(RowKind.Room));
            Assert.That(r.Mr.Enters, Is.EqualTo(1), "Play in my room enters MR");
            Assert.IsTrue(r.Ctl.Shown, "the panel stays open");
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Room)).Label, Is.EqualTo("Music room"));
            r.Mr.Status = MrStatus.NoRooms;
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Setup));
            Assert.That(r.Mr.Setups, Is.EqualTo(1));
            r.Tap(r.RowNamed(RowKind.Room));
            Assert.That(r.Mr.Exits, Is.EqualTo(1), "Music room goes back to the virtual room");
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Room)).Label, Is.EqualTo("Play in my room"));
            r.Mr.Available = false;
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Room));
            Assert.That(r.Mr.Enters, Is.EqualTo(1), "no passthrough here: nothing to enter");
        }

        [Test]
        public void TheGuideRowHidesSunnyOrRestartsTheTour()
        {
            var r = new Rig();
            r.Tap(r.RowNamed(RowKind.Guide));
            Assert.That(r.Host.Hides, Is.EqualTo(1), "Show me around puts the panel away first");
            Assert.That(r.Guide.Starts, Is.EqualTo(1));
            Assert.IsFalse(r.Ctl.Shown);
            r.Ctl.SetShown(true);
            r.Frame();
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Guide)).Label, Is.EqualTo("Hide Sunny"));
            r.Tap(r.RowNamed(RowKind.Guide));
            Assert.That(r.Guide.Dismisses, Is.EqualTo(1));
            Assert.IsTrue(r.Ctl.Shown, "Hide Sunny keeps the panel open");
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Guide)).Label, Is.EqualTo("Show me around"));
        }

        [Test]
        public void EverySubScreenHasTheBackRowAcrossTheHeader()
        {
            var r = new Rig();
            r.Takes.Add("Take 1 · 0:02", 1000, 2f, 1);
            r.Frame();
            foreach (var s in new[] { MenuScreen.Lessons, MenuScreen.Courses, MenuScreen.Instruments, MenuScreen.Recordings })
            {
                r.Ctl.Go(s);
                r.Frame();
                var b = r.Ctl.Row(0);
                Assert.That(b.Id, Is.EqualTo(RowKind.Back), s.ToString());
                Assert.That(new[] { b.X0, b.Y0, b.X1, b.Y1 }, Is.EqualTo(new[] { 0f, 0f, 1024f, 110f }));
                Assert.IsTrue(r.Ctl.HeaderBack);
                Assert.That(r.Ctl.HeaderTitle, Does.StartWith("‹  "));
            }
            r.Tap(1);   // the take
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Take));
            Assert.That(r.Ctl.Row(0).Id, Is.EqualTo(RowKind.Back));
            r.Tutorial.Start("piano-scale");
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson), "any lesson start shows the lesson screen");
            Assert.That(r.Ctl.Row(0).Id, Is.EqualTo(RowKind.Back));
        }

        [Test]
        public void PokingNavigatesAndBackGoesToTheParent()
        {
            var r = new Rig();
            r.Tap(0);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lessons));
            r.Tap(0);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Home));
            r.Tap(1);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Instruments));
            r.Tap(0);
            r.Tap(2);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Recordings));
            r.Tap(0);
            Assert.That(r.Host.Screens, Is.EqualTo(new[] { MenuScreen.Lessons, MenuScreen.Home, MenuScreen.Instruments, MenuScreen.Home, MenuScreen.Recordings, MenuScreen.Home }));
            Assert.That(MenuStyle.Parent(MenuScreen.Courses), Is.EqualTo(MenuScreen.Lessons));
            Assert.That(MenuStyle.Parent(MenuScreen.Lesson), Is.EqualTo(MenuScreen.Courses));
            Assert.That(MenuStyle.Parent(MenuScreen.Take), Is.EqualTo(MenuScreen.Recordings));
            Assert.That(MenuStyle.Parent(MenuScreen.Instruments), Is.EqualTo(MenuScreen.Home));
            // Go(same) only redraws: no event.
            int before = r.Host.Screens.Count;
            r.Ctl.Go(MenuScreen.Home);
            Assert.That(r.Host.Screens.Count, Is.EqualTo(before));
        }

        [Test]
        public void LessonsScreenListsTheInstruments()
        {
            // platform.md 5.3: the Instruments grid's tiles for the instruments with lessons (this library: the classic
            // three), in carousel order, each with "N lessons".
            var r = new Rig();
            r.Layout.Mode = LayoutMode.Single; r.Layout.Current = InstrumentKind.Drums;
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Piano,Harp,Drums*"), "on = single mode with that instrument current");
            Assert.That(Notes(r, 1), Is.EqualTo("15 lessons,9 lessons,12 lessons"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Poke an instrument"));
            r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Harp));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            Assert.That(r.Ctl.Instrument, Is.EqualTo(InstrumentKind.Harp));
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Harp"));
            Assert.That(r.Layout.Sets, Is.EqualTo(0), "picking the instrument's courses does not change the layout");
        }

        [Test]
        public void CourseListsScrollWithTheReferenceGeometry()
        {
            var r = new Rig();
            var want = new Dictionary<InstrumentKind, float> { { InstrumentKind.Piano, 1320f }, { InstrumentKind.Drums, 930f }, { InstrumentKind.Harp, 540f } };
            foreach (var kv in want)
            {
                r.Ctl.Go(MenuScreen.Lessons);
                r.Frame();
                r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == kv.Key));
                Assert.That(r.Ctl.MaxScroll, Is.EqualTo(kv.Value), kv.Key + " maxScroll = n x 130 - 630");
                Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Courses  ·  drag to scroll"));
                for (int i = 1; i < r.Ctl.RowCount; i++)
                {
                    var row = r.Ctl.Row(i);
                    Assert.IsTrue(row.Scrolls);
                    Assert.That(row.Y0, Is.GreaterThanOrEqualTo(130f));
                    Assert.That(row.Y1, Is.LessThanOrEqualTo(750f));
                    Assert.That(row.Height, Is.GreaterThanOrEqualTo(30f), "under 30 px showing, a row is not laid out");
                }
                Assert.That(r.Ctl.RowCount, Is.LessThan(1 + r.Lessons.Count(kv.Key)));
            }
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Piano));
            Assert.That(r.Ctl.Row(1).Label + " / " + r.Ctl.Row(1).Note, Is.EqualTo("Ode to Joy / 30 notes"));   // first five: the new order, Ode to Joy first
            Assert.That(new[] { r.Ctl.Row(1).Y0, r.Ctl.Row(1).Y1, r.Ctl.Row(2).Y0 }, Is.EqualTo(new[] { 130f, 250f, 260f }), "pitch 130, rows 120 high");
            // The thumb: top 130 + scroll/(620+max) x 620, height 620^2/(620+max).
            r.Ctl.SetScroll(530f);
            var c = r.Paint();
            Assert.That(c.Clips, Is.EqualTo(1), "the list is clipped to its viewport");
            Assert.That(r.Ctl.Row(1).Y0, Is.EqualTo(130f), "a partly hidden row is clipped to the viewport");
        }

        [Test]
        public void ACourseStartsTheLessonAndTheTutorialBringsTheInstrument()
        {
            // platform.md 7.3: Tutorial.Start brings the lesson's instrument itself (Bring(kind, Lesson)); the menu no
            // longer sets the layout first.
            var r = new Rig();
            r.Layout.Mode = LayoutMode.Single; r.Layout.Current = InstrumentKind.Harp;
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            r.Tap(r.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Drums));
            r.Tap(r.RowNamed(RowKind.Course, x => x.Lesson.id == "drums-beat"));
            Assert.That(r.Layout.Sets, Is.EqualTo(0), "no Set(single, drums) from the menu");
            Assert.That(r.Layout.Brings, Is.Empty, "nor a Bring: the tutorial's");
            Assert.That(r.Tutorial.Started, Is.EqualTo(new[] { "drums-beat" }));
            Assert.That(r.Host.LessonsShown, Is.EqualTo(new[] { InstrumentKind.Drums }), "the module may move the panel to its panel home");
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Basic beat"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Drums"));
            // In the full room the layout is left alone.
            var a = new Rig();
            a.Ctl.Go(MenuScreen.Lessons);
            a.Frame();
            a.Tap(a.RowNamed(RowKind.Inst, x => x.Inst == InstrumentKind.Piano));
            a.Tap(1);
            Assert.That(a.Layout.Sets, Is.EqualTo(0));
            Assert.That(a.Tutorial.Started, Is.EqualTo(new[] { "piano-ode" }));   // first five: Ode to Joy is the first piano lesson
        }

        [Test]
        public void TheLessonScreenFollowsTheTutorial()
        {
            var r = new Rig();
            r.Tutorial.EventsTotal = 15;
            r.Tutorial.Start("piano-scale");
            r.Tutorial.Countdown = 2.4f;
            r.Tutorial.Version++;
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));
            Assert.That(r.Ctl.Instrument, Is.EqualTo(InstrumentKind.Piano));
            var c = r.Paint();
            Assert.That(c.Find("3").Value.Size, Is.EqualTo(220f), "countdown digit max(1, ceil(2.4))");
            Assert.That(c.Find("3").Value.Color, Is.EqualTo(MenuStyle.Cyan));
            Assert.IsTrue(c.Has("Get ready"));
            Assert.IsTrue(c.Has("0 of 15"));
            Assert.That(c.Find("Mistakes: 0").Value.Color, Is.EqualTo(MenuStyle.Dim));
            Assert.That(r.Labels(1), Is.EqualTo("Stop"));
            Assert.That(r.Ctl.Row(1).Y0, Is.EqualTo(640f));

            r.Tutorial.State = TutorialState.Waiting;
            r.Tutorial.Hint = "Play C4";
            r.Tutorial.EventsDone = 3;
            r.Tutorial.Summary = new TutorialSummary { Mistakes = 1 };
            r.Tutorial.Version++;
            r.Frame();
            c = r.Paint();
            var hint = c.Find("Play C4").Value;
            Assert.That(hint.Size, Is.EqualTo(120f), "a short hint at 120 px");
            Assert.That(hint.Color, Is.EqualTo(MenuStyle.White), "white while waiting");
            Assert.That(c.Find("Mistakes: 1").Value.Color, Is.EqualTo(MenuStyle.Red));
            Assert.IsTrue(c.Has("3 of 15"));
            Assert.That(r.Ctl.ProgressFrac, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(c.Find("Stop").Value.Color, Is.EqualTo(MenuStyle.Red));

            r.Tutorial.State = TutorialState.Playing;
            r.Tutorial.Hint = "Play C4 + E4 + G4 + C5";
            r.Tutorial.Version++;
            r.Frame();
            c = r.Paint();
            Assert.That(c.Find("Play C4 + E4 + G4 + C5").Value.Size, Is.EqualTo(80f), "longer than 14 chars: 80 px");
            Assert.That(c.Find("Play C4 + E4 + G4 + C5").Value.Color, Is.EqualTo(MenuStyle.Ink));

            r.Tutorial.State = TutorialState.Done;
            r.Tutorial.EventsDone = 15;
            r.Tutorial.Summary = new TutorialSummary { Notes = 15, Mistakes = 1, Seconds = 12.34f };
            r.Tutorial.Version++;
            r.Frame();
            c = r.Paint();
            Assert.That(c.Find("Lesson complete!").Value.Color, Is.EqualTo(MenuStyle.Green));
            Assert.IsTrue(c.Has("15 notes  ·  1 mistake  ·  12.3 s"));
            Assert.That(r.Labels(1), Is.EqualTo("Replay,Courses"));
            Assert.That(c.Find("Replay").Value.Color, Is.EqualTo(MenuStyle.Green));
            r.Tap(r.RowNamed(RowKind.Replay));
            Assert.That(r.Tutorial.Started, Is.EqualTo(new[] { "piano-scale", "piano-scale" }), "Replay starts the lesson again");
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));

            // Back from the lesson stops it and shows the courses.
            r.Tap(0);
            Assert.That(r.Tutorial.Stops, Is.EqualTo(1));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            // A lesson screen whose tutorial went idle (its instrument left) falls back to the courses.
            r.Tutorial.Start("piano-twinkle");
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Lesson));
            r.Tutorial.Stop();
            r.Frame();
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            Assert.That(r.Host.Screens[r.Host.Screens.Count - 1], Is.EqualTo(MenuScreen.Courses), "and says so (MenuEvent)");
        }

        [Test]
        public void StopAndCoursesRowsStopTheLesson()
        {
            var r = new Rig();
            r.Tutorial.Start("harp-arpeggio");
            r.Frame();
            Assert.That(r.Ctl.Instrument, Is.EqualTo(InstrumentKind.Harp), "syncTo picks the lesson's instrument");
            r.Tap(r.RowNamed(RowKind.Stop));
            Assert.That(r.Tutorial.Stops, Is.EqualTo(1));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Courses));
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Harp"));
        }

        [Test]
        public void InstrumentsScreenIsTheGridWithTheModePairAndMove()
        {
            // platform.md 5.1 (replacing the spec's five rows, 12.3): seven tiles in carousel order, then My set | One alone
            // and Move (the "Move instruments" setting, since 2026-11-10 in place of Lock). No sound switch: the classic
            // sound is the only one since 2026-11-10.
            var r = new Rig(withArrange: true);
            r.Host.KeepShown = true;
            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Piano*,Harp*,Singing bowls,Handpan,Marimba,Hand drums,Drums*,My set*,One alone,Move"),
                "the tiles in carousel order (* = at the seat), then the bottom row (* = the mode)");
            var moveRow = r.Ctl.Row(r.RowNamed(RowKind.Arrange));
            Assert.That(moveRow.Note, Is.EqualTo("Off: they stay put"));
            Assert.That(moveRow.Y1, Is.LessThanOrEqualTo(700f));
            Assert.That(moveRow.Icon, Is.EqualTo(IconKind.Move));
            Assert.IsFalse(moveRow.On);
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Piano, Drums, Harp"));
            Assert.That(r.Ctl.Footer, Is.EqualTo("Poke one to bring it, or a ticked one to send it back"));

            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.Single));
            Assert.That(r.Layout.Mode, Is.EqualTo(LayoutMode.Single));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Piano), "One alone keeps the current instrument (nothing is faced)");
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Piano alone"));
            Assert.IsTrue(r.Ctl.Row(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.Single)).On);
            Assert.That(r.Ctl.Footer, Is.EqualTo("Sweep a hand sideways to turn the page"));

            r.Tap(r.RowNamed(RowKind.Arrange));
            Assert.IsTrue(r.Arrange.Arranging, "Move instruments on");
            Assert.That(r.Arrange.Begins, Is.EqualTo(1));
            Assert.That(r.Host.Hides, Is.EqualTo(1), "the panel goes away so the room is clear");
            moveRow = r.Ctl.Row(r.RowNamed(RowKind.Arrange));
            Assert.That(moveRow.Note, Is.EqualTo("On: pinch to move"));
            Assert.IsTrue(moveRow.On);
            r.Tap(r.RowNamed(RowKind.Layout, x => x.Mode == LayoutMode.All));
            Assert.That(r.Layout.Mode, Is.EqualTo(LayoutMode.All));
            Assert.That(r.Layout.Current, Is.EqualTo(InstrumentKind.Piano), "My set keeps the current instrument");
            r.Tap(r.RowNamed(RowKind.Arrange));
            Assert.IsFalse(r.Arrange.Arranging, "the Move button switches it back off");
            Assert.That(r.Arrange.Ends, Is.EqualTo(1));
            Assert.That(r.Host.Hides, Is.EqualTo(1), "turning it off keeps the menu open");

            // Without the arrange service there is no Move button (and never a Lock button).
            var bare = new Rig();
            bare.Ctl.Go(MenuScreen.Instruments);
            bare.Frame();
            Assert.That(bare.Labels(1), Does.EndWith(",My set*,One alone"));
        }

        [Test]
        public void RecordingsListTakesAndTheTakeScreenPlaysStopsAndDeletes()
        {
            var r = new Rig();
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            var c = r.Paint();
            Assert.IsTrue(c.Has("No recordings yet"));
            Assert.IsTrue(c.Has("Press REC on the tape deck; each take is kept on this device."));
            Assert.IsTrue(c.Has("Nothing leaves the headset."));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("0 takes"));

            var t1 = r.Takes.Add("Take 1 · 0:03", 1000, 3.2f, 2);
            r.Takes.Add("Take 2 · 0:01", 2000, 1f, 1);
            r.Frame();
            Assert.That(r.Labels(1), Is.EqualTo("Take 2 · 0:01,Take 1 · 0:03,,"), "newest first, then each one's bin");
            Assert.That(r.Ctl.Row(1).Note, Is.EqualTo("20 Oct, 14:32"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("2 takes"));

            r.Tap(2);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Take));
            Assert.That(r.Ctl.Take.id, Is.EqualTo(t1.id));
            Assert.That(r.Ctl.TakeRecord, Is.SameAs(t1), "loaded");
            var ids = new List<RowKind>();
            for (int i = 0; i < r.Ctl.RowCount; i++) ids.Add(r.Ctl.Row(i).Id);
            Assert.That(ids, Is.EqualTo(new[] { RowKind.Back, RowKind.Play, RowKind.TStop, RowKind.Delete, RowKind.Paint }));
            // Keep painting (the room's wall paint) under the three: Off by default; a poke switches it and saves it.
            MenuRow keep = r.Ctl.Row(r.RowNamed(RowKind.Paint));
            Assert.That(keep.Label, Is.EqualTo(MenuStyle.KeepPainting));
            Assert.That(keep.Note, Is.EqualTo(MenuStyle.KeepPaintingOff));
            Assert.IsFalse(keep.On);
            Assert.That(new[] { keep.Y0, keep.Y1 }, Is.EqualTo(new[] { MenuStyle.KeepTop, MenuStyle.KeepBottom }));
            r.Tap(r.RowNamed(RowKind.Paint));
            Assert.IsTrue(r.Settings.KeepPaintings);
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Paint)).Note, Is.EqualTo(MenuStyle.KeepPaintingOn));
            Assert.IsTrue(r.Ctl.Row(r.RowNamed(RowKind.Paint)).On);
            r.Tap(r.RowNamed(RowKind.Paint));
            Assert.IsFalse(r.Settings.KeepPaintings);
            Assert.That(r.Ctl.HeaderTitle, Is.EqualTo("‹  Take 1 · 0:03"));
            Assert.That(r.Ctl.TakeLine, Is.EqualTo("20 Oct, 14:32  ·  0:03  ·  2 events"));
            c = r.Paint();
            Assert.That(c.Find("Play").Value.Color, Is.EqualTo(MenuStyle.Green));
            Assert.That(c.Find("Delete").Value.Color, Is.EqualTo(MenuStyle.Red));

            r.Tap(r.RowNamed(RowKind.Play));
            Assert.That(r.Recorder.Played, Is.SameAs(t1.events), "Play goes through Recorder.PlayEvents");
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Play)).Label, Is.EqualTo("Playing…"));
            Assert.IsTrue(r.Ctl.Row(r.RowNamed(RowKind.Play)).On);
            r.Tap(r.RowNamed(RowKind.TStop));
            Assert.That(r.Recorder.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Play)).Label, Is.EqualTo("Play"));

            r.Tap(r.RowNamed(RowKind.Delete));
            Assert.IsTrue(r.Ctl.Confirm, "the first poke asks");
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Delete)).Label, Is.EqualTo("Delete, sure?"));
            Assert.That(r.Ctl.Footer, Is.EqualTo("Poke Delete again to remove it from this device"));
            Assert.That(r.Ctl.FooterColor, Is.EqualTo(MenuStyle.Red));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(2));
            r.Tap(0);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Recordings));
            Assert.IsFalse(r.Ctl.Confirm, "back cancels the delete");
            Assert.That(r.Takes.Records.Count, Is.EqualTo(2));

            r.Tap(2);
            r.Tap(r.RowNamed(RowKind.Play));
            r.Tap(r.RowNamed(RowKind.Delete));
            r.Tap(r.RowNamed(RowKind.Delete));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(1), "the second poke removes it");
            Assert.That(r.Recorder.Stops, Is.GreaterThanOrEqualTo(2), "a playing saved take is stopped first");
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Recordings));
            Assert.That(r.Labels(1), Is.EqualTo("Take 2 · 0:01,"));
        }

        [Test]
        public void ALongRecordingListScrollsAndSavingOffShowsWhy()
        {
            var r = new Rig();
            for (int i = 0; i < 8; i++) r.Takes.Add("Take " + (i + 1), 1000 + i, 2f, 1);
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            Assert.That(r.Ctl.MaxScroll, Is.EqualTo(8 * 130f - 630f));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("8 takes  ·  drag to scroll"));
            Assert.That(r.Ctl.RowCount, Is.LessThan(1 + 2 * 8), "only the visible takes (and their bins) are rows");
            r.Takes.Available = false;
            r.Ctl.Changed();
            r.Frame();
            var c = r.Paint();
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("Saving off"));
            Assert.That(r.Ctl.RowCount, Is.EqualTo(1), "nothing to list without storage");
            Assert.IsTrue(c.Has("Saving is off"));
            Assert.IsTrue(c.Has("This device gives the room no storage, so takes stay on the tape deck only."));
            Assert.That(c.Find("This device gives the room no storage, so takes stay on the tape deck only.").Value.Fit, Is.EqualTo(TextFit.Shrink));
        }

        [Test]
        public void ThePainterDrawsTheHeaderAccentAndFooterPerScreen()
        {
            var r = new Rig();
            var c = r.Paint();
            var title = c.Find("Music Room").Value;
            Assert.That(title.Color, Is.EqualTo(MenuStyle.Ink), "home title in ink, no chevron");
            Assert.That(title.Size, Is.EqualTo(52f));
            Assert.That(title.X, Is.EqualTo(48f));
            Assert.That(title.Y, Is.EqualTo(57f));
            Assert.That(c.Find("Piano, Drums, Harp").Value.Align, Is.EqualTo(TextAlign.Right));
            Assert.That(c.Find("Show your palm to hide").Value.Y, Is.EqualTo(724f));
            Assert.That(c.Find("Show your palm to hide").Value.Size, Is.EqualTo(38f));
            Assert.That(c.Fills, Does.Contain(MenuStyle.Cyan), "cyan accent bar on home");
            Assert.That(c.Find("Lessons").Value.Size, Is.LessThanOrEqualTo(64f));
            Assert.That(c.Find("36 courses").Value.Size, Is.EqualTo(42f));

            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            c = r.Paint();
            Assert.That(c.Fills, Does.Contain(MenuStyle.Amber), "amber on the instruments screen");
            var sub = c.Find("‹  Instruments").Value;
            Assert.That(sub.Color, Is.EqualTo(MenuStyle.Cyan), "a sub-screen title is the cyan back row");
            Assert.IsFalse(c.Has("Lock"), "no Lock button any more (Move instruments is the one setting)");

            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            c = r.Paint();
            Assert.That(c.Fills, Does.Contain(MenuStyle.Rose), "rose on recordings");
        }

        [Test]
        public void HomeLabelsShrinkByTwoToFitBesideTheirNote()
        {
            // A full-height home row (h 100, the spec's five rows): the spec's rule.
            var r = new Rig(withMr: false);
            var row = r.Ctl.Row(0);
            Assert.That(row.Height, Is.EqualTo(100f));
            var c = new RecordingCanvas();
            // avail = 984 - (40 + 34 + 72 + 36) - 330 = 472 px; the recording canvas measures 0.5 em per char.
            row.Label = new string('x', 14);   // 448 px at 64: fits
            Assert.That(MenuPainter.HomeLabelSize(c, row), Is.EqualTo(64f));
            row.Label = new string('x', 20);   // 640 at 64; 460 at 46
            Assert.That(MenuPainter.HomeLabelSize(c, row), Is.EqualTo(46f));
            row.Label = new string('x', 40);
            Assert.That(MenuPainter.HomeLabelSize(c, row), Is.EqualTo(44f), "never below 44");
            // The denser pages (7 / 8 rows since Move instruments and Exit) start below the row height: 72 -> 56, 62 -> 48.
            var dense = new MenuRow { Id = RowKind.Menu, X0 = 40f, Y0 = 132f, X1 = 984f, Y1 = 204f, Label = "Lessons", Note = "29 courses" };
            Assert.That(MenuPainter.HomeLabelSize(c, dense), Is.EqualTo(56f));
            dense.Y1 = 194f;
            Assert.That(MenuPainter.HomeLabelSize(c, dense), Is.EqualTo(48f));
            Assert.That(MenuPainter.NoteSize(dense), Is.EqualTo(37f), "the note shrinks with the row");
        }

        [Test]
        public void AnIdleFrameDoesNotRelayoutAndAChangeDoes()
        {
            var r = new Rig();
            r.Frame();
            int n = r.Ctl.Relayouts;
            for (int i = 0; i < 10; i++) r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n), "nothing changed: no relayout");
            r.Recorder.State = RecorderState.Recording;
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n), "the recorder only matters on the take / recordings screens");
            r.Guide.Present = true;
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 1), "Sunny's presence relayouts home");
            r.Layout.Version++;
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 2), "a layout change relayouts");
            r.Tutorial.Version++;
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 3), "a tutorial version change relayouts");
            // Hidden: changes wait until it is shown again.
            r.Ctl.SetShown(false);
            r.Layout.Version++;
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 3));
            r.Ctl.SetShown(true);
            r.Frame();
            Assert.That(r.Ctl.Relayouts, Is.EqualTo(n + 4));
        }

        [Test]
        public void TextCacheReusesStrings()
        {
            var t = new MenuText();
            Assert.That(t.Progress(3, 15), Is.EqualTo("3 of 15"));
            Assert.That(t.Progress(3, 15), Is.SameAs(t.Progress(3, 15)));
            Assert.That(t.Mistakes(2), Is.EqualTo("Mistakes: 2"));
            Assert.That(t.Takes(1), Is.EqualTo("1 take"));
            Assert.That(t.Takes(3), Is.EqualTo("3 takes"));
            Assert.That(t.Summary(15, 0, 9.96f), Is.EqualTo("15 notes  ·  0 mistakes  ·  10.0 s"));
            Assert.That(t.Prefixed("Lessons"), Is.EqualTo("‹  Lessons"));
            Assert.That(MenuText.FormatLength(59.6), Is.EqualTo("1:00"), "round first: never 0:60");
            Assert.That(MenuText.FormatLength(3.2), Is.EqualTo("0:03"));
        }
    }
}
