// The singing bowls' design (bowls.md sections 2.2-2.5; the Node bowls design reference, transliterated operation by
// operation). Main thread, double maths, every table value rounded ONCE to float32, exactly as the reference's f32(). The
// three bowls (big G3, middle C4, small E4) each copy one measured bowl's mode ratios and split pattern:
//   [I06] O. Inacio, L. Henrique, J. Antunes, "The dynamics of Tibetan singing bowls", Acta Acustica 92(4) 2006, Table I
//   [WSN02] C. Wilkerson, S. Serafin, C. Ng, "Physical model synthesis and performance mappings of bowl resonators", 2002
//   [TB11] D. Terwagne, J. W. M. Bush, "Tibetan singing bowls", Nonlinearity 24 (2011): split pairs, pi / 2n apart
// Allocation happens here only (design objects), never on the audio thread.
using System;

namespace MusicRoom.Audio.Burst
{
    /// <summary>Which surface of a bowl a contact is on (bowls.md 2.10: Contact.N).</summary>
    public enum BowlSurface : byte { Outer = 0, Lip = 1, Inner = 2 }

    /// <summary>What strikes a bowl (bowls.md 2.3): a fingertip, a palm, the puja's suede end, its bare wooden shaft.</summary>
    public enum BowlStrikeKind : byte { Tap = 0, Palm = 1, Suede = 2, Wood = 3 }

    /// <summary>The design constants (bowls.md 2.3, the reference's C). Doubles; the unit rounds them once.</summary>
    public static class BowlsConst
    {
        public const int SR = 48000;
        public const int NP = 8, NM = 16, NG = 4, NLEVEL = 4, NPOLESET = 8, NLANE = 4, NPULSE = 4;
        public const int NBOWLS = 3;
        public const int MaxFrames = 1024;
        public const int PulseKMax = 600;

        public const double ZETA0 = 2e-5, ZETA_EXP = 0.68, ZETA_MAX = 1.5e-4;
        public static readonly double[] SUPPORT = { 4, 3, 2, 2, 2, 2, 2, 2 };
        public const double MASS_FRACTION = 0.2;
        public static readonly double[] RAD_DB = { 0, -18.2, -15.6, -16.3, -13.0, -14.5, -16.0, -18.0 };
        public const double MALLET_MS = 0.25, MALLET_FLOOR = 0.316;
        public const double FILL = 0.35;
        public const double OUT_GAIN = 413.333333, F_REF = 261.626;
        public static readonly double[] TOUCH_ALPHA = { 0, 0.8, 8, 25 };
        public const double TOUCH_EXP = 0.5;
        public const double CONTACT_ALPHA = 1.5;
        public const double MU_S = 0.85, MU_INF = 0.30, VC = 0.25, EPS = 1e-4;
        public const double KC = 1e5, CC = 30, M_TIP = 0.02;
        public const int NORMAL_RAMP = 512, ONSET_RAMP = 1440, RELEASE_RAMP = 1440;
        public const int RAMP_MIN = 64, RAMP_MAX = 12000;
        public const double ANGLE_LOCK = 0.02;
        /// <summary>The soft pad (SetPad): the blend of the stick's force toward N, the pad's low-pass (Hz), its pressing force (N).</summary>
        public const double PAD_MIX = 0.3, PAD_HZ = 1200, PAD_N = 5.0;
        /// <summary>The app's tonal rub noise (SetRubNoise): the friction noise's scale against NOISE_K, its cap against the
        /// bowl's tone envelope (amplitude ratio), and the envelope's time constant (s).</summary>
        public const double RUB_NOISE_SCALE = 0.5, RUB_NOISE_REL = 0.12, TONE_TAU = 0.02;
        public const double NOISE_HZ = 1800, NOISE_Q = 0.5, NOISE_LP = 5000, NOISE_K = 0.018, NOISE_TAU = 0.010;
        public const double T_MIN = 0.0004, T_MAX = 0.012;
        public const double HP_HZ = 25;
        public const double CULL2 = 1e-11, FLUSH = 1e-20;
        public const double PULSE_SPLIT = 0.6, PULSE_SHORT = 0.72;
        public const int CONTACT_WORK = 24;
        public const double PICK_SLEW = 0.005;

