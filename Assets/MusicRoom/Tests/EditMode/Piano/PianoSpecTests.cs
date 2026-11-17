// Piano data and pure functions against the research data (Docs/05-instruments.md, `piano`, `curves`):
// the 88-key table, the constants, StrikeVelocity / KeyShape / spring samples, label texts and atlas cells, the body
// boxes of piano.build, the key and label transforms (ARCHITECTURE.md 15.1 tests).
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MusicRoom.Core;
using MusicRoom.Piano;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Piano
{
    public class PianoSpecTests
    {
        // Instrument data piano.keys: (midi, black, x, label, atlasCell, inst).
        static readonly (int midi, bool black, float x, string label, string cell, int inst)[] JsonKeys =
        {
            (21, false, -0.59925f, "A", "w:A", 0), (22, true, -0.5835f, "A#", "b:A#", 0), (23, false, -0.57575f, "B", "w:B", 1), (24, false, -0.55225f, "C1", "w:C1", 2),
            (25, true, -0.5433f, "C#", "b:C#", 1), (26, false, -0.52875f, "D", "w:D", 3), (27, true, -0.5142f, "D#", "b:D#", 2), (28, false, -0.50525f, "E", "w:E", 4),
            (29, false, -0.48175f, "F", "w:F", 5), (30, true, -0.474f, "F#", "b:F#", 3), (31, false, -0.45825f, "G", "w:G", 6), (32, true, -0.4465f, "G#", "b:G#", 4),
            (33, false, -0.43475f, "A", "w:A", 7), (34, true, -0.419f, "A#", "b:A#", 5), (35, false, -0.41125f, "B", "w:B", 8), (36, false, -0.38775f, "C2", "w:C2", 9),
            (37, true, -0.3788f, "C#", "b:C#", 6), (38, false, -0.36425f, "D", "w:D", 10), (39, true, -0.3497f, "D#", "b:D#", 7), (40, false, -0.34075f, "E", "w:E", 11),
            (41, false, -0.31725f, "F", "w:F", 12), (42, true, -0.3095f, "F#", "b:F#", 8), (43, false, -0.29375f, "G", "w:G", 13), (44, true, -0.282f, "G#", "b:G#", 9),
            (45, false, -0.27025f, "A", "w:A", 14), (46, true, -0.2545f, "A#", "b:A#", 10), (47, false, -0.24675f, "B", "w:B", 15), (48, false, -0.22325f, "C3", "w:C3", 16),
            (49, true, -0.2143f, "C#", "b:C#", 11), (50, false, -0.19975f, "D", "w:D", 17), (51, true, -0.1852f, "D#", "b:D#", 12), (52, false, -0.17625f, "E", "w:E", 18),
            (53, false, -0.15275f, "F", "w:F", 19), (54, true, -0.145f, "F#", "b:F#", 13), (55, false, -0.12925f, "G", "w:G", 20), (56, true, -0.1175f, "G#", "b:G#", 14),
            (57, false, -0.10575f, "A", "w:A", 21), (58, true, -0.09f, "A#", "b:A#", 15), (59, false, -0.08225f, "B", "w:B", 22), (60, false, -0.05875f, "C4", "c:C4", 23),
            (61, true, -0.0498f, "C#", "b:C#", 16), (62, false, -0.03525f, "D", "w:D", 24), (63, true, -0.0207f, "D#", "b:D#", 17), (64, false, -0.01175f, "E", "w:E", 25),
            (65, false, 0.01175f, "F", "w:F", 26), (66, true, 0.0195f, "F#", "b:F#", 18), (67, false, 0.03525f, "G", "w:G", 27), (68, true, 0.047f, "G#", "b:G#", 19),
            (69, false, 0.05875f, "A", "w:A", 28), (70, true, 0.0745f, "A#", "b:A#", 20), (71, false, 0.08225f, "B", "w:B", 29), (72, false, 0.10575f, "C5", "w:C5", 30),
            (73, true, 0.1147f, "C#", "b:C#", 21), (74, false, 0.12925f, "D", "w:D", 31), (75, true, 0.1438f, "D#", "b:D#", 22), (76, false, 0.15275f, "E", "w:E", 32),
            (77, false, 0.17625f, "F", "w:F", 33), (78, true, 0.184f, "F#", "b:F#", 23), (79, false, 0.19975f, "G", "w:G", 34), (80, true, 0.2115f, "G#", "b:G#", 24),
            (81, false, 0.22325f, "A", "w:A", 35), (82, true, 0.239f, "A#", "b:A#", 25), (83, false, 0.24675f, "B", "w:B", 36), (84, false, 0.27025f, "C6", "w:C6", 37),
            (85, true, 0.2792f, "C#", "b:C#", 26), (86, false, 0.29375f, "D", "w:D", 38), (87, true, 0.3083f, "D#", "b:D#", 27), (88, false, 0.31725f, "E", "w:E", 39),
            (89, false, 0.34075f, "F", "w:F", 40), (90, true, 0.3485f, "F#", "b:F#", 28), (91, false, 0.36425f, "G", "w:G", 41), (92, true, 0.376f, "G#", "b:G#", 29),
            (93, false, 0.38775f, "A", "w:A", 42), (94, true, 0.4035f, "A#", "b:A#", 30), (95, false, 0.41125f, "B", "w:B", 43), (96, false, 0.43475f, "C7", "w:C7", 44),
            (97, true, 0.4437f, "C#", "b:C#", 31), (98, false, 0.45825f, "D", "w:D", 45), (99, true, 0.4728f, "D#", "b:D#", 32), (100, false, 0.48175f, "E", "w:E", 46),
            (101, false, 0.50525f, "F", "w:F", 47), (102, true, 0.513f, "F#", "b:F#", 33), (103, false, 0.52875f, "G", "w:G", 48), (104, true, 0.5405f, "G#", "b:G#", 34),
            (105, false, 0.55225f, "A", "w:A", 49), (106, true, 0.568f, "A#", "b:A#", 35), (107, false, 0.57575f, "B", "w:B", 50), (108, false, 0.59925f, "C8", "w:C8", 51),
        };

        static PianoAction NewAction() => new PianoAction(new Clock(), null);

        [Test]
        public void KeyTableMatchesTheJson()
        {
            var a = NewAction();
            Assert.That(a.Keys.Length, Is.EqualTo(88));
            Assert.That(JsonKeys.Length, Is.EqualTo(88));
            int whites = 0, blacks = 0;
            for (int i = 0; i < 88; i++)
            {
                var j = JsonKeys[i];
                var k = a.Keys[i];
                Assert.That(k.Midi, Is.EqualTo(j.midi));
                Assert.That(k.Black, Is.EqualTo(j.black), $"black {j.midi}");
                Assert.That(k.X, Is.EqualTo(j.x).Within(2e-6f), $"x {j.midi}");
                Assert.That(k.Inst, Is.EqualTo(j.inst), $"inst {j.midi}");
                Assert.That(PianoSpec.KeyX(j.midi), Is.EqualTo(j.x).Within(2e-6f));
                Assert.That(PianoSpec.LabelText(j.midi), Is.EqualTo(j.label), $"label {j.midi}");
                Assert.That(PianoSpec.LabelCellKey(j.midi), Is.EqualTo(j.cell), $"cell {j.midi}");
                if (k.Black) blacks++; else whites++;
            }
            Assert.That(whites, Is.EqualTo(52));
            Assert.That(blacks, Is.EqualTo(36));
            Assert.That(a.KeyByMidi(60).X, Is.EqualTo(-0.05875f).Within(1e-6f), "middle C");
            Assert.That(a.KeyByMidi(20), Is.Null);
            Assert.That(a.KeyByMidi(109), Is.Null);
        }

        [Test]
        public void ConstantsMatchTheJson()
        {
            Assert.That(PianoSpec.W, Is.EqualTo(0.0235f));
            Assert.That(PianoSpec.L, Is.EqualTo(0.15f));
            Assert.That(PianoSpec.H, Is.EqualTo(0.02f));
            Assert.That(PianoSpec.BW, Is.EqualTo(0.0137f));
            Assert.That(PianoSpec.BL, Is.EqualTo(0.095f));
            Assert.That(PianoSpec.BTOP, Is.EqualTo(0.012f));
            Assert.That(PianoSpec.WW, Is.EqualTo(0.0221f).Within(1e-7f));
            Assert.That(PianoSpec.X0, Is.EqualTo(-0.611f).Within(1e-6f));
            Assert.That(PianoSpec.MaxAngleWhite, Is.EqualTo(0.06666667f).Within(1e-7f));
            Assert.That(PianoSpec.MaxAngleBlack, Is.EqualTo(0.084f));
            Assert.That(PianoSpec.TableLift, Is.EqualTo(0.092f));
            // Key travel: 10 mm at a white key's front edge, ~8 mm at a black key's own front (piano-check travelRight).
            float white = PianoSpec.L * Mathf.Sin(PianoSpec.MaxAngleWhite) * 1000f, black = PianoSpec.BL * Mathf.Sin(PianoSpec.MaxAngleBlack) * 1000f;
            Assert.That(white, Is.EqualTo(10f).Within(0.05f));
            Assert.That(black, Is.LessThan(white).And.GreaterThan(7f));
            // The escapement is 1 mm above the felt at a white key's front.
            Assert.That((1f - PianoSpec.S_ESC) * PianoSpec.TRAVEL, Is.EqualTo(0.001f).Within(1e-7f));
        }

        [Test]
        public void StrikeVelocityMatchesTheCurve()
        {
            // curves.pianoStrikeVelocity (JSON, 4 decimals) and the spec samples .07->.10, .2->.38, .5->.62, 1->.84.
            float[,] samples = { { 0.07f, 0.1f }, { 0.1f, 0.2093f }, { 0.15f, 0.3082f }, { 0.2f, 0.3769f }, { 0.3f, 0.4778f }, { 0.5f, 0.6179f }, { 0.75f, 0.744f }, { 1f, 0.8433f }, { 1.5f, 1f }, { 2f, 1f } };
            for (int i = 0; i < samples.GetLength(0); i++)
                Assert.That(PianoSpec.StrikeVelocity(samples[i, 0]), Is.EqualTo(samples[i, 1]).Within(1e-4f), $"v = {samples[i, 0]}");
            Assert.That(PianoSpec.StrikeVelocity(0.2f), Is.EqualTo(0.38f).Within(0.005f));
            Assert.That(PianoSpec.StrikeVelocity(0.5f), Is.EqualTo(0.62f).Within(0.005f));
            Assert.That(PianoSpec.StrikeVelocity(1f), Is.EqualTo(0.84f).Within(0.005f));
            Assert.That(PianoSpec.StrikeVelocity(0f), Is.EqualTo(0.1f), "never below .1");
            // It inverts the synth's level .08 + .8 vel^2: the level rises evenly with log speed (about 20 dB over the range).
            float lv(float v) { float vel = PianoSpec.StrikeVelocity(v); return 20f * Mathf.Log10(0.08f + 0.8f * vel * vel); }
            Assert.That(lv(1.5f) - lv(0.07f), Is.EqualTo(20f).Within(0.6f));
            Assert.That(lv(0.4f) - lv(0.2f), Is.EqualTo(lv(0.8f) - lv(0.4f)).Within(0.05f));
        }

        [Test]
        public void KeyShapeMatchesTheCurve()
        {
            float[,] samples = { { 0f, 0f }, { 0.5f, 0.5f }, { 0.84f, 0.84f }, { 0.87f, 0.84f }, { 0.9f, 0.84f }, { 0.91f, 0.8711f }, { 0.925f, 0.925f }, { 0.94f, 0.94f }, { 1f, 0.9779f }, { 1.2f, 0.9992f }, { 2f, 1f } };
            for (int i = 0; i < samples.GetLength(0); i++)
                Assert.That(PianoSpec.KeyShape(samples[i, 0]), Is.EqualTo(samples[i, 1]).Within(1e-4f), $"s = {samples[i, 0]}");
            Assert.That(PianoSpec.KeyShape(-0.3f), Is.EqualTo(0f));
            // The hitch: the key pauses (dips by at most 0.06 * 0.096 = 5.8 mm/m) while the finger goes .84 -> .90, then catches
            // up; it never runs ahead of the finger and never passes the felt.
            float max = 0f;
            for (float s = 0f; s <= 3f; s += 0.001f)
            {
                float d = PianoSpec.KeyShape(s);
                Assert.That(d, Is.GreaterThanOrEqualTo(max - 0.0116f), $"s = {s}");
                Assert.That(d, Is.LessThanOrEqualTo(Mathf.Min(1f, s) + 1e-6f));
                max = Mathf.Max(max, d);
            }
        }

        [Test]
        public void SpringReleasesInTheMeasuredTimes()
        {
            // curves.spring90Hz: from the felt at 90 Hz the damper point (depth <= .3) is reached after 44.4 ms, rest after 100 ms.
            float depth = 1f, v = 0f, t = 0f, tDamper = -1f;
            const float dt = 1f / 90f;
            int frames = 0;
            while (depth > 0f && frames < 100)
            {
                PianoSpec.Spring(ref depth, ref v, dt);
                t += dt;
                frames++;
                if (tDamper < 0f && depth <= PianoSpec.S_DAMPER) tDamper = t;
                Assert.That(depth, Is.GreaterThanOrEqualTo(0f), "lands on the rail, no undershoot");
            }
            Assert.That(tDamper, Is.EqualTo(0.0444f).Within(0.0005f));
            Assert.That(t, Is.EqualTo(0.1f).Within(0.0005f));
            Assert.That(v, Is.EqualTo(0f));
            // The same rise at any frame rate (exact solution): at 360 Hz rest comes within one 90 Hz frame of 100 ms.
            depth = 1f; v = 0f; t = 0f;
            while (depth > 0f) { PianoSpec.Spring(ref depth, ref v, 1f / 360f); t += 1f / 360f; }
            Assert.That(t, Is.InRange(0.08f, 0.11f));
        }

        [Test]
        public void LabelTextsAndAtlasCells()
        {
            string t(int m) => PianoSpec.LabelText(m);
            Assert.That(string.Join(" ", t(21), t(23), t(24), t(60), t(61), t(63), t(66), t(68), t(70), t(107), t(108)), Is.EqualTo("A B C1 C4 C# D# F# G# A# B C8"));
            var cells = PianoLabelAtlas.Cells;
            Assert.That(cells.Count, Is.EqualTo(19));
            var want = new[] { "w:A", "b:A#", "w:B", "w:C1", "b:C#", "w:D", "b:D#", "w:E", "w:F", "b:F#", "w:G", "b:G#", "w:C2", "w:C3", "c:C4", "w:C5", "w:C6", "w:C7", "w:C8" };
            for (int i = 0; i < want.Length; i++) Assert.That(cells[i].Key, Is.EqualTo(want[i]));
            var pattern = new Regex(@"^[wbc]:[A-G]#?\d?$");
            foreach (var c in cells) Assert.That(pattern.IsMatch(c.Key), Is.True, c.Key);   // letters only: no brand text, no styles
            Assert.That(cells[PianoLabelAtlas.CellOf(60)].Marked, Is.True, "middle C has its own marked cell");
            Assert.That(cells[PianoLabelAtlas.CellOf(72)].Marked, Is.False);
            Assert.That(cells[PianoLabelAtlas.CellOf(61)].Mark, Is.EqualTo("#"));
            Assert.That(cells[PianoLabelAtlas.CellOf(60)].Digit, Is.EqualTo("4"));
            Assert.That(PianoLabelAtlas.Rows, Is.EqualTo(3));
            Assert.That(PianoLabelAtlas.Width, Is.EqualTo(1024));
            Assert.That(PianoLabelAtlas.Height, Is.EqualTo(384));
            // The atlas uv mapping: u0 = (cell % cols) / cols, v1 = 1 - floor(cell / cols) / rows.
            var uv = PianoLabelAtlas.CellUv(14);
            Assert.That(uv.x, Is.EqualTo(6f / 8f).Within(1e-6f));
            Assert.That(uv.w, Is.EqualTo(1f - 1f / 3f).Within(1e-6f));
            Assert.That(uv.y, Is.EqualTo(1f - 2f / 3f).Within(1e-6f));
            // labelQuad_fromHinge: white 17 mm wide, 146..122 mm from the hinge; black 10 mm, 91..73 mm.
            Assert.That(PianoLabelAtlas.WhiteQuad.W, Is.EqualTo(0.017f));
            Assert.That(PianoLabelAtlas.WhiteQuad.Front, Is.EqualTo(0.146f).Within(1e-6f));
            Assert.That(PianoLabelAtlas.WhiteQuad.Rear, Is.EqualTo(0.122f).Within(1e-6f));
            Assert.That(PianoLabelAtlas.BlackQuad.Front, Is.EqualTo(0.091f).Within(1e-6f));
            Assert.That(PianoLabelAtlas.BlackQuad.Rear, Is.EqualTo(0.073f).Within(1e-6f));
        }

        [Test]
        public void LabelLayoutFollowsDrawLabel()
        {
            // With the Node fallback metrics (canvas measureText unavailable: cap = .72 em) the JSON records cap 10.8 / 6.4 mm.
            var runs = new List<PianoLabelAtlas.Run>();
            var lay = new PianoLabelAtlas.Layout[PianoLabelAtlas.Cells.Count];
            PianoLabelAtlas.LayoutCells(s => 0.7f * s.Length, _ => 0.72f, runs, lay);
            var d = lay[PianoLabelAtlas.CellOf(62)];
            Assert.That(d.W, Is.EqualTo(17f).Within(1e-3f));
            Assert.That(d.H, Is.EqualTo(24f).Within(1e-3f));
            Assert.That(d.Em, Is.EqualTo(10f));
            Assert.That(d.Cap, Is.EqualTo(10.8f).Within(1e-3f));
            var cs = lay[PianoLabelAtlas.CellOf(61)];
            Assert.That(cs.Cap, Is.EqualTo(6.4f).Within(0.03f), "JSON capMm black 6.4 (5.9 x 1.5 x .72 = 6.372)");
            // A name too wide for its key shrinks to 88 % (white) / 92 % (black) of the quad width.
            PianoLabelAtlas.LayoutCells(s => 2f * s.Length, _ => 0.72f, runs, lay);
            var c4 = lay[PianoLabelAtlas.CellOf(60)];
            Assert.That(c4.Em * c4.Total, Is.EqualTo(17f * 0.88f).Within(1e-3f));
            // The sharp sign rides 0.35 cap above the baseline; the octave digit shares it.
            PianoLabelAtlas.Run name = default, mark = default;
            foreach (var r in runs) { if (r.Cell == PianoLabelAtlas.CellOf(61) && r.Text == "C") name = r; if (r.Cell == PianoLabelAtlas.CellOf(61) && r.Text == "#") mark = r; }
            Assert.That(name.Baseline - mark.Baseline, Is.EqualTo(0.35f * lay[PianoLabelAtlas.CellOf(61)].Cap).Within(1e-4f));
        }

        [Test]
        public void ThePianoBakesNothingOfItsOwn()
        {
            // Grille and stand wood come from Core's shared set (15.1), so the piano registers no bake spec and no texture
            // under a non-"Piano/" name; its only editor asset is the label template, prefixed with the module name.
            Assert.That(typeof(ITextureBakeProvider).IsAssignableFrom(typeof(PianoAssets)), Is.False);
            foreach (var t in new PianoAssets().Templates) Assert.That(t.Name, Does.StartWith("Piano/"));
            foreach (var spec in SharedTextures.Specs())
                Assert.That(spec.Name, Does.Not.StartWith("Piano"), "no piano texture hides in the shared set");
            var names = new List<string>();
            foreach (var spec in SharedTextures.Specs()) names.Add(spec.Name);
            Assert.That(names, Has.Member(SharedTextures.Grille).And.Member(SharedTextures.WoodStand), "the two textures the piano uses are baked by Core");
        }

        [Test]
        public void ShadePaletteReproducesTheVertexGrey()
        {
            // shadeKeySides: sides fall to 45 % at the bottom, the underside to 40 %, the top stays white.
            Assert.That(PianoTextures.SideShade(0f, -0.02f, 0f, Vector3.up), Is.EqualTo(1f));
            Assert.That(PianoTextures.SideShade(-0.02f, -0.02f, 0f, Vector3.right), Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That(PianoTextures.SideShade(-0.02f, -0.02f, 0f, Vector3.down), Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(PianoTextures.SideShade(-0.01f, -0.02f, 0f, Vector3.back), Is.EqualTo(0.91f).Within(1e-6f), "front face (Unity -z)");
            // The palette is c * t, read back by bilinear filtering of a 2 x 2 texture: check the texel values and the uv map.
            var tex = PianoTextures.ShadePalette();
            try
            {
                var px = tex.GetPixels32();
                Assert.That(px[0].r, Is.EqualTo(Mathf.RoundToInt(0.3f * 0.7f * 255f)));
                Assert.That(px[3].r, Is.EqualTo(255));
                Vector2 uv = PianoTextures.ShadeUv(1f, 1f);
                Assert.That(uv, Is.EqualTo(new Vector2(0.75f, 0.75f)));
                // The GPU's bilinear filter between the texel centres (u, v at .25 / .75), evaluated here by hand.
                float Sample(Vector2 q)
                {
                    float fu = Mathf.Clamp01((q.x - 0.25f) / 0.5f), fv = Mathf.Clamp01((q.y - 0.25f) / 0.5f);
                    float bottom = Mathf.Lerp(px[0].r, px[1].r, fu), top = Mathf.Lerp(px[2].r, px[3].r, fu);
                    return Mathf.Lerp(bottom, top, fv) / 255f;
                }
                foreach (float c in new[] { 0.3f, 0.45f, 0.65f, 0.9f, 1f })
                    foreach (float t in new[] { 0.7f, 0.84f, 0.92f, 1f })
                        Assert.That(Sample(PianoTextures.ShadeUv(c, t)), Is.EqualTo(c * t).Within(0.006f), $"c {c} t {t}: bilinear filtering gives c * t");
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [Test]
        public void BodyBoxesMatchPianoBuild()
        {
            // Instrument data piano.bodyBoxes (unity), metres.
            AssertBox(PianoBody.Case().ComputeBounds(), new Vector3(-0.663f, -0.092f, -0.004f), new Vector3(0.663f, 0.038f, 0.29f), 0.0005f, "case");
            AssertBox(PianoBody.Panel().ComputeBounds(), new Vector3(-0.6005f, 0.038f, 0.161f), new Vector3(0.6005f, 0.0412f, 0.236f), 0.0003f, "panel");
            AssertBox(PianoBody.Grille().ComputeBounds(), new Vector3(-0.6005f, 0.0384f, 0.2465f), new Vector3(0.6005f, 0.0384f, 0.2655f), 0.0002f, "grille");
            AssertBox(PianoBody.MusicRest().ComputeBounds(), new Vector3(-0.26f, 0.0388f, 0.223f), new Vector3(0.26f, 0.1681f, 0.2817f), 0.0006f, "music rest");
            AssertBox(PianoBody.Stand().ComputeBounds(), new Vector3(-0.6025f, -0.72f, 0.005f), new Vector3(0.6025f, -0.092f, 0.315f), 0.0005f, "stand");
            // piano-check bodySlim: about 13 cm tall, 30 cm deep, 1.326 m wide.
            var s = PianoBody.Case().ComputeBounds().size;
            Assert.That(s.y, Is.EqualTo(0.13f).Within(0.01f));
            Assert.That(s.z, Is.EqualTo(0.3f).Within(0.02f));
            Assert.That(s.x, Is.EqualTo(1.326f).Within(0.01f));
        }

        static void AssertBox(Bounds b, Vector3 min, Vector3 max, float tol, string what)
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.That(b.min[i], Is.EqualTo(min[i]).Within(tol), $"{what} min[{i}]");
                Assert.That(b.max[i], Is.EqualTo(max[i]).Within(tol), $"{what} max[{i}]");
            }
        }

        [Test]
        public void CaseFacesPointOutward()
        {
            // The extrusion is built natively in Unity's winding: every triangle's geometric normal matches its stored normal
            // and points away from the solid (top faces up, the key-well floor up, the rear face back).
            var b = PianoBody.Case();
            int up = 0, back = 0;
            for (int t = 0; t < b.Indices.Count; t += 3)
            {
                Vector3 p0 = b.Positions[b.Indices[t]], p1 = b.Positions[b.Indices[t + 1]], p2 = b.Positions[b.Indices[t + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                float len = n.magnitude;   // not .normalized: it returns zero below 1e-5 and these triangles are millimetres
                if (len < 1e-12f) continue;
                n /= len;
                Assert.That(Vector3.Dot(n, b.Normals[b.Indices[t]]), Is.GreaterThan(0.99f));
                Vector3 c = (p0 + p1 + p2) / 3f;
                if (c.y > 0.0375f && Mathf.Abs(c.x) < 0.5f && c.z > 0.17f && c.z < 0.26f) { Assert.That(n.y, Is.GreaterThan(0.9f), "case top faces up"); up++; }
                if (c.z > 0.2895f && Mathf.Abs(c.x) < 0.5f && c.y < 0f && c.y > -0.08f) { Assert.That(n.z, Is.GreaterThan(0.9f), "rear face faces back"); back++; }
            }
            Assert.That(up, Is.GreaterThan(0));
            Assert.That(back, Is.GreaterThan(0));
            // Rounded boxes too: a unit-ish box's faces all point away from its centre.
            var box = new PianoMeshBuilder();
            box.AddRoundedBox(new Vector3(0.1f, 0.05f, 0.2f), 2, 0.01f, Vector3.zero, PianoMeshBuilder.BoxUv.Planar);
            Assert.That(box.VertexCount, Is.EqualTo(PianoMeshBuilder.RoundedBoxVertexCount(2)));
            for (int t = 0; t < box.Indices.Count; t += 3)
            {
                Vector3 p0 = box.Positions[box.Indices[t]], p1 = box.Positions[box.Indices[t + 1]], p2 = box.Positions[box.Indices[t + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                Assert.That(Vector3.Dot(n, (p0 + p1 + p2) / 3f), Is.GreaterThan(0f));
            }
        }

        [Test]
        public void KeysAndLabelsSitOnTheirHinges()
        {
            var a = NewAction();
            var view = new PianoKeysView(a);
            try
            {
                var e4 = a.KeyByMidi(64);
                var cs4 = a.KeyByMidi(61);
                // At rest: white x within WW/2, y -0.02..0, back 0..L; black y -0.004..0.012, back L-BL..L.
                for (int v = 0; v < view.VerticesPerKey(false); v++)
                {
                    Vector3 p = view.KeyVertexPosition(e4, v);
                    Assert.That(Mathf.Abs(p.x - e4.X), Is.LessThanOrEqualTo(PianoSpec.WW / 2f + 1e-5f));
                    Assert.That(p.y, Is.InRange(-0.02f - 1e-5f, 1e-5f));
                    Assert.That(p.z, Is.InRange(-1e-5f, PianoSpec.L + 1e-5f));
                }
                for (int v = 0; v < view.VerticesPerKey(true); v++)
                {
                    Vector3 p = view.KeyVertexPosition(cs4, v);
                    Assert.That(p.y, Is.InRange(-0.004f - 1e-5f, PianoSpec.BTOP + 1e-5f));
                    Assert.That(p.z, Is.InRange(PianoSpec.L - PianoSpec.BL - 1e-5f, PianoSpec.L + 1e-5f));
                }
                // Labels at rest: just above the key tops (0.3 mm), on the flat part, in the front part (labels-check).
                foreach (var k in a.Keys)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 c = view.LabelCorner(k, i);
                        Assert.That(c.y, Is.EqualTo(k.Top + 0.0003f).Within(1e-6f));
                        Assert.That(Mathf.Abs(c.x - k.X), Is.LessThanOrEqualTo(k.Black ? 0.0051f : 0.0086f));
                        if (k.Black) Assert.That(c.z, Is.InRange(0.0585f, 0.0775f));
                        else Assert.That(c.z, Is.InRange(0.0035f, 0.0285f));
                    }
                    Vector4 uv = PianoLabelAtlas.CellUv(PianoLabelAtlas.CellOf(k.Midi));
                    Assert.That(view.LabelUv(k, 0), Is.EqualTo(new Vector2(uv.x, uv.y)), "front-left = (u0, v0)");
                    Assert.That(view.LabelUv(k, 2), Is.EqualTo(new Vector2(uv.z, uv.w)), "rear-right = (u1, v1): the text's top faces the hinge");
                }
                // Fully down: the white key's front edge travels 10 mm, the label rides along and darkens with the key.
                e4.Depth = 1f;
                view.WriteKey(e4);
                float minY = 0f;
                for (int v = 0; v < view.VerticesPerKey(false); v++) minY = Mathf.Min(minY, view.KeyVertexPosition(e4, v).y);
                Assert.That(minY, Is.EqualTo(-0.02f - 0.01f).Within(0.0015f), "front bottom corner down by the travel");
                Vector3 front = view.LabelCorner(e4, 0);
                Assert.That(front.y, Is.LessThan(-0.009f), "label front follows the key down");
                Assert.That(view.LabelColour(e4).r, Is.EqualTo(Mathf.RoundToInt((1f - 0.16f) * 255f)).Within(1));
                // The key keeps its hinge: vertices at the hinge (back L, top) do not move.
                Vector3 hingeTop = new Vector3(e4.X, 0f, PianoSpec.L);
                float nearest = float.MaxValue;
                for (int v = 0; v < view.VerticesPerKey(false); v++) nearest = Mathf.Min(nearest, (view.KeyVertexPosition(e4, v) - hingeTop).magnitude);
                Assert.That(nearest, Is.LessThan(0.012f));
            }
            finally { view.Dispose(); }
        }

        [Test]
        public void MovingKeysUploadOnlyTheirOwnVertexRuns()
        {
            // Two hands far apart (A0 and C8 springing back) must not re-send the 50 white keys between them every frame:
            // one call per run of dirty keys, neighbours (and a single clean key between two) merged into one.
            var a = NewAction();
            var view = new PianoKeysView(a);
            try
            {
                int vw = view.VerticesPerKey(false), vb = view.VerticesPerKey(true);
                Assert.That(vw, Is.EqualTo(216), "RoundedBox(2 segments): 6 x 6 x 6 vertices per white key");
                int u0 = view.Uploads;
                view.Sync();
                Assert.That(view.Uploads, Is.EqualTo(u0), "nothing moved: nothing sent");
                Assert.That(view.UploadedVertices, Is.EqualTo(0));

                void Move(params int[] midis) { foreach (int m in midis) { var k = a.KeyByMidi(m); k.Depth = 0.5f; k.Dirty = true; } }
                Move(21, 108);   // A0 and C8: the two ends of the white mesh
                u0 = view.Uploads;
                view.Sync();
                Assert.That(view.Uploads - u0, Is.EqualTo(3), "A0, C8 (two white runs) and the label span");
                Assert.That(view.UploadedVertices, Is.EqualTo(2 * vw + (108 - 21 + 1) * 4), "two keys' vertices, not 52");

                Move(60, 62, 64);   // C4 D4 E4: adjacent whites, one run
                u0 = view.Uploads;
                view.Sync();
                Assert.That(view.Uploads - u0, Is.EqualTo(2), "one white run + labels");
                Assert.That(view.UploadedVertices, Is.EqualTo(3 * vw + (64 - 60 + 1) * 4));

                Move(60, 64, 61, 70);   // C4 _ E4 (one clean key between: merged), C#4 and A#4 (blacks far apart)
                u0 = view.Uploads;
                view.Sync();
                Assert.That(view.Uploads - u0, Is.EqualTo(4), "one white run, two black runs, labels");
                Assert.That(view.UploadedVertices, Is.EqualTo(3 * vw + 2 * vb + (70 - 60 + 1) * 4));

                // What reached the mesh is what was written (the runs cover every dirty key).
                var verts = new System.Collections.Generic.List<Vector3>();
                view.WhiteMesh.GetVertices(verts);
                foreach (int m in new[] { 21, 60, 64, 108 })
                {
                    var k = a.KeyByMidi(m);
                    for (int v = 0; v < vw; v += 37)
                        Assert.That((verts[k.Inst * vw + v] - view.KeyVertexPosition(k, v)).sqrMagnitude, Is.LessThan(1e-12f), $"key {m} vertex {v}");
                }
                view.BlackMesh.GetVertices(verts);
                var as4 = a.KeyByMidi(70);
                Assert.That((verts[as4.Inst * vb] - view.KeyVertexPosition(as4, 0)).sqrMagnitude, Is.LessThan(1e-12f));
                Assert.That(view.KeyVertexPosition(as4, 0), Is.Not.EqualTo(Vector3.zero));
            }
            finally { view.Dispose(); }
        }

        [Test]
        public void NoBrandNamesInThePianoSources()
        {
            // piano-check section 9: no brand names, logos or wordmarks in the piano's code or anything it draws.
            string dir = Path.Combine(Application.dataPath, "MusicRoom/Piano");
            var brand = new Regex(@"yamaha|p-?145|clavinova|casio|roland|kawai|korg|steinway|nord\b", RegexOptions.IgnoreCase);
            int files = 0;
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                Assert.That(brand.IsMatch(File.ReadAllText(f)), Is.False, f);
            }
            Assert.That(files, Is.GreaterThan(5));
        }
    }
}
