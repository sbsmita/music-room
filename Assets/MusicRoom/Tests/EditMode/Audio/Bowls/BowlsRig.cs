// Test harness for the bowls DSP (phase A: the unit is not registered in the engine yet). One BowlsState in its own
// zeroed, 64-byte aligned memory, a command queue with absolute samples (the reference's post(): sorted by (at, seq),
// due when at < block end, late events at offset 0), rendered in blocks through one of the render entries:
//   Managed (plain C#: the bit-exact reference), Fast (Burst, FloatMode.Fast), Strict (Burst, FloatMode.Strict),
//   StrictPointer (the Strict function pointer called from a Fast Burst entry: phase B's graph path).
// Mirrors BowlsEngine (bowls-engine.mjs) and lib/scenarios.mjs schedule(). No NUnit / Unity dependency, so the same file
// also builds into the offline mono check (a plain .NET build outside Unity).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MusicRoom.Audio.Burst;

namespace MusicRoom.Tests.Audio
{
    public enum BowlsPath { Managed, Fast, Strict, StrictPointer }

    public sealed unsafe class BowlsRig : IDisposable, IBowlsCommandSink
    {
        public const int SR = 48000;
        static BowlDesign[] s_design;
        public static BowlDesign[] Design48k => s_design ?? (s_design = BowlsDesign.Bowls(SR));

        public readonly BowlsState* S;
        public readonly BowlDesign[] Design;
        public readonly BowlsSoundCore Core;
        public BowlsPath Path;
        public long Now;
        public int Budget = 1200;
        public float Share = 1f;
        /// <summary>The unit's one channel and each bowl's block of the last RenderBlock.</summary>
        public readonly float[] Mix = new float[BowlsConst.MaxFrames];
        public readonly float[][] Bowl = { new float[BowlsConst.MaxFrames], new float[BowlsConst.MaxFrames], new float[BowlsConst.MaxFrames] };
        /// <summary>Called with each block's start before the block renders (BowlsVoice's frame posts in the scenarios).</summary>
        public Action<long> BeforeBlock;

        readonly IntPtr _mem, _dueMem, _mixMem;
        readonly UnitEvent* _due;
        readonly float* _mix;
        readonly List<(long at, int seq, GraphCommand c)> _q = new List<(long, int, GraphCommand)>();
        int _seq;
        bool _sorted = true;

        public static IntPtr StrictPointer;   // compiled once per domain (BowlsRigBurst sets it)
        public static Action<IntPtr, IntPtr, IntPtr, int, int, IntPtr, int, float> ViaPointer;   // the Fast trampoline (Unity only)

        public BowlsRig(uint seed = 1, BowlsPath path = BowlsPath.Managed, BowlDesign[] design = null)
        {
            Path = path;
            Design = design ?? Design48k;
            long bytes = sizeof(BowlsState);
            _mem = Marshal.AllocHGlobal((IntPtr)(bytes + 64));
            S = (BowlsState*)(((long)_mem + 63) & ~63L);
            byte* p = (byte*)S;
            for (long i = 0; i < bytes; i++) p[i] = 0;
            BowlsDesign.Build(S, Design, SR, seed);
            _dueMem = Marshal.AllocHGlobal(sizeof(UnitEvent) * 1024 + 64);
            _due = (UnitEvent*)(((long)_dueMem + 63) & ~63L);
            _mixMem = Marshal.AllocHGlobal(4 * BowlsConst.MaxFrames + 64);
            _mix = (float*)(((long)_mixMem + 63) & ~63L);
            Core = new BowlsSoundCore(S, this, SR);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_mem);
            Marshal.FreeHGlobal(_dueMem);
            Marshal.FreeHGlobal(_mixMem);
        }

        public BowlState* B(int i) => BowlsState.Bowl(S, i);
        public int PendingCount => _q.Count;

