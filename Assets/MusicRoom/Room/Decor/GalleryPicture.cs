// A song as a framed painting (Decor/PaintGallery.cs): a small watercolour landscape composed from the take's own events
// (round 4, 2026-11-16: "make paintings of flowers birds nature scenery"), so the same take always paints the same
// picture and nothing but its id needs saving (as the night sky's thumbnails: Core's SongStars):
//   - the sky from the song's mood: a lively song a bright day (the sun high), a calm one dawn or dusk (a low glowing sun),
//     a slow, low, quiet one a moonlit evening (the moon, a few stars);
//   - rolling hills whose outline follows the melody's contour (far hills the melody itself, nearer ones its slower swell),
//     a meadow in front;
//   - long notes (held piano notes, ringing bowls) a river winding down from the hills' lowest dip, or a lake there when
//     they fill much of the song;
//   - every instrument its motif in its own pigment, from its own notes in time order across the picture (the walls'
//     brushes, Decor/PaintBrushes.cs): drum hits trees on the hills (a kick a big autumn tree, a cymbal a small far one),
//     piano notes flowers in the foreground (higher notes on taller stalks), harp notes birds in the sky (a glissando a
//     swooping flock), bowls clouds, the handpan butterflies, the hand drums mushrooms, the marimba slim trees;
//   - watercolour on warm paper: washes with granulation and darker wet edges, a deckled paper margin.
// A small software painter: the landscape's washes per pixel, each motif's brush quads as two triangles sampling the
// brush atlas (bilinear, the mip that fits its size) and laid over the washes as the wall shader lays them over a wall.
//   - Notes (main thread: the registry names each event's instrument): the onsets of a take, with pitch, loudness, key,
//     and how long each was held (a piano note to its release; a bowl rings long).
//   - Compose (any thread: pure maths on arrays): Width x Height sRGB pixels, row 0 at the bottom.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Textures;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    /// <summary>One onset of a take for its picture (Length: seconds it was held, 0 when not known).</summary>
    public struct PictureNote
    {
        public InstrumentKind Kind;
        public float Pitch, Level, Time, Length;
        public int Key;
        public bool Accent;
    }

    /// <summary>The brush atlas's pixels on the CPU (PaintArt.Build) with a few box-filtered levels, for the picture painter.</summary>
    public sealed class BrushAtlas
    {
        public const int Levels = 4;
        readonly byte[][] _dens = new byte[Levels][], _alpha = new byte[Levels][];
        readonly int[] _size = new int[Levels];

        public BrushAtlas(Color32[] px)
        {
            int n = PaintArt.Size;
            _size[0] = n;
            _dens[0] = new byte[n * n]; _alpha[0] = new byte[n * n];
            for (int i = 0; i < n * n; i++) { _dens[0][i] = px[i].r; _alpha[0][i] = px[i].a; }
            for (int l = 1; l < Levels; l++)
            {
                int m = _size[l - 1] / 2;
                _size[l] = m;
                _dens[l] = new byte[m * m]; _alpha[l] = new byte[m * m];
                byte[] pd = _dens[l - 1], pa = _alpha[l - 1];
                int pn = _size[l - 1];
                for (int y = 0; y < m; y++)
                    for (int x = 0; x < m; x++)
                    {
                        int a = (2 * y) * pn + 2 * x, b = a + 1, c = a + pn, d = c + 1;
                        _dens[l][y * m + x] = (byte)((pd[a] + pd[b] + pd[c] + pd[d] + 2) >> 2);
                        _alpha[l][y * m + x] = (byte)((pa[a] + pa[b] + pa[c] + pa[d] + 2) >> 2);
                    }
            }
        }

        static readonly object s_lock = new object();
        static BrushAtlas s_shared;
        static Color32[] s_pixels;

        /// <summary>The atlas pixels, made once (any thread; the first caller pays ~0.1-0.3 s).</summary>
        public static Color32[] Pixels
        {
            get
            {
                lock (s_lock)
                {
                    if (s_pixels == null) s_pixels = PaintArt.Build();
                    return s_pixels;
                }
            }
        }

        /// <summary>The shared CPU atlas (any thread).</summary>
        public static BrushAtlas Shared
        {
            get
            {
                Color32[] px = Pixels;
                lock (s_lock)
                {
                    if (s_shared == null) s_shared = new BrushAtlas(px);
                    return s_shared;
                }
            }
        }

        /// <summary>Density and coverage (0..1) of a cell at (u, v) in it (0..1), bilinear within the cell at a level.</summary>
        public void Sample(int cell, float u, float v, int level, out float density, out float alpha)
        {
            level = Mathf.Clamp(level, 0, Levels - 1);
            int n = _size[level], cpx = n / PaintArt.Grid;
            int ox = (cell % PaintArt.Grid) * cpx, oy = (cell / PaintArt.Grid % PaintArt.Grid) * cpx;
            float fx = Mathf.Clamp(u * cpx - 0.5f, 0f, cpx - 1.001f), fy = Mathf.Clamp(v * cpx - 0.5f, 0f, cpx - 1.001f);
            int x0 = (int)fx, y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;
            int i00 = (oy + y0) * n + ox + x0, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
            byte[] d = _dens[level], a = _alpha[level];
            density = (Mathf.Lerp(Mathf.Lerp(d[i00], d[i10], tx), Mathf.Lerp(d[i01], d[i11], tx), ty)) / 255f;
            alpha = (Mathf.Lerp(Mathf.Lerp(a[i00], a[i10], tx), Mathf.Lerp(a[i01], a[i11], tx), ty)) / 255f;
        }
    }

    public static class GalleryPicture
    {
        public const int Width = 256, Height = 192;
        /// <summary>The picture is a canvas this big (m) the motifs are painted on (as on a wall, smaller).</summary>
        public const float CanvasW = 1.6f, CanvasH = 1.2f;
        /// <summary>At most this many motifs of one instrument (a long song keeps an even share of its notes).</summary>
        public const int MaxFlowers = 46, MaxTrees = 22, MaxBirds = 30, MaxOthers = 14, MaxClouds = 6;
        /// <summary>A held note this long (s) or longer is a long note (water).</summary>
        public const float LongNote = 0.9f, BowlRing = 2.5f;
        /// <summary>Watercolour paper (sRGB).</summary>
        public static readonly Color Paper = new Color(0.94f, 0.91f, 0.84f);

        // ---------------------------------------------------------------- the take's notes (main thread)

        /// <summary>The onsets of a take (a saved loop's layers too: its header and layer marks name no instrument), in time
        /// order, with pitch (PaintPalette.PitchOf), loudness, key, accent (a hand drum's slap) and length (a piano note to
        /// its release, a bowl's ring). Returns the take's length.</summary>
        public static float Notes(List<TakeEvent> events, InstrumentRegistry registry, List<PictureNote> into)
        {
            into.Clear();
            float end = 0f;
            if (events == null) return 0f;
            var held = new Dictionary<int, int>();   // a piano key still down: its note's index
            for (int i = 0; i < events.Count; i++)
            {
                TakeEvent e = events[i];
                if (e.type == null) continue;
                if (e.type == TakeEventType.PianoOff)
                {
                    if (held.TryGetValue(e.midi, out int at))
                    {
                        PictureNote p = into[at];
                        p.Length = Mathf.Max(0f, e.t - p.Time);
                        into[at] = p;
                        held.Remove(e.midi);
                    }
                    end = Mathf.Max(end, e.t);
                    continue;
                }
                InstrumentKind kind = registry != null ? registry.OwnerOfEvent(e.type) : ClassicOwner(e.type);
                EventRole role = registry != null ? registry.RoleOf(e.type) : ClassicRole(e.type);
                if (kind == InstrumentKind.Table || role != EventRole.Onset) continue;
                float level = e.vel > 0f ? Mathf.Min(1f, e.vel) : 0.7f;
                float pitch = PaintPalette.PitchOf(kind, e, out int key);
                if (e.type == TakeEventType.PianoOn) held[e.midi] = into.Count;
                into.Add(new PictureNote
                {
                    Kind = kind, Pitch = pitch, Level = level, Time = Mathf.Max(0f, e.t), Key = key, Accent = e.zone == "slap",
                    Length = kind == InstrumentKind.Bowls ? BowlRing : 0f,
                });
                end = Mathf.Max(end, e.t);
            }
            // events are in time order: so are the notes (a stable sort keeps a chord's order)
            var order = new List<PictureNote>(into);
            order.Sort((a, b) => a.Time.CompareTo(b.Time));
            into.Clear();
            into.AddRange(order);
            return end;
        }

        static InstrumentKind ClassicOwner(string type)
        {
            switch (type)
            {
                case TakeEventType.PianoOn: return InstrumentKind.Piano;
                case TakeEventType.Drum: return InstrumentKind.Drums;
                case TakeEventType.Harp: return InstrumentKind.Harp;
                default: return InstrumentKind.Table;
            }
        }

        static EventRole ClassicRole(string type) =>
            type == TakeEventType.PianoOn || type == TakeEventType.Drum || type == TakeEventType.Harp ? EventRole.Onset : EventRole.Release;

        // ---------------------------------------------------------------- what the song says (any thread)

        /// <summary>What a song's picture is made of (tests read it too): how lively it is (0 calm .. 1 lively), its time of
        /// day, the melody's contour across the picture, and whether long notes bring water.</summary>
        public sealed class Scene
        {
            public float Seconds, Lively, MeanPitch, LongShare;
            public uint Seed;
            /// <summary>0 dawn / dusk .. 1 bright day; Night: a moonlit evening; Dawn: calm and early (else dusk).</summary>
            public float Day;
            public bool Night, Dawn, River, Lake;
            /// <summary>The melody's contour (0 low .. 1 high) and its slower swell, per pixel column.</summary>
            public readonly float[] Contour = new float[Width], Swell = new float[Width];
            /// <summary>Motifs painted (tests): flowers, trees, birds, butterflies, mushrooms, clouds.</summary>
            public int Flowers, Trees, Birds, Butterflies, Mushrooms, Clouds;
            /// <summary>The hills' outlines (pixel rows) per column: far, near, the meadow's top.</summary>
            public readonly float[] Far = new float[Width], Near = new float[Width], Meadow = new float[Width];
            public float Valley, SunX, SunY;
        }

        /// <summary>The song's scene: mood, melody contour, water. Deterministic from the notes.</summary>
        public static Scene Read(IReadOnlyList<PictureNote> notes, float seconds)
        {
            var s = new Scene();
            int n = notes?.Count ?? 0;
            float dur = Mathf.Max(seconds, n > 0 ? notes[n - 1].Time + 0.5f : 0f, 1f);
            s.Seconds = dur;
            uint seed = 0x9E3779B9u;
            float level = 0f, pitch = 0f, longSec = 0f;
            int pitched = 0;
            for (int i = 0; i < n; i++)
            {
                PictureNote p = notes[i];
                unchecked { seed = (seed ^ (uint)(p.Key * 7919 + (int)(p.Time * 1000f) + (int)p.Kind * 104729)) * 0x01000193u; }
                level += p.Level;
                if (Pitched(p.Kind)) { pitch += p.Pitch; pitched++; }
                if (p.Length >= LongNote) longSec += Mathf.Min(p.Length, 4f);
            }
            s.Seed = seed;
            float meanLevel = n > 0 ? level / n : 0.6f;
            s.MeanPitch = pitched > 0 ? pitch / pitched : 0.5f;
            float density = n / dur;
            s.Lively = Mathf.Clamp01(Mathf.Clamp01((density - 1.2f) / 5f) * 0.78f + Mathf.Clamp01((meanLevel - 0.35f) / 0.5f) * 0.22f);
            s.Day = Smooth(0.28f, 0.68f, s.Lively);
            s.Night = s.Lively < 0.16f && s.MeanPitch < 0.45f;
            s.Dawn = (seed >> 7 & 1u) == 0u;
            s.LongShare = Mathf.Clamp01(longSec / dur);
            s.Lake = s.LongShare >= 0.45f;
            s.River = !s.Lake && longSec >= 1.8f;
            // the melody: the piano's top notes (else every pitched note)
            var t = new List<float>(256);
            var p0 = new List<float>(256);
            bool piano = false;
            for (int i = 0; i < n && !piano; i++) piano = notes[i].Kind == InstrumentKind.Piano;
            float last = -10f;
            for (int i = 0; i < n; i++)
            {
                PictureNote p = notes[i];
                if (piano ? p.Kind != InstrumentKind.Piano : !Pitched(p.Kind)) continue;
                if (p.Time - last < PaintBrushes.ChordWindow && p0.Count > 0) { p0[p0.Count - 1] = Mathf.Max(p0[p0.Count - 1], p.Pitch); continue; }
                last = p.Time;
                t.Add(p.Time); p0.Add(p.Pitch);
            }
            Smoothed(t, p0, dur, Mathf.Max(0.6f, dur / 14f), seed, s.Contour);
            Smoothed(t, p0, dur, Mathf.Max(1.5f, dur / 5f), seed * 31u, s.Swell);
            return s;
        }

        /// <summary>A contour per column: the pitches near each column's time, Gaussian weighted, scaled to their own range
        /// (a song on few notes still rolls); no melody: a gentle swell from the seed.</summary>
        static void Smoothed(List<float> t, List<float> p, float dur, float sigma, uint seed, float[] into)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            float phase = (seed & 1023u) / 1023f * 6.2832f;
            for (int x = 0; x < Width; x++)
            {
                float tx = dur * (x + 0.5f) / Width;
                float sw = 0f, sp = 0f;
                for (int i = 0; i < t.Count; i++)
                {
                    float d = (t[i] - tx) / sigma;
                    if (d * d > 9f) continue;
                    float w = Mathf.Exp(-d * d);
                    sw += w; sp += w * p[i];
                }
                float v = sw > 1e-4f ? sp / sw : float.NaN;
                into[x] = v;
                if (!float.IsNaN(v)) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            }
            float span = hi - lo;
            for (int x = 0; x < Width; x++)
            {
                float gentle = 0.5f + 0.3f * Mathf.Sin(x / (float)Width * 5.2f + phase);
                if (float.IsNaN(into[x]) || span < 0.04f) { into[x] = float.IsNaN(into[x]) || span >= 0.04f ? gentle : Mathf.Lerp(gentle, 0.5f, 0.3f); continue; }
                into[x] = Mathf.Lerp((into[x] - lo) / span, gentle, 0.12f);
            }
        }

        static bool Pitched(InstrumentKind k) => k != InstrumentKind.Drums && k != InstrumentKind.HandDrums;

        // ---------------------------------------------------------------- the picture (any thread)

        /// <summary>The picture of a take's notes: Width x Height sRGB pixels into <paramref name="into"/> (row 0 at the bottom).
        /// Deterministic: the same notes give the same pixels. No notes: plain paper.</summary>
        public static void Compose(IReadOnlyList<PictureNote> notes, float seconds, BrushAtlas atlas, Color32[] into) =>
            Compose(notes, seconds, atlas, into, out _);

        public static void Compose(IReadOnlyList<PictureNote> notes, float seconds, BrushAtlas atlas, Color32[] into, out Scene scene)
        {
            var px = new Vector3[Width * Height];
            scene = null;
            Color paper = Linear(Paper);
            for (int i = 0; i < px.Length; i++) px[i] = new Vector3(paper.r, paper.g, paper.b) * (0.97f + 0.05f * (Grain(i % Width, i / Width) - 0.5f));
            if (notes == null || notes.Count == 0 || atlas == null) { Finish(px, into); return; }
            Scene s = scene = Read(notes, seconds);
            Palette(s, out Vector3 skyTop, out Vector3 skyLow, out Vector3 far, out Vector3 near, out Vector3 meadow, out Vector3 water);
            Outlines(s);
            Sky(px, s, skyTop, skyLow);
            SunOrMoon(px, s, skyLow);
            var b = new PaintBrushes();
            Clouds(b, atlas, px, notes, s, skyTop, skyLow);
            Hill(px, s.Far, far, 0.72f, 4.1f, 301, Smooth(0f, 1f, s.Day) * 0.25f);
            Motifs(b, atlas, px, notes, s, far, layer: 0);
            if (s.Lake) Lake(px, s, water);
            Hill(px, s.Near, near, 0.95f, 2.6f, 302, 0.1f);
            Hill(px, s.Meadow, meadow, 1f, 1.6f, 303, 0f);
            if (s.River) River(px, s, water);
            Motifs(b, atlas, px, notes, s, near, layer: 1);
            Motifs(b, atlas, px, notes, s, meadow, layer: 2);
            Motifs(b, atlas, px, notes, s, skyTop, layer: 3);
            Foreground(b, atlas, px, s);
            Deckle(px, paper, s.Seed);
            Finish(px, into);
        }

        // ---------------------------------------------------------------- the landscape's washes

        /// <summary>The scene's colours (linear): the sky by the time of day, the hills greener near, hazier far, the water the
        /// sky's.</summary>
        static void Palette(Scene s, out Vector3 skyTop, out Vector3 skyLow, out Vector3 far, out Vector3 near, out Vector3 meadow, out Vector3 water)
        {
            Vector3 calmTop = s.Dawn ? Lin(0.5f, 0.6f, 0.83f) : Lin(0.36f, 0.38f, 0.64f);
            Vector3 calmLow = s.Dawn ? Lin(0.99f, 0.82f, 0.68f) : Lin(0.99f, 0.66f, 0.47f);
            skyTop = Vector3.Lerp(calmTop, Lin(0.36f, 0.62f, 0.9f), s.Day);
            skyLow = Vector3.Lerp(calmLow, Lin(0.84f, 0.91f, 0.95f), s.Day);
            Vector3 green = Vector3.Lerp(Lin(0.4f, 0.52f, 0.3f), Lin(0.42f, 0.66f, 0.3f), s.Day);
            if (s.Night)
            {
                skyTop = Lin(0.14f, 0.18f, 0.38f);
                skyLow = Lin(0.4f, 0.42f, 0.64f);
                green = Lin(0.22f, 0.34f, 0.3f);
            }
            far = Vector3.Lerp(Vector3.Lerp(green, skyTop, 0.5f), Vector3.Lerp(green, skyLow, 0.42f), s.Day) * 0.95f;   // hazy: violet at dusk, pale by day
            near = Vector3.Lerp(green, skyLow, 0.12f) * 0.88f;
            meadow = Vector3.Lerp(green, Lin(0.78f, 0.8f, 0.36f), s.Night ? 0.1f : 0.3f);
            water = Vector3.Lerp(skyLow, skyTop, 0.55f) * 0.95f;
        }

        /// <summary>The hills' outlines (rows): far hills follow the melody's contour, the near ones its slower swell, the
        /// meadow's edge a gentle rise; the valley (the far hills' lowest dip) and where the sun or moon stands.</summary>
        static void Outlines(Scene s)
        {
            float phase = (s.Seed >> 3 & 1023u) / 1023f * 6.2832f;
            float lo = float.MaxValue;
            for (int x = 0; x < Width; x++)
            {
                float u = x / (float)Width;
                s.Far[x] = Height * (0.4f + 0.2f * s.Contour[x]) + 2.2f * (PaintArt.Fbm(u * 9f, phase, 2, 311) - 0.5f) * 2f;
                // the near hills: the melody's slower swell, read backwards (so they rise where the far ones dip), rolling
                float swell = Mathf.Lerp(s.Swell[Width - 1 - x], 0.5f + 0.5f * Mathf.Sin(u * 3.4f + phase * 1.7f), 0.35f);
                s.Near[x] = Height * (0.22f + 0.12f * swell) + 1.5f * (PaintArt.Fbm(u * 7f, phase + 3f, 2, 312) - 0.5f) * 2f;
                s.Meadow[x] = Height * (0.105f + 0.03f * Mathf.Sin(u * 4.4f + phase)) + 1.2f * (PaintArt.Fbm(u * 11f, phase + 7f, 2, 313) - 0.5f);
                if (x > Width / 5 && x < Width * 4 / 5 && s.Far[x] < lo) { lo = s.Far[x]; s.Valley = x; }
            }
            s.SunX = Width * (0.18f + 0.64f * ((s.Seed >> 13 & 1023u) / 1023f));
            int sx = Mathf.Clamp((int)s.SunX, 0, Width - 1);
            s.SunY = s.Night ? Height * 0.78f : Mathf.Lerp(s.Far[sx] + Height * 0.07f, Height * 0.8f, s.Day);
        }

        static void Sky(Vector3[] px, Scene s, Vector3 top, Vector3 low)
        {
            float phase = (s.Seed & 255u) * 0.1f;
            for (int y = 0; y < Height; y++)
            {
                float k = Smooth(Height * 0.35f, Height * 0.98f, y);
                for (int x = 0; x < Width; x++)
                {
                    // a graded wash, streaked a little where the brush went across, granulating
                    float streak = PaintArt.Fbm(x * 0.012f + phase, y * 0.09f, 2, 321);
                    float gran = PaintArt.Fbm(x * 0.21f, y * 0.21f, 2, 322);
                    Vector3 c = Vector3.Lerp(low, top, Mathf.Clamp01(k + 0.18f * (streak - 0.5f)));
                    c *= 1f + 0.08f * (gran - 0.5f);
                    Over(px, x, y, c, 0.97f);
                }
            }
            if (!s.Night) return;
            for (int i = 0; i < 24; i++)
            {
                float fx = Hash(s.Seed, 400 + i) * Width, fy = Height * (0.62f + 0.36f * Hash(s.Seed, 450 + i));
                Dot(px, fx, fy, 0.6f + 0.6f * Hash(s.Seed, 500 + i), new Vector3(1f, 0.97f, 0.85f), 0.8f);
            }
        }

        static void SunOrMoon(Vector3[] px, Scene s, Vector3 low)
        {
            float r = s.Night ? 9f : Mathf.Lerp(15f, 10f, s.Day);
            Vector3 disc = s.Night ? Lin(0.97f, 0.95f, 0.84f) : Vector3.Lerp(Lin(0.99f, 0.62f, 0.3f), Lin(1f, 0.96f, 0.78f), s.Day);
            Vector3 glow = s.Night ? Lin(0.7f, 0.72f, 0.85f) : Vector3.Lerp(Lin(1f, 0.74f, 0.45f), Lin(1f, 0.98f, 0.88f), s.Day);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float dx = x + 0.5f - s.SunX, dy = y + 0.5f - s.SunY;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float halo = Mathf.Exp(-Mathf.Max(0f, d - r) / (r * 1.6f)) * (s.Night ? 0.35f : 0.55f);
                    if (halo > 0.01f) Over(px, x, y, glow, halo);
                    float edge = r + 0.6f * (PaintArt.Fbm(dx * 0.3f, dy * 0.3f, 2, 331) - 0.5f);
                    float a = Mathf.Clamp01((edge - d) * 0.9f);
                    if (s.Night)
                    {
                        // a crescent: the moon's disc, the sky's own colour over its shadowed side
                        float cx = dx - r * 0.45f, cy = dy - r * 0.2f;
                        a *= Mathf.Clamp01((Mathf.Sqrt(cx * cx + cy * cy) - r * 0.85f) * 0.9f);
                    }
                    if (a > 0f) Over(px, x, y, disc * (0.97f + 0.06f * PaintArt.Fbm(x * 0.4f, y * 0.4f, 2, 332)), a * 0.95f);
                }
        }

        /// <summary>A hill: the wash below its outline (rows per column), a darker wet edge along the top, granulation, a
        /// lighter mist toward its foot by mist.</summary>
        static void Hill(Vector3[] px, float[] outline, Vector3 colour, float strength, float edgePx, int seed, float mist)
        {
            for (int x = 0; x < Width; x++)
            {
                float top = outline[x];
                int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(top + 1f));
                for (int y = 0; y <= y1; y++)
                {
                    float d = top - (y + 0.5f);
                    float a = Mathf.Clamp01(d * 0.8f + 0.5f);
                    if (a <= 0f) continue;
                    float gran = PaintArt.Fbm(x * 0.17f, y * 0.17f, 3, seed);
                    float wet = Mathf.Exp(-Mathf.Max(0f, d) / edgePx);
                    float down = Mathf.Clamp01(d / Mathf.Max(8f, top));
                    Vector3 c = colour * (0.94f - 0.2f * wet + 0.16f * (gran - 0.5f) + 0.08f * down);
                    c = Vector3.Lerp(c, new Vector3(0.9f, 0.9f, 0.88f), mist * down);
                    Over(px, x, y, c, a * strength);
                }
            }
        }

        static void Lake(Vector3[] px, Scene s, Vector3 water)
        {
            int vx = Mathf.Clamp((int)s.Valley, 0, Width - 1);
            float cx = s.Valley, cy = Mathf.Lerp(s.Near[vx], s.Far[vx], 0.35f), rx = Width * 0.24f, ry = Height * 0.05f;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float ex = (x + 0.5f - cx) / rx, ey = (y + 0.5f - cy) / ry;
                    if (ey > 0f) ey *= 1.6f;   // the far shore flatter
                    float e = ex * ex + ey * ey;
                    float wob = 0.08f * (PaintArt.Fbm(x * 0.1f, y * 0.1f, 2, 341) - 0.5f);
                    float a = Mathf.Clamp01((1f + wob - e) * 6f);
                    if (a <= 0f) continue;
                    float ripple = PaintArt.Fbm(x * 0.05f, y * 0.9f, 2, 342);
                    Vector3 c = water * (0.92f + 0.22f * (ripple - 0.5f));
                    c = Vector3.Lerp(c, new Vector3(1f, 1f, 1f), 0.3f * Smooth(0.1f, 0.8f, ey) + 0.25f * Smooth(0.62f, 0.9f, ripple));
                    c *= 1f - 0.2f * Smooth(0.5f, 1f, e) * Smooth(0.2f, -0.6f, ey);
                    Over(px, x, y, c, a * 0.95f);
                }
        }

        static void River(Vector3[] px, Scene s, Vector3 water)
        {
            int vx = Mathf.Clamp((int)s.Valley, 0, Width - 1);
            float top = s.Far[vx] - 1.5f, endX = Mathf.Clamp(s.Valley + Width * (0.5f * Hash(s.Seed, 351) - 0.25f), Width * 0.15f, Width * 0.85f);
            float phase = Hash(s.Seed, 352) * 6.2832f;
            for (int y = 0; y < Height; y++)
            {
                if (y > top) break;
                float k = 1f - y / Mathf.Max(1f, top);   // 0 at the hills .. 1 at the bottom
                float cx = Mathf.Lerp(s.Valley, endX, k * k) + Width * 0.07f * Mathf.Sin(k * 7f + phase) * k;
                float hw = Mathf.Lerp(1.2f, Width * 0.15f, Mathf.Pow(k, 1.6f));
                for (int x = Mathf.Max(0, (int)(cx - hw - 2f)); x <= Mathf.Min(Width - 1, (int)(cx + hw + 2f)); x++)
                {
                    float d = hw - Mathf.Abs(x + 0.5f - cx) + 0.8f * (PaintArt.Fbm(x * 0.2f, y * 0.2f, 2, 353) - 0.5f);
                    float a = Mathf.Clamp01(d * 0.8f + 0.3f);
                    if (a <= 0f) continue;
                    float ripple = PaintArt.Fbm(x * 0.06f + k * 3f, y * 0.7f, 2, 354);
                    Vector3 c = water * (0.92f + 0.24f * (ripple - 0.5f) - 0.15f * Mathf.Exp(-Mathf.Max(0f, d) / 1.5f));
                    c = Vector3.Lerp(c, new Vector3(1f, 1f, 1f), 0.3f * Smooth(0.7f, 0.95f, ripple) * k);
                    Over(px, x, y, c, a * 0.95f);
                }
            }
        }

        /// <summary>The paper's deckled margin: the washes stop short of the edge, raggedly; a faint grey at their edge.</summary>
        static void Deckle(Vector3[] px, Color paper, uint seed)
        {
            var p = new Vector3(paper.r, paper.g, paper.b);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x + 0.5f, Width - x - 0.5f), Mathf.Min(y + 0.5f, Height - y - 0.5f));
                    float rag = 3.2f + 1.6f * PaintArt.Fbm(x * 0.15f + y * 0.15f, (x - y) * 0.07f, 2, 361 + (int)(seed & 7u));
                    float a = Mathf.Clamp01((rag - d) * 0.9f);
                    int i = y * Width + x;
                    if (a > 0f) px[i] = Vector3.Lerp(px[i], p * (0.97f + 0.05f * (Grain(x, y) - 0.5f)), a);
                    else if (d - rag < 1.2f) px[i] *= 0.93f;   // the wash's dried edge
                }
        }

        // ---------------------------------------------------------------- the motifs

        /// <summary>A note's place across the picture: its time between the margins (pixels), a little hash jitter.</summary>
        static float Across(in PictureNote n, Scene s, uint h) =>
            Mathf.Clamp(Width * (0.06f + 0.88f * (n.Time / s.Seconds)) + 6f * (Hash(h, 1) - 0.5f), 6f, Width - 6f);

        /// <summary>Of every instrument's notes, which to paint: an even share up to its cap.</summary>
        static int Every(IReadOnlyList<PictureNote> notes, Func<PictureNote, bool> which, int cap)
        {
            int c = 0;
            for (int i = 0; i < notes.Count; i++) if (which(notes[i])) c++;
            return Mathf.Max(1, Mathf.CeilToInt(c / (float)Mathf.Max(1, cap)));
        }

        static bool FarTree(in PictureNote n) => n.Kind == InstrumentKind.Drums && n.Key >= 6 && n.Key <= 8;
        static bool NearTree(in PictureNote n) => (n.Kind == InstrumentKind.Drums && !FarTree(n)) || n.Kind == InstrumentKind.Marimba;

        /// <summary>One layer of motifs: 0 small trees on the far hills (cymbals); 1 trees on the near hills (kicks, toms, the
        /// snare's bushes, the marimba's slim trees); 2 the meadow (flowers, mushrooms, butterflies); 3 the sky (birds).</summary>
        static void Motifs(PaintBrushes b, BrushAtlas atlas, Vector3[] px, IReadOnlyList<PictureNote> notes, Scene s, Vector3 tone, int layer)
        {
            const float m = CanvasW / Width;   // metres per pixel
            if (layer == 0 || layer == 1)
            {
                int every = Every(notes, n => layer == 0 ? FarTree(n) : NearTree(n), layer == 0 ? MaxTrees / 3 : MaxTrees);
                int seen = 0;
                for (int i = 0; i < notes.Count; i++)
                {
                    PictureNote n = notes[i];
                    if (!(layer == 0 ? FarTree(n) : NearTree(n)) || seen++ % every != 0) continue;
                    uint h = WallCanvas.Hash(i, n.Key, (int)n.Kind);
                    float x = Across(n, s, h) + (layer == 0 ? 10f * (Hash(h, 3) - 0.5f) : 0f);
                    if (s.River && OnRiver(s, x, layer == 0 ? s.Far : s.Near)) continue;
                    int xi = Mathf.Clamp((int)x, 0, Width - 1);
                    float footY = (layer == 0 ? s.Far[xi] : s.Near[xi]) - 1.5f - (layer == 1 ? 6f : 9f) * Hash(h, 2);
                    Vector2 foot = new Vector2(x * m, footY * m);
                    Color pig = Linear(PaintPalette.Of(n.Kind, n.Pitch));
                    float l = Mathf.Clamp01(n.Level);
                    b.Clear();
                    if (layer == 0)
                    {
                        Color c = Haze(pig, tone, 0.45f);
                        b.Tree(foot, Mathf.Lerp(0.06f, 0.1f, l) * (0.8f + 0.4f * Hash(h, 4)), c, Haze(PaintBrushes.Bark, tone, 0.45f), 0.9f, h, 0f, n.Key == 6);
                    }
                    else if (n.Kind == InstrumentKind.Marimba)
                        b.Tree(foot, Mathf.Lerp(0.16f, 0.22f, l), pig, PaintBrushes.Bark, 0.95f, h, 0f, true);
                    else if (n.Key == 5)
                        b.AddRect(foot + new Vector2(0f, 0.025f), 0.05f, 0.04f, 0f, PaintArt.Crown, Color.Lerp(pig, PaintBrushes.Foliage, 0.45f), 0.92f, BrushMode.Grow, 0f, 0.1f, 1f, 1f);
                    else
                    {
                        bool kick = n.Key == 1;
                        b.Tree(foot, kick ? Mathf.Lerp(0.2f, 0.27f, l) : Mathf.Lerp(0.12f, 0.16f, l), kick ? pig : Color.Lerp(pig, PaintBrushes.Foliage, 0.4f), PaintBrushes.Bark, 0.95f, h, 0f, false);
                    }
                    Draw(b, atlas, px);
                    s.Trees++;
                }
                return;
            }
            if (layer == 2)
            {
                // flowers from the piano: across the meadow in time, taller stalks for higher notes, buds for chord notes
                int every = Every(notes, n => n.Kind == InstrumentKind.Piano, MaxFlowers);
                int seen = 0;
                float last = -10f;
                for (int i = 0; i < notes.Count; i++)
                {
                    PictureNote n = notes[i];
                    if (n.Kind != InstrumentKind.Piano) continue;
                    bool chord = n.Time - last < PaintBrushes.ChordWindow;
                    last = n.Time;
                    if (seen++ % every != 0) continue;
                    uint h = WallCanvas.Hash(i, n.Key, (int)n.Kind);
                    float x = Across(n, s, h) + (chord ? 7f * (Hash(h, 3) - 0.5f) : 0f);
                    int xi = Mathf.Clamp((int)x, 0, Width - 1);
                    float baseY = Mathf.Lerp(4f, s.Meadow[xi] - 3f, Hash(h, 5));
                    if (s.River && OnRiver(s, x, baseY)) x += (Hash(h, 4) > 0.5f ? 1f : -1f) * 22f;   // no flowers on the water
                    if (s.River && OnRiver(s, x, baseY)) continue;
                    float stalk = Mathf.Lerp(0.05f, 0.2f, Mathf.Clamp01(n.Pitch * 1.2f - 0.1f)) * (chord ? 0.6f : 1f);
                    Color pig = Linear(PaintPalette.Of(n.Kind, n.Pitch));
                    Vector2 foot = new Vector2(x * m, baseY * m);
                    float lean = (Hash(h, 6) - 0.5f) * 0.35f;
                    Vector2 head = foot + new Vector2(-Mathf.Sin(lean), Mathf.Cos(lean)) * stalk;
                    float r = Mathf.Lerp(0.026f, 0.042f, Mathf.Clamp01(n.Level)) * (chord ? 0.75f : 1f);
                    b.Clear();
                    b.Stalk(foot, head, 0.0045f, PaintBrushes.Stem, 0.95f, 0f, 0.1f);
                    if (Hash(h, 7) < 0.6f) b.LeafAt(foot + (head - foot) * 0.35f, 0.045f, Hash(h, 8) > 0.5f ? 0.5f : 2.64f, PaintBrushes.Foliage, 0.92f, 0f);
                    if (chord || Hash(h, 9) < 0.25f) b.Tulip(head, r * 1.05f, lean, pig, 0.95f, 0f);
                    else b.Blossom(head, r, Hash(h, 10) * 1.2f, pig, 0.95f, 0f);
                    Draw(b, atlas, px);
                    s.Flowers++;
                }
                // mushrooms from the hand drums, butterflies from the handpan
                int everyM = Every(notes, n => n.Kind == InstrumentKind.HandDrums, MaxOthers);
                int everyB = Every(notes, n => n.Kind == InstrumentKind.Handpan, MaxOthers);
                int seenM = 0, seenB = 0;
                for (int i = 0; i < notes.Count; i++)
                {
                    PictureNote n = notes[i];
                    uint h = WallCanvas.Hash(i, n.Key, (int)n.Kind);
                    Color pig = Linear(PaintPalette.Of(n.Kind, n.Pitch));
                    if (n.Kind == InstrumentKind.HandDrums && seenM++ % everyM == 0)
                    {
                        float x = Across(n, s, h);
                        int xi = Mathf.Clamp((int)x, 0, Width - 1);
                        float y = Mathf.Lerp(5f, s.Meadow[xi] - 4f, Hash(h, 5));
                        if (s.River && OnRiver(s, x, y)) continue;
                        b.Clear();
                        b.Mushroom(new Vector2(x * m, y * m), (n.Accent ? 0.06f : 0.045f), (Hash(h, 6) - 0.5f) * 0.3f, pig, 0.95f, 0f);
                        Draw(b, atlas, px);
                        s.Mushrooms++;
                    }
                    else if (n.Kind == InstrumentKind.Handpan && seenB++ % everyB == 0)
                    {
                        float x = Across(n, s, h);
                        int xi = Mathf.Clamp((int)x, 0, Width - 1);
                        float y = Mathf.Lerp(s.Meadow[xi] + 6f, s.Near[xi] + 14f, Mathf.Clamp01(n.Pitch));
                        b.Clear();
                        b.Butterfly(new Vector2(x * m, y * m), Mathf.Lerp(0.03f, 0.04f, Mathf.Clamp01(n.Level)), (Hash(h, 6) - 0.5f) * 0.6f, pig, 0.95f, 0f);
                        Draw(b, atlas, px);
                        s.Butterflies++;
                    }
                }
                return;
            }
            // the sky's birds from the harp: a glissando a flock swooping along an arc, a pluck one bird
            {
                int every = Every(notes, n => n.Kind == InstrumentKind.Harp, MaxBirds);
                int seen = 0;
                b.Reset();
                const float scale = 0.42f;
                for (int i = 0; i < notes.Count; i++)
                {
                    PictureNote n = notes[i];
                    if (n.Kind != InstrumentKind.Harp || seen++ % every != 0) continue;
                    uint h = WallCanvas.Hash(i, n.Key, (int)n.Kind);
                    var bn = new BrushNote { Kind = n.Kind, Pitch = n.Pitch, Level = n.Level, Time = n.Time, Key = n.Key, Hash = h };
                    Color pig = Linear(PaintPalette.Of(n.Kind, n.Pitch));
                    b.Clear();
                    if (every == 1 && b.ArcContinues(bn)) b.ArcNext(bn, pig);
                    else
                    {
                        float x = Across(n, s, h);
                        int xi = Mathf.Clamp((int)x, 0, Width - 1);
                        float lo = Mathf.Max(s.Far[xi] + 14f, Height * 0.56f), hi = Height * 0.86f;
                        float y = Mathf.Lerp(lo, hi, Mathf.Clamp01(n.Pitch)) - 22f;
                        b.ArcStart(bn, new Vector2(x * m, y * m), scale, pig);
                    }
                    Draw(b, atlas, px);
                    s.Birds++;
                }
                b.ArcEnd();
            }
        }

        /// <summary>The clouds: one for each bowl strike (in the bowls' lavender, a long song an even share), else one or two
        /// of the sky's own (a lively song's day sky).</summary>
        static void Clouds(PaintBrushes b, BrushAtlas atlas, Vector3[] px, IReadOnlyList<PictureNote> notes, Scene s, Vector3 top, Vector3 low)
        {
            const float m = CanvasW / Width;
            int every = Every(notes, n => n.Kind == InstrumentKind.Bowls, MaxClouds);
            int seen = 0;
            Color sky = new Color(Mathf.Lerp(low.x, 1f, 0.75f), Mathf.Lerp(low.y, 1f, 0.75f), Mathf.Lerp(low.z, 1f, 0.75f), 1f);
            for (int i = 0; i < notes.Count; i++)
            {
                PictureNote n = notes[i];
                if (n.Kind != InstrumentKind.Bowls || seen++ % every != 0) continue;
                uint h = WallCanvas.Hash(i, n.Key, (int)n.Kind);
                float x = Across(n, s, h), y = Height * (0.7f + 0.2f * Hash(h, 2));
                if (Mathf.Abs(x - s.SunX) < 34f && Mathf.Abs(y - s.SunY) < 26f) x += x < s.SunX ? -34f : 34f;   // the sun stays clear
                Color pig = Linear(PaintPalette.Of(n.Kind, n.Pitch));
                b.Clear();
                b.Cloud(new Vector2(x * m, y * m), Mathf.Lerp(0.12f, 0.18f, n.Level), Mathf.Lerp(0.09f, 0.13f, n.Level), 0f, Color.Lerp(sky, pig, 0.3f), 0.85f, 0f);
                Draw(b, atlas, px);
                s.Clouds++;
            }
            if (s.Clouds > 0 || s.Night) return;
            int count = 1 + (int)(Hash(s.Seed, 371) * 2.2f);
            for (int k = 0; k < count; k++)
            {
                float x = Width * (0.15f + 0.7f * Hash(s.Seed, 372 + k)), y = Height * (0.72f + 0.16f * Hash(s.Seed, 380 + k));
                if (Mathf.Abs(x - s.SunX) < 24f && Mathf.Abs(y - s.SunY) < 20f) x = Mathf.Repeat(x + Width * 0.4f, Width * 0.8f) + Width * 0.1f;
                b.Clear();
                b.Cloud(new Vector2(x * m, y * m), 0.13f + 0.05f * Hash(s.Seed, 390 + k), 0.09f, 0f, sky, 0.8f, 0f);
                Draw(b, atlas, px);
                s.Clouds++;
            }
        }

        /// <summary>Grass along the picture's foot.</summary>
        static void Foreground(PaintBrushes b, BrushAtlas atlas, Vector3[] px, Scene s)
        {
            const float m = CanvasW / Width;
            for (int k = 0; k < 22; k++)
            {
                float x = Width * (k + 0.2f + 0.6f * Hash(s.Seed, 600 + k)) / 22f;
                b.Clear();
                Color c = Color.Lerp(PaintBrushes.Stem, PaintBrushes.Foliage, Hash(s.Seed, 630 + k));
                b.Grass(new Vector2(x * m, (2f + 3f * Hash(s.Seed, 660 + k)) * m), 0.05f + 0.04f * Hash(s.Seed, 690 + k), (Hash(s.Seed, 720 + k) - 0.5f) * 0.4f, c, 0.85f, 0f);
                Draw(b, atlas, px);
            }
        }

        static bool OnRiver(Scene s, float x, float[] row) => OnRiver(s, x, row[Mathf.Clamp((int)x, 0, Width - 1)]);

        static bool OnRiver(Scene s, float x, float y)
        {
            int vx = Mathf.Clamp((int)s.Valley, 0, Width - 1);
            float top = s.Far[vx];
            if (y > top) return false;
            float k = 1f - y / Mathf.Max(1f, top);
            float endX = Mathf.Clamp(s.Valley + Width * (0.5f * Hash(s.Seed, 351) - 0.25f), Width * 0.15f, Width * 0.85f);
            float cx = Mathf.Lerp(s.Valley, endX, k * k) + Width * 0.07f * Mathf.Sin(k * 7f + Hash(s.Seed, 352) * 6.2832f) * k;
            float hw = Mathf.Lerp(1.2f, Width * 0.15f, Mathf.Pow(k, 1.6f));
            return Mathf.Abs(x - cx) < hw + 8f;
        }

        static Color Haze(Color c, Vector3 tone, float k) => new Color(Mathf.Lerp(c.r, tone.x, k), Mathf.Lerp(c.g, tone.y, k), Mathf.Lerp(c.b, tone.z, k), 1f);

        // ---------------------------------------------------------------- painting

        static void Over(Vector3[] px, int x, int y, Vector3 c, float a)
        {
            int i = y * Width + x;
            px[i] = Vector3.Lerp(px[i], c, a);
        }

        static void Dot(Vector3[] px, float cx, float cy, float r, Vector3 c, float a)
        {
            for (int y = Mathf.Max(0, (int)(cy - r - 1f)); y <= Mathf.Min(Height - 1, (int)(cy + r + 1f)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r - 1f)); x <= Mathf.Min(Width - 1, (int)(cx + r + 1f)); x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float k = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    if (k > 0f) Over(px, x, y, c, a * k);
                }
        }

        static float Grain(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ 0x2545F491u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                float a = (h >> 8) / 16777216f;
                uint h2 = (uint)(x / 3) * 0x8DA6B343u ^ (uint)(y / 3) * 0xD8163841u ^ 0x68E31DA4u;
                h2 ^= h2 >> 13; h2 *= 0x5bd1e995u; h2 ^= h2 >> 15;
                return 0.4f * a + 0.6f * ((h2 >> 8) / 16777216f);
            }
        }

        static float Hash(uint seed, int channel) => WallCanvas.Rand(seed, channel);

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static void Finish(Vector3[] px, Color32[] into)
        {
            for (int i = 0; i < Width * Height; i++)
            {
                Vector3 c = px[i];
                into[i] = new Color32((byte)Mathf.RoundToInt(Gamma(c.x) * 255f), (byte)Mathf.RoundToInt(Gamma(c.y) * 255f), (byte)Mathf.RoundToInt(Gamma(c.z) * 255f), 255);
            }
        }

        /// <summary>Paint the brushes' quads (settled: leaves fallen, motifs grown, stalks drawn) onto the picture.</summary>
        static void Draw(PaintBrushes b, BrushAtlas atlas, Vector3[] px) => DrawQuads(b, atlas, px, Width, Height, Width / CanvasW, Vector2.zero);

        /// <summary>Paint the brushes' quads onto any linear pixel buffer (w x h, row 0 at the bottom), the canvas's metres
        /// scaled by pxPerM from origin (the picture; tests' flat previews of a wall).</summary>
        public static void DrawQuads(PaintBrushes b, BrushAtlas atlas, Vector3[] px, int w, int h, float pxPerM, Vector2 origin)
        {
            for (int q = 0; q < b.Count; q++)
            {
                BrushQuad bq = b.Out[q];
                Vector2 A = bq.A - origin, B = bq.B - origin, C = bq.C - origin, D = bq.D - origin;
                if (bq.Mode == BrushMode.Fall) { Vector2 f = new Vector2(0f, bq.P1); A -= f; B -= f; C -= f; D -= f; }
                float alpha = Mathf.Min(1f, bq.Alpha * 1.1f);
                if (bq.Mode == BrushMode.Grow) alpha *= Mathf.Max(0.5f, bq.P2);
                Vector2 pa = A * pxPerM, pb = B * pxPerM, pc = C * pxPerM, pd = D * pxPerM;
                bool tiles = PaintArt.Tiles(bq.Cell);
                // the level whose texels match the quad's pixels
                float lu = Mathf.Max((pb - pa).magnitude, 1e-3f), lv = Mathf.Max((pd - pa).magnitude, 1e-3f);
                float du = Mathf.Abs(bq.UvB.x - bq.UvA.x), dv = Mathf.Abs(bq.UvD.y - bq.UvA.y);
                float texPerPx = Mathf.Max(du * PaintArt.CellPx / lu, dv * PaintArt.CellPx / lv);
                int level = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Mathf.Max(texPerPx, 1f), 2f)), 0, BrushAtlas.Levels - 1);
                Color c = bq.Colour;
                var pig = new Vector3(c.r, c.g, c.b);
                float hue = bq.Mode == BrushMode.Band ? bq.P1 : 0f;
                Tri(px, w, h, atlas, pa, pb, pc, bq.UvA, bq.UvB, bq.UvC, bq.Cell, tiles, level, pig, alpha, hue);
                Tri(px, w, h, atlas, pa, pc, pd, bq.UvA, bq.UvC, bq.UvD, bq.Cell, tiles, level, pig, alpha, hue);
            }
        }

        static void Tri(Vector3[] px, int width, int height, BrushAtlas atlas, Vector2 a, Vector2 b, Vector2 c, Vector2 ua, Vector2 ub, Vector2 uc, int cell, bool tiles, int level, Vector3 pig, float alpha, float hue)
        {
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            float inv = 1f / area;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float w0 = ((b.x - qx) * (c.y - qy) - (b.y - qy) * (c.x - qx)) * inv;
                    float w1 = ((c.x - qx) * (a.y - qy) - (c.y - qy) * (a.x - qx)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    Vector2 uv = ua * w0 + ub * w1 + uc * w2;
                    float u = tiles ? PaintArt.TileU(uv.x) : Mathf.Clamp01(uv.x);
                    atlas.Sample(cell, u, Mathf.Clamp01(uv.y), level, out float dens, out float cover);
                    float k = Mathf.Clamp01(cover * alpha);
                    if (k <= 0.002f) continue;
                    Vector3 col = pig;
                    if (hue != 0f) col = HueTurn(col, (uv.y - 0.5f) * hue);
                    col *= 1.25f * (1.12f - 0.42f * dens) * 0.92f;
                    col = new Vector3(Mathf.Clamp01(col.x), Mathf.Clamp01(col.y), Mathf.Clamp01(col.z));
                    int i = y * width + x;
                    px[i] = Vector3.Lerp(px[i], col, k);
                }
        }

        /// <summary>sRGB to linear and back in plain maths (Unity's own conversions are for the main thread).</summary>
        public static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055) / 1.055, 2.4);
        public static float Gamma(float c)
        {
            c = Mathf.Clamp01(c);
            return c <= 0.0031308f ? c * 12.92f : (float)(1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055);
        }
        public static Color Linear(Color c) => new Color(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b), c.a);
        static Vector3 Lin(float r, float g, float b) => new Vector3(ToLinear(r), ToLinear(g), ToLinear(b));

        static Vector3 HueTurn(Vector3 c, float turns)
        {
            float a = turns * 6.2832f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            var k = new Vector3(0.57735f, 0.57735f, 0.57735f);
            Vector3 r = c * ca + Vector3.Cross(k, c) * sa + k * (Vector3.Dot(k, c) * (1f - ca));
            return new Vector3(Mathf.Max(0f, r.x), Mathf.Max(0f, r.y), Mathf.Max(0f, r.z));
        }
    }
}
