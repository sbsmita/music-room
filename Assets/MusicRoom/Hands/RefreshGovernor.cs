// E5 (experience.md section 7): 90 Hz with a quality governor. Plain C#, no Unity API, no allocation per frame (a fixed
// 64-bin histogram), so EditMode tests drive it with scripted GPU times (the experience reference's governorFrame).
// RigBootstrap feeds it the app GPU time and the compositor's dropped frames every frame and applies its rung.
//
// The ladder has two rungs, 90 Hz and 72 Hz, and only the refresh rate moves: the foveation level is fixed for the whole
// session (SessionFoveation). On the headset a foveation level changed mid-session left flickering blocks in the right
// eye, so the level is set once when XR starts and never again. The ladder never steps back up within a session (the
// player must never feel quality bouncing): two bad 2 s windows in a row take 72 Hz. A window is bad when its
// 95th-percentile GPU time exceeds 90 % of the frame budget, or more than 2 % of its frames were dropped (at 1 % two
// dropped frames in a 2 s window took 72 Hz on the headset a minute into every session). The first 10 s,
// and 3 s after mixed reality is entered or left, a recentre, a pause, a focus change or a rung change, are not judged,
// and a frame longer than MaxFrameDt (a pause, the system menu) restarts the window instead of closing it. After the app
// resumes from a pause, 90 Hz is tried again once, 10 s later.
using System;

namespace MusicRoom.Hands
{
    /// <summary>The quality ladder (experience.md E5): the refresh rate only.</summary>
    public enum QualityRung : byte
    {
        /// <summary>90 Hz (the start when the display offers it).</summary>
        Hz90 = 0,
        /// <summary>72 Hz (the start without 90 Hz, and the last rung).</summary>
        Hz72 = 1,
    }

    public sealed class RefreshGovernor
    {
        public const double Window = 2.0, Warmup = 10.0, SettleAfterEvent = 3.0, RetryAfterResume = 10.0;
        public const int Bins = 64, BadWindowsToStep = 2;
        public const double BinMs = 0.3125;   // 64 bins over 0 .. 20 ms
        public const double GpuFrac = 0.90, DropFrac = 0.02;
        public const float HighHz = 90f, LowHz = 72f;
        /// <summary>A frame longer than this is a gap (a pause, the system menu, a hitch while unfocused), not a measurement.</summary>
        public const double MaxFrameDt = 0.25;
        /// <summary>The SRP foveation level for the whole session (XRDisplaySubsystem.foveatedRenderingLevel, 0 .. 1).</summary>
        public const float SessionFoveation = 0.5f;

        readonly int[] _hist = new int[Bins];
        double _quietUntil = Warmup, _wT, _retryAt = -1;
        int _wFrames, _wDropped, _bad;
        bool _has90 = true, _retried, _unfocused;

        public QualityRung Rung { get; private set; } = QualityRung.Hz90;
        /// <summary>The rung before the last change (for the log line).</summary>
        public QualityRung Previous { get; private set; } = QualityRung.Hz90;
        /// <summary>Seconds since Start (gaps count as MaxFrameDt).</summary>
        public double T { get; private set; }
        /// <summary>The last closed window: its p95 GPU ms, dropped frames, frames, bad flag, and when it closed.</summary>
        public double LastP95 { get; private set; }
        public int LastDropped { get; private set; }
        public int LastFrames { get; private set; }
        public bool LastBad { get; private set; }
        public double LastWindowAt { get; private set; }
        public int Windows { get; private set; }
        public int Steps { get; private set; }

        public bool IsLastRung => Rung == QualityRung.Hz72;
        public float Hz => HzOf(Rung);
        public float Foveation => SessionFoveation;

        public static float HzOf(QualityRung r) => r == QualityRung.Hz90 ? HighHz : LowHz;
        public static string Describe(QualityRung r) => $"{HzOf(r):0} Hz";

