// MarimbaPlay under realistic VR strokes (spec 9.2 "Realistic strokes"; the marimba lab's ix-realistic.mjs transcribed
// with the same seeded paths): no bar stops the hand, so a stroke decelerates and turns around under the strike plane;
// tracking adds AR(1) jitter, magnified at the mallet head by the 27 cm lever. The eight acceptance checks of the
// reference with its pass marks, and the measured numbers logged next to the lab's (Reference~/ix-realistic.json).
// Real Quest hand tracking was also kept in mind: fast strokes skip samples (the 72 / 90 Hz hard strokes, a 10 cm jump
// per sample still plays), and low-confidence samples never strike.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio.Burst;
using MusicRoom.Marimba;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using MusicRoom.Tests.Audio;
using static MusicRoom.Tests.Audio.MarimbaRef;

namespace MusicRoom.Tests.Marimba
{
    public class MarimbaRealisticTests
    {
        uint _seed;
        double Rnd() { _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5; return _seed / 4294967296.0; }
        double Gauss() { double u = 0; while (u == 0) u = Rnd(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * Rnd()); }

        sealed class Jitter
        {
            readonly MarimbaRealisticTests _t; readonly double _s, _rho; readonly double[] _st = new double[3];
            public Jitter(MarimbaRealisticTests t, double s, double rho) { _t = t; _s = s; _rho = rho; }
            public double3 Next() { for (int a = 0; a < 3; a++) _st[a] = _rho * _st[a] + Math.Sqrt(1 - _rho * _rho) * _s * _t.Gauss(); return new double3(_st[0], _st[1], _st[2]); }
        }

        struct StrokeH
        {
            public double T, TA, TB, TD, V, D, Dwell, Vr, H0;
            public double H(double t) =>
                t < TA ? H0 - V * t : t < TA + TB ? -(V * (t - TA) - 0.5 * (V / TB) * (t - TA) * (t - TA)) : t < TA + TB + Dwell ? -D : t < TA + TB + Dwell + TD ? -D + Vr * (t - TA - TB - Dwell) : H0;
        }

        static StrokeH Stroke(double v, double d, double dwell, double vr, double h0 = 0.08)
        {
            double tA = h0 / v, tB = 2 * d / v, tD = (h0 + d) / vr;
            return new StrokeH { T = tA + tB + dwell + tD + 0.15, TA = tA, TB = tB, TD = tD, V = v, D = d, Dwell = dwell, Vr = vr, H0 = h0 };
        }

        static int Bar(string n) => MarimbaGeometry.BarOf(n);
        static double3 At(int b, double u, double c, double h, bool finger = false) =>
            new double3(MarimbaTables.X[b] + c * MarimbaTables.Width[b] / 2, MarimbaTables.YTop[b] + (finger ? MarimbaIx.FingerR : MarimbaIx.HeadR) + h, MarimbaTables.Z[b] - MarimbaTables.Length[b] / 2 + u * MarimbaTables.Length[b]);

        sealed class Path { public string Id, Owner; public bool Finger; public Func<double, double3?> Pos; }

        /// <summary>ix-realistic.mjs run(): every path sampled at fps from -lead s, events with t re the stroke's start.</summary>
        static List<MarimbaEventLog.E> Run(Path[] paths, double T, double fps = 90, bool deadStrokes = true, double lead = 0.1)
        {
            var owners = new List<string>();
            var ptrs = new MarimbaPointer[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                if (!owners.Contains(paths[i].Owner)) owners.Add(paths[i].Owner);
                ptrs[i] = new MarimbaPointer(paths[i].Id, owners.IndexOf(paths[i].Owner), paths[i].Finger, MarimbaPlayTests.TipOf(paths[i].Id));
            }
            var log = new MarimbaEventLog();
            var play = new MarimbaPlay(log) { DeadStrokes = deadStrokes };
            for (int k = 0; k * (1000 / fps) <= (T + lead) * 1000; k++)
            {
                double t = k / fps - lead, now = k * 1000 / fps;
                for (int i = 0; i < paths.Length; i++)
                {
                    var p = ptrs[i];
                    p.Advance();
                    double3? pos = paths[i].Pos(Math.Max(0, t));
                    if (pos == null) p.Sample(false, default);
                    else
                    {
                        double3 d = pos.Value - p.Prev;
                        p.Sample(true, pos.Value, p.HasPrev ? Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z) * fps : 0);
                    }
                }
                play.Tick(ptrs, ptrs.Length, 1 / fps, now);
            }
            for (int i = 0; i < log.Events.Count; i++) { var e = log.Events[i]; e.T -= lead * 1000; log.Events[i] = e; }
            return log.Events;
        }

