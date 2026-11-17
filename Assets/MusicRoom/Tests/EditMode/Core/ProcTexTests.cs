// Core ProcTex (the shared texture generators) against fingerprints of the reference texture formulas, computed in node
// (scratch tool proctex/ref.mjs: the pixel writes go through a Uint8ClampedArray, which clamps and rounds half to
// even). Noise is bit-exact; pixel bytes may differ by 1 where Math.sin differs from the JS engine in the last bit.
using System;
using MusicRoom.Core;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Core
{
    public class ProcTexTests
    {
        static readonly double[][] HashRef = {
            new double[] { 0, 0, 0.0 },
            new double[] { 1, 0, 0.5081244610501743 },
            new double[] { 0, 1, 0.7682745544631674 },
            new double[] { 123, 456, 0.9080349362706847 },
            new double[] { -7, 3, 0.3340498575321515 },
            new double[] { 100000, -2, 0.260661649578405 },
            new double[] { 37, 37, 0.3961074017910537 },
            new double[] { 5, 12, 0.4370673118245479 },
        };
        static readonly double[][] VNoiseRef = {
            new double[] { 0.5, 0.5, 0.48506254731562515 },
            new double[] { 1.25, 3.75, 0.24732653163215726 },
            new double[] { 10.1, 20.9, 0.07561482386545443 },
            new double[] { -3.3, 4.4, 0.8107919321416264 },
            new double[] { 123.456, 0.001, 0.5031529420847004 },
            new double[] { 7.0, 7.0, 0.25956007285499016 },
        };
        static readonly double[][] FbmRef = { // x, y, fbm(oct 3), fbm(oct 2)
            new double[] { 0.5, 0.5, 0.5560448093170937, 0.544658756126803 },
            new double[] { 1.25, 3.75, 0.3455455160201664, 0.31466919896082673 },
            new double[] { 10.1, 20.9, 0.2759492103910635, 0.21403578375011925 },
            new double[] { 2.052, 0.1155, 0.1527947648617515, 0.08701453280016662 },
        };
        static readonly int[][] WoodOakRef = { // x, y, r, g, b, rough, seam
            new[] { 0, 0, 50, 34, 20, 230, 1 },
            new[] { 1, 1, 54, 37, 22, 216, 1 },
            new[] { 10, 20, 129, 86, 47, 196, 0 },
            new[] { 63, 100, 159, 112, 68, 158, 0 },
            new[] { 64, 100, 47, 31, 17, 242, 1 },
            new[] { 65, 5, 47, 31, 18, 241, 1 },
            new[] { 512, 341, 54, 38, 23, 214, 1 },
            new[] { 1023, 1023, 156, 109, 66, 162, 0 },
            new[] { 200, 511, 143, 98, 57, 178, 0 },
            new[] { 300, 7, 142, 97, 56, 180, 0 },
        };
        static readonly int[][] WoodWalnutRef = { // x, y, r, g, b, rough, seam
            new[] { 0, 0, 43, 23, 13, 217, 0 },
            new[] { 1, 1, 43, 23, 13, 217, 0 },
            new[] { 10, 20, 66, 37, 21, 175, 0 },
            new[] { 63, 100, 57, 31, 18, 192, 0 },
            new[] { 64, 100, 54, 30, 17, 197, 0 },
            new[] { 65, 5, 54, 30, 17, 197, 0 },
            new[] { 256, 170, 71, 41, 23, 165, 0 },
            new[] { 511, 511, 78, 45, 26, 152, 0 },
            new[] { 200, 511, 63, 35, 20, 181, 0 },
            new[] { 300, 7, 67, 38, 21, 173, 0 },
        };
        static readonly int[][] WoodMapleRef = { // x, y, r, g, b, rough, seam
            new[] { 0, 0, 184, 135, 82, 217, 0 },
            new[] { 1, 1, 184, 135, 82, 217, 0 },
            new[] { 10, 20, 203, 157, 101, 175, 0 },
            new[] { 63, 100, 195, 148, 93, 192, 0 },
            new[] { 64, 100, 193, 145, 91, 197, 0 },
            new[] { 65, 5, 192, 145, 90, 197, 0 },
            new[] { 256, 170, 207, 162, 105, 165, 0 },
            new[] { 511, 511, 213, 168, 111, 152, 0 },
            new[] { 200, 511, 200, 154, 98, 181, 0 },
            new[] { 300, 7, 204, 158, 102, 173, 0 },
        };
        static readonly int[][] WoodStandRef = { // x, y, r, g, b, rough, seam
            new[] { 0, 0, 9, 9, 9, 217, 0 },
            new[] { 1, 1, 9, 9, 9, 217, 0 },
            new[] { 10, 20, 14, 13, 13, 175, 0 },
            new[] { 63, 100, 12, 11, 11, 192, 0 },
            new[] { 64, 100, 11, 11, 11, 197, 0 },
            new[] { 65, 5, 11, 11, 11, 197, 0 },
            new[] { 128, 85, 12, 11, 11, 192, 0 },
            new[] { 255, 255, 15, 15, 15, 160, 0 },
            new[] { 200, 255, 13, 13, 13, 178, 0 },
            new[] { 44, 7, 10, 10, 10, 203, 0 },
        };
        static readonly int[][] DrumHeadRef = {
            new[] { 0, 0, 208, 205, 196 },
            new[] { 1, 0, 224, 220, 211 },
            new[] { 17, 33, 225, 221, 212 },
            new[] { 128, 128, 216, 213, 204 },
            new[] { 255, 255, 214, 211, 202 },
        };
        static readonly int[][] CymbalRef = { // x, y, r, g, b, rough
            new[] { 0, 0, 187, 153, 81, 82 },
            new[] { 3, 1, 213, 174, 92, 123 },
            new[] { 40, 77, 210, 172, 91, 118 },
            new[] { 128, 200, 193, 158, 83, 90 },
            new[] { 255, 255, 211, 172, 91, 118 },
        };

        [Test]
        public void HashVNoiseAndFbmAreBitExact()
        {
            foreach (var r in HashRef) Assert.That(ProcTex.Hash((int)r[0], (int)r[1]), Is.EqualTo(r[2]), $"hash({r[0]}, {r[1]})");
            foreach (var r in VNoiseRef) Assert.That(ProcTex.VNoise(r[0], r[1]), Is.EqualTo(r[2]).Within(1e-15), $"vnoise({r[0]}, {r[1]})");
            foreach (var r in FbmRef)
            {
                Assert.That(ProcTex.Fbm(r[0], r[1], 3), Is.EqualTo(r[2]).Within(1e-15), $"fbm3({r[0]}, {r[1]})");
                Assert.That(ProcTex.Fbm(r[0], r[1], 2), Is.EqualTo(r[3]).Within(1e-15), $"fbm2({r[0]}, {r[1]})");
            }
            Assert.That(ProcTex.ToInt32(4294967296.0 + 5), Is.EqualTo(5), "JS x | 0 wraps modulo 2^32");
            Assert.That(ProcTex.ToInt32(-1.7), Is.EqualTo(-1), "and truncates");
        }

        static void CheckWood(in WoodParams p, int[][] want, string name)
        {
            foreach (var r in want)
            {
                ProcTex.WoodPixel(p, r[0], r[1], out var c, out byte rough, out bool seam);
                string at = $"{name} ({r[0]}, {r[1]})";
                Assert.That(Math.Abs(c.r - r[2]), Is.LessThanOrEqualTo(1), at + " r");
                Assert.That(Math.Abs(c.g - r[3]), Is.LessThanOrEqualTo(1), at + " g");
                Assert.That(Math.Abs(c.b - r[4]), Is.LessThanOrEqualTo(1), at + " b");
                Assert.That(Math.Abs(rough - r[5]), Is.LessThanOrEqualTo(1), at + " rough");
                Assert.That(seam ? 1 : 0, Is.EqualTo(r[6]), at + " seam");
            }
        }

        [Test]
        public void WoodPixelsMatchTheReference()
        {
            CheckWood(WoodParams.Oak, WoodOakRef, "oak (16 planks)");
            CheckWood(WoodParams.Walnut, WoodWalnutRef, "walnut");
            CheckWood(WoodParams.Maple, WoodMapleRef, "maple");
            CheckWood(WoodParams.Stand, WoodStandRef, "stand");
        }

        [Test]
        public void DrumHeadAndCymbalPixelsMatchTheReference()
        {
            foreach (var r in DrumHeadRef)
            {
                var c = ProcTex.DrumHeadPixel(r[0], r[1]);
                Assert.That(new[] { (int)c.r, c.g, c.b }, Is.EqualTo(new[] { r[2], r[3], r[4] }).Within(1), $"drum head ({r[0]}, {r[1]})");
            }
            foreach (var r in CymbalRef)
            {
                ProcTex.CymbalPixel(r[0], r[1], out var c, out byte rough);
                Assert.That(new[] { (int)c.r, c.g, c.b, rough }, Is.EqualTo(new[] { r[2], r[3], r[4], r[5] }).Within(1), $"cymbal ({r[0]}, {r[1]})");
            }
        }

        [Test]
        public void TexturesUseTheCanvasRowConventionAndShape()
        {
            // Canvas row y is Texture2D row h-1-y (ARCHITECTURE section 5).
            var dh = ProcTex.DrumHead(16);
            Assert.That((Color)dh.GetPixel(3, 15), Is.EqualTo((Color)ProcTex.DrumHeadPixel(3, 0)), "canvas row 0 = texture top");
            UnityEngine.Object.DestroyImmediate(dh);
            // Grille: hole centres dark, between holes the metal; the tile wraps in x.
            Color32 hole = ProcTex.GrillePixel(64, 8, 8), metal = ProcTex.GrillePixel(64, 0, 0), wrapped = ProcTex.GrillePixel(64, 0, 24);
            Assert.That(hole.r, Is.EqualTo(0x06));
            Assert.That(metal.r, Is.EqualTo(0x2b));
            Assert.That(wrapped.r, Is.LessThan(0x2b), "odd rows are offset half a pitch and wrap around x = 0");
            // Soft dot: opaque centre, .55 at 35 % of the radius, transparent corners.
            Assert.That(ProcTex.SoftDotAlpha(64, 31, 31), Is.GreaterThan(245));
            Assert.That(ProcTex.SoftDotAlpha(64, 0, 0), Is.EqualTo(0));
            Assert.That(ProcTex.SoftDotAlpha(64, 32 + 11, 31), Is.EqualTo(140).Within(4), "r = .35");
        }

        [Test]
        public void SharedTextureNamesAreModulePrefixedAndUnique()
        {
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var spec in SharedTextures.Specs())
            {
                Assert.That(spec.Name, Does.StartWith("Core/"));
                Assert.That(names.Add(spec.Name), Is.True, "unique " + spec.Name);
                Assert.That(spec.NeedsGpu, Is.False, "CPU generators (bake runs -nographics)");
            }
            Assert.That(names, Does.Contain(SharedTextures.WoodWalnut).And.Contain(SharedTextures.DrumHead).And.Contain(SharedTextures.Grille));
            var small = SharedTextures.Generate(SharedTextures.Grille);
            Assert.That(small.width, Is.EqualTo(64));
            UnityEngine.Object.DestroyImmediate(small);
        }
    }
}
