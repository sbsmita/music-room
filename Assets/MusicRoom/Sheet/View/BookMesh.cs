// The book's two meshes (sheet.md 2.1, 5.3, 7.3), in the book frame (BookGeometry):
//   case : dark green bookcloth boards, the cream page block's edges, the red silk ribbon (down the gutter, over the ledge,
//          its V-cut tail on the piano's top panel) and the small brass reading lamp clipped to the rest. One material.
//   paper: the two open pages (33 x 9 vertices each, the gutter curl, arc-length page mm per vertex) and three turning
//          leaves (front and back layers). The leaves' vertices are rewritten only while a turn runs (one
//          SetVertexBufferData, no allocation); at rest they collapse to a point (no pixels, no extra draw).
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Sheet
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PaperVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Page;   // x mm, y mm, role, 0
    }

    public static class BookMesh
    {
        public const int Leaves = 3;
        public const int VertsPerLayer = BookGeometry.Columns * BookGeometry.Rows;
        public const int LeafVertexStart = 2 * VertsPerLayer;
        public const int LeafVertexCount = Leaves * 2 * VertsPerLayer;
        public const float LeafEps = 0.0004f;    // a leaf lies this far above the page it rests on (no z-fighting)

        static readonly VertexAttributeDescriptor[] s_layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };

        // ------------------------------------------------------------------------------------ paper
        public static Mesh BuildPaper(float sBottom, out NativeArray<PaperVertex> leafVerts)
        {
            int total = 2 * VertsPerLayer + LeafVertexCount;
            var verts = new NativeArray<PaperVertex>(total, Allocator.Temp);
            for (int side = 0; side < 2; side++)
            {
                int sigma = side == 0 ? -1 : 1;
                for (int j = 0; j < BookGeometry.Rows; j++)
                    for (int i = 0; i < BookGeometry.Columns; i++)
                    {
                        float s = BookGeometry.ColumnArc(i), v = BookGeometry.PageH * j / (BookGeometry.Rows - 1);
                        float d = BookGeometry.FlatReach(s);
                        var mm = BookGeometry.PageMm(sigma, s, v);
                        verts[side * VertsPerLayer + j * BookGeometry.Columns + i] = new PaperVertex
                        {
                            Position = new Vector3(sigma * d, sBottom + v, -BookGeometry.Lift(d)),
                            Normal = BookGeometry.PageNormal(sigma, d),
                            Page = new Vector4(mm.x, mm.y, side, 0f),
                        };
                    }
            }
            leafVerts = new NativeArray<PaperVertex>(LeafVertexCount, Allocator.Persistent);
            for (int k = 0; k < Leaves; k++)
                for (int layer = 0; layer < 2; layer++)
                {
                    int sigma = layer == 0 ? 1 : -1;   // the front shows a right page, the back a left page
                    for (int j = 0; j < BookGeometry.Rows; j++)
                        for (int i = 0; i < BookGeometry.Columns; i++)
                        {
                            float s = BookGeometry.ColumnArc(i), v = BookGeometry.PageH * j / (BookGeometry.Rows - 1);
                            var mm = BookGeometry.PageMm(sigma, s, v);
                            leafVerts[(k * 2 + layer) * VertsPerLayer + j * BookGeometry.Columns + i] = new PaperVertex
                            {
                                Position = new Vector3(0f, sBottom, 0f),
                                Normal = new Vector3(0f, 0f, -1f),
                                Page = new Vector4(mm.x, mm.y, 2 + 2 * k + layer, 0f),
                            };
                        }
                }
            NativeArray<PaperVertex>.Copy(leafVerts, 0, verts, LeafVertexStart, LeafVertexCount);
            var idx = new List<int>(6 * (BookGeometry.Columns - 1) * (BookGeometry.Rows - 1) * 8);
            AddGrid(idx, 0, frontFacesPlayer: false);                    // the left page: mirrored x, so the other winding
            AddGrid(idx, VertsPerLayer, frontFacesPlayer: true);
            for (int k = 0; k < Leaves; k++)
            {
                AddGrid(idx, LeafVertexStart + (2 * k) * VertsPerLayer, frontFacesPlayer: true);       // front: as the right page
                AddGrid(idx, LeafVertexStart + (2 * k + 1) * VertsPerLayer, frontFacesPlayer: false);  // back: reversed
            }
            var mesh = new Mesh { name = "SheetPaper" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(total, s_layout);
            mesh.SetVertexBufferData(verts, 0, 0, total, 0, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetIndexBufferParams(idx.Count, IndexFormat.UInt16);
            var ib = new NativeArray<ushort>(idx.Count, Allocator.Temp);
            for (int i = 0; i < idx.Count; i++) ib[i] = (ushort)idx[i];
            mesh.SetIndexBufferData(ib, 0, 0, idx.Count, MeshUpdateFlags.DontRecalculateBounds);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Count), MeshUpdateFlags.DontRecalculateBounds);
            // bounds of the whole sweep of a leaf (it stands up to a page width toward the player)
            mesh.bounds = new Bounds(new Vector3(0f, sBottom + BookGeometry.PageH / 2f, -0.17f), new Vector3(0.68f, BookGeometry.PageH + 0.02f, 0.36f));
            verts.Dispose();
            ib.Dispose();
            return mesh;
        }

        /// <summary>Two triangles per cell. frontFacesPlayer: the winding for a page with +x to the player's right.</summary>
        static void AddGrid(List<int> idx, int b, bool frontFacesPlayer)
        {
            int C = BookGeometry.Columns;
            for (int j = 0; j < BookGeometry.Rows - 1; j++)
                for (int i = 0; i < C - 1; i++)
                {
                    int a = b + j * C + i, bb = a + 1, c = a + C + 1, d = a + C;
                    if (frontFacesPlayer) { idx.Add(a); idx.Add(d); idx.Add(c); idx.Add(a); idx.Add(c); idx.Add(bb); }
                    else { idx.Add(a); idx.Add(c); idx.Add(d); idx.Add(a); idx.Add(bb); idx.Add(c); }
                }
        }

        // ------------------------------------------------------------------------------------ case
        sealed class Builder
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<int> I = new List<int>();

            public int V(Vector3 p, Vector3 n, Vector2 uv) { P.Add(p); N.Add(n); U.Add(uv); return P.Count - 1; }

            /// <summary>A flat quad a b c d (in order round its edge) facing `toward`.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv, Vector3 toward)
            {
                var n = Vector3.Cross(b - a, d - a).normalized;
                if (Vector3.Dot(n, toward) < 0f) n = -n;
                int i0 = V(a, n, uv), i1 = V(b, n, uv), i2 = V(c, n, uv), i3 = V(d, n, uv);
                Tri(this, i0, i1, i2, n); Tri(this, i0, i2, i3, n);
            }

            /// <summary>An axis-aligned box in the book frame with a palette uv.</summary>
            public void Box(Vector3 min, Vector3 max, Vector2 uv)
            {
                Vector3 a = new Vector3(min.x, min.y, min.z), bb = new Vector3(max.x, min.y, min.z), c = new Vector3(max.x, max.y, min.z), d = new Vector3(min.x, max.y, min.z);
                Vector3 e = new Vector3(min.x, min.y, max.z), f = new Vector3(max.x, min.y, max.z), g = new Vector3(max.x, max.y, max.z), h = new Vector3(min.x, max.y, max.z);
                Quad(a, bb, c, d, uv, Vector3.back); Quad(f, e, h, g, uv, Vector3.forward); Quad(e, a, d, h, uv, Vector3.left);
                Quad(bb, f, g, c, uv, Vector3.right); Quad(d, c, g, h, uv, Vector3.up); Quad(e, f, bb, a, uv, Vector3.down);
            }

            /// <summary>A cylinder between p0 and p1 (radius r, seg sides, smooth), capped.</summary>
            public void Cylinder(Vector3 p0, Vector3 p1, float r, int seg, Vector2 uv)
            {
                Vector3 ax = (p1 - p0).normalized;
                Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ax, u);
                int b0 = P.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    Vector3 n = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                    V(p0 + n * r, n, uv); V(p1 + n * r, n, uv);
                }
                for (int i = 0; i < seg; i++)
                {
                    int a0 = b0 + 2 * i, a1 = a0 + 1, c0 = a0 + 2, c1 = a0 + 3;
                    Vector3 face = N[a0] + N[c0];
                    Tri(this, a0, a1, c1, face); Tri(this, a0, c1, c0, face);
                }
                Disc(p0, -ax, u, w, r, seg, uv);
                Disc(p1, ax, u, w, r, seg, uv);
            }

            void Disc(Vector3 c, Vector3 n, Vector3 u, Vector3 w, float r, int seg, Vector2 uv)
            {
                int ci = V(c, n, uv), b0 = P.Count;
                for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; V(c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * r, n, uv); }
                for (int i = 0; i < seg; i++) Tri(this, ci, b0 + i, b0 + i + 1, n);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (P.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(P); m.SetNormals(N); m.SetUVs(0, U); m.SetTriangles(I, 0, true);
                return m;
            }
        }

        /// <summary>Where the ribbon's tail lies (book frame): its last 30 mm, the poke capsule's segment.</summary>
        public static void RibbonTail(out Vector3 a, out Vector3 b)
        {
            a = BookGeometry.PianoToBook(new Vector3(0.004f, TopPanelY + 0.0006f, 0.205f));
            b = BookGeometry.PianoToBook(new Vector3(0.006f, TopPanelY + 0.0006f, 0.176f));
        }
        const float TopPanelY = 0.038f + 0.0016f;   // PianoBody.Panel's top surface (piano frame)

        /// <summary>The lamp tube (book frame): centre, half-length along x, and the direction its opening faces.</summary>
        public static readonly Vector3 LampCentre = new Vector3(0f, 0.300f, -0.090f);
        public const float LampHalfLength = 0.19f, LampShadeR = 0.021f;
        public static readonly Vector3 LampAxisOpen = new Vector3(0f, -0.86f, 0.51f).normalized;
        public const float LampStemX = -0.11f;

        public static Mesh BuildCase(float ledge)
        {
            var b = new Builder();
            float sBottom = ledge + BookGeometry.Square;
            float w = BookGeometry.WFlat + BookGeometry.Square, y0 = ledge, y1 = sBottom + BookGeometry.PageH + BookGeometry.Square;
            // ---- boards: one cloth slab under both pages (the spine's hump is behind, against the rest), corners bevelled
            const float bev = 0.0015f;
            Vector2 cloth = SheetTextures.Uv(SheetTextures.Swatch.Cloth), edge = SheetTextures.Uv(SheetTextures.Swatch.ClothEdge);
            float zf = -BookGeometry.Cover, zb = -0.0002f;
            // front face (toward the player): the visible squares around the pages
            Vector3 toP = Vector3.back;
            b.Quad(new Vector3(-w + bev, y0 + bev, zf), new Vector3(w - bev, y0 + bev, zf), new Vector3(w - bev, y1 - bev, zf), new Vector3(-w + bev, y1 - bev, zf), cloth, toP);
            // bevelled rims and edges
            b.Quad(new Vector3(-w, y0, zf + bev), new Vector3(w, y0, zf + bev), new Vector3(w - bev, y0 + bev, zf), new Vector3(-w + bev, y0 + bev, zf), edge, new Vector3(0f, -1f, -1f));
            b.Quad(new Vector3(-w + bev, y1 - bev, zf), new Vector3(w - bev, y1 - bev, zf), new Vector3(w, y1, zf + bev), new Vector3(-w, y1, zf + bev), edge, new Vector3(0f, 1f, -1f));
            b.Quad(new Vector3(-w, y1, zf + bev), new Vector3(-w, y0, zf + bev), new Vector3(-w + bev, y0 + bev, zf), new Vector3(-w + bev, y1 - bev, zf), edge, new Vector3(-1f, 0f, -1f));
            b.Quad(new Vector3(w, y0, zf + bev), new Vector3(w, y1, zf + bev), new Vector3(w - bev, y1 - bev, zf), new Vector3(w - bev, y0 + bev, zf), edge, new Vector3(1f, 0f, -1f));
            b.Quad(new Vector3(-w, y0, zb), new Vector3(w, y0, zb), new Vector3(w, y0, zf + bev), new Vector3(-w, y0, zf + bev), edge, Vector3.down);
            b.Quad(new Vector3(-w, y1, zf + bev), new Vector3(w, y1, zf + bev), new Vector3(w, y1, zb), new Vector3(-w, y1, zb), edge, Vector3.up);
            b.Quad(new Vector3(-w, y1, zb), new Vector3(-w, y0, zb), new Vector3(-w, y0, zf + bev), new Vector3(-w, y1, zf + bev), edge, Vector3.left);
            b.Quad(new Vector3(w, y0, zb), new Vector3(w, y1, zb), new Vector3(w, y1, zf + bev), new Vector3(w, y0, zf + bev), edge, Vector3.right);
            b.Quad(new Vector3(w, y0, zb), new Vector3(-w, y0, zb), new Vector3(-w, y1, zb), new Vector3(w, y1, zb), cloth, Vector3.forward);
            // ---- the page block: its bottom, top and outer fore-edges, between the board and the curled top page
            Vector2 pe = SheetTextures.Uv(SheetTextures.Swatch.PageEdge);
            int C = BookGeometry.Columns;
            float yTop = sBottom + BookGeometry.PageH;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < C - 1; i++)
                {
                    float da = BookGeometry.FlatReach(BookGeometry.ColumnArc(i)), db = BookGeometry.FlatReach(BookGeometry.ColumnArc(i + 1));
                    float xa = side * da, xb = side * db, la = -BookGeometry.Lift(da), lb = -BookGeometry.Lift(db);
                    b.Quad(new Vector3(xa, sBottom, zf), new Vector3(xb, sBottom, zf), new Vector3(xb, sBottom, lb), new Vector3(xa, sBottom, la), pe, Vector3.down);
                    b.Quad(new Vector3(xa, yTop, la), new Vector3(xb, yTop, lb), new Vector3(xb, yTop, zf), new Vector3(xa, yTop, zf), pe, Vector3.up);
                }
            float lo = -BookGeometry.Lift(BookGeometry.WFlat), wf = BookGeometry.WFlat;
            b.Quad(new Vector3(wf, sBottom, zf), new Vector3(wf, yTop, zf), new Vector3(wf, yTop, lo), new Vector3(wf, sBottom, lo), pe, Vector3.right);
            b.Quad(new Vector3(-wf, yTop, zf), new Vector3(-wf, sBottom, zf), new Vector3(-wf, sBottom, lo), new Vector3(-wf, yTop, lo), pe, Vector3.left);
            AddRibbon(b, sBottom, ledge);
            AddLamp(b);
            var mesh = b.ToMesh("SheetCase");
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The red silk ribbon: from the head of the spine down the gutter, out at the bottom, over the ledge and onto the
        /// piano's top panel toward the player, cut in a V. Its path in the book frame (pieces given in the piano frame).</summary>
        static void AddRibbon(Builder b, float sBottom, float ledge)
        {
            float g = -(BookGeometry.Lift(0f) + 0.0007f);
            var path = new List<Vector3>
            {
                new Vector3(0f, sBottom + BookGeometry.PageH + 0.002f, g),
                new Vector3(0f, sBottom + 0.004f, g),
                new Vector3(0.0003f, sBottom - 0.0005f, g - 0.0012f),
                new Vector3(0.0006f, ledge - 0.0012f, g - 0.0022f),
                BookGeometry.PianoToBook(new Vector3(0.0009f, 0.0466f, 0.229f)),
                BookGeometry.PianoToBook(new Vector3(0.0012f, 0.0466f, 0.2232f)),
                BookGeometry.PianoToBook(new Vector3(0.0016f, 0.0440f, 0.2222f)),
                BookGeometry.PianoToBook(new Vector3(0.0022f, TopPanelY + 0.0006f, 0.2180f)),
                BookGeometry.PianoToBook(new Vector3(0.0040f, TopPanelY + 0.0006f, 0.2050f)),
                BookGeometry.PianoToBook(new Vector3(0.0060f, TopPanelY + 0.0006f, 0.1760f)),
            };
            const float halfW = 0.003f;
            Vector2 red = SheetTextures.Uv(SheetTextures.Swatch.Ribbon);
            int n = path.Count;
            var left = new Vector3[n];
            var right = new Vector3[n];
            var normals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                Vector3 side = Vector3.right;
                Vector3 nrm = Vector3.Cross(side, t).normalized;   // the strip's face: toward the player / up
                if (Vector3.Dot(nrm, new Vector3(0f, 0.3f, -1f)) < 0f) nrm = -nrm;
                normals[i] = nrm;
                left[i] = path[i] - side * halfW;
                right[i] = path[i] + side * halfW;
            }
            // the V cut: the centre of the last edge retracts 6 mm
            Vector3 tail = path[n - 1], back = (path[n - 2] - path[n - 1]).normalized;
            for (int i = 0; i < n - 1; i++)
            {
                if (i == n - 2)
                {
                    Vector3 notch = tail + back * 0.006f;
                    int a0 = b.V(left[i], normals[i], red), a1 = b.V(right[i], normals[i], red), a2 = b.V(right[i + 1], normals[i + 1], red),
                        a3 = b.V(notch, normals[i + 1], red), a4 = b.V(left[i + 1], normals[i + 1], red);
                    Tri(b, a0, a1, a3, normals[i]); Tri(b, a1, a2, a3, normals[i]); Tri(b, a0, a3, a4, normals[i]);
                    continue;
                }
                int c0 = b.V(left[i], normals[i], red), c1 = b.V(right[i], normals[i], red), c2 = b.V(right[i + 1], normals[i + 1], red), c3 = b.V(left[i + 1], normals[i + 1], red);
                Tri(b, c0, c1, c2, normals[i]); Tri(b, c0, c2, c3, normals[i]);
            }
        }

        /// <summary>A triangle facing `toward` (Unity: clockwise as seen from that side).</summary>
        static void Tri(Builder b, int i0, int i1, int i2, Vector3 toward)
        {
            Vector3 n = Vector3.Cross(b.P[i1] - b.P[i0], b.P[i2] - b.P[i0]);
            if (Vector3.Dot(n, toward) > 0f) { b.I.Add(i0); b.I.Add(i1); b.I.Add(i2); }
            else { b.I.Add(i0); b.I.Add(i2); b.I.Add(i1); }
        }

        /// <summary>A small brass piano lamp: a felt clamp on the rest's top edge behind the book, a brass stem rising above the
        /// book, an arm reaching toward the player and a long green tube shade over the pages (its bulb hidden inside).</summary>
        static void AddLamp(Builder b)
        {
            Vector2 brass = SheetTextures.Uv(SheetTextures.Swatch.Brass), felt = SheetTextures.Uv(SheetTextures.Swatch.Clamp);
            Vector2 shade = SheetTextures.Uv(SheetTextures.Swatch.Shade), inside = SheetTextures.Uv(SheetTextures.Swatch.ShadeInside);
            const float restTop = 0.135f, x = LampStemX;
            // the clamp around the rest's top edge (the 5 mm acrylic board: book frame z 0 .. 0.005)
            b.Box(new Vector3(x - 0.011f, restTop - 0.016f, 0.0015f), new Vector3(x + 0.011f, restTop + 0.005f, 0.0125f), felt);
            b.Box(new Vector3(x - 0.012f, restTop - 0.004f, -0.0012f), new Vector3(x + 0.012f, restTop + 0.006f, 0.0128f), brass);
            // the stem: up behind the book, then the arm forward over its top edge
            Vector3 s0 = new Vector3(x, restTop + 0.006f, 0.0075f), s1 = new Vector3(x, 0.276f, 0.0075f), s2 = new Vector3(x, 0.296f, -0.010f);
            Vector3 s3 = new Vector3(x, LampCentre.y + 0.006f, LampCentre.z + 0.012f);
            b.Cylinder(s0, s1, 0.0028f, 12, brass);
            b.Cylinder(s1, s2, 0.0026f, 12, brass);
            b.Cylinder(s2, s3, 0.0025f, 12, brass);
            // the shade: a tube along x, closed on its top and front, open toward the pages (LampAxisOpen); cream inside
            const int seg = 18;
            float open = 70f * Mathf.Deg2Rad;                    // half-angle of the opening
            Vector3 axOpen = LampAxisOpen, axUp = Vector3.Cross(Vector3.right, axOpen).normalized;
            float x0 = LampCentre.x - LampHalfLength, x1 = LampCentre.x + LampHalfLength;
            for (int layer = 0; layer < 2; layer++)
            {
                float r = layer == 0 ? LampShadeR : LampShadeR - 0.0008f;
                Vector2 uv = layer == 0 ? shade : inside;
                int b0 = b.P.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = open + (2f * Mathf.PI - 2f * open) * i / seg;   // from one lip round the back to the other
                    Vector3 n = axOpen * Mathf.Cos(a) + axUp * Mathf.Sin(a);
                    Vector3 nn = layer == 0 ? n : -n;
                    b.V(new Vector3(x0, LampCentre.y, LampCentre.z) + n * r, nn, uv);
                    b.V(new Vector3(x1, LampCentre.y, LampCentre.z) + n * r, nn, uv);
                }
                for (int i = 0; i < seg; i++)
                {
                    int a0 = b0 + 2 * i, a1 = a0 + 1, c0 = a0 + 2, c1 = a0 + 3;
                    Vector3 face = layer == 0 ? (b.N[a0] + b.N[c0]) : (b.N[a0] + b.N[c0]);
                    Tri(b, a0, a1, c1, face); Tri(b, a0, c1, c0, face);
                }
            }
            // end caps: brass discs closing the tube's ends
            for (int e = 0; e < 2; e++)
            {
                float xe = e == 0 ? x0 - 0.0006f : x1 + 0.0006f;
                Vector3 c = new Vector3(xe, LampCentre.y, LampCentre.z), nrm = e == 0 ? Vector3.left : Vector3.right;
                int ci = b.V(c, nrm, brass), b0 = b.P.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = open + (2f * Mathf.PI - 2f * open) * i / seg;
                    b.V(c + (axOpen * Mathf.Cos(a) + axUp * Mathf.Sin(a)) * (LampShadeR + 0.0012f), nrm, brass);
                }
                for (int i = 0; i < seg; i++) Tri(b, ci, b0 + i, b0 + i + 1, nrm);
                // the same cap seen from inside
                int ci2 = b.V(c, -nrm, brass), b2 = b.P.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = open + (2f * Mathf.PI - 2f * open) * i / seg;
                    b.V(c + (axOpen * Mathf.Cos(a) + axUp * Mathf.Sin(a)) * (LampShadeR + 0.0012f), -nrm, brass);
                }
                for (int i = 0; i < seg; i++) Tri(b, ci2, b2 + i, b2 + i + 1, -nrm);
            }
            // a brass rim along both lips of the opening
            for (int lip = -1; lip <= 1; lip += 2)
            {
                float a = lip < 0 ? open : 2f * Mathf.PI - open;
                Vector3 off = (axOpen * Mathf.Cos(a) + axUp * Mathf.Sin(a)) * LampShadeR;
                b.Cylinder(new Vector3(x0, LampCentre.y, LampCentre.z) + off, new Vector3(x1, LampCentre.y, LampCentre.z) + off, 0.0012f, 8, brass);
            }
        }

        /// <summary>The bulb's tube (the analytic light): world centre and half axis for the paper shader.</summary>
        public static void LampWorld(Transform book, out Vector3 centre, out Vector3 halfAxis, out Vector3 dir)
        {
            centre = book.TransformPoint(LampCentre + LampAxisOpen * 0.004f);
            halfAxis = book.TransformVector(new Vector3(LampHalfLength * 0.9f, 0f, 0f));
            dir = book.TransformDirection(LampAxisOpen).normalized;
        }
    }
}
