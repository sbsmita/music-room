// The harp's layout and meshes against the reference: the string table, play points, neck centreline and part
// transforms/boxes of the instrument data `harp` (Docs/05-instruments.md), plus vertex samples of the reference meshes
// in the right-handed source frame (the tapered neck tube, the soundbox frustum, a string's open cylinder, the pins, the
// sphere and the box) so the mesh generators match vertex for vertex, then the one mirror into Unity space.
using System;
using MusicRoom.Core;
using MusicRoom.Harp;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Harp
{
    public class HarpGeometryTests
    {
        // Instrument data harp.strings: i, midi, a.x, a.y, b.x, b.y, len, radius, playPoint.x, playPoint.y.
        static readonly float[,] Table =
        {
            { 0, 48, -0.5252f, 0.2119f, -0.6276f, 1.3441f, 1.1369f, 0.0016f, -0.592f, 0.95f },
            { 1, 50, -0.5038f, 0.2482f, -0.6013f, 1.368f, 1.1241f, 0.00158f, -0.5649f, 0.95f },
            { 2, 52, -0.4823f, 0.2845f, -0.5746f, 1.3906f, 1.11f, 0.00155f, -0.5378f, 0.95f },
            { 3, 53, -0.4608f, 0.3208f, -0.5468f, 1.41f, 1.0926f, 0.00153f, -0.5105f, 0.95f },
            { 4, 55, -0.4393f, 0.3571f, -0.5175f, 1.4246f, 1.0703f, 0.0015f, -0.4827f, 0.95f },
            { 5, 57, -0.4179f, 0.3934f, -0.485f, 1.4329f, 1.0417f, 0.00148f, -0.4538f, 0.95f },
            { 6, 59, -0.3964f, 0.4297f, -0.448f, 1.4356f, 1.0072f, 0.00146f, -0.4231f, 0.95f },
            { 7, 60, -0.3749f, 0.4661f, -0.4087f, 1.4333f, 0.9678f, 0.00143f, -0.3918f, 0.95f },
            { 8, 62, -0.3534f, 0.5024f, -0.3683f, 1.4268f, 0.9246f, 0.00141f, -0.3606f, 0.95f },
            { 9, 64, -0.332f, 0.5387f, -0.3281f, 1.4171f, 0.8785f, 0.00139f, -0.3301f, 0.95f },
            { 10, 65, -0.3105f, 0.575f, -0.2892f, 1.405f, 0.8303f, 0.00136f, -0.3009f, 0.95f },
            { 11, 67, -0.289f, 0.6113f, -0.253f, 1.3912f, 0.7808f, 0.00134f, -0.2734f, 0.95f },
            { 12, 69, -0.2675f, 0.6476f, -0.2197f, 1.3758f, 0.7298f, 0.00131f, -0.2477f, 0.95f },
            { 13, 71, -0.2461f, 0.6839f, -0.1867f, 1.3571f, 0.6758f, 0.00129f, -0.2226f, 0.95f },
            { 14, 72, -0.2246f, 0.7203f, -0.1547f, 1.3359f, 0.6197f, 0.00127f, -0.1985f, 0.95f },
            { 15, 74, -0.2031f, 0.7566f, -0.1244f, 1.313f, 0.5619f, 0.00124f, -0.1758f, 0.95f },
            { 16, 76, -0.1816f, 0.7929f, -0.0968f, 1.2889f, 0.5033f, 0.00122f, -0.1548f, 0.95f },
            { 17, 77, -0.1602f, 0.8292f, -0.0727f, 1.2646f, 0.4441f, 0.0012f, -0.1339f, 0.9598f },
            { 18, 79, -0.1387f, 0.8655f, -0.0544f, 1.2427f, 0.3865f, 0.00117f, -0.1134f, 0.9787f },
            { 19, 81, -0.1172f, 0.9018f, -0.0421f, 1.2235f, 0.3303f, 0.00115f, -0.0947f, 0.9983f },
            { 20, 83, -0.0957f, 0.9381f, -0.0323f, 1.2037f, 0.273f, 0.00112f, -0.0767f, 1.0178f },
            { 21, 84, -0.0743f, 0.9745f, -0.0242f, 1.1835f, 0.215f, 0.0011f, -0.0592f, 1.0372f },
        };

        // harp.neck.centrelineByArcLength: t, x, y, radius (every 4th sample plus the ends).
        static readonly float[,] Centreline =
        {
            { 0f, -0.68f, 1.3f, 0.04f }, { 0.1f, -0.6145f, 1.3561f, 0.04f }, { 0.2f, -0.5472f, 1.4098f, 0.04f },
            { 0.3f, -0.4656f, 1.435f, 0.04f }, { 0.4f, -0.3798f, 1.429f, 0.04f }, { 0.5f, -0.2963f, 1.4074f, 0.04f },
            { 0.6f, -0.2168f, 1.3743f, 0.038f }, { 0.7f, -0.144f, 1.3282f, 0.034f }, { 0.8f, -0.0791f, 1.2714f, 0.03f },
            { 0.9f, -0.031f, 1.2005f, 0.026f }, { 0.975f, -0.0083f, 1.1399f, 0.024f }, { 1f, 0f, 1.12f, 0.024f },
        };

        [Test]
        public void StringTableMatchesTheReferenceLayout()
        {
            StringLayout[] s = HarpGeometry.Strings;
            Assert.That(s.Length, Is.EqualTo(22));
            for (int i = 0; i < 22; i++)
            {
                Assert.That(s[i].Index, Is.EqualTo(i));
                Assert.That(s[i].Midi, Is.EqualTo((int)Table[i, 1]), "midi " + i);
                Assert.That(HarpGeometry.MidiOf(i), Is.EqualTo((int)Table[i, 1]));
                Assert.That(s[i].A.x, Is.EqualTo(Table[i, 2]).Within(1e-4f), "a.x " + i);
                Assert.That(s[i].A.y, Is.EqualTo(Table[i, 3]).Within(1e-4f), "a.y " + i);
                Assert.That(s[i].B.x, Is.EqualTo(Table[i, 4]).Within(1e-4f), "b.x " + i);
                Assert.That(s[i].B.y, Is.EqualTo(Table[i, 5]).Within(1e-4f), "b.y " + i);
                Assert.That(s[i].A.z, Is.EqualTo(0f));
                Assert.That(s[i].B.z, Is.EqualTo(0f));
                Assert.That(s[i].Length, Is.EqualTo(Table[i, 6]).Within(1e-4f), "len " + i);
                Assert.That(s[i].Radius, Is.EqualTo(Table[i, 7]).Within(1e-5f), "radius " + i);
                Vector3 pp = HarpGeometry.PlayPoint(i);
                // 10-upgrade harp.md 5.2: u clamped to [.30, .40] (the table's is [.30, .70]): the table's point where its u is at
                // most .40 (B4 and up), else .40 up the string, toward the soundboard.
                double refU = (Table[i, 9] - Table[i, 3]) / (Table[i, 5] - Table[i, 3]);
                float u = (float)Math.Min(refU, 0.40);
                Assert.That(pp.x, Is.EqualTo(Table[i, 2] + u * (Table[i, 4] - Table[i, 2])).Within(1e-4f), "playPoint.x " + i);
                Assert.That(pp.y, Is.EqualTo(Table[i, 3] + u * (Table[i, 5] - Table[i, 3])).Within(1e-4f), "playPoint.y " + i);
                if (refU <= 0.40) Assert.That(pp.y, Is.EqualTo(Table[i, 9]).Within(1e-4f), "unchanged where the table's point is low enough " + i);
                Assert.That(pp.z, Is.EqualTo(0f));
                StringColour want = i % 7 == 0 ? StringColour.Red : i % 7 == 3 ? StringColour.Blue : StringColour.Cream;
                Assert.That(s[i].Colour, Is.EqualTo(want), "C strings red, F strings blue");
            }
            Assert.That(s[0].Length, Is.EqualTo(1.137f).Within(1e-3f), "C3 by the pillar, 1.137 m");
            Assert.That(s[21].Length, Is.EqualTo(0.215f).Within(1e-3f), "C6, 21.5 cm, in the open");
            Assert.That(HarpGeometry.PlayPoint(-1), Is.EqualTo(Vector3.zero));
            Assert.That(HarpGeometry.PlayPoint(22), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void BassStringClearsThePillarAndTrebleEndsBeforeTheSoundbox()
        {
            // Layout rule: the bass string stays at least 5.4 cm from the pillar's axis (2 cm clear of its surface).
            StringLayout s0 = HarpGeometry.Strings[0];
            Vector2 pa = new Vector2(-0.7f, 0.02f), pb = new Vector2(-0.68f, 1.3f);
            for (int k = 0; k <= 20; k++)
            {
                Vector2 p = Vector2.Lerp(new Vector2(s0.A.x, s0.A.y), new Vector2(s0.B.x, s0.B.y), k / 20f);
                Vector2 d = pb - pa;
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, d) / d.sqrMagnitude);
                Assert.That(Vector2.Distance(p, pa + d * t), Is.GreaterThanOrEqualTo(0.054f), "bass string vs pillar axis at " + k);
            }
        }

        [Test]
        public void SingleModeHomeIsTheMeanStringMidpoint()
        {
            Assert.That(HarpGeometry.MeanStringMidX(), Is.EqualTo(-0.2966f).Within(1e-4f), "07 layout table: (-0.2966, 0, 0.45) yaw 180");
        }

        [Test]
        public void NeckCentrelineByArcLengthMatches()
        {
            CatmullRomCurve neck = HarpGeometry.Neck;
            neck.GetPoint(0, out double x0, out double y0, out _);
            neck.GetPoint(1, out double x1, out double y1, out _);
            Assert.That(x0, Is.EqualTo(-0.68).Within(1e-12));
            Assert.That(y0, Is.EqualTo(1.3).Within(1e-12));
            Assert.That(x1, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(y1, Is.EqualTo(1.12).Within(1e-12));
            for (int k = 0; k < Centreline.GetLength(0); k++)
            {
                double t = Centreline[k, 0];
                neck.GetPointAt(t, out double x, out double y, out double z);
                Assert.That(x, Is.EqualTo(Centreline[k, 1]).Within(1e-4), "x at " + t);
                Assert.That(y, Is.EqualTo(Centreline[k, 2]).Within(1e-4), "y at " + t);
                Assert.That(z, Is.EqualTo(0.0).Within(1e-12));
                Assert.That(HarpGeometry.NeckRadius * HarpGeometry.NeckTaper(t), Is.EqualTo(Centreline[k, 3]).Within(6e-4f), "radius at " + t);
            }
            Assert.That(neck.UToT(0), Is.EqualTo(0.0));
            Assert.That(neck.UToT(1), Is.EqualTo(1.0));
        }

        [Test]
        public void NeckTubeMatchesTheReferenceVertexForVertex()
        {
            // Tube along the curve, 40 tubular x 12 radial segments, radius .04, the radius taper, smooth normals (right-handed).
            MeshData g = HarpGeometry.NeckMesh();
            Assert.That(g.VertexCount, Is.EqualTo(533));
            Assert.That(g.Indices.Count, Is.EqualTo(2880));
            AssertSample(g, 0, new Vector3(-0.68f, 1.3f, 0.04f), new Vector3(-0.15811f, 0.20506f, 0.96589f), new Vector2(0f, 0f));
            AssertSample(g, 5, new Vector3(-0.6917122f, 1.3162118f, -0.0346410f), new Vector3(-0.35005f, 0.45386f, -0.81944f), new Vector2(0f, 0.41667f));
            AssertSample(g, 13, new Vector3(-0.6629217f, 1.3131680f, 0.04f), new Vector3(-0.16135f, 0.20240f, 0.96592f), new Vector2(0.025f, 0f));
            AssertSample(g, 100, new Vector3(-0.5414997f, 1.3654806f, 0f), new Vector3(0.58693f, -0.80963f, 0.00210f), new Vector2(0.175f, 0.75f));
            AssertSample(g, 271, new Vector3(-0.3026703f, 1.3884383f, 0.0346410f), new Vector3(-0.15892f, -0.47192f, 0.86720f), new Vector2(0.5f, 0.91667f));
            AssertSample(g, 400, new Vector3(-0.1285359f, 1.2802078f, 0.016f), new Vector3(-0.53028f, -0.68233f, 0.50322f), new Vector2(0.75f, 0.83333f));
            AssertSample(g, 532, new Vector3(0f, 1.12f, 0.024f), new Vector3(-0.23880f, -0.10020f, 0.96589f), new Vector2(1f, 1f));
        }

        static void AssertSample(MeshData g, int i, Vector3 p, Vector3 n, Vector2 uv)
        {
            Assert.That(Vector3.Distance(g.Positions[i], p), Is.LessThan(2e-6f), $"position {i}: {g.Positions[i]:F7} vs {p:F7}");
            Assert.That(Vector3.Distance(g.Normals[i], n), Is.LessThan(2e-4f), $"normal {i}: {g.Normals[i]:F5} vs {n:F5}");
            Assert.That(Vector2.Distance(g.Uvs[i], uv), Is.LessThan(1e-4f), $"uv {i}");
        }

        [Test]
        public void SoundboxMatchesTheReference()
        {
            HarpGeometry.SoundboxAxis(out Vector3 c0, out Vector3 c1);
            Assert.That(c0.x, Is.EqualTo(-0.47f).Within(1e-4f));
            Assert.That(c0.y, Is.EqualTo(0.1227f).Within(1e-4f));
            Assert.That(c1.x, Is.EqualTo(0.0491f).Within(1e-4f));
            Assert.That(c1.y, Is.EqualTo(1.071f).Within(1e-4f));
            Assert.That((c1 - c0).magnitude, Is.EqualTo(1.0811f).Within(1e-4f));
            Vector3 o = HarpGeometry.Out;
            Assert.That(o.x, Is.EqualTo(0.8607f).Within(1e-4f));
            Assert.That(o.y, Is.EqualTo(-0.509f).Within(1e-4f));

            HarpPart box = HarpGeometry.BuildFrame()[0];
            Assert.That(box.Name, Is.EqualTo("Soundbox"));
            Assert.That(box.Material, Is.EqualTo(HarpWood.Maple));
            Assert.IsTrue(box.CastShadows);
            AssertPosition(box.LocalPosition, new Vector3(-0.2104443f, 0.5968219f, 0f), 1e-6f);
            // right-handed (0.0387604, 0.1515565, -0.2447234, 0.9568901) -> Unity (-x, -y, z, w) = JSON quat_unity.
            AssertRotation(box.LocalRotation, new Quaternion(-0.0387604f, -0.1515565f, -0.2447234f, 0.9568901f), 1e-5f);
            Assert.That(box.Mesh.VertexCount, Is.EqualTo(60), "toNonIndexed: 20 triangles");
            // Reference world samples (vertex 0, 1, 2 share face 0's flat normal; 30 is a cap), mirrored z for Unity.
            AssertWorld(box, 0, new Vector3(0.0639714f, 1.0628250f, -0.0523081f), new Vector3(0.7254392f, -0.3586611f, -0.5874522f));
            AssertWorld(box, 1, new Vector3(-0.4428435f, 0.1078234f, -0.0951057f), new Vector3(0.7254392f, -0.3586611f, -0.5874522f));
            AssertWorld(box, 2, new Vector3(0.0973092f, 1.0445794f, 0f), new Vector3(0.7254392f, -0.3586611f, -0.5874522f));
            AssertWorld(box, 29, new Vector3(0.0639714f, 1.0628250f, -0.0523081f), new Vector3(-0.2547627f, 0.1777980f, -0.9505177f));
            AssertWorld(box, 30, new Vector3(0.0639714f, 1.0628250f, -0.0523081f), new Vector3(0.4800956f, 0.8772162f, 0f));
            AssertWorld(box, 59, new Vector3(-0.4699510f, 0.1226592f, 0f), new Vector3(-0.4800956f, -0.8772162f, 0f));
        }

        /// <summary>Vertex i of a Unity-space part (in the part's frame) mapped to harp-local Unity, against the reference world
        /// samples with z negated. ToUnity keeps the vertex order (only the index buffer's winding is reversed).</summary>
        static void AssertWorld(HarpPart part, int refIndex, Vector3 world, Vector3 normal)
        {
            int k = refIndex;
            Vector3 p = part.LocalPosition + part.LocalRotation * Vector3.Scale(part.LocalScale, part.Mesh.Positions[k]);
            Vector3 n = (part.LocalRotation * part.Mesh.Normals[k]).normalized;
            Assert.That(Vector3.Distance(p, world), Is.LessThan(2e-6f), $"world {refIndex}: {p:F7} vs {world:F7}");
            Assert.That(Vector3.Distance(n, normal), Is.LessThan(2e-5f), $"normal {refIndex}: {n:F6} vs {normal:F6}");
        }

        static void AssertPosition(Vector3 got, Vector3 want, float tol) =>
            Assert.That(Vector3.Distance(got, want), Is.LessThan(tol), $"{got:F6} vs {want:F6}");

        static void AssertRotation(Quaternion got, Quaternion want, float tol) =>
            Assert.That(1f - Mathf.Abs(Quaternion.Dot(got, want)), Is.LessThan(tol), $"{got:F6} vs {want:F6}");

        [Test]
        public void FramePartTransformsAndBoxesMatchTheJson()
        {
            HarpPart[] parts = HarpGeometry.BuildFrame();
            string[] names = { "Soundbox", "Soundboard", "Rib", "Pillar", "Cap", "Neck", "Base", "Foot0", "Foot1", "Foot2", "Foot3", "Pins" };
            Assert.That(parts.Length, Is.EqualTo(names.Length));
            for (int i = 0; i < names.Length; i++) Assert.That(parts[i].Name, Is.EqualTo(names[i]));
            // JSON harp.parts pos_unity / quat_unity / box_unity (box = the part's geometry box through its matrix, like
            // Renderer.bounds in a harp at the origin).
            float[][] pos =
            {
                new[] { -0.2104f, 0.5968f, 0f }, new[] { -0.2647f, 0.6239f, 0f }, new[] { -0.275f, 0.635f, 0f }, new[] { -0.69f, 0.66f, 0f },
                new[] { -0.68f, 1.31f, 0f }, new[] { 0f, 0f, 0f }, new[] { -0.33f, 0.04f, 0f }, new[] { -0.72f, 0.02f, 0.09f },
                new[] { -0.72f, 0.02f, -0.09f }, new[] { 0.05f, 0.02f, 0.09f }, new[] { 0.05f, 0.02f, -0.09f }, new[] { 0f, 0f, 0f },
            };
            float[][] quat =
            {
                new[] { -0.03876f, -0.15156f, -0.24472f, 0.95689f }, new[] { 0f, 0f, -0.24878f, 0.96856f }, new[] { 0f, 0f, -0.26387f, 0.96456f },
                new[] { 0f, 0f, -0.00781f, 0.99997f }, new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f, 0f, 1f },
                new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f, 0f, 1f }, new[] { 0f, 0f, 0f, 1f },
            };
            float[][] box =
            {
                new[] { -0.5712f, 0.0644f, -0.1245f, 0.1555f, 1.1264f, 0.1063f }, new[] { -0.5537f, 0.1162f, -0.064f, 0.0243f, 1.1316f, 0.064f },
                new[] { -0.5569f, 0.1659f, -0.008f, 0.0069f, 1.1041f, 0.008f }, new[] { -0.735f, 0.0195f, -0.035f, -0.645f, 1.3005f, 0.035f },
                new[] { -0.725f, 1.265f, -0.045f, -0.635f, 1.355f, 0.045f }, new[] { -0.7034f, 1.1099f, -0.04f, 0.0218f, 1.4756f, 0.04f },
                new[] { -0.805f, 0.02f, -0.11f, 0.145f, 0.06f, 0.11f }, new[] { -0.75f, 0f, 0.07f, -0.69f, 0.04f, 0.11f },
                new[] { -0.75f, 0f, -0.11f, -0.69f, 0.04f, -0.07f }, new[] { 0.02f, 0f, 0.07f, 0.08f, 0.04f, 0.11f },
                new[] { 0.02f, 0f, -0.11f, 0.08f, 0.04f, -0.07f }, new[] { -0.6316f, 1.1795f, -0.04f, -0.0202f, 1.4396f, -0.02f },
            };
            for (int i = 0; i < parts.Length; i++)
            {
                HarpPart p = parts[i];
                AssertPosition(p.LocalPosition, new Vector3(pos[i][0], pos[i][1], pos[i][2]), 1e-4f);
                AssertRotation(p.LocalRotation, new Quaternion(quat[i][0], quat[i][1], quat[i][2], quat[i][3]), 1e-4f);
                Bounds lb = p.Mesh.Bounds();
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) != 0 ? lb.max.x : lb.min.x, (c & 2) != 0 ? lb.max.y : lb.min.y, (c & 4) != 0 ? lb.max.z : lb.min.z);
                    Vector3 w = p.LocalPosition + p.LocalRotation * Vector3.Scale(p.LocalScale, corner);
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
                var wantMin = new Vector3(box[i][0], box[i][1], box[i][2]);
                var wantMax = new Vector3(box[i][3], box[i][4], box[i][5]);
                Assert.That(Vector3.Distance(min, wantMin), Is.LessThan(2e-4f), $"{p.Name} box min {min:F4} vs {wantMin:F4}");
                Assert.That(Vector3.Distance(max, wantMax), Is.LessThan(2e-4f), $"{p.Name} box max {max:F4} vs {wantMax:F4}");
            }
            Assert.That(parts[11].Material, Is.EqualTo(HarpWood.Brass));
            Assert.That(parts[1].LocalScale, Is.EqualTo(new Vector3(1f, 1f, 4f)), "soundboard strip scaled 4x through the plane");
        }

        [Test]
        public void PinsSitBehindTheStringPlaneAtUnityZMinus3cm()
        {
            HarpPart pins = HarpGeometry.BuildFrame()[11];
            Assert.That(pins.Mesh.VertexCount, Is.EqualTo(22 * 52), "22 cylinders (r .004, h .02, 8 radial segments), merged");
            // Right-handed: pin 0 vertex 0 = (-0.6276488, 1.3401020, +0.04) -> Unity z -0.04; vertex 9 z +0.02 -> -0.02.
            AssertPosition(pins.Mesh.Positions[0], new Vector3(-0.6276488f, 1.3401020f, -0.04f), 2e-6f);
            AssertPosition(pins.Mesh.Positions[9], new Vector3(-0.6276488f, 1.3401020f, -0.02f), 2e-6f);
            Bounds b = pins.Mesh.Bounds();
            Assert.That(b.center.z, Is.EqualTo(-0.03f).Within(1e-5f));
            for (int i = 0; i < 22; i++)
            {
                Vector3 c = Vector3.zero;
                for (int k = 0; k < 52; k++) c += pins.Mesh.Positions[i * 52 + k];
                c /= 52f;
                Assert.That(c.x, Is.EqualTo(HarpGeometry.Strings[i].B.x).Within(2e-3f), "pin over string top " + i);
            }
        }

        [Test]
        public void StringMeshIsTheReferenceCylinderMirrored()
        {
            MeshData m = HarpGeometry.StringMesh(7, out Vector3 pos, out Quaternion rot);
            // String 7: open cylinder, radius r, height .9678, 5 radial x 16 height segments: 102 vertices, 480 indices.
            Assert.That(m.VertexCount, Is.EqualTo(102));
            Assert.That(m.Indices.Count, Is.EqualTo(480));
            AssertPosition(m.Positions[0], new Vector3(0f, 0.4839052f, -0.0014333f), 2e-7f);
            AssertPosition(m.Positions[1], new Vector3(0.0013632f, 0.4839052f, -0.0004429f), 2e-7f);
            AssertPosition(m.Positions[6], new Vector3(0f, 0.4234170f, -0.0014333f), 2e-7f);
            AssertPosition(m.Positions[50], new Vector3(0.0008425f, 0f, 0.0011596f), 2e-7f);
            AssertPosition(m.Positions[101], new Vector3(0f, -0.4839052f, -0.0014333f), 2e-7f);
            AssertPosition(pos, (new Vector3(-0.3749167f, 0.46605f, 0f) + new Vector3(-0.4086775f, 1.4332713f, 0f)) * 0.5f, 2e-6f);
            AssertRotation(rot, new Quaternion(0f, 0f, 0.0174445f, 0.9998478f), 1e-6f);
            // Row 0 is the neck end (u = 1), the last row the soundboard end (u = 0).
            Assert.That(m.Positions[0].y, Is.EqualTo(HarpGeometry.Strings[7].Length / 2f).Within(1e-6f));
            Assert.That(m.Positions[101].y, Is.EqualTo(-HarpGeometry.Strings[7].Length / 2f).Within(1e-6f));
            // Its +y points from a to b.
            Vector3 up = rot * Vector3.up;
            Vector3 ab = (HarpGeometry.Strings[7].B - HarpGeometry.Strings[7].A).normalized;
            Assert.That(Vector3.Dot(up, ab), Is.GreaterThan(0.99999f));
        }

        [Test]
        public void SphereAndBoxMeshesMatchTheReference()
        {
            MeshData s = PrimitiveMeshes.Sphere(0.045f, 20, 16);
            Assert.That(s.VertexCount, Is.EqualTo(357));
            Assert.That(s.Indices.Count, Is.EqualTo(1800));
            AssertPosition(s.Positions[25], new Vector3(-0.0027129f, 0.0441353f, 0.0083494f), 2e-7f);
            Assert.That(Vector2.Distance(s.Uvs[25], new Vector2(0.2f, 0.9375f)), Is.LessThan(1e-6f));
            MeshData b = PrimitiveMeshes.Box(0.95f, 0.04f, 0.22f);
            Assert.That(b.VertexCount, Is.EqualTo(24));
            AssertPosition(b.Positions[0], new Vector3(0.475f, 0.02f, 0.11f), 1e-7f);
            AssertPosition(b.Positions[5], new Vector3(-0.475f, 0.02f, 0.11f), 1e-7f);
            AssertPosition(b.Positions[13], new Vector3(0.475f, -0.02f, 0.11f), 1e-7f);
            AssertPosition(b.Positions[23], new Vector3(-0.475f, -0.02f, -0.11f), 1e-7f);
            Assert.That(b.Normals[13], Is.EqualTo(new Vector3(0f, -1f, 0f)));
            Assert.That(b.Normals[23], Is.EqualTo(new Vector3(0f, 0f, -1f)));
            Assert.That(b.Uvs[23], Is.EqualTo(new Vector2(1f, 0f)));
            int[] want = { 0, 2, 1, 2, 3, 1, 4, 6, 5, 6, 7, 5 };
            for (int i = 0; i < want.Length; i++) Assert.That(b.Indices[i], Is.EqualTo(want[i]), "box index " + i);
        }

        [Test]
        public void ConvertedMeshesFaceOutward()
        {
            // After ToUnity the winding is reversed, so Unity's front face (clockwise: cross(b - a, c - a) along the
            // outward normal) agrees with the mirrored vertex normals on every triangle of every part and string.
            foreach (HarpPart p in HarpGeometry.BuildFrame()) AssertOutward(p.Mesh, p.Name);
            for (int i = 0; i < 22; i++) AssertOutward(HarpGeometry.StringMesh(i, out _, out _), "string " + i);
        }

        static void AssertOutward(MeshData m, string name)
        {
            Assert.IsTrue(m.UnitySpace, name);
            int bad = 0;
            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                Vector3 face = Vector3.Cross(m.Positions[b] - m.Positions[a], m.Positions[c] - m.Positions[a]);
                if (face.sqrMagnitude < 1e-14f) continue; // degenerate seam/cap slivers
                Vector3 n = m.Normals[a] + m.Normals[b] + m.Normals[c];
                if (Vector3.Dot(face, n) <= 0f) bad++;
            }
            Assert.That(bad, Is.EqualTo(0), name + ": triangles facing inward");
        }

        [Test]
        public void CoordinateConventionsForTheRig()
        {
            // ARCHITECTURE.md section 5: harp (-.78, 0, .2) yaw -83 deg (right-handed) -> (-.78, 0, -.2) yaw +83 (Unity).
            Assert.That(Coords.FromRightHanded(-0.78f, 0f, 0.2f), Is.EqualTo(HarpInstrument.HomeAll));
            Assert.That(Coords.YawDegFromRightHandedRad(-83f * Mathf.Deg2Rad), Is.EqualTo(HarpInstrument.HomeYawAll).Within(1e-4f));
            Assert.That(HarpGeometry.SourceLocal, Is.EqualTo(new Vector3(-0.33f, 0.8f, 0f)));
            Assert.That(HarpGeometry.BlobLocal, Is.EqualTo(new Vector3(-0.2725f, 0.005f, -0.168f)));
        }
    }
}
