// The singing bowls in the real app (Category Graphics: Tools/unity.sh test-play --gpu), phase B: every real module, a
// simulated headset, manual frames, the platform's Layout placing the bowls. Pictures: on display on the window sill (the
// first arrangement), mid-glide from the sill to the seat, at the seat in a real set (piano, drums, bowls: the
// FrontLeft pocket), a struck bowl rocking, the stick in the hand above a rim, the stand hidden as on a real table in
// mixed reality (the cushions and the stick stay), and the menu's portrait. Written to $MUSICROOM_SHOTS, else
// $MUSICROOM_SHOTS_ROOT/bowls/phase-b, else Builds/Shots/bowls/phase-b. Also checks the draws (5 at the
// seat, 2 on display), the triangle budgets the descriptor promises, that nothing emits light, and logs the decor near
// the displayed bowls (the ivy on the sill).
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusicRoom.Bowls;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Menu;
using MusicRoom.Room;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.BowlsShots
{
    [Category("Graphics")]
    public class BowlsShotTests
    {
        const int Width = 1280, Height = 800;
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            GC.Collect();
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is LayoutModule lm) _layout = lm;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            GC.Collect();
        }

        static string OutDir()
        {
            string env = Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
            string root = ShotDirs.FromRoot("bowls");
            string dir = !string.IsNullOrEmpty(env) ? env : root != null ? Path.Combine(root, "phase-b")
                : Path.Combine(ShotDirs.Default("bowls"), "phase-b");
            Directory.CreateDirectory(dir);
            return dir;
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { _boot.Step(Dt); yield return null; }
        }

        static int Draws(GameObject root) => root.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled && r.gameObject.activeInHierarchy);
        static int Tris(GameObject root) => root.GetComponentsInChildren<MeshFilter>().Where(f => f.gameObject.activeInHierarchy).Sum(f => f.sharedMesh.triangles.Length / 3);

        static Bounds WorldBounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator TheBowlsOnDisplayGlidingAtTheSeatAndInTheMenu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("no GPU (run test-play --gpu)"); yield break; }
            RoomModule room = null;
            foreach (var m in _app.Modules) if (m is RoomModule rm) room = rm;
            float t0 = Time.realtimeSinceStartup;
            while ((_app.Rig.State == XrState.Starting || !_layout.FirstArrangeDone || (room != null && (room.TexturesPending > 0 || !room.ProbeRendered)))
                   && Time.realtimeSinceStartup - t0 < 300f)
            {
                _boot.Step(Dt);
                yield return null;
            }
            yield return Frames(30);
            BowlsInstrument b = null;
            foreach (IInstrument i in _app.Instruments) if (i.Kind == InstrumentKind.Bowls) b = i as BowlsInstrument;
            Assert.That(b, Is.Not.Null, "the real bowls module is loaded");
            InstrumentDescriptor d = _app.Registry.Get(InstrumentKind.Bowls);
            Assert.That(d.Placeholder, Is.False);
            foreach (var mat in new[] { b.View.BronzeMat, b.View.WoodMat })
                Assert.That(mat.IsKeywordEnabled("_EMISSION"), Is.False, mat.name + " does not glow");

            string dir = OutDir();
            var camGo = new GameObject("BowlsShotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = XrMode.VrBackground;
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 40f; cam.allowHDR = false; cam.allowMSAA = true;
            cam.cullingMask = _app.Rig.HeadCamera.cullingMask;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            try
            {
                Vector3 eye = _app.Head.position;
                Vector3 fwd = _app.Head.forward; fwd.y = 0f; fwd.Normalize();

                // ---- 1. on display: the window sill (the first arrangement leaves the bowls out of the default set)
                Assert.That(_layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Display), "on display at start");
                Assert.That(b.Stowed, Is.True);
                Transform rig = b.transform;
                Debug.Log($"[BowlsShots] on display at {rig.position.ToString("F3")} yaw {rig.eulerAngles.y:F1} (spot {d.Stow.SpotId} {d.Stow.Pose.ToString("F2")})");
                int shownDraws = Draws(b.View.Root), shownTris = Tris(b.View.Root);
                Bounds shown = WorldBounds(b.View.Root);
                Debug.Log($"[BowlsShots] on display: {shownDraws} draws, {shownTris} triangles, bounds {shown.min.ToString("F3")} .. {shown.max.ToString("F3")}");
                Assert.That(shownDraws, Is.LessThanOrEqualTo(d.DrawsStowed));
                Assert.That(shownTris, Is.LessThanOrEqualTo(d.TrisStowed));
                Assert.That(shown.min.y, Is.GreaterThanOrEqualTo(BowlsLayout.SillTop - 0.002f), "standing on the sill, not in it");
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r.transform.IsChildOf(b.View.Root.transform)) continue;
                    string n = r.gameObject.name.ToLowerInvariant();
                    if (!(n.Contains("ivy") || n.Contains("plant") || n.Contains("lea") || n.Contains("candle") || n.Contains("curtain"))) continue;
                    Debug.Log($"[BowlsShots] decor {r.gameObject.name}: bounds {r.bounds.min.ToString("F2")} .. {r.bounds.max.ToString("F2")} (overlaps the displayed bowls' bounds: {r.bounds.Intersects(shown)})");
                }
                yield return Shot(cam, rt, readback, dir, "display_from_the_seat", eye, new Vector3(3.09f, 0.70f, -0.62f), 55f);
                yield return Shot(cam, rt, readback, dir, "display_window_sill", new Vector3(2.20f, 1.20f, -0.64f), new Vector3(3.09f, 0.68f, -0.64f), 45f);
                yield return Shot(cam, rt, readback, dir, "display_sill_with_ivy", RoomFrame.ToWorld(new Vector3(2.05f, 1.40f, 0.05f)), RoomFrame.ToWorld(new Vector3(3.08f, 0.68f, -1.15f)), 60f);   // room-frame views, turned (RoomFrame)
                yield return Shot(cam, rt, readback, dir, "display_window_wall", RoomFrame.ToWorld(new Vector3(-0.6f, 1.6f, -1.4f)), RoomFrame.ToWorld(new Vector3(2.8f, 0.6f, -1.3f)), 70f);

                // ---- 2. mid-glide: the bowls leave the sill for the seat
                Assert.IsTrue(_layout.Bring(InstrumentKind.Bowls, BringReason.Menu));
                RigGlide g = _layout.GlideOf(InstrumentKind.Bowls);
                Assert.That(g, Is.Not.Null);
                int half = Mathf.RoundToInt((g.Delay + g.Duration * 0.5f) / Dt);
                yield return Frames(half);
                Debug.Log($"[BowlsShots] glide mid-way: bowls at {rig.position.ToString("F2")}, {g.Length:F2} m in {g.Duration:F2} s, stowed {b.Stowed}");
                yield return Shot(cam, rt, readback, dir, "glide_midway", RoomFrame.ToWorld(new Vector3(0.9f, 1.9f, -2.6f)), rig.position + Vector3.up * 0.5f, 70f);
                yield return Shot(cam, rt, readback, dir, "glide_midway_from_the_seat", eye, rig.position + Vector3.up * 0.55f, 75f);
                for (int i = 0; i < 200 && _layout.AnyMoving; i++) yield return Frames(1);
                yield return Frames(10);

                // ---- 3. at the seat in a real set (piano, drums, bowls)
                Debug.Log($"[BowlsShots] my set: {string.Join(", ", _layout.MySet)}; bowls at {rig.position.ToString("F3")} yaw {rig.eulerAngles.y:F1}, slot {_layout.SlotOf(InstrumentKind.Bowls)}");
                Assert.That(_layout.PlaceOf(InstrumentKind.Bowls), Is.EqualTo(InstrumentPlace.Seat));
                Assert.That(b.IsPresent, Is.True);
                int seatDraws = Draws(b.View.Root), seatTris = Tris(b.View.Root);
                Debug.Log($"[BowlsShots] at the seat: {seatDraws} draws, {seatTris} triangles");
                Assert.That(seatDraws, Is.LessThanOrEqualTo(d.DrawsAtSeat));
                Assert.That(seatTris, Is.LessThanOrEqualTo(d.TrisAtSeat));
                Vector3 W(float x, float y, float z) => rig.TransformPoint(new Vector3(x, y, z));
                Vector3 centre = W(0f, 0.70f, -0.016f);
                yield return Shot(cam, rt, readback, dir, "seat_in_a_set", eye, eye + Quaternion.Euler(0f, -30f, 0f) * fwd - Vector3.up * 0.7f, 85f);
                yield return Shot(cam, rt, readback, dir, "seat_view", eye, centre, 70f);
                yield return Shot(cam, rt, readback, dir, "seat_set_overview", eye + Vector3.up * 0.9f - fwd * 1.3f, eye + fwd * 0.4f - Vector3.up * 0.9f, 80f);
                yield return Shot(cam, rt, readback, dir, "bowls_close", W(0.30f, 1.02f, -0.36f), W(0f, 0.68f, -0.01f), 50f);
                yield return Shot(cam, rt, readback, dir, "bowls_three_quarter", W(0.78f, 1.02f, -0.30f), W(0f, 0.50f, 0f), 48f);

                // a struck bowl rocks on its cushion (playback of a palm strike on the big bowl, 80 ms later)
                b.Play(new TakeEvent { type = BowlsEvents.Strike, i = 0, vel = 1.5f, kind = "palm", midi = (int)(Mathf.PI * 1000f * 0.8f), pos = 0.2f, lane = PlaybackLanes.Take });
                b.View.Tick(0.08f);
                yield return Shot(cam, rt, readback, dir, "big_bowl_rocking", W(0.48f, 0.80f, -0.30f), W(BowlsLayout.BowlXZ[0].x, 0.69f, BowlsLayout.BowlXZ[0].y), 40f);
                b.View.StopRocking(0);

                // the stick in the hand: its own renderer, held with its shaft against the middle bowl's rim
                Vector3 m = new Vector3(BowlsLayout.BowlXZ[1].x, BowlsLayout.RimY(1), BowlsLayout.BowlXZ[1].y);
                var held = Matrix4x4.TRS(m + new Vector3(-(BowlsLayout.R(1) + BowlsLayout.PujaRadius) - 0.010f, -0.06f, 0f), Quaternion.Euler(0f, 0f, 84f), Vector3.one);
                b.View.SetPujaPose(held);
                yield return Shot(cam, rt, readback, dir, "stick_on_the_rim", W(m.x - 0.30f, 0.98f, m.z - 0.28f), W(m.x + 0.05f, 0.68f, m.z), 45f);
                // on a real table in mixed reality: the stand hides, the cushions and the stick stay (the stick still held)
                b.SetOnSurface(true);
                int mrDraws = Draws(b.View.Root);
                bool stickShown = b.View.PujaRenderer.enabled && b.View.PujaRenderer.gameObject.activeInHierarchy;
                Debug.Log($"[BowlsShots] on a surface (MR): {mrDraws} draws, the stick {(stickShown ? "visible" : "HIDDEN")}");
                Assert.That(stickShown, Is.True, "the stick stays visible in MR");
                yield return Shot(cam, rt, readback, dir, "mr_stand_hidden_stick_held", W(m.x - 0.30f, 0.98f, m.z - 0.28f), W(m.x + 0.05f, 0.68f, m.z), 45f);
                b.View.SetPujaPose(null);
                yield return Shot(cam, rt, readback, dir, "mr_stand_hidden_at_rest", W(0.30f, 1.02f, -0.36f), W(0f, 0.66f, -0.01f), 50f);
                b.SetOnSurface(false);
            }
            finally
            {
                Object.Destroy(camGo); rt.Release(); Object.Destroy(rt); Object.Destroy(readback);
            }

            // ---- 4. the menu's portrait (the Instruments grid renders every loaded instrument's portrait)
            var menu = (MenuModule)_app.Get<IMenuPanel>();
            for (int i = 0; i < 3 && menu.Portraits == null; i++) yield return Frames(1);
            PortraitStudio studio = menu.Portraits;
            Assert.That(studio, Is.Not.Null);
            menu.Show();
            menu.Go(MenuScreen.Instruments);
            t0 = Time.realtimeSinceStartup;
            while (studio.AnyPending && Time.realtimeSinceStartup - t0 < 10f) yield return Frames(1);
            Assert.IsFalse(studio.AnyPending, "every portrait rendered");
            yield return Frames(2);
            var prevRt = RenderTexture.active;
            RenderTexture.active = studio.Atlas;
            var atlas = new Texture2D(studio.Atlas.width, studio.Atlas.height, TextureFormat.RGBA32, false, !studio.Atlas.sRGB);
            atlas.ReadPixels(new Rect(0, 0, studio.Atlas.width, studio.Atlas.height), 0, 0);
            atlas.Apply();
            RenderTexture.active = prevRt;
            int cell = PortraitStudio.CellOf(_app.Registry, InstrumentKind.Bowls);
            Assert.That(studio.StateOf(cell), Is.EqualTo(PortraitStudio.CellState.Ready));
            RectInt band = PortraitStudio.CellPixels(cell);
            PortraitTests.Measure(atlas.GetPixels32(), atlas.width, band, MenuStyle.PhotoPaper, out float coverage, out float fill, out float off, out float lum);
            var cellTex = new Texture2D(band.width, band.height, TextureFormat.RGBA32, false);
            cellTex.SetPixels(atlas.GetPixels(band.x, band.y, band.width, band.height));
            cellTex.Apply();
            File.WriteAllBytes(Path.Combine(dir, "menu_portrait.png"), cellTex.EncodeToPNG());
            Debug.Log($"[BowlsShots] menu portrait: fill {fill:P0} coverage {coverage:P0} off-centre {off:P0} lum {lum:F2}");
            Object.Destroy(cellTex); Object.Destroy(atlas);
            t0 = Time.realtimeSinceStartup;
            while (menu.AnimK < 1f && Time.realtimeSinceStartup - t0 < 2f) yield return Frames(1);
            menu.Redraw();
            yield return Frames(2);
            var view = MenuScreenshotTests.SeatedView(_app.Rig.HeadCamera, 1280, 960);
            File.WriteAllBytes(Path.Combine(dir, "menu_instruments_grid.png"), view.EncodeToPNG());
            Object.Destroy(view);
            Assert.That(coverage, Is.GreaterThan(0.02f), "not blank");
            Assert.That(fill, Is.InRange(0.40f, 0.85f), "the bowls fill 40-85 % of the portrait");
            Assert.That(off, Is.LessThan(0.08f), "centred");
            Assert.That(lum, Is.InRange(0.25f, 0.75f), "mean luminance");
        }

        IEnumerator Shot(Camera cam, RenderTexture rt, Texture2D readback, string dir, string name, Vector3 eye, Vector3 look, float fov)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            cam.fieldOfView = fov;
            cam.aspect = Width / (float)Height;
            cam.targetTexture = rt;
            yield return null;
            Shader.SetGlobalFloat(RoomModule.SnowPxId, Height * 0.5f);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, readback.EncodeToPNG());
            Debug.Log($"[BowlsShots] shot {name}: {path}");
        }
    }
}
