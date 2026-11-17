// A fireplace in the player's own room (Room/Decor/MrFireplace.cs, Room/Decor/MrHearthPlace.cs, Room/World/HearthBuilder.cs):
//   - in one of MRUK's recorded rooms: entering mixed reality still turns FIRE off; FIRE on lights the hearth against a real
//     wall in front of the player (on the floor, inside the room, a metre clear of the head, clear of every door, window,
//     piece of furniture and instrument), growing in and then burning (9 renderers, its light on), the crackle placed at
//     it; FIRE off puts it out with a short fade; on again lights it again; leaving mixed reality hides it at once, the
//     crackle goes back to the VR room's fireplace and the VR room's own fire burns;
//   - with no scene model (no walls): on the floor about 2.5 m in front, facing the player;
//   - in a simulated room (a box of four walls about the player, no MRUK): on the wall in front;
//   - a frame of it allocates nothing (burning, out, in the VR room).
// The [PERF] numbers: MrFireplacePerfTests. The look: MrFireplaceShotsTests (GPU).
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.MR
{
    /// <summary>A scene model of the test's own: the walls given (none: no room), no blocks, no depth sensor.</summary>
    public sealed class FakeRealWalls : IRealWalls
    {
        public readonly List<RealWall> Walls = new List<RealWall>();
        public readonly List<RealWallBlock> Blocks = new List<RealWallBlock>();
        public int Refreshes;
        public bool Refresh() { Refreshes++; return Walls.Count > 0; }
        public int Version => 1;
        public int WallCount => Walls.Count;
        public RealWall Wall(int i) => Walls[i];
        public int BlockCount => Blocks.Count;
        public RealWallBlock Block(int i) => Blocks[i];
        public bool DepthWall(Ray ray, out Vector3 point, out Vector3 inward) { point = inward = default; return false; }

        /// <summary>Four walls w x d about <paramref name="centre"/> on the floor, 2.5 m high.</summary>
        public static FakeRealWalls Box(Vector3 centre, float floor, float w, float d)
        {
            var f = new FakeRealWalls();
            float y = floor + 1.25f;
            f.Walls.Add(new RealWall { Centre = new Vector3(centre.x, y, centre.z + d / 2f), Inward = Vector3.back, Size = new Vector2(w, 2.5f) });
            f.Walls.Add(new RealWall { Centre = new Vector3(centre.x, y, centre.z - d / 2f), Inward = Vector3.forward, Size = new Vector2(w, 2.5f) });
            f.Walls.Add(new RealWall { Centre = new Vector3(centre.x + w / 2f, y, centre.z), Inward = Vector3.left, Size = new Vector2(d, 2.5f) });
            f.Walls.Add(new RealWall { Centre = new Vector3(centre.x - w / 2f, y, centre.z), Inward = Vector3.right, Size = new Vector2(d, 2.5f) });
            return f;
        }
    }

    /// <summary>The fireplace tests' shared steps.</summary>
    public abstract class MrFireplaceRig : MrTestRig
    {
        protected override bool TableOnEnter => false;
        protected override void Prepare(MemorySettingsStore settings) { settings.SetInt(AppSettings.KeyTourDone, 1); MrFireplace.DebugPlace = true; }
        [TearDown] public void NoDebugLines() => MrFireplace.DebugPlace = false;

        protected RoomModule Music => App.Get<IRoom>() as RoomModule;

        /// <summary>Lights it (FIRE on) and steps until it burns.</summary>
        protected MrFireplace Light()
        {
            Music.Set(AmbienceKind.Fire, true);
            StepFrames(2);
            MrFireplace fire = Music.MrFireplace;
            Assert.IsTrue(fire.Showing, "lighting up");
            Assert.That(fire.Level, Is.GreaterThan(0f).And.LessThan(1f), "a fade, not a pop");
            StepFrames(30);
            Assert.That(fire.Level, Is.EqualTo(1f), "burning");
            return fire;
        }

        protected void AssertValidPlace(MrFireplace fire, IRealWalls walls, bool? onWall)
        {
            MrHearthPlace.Spot spot = fire.Spot;
            Vector3 head = App.Head.position;
            Debug.Log($"[MrFire] {spot}; head {head:F2}, floor {Mr.FloorHeight:F2}");
            Assert.IsTrue(fire.Placed && spot.Found);
            if (onWall.HasValue) Assert.That(spot.OnWall, Is.EqualTo(onWall.Value), onWall.Value ? "against a real wall" : "on the floor in front");
            Transform root = fire.Hearth.Root;
            Assert.That(Vector3.Distance(root.position, spot.Pose.position), Is.LessThan(1e-4f));
            Assert.That(root.position.y, Is.EqualTo(RoomSnowModel.MrFloor(head.y, Mr.FloorHeight)).Within(1e-3f), "standing on the real floor");
            Assert.That(MrHearthPlace.FlatDistance(spot.Pose, head, spot.Size), Is.GreaterThanOrEqualTo(MrHearthPlace.HeadClear - 1e-3f), "a metre clear of the head");
            if (onWall.HasValue) Assert.That(spot.Angle, Is.LessThanOrEqualTo(MrHearthPlace.MaxAngle + 1e-3f), "in front of the player");
            Vector3 toPlayer = head - root.position; toPlayer.y = 0f;
            Assert.That(Vector3.Dot(root.forward, toPlayer.normalized), Is.GreaterThan(0.2f), "facing the player's side");
            var scene = new MrHearthPlace.Scene { Head = head, Forward = App.Head.forward, Floor = root.position.y };
            if (walls != null && walls.Refresh())
            {
                for (int i = 0; i < walls.WallCount; i++) scene.Walls.Add(walls.Wall(i));
                for (int i = 0; i < walls.BlockCount; i++) scene.Blocks.Add(walls.Block(i));
            }
            Assert.IsTrue(MrHearthPlace.InsideWalls(scene, spot.Pose, spot.Size), "inside the room");
            if (onWall.HasValue) Assert.IsFalse(spot.Rough, "a free place");
            foreach (RealWallBlock b in scene.Blocks)
            {
                if (spot.Rough) break;   // no free place in this room: the least bad may stand in furniture
                Assert.IsFalse(MrHearthPlace.Overlaps(spot.Pose, b, 0f, spot.Size), "clear of the room's doors, windows and furniture");
            }
            foreach (Rig r in App.Get<IRigRegistry>().Rigs)
            {
                if (r?.Group == null || !r.Group.gameObject.activeInHierarchy) continue;
                foreach (Renderer h in r.Handles)
                    if (h != null && h.enabled && h.gameObject.activeInHierarchy && h.bounds.size.sqrMagnitude > 1e-6f)
                        Assert.IsFalse(MrHearthPlace.Overlaps(spot.Pose, h.bounds, 0f, spot.Size), r.Id + " stands clear of it");
                Assert.That(MrHearthPlace.FlatDistance(spot.Pose, r.Group.position, spot.Size), Is.GreaterThan(0f), r.Id + "'s origin is not inside it");
            }
            // what shows: the hearth's 9 renderers, its light, the crackle at it
            Assert.IsTrue(root.gameObject.activeInHierarchy);
            Assert.That(fire.Hearth.RendererCount, Is.EqualTo(9));
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>()) Assert.IsTrue(r.enabled, r.name);
            Assert.IsTrue(fire.Hearth.Light.enabled, "its warm light");
            Assert.IsTrue(fire.SoundAtHearth);
            Assert.That(Vector3.Distance(App.Get<IAudioEngine>().Source(SourceId.Fire).transform.position, spot.FirePoint), Is.LessThan(1e-3f), "the crackle from the hearth");
        }

    }

    public class MrFireplaceTests : MrFireplaceRig
    {
        [UnityTest]
        public IEnumerator FireInARealRoomLightsAFireplaceOnAWallAndLeavingRestoresTheRoomsFire()
        {
            RoomModule room = Music;
            room.Set(AmbienceKind.Fire, true);
            yield return EnterRoom("MeshLivingRoom2");
            StepFrames(4);
            MrFireplace fire = room.MrFireplace;
            Assert.IsFalse(room.Get(AmbienceKind.Fire), "entering mixed reality still turns FIRE off: the player lights it");
            Assert.IsFalse(fire.Showing, "no fireplace until FIRE is pressed");
            Assert.That(fire.Hearth, Is.Not.Null, "built on entering mixed reality");
            Assert.IsFalse(fire.Hearth.Root.gameObject.activeSelf);

            Light();
            AssertValidPlace(fire, App.Get<IRealWalls>(), onWall: null);
            Vector3 first = fire.Spot.Pose.position;

            // FIRE off: a short fade, then gone
            room.Set(AmbienceKind.Fire, false);
            StepFrames(3);
            Assert.IsTrue(fire.Showing, "going out, not gone at once");
            Assert.That(fire.Level, Is.LessThan(1f));
            StepFrames(12);
            Assert.IsFalse(fire.Showing, "gone after FadeOut");
            Assert.IsFalse(fire.Hearth.Root.gameObject.activeSelf);

            // on again: lit again, in the same place (the player did not move)
            Light();
            Assert.That(Vector3.Distance(fire.Spot.Pose.position, first), Is.LessThan(0.01f));
            Assert.That(fire.Placements, Is.EqualTo(2));

            // leaving: hidden at once, the crackle back at the VR room's fireplace, the VR room's fire burning
            Mr.Exit();
            StepFrames(2);
            Assert.IsFalse(fire.Showing);
            Assert.IsFalse(fire.Hearth.Root.gameObject.activeSelf);
            Assert.IsFalse(fire.SoundAtHearth);
            Assert.That(Vector3.Distance(App.Get<IAudioEngine>().Source(SourceId.Fire).transform.position, AudioModule.FirePosition), Is.LessThan(1e-4f));
            Assert.IsTrue(room.Visible);
            Assert.IsTrue(room.Get(AmbienceKind.Fire), "FIRE as it was before mixed reality");
            Assert.IsTrue(room.Scene.Fireplace.gameObject.activeInHierarchy, "the VR room's fire burns");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WithNoWallsItStandsOnTheFloorInFront()
        {
            yield return EnterRoom("MeshBedroom3");
            StepFrames(4);
            MrFireplace fire = Music.MrFireplace;
            var none = new FakeRealWalls();
            fire.RealWallsOverride = none;
            MrFireplace lit = Light();
            Assert.That(none.Refreshes, Is.GreaterThan(0), "it asked for the walls");
            AssertValidPlace(lit, none, onWall: false);
            Assert.That(lit.Spot.Distance, Is.InRange(2.0f, 3.1f), "about 2.5 m in front");
            // a frame of it, burning, allocates nothing (the walls are asked again every 2 s: still none)
            Assert.That(() => { for (int i = 0; i < 120; i++) lit.Tick(1f / 72f, false); }, Is.Not.AllocatingGCMemory());
            Assert.That(lit.Level, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator InASimulatedRoomItStandsOnTheWallInFront()
        {
            yield return EnterRoom("MeshBedroom3");
            StepFrames(4);
            MrFireplace fire = Music.MrFireplace;
            Vector3 head = App.Head.position;
            Vector3 fwd = App.Head.forward; fwd.y = 0f; fwd.Normalize();
            float floor = RoomSnowModel.MrFloor(head.y, Mr.FloorHeight);
            // a 5 x 6 m box about the player, turned to face their view (its front wall 3 m ahead)
            var walls = FakeRealWalls.Box(Vector3.zero, floor, 5f, 6f);
            Quaternion turn = Quaternion.LookRotation(fwd);
            for (int i = 0; i < walls.Walls.Count; i++)
            {
                RealWall w = walls.Walls[i];
                w.Centre = new Vector3(head.x, 0f, head.z) + turn * w.Centre;
                w.Inward = turn * w.Inward;
                walls.Walls[i] = w;
            }
            fire.RealWallsOverride = walls;
            MrFireplace lit = Light();
            AssertValidPlace(lit, walls, onWall: true);
            Assert.That(lit.Spot.Wall, Is.Not.EqualTo(1), "never the wall behind the player");
            if (!lit.Spot.Hidden && lit.Spot.Wall == 0) Assert.That(Vector3.Angle(lit.Hearth.Root.forward, -fwd), Is.LessThan(1f), "facing the player");
        }

        [UnityTest]
        public IEnumerator AFrameOfItAllocatesNothing()
        {
            yield return EnterRoom("MeshLivingRoom2");
            StepFrames(4);
            MrFireplace fire = Light();
            Assert.That(() => { for (int i = 0; i < 120; i++) fire.Tick(1f / 72f, false); }, Is.Not.AllocatingGCMemory(), "burning");
            Music.Set(AmbienceKind.Fire, false);
            Assert.That(() => { for (int i = 0; i < 120; i++) fire.Tick(1f / 72f, false); }, Is.Not.AllocatingGCMemory(), "going out, and out");
            Assert.IsFalse(fire.Showing);
            Mr.Exit();
            StepFrames(2);
            Assert.That(() => { for (int i = 0; i < 120; i++) fire.Tick(1f / 72f, true); }, Is.Not.AllocatingGCMemory(), "in the VR room");
        }
    }

    /// <summary>The app's own Layout (as MrInFrontTests): the instruments of the player's set in front of them, as the app has
    /// it in mixed reality, in every one of MRUK's recorded rooms.</summary>
    public class MrFireplaceRoomsTests : MrFireplaceRig
    {
        protected override bool UseContractLayout => false;

        static readonly string[] Rooms = { "MeshBedroom1", "MeshBedroom2", "MeshBedroom3", "MeshLivingRoom1", "MeshLivingRoom2", "MeshLivingRoom3", "MeshOffice1", "MeshOffice2" };

        [UnityTest]
        public IEnumerator EveryRecordedRoomGetsAValidPlace()
        {
            int onWall = 0, rough = 0;
            var lines = new List<string>();
            foreach (string name in Rooms)
            {
                yield return EnterRoom(name);
                StepFrames(4);
                MrFireplace fire = Music.MrFireplace;
                Light();
                AssertValidPlace(fire, App.Get<IRealWalls>(), onWall: null);
                if (fire.Spot.OnWall) onWall++;
                if (fire.Spot.Rough) rough++;
                lines.Add(name + ": " + fire.Spot);
                Music.Set(AmbienceKind.Fire, false);
                StepFrames(20);
                Mr.Exit();
                StepFrames(2);
                yield return WaitFor(() => !Mr.Loading);
            }
            Debug.Log("[MrFire] recorded rooms:\n  " + string.Join("\n  ", lines));
            // MRUK's recorded rooms are furnished: most walls near the player are lined with furniture, windows and doors, so
            // the hearth stands free on the floor in most of them (2026-11-14: 2 of 8 on a wall, none rough).
            Assert.That(onWall, Is.GreaterThanOrEqualTo(2), "against a real wall where one is free");
            Assert.That(rough, Is.EqualTo(0), "a free place in every room");
        }

    }
}
#endif
