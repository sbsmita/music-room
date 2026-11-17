// The hand drums' unmanaged state (Docs/12-new-instruments/handdrums.md 2.12) and the unit's public sound API.
// One allocation per drum holds its tables (loaded from Resources/MusicRoomHandDrums.bytes, exported from the Node
// reference at the precision the reference holds them) and its live state; HandDrumsState holds both drums and the
// budget. Drum indices are 0 = conga, 1 = tumba everywhere; only the render maps drum d to output channel 1 - d (the
// platform's source order HandDrumLo, HandDrumHi). Layouts are blittable: the render is Burst code.
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    /// <summary>The hand part a contact patch is made of (handdrums.md 2.3, PARTS order).</summary>
    public enum HandPart : byte { Palm = 0, Heel = 1, Fingers = 2, Tips = 3, Base = 4, Pads = 5 }

    /// <summary>A Patch command's points: 1-3 explicit points, or a line of 4 points from p0 to p1.</summary>
    public enum PatchShape : byte { Points = 0, Line = 1 }

    /// <summary>The canonical strokes (handdrums.md 2.10). Mute and SlapClosed add their rest after 12 ms for 0.25 s.</summary>
    public enum HandStroke : byte { Open = 0, Mute = 1, Slap = 2, SlapClosed = 3, Bass = 4, Heel = 5, Toe = 6, Touch = 7 }

    /// <summary>The canonical rests (handdrums.md 2.10 RESTS, plus the toe's finger pads of the renders' heel-toe).</summary>
    public enum HandRest : byte { Mute = 0, SlapClosed = 1, Heel = 2, Palm = 3, Toe = 4 }

    public struct RestPoint { public HandPart Part; public float Rho, Theta; }
    public struct PatchPoint { public float Rho, Theta; }

    /// <summary>What the instrument module calls (through audio.ActiveUnit(UnitId.HandDrums) as IHandDrumSound in phase B;
    /// in phase A through HandDrumsSound over any command sink). Times follow GraphCommand.When: 0 = as soon as possible,
    /// &gt; 0 absolute audio seconds, &lt; 0 = that many seconds after the consuming block. Nothing here allocates.</summary>
    public interface IHandDrumSound
    {
        /// <summary>One measured contact patch. u: this patch's stroke value 0..1 (the stroke's u x the patch's u scale); the
        /// unit turns it into the part's impact speed. Points: 1-3 points p0..p2; Line: 4 points from p0 to p1.
        /// delay: this patch's delay after the stroke (the bass's pads 4 ms, the slap's tips 0.8 ms).</summary>
        void Patch(int drum, int hand, HandPart part, float u, PatchShape shape, PatchPoint p0, PatchPoint p1, PatchPoint p2, int count, double when, float delay = 0f);
        /// <summary>A canonical stroke (playback, lessons, demos): the geometry of handdrums.md 2.10 turned by thetaOffset.</summary>
        /// rhoOffset moves every canonical point outward (+) or inward (-) (playback of a recorded stroke's pos).
        void Stroke(int drum, int hand, HandStroke kind, double u, double thetaOffset, double when, float rhoOffset = 0f);
        /// <summary>Up to 3 resting points of one hand on one head (they damp what rings).</summary>
        void Rest(int drum, int hand, RestPoint p0, RestPoint p1, RestPoint p2, int count, double when);
        /// <summary>A canonical rest (playback of handDrumMute): RESTS[kind] turned by thetaOffset.</summary>
        void RestCanonical(int drum, int hand, HandRest kind, double thetaOffset, double when);
        /// <summary>The hand leaves that head: its damping is removed.</summary>
        void Lift(int drum, int hand, double when);
        /// <summary>Audio -> main: the drum's output rms over the last block (the head's optional ringing visual).</summary>
        float Level(int drum);
    }

    /// <summary>Constants of the reference engine (handdrums.reference.mjs), checked against the exported table.</summary>
    public static class HandDrumsConst
    {
        public const int Sr = 48000, Sub = 32, Slots = 4, MaxPts = 4, ShapeN = 65, MaxM = 24, MaxFrames = 256, Parts = 6;
        public const double GlideEps = 2e-6, FlightS = 0.003, ContactMaxS = 0.04, OutputTrimDb = 7.86, Y2Rel = 0.03, PopK = 52;
        public const int PopMin = 18, PopMax = 28;
        public const float Cull2 = 1e-11f, Flush = 1e-20f;
        public const int CapMin = 12;
        public const double CoreHz = 700, LouderHyst = 2, FadeDb = -60, BassPadsU = 0.6;
        public const double UV0Palm = 0.30, UV0Fingers = 0.25, UV1 = 2.0, UExp = 0.7;
        public const int KindB = 0, KindT = 1, KindA2 = 2, KindMem = 3, KindDense = 4, KindShell = 5, KindCav = 6;
        /// <summary>The unit's own sub-ops (GraphCommand.Op).</summary>
        public const byte OpPatch = 1, OpRest = 2, OpLift = 3, OpReset = 4, OpStrokePatch = 5, OpRestCanon = 6;
        // PARTS (handdrums.md 2.3): m (kg), k (N/m^alpha), alpha, lambda (s/m), b (m), R (N s/m), cup, Vmin, Vmax (m/s), cap
        public static readonly double[] PartM = { 0.18, 0.15, 0.030, 0.005, 0.10, 0.03 };
        public static readonly double[] PartK = { 6.0e8, 1.0e9, 1.0e9, 2.0e7, 1.0e9, 5.0e8 };
        public static readonly double[] PartAlpha = { 2.5, 2.5, 2.5, 1.5, 2.5, 2.5 };
        public static readonly double[] PartLam = { 0.8, 0.8, 1.0, 1.0, 0.8, 0.8 };
        public static readonly double[] PartB = { 0.035, 0.022, 0.012, 0.007, 0.020, 0.012 };
        public static readonly double[] PartR = { 55, 40, 18, 8, 30, 14 };
        public static readonly double[] PartCup = { 1.3, 0.2, 1.2, 1.8, 0.4, 0.3 };
        public static readonly double[] PartVmin = { 0.15, 0.15, 0.15, 0.40, 0.20, 0.15 };
        public static readonly double[] PartVmax = { 3.6, 1.6, 4.5, 6.0, 3.0, 1.6 };
        public static readonly double[] PartCap = { 1.3, 1.3, 0, 0, 0, 0 };
    }

    /// <summary>Names and the channel map (managed code only: the render uses channel 1 - drum).</summary>
    public static class HandDrumsNames
    {
        /// <summary>drum d plays on channel ChannelOfDrum[d]: drum 0 (conga) -> 1 (HandDrumHi), drum 1 (tumba) -> 0 (HandDrumLo).</summary>
        public static readonly int[] ChannelOfDrum = { 1, 0 };
        /// <summary>The platform's source order of the unit's outputs (Register): channel 0, channel 1.</summary>
        public static readonly string[] Sources = { "HandDrumLo", "HandDrumHi" };
        public static readonly string[] Drums = { "conga", "tumba" };
        public static readonly string[] Parts = { "palm", "heel", "fingers", "tips", "base", "pads" };
    }

    /// <summary>One contact patch slot (handdrums.md 2.12 Patch[4]).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HandDrumsPatch
    {
        public int On, Started, Part, Hand, An, PopLen, PopLeft, Flight, Age, TFirst, TLast, Pad0;
        public double M, K, Alpha, Lam, Kh, Zh, Iy2, X, V, Y, Yd, F, Fp, Fmax, Cup, V0, PopA;
        /// <summary>Per-mode input gains (nPad), the force differences and the air pop of the block (MaxFrames each).</summary>
        public float* Gi, DF, Pop;
    }

    /// <summary>One drum: its tables (read-only after the build) and its state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HandDrumsDrum
    {
        // ---- identity (HandDrumsTables)
        public int N, NPad, G, GCav, GlideCount, ShedCount;
        public double A, T, Sigma, Kappa, GlideMax, OutGain;
        public ModalGroup* Bank;                                     // G MB4 groups: zr, zi, pr, pi, go (gi unused)
        public float* Shape, AvgTab;                                 // nPad x 65 radial shapes; 6 x nPad Hertz averages
        public double* R0, W0, Cr0, Ci0, Mk, Om, Kap, EW, CPh, SPh;  // nPad each (the reference's doubles)
        public int* Kind, Mm, Orient, GlideList, ShedOrder;
        public byte* GlideOn, GClass;
        // ---- state
        public byte* Act, Allowed, Fading;                           // G each
        public int* ActList;                                         // G: this segment's active groups
        public double* DMul0, DMul1, DAl;                            // nPad: rest damping per hand, rest scratch
        public float4* Acc;                                          // MaxFrames lane accumulator
        public HandDrumsPatch P0, P1, P2, P3;
        public double S, CavMul, Energy, FadeMul;
        public int RestOn0, RestOn1, PolesDirty, GlideDirty, AnyFading, Idle, Work, Pad1;
        public uint Rng;
        public int Pad2;
        // ---- one event's scratch (no allocation per event)
        public fixed double Pr[HandDrumsConst.MaxPts];
        public fixed double Pt[HandDrumsConst.MaxPts];
        public fixed double Cm[HandDrumsConst.MaxPts * (HandDrumsConst.MaxM + 1)];
        public fixed double Sm[HandDrumsConst.MaxPts * (HandDrumsConst.MaxM + 1)];
    }

    /// <summary>The unit's state (UnitSlot.State).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HandDrumsState
    {
        public HandDrumsDrum D0, D1;
        public int SampleRate, GTot, Cap, Louder, MaskKey, Ready;
        /// <summary>main -> audio: this unit's share of the LOD budget (platform 10.4; phase B reads UnitSlot.Share).</summary>
        public float Share;
        /// <summary>audio -> main (Volatile): group sub-blocks (x 32 samples) of the last block (-> UnitSlot.Work), stats.</summary>
        public int Work;
        public long WorkTotal;
        public int Blocks, Strokes, PatchesStolen, MaxActivePatches, ShedGroups, Resets;
        /// <summary>audio -> main (Volatile): each drum's output rms over the last block.</summary>
        public float Level0, Level1;
    }
}
