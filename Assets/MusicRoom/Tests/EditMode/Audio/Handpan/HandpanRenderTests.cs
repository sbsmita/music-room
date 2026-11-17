// The listening phrases of the handpan lab's render.mjs (exported as unit calls by export-phrases.mjs, Reference~/phrases.json)
// rendered through the C# unit and null-tested against the Node WAVs in Reference~/renders/ (each Node file is the dry
// unit output x one loudness gain, 24-bit). The Unity renders are written, at the same gain, to
// Builds/Listen/handpan/handpan-<key>.wav (the Burst path: what the headset runs), with a report next to them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MusicRoom.Audio.Burst;
using MusicRoom.Handpan;
using MusicRoom.Tests.Handpan;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public class HandpanRenderTests
    {
        static string Listen => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Handpan/Reference~/renders");
        static string OutDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Listen", "handpan");

        static Dictionary<string, object> Phrases() =>
            HandpanJson.O(HandpanJson.Load(Path.Combine(HandpanJson.ReferenceDir("Audio/Handpan"), "phrases.json")));

        /// <summary>One phrase through the unit (render.mjs renderEvents; a tak is HandpanModule.StrikeTak).</summary>
        public static float[] RenderPhrase(Dictionary<string, object> f, bool burst)
        {
            var voice = f.Text("voice") == "measured" ? HandpanVoice.Measured : HandpanVoice.Calm;
            using var rig = new HandpanRig(voice, burst);
            foreach (object eo in f.Arr("events"))
            {
                var e = HandpanJson.O(eo);
                long at = HandpanCommands.JsRound(e.Num("t") * HandpanRig.Sr);
                switch (e.Text("op"))
                {
                    case "tak": rig.Tak(at, e.Num("vel"), e.Num("X"), e.Num("Z")); break;
                    case "damp": rig.Damp(at, HandpanJson.I(e["field"]), HandpanJson.I(e["slot"]), e.Num("strength"), e.Num("u"), e.Num("v"), HandpanJson.I(e["kind"])); break;
                    default: rig.Strike(at, HandpanJson.I(e["field"]), e.Num("vel"), e.Num("u"), e.Num("v"), (HandpanTech)HandpanJson.I(e["tech"])); break;
                }
            }
            return rig.Render(HandpanJson.I(f["frames"]));
        }

        [Test]
        public void ThePhrasesNullAgainstTheNodeRendersAndAreWritten()
        {
            var doc = Phrases();
            var report = new StringBuilder();
            report.AppendLine("Handpan: Unity unit vs the Node reference renders (Reference~/renders/handpan-*.wav)");
            report.AppendLine("residual = 10 log10(sum (unity x gain - node)^2 / sum node^2), whole file, channel 0; the Node file is 24-bit (its own floor is about -126 dB)");
            report.AppendLine($"{"file",-34} {"exact (managed, two roundings)",31} {"burst (device path, FloatMode.Fast)",37}");
            Directory.CreateDirectory(OutDir);
            foreach (object fo in doc.Arr("files"))
            {
                var f = HandpanJson.O(fo);
                string key = f.Text("key");
                double gain = f.Num("gain");
                float[] node = WavFile.Read(Path.Combine(Listen, $"handpan-{key}.wav"), out int sr)[0];
                Assert.AreEqual(48000, sr);
                float[] exact = RenderPhrase(f, burst: false), burst = RenderPhrase(f, burst: true);
                Assert.AreEqual(node.Length, exact.Length, key + " length");
                double rExact = NullTest.ResidualDb(exact, node, gain: gain), rBurst = NullTest.ResidualDb(burst, node, gain: gain);
                // a spectral view of the same match: the level of the Burst residual relative to the signal in 4 bands
                string bands = BandResidual(burst, node, gain);
                Debug.Log($"[Handpan] null {key}: exact {rExact:F1} dB, burst {rBurst:F1} dB ({bands})");
                report.AppendLine($"{"handpan-" + key + ".wav",-34} {rExact,28:F1} dB {rBurst,34:F1} dB   burst residual by band: {bands}");
                var scaled = new float[burst.Length];
                for (int i = 0; i < burst.Length; i++) scaled[i] = (float)(burst[i] * gain);
                WavFile.Write(Path.Combine(OutDir, $"handpan-{key}.wav"), new[] { scaled, scaled }, 48000, 24);
                Assert.That(rExact, Is.LessThan(-110.0), key + ": the managed path is the reference (24-bit floor)");
                Assert.That(rBurst, Is.LessThan(-70.0), key + ": the Burst path nulls below -70 dB (handpan.md 9.1)");
                foreach (float v in burst) Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), key);
            }
            report.AppendLine();
            report.AppendLine("The Unity files here are the Burst render x the same loudness gain as the Node file, 48 kHz 24-bit dual mono.");
            File.WriteAllText(Path.Combine(OutDir, "handpan-null-report.txt"), report.ToString());
            Debug.Log("[Handpan] renders and report: " + OutDir);
        }

        /// <summary>Residual vs signal energy (dB) in four bands (one-pole splits at 200 Hz, 1 kHz, 4 kHz).</summary>
        static string BandResidual(float[] y, float[] node, double gain)
        {
            double[] edges = { 200, 1000, 4000 };
            var sb = new StringBuilder();
            float[] prevSig = null, prevRes = null;
            // band = the difference of two one-pole low-passes
            var d = new float[node.Length];
            for (int i = 0; i < node.Length; i++) d[i] = (float)(y[i] * gain - node[i]);
            double[] eS = new double[4], eR = new double[4];
            for (int b = 0; b < 3; b++)
            {
                double a = Math.Exp(-2 * Math.PI * edges[b] / 48000);
                float[] s2 = OnePole(node, a), r2 = OnePole(d, a);
                eS[b] = BandEnergy(s2, prevSig); eR[b] = BandEnergy(r2, prevRes);
                prevSig = s2; prevRes = r2;
            }
            eS[3] = BandEnergy(node, prevSig); eR[3] = BandEnergy(d, prevRes);
            string[] names = { "<200", "200-1k", "1k-4k", ">4k" };
            for (int b = 0; b < 4; b++) sb.Append($"{names[b]} {10 * Math.Log10(Math.Max(eR[b], 1e-300) / Math.Max(eS[b], 1e-300)):F0}{(b < 3 ? ", " : "")}");
            return sb.ToString();
        }

        static float[] OnePole(float[] x, double a)
        {
            var y = new float[x.Length];
            double z = 0;
            for (int i = 0; i < x.Length; i++) { z = (1 - a) * x[i] + a * z; y[i] = (float)z; }
            return y;
        }

        static double BandEnergy(float[] lp, float[] lower)
        {
            double e = 0;
            for (int i = 0; i < lp.Length; i++) { double v = lp[i] - (lower != null ? lower[i] : 0); e += v * v; }
            return e;
        }
    }
}
