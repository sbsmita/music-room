// The platform's ghost-hand demos (platform.md 8.3; Core DemoKind's comment): every kind beyond the tour's six as a
// keyframed preset at a world anchor, plus Custom keys, checked like SunnyDemoTests at sample times in the anchor frame
// (y along the anchor's up, z toward the player, x to their right), then the world conversion into Sunny's frame (the
// contact point lands where the preset says; fingers point down for the fingertip kinds, the palm faces the surface for
// the palm kinds), and Sunny running a tip demo at an anchor, then its ThenDemo, ending when Sunny leaves.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public class SunnyAnchoredDemoTests
    {
        const float R = 0.12f;

        static IEnumerable<(float t, SunnyDemo.AnchoredPose p)> Run(DemoKind k, float step = 0.005f, DemoMotion m = null)
        {
            for (float t = 0f; t < GuideTiming.DemoLoop; t += step) yield return (t, SunnyDemo.Sample(k, t, R, m));
        }

        static float MinY(DemoKind k) { float y = float.MaxValue; foreach (var (_, p) in Run(k)) y = Mathf.Min(y, p.Offset.y); return y; }
        static float MaxY(DemoKind k) { float y = float.MinValue; foreach (var (_, p) in Run(k)) y = Mathf.Max(y, p.Offset.y); return y; }

        // Contacts: how many times the contact point comes down to (near) the anchor plane.
        static int Touches(DemoKind k, float below = 0.002f)
        {
            int n = 0;
            bool down = false;
            foreach (var (_, p) in Run(k))
            {
                bool d = p.Offset.y < below;
                if (d && !down) n++;
                down = d;
            }
            return n;
        }

        static readonly DemoKind[] Platform =
        {
            DemoKind.Tap, DemoKind.Slap, DemoKind.MalletStrike, DemoKind.Approach, DemoKind.Circle, DemoKind.Strike, DemoKind.Hold,
            DemoKind.PalmRest, DemoKind.Pinch, DemoKind.Point, DemoKind.OpenTone, DemoKind.PalmBass, DemoKind.CuppedSlap,
            DemoKind.HeelToe, DemoKind.Roll, DemoKind.MalletHold, DemoKind.OpenHand, DemoKind.Hover, DemoKind.Lift, DemoKind.Lower,
            DemoKind.Rock, DemoKind.Dip, DemoKind.Curl, DemoKind.FingerCircle,
            DemoKind.Sweep, DemoKind.PinchPull, DemoKind.Twist, DemoKind.Drag,   // first five's (SunnyDemoWorld poses them in the tour)
        };

        [Test]
        public void EveryKindBeyondTheToursIsAPresetAndOnlyTheToursArePosedAtSunny()
        {
            foreach (DemoKind k in (DemoKind[])Enum.GetValues(typeof(DemoKind)))
            {
                bool tour = k == DemoKind.None || k == DemoKind.Palm || k == DemoKind.Poke || k == DemoKind.Swipe || k == DemoKind.Nod || k == DemoKind.Press;
                if (tour || k == DemoKind.Custom) continue;
                Assert.That(Platform, Does.Contain(k), k + " has a preset");
            }
            Assert.That(Platform.Length, Is.EqualTo(28));
            Assert.IsTrue(SunnyDemo.IsTourKind(DemoKind.Poke));
            Assert.IsFalse(SunnyDemo.IsTourKind(DemoKind.Tap));
        }

        [Test]
        public void TapsStrikesAndMalletsComeDownOnTheAnchor()
        {
            Assert.That(Touches(DemoKind.Tap), Is.EqualTo(2), "two light taps");
            Assert.That(MaxY(DemoKind.Tap), Is.EqualTo(0.02f).Within(1e-4f), "2 cm");
            Assert.That(MinY(DemoKind.Tap), Is.LessThan(0.001f));
            Assert.That(Touches(DemoKind.Strike), Is.EqualTo(1), "one stroke");
            Assert.That(MaxY(DemoKind.Strike), Is.EqualTo(0.04f).Within(1e-4f), "4 cm");
            Assert.That(Touches(DemoKind.MalletStrike), Is.EqualTo(2), "two strokes");
            Assert.That(MaxY(DemoKind.MalletStrike), Is.EqualTo(0.06f).Within(1e-4f), "6 cm");
            Assert.That(SunnyDemo.Sample(DemoKind.MalletStrike, 1f, R).Hand, Is.EqualTo(GhostHand.Mallet));
            Assert.That(SunnyDemo.Sample(DemoKind.Tap, 1f, R).Hand, Is.EqualTo(GhostHand.Cupped), "fingertips together");
            // MalletHold strikes and stays down 0.6 s.
            float downFor = 0f;
            foreach (var (_, p) in Run(DemoKind.MalletHold, 0.01f)) if (p.Offset.y < 0.001f) downFor += 0.01f;
            Assert.That(downFor, Is.EqualTo(0.6f).Within(0.06f), "(the eased edges add a few hundredths)");
            // Roll: quick alternating taps, both sides.
            bool left = false, right = false;
            foreach (var (_, p) in Run(DemoKind.Roll)) { left |= p.Offset.x < -0.02f && p.Offset.y < 0.002f; right |= p.Offset.x > 0.02f && p.Offset.y < 0.002f; }
            Assert.IsTrue(left && right, "two hands alternating");
            Assert.That(Touches(DemoKind.Roll), Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void PalmsSlapRestAndHoverWhereTheirKindsSay()
        {
            SunnyDemo.AnchoredPose first = SunnyDemo.Sample(DemoKind.Slap, 0.55f, R), second = SunnyDemo.Sample(DemoKind.Slap, 1.65f, R);
            Assert.That(first.Hold, Is.EqualTo(SunnyDemo.HandHold.PalmDown));
            Assert.That(first.Offset.y, Is.LessThan(0.001f)); Assert.That(first.Offset.z, Is.EqualTo(0.8f * R).Within(1e-4f), "the edge first");
            Assert.That(second.Offset.y, Is.LessThan(0.001f)); Assert.That(second.Offset.z, Is.EqualTo(0f).Within(1e-4f), "then the centre");
            Assert.That(MaxY(DemoKind.Slap), Is.EqualTo(0.05f).Within(1e-4f), "a 5 cm slap");
            Assert.That(SunnyDemo.Sample(DemoKind.CuppedSlap, 0.59f, R).Offset.z, Is.EqualTo(0.8f * R).Within(1e-4f), "cupped, at the edge");
            Assert.That(SunnyDemo.Sample(DemoKind.CuppedSlap, 0.59f, R).Hand, Is.EqualTo(GhostHand.Cupped));
            Assert.That(SunnyDemo.Sample(DemoKind.OpenTone, 0.6f, R).Offset.z, Is.EqualTo(0.7f * R).Within(1e-4f), "flat fingers at the edge");
            Assert.That(Touches(DemoKind.OpenTone), Is.EqualTo(2));
            Assert.That(MaxY(DemoKind.PalmBass), Is.EqualTo(0.08f).Within(1e-4f), "an 8 cm drop onto the centre");
            Assert.That(SunnyDemo.Sample(DemoKind.PalmBass, 0.9f, R).Offset.y, Is.LessThan(1e-4f));
            float held = 0f;
            foreach (var (_, p) in Run(DemoKind.Hold, 0.01f)) if (p.Offset.y < 0.0005f) held += 0.01f;
            Assert.That(held, Is.EqualTo(1.0f).Within(0.08f), "held 1 s (the eased edges add a few hundredths)");
            Assert.That(SunnyDemo.Sample(DemoKind.PalmRest, 1.5f, R).Offset.y, Is.LessThan(1e-4f), "laid on it");
            foreach (var (_, p) in Run(DemoKind.Hover)) Assert.That(p.Offset.y, Is.EqualTo(0.03f).Within(1e-6f), "3 cm above, still");
            Assert.That(SunnyDemo.Sample(DemoKind.Lift, 0.1f, R).Offset.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(SunnyDemo.Sample(DemoKind.Lift, 2.0f, R).Offset.y, Is.EqualTo(0.12f).Within(1e-4f), "rises 12 cm");
            Assert.That(SunnyDemo.Sample(DemoKind.Lower, 0.1f, R).Offset.y, Is.EqualTo(0.12f).Within(1e-4f));
            Assert.That(SunnyDemo.Sample(DemoKind.Lower, 2.0f, R).Offset.y, Is.EqualTo(0f).Within(1e-4f), "sinks 12 cm");
            Assert.That(MaxY(DemoKind.Dip) - MinY(DemoKind.Dip), Is.EqualTo(0.04f).Within(1e-4f), "a 4 cm dip");
        }

        [Test]
        public void RocksCurlsCirclesPinchesAndPoints()
        {
            float heel = float.MinValue, toe = float.MaxValue, rock = 0f;
            foreach (var (_, p) in Run(DemoKind.HeelToe)) { heel = Mathf.Max(heel, p.RollDeg); toe = Mathf.Min(toe, p.RollDeg); }
            Assert.That(heel, Is.EqualTo(15f).Within(0.1f)); Assert.That(toe, Is.EqualTo(-15f).Within(0.1f), "heel, then fingertips");
            foreach (var (_, p) in Run(DemoKind.Rock)) rock = Mathf.Max(rock, Mathf.Abs(p.RollDeg));
            Assert.That(rock, Is.EqualTo(15f).Within(0.1f), "rocks +-15 deg");
            float curl = 0f;
            foreach (var (_, p) in Run(DemoKind.Curl)) curl = Mathf.Max(curl, p.Curl);
            Assert.That(curl, Is.EqualTo(0.4f).Within(1e-3f), "curls a little");
            Assert.That(SunnyDemo.Sample(DemoKind.OpenHand, 0.2f, R).Curl, Is.EqualTo(1f), "a closed hand");
            Assert.That(SunnyDemo.Sample(DemoKind.OpenHand, 2.0f, R).Curl, Is.EqualTo(0f), "opens, letting the tool go");
            Assert.That(SunnyDemo.Sample(DemoKind.Pinch, 1.0f, R).PinchBlend, Is.EqualTo(1f), "pinched from 0.6 to 1.6 s");
            Assert.That(SunnyDemo.Sample(DemoKind.Pinch, 0.1f, R).PinchBlend, Is.EqualTo(0f));
            // Circle: on the rim, all the way round; FingerCircle 1 cm below the lip, one turn in 1.4 s.
            float minA = 10f, maxA = -10f;
            foreach (var (t, p) in Run(DemoKind.Circle))
            {
                Assert.That(new Vector2(p.Offset.x, p.Offset.z).magnitude, Is.EqualTo(R).Within(1e-4f), "on the rim");
                float a = Mathf.Atan2(p.Offset.x, p.Offset.z);
                minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
            }
            Assert.That(maxA - minA, Is.GreaterThan(2f * Mathf.PI - 0.1f), "a whole turn");
            foreach (var (_, p) in Run(DemoKind.FingerCircle)) Assert.That(p.Offset.y, Is.EqualTo(-0.01f).Within(1e-6f));
            Vector3 a0 = SunnyDemo.Sample(DemoKind.FingerCircle, 0.2f, R).Offset, a1 = SunnyDemo.Sample(DemoKind.FingerCircle, 1.6f, R).Offset;
            Assert.That(Vector3.Distance(a0, a1), Is.LessThan(1e-4f), "one turn in 1.4 s");
            // Point: from about 30 cm, toward the player and above.
            SunnyDemo.AnchoredPose point = SunnyDemo.Sample(DemoKind.Point, 0.2f, R);
            Assert.That(point.Offset.magnitude, Is.EqualTo(0.30f).Within(0.002f));
            Assert.That(point.Offset.z, Is.GreaterThan(0.2f), "on the player's side");
            Assert.That(point.Hold, Is.EqualTo(SunnyDemo.HandHold.Pointing));
            // Approach: 25 cm toward the anchor and back.
            float far = 0f, near = 1f;
            foreach (var (_, p) in Run(DemoKind.Approach)) { far = Mathf.Max(far, p.Offset.x); near = Mathf.Min(near, p.Offset.x); }
            Assert.That(far - near, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(SunnyDemo.Sample(DemoKind.Approach, 2.9f, R).Offset.x, Is.EqualTo(far).Within(1e-4f), "and back");
        }

        [Test]
        public void CustomKeysAreFollowedWithTheirHandShapes()
        {
            var m = new DemoMotion
            {
                Loop = 2f,
                Keys = new[] { new DemoKey(0.2f, new Vector3(0f, 0.05f, 0f), GhostHand.Point), new DemoKey(1.0f, new Vector3(0.04f, 0f, 0f), GhostHand.Flat), new DemoKey(1.6f, new Vector3(0f, 0.05f, 0f), GhostHand.Pinch) },
            };
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 0.1f, R, m).Offset, Is.EqualTo(new Vector3(0f, 0.05f, 0f)), "before the first key: at it");
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 1.0f, R, m).Offset.x, Is.EqualTo(0.04f).Within(1e-5f), "at a key: its offset");
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 0.6f, R, m).Offset.x, Is.EqualTo(0.02f).Within(1e-5f), "eased between keys (halfway)");
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 0.5f, R, m).Hand, Is.EqualTo(GhostHand.Point));
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 1.2f, R, m).Hand, Is.EqualTo(GhostHand.Flat), "each key's shape");
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 1.2f, R, m).Hold, Is.EqualTo(SunnyDemo.HandHold.PalmDown), "a flat hand lies palm down");
            Assert.That(SunnyDemo.Sample(DemoKind.Custom, 1.8f, R, m).Hand, Is.EqualTo(GhostHand.Pinch));
        }

        [Test]
        public void TheWorldPoseTouchesTheAnchorWhereThePresetSays()
        {
            var anchor = new Vector3(0.3f, 0.82f, 0.45f);
            Vector3 up = Quaternion.Euler(-28f, 15f, 0f) * Vector3.up * R;   // a tilted field; its length is the rim radius
            var eye = new Vector3(0f, 1.2f, 0f);
            var sunnyPos = new Vector3(-0.2f, 1.1f, 0.5f);
            Quaternion sunnyRot = Quaternion.LookRotation(sunnyPos - eye, Vector3.up);
            const float scale = 0.8f;
            SunnyDemo.AnchorFrame(anchor, up, eye, out Vector3 x, out Vector3 y, out Vector3 z);
            Assert.That(Vector3.Dot(z, eye - anchor), Is.GreaterThan(0f), "z toward the player");
            Assert.That(Vector3.Dot(Vector3.Cross(y, z), x), Is.LessThan(0f), "x to the player's right (x = z cross y)");
            foreach (DemoKind k in Platform)
                for (float t = 0.3f; t < 2.7f; t += 0.37f)
                {
                    DemoPose p = default;
                    SunnyDemo.EvaluateAnchored(k, t, anchor, up, eye, null, sunnyPos, sunnyRot, scale, ref p);
                    SunnyDemo.AnchoredPose a = SunnyDemo.Sample(k, t, R);
                    Vector3 want = anchor + x * a.Offset.x + y * a.Offset.y + z * a.Offset.z;
                    Vector3 contact = SunnyDemo.ContactPoint(a, p.IndexCurl);
                    Vector3 got = sunnyPos + sunnyRot * (scale * p.HandPosition + p.HandRotation * contact * scale);
                    Assert.That(Vector3.Distance(got, want), Is.LessThan(1e-4f), $"{k} at {t}: the contact point");
                    Assert.IsTrue(p.Visible && p.Wide, k + ": shown, wide bounds");
                    Quaternion world = sunnyRot * p.HandRotation;
                    if (a.Hold == SunnyDemo.HandHold.FingersDown && a.RollDeg == 0f)
                        Assert.That(Vector3.Dot(world * Vector3.up, -y), Is.GreaterThan(0.999f), k + ": fingers down at it");
                    if (a.Hold == SunnyDemo.HandHold.PalmDown && a.RollDeg == 0f)
                        Assert.That(Vector3.Dot(world * Vector3.back, -y), Is.GreaterThan(0.999f), k + ": the palm on the surface");
                    if (a.Hold == SunnyDemo.HandHold.Pointing)
                        Assert.That(Vector3.Dot(world * Vector3.up, (anchor - want).normalized), Is.GreaterThan(0.999f), k + ": the index points at it");
                }
            Assert.That(SunnyDemo.RadiusOf(up), Is.EqualTo(R).Within(1e-5f), "the up's length is the radius");
            Assert.That(SunnyDemo.RadiusOf(Vector3.up), Is.EqualTo(0.08f), "a unit up carries none");
        }

        [Test]
        public void SunnyRunsATipDemoAtItsAnchorThenItsSecondAndStopsWhenItLeaves()
        {
            var rig = new SunnyRig();
            Sunny s = rig.Sunny;
            var anchor = new Vector3(0.2f, 0.8f, 0.5f);
            Assert.IsTrue(s.TipLines("tip_test", new[] { GuideLines.TipArrange }, 6f));
            s.ShowTipDemo(DemoKind.Tap, true, anchor, Vector3.up, null, DemoKind.Circle, true, anchor + Vector3.right * 0.1f, Vector3.up * 0.1f);
            Assert.That(s.TipId, Is.EqualTo("tip_test"));
            rig.Run(0.5f);
            Assert.That(s.DemoKind, Is.EqualTo(DemoKind.Tap));
            Assert.IsTrue(s.Demo.Visible && s.Demo.Wide && s.TipDemoAnchored);
            rig.Run(GuideTiming.DemoLoop);
            Assert.That(s.DemoKind, Is.EqualTo(DemoKind.Circle), "the second demo after one loop");
            Assert.That(Vector3.Distance(s.TipAnchor, anchor + Vector3.right * 0.1f), Is.LessThan(1e-6f), "at its own anchor");
            Assert.IsTrue(rig.Until(() => s.State == SunnyState.Leave, 10f));
            rig.Step();
            Assert.That(s.DemoKind, Is.EqualTo(DemoKind.None), "the demo goes with the tip");
            Assert.IsFalse(s.Demo.Visible);
        }

        [Test]
        public void ACaptionOnlyTipAndAMultiLineTipAreSaid()
        {
            var rig = new SunnyRig();
            Sunny s = rig.Sunny;
            Assert.IsTrue(s.TipText("tip_handpan_arrive", "Tap the top of the handpan to hear it.", 4f));
            Assert.That(s.Line, Is.EqualTo("Tap the top of the handpan to hear it."));
            Assert.That(rig.Voice.Plays, Is.Empty, "no voice for a caption-only tip");
            Assert.IsTrue(s.InTip);
            rig.Run(3.5f);
            Assert.That(s.State, Is.EqualTo(SunnyState.Tip), "held for its seconds");
            Assert.IsTrue(rig.Until(() => s.State != SunnyState.Tip, 2f));
            Assert.IsTrue(s.TipHeard);
            var rig2 = new SunnyRig();
            Assert.IsTrue(rig2.Sunny.TipLines("tip_two", new[] { GuideLines.TipArrange, GuideLines.TipLoopStart }, GuideTiming.TipT));
            Assert.IsTrue(rig2.Until(() => rig2.Voice.Plays.Count == 2, 10f), "the second line follows the first");
            Assert.That(rig2.Voice.Plays, Is.EqualTo(new[] { GuideLines.TipArrange, GuideLines.TipLoopStart }));
            Assert.That(rig2.Sunny.Line, Is.EqualTo(GuideLines.TextOf(GuideLines.TipLoopStart)));
            GuideLines.ReloadMerged();
            var rig3 = new SunnyRig();
            Assert.IsTrue(rig3.Sunny.Tip("tip_set_full"), "a merged line is a line");
            Assert.That(rig3.Sunny.Line, Is.EqualTo("Only three fit. The least played one goes back."));
            Assert.That(rig3.Sunny.LineSeconds("tip_set_full"), Is.EqualTo(3.229f + GuideTiming.Lead).Within(1e-4f), "its manifest length (the fake voice has none)");
        }
    }
}
