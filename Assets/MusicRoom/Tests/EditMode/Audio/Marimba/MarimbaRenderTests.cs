// The spec's demo phrases (spec 10; the marimba lab's render.mjs) rendered by the C# unit and written to
// Builds/Listen/marimba/marimba-*.wav (48 kHz stereo 24-bit, dual mono, the Node file's own loudness gain), then
// compared with the Node renders in Reference~/renders/: a null test (residual energy re the Node file), the
// sample-exact share of the 24-bit words (strict render), and a third-octave long-term spectrum match.
// The scores are render.mjs's own (the same seeded humanising), exported with their exact gains by
// tools/export-unity.mjs, which checks that they reproduce the Node WAVs. The room render (amazing-grace-room) needs the
// room unit and the three spatial sources: phase B.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using UnityEngine;
using static MusicRoom.Tests.Audio.MarimbaRef;

namespace MusicRoom.Tests.Audio
{
    public class MarimbaRenderTests
    {
        static readonly string[] Pieces = { "velocity-ladder", "range-chromatic", "techniques", "amazing-grace", "canon-pattern", "first-lesson" };
        static readonly Dictionary<string, string> Report = new Dictionary<string, string>();

        /// <summary>Third-octave band energies (dB re total) of the long-term spectrum, 50 Hz .. 16 kHz (Welch, 4096-point Hann).</summary>
        static double[] ThirdOctaves(float[] y, out double[] centres)
        {
            const int N = 4096, hop = 2048;
            var re = new double[N]; var im = new double[N];
            var power = new double[N / 2 + 1];
            var win = new double[N];
            for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
            for (int s = 0; s + N <= y.Length; s += hop)
            {
                for (int i = 0; i < N; i++) { re[i] = y[s + i] * win[i]; im[i] = 0; }
                Fft(re, im);
                for (int k = 0; k <= N / 2; k++) power[k] += re[k] * re[k] + im[k] * im[k];
            }
            var cs = new List<double>();
            for (double f = 50; f <= 16000; f *= Math.Pow(2, 1 / 3.0)) cs.Add(f);
            centres = cs.ToArray();
            var bands = new double[cs.Count];
            double total = 0;
            for (int k = 1; k <= N / 2; k++) total += power[k];
            for (int b = 0; b < cs.Count; b++)
            {
                double lo = cs[b] / Math.Pow(2, 1 / 6.0), hi = cs[b] * Math.Pow(2, 1 / 6.0), e = 0;
                for (int k = 1; k <= N / 2; k++) { double f = k * 48000.0 / N; if (f >= lo && f < hi) e += power[k]; }
                bands[b] = 10 * Math.Log10(e / total + 1e-30);
            }
            return bands;
        }

        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                        double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }

        [TestCaseSource(nameof(Pieces))]
        public void DemoPhrase_RendersAndNullsAgainstTheNodeRender(string name)
        {
            var sc = Obj(Load("marimba-scores.json"), name);
            var events = Arr(sc, "events");
            int samples = (int)Num(sc, "samples");
            double g = Num(sc, "gain");
            string nodePath = Path.Combine(ListenDir, $"marimba-{name}.wav");
            Assume.That(File.Exists(nodePath), "the Node render " + nodePath);
            var node = WavFile.Read(nodePath, out int sr)[0];
            Assert.That(sr, Is.EqualTo(48000));
            Assert.That(node.Length, Is.EqualTo(samples), "the Node render's length");

            // the app's path: Burst FloatMode.Fast, written as the listening file
            var fast = MarimbaOffline.Mono(RenderScore(events, samples, MarimbaRenderMode.Fast));
            var outF = new float[samples];
            for (int i = 0; i < samples; i++) outF[i] = (float)(fast[i] * g);
            string outPath = Path.Combine(UnityListenDir, $"marimba-{name}.wav");
            WavFile.Write(outPath, new[] { outF, (float[])outF.Clone() }, 48000, 24);
            double nullDb = NullTest.ResidualDb(outF, node);
            double bg = NullTest.BestGain(outF, node);
            double nullMatched = NullTest.ResidualDb(outF, node, gain: bg);

            // the strict path: the 24-bit words of the Node file, word for word (round half up, as the lab's writer)
            var strict = MarimbaOffline.Mono(RenderScore(events, samples, MarimbaRenderMode.Strict));
            var nodeWords = WavFile.Read(nodePath, out _)[0];
            int same = 0, off1 = 0;
            for (int i = 0; i < samples; i++)
            {
                double v = Math.Max(-1, Math.Min(1, (float)(strict[i] * g)));   // the lab scales into a Float32Array first
                int q = (int)Math.Floor(v * 8388607 + 0.5);
                int w = (int)Math.Round(nodeWords[i] * 8388608.0);
                if (q == w) same++; else if (Math.Abs(q - w) == 1) off1++;
            }
            double strictNull = NullTest.ResidualDb(Scale(strict, g), node);

            // spectral match (long-term third-octave bands, dB re total)
            var bu = ThirdOctaves(outF, out var centres);
            var bn = ThirdOctaves(node, out _);
            double worstBand = 0; double worstAt = 0;
            for (int b = 0; b < bu.Length; b++)
                if (bn[b] > -60 && Math.Abs(bu[b] - bn[b]) > worstBand) { worstBand = Math.Abs(bu[b] - bn[b]); worstAt = centres[b]; }

            string line = $"{name,-16} {samples / 48000.0,5:F1} s  null (Fast) {nullDb,7:F1} dB  level-matched {nullMatched,7:F1} dB (gain {bg:F7})  strict null {strictNull,7:F1} dB, 24-bit words identical {100.0 * same / samples,6:F2} % (+-1 LSB {100.0 * off1 / samples:F2} %)  worst 1/3-oct band {worstBand:F3} dB at {worstAt:F0} Hz";
            Report[name] = line;
            Debug.Log("[MARIMBA] render " + line);
            WriteReadme();
            Assert.That(nullDb, Is.LessThan(-80), "Burst on: residual <= -80 dB re the Node render (spec 9.1 asks <= -60)");
            Assert.That(strictNull, Is.LessThan(-120), "strict: down at the 24-bit floor");
            Assert.That(worstBand, Is.LessThan(0.01), "long-term spectra match");
        }

        static float[] Scale(float[] y, double g) { var o = new float[y.Length]; for (int i = 0; i < y.Length; i++) o[i] = (float)(y[i] * g); return o; }

        static void WriteReadme()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Marimba: the Unity renders (C# unit, Burst FloatMode.Fast as on the headset) of the spec's demo phrases.");
            sb.AppendLine("Written by Assets/MusicRoom/Tests/EditMode/Audio/Marimba/MarimbaRenderTests.cs (u-marimba clone, phase A).");
            sb.AppendLine("Same scores and the same loudness gain as the Node files one folder up (render.mjs), so they should sound identical.");
            sb.AppendLine("48 kHz stereo 24-bit, dual mono (the app spatializes the three sources). The room take (amazing-grace-room) waits for phase B.");
            sb.AppendLine();
            sb.AppendLine("Null test against the Node render (residual energy re the Node file; lower is closer):");
            foreach (var p in Pieces) if (Report.TryGetValue(p, out var l)) sb.AppendLine("  " + l);
            Directory.CreateDirectory(UnityListenDir);
            File.WriteAllText(Path.Combine(UnityListenDir, "marimba-unity-vs-node.txt"), sb.ToString());   // the folder is shared by the five instruments
        }
    }
}
