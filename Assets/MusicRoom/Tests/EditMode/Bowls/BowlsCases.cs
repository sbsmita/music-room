// The scripted-hand cases of the bowls lab's interaction-cases.mjs (25, the same scripts and pass rules), plus the
// phase-A review-item cases: a lip first-timer rub on each bowl (item 13), a full hand rubbing the middle bowl on the
// side facing the small one (item 19), and every case again on the app's layout (the big bowl nearest, item 15).
// Each returns pass + a line of measurements. 30 Hz tracker unless stated, 72 Hz display frames, seeded jitter, the unit
// running managed (bit-exact to the reference engine). No NUnit dependency (also runs in the offline mono check).
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Audio.Burst;
using MusicRoom.Bowls;
using Unity.Mathematics;
using static MusicRoom.Tests.Bowls.BowlsSim;

namespace MusicRoom.Tests.Bowls
{
    public struct CaseResult
    {
        public bool Pass; public string Measured;
        public CaseResult(bool pass, string m) { Pass = pass; Measured = m; }
        public override string ToString() => (Pass ? "PASS " : "FAIL ") + Measured;
    }

    public static class BowlsCases
    {
        static readonly string[] Name = { "big", "middle", "small" };
        static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        static string F(double? v) => v.HasValue ? F(v.Value) : "null";

        static SimResult Sim(Placement L, double seconds, Func<double, SimHand[]> hands, double hz = 72, double tracker = 30, double jitter = 0.003, uint seed = 11, bool engine = true, double[] loss = null, bool lipAsOuter = true, bool quiet = true)
            => Simulate(new Opts { Layout = L, Seconds = seconds, HandsAt = hands, Hz = hz, TrackerHz = tracker, Jitter = jitter, Seed = seed, Engine = engine, LossAt = loss, LipAsOuter = lipAsOuter, QuietNeighbours = quiet });

        static void Dispose(SimResult r) => r.Rig?.Dispose();

        // ------------------------------------------------------------------ A. the basics
        public static CaseResult Tap(Placement L)
        {
            var r = Sim(L, 1.0, Hand(Poke(L, 1, v: 0.8, over: 0.015)));
            var s = r.Strikes.ToList();
            bool ok = s.Count == 1 && s[0].V > 0.55 && s[0].V < 1.0 && r.Count("contact") == 0 && !r.Damps.Any(d => d.L != 0);
            Dispose(r);
            return new CaseResult(ok, $"tap: V {string.Join(",", s.Select(q => F(q.V)))} contacts {r.Count("contact")}");
        }

        public static CaseResult FirstTimer(Placement L, int i)
        {
            var r = Sim(L, 6.6, Hand(Circle(L, i)));
            var st = r.FirstEvent(BowlsHandEventType.RubStart);
            var vfs = r.Contacts.Where(c => c.Vf != 0).Select(c => Math.Abs(c.Vf)).ToList();
            double mv = vfs.Count > 0 ? vfs.Average() : 0, tv = 2 * Math.PI * Placed(L)[i].R * 0.7;
            double? sa = FirstSing(r, i, 0.4); double fr = SingFrac(r, i, 2.4, 6.4); int rel = r.Count("release");
            bool ok = st.HasValue && st.Value.T < 1.0 && Math.Abs(mv - tv) < 0.15 * tv && sa.HasValue && sa < 1.5 && fr >= 0.95 && rel == 1;
            Dispose(r);
            return new CaseResult(ok, $"first-timer {Name[i]}: rubStart {F(st?.T)} meanVf {F(mv)} true {F(tv)} sings {F(sa)} frac {F(fr)} releases {rel} touch {string.Join(",", r.Strikes.Select(q => F(q.V)))}");
        }

