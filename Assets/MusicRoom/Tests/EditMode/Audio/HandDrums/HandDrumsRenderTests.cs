// The spec's demo phrases (handdrums.md 8) rendered by the C# unit (Burst FloatMode.Fast, as in the app):
//  1. per drum, sample by sample, against the Node DRY render of the same calls (Reference~/dry/, from
//     Tools~/export-refs.mjs): a null test (residual dB);
//  2. the listening files: the lab's direct-path head model (implemented below), then the app's master chain (MasterBus:
//     x0.9, the compressor, the 7.5 kHz shelf, the 0.98 ceiling), then one gain to -18 LUFS or -1.2 dBTP, whichever is
//     lower, written to Builds/Listen/handdrums/handdrums-<name>.wav. (Until 2026-11-11 they went through the upgraded
//     room's master and were nulled against the lab's WAVs; that master was deleted with the room unit, the classic room
//     being the only one, so the listening files are written, not compared.)
using System;
using System.IO;
using System.Text;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class HandDrumsRenderTests
    {
        const int Sr = 48000;
        static readonly string[] Names = { "ladder-open", "ladder-bass", "ladder-slap", "techniques", "tumbao", "congaline" };

        [Test, Timeout(600000)]
        public void TheDemoPhrasesMatchTheNodeRenders([ValueSource(nameof(Names))] string name)
        {
            var demo = HdRef.Json<HdDemo>("demo-calls.json");
            HdPhrase p = Array.Find(demo.phrases, q => q.name == name);
            Assert.That(p, Is.Not.Null, name + " in demo-calls.json");
            string dryPath = Path.Combine(HdRef.Dir, demo.dryDir, name + ".f32");
            if (!System.IO.File.Exists(dryPath)) Assert.Ignore("Node dry render missing (run Audio/Sound/HandDrums/Tools~/export-refs.mjs): " + dryPath);
            var y = HdRender.Phrase(p, HandDrumsEntry.BurstFast);
            var log = new StringBuilder($"[HandDrums] render {name} ({p.frames / (double)Sr:F1} s, {p.calls.Length} calls):");
            // 1. the dry unit output against the Node engine, per drum
            for (int d = 0; d < 2; d++)
            {
                float[] r = HdRef.ReadF32(dryPath, d * p.frames, p.frames);
                double res = NullTest.ResidualDb(y[d], r), mx = 0;
                for (int i = 0; i < r.Length; i++) mx = Math.Max(mx, Math.Abs(y[d][i] - r[i]));
                log.Append($" dry {HandDrumsNames.Drums[d]} null {res:F1} dB (max |diff| {mx:E1});");
                Assert.That(res, Is.LessThan(-80), $"{name}: the {HandDrumsNames.Drums[d]}'s dry output nulls against Node");
            }
            // 2. the listening file: direct path -> the app's master chain -> one gain to -18 LUFS or -1.2 dBTP
            var st = DirectStereo(y[0], y[1]);
            Master(st);
            double appLufs = Loudness.Lufs(st[0], st[1]), appTp = HdAnalysis.TruePeakDb(st[0], st[1]);
            double gDb = Math.Min(-18 - appLufs, -1.2 - appTp), g = Math.Pow(10, gDb / 20);
            log.Append($" master: app level {appLufs:F2} LUFS, {appTp:F2} dBTP, gain {gDb:+0.00;-0.00} dB;");
            for (int c = 0; c < 2; c++) for (int i = 0; i < st[c].Length; i++) st[c][i] = (float)(st[c][i] * g);
            Directory.CreateDirectory(HdRef.UnityListenDir);
            string outPath = Path.Combine(HdRef.UnityListenDir, $"handdrums-{name}.wav");
            WavFile.Write(outPath, st, Sr, 24);
            Debug.Log(log + " -> " + outPath);
            Assert.That(double.IsNaN(appLufs) || double.IsInfinity(appLufs), Is.False, name + ": the listening file has a level");
            Assert.IsTrue(File.Exists(outPath));
        }

        /// <summary>Review item 12 (the glide on hard basses): the same four hard basses (conga and tumba, u .8 and 1) with the
        /// designed glide, then with half of it (kappa and glideMax x 0.5), in one listening file, with the peak of each
        /// head's s(t). The unit keeps the designed value (the reference's; checked by G1); the file lets an ear decide.</summary>
        [Test]
        public void TheGlideABForListening()
        {
            var log = new System.Text.StringBuilder("[HandDrums] glide A/B (designed | half), peak cents u .8 / u 1:");
            float[][] Seg(double scale)
            {
                using var h = new HandDrumsOffline();
                h.State->D0.Kappa *= scale; h.State->D0.GlideMax *= scale;
                h.State->D1.Kappa *= scale; h.State->D1.GlideMax *= scale;
                double[] at = { 0.3, 1.3, 2.3, 3.3 };
                for (int i = 0; i < 4; i++) h.Sound.Stroke(i < 2 ? 0 : 1, 1, HandStroke.Bass, i % 2 == 0 ? 0.8 : 1.0, 0.35, HandDrumsOffline.At(HdRef.JsAt(at[i])));
                int n = (int)(4.5 * Sr);
                var y = new[] { new float[n], new float[n] };
                var peak = new double[4];
                for (int s0 = 0; s0 < n; s0 += 32)
                {
                    h.RenderBlock(y[0], y[1], s0, Math.Min(32, n - s0));
                    double t = s0 / (double)Sr;
                    int k = t < 1.3 ? 0 : t < 2.3 ? 1 : t < 3.3 ? 2 : 3;
                    double c = 1200 * Math.Log(k < 2 ? h.State->D0.S : h.State->D1.S, 2);
                    peak[k] = Math.Max(peak[k], c);
                }
                log.Append($" x{scale}: conga {peak[0]:F1} / {peak[1]:F1}, tumba {peak[2]:F1} / {peak[3]:F1};");
                return y;
            }
            var a = Seg(1); var b = Seg(0.5);
            int na = a[0].Length;
            var both = new[] { new float[2 * na], new float[2 * na] };
            for (int d = 0; d < 2; d++) { Array.Copy(a[d], both[d], na); Array.Copy(b[d], 0, both[d], na, na); }
            var st = DirectStereo(both[0], both[1]);
            Master(st);
            double gDb = Math.Min(-18 - Loudness.Lufs(st[0], st[1]), -1.2 - HdAnalysis.TruePeakDb(st[0], st[1])), g = Math.Pow(10, gDb / 20);
            for (int c = 0; c < 2; c++) for (int i = 0; i < st[c].Length; i++) st[c][i] = (float)(st[c][i] * g);
            Directory.CreateDirectory(HdRef.UnityListenDir);
            string path = Path.Combine(HdRef.UnityListenDir, "handdrums-glide-ab.wav");
            WavFile.Write(path, st, Sr, 24);
            Debug.Log(log + " -> " + path);
            Assert.IsTrue(File.Exists(path));
        }

        // ---- render.mjs directStereo (the room lab's head model: distance law, Woodworth ITD, Brown-Duda shadow)
        const double CSound = 343.0, HeadRadius = 0.0875, HrtfFrontDb = -3.0;
        static double DistGain(double d) => 0.6 / (0.6 + 1.1 * (Math.Max(d, 0.6) - 0.6));
        static double BdAlpha(double theta) => (1 + 0.05) + (1 - 0.05) * Math.Cos((theta / (150 * Math.PI / 180)) * Math.PI);
        static void BdCoefs(double alpha, double sr, out double b0, out double b1, out double a1)
        {
            double w0x2 = 2 * CSound / HeadRadius, K = 2 * sr, den = w0x2 + K;
            b0 = (w0x2 + alpha * K) / den; b1 = (w0x2 - alpha * K) / den; a1 = (w0x2 - K) / den;
        }
        static double WoodworthItd(double lat) => (HeadRadius / CSound) * (lat + Math.Sin(lat));

        public static float[][] DirectStereo(float[] conga, float[] tumba)
        {
            int n = conga.Length;
            var L = new float[n]; var R = new float[n];
            double gH = Math.Pow(10, HrtfFrontDb / 20);
            double[][] pos = { new[] { -0.185, 0.75, 0.57 }, new[] { 0.19, 0.755, 0.57 } };
            double[] head = { 0, 1.2, 0 };
            float[][] src = { conga, tumba };
            for (int s = 0; s < 2; s++)
            {
                double dx = pos[s][0] - head[0], dy = pos[s][1] - head[1], dz = pos[s][2] - head[2], d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double cr = Math.Max(-1, Math.Min(1, dx / d));
                double lat = Math.Asin(cr), itd = WoodworthItd(lat), g = DistGain(d) * gH;
                for (int ear = 0; ear < 2; ear++)
                {
                    float[] o = ear == 0 ? L : R;
                    double delay = (d / CSound + (ear == 0 ? itd / 2 : -itd / 2)) * Sr, theta = ear == 0 ? Math.PI - Math.Acos(cr) : Math.Acos(cr);
                    BdCoefs(BdAlpha(theta), Sr, out double b0, out double b1, out double a1);
                    int di = (int)Math.Floor(delay); double fr = delay - di;
                    double x1 = 0, y1 = 0;
                    float[] x = src[s];
                    for (int i = 0; i < n; i++)
                    {
                        int j = i - di;
                        double v = (j >= 0 ? x[j] * (1 - fr) : 0) + (j - 1 >= 0 ? x[j - 1] * fr : 0);
                        double yy = b0 * v + b1 * x1 - a1 * y1;
                        x1 = v; y1 = yy;
                        o[i] = (float)(o[i] + yy * g);
                    }
                }
            }
            return new[] { L, R };
        }

        /// <summary>The app's master chain (MasterBus: input gain 0.9, the compressor -14 dB / knee 18 / 4:1 / 3 ms / 180 ms, the
        /// 7.5 kHz +1.5 dB shelf, the 0.98 ceiling), block by block.</summary>
        public static void Master(float[][] ch)
        {
            int n = ch[0].Length;
            var comp = new MusicRoom.Audio.Dsp.DynamicsCompressor(Sr, -14, 18, 4, 0.003, 0.18, 0.0, 2);
            var shL = MusicRoom.Audio.Dsp.Biquad.Create(MusicRoom.Audio.Dsp.BiquadType.Highshelf, 7500, 0, 1.5, Sr);
            var shR = MusicRoom.Audio.Dsp.Biquad.Create(MusicRoom.Audio.Dsp.BiquadType.Highshelf, 7500, 0, 1.5, Sr);
            var inter = new float[2 * 256];
            for (int s0 = 0; s0 < n; s0 += 256)
            {
                int m = Math.Min(256, n - s0);
                for (int i = 0; i < m; i++) { inter[2 * i] = ch[0][s0 + i] * 0.9f; inter[2 * i + 1] = ch[1][s0 + i] * 0.9f; }
                comp.Process(inter, 2, m);
                for (int i = 0; i < m; i++)
                {
                    ch[0][s0 + i] = Mathf.Clamp(shL.Process(inter[2 * i]), -0.98f, 0.98f);
                    ch[1][s0 + i] = Mathf.Clamp(shR.Process(inter[2 * i + 1]), -0.98f, 0.98f);
                }
            }
        }
    }
}
