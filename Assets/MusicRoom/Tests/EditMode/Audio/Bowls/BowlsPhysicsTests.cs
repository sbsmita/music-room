// Render-and-measure checks of the bowls unit (bowls.md 8 and 11.2.3; the lab's verify.mjs), run on the Strict Burst path
// when Burst is on (bit-identical to the managed reference, ~10x faster) and managed otherwise:
//   the phasor renormalisation over a 10-minute rub (review item 10), damping order and T60s, the rub map's playable cells,
//   chatter at 1.5 m/s, the inside 20 dB under the outside, one extra finger keeping the song, no subnormal state,
//   the tap-then-rub sweep (576 starts), an adversarial workout staying finite, and the mf loudness reference.
using System;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class BowlsPhysicsTests
    {
        const int SR = 48000;
        static BowlsPath Fast => BurstCompiler.IsEnabled ? BowlsPath.Strict : BowlsPath.Managed;

        static BowlsRig Rig(uint seed = 1, double pickup = 0)
        {
            var e = new BowlsRig(seed, Fast);
            for (int b = 0; b < 3; b++) e.Pickup(b, pickup, 0, true);
            return e;
        }

        /// <summary>Posts one rub's contact frames block by block (90 Hz, the module's rate) instead of all at once.</summary>
        sealed class LiveRub
        {
            readonly BowlsRig _e; readonly int _bowl, _kind; readonly double _t0, _t1, _vf, _N, _R; double _ang; long _next; bool _released;
            public LiveRub(BowlsRig e, int bowl, double t0, double dur, double vf, double N, int kind = 0, double angle0 = 0)
            {
                _e = e; _bowl = bowl; _t0 = t0; _t1 = t0 + dur; _vf = vf; _N = N; _kind = kind; _R = e.Design[bowl].R; _ang = angle0;
                _next = (long)Math.Round(t0 * SR);
            }
            public void Before(long blockStart)
            {
                long end = blockStart + 256;
                while (!_released && _next < end)
                {
                    double t = _next / (double)SR;
                    if (t >= _t1) { _e.Release(_bowl, _next); _released = true; break; }
                    double om = _vf / _R;
                    _e.Contact(_bowl, _kind, _N, _vf, _ang, om, 1, 0, 0, _next);
                    _ang += om / 90.0;
                    _next += 533;   // 90 Hz frames (48000 / 90 = 533.3)
                }
            }
        }

        static double SteadyDb(float[] y, double a, double b)
        {
            double e = 0; int n = 0;
            for (int i = (int)(a * SR); i < Math.Min(y.Length, (int)(b * SR)); i++) { e += (double)y[i] * y[i]; n++; }
            return 10 * Math.Log10(e / Math.Max(1, n) + 1e-30);
        }

        // ---------------------------------------------------------------- review item 10: the phasors over 10 minutes

        [Test]
        public void ContactPhasorsStayUnitOverATenMinuteRub()
        {
            using var e = Rig();
            var rub = new LiveRub(e, 1, 0.05, 600, 0.3, 4);
            e.BeforeBlock = rub.Before;
            double worst = 0, worstT = 0; int checks = 0;
            long blocks = 600L * SR / 256;
            for (long k = 0; k < blocks; k++)
            {
                e.RenderBlock(256);
                if (k % 1875 == 1874)   // every 10 s: the magnitude the next block starts from
                {
                    BowlState* s = e.B(1);
                    double me = Math.Abs(Math.Sqrt((double)s->Ec * s->Ec + (double)s->Es * s->Es) - 1);
                    double mt = Math.Abs(Math.Sqrt((double)s->Tc * s->Tc + (double)s->Ts * s->Ts) - 1);
                    if (Math.Max(me, mt) > worst) { worst = Math.Max(me, mt); worstT = k * 256.0 / SR; }
                    checks++;
                    Assert.That(s->On, Is.EqualTo(1), "still rubbing");
                }
            }
            float sv = e.B(1)->StSingVel;
            Debug.Log($"[BOWLS] 10-minute rub: worst ||e| - 1| {worst:E2} (at {worstT:F0} s, {checks} checks), sing velocity at the end {sv:F3} m/s");
            Assert.That(worst, Is.LessThan(1e-5), "|e| - 1 stays below 1e-5 for 10 minutes");
            Assert.That(sv, Is.GreaterThan(0.15f), "and the bowl still sings");
        }

        // ---------------------------------------------------------------- damping

        static double T60Fit(float[] y, double a, double b)
        {
            // least-squares slope of the 10 ms RMS envelope (dB) over [a, b)
            int w = 480; double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = 0;
            for (int i = (int)(a * SR); i + w <= (int)(b * SR); i += w)
            {
                double e = 0; for (int k = i; k < i + w; k++) e += (double)y[k] * y[k];
                double db = 10 * Math.Log10(e / w + 1e-30), t = i / (double)SR;
                sx += t; sy += db; sxx += t * t; sxy += t * db; n++;
            }
            double slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            return -60 / slope;
        }

        [Test]
        public void TouchDampingLevelsMatchTheDesign()
        {
            double[] exp = { 7.0, 0.81, 0.28 };
            double prev = double.MaxValue;
            for (int L = 1; L <= 3; L++)
            {
                using var e = Rig(1, 0.4);
                e.Strike(1, BowlStrikeKind.Tap, 1.5, 0.4, 1, (long)(0.05 * SR));
                e.Damp(1, L, (long)(1.5 * SR));
                float[] y = e.Render(4 * SR, 1);
                double t60 = T60Fit(y, 1.55, 1.55 + new[] { 2, 0.8, 0.25 }[L - 1]);
                Debug.Log($"[BOWLS] Damp({L}) T60 {t60:F2} s (design {exp[L - 1]})");
                Assert.That(t60, Is.EqualTo(exp[L - 1]).Within(0.25 * exp[L - 1]), $"Damp({L}) T60");
                Assert.That(t60, Is.LessThan(prev), "each level damps faster");
                prev = t60;
            }
        }

        [Test]
        public void OneLightFingerKeepsTheSong()
        {
            for (int b = 0; b < 3; b++)
            {
                float[] sv = new float[2];
                for (int L = 0; L < 2; L++)
                {
                    using var e = Rig(1, -0.6);
                    var rub = new LiveRub(e, b, 0.3, 5.0, 0.33, 3.5, 0, -0.6);
                    e.Strike(b, BowlStrikeKind.Tap, 0.15, -0.6, 1, (long)(0.3 * SR));
                    if (L == 1) e.Damp(b, 1, (long)(0.3 * SR));
                    e.BeforeBlock = rub.Before;
                    e.Render((int)(5.2 * SR));
                    sv[L] = e.B(b)->StSingVel;
                }
                Debug.Log($"[BOWLS] bowl {b}: sing velocity {sv[0]:F3} undamped, {sv[1]:F3} with Damp(1) ({sv[1] / sv[0]:P0})");
                Assert.That(sv[1] / sv[0], Is.GreaterThanOrEqualTo(0.9f), $"bowl {b}: one extra light finger keeps >= 90 % of the song");
            }
        }

        // ---------------------------------------------------------------- rubbing

        static float SingAfter(int bowl, double vf, double N, double seconds, int kind, out float lost)
        {
            using var e = Rig(1, -0.6);
            e.Strike(bowl, BowlStrikeKind.Tap, 0.15, -0.6, 1, (long)(0.1 * SR));
            var rub = new LiveRub(e, bowl, 0.1, seconds + 1, vf, N, kind, -0.6);
            e.BeforeBlock = rub.Before;
            e.Render((int)((0.1 + seconds) * SR));
            BowlState* s = e.B(bowl);
            lost = s->StContactSamples > 0 ? (float)s->StLostSamples / s->StContactSamples : 0f;
            return s->StSingVel;
        }

        [Test]
        public void PlayableRubsSingOnEveryBowlAtEveryPressure()
        {
            foreach (int b in new[] { 0, 1, 2 }) foreach (double vf in new[] { 0.2, 0.3 }) foreach (double N in new[] { 2.5, 4, 6 })
            {
                float sv = SingAfter(b, vf, N, 2.5, 0, out float lost);
                Assert.That(sv, Is.GreaterThan(0.15f), $"bowl {b} at {vf} m/s, {N} N sings within 2.5 s (lost {lost:P1})");
            }
        }

        [Test]
        public void TooFastAndLightChatters()
        {
            foreach (int b in new[] { 0, 1, 2 })
            {
                SingAfter(b, 1.5, 2.5, 3, 0, out float lost);
                Assert.That(lost, Is.GreaterThan(0.2f), $"bowl {b}: 1.5 m/s at 2.5 N loses contact (rattles) {lost:P0} of the time");
            }
        }

        [Test]
        public void TheInsideDoesNotSing()
        {
            float[] lvl = new float[2];
            for (int k = 0; k < 2; k++)
            {
                using var e = Rig(1, -0.6);
                var rub = new LiveRub(e, 1, 0.3, 5, 0.3, 4, k == 0 ? 2 : 0, -0.6);
                e.Strike(1, BowlStrikeKind.Tap, 0.15, -0.6, 1, (long)(0.3 * SR));
                e.BeforeBlock = rub.Before;
                float[] y = e.Render(5 * SR, 1);
                lvl[k] = (float)SteadyDb(y, 3.5, 5.0);
            }
            Debug.Log($"[BOWLS] inner {lvl[0]:F1} dB, outer {lvl[1]:F1} dB");
            Assert.That(lvl[1] - lvl[0], Is.GreaterThanOrEqualTo(20f), "the inside is >= 20 dB under the outside ([DA16])");
        }

        /// <summary>The M3 sweep: 3 bowls x 48 tap angles x taps 1 / 1.5 m/s x rubs 0.2 / 0.3 m/s from the tap spot (3.5 N):
        /// every start sings 3 s after the touch (dev/tapdead-sweep.mjs).</summary>
        [Test]
        public void TapThenRubSweepAlwaysSings()
        {
            int fails = 0, total = 0; var bad = new System.Text.StringBuilder();
            foreach (int b in new[] { 0, 1, 2 }) foreach (double V in new[] { 1.0, 1.5 }) foreach (double vf in new[] { 0.2, 0.3 }) for (int k = 0; k < 48; k++)
            {
                double a = k * Math.PI / 48;
                using var e = new BowlsRig(7, Fast);
                for (int q = 0; q < 3; q++) e.Pickup(q, -0.6, 0, true);
                e.Schedule(new[] { new BowlsRig.Act { T = 0.2, Type = "tap", Bowl = b, V = V, Angle = a }, new BowlsRig.Act { T = 0.5, Type = "rub", Bowl = b, Dur = 3.2, Vf = vf, N = 3.5, Angle0 = a, TouchV = 0 } }, -0.6);
                float sv = 0;
                for (int s = 0; s < 3.5 * SR; s += 256) { e.RenderBlock(256); if (s / (double)SR > 3.4 && s / (double)SR < 3.5) sv = e.B(b)->StSingVel; }
                total++;
                if (sv < 0.15f) { fails++; if (bad.Length < 400) bad.Append($" {b}/{V}/{vf}/{a:F2}"); }
            }
            Debug.Log($"[BOWLS] tap-then-rub sweep: {fails} / {total} fail{bad}");
            Assert.That(fails, Is.EqualTo(0), "tap-then-rub dead spots:" + bad);
        }

        // ---------------------------------------------------------------- robustness

        [Test]
        public void AdversarialWorkoutStaysFiniteWithNoSubnormalState()
        {
            using var e = Rig(3, 0.2);
            uint r = 12345;
            double Rnd() { unchecked { r = r * 1664525u + 1013904223u; } return r / 4294967296.0; }
            long t = 0; float peak = 0; int subnormal = 0; bool finite = true;
            for (int k = 0; k < 4000; k++)
            {
                int b = (int)(Rnd() * 3);
                double x = Rnd();
                if (x < 0.25) e.Strike(b, (BowlStrikeKind)(int)(Rnd() * 4), Rnd() * 3, Rnd() * 7, 0.5 + Rnd() * 0.5, t);
                else if (x < 0.75) e.Contact(b, (int)(Rnd() * 3), Rnd() * 9, (Rnd() - 0.5) * 3, Rnd() * 7, (Rnd() - 0.5) * 40, 0.5 + Rnd() * 0.5, (int)(Rnd() * 4), Rnd() < 0.3 ? (int)(Rnd() * 13000) : 0, t);
                else if (x < 0.85) e.Release(b, t, (int)(Rnd() * 4));
                else if (x < 0.95) e.Damp(b, (int)(Rnd() * 4), t);
                else e.Pickup(b, Rnd() * 7, t, Rnd() < 0.5);
                t += (long)(Rnd() * 600);
                while (e.Now + 256 <= t) { e.RenderBlock(256); for (int n = 0; n < 256; n++) { if (float.IsNaN(e.Mix[n]) || float.IsInfinity(e.Mix[n])) finite = false; else peak = Math.Max(peak, Math.Abs(e.Mix[n])); } }
            }
            for (int k = 0; k < 120 * SR / 256; k++)
            {
                e.RenderBlock(256);
                for (int b = 0; b < 3; b++) for (int i = 0; i < 128; i++) { float v = e.B(b)->G[i]; if (v != 0 && Math.Abs(v) < 1.1754943508222875e-38f) subnormal++; }
            }
            Debug.Log($"[BOWLS] adversarial: peak {peak:F2}, subnormal states {subnormal}, awake after 2 min {e.B(0)->Awake}{e.B(1)->Awake}{e.B(2)->Awake}");
            Assert.That(finite, Is.True, "finite");
            Assert.That(peak, Is.LessThan(20f), "bounded");
            Assert.That(subnormal, Is.EqualTo(0), "no subnormal state");
        }

        // ---------------------------------------------------------------- loudness (bowls.md 9)

        [Test]
        public void MfReferencePhraseSitsAtThePianosLoudness()
        {
            // 4 taps E4 E4 C4 G3 at 0.7 m/s, 108 bpm, the unit's channel, dry (tools/conformance.mjs: seed 7, each bowl struck
            // and heard from its facing angle on the reviewed layout): -25.13 LUFS in the lab; the piano's mf reference
            // (Ode bar 1, vel 0.55, dry) is -24.01 LUFS (platform 10.3: within +-2 LU)
            double spb = 60.0 / 108, dur = 0.05 + 4 * spb + 1.0; int[] seq = { 2, 2, 1, 0 };
            using var e = new BowlsRig(7, Fast);
            for (int b = 0; b < 3; b++) e.Pickup(b, MusicRoom.Tests.Bowls.BowlsSim.Facing(MusicRoom.Tests.Bowls.BowlsSim.Placement.Reference, b), 0, true);
            for (int k = 0; k < 4; k++) e.Strike(seq[k], BowlStrikeKind.Tap, 0.7, MusicRoom.Tests.Bowls.BowlsSim.Facing(MusicRoom.Tests.Bowls.BowlsSim.Placement.Reference, seq[k]), 1, (long)BowlsConst.JsRound((0.05 + k * spb) * SR));
            float[] y = e.Render((int)Math.Round(dur * SR));
            double lufs = Loudness.Lufs(y);
            Assert.That(lufs, Is.EqualTo(-25.13).Within(0.05), "the lab's mf reference phrase level");
            Debug.Log($"[BOWLS] mf phrase {lufs:F2} LUFS ({lufs + 24.01:+0.00;-0.00} LU against the piano)");
            Assert.That(lufs - (-24.01), Is.InRange(-2.0, 2.0), "loudness parity with the piano's mf reference");
            // a 0.3 m/s rub at 4 N (middle) is at most +3 LU, 0.45 m/s at most +6 LU
            foreach (var (vf, max, lab) in new[] { (0.3, 3.0, 1.47), (0.45, 6.0, 5.04) })
            {
                using var r = new BowlsRig(7, Fast);
                double f1 = MusicRoom.Tests.Bowls.BowlsSim.Facing(MusicRoom.Tests.Bowls.BowlsSim.Placement.Reference, 1);
                for (int b = 0; b < 3; b++) r.Pickup(b, MusicRoom.Tests.Bowls.BowlsSim.Facing(MusicRoom.Tests.Bowls.BowlsSim.Placement.Reference, b), 0, true);
                r.Schedule(new[] { new BowlsRig.Act { T = 0.05, Type = "rub", Bowl = 1, Dur = 6, Vf = vf, N = 4, Angle0 = f1 } }, 0);
                float[] z = r.Render((int)Math.Round(6.2 * SR));
                var win = new float[(int)Math.Round((2.9 + dur) * SR) - (int)Math.Round(2.9 * SR)];
                Array.Copy(z, (int)Math.Round(2.9 * SR), win, 0, win.Length);
                double l = Loudness.Lufs(win) + 24.01;
                Debug.Log($"[BOWLS] rub {vf} m/s, 4 N: {l:+0.00;-0.00} LU against the piano (lab {lab:+0.00})");
                Assert.That(l, Is.LessThanOrEqualTo(max), $"rub {vf} m/s loudness");
                Assert.That(l, Is.EqualTo(lab).Within(0.05), "the lab's level");
            }
        }
    }
}
