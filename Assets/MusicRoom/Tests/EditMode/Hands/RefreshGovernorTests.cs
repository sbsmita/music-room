// E5 (experience.md section 7): the 90 Hz quality governor. The three reference scenarios are the experience lab's
// experience.vectors.json refreshGovernor (20 s at the current Hz): steady 8 ms stays; 9.5 + U[0,1) ms (mulberry32(7))
// has bad windows at 12.011 and 14.022 s and switches at 14.022 s (p95 bin 10.625 ms); 8 ms with Bernoulli(3/90) drops
// from 14 s has a bad window at 16.033 (7 dropped), not at 18.044 (2 of 181 is under the 2 % limit), and switches at the
// first two bad windows in a row (run for 40 s). In the app a switch takes the
// ladder from 90 Hz to 72 Hz; the foveation level never changes (it is fixed for the session). The rest is the ladder
// itself (never up, thermal drop, resume retry, settling, gaps and focus).
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;

namespace MusicRoom.Tests.Hands
{
    public class RefreshGovernorTests
    {
        struct Window { public double T, P95; public int Dropped, Frames; public bool Bad; }

        /// <summary>The reference loop: for (t = 0; t &lt; seconds; t += 1 / hz) hz = frame(1 / hz, gpu(t), drop(t)).</summary>
        static (System.Collections.Generic.List<Window> windows, double switchAt) Run(RefreshGovernor g, System.Func<double, double> gpu,
            System.Func<double, int> drop, double seconds = 20.0)
        {
            var windows = new System.Collections.Generic.List<Window>();
            double switchAt = -1;
            double hz = g.Hz;
            for (double t = 0; t < seconds; t += 1.0 / hz)
            {
                int before = g.Windows;
                bool changed = g.Frame(1.0 / hz, gpu(t), drop(t));
                if (g.Windows != before)
                    windows.Add(new Window { T = g.LastWindowAt, P95 = g.LastP95, Dropped = g.LastDropped, Frames = g.LastFrames, Bad = g.LastBad });
                if (changed && switchAt < 0) switchAt = g.T;
                hz = g.Hz;
            }
            return (windows, switchAt);
        }

