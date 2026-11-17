// The Burst audio graph: ONE Burst call renders every instrument for one block into per-source mono buffers
// (spec 10-upgrade/burst.md section 3). Mirrors the Node reference graph (spec section 3) operation for operation for the modal
// banks (graph-level test vectors are exact in managed code), then the engine additions in this order: the one-shot
// pool (section 5.4: quietest-first stealing into a 128-sample ghost fade), the sound units (ENGINE-API.md: each unit's
// Burst render into its own channels, mixed into the sources with its fade ramp), and the LOD budget over the banks.
//
// Banks: a bank is the set of modal groups (4 modes each) owned by ONE physical object (a piano key's strings, a drum
// head, a cymbal, a soundboard). A strike adds energy into the same bank, so keyed instruments never steal voices.
// CPU is capped by LOD: when more groups are active than Budget, the least audible ones (energy x perceptual weight,
// 2 dB histogram) fade out over one block and retire. Events (strikes, pulses, dampers) are sample-accurate: a bank's
// block is split at its events. Nothing here allocates; all memory is the host's unmanaged arena.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Pulse
    {
        public int Left;
        public float Amp, C, S, Zr, Zi;   // half-sine by rotation: x = Amp Im(z), z *= e^{j pi / W}
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModalBank
    {
        public int First, Count, Modes, Source;
        public int StrikeOffset;          // into GraphState.Strike: 4 floats per mode (a, b, e, phase)
        public int ListCount;             // active groups, ascending, in GraphState.List[First ..]
        public int PulseCount;            // 0..2 (P0 is the older)
        public int Silence;               // fade every active group over the next block
        public long LastStrike;           // sample of the last strike/pulse (LOD protection)
        public Pulse P0, P1;
    }

    /// <summary>A due graph event (32 bytes). For OneShot, B carries the voice id's bits (math.asfloat(Id)).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GraphEvent
    {
        public long At;
        public int Seq, Target;
        public GraphOp Op;
        public float A, B;
        public int N;
    }

    /// <summary>A one-shot recipe (engine addition, section 5.4): up to four noise bands, NZ4 -> BQ4 -> EXP4 -> lane mix.
    /// Built on the main thread (AudioGraphHost.AddOneShotPreset); a OneShot command starts a pooled voice from it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OneShotPreset
    {
        public Biquad4 Filter;      // coefficients per lane (DspSetup.SetBiquad); the voice starts from rest
        public float4 EnvGain;      // per-lane starting gain (the envelope's peak)
        public float4 EnvMul;       // per-lane per-sample decay (DspSetup.DecayMultiplier(tau, sr))
        public float4 Mix;          // per-lane output weight into the mono source
        public int Length;          // samples after which the voice frees itself (0 = when it is quiet)
        public float LevelDb;       // output level estimate at gain 1 and envelope gain 1 (quietest-first stealing)
        public uint Seed;           // noise seed (each voice xors its start sequence number)
        public int Pad;
    }

    /// <summary>A playing one-shot (the payload VoiceAllocator's slots move into a ghost when stolen).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OneShotVoice
    {
        public Noise4 Noise;
        public Biquad4 Filter;
        public Exp4 Env;
        public float4 Mix;
        public float Gain, LevelDb;
        public int Source, Left, Delay, Preset;
    }

    public unsafe struct GraphState
    {
        public int SampleRate, MaxFrames, SourceCount;
        public float* Out;                  // SourceCount x MaxFrames mono
        public float4* Acc, AccFade;        // MaxFrames each
        public float* Exc;                  // MaxFrames
        public ModalGroup* Groups;          // GroupCapacity
        public float4* P0r, P0i;            // undamped poles
        public byte* Active, Fading;
        public int* List;                   // per bank at [First, First + ListCount)
        public float* Energy, Weight;
        public float* Strike;               // 4 per mode
        public ModalBank* Banks;
        public int BankCount, BankCapacity, GroupCount, GroupCapacity, StrikeUsed, StrikeCapacity;
        public int Budget;
        public GraphEvent* Pending, Due;
        public int PendingCount, PendingCapacity;
        public int* LodBucket, LodGroup, LodBank;
        public int* ScratchA, ScratchB;     // GroupCapacity each (segment lists)
        public byte* LodProt;
        public CommandRing Ring;
        public long LastBlock;
        public int Seq;
        // stats: written by the audio thread, read by the main thread (Volatile.Read)
        public int Culled, LodDropped, ActiveGroups, MaxActiveGroups, DroppedEvents;
        // ---- engine additions ----
        public OneShotSlot* Slots;          // OneShotLive + OneShotGhosts
        public OneShotVoice* Voices;        // parallel to Slots
        public OneShotPreset* Presets;
        public float4* Scratch4;            // MaxFrames (one-shot lanes)
        public int OneShotLive, OneShotGhosts, PresetCount, PresetCapacity;
        public UnitSlot* Units;             // UnitId.Count
        public int UnitCount;
        public byte* SourceUsed;            // per source: something was mixed into Out[source] this block
        public int OneShotsStarted, OneShotSteals, ActiveOneShots, LateDropped;
        public long Blocks;
    }

    [BurstCompile]
    public static unsafe class AudioGraph
    {
        public const int ProtectSamples = 1440;          // 30 ms at 48 kHz: just-struck banks are never LOD victims
        public const int LodBuckets = 65, LodDbStep = 2, LodFloorDb = -130;
        /// <summary>A Strike or Pulse due more than this many samples before the block (100 ms: the mixer was paused,
        /// e.g. headset off) is dropped instead of firing late in a burst; state ops (damp, budget) still apply.</summary>
        public const int LateDropSamples = 4800;
        /// <summary>A one-shot voice whose level estimate falls below this retires (the modal cull level, section 5.3).</summary>
        public const float OneShotCullDb = -110f;
        /// <summary>A unit registered with idle skip is skipped after this many silent rendered blocks (43 ms at 256 frames).</summary>
        public const int IdleSkipBlocks = 8;

        // ---------------------------------------------------------------- main thread (managed, before playback)

        /// <summary>Main thread, before the bank is used: add a bank of modes (padded to whole groups). Returns the bank
        /// id or -1 when the arena is full. Coefficients in double, rounded once (same as lab/kernels.mjs mbSetMode).</summary>
        public static int AddBank(GraphState* g, int source, int modeCount, float* hz, float* tau, float* go, float* gi,
            float* strikeA, float* strikeB, float* velExp, float* phase, float* weight)
        {
            int groups = (modeCount + 3) >> 2;
            if (g->BankCount >= g->BankCapacity || g->GroupCount + groups > g->GroupCapacity || g->StrikeUsed + 4 * modeCount > g->StrikeCapacity) return -1;
            int first = g->GroupCount;
            for (int i = 0; i < groups; i++) { g->Groups[first + i] = default; g->Active[first + i] = 0; g->Fading[first + i] = 0; g->Energy[first + i] = 0; }
            double sr = g->SampleRate;
            for (int m = 0; m < modeCount; m++)
            {
                int gi4 = first + (m >> 2), l = m & 3;
                double r = tau[m] > 0 ? Math.Exp(-1.0 / (tau[m] * sr)) : 0.0;
                double w = 2.0 * Math.PI * hz[m] / sr;
                ref ModalGroup mg = ref g->Groups[gi4];
                mg.Pr[l] = (float)(r * Math.Cos(w));
                mg.Pi[l] = (float)(r * Math.Sin(w));
                mg.Go[l] = go[m];
                mg.Gi[l] = gi != null ? gi[m] : 0f;
                float* st = g->Strike + g->StrikeUsed + 4 * m;
                st[0] = strikeA[m]; st[1] = strikeB != null ? strikeB[m] : strikeA[m]; st[2] = velExp != null ? velExp[m] : 1f; st[3] = phase != null ? phase[m] : 0f;
            }
            for (int i = 0; i < groups; i++)
            {
                g->P0r[first + i] = g->Groups[first + i].Pr; g->P0i[first + i] = g->Groups[first + i].Pi;
                float wmax = 0f;
                for (int l = 0; l < 4 && 4 * i + l < modeCount; l++) wmax = math.max(wmax, weight != null ? weight[4 * i + l] : 1f);
                g->Weight[first + i] = wmax;
            }
            int id = g->BankCount;
            g->Banks[id] = new ModalBank { First = first, Count = groups, Modes = modeCount, Source = source, StrikeOffset = g->StrikeUsed, LastStrike = long.MinValue / 2 };
            g->GroupCount += groups;
            g->StrikeUsed += 4 * modeCount;
            Volatile.Write(ref g->BankCount, id + 1);   // publish last: the audio thread may be rendering
            return id;
        }

        /// <summary>Main thread: add a one-shot preset; returns its id or -1 when the table is full.</summary>
        public static int AddOneShotPreset(GraphState* g, OneShotPreset* p)
        {
            int id = g->PresetCount;
            if (id >= g->PresetCapacity) return -1;
            g->Presets[id] = *p;
            Volatile.Write(ref g->PresetCount, id + 1);
            return id;
        }

        // ---------------------------------------------------------------- audio thread (Burst)

        /// <summary>Renders block [blockStart, blockStart + frames) for every bank, one-shot and unit into g->Out. Idempotent
        /// per block: the first SynthSource callback of a block renders, the others only copy (callbacks are serial, spec 04 E4).</summary>
        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance, CompileSynchronously = true)]
        public static void RenderBlock(GraphState* g, long blockStart, int frames)
        {
            if (blockStart == g->LastBlock || frames <= 0 || frames > g->MaxFrames) return;
            g->LastBlock = blockStart;
            g->Blocks++;
            for (int s = 0; s < g->SourceCount; s++) { DspKernels.Clear(g->Out + s * g->MaxFrames, frames); g->SourceUsed[s] = 0; }
            long end = blockStart + frames;
            int dueCount = CollectDue(g, blockStart, end);
            // state ops first (SetBudget as the spec; the engine's unit fades, one-shot releases and starts)
            for (int e = 0; e < dueCount; e++)
            {
                GraphEvent* ev = g->Due + e;
                switch (ev->Op)
                {
                    case GraphOp.SetBudget: g->Budget = ev->N; break;
                    case GraphOp.UnitFade: ApplyUnitFade(g, ev); break;
                    case GraphOp.Release: ReleaseOneShot(g, ev); break;
                    case GraphOp.OneShot: StartOneShot(g, ev, blockStart); break;
                }
            }

            int bankCount = Volatile.Read(ref g->BankCount);
            for (int b = 0; b < bankCount; b++)
            {
                ModalBank* bank = g->Banks + b;
                bool anyFade = false;
                for (int i = 0; i < bank->Count; i++) if (g->Fading[bank->First + i] != 0) { anyFade = true; break; }
                bool hasEvents = false;
                for (int e = 0; e < dueCount; e++) if (g->Due[e].Target == b && IsBankOp(g->Due[e].Op)) { hasEvents = true; break; }
                if (bank->ListCount == 0 && !hasEvents && !anyFade && bank->PulseCount == 0) continue;

                int cursor = 0;
                for (int e = 0; e < dueCount; e++)
                {
                    GraphEvent* ev = g->Due + e;
                    if (ev->Target != b || !IsBankOp(ev->Op)) continue;
                    long o = ev->At - blockStart;
                    int off = o < 0 ? 0 : o > frames ? frames : (int)o;
                    Segment(g, bank, cursor, off);
                    Apply(g, bank, ev);
                    cursor = off;
                }
                Segment(g, bank, cursor, frames);

                float* output = g->Out + bank->Source * g->MaxFrames;
                g->SourceUsed[bank->Source] = 1;
                DspKernels.AccResolve(g->Acc, output, 0, frames);
                if (anyFade)
                {
                    float dg = -1f / frames;
                    DspKernels.AccResolveRamp(g->AccFade, output, 0, frames, 1f, dg);
                    for (int i = 0; i < bank->Count; i++)
                        if (g->Fading[bank->First + i] != 0) { g->Fading[bank->First + i] = 0; g->Active[bank->First + i] = 0; ZeroState(g->Groups + bank->First + i); }
                }
                for (int i = 0; i < bank->Count; i++)
                {
                    int gi = bank->First + i;
                    if (g->Active[gi] == 0) continue;
                    DspKernels.ModalEndBlock(g->Groups + gi, 1, g->Energy + gi);
                    if (g->Energy[gi] < DspKernels.CullEnergy && bank->PulseCount == 0) { g->Active[gi] = 0; ZeroState(g->Groups + gi); g->Culled++; }
                }
                Rebuild(g, bank);
                if (bank->Silence != 0)
                {
                    for (int i = 0; i < bank->ListCount; i++) g->Fading[bank->First + g->List[bank->First + i]] = 1;
                    bank->Silence = 0;
                }
            }
            RenderOneShots(g, frames);
            RenderUnits(g, blockStart, frames);
            Lod(g, end);
        }

        static bool IsBankOp(GraphOp op) => op >= GraphOp.Strike && op <= GraphOp.Silence;

        static int CollectDue(GraphState* g, long blockStart, long end)
        {
            // 1. new commands -> pending (When -> absolute sample, the SynthSource.Drain rules)
            double sr = g->SampleRate;
            while (g->Ring.TryDequeue(out GraphCommand c))
            {
                if (g->PendingCount >= g->PendingCapacity) { g->DroppedEvents++; continue; }
                long raw = c.When > 0 ? (long)math.round(c.When * sr) : blockStart + (long)math.round(-c.When * sr);
                bool onset = c.Op == GraphOp.Strike || c.Op == GraphOp.Pulse || c.Op == GraphOp.OneShot;
                if (raw < blockStart - LateDropSamples && onset) { g->DroppedEvents++; g->LateDropped++; continue; }
                long at = math.max(blockStart, raw);
                float b = c.Op == GraphOp.OneShot ? math.asfloat(c.Id) : c.B;
                g->Pending[g->PendingCount++] = new GraphEvent { At = at, Seq = g->Seq++, Target = c.Target, Op = c.Op, A = c.A, B = b, N = c.N };
            }
            // 2. due = pending with At < end (stable), pending keeps the rest (stable)
            int due = 0, keep = 0;
            for (int i = 0; i < g->PendingCount; i++)
            {
                GraphEvent e = g->Pending[i];
                if (e.At < end) g->Due[due++] = e; else g->Pending[keep++] = e;
            }
            g->PendingCount = keep;
            // 3. insertion sort by (At, Seq): few events per block
            for (int i = 1; i < due; i++)
            {
                GraphEvent x = g->Due[i];
                int j = i - 1;
                while (j >= 0 && (g->Due[j].At > x.At || (g->Due[j].At == x.At && g->Due[j].Seq > x.Seq))) { g->Due[j + 1] = g->Due[j]; j--; }
                g->Due[j + 1] = x;
            }
            return due;
        }

        static void Segment(GraphState* g, ModalBank* bank, int from, int to)
        {
            if (to <= from) return;
            bool driven = bank->PulseCount > 0;
            if (driven)
            {
                for (int n = from; n < to; n++) g->Exc[n] = 0f;
                RunPulse(&bank->P0, g->Exc, from, to);
                if (bank->PulseCount > 1) RunPulse(&bank->P1, g->Exc, from, to);
            }
            // split the active list into normal and fading groups (both ascending): canonical accumulation order
            int* list = g->List + bank->First;
            int* normal = g->ScratchA;
            int* fading = g->ScratchB;
            int nn = 0, nf = 0;
            for (int i = 0; i < bank->ListCount; i++)
            {
                int li = list[i];
                if (g->Fading[bank->First + li] != 0) fading[nf++] = li; else normal[nn++] = li;
            }
            ModalGroup* bankBase = g->Groups + bank->First;
            if (driven)
            {
                DspKernels.ModalDrivenList(bankBase, normal, nn, g->Exc, g->Acc, from, to);
                DspKernels.ModalDrivenList(bankBase, fading, nf, g->Exc, g->AccFade, from, to);
                // drop finished pulses, keep order (P0 is the older)
                if (bank->PulseCount == 2 && bank->P1.Left <= 0) bank->PulseCount = 1;
                if (bank->PulseCount >= 1 && bank->P0.Left <= 0)
                {
                    if (bank->PulseCount == 2) { bank->P0 = bank->P1; bank->PulseCount = 1; }
                    else bank->PulseCount = 0;
                }
            }
            else
            {
                DspKernels.ModalFreeList(bankBase, normal, nn, g->Acc, from, to);
                DspKernels.ModalFreeList(bankBase, fading, nf, g->AccFade, from, to);
            }
        }

        static void RunPulse(Pulse* p, float* x, int from, int to)
        {
            float amp = p->Amp, c = p->C, s = p->S, ns = -p->S, zr = p->Zr, zi = p->Zi;
            int left = p->Left;
            for (int n = from; n < to && left > 0; n++, left--)
            {
                x[n] = math.mad(amp, zi, x[n]);
                float nr = math.mad(c, zr, ns * zi);
                float ni = math.mad(c, zi, s * zr);
                zr = nr; zi = ni;
            }
            p->Left = left; p->Zr = zr; p->Zi = zi;
        }

        static void Apply(GraphState* g, ModalBank* bank, GraphEvent* e)
        {
            switch (e->Op)
            {
                case GraphOp.Strike:
                {
                    double vel = e->A, pos = e->B;
                    float* st = g->Strike + bank->StrikeOffset;
                    for (int m = 0; m < bank->Modes; m++)
                    {
                        double a = st[4 * m], b = st[4 * m + 1], ex = st[4 * m + 2], ph = st[4 * m + 3];
                        double A = Math.Pow(vel, ex) * (a + (b - a) * pos);
                        if (A == 0) continue;
                        int gi = bank->First + (m >> 2), l = m & 3;
                        ref ModalGroup mg = ref g->Groups[gi];
                        mg.Zr[l] = mg.Zr[l] + (float)(A * Math.Cos(ph));
                        mg.Zi[l] = mg.Zi[l] + (float)(A * Math.Sin(ph));
                        g->Active[gi] = 1;
                    }
                    bank->LastStrike = e->At;
                    Rebuild(g, bank);
                    break;
                }
                case GraphOp.Pulse:
                {
                    int W = math.max(2, (int)math.round(e->B * g->SampleRate));
                    double th = Math.PI / W;
                    var p = new Pulse { Left = W, Amp = e->A, C = (float)Math.Cos(th), S = (float)Math.Sin(th), Zr = 1f, Zi = 0f };
                    if (bank->PulseCount >= 2) { bank->P0 = bank->P1; bank->P1 = p; }
                    else if (bank->PulseCount == 1) { bank->P1 = p; bank->PulseCount = 2; }
                    else { bank->P0 = p; bank->PulseCount = 1; }
                    for (int i = 0; i < bank->Count; i++) g->Active[bank->First + i] = 1;
                    bank->LastStrike = e->At;
                    Rebuild(g, bank);
                    break;
                }
                case GraphOp.Damp:
                case GraphOp.Undamp:
                {
                    double k = e->Op == GraphOp.Damp ? e->A : 1.0;
                    for (int i = 0; i < bank->Count; i++)
                    {
                        int gi = bank->First + i;
                        float4 pr0 = g->P0r[gi], pi0 = g->P0i[gi];
                        ref ModalGroup mg = ref g->Groups[gi];
                        for (int l = 0; l < 4; l++)
                        {
                            double r = Math.Sqrt((double)pr0[l] * pr0[l] + (double)pi0[l] * pi0[l]);
                            double f = r > 0 ? Math.Pow(r, k - 1) : 0;
                            mg.Pr[l] = (float)(pr0[l] * f);
                            mg.Pi[l] = (float)(pi0[l] * f);
                        }
                    }
                    break;
                }
                case GraphOp.Silence:
                    bank->Silence = 1;
                    break;
            }
        }

        static void ZeroState(ModalGroup* mg) { mg->Zr = float4.zero; mg->Zi = float4.zero; }

        static void Rebuild(GraphState* g, ModalBank* bank)
        {
            int* list = g->List + bank->First;
            int n = 0;
            for (int i = 0; i < bank->Count; i++) if (g->Active[bank->First + i] != 0) list[n++] = i;
            bank->ListCount = n;
        }

        static int Bucket(float a)
        {
            if (a <= 0f) return 0;
            double db = 20.0 * Math.Log10(a);
            int b = (int)Math.Floor((db - LodFloorDb) / LodDbStep);
            return b < 0 ? 0 : b >= LodBuckets ? LodBuckets - 1 : b;
        }

        static void Lod(GraphState* g, long now)
        {
            int total = 0;
            int bankCount = Volatile.Read(ref g->BankCount);
            for (int b = 0; b < bankCount; b++)
            {
                ModalBank* bank = g->Banks + b;
                for (int i = 0; i < bank->ListCount; i++)
                {
                    int gi = bank->First + g->List[bank->First + i];
                    if (g->Fading[gi] != 0) continue;
                    g->LodGroup[total] = gi;
                    g->LodBank[total] = b;
                    g->LodBucket[total] = Bucket(math.sqrt(g->Energy[gi]) * g->Weight[gi]);
                    g->LodProt[total] = (byte)(now - bank->LastStrike < ProtectSamples ? 1 : 0);
                    total++;
                }
            }
            g->ActiveGroups = total;
            if (total > g->MaxActiveGroups) g->MaxActiveGroups = total;
            if (total <= g->Budget) return;

            int* hist = stackalloc int[LodBuckets];
            for (int k = 0; k < LodBuckets; k++) hist[k] = 0;
            int prot = 0;
            for (int i = 0; i < total; i++) { if (g->LodProt[i] != 0) prot++; else hist[g->LodBucket[i]]++; }
            int room = math.max(0, g->Budget - prot);
            int thr = LodBuckets, keepAtThr = 0;
            for (int k = LodBuckets - 1; k >= 0; k--)
            {
                if (hist[k] <= room) { room -= hist[k]; thr = k; keepAtThr = hist[k]; }
                else { thr = k; keepAtThr = room; room = 0; break; }
            }
            int keptAt = 0;
            for (int i = 0; i < total; i++)
            {
                if (g->LodProt[i] != 0) continue;
                int bk = g->LodBucket[i];
                bool drop = bk < thr;
                if (bk == thr) { if (keptAt < keepAtThr) keptAt++; else drop = true; }
                if (drop) { g->Fading[g->LodGroup[i]] = 1; g->LodDropped++; }
            }
        }

        // ---------------------------------------------------------------- engine additions: one-shot pool (section 5.4)

        static void StartOneShot(GraphState* g, GraphEvent* e, long blockStart)
        {
            if (g->OneShotLive <= 0 || (uint)e->Target >= (uint)g->PresetCount || (uint)e->N >= (uint)g->SourceCount) { g->DroppedEvents++; return; }
            int slot = VoiceAllocator.Allocate(g->Slots, g->OneShotLive, g->OneShotGhosts, e->At, out int ghost);
            if (slot < 0) { g->DroppedEvents++; return; }
            if (ghost >= 0)
            {
                // The victim keeps sounding from the ghost slot for at most StealFadeSamples, from this block's start.
                g->Voices[ghost] = g->Voices[slot];
                g->Voices[ghost].Delay = 0;
                g->OneShotSteals++;
            }
            OneShotPreset* p = g->Presets + e->Target;
            OneShotVoice* v = g->Voices + slot;
            v->Noise = SplitMix32.SeedNoise(p->Seed ^ ((uint)e->Seq * 0x9E3779B9u));
            v->Filter = p->Filter;
            v->Filter.Z1 = float4.zero; v->Filter.Z2 = float4.zero;
            v->Env = new Exp4 { G = p->EnvGain, M = p->EnvMul };
            v->Mix = p->Mix;
            v->Gain = e->A;
            v->Source = e->N;
            v->Left = p->Length;
            v->Delay = (int)math.max(0, e->At - blockStart);
            v->Preset = e->Target;
            v->LevelDb = p->LevelDb;
            OneShotSlot* s = g->Slots + slot;
            *s = default;
            s->Id = math.asint(e->B);
            s->State = SlotState.Active;
            s->Start = e->At;
            s->LevelDb = LevelOf(v);
            g->OneShotsStarted++;
        }

        static void ReleaseOneShot(GraphState* g, GraphEvent* e)
        {
            if (e->Target == 0) return;
            float m = e->A > 0 ? (float)Math.Exp(-1.0 / (e->A * g->SampleRate)) : 0f;
            for (int i = 0; i < g->OneShotLive; i++)
            {
                OneShotSlot* s = g->Slots + i;
                if (s->Id != e->Target || s->State != SlotState.Active) continue;
                s->State = SlotState.Released;
                g->Voices[i].Env.M = new float4(m);
            }
        }

        static float LevelOf(OneShotVoice* v)
        {
            float amp = math.cmax(math.abs(v->Env.G * v->Mix)) * math.abs(v->Gain);
            return amp > 1e-12f ? 20f * (float)Math.Log10(amp) + v->LevelDb : -240f;
        }

        static void RenderOneShots(GraphState* g, int frames)
        {
            int total = g->OneShotLive + g->OneShotGhosts, active = 0;
            for (int i = 0; i < total; i++)
            {
                OneShotSlot* s = g->Slots + i;
                if (s->State == SlotState.Free) continue;
                OneShotVoice* v = g->Voices + i;
                int from = math.min(v->Delay, frames);
                v->Delay = 0;
                int to = frames;
                float g0 = v->Gain, dg = 0f;
                bool ghost = s->State == SlotState.Ghost;
                if (ghost)
                {
                    VoiceAllocator.GhostRamp(s, frames - from, out float r0, out float rd, out int samples);
                    to = from + samples;
                    g0 = v->Gain * r0;
                    dg = v->Gain * rd;
                }
                if (v->Left > 0 && to - from > v->Left) to = from + v->Left;
                if (to > from && (uint)v->Source < (uint)g->SourceCount)
                {
                    float4* io = g->Scratch4;
                    DspKernels.Noise4Process(&v->Noise, io, from, to);
                    DspKernels.Biquad4Process(&v->Filter, io, from, to);
                    DspKernels.Exp4Process(&v->Env, io, from, to);
                    DspKernels.Lane4Mix(io, &v->Mix, g->Out + v->Source * g->MaxFrames, from, to, g0, dg);
                    g->SourceUsed[v->Source] = 1;
                }
                if (ghost) { if (s->State == SlotState.Free) *v = default; continue; }
                active++;
                if (v->Left > 0) v->Left = math.max(0, v->Left - (to - from));
                s->LevelDb = LevelOf(v);
                if ((g->Presets[v->Preset].Length > 0 && v->Left <= 0) || s->LevelDb < OneShotCullDb)
                {
                    *s = default;
                    *v = default;
                    active--;
                }
            }
            g->ActiveOneShots = active;
        }

        // ---------------------------------------------------------------- engine additions: sound units (ENGINE-API.md)

        static void ApplyUnitFade(GraphState* g, GraphEvent* e)
        {
            if ((uint)e->Target >= (uint)g->UnitCount) return;
            UnitSlot* u = g->Units + e->Target;
            float target = math.saturate(e->A);
            int n = math.max(1, e->N);
            if (target > 0f)
            {
                // Resume: a suspended unit was reset (UnitFlags.Reset), nothing of it rings, so it comes back at once;
                // a unit still fading out ramps back up from where it is.
                if (u->Suspended != 0) { u->Suspended = 0; u->Gain = target; u->Target = target; u->Step = 0f; }
                else { u->Target = target; u->Step = (target - u->Gain) / n; }
            }
            else
            {
                u->Target = 0f;
                u->Step = -u->Gain / n;
                if (u->Gain <= 0f) { u->Gain = 0f; u->Step = 0f; u->Suspended = 1; u->Flags |= UnitFlags.Reset; }
            }
        }

        static void CollectUnit(GraphState* g, UnitSlot* u, long blockStart, long end)
        {
            double sr = g->SampleRate;
            while (u->Ring.TryDequeue(out GraphCommand c))
            {
                if (u->PendingCount >= u->PendingCapacity) { u->DroppedEvents++; continue; }
                long raw = c.When > 0 ? (long)math.round(c.When * sr) : blockStart + (long)math.round(-c.When * sr);
                if ((c.Flags & CommandFlags.LateDrop) != 0 && raw < blockStart - LateDropSamples) { u->LateDropped++; continue; }
                u->Pending[u->PendingCount++] = new UnitEvent { At = math.max(blockStart, raw), Seq = u->Seq++, C = c };
            }
            int due = 0, keep = 0;
            for (int i = 0; i < u->PendingCount; i++)
            {
                UnitEvent e = u->Pending[i];
                if (e.At < end) u->Due[due++] = e; else u->Pending[keep++] = e;
            }
            u->PendingCount = keep;
            for (int i = 1; i < due; i++)
            {
                UnitEvent x = u->Due[i];
                int j = i - 1;
                while (j >= 0 && (u->Due[j].At > x.At || (u->Due[j].At == x.At && u->Due[j].Seq > x.Seq))) { u->Due[j + 1] = u->Due[j]; j--; }
                u->Due[j + 1] = x;
            }
            for (int i = 0; i < due; i++) u->Due[i].Offset = (int)(u->Due[i].At - blockStart);
            u->DueCount = due;
        }

        static void RenderUnits(GraphState* g, long blockStart, int frames)
        {
            long end = blockStart + frames;
            for (int k = 0; k < g->UnitCount; k++)
            {
                UnitSlot* u = g->Units + k;
                if (Volatile.Read(ref u->Registered) == 0) continue;
                CollectUnit(g, u, blockStart, end);
                u->GainStart = u->Gain;
                if (u->Suspended != 0) { u->DueCount = 0; u->GainEnd = 0f; continue; }
                // Idle skip (platform.md 10.2): nothing due, nothing pending, no fade running and no output for IdleSkipBlocks
                // rendered blocks: the unit is at rest, skip its Render (its commands are still collected above, so the first
                // event after a skip renders on its exact sample). Its next Render gets UnitFlags.Resumed.
                if (u->IdleSkip != 0 && u->DueCount == 0 && u->PendingCount == 0 && u->Step == 0f && u->QuietBlocks >= IdleSkipBlocks)
                {
                    u->Skipping = 1;
                    u->IdleBlocks++;
                    u->Work = 0;
                    u->ChannelsUsed = 0;
                    u->GainEnd = u->Gain;
                    continue;
                }
                if (u->Skipping != 0) { u->Skipping = 0; u->Flags |= UnitFlags.Resumed; }
                u->BlockStart = blockStart;
                u->Frames = frames;
                u->Work = 0;
                for (int c = 0; c < u->Channels; c++) DspKernels.Clear(u->Channel(c), frames);
                UnitHooks.Render(u);
                u->Flags = 0;
                u->Blocks++;
                float gEnd = Advance(u->Gain, u->Step, u->Target, frames);
                int used = 0;
                for (int c = 0; c < u->Channels; c++)
                {
                    float* src = u->Channel(c);
                    int s = u->Source[c];
                    if ((uint)s >= (uint)g->SourceCount || DspKernels.PeakAbs(src, frames) == 0f) continue;
                    used |= 1 << c;
                    DspKernels.MixToward(g->Out + s * g->MaxFrames, src, frames, u->Gain, u->Step, u->Target);
                    g->SourceUsed[s] = 1;
                }
                u->ChannelsUsed = used;
                u->QuietBlocks = used == 0 ? math.min(u->QuietBlocks + 1, 1 << 30) : 0;
                u->Gain = gEnd;
                u->GainEnd = gEnd;
                if (gEnd == u->Target) u->Step = 0f;
                if (u->Target <= 0f && gEnd <= 0f) { u->Suspended = 1; u->Flags = UnitFlags.Reset; u->PendingCount = 0; }
            }
        }

        static float Advance(float gain, float step, float target, int frames)
        {
            if (step == 0f) return target;
            float g = gain + step * frames;
            return step > 0f ? math.min(g, target) : math.max(g, target);
        }
    }
}
