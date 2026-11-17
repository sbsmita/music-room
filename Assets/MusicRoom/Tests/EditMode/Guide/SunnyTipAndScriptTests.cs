// Sunny's two additions for the looper: a one-line tip outside the tour (IGuide.Tip: says it, stays Hold(id, 1.5), leaves
// silently, counts as heard unless cut, never touches the tour's Done), and the scripted mode of the deck walkthrough
// (perched where the script says, the ghost index finger pressing a control at a world anchor, leaving with or without
// "Bye!").
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Guide
{
    public class SunnyTipAndScriptTests
    {
        [Test]
        public void ATipIsSaidThenSunnyLeavesSilentlyAndTheTourIsUntouched()
        {
            var rig = new SunnyRig();
            Sunny s = rig.Sunny;
            s.Tip(GuideLines.TipLoopStart);
            Assert.That(s.State, Is.EqualTo(SunnyState.Tip));
            Assert.IsTrue(s.Present);
            Assert.That(s.Line, Is.EqualTo("Listen to the count, then play. It will repeat."));
            Assert.That(rig.Voice.Plays, Is.EqualTo(new[] { GuideLines.TipLoopStart }));
            float hold = s.Hold(GuideLines.TipLoopStart, GuideTiming.TipT);
            Assert.That(hold, Is.EqualTo(3.416f + GuideTiming.Lead + GuideTiming.HoldPad).Within(1e-4f));
            rig.Run(hold - 0.2f);
            Assert.That(s.State, Is.EqualTo(SunnyState.Tip), "it stays for its line");
            Assert.IsTrue(rig.Until(() => s.State != SunnyState.Tip, 2f));
            Assert.That(s.State, Is.EqualTo(SunnyState.Leave));
            Assert.IsTrue(s.TipHeard);
            Assert.That(rig.Voice.Plays.Count, Is.EqualTo(1), "no \"Bye!\" after a tip");
            Assert.IsFalse(s.Done, "a tip is not the tour");
        }

        [Test]
        public void ATipCutShortIsNotHeardAndHidingItLeavesTheTourAlone()
        {
            var rig = new SunnyRig();
            Sunny s = rig.Sunny;
            s.Tip(GuideLines.TipLoopStart);
            rig.Run(0.5f);
            s.StopVoice();                       // the headset came off
            Assert.IsTrue(rig.Until(() => s.State != SunnyState.Tip, 6f));
            Assert.IsFalse(s.TipHeard, "cut: it will be said another time");
            var rig2 = new SunnyRig();
            rig2.Sunny.Tip(GuideLines.TipLoopStart);
            rig2.Run(0.3f);
            rig2.Sunny.Dismiss();
            Assert.IsFalse(rig2.Sunny.Done, "Hide Sunny during a tip does not finish the tour");
        }

        [Test]
        public void AScriptPerchesSunnyAndThePressDemoTouchesTheControl()
        {
            var rig = new SunnyRig();
            Sunny s = rig.Sunny;
            s.BeginScript();
            Assert.That(s.State, Is.EqualTo(SunnyState.Script));
            var perch = new Vector3(-0.3f, 0.95f, -0.2f);
            s.Perch = perch;
            rig.Step();
            Assert.That(Vector3.Distance(s.Position, perch), Is.LessThan(0.02f), "snaps to the perch (then bobs)");
            var anchor = new Vector3(-0.35f, 0.68f, -0.25f);
            s.ShowPress(anchor, Vector3.up);
            s.SayScript("deck_guide_rec");
            Assert.That(rig.Voice.Plays, Does.Contain("deck_guide_rec"));
            for (int i = 0; i < 200; i++) rig.Step();
            Assert.That(s.DemoKind, Is.EqualTo(DemoKind.Press));
            Assert.IsTrue(s.Demo.Visible);
            Assert.IsTrue(s.Demo.Wide, "the demo reaches far from Sunny: wide culling bounds");
            // The fingertip, in world space: Sunny's frame applied to the hand pose and its index tip.
            float lowest = float.MaxValue, highest = float.MinValue;
            for (float t = 0f; t < GuideTiming.DemoLoop; t += 0.02f)
            {
                DemoPose p = default;
                SunnyDemo.EvaluatePress(t, anchor, Vector3.up, s.Position, s.Rotation, s.Scale, rig.World.Head, ref p);
                Vector3 tip = s.Position + s.Rotation * (s.Scale * p.HandPosition + p.HandRotation * SunnyDemo.IndexTip * s.Scale);
                Assert.That(new Vector2(tip.x - anchor.x, tip.z - anchor.z).magnitude, Is.LessThan(1e-3f), "straight over the control");
                lowest = Mathf.Min(lowest, tip.y - anchor.y);
                highest = Mathf.Max(highest, tip.y - anchor.y);
                Vector3 down = s.Rotation * (p.HandRotation * Vector3.up);
                Assert.That(Vector3.Dot(down, Vector3.down), Is.GreaterThan(0.999f), "the fingers point down at it");
            }
            Assert.That(lowest, Is.EqualTo(SunnyDemo.PressTouch).Within(2e-3f), "it touches the control's top");
            Assert.That(highest, Is.EqualTo(SunnyDemo.PressLift).Within(1e-3f), "from 6 cm over it");
            s.EndScript(bye: false);
            Assert.That(s.State, Is.EqualTo(SunnyState.Leave));
            Assert.IsFalse(s.Done, "the walkthrough is not the tour");
            Assert.IsNull(s.Perch);
        }
    }
}
