// The piece SynthSource.RenderBlock calls (spec 10-upgrade/burst.md section 3.2). Lets today's managed voices and the
// Burst graph coexist: the first source of a block renders the graph (one Burst call), each source then adds its own
// graph slice to its mix, after its legacy voices, processors and body resonator and before its send and bus, so the new
// instruments go through the same send (drum room), bus, spatializer and master as the classic ones.
//
// SynthSource.RenderBlock (everything else unchanged):
//     Drain(...)
//     bool graph = GraphBridge.Render(Graph, (int)id, blockStart, frames);   // NEW: renders the graph once per block
//     if (idleSkip && !graph && IsIdle()) { ... skip ... }                     // a source with graph signal is never idle
//     ... legacy voices into _mix, pending sends, processors, resonator ...
//     if (graph) GraphBridge.AddSlice(Graph, (int)id, _mix, frames);          // NEW
//     ... send, NoSend voices, bus loop writes data ...
using System.Threading;

namespace MusicRoom.Audio.Burst
{
    public static unsafe class GraphBridge
    {
        static AudioGraphHost s_host;

        /// <summary>The engine's graph (set by AudioEngine.Init on the main thread, null after shutdown). The sources hold
        /// their own reference (SynthSource.Graph); this one is for the HUD and tools.</summary>
        public static AudioGraphHost Host { get => Volatile.Read(ref s_host); set => Volatile.Write(ref s_host, value); }

        /// <summary>Audio thread: render the graph for this block if no source did yet; true when this source has graph signal.</summary>
        public static bool Render(AudioGraphHost host, int source, long blockStart, int frames)
        {
            if (host == null || !host.Enter()) return false;
            bool used = false;
            if (!host.IsDisposed && frames <= host.Config.MaxFrames)
            {
                host.EnsureRendered(blockStart, frames);
                used = host.SourceUsed(source);
            }
            host.Exit();
            return used;
        }

        /// <summary>Audio thread: mix[0..frames) += this block's slice of the source.</summary>
        public static void AddSlice(AudioGraphHost host, int source, float[] mix, int frames)
        {
            if (host == null || !host.Enter()) return;
            if (!host.IsDisposed && (uint)source < (uint)host.Config.Sources && frames <= mix.Length)
                fixed (float* m = mix)   // pinning is free: Unity's Boehm GC (Mono and IL2CPP) does not move objects
                    DspKernels.MixRamp(m, host.SourceSlice(source), 0, frames, 1f, 0f);
            host.Exit();
        }

        /// <summary>Audio thread: the mono mix to every channel of OnAudioFilterRead's interleaved data.</summary>
        public static void WriteChannels(float[] mix, float[] data, int frames, int channels)
        {
            fixed (float* m = mix, d = data) DspKernels.MonoToInterleaved(m, d, frames, channels);
        }
    }
}
