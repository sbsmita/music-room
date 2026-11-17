// The drawn textures (drawFace / drawBubble) sampled at the reference coordinates, the procedural meshes
// (counts, outward winding, the face's uv on the player's side), and the robot chain against its acceptance
// checks (speech band within 3 dB, no clipping, audibly changed).
using MusicRoom.Audio.Dsp;
using MusicRoom.Audio.Synths;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public class SunnyArtTests
    {
        static void Near(Color got, Color want, float tol, string what)
        {
            Assert.That(got.r, Is.EqualTo(want.r).Within(tol), what + " r");
            Assert.That(got.g, Is.EqualTo(want.g).Within(tol), what + " g");
            Assert.That(got.b, Is.EqualTo(want.b).Within(tol), what + " b");
        }

        static int Happy(bool eyesShut = false, bool mouthOpen = false) => SunnyFace.StateIndex(true, eyesShut, mouthOpen);
        static int Calm(bool eyesShut = false, bool mouthOpen = false) => SunnyFace.StateIndex(false, eyesShut, mouthOpen);

        [Test]
        public void FaceGradientEyesCheeksAndShine()
        {
            SunnyCanvas c = SunnyFace.Paint(Happy());
            Assert.That(c.W, Is.EqualTo(512));
            Assert.That(c.H, Is.EqualTo(256));
            Near(c.At(128, 127), SunnyFace.Hex("#fff1a8"), 0.02f, "centre of the face");
            Near(c.At(400, 128), SunnyFace.Hex("#f59a2a"), 0.01f, "the back of the sphere");
            Assert.That(c.At(128, 127).a, Is.EqualTo(1f), "opaque");
            // the 0.45 stop at d = 10 + 0.45 * 246
            Near(c.At(128 + 121, 127), SunnyFace.Hex("#ffd45a"), 0.02f, "the middle stop");
            // eyes at (128 -+ 24, 107.52), ink, with a white shine at (+3.5, -4)
            Near(c.At(103, 109), SunnyFace.Ink, 0.02f, "left eye");
            Near(c.At(151, 109), SunnyFace.Ink, 0.02f, "right eye");
            Near(c.At(107, 103), Color.white, 0.05f, "shine");
            // cheeks: a translucent rose over the skin at (86, 145.92)
            Color cheek = c.At(86, 145), plain = c.At(64, 145);
            Assert.That(cheek.r, Is.GreaterThan(0.95f));
            Assert.That(cheek.g, Is.LessThan(plain.g - 0.05f), "the cheek tints the skin toward rose");
        }

        [Test]
        public void EyesShutToALineWhileBlinking()
        {
            SunnyCanvas c = SunnyFace.Paint(Calm(eyesShut: true));
            Near(c.At(104, 107), SunnyFace.Ink, 0.02f, "the line (y 105.5 .. 110.5)");
            Assert.That(c.At(104, 101).r, Is.GreaterThan(0.9f), "above the line is skin");
            Assert.That(c.At(104, 114).r, Is.GreaterThan(0.9f), "below the line is skin");
            Assert.That(c.At(107, 103).g, Is.Not.EqualTo(1f).Within(0.01f), "no shine on a shut eye");
        }

        [Test]
        public void MouthsSmileOpenAndCalm()
        {
            // open "o": filled (#7a2a1a) inside a 5 px ink ring
            SunnyCanvas open = SunnyFace.Paint(Happy(mouthOpen: true));
            Near(open.At(127, 150), SunnyFace.MouthFill, 0.03f, "inside the open mouth");
            Near(open.At(127, 151 + 12 - 1), SunnyFace.Ink, 0.08f, "the open mouth's outline (happy ry 12)");
            SunnyCanvas calmOpen = SunnyFace.Paint(Calm(mouthOpen: true));
            Assert.That(calmOpen.At(127, 165).r, Is.GreaterThan(0.9f), "the calm o is smaller (ry 9)");
            // happy shut: a filled smile (segment under the chord at y = 140.8 + 20 sin .15pi = 149.9)
            SunnyCanvas happy = SunnyFace.Paint(Happy());
            Near(happy.At(127, 155), SunnyFace.MouthFill, 0.03f, "inside the smile");
            Near(happy.At(127, 162), SunnyFace.Ink, 0.08f, "the smile's outline, outside r 20 (the fill covers its inner half)");
            Assert.That(happy.At(127, 146).r, Is.GreaterThan(0.9f), "above the chord is skin");
            // calm shut: just the 5 px arc, r 14 at (128, 143.36)
            SunnyCanvas calm = SunnyFace.Paint(Calm());
            Near(calm.At(127, 157), SunnyFace.Ink, 0.08f, "the calm smile's stroke");
            Assert.That(calm.At(127, 150).r, Is.GreaterThan(0.9f), "no fill inside the calm smile");
        }

        [Test]
        public void TheEightFaceStatesAreDistinctAndTheTextureIsFlipped()
        {
            var pixels = new Color32[SunnyFace.StateCount][];
            for (int s = 0; s < SunnyFace.StateCount; s++)
            {
                pixels[s] = new Color32[SunnyFace.W * SunnyFace.H];
                SunnyFace.Paint(s).ToPixels(pixels[s], Color.black);
                Assert.That(SunnyFace.Name(s), Does.StartWith("Guide/sunnyFace_"));
            }
            for (int a = 0; a < SunnyFace.StateCount; a++)
                for (int b = a + 1; b < SunnyFace.StateCount; b++)
                {
                    int diff = 0;
                    for (int i = 0; i < pixels[a].Length; i++) if (!pixels[a][i].Equals(pixels[b][i])) diff++;
                    Assert.That(diff, Is.GreaterThan(20), $"{SunnyFace.Name(a)} vs {SunnyFace.Name(b)}");
                }
            Assert.That(SunnyFace.StateIndex(true, true, true), Is.EqualTo(7));
            Assert.That(SunnyFace.Name(SunnyFace.StateIndex(true, false, true)), Is.EqualTo("Guide/sunnyFace_happy_eyesOpen_mouthOpen"));
            // Canvas row y lands in texture row h-1-y: the eye (canvas y 107) is in the upper half of the texture.
            Texture2D tex = SunnyFace.Create(Happy(), true);
            try
            {
                Assert.That(tex.width, Is.EqualTo(512));
                Assert.That(tex.height, Is.EqualTo(256));
                Assert.That(tex.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(tex.mipmapCount, Is.GreaterThan(1));
                Color eye = tex.GetPixel(103, 255 - 109);
                Near(eye, SunnyFace.Ink, 0.02f, "eye in the flipped texture");
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void BubbleIsACreamRoundedBoxWithAnAmberOutlineAndATail()
        {
            SunnyCanvas c = SunnyBubbleArt.Paint();
            Assert.That(c.W, Is.EqualTo(768));
            Assert.That(c.H, Is.EqualTo(340));
            Color mid = c.At(384, 153);
            Near(mid, SunnyBubbleArt.Fill, 0.005f, "fill");
            Assert.That(mid.a, Is.EqualTo(0.96f).Within(0.005f), "fill alpha 0.96");
            Color top = c.At(384, 10);
            Near(top, SunnyBubbleArt.Stroke, 0.02f, "outline on the top edge");
            Assert.That(top.a, Is.EqualTo(1f).Within(0.01f));
            Assert.That(c.At(384, 3).a, Is.EqualTo(0f), "outside the box");
            Assert.That(c.At(14, 14).a, Is.EqualTo(0f), "the rounded corner is cut away");
            Assert.That(c.At(60, 153).a, Is.GreaterThan(0.9f), "left side inside");
            // the tail: below the box only between x 130 and 200, its tip near (130, 334)
            Assert.That(c.At(160, 310).a, Is.GreaterThan(0.9f), "inside the tail");
            Assert.That(c.At(400, 310).a, Is.EqualTo(0f), "no tail elsewhere");
            Assert.That(c.At(131, 330).a, Is.GreaterThan(0.5f), "the tail's tip");
            var px = new Color32[SunnyBubbleArt.W * SunnyBubbleArt.H];
            c.ToPixels(px, SunnyBubbleArt.Stroke);
            Color32 clear = px[(SunnyBubbleArt.H - 1 - 3) * SunnyBubbleArt.W + 384];
            Assert.That(clear.a, Is.EqualTo(0));
            Assert.That(clear.r, Is.EqualTo(240), "transparent texels carry the outline colour (no dark mip fringe)");
            Assert.That(SunnyBubbleArt.Outline().Count, Is.GreaterThan(60));
        }

        [Test]
        public void SphereIsLowPolyWoundOutwardWithTheFaceTowardThePlayer()
        {
            Mesh m = SunnyMeshes.Sphere(0.045f, 20, 14);
            try
            {
                Assert.That(m.vertexCount, Is.EqualTo(21 * 15));
                int tris = m.triangles.Length / 3;
                Assert.That(tris, Is.EqualTo(20 * (14 * 2 - 2)));
                Assert.That(tris, Is.LessThan(700), "lowPoly");
                AssertOutward(m);
                // uv (0.25, 0.5) is the vertex ix 5, iy 7: on the player's side (local -z).
                Vector3 v = m.vertices[7 * 21 + 5];
                Vector2 uv = m.uv[7 * 21 + 5];
                Assert.That(uv.x, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(uv.y, Is.EqualTo(0.5f).Within(1e-6f));
                Assert.That(v.z, Is.EqualTo(-0.045f).Within(1e-5f));
                Assert.That(Mathf.Abs(v.x) + Mathf.Abs(v.y), Is.LessThan(1e-5f));
                // u increases to the viewer's right (+x) across the face: not mirrored.
                Assert.That(m.vertices[7 * 21 + 6].x, Is.GreaterThan(m.vertices[7 * 21 + 4].x), "u = .3 lies right of u = .2 for the player");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void DemoMeshesHaveTheReferenceDimensions()
        {
            Mesh palm = SunnyMeshes.Box(SunnyDemo.PalmBox, "t");
            Assert.That(palm.bounds.size.x, Is.EqualTo(0.075f).Within(1e-6f));
            Assert.That(palm.bounds.size.y, Is.EqualTo(0.085f).Within(1e-6f));
            Assert.That(palm.bounds.size.z, Is.EqualTo(0.022f).Within(1e-6f));
            AssertOutward(palm);
            Mesh cap = SunnyMeshes.Capsule(SunnyDemo.FingerRadius, SunnyDemo.StraightLength(0.065f), "t");
            Assert.That(cap.bounds.size.y, Is.EqualTo(0.065f).Within(1e-5f), "capsule length + 2r = the finger length");
            Assert.That(cap.bounds.size.x, Is.EqualTo(2f * SunnyDemo.FingerRadius).Within(1e-5f));
            AssertOutward(cap);
            Mesh cone = SunnyMeshes.ConeMesh(SunnyDemo.ConeR, SunnyDemo.ConeH, SunnyDemo.ConeSides);
            Assert.That(cone.bounds.max.y, Is.EqualTo(0.015f).Within(1e-6f));
            Assert.That(cone.bounds.min.y, Is.EqualTo(-0.015f).Within(1e-6f));
            AssertOutward(cone);
            Mesh ring = SunnyMeshes.Ring(SunnyDemo.RingInner, SunnyDemo.RingOuter, SunnyDemo.RingSegments);
            Assert.That(ring.bounds.extents.x, Is.EqualTo(0.036f).Within(1e-5f));
            Assert.That(ring.triangles.Length / 3, Is.EqualTo(80));
            Assert.That(SunnyMeshes.GhostPanelPoints().Length, Is.EqualTo(14), "outline + three rows");
            Vector3[] dashes = SunnyMeshes.DashPoints();
            Assert.That(dashes.Length / 2, Is.EqualTo(13), "0.28 m of 1 cm dashes with 1.2 cm gaps");
            Assert.That(dashes[0].x, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(dashes[1].x, Is.EqualTo(0.14f).Within(1e-6f));
            Mesh quad = SunnyMeshes.Quad(0.26f, 0.115f, "t");
            Vector3[] v = quad.vertices;
            int[] t = quad.triangles;
            Assert.That(Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]).z, Is.LessThan(0f), "the quad's front faces the player (-z)");
            foreach (Object o in new Object[] { palm, cap, cone, ring, quad }) Object.DestroyImmediate(o);
        }

        static void AssertOutward(Mesh m)
        {
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-16f) continue;
                Assert.That(Vector3.Dot(cross, (a + b + c) / 3f), Is.GreaterThan(0f), $"{m.name} triangle {i / 3} faces inward");
            }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator RobotChainKeepsTheWordsAndAddsTheMetal()
        {
            // Acceptance checks speechBandWithin3dB, robotNoClip, robotDiffersFromDry, on the real intro recording.
            var clip = Resources.Load<AudioClip>("Voice/intro_vr");
            Assert.That(clip, Is.Not.Null);
            // Load In Background: the editor finishes the decode between frames.
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            float t0 = Time.realtimeSinceStartup;
            while (clip.loadState != AudioDataLoadState.Loaded && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            var x = new float[clip.samples];
            Assert.IsTrue(clip.GetData(x, 0), "the clip's samples are readable (Decompress On Load, preloaded)");
            double rms = 0;
            foreach (float s in x) rms += s * s;
            rms = System.Math.Sqrt(rms / x.Length);
            Assert.That(rms, Is.GreaterThan(0.02), "not silent");
            float[] y = RobotVoice.Process(x, clip.frequency);
            double band(float[] s)
            {
                var f1 = Biquad.Create(BiquadType.Highpass, 300, System.Math.Sqrt(0.5), 0, clip.frequency);
                var f2 = Biquad.Create(BiquadType.Highpass, 300, System.Math.Sqrt(0.5), 0, clip.frequency);
                var f3 = Biquad.Create(BiquadType.Lowpass, 3400, System.Math.Sqrt(0.5), 0, clip.frequency);
                var f4 = Biquad.Create(BiquadType.Lowpass, 3400, System.Math.Sqrt(0.5), 0, clip.frequency);
                double e = 0;
                foreach (float v in s)
                {
                    float o = f4.Process(f3.Process(f2.Process(f1.Process(v))));
                    e += o * o;
                }
                return e;
            }
            double db = 10 * System.Math.Log10(band(y) / band(x));
            Assert.That(System.Math.Abs(db), Is.LessThanOrEqualTo(3.0), $"speech band {db:F2} dB");
            float peak = 0;
            foreach (float v in y) peak = Mathf.Max(peak, Mathf.Abs(v));
            Assert.That(peak, Is.LessThan(1f), "no clipping");
            double num = 0, da = 0, dbb = 0;
            for (int i = 0; i < x.Length; i++) { num += x[i] * y[i]; da += x[i] * x[i]; dbb += y[i] * y[i]; }
            Assert.That(System.Math.Abs(num / System.Math.Sqrt(da * dbb)), Is.LessThan(0.999), "audibly changed");
            // The mouth envelope (RMS per 21.3 ms) has words and gaps. Computed here rather than with the Audio module's
            // own envelope helper (its signature is the Audio module's to change): only RobotVoice.Process is used.
            int window = Mathf.RoundToInt(1024f / 48000f * clip.frequency);
            var env = new float[(y.Length + window - 1) / window];
            for (int w = 0; w < env.Length; w++)
            {
                double e = 0;
                int from = w * window, to = Mathf.Min(y.Length, from + window);
                for (int i = from; i < to; i++) e += y[i] * y[i];
                env[w] = (float)System.Math.Sqrt(e / (to - from));
            }
            float max = 0, min = 1;
            for (int i = env.Length / 5; i < env.Length * 4 / 5; i++) { max = Mathf.Max(max, env[i]); min = Mathf.Min(min, env[i]); }
            Assert.That(max, Is.GreaterThan(0.05f));
            Assert.That(min, Is.LessThan(max * 0.35f), "a gap between words shuts the mouth");
        }
    }
}
