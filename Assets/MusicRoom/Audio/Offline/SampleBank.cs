// Rendered note buffers shared by worker threads (writers), the audio thread and the main thread (readers).
// A slot is published once with Volatile.Write after the array is completely filled, so any reader that sees
// a non-null array sees all of its samples. Readers never block; a null slot means "still rendering".
// Slots: AudioEngine.PianoSlot0 + (midi - 21), HarpSlot0 + string, VoiceSlot0 + guide line, ChirpSlot0 + kind.
using System;
using System.Threading;

namespace MusicRoom.Audio
{
    public sealed class SampleBank
    {
        private readonly float[][] _slots;

        public SampleBank(int slotCount) { _slots = new float[slotCount][]; }

        public int SlotCount => _slots.Length;

        public float[] Get(int slot) => (uint)slot < (uint)_slots.Length ? Volatile.Read(ref _slots[slot]) : null;

        public void Publish(int slot, float[] samples) => Volatile.Write(ref _slots[slot], samples);

        public long TotalSamples()
        {
            long n = 0;
            for (int i = 0; i < _slots.Length; i++) { var s = Get(i); if (s != null) n += s.Length; }
            return n;
        }
    }

    /// <summary>Renders buffers on background threads in a given order (the piano renders outward from C4 so early presses
    /// are instant). Progress is polled from the main thread; workers never call into Unity (except
    /// the Android priority call below). Jobs render into buffers the engine allocated up front on the main thread, so the
    /// workers produce no large garbage while Sunny's intro plays.
    /// Priority: Thread.Priority is a no-op under IL2CPP on Android (libil2cpp ThreadImpl::SetPriority is empty), so on
    /// Android each worker lowers itself through android.os.Process.setThreadPriority(THREAD_PRIORITY_BACKGROUND = 10)
    /// after attaching to the JVM (detached again when it exits).</summary>
    public sealed class OfflineRenderQueue
    {
        /// <summary>android.os.Process.THREAD_PRIORITY_BACKGROUND (nice 10).</summary>
        public const int AndroidBackgroundPriority = 10;

        private readonly SampleBank _bank;
        private readonly int[] _slots;
        private readonly Func<int, float[]>[] _jobs;
        private int _count, _next = -1, _done, _failed;
        private Thread[] _threads;
        private volatile bool _cancel;
        private bool _androidPriority;
        private int _priorityLowered;

        /// <summary>Workers that lowered their OS priority (Android only; 0 elsewhere). Diagnostics.</summary>
        public int PriorityLowered => Volatile.Read(ref _priorityLowered);

        public OfflineRenderQueue(SampleBank bank, int capacity)
        {
            _bank = bank;
            _slots = new int[capacity];
            _jobs = new Func<int, float[]>[capacity];
        }

        public SampleBank Bank => _bank;
        public int Total => _count;
        public int Completed => Volatile.Read(ref _done);
        public int Failed => Volatile.Read(ref _failed);
        public float Progress => _count == 0 ? 1f : (float)Completed / _count;
        public bool IsDone => Completed >= _count;

        /// <summary>Main thread, before Start(). job(slot) returns the rendered samples (null = nothing to publish).</summary>
        public void Add(int slot, Func<int, float[]> job)
        {
            if (_threads != null) throw new InvalidOperationException("Add before Start.");
            if (_count >= _slots.Length) throw new InvalidOperationException("Render queue full.");
            _slots[_count] = slot;
            _jobs[_count] = job;
            _count++;
        }

        public void Start(int workers)
        {
            if (_threads != null) return;
            _androidPriority = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android; // main thread
            _threads = new Thread[Math.Max(1, workers)];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "MusicRoom.Render" + i };
                _threads[i].Start();
            }
        }

        public void Cancel() => _cancel = true;

        private void Run()
        {
            bool attached = _androidPriority && LowerAndroidPriority();
            try
            {
                while (!_cancel)
                {
                    int i = Interlocked.Increment(ref _next);
                    if (i >= _count) return;
                    try
                    {
                        float[] samples = _jobs[i](_slots[i]);
                        if (samples != null && _slots[i] >= 0) _bank.Publish(_slots[i], samples);
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref _failed);
                    }
                    Interlocked.Increment(ref _done);
                }
            }
            finally
            {
                if (attached) { try { UnityEngine.AndroidJNI.DetachCurrentThread(); } catch (Exception) { } }
            }
        }

        /// <summary>Worker thread, Android only: attach to the JVM and call Process.setThreadPriority(10) for this thread.
        /// Returns whether the thread is attached (Run detaches it when it exits).</summary>
        private bool LowerAndroidPriority() => LowerThisThread(ref _priorityLowered);

        /// <summary>Any worker thread on Android (the render workers, Sunny's voice worker): attach to the JVM and call
        /// android.os.Process.setThreadPriority(THREAD_PRIORITY_BACKGROUND) for the calling thread; <paramref name="lowered"/>
        /// counts successes. Returns whether the thread is attached: the caller detaches it
        /// (AndroidJNI.DetachCurrentThread) when it exits.</summary>
        public static bool LowerThisThread(ref int lowered)
        {
            try
            {
                if (UnityEngine.AndroidJNI.AttachCurrentThread() != 0) return false;
            }
            catch (Exception)
            {
                return false;
            }
            try
            {
                using (var process = new UnityEngine.AndroidJavaClass("android.os.Process"))
                    process.CallStatic("setThreadPriority", AndroidBackgroundPriority);
                Interlocked.Increment(ref lowered);
            }
            catch (Exception) { }
            return true;
        }
    }
}
