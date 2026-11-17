// The VR room faces the fire (2026-11-11, "in music room VR i want to inverse my position. I should see the fire and whole
// room."; Core's RoomFrame turns the room a half turn about the seat). Category Graphics (`Tools/unity.sh test-play --gpu`):
// every real module, a simulated headset, after the first arrange. From the seated eye looking ahead: the fire is in view
// over the instruments (its pixels are warm in the picture, not hidden by the piano), the window on the left, the room's
// furniture in front and clear of the seated player and of My set's homes; with the default set and with the piano alone.
// Pictures to Builds/Shots/room-facing (or $MUSICROOM_SHOTS_ROOT/room-facing): seat_fire_default(_wide),
// seat_fire_piano(_wide), seat_left_window, seat_behind, plan_turned.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Room;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Room
{
    [Category("Graphics")]
    public class RoomFacingShotsTests
    {
        const int Width = 1280, Height = 960;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        RoomModule _room;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no tour: Sunny out of the picture
            _boot.Hands.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(0.6f, 1.9f, -0.9f)));
            _boot.Hands.SetPose(HandSide.Left, HandPose.Open(HandSide.Left, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(-0.6f, 1.9f, -0.9f)));
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules)
            {
                if (m is LayoutModule lm) _layout = lm;
                if (m is RoomModule rm) _room = rm;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            GC.Collect();
        }

        static string OutDir()
        {
            string dir = ShotDirs.FromRoot("room-facing") ?? ShotDirs.Default("room-facing");
            Directory.CreateDirectory(dir);
            return dir;
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 6; i++) yield return null;   // the glides start on the next frames
            for (int i = 0; i < 900 && (_layout.AnyMoving || _layout.Settling); i++) yield return null;
            for (int i = 0; i < 12; i++) yield return null;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator FromTheSeatThePlayerFacesTheFireOverTheInstruments()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            float t0 = Time.realtimeSinceStartup;
            while ((!_layout.FirstArrangeDone || _room.TexturesPending > 0 || !_room.ProbeRendered) && Time.realtimeSinceStartup - t0 < 300f) yield return null;
            Assert.IsTrue(_layout.FirstArrangeDone, "Layout arranged the room");
            for (int i = 0; i < 30; i++) yield return null;

            // The seat faces the fire: the room root carries the turn, the fire straight ahead, the window on the left.
            Transform head = _app.Head;
            Vector3 eye = head.position;
            Assert.That(Quaternion.Angle(_room.Root.rotation, RoomFrame.Rotation), Is.LessThan(1e-3f), "the room root is RoomFrame");
            Vector3 fire = _room.Scene.FireLight.transform.position;
            Assert.That(fire.z - eye.z, Is.GreaterThan(3.0f), "the fire ahead of the seat");
            Assert.That(Mathf.Abs(fire.x - eye.x), Is.LessThan(0.6f), "nearly straight ahead");
            Assert.That(_room.Scene.Window.position.x, Is.LessThan(eye.x - 2.5f), "the window on the left wall");

            // Nothing of the room stands in the seated player or in My set's homes (the bench is the seat itself).
            AssertFurnitureClearOfTheSeat();

            string dir = OutDir();
            var camGo = new GameObject("RoomFacingCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = XrMode.VrBackground;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.cullingMask = _app.Rig.HeadCamera.cullingMask;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            Renderer flames = _room.Scene.Fireplace.Find("Flames").GetComponent<Renderer>();
            try
            {
                // Seated, looking ahead and a little down, as a player at the piano does (the Quest's view is ~100 degrees).
                Vector3 ahead = eye + Quaternion.Euler(8f, 0f, 0f) * Vector3.forward;
                yield return Shot(cam, rt, readback, dir, "seat_fire_default", eye, ahead, 90f);
                float warmDefault = Warmth(cam, readback, flames.bounds.center, out Vector2 px);
                Debug.Log($"[Room] facing the fire, default set: flames at pixel {px}, warmth {warmDefault:F0}");
                yield return Shot(cam, rt, readback, dir, "seat_fire_default_wide", eye, ahead, 110f);
                yield return Shot(cam, rt, readback, dir, "seat_left_window", eye, eye + new Vector3(-1f, -0.05f, 0.35f), 90f);
                yield return Shot(cam, rt, readback, dir, "seat_behind", eye, eye + new Vector3(0f, -0.15f, -1f), 90f);
                yield return Shot(cam, rt, readback, dir, "plan_turned", RoomFrame.WorldBox.center + new Vector3(0f, 8.6f - 1.45f, 0f), RoomFrame.WorldBox.center + new Vector3(0f, -1.45f, 1e-4f), 52f, Vector3.forward);
                Assert.That(warmDefault, Is.GreaterThan(60f), "the fire shows over the default set (warm pixels where the flames are)");

                // The piano alone.
                _layout.Set(LayoutMode.Single, InstrumentKind.Piano);
                yield return Settle();
                Assert.That(_layout.PlaceOf(InstrumentKind.Harp), Is.Not.EqualTo(InstrumentPlace.Seat), "the piano alone: the harp went to its spot");
                Assert.That(_layout.PlaceOf(InstrumentKind.Drums), Is.Not.EqualTo(InstrumentPlace.Seat));
                yield return Shot(cam, rt, readback, dir, "seat_fire_piano", eye, ahead, 90f);
                float warmPiano = Warmth(cam, readback, flames.bounds.center, out px);
                Debug.Log($"[Room] facing the fire, the piano alone: flames at pixel {px}, warmth {warmPiano:F0}");
                yield return Shot(cam, rt, readback, dir, "seat_fire_piano_wide", eye, ahead, 110f);
                Assert.That(warmPiano, Is.GreaterThan(60f), "the fire shows over the piano");
                _layout.Set(LayoutMode.All, InstrumentKind.Piano);
                yield return Settle();
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(readback);
            }
        }

        /// <summary>Every published floor blocker but the bench stays clear (3 cm) of the seated player's boxes and of the default
        /// set's boxes at their homes, and of the side table at its home and its alternate.</summary>
        void AssertFurnitureClearOfTheSeat()
        {
            IRoomFurniture furniture = _app.Get<IRoomFurniture>();
            SeatPlanner planner = SeatPlanner.FromRegistry(_app.Registry);
            var seat = new List<(string, SeatPlanner.FloorObb)>();
            var origin = new SeatPlanner.Pose2(0, 0, 0);
            for (int i = 0; i < SeatPlanner.Player.Length; i++) seat.Add(("player " + SeatPlanner.PlayerNames[i], SeatPlanner.WorldBox(origin, SeatPlanner.Player[i])));
            SeatPlanner.Plan p = planner.PlanSet(new List<InstrumentKind> { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp });
            Assert.IsTrue(p.Fits, "the default set fits");
            foreach (SeatPlanner.Member m in p.Members)
                foreach (SeatPlanner.Box b in planner.Get(m.Kind).Boxes) if (!b.Air) seat.Add((m.Kind.ToString(), SeatPlanner.WorldBox(m.Pose, b)));
            foreach (SeatPlanner.Pose2 t in new[] { planner.TableHome, planner.TableAlternate })
                foreach (SeatPlanner.Box b in planner.TableBoxes) seat.Add(("side table", SeatPlanner.WorldBox(t, b)));
            var problems = new List<string>();
            foreach (FurnitureBlocker f in furniture.FloorBlockers.Concat(furniture.Decor))
            {
                if (f.Name == "bench") continue;
                SeatPlanner.FloorObb o = StowPlan.BlockerBox(f);
                foreach (var (who, box) in seat)
                    if (SeatPlanner.Overlaps(o, box, StowPlan.Gap)) problems.Add($"{f.Name} meets {who}");
            }
            Assert.That(problems, Is.Empty, "the room keeps clear of the seat");
        }

        /// <summary>How warm the picture is where <paramref name="world"/> projects: the largest (r - b) in a 15 x 15 patch.</summary>
        static float Warmth(Camera cam, Texture2D img, Vector3 world, out Vector2 px)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            px = new Vector2(v.x * Width, v.y * Height);
            if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return -1f;
            float best = -255f;
            for (int dy = -7; dy <= 7; dy++)
                for (int dx = -7; dx <= 7; dx++)
                {
                    int x = Mathf.Clamp((int)px.x + dx, 0, Width - 1), y = Mathf.Clamp((int)px.y + dy, 0, Height - 1);
                    Color32 c = img.GetPixel(x, y);
                    best = Mathf.Max(best, c.r - c.b);
                }
            return best;
        }

        IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 look, float fov, Vector3? up = null)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, up ?? Vector3.up));
            cam.fieldOfView = fov;
            cam.aspect = Width / (float)Height;
            cam.targetTexture = rt;
            yield return null;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), readback.EncodeToPNG());
            Debug.Log($"[Room] shot {name}");
        }
    }
}
