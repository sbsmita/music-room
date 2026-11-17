// Numeric helpers that reproduce the Node lab's JavaScript semantics exactly (the sheet lab's modules), so the C# engraver
// produces the same coordinates and the same page rasters as the lab: V8's Math.hypot (Kahan-summed, normalised to the
// largest argument), Math.round (floor(x + 0.5)), Math.sign, and a stable sort (Array.prototype.sort is stable; List.Sort
// is not). Everything else in the engraver is plain IEEE double arithmetic written in the lab's operation order.
using System;
using System.Collections.Generic;

namespace MusicRoom.Sheet.Engrave
{
    public static class JsMath
    {
        /// <summary>V8's Math.hypot(a, b) (builtins math.tq): |a|, |b| normalised to the larger one, Kahan-summed squares,
        /// sqrt times the max.</summary>
        public static double Hypot(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.IsInfinity(a) || double.IsInfinity(b) ? double.PositiveInfinity : double.NaN;
            double x = Math.Abs(a), y = Math.Abs(b);
            double max = x > y ? x : y;
            if (double.IsPositiveInfinity(max)) return double.PositiveInfinity;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            double n = x / max;
            double summand = n * n - comp;
            double pre = sum + summand;
            comp = (pre - sum) - summand;
            sum = pre;
            n = y / max;
            summand = n * n - comp;
            pre = sum + summand;
            comp = (pre - sum) - summand;
            sum = pre;
            return Math.Sqrt(sum) * max;
        }

        /// <summary>JS Math.round: rounds half up (toward +infinity), never to even.</summary>
        public static double Round(double x) => Math.Floor(x + 0.5);
        public static int RoundInt(double x) => (int)Math.Floor(x + 0.5);

        /// <summary>JS Math.sign for finite numbers (0 for 0).</summary>
        public static double Sign(double x) => x > 0 ? 1 : x < 0 ? -1 : 0;

        /// <summary>Math.log2. V8's log2 is correctly rounded where log(x) / log(2) can be 1 ulp off (e.g. 9): the spacing
        /// arguments (1 + 2q for q a multiple of 1/16 quarter up to 16) come from a table of V8's own results.</summary>
        public static double Log2(double x)
        {
            double k = (x - 1) * 8;
            if (k >= 0 && k <= 256 && k == Math.Floor(k)) return BitConverter.Int64BitsToDouble(Log2Table[(int)k]);
            return Math.Log(x) / Math.Log(2.0);
        }

