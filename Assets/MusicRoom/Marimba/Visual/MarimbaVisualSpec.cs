// Visual-only dimensions of the marimba (spec 4.1-4.3 and the review items 11 and 17) and the resonator planner. Every
// bar, tube radius and acoustic length comes from MarimbaGeometry / MarimbaTables (what is drawn is what is struck and
// what sounds); this file only decides how the frame and the tubes are drawn around them. Instrument-local frame of
// MarimbaGeometry (origin on the floor under the natural row's centre line; +x right, +z away from the player).
using System.Collections.Generic;
using MusicRoom.Audio.Burst;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public static class MarimbaVisualSpec
    {
        // ---- bars (4.2): crowned top, rounded long edges, the arch underneath in 16 segments, cord holes
        public const float Crown = 0.0006f, EdgeRadius = 0.002f, HoleRadius = 0.0032f, HoleOut = 0.0003f;
        public const int ArchSegments = 16;
        public const float RoseMaxLength = 0.42f;   // the rosewood window: the longest bar uses 0.40 / 0.42 of the grain run

        // ---- the frame (4.2): rails 35 x 22 mm under the node lines, end boards, legs, stretchers
        public const float RailWidth = 0.035f, RailHeight = 0.022f, RailFelt = 0.003f;
        /// <summary>A rail's top sits this far under the natural (or sharp) bar tops: the thickest bar (22 mm) plus a felt gap.</summary>
        public const float RailTopBelowBars = 0.0255f;
        public const float BoardThickness = 0.03f, BoardX = 0.605f;                    // centre of each end board (outer faces at +-0.62)
        /// <summary>End boards: from just in front of the end bars' near ends to behind the sharps; heights 0.30 / 0.22 under
        /// the rails, which rest on them (the top steps up 22 mm under the sharps' rails); a shallow arch cut out of the
        /// bottom edge between the legs; rounded corners.</summary>
        public const float BassBoardZ0 = -0.19f, BassBoardZ1 = 0.62f, BassBoardHeight = 0.30f;
        public const float TrebleBoardZ0 = -0.10f, TrebleBoardZ1 = 0.40f, TrebleBoardHeight = 0.22f;
        public const float BoardCorner = 0.03f, BassBoardCut = 0.07f, TrebleBoardCut = 0.045f;
        public const float LegRadius = 0.022f;
        public static readonly float[] BassLegZ = { -0.12f, 0.56f }, TrebleLegZ = { -0.04f, 0.34f };
        public const float StretcherY = 0.20f, StretcherZ = 0.14f, StretcherRadius = 0.016f, EndStretcherRadius = 0.014f;

        // ---- posts and cord (4.2)
        public const float PostRadius = 0.002f, PostHeight = 0.014f, CordRadius = 0.0025f;

        // ---- the tray (MarimbaGeometry: 0.82 x 0.05, 15 mm well, top at H + 0.005, review item 11) and its brackets
        public const float TrayWall = 0.006f, TrayBracketX = 0.30f;

        // ---- resonators (2.3, 4.2, review item 11)
        public const float TubeWall = 0.001f;
        /// <summary>Review item 11: from C5 (midi 72) the tubes end on one straight cosmetic line at C5's own length, with an
        /// inner stopper at each tube's acoustic length.</summary>
        public const int CosmeticFromMidi = 72;
        /// <summary>The natural tubes C3..B4 are folded: down, a tight elbow away from the player, a run under the sharps,
        /// a second elbow and down again behind, at this z.</summary>
        public const float FoldBackZ = 0.25f, FoldVertical = 0.010f;
        /// <summary>Review item 11's clearance line: below this height nothing within 0.62 m of the eyes (z &lt; +0.12, the
        /// natural centre line being 0.50 m ahead) in the knee span.</summary>
        public const float KneeLineY = 0.62f, KneeLineZ = 0.12f, KneeTopY = 0.60f, KneeSpanX = 0.21f;

        /// <summary>Sides around a resonator: 12 on the folded tubes (their elbows sit right under the player's eyes and must
        /// read as round, swept tubes), 10 on the straight ones; the display version uses 8.</summary>
        public static int Sides(bool folded) => folded ? 12 : 10;
        public const int DisplaySides = 8;

        /// <summary>Thickness fraction of bar i at xi (0 near end .. 1 far end): 1 outside the arch, the undercut inside
        /// (h = h0 (1 - depth (1 - s^2)^shape), s = (xi - .5) / (width / 2)), the lo and hi profiles blended by register.</summary>
        public static float Profile(int bar, float xi)
        {
            double lo = Arch(MarimbaTables.ArchLo, xi), hi = Arch(MarimbaTables.ArchHi, xi), t = MarimbaTables.HiBlend[bar];
            return (float)(lo + (hi - lo) * t);
        }

        static double Arch(double[] a, double xi)
        {
            double s = (xi - 0.5) / (a[0] / 2);
            if (System.Math.Abs(s) >= 1) return 1;
            return 1 - a[1] * System.Math.Pow(1 - s * s, a[2]);
        }

        /// <summary>The arch's xi span of bar i (the wider of the two profiles' spans, blended).</summary>
        public static void ArchSpan(int bar, out float xi0, out float xi1)
        {
            // the blended thickness is undercut wherever either profile is: the lo profile's (wider) span until C6
            double w = MarimbaTables.HiBlend[bar] < 1 ? MarimbaTables.ArchLo[0] : MarimbaTables.ArchHi[0];
            xi0 = (float)(0.5 - w / 2); xi1 = (float)(0.5 + w / 2);
        }

        /// <summary>The cord hole's height on bar i (mid-thickness, outside the arch).</summary>
        public static float HoleY(int bar, float h) => MarimbaGeometry.Top(bar, h) - MarimbaGeometry.Thickness(bar) * 0.5f;

        /// <summary>z of the cord line through bar i at its near (front) or far (back) node.</summary>
        public static float NodeZ(int bar, bool back) =>
            back ? MarimbaGeometry.FarZ(bar) - MarimbaGeometry.CordXi(bar) * MarimbaGeometry.Length(bar)
                 : MarimbaGeometry.NearZ(bar) + MarimbaGeometry.CordXi(bar) * MarimbaGeometry.Length(bar);

        /// <summary>A rail's centre line (plan) from the bass to the treble end board: through the node points of the row's
        /// lowest and highest bars. row 0 natural, 1 sharp; back = the far node line.</summary>
        public static void Rail(int row, bool back, out Vector2 a, out Vector2 b)
        {
            int i0 = row == 0 ? 0 : 1, i1 = row == 0 ? 36 : 34;
            Vector2 p0 = new Vector2(MarimbaGeometry.X(i0), NodeZ(i0, back)), p1 = new Vector2(MarimbaGeometry.X(i1), NodeZ(i1, back));
            float outer = BoardX + BoardThickness * 0.5f;   // the rails rest on the end boards, flush with their outer faces
            a = RailAt(p0, p1, -outer); b = RailAt(p0, p1, outer);
        }

        /// <summary>z of a rail (row, back) at x.</summary>
        public static float RailZ(int row, bool back, float x)
        {
            Rail(row, back, out Vector2 a, out Vector2 b);
            return Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
        }

        static Vector2 RailAt(Vector2 p0, Vector2 p1, float x) => new Vector2(x, Mathf.LerpUnclamped(p0.y, p1.y, (x - p0.x) / (p1.x - p0.x)));

        public static float RailTop(int row, float h) => h + (row == 1 ? MarimbaGeometry.AccRaise : 0f) - RailTopBelowBars;

        /// <summary>The end board's top edge at z (end 0 bass, 1 treble): under the natural rails in front, stepping up in a
        /// smooth S under the sharps' rails behind (the rails rest on it).</summary>
        public static float BoardTop(int end, float z, float h)
        {
            float x = end == 0 ? -BoardX : BoardX;
            float za = RailZ(0, true, x) + RailWidth * 0.5f + 0.012f, zb = RailZ(1, false, x) - RailWidth * 0.5f - 0.012f;
            float t = Mathf.Clamp01((z - za) / Mathf.Max(0.01f, zb - za));
            t = t * t * (3f - 2f * t);
            return RailTop(0, h) - RailHeight + MarimbaGeometry.AccRaise * t;
        }

        /// <summary>The end board's bottom edge at z: level, with a shallow arch cut out between the legs.</summary>
        public static float BoardBottom(int end, float z, float h)
        {
            float baseY = RailTop(0, h) - RailHeight - (end == 0 ? BassBoardHeight : TrebleBoardHeight);
            float[] legs = end == 0 ? BassLegZ : TrebleLegZ;
            float z0 = legs[0] + LegRadius * 1.6f, z1 = legs[1] - LegRadius * 1.6f;
            if (z <= z0 || z >= z1) return baseY;
            return baseY + (end == 0 ? BassBoardCut : TrebleBoardCut) * Mathf.Sin(Mathf.PI * (z - z0) / (z1 - z0));
        }

        /// <summary>Where bar i's resonator mouth sits: centred under the bar, 25 mm under the arch's deepest point.</summary>
        public static Vector3 Mouth(int bar, float h)
        {
            float under = MarimbaGeometry.Top(bar, h) - MarimbaGeometry.Thickness(bar) * Profile(bar, 0.5f);
            return new Vector3(MarimbaGeometry.X(bar), under - MarimbaGeometry.TubeMouthGap, MarimbaGeometry.Z(bar));
        }

        public struct TubePlan
        {
            public int Bar;
            public bool Folded, Cosmetic;
            public float Radius, Acoustic, Drawn;
            /// <summary>The centre line from the mouth down to the closed end.</summary>
            public List<Vector3> Path;
            /// <summary>The end of the centre line (the closed bottom).</summary>
            public Vector3 End;
            /// <summary>The tube surface's lowest point anywhere, and its lowest point at z &lt; KneeLineZ (the review's clearance).</summary>
            public float LowestY, LowestFrontY;
        }

        /// <summary>The resonator of bar i (review item 11): naturals C3..B4 folded at their acoustic length, C5..C6 straight on
        /// a cosmetic line at C5's length, the other sharps straight at their acoustic length.</summary>
        public static TubePlan Tube(int bar, float h)
        {
            float r = MarimbaGeometry.TubeRadius(bar), lac = MarimbaGeometry.TubeAcousticLength(bar);
            Vector3 m = Mouth(bar, h);
            var plan = new TubePlan { Bar = bar, Radius = r, Acoustic = lac, Path = new List<Vector3>(24) };
            int midi = MarimbaGeometry.MidiOf(bar);
            bool natural = MarimbaGeometry.Natural(bar);
            if (natural && midi < CosmeticFromMidi)
            {
                plan.Folded = true;
                float railBottom = RailTop(0, h) - RailHeight - RailFelt * 0;
                float r1 = Mathf.Max(1.15f * r, 0.026f), r2 = r1;
                float yRun = Mathf.Min(m.y - FoldVertical - r1, railBottom - 0.002f - r);
                var pts = new List<Vector3> { m, new Vector3(m.x, yRun + r1, m.z) };
                const int arcN = 6;
                for (int k = 1; k <= arcN; k++)
                {
                    float t = k / (float)arcN * Mathf.PI * 0.5f;
                    pts.Add(new Vector3(m.x, yRun + r1 - r1 * Mathf.Sin(t), m.z + r1 - r1 * Mathf.Cos(t)));
                }
                float zb = FoldBackZ;
                pts.Add(new Vector3(m.x, yRun, zb - r2));
                for (int k = 1; k <= arcN; k++)
                {
                    float t = k / (float)arcN * Mathf.PI * 0.5f;
                    pts.Add(new Vector3(m.x, yRun - r2 + r2 * Mathf.Cos(t), zb - r2 + r2 * Mathf.Sin(t)));
                }
                pts.Add(new Vector3(m.x, 0.02f, zb));   // far enough: cut at the acoustic length below
                Cut(pts, lac, plan.Path);
                plan.Drawn = lac;
            }
            else
            {
                float drawn = lac;
                if (midi >= CosmeticFromMidi)
                {
                    // one straight line per row at C5's length (the naturals from C5, the sharps from C#5's mouth)
                    int refBar = natural ? 24 : 25;
                    float bottom = Mouth(refBar, h).y - MarimbaGeometry.TubeAcousticLength(24);
                    drawn = m.y - bottom;
                    plan.Cosmetic = true;
                }
                plan.Path.Add(m);
                plan.Path.Add(m + Vector3.down * drawn);
                plan.Drawn = drawn;
            }
            plan.End = plan.Path[plan.Path.Count - 1];
            plan.LowestY = float.MaxValue; plan.LowestFrontY = float.MaxValue;
            // the surface's lowest points: sample the centre line densely, the tube's cross-section around it
            for (int k = 0; k < plan.Path.Count - 1; k++)
            {
                Vector3 a = plan.Path[k], b = plan.Path[k + 1];
                for (int s = 0; s <= 8; s++)
                {
                    Vector3 c = Vector3.Lerp(a, b, s / 8f), t = (b - a).normalized;
                    for (int q = 0; q < 16; q++)
                    {
                        float ang = q / 16f * Mathf.PI * 2f;
                        Vector3 n0 = Mathf.Abs(t.y) < 0.9f ? Vector3.Cross(t, Vector3.up).normalized : Vector3.Cross(t, Vector3.right).normalized;
                        Vector3 n1 = Vector3.Cross(t, n0);
                        Vector3 p = c + (n0 * Mathf.Cos(ang) + n1 * Mathf.Sin(ang)) * r;
                        plan.LowestY = Mathf.Min(plan.LowestY, p.y);
                        if (p.z < KneeLineZ) plan.LowestFrontY = Mathf.Min(plan.LowestFrontY, p.y);
                    }
                }
            }
            return plan;
        }

        /// <summary>The first `length` metres of a polyline.</summary>
        static void Cut(List<Vector3> pts, float length, List<Vector3> into)
        {
            into.Add(pts[0]);
            float acc = 0f;
            for (int k = 1; k < pts.Count; k++)
            {
                float d = (pts[k] - pts[k - 1]).magnitude;
                if (acc + d >= length) { into.Add(Vector3.Lerp(pts[k - 1], pts[k], (length - acc) / d)); return; }
                acc += d;
                into.Add(pts[k]);
            }
        }

        /// <summary>Seeded per-bar texture window (review item 17): band, u offset, flip.</summary>
        public static void BarGrain(int bar, out int band, out float u0, out bool flip)
        {
            uint s = (uint)(0x9e3779b9u * (uint)(bar + 11)) ^ 0x51ed27u;
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            band = (int)(s % MarimbaTextures.RoseBands);
            // neighbours never share a band
            if (bar > 0) { BarGrainBand(bar - 1, out int prev); if (prev == band) band = (band + 1 + (int)((s >> 8) & 1)) % MarimbaTextures.RoseBands; }
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            float len = MarimbaGeometry.Length(bar) / RoseMaxLength * MarimbaTextures.RoseGrainU;
            u0 = (s % 10000) / 10000f * (MarimbaTextures.RoseGrainU - len);
            flip = ((s >> 12) & 1) != 0;
        }

        static void BarGrainBand(int bar, out int band)
        {
            uint s = (uint)(0x9e3779b9u * (uint)(bar + 11)) ^ 0x51ed27u;
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            band = (int)(s % MarimbaTextures.RoseBands);
            if (bar > 0) { BarGrainBand(bar - 1, out int prev); if (prev == band) band = (band + 1 + (int)((s >> 8) & 1)) % MarimbaTextures.RoseBands; }
        }
    }
}
