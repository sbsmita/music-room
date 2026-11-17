// Window curtains (10-upgrade room.md 4.3 / 12): two gathered panels hang open beside the window, floor to a brass
// rod, clear of the deep sill (the numbers below were the upgraded room's acoustic model's, deleted 2026-11-11 with the
// classic sound the only one). The pleats are geometry (a sine across the panel, deeper at the hem, both faces
// lit); the panels join the bench cushion's fabric batch and the rod the brass batch, so the room keeps its renderer
// count (spec 07 section 11 budget: 27 static, 34 drawing) and adds about 2.4 k triangles.
using MusicRoom.Room.Geometry;
using UnityEngine;

namespace MusicRoom.Room.World
{
    public static class Curtains
    {
        public const int Columns = 40, Rows = 6;
        /// <summary>Pleats per panel and their half-depth at the rod and at the hem (m).</summary>
        public const float Pleats = 5f, DepthTop = 0.018f, DepthHem = 0.032f;
        /// <summary>The panels' mean plane and the rod, in from the right wall (m).</summary>
        public const float WallInset = 0.075f, RodInset = 0.085f, RodY = 2.66f, RodRadius = 0.012f, RodOverhang = 0.06f;
        /// <summary>Panel width, gap to the window, top and bottom (m); the window's z span in the source frame (right wall).</summary>
        public const double PanelWidth = 0.40, Gap = 0.16, Top = 2.62, Bottom = 0.02;
        public const double WindowZ0 = -2.08, WindowZ1 = -0.28;
        /// <summary>The panels' near edges (source-frame z): left of the window and right of it.</summary>
        public const double LeftZ0 = WindowZ0 - Gap - PanelWidth, RightZ0 = WindowZ1 + Gap;

        /// <summary>Append both panels to fabric and the rod (with finials and brackets) to brass, in the source frame.</summary>
        public static void Build(Geo fabric, Geo brass)
        {
            float x = RoomDims.WallX - WallInset;
            float y0 = (float)Bottom, y1 = (float)Top, w = (float)PanelWidth;
            // Unity z -> source-frame z is a sign flip; the panels in the source frame
            float l0 = -(float)(LeftZ0 + w), r0 = -(float)(RightZ0 + w);
            fabric.Append(Panel(x, l0, w, y0, y1, 0.3f));
            fabric.Append(Panel(x, r0, w, y0, y1, 1.7f));
            float zA = Mathf.Min(l0, r0) - RodOverhang, zB = Mathf.Max(l0, r0) + w + RodOverhang, rx = RoomDims.WallX - RodInset;
            // the rod along source-frame z: a y cylinder turned onto z
            brass.Append(PrimitiveMeshes.Cylinder(RodRadius, RodRadius, zB - zA, 12), Rh.TRS(new Vector3(rx, RodY, (zA + zB) / 2f), Rh.Euler(Mathf.PI / 2f, 0f, 0f)));
            foreach (float z in new[] { zA, zB })
            {
                brass.Append(PrimitiveMeshes.Sphere(0.022f, 12, 8), Rh.T(rx, RodY, z + (z == zA ? -0.018f : 0.018f)));
                brass.Append(PrimitiveMeshes.Cylinder(0.006f, 0.006f, RodInset, 8), Rh.TRS(new Vector3(RoomDims.WallX - RodInset / 2f, RodY, z + (z == zA ? 0.03f : -0.03f)), Rh.Euler(0f, 0f, Mathf.PI / 2f)));
            }
        }

        /// <summary>One pleated panel in the plane x = x0 (+- the pleat depth), source-frame z in [z0, z0 + w], y in [y0, y1], its front
        /// facing the room (-x); phase: where the sine starts, so the two panels differ.</summary>
        static Geo Panel(float x0, float z0, float w, float y0, float y1, float phase)
        {
            var g = new Geo();
            float k = 2f * Mathf.PI * Pleats / w;
            for (int r = 0; r <= Rows; r++)
            {
                float t = r / (float)Rows, y = Mathf.Lerp(y0, y1, t);
                float a = Mathf.Lerp(DepthHem, DepthTop, Mathf.Sqrt(t));   // the folds open toward the hem
                for (int c = 0; c <= Columns; c++)
                {
                    float u = c / (float)Columns, z = z0 + u * w, s = k * (z - z0) + phase;
                    float x = x0 + a * Mathf.Sin(s), dxdz = a * k * Mathf.Cos(s);
                    g.P.Add(new Vector3(x, y, z));
                    g.N.Add(new Vector3(-1f, 0f, dxdz).normalized);
                    g.UV.Add(new Vector2(u, t));
                }
            }
            int stride = Columns + 1;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                {
                    int a = r * stride + c, b = a + 1, d = a + stride, e = d + 1;
                    // counter-clockwise seen from the room (looking along +x, +z is to the right)
                    g.I.Add(a); g.I.Add(b); g.I.Add(d);
                    g.I.Add(b); g.I.Add(e); g.I.Add(d);
                }
            return g.DoubleSided();
        }
    }
}
