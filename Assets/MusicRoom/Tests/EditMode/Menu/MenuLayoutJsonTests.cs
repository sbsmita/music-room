// Row hit rectangles, list geometry, palette and summon ring against 08-ux-visuals.json (a copy of
// Docs/08-ux-visuals.json sits next to this file, so the test reads the spec's own numbers).
using System;
using MusicRoom.Core;
using MusicRoom.Menu;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusicRoom.Tests.Menu
{
    public class MenuLayoutJsonTests
    {
        [Serializable] class UxFile { public UxPanel panel; public UxRing summonRing; }
        [Serializable] class UxPanel
        {
            public float[] faceSizeM; public float cylinderRadiusM; public int segments; public int[] canvasPx; public float pxPerMetre; public float cornerRadiusPx;
            public UxPalette palette; public UxBack backPlate; public UxScreens screens; public UxList list;
        }
        [Serializable] class UxPalette { public string INK, DIM, CYAN, GREEN, RED, AMBER, ROSE; }
        [Serializable] class UxBack { public float[] sizeM; public float localZ; public int[] texturePx; }
        [Serializable] class UxRow { public string id, to, inst, mode; public float[] rect; }
        [Serializable] class UxHome5 { public float rowH, gap, top; public string[] order; }
        [Serializable] class UxScreens { public float[] backRow; public UxRow[] home, lessons, instruments, lessonRunning, lessonDone, take; public UxHome5 homeWithRoomRowProposed; }
        [Serializable] class UxList { public float top, bottom, pitch, rowH, minVisiblePx; public int maxRows; }
        [Serializable] class UxRing { public float innerM, outerM; public int segments; public string color; public float offsetAlongPalmNormalM; }

        static UxFile Load()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/MusicRoom/Tests/EditMode/Menu/08-ux-visuals.json");
            Assert.That(ta, Is.Not.Null, "08-ux-visuals.json beside the test");
            var f = JsonUtility.FromJson<UxFile>(ta.text);
            Assert.That(f?.panel?.screens, Is.Not.Null);
            return f;
        }

        static void AssertRows(Rig r, int from, UxRow[] want, string screen)
        {
            Assert.That(r.Ctl.RowCount - from, Is.EqualTo(want.Length), screen + " row count");
            for (int i = 0; i < want.Length; i++)
            {
                var row = r.Ctl.Row(from + i);
                Assert.That(new[] { row.X0, row.Y0, row.X1, row.Y1 }, Is.EqualTo(want[i].rect), $"{screen} row {i} ({want[i].id})");
                Assert.That(row.Id.ToString().ToLowerInvariant(), Is.EqualTo(want[i].id), $"{screen} row {i} id");
            }
        }

        [Test]
        public void RowRectanglesMatchTheSpec()
        {
            var s = Load().panel.screens;
            var r = new Rig();
            // Every sub-screen's back row.
            // The Lessons and Instruments screens are the tile grid of platform.md 5.1 / 5.3 since the many-instrument
            // platform (12.3): their rectangles are MenuInstrumentGridTests'. The back row is still the spec's.
            r.Ctl.Go(MenuScreen.Lessons);
            r.Frame();
            var b = r.Ctl.Row(0);
            Assert.That(new[] { b.X0, b.Y0, b.X1, b.Y1 }, Is.EqualTo(s.backRow));
            Assert.That(s.lessons.Length, Is.EqualTo(3), "the original lessons rows (kept in the JSON for the record)");
            r.Ctl.Go(MenuScreen.Instruments);
            r.Frame();
            b = r.Ctl.Row(0);
            Assert.That(new[] { b.X0, b.Y0, b.X1, b.Y1 }, Is.EqualTo(s.backRow));

            r.Tutorial.Start("piano-scale");
            r.Frame();
            AssertRows(r, 1, s.lessonRunning, "lessonRunning");
            r.Tutorial.State = TutorialState.Done;
            r.Tutorial.Version++;
            r.Frame();
            AssertRows(r, 1, s.lessonDone, "lessonDone");

            r.Takes.Add("Take 1", 1, 1f, 1);
            r.Ctl.Go(MenuScreen.Recordings);
            r.Frame();
            r.Tap(1);
            Assert.That(r.Ctl.Screen, Is.EqualTo(MenuScreen.Take));
            AssertRows(r, 1, s.take, "take");
        }

        [Test]
        public void HomeRowsAreTheJsonOrderPlusExitAtTheEnd()
        {
            // The home page is the JSON's order (the spec's four rows, the proposed five with the Room row) plus, since the
            // first headset test, Exit as the last row (and Move instruments after Instruments when IArrangeMode is bound,
            // MenuArrangeExitStartTests). The rows share the page from y 132 (MenuController.HomeGeometry).
            var s = Load().panel.screens;
            var w = new Rig(withMr: false);
            Assert.That(w.Ctl.RowCount, Is.EqualTo(s.home.Length + 1), "the JSON's four rows and Exit");
            for (int i = 0; i < s.home.Length; i++)
            {
                Assert.That(w.Ctl.Row(i).Id.ToString().ToLowerInvariant(), Is.EqualTo(s.home[i].id), "home (JSON) row " + i);
                Assert.That(w.Ctl.Row(i).X0, Is.EqualTo(s.home[i].rect[0]));
                Assert.That(w.Ctl.Row(i).X1, Is.EqualTo(s.home[i].rect[2]));
            }
            for (int i = 0; i < 3; i++) Assert.That(w.Ctl.Row(i).To.ToString().ToLowerInvariant(), Is.EqualTo(s.home[i].to));
            Assert.That(w.Ctl.Row(s.home.Length).Id, Is.EqualTo(RowKind.Exit));
            for (int i = 0; i < w.Ctl.RowCount; i++) Assert.That(w.Ctl.Row(i).Y0, Is.EqualTo(132f + 112f * i), "five rows: h 100, gap 12");

            var p = s.homeWithRoomRowProposed;
            var r = new Rig();
            Assert.That(r.Ctl.RowCount, Is.EqualTo(p.order.Length + 1));
            for (int i = 0; i < p.order.Length; i++)
            {
                var row = r.Ctl.Row(i);
                Assert.That(row.Y0, Is.EqualTo(p.top + i * (84f + 10f)), "six rows: h 84, gap 10");
                Assert.That(row.Height, Is.EqualTo(84f));
                string id = row.Id == RowKind.Menu ? row.To.ToString().ToLowerInvariant() : row.Id.ToString().ToLowerInvariant();
                Assert.That(id, Is.EqualTo(p.order[i]));
            }
            Assert.That(r.Ctl.Row(p.order.Length).Id, Is.EqualTo(RowKind.Exit), "Exit is the last row");
        }

        [Test]
        public void ListGeometryMatchesTheSpec()
        {
            var l = Load().panel.list;
            Assert.That(MenuStyle.ListTop, Is.EqualTo(l.top));
            Assert.That(MenuStyle.ListBottom, Is.EqualTo(l.bottom));
            Assert.That(MenuStyle.ListPitch, Is.EqualTo(l.pitch));
            Assert.That(MenuStyle.ListH, Is.EqualTo(l.rowH));
            Assert.That(MenuStyle.ListMinVisible, Is.EqualTo(l.minVisiblePx));
            Assert.That(MenuController.MaxRows, Is.EqualTo(l.maxRows));
        }

        static Color Css(string css)
        {
            if (css.StartsWith("#")) { ColorUtility.TryParseHtmlString(css, out var c); return c; }
            var parts = css.Substring(css.IndexOf('(') + 1).TrimEnd(')').Split(',');
            return new Color(float.Parse(parts[0]) / 255f, float.Parse(parts[1]) / 255f, float.Parse(parts[2]) / 255f,
                parts.Length > 3 ? float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) : 1f);
        }

        static void Same(Color a, Color b, string what)
        {
            Assert.That(a.r, Is.EqualTo(b.r).Within(1e-3f), what); Assert.That(a.g, Is.EqualTo(b.g).Within(1e-3f), what);
            Assert.That(a.b, Is.EqualTo(b.b).Within(1e-3f), what); Assert.That(a.a, Is.EqualTo(b.a).Within(1e-3f), what);
        }

        [Test]
        public void PanelSizePaletteAndRingMatchTheSpec()
        {
            var f = Load();
            var p = f.panel;
            Assert.That(MenuMath.W, Is.EqualTo(p.faceSizeM[0]));
            Assert.That(MenuMath.H, Is.EqualTo(p.faceSizeM[1]));
            Assert.That(MenuMath.R, Is.EqualTo(p.cylinderRadiusM));
            Assert.That(MenuMath.Segments, Is.EqualTo(p.segments));
            Assert.That(MenuConstants.CanvasW, Is.EqualTo(p.canvasPx[0]));
            Assert.That(MenuConstants.CanvasH, Is.EqualTo(p.canvasPx[1]));
            Assert.That(MenuStyle.PxPerMetre, Is.EqualTo(p.pxPerMetre));
            Assert.That(MenuStyle.Corner, Is.EqualTo(p.cornerRadiusPx));
            Assert.That(MenuMath.BackW, Is.EqualTo(p.backPlate.sizeM[0]).Within(1e-4f));
            Assert.That(MenuMath.BackH, Is.EqualTo(p.backPlate.sizeM[1]).Within(1e-4f));
            Assert.That(MenuMath.BackZ, Is.EqualTo(p.backPlate.localZ));
            Assert.That(MenuStyle.BackW, Is.EqualTo(p.backPlate.texturePx[0]));
            Assert.That(MenuStyle.BackH, Is.EqualTo(p.backPlate.texturePx[1]));
            Same(MenuStyle.Ink, Css(p.palette.INK), "INK");
            Same(MenuStyle.Dim, Css(p.palette.DIM), "DIM");
            Same(MenuStyle.Cyan, Css(p.palette.CYAN), "CYAN");
            Same(MenuStyle.Green, Css(p.palette.GREEN), "GREEN");
            Same(MenuStyle.Red, Css(p.palette.RED), "RED");
            Same(MenuStyle.Amber, Css(p.palette.AMBER), "AMBER");
            Same(MenuStyle.Rose, Css(p.palette.ROSE), "ROSE");
            var ring = f.summonRing;
            Same(MenuView.RingColor, Css(ring.color), "ring");
            Assert.That(SummonGesture.RingOffset, Is.EqualTo(ring.offsetAlongPalmNormalM));
            Mesh m = MenuMath.BuildRing();
            try
            {
                Assert.That(m.vertexCount, Is.EqualTo((ring.segments + 1) * 2));
                float rmin = 1f, rmax = 0f;
                foreach (var v in m.vertices) { rmin = Mathf.Min(rmin, v.magnitude); rmax = Mathf.Max(rmax, v.magnitude); }
                Assert.That(rmin, Is.EqualTo(ring.innerM).Within(1e-6f));
                Assert.That(rmax, Is.EqualTo(ring.outerM).Within(1e-6f));
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }

        [Test]
        public void MaterialTemplatesAndShadersExist()
        {
            var provider = new MenuMaterials();
            Assert.That(provider.ResourcesFolder, Is.EqualTo("Assets/MusicRoom/Menu"));
            int n = 0;
            foreach (var spec in provider.Templates)
            {
                n++;
                Assert.That(spec.Name, Does.StartWith("Menu/"), "module-prefixed template names");
                Assert.That(Shader.Find(spec.ShaderName), Is.Not.Null, spec.ShaderName);
                var mat = new Material(Shader.Find(spec.ShaderName));
                try
                {
                    spec.Init(mat);
                    if (spec.Name == MenuView.FaceTemplate) Assert.That(mat.renderQueue, Is.EqualTo(2999));
                    if (spec.Name == MenuView.BackTemplate) Assert.That(mat.renderQueue, Is.EqualTo(2998));
                }
                finally { UnityEngine.Object.DestroyImmediate(mat); }
            }
            Assert.That(n, Is.EqualTo(3));
            Assert.That(Shader.Find(MenuMaterials.FaceShader).isSupported || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null, Is.True);
        }
    }
}
