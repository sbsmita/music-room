// The page rasteriser (the Node sheet lab's raster fillPath, 1:1): nonzero winding over all contours of a group
// at once (overlapping strokes union exactly), 16 sub-scanlines per pixel row, exact horizontal span coverage through an
// accumulator plus a prefix-summed delta row, stable insertion sorts, float32 storage; groups composite as
// dst + c (1 - dst). The lab is JavaScript: every value it keeps in a Float32Array (edges, crossings, the coverage and
// span rows) is a float here and every intermediate is a double, in the same order, so the C# pages are bit-identical
// with the lab's. FloatMode.Strict keeps Burst from fusing or reordering any of it.
// Then the mip chain: a 2 x 2 box filter of the linear coverage, each level quantised to R8 (round(c * 255)).
// Input: points (pixel units, x right, y DOWN), contour ends, group ends, group alphas. Scratch sized by SheetRaster.
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace MusicRoom.Sheet.Raster
{
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public unsafe struct SheetRasterJob : IJob
    {
        public const int Sub = 16;
        public int W, H;
        public int GroupCount;
        /// <summary>Generate the R8 mip chain into Pixels (false: coverage only, for the tests' raw vectors).</summary>
        public bool Mips;

        [ReadOnly] public NativeArray<double> Points;        // x, y pairs
        [ReadOnly] public NativeArray<int> ContourEnd;       // point index (exclusive) where contour c ends
        [ReadOnly] public NativeArray<int> GroupEnd;         // contour index (exclusive) where group g ends
        [ReadOnly] public NativeArray<double> GroupAlpha;

        public NativeArray<float> Coverage;                  // W * H, cleared by the job
        public NativeArray<byte> Pixels;                     // every mip level, level 0 first
        public NativeArray<float> MipScratch;                // >= sum of the levels below 0

        // scratch
        public NativeArray<float> Edges;                     // 5 per edge
        public NativeArray<int> Order, Active;
        public NativeArray<float> Xs;
        public NativeArray<sbyte> Ws;
        public NativeArray<float> Acc, Del;                  // W + 2

        // raw pointers for the hot loops (no per-access safety checks; identical arithmetic)
        [NativeDisableUnsafePtrRestriction] double* _pts;
        [NativeDisableUnsafePtrRestriction] int* _cEnd;
        [NativeDisableUnsafePtrRestriction] float* _cov;
        [NativeDisableUnsafePtrRestriction] float* _edges;
        [NativeDisableUnsafePtrRestriction] float* _xs;
        [NativeDisableUnsafePtrRestriction] float* _acc;
        [NativeDisableUnsafePtrRestriction] float* _del;
        [NativeDisableUnsafePtrRestriction] int* _order;
        [NativeDisableUnsafePtrRestriction] int* _active;
        [NativeDisableUnsafePtrRestriction] sbyte* _ws;

        public void Execute()
        {
            _pts = (double*)Points.GetUnsafeReadOnlyPtr(); _cEnd = (int*)ContourEnd.GetUnsafeReadOnlyPtr();
            _cov = (float*)Coverage.GetUnsafePtr(); _edges = (float*)Edges.GetUnsafePtr(); _xs = (float*)Xs.GetUnsafePtr();
            _acc = (float*)Acc.GetUnsafePtr(); _del = (float*)Del.GetUnsafePtr(); _order = (int*)Order.GetUnsafePtr();
            _active = (int*)Active.GetUnsafePtr(); _ws = (sbyte*)Ws.GetUnsafePtr();
            for (int i = 0; i < W * H; i++) _cov[i] = 0f;
            int c0 = 0;
            for (int g = 0; g < GroupCount; g++)
            {
                int c1 = GroupEnd[g];
                Fill(c0, c1, GroupAlpha[g]);
                c0 = c1;
            }
            if (Mips) BuildMips();
        }

        void Fill(int cFirst, int cEnd, double alpha)
        {
            int ne = 0;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            for (int c = cFirst; c < cEnd; c++)
            {
                int p0 = c == 0 ? 0 : _cEnd[c - 1], p1 = _cEnd[c];
                int n = p1 - p0;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    double x0 = _pts[2 * (p0 + i)], y0 = _pts[2 * (p0 + i) + 1], x1 = _pts[2 * (p0 + j)], y1 = _pts[2 * (p0 + j) + 1];
                    if (x0 < minX) minX = x0;
                    if (x0 > maxX) maxX = x0;
                    if (y0 < minY) minY = y0;
                    if (y0 > maxY) maxY = y0;
                    if (y0 == y1) continue;
                    float dir = 1f;
                    if (y0 > y1) { double t = x0; x0 = x1; x1 = t; t = y0; y0 = y1; y1 = t; dir = -1f; }
                    int o = ne * 5;
                    _edges[o] = (float)x0; _edges[o + 1] = (float)y0; _edges[o + 2] = (float)x1; _edges[o + 3] = (float)y1; _edges[o + 4] = dir;
                    ne++;
                }
            }
            if (ne == 0) return;
            int px0 = Max(0, FloorI(minX)), px1 = Min(W - 1, FloorI(maxX));
            int py0 = Max(0, FloorI(minY)), py1 = Min(H - 1, FloorI(maxY));
            if (px0 > px1 || py0 > py1) return;
            // sort edge indices by y0 (stable insertion sort)
            for (int i = 0; i < ne; i++) _order[i] = i;
            for (int i = 1; i < ne; i++)
            {
                int k = _order[i];
                double ky = _edges[k * 5 + 1];
                int j = i - 1;
                while (j >= 0 && _edges[_order[j] * 5 + 1] > ky) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = k;
            }
            int na = 0, next = 0;
            const double inv = 1.0 / Sub;
            for (int py = py0; py <= py1; py++)
            {
                while (next < ne && _edges[_order[next] * 5 + 1] < py + 1) _active[na++] = _order[next++];
                int kk = 0;
                for (int i = 0; i < na; i++) if (_edges[_active[i] * 5 + 3] > py) _active[kk++] = _active[i];
                na = kk;
                if (na == 0) continue;
                int span = px1 - px0 + 2;
                for (int i = 0; i < span; i++) { _acc[i] = 0f; _del[i] = 0f; }
                bool any = false;
                for (int s = 0; s < Sub; s++)
                {
                    double ys = py + (s + 0.5) * inv;
                    int nx = 0;
                    for (int i = 0; i < na; i++)
                    {
                        int o = _active[i] * 5;
                        double ey0 = _edges[o + 1], ey1 = _edges[o + 3];
                        if (ys < ey0 || ys >= ey1) continue;
                        double ex0 = _edges[o], ex1 = _edges[o + 2];
                        double x = ex0 + (ys - ey0) * (ex1 - ex0) / (ey1 - ey0);
                        int j = nx - 1;
                        while (j >= 0 && _xs[j] > x) { _xs[j + 1] = _xs[j]; _ws[j + 1] = _ws[j]; j--; }
                        _xs[j + 1] = (float)x; _ws[j + 1] = (sbyte)_edges[o + 4];
                        nx++;
                    }
                    int wsum = 0;
                    for (int i = 0; i < nx - 1; i++)
                    {
                        wsum += _ws[i];
                        if (wsum == 0) continue;
                        double xa = _xs[i], xb = _xs[i + 1];
                        if (xb <= xa) continue;
                        if (xa < px0) xa = px0;
                        if (xb > px1 + 1) xb = px1 + 1;
                        if (xb <= xa) continue;
                        Span(xa - px0, xb - px0, inv);
                        any = true;
                    }
                }
                if (!any) continue;
                double run = 0;
                int row = py * W;
                for (int i = 0; i <= px1 - px0; i++)
                {
                    run += _del[i];
                    double c = _acc[i] + run;
                    if (c <= 0) continue;
                    if (c > 1) c = 1;
                    c *= alpha;
                    int d = row + px0 + i;
                    double old = _cov[d];
                    _cov[d] = (float)(old + c * (1 - old));
                }
            }
        }

        void Span(double xa, double xb, double inv)
        {
            int ia = FloorI(xa), ib = FloorI(xb);
            if (ia == ib) { _acc[ia] = (float)(_acc[ia] + (xb - xa) * inv); return; }
            _acc[ia] = (float)(_acc[ia] + (ia + 1 - xa) * inv);
            if (ib > ia + 1) { _del[ia + 1] = (float)(_del[ia + 1] + inv); _del[ib] = (float)(_del[ib] - inv); }
            _acc[ib] = (float)(_acc[ib] + (xb - ib) * inv);
        }

        void BuildMips()
        {
            int n0 = W * H;
            for (int i = 0; i < n0; i++) Pixels[i] = Quantise(_cov[i]);
            int w = W, h = H, srcOff = -1, dstOff = 0, pixOff = n0;
            while (w > 1 || h > 1)
            {
                int nw = w > 1 ? w >> 1 : 1, nh = h > 1 ? h >> 1 : 1;
                for (int y = 0; y < nh; y++)
                {
                    int y0 = Min(h - 1, 2 * y), y1 = Min(h - 1, 2 * y + 1);
                    for (int x = 0; x < nw; x++)
                    {
                        int x0 = Min(w - 1, 2 * x), x1 = Min(w - 1, 2 * x + 1);
                        double s = Src(srcOff, w, x0, y0) + Src(srcOff, w, x1, y0) + Src(srcOff, w, x0, y1) + Src(srcOff, w, x1, y1);
                        float v = (float)(s * 0.25);
                        MipScratch[dstOff + y * nw + x] = v;
                        Pixels[pixOff + y * nw + x] = Quantise(v);
                    }
                }
                srcOff = dstOff;
                dstOff += nw * nh;
                pixOff += nw * nh;
                w = nw; h = nh;
            }
        }

        double Src(int off, int w, int x, int y) => off < 0 ? _cov[y * w + x] : MipScratch[off + y * w + x];

        static byte Quantise(double c)
        {
            double v = c * 255 + 0.5;
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)FloorI(v);
        }

        static int FloorI(double v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }
        static int Max(int a, int b) => a > b ? a : b;
        static int Min(int a, int b) => a < b ? a : b;
    }
}
