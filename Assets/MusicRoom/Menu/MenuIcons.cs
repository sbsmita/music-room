// The row icons, drawn in a box of size s with its top-left at (x, y), stroke width
// max(4, .07 s), round caps and joins. Every coordinate below is in a 100-unit box. Since the
// many-instrument platform (platform.md 5.2) the menu names no instrument: a tile whose portrait cannot be rendered
// draws its descriptor's vector icon (PortraitSpec.Fallback, IconStrokes in the same 100-unit box) with these rules
// (DrawStrokes). The three classic icons stay for anything that still asks for them by IconKind.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public static class MenuIcons
    {
        /// <summary>The quadratic harp neck is drawn as this many segments (round joins make it smooth).</summary>
        public const int CurveSegments = 10;

        public static void Draw(IMenuCanvas c, IconKind kind, float x, float y, float s, Color col)
        {
            float u = s / 100f;
            float lw = Mathf.Max(4f, s * 0.07f);
            switch (kind)
            {
                case IconKind.Piano:
                    c.StrokeRoundRect(x + 6 * u, y + 22 * u, 88 * u, 56 * u, 8 * u, lw, col);
                    for (int i = 1; i < 7; i++) L(c, x, y, u, 6 + i * 12.6f, 50, 6 + i * 12.6f, 78, lw, col);
                    for (int k = 0; k < 5; k++)
                    {
                        int i = k < 2 ? k + 1 : k + 2;   // 1, 2, 4, 5, 6
                        c.FillRoundRect(x + (6 + i * 12.6f - 4) * u, y + 22 * u, 8 * u, 30 * u, 0f, col);
                    }
                    break;
                case IconKind.Drums:
                    c.StrokeEllipse(x + 50 * u, y + 42 * u, 36 * u, 16 * u, lw, col);
                    L(c, x, y, u, 14, 42, 14, 72, lw, col);
                    c.StrokeEllipse(x + 50 * u, y + 72 * u, 36 * u, 16 * u, lw, col, -1);   // the shell's lower rim
                    L(c, x, y, u, 86, 72, 86, 42, lw, col);
                    L(c, x, y, u, 30, 6, 60, 36, lw, col);
                    L(c, x, y, u, 72, 6, 42, 36, lw, col);
                    break;
                case IconKind.Harp:
                {
                    L(c, x, y, u, 18, 92, 18, 14, lw, col);
                    // quadraticCurveTo(50, 0, 84, 22) from (18, 14)
                    float px = 18, py = 14;
                    for (int i = 1; i <= CurveSegments; i++)
                    {
                        float t = i / (float)CurveSegments, a = (1 - t) * (1 - t), b = 2 * (1 - t) * t, d = t * t;
                        float qx = a * 18 + b * 50 + d * 84, qy = a * 14 + b * 0 + d * 22;
                        L(c, x, y, u, px, py, qx, qy, lw, col);
                        px = qx; py = qy;
                    }
                    L(c, x, y, u, 84, 22, 84, 92, lw, col);
                    L(c, x, y, u, 84, 92, 18, 92, lw, col);
                    float sw = Mathf.Max(2f, s * 0.03f);
                    for (int i = 1; i < 5; i++)
                    {
                        float xx = 18 + i * 13.2f;
                        L(c, x, y, u, xx, 14 + i * 1.5f, xx, 92, sw, col);
                    }
                    break;
                }
                case IconKind.Recordings:
                    for (int k = 0; k < 2; k++)
                    {
                        float cx = k == 0 ? 30 : 70;
                        c.StrokeRoundRect(x + (cx - 20) * u, y + 24 * u, 40 * u, 40 * u, 20 * u, lw, col);
                        c.FillRoundRect(x + (cx - 6) * u, y + 38 * u, 12 * u, 12 * u, 6 * u, col);
                    }
                    L(c, x, y, u, 14, 64, 24, 84, lw, col);
                    L(c, x, y, u, 24, 84, 76, 84, lw, col);
                    L(c, x, y, u, 76, 84, 86, 64, lw, col);
                    break;
                case IconKind.Instruments:
                    c.StrokeRoundRect(x + 8 * u, y + 8 * u, 38 * u, 38 * u, 8 * u, lw, col);
                    c.StrokeRoundRect(x + 54 * u, y + 8 * u, 38 * u, 38 * u, 8 * u, lw, col);
                    c.StrokeRoundRect(x + 8 * u, y + 54 * u, 38 * u, 38 * u, 8 * u, lw, col);
                    c.StrokeRoundRect(x + 54 * u, y + 54 * u, 38 * u, 38 * u, 8 * u, lw, col);
                    c.FillRoundRect(x + 64 * u, y + 64 * u, 18 * u, 18 * u, 9 * u, col);
                    break;
                case IconKind.Lessons:
                    c.StrokeRoundRect(x + 12 * u, y + 64 * u, 76 * u, 24 * u, 6 * u, lw, col);
                    c.FillRoundRect(x + 36 * u, y + 14 * u, 28 * u, 36 * u, 6 * u, MenuStyle.WithAlpha(col, col.a * 0.9f));
                    L(c, x, y, u, 50, 52, 50, 62, lw, col);
                    break;
                case IconKind.Play:
                    c.FillTriangle(new Vector2(x + 30 * u, y + 18 * u), new Vector2(x + 82 * u, y + 50 * u), new Vector2(x + 30 * u, y + 82 * u), col);
                    break;
                case IconKind.Stop:
                    c.FillRoundRect(x + 24 * u, y + 24 * u, 52 * u, 52 * u, 8 * u, col);
                    break;
                case IconKind.Delete:
                    L(c, x, y, u, 22, 28, 78, 28, lw, col);
                    L(c, x, y, u, 40, 16, 60, 16, lw, col);
                    L(c, x, y, u, 30, 28, 34, 86, lw, col);
                    L(c, x, y, u, 34, 86, 66, 86, lw, col);
                    L(c, x, y, u, 66, 86, 70, 28, lw, col);
                    break;
                case IconKind.Back:
                    L(c, x, y, u, 62, 22, 34, 50, lw, col);
                    L(c, x, y, u, 34, 50, 62, 78, lw, col);
                    break;
                case IconKind.Lock:
                case IconKind.Unlock:
                {
                    // A padlock: the body, and a shackle that is closed or swung open to the right.
                    c.FillRoundRect(x + 22 * u, y + 46 * u, 56 * u, 42 * u, 8 * u, col);
                    bool locked = kind == IconKind.Lock;
                    float acx = locked ? 50 : 70, acy = locked ? 40 : 36;
                    c.StrokeEllipse(x + acx * u, y + acy * u, 17 * u, 17 * u, lw, col, +1);
                    L(c, x, y, u, 33, 40, 33, 46, lw, col);
                    if (locked) L(c, x, y, u, 67, 40, 67, 46, lw, col);
                    else L(c, x, y, u, 53, 36, 53, 44, lw, col);
                    break;
                }
                case IconKind.Move:
                    // Four arrows out of the middle: "move it anywhere".
                    L(c, x, y, u, 50, 12, 50, 88, lw, col);
                    L(c, x, y, u, 12, 50, 88, 50, lw, col);
                    L(c, x, y, u, 38, 24, 50, 12, lw, col); L(c, x, y, u, 62, 24, 50, 12, lw, col);
                    L(c, x, y, u, 38, 76, 50, 88, lw, col); L(c, x, y, u, 62, 76, 50, 88, lw, col);
                    L(c, x, y, u, 24, 38, 12, 50, lw, col); L(c, x, y, u, 24, 62, 12, 50, lw, col);
                    L(c, x, y, u, 76, 38, 88, 50, lw, col); L(c, x, y, u, 76, 62, 88, 50, lw, col);
                    break;
                case IconKind.Exit:
                    // A door frame and an arrow leaving it.
                    L(c, x, y, u, 54, 26, 54, 14, lw, col); L(c, x, y, u, 54, 14, 16, 14, lw, col);
                    L(c, x, y, u, 16, 14, 16, 86, lw, col); L(c, x, y, u, 16, 86, 54, 86, lw, col);
                    L(c, x, y, u, 54, 86, 54, 74, lw, col);
                    L(c, x, y, u, 36, 50, 88, 50, lw, col);
                    L(c, x, y, u, 74, 36, 88, 50, lw, col); L(c, x, y, u, 74, 64, 88, 50, lw, col);
                    break;
                case IconKind.House:
                    L(c, x, y, u, 10, 50, 50, 14, lw, col); L(c, x, y, u, 50, 14, 90, 50, lw, col);
                    c.StrokeRoundRect(x + 22 * u, y + 46 * u, 56 * u, 42 * u, 4 * u, lw, col);
                    c.FillRoundRect(x + 42 * u, y + 62 * u, 16 * u, 26 * u, 2 * u, col);
                    break;
                case IconKind.Check:
                    L(c, x, y, u, 16, 52, 40, 76, lw * 1.4f, col);
                    L(c, x, y, u, 40, 76, 86, 26, lw * 1.4f, col);
                    break;
                case IconKind.Undo:
                    // A curved arrow turning back: the upper half of a circle with its head at the left end.
                    c.StrokeEllipse(x + 52 * u, y + 56 * u, 32 * u, 32 * u, lw, col, +1);
                    L(c, x, y, u, 84, 56, 84, 70, lw, col);
                    L(c, x, y, u, 20, 56, 8, 42, lw, col); L(c, x, y, u, 20, 56, 32, 44, lw, col);
                    break;
                // Guide, Room, Setup: the 'guide' icon draws nothing; its space is kept.
            }
        }

        /// <summary>A descriptor's fallback icon (PortraitSpec.Fallback) in a box of size s at (x, y), with the icons' stroke
        /// rules: width max(4, .07 s), round caps (a segment per pair of points, so joins are round too); a Closed stroke
        /// returns to its first point; a Fill stroke is filled as a fan from its first point (convex outlines). Returns the
        /// shapes drawn (0 for no strokes).</summary>
        public static int DrawStrokes(IMenuCanvas c, IconStroke[] strokes, float x, float y, float s, Color col)
        {
            if (strokes == null) return 0;
            float u = s / 100f;
            float lw = Mathf.Max(4f, s * 0.07f);
            int n = 0;
            for (int k = 0; k < strokes.Length; k++)
            {
                Vector2[] p = strokes[k].Points;
                if (p == null || p.Length == 0) continue;
                if (strokes[k].Fill && p.Length >= 3)
                {
                    var a = new Vector2(x + p[0].x * u, y + p[0].y * u);
                    for (int i = 1; i + 1 < p.Length; i++)
                    {
                        c.FillTriangle(a, new Vector2(x + p[i].x * u, y + p[i].y * u), new Vector2(x + p[i + 1].x * u, y + p[i + 1].y * u), col);
                        n++;
                    }
                    continue;
                }
                if (p.Length == 1) { L(c, x, y, u, p[0].x, p[0].y, p[0].x, p[0].y, lw, col); n++; continue; }
                for (int i = 0; i + 1 < p.Length; i++) { L(c, x, y, u, p[i].x, p[i].y, p[i + 1].x, p[i + 1].y, lw, col); n++; }
                if (strokes[k].Closed && p.Length > 2) { L(c, x, y, u, p[p.Length - 1].x, p[p.Length - 1].y, p[0].x, p[0].y, lw, col); n++; }
            }
            return n;
        }

        static void L(IMenuCanvas c, float x, float y, float u, float x0, float y0, float x1, float y1, float lw, Color col) =>
            c.Line(x + x0 * u, y + y0 * u, x + x1 * u, y + y1 * u, lw, col);
    }
}
