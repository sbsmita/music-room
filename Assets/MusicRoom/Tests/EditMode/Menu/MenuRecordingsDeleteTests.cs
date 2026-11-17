// Deleting recordings from the Recordings list (headset report 2026-11-10: "recordings should be deletable"): a bin beside
// each take, poked once it asks ("Delete?" in red), poked again within 3 s it deletes that take (its file and its list
// entry, through ITakeLibrary.Remove); the ask is forgotten after 3 s or on another screen; a playback of that very take
// stops, any other goes on; the bin never opens the take. The list rows show the name over the local time.
using MusicRoom.Core;
using MusicRoom.Guide;
using MusicRoom.Menu;
using NUnit.Framework;

namespace MusicRoom.Tests.Menu
{
    public class MenuRecordingsDeleteTests
    {
        static Rig WithTwo(out TakeRecord t1, out TakeRecord t2)
        {
            var r = new Rig();
            t1 = r.Takes.Add("Take 1 · 0:03", 1000, 3.2f, 2);
            t2 = r.Takes.Add("Take 2 · 0:01", 2000, 1f, 1);
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            return r;
        }

        static int Bin(Rig r, TakeRecord t) => r.RowNamed(RowKind.Trash, x => x.Take != null && x.Take.id == t.id);
        static int TakeRow(Rig r, TakeRecord t) => r.RowNamed(RowKind.Take, x => x.Take != null && x.Take.id == t.id);

        [Test]
        public void EachTakeHasABinThatAsksOnceThenDeletes()
        {
            Rig r = WithTwo(out TakeRecord t1, out TakeRecord t2);
            int row = TakeRow(r, t1), bin = Bin(r, t1);
            Assert.That(bin, Is.GreaterThan(0), "a bin beside the take");
            MenuRow tr = r.Ctl.Row(row), br = r.Ctl.Row(bin);
            Assert.That(tr.X1, Is.EqualTo(MenuStyle.TakeRowX1));
            Assert.That(br.X0, Is.EqualTo(MenuStyle.TakeRowX1 + MenuStyle.TrashGap));
            Assert.That(br.X1, Is.EqualTo(MenuStyle.RowX1));
            Assert.That(new[] { br.Y0, br.Y1 }, Is.EqualTo(new[] { tr.Y0, tr.Y1 }), "on the take's line");
            Assert.That(br.Icon, Is.EqualTo(IconKind.Delete));
            Assert.That(br.Label, Is.EqualTo(""));

            r.Tap(bin);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Recordings), "the bin never opens the take");
            Assert.That(r.Ctl.TrashArmedId, Is.EqualTo(t1.id));
            bin = Bin(r, t1);
            Assert.That(r.Ctl.Row(bin).Label, Is.EqualTo("Delete?"));
            Assert.IsTrue(r.Ctl.Row(bin).On);
            Assert.IsFalse(r.Ctl.Row(Bin(r, t2)).On, "only that take asks");
            Assert.That(r.Host.Rows[r.Host.Rows.Count - 1].RowId, Is.EqualTo("delete"), "Sunny's tip_delete listens to this id");
            Assert.IsTrue(r.Host.Rows[r.Host.Rows.Count - 1].Confirming, "the first poke asks");
            Assert.That(r.Takes.Records.Count, Is.EqualTo(2), "nothing deleted yet");
            RecordingCanvas c = r.Paint();
            Assert.That(c.Find("Delete?").Value.Color, Is.EqualTo(MenuStyle.Red));

