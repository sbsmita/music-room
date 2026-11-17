// GPU renders of the fireplace FIRE lights in the player's own room (Tools/unity.sh test-play --gpu; Room/Decor/MrFireplace.cs), in MRUK's MeshBedroom1 with the app's own Layout:
// one of MRUK's recorded rooms drawn as flat grey surfaces behind everything (the passthrough stand-in of MrShotsTests),
// FIRE on: the player's view turned to it, a nearer three-quarter view with a grey box standing beside it (the warm
// light on virtual things), and halfway through going out. PNGs go to Builds/Shots/mrfire/ (or $MUSICROOM_SHOTS_ROOT/
// mrfire). Each render is checked for the fire's warm pixels where the hearth is.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Meta.XR.MRUtilityKit;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.MR;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.MR
{
    [Category("Graphics")]
    public class MrFireplaceShotsTests : MrTestRig
    {
        static string ShotRoot(string m) => ShotDirs.FromRoot(m) ?? ShotDirs.Default(m);
        public static readonly string ShotDir = ShotRoot("mrfire");
        const int W = 1280, H = 720;
        protected override bool Headless => false;
        protected override bool TableOnEnter => false;
        protected override bool UseContractLayout => false;   // the app's own Layout: the player's set in front of them
        protected override void Prepare(MemorySettingsStore settings) => settings.SetInt(AppSettings.KeyTourDone, 1);
        readonly List<Object> _made = new List<Object>();
        GameObject _preview;
        RenderTexture _rt;

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var o in _made) if (o != null) Object.Destroy(o);
            _made.Clear();
            if (_preview != null) Object.Destroy(_preview);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AFireplaceInTheBedroom()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (run Tools/unity.sh test-play --gpu)");
            Directory.CreateDirectory(ShotDir);
            yield return EnterRoom("MeshBedroom1");   // a free wall in front, the instruments between
            BuildRoomPreview();
            var room = App.Get<IRoom>() as RoomModule;
            MrFireplace fire = room.MrFireplace;
            room.Set(AmbienceKind.Fire, true);
            yield return WaitFor(() => fire.Level >= 1f, 5f);
            for (int i = 0; i < 20; i++) yield return null;   // the smoke and embers up
            Assert.That(fire.Level, Is.EqualTo(1f));
            MrHearthPlace.Spot spot = fire.Spot;
            Debug.Log($"[MrFire-SHOT] {spot}");
            Assert.IsTrue(spot.OnWall, "against the bedroom's free wall");
            Vector3 head = App.Head.position, mid = spot.Pose.position + spot.Inward * 0.3f + Vector3.up * 0.5f;

            // 1. The player's view, turned to the fire.
            Look(head, mid);
            yield return Shot("mrfire-player-view.png", mid);

            // 2. Nearer, three quarters on, a grey box beside it catching its light.
            Vector3 side = Vector3.Cross(Vector3.up, spot.Inward);
            MakeBox(spot.Pose.position + spot.Inward * 1.1f + side * 1.25f + Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 0.5f));
            Vector3 eye = spot.Pose.position + spot.Inward * 2.4f - side * 0.9f + Vector3.up * 1.35f;
            Place(eye);
            Look(eye, mid + Vector3.down * 0.1f);
            yield return Shot("mrfire-near.png", mid);

            // 3. FIRE off, halfway out: the fire dying, the hearth sinking.
            room.Set(AmbienceKind.Fire, false);
            yield return WaitFor(() => fire.Level <= 0.45f, 3f);
            yield return Shot("mrfire-going-out.png", null);
            yield return WaitFor(() => !fire.Showing, 3f);
            Assert.IsFalse(fire.Showing);
            Place(DesktopRigFallback.EyePosition);
        }

        IEnumerator Shot(string file, Vector3? fireAt)
        {
            yield return null;
            var cam = App.Rig.HeadCamera;
            if (_rt == null) { _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "MR fire shot" }; _made.Add(_rt); }
            cam.targetTexture = _rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(ShotDir, file), tex.EncodeToPNG());
            Debug.Log($"[MrFire-SHOT] wrote {file}");
            if (fireAt.HasValue)
            {
                // warm pixels (the flames, the glow) about where the fire is on screen
                Vector3 sp = cam.WorldToViewportPoint(fireAt.Value + Vector3.down * 0.25f);
                cam.targetTexture = null;
                Assert.That(sp.z, Is.GreaterThan(0f), "the fire is in front of the camera");
                int warm = 0, cx = (int)(sp.x * W), cy = (int)(sp.y * H);
                for (int y = cy - 80; y <= cy + 80; y += 2)
                    for (int x = cx - 120; x <= cx + 120; x += 2)
                    {
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        Color c = tex.GetPixel(x, y);
                        if (c.r > 0.55f && c.r > c.g + 0.12f && c.g > c.b + 0.05f) warm++;
                    }
                Debug.Log($"[MrFire-SHOT] {file}: {warm} warm pixels about the fire at ({cx}, {cy})");
                Assert.That(warm, Is.GreaterThan(40), file + ": the fire shows");
            }
            cam.targetTexture = null;
            Object.Destroy(tex);
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

        void MakeBox(Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = centre;
            go.transform.localScale = size;
            var m = App.Materials.Lit(new Color(0.55f, 0.55f, 0.56f), 0.3f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            _made.Add(go);
        }

        // The recorded room as flat grey surfaces drawn first and never written to depth (MrShotsTests' stand-in for passthrough).
        void BuildRoomPreview()
        {
            _preview = new GameObject("Passthrough stand-in");
            var shader = Shader.Find(MrMaterials.UnlitShader);
            Vector3 light = new Vector3(0.4f, 0.8f, -0.45f).normalized;
            foreach (var a in Room.Anchors)
            {
                if (!a.PlaneRect.HasValue && !a.VolumeBounds.HasValue) continue;
                Color baseColor; int queue;
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
}
#endif
