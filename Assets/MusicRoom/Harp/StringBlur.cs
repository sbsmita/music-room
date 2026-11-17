// E8 (experience.md section 10): a plucked harp string vibrates at 131-1047 Hz, far above the 90 Hz display, so the eye
// sees the ENVELOPE of its motion, a soft spindle that narrows as the note dies. The modal sum of an ideal string plucked
// into a triangle of height h at p, y(u, phi) = sum_n c_n cos(n phi) sin(n pi u), b_n = 2 h sin(n pi p) / (n^2 pi^2 p (1 - p)),
// c_n = b_n e^(-t / tau_n) (Fletcher & Rossing ch. 2), sampled at the 17 rings of the string mesh over 24 phases:
// up_j = max y, lo_j = min y. At t = 0 this is d'Alembert's parallelogram (0,0) (p,h) (1,0) (1-p,-h): the upper envelope
// peaks at p, the lower one at 1 - p, so the blur is asymmetric like a real string's. tau_n is the decay of partial n of
// the harp sound that plays (IAudioEngine.HarpPartialTau), so the picture dies away exactly as the sound does.
// Exact savings (no approximation): cos(n(2 pi - phi)) = cos(n phi) leaves 13 distinct phases of the 24, and
// y(16 - j, phi + pi) = -y(j, phi) gives up(16 - j) = -lo(j): only rings 0 .. 8 are computed (936 multiply-adds a string).
// Reference: harpEnvelope / pluckCoeffs / blurAlpha in the experience reference (Docs/10-upgrade/experience.md). No allocation.
using UnityEngine;

namespace MusicRoom.Harp
{
    public static class HarpBlur
    {
        public const int Modes = 8, Phases = 24, Segments = HarpGeometry.Segments, Rings = Segments + 1;
        const int HalfPhases = Phases / 2 + 1, HalfRings = Segments / 2 + 1;
        /// <summary>The blur is complete (edges at full alpha) at a swing of 4 string radii.</summary>
        public const float BlurFullAt = 4f;
        public const float CoreAlphaDrop = 0.65f, EdgeAlpha = 0.45f;
        /// <summary>A string whose widest swing (up - lo) is under 0.05 mm is still: no more updates.</summary>
        public const float StopSwing = 5e-5f;
        /// <summary>Plucked more recently than this: the envelope is updated every frame; later every other frame.</summary>
        public const float FullRateFor = 0.1f;

        // S[j * Modes + n] = sin((n + 1) pi j / 16), C[k * Modes + n] = cos((n + 1) 2 pi k / 24): built once.
        static readonly float[] S = BuildS(), C = BuildC();
        static readonly float[] s_a = new float[Modes];   // main-thread scratch

        static float[] BuildS()
        {
            var s = new float[Rings * Modes];
            for (int j = 0; j < Rings; j++)
                for (int n = 1; n <= Modes; n++) s[j * Modes + n - 1] = (float)System.Math.Sin(n * System.Math.PI * j / Segments);
            return s;
        }

        static float[] BuildC()
        {
            var c = new float[Phases * Modes];
            for (int k = 0; k < Phases; k++)
                for (int n = 1; n <= Modes; n++) c[k * Modes + n - 1] = (float)System.Math.Cos(n * 2.0 * System.Math.PI * k / Phases);
            return c;
        }

        /// <summary>b_n of the plucked triangle (height h at p), n = 1 .. Modes, into b[0 .. Modes - 1].</summary>
        public static void PluckCoeffs(float p, float h, float[] b)
        {
            double pp = Mathf.Clamp(p, 0.01f, 0.99f);
            for (int n = 1; n <= Modes; n++)
                b[n - 1] = (float)(2.0 * h * System.Math.Sin(n * System.Math.PI * pp) / (n * n * System.Math.PI * System.Math.PI * pp * (1.0 - pp)));
        }

        /// <summary>Upper and lower envelopes at the 17 rings (j = 0 at the soundboard end, u = j / 16) at time t after the
        /// pluck: c_n = gain b_n e^(-t / tau_n). <paramref name="c"/> is scratch of Modes. Returns the widest swing
        /// max_j (up_j - lo_j).</summary>
        public static float Envelope(float[] b, float[] tau, float t, float gain, float[] c, float[] up, float[] lo)
        {
            for (int n = 0; n < Modes; n++) c[n] = gain * b[n] * Mathf.Exp(-t / Mathf.Max(tau[n], 1e-4f));
            float widest = 0f;
            float[] a = s_a;
            for (int j = 0; j < HalfRings; j++)
            {
                int sj = j * Modes;
                for (int n = 0; n < Modes; n++) a[n] = c[n] * S[sj + n];
                float mx = 0f, mn = 0f;
                for (int k = 0; k < HalfPhases; k++)
                {
                    int ck = k * Modes;
                    float y = a[0] * C[ck] + a[1] * C[ck + 1] + a[2] * C[ck + 2] + a[3] * C[ck + 3]
                            + a[4] * C[ck + 4] + a[5] * C[ck + 5] + a[6] * C[ck + 6] + a[7] * C[ck + 7];
                    if (y > mx) mx = y;
                    if (y < mn) mn = y;
                }
                up[j] = mx;
                lo[j] = mn;
                up[Segments - j] = -mn;   // y(16 - j, phi + pi) = -y(j, phi)
                lo[Segments - j] = -mx;
                if (mx - mn > widest) widest = mx - mn;
            }
            return widest;
        }

        /// <summary>Vertex alphas of a ring from its swing: b = clamp01((up - lo) / (4 r)); the core fades to 1 - .65 b,
        /// the edges rise to .45 b (a sinusoidally moving string spends most of its time near its extremes).</summary>
        public static void Alpha(float up, float lo, float radius, out float core, out float edge)
        {
            float b = Mathf.Clamp01((up - lo) / (BlurFullAt * radius));
            core = 1f - CoreAlphaDrop * b;
            edge = EdgeAlpha * b;
        }
    }
}
