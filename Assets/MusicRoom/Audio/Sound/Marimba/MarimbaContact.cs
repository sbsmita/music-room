// The marimba's main-thread physics (spec 2.4, 2.5; marimba-engine.mjs contactSolve / dampSolve), double precision,
// operation for operation as the reference so the float32 records match it (to the last bit wherever the platform's
// libm agrees with V8's for pow / cos; otherwise within 1e-6 relative: review item 16).
//   Solve: the yarn mallet (or a fingertip pad) against the bar's modes, Hunt-Crossley contact F = K d^p (1 + mu d'),
//          kick-drift-kick at OS x 48 kHz for at most 12 ms. Writes the radiated sound during the contact, the knock,
//          and the complex modal states at the handoff (the coupled fundamental goes to the e+ / e- lanes).
//   DampPoles: the 8 lane poles with the added decay of a resting hand or a held mallet.
// Preallocated scratch only: no allocation per call (spec 9.1 "Allocation").
using System;

namespace MusicRoom.Audio.Burst
{
    /// <summary>Diagnostics of the last solve (tests, the perf report).</summary>
    public struct MarimbaStrikeStats
    {
        public double ImpactSpeed, ContactMs, Fmax;
        public int Os, Samples;
    }

    public sealed unsafe class MarimbaContact
    {
        const int L = MarimbaConst.Lanes, NM = MarimbaTables.ContactModes, NN = MarimbaTables.ShapeNodes;
        readonly double[] _ph = new double[8], _qr = new double[8], _qi = new double[8], _sRe = new double[8], _sIm = new double[8];
        readonly double[] _pRe = new double[8], _pIm = new double[8], _cOut = new double[8];
        readonly int[] _idx = new int[8];
        readonly double[] _force = new double[MarimbaConst.SegMax];

        public MarimbaStrikeStats Last;

        // ------------------------------------------------------------------ shapes and speeds

        /// <summary>fe-bar.mjs shapeAt: linear interpolation of the 73 nodal values.</summary>
        static double ShapeAt(double[] w, int m, double xi)
        {
            int ne = NN - 1, o = m * NN;
            double t = Math.Min(Math.Max(xi, 0), 1) * ne;
            int i = Math.Min(ne - 1, (int)Math.Floor(t));
            double f = t - i;
            return w[o + i] * (1 - f) + w[o + i + 1] * f;
        }

        /// <summary>design.mjs bendShape: mass-normalised psi_m(xi) of bar i, the lo and hi profiles blended by register.</summary>
        public static double BendShape(int bar, int m, double xi)
        {
            double a = ShapeAt(MarimbaTables.ShapeLo, m, xi), b = ShapeAt(MarimbaTables.ShapeHi, m, xi);
            return a + (b - a) * MarimbaTables.HiBlend[bar];
        }

        /// <summary>Impact speed from vel (m/s): v0 + (v1 - v0) clamp(vel, .05, 1)^gamma.</summary>
        public static double ImpactSpeed(int mallet, float vel)
        {
            double v = Math.Min(1, Math.Max(0.05, (double)vel));
            return MarimbaTables.MalletV0[mallet] + (MarimbaTables.MalletV1[mallet] - MarimbaTables.MalletV0[mallet]) * Math.Pow(v, MarimbaTables.MalletGamma[mallet]);
        }

        /// <summary>The knock seed of a strike: marimba-engine.mjs seedOf(unitSeed, counter).</summary>
        public static uint SeedOf(uint unitSeed, int counter)
        {
            unchecked
            {
                uint z = unitSeed + (uint)(counter + 1) * 0x9e3779b9u;
                z = (z ^ (z >> 16)) * 0x85ebca6bu;
                z = (z ^ (z >> 13)) * 0xc2b2ae35u;
                z ^= z >> 16;
                return z != 0 ? z : 1u;
            }
        }

        // ------------------------------------------------------------------ the strike

