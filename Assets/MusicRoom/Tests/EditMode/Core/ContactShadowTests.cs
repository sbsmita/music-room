// E3 of 10-upgrade/experience.md section 5 (soft contact shadows) and the shared plumbing of section 2.2 (ContactVisuals):
// the darkness and quad tables of the experience lab's experience.vectors.json (contactShadow; r 0.0076, A 0.55; tolerance
// 1e-5), the per-frame contact logic on scripted hand frames against plane surfaces (nearest hit, clamp vs shadow-only,
// the release, MR), the hover-glow dimming, the material templates (ContactShadow, HandSkin, HandPassthrough: queues and
// blend states), and Is.Not.AllocatingGCMemory around the update with 10 tracked fingers.
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Core
{
    public class ContactShadowTests
    {
        const float Dt = 1f / 90f;

        /// A horizontal plane surface (y = Y) over a square footprint.
        sealed class Plane : IContactSurface
        {
            public float Y, Half = 0.5f;
            public Vector3 Centre;
            public ContactUse Use = ContactUse.Both;
            public bool Present = true;
            public int Queries;
            public bool QueryContact(Vector3 world, float above, float below, out ContactHit hit)
            {
                Queries++;
                hit = default;
                if (!Present || Mathf.Abs(world.x - Centre.x) > Half || Mathf.Abs(world.z - Centre.z) > Half) return false;
                float h = world.y - Y;
                if (h > above || h < -below) return false;
                hit = new ContactHit { Point = new Vector3(world.x, Y, world.z), Normal = Vector3.up, Tangent = Vector3.right, Height = h, Use = Use };
                return true;
            }
        }

        /// A right hand, palm down, fingers along +z, every fingertip joint at height <paramref name="tipY"/>.
        static HandFrame Hand(HandSide side, Vector3 at, float tipY)
        {
            var f = new HandFrame(side);
            var pose = HandPose.Open(side, Vector3.zero, Quaternion.LookRotation(Vector3.forward, Vector3.up));
            for (int i = 0; i < HandFrame.JointCount; i++)
            {
                f.position[i] = at + pose.Position[i];
                f.rotation[i] = pose.Rotation[i];
                f.radius[i] = HandRestPose.Radius[i];
            }
            for (int k = 0; k < 5; k++)
            {
                int j = HandFrame.TipJoint[k];
                Vector3 d = new Vector3(0f, tipY - f.position[j].y, 0f);
                f.position[j] += d;            // the tip and its distal joint dropped to the height
                f.position[j - 1] += d * 0.5f;
            }
            f.isTracked = true;
            f.isHighConfidence = true;
            return f;
        }

        // ---------------- the shadow model ----------------

        [Test]
        public void TheSphereShadowMatchesTheVectors()
        {
            float[,] rows =
            {
                { 0f, 0.0076f, 0.55f, 0.019f }, { 0f, 0.01f, 0.31768f, 0.025f }, { 0.005f, 0.01f, 0.227313f, 0.025f },
                { 0f, 0.02f, 0.07942f, 0.05f }, { 0.01f, 0.02f, 0.056828f, 0.05f }, { 0f, 0.04f, 0.019855f, 0.08f }, { 0f, 0.06f, 0.008824f, 0.08f },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                float rho = rows[i, 0], h = rows[i, 1];
                Assert.That(ContactShadowPool.Darkness(rho, h, 0.0076f, ContactVisuals.AmbientVr), Is.EqualTo(rows[i, 2]).Within(1e-5f), $"darkness rho {rho} h {h}");
                Assert.That(ContactShadowPool.QuadHalf(h, 0.0076f), Is.EqualTo(rows[i, 3]).Within(1e-5f), $"quad half h {h}");
            }
            Assert.That(ContactShadowPool.HalfWidth(0.02f), Is.EqualTo(0.015328f).Within(1e-5f), "0.766 h");
            Assert.That(ContactShadowPool.Darkness(0f, -0.01f, 0.0076f, ContactVisuals.AmbientVr), Is.EqualTo(ContactShadowPool.MaxDark), "a centre at or under the plane: the floor 0.8 r, capped at 0.6");
            Assert.That(ContactVisuals.AmbientMr, Is.EqualTo(0.35f));
            Assert.That(ContactShadowPool.Capacity, Is.EqualTo(32));
            Assert.That(ContactShadowPool.HMax, Is.EqualTo(0.06f));
        }

        [Test]
        public void ThePoolSkipsHighBlobsAndClipsPressedKeys()
        {
            var pool = new ContactShadowPool(null, null, true);
            pool.Begin();
            Assert.IsFalse(pool.Add(Vector3.zero, Vector3.up, Vector3.right, 0f, 0.061f, 0.0076f, 0.55f), "over 6 cm: skipped");
            Assert.IsTrue(pool.Add(Vector3.zero, Vector3.up, Vector3.right, 0.01105f, 0.01f, 0.0076f, 0.55f));
            Assert.IsTrue(pool.Add(Vector3.zero, Vector3.up, Vector3.zero, 0f, 0.002f, 0.0076f, 0.55f));
            Assert.That(pool.Get(0).ClipHalf, Is.EqualTo(0.01105f), "a pressed white key: half its visible width");
            Assert.That(pool.Get(1).ClipHalf, Is.EqualTo(ContactShadowPool.NoClip), "a resting key: no clip");
            Assert.That(pool.Get(1).HEff, Is.EqualTo(0.8f * 0.0076f).Within(1e-7f), "h_eff floor 0.8 r");
            for (int i = 0; i < 40; i++) pool.Add(Vector3.zero, Vector3.up, Vector3.zero, 0f, 0.01f, 0.0076f, 0.55f);
            Assert.That(pool.Count, Is.EqualTo(32));
            Assert.That(pool.Dropped, Is.EqualTo(10));
            pool.End(Vector3.zero);
            Assert.That(pool.Drawn, Is.EqualTo(32));
            pool.Dispose();
        }

        // ---------------- the per-frame contact logic ----------------

        [Test]
        public void AFingerInAKeyIsLiftedAndShadowedAndTheNearestSurfaceWins()
        {
            var low = new Plane { Y = 0f };
            var high = new Plane { Y = 0.012f, Half = 0.01f, Centre = new Vector3(0.5f, 0f, 0.5f) };   // elsewhere
            var cv = new ContactVisuals(new ContactShadowPool(null, null, true), null);
            cv.SetSurfaces(new IContactSurface[] { high, low });
            // Every fingertip joint 4 mm above the plane: the 7.6 mm pad is 3.6 mm inside it.
            HandFrame right = Hand(HandSide.Right, new Vector3(0f, 0.05f, -0.1f), 0.004f);
            cv.Step(Dt, null, right, 0, Vector3.zero);
            float rc = right.radius[(int)HandJoint.IndexTip];
            Assert.That(cv.Lift(HandSide.Right, Finger.Index), Is.EqualTo(rc - 0.004f).Within(1e-6f), "lift = rc - h");
            Assert.That(cv.LiftNormal(HandSide.Right, Finger.Index), Is.EqualTo(Vector3.up));
            Assert.That(cv.LiftedFingers, Is.EqualTo(5));
            Assert.That(cv.ShadowCount, Is.EqualTo(10), "tip + distal joint per finger");
            var tipBlob = cv.Shadows.Get(2);   // index finger tip (fingers in Finger order, two blobs each)
            Assert.That(tipBlob.Centre.y, Is.EqualTo(0f).Within(1e-6f), "on the plane");
            Assert.That(tipBlob.HEff, Is.EqualTo(rc).Within(2e-4f), "cast from the DRAWN (lifted) tip: it rests rc above the plane");
            Assert.That(tipBlob.Ambient, Is.EqualTo(ContactVisuals.AmbientVr));

            // A shadow-only surface (a cymbal) nearer than the plane: no lift, still shadows.
            var cymbal = new Plane { Y = 0.003f, Use = ContactUse.Shadow };
            cv.SetSurfaces(new IContactSurface[] { low, cymbal });
            for (int i = 0; i < 30; i++) cv.Step(Dt, null, right, 0, Vector3.zero);
            Assert.That(cv.Lift(HandSide.Right, Finger.Index), Is.EqualTo(0f), "a hand on a cymbal is never lifted (choke)");
            Assert.That(cv.ShadowCount, Is.EqualTo(10));

            // Nothing under the hand: no lift, no shadow.
            cv.SetSurfaces(new IContactSurface[] { high });
            cv.Step(Dt, null, right, 0, Vector3.zero);
            Assert.That(cv.ShadowCount, Is.EqualTo(0));
            cv.Dispose();
        }

        [Test]
        public void TheClampReleasesSmoothlyAndIsOffWithTheRealHandsOrWhenDisabled()
        {
            var plane = new Plane { Y = 0f };
            var cv = new ContactVisuals(new ContactShadowPool(null, null, true), null);
            cv.SetSurfaces(new IContactSurface[] { plane });
            HandFrame left = Hand(HandSide.Left, new Vector3(0f, 0.05f, -0.1f), 0.004f);
            cv.Step(Dt, left, null, 0, Vector3.zero);
            float l0 = cv.Lift(HandSide.Left, Finger.Middle);
            Assert.That(l0, Is.GreaterThan(0.003f));
            plane.Present = false;                                   // slid off the key
            cv.Step(Dt, left, null, 0, Vector3.zero);
            Assert.That(cv.Lift(HandSide.Left, Finger.Middle), Is.EqualTo(l0 * Mathf.Exp(-Dt / ContactMath.ReleaseTau)).Within(1e-6f), "25 ms release");
            plane.Present = true;
            cv.SetMixedReality(true);
            Assert.IsTrue(cv.ClampActive, "MR with the drawn hands (the default): the fingers still stop on the keys");
            Assert.IsFalse(cv.CutoutActive);
            cv.RealHandsInMr = true;
            Assert.IsTrue(cv.CutoutActive);
            Assert.IsFalse(cv.ClampActive);
            cv.Step(Dt, left, null, 0, Vector3.zero);
            Assert.That(cv.Lift(HandSide.Left, Finger.Middle), Is.EqualTo(0f), "real hands: the cut-out must match the real hand");
            Assert.That(cv.ShadowCount, Is.EqualTo(10), "shadows stay in MR");
            Assert.That(cv.Shadows.Get(0).Ambient, Is.EqualTo(ContactVisuals.AmbientMr), "A = 0.35 in MR");
            cv.SetMixedReality(false);
            cv.ClampEnabled = false;
            cv.Step(Dt, left, null, 0, Vector3.zero);
            Assert.That(cv.Lift(HandSide.Left, Finger.Middle), Is.EqualTo(0f), "ClampEnabled = false turns it off");
            cv.ClampEnabled = true;
            cv.ShadowsEnabled = false;
            cv.Step(Dt, left, null, 0, Vector3.zero);
            Assert.That(cv.ShadowCount, Is.EqualTo(0));
            Assert.That(cv.Lift(HandSide.Left, Finger.Middle), Is.GreaterThan(0f));
            cv.Dispose();
        }

        [Test]
        public void StickTipsCastShadowsAndAShadowDimsTheHoverGlowUnderIt()
        {
            var plane = new Plane { Y = 0f };
            var glow = new HoverGlowPool(null, null, true);
            var cv = new ContactVisuals(new ContactShadowPool(null, null, true), glow);
            cv.SetSurfaces(new IContactSurface[] { plane });
            glow.Add(HoverGlowSpec.At(new Vector3(0f, 0.002f, 0f), Vector3.up, 0.09f, 0.09f, 0.5f));
            glow.Add(HoverGlowSpec.At(new Vector3(0.4f, 0.002f, 0.4f), Vector3.up, 0.09f, 0.09f, 0.5f));
            System.Span<Vector4> sticks = stackalloc Vector4[] { new Vector4(0f, 0.02f, 0f, 0.0062f), new Vector4(0f, 0.2f, 0f, 0.0062f) };
            cv.Step(Dt, null, null, sticks, Vector3.zero);
            Assert.That(cv.ShadowCount, Is.EqualTo(1), "the low stick tip only (the other is 20 cm up)");
            Assert.That(cv.Shadows.Get(0).Radius, Is.EqualTo(0.0062f));
            Assert.That(glow.DimOf(0), Is.EqualTo(ContactVisuals.HoverGlowGainWithShadow), "the glow under the shadow");
            Assert.That(glow.DimOf(1), Is.EqualTo(1f), "a glow far from every shadow");
            Assert.That(glow.Spec(0).Gain, Is.EqualTo(1f), "the instrument's spec is unchanged");
            Assert.That(cv.DimmedGlows, Is.EqualTo(1));
            cv.Dispose();
        }

        [Test]
        public void TheUpdateWithTenTrackedFingersAllocatesNothing()
        {
            var plane = new Plane { Y = 0f };
            var glow = new HoverGlowPool(null, null, true);
            var cv = new ContactVisuals(new ContactShadowPool(null, null, true), glow);
            cv.SetSurfaces(new IContactSurface[] { plane, new Plane { Y = 0.01f, Use = ContactUse.Shadow } });
            HandFrame left = Hand(HandSide.Left, new Vector3(-0.2f, 0.05f, -0.1f), 0.003f);
            HandFrame right = Hand(HandSide.Right, new Vector3(0.2f, 0.05f, -0.1f), 0.02f);
            void Work()
            {
                for (int i = 0; i < 20; i++)
                {
                    glow.Add(HoverGlowSpec.At(Vector3.zero, Vector3.up, 0.07f, 0.024f, 0.5f));
                    cv.Step(Dt, left, right, 0, Vector3.zero);
                    glow.Update(0f);
                }
            }
            Work();
            Assert.That(cv.ShadowCount, Is.EqualTo(20), "10 fingers, two blobs each");
            Assert.That(() => { var a = new float[1024]; a[0] = 1f; }, Is.AllocatingGCMemory(), "calibration");
            Assert.That(Work, Is.Not.AllocatingGCMemory());
            cv.Dispose();
        }

        // ---------------- templates ----------------

        static Material Template(string name)
        {
            var m = Resources.Load<Material>(MaterialLibrary.ResourcesPrefix + name);
            Assert.That(m, Is.Not.Null, name + " template (Tools/unity.sh render-setup writes it)");
            return m;
        }

        [Test]
        public void CoresProviderWritesTheThreeTemplates()
        {
            var provider = new CoreMaterialTemplates();
            Assert.That(provider.ResourcesFolder, Is.EqualTo("Assets/MusicRoom"));
            var names = new List<string>();
            foreach (var s in provider.Templates) names.Add(s.Name);
            Assert.That(names, Is.EquivalentTo(new[] { "ContactShadow", "HandSkin", "HandPassthrough" }));

            Material shadow = Template(CoreMaterialTemplates.ContactShadow);
            Assert.That(shadow.shader.name, Is.EqualTo("MusicRoom/ContactShadow"));
            Assert.That(shadow.renderQueue, Is.EqualTo(2991), "after the opaques and the key labels (2990), before the menu face, glass and glows");
            Assert.That(shadow.GetFloat("_MaxDark"), Is.EqualTo(0.6f));

            Material skin = Template(CoreMaterialTemplates.HandSkin);
            Assert.That(skin.shader.name, Is.EqualTo("MusicRoom/HandSkin"));
            Assert.That(skin.GetFloat("_Alpha"), Is.EqualTo(1f));
            Assert.That(skin.GetFloat("_MaxOpacity"), Is.EqualTo(0.92f).Within(1e-6f));
            Assert.That(skin.GetFloat("_Wrap"), Is.EqualTo(0.5f));

            Material cut = Template(CoreMaterialTemplates.HandPassthrough);
            Assert.That(cut.shader.name, Is.EqualTo("MusicRoom/HandPassthrough"));
            Assert.That(cut.renderQueue, Is.EqualTo(3999), "the cut-out draws last");
            Assert.That(cut.GetFloat("_Dilate"), Is.EqualTo(0.0015f).Within(1e-7f));
            Assert.That(cut.passCount, Is.EqualTo(2), "depth, then the hole");
        }

        [Test]
        public void TheShadersCarryTheSpecifiedRenderState()
        {
            string shadow = File.ReadAllText("Assets/MusicRoom/Core/Render/ContactShadow.shader");
            StringAssert.Contains("Blend DstColor Zero", shadow, "multiply blend");
            StringAssert.Contains("ZWrite Off", shadow);
            StringAssert.Contains("ZTest LEqual", shadow);
            StringAssert.Contains("Offset -1, -1", shadow);
            StringAssert.Contains("\"Queue\" = \"Transparent-9\"", shadow);
            StringAssert.Contains("if (abs(i.rho.x - i.clipCentre) > clipHalf) discard;", shadow, "clipped about the key's centre line");
            StringAssert.Contains("float q = h * h + dot(i.rho, i.rho);", shadow);
            StringAssert.Contains("A * r * r * h / (q * sqrt(q))", shadow, "(h^2 + rho^2)^1.5");

            string cut = File.ReadAllText("Assets/MusicRoom/Hands/HandPassthrough.shader");
            StringAssert.Contains("\"Queue\" = \"Overlay-1\"", cut, "3999");
            StringAssert.Contains("Blend Zero OneMinusSrcAlpha, Zero OneMinusSrcAlpha", cut);
            StringAssert.Contains("ColorMask 0", cut);
            StringAssert.Contains("smoothstep(_EdgeFrom, _EdgeTo, saturate(dot(n, v)))", cut);

            string skin = File.ReadAllText("Assets/MusicRoom/Hands/HandSkin.shader");
            StringAssert.Contains("Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl", skin);
            StringAssert.Contains("#pragma multi_compile _ _ADDITIONAL_LIGHTS", skin);
            StringAssert.Contains("GetAdditionalLightsCount()", skin);
            StringAssert.Contains("SampleSH(n)", skin);

            foreach (string src in new[] { shadow, cut, skin })
            {
                StringAssert.Contains("CBUFFER_START(UnityPerMaterial)", src, "SRP Batcher shaped");
                StringAssert.Contains("UNITY_VERTEX_OUTPUT_STEREO", src, "single-pass instanced stereo");
                StringAssert.Contains("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO", src);
                StringAssert.Contains("#pragma multi_compile_instancing", src);
            }
        }
    }
}
