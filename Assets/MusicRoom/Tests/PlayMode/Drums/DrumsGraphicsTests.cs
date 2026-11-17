// Visual check of the kit (ARCHITECTURE.md section 16, GPU tests): boots the app desktop-style through TestBoot (not
// headless, scripted hands drawn as joint spheres) and renders the kit from the seat and around it into PNGs for a human / agent to compare
// with the reference views (the seat's right, the plan; spec 07). The room module is still a stub
// in this clone, so two test lights stand in for its floor lamp and fire (spec 07 section 6) to make the shading
// readable; the kit's floor shadow is the Room module's (RigShadows), so it is absent here. It also checks the kit's draw
// budget (section 17: drums <= 25) twice: by counting the renderers that draw, and with the profiler's "Draw Calls
// Count" of a view framing the whole kit, minus the same view with the kit's renderers switched off. Run with
// Tools/unity.sh test-play --gpu; ignored without a GPU.
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Drums;
using MusicRoom.Hands;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Drums
{
    [Category("Graphics")]
    public class DrumsGraphicsTests
    {
        // MUSICROOM_SHOTS_ROOT (optional) redirects the shots of a whole run (e.g. a clone's own folder); default unchanged.
        static string ShotRoot(string m) => ShotDirs.FromRoot(m) ?? ShotDirs.Default(m);
        public static readonly string ShotDir = ShotRoot("drums");
        const int W = 1280, H = 720;

        GameObject _lights;
        TestBoot _boot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_lights != null) Object.Destroy(_lights);
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        [UnityTest]
        public IEnumerator RendersTheKitFromTheSeatAndAroundIt()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU: run Tools/unity.sh test-play --gpu"); yield break; }
            _boot = new TestBoot(headless: false);
            yield return _boot.Start();
            App app = _boot.App;
            FakeHandSource fake = _boot.Hands;
            var layout = app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (!layout.FirstArrangeDone && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.IsTrue(layout.FirstArrangeDone, "the desktop rig arranged the room");
            DrumsModule mod = null;
            foreach (var m in app.Modules) if (m is DrumsModule d) mod = d;
            DrumKit kit = mod.Kit;
            Directory.CreateDirectory(ShotDir);

            // Draw budget: the renderers of the kit (sticks included) that draw, one draw each (one material per renderer).
            int drawing = 0;
            foreach (var r in kit.GetComponentsInChildren<Renderer>(false)) if (r.enabled && !r.forceRenderingOff) drawing++;
            Debug.Log($"[Drums] kit renderers drawing: {drawing} (budget 25)");
            Assert.That(drawing, Is.LessThanOrEqualTo(25), "ARCHITECTURE.md section 17: drums <= 25 draws");

            // Stand-ins for the room's lamp and fire (ARCHITECTURE.md section 13, Unity coordinates: floor lamp #ffd6a0 1.08
            // range 5 at (-2.4, 1.6, .52), fire #ff8f3c 2.07 range 6 at (.3, .5, -3.63)).
            _lights = new GameObject("TestLights");
            AddPoint(_lights.transform, new Vector3(-2.4f, 1.6f, 0.52f), "#ffd6a0", 1.08f, 5f);
            AddPoint(_lights.transform, new Vector3(0.3f, 0.5f, -3.63f), "#ff8f3c", 2.07f, 6f);
            AddPoint(_lights.transform, new Vector3(1.2f, 2.4f, -0.2f), "#fff1df", 0.9f, 4f);   // a soft key over the kit

            var desk = app.Rig.Desktop;
            if (desk != null) desk.enabled = false;   // the test owns the camera
            Camera cam = app.Rig.HeadCamera;
            Transform head = cam.transform;
            float fov0 = cam.fieldOfView;
            Vector3 seat = DesktopRigFallback.EyePosition;   // the head the layout arranged around (rig at the origin)
            Vector3 kitCentre = kit.transform.TransformPoint(new Vector3(0.05f, 0.75f, 0.4f));
            DrumPiece snare = kit.Piece(DrumSpec.SnareId);

            // The room's one reflection probe (ARCHITECTURE.md section 13: realtime, via scripting, 128 px, rendered once),
            // so the metals reflect the dark room instead of the default sky.
            var probe = new GameObject("TestProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(_lights.transform, false);
            probe.transform.position = new Vector3(0.3f, 1.2f, -0.4f);
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.resolution = 128;
            probe.size = new Vector3(8f, 4f, 8f);
            int job = probe.RenderProbe();
            for (int i = 0; i < 30 && !probe.IsFinishedRendering(job); i++) yield return null;
            yield return null;
            Render(cam, null);   // the first render of a session comes out unlit: warm the pipeline up
            Shot(cam, head, "drums-seated", seat, kitCentre, 75f);
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            {
                Render(cam, null);
                yield return null;
                Debug.Log($"[Drums] seated view, whole scene: draws={draws.LastValue} (valid={draws.Valid}; scene budget 150)");
            }
            Shot(cam, head, "drums-seat-right", seat, seat + Quaternion.Euler(22f, 90f, 0f) * Vector3.forward, 75f);
            Shot(cam, head, "drums-front", kit.transform.TransformPoint(new Vector3(0.05f, 1.15f, 2.2f)), kitCentre, 50f);
            Shot(cam, head, "drums-side", kit.transform.TransformPoint(new Vector3(2.2f, 0.95f, 0.45f)), kitCentre + Vector3.down * 0.1f, 45f);
            Shot(cam, head, "drums-plan", kitCentre + Vector3.up * 2.4f, kitCentre, 45f, kit.transform.forward);
            Shot(cam, head, "drums-sticks-rest", snare.Group.TransformPoint(new Vector3(0f, 0.42f, -0.42f)), snare.Group.position, 40f);

            // The kit's share of the profiler's draw count (single-pass instanced: one draw per renderer for both eyes),
            // from a view that holds the whole kit: everything else in the frame (debug hands, the glow mesh) is the same
            // in both measurements.
            var kitDrawing = new System.Collections.Generic.List<Renderer>();
            foreach (var r in kit.GetComponentsInChildren<Renderer>(false)) if (r.enabled && !r.forceRenderingOff) kitDrawing.Add(r);
            Vector3 front = kit.transform.TransformPoint(new Vector3(0.05f, 1.3f, 2.4f));
            var withKit = new long[2];
            var withoutKit = new long[2];
            yield return CountDraws(cam, head, front, kitCentre, 70f, withKit);
            foreach (var r in kitDrawing) r.forceRenderingOff = true;
            yield return CountDraws(cam, head, front, kitCentre, 70f, withoutKit);
            foreach (var r in kitDrawing) r.forceRenderingOff = false;
            if (withKit[1] != 0 && withoutKit[1] != 0)
            {
                long kitDraws = withKit[0] - withoutKit[0];
                Debug.Log($"[Drums] profiler draws, kit framed: {withKit[0]} with the kit, {withoutKit[0]} without: kit = {kitDraws} (budget 25)");
                Assert.That(kitDraws, Is.LessThanOrEqualTo(25), "ARCHITECTURE.md section 17: drums <= 25 draws (profiler)");
                Assert.That(kitDraws, Is.GreaterThan(0), "the kit was in view");
            }
            else Debug.Log("[Drums] profiler render counters unavailable: draw budget checked by renderer count only");

            // A stick picked up by a scripted right hand (IDrumsApi.TryPickUp, as Layout calls it) and held over the snare.
            Drumstick stick = mod.Sticks[1];
            Quaternion rot = Quaternion.LookRotation(kit.transform.TransformDirection(new Vector3(0.15f, -0.55f, 1f).normalized), Vector3.up);
            fake.SetPose(HandSide.Right, HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing().WithPinch(0f).WithIndexTipAt(stick.GripWorld + Vector3.up * 0.02f));
            for (int i = 0; i < 3; i++) yield return null;
            HandState hand = app.Input.Hand(Handedness.Right);
            Assert.That(app.Get<IDrumsApi>().TryPickUp(hand, hand.Pointers[0].Tip), Is.EqualTo(StickPick.Grabbed));
            Assert.That(hand.Holding, Is.SameAs(stick), "the scripted pinch picked up the stick");
            var held = HandPose.Open(HandSide.Right, Vector3.zero, rot).WithPointing().WithPinch(0f);
            held = held.WithIndexTipAt(snare.Group.TransformPoint(new Vector3(-0.05f, 0.30f, -0.05f)));
            fake.SetPose(HandSide.Right, held);
            for (int i = 0; i < 3; i++) yield return null;
            Shot(cam, head, "drums-stick-held", seat, (stick.GripWorld + stick.TipWorld) * 0.5f, 60f);
            Shot(cam, head, "drums-stick-held-close", stick.GripWorld + kit.transform.TransformDirection(new Vector3(-0.35f, 0.15f, -0.25f)), (stick.GripWorld + stick.TipWorld) * 0.5f, 45f);
            cam.fieldOfView = fov0;
        }

        static void AddPoint(Transform parent, Vector3 pos, string hex, float intensity, float range)
        {
            var l = new GameObject("light").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.position = pos;
            l.type = LightType.Point;
            l.color = TextureUtil.Hex(hex);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        /// The profiler's draw count of the head camera rendering into a texture for a few frames from one view:
        /// result[0] = draws, result[1] = 1 when the counter was valid.
        static IEnumerator CountDraws(Camera cam, Transform head, Vector3 from, Vector3 at, float fov, long[] result)
        {
            head.position = from;
            head.rotation = Quaternion.LookRotation(at - from, Vector3.up);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(W, H, 24) { name = "DrumsDrawCount" };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            {
                for (int i = 0; i < 6; i++) yield return null;
                result[0] = draws.LastValue;
                result[1] = draws.Valid ? 1 : 0;
            }
            cam.targetTexture = prev;
            rt.Release();
            Object.Destroy(rt);
        }

        /// Renders the camera into a fresh RT (null name: a throwaway frame).
        static RenderTexture Render(Camera cam, string name)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1, name = name ?? "warm-up" };
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else
            {
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;
            }
            if (name != null) return rt;
            rt.Release();
            Object.Destroy(rt);
            return null;
        }

        static void Shot(Camera cam, Transform head, string name, Vector3 from, Vector3 at, float fov, Vector3? up = null)
        {
            head.position = from;
            head.rotation = Quaternion.LookRotation(at - from, up ?? Vector3.up);
            cam.fieldOfView = fov;
            RenderTexture rt = Render(cam, name);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            string path = Path.Combine(ShotDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            // Not a blank frame: the kit put real variation in the picture.
            var px = tex.GetPixels32();
            double sum = 0, sum2 = 0;
            for (int i = 0; i < px.Length; i += 7) { double v = px[i].r + px[i].g + px[i].b; sum += v; sum2 += v * v; }
            double n = (px.Length + 6) / 7, mean = sum / n, sd = System.Math.Sqrt(System.Math.Max(0, sum2 / n - mean * mean));
            Debug.Log($"[Drums] shot {path} mean={mean / 3:F1} sd={sd / 3:F1}");
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Assert.That(sd, Is.GreaterThan(6.0), name + " is not a blank frame");
        }
    }
}
