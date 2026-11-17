// Scripted hands for BowlsHands (the bowls lab's hands-sim.mjs, transliterated): the tracker delivers new samples at
// trackerHz with seeded i.i.d. jitter; display frames (hz) repeat the last sample with its sample time; BowlsHands then
// emulates the runtime's display-time prediction. The unit (BowlsRig, managed render) gets the module's calls at the
// frame's audio time. Layout: the reference's (layout.mjs as reviewed) or the app's (BowlsLayout, big bowl nearest).
// No NUnit / Unity dependency (also builds into the offline mono check).
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using MusicRoom.Bowls;
using MusicRoom.Tests.Audio;
using Unity.Mathematics;

namespace MusicRoom.Tests.Bowls
{
    public sealed class SimHand
    {
        public readonly double3?[] Tips = new double3?[5];   // 0 thumb, 1 index, 2 middle, 3 ring, 4 little
        public bool HasPalm; public double3 PalmC, PalmN;
        public bool HasTool; public double3 ToolA, ToolB;
    }

    public sealed class SimCall
    {
        public double T; public string C; public int I; public BowlStrikeKind Kind; public BowlSurface Surface; public double V, N, Vf, Angle, Omega, Hs; public int L;
    }

    public sealed class SimResult
    {
        public readonly List<SimCall> Calls = new List<SimCall>();
        public readonly List<BowlsHandEvent> Events = new List<BowlsHandEvent>();
        public readonly List<float[]> Sv = new List<float[]>();
        public readonly List<double[]> Rms = new List<double[]>();
        public BowlsRig Rig;
        public BowlsHands Hands;
        /// <summary>The unit's output, every block in order (Opts.KeepAudio; null otherwise).</summary>
        public List<float> Audio;
        public IEnumerable<SimCall> Strikes { get { foreach (var c in Calls) if (c.C == "strike") yield return c; } }
        public IEnumerable<SimCall> Contacts { get { foreach (var c in Calls) if (c.C == "contact") yield return c; } }
        public IEnumerable<SimCall> Releases { get { foreach (var c in Calls) if (c.C == "release") yield return c; } }
        public IEnumerable<SimCall> Damps { get { foreach (var c in Calls) if (c.C == "damp") yield return c; } }
        public int Count(string c) { int n = 0; foreach (var x in Calls) if (x.C == c) n++; return n; }
        public int CountEvents(BowlsHandEventType t) { int n = 0; foreach (var e in Events) if (e.Type == t) n++; return n; }
        public BowlsHandEvent? FirstEvent(BowlsHandEventType t) { foreach (var e in Events) if (e.Type == t) return e; return null; }
    }

    public static class BowlsSim
    {
        public const int SR = 48000;
        public static readonly double3 FAR = new double3(9, 9, 9);

        public enum Placement { Reference, App }

        public struct BowlGeo { public double3 C; public double R, H, Wall; }

        /// <summary>The bowls at their home in the seat frame (yaw 0): base centres, radius, height, wall.</summary>
        public static BowlGeo[] Placed(Placement layout)
        {
            var g = new BowlGeo[3];
            double hx = -0.46, hz = 0.25;
            double[] refX = { -0.100, 0.105, 0.012 }, refZ = { -0.0575, -0.0575, 0.082 };
            for (int i = 0; i < 3; i++)
            {
                double bx = layout == Placement.Reference ? refX[i] : BowlsLayout.BowlX[i], bz = layout == Placement.Reference ? refZ[i] : BowlsLayout.BowlZ[i];
                g[i] = new BowlGeo { C = new double3(hx + bx, BowlsLayout.BaseYD, hz + bz), R = BowlsLayout.RimDiamD[i] / 2, H = BowlsLayout.HeightD[i], Wall = BowlsLayout.WallD[i] };
            }
            return g;
        }

        public static BowlsHandBowl[] HandBowls(Placement layout)
        {
            var g = Placed(layout);
            var b = new BowlsHandBowl[3];
            for (int i = 0; i < 3; i++) b[i] = new BowlsHandBowl { Pos = g[i].C, Yaw = 0, R = g[i].R, H = g[i].H, T = g[i].Wall, Index = i };
            return b;
        }

        public static double Facing(Placement layout, int i) { var c = Placed(layout)[i].C; return Math.Atan2(-c.z, -c.x); }

