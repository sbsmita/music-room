// The lab's scripted hands (the hand-drums lab's interaction.mjs: poseHand, motion, strokeKeys, noiseSource,
// addNoise, runCase, judge, CASES, statCases), transcribed 1:1 for the EditMode tests of HandDrumsInput: app frames at the app
// rate, tracker samples at the tracker rate (the pose is held between samples, not a new sample), seeded tracking noise
// per sample (mulberry32 + Box-Muller, sigma per joint and for the whole hand).
using System;
using System.Collections.Generic;
using MusicRoom.HandDrums;

namespace MusicRoom.Tests.HandDrums
{
    /// <summary>Pose parameters of a scripted hand (poseHand's options) with presence flags, as JS objects merge.</summary>
    public struct HP
    {
        public const uint FRho = 1, FTheta = 2, FLift = 4, FDir = 8, FCurl = 16, FPitch = 32, FJitter = 64;
        public double Rho, Theta, Lift, Dir, Curl, Pitch, Jitter;
        public uint Has;
        public bool Lost, LowFingers;
        public int Drum;   // -1 = not set

        public static HP Of(double rho, double theta = 0, double dir = Math.PI, double curl = 0, double? pitch = null)
        {
            var p = new HP { Rho = rho, Theta = theta, Dir = dir, Curl = curl, Has = FRho | FTheta | FDir | FCurl, Drum = -1 };
            if (pitch.HasValue) { p.Pitch = pitch.Value; p.Has |= FPitch; }
            return p;
        }
        public HP L(double lift) { var p = this; p.Lift = lift; p.Has |= FLift; return p; }
        public HP P(double pitch) { var p = this; p.Pitch = pitch; p.Has |= FPitch; return p; }
        public HP C(double curl) { var p = this; p.Curl = curl; p.Has |= FCurl; return p; }
        public HP J(double jitter) { var p = this; p.Jitter = jitter; p.Has |= FJitter; return p; }
        public HP Th(double theta) { var p = this; p.Theta = theta; p.Has |= FTheta; return p; }
        public HP Low() { var p = this; p.LowFingers = true; return p; }
        public double Get(uint f, double def) => (Has & f) != 0 ? (f == FRho ? Rho : f == FTheta ? Theta : f == FLift ? Lift : f == FDir ? Dir : f == FCurl ? Curl : f == FPitch ? Pitch : Jitter) : def;
    }

    public struct Key { public double T; public HP P; public Key(double t, HP p) { T = t; P = p; } }

    public static class Scripted
    {
        public static readonly HP OPEN = HP.Of(1.25, 0, Math.PI, 0.05);
        public static readonly HP BASS = HP.Of(0.05, 0, Math.PI, 0.1);
        public static readonly HP SLAP = HP.Of(1.30, 0, Math.PI, 0.55);
        public static readonly HP HEEL = HP.Of(0.42, 0, Math.PI, 0.2, 0.55);

        /// <summary>motion(keys): linear interpolation of the numeric parameters between keyframes.</summary>
        public static Func<double, HP> Motion(IList<Key> keys) => t =>
        {
            int i = 0;
            while (i < keys.Count - 2 && t > keys[i + 1].T) i++;
            Key a = keys[i];
            if (i + 1 >= keys.Count) return a.P;
            Key b = keys[i + 1];
            double s = Math.Min(1, Math.Max(0, (t - a.T) / (b.T - a.T)));
            HP p = a.P;
            double Lerp(double x, double y) => x + (y - x) * s;
            if ((a.P.Has & HP.FRho) != 0) p.Rho = Lerp(a.P.Rho, b.P.Rho);
            if ((a.P.Has & HP.FTheta) != 0) p.Theta = Lerp(a.P.Theta, b.P.Theta);
            if ((a.P.Has & HP.FLift) != 0) p.Lift = Lerp(a.P.Lift, b.P.Lift);
            if ((a.P.Has & HP.FDir) != 0) p.Dir = Lerp(a.P.Dir, b.P.Dir);
            if ((a.P.Has & HP.FCurl) != 0) p.Curl = Lerp(a.P.Curl, b.P.Curl);
            if ((a.P.Has & HP.FPitch) != 0) p.Pitch = Lerp(a.P.Pitch, (b.P.Has & HP.FPitch) != 0 ? b.P.Pitch : a.P.Pitch);
            if ((a.P.Has & HP.FJitter) != 0) p.Jitter = Lerp(a.P.Jitter, (b.P.Has & HP.FJitter) != 0 ? b.P.Jitter : a.P.Jitter);
            return p;
        };

