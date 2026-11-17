// The menu's instrument portraits (12-new-instruments/platform.md 5.2, Core/Render/PortraitStudio): GPU runs
// (Tools/unity.sh test-play --gpu) render every loaded instrument's own model into the 1024 x 512 atlas and check each
// cell: rendered (Ready), not blank, the instrument filling 40-85 % of the portrait band, a mean luminance of 0.25-0.75;
// the rigs back on their own layers, the room's sun restored, the Rendering flag down; the per-portrait CPU cost
// logged. The atlas, each cell, and the Instruments grid (canvas and the seated view) are saved to $MUSICROOM_SHOTS
// (after $MUSICROOM_SHOTS_ROOT/menu; default Builds/Shots/menu). In this phase-0 copy the real Layout still hides the instruments outside My set
// (the display is being built in parallel), so the test shows every rig for its portrait and hides them again after.
// The headless fallback is MenuInstrumentGridTests.HeadlessThePortraitsFallBackToTheVectorIcons and the EditMode
// FallbackIconsDrawWithoutAGpu.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Menu
{
    public class PortraitTests
    {
        /// <summary>$MUSICROOM_SHOTS_ROOT/menu, else $MUSICROOM_SHOTS, else Builds/Shots/menu.</summary>
        public static string ShotDir
        {
            get
            {
                string dir = ShotDirs.FromRoot("menu") ?? System.Environment.GetEnvironmentVariable("MUSICROOM_SHOTS");
                if (string.IsNullOrEmpty(dir)) dir = ShotDirs.Default("menu");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        TestBoot _boot;
        GridLayoutProbe _layout;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            _boot = new TestBoot(headless: false);
            _layout = new GridLayoutProbe();
            _boot.Options.ExtraModules.Add(_layout);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MenuBootGuard.Stop();
            yield return _boot.Stop();
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, !rt.sRGB);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        static void Save(Texture2D t, string name) => File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), t.EncodeToPNG());

        /// <summary>A portrait band against its backdrop paper: <paramref name="coverage"/> = the share of pixels that are
        /// not the paper (any channel more than 10/255 off); <paramref name="fill"/> = how much of the portrait the
        /// instrument fills, the larger of its pixel bounding box's width share and height share (a harp is tall, a
        /// marimba wide: each fills the frame along its own limiting axis); <paramref name="offCentre"/> = the box centre's
        /// distance from the band centre, in band widths / heights (the larger); <paramref name="luminance"/> = the mean
        /// Rec. 709 luminance of the sRGB values (0..1).</summary>
        public static void Measure(Color32[] px, int w, RectInt band, Color paper, out float coverage, out float fill, out float offCentre, out float luminance)
        {
            int pr = Mathf.RoundToInt(paper.r * 255f), pg = Mathf.RoundToInt(paper.g * 255f), pb = Mathf.RoundToInt(paper.b * 255f);
            int n = 0, ink = 0, x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
            double lum = 0;
            for (int y = band.y; y < band.yMax; y++)
                for (int x = band.x; x < band.xMax; x++)
                {
                    Color32 c = px[y * w + x];
                    n++;
                    lum += (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0;
                    if (Mathf.Abs(c.r - pr) <= 10 && Mathf.Abs(c.g - pg) <= 10 && Mathf.Abs(c.b - pb) <= 10) continue;
                    ink++;
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            coverage = n > 0 ? ink / (float)n : 0f;
            luminance = n > 0 ? (float)(lum / n) : 0f;
            if (ink == 0) { fill = 0f; offCentre = 1f; return; }
            float fw = (x1 - x0 + 1) / (float)band.width, fh = (y1 - y0 + 1) / (float)band.height;
            fill = Mathf.Max(fw, fh);
            float cx = ((x0 + x1 + 1) * 0.5f - band.x) / band.width - 0.5f, cy = ((y0 + y1 + 1) * 0.5f - band.y) / band.height - 0.5f;
            offCentre = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cy));
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator EveryLoadedInstrumentGetsAFramedLitPortrait()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            var app = App.Current;
            float t0 = Time.realtimeSinceStartup;
            while ((app.Rig.State == XrState.Starting || !_layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            var m = (MenuModule)app.Get<IMenuPanel>();
            for (int i = 0; i < 3 && m.Portraits == null; i++) yield return null;
            PortraitStudio studio = m.Portraits;
            Assert.That(studio, Is.Not.Null, "made once the first arrangement is done");
            Assert.IsTrue(studio.Available, "a GPU, not headless: the render-request path");
            Assert.That(studio.Atlas.width, Is.EqualTo(1024));
            Assert.That(studio.Atlas.height, Is.EqualTo(512));
            Assert.That(studio.Atlas.format, Is.EqualTo(RenderTextureFormat.ARGB32), "RGBA32: 2 MiB");
            Assert.That(studio.Rendered, Is.EqualTo(0), "nothing before a grid was shown (no start panel in this boot)");

            // Show every rig (this copy's Layout hides the ones outside My set), note what each renderer's layer was.
            var rigs = app.Get<IRigRegistry>();
            var shown = new List<GameObject>();
            var layers = new Dictionary<Renderer, int>();
            foreach (var d in app.Registry.All)
            {
                if (!app.Registry.IsLoaded(d.Kind)) continue;
                Rig rig = rigs.Get(d.Rig);
                Assert.That(rig, Is.Not.Null, d.Id);
                if (!rig.Group.gameObject.activeSelf) { rig.Group.gameObject.SetActive(true); shown.Add(rig.Group.gameObject); }
                foreach (var r in rig.Group.GetComponentsInChildren<Renderer>(true)) layers[r] = r.gameObject.layer;
            }
            Light sun = RenderSettings.sun;

            m.Show();
            m.Go(MenuScreen.Instruments);
            int paints = m.Paints;
            t0 = Time.realtimeSinceStartup;
            int frames = 0;
            while (studio.AnyPending && Time.realtimeSinceStartup - t0 < 5f) { yield return null; frames++; }
            Assert.IsFalse(studio.AnyPending, "every portrait rendered");
            Assert.That(frames, Is.GreaterThanOrEqualTo(studio.Rendered - 1), "one portrait per frame");
            yield return null;
            yield return null;
            Assert.That(m.Paints, Is.LessThanOrEqualTo(paints + 2), "the grid repaints once when the portraits are in (plus the opening)");
            Assert.IsFalse(PortraitStudio.Rendering);
            Assert.That(RenderSettings.sun, Is.SameAs(sun), "the room's sun is back");
            foreach (var kv in layers) if (kv.Key != null) Assert.That(kv.Key.gameObject.layer, Is.EqualTo(kv.Value), kv.Key.name + " back on its layer");
            Debug.Log($"[Menu] portraits: {studio.Rendered} rendered over {frames} frames, last {studio.LastMs:F2} ms, max {studio.MaxMs:F2} ms CPU each (editor)");

            Texture2D atlas = Read(studio.Atlas);
            Save(atlas, "portrait-atlas");
            Color32[] px = atlas.GetPixels32();
            var report = new System.Text.StringBuilder();
            foreach (var d in app.Registry.All)
            {
                if (!app.Registry.IsLoaded(d.Kind)) continue;
                int cell = PortraitStudio.CellOf(app.Registry, d.Kind);
                Assert.That(studio.StateOf(cell), Is.EqualTo(PortraitStudio.CellState.Ready), d.Id);
                RectInt band = PortraitStudio.CellPixels(cell);
                Measure(px, atlas.width, band, MenuStyle.PhotoPaper, out float coverage, out float fill, out float off, out float lum);
                var cellTex = new Texture2D(band.width, band.height, TextureFormat.RGBA32, false);
                cellTex.SetPixels(atlas.GetPixels(band.x, band.y, band.width, band.height));
                cellTex.Apply();
                Save(cellTex, "portrait-" + d.Id);
                Object.Destroy(cellTex);
                report.Append($"{d.Id} fill {fill:P0} coverage {coverage:P0} off-centre {off:P0} lum {lum:F2}; ");
                Assert.That(coverage, Is.GreaterThan(0.02f), d.Id + ": not blank");
                Assert.That(fill, Is.InRange(0.40f, 0.85f), d.Id + ": the instrument fills 40-85 % of its portrait");
                Assert.That(off, Is.LessThan(0.08f), d.Id + ": centred");
                Assert.That(lum, Is.InRange(0.25f, 0.75f), d.Id + ": mean luminance");
            }
            Debug.Log("[Menu] portrait cells: " + report);
            // The cell's margins outside the band stay paper.
            Color32 corner = atlas.GetPixel(2, 2);
            Assert.That(Mathf.Abs(corner.r - Mathf.RoundToInt(MenuStyle.PhotoPaper.r * 255f)), Is.LessThanOrEqualTo(3), "the atlas is cleared to the backdrop paper");
            Color32 back = atlas.GetPixel(PortraitStudio.CellPixels(0).x + 2, PortraitStudio.CellPixels(0).y + 2);
            Assert.That(Mathf.Abs(back.r - Mathf.RoundToInt(MenuStyle.PhotoPaper.r * 255f)) + Mathf.Abs(back.g - Mathf.RoundToInt(MenuStyle.PhotoPaper.g * 255f)), Is.LessThanOrEqualTo(6),
                "the camera clears to the same paper (sRGB): the portrait has no seam with its margins");

            // The grid with its portraits: the painted canvas and the seated view (the open animation finished).
            foreach (var go in shown) go.SetActive(false);
            t0 = Time.realtimeSinceStartup;
            while (m.AnimK < 1f && Time.realtimeSinceStartup - t0 < 1f) yield return null;
            m.Redraw();
            yield return null;
            yield return null;
            var canvas = Read(m.FaceTexture);
            Save(canvas, "canvas-instruments-grid");
            var view = MenuScreenshotTests.SeatedView(app.Rig.HeadCamera, 1280, 960);
            Save(view, "seated-instruments-grid");
            Object.Destroy(view);
            Object.Destroy(canvas);
            Object.Destroy(atlas);

            // The other states of the grid, for the eye: a set with an instrument that has options (its chip), the header
            // naming the member that made room, "No room", One alone, and the options page itself.
            InstrumentDescriptor th = app.Registry.Get(InstrumentKind.Marimba);
            InstrumentOption[] keep = th.Options;
            th.Options = new[]
            {
                new InstrumentOption { Key = "help", Label = "Note help", Choices = new[] { "Off", "On" }, Default = 1, Note = "Keeps held notes in tune" },
                new InstrumentOption { Key = "tone", Label = "Marimba tone", Choices = new[] { "Warm", "Classic", "Bright" }, Default = 1 },
            };
            try
            {
                var c = m.Controller;
                _layout.Join = new JoinPreview(JoinAction.Replace, InstrumentKind.Drums);
                double at = app.Clock.NowMs + 300;
                int tile = -1;
                for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == RowKind.Tile && c.Row(i).Inst == InstrumentKind.Bowls) tile = i;
                c.BeginPokes(at); c.Fire(tile); c.EndPokes();
                yield return SaveCanvas(m, "canvas-instruments-replaced");
                Assert.That(c.HeaderNote, Is.EqualTo("Drums went back to the left wall"));
                _layout.Join = new JoinPreview(JoinAction.Alone);
                for (int i = 0; i < c.RowCount; i++) if (c.Row(i).Id == RowKind.Tile && c.Row(i).Inst == InstrumentKind.Marimba) tile = i;
                c.BeginPokes(at + 400); c.Fire(tile); c.EndPokes();
                yield return SaveCanvas(m, "canvas-instruments-noroom");
                Assert.That(c.HeaderNote, Is.EqualTo("No room: take one out first"));
                _layout.Join = null;
                _layout.Mode = LayoutMode.Single;
                _layout.Current = InstrumentKind.Marimba;
                c.Changed();
                yield return SaveCanvas(m, "canvas-instruments-alone");
                c.OpenOptions(th);
                yield return SaveCanvas(m, "canvas-instruments-options");
                view = MenuScreenshotTests.SeatedView(app.Rig.HeadCamera, 1280, 960);
                Save(view, "seated-instruments-options");
                Object.Destroy(view);
            }
            finally { th.Options = keep; }
            Debug.Log("[Menu] portrait shots saved to " + ShotDir);
        }

        static IEnumerator SaveCanvas(MenuModule m, string name)
        {
            m.Redraw();
            yield return null;
            yield return null;
            var t = Read(m.FaceTexture);
            Save(t, name);
            Object.Destroy(t);
        }

        [UnityTest, Category("Graphics")]
        public IEnumerator PortraitsRenderOnePerFrameWhileTheStartPanelWaits()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("needs a GPU: Tools/unity.sh test-play --gpu");
            // A second boot with the launch panel, as the app starts.
            yield return _boot.Stop();
            _boot = new TestBoot(headless: false, startPanel: true);
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            var app = App.Current;
            var m = (MenuModule)app.Get<IMenuPanel>();
            float t0 = Time.realtimeSinceStartup;
            while (!m.StartShowing && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.IsTrue(m.StartShowing, "the start panel is up");
            Assert.That(m.Portraits, Is.Not.Null);
            // Every instrument whose rig is in the room now (in this copy: My set's) gets its portrait, one per frame.
            var rigs = app.Get<IRigRegistry>();
            int expected = 0;
            foreach (var d in app.Registry.All)
                if (app.Registry.IsLoaded(d.Kind) && rigs.Get(d.Rig) != null && rigs.Get(d.Rig).Group.gameObject.activeInHierarchy) expected++;
            Assert.That(expected, Is.GreaterThanOrEqualTo(3));
            int last = m.Portraits.Rendered, frames = 0, maxStep = 0;
            t0 = Time.realtimeSinceStartup;
            while (m.Portraits.Rendered < expected && Time.realtimeSinceStartup - t0 < 3f)
            {
                yield return null;
                frames++;
                maxStep = Mathf.Max(maxStep, m.Portraits.Rendered - last);
                last = m.Portraits.Rendered;
            }
            Assert.That(m.Portraits.Rendered, Is.EqualTo(expected), "rendered while the start panel waits, before any grid was shown");
            Assert.That(maxStep, Is.LessThanOrEqualTo(1), "never more than one portrait in a frame");
            Assert.IsTrue(m.StartShowing, "still the start panel");
            Assert.IsFalse(m.Controller.GridSeen);
            Debug.Log($"[Menu] start-panel portraits: {expected} over {frames} frames, max {m.Portraits.MaxMs:F2} ms CPU each (editor)");
        }
    }
}
