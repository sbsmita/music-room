// Tools in Layout's grab pass (platform.md 4.6, the brief's step 7): every IHandToolProvider is asked for NearestFree(tip)
// and only the nearest gets TryPickUp (Grabbed / Near consume the pinch as the kit's sticks do); with no provider for the kit
// IDrumsApi.TryPickUp runs exactly as today; the tools of an instrument that leaves the seat are released (ReleaseAll of its
// providers), the kit's sticks as today (IDrumsApi.ReleaseAllSticks).
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class ToolProviderTests : LayoutPlayTestBase
    {
        sealed class FakeTools : IHandToolProvider
        {
            public InstrumentKind Owner { get; set; }
            public float Distance = float.PositiveInfinity;
            public StickPick Answer = StickPick.Grabbed;
            public int Asked, Picks, Releases;
            public float NearestFree(Vector3 tip) { Asked++; return Distance; }
            public StickPick TryPickUp(HandState hand, Vector3 tip) { Picks++; return Answer; }
            public bool AnyHeld => false;
            public void ReleaseAll() => Releases++;
            public int ShadowSpheres(System.Span<Vector4> centreRadius) => 0;
        }

        sealed class Registrar : AppModule
        {
            public readonly FakeTools Near = new FakeTools { Owner = InstrumentKind.Handpan, Distance = 0.05f };
            public readonly FakeTools Far = new FakeTools { Owner = InstrumentKind.Bowls, Distance = 0.2f };
            public override int Order => 50;
            public override void Init(App app)
            {
                app.Register<IHandToolProvider>(Far);
                app.Register<IHandToolProvider>(Near);
            }
        }

        Registrar _tools;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _tools = new Registrar();
            yield return BootApp(withStubs: true, extra: _tools);
        }

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        [UnityTest]
        public IEnumerator TheNearestProviderAloneGetsThePickUp()
        {
            Vector3 at = Head.position + new Vector3(0.3f, -0.2f, 0.35f);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.04f));
            yield return Frame();
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            yield return Frame();
            Assert.That(_tools.Near.Asked, Is.GreaterThan(0), "every provider is asked");
            Assert.That(_tools.Far.Asked, Is.GreaterThan(0));
            Assert.That(_tools.Near.Picks, Is.EqualTo(1), "the nearest one picks up");
            Assert.That(_tools.Far.Picks, Is.Zero, "the farther one never does");
            Assert.That(Probe.Drums.PickCalls, Is.Zero, "a provider took it: the kit's sticks were not asked");
            Assert.IsFalse(Layout.IsArmed(1), "Grabbed: the pinch is not the start of a grab");
            yield return Release(HandSide.Right, at);
            // none near (every provider +inf): the kit's own sticks as today
            _tools.Near.Distance = _tools.Far.Distance = float.PositiveInfinity;
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            yield return Frame();
            Assert.That(Probe.Drums.PickCalls, Is.EqualTo(1), "IDrumsApi.TryPickUp, with no provider for the kit");
            Assert.That(_tools.Near.Picks, Is.EqualTo(1));
            yield return Release(HandSide.Right, at);
        }

        [UnityTest]
        public IEnumerator AnInstrumentLeavingTheSeatReleasesItsTools()
        {
            Assert.IsTrue(Layout.TrySetSet(new[] { InstrumentKind.Piano, InstrumentKind.Handpan }));
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) yield return Frame();
            int before = _tools.Near.Releases, far = _tools.Far.Releases, kit = Probe.Drums.ReleaseCount;
            Assert.That(kit, Is.EqualTo(1), "the kit left the set: its sticks went with it (as today)");
            Assert.IsTrue(Layout.PutAway(InstrumentKind.Handpan));
            Assert.That(_tools.Near.Releases, Is.EqualTo(before + 1), "the handpan's tools are released as it leaves");
            Assert.That(_tools.Far.Releases, Is.EqualTo(far), "not another instrument's");
            Assert.That(Probe.Drums.ReleaseCount, Is.EqualTo(kit));
            for (int i = 0; i < 150 && Layout.AnyMoving; i++) yield return Frame();
        }
    }
}