        [Test]
        public void ASteadyEightMillisecondsStaysAtNinetyHertz()
        {
            var g = new RefreshGovernor();
            Assert.That(g.Start(true), Is.EqualTo(QualityRung.Hz90));
            var (w, sw) = Run(g, t => 8.0, t => 0);
            Assert.That(sw, Is.LessThan(0), "no switch");
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz90));
            Assert.That(w.Count, Is.EqualTo(4));
            double[] at = { 12.011, 14.022, 16.033, 18.044 };
            for (int i = 0; i < 4; i++)
            {
                Assert.That(w[i].T, Is.EqualTo(at[i]).Within(6e-4), $"window {i}");
                Assert.That(w[i].P95, Is.EqualTo(8.125).Within(1e-9));
                Assert.That(w[i].Frames, Is.EqualTo(181));
                Assert.IsFalse(w[i].Bad);
            }
        }

        [Test]
        public void AGpuOverNinetyPercentOfTheBudgetStepsOneRungDownAfterTwoBadWindows()
        {
            var g = new RefreshGovernor();
            g.Start(true);
            var rnd = new Mulberry32(7);
            var (w, sw) = Run(g, t => 9.5 + rnd.Next(), t => 0);
            Assert.That(w[0].T, Is.EqualTo(12.011).Within(6e-4));
            Assert.That(w[1].T, Is.EqualTo(14.022).Within(6e-4));
            Assert.That(w[0].P95, Is.EqualTo(10.625).Within(1e-9), "the bin over 10.0 ms (90 % of 11.1)");
            Assert.That(w[1].P95, Is.EqualTo(10.625).Within(1e-9));
            Assert.IsTrue(w[0].Bad && w[1].Bad);
            Assert.That(sw, Is.EqualTo(14.022).Within(6e-4), "the switch at the second bad window");
            Assert.That(g.Previous, Is.EqualTo(QualityRung.Hz90));
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72), "one rung down: 72 Hz");
            Assert.That(g.Hz, Is.EqualTo(72f));
            Assert.That(g.Foveation, Is.EqualTo(RefreshGovernor.SessionFoveation), "the foveation level never moves");
        }

        [Test]
        public void DroppedFramesOverOnePercentAreBadToo()
        {
            var g = new RefreshGovernor();
            g.Start(true);
            var rnd = new Mulberry32(7);
            var (w, sw) = Run(g, t => 8.0, t => t > 14 && rnd.Next() < 3.0 / 90.0 ? 1 : 0, 40.0);
            Assert.That(w[1].T, Is.EqualTo(14.022).Within(6e-4));
            Assert.That(w[1].Dropped, Is.EqualTo(1));
            Assert.IsFalse(w[1].Bad, "1 of 181 is under 2 %");
            Assert.That(w[2].T, Is.EqualTo(16.033).Within(6e-4));
            Assert.That(w[2].Dropped, Is.EqualTo(7));
            Assert.IsTrue(w[2].Bad);
            Assert.That(w[3].T, Is.EqualTo(18.044).Within(6e-4));
            Assert.That(w[3].Dropped, Is.EqualTo(2));
            Assert.IsFalse(w[3].Bad, "2 of 181 is under 2 %: a blip, not a trend");
            // Every window: bad exactly when more than 2 % dropped; the switch is the second bad window in a row.
            double expected = -1;
            for (int i = 0; i < w.Count; i++)
            {
                Assert.That(w[i].Bad, Is.EqualTo(w[i].Dropped > 0.02 * w[i].Frames), $"window {i}");
                if (expected < 0 && i > 0 && w[i].Bad && w[i - 1].Bad) expected = w[i].T;
            }
            Assert.That(expected, Is.GreaterThan(0), "a 3.3 % drop rate still steps down");
            Assert.That(sw, Is.EqualTo(expected).Within(6e-4));
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72));
        }

        [Test]
        public void TheLadderNeverStepsUpAndTheFoveationNeverChanges()
        {
            var g = new RefreshGovernor();
            g.Start(true);
            var seen = new System.Collections.Generic.List<QualityRung> { g.Rung };
            float fov = g.Foveation;
            double hz = g.Hz;
            // Always over budget: 90 -> 72 Hz after two bad windows; then it stays.
            for (double t = 0; t < 120; t += 1.0 / hz)
            {
                if (g.Frame(1.0 / hz, 16.0, 0)) seen.Add(g.Rung);
                Assert.That(g.Foveation, Is.EqualTo(fov), "the foveation level is fixed for the session");
                hz = g.Hz;
            }
            Assert.That(seen, Is.EqualTo(new[] { QualityRung.Hz90, QualityRung.Hz72 }));
            Assert.IsTrue(g.IsLastRung);
            // Then light frames for a minute: it never climbs back within the session.
            for (double t = 0; t < 60; t += 1.0 / 72) Assert.IsFalse(g.Frame(1.0 / 72, 2.0, 0));
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72));
            Assert.That(RefreshGovernor.HzOf(QualityRung.Hz72), Is.EqualTo(72f));
        }

        [Test]
        public void WithoutNinetyHertzItStartsAtSeventyTwoAndAThermalDropGoesThere()
        {
            var g = new RefreshGovernor();
            Assert.That(g.Start(false), Is.EqualTo(QualityRung.Hz72));
            Assert.IsFalse(g.SystemDroppedRefresh(), "already at 72 Hz");

            g.Start(true);
            Assert.IsTrue(g.SystemDroppedRefresh(), "the headset forced 72 Hz (thermal)");
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72));
        }

        [Test]
        public void EventsAreNotJudgedForThreeSecondsAndAResumeRetriesNinetyHertzOnce()
        {
            var g = new RefreshGovernor();
            g.Start(true);
            for (int i = 0; i < 90 * 11; i++) g.Frame(1.0 / 90, 8.0, 0);   // past the 10 s warm-up
            g.Settle();                                                     // mixed reality entered
            int windows = g.Windows;
            for (int i = 0; i < 90 * 3 - 2; i++) g.Frame(1.0 / 90, 16.0, 5);
            Assert.That(g.Windows, Is.EqualTo(windows), "nothing is judged in the 3 s after an event");

            // Down to 72 Hz, then a pause: 10 s after the resume 90 Hz is tried again, once.
            for (int i = 0; i < 90 * 6; i++) g.Frame(1.0 / 90, 16.0, 0);
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72));
            g.Resumed();
            bool back = false;
            for (int i = 0; i < 72 * 14; i++) back |= g.Frame(1.0 / 72, 8.0, 0) && g.Rung == QualityRung.Hz90;
            Assert.IsTrue(back, "retried 90 Hz after the resume");
            for (int i = 0; i < 90 * 8; i++) g.Frame(1.0 / 90, 16.0, 0);
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72), "over budget again: down");
            g.Resumed();
            for (int i = 0; i < 72 * 16; i++) g.Frame(1.0 / 72, 8.0, 0);
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz72), "the retry happens once per session");
        }

        [Test]
        public void APauseOrSystemMenuGapIsNeverJudged()
        {
            var g = new RefreshGovernor();
            g.Start(true);
            for (int i = 0; i < 90 * 11; i++) g.Frame(1.0 / 90, 8.0, 0);   // past the warm-up, all good
            int windows = g.Windows;
            // A 4 s gap (the headset was off) followed by the dropped frames the compositor counted meanwhile.
            Assert.IsFalse(g.Frame(4.0, 16.0, 300));
            for (int i = 0; i < 90 * 3 - 2; i++) g.Frame(1.0 / 90, 8.0, 0);
            Assert.That(g.Windows, Is.EqualTo(windows), "the gap restarted the window and the next 3 s are quiet");
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz90));
        }

        [Test]
        public void NothingIsJudgedWhileTheSystemMenuHasFocus()
        {
            var g = new RefreshGovernor();
            g.Start(true);
            for (int i = 0; i < 90 * 11; i++) g.Frame(1.0 / 90, 8.0, 0);
            g.SetFocused(false);
            int windows = g.Windows;
            for (int i = 0; i < 90 * 20; i++) Assert.IsFalse(g.Frame(1.0 / 90, 16.0, 3), "over budget, but unfocused");
            Assert.That(g.Windows, Is.EqualTo(windows));
            Assert.That(g.Rung, Is.EqualTo(QualityRung.Hz90));
            g.SetFocused(true);
            for (int i = 0; i < 90 * 3 - 2; i++) g.Frame(1.0 / 90, 16.0, 3);
            Assert.That(g.Windows, Is.EqualTo(windows), "and 3 s quiet after focus comes back");
        }
    }
}