        // Math.log2(1 + k / 8) for k = 0..256, as printed by Node 26 (the sheet lab; bit patterns of the doubles).
        static readonly long[] Log2Table =
        {
            0x0000000000000000L, 0x3fc5c01a39fbd688L, 0x3fd49a784bcd1b8bL, 0x3fdd6753e032ea0fL, 0x3fe2b803473f7ad1L, 0x3fe66a008e4788ccL, 
            0x3fe9d5d9fd5010b3L, 0x3fed053f6d260896L, 0x3ff0000000000000L, 0x3ff1663f6fac9131L, 0x3ff2b803473f7ad1L, 0x3ff3f782d7204d01L, 
            0x3ff5269e12f346e3L, 0x3ff646eea247c5c2L, 0x3ff759d4f80cba84L, 0x3ff86082806b1d53L, 0x3ff95c01a39fbd68L, 0x3ffa4d3c25e68dc6L, 
            0x3ffb35004723c466L, 0x3ffc1404eadf3839L, 0x3ffceaecfea8085aL, 0x3ffdba4a47aa996dL, 0x3ffe829fb693044bL, 0x3fff446359b13539L, 
            0x4000000000000000L, 0x40005aeb4dd63bf6L, 0x4000b31fb7d64899L, 0x400108c588cda79eL, 0x40015c01a39fbd68L, 0x4001acf5e2db4ec9L, 
            0x4001fbc16b902681L, 0x40024880f561c0e7L, 0x4002934f0979a371L, 0x4002dc4439b3a19cL, 0x400323775123e2e1L, 0x400368fd7ee71054L, 
            0x4003acea7c065d42L, 0x4003ef50ad1960daL, 0x4004304140358eaaL, 0x40046fcc47a57403L, 0x4004ae00d1cfdeb4L, 0x4004eaecfea8085aL, 
            0x4005269e12f346e3L, 0x4005612089a6274dL, 0x40059a802391e233L, 0x4005d2c7f59381d5L, 0x40060a02756f9c1dL, 0x40064039858000b3L, 
            0x400675767f54042dL, 0x4006a9c23d600535L, 0x4006dd2523d54cb7L, 0x40070fa728b86815L, 0x4007414fdb498226L, 0x400772266acffd5bL, 
            0x4007a231acd89a9dL, 0x4007d17822f3c195L, 0x4008000000000000L, 0x40082dcf2d0b85a4L, 0x40085aeb4dd63bf6L, 0x40088759c4fd14fdL, 
            0x4008b31fb7d64899L, 0x4008de4212056d5eL, 0x400908c588cda79eL, 0x400932ae9e278ae2L, 0x40095c01a39fbd68L, 0x400984c2bd02f03bL, 
            0x4009acf5e2db4ec9L, 0x4009d49ee4c32597L, 0x4009fbc16b902681L, 0x400a2260fb5a616fL, 0x400a4880f561c0e7L, 0x400a6e2499d499beL, 
            0x400a934f0979a371L, 0x400ab803473f7ad1L, 0x400adc4439b3a19cL, 0x400b0014ac62c38bL, 0x400b23775123e2e1L, 0x400b466ec14fec0aL, 
            0x400b68fd7ee71054L, 0x400b8b25f5a52b6bL, 0x400bacea7c065d42L, 0x400bce4d543cea9aL, 0x400bef50ad1960daL, 0x400c0ff6a2e5e660L, 
            0x400c304140358eaaL, 0x400c50327ea87951L, 0x400c6fcc47a57403L, 0x400c8f107509c9f2L, 0x400cae00d1cfdeb4L, 0x400ccc9f1aad272aL, 
            0x400ceaecfea8085aL, 0x400d08ec1fa61aaaL, 0x400d269e12f346e3L, 0x400d440461c22a3eL, 0x400d612089a6274dL, 0x400d7df3fd07826cL, 
            0x400d9a802391e233L, 0x400db6c65a9d8652L, 0x400dd2c7f59381d5L, 0x400dee863e4d40b9L, 0x400e0a02756f9c1dL, 0x400e253dd2c1bbe3L, 
            0x400e4039858000b3L, 0x400e5af6b4ab2d7dL, 0x400e75767f54042dL, 0x400e8fb9fce38608L, 0x400ea9c23d600535L, 0x400ec39049af321bL, 
            0x400edd2523d54cb7L, 0x400ef681c7319f9bL, 0x400f0fa728b86815L, 0x400f2896372a4cc6L, 0x400f414fdb498226L, 0x400f59d4f80cba84L, 
            0x400f72266acffd5bL, 0x400f8a450b838050L, 0x400fa231acd89a9dL, 0x400fb9ed1c6cea54L, 0x400fd17822f3c195L, 0x400fe8d3845df08dL, 
            0x4010000000000000L, 0x40100b7f285b7784L, 0x401016e79685c2d2L, 0x40102239a3aa2668L, 0x40102d75a6eb1dfbL, 0x4010389bf5721557L, 
            0x401043ace27e8a7eL, 0x40104ea8bf749fc7L, 0x4010598fdbeb244cL, 0x4010646285b918cdL, 0x40106f210902b6afL, 0x401079cbb045fe97L, 
            0x40108462c466d3cfL, 0x40108ee68cbaa95cL, 0x401099574f13c571L, 0x4010a3b54fcc1fbaL, 0x4010ae00d1cfdeb4L, 0x4010b83a16a77814L, 
            0x4010c2615e81781eL, 0x4010cc76e83bf387L, 0x4010d67af16da765L, 0x4010e06db66eca6fL, 0x4010ea4f726192cbL, 0x4010f4205f3a7352L, 
            0x4010fde0b5c81340L, 0x40110790adbb0301L, 0x401111307dad30b7L, 0x40111ac05b291f07L, 0x401124407ab0e074L, 0x40112db10fc4d9abL, 
            0x401137124cea4cdfL, 0x4011406463b1b045L, 0x401149a784bcd1b9L, 0x401152dbdfc4c96bL, 0x40115c01a39fbd68L, 0x40116518fe4677baL, 
            0x40116e221cd9d0ceL, 0x4011771d2ba7efb4L, 0x4011800a563161c5L, 0x401188e9c72e0b22L, 0x401191bba891f171L, 0x40119a802391e233L, 
            0x4011a33760a7f605L, 0x4011abe18797f1f5L, 0x4011b47ebf73882aL, 0x4011bd0f2e9e7903L, 0x4011c592fad295b5L, 0x4011ce0a4923a588L, 
            0x4011d6753e032ea1L, 0x4011ded3fd442365L, 0x4011e726aa1e754dL, 0x4011ef6d67328e22L, 0x4011f7a8568cb06dL, 0x4011ffd799a83ffaL, 
            0x401207fb5172f330L, 0x401210139e4fee08L, 0x40121820a01ac755L, 0x40122022762a791dL, 0x401228193f543ca8L, 0x4012300519ee52edL, 
            0x401237e623d2ba02L, 0x40123fbc7a61d025L, 0x401247883a84e4f9L, 0x40124f4980b0b97fL, 0x4012570068e7ef5aL, 0x40125ead0ebd67e5L, 
            0x4012664f8d569395L, 0x40126de7ff6db22cL, 0x401275767f54042dL, 0x40127cfb26f3ee03L, 0x401284760fd30d55L, 0x40128be7531440e7L, 
            0x4012934f0979a371L, 0x40129aad4b6679d9L, 0x4012a20230e1151fL, 0x4012a94dd194a872L, 0x4012b09044d313a6L, 0x4012b7c9a196a287L, 
            0x4012bef9fe83c136L, 0x4012c62171eaa609L, 0x4012cd4011c8f119L, 0x4012d455f3cb41e1L, 0x4012db632d4ec329L, 0x4012e267d362ad8fL, 
            0x4012e963fac9c0eaL, 0x4012f057b7fbb4cbL, 0x4012f7431f26a05dL, 0x4012fe26443059e3L, 0x401305013ab7ce0eL, 0x40130bd416164f65L, 
            0x4013129ee960ddf1L, 0x40131961c7696778L, 0x4013201cc2c0005aL, 0x401326cfedb41566L, 0x40132d7b5a5596bfL, 0x4013341f1a761c0aL, 
            0x40133abb3faa0216L, 0x4013414fdb498226L, 0x401347dcfe71c304L, 0x40134e62ba05e419L, 0x401354e11eb0029aL, 0x40135b583ce23906L, 
            0x401361c824d7990dL, 0x40136830e6952011L, 0x40136e9291eaa65bL, 0x401374ed3673c92dL, 0x40137b40e398cfceL, 0x4013818da88f8bbaL, 
            0x401387d3945c340bL, 0x40138e12b5d23c39L, 0x4013944b1b952663L, 0x40139a7cd4195121L, 0x4013a0a7eda4c113L, 0x4013a6cc764fe63bL, 
            0x4013acea7c065d42L, 0x4013b3020c87acc3L, 0x4013b9133567feaeL, 0x4013bf1e0410d5e6L, 0x4013c52285c1c028L, 0x4013cb20c791045aL, 
            0x4013d118d66c4d4eL, 0x4013d70abf19511fL, 0x4013dcf68e36752aL, 0x4013e2dc503b6ec6L, 0x4013e8bc1179e0cbL, 0x4013ee95de1df5f6L, 
            0x4013f469c22ef846L, 0x4013fa37c98fe55fL, 0x4014000000000000L, 0x401405c2711b5eabL, 0x40140b7f285b7784L, 0x401411363117a97bL, 
            0x401416e79685c2d2L, 0x40141c9363ba8510L, 0x40142239a3aa2668L, 0x401427da6128d0b4L, 0x40142d75a6eb1dfbL
        };