        /// <summary>The rim angle a scripted finger approaches bowl i from: the reference layout keeps the reference's
        /// facing angle (toward the head); the app layout takes the angle nearest the facing one whose 7 cm radial approach
        /// keeps 2 cm from the other bowls (the big bowl now stands on the head side of the middle and small ones).</summary>
        public static double Approach(Placement layout, int i)
        {
            double f = Facing(layout, i);
            if (layout == Placement.Reference) return f;
            var g = Placed(layout);
            for (int k = 0; k <= 60; k++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    double a = f + sgn * k * 0.05;
                    bool ok = true;
                    for (double d = 0; d <= 0.07 && ok; d += 0.005)
                    {
                        double r = g[i].R + TIP_R + d, x = g[i].C.x + r * Math.Cos(a), z = g[i].C.z + r * Math.Sin(a);
                        for (int j = 0; j < 3 && ok; j++) if (j != i && Math.Sqrt((x - g[j].C.x) * (x - g[j].C.x) + (z - g[j].C.z) * (z - g[j].C.z)) < g[j].R + TIP_R + 0.02) ok = false;
                    }
                    if (ok) return a;
                }
            return f;
        }

        sealed class Sink : IBowlsHandsSink
        {
            public double TNow; public SimResult R; public BowlsRig E; public Action<string, double, SimCall> OnCall;
            long At => (long)BowlsConst.JsRound(TNow * SR);
            public void Strike(int i, BowlStrikeKind kind, double V, double angle, double hs)
            {
                var c = new SimCall { T = TNow, C = "strike", I = i, Kind = kind, V = V, Angle = angle, Hs = hs }; R.Calls.Add(c);
                E?.Strike(i, kind, V, angle, hs, At); OnCall?.Invoke("strike", TNow, c);
            }
            public void Contact(int i, BowlSurface kind, double N, double vf, double angle, double omega, double hs)
            {
                var c = new SimCall { T = TNow, C = "contact", I = i, Surface = kind, N = N, Vf = vf, Angle = angle, Omega = omega, Hs = hs }; R.Calls.Add(c);
                E?.Contact(i, (int)kind, N, vf, angle, omega, hs, 0, 0, At); OnCall?.Invoke("contact", TNow, c);
            }
            public void Release(int i) { var c = new SimCall { T = TNow, C = "release", I = i }; R.Calls.Add(c); E?.Release(i, At, 0); OnCall?.Invoke("release", TNow, c); }
            public void Damp(int i, int L) { var c = new SimCall { T = TNow, C = "damp", I = i, L = L }; R.Calls.Add(c); E?.Damp(i, L, At); OnCall?.Invoke("damp", TNow, c); }
            public void Event(in BowlsHandEvent e) => R.Events.Add(e);
        }

        public sealed class Opts
        {
            public double Seconds; public double Hz = 72, TrackerHz = 30; public Func<double, SimHand[]> HandsAt;
            public bool Engine = true; public double Jitter = 0.003; public uint Seed = 11; public double[] LossAt;
            public Placement Layout = Placement.Reference; public bool LipAsOuter = true, QuietNeighbours = true, RecoilGuard = true;
            /// <summary>BowlsHands.RubSmoothing; KeepAudio: SimResult.Audio holds the unit's output.</summary>
            public bool RubSmoothing = true, KeepAudio;
            /// <summary>The unit's soft pad on outer contacts (BowlsDesign.SetPad), as the app's unit has it.</summary>
            public bool Pad = true;
            /// <summary>BowlsHands.SmoothContact.</summary>
            public bool SmoothContact = true;
            /// <summary>The unit's tonal rub noise (BowlsDesign.SetRubNoise), as the app's unit has it.</summary>
            public bool TonalNoise = true;
            /// <summary>Overrides of the pad's blend and low-pass (Hz), when PadHz > 0.</summary>
            public double PadMix, PadHz, PadN;
            public Action<string, double, SimCall> OnCall; public BowlsPath Path = BowlsPath.Managed;
            /// <summary>Live: the unit renders as the frames advance (never past a frame's own sample, so every call lands on
            /// the same sample as in the batch run), and OnFrame(t, hands, result) runs after each frame (the module's Tick).</summary>
            public Action<double, BowlsHands, SimResult> OnFrame;
            /// <summary>Runs on each display frame's hand inputs just before the hand logic (a held tool posed per frame, as the
            /// app's StickPoses phase does before the bowls' Tick).</summary>
            public Action<double, BowlsHandInput[]> BeforeFrame;
            /// <summary>Runs after every rendered 256-frame block (the unit's per-block stats).</summary>
            public Action<BowlsRig> AfterBlock;
        }

