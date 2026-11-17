// The page-turn sound (sheet.md 5.5, 9.2 PaperSoundTests): SheetPaperSound's presets and its Turn schedule, played through
// a test-only transcription of the lab's one-shot pool reference (page-sound.mjs Pool: float32 state, xorshift noise lanes,
// reference biquads, exp envelopes), give the lab's vectors: Turn(0.62, 1, seed 1) at sample 14400, its first 32 samples and the RMS
// of every 50 ms. Paper, not a whoosh: no lane above Q 2.4 and no tau over 0.2 s. (Phase B plays the same shots through
// the engine's one-shot pool; phase A's engine has none.)
using System;
using System.Collections.Generic;
using System.Linq;
using MusicRoom.Sheet;
using NUnit.Framework;

namespace MusicRoom.Tests.Sheet
{
    public class SheetPaperSoundTests
    {
        const int SR = 48000, Block = 256;

        sealed class Lane { public float B0, B1, B2, Na1, Na2, Mul, Mix; public uint X; public float Z1, Z2, G; }
        sealed class Voice { public Lane[] Lanes; public float Gain; public int Left, Delay; }
        struct Pending { public int Preset; public double Gain; public long At; public uint Seed; public int Order; }

        static float F(double v) => (float)v;

        static Lane[] Build(SheetPaperSound.PresetSpec p)
        {
            return p.Lanes.Select(l =>
            {
                double w = 2 * Math.PI * l.Hz / SR, cs = Math.Cos(w), al = Math.Sin(w) / (2 * l.Q);
                double b0, b1, b2;
                if (l.Type == SheetPaperSound.Lane.LowPass) { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = b0; }
                else if (l.Type == SheetPaperSound.Lane.HighPass) { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = b0; }
                else { b0 = al; b1 = 0; b2 = -al; }
                double a0 = 1 + al;
                return new Lane { B0 = F(b0 / a0), B1 = F(b1 / a0), B2 = F(b2 / a0), Na1 = F((2 * cs) / a0), Na2 = F(-(1 - al) / a0), Mul = F(Math.Exp(-1 / ((double)l.Tau * SR))), Mix = F(l.Mix) };
            }).ToArray();
        }

        static uint SplitMix32(long a)
        {
            unchecked
            {
                int x = (int)(a + 0x9e3779b9L);
                int z = x;
                z = (z ^ (int)((uint)z >> 16)) * (int)0x85ebca6b;
                z = (z ^ (int)((uint)z >> 13)) * (int)0xc2b2ae35;
                return (uint)(z ^ (int)((uint)z >> 16));
            }
        }

        /// <summary>page-sound.mjs Pool.render of the given shots (sample times) into n samples.</summary>
        static float[] Render(List<Pending> shots, int n)
        {
            var built = SheetPaperSound.Presets.Select(Build).ToArray();
            var pending = shots.OrderBy(s => s.At).ThenBy(s => s.Order).ToList();
            var voices = new List<Voice>();
            var y = new float[n];
            var blk = new float[Block];
            for (int b = 0; b < n; b += Block)
            {
                Array.Clear(blk, 0, Block);
                while (pending.Count > 0 && pending[0].At < b + Block)
                {
                    var e = pending[0]; pending.RemoveAt(0);
                    var spec = SheetPaperSound.Presets[e.Preset];
                    var lanes = built[e.Preset].Select((l, i) => new Lane { B0 = l.B0, B1 = l.B1, B2 = l.B2, Na1 = l.Na1, Na2 = l.Na2, Mul = l.Mul, Mix = l.Mix, X = SplitMix32((long)e.Seed * 4 + i) is uint u && u != 0 ? u : 1u, G = 1f }).ToArray();
                    voices.Add(new Voice { Lanes = lanes, Gain = F(e.Gain), Left = (int)Math.Floor(spec.Seconds * SR + 0.5), Delay = (int)Math.Max(0, e.At - b) });
                }
                foreach (var v in voices)
                {
                    for (int i = v.Delay; i < Block && v.Left > 0; i++, v.Left--)
                    {
                        double s = 0;
                        foreach (var l in v.Lanes)
                        {
                            unchecked { l.X ^= l.X << 13; l.X ^= l.X >> 17; l.X ^= l.X << 5; }
                            float xin = F((int)l.X * 4.656612873077393e-10);
                            float yy = F((double)l.B0 * xin + l.Z1);
                            l.Z1 = F((double)l.B1 * xin + (double)l.Na1 * yy + l.Z2);
                            l.Z2 = F((double)l.B2 * xin + (double)l.Na2 * yy);
                            s += (double)yy * l.G * l.Mix;
                            l.G = F((double)l.G * l.Mul);
                        }
                        blk[i] = F(blk[i] + F(s * v.Gain));
                    }
                    v.Delay = 0;
                }
                voices.RemoveAll(v => v.Left <= 0);
                Array.Copy(blk, 0, y, b, Math.Min(Block, n - b));
            }
            return y;
        }

