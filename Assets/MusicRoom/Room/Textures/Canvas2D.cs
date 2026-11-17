// A small software 2D canvas (fillRect, linear / concentric radial gradients, arcs, ellipses, filled paths, strokes,
// clip, get/putImageData), so the room's procedural textures (spec 07 section 7) are drawn call by call as specified. Rows run top-down like the canvas (row 0 = top); PixelImage flips once on output.
// Colour is straight (unpremultiplied) sRGB in 0..1 per channel, composited source-over; edges are anti-aliased by
// pixel coverage (exact for axis-aligned rects, distance-based for discs, 4x4 supersampling for small paths).
// Thread-safe per instance (no Unity API), so textures generate on worker threads.
using System;
using System.Collections.Generic;

namespace MusicRoom.Room.Textures
{
    /// <summary>A colour stop or solid colour (sRGB 0..1, alpha 0..1).</summary>
    public struct Rgba
    {
        public float R, G, B, A;
        public Rgba(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }
        /// <summary>From 0..255 channels (CSS rgb()/rgba()).</summary>
        public static Rgba Bytes(double r, double g, double b, double a = 1) => new Rgba((float)(r / 255.0), (float)(g / 255.0), (float)(b / 255.0), (float)a);
        public static Rgba Hex(string hex)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return Bytes((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }
        public static Rgba Lerp(Rgba a, Rgba b, float t) => new Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }

    /// <summary>A canvas paint: solid colour, linear gradient or concentric radial gradient (the only kinds the room textures use).</summary>
    public sealed class Paint
    {
        enum Kind { Solid, Linear, Radial }
        Kind _kind;
        Rgba _solid;
        float _x0, _y0, _x1, _y1, _r0, _r1;
        readonly List<float> _offsets = new List<float>();
        readonly List<Rgba> _colors = new List<Rgba>();

        public static Paint Solid(Rgba c) => new Paint { _kind = Kind.Solid, _solid = c };
        public static Paint Linear(float x0, float y0, float x1, float y1) => new Paint { _kind = Kind.Linear, _x0 = x0, _y0 = y0, _x1 = x1, _y1 = y1 };
        public static Paint Radial(float cx, float cy, float r0, float r1) => new Paint { _kind = Kind.Radial, _x0 = cx, _y0 = cy, _r0 = r0, _r1 = r1 };

        public Paint Stop(float offset, Rgba c) { _offsets.Add(offset); _colors.Add(c); return this; }

        /// <summary>Colour at a canvas point (pixel centres are x + .5, y + .5). Gradients pad with their end colours and
        /// interpolate unpremultiplied sRGB, as the canvas goldens do.</summary>
        public Rgba At(float x, float y)
        {
            if (_kind == Kind.Solid) return _solid;
            float t;
            if (_kind == Kind.Linear)
            {
                float dx = _x1 - _x0, dy = _y1 - _y0, len2 = dx * dx + dy * dy;
                t = len2 > 0f ? ((x - _x0) * dx + (y - _y0) * dy) / len2 : 0f;
            }
            else
            {
                float d = (float)Math.Sqrt((x - _x0) * (x - _x0) + (y - _y0) * (y - _y0));
                t = _r1 != _r0 ? (d - _r0) / (_r1 - _r0) : 1f;
            }
            if (_offsets.Count == 0) return default;
            if (t <= _offsets[0]) return _colors[0];
            int last = _offsets.Count - 1;
            if (t >= _offsets[last]) return _colors[last];
            for (int i = 0; i < last; i++)
                if (t <= _offsets[i + 1])
                {
                    float span = _offsets[i + 1] - _offsets[i];
                    return Rgba.Lerp(_colors[i], _colors[i + 1], span > 0f ? (t - _offsets[i]) / span : 1f);
                }
            return _colors[last];
        }

        /// <summary>Radial gradients that end fully transparent paint nothing beyond r1: the fill can skip those pixels.</summary>
        public bool TryRadialExtent(out float cx, out float cy, out float r)
        {
            cx = _x0; cy = _y0; r = _r1;
            return _kind == Kind.Radial && _colors.Count > 0 && _colors[_colors.Count - 1].A <= 0f;
        }
    }