        /// <summary>Strike constants per kind: mass (kg), restitution, reference contact time (s) at 1 m/s.</summary>
        public static void StrikeParams(BowlStrikeKind kind, out double m, out double e, out double tRef)
        {
            switch (kind)
            {
                case BowlStrikeKind.Palm: m = 0.090; e = 0.3; tRef = 0.003; break;
                case BowlStrikeKind.Suede: m = 0.040; e = 0.4; tRef = 0.0016; break;
                case BowlStrikeKind.Wood: m = 0.040; e = 0.7; tRef = 0.00035; break;
                default: m = 0.030; e = 0.6; tRef = 0.0009; break;
            }
        }

        /// <summary>strikePulse(): impulse I = (1 + e) m V (N s) and contact time T = T_ref V^-0.2, clamped 0.4-12 ms.</summary>
        public static void StrikePulse(BowlStrikeKind kind, double V, out double impulse, out double T)
        {
            StrikeParams(kind, out double m, out double e, out double tRef);
            double v = Math.Max(0.01, V);
            T = Math.Min(T_MAX, Math.Max(T_MIN, tRef * Math.Pow(v / 1.0, -0.2)));
            impulse = (1 + e) * m * v;
        }

        /// <summary>JavaScript's Math.round (half toward +infinity), which the reference uses for every sample index.</summary>
        public static double JsRound(double x) => Math.Floor(x + 0.5);
    }

    /// <summary>One mode (one member of an (n,0) pair) of a designed bowl.</summary>
    public sealed class BowlMode
    {
        public int N, Member;
        public double F, W, Zeta, T60, Theta0, Rad;
        public float Gi, Om;
        public float[] PolePr = new float[BowlsConst.NPOLESET], PolePi = new float[BowlsConst.NPOLESET];
    }

    /// <summary>One designed bowl (designBowls()[i]).</summary>
    public sealed class BowlDesign
    {
        public int Index;
        public string Key, Label, Note;
        public int Midi;
        public double F0, RimDiam, Height, Wall, Mass, M, R, OutScale;
        public double[] Ratios, Splits;
        public BowlMode[] Modes = new BowlMode[BowlsConst.NM];
        public float[] Oc = new float[BowlsConst.NP], Os = new float[BowlsConst.NP], InvN = new float[BowlsConst.NP];
        public float Y0t;
    }

    /// <summary>designBowls(sr): the three bowls as float32 tables (main thread).</summary>
    public static class BowlsDesign
    {
        // [I06] Table I, double modal frequencies (Hz): f_A, f_B for (2,0) .. (8,0)
        static readonly double[,] InacioBowl1 = { { 219.6, 220.6 }, { 609.1, 609.9 }, { 1135.9, 1139.7 }, { 1787.6, 1787.9 }, { 2555.2, 2564.8 }, { 3427.0, 3428.3 }, { 4376.3, 4389.4 } };
        static readonly double[,] InacioBowl2 = { { 310.2, 312.1 }, { 828.1, 828.8 }, { 1503.4, 1506.7 }, { 2328.1, 2340.1 }, { 3303.7, 3312.7 }, { 4413.2, 4416.4 }, { 5635.4, 5642.0 } };
        // [WSN02] Table 1: a 21 cm bowl struck with a wooden mallet
        static readonly double[] WsnHz = { 186, 551, 1007, 1627, 2337 };

        static void Pairs(double[,] rows, out double[] mean, out double[] split)
        {
            int n = rows.GetLength(0);
            mean = new double[n]; split = new double[n];
            for (int i = 0; i < n; i++) { double a = rows[i, 0], b = rows[i, 1]; mean[i] = (a + b) / 2; split[i] = Math.Abs(b - a); }
        }