        /// <summary>strokeKeys(t0, base, { v, from, depth, stay, up }): down at v from `from` above to `depth` below, back up.</summary>
        public static List<Key> StrokeKeys(double t0, HP b, double v = 1.2, double from = 0.08, double depth = 0.012, double stay = 0, double up = 0.6)
        {
            double tDown = (from + depth) / v;
            var keys = new List<Key> { new Key(t0, b.L(from)), new Key(t0 + tDown, b.L(-depth)) };
            if (stay > 0) keys.Add(new Key(t0 + tDown + stay, b.L(-depth)));
            keys.Add(new Key(t0 + tDown + stay + (from + depth) / up, b.L(from)));
            return keys;
        }

        public static List<Key> Concat(params List<Key>[] parts) { var o = new List<Key>(); foreach (var p in parts) o.AddRange(p); return o; }

        static readonly double[,] ProxOff = { { 0.096, 0.023 }, { 0.096, 0.002 }, { 0.089, -0.017 }, { 0.078, -0.035 } };
        static readonly double[] Seg1 = { 0.038, 0.042, 0.038, 0.03 }, Seg2 = { 0.046, 0.052, 0.049, 0.04 };

        /// <summary>poseHand(d, p): a hand over drum d, palm centre at (rho, theta), `lift` above the head, fingers along dir,
        /// curl 0..1, pitch &gt; 0 tips the fingers up; skin radii at scale 1.</summary>
        public static void PoseHand(HdHead hd, HP p, HdHandSample o, double side = 1)
        {
            double rho = p.Rho, theta = p.Get(HP.FTheta, 0), lift = p.Lift, dir = p.Get(HP.FDir, Math.PI), curl = p.Get(HP.FCurl, 0), pitch = p.Get(HP.FPitch, 0);
            D3 InPlane(double r, double th) => hd.C + (hd.E0 * (r * hd.A * Math.Cos(th)) + hd.E1 * (r * hd.A * Math.Sin(th)));
            D3 F = (hd.E0 * Math.Cos(dir) + hd.E1 * Math.Sin(dir)).Normalized;
            F = (F * Math.Cos(pitch) + hd.N * Math.Sin(pitch)).Normalized;
            D3 back = (hd.N - F * D3.Dot(hd.N, F)).Normalized, S = D3.Cross(back, F) * side;
            D3 palmSkin = InPlane(rho, theta) + hd.N * lift;
            D3 palm = palmSkin + back * HdK.RPalm;
            D3 wrist = palm - F * 0.065;
            for (int f = 0; f < 4; f++)
            {
                D3 pp = (wrist + F * ProxOff[f, 0]) + (S * ProxOff[f, 1] + back * (-HdK.RPalm + HdK.RProx[f]));
                double a1 = curl * 0.6, a2 = curl * 1.2;
                D3 m = pp + (F * (Seg1[f] * Math.Cos(a1)) + back * (-Seg1[f] * Math.Sin(a1)));
                D3 t = m + (F * (Seg2[f] * Math.Cos(a2)) + back * (-Seg2[f] * Math.Sin(a2)));
                o.Prox[f] = pp; o.Mid[f] = m; o.Tip[f] = t;
            }
            o.Wrist = wrist; o.Palm = palm; o.Back = back;
            o.Tracked = true; o.HighConfidence = true; o.HandScale = 1;
            for (int k = 0; k < 4; k++) o.FingerConfidence[k] = true;
        }

