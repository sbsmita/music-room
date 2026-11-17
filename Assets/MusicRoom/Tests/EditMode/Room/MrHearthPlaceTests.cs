// Where the mixed reality fireplace stands (Room/Decor/MrHearthPlace.cs), in made-up rooms: a plain box room puts it on the
// wall in front, centred, facing the player, on the floor, inside the room and a metre clear of the head; a door, a low
// window, a sofa, a hung painting or an instrument on that wall pushes it along the wall onto free space (never overlapping
// them); an instrument between the player and the wall never hides the fire when another spot shows it; a wall too close
// or behind the player is not used; with no walls it stands on the floor about 2.5 m in front facing the player, turned
// aside when an instrument stands there; never more than MaxAngle off the view.
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Room
{
    public class MrHearthPlaceTests
    {
        const float Floor = 0.1f;

        /// <summary>A box room w (x) by d (z) about (cx, cz), 2.5 m high, the player at <paramref name="head"/> facing +z.</summary>
        static MrHearthPlace.Scene BoxRoom(float w, float d, Vector3 head, float cx = 0f, float cz = 0f)
        {
            var s = new MrHearthPlace.Scene { Head = head, Forward = Vector3.forward, Floor = Floor };
            const float h = 2.5f;
            float y = Floor + h / 2f;
            s.Walls.Add(new RealWall { Centre = new Vector3(cx, y, cz + d / 2f), Inward = Vector3.back, Size = new Vector2(w, h) });    // front
            s.Walls.Add(new RealWall { Centre = new Vector3(cx, y, cz - d / 2f), Inward = Vector3.forward, Size = new Vector2(w, h) }); // back
            s.Walls.Add(new RealWall { Centre = new Vector3(cx + w / 2f, y, cz), Inward = Vector3.left, Size = new Vector2(d, h) });    // right
            s.Walls.Add(new RealWall { Centre = new Vector3(cx - w / 2f, y, cz), Inward = Vector3.right, Size = new Vector2(d, h) });   // left
            return s;
        }

        static readonly Vector3 Seat = new Vector3(0f, Floor + 1.2f, 0f);

        static void AssertValid(MrHearthPlace.Scene s, MrHearthPlace.Spot spot)
        {
            Assert.IsTrue(spot.Found);
            Assert.That(spot.Pose.position.y, Is.EqualTo(Floor).Within(1e-4f), "on the floor");
            Assert.That(MrHearthPlace.FlatDistance(spot.Pose, s.Head, spot.Size), Is.GreaterThanOrEqualTo(MrHearthPlace.HeadClear - 1e-3f), "a metre clear of the head");
            Assert.IsTrue(MrHearthPlace.InsideWalls(s, spot.Pose, spot.Size), "inside the room");
            Assert.That(spot.Angle, Is.LessThanOrEqualTo(MrHearthPlace.MaxAngle + 1e-3f));
            Vector3 toPlayer = s.Head - spot.Pose.position; toPlayer.y = 0f;
            Assert.That(Vector3.Dot(spot.Inward, toPlayer.normalized), Is.GreaterThan(0.2f), "it faces the room the player is in");
            foreach (Bounds r in s.Rigs) Assert.IsFalse(MrHearthPlace.Overlaps(spot.Pose, r, MrHearthPlace.RigMargin - 0.01f, spot.Size), "never on an instrument");
            foreach (Bounds f in s.Frames) Assert.IsFalse(MrHearthPlace.Overlaps(spot.Pose, f, MrHearthPlace.FrameMargin - 0.01f, spot.Size), "never over a painting");
            foreach (RealWallBlock b in s.Blocks)
            {
                Assert.IsFalse(MrHearthPlace.Overlaps(spot.Pose, b, MrHearthPlace.BlockMargin - 0.01f, spot.Size), "never in a door, a window or furniture");
            }
        }

        [Test]
        public void APlainRoomPutsItOnTheWallInFrontCentredAndFacingThePlayer()
        {
            var s = BoxRoom(5f, 6f, Seat);
            var spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] plain room: {spot}");
            AssertValid(s, spot);
            Assert.IsTrue(spot.OnWall);
            Assert.That(spot.Wall, Is.EqualTo(0), "the wall in front");
            Assert.That(spot.Pose.position.z, Is.EqualTo(3f - MrHearthPlace.WallGap).Within(1e-3f), "its back to the wall");
            Assert.That(Mathf.Abs(spot.Pose.position.x), Is.LessThan(0.06f), "centred in front of the player");
            Assert.That(Vector3.Angle(spot.Inward, Vector3.back), Is.LessThan(0.5f), "facing into the room");
            Assert.IsFalse(spot.Hidden);
            Assert.That(spot.Angle, Is.LessThan(3f));
        }

        [Test]
        public void ADoorAWindowASofaOrAPaintingPushItOntoFreeWall()
        {
            var s = BoxRoom(6f, 6f, Seat);
            // a door in the middle of the front wall, a low window to its right (the hearth fits neither under nor in front)
            s.Blocks.Add(new RealWallBlock { Centre = new Vector3(0f, Floor + 1.0f, 3f), Half = new Vector3(0.45f, 1.0f, 0.12f), Rotation = Quaternion.LookRotation(Vector3.back) });
            s.Blocks.Add(new RealWallBlock { Centre = new Vector3(1.4f, Floor + 1.4f, 3f), Half = new Vector3(0.5f, 0.5f, 0.12f), Rotation = Quaternion.LookRotation(Vector3.back) });
            var spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] door + window: {spot}");
            AssertValid(s, spot);
            Assert.IsTrue(spot.OnWall);
            Assert.That(spot.Wall, Is.EqualTo(0), "still the front wall: there is room left of the door");
            Assert.That(spot.Pose.position.x, Is.LessThan(-1.4f), "to the door's left");
            // a sofa against the left part, a painting hung over what is left: no front-wall spot; another wall
            s.Blocks.Add(new RealWallBlock { Centre = new Vector3(-2.2f, Floor + 0.4f, 2.55f), Half = new Vector3(0.8f, 0.45f, 0.4f), Rotation = Quaternion.identity });
            s.Frames.Add(new Bounds(new Vector3(-1.0f, Floor + 1.4f, 2.98f), new Vector3(0.5f, 0.45f, 0.03f)));
            spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] door + window + sofa + painting: {spot}");
            AssertValid(s, spot);
            Assert.IsTrue(spot.OnWall);
            Assert.That(spot.Wall, Is.Not.EqualTo(0));
            Assert.That(spot.Wall, Is.Not.EqualTo(1), "not the wall behind the player");
        }

        [Test]
        public void AnInstrumentNeverStandsInItNorHidesTheFire()
        {
            var s = BoxRoom(5f, 5f, Seat);
            // the piano straight ahead (1.4 m wide, 1 m high), the drums ahead left: what stands in front of the player in MR
            s.Rigs.Add(new Bounds(new Vector3(0f, Floor + 0.5f, 0.9f), new Vector3(1.45f, 1.0f, 0.5f)));
            s.Rigs.Add(new Bounds(new Vector3(-1.3f, Floor + 0.55f, 0.9f), new Vector3(1.1f, 1.1f, 1.0f)));
            var spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] piano + drums ahead: {spot}");
            AssertValid(s, spot);
            Assert.IsTrue(spot.OnWall);
            Assert.IsFalse(spot.Hidden, "the flames are in sight");
            Vector3 mid = spot.Pose.position + spot.Inward * 0.36f + Vector3.up * MrHearthPlace.SightY;
            Assert.IsFalse(MrHearthPlace.Hides(s, s.Head, mid));
            // an instrument standing against the front wall itself
            s.Rigs.Add(new Bounds(new Vector3(0.4f, Floor + 0.6f, 2.2f), new Vector3(1.2f, 1.2f, 0.6f)));
            spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] + harp at the wall: {spot}");
            AssertValid(s, spot);
        }

        [Test]
        public void AWallTooCloseOrBehindThePlayerIsNotUsed()
        {
            // the front wall 1.3 m ahead: the hearth would come within 0.6 m of the head
            var s = BoxRoom(4f, 6f, Seat, cz: -1.7f);
            var spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] front wall close: {spot}");
            AssertValid(s, spot);
            Assert.That(spot.Wall, Is.Not.EqualTo(0), "not the near wall in front");
            Vector3 toSpot = spot.Pose.position - s.Head; toSpot.y = 0f;
            Assert.That(Vector3.Dot(toSpot, Vector3.forward), Is.GreaterThan(-0.5f), "not behind the player");
        }

        [Test]
        public void NoWallsPutsItOnTheFloorInFrontFacingThePlayer()
        {
            var s = new MrHearthPlace.Scene { Head = Seat, Forward = new Vector3(0.6f, -0.2f, 0.8f), Floor = Floor };
            var spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] no walls: {spot}");
            AssertValid(s, spot);
            Assert.IsFalse(spot.OnWall);
            Assert.That(spot.Distance, Is.EqualTo(MrHearthPlace.FallbackDistance).Within(0.01f));
            Assert.That(spot.Angle, Is.LessThan(1f), "straight ahead");
            Vector3 fwd = new Vector3(0.6f, 0f, 0.8f).normalized;
            Assert.That(Vector3.Angle(spot.Inward, -fwd), Is.LessThan(0.5f), "facing the player");
            // an instrument where it would stand, and one between: turned aside, clear and in sight
            s.Rigs.Add(new Bounds(Seat + fwd * 2.5f + Vector3.down * 0.7f, new Vector3(1.2f, 1.2f, 1.2f)));
            s.Rigs.Add(new Bounds(Seat + fwd * 1.0f + Vector3.down * 0.6f, new Vector3(1.4f, 1.0f, 0.6f)));
            spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] no walls, instruments ahead: {spot}");
            AssertValid(s, spot);
            Assert.IsFalse(spot.Hidden);
            Assert.That(spot.Angle, Is.GreaterThan(10f));
        }

        [Test]
        public void ARoomWithNoFreeWallFallsBackToTheFloorInsideIt()
        {
            var s = BoxRoom(7f, 7f, Seat);
            // every wall covered by doors from end to end
            for (int w = 0; w < 4; w++)
            {
                RealWall wall = s.Walls[w];
                s.Blocks.Add(new RealWallBlock { Centre = new Vector3(wall.Centre.x, Floor + 1f, wall.Centre.z), Half = new Vector3(3.5f, 1f, 0.12f), Rotation = Quaternion.LookRotation(wall.Inward) });
            }
            var spot = MrHearthPlace.Find(s);
            Debug.Log($"[MrFire] no free wall: {spot}");
            AssertValid(s, spot);
            Assert.IsFalse(spot.OnWall);
        }
    }
}
