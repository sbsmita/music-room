// The sheet-music hook against the piano's real music rest (12-new-instruments/platform.md 13.5, sheet.md 12 item 1):
// PianoMusicRest.Local is the front face's bottom centre of the board PianoBody.MusicRest builds (every board vertex lies on
// or behind that face and on or above that edge, the board centred on x 0), up along the face (0, cos .3, sin .3), forward
// into it; the ledge's top is MusicRestLedge up the face from there; the descriptor's SheetAnchor is the same pose; and the
// numbers sheet.md quoted (0, .039, .2375, ledge .0073) are within a millimetre (they left out the board's half thickness).
using MusicRoom.Core;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Piano
{
    public class PianoMusicRestTests
    {
        [Test]
        public void TheRestPoseIsTheBoardsFrontFaceBottomCentre()
        {
            PianoMeshBuilder b = PianoBody.MusicRest();
            int board = PianoMeshBuilder.RoundedBoxVertexCount(2);
            Assert.That(b.VertexCount, Is.EqualTo(2 * board), "the board, then the ledge");
            Pose rest = PianoMusicRest.Local;
            Vector3 up = rest.rotation * Vector3.up, fwd = rest.rotation * Vector3.forward;
            Assert.That(Vector3.Angle(up, new Vector3(0f, Mathf.Cos(0.3f), Mathf.Sin(0.3f))), Is.LessThan(1e-3f), "up along the rest, leaning back 0.3 rad");
            Assert.That(Vector3.Dot(fwd, Vector3.forward), Is.GreaterThan(0.9f), "forward away from the player");
            float minFace = float.MaxValue, minUp = float.MaxValue, minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < board; i++)
            {
                Vector3 d = b.Positions[i] - rest.position;
                minFace = Mathf.Min(minFace, Vector3.Dot(d, fwd));
                minUp = Mathf.Min(minUp, Vector3.Dot(d, up));
                minX = Mathf.Min(minX, b.Positions[i].x);
                maxX = Mathf.Max(maxX, b.Positions[i].x);
            }
            Assert.That(minFace, Is.EqualTo(0f).Within(2e-5f), "the front face passes through the pose");
            Assert.That(minUp, Is.EqualTo(0f).Within(2e-5f), "the board's bottom edge is the pose");
            Assert.That(minX + maxX, Is.EqualTo(0f).Within(1e-5f), "centred");
            Assert.That(rest.position.x, Is.EqualTo(0f));
            // The ledge the pages stand on: its top along the face.
            float ledgeTop = float.MinValue;
            for (int i = board; i < b.VertexCount; i++) ledgeTop = Mathf.Max(ledgeTop, b.Positions[i].y);
            Assert.That(rest.position.y + PianoMusicRest.Ledge * up.y, Is.EqualTo(ledgeTop).Within(2e-5f), "MusicRestLedge reaches the ledge's top");
            Debug.Log($"[Piano] music rest: local ({rest.position.x:F4}, {rest.position.y:F5}, {rest.position.z:F5}), up {up:F4}, ledge {PianoMusicRest.Ledge:F5} m " +
                      $"(sheet.md: (0, 0.039, 0.2375), ledge 0.0073); ledge top y {ledgeTop:F4}");
            // sheet.md's numbers, within a millimetre.
            Assert.That(Vector3.Distance(rest.position, new Vector3(0f, 0.039f, 0.2375f)), Is.LessThan(1e-3f));
            Assert.That(PianoMusicRest.Ledge, Is.EqualTo(0.0073f).Within(1e-3f));
        }

        [Test]
        public void TheDescriptorsSheetAnchorIsTheRest()
        {
            InstrumentDescriptor d = new PianoDefinition().Describe();
            Assert.That(d.HasSheetAnchor, Is.True);
            Assert.That(d.SheetAnchor.position, Is.EqualTo(PianoMusicRest.Local.position));
            Assert.That(Quaternion.Angle(d.SheetAnchor.rotation, PianoMusicRest.Local.rotation), Is.LessThan(1e-4f));
            Assert.That(d.Anchors, Does.Contain(PianoPlatform.KeysMiddle));
        }

        [Test]
        public void AnchorIdsParseWithoutSurprises()
        {
            Assert.That(PianoPlatform.TryParseIndex("key.60", "key.", out int n) && n == 60, Is.True);
            Assert.That(PianoPlatform.TryParseIndex("key.", "key.", out _), Is.False);
            Assert.That(PianoPlatform.TryParseIndex("key.6a", "key.", out _), Is.False);
            Assert.That(PianoPlatform.TryParseIndex("keys.60", "key.", out _), Is.False);
            Assert.That(PianoPlatform.KeyTop(20, out _) || PianoPlatform.KeyTop(109, out _), Is.False, "outside the 88 keys");
            Assert.That(PianoPlatform.KeyTop(61, out Vector3 black) && black.y == PianoSpec.BTOP, Is.True);
        }
    }
}
