// The kit's formulas against the reference numbers: the instrument data's `curves` (Docs/05-instruments.md: drumFinger,
// drumStick, nodKick, sampled from the reference kit), the spec 05 section 3 worked examples, the strike zones of
// strikeAt, the stick grip of handGrip and the packing of the cymbal map from Core's ProcTex.
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.Drums
{
    public class DrumMathTests
    {
        // Instrument data curves.drumFinger / drumStick / nodKick: [speed, velocity].
        static readonly float[,] FingerCurve = { { 0.12f, 0.05f }, { 0.2f, 0.1362f }, { 0.3f, 0.2403f }, { 0.5f, 0.4054f }, { 0.8f, 0.6093f }, { 1f, 0.7298f }, { 1.5f, 1f }, { 2f, 1f } };
        static readonly float[,] StickCurve = { { 0.3f, 0.05f }, { 0.5f, 0.1617f }, { 0.8f, 0.3071f }, { 1f, 0.3887f }, { 1.5f, 0.5669f }, { 2f, 0.7234f }, { 3f, 1f }, { 4f, 1f } };
        static readonly float[,] NodCurve = { { 1.6f, 0.25f }, { 2.5f, 0.3878f }, { 3f, 0.4643f }, { 4f, 0.6173f }, { 5f, 0.7704f }, { 6.5f, 1f }, { 8f, 1f } };

        [Test]
        public void FingerVelocityCurveMatchesTheReferenceSamples()
        {
            for (int i = 0; i < FingerCurve.GetLength(0); i++)
                Assert.That(DrumMath.StrikeVelocity(FingerCurve[i, 0], false), Is.EqualTo(FingerCurve[i, 1]).Within(5e-4f), $"finger {FingerCurve[i, 0]} m/s");
        }

        [Test]
        public void StickVelocityCurveMatchesTheReferenceSamples()
        {
            for (int i = 0; i < StickCurve.GetLength(0); i++)
                Assert.That(DrumMath.StrikeVelocity(StickCurve[i, 0], true), Is.EqualTo(StickCurve[i, 1]).Within(5e-4f), $"stick {StickCurve[i, 0]} m/s");
        }

        [Test]
        public void SlowerThanV0IsSilentAndTheWorkedExamplesHold()
        {
            Assert.That(DrumMath.StrikeVelocity(0.119f, false), Is.LessThan(0f), "below .12 m/s a fingertip is silent");
            Assert.That(DrumMath.StrikeVelocity(0.299f, true), Is.LessThan(0f), "below .3 m/s a stick is silent");
            // spec 05 section 3: "Fingers: .3 m/s -> .24, .5 -> .41, 1 -> .73. Sticks: .5 -> .16, 1 -> .39, 2 -> .72."
            Assert.That(DrumMath.StrikeVelocity(0.3f, false), Is.EqualTo(0.24f).Within(0.005f));
            Assert.That(DrumMath.StrikeVelocity(0.5f, false), Is.EqualTo(0.41f).Within(0.006f));
            Assert.That(DrumMath.StrikeVelocity(1f, false), Is.EqualTo(0.73f).Within(0.005f));
            Assert.That(DrumMath.StrikeVelocity(0.5f, true), Is.EqualTo(0.16f).Within(0.005f));
            Assert.That(DrumMath.StrikeVelocity(1f, true), Is.EqualTo(0.39f).Within(0.005f));
            Assert.That(DrumMath.StrikeVelocity(2f, true), Is.EqualTo(0.72f).Within(0.005f));
            // "Ghost notes come out near .05."
            Assert.That(DrumMath.StrikeVelocity(0.125f, false), Is.EqualTo(0.05f).Within(1e-6f));
        }

        [Test]
        public void NodVelocityCurveMatchesTheReferenceSamples()
        {
            for (int i = 0; i < NodCurve.GetLength(0); i++)
                Assert.That(DrumMath.NodVelocity(NodCurve[i, 0]), Is.EqualTo(NodCurve[i, 1]).Within(5e-4f), $"nod {NodCurve[i, 0]} rad/s");
        }

        [Test]
        public void StrikeZonesFollowTheReferenceStrikeAt()
        {
            // Drum: head inside .9 r, rim past it (and up to 3 cm past the head), pos clamps to 1.
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Drum, false, false, 0.18f, 0.16f, false), 0.16f / 0.18f, DrumZone.Head);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Drum, false, false, 0.18f, 0.163f, false), 0.163f / 0.18f, DrumZone.Rim);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Drum, false, false, 0.18f, 0.205f, false), 1f, DrumZone.Rim);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Drum, false, false, 0.18f, 0f, false), 0f, DrumZone.Head);
            // The pad has no rim.
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Drum, false, true, 0.1f, 0.099f, false), 0.99f, DrumZone.Head);
            // Hi-hat: top up to .75 of the radius, edge past it; the open flag rides along.
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Cymbal, true, false, 0.17f, 0.17f * 0.74f, false), 0.74f, DrumZone.Top);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Cymbal, true, false, 0.17f, 0.17f * 0.76f, true), 0.76f, DrumZone.Edge);
            Assert.That(DrumMath.StrikeAt(DrumPieceType.Cymbal, true, false, 0.17f, 0.05f, true).Open, Is.True);
            Assert.That(DrumMath.StrikeAt(DrumPieceType.Cymbal, true, false, 0.17f, 0.25f, false).Pos, Is.EqualTo(1f), "pos clamps to the edge");
            // Crash / ride: bell under .22, edge over .85, bow between.
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Cymbal, false, false, 0.21f, 0.21f * 0.21f, false), 0.21f, DrumZone.Bell);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Cymbal, false, false, 0.21f, 0.21f * 0.23f, false), 0.23f, DrumZone.Bow);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Cymbal, false, false, 0.22f, 0.22f * 0.84f, false), 0.84f, DrumZone.Bow);
            AssertZone(DrumMath.StrikeAt(DrumPieceType.Cymbal, false, false, 0.22f, 0.22f * 0.86f, false), 0.86f, DrumZone.Edge);
            Assert.That(DrumStrike.ZoneName(DrumZone.Bow), Is.EqualTo("bow"));
        }

        static void AssertZone(DrumStrike s, float pos, DrumZone zone)
        {
            Assert.That(s.Pos, Is.EqualTo(pos).Within(1e-5f));
            Assert.That(s.Zone, Is.EqualTo(zone));
        }

        [Test]
        public void CrossingRadiusIsWhereThePathCrossedTheHead()
        {
            // prev (0.05, .03, 0) -> tip (0.07, -.01, 0): t = .75, x = .065.
            Assert.That(DrumMath.CrossingRadius(new Vector3(0.05f, 0.03f, 0f), new Vector3(0.07f, -0.01f, 0f)), Is.EqualTo(0.065f).Within(1e-6f));
            Assert.That(DrumMath.CrossingRadius(new Vector3(0f, 0.02f, 0.1f), new Vector3(0f, -0.02f, 0.2f)), Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(DrumMath.StrikeReach(DrumPieceType.Drum, false, 0.18f), Is.EqualTo(0.21f).Within(1e-6f));
            Assert.That(DrumMath.StrikeReach(DrumPieceType.Drum, true, 0.1f), Is.EqualTo(0.1f));
            Assert.That(DrumMath.StrikeReach(DrumPieceType.Cymbal, false, 0.21f), Is.EqualTo(0.21f));
            Assert.That(DrumMath.SampleSeconds(0f, 0.0139f), Is.EqualTo(0.0139f), "no learned interval yet: dt");
            Assert.That(DrumMath.SampleSeconds(0.011f, 0.0139f), Is.EqualTo(0.011f));
            Assert.That(DrumMath.SampleSeconds(0f, 0f), Is.EqualTo(0.001f), "never below 1 ms");
        }

        [Test]
        public void StickGripDroopsTwelvePointSixDegreesTowardThePalm()
        {
            // A right hand held level, fingers along +z, back of the hand up.
            Vector3 wrist = new Vector3(0.3f, 1f, 0.2f);
            Quaternion rot = Quaternion.identity;
            Vector3 thumb = wrist + new Vector3(-0.02f, -0.03f, 0.10f), index = wrist + new Vector3(0.0f, -0.03f, 0.10f);
            DrumMath.StickGrip(wrist, rot, thumb, index, out Vector3 grip, out Vector3 axis);
            Assert.That(Vector3.Distance(grip, (thumb + index) * 0.5f), Is.LessThan(1e-6f), "the grip is the thumb-index midpoint");
            Vector3 fwd = (grip - wrist).normalized;
            Assert.That(Vector3.Angle(fwd, axis), Is.EqualTo(0.22f * Mathf.Rad2Deg).Within(1e-3f), "TILT .22 rad below the hand's forward line");
            Vector3 up = rot * Vector3.up;
            Assert.That(Vector3.Dot(axis, up), Is.LessThan(Vector3.Dot(fwd, up)), "drooping toward the palm, away from the back of the hand");
            Assert.That(Vector3.Dot(Vector3.Cross(fwd, up).normalized, axis), Is.EqualTo(0f).Within(1e-5f), "the droop stays in the forward/up plane");

            DrumMath.StickPose(grip, axis, out Vector3 butt, out Quaternion srot, out Vector3 tip, out Vector3 shoulder);
            Assert.That(Vector3.Distance(butt, grip - axis * 0.13f), Is.LessThan(1e-6f), "GRIP_FROM_BUTT .13");
            Assert.That(Vector3.Distance(tip, butt + axis * 0.4f), Is.LessThan(1e-6f), "STICK_LEN .4");
            Assert.That(Vector3.Distance(shoulder, butt + axis * 0.32f), Is.LessThan(1e-6f), "SHOULDER_FROM_BUTT .32");
            Assert.That(Vector3.Angle(srot * Vector3.up, axis), Is.LessThan(1e-3f), "stick-local +y along the axis");
        }

        [Test]
        public void SettleEaseAndSegmentDistance()
        {
            Assert.That(DrumMath.SettleEase(0f), Is.EqualTo(0f));
            Assert.That(DrumMath.SettleEase(0.5f), Is.EqualTo(0.875f).Within(1e-6f));
            Assert.That(DrumMath.SettleEase(1f), Is.EqualTo(1f));
            Vector3 a = Vector3.zero, b = new Vector3(0f, 0.4f, 0f);
            Assert.That(DrumMath.SegmentDistance(a, b, new Vector3(0.1f, 0.2f, 0f)), Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(DrumMath.SegmentDistance(a, b, new Vector3(0f, 0.5f, 0f)), Is.EqualTo(0.1f).Within(1e-6f), "clamped to the tip");
            Assert.That(DrumMath.SegmentDistance(a, b, new Vector3(0f, -0.2f, 0.0f)), Is.EqualTo(0.2f).Within(1e-6f), "clamped to the butt");
        }

        [Test]
        public void HatStandZoneAndPieceZones()
        {
            Vector3 hat = DrumSpec.Pieces[DrumSpec.HatId].Pos;   // (-.42, .80, .36)
            Assert.IsTrue(DrumMath.InHatStandZone(new Vector3(-0.42f, 0.5f, 0.36f), hat));
            Assert.IsTrue(DrumMath.InHatStandZone(new Vector3(-0.42f + 0.079f, 0.13f, 0.36f), hat));
            Assert.IsFalse(DrumMath.InHatStandZone(new Vector3(-0.42f + 0.081f, 0.5f, 0.36f), hat), "8 cm from the pole");
            Assert.IsFalse(DrumMath.InHatStandZone(new Vector3(-0.42f, 0.11f, 0.36f), hat), "above .12");
            Assert.IsFalse(DrumMath.InHatStandZone(new Vector3(-0.42f, 0.73f, 0.36f), hat), "below hat.y - .08 = .72");
            Assert.IsTrue(DrumMath.OverPiece(new Vector3(0.21f, 0.25f, 0f), 0.18f), "r + .04, up to .25 above");
            Assert.IsFalse(DrumMath.OverPiece(new Vector3(0.23f, 0f, 0f), 0.18f));
            Assert.IsFalse(DrumMath.OverPiece(new Vector3(0f, -0.121f, 0f), 0.18f), ".12 below");
            Assert.IsFalse(DrumMath.OverPiece(new Vector3(0f, 0.251f, 0f), 0.18f));
        }

        [Test]
        public void TheCymbalMapIsCoresProcTexPixelWithSmoothnessInAlpha()
        {
            // Core owns the texture generators (ProcTexTests fingerprint them); Drums only packs smoothness = 1 - roughness into
            // the alpha its Drums/Cymbal template reads. Canvas row y lives in Texture2D row 255 - y.
            Texture2D t = DrumTextures.Cymbal();
            try
            {
                Assert.That(t.width, Is.EqualTo(256));
                Assert.That(t.height, Is.EqualTo(256));
                Assert.That(t.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
                foreach (var (x, y) in new[] { (0, 0), (17, 3), (128, 200), (255, 255) })
                {
                    ProcTex.CymbalPixel(x, y, out Color32 c, out byte rough);
                    Color32 got = t.GetPixel(x, 255 - y);
                    Assert.That(got.r, Is.EqualTo(c.r), $"r at {x},{y}");
                    Assert.That(got.g, Is.EqualTo(c.g), $"g at {x},{y}");
                    Assert.That(got.b, Is.EqualTo(c.b), $"b at {x},{y}");
                    Assert.That(got.a, Is.EqualTo(255 - rough), $"smoothness at {x},{y}");
                }
            }
            finally { Object.DestroyImmediate(t); }
            Assert.That(DrumTextures.CymbalName, Is.EqualTo("Drums/cymbal"), "module-prefixed bake name");
            var specs = new System.Collections.Generic.List<TextureBakeSpec>(new DrumsTextureProvider().Textures);
            Assert.That(specs.Count, Is.EqualTo(1), "the head and the soft dot are Core's shared textures");
            Assert.That(specs[0].Name, Is.EqualTo(DrumTextures.CymbalName));
            Assert.IsTrue(specs[0].HasAlpha && specs[0].Mipmaps && specs[0].Repeat && !specs[0].Linear && !specs[0].NeedsGpu);
        }
    }
}
