// A minimal JSON reader for the handpan tests' Reference~ files (the lab's own JSON, copied verbatim): objects become
// Dictionary<string, object>, arrays List<object>, numbers double, plus string / bool / null. Test-only (it allocates).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MusicRoom.Tests.Handpan
{
    public static class HandpanJson
    {
        public static string ReferenceDir(string sub) => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode", sub, "Reference~");

        public static object Load(string path) => Parse(File.ReadAllText(path));

        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("trailing data at " + i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i); if (s[i] != ':') throw new FormatException("':' expected at " + i); i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new FormatException("',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int j = i;
            while (j < s.Length && "+-0123456789.eE".IndexOf(s[j]) >= 0) j++;
            double x = double.Parse(s.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture);
            i = j;
            return x;
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected at " + i);
            i++;
            var b = new StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'n') b.Append('\n'); else if (e == 't') b.Append('\t'); else if (e == 'u') { b.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else b.Append(e);
                    i++;
                }
                else b.Append(s[i++]);
            }
            i++;
            return b.ToString();
        }

        // ---- access helpers
        public static Dictionary<string, object> O(object o) => (Dictionary<string, object>)o;
        public static List<object> A(object o) => (List<object>)o;
        public static double D(object o) => Convert.ToDouble(o, CultureInfo.InvariantCulture);
        public static int I(object o) => (int)Math.Round(D(o));
        public static object Get(this Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? v : null;
        public static double Num(this Dictionary<string, object> d, string k, double fallback = 0) => d.TryGetValue(k, out object v) && v != null ? D(v) : fallback;
        public static string Text(this Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? v as string : null;
        public static bool Flag(this Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && v is bool b && b;
        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string k) => O(d[k]);
        public static List<object> Arr(this Dictionary<string, object> d, string k) => A(d[k]);

        /// <summary>FNV-1a over float32 little-endian bytes (lab common.mjs fnv1a32), as "0x%08x".</summary>
        public static string Fnv(float[] x, int from = 0, int to = -1)
        {
            if (to < 0) to = x.Length;
            uint h = 0x811c9dc5;
            for (int i = from; i < to; i++)
            {
                uint w = (uint)BitConverter.SingleToInt32Bits(x[i]);
                for (int k = 0; k < 4; k++) { h ^= w & 0xff; h *= 0x01000193; w >>= 8; }
            }
            return "0x" + h.ToString("x8");
        }

        public static float[] ReadF32(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            var x = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, x, 0, x.Length * 4);
            return x;
        }
    }
}
