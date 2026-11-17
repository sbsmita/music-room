// The id block's audio side (12-new-instruments/platform.md section 2, rule 2): ten roles, 4-bit unit handles with 27-bit
// payloads, and the piano's and harp's payloads decoding as before the platform.
using MusicRoom.Audio;
using MusicRoom.Audio.Burst;
using MusicRoom.Core;
using NUnit.Framework;

namespace MusicRoom.Tests.Audio
{
    public class UnitIdTests
    {
        [Test]
        public void TheRolesKeepTheirValuesAndThePlatformsAreFiveToNine()
        {
            Assert.That((int)UnitId.Piano, Is.EqualTo(0));
            Assert.That((int)UnitId.Drums, Is.EqualTo(1));
            Assert.That((int)UnitId.Harp, Is.EqualTo(2));
            Assert.That((int)UnitId.Room, Is.EqualTo(3), "Room stays 3");
            Assert.That((int)UnitId.Example, Is.EqualTo(4), "Example stays 4");
            Assert.That((int)UnitId.Handpan, Is.EqualTo(5));
            Assert.That((int)UnitId.HandDrums, Is.EqualTo(6));
            Assert.That((int)UnitId.Marimba, Is.EqualTo(7));
            Assert.That(System.Enum.IsDefined(typeof(UnitId), (byte)8), Is.False, "8 is retired (never reused)");
            Assert.That((int)UnitId.Bowls, Is.EqualTo(9));
            Assert.That((int)UnitId.Count, Is.EqualTo(10));
            Assert.That(SoundUnits.Roles, Has.Length.EqualTo(4), "the unit-only roles (the piano, drums, harp and room units were deleted on 2026-11-11)");
            foreach (UnitId r in new[] { UnitId.Handpan, UnitId.HandDrums, UnitId.Marimba, UnitId.Bowls })
            {
                Assert.That(SoundUnits.IsUnitOnly(r), Is.True, r + " has no classic synth");
                Assert.That(SoundUnits.Roles, Does.Contain(r));
                Assert.That((int)r, Is.EqualTo((int)(InstrumentKind)(int)r), "UnitId value = InstrumentKind value for the platform's roles");
            }
            for (var r = UnitId.Piano; r <= UnitId.Example; r++) Assert.That(SoundUnits.IsUnitOnly(r), Is.False, r.ToString());
        }

        [Test]
        public void EveryRoleRoundTripsPayloadsZeroOneAndTheTop()
        {
            int[] payloads = { 0, 1, (1 << 27) - 1, 12345, 1 << 26 };
            for (var r = UnitId.Piano; r < UnitId.Count; r++)
                foreach (int p in payloads)
                {
                    VoiceHandle h = UnitHandles.Make(null, r, p);
                    Assert.That(UnitHandles.IsUnit(h), Is.True, $"{r} {p}: a unit handle (negative id)");
                    Assert.That(h.Id, Is.LessThan(0));
                    Assert.That(UnitHandles.Unit(h), Is.EqualTo(r), $"{r} {p}: the role");
                    Assert.That(UnitHandles.Payload(h), Is.EqualTo(p), $"{r} {p}: the payload");
                }
            Assert.That(UnitHandles.UnitBits, Is.EqualTo(4));
            Assert.That(UnitHandles.PayloadMask, Is.EqualTo((1 << 27) - 1), "27-bit payloads");
            // the payload loses its top bit only: 2^27 wraps to 0, today's synths' positive ids never look like units
            Assert.That(UnitHandles.Payload(UnitHandles.Make(null, UnitId.Bowls, 1 << 27)), Is.EqualTo(0));
            Assert.That(UnitHandles.IsUnit(new VoiceHandle(null, 7)), Is.False);
        }

        [Test]
        public void ThePianoAndHarpPayloadsDecodeAsBefore()
        {
            // the piano: (gen << 7) | key (key 0..87), wrapping at 2^20 strikes per key (was 2^21)
            foreach (int gen in new[] { 0, 1, 77, (1 << 20) - 1 })
                foreach (int key in new[] { 0, 39, 87 })
                {
                    int payload = (gen << 7) | key;
                    VoiceHandle h = UnitHandles.Make(null, UnitId.Piano, payload);
                    int p = UnitHandles.Payload(h);
                    Assert.That(UnitHandles.Unit(h), Is.EqualTo(UnitId.Piano));
                    Assert.That(p & 127, Is.EqualTo(key), $"key of gen {gen}");
                    Assert.That(p >> 7, Is.EqualTo(gen), $"gen {gen}");
                }
            // the harp: (g << 5) | string (string 0..21)
            foreach (int g in new[] { 0, 1, 5000, (1 << 22) - 1 })
                foreach (int s in new[] { 0, 11, 21 })
                {
                    VoiceHandle h = UnitHandles.Make(null, UnitId.Harp, (g << 5) | s);
                    int p = UnitHandles.Payload(h);
                    Assert.That(UnitHandles.Unit(h), Is.EqualTo(UnitId.Harp));
                    Assert.That(p & 31, Is.EqualTo(s));
                    Assert.That(p >> 5, Is.EqualTo(g));
                }
        }
    }
}
