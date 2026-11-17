// Layout against every discovered module (no probe; the real modules since the integration): the room as the desktop boot leaves
// it (Layout never arranges on the desktop: every rig at its home about ORIGIN (0, 1.2, 0), the camera 10 cm behind)
// and the MR members of ILayoutService. The golden-geometry cases of ARCHITECTURE.md 15.5 are in the other Layout tests.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class LayoutStubTests
    {
        TestBoot _boot;

        [UnitySetUp]
        public IEnumerator Boot() { _boot = new TestBoot(); yield return _boot.Start(); }

        [UnityTearDown]
        public IEnumerator TearDown() => _boot.Stop();

        [UnityTest]
        public IEnumerator TheDesktopRoomIsEveryHomeAboutTheOrigin()
        {
            var app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            yield return null;
            var layout = app.Get<ILayoutService>();
            Assert.IsFalse(app.IsPresenting);
            Assert.IsTrue(layout.FirstArrangeDone, "the desktop room is ready on its first frame");
            Assert.That(layout.Mode, Is.EqualTo(LayoutMode.All), "a fresh settings store: the default layout");
            var piano = app.Get<IRigRegistry>().Get(RigId.Piano);
            Assert.That(piano.Group.position.z, Is.EqualTo(0.45f).Within(1e-3f), "piano home 0.45 ahead of ORIGIN");
            Assert.That(piano.Group.position.z - app.Head.position.z, Is.EqualTo(0.55f).Within(1e-3f), "0.55 m ahead of the desktop camera, as in the golden layout");
            Assert.That(piano.Group.position.y, Is.EqualTo(0.72f).Within(1e-3f));
            var drums = app.Get<IRigRegistry>().Get(RigId.Drums);
            Assert.That(drums.Group.position.x, Is.EqualTo(0.2f).Within(1e-3f));
            Assert.That(drums.Group.position.z, Is.EqualTo(-0.26f).Within(1e-3f));
            Assert.That(drums.Group.eulerAngles.y, Is.EqualTo(90f).Within(1e-2f));
            Pose anchor = layout.Anchor;
            Assert.That(anchor.position, Is.EqualTo(piano.Group.position), "All mode: the piano is the anchor");
        }

        [UnityTest]
        public IEnumerator PlaceAroundPianoKeepsTheHomeOffsetsFromThePiano()
        {
            var app = App.Current;
            var layout = app.Get<ILayoutService>();
            var reg = app.Get<IRigRegistry>();
            var piano = reg.Get(RigId.Piano);
            // Inside the VR room box (the room faces the fire: x +-3.2, z -1.62..3.58) and well away from the seat: with the real
            // rigs' footprints the settle would otherwise push rigs that land outside a wall (the room box is on outside mixed reality).
            piano.Group.SetPositionAndRotation(new Vector3(1.5f, 0.8f, 2.5f), Quaternion.Euler(0f, 90f, 0f));
            layout.PlaceAroundPiano();
            var drums = reg.Get(RigId.Drums);
            Vector3 want = piano.Group.position + Quaternion.Euler(0f, 90f, 0f) * new Vector3(drums.HomeAll.x - piano.HomeAll.x, 0f, drums.HomeAll.z - piano.HomeAll.z);
            Assert.That(drums.Group.position.x, Is.EqualTo(want.x).Within(1e-4f));
            Assert.That(drums.Group.position.z, Is.EqualTo(want.z).Within(1e-4f));
            Assert.That(drums.Group.eulerAngles.y, Is.EqualTo(Mathf.Repeat(drums.HomeYawAll + 90f, 360f)).Within(1e-2f));
            Assert.That(piano.Group.position, Is.EqualTo(new Vector3(1.5f, 0.8f, 2.5f)), "the piano stays where it was placed");
            yield return null;
        }
    }
}
