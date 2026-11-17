// Draw lists -> flat arrays for the Burst raster job, and the job's native buffers (a "lane": one page in flight).
// Packing runs on any thread (managed arrays); a lane is scheduled and completed on the main thread. Lanes keep their
// buffers between pages and free them when idle (SheetPages), so nothing is allocated per frame.
using System;
using System.Collections.Generic;
using MusicRoom.Sheet.Engrave;
using Unity.Collections;
using Unity.Jobs;

namespace MusicRoom.Sheet.Raster
{
    /// <summary>A page's draw list in pixel units, flat (built off the main thread).</summary>
    public sealed class PackedPage
    {
        public int Number;
        public double[] Points = Array.Empty<double>();
        public int[] ContourEnd = Array.Empty<int>(), GroupEnd = Array.Empty<int>();
        public double[] Alpha = Array.Empty<double>();
        public int PointCount, ContourCount, GroupCount, MaxGroupPoints;
        /// <summary>Milliseconds spent building the draws and packing them (diagnostics, device item S5).</summary>
        public double BuildMs;

        /// <summary>rasterPage's scaling: every coordinate times ppm (double), groups in draw order, alpha per group.</summary>
        public static PackedPage Pack(List<Draw> draws, double ppm, int number = 0)
        {
            int pts = 0, cons = 0;
            foreach (var d in draws) foreach (var c in d.Parts) { if (c.Length < 2) continue; pts += c.Length >> 1; cons++; }
            var p = new PackedPage
            {
                Number = number, Points = new double[pts * 2], ContourEnd = new int[cons], GroupEnd = new int[draws.Count], Alpha = new double[draws.Count],
                PointCount = pts, ContourCount = cons, GroupCount = draws.Count,
            };
            int w = 0, ci = 0;
            for (int g = 0; g < draws.Count; g++)
            {
                int gp = 0;
                foreach (var c in draws[g].Parts)
                {
                    if (c.Length < 2) continue;
                    for (int i = 0; i < c.Length; i++) p.Points[w++] = c[i] * ppm;
                    p.ContourEnd[ci++] = w >> 1;
                    gp += c.Length >> 1;
                }
                p.GroupEnd[g] = ci;
                p.Alpha[g] = draws[g].Alpha;
                if (gp > p.MaxGroupPoints) p.MaxGroupPoints = gp;
            }
            return p;
        }
    }

    /// <summary>One raster in flight: the job's inputs, scratch and outputs (native, persistent until Dispose).</summary>
    public sealed class RasterLane : IDisposable
    {
        public readonly int W, H;
        public readonly int PixelBytes;
        NativeArray<double> _points;
        NativeArray<int> _contourEnd, _groupEnd, _order, _active;
        NativeArray<double> _alpha;
        NativeArray<float> _coverage, _mipScratch, _edges, _xs, _acc, _del;
        NativeArray<sbyte> _ws;
        NativeArray<byte> _pixels;
        JobHandle _handle;
        public bool Busy { get; private set; }
        public int Page { get; private set; } = int.MinValue;
        public PackedPage Packed { get; private set; }
        public NativeArray<byte> Pixels => _pixels;
        public NativeArray<float> Coverage => _coverage;
        public bool Allocated => _coverage.IsCreated;

        public RasterLane(int w, int h)
        {
            W = w; H = h;
            PixelBytes = MipChainBytes(w, h);
        }

