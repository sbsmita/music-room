// The conformance suite's fixture for the singing bowls (platform.md 12.2 check 7): the right index taps the big bowl's
// outer wall 1 cm under the rim, from the side facing the head, at about 1 m/s (three frames in, then away), the way a
// fingertip tap is played: the hand logic strikes it (bowlStrike).
using System;
using MusicRoom.Bowls;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class BowlsFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.Bowls;

        public void Strike(FixtureContext c)
        {
            BowlsInstrument b = null;
            foreach (IInstrument i in c.App.Instruments) if (i.Kind == Kind) b = i as BowlsInstrument;
            if (b == null) throw new InvalidOperationException("the bowls are not a BowlsInstrument");
            Tap(c, b, 0);
        }

        /// <summary>A side tap on bowl i's outer wall (1 cm under the rim, facing the head).</summary>
        public static void Tap(FixtureContext c, BowlsInstrument b, int bowl, float speed = 1.0f)
        {
            b.TryGetAnchor(BowlsLayout.Key[bowl] == "big" ? "bowl.big.near" : "bowl." + BowlsLayout.Key[bowl] + ".near", out Vector3 rim, out Vector3 outward);
            Vector3 wall = rim - b.transform.up * 0.01f;
            Vector3 from = wall + outward * 0.05f + b.transform.up * 0.01f, to = wall - outward * 0.004f;
            c.Index(from + Vector3.up * 0.25f); c.Frames(4);
            c.Index(from); c.Frames(4);
            const int steps = 4;
            for (int k = 1; k <= steps; k++) { c.Index(Vector3.Lerp(from, to, k / (float)steps)); c.Frame(); }
            c.Index(from); c.Frames(2);
            c.Index(from + Vector3.up * 0.3f); c.Frames(12);
        }
    }
}