        // Ratios to the (2,0) mean, extended to (9,0) with the local power law of the last two measured pairs (r ~ n^p).
        static double[] RatiosFrom(double[] means, int upTo = BowlsConst.NP)
        {
            var r = new double[upTo];
            int k = means.Length;
            for (int i = 0; i < k; i++) r[i] = means[i] / means[0];
            while (k < upTo)
            {
                int n1 = k, n2 = k + 1;
                double p = Math.Log(r[k - 1] / r[k - 2]) / Math.Log((double)n2 / n1);
                r[k] = r[k - 1] * Math.Pow((double)(n2 + 1) / n2, p);
                k++;
            }
            return r;
        }

        static double[] SplitsFrom(double[] mean, double[] split, int upTo = BowlsConst.NP, double fill = 0.002)
        {
            var s = new double[upTo];
            for (int i = 0; i < upTo; i++) s[i] = i < mean.Length ? split[i] / mean[i] : fill;
            return s;
        }

        // The deterministic "identity" of each bowl: pair orientations (the reference's seededAngles, uint32 arithmetic).
        static double[] SeededAngles(uint seed, int n)
        {
            uint s = seed;
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                unchecked { s = (s ^ (s >> 15)) * 0x2c1b3c6du + 0x9e3779b9u; }
                s ^= s >> 12;
                o[i] = s / 4294967296.0;
            }
            return o;
        }

        /// <summary>|F(f)| / |F(0)| of the unit's two-component strike (60 % over T, 40 % over 0.72 T), linear phases.</summary>
        public static double ContactSpectrum(double f, double T, double split = 0.6, double shortK = 0.72)
        {
            H(f, T, out double ar, out double ai);
            H(f, shortK * T, out double br, out double bi);
            double x = split * ar + (1 - split) * br, y = split * ai + (1 - split) * bi;
            return Hypot(x, y);
        }

        static void H(double f, double TT, out double re, out double im)
        {
            double x = 2 * f * TT;
            double a = Math.Abs(1 - x) < 1e-9 ? Math.PI / 4 : Math.Cos(Math.PI * f * TT) / (1 - x * x);
            re = a * Math.Cos(Math.PI * f * TT);
            im = -a * Math.Sin(Math.PI * f * TT);
        }

        // V8's Math.hypot (builtins/math.tq): scaled by the max, Kahan-compensated sum, sqrt(sum) * max.
        static double Hypot(double x, double y)
        {
            double ax = Math.Abs(x), ay = Math.Abs(y);
            double max = Math.Max(ax, ay);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            for (int k = 0; k < 2; k++)
            {
                double n = (k == 0 ? ax : ay) / max;
                double summand = n * n - comp;
                double prelim = sum + summand;
                comp = (prelim - sum) - summand;
                sum = prelim;
            }
            return Math.Sqrt(sum) * max;
        }

        /// <summary>The three bowls (bowls.md 2.2, D1/D2).</summary>
        public static BowlDesign[] Bowls(int sr = BowlsConst.SR)
        {
            Pairs(InacioBowl1, out double[] m1, out double[] s1);
            Pairs(InacioBowl2, out double[] m2, out double[] s2);
            var midSplits = new double[BowlsConst.NP];
            for (int i = 0; i < 7; i++) midSplits[i] = 0.5 * (s1[i] / m1[i] + s2[i] / m2[i]);
            midSplits[7] = 0.002;
            var b = new BowlDesign[3];
            b[0] = new BowlDesign { Index = 0, Key = "large", Label = "Big bowl", Note = "G3", Midi = 55, F0 = 195.998, RimDiam = 0.175, Height = 0.080, Wall = 0.0038, Mass = 0.80, Ratios = RatiosFrom(m1), Splits = SplitsFrom(m1, s1) };
            b[1] = new BowlDesign { Index = 1, Key = "medium", Label = "Middle bowl", Note = "C4", Midi = 60, F0 = 261.626, RimDiam = 0.150, Height = 0.068, Wall = 0.0034, Mass = 0.50, Ratios = RatiosFrom(WsnHz), Splits = midSplits };
            b[2] = new BowlDesign { Index = 2, Key = "small", Label = "Small bowl", Note = "E4", Midi = 64, F0 = 329.628, RimDiam = 0.125, Height = 0.056, Wall = 0.0031, Mass = 0.32, Ratios = RatiosFrom(m2), Splits = SplitsFrom(m2, s2) };
            for (int i = 0; i < 3; i++) Design(b[i], sr);
            return b;
        }

