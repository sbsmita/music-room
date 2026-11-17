// IMenuCanvas on the GPU (spec 08 section 1.3): one CommandBuffer, executed only when the panel is dirty, draws into the
// panel's render texture with an orthographic canvas-pixel projection (origin top-left, y down). Shapes are quads with
// the MusicRoom/MenuShape signed-distance shader; text is TextMeshPro (LiberationSans SDF): one hidden TextMeshPro per
// text slot whose mesh is drawn with the font's own material. Slots are handed out in paint order, so an unchanged
// screen repaints (hover fades) without regenerating a single text mesh; text colour goes through _FaceColor in a
// property block, never through TMP vertex colours (TMP would gamma-convert them in a Linear project, and this canvas
// keeps raw sRGB numbers). No camera, no layer, no uGUI.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Menu
{
    public sealed class GpuMenuCanvas : IMenuCanvas, IDisposable
    {
        public const int MaxTextSlots = 64;
        /// <summary>TMP synthesises bold by dilating the glyphs AND adding the font's boldSpacing (7% em) after every
        /// character, which makes bold labels ~10% wider than a real bold face (the design's bold); -4% em brings
        /// "Lock instruments" back beside its note.</summary>
        public const float BoldCharacterSpacing = -4f;
        const string ClipKeyword = "UNITY_UI_CLIP_RECT";
        const string Ellipsis = "\u2026";

        static readonly int IdBounds = Shader.PropertyToID("_Bounds"), IdA = Shader.PropertyToID("_A"), IdB = Shader.PropertyToID("_B"),
            IdClip = Shader.PropertyToID("_Clip"), IdGrad = Shader.PropertyToID("_Grad"), IdPrim = Shader.PropertyToID("_Prim"),
            IdColor = Shader.PropertyToID("_Color"), IdColor2 = Shader.PropertyToID("_Color2"), IdColor3 = Shader.PropertyToID("_Color3"),
            IdFaceColor = Shader.PropertyToID("_FaceColor"), IdClipRect = Shader.PropertyToID("_ClipRect"), IdScreenParams = Shader.PropertyToID("_ScreenParams"),
            IdMainTex = Shader.PropertyToID("_MainTex"), IdUvRect = Shader.PropertyToID("_UvRect");
        static readonly Vector4 NoClip = new Vector4(-1e5f, -1e5f, 1e5f, 1e5f);
        static readonly Vector4 TmpNoClip = new Vector4(-32767f, -32767f, 32767f, 32767f);

        sealed class Slot
        {
            public TextMeshPro Tmp;
            public string Text;
            public float Size = -1f, MaxWidth = -1f;
            public bool Bold;
            public TextAlign Align;
            public TextFit Fit;
        }

        struct MeasureKey : IEquatable<MeasureKey>
        {
            public string Text; public float Size, Width; public bool Bold;
            public bool Equals(MeasureKey o) => Size == o.Size && Width == o.Width && Bold == o.Bold && string.Equals(Text, o.Text);
            public override bool Equals(object obj) => obj is MeasureKey o && Equals(o);
            public override int GetHashCode() => ((Text != null ? Text.GetHashCode() : 0) * 31 + Size.GetHashCode() * 7 + Width.GetHashCode()) * 3 + (Bold ? 1 : 0);
        }

        readonly CommandBuffer _cb = new CommandBuffer { name = "[Menu] canvas" };
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        readonly Material _shape;
        readonly Mesh _quad;
        readonly Transform _textRoot;
        readonly TMP_FontAsset _font;
        readonly List<Slot> _slots = new List<Slot>(MaxTextSlots);
        // The text measure caches. Labels with changing words (a take's time, a count) add keys all session long, so each
        // cache is bounded: past CacheLimit entries it starts over (a cleared cache only costs a re-measure per label).
        readonly Dictionary<MeasureKey, float> _measured = new Dictionary<MeasureKey, float>(64);
        readonly Dictionary<MeasureKey, string> _fitted = new Dictionary<MeasureKey, string>(32);
        /// <summary>Entries either text cache holds at most before it is cleared.</summary>
        public const int CacheLimit = 512;
        /// <summary>Entries in the two text caches now (tests).</summary>
        public int CachedTexts => _measured.Count + _fitted.Count;
        TextMeshPro _measure;
        int _next;
        Vector4 _clip = NoClip;
        bool _clipOn, _keywordOn;
        float _w, _h;
        bool _warnedSlots;

        public int TextSlotsUsed => _next;
        public int Draws { get; private set; }

        /// <summary>Tests: text objects made so far, and how many of them left TMP's per-camera update queue.</summary>
        public int TextObjects => _slots.Count + (_measure != null ? 1 : 0);
        public int StaticTextObjects
        {
            get
            {
                int n = _measure != null && _measure.isTextObjectScaleStatic ? 1 : 0;
                for (int i = 0; i < _slots.Count; i++) if (_slots[i].Tmp != null && _slots[i].Tmp.isTextObjectScaleStatic) n++;
                return n;
            }
        }

        public GpuMenuCanvas(Material shape, Transform textRoot)
        {
            _shape = shape;
            _textRoot = textRoot;
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null) Debug.LogWarning("[Menu] no TextMeshPro default font (run Tools/unity.sh tmp-import): the panel draws without text");
            _quad = new Mesh { name = "MenuCanvasQuad" };
            _quad.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
        }

        /// <summary>Start a paint into <paramref name="target"/> (cleared to transparent).</summary>
        public void Begin(RenderTexture target)
        {
            _w = target.width;
            _h = target.height;
            _next = 0;
            Draws = 0;
            _clip = NoClip;
            _clipOn = false;
            _keywordOn = false;
            _cb.Clear();
            _cb.SetRenderTarget(target);
            _cb.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 0f));
            // Canvas pixels: x right, y DOWN (bottom = h, top = 0), so canvas row 0 lands at the texture's top (v = 1).
            _cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, _w, _h, 0f, -1f, 1f));
            // TMP's SDF shader derives its anti-aliasing width from _ScreenParams: make it the target's size.
            _cb.SetGlobalVector(IdScreenParams, new Vector4(_w, _h, 1f + 1f / _w, 1f + 1f / _h));
        }

        /// <summary>Finish: mips, then run the buffer now (outside any camera).</summary>
        public void End(RenderTexture target)
        {
            if (_keywordOn) { _cb.DisableShaderKeyword(ClipKeyword); _keywordOn = false; }
            if (target.useMipMap && !target.autoGenerateMips) _cb.GenerateMips(target);
            Graphics.ExecuteCommandBuffer(_cb);
        }

        // ---------------------------------------------------------------- shapes

        void Shape(int prim, float x0, float y0, float x1, float y1, Vector4 a, Vector4 b, Color c, in MenuGradient g, bool gradient)
        {
            _mpb.Clear();
            _mpb.SetFloat(IdPrim, prim);
            _mpb.SetVector(IdBounds, new Vector4(x0, y0, x1, y1));
            _mpb.SetVector(IdA, a);
            _mpb.SetVector(IdB, b);
            _mpb.SetVector(IdClip, _clip);
            if (gradient)
            {
                _mpb.SetVector(IdGrad, new Vector4(g.Horizontal ? 2f : 1f, g.From, g.To, g.Mid));
                _mpb.SetVector(IdColor, g.C0);
                _mpb.SetVector(IdColor2, g.C1);
                _mpb.SetVector(IdColor3, g.C2);
            }
            else
            {
                _mpb.SetVector(IdGrad, Vector4.zero);
                _mpb.SetVector(IdColor, c);
            }
            _cb.DrawMesh(_quad, Matrix4x4.identity, _shape, 0, 0, _mpb);
            Draws++;
        }

        static float ClampRadius(float w, float h, float r) => Mathf.Clamp(r, 0f, 0.5f * Mathf.Min(w, h));

        public void FillRoundRect(float x, float y, float w, float h, float r, Color c)
        {
            if (w <= 0f || h <= 0f || c.a <= 0f) return;
            r = ClampRadius(w, h, r);
            Shape(0, x - 1f, y - 1f, x + w + 1f, y + h + 1f, new Vector4(x + w / 2f, y + h / 2f, w / 2f, h / 2f), new Vector4(r, 0f, 0f, 0f), c, default, false);
        }

        public void FillRoundRect(float x, float y, float w, float h, float r, in MenuGradient g)
        {
            if (w <= 0f || h <= 0f) return;
            r = ClampRadius(w, h, r);
            Shape(0, x - 1f, y - 1f, x + w + 1f, y + h + 1f, new Vector4(x + w / 2f, y + h / 2f, w / 2f, h / 2f), new Vector4(r, 0f, 0f, 0f), default, g, true);
        }

        public void StrokeRoundRect(float x, float y, float w, float h, float r, float lineWidth, Color c)
        {
            if (w <= 0f || h <= 0f || c.a <= 0f || lineWidth <= 0f) return;
            r = ClampRadius(w, h, r);
            float e = lineWidth / 2f + 1f;
            Shape(0, x - e, y - e, x + w + e, y + h + e, new Vector4(x + w / 2f, y + h / 2f, w / 2f, h / 2f), new Vector4(r, lineWidth, 0f, 0f), c, default, false);
        }

        public void ShadowRoundRect(float x, float y, float w, float h, float r, float blur, Color c)
        {
            if (w <= 0f || h <= 0f || c.a <= 0f) return;
            r = ClampRadius(w, h, r);
            float sigma = Mathf.Max(0.5f, blur / 2f), e = 3f * sigma + 1f;
            Shape(4, x - e, y - e, x + w + e, y + h + e, new Vector4(x + w / 2f, y + h / 2f, w / 2f, h / 2f), new Vector4(r, sigma, 0f, 0f), c, default, false);
        }

        public void Line(float x0, float y0, float x1, float y1, float lineWidth, Color c)
        {
            if (c.a <= 0f) return;
            float e = lineWidth / 2f + 1f;
            Shape(1, Mathf.Min(x0, x1) - e, Mathf.Min(y0, y1) - e, Mathf.Max(x0, x1) + e, Mathf.Max(y0, y1) + e, new Vector4(x0, y0, x1, y1), new Vector4(lineWidth, 0f, 0f, 0f), c, default, false);
        }

        public void StrokeEllipse(float cx, float cy, float rx, float ry, float lineWidth, Color c, int half = 0)
        {
            if (c.a <= 0f) return;
            float e = lineWidth / 2f + 2f;
            Shape(2, cx - rx - e, cy - ry - e, cx + rx + e, cy + ry + e, new Vector4(cx, cy, rx, ry), new Vector4(lineWidth, half, 0f, 0f), c, default, false);
        }

        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            if (col.a <= 0f) return;
            float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 1f, y0 = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - 1f;
            float x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 1f, y1 = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + 1f;
            Shape(3, x0, y0, x1, y1, new Vector4(a.x, a.y, b.x, b.y), new Vector4(c.x, c.y, 0f, 0f), col, default, false);
        }

        public bool DrawsImages => true;

        /// <summary>Primitive 5 of MusicRoom/MenuShape: the texture's uv rectangle into the rounded box, opaque, with the
        /// same one-pixel edge as the shapes (the portrait atlas on a tile, platform.md 5.2).</summary>
        public void Image(Texture texture, Rect uv, float x, float y, float w, float h, float r, bool linearSource)
        {
            if (texture == null || w <= 0f || h <= 0f) return;
            r = ClampRadius(w, h, r);
            _mpb.Clear();
            _mpb.SetFloat(IdPrim, 5f);
            _mpb.SetVector(IdBounds, new Vector4(x - 1f, y - 1f, x + w + 1f, y + h + 1f));
            _mpb.SetVector(IdA, new Vector4(x + w / 2f, y + h / 2f, w / 2f, h / 2f));
            _mpb.SetVector(IdB, new Vector4(r, linearSource ? 1f : 0f, 0f, 0f));
            _mpb.SetVector(IdClip, _clip);
            _mpb.SetVector(IdGrad, Vector4.zero);
            _mpb.SetVector(IdColor, Color.white);
            _mpb.SetTexture(IdMainTex, texture);
            _mpb.SetVector(IdUvRect, new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax));
            _cb.DrawMesh(_quad, Matrix4x4.identity, _shape, 0, 0, _mpb);
            Draws++;
        }

        public void SetClip(float x0, float y0, float x1, float y1)
        {
            _clip = new Vector4(x0, y0, x1, y1);
            _clipOn = true;
        }

        public void ClearClip()
        {
            _clip = NoClip;
            _clipOn = false;
        }

        // ---------------------------------------------------------------- text

        Slot NextSlot()
        {
            if (_next < _slots.Count) return _slots[_next++];
            if (_slots.Count >= MaxTextSlots)
            {
                if (!_warnedSlots) { _warnedSlots = true; Debug.LogWarning("[Menu] out of text slots"); }
                return null;
            }
            var slot = new Slot { Tmp = MakeText("MenuText" + _slots.Count) };
            _slots.Add(slot);
            _next++;
            return slot;
        }

        TextMeshPro MakeText(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_textRoot, false);
            var tmp = go.AddComponent<TextMeshPro>();
            // The slots are parked and never rescaled: leave TMP's per-camera InternalUpdate queue (it would read each
            // slot's lossyScale on every camera pre-cull for the whole session, 65 objects, for nothing).
            tmp.isTextObjectScaleStatic = true;
            if (_font != null) tmp.font = _font;
            tmp.isOrthographic = true;                 // 1 font unit = 1 canvas px (TMP 3D otherwise scales by 0.1)
            tmp.richText = false;                      // take names and lesson titles are shown as typed
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = Color.white;                   // the colour comes from _FaceColor at draw time
            tmp.margin = Vector4.zero;
            tmp.extraPadding = false;
            tmp.overflowMode = TextOverflowModes.Overflow;   // fitting is FitText's (cut with an ellipsis), not TMP's
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.forceRenderingOff = true;   // drawn only into the canvas, never by a scene camera
            return tmp;
        }

        static TextAlignmentOptions Options(TextAlign a) =>
            a == TextAlign.Left ? TextAlignmentOptions.Left : a == TextAlign.Right ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;

        static Vector2 Pivot(TextAlign a) => new Vector2(a == TextAlign.Left ? 0f : a == TextAlign.Right ? 1f : 0.5f, 0.5f);

        public float MeasureText(string text, float size, bool bold)
        {
            if (string.IsNullOrEmpty(text) || _font == null) return 0f;
            var key = new MeasureKey { Text = text, Size = size, Bold = bold };
            if (_measured.TryGetValue(key, out float w)) return w;
            w = MeasureRaw(text, size, bold);
            if (_measured.Count >= CacheLimit) _measured.Clear();
            _measured[key] = w;
            return w;
        }

        float MeasureRaw(string text, float size, bool bold)
        {
            if (_measure == null) _measure = MakeText("MenuMeasure");
            _measure.fontSize = size;
            _measure.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            _measure.characterSpacing = bold ? BoldCharacterSpacing : 0f;
            return _measure.GetPreferredValues(text, 1e5f, 1e5f).x;
        }

        /// <summary>FitText: the text if it fits, else its longest prefix that fits with an ellipsis.
        /// Cached per (text, size, bold, width): a repaint never measures or builds a string again.</summary>
        public string FitText(string text, float size, bool bold, float maxWidth)
        {
            if (maxWidth <= 0f || string.IsNullOrEmpty(text) || MeasureText(text, size, bold) <= maxWidth) return text;
            var key = new MeasureKey { Text = text, Size = size, Bold = bold, Width = maxWidth };
            if (_fitted.TryGetValue(key, out string s)) return s;
            int lo = 1, hi = text.Length - 1, best = 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (MeasureRaw(text.Substring(0, mid) + Ellipsis, size, bold) <= maxWidth) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            s = text.Substring(0, best) + Ellipsis;
            if (_fitted.Count >= CacheLimit) _fitted.Clear();
            _fitted[key] = s;
            return s;
        }

        public void Text(string text, float x, float y, float size, bool bold, Color c, TextAlign align, float maxWidth = 0f, TextFit fit = TextFit.Ellipsis)
        {
            if (string.IsNullOrEmpty(text) || _font == null || c.a <= 0f) return;
            Slot s = NextSlot();
            if (s == null) return;
            if (fit == TextFit.None) maxWidth = 0f;
            if (fit == TextFit.Ellipsis) text = FitText(text, size, bold, maxWidth);
            if (!string.Equals(s.Text, text) || s.Size != size || s.Bold != bold || s.Align != align || s.MaxWidth != maxWidth || s.Fit != fit)
            {
                float fontSize = size;
                if (fit == TextFit.Shrink && maxWidth > 0f)
                {
                    float wNow = MeasureText(text, size, bold);
                    if (wNow > maxWidth) fontSize = Mathf.Max(size * 0.6f, Mathf.Floor(size * maxWidth / wNow));
                }
                TextMeshPro t = s.Tmp;
                t.fontSize = fontSize;
                t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                t.characterSpacing = bold ? BoldCharacterSpacing : 0f;
                t.alignment = Options(align);
                var rt = t.rectTransform;
                rt.pivot = Pivot(align);
                rt.sizeDelta = new Vector2(maxWidth > 0f ? maxWidth : 4096f, size * 2f);
                t.text = text;
                t.ForceMeshUpdate(true, true);
                s.Text = text; s.Size = size; s.Bold = bold; s.Align = align; s.MaxWidth = maxWidth; s.Fit = fit;
            }
            Mesh mesh = s.Tmp.mesh;
            Material mat = s.Tmp.fontSharedMaterial;
            if (mesh == null || mesh.vertexCount == 0 || mat == null) return;
            _mpb.Clear();
            _mpb.SetVector(IdFaceColor, c);
            if (_clipOn)
            {
                // TMP's clip rect is in the text mesh's object space (y up, origin at (x, y)).
                _mpb.SetVector(IdClipRect, new Vector4(_clip.x - x, y - _clip.w, _clip.z - x, y - _clip.y));
                if (!_keywordOn) { _cb.EnableShaderKeyword(ClipKeyword); _keywordOn = true; }
            }
            else
            {
                _mpb.SetVector(IdClipRect, TmpNoClip);
                if (_keywordOn) { _cb.DisableShaderKeyword(ClipKeyword); _keywordOn = false; }
            }
            // Canvas y runs down, TMP's y runs up: flip the mesh about its middle line.
            _cb.DrawMesh(mesh, Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, new Vector3(1f, -1f, 1f)), mat, 0, 0, _mpb);
            Draws++;
        }

        public void Dispose()
        {
            _cb.Release();
            if (_quad != null) UnityEngine.Object.Destroy(_quad);
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].Tmp != null) UnityEngine.Object.Destroy(_slots[i].Tmp.gameObject);
            if (_measure != null) UnityEngine.Object.Destroy(_measure.gameObject);
            _slots.Clear();
        }
    }
}
