// Offline renders against the spec 06 fingerprints (harp: bit-exact; piano: the reference render, ~1e-4).
using System;
using MusicRoom.Audio.Offline;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio
{
    public class RendererTests
    {
        static void Stats(float[] d, out double peak, out double rms)
        {
            peak = 0; double sq = 0;
            foreach (var v in d) { peak = Math.Max(peak, Math.Abs(v)); sq += v * v; }
            rms = Math.Sqrt(sq / d.Length);
        }

        [Test]
        public void HarpOldC3IsBitExact()
        {
            var o = HarpRenderer.RenderOld(48, 48000, 2048);
            Assert.That(o.Length, Is.EqualTo(216000));
            Assert.That(o[0], Is.EqualTo(0.187185138f).Within(1e-7f));
            Assert.That(o[1], Is.EqualTo(0.409280658f).Within(1e-7f));
            Assert.That(o[2], Is.EqualTo(0.06184588f).Within(1e-7f));
            Assert.That(o[10], Is.EqualTo(0.101109423f).Within(1e-7f));
            Assert.That(o[1000], Is.EqualTo(-0.237092108f).Within(1e-7f));
            Assert.That(o[10000], Is.EqualTo(0.044145748f).Within(1e-7f));
            Assert.That(o[100000], Is.EqualTo(-0.0043207542f).Within(1e-7f));
            Stats(o, out double peak, out double rms);
            Assert.That(peak, Is.EqualTo(0.8).Within(1e-6));
            Assert.That(rms, Is.EqualTo(0.0351909529).Within(1e-7));
        }

        [Test]
        public void HarpOldC6IsBitExact()
        {
            var o = HarpRenderer.RenderOld(84, 48000, 2084);
            Assert.That(o.Length, Is.EqualTo(77760));
            Assert.That(o[0], Is.EqualTo(-0.232360363f).Within(1e-7f));
            Assert.That(o[3], Is.EqualTo(-0.474681139f).Within(1e-7f));
            Assert.That(o[1000], Is.EqualTo(-0.00695758965f).Within(1e-7f));
            Assert.That(o[10000], Is.EqualTo(0.0352147929f).Within(1e-7f));
            Stats(o, out _, out double rms);
            Assert.That(rms, Is.EqualTo(0.0285492586).Within(1e-7));
        }

        [Test]
        public void HarpStringTableIsCMajorC3ToC6()
        {
            Assert.That(HarpRenderer.Strings.Length, Is.EqualTo(22));
            Assert.That(HarpRenderer.Strings[0], Is.EqualTo(48));
            Assert.That(HarpRenderer.Strings[7], Is.EqualTo(60));
            Assert.That(HarpRenderer.Strings[21], Is.EqualTo(84));
            for (int i = 0; i < 22; i++) Assert.That(HarpRenderer.MidiOf(i), Is.EqualTo(HarpRenderer.Strings[i]));
        }

        [Test]
        public void PianoC4MatchesTheReferenceRender()
        {
            var d = PianoRenderer.Render(60, 48000, 1060);
            Assert.That(d.Length, Is.EqualTo(215503));
            Assert.That(d[0], Is.EqualTo(0.0036449458f).Within(3e-4f));
            Assert.That(d[1], Is.EqualTo(0.0111054247f).Within(3e-4f));
            Assert.That(d[2], Is.EqualTo(0.0195513908f).Within(3e-4f));
            Assert.That(d[100], Is.EqualTo(-0.00864864513f).Within(3e-4f));
            Assert.That(d[1000], Is.EqualTo(0.0960310027f).Within(3e-4f));
            Assert.That(d[10000], Is.EqualTo(-0.00317950849f).Within(3e-4f));
            Assert.That(d[100000], Is.EqualTo(-0.0107802749f).Within(3e-4f));
            Stats(d, out double peak, out double rms);
            Assert.That(peak, Is.EqualTo(0.9).Within(1e-5));
            Assert.That(rms, Is.EqualTo(0.0741663125).Within(2e-4));
        }

        /// The start-up path renders into buffers allocated up front, with per-thread scratch reused key after key: the
        /// result must not depend on it (two different keys through the same scratch, then the first again).
        [Test]
        public void RenderIntoAndScratchReuseAreExact()
        {
            var a = PianoRenderer.Render(60, 48000, 1060);
            var dst = new float[PianoRenderer.Length(30, 48000)];
            PianoRenderer.RenderInto(30, 48000, 1030, dst);       // a longer key through the same scratch
            var b = PianoRenderer.RenderInto(60, 48000, 1060, new float[PianoRenderer.Length(60, 48000)]);
            Assert.That(b, Is.EqualTo(a));
            Assert.Throws<ArgumentException>(() => PianoRenderer.RenderInto(60, 48000, 1060, new float[10]));
            var h = HarpRenderer.RenderOld(60, 48000, 2060);
            var h2 = HarpRenderer.RenderOldInto(60, 48000, 2060, new float[HarpRenderer.Length(60, 48000)]);
            Assert.That(h2, Is.EqualTo(h));
            var x = new float[2205];
            for (int i = 0; i < x.Length; i++) x[i] = (float)Math.Sin(i * 0.05);
            var y1 = MusicRoom.Audio.Synths.RobotVoice.Process(x, 22050);
            var y2 = MusicRoom.Audio.Synths.RobotVoice.ProcessInto(x, 22050, new float[x.Length]);
            Assert.That(y2, Is.EqualTo(y1), "robot chain: the reused comb scratch starts clean");
        }

        [Test]
        public void PianoC8MatchesTheReferenceRender()
        {
            var d = PianoRenderer.Render(108, 48000, 1108);
            Assert.That(d.Length, Is.EqualTo(67200));
            Assert.That(d[1000], Is.EqualTo(0.46665132f).Within(5e-4f));
            Assert.That(d[10000], Is.EqualTo(0.0651850328f).Within(5e-4f));
            Stats(d, out _, out double rms);
            Assert.That(rms, Is.EqualTo(0.0888772433).Within(3e-4));
        }
    }
}