        // ---- the reference engine's main-thread API (absolute samples) ----
        public void Post(long at, GraphCommand c) { _q.Add((at, _seq++, c)); _sorted = false; }
        /// <summary>IBowlsCommandSink: When > 0 is absolute seconds (round(When sr)), 0 = now, &lt; 0 relative.</summary>
        public void Post(in GraphCommand c)
        {
            long at = c.When > 0 ? (long)Math.Round(c.When * SR) : Now + (long)Math.Round(-c.When * SR);
            _q.Add((at, _seq++, c)); _sorted = false;
        }
        public void Strike(int bowl, BowlStrikeKind kind, double V, double angle, double hs, long at) => Post(at, BowlsCommands.Strike(bowl, kind, V, angle, hs, SR));
        public void StrikeRaw(int bowl, double impulse, double T, double cs, double sn, double hs, long at) => Post(at, BowlsCommands.StrikeRaw(bowl, impulse, T, cs, sn, hs, SR));
        public void Contact(int bowl, int kind, double N, double vf, double angle, double omega, double hs, int lane, int ramp, long at)
            => Post(at, BowlsCommands.Contact(bowl, kind, N, vf, angle, omega, hs, lane, ramp, SR));
        public void Release(int bowl, long at, int lane = 0) => Post(at, BowlsCommands.Release(bowl, lane));
        public void Damp(int bowl, int level, long at) => Post(at, BowlsCommands.Damp(bowl, level));
        public void Pickup(int bowl, double angle, long at, bool snap) => Post(at, BowlsCommands.Pickup(bowl, angle, snap));
        public void Seed(uint seed, long at) { for (int b = 0; b < 3; b++) Post(at, BowlsCommands.Seed(b, seed)); }
        public void Silence(int bowl, long at) => Post(at, BowlsCommands.Silence(bowl));

        /// <summary>One block: due events (at &lt; end, sorted by (at, seq), offset max(0, at - start)) -> render.</summary>
        public void RenderBlock(int frames)
        {
            BeforeBlock?.Invoke(Now);
            long start = Now, end = Now + frames;
            int n = 0;
            if (_q.Count > 0)
            {
                if (!_sorted) { _q.Sort((a, b) => a.at != b.at ? a.at.CompareTo(b.at) : a.seq.CompareTo(b.seq)); _sorted = true; }
                int k = 0;
                while (k < _q.Count && _q[k].at < end)
                {
                    if (n < 1024) _due[n++] = new UnitEvent { At = _q[k].at, Offset = (int)Math.Max(0, _q[k].at - start), Seq = _q[k].seq, C = _q[k].c };
                    k++;
                }
                if (k > 0) _q.RemoveRange(0, k);
            }
            for (int i = 0; i < frames; i++) _mix[i] = 0f;
            switch (Path)
            {
                case BowlsPath.Fast: BowlsDsp.RenderFast(S, _due, n, frames, _mix, Budget, Share); break;
                case BowlsPath.Strict: BowlsDsp.RenderStrict(S, _due, n, frames, _mix, Budget, Share); break;
                case BowlsPath.StrictPointer:
                    if (ViaPointer != null && StrictPointer != IntPtr.Zero) ViaPointer(StrictPointer, (IntPtr)S, (IntPtr)_due, n, frames, (IntPtr)_mix, Budget, Share);
                    else BowlsDsp.Render(S, _due, n, frames, _mix, Budget, Share);
                    break;
                default: BowlsDsp.Render(S, _due, n, frames, _mix, Budget, Share); break;
            }
            for (int i = 0; i < frames; i++) Mix[i] = _mix[i];
            for (int b = 0; b < 3; b++) { float* y = S->Y + b * BowlsConst.MaxFrames; float[] o = Bowl[b]; for (int i = 0; i < frames; i++) o[i] = y[i]; }
            Now = end;
        }

        /// <summary>Renders nSamples in 256-frame blocks; channel 0..2 = a bowl, -1 = the mix. onBlock(k) before block k.</summary>
        public float[] Render(int nSamples, int channel = -1, Action<int> onBlock = null, int block = 256)
        {
            var y = new float[nSamples];
            for (int s0 = 0, k = 0; s0 < nSamples; s0 += block, k++)
            {
                int m = Math.Min(block, nSamples - s0);
                onBlock?.Invoke(k);
                RenderBlock(m);
                float[] src = channel < 0 ? Mix : Bowl[channel];
                Array.Copy(src, 0, y, s0, m);
            }
            return y;
        }

