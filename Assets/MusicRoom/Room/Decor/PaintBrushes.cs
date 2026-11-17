// The song paints a nature picture (round 4, 2026-11-16: "make paintings of flowers birds nature scenery. or may be paint
// few nature sceneires"; before it, round 2: each instrument's own brush). Pure 2D maths, no Unity objects but Vector2 /
// Color: a note becomes a few textured quads (BrushQuad) of watercolour motifs on a flat canvas (metres: x along a wall,
// y up), with how each one comes alive (Mode). Each instrument keeps its own pigment and its own character:
//   - Piano: the melody grows ONE flowering vine (RibbonPoint: a green stem rising and dipping with the pitch, curving
//     smoothly from note to note, drawn as it grows) and every melody note blooms a flower on a short stalk off it
//     (Bloom: a five-petal blossom or a tulip in the piano's rose, a golden heart, now and then a leaf on the vine);
//     higher notes bloom higher up. A phrase's end tapers the vine (RibbonEnd); chord notes add buds (ChordBud).
//   - Drums: the ground: a strong kick plants an autumn tree (trunk and crown in the drums' burnt orange), a soft one a
//     bush; the snare a tuft of grass with pebbles, the hats small sprigs, the ride pebbles; the toms drop leaves that
//     drift down the wall (Fall) and a crash blows a gust of them.
//   - Harp: birds in flight: a pluck sets a bird in the sky, every note of a glissando that follows adds the next bird of a
//     flock swooping along an arc, banking as it turns.
//   - Bowls: soft clouds swelling slowly; handpan: butterflies with a dotted flight; hand drums: mushrooms (a slap: one
//     more); marimba: slim trees.
// Height says where on the wall a motif belongs (the ground low, the flowers in the middle, birds and clouds high), so the
// walls fill up as one scene. The quads are relative to an anchor the caller placed (WallCanvas.Place with Reach as the
// radius: every quad of a mark lies within Reach x 1.42 of it, which the caller keeps on clear wall; falling leaves drift
// further down later, which the caller shortens to clear wall), or, for the vine and the flock, in the canvas's own
// coordinates. The same brushes paint the walls (Decor/WallPaint.cs) and the gallery's landscapes
// (Decor/GalleryPicture.cs). Deterministic: randomness only from the note's hash. Allocation-free: a fixed output
// buffer, cleared by the caller.
using MusicRoom.Core;
using UnityEngine;
using MusicRoom.Room.Textures;
using static MusicRoom.Room.Textures.PaintArt;

namespace MusicRoom.Room.Decor
{
    /// <summary>How a quad comes alive (the shader's mode): Grow from StartScale to full size over Dur (alpha toward EndAlpha
    /// as it grows); Fall: the whole quad drifts down by Length over Dur, easing to a stop (a leaf, a petal); Wipe: drawn from
    /// its start edge (A, D) to its end edge (B, C) over Dur (the vine, a stalk); Band: a Wipe whose hue turns across it by
    /// Hue (a bird's path).</summary>
    public enum BrushMode : byte { Grow = 0, Fall = 1, Wipe = 2, Band = 3 }

    /// <summary>One textured quad of a mark. Corners A (uv 0,0), B (1,0), C (1,1), D (0,1) on the canvas (m); Uv: the
    /// cell's coordinates at each corner (a tiling cell's u runs on in repeats). Delay: seconds after the note it starts.</summary>
    public struct BrushQuad
    {
        public Vector2 A, B, C, D;
        public Vector2 UvA, UvB, UvC, UvD;
        public int Cell;
        public BrushMode Mode;
        public Color Colour;
        public float Alpha, Delay, Dur;
        /// <summary>Grow: StartScale; Fall: Length (m); Band: Hue (turns across the band).</summary>
        public float P1;
        /// <summary>Grow: EndAlpha (1 for a mark that stays as strong).</summary>
        public float P2;

        public Vector2 Centre => (A + B + C + D) * 0.25f;
    }

    /// <summary>A note for the brushes: instrument, pitch 0..1, loudness 0..1, seconds since the painting started, its key
    /// (PaintPalette.PitchOf: midi, string + 100, drum piece 1..9 ...), its hash (WallCanvas.Hash) and an accent (a hand
    /// drum's slap).</summary>
    public struct BrushNote
    {
        public InstrumentKind Kind;
        public float Pitch, Level, Time;
        public int Key;
        public uint Hash;
        public bool Accent;
    }

    public sealed class PaintBrushes
    {
        public const int Capacity = 64;
        /// <summary>Strength: soft notes AlphaMin, loud AlphaMax.</summary>
        public const float AlphaMin = 0.74f, AlphaMax = 0.95f;
        /// <summary>The vine: half width soft .. loud (m), how long a repeat of its texture is (m), the time to grow a piece at
        /// most (s), a phrase ends after PhraseGap (s) without a melody note, the taper's length (m).</summary>
        public const float RibbonMin = 0.007f, RibbonMax = 0.0145f, RibbonTile = 0.26f, RibbonDraw = 0.32f, PhraseGap = 0.9f, Taper = 0.12f;
        /// <summary>Notes of the piano this close together (s) sound as one chord: the highest carries the vine.</summary>
        public const float ChordWindow = 0.045f;
        /// <summary>The harp's flock: its arc's radius soft .. loud (m), the first bird's place on it and each glissando note's
        /// step (radians), the most time between glissando notes (s), a bird's half size soft .. loud (m).</summary>
        public const float ArcRMin = 0.34f, ArcRMax = 0.46f, ArcFirst = 0.25f, ArcStep = 0.5f, GlissGap = 0.2f,
            BirdMin = 0.065f, BirdMax = 0.09f;
        /// <summary>Falling leaves and petals: how long they drift (s) and when they let go (s).</summary>
        public const float FallSeconds = 1.8f, FallAfter = 0.35f;
        /// <summary>A cloud swells this long (s) from this size.</summary>
        public const float CloudSeconds = 2.6f, CloudStart = 0.72f;
        /// <summary>A motif's ordinary bloom (s, from 30 %).</summary>
        public const float GrowSeconds = 0.5f, GrowStart = 0.3f;

