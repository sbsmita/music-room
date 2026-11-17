// A recording's time as the player reads it (headset report 2026-11-10: "recording time should be accurate to timezone"):
// takes store UTC unix ms; Core.LocalTime shows them in the headset's zone, friendly ("Today 3:42 PM", "Yesterday 9:10 AM",
// "Nov 8, 4:05 PM", a past year "Nov 8, 2025, 4:05 PM"), with fixed offsets here (UTC, India +5:30, California -8 / -7),
// across local midnight and a DST change; the take library and the menu use it, and the menu's cached "Today" turns into
// "Yesterday" once the local day changes.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicRoom.Core;
using MusicRoom.Lessons;
using MusicRoom.Menu;
using NUnit.Framework;

namespace MusicRoom.Tests.Core
{
    public class LocalTimeTests
    {
        const long H = 3600000L, M = 60000L;

        static long Utc(int y, int mo, int d, int h, int mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        [TearDown]
        public void Reset()
        {
            LocalTime.OffsetSource = null;
            LocalTime.NowSource = null;
        }

        [Test]
        public void UtcTodayYesterdayAndOlder()
        {
            long now = Utc(2026, 11, 10, 18, 0);
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 10, 15, 42), 0, now, 0), Is.EqualTo("Today 3:42 PM"));
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 10, 0, 0), 0, now, 0), Is.EqualTo("Today 12:00 AM"), "local midnight is today");
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 9, 23, 59), 0, now, 0), Is.EqualTo("Yesterday 11:59 PM"));
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 9, 9, 10), 0, now, 0), Is.EqualTo("Yesterday 9:10 AM"));
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 8, 16, 5), 0, now, 0), Is.EqualTo("Nov 8, 4:05 PM"));
            Assert.That(LocalTime.Friendly(Utc(2026, 1, 2, 12, 0), 0, now, 0), Is.EqualTo("Jan 2, 12:00 PM"), "noon");
            Assert.That(LocalTime.Friendly(Utc(2025, 11, 8, 16, 5), 0, now, 0), Is.EqualTo("Nov 8, 2025, 4:05 PM"), "a past year says so");
        }

        [Test]
        public void ANonZeroOffsetMovesTheTimeAndTheDay()
        {
            const long india = 5 * H + 30 * M;
            // 20:00 UTC on Nov 10 is 1:30 AM on Nov 11 in India; now is 21:00 UTC (2:30 AM Nov 11 there).
            long now = Utc(2026, 11, 10, 21, 0);
            long t = Utc(2026, 11, 10, 20, 0);
            Assert.That(LocalTime.Friendly(t, india, now, india), Is.EqualTo("Today 1:30 AM"));
            Assert.That(LocalTime.Friendly(t, 0, now, 0), Is.EqualTo("Today 8:00 PM"), "the same moment in UTC");
            // 18:00 UTC on Nov 10 is 11:30 PM there: yesterday once it is past midnight in India.
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 10, 18, 0), india, now, india), Is.EqualTo("Yesterday 11:30 PM"));

            const long pst = -8 * H;
            // 7:30 UTC on Nov 10 is 11:30 PM on Nov 9 in California; now (17:00 UTC) is 9:00 AM Nov 10 there.
            long nowCa = Utc(2026, 11, 10, 17, 0);
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 10, 7, 30), pst, nowCa, pst), Is.EqualTo("Yesterday 11:30 PM"));
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 10, 7, 30), 0, nowCa, 0), Is.EqualTo("Today 7:30 AM"), "UTC would say today");
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 10, 8, 0), pst, nowCa, pst), Is.EqualTo("Today 12:00 AM"));
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 8, 0, 5), pst, nowCa, pst), Is.EqualTo("Nov 7, 4:05 PM"));
        }

        [Test]
        public void EachMomentUsesItsOwnOffsetAcrossADstChange()
        {
            // California: a take on Nov 1 at 10:00 AM PDT (-7, 17:00 UTC), read on Nov 2 at 9:00 AM PST (-8, 17:00 UTC).
            long take = Utc(2026, 11, 1, 17, 0), now = Utc(2026, 11, 2, 17, 0);
            Assert.That(LocalTime.Friendly(take, -7 * H, now, -8 * H), Is.EqualTo("Yesterday 10:00 AM"));
            LocalTime.OffsetSource = ms => ms < Utc(2026, 11, 1, 9, 0) ? -7 * H : -8 * H;   // PST from 2 AM PDT on Nov 1
            LocalTime.NowSource = () => now;
            Assert.That(LocalTime.Friendly(Utc(2026, 11, 1, 8, 0)), Is.EqualTo("Yesterday 1:00 AM"), "PDT before the change");
            Assert.That(LocalTime.Friendly(take), Is.EqualTo("Yesterday 9:00 AM"), "PST after it");
        }

        [Test]
        public void TheSourcesFeedTheLibraryAndTheMenusCacheFollowsTheDay()
        {
            const long india = 5 * H + 30 * M;
            long now = Utc(2026, 11, 10, 12, 0);   // 5:30 PM in India
            LocalTime.OffsetSource = _ => india;
            LocalTime.NowSource = () => now;
            Assert.That(LocalTime.OffsetMs(now), Is.EqualTo(india));
            long t = Utc(2026, 11, 10, 10, 12);    // 3:42 PM in India
            var lib = new TakeLibrary(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mr-localtime-" + Guid.NewGuid().ToString("N")));
            Assert.That(lib.FormatDate(t), Is.EqualTo("Today 3:42 PM"), "the library shows the headset's local time");

            var text = new MenuText();
            var info = new TakeInfo { id = "take-x", name = "Take 1 · 0:03", created = t, seconds = 3f, count = 2 };
            Assert.That(text.Date(info, lib), Is.EqualTo("Today 3:42 PM"));
            Assert.That(text.TakeInfo(info, lib), Does.StartWith("Today 3:42 PM"));
            // Past local midnight (18:31 UTC is 12:01 AM on Nov 11 there) the same take reads "Yesterday".
            now = Utc(2026, 11, 10, 18, 31);
            LocalTime.NowSource = () => now;   // also forgets the cached offset of now
            Assert.That(text.Date(info, lib), Is.EqualTo("Yesterday 3:42 PM"), "the cached Today is built again on a new day");
            Assert.That(text.TakeInfo(info, lib), Does.StartWith("Yesterday 3:42 PM"));
            Assert.That(text.Date(info, null), Is.EqualTo("Yesterday 3:42 PM"), "the menu's fallback without a library");
        }

        [Test]
        public void ThePlatformOffsetIsTheSystemsOutsideTheDevice()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long want = (long)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(now)).TotalMilliseconds;
            Assert.That(LocalTime.OffsetMs(now), Is.EqualTo(want), "the editor: TimeZoneInfo.Local (the device asks the platform's own zone)");
            Assert.That(LocalTime.Friendly(now), Does.StartWith("Today "));
        }
    }
}