    public sealed class Canvas2D
    {
        public readonly int W, H;
        /// <summary>Straight RGBA, 4 floats per pixel, row 0 = top.</summary>
        public readonly float[] Px;
        float[] _clip;   // coverage mask (null = none)

        public Canvas2D(int w, int h)
        {
            W = w; H = h;
            Px = new float[w * h * 4];
        }

        public void Clip(float[] coverage) => _clip = coverage;
        public void ResetClip() => _clip = null;

        /// <summary>Source-over of a paint with coverage c at pixel (x, y).</summary>
        public void Blend(int x, int y, float c, Rgba s)
        {
            if (c <= 0f) return;
            int i = y * W + x;
            if (_clip != null) { c *= _clip[i]; if (c <= 0f) return; }
            float sa = s.A * (c > 1f ? 1f : c);
            if (sa <= 0f) return;
            int k = i * 4;
            float da = Px[k + 3];
            float oa = sa + da * (1f - sa);
            if (oa <= 0f) return;
            float wd = da * (1f - sa) / oa, ws = sa / oa;
            Px[k] = s.R * ws + Px[k] * wd;
            Px[k + 1] = s.G * ws + Px[k + 1] * wd;
            Px[k + 2] = s.B * ws + Px[k + 2] * wd;
            Px[k + 3] = oa;
        }

        /// <summary>ctx.fillRect with exact area coverage of the (possibly fractional) rectangle.</summary>
        public void FillRect(float x, float y, float w, float h, Paint p)
        {
            if (w < 0f) { x += w; w = -w; }
            if (h < 0f) { y += h; h = -h; }
            float x1 = x + w, y1 = y + h;
            int ix0 = Math.Max(0, (int)Math.Floor(x)), iy0 = Math.Max(0, (int)Math.Floor(y));
            int ix1 = Math.Min(W - 1, (int)Math.Ceiling(x1) - 1), iy1 = Math.Min(H - 1, (int)Math.Ceiling(y1) - 1);
            bool radial = p.TryRadialExtent(out float cx, out float cy, out float rr);
            if (radial)
            {
                ix0 = Math.Max(ix0, (int)Math.Floor(cx - rr)); ix1 = Math.Min(ix1, (int)Math.Ceiling(cx + rr));
                iy0 = Math.Max(iy0, (int)Math.Floor(cy - rr)); iy1 = Math.Min(iy1, (int)Math.Ceiling(cy + rr));
            }
            for (int py = iy0; py <= iy1; py++)
            {
                float cyov = Math.Min(py + 1, y1) - Math.Max(py, y);
                if (cyov <= 0f) continue;
                for (int px = ix0; px <= ix1; px++)
                {
                    float cxov = Math.Min(px + 1, x1) - Math.Max(px, x);
                    if (cxov <= 0f) continue;
                    Blend(px, py, cxov * cyov, p.At(px + 0.5f, py + 0.5f));
                }
            }
        }

        public void FillRect(float x, float y, float w, float h, Rgba c) => FillRect(x, y, w, h, Paint.Solid(c));

        /// <summary>ctx.strokeRect: the 4 bands of width lw centred on the rectangle's edges (corners covered once).</summary>
        public void StrokeRect(float x, float y, float w, float h, float lw, Rgba c)
        {
            float o = lw / 2f;
            FillRect(x - o, y - o, w + lw, lw, c);                 // top
            FillRect(x - o, y + h - o, w + lw, lw, c);             // bottom
            FillRect(x - o, y + o, lw, h - lw, c);                 // left
            FillRect(x + w - o, y + o, lw, h - lw, c);             // right
        }