        /// <summary>contactSolve: fills rec for bar i at vel, u (along, 0..1), c (across, -1..1) with mallet kind and the
        /// knock seed (the reference's `seed` argument; the unit passes SeedOf(unitSeed, counter)).</summary>
        public void Solve(MarimbaStrikeRecord* rec, int bar, float vel, double u, double c, MarimbaMallet mallet, uint seed)
        {
            const int sr = MarimbaTables.SampleRate;
            int mk = (int)mallet;
            bool finger = mallet == MarimbaMallet.Finger;
            double G = MarimbaTables.BarGain[bar] * (finger ? MarimbaTables.FingerGain[bar] : 1);
            u = Math.Min(1, Math.Max(0, u));
            double cx = Math.Min(1, Math.Max(-1, c));
            double v0 = ImpactSpeed(mk, vel);
            int OS = MarimbaTables.MalletOs[mk];
            double dtf = 1.0 / (OS * sr);
            double[] ph = _ph, Qr = _qr, Qi = _qi, sRe = _sRe, sIm = _sIm, pRe = _pRe, pIm = _pIm, cOut = _cOut;
            int[] idx = _idx;
            int nl = 0;
            double S2 = 0, sqrtMass = Math.Sqrt(MarimbaTables.Mass[bar]);
            for (int m = 0; m < NM; m++)
            {
                int k = bar * NM + m;
                double f;
                if (!MarimbaTables.CmLive[k]) f = 0;
                else if (MarimbaTables.CmBend[k] >= 0) f = BendShape(bar, MarimbaTables.CmBend[k], u) / sqrtMass;
                else f = (cx * MarimbaTables.Width[bar] / 2) * Math.Cos(MarimbaTables.CmTors[k] * Math.PI * u) * MarimbaTables.CmNorm[k];
                if (f == 0) continue;
                idx[nl] = m; ph[nl] = f; Qr[nl] = 0; Qi[nl] = 0;
                sRe[nl] = MarimbaTables.CmSRe[k]; sIm[nl] = MarimbaTables.CmSIm[k];
                pRe[nl] = MarimbaTables.CmPropRe[k * MarimbaTables.MalletKinds + mk]; pIm[nl] = MarimbaTables.CmPropIm[k * MarimbaTables.MalletKinds + mk];
                cOut[nl] = MarimbaTables.CmC[k];
                S2 += f * f; nl++;
            }
            double K = MarimbaTables.MalletK[mk], p = MarimbaTables.MalletP[mk], mu = MarimbaTables.MalletMu[mk], mass = MarimbaTables.MalletMass[mk];
            double hm = 0.5 * dtf / mass, cHalf = 0.5 * dtf * (1 / mass + S2);
            double y = 0, yd = v0, Fmax = 0, F = 0;
            int contactEnd = -1, sep = 0, n = 0;
            bool started = false;
            int maxN = (int)Math.Floor(MarimbaConst.MaxContactS * sr + 0.5);
            float* outp = rec->Seg;
            double[] force = _force;
            // kick-drift-kick: half kick with the force at the step's start, the exact free drift of the mallet and of
            // every mode, the new force (Hunt-Crossley at the end-of-step velocity), the other half kick
            for (n = 0; n < maxN && contactEnd < 0; n++)
            {
                double fsum = 0, del = 0;
                for (int j = 0; j < OS; j++)
                {
                    double h0 = F * 0.5 * dtf;
                    yd -= F * hm; y += yd * dtf;
                    double w = 0, wd = 0;
                    for (int k = 0; k < nl; k++)
                    {
                        double r = Qr[k], q = Qi[k] - ph[k] * h0 / sIm[k];
                        double nr = r * pRe[k] - q * pIm[k], ni = r * pIm[k] + q * pRe[k];
                        Qr[k] = nr; Qi[k] = ni; w += ph[k] * nr; wd += ph[k] * (sRe[k] * nr - sIm[k] * ni);
                    }
                    del = y - w;
                    double Fn = 0;
                    if (del > 0)
                    {
                        double A = K * Math.Pow(del, p);
                        Fn = A * (1 + mu * (yd - wd)) / (1 + A * mu * cHalf);
                        if (Fn < 0) Fn = 0;
                        started = true;
                    }
                    if (Fn != 0)
                    {
                        double h1 = Fn * 0.5 * dtf;
                        yd -= Fn * hm;
                        for (int k = 0; k < nl; k++) Qi[k] -= ph[k] * h1 / sIm[k];
                    }
                    if (Fn > Fmax) Fmax = Fn;
                    fsum += 0.5 * (F + Fn); F = Fn;
                    if (started && Fn == 0 && del < 0) sep++; else sep = 0;
                }
                force[n] = fsum / OS;
                double pr = 0;
                for (int k = 0; k < nl; k++) pr += cOut[k] * (sRe[k] * Qr[k] - sIm[k] * Qi[k]);
                outp[n] = (float)(G * pr);
                if (sep >= OS) contactEnd = n + 1;   // off the bar for a whole output sample
            }
            if (contactEnd < 0) contactEnd = n;
            int nh = contactEnd + 1;   // the audio thread's modes produce sample nh onwards
            // one more free output sample (nh - 1), then the injection z = j c V (V = s Q, the analytic modal velocity)
            for (int j = 0; j < OS; j++)
                for (int k = 0; k < nl; k++) { double r = Qr[k], q = Qi[k]; Qr[k] = r * pRe[k] - q * pIm[k]; Qi[k] = r * pIm[k] + q * pRe[k]; }
            {
                double pr = 0;
                for (int k = 0; k < nl; k++) pr += cOut[k] * (sRe[k] * Qr[k] - sIm[k] * Qi[k]);
                outp[nh - 1] = (float)(G * pr);
            }
            for (int l = 0; l < L; l++) { rec->InjR[l] = 0; rec->InjI[l] = 0; }
            for (int k = 0; k < nl; k++)
            {
                int m = idx[k], km = bar * NM + m;
                double Vr = sRe[k] * Qr[k] - sIm[k] * Qi[k], Vi = sRe[k] * Qi[k] + sIm[k] * Qr[k];
                int lane = MarimbaTables.CmLane[km];
                if (lane < 0)
                {
                    for (int e = 0; e < 2; e++)
                    {
                        double kr = MarimbaTables.PairKRe[bar * 2 + e], ki = MarimbaTables.PairKIm[bar * 2 + e];
                        double wr = kr * Vr - ki * Vi, wi = kr * Vi + ki * Vr;   // K V
                        // C.scale(C.mul(j, K V), G), written as the reference evaluates it (signed zeros included)
                        rec->InjR[e] = (float)((0 * wr - 1 * wi) * G); rec->InjI[e] = (float)((0 * wi + 1 * wr) * G);
                    }
                }
                else
                {
                    double sc = MarimbaTables.CmC[km] * G;
                    rec->InjR[lane] = (float)((0 * Vr - 1 * Vi) * sc); rec->InjI[lane] = (float)((0 * Vi + 1 * Vr) * sc);
                }
            }
            // the wood knock: seeded noise x force envelope (then a 1.5 ms tail) through a band-pass
            int len = Math.Min(MarimbaConst.SegMax, nh + MarimbaConst.KnockTail);
            for (int q = nh; q < MarimbaConst.SegMax; q++) outp[q] = 0;
            double knockK = MarimbaTables.MalletKnock[mk];
            if (knockK > 0 && Fmax > 0)
            {
                uint st = SeedOf(seed, 0);
                double b0 = MarimbaTables.KnockB0[bar], b2 = MarimbaTables.KnockB2[bar], a1 = MarimbaTables.KnockA1[bar], a2 = MarimbaTables.KnockA2[bar];
                double lvl = MarimbaConst.KnockLevel * G * knockK * Math.Sqrt(Fmax / 20) * 0.05, dec = MarimbaTables.KnockDecay;
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, env = 0;
                for (int q = 0; q < len; q++)
                {
                    unchecked { st ^= st << 13; st ^= st >> 17; st ^= st << 5; }
                    double nz = (st / 4294967296.0) * 2 - 1;
                    double e = q < contactEnd ? force[q] / Fmax : env * dec; env = e;
                    double xin = nz * e;
                    double yv = b0 * xin + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = xin; y2 = y1; y1 = yv;
                    outp[q] = (float)(outp[q] + lvl * yv);
                }
            }
            rec->Bar = bar; rec->Len = len; rec->Nh = nh;
            Last = new MarimbaStrikeStats { ImpactSpeed = v0, ContactMs = contactEnd * 1000.0 / sr, Fmax = Fmax, Os = OS, Samples = n };
        }

