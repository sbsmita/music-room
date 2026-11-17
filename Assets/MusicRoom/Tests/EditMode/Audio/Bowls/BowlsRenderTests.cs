// Offline renders of the spec's demo phrases through the Unity bowls unit (phase A: the unit is not registered in the
// engine yet, so this drives the DSP through the rig), written for listening to
//   Builds/Listen/bowls/<key>.wav (48 kHz, 24-bit, dual mono, -18 LUFS, the Node files' own normalisation) and
//   compared with the Node renders in Reference~/renders/ sample by sample.
// The phrases are render.mjs's own (Reference~/render-acts.json, exported from the bowls reference engine: the
// seeded humanising, and the scripted-hands renders' unit calls). The room piece is compared dry (the unit's channel
// before the room chain) against the lab's dry render of the same phrase (the room is the room unit's job).
// Path: the Strict function pointer called from a Fast Burst entry (phase B's graph path); Burst off: managed.
using System;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public class BowlsRenderTests
    {
        const int SR = 48000;
        static string Listen => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Bowls/Reference~/renders");
        static string OutDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Listen", "bowls");

        [Serializable] class Jit { public double angleRad = double.NaN, speedFrac; public int seed = int.MinValue; }
        [Serializable] class ActJ { public double t, V = double.NaN, angle = double.NaN, h = double.NaN, dur, vf, N = double.NaN, angle0 = double.NaN, touchV = double.NaN; public string type; public int bowl, kind, level; public Jit jitter; }
        [Serializable] class CallJ { public string c, skind; public double t, V, angle, hs, N, vf, omega; public int i, surface, L; }
        [Serializable] class RenderJ { public string key; public double seconds; public bool room; public ActJ[] acts; public CallJ[] calls; }
        [Serializable] class FileJ { public double[] facing; public RenderJ[] renders; }

        static BowlStrikeKind Kind(string s) => s == "palm" ? BowlStrikeKind.Palm : s == "suede" ? BowlStrikeKind.Suede : s == "wood" ? BowlStrikeKind.Wood : BowlStrikeKind.Tap;

        static float[] Render(RenderJ r, double[] facing, BowlsPath path)
        {
            using var e = new BowlsRig(7, path);
            for (int b = 0; b < 3; b++) e.Pickup(b, facing[b], 0, true);
            if (r.acts != null)
            {
                var acts = new List<BowlsRig.Act>();
                foreach (var a in r.acts)
                {
                    var x = new BowlsRig.Act { T = a.t, Type = a.type, Bowl = a.bowl, Dur = a.dur, Vf = a.vf, Kind = a.kind, Level = a.level };
                    if (!double.IsNaN(a.V)) x.V = a.V;
                    if (!double.IsNaN(a.angle)) x.Angle = a.angle;
                    if (!double.IsNaN(a.h)) x.H = a.h;
                    if (!double.IsNaN(a.N)) x.N = a.N;
                    if (!double.IsNaN(a.angle0)) x.Angle0 = a.angle0;
                    if (!double.IsNaN(a.touchV)) x.TouchV = a.touchV;
                    if (a.jitter != null && !double.IsNaN(a.jitter.angleRad)) { x.Jitter = true; x.JitAngle = a.jitter.angleRad; x.JitSpeed = a.jitter.speedFrac; x.JitSeed = a.jitter.seed; }
                    acts.Add(x);
                }
                e.Schedule(acts, 0);
            }
            else
            {
                foreach (var c in r.calls)
                {
                    long at = (long)BowlsConst.JsRound(c.t * SR);
                    if (c.c == "strike") e.Strike(c.i, Kind(c.skind), c.V, c.angle, c.hs, at);
                    else if (c.c == "contact") e.Contact(c.i, c.surface, c.N, c.vf, c.angle, c.omega, c.hs, 0, 0, at);
                    else if (c.c == "release") e.Release(c.i, at);
                    else if (c.c == "damp") e.Damp(c.i, c.L, at);
                }
            }
            return e.Render((int)Math.Round(r.seconds * SR));
        }

        [Test]
        [Timeout(600000)]
        public void RendersTheDemoPhrasesAndMatchesTheNodeRenders()
        {
            BowlsBurstProbe.Install();
            var path = BurstCompiler.IsEnabled ? BowlsPath.StrictPointer : BowlsPath.Managed;
            string refFile = Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Bowls/Reference~/render-acts.json");
            var f = JsonUtility.FromJson<FileJ>(File.ReadAllText(refFile));
            Directory.CreateDirectory(OutDir);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Unity bowls renders (phase A, path {path}) against the Node renders; residual = 10 log10(sum (unity - node)^2 / sum node^2)");
            double worst = -999;
            foreach (var r in f.renders)
            {
                float[] y = Render(r, f.facing, path);
                string outKey = r.room ? r.key.Replace("-room", "-dry") : r.key;
                double lu = Loudness.Lufs(y, y), g = Math.Pow(10, (-18 - lu) / 20);
                var yn = new float[y.Length]; for (int i = 0; i < y.Length; i++) yn[i] = (float)(y[i] * g);
                WavFile.Write(Path.Combine(OutDir, outKey + ".wav"), new[] { yn, yn }, SR, 24);
                double res, resBest, peakDiff = 0;
                if (r.room)
                {
                    // the dry piece: float32 from the lab, compared before normalisation
                    float[] node = WavFile.Read(Path.Combine(Listen, "dry", outKey + ".wav"), out _)[0];
                    res = NullTest.ResidualDb(y, node); resBest = res;
                    for (int i = 0; i < Math.Min(y.Length, node.Length); i++) peakDiff = Math.Max(peakDiff, Math.Abs(y[i] - node[i]));
                }
                else
                {
                    float[] node = WavFile.Read(Path.Combine(Listen, r.key + ".wav"), out int sr)[0];
                    Assert.That(sr, Is.EqualTo(SR));
                    res = NullTest.ResidualDb(yn, node); resBest = NullTest.ResidualDb(yn, node, gain: NullTest.BestGain(yn, node));
                    for (int i = 0; i < Math.Min(yn.Length, node.Length); i++) peakDiff = Math.Max(peakDiff, Math.Abs(yn[i] - node[i]));
                }
                worst = Math.Max(worst, resBest);
                string line = $"{outKey,-36} {r.seconds,4:F0} s  residual {res,7:F1} dB (level-matched {resBest,7:F1} dB), max |diff| {peakDiff:E2}{(r.room ? " (dry, float)" : " (24-bit files)")}";
                report.AppendLine(line);
                Debug.Log("[BOWLS-RENDER] " + line);
            }
            File.WriteAllText(Path.Combine(OutDir, "bowls-compare.txt"), report.ToString());
            Assert.That(worst, Is.LessThan(-80.0), "every Unity render nulls against the Node render");
        }

        [Test]
        [Timeout(600000)]
        public void FastPathRendersStayCloseToTheReference()
        {
            if (!BurstCompiler.IsEnabled) Assert.Ignore("Burst is off");
            string refFile = Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Bowls/Reference~/render-acts.json");
            var f = JsonUtility.FromJson<FileJ>(File.ReadAllText(refFile));
            foreach (var r in f.renders)
            {
                float[] a = Render(r, f.facing, BowlsPath.Strict), b = Render(r, f.facing, BowlsPath.Fast);
                double res = NullTest.ResidualDb(b, a);
                double la = Loudness.Lufs(a, a), lb = Loudness.Lufs(b, b);
                Debug.Log($"[BOWLS-RENDER-FAST] {r.key}: FloatMode.Fast against Strict: residual {res:F1} dB, loudness {lb - la:+0.000;-0.000} LU");
                Assert.That(Math.Abs(lb - la), Is.LessThan(1.5), r.key + " loudness under Fast (informational: the chatter is chaotic)");
            }
        }
    }
}
