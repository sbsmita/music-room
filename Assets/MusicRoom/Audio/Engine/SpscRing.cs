// Lock-free, allocation-free single-producer / single-consumer ring (spec 04 section 2.7). Producer: the Unity
// main thread (engine API). Consumer: the audio thread (OnAudioFilterRead). Volatile.Write on the tail publishes a
// batch (release); Volatile.Read on the consumer side acquires it, so slot contents are visible before the index is.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MusicRoom.Audio
{
    public sealed class SpscRing<T> where T : struct
    {
        private readonly T[] _slots;
        private readonly int _mask;
        private PaddedIndex _head; // next slot to read; written only by the consumer
        private PaddedIndex _tail; // next slot to write; written only by the producer
        private int _staged;       // producer-local: items written but not yet published (>= _tail)

        public SpscRing(int capacityPowerOfTwo)
        {
            if (capacityPowerOfTwo < 2 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
                throw new ArgumentException("Capacity must be a power of two >= 2.", nameof(capacityPowerOfTwo));
            _slots = new T[capacityPowerOfTwo];
            _mask = capacityPowerOfTwo - 1;
        }

        public int Capacity => _slots.Length;

        /// <summary>Items published and not yet consumed (exact on either endpoint thread, a snapshot elsewhere).</summary>
        public int Count => Volatile.Read(ref _tail.Value) - Volatile.Read(ref _head.Value);

        /// <summary>Items staged but not yet published (producer thread).</summary>
        public int Staged => _staged - Volatile.Read(ref _tail.Value);

        /// <summary>Producer thread only. Returns false when the ring is full (the item is not queued).</summary>
        public bool TryEnqueue(in T item)
        {
            if (!TryStage(item)) return false;
            Commit();
            return true;
        }

        /// <summary>Producer only: write an item without publishing it. Items staged since the last Commit() become visible
        /// to the consumer together, so every voice of one drum hit starts in the same audio block.</summary>
        public bool TryStage(in T item)
        {
            int staged = _staged;                      // producer-local
            int head = Volatile.Read(ref _head.Value); // acquire the consumer's progress
            if (staged - head >= _slots.Length) return false;
            _slots[staged & _mask] = item;
            _staged = staged + 1;
            return true;
        }

        /// <summary>Producer only: publish everything staged (release).</summary>
        public void Commit() => Volatile.Write(ref _tail.Value, _staged);

        /// <summary>Consumer thread only.</summary>
        public bool TryDequeue(out T item)
        {
            int head = _head.Value;
            int tail = Volatile.Read(ref _tail.Value); // acquire published slots
            if (head == tail)
            {
                item = default;
                return false;
            }
            item = _slots[head & _mask];
            Volatile.Write(ref _head.Value, head + 1); // release: the slot may be reused
            return true;
        }
    }

    /// <summary>An int with 64 bytes either side, so producer and consumer indices never share a cache line.
    /// Must NOT be nested inside the generic ring: Mono throws "TypeLoadException: Generic class cannot have
    /// explicit layout" for an explicit-layout struct nested in a generic type (measured in Unity 6000.3).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct PaddedIndex
    {
        [FieldOffset(64)] public int Value;
    }
}
