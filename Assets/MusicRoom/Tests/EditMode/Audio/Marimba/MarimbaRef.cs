// Test support for the marimba sound unit: the lab's reference files (Reference~/, copied by
// the marimba lab's tools/export-unity.mjs; never read from the lab at run time), a small JSON reader for them, and the
// lab's FNV-1a over float32 bits. Floats that must be exact travel as uint32 bit patterns, so no decimal parsing is
// trusted with them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MusicRoom.Audio.Burst;
using UnityEngine;

namespace MusicRoom.Tests.Audio
{
    public static class MarimbaRef
    {
        public static string Dir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Marimba/Reference~");
        public static string ListenDir => Path.Combine(Dir, "renders");
        public static string UnityListenDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "Listen", "marimba");

        static readonly Dictionary<string, object> s_cache = new Dictionary<string, object>();

        public static object Load(string file)
        {
            if (s_cache.TryGetValue(file, out var o)) return o;
            o = MarimbaJson.Parse(File.ReadAllText(Path.Combine(Dir, file)));
            s_cache[file] = o;
            return o;
        }

        public static Dictionary<string, object> Obj(object o) => (Dictionary<string, object>)o;
        public static List<object> Arr(object o) => (List<object>)o;
        public static double Num(object o) => o is bool b ? (b ? 1 : 0) : (double)o;
        public static Dictionary<string, object> Obj(object o, string key) => Obj(Obj(o)[key]);
        public static List<object> Arr(object o, string key) => Arr(Obj(o)[key]);
        public static double Num(object o, string key) => Num(Obj(o)[key]);
        public static float Bits(object o) => BitConverter.ToSingle(BitConverter.GetBytes((uint)(double)o), 0);
        public static uint Hex(string h) => uint.Parse(h.Substring(2), NumberStyles.HexNumber);

        public static uint Fnv(float[] a, int from = 0, int count = -1)
        {
            if (count < 0) count = a.Length - from;
            uint h = 0x811c9dc5;
            for (int i = from; i < from + count; i++)
            {
                uint w = BitConverter.ToUInt32(BitConverter.GetBytes(a[i]), 0);
                for (int k = 0; k < 4; k++) { h ^= w & 0xff; h = unchecked(h * 0x01000193u); w >>= 8; }
            }
            return h;
        }

        public static unsafe uint Fnv(float* a, int count)
        {
            uint h = 0x811c9dc5;
            for (int i = 0; i < count; i++)
            {
                uint w = *(uint*)(a + i);
                for (int k = 0; k < 4; k++) { h ^= w & 0xff; h = unchecked(h * 0x01000193u); w >>= 8; }
            }
            return h;
        }

        public static MarimbaMallet Mallet(string name) =>
            name == "soft" ? MarimbaMallet.Soft : name == "hard" ? MarimbaMallet.Hard : name == "finger" ? MarimbaMallet.Finger : MarimbaMallet.Medium;

        /// <summary>Posts one exported score event ([at, 0 strike, bar, vel, u, c, mallet] | [at, 1 damp, bar, kind, u, c] |
        /// [at, 2 undamp, bar]) through the unit's own API, at its exact sample.</summary>
        public static void Post(MarimbaOffline h, List<object> e)
        {
            long at = (long)Num(e[0]);
            int kind = (int)Num(e[1]), bar = (int)Num(e[2]);
            double when = MarimbaOffline.At(at);
            if (kind == 0) h.Sound.StrikeExact(bar, (float)Num(e[3]), Num(e[4]), Num(e[5]), (MarimbaMallet)(int)Num(e[6]), when);
            else if (kind == 1) h.Sound.DampExact(bar, (MarimbaDampKind)(int)Num(e[3]), Num(e[4]), Num(e[5]), when);
            else h.Sound.Undamp(bar, when);
        }

        /// <summary>Renders an exported score (the main thread posts each block's events just before it, as render.mjs).</summary>
        public static float[][] RenderScore(List<object> events, int samples, MarimbaRenderMode mode = MarimbaRenderMode.Fast, uint seed = 1, int budget = 1200)
        {
            using var h = new MarimbaOffline(seed, budget, mode);
            int k = 0;
            return h.Render(samples, s =>
            {
                while (k < events.Count && (long)Num(Arr(events[k])[0]) < s + MarimbaOffline.B) Post(h, Arr(events[k++]));
            });
        }
    }

    /// <summary>A minimal JSON reader: objects -> Dictionary, arrays -> List, numbers -> double, strings, bools, null.</summary>
    public static class MarimbaJson
    {
        public static object Parse(string s) { int i = 0; object v = Value(s, ref i); return v; }

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
                    Ws(s, ref i); i++;   // ':'
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;       // '}'
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
                    i++; return a;       // ']'
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int j = i;
            while (j < s.Length && "+-0123456789.eE".IndexOf(s[j]) >= 0) j++;
            double v = double.Parse(s.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture);
            i = j;
            return v;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;   // '"'
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 5; continue; }
                    sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                    i++;
                    continue;
                }
                sb.Append(s[i++]);
            }
            i++;
            return sb.ToString();
        }
    }
}