        /// <summary>The colours nature adds round a motif (linear): leaves and stems, a flower's golden heart, bark, a
        /// mushroom's pale stem.</summary>
        public static readonly Color Foliage = Lin(0.34f, 0.54f, 0.22f), Stem = Lin(0.27f, 0.45f, 0.18f), Heart = Lin(0.97f, 0.78f, 0.22f),
            Bark = Lin(0.36f, 0.24f, 0.15f), Cream = Lin(0.93f, 0.87f, 0.74f), Stone = Lin(0.55f, 0.52f, 0.48f);

        public readonly BrushQuad[] Out = new BrushQuad[Capacity];
        public int Count;

        // the vine
        bool _ribbon;
        Vector2 _rp, _rdir, _rEdge0, _rEdge1;   // last point, its direction, its edge pair (below, above)
        float _rw, _rt, _ru;                     // last half width, its time, the texture's u so far
        Color _rc;
        float _ra;
        // the harp's flock
        bool _arc;
        Vector2 _ac;
        float _ar, _ath, _at, _as;
        int _aKey, _aBirds;

        public bool RibbonOpen => _ribbon;
        public float RibbonTime => _rt;
        public Vector2 RibbonPoint0 => _rp;
        public bool ArcOpen => _arc;
        public float ArcAngle => _ath;
        public float ArcRadius => _ar;
        public Vector2 ArcCentre => _ac;

        public void Clear() => Count = 0;

        /// <summary>A new painting: the vine and the flock are let go (nothing drawn).</summary>
        public void Reset()
        {
            _ribbon = false;
            _arc = false;
            Count = 0;
        }

        public static float AlphaOf(float level) => Mathf.Lerp(AlphaMin, AlphaMax, Mathf.Clamp01(level));

        static Color Lin(float r, float g, float b) => new Color(GalleryPicture.ToLinear(r), GalleryPicture.ToLinear(g), GalleryPicture.ToLinear(b), 1f);

        // ---------------------------------------------------------------- where a motif belongs, how far it reaches

        /// <summary>Where on the wall a note's motif belongs, 0 the ground .. 1 the sky (the canvas's pitch to height): trees,
        /// grass, pebbles and mushrooms low, falling leaves and butterflies in the middle, birds and clouds high; the melody's
        /// own pitch for the piano. Within its band a higher note still goes higher.</summary>
        public static float Height(in BrushNote n)
        {
            float p = Mathf.Clamp01(n.Pitch);
            switch (n.Kind)
            {
                case InstrumentKind.Drums:
                    switch (n.Key)
                    {
                        case 1: return 0.05f;                     // a tree on the ground
                        case 5: case 6: case 7: return 0f;        // grass, sprigs, pebbles at the foot
                        case 8: return 0.55f;                     // a gust of leaves high up
                        default: return 0.3f + 0.15f * p;         // leaves falling
                    }
                case InstrumentKind.HandDrums: return 0f;
                case InstrumentKind.Marimba: return 0.04f + 0.08f * p;
                case InstrumentKind.Harp: return 0.72f + 0.28f * p;
                case InstrumentKind.Bowls: return 0.86f + 0.14f * p;
                case InstrumentKind.Handpan: return 0.38f + 0.4f * p;
                default: return p;
            }
        }

        /// <summary>The radius a note's mark needs round its anchor (m): the caller places the anchor where a disc this big
        /// lies on clear wall, then calls Mark with scale = the radius it got / this.</summary>
        public static float Reach(in BrushNote n)
        {
            float l = Mathf.Clamp01(n.Level);
            switch (n.Kind)
            {
                case InstrumentKind.Drums: return DrumSize(n.Key, l);
                case InstrumentKind.Harp: return Mathf.Lerp(ArcRMin, ArcRMax, l) + Mathf.Lerp(BirdMin, BirdMax, l);
                case InstrumentKind.Bowls: return Mathf.Lerp(0.24f, 0.34f, l);
                case InstrumentKind.Handpan: return Mathf.Lerp(0.11f, 0.16f, l);
                case InstrumentKind.HandDrums: return Mathf.Lerp(0.085f, 0.115f, l) * (n.Accent ? 1.25f : 1f);
                case InstrumentKind.Marimba: return Mathf.Lerp(0.14f, 0.19f, l);
                default: return Mathf.Lerp(0.06f, 0.1f, l);
            }
        }

