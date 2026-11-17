// One unmanaged, 64-byte aligned arena for the whole graph (spec 10-upgrade/burst.md section 4). The host allocates
// Bytes(config) once at start-up (UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent)) and Layout carves every array
// out of it. Nothing is allocated again on the audio thread: no GC, no NativeArray safety handles, cache-friendly, and
// trivially freed. Engine additions after the spec's arrays: the one-shot pool (section 5.4), the unit slots and the
// per-source "used this block" flags (ENGINE-API.md). Units' own buffers come from AudioGraphHost.Alloc.
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    public struct GraphConfig
    {
        public int SampleRate, MaxFrames, Sources, Banks, Groups, StrikeModes, Pending, RingCapacity, Budget;
        /// <summary>Engine additions: one-shot live slots, ghost slots and presets (0 = no one-shot pool).</summary>
        public int OneShots, Ghosts, Presets;

        /// <summary>Quest defaults (section 4): 8192-frame safety cap like SynthSource.MaxFrames, 18 sources,
        /// 160 banks (88 keys + 3 boards + 10 kit pieces + 22 strings' bodies + spare), 2048 groups = 8192 modes;
        /// one-shots 64 live + 16 ghosts (section 5.4), 64 presets.</summary>
        public static GraphConfig QuestDefault(int sampleRate) => new GraphConfig
        {
            SampleRate = sampleRate, MaxFrames = 8192, Sources = (int)SourceId.Count, Banks = 160, Groups = 2048, StrikeModes = 8192,
            Pending = 512, RingCapacity = 1024, Budget = 1200, OneShots = 64, Ghosts = 16, Presets = 64,
        };
    }

    public static unsafe class GraphArena
    {
        const int Align = 64;
        static long Up(long n) => (n + Align - 1) & ~(long)(Align - 1);

        public static long Bytes(in GraphConfig c)
        {
            long n = 0;
            n += Up((long)c.Sources * c.MaxFrames * 4);           // Out
            n += 2 * Up((long)c.MaxFrames * 16);                    // Acc, AccFade
            n += Up((long)c.MaxFrames * 4);                         // Exc
            n += Up((long)c.Groups * sizeof(ModalGroup));           // Groups
            n += 2 * Up((long)c.Groups * 16);                       // P0r, P0i
            n += 2 * Up(c.Groups);                                  // Active, Fading
            n += Up((long)c.Groups * 4);                            // List
            n += 2 * Up((long)c.Groups * 4);                        // Energy, Weight
            n += Up((long)c.StrikeModes * 16);                      // Strike
            n += Up((long)c.Banks * sizeof(ModalBank));             // Banks
            n += 2 * Up((long)c.Pending * sizeof(GraphEvent));      // Pending, Due
            n += 3 * Up((long)c.Groups * 4) + Up(c.Groups);         // LodBucket, LodGroup, LodBank, LodProt
            n += 2 * Up((long)c.Groups * 4);                        // ScratchA, ScratchB
            n += Up((long)c.RingCapacity * sizeof(GraphCommand));   // ring slots
            n += 2 * Align;                                         // ring head, tail (own lines)
            // engine additions
            int slots = c.OneShots + c.Ghosts;
            n += Up((long)slots * sizeof(OneShotSlot));             // Slots
            n += Up((long)slots * sizeof(OneShotVoice));            // Voices
            n += Up((long)c.Presets * sizeof(OneShotPreset));       // Presets
            n += slots > 0 ? Up((long)c.MaxFrames * 16) : 0;        // Scratch4 (one-shot lanes)
            n += Up((long)(int)UnitId.Count * sizeof(UnitSlot));    // Units
            n += Up(c.Sources);                                     // SourceUsed
            return n;
        }

        /// <summary>Carves the arrays out of mem (Bytes(c) bytes, 64-byte aligned, zeroed by the caller) into *g.</summary>
        public static void Layout(GraphState* g, byte* mem, in GraphConfig c)
        {
            byte* p = mem;
            byte* Take(long bytes) { byte* r = p; p += Up(bytes); return r; }
            *g = default;
            g->SampleRate = c.SampleRate; g->MaxFrames = c.MaxFrames; g->SourceCount = c.Sources;
            g->Out = (float*)Take((long)c.Sources * c.MaxFrames * 4);
            g->Acc = (float4*)Take((long)c.MaxFrames * 16);
            g->AccFade = (float4*)Take((long)c.MaxFrames * 16);
            g->Exc = (float*)Take((long)c.MaxFrames * 4);
            g->Groups = (ModalGroup*)Take((long)c.Groups * sizeof(ModalGroup));
            g->P0r = (float4*)Take((long)c.Groups * 16);
            g->P0i = (float4*)Take((long)c.Groups * 16);
            g->Active = Take(c.Groups);
            g->Fading = Take(c.Groups);
            g->List = (int*)Take((long)c.Groups * 4);
            g->Energy = (float*)Take((long)c.Groups * 4);
            g->Weight = (float*)Take((long)c.Groups * 4);
            g->Strike = (float*)Take((long)c.StrikeModes * 16);
            g->Banks = (ModalBank*)Take((long)c.Banks * sizeof(ModalBank));
            g->Pending = (GraphEvent*)Take((long)c.Pending * sizeof(GraphEvent));
            g->Due = (GraphEvent*)Take((long)c.Pending * sizeof(GraphEvent));
            g->LodBucket = (int*)Take((long)c.Groups * 4);
            g->LodGroup = (int*)Take((long)c.Groups * 4);
            g->LodBank = (int*)Take((long)c.Groups * 4);
            g->LodProt = Take(c.Groups);
            g->ScratchA = (int*)Take((long)c.Groups * 4);
            g->ScratchB = (int*)Take((long)c.Groups * 4);
            g->Ring.Slots = (GraphCommand*)Take((long)c.RingCapacity * sizeof(GraphCommand));
            g->Ring.Mask = c.RingCapacity - 1;   // RingCapacity must be a power of two
            g->Ring.Head = (int*)Take(Align);
            g->Ring.Tail = (int*)Take(Align);
            g->BankCapacity = c.Banks; g->GroupCapacity = c.Groups; g->StrikeCapacity = 4 * c.StrikeModes;
            g->PendingCapacity = c.Pending; g->Budget = c.Budget;
            g->LastBlock = long.MinValue;        // so block 0 renders
            // engine additions
            int slots = c.OneShots + c.Ghosts;
            g->Slots = (OneShotSlot*)Take((long)slots * sizeof(OneShotSlot));
            g->Voices = (OneShotVoice*)Take((long)slots * sizeof(OneShotVoice));
            g->Presets = (OneShotPreset*)Take((long)c.Presets * sizeof(OneShotPreset));
            g->Scratch4 = null;
            if (slots > 0) g->Scratch4 = (float4*)Take((long)c.MaxFrames * 16);
            g->OneShotLive = c.OneShots; g->OneShotGhosts = c.Ghosts; g->PresetCapacity = c.Presets;
            g->Units = (UnitSlot*)Take((long)(int)UnitId.Count * sizeof(UnitSlot));
            g->UnitCount = (int)UnitId.Count;
            for (int i = 0; i < (int)UnitId.Count; i++) { g->Units[i].Id = (UnitId)i; g->Units[i].Kernel = (UnitId)i; }
            g->SourceUsed = Take(c.Sources);
        }
    }
}