        /// <summary>noiseSource(seed): mulberry32 + Box-Muller, one N(0, 1) value per call.</summary>
        public sealed class Noise
        {
            uint _a;
            public Noise(uint seed) { _a = seed; }
            double Nx()
            {
                _a += 0x6d2b79f5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
            public double Next() { double u = 0; while (u == 0) u = Nx(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * Nx()); }
        }

        /// <summary>addNoise(pose, g, sJ, sH): one whole-hand offset, then per joint (wrist, palm, prox, mid, tip; x, y, z).</summary>
        public static void AddNoise(HdHandSample o, Noise g, double sJ, double sH)
        {
            if (sJ == 0 && sH == 0) return;
            D3 W = new D3(g.Next() * sH, g.Next() * sH, g.Next() * sH);
            D3 J(D3 p) { double x = p.X + W.X + g.Next() * sJ, y = p.Y + W.Y + g.Next() * sJ, z = p.Z + W.Z + g.Next() * sJ; return new D3(x, y, z); }
            o.Wrist = J(o.Wrist); o.Palm = J(o.Palm);
            for (int f = 0; f < 4; f++) o.Prox[f] = J(o.Prox[f]);
            for (int f = 0; f < 4; f++) o.Mid[f] = J(o.Mid[f]);
            for (int f = 0; f < 4; f++) o.Tip[f] = J(o.Tip[f]);
        }

        /// <summary>A scripted hand: its drum (-1: from the pose), its motion (null pose = lost).</summary>
        public sealed class HandScript
        {
            public int Drum = -1;
            public Func<double, HP?> Fn;
            public static HandScript Of(Func<double, HP> fn, int drum = -1) => new HandScript { Drum = drum, Fn = t => fn(t) };
        }

        public sealed class Case
        {
            public string Id;
            public double Seconds;
            public HandScript[] Hands = new HandScript[2];
            public List<(string op, string kind, int hand, int drum, string label)> Expect = new List<(string, string, int, int, string)>();
            public string[] Forbid = Array.Empty<string>();
            public int MaxStrokes = -1, MaxRests = -1;
            public bool Rising;
            public double FlamMs;
        }

        /// <summary>runCase(c, { appHz, trackerHz, sigmaJoint, sigmaHand, seed }).</summary>
        public static HandDrumsInput Run(Case c, int appHz = 72, int trackerHz = 0, double sigmaJoint = 0, double sigmaHand = 0, uint seed = 1)
        {
            var input = new HandDrumsInput();
            double dt = 1.0 / appHz;
            int tr = trackerHz > 0 ? trackerHz : appHz;
            var g = new Noise(seed);
            var samples = new[] { new HdHandSample(), new HdHandSample() };
            var last = new HdHandSample[2];
            var lastS = new long[] { -1, -1 };
            for (int i = 0; i * dt <= c.Seconds + 1e-9; i++)
            {
                double t = i * dt;
                long sIdx = (long)Math.Floor(t * tr + 1e-6);
                double ts = sIdx / (double)tr;
                var hands = new HdHandSample[2];
                for (int h = 0; h < 2; h++)
                {
                    var m = c.Hands[h];
                    if (m == null) { hands[h] = null; continue; }
                    if (sIdx == lastS[h]) { if (last[h] != null) last[h].NewSample = false; hands[h] = last[h]; continue; }
                    lastS[h] = sIdx;
                    HP? pp = m.Fn(ts);
                    if (!pp.HasValue || pp.Value.Lost) { last[h] = null; hands[h] = null; continue; }
                    HP p = pp.Value;
                    int drum = m.Drum >= 0 ? m.Drum : p.Drum >= 0 ? p.Drum : 0;
                    var o = samples[h];
                    PoseHand(input.Heads[drum], p, o);
                    if (p.LowFingers) { o.FingerConfidence[0] = true; o.FingerConfidence[1] = false; o.FingerConfidence[2] = false; o.FingerConfidence[3] = true; }
                    if ((p.Has & HP.FJitter) != 0 && p.Jitter != 0)
                    {
                        D3 n0 = input.Heads[0].N;
                        double Jj(int k) => Math.Sin(ts * 61 + k) * p.Jitter;
                        o.Palm = o.Palm + n0 * Jj(1);
                        for (int f = 0; f < 4; f++) o.Prox[f] = o.Prox[f] + n0 * Jj(f + 2);
                        for (int f = 0; f < 4; f++) o.Mid[f] = o.Mid[f] + n0 * Jj(f + 2);
                        for (int f = 0; f < 4; f++) o.Tip[f] = o.Tip[f] + n0 * Jj(f + 2);
                    }
                    AddNoise(o, g, sigmaJoint, sigmaHand);
                    o.SampleTime = ts; o.NewSample = true;
                    last[h] = o; hands[h] = o;
                }
                input.Step(t, hands[0], hands[1], dt);
            }
            return input;
        }

