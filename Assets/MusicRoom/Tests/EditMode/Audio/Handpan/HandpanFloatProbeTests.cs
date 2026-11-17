// What managed arithmetic does in this editor (Mono, Apple silicon), measured, because the handpan's exact (managed)
// render relies on it to match the Node reference bit for bit. a = 1 + 2^-12: a*a = 1 + 2^-11 + 2^-24 exactly, so
// a*a + c with c = -(1 + 2^-11) is 0 when the product is rounded to float32 and 2^-24 when it is kept wider or fused.
// Measured (2026-11-01): a plain `a * a + c` and math.mad give 2^-24 (NOT rounded); an explicit (float) cast on the
// product, and float4 operators, round. So the handpan's scalar per-sample code writes every product as (float)(x * y):
// a no-op for Burst (which may fuse: FloatMode.Fast), the reference's rounding for Mono. Doubles: see the log line.
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public class HandpanFloatProbeTests
    {
        static float LoadF(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        static double LoadD(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void ExplicitCastsAndFloat4RoundEveryOperation()
        {
            float a = LoadF("1.000244140625"), c = LoadF("-1.00048828125");
            float plain = a * a + c;
            float cast = (float)(a * a) + c;
            float4 a4 = new float4(a), c4 = new float4(c);
            float4 v4 = a4 * a4 + c4;
            float mad = math.mad(a, a, c);
            // doubles: da = 1 + 2^-27, da*da = 1 + 2^-26 + 2^-54: rounded to double, minus (1 + 2^-26) is 0; fused, 2^-54
            double da = LoadD("1.000000007450580596923828125"), dc = LoadD("-1.0000000149011611938476562500");
            double dplain = da * da + dc, dcast = (double)(da * da) + dc;
            Debug.Log($"[Handpan] float probe: plain {plain:G9} cast {cast:G9} float4 {v4.x:G9} math.mad {mad:G9}; double plain {dplain:G17} cast {dcast:G17}");
            Assert.AreEqual(0f, cast, "an explicit (float) cast rounds the product");
            Assert.AreEqual(0f, v4.x, "float4 operators round per component");
            Assert.AreEqual(0.0, dplain, "double products are rounded (no fused multiply-add in managed doubles)");
        }
    }
}
