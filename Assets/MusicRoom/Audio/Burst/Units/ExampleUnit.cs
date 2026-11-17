// The engine's reference unit: the smallest complete sound unit, written the way an instrument unit is (ENGINE-API.md
// section 4 walks through it). Eight decaying sine voices (one MB4 lane each); commands Note / Damp / Hush; it plays the
// piano role in the engine's own tests (SoundUnits.Override), so routing, handles, fades, suspend and reset are tested
// without any instrument. Not created in the app.
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace MusicRoom.Audio.Burst
{
    /// <summary>The Example unit's unmanaged state (one allocation, AudioGraphHost.Alloc).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExampleState
    {
        public const int Voices = 8;
        public ModalGroup G0, G1;          // 8 lanes = 8 voices (state z, pole p, Go = amplitude)
        public fixed int Channel[Voices];  // output channel per voice
        public fixed int Gen[Voices];      // handle generation per voice
        public int Next;                   // round-robin voice
        public int Notes, Resets;          // stats (audio thread)
        public int LastOffset;             // offset of the last applied event (tests)
    }

    /// <summary>The Example unit's audio-thread side (called from UnitHooks.Render inside AudioGraph.RenderBlock).</summary>
    public static unsafe class ExampleUnitDsp
    {
        public const byte OpNote = 1;   // N = channel, Id = voice slot, A = hz, B = amplitude, C = tau (s)
        public const byte OpDamp = 2;   // Id = voice slot, A = tau (s): the voice decays faster from here
        public const byte OpHush = 3;   // every voice to 0 at once (tests of the engine fade: the unit itself never clicks otherwise)

        public static void Render(UnitSlot* u)
        {
            var s = (ExampleState*)u->State;
            if (s == null) return;
            if ((u->Flags & UnitFlags.Reset) != 0) { s->G0.Zr = s->G0.Zi = s->G1.Zr = s->G1.Zi = float4.zero; s->Resets++; }
            int cursor = 0;
            for (int e = 0; e < u->DueCount; e++)
            {
                UnitEvent* ev = u->Due + e;
                Segment(u, s, cursor, ev->Offset);
                Apply(u, s, ev);
                cursor = ev->Offset;
            }
            Segment(u, s, cursor, u->Frames);
            // denormal policy (burst.md section 6): flush at block end
            Flush(&s->G0); Flush(&s->G1);
        }

        static void Flush(ModalGroup* g)
        {
            g->Zr = math.select(g->Zr, 0f, math.abs(g->Zr) < DspKernels.Flush);
            g->Zi = math.select(g->Zi, 0f, math.abs(g->Zi) < DspKernels.Flush);
        }

        static void Segment(UnitSlot* u, ExampleState* s, int from, int to)
        {
            if (to <= from) return;
            for (int v = 0; v < ExampleState.Voices; v++)
            {
                ModalGroup* g = &s->G0;
                if (v >= 4) g = &s->G1;
                int l = v & 3;
                float zr = g->Zr[l], zi = g->Zi[l];
                if (zr == 0f && zi == 0f) continue;
                float pr = g->Pr[l], pi = g->Pi[l], go = g->Go[l];
                int ch = s->Channel[v];
                if ((uint)ch >= (uint)u->Channels) continue;
                float* o = u->Channel(ch);
                for (int n = from; n < to; n++)
                {
                    float t = math.mad(pr, zi, pi * zr);
                    zr = math.mad(pr, zr, -pi * zi);
                    zi = t;
                    o[n] += go * zi;
                }
                g->Zr[l] = zr; g->Zi[l] = zi;
            }
        }

        static void Apply(UnitSlot* u, ExampleState* s, UnitEvent* ev)
        {
            s->LastOffset = ev->Offset;
            int v = ev->C.Id & (ExampleState.Voices - 1);
            ModalGroup* g = &s->G0;
            if (v >= 4) g = &s->G1;
            int l = v & 3;
            double sr = u->SampleRate;
            switch ((byte)ev->C.Op)
            {
                case OpNote:
                {
                    double r = ev->C.C > 0 ? Math.Exp(-1.0 / (ev->C.C * sr)) : 0.0, w = 2.0 * Math.PI * ev->C.A / sr;
                    g->Pr[l] = (float)(r * Math.Cos(w));
                    g->Pi[l] = (float)(r * Math.Sin(w));
                    g->Go[l] = ev->C.B;
                    g->Zr[l] = 1f; g->Zi[l] = 0f;     // a sine from phase 0
                    s->Channel[v] = ev->C.N;
                    s->Notes++;
                    break;
                }
                case OpDamp:
                {
                    double pr = g->Pr[l], pi = g->Pi[l], w = Math.Atan2(pi, pr);
                    double r = ev->C.A > 0 ? Math.Exp(-1.0 / (ev->C.A * sr)) : 0.0;
                    g->Pr[l] = (float)(r * Math.Cos(w));
                    g->Pi[l] = (float)(r * Math.Sin(w));
                    break;
                }
                case OpHush:
                    s->G0.Zr = s->G0.Zi = s->G1.Zr = s->G1.Zi = float4.zero;
                    break;
            }
        }
    }

    /// <summary>The Example unit's main-thread side. In the piano role it maps midi to a sine on the register's source.</summary>
    public sealed unsafe class ExampleUnit : SoundUnit
    {
        readonly ExampleState* _s;
        int _next, _gen;

        /// <summary>role: the role it plays (tests use Piano); outputs: its channels (default: the piano's three sources).</summary>
        public ExampleUnit(SoundContext ctx, UnitId role = UnitId.Example, SourceId[] outputs = null) : base(ctx)
        {
            _s = Alloc<ExampleState>();
            Register(ctx, role, outputs ?? new[] { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi }, _s, 64, 64, UnitId.Example);
        }

        public ExampleState* State => _s;
        public int Notes => System.Threading.Volatile.Read(ref _s->Notes);
        public int Resets => System.Threading.Volatile.Read(ref _s->Resets);

        /// <summary>A decaying sine on channel ch; returns the voice slot.</summary>
        public int Note(int ch, float hz, float amp, float tau, double when = 0)
        {
            int v = _next++ & (ExampleState.Voices - 1);
            Post(new GraphCommand { Op = (GraphOp)ExampleUnitDsp.OpNote, Flags = CommandFlags.LateDrop, When = when, N = ch, Id = v, A = hz, B = amp, C = tau });
            return v;
        }

        public void Damp(int voice, float tau, double when = 0) =>
            Post(new GraphCommand { Op = (GraphOp)ExampleUnitDsp.OpDamp, When = when, Id = voice, A = tau });

        public void Hush(double when = 0) => Post(new GraphCommand { Op = (GraphOp)ExampleUnitDsp.OpHush, When = when });

        // ---- a piano-like API (tests): midi -> sine, register -> channel, Release -> damp tau 60 ms
        public VoiceHandle Piano(int midi, float vel, double when)
        {
            int ch = midi < 52 ? 0 : midi < 76 ? 1 : 2;
            int v = Note(ch, (float)(440.0 * Math.Pow(2, (midi - 69) / 12.0)), 0.2f * vel, 1.5f, when);
            _gen++;
            return UnitHandles.Make(Context.Source(Outputs[ch]), Id, (_gen << 3) | v);
        }

        public void Release(VoiceHandle h, bool thud, double when)
        {
            if (!UnitHandles.IsUnit(h) || UnitHandles.Unit(h) != Id) return;
            Damp(UnitHandles.Payload(h) & 7, 0.06f, when);
        }

        public override string Stats => $"example notes {Notes} resets {Resets}";
    }
}
