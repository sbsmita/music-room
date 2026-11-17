// Each recording's row on the Recordings list shows its constellation (the playback show's night sky in small): made from
// the take's own events (SongStars.BuildThumb, nothing more saved), the same take the same picture, kept per take; drawn
// at the row's right end without touching the name, the local time, the bin or its "Delete?".
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuTakeStarsTests
    {
        static TakeRecord Melody(Rig r, string name, long created, int lo, bool falling = false)
        {
            TakeRecord t = r.Takes.Add(name, created, 3f, 0);
            for (int i = 0; i < 10; i++)
            {
                TakeEvent e = TakeEvent.PianoOn(lo + 2 * (falling ? 4 - i % 5 : i % 5), 0.5f + 0.04f * i); e.t = 0.25f * i; t.events.Add(e);
                if (i % 3 == 0) { TakeEvent d = TakeEvent.Drum("snare", 0.8f, 0.3f, "head", false); d.t = e.t; t.events.Add(d); }
            }
            return t;
        }

        static int TakeRow(Rig r, TakeRecord t) => r.RowNamed(RowKind.Take, x => x.Take != null && x.Take.id == t.id);

        [Test]
        public void EachTakeRowShowsItsOwnConstellation()
        {
            var r = new Rig();
            TakeRecord a = Melody(r, "Take 1", 1000, 60), b = Melody(r, "Take 2", 2000, 64, falling: true);
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            r.Frame();
            r.Frame();
            MenuRow ra = r.Ctl.Row(TakeRow(r, a)), rb = r.Ctl.Row(TakeRow(r, b));
            Assert.IsNotNull(ra.Stars, "the picture is made once the take is read");
            Assert.IsNotNull(rb.Stars);
            Assert.That(ra.Stars.Count, Is.EqualTo(14), "10 piano notes and 4 snare hits");
            Assert.That(ra.Stars.Hash, Is.EqualTo(SongStars.BuildThumb(a.events, new SongStars.Types(r.Registry)).Hash), "from the take's own events");
            Assert.That(rb.Stars.Hash, Is.Not.EqualTo(ra.Stars.Hash), "another take, another picture");
            // kept: the same object on the next layout, nothing read again
            SongStars.Thumb kept = ra.Stars;
            r.Ctl.Relayout();
            r.Frame();
            Assert.That(r.Ctl.Row(TakeRow(r, a)).Stars, Is.SameAs(kept));
            Assert.That(r.Ctl.StarsOf(new TakeInfo { id = a.id }), Is.SameAs(kept));
        }

        [Test]
        public void ThePictureSitsAtTheRowsEndAndKeepsTheTextAndTheBin()
        {
            var r = new Rig();
            TakeRecord a = Melody(r, "Take 1", 1000, 60);
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            r.Frame();
            MenuRow row = r.Ctl.Row(TakeRow(r, a));
            Assert.That(row.X1, Is.EqualTo(MenuStyle.TakeRowX1), "the row's width is unchanged");
            RecordingCanvas c = r.Paint();
            RecordingCanvas.TextOp? name = c.Find("Take 1");
            Assert.IsTrue(name.HasValue);
            float picture = row.X1 - MenuPainter.TakeStarsRight - MenuPainter.TakeStarsW;
            Assert.That(name.Value.X + name.Value.MaxWidth, Is.LessThanOrEqualTo(picture), "the name stops before the picture");
            Assert.That(name.Value.MaxWidth, Is.GreaterThan(400f), "and still has room");
            Assert.That(picture, Is.GreaterThan(row.X0 + 400f));
            Assert.That(c.Fills.Contains(MenuPainter.TakeStarsSky), Is.True, "its small night is drawn");
            Assert.That(c.Fills.Contains(SongStars.ColorOf(InstrumentKind.Piano)), Is.True, "piano stars in the piano's colour");
            Assert.That(c.Fills.Contains(SongStars.ColorOf(InstrumentKind.Drums)), Is.True, "and the kit's in its own");
            // the bin and its ask are untouched
            int bin = r.RowNamed(RowKind.Trash, x => x.Take != null && x.Take.id == a.id);
            r.Tap(bin);
            c = r.Paint();
            Assert.That(c.Find("Delete?").Value.Color, Is.EqualTo(MenuStyle.Red));
            Assert.That(r.Ctl.Row(r.RowNamed(RowKind.Trash, x => x.Take != null && x.Take.id == a.id)).X0, Is.EqualTo(MenuStyle.TakeRowX1 + MenuStyle.TrashGap));
        }

        [Test]
        public void ATakeWithoutNotesHasAnEmptyNightAndNoError()
        {
            var r = new Rig();
            TakeRecord a = Melody(r, "Take 1", 1000, 60);
            a.events.Clear();
            a.events.Add(new TakeEvent { type = "pianoOff", midi = 60 });   // no onset: no star
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            r.Frame();
            MenuRow row = r.Ctl.Row(TakeRow(r, a));
            Assert.IsNotNull(row.Stars);
            Assert.That(row.Stars.Count, Is.EqualTo(0));
            Assert.DoesNotThrow(() => r.Paint());
        }
    }
}