        static void Design(BowlDesign b, int sr)
        {
            double T = 1.0 / sr;
            b.M = BowlsConst.MASS_FRACTION * b.Mass;
            double[] orient = SeededAngles((uint)(0xb0e1 + 977 * b.Index), BowlsConst.NP);
            double w2 = 2 * Math.PI * b.F0;
            var radRaw = new double[BowlsConst.NP];
            for (int p = 0; p < BowlsConst.NP; p++)
            {
                double r = b.Ratios[p];
                radRaw[p] = Math.Pow(10, BowlsConst.RAD_DB[p] / 20) * (r * w2) / Math.Max(BowlsConst.MALLET_FLOOR, ContactSpectrum(r * b.F0, BowlsConst.MALLET_MS / 1000));
            }
            for (int p = 0; p < BowlsConst.NP; p++)
            {
                int n = p + 2;
                double r = b.Ratios[p], fm = r * b.F0, df = b.Splits[p] * fm;
                double zFree = Math.Min(BowlsConst.ZETA_MAX, BowlsConst.ZETA0 * Math.Pow(r, BowlsConst.ZETA_EXP));
                double zeta = zFree * BowlsConst.SUPPORT[p];
                double theta0 = orient[p] * Math.PI / n;
                for (int mbr = 0; mbr < 2; mbr++)
                {
                    double f = fm + (mbr != 0 ? 0.5 : -0.5) * df;
                    double w = 2 * Math.PI * f;
                    double alpha0 = zeta * w;
                    var md = new BowlMode { N = n, Member = mbr, F = f, W = w, Zeta = zeta, T60 = 6.907755 / alpha0, Theta0 = theta0, Rad = radRaw[p] / radRaw[0] };
                    for (int L = 0; L < BowlsConst.NPOLESET; L++)
                    {
                        double a = alpha0 + BowlsConst.TOUCH_ALPHA[L % BowlsConst.NLEVEL] * Math.Pow(f / b.F0, BowlsConst.TOUCH_EXP)
                                   + (L >= BowlsConst.NLEVEL && n >= 3 ? BowlsConst.CONTACT_ALPHA * Math.Pow(f / b.F0, BowlsConst.TOUCH_EXP) : 0);
                        double rr = Math.Exp(-a * T), ww = w * T;
                        md.PolePr[L] = (float)(rr * Math.Cos(ww));
                        md.PolePi[L] = (float)(rr * Math.Sin(ww));
                    }
                    md.Gi = (float)(T / (b.M * w));
                    md.Om = (float)w;
                    b.Modes[2 * p + mbr] = md;
                }
            }
            for (int p = 0; p < BowlsConst.NP; p++)
            {
                int n = p + 2;
                double th = b.Modes[2 * p].Theta0;
                b.Oc[p] = (float)Math.Cos(-n * th);
                b.Os[p] = (float)Math.Sin(-n * th);
                b.InvN[p] = (float)(1.0 / (p + 2));
            }
            double invN2 = 0;
            for (int p = 0; p < BowlsConst.NP; p++) invN2 += 1.0 / ((p + 2) * (p + 2));
            b.Y0t = (float)(T * invN2 / b.M);
            b.R = b.RimDiam / 2;
            b.OutScale = BowlsConst.OUT_GAIN * b.F0 / BowlsConst.F_REF;
        }

