// Visual check of the harp (Graphics category, Tools/unity.sh test-play --gpu): boots the app desktop-style through
// TestBoot with manual frames (no headset, scripted hands untracked so no debug hand is drawn; every app frame is an
// explicit 1/72 s step, so a capture shows exactly the state the test made), and renders the head camera to PNGs:
//   harp-seated.png        the seated reference view (seat 1.2 m, Quest-like 90 deg, looking at string
//                          10's play point): all 22 strings in view, none hidden by the pillar or soundbox;
//   harp-seated-left.png   the desktop seat (0, 1.2, -0.1) looking left at the harp (the reference seat-left view);
//   harp-pluck-closeup.png a string mid-vibration (plucked across the plane), close up, with its warm flash;
//   harp-pluck-seat.png    the same pluck from the seat; harp-target.png a lesson's blue target string;
//   harp-hover.png         a fingertip 2 cm short of a string with its hover glow; harp-single.png single mode.
// Output: $MUSICROOM_SHOTS_ROOT/harp, else $MUSICROOM_SHOTS_DIR, else Builds/Shots/harp. The Room module is a stub in
// a module clone: when the scene has fewer than three lights the test adds the room's floor lamp and fire light (07
// lights, Unity values) so the wood reads as in the full room.
using System;
using System.Collections;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Harp;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Harp
{
    [Category("Graphics")]
    public class HarpVisualTests
    {
        const int W = 1280, H = 1200;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        FakeHandSource _fake;
        readonly System.Collections.Generic.List<GameObject> _extra = new System.Collections.Generic.List<GameObject>();

        static string ShotDir()
        {
            string env = ShotDirs.FromRoot("harp") ?? Environment.GetEnvironmentVariable("MUSICROOM_SHOTS_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return ShotDirs.Default("harp");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _extra) if (go != null) Object.Destroy(go);
            _extra.Clear();
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        void AddLight(string name, Color color, float intensity, float range, Vector3 pos)
        {
            var go = new GameObject("Test " + name);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            go.transform.position = pos;
            _extra.Add(go);
        }

        /// <summary>Renders the camera into an sRGB render texture (MSAA 4) and writes a PNG; returns the non-background share.</summary>
        static float Capture(Camera cam, string file)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            cam.targetTexture = prev;
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Color32[] px = tex.GetPixels32();
            Color32 bg = px[0];
            int differ = 0;
            for (int i = 0; i < px.Length; i += 7) if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 24) differ++;
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Debug.Log($"[Harp] shot {file}");
            return differ / (px.Length / 7f);
        }

        static void Aim(DesktopRigFallback desk, Vector3 eye, Vector3 target, float fov)
        {
            Vector3 e = Quaternion.LookRotation(target - eye).eulerAngles;
            desk.yaw = e.y;
            desk.pitch = e.x > 180f ? e.x - 360f : e.x;
            desk.fov = fov;
        }

        /// <summary>Boots a rendering app (TestBoot: fresh settings store, temp data folder, manual frames), waits for the
        /// desktop seat, steps the first arrange, and lights the stub room like the full room when the Room module is not in
        /// this clone.</summary>
        IEnumerator BootSeated()
        {
            _boot = new TestBoot(headless: false, manualFrames: true);
            _fake = _boot.Hands;
            yield return _boot.Start();
            App app = App.Current;
            var layout = app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while (app.Rig.State != XrState.Desktop && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.That(app.Rig.State, Is.EqualTo(XrState.Desktop));
            _boot.Step(Dt);
            Assert.That(layout.FirstArrangeDone, Is.True);
            if (Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length < 3)
            {
                AddLight("floor lamp", new Color32(0xff, 0xd6, 0xa0, 0xff), 1.0804f, 5f, new Vector3(-2.4f, 1.6f, 0.52f));
                AddLight("fire", new Color32(0xff, 0x8f, 0x3c, 0xff), 2.0747f, 6f, new Vector3(0.3f, 0.5f, -3.63f));
            }
        }

        /// <summary>Draw calls the head camera makes over a few frames (profiler "Draw Calls Count", as DrawBudgetTests).</summary>
        static IEnumerator CountDraws(Camera cam, long[] result)
        {
            var rt = new RenderTexture(1024, 1024, 24) { name = "HarpDraws" };
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            for (int i = 0; i < 6; i++) yield return null;
            result[0] = draws.Valid ? draws.LastValue : -1;
            draws.Dispose();
            cam.targetTexture = prev;
            rt.Release();
            Object.Destroy(rt);
        }

        /// <summary>ARCHITECTURE.md sections 14 / 17: the harp draws at most 6 times (4 frame materials, the one strings
        /// mesh, the contact shadow), measured with a plucked, lit string and a lesson target showing: the scene's draws
        /// with the harp in view minus the same view with the harp switched off.</summary>
        [UnityTest]
        public IEnumerator TheHarpStaysInsideItsDrawBudget()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (run Tools/unity.sh test-play --gpu)");
            yield return BootSeated();
            App app = App.Current;
            var harp = (HarpInstrument)app.Get<IHarpApi>();
            Camera cam = app.Rig.HeadCamera;
            DesktopRigFallback desk = app.Rig.Desktop;
            Aim(desk, app.Head.position, harp.Root.TransformPoint(new Vector3(-0.33f, 0.75f, 0f)), 90f);
            yield return null;
            // Every string state at once: ringing (deformed), flashing, a lesson target.
            for (int i = 0; i < 22; i += 3) harp.PlaybackPluck(i, 0.8f, 0.4f, 0.5f, "across");
            harp.Highlight(5, 1f);
            _boot.Step(Dt);
            Assert.That(harp.ShownGlow(5).b, Is.GreaterThan(0f), "the target is lit");
            var on = new long[1];
            yield return CountDraws(cam, on);
            if (on[0] < 0) Assert.Ignore("render counters unavailable in this player");
            foreach (Renderer r in harp.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(r.HasPropertyBlock(), r.name + " has a MaterialPropertyBlock");
            harp.Root.gameObject.SetActive(false);
            var off = new long[1];
            yield return CountDraws(cam, off);
            harp.Root.gameObject.SetActive(true);
            long harpDraws = on[0] - off[0];
            Debug.Log($"[Harp] draws with the harp {on[0]}, without {off[0]}: harp = {harpDraws} (budget 6)");
            Assert.That(harpDraws, Is.GreaterThanOrEqualTo(5), "the harp is in view and drawn");
            Assert.That(harpDraws, Is.LessThanOrEqualTo(6));
        }

        [UnityTest]
        public IEnumerator HarpFromTheSeatAndAPluckedString()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU (run Tools/unity.sh test-play --gpu)");
            yield return BootSeated();
            App app = App.Current;
            var harp = (HarpInstrument)app.Get<IHarpApi>();
            IHarpApi api = harp;
            Camera cam = app.Rig.HeadCamera;
            DesktopRigFallback desk = app.Rig.Desktop;
            Assert.That(cam, Is.Not.Null);
            Assert.That(desk, Is.Not.Null);
            string dir = ShotDir();
            Transform head = app.Head;
            Vector3 Play(int i) => harp.Root.TransformPoint(HarpGeometry.PlayPoint(i));

            // 1. The seat shot: the camera at the seat the harp was arranged around (the desktop eye,
            //    1.2 m), Quest-like 90 deg, looking at string 10's play point.
            Assert.That(head.localPosition, Is.EqualTo(DesktopRigFallback.EyePosition), "the arrange seat");
            Aim(desk, head.position, Play(10), 90f);
            yield return null;
            // Warm-up: the very first render request after boot comes out blue-cast (environment lighting not settled
            // yet); render once, let two frames pass, then take the shot.
            Capture(cam, Path.Combine(dir, "warmup.png"));
            yield return null;
            yield return null;
            File.Delete(Path.Combine(dir, "warmup.png"));
            float seated = Capture(cam, Path.Combine(dir, "harp-seated.png"));
            Assert.That(seated, Is.GreaterThan(0.05f), "the seated view is not empty");

            // 2. The desktop seat looking left at the harp (the reference seat-left framing).
            head.localPosition = DesktopRigFallback.EyePosition;
            Aim(desk, head.position, harp.Root.TransformPoint(new Vector3(-0.33f, 0.85f, 0f)), 70f);
            yield return null;
            Capture(cam, Path.Combine(dir, "harp-seated-left.png"));

            // 3. A string mid-vibration, framed whole from 0.6 m in front: aim first, then pluck it hard across the plane,
            //    run one deterministic frame and render at once (E8: the blur's two edges at the envelope of the motion).
            HarpString s = harp.Play.Strings[11];
            Vector3 midLocal = Vector3.Lerp(s.A, s.B, 0.5f);
            Vector3 mid = harp.Root.TransformPoint(midLocal);
            Vector3 from = harp.Root.TransformPoint(midLocal + new Vector3(0f, 0f, 0.6f));
            head.position = from;
            Aim(desk, from, mid, 70f);
            yield return null;
            api.PlaybackPluck(11, 1f, 0.5f, 0.9f, "across");
            _boot.Step(Dt);
            Assert.That(s.Amp, Is.GreaterThan(0.003f), "ringing");
            Assert.That(s.Flash, Is.GreaterThan(0.9f), "lit");
            Vector3[] v = harp.StringsMesh.vertices;
            Assert.That(v.Length, Is.EqualTo(3 * harp.CopyVertexCount), "cores + two blur edges");
            float bow = 0f, others = 0f, core = 0f;
            for (int i = 0; i < 22; i++)
                for (int k = 0; k < harp.VerticesPerString; k++)
                {
                    float d = Mathf.Max(Vector3.Distance(v[harp.EdgeStart(i, true) + k], harp.RestLocal(i, k)),
                                        Vector3.Distance(v[harp.EdgeStart(i, false) + k], harp.RestLocal(i, k)));
                    if (i == 11) bow = Mathf.Max(bow, d); else others = Mathf.Max(others, d);
                    core = Mathf.Max(core, Vector3.Distance(v[harp.VertexStart(i) + k], harp.RestLocal(i, k)));
                }
            Assert.That(bow, Is.GreaterThan(0.0025f), "the uploaded strings mesh blurs string 11 to its envelope (mm): " + bow * 1000f);
            Assert.That(others, Is.LessThan(1e-5f), "and only string 11");
            Assert.That(core, Is.LessThan(1e-5f), "the cores stay on their rest lines (no alternating bow)");
            Assert.That(harp.ShownAlpha(11, 8, true), Is.LessThan(0.6f), "the core fades where the string swings");
            Assert.That(harp.ShownAlpha(11, 8, false), Is.GreaterThan(0.3f), "the edges appear");
            Assert.That(harp.ShownAlpha(10, 8, true), Is.EqualTo(1f), "a still string is solid");
            var glowUv = new System.Collections.Generic.List<Vector4>();
            harp.StringsMesh.GetUVs(1, glowUv);
            Assert.That(glowUv[harp.VertexStart(11)].x, Is.GreaterThan(0.15f), "the warm flash (a quarter of v1's) is in string 11's glow vertices");
            Capture(cam, Path.Combine(dir, "harp-pluck-closeup.png"));

            // 4. The same pluck from the seat (past the 70 ms retrigger guard first).
            _boot.Step(Dt, 6);
            head.localPosition = DesktopRigFallback.EyePosition;
            Aim(desk, head.position, Play(10), 90f);
            yield return null;
            int plucks = harp.Play.Plucks;
            api.PlaybackPluck(11, 1f, 0.5f, 0.9f, "across");
            Assert.That(harp.Play.Plucks, Is.EqualTo(plucks + 1), "plucked again");
            _boot.Step(Dt);
            Capture(cam, Path.Combine(dir, "harp-pluck-seat.png"));

            // 5. A lesson's target string (purple) once the flash has faded, and 6. a fingertip's hover glow near string 14.
            for (int k = 0; k < 24; k++) { api.Highlight(5, 1f); _boot.Step(Dt); }
            Assert.That(harp.ShownGlow(5).b, Is.EqualTo(HarpPlay.TargetGlow.b).Within(1e-4f), "the target glows purple");
            Assert.That(harp.ShownGlow(11).r, Is.EqualTo(0f), "the flash has faded");
            Capture(cam, Path.Combine(dir, "harp-target.png"));
            Vector3 fingers = harp.Root.TransformDirection(Vector3.back);
            _fake.SetPose(HandSide.Right, HandPose.OpenFacing(HandSide.Right, Vector3.zero, fingers, Vector3.down).WithPointing()
                .WithIndexTipAt(harp.Root.TransformPoint(HarpGeometry.PlayPoint(14) + new Vector3(0f, 0f, 0.02f))));
            _boot.Step(Dt, 2);
            Assert.That(app.HoverGlow.Drawn, Is.GreaterThanOrEqualTo(1), "a hover glow is drawn");
            Capture(cam, Path.Combine(dir, "harp-hover.png"));
            _fake.SetTracked(HandSide.Right, false);

            // 7. Single mode: the harp alone in front of the player, strings centred. Placed here as Layout places it
            //    (seat frame, HomeSingle, yaw 180) directly, so the stub Layout of a module clone does not matter.
            var seat = new SeatFrame { Head = DesktopRigFallback.EyePosition, YawDeg = 0f };
            foreach (Rig r in app.Get<IRigRegistry>().Rigs)
                if (r.Id != RigId.Harp && r.Id != RigId.Table && r.Group != null) r.Group.gameObject.SetActive(false);
            harp.Root.SetPositionAndRotation(seat.Place(harp.Rig.HomeSingle), Quaternion.Euler(0f, seat.Yaw(harp.Rig.HomeYawSingle), 0f));
            head.localPosition = DesktopRigFallback.EyePosition;
            Aim(desk, head.position, Play(10), 90f);
            yield return null;
            Capture(cam, Path.Combine(dir, "harp-single.png"));
            Debug.Log($"[Harp] shots written to {dir}");
        }
    }
}