        // ---------------------------------------------------------------------------------------------- scenarios
        // lib/scenarios.mjs schedule(): contact commands once per 90 Hz display frame with the angle seen at that frame.
        public const double FrameHz = 90;

        public sealed class Act
        {
            public double T; public string Type; public int Bowl; public double V = 1; public double? Angle; public double H = 1;
            public double Dur; public double Vf; public double N = 3; public int Kind; public double? Angle0; public double? TouchV;
            public double? VfEnd, NEnd; public double JitAngle, JitSpeed; public int? JitSeed; public bool Jitter; public int Lane; public int Level;
        }

        static BowlStrikeKind KindOf(string t) => t == "palm" ? BowlStrikeKind.Palm : t == "suede" ? BowlStrikeKind.Suede : t == "wood" ? BowlStrikeKind.Wood : BowlStrikeKind.Tap;

        public void Schedule(IEnumerable<Act> acts, double facing = 0)
        {
            double frame = SR / FrameHz;
            foreach (var a in acts)
            {
                long at = (long)BowlsConst.JsRound(a.T * SR);
                if (a.Type == "tap" || a.Type == "palm" || a.Type == "suede" || a.Type == "wood") Strike(a.Bowl, KindOf(a.Type), a.V, a.Angle ?? facing, a.H, at);
                else if (a.Type == "damp") { Damp(a.Bowl, a.Level, at); if (a.Dur > 0) Damp(a.Bowl, 0, at + (long)BowlsConst.JsRound(a.Dur * SR)); }
                else if (a.Type == "rub")
                {
                    double R = Design[a.Bowl].R, ang0 = a.Angle0 ?? facing;
                    uint rng = (uint)(a.JitSeed ?? 7);
                    double Rnd() { unchecked { rng = rng * 1664525u + 1013904223u; } return rng / 4294967296.0 - 0.5; }
                    if (!(a.TouchV.HasValue && a.TouchV.Value == 0)) Strike(a.Bowl, BowlStrikeKind.Tap, a.TouchV ?? 0.15, ang0, a.H, at);
                    double ang = ang0;
                    int nF = (int)Math.Max(1, BowlsConst.JsRound(a.Dur * FrameHz));
                    for (int k = 0; k < nF; k++)
                    {
                        double u = (double)k / nF;
                        double vf0 = a.Vf + (a.VfEnd.HasValue ? (a.VfEnd.Value - a.Vf) * u : 0), N = a.N + (a.NEnd.HasValue ? (a.NEnd.Value - a.N) * u : 0);
                        double vf = vf0 * (1 + (a.Jitter ? a.JitSpeed * 2 * Rnd() : 0));
                        double om = vf / R;
                        double seen = ang + (a.Jitter ? a.JitAngle * 2 * Rnd() : 0);
                        Contact(a.Bowl, a.Kind, N, vf, seen, om, a.H, a.Lane, 0, at + (long)BowlsConst.JsRound(k * frame));
                        ang += om / FrameHz;
                    }
                    Release(a.Bowl, at + (long)BowlsConst.JsRound(nF * frame), a.Lane);
                }
            }
        }

        public static Act Rub(double t, int bowl, double dur, double vf, double N, int kind = 0) => new Act { T = t, Type = "rub", Bowl = bowl, Dur = dur, Vf = vf, N = N, Kind = kind };
        public static Act Tap(double t, int bowl, double V, double angle, string kind = "tap") => new Act { T = t, Type = kind, Bowl = bowl, V = V, Angle = angle };

