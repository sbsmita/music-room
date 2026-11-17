// The page textures (sheet.md 7.1-7.4): one Texture2DArray of 4 slices, 1024 x 781 R8 (3.2 texels per mm), linear, with
// mips, 4.26 MB. A slice holds a page; the open spread uses two, a turn's destination the other two. A page is drawn in
// three steps off the main thread's frame budget: its draw list is built and packed on a worker (managed, a few ms), the
// Burst job rasterises it with its mip chain (SheetRasterJob), and the result is uploaded in EndFrame: with a GPU through a
// small staging texture and Graphics.CopyTexture (1 MB per page, the array itself is not readable); without one (batchmode
// tests) through the readable array's SetPixelData. A page number outside the book is a blank sheet.
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MusicRoom.Sheet.Engrave;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Sheet.Raster
{
    public sealed class SheetPages : IDisposable
    {
        public const int Slices = 4;
        public const float Ppm = 3.2f;
        public static readonly int W = (int)Math.Floor(SheetStyle.PageW * Ppm + 0.5), H = (int)Math.Floor(SheetStyle.PageH * Ppm + 0.5);
        public const int Unassigned = int.MinValue;
        const float LaneIdleRelease = 3f;

        public Texture2DArray Texture { get; private set; }
        public readonly bool Gpu;
        public int MipCount { get; }
        /// <summary>Bytes of the array on the GPU (all slices, all mips).</summary>
        public long TextureBytes => (long)RasterLane.MipChainBytes(W, H) * Slices;

        enum State : byte { Empty, Queued, Building, Rastering, Ready }

        struct Slot
        {
            public int Page;
            public State State;
            public int LastUse;
            public Task<PackedPage> Build;
            public int Lane;
            public long Stamp;
        }

        readonly Slot[] _slots = new Slot[Slices];
        readonly RasterLane[] _lanes = new RasterLane[2];
        readonly Texture2D[] _staging = new Texture2D[2];
        readonly float[] _laneIdle = new float[2];
        readonly int[] _laneSlot = { -1, -1 };
        readonly Stopwatch _clock = Stopwatch.StartNew();
        NativeArray<byte> _blank;
        int _use;
        SheetBook _book;
        bool _pinnedSet;
        readonly int[] _pinned = new int[8];
        int _pinnedCount;

        /// <summary>Raster timings (device item S5): the last page's build and raster ms.</summary>
        public double LastBuildMs { get; private set; }
        public double LastRasterMs { get; private set; }
        public int PagesRastered { get; private set; }

        public SheetPages(bool gpu)
        {
            Gpu = gpu;
            MipCount = RasterLane.MipCount(W, H);
            Texture = new Texture2DArray(W, H, Slices, TextureFormat.R8, true, true)
            {
                name = "SheetPages", filterMode = FilterMode.Trilinear, anisoLevel = 2, mipMapBias = -0.4f, wrapMode = TextureWrapMode.Clamp,
            };
            for (int i = 0; i < Slices; i++) _slots[i] = new Slot { Page = Unassigned, State = State.Empty, Lane = -1 };
            for (int i = 0; i < _lanes.Length; i++) _lanes[i] = new RasterLane(W, H);
            _blank = new NativeArray<byte>(RasterLane.MipChainBytes(W, H), Allocator.Persistent, NativeArrayOptions.ClearMemory);
            // blank paper everywhere until the first pages arrive (never pink): the array starts zeroed
            Texture.Apply(false, Gpu);
        }

        public void SetBook(SheetBook book) => _book = book;

        /// <summary>The slice showing `page`, or -1 when it is not resident and ready.</summary>
        public int SliceOf(int page)
        {
            for (int i = 0; i < Slices; i++) if (_slots[i].Page == page && _slots[i].State == State.Ready) return i;
            return -1;
        }

        public bool IsReady(int page) => SliceOf(page) >= 0;

        /// <summary>Pages that must stay resident this frame (the open spread, the turning leaves, a pending turn).</summary>
        public void BeginPins() { _pinnedCount = 0; _pinnedSet = true; }
        public void Pin(int page) { if (_pinnedCount < _pinned.Length) _pinned[_pinnedCount++] = page; }
        bool Pinned(int page) { for (int i = 0; i < _pinnedCount; i++) if (_pinned[i] == page) return true; return false; }

        /// <summary>Ask for a page (allocation-free when it is resident). Returns its slice when ready, else -1.</summary>
        public int Request(int page)
        {
            for (int i = 0; i < Slices; i++)
                if (_slots[i].Page == page)
                {
                    _slots[i].LastUse = ++_use;
                    return _slots[i].State == State.Ready ? i : -1;
                }
            // a free or least recently used, unpinned slice
            int best = -1;
            for (int i = 0; i < Slices; i++)
            {
                if (_slots[i].Page == Unassigned) { best = i; break; }
                if (_pinnedSet && Pinned(_slots[i].Page)) continue;
                if (_slots[i].State == State.Building || _slots[i].State == State.Rastering) continue;
                if (best < 0 || _slots[i].LastUse < _slots[best].LastUse) best = i;
            }
            if (best < 0) return -1;
            _slots[best] = new Slot { Page = page, State = State.Queued, LastUse = ++_use, Lane = -1 };
            return -1;
        }

        /// <summary>Main thread, once a frame: start builds, schedule rasters, upload finished pages, free idle lanes.</summary>
        public void Tick(float dt)
        {
            if (_book == null) return;
            for (int i = 0; i < Slices; i++)
            {
                ref var s = ref _slots[i];
                switch (s.State)
                {
                    case State.Queued:
                        if (_book.Page(s.Page) == null) { UploadBlank(i); s.State = State.Ready; break; }
                        int lane = FreeLane();
                        if (lane < 0) break;
                        _laneSlot[lane] = i;
                        s.Lane = lane;
                        s.Stamp = _clock.ElapsedTicks;
                        s.Build = StartBuild(_book, s.Page);   // (a lambda here would allocate its closure for every slot every frame)
                        s.State = State.Building;
                        break;
                    case State.Building:
                        if (!s.Build.IsCompleted) break;
                        if (s.Build.IsFaulted)
                        {
                            Debug.LogError($"[Sheet] page {s.Page} draw list failed: {s.Build.Exception?.GetBaseException()}");
                            _laneSlot[s.Lane] = -1; s.Lane = -1; s.Build = null;
                            UploadBlank(i); s.State = State.Ready;
                            break;
                        }
                        var packed = s.Build.Result;
                        s.Build = null;
                        LastBuildMs = packed.BuildMs;
                        s.Stamp = _clock.ElapsedTicks;
                        _lanes[s.Lane].Schedule(packed, mips: true);
                        s.State = State.Rastering;
                        break;
                    case State.Rastering:
                        var ln = _lanes[s.Lane];
                        if (!ln.IsCompleted) break;
                        ln.Complete();
                        LastRasterMs = (_clock.ElapsedTicks - s.Stamp) * 1000.0 / Stopwatch.Frequency;
                        Upload(i, ln.Pixels, s.Lane);
                        PagesRastered++;
                        Debug.Log($"[Sheet] raster page {s.Page}: build {LastBuildMs:F1} ms, raster {LastRasterMs:F1} ms ({ln.Packed.PointCount} points)");
                        _laneSlot[s.Lane] = -1;
                        _laneIdle[s.Lane] = 0f;
                        s.Lane = -1;
                        s.State = State.Ready;
                        break;
                }
            }
            for (int l = 0; l < _lanes.Length; l++)
            {
                if (_laneSlot[l] >= 0 || !_lanes[l].Allocated) continue;
                _laneIdle[l] += dt;
                if (_laneIdle[l] > LaneIdleRelease) _lanes[l].Release();
            }
        }

        static Task<PackedPage> StartBuild(SheetBook book, int page) => Task.Run(() =>
        {
            var t0 = Stopwatch.StartNew();
            var p = PackedPage.Pack(book.PageDraws(page), Ppm, page);
            p.BuildMs = t0.Elapsed.TotalMilliseconds;
            return p;
        });

        int FreeLane() { for (int l = 0; l < _lanes.Length; l++) if (_laneSlot[l] < 0 && !_lanes[l].Busy) return l; return -1; }

        /// <summary>Busy building or rastering (tests wait on it).</summary>
        public bool Pending { get { for (int i = 0; i < Slices; i++) if (_slots[i].State != State.Ready && _slots[i].State != State.Empty) return true; return false; } }

        void UploadBlank(int slice) => Upload(slice, _blank, -1);

        void Upload(int slice, NativeArray<byte> pixels, int lane)
        {
            if (Gpu)
            {
                int k = lane >= 0 ? lane : 0;
                if (_staging[k] == null)
                    _staging[k] = new Texture2D(W, H, TextureFormat.R8, true, true) { name = "SheetPageStaging" + k, filterMode = FilterMode.Bilinear };
                var st = _staging[k];
                int w = W, h = H, off = 0;
                for (int m = 0; m < MipCount; m++)
                {
                    st.SetPixelData(pixels, m, off);
                    off += w * h; w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1);
                }
                st.Apply(false, false);
                for (int m = 0; m < MipCount; m++) Graphics.CopyTexture(st, 0, m, Texture, slice, m);
            }
            else
            {
                int w = W, h = H, off = 0;
                for (int m = 0; m < MipCount; m++)
                {
                    Texture.SetPixelData(pixels, m, slice, off);
                    off += w * h; w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1);
                }
                Texture.Apply(false, false);
            }
        }

        /// <summary>Test read-back: the page in a slice, and the R8 mip-0 byte at (x, y) (CPU path only).</summary>
        public int PageIn(int slice) => _slots[slice].Page;
        public byte ReadTexel(int slice, int x, int y) => Gpu ? (byte)0 : Texture.GetPixelData<byte>(0, slice)[y * W + x];

        public void Dispose()
        {
            for (int i = 0; i < Slices; i++)
            {
                try { _slots[i].Build?.Wait(2000); } catch (Exception) { }
                _slots[i].Build = null;
            }
            foreach (var l in _lanes) l.Release();
            if (_blank.IsCreated) _blank.Dispose();
            foreach (var t in _staging) if (t != null) UnityEngine.Object.Destroy(t);
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }
}
