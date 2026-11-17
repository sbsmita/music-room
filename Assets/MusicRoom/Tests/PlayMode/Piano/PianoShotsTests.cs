// Visual check of the piano (GPU only: Tools/unity.sh test-play --gpu). Boots the app desktop-style (desktop rig, first
// arrange about the seat), checks the TMP-rendered label atlas against the label rules (every cell inked,
// middle C's coral bar under its name, clear texels carry colour, letter sizes on the key), and saves PNGs of the seat
// view, a C4-C5 close-up with the labels, a key mid-press, the stand from the side and the reference piano views
// (in the piano's frame) for an A/B against the reference renders.
// Output: $MUSICROOM_SHOTS_ROOT/piano, else $PIANO_SHOTS, else Builds/Shots/piano.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MusicRoom.Tests.PlayMode.Piano
{
    [Category("Graphics")]
    public class PianoShotsTests
    {
        const int Width = 1280, Height = 720;
        TestBoot _boot;
        App _app;
        PianoInstrument _piano;
        readonly List<GameObject> _temp = new List<GameObject>();

        static string OutDir()
        {
            string dir = ShotDirs.FromRoot("piano") ?? Environment.GetEnvironmentVariable("PIANO_SHOTS");
            if (string.IsNullOrEmpty(dir)) dir = ShotDirs.Default("piano");
            try { Directory.CreateDirectory(dir); return dir; }
            catch (Exception) { dir = Path.Combine(Application.temporaryCachePath, "piano-shots"); Directory.CreateDirectory(dir); return dir; }
        }

        // The whole app, desktop-style, through TestBoot (section 16: fresh settings, temp DataPath, real frames, renderers
        // on); the tour is marked done so Sunny's first-launch intro never walks into the pictures. The desktop rig and the
        // first arrange need a few frames, waited for here.
        [UnitySetUp]
        public IEnumerator Boot()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            _boot = new TestBoot(headless: false);
            _boot.Hands.SetTracked(HandSide.Left, false);
            _boot.Hands.SetTracked(HandSide.Right, false);   // no debug hands in the pictures
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            yield return _boot.Start();
            _app = _boot.App;
            foreach (var m in _app.Modules) if (m is PianoModule pm) _piano = pm.Piano;
            // The room's own point lights (ARCHITECTURE.md 13), which the Room stub does not build yet: the floor lamp and the fire.
            AddLight("FloorLamp", new Color32(0xff, 0xd6, 0xa0, 0xff), 1.08f, 5f, new Vector3(-2.4f, 1.6f, 0.52f));
            AddLight("Fire", new Color32(0xff, 0x8f, 0x3c, 0xff), 2.07f, 6f, new Vector3(0.3f, 0.5f, -3.63f));
            var layout = _app.Get<ILayoutService>();
            float t0 = Time.realtimeSinceStartup;
            while ((layout == null || !layout.FirstArrangeDone) && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            // The room's one reflection probe (ARCHITECTURE 13; the Room module renders it once the room is built), with the
            // Room's settings: without any probe URP reflects its default sky and the dark glossy case reads blue-grey.
            var probe = AddRoomProbe();
            int id = probe.RenderProbe();
            t0 = Time.realtimeSinceStartup;
            while (!probe.IsFinishedRendering(id) && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            yield return null;
        }

        ReflectionProbe AddRoomProbe()
        {
            var go = new GameObject("RoomReflectionProbe");
            _temp.Add(go);
            go.transform.position = new Vector3(0f, 1.2f, -1.48f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(6.4f, 2.9f, 5.2f);
            probe.center = new Vector3(0f, 0.25f, 0f);
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 25f;
            probe.hdr = false;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color32(0x05, 0x07, 0x0f, 0xff);
            probe.importance = 1;
            return probe;
        }

        void AddLight(string name, Color c, float intensity, float range, Vector3 pos)
        {
            var go = new GameObject(name);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            go.transform.position = pos;
            _temp.Add(go);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
            if (_boot != null) yield return _boot.Stop();
            _boot = null; _app = null; _piano = null;
        }

        // ---------------- the atlas (label checks) ----------------

        [UnityTest]
        public IEnumerator LabelAtlasIsInkedAndMarked()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU: run Tools/unity.sh test-play --gpu");
            Assert.That(PianoLabelAtlas.HasGlyphs, Is.True, "TMP rendered the letters");
            var atlas = PianoLabelAtlas.Get();
            var px = atlas.GetPixels32();
            int AW = atlas.width, AH = atlas.height, CW = PianoLabelAtlas.CellPx;
            Color32 At(int cell, int x, int y) { int x0 = (cell % PianoLabelAtlas.Cols) * CW, y0 = (cell / PianoLabelAtlas.Cols) * CW; return px[(AH - 1 - (y0 + y)) * AW + x0 + x]; }
            bool Ink(PianoLabelAtlas.Cell c, Color32 p) => c.Black ? p.a > 128 && p.r > 150 : p.a > 200 && p.r < 120;
            bool Coral(Color32 p) => p.a > 250 && p.r > 200 && p.g > 100 && p.g < 170 && p.b < 140;
            RectInt Box(int cell, Func<Color32, bool> pred, int xFrom = 0)
            {
                int minx = int.MaxValue, maxx = -1, miny = int.MaxValue, maxy = -1;
                for (int y = 0; y < CW; y++) for (int x = xFrom; x < CW; x++) if (pred(At(cell, x, y))) { minx = Mathf.Min(minx, x); maxx = Mathf.Max(maxx, x); miny = Mathf.Min(miny, y); maxy = Mathf.Max(maxy, y); }
                return maxx < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minx, miny, maxx - minx + 1, maxy - miny + 1);
            }
            int CountWhere(int cell, Func<Color32, bool> pred) { int n = 0; for (int y = 0; y < CW; y++) for (int x = 0; x < CW; x++) if (pred(At(cell, x, y))) n++; return n; }
            var cells = PianoLabelAtlas.Cells;
            foreach (var c in cells) Assert.That(CountWhere(c.Index, p => Ink(c, p)), Is.GreaterThanOrEqualTo(60), $"cell {c.Key} has ink");
            int blackClear = 0;
            foreach (var p in px) if (p.a == 0 && p.r + p.g + p.b < 300) blackClear++;
            Assert.That(blackClear, Is.EqualTo(0), "no clear texel is black (the mips would rim every sticker)");

            var c4 = cells[PianoLabelAtlas.CellOf(60)];
            var coral = Box(c4.Index, Coral);
            int coralN = CountWhere(c4.Index, Coral);
            Assert.That(coralN, Is.InRange(150, (int)(0.08f * CW * CW)), "a thin coral bar");
            Assert.That(coral.height, Is.LessThanOrEqualTo(14));
            Assert.That(coral.width, Is.GreaterThanOrEqualTo(30));
            Assert.That(Mathf.Abs(coral.x + coral.width / 2f - CW / 2f), Is.LessThanOrEqualTo(3f), "centred");
            foreach (int m in new[] { 62, 72, 48, 64, 67 }) Assert.That(CountWhere(PianoLabelAtlas.CellOf(m), Coral), Is.EqualTo(0), $"no coral on {m}");
            var name = Box(c4.Index, p => Ink(c4, p));
            Assert.That(coral.y, Is.GreaterThan(name.y + name.height + 2), "the bar sits below middle C's name (canvas y down)");
            Assert.That(coral.y + coral.height, Is.LessThan(CW - 4), "inside the sticker");

            // Printed sizes on the key (labels-check): a white letter about 10.5 mm tall and 6 mm wide, a sharp's name about
            // 6.5 mm tall, the octave digit at least 7 mm, the sharp sign raised.
            var q = PianoLabelAtlas.WhiteQuad; var qb = PianoLabelAtlas.BlackQuad;
            float wMm = q.W * 1000f, hMm = (q.Front - q.Rear) * 1000f, bwMm = qb.W * 1000f, bhMm = (qb.Front - qb.Rear) * 1000f;
            var dCell = cells[PianoLabelAtlas.CellOf(62)];
            var d = Box(dCell.Index, p => Ink(dCell, p));
            float dH = d.height * hMm / CW, dW = d.width * wMm / CW;
            Debug.Log($"[Piano] atlas ink: D {dW:F2} x {dH:F2} mm, C4 name box {name}, coral {coral} ({coralN} texels)");
            Assert.That(dH, Is.InRange(10f, 11.5f), "D is about 10.5 mm tall along the key");
            Assert.That(dW, Is.GreaterThanOrEqualTo(5.5f));
            int GapAfterFirstGlyph(PianoLabelAtlas.Cell c)
            {
                var col = new int[CW];
                for (int x = 0; x < CW; x++) for (int y = 0; y < CW; y++) if (Ink(c, At(c.Index, x, y))) col[x]++;
                int first = Array.FindIndex(col, n => n > 0), xx = first;
                while (xx < CW && col[xx] > 0) xx++;
                return xx;
            }
            var digit = Box(c4.Index, p => Ink(c4, p), GapAfterFirstGlyph(c4));
            Assert.That(digit.height * hMm / CW, Is.GreaterThanOrEqualTo(7f), "the octave digit is at least 7 mm tall");
            var cs = cells[PianoLabelAtlas.CellOf(61)];
            var csName = Box(cs.Index, p => Ink(cs, p));
            var sharp = Box(cs.Index, p => Ink(cs, p), GapAfterFirstGlyph(cs));
            float csH = csName.height * bhMm / CW;
            Assert.That(csH, Is.InRange(6f, 0.76f * 9f), "a sharp's name about 6.5 mm tall");
            Assert.That(csName.width * bwMm / CW, Is.LessThanOrEqualTo(bwMm * 0.95f));
            Assert.That(sharp.y + sharp.height, Is.LessThan(csName.y + csName.height - 2), "the sharp sign rides high");
            File.WriteAllBytes(Path.Combine(OutDir(), "label-atlas.png"), atlas.EncodeToPNG());
            yield return null;
        }

        // ---------------- the pictures ----------------

        Texture2D Render(Vector3 worldPos, Vector3 worldTarget, float fov)
        {
            var go = new GameObject("PianoShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x05, 0x07, 0x0f, 0xff);   // the VR room's background
            cam.allowHDR = false;
            // No stereoTargetEye: a built-in-renderer setting that URP warns about; with a target texture URP never uses XR.
            cam.aspect = Width / (float)Height;
            go.transform.SetPositionAndRotation(worldPos, Quaternion.LookRotation(worldTarget - worldPos, Vector3.up));
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var resolved = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            resolved.Create();
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            Graphics.Blit(rt, resolved);
            var prev = RenderTexture.active;
            RenderTexture.active = resolved;
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            shot.Apply(false);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release(); resolved.Release();
            Object.Destroy(rt); Object.Destroy(resolved);
            Object.Destroy(go);
            return shot;
        }

        /// <summary>Where a world point lands in a Render() picture (pixels, rows from the bottom as ReadPixels stores them).</summary>
        static Vector2 Project(Vector3 world, Vector3 worldPos, Vector3 worldTarget, float fov)
        {
            var go = new GameObject("PianoShotProjector");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 40f;
                cam.aspect = Width / (float)Height;
                go.transform.SetPositionAndRotation(worldPos, Quaternion.LookRotation(worldTarget - worldPos, Vector3.up));
                Vector3 v = cam.WorldToViewportPoint(world);
                return new Vector2(v.x * Width, v.y * Height);
            }
            finally { Object.Destroy(go); }
        }

        Vector3 P(float x, float y, float back) => _piano.transform.TransformPoint(new Vector3(x, y, back));

        void Save(Texture2D shot, string name)
        {
            string path = Path.Combine(OutDir(), name + ".png");
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Debug.Log($"[Piano] shot {path}");
            Object.Destroy(shot);
        }

        static float MeanLuma(Texture2D t, RectInt r)
        {
            double s = 0; int n = 0;
            for (int y = r.yMin; y < r.yMax; y += 2) for (int x = r.xMin; x < r.xMax; x += 2) { var c = t.GetPixel(x, y); s += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; n++; }
            return (float)(s / Math.Max(1, n));
        }

        [UnityTest]
        public IEnumerator SavesThePianoViews()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no GPU: run Tools/unity.sh test-play --gpu");
            Assert.That(_piano, Is.Not.Null);
            Assert.That(_piano.IsPresent, Is.True, "the piano is in the layout");
            var head = _app.Head;

            // 1. The seat view of the whole keyboard: the desktop rig camera itself (0, 1.2, -0.1) -> (0, 0.95, 0.45), fov 55.
            Vector3 eye = head.position, look = head.position + head.forward;
            var seat = Render(eye, look, DesktopRigFallback.DefaultFov);
            // The keyboard is in view and lit: a thin band across the plain ivory tops of C4..C5 (4 cm behind the fronts:
            // behind the labels, in front of the black keys) is much brighter than the background.
            _piano.TryGetKey(60, out var kc4); _piano.TryGetKey(72, out var kc5);
            Vector2 from = Project(P(kc4.X, 0f, 0.04f), eye, look, DesktopRigFallback.DefaultFov);
            Vector2 to = Project(P(kc5.X, 0f, 0.04f), eye, look, DesktopRigFallback.DefaultFov);
            var band = new RectInt(Mathf.RoundToInt(from.x), Mathf.RoundToInt((from.y + to.y) / 2f) - 2, Mathf.RoundToInt(to.x - from.x), 5);
            Assert.That(band.xMin, Is.GreaterThan(0)); Assert.That(band.xMax, Is.LessThan(Width));
            Assert.That(band.yMin, Is.GreaterThan(0)); Assert.That(band.yMax, Is.LessThan(Height));
            float keysLuma = MeanLuma(seat, band);
            float bgLuma = MeanLuma(seat, new RectInt(10, Height - 60, 200, 50));
            // The ebony tops of C#4 and D#4, 12 cm behind the white fronts (black keys run from back .055 to the hinge): the
            // reference the ivory must clear. The background is no reference in the integrated app: the real room's lit
            // plaster wall is as bright as the keys (the module clone's stub room was black).
            _piano.TryGetKey(61, out var cs4); _piano.TryGetKey(63, out var ds4);
            float blackLuma = 0f;
            foreach (var bk in new[] { cs4, ds4 })
            {
                Vector2 q = Project(P(bk.X, _piano.BTOP, 0.12f), eye, look, DesktopRigFallback.DefaultFov);
                blackLuma += 0.5f * MeanLuma(seat, new RectInt(Mathf.RoundToInt(q.x) - 1, Mathf.RoundToInt(q.y) - 1, 3, 3));
            }
            Debug.Log($"[Piano] seat view luma: C4-C5 key tops {keysLuma:F3} (band {band}), C#4/D#4 tops {blackLuma:F3}, top-left background {bgLuma:F3}");
            Save(seat, "piano-seat");

            // 2. A close-up of C4..C5 with their labels (letters small, octave digits, middle C's coral bar).
            _piano.TryGetKey(60, out var c4); _piano.TryGetKey(72, out var c5);
            float cx = (c4.X + c5.X) / 2f;
            Save(Render(P(cx, 0.16f, -0.11f), P(cx, 0f, 0.04f), 30f), "piano-closeup-c4-c5");
            Save(Render(P(c4.X, 0.075f, -0.05f), P(c4.X, 0f, 0.02f), 24f), "piano-closeup-middle-c");

            // 3. A key mid-press: E4 pushed to s = 0.65 by a fingertip (no note: short of the escapement), black F#4 driven
            //    fully down, both rendered at once before any frame lets them spring back (the key-mid-press view).
            var hand = new HandState(Handedness.Right);
            var p = new Pointer("shot-mid", hand, 10) { Active = true, WasActive = true, SampleDt = 0.011f };
            _piano.TryGetKey(64, out var e4);
            var pts = new List<Pointer> { p };
            p.Tip = p.Prev = P(e4.X, 0.01f, 0.03f);
            _piano.Tick(pts, 0.011f);
            for (int i = 1; i <= 8; i++) { p.Prev = p.Tip; p.Tip = P(e4.X, 0.01f - i * 0.0019f, 0.03f); _piano.Tick(pts, 0.011f); }
            var e4Key = _piano.Action.KeyByMidi(64);
            Assert.That(e4Key.Depth, Is.InRange(0.5f, 0.8f), "E4 mid-press");
            Assert.That(e4Key.HasVoice, Is.False, "short of the escapement: silent");
            var fs4 = _piano.Action.KeyByMidi(66);
            fs4.Depth = 1f; fs4.Dirty = true;
            _piano.View.Sync();
            Save(Render(P(0.02f, 0.14f, -0.15f), P(0.03f, 0f, 0.03f), 40f), "piano-key-mid-press");
            Save(Render(P(-0.09f, 0.035f, -0.09f), P(-0.005f, -0.004f, 0.03f), 34f), "piano-key-mid-press-low");
            fs4.Depth = 0f; fs4.Dirty = true;
            p.Active = false;
            // Let E4 spring back (about 0.1 s) before the next views: they are rendered at once, with no frame between.
            for (int i = 0; i < 30; i++) _piano.Tick(pts, 0.011f);
            Assert.That(e4Key.Depth, Is.EqualTo(0f), "every key back at rest for the remaining views");

            // 4. The stand from the side (the side-stand view) and square on to the right end.
            Save(Render(P(1.3f, 0.23f, -0.3f), P(0.15f, -0.3f, 0.05f), 55f), "piano-side-stand");
            Save(Render(P(1.75f, -0.28f, 0.15f), P(0f, -0.3f, 0.15f), 38f), "piano-side-profile");

            // 5. The other reference views for the A/B: seat (head (0, 1.15, 0)), seat-wide, rear three-quarter.
            Save(Render(P(0f, 0.43f, -0.45f), P(0f, 0.02f, 0.05f), 60f), "piano-ref-seat");
            Save(Render(P(0f, 0.43f, -0.45f), P(0f, -0.12f, 0.15f), 80f), "piano-ref-seat-wide");
            Save(Render(P(-1f, 0.63f, 0.9f), P(0f, -0.12f, 0.05f), 55f), "piano-ref-rear-three-quarter");

            // 6. Mixed reality on a table: the stand hidden.
            _piano.SetOnSurface(true);
            Save(Render(P(1.3f, 0.23f, -0.3f), P(0.15f, -0.3f, 0.05f), 55f), "piano-on-table-no-stand");
            _piano.ResetSurface();
            Assert.That(keysLuma, Is.GreaterThan(blackLuma + 0.2f), "the keys are lit and in view: ivory well above ebony");
            Assert.That(keysLuma, Is.GreaterThan(0.3f), "ivory, not grey");
            yield return null;
        }
    }
}
