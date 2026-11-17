// The marimba sound unit's behaviour in Unity (spec 3 checks re-measured on the C# unit, platform 10.3 conformance):
// loudness (calibration and parity numbers), clicks at the handoff, the cap fade and Silence, no low-frequency push,
// denormals and retirement, a 60 s random workout, hand and mallet damping, rolls, the fingertip voicing, the governor's
// Share, and zero allocation on the main-thread API and the render. Renders run through the Burst entry with the
// engine's FloatMode.Fast unless a check is bit-level.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using static MusicRoom.Tests.Audio.MarimbaRef;

namespace MusicRoom.Tests.Audio
{
    public unsafe class MarimbaBehaviourTests
    {
        const int Sr = 48000;
        static Dictionary<string, object> Verify => Obj(Load("verify.json"));

        static List<object> Hit(double t, int bar, float vel, double u = 0.45, double c = 0, MarimbaMallet m = MarimbaMallet.Medium) =>
            new List<object> { Math.Floor(t * Sr + 0.5), 0.0, (double)bar, (double)vel, u, c, (double)(int)m };
        static List<object> DampAt(double t, int bar, MarimbaDampKind kind, double u = 0.5, double c = 0) =>
            new List<object> { Math.Floor(t * Sr + 0.5), 1.0, (double)bar, (double)(int)kind, u, c };
        static List<object> UndampAt(double t, int bar) => new List<object> { Math.Floor(t * Sr + 0.5), 2.0, (double)bar };
        static float[] Mono(List<object> ev, double seconds, MarimbaRenderMode mode = MarimbaRenderMode.Fast) =>
            MarimbaOffline.Mono(RenderScore(Sorted(ev), (int)Math.Ceiling(seconds * Sr), mode));
        static List<object> Sorted(List<object> ev) { var s = new List<object>(ev); MergeSortByAt(s); return s; }
        static void MergeSortByAt(List<object> a)
        {
            // stable insertion sort by sample (renderScore's stable sort)
            for (int i = 1; i < a.Count; i++)
            {
                var x = a[i]; double t = Num(Arr(x)[0]); int j = i - 1;
                while (j >= 0 && Num(Arr(a[j])[0]) > t) { a[j + 1] = a[j]; j--; }
                a[j + 1] = x;
            }
        }
        static double Rms(float[] y, int a, int b) { double s = 0; for (int i = a; i < b; i++) s += (double)y[i] * y[i]; return Math.Sqrt(s / Math.Max(1, b - a)); }
        static double LevelDb(float[] y, double t) { int a = (int)Math.Round(t * Sr); return 20 * Math.Log10(Rms(y, a, a + 2400) + 1e-15); }
        static float[] Slice(float[] y, int n) { var o = new float[Math.Min(n, y.Length)]; Array.Copy(y, o, o.Length); return o; }
        static float VelFinger(double v) => (float)Math.Min(1, Math.Max(0.05, Math.Pow((v - 0.12) / (1.5 - 0.12), 0.7)));
        static float VelMallet(double v) => (float)Math.Min(1, Math.Max(0.05, Math.Pow((v - 0.25) / (2.8 - 0.25), 0.7)));

        // ------------------------------------------------------------------ loudness

        [Test]
        public void Loudness_CalibrationAndParityWithThePiano()
        {
            // calibrate.mjs: C4 vel .6 u .45 medium, first 0.42 s, dry: -21.70 LUFS (the new piano's C4 at vel .6)
            var y = Mono(new List<object> { Hit(0.02, 12, 0.6f) }, 0.6);
            double c4 = Loudness.Lufs(Slice(y, (int)Math.Round(0.42 * Sr)));
            // tools/parity.mjs: Ode to Joy bar 1 (E4 E4 F4 G4, 108 BPM) at vel .55, dry: -24.14 LUFS (piano -24.01)
            double spb = 60.0 / 108;
            int[] notes = { 64, 64, 65, 67 };
            var ev = new List<object>();
            for (int k = 0; k < 4; k++) ev.Add(Hit(0.05 + k * spb, notes[k] - 48, 0.55f));
            double ode = Loudness.Lufs(Mono(ev, 0.05 + 4 * spb + 1.0));
            Debug.Log($"[MARIMBA] loudness: C4 vel .6 first 0.42 s {c4:F2} LUFS (lab -21.70); Ode bar 1 vel .55 {ode:F2} LUFS (lab -24.14, the piano -24.01: {ode + 24.01:+0.00;-0.00} LU, limit +-2)");
            Assert.That(c4, Is.EqualTo(-21.70).Within(0.06));
            Assert.That(ode, Is.EqualTo(-24.14).Within(0.06));
            Assert.That(Math.Abs(ode - -24.01), Is.LessThanOrEqualTo(2.0), "loudness parity with the piano (platform 10.3)");
        }

