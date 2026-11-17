// The panel's redraw() and its draw* helpers, onto an IMenuCanvas: the frosted face, the
// header with its accent bar, the rows with their "on" state, icons, lists with their scrollbar, and each screen's
// body. It reads only the controller (whose LayoutRows captured what the services showed), so a repaint touches no
// service and builds no string. The hover highlight is not painted: HoverShape gives its geometry to the face shader
// (ARCHITECTURE.md 15.6). Also paints the back plate's 512 x 384 canvas. The Instruments and Lessons screens are tile
// grids (platform.md 5.1, 5.3): printed cards (the panel's cream ink as their paper, its dark face colour as their
// ink), each with its instrument's portrait (the studio's atlas) or, without one, the descriptor's line drawing; no
// glow anywhere.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public static class MenuPainter
    {
        /// <summary>The back plate: a dark rounded slab with a warm glow and rim.</summary>
        public static void PaintBack(IMenuCanvas c)
        {
            float m = MenuStyle.BackMargin, bw = MenuStyle.BackW, bh = MenuStyle.BackH;
            c.ShadowRoundRect(m, m, bw - 2 * m, bh - 2 * m, MenuStyle.BackRadius, MenuStyle.BackGlowBlur, MenuStyle.BackGlow);
            c.FillRoundRect(m, m, bw - 2 * m, bh - 2 * m, MenuStyle.BackRadius, MenuStyle.BackFill);
            float i = MenuStyle.BackRimInset;
            c.StrokeRoundRect(m + i, m + i, bw - 2 * m - 2 * i, bh - 2 * m - 2 * i, MenuStyle.BackRimRadius, MenuStyle.BackRimWidth, MenuStyle.BackRim);
        }

        /// <summary>redraw(now): the face, then the current screen. The hover highlight is not painted here: the face
        /// shader draws it from HoverShape (so <paramref name="nowMs"/> is kept only for the redraw(now) signature).</summary>
        public static void Paint(IMenuCanvas c, MenuController m, double nowMs)
        {
            float cw = MenuStyle.CW, ch = MenuStyle.CH, rad = MenuStyle.Corner;
            // The frosted face: a dark gradient, a soft sheen at the top, a thin warm rim.
            c.FillRoundRect(0f, 0f, cw, ch, rad, MenuGradient.Vertical3(0f, ch, MenuStyle.FaceTop, MenuStyle.FaceMidStop, MenuStyle.FaceMid, MenuStyle.FaceBottom));
            c.FillRoundRect(0f, 0f, cw, ch, rad, MenuGradient.Vertical(0f, MenuStyle.SheenH, MenuStyle.SheenTop, MenuStyle.SheenBottom));
            c.StrokeRoundRect(2f, 2f, cw - 4f, ch - 4f, rad - 2f, 3f, MenuStyle.InnerRim);
            switch (m.Screen)
            {
                case MenuScreen.Home: DrawHome(c, m); break;
                case MenuScreen.Start: DrawStart(c, m); break;
                case MenuScreen.Arrange: DrawArrange(c, m); break;
                case MenuScreen.Lessons: DrawLessons(c, m); break;
                case MenuScreen.Courses: DrawCourses(c, m); break;
                case MenuScreen.Instruments: DrawInstruments(c, m); break;
                case MenuScreen.Recordings: DrawRecordings(c, m); break;
                case MenuScreen.Take: DrawTake(c, m); break;
                case MenuScreen.Guide: DrawGuide(c, m); break;         // first five: the Sunny screen (header, rows, footer)
                case MenuScreen.Tips: DrawCourses(c, m); break;        // first five: the Tips list (header, list)
                default: DrawLesson(c, m); break;
            }
        }

        /// <summary>The hover highlight of row <paramref name="i"/> as the face shader draws it (ARCHITECTURE.md 15.6:
        /// hover is a uniform of the face material, never a repaint): drawRow's highlight (fill .22k + 4 px
        /// stroke at alpha k, inset 2 px, radius min(28, h/2)) or, for the back row of a sub-screen, drawHeader's fill
        /// (6, 6, CW-12, HEADER-6, radius CORNER-6, no stroke). Rows of a scrolling list are clipped to the list, like
        /// DrawList. rect = canvas px (x0, y0, x1, y1); shape = (radius, stroke 0/1); clip = (y0, y1).</summary>
        public static bool HoverShape(MenuController m, int i, out Vector4 rect, out Vector2 shape, out Vector2 clip)
        {
            clip = new Vector2(-1e5f, 1e5f);
            if (i < 0 || i >= m.RowCount) { rect = default; shape = default; return false; }
            if (i == 0 && m.HeaderBack)
            {
                rect = new Vector4(6f, 6f, MenuStyle.CW - 6f, MenuStyle.Header);
                shape = new Vector2(MenuStyle.Corner - 6f, 0f);
                return true;
            }
            MenuRow r = m.Row(i);
            rect = new Vector4(r.X0, r.Y0, r.X1, r.Y1);
            float radius = IsTile(m, r) ? MenuStyle.TileRadius : r.Id == RowKind.Options ? MenuStyle.ChipH / 2f
                : IsBarRow(m, r) ? MenuStyle.BarRadius : Mathf.Min(28f, (r.Y1 - r.Y0) / 2f);
            shape = new Vector2(radius, 1f);
            if (IsListRow(m, i)) clip = new Vector2(MenuStyle.ListTop, MenuStyle.ListBottom);
            return true;
        }

        /// <summary>Row i is drawn inside DrawList's clip (courses, or a recordings screen that lists takes).</summary>
        public static bool IsListRow(MenuController m, int i) =>
            i >= 1 && (m.Screen == MenuScreen.Courses || m.Screen == MenuScreen.Tips || (m.Screen == MenuScreen.Recordings && m.BodyTitle == null));

        /// <summary>A tile of the Instruments or Lessons grid.</summary>
        public static bool IsTile(MenuController m, MenuRow r) =>
            r.Id == RowKind.Tile || (r.Id == RowKind.Inst && m.Screen == MenuScreen.Lessons);

        /// <summary>A control of the Instruments grid's bottom row (My set | One alone, Move, and Real hands in mixed reality).</summary>
        static bool IsBarRow(MenuController m, MenuRow r) =>
            m.Screen == MenuScreen.Instruments && !m.OnOptions && (r.Id == RowKind.Layout || r.Id == RowKind.Arrange || r.Id == RowKind.Hands);

        /// <summary>Title at the left, a note at the right, the screen's accent bar beneath. On a sub-screen the whole
        /// header is the back row, with its chevron.</summary>
        static void DrawHeader(IMenuCanvas c, MenuController m, TextFit noteFit = TextFit.Ellipsis)
        {
            Color accent = MenuStyle.Accent(m.Screen);
            bool back = m.HeaderBack;
            float titleMax = MenuStyle.CW - 420f;
            c.Text(m.HeaderTitle, 48f, MenuStyle.HeaderTextY, 52f, true, back ? MenuStyle.Cyan : MenuStyle.Ink, TextAlign.Left, titleMax);
            if (!string.IsNullOrEmpty(m.HeaderNote))
            {
                // A fixed 400 px fit cut "Courses  ·  drag to scroll" short, so the note may also use the room
                // a short title leaves (never less than 400 px, never closer than 40 px to the title).
                float titleW = Mathf.Min(c.MeasureText(m.HeaderTitle, 52f, true), titleMax);
                float noteMax = Mathf.Max(400f, MenuStyle.CW - 96f - titleW - 40f);
                c.Text(m.HeaderNote, MenuStyle.CW - 48f, MenuStyle.HeaderTextY, 40f, false, MenuStyle.Dim, TextAlign.Right, noteMax, noteFit);
            }
            c.FillRoundRect(48f, MenuStyle.BarY, MenuStyle.CW - 96f, MenuStyle.BarH, 3f, MenuGradient.HorizontalOf(48f, MenuStyle.CW - 48f, accent, MenuStyle.BarEnd));
        }

        static void DrawRow(IMenuCanvas c, MenuController m, int i, Color color, float size)
        {
            MenuRow r = m.Row(i);
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            float rad = Mathf.Min(28f, h / 2f);
            // Exit asking for its second poke is "on" in red, not the amber of a chosen row.
            bool warn = r.Id == RowKind.Exit && r.On;
            c.FillRoundRect(r.X0, r.Y0, w, h, rad, r.On ? MenuStyle.RowOnFill : MenuStyle.RowFill);
            if (r.On) c.StrokeRoundRect(r.X0 + 1.5f, r.Y0 + 1.5f, w - 3f, h - 3f, rad - 1f, 3f, warn ? MenuStyle.WithAlpha(MenuStyle.Red, 0.6f) : MenuStyle.RowOnStroke);
            float cy = r.CentreY;
            float tx = r.X0 + 36f;
            if (r.Icon != IconKind.None)
            {
                float s = Mathf.Min(100f, h - 28f);
                MenuIcons.Draw(c, r.Icon, r.X0 + 34f, cy - s / 2f, s, r.On && !warn ? MenuStyle.Amber : color);
                tx = r.X0 + 34f + s + 36f;
            }
            c.Text(r.Label, tx, cy, size, true, color, TextAlign.Left, string.IsNullOrEmpty(r.Note) ? r.X1 - tx - 36f : r.X1 - tx - 330f);
            if (!string.IsNullOrEmpty(r.Note))
                c.Text(r.Note, r.X1 - 36f, cy, NoteSize(r), r.On, r.On ? MenuStyle.Amber : MenuStyle.Dim, TextAlign.Right, 0f, TextFit.None);
        }

        /// <summary>The 42 px note, smaller on the denser home pages (rows under 70 px).</summary>
        public static float NoteSize(MenuRow r) => Mathf.Min(42f, Mathf.Round(r.Height * 0.6f));

        /// <summary>The colour of a home row's label and icon: Exit turns red once it asks to be poked again.</summary>
        static Color HomeColour(MenuController m, MenuRow r) =>
            r.Id == RowKind.Exit && (m.ExitArmed || m.Quitting) ? MenuStyle.Red : MenuStyle.Ink;

        static void Footer(IMenuCanvas c, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            c.Text(text, MenuStyle.CW / 2f, MenuStyle.FooterY, 38f, false, color, TextAlign.Center, MenuStyle.CW - 48f, TextFit.Shrink);
        }

        /// <summary>The home label size: 64 px, shrinking by 2 to 44 until it fits beside its note.</summary>
        public static float HomeLabelSize(IMenuCanvas c, MenuRow r)
        {
            float avail = r.X1 - (r.X0 + 34f + Mathf.Min(100f, r.Height - 28f) + 36f) - (string.IsNullOrEmpty(r.Note) ? 36f : 330f);
            // The denser home pages (7 or 8 rows since Move instruments and Exit) start smaller so a label fits its row.
            float size = Mathf.Min(64f, 2f * Mathf.Floor(r.Height * 0.78f / 2f));
            float min = Mathf.Min(44f, size - 8f);
            for (; size > min; size -= 2f)
                if (c.MeasureText(r.Label, size, true) <= avail) break;
            return size;
        }

        static void DrawHome(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            for (int i = 0; i < m.RowCount; i++) DrawRow(c, m, i, HomeColour(m, m.Row(i)), HomeLabelSize(c, m.Row(i)));
            Footer(c, m.Footer, m.FooterColor);
        }

        /// <summary>The start panel: the app's name, a question, and one big row per way to play.</summary>
        static void DrawStart(IMenuCanvas c, MenuController m)
        {
            float cx = MenuStyle.CW / 2f;
            c.Text(m.HeaderTitle, cx, MenuStyle.StartTitleY, 84f, true, MenuStyle.Ink, TextAlign.Center, MenuStyle.CW - 96f, TextFit.Shrink);
            c.Text(MenuStyle.StartLine, cx, MenuStyle.StartLineY, 44f, false, MenuStyle.Dim, TextAlign.Center, MenuStyle.CW - 96f, TextFit.Shrink);
            for (int i = 0; i < m.RowCount; i++) DrawRow(c, m, i, m.Row(i).Id == RowKind.PlayHere ? MenuStyle.Green : MenuStyle.Cyan, 72f);
            Footer(c, m.Footer, m.FooterColor);
        }

        /// <summary>The Done / Reset buttons of "Move instruments": the title, two lines of how-to, two big buttons.</summary>
        static void DrawArrange(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            float cx = MenuStyle.CW / 2f;
            c.Text(MenuStyle.ArrangeLine1, cx, MenuStyle.ArrangeLine1Y, 50f, false, MenuStyle.Ink, TextAlign.Center, MenuStyle.CW - 64f, TextFit.Shrink);
            c.Text(MenuStyle.ArrangeLine2, cx, MenuStyle.ArrangeLine2Y, 50f, false, MenuStyle.Dim, TextAlign.Center, MenuStyle.CW - 64f, TextFit.Shrink);
            for (int i = 0; i < m.RowCount; i++)
            {
                MenuRow r = m.Row(i);
                Color col = r.Id == RowKind.Done ? MenuStyle.Green : MenuStyle.Ink;
                float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
                c.FillRoundRect(r.X0, r.Y0, w, h, 40f, MenuStyle.RowFill);
                c.StrokeRoundRect(r.X0 + 2f, r.Y0 + 2f, w - 4f, h - 4f, 38f, 4f, MenuStyle.WithAlpha(col, 0.55f));
                const float s = 150f;
                MenuIcons.Draw(c, r.Icon, (r.X0 + r.X1) / 2f - s / 2f, r.Y0 + 46f, s, col);
                c.Text(r.Label, (r.X0 + r.X1) / 2f, r.Y1 - 92f, 96f, true, col, TextAlign.Center, w - 40f, TextFit.Shrink);
            }
        }

        /// <summary>The Sunny screen: the header, its four rows (Show me around, Tips, Make a song in layers, Tips while I
        /// play: On in amber) and the footer. It was painted as the Instruments grid, which draws only tiles and the grid's
        /// bottom row: on the headset the screen showed its header and nothing else.</summary>
        static void DrawGuide(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            for (int i = 1; i < m.RowCount; i++) DrawGuideRow(c, m.Row(i));
            Footer(c, m.Footer, m.FooterColor);
        }

        /// <summary>A Sunny screen row: its icon, the label (bold) with its note under it ("A quick tour, under a minute", "3 of 40
        /// heard", "On: Sunny helps" in amber while on), so a long label and a long note never share one line.</summary>
        static void DrawGuideRow(IMenuCanvas c, MenuRow r)
        {
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0, rad = Mathf.Min(28f, h / 2f);
            c.FillRoundRect(r.X0, r.Y0, w, h, rad, r.On ? MenuStyle.RowOnFill : MenuStyle.RowFill);
            if (r.On) c.StrokeRoundRect(r.X0 + 1.5f, r.Y0 + 1.5f, w - 3f, h - 3f, rad - 1f, 3f, MenuStyle.RowOnStroke);
            float cy = r.CentreY, tx = r.X0 + 36f;
            if (r.Icon != IconKind.None)
            {
                float s = Mathf.Min(100f, h - 28f);
                MenuIcons.Draw(c, r.Icon, r.X0 + 34f, cy - s / 2f, s, r.On ? MenuStyle.Amber : MenuStyle.Ink);
                tx = r.X0 + 34f + s + 36f;
            }
            float maxW = r.X1 - tx - 36f;
            bool note = !string.IsNullOrEmpty(r.Note);
            c.Text(r.Label, tx, note ? cy - MenuStyle.GuideLabelDy : cy, MenuStyle.GuideLabelSize, true, MenuStyle.Ink, TextAlign.Left, maxW, TextFit.Shrink);
            if (note) c.Text(r.Note, tx, cy + MenuStyle.GuideNoteDy, MenuStyle.GuideNoteSize, r.On, r.On ? MenuStyle.Amber : MenuStyle.Dim, TextAlign.Left, maxW, TextFit.Shrink);
        }

        static void DrawLessons(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            for (int i = 1; i < m.RowCount; i++) if (IsTile(m, m.Row(i))) DrawTile(c, m, m.Row(i));
        }

        static void DrawInstruments(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m, TextFit.Shrink);
            if (m.OnOptions)
            {
                for (int i = 1; i < m.RowCount; i++) DrawOptionRow(c, m.Row(i));
                Footer(c, m.Footer, m.FooterColor);
                return;
            }
            // Tiles first, then the chips printed over their corners, then the bottom row.
            for (int i = 1; i < m.RowCount; i++) if (m.Row(i).Id == RowKind.Tile) DrawTile(c, m, m.Row(i));
            for (int i = 1; i < m.RowCount; i++) if (m.Row(i).Id == RowKind.Options) DrawChip(c, m.Row(i));
            DrawBar(c, m);
            Footer(c, m.Footer, m.FooterColor);
        }

        /// <summary>The portrait is drawn from the atlas (the cell is Ready and the canvas draws images), else the
        /// descriptor's line drawing.</summary>
        public static bool HasPhoto(IMenuCanvas c, MenuController m, MenuRow r) =>
            c.DrawsImages && m.Portraits != null && m.Portraits.Atlas != null && m.Portraits.Ready(r.Cell);

        /// <summary>A grid tile: a cream card with rounded corners; the portrait (a photograph on the same paper) or the
        /// fallback line drawing in its top 150 px; a hairline; the name (34 px, bold) and, when it has one (Lessons), the
        /// status (22 px) in dark ink; at the seat, an amber frame round the card; on My set's members, the printed tick box.</summary>
        static void DrawTile(IMenuCanvas c, MenuController m, MenuRow r)
        {
            float x = r.X0, y = r.Y0, w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            if (r.On) c.StrokeRoundRect(x - 4f, y - 4f, w + 8f, h + 8f, MenuStyle.TileRadius + 4f, 4f, MenuStyle.TileOnStroke);
            c.FillRoundRect(x, y, w, h, MenuStyle.TileRadius, MenuStyle.Paper);
            float ph = MenuStyle.PortraitH, inset = MenuStyle.PhotoInset;
            float px = x + inset, py = y + inset, pw = w - 2f * inset, phh = ph - inset;
            if (HasPhoto(c, m, r)) c.Image(m.Portraits.Atlas, Cover(PortraitStudio.CellUv(r.Cell), PortraitStudio.Aspect, pw / phh), px, py, pw, phh, MenuStyle.PhotoRadius, true);
            else
            {
                // The same print without a photograph: the backdrop paper and the descriptor's line drawing on it.
                c.FillRoundRect(px, py, pw, phh, MenuStyle.PhotoRadius, MenuStyle.PhotoPaper);
                float s = Mathf.Min(MenuStyle.FallbackIconSize, phh - 16f);
                MenuIcons.DrawStrokes(c, r.Desc?.Portrait?.Fallback, px + (pw - s) / 2f, py + (phh - s) / 2f, s, MenuStyle.IconInk);
            }
            c.FillRoundRect(x + 14f, y + ph, w - 28f, 2f, 1f, MenuStyle.PaperRule);
            bool status = !string.IsNullOrEmpty(r.Note);
            c.Text(r.Label, x + w / 2f, y + (status ? MenuStyle.TileNameY : MenuStyle.TileNameAloneY), MenuStyle.TileNameSize, true, MenuStyle.PaperInk, TextAlign.Center, w - 20f, TextFit.Shrink);
            if (status)
                c.Text(r.Note, x + w / 2f, y + MenuStyle.TileStatusY, MenuStyle.TileStatusSize, r.On, r.On ? MenuStyle.PaperInk : MenuStyle.PaperDim, TextAlign.Center, w - 20f, TextFit.Shrink);
            if (r.Ticked) DrawTick(c, x + w - MenuStyle.TickInset - MenuStyle.TickSize, y + MenuStyle.TickInset);
        }

        /// <summary>The middle of a uv rectangle (of aspect <paramref name="from"/>) that fills a box of aspect
        /// <paramref name="to"/> without stretching (CSS object-fit: cover).</summary>
        public static Rect Cover(Rect uv, float from, float to)
        {
            if (to <= 0f || from <= 0f || Mathf.Approximately(from, to)) return uv;
            if (to > from)
            {
                float hgt = uv.height * from / to;
                return new Rect(uv.x, uv.y + (uv.height - hgt) * 0.5f, uv.width, hgt);
            }
            float wid = uv.width * to / from;
            return new Rect(uv.x + (uv.width - wid) * 0.5f, uv.y, wid, uv.height);
        }

        /// <summary>The printed tick box: a cream square with a dark rule and a dark tick.</summary>
        static void DrawTick(IMenuCanvas c, float x, float y)
        {
            float s = MenuStyle.TickSize;
            c.FillRoundRect(x, y, s, s, 5f, MenuStyle.Paper);
            c.StrokeRoundRect(x + 1.5f, y + 1.5f, s - 3f, s - 3f, 4f, 3f, MenuStyle.PaperInk);
            c.Line(x + 0.24f * s, y + 0.52f * s, x + 0.43f * s, y + 0.72f * s, 4.5f, MenuStyle.PaperInk);
            c.Line(x + 0.43f * s, y + 0.72f * s, x + 0.78f * s, y + 0.28f * s, 4.5f, MenuStyle.PaperInk);
        }

        /// <summary>The "Options" chip on an at-seat tile: a small dark-ink pill with cream type.</summary>
        static void DrawChip(IMenuCanvas c, MenuRow r)
        {
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            c.FillRoundRect(r.X0, r.Y0, w, h, h / 2f, MenuStyle.ChipFill);
            c.Text(r.Label, (r.X0 + r.X1) / 2f, r.CentreY, MenuStyle.ChipTextSize, true, MenuStyle.Paper, TextAlign.Center, w - 12f, TextFit.Shrink);
        }

        /// <summary>The bottom row: My set | One alone as one segmented control (the chosen half in the panel's amber "on"
        /// style), then Move (and Real hands in mixed reality) as buttons with their state under the label.</summary>
        static void DrawBar(IMenuCanvas c, MenuController m)
        {
            float rad = MenuStyle.BarRadius;
            bool pair = false;
            for (int i = 1; i < m.RowCount; i++)
            {
                MenuRow r = m.Row(i);
                if (r.Id != RowKind.Layout) continue;
                if (!pair)
                {
                    // The pair's frame, drawn once under both halves (from this half, the first, to the next one's end).
                    MenuRow second = i + 1 < m.RowCount && m.Row(i + 1).Id == RowKind.Layout ? m.Row(i + 1) : r;
                    float x0 = r.X0, x1 = second.X1;
                    c.FillRoundRect(x0, MenuStyle.BarTop, x1 - x0, MenuStyle.BarBottom - MenuStyle.BarTop, rad, MenuStyle.RowFill);
                    c.FillRoundRect(second.X0 - 1f, MenuStyle.BarTop + 22f, 2f, MenuStyle.BarBottom - MenuStyle.BarTop - 44f, 1f, MenuStyle.ScrollTrack);
                    pair = true;
                }
                float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
                if (r.On)
                {
                    c.FillRoundRect(r.X0 + 4f, r.Y0 + 4f, w - 8f, h - 8f, rad - 4f, MenuStyle.RowOnFill);
                    c.StrokeRoundRect(r.X0 + 5.5f, r.Y0 + 5.5f, w - 11f, h - 11f, rad - 5f, 3f, MenuStyle.RowOnStroke);
                }
                c.Text(r.Label, (r.X0 + r.X1) / 2f, r.CentreY, MenuStyle.BarLabelSize, true, r.On ? MenuStyle.Amber : MenuStyle.Ink, TextAlign.Center, w - 24f, TextFit.Shrink);
            }
            for (int i = 1; i < m.RowCount; i++)
            {
                MenuRow r = m.Row(i);
                if (r.Id != RowKind.Arrange && r.Id != RowKind.Hands) continue;
                float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
                c.FillRoundRect(r.X0, r.Y0, w, h, rad, r.On ? MenuStyle.RowOnFill : MenuStyle.RowFill);
                if (r.On) c.StrokeRoundRect(r.X0 + 1.5f, r.Y0 + 1.5f, w - 3f, h - 3f, rad - 1f, 3f, MenuStyle.RowOnStroke);
                float cx = (r.X0 + r.X1) / 2f;
                c.Text(r.Label, cx, r.CentreY - 16f, MenuStyle.ButtonLabelSize, true, MenuStyle.Ink, TextAlign.Center, w - 24f, TextFit.Shrink);
                if (!string.IsNullOrEmpty(r.Note))
                    c.Text(r.Note, cx, r.CentreY + 24f, MenuStyle.BarNoteSize, false, r.On ? MenuStyle.Amber : MenuStyle.Dim, TextAlign.Center, w - 20f, TextFit.Shrink);
            }
        }

        /// <summary>An options row: the label (with its small print under it) at the left, the current choice at the right.</summary>
        static void DrawOptionRow(IMenuCanvas c, MenuRow r)
        {
            if (r.Id != RowKind.Option) return;
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            c.FillRoundRect(r.X0, r.Y0, w, h, Mathf.Min(28f, h / 2f), MenuStyle.RowFill);
            float cy = r.CentreY, tx = r.X0 + 36f;
            bool small = !string.IsNullOrEmpty(r.Small);
            c.Text(r.Label, tx, small ? cy - 15f : cy, 46f, true, MenuStyle.Ink, TextAlign.Left, w - 72f - 300f);
            if (small) c.Text(r.Small, tx, cy + 27f, 26f, false, MenuStyle.Dim, TextAlign.Left, w - 72f - 300f);
            if (!string.IsNullOrEmpty(r.Note)) c.Text(r.Note, r.X1 - 36f, cy, 42f, true, MenuStyle.Amber, TextAlign.Right, 280f, TextFit.Shrink);
        }

        static void DrawList(IMenuCanvas c, MenuController m, int from)
        {
            c.SetClip(0f, MenuStyle.ListTop, MenuStyle.CW, MenuStyle.ListBottom);
            for (int i = from; i < m.RowCount; i++) DrawRow(c, m, i, MenuStyle.Ink, 50f);
            c.ClearClip();
            DrawScrollbar(c, m);
        }

        static void DrawScrollbar(IMenuCanvas c, MenuController m)
        {
            if (m.MaxScroll > 0f)
            {
                float span = MenuStyle.ListBottom - MenuStyle.ListTop;
                float total = span + m.MaxScroll;
                c.FillRoundRect(MenuStyle.ScrollbarX, MenuStyle.ListTop, MenuStyle.ScrollbarW, span, 5f, MenuStyle.ScrollTrack);
                c.FillRoundRect(MenuStyle.ScrollbarX, MenuStyle.ListTop + m.Scroll / total * span, MenuStyle.ScrollbarW, span / total * span, 5f, MenuStyle.Accent(m.Screen));
            }
        }

        static void DrawCourses(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            DrawList(c, m, 1);
        }

        static void DrawRecordings(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            if (m.BodyTitle != null)
            {
                float cx = MenuStyle.CW / 2f, maxW = MenuStyle.CW - 64f;
                c.Text(m.BodyTitle, cx, 360f, 52f, true, MenuStyle.Ink, TextAlign.Center, maxW, TextFit.Shrink);
                if (m.BodyLine1 != null) c.Text(m.BodyLine1, cx, 440f, 40f, false, MenuStyle.Dim, TextAlign.Center, maxW, TextFit.Shrink);
                if (m.BodyLine2 != null) c.Text(m.BodyLine2, cx, 500f, 40f, false, MenuStyle.Dim, TextAlign.Center, maxW, TextFit.Shrink);
                return;
            }
            c.SetClip(0f, MenuStyle.ListTop, MenuStyle.CW, MenuStyle.ListBottom);
            for (int i = 1; i < m.RowCount; i++)
            {
                MenuRow r = m.Row(i);
                if (r.Id == RowKind.Trash) DrawTrash(c, r);
                else if (r.Id == RowKind.Take) DrawTakeRow(c, r);
                else DrawRow(c, m, i, MenuStyle.Ink, 50f);
            }
            c.ClearClip();
            DrawScrollbar(c, m);
        }

        /// <summary>A take of the Recordings list: its name (bold) over its local time (dim), the whole row's width.</summary>
        static void DrawTakeRow(IMenuCanvas c, MenuRow r)
        {
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0;
            c.FillRoundRect(r.X0, r.Y0, w, h, Mathf.Min(28f, h / 2f), MenuStyle.RowFill);
            // The row's full slot (ListH), not the part the list clip leaves: the text scrolls with the row.
            float cy = r.Y0 <= MenuStyle.ListTop + 0.5f && h < MenuStyle.ListH ? r.Y1 - MenuStyle.ListH / 2f : r.Y0 + MenuStyle.ListH / 2f;
            // its constellation at the row's right end (the take's notes as the night sky draws them)
            float sx = r.X1 - TakeStarsRight - TakeStarsW;
            DrawTakeStars(c, r.Stars, sx, cy - TakeStarsH / 2f, TakeStarsW, TakeStarsH);
            float tx = r.X0 + 36f, maxW = sx - 20f - tx;
            c.Text(r.Label, tx, cy + MenuStyle.TakeNameDy, MenuStyle.TakeNameSize, true, MenuStyle.Ink, TextAlign.Left, maxW);
            if (!string.IsNullOrEmpty(r.Note))
                c.Text(r.Note, tx, cy + MenuStyle.TakeDateDy, MenuStyle.TakeDateSize, false, MenuStyle.Dim, TextAlign.Left, maxW);
        }

        /// <summary>The take's constellation picture: its own small night (px), the stars at their time (across) and height
        /// (up), each instrument in its colour, joined to the one before it of the same instrument.</summary>
        public const float TakeStarsW = 210f, TakeStarsH = 88f, TakeStarsRight = 16f, TakeStarsPad = 14f;
        public static readonly Color TakeStarsSky = new Color(0.05f, 0.075f, 0.18f, 0.95f);

        static void DrawTakeStars(IMenuCanvas c, SongStars.Thumb th, float x, float y, float w, float h)
        {
            c.FillRoundRect(x, y, w, h, 18f, TakeStarsSky);
            if (th == null || th.Count == 0) return;
            float p = TakeStarsPad, iw = w - 2f * p, ih = h - 2f * p;
            for (int i = 0; i < th.Count; i++)
            {
                int j = th.Prev[i];
                if (j < 0) continue;
                Color lc = SongStars.ColorOf(th.Kind[i]);
                lc.a = 0.45f;
                c.Line(x + p + th.Pos[j].x * iw, y + p + (1f - th.Pos[j].y) * ih, x + p + th.Pos[i].x * iw, y + p + (1f - th.Pos[i].y) * ih, 2f, lc);
            }
            for (int i = 0; i < th.Count; i++)
            {
                Color sc = SongStars.ColorOf(th.Kind[i]);
                float sxp = x + p + th.Pos[i].x * iw, syp = y + p + (1f - th.Pos[i].y) * ih;
                float s = 2.2f + 2.4f * th.Size[i];
                Color halo = sc;
                halo.a = 0.16f;
                c.FillRoundRect(sxp - 2.4f * s, syp - 2.4f * s, 4.8f * s, 4.8f * s, 2.4f * s, halo);
                c.FillRoundRect(sxp - s, syp - s, 2f * s, 2f * s, s, sc);
            }
        }

        /// <summary>A take's bin: the bin icon in a row-coloured cell; once poked, a red cell reading "Delete?" until the
        /// second poke (or 3 s).</summary>
        static void DrawTrash(IMenuCanvas c, MenuRow r)
        {
            float w = r.X1 - r.X0, h = r.Y1 - r.Y0, rad = Mathf.Min(28f, h / 2f);
            float cy = r.Y0 <= MenuStyle.ListTop + 0.5f && h < MenuStyle.ListH ? r.Y1 - MenuStyle.ListH / 2f : r.Y0 + MenuStyle.ListH / 2f;
            float cx = (r.X0 + r.X1) / 2f;
            if (r.On)
            {
                c.FillRoundRect(r.X0, r.Y0, w, h, rad, MenuStyle.WithAlpha(MenuStyle.Red, 0.16f));
                c.StrokeRoundRect(r.X0 + 1.5f, r.Y0 + 1.5f, w - 3f, h - 3f, rad - 1f, 3f, MenuStyle.WithAlpha(MenuStyle.Red, 0.7f));
                c.Text(r.Label, cx, cy, 40f, true, MenuStyle.Red, TextAlign.Center, w - 20f, TextFit.Shrink);
                return;
            }
            c.FillRoundRect(r.X0, r.Y0, w, h, rad, MenuStyle.RowFill);
            const float s = 72f;
            MenuIcons.Draw(c, IconKind.Delete, cx - s / 2f, cy - s / 2f, s, MenuStyle.WithAlpha(MenuStyle.Red, 0.85f));
        }

        static void DrawTake(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            if (!string.IsNullOrEmpty(m.TakeLine)) c.Text(m.TakeLine, 48f, 200f, 42f, false, MenuStyle.Dim, TextAlign.Left, MenuStyle.CW - 96f, TextFit.Shrink);
            for (int i = 1; i < m.RowCount; i++)
            {
                RowKind id = m.Row(i).Id;
                DrawRow(c, m, i, id == RowKind.Play ? MenuStyle.Green : id == RowKind.Delete ? MenuStyle.Red : MenuStyle.Ink, 52f);
            }
            Footer(c, m.Footer, m.FooterColor);
        }

        static void DrawLesson(IMenuCanvas c, MenuController m)
        {
            DrawHeader(c, m);
            float cx = MenuStyle.CW / 2f;
            if (m.LessonState == TutorialState.Done)
            {
                c.Text(MenuStyle.LessonComplete, cx, 200f, 64f, true, MenuStyle.Green, TextAlign.Center);
                c.Text(m.SummaryLine, cx, 270f, 44f, false, MenuStyle.Ink, TextAlign.Center, MenuStyle.CW - 64f, TextFit.Shrink);
                DrawProgress(c, m, MenuStyle.ProgressDoneY);
                // First five: with a Next row it is the one in green; Replay otherwise.
                RowKind first = m.RowCount > 1 ? m.Row(1).Id : RowKind.None;
                for (int i = 1; i < m.RowCount; i++) DrawRow(c, m, i, m.Row(i).Id == first ? MenuStyle.Green : MenuStyle.Ink, m.Row(i).Label.Length > 22 ? 46f : 56f);
                return;
            }
            DrawProgress(c, m, MenuStyle.ProgressRunningY);
            if (m.LessonState == TutorialState.Countdown)
            {
                c.Text(m.Digit, cx, 420f, 220f, true, MenuStyle.Cyan, TextAlign.Center);
                c.Text(MenuStyle.GetReady, cx, 570f, 40f, false, MenuStyle.Dim, TextAlign.Center);
            }
            else
            {
                string hint = m.Hint ?? "";
                c.Text(hint, cx, 430f, hint.Length > 14 ? 80f : 120f, true, m.LessonState == TutorialState.Waiting ? MenuStyle.White : MenuStyle.Ink, TextAlign.Center, MenuStyle.CW - 80f);
            }
            for (int i = 1; i < m.RowCount; i++) DrawRow(c, m, i, MenuStyle.Red, 56f);
        }

        static void DrawProgress(IMenuCanvas c, MenuController m, float y)
        {
            float x0 = 48f, x1 = MenuStyle.CW - 48f, h = MenuStyle.ProgressH;
            c.FillRoundRect(x0, y - h / 2f, x1 - x0, h, h / 2f, MenuStyle.ProgressTrack);
            if (m.ProgressFrac > 0f)
                c.FillRoundRect(x0, y - h / 2f, Mathf.Max(h, (x1 - x0) * m.ProgressFrac), h, h / 2f, m.LessonState == TutorialState.Done ? MenuStyle.Green : MenuStyle.Cyan);
            c.Text(m.ProgressLeft, x0, y + 50f, 44f, false, MenuStyle.Ink, TextAlign.Left);
            c.Text(m.ProgressRight, x1, y + 50f, 44f, false, m.ProgressMistakes ? MenuStyle.Red : MenuStyle.Dim, TextAlign.Right);
        }
    }
}
