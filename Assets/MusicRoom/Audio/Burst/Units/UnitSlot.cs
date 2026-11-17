// Sound units: the plug-in point for the unit-only instruments (handpan, hand drums, marimba, bowls; Docs/10-upgrade/
// ENGINE-API.md). A unit is one instrument's whole DSP: its own unmanaged state, its own command ring, its own Burst
// render, called INSIDE AudioGraph.RenderBlock, the one Burst call per block (burst.md section 3). Each unit adds its
// block into its own mono output channels (one per SynthSource it plays on); the engine then mixes those channels into
// the graph's per-source buffers with the unit's fade ramp (a fade never clicks). The piano, drums, harp and room play
// the classic sound only; their upgraded units were deleted on 2026-11-11 (UnitId keeps their ids).
//
// Adding a unit touches no shared file: the unit's files implement ONE partial method of UnitHooks (the block render)
// and ONE of SoundUnits (the main-thread factory). Partial methods without an implementation compile to nothing.
using System.Runtime.InteropServices;

namespace MusicRoom.Audio.Burst
{
    /// <summary>The fixed unit ids (one per parallel workstream; Example is the engine's reference unit, used by its tests).</summary>
    /// <summary>The id block (12-new-instruments/platform.md section 2): Room stays 3, Example 4; the platform's five
    /// unit-only roles are 5-9 (UnitId value = InstrumentKind value for them). Values never move.</summary>
    public enum UnitId : byte { Piano = 0, Drums = 1, Harp = 2, Room = 3, Example = 4, Handpan = 5, HandDrums = 6, Marimba = 7, Bowls = 9, Count = 10 }

    /// <summary>UnitSlot.Flags bits, set by the engine for the unit's next Render.</summary>
    public static class UnitFlags
    {
        /// <summary>The unit was faded out and suspended (AudioGraphHost.FadeUnit) and now resumes: drop every ringing voice and pending
        /// state before rendering this block (the engine resumes at full gain, so stale state must not come back).</summary>
        public const int Reset = 1;
        /// <summary>The unit (registered with idle skip) was not rendered for one or more blocks while it was silent and had
        /// nothing due (platform.md 10.2); set on its first Render after the skip, once. Its state is as it left it (silent):
        /// a unit that keeps time inside its state (an LFO phase) may catch up here.</summary>
        public const int Resumed = 2;
    }

    /// <summary>UnitSlot.Class: the governor's priority classes (platform.md 10.4, UnitGovernor). Focus never drops.</summary>
    public static class UnitClass
    {
        /// <summary>Displayed and still ringing (or silent).</summary>
        public const byte Tail = 0;
        /// <summary>Displayed, sounding only through take / loop dispatch.</summary>
        public const byte Playback = 1;
        public const byte AtSeat = 2;
        /// <summary>Played live in the last 2 s, or faced (ILayoutService.Facing).</summary>
        public const byte Focus = 3;
        public static string Name(byte c) => c == Focus ? "focus" : c == AtSeat ? "seat" : c == Playback ? "playback" : "tail";
    }

    /// <summary>A due unit command: the GraphCommand as the main thread staged it, with its absolute sample and its offset
    /// in the block. Due events are sorted by (At, Seq): call order for equal times (80 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitEvent
    {
        public long At;
        /// <summary>At - BlockStart, in [0, Frames).</summary>
        public int Offset;
        public int Seq;
        public GraphCommand C;
    }

