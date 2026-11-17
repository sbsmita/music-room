// The page turn's sound through the real audio engine (a band-passed noise burst, then a brighter
// one 40 ms later, IAudioEngine.Swish on the Ui source), captured with the engine's test tap.
using System.Collections;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class PageTurnSoundTests : LayoutPlayTestBase
    {
        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(withAudio: true); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        [UnityTest]
        public IEnumerator ASweepPageTurnSwishes()
        {
            Layout.Set(LayoutMode.Single, InstrumentKind.Piano);
            var engine = App.Get<IAudioEngine>();
            Assert.That(engine, Is.Not.Null);
            var tap = new AudioTestTap();
            engine.TestTap = tap;
            Vector3 palm = Head.position + new Vector3(0.12f, -0.15f, 0.35f);
            Fake.SetPose(HandSide.Right, LayoutModeTests.SweepPose(palm));
            for (int i = 0; i < 6; i++) yield return Frame();
            for (int i = 0; i < 10 && !Layout.IsTurning; i++)
            {
                palm += new Vector3(0.03f, 0f, 0f);   // to the right: previous (the carousel of eight: piano -> drums)
                Fake.SetPose(HandSide.Right, LayoutModeTests.SweepPose(palm));
                yield return Frame();
            }
            engine.TestTap = null;
            Assert.IsTrue(Layout.IsTurning);
            Assert.That(Layout.Current, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(tap.Count(SourceId.Ui, SynthOp.StartNoise), Is.EqualTo(2), "the swish: two noise bursts on the Ui source");
            Assert.That(tap.Commands.Count, Is.EqualTo(2), "and nothing else");
        }
    }
}
