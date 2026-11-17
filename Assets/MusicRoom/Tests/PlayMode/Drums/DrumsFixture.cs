// The conformance suite's fixture for the drum kit (platform.md 12.2 check 7): a fingertip strike on the snare's head.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class DrumsFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.Drums;

        public void Strike(FixtureContext c)
        {
            IDrumsApi d = c.App.Get<IDrumsApi>();
            Transform snare = d.Pieces[0].Transform;
            c.Stroke(snare.TransformPoint(new Vector3(0.03f, 0.04f, 0f)), snare.TransformPoint(new Vector3(0.03f, -0.01f, 0f)));
        }
    }
}