        public static CaseResult Rest(Placement L)
        {
            var g = Placed(L)[1]; double a = Approach(L, 1);
            Func<double, double3?> p = t => { double rr = g.R + TIP_R + 0.02 - Math.Min(0.026, Math.Max(0, t - 0.2) * 0.05); if (t > 3.0) return null; return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H - 0.01, g.C.z + rr * Math.Sin(a)); };
            var r = Sim(L, 3.4, Hand(p), jitter: 0.0015, tracker: 36);
            var d = r.Damps.ToList(); var first = d.FirstOrDefault(q => q.L > 0);
            BowlsHandEvent? entry = null; foreach (var e in r.Events) if (e.Type == BowlsHandEventType.Touch || e.Type == BowlsHandEventType.Tap) { entry = e; break; }
            bool ok = !r.Strikes.Any(s => s.V > 0.25) && r.Count("contact") == 0 && first != null && entry.HasValue && first.T - entry.Value.T >= 0.6 && !d.Any(q => q.L >= 2) && d.Count > 0 && d[d.Count - 1].L == 0;
            Dispose(r);
            return new CaseResult(ok, $"rest: entry {F(entry?.T)} firstDamp {F(first?.T)} levels {string.Join(",", d.Select(q => q.L))}");
        }

        public static CaseResult TapThenRub(Placement L, int i)
        {
            var g = Placed(L)[i]; double A0 = Approach(L, i), rr = g.R + TIP_R - 0.006;
            Func<double, double3?> p = t =>
            {
                if (t < 0.36) { double r0 = rr + 0.05 - Math.Max(0, t - 0.30) * 0.8; double q = Math.Max(rr, r0); return new double3(g.C.x + q * Math.Cos(A0), g.C.y + g.H - 0.008, g.C.z + q * Math.Sin(A0)); }
                double a = A0 + 2 * Math.PI * 0.6 * Math.Max(0, t - 0.5);
                return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H - 0.008, g.C.z + rr * Math.Sin(a));
            };
            var r = Sim(L, 4, Hand(p));
            double fr = SingFrac(r, i, 1.8, 3.8);
            bool ok = r.CountEvents(BowlsHandEventType.Tap) == 1 && r.Count("strike") == 1 && r.CountEvents(BowlsHandEventType.RubStart) > 0 && fr >= 0.95;
            Dispose(r);
            return new CaseResult(ok, $"tap then rub {Name[i]}: strikes {string.Join(",", r.Strikes.Select(q => F(q.V)))} rubStart {F(r.FirstEvent(BowlsHandEventType.RubStart)?.T)} frac {F(fr)}");
        }

        public static CaseResult Hover(Placement L)
        {
            var g = Placed(L)[1]; double a = Approach(L, 1);
            var r = Sim(L, 1.5, Hand(t => new double3(g.C.x + (g.R + TIP_R + 0.01) * Math.Cos(a), g.C.y + g.H - 0.01, g.C.z + (g.R + TIP_R + 0.01) * Math.Sin(a))), jitter: 0.002, engine: false);
            return new CaseResult(r.Calls.Count == 0, $"hover: calls {r.Calls.Count}");
        }

        public static CaseResult Loss80(Placement L)
        {
            var r = Sim(L, 3.0, Hand(Circle(L, 1, t0: 0.3, t1: 2.7)), loss: new[] { 1.5, 1.58 });
            bool ok = !r.Releases.Any(q => q.T < 2.65) && !r.Strikes.Any(q => q.T > 0.6);
            Dispose(r);
            return new CaseResult(ok, $"loss 80 ms: releases {string.Join(",", r.Releases.Select(q => F(q.T)))}");
        }

        public static CaseResult Loss400(Placement L)
        {
            var r = Sim(L, 3.0, Hand(Circle(L, 1, t0: 0.3, t1: 2.7)), loss: new[] { 1.5, 1.9 });
            bool ok = r.Releases.Count(q => q.T > 1.5 && q.T < 1.8) == 1 && !r.Strikes.Any(q => q.T > 0.6) && r.Contacts.Any(q => q.T > 1.95);
            Dispose(r);
            return new CaseResult(ok, $"loss 400 ms: releases {string.Join(",", r.Releases.Select(q => F(q.T)))} strikesAfter {r.Strikes.Count(q => q.T > 0.6)}");
        }

        public static CaseResult Bounce(Placement L)
        {
            var g = Placed(L)[1]; double a = Approach(L, 1);
            Func<double, double3?> p = t => { double bs = g.R + TIP_R, r0; if (t < 0.2) r0 = bs + 0.05; else if (t < 0.26) r0 = bs + 0.05 - (t - 0.2) * 1.0; else if (t < 0.28) r0 = bs + 0.008; else if (t < 0.30) r0 = bs - 0.006; else r0 = bs + 0.05; return new double3(g.C.x + r0 * Math.Cos(a), g.C.y + g.H - 0.01, g.C.z + r0 * Math.Sin(a)); };
            var r = Sim(L, 0.6, Hand(p), hz: 90, tracker: 90, jitter: 0, engine: false);
            return new CaseResult(r.Count("strike") == 1, $"bounce: strikes {r.Count("strike")}");
        }

        public static CaseResult Glitch(Placement L)
        {
            var g = Placed(L)[1]; double a = Approach(L, 1);
            Func<double, double3?> p = t => { double r0 = t < 0.4 ? g.R + 0.20 : g.R + TIP_R - 0.008; return new double3(g.C.x + r0 * Math.Cos(a), g.C.y + g.H - 0.01, g.C.z + r0 * Math.Sin(a)); };
            var r = Sim(L, 0.8, Hand(p), jitter: 0, engine: false);
            return new CaseResult(r.CountEvents(BowlsHandEventType.Tap) == 0, $"glitch: taps {r.CountEvents(BowlsHandEventType.Tap)}");
        }

        public static CaseResult InnerWall(Placement L)
        {
            var g = Placed(L)[1]; double A0 = Approach(L, 1), w = g.Wall;
            Func<double, double3?> p = t => { double a = A0 + 2 * Math.PI * 0.7 * Math.Max(0, t - 0.3), rr = g.R - w - TIP_R + 0.004 + Math.Max(0, 0.3 - t) * -0.1; return new double3(g.C.x + rr * Math.Cos(a), g.C.y + g.H + (t < 0.25 ? 0.03 - t * 0.12 : -0.012), g.C.z + rr * Math.Sin(a)); };
            var r = Sim(L, 3, Hand(p));
            bool ok = r.Count("contact") > 20 && r.Contacts.All(q => q.Surface == BowlSurface.Inner) && FirstSing(r, 1, 0.3) == null;
            Dispose(r);
            return new CaseResult(ok, $"inner: contacts {r.Count("contact")} kinds {string.Join(",", r.Contacts.Select(q => q.Surface).Distinct())} sings {F(FirstSing(r, 1, 0.3))}");
        }

        public static CaseResult PalmRest(Placement L)
        {
            var g = Placed(L)[1];
            Func<double, SimHand[]> hands = t => { var h = new SimHand { HasPalm = true, PalmC = new double3(g.C.x, g.C.y + g.H + (t < 0.5 ? 0.08 - t * 0.13 : t < 1.5 ? 0.015 : 0.2), g.C.z), PalmN = new double3(0, -1, 0) }; return new[] { h }; };
            var r = Sim(L, 2.4, hands);
            var d = r.Damps.ToList();
            bool ok = d.Count >= 2 && d[0].L == 3 && d[d.Count - 1].L == 0 && r.CountEvents(BowlsHandEventType.PalmStrike) == 0;
            Dispose(r);
            return new CaseResult(ok, $"palm rest: levels {string.Join(",", d.Select(q => q.L))}");
        }

        public static CaseResult TwoHands(Placement L)
        {
            Func<double, double3?> right = t => { double[] ks = { 2.0, 3.0, 4.0 }; foreach (var t0 in ks) if (t >= t0 - 0.1 && t < t0 + 0.2) return Poke(L, 0, v: 0.8, over: 0.01, t0: t0 - 0.08)(t); return null; };
            var left = Circle(L, 2, t1: 4.8);
            Func<double, SimHand[]> hands = t => { var a = new SimHand(); a.Tips[1] = left(t); var b = new SimHand(); b.Tips[1] = right(t); return new[] { a, b }; };
            var r = Sim(L, 5.0, hands);
            double fr = SingFrac(r, 2, 1.0, 4.5); int bs = r.Strikes.Count(q => q.I == 0);
            Dispose(r);
            return new CaseResult(fr >= 0.95 && bs == 3, $"two hands: small frac {F(fr)} big strikes {bs}");
        }

        // ------------------------------------------------------------------ B. gentle taps (H2)
        public static CaseResult Pokes(Placement L, out int fails)
        {
            var rows = new List<(double v, double over, double dwell, int ph, int strikes, double V, int contacts, double ring)>();
            bool all = true; fails = 0;
            foreach (double v in new[] { 0.2, 0.3, 0.5 }) foreach (double over in new[] { 0.01, 0.02, 0.04 }) foreach (double dwell in new[] { 0, 0.1, 0.25 }) for (int ph = 0; ph < 4; ph++)
            {
                var r = Sim(L, 3.0, Hand(Poke(L, 1, v: v, over: over, dwell: dwell, t0: 0.3 + ph * 0.0083)), seed: (uint)(5 + ph), jitter: 0.002);
                var s = r.Strikes.ToList(); double V = s.Count > 0 ? s[0].V : 0;
                double ring = EnergyDb(r, 1, 1.5, 2.5);
                bool ok = s.Count == 1 && V >= 0.75 * v && r.Count("contact") == 0;
                rows.Add((v, over, dwell, ph, s.Count, V, r.Count("contact"), ring));
                if (!ok) { all = false; fails++; }
                Dispose(r);
            }
            double worst = 0;
            foreach (double v in new[] { 0.2, 0.3, 0.5 }) foreach (double over in new[] { 0.01, 0.02, 0.04 })
            {
                var a = rows.Where(q => q.v == v && q.over == over && q.dwell == 0).ToList(); var b = rows.Where(q => q.v == v && q.over == over && q.dwell == 0.25).ToList();
                for (int k = 0; k < a.Count; k++) worst = Math.Max(worst, Math.Abs(b[k].ring - a[k].ring - 20 * Math.Log10(b[k].V / a[k].V)));
            }
            double minR = rows.Min(q => q.V / q.v);
            return new CaseResult(all && worst <= 1.0, $"pokes (108): fails {fails} minV/v {F(minR)} worstRingDiffDb {F(worst)}");
        }

        public static CaseResult TapHold(Placement L)
        {
            Func<double, Func<double, double3?>> holdPath = dwell => Poke(L, 1, v: 0.8, over: 0.012, dwell: dwell, t0: 0.42);
            var bse = Sim(L, 2.6, Hand(holdPath(0)), jitter: 0.002);
            var res = new List<string>(); bool ok = true;
            foreach (double dwell in new[] { 0.12, 0.25, 0.5 })
            {
                var r = Sim(L, 2.6, Hand(holdPath(dwell)), jitter: 0.002);
                var s = r.Strikes.ToList();
                double dB = EnergyDb(r, 1, 1.6, 2.1) - EnergyDb(bse, 1, 1.6, 2.1) - 20 * Math.Log10(s[0].V / bse.Strikes.First().V);
                ok &= s.Count == 1 && Math.Abs(dB) <= 1;
                res.Add($"{F(dwell)}s:{s.Count}/{F(dB)}dB");
                Dispose(r);
            }
            Dispose(bse);
            return new CaseResult(ok, "tap and hold: " + string.Join(" ", res));
        }

        public static CaseResult DoubleTaps(Placement L)
        {
            var res = new List<string>(); bool ok = true;
            foreach (double gap in new[] { 0.15, 0.2, 0.25, 0.35 })
            {
                var p1 = Poke(L, 1, v: 1.0, over: 0.015, t0: 0.4); var p2 = Poke(L, 1, v: 1.0, over: 0.015, t0: 0.4 + gap);
                var r = Sim(L, 2.0, Hand(t => t < 0.4 + gap - 0.02 ? p1(t) : p2(t)), jitter: 0.002, engine: false);
                ok &= r.Count("strike") == 2; res.Add($"{F(gap)}:{r.Count("strike")}");
            }
            return new CaseResult(ok, "double taps: " + string.Join(" ", res));
        }

        // ------------------------------------------------------------------ C. fast taps (M2)
        static int RunFast(Placement L, double hz, double v, double over, int n = 20)
        {
            int hit = 0;
            for (int k = 0; k < n; k++)
            {
                var r = Sim(L, 1.0, Hand(Poke(L, 1, v: v, over: over, t0: 0.3 + k * (1 / hz) / n)), tracker: hz, seed: (uint)(100 + k), jitter: 0.001, engine: false);
                if (r.Count("strike") == 1) hit++;
            }
            return hit;
        }

        public static CaseResult FastTaps(Placement L)
        {
            bool all = true; var rows = new List<string>();
            foreach (double v in new[] { 1.0, 2.0 }) foreach (double over in new[] { 0.005, 0.01, 0.02 }) { int h = RunFast(L, 30, v, over); rows.Add($"30Hz {F(v)}m/s {F(over * 1000)}mm {h}/20"); if (h != 20) all = false; }
            foreach (double over in new[] { 0.005, 0.01, 0.02 }) { int h = RunFast(L, 60, 3, over); rows.Add($"60Hz 3m/s {F(over * 1000)}mm {h}/20"); if (h != 20) all = false; }
            return new CaseResult(all, "fast: " + string.Join("; ", rows));
        }

        public static CaseResult FastTaps3At30(Placement L)
        {
            var rows = new List<string>(); bool ok = true;
            foreach (double over in new[] { 0.005, 0.01, 0.02 }) { int h = RunFast(L, 30, 3, over); rows.Add($"{F(over * 1000)}mm {h}/20"); ok &= h >= 15; }
            return new CaseResult(ok, "fast 3 m/s at 30 Hz (info): " + string.Join(" ", rows));
        }

        // ------------------------------------------------------------------ D. other fingers (H3)
        public static CaseResult Fingers(Placement L, out int fails)
        {
            fails = 0; bool all = true; var bad = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                double R = Placed(L)[i].R; Func<double, double> lead = d => d / R;
                var hands = new (string, Func<double, SimHand[]>)[] {
                    ("index only", Hand(Circle(L, i))),
                    ("two-finger same depth", Hand(Circle(L, i), middle: Circle(L, i, lead: lead(0.019)))),
                    ("two-finger 6 mm shallower", Hand(Circle(L, i), middle: Circle(L, i, lead: lead(0.019), depth: 0.0))),
                    ("flat hand", Hand(Circle(L, i), middle: Circle(L, i, lead: lead(0.019), depth: 0.010), ring: Circle(L, i, lead: lead(0.038), depth: 0.012))),
                    ("curled middle", Hand(Circle(L, i), middle: Circle(L, i, lead: lead(0.012), depth: 0.002, yOff: 0.004, ywob: 0.006))),
                };
                foreach (var (label, h) in hands)
                {
                    var r = Sim(L, 6.6, h, jitter: 0.005, seed: (uint)(17 + i));
                    double? sa = FirstSing(r, i, 0.4); double fr = SingFrac(r, i, 2.4, 6.4);
                    bool ok = sa.HasValue && sa <= 2.0 && fr >= 0.9;
                    if (!ok) { all = false; fails++; bad.Add($"{Name[i]}/{label} sa {F(sa)} fr {F(fr)}"); }
                    Dispose(r);
                }
            }
            return new CaseResult(all, $"fingers (15): fails {fails} {string.Join("; ", bad)}");
        }

        // ------------------------------------------------------------------ E. freehand circles (M1)
        public static CaseResult Freehand(Placement L, out int fails)
        {
            fails = 0; bool all = true; var bad = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var paths = new (string, Func<double, double3?>)[] {
                    ("off-centre 8 mm, ellipse 10 mm", Circle(L, i, offX: 0.008, wob: 0.010, drift: 0.006, ywob: 0.006)),
                    ("ellipse 15 mm, depth 4 mm", Circle(L, i, wob: 0.015, drift: 0.006, ywob: 0.006, depth: 0.004)),
                    ("pressed 15 mm, ellipse 15 mm", Circle(L, i, wob: 0.015, drift: 0.006, ywob: 0.006, depth: 0.015)),
                };
                foreach (var (label, p) in paths)
                {
                    var r = Sim(L, 6.6, Hand(p));
                    double? sa = FirstSing(r, i, 0.4); double fr = SingFrac(r, i, 2.4, 6.4); int rel = r.Count("release");
                    bool ok = sa.HasValue && sa <= 2.0 && rel <= 1 && fr >= 0.9;
                    if (!ok) { all = false; fails++; bad.Add($"{Name[i]}/{label} sa {F(sa)} fr {F(fr)} rel {rel}"); }
                    Dispose(r);
                }
            }
            return new CaseResult(all, $"freehand (9): fails {fails} {string.Join("; ", bad)}");
        }

        // ------------------------------------------------------------------ F. the puja
        public static CaseResult PujaRub(Placement L)
        {
            var g = Placed(L)[1]; double A0 = Approach(L, 1);
            Func<double, SimHand[]> hands = t =>
            {
                var h = new SimHand();
                if (t <= 6.0)
                {
                    double a = A0 + 2 * Math.PI * 0.6 * Math.Max(0, t - 0.4), rr = g.R + BowlsHands.IX.PUJA_R - 0.005 + Math.Max(0, 0.4 - t) * 0.1;
                    double x = g.C.x + rr * Math.Cos(a), z = g.C.z + rr * Math.Sin(a);
                    h.HasTool = true; h.ToolA = new double3(x, g.C.y + g.H + 0.06, z); h.ToolB = new double3(x, g.C.y + g.H - 0.09, z);
                }
                return new[] { h };
            };
            var r = Sim(L, 6.4, hands);
            double? sa = FirstSing(r, 1, 0.4);
            bool toolRub = r.Events.Any(e => e.Type == BowlsHandEventType.RubStart && e.Tool);
            bool ok = sa.HasValue && sa <= 1.5 && !r.Strikes.Any(q => q.Kind == BowlStrikeKind.Suede) && toolRub;
            Dispose(r);
            return new CaseResult(ok, $"puja shaft rub: sings {F(sa)} frac {F(SingFrac(r, 1, 2, 5.8))} strikes {string.Join(" ", r.Strikes.Select(q => q.Kind + ":" + F(q.V)))}");
        }

        public static CaseResult PujaSuede(Placement L)
        {
            var g = Placed(L)[1];
            var pk = Poke(L, 1, v: 1.0, over: 0.01, t0: 0.4, yOff: -0.012);
            Func<double, SimHand[]> hands = t =>
            {
                double3 p = pk(t).Value; double3 d = new double3(p.x - g.C.x, 0, p.z - g.C.z); double Ld = Math.Sqrt(d.x * d.x + d.z * d.z);
                var h = new SimHand { HasTool = true, ToolA = p, ToolB = new double3(p.x + 0.15 * d.x / Ld * 0.5, p.y - 0.13, p.z + 0.15 * d.z / Ld * 0.5) };
                return new[] { h };
            };
            var r = Sim(L, 1.2, hands, engine: false);
            var s = r.Strikes.ToList();
            bool ok = s.Count == 1 && s[0].Kind == BowlStrikeKind.Suede && s[0].V > 0.7 && s[0].V < 1.1;
            return new CaseResult(ok, $"suede strike: {string.Join(" ", s.Select(q => q.Kind + ":" + F(q.V)))}");
        }

        // ------------------------------------------------------------------ phase A: review items
        /// <summary>Item 13: a first-timer circling ON TOP of the rim (the wine-glass gesture): it must sing like the outside.</summary>
        public static CaseResult LipFirstTimer(Placement L, int i, bool lipAsOuter = true)
        {
            // the fingertip pad rests on the lip from above: centre 2 mm into the virtual lip, over the rim line
            var g = Placed(L)[i]; double A0 = Approach(L, i);
            Func<double, double3?> p = t =>
            {
                if (t > 6.4) return null;
                double tt = Math.Max(0, t - 0.4), a = A0 + 2 * Math.PI * 0.7 * tt, rr = g.R - g.Wall * 0.5;
                double y = g.C.y + g.H + TIP_R - 0.002 + (t < 0.4 ? Math.Min(0.03, (0.4 - t) * 0.1) : 0);
                return new double3(g.C.x + rr * Math.Cos(a), y, g.C.z + rr * Math.Sin(a));
            };
            var r = Sim(L, 6.6, Hand(p), lipAsOuter: lipAsOuter);
            var st = r.FirstEvent(BowlsHandEventType.RubStart);
            double? sa = FirstSing(r, i, 0.4); double fr = SingFrac(r, i, 2.4, 6.4); int rel = r.Count("release");
            bool onLip = st.HasValue && st.Value.Surface == BowlSurface.Lip;
            bool ok = onLip && sa.HasValue && sa < 1.5 && fr >= 0.95 && rel == 1;
            Dispose(r);
            return new CaseResult(ok, $"lip first-timer {Name[i]} (lipAsOuter {lipAsOuter}): surface {(st.HasValue ? st.Value.Surface.ToString() : "-")} sings {F(sa)} frac {F(fr)} releases {rel} unitKinds {string.Join(",", r.Contacts.Select(c => c.Surface).Distinct())}");
        }

        /// <summary>Item 19: a full 5-tip hand rubbing the middle bowl on the side facing the small one: no call reaches the
        /// small bowl (the thumb, ring and little tips sweep through its band).</summary>
        public static CaseResult FullHandNeighbour(Placement L, bool quiet = true)
        {
            var G = Placed(L); var m = G[1]; var s = G[2];
            // the index circles the middle bowl at 0.7 turns/s; the other tips trail the hand: the middle 19 mm behind on the
            // rim, the ring and little tips splayed outward by the gap (+4 / +8 mm): each turn, as the hand passes the side
            // facing the small bowl, they sweep through the small bowl's rub band and wall
            double R = m.R, A0 = Approach(L, 1);
            double gap = Math.Sqrt((s.C.x - m.C.x) * (s.C.x - m.C.x) + (s.C.z - m.C.z) * (s.C.z - m.C.z)) - m.R - s.R;
            Func<double, double3?> tipAt(double lead, double outward, double depth, double dy) => t =>
            {
                if (t > 6.4) return null;
                double a = A0 + 2 * Math.PI * 0.7 * Math.Max(0, t - 0.4) - lead / R, rr = R + TIP_R - depth + outward;
                if (t < 0.4) rr += Math.Min(0.03, (0.4 - t) * 0.1);
                return new double3(m.C.x + rr * Math.Cos(a), m.C.y + m.H - 0.008 + dy, m.C.z + rr * Math.Sin(a));
            };
            var hand = Hand(tipAt(0, 0, 0.006, 0), thumb: tipAt(-0.03, 0.012, 0, -0.02), middle: tipAt(0.019, 0, 0.004, 0),
                ring: tipAt(0.038, gap + 0.004, 0, -0.004), little: tipAt(0.055, gap + 0.008, 0, -0.012));
            var r = Sim(L, 6.6, hand, lipAsOuter: true, quiet: quiet);
            int toSmallCalls = r.Calls.Count(c => c.I == 2);
            double fr = SingFrac(r, 1, 2.4, 6.4);
            Dispose(r);
            return new CaseResult(toSmallCalls == 0 && fr >= 0.9, $"full hand by the small bowl: calls to the small bowl {toSmallCalls} ({string.Join(",", r.Calls.Where(c => c.I == 2).Select(c => c.C).Distinct())}) middle frac {F(fr)} gap {F(gap * 1000)} mm");
        }
    }
}