        [Test]
        public void Velocity_LouderAndTheContactShortens()
        {
            double prev = double.NegativeInfinity, prevMs = double.PositiveInfinity;
            var contact = new MarimbaContact();
            var mem = new MarimbaStrikeRecord[1];
            foreach (float v in new[] { 0.1f, 0.3f, 0.6f, 0.9f, 1.0f })
            {
                double l = Loudness.Lufs(Slice(Mono(new List<object> { Hit(0.02, 12, v) }, 0.6), (int)Math.Round(0.42 * Sr)));
                fixed (MarimbaStrikeRecord* r = mem) contact.Solve(r, 12, v, 0.45, 0, MarimbaMallet.Medium, 1);
                Assert.That(l, Is.GreaterThan(prev), $"vel {v} louder");
                Assert.That(contact.Last.ContactMs, Is.LessThanOrEqualTo(prevMs + 1e-9), $"vel {v} contact shortens");
                prev = l; prevMs = contact.Last.ContactMs;
            }
        }

        [Test]
        public void Fingertip_AnOrdinaryTapSitsJustUnderAnOrdinaryMalletStroke()
        {
            // check 3.23: a 0.6 m/s tap within 4 LU of a 1.2 m/s stroke on every bar G3..F#5 and never louder (+0.5 LU)
            float vf = VelFinger(0.6), vm = VelMallet(1.2);
            int W = (int)Math.Round(0.42 * Sr);
            double worst = 0, best = -99;
            for (int i = 0; i < 37; i++)
            {
                double lf = Loudness.Lufs(Slice(Mono(new List<object> { Hit(0.02, i, vf, 0.45, 0, MarimbaMallet.Finger) }, 0.5), W));
                double lm = Loudness.Lufs(Slice(Mono(new List<object> { Hit(0.02, i, vm) }, 0.5), W));
                double d = lf - lm;
                Assert.That(d, Is.LessThanOrEqualTo(0.5), $"bar {i}: the fingertip never louder");
                if (i >= 7 && i <= 30) { Assert.That(d, Is.InRange(-4.0, 0.0), $"bar {i}"); worst = Math.Min(worst, d); best = Math.Max(best, d); }
            }
            Debug.Log($"[MARIMBA] fingertip 0.6 m/s (vel {vf:F4}) vs mallet 1.2 m/s (vel {vm:F4}), G3..F#5: {worst:F2} .. {best:F2} LU");
        }

        // ------------------------------------------------------------------ artefacts

        [Test]
        public void Handoff_NoStepWhereTheContactHandsOverToTheModes()
        {
            var contact = new MarimbaContact();
            var mem = new MarimbaStrikeRecord[1];
            foreach (var (bar, vel, m) in new[] { (0, 0.3f, MarimbaMallet.Medium), (12, 0.6f, MarimbaMallet.Medium), (12, 1f, MarimbaMallet.Hard), (24, 0.8f, MarimbaMallet.Hard), (36, 1f, MarimbaMallet.Hard), (6, 0.6f, MarimbaMallet.Finger) })
            {
                int nh;
                fixed (MarimbaStrikeRecord* r = mem) { contact.Solve(r, bar, vel, 0.45, 0, m, 1); nh = r->Nh; }
                var y = Mono(new List<object> { Hit(0.02, bar, vel, 0.45, 0, m) }, 0.3);
                int h = (int)Math.Round(0.02 * Sr) + nh;
                float D2(int n) => Math.Abs(y[n] - 2 * y[n - 1] + y[n - 2]);
                float at = Math.Max(D2(h), D2(h + 1)), mx = 0;
                for (int n = h - 24; n < h + 24; n++) if (Math.Abs(n - h) > 2) mx = Math.Max(mx, D2(n));
                Assert.That(at / mx, Is.LessThanOrEqualTo(1.2), $"bar {bar} {m} vel {vel}");
            }
        }

