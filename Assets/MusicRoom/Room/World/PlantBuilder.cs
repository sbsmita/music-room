// The plants (spec 07 section 3.2), in the right-handed source frame with the spec's exact formulas and random-call
// order. A Math.random draw would differ per load; here each plant has a fixed mulberry32 seed (101..106), and an
// EditMode test reproduces the reference construction with Math.random = mulberry32(seed) (stems, leaf matrices,
// sway phase and amplitude). Pots stay static (merged into the room); stems and leaves sway about the plant base.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Geometry;
using UnityEngine;

namespace MusicRoom.Room.World
{
    public struct PlantOptions
    {
        public float Height, Spread, LeafMin, LeafMax, Droop, Pot;
        public int Stems, LeavesPerStem;
        public bool Trailing, Dark;

        /// <summary>Plant defaults: height .9, stems 7, spread .25, leafScale [.18, .3], droop .35, pot .11, 3 per stem.</summary>
        public static PlantOptions Default => new PlantOptions { Height = 0.9f, Stems = 7, Spread = 0.25f, LeafMin = 0.18f, LeafMax = 0.3f, Droop = 0.35f, Pot = 0.11f, LeavesPerStem = 3 };
    }

    public sealed class PlantGeometry
    {
        public Geo Pot, Soil, Stems, Leaves;
        public float Phase, Amp;
        public int LeafCount;
        /// <summary>The leaf instances' source-frame matrices, for the tests.</summary>
        public readonly List<Matrix4x4> LeafMatrices = new List<Matrix4x4>();
    }

    public static class PlantBuilder
    {
        /// <summary>The six plants (spec 07 section 3.2, `plants`).</summary>
        public static PlantOptions P1 => With(1.1f, 8, 0.3f, 0.24f, 0.38f, 0.25f, 0.14f, false);
        public static PlantOptions P2 => With(0.95f, 7, 0.28f, 0.22f, 0.34f, 0.3f, 0.13f, true);
        public static PlantOptions P3 => With(1.2f, 9, 0.3f, 0.26f, 0.4f, 0.2f, 0.15f, false);
        public static PlantOptions P4 => With(0.7f, 10, 0.24f, 0.14f, 0.22f, 0.45f, 0.11f, true);
        public static PlantOptions Fern { get { var o = With(0.32f, 14, 0.22f, 0.07f, 0.12f, 0.55f, 0.075f, true); o.LeavesPerStem = 4; return o; } }
        public static PlantOptions Ivy { get { var o = With(0.5f, 9, 0.16f, 0.05f, 0.09f, 0.35f, 0.06f, true); o.Trailing = true; o.LeavesPerStem = 4; return o; } }

        static PlantOptions With(float height, int stems, float spread, float l0, float l1, float droop, float pot, bool dark)
        {
            var o = PlantOptions.Default;
            o.Height = height; o.Stems = stems; o.Spread = spread; o.LeafMin = l0; o.LeafMax = l1; o.Droop = droop; o.Pot = pot; o.Dark = dark;
            return o;
        }

        static Geo s_blade;

        /// <summary>The leaf blade: plane 1 x 1.8 (3 x 8), base at the origin, curled so it droops toward
        /// the tip and folds along the midrib: z = -.45 u^2 + .12 |x|, u = y / 1.8; vertex normals recomputed.</summary>
        public static Geo LeafBlade()
        {
            if (s_blade != null) return s_blade;
            var g = PrimitiveMeshes.Plane(1f, 1.8f, 3, 8).Translate(0f, 0.9f, 0f);
            for (int i = 0; i < g.P.Count; i++)
            {
                Vector3 p = g.P[i];
                float u = p.y / 1.8f;
                p.z = -0.45f * u * u + 0.12f * Mathf.Abs(p.x);
                g.P[i] = p;
            }
            g.ComputeVertexNormals();
            return s_blade = g;
        }