        /// <summary>90 Hz when the display offers it, else 72 Hz (experience.md E5 table).</summary>
        public QualityRung Start(bool has90Hz)
        {
            _has90 = has90Hz;
            T = 0;
            _quietUntil = Warmup;
            _retryAt = -1;
            _retried = false;
            _unfocused = false;
            Steps = Windows = 0;
            ResetWindow();
            _bad = 0;
            Rung = Previous = has90Hz ? QualityRung.Hz90 : QualityRung.Hz72;
            return Rung;
        }

        /// <summary>One frame: dt seconds, the app's GPU time in ms, the compositor's newly dropped frames (negative deltas
        /// are ignored). Returns true when the rung changed this frame (the caller applies Rung).</summary>
        public bool Frame(double dt, double gpuMs, int droppedDelta)
        {
            if (dt > MaxFrameDt)
            {
                // A gap: the window it would close says nothing about rendering.
                T += MaxFrameDt;
                Settle();
                return false;
            }
            T += dt;
            if (_retryAt >= 0 && T >= _retryAt)
            {
                _retryAt = -1;
                _retried = true;
                QualityRung r0 = _has90 ? QualityRung.Hz90 : QualityRung.Hz72;
                if (Rung != r0) { SetRung(r0); return true; }
            }
            if (IsLastRung || _unfocused || T < _quietUntil) return false;
            int bin = (int)Math.Floor(gpuMs / BinMs);
            _hist[bin < 0 ? 0 : bin >= Bins ? Bins - 1 : bin]++;
            _wFrames++;
            if (droppedDelta > 0) _wDropped += droppedDelta;
            _wT += dt;
            if (_wT < Window) return false;
            double budget = 1000.0 / HzOf(Rung), q = P95();
            bool bad = q > GpuFrac * budget || _wDropped > DropFrac * _wFrames;
            _bad = bad ? _bad + 1 : 0;
            LastP95 = q;
            LastDropped = _wDropped;
            LastFrames = _wFrames;
            LastBad = bad;
            LastWindowAt = T;
            Windows++;
            ResetWindow();
            if (_bad < BadWindowsToStep) return false;
            SetRung(QualityRung.Hz72);
            return true;
        }

        /// <summary>Mixed reality entered or left, a recentre, a pause or a focus change: the next 3 s are not judged (and
        /// the window restarts).</summary>
        public void Settle()
        {
            _quietUntil = Math.Max(_quietUntil, T + SettleAfterEvent);
            ResetWindow();
            _bad = 0;
        }

        /// <summary>Input focus lost (the system menu over the app) or regained: nothing is judged while unfocused, and the
        /// first 3 s after either change are not judged.</summary>
        public void SetFocused(bool focused)
        {
            _unfocused = !focused;
            Settle();
        }

        /// <summary>OVRManager.DisplayRefreshRateChanged to a rate under 90 Hz that the governor did not ask for (thermal
        /// throttling): 72 Hz, and it stays (the ladder never steps up). Returns true when it moved.</summary>
        public bool SystemDroppedRefresh()
        {
            if (Rung == QualityRung.Hz72) return false;
            SetRung(QualityRung.Hz72);
            return true;
        }

        /// <summary>OnApplicationPause(false): retry 90 Hz once, RetryAfterResume seconds later.</summary>
        public void Resumed()
        {
            Settle();
            if (_retried) return;
            _retryAt = T + RetryAfterResume;
        }

        void SetRung(QualityRung r)
        {
            Previous = Rung;
            Rung = r;
            Steps++;
            _bad = 0;
            Settle();
        }

        void ResetWindow()
        {
            Array.Clear(_hist, 0, Bins);
            _wT = 0;
            _wFrames = 0;
            _wDropped = 0;
        }

        /// <summary>The 95th percentile as the top of the bin it falls in (reference p95).</summary>
        double P95()
        {
            int target = (int)Math.Ceiling(0.95 * _wFrames), acc = 0;
            for (int i = 0; i < Bins; i++)
            {
                acc += _hist[i];
                if (acc >= target) return (i + 1) * BinMs;
            }
            return Bins * BinMs;
        }
    }
}
