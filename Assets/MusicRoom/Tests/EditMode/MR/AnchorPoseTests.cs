// E9 (experience.md section 11): a re-localized piano anchor is used only when its pose is plausible for a seated player
// (the experience lab's experience.vectors.json anchorPose): within 3 m of the head horizontally, upright (up.y >= .98),
// between -0.1 m and 1.6 m high.
using MusicRoom.MR;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.MR
{
    public class AnchorPoseTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);

        [Test]
        public void TheReferenceCases()
        {
            Assert.IsTrue(PianoAnchorStore.PoseOk(new Vector3(0.3f, 0.74f, 0.6f), Vector3.up, Head), "a table in front");
            Assert.IsFalse(PianoAnchorStore.PoseOk(new Vector3(0.3f, 0.74f, 3.5f), Vector3.up, Head), "too far");
            Assert.IsFalse(PianoAnchorStore.PoseOk(new Vector3(0.3f, 0.74f, 0.6f), new Vector3(0.26f, 0.965f, 0f), Head), "tilted");
            Assert.IsFalse(PianoAnchorStore.PoseOk(new Vector3(0f, 1.9f, 0.5f), Vector3.up, Head), "too high");
        }

        [Test]
        public void TheLimitsAreInclusiveAndHorizontal()
        {
            Assert.IsTrue(PianoAnchorStore.PoseOk(new Vector3(0f, -0.1f, 3f), Vector3.up, Head), "on the floor, 3 m away");
            Assert.IsTrue(PianoAnchorStore.PoseOk(new Vector3(0f, 0.7f, 2.99f), Vector3.up, new Vector3(0f, 0.2f, 0f)), "the head's height does not count");
            Assert.IsFalse(PianoAnchorStore.PoseOk(new Vector3(0f, -0.2f, 1f), Vector3.up, Head), "under the floor");
            Assert.IsTrue(PianoAnchorStore.PoseOk(new Vector3(0f, 0.7f, 1f), new Vector3(0f, 0.98f, 0.199f), Head), "1 deg short of the tilt limit is fine");
            Assert.That(PianoAnchorStore.KeyAnchor, Is.EqualTo("musicroom.mrPianoAnchor"));
            Assert.That(PianoAnchorStore.KeyOnTable, Is.EqualTo("musicroom.mrPianoOnTable"));
            Assert.That(PianoAnchorStore.KeyFailures, Is.EqualTo("musicroom.anchorFailures"));
        }
    }
}
