// The conformance suite's fixture for the piano (platform.md 12.2 check 7): the index presses middle C.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class PianoFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.Piano;

        public void Strike(FixtureContext c)
        {
            IPianoApi p = c.App.Get<IPianoApi>();
            p.TryGetKey(60, out PianoKeyInfo key);
            Vector3 top = p.Root.TransformPoint(new Vector3(key.X, 0f, 0.05f));
            c.Stroke(top + Vector3.up * 0.03f, top - Vector3.up * 0.012f);
        }
    }
}
