// E4 (experience.md section 6): Fast Motion Mode only while drumming. The vectors are the experience lab's
// experience.vectors.json fmmPolicy (90 Hz frames; drum hits at 2, 2.5, 3, 12 s; a stick held 15..20 s; from 22 s one of
// the two hands at low confidence): requests on at 2.000, off at 11.989, on at 12.000, off at 23.578 (blocked), +-1 frame.
using System.Collections.Generic;
using MusicRoom.Core;
using NUnit.Framework;

namespace MusicRoom.Tests.Core
{
    public class FastMotionPolicyTests
    {
        const double Frame = 1.0 / 90.0;

        struct Req { public double T; public bool On; public bool Blocked; }

        static List<Req> Run(FastMotionPolicy p, int frames, System.Func<double, (bool hit, bool stick, bool single, int tracked, int low)> at)
        {
            var reqs = new List<Req>();
            for (int f = 0; f <= frames; f++)
            {
                double t = f * Frame;
                var s = at(t);
                int r = p.Frame(t, Frame, s.hit, s.stick, s.single, s.tracked, s.low);
                if (r != 0) reqs.Add(new Req { T = t, On = r > 0, Blocked = p.Blocked });
            }
            return reqs;
        }

        [Test]
        public void TheReferenceTimelineRequestsOnAtOnceOffAfterFiveQuietSecondsAndBlocksInADimRoom()
        {
            var p = new FastMotionPolicy();
            var hits = new HashSet<int> { 180, 225, 270, 1080 };   // round(t * 90) of 2, 2.5, 3, 12 s
            var reqs = Run(p, 30 * 90, t =>
            {
                int f = (int)System.Math.Round(t * 90);
                return (hits.Contains(f), t >= 15 && t < 20, false, 2, t >= 22 && t < 30 ? 1 : 0);
            });
            double[] want = { 2.0, 11.9889, 12.0, 23.5778 };
            bool[] on = { true, false, true, false };
            Assert.That(reqs.Count, Is.EqualTo(4), string.Join(", ", reqs.ConvertAll(r => $"{r.T:F4} {(r.On ? "on" : "off")}")));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(reqs[i].T, Is.EqualTo(want[i]).Within(Frame * 1.01), $"request {i}");
                Assert.That(reqs[i].On, Is.EqualTo(on[i]), $"request {i} on/off");
            }
            Assert.IsFalse(reqs[2].Blocked);
            Assert.IsTrue(reqs[3].Blocked, "the last off is the low-light guard");
            Assert.IsTrue(p.Blocked);
            Assert.That(p.BlockedFraction, Is.EqualTo(0.5).Within(1e-6), "one of two hands at low confidence");
        }

        [Test]
        public void SingleModeOnTheDrumsKeepsItOnAndBlockedStaysOffForTheSession()
        {
            var p = new FastMotionPolicy();
            var reqs = Run(p, 20 * 90, t => (false, false, t < 10, 2, 0));
            Assert.That(reqs.Count, Is.EqualTo(2));
            Assert.That(reqs[0].T, Is.EqualTo(0.0), "on at the first frame of single-mode drums");
            Assert.That(reqs[1].T, Is.EqualTo(15.0).Within(Frame * 1.01), "off 5 s after leaving the drums");
            Assert.IsFalse(p.On);

            // A dim room: both hands at low confidence from the start of drumming. Blocked after one 3 hand-second block
            // (1 s grace + 1.5 s with two hands), and never on again, whatever the player does.
            var dim = new FastMotionPolicy();
            var r2 = Run(dim, 20 * 90, t => (t >= 1 && t < 2, true, false, 2, 2));
            Assert.That(r2.Count, Is.EqualTo(2));
            Assert.IsTrue(r2[0].On);
            Assert.IsFalse(r2[1].On);
            Assert.That(r2[1].T, Is.EqualTo(2.5).Within(2 * Frame), "1 s grace + 3 hand-seconds of two hands");
            Assert.IsTrue(dim.Blocked);
        }

        [Test]
        public void OffRequestsAreAtLeastOneSecondApartButOnIsNeverDelayed()
        {
            var p = new FastMotionPolicy();
            // on at 0, off at 9 (4 s recent window + 5 s quiet), on again at 9.5 at once (a hit), then off 9.5 + 4 + 5.
            var reqs = Run(p, 20 * 90, t =>
            {
                int f = (int)System.Math.Round(t * 90);
                return (f == 0 || f == 855, false, false, 2, 0);
            });
            Assert.That(reqs.Count, Is.EqualTo(4));
            Assert.That(reqs[1].T, Is.EqualTo(9.0).Within(Frame * 1.01));
            Assert.That(reqs[2].T, Is.EqualTo(9.5).Within(Frame * 0.01), "on is immediate even 0.5 s after an off");
            Assert.That(reqs[3].T, Is.EqualTo(18.5).Within(Frame * 1.01));
        }
    }
}
