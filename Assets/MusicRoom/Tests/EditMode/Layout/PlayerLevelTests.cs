// The player's level (Layout/LayoutModule.Height.cs, 2026-11-11 headset test: "that instrument become low ... unable to bring
// that instrument to my level"): the lift Reset view takes for the eyes' height, and every instrument's height range (the
// piano's keys from a low seat to standing, every other instrument from the floor up to a standing height, never into the
// floor); and mixed reality's focus return asks the device before reloading a room without Space Setup.
using MusicRoom.Core;
using MusicRoom.Layout;
using MusicRoom.MR;
using NUnit.Framework;

namespace MusicRoom.Tests.Layout
{
    public class PlayerLevelTests
    {
        [Test]
        public void ASeatedPlayerGetsTheHomesAStandingOneALift()
        {
            foreach (float eye in new[] { 1.05f, 1.15f, 1.2f, 1.3f })
                Assert.That(LayoutModule.EyeLift(eye), Is.EqualTo(0f), "seated eyes " + eye + ": the homes");
            Assert.That(LayoutModule.EyeLift(1.45f), Is.EqualTo(0.15f).Within(1e-5f), "a bar stool");
            Assert.That(LayoutModule.EyeLift(1.60f), Is.EqualTo(0.30f).Within(1e-5f), "standing: the keys at 1.02 m");
            Assert.That(LayoutModule.EyeLift(0.95f), Is.EqualTo(-0.10f).Within(1e-5f), "a low sofa");
            Assert.That(LayoutModule.EyeLift(2.2f), Is.EqualTo(HeightConstants.PlayerLiftMax), "held in range");
            Assert.That(LayoutModule.EyeLift(0.5f), Is.EqualTo(HeightConstants.PlayerLiftMin), "held in range");
            Assert.That(LayoutModule.EyeLift(float.NaN), Is.EqualTo(0f), "eyes unknown: the homes");
        }

        [Test]
        public void EveryInstrumentRisesToAStandingHeightAndNoneSinksIntoTheFloor()
        {
            IInstrumentRegistry reg = InstrumentRegistry.Discover();
            int n = 0;
            foreach (InstrumentDescriptor d in reg.All)
            {
                if (!d.Playable || d.Seat == null) continue;
                n++;
                if (d.Kind == InstrumentKind.Piano) Assert.That(d.Seat.LiftMin, Is.LessThan(0f), "the piano's stand shrinks too");
                else Assert.That(d.Seat.LiftMin, Is.GreaterThanOrEqualTo(0f), d.Id + ": never into the floor (a riser raises it)");
                Assert.That(d.Seat.LiftMax, Is.GreaterThanOrEqualTo(0.30f), d.Id + ": up to a standing player's level");
                Assert.That(d.Seat.LiftMax, Is.LessThanOrEqualTo(ComfortCodec.MaxHeight), d.Id + ": a tweak the codec keeps");
            }
            Assert.That(n, Is.GreaterThanOrEqualTo(7), "the seven instruments");
        }

        [Test]
        public void AFocusReturnWithoutARoomAsksTheDeviceInsteadOfReloading()
        {
            Assert.IsTrue(MrModule.AskDeviceOnFocus(MrSceneStatus.NoRooms), "no Space Setup: no room -> no room is no change");
            Assert.IsTrue(MrModule.AskDeviceOnFocus(MrSceneStatus.Loaded));
            Assert.IsFalse(MrModule.AskDeviceOnFocus(MrSceneStatus.NoPermission), "spatial data may have been allowed meanwhile");
            Assert.IsFalse(MrModule.AskDeviceOnFocus(MrSceneStatus.Failed));
            Assert.IsFalse(MrModule.AskDeviceOnFocus(MrSceneStatus.NotAvailable));
        }
    }
}