        [Test]
        public void ATurnMatchesTheLabsPageSoundVectors()
        {
            var v = SheetTestData.O(SheetTestData.Json("page-sound-vectors.json"));
            var shots = new SheetPaperSound.Shot[SheetPaperSound.MaxShotsPerTurn];
            int count = SheetPaperSound.Turn(shots, 0, 0.0, 0.62f, 1f, 1u);
            Assert.That(count, Is.InRange(1 + 3 + 3 + 1, SheetPaperSound.MaxShotsPerTurn));
            const long start = 14400;   // 0.30 s
            var pend = new List<Pending>();
            for (int i = 0; i < count; i++)
                pend.Add(new Pending { Preset = shots[i].Preset, Gain = shots[i].Gain, At = start + (long)Math.Floor(shots[i].At * SR + 0.5), Seed = shots[i].Seed ^ (uint)(i + 1), Order = i });
            var y = Render(pend, (int)(start + 1.1 * SR));
            var first = SheetTestData.L(v["first32"]);
            for (int i = 0; i < 32; i++) Assert.That(y[start + i], Is.EqualTo(SheetTestData.D(first[i])).Within(2e-6), $"sample {i}");
            var rms = SheetTestData.L(v["rms50ms"]);
            for (int w = 0; w < rms.Count; w++)
            {
                double a = 0;
                for (int j = 0; j < 2400; j++) { double s = y[start + w * 2400 + j]; a += s * s; }
                double mine = Math.Sqrt(a / 2400), want = SheetTestData.D(rms[w]);
                if (want < 1e-6) { Assert.That(mine, Is.LessThan(1e-6)); continue; }
                Assert.That(20 * Math.Log10(mine / want), Is.EqualTo(0).Within(0.5), $"50 ms window {w}");
            }
        }

        [Test]
        public void PaperNotAWhoosh()
        {
            foreach (var p in SheetPaperSound.Presets)
                foreach (var l in p.Lanes)
                {
                    Assert.That(l.Q, Is.LessThanOrEqualTo(2.4f), $"{p.Name}: no ringing lane");
                    Assert.That(l.Tau, Is.LessThanOrEqualTo(0.2f), $"{p.Name}: no hanging hiss");
                }
            var shots = new SheetPaperSound.Shot[SheetPaperSound.MaxShotsPerTurn];
            int n = SheetPaperSound.Turn(shots, 0, 1.0, 0.34f, SheetPaperSound.RiffleLevel(2), 7u);
            Assert.That(shots.Take(n).Min(s => s.At), Is.EqualTo(1.0));
            Assert.That(shots.Take(n).Max(s => s.At), Is.EqualTo(1.0 + 0.34).Within(1e-6), "the landing at T");
            Assert.That(SheetPaperSound.RiffleLevel(1), Is.LessThan(SheetPaperSound.RiffleLevel(0)));
        }
    }
}