        // ------------------------------------------------------------------ damping

        /// <summary>The mean psi_m^2 over the hand span u +- 0.12 (16 points), as marimba-engine.mjs handMean.</summary>
        public static double HandMean(int bar, int m, double u)
        {
            double s = 0;
            const int N = 16;
            for (int k = 0; k < N; k++)
            {
                double x = u - MarimbaConst.HandSpan + 2 * MarimbaConst.HandSpan * (k + 0.5) / N;
                double v = BendShape(bar, m, Math.Min(1, Math.Max(0, x)));
                s += v * v;
            }
            return s / N;
        }

        static readonly int[] LaneBend = { 0, 0, 1, 2, 3 };

        /// <summary>dampSolve: the bar's 8 poles with the added decay of a hand (spread over u +- 0.12) or a held mallet at u.</summary>
        public static void DampPoles(MarimbaDampRecord* rec, int bar, MarimbaDampKind kind, double u, double c)
        {
            const double sr = MarimbaTables.SampleRate;
            bool malletK = kind == MarimbaDampKind.Mallet;
            double A = malletK ? MarimbaConst.MalletRestAlpha : MarimbaConst.HandAlpha;
            for (int l = 0; l < L; l++)
            {
                int k = bar * L + l;
                double add;
                if (l <= 4)
                {
                    int m = LaneBend[l];
                    double shapeMean;
                    if (malletK) { double v = BendShape(bar, m, u); shapeMean = v * v / MarimbaTables.MaxPsi2[bar * 4 + m]; }
                    else shapeMean = HandMean(bar, m, u) / MarimbaTables.MaxHand2[bar * 4 + m];
                    add = A * shapeMean * (l <= 1 ? MarimbaTables.LanePartBar[k] : 1);
                }
                else add = A * (!malletK ? 1.0 : (c * c + 0.25));
                double alpha = MarimbaTables.LaneAlpha[k] + add, w = MarimbaTables.LaneOmega[k], r = Math.Exp(-alpha / sr), th = w / sr;
                rec->Pr[l] = (float)(r * Math.Cos(th));
                rec->Pi[l] = (float)(r * Math.Sin(th));
            }
            rec->Bar = bar;
        }
    }
}