        public static SimResult Simulate(Opts o)
        {
            var res = new SimResult();
            if (o.KeepAudio) res.Audio = new List<float>((int)((o.Seconds + 0.4) * SR));
            var g = Placed(o.Layout);
            BowlsRig eng = null;
            if (o.Engine)
            {
                eng = new BowlsRig(1, o.Path);
                for (int b = 0; b < 3; b++) eng.Pickup(b, Math.Atan2(-g[b].C.z, -g[b].C.x), 0, true);
                unsafe { if (o.Pad && o.PadHz > 0) BowlsDesign.SetPad(eng.S, SR, o.PadMix, o.PadHz, o.PadN); else BowlsDesign.SetPad(eng.S, SR, o.Pad); BowlsDesign.SetRubNoise(eng.S, SR, o.TonalNoise); }
            }
            var sink = new Sink { R = res, E = eng, OnCall = o.OnCall };
            var ix = new BowlsHands(HandBowls(o.Layout), sink) { LipAsOuter = o.LipAsOuter, QuietNeighbours = o.QuietNeighbours, RecoilGuard = o.RecoilGuard, RubSmoothing = o.RubSmoothing, SmoothContact = o.SmoothContact };
            res.Hands = ix; res.Rig = eng;
            uint rng = o.Seed;
            double Rnd() { unchecked { rng = rng * 1664525u + 1013904223u; } return rng / 4294967296.0 - 0.5; }
            double3? Jit(double3? p) { if (!p.HasValue) return null; double x = p.Value.x + o.Jitter * 2 * Rnd(), y = p.Value.y + o.Jitter * 2 * Rnd(), z = p.Value.z + o.Jitter * 2 * Rnd(); return new double3(x, y, z); }
            var frames = new[] { new BowlsHandInput(), new BowlsHandInput() };
            SimHand[] held = null; double sT = -1;
            int nFrames = (int)BowlsConst.JsRound(o.Seconds * o.Hz);
            for (int k = 0; k < nFrames; k++)
            {
                double t = k / o.Hz; sink.TNow = t;
                double st = Math.Floor(t * o.TrackerHz) / o.TrackerHz;
                if (st != sT)
                {
                    sT = st;
                    SimHand[] src = o.HandsAt(st);
                    held = new SimHand[src.Length];
                    for (int h = 0; h < src.Length; h++)
                    {
                        var d = new SimHand();
                        for (int f = 0; f < 5; f++) d.Tips[f] = Jit(src[h].Tips[f]);
                        d.HasPalm = src[h].HasPalm; d.PalmC = src[h].PalmC; d.PalmN = src[h].PalmN;
                        if (src[h].HasTool) { d.HasTool = true; d.ToolA = Jit(src[h].ToolA).Value; d.ToolB = Jit(src[h].ToolB).Value; }
                        held[h] = d;
                    }
                }
                bool lost = o.LossAt != null && t >= o.LossAt[0] && t < o.LossAt[1];
                for (int h = 0; h < 2; h++)
                {
                    BowlsHandInput f = frames[h];
                    f.SampleTime = st; f.Predicted = false; f.HasTipVel = false;
                    if (h < held.Length)
                    {
                        SimHand s = held[h];
                        bool any = s.HasPalm || s.HasTool; for (int q = 0; q < 5; q++) any |= s.Tips[q].HasValue;
                        f.Tracked = !lost && any;
                        for (int q = 0; q < 5; q++) { f.Tips[q] = s.Tips[q] ?? FAR; f.TipValid[q] = true; }
                        f.HasPalm = true; f.PalmC = s.HasPalm ? s.PalmC : FAR; f.PalmN = s.HasPalm ? s.PalmN : new double3(0, 1, 0);
                        f.HasTool = s.HasTool; f.ToolA = s.ToolA; f.ToolB = s.ToolB;
                    }
                    else
                    {
                        f.Tracked = false; for (int q = 0; q < 5; q++) { f.Tips[q] = FAR; f.TipValid[q] = true; }
                        f.HasPalm = false; f.HasTool = false;
                    }
                }
                if (o.OnFrame != null && eng != null) while (eng.Now + 256 <= (long)BowlsConst.JsRound(t * SR)) Block(eng, res, o.AfterBlock);
                o.BeforeFrame?.Invoke(t, frames);
                ix.Frame(t, frames);
                o.OnFrame?.Invoke(t, ix, res);
            }
            if (eng != null)
            {
                int nB = (int)Math.Ceiling((o.Seconds + 0.3) * SR / 256);
                while (res.Sv.Count < nB) Block(eng, res, o.AfterBlock);
            }
            return res;
        }

        static void Block(BowlsRig eng, SimResult res, Action<BowlsRig> after = null)
        {
            eng.RenderBlock(256);
            after?.Invoke(eng);
            if (res.Audio != null) for (int n = 0; n < 256; n++) res.Audio.Add(eng.Mix[n]);
            var sv = new float[3]; var rms = new double[3];
            for (int b = 0; b < 3; b++) { unsafe { sv[b] = eng.B(b)->StSingVel; } double e = 0; for (int n = 0; n < 256; n++) e += (double)eng.Bowl[b][n] * eng.Bowl[b][n]; rms[b] = e / 256; }
            res.Sv.Add(sv); res.Rms.Add(rms);
        }