        [Test]
        public void LowFrequency_NoPushAndNoDc()
        {
            foreach (var (bar, vel, m) in new[] { (0, 0.9f, MarimbaMallet.Hard), (0, 0.6f, MarimbaMallet.Medium), (12, 0.6f, MarimbaMallet.Medium), (24, 1f, MarimbaMallet.Hard), (6, 0.8f, MarimbaMallet.Finger) })
            {
                var y = Mono(new List<object> { Hit(0.02, bar, vel, 0.45, 0, m) }, 2.0);
                double s = 0; foreach (float v in y) s += v;
                double mean = s / y.Length, rms = Rms(y, 0, y.Length);
                Assert.That(Math.Abs(mean) / rms, Is.LessThan(1e-3), $"bar {bar} {m}: |mean| / rms");
            }
        }

        [Test]
        public void Denormals_NeverAndEveryBarRetiresToExactZero()
        {
            using var h = new MarimbaOffline();
            int sub = 0, maxActive = 0, next = 0;
            float lastSecondPeak = 0;
            int blocks = 32 * Sr / 256;
            for (int b = 0; b < blocks; b++)
            {
                // the main thread posts each block's strokes just before it (37 strokes > the 32 record slots: posting them
                // all at once would overwrite records the audio thread has not read yet)
                while (next < 37 && (long)Math.Floor((0.05 + next * 0.04) * Sr + 0.5) < h.Frame + 256)
                {
                    h.Sound.StrikeExact(next, 0.9f, 0.45, 0, MarimbaMallet.Hard, MarimbaOffline.At((long)Math.Floor((0.05 + next * 0.04) * Sr + 0.5)));
                    next++;
                }
                float* o = h.RenderBlock();
                float* g = (float*)h.State->Groups;
                for (int k = 0; k < 74 * 24; k++) { float v = g[k]; if (v != 0 && Math.Abs(v) < 1.17549435e-38f) sub++; Assert.That(float.IsNaN(v) || float.IsInfinity(v), Is.False); }
                int act = 0; for (int i = 0; i < 37; i++) act += h.State->Active[i];
                maxActive = Math.Max(maxActive, act);
                if (b >= blocks - Sr / 256) for (int k = 0; k < 3 * 256; k++) lastSecondPeak = Math.Max(lastSecondPeak, Math.Abs(o[k]));
            }
            int activeEnd = 0; for (int i = 0; i < 37; i++) activeEnd += h.State->Active[i];
            var lab = Obj(Verify, "denormals");
            Debug.Log($"[MARIMBA] denormals: subnormal states {sub}, max active {maxActive}, active at end {activeEnd}, retired {h.State->Retired} capped {h.State->Capped} (lab: retired {Num(lab, "retired")}, capped {Num(lab, "capped")})");
            Assert.That(sub, Is.EqualTo(0));
            Assert.That(activeEnd, Is.EqualTo(0));
            Assert.That(lastSecondPeak, Is.EqualTo(0f));
            Assert.That(h.State->Retired, Is.EqualTo((int)Num(lab, "retired")));
            Assert.That(h.State->Capped, Is.EqualTo((int)Num(lab, "capped")));
        }

