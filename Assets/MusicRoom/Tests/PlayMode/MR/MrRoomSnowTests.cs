// Snow in the whole room in mixed reality (Room/Decor/RoomSnow.cs; 2026-11-13: the snowy window is gone, the snow falls
// in the player's own room): entering one of MRUK's recorded rooms with SNOW on, the snowfall is round about the player,
// stands on the scene's floor (IMixedReality.FloorHeight) and reaches well above the head; every flake that shows is
// inside it, all around the head and never at it; leaving brings back the VR room's own box.
#if UNITY_EDITOR
using System.Collections;
using MusicRoom.Core;
using MusicRoom.MR;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrRoomSnowTests : MrTestRig
    {
        protected override bool TableOnEnter => false;

        [UnityTest]
        public IEnumerator ItSnowsRoundThePlayerInTheirOwnRoom()
        {
            var room = App.Get<IRoom>() as RoomModule;
            Assert.That(room, Is.Not.Null);
            room.Set(AmbienceKind.Snow, true);
            room.Weather.Settle(1f, 0.5f, room.Weather.TimeD);
            yield return EnterRoom("MeshBedroom3");
            StepFrames(10);
            Assert.That(Mr.Active, Is.True);
            RoomSnow snow = room.RoomSnow;
            Assert.IsTrue(snow.Falling, "SNOW stays on in mixed reality");
            Assert.IsTrue(snow.InMixedReality);
            RoomSnowModel.Frame f = snow.Frame;
            Vector3 head = App.Head.position;
            Assert.IsTrue(f.Vol.Round);
            float floor = RoomSnowModel.MrFloor(head.y, Mr.FloorHeight);
            Assert.That(f.Vol.Floor, Is.EqualTo(floor).Within(1e-3f), "on the real floor (or a seated estimate)");
            Assert.That(f.Vol.Top, Is.GreaterThanOrEqualTo(head.y + RoomSnowModel.MrAboveHead - 1e-3f));
            Assert.That(Vector2.Distance(new Vector2(f.Vol.Centre.x, f.Vol.Centre.z), new Vector2(head.x, head.z)), Is.LessThan(0.1f), "about the player");
            var sectors = new int[8];
            int shown = 0;
            foreach (RoomSnowModel.Seed s in snow.Seeds)
            {
                RoomSnowModel.FlakeAt(s, f, f.Head, out Vector3 p, out float a, out _);
                if (a <= 0f) continue;
                shown++;
                Assert.IsTrue(f.Vol.Contains(p, 1e-3f));
                Assert.That(Vector3.Distance(p, f.Head), Is.GreaterThan(RoomSnowModel.HeadClear), "the head stays clear");
                Vector3 d = p - f.Head;
                if (new Vector2(d.x, d.z).magnitude < 0.3f) continue;
                sectors[Mathf.Clamp((int)((Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 180f) / 45f), 0, 7)]++;
            }
            Debug.Log($"[RoomSnow] MR room: floor {f.Vol.Floor:F2} (scene {Mr.FloorHeight:F2}), top {f.Vol.Top:F2}, head {head.y:F2}; {shown} flakes, sectors {string.Join(",", sectors)}");
            Assert.That(shown, Is.GreaterThan(RoomSnowModel.Count / 3));
            for (int k = 0; k < 8; k++) Assert.That(sectors[k], Is.GreaterThan(30), "all around (" + k + ")");
            Mr.Exit();
            StepFrames(4);
            Assert.IsFalse(snow.InMixedReality, "back in the music room: its own box");
            Assert.IsFalse(snow.Frame.Vol.Round);
        }
    }
}
#endif
