// Snow in the whole room, its numbers (Room/Decor/RoomSnowModel.cs, the CPU side of MusicRoom/RoomSnow): the volume is the
// built room's box under the room's pose (any turn), or a round snowfall about the player in mixed reality on a plausible
// floor; every flake that shows lies inside it, all around the head (every direction), never at the head or the eyes, thin
// over the playing area in front; flakes land on the floor, lie there and melt; the SNOW level brings them in; the flakes
// are anchored to the world when the MR volume moves; a playback's gust swirls them within the volume and nothing moves
// at rest; the playing instrument's tint comes and goes back to exactly nothing; the shader's constants are the model's;
// a frame allocates nothing.
using System.IO;
using System.Text.RegularExpressions;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.TestTools.Utils;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class RoomSnowModelTests
    {
        const float Dt = 1f / 72f;
        static readonly RoomSnowModel.Seed[] Seeds = MakeSeeds();

        static RoomSnowModel.Seed[] MakeSeeds()
        {
            var s = new RoomSnowModel.Seed[RoomSnowModel.Count];
            RoomSnowModel.Seeds(0x5a0cu, s);
            return s;
        }

        /// <summary>The turned VR room (RoomFrame's pose), the player at the seat looking ahead, snowing fully.</summary>
        static RoomSnowModel.Frame Vr(float time, float level = 1f)
        {
            return new RoomSnowModel.Frame
            {
                Time = time, Wind = 0.32f * time, WindVel = 0.32f, Level = level,
                Vol = RoomSnowModel.ForRoom(RoomFrame.Position, RoomFrame.Yaw, RoomFrame.LocalBox),
                Head = new Vector3(0f, 1.2f, 0f), Forward = Vector3.forward,
            };
        }

        static RoomSnowModel.Frame Mr(float time, Vector3 centre)
        {
            Vector3 head = new Vector3(0.4f, 1.25f, -0.3f);
            return new RoomSnowModel.Frame
            {
                Time = time, Wind = 0.32f * time, WindVel = 0.32f, Level = 1f,
                Vol = RoomSnowModel.ForMr(centre, head.y, 0f),
                Head = head, Forward = Vector3.right,
            };
        }

        [Test]
        public void TheVrVolumeIsTheBuiltRoomsBoxUnderItsPoseInsetFromTheWalls()
        {
            RoomSnowModel.Volume v = RoomSnowModel.ForRoom(RoomFrame.Position, RoomFrame.Yaw, RoomFrame.LocalBox);
            Bounds w = RoomFrame.WorldBox;
            Assert.That(v.Centre.x, Is.EqualTo(w.center.x).Within(1e-4f));
            Assert.That(v.Centre.z, Is.EqualTo(w.center.z).Within(1e-4f));
            Assert.That(v.Floor, Is.EqualTo(w.min.y).Within(1e-4f), "from the floor");
            Assert.That(v.Top, Is.EqualTo(w.max.y - RoomSnowModel.CeilingInset).Within(1e-4f), "to just under the ceiling");
            Assert.That(v.Half.x, Is.EqualTo(RoomFrame.LocalBox.extents.x - RoomSnowModel.WallInset).Within(1e-4f));
            Assert.That(v.Half.z, Is.EqualTo(RoomFrame.LocalBox.extents.z - RoomSnowModel.WallInset).Within(1e-4f));
            Assert.IsFalse(v.Round);
            // the seat is inside, the fire wall's far corner too (the room is turned: never hard-coded)
            Assert.IsTrue(v.Contains(new Vector3(0f, 1.2f, 0f), 0f), "the player sits inside it");
            Assert.IsTrue(v.Contains(RoomFrame.ToWorld(new Vector3(-3f, 1f, -3.9f)), 0f));
            Assert.IsFalse(v.Contains(RoomFrame.ToWorld(new Vector3(0f, 1f, 1.5f)), 0f), "not behind the room's wall");
            // a room turned any other way: its own box, turned
            var root = new Vector3(1f, 0.2f, -2f);
            RoomSnowModel.Volume t = RoomSnowModel.ForRoom(root, 37f, RoomFrame.LocalBox);
            Quaternion r = Quaternion.Euler(0f, 37f, 0f);
            foreach (Vector3 corner in new[] { new Vector3(-3.05f, 0.1f, -3.95f), new Vector3(3.05f, 2.7f, 0.98f), new Vector3(-3.05f, 1.5f, 0.98f) })
                Assert.IsTrue(t.Contains(root + r * corner, 1e-3f), "corner " + corner);
            Assert.IsFalse(t.Contains(root + r * new Vector3(3.3f, 1f, 0f), 0f), "beyond its wall");
        }

        [Test]
        public void TheMixedRealityVolumeIsRoundAboutThePlayerOnAPlausibleFloor()
        {
            RoomSnowModel.Volume v = RoomSnowModel.ForMr(new Vector3(1f, 1.3f, 2f), 1.3f, 0.05f);
            Assert.IsTrue(v.Round);
            Assert.That(v.Floor, Is.EqualTo(0.05f).Within(1e-5f), "the real floor");
            Assert.That(v.Top, Is.EqualTo(0.05f + RoomSnowModel.MrHeight).Within(1e-4f));
            Assert.That(v.Half.x, Is.EqualTo(RoomSnowModel.MrRadius));
            Assert.That(new Vector2(v.Centre.x, v.Centre.z), Is.EqualTo(new Vector2(1f, 2f)));
            // no scene floor (0 = the tracking floor) and a head 3 m above it, or under it: a seated estimate instead
            Assert.That(RoomSnowModel.MrFloor(3f, 0f), Is.EqualTo(3f - RoomSnowModel.SeatedHead).Within(1e-5f));
            Assert.That(RoomSnowModel.MrFloor(0.2f, 0f), Is.EqualTo(0.2f - RoomSnowModel.SeatedHead).Within(1e-5f));
            Assert.That(RoomSnowModel.MrFloor(1.6f, 0f), Is.EqualTo(0f), "standing over the real floor");
            // a tall player: the top keeps above the head
            RoomSnowModel.Volume tall = RoomSnowModel.ForMr(Vector3.zero, 2.2f, 0f);
            Assert.That(tall.Top, Is.GreaterThanOrEqualTo(2.2f + RoomSnowModel.MrAboveHead - 1e-4f));
        }

        static void Check(in RoomSnowModel.Frame f, Vector3 eye, out int shown, int[] sectors, out float front, out float back, out int landed)
        {
            shown = landed = 0;
            front = back = 0f;
            for (int i = 0; i < Seeds.Length; i++)
            {
                RoomSnowModel.FlakeAt(Seeds[i], f, eye, out Vector3 p, out float a, out float melt);
                if (a <= 0f) continue;
                shown++;
                Assert.IsTrue(f.Vol.Contains(p, 1e-3f), $"flake {i} at {p} is in the volume");
                float dh = Vector3.Distance(p, f.Head);
                Assert.That(dh, Is.GreaterThan(RoomSnowModel.HeadClear), $"flake {i} keeps off the head");
                Assert.That(Vector3.Distance(p, eye), Is.GreaterThan(RoomSnowModel.EyeClear), $"flake {i} keeps off the eye");
                if (melt > 0f) { landed++; Assert.That(p.y, Is.EqualTo(f.Vol.Floor + 0.004f).Within(1e-3f), "lies on the floor"); }
                Vector3 d = p - f.Head;
                if (sectors != null && new Vector2(d.x, d.z).magnitude > 0.3f)
                {
                    float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    sectors[Mathf.Clamp((int)((ang + 180f) / 45f), 0, 7)]++;
                }
                // the playing area ahead and its mirror behind, same size
                float ahead = Vector3.Dot(d, f.Forward);
                Vector3 side = d - f.Forward * ahead;
                bool inBand = side.y < -0.2f && side.y > -0.7f && new Vector2(side.x, side.z).magnitude < 0.5f;
                if (inBand && ahead > 0.2f && ahead < 0.8f) front += a;
                if (inBand && ahead < -0.2f && ahead > -0.8f) back += a;
            }
        }

        [Test]
        public void InTheVrRoomTheFlakesFallAllAroundTheHeadNeverAtItAndThinOverThePlayingArea()
        {
            float front = 0f, back = 0f;
            int landed = 0, shown = 0;
            var sectors = new int[8];
            for (int k = 0; k < 12; k++)
            {
                RoomSnowModel.Frame f = Vr(30f + 7.3f * k);
                Check(f, f.Head + new Vector3(0.032f, 0f, 0f), out int s, sectors, out float fr, out float bk, out int ld);
                shown += s; front += fr; back += bk; landed += ld;
            }
            Debug.Log($"[RoomSnow] VR: {shown / 12} flakes showing a frame, sectors {string.Join(",", sectors)}, playing area {front:F1} vs behind {back:F1} (opacity), landed {landed / 12}");
            Assert.That(shown / 12, Is.GreaterThan(RoomSnowModel.Count / 2), "most flakes show while it snows fully");
            for (int s = 0; s < 8; s++) Assert.That(sectors[s], Is.GreaterThan(12 * 60), "360 degrees: every eighth of the room around the head (" + s + ")");
            Assert.That(landed, Is.GreaterThan(0), "some lie on the floor, melting");
            Assert.That(front, Is.LessThan(back * 0.5f), "far fainter over the hands' playing area than behind the player");
        }

        [Test]
        public void InMixedRealityTheyFallInARoundVolumeAboutThePlayer()
        {
            var sectors = new int[8];
            int shown = 0;
            for (int k = 0; k < 8; k++)
            {
                RoomSnowModel.Frame f = Mr(11f + 5.1f * k, new Vector3(0.4f, 0f, -0.3f));
                Check(f, f.Head, out int s, sectors, out _, out _, out _);
                shown += s;
                for (int i = 0; i < Seeds.Length; i++)
                {
                    RoomSnowModel.FlakeAt(Seeds[i], f, f.Head, out Vector3 p, out float a, out _);
                    Vector3 d = p - f.Vol.Centre;
                    if (a > 0f) Assert.That(new Vector2(d.x, d.z).magnitude, Is.LessThan(RoomSnowModel.MrRadius), "round: the corners never show");
                }
            }
            Assert.That(shown / 8, Is.GreaterThan(RoomSnowModel.Count / 3));
            for (int s = 0; s < 8; s++) Assert.That(sectors[s], Is.GreaterThan(8 * 50), "all around (" + s + ")");
        }

        [Test]
        public void AFlakeFallsGentlyLandsLiesAMomentAndMeltsThenFallsAgain()
        {
            RoomSnowModel.Seed s = Seeds[17];
            float lastY = float.PositiveInfinity, maxMelt = 0f;
            bool fell = false, landed = false, again = false, lying = false;
            Vector3 landedAt = default;
            for (float t = 0f; t < 40f; t += 0.05f)
            {
                RoomSnowModel.Frame f = Vr(100f + t);
                f.Head = new Vector3(50f, 50f, 50f);   // far away: nothing hides it
                RoomSnowModel.FlakeAt(s, f, f.Head, out Vector3 p, out _, out float melt);
                if (melt > 0f)
                {
                    if (!lying) landedAt = p;
                    landed = lying = true;
                    maxMelt = Mathf.Max(maxMelt, melt);
                    Assert.That(Vector3.Distance(p, landedAt), Is.LessThan(0.01f), "it lies still where it landed");
                }
                else if (p.y > lastY + 0.5f) { lying = false; if (landed) again = true; }
                else
                {
                    if (lastY < float.PositiveInfinity) Assert.That(lastY - p.y, Is.LessThan(RoomSnowModel.FallMax * 1.3f * 0.05f + 0.02f), "gently");
                    fell = true;
                }
                lastY = p.y;
            }
            Assert.IsTrue(fell && landed && again, "falls, lands, melts away, comes again from the top");
            Assert.That(maxMelt, Is.GreaterThan(0.9f));
        }

        [Test]
        public void TheSnowSwitchLevelBringsTheFlakesInOneByOne()
        {
            int Count(float level)
            {
                RoomSnowModel.Frame f = Vr(50f, level);
                f.Head = new Vector3(50f, 50f, 50f);
                int n = 0;
                for (int i = 0; i < Seeds.Length; i++) { RoomSnowModel.FlakeAt(Seeds[i], f, f.Head, out _, out float a, out _); if (a > 0f) n++; }
                return n;
            }
            int none = Count(0f), half = Count(0.5f), full = Count(1f);
            Assert.That(none, Is.Zero, "off: not one flake");
            Assert.That(half, Is.InRange(full / 3, full * 2 / 3), "half way: about half");
            Assert.That(full, Is.GreaterThan(half));
        }

        [Test]
        public void InMixedRealityTheFlakesStayPutWhenTheVolumeFollowsThePlayer()
        {
            RoomSnowModel.Frame a = Mr(20f, new Vector3(0.4f, 0f, -0.3f));
            RoomSnowModel.Frame b = Mr(20f, new Vector3(0.9f, 0f, -0.1f));   // the snowfall's centre moved half a metre
            b.Head = a.Head;
            int same = 0, n = 0;
            for (int i = 0; i < Seeds.Length; i++)
            {
                RoomSnowModel.FlakeAt(Seeds[i], a, a.Head, out Vector3 pa, out float aa, out _);
                RoomSnowModel.FlakeAt(Seeds[i], b, b.Head, out Vector3 pb, out float ab, out _);
                if (aa <= 0f || ab <= 0f) continue;
                n++;
                if (Vector3.Distance(pa, pb) < 1e-3f) same++;
            }
            Assert.That(same, Is.GreaterThan(n * 9 / 10), "the world holds the flakes, not the box");
        }

        [Test]
        public void AGustSwirlsTheFlakesWithinTheRoomAndNothingMovesAtRest()
        {
            RoomSnowModel.Frame calm = Vr(70f);
            RoomSnowModel.Frame gust = calm;
            gust.Show = new Vector4(1f, 2.1f, RoomSnowModel.Whirl, RoomSnowModel.Lift);
            int moved = 0, n = 0;
            for (int i = 0; i < Seeds.Length; i++)
            {
                RoomSnowModel.FlakeAt(Seeds[i], calm, calm.Head, out Vector3 p0, out float a0, out float m0);
                RoomSnowModel.Frame zero = calm; zero.Show = Vector4.zero;
                RoomSnowModel.FlakeAt(Seeds[i], zero, zero.Head, out Vector3 pz, out _, out _);
                Assert.That(pz, Is.EqualTo(p0), "exactly the plain snowfall at rest");
                RoomSnowModel.FlakeAt(Seeds[i], gust, gust.Head, out Vector3 p1, out float a1, out float m1);
                if (a1 > 0f) Assert.IsTrue(gust.Vol.Contains(p1, 1e-3f), "inside the room in a gust too");
                if (m0 > 0f) { Assert.That(p1, Is.EqualTo(p0), "a landed flake lies still"); continue; }
                if (a0 <= 0f || a1 <= 0f) continue;
                n++;
                float d = Vector3.Distance(p0, p1);
                if (d > 0.04f) moved++;
                float r = Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(calm.Head.x, calm.Head.z));
                if (r < 2.5f) Assert.That(d, Is.LessThan(RoomSnowModel.EddyMax * 1.2f + RoomSnowModel.Lift + RoomSnowModel.Whirl * r + 0.02f), "a puff, not a jump");
            }
            Assert.That(moved, Is.GreaterThan(n / 2), "most flakes swirl");
        }

        [Test]
        public void TheTintTakesTheInstrumentsColourAndGoesBackToExactlyNothing()
        {
            var m = new RoomSnowModel();
            double now = 5;
            m.Tick(Dt, now);
            Assert.That(m.Tint, Is.EqualTo(Vector4.zero));
            Assert.IsTrue(m.TintResting);
            m.Note(new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Release, Level = 0f }, now);
            Assert.IsTrue(m.TintResting, "a release is no note");
            m.Note(new PlaybackNoteEvent { Kind = InstrumentKind.Piano, Role = EventRole.Onset, Level = 0.9f }, now);
            now += Dt; m.Tick(Dt, now);
            Color gold = SongStars.ColorOf(InstrumentKind.Piano).linear;
            Vector4 t = m.Tint;
            Assert.That(t.w, Is.GreaterThan(0.2f).And.LessThanOrEqualTo(RoomSnowModel.TintMax));
            Assert.That(new Vector3(t.x, t.y, t.z), Is.EqualTo(new Vector3(gold.r, gold.g, gold.b)).Using(Vector3EqualityComparer.Instance), "the piano's gold");
            // a drum ahead of time waits until it sounds
            m.Note(new PlaybackNoteEvent { Kind = InstrumentKind.Drums, Role = EventRole.Onset, Level = 1f, When = now + 0.5 }, now);
            Assert.That(m.PendingCount, Is.EqualTo(1));
            Assert.That(m.Heat(InstrumentKind.Drums), Is.Zero);
            for (int i = 0; i < 40; i++) { now += Dt; m.Tick(Dt, now); }
            Assert.That(m.PendingCount, Is.Zero);
            Assert.That(m.Heat(InstrumentKind.Drums), Is.GreaterThan(0.5f), "it sounded");
            Assert.That(m.Tint.z, Is.GreaterThan(t.z), "bluer with the drums");
            m.Stop();
            for (int i = 0; i < 72 * 15; i++) { now += Dt; m.Tick(Dt, now); }
            Assert.IsTrue(m.TintResting);
            Assert.That(m.Tint, Is.EqualTo(Vector4.zero), "exactly nothing");
            // Stop drops what waits
            m.Note(new PlaybackNoteEvent { Kind = InstrumentKind.Harp, Role = EventRole.Onset, Level = 1f, When = now + 2 }, now);
            m.Stop();
            for (int i = 0; i < 72 * 4; i++) { now += Dt; m.Tick(Dt, now); }
            Assert.That(m.Heat(InstrumentKind.Harp), Is.Zero);
            Assert.IsTrue(m.TintResting);
        }

        [Test]
        public void TheShadersConstantsAreTheModels()
        {
            string text = File.ReadAllText("Assets/MusicRoom/Room/Shaders/RoomSnow.shader");
            float Def(string name)
            {
                Match m = Regex.Match(text, @"#define\s+" + name + @"\s+([0-9.]+)");
                Assert.IsTrue(m.Success, name);
                return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            Assert.That(Def("SIZE_MIN"), Is.EqualTo(RoomSnowModel.SizeMin));
            Assert.That(Def("SIZE_MAX"), Is.EqualTo(RoomSnowModel.SizeMax));
            Assert.That(Def("FALL_MIN"), Is.EqualTo(RoomSnowModel.FallMin));
            Assert.That(Def("FALL_MAX"), Is.EqualTo(RoomSnowModel.FallMax));
            Assert.That(Def("LAND_REST"), Is.EqualTo(RoomSnowModel.LandRest));
            Assert.That(Def("DRAFT"), Is.EqualTo(RoomSnowModel.Draft));
            Assert.That(Def("HEAD_CLEAR"), Is.EqualTo(RoomSnowModel.HeadClear));
            Assert.That(Def("HEAD_FULL"), Is.EqualTo(RoomSnowModel.HeadFull));
            Assert.That(Def("EYE_CLEAR"), Is.EqualTo(RoomSnowModel.EyeClear));
            Assert.That(Def("EYE_FULL"), Is.EqualTo(RoomSnowModel.EyeFull));
            Assert.That(Def("CLEAR_AHEAD"), Is.EqualTo(RoomSnowModel.ClearAhead));
            Assert.That(Def("CLEAR_DOWN"), Is.EqualTo(RoomSnowModel.ClearDown));
            Assert.That(Def("CLEAR_KEEP"), Is.EqualTo(RoomSnowModel.ClearKeep));
            Assert.That(Def("WALL_FADE"), Is.EqualTo(RoomSnowModel.WallFade));
            Assert.That(Def("FIRE_GONE"), Is.EqualTo(RoomSnowModel.FireGone));
            Assert.That(Def("FIRE_MELT"), Is.EqualTo(RoomSnowModel.FireMelt));
            Assert.That(Def("FIRE_WARM"), Is.EqualTo(RoomSnowModel.FireWarm));
            Assert.That(Def("WHIRL_REACH"), Is.EqualTo(RoomSnowModel.WhirlReach));
            Assert.That(Def("EDDY_MIN"), Is.EqualTo(RoomSnowModel.EddyMin));
            Assert.That(Def("EDDY_MAX"), Is.EqualTo(RoomSnowModel.EddyMax));
            Assert.That(Def("TINT_REACH"), Is.EqualTo(RoomSnowModel.TintReach));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            StringAssert.Contains("#define CLEAR_R float3(" + RoomSnowModel.ClearRx.ToString(inv) + ", " + RoomSnowModel.ClearRy.ToString(inv) + ", " + RoomSnowModel.ClearRz.ToString(inv) + ")", text);
        }

        [Test]
        public void AFrameAllocatesNothing()
        {
            var m = new RoomSnowModel();
            RoomSnowModel.Frame f = Vr(12f);
            f.Show = new Vector4(0.7f, 1f, 0.2f, 0.1f);
            var e = new PlaybackNoteEvent { Kind = InstrumentKind.Harp, Role = EventRole.Onset, Level = 0.8f };
            double now = 1;
            float sink = 0f;
            m.Note(e, now);
            m.Tick(Dt, now);
            RoomSnowModel.FlakeAt(Seeds[0], f, f.Head, out _, out _, out _);
            Assert.That(() =>
            {
                for (int k = 0; k < 50; k++)
                {
                    now += Dt;
                    m.Note(e, now);
                    m.Tick(Dt, now);
                    sink += m.Tint.w;
                    for (int i = 0; i < 40; i++) { RoomSnowModel.FlakeAt(Seeds[i], f, f.Head, out Vector3 p, out float a, out _); sink += p.x + a; }
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(float.IsNaN(sink), Is.False);
        }
    }
}
