// The conformance suite's fixture for the marimba (platform.md 12.2 check 7): a fingertip stroke down onto E4 (a lesson
// target's landing), through the scripted hands, with the marimba at the seat. The bare fingertip plays the marimba as a
// mallet does (spec 5.2): one live `marimba` onset.
using System;
using MusicRoom.Core;
using MusicRoom.Marimba;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode
{
    public sealed class MarimbaFixture : IInstrumentFixture
    {
        public InstrumentKind Kind => InstrumentKind.Marimba;

        public void Strike(FixtureContext c)
        {
            MarimbaInstrument m = null;
            foreach (IInstrument i in c.App.Instruments) if (i.Kind == Kind) m = i as MarimbaInstrument;
            if (m == null) throw new InvalidOperationException("the marimba is not a MarimbaInstrument");
            m.TryGetLanding(new LessonTarget { i = 16, midi = 64, note = "E4" }, out Pose landing, out _, out _);
            Vector3 up = landing.rotation * Vector3.up;
            c.Stroke(landing.position + up * 0.05f, landing.position - up * 0.005f);
        }
    }
}
