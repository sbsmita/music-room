// Unmanaged single-producer / single-consumer command ring (spec 10-upgrade/burst.md section 3.2). Same protocol as
// Engine/SpscRing.cs (stage, commit with one release store, acquire on the consumer), but the slots and indices live in
// unmanaged memory so the CONSUMER is Burst code (AudioGraph.RenderBlock drains it). Burst supports the non-generic
// Volatile.Read/Write (Burst manual, "System.Threading.Volatile"). Head and Tail sit on separate 64-byte lines.
// The graph owns one ring (banks, one-shots, engine ops); every sound unit owns another (ENGINE-API.md), so one unit's
// commands keep their call order and never wait behind another's.
using System.Runtime.InteropServices;
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    public enum GraphOp : byte
    {
        None = 0,
        Strike = 1,     // Target bank; A = velocity (0..1], B = strike position 0 (centre) .. 1 (edge): z += vel^e (a + (b - a) pos) e^{j phase}
        Pulse = 2,      // Target bank; A = amplitude, B = width (s): half-sine force pulse into a driven bank (hammer felt, mallet, finger)
        Damp = 3,       // Target bank; A = k >= 1: every pole radius r0 -> r0^k (decay rate x k), e.g. a piano damper landing
        Undamp = 4,     // Target bank: poles back to r0 (key pressed again)
        Silence = 5,    // Target bank: all active groups fade out over the next block and retire
        SetBudget = 6,  // N = max active modal groups (LOD budget)
        // ---- engine additions (ENGINE-API.md) ----
        OneShot = 7,    // Target = one-shot preset, N = source, A = gain, Id = voice id (0 = anonymous): a pooled noise voice (section 5.4)
        Release = 8,    // Target = one-shot voice id, A = tau (s): its envelope decays with tau from here, the slot counts as released
        UnitFade = 9,   // Target = UnitId, A = gain target (0 or 1), N = ramp samples: a sound unit fades out (then suspends) or resumes
    }

    /// <summary>GraphCommand.Flags bits (unit commands: the unit's own meaning, except LateDrop).</summary>
    public static class CommandFlags
    {
        /// <summary>Drop the command when it is due more than AudioGraph.LateDropSamples (100 ms) before the block that consumes
        /// it (the mixer was paused, e.g. headset off). Graph strikes, pulses and one-shots always behave like this; unit
        /// commands opt in per command (an onset should be dropped, a damper or a pedal never).</summary>
        public const byte LateDrop = 1;
    }

    /// <summary>One main-thread -> audio-thread message (64 bytes, no references). When: 0 = ASAP (next block start),
    /// &gt; 0 = absolute AudioSettings.dspTime (a past time starts now), &lt; 0 = -When seconds after the consuming block.
    /// Unit commands: Op is the unit's own sub-op (any byte), Target/A..D/N/Id/Seed its payload.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct GraphCommand
    {
        public GraphOp Op;
        public byte Flags;
        public short Pad;
        public int Target;
        public double When;
        public float A, B, C, D;
        public int N;
        /// <summary>Engine addition: a voice id (one-shots) or any integer payload of a unit command.</summary>
        public int Id;
        /// <summary>Engine addition: a seed or any second integer payload of a unit command.</summary>
        public uint Seed;
        public float E, F, G;   // more unit payload (spare)
    }

    public unsafe struct CommandRing
    {
        public GraphCommand* Slots;
        public int Mask;
        public int* Head;     // consumer progress (own cache line)
        public int* Tail;     // published producer index (own cache line)
        public int Staged;    // producer-local

        public int Capacity => Mask + 1;

        /// <summary>Producer (main thread) only. False when full: the caller counts a drop.</summary>
        public bool TryStage(in GraphCommand c)
        {
            int head = Volatile.Read(ref *Head);
            if (Staged - head >= Mask + 1) return false;
            Slots[Staged & Mask] = c;
            Staged++;
            return true;
        }

        /// <summary>Producer only: publish everything staged with one release store (one hit = one batch = one block).</summary>
        public void Commit() => Volatile.Write(ref *Tail, Staged);

        /// <summary>Consumer (audio thread, Burst) only.</summary>
        public bool TryDequeue(out GraphCommand c)
        {
            int head = *Head;
            int tail = Volatile.Read(ref *Tail);
            if (head == tail) { c = default; return false; }
            c = Slots[head & Mask];
            Volatile.Write(ref *Head, head + 1);
            return true;
        }
    }
}
