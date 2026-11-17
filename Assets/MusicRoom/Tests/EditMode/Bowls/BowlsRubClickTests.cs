// A rub must not crackle. On the headset the tracker gives a new fingertip sample every 20-40 ms, with millimetres of
// jitter and now and then a jump of a few centimetres, and the bowls hand logic turns each sample into the rub's force
// (from the depth) and speed (along the rim). Raw, those steps and one-sample speed spikes reached the friction model
// as jumps: the stick bounced off the wall and the friction jerked, clicks like a broken cassette. BowlsHands' rub
// smoothing (RubSmoothing) clamps and slews what it sends; the unit ramps each frame's values per sample.
// The test rubs the big bowl's outer wall (0.7 turns/s, the scripted circle of the cases) three ways through the scripted
// hands and the managed unit: smooth (90 Hz tracker, no jitter: the reference rub), and jittery (25 Hz, 3 mm jitter, one
// 4 cm tracker jump) with and without the smoothing. Clicks are measured on the output's second difference (a high-pass
// that a sample-to-sample jump dominates): the worst 1 ms window's energy over the median window's, in the steady rub. The
// jittery rub with smoothing stays within a margin of the smooth rub's score.
using System;
using System.Collections.Generic;
using MusicRoom.Bowls;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.Bowls
{
    public class BowlsRubClickTests
    {
        const int Bowl = 0;
        const double From = 2.4, To = 6.0;   // the steady rub (it sings from about 1.5 s)
        /// <summary>The click score of a jittery rub may be this many times the smooth rub's.</summary>
        const double Margin = 3.0;

        static Func<double, double3?> WithJump(Func<double, double3?> path, double at, double3 jump) =>
            t => { var p = path(t); return p.HasValue && t >= at && t < at + 0.04 ? p.Value + jump : p; };

        static float[] Rub(double trackerHz, double jitter, bool spike, bool smoothing)
        {
            Func<double, double3?> path = Circle(Placement.App, Bowl);
            if (spike) path = WithJump(path, 3.5, new double3(0.03, 0, 0.03));
            var r = Simulate(new Opts
            {
                Layout = Placement.App, Seconds = 6.6, HandsAt = Hand(path), Hz = 72, TrackerHz = trackerHz, Jitter = jitter, Seed = 7,
                RubSmoothing = smoothing, KeepAudio = true,
            });
            float[] y = r.Audio.ToArray();
            r.Rig?.Dispose();
            return y;
        }

        /// <summary>The worst 1 ms window's second-difference energy over the median window's, in [From, To).</summary>
        static double ClickScore(float[] y, out double rms)
        {
            int a = (int)(From * SR), b = Math.Min(y.Length, (int)(To * SR)), w = SR / 1000;
            var e = new List<double>((b - a) / w + 1);
            double sum = 0;
            for (int s = a; s + w <= b; s += w)
            {
                double acc = 0;
                for (int n = s; n < s + w; n++) { double d2 = y[n] - 2.0 * y[n - 1] + y[n - 2]; acc += d2 * d2; sum += (double)y[n] * y[n]; }
                e.Add(acc);
            }
            rms = Math.Sqrt(sum / Math.Max(1, b - a));
            var sorted = new List<double>(e);
            sorted.Sort();
            double median = sorted[sorted.Count / 2], max = sorted[sorted.Count - 1];
            return max / Math.Max(1e-30, median);
        }

        [Test]
        public void AJitteryRubWithATrackerJumpDoesNotClick()
        {
            double smooth = ClickScore(Rub(90, 0, false, true), out double rmsSmooth);
            double jittery = ClickScore(Rub(25, 0.003, true, true), out double rmsJittery);
            double raw = ClickScore(Rub(25, 0.003, true, false), out double rmsRaw);
            double noJump = ClickScore(Rub(25, 0.003, false, true), out _);
            Debug.Log($"[BowlsRubClick] click score (worst 1 ms second-difference energy / median): smooth rub {smooth:F1} (rms {rmsSmooth:F4}), " +
                      $"jittery + jump with smoothing {jittery:F1} (rms {rmsJittery:F4}), without {raw:F1} (rms {rmsRaw:F4}); jittery, no jump, with smoothing {noJump:F1}");
            Assert.That(rmsSmooth, Is.GreaterThan(1e-4), "the smooth rub sings");
            Assert.That(rmsJittery, Is.GreaterThan(0.25 * rmsSmooth), "the jittery rub sings too");
            Assert.That(jittery, Is.LessThan(Margin * smooth), "no clicks beyond the smooth rub's own texture");
        }
    }
}