        [Test]
        public void Workout_SixtySecondsRandom_FiniteAndBounded()
        {
            // verify.mjs 3.16, the same seeded score (xorshift32 seed 12345)
            uint s = 12345;
            double Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s / 4294967296.0; }
            var ev = new List<object>();
            double t = 0.05;
            MarimbaMallet[] ms = { MarimbaMallet.Soft, MarimbaMallet.Medium, MarimbaMallet.Hard, MarimbaMallet.Finger };
            while (t < 58)
            {
                t += 0.03 + Rnd() * 0.12;
                int bar = (int)Math.Floor(Rnd() * 37);
                if (Rnd() < 0.08) { var kind = Rnd() < 0.5 ? MarimbaDampKind.Hand : MarimbaDampKind.Mallet; ev.Add(DampAt(t, bar, kind, Rnd())); ev.Add(UndampAt(t + 0.3, bar)); }
                else { float vel = (float)Rnd(); double u = Rnd(), c = Rnd() * 2 - 1; ev.Add(Hit(t, bar, vel, u, c, ms[(int)Math.Floor(Rnd() * 4)])); }
            }
            var y = Mono(ev, 60);
            float pk = 0; bool fin = true;
            foreach (float v in y) { if (float.IsNaN(v) || float.IsInfinity(v)) fin = false; pk = Math.Max(pk, Math.Abs(v)); }
            double labPeak = Num(Obj(Verify, "stability"), "peak");
            Debug.Log($"[MARIMBA] 60 s workout: {ev.Count} events, peak {pk:F4} (lab {labPeak})");
            Assert.That(fin, Is.True);
            Assert.That(pk, Is.LessThan(2f));
            Assert.That(pk, Is.EqualTo((float)labPeak).Within(0.001f), "the same score as the lab gives the same peak");
        }

        [Test]
        public void Cap_TheQuietestBarFadesWithoutAClick()
        {
            // 12 bars inside 22 ms at Budget 400 (cap 8): the capped bars fade over one block; the largest step there is no
            // larger than the signal's own (+1 %)
            var ev = new List<object>();
            for (int i = 0; i < 12; i++) ev.Add(Hit(0.002 * i, i * 3, 0.8f));
            var y = MarimbaOffline.Mono(RenderScore(ev, 12 * 256, MarimbaRenderMode.Fast, 1, 400));
            float own = NullTest.MaxStep(y, 1, 256 * 3);
            float fade = NullTest.MaxStep(y, 256 * 3, y.Length);
            Assert.That(fade, Is.LessThanOrEqualTo(own * 1.01f));
        }

        [Test]
        public void Silence_FadesOverOneBlockWithoutAClick()
        {
            using var h = new MarimbaOffline();
            h.Sound.Strike(0, 0.8f, 0.45f, 0f, MarimbaMallet.Medium);
            h.Sound.Strike(12, 0.8f, 0.45f, 0f, MarimbaMallet.Medium);
            var y = new List<float>();
            for (int b = 0; b < 20; b++) { float* o = h.RenderBlock(); for (int i = 0; i < 256; i++) y.Add(o[i] + o[256 + i] + o[512 + i]); }
            h.Sound.Silence();
            for (int b = 0; b < 4; b++) { float* o = h.RenderBlock(); for (int i = 0; i < 256; i++) y.Add(o[i] + o[256 + i] + o[512 + i]); }
            var a = y.ToArray();
            float own = NullTest.MaxStep(a, 256 * 10, 256 * 20);
            Assert.That(NullTest.MaxStep(a, 256 * 20, 256 * 21), Is.LessThanOrEqualTo(own * 1.01f), "the fade block");
            Assert.That(NullTest.Peak(a, 256 * 21, a.Length), Is.EqualTo(0f), "silent after one block");
        }

        // ------------------------------------------------------------------ techniques

        [Test]
        public void HandDamp_QuickButNotACut()
        {
            var yh = Mono(new List<object> { Hit(0.02, 12, 0.6f), DampAt(0.32, 12, MarimbaDampKind.Hand, 0.5) }, 1.5);
            double drop = LevelDb(yh, 0.29) - LevelDb(yh, 0.47);
            Debug.Log($"[MARIMBA] hand damp on C4: {drop:F1} dB in 150 ms (lab {Num(Obj(Obj(Verify, "damping"), "handAt300ms"), "dropDb")})");
            Assert.That(drop, Is.InRange(25.0, 60.0));
        }