        public static int MipChainBytes(int w, int h)
        {
            int n = 0;
            while (true) { n += w * h; if (w == 1 && h == 1) break; w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); }
            return n;
        }
        public static int MipCount(int w, int h) { int n = 1; while (w > 1 || h > 1) { w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); n++; } return n; }
        public static int MipOffset(int w, int h, int level) { int o = 0; for (int i = 0; i < level; i++) { o += w * h; w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); } return o; }

        static void Ensure<T>(ref NativeArray<T> a, int n) where T : struct
        {
            if (a.IsCreated && a.Length >= n) return;
            if (a.IsCreated) a.Dispose();
            a = new NativeArray<T>(Math.Max(n, 16), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>Copies a packed page in and schedules the Burst job (main thread).</summary>
        public void Schedule(PackedPage p, bool mips = true)
        {
            if (Busy) throw new InvalidOperationException("lane busy");
            Ensure(ref _coverage, W * H);
            Ensure(ref _pixels, PixelBytes);
            Ensure(ref _mipScratch, Math.Max(1, PixelBytes - W * H));
            Ensure(ref _acc, W + 2);
            Ensure(ref _del, W + 2);
            Ensure(ref _points, p.PointCount * 2);
            Ensure(ref _contourEnd, p.ContourCount);
            Ensure(ref _groupEnd, p.GroupCount);
            Ensure(ref _alpha, p.GroupCount);
            int edges = Math.Max(1, p.MaxGroupPoints);
            Ensure(ref _edges, edges * 5);
            Ensure(ref _order, edges);
            Ensure(ref _active, edges);
            Ensure(ref _xs, edges + 1);
            Ensure(ref _ws, edges + 1);
            if (p.PointCount > 0) NativeArray<double>.Copy(p.Points, _points, p.PointCount * 2);
            if (p.ContourCount > 0) NativeArray<int>.Copy(p.ContourEnd, _contourEnd, p.ContourCount);
            if (p.GroupCount > 0)
            {
                NativeArray<int>.Copy(p.GroupEnd, _groupEnd, p.GroupCount);
                NativeArray<double>.Copy(p.Alpha, _alpha, p.GroupCount);
            }
            var job = MakeJob(p, mips);
            _handle = job.Schedule();
            Busy = true;
            Page = p.Number;
            Packed = p;
        }

        /// <summary>Synchronous run on this thread (EditMode tests): `burst` false runs the same code managed.</summary>
        public void Run(PackedPage p, bool mips, bool burst = true)
        {
            Schedule(p, mips);
            if (!burst)
            {
                _handle.Complete();   // the scheduled Burst run is discarded: run again managed
                var job = MakeJob(p, mips);
                job.Execute();
            }
            Complete();
        }

        SheetRasterJob MakeJob(PackedPage p, bool mips) => new SheetRasterJob
        {
            W = W, H = H, GroupCount = p.GroupCount, Mips = mips,
            Points = _points, ContourEnd = _contourEnd, GroupEnd = _groupEnd, GroupAlpha = _alpha,
            Coverage = _coverage, Pixels = _pixels, MipScratch = _mipScratch,
            Edges = _edges, Order = _order, Active = _active, Xs = _xs, Ws = _ws, Acc = _acc, Del = _del,
        };

        public bool IsCompleted => Busy && _handle.IsCompleted;

        public void Complete()
        {
            if (!Busy) return;
            _handle.Complete();
            Busy = false;
        }

        /// <summary>Frees the native buffers (an idle lane, Shutdown).</summary>
        public void Release()
        {
            if (Busy) Complete();
            if (_points.IsCreated) _points.Dispose();
            if (_contourEnd.IsCreated) _contourEnd.Dispose();
            if (_groupEnd.IsCreated) _groupEnd.Dispose();
            if (_alpha.IsCreated) _alpha.Dispose();
            if (_order.IsCreated) _order.Dispose();
            if (_active.IsCreated) _active.Dispose();
            if (_coverage.IsCreated) _coverage.Dispose();
            if (_mipScratch.IsCreated) _mipScratch.Dispose();
            if (_edges.IsCreated) _edges.Dispose();
            if (_xs.IsCreated) _xs.Dispose();
            if (_ws.IsCreated) _ws.Dispose();
            if (_acc.IsCreated) _acc.Dispose();
            if (_del.IsCreated) _del.Dispose();
            if (_pixels.IsCreated) _pixels.Dispose();
            Packed = null;
            Page = int.MinValue;
        }

        public void Dispose() => Release();

        /// <summary>The coverage as R8 bytes (round(c * 255)), what the tests hash.</summary>
        public byte[] CoverageBytes()
        {
            var b = new byte[W * H];
            for (int i = 0; i < b.Length; i++)
            {
                double v = _coverage[i] * 255.0 + 0.5;
                b[i] = v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)Math.Floor(v);
            }
            return b;
        }

        public double InkSum()
        {
            double s = 0;
            for (int i = 0; i < W * H; i++) s += _coverage[i];
            return s;
        }
    }
}
