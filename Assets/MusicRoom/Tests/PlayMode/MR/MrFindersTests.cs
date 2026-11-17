// The verified 03 sketch tests (Docs/03-mr-sketch/Tests/MrFindersTests.cs), moved here (ARCHITECTURE.md 15.9):
// TableFinder on MRUK's recorded rooms (MeshOffice1: the 0.74 m desk), surfaces in a room without tables, room events.
// (WindowFinder and its window tests went with the snowy window, 2026-11-13.) No Interaction SDK.
// LogAssert.ignoreFailingMessages is reset in TearDown so it never hides errors in other tests.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrFindersTests
    {
        const string Rooms = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/";
        const string TestJson = "Packages/com.meta.xr.mrutilitykit/Tests/JSON/";
        static readonly StringBuilder Report = new StringBuilder();
        GameObject _rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true; // MRUK logs its own errors for sample rooms without a headset
            if (Object.FindAnyObjectByType<OVRCameraRig>() == null)
            {
                _rig = new GameObject("OVRCameraRig");
                _rig.AddComponent<OVRCameraRig>();
            }
            MrScene.EnsureMruk();
            Assert.IsNotNull(MRUK.Instance, "MRUK made from code");
            Report.AppendLine($"[setup] MRUK created, worldLock={MRUK.Instance.EnableWorldLock}");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (MRUK.Instance != null) Object.Destroy(MRUK.Instance.gameObject);
            if (_rig != null) Object.Destroy(_rig);
            yield return null;
            LogAssert.ignoreFailingMessages = false;
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Logs"));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "mr-finders-report.txt"), Report.ToString());
        }

        // By time, not frames: batchmode frames are very short and MRUK builds a global-mesh BVH on a worker thread,
        // which takes longer when other editors share the CPU. A load still running when the next test starts makes
        // MRUK refuse every later load ("anchor discovery is running").
        static IEnumerator Wait(Task<MrSceneStatus> t, float seconds = 20f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!t.IsCompleted && Time.realtimeSinceStartup - t0 < seconds) yield return null;
            Assert.IsTrue(t.IsCompleted, "load finished");
        }

        [UnityTest]
        public IEnumerator JsonRooms_Tables()
        {
            foreach (var name in new[] { "MeshLivingRoom1", "MeshLivingRoom2", "MeshLivingRoom3", "MeshOffice1", "MeshOffice2", "MeshBedroom1", "MeshBedroom2", "MeshBedroom3" })
            {
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Rooms + "Json/" + name + ".json");
                Assert.IsNotNull(json, name);
                var t = MrScene.LoadJsonSceneAsync(json.text);
                yield return Wait(t);
                Assert.AreEqual(MrSceneStatus.Loaded, t.Result, name);
                var room = MRUK.Instance.GetCurrentRoom() ?? MRUK.Instance.Rooms[0];
                Bounds rb = room.GetRoomBounds();
                Report.AppendLine($"[json] {name} anchors={room.Anchors.Count} floorY={TableFinder.FloorY(room):F3}");

                Vector3 head = new Vector3(rb.center.x, TableFinder.FloorY(room) + 1.2f, rb.center.z);
                var all = new List<TableSurface>();
                bool ok = TableFinder.TryFindBest(room, head, Vector3.forward, TableQuery.Default, out var best, all);
                Report.AppendLine($"  candidates={all.Count} best={(ok ? $"h={best.TopHeight:F2} edge={best.EdgeLength:F2} score={best.Score:F2}" : "none")}");
                foreach (var a in room.Anchors)
                {
                    if (!a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) continue;
                    TableFinder.TryDescribe(a, head, TableFinder.FloorY(room), out var s);
                    // TryDescribe's near edge is MRUK's own EDGE pose for a ray from the head at the top centre.
                    var ray = new Ray(head, (s.TopCenter - head).normalized);
                    var pose = room.GetBestPoseFromRaycast(ray, 10f, new LabelFilter(MRUKAnchor.SceneLabels.TABLE), out MRUKAnchor hitA, out Vector3 n, MRUK.PositioningMethod.EDGE);
                    if (hitA == a)
                    {
                        Assert.That(Vector3.Distance(pose.position, s.EdgeMidpoint), Is.LessThan(0.011f), name + " EDGE point");
                        Assert.That(Vector3.Dot(pose.rotation * Vector3.forward, s.TowardUser), Is.GreaterThan(0.99f), name + " EDGE direction");
                    }
                }
                if (name == "MeshBedroom3") Assert.IsFalse(ok);
                if (name == "MeshLivingRoom2")
                {
                    Assert.IsTrue(ok);
                    Assert.That(best.TopHeight, Is.EqualTo(0.76f).Within(0.01f), "the 1.32 m-edge table");
                    Assert.That(best.EdgeLength, Is.EqualTo(1.32f).Within(0.01f));
                }
                if (name == "MeshOffice1")
                {
                    Assert.IsTrue(ok);
                    Assert.That(best.TopHeight, Is.EqualTo(0.74f).Within(0.01f), "the 0.74 m desk");
                    Assert.That(best.Score, Is.EqualTo(1.34f).Within(0.01f));
                }
            }
        }

        [UnityTest]
        public IEnumerator Events_RefreshOnRoomUpdate()
        {
            var a = AssetDatabase.LoadAssetAtPath<TextAsset>(Rooms + "Json/MeshLivingRoom2.json");
            var t = MrScene.LoadJsonSceneAsync(a.text);
            yield return Wait(t);
            yield return null;
            Assert.AreEqual(1, MRUK.Instance.Rooms.Count);
            var first = MRUK.Instance.Rooms[0];

            var b = AssetDatabase.LoadAssetAtPath<TextAsset>(TestJson + "SceneWithRoom1.json");
            t = MrScene.LoadJsonSceneAsync(b.text); // different room UUID: old room removed, new room created
            yield return Wait(t);
            yield return null;
            Assert.AreEqual(1, MRUK.Instance.Rooms.Count);
            Assert.That(MRUK.Instance.Rooms[0], Is.Not.SameAs(first), "a new room");

            var room = MRUK.Instance.Rooms[0];
            int tablesBefore = 0;
            foreach (var an in room.Anchors) if (an.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) tablesBefore++;
            var c = AssetDatabase.LoadAssetAtPath<TextAsset>(TestJson + "SceneWithRoom1MoreAnchors.json");
            t = MrScene.LoadJsonSceneAsync(c.text); // same room UUID with two more tables: RoomUpdatedEvent
            yield return Wait(t);
            yield return null;
            int tablesAfter = 0;
            foreach (var an in MRUK.Instance.Rooms[0].Anchors) if (an.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) tablesAfter++;
            Assert.AreEqual(tablesBefore + 2, tablesAfter);
            Assert.That(MRUK.Instance.Rooms[0], Is.SameAs(room), "the room object is reused");

            MrScene.Clear();
            yield return null;
            yield return null;
            Assert.AreEqual(0, MRUK.Instance.Rooms.Count);
        }

        [UnityTest]
        public IEnumerator NoTables_FloorAndTableSurfaces()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Rooms + "Json/MeshBedroom3.json"); // no windows, no tables
            var t = MrScene.LoadJsonSceneAsync(json.text);
            yield return Wait(t);
            var room = MRUK.Instance.Rooms[0];
            float floorY = TableFinder.FloorY(room);
            Bounds rb = room.GetRoomBounds();
            Vector3 eye = new Vector3(rb.center.x, floorY + 1.2f, rb.center.z);

            // Floor: pointing straight down at open floor is a surface hit but not a table.
            Vector3 open = eye;
            for (float x = rb.min.x + 0.3f; x < rb.max.x; x += 0.25f)
            for (float z = rb.min.z + 0.3f; z < rb.max.z; z += 0.25f)
            {
                var fp = new Vector3(x, floorY + 0.05f, z);
                if (room.IsPositionInRoom(fp, false) && !room.IsPositionInSceneVolume(fp, false, 0.15f)) { open = new Vector3(x, floorY + 1.2f, z); x = z = float.MaxValue; }
            }
            bool hitFloor = TableFinder.TryPointAtSurface(room, new Ray(open, Vector3.down), out var p, out var a, out bool onTable);
            Assert.IsTrue(hitFloor);
            Assert.IsFalse(onTable);

            // A room with tables: point at the best table's top and place the piano.
            var j2 = AssetDatabase.LoadAssetAtPath<TextAsset>(Rooms + "Json/MeshOffice1.json");
            t = MrScene.LoadJsonSceneAsync(j2.text);
            yield return Wait(t);
            room = MRUK.Instance.Rooms[0];
            rb = room.GetRoomBounds();
            eye = new Vector3(rb.center.x, TableFinder.FloorY(room) + 1.2f, rb.center.z);
            Assert.IsTrue(TableFinder.TryFindBest(room, eye, Vector3.forward, TableQuery.Default, out var best));
            var ray = new Ray(eye, (best.EdgeMidpoint - best.TowardUser * 0.05f - eye).normalized);
            bool hitTable = TableFinder.TryPointAtTable(room, ray, eye, out var s, out var hp);
            bool hitAny = TableFinder.TryPointAtSurface(room, ray, out var p2, out var a2, out bool onTable2);
            var pose = TableFinder.PianoPoseOnTable(s, hp);
            Assert.IsTrue(hitTable && hitAny && onTable2);
            Assert.AreEqual(s.TopY + TableFinder.TableLift, pose.position.y, 1e-4f);
            Assert.Less(Vector3.Dot(pose.rotation * Vector3.forward, s.TowardUser), -0.99f, "piano local +Z points away from the user");
            // The pinch-placement maths agrees with the table maths on that hit (PianoPlacement.FromHit vs PianoPoseOnTable).
            var pinch = PianoPlacement.FromHit(hp, eye, TableFinder.FloorY(room), out bool pinchTable);
            Assert.IsTrue(pinchTable);
            Assert.That(pinch.position.y, Is.EqualTo(pose.position.y).Within(2e-3f));
        }

        [UnityTest]
        public IEnumerator Device_InEditor_IsNotAvailable()
        {
            var t = MrScene.LoadDeviceSceneAsync();
            yield return Wait(t);
            Report.AppendLine($"[device] editor result={MrScene.LastResult} status={t.Result} ovrPluginInit={OVRPlugin.initialized}");
            Assert.AreEqual(MrSceneStatus.NotAvailable, t.Result);
        }

        [UnityTest]
        public IEnumerator LargeScale_MultiRoom()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Rooms + "Prefabs/LargeScale/ThreeRooms.prefab");
            var t = MrScene.LoadPrefabSceneAsync(prefab);
            yield return Wait(t);
            Assert.That(MRUK.Instance.Rooms.Count, Is.EqualTo(3));
            Assert.That(MRUK.Instance.GetCurrentRoom(), Is.Not.Null);
        }
    }
}
#endif