        [Test]
        public void DeadStroke_ShortAfterTheMute()
        {
            const double tD = 0.200;   // MarimbaPlay mutes a dead stroke 200 ms after the stroke (spec 5.4)
            var y0 = Mono(new List<object> { Hit(0.02, 12, 0.6f) }, 1.5);
            var yd = Mono(new List<object> { Hit(0.02, 12, 0.6f), DampAt(0.02 + tD, 12, MarimbaDampKind.Mallet, 0.45) }, 1.5);
            double reference = LevelDb(yd, 0.03), t40 = -1;
            for (double t = 0.03; t < 1.4; t += 0.005) if (LevelDb(yd, t) < reference - 40) { t40 = t; break; }
            double underFree = LevelDb(y0, 0.02 + tD + 0.1) - LevelDb(yd, 0.02 + tD + 0.1);
            Debug.Log($"[MARIMBA] dead stroke: -40 dB {(t40 - 0.02 - tD) * 1000:F0} ms after the mute, {underFree:F1} dB under the free note 100 ms after it");
            Assert.That(t40, Is.GreaterThan(0));
            Assert.That((t40 - 0.02 - tD) * 1000, Is.LessThanOrEqualTo(120));
            Assert.That(underFree, Is.GreaterThanOrEqualTo(25));
        }

        [Test]
        public void Roll_SustainsWithoutRunningAway()
        {
            var ev = new List<object>();
            for (int k = 0; k < 18; k++) ev.Add(Hit(0.05 + k / 12.0, 19, 0.45f + 0.05f * (k % 2), k % 2 == 1 ? 0.42 : 0.56, k % 2 == 1 ? -0.2 : 0.2));
            var yr = Mono(ev, 2.5);
            double Seg(double a, double b) => 20 * Math.Log10(Rms(yr, (int)Math.Round(a * Sr), (int)Math.Round(b * Sr)));
            double gain = Seg(0.8, 1.3) - Seg(0.05, 0.3);
            Assert.That(gain, Is.InRange(-3.0, 6.0));
        }

        // ------------------------------------------------------------------ the tail (the conformance's metal rule + spec 3.22)

