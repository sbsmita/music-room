// Focus (platform.md 4.6, 12.1 FocusTests): head yaw sweeps over the default set (piano Front 0, drums Right +90, harp Left
// -90). Facing is the at-seat instrument whose slot heading is within +-30 deg of the head's flattened forward, with 10 deg
// of hysteresis and a 0.3 s dwell; FocusEvent on every change; One alone: Current always.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class FocusTests : LayoutPlayTestBase
    {
        readonly List<FocusEvent> _events = new List<FocusEvent>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return BootApp();
            _events.Clear();
            App.Events.Subscribe<FocusEvent>(e => _events.Add(e));
        }

        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        /// <summary>The head turned to <paramref name="yawDeg"/> (the seat's heading stays: no re-arrangement).</summary>
        void Look(float yawDeg) => App.Head.rotation = Quaternion.Euler(10f, yawDeg, 0f);   // a little down, as seated

        IEnumerator LookFor(float yawDeg, float seconds)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds / DT));
            for (int i = 0; i < n; i++) { Look(yawDeg); yield return Frame(); }
        }

        [UnityTest]
        public IEnumerator FacingFollowsTheGazeWithHysteresisAndDwell()
        {
            Assert.That(Layout.Mode, Is.EqualTo(LayoutMode.All));
            yield return LookFor(0f, 0.4f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Piano), "looking ahead: the piano");
            _events.Clear();
            // to the right: 25 deg is still the piano's (within 30)
            yield return LookFor(25f, 0.4f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Piano));
            // 38 deg: past 30 but inside the 10 deg hysteresis: still the piano, and no event
            yield return LookFor(38f, 0.6f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Piano), "hysteresis: it holds to 40 deg");
            Assert.That(_events, Is.Empty);
            // 45 deg: nothing within 30 (the drums' slot is +90): none, after the dwell
            yield return LookFor(45f, 0.2f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Piano), "the 0.3 s dwell");
            yield return LookFor(45f, 0.2f);
            Assert.That(Layout.Facing, Is.Null, "between slots: none");
            Assert.That(_events.Count, Is.EqualTo(1));
            Assert.IsFalse(_events[0].Has);
            // 70 deg: the drums (within 30 of +90)
            yield return LookFor(70f, 0.4f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(_events[_events.Count - 1].Kind, Is.EqualTo(InstrumentKind.Drums));
            Assert.IsTrue(_events[_events.Count - 1].Has);
            // a glance back for less than the dwell changes nothing
            yield return LookFor(0f, 0.15f);
            yield return LookFor(80f, 0.1f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Drums), "a glance shorter than 0.3 s");
            // to the left: the harp
            yield return LookFor(-85f, 0.5f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Harp));
            // coming back toward the front, the harp holds to -50 (30 + 10 from -90), then the piano takes over at -30
            yield return LookFor(-52f, 0.5f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Harp), "hysteresis on the way back");
            yield return LookFor(-25f, 0.5f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Piano));
        }

        [UnityTest]
        public IEnumerator OneAloneFacesCurrentAlways()
        {
            Layout.Set(LayoutMode.Single, InstrumentKind.Harp);
            yield return LookFor(120f, 0.5f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Harp), "One alone: Current, wherever the head looks");
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            yield return LookFor(-120f, 0.1f);
            Assert.That(Layout.Facing, Is.EqualTo(InstrumentKind.Drums));
            Assert.That(_events[_events.Count - 1].Kind, Is.EqualTo(InstrumentKind.Drums), "FocusEvent on the change");
        }

        [UnityTest]
        public IEnumerator FocusAllocatesNothing()
        {
            yield return LookFor(0f, 0.2f);
            Assert.That(() => { for (int i = 0; i < 30; i++) { Look(i * 3f); Boot.Step(DT); } },
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
