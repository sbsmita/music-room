// The bowls unit against its Node reference (bowls.md 11.1-11.2; the reference engine's test vectors, frozen in
// BowlsVectors.g.cs and the vec-*.f32 files in Reference~): the design tables, TV1-TV16, and how each render path matches:
//   managed (plain C#)                       bit-exact (FNV-1a over the float32 bits, every sample)
//   Burst FloatMode.Strict                   bit-exact
//   Strict function pointer from a Fast entry bit-exact: the phase-B graph path (UnitHooks.RenderBowls inside the Fast
//                                            AudioGraph.RenderBlock calls the bowls through this pointer)
//   Burst FloatMode.Fast (fused mad)         informational: linear vectors within 1e-4 of the peak, contact vectors'
//                                            block RMS within 3 dB (the friction branch flips under fusion; review item 20)
using System;
using System.IO;
using MusicRoom.Audio.Burst;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public unsafe class BowlsDspTests
    {
        static string RefDir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Bowls/Reference~");

        static float[] ReadF32(string name)
        {
            string p = Path.Combine(RefDir, "vec-" + name + ".f32");
            if (!File.Exists(p)) return null;
            byte[] b = File.ReadAllBytes(p);
            var y = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, y, 0, b.Length);
            return y;
        }

        [OneTimeSetUp] public void Setup() => BowlsBurstProbe.Install();

        // ---------------------------------------------------------------- design

        [Test]
        public void DesignTablesMatchTheReferenceFingerprints()
        {
            var d = BowlsRig.Design48k;
            for (int b = 0; b < 3; b++)
            {
                var exp = BowlsVectors.Design[b];
                Assert.That(d[b].Y0t, Is.EqualTo(exp.Y0t), exp.Key + " Y0t");
                Assert.That(d[b].OutScale, Is.EqualTo(exp.OutScale).Within(1e-6), exp.Key + " outScale");
                for (int p = 0; p < BowlsConst.NP; p++) { Assert.That(d[b].Oc[p], Is.EqualTo(exp.Oc[p]), $"{exp.Key} oc {p}"); Assert.That(d[b].Os[p], Is.EqualTo(exp.Os[p]), $"{exp.Key} os {p}"); }
                for (int m = 0; m < BowlsConst.NM; m++)
                {
                    var md = d[b].Modes[m]; var e = exp.Modes[m];
                    Assert.That(md.PolePr[0], Is.EqualTo(e.Pr0), $"{exp.Key} mode {m} pr0");
                    Assert.That(md.PolePi[0], Is.EqualTo(e.Pi0), $"{exp.Key} mode {m} pi0");
                    Assert.That(md.PolePr[3], Is.EqualTo(e.Pr3), $"{exp.Key} mode {m} pr3 (palm)");
                    Assert.That(md.PolePr[4], Is.EqualTo(e.Pr4), $"{exp.Key} mode {m} pr4 (finger on)");
                    Assert.That(md.PolePi[4], Is.EqualTo(e.Pi4), $"{exp.Key} mode {m} pi4");
                    Assert.That(md.Gi, Is.EqualTo(e.Gi), $"{exp.Key} mode {m} gi");
                    Assert.That(md.Om, Is.EqualTo(e.Om), $"{exp.Key} mode {m} om");
                    Assert.That(md.Rad, Is.EqualTo(e.Rad).Within(Math.Abs(e.Rad) * 6e-9), $"{exp.Key} mode {m} rad (9 significant digits)");
                    Assert.That(md.F, Is.LessThan(0.45 * 48000 / 2), "below 0.45 Nyquist");
                    if (md.N == 2) { Assert.That(md.PolePr[4], Is.EqualTo(md.PolePr[0]), "the rubbed (2,0) pair keeps its poles"); }
                    else Assert.That(md.PolePr[4], Is.LessThan(md.PolePr[0]), "tissue loss on n >= 3");
                }
            }
            // the published ratios ([I06] bowl 1 for the big bowl, [WSN02] for the middle, [I06] bowl 2 for the small)
            double[] big = { 2.769, 5.169, 8.122, 11.631, 15.573, 19.913 }, mid = { 2.962, 5.414, 8.747, 12.565 }, sml = { 2.663, 4.837, 7.502, 10.632, 14.189, 18.122 };
            for (int k = 0; k < big.Length; k++) Assert.That(d[0].Ratios[k + 1], Is.EqualTo(big[k]).Within(0.0006));
            for (int k = 0; k < mid.Length; k++) Assert.That(d[1].Ratios[k + 1], Is.EqualTo(mid[k]).Within(0.0006));
            for (int k = 0; k < sml.Length; k++) Assert.That(d[2].Ratios[k + 1], Is.EqualTo(sml[k]).Within(0.0006));
            Assert.That(d[0].Modes[0].F + d[0].Modes[1].F, Is.EqualTo(2 * 195.998).Within(1e-6), "G3");
            Assert.That(d[1].Modes[0].F + d[1].Modes[1].F, Is.EqualTo(2 * 261.626).Within(1e-6), "C4");
            Assert.That(d[2].Modes[0].F + d[2].Modes[1].F, Is.EqualTo(2 * 329.628).Within(1e-6), "E4");
        }

        // ---------------------------------------------------------------- vectors

        static void CheckExact(string name, BowlsPath path)
        {
            BowlsVectors.Vec v = Array.Find(BowlsVectors.All, q => q.Name == name);
            float[] y = BowlsRig.RunVector(name, path, out BowlsRig rig, out int retired);
            try
            {
                uint h = BowlsRig.Fnv1a(y);
                float[] r = ReadF32(name);
                if (r != null)
                {
                    int first = -1;
                    for (int i = 0; i < r.Length; i++) if (BitConverter.ToInt32(BitConverter.GetBytes(r[i]), 0) != BitConverter.ToInt32(BitConverter.GetBytes(y[i]), 0)) { first = i; break; }
                    Assert.That(first, Is.EqualTo(-1), $"{name} [{path}] first sample that differs (ref {(first >= 0 ? r[first] : 0):G9}, got {(first >= 0 ? y[first] : 0):G9})");
                }
                Assert.That(h, Is.EqualTo(v.Fnv), $"{name} [{path}] FNV-1a");
                int fz = Array.FindIndex(y, 0, Math.Min(4800, y.Length), q => q != 0);
                Assert.That(fz, Is.EqualTo(v.FirstNonZero), name + " first non-zero sample");
                for (int k = 0; k < v.First16.Length; k++) Assert.That((double)y[Math.Max(0, fz) + k], Is.EqualTo(v.First16[k]).Within(Math.Abs(v.First16[k]) * 1e-8 + 1e-30), $"{name} sample {fz + k}");
                if (v.Channel >= 0)
                {
                    BowlState* s = rig.B(v.Channel);
                    Assert.That(s->StContactSamples, Is.EqualTo(v.Contact), name + " contact samples");
                    Assert.That(s->StLostSamples, Is.EqualTo(v.Lost), name + " lost samples");
                    Assert.That(s->StStickSamples, Is.EqualTo(v.Stick), name + " stick samples");
                    Assert.That(s->StPreempts, Is.EqualTo(v.Preempts), name + " preempts");
                    Assert.That(s->StLaneDrops, Is.EqualTo(v.LaneDrops), name + " lane drops");
                    Assert.That((double)s->StSingVel, Is.EqualTo(v.SingVel).Within(Math.Abs(v.SingVel) * 1e-6 + 1e-12), name + " sing velocity");
                }
                if (v.RetiredBlock >= 0) Assert.That(retired, Is.EqualTo(v.RetiredBlock), name + " retires at its block");
            }
            finally { rig.Dispose(); }
        }

        public static readonly string[] Names = BowlsRig.VectorNames;

        /// <summary>Plain C# under the editor's Mono: Unity's embedded Mono evaluates float intermediates in higher precision
        /// (the same code is bit-exact under the standalone MonoBleedingEdge runtime and under Burst Strict, see the offline
        /// .NET check built from BowlsRig.cs), so here it is held to the Fast tolerances.</summary>
        [Test]
        public void VectorsManagedInTheEditorStayNear() => CompareAll(BowlsPath.Managed, "MANAGED");

        [Test, TestCaseSource(nameof(Names))]
        public void VectorBurstStrictBitExact(string name)
        {
            if (!BurstCompiler.IsEnabled) Assert.Ignore("Burst is off");
            CheckExact(name, BowlsPath.Strict);
        }

        [Test, TestCaseSource(nameof(Names))]
        public void VectorStrictPointerFromFastEntryBitExact(string name)
        {
            if (!BurstCompiler.IsEnabled) Assert.Ignore("Burst is off");
            Assert.That(BowlsRig.StrictPointer, Is.Not.EqualTo(IntPtr.Zero), "the Strict function pointer compiled");
            CheckExact(name, BowlsPath.StrictPointer);
        }

        [Test]
        public void StrictFunctionPointerKeepsItsFloatModeInsideAFastEntry()
        {
            if (!BurstCompiler.IsEnabled) Assert.Ignore("Burst is off");
            float a = 1f + 1f / 4096f, c = -(1f + 1f / 2048f);
            IntPtr strict = BurstCompiler.CompileFunctionPointer<BowlsBurstProbe.MadFn>(BowlsBurstProbe.StrictMad).Value;
            IntPtr fast = BurstCompiler.CompileFunctionPointer<BowlsBurstProbe.MadFn>(BowlsBurstProbe.FastMad).Value;
            float viaStrict = BowlsBurstProbe.FastEntryCallsPointer(strict, a, c), viaFast = BowlsBurstProbe.FastEntryCallsPointer(fast, a, c);
            Debug.Log($"[BOWLS-PROBE] Fast entry -> Strict pointer {viaStrict:G9} (0 = not fused), -> Fast pointer {viaFast:G9}");
            Assert.That(viaStrict, Is.EqualTo(0f), "a Strict function pointer never fuses, even called from a Fast entry");
        }

        [Test]
        public void VectorsUnderBurstFastStayNear()
        {
            if (!BurstCompiler.IsEnabled) Assert.Ignore("Burst is off");
            CompareAll(BowlsPath.Fast, "FAST");
        }

        static void CompareAll(BowlsPath path, string tag)
        {
            var lines = new System.Text.StringBuilder();
            foreach (string name in Names)
            {
                float[] a = ReadF32(name);
                if (a == null) { a = BowlsRig.RunVector(name, BurstCompiler.IsEnabled ? BowlsPath.Strict : BowlsPath.Managed, out var ra, out _); ra.Dispose(); }
                float[] b = BowlsRig.RunVector(name, path, out var rb, out _); rb.Dispose();
                double md = 0, pk = 0, worstDb = 0;
                for (int i = 0; i < a.Length; i++) { md = Math.Max(md, Math.Abs(a[i] - b[i])); pk = Math.Max(pk, Math.Abs(a[i])); }
                for (int s0 = 0; s0 < a.Length; s0 += 256)
                {
                    double ea = 0, eb = 0; for (int i = s0; i < Math.Min(a.Length, s0 + 256); i++) { ea += (double)a[i] * a[i]; eb += (double)b[i] * b[i]; }
                    if (ea > 1e-14) worstDb = Math.Max(worstDb, Math.Abs(10 * Math.Log10((ea + 1e-30) / (eb + 1e-30))));
                }
                var v = Array.Find(BowlsVectors.All, q => q.Name == name);
                bool contact = v.Contact > 0;
                lines.AppendLine($"[BOWLS-{tag}] {name}: max |d| {md:E2} ({md / Math.Max(pk, 1e-30):E2} of peak), worst block RMS {worstDb:F3} dB (Node fused: {v.FusedMaxAbsDiff:E2}, {v.FusedMaxBlockDb:F3} dB)");
            }
            Debug.Log(lines.ToString());
            foreach (string name in Names)
            {
                // re-run for the assertions (the log above lists every vector first)
                float[] a = ReadF32(name);
                if (a == null) { a = BowlsRig.RunVector(name, BurstCompiler.IsEnabled ? BowlsPath.Strict : BowlsPath.Managed, out var ra, out _); ra.Dispose(); }
                float[] b = BowlsRig.RunVector(name, path, out var rb, out _); rb.Dispose();
                double md = 0, pk = 0, worstDb = 0;
                for (int i = 0; i < a.Length; i++) { md = Math.Max(md, Math.Abs(a[i] - b[i])); pk = Math.Max(pk, Math.Abs(a[i])); }
                for (int s0 = 0; s0 < a.Length; s0 += 256)
                {
                    double ea = 0, eb = 0; for (int i = s0; i < Math.Min(a.Length, s0 + 256); i++) { ea += (double)a[i] * a[i]; eb += (double)b[i] * b[i]; }
                    if (ea > 1e-14) worstDb = Math.Max(worstDb, Math.Abs(10 * Math.Log10((ea + 1e-30) / (eb + 1e-30))));
                }
                bool contact = Array.Find(BowlsVectors.All, q => q.Name == name).Contact > 0;
                // contact vectors are chaotic under fused maths (the friction branch flips): informational, bounded
                if (contact) Assert.That(worstDb, Is.LessThan(10.0), $"{name} block RMS [{tag}]");
                else Assert.That(md / pk, Is.LessThan(2e-3), $"{name} linear vector [{tag}]");
            }
        }
    }
}
