// Shared helpers of the hand-drum unit's EditMode tests (handdrums.md 6.1-6.2): the Reference~ data copied from
// the hand-drums lab by Audio/Sound/HandDrums/Tools~/export-refs.mjs, the reference's test-vector scenario and demo
// phrases replayed through IHandDrumSound on the standalone host (HandDrumsOffline: phase A has no UnitId for the unit
// yet), FNV-1a, and the lab's analysis measures (lib/measure.mjs, drums/lib/analysis.mjs) in double precision.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio.Burst;
using Unity.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public static class HdRef
    {
        public static string Dir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/HandDrums/Reference~");
        public static string File(string name) => Path.Combine(Dir, name);
        public static string ListenDir => Path.Combine(Dir, "renders");
        public static string UnityListenDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Listen", "handdrums");
        public static string LogDir => Path.Combine(Application.dataPath, "../Logs/renders/handdrums");
        public static T Json<T>(string name) => JsonUtility.FromJson<T>(System.IO.File.ReadAllText(File(name)));

        public static float[] ReadF32(string path, int offset, int count)
        {
            byte[] b = System.IO.File.ReadAllBytes(path);
            var y = new float[count];
            Buffer.BlockCopy(b, offset * 4, y, 0, count * 4);
            return y;
        }

        /// <summary>The managed path is bit-exact only without Burst (ENGINE-API 3.4); the Strict entry is IEEE too.</summary>
        public static bool BurstOn => BurstCompiler.IsEnabled;

        public static uint Fnv(float[] x)
        {
            uint h = 0x811c9dc5;
            for (int i = 0; i < x.Length; i++)
            {
                uint w = (uint)BitConverter.SingleToInt32Bits(x[i]);
                for (int k = 0; k < 4; k++) { h ^= w & 0xff; h *= 0x01000193; w >>= 8; }
            }
            return h;
        }

        public static uint ParseHex(string s) => Convert.ToUInt32(s.Substring(2), 16);
        public static string Hex(uint u) => "0x" + u.ToString("x8");
        public static long JsAt(double t) => (long)Math.Floor(t * 48000 + 0.5);
    }

    // ---------------------------------------------------------------- JSON shapes (JsonUtility)
    [Serializable] public class HdTv { public bool identicalAcrossRuns; public HdTvDrums drums; public HdTvTables tables; public HdTvContact contact; public double[] openToneGi12; public double rngAfterOpen; public HdTvShare scenarioShare04; public int scenarioShare1Work; public HdTvLaws laws; }
    [Serializable] public class HdTvDrums { public HdTvDrum conga, tumba; public HdTvDrum this[int d] => d == 0 ? conga : tumba; }
    [Serializable] public class HdTvDrum { public int firstNonZero; public double[] first8; public HdTvRms rmsWindows; public double peak; public string fnv1a; }
    [Serializable] public class HdTvRms { public double[] rms; }
    [Serializable] public class HdTvTables { public HdTvTable conga, tumba; public HdTvTable this[int d] => d == 0 ? conga : tumba; }
    [Serializable] public class HdTvTable { public int n, nPad, groups, gCav; public double tensionNperM, waveSpeed, kappa, level; public HdTvMode[] firstModes; public string fnvShape, fnvGo, fnvAvg; }
    [Serializable] public class HdTvMode { public int kind, m, n, orient; public double f, t60, M; }
    [Serializable] public class HdTvContact { public HdTvProbe palm, heel, fingers, tips, @base, pads; public HdTvProbe this[int p] => new[] { palm, heel, fingers, tips, @base, pads }[p]; }
    [Serializable] public class HdTvProbe { public double V, Kh, Zh, FmaxN; public int contactSamples; public string fnvDF; public double[] dF_first8; }
    [Serializable] public class HdTvShare { public int cap, work, shedGroups; public HdTvFnv fnv1a; public HdTvPeak peak; }
    [Serializable] public class HdTvFnv { public string conga, tumba; }
    [Serializable] public class HdTvPeak { public double conga, tumba; }
    [Serializable] public class HdTvLaws { public HdTvPartV partV; public HdTvHand handSpeed; public double[] hertzAvg; public int[] channelOfDrum; public string[] sources; }
    [Serializable] public class HdTvPartV { public double[] palm, heel, fingers, tips, @base, pads; public double[] this[int p] => new[] { palm, heel, fingers, tips, @base, pads }[p]; }
    [Serializable] public class HdTvHand { public double[] palm, fingers; }

    [Serializable] public class HdTables { public int version; public HdTablesDrums drums; }
    [Serializable] public class HdTablesDrums { public HdTablesDrum conga, tumba; public HdTablesDrum this[int d] => d == 0 ? conga : tumba; }
    [Serializable] public class HdTablesDrum { public double a, sigma, tension, waveSpeed, headOwn01, fT, fB, fA2, glideMax, kappa, level; public int n, nPad, groups, gCav; public HdTablesMode[] modes; public int[] gClass, shedOrder, glideList; }
    [Serializable] public class HdTablesMode { public int k, kind, m, n, orient, glide; public double f, t60, M, kappa, go, r0, w0, eW, nReal; }

    [Serializable] public class HdDemo { public int sr, seed; public string dryDir; public HdPhrase[] phrases; }
    [Serializable] public class HdPhrase { public string name; public double seconds, peak; public int frames; public string[] fnv; public HdCall[] calls; }
    [Serializable] public class HdCall { public string op, kind, rest; public long at; public int drum, hand; public double u, theta; }

    [Serializable] public class HdRenders { public HdRenderFiles files; }
    [Serializable] public class HdRenderFiles { public HdRenderFile ladder_open, ladder_bass, ladder_slap, techniques, tumbao, congaline; }
    [Serializable] public class HdRenderFile { public string path; public double seconds, lufs, truePeakDb, gainAfterMasterDb; }

    /// <summary>The unit on the standalone host.</summary>
    public static class HdRender
    {
        public const int Sr = 48000;

        public static HandStroke Kind(string k)
        {
            switch (k)
            {
                case "open": return HandStroke.Open;
                case "mute": return HandStroke.Mute;
                case "slap": return HandStroke.Slap;
                case "slapClosed": return HandStroke.SlapClosed;
                case "bass": return HandStroke.Bass;
                case "heel": return HandStroke.Heel;
                case "toe": return HandStroke.Toe;
                case "touch": return HandStroke.Touch;
                default: throw new ArgumentException(k);
            }
        }

        public static HandRest Rest(string r)
        {
            switch (r)
            {
                case "mute": return HandRest.Mute;
                case "slapClosed": return HandRest.SlapClosed;
                case "heel": return HandRest.Heel;
                case "palm": return HandRest.Palm;
                case "toe": return HandRest.Toe;
                default: throw new ArgumentException(r);
            }
        }

        /// <summary>The test-vector scenario (the hand-drums lab's vectors.mjs EVENTS), posted in its order.</summary>
        public static void PostScenario(HandDrumsSound s)
        {
            double A(double t) => HandDrumsOffline.At(HdRef.JsAt(t));
            s.Stroke(0, 1, HandStroke.Open, 0.8, 0, A(0.000));
            s.Stroke(1, 0, HandStroke.Bass, 0.7, 0, A(0.250));
            s.Stroke(0, 1, HandStroke.Slap, 0.9, 0, A(0.500));
            s.RestCanonical(0, 0, HandRest.Mute, 0, A(0.620));
            s.Lift(0, 0, A(0.800));
            s.Stroke(1, 0, HandStroke.Heel, 0.5, 0, A(1.000));
            s.RestCanonical(1, 0, HandRest.Heel, 0, A(1.012));
            s.Lift(1, 0, A(1.200));
            s.Stroke(1, 0, HandStroke.Toe, 0.4, 0, A(1.200));
            s.Stroke(0, 0, HandStroke.Open, 0.7, 0, A(1.500));
            s.Stroke(0, 1, HandStroke.Open, 0.7, 0, A(1.512));
            s.RestCanonical(1, 1, HandRest.Palm, 0, A(2.000));
            s.Lift(1, 1, A(2.500));
            s.Stroke(0, 1, HandStroke.SlapClosed, 1.0, 0, A(2.600));
        }

        /// <summary>The 3 s scenario on a fresh unit (seed 1): (conga, tumba) and the host for its stats.</summary>
        public static float[][] Scenario(HandDrumsEntry entry, float share = 1f, Action<HandDrumsOffline> after = null)
        {
            using var h = new HandDrumsOffline();
            h.Entry = entry;
            h.Sound.SetShare(share);
            PostScenario(h.Sound);
            // vectors.mjs processes whole 256-frame blocks: 563 of them for 3 s (the last one past the end), which counts in Work
            int frames = 3 * Sr, blocks = (frames + 255) / 256;
            var y = h.Render(blocks * 256);
            after?.Invoke(h);
            Array.Resize(ref y[0], frames); Array.Resize(ref y[1], frames);
            return y;
        }

        /// <summary>One demo phrase's calls (export-refs.mjs: render.mjs renderEvents, call by call).</summary>
        public static void Post(HandDrumsSound s, HdCall c)
        {
            double when = HandDrumsOffline.At(c.at);
            switch (c.op)
            {
                case "lift": s.Lift(c.drum, c.hand, when); break;
                case "rest": s.RestCanonical(c.drum, c.hand, Rest(c.rest), c.theta, when); break;
                default: s.Stroke(c.drum, c.hand, Kind(c.kind), c.u, c.theta, when); break;
            }
        }

        public static float[][] Phrase(HdPhrase p, HandDrumsEntry entry)
        {
            using var h = new HandDrumsOffline();
            h.Entry = entry;
            foreach (var c in p.calls) Post(h.Sound, c);
            return h.Render(p.frames);
        }

        /// <summary>A single canonical stroke at `pre` s into a render of `seconds` (probe.mjs renderStroke): the drum's output.</summary>
        public static float[] Stroke(HandStroke kind, int drum = 0, double u = 0.8, double seconds = 1.2, double pre = 0.002, HandDrumsEntry entry = HandDrumsEntry.BurstFast, Action<HandDrumsOffline> setup = null, int hand = 1)
        {
            using var h = new HandDrumsOffline();
            h.Entry = entry;
            setup?.Invoke(h);
            int n = (int)Math.Floor(seconds * Sr + 0.5);
            h.Sound.Stroke(drum, hand, kind, u, 0, HandDrumsOffline.At(HdRef.JsAt(pre)));
            return h.Render(n)[drum];
        }

        /// <summary>probe.mjs solo: keep only the modes of one kind audible (output gain 0 for the others).</summary>
        public static unsafe void Solo(HandDrumsOffline h, int kind)
        {
            for (int d = 0; d < 2; d++)
            {
                HandDrumsDrum* dr = d == 0 ? &h.State->D0 : &h.State->D1;
                for (int k = 0; k < dr->NPad; k++)
                    if (k >= dr->N || dr->Kind[k] != kind) ((float*)(dr->Bank + (k >> 2)))[16 + (k & 3)] = 0f;
            }
        }
    }

    /// <summary>The lab's measures (drums/lib/analysis.mjs, handdrums/lib/measure.mjs, tools/strokelab.mjs), double.</summary>
    public static class HdAnalysis
    {
        public const double Sr = 48000;

        public static int NextPow2(int n) { int p = 1; while (p < n) p <<= 1; return p; }

        /// <summary>In-place radix-2 FFT (the lab's fft: forward, no scaling).</summary>
        public static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                int h = len >> 1;
                for (int k = 0; k < h; k++)
                {
                    double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                    for (int i = k; i < n; i += len)
                    {
                        int j = i + h;
                        double xr = re[j] * wr - im[j] * wi, xi = re[j] * wi + im[j] * wr;
                        re[j] = re[i] - xr; im[j] = im[i] - xi; re[i] += xr; im[i] += xi;
                    }
                }
            }
        }

        /// <summary>Rectangular window from `from`, zero-padded (default 4x): |X|^2 for k = 0..N/2.</summary>
        public static double[] PowerRect(float[] x, int from, int len, int nfft = 0)
        {
            if (nfft == 0) nfft = NextPow2(4 * len);
            var re = new double[nfft]; var im = new double[nfft];
            for (int i = 0; i < len && from + i < x.Length; i++) re[i] = x[from + i];
            Fft(re, im);
            var p = new double[nfft / 2 + 1];
            for (int k = 0; k <= nfft / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k];
            return p;
        }

        /// <summary>Hann window (the lab's powerSpectrum).</summary>
        public static double[] PowerHann(float[] x, int from, int len, int nfft = 0)
        {
            if (nfft == 0) nfft = NextPow2(len);
            var re = new double[nfft]; var im = new double[nfft];
            for (int i = 0; i < len && from + i < x.Length; i++) re[i] = x[from + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 0.5) / len));
            Fft(re, im);
            var p = new double[nfft / 2 + 1];
            for (int k = 0; k <= nfft / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k];
            return p;
        }

        public struct Peak { public double F, Db; }

        /// <summary>Strongest local maximum of the rect spectrum (2^17 points) within [lo, hi] Hz (measure.mjs peakIn).</summary>
        public static Peak PeakIn(float[] x, int from, int len, double lo, double hi)
        {
            var p = PowerRect(x, from, len, 1 << 17);
            double df = Sr / ((p.Length - 1) * 2);
            var best = new Peak { F = 0, Db = -999 };
            for (int k = (int)Math.Ceiling(lo / df); k <= (int)Math.Floor(hi / df) && k < p.Length - 1; k++)
                if (k > 0 && p[k] >= p[k - 1] && p[k] >= p[k + 1]) { double L = 10 * Math.Log10(p[k] + 1e-30); if (L > best.Db) best = new Peak { F = k * df, Db = L }; }
            return best;
        }

        /// <summary>analysis.mjs spectralPeaks: Hann, parabolic interpolation, strongest first, minSepHz apart.</summary>
        public static List<Peak> SpectralPeaks(float[] x, int from, int len, int count, double minHz, double maxHz, double minSepHz = 8)
        {
            var p = PowerHann(x, from, len, Math.Max(1 << 17, NextPow2(len)));
            int N = (p.Length - 1) * 2;
            double df = Sr / N;
            var c = new List<Peak>();
            for (int k = Math.Max(2, (int)Math.Floor(minHz / df)); k < Math.Min(p.Length - 2, maxHz / df); k++)
                if (p[k] > p[k - 1] && p[k] >= p[k + 1])
                {
                    double a = 10 * Math.Log10(p[k - 1] + 1e-30), b = 10 * Math.Log10(p[k] + 1e-30), cc = 10 * Math.Log10(p[k + 1] + 1e-30);
                    double den = a - 2 * b + cc; if (den == 0) den = 1e-12;
                    double d = 0.5 * (a - cc) / den;
                    c.Add(new Peak { F = (k + d) * df, Db = b - 0.25 * (a - cc) * d });
                }
            c.Sort((u, v) => v.Db.CompareTo(u.Db));
            var o = new List<Peak>();
            foreach (var q in c)
            {
                bool far = true;
                foreach (var w in o) if (Math.Abs(w.F - q.F) <= minSepHz) { far = false; break; }
                if (far) o.Add(q);
                if (o.Count >= count) break;
            }
            return o;
        }

        /// <summary>Energy in [lo, hi) Hz of the rect spectrum (dB, arbitrary reference).</summary>
        public static double BandRect(float[] x, int from, int len, double lo, double hi)
        {
            var p = PowerRect(x, from, len);
            int nfft = (p.Length - 1) * 2;
            double e = 0;
            for (int k = (int)Math.Ceiling(lo * nfft / Sr); k < Math.Min(p.Length, hi * nfft / Sr); k++) e += p[k];
            return 10 * Math.Log10(e + 1e-30);
        }

        public static double CentroidRect(float[] x, int from, int len)
        {
            var p = PowerRect(x, from, len);
            int nfft = (p.Length - 1) * 2;
            double a = 0, b = 0;
            for (int k = 1; k < p.Length; k++) { double f = k * Sr / nfft; a += f * p[k]; b += p[k]; }
            return b > 0 ? a / b : 0;
        }

        /// <summary>strokelab.mjs rectShare: energy at or above lo re all (dB).</summary>
        public static double RectShare(float[] x, int from, int len, double lo)
        {
            var p = PowerRect(x, from, len);
            double df = Sr / ((p.Length - 1) * 2), a = 0, b = 0;
            for (int k = 1; k < p.Length; k++) { b += p[k]; if (k * df >= lo) a += p[k]; }
            return 10 * Math.Log10(a / b);
        }

        /// <summary>IEC 61672 A-weighting (linear, 0 dB at 1 kHz).</summary>
        public static double AWeight(double f)
        {
            double f2 = f * f, c1 = 20.598997 * 20.598997, c2 = 107.65265 * 107.65265, c3 = 737.86223 * 737.86223, c4 = 12194.217 * 12194.217;
            double r = (c4 * f2 * f2) / ((f2 + c1) * Math.Sqrt((f2 + c2) * (f2 + c3)) * (f2 + c4));
            return r * 1.2589412;
        }

        /// <summary>measure.mjs aLevel: A-weighted energy of [from, from + len) (dB, relative).</summary>
        public static double ALevel(float[] x, int from, int len)
        {
            var p = PowerRect(x, from, len);
            int nfft = (p.Length - 1) * 2;
            double e = 0;
            for (int k = 1; k < p.Length; k++) { double a = AWeight(k * Sr / nfft); e += p[k] * a * a; }
            return 10 * Math.Log10(e / len + 1e-30);
        }

        /// <summary>The critic's spectrum (drums/critic/lib.mjs): Blackman-Harris, normalised by the window power.</summary>
        public static double[] PowerBh(float[] x, int from, int len, int nfft, out double df)
        {
            var re = new double[nfft]; var im = new double[nfft];
            double ws = 0;
            for (int i = 0; i < len; i++)
            {
                double t = 2 * Math.PI * i / (len - 1);
                double w = 0.35875 - 0.48829 * Math.Cos(t) + 0.14128 * Math.Cos(2 * t) - 0.01168 * Math.Cos(3 * t);
                ws += w * w;
                re[i] = (from + i < x.Length ? x[from + i] : 0) * w;
            }
            Fft(re, im);
            var p = new double[nfft / 2 + 1];
            for (int k = 0; k <= nfft / 2; k++) p[k] = (re[k] * re[k] + im[k] * im[k]) / ws;
            df = Sr / nfft;
            return p;
        }

        /// <summary>strokelab.mjs peakToMedian: per 1/3 octave from 1250 Hz inside [lo, hi], the band's peak bin over its
        /// median bin (dB): a ringing line reads high, decaying noise ~7-10 dB.</summary>
        public static List<double> PeakToMedian(float[] y, int a, int len, double lo, double hi)
        {
            var p = PowerBh(y, a, len, 1 << 15, out double df);
            var rows = new List<double>();
            for (double fc = 1250; fc < 7000; fc *= Math.Pow(2, 1.0 / 3))
            {
                if (fc < lo * 0.99 || fc > hi * 1.01) continue;
                int k0 = (int)Math.Ceiling(fc / Math.Pow(2, 1.0 / 6) / df), k1 = (int)Math.Floor(fc * Math.Pow(2, 1.0 / 6) / df);
                var v = new List<double>();
                for (int k = k0; k <= k1; k++) v.Add(p[k]);
                v.Sort();
                rows.Add(Math.Round((10 * Math.Log10(Math.Max(1e-30, v[v.Count - 1])) - 10 * Math.Log10(Math.Max(1e-30, v[v.Count >> 1]))) * 10) / 10);
            }
            return rows;
        }

        /// <summary>analysis.mjs clickScore: the worst |second difference| over the local 4 ms rms outside the onsets.</summary>
        public static double ClickScore(float[] x, IList<long> onsets, double guard = 0.004)
        {
            int W = (int)Math.Round(0.002 * Sr), g = (int)Math.Round(guard * Sr);
            var sorted = new List<long>(onsets); sorted.Sort();
            double worst = 0;
            int oi = 0;
            for (int i = 2; i < x.Length; i++)
            {
                while (oi < sorted.Count && sorted[oi] + g <= i) oi++;
                bool on = false;
                for (int j = oi; j < sorted.Count && sorted[j] - 8 <= i; j++) if (i >= sorted[j] - 8 && i < sorted[j] + g) { on = true; break; }
                if (on) continue;
                double d2 = Math.Abs(x[i] - 2.0 * x[i - 1] + x[i - 2]);
                if (d2 < 1e-6) continue;
                int from = Math.Max(0, i - W), to = Math.Min(x.Length, from + 2 * W);
                double r = Rms(x, from, to) + 1e-9;
                worst = Math.Max(worst, d2 / r);
            }
            return worst;
        }

        /// <summary>Zero-crossing frequency over [i0, i1) (verify.mjs zcFreq): a pure partial's instantaneous pitch.</summary>
        public static double ZcFreq(float[] y, int i0, int i1)
        {
            double first = -1, last = -1; int n = 0;
            for (int i = i0 + 1; i < i1; i++)
                if (y[i - 1] < 0 && y[i] >= 0) { double z = i - 1 + y[i - 1] / (y[i - 1] - y[i]); if (first < 0) first = z; last = z; n++; }
            return n > 1 ? (n - 1) * Sr / (last - first) : 0;
        }

        /// <summary>common.mjs truePeakDb: 4x oversampling with a 48-tap Hann-windowed sinc, plus the sample peaks.</summary>
        public static double TruePeakDb(params float[][] channels)
        {
            const int taps = 12, os = 4;
            var h = new double[os, 2 * taps];
            for (int p = 0; p < os; p++)
                for (int k = -taps; k < taps; k++)
                {
                    double t = k + p / (double)os, x = Math.PI * t;
                    double sinc = t == 0 ? 1 : Math.Sin(x) / x, w = 0.5 + 0.5 * Math.Cos(Math.PI * t / taps);
                    h[p, k + taps] = sinc * w;
                }
            double peak = 0;
            foreach (var c in channels)
                for (int i = taps; i < c.Length - taps; i++)
                    for (int p = 0; p < os; p++)
                    {
                        double v = 0;
                        for (int k = 0; k < 2 * taps; k++) v += h[p, k] * c[i + taps - k];
                        peak = Math.Max(peak, Math.Abs(v));
                    }
            foreach (var c in channels) foreach (var v in c) peak = Math.Max(peak, Math.Abs(v));
            return 20 * Math.Log10(peak > 0 ? peak : 1e-12);
        }

        public static double PeakAbs(float[] x) { double m = 0; foreach (var v in x) m = Math.Max(m, Math.Abs(v)); return m; }
        public static double Rms(float[] x, int from, int to) { double e = 0; for (int i = from; i < to; i++) e += (double)x[i] * x[i]; return Math.Sqrt(e / Math.Max(1, to - from)); }
        public static double Db(double v) => 20 * Math.Log10(Math.Max(1e-12, v));
        public static float[] Sum(float[] a, float[] b) { var y = new float[a.Length]; for (int i = 0; i < y.Length; i++) y[i] = a[i] + b[i]; return y; }

        /// <summary>The per-1/3-octave levels of a whole signal (Hann frames of 8192, 50 % overlap), 31.5 Hz .. 16 kHz, dB.</summary>
        public static double[] ThirdOctaves(float[] x, out double[] centres)
        {
            const int N = 8192;
            var acc = new double[N / 2 + 1];
            for (int s = 0; s + N <= x.Length; s += N / 2)
            {
                var p = PowerHann(x, s, N, N);
                for (int k = 0; k < p.Length; k++) acc[k] += p[k];
            }
            var cs = new List<double>();
            for (double fc = 31.5; fc < 17000; fc *= Math.Pow(2, 1.0 / 3)) cs.Add(fc);
            centres = cs.ToArray();
            var o = new double[centres.Length];
            double df = Sr / N;
            for (int b = 0; b < centres.Length; b++)
            {
                double lo = centres[b] / Math.Pow(2, 1.0 / 6), hi = centres[b] * Math.Pow(2, 1.0 / 6), e = 0;
                for (int k = (int)Math.Ceiling(lo / df); k <= (int)Math.Floor(hi / df) && k < acc.Length; k++) e += acc[k];
                o[b] = 10 * Math.Log10(e + 1e-30);
            }
            return o;
        }
    }
}
