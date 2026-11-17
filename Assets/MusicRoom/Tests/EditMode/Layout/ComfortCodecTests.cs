// The comfort offsets (ComfortCodec): an offset from the default pose in front of the player, in the default pose's own
// bearing frame, round-trips through Between / Apply for every bearing; the text format round-trips and drops damaged
// entries; the comfortable range clamps and judges old placements; the settings keys per layout; the real-hands switch
// defaults to the drawn hands for old saves and persists.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public class ComfortCodecTests
    {
        static readonly Vector2[] s_centres = { new Vector2(0f, 0.5f), new Vector2(0.6f, 0.2f), new Vector2(-0.55f, -0.1f), new Vector2(0.01f, 0.01f) };

        [Test]
        public void AnOffsetRoundTripsInEveryBearingFrame()
        {
            foreach (Vector2 c in s_centres)
            {
                Vector2 b = ComfortCodec.Bearing(c);
                Assert.That(b.magnitude, Is.EqualTo(1f).Within(1e-5f));
                var def = new SeatPose { Local = new Vector3(c.x, 0.72f, c.y), Yaw = 20f };
                var o = new ComfortOffset(0.12f, -0.07f, 0.05f, -15f);
                SeatPose moved = ComfortCodec.Apply(def, o, b);
                ComfortOffset back = ComfortCodec.Between(def, moved, b);
                Assert.That(back.Distance, Is.EqualTo(o.Distance).Within(1e-5f));
                Assert.That(back.Side, Is.EqualTo(o.Side).Within(1e-5f));
                Assert.That(back.Height, Is.EqualTo(o.Height).Within(1e-5f));
                Assert.That(back.Yaw, Is.EqualTo(o.Yaw).Within(1e-4f));
                // + distance moves away from the head along the bearing; + side to the player's right of it
                Vector2 d = new Vector2(moved.Local.x - def.Local.x, moved.Local.z - def.Local.z);
                Assert.That(Vector2.Dot(d, b), Is.EqualTo(0.12f).Within(1e-5f));
                Assert.That(Vector2.Dot(d, new Vector2(b.y, -b.x)), Is.EqualTo(-0.07f).Within(1e-5f));
            }
            Assert.That(ComfortCodec.Bearing(Vector2.zero), Is.EqualTo(new Vector2(0f, 1f)), "a centre at the head: straight ahead");
        }

        [Test]
        public void ARoundTripThroughTheSeatFrameKeepsTheTweakAtAnotherSeat()
        {
            // The same offset applied at two seats lands at the same place relative to each seat.
            var def = new SeatPose { Local = new Vector3(0f, 0.72f, 0.3f), Yaw = 0f };
            Vector2 b = ComfortCodec.Bearing(new Vector2(0f, 0.375f));
            var o = new ComfortOffset(0.1f, 0.05f, 0.04f, 10f);
            SeatPose sp = ComfortCodec.Apply(def, o, b);
            foreach (float seatYaw in new[] { 0f, 1.2f, -2.5f })
            {
                Vector3 head = new Vector3(0.4f, 1.15f, -0.6f);
                ArrangementCodec.FromSeat(sp, head, seatYaw, out Vector3 world, out float worldYaw);
                SeatPose again = ArrangementCodec.ToSeat(world, worldYaw, head, seatYaw);
                ComfortOffset o2 = ComfortCodec.Between(def, again, b);
                Assert.That(o2.Distance, Is.EqualTo(o.Distance).Within(1e-4f));
                Assert.That(o2.Side, Is.EqualTo(o.Side).Within(1e-4f));
                Assert.That(o2.Yaw, Is.EqualTo(o.Yaw).Within(1e-3f));
            }
        }

        [Test]
        public void TheTextRoundTripsAndDropsDamagedEntries()
        {
            var offs = new Dictionary<RigId, ComfortOffset>
            {
                [RigId.Piano] = new ComfortOffset(0.1f, -0.02f, 0.03f, 4.5f),
                [RigId.Bowls] = new ComfortOffset(-0.05f, 0.2f, 0f, -30f),
            };
            string text = ComfortCodec.Encode(offs);
            Assert.That(text, Is.EqualTo("c1;Piano:0.1,-0.02,0.03,4.5;Bowls:-0.05,0.2,0,-30"));
            var back = new Dictionary<RigId, ComfortOffset>();
            Assert.That(ComfortCodec.Decode(text, back), Is.EqualTo(2));
            Assert.That(back[RigId.Bowls].Side, Is.EqualTo(0.2f));
            Assert.That(ComfortCodec.Decode("c1;Piano:1,2;Nope:0,0,0,0;Harp:0.1,0,0,0;Drums:NaN,0,0,0;Bowls:99,0,0,0", back), Is.EqualTo(1));
            Assert.That(back.ContainsKey(RigId.Harp));
            Assert.That(ComfortCodec.Decode("a1;Piano:0,0.72,0.45,0", back), Is.EqualTo(0), "an old placement is not an offset");
            Assert.That(ComfortCodec.Decode("", back), Is.EqualTo(0));
            Assert.That(ComfortCodec.Decode(null, back), Is.EqualTo(0));
            Assert.That(ComfortCodec.Encode(new Dictionary<RigId, ComfortOffset>()), Is.EqualTo(""));
        }

        [Test]
        public void TheComfortableRangeClampsAndJudgesOldPlacements()
        {
            var far = new ComfortOffset(2f, -1f, 0.9f, 170f);
            ComfortOffset c = ComfortCodec.Clamp(far, main: false);
            Assert.That(c.Distance, Is.EqualTo(ComfortCodec.MaxFarther));
            Assert.That(c.Side, Is.EqualTo(-ComfortCodec.MaxSide));
            Assert.That(c.Height, Is.EqualTo(ComfortCodec.MaxHeight));
            Assert.That(c.Yaw, Is.EqualTo(170f).Within(1e-4f), "any turn is kept: an instrument spins all the way round");
            Assert.That(ComfortCodec.Clamp(new ComfortOffset(0f, 0f, 0f, 200f), false).Yaw, Is.EqualTo(-160f).Within(1e-3f), "wrapped, not clamped");
            Assert.That(ComfortCodec.Clamp(new ComfortOffset(0f, 0.5f, 0f, 0f), main: true).Side, Is.EqualTo(ComfortCodec.MaxSideMain), "the main one stays centred");
            Assert.That(ComfortCodec.Clamp(new ComfortOffset(-1f, 0f, 0f, 0f), false).Distance, Is.EqualTo(-ComfortCodec.MaxCloser));
            Assert.IsFalse(ComfortCodec.Plausible(far, false));
            Assert.IsTrue(ComfortCodec.Plausible(new ComfortOffset(0.2f, 0.4f, 0.1f, 30f), false));
            Assert.IsFalse(ComfortCodec.Plausible(new ComfortOffset(0.2f, 0.4f, 0.1f, 30f), true), "0.4 m aside is too far for the main one");
            Assert.IsTrue(new ComfortOffset(0.002f, -0.003f, 0.001f, 0.2f).IsZero);
            Assert.IsFalse(new ComfortOffset(0.01f, 0f, 0f, 0f).IsZero);
        }

        [Test]
        public void TheSettingsKeysAndTheRealHandsSwitch()
        {
            Assert.That(AppSettings.ComfortKey(LayoutMode.All, InstrumentKind.Harp), Is.EqualTo("musicroom.comfort.all"));
            Assert.That(AppSettings.ComfortKey(LayoutMode.Single, InstrumentKind.Marimba), Is.EqualTo("musicroom.comfort.single.marimba"));
            var store = new MemorySettingsStore();
            var s = new AppSettings(store);
            Assert.IsFalse(s.MrRealHands, "an old save without the key: the drawn hands");
            s.MrRealHands = true;
            Assert.IsTrue(new AppSettings(store).MrRealHands, "persisted");
            Assert.That(store.GetInt(AppSettings.KeyMrRealHands, -1), Is.EqualTo(1));
            s.SetComfort(LayoutMode.All, InstrumentKind.Piano, "c1;Piano:0.1,0,0,0");
            Assert.That(s.GetComfort(LayoutMode.All, InstrumentKind.Drums), Is.EqualTo("c1;Piano:0.1,0,0,0"), "one key for My set");
            s.SetComfort(LayoutMode.All, InstrumentKind.Piano, "");
            Assert.IsFalse(store.HasKey(AppSettings.ComfortKey(LayoutMode.All, InstrumentKind.Piano)));
        }
    }
}