        /// <summary>A drum piece's motif radius (m): the kick's tree biggest, a crash's gust wide.</summary>
        public static float DrumSize(int piece, float level)
        {
            switch (piece)
            {
                case 1: return Mathf.Lerp(0.17f, 0.25f, level);   // kick: a tree (a bush when soft)
                case 5: return Mathf.Lerp(0.1f, 0.14f, level);    // snare: grass and pebbles
                case 6: return Mathf.Lerp(0.07f, 0.1f, level);    // hats: sprigs
                case 7: return Mathf.Lerp(0.08f, 0.11f, level);   // ride: pebbles
                case 8: return Mathf.Lerp(0.18f, 0.24f, level);   // crash: a gust of leaves
                default: return Mathf.Lerp(0.11f, 0.15f, level);  // toms: falling leaves
            }
        }

        /// <summary>The kick's strength that plants a tree (softer: a bush).</summary>
        public const float TreeLevel = 0.5f;

        // ---------------------------------------------------------------- the marks round an anchor

        /// <summary>The note's motif round <paramref name="at"/> (the canvas, m), every size times <paramref name="scale"/>.
        /// The piano's vine and the harp's flock have their own calls (they go on across notes); here the piano gives a bud
        /// (a chord note away from the vine) and the harp one bird.</summary>
        public void Mark(in BrushNote n, Vector2 at, float scale, Color colour)
        {
            float l = Mathf.Clamp01(n.Level), alpha = AlphaOf(l);
            uint h = n.Hash;
            switch (n.Kind)
            {
                case InstrumentKind.Drums: Drum(n, at, scale, colour, alpha); break;
                case InstrumentKind.Bowls:
                {
                    // a soft cloud in the bowls' lavender, swelling slowly
                    float R = Mathf.Lerp(0.24f, 0.34f, l) * scale;
                    Cloud(at, R * 0.95f, R * 0.74f, (Rand(h, 6) - 0.5f) * 0.12f, Color.Lerp(colour, Color.white, 0.42f), alpha * 0.85f, 0f);
                    break;
                }
                case InstrumentKind.Handpan:
                {
                    // a butterfly, its dotted flight behind it; a loud note brings a second, smaller one
                    float R = Mathf.Lerp(0.11f, 0.16f, l) * scale;
                    float tilt = (Rand(h, 6) - 0.5f) * 0.7f;
                    float side = Rand(h, 7) > 0.5f ? 1f : -1f;
                    Butterfly(at, R * 0.72f, tilt, colour, alpha, 0f);
                    for (int i = 0; i < 4; i++)
                    {
                        float t = (i + 1) / 5f;
                        var p = at + new Vector2(-side * R * (0.35f + 0.6f * t), -R * (0.15f + 0.5f * t) + R * 0.2f * Mathf.Sin(t * 7f + Rand(h, 8) * 6f));
                        AddRect(p, 0.012f * scale, 0.012f * scale, 0f, Droplet, colour, alpha * 0.55f, BrushMode.Grow, 0.05f * i, 0.2f, 0.4f, 1f);
                    }
                    if (l > 0.7f) Butterfly(at + new Vector2(side * R * 0.55f, R * 0.42f), R * 0.46f, -tilt * 1.5f, Color.Lerp(colour, Heart, 0.3f), alpha, 0.15f);
                    break;
                }
                case InstrumentKind.HandDrums:
                {
                    // mushrooms in the hand drums' rust (a slap: a third, and a blade of grass)
                    float R = Reach(n) * scale;
                    float lean = (Rand(h, 6) - 0.5f) * 0.25f;
                    Mushroom(at + new Vector2(-0.22f * R, -0.55f * R), 1.25f * R, lean, colour, alpha, 0f);
                    Mushroom(at + new Vector2(0.5f * R, -0.55f * R), 0.72f * R, -lean, Color.Lerp(colour, Color.white, 0.18f), alpha, 0.1f);
                    if (n.Accent)
                    {
                        Mushroom(at + new Vector2(-0.75f * R, -0.55f * R), 0.55f * R, lean * 2f, colour, alpha, 0.18f);
                        Grass(at + new Vector2(0.15f * R, -0.62f * R), 0.75f * R, 0f, Foliage, alpha * 0.85f, 0.05f);
                    }
                    break;
                }
                case InstrumentKind.Marimba:
                {
                    // slim trees in the marimba's sap green (a loud note: a little one beside)
                    float R = Mathf.Lerp(0.14f, 0.19f, l) * scale;
                    bool two = l > 0.72f;
                    float x = two ? -0.22f * R : 0f;
                    Tree(at + new Vector2(x, -0.85f * R), (two ? 1.7f : 1.85f) * R, colour, Color.Lerp(Bark, colour, 0.2f), alpha, h, 0f, true);
                    if (two) Tree(at + new Vector2(0.6f * R, -0.85f * R), 1.05f * R, Color.Lerp(colour, Foliage, 0.4f), Bark, alpha, h * 31u, 0.15f, true);
                    break;
                }
                case InstrumentKind.Piano:
                    LoneFlower(n, at, Reach(n) * scale, colour, alpha);
                    break;
                case InstrumentKind.Harp:
                {
                    float s = Mathf.Lerp(BirdMin, BirdMax, l) * scale;
                    Bird(at, s, (Rand(h, 6) - 0.5f) * 0.5f, Deep(colour), alpha, 0f);
                    break;
                }
                default:
                {
                    float r = Mathf.Lerp(0.05f, 0.08f, l) * scale;
                    Blossom(at, r, Rand(h, 6) * 1.2f, colour, alpha, 0f);
                    break;
                }
            }
        }

