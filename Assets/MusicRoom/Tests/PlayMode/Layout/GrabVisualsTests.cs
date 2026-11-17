// The grab feedback with real renderers (not headless): the dwell ring fills segment by segment, the floor ring and
// the cage follow the held rig in gold, and the per-frame updates (sub-mesh range, colours on the renderers' own clones) allocate nothing.
using System.Collections;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension on Is.Not
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public class GrabVisualsTests : LayoutPlayTestBase
    {
        static float[] s_keep;

        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(headless: false); }
        [UnityTearDown] public IEnumerator TearDown() { yield return TearDownApp(); }

        [UnityTest]
        public IEnumerator RingsAndCageShowTheGrabAndAllocateNothing()
        {
            Transform root = App.WorldRoot.Find("GrabVisuals");
            Assert.That(root, Is.Not.Null, "built under World");
            var ring = root.Find("GrabRing").GetComponent<MeshRenderer>();
            var hint = root.Find("GrabHint").GetComponent<MeshRenderer>();
            var cage = root.Find("GrabCage").GetComponent<MeshRenderer>();
            Assert.IsFalse(ring.gameObject.activeSelf || hint.gameObject.activeSelf || cage.gameObject.activeSelf, "hidden at rest");
            Assert.That(ring.sharedMaterial.name, Does.Contain("Layout/GrabRing"));

            Vector3 at = Handle(GoldenLayout.Drums);
            Fake.SetPose(HandSide.Right, OpenAt(HandSide.Right, at + Vector3.up * 0.04f));
            yield return Frame();
            Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, at));
            yield return Frames(10);
            Assert.IsTrue(hint.gameObject.activeSelf, "the dwell ring fills in");
            Mesh hm = hint.GetComponent<MeshFilter>().sharedMesh;
            uint early = hm.GetIndexCount(0);
            Assert.That(early, Is.EqualTo((uint)(6 * Layout.Visuals.HintSegmentsShown)));
            Assert.That(() => { for (int k = 0; k < 20; k++) Layout.Tick(FramePhase.Grab, DT); }, Is.Not.AllocatingGCMemory(), "dwell ring + cage updates");
            yield return Frames(4);
            Assert.That(hm.GetIndexCount(0), Is.GreaterThan(early), "more of the ring as the dwell goes on");
            Assert.IsTrue(cage.gameObject.activeSelf, "the cage brightens with the dwell");
            yield return Frames(12);
            Assert.That(Layout.HeldRig, Is.SameAs(Probe.Rigs[GoldenLayout.Drums]));
            Assert.IsTrue(ring.gameObject.activeSelf);
            Assert.IsFalse(hint.gameObject.activeSelf, "the dwell ring gives way to the floor ring");
            Assert.IsFalse(ring.HasPropertyBlock() || hint.HasPropertyBlock() || cage.HasPropertyBlock(), "no property blocks (SRP Batcher)");
            Assert.That(ring.sharedMaterial, Is.Not.SameAs(cage.sharedMaterial), "each renderer has its own clone");
            Color c = ring.sharedMaterial.GetColor(MaterialLibrary.BaseColor);
            Assert.That(c.r, Is.EqualTo(GrabVisuals.Gold.r).Within(1e-3f));
            Assert.That(c.a, Is.EqualTo(0.5f).Within(1e-3f), "#ffc46a at alpha .5");
            Assert.That(ring.transform.position.y, Is.EqualTo(GrabVisuals.RingY).Within(1e-5f), "on the floor");

            Assert.That(() => { s_keep = new float[1024]; }, Is.AllocatingGCMemory(), "GC.Alloc recorder works here");
            Vector3 p = at;
            for (int i = 1; i <= 5; i++)
            {
                p += Vector3.left * 0.02f;
                Fake.SetPose(HandSide.Right, PinchAt(HandSide.Right, p));
                yield return Frame();
            }
            Assert.That(() => { for (int k = 0; k < 20; k++) Layout.Tick(FramePhase.Grab, DT); }, Is.Not.AllocatingGCMemory(), "floor ring, cage and drag");
            Vector3 ringAt = ring.transform.position;
            Vector3 c0 = Centre(GoldenLayout.Drums);
            Assert.That(Vector2.Distance(new Vector2(ringAt.x, ringAt.z), new Vector2(c0.x, c0.z)), Is.LessThan(1e-3f), "under the footprint's centre");
            yield return ReleaseHands(p);
            Assert.IsFalse(ring.gameObject.activeSelf || cage.gameObject.activeSelf, "gone when let go");
        }

        [UnityTest]
        public IEnumerator TheProceduralMeshesAreFreedWithTheApp()
        {
            // A destroyed MeshFilter does not free a `new Mesh`; the suite boots the app dozens of times (section 16).
            Mesh ring = Layout.Visuals.RingMeshAsset, hint = Layout.Visuals.HintMeshAsset, cage = Layout.Visuals.CageMeshAsset;
            Assert.IsTrue(ring != null && hint != null && cage != null, "built when the app renders");
            yield return TearDownApp();
            yield return null;
            Assert.IsTrue(ring == null, "the floor ring mesh is destroyed");
            Assert.IsTrue(hint == null, "the dwell ring mesh is destroyed");
            Assert.IsTrue(cage == null, "the cage mesh is destroyed");
        }
    }
}
