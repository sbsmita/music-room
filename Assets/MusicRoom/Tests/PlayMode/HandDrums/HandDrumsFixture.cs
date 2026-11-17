// The conformance suite's fixture for the hand drums (platform.md 12.2 check 7): one live open tone on the conga, played by
// the scripted right hand through the real stroke logic (FakeHandSource, all 26 joints and their rotations): the open-tone
// demo hand of Sunny's tip (HandDrumsDemos, the same motion the EditMode tests prove the stroke logic plays as "open"),
// posed over the drums' rig wherever the layout put them, at 72 frames per second.
using System;
using MusicRoom.Core;
using MusicRoom.HandDrums;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class HandDrumsFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.HandDrums;

        public void Strike(FixtureContext c) => Play(c, HdDemo.OpenTone, 0, 1.3f);

        /// <summary>Plays a demo variant's hand motion on drum `drum` for `seconds` (one frame per Frame()), then parks the hand.</summary>
        public static void Play(FixtureContext c, HdDemo demo, int drum, float seconds, bool park = true)
        {
            HandDrumsInstrument drums = Find(c.App);
            if (drums == null) throw new InvalidOperationException("the hand drums are not loaded");
            var pose = new HandPose[2];
            for (int h = 0; h < 2; h++) pose[h] = new HandPose { Position = new Vector3[HandFrame.JointCount], Rotation = new Quaternion[HandFrame.JointCount] };
            HandDrumsDemos.Key[][] all = { HandDrumsDemos.Keys(demo, 0), HandDrumsDemos.Keys(demo, 1) };
            const float dt = 1f / 72f;
            for (float t = 0f; t <= seconds; t += dt)
            {
                for (int h = 0; h < 2; h++)
                {
                    if (all[h] == null || !HandDrumsDemos.At(all[h], t, out HandDrumsDemos.Key k)) continue;
                    HandDrumsDemos.WorldPose(drums.transform, drum, h, k, pose[h].Position, pose[h].Rotation);
                    c.Boot.Hands.SetPose((HandSide)h, pose[h]);
                }
                c.Frame();
            }
            if (!park) return;
            // away from the drums (up and back toward the player), a few frames
            Vector3 away = drums.transform.TransformPoint(new Vector3(0f, 1.25f, -0.45f));
            if (all[1] != null) c.Boot.Hands.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, away + Vector3.right * 0.15f, Vector3.forward, Vector3.down));
            if (all[0] != null) c.Boot.Hands.SetPose(HandSide.Left, HandPose.OpenFacing(HandSide.Left, away + Vector3.left * 0.15f, Vector3.forward, Vector3.down));
            c.Frames(12);
        }

        public static HandDrumsInstrument Find(App app)
        {
            foreach (IInstrument i in app.Instruments) if (i is HandDrumsInstrument d) return d;
            return null;
        }
    }
}