        Path Mallet(int b, StrokeH s, Jitter J, double u = 0.45) =>
            new Path { Id = "m", Owner = "malletR", Pos = t => { double3 j = J.Next(); return At(b, u, 0, s.H(t)) + j; } };

        static int Count(List<MarimbaEventLog.E> ev, string type) { int n = 0; foreach (var e in ev) if (e.Type == type) n++; return n; }
        static Dictionary<string, object> Lab => Obj(MarimbaJson.Parse(File.ReadAllText(System.IO.Path.Combine(MarimbaPlayTests.Dir, "ix-realistic.json"))));

        [Test]
        public void Realistic_TheEightChecksOfTheLab()
        {
            _seed = 0x5a17e5;
            var lab = Lab;
            var report = new List<string>();
            // 1. ordinary strokes never become dead strokes (2 160 strokes)
            {
                int b = Bar("E4"), n = 0, dead = 0, dead60 = 0, missed = 0, doubles = 0;
                foreach (double sig in new[] { 0, 0.002, 0.004, 0.006 }) foreach (double v in new[] { 0.6, 1.0, 1.5, 2.5, 4.0 }) foreach (double d in new[] { 0.02, 0.04, 0.07 })
                    foreach (double dwell in new[] { 0, 0.06, 0.1 }) foreach (double vr in new[] { 0.4, 1.0 }) for (int k = 0; k < 6; k++)
                    {
                        var s = Stroke(v, d, dwell, vr);
                        var ev = Run(new[] { Mallet(b, s, new Jitter(this, sig, 0.6)) }, s.T);
                        n++; int st = Count(ev, "strike");
                        if (st == 0) missed++; if (st > 1) doubles++;
                        if (Count(ev, "damp") > 0) { dead++; if (dwell <= 0.06) dead60++; }
                    }
                var l = Obj(lab, "ordinary");
                report.Add($"1 ordinary strokes {n}: dead {dead} ({100.0 * dead / n:F2} %), with a pause <= 60 ms {dead60}, missed {missed}, doubles {doubles} (lab: dead {Num(l, "deadStrokes")}, missed {Num(l, "missed")}, doubles {Num(l, "doubles")})");
                Assert.That(dead60, Is.EqualTo(0)); Assert.That(dead / (double)n, Is.LessThanOrEqualTo(0.005)); Assert.That(missed, Is.EqualTo(0)); Assert.That(doubles, Is.EqualTo(0));
            }
            // 2. deliberate dead strokes fire
            {
                int b = Bar("G4");
                var rows = new List<string>();
                foreach (double sig in new[] { 0, 0.002, 0.004, 0.006 }) foreach (double d in new[] { 0.0, 0.005, 0.01, 0.015 })
                {
                    int ok = 0, n = 0;
                    foreach (double v in new[] { 0.8, 1.5, 2.5 }) foreach (double hold in new[] { 0.3, 0.45 }) for (int k = 0; k < 8; k++)
                    {
                        var s = Stroke(v, Math.Max(d, 0.001), hold, 0.8);
                        var ev = Run(new[] { Mallet(b, s, new Jitter(this, sig, 0.6)) }, s.T);
                        n++;
                        bool st = false, dm = false;
                        foreach (var e in ev) { if (e.Type == "strike") st = true; if (e.Type == "damp" && e.Dead) dm = true; }
                        if (st && dm) ok++;
                    }
                    double fired = ok / (double)n;
                    rows.Add($"{sig * 1000:F0}mm/{d * 1000:F0}mm {fired:F3}");
                    Assert.That(fired, Is.GreaterThanOrEqualTo(sig >= 0.006 || d >= 0.015 ? 0.85 : 0.95), $"jitter {sig} press {d}");
                }
                report.Add("2 deliberate dead strokes fired (jitter/press share): " + string.Join(", ", rows));
            }
            // 3. a resting mallet: no phantom strikes after it settles; one mute, never toggled
            {
                int b = Bar("G4");
                var rows = new List<string>();
                foreach (double sig in new[] { 0.002, 0.004, 0.006, 0.008 }) foreach (double depth in new[] { 0, -0.01 })
                {
                    var J = new Jitter(this, sig, 0.5);
                    var ev = Run(new[] { new Path { Id = "m", Owner = "malletR", Pos = t => { double h = Math.Max(depth, 0.05 - 0.2 * t); double3 j = t > 0.25 ? J.Next() : default; return At(b, 0.45, 0, h) + j; } } }, 60);
                    int phantom = 0; foreach (var e in ev) if (e.Type == "strike" && e.T >= 600) phantom++;
                    int damps = Count(ev, "damp"), undamps = Count(ev, "undamp");
                    rows.Add($"{sig * 1000:F0}mm/{depth * 1000:F0}mm phantom {phantom} damps {damps} undamps {undamps}");
                    if (sig <= 0.006) { Assert.That(phantom, Is.EqualTo(0), $"jitter {sig}"); Assert.That(damps, Is.EqualTo(1), $"jitter {sig}"); Assert.That(undamps, Is.EqualTo(0), $"jitter {sig}"); }
                }
                report.Add("3 resting mallet 60 s: " + string.Join(", ", rows));
            }
            // 4. hard strokes 4-10 m/s sound once at vel 1 (72 and 90 Hz: fast strokes skip samples)
            {
                int b = Bar("C4");
                foreach (double fps in new[] { 72.0, 90.0 }) foreach (double v in new[] { 4.0, 6, 8, 9, 10 })
                {
                    var s = Stroke(v, 0.08, 0, 1.5, 0.2);
                    var ev = Run(new[] { Mallet(b, s, new Jitter(this, 0.002, 0.6)) }, s.T, fps);
                    Assert.That(Count(ev, "strike"), Is.EqualTo(1), $"{v} m/s at {fps} Hz");
                    foreach (var e in ev) if (e.Type == "strike") Assert.That(e.Vel, Is.EqualTo(1.0), $"{v} m/s at {fps} Hz");
                }
                report.Add("4 hard strokes 4-10 m/s at 72 / 90 Hz: one strike each at vel 1");
            }
            // 5. a fingertip tap-and-stay never mutes its own note
            {
                int b = Bar("E4");
                foreach (double depth in new[] { 0, -0.005, -0.012, -0.02 }) foreach (int tips in new[] { 1, 2, 3 })
                {
                    double HT(double t) => Math.Max(depth, 0.05 - 0.6 * t);
                    var paths = new List<Path> { new Path { Id = "indexR", Owner = "handR", Finger = true, Pos = t => At(b, 0.45, -0.2, HT(t), true) } };
                    if (tips >= 2) paths.Add(new Path { Id = "middleR", Owner = "handR", Finger = true, Pos = t => At(b, 0.52, 0.45, HT(t) + 0.004, true) });
                    if (tips >= 3) paths.Add(new Path { Id = "ringR", Owner = "handR", Finger = true, Pos = t => At(b, 0.58, 0.8, HT(t) + 0.008, true) });
                    var ev = Run(paths.ToArray(), 0.8);
                    Assert.That(Count(ev, "strike"), Is.EqualTo(1), $"depth {depth} tips {tips}");
                    if (tips <= 2) Assert.That(Count(ev, "damp"), Is.EqualTo(0), $"depth {depth} tips {tips}: no self-mute");
                }
                report.Add("5 fingertip tap-and-stay (1-3 tips, 0-20 mm deep): one strike, no self-mute with 1-2 tips");
            }
            // 6. a flat hand (3-4 tips) lowered at 0.08-0.6 m/s mutes the bar
            {
                int b = Bar("D4");
                string[] names = { "indexL", "middleL", "ringL", "littleL" };
                foreach (double v in new[] { 0.08, 0.3, 0.6 }) foreach (int tips in new[] { 3, 4 })
                {
                    var paths = new Path[tips];
                    for (int k = 0; k < tips; k++) { int kk = k; paths[k] = new Path { Id = names[k], Owner = "handL", Finger = true, Pos = t => At(b, 0.4 + 0.07 * kk, -0.6 + 0.4 * kk, Math.Max(-0.004, 0.05 - v * t) + 0.002 * kk, true) }; }
                    var ev = Run(paths, 0.05 / v + 0.5);
                    bool damped = false; foreach (var e in ev) if (e.Type == "damp" && e.Kind == "hand") damped = true;
                    Assert.That(damped, Is.True, $"{v} m/s, {tips} tips");
                }
                report.Add("6 flat hand 3-4 tips at 0.08-0.6 m/s: muted every time");
            }
            // 7. the neighbouring fingertip: index aimed at E4 with 11 mm error, the middle tip 19 mm beside it
            {
                var rows = new List<string>();
                int b = Bar("E4");
                foreach (double dy in new[] { -0.008, -0.004, 0, 0.004 })
                {
                    int wrong = 0, right = 0, none = 0;
                    for (int k = 0; k < 400; k++)
                    {
                        double ex = Gauss() * 0.011, side = Rnd() < 0.5 ? 1 : -1, ph = Rnd() / 90;
                        double H(double t) => 0.06 - 0.6 * (t + ph);
                        var ev = Run(new[]
                        {
                            new Path { Id = "idx", Owner = "handR", Finger = true, Pos = t => At(b, 0.45, 0, H(t), true) + new double3(ex, 0, 0) },
                            new Path { Id = "mid", Owner = "handR", Finger = true, Pos = t => At(b, 0.47, 0, H(t) + dy, true) + new double3(ex + side * 0.019, 0, 0) },
                        }, 0.2);
                        int idxBar = -1;
                        for (int i = 0; i < MarimbaGeometry.Bars; i++)
                            if (MarimbaTables.Natural[i] && Math.Abs(MarimbaTables.X[b] + ex - MarimbaTables.X[i]) <= MarimbaTables.Width[i] / 2 + MarimbaIx.SideReach) { idxBar = i; break; }
                        int strikes = Count(ev, "strike");
                        int sb = -1; foreach (var e in ev) if (e.Type == "strike") { sb = e.Bar; break; }
                        if (strikes == 0) none++; else if (strikes == 1 && sb == (idxBar >= 0 ? idxBar : b)) right++; else wrong++;
                    }
                    rows.Add($"middle {-dy * 1000:F0} mm lower: index bar {right / 400.0:F4}, other {wrong / 400.0:F4}, none {none / 400.0:F4}");
                    Assert.That(right / 400.0, Is.GreaterThanOrEqualTo(0.99), $"middle tip {-dy * 1000} mm lower");
                }
                report.Add("7 neighbouring tip: " + string.Join("; ", rows));
            }
            // 8. a sharp's tip aimed at u .04 with 11 mm along-bar error rarely misses (END_REACH)
            {
                int b = Bar("F#4"), miss = 0, n = 0;
                var us = new List<double>();
                for (int k = 0; k < 400; k++)
                {
                    double ez = Gauss() * 0.011;
                    var s = Stroke(1.5, 0.03, 0, 1);
                    var ev = Run(new[] { new Path { Id = "m", Owner = "malletR", Pos = t => At(b, 0.04, 0, s.H(t)) + new double3(0, 0, ez) } }, s.T);
                    n++;
                    bool hit = false; foreach (var e in ev) if (e.Type == "strike" && e.Bar == b) { hit = true; us.Add(e.U); break; }
                    if (!hit) miss++;
                }
                us.Sort();
                var l = Obj(lab, "sharpTip");
                report.Add($"8 sharp tip u .04 with 11 mm error: missed {miss} / {n} (lab {Num(l, "missedBar")}), u median {us[us.Count / 2]:F3} (lab {Num(l, "uMedian"):F3})");
                Assert.That(miss / (double)n, Is.LessThan(0.15));
            }
            foreach (var r in report) Debug.Log("[MARIMBA] realistic " + r);
        }
    }
}