        public static PlantGeometry Build(in PlantOptions o, uint seed)
        {
            var rng = new Mulberry32(seed);
            float Rand(float a, float b) => a + (float)rng.Next() * (b - a);
            var r = new PlantGeometry();
            float pot = o.Pot;

            r.Pot = new Geo();
            r.Pot.Append(PrimitiveMeshes.Cylinder(pot, pot * 0.72f, pot * 1.5f, 24, 1), Rh.T(0f, pot * 0.75f, 0f));
            r.Pot.Append(PrimitiveMeshes.Torus(pot, pot * 0.07f, 8, 32), Rh.TRS(new Vector3(0f, pot * 1.5f, 0f), Rh.Euler(Mathf.PI / 2f, 0f, 0f)));
            float bas = pot * 1.5f - 0.012f;
            r.Soil = new Geo();
            r.Soil.Append(PrimitiveMeshes.Circle(pot * 0.94f, 24), Rh.TRS(new Vector3(0f, bas, 0f), Rh.Euler(-Mathf.PI / 2f, 0f, 0f)));

            r.Stems = new Geo();
            r.Leaves = new Geo();
            Geo blade = LeafBlade();
            for (int i = 0; i < o.Stems; i++)
            {
                float a = (float)i / o.Stems * Mathf.PI * 2f + Rand(-0.35f, 0.35f);
                float rad = o.Spread * Rand(0.55f, 1f);
                float h = o.Height * Rand(0.65f, 1f);
                float dx = Mathf.Cos(a) * rad, dz = Mathf.Sin(a) * rad;
                CatmullRom3 curve = o.Trailing
                    ? new CatmullRom3(new Vector3(0f, bas, 0f), new Vector3(dx * 0.5f, bas + 0.05f, dz * 0.5f), new Vector3(dx, bas - h * 0.45f, dz), new Vector3(dx * 1.15f, bas - h, dz * 1.15f))
                    : new CatmullRom3(new Vector3(0f, bas, 0f), new Vector3(dx * 0.35f, bas + h * 0.55f, dz * 0.35f), new Vector3(dx, bas + h, dz));
                r.Stems.Append(PrimitiveMeshes.Tube(curve, 10, o.Trailing ? 0.004f : 0.007f, 6));
                int n = o.Trailing ? o.LeavesPerStem * 2 : o.LeavesPerStem;
                for (int k = 0; k < n; k++)
                {
                    float t = o.Trailing ? 0.25f + 0.75f * ((float)k / Mathf.Max(1, n - 1)) : 0.5f + 0.5f * ((float)k / Mathf.Max(1, n - 1));
                    Vector3 p = curve.GetPoint(t);
                    float yaw = Mathf.Atan2(dx, dz) + Rand(-0.7f, 0.7f);
                    float pitch = (o.Trailing ? -1.0f : -o.Droop) + Rand(-0.3f, 0.3f);
                    float s = Rand(o.LeafMin, o.LeafMax) * (k == n - 1 ? 1f : 0.8f);
                    float roll = Rand(-0.4f, 0.4f);
                    Matrix4x4 m = Rh.TRS(p, Rh.Euler(Mathf.PI / 2f - pitch, yaw, roll, "YXZ"), new Vector3(s, s, s));
                    r.LeafMatrices.Add(m);
                    r.Leaves.Append(blade, m);
                }
            }
            r.LeafCount = r.LeafMatrices.Count;
            r.Phase = (float)rng.Next() * 6.28f;
            r.Amp = 0.012f + (float)rng.Next() * 0.01f;
            return r;
        }
    }

    /// <summary>The sill snow mound (spec 07 section 5.5): a soft mound along the outside sill, deepest against the
    /// glass, rounding over the outer lip, plus a skirt row. Built at full height; scale.y grows it with the cover. In the source
    /// frame it runs out along -z (Unity +z, outward).</summary>
    public static class SillMound
    {
        public const int Nx = 56, Nz = 9;

        public static Geo Build(float width, float depth, float height)
        {
            var g = new Geo();
            var rnd = new Mulberry32(31);
            var bumps = new float[9];
            for (int i = 0; i < 9; i++) bumps[i] = (float)rnd.Next();
            float Smooth(float x) => x <= 0f ? 0f : x >= 1f ? 1f : x * x * (3f - 2f * x);
            float Bump(float xn)
            {
                float f = xn * 8f;
                int i = Mathf.FloorToInt(f);
                float t = Smooth(f - i);
                return bumps[Mathf.Min(8, i)] * (1f - t) + bumps[Mathf.Min(8, i + 1)] * t;
            }
            for (int j = 0; j <= Nz + 1; j++)
                for (int i = 0; i <= Nx; i++)
                {
                    float xn = (float)i / Nx;
                    float ends = Smooth(xn / 0.05f) * Smooth((1f - xn) / 0.05f);
                    float y, z;
                    if (j <= Nz)
                    {
                        float zn = (float)j / Nz;
                        float prof = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(zn, 3f))) * 0.88f + 0.12f;
                        y = height * prof * ends * (0.82f + 0.36f * Bump(xn));
                        z = -zn * depth;
                    }
                    else
                    {
                        // A short skirt over the outer edge, so the mound has thickness from inside.
                        y = -0.012f * ends;
                        z = -depth - 0.008f;
                    }
                    g.P.Add(new Vector3((xn - 0.5f) * width, y, z));
                    g.N.Add(Vector3.up);
                    g.UV.Add(new Vector2(xn, (float)j / (Nz + 1)));
                }
            int row = Nx + 1;
            for (int j = 0; j <= Nz; j++)
                for (int i = 0; i < Nx; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    g.I.Add(a); g.I.Add(b); g.I.Add(c);
                    g.I.Add(b); g.I.Add(d); g.I.Add(c);
                }
            g.ComputeVertexNormals();
            return g;
        }
    }
}
