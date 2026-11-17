// The wall paint's maths (Room/Decor/WallCanvas.cs, PaintPalette.cs, Textures/PaintArt.cs), without Unity objects:
//   - a room of four walls (one with a window opening made of four pieces, as the VR room's right wall) and things at its
//     walls (a shelf, a picture frame, a fireplace's breast, a door in mixed reality): the clear cells avoid all of them,
//     the opening, the floor band, the ceiling band and the corners;
//   - every splash of a long song lies wholly on clear wall, never over what stands there, inside the band;
//   - the same take paints the same picture (deterministic), another take another one;
//   - pitch sets the height, the song moves along the walls as it plays, and it starts in front of the player;
//   - the walls are in order round the room (any room, any turn of it);
//   - the pigments differ per instrument and shade with pitch; pitches read from the played-back event;
//   - the brush atlas: sixteen marks, clear at every cell's border, the stroke bodies tiling seamlessly;
//   - placing a note allocates nothing.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Decor;
using MusicRoom.Room.Textures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.Room
{
    public class WallPaintTests
    {
        const float W = 6.4f, D = 5.2f, H = 2.9f;

        /// <summary>A rectangular room centred on (0, 0) turned by yaw (degrees): walls inward; the +x wall in four pieces round
        /// a window 1.8 x 1.7 m at its middle (y 0.7 .. 2.4); a shelf against the -x wall, a frame on the -z wall, a chimney
        /// breast on the +z wall. Returns the obstacles' boxes (centre, half, rotation) for the checks.</summary>
        static WallCanvas Room(float yaw, List<(Vector3 c, Vector3 h, Quaternion r)> blocks)
        {
            var c = new WallCanvas();
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            // -z wall (normal +z), +x wall (normal -x), +z wall (normal -z), -x wall (normal +x)
            AddSolid(c, q, new Vector3(0f, 0f, -D / 2f), Vector3.forward, W);
            AddSolid(c, q, new Vector3(0f, 0f, D / 2f), Vector3.back, W);
            AddSolid(c, q, new Vector3(-W / 2f, 0f, 0f), Vector3.right, D);
            // +x wall: pieces round the window (as the room's own wall mesh: triangles)
            Vector3 n = q * Vector3.left, along = Vector3.Cross(Vector3.up, n).normalized;
            Vector3 origin = q * new Vector3(W / 2f, 0f, 0f) - along * (D / 2f);
            int w = c.AddWall(origin, along, n, D, H, solid: false);
            float wu0 = D / 2f - 0.9f, wu1 = D / 2f + 0.9f, wv0 = 0.7f, wv1 = 2.4f;
            Quad(c, w, origin, along, 0f, wu0, 0f, H);           // left of the window
            Quad(c, w, origin, along, wu1, D, 0f, H);            // right of it
            Quad(c, w, origin, along, wu0, wu1, 0f, wv0);        // under it
            Quad(c, w, origin, along, wu0, wu1, wv1, H);         // over it
            // things at the walls (room frame, then turned)
            Block(c, q, blocks, new Vector3(-W / 2f + 0.17f, 0.95f, -0.8f), new Vector3(0.17f, 0.95f, 0.55f));   // shelf at -x
            Block(c, q, blocks, new Vector3(-1.2f, 1.85f, -D / 2f + 0.03f), new Vector3(0.45f, 0.35f, 0.03f));     // a frame on -z
            Block(c, q, blocks, new Vector3(0.3f, 1.45f, D / 2f - 0.2f), new Vector3(0.9f, 1.45f, 0.2f));          // chimney breast on +z
            c.Finish();
            return c;
        }

        static void AddSolid(WallCanvas c, Quaternion q, Vector3 centre, Vector3 inward, float width)
        {
            Vector3 n = q * inward, along = Vector3.Cross(Vector3.up, n).normalized;
            c.AddWall(q * centre - along * (width / 2f), along, n, width, H, solid: true);
        }

        static void Quad(WallCanvas c, int wall, Vector3 o, Vector3 along, float u0, float u1, float v0, float v1)
        {
            Vector3 a = o + along * u0 + Vector3.up * v0, b = o + along * u1 + Vector3.up * v0, cc = o + along * u1 + Vector3.up * v1, d = o + along * u0 + Vector3.up * v1;
            c.MarkWall(wall, a, b, cc);
            c.MarkWall(wall, a, cc, d);
        }

        static void Block(WallCanvas c, Quaternion q, List<(Vector3, Vector3, Quaternion)> list, Vector3 centre, Vector3 half)
        {
            c.BlockBox(q * centre, half, q);
            list?.Add((q * centre, half, q));
        }

        /// <summary>A point on a wall (normal n) is covered by a box when the box lies in front of it within the band (seen
        /// straight on), padded by pad.</summary>
        static bool InBox(Vector3 p, Vector3 n, (Vector3 c, Vector3 h, Quaternion r) b, float pad)
        {
            for (float t = 0f; t <= WallCanvas.Band; t += 0.025f)
            {
                Vector3 l = Quaternion.Inverse(b.r) * (p + n * t - b.c);
                if (Mathf.Abs(l.x) <= b.h.x + pad && Mathf.Abs(l.y) <= b.h.y + pad && Mathf.Abs(l.z) <= b.h.z + pad) return true;
            }
            return false;
        }

        static IEnumerable<PaintNote> Song(int n, int seed, float gap = 0.25f)
        {
            var rng = new Mulberry32((uint)seed);
            for (int i = 0; i < n; i++)
            {
                int midi = 40 + (int)(rng.Next() * 50);
                yield return new PaintNote { Kind = (int)(rng.Next() * 3), Pitch = (midi - 36) / 60f, Level = 0.2f + 0.8f * (float)rng.Next(), Time = i * gap, Index = i, Key = midi };
            }
        }

        // ---------------------------------------------------------------- the canvas

        [Test]
        public void ClearCellsAvoidTheOpeningTheThingsAtTheWallsTheBandsAndTheCorners()
        {
            var blocks = new List<(Vector3, Vector3, Quaternion)>();
            WallCanvas c = Room(0f, blocks);
            Assert.That(c.WallCount, Is.EqualTo(4));
            Assert.That(c.Perimeter, Is.EqualTo(2f * (W + D)).Within(1e-3f));
            Assert.That(c.PaintableCells, Is.GreaterThan(1500), "most of the walls are clear");
            for (int w = 0; w < c.WallCount; w++)
            {
                c.WallFrame(w, out Vector3 o, out Vector3 along, out Vector3 n, out float width, out float height);
                for (float u = 0.05f; u < width; u += WallCanvas.Cell)
                    for (float v = 0.05f; v < height; v += WallCanvas.Cell)
                    {
                        if (!c.Paintable(w, u, v)) continue;
                        Assert.That(v, Is.GreaterThanOrEqualTo(c.MinY - 1e-4f), "above the floor band");
                        Assert.That(v, Is.LessThanOrEqualTo(height - c.TopClear + 1e-4f), "below the ceiling band (the night sky's)");
                        Assert.That(u, Is.InRange(c.EdgeClear - 1e-4f, width - c.EdgeClear + 1e-4f), "clear of the corners");
                        Vector3 p = o + along * u + Vector3.up * v;
                        foreach (var b in blocks) Assert.IsFalse(InBox(p, n, b, 0.02f), $"clear cell {p} over a thing at the wall");
                    }
            }
            // the window opening on the +x wall (normal -x): no wall there
            int win = -1;
            for (int w = 0; w < c.WallCount; w++) { c.WallFrame(w, out _, out _, out Vector3 n, out _, out _); if (n.x < -0.9f) win = w; }
            Assert.That(win, Is.GreaterThanOrEqualTo(0));
            Assert.That(c.CellAt(win, D / 2f, 1.5f), Is.EqualTo(WallCanvas.None), "the window is a hole");
            Assert.IsFalse(c.Paintable(win, D / 2f - 0.95f, 1.5f), "the opening's rim stays clear (margin)");
            Assert.IsTrue(c.Paintable(win, 0.8f, 1.5f), "the wall beside the window is paintable");
        }

        [Test]
        public void EverySplashOfALongSongLiesWhollyOnClearWall()
        {
            var blocks = new List<(Vector3, Vector3, Quaternion)>();
            WallCanvas c = Room(0f, blocks);
            int placed = 0;
            foreach (PaintNote note in Song(600, 3))
            {
                if (!c.Place(note, 0f, out PaintSpot s)) continue;
                placed++;
                Assert.IsTrue(c.Fits(s.Wall, s.U, s.V, s.Radius));
                Assert.That(s.Radius, Is.InRange(0.6f * c.RMin, c.RMax * 1.2f));
                c.WallFrame(s.Wall, out _, out _, out _, out _, out float height);
                Assert.That(s.V - s.Radius * 0.8f, Is.GreaterThanOrEqualTo(c.MinY - WallCanvas.Cell));
                Assert.That(s.V + s.Radius * 0.8f, Is.LessThanOrEqualTo(height - c.TopClear + WallCanvas.Cell));
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector3 p = s.Centre + s.Along * (Mathf.Cos(a) * s.Radius * 0.8f) + Vector3.up * (Mathf.Sin(a) * s.Radius * 0.8f);
                    foreach (var b in blocks) Assert.IsFalse(InBox(p, s.Normal, b, 0f), $"splash over a thing at the wall at {p}");
                }
                Assert.That(Vector3.Dot(s.Normal, Vector3.up), Is.EqualTo(0f).Within(1e-4f), "on an upright wall");
            }
            Assert.That(placed, Is.GreaterThan(570), "nearly every note finds clear wall");
        }

        [Test]
        public void TheSameTakePaintsTheSamePictureAndAnotherTakeAnother()
        {
            WallCanvas a = Room(0f, null), b = Room(0f, null);
            var first = new List<Vector3>();
            foreach (PaintNote n in Song(200, 5)) { a.Place(n, 1.3f, out PaintSpot s); first.Add(s.Centre); }
            int i = 0, same = 0;
            foreach (PaintNote n in Song(200, 5)) { b.Place(n, 1.3f, out PaintSpot s); Assert.That((s.Centre - first[i++]).magnitude, Is.LessThan(1e-5f)); }
            i = 0;
            foreach (PaintNote n in Song(200, 6)) { a.Place(n, 1.3f, out PaintSpot s); if ((s.Centre - first[i++]).magnitude < 0.05f) same++; }
            Assert.That(same, Is.LessThan(20), "another take lands elsewhere");
        }

        [Test]
        public void PitchSetsTheHeightAndTheSongMovesAlongTheWalls()
        {
            WallCanvas c = Room(0f, null);
            float low = 0f, high = 0f;
            int nl = 0, nh = 0;
            for (int i = 0; i < 200; i++)
            {
                var lo = new PaintNote { Kind = 0, Pitch = 0.1f, Level = 0.5f, Time = i * 0.2f, Index = i, Key = 40 };
                var hi = new PaintNote { Kind = 0, Pitch = 0.9f, Level = 0.5f, Time = i * 0.2f, Index = i, Key = 90 };
                if (c.Place(lo, 0f, out PaintSpot a)) { low += a.V; nl++; }
                if (c.Place(hi, 0f, out PaintSpot b)) { high += b.V; nh++; }
            }
            Assert.That(high / nh - low / nl, Is.GreaterThan(0.8f), "high notes paint higher");

            // along: the mean place on the perimeter of the first 20 s and of the next 20 s
            float Mean(float t0)
            {
                float sum = 0f; int n = 0;
                for (int i = 0; i < 100; i++)
                {
                    var note = new PaintNote { Kind = 1, Pitch = 0.5f, Level = 0.5f, Time = t0 + i * 0.2f, Index = i, Key = 60 };
                    if (!c.Place(note, 0f, out PaintSpot s)) continue;
                    c.WallFrame(s.Wall, out Vector3 o, out _, out _, out _, out _);
                    sum += Unwrap(c, s); n++;
                }
                return sum / n;
            }
            float m0 = Mean(0f), m1 = Mean(20f);
            Assert.That(m1 - m0, Is.EqualTo(c.Drift * 20f).Within(0.6f), "the song moves Drift metres a second along the walls");
        }

        /// <summary>A spot's place on the perimeter near 0 (unwrapped about it).</summary>
        static float Unwrap(WallCanvas c, PaintSpot s)
        {
            Vector3 head = Vector3.zero;
            float best = 0f, bd = float.MaxValue;
            for (float x = -3f; x < 6f; x += 0.01f)
            {
                c.Locate(x, out int w, out float u);
                if (w != s.Wall) continue;
                float d = Mathf.Abs(u - s.U);
                if (d < bd) { bd = d; best = x; }
            }
            return best;
        }

        [Test]
        public void TheWallsGoRoundTheRoomInOrderHoweverItIsTurned()
        {
            foreach (float yaw in new[] { 0f, 90f, 180f, 33f })
            {
                WallCanvas c = Room(yaw, null);
                // walking the perimeter: each step lands on the same wall or the next one round, its far end meeting the next's start
                Vector3 prev = default;
                bool any = false;
                for (float s = 0.02f; s < c.Perimeter; s += 0.05f)
                {
                    c.Locate(s, out int w, out float u);
                    Vector3 p = c.Point(w, u, 1f);
                    if (any) Assert.That((p - prev).magnitude, Is.LessThan(0.08f), $"yaw {yaw}: continuous at s {s:F2}");
                    prev = p; any = true;
                }
                // looking ahead from the middle meets the wall ahead
                Vector3 ahead = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float at = c.PerimeterAlong(new Vector3(0f, 1.2f, 0f), ahead);
                c.Locate(at, out int aw, out float au);
                Vector3 hit = c.Point(aw, au, 1.2f);
                Assert.That(Vector3.Dot(hit, ahead), Is.EqualTo(D / 2f).Within(0.02f), $"yaw {yaw}: the wall ahead");
                hit.y = 0f;
                Assert.That(Vector3.Cross(ahead, hit).magnitude, Is.LessThan(0.05f), $"yaw {yaw}: straight ahead");
            }
        }

        [Test]
        public void TheSongStartsInFrontOfThePlayer()
        {
            WallCanvas c = Room(0f, null);
            float s0 = c.PerimeterAlong(new Vector3(0f, 1.2f, 0f), Vector3.forward) - WallPaint.StartBefore;
            int ahead = 0, n = 0;
            for (int i = 0; i < 40; i++)
            {
                var note = new PaintNote { Kind = 0, Pitch = 0.5f, Level = 0.6f, Time = i * 0.25f, Index = i, Key = 60 + i % 7 };
                if (!c.Place(note, s0, out PaintSpot s)) continue;
                n++;
                if (s.Centre.z > D / 2f - 0.05f) ahead++;
            }
            Assert.That(ahead, Is.GreaterThan(n * 0.8f), "the first ten seconds paint the wall the player faces");
        }

        [Test]
        public void ARoomFullOfThingsPaintsNothingRatherThanOverThem()
        {
            var c = new WallCanvas();
            int w = c.AddWall(new Vector3(-2f, 0f, 2f), Vector3.right, Vector3.back, 4f, 2.6f, solid: true);
            c.BlockBox(new Vector3(0f, 1.3f, 1.9f), new Vector3(2f, 1.3f, 0.2f), Quaternion.identity);   // a wall of cupboards
            c.Finish();
            Assert.That(c.PaintableCells, Is.EqualTo(0));
            Assert.IsFalse(c.Place(new PaintNote { Pitch = 0.5f, Level = 1f, Index = 1 }, 0f, out _));
            // a door far from the wall (2 m out) blocks nothing
            var d = new WallCanvas();
            d.AddWall(new Vector3(-2f, 0f, 2f), Vector3.right, Vector3.back, 4f, 2.6f, solid: true);
            d.BlockBox(new Vector3(0f, 1f, 0f), new Vector3(0.5f, 1f, 0.05f), Quaternion.identity);
            d.Finish();
            Assert.IsTrue(d.Paintable(0, 2f, 1.3f));
        }

        [Test]
        public void PlacingANoteAllocatesNothing()
        {
            WallCanvas c = Room(0f, null);
            var note = new PaintNote { Kind = 0, Pitch = 0.5f, Level = 0.8f, Time = 1f, Index = 3, Key = 64 };
            var drum = TakeEvent.Drum("snare", 0.8f, 0.2f, "head", false);
            // warm (first calls)
            c.Place(note, 0f, out _);
            PaintPalette.Of(InstrumentKind.Piano, 0.7f);
            PaintPalette.PitchOf(InstrumentKind.Drums, drum, out _);
            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    note.Index = i; note.Time = i * 0.1f;
                    c.Place(note, 0f, out _);
                }
                PaintPalette.Of(InstrumentKind.Piano, 0.7f);
                PaintPalette.PitchOf(InstrumentKind.Drums, drum, out _);
            }, Is.Not.AllocatingGCMemory());
        }

        // ---------------------------------------------------------------- colours and pitches

        [Test]
        public void EachInstrumentHasItsPigmentShadedByPitch()
        {
            var kinds = new[] { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp, InstrumentKind.Handpan, InstrumentKind.HandDrums, InstrumentKind.Marimba, InstrumentKind.Bowls };
            for (int i = 0; i < kinds.Length; i++)
                for (int j = i + 1; j < kinds.Length; j++)
                {
                    Color a = PaintPalette.Pigment(kinds[i]), b = PaintPalette.Pigment(kinds[j]);
                    Assert.That(Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b), Is.GreaterThan(0.12f), $"{kinds[i]} vs {kinds[j]}");
                }
            foreach (InstrumentKind k in kinds)
            {
                Color lo = PaintPalette.Of(k, 0f), hi = PaintPalette.Of(k, 1f);
                Assert.That(hi.grayscale, Is.GreaterThan(lo.grayscale), $"{k}: high notes lighter");
                Color.RGBToHSV(PaintPalette.Of(k, 0.5f), out _, out float s, out _);
                Assert.That(s, Is.GreaterThan(0.25f), $"{k}: a colour, not a grey");
            }
        }

        [Test]
        public void PitchesComeFromThePlayedBackEvent()
        {
            Assert.That(PaintPalette.PitchOf(InstrumentKind.Piano, TakeEvent.PianoOn(36, 1f), out int k1), Is.EqualTo(0f));
            Assert.That(PaintPalette.PitchOf(InstrumentKind.Piano, TakeEvent.PianoOn(96, 1f), out int k2), Is.EqualTo(1f));
            Assert.That(k1, Is.Not.EqualTo(k2));
            float kick = PaintPalette.PitchOf(InstrumentKind.Drums, TakeEvent.Drum("kick", 1f, 0f, "head", false), out _);
            float snare = PaintPalette.PitchOf(InstrumentKind.Drums, TakeEvent.Drum("snare", 1f, 0f, "head", false), out _);
            float crash = PaintPalette.PitchOf(InstrumentKind.Drums, TakeEvent.Drum("crash", 1f, 0f, "bow", false), out _);
            Assert.That(kick, Is.LessThan(snare));
            Assert.That(snare, Is.LessThan(crash));
            float h0 = PaintPalette.PitchOf(InstrumentKind.Harp, TakeEvent.Harp(0, 1f, 0.5f, 0.3f, "through"), out _);
            float h21 = PaintPalette.PitchOf(InstrumentKind.Harp, TakeEvent.Harp(21, 1f, 0.5f, 0.3f, "through"), out _);
            Assert.That(h0, Is.EqualTo(0f));
            Assert.That(h21, Is.EqualTo(1f));
        }

        [Test]
        public void TheBrushAtlasHasSixteenNatureMotifsClearAtTheirBordersAndStrokesThatTile()
        {
            for (int cell = 0; cell < PaintArt.Cells; cell++)
            {
                int x0 = (cell % PaintArt.Grid) * PaintArt.CellPx, y0 = (cell / PaintArt.Grid) * PaintArt.CellPx;
                float cover = 0f;
                int n = 0;
                for (int y = y0 + 8; y < y0 + PaintArt.CellPx - 8; y += 4)
                    for (int x = x0 + 8; x < x0 + PaintArt.CellPx - 8; x += 4)
                    {
                        PaintArt.Pixel(x, y, out _, out float a);
                        cover += a; n++;
                    }
                bool sparse = cell == PaintArt.Bird || cell == PaintArt.Trunk;   // thin shapes: wings, a trunk and boughs
                Assert.That(cover / n, Is.InRange(sparse ? 0.02f : 0.12f, PaintArt.Tiles(cell) ? 0.95f : 0.75f), $"cell {cell}: a mark, not a square and not nothing");
                for (int i = 0; i < PaintArt.CellPx; i++)
                {
                    PaintArt.Pixel(x0 + i, y0, out _, out float top);
                    PaintArt.Pixel(x0 + i, y0 + PaintArt.CellPx - 1, out _, out float bottom);
                    Assert.That(top + bottom, Is.LessThan(0.01f), $"cell {cell}: clear at its top and bottom");
                    if (PaintArt.Tiles(cell))
                    {
                        // a stroke's body repeats: column 127 is column 1 again
                        PaintArt.Pixel(x0 + 1, y0 + i, out float d1, out float a1);
                        PaintArt.Pixel(x0 + PaintArt.CellPx - 1, y0 + i, out float d2, out float a2);
                        Assert.That(Mathf.Abs(a1 - a2) + Mathf.Abs(d1 - d2), Is.LessThan(1e-4f), $"cell {cell}: tiles seamlessly");
                        continue;
                    }
                    PaintArt.Pixel(x0, y0 + i, out _, out float left);
                    PaintArt.Pixel(x0 + PaintArt.CellPx - 1, y0 + i, out _, out float right);
                    Assert.That(left + right, Is.LessThan(0.01f), $"cell {cell}: clear at its sides");
                }
                if (sparse) continue;
                PaintArt.Pixel(x0 + PaintArt.CellPx / 2, y0 + PaintArt.CellPx / 2, out float dens, out float mid);
                Assert.That(mid, Is.GreaterThan(0.45f), $"cell {cell}: opaque in the middle");
                Assert.That(dens, Is.InRange(0.05f, 1f));
            }
            Assert.That(PaintArt.TileU(0f), Is.EqualTo(PaintArt.TileU(1f)).Within(1e-6f));
            Assert.That(PaintArt.TileU(0.5f), Is.InRange(1f / 128f, 127f / 128f));
        }
    }
}
