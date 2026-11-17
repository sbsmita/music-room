// The conformance suite's fixture for the harp (platform.md 12.2 check 7): a poke through string 14 at its play point.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class HarpFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.Harp;

        public void Strike(FixtureContext c)
        {
            IHarpApi h = c.App.Get<IHarpApi>();
            Vector3 p = h.PlayPoint(14);
            c.Stroke(h.Root.TransformPoint(p + new Vector3(0f, 0f, 0.03f)), h.Root.TransformPoint(p + new Vector3(0f, 0f, -0.01f)));
        }
    }
}
