// The nature paint's maths, without Unity objects (round 2, 2026-11-13; round 4, 2026-11-16: "make paintings of flowers
// birds nature scenery"): Room/Decor/PaintBrushes.cs (each instrument's motif), PaintThrow.cs (the throw gesture),
// GalleryPicture.cs and PaintGallery.cs (the framed landscapes), the canvas's parts (WallCanvas: reserving, a ray):
//   - the piano's vine: one continuous stem (each piece starts where the last ended), rising and falling with the notes,
//     thin, grown piece after piece, tapering at the phrase's end; every melody note blooms a flower on a stalk off it
//     (a blossom or a tulip, a golden heart, now and then a leaf); a chord note hangs a bud;
//   - the drums plant the ground: a strong kick a tree (a crown up on its trunk), a soft one a bush, the snare grass and
//     pebbles, the ride pebbles, the toms leaves drifting down, a crash a gust of them;
//   - the harp: a flock, a bird a note along an arc that a glissando sweeps on (a pause or a far string starts another);
//   - the bowls clouds, the handpan butterflies, the hand drums mushrooms, the marimba slim trees; each where it belongs
//     (the ground low, the sky high);
//   - every motif lies within its Reach of its anchor, for every instrument; the same note, the same quads;
//   - thrown paint bursts into a flower with petals and leaves;
//   - the throw: a quick forward flick of an open hand is a throw; slow moves, a busy hand, a pinch, a fist, a strike
//     downward, a flick toward the face, a pose the tracker repeats are not; one throw per hand per Refractory;
//   - the gallery: slots on clear wall, nearest the view first, apart, never over what blocks a wall; the saved list
//     parses back (bad parts dropped); a reserved frame keeps paint off; a ray finds the wall it hits;
//   - the picture: the same take, the same pixels; another take, another picture; a landscape from the song (a lively
//     song a bright day, a calm one dusk; hills following the melody; flowers, trees, birds, butterflies, mushrooms,
//     clouds from their instruments; long notes water).
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
    public class PaintBrushTests
    {
        static BrushNote Note(InstrumentKind kind, float pitch, float level, int key, float time = 0f, int index = 0, bool accent = false) =>
            new BrushNote { Kind = kind, Pitch = pitch, Level = level, Key = key, Time = time, Hash = WallCanvas.Hash(index, key, (int)kind), Accent = accent };

        static float Width(in BrushQuad q) => ((q.D - q.A).magnitude + (q.C - q.B).magnitude) * 0.5f;

        // ------------------------------------------------------------------------------------------------ the piano's vine

        [Test]
        public void ThePianoGrowsOneFlowingVineThatTapersAtThePhrasesEnd()
        {
            var b = new PaintBrushes();
            var quads = new List<BrushQuad>();
            float[] heights = { 1.4f, 1.55f, 1.7f, 1.6f, 1.45f, 1.5f, 1.75f };
            float[] levels = { 0.3f, 0.5f, 1f, 0.8f, 0.4f, 0.3f, 0.9f };
            for (int i = 0; i < heights.Length; i++)
            {
                b.Clear();
                b.RibbonPoint(new Vector2(0.3f * i, heights[i]), levels[i], 0.4f * i, 0.25f, Color.green, 0.8f);
                Assert.That(b.Count, Is.GreaterThan(0), "every melody note grows it on");
                for (int q = 0; q < b.Count; q++) quads.Add(b.Out[q]);
            }
            Assert.IsTrue(b.RibbonOpen);
            b.Clear();
            b.RibbonEnd();
            Assert.That(b.Count, Is.GreaterThan(0));
            for (int q = 0; q < b.Count; q++) quads.Add(b.Out[q]);
            Assert.IsFalse(b.RibbonOpen);
            for (int i = 0; i < quads.Count; i++)
            {
                BrushQuad q = quads[i];
                Assert.That(q.Cell, Is.EqualTo(PaintArt.Ribbon));
                Assert.That(q.Mode, Is.EqualTo(BrushMode.Wipe), "drawn as it grows");
                if (i == 0) continue;
                // one continuous stem: each piece starts at the last one's end edge
                Assert.That((q.A - quads[i - 1].B).magnitude, Is.LessThan(1e-4f), $"piece {i} joins");
                Assert.That((q.D - quads[i - 1].C).magnitude, Is.LessThan(1e-4f));
                Assert.That(q.UvA.x, Is.EqualTo(quads[i - 1].UvB.x).Within(1e-4f), "the texture runs on");
            }
            // it rises and dips with the notes: the stem passes near each note's height
            for (int i = 1; i < heights.Length; i++)
            {
                float best = float.MaxValue;
                foreach (BrushQuad q in quads) best = Mathf.Min(best, (((q.B + q.C) * 0.5f) - new Vector2(0.3f * i, heights[i])).magnitude);
                Assert.That(best, Is.LessThan(0.01f), $"through note {i}");
            }
            // a stem, not a ribbon: thin; loud is a little thicker
            float WidthAt(Vector2 p) { float w = 0f, d = float.MaxValue; foreach (BrushQuad q in quads) { float dd = (((q.B + q.C) * 0.5f) - p).magnitude; if (dd < d) { d = dd; w = (q.C - q.B).magnitude; } } return w; }
            Assert.That(WidthAt(new Vector2(0.6f, 1.7f)), Is.InRange(0.02f, 0.035f), "a loud note's stem");
            Assert.That(WidthAt(new Vector2(0.6f, 1.7f)), Is.GreaterThan(WidthAt(new Vector2(1.5f, 1.5f)) * 1.3f), "loud thicker than soft");
            BrushQuad last = quads[quads.Count - 1];
            Assert.That((last.C - last.B).magnitude, Is.LessThan(0.005f), "a fine tip");
            // a note's pieces are grown one after the other
            b.Reset();
            b.RibbonPoint(new Vector2(0f, 1.5f), 0.5f, 0f, 0.2f, Color.green, 0.8f);
            b.Clear();
            b.RibbonPoint(new Vector2(0.3f, 1.8f), 0.5f, 0.3f, 0.3f, Color.green, 0.8f);
            Assert.That(b.Count, Is.GreaterThan(2));
            for (int q = 1; q < b.Count; q++) Assert.That(b.Out[q].Delay, Is.GreaterThan(b.Out[q - 1].Delay));
            Assert.That(b.Out[b.Count - 1].Delay + b.Out[b.Count - 1].Dur, Is.EqualTo(0.3f).Within(1e-4f));
        }

        [Test]
        public void EveryMelodyNoteBloomsAFlowerOffTheVineHigherNotesHigher()
        {
            var b = new PaintBrushes();
            int flowers = 0, tulips = 0, leaves = 0;
            for (int i = 0; i < 40; i++)
            {
                BrushNote n = Note(InstrumentKind.Piano, 0.5f, 0.3f + 0.6f * (i % 5) / 4f, 60 + i % 12, 0.3f * i, i);
                var vine = new Vector2(0.3f * i, 1.5f);
                PaintBrushes.BloomSpot(n, vine, 1f, out Vector2 head, out float r);
                Assert.That(head.y, Is.GreaterThan(vine.y + 0.03f), "a flower stands up off the vine");
                Assert.That((head - vine).magnitude, Is.InRange(0.06f, 0.2f), "on a short stalk");
                Assert.That(r, Is.InRange(0.045f, 0.085f));
                b.Clear();
                b.Bloom(n, vine, head, r, Color.red, 0.8f, 0.2f);
                Assert.That(b.Out[0].Cell, Is.EqualTo(PaintArt.Ribbon), "a stalk first");
                Assert.That(b.Out[0].Mode, Is.EqualTo(BrushMode.Wipe), "growing out from the vine");
                Assert.That((b.Out[0].A + b.Out[0].D) * 0.5f - vine, Has.Property("magnitude").LessThan(1e-4f));
                bool head1 = false;
                for (int q = 0; q < b.Count; q++)
                {
                    BrushQuad x = b.Out[q];
                    Assert.That(x.Delay, Is.GreaterThanOrEqualTo(0.2f), "after the vine reaches it");
                    if (x.Cell == PaintArt.Blossom || x.Cell == PaintArt.Bud)
                    {
                        head1 = true;
                        Assert.That((x.Centre - head).magnitude, Is.LessThan(1e-3f));
                        Assert.That(x.Mode, Is.EqualTo(BrushMode.Grow), "it opens");
                        Assert.That(x.P1, Is.LessThan(0.3f), "from a bud");
                        if (x.Cell == PaintArt.Blossom) flowers++; else tulips++;
                    }
                    if (x.Cell == PaintArt.Leaf) leaves++;
                    if (x.Cell == PaintArt.Bead) Assert.That(x.Colour.g, Is.GreaterThan(x.Colour.b), "a golden heart");
                }
                Assert.IsTrue(head1, "a flower head");
            }
            Assert.That(flowers, Is.GreaterThan(20), "mostly five-petal blossoms");
            Assert.That(tulips, Is.GreaterThan(3), "now and then a tulip");
            Assert.That(leaves, Is.InRange(10, 30), "a leaf on the vine every other note or so");
            // where the vine is, a higher note is higher (the caller's height for a pitch); the flower rides on it
            BrushNote hi = Note(InstrumentKind.Piano, 0.9f, 0.6f, 84), lo = Note(InstrumentKind.Piano, 0.1f, 0.6f, 36);
            Assert.That(PaintBrushes.Height(hi), Is.GreaterThan(PaintBrushes.Height(lo)));
        }

        [Test]
        public void AChordNoteHangsABudOffTheVine()
        {
            var b = new PaintBrushes();
            var vine = new Vector2(1f, 1.5f);
            BrushNote n = Note(InstrumentKind.Piano, 0.4f, 0.8f, 55);
            PaintBrushes.ChordSpot(n, vine, 1f, out Vector2 bud, out float r);
            Assert.That(bud.y, Is.LessThan(vine.y - 0.03f), "it hangs below the vine");
            b.ChordBud(n, vine, bud, r, Color.red, 0.7f, 0.1f);
            Assert.That(b.Count, Is.EqualTo(2));
            Assert.That(b.Out[0].Cell, Is.EqualTo(PaintArt.Ribbon), "a stalk");
            Assert.That(b.Out[1].Cell, Is.EqualTo(PaintArt.Bud), "a tulip bud");
            Assert.That((b.Out[1].D - b.Out[1].A).y, Is.LessThan(0f), "turned down like a bell");
        }

        // ------------------------------------------------------------------------------------------------ each its motif

        static int CountOf(PaintBrushes b, int cell) { int n = 0; for (int q = 0; q < b.Count; q++) if (b.Out[q].Cell == cell) n++; return n; }

        [Test]
        public void TheDrumsPlantTheGround()
        {
            var b = new PaintBrushes();
            var at = new Vector2(2f, 1.2f);
            b.Mark(Note(InstrumentKind.Drums, 0.05f, 0.9f, 1), at, 1f, Color.red);   // a strong kick: a tree
            Assert.That(CountOf(b, PaintArt.Trunk), Is.EqualTo(1));
            Assert.That(CountOf(b, PaintArt.Crown), Is.EqualTo(3), "a crown and two side clumps");
            BrushQuad trunk = b.Out[0];
            float crownY = float.MinValue;
            for (int q = 0; q < b.Count; q++) if (b.Out[q].Cell == PaintArt.Crown) crownY = Mathf.Max(crownY, b.Out[q].Centre.y);
            Assert.That(crownY, Is.GreaterThan(trunk.Centre.y + 0.1f), "the crown up on the trunk");
            Assert.That(Mathf.Min(trunk.A.y, trunk.B.y), Is.LessThan(at.y - 0.2f), "standing on the ground below");
            float tall = crownY - Mathf.Min(trunk.A.y, trunk.B.y);
            b.Clear();
            b.Mark(Note(InstrumentKind.Drums, 0.05f, 0.3f, 1), at, 1f, Color.red);   // a soft kick: a bush
            Assert.That(CountOf(b, PaintArt.Trunk), Is.EqualTo(0));
            Assert.That(CountOf(b, PaintArt.Crown), Is.EqualTo(3));
            b.Clear();
            b.Mark(Note(InstrumentKind.Drums, 0.55f, 0.8f, 5), at, 1f, Color.red);   // the snare: grass and pebbles
            Assert.That(CountOf(b, PaintArt.Grass), Is.EqualTo(1));
            Assert.That(CountOf(b, PaintArt.Blot), Is.EqualTo(2));
            b.Clear();
            b.Mark(Note(InstrumentKind.Drums, 0.86f, 0.8f, 7), at, 1f, Color.red);   // the ride: pebbles
            Assert.That(CountOf(b, PaintArt.Blot), Is.EqualTo(3));
            b.Clear();
            b.Mark(Note(InstrumentKind.Drums, 0.36f, 0.9f, 3), at, 1f, Color.red);   // a tom: leaves letting go
            Assert.That(CountOf(b, PaintArt.Leaf), Is.EqualTo(3));
            int falls = 0;
            for (int q = 0; q < b.Count; q++)
            {
                if (b.Out[q].Mode != BrushMode.Fall) continue;
                falls++;
                Assert.That(b.Out[q].P1, Is.InRange(0.05f, 0.4f), "drifts some way down");
                Assert.That(b.Out[q].Delay, Is.GreaterThan(0.2f), "after it shows");
                Assert.That(b.Out[q].Dur, Is.InRange(1f, 2.5f), "over a second or two");
            }
            Assert.That(falls, Is.EqualTo(2));
            b.Clear();
            b.Mark(Note(InstrumentKind.Drums, 0.96f, 0.9f, 8), at, 1f, Color.red);   // a crash: a gust
            Assert.That(CountOf(b, PaintArt.Leaf), Is.EqualTo(5));
            // where they belong: the ground for the kick, the snare and the ride, higher for leaves in the air
            Assert.That(PaintBrushes.Height(Note(InstrumentKind.Drums, 0.05f, 0.9f, 1)), Is.LessThan(0.1f));
            Assert.That(PaintBrushes.Height(Note(InstrumentKind.Drums, 0.55f, 0.9f, 5)), Is.LessThan(0.1f));
            Assert.That(PaintBrushes.Height(Note(InstrumentKind.Drums, 0.96f, 0.9f, 8)), Is.GreaterThan(0.4f));
            Assert.That(tall, Is.InRange(0.28f, 0.6f), "a tree you see from the seat");
        }

        [Test]
        public void AGlissandoIsAFlockOfBirdsSwoopingAlongAnArc()
        {
            var b = new PaintBrushes();
            var centre = new Vector2(3f, 1.8f);
            BrushNote first = Note(InstrumentKind.Harp, 0.3f, 0.7f, 106, 0f);
            Assert.IsFalse(b.ArcContinues(first));
            b.ArcStart(first, centre, 1f, Color.cyan);
            var birds = new List<BrushQuad>();
            var others = new List<BrushQuad>();
            void Take() { for (int q = 0; q < b.Count; q++) (b.Out[q].Cell == PaintArt.Bird ? birds : others).Add(b.Out[q]); }
            Take();
            Assert.That(birds.Count, Is.EqualTo(1), "a pluck: a bird");
            float th0 = b.ArcAngle;
            for (int i = 1; i <= 4; i++)
            {
                BrushNote n = Note(InstrumentKind.Harp, 0.3f + 0.04f * i, 0.7f, 106 + i, 0.08f * i, i);
                Assert.IsTrue(b.ArcContinues(n), $"note {i} of the glissando joins the flock");
                b.Clear();
                b.ArcNext(n, Color.cyan);
                Take();
            }
            Assert.That(birds.Count, Is.EqualTo(5), "a bird a note");
            Assert.That(b.ArcAngle, Is.LessThan(th0 - 1.5f), "swooped on over the top");
            float R = b.ArcRadius;
            float lastAngle = float.MaxValue;
            foreach (BrushQuad q in birds)
            {
                Vector2 d = q.Centre - centre;
                Assert.That(d.magnitude, Is.InRange(R - 0.045f, R + 0.045f), "along its arc");
                float a = Mathf.Atan2(d.y, d.x);
                Assert.That(a, Is.LessThan(lastAngle), "each one further on");
                lastAngle = a;
                Assert.That(q.Colour.b, Is.LessThan(1f), "the harp's colour deepened");
                Assert.That((q.B - q.A).magnitude, Is.InRange(0.1f, 0.25f), "a bird you see");
            }
            Assert.That(others.Count, Is.EqualTo(0), "only birds");
            // apart: the next bird clear of the last (they read as a flock, not a smudge)
            for (int i = 1; i < birds.Count; i++)
                Assert.That((birds[i].Centre - birds[i - 1].Centre).magnitude, Is.GreaterThan(0.8f * (birds[i].B - birds[i].A).magnitude));
            // a pause, or a string far away, starts another flock
            Assert.IsFalse(b.ArcContinues(Note(InstrumentKind.Harp, 0.5f, 0.7f, 113, 0.48f + PaintBrushes.GlissGap + 0.05f)));
            Assert.IsFalse(b.ArcContinues(Note(InstrumentKind.Harp, 0.9f, 0.7f, 120, 0.52f)));
            Assert.That(PaintBrushes.Height(first), Is.GreaterThan(0.6f), "birds fly high");
        }

        [Test]
        public void BowlsCloudsHandpanButterfliesHandDrumsMushroomsMarimbaTrees()
        {
            var b = new PaintBrushes();
            var at = new Vector2(1f, 1.5f);
            b.Mark(Note(InstrumentKind.Bowls, 0.5f, 0.8f, 401), at, 1f, Color.blue);
            Assert.That(CountOf(b, PaintArt.Cloud), Is.EqualTo(1));
            Assert.That(b.Out[0].Mode, Is.EqualTo(BrushMode.Grow));
            Assert.That(b.Out[0].Dur, Is.GreaterThan(2f), "swelling slowly");
            Assert.That(b.Out[0].Colour.r, Is.GreaterThan(Color.blue.r), "a soft cloud, the bowls' colour lightened");
            Assert.That(PaintBrushes.Height(Note(InstrumentKind.Bowls, 0.5f, 0.8f, 401)), Is.GreaterThan(0.8f), "high in the sky");
            b.Clear(); b.Mark(Note(InstrumentKind.Handpan, 0.5f, 0.5f, 303), at, 1f, Color.yellow);
            Assert.That(CountOf(b, PaintArt.Butterfly), Is.EqualTo(1));
            Assert.That(CountOf(b, PaintArt.Droplet), Is.EqualTo(4), "its dotted flight");
            b.Clear(); b.Mark(Note(InstrumentKind.Handpan, 0.5f, 0.95f, 303), at, 1f, Color.yellow);
            Assert.That(CountOf(b, PaintArt.Butterfly), Is.EqualTo(2), "a loud note brings another");
            b.Clear(); b.Mark(Note(InstrumentKind.HandDrums, 0.5f, 0.7f, 214), at, 1f, Color.red);
            Assert.That(CountOf(b, PaintArt.Cap), Is.EqualTo(2), "mushrooms");
            b.Clear(); b.Mark(Note(InstrumentKind.HandDrums, 0.5f, 0.7f, 214, accent: true), at, 1f, Color.red);
            Assert.That(CountOf(b, PaintArt.Cap), Is.EqualTo(3), "a slap: one more");
            Assert.That(CountOf(b, PaintArt.Grass), Is.EqualTo(1));
            for (int q = 0; q < b.Count; q++) if (b.Out[q].Cell == PaintArt.Blot) Assert.That(b.Out[q].Colour, Is.EqualTo(PaintBrushes.Cream), "pale stems");
            b.Clear(); b.Mark(Note(InstrumentKind.Marimba, 0.7f, 0.4f, 72), at, 1f, Color.green);
            Assert.That(CountOf(b, PaintArt.Trunk), Is.EqualTo(1));
            Assert.That(CountOf(b, PaintArt.Crown), Is.EqualTo(1), "one slim crown");
            BrushQuad crown = b.Out[1];
            Assert.That((crown.D - crown.A).magnitude, Is.GreaterThan((crown.B - crown.A).magnitude * 1.5f), "tall and slim");
            b.Clear(); b.Mark(Note(InstrumentKind.Marimba, 0.7f, 1f, 72), at, 1f, Color.green);
            Assert.That(CountOf(b, PaintArt.Trunk), Is.EqualTo(2), "a loud run: a little one beside");
            // the ground's motifs low, the sky's high
            Assert.That(PaintBrushes.Height(Note(InstrumentKind.HandDrums, 0.5f, 0.7f, 214)), Is.LessThan(0.1f));
            Assert.That(PaintBrushes.Height(Note(InstrumentKind.Marimba, 0.7f, 1f, 72)), Is.LessThan(0.15f));
        }

        [Test]
        public void EveryMarkLiesWithinItsReachAndTheSameNotePaintsTheSame()
        {
            var b = new PaintBrushes();
            var c = new PaintBrushes();
            InstrumentKind[] kinds = { InstrumentKind.Drums, InstrumentKind.Bowls, InstrumentKind.Handpan, InstrumentKind.HandDrums, InstrumentKind.Marimba, InstrumentKind.Piano, InstrumentKind.Harp };
            var rng = new System.Random(7);
            for (int i = 0; i < 700; i++)
            {
                InstrumentKind k = kinds[i % kinds.Length];
                int key = k == InstrumentKind.Drums ? 1 + rng.Next(8) : 100 + rng.Next(30);
                BrushNote n = Note(k, (float)rng.NextDouble(), 0.05f + 0.95f * (float)rng.NextDouble(), key, i * 0.1f, i, rng.Next(2) == 0);
                float reach = PaintBrushes.Reach(n), scale = 0.65f + 0.35f * (float)rng.NextDouble();
                var at = new Vector2(5f, 1.5f);
                b.Clear(); c.Clear();
                b.Mark(n, at, scale, Color.white);
                c.Mark(n, at, scale, Color.white);
                Assert.That(b.Count, Is.GreaterThan(0));
                Assert.That(c.Count, Is.EqualTo(b.Count));
                for (int q = 0; q < b.Count; q++)
                {
                    BrushQuad x = b.Out[q], y = c.Out[q];
                    Assert.That((x.A - y.A).magnitude + (x.C - y.C).magnitude + Mathf.Abs(x.Delay - y.Delay), Is.LessThan(1e-6f), "deterministic");
                    foreach (Vector2 p in new[] { x.A, x.B, x.C, x.D })
                        Assert.That((p - at).magnitude, Is.LessThan(reach * scale * 1.42f + 1e-3f), $"{k} mark {q} within its reach");
                }
            }
        }

        [Test]
        public void AThrownSplatBurstsIntoPetalsAndLeaves()
        {
            var b = new PaintBrushes();
            b.ThrowSplat(new Vector2(1f, 1.6f), 0.17f, 12345u, Color.magenta);
            Assert.That(b.Out[0].Cell, Is.EqualTo(PaintArt.Blossom), "a flower where it lands");
            int petals = 0, falls = 0;
            for (int q = 1; q < b.Count; q++) { if (b.Out[q].Cell == PaintArt.Petal) petals++; if (b.Out[q].Mode == BrushMode.Fall) falls++; }
            Assert.That(petals, Is.EqualTo(9), "petals flying round it");
            Assert.That(falls, Is.EqualTo(3), "some drifting down");
            Assert.That(CountOf(b, PaintArt.Leaf), Is.EqualTo(3));
        }

        [Test]
        public void BrushesAllocateNothing()
        {
            var b = new PaintBrushes();
            BrushNote d = Note(InstrumentKind.Drums, 0.05f, 0.9f, 1), h = Note(InstrumentKind.Harp, 0.5f, 0.6f, 110), p = Note(InstrumentKind.Piano, 0.5f, 0.6f, 60);
            void Paint(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    b.Clear();
                    b.Mark(d, new Vector2(1f, 1.5f), 1f, Color.red);
                    b.Clear();
                    var v = new Vector2(0.1f * i, 1.5f + 0.1f * Mathf.Sin(i));
                    b.RibbonPoint(v, 0.6f, 0.2f * i, 0.2f, Color.green, 0.8f);
                    PaintBrushes.BloomSpot(p, v, 1f, out Vector2 head, out float r);
                    b.Bloom(p, v, head, r, Color.red, 0.8f, 0.2f);
                    PaintBrushes.ChordSpot(p, v, 1f, out Vector2 bud, out float br);
                    b.ChordBud(p, v, bud, br, Color.red, 0.8f, 0.1f);
                    b.Clear();
                    if (b.ArcContinues(h)) b.ArcNext(h, Color.cyan); else b.ArcStart(h, new Vector2(2f, 1.4f), 1f, Color.cyan);
                    b.Clear();
                    b.ThrowSplat(new Vector2(1f, 1.5f), 0.17f, (uint)i, Color.red);
                }
            }
            Paint(10);
            Assert.That(() => Paint(200), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------------------------------------------ the throw

        /// <summary>One hand's samples at rate Hz: from start, moving with velocity(t) for seconds; the head at (0, 1.2, 0)
        /// looking +z. repeat: each pose held for that many frames (a tracker slower than the frames).</summary>
        static List<ThrowSample> Motion(Vector3 start, System.Func<float, Vector3> velocity, float seconds, float hz = 72f, float open = 0.18f, bool busy = false, bool pinch = false, int repeat = 1, float t0 = 1f)
        {
            var list = new List<ThrowSample>();
            Vector3 p = start, shown = start;
            int frames = Mathf.RoundToInt(seconds * hz);
            for (int i = 0; i <= frames; i++)
            {
                float t = i / hz;
                if (i % repeat == 0) shown = p;
                list.Add(new ThrowSample { Time = t0 + t, Palm = shown, Open = open, Busy = busy, Pinch = pinch, Tracked = true });
                p += velocity(t) / hz;
            }
            return list;
        }

        /// <summary>A flick: speeding up to peak (m/s) along dir over 0.12 s, snapping back to rest over 0.08 s, then still.</summary>
        static System.Func<float, Vector3> Flick(Vector3 dir, float peak) => t =>
        {
            float s = t < 0.12f ? peak * (t / 0.12f) : t < 0.2f ? peak * (1f - (t - 0.12f) / 0.08f) : 0f;
            return dir.normalized * s;
        };

        static int Throws(ThrowGesture g, List<ThrowSample> s, out Vector3 dir, int hand = 1)
        {
            int n = 0;
            dir = default;
            var head = new Vector3(0f, 1.2f, 0f);
            foreach (ThrowSample x in s)
                if (g.Push(hand, x, head, Vector3.forward, out _, out Vector3 d, out _)) { n++; dir = d; }
            return n;
        }

        static readonly Vector3 Chest = new Vector3(0.2f, 1.0f, 0.25f);

        [Test]
        public void AQuickForwardFlickOfAnOpenHandIsAThrow()
        {
            var g = new ThrowGesture();
            var rest = Motion(Chest, t => Vector3.zero, 0.3f);
            Assert.That(Throws(g, rest, out _), Is.EqualTo(0), "a hand at rest");
            var flick = Motion(Chest, Flick(new Vector3(0f, 0.25f, 1f), 2.8f), 0.4f, t0: 2f);
            Assert.That(Throws(g, flick, out Vector3 dir), Is.EqualTo(1), "one throw: " + g.Why);
            Assert.That(Vector3.Dot(dir, new Vector3(0f, 0.25f, 1f).normalized), Is.GreaterThan(0.95f), "where the hand threw");
            // sideways forward (toward a side wall ahead) too
            var g2 = new ThrowGesture();
            Assert.That(Throws(g2, Motion(Chest, Flick(new Vector3(0.8f, 0.1f, 1f), 2.5f), 0.4f), out _), Is.EqualTo(1));
            // a tracker at half the frame rate (each pose twice) still sees it
            var g3 = new ThrowGesture();
            Assert.That(Throws(g3, Motion(Chest, Flick(Vector3.forward, 2.8f), 0.4f, repeat: 2), out _), Is.EqualTo(1), "repeated poses: " + g3.Why);
        }

        [Test]
        public void SlowMovesPlayingPinchesFistsStrikesAndTheFaceAreNotThrows()
        {
            void None(string what, List<ThrowSample> s) { var g = new ThrowGesture(); Assert.That(Throws(g, s, out _), Is.EqualTo(0), what + " (" + g.Why + ")"); }
            None("a slow reach forward", Motion(Chest, t => Vector3.forward * 0.7f, 0.6f));
            None("a slow reach, the tracker repeating poses", Motion(Chest, t => Vector3.forward * 0.9f, 0.6f, repeat: 3));
            None("a flick while playing an instrument (busy)", Motion(Chest, Flick(Vector3.forward, 3f), 0.4f, busy: true));
            None("a flick mid-pinch", Motion(Chest, Flick(Vector3.forward, 3f), 0.4f, pinch: true));
            None("a fist punching forward", Motion(Chest, Flick(Vector3.forward, 3f), 0.4f, open: 0.09f));
            None("a pointing hand", Motion(Chest, Flick(Vector3.forward, 3f), 0.4f, open: 0.135f));
            None("a stroke down onto a drum", Motion(Chest, Flick(new Vector3(0f, -1f, 0.15f), 3.5f), 0.4f));
            None("a hand flung back toward the face", Motion(Chest + Vector3.forward * 0.3f, Flick(new Vector3(-0.2f, 0.2f, -1f), 3f), 0.4f));
            None("a hand thrown behind", Motion(Chest, Flick(new Vector3(0f, 0f, -1f), 3f), 0.4f));
            None("a short twitch", Motion(Chest, Flick(Vector3.forward, 1.0f), 0.4f));
            None("straight up", Motion(Chest, Flick(Vector3.up, 3f), 0.4f));
        }

        [Test]
        public void ATrackingJumpIsNotAThrow()
        {
            var g = new ThrowGesture();
            var s = Motion(Chest, t => Vector3.zero, 0.2f);
            // the hand found again 0.5 m further on in one frame, then still
            var jump = Motion(Chest + new Vector3(0.1f, 0.1f, 0.5f), t => Vector3.zero, 0.3f, t0: 1f + 0.2f + 1f / 72f);
            s.AddRange(jump);
            Assert.That(Throws(g, s, out _), Is.EqualTo(0), g.Why);
        }

        [Test]
        public void ABowlsPitchIsTheBowlNotItsStrikeAngle()
        {
            var e = new TakeEvent { type = "bowlStrike", i = 2, midi = 3141, vel = 0.6f };
            Assert.That(PaintPalette.PitchOf(InstrumentKind.Bowls, e, out int key), Is.EqualTo(1f - 2f / 6f).Within(1e-5f));
            Assert.That(key, Is.EqualTo(402));
        }

        [Test]
        public void OneThrowPerHandPerRefractoryAndBothHandsApart()
        {
            var g = new ThrowGesture();
            var two = Motion(Chest, t => Flick(Vector3.forward, 2.8f)(t % 0.25f), 0.5f);   // two flicks 0.25 s apart
            Assert.That(Throws(g, two, out _), Is.EqualTo(1), "the second is too soon");
            var later = Motion(Chest, Flick(Vector3.forward, 2.8f), 0.4f, t0: 3f);
            Assert.That(Throws(g, later, out _), Is.EqualTo(1), "later: another");
            Assert.That(Throws(g, Motion(-Chest + new Vector3(0f, 2f, 0.5f), Flick(Vector3.forward, 2.8f), 0.4f, t0: 3.05f), out _, hand: 0), Is.EqualTo(1), "the other hand is its own");
        }

        [Test]
        public void TheGestureAllocatesNothing()
        {
            var g = new ThrowGesture();
            var s = Motion(Chest, Flick(Vector3.forward, 2.8f), 2f);
            var head = new Vector3(0f, 1.2f, 0f);
            void Run() { foreach (ThrowSample x in s) g.Push(1, x, head, Vector3.forward, out _, out _, out _); }
            Run();
            Assert.That(Run, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------------------------------------------ the gallery

        /// <summary>A 6 x 5 m room of four solid walls round (0, 0), 2.8 m high; a window box on the +x wall at its middle.</summary>
        static WallCanvas Room()
        {
            var c = new WallCanvas();
            void Wall(Vector3 centre, Vector3 inward, float width)
            {
                Vector3 along = Vector3.Cross(Vector3.up, inward).normalized;
                c.AddWall(centre - along * (width / 2f), along, inward, width, 2.8f, solid: true);
            }
            Wall(new Vector3(0f, 0f, 2.5f), Vector3.back, 6f);
            Wall(new Vector3(0f, 0f, -2.5f), Vector3.forward, 6f);
            Wall(new Vector3(3f, 0f, 0f), Vector3.left, 5f);
            Wall(new Vector3(-3f, 0f, 0f), Vector3.right, 5f);
            c.BlockBox(new Vector3(2.95f, 1.5f, 0f), new Vector3(0.1f, 0.9f, 0.9f), Quaternion.identity);   // the window
            c.BlockBox(new Vector3(0f, 1.4f, 2.4f), new Vector3(0.8f, 1.4f, 0.2f), Quaternion.identity);    // a chimney ahead
            c.Finish();
            return c;
        }

        [Test]
        public void FramesHangOnClearWallNearestTheViewFirstAndApart()
        {
            WallCanvas c = Room();
            var slots = new List<PaintGallery.Slot>();
            PaintGallery.FindSlots(c, new Vector3(0f, 1.2f, 0f), Vector3.forward, slots);
            Assert.That(slots.Count, Is.EqualTo(PaintGallery.Max));
            // the first ones on the wall ahead (+z), either side of the chimney
            Assert.That(slots[0].Centre.z, Is.EqualTo(2.5f).Within(0.01f), "the first faces the seat");
            Assert.That(Mathf.Abs(slots[0].Centre.x), Is.GreaterThan(0.8f + slots[0].W * 0.5f), "not over the chimney");
            Assert.That(slots[0].W, Is.EqualTo(PaintGallery.Widths[0]), "as big as the wall allows");
            for (int i = 0; i < slots.Count; i++)
            {
                PaintGallery.Slot s = slots[i];
                Assert.That(s.V, Is.InRange(PaintGallery.PictureY, PaintGallery.Heights[PaintGallery.Heights.Length - 1]));
                Assert.IsFalse(Mathf.Abs(s.Centre.x - 3f) < 0.01f && Mathf.Abs(s.Centre.z) < 0.9f + s.W * 0.5f + PaintGallery.Border, $"slot {i} not over the window");
                for (int j = 0; j < i; j++) Assert.IsFalse(PaintGallery.TooClose(s, slots[j]), "apart");
                if (i > 0)
                {
                    float a0 = Vector3.Angle(slots[i - 1].Centre - new Vector3(0f, 1.2f, 0f), Vector3.forward);
                    Assert.That(Vector3.Angle(s.Centre - new Vector3(0f, 1.2f, 0f), Vector3.forward), Is.GreaterThan(a0 - 25f), "nearest the view first");
                }
            }
            // a reserved frame keeps paint off it, and gives it back
            int free = c.FreeCells;
            PaintGallery.Slot f = slots[0];
            c.Reserve(f.Wall, f.U - 0.4f, f.V - 0.4f, f.U + 0.4f, f.V + 0.35f);
            Assert.IsFalse(c.Paintable(f.Wall, f.U, f.V));
            Assert.That(c.FreeCells, Is.LessThan(free - 40));
            var note = new PaintNote { Kind = 1, Pitch = 0.6f, Level = 0.8f };
            for (int i = 0; i < 300; i++)
            {
                note.Index = i; note.Time = i * 0.2f;
                if (!c.Place(note, 0f, 0.1f, out PaintSpot sp) || sp.Wall != f.Wall) continue;
                Assert.IsFalse(Mathf.Abs(sp.U - f.U) < 0.4f + 0.05f && Mathf.Abs(sp.V - f.V) < 0.35f, "no paint over the frame");
            }
            c.Release(f.Wall, f.U - 0.4f, f.V - 0.4f, f.U + 0.4f, f.V + 0.35f);
            Assert.That(c.FreeCells, Is.EqualTo(free));
            // a ray finds the wall it hits
            Assert.IsTrue(c.Raycast(new Vector3(0f, 1.2f, 0f), new Vector3(0.3f, 0.1f, 1f), out int wall, out float u, out float v, out float dist));
            Vector3 hit = c.Point(wall, u, v);
            Assert.That(hit.z, Is.EqualTo(2.5f).Within(1e-3f));
            Assert.That(dist, Is.InRange(2.5f, 2.8f));
            Assert.IsFalse(c.Raycast(new Vector3(0f, 1.2f, 0f), Vector3.up, out _, out _, out _, out _), "not the ceiling");
        }

        [Test]
        public void FramesKeepClearOfAnInstrumentStandingAtAWall()
        {
            WallCanvas c = Room();
            var slots = new List<PaintGallery.Slot>();
            PaintGallery.FindSlots(c, new Vector3(0f, 1.2f, 0f), Vector3.forward, slots);
            PaintGallery.Slot first = slots[0];
            // an instrument put on display right in front of the first place (a sphere on a bracket: its box)
            Vector3 box = first.Centre + first.Normal * 0.25f, half = new Vector3(0.3f, 0.3f, 0.3f);
            int free = c.FreeCells;
            c.BlockMoving(box, half, Quaternion.identity);
            Assert.That(c.FreeCells, Is.LessThan(free), "it blocks the wall behind it");
            Assert.IsFalse(c.Paintable(first.Wall, first.U, first.V), "no paint behind it either");
            PaintGallery.FindSlots(c, new Vector3(0f, 1.2f, 0f), Vector3.forward, slots);
            foreach (PaintGallery.Slot s in slots)
            {
                if (s.Wall != first.Wall) continue;
                float halfW = s.W * 0.5f + PaintGallery.Border;
                Assert.IsTrue(Mathf.Abs(s.U - first.U) > halfW + 0.3f - 0.01f, $"a frame at {s.Centre} behind the instrument at {box}");
            }
            // it moves away again: the wall is free
            c.ClearMoving();
            Assert.That(c.FreeCells, Is.EqualTo(free));
            Assert.That(c.MovingCells, Is.EqualTo(0));
            // something far from every wall (an instrument at the seat) blocks nothing
            c.BlockMoving(new Vector3(0f, 1f, 0f), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity);
            Assert.That(c.FreeCells, Is.EqualTo(free));
        }

        [Test]
        public void TheSavedGalleryParsesBack()
        {
            var list = new List<(int slot, string id)>();
            PaintGallery.Parse("0:abc;2:def;1:ghi", list);
            Assert.That(list.Count, Is.EqualTo(3));
            Assert.That(list[1], Is.EqualTo((2, "def")));
            PaintGallery.Parse("0:abc;x:bad;0:dup;9:far;:;3:abc;4:", list);
            Assert.That(list.Count, Is.EqualTo(1), "bad, duplicate and out of range parts dropped");
            PaintGallery.Parse("", list);
            Assert.That(list.Count, Is.EqualTo(0));
            PaintGallery.Parse(null, list);
            Assert.That(list.Count, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------------------------------------ the picture

        static TakeEvent At(TakeEvent e, float t) { e.t = t; return e; }

        static List<TakeEvent> Song(int seed, float seconds)
        {
            var rng = new System.Random(seed);
            var take = new List<TakeEvent>();
            for (float t = 0.1f; t < seconds; t += 0.25f)
            {
                var on = TakeEvent.PianoOn(55 + rng.Next(24), 0.4f + 0.6f * (float)rng.NextDouble()); on.t = t; take.Add(on);
                var off = TakeEvent.PianoOff(on.midi); off.t = t + 0.2f; take.Add(off);
                if (rng.Next(3) == 0) { var d = TakeEvent.Drum(rng.Next(2) == 0 ? "kick" : "snare", 0.8f, 0.2f, "head", false); d.t = t; take.Add(d); }
                if (rng.Next(5) == 0) for (int k = 0; k < 4; k++) { var h = TakeEvent.Harp(5 + k, 0.7f, 0.5f, 0.3f, "through"); h.t = t + 0.05f * k; take.Add(h); }
            }
            take.Sort((a, b) => a.t.CompareTo(b.t));
            return take;
        }

        /// <summary>A slow, quiet piano tune of held notes climbing from low to high (its second half higher).</summary>
        static List<TakeEvent> Calm(float seconds, bool held)
        {
            var take = new List<TakeEvent>();
            int n = 0;
            for (float t = 0.3f; t < seconds; t += 1.2f, n++)
            {
                int m = 48 + (int)(30f * t / seconds) + (n % 3) * 2;
                take.Add(At(TakeEvent.PianoOn(m, 0.4f), t));
                take.Add(At(TakeEvent.PianoOff(m), t + (held ? 1.1f : 0.25f)));
            }
            return take;
        }

        static float Hue(Color32 c) { Color.RGBToHSV(c, out float h, out _, out _); return h; }

        static Color32 Mean(Color32[] px, int x0, int y0, int x1, int y1)
        {
            float r = 0f, g = 0f, b = 0f; int n = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { Color32 c = px[y * GalleryPicture.Width + x]; r += c.r; g += c.g; b += c.b; n++; }
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }

        [Test]
        public void ASongsPictureIsPaintedTheSameEveryTime()
        {
            var notes = new List<PictureNote>();
            float len = GalleryPicture.Notes(Song(1, 20f), null, notes);
            Assert.That(notes.Count, Is.GreaterThan(80), "onsets only");
            foreach (PictureNote n in notes) Assert.IsTrue(n.Kind == InstrumentKind.Piano || n.Kind == InstrumentKind.Drums || n.Kind == InstrumentKind.Harp);
            foreach (PictureNote n in notes) if (n.Kind == InstrumentKind.Piano) Assert.That(n.Length, Is.EqualTo(0.2f).Within(1e-3f), "held to its release");
            var a = new Color32[GalleryPicture.Width * GalleryPicture.Height];
            var b = new Color32[a.Length];
            var c = new Color32[a.Length];
            GalleryPicture.Compose(notes, len, BrushAtlas.Shared, a);
            GalleryPicture.Compose(notes, len, BrushAtlas.Shared, b);
            for (int i = 0; i < a.Length; i++) Assert.That(a[i], Is.EqualTo(b[i]), "the same pixels");
            var other = new List<PictureNote>();
            GalleryPicture.Notes(Song(2, 20f), null, other);
            GalleryPicture.Compose(other, len, BrushAtlas.Shared, c);
            int differ = 0, painted = 0;
            Color32 paper = GalleryPicture.Paper;
            for (int i = 0; i < a.Length; i++)
            {
                if (Mathf.Abs(a[i].r - c[i].r) + Mathf.Abs(a[i].g - c[i].g) + Mathf.Abs(a[i].b - c[i].b) > 24) differ++;
                if (Mathf.Abs(a[i].r - paper.r) + Mathf.Abs(a[i].g - paper.g) + Mathf.Abs(a[i].b - paper.b) > 40) painted++;
            }
            Assert.That(differ, Is.GreaterThan(a.Length / 20), "another song, another picture");
            Assert.That(painted, Is.GreaterThan(a.Length * 3 / 4), "a whole scene, a paper margin round it");
            Assert.That(a[GalleryPicture.Width + 1], Is.EqualTo(b[GalleryPicture.Width + 1]));
            Assert.That(Mathf.Abs(a[1].r - paper.r) + Mathf.Abs(a[1].g - paper.g) + Mathf.Abs(a[1].b - paper.b), Is.LessThan(40), "the paper's edge");
            // an empty take: blank paper, no trouble
            GalleryPicture.Compose(new List<PictureNote>(), 0f, BrushAtlas.Shared, c);
            Assert.That(c[GalleryPicture.Width * 96 + 128].a, Is.EqualTo(255));
        }

        [Test]
        public void APictureIsALandscapeComposedFromTheSong()
        {
            const int W = GalleryPicture.Width, H = GalleryPicture.Height;
            var px = new Color32[W * H];
            // a lively song: a bright day; flowers from the piano, trees from the drums, birds from the harp
            var notes = new List<PictureNote>();
            float len = GalleryPicture.Notes(Song(1, 20f), null, notes);
            GalleryPicture.Compose(notes, len, BrushAtlas.Shared, px, out GalleryPicture.Scene day);
            Assert.That(day.Lively, Is.GreaterThan(0.7f));
            Assert.That(day.Day, Is.GreaterThan(0.8f), "a bright day");
            Assert.IsFalse(day.Night);
            Assert.That(day.SunY, Is.GreaterThan(H * 0.7f), "the sun high");
            Assert.That(day.Flowers, Is.InRange(20, GalleryPicture.MaxFlowers), "flowers from the piano");
            Assert.That(day.Trees, Is.InRange(5, GalleryPicture.MaxTrees + GalleryPicture.MaxTrees / 2), "trees from the drums");
            Assert.That(day.Birds, Is.InRange(5, GalleryPicture.MaxBirds), "birds from the harp");
            Assert.That(day.Butterflies + day.Mushrooms, Is.EqualTo(0), "no handpan, no hand drums");
            Assert.IsFalse(day.River || day.Lake, "no long notes, no water");
            Color32 sky = Mean(px, 20, H - 30, W - 20, H - 10), meadow = Mean(px, 20, 6, W - 20, 14);
            Assert.That(sky.b, Is.GreaterThan(sky.r + 30), "a blue sky");
            Assert.That(meadow.g, Is.GreaterThan(meadow.b + 20), "a green meadow");
            Assert.That(meadow.g, Is.GreaterThan(meadow.r), "a green meadow");
            // the hills follow the melody: a tune climbing from low to high lifts them to the right
            var calm = new List<PictureNote>();
            float calmLen = GalleryPicture.Notes(Calm(36f, held: false), null, calm);
            GalleryPicture.Compose(calm, calmLen, BrushAtlas.Shared, px, out GalleryPicture.Scene dusk);
            float left = 0f, right = 0f;
            for (int x = 10; x < 60; x++) { left += dusk.Far[x]; right += dusk.Far[W - 1 - x]; }
            Assert.That(right, Is.GreaterThan(left + 50f * 12f), "higher hills where the melody is higher");
            Assert.That(dusk.Lively, Is.LessThan(0.3f));
            Assert.That(dusk.Day, Is.LessThan(0.2f), "a calm song: dawn, dusk or moonlight");
            Assert.That(dusk.Night || dusk.SunY < H * 0.75f, "a low sun (or the moon)");
            Color32 duskSky = Mean(px, 20, H / 2, W - 20, H / 2 + 12);
            if (!dusk.Night) Assert.That(duskSky.r, Is.GreaterThan(duskSky.b), "a warm glow low in the sky");
            Assert.IsFalse(dusk.River || dusk.Lake, "short notes");
            // held notes: water in the valley
            var held = new List<PictureNote>();
            float heldLen = GalleryPicture.Notes(Calm(36f, held: true), null, held);
            GalleryPicture.Compose(held, heldLen, BrushAtlas.Shared, px, out GalleryPicture.Scene water);
            Assert.That(water.LongShare, Is.GreaterThan(0.5f));
            Assert.IsTrue(water.Lake || water.River, "long notes: a lake or a river");
            // the other instruments' motifs
            var mixed = new List<TakeEvent>();
            for (int i = 0; i < 12; i++)
            {
                mixed.Add(At(new TakeEvent { type = "handpan", i = 1 + i % 8, vel = 0.7f }, 0.5f + i));
                mixed.Add(At(new TakeEvent { type = "handDrum", kind = "conga", zone = "open", vel = 0.8f }, 0.7f + i));
                mixed.Add(At(new TakeEvent { type = "bowlStrike", i = i % 3, vel = 0.7f }, 0.9f + i));
            }
            var mx = new List<PictureNote>();
            foreach (TakeEvent e in mixed)
            {
                InstrumentKind k = e.type == "handpan" ? InstrumentKind.Handpan : e.type == "handDrum" ? InstrumentKind.HandDrums : InstrumentKind.Bowls;
                mx.Add(new PictureNote { Kind = k, Pitch = PaintPalette.PitchOf(k, e, out int key), Key = key, Level = 0.7f, Time = e.t, Length = k == InstrumentKind.Bowls ? GalleryPicture.BowlRing : 0f });
            }
            mx.Sort((p, q) => p.Time.CompareTo(q.Time));
            GalleryPicture.Compose(mx, 13f, BrushAtlas.Shared, px, out GalleryPicture.Scene others);
            Assert.That(others.Butterflies, Is.GreaterThan(5), "butterflies from the handpan");
            Assert.That(others.Mushrooms, Is.GreaterThan(5), "mushrooms from the hand drums");
            Assert.That(others.Clouds, Is.InRange(3, GalleryPicture.MaxClouds), "clouds from the bowls");
            Assert.IsTrue(others.River || others.Lake, "ringing bowls: water");
        }
    }
}