        /// <summary>RBJ biquad (the Dsp/Biquad.cs formulas), normalised [b0, b1, b2, a1, a2]. type: 0 high-pass, 1 band-pass, 2 low-pass.</summary>
        public static double[] Rbj(int type, double f, double Q, int sr)
        {
            double w = 2 * Math.PI * f / sr, cw = Math.Cos(w), sw = Math.Sin(w), al = sw / (2 * Q);
            double b0, b1, b2, a0, a1, a2;
            if (type == 0) { b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = (1 + cw) / 2; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; }
            else if (type == 1) { b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; }
            else { b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = (1 - cw) / 2; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; }
            return new[] { b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0 };
        }

        /// <summary>Lays the three designed bowls and the shared tables into the unit's state (main thread, before play).
        /// The state must be zeroed. seed: the session seed (the reference's seedBowl for each bowl).</summary>
        public static unsafe void Build(BowlsState* s, BowlDesign[] bowls, int sr, uint seed)
        {
            for (int k = 2; k <= BowlsConst.PulseKMax; k++)
            {
                s->PulseCos[k] = (float)Math.Cos(Math.PI / k);
                s->PulseSin[k] = (float)Math.Sin(Math.PI / k);
            }
            s->PickRc = (float)Math.Cos(BowlsConst.PICK_SLEW);
            s->PickRs = (float)Math.Sin(BowlsConst.PICK_SLEW);
            s->SampleRate = sr;
            double[] nb = Rbj(1, BowlsConst.NOISE_HZ, BowlsConst.NOISE_Q, sr);
            double[] lb = Rbj(2, BowlsConst.NOISE_LP, Math.Sqrt(0.5), sr);
            double[] hb = Rbj(0, BowlsConst.HP_HZ, Math.Sqrt(0.5), sr);
            for (int i = 0; i < 3; i++)
            {
                BowlState* b = BowlsState.Bowl(s, i);
                BowlDesign d = bowls[i];
                b->Index = i;
                b->T = (float)(1.0 / sr);
                b->Y0t = d.Y0t;
                b->Pc = 1; b->Ps = 0; b->Ptc = 1; b->Pts = 0; b->PickDirty = 1;
                b->Ec = 1; b->Es = 0; b->Tc = 1; b->Ts = 0; b->Trc = 1; b->Trs = 0; b->Hs = 1;
                b->Keep = BowlsConst.NG; b->FadeHi = BowlsConst.NG; b->Frames = 256; b->InvFrames = 1f / 256f;
                b->StLane = -1;
                b->NEnvK = (float)(1 - Math.Exp(-1 / (BowlsConst.NOISE_TAU * sr)));
                for (int m = 0; m < BowlsConst.NM; m++)
                {
                    BowlMode md = d.Modes[m];
                    int o = (m >> 2) * BowlState.STRIDE, l = m & 3;
                    b->G[o + BowlState.GI + l] = md.Gi;
                    b->G[o + BowlState.OM + l] = md.Om;
                    for (int L = 0; L < BowlsConst.NPOLESET; L++) { b->Poles[(L * BowlsConst.NM + m) * 2] = md.PolePr[L]; b->Poles[(L * BowlsConst.NM + m) * 2 + 1] = md.PolePi[L]; }
                    b->G[o + BowlState.PR + l] = md.PolePr[0];
                    b->G[o + BowlState.PI + l] = md.PolePi[0];
                    b->Rad[m] = (float)(md.Rad * d.OutScale);
                }
                for (int p = 0; p < BowlsConst.NP; p++) { b->Oc[p] = d.Oc[p]; b->Os[p] = d.Os[p]; b->InvN[p] = d.InvN[p]; }
                for (int k = 0; k < 5; k++) { b->Nb[k] = (float)nb[k]; b->Lb[k] = (float)lb[k]; b->Hb[k] = (float)hb[k]; }
                SeedBowl(b, seed);
            }
            s->Ready = 1;
        }