            r.Tap(bin);
            Assert.That(r.Takes.Records.Count, Is.EqualTo(1), "the second poke deletes it");
            Assert.That(r.Takes.Records[0].id, Is.EqualTo(t2.id), "only that take");
            Assert.IsFalse(r.Host.Rows[r.Host.Rows.Count - 1].Confirming);
            Assert.That(r.Ctl.TrashArmedId, Is.Null);
            Assert.That(r.Ctl.TakeList.Count, Is.EqualTo(1));
            Assert.That(TakeRow(r, t1), Is.EqualTo(-1), "its row is gone");
            Assert.That(r.Labels(1), Is.EqualTo("Take 2 · 0:01,"));
            Assert.That(r.Ctl.HeaderNote, Is.EqualTo("1 take"));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Recordings));
        }

        [Test]
        public void TheAskEndsAfterThreeSecondsOrOnAnotherScreen()
        {
            Rig r = WithTwo(out TakeRecord t1, out TakeRecord t2);
            r.Tap(Bin(r, t1));
            Assert.That(r.Ctl.TrashArmedId, Is.EqualTo(t1.id));
            r.Frame(2800f);
            Assert.That(r.Ctl.TrashArmedId, Is.EqualTo(t1.id), "still asking under 3 s");
            r.Frame(400f);
            Assert.That(r.Ctl.TrashArmedId, Is.Null, "3 s without a second poke: a bin again");
            Assert.That(r.Ctl.Row(Bin(r, t1)).Label, Is.EqualTo(""));
            Assert.IsFalse(r.Ctl.Row(Bin(r, t1)).On);
            r.Tap(Bin(r, t1));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(2), "a poke after the timeout asks again, it does not delete");
            Assert.That(r.Ctl.TrashArmedId, Is.EqualTo(t1.id));

            // Another take's bin asks for that one instead.
            r.Tap(Bin(r, t2));
            Assert.That(r.Ctl.TrashArmedId, Is.EqualTo(t2.id));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(2));
            Assert.IsFalse(r.Ctl.Row(Bin(r, t1)).On);

            // A screen change forgets it.
            r.Ctl.Go(MenuScreen.Home);
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            Assert.That(r.Ctl.TrashArmedId, Is.Null);
            r.Tap(Bin(r, t2));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(2), "asks again after coming back");
        }

        [Test]
        public void DeletingThePlayingTakeStopsItAndAnotherTakePlaysOn()
        {
            Rig r = WithTwo(out TakeRecord t1, out TakeRecord t2);
            r.Tap(TakeRow(r, t1));
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Take));
            r.Tap(r.RowNamed(RowKind.Play));
            Assert.IsTrue(r.Recorder.External);
            Assert.That(r.Ctl.PlayingTakeId, Is.EqualTo(t1.id));
            r.Tap(0);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Recordings));
            int stops = r.Recorder.Stops;

            // Take 2 goes: take 1 keeps playing.
            r.Tap(Bin(r, t2));
            r.Tap(Bin(r, t2));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Stops, Is.EqualTo(stops), "another take's playback goes on");
            Assert.That(r.Recorder.State, Is.EqualTo(RecorderState.Playing));

            // Take 1 (playing) goes: its playback stops with it.
            r.Tap(Bin(r, t1));
            Assert.That(r.Recorder.State, Is.EqualTo(RecorderState.Playing), "asking does not stop it");
            r.Tap(Bin(r, t1));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(0));
            Assert.That(r.Recorder.Stops, Is.EqualTo(stops + 1), "the playing take is stopped");
            Assert.That(r.Recorder.State, Is.EqualTo(RecorderState.Idle));
            Assert.That(r.Ctl.PlayingTakeId, Is.Null);
            Assert.IsTrue(r.Paint().Has("No recordings yet"));
        }

        [Test]
        public void TheDeckPlayingItsOwnTakeIsNotStoppedByADelete()
        {
            Rig r = WithTwo(out TakeRecord t1, out _);
            r.Recorder.Play();   // the deck's PLAY: its own take, not a saved one (External false)
            int stops = r.Recorder.Stops;
            r.Tap(Bin(r, t1));
            r.Tap(Bin(r, t1));
            Assert.That(r.Takes.Records.Count, Is.EqualTo(1));
            Assert.That(r.Recorder.Stops, Is.EqualTo(stops));
            Assert.That(r.Recorder.State, Is.EqualTo(RecorderState.Playing));
        }

        [Test]
        public void SunnyTeachesTheBin()
        {
            GuideLines.ReloadMerged();
            Assert.That(GuideLines.TextOf("tip_recordings"), Does.Contain("bin"), "the Recordings tip shows the bin");
            Assert.That(GuideLines.TextOf("tip_delete"), Does.Contain("Delete").And.Contain("three seconds"), "said on the first poke");
            Assert.That(MenuRowIds.FeatureOf(RowKind.Trash), Is.EqualTo("recordings.delete"));
            Assert.That(MenuRowIds.Of(RowKind.Trash), Is.EqualTo(MenuRowIds.Delete), "tip_delete's trigger");
            Assert.That(MenuStyle.DeleteConfirmMs, Is.EqualTo(3000.0));
        }

        [Test]
        public void ARowShowsTheNameOverItsLocalTime()
        {
            Rig r = WithTwo(out TakeRecord t1, out _);
            RecordingCanvas c = r.Paint();
            MenuRow row = r.Ctl.Row(TakeRow(r, t1));
            RecordingCanvas.TextOp name = c.Find("Take 1 · 0:03").Value;
            Assert.That(name.Size, Is.EqualTo(MenuStyle.TakeNameSize));
            Assert.IsTrue(name.Bold);
            Assert.That(name.Y, Is.EqualTo(row.CentreY + MenuStyle.TakeNameDy));
            // The fake library's date: every date text sits under its name, dim, in the row's width.
            int dates = 0;
            foreach (RecordingCanvas.TextOp t in c.Texts)
            {
                if (t.Text != "20 Oct, 14:32") continue;
                dates++;
                Assert.That(t.Size, Is.EqualTo(MenuStyle.TakeDateSize));
                Assert.That(t.Color, Is.EqualTo(MenuStyle.Dim));
                Assert.That(t.X + t.MaxWidth, Is.LessThanOrEqualTo(MenuStyle.TakeRowX1));
            }
            Assert.That(dates, Is.EqualTo(2), "both takes show their time");
        }
    }
}
