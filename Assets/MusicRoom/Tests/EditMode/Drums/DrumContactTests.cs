// The kit's contact surface (10-upgrade/experience.md section 2.1; E1 / E3, visual only): piece-local hypot(x, z) <= r on
// the plane y = 0 of each piece, the cymbals on their plate at its current tilt; Use Both on the heads and the pad,
// Shadow only on the cymbals (a hand on a cymbal is the choke gesture), nothing while the kit is held.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class DrumContactTests
    {
        sealed class HeldGrab : IGrabService
        {
            public Rig Held;
            public Rig HeldRig => Held;
            public bool AnyHeld => Held != null;
            public Rig GrabTarget(Vector3 point, float reach = 0.07f) => null;
        }

        [Test]
        public void HeadsAndThePadStopFingersCymbalsOnlyShadowThem()
        {
            using (var k = new KitFixture())
            {
                DrumKit kit = k.Kit;
                Assert.IsTrue(kit.QueryContact(k.At(DrumSpec.SnareId, 0.05f, -0.004f, 0.03f), 0.06f, 0.04f, out ContactHit hit), "4 mm into the snare head");
                Assert.That(hit.Use, Is.EqualTo(ContactUse.Both));
                // heights count from the head as drawn, 1 mm above the strike plane (DrumSpec.HeadLift)
                Assert.That(hit.Height, Is.EqualTo(-0.004f - DrumSpec.HeadLift).Within(1e-5f));
                Assert.That(Vector3.Dot(hit.Normal, k.Piece(DrumSpec.SnareId).Group.up), Is.GreaterThan(0.9999f));
                Assert.That(Vector3.Distance(hit.Point, k.At(DrumSpec.SnareId, 0.05f, DrumSpec.HeadLift, 0.03f)), Is.LessThan(1e-5f), "the foot on the drawn head");

                Assert.IsTrue(kit.QueryContact(k.At(DrumSpec.KickPadId, 0.02f, 0.01f, 0f), 0.06f, 0.04f, out hit));
                Assert.That(hit.Use, Is.EqualTo(ContactUse.Both), "the kick pad");

                DrumPiece crash = k.Piece(DrumSpec.CrashId);
                Transform plate = DrumKit.SurfaceOf(crash);
                Assert.That(plate, Is.SameAs(crash.Top), "the plate that tilts");
                Assert.IsTrue(kit.QueryContact(plate.TransformPoint(new Vector3(0.1f, 0.02f, 0f)), 0.06f, 0.04f, out hit));
                Assert.That(hit.Use, Is.EqualTo(ContactUse.Shadow), "a hand on a cymbal is never lifted");
                // the surface is the plate's drawn top (its lathe profile), not its strike plane: the shadow lies on it
                float top = DrumSpec.CymbalTopY(crash.R, 0.1f);
                Assert.That(top, Is.InRange(0.006f, 0.012f), "the bow between 6 cm and 0.6 r");
                Assert.That(plate.InverseTransformPoint(hit.Point).y, Is.EqualTo(top).Within(1e-5f), "the foot on the plate's top");
                Assert.That(hit.Height, Is.EqualTo(0.02f - top).Within(1e-5f));
                Assert.That(DrumSpec.CymbalTopY(crash.R, 0.03f), Is.EqualTo(0.016f).Within(1e-6f), "the 16 mm bell");
                Assert.That(DrumSpec.CymbalTopY(crash.R, crash.R * 0.6f), Is.EqualTo(0.006f).Within(1e-6f));
                Assert.That(DrumSpec.CymbalTopY(crash.R, crash.R), Is.EqualTo(0f), "the edge");
                Assert.IsFalse(kit.QueryContact(plate.TransformPoint(new Vector3(0.1f, -0.02f, 0f)), 0.06f, 0.04f, out _), "2 cm under the plate is not on it");

                // The plate's current tilt is the surface.
                plate.localRotation = Quaternion.Euler(6f, 0f, 0f);
                Assert.IsTrue(kit.QueryContact(plate.TransformPoint(new Vector3(0f, 0.01f, 0.15f)), 0.06f, 0.04f, out hit));
                Assert.That(Vector3.Angle(hit.Normal, plate.up), Is.LessThan(0.01f));
                Assert.That(hit.Height, Is.EqualTo(0.01f - DrumSpec.CymbalTopY(crash.R, 0.15f)).Within(1e-5f));
                plate.localRotation = Quaternion.identity;

                Assert.IsFalse(kit.QueryContact(k.At(DrumSpec.SnareId, 0.25f, 0.01f, 0f), 0.06f, 0.04f, out _), "outside the head");
                Assert.IsFalse(kit.QueryContact(k.At(DrumSpec.SnareId, 0f, 0.07f, 0f), 0.06f, 0.04f, out _), "7 cm above: beyond the query");

                kit.Grab = new HeldGrab { Held = kit.Rig };
                Assert.IsFalse(kit.QueryContact(k.At(DrumSpec.SnareId, 0.05f, 0.01f, 0.03f), 0.06f, 0.04f, out _), "a held kit offers no surface");
                kit.Grab = null;
            }
        }
    }
}
