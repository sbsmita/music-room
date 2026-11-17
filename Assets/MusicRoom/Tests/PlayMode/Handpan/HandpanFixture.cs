// The conformance suite's fixture for the handpan (platform.md 12.2 check 7): the right index tip comes down onto A3
// (field 1), 2 cm off its apex toward the ding, along the field normal at about 1 m/s, dwells three frames and lifts, the
// way HandpanFeedTests plays it.
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Handpan;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class HandpanFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.Handpan;

        public void Strike(FixtureContext c)
        {
            IHandpanApi h = c.App.Get<IHandpanApi>();
            Vector3 n = h.FieldNormal(1);
            Vector3 toDing = Vector3.ProjectOnPlane(h.FieldCentre(0) - h.FieldCentre(1), n).normalized;
            Vector3 surf = h.FieldCentre(1) + toDing * 0.02f;
            float pad = HandRestPose.Radius[(int)HandJoint.IndexTip] * c.Boot.Hands.HandScale;
            void At(float height) => c.Index(surf + n * (pad + height));
            At(0.05f); c.Frames(8);
            for (float y = 0.05f; y > -0.012f; y -= 0.012f) { At(y); c.Frame(); }
            At(-0.012f); c.Frames(3);
            for (float y = -0.012f; y < 0.06f; y += 0.008f) { At(y); c.Frame(); }
            c.Index(surf + n * 0.3f); c.Frames(12);
        }
    }
}
