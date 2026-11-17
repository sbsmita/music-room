// The marimba's procedural geometry (spec 4.2, 4.3; review items 11, 17), in the instrument-local frame of
// MarimbaGeometry. Four meshes, one per material: the 37 bars (one dynamic mesh, each bar a contiguous vertex range the
// view rewrites while it moves), the frame wood (end boards, rails with felt, legs, stretchers, the tray, the cords),
// the metal (resonators, brass rims, posts, the tray brackets) and one mallet (shared by both mallets). Build time only.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Marimba
{
    /// <summary>The bars mesh as plain arrays: base positions and normals (the rest pose), UVs, triangles, and each bar's
    /// vertex range and roll pivot.</summary>
    public sealed class MarimbaBarsData
    {
        public Vector3[] Pos, Nrm;
        public Vector2[] Uv;
        public int[] Tris;
        public readonly int[] Start = new int[MarimbaGeometry.Bars], Count = new int[MarimbaGeometry.Bars];
        public readonly Vector3[] Pivot = new Vector3[MarimbaGeometry.Bars];
    }

    public static class MarimbaBuilder
    {
        // ------------------------------------------------------------------ bars

        public static MarimbaBarsData Bars(float h)
        {
            var k = new MarimbaMeshKit();
            var data = new MarimbaBarsData();
            for (int i = 0; i < MarimbaGeometry.Bars; i++)
            {
                data.Start[i] = k.VertexCount;
                Bar(k, i, h);
                data.Count[i] = k.VertexCount - data.Start[i];
                data.Pivot[i] = new Vector3(MarimbaGeometry.X(i), MarimbaVisualSpec.HoleY(i, h), MarimbaGeometry.Z(i));
            }
            data.Pos = k.V.ToArray(); data.Nrm = k.N.ToArray(); data.Uv = k.UV.ToArray(); data.Tris = k.T.ToArray();
            return data;
        }

        /// <summary>One bar: the crowned top with rounded long edges, the two side walls and the bottom following the arch,
        /// both end faces (end grain), and the cord holes as near-black discs where the cord enters.</summary>
        static void Bar(MarimbaMeshKit k, int i, float h)
        {
            float x = MarimbaGeometry.X(i), zc = MarimbaGeometry.Z(i), L = MarimbaGeometry.Length(i), w = MarimbaGeometry.Width(i);
            float top = MarimbaGeometry.Top(i, h), h0 = MarimbaGeometry.Thickness(i);
            float z0 = zc - L * 0.5f, z1 = zc + L * 0.5f, re = MarimbaVisualSpec.EdgeRadius, hw = w * 0.5f;
            MarimbaVisualSpec.BarGrain(i, out int band, out float u0, out bool flip);
            float lenU = L / MarimbaVisualSpec.RoseMaxLength * MarimbaTextures.RoseGrainU;
            float bh = 1f / MarimbaTextures.RoseBands, vb = band * bh;
            float U(float z) { float t = (z - z0) / L; return u0 + (flip ? 1f - t : t) * lenU; }
            float V(float f) => vb + (0.04f + 0.92f * Mathf.Clamp01(f)) * bh;
            float CrownY(float xx) => top - MarimbaVisualSpec.Crown * (xx / hw) * (xx / hw);
            float yEdge = CrownY(hw - re);

            // the top's cross-section across x (11 points): side top, two corner points, the crown, mirrored
            var prof = new List<Vector2>(11); var pn = new List<Vector2>(11);
            prof.Add(new Vector2(-hw, yEdge - re)); pn.Add(new Vector2(-1, 0));
            foreach (float deg in new[] { 150f, 120f })
            {
                float a = deg * Mathf.Deg2Rad;
                prof.Add(new Vector2(-hw + re + re * Mathf.Cos(a), yEdge - re + re * Mathf.Sin(a))); pn.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            foreach (float xx in new[] { -(hw - re), -hw * 0.5f, 0f, hw * 0.5f, hw - re })
            {
                prof.Add(new Vector2(xx, CrownY(xx)));
                pn.Add(new Vector2(2f * MarimbaVisualSpec.Crown * xx / (hw * hw), 1f).normalized);
            }
            foreach (float deg in new[] { 60f, 30f })
            {
                float a = deg * Mathf.Deg2Rad;
                prof.Add(new Vector2(hw - re + re * Mathf.Cos(a), yEdge - re + re * Mathf.Sin(a))); pn.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            prof.Add(new Vector2(hw, yEdge - re)); pn.Add(new Vector2(1, 0));
            int np = prof.Count;
            // across: the top's arc length mapped into the band
            var across = new float[np];
            float per = 0f;
            for (int p = 1; p < np; p++) per += (prof[p] - prof[p - 1]).magnitude;
            for (int p = 1; p < np; p++) across[p] = across[p - 1] + (prof[p] - prof[p - 1]).magnitude / per;
            int s0 = k.VertexCount;
            foreach (float z in new[] { z0, z1 })
                for (int p = 0; p < np; p++)
                    k.Vert(new Vector3(x + prof[p].x, prof[p].y, z), new Vector3(pn[p].x, pn[p].y, 0f), new Vector2(U(z), V(0.08f + 0.84f * across[p])));
            for (int p = 0; p < np - 1; p++) k.Quad(s0 + p, s0 + p + 1, s0 + np + p + 1, s0 + np + p);

            // slices along the length: the ends, the arch's span in 16 segments
            MarimbaVisualSpec.ArchSpan(i, out float a0, out float a1);
            var xis = new List<float>(20) { 0f };
            for (int s = 0; s <= MarimbaVisualSpec.ArchSegments; s++) xis.Add(a0 + (a1 - a0) * s / MarimbaVisualSpec.ArchSegments);
            xis.Add(1f);
            int ns = xis.Count;
            float Bot(float xi) => top - h0 * MarimbaVisualSpec.Profile(i, xi);
            // side walls
            for (int side = -1; side <= 1; side += 2)
            {
                int b0 = k.VertexCount;
                for (int s = 0; s < ns; s++)
                {
                    float z = z0 + xis[s] * L;
                    k.Vert(new Vector3(x + side * hw, yEdge - re, z), new Vector3(side, 0, 0), new Vector2(U(z), V(side < 0 ? 0.02f : 0.98f)));
                    k.Vert(new Vector3(x + side * hw, Bot(xis[s]), z), new Vector3(side, 0, 0), new Vector2(U(z), V(side < 0 ? 0.0f : 1f) + (side < 0 ? -0.0f : 0f)));
                }
                for (int s = 0; s < ns - 1; s++) k.Quad(b0 + 2 * s, b0 + 2 * s + 1, b0 + 2 * s + 3, b0 + 2 * s + 2);
            }
            // the bottom (normal down, tilted with the arch's slope)
            {
                int b0 = k.VertexCount;
                for (int s = 0; s < ns; s++)
                {
                    float z = z0 + xis[s] * L;
                    float xa = Mathf.Max(0f, xis[s] - 0.002f), xb = Mathf.Min(1f, xis[s] + 0.002f);
                    float slope = (Bot(xb) - Bot(xa)) / ((xb - xa) * L);
                    Vector3 n = new Vector3(0f, -1f, slope).normalized;
                    k.Vert(new Vector3(x - hw, Bot(xis[s]), z), n, new Vector2(U(z), V(0.25f)));
                    k.Vert(new Vector3(x + hw, Bot(xis[s]), z), n, new Vector2(U(z), V(0.75f)));
                }
                for (int s = 0; s < ns - 1; s++) k.Quad(b0 + 2 * s, b0 + 2 * s + 1, b0 + 2 * s + 3, b0 + 2 * s + 2);
            }
            // end faces: end grain, a fan from the section's centre
            for (int e = 0; e < 2; e++)
            {
                float z = e == 0 ? z0 : z1;
                Vector3 n = new Vector3(0, 0, e == 0 ? -1 : 1);
                float yb = Bot(e == 0 ? 0f : 1f);
                var ring = new List<Vector2>(prof);
                ring.Add(new Vector2(hw, yb)); ring.Add(new Vector2(-hw, yb));
                float ue = MarimbaTextures.RoseEndU0, ue1 = MarimbaTextures.RoseEndU1;
                int c = k.Vert(new Vector3(x, (top + yb) * 0.5f, z), n, new Vector2((ue + ue1) * 0.5f, V(0.5f)));
                int r0 = k.VertexCount;
                foreach (var q in ring) k.Vert(new Vector3(x + q.x, q.y, z), n, new Vector2(ue + (ue1 - ue) * (q.y - yb) / (top - yb), V(0.5f + q.x / w)));
                for (int q = 0; q < ring.Count; q++) k.Tri(c, r0 + q, r0 + (q + 1) % ring.Count);
            }
            // the cord holes (review item 17): a near-black disc on each side wall where the cord passes through
            float yh = MarimbaVisualSpec.HoleY(i, h), cx = MarimbaGeometry.CordXi(i);
            Vector2 hole = new Vector2((MarimbaTextures.RoseHoleU0 + MarimbaTextures.RoseHoleU1) * 0.5f, V(0.5f));
            foreach (float xi in new[] { cx, 1f - cx })
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = z0 + xi * L, xs = x + side * (hw + MarimbaVisualSpec.HoleOut);
                    Vector3 n = new Vector3(side, 0, 0);
                    int c = k.Vert(new Vector3(xs, yh, z), n, hole);
                    int r0 = k.VertexCount;
                    for (int q = 0; q < 6; q++)
                    {
                        float a = q / 6f * Mathf.PI * 2f;
                        k.Vert(new Vector3(xs, yh + MarimbaVisualSpec.HoleRadius * Mathf.Sin(a), z + MarimbaVisualSpec.HoleRadius * Mathf.Cos(a)), n, hole);
                    }
                    for (int q = 0; q < 6; q++) k.Tri(c, r0 + q, r0 + (q + 1) % 6);
                }
        }

        // ------------------------------------------------------------------ the frame (walnut, felt, cord)

        public static Mesh Frame(float h, out List<(Vector3 centre, Quaternion rot, Vector3 size)> handles)
        {
            handles = new List<(Vector3, Quaternion, Vector3)>(8);
            var k = new MarimbaMeshKit();
            // end boards: an arched top, rounded corners, 30 mm thick
            EndBoard(k, h, -1, MarimbaVisualSpec.BassBoardZ0, MarimbaVisualSpec.BassBoardZ1, MarimbaVisualSpec.BassBoardHeight, handles);
            EndBoard(k, h, +1, MarimbaVisualSpec.TrebleBoardZ0, MarimbaVisualSpec.TrebleBoardZ1, MarimbaVisualSpec.TrebleBoardHeight, handles);
            // rails with a felt strip on top
            for (int row = 0; row < 2; row++)
                for (int back = 0; back < 2; back++)
                {
                    MarimbaVisualSpec.Rail(row, back == 1, out Vector2 a, out Vector2 b);
                    float yt = MarimbaVisualSpec.RailTop(row, h);
                    Vector3 pa = new Vector3(a.x, yt - MarimbaVisualSpec.RailHeight * 0.5f, a.y), pb = new Vector3(b.x, yt - MarimbaVisualSpec.RailHeight * 0.5f, b.y);
                    k.Beam(pa, pb, Vector3.up, MarimbaVisualSpec.RailWidth, MarimbaVisualSpec.RailHeight, MarimbaTextures.Walnut);
                    Vector3 fa = new Vector3(a.x, yt + MarimbaVisualSpec.RailFelt * 0.5f, a.y), fb = new Vector3(b.x, yt + MarimbaVisualSpec.RailFelt * 0.5f, b.y);
                    k.Beam(fa, fb, Vector3.up, MarimbaVisualSpec.RailWidth - 0.006f, MarimbaVisualSpec.RailFelt, MarimbaTextures.Felt);
                    Vector3 d = pb - pa;
                    handles.Add(((pa + pb) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), new Vector3(MarimbaVisualSpec.RailWidth, MarimbaVisualSpec.RailHeight, d.magnitude)));
                }
            // legs, end stretchers and the long stretcher
            for (int end = 0; end < 2; end++)
            {
                float sx = end == 0 ? -MarimbaVisualSpec.BoardX : MarimbaVisualSpec.BoardX;
                float[] zs = end == 0 ? MarimbaVisualSpec.BassLegZ : MarimbaVisualSpec.TrebleLegZ;
                foreach (float z in zs) k.Lathe(new Vector3(sx, 0f, z), LegProfile(MarimbaVisualSpec.BoardBottom(end, z, h) + 0.01f), 8, MarimbaTextures.Walnut);
                k.Rod(new Vector3(sx, MarimbaVisualSpec.StretcherY, zs[0]), new Vector3(sx, MarimbaVisualSpec.StretcherY, zs[1]), MarimbaVisualSpec.EndStretcherRadius, 8, MarimbaTextures.Walnut);
            }
            k.Rod(new Vector3(-MarimbaVisualSpec.BoardX, MarimbaVisualSpec.StretcherY, MarimbaVisualSpec.StretcherZ), new Vector3(MarimbaVisualSpec.BoardX, MarimbaVisualSpec.StretcherY, MarimbaVisualSpec.StretcherZ),
                MarimbaVisualSpec.StretcherRadius, 10, MarimbaTextures.Walnut);
            Tray(k, h);
            Cords(k, h);
            return k.ToMesh("marimba-frame");
        }

        /// <summary>A turned leg from the floor to y1: a foot, a taper, beads, a square-ish top block (radius, height).</summary>
        static Vector2[] LegProfile(float y1)
        {
            float r = MarimbaVisualSpec.LegRadius, H = y1;
            return new[]
            {
                new Vector2(0.0001f, 0f), new Vector2(r * 1.15f, 0f), new Vector2(r * 1.2f, 0.012f), new Vector2(r * 0.95f, 0.03f),
                new Vector2(r * 0.85f, 0.08f), new Vector2(r * 1.05f, H * 0.42f), new Vector2(r * 1.15f, H * 0.45f), new Vector2(r * 1.0f, H * 0.48f),
                new Vector2(r * 0.9f, H * 0.80f), new Vector2(r * 1.15f, H * 0.86f), new Vector2(r * 1.15f, H - 0.004f), new Vector2(0.0001f, H),
            };
        }

        /// <summary>An end board: a z-monotone outline (MarimbaVisualSpec.BoardTop / BoardBottom with rounded corners), its two
        /// faces as strips between the top and bottom edges, and the edge strip around it; 30 mm thick, walnut grain along z.</summary>
        static void EndBoard(MarimbaMeshKit k, float h, int side, float z0, float z1, float height, List<(Vector3, Quaternion, Vector3)> handles, int N = 24)
        {
            int end = side < 0 ? 0 : 1;
            float rc = MarimbaVisualSpec.BoardCorner;
            var zs = new float[N + 1]; var tops = new float[N + 1]; var bots = new float[N + 1];
            for (int q = 0; q <= N; q++)
            {
                // denser samples near the ends for the rounded corners
                float u = q / (float)N, w = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u);
                float z = Mathf.Lerp(z0, z1, 0.65f * w + 0.35f * u);
                float d = Mathf.Min(z - z0, z1 - z);
                float fillet = d < rc ? rc - Mathf.Sqrt(Mathf.Max(0f, rc * rc - (rc - d) * (rc - d))) : 0f;
                zs[q] = z;
                tops[q] = MarimbaVisualSpec.BoardTop(end, z, h) - fillet;
                bots[q] = MarimbaVisualSpec.BoardBottom(end, z, h) + fillet;
            }
            float x0 = side * MarimbaVisualSpec.BoardX - MarimbaVisualSpec.BoardThickness * 0.5f, x1 = x0 + MarimbaVisualSpec.BoardThickness;
            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int q = 0; q <= N; q++) { yMin = Mathf.Min(yMin, bots[q]); yMax = Mathf.Max(yMax, tops[q]); }
            UvRegion uv = MarimbaTextures.Walnut;
            Vector2 UvAt(float z, float y) => uv.At((z - z0) / (z1 - z0), (y - yMin) / Mathf.Max(0.01f, yMax - yMin));
            // the two faces
            for (int f = 0; f < 2; f++)
            {
                float x = f == 0 ? x0 : x1;
                Vector3 n = new Vector3(f == 0 ? -1f : 1f, 0f, 0f);
                int b0 = k.VertexCount;
                for (int q = 0; q <= N; q++)
                {
                    k.Vert(new Vector3(x, tops[q], zs[q]), n, UvAt(zs[q], tops[q]));
                    k.Vert(new Vector3(x, bots[q], zs[q]), n, UvAt(zs[q], bots[q]));
                }
                for (int q = 0; q < N; q++) k.Quad(b0 + 2 * q, b0 + 2 * q + 1, b0 + 2 * q + 3, b0 + 2 * q + 2);
            }
            // the edge strip: top edge front -> back, the back end, bottom edge back -> front, the front end
            var loop = new List<Vector2>(2 * N + 4);
            for (int q = 0; q <= N; q++) loop.Add(new Vector2(zs[q], tops[q]));
            for (int q = N; q >= 0; q--) loop.Add(new Vector2(zs[q], bots[q]));
            int m = loop.Count, s0 = k.VertexCount;
            float acc = 0f;
            for (int q = 0; q <= m; q++)
            {
                Vector2 p = loop[q % m], prev = loop[(q - 1 + m) % m], next = loop[(q + 1) % m];
                Vector2 t = (next - prev).normalized;
                Vector2 o = new Vector2(-t.y, t.x);   // the loop runs clockwise in (z, y): outward is to the left
                if (q > 0) acc += (p - loop[q - 1]).magnitude;
                Vector3 n = new Vector3(0f, o.y, o.x);
                k.Vert(new Vector3(x0, p.y, p.x), n, uv.At(Mathf.Repeat(acc / 0.5f, 1f) * 0.98f, 0.1f));
                k.Vert(new Vector3(x1, p.y, p.x), n, uv.At(Mathf.Repeat(acc / 0.5f, 1f) * 0.98f, 0.16f));
            }
            for (int q = 0; q < m; q++) k.Quad(s0 + 2 * q, s0 + 2 * q + 1, s0 + 2 * q + 3, s0 + 2 * q + 2);
            handles?.Add((new Vector3(side * MarimbaVisualSpec.BoardX, (yMin + yMax) * 0.5f, (z0 + z1) * 0.5f), Quaternion.identity,
                new Vector3(MarimbaVisualSpec.BoardThickness, yMax - yMin, z1 - z0)));
        }

        /// <summary>The felt-lined walnut tray on the front rail (review item 11: its top at H + 0.005).</summary>
        static void Tray(MarimbaMeshKit k, float h)
        {
            float yt = h + MarimbaGeometry.TrayAboveH, len = MarimbaGeometry.TrayLength, dep = MarimbaGeometry.TrayDepth, well = MarimbaGeometry.TrayWell, wall = MarimbaVisualSpec.TrayWall;
            float zc = MarimbaGeometry.TrayZ, wallH = well + 0.006f;
            k.Box(new Vector3(0f, yt - well - 0.003f, zc), Quaternion.identity, new Vector3(len, 0.006f, dep), MarimbaTextures.Walnut);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(0f, yt - wallH * 0.5f, zc + s * (dep * 0.5f - wall * 0.5f)), Quaternion.identity, new Vector3(len, wallH, wall), MarimbaTextures.Walnut);
                k.Box(new Vector3(s * (len * 0.5f - wall * 0.5f), yt - wallH * 0.5f, zc), Quaternion.identity, new Vector3(wall, wallH, dep - 2 * wall), MarimbaTextures.Walnut);
            }
            k.Box(new Vector3(0f, yt - well + 0.001f, zc), Quaternion.identity, new Vector3(len - 2 * wall, 0.002f, dep - 2 * wall), MarimbaTextures.Felt);
        }

        /// <summary>The dark red cords: through each bar's holes along a node line, dipping round the posts in the gaps.</summary>
        static void Cords(MarimbaMeshKit k, float h)
        {
            for (int row = 0; row < 2; row++)
                for (int back = 0; back < 2; back++)
                {
                    // the visible spans only (inside a bar the cord is hidden): bar exit -> post -> next bar's entry
                    var bars = RowBars(row);
                    var posts = PostXs(row);
                    float yp = MarimbaVisualSpec.RailTop(row, h) + 0.010f;
                    Vector3 Post(int j) => new Vector3(posts[j], yp, MarimbaVisualSpec.RailZ(row, back == 1, posts[j]));
                    Vector3 Hole(int j, int side)
                    {
                        int i = bars[j];
                        float x = MarimbaGeometry.X(i) + side * (MarimbaGeometry.Width(i) * 0.5f - 0.002f);
                        return new Vector3(x, MarimbaVisualSpec.HoleY(i, h), MarimbaVisualSpec.NodeZ(i, back == 1));
                    }
                    k.Sweep(new[] { Post(0), Hole(0, -1) }, MarimbaVisualSpec.CordRadius, 3, MarimbaTextures.Cord, true, false, 0f);
                    for (int j = 0; j < bars.Count - 1; j++)
                        k.Sweep(new[] { Hole(j, 1), Post(j + 1), Hole(j + 1, -1) }, MarimbaVisualSpec.CordRadius, 3, MarimbaTextures.Cord, false, false, 0f);
                    k.Sweep(new[] { Hole(bars.Count - 1, 1), Post(bars.Count) }, MarimbaVisualSpec.CordRadius, 3, MarimbaTextures.Cord, false, true, 0f);
                }
        }

        /// <summary>The bars of a row in x order (row 0 the naturals, 1 the sharps).</summary>
        public static List<int> RowBars(int row)
        {
            var list = new List<int>(22);
            for (int i = 0; i < MarimbaGeometry.Bars; i++) if (MarimbaGeometry.Natural(i) == (row == 0)) list.Add(i);
            return list;
        }

        /// <summary>The posts' x on a row: outside the first bar, in every gap, outside the last bar (bars + 1 values).</summary>
        public static List<float> PostXs(int row)
        {
            var bars = RowBars(row);
            var xs = new List<float>(bars.Count + 1);
            xs.Add(MarimbaGeometry.X(bars[0]) - MarimbaGeometry.Width(bars[0]) * 0.5f - 0.006f);
            for (int j = 0; j < bars.Count - 1; j++)
            {
                int a = bars[j], b = bars[j + 1];
                xs.Add(((MarimbaGeometry.X(a) + MarimbaGeometry.Width(a) * 0.5f) + (MarimbaGeometry.X(b) - MarimbaGeometry.Width(b) * 0.5f)) * 0.5f);
            }
            int last = bars[bars.Count - 1];
            xs.Add(MarimbaGeometry.X(last) + MarimbaGeometry.Width(last) * 0.5f + 0.006f);
            return xs;
        }

        // ------------------------------------------------------------------ metal (bronze tubes, brass rims, posts, brackets)

        public static Mesh Metal(float h, out MarimbaVisualSpec.TubePlan[] tubes)
        {
            var k = new MarimbaMeshKit();
            tubes = new MarimbaVisualSpec.TubePlan[MarimbaGeometry.Bars];
            for (int i = 0; i < MarimbaGeometry.Bars; i++)
            {
                var t = MarimbaVisualSpec.Tube(i, h);
                tubes[i] = t;
                int sides = MarimbaVisualSpec.Sides(t.Folded);
                k.Sweep(t.Path, t.Radius, sides, MarimbaTextures.Bronze, false, true, 0f);
                Vector3 m = t.Path[0];
                // the brass rim on the mouth, and the blackened inside a little way down (a stopper seen from above)
                // (12-sided: the rims are what the player sees between the bars; the body's facets hide under smooth normals)
                const int rim = 12;
                k.Annulus(m, Vector3.up, t.Radius - MarimbaVisualSpec.TubeWall - 0.0015f, t.Radius + 0.0018f, rim, MarimbaTextures.Brass);
                float inside = Mathf.Min(0.03f, (t.Path[1] - t.Path[0]).magnitude - 0.001f);
                k.Sweep(new[] { m, m + Vector3.down * inside }, t.Radius - MarimbaVisualSpec.TubeWall, rim, MarimbaTextures.TubeInside, false, false, 0f, true);
                k.Annulus(m + Vector3.down * inside, Vector3.up, 0.0001f, t.Radius - MarimbaVisualSpec.TubeWall, rim, MarimbaTextures.TubeInside);
            }
            // brass posts on the rails, one in every gap and outside the end bars, front and back
            for (int row = 0; row < 2; row++)
            {
                float yt = MarimbaVisualSpec.RailTop(row, h) + MarimbaVisualSpec.RailFelt;
                foreach (float x in PostXs(row))
                    for (int back = 0; back < 2; back++)
                    {
                        float z = MarimbaVisualSpec.RailZ(row, back == 1, x);
                        k.Rod(new Vector3(x, yt - 0.002f, z), new Vector3(x, yt + MarimbaVisualSpec.PostHeight, z), MarimbaVisualSpec.PostRadius, 4, MarimbaTextures.Brass, false, true);
                    }
            }
            // the tray's two brackets: from the front rail's face forward under the tray, then up to it
            float trayBottom = h + MarimbaGeometry.TrayAboveH - MarimbaGeometry.TrayWell - 0.006f;
            float yb = MarimbaVisualSpec.RailTop(0, h) - MarimbaVisualSpec.RailHeight * 0.6f;
            foreach (float bx in new[] { -MarimbaVisualSpec.TrayBracketX, MarimbaVisualSpec.TrayBracketX })
            {
                float zr = MarimbaVisualSpec.RailZ(0, false, bx) - MarimbaVisualSpec.RailWidth * 0.5f, zt = MarimbaGeometry.TrayZ + MarimbaGeometry.TrayDepth * 0.5f - 0.004f;
                k.Box(new Vector3(bx, yb, (zr + zt) * 0.5f), Quaternion.identity, new Vector3(0.014f, 0.003f, zr - zt + 0.004f), MarimbaTextures.Brass);
                k.Box(new Vector3(bx, (yb + trayBottom) * 0.5f, zt), Quaternion.identity, new Vector3(0.014f, trayBottom - yb + 0.003f, 0.003f), MarimbaTextures.Brass);
            }
            return k.ToMesh("marimba-metal");
        }

        // ------------------------------------------------------------------ the display version (spec 4.3)
        // On display the marimba stands by the window, metres away: plain bars (no arch, no holes), 8-sided tubes on a
        // thinned centre line with a dark mouth instead of the rims, no cords or posts, plain round legs, the tray as one
        // box, and simple mallets resting in it (merged into the frame draw). About 4 k triangles in 3 draws.

        public static Mesh DisplayBars(float h)
        {
            var k = new MarimbaMeshKit();
            for (int i = 0; i < MarimbaGeometry.Bars; i++)
            {
                MarimbaVisualSpec.BarGrain(i, out int band, out float u0, out bool flip);
                float lenU = MarimbaGeometry.Length(i) / MarimbaVisualSpec.RoseMaxLength * MarimbaTextures.RoseGrainU;
                float bh = 1f / MarimbaTextures.RoseBands, v0 = (band + 0.08f) * bh, v1 = (band + 0.92f) * bh;
                var uv = flip ? new UvRegion(u0 + lenU, u0, v0, v1) : new UvRegion(u0, u0 + lenU, v0, v1);
                float top = MarimbaGeometry.Top(i, h), t = MarimbaGeometry.Thickness(i);
                k.Box(new Vector3(MarimbaGeometry.X(i), top - t * 0.5f, MarimbaGeometry.Z(i)), Quaternion.identity,
                    new Vector3(MarimbaGeometry.Width(i), t, MarimbaGeometry.Length(i)), uv);
            }
            return k.ToMesh("marimba-display-bars");
        }

        public static Mesh DisplayMetal(MarimbaVisualSpec.TubePlan[] tubes)
        {
            var k = new MarimbaMeshKit();
            var path = new List<Vector3>(16);
            foreach (var t in tubes)
            {
                path.Clear();
                for (int p = 0; p < t.Path.Count; p++) if (p <= 1 || p == t.Path.Count - 1 || p % 2 == 0) path.Add(t.Path[p]);
                k.Sweep(path, t.Radius, MarimbaVisualSpec.DisplaySides, MarimbaTextures.Bronze, false, true, 0f);
                k.Annulus(t.Path[0] + Vector3.down * 0.002f, Vector3.up, 0.0001f, t.Radius, MarimbaVisualSpec.DisplaySides, MarimbaTextures.TubeInside);
            }
            return k.ToMesh("marimba-display-metal");
        }

        /// <summary>The display frame with both mallets resting on the tray (one mesh, the frame wood material).</summary>
        public static Mesh DisplayFrame(float h)
        {
            var k = new MarimbaMeshKit();
            EndBoard(k, h, -1, MarimbaVisualSpec.BassBoardZ0, MarimbaVisualSpec.BassBoardZ1, MarimbaVisualSpec.BassBoardHeight, null, 10);
            EndBoard(k, h, +1, MarimbaVisualSpec.TrebleBoardZ0, MarimbaVisualSpec.TrebleBoardZ1, MarimbaVisualSpec.TrebleBoardHeight, null, 10);
            for (int row = 0; row < 2; row++)
                for (int back = 0; back < 2; back++)
                {
                    MarimbaVisualSpec.Rail(row, back == 1, out Vector2 a, out Vector2 b);
                    float y = MarimbaVisualSpec.RailTop(row, h) - MarimbaVisualSpec.RailHeight * 0.5f;
                    k.Beam(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), Vector3.up, MarimbaVisualSpec.RailWidth, MarimbaVisualSpec.RailHeight, MarimbaTextures.Walnut);
                }
            for (int end = 0; end < 2; end++)
            {
                float sx = end == 0 ? -MarimbaVisualSpec.BoardX : MarimbaVisualSpec.BoardX;
                float[] zs = end == 0 ? MarimbaVisualSpec.BassLegZ : MarimbaVisualSpec.TrebleLegZ;
                foreach (float z in zs) k.Rod(new Vector3(sx, 0f, z), new Vector3(sx, MarimbaVisualSpec.BoardBottom(end, z, h) + 0.01f, z), MarimbaVisualSpec.LegRadius, 6, MarimbaTextures.Walnut, false, false);
                k.Rod(new Vector3(sx, MarimbaVisualSpec.StretcherY, zs[0]), new Vector3(sx, MarimbaVisualSpec.StretcherY, zs[1]), MarimbaVisualSpec.EndStretcherRadius, 6, MarimbaTextures.Walnut, false, false);
            }
            k.Rod(new Vector3(-MarimbaVisualSpec.BoardX, MarimbaVisualSpec.StretcherY, MarimbaVisualSpec.StretcherZ), new Vector3(MarimbaVisualSpec.BoardX, MarimbaVisualSpec.StretcherY, MarimbaVisualSpec.StretcherZ),
                MarimbaVisualSpec.StretcherRadius, 6, MarimbaTextures.Walnut, false, false);
            float yt = h + MarimbaGeometry.TrayAboveH, well = MarimbaGeometry.TrayWell;
            k.Box(new Vector3(0f, yt - (well + 0.006f) * 0.5f, MarimbaGeometry.TrayZ), Quaternion.identity, new Vector3(MarimbaGeometry.TrayLength, well + 0.006f, MarimbaGeometry.TrayDepth), MarimbaTextures.Walnut);
            for (int s = 0; s < 2; s++)
            {
                MarimbaGeometry.MalletRest(s, h, out Vector3 butt, out Vector3 axis);
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, axis);
                float head = MarimbaGeometry.MalletHeadFromButt;
                k.Rod(butt, butt + axis * (head - 0.008f), MarimbaGeometry.MalletShaftRadius, 6, MarimbaTextures.Rattan, true, false);
                k.Ellipsoid(butt + axis * head, rot, new Vector3(MarimbaGeometry.MalletHeadWidth * 0.5f, MarimbaGeometry.MalletHeadHeight * 0.5f, MarimbaGeometry.MalletHeadWidth * 0.5f), 8, 6, MarimbaTextures.Yarn);
            }
            return k.ToMesh("marimba-display-frame");
        }

        // ------------------------------------------------------------------ a mallet (local: butt at the origin, +y to the head)

        public static Mesh Mallet()
        {
            var k = new MarimbaMeshKit();
            float head = MarimbaGeometry.MalletHeadFromButt;
            var shaft = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, head * 0.5f, 0f), new Vector3(0f, head - 0.008f, 0f) };
            k.Sweep(shaft, MarimbaGeometry.MalletShaftRadius, 8, MarimbaTextures.Rattan, true, false, 0f);
            // the yarn head: oblate along the shaft (34 mm across, 31 mm along)
            k.Ellipsoid(new Vector3(0f, head, 0f), Quaternion.identity, new Vector3(MarimbaGeometry.MalletHeadWidth * 0.5f, MarimbaGeometry.MalletHeadHeight * 0.5f, MarimbaGeometry.MalletHeadWidth * 0.5f), 16, 10, MarimbaTextures.Yarn);
            return k.ToMesh("marimba-mallet");
        }
    }
}
