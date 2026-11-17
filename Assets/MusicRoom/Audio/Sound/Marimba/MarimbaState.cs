// The marimba unit's unmanaged state (spec 6.2 "MarimbaState"): one allocation, 64-byte aligned, laid out by
// MarimbaStateBuilder. The main thread writes strike and damp records (and the lane tables once, before Ready); the audio
// thread owns everything else. No managed references: Burst reads it through raw pointers.
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    /// <summary>A strike record (main thread writes it before the Strike command is committed; the ring's release store
    /// publishes it): the radiated sound during the contact plus the knock, and the modal states to inject when the mallet
    /// leaves (z_lane += inj at the handoff sample Nh).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MarimbaStrikeRecord
    {
        public int Bar, Len, Nh, Pad;
        public fixed float InjR[MarimbaConst.Lanes];
        public fixed float InjI[MarimbaConst.Lanes];
        public fixed float Seg[MarimbaConst.SegMax];
    }

    /// <summary>A damp record: the bar's 8 lane poles while a hand or a held mallet rests on it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MarimbaDampRecord
    {
        public int Bar, Pad0, Pad1, Pad2;
        public fixed float Pr[MarimbaConst.Lanes];
        public fixed float Pi[MarimbaConst.Lanes];
    }

    /// <summary>A strike record being played onto its bar's source.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MarimbaPlayer
    {
        public int On, Rec, Bar, Pad;
        public long Start;
    }

    /// <summary>A sample-accurate action on one bar, kept sorted by (At, Seq).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MarimbaAction
    {
        public const int Inject = 1, Poles = 2, Undamp = 3;
        public long At;
        public int Seq, Kind, Bar, Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MarimbaState
    {
        /// <summary>Audio-thread block size the scratch holds; longer blocks are rendered in chunks of this size.</summary>
        public const int ChunkFrames = MarimbaConst.Block;

        public ModalGroup* Groups;          // 2 per bar: lanes e+ e- b2 b3 | b4 t1 t2 -
        public float* P0r, P0i;             // undamped poles, 8 per bar
        public byte* Live;                  // 8 per bar
        public byte* Src;                   // output channel per bar (0 Lo, 1 Mid, 2 Hi)
        /// <summary>Review item 13 (opt-in, off by default): the bars A#3..C#4 and A#4..C#5 crossfade at constant power between
        /// the two neighbouring sources by bar x (Src gets G1, Src2 gets G2; Src2 = 0xFF: hard-assigned).</summary>
        public byte* Src2;
        public float* G1, G2;
        public int Crossfade;
        public byte* LiveCount;             // live lanes per bar (Work)
        public byte* Active, Fading, Damped;
        public float* Energy, Level;
        public MarimbaStrikeRecord* Strikes;
        public MarimbaDampRecord* Damps;
        public MarimbaPlayer* Players;
        public MarimbaAction* Actions;
        public float4* Acc;                 // ChunkFrames
        public float4* Acc2;                // ChunkFrames: the second bar of an interleaved pair
        public float* BarOut;               // ChunkFrames
        public int ActionCount;
        /// <summary>main -> audio: 1 once the lane tables are in (strikes before that are dropped).</summary>
        public int Ready;
        /// <summary>main -> audio: the platform's UnitSlot.Share (phase B reads u->Share; until then this field, default 1).</summary>
        public float Share;
        /// <summary>The LOD budget when the unit renders outside the graph (u->Graph == null: the offline harness).</summary>
        public int Budget;
        // ---- stats (audio thread writes, main thread reads with Volatile.Read)
        public int StrikesPlayed, Injections, DroppedPlayers, DroppedActions, Capped, Retired, MaxActive, Work, ActiveBars;
        public long Blocks;
    }

    /// <summary>Lays a MarimbaState and its arrays out in one block of host memory and fills the lane tables.</summary>
    public static unsafe class MarimbaStateBuilder
    {
        /// <summary>The boundary crossfade of review item 13: over the bars between A3 and D4 (Lo / Mid) and between A4 and D5
        /// (Mid / Hi), t = (x - x0) / (x1 - x0) by bar centre, gains cos(t pi / 2) on the lower source and sin(t pi / 2) on
        /// the upper one (constant power); every other bar keeps its one source (src2 = -1).</summary>
        public static void BoundaryGains(int bar, out int src2, out float g1, out float g2)
        {
            src2 = -1; g1 = 1f; g2 = 0f;
            int lo, hi, a, b;
            if (bar >= 10 && bar <= 13) { lo = 0; hi = 1; a = 9; b = 14; }        // A#3 B3 | C4 C#4, between A3 and D4
            else if (bar >= 22 && bar <= 25) { lo = 1; hi = 2; a = 21; b = 26; }  // A#4 B4 | C5 C#5, between A4 and D5
            else return;
            double t = (MarimbaTables.X[bar] - MarimbaTables.X[a]) / (MarimbaTables.X[b] - MarimbaTables.X[a]);
            double gl = System.Math.Cos(t * System.Math.PI / 2), gh = System.Math.Sin(t * System.Math.PI / 2);
            // channel 1 (Src) is the bar's own source; channel 2 is the neighbour across the boundary
            if (MarimbaTables.Source[bar] == lo) { g1 = (float)gl; g2 = (float)gh; src2 = hi; }
            else { g1 = (float)gh; g2 = (float)gl; src2 = lo; }
        }

        const int Align = 64;
        static long Up(long n) => (n + Align - 1) & ~(long)(Align - 1);

        static byte* Take(ref byte* p, long bytes) { byte* q = p; p += Up(bytes); return q; }

        public static long Bytes()
        {
            int nb = MarimbaConst.Bars, nl = nb * MarimbaConst.Lanes;
            long n = Up(sizeof(MarimbaState));
            n += Up((long)sizeof(ModalGroup) * nb * MarimbaConst.GroupsPerBar);
            n += 2 * Up(4L * nl) + Up(nl);
            n += 6 * Up(nb) + 4 * Up(4L * nb);
            n += Up((long)sizeof(MarimbaStrikeRecord) * MarimbaConst.StrikeSlots);
            n += Up((long)sizeof(MarimbaDampRecord) * MarimbaConst.DampSlots);
            n += Up((long)sizeof(MarimbaPlayer) * MarimbaConst.Players);
            n += Up((long)sizeof(MarimbaAction) * MarimbaConst.Actions);
            n += 2 * Up(16L * MarimbaState.ChunkFrames) + Up(4L * MarimbaState.ChunkFrames);
            return n;
        }

        /// <summary>mem: Bytes() zeroed bytes, 64-byte aligned. Returns the state with the undamped lane tables installed.</summary>
        public static MarimbaState* Build(byte* mem)
        {
            int nb = MarimbaConst.Bars, nl = nb * MarimbaConst.Lanes;
            byte* p = mem;
            var s = (MarimbaState*)Take(ref p, sizeof(MarimbaState));
            s->Groups = (ModalGroup*)Take(ref p, (long)sizeof(ModalGroup) * nb * MarimbaConst.GroupsPerBar);
            s->P0r = (float*)Take(ref p, 4L * nl);
            s->P0i = (float*)Take(ref p, 4L * nl);
            s->Live = Take(ref p, nl);
            s->Src = Take(ref p, nb);
            s->Src2 = Take(ref p, nb);
            s->G1 = (float*)Take(ref p, 4L * nb);
            s->G2 = (float*)Take(ref p, 4L * nb);
            s->LiveCount = Take(ref p, nb);
            s->Active = Take(ref p, nb);
            s->Fading = Take(ref p, nb);
            s->Damped = Take(ref p, nb);
            s->Energy = (float*)Take(ref p, 4L * nb);
            s->Level = (float*)Take(ref p, 4L * nb);
            s->Strikes = (MarimbaStrikeRecord*)Take(ref p, (long)sizeof(MarimbaStrikeRecord) * MarimbaConst.StrikeSlots);
            s->Damps = (MarimbaDampRecord*)Take(ref p, (long)sizeof(MarimbaDampRecord) * MarimbaConst.DampSlots);
            s->Players = (MarimbaPlayer*)Take(ref p, (long)sizeof(MarimbaPlayer) * MarimbaConst.Players);
            s->Actions = (MarimbaAction*)Take(ref p, (long)sizeof(MarimbaAction) * MarimbaConst.Actions);
            s->Acc = (float4*)Take(ref p, 16L * MarimbaState.ChunkFrames);
            s->Acc2 = (float4*)Take(ref p, 16L * MarimbaState.ChunkFrames);
            s->BarOut = (float*)Take(ref p, 4L * MarimbaState.ChunkFrames);
            s->Share = 1f;
            s->Budget = 1200;
            // the lane tables (MarimbaTables: the lab's float32 poles), output gain 1 on the live lanes: the modes' output
            // weights are folded into the injected states (z = j c V G), so y = Im(z) per lane
            for (int i = 0; i < nb; i++)
            {
                int live = 0;
                for (int l = 0; l < MarimbaConst.Lanes; l++)
                {
                    int k = i * MarimbaConst.Lanes + l;
                    s->P0r[k] = MarimbaTables.P0r[k];
                    s->P0i[k] = MarimbaTables.P0i[k];
                    s->Live[k] = (byte)(MarimbaTables.LaneLive[k] ? 1 : 0);
                    live += s->Live[k];
                    ModalGroup* g = s->Groups + i * 2 + (l >> 2);
                    int c = l & 3;
                    ((float*)&g->Pr)[c] = s->P0r[k];
                    ((float*)&g->Pi)[c] = s->P0i[k];
                    ((float*)&g->Go)[c] = s->Live[k] != 0 ? 1f : 0f;
                }
                s->LiveCount[i] = (byte)live;
                s->Src[i] = (byte)MarimbaTables.Source[i];
                BoundaryGains(i, out int src2, out float g1, out float g2);
                s->Src2[i] = (byte)(src2 < 0 ? 0xFF : src2);
                s->G1[i] = g1; s->G2[i] = g2;
            }
            System.Threading.Volatile.Write(ref s->Ready, 1);
            return s;
        }
    }
}