        public static string Kind(HdKind k) => k switch { HdKind.Open => "open", HdKind.Bass => "bass", HdKind.Slap => "slap", HdKind.Heel => "heel", HdKind.Toe => "toe", HdKind.Touch => "touch", _ => null };
        public static string Label(HdLabel l) => l == HdLabel.Mute ? "mute" : l == HdLabel.SlapClosed ? "slapClosed" : null;
        public static string Op(HdOp o) => o == HdOp.Stroke ? "stroke" : o == HdOp.Rest ? "rest" : o == HdOp.Lift ? "lift" : "relabel";

        /// <summary>The input's events without the Relabel markers (their labels are on the strokes), as the reference lists them.</summary>
        public static List<HdEvent> Events(HandDrumsInput input)
        {
            var o = new List<HdEvent>();
            for (int i = 0; i < input.Count; i++) if (input.Events[i].Op != HdOp.Relabel) o.Add(input.Events[i]);
            return o;
        }

        /// <summary>judge(c, input): the expected events in order, the forbidden ones, counts, rising u, the flam spacing.</summary>
        public static (bool ok, string why) Judge(Case c, HandDrumsInput input)
        {
            var ev = Events(input);
            var why = new List<string>();
            bool ok = true;
            int j = 0;
            foreach (var e in ev)
            {
                if (j >= c.Expect.Count) break;
                var x = c.Expect[j];
                if (Op(e.Op) == x.op && (x.kind == null || Kind(e.Kind) == x.kind) && (x.hand < 0 || e.Hand == x.hand) && (x.drum < 0 || e.Drum == x.drum) && (x.label == null || Label(e.Label) == x.label)) j++;
            }
            if (j < c.Expect.Count) { ok = false; why.Add("expected " + c.Expect[j]); }
            foreach (var f in c.Forbid) foreach (var e in ev) if (Op(e.Op) == f || Kind(e.Kind) == f || Label(e.Label) == f) { ok = false; why.Add("forbidden " + f); break; }
            var strokes = ev.FindAll(e => e.Op == HdOp.Stroke);
            int nExp = c.Expect.FindAll(x => x.op == "stroke").Count;
            if (nExp != strokes.Count && Array.IndexOf(c.Forbid, "stroke") < 0) { ok = false; why.Add($"strokes {strokes.Count} != {nExp}"); }
            if (c.MaxStrokes >= 0 && strokes.Count > c.MaxStrokes) { ok = false; why.Add("double trigger"); }
            if (c.MaxRests >= 0 && ev.FindAll(e => e.Op == HdOp.Rest).Count > c.MaxRests) { ok = false; why.Add("rest re-sent"); }
            if (c.Rising && !(strokes.Count == 3 && strokes[0].U < strokes[1].U && strokes[1].U < strokes[2].U)) { ok = false; why.Add("u not rising"); }
            if (c.FlamMs > 0 && strokes.Count == 2 && Math.Abs(strokes[1].TStar - strokes[0].TStar - c.FlamMs) > 3) { ok = false; why.Add($"flam spacing {strokes[1].TStar - strokes[0].TStar:F1} ms"); }
            return (ok, string.Join("; ", why));
        }