        // ---- analysis (hands-sim.mjs) ----
        public static double? FirstSing(SimResult r, int i, double from, double thr = 0.15)
        {
            for (int k = (int)Math.Floor(from * SR / 256); k < r.Sv.Count; k++) if (r.Sv[k][i] > thr) return Math.Round((k * 256.0 / SR - from) * 100) / 100;
            return null;
        }
        public static double SingFrac(SimResult r, int i, double a, double b, double thr = 0.15)
        {
            int n = 0, s = 0;
            for (int k = (int)Math.Floor(a * SR / 256); k < Math.Min(r.Sv.Count, b * SR / 256); k++) { n++; if (r.Sv[k][i] > thr) s++; }
            return Math.Round((double)s / Math.Max(1, n) * 100) / 100;
        }
        public static double EnergyDb(SimResult r, int i, double a, double b)
        {
            double e = 0;
            for (int k = (int)Math.Floor(a * SR / 256); k < Math.Min(r.Rms.Count, (int)Math.Floor(b * SR / 256)); k++) e += r.Rms[k][i];
            return 10 * Math.Log10(e + 1e-30);
        }

        // ---- paths ----
        public const double TIP_R = BowlsHands.IX.TIP_R;

        /// <summary>A freehand circle round bowl i (circle() of hands-sim.mjs).</summary>
        public static Func<double, double3?> Circle(Placement L, int i, double rps = 0.7, double t0 = 0.4, double t1 = 6.4, double depth = 0.006, double offX = 0, double offZ = 0,
            double wob = 0, double drift = 0, double ywob = 0, double yOff = -0.008, double lead = 0, double dir = 1, double approach = 0.1)
        {
            var g = Placed(L)[i]; double A0 = Approach(L, i);
            return t =>
            {
                if (t > t1) return null;
                double tt = Math.Max(0, t - t0), a = A0 + dir * 2 * Math.PI * rps * tt - dir * lead;
                double rr = g.R + TIP_R - depth + wob * Math.Cos(2 * a + 0.7) + drift * Math.Sin(2 * Math.PI * 0.23 * tt);
                if (t < t0) rr += Math.Min(0.03, (t0 - t) * approach);
                double y = g.C.y + g.H + yOff + ywob * Math.Sin(2 * Math.PI * 0.9 * tt);
                return new double3(g.C.x + offX + rr * Math.Cos(a), y, g.C.z + offZ + rr * Math.Sin(a));
            };
        }

        /// <summary>A radial poke into bowl i's outer wall at its facing angle (poke()).</summary>
        public static Func<double, double3?> Poke(Placement L, int i, double v = 0.5, double over = 0.01, double dwell = 0, double t0 = 0.3, double yOff = -0.01, double? angle = null)
        {
            var g = Placed(L)[i];
            double a = angle ?? Approach(L, i), bse = g.R + TIP_R, D = 0.05 + over, T = Math.PI * D / v;
            return t =>
            {
                double d, u = (t - t0) / T;
                if (u < 0) d = 0;
                else if (u < 0.5) d = D * (1 - Math.Cos(2 * Math.PI * u)) / 2;
                else if (t < t0 + T / 2 + dwell) d = D;
                else { double u2 = (t - t0 - dwell) / T; d = u2 > 1 ? 0 : D * (1 - Math.Cos(2 * Math.PI * u2)) / 2; }
                double r = bse + 0.05 - d;
                return new double3(g.C.x + r * Math.Cos(a), g.C.y + g.H + yOff, g.C.z + r * Math.Sin(a));
            };
        }

        /// <summary>One hand with the index (and optional other fingertips, a palm).</summary>
        public static Func<double, SimHand[]> Hand(Func<double, double3?> index, Func<double, double3?> thumb = null, Func<double, double3?> middle = null,
            Func<double, double3?> ring = null, Func<double, double3?> little = null, Func<double, (double3 c, double3 n)?> palm = null)
        {
            return t =>
            {
                var h = new SimHand();
                h.Tips[0] = thumb?.Invoke(t); h.Tips[1] = index(t); h.Tips[2] = middle?.Invoke(t); h.Tips[3] = ring?.Invoke(t); h.Tips[4] = little?.Invoke(t);
                var p = palm?.Invoke(t); if (p.HasValue) { h.HasPalm = true; h.PalmC = p.Value.c; h.PalmN = p.Value.n; }
                return new[] { h };
            };
        }
    }
}