        // ---------------------------------------------------------------------------------------------- the vectors
        /// <summary>test-vectors.mjs: the 16 vectors' set-ups (seed, samples, channel; -1 = mix).</summary>
        public static void Vector(string name, out uint seed, out int samples, out int channel, out Action<BowlsRig> setup, out Action<BowlsRig, int> onBlock)
        {
            seed = 1; onBlock = null;
            switch (name)
            {
                case "TV1_tap_medium": samples = 4800; channel = 1; setup = e => { e.Pickup(1, 0.4, 0, true); e.Strike(1, BowlStrikeKind.Tap, 1, 0.4, 1, 37); }; break;
                case "TV2_palm_large": samples = 4800; channel = 0; setup = e => { e.Pickup(0, -0.2, 0, true); e.Strike(0, BowlStrikeKind.Palm, 1.2, 0.9, 0.85, 300); }; break;
                case "TV3_two_taps_overlap": samples = 4800; channel = 2; setup = e => { e.Pickup(2, 0, 0, true); e.Strike(2, BowlStrikeKind.Tap, 0.8, 0.1, 1, 10); e.Strike(2, BowlStrikeKind.Tap, 1.6, 2.0, 1, 40); e.Strike(2, BowlStrikeKind.Tap, 0.5, 1.0, 1, 70); }; break;
                case "TV4_damp_palm": samples = 9600; channel = 1; setup = e => { e.Pickup(1, 0.4, 0, true); e.Strike(1, BowlStrikeKind.Tap, 1.5, 0.4, 1, 0); e.Damp(1, 3, 2400); e.Damp(1, 0, 7000); }; break;
                case "TV5_pickup_slew": samples = 9600; channel = 1; setup = e => { e.Pickup(1, 0.4, 0, true); e.Strike(1, BowlStrikeKind.Tap, 1.5, 0.4, 1, 0); e.Pickup(1, 1.2, 2000, false); }; break;
                case "TV6_rub_outer": samples = 24000; channel = 2; setup = e => { e.Pickup(2, 0, 0, true); e.Schedule(new[] { Rub(0, 2, 0.45, 0.3, 3) }); }; break;
                case "TV7_rub_lip_reverse": samples = 24000; channel = 1; setup = e => { e.Pickup(1, 0, 0, true); e.Schedule(new[] { Rub(0, 1, 0.2, 0.25, 3, 1), new Act { T = 0.21, Type = "rub", Bowl = 1, Dur = 0.2, Vf = -0.25, N = 3, Kind = 1, TouchV = 0, Angle0 = 0.25 / 0.08 * 0.2 } }); }; break;
                case "TV8_rub_inner": samples = 24000; channel = 1; setup = e => { e.Pickup(1, 0, 0, true); e.Schedule(new[] { Rub(0, 1, 0.45, 0.3, 3, 2) }); }; break;
                case "TV9_resting_finger": samples = 24000; channel = 1; setup = e => { e.Pickup(1, 0.4, 0, true); e.Strike(1, BowlStrikeKind.Tap, 1.5, 0.4, 1, 0); e.Schedule(new[] { new Act { T = 0.1, Type = "rub", Bowl = 1, Dur = 0.3, Vf = 0, N = 3, TouchV = 0, Angle0 = 0.4 } }); }; break;
                case "TV10_seed77_rub": seed = 77; samples = 12000; channel = 2; setup = e => { e.Pickup(2, 0, 0, true); e.Schedule(new[] { Rub(0, 2, 0.24, 0.3, 3) }); }; break;
                case "TV11_retire": samples = 48000 * 30; channel = 2; setup = e => { e.Pickup(2, 0, 0, true); e.StrikeRaw(2, 1e-5, 0.001, 1, 0, 1, 0); }; break;
                case "TV12_suede_wood": samples = 9600; channel = 0; setup = e => { e.Pickup(0, 0.3, 0, true); e.Strike(0, BowlStrikeKind.Suede, 1.0, 0.3, 1, 20); e.Strike(0, BowlStrikeKind.Wood, 0.6, 1.4, 1, 4800); }; break;
                case "TV13_lane_preempt": samples = 24000; channel = 1; setup = e => { e.Pickup(1, 0, 0, true); e.Strike(1, BowlStrikeKind.Tap, 0.2, 0, 1, 0); e.Schedule(new[] { new Act { T = 0.0, Type = "rub", Bowl = 1, Dur = 0.45, Vf = 0.3, N = 3.5, Lane = 1, TouchV = 0 }, new Act { T = 0.2, Type = "rub", Bowl = 1, Dur = 0.2, Vf = -0.25, N = 4, Lane = 0, Angle0 = 2.0, TouchV = 0 } }); }; break;
                case "TV14_glide_ramp":
                    samples = 14400; channel = 2;
                    setup = e => { e.Pickup(2, 0, 0, true); e.Strike(2, BowlStrikeKind.Tap, 0.2, 0, 1, 0); double R = e.Design[2].R; e.Contact(2, 0, 3, 0.25, 0, 0.25 / R, 1, 2, 0, 10); e.Contact(2, 0, 5, 0.35, 0.4, 0.35 / R, 1, 2, 4800, 2400); e.Contact(2, 0, 0, 0.35, 1.0, 0.35 / R, 1, 2, 2400, 9600); e.Release(2, 12000, 2); };
                    break;
                case "TV15_share_cap":
                    samples = 9600; channel = -1;
                    setup = e => { for (int b = 0; b < 3; b++) { e.Pickup(b, 0.3, 0, true); e.Strike(b, BowlStrikeKind.Tap, 1.2, 0.3 + b, 1, 10 + b); } };
                    onBlock = (e, k) => { if (k == 16) e.Share = 0.4f; if (k == 30) e.Share = 1f; };
                    break;
                case "TV16_mix_three":
                    samples = 9600; channel = -1;
                    setup = e => { for (int b = 0; b < 3; b++) e.Pickup(b, -0.5 + b, 0, true); e.Strike(0, BowlStrikeKind.Palm, 1.0, 0.2, 1, 5); e.Strike(1, BowlStrikeKind.Tap, 0.9, 1.1, 1, 900); e.Schedule(new[] { Rub(0.02, 2, 0.15, 0.3, 3.5) }); };
                    break;
                default: throw new ArgumentException(name);
            }
        }

