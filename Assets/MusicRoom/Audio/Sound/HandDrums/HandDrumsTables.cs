// The hand drums' identity: the binary table written by Tools~/export-handdrums.mjs from the Node reference (buildDrum()
// for the conga and the tumba, every value at the precision the reference holds it), and the main-thread builder that
// lays it out as the unmanaged HandDrumsState (handdrums.md 2.2, 2.12). The app loads the exported table and never
// re-draws the dense field's random realization (handdrums.md 2.2 "Which random drum"). About 0.16 MB in Resources.
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Audio.Burst
{
    /// <summary>The parsed table: named float32 / float64 / int32 / uint32 arrays ("&lt;drum&gt;.&lt;field&gt;", "k.&lt;const&gt;", "part.&lt;name&gt;").</summary>
    public sealed class HandDrumsTables
    {
        public const string ResourceName = "MusicRoomHandDrums";
        readonly Dictionary<string, Array> _a = new Dictionary<string, Array>();
        public int Count => _a.Count;
        public IEnumerable<string> Names => _a.Keys;

        /// <summary>The shipped table (Resources, main thread).</summary>
        public static byte[] LoadDefaultBytes()
        {
            var t = Resources.Load<TextAsset>(ResourceName);
            if (t == null) throw new InvalidOperationException("hand drum table " + ResourceName + " not found in Resources");
            byte[] b = t.bytes;
            Resources.UnloadAsset(t);
            return b;
        }

        public static HandDrumsTables Parse(byte[] b)
        {
            if (b == null || b.Length < 8 || b[0] != 'H' || b[1] != 'D' || b[2] != 'T' || b[3] != '1') throw new FormatException("not an HDT1 hand drum table");
            var t = new HandDrumsTables();
            int count = BitConverter.ToInt32(b, 4), p = 8;
            for (int r = 0; r < count; r++)
            {
                int len = b[p++];
                string name = Encoding.ASCII.GetString(b, p, len); p += len;
                char type = (char)b[p++];
                int n = BitConverter.ToInt32(b, p); p += 4;
                Array a;
                switch (type)
                {
                    case 'f': { var v = new float[n]; for (int i = 0; i < n; i++, p += 4) v[i] = BitConverter.ToSingle(b, p); a = v; break; }
                    case 'd': { var v = new double[n]; for (int i = 0; i < n; i++, p += 8) v[i] = BitConverter.ToDouble(b, p); a = v; break; }
                    case 'i': { var v = new int[n]; for (int i = 0; i < n; i++, p += 4) v[i] = BitConverter.ToInt32(b, p); a = v; break; }
                    case 'u': { var v = new uint[n]; for (int i = 0; i < n; i++, p += 4) v[i] = BitConverter.ToUInt32(b, p); a = v; break; }
                    default: throw new FormatException("bad record type " + type);
                }
                t._a[name] = a;
            }
            return t;
        }

        public bool Has(string name) => _a.ContainsKey(name);
        public float[] F(string name) => Get<float[]>(name);
        public double[] D(string name) => Get<double[]>(name);
        public int[] I(string name) => Get<int[]>(name);
        public double D1(string name) => Get<double[]>(name)[0];
        public int I1(string name) => Get<int[]>(name)[0];
        public uint U1(string name) => Get<uint[]>(name)[0];

        T Get<T>(string name) where T : class
        {
            if (!_a.TryGetValue(name, out Array a)) throw new KeyNotFoundException("hand drum table: no " + name);
            return a as T ?? throw new FormatException("hand drum table: " + name + " has the wrong type");
        }
    }

    /// <summary>Builds the unmanaged unit state from a table. alloc returns zeroed, 64-byte aligned memory that outlives the
    /// state (AudioGraphHost.Alloc in the app, HandDrumsOffline's arena in phase-A tests).</summary>
    public static unsafe class HandDrumsBuilder
    {
        /// <summary>The per-drum RNG seed of the reference (HandDrums constructor): (seed * 2654435761 + 0x9e3779b9 (d + 1)) &gt;&gt;&gt; 0
        /// in JS double arithmetic.</summary>
        public static uint DrumSeed(uint seed, int d)
        {
            double x = seed * 2654435761.0 + 2654435769.0 * (d + 1);
            return (uint)(ulong)x;
        }

        public static HandDrumsState* Build(HandDrumsTables t, Func<long, IntPtr> alloc, uint seed = 1, int sampleRate = HandDrumsConst.Sr)
        {
            var s = (HandDrumsState*)alloc(sizeof(HandDrumsState));
            s->SampleRate = sampleRate;
            s->Share = 1f;
            s->MaskKey = -1;
            BuildDrum(t, "conga.", &s->D0, alloc, sampleRate, DrumSeed(seed, 0));
            BuildDrum(t, "tumba.", &s->D1, alloc, sampleRate, DrumSeed(seed, 1));
            s->GTot = s->D0.G + s->D1.G;
            s->Cap = s->GTot;
            System.Threading.Volatile.Write(ref s->Ready, 1);
            return s;
        }

        /// <summary>Bytes the two drums take (tables + state), for the Stats line.</summary>
        public static long Bytes(HandDrumsTables t)
        {
            long b = sizeof(HandDrumsState);
            foreach (var k in HandDrumsNames.Drums) b += DrumBytes(t.I1(k + ".nPad"), t.I1(k + ".G"), t.I(k + ".glideList").Length, t.I(k + ".shedOrder").Length);
            return b;
        }

        static long Align(long x) => (x + 63) & ~63L;

        static long DrumBytes(int nPad, int G, int nGlide, int nShed)
        {
            long b = 0;
            b += Align(G * sizeof(ModalGroup));
            b += Align(nPad * HandDrumsConst.ShapeN * 4) + Align(HandDrumsConst.Parts * nPad * 4);
            b += 10 * Align(nPad * 8);
            b += 3 * Align(nPad * 4) + Align(nGlide * 4) + Align(nShed * 4);
            b += Align(nPad) + Align(G);
            b += 3 * Align(G) + Align(G * 4);
            b += 3 * Align(nPad * 8);
            b += Align(HandDrumsConst.MaxFrames * 16);
            b += HandDrumsConst.Slots * (Align(nPad * 4) + 2 * Align(HandDrumsConst.MaxFrames * 4));
            return b;
        }

        static void BuildDrum(HandDrumsTables t, string k, HandDrumsDrum* d, Func<long, IntPtr> alloc, int sr, uint rngSeed)
        {
            int n = t.I1(k + "n"), nPad = t.I1(k + "nPad"), G = t.I1(k + "G");
            int[] glide = t.I(k + "glideList"), shed = t.I(k + "shedOrder");
            if (nPad != 4 * G || n > nPad) throw new FormatException(k + " bad mode counts");
            if (t.I1(k + "gCav") != G - 1) throw new FormatException(k + " the cavity group must be the last group (the render drives it last)");
            byte* mem = (byte*)alloc(DrumBytes(nPad, G, glide.Length, shed.Length));
            byte* p = mem;

            d->N = n; d->NPad = nPad; d->G = G; d->GCav = t.I1(k + "gCav");
            d->A = t.D1(k + "a"); d->T = t.D1(k + "T"); d->Sigma = t.D1(k + "sigma");
            d->Kappa = t.D1(k + "kappa"); d->GlideMax = t.D1(k + "glideMax");
            // Drum constructor: outGain = level * 10^(OUTPUT_TRIM_DB / 20) (exported, so no pow difference reaches go)
            d->OutGain = t.D1(k + "outGain");
            d->Bank = Take<ModalGroup>(ref p, G);
            d->Shape = Take<float>(ref p, nPad * HandDrumsConst.ShapeN); Copy(t.F(k + "shape"), d->Shape, nPad * HandDrumsConst.ShapeN);
            d->AvgTab = Take<float>(ref p, HandDrumsConst.Parts * nPad); Copy(t.F(k + "avgTab"), d->AvgTab, HandDrumsConst.Parts * nPad);
            d->R0 = Take<double>(ref p, nPad); Copy(t.D(k + "r0"), d->R0, nPad);
            d->W0 = Take<double>(ref p, nPad); Copy(t.D(k + "w0"), d->W0, nPad);
            d->Cr0 = Take<double>(ref p, nPad); Copy(t.D(k + "cr0"), d->Cr0, nPad);
            d->Ci0 = Take<double>(ref p, nPad); Copy(t.D(k + "ci0"), d->Ci0, nPad);
            d->Mk = Take<double>(ref p, nPad); Copy(t.D(k + "Mk"), d->Mk, nPad);
            d->Om = Take<double>(ref p, nPad); Copy(t.D(k + "om"), d->Om, nPad);
            d->Kap = Take<double>(ref p, nPad); Copy(t.D(k + "kap"), d->Kap, nPad);
            d->EW = Take<double>(ref p, nPad); Copy(t.D(k + "eW"), d->EW, nPad);
            d->CPh = Take<double>(ref p, nPad); Copy(t.D(k + "cPh"), d->CPh, nPad);
            d->SPh = Take<double>(ref p, nPad); Copy(t.D(k + "sPh"), d->SPh, nPad);
            d->Kind = Take<int>(ref p, nPad); Copy(t.I(k + "kind"), d->Kind, nPad);
            d->Mm = Take<int>(ref p, nPad); Copy(t.I(k + "mm"), d->Mm, nPad);
            d->Orient = Take<int>(ref p, nPad); Copy(t.I(k + "orient"), d->Orient, nPad);
            d->GlideList = Take<int>(ref p, glide.Length); Copy(glide, d->GlideList, glide.Length); d->GlideCount = glide.Length;
            d->ShedOrder = Take<int>(ref p, shed.Length); Copy(shed, d->ShedOrder, shed.Length); d->ShedCount = shed.Length;
            d->GlideOn = Take<byte>(ref p, nPad); { int[] g = t.I(k + "glideOn"); for (int i = 0; i < nPad; i++) d->GlideOn[i] = (byte)g[i]; }
            d->GClass = Take<byte>(ref p, G); { int[] g = t.I(k + "gClass"); for (int i = 0; i < G; i++) d->GClass[i] = (byte)g[i]; }
            d->Act = Take<byte>(ref p, G); d->Allowed = Take<byte>(ref p, G); d->Fading = Take<byte>(ref p, G);
            d->ActList = Take<int>(ref p, G);
            d->DMul0 = Take<double>(ref p, nPad); d->DMul1 = Take<double>(ref p, nPad); d->DAl = Take<double>(ref p, nPad);
            d->Acc = Take<float4>(ref p, HandDrumsConst.MaxFrames);
            HandDrumsPatch* P = &d->P0;
            for (int s = 0; s < HandDrumsConst.Slots; s++)
            {
                P[s].Gi = Take<float>(ref p, nPad);
                P[s].DF = Take<float>(ref p, HandDrumsConst.MaxFrames);
                P[s].Pop = Take<float>(ref p, HandDrumsConst.MaxFrames);
            }
            if (p - mem > DrumBytes(nPad, G, glide.Length, shed.Length)) throw new InvalidOperationException("hand drum layout overflow");

            // Drum constructor: output gains go = f32(go * outGain) (0 for padding), rests none, poles from r0 / w0
            float[] go = t.F(k + "goBank");
            for (int q = 0; q < nPad; q++)
            {
                float* b = (float*)(d->Bank + (q >> 2));
                b[16 + (q & 3)] = q < n ? go[q] : 0f;
                d->DMul0[q] = d->DMul1[q] = 1;
            }
            for (int g = 0; g < G; g++) d->Allowed[g] = 1;
            d->FadeMul = 1; d->S = 1; d->CavMul = 1; d->PolesDirty = 1; d->Idle = 1;
            d->Rng = rngSeed;
            HandDrumsDsp.UpdatePoles(d, true);
        }

        static T* Take<T>(ref byte* p, long count) where T : unmanaged { T* r = (T*)p; p += Align(count * sizeof(T)); return r; }

        static void Copy(float[] src, float* dst, int n) { for (int i = 0; i < n; i++) dst[i] = src[i]; }
        static void Copy(double[] src, double* dst, int n) { for (int i = 0; i < n; i++) dst[i] = src[i]; }
        static void Copy(int[] src, int* dst, int n) { for (int i = 0; i < n; i++) dst[i] = src[i]; }
    }
}