        /// <summary>BUILD-GUIDE 13 / phase-A R2: the wood rule's top-20 share is 100 % for any single tuned note, so the
        /// marimba carries its own tail check: 0.3 s after a stroke everything but the fundamental (and its tube, tuned to
        /// it) is at least 12 dB down. A Hann-windowed DFT over 0.3-0.5 s after the stroke, 40 Hz - 8 kHz in 2.5 Hz steps;
        /// the fundamental's band is +-max(8 %, 25 Hz).</summary>
        [TestCase(0)] [TestCase(7)] [TestCase(12)] [TestCase(19)] [TestCase(24)] [TestCase(31)] [TestCase(36)]
        public void Tail_AfterPointThreeSecondsOnlyTheFundamentalRemains(int bar)
        {
            var y = Mono(new List<object> { Hit(0.02, bar, 0.6f) }, 0.6);
            int a = (int)Math.Round(0.32 * Sr), n = (int)Math.Round(0.20 * Sr);
            var w = new double[n];
            for (int i = 0; i < n; i++) w[i] = y[a + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
            double f0 = 440.0 * Math.Pow(2, (48 + bar - 69) / 12.0), band = Math.Max(0.08 * f0, 25.0);
            double pf = 0, po = 0, fo = 0;
            for (double f = 40; f <= 8000; f += 2.5)
            {
                double re = 0, im = 0, k = 2 * Math.PI * f / Sr;
                for (int i = 0; i < n; i++) { re += w[i] * Math.Cos(k * i); im -= w[i] * Math.Sin(k * i); }
                double p = re * re + im * im;
                if (Math.Abs(f - f0) <= band) pf = Math.Max(pf, p);
                else if (p > po) { po = p; fo = f; }
            }
            double down = 10 * Math.Log10(pf / Math.Max(po, 1e-30));
            Debug.Log($"[MARIMBA] tail bar {bar} ({f0:F1} Hz): the strongest other peak ({fo:F0} Hz) is {down:F1} dB under the fundamental 0.3 s after the stroke");
            Assert.That(pf, Is.GreaterThan(0));
            Assert.That(down, Is.GreaterThanOrEqualTo(12.0), "spec 3.22");
        }

        // ------------------------------------------------------------------ the governor's share (platform UnitShareTests)

        [Test]
        public void Share_PointFourLowersTheSteadyWorkByHalf()
        {
            double Steady(float share)
            {
                using var h = new MarimbaOffline();
                h.Sound.Share = share;
                for (int i = 0; i < 30; i++) h.Sound.StrikeExact(i, 0.8f, 0.45, 0, MarimbaMallet.Medium, MarimbaOffline.At(10 + i * 40));
                double sum = 0; int n = 0;
                for (int b = 0; b < 40; b++) { h.RenderBlock(); if (b >= 8) { sum += h.State->Work; n++; } }
                return sum / n;
            }
            double full = Steady(1f), cut = Steady(0.4f);
            Debug.Log($"[MARIMBA] share: steady Work {full:F1} lanes at Share 1, {cut:F1} at Share 0.4 (lab 168 -> 70): {100 * (1 - cut / full):F0} % less");
            Assert.That(cut, Is.LessThanOrEqualTo(0.5 * full));
        }

        // ------------------------------------------------------------------ review item 13: the opt-in boundary crossfade

        [Test]
        public void BoundaryCrossfade_OptIn_ConstantPowerAcrossTheOctaveBoundaries()
        {
            float[][] Render(int bar, bool xf)
            {
                using var h = new MarimbaOffline();
                h.Sound.BoundaryCrossfade = xf;
                h.Sound.Strike(bar, 0.6f, 0.45f, 0f, MarimbaMallet.Medium);
                return h.Render(Sr / 2);
            }
            double E(float[] y) { double e = 0; foreach (float v in y) e += (double)v * v; return e; }
            foreach (int bar in new[] { 10, 11, 12, 13, 22, 23, 24, 25 })
            {
                var hard = Render(bar, false); var xf = Render(bar, true);
                int own = MarimbaTables.Source[bar];
                MarimbaStateBuilder.BoundaryGains(bar, out int other, out float g1, out float g2);
                double eh = E(hard[own]), e1 = E(xf[own]), e2 = E(xf[other]);
                Assert.That(e1 + e2, Is.EqualTo(eh).Within(eh * 1e-4), $"bar {bar}: constant power");
                Assert.That(e2 / e1, Is.EqualTo((double)g2 * g2 / ((double)g1 * g1)).Within(1e-3 * (1 + e2 / e1)), $"bar {bar}: the split follows x");
                Debug.Log($"[MARIMBA] crossfade bar {bar} ({MarimbaRef.Mallet("medium")}): own source {own} {10 * Math.Log10(e1 / eh):F2} dB, neighbour {other} {10 * Math.Log10(e2 / eh):F2} dB");
            }
            // every other bar is untouched, to the bit
            var a = Render(16, false); var b = Render(16, true);
            for (int c = 0; c < 3; c++) Assert.That(Fnv(a[c]), Is.EqualTo(Fnv(b[c])), "E4 keeps its one source");
        }

        // ------------------------------------------------------------------ allocation

        sealed class NullSink : IMarimbaCommandSink { public int N; public bool Post(in GraphCommand c) { N++; return true; } }

        [Test]
        public void Allocation_NoneInTheMainThreadApiOrTheRender()
        {
            using var h = new MarimbaOffline();
            var sink = new NullSink();
            var sound = new MarimbaSound(h.State, sink, 1);
            sound.Strike(3, 0.5f, 0.45f, 0f, MarimbaMallet.Medium);   // warm up (JIT)
            sound.Damp(3, MarimbaDampKind.Hand, 0.5f, 0f);
            sound.Undamp(3);
            Assert.That(() =>
            {
                for (int k = 0; k < 30; k++)
                {
                    sound.Strike(k % 37, 0.15f + 0.02f * k, 0.45f, 0.1f, (MarimbaMallet)(k & 3));
                    sound.Damp(k % 37, (MarimbaDampKind)(k & 1), 0.5f, 0.2f);
                    sound.Undamp(k % 37);
                }
                sound.Silence();
                float l = sound.BarLevel(3);
            }, Is.Not.AllocatingGCMemory());
            for (int k = 0; k < 10; k++) h.Sound.Strike(k, 0.7f, 0.45f, 0f, MarimbaMallet.Hard);
            h.RenderBlock();
            Assert.That(() => { for (int b = 0; b < 20; b++) h.RenderBlock(); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Memory_StateSize()
        {
            long bytes = MarimbaOffline.StateBytes;
            Debug.Log($"[MARIMBA] audio state {bytes} B ({bytes / 1024.0:F1} KiB) in one allocation (spec 151 KiB; platform arena cap 2 MiB), 0 baked");
            Assert.That(bytes, Is.LessThan(2L * 1024 * 1024));
        }
    }
}