        /// <summary>A stable sort (insertion sort for short lists, a merge sort above), matching Array.prototype.sort.</summary>
        public static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            int n = list.Count;
            if (n < 2) return;
            if (n <= 32)
            {
                for (int i = 1; i < n; i++)
                {
                    T k = list[i];
                    int j = i - 1;
                    while (j >= 0 && cmp(list[j], k) > 0) { list[j + 1] = list[j]; j--; }
                    list[j + 1] = k;
                }
                return;
            }
            var a = list.ToArray();
            var tmp = new T[n];
            MergeSort(a, tmp, 0, n, cmp);
            for (int i = 0; i < n; i++) list[i] = a[i];
        }

        static void MergeSort<T>(T[] a, T[] tmp, int lo, int hi, Comparison<T> cmp)
        {
            if (hi - lo < 2) return;
            int mid = (lo + hi) >> 1;
            MergeSort(a, tmp, lo, mid, cmp);
            MergeSort(a, tmp, mid, hi, cmp);
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi) tmp[k++] = cmp(a[j], a[i]) < 0 ? a[j++] : a[i++];
            while (i < mid) tmp[k++] = a[i++];
            while (j < hi) tmp[k++] = a[j++];
            Array.Copy(tmp, lo, a, lo, hi - lo);
        }

        /// <summary>Comparison from a JS-style numeric key difference (a - b), NaN-safe.</summary>
        public static int Cmp(double d) => d < 0 ? -1 : d > 0 ? 1 : 0;
    }

    /// <summary>Contour helpers of lib/raster.mjs: flat arrays [x0, y0, x1, y1, ...], closed implicitly.</summary>
    public static class Contours
    {
        /// <summary>Ellipse polygon (raster.mjs ellipse).</summary>
        public static double[] Ellipse(double cx, double cy, double rx, double ry, double rot = 0, int seg = 0)
        {
            int n = seg != 0 ? seg : Math.Max(12, Math.Min(96, (int)Math.Ceiling(Math.Sqrt(Math.Max(rx, ry)) * 10)));
            double c = Math.Cos(rot), s = Math.Sin(rot);
            var o = new double[2 * n];
            for (int i = 0; i < n; i++)
            {
                double a = ((double)i / n) * Math.PI * 2;
                double x = rx * Math.Cos(a), y = ry * Math.Sin(a);
                o[2 * i] = cx + x * c - y * s;
                o[2 * i + 1] = cy + x * s + y * c;
            }
            return o;
        }

        public static double[] Reverse(double[] c)
        {
            int n = c.Length >> 1;
            var o = new double[c.Length];
            for (int i = 0; i < n; i++) { o[2 * i] = c[2 * (n - 1 - i)]; o[2 * i + 1] = c[2 * (n - 1 - i) + 1]; }
            return o;
        }

        public static double SignedArea(double[] c)
        {
            double a = 0;
            int n = c.Length >> 1;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; a += c[2 * i] * c[2 * j + 1] - c[2 * j] * c[2 * i + 1]; }
            return a / 2;
        }

        /// <summary>Same orientation for every part of a union group (positive area): raster.mjs orient.</summary>
        public static double[] Orient(double[] c, int sign = 1) => JsMath.Sign(SignedArea(c)) == sign ? c : Reverse(c);

        public static double[] Rect(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1 };

        /// <summary>Parallelogram from p0 to p1 with vertical thickness t (beams, slanted bars).</summary>
        public static double[] Slab(double x0, double y0, double x1, double y1, double t)
            => new[] { x0, y0 - t / 2, x1, y1 - t / 2, x1, y1 + t / 2, x0, y0 + t / 2 };

        /// <summary>Flat array from [x, y] pairs.</summary>
        public static double[] Flat(List<double[]> pairs)
        {
            var o = new double[pairs.Count * 2];
            for (int i = 0; i < pairs.Count; i++) { o[2 * i] = pairs[i][0]; o[2 * i + 1] = pairs[i][1]; }
            return o;
        }
    }
}