    /// <summary>One registered unit, as the audio thread sees it. Lives in the graph's arena (GraphState.Units[UnitId]).
    /// Fields marked "unit" are the unit's to read (and write where noted); everything else is the engine's.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UnitSlot
    {
        public const int MaxChannels = 8;

        /// <summary>The role: which instrument this unit plays.</summary>
        public UnitId Id;
        /// <summary>Which hook renders it (normally Id; tests install the Example unit in a role).</summary>
        public UnitId Kernel;
        public byte Registered;     // engine: 1 once AudioGraphHost.RegisterUnit ran
        public byte Suspended;      // engine: faded out (AudioGraphHost.FadeUnit): Render is not called and due events are dropped
        /// <summary>unit (read): UnitFlags for this Render (Reset).</summary>
        public int Flags;
        /// <summary>unit (read/write): the unit's own unmanaged state (AudioGraphHost.Alloc), set on the main thread before play.</summary>
        public void* State;
        /// <summary>unit (read): output channels and the SynthSource (SourceId) each one plays on.</summary>
        public int Channels;
        public fixed int Source[MaxChannels];
        /// <summary>unit (write): Channels x MaxFrames mono floats, channel c at Out + c * MaxFrames. Cleared by the engine before
        /// Render; the unit ADDS its block [0, Frames) into it.</summary>
        public float* Out;
        public int MaxFrames;
        public int SampleRate;
        /// <summary>main -> audio commands (AudioGraphHost.Stage / Commit through SoundUnit).</summary>
        public CommandRing Ring;
        public UnitEvent* Pending;
        /// <summary>unit (read): this block's due events, sorted by (At, Seq), Offset in [0, Frames).</summary>
        public UnitEvent* Due;
        public int PendingCount, PendingCapacity, DueCount, Seq;
        /// <summary>unit (read): the block being rendered: absolute engine sample of its first frame, and its length.</summary>
        public long BlockStart;
        public int Frames;
        /// <summary>engine: output gain ramp (unit fades). GainStart / GainEnd: the gain at the start and end of the last block.</summary>
        public float Gain, Target, Step, GainStart, GainEnd;
        /// <summary>unit (read): the whole graph (the engine's modal banks, one-shots, budget) for units that use them.</summary>
        public GraphState* Graph;
        /// <summary>stats (audio thread writes, main thread reads): commands lost to a full pending queue, onsets dropped as
        /// too late (CommandFlags.LateDrop), blocks rendered, the channels that carried signal in the last block (bits).</summary>
        public int DroppedEvents, LateDropped, ChannelsUsed, Pad1;
        public long Blocks;
        // ---- the many-instrument platform (platform.md 10.2-10.4) ----
        /// <summary>unit (read at block start): the governor's share of the unit's own caps (main -> audio, 1 = full, floor
        /// 0.4, UnitGovernor). A unit scales its caps by Graph->Budget / 1200 x Share.</summary>
        public float Share;
        /// <summary>main -> audio: the governor's class (UnitClass), for diagnostics.</summary>
        public byte Class;
        /// <summary>engine: registered with idle skip (SoundUnit.Register idleSkip); Skipping: the last block was skipped.</summary>
        public byte IdleSkip, Skipping, Pad2;
        /// <summary>unit (write): its own work count for the block (KnockDsp: mode-samples rendered), 0 when skipped. The dev
        /// HUD shows each unit's part of the block from it.</summary>
        public int Work;
        /// <summary>stats (audio thread writes, main thread reads): blocks skipped while idle.</summary>
        public int IdleBlocks;
        /// <summary>engine: consecutive rendered blocks without output (ChannelsUsed == 0).</summary>
        public int QuietBlocks, Pad3;

        /// <summary>unit: channel c's output buffer.</summary>
        public float* Channel(int c) => Out + c * MaxFrames;
    }

    /// <summary>The block hooks the sound units implement (Burst code, audio thread: no allocation, no managed objects, no
    /// locks, no Unity API). Each workstream implements exactly ONE of these partial methods in its own file:
    ///   public static unsafe partial class UnitHooks { static partial void RenderMarimba(UnitSlot* u) { ... } }
    /// RenderBlock calls it once per block with u->Due / u->Frames / u->Out set up (ENGINE-API.md section 3).</summary>
    public static unsafe partial class UnitHooks
    {
        // The platform's unit-only roles (platform.md 10.2): declared once in phase 0; each instrument's unit folder implements its
        // own (Audio/Sound/<X>/), the phase-0 stubs with a knock (KnockDsp).
        static partial void RenderHandpan(UnitSlot* u);
        static partial void RenderHandDrums(UnitSlot* u);
        static partial void RenderMarimba(UnitSlot* u);
        static partial void RenderBowls(UnitSlot* u);

        internal static void Render(UnitSlot* u)
        {
            switch (u->Kernel)
            {
                case UnitId.Example: ExampleUnitDsp.Render(u); break;
                case UnitId.Handpan: RenderHandpan(u); break;
                case UnitId.HandDrums: RenderHandDrums(u); break;
                case UnitId.Marimba: RenderMarimba(u); break;
                case UnitId.Bowls: RenderBowls(u); break;
                case KnockUnit.Kernel: KnockDsp.Render(u); break;
            }
        }
    }
}
