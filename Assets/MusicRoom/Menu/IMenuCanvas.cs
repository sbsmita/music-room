// The 2D drawing surface the painter talks to: the primitives the panel needs
// (rounded rectangles filled, stroked or gradient-filled, lines with round caps, ellipses, a triangle, a box shadow,
// text measured and drawn at its middle line, a clip rectangle). GpuMenuCanvas renders it into the panel's render
// texture; tests use a recording canvas. Coordinates are canvas px (origin top-left, y down); colours raw sRGB.
using UnityEngine;

namespace MusicRoom.Menu
{
    public enum TextAlign : byte { Left, Center, Right }

    /// <summary>How a text that is too wide is handled: fit (cut with an ellipsis), shrink to the width
    /// (for the long body lines that would run off the panel), or nothing.</summary>
    public enum TextFit : byte { None, Ellipsis, Shrink }

    /// <summary>A linear gradient: vertical (along canvas y) or horizontal, two stops or three with a middle one.</summary>
    public struct MenuGradient
    {
        public bool Horizontal;
        public float From, To, Mid;   // px along the axis; Mid in 0..1 (0 = two stops)
        public Color C0, C1, C2;

        public static MenuGradient Vertical(float from, float to, Color c0, Color c1) =>
            new MenuGradient { From = from, To = to, C0 = c0, C1 = c1, C2 = c1 };
        public static MenuGradient Vertical3(float from, float to, Color c0, float mid, Color c1, Color c2) =>
            new MenuGradient { From = from, To = to, Mid = mid, C0 = c0, C1 = c1, C2 = c2 };
        public static MenuGradient HorizontalOf(float from, float to, Color c0, Color c1) =>
            new MenuGradient { Horizontal = true, From = from, To = to, C0 = c0, C1 = c1, C2 = c1 };
    }

    public interface IMenuCanvas
    {
        void FillRoundRect(float x, float y, float w, float h, float r, Color c);
        void FillRoundRect(float x, float y, float w, float h, float r, in MenuGradient g);
        /// <summary>Stroke centred on the rounded-rectangle path (lineWidth straddles the path: half inside, half outside).</summary>
        void StrokeRoundRect(float x, float y, float w, float h, float r, float lineWidth, Color c);
        /// <summary>The fill's shadow alone (a Gaussian blur with no offset): sigma = blur / 2.</summary>
        void ShadowRoundRect(float x, float y, float w, float h, float r, float blur, Color c);
        void Line(float x0, float y0, float x1, float y1, float lineWidth, Color c);
        /// <summary>Ellipse outline; half +1 = upper half only, -1 = lower half, 0 = whole (round caps at the cut).</summary>
        void StrokeEllipse(float cx, float cy, float rx, float ry, float lineWidth, Color c, int half = 0);
        void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color col);
        /// <summary>Text with its middle line at y (textBaseline 'middle').</summary>
        void Text(string text, float x, float y, float size, bool bold, Color c, TextAlign align, float maxWidth = 0f, TextFit fit = TextFit.Ellipsis);
        float MeasureText(string text, float size, bool bold);
        void SetClip(float x0, float y0, float x1, float y1);
        void ClearClip();
        /// <summary>An image (the portrait atlas, platform.md 5.2): the texture's <paramref name="uv"/> rectangle (xMin, yMin =
        /// its bottom-left in texture space) drawn opaque into the canvas box (x, y, w, h), its corners rounded by r.
        /// <paramref name="linearSource"/>: the texture stores linear values (an sRGB render target sampled with hardware
        /// decode), so the canvas, which keeps sRGB numbers, encodes them first. The default draws nothing (a canvas
        /// without images: the painter then shows the fallback icon).</summary>
        void Image(Texture texture, Rect uv, float x, float y, float w, float h, float r, bool linearSource) { }
        /// <summary>The canvas can draw images (the GPU canvas); false: the painter draws fallback icons instead.</summary>
        bool DrawsImages => false;
    }
}