        public static readonly string[] VectorNames = {
            "TV1_tap_medium", "TV2_palm_large", "TV3_two_taps_overlap", "TV4_damp_palm", "TV5_pickup_slew", "TV6_rub_outer",
            "TV7_rub_lip_reverse", "TV8_rub_inner", "TV9_resting_finger", "TV10_seed77_rub", "TV11_retire", "TV12_suede_wood",
            "TV13_lane_preempt", "TV14_glide_ramp", "TV15_share_cap", "TV16_mix_three" };

        /// <summary>Renders a vector; retiredBlock = the first block after which bowl `channel` sleeps (-1 if it never does).</summary>
        public static float[] RunVector(string name, BowlsPath path, out BowlsRig rigOut, out int retiredBlock)
        {
            Vector(name, out uint seed, out int samples, out int channel, out var setup, out var onBlock);
            var e = new BowlsRig(seed, path);
            setup(e);
            var y = new float[samples];
            retiredBlock = -1;
            for (int s0 = 0, k = 0; s0 < samples; s0 += 256, k++)
            {
                int m = Math.Min(256, samples - s0);
                onBlock?.Invoke(e, k);
                e.RenderBlock(m);
                Array.Copy(channel < 0 ? e.Mix : e.Bowl[channel], 0, y, s0, m);
                if (retiredBlock < 0 && channel >= 0 && e.B(channel)->Awake == 0) retiredBlock = k;
            }
            rigOut = e;
            return y;
        }

        public static unsafe uint Fnv1a(float[] x)
        {
            uint h = 0x811c9dc5;
            for (int i = 0; i < x.Length; i++)
            {
                float f = x[i];
                uint w = *(uint*)&f;
                for (int k = 0; k < 4; k++) { h ^= w & 0xff; unchecked { h *= 0x01000193; } w >>= 8; }
            }
            return h;
        }
    }
}
