// GPU renders of "Play in my room" (Tools/unity.sh test-play --gpu): passthrough simulated as a grey background, one
// of MRUK's recorded rooms loaded from JSON and drawn behind everything as flat grey surfaces (a stand-in for the
// camera image: like passthrough it never hides virtual content). PNGs go to Builds/Shots/mr/ (or
// $MUSICROOM_SHOTS_ROOT/mr). Also the MR draw budget (ARCHITECTURE.md 14: MR <= 80 draws). (The snowy window and the snow
// behind real windows were removed 2026-11-13, with their shots and the clipped-to-window check.)
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Meta.XR.MRUtilityKit;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.MR;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    [Category("Graphics")]
    public class MrShotsTests : MrTestRig
    {
        // MUSICROOM_SHOTS_ROOT (optional) redirects the shots of a whole run (e.g. a clone's own folder); default unchanged.
        static string ShotRoot(string m) => ShotDirs.FromRoot(m) ?? ShotDirs.Default(m);
        public static readonly string ShotDir = ShotRoot("mr");
        const int W = 1280, H = 720;
        protected override bool Headless => false;
        readonly List<Object> _made = new List<Object>();
        GameObject _preview;

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var o in _made) if (o != null) Object.Destroy(o);
            _made.Clear();
            if (_preview != null) Object.Destroy(_preview);
            yield return null;
        }

        static void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (run Tools/unity.sh test-play --gpu)");
        }

        [UnityTest]
        public IEnumerator LivingRoomSeatedViewAndTableReticle()
        {
            RequireGpu();
            Directory.CreateDirectory(ShotDir);
            Weather.Target = 1f; Weather.Level = 1f; Weather.Cover = 0.6f; Weather.WindVel = 0.5f;
            yield return EnterRoom("MeshLivingRoom2");
            AddPianoStandIn();
            BuildRoomPreview();
            for (int i = 0; i < 30; i++) yield return null; // catcher placed

            // 1. The seated view (the desktop seat's default heading).
            yield return Shot("mr-seated.png");

            // 3. The placing reticle on a table: start placing, point the right hand at the best table.
            Assert.That(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var table));
            Vector3 onTop = table.EdgeMidpoint - table.TowardUser * 0.15f;
            Vector3 seat = table.EdgeMidpoint + table.TowardUser * 0.75f;
            seat.y = TableFinder.FloorY(Room) + 1.2f;
            Place(seat);
            Look(seat, onTop + Vector3.down * 0.1f);
            Vector3 wrist = seat + Quaternion.LookRotation(MrUtil.Flat(onTop - seat, Vector3.forward)) * new Vector3(0.18f, -0.38f, 0.2f);
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, onTop, 0.05f));
            yield return null;
            Assert.That(Mr.BeginPlacing(1), Is.True);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.True, "the reticle on the table");
            yield return Shot("mr-table-reticle.png");

            // 4. Pinch: the piano lands there, its shadow on the catcher.
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, onTop, 0f));
            yield return null;
            Assert.That(Mr.PianoOnTable && Mr.PlacedByPlayer);
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, onTop, 0.05f));
            Fake.SetTracked(HandSide.Right, false);
            for (int i = 0; i < 3; i++) yield return null;
            yield return Shot("mr-piano-placed.png");

            // 5. From the shadow side: the piano's contact shadow on the table (the catcher, alpha .32).
            yield return ShadowShot("mr-piano-shadow.png");
            Place(DesktopRigFallback.EyePosition);
        }

        [UnityTest]
        public IEnumerator BedroomPianoFloorShadow()
        {
            RequireGpu();
            Directory.CreateDirectory(ShotDir);
            yield return EnterRoom("MeshBedroom3");
            BuildRoomPreview();
            for (int i = 0; i < 3; i++) yield return null;
            // No table in this room: the piano stands in front of the player on its stand; its shadow on the floor.
            Assert.That(Mr.PianoOnTable, Is.False);
            AddPianoStandIn();
            yield return ShadowShot("mr-piano-floor-shadow.png");
        }

        IEnumerator ShadowShot(string file)
        {
            Transform piano = Rig(RigId.Piano).Group;
            Vector3 sunFlat = MrUtil.Flat(Mr.Lighting.Sun.transform.forward, Vector3.forward);
            Vector3 side = Vector3.Cross(Vector3.up, sunFlat);
            // The shadow falls along the sun's heading (+sunFlat); seen from across the light (not from the sun's side,
            // where the piano hides its own shadow) it lies beside the body: a thin contact rim on a table, a full
            // footprint on the floor 0.72 m below.
            bool table = Mr.PianoOnTable;
            Vector3 eye = piano.position + side * 1.3f + Vector3.up * 0.8f;
            Vector3 at = piano.position + sunFlat * (table ? 0.1f : 0.5f) + Vector3.down * (table ? 0.1f : 0.6f);
            Debug.Log($"[MR-TEST] {file}: piano={piano.position:F3} catcher={Mr.Lighting.Catcher.position:F3} sun={Mr.Lighting.Sun.transform.forward:F3}");
            if (Mr.HintTransform != null) Mr.HintTransform.gameObject.SetActive(false);
            Place(eye);
            Look(eye, at);
            for (int i = 0; i < 2; i++) yield return null;
            yield return Shot(file);
            Place(DesktopRigFallback.EyePosition);
            if (Mr.HintTransform != null) Mr.HintTransform.gameObject.SetActive(true);
        }

        RenderTexture _rt;

        IEnumerator Shot(string file, System.Action<Texture2D> check = null)
        {
            yield return null;
            var tex = Render();
            File.WriteAllBytes(Path.Combine(ShotDir, file), tex.EncodeToPNG());
            Debug.Log($"[MR-TEST] wrote {file}");
            check?.Invoke(tex);
            Object.Destroy(tex);
        }

        Texture2D Render()
        {
            var cam = App.Rig.HeadCamera;
            if (_rt == null)
            {
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "MR shot" };
                _made.Add(_rt);
            }
            cam.targetTexture = _rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            return tex;
        }

        void Place(Vector3 eye) => App.Head.localPosition = eye; // the code-only rig sits at the origin

        void Look(Vector3 from, Vector3 at)
        {
            Vector3 d = at - from;
            var desk = App.Rig.Desktop;
            desk.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            desk.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            App.Head.localRotation = Quaternion.Euler(desk.pitch, desk.yaw, 0f);
        }

        // A plain piano body under the (stub) piano rig, so the placement and its shadow show in the renders.
        void AddPianoStandIn()
        {
            var g = Rig(RigId.Piano).Group;
            MakeBox(g, new Vector3(0f, -0.027f, 0.145f), new Vector3(1.326f, 0.13f, 0.29f), new Color(0.07f, 0.07f, 0.08f));
            var keys = MakeBox(g, new Vector3(0f, -0.006f, 0.08f), new Vector3(1.22f, 0.014f, 0.15f), new Color(0.95f, 0.93f, 0.88f));
            keys.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
        }

        GameObject MakeBox(Transform parent, Vector3 local, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            var m = App.Materials.Lit(color, 0.35f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            _made.Add(go);
            return go;
        }

        // The recorded room as flat grey surfaces drawn first and never written to depth: a stand-in for passthrough.
        void BuildRoomPreview()
        {
            _preview = new GameObject("Passthrough stand-in");
            var shader = Shader.Find(MrMaterials.UnlitShader);
            var room = Room;
            Vector3 light = new Vector3(0.4f, 0.8f, -0.45f).normalized;
            foreach (var a in room.Anchors)
            {
                if (!a.PlaneRect.HasValue && !a.VolumeBounds.HasValue) continue;
                Color baseColor; int queue;
                // Painter's order: floor and ceiling first, walls over them, then windows / doors, then furniture tops.
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR)) { baseColor = new Color(0.40f, 0.39f, 0.37f); queue = 998; }
                else if (a.HasAnyLabel(MRUKAnchor.SceneLabels.CEILING)) { baseColor = new Color(0.62f, 0.62f, 0.61f); queue = 998; }
                else if (a.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE)) { baseColor = new Color(0.56f, 0.55f, 0.53f); queue = 1000; }
                else if (a.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME)) { baseColor = new Color(0.30f, 0.34f, 0.40f); queue = 1002; }
                else if (a.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WALL_ART)) { baseColor = new Color(0.47f, 0.45f, 0.42f); queue = 1002; }
                else if (a.HasAnyLabel(MRUKAnchor.SceneLabels.GLOBAL_MESH)) continue;
                else { baseColor = new Color(0.45f, 0.38f, 0.32f); queue = 1003; }
                Rect rect;
                if (a.PlaneRect.HasValue) rect = a.PlaneRect.Value;
                else { var b = a.VolumeBounds.Value; rect = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y); }
                Vector3 n = a.transform.forward;
                float shade = 0.82f + 0.18f * Vector3.Dot(n, light);
                var m = new Material(shader);
                m.SetColor(MrMaterials.BaseColorId, new Color(baseColor.r * shade, baseColor.g * shade, baseColor.b * shade, 1f));
                m.SetFloat(MrMaterials.CullId, (float)CullMode.Off);
                m.SetFloat(MrMaterials.ZTestId, (float)CompareFunction.Always);
                m.renderQueue = queue;
                _made.Add(m);
                var quad = new GameObject(a.name + " stand-in");
                quad.transform.SetParent(_preview.transform, false);
                quad.transform.SetPositionAndRotation(a.transform.TransformPoint(rect.center.x, rect.center.y, 0f), a.transform.rotation);
                quad.AddComponent<MeshFilter>().sharedMesh = MrMeshes.Rect(rect.width, rect.height);
                var mr = quad.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }
    }

    /// <summary>ARCHITECTURE.md 14 / 17: mixed reality draws at most 80 calls (single-pass instanced), in MeshLivingRoom2 with
    /// the catcher, the hint card and the sun's shadow pass. The profiler's render
    /// counters, as DrawBudgetTests (the stub instruments draw nothing, so this is MR's own share plus the scene).</summary>
    [Category("Graphics")]
    public class MrDrawBudgetTests : MrTestRig
    {
        public const int MixedRealityDraws = 80;

        [UnityTest]
        public IEnumerator MixedRealityStaysInsideItsDrawBudget()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (run Tools/unity.sh test-play --gpu)");
            Weather.Target = 1f; Weather.Level = 1f; Weather.Cover = 0.5f;
            yield return EnterRoom("MeshLivingRoom2");
            var cam = App.Rig.HeadCamera;
            App.Rig.Desktop.pitch = 0f;
            var rt = new RenderTexture(1024, 1024, 24) { name = "MR draw budget" };
            cam.targetTexture = rt;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            for (int i = 0; i < 8; i++) yield return null;
            long n = draws.LastValue, sp = setPass.LastValue;
            bool valid = draws.Valid;
            draws.Dispose(); setPass.Dispose();
            cam.targetTexture = null;
            Object.Destroy(rt);
            if (!valid) Assert.Ignore("render counters unavailable in this player");
            Debug.Log($"[MR-TEST] draw budget: draws={n} setPass={sp} hint={Mr.HintVisible} catcher={Mr.Lighting.CatcherVisible} (budget {MixedRealityDraws})");
            Assert.That(n, Is.GreaterThan(0));
            Assert.That(n, Is.LessThanOrEqualTo(MixedRealityDraws));
        }
    }
}
#endif
