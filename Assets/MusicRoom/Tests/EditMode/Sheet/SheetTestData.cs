// Shared test data for the sheet-music EditMode tests: the lab's vectors (Reference~/, copied from the sheet lab's
// output and written by its vectors-for.mjs for both lesson files), a minimal JSON reader (the
// test assembly has no JSON library), and the two books the tests engrave: today's Lessons/Resources/lessons.json and the
// corrected (proposed) set, Reference~/lessons-proposed.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MusicRoom.Core;
using MusicRoom.Sheet.Engrave;
using UnityEngine;

namespace MusicRoom.Tests.Sheet
{
    public static class SheetTestData
    {
        public static string RefDir => Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Sheet/Reference~");
        public static string SheetDir => Path.Combine(Application.dataPath, "MusicRoom/Sheet");

        public static object Json(string file) => MiniJson.Parse(File.ReadAllText(Path.Combine(RefDir, file)));

        static SheetFont s_font;
        public static SheetFont Font
        {
            get
            {
                if (s_font != null) return s_font;
                var ta = Resources.Load<TextAsset>(SheetFont.ResourceName);
                if (ta == null) throw new FileNotFoundException("Sheet/Resources/sheet-font.bytes missing (SHEET_WRITE=1 Tools/unity.sh test-edit regenerates it)");
                return s_font = SheetFont.FromBytes(ta.bytes);
            }
        }

        static SheetBookFile s_bookFile;
        public static SheetBookFile BookFile => s_bookFile ?? (s_bookFile = SheetBookData.Parse(Resources.Load<TextAsset>(SheetBookData.ResourceName).text));
        public static Dictionary<string, SheetMeta> Meta => SheetBookData.MetaTable(BookFile);

        public static LessonFile CurrentLessons() => JsonUtility.FromJson<LessonFile>(Resources.Load<TextAsset>("lessons").text);
        public static LessonFile ProposedLessons() => JsonUtility.FromJson<LessonFile>(File.ReadAllText(Path.Combine(RefDir, "lessons-proposed.json")));

        /// <summary>The piano songs of a lesson file, as the book prints them (meta, or the default).</summary>
        public static List<(SheetLesson, SheetMeta)> Songs(LessonFile f)
        {
            var meta = Meta;
            var o = new List<(SheetLesson, SheetMeta)>();
            foreach (var l in f.lessons)
            {
                if (l.instrument != "piano") continue;
                if (meta.TryGetValue(l.id, out var m) && m.BookSong != null) continue;
                o.Add((SheetBookData.FromLesson(l), m ?? SheetMeta.Default));
            }
            return o;
        }

        static readonly Dictionary<string, SheetBook> s_books = new Dictionary<string, SheetBook>();
        public static SheetBook Book(string which)
        {
            if (s_books.TryGetValue(which, out var b)) return b;
            b = new SheetBook(Font, BookFile.book?.title);
            b.Build(Songs(which == "current" ? CurrentLessons() : ProposedLessons()));
            return s_books[which] = b;
        }

        /// <summary>The two-staff demos (sheet-book.json extraLessons), with the Ode's melody from the proposed set.</summary>
        public static List<(SheetLesson, SheetMeta)> Extras()
        {
            var prop = ProposedLessons();
            SheetLesson ById(string id) { foreach (var l in prop.lessons) if (l.id == id) return SheetBookData.FromLesson(l); return null; }
            var o = new List<(SheetLesson, SheetMeta)>();
            foreach (var x in BookFile.extraLessons) o.Add((SheetBookData.ExpandExtra(x, ById), Meta[x.id]));
            return o;
        }

        public static string Sha1(byte[] b)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var h = sha.ComputeHash(b);
                var sb = new StringBuilder(40);
                foreach (byte x in h) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static double D(object o) => Convert.ToDouble(o, CultureInfo.InvariantCulture);
        public static int I(object o) => (int)Math.Round(D(o));
        public static List<object> L(object o) => (List<object>)o;
        public static Dictionary<string, object> O(object o) => (Dictionary<string, object>)o;
    }

    /// <summary>A small JSON reader: objects -> Dictionary, arrays -> List, numbers -> double, strings, bools, null.</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

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
                    Ws(s, ref i); i++; // :
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
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
            i++;
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }
    }
}