        static (string, string, int, int, string) X(string op, string kind = null, int hand = -1, int drum = -1, string label = null) => (op, kind, hand, drum, label);

        /// <summary>CASES (interaction.mjs), in the reference's order.</summary>
        public static List<Case> Cases()
        {
            var L = new List<Case>();
            Func<double, HP> M(List<Key> k) => Motion(k);
            L.Add(new Case { Id = "C01-open", Seconds = 0.6, Hands = { [1] = HandScript.Of(M(StrokeKeys(0.1, OPEN, 1.2))) }, Expect = { X("stroke", "open") } });
            L.Add(new Case { Id = "C02-bass", Seconds = 0.6, Hands = { [1] = HandScript.Of(M(StrokeKeys(0.1, BASS, 1.0))) }, Expect = { X("stroke", "bass") } });
            L.Add(new Case { Id = "C03-slap", Seconds = 0.6, Hands = { [1] = HandScript.Of(M(StrokeKeys(0.1, SLAP, 1.6, 0.13, -0.02))) }, Expect = { X("stroke", "slap") } });
            L.Add(new Case { Id = "C04-mute", Seconds = 0.9, Hands = { [1] = HandScript.Of(M(StrokeKeys(0.1, OPEN, 1.2, depth: 0.006, stay: 0.3))) }, Expect = { X("stroke", "open", label: "mute"), X("rest"), X("lift") } });
            L.Add(new Case
            {
                Id = "C05-heel-toe", Seconds = 1.0,
                Hands = { [0] = HandScript.Of(M(new List<Key> { new Key(0.1, HEEL.L(0.06)), new Key(0.2, HEEL.L(0.010)), new Key(0.45, HEEL.L(0.010)), new Key(0.55, HEEL.L(0.010).P(-0.12).C(0.25)), new Key(0.8, HEEL.L(0.06).P(-0.12).C(0.25)) }), 0) },
                Expect = { X("stroke", "heel"), X("rest"), X("stroke", "toe") },
            });
            L.Add(new Case
            {
                Id = "C06-flam", Seconds = 0.6,
                Hands = { [0] = HandScript.Of(M(StrokeKeys(0.1, OPEN.Th(-0.4), 1.2))), [1] = HandScript.Of(M(StrokeKeys(0.115, OPEN.Th(0.4), 1.2))) },
                Expect = { X("stroke", "open", 0), X("stroke", "open", 1) }, FlamMs = 15,
            });
            {
                var c = new Case { Id = "C07-roll", Seconds = 1.15 };
                for (int h = 0; h < 2; h++)
                {
                    var keys = new List<Key>();
                    for (int i = 0; i < 6; i++) keys.AddRange(StrokeKeys(0.1 + h / 12.0 + i / 6.0, OPEN.Th(h == 1 ? 0.4 : -0.4), 1.5, 0.05, up: 1.5));
                    c.Hands[h] = HandScript.Of(M(keys));
                }
                for (int i = 0; i < 12; i++) c.Expect.Add(X("stroke", "open"));
                L.Add(c);
            }
            L.Add(new Case { Id = "C08-jitter", Seconds = 1.2, Hands = { [1] = HandScript.Of(M(new List<Key> { new Key(0, BASS.L(0.03)), new Key(0.4, BASS.L(-0.002).J(0)), new Key(1.2, BASS.L(-0.002).J(0.003)) })) }, Expect = { X("rest") }, Forbid = new[] { "stroke" }, MaxRests = 2 });
            L.Add(new Case { Id = "C09-slow-place", Seconds = 1.2, Hands = { [1] = HandScript.Of(M(new List<Key> { new Key(0, BASS.L(0.06)), new Key(0.9, BASS.L(-0.004)), new Key(1.2, BASS.L(-0.004)) })) }, Expect = { X("rest") }, Forbid = new[] { "stroke" } });
            L.Add(new Case { Id = "C10-from-below", Seconds = 0.8, Hands = { [1] = new HandScript { Fn = t => t < 0.2 ? new HP { Lost = true, Drum = -1 } : BASS.L(-0.03 + (t - 0.2) * 0.2) } }, Forbid = new[] { "stroke" } });
            {
                var k = M(StrokeKeys(0.1, OPEN, 1.2));
                L.Add(new Case { Id = "C11-lost-mid-stroke", Seconds = 0.6, Hands = { [1] = new HandScript { Fn = t => t > 0.13 && t < 0.2 ? new HP { Lost = true, Drum = -1 } : k(t) } }, Forbid = new[] { "stroke" } });
            }
            {
                var a = M(StrokeKeys(0.1, BASS, 1.0)); var b = M(StrokeKeys(0.7, OPEN, 1.2));
                L.Add(new Case { Id = "C12-low-finger-confidence", Seconds = 1.2, Hands = { [1] = new HandScript { Fn = t => (t < 0.6 ? a(t) : b(t)).Low() } }, Expect = { X("stroke", "bass"), X("stroke", "open") }, Forbid = new[] { "slap" } });
            }
            L.Add(new Case { Id = "C13-palm-then-fingers", Seconds = 0.7, Hands = { [1] = HandScript.Of(M(new List<Key> { new Key(0.1, BASS.L(0.08).P(0.25)), new Key(0.18, BASS.L(-0.008).P(0.25)), new Key(0.2, BASS.L(-0.008).P(0)), new Key(0.4, BASS.L(0.08).P(0)) })) }, Expect = { X("stroke", "bass") }, MaxStrokes = 1 });
            L.Add(new Case { Id = "C14-rim-miss", Seconds = 0.6, Hands = { [1] = HandScript.Of(M(StrokeKeys(0.1, HP.Of(1.9, 0, 0, 0.6), 1.2))) }, Forbid = new[] { "stroke" } });
            L.Add(new Case { Id = "C15-too-slow", Seconds = 1.2, Hands = { [1] = HandScript.Of(M(StrokeKeys(0.1, OPEN, 0.1, 0.03, stay: 0.2))) }, Expect = { X("rest") }, Forbid = new[] { "stroke" } });
            L.Add(new Case
            {
                Id = "C16-two-drums", Seconds = 1.0,
                Hands = { [0] = HandScript.Of(M(StrokeKeys(0.1, BASS, 1.0)), 0), [1] = HandScript.Of(M(Concat(StrokeKeys(0.12, OPEN, 1.2), StrokeKeys(0.5, OPEN, 1.2))), 1) },
                Expect = { X("stroke", "bass", drum: 0), X("stroke", "open", drum: 1), X("stroke", "open", drum: 1) },
            });
            L.Add(new Case
            {
                Id = "C18-overshoot", Seconds = 0.7,
                Hands = { [1] = HandScript.Of(M(new List<Key> { new Key(0.1, OPEN.L(0.08)), new Key(0.15, OPEN.L(-0.02)), new Key(0.17, OPEN.L(-0.035)), new Key(0.185, OPEN.L(-0.04)), new Key(0.2, OPEN.L(-0.035)), new Key(0.22, OPEN.L(-0.02)), new Key(0.3, OPEN.L(0.08)) })) },
                Expect = { X("stroke", "open") }, Forbid = new[] { "rest", "mute" },
            });
            L.Add(new Case { Id = "C17-speed", Seconds = 1.6, Hands = { [1] = HandScript.Of(M(Concat(StrokeKeys(0.1, OPEN, 0.4), StrokeKeys(0.6, OPEN, 1.0), StrokeKeys(1.1, OPEN, 2.0)))) }, Expect = { X("stroke", "open"), X("stroke", "open"), X("stroke", "open") }, Rising = true });
            return L;
        }

        public static readonly (int app, int tr)[] CaseRates = { (72, 72), (90, 90), (72, 60), (90, 60) };
        public static readonly (int app, int tr)[] Rates = { (72, 60), (72, 30), (90, 60), (90, 30) };
    }
}
