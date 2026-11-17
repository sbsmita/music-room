// Sunny's voice line table (12-new-instruments/platform.md 8.2): the Guide's own Resources/Voice/manifest.json merged with
// every module's Resources/VoiceManifests/<Module>.json (Resources.LoadAll<TextAsset>("VoiceManifests"), e.g. the handpan
// module's Assets/MusicRoom/Handpan/Resources/VoiceManifests/Handpan.json). Every file has today's manifest.json shape:
//   { "<line id>": { "file": "<line id>.wav", "seconds": 3.573, "rate": 22050 }, ... }
// The Guide's lines come first in their file order (GuideLines.Ids keeps the same order), then each module's in the order
// of its file name (ordinal), each in its file order. A duplicate id is an error ("[Audio] voice: duplicate line <id>") and
// the later one is dropped. The WAV of line <id> is Resources.Load<AudioClip>("Voice/<id>"), in any module's
// Resources/Voice/ folder. Also the small JSON reader the merge needs (objects keep their key order).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MusicRoom.Audio.Synths
{
    public sealed class VoiceManifest
    {
        /// <summary>The Guide's own manifest (Guide/Resources/Voice/manifest.json).</summary>
        public const string BaseResource = "Voice/manifest";
        /// <summary>The Resources folder of the modules' manifests.</summary>
        public const string ModuleFolder = "VoiceManifests";

        readonly List<string> _ids = new List<string>();
        readonly List<float> _seconds = new List<float>();
        readonly List<string> _from = new List<string>();
        readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<string> _errors = new List<string>();
        string[] _idArray;

        public int Count => _ids.Count;
        /// <summary>The ids in table order (the voice slots: AudioEngine.VoiceSlot0 + index).</summary>
        public string[] Ids => _idArray ?? (_idArray = _ids.ToArray());
        public string Id(int i) => _ids[i];
        /// <summary>The manifest's dry seconds of the recording (-1 when the entry has none).</summary>
        public float Seconds(int i) => _seconds[i];
        /// <summary>The file a line came from ("manifest" for the Guide's, else the module manifest's name).</summary>
        public string From(int i) => _from[i];
        public int IndexOf(string id) => id != null && _index.TryGetValue(id, out int i) ? i : -1;
        /// <summary>Lines dropped as duplicates and files that did not parse (each also logged).</summary>
        public IReadOnlyList<string> Errors => _errors;

        static VoiceManifest s_default;

        /// <summary>The app's table (loaded once from Resources, main thread).</summary>
        public static VoiceManifest Default => s_default ?? (s_default = Load());

        /// <summary>Tests: forget the loaded table (the next Default loads again).</summary>
        public static void ResetDefault() => s_default = null;

        /// <summary>The Guide's manifest and every module manifest from Resources.</summary>
        public static VoiceManifest Load()
        {
            var baseAsset = Resources.Load<TextAsset>(BaseResource);
            var modules = new List<KeyValuePair<string, string>>();
            foreach (TextAsset t in Resources.LoadAll<TextAsset>(ModuleFolder))
                if (t != null) modules.Add(new KeyValuePair<string, string>(t.name, t.text));
            return Merge(baseAsset != null ? baseAsset.text : null, modules);
        }

        /// <summary>The merge alone (tests): baseJson (the Guide's; null = none), then the modules sorted by name (ordinal).</summary>
        public static VoiceManifest Merge(string baseJson, IList<KeyValuePair<string, string>> modules)
        {
            var m = new VoiceManifest();
            if (baseJson != null) m.Add("manifest", baseJson);
            if (modules != null)
            {
                var sorted = new List<KeyValuePair<string, string>>(modules);
                sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var kv in sorted) m.Add(kv.Key, kv.Value);
            }
            return m;
        }

        void Add(string name, string json)
        {
            List<KeyValuePair<string, object>> root;
            try { root = MiniJson.Parse(json) as List<KeyValuePair<string, object>>; }
            catch (FormatException e) { Error($"[Audio] voice: manifest {name} does not parse ({e.Message})"); return; }
            if (root == null) { Error($"[Audio] voice: manifest {name} is not an object of lines"); return; }
            foreach (var kv in root)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (_index.ContainsKey(kv.Key)) { Error("[Audio] voice: duplicate line " + kv.Key); continue; }
                float seconds = -1f;
                if (kv.Value is List<KeyValuePair<string, object>> entry)
                    foreach (var f in entry)
                        if (f.Key == "seconds" && f.Value is double d) seconds = (float)d;
                _index[kv.Key] = _ids.Count;
                _ids.Add(kv.Key);
                _seconds.Add(seconds);
                _from.Add(name);
            }
            _idArray = null;
        }

        void Error(string msg)
        {
            _errors.Add(msg);
            Debug.LogError(msg);
        }

        /// <summary>A manifest file's text for lines (id, seconds) in the shape above (tests, tools).</summary>
        public static string Write(IEnumerable<KeyValuePair<string, float>> lines)
        {
            var sb = new StringBuilder("{\n");
            bool first = true;
            foreach (var l in lines)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append(" \"").Append(l.Key).Append("\": { \"file\": \"").Append(l.Key).Append(".wav\", \"seconds\": ")
                  .Append(l.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append(", \"rate\": 22050 }");
            }
            return sb.Append("\n}\n").ToString();
        }
    }

    /// <summary>A minimal JSON reader: objects as List&lt;KeyValuePair&lt;string, object&gt;&gt; (key order kept), arrays as
    /// List&lt;object&gt;, numbers as double, strings, bool, null. Throws FormatException on malformed input. Not for hot paths.</summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("no text");
            int i = 0;
            object v = Value(json, ref i);
            Ws(json, ref i);
            if (i != json.Length) throw new FormatException("trailing text at " + i);
            return v;
        }

        /// <summary>obj[key] of a parsed object (null when absent or not an object).</summary>
        public static object Get(object obj, string key)
        {
            if (obj is List<KeyValuePair<string, object>> o) foreach (var kv in o) if (kv.Key == key) return kv.Value;
            return null;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't' && Match(s, ref i, "true")) return true;
            if (c == 'f' && Match(s, ref i, "false")) return false;
            if (c == 'n' && Match(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        static bool Match(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw new FormatException("bad literal at " + i);
            i += w.Length;
            return true;
        }

        static List<KeyValuePair<string, object>> Obj(string s, ref int i)
        {
            var o = new List<KeyValuePair<string, object>>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("expected a key at " + i);
                string k = Str(s, ref i);
                Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at " + i);
                i++;
                o.Add(new KeyValuePair<string, object>(k, Value(s, ref i)));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return o; }
                throw new FormatException("expected ',' or '}' at " + i);
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(Value(s, ref i));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw new FormatException("expected ',' or ']' at " + i);
            }
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        static double Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("unexpected '" + s[i] + "' at " + i);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