        /// <summary>The soft pad on outer contacts (the app's unit; the reference vectors run without it): the bowl feels the
        /// stick's force blended PAD_MIX toward N and low-passed at PAD_HZ, so a light, fast or grazing rub that makes the
        /// stick bounce off the wall rattles softly instead of crackling. Main thread, before the first render.</summary>
        public static unsafe void SetPad(BowlsState* s, int sr, bool on) => SetPad(s, sr, on ? BowlsConst.PAD_MIX : 0, on ? BowlsConst.PAD_HZ : 0, on ? BowlsConst.PAD_N : 0);

        /// <summary>SetPad with its values (hz 0: off).</summary>
        public static unsafe void SetPad(BowlsState* s, int sr, double mix, double hz, double pressN)
        {
            float k = hz > 0 ? (float)(1 - Math.Exp(-2 * Math.PI * hz / sr)) : 0f;
            for (int i = 0; i < BowlsConst.NBOWLS; i++)
            {
                BowlState* b = BowlsState.Bowl(s, i);
                b->PadK = k; b->PadMix = k != 0f ? (float)mix : 0f; b->PadN = k != 0f ? (float)pressN : 0f; b->PadF = 0f;
            }
        }

        /// <summary>The tonal rub noise (the app's unit; the reference vectors run without it). The reference's rub noise follows
        /// the friction power alone, so a rub that has not (or not yet) made the bowl sing is all hiss: under real hand input the
        /// band-passed noise ran 3 to 18 dB under the fundamental per 10 Hz bin, the "broken cassette" on the headset. On, the
        /// noise is scaled by RUB_NOISE_SCALE and capped at RUB_NOISE_REL x the bowl's own tone envelope, so it can only ever
        /// be a faint texture on the sing (at least 40 dB under the fundamental per bin). Main thread, before the first render.</summary>
        public static unsafe void SetRubNoise(BowlsState* s, int sr, bool on) => SetRubNoise(s, sr, on ? BowlsConst.RUB_NOISE_SCALE : 0, on ? BowlsConst.RUB_NOISE_REL : 0);

        /// <summary>SetRubNoise with its values (rel 0: off, the reference's noise).</summary>
        public static unsafe void SetRubNoise(BowlsState* s, int sr, double scale, double rel)
        {
            float k = (float)(1 - Math.Exp(-1 / (BowlsConst.TONE_TAU * sr)));
            for (int i = 0; i < BowlsConst.NBOWLS; i++)
            {
                BowlState* b = BowlsState.Bowl(s, i);
                b->NoiseRel = rel > 0 ? (float)rel : 0f; b->NoiseScale = rel > 0 ? (float)scale : 0f; b->ToneK = rel > 0 ? k : 0f; b->ToneEnv = 0f;
            }
        }

        /// <summary>seedBowl(): the bowl's rub-noise lane from SplitMix32 (seed + 0x51ed27 (index + 1)).</summary>
        public static unsafe void SeedBowl(BowlState* b, uint seed)
        {
            uint st;
            unchecked { st = seed + 0x51ed27u * (uint)(b->Index + 1); }
            uint v = SplitMix32Next(ref st);
            b->Nz = v == 0 ? 0x9e3779b9u : v;
        }

        /// <summary>splitmix32 (the burst lab's common.mjs): one step.</summary>
        public static uint SplitMix32Next(ref uint s)
        {
            unchecked
            {
                s += 0x9e3779b9u;
                uint z = s;
                z = (z ^ (z >> 16)) * 0x85ebca6bu;
                z = (z ^ (z >> 13)) * 0xc2b2ae35u;
                return z ^ (z >> 16);
            }
        }
    }
}
