// The platform's placeholder sound (12-new-instruments/platform.md 13.3): a soft wooden knock, three decaying modes per
// stroke, six voices round robin. Phase 0's five stub instruments play it through their own unit-only role (each stub's
// Audio/Sound/<X>/<X>StubUnit.cs implements its Create<X> / Render<X> partials with it), so a stub is audible and the
// engine path of every new role (register, render, idle skip, Share, the test tap) is exercised before the real units
// exist. The instrument agents replace their stub file with their own unit; this file stays as the engine's utility.
//
// Budget (platform.md 10.3): at block start the knock reads u->Graph->Budget / 1200 x u->Share and keeps at most that
// fraction of its six voices (at least one): the oldest voices over the cap fade out across the block and retire, so
// at Share 0.4 two voices ring instead of six. u->Work is the block's rendered mode-samples (3 per voice per sample).
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    /// <summary>The knock's unmanaged state (one allocation).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct KnockState
    {
        public const int Voices = 6, Modes = 3;
        /// <summary>Voice v is ModalGroup (&amp;V0)[v]: lanes 0-2 its modes (lane 3 unused).</summary>
        public ModalGroup V0, V1, V2, V3, V4, V5;
        public fixed int Channel[Voices];
        /// <summary>Start order of each voice (the oldest is the first to go over the cap).</summary>
        public fixed int Order[Voices];
        /// <summary>1 while the voice fades out across the current block (over the cap); it retires at the block's end.</summary>
        public fixed int Fade[Voices];
        public int Seq;
        public int Knocks, Resets, Hushes, Resumes;
        /// <summary>The last block's rendered mode-samples (also u->Work) and voice cap.</summary>
        public int LastWork, Cap;
    }

    /// <summary>The knock's audio-thread side, called from a stub's Render&lt;X&gt; partial inside AudioGraph.RenderBlock.</summary>
    public static unsafe class KnockDsp
    {
        public const byte OpKnock = 1;   // N = channel, Id = voice, A = hz, B = amplitude
        public const byte OpHush = 2;    // every voice to 0 (ReleaseAll, a reset)
        /// <summary>Mode ratios and decay times (s) of a small hardwood block.</summary>
        public static readonly float3 Ratio = new float3(1f, 2.76f, 5.40f), Tau = new float3(0.16f, 0.07f, 0.035f), Gain = new float3(1f, 0.45f, 0.18f);

        /// <summary>The voice cap at budget scale x (Graph->Budget / 1200 x Share): round(6 x), 1..6.</summary>
        public static int VoiceCap(float scale) => math.clamp((int)math.floor(KnockState.Voices * scale + 0.5f), 1, KnockState.Voices);

        public static void Render(UnitSlot* u)
        {
            var s = (KnockState*)u->State;
            if (s == null) return;
            if ((u->Flags & UnitFlags.Reset) != 0) { Hush(s); s->Resets++; }
            if ((u->Flags & UnitFlags.Resumed) != 0) s->Resumes++;
            float scale = u->Share;
            if (u->Graph != null) scale *= u->Graph->Budget / 1200f;
            int cap = VoiceCap(scale);
            s->Cap = cap;
            LimitVoices(s, cap);
            int cursor = 0, work = 0;
            for (int e = 0; e < u->DueCount; e++)
            {
                UnitEvent* ev = u->Due + e;
                work += Segment(u, s, cursor, ev->Offset);
                Apply(u, s, ev);
                cursor = ev->Offset;
            }
            work += Segment(u, s, cursor, u->Frames);
            s->LastWork = work;
            u->Work = work;
            ModalGroup* g = &s->V0;
            for (int v = 0; v < KnockState.Voices; v++)
            {
                if (s->Fade[v] != 0) { s->Fade[v] = 0; g[v].Zr = float4.zero; g[v].Zi = float4.zero; continue; }   // faded out over the block
                g[v].Zr = math.select(g[v].Zr, 0f, math.abs(g[v].Zr) < DspKernels.Flush);
                g[v].Zi = math.select(g[v].Zi, 0f, math.abs(g[v].Zi) < DspKernels.Flush);
                // retire below -110 dBFS
                if (math.cmax(math.abs(g[v].Zr) + math.abs(g[v].Zi)) * math.cmax(math.abs(g[v].Go)) < 3e-6f) { g[v].Zr = float4.zero; g[v].Zi = float4.zero; }
            }
        }

        static bool Sounding(ModalGroup* m) => !(math.all(m->Zr == 0f) && math.all(m->Zi == 0f));

        /// <summary>More voices ringing than the cap: the oldest of them fade out across this block.</summary>
        static void LimitVoices(KnockState* s, int cap)
        {
            ModalGroup* g = &s->V0;
            int active = 0;
            for (int v = 0; v < KnockState.Voices; v++) if (Sounding(g + v)) active++;
            for (int over = active - cap; over > 0; over--)
            {
                int oldest = -1;
                for (int v = 0; v < KnockState.Voices; v++)
                    if (s->Fade[v] == 0 && Sounding(g + v) && (oldest < 0 || s->Order[v] - s->Order[oldest] < 0)) oldest = v;
                if (oldest < 0) break;
                s->Fade[oldest] = 1;
            }
        }

        static void Hush(KnockState* s)
        {
            ModalGroup* g = &s->V0;
            for (int v = 0; v < KnockState.Voices; v++) { g[v].Zr = float4.zero; g[v].Zi = float4.zero; s->Fade[v] = 0; }
        }

        static int Segment(UnitSlot* u, KnockState* s, int from, int to)
        {
            if (to <= from) return 0;
            int work = 0;
            ModalGroup* g = &s->V0;
            float inv = 1f / math.max(1, u->Frames);
            for (int v = 0; v < KnockState.Voices; v++)
            {
                ModalGroup* m = g + v;
                if (!Sounding(m)) continue;
                int ch = s->Channel[v];
                if ((uint)ch >= (uint)u->Channels) continue;
                float* o = u->Channel(ch);
                float4 zr = m->Zr, zi = m->Zi, pr = m->Pr, pi = m->Pi, go = m->Go;
                if (s->Fade[v] != 0)
                {
                    // over the cap: a linear fade to zero across the whole block (continuous over the event splits)
                    for (int n = from; n < to; n++)
                    {
                        float4 t = pr * zi + pi * zr;
                        zr = pr * zr - pi * zi;
                        zi = t;
                        o[n] += math.csum(go * zi) * (1f - (n + 1) * inv);
                    }
                }
                else
                {
                    for (int n = from; n < to; n++)
                    {
                        float4 t = pr * zi + pi * zr;
                        zr = pr * zr - pi * zi;
                        zi = t;
                        o[n] += math.csum(go * zi);
                    }
                }
                m->Zr = zr; m->Zi = zi;
                work += KnockState.Modes * (to - from);
            }
            return work;
        }

        static void Apply(UnitSlot* u, KnockState* s, UnitEvent* ev)
        {
            ModalGroup* g = &s->V0;
            switch ((byte)ev->C.Op)
            {
                case OpKnock:
                {
                    int v = (int)((uint)ev->C.Id % KnockState.Voices);
                    ModalGroup* m = g + v;
                    double sr = u->SampleRate;
                    for (int l = 0; l < 3; l++)
                    {
                        double hz = ev->C.A * Ratio[l], r = Math.Exp(-1.0 / (Tau[l] * sr)), w = 2.0 * Math.PI * Math.Min(hz, sr * 0.45) / sr;
                        m->Pr[l] = (float)(r * Math.Cos(w));
                        m->Pi[l] = (float)(r * Math.Sin(w));
                        m->Go[l] = ev->C.B * Gain[l];
                        m->Zr[l] = 0f; m->Zi[l] = 1f;   // starts at its peak displacement velocity: a struck block
                    }
                    m->Go[3] = 0f; m->Zr[3] = 0f; m->Zi[3] = 0f;
                    s->Channel[v] = ev->C.N;
                    s->Order[v] = ++s->Seq;
                    s->Fade[v] = 0;
                    s->Knocks++;
                    break;
                }
                case OpHush:
                    Hush(s);
                    s->Hushes++;
                    break;
            }
        }
    }

    /// <summary>The knock's main-thread side: Knock(index, vel) on channel index % channels at BaseHz x 2^(index / 12 x 5).
    /// A stub's sound interface (I&lt;X&gt;Sound) is implemented by a subclass. Its mf reference phrase (loudness parity) is
    /// the piano's rhythm: four knocks at 108 bpm, vel 0.55.</summary>
    public unsafe class KnockUnit : SoundUnit, ILoudnessReference
    {
        readonly KnockState* _s;
        int _next;
        public readonly float BaseHz;

        /// <summary>idleSkip: the graph skips the knock's render while it is silent (the stubs register with it).</summary>
        public KnockUnit(SoundContext ctx, UnitId role, SourceId[] outputs, float baseHz, bool idleSkip = true) : base(ctx)
        {
            BaseHz = baseHz;
            _s = Alloc<KnockState>();
            Register(ctx, role, outputs, _s, 128, 128, kernel: Kernel, idleSkip: idleSkip);
        }

        /// <summary>The knock's own render hook (UnitHooks.Render), whatever role slot it sits in: a knock never reaches the
        /// role's real unit's Render with a KnockState (a test's knock in a role that has its own unit now).</summary>
        public const UnitId Kernel = (UnitId)200;

        public KnockState* State => _s;
        public int Knocks => System.Threading.Volatile.Read(ref _s->Knocks);
        /// <summary>Renders that came back from an idle skip (UnitFlags.Resumed).</summary>
        public int Resumes => System.Threading.Volatile.Read(ref _s->Resumes);

        /// <summary>One knock: index picks the pitch step and the channel; vel 0..1.</summary>
        public void Knock(int index, float vel, double when = 0)
        {
            RecordUnit(KnockDsp.OpKnock, index, vel, when);
            int ch = Outputs.Length > 0 ? ((index % Outputs.Length) + Outputs.Length) % Outputs.Length : 0;
            float hz = BaseHz * (float)Math.Pow(2.0, (index % 8) * 5 / 12.0);
            float amp = 0.18f * Math.Max(0f, Math.Min(1f, vel));
            Post(new GraphCommand { Op = (GraphOp)KnockDsp.OpKnock, Flags = CommandFlags.LateDrop, When = when, N = ch, Id = _next++, A = hz, B = amp });
        }

        /// <summary>Every knock silenced (IInstrumentPlayback.ReleaseAll).</summary>
        public void Hush(double when = 0)
        {
            RecordUnit(KnockDsp.OpHush, -1, 0f, when);
            Post(new GraphCommand { Op = (GraphOp)KnockDsp.OpHush, When = when });
        }

        /// <summary>The mf reference: E E F G's rhythm (quarters at 108 bpm from 0.2 s) on steps 0, 0, 1, 2, vel 0.55.</summary>
        public double ReferenceSeconds => 4.0;

        static readonly int[] s_referenceSteps = { 0, 0, 1, 2 };

        public void PlayReference(double start)
        {
            const double spb = 60.0 / 108.0;
            for (int i = 0; i < s_referenceSteps.Length; i++) Knock(s_referenceSteps[i], 0.55f, start + 0.2 + i * spb);
        }

        public override string Stats => $"{Id} knock: {Knocks} knocks, idle {IdleBlocks} blocks";
    }
}