        /// <summary>ctx.arc(x, y, r, 0, 2 pi) + fill: coverage clamp(r + .5 - d) at pixel centres.</summary>
        public void FillCircle(float cx, float cy, float r, Rgba c)
        {
            int x0 = Math.Max(0, (int)Math.Floor(cx - r - 1)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx + r + 1));
            int y0 = Math.Max(0, (int)Math.Floor(cy - r - 1)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy + r + 1));
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                    float cov = r + 0.5f - (float)Math.Sqrt(dx * dx + dy * dy);
                    if (r < 0.5f) cov = Math.Min(cov, 2f * r);     // tiny discs cover at most their area
                    if (cov > 0f) Blend(px, py, cov > 1f ? 1f : cov, c);
                }
        }

        /// <summary>ctx.ellipse(x, y, rx, ry, 0, 0, 2 pi) + fill, 4x4 supersampled.</summary>
        public void FillEllipse(float cx, float cy, float rx, float ry, Rgba c)
        {
            int x0 = Math.Max(0, (int)Math.Floor(cx - rx)), x1 = Math.Min(W - 1, (int)Math.Ceiling(cx + rx));
            int y0 = Math.Max(0, (int)Math.Floor(cy - ry)), y1 = Math.Min(H - 1, (int)Math.Ceiling(cy + ry));
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float dx = (px + (sx + 0.5f) / 4f - cx) / rx, dy = (py + (sy + 0.5f) / 4f - cy) / ry;
                            if (dx * dx + dy * dy <= 1f) hits++;
                        }
                    if (hits > 0) Blend(px, py, hits / 16f, c);
                }
        }

        /// <summary>Fill a closed polygon (non-zero winding), 4x4 supersampled inside its bounding box. For small shapes.</summary>
        public void FillPolygon(IList<float> xy, Paint p) { var cov = PolygonCoverage(xy); ApplyCoverage(cov, p); }

        public float[] PolygonCoverage(IList<float> xy)
        {
            var cov = new float[W * H];
            int n = xy.Count / 2;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, xy[2 * i]); maxX = Math.Max(maxX, xy[2 * i]);
                minY = Math.Min(minY, xy[2 * i + 1]); maxY = Math.Max(maxY, xy[2 * i + 1]);
            }
            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(W - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(H - 1, (int)Math.Ceiling(maxY));
            for (int py = y0; py <= y1; py++)
                for (int sy = 0; sy < 4; sy++)
                {
                    float y = py + (sy + 0.5f) / 4f;
                    for (int px = x0; px <= x1; px++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float x = px + (sx + 0.5f) / 4f;
                            if (Winding(xy, n, x, y) != 0) cov[py * W + px] += 1f / 16f;
                        }
                }
            return cov;
        }

        static int Winding(IList<float> xy, int n, float x, float y)
        {
            int w = 0;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = xy[2 * i], yi = xy[2 * i + 1], xj = xy[2 * j], yj = xy[2 * j + 1];
                if (yj <= y)
                {
                    if (yi > y && (xi - xj) * (y - yj) - (x - xj) * (yi - yj) > 0f) w++;
                }
                else if (yi <= y && (xi - xj) * (y - yj) - (x - xj) * (yi - yj) < 0f) w--;
            }
            return w;
        }

        public void ApplyCoverage(float[] cov, Paint p)
        {
            for (int py = 0; py < H; py++)
                for (int px = 0; px < W; px++)
                {
                    float c = cov[py * W + px];
                    if (c > 0f) Blend(px, py, c, p.At(px + 0.5f, py + 0.5f));
                }
        }

        /// <summary>The filled region below a polyline ridge: the hills' paths (moveTo(0, h), lineTo(x_i, y_i)..., lineTo(w, h)).
        /// Coverage per pixel averages 4 sub-columns of the vertical overlap with the area under the ridge.</summary>
        public void FillBelowRidge(float[] ridgeX, float[] ridgeY, Rgba c)
        {
            int n = ridgeX.Length;
            for (int px = 0; px < W; px++)
            {
                for (int sx = 0; sx < 4; sx++)
                {
                    float x = px + (sx + 0.5f) / 4f;
                    float y = RidgeAt(ridgeX, ridgeY, n, x);
                    int top = Math.Max(0, (int)Math.Floor(y));
                    for (int py = top; py < H; py++)
                    {
                        float cov = Math.Min(1f, Math.Max(0f, py + 1 - y));
                        if (cov > 0f) _accum[py * W + px] += cov * 0.25f;
                    }
                }
            }
            for (int i = 0; i < _accum.Length; i++)
            {
                float a = _accum[i];
                if (a > 0f) { Blend(i % W, i / W, a, c); _accum[i] = 0f; }
            }
        }

        float[] _accumStore;
        float[] _accum => _accumStore ?? (_accumStore = new float[W * H]);

        static float RidgeAt(float[] xs, float[] ys, int n, float x)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < n; i++)
                if (x <= xs[i])
                {
                    float t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
                    return ys[i - 1] + (ys[i] - ys[i - 1]) * t;
                }
            return ys[n - 1];
        }

        /// <summary>ctx.stroke of polylines (one stroke() call = one union): coverage clamp(lw/2 + .5 - distance) at pixel
        /// centres, taking the max over the segments so overlapping subpaths are not double-blended.</summary>
        public void StrokePolylines(List<float[]> lines, float lw, Rgba c)
        {
            float half = lw / 2f;
            var cov = new Dictionary<int, float>();
            foreach (var pts in lines)
                for (int s = 0; s + 3 < pts.Length; s += 2)
                {
                    float ax = pts[s], ay = pts[s + 1], bx = pts[s + 2], by = pts[s + 3];
                    int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - half - 1)), x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(ax, bx) + half + 1));
                    int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - half - 1)), y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(ay, by) + half + 1));
                    for (int py = y0; py <= y1; py++)
                        for (int px = x0; px <= x1; px++)
                        {
                            float d = SegmentDistance(px + 0.5f, py + 0.5f, ax, ay, bx, by, out float past);
                            float v = Math.Min(1f, half + 0.5f - d);
                            if (lw < 1f) v = Math.Min(v, lw);
                            // Butt caps: past a segment end coverage falls off along the segment (a neighbouring
                            // segment of the same polyline covers its joint as "inside").
                            if (past > 0f) v = Math.Min(v, 0.5f - past);
                            if (v <= 0f) continue;
                            int key = py * W + px;
                            if (!cov.TryGetValue(key, out float old) || v > old) cov[key] = v;
                        }
                }
            foreach (var kv in cov) Blend(kv.Key % W, kv.Key / W, kv.Value, c);
        }

        /// <summary>Perpendicular distance to the segment's line; <paramref name="past"/> = how far beyond an end the
        /// projection lies along the segment (0 when it falls on the segment).</summary>
        static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by, out float past)
        {
            float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            if (len2 <= 0f) { past = 0f; return (float)Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay)); }
            float len = (float)Math.Sqrt(len2);
            float t = ((px - ax) * dx + (py - ay) * dy) / len2;
            past = t < 0f ? -t * len : t > 1f ? (t - 1f) * len : 0f;
            float qx = ax + dx * t - px, qy = ay + dy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        // ------------------------------------------------------------------ paths

        /// <summary>A cubic Bezier from (x0, y0) flattened into n segments appended to xy (the start point excluded).</summary>
        public static void Bezier(List<float> xy, float x0, float y0, float c1x, float c1y, float c2x, float c2y, float x1, float y1, int n = 48)
        {
            for (int i = 1; i <= n; i++)
            {
                float t = (float)i / n, u = 1f - t;
                float a = u * u * u, b = 3f * u * u * t, c = 3f * u * t * t, d = t * t * t;
                xy.Add(a * x0 + b * c1x + c * c2x + d * x1);
                xy.Add(a * y0 + b * c1y + c * c2y + d * y1);
            }
        }

        /// <summary>A quadratic curve from (x0, y0) as a polyline including the start point.</summary>
        public static float[] Quadratic(float x0, float y0, float cx, float cy, float x1, float y1, int n = 24)
        {
            var pts = new float[(n + 1) * 2];
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n, u = 1f - t;
                pts[2 * i] = u * u * x0 + 2f * u * t * cx + t * t * x1;
                pts[2 * i + 1] = u * u * y0 + 2f * u * t * cy + t * t * y1;
            }
            return pts;
        }

        // ------------------------------------------------------------------ image data

        /// <summary>getImageData: the canvas quantised to bytes (straight alpha), row 0 = top.</summary>
        public byte[] GetBytes()
        {
            var b = new byte[W * H * 4];
            for (int i = 0; i < Px.Length; i++) b[i] = RoomTexGen.ClampByte(Px[i] * 255.0);
            return b;
        }

        /// <summary>putImageData of byte data.</summary>
        public void PutBytes(byte[] b)
        {
            for (int i = 0; i < Px.Length; i++) Px[i] = b[i] / 255f;
        }
    }
}
