// The Room module against its reference: every expected value below was computed by running the reference code
// (the noise and per-pixel texture generators, SnowWeather / SnowField.flakeAt / sillSnowGeometry, the seeded
// plant with Math.random = mulberry32(seed), the books, the right-handed mesh generators) in node
// (scratch script ref-room.mjs; ARCHITECTURE.md section 15.4 checklist: Hash/VNoise/Fbm determinism (RoomTexGen forwards to Core's ProcTex),
// SnowWeather timeline, FlakeAt mirror of the shader). Float tolerances cover JS doubles vs C# floats.
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Geometry;
using MusicRoom.Room.Textures;
using MusicRoom.Room.World;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Room
{
    public class RoomGoldenTests
    {
        // ------------------------------------------------------------------ texture noise core

        static readonly double[][] HashRef =
        {
            new[] { 0, 0, 0.0 }, new[] { 1, 0, 0.5081244610501743 }, new[] { 0, 1, 0.7682745544631674 }, new[] { 12, 34, 0.03931462625956969 },
            new[] { -5, 7, 0.3129855674488902 }, new[] { 1000, -3, 0.25577981473314104 }, new[] { 100, 3, 0.014307007662557766 },
            new[] { 2047, 1023, 0.006669030060681754 }, new[] { -1, -1, 0.1702382530016448 },
        };

        [Test]
        public void HashIsTheReferenceIntegerHash()
        {
            foreach (var r in HashRef) Assert.That(RoomTexGen.Hash((int)r[0], (int)r[1]), Is.EqualTo(r[2]).Within(1e-15), $"hash({r[0]}, {r[1]})");
        }

        [Test]
        public void ValueNoiseAndFbmMatchTheReference()
        {
            double[][] vn = { new[] { 0.5, 0.5, 0.48506254731562515 }, new[] { 1.25, 3.75, 0.24732653163215726 }, new[] { -2.3, 4.1, 0.9057927546585428 },
                new[] { 100.7, 0.3, 0.18914842178051605 }, new[] { 0.0, 0.0, 0.0 }, new[] { 17.9, 63.2, 0.74621220013941 } };
            foreach (var r in vn) Assert.That(RoomTexGen.VNoise(r[0], r[1]), Is.EqualTo(r[2]).Within(1e-13), $"vnoise({r[0]}, {r[1]})");
            double[][] fb = { new[] { 0.5, 0.5, 3, 0.5560448093170937 }, new[] { 1.25, 3.75, 2, 0.31466919896082673 }, new[] { -2.3, 4.1, 3, 0.7640351166788092 },
                new[] { 6.144, 0, 3, 0.8436866862285566 }, new[] { 2.7, 4.8, 3, 0.21904593592730187 }, new[] { 12.5, 7.25, 2, 0.8066523352692182 } };
            foreach (var r in fb) Assert.That(RoomTexGen.Fbm(r[0], r[1], (int)r[2]), Is.EqualTo(r[3]).Within(1e-13), $"fbm({r[0]}, {r[1]}, {r[2]})");
        }

        [Test]
        public void ClampByteRoundsHalfToEvenLikeUint8ClampedArray()
        {
            Assert.That(RoomTexGen.ClampByte(216.5), Is.EqualTo(216));
            Assert.That(RoomTexGen.ClampByte(217.5), Is.EqualTo(218));
            Assert.That(RoomTexGen.ClampByte(216.75), Is.EqualTo(217));
            Assert.That(RoomTexGen.ClampByte(-3), Is.EqualTo(0));
            Assert.That(RoomTexGen.ClampByte(300), Is.EqualTo(255));
            Assert.That(RoomTexGen.ClampByte(double.NaN), Is.EqualTo(0));
        }

        [Test]
        public void WoodPixelsAreTheReferenceBytes()
        {
            // x, y, r, g, b, roughness byte, tone t (woodTexture, the oak and walnut materials).
            double[][] oak =
            {
                new double[] { 0, 0, 50, 34, 20, 230, 0.42681203205583 }, new double[] { 1, 1, 54, 37, 22, 216, 0.5802768342160751 },
                new double[] { 5, 9, 156, 109, 65, 162, 0.6131011615117268 }, new double[] { 64, 300, 54, 37, 22, 216, 0.5796349925520909 },
                new double[] { 130, 511, 149, 103, 61, 170, 0.5205297741877741 }, new double[] { 700, 1000, 173, 123, 77, 141, 0.8459810770177145 },
                new double[] { 1023, 1023, 156, 109, 66, 162, 0.6179209334756124 }, new double[] { 333, 777, 133, 89, 50, 191, 0.2913557475553208 },
            };
            double[][] walnut =
            {
                new double[] { 0, 0, 43, 23, 13, 217, 0 }, new double[] { 5, 9, 58, 32, 18, 189, 0.30734638238939604 },
                new double[] { 100, 200, 66, 37, 21, 175, 0.46618269430088477 }, new double[] { 511, 511, 78, 45, 26, 152, 0.7240884653945239 },
                new double[] { 256, 3, 70, 40, 23, 167, 0.5532048929771904 },
            };
            // Core's ProcTex draws the shared woods; the room binds them and derives its URP gloss maps from the same bytes.
            Check(WoodParams.Oak, oak, 1.0, RoomTexGen.WoodGloss(WoodParams.Oak, 1.0, "oak gloss"));
            Check(WoodParams.Walnut, walnut, 0.8, RoomTexGen.WoodGloss(WoodParams.Walnut, 0.8, "walnut gloss"));

            void Check(WoodParams s, double[][] rows, double roughness, PixelImage gloss)
            {
                foreach (var r in rows)
                {
                    int x = (int)r[0], y = (int)r[1];
                    ProcTex.WoodPixel(s, x, y, out Color32 c, out byte rough, out _);
                    Assert.That(new[] { c.r, c.g, c.b, rough }, Is.EqualTo(new[] { (byte)r[2], (byte)r[3], (byte)r[4], (byte)r[5] }), $"wood {s.Size} ({r[0]}, {r[1]})");
                    // Reference: roughness x texel; URP's metallic-gloss map stores the smoothness 1 - that in A, metallic 0 in R.
                    Color32 g = gloss.Pixels[(s.Size - 1 - y) * s.Size + x];
                    Assert.That(g.a, Is.EqualTo(ProcTex.ClampByte((1.0 - roughness * r[5] / 255.0) * 255.0)), $"gloss ({x}, {y})");
                    Assert.That(g.r, Is.EqualTo(0));
                }
                Assert.That(gloss.Linear && gloss.Repeat, Is.True);
            }
        }

        [Test]
        public void PlasterWeaveAndHillSnowBytesMatchTheReference()
        {
            var plaster = RoomTexGen.Plaster();
            int[][] pl = { new[] { 0, 0, 213, 202, 183 }, new[] { 10, 20, 223, 210, 191 }, new[] { 128, 128, 222, 210, 190 }, new[] { 255, 1, 222, 210, 191 } };
            foreach (var r in pl)
            {
                Color32 c = plaster.Pixels[(255 - r[1]) * 256 + r[0]];   // canvas row y -> texture row h-1-y
                Assert.That(new[] { c.r, c.g, c.b }, Is.EqualTo(new[] { (byte)r[2], (byte)r[3], (byte)r[4] }), $"plaster ({r[0]}, {r[1]})");
            }
            int[][] wv = { new[] { 0, 0, 108 }, new[] { 3, 7, 126 }, new[] { 64, 64, 231 }, new[] { 127, 5, 132 } };
            foreach (var r in wv) Assert.That(RoomTexGen.ClampByte(RoomTexGen.WeaveHeight(r[0], r[1]) * 255), Is.EqualTo(r[2]), $"weave ({r[0]}, {r[1]})");
            int[][] hs =
            {
                new[] { 0, 511, 92, 104, 140, 41 }, new[] { 10, 420, 112, 126, 162, 103 }, new[] { 300, 460, 92, 104, 140, 190 },
                new[] { 512, 500, 92, 104, 140, 128 }, new[] { 800, 430, 112, 126, 162, 122 }, new[] { 1023, 470, 92, 104, 140, 0 }, new[] { 77, 300, 0, 0, 0, 0 },
            };
            foreach (var r in hs)
            {
                Color32 c = RoomTexGen.HillSnowPixel(r[0], r[1]);
                Assert.That(new[] { c.r, c.g, c.b, c.a }, Is.EqualTo(new[] { (byte)r[2], (byte)r[3], (byte)r[4], (byte)r[5] }), $"hill snow ({r[0]}, {r[1]})");
            }
        }

        // ------------------------------------------------------------------ SnowWeather

        // step, t, level, cover, wind, windVel, gust envelope (dt 1/72, switch on for 60 s, then off for 30 s).
        static readonly double[][] WeatherRef =
        {
            new[] { 1, 0.013888888888888888, 0.011111111111111112, 3.858024691358025e-06, 0.0044464814806890384, 0.3201466666096108, 0 },
            new[] { 10, 0.13888888888888892, 0.1057170061612254, 0.00020527307348990926, 0.044556479084361715, 0.32146660961143403, 0 },
            new[] { 72, 0.9999999999999983, 0.5526784729555526, 0.007920699967692986, 0.3253478626548031, 0.3305387168803688, 0 },
            new[] { 216, 2.9999999999999916, 0.910492506342991, 0.04686325240815063, 1.007306337901343, 0.3511081307259073, 0 },
            new[] { 288, 3.999999999999988, 0.9599613712554257, 0.07033452706884274, 1.6435773219184935, 1.0803622210468335, 0.3128139271306512 },
            new[] { 504, 7.000000000000073, 0.9964162426916094, 0.14420797027793303, 6.004498270812197, 1.3557359258122097, 0.4212638882191231 },
            new[] { 576, 8.000000000000101, 1, 0.16919088101779836, 7.155002486208446, 0.9290707241359383, 0.23264338772245072 },
            new[] { 1440, 20.00000000000044, 1, 0.4691908810177973, 17.21637369074294, 2.0173550867519054, 0.7042345356457512 },
            new[] { 2880, 39.99999999999896, 1, 0.9691908810178705, 34.835549932701056, 1.7917942053942837, 0.6796295671685538 },
            new[] { 3024, 41.99999999999851, 1, 1, 38.615521650541424, 1.8615395407085507, 0.7117957031837064 },
            new[] { 4320, 59.999999999994415, 1, 1, 49.85800515784155, 0.34990797089722825, 0 },
            new[] { 10001, 60.0138888888833, 0.9888888888888889, 0.9999074074074074, 49.86286692594413, 0.35004730338566803, 0 },
            new[] { 10360, 64.99999999999328, 0.0179101405507887, 0.9666666666666481, 55.987680187979265, 0.6136748892661641, 0.09586820191283241 },
            new[] { 10720, 69.99999999999214, 0, 0.9333333333332963, 58.06782990640207, 0.41486415045217934, 0 },
            new[] { 12160, 89.9999999999876, 0, 0.7999999999998888, 65.37674084856121, 0.2760765541976856, 0 },
        };

        // at, strength, attack, hold, release, next gust after it.
        static readonly double[][] GustRef =
        {
            new[] { 3.0138888888888804, 0.49031223796773704, 1.6627295918762686, 1.4834272229112684, 3.5587691126856953, 11.403152709164544 },
            new[] { 11.416666666666865, 0.5147697090753354, 1.2294279851019383, 1.3633084224537013, 2.758501560147852, 18.841614581194595 },
            new[] { 18.84722222222263, 0.7493684256216511, 1.3551218466833235, 1.593091563601047, 2.960819999454543, 27.444164307832402 },
            new[] { 27.444444444445097, 0.6784418495488354, 1.4971545167267324, 0.5100719939917326, 3.540157223958522, 38.39910144372391 },
            new[] { 38.4027777777771, 0.7261420079506934, 1.8885708685964349, 1.4971313680522145, 2.5328761068172754, 48.69254941961454 },
            new[] { 48.69444444444143, 0.5578799937851727, 1.5477604957297446, 1.1203289604745805, 3.4417137937154623, 60.710114844996674 },
            new[] { 60.72222222221647, 0.6283515515853652, 1.2411304330453277, 1.0374155797064304, 2.6540188020560893, 70.22856053101462 },
        };

        [Test]
        public void SnowWeatherTimelineMatchesTheReference()
        {
            // The reference gusts draw Math.random; with Math.random = mulberry32(1234) the same stream as SnowWeather(1234).
            var w = new SnowWeather(1234);
            var gusts = new System.Collections.Generic.List<(double at, SnowGust g, double next)>();
            w.OnGust += g => gusts.Add((w.LastGustAt, g, w.NextGust));
            const double dt = 1.0 / 72.0;   // the reference dt, in double like its clock
            int si = 0;
            w.Target = 1f;
            for (int i = 1; i <= 72 * 60; i++) { w.Update(dt); CheckSample(i); }
            w.Target = 0f;
            for (int i = 1; i <= 72 * 30; i++) { w.Update(dt); CheckSample(10000 + i); }
            Assert.That(si, Is.EqualTo(WeatherRef.Length), "every reference sample was checked");

            Assert.That(gusts.Count, Is.EqualTo(GustRef.Length), "seven gusts in 90 s, the last starting just after the switch went off");
            for (int i = 0; i < GustRef.Length; i++)
            {
                var r = GustRef[i];
                Assert.That(gusts[i].at, Is.EqualTo(r[0]).Within(1e-9), $"gust {i} starts on the reference frame");
                Assert.That(gusts[i].g.Strength, Is.EqualTo(r[1]).Within(1e-6));
                Assert.That(gusts[i].g.Attack, Is.EqualTo(r[2]).Within(1e-6));
                Assert.That(gusts[i].g.Hold, Is.EqualTo(r[3]).Within(1e-6));
                Assert.That(gusts[i].g.Release, Is.EqualTo(r[4]).Within(1e-6));
                Assert.That(gusts[i].next, Is.EqualTo(r[5]).Within(1e-5), $"gust {i} schedules the next");
            }

            void CheckSample(int step)
            {
                if (si >= WeatherRef.Length || (int)WeatherRef[si][0] != step) return;
                var r = WeatherRef[si++];
                // Double state in the reference operation order: equal to the JS up to sin's last bits (and float accessors).
                Assert.That(w.TimeD, Is.EqualTo(r[1]).Within(1e-12), $"t at step {step}");
                Assert.That(w.Level, Is.EqualTo(r[2]).Within(1e-6), $"level at step {step}");
                Assert.That(w.Cover, Is.EqualTo(r[3]).Within(1e-6), $"cover at step {step}");
                Assert.That(w.WindD, Is.EqualTo(r[4]).Within(1e-9), $"wind at step {step}");
                Assert.That(w.WindVel, Is.EqualTo(r[5]).Within(1e-6), $"wind speed at step {step}");
                Assert.That(w.GustValue, Is.EqualTo(r[6]).Within(1e-6), $"gust at step {step}");
            }
        }

        [Test]
        public void GustEnvelopeMatchesTheReference()
        {
            var g = new SnowGust { Strength = 0.8f, Attack = 1.5f, Hold = 1.0f, Release = 3.0f };
            double[][] env = { new[] { 0.5, 0 }, new[] { 1.2, 0.03887407407407406 }, new[] { 2.0, 0.5925925925925926 }, new[] { 3.0, 0.8 }, new[] { 4.5, 0.5925925925925926 }, new[] { 6.0, 0.05925925925925917 }, new[] { 7.5, 0 } };
            foreach (var r in env) Assert.That(SnowWeather.Envelope(g, 1.0, r[0]), Is.EqualTo(r[1]).Within(1e-6), $"envelope at {r[0]}");
            Assert.That(g.Duration, Is.EqualTo(5.5f).Within(1e-6f));
        }

        [Test]
        public void GustsWaitForRealSnowAndTheirAnnouncementCarriesTheGust()
        {
            var w = new SnowWeather(7);
            int heard = 0;
            SnowGust last = default;
            w.OnGust += g => { heard++; last = g; };
            for (int i = 0; i < 72 * 10; i++) w.Update(1f / 72f);
            Assert.That(heard, Is.EqualTo(0), "no gusts while the switch is off");
            Assert.That(w.NextGust, Is.GreaterThanOrEqualTo(w.Time + 2f - 1e-3f), "the first gust keeps being pushed 2 s ahead");
            w.Target = 1f;
            float t0 = w.Time;
            while (w.Level < SnowWeather.GustMinLevel) w.Update(1f / 72f);
            Assert.That(heard, Is.EqualTo(0), "below level .15 still nothing");
            for (int i = 0; i < 72 * 6; i++) w.Update(1f / 72f);
            Assert.That(heard, Is.EqualTo(1), "a gust within 2 s of reaching level .15");
            Assert.That(last.Strength, Is.InRange(0.45f, 1f));
            Assert.That(last.Attack, Is.InRange(1.1f, 1.9f));
            Assert.That(last.Hold, Is.InRange(0.4f, 1.6f));
            Assert.That(last.Release, Is.InRange(2.2f, 3.6f));
            Assert.That(w.Time - t0, Is.LessThan(8f));
        }

        // ------------------------------------------------------------------ SnowField.flakeAt

        [Test]
        public void FlakeAtMatchesTheReferenceShaderMirror()
        {
            // i, x, y, z (right-handed: -dd), fall, xn, yn, seen, then the attributes: pos xyz, seed4, tone.
            double[][] flakes =
            {
                new[] { 0, -12.081585681540142, 11.93038823312573, -14.46233946528543, 0.9089769918471575, 0.07853956543580384, 0.85054132137978, 0, 0.011704753153026104, 0.06195825710892677, 0.9769076108932495, 0.699028730392456, 0.5214452743530273, 0.4055216908454895, 0.46623262763023376, 0.23992519080638885 },
                new[] { 1, 2.0592940271451754, -0.6902458575899677, -1.8692585380123916, 0.6462656505405903, 0.8363366893705009, 0.20114248410046198, 0, 0.5533255934715271, 0.7298220992088318, 0.25781556963920593, 0.15594837069511414, 0.7640898823738098, 0.5184025168418884, 0.1971372663974762, 0.36795860528945923 },
                new[] { 2, 0.42934954861640023, 2.085906449121896, -2.2188081948040574, 1.0385617464780807, 0.5586070550980264, 0.7513903989066449, 0, 0.2932473421096802, 0.5347395539283752, 0.2963302433490753, 0.9779461026191711, 0.24753358960151672, 0.8777796030044556, 0.19079171121120453, 0.143657386302948 },
                new[] { 100, 1.3054779129839285, 1.9034817974759095, -2.97565499303117, 0.8559874795377254, 0.6569256489831153, 0.6290534992082588, 1, 0.4494382441043854, 0.19771896302700043, 0.356912225484848, 0.4424408972263336, 0.1157982349395752, 0.19807347655296326, 0.5925395488739014, 0.7068914771080017 },
                new[] { 2500, -2.746557418486466, -0.8153273607349891, -5.3643145290854735, 0.781666748970747, 0.28641904613597957, 0.10149642556303268, 1, 0.13994000852108002, 0.4755946099758148, 0.5221290588378906, 0.41774123907089233, 0.6173468828201294, 0.24294638633728027, 0.1265653818845749, 0.35630056262016296 },
                new[] { 5999, 0.23593920044415856, -1.2770861893253993, -2.555889041601433, 1.0152236372232437, 0.5330067850975843, 0.06159737591368386, 1, 0.2934334874153137, 0.7781350612640381, 0.32790806889533997, 0.6863422989845276, 0.6670050621032715, 0.4888428747653961, 0.7073315978050232, 0.8851081728935242 },
            };
            var field = new SnowField(SnowFieldParams.Room);
            // The reference eye (0.2, -0.1, 2.5) in its window frame (+z into the room) = Unity (0.2, -0.1, -2.5).
            var cam = new Vector3(0.2f, -0.1f, -2.5f);
            foreach (var r in flakes)
            {
                int i = (int)r[0];
                Assert.That(field.Pos[i].x, Is.EqualTo(r[8]).Within(1e-7)); Assert.That(field.Pos[i].y, Is.EqualTo(r[9]).Within(1e-7)); Assert.That(field.Pos[i].z, Is.EqualTo(r[10]).Within(1e-7));
                Assert.That(field.Seed4[i].x, Is.EqualTo(r[11]).Within(1e-7)); Assert.That(field.Seed4[i].w, Is.EqualTo(r[14]).Within(1e-7));
                Assert.That(field.Tone[i], Is.EqualTo(r[15]).Within(1e-7), $"flake {i} seeds in the reference order");
                var f = field.FlakeAt(i, 3.7, 2.1, 0.5, cam);
                Assert.That(f.Position.x, Is.EqualTo(r[1]).Within(1e-4), $"flake {i} x");
                Assert.That(f.Position.y, Is.EqualTo(r[2]).Within(1e-4), $"flake {i} y");
                Assert.That(f.Position.z, Is.EqualTo(-r[3]).Within(1e-4), $"flake {i} depth (+z outward in Unity)");
                Assert.That(f.Fall, Is.EqualTo(r[4]).Within(1e-6));
                Assert.That(f.Xn, Is.EqualTo(r[5]).Within(1e-5));
                Assert.That(f.Yn, Is.EqualTo(r[6]).Within(1e-5));
                Assert.That(f.Seen, Is.EqualTo(r[7] > 0.5), $"flake {i} seen through the window");
            }
        }

        [Test]
        public void SnowFieldMeshIsFourVerticesPerFlakeInsideTheVolume()
        {
            var field = new SnowField(SnowFieldParams.Room);
            var mesh = field.BuildMesh();
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(6000 * 4));
                Assert.That(mesh.triangles.Length, Is.EqualTo(6000 * 6));
                Bounds b = mesh.bounds;
                // updateBounds: hw = 1.4 + .9 d + .15 at d = .1 and 15; z from d0 - .1 to d1 + .1 (outward).
                Assert.That(b.min.z, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(b.max.z, Is.EqualTo(15.1f).Within(1e-4f));
                Assert.That(b.max.x, Is.EqualTo(1.4f + 0.9f * 15f + 0.15f).Within(1e-4f));
                Assert.That(b.min.y, Is.EqualTo(-1.6f).Within(1e-4f));
                Assert.That(b.max.y, Is.EqualTo(1.3f + 0.9f * 15f).Within(1e-4f));
                // Every flake position lands inside the volume.
                for (int i = 0; i < 6000; i += 97)
                {
                    var f = field.FlakeAt(i, 12.3, 4.5, 1.1);
                    Assert.That(b.Contains(f.Position), Is.True, $"flake {i} at {f.Position}");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // ------------------------------------------------------------------ sillSnowGeometry

        [Test]
        public void SillMoundMatchesTheReference()
        {
            Geo g = SillMound.Build(1.92f, 0.155f, 0.065f);
            Assert.That(g.VertexCount, Is.EqualTo(627), "57 x (9 + 1 rows + skirt)");
            Assert.That(g.I.Count, Is.EqualTo(3360));
            double[][] v =
            {
                new[] { 0, -0.9599999785423279, 0, 0, -0.5073826313018799, 0.861720860004425, 0 },
                new[] { 1, -0.9257143139839172, 0.02018745429813862, 0, -0.5949199795722961, 0.8037848472595215, -0.00037925640936009586 },
                new[] { 28, 0, 0.06305170059204102, 0, -0.008371355943381786, 0.9999625086784363, -0.002204952761530876 },
                new[] { 300, -0.4457142949104309, 0.06440960615873337, -0.08611111342906952, 0.042882680892944336, 0.9782779812812805, -0.20281332731246948 },
                new[] { 512, 0.9599999785423279, 0, -0.13777777552604675, 0.2571525573730469, 0.948268473148346, -0.18617050349712372 },
                new[] { 626, 0.9599999785423279, 0, -0.16300000250339508, -0.10151384025812149, 0.9948341250419617, 0 },
            };
            foreach (var r in v)
            {
                int i = (int)r[0];
                AssertV(g.P[i], r[1], r[2], r[3], 1e-6, $"mound vertex {i}");
                AssertV(g.N[i], r[4], r[5], r[6], 1e-4, $"mound normal {i}");
            }
        }

        // ------------------------------------------------------------------ books

        [Test]
        public void BooksAreTheReferenceLayoutAndColours()
        {
            var books = RoomBuilder.Books();
            Assert.That(books.Count, Is.EqualTo(88));
            // shelf, k, x, y, w, h, d, r, g, b (linear, the reference working space)
            double[][] r =
            {
                new[] { 0, 0, -0.4875, 0.2025, 0.025, 0.22, 0.22, 0.58, 0.22000000000000008, 0.22000000000000008 },
                new[] { 0, 1, -0.45049999999999996, 0.27749999999999997, 0.043000000000000003, 0.37, 0.24, 0.58, 0.43600000000000017, 0.22000000000000008 },
                new[] { 0, 8, -0.11750000000000002, 0.26249999999999996, 0.049, 0.33999999999999997, 0.26, 0.5079999999999999, 0.22000000000000008, 0.58 },
                new[] { 0, 20, 0.41250000000000003, 0.26249999999999996, 0.025, 0.33999999999999997, 0.26, 0.58, 0.22000000000000008, 0.22000000000000008 },
                new[] { 3, 21, 0.4585000000000001, 1.5225000000000002, 0.049, 0.22, 0.22, 0.22000000000000008, 0.36399999999999977, 0.58 },
            };
            var sample = new[] { books[0], books[1], books[8], books[20], books[books.Count - 1] };
            for (int i = 0; i < r.Length; i++)
            {
                var b = sample[i];
                Assert.That(b.Shelf, Is.EqualTo((int)r[i][0])); Assert.That(b.K, Is.EqualTo((int)r[i][1]));
                Assert.That(b.X, Is.EqualTo(r[i][2]).Within(1e-5)); Assert.That(b.Y, Is.EqualTo(r[i][3]).Within(1e-5));
                Assert.That(b.W, Is.EqualTo(r[i][4]).Within(1e-6)); Assert.That(b.H, Is.EqualTo(r[i][5]).Within(1e-6)); Assert.That(b.D, Is.EqualTo(r[i][6]).Within(1e-6));
                RoomTexGen.HslLinear(b.Palette / 10.0, 0.45, 0.4, out double cr, out double cg, out double cb);
                Assert.That(new[] { cr, cg, cb }, Is.EqualTo(new[] { r[i][7], r[i][8], r[i][9] }).Within(1e-9), $"book {i} colour");
            }
        }

        // ------------------------------------------------------------------ plant (seeded)

        [Test]
        public void PlantP1IsTheReferenceConstructionWithTheSameRandomStream()
        {
            // The reference ran decor.plant with Math.random = mulberry32(101) for the plant's own rand() calls (the
            // UUID generator got a separate stream: in the reference every scene object / geometry / material draws 4 randoms).
            PlantGeometry p = PlantBuilder.Build(PlantBuilder.P1, 101);
            Assert.That(p.Stems.VertexCount, Is.EqualTo(616), "8 tubes of 11 x 7 vertices");
            Assert.That(p.Stems.I.Count, Is.EqualTo(2880));
            Assert.That(p.LeafCount, Is.EqualTo(24));
            Assert.That(p.Phase, Is.EqualTo(3.410795692661777).Within(1e-5));
            Assert.That(p.Amp, Is.EqualTo(0.01698433956177905).Within(1e-7));
            double[][] stems =
            {
                new[] { 0, 5.654982305713929e-05, 0.1983160674571991, 0.006992632057517767, 0.00807854626327753, 0.04515301063656807, 0.9989474415779114 },
                new[] { 1, -0.005939145106822252, 0.19922569394111633, 0.0034963160287588835, -0.8484492897987366, 0.17509940266609192, 0.4994737207889557 },
                new[] { 7, 0.015647128224372864, 0.2936720550060272, 0.0029277873691171408, 0.006976541131734848, 0.03848724812269211, 0.9992347359657288 },
                new[] { 70, 0.2597304582595825, 1.1218070983886719, -0.06071070209145546, 0.022802865132689476, 0.09612099826335907, 0.995108425617218 },
                new[] { 615, 0.15385016798973083, 1.2593969106674194, -0.08408666402101517, 0.01488178689032793, 0.11996881663799286, 0.9926661252975464 },
            };
            foreach (var r in stems)
            {
                int i = (int)r[0];
                AssertV(p.Stems.P[i], r[1], r[2], r[3], 2e-5, $"stem vertex {i}");
                AssertV(p.Stems.N[i], r[4], r[5], r[6], 2e-3, $"stem normal {i}");
            }
            double[][] leaves =
            {
                new[] { 0, -0.1021924763917923, -0.005926334299147129, -0.2494712769985199, 0, 0.237660750746727, -0.08450236171483994, -0.09534705430269241, 0, -0.07608158886432648, -0.25600501894950867, 0.037247322499752045, 0, 0.09084979444742203, 0.7057238221168518, -0.023686761036515236, 1 },
                new[] { 1, -0.16739948093891144, 0.007351005915552378, -0.11046852916479111, 0, 0.09887024760246277, -0.08018729090690613, -0.15515990555286407, 0, -0.04981974884867668, -0.18383656442165375, 0.06326160579919815, 0, 0.17014776170253754, 0.9169819951057434, -0.04436167702078819, 1 },
                new[] { 23, -0.1107056587934494, -0.0025694689247757196, -0.28509050607681274, 0, 0.2831277847290039, -0.03691987320780754, -0.10961075127124786, 0, -0.03349404036998749, -0.3035939037799835, 0.01574256271123886, 0, 0.15374599397182465, 1.2585572004318237, -0.09103532135486603, 1 },
            };
            AssertLeaves(p, leaves);
        }

        [Test]
        public void TrailingIvyIsTheReferenceConstructionWithTheSameRandomStream()
        {
            PlantGeometry p = PlantBuilder.Build(PlantBuilder.Ivy, 106);
            Assert.That(p.Stems.VertexCount, Is.EqualTo(693), "9 tubes");
            Assert.That(p.Stems.I.Count, Is.EqualTo(3240));
            Assert.That(p.LeafCount, Is.EqualTo(72), "trailing: 2 x 4 leaves per stem");
            Assert.That(p.Phase, Is.EqualTo(3.7210786021035167).Within(1e-5));
            Assert.That(p.Amp, Is.EqualTo(0.017269007401075214).Within(1e-7));
            AssertV(p.Stems.P[1], -0.0018296827329322696, 0.08095933496952057, 0.001973474631085992, 2e-5, "ivy stem vertex 1");
            AssertV(p.Stems.P[70], 0.15349134802818298, -0.2676253020763397, -0.027597080916166306, 2e-5, "ivy stem vertex 70");
            AssertV(p.Stems.P[692], 0.10361190885305405, -0.30363306403160095, -0.1425594538450241, 2e-5, "ivy stem vertex 692");
            double[][] leaves =
            {
                new[] { 0, -0.028285861015319824, -0.012540769763290882, -0.04934173449873924, 0, 0.026617739349603653, -0.051759421825408936, -0.0021037491969764233, 0, -0.04339785501360893, -0.023572416976094246, 0.03086964599788189, 0, 0.05098153278231621, 0.12511765956878662, -0.010512701235711575, 1 },
                new[] { 71, -0.056694675236940384, 0.01672256551682949, 0.010167866013944149, 0, -0.019449427723884583, -0.04467165470123291, -0.034978266805410385, 0, -0.0021793281193822622, -0.03636090084910393, 0.04764925315976143, 0, 0.10134071111679077, -0.3034779727458954, -0.14584846794605255, 1 },
            };
            AssertLeaves(p, leaves);
        }

        static void AssertLeaves(PlantGeometry p, double[][] leaves)
        {
            foreach (var r in leaves)
            {
                int li = (int)r[0];
                Matrix4x4 m = p.LeafMatrices[li];
                // The reference matrix elements are column-major.
                for (int col = 0; col < 4; col++)
                    for (int row = 0; row < 4; row++)
                        Assert.That(m[row, col], Is.EqualTo(r[1 + col * 4 + row]).Within(3e-5), $"leaf {li} m[{row},{col}]");
            }
        }

        [Test]
        public void LeafBladeIsCurledLikeTheReference()
        {
            Geo g = PlantBuilder.LeafBlade();
            Assert.That(g.VertexCount, Is.EqualTo(36));
            double[][] v =
            {
                new[] { 0, -0.5, 1.7999999523162842, -0.38999998569488525, 0.10801928490400314, 0.42195048928260803, 0.9001609086990356 },
                new[] { 3, 0.5, 1.7999999523162842, -0.38999998569488525, -0.10801928490400314, 0.42195048928260803, 0.9001609086990356 },
                new[] { 17, -0.1666666716337204, 0.8999999761581421, -0.0924999937415123, 0.05811018496751785, 0.24212577939033508, 0.9685031175613403 },
                new[] { 35, 0.5, 2.3841858265427618e-08, 0.05999999865889549, -0.11908789724111557, 0.03101246803998947, 0.992399275302887 },
            };
            foreach (var r in v) { AssertV(g.P[(int)r[0]], r[1], r[2], r[3], 1e-6, "blade vertex"); AssertV(g.N[(int)r[0]], r[4], r[5], r[6], 1e-5, "blade normal"); }
        }

        // ------------------------------------------------------------------ right-handed mesh generators

        [Test]
        public void RoundedBoxIsTheReferenceRoundedBox()
        {
            Geo g = PrimitiveMeshes.RoundedBox(1.25f, 0.3f, 0.85f, 2, 0.04f);
            Assert.That(g.VertexCount, Is.EqualTo(900), "non-indexed, 6 faces x 5 x 5 x 2 triangles");
            double[][] v =
            {
                new[] { 0, 0.6080940365791321, 0.13309401273727417, 0.40809401869773865, 0.5773502588272095, 0.5773502588272095, 0.5773502588272095, 0, 1 },
                new[] { 1, 0.6116666793823242, 0.12333333492279053, 0.4116666615009308, 0.6666666865348816, 0.3333333432674408, 0.6666666865348816, 0, 0.9544958472251892 },
                new[] { 2, 0.6116666793823242, 0.1366666704416275, 0.398333340883255, 0.6666666865348816, 0.6666666865348816, 0.3333333432674408, 0.015453324653208256, 1 },
                new[] { 150, -0.6080940365791321, 0.13309401273727417, -0.40809401869773865, -0.5773502588272095, 0.5773502588272095, -0.5773502588272095, 0, 1 },
                new[] { 449, 0.6116666793823242, 0.1366666704416275, 0.398333340883255, 0.6666666865348816, 0.6666666865348816, 0.3333333432674408, 1, 0.015453324653208256 },
                new[] { 777, -0.6013299226760864, 0.1263299286365509, -0.41765984892845154, -0.40824830532073975, 0.40824830532073975, -0.8164965510368347, 0.989560604095459, 0.9544958472251892 },
                new[] { 899, -0.6116666793823242, -0.12333333492279053, -0.4116666615009308, -0.6666666865348816, -0.3333333432674408, -0.6666666865348816, 1, 0.045504145324230194 },
            };
            foreach (var r in v)
            {
                int i = (int)r[0];
                AssertV(g.P[i], r[1], r[2], r[3], 1e-6, $"rbox vertex {i}");
                AssertV(g.N[i], r[4], r[5], r[6], 1e-6, $"rbox normal {i}");
                Assert.That(g.UV[i].x, Is.EqualTo(r[7]).Within(1e-5), $"rbox u {i}");
                Assert.That(g.UV[i].y, Is.EqualTo(r[8]).Within(1e-5), $"rbox v {i}");
            }
        }

        [Test]
        public void CylinderTorusAndOpenCylinderMatchTheReference()
        {
            Geo c = PrimitiveMeshes.Cylinder(0.045f, 0.05f, 0.52f, 10);
            Assert.That(c.VertexCount, Is.EqualTo(64)); Assert.That(c.I.Count, Is.EqualTo(120));
            double[][] v =
            {
                new[] { 0, 0, 0.25999999046325684, 0.04500000178813934, 0, 0.00961493980139494, 0.9999537467956543, 0, 1 },
                new[] { 3, 0.04279754310846329, 0.25999999046325684, -0.013905764557421207, 0.9510125517845154, 0.00961493980139494, -0.30900269746780396, 0.30000001192092896, 1 },
                new[] { 11, 0, -0.25999999046325684, 0.05000000074505806, 0, 0.00961493980139494, 0.9999537467956543, 0, 0 },
                new[] { 22, 0, 0.25999999046325684, 0, 0, 1, 0, 0.5, 0.5 },
                new[] { 40, -0.04279754310846329, 0.25999999046325684, 0.013905764557421207, 0, 1, 0, 0.6545084714889526, 0.024471741169691086 },
            };
            foreach (var r in v)
            {
                int i = (int)r[0];
                AssertV(c.P[i], r[1], r[2], r[3], 1e-6, $"cylinder vertex {i}");
                AssertV(c.N[i], r[4], r[5], r[6], 1e-6, $"cylinder normal {i}");
                Assert.That(c.UV[i].x, Is.EqualTo(r[7]).Within(1e-6)); Assert.That(c.UV[i].y, Is.EqualTo(r[8]).Within(1e-6));
            }
            Geo open = PrimitiveMeshes.Cylinder(0.16f, 0.22f, 0.3f, 24, 1, true);
            Assert.That(open.VertexCount, Is.EqualTo(50)); Assert.That(open.I.Count, Is.EqualTo(144));
            Geo t = PrimitiveMeshes.Torus(0.14f, 0.0098f, 8, 32);
            Assert.That(t.VertexCount, Is.EqualTo(297)); Assert.That(t.I.Count, Is.EqualTo(1536));
        }

        [Test]
        public void CentripetalCatmullRomAndTubeMatchTheReference()
        {
            var curve = new CatmullRom3(new Vector3(0f, 0.2f, 0f), new Vector3(0.1f, 0.7f, 0.05f), new Vector3(0.3f, 1.2f, 0.15f));
            // t, getPoint(t), getPointAt(t), getTangentAt(t)
            double[][] r =
            {
                new[] { 0, 0, 0.2, 0, 0, 0.2, 0, 0.19516233984084713, 0.9759044914256759, 0.09758116992042357 },
                new[] { 0.13, 0.0237020413515538, 0.3304037415958018, 0.0118510206757769, 0.024507040289696535, 0.33514460300793897, 0.012253520144848267, 0.16665127426104873, 0.982488773973505, 0.08332563713052436 },
                new[] { 0.5, 0.1, 0.7, 0.05, 0.1050804335292739, 0.716752791419061, 0.05254021676463695, 0.2927023892852429, 0.944937373127652, 0.14635119464262145 },
                new[] { 0.77, 0.20238862632570875, 0.9709858945736167, 0.10119431316285438, 0.20616145849587592, 0.9797435089433326, 0.10308072924793796, 0.38860160150983825, 0.900686401657084, 0.19430080075491912 },
                new[] { 1, 0.29999999999999993, 1.1999999999999997, 0.14999999999999997, 0.29999999999999993, 1.1999999999999997, 0.14999999999999997, 0.3651643618572814, 0.9128629338716221, 0.1825821809286407 },
            };
            foreach (var x in r)
            {
                float t = (float)x[0];
                AssertV(curve.GetPoint(t), x[1], x[2], x[3], 1e-6, $"getPoint({t})");
                AssertV(curve.GetPointAt(t), x[4], x[5], x[6], 1e-5, $"getPointAt({t})");
                AssertV(curve.GetTangentAt(t), x[7], x[8], x[9], 1e-3, $"getTangentAt({t})");
            }
            Geo tube = PrimitiveMeshes.Tube(curve, 10, 0.007f, 6);
            Assert.That(tube.VertexCount, Is.EqualTo(77)); Assert.That(tube.I.Count, Is.EqualTo(360));
            AssertV(tube.P[0], -0.0001339484442723915, 0.1993301957845688, 0.006966593209654093, 2e-5, "tube vertex 0");
            AssertV(tube.P[40], 0.1107645183801651, 0.7144675850868225, 0.05592680722475052, 2e-5, "tube vertex 40");
            AssertV(tube.N[40], 0.8120124340057373, -0.3264580965042114, 0.48379844427108765, 3e-3, "tube normal 40");
            AssertV(tube.P[76], 0.29969295859336853, 1.198746681213379, 0.15688005089759827, 2e-5, "tube vertex 76");
        }

        [Test]
        public void RightHandedEulerOrdersBecomeTheSameQuaternions()
        {
            Quaternion yxz = Rh.Euler(1.7207963267948965f, 0.7f, -0.2f, "YXZ");
            AssertQ(yxz, 0.6862585197321778, 0.29359417253652764, -0.31981073096629076, 0.5835861567124416);
            Quaternion xyz = Rh.Euler(0f, 0.35f, 1.5707963267948966f, "XYZ");
            AssertQ(xyz, 0.12311304475219215, 0.12311304475219216, 0.696306813274037, 0.6963068132740371);
            // And the mirror: right-handed (x, y, z, w) -> Unity (-x, -y, z, w) (ARCHITECTURE.md section 5).
            Assert.That(Rh.ToUnity(xyz), Is.EqualTo(Coords.FromRightHanded(xyz)));
        }

        [Test]
        public void ToUnityMeshMirrorsZAndReversesWinding()
        {
            var g = new Geo();
            g.P.Add(new Vector3(0, 0, 1)); g.P.Add(new Vector3(1, 0, 1)); g.P.Add(new Vector3(0, 1, 1));
            for (int i = 0; i < 3; i++) { g.N.Add(new Vector3(0, 0, 1)); g.UV.Add(Vector2.zero); }
            g.I.AddRange(new[] { 0, 1, 2 });
            var m = g.ToUnityMesh("t");
            try
            {
                Assert.That(m.vertices[0], Is.EqualTo(new Vector3(0, 0, -1)));
                Assert.That(m.normals[0], Is.EqualTo(new Vector3(0, 0, -1)));
                Assert.That(m.triangles, Is.EqualTo(new[] { 0, 2, 1 }));
                // The counter-clockwise (right-handed, front face +z) triangle stays front-facing toward its normal in Unity.
                Vector3 a = m.vertices[0], b = m.vertices[2], c = m.vertices[1];
                Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), m.normals[0]), Is.GreaterThan(0f), "Unity front faces wind clockwise seen from the normal");
            }
            finally { Object.DestroyImmediate(m); }
        }

        static void AssertV(Vector3 v, double x, double y, double z, double tol, string what)
        {
            Assert.That(v.x, Is.EqualTo(x).Within(tol), what + " x");
            Assert.That(v.y, Is.EqualTo(y).Within(tol), what + " y");
            Assert.That(v.z, Is.EqualTo(z).Within(tol), what + " z");
        }

        static void AssertQ(Quaternion q, double x, double y, double z, double w)
        {
            Assert.That(q.x, Is.EqualTo(x).Within(1e-6)); Assert.That(q.y, Is.EqualTo(y).Within(1e-6));
            Assert.That(q.z, Is.EqualTo(z).Within(1e-6)); Assert.That(q.w, Is.EqualTo(w).Within(1e-6));
        }
    }
}