        void Drum(in BrushNote n, Vector2 at, float scale, Color colour, float alpha)
        {
            float l = Mathf.Clamp01(n.Level);
            uint h = n.Hash;
            int piece = n.Key;
            float R = DrumSize(piece, l) * scale;
            switch (piece)
            {
                case 1:
                    if (l >= TreeLevel)
                    {
                        // an autumn tree: a trunk, a crown in the drums' colour
                        Tree(at + new Vector2(0f, -0.95f * R), 1.9f * R, colour, Color.Lerp(Bark, colour, 0.15f), alpha, h, 0f, false);
                    }
                    else
                    {
                        // a bush: three clumps on the ground
                        Color c2 = Color.Lerp(colour, Foliage, 0.35f);
                        AddRect(at + new Vector2(-0.4f * R, -0.2f * R), 0.55f * R, 0.5f * R, 0f, Crown, c2, alpha, BrushMode.Grow, 0f, GrowSeconds, GrowStart, 1f);
                        AddRect(at + new Vector2(0.38f * R, -0.25f * R), 0.5f * R, 0.45f * R, 0f, Crown, Darker(c2, 0.85f), alpha, BrushMode.Grow, 0.06f, GrowSeconds, GrowStart, 1f);
                        AddRect(at + new Vector2(0f, 0.05f * R), 0.62f * R, 0.55f * R, 0f, Crown, colour, alpha, BrushMode.Grow, 0.1f, GrowSeconds, GrowStart, 1f);
                    }
                    break;
                case 5:
                {
                    // a tuft of grass, two pebbles at its foot
                    Vector2 foot = at + new Vector2(0f, -0.62f * R);
                    Grass(foot, 1.45f * R, (Rand(h, 6) - 0.5f) * 0.25f, Color.Lerp(colour, Foliage, 0.45f), alpha, 0f);
                    Pebble(foot + new Vector2(-0.55f * R, 0.05f * R), 0.22f * R, Color.Lerp(colour, Stone, 0.6f), alpha, 0.1f, h, 1);
                    Pebble(foot + new Vector2(0.6f * R, 0.03f * R), 0.16f * R, Color.Lerp(colour, Stone, 0.4f), alpha, 0.15f, h, 2);
                    break;
                }
                case 6:
                {
                    // two small sprigs
                    Vector2 foot = at + new Vector2(0f, -0.6f * R);
                    Grass(foot + new Vector2(-0.35f * R, 0f), 1.1f * R, -0.2f, Color.Lerp(colour, Foliage, 0.55f), alpha * 0.9f, 0f);
                    Grass(foot + new Vector2(0.4f * R, 0f), 0.85f * R, 0.25f, Color.Lerp(colour, Foliage, 0.35f), alpha * 0.9f, 0.08f);
                    break;
                }
                case 7:
                {
                    // pebbles
                    for (int i = 0; i < 3; i++)
                    {
                        var p = at + new Vector2((i - 1) * 0.55f * R + 0.15f * R * (Rand(h, 20 + i) - 0.5f), -0.3f * R + 0.15f * R * (Rand(h, 25 + i) - 0.5f));
                        Pebble(p, (0.3f - 0.06f * i) * R, Color.Lerp(colour, Stone, 0.35f + 0.2f * i), alpha, 0.06f * i, h, 30 + i);
                    }
                    break;
                }
                default:
                {
                    // leaves letting go and drifting down: a crash blows a gust of five, a tom drops two or three
                    bool crash = piece == 8;
                    int leaves = crash ? 5 : 2 + (l > 0.6f ? 1 : 0);
                    for (int i = 0; i < leaves; i++)
                    {
                        float a = Rand(h, 40 + i) * 6.2832f, d = R * (crash ? 0.25f + 0.5f * Rand(h, 45 + i) : 0.15f + 0.35f * Rand(h, 45 + i));
                        Vector2 c = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                        float len = R * (crash ? 0.45f : 0.75f) * (0.8f + 0.35f * Rand(h, 50 + i));
                        Color lc = i % 2 == 0 ? colour : Color.Lerp(colour, Foliage, 0.3f + 0.3f * Rand(h, 55 + i));
                        float turn = Rand(h, 60 + i) * 6.2832f;
                        AddRect(c, len * 0.5f, len * 0.5f, turn, Leaf, lc, alpha, BrushMode.Grow, 0.04f * i, 0.25f, 0.4f, 1f);
                        if (i < (crash ? 3 : 2))
                        {
                            // this one lets go and drifts down
                            ref BrushQuad q = ref Out[Count - 1];
                            q.Mode = BrushMode.Fall;
                            q.P1 = R * (0.5f + 0.8f * Rand(h, 65 + i));
                            q.Delay = FallAfter + 0.12f * i;
                            q.Dur = FallSeconds * (0.8f + 0.4f * Rand(h, 70 + i));
                        }
                    }
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- motifs (the walls' and the gallery's)

        /// <summary>A tree standing at <paramref name="foot"/>, h tall (m): a trunk, its crown (slim: a tall narrow one, else a
        /// round one with two side clumps a shade apart).</summary>
        public void Tree(Vector2 foot, float h, Color crown, Color trunk, float alpha, uint hash, float delay, bool slim)
        {
            float tilt = (Rand(hash, 11) - 0.5f) * 0.08f;
            float th = 0.36f * h;
            AddRect(foot + new Vector2(0f, 0.88f * th), th * (slim ? 0.8f : 1f), th, tilt, Trunk, trunk, Mathf.Min(1f, alpha * 1.05f), BrushMode.Grow, delay, 0.35f, 0.5f, 1f);
            if (slim)
            {
                AddRect(foot + new Vector2(0f, 0.6f * h), 0.27f * h, 0.5f * h, tilt, Crown, crown, alpha, BrushMode.Grow, delay + 0.12f, GrowSeconds, GrowStart, 1f);
                return;
            }
            float side = Rand(hash, 12) > 0.5f ? 1f : -1f;
            AddRect(foot + new Vector2(-side * 0.25f * h, 0.52f * h), 0.22f * h, 0.22f * h, Rand(hash, 13) * 6.2832f, Crown, Darker(crown, 0.82f), alpha, BrushMode.Grow, delay + 0.1f, GrowSeconds, GrowStart, 1f);
            AddRect(foot + new Vector2(0f, 0.66f * h), 0.4f * h, 0.4f * h, tilt, Crown, crown, alpha, BrushMode.Grow, delay + 0.14f, GrowSeconds, GrowStart, 1f);
            AddRect(foot + new Vector2(side * 0.25f * h, 0.54f * h), 0.2f * h, 0.2f * h, Rand(hash, 14) * 6.2832f, Crown, Color.Lerp(crown, Color.white, 0.18f), alpha * 0.9f, BrushMode.Grow, delay + 0.2f, GrowSeconds, GrowStart, 1f);
        }

        /// <summary>A five-petal flower head of radius r round c, turned by angle, its golden heart.</summary>
        public void Blossom(Vector2 c, float r, float angle, Color petals, float alpha, float delay)
        {
            AddRect(c, r, r, angle, PaintArt.Blossom, petals, alpha, BrushMode.Grow, delay, 0.55f, 0.15f, 1f);
            AddRect(c, r * 0.3f, r * 0.3f, angle, Bead, Heart, Mathf.Min(1f, alpha * 1.1f), BrushMode.Grow, delay + 0.25f, 0.3f, 0.3f, 1f);
        }

        /// <summary>A tulip of radius r round c, leaning by angle.</summary>
        public void Tulip(Vector2 c, float r, float angle, Color petals, float alpha, float delay) =>
            AddRect(c, r, r, angle, Bud, petals, alpha, BrushMode.Grow, delay, 0.5f, 0.2f, 1f);

        /// <summary>A leaf from its stalk at <paramref name="from"/>, len long, pointing along angle (radians from +x).</summary>
        public void LeafAt(Vector2 from, float len, float angle, Color colour, float alpha, float delay)
        {
            float hy = len / 1.76f;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            AddRect(from + dir * (0.9f * hy), hy, hy, angle - 1.5708f, Leaf, colour, alpha, BrushMode.Grow, delay, 0.35f, 0.3f, 1f);
        }

        /// <summary>A thin stalk from a to b (half width w), growing from a over dur.</summary>
        public void Stalk(Vector2 a, Vector2 b, float w, Color colour, float alpha, float delay, float dur)
        {
            if (Count >= Capacity) return;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 p = Perp(d / len) * w;
            float u1 = len / RibbonTile;
            Out[Count++] = new BrushQuad
            {
                A = a - p, B = b - p, C = b + p, D = a + p,
                UvA = new Vector2(0f, 0f), UvB = new Vector2(u1, 0f), UvC = new Vector2(u1, 1f), UvD = new Vector2(0f, 1f),
                Cell = Ribbon, Mode = BrushMode.Wipe, Colour = colour, Alpha = alpha, Delay = delay, Dur = dur,
            };
        }

        /// <summary>A bird of half size s at c, banking by angle.</summary>
        public void Bird(Vector2 c, float s, float angle, Color colour, float alpha, float delay) =>
            AddRect(c, s, s, angle, PaintArt.Bird, colour, alpha, BrushMode.Grow, delay, 0.3f, 0.4f, 1f);

        /// <summary>A butterfly of half size s at c, tilted by angle.</summary>
        public void Butterfly(Vector2 c, float s, float angle, Color colour, float alpha, float delay) =>
            AddRect(c, s, s, angle, PaintArt.Butterfly, colour, alpha, BrushMode.Grow, delay, 0.4f, 0.25f, 1f);

        /// <summary>A cloud of half sizes hx, hy at c, swelling slowly.</summary>
        public void Cloud(Vector2 c, float hx, float hy, float angle, Color colour, float alpha, float delay) =>
            AddRect(c, hx, hy, angle, PaintArt.Cloud, colour, alpha, BrushMode.Grow, delay, CloudSeconds, CloudStart, 1f);

        /// <summary>A tuft of grass from its foot, h tall, leaning by angle.</summary>
        public void Grass(Vector2 foot, float h, float angle, Color colour, float alpha, float delay)
        {
            float hy = h / 1.7f;
            var up = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            AddRect(foot + up * (0.88f * hy), hy, hy, angle, PaintArt.Grass, colour, alpha, BrushMode.Grow, delay, 0.4f, 0.4f, 1f);
        }

        /// <summary>A pebble of radius r at c, lying flat.</summary>
        public void Pebble(Vector2 c, float r, Color colour, float alpha, float delay, uint hash, int channel) =>
            AddRect(c, r, r * (0.55f + 0.15f * Rand(hash, channel)), (Rand(hash, channel + 7) - 0.5f) * 0.3f, Blot, colour, alpha * 0.9f, BrushMode.Grow, delay, 0.25f, 0.5f, 1f);

        /// <summary>A mushroom h tall from its foot: a pale stem, a cap in the colour, leaning by angle.</summary>
        public void Mushroom(Vector2 foot, float h, float angle, Color cap, float alpha, float delay)
        {
            var up = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle));
            AddRect(foot + up * (0.3f * h), 0.1f * h, 0.32f * h, angle, Blot, Cream, alpha, BrushMode.Grow, delay, 0.3f, 0.4f, 1f);
            AddRect(foot + up * (0.66f * h), 0.48f * h, 0.48f * h, angle, Cap, cap, alpha, BrushMode.Grow, delay + 0.1f, 0.35f, 0.3f, 1f);
        }

        /// <summary>Where a chord note's bud hangs off the vine point: on a short stalk down and to one side (by the hash).</summary>
        public static void ChordSpot(in BrushNote n, Vector2 vine, float scale, out Vector2 head, out float radius)
        {
            uint h = n.Hash;
            radius = Mathf.Lerp(0.032f, 0.048f, Mathf.Clamp01(n.Level)) * scale;
            float lean = (Rand(h, 6) > 0.5f ? 1f : -1f) * (0.35f + 0.5f * Rand(h, 7));
            head = vine + new Vector2(Mathf.Sin(lean), -Mathf.Cos(lean)) * (Mathf.Lerp(0.05f, 0.08f, Rand(h, 8)) * scale + radius);
        }

        /// <summary>A chord note: a bud hanging from the vine point on a short stalk (a tulip turned down, like a bell).</summary>
        public void ChordBud(in BrushNote n, Vector2 vine, Vector2 head, float radius, Color colour, float alpha, float delay)
        {
            Vector2 d = head - vine;
            float len = d.magnitude;
            Vector2 dir = len > 1e-4f ? d / len : new Vector2(0f, -1f);
            Stalk(vine, head - dir * (radius * 0.55f), Mathf.Max(0.003f, radius * 0.1f), Stem, alpha, delay, 0.15f);
            Tulip(head, radius, Mathf.Atan2(dir.y, dir.x) - 1.5708f, colour, alpha, delay + 0.12f);
        }

        /// <summary>A small flower standing on its own (a chord note with no vine near): a stalk up to a blossom round at.</summary>
        public void LoneFlower(in BrushNote n, Vector2 at, float reach, Color colour, float alpha)
        {
            float lean = (Rand(n.Hash, 6) - 0.5f) * 0.5f;
            var up = new Vector2(-Mathf.Sin(lean), Mathf.Cos(lean));
            Vector2 head = at + up * (0.35f * reach);
            Stalk(at - up * (0.9f * reach), head - up * (0.25f * reach), Mathf.Max(0.003f, reach * 0.05f), Stem, alpha, 0f, 0.15f);
            Blossom(head, 0.5f * reach, lean + Rand(n.Hash, 7), colour, alpha, 0.12f);
        }

        /// <summary>Thrown paint landing (the player's flick): a burst of petals round a big blossom, a few leaves, some petals
        /// drifting down.</summary>
        public void ThrowSplat(Vector2 at, float size, uint h, Color colour)
        {
            float alpha = 0.86f;
            Blossom(at, size * 0.55f, Rand(h, 6) * 6.2832f, colour, alpha, 0f);
            const int petals = 9;
            for (int i = 0; i < petals; i++)
            {
                float a = i * 6.2832f / petals + 0.5f * (Rand(h, 10 + i) - 0.5f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float d = size * (0.72f + 0.45f * Rand(h, 20 + i)), s = size * (0.17f + 0.08f * Rand(h, 30 + i));
                Color pc = i % 3 == 0 ? Color.Lerp(colour, Color.white, 0.3f) : colour;
                AddRect(at + dir * d, s * 0.62f, s, a - 1.5708f + 0.6f * (Rand(h, 35 + i) - 0.5f), Petal, pc, alpha, BrushMode.Grow, 0.03f * i, 0.22f, 0.2f, 1f);
                if (i % 3 == 1)
                {
                    ref BrushQuad q = ref Out[Count - 1];
                    q.Mode = BrushMode.Fall;
                    q.P1 = 0.12f + 0.15f * Rand(h, 40 + i);
                    q.Delay = FallAfter + 0.1f * i;
                    q.Dur = FallSeconds;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                float a = (i + 0.5f) * 2.0944f + 0.4f * (Rand(h, 50 + i) - 0.5f);
                LeafAt(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (size * 0.4f), size * 0.55f, a, Foliage, alpha, 0.08f + 0.04f * i);
            }
        }

        // ---------------------------------------------------------------- the piano's vine and its flowers

        /// <summary>The vine on to p (the canvas, m) for a melody note: a smooth curve from the last point (the previous
        /// direction carried on), its width easing to this note's (loudness), grown over <paramref name="drawSeconds"/>
        /// (Wipe pieces one after the other). A first point starts a stem with a short sprout.</summary>
        public void RibbonPoint(Vector2 p, float level, float time, float drawSeconds, Color colour, float alpha)
        {
            float w = Mathf.Lerp(RibbonMin, RibbonMax, Mathf.Clamp01(level));
            drawSeconds = Mathf.Clamp(drawSeconds, 0.06f, RibbonDraw);
            if (!_ribbon)
            {
                // the sprout: a short piece from a little behind, from a thin start
                _ribbon = true;
                _rdir = new Vector2(1f, 0f);
                Vector2 s = p - _rdir * 0.05f;
                _rp = s;
                _rw = w * 0.25f;
                _ru = 0f;
                _rEdge0 = s - Perp(_rdir) * _rw;
                _rEdge1 = s + Perp(_rdir) * _rw;
                _rc = colour; _ra = alpha;
                Piece(s, p, _rdir, _rdir, _rw, w, 0f, drawSeconds * 0.6f, 2);
            }
            else
            {
                Vector2 d = p - _rp;
                float len = d.magnitude;
                if (len < 1e-4f) { _rt = time; return; }
                Vector2 chord = d / len;
                Vector2 dir1 = (chord + new Vector2(0.9f, 0f)).normalized;   // a growing stem rounds its turns
                int pieces = Mathf.Clamp(Mathf.CeilToInt(len / 0.045f), 1, 5);
                _rc = Color.Lerp(_rc, colour, 0.65f); _ra = alpha;
                Piece(_rp, p, _rdir, dir1, _rw, w, 0f, drawSeconds, pieces);
            }
            _rt = time;
        }

        /// <summary>The phrase ended: the vine tapers to a point a little further on and is let go.</summary>
        public void RibbonEnd(float drawSeconds = 0.18f)
        {
            if (!_ribbon) return;
            Vector2 end = _rp + _rdir * Taper + new Vector2(0f, 0.02f);
            Piece(_rp, end, _rdir, (_rdir + new Vector2(0f, 0.6f)).normalized, _rw, 0.0015f, 0f, drawSeconds, 2);
            _ribbon = false;
        }

        /// <summary>The vine cannot go on (a wall's end, something in the way): let it go as it is (no taper).</summary>
        public void RibbonBreak() => _ribbon = false;

        /// <summary>Where a melody note's flower goes off the vine point: on a short stalk leaning left or right (by the hash),
        /// its radius by loudness.</summary>
        public static void BloomSpot(in BrushNote n, Vector2 vine, float scale, out Vector2 head, out float radius)
        {
            uint h = n.Hash;
            float l = Mathf.Clamp01(n.Level);
            float lean = (Rand(h, 15) > 0.5f ? 1f : -1f) * (0.25f + 0.45f * Rand(h, 16));
            float len = Mathf.Lerp(0.055f, 0.1f, Rand(h, 17)) * scale;
            radius = Mathf.Lerp(0.048f, 0.082f, l) * scale;
            head = vine + new Vector2(-Mathf.Sin(lean), Mathf.Cos(lean)) * (len + radius * 0.6f);
        }

        /// <summary>A melody note's flower: a stalk from the vine point out to its head, then the flower opens (a blossom, or a
        /// tulip now and then), its leaf on the vine every other note; all after delay (the vine reaching the point).</summary>
        public void Bloom(in BrushNote n, Vector2 vine, Vector2 head, float radius, Color colour, float alpha, float delay)
        {
            uint h = n.Hash;
            Vector2 d = head - vine;
            float len = d.magnitude;
            Vector2 dir = len > 1e-4f ? d / len : Vector2.up;
            float angle = Mathf.Atan2(dir.y, dir.x) - 1.5708f;
            Stalk(vine, head - dir * (radius * 0.5f), Mathf.Max(0.0035f, radius * 0.09f), Stem, alpha, delay, 0.2f);
            if (Rand(h, 18) < 0.28f) Tulip(head, radius * 1.05f, angle, colour, alpha, delay + 0.16f);
            else Blossom(head, radius, angle + Rand(h, 19) * 1.2f, colour, alpha, delay + 0.16f);
            if (Rand(h, 20) < 0.5f)
            {
                float side = dir.x >= 0f ? -1f : 1f;
                LeafAt(vine, Mathf.Lerp(0.045f, 0.07f, Rand(h, 21)) * (radius / 0.06f), side > 0f ? -0.5f : 3.6416f, Color.Lerp(Foliage, Stem, Rand(h, 22)), alpha, delay + 0.08f);
            }
        }

        /// <summary>Hermite pieces from a to b (tangents t0, t1 scaled by the length), widths w0 to w1, drawn one after the
        /// other over seconds from delay.</summary>
        void Piece(Vector2 a, Vector2 b, Vector2 t0, Vector2 t1, float w0, float w1, float delay, float seconds, int pieces)
        {
            float len = (b - a).magnitude;
            Vector2 m0 = t0 * len, m1 = t1 * len;
            Vector2 prev = a;
            Vector2 e0 = _rEdge0, e1 = _rEdge1;
            Vector2 dirPrev = t0;
            for (int i = 1; i <= pieces; i++)
            {
                float t = i / (float)pieces;
                float t2 = t * t, t3 = t2 * t;
                Vector2 q = (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * b + (t3 - t2) * m1;
                Vector2 dq = (6f * t2 - 6f * t) * a + (3f * t2 - 4f * t + 1f) * m0 + (-6f * t2 + 6f * t) * b + (3f * t2 - 2f * t) * m1;
                Vector2 dir = dq.sqrMagnitude > 1e-10f ? dq.normalized : dirPrev;
                float w = Mathf.Lerp(w0, w1, t * t * (3f - 2f * t));
                Vector2 n0 = q - Perp(dir) * w, n1 = q + Perp(dir) * w;
                float seg = (q - prev).magnitude;
                float u0 = _ru / RibbonTile, u1 = (_ru + seg) / RibbonTile;
                if (Count < Capacity)
                    Out[Count++] = new BrushQuad
                    {
                        A = e0, B = n0, C = n1, D = e1,
                        UvA = new Vector2(u0, 0f), UvB = new Vector2(u1, 0f), UvC = new Vector2(u1, 1f), UvD = new Vector2(u0, 1f),
                        Cell = Ribbon, Mode = BrushMode.Wipe, Colour = _rc, Alpha = _ra,
                        Delay = delay + seconds * (i - 1) / pieces, Dur = seconds / pieces,
                    };
                _ru += seg;
                e0 = n0; e1 = n1;
                prev = q;
                dirPrev = dir;
            }
            _rEdge0 = e0; _rEdge1 = e1;
            _rp = b;
            _rdir = dirPrev;
            _rw = w1;
        }

        // ---------------------------------------------------------------- the harp's flock

        /// <summary>Whether a harp note goes on with the open flock (a glissando: soon after the last, a string or two away).</summary>
        public bool ArcContinues(in BrushNote n) => _arc && n.Time - _at <= GlissGap && Mathf.Abs(n.Key - _aKey) <= 3 && _ath > 0.35f;

        /// <summary>A new flock round centre (the anchor placed with Reach): its first bird on the arc's left, a little above
        /// level; the flock swoops on over the top to the right.</summary>
        public void ArcStart(in BrushNote n, Vector2 centre, float scale, Color colour)
        {
            float l = Mathf.Clamp01(n.Level);
            _arc = true;
            _ac = centre;
            _ar = Mathf.Lerp(ArcRMin, ArcRMax, l) * scale;
            _as = Mathf.Lerp(BirdMin, BirdMax, l) * scale;
            _ath = Mathf.PI - ArcFirst - 0.3f * Rand(n.Hash, 6);
            _at = n.Time;
            _aKey = n.Key;
            _aBirds = 0;
            FlockBird(n.Hash, Deep(colour), AlphaOf(l), 0f);
        }

        /// <summary>A glissando note: the flock's next bird, ArcStep on along the arc.</summary>
        public void ArcNext(in BrushNote n, Color colour)
        {
            _at = n.Time;
            _aKey = n.Key;
            _ath = Mathf.Max(0.12f, _ath - ArcStep);
            FlockBird(n.Hash, Deep(colour), AlphaOf(n.Level), 0.1f);
            if (_ath <= 0.35f) _arc = false;   // past the top and down: the next note starts a new flock
        }

        void FlockBird(uint h, Color colour, float alpha, float delay)
        {
            float r = _ar + (_aBirds % 2 == 0 ? 1f : -1f) * 0.035f * Rand(h, 23);
            var dir = new Vector2(Mathf.Cos(_ath), Mathf.Sin(_ath));
            float s = _as * (0.85f + 0.3f * Rand(h, 24)) * (1f - 0.04f * Mathf.Min(_aBirds, 6));
            float bank = (_ath - 1.5708f) * 0.4f;   // flying clockwise: tilted into the turn
            Bird(_ac + dir * r, s, bank, colour, alpha, delay);
            _aBirds++;
        }

        public void ArcEnd() => _arc = false;

        // ---------------------------------------------------------------- quads

        /// <summary>A rectangle quad: half sizes (hx along its own x, hy up), turned by angle (radians).</summary>
        public void AddRect(Vector2 c, float hx, float hy, float angle, int cell, Color colour, float alpha, BrushMode mode, float delay, float dur, float p1, float p2)
        {
            if (Count >= Capacity) return;
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            var ax = new Vector2(ca, sa) * hx;
            var ay = new Vector2(-sa, ca) * hy;
            Out[Count++] = new BrushQuad
            {
                A = c - ax - ay, B = c + ax - ay, C = c + ax + ay, D = c - ax + ay,
                UvA = new Vector2(0f, 0f), UvB = new Vector2(1f, 0f), UvC = new Vector2(1f, 1f), UvD = new Vector2(0f, 1f),
                Cell = cell, Mode = mode, Colour = colour, Alpha = alpha, Delay = delay, Dur = dur, P1 = p1, P2 = p2,
            };
        }

        // ---------------------------------------------------------------- helpers

        static Vector2 Perp(Vector2 d) => new Vector2(-d.y, d.x);
        static Color Darker(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        /// <summary>A bird's colour: the pigment deepened, so a flock reads against the sky.</summary>
        public static Color Deep(Color c) => new Color(c.r * 0.62f, c.g * 0.62f, c.b * 0.62f, c.a);
        static float Rand(uint h, int channel) => WallCanvas.Rand(h, channel);
    }
}
