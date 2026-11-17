// The platform's reference vectors (platform.vectors.json from the platform reference, copied to Reference~/) as the Layout tests
// read them: a small JSON reader (the file has keyed objects JsonUtility cannot read), the reference instruments as
// SeatPlanner instruments (so the C# planner is tested on the reference's own numbers, independent of the live
// descriptors), the reference display spots and stow-plan.mjs FURNITURE (copied here as test data), and registry.mjs's
// PRIORITY order (not in the file).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MusicRoom.Core;
using MusicRoom.Layout;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Layout
{
    public static class PlatformVectors
    {
        public const string RelativePath = "MusicRoom/Tests/EditMode/Layout/Reference~/platform.vectors.json";

        /// <summary>registry.mjs PRIORITY (planning order, big first).</summary>
        public static readonly string[] Priority = { "piano", "drums", "marimba", null /* 3: retired */, "harp", "handdrums", "handpan", "bowls" };

        static Dictionary<string, object> s_root;

        public static Dictionary<string, object> Root
        {
            get
            {
                if (s_root != null) return s_root;
                string path = Path.Combine(Application.dataPath, RelativePath);
                Assert.That(File.Exists(path), Is.True, path);
                s_root = (Dictionary<string, object>)new Reader(File.ReadAllText(path)).Value();
                return s_root;
            }
        }

        // ---------------------------------------------------------------- accessors

        public static Dictionary<string, object> Obj(object o, string key) => (Dictionary<string, object>)((Dictionary<string, object>)o)[key];
        public static List<object> Arr(object o, string key) => (List<object>)((Dictionary<string, object>)o)[key];
        public static double Num(object o, string key) => Convert.ToDouble(((Dictionary<string, object>)o)[key], CultureInfo.InvariantCulture);
        public static string Str(object o, string key) => ((Dictionary<string, object>)o)[key] as string;
        public static bool Bool(object o, string key) { object v = ((Dictionary<string, object>)o)[key]; return v is bool b && b; }
        public static bool Has(object o, string key) => ((Dictionary<string, object>)o).TryGetValue(key, out object v) && v != null;

        public static InstrumentKind KindOf(string id)
        {
            Assert.IsTrue(InstrumentIds.TryParse(id, out InstrumentKind k), id);
            return k;
        }

        public static SeatSlot SlotOf(string name) => (SeatSlot)Enum.Parse(typeof(SeatSlot), name);

        static SeatPlanner.Box BoxOf(object b) => new SeatPlanner.Box(Num(b, "cx"), Num(b, "cz"), Num(b, "hx"), Num(b, "hz"), Num(b, "y0"), Num(b, "y1"), Has(b, "air") && Bool(b, "air"));

        static SeatPlanner.Pose2 PoseOf(object p) => new SeatPlanner.Pose2(Num(p, "x"), Num(p, "z"), Num(p, "yaw"));

        /// <summary>The reference instrument (registry.mjs INSTRUMENTS[id]) as the planner sees it.</summary>
        public static SeatPlanner.Inst Inst(string id)
        {
            object d = Obj(Obj(Root, "instruments"), id);
            var prefs = new List<SeatSlot>();
            foreach (object s in Arr(d, "prefs")) prefs.Add(SlotOf((string)s));
            var boxes = new List<SeatPlanner.Box>();
            foreach (object b in Arr(d, "boxes")) boxes.Add(BoxOf(b));
            return new SeatPlanner.Inst
            {
                Kind = KindOf(id), Id = id, Canonical = SlotOf(Str(d, "canonicalSlot")), Prefs = prefs.ToArray(),
                Priority = Array.IndexOf(Priority, id), Home = PoseOf(Obj(d, "home")), Single = PoseOf(Obj(d, "single")),
                ReachMax = Num(d, "reachMax"), PlayX = Num(Obj(d, "playCentre"), "x"), PlayZ = Num(Obj(d, "playCentre"), "z"),
                Boxes = boxes.ToArray(),
            };
        }

        public static IEnumerable<string> InstrumentIdsInFile => Obj(Root, "instruments").Keys;

        /// <summary>A planner on the reference's own data: its eight instruments and its side table.</summary>
        public static SeatPlanner Planner()
        {
            var p = new SeatPlanner();
            foreach (string id in InstrumentIdsInFile) p.Add(Inst(id));
            object table = Obj(Obj(Root, "constants"), "table");
            p.TableHome = PoseOf(Obj(table, "home"));
            p.TableAlternate = PoseOf(Obj(table, "alternate"));
            var tb = new List<SeatPlanner.Box>();
            foreach (object b in Arr(table, "boxes")) tb.Add(BoxOf(b));
            p.TableBoxes = tb.ToArray();
            return p;
        }

        public static List<InstrumentKind> Kinds(List<object> ids)
        {
            var k = new List<InstrumentKind>(ids.Count);
            foreach (object o in ids) k.Add(KindOf((string)o));
            return k;
        }

        public static string[] Carousel
        {
            get
            {
                var l = new List<string>();
                foreach (object o in Arr(Obj(Root, "carousel"), "order")) l.Add((string)o);
                return l.ToArray();
            }
        }

        /// <summary>The reference display spot of an instrument (stow-plan.mjs SPOTS[id], its stowBoxes or its seat boxes
        /// without air), in the WORLD: the reference's pose is in room coordinates, turned by RoomFrame (the room faces the
        /// fire since 2026-11-11), as Layout turns the descriptors' spots (StowPlan.Spot.From).</summary>
        public static StowPlan.Spot Spot(string id) => Turned(SpotRoom(id));

        static StowPlan.Spot Turned(StowPlan.Spot r) => r.MovedToRoom(r.X, r.Z, r.Yaw);

        /// <summary>The reference display spot as the vectors give it, in ROOM coordinates.</summary>
        public static StowPlan.Spot SpotRoom(string id)
        {
            object s = Obj(Obj(Obj(Root, "stow"), "spots"), id);
            object inst = Obj(Obj(Root, "instruments"), id);
            var boxes = new List<SeatPlanner.Box>();
            if (Has(s, "stowBoxes")) foreach (object b in Arr(s, "stowBoxes")) boxes.Add(BoxOf(b));
            else foreach (object b in Arr(inst, "boxes")) { SeatPlanner.Box x = BoxOf(b); if (!x.Air) boxes.Add(x); }
            object pose = Obj(s, "pose");
            string mount = Str(s, "mount");
            return new StowPlan.Spot
            {
                Kind = KindOf(id), Id = Str(s, "id"), Mount = mount == "wall" ? StowMount.Wall : mount == "surface" ? StowMount.Surface : StowMount.Floor,
                X = Num(pose, "x"), Y = 0.0, Z = Num(pose, "z"), Yaw = Num(pose, "yaw"), Boxes = boxes.ToArray(),
                PlayX = Num(Obj(inst, "playCentre"), "x"), PlayZ = Num(Obj(inst, "playCentre"), "z"),
            };
        }

        /// <summary>stow-plan.mjs FURNITURE turned into the WORLD (RoomFrame; the bench stays at the seat), as StowPlan's
        /// ReferenceFurnitureWorld and the Room's published IRoomFurniture.</summary>
        public static readonly FurnitureBlocker[] Furniture = Array.ConvertAll(FurnitureRoomInit(), f => f.Name == StowPlan.SeatFurniture ? f : RoomFrame.ToWorld(f));

        /// <summary>stow-plan.mjs FURNITURE (the reference blockers in ROOM coordinates; Room publishes its exact ones as IRoomFurniture).</summary>
        public static FurnitureBlocker[] FurnitureRoom => FurnitureRoomInit();

        static FurnitureBlocker[] FurnitureRoomInit() => new[]
        {
            new FurnitureBlocker("bookshelf", -3.03f, -2.68f, 0.17f, 0.56f, 0f, 1.93f),
            new FurnitureBlocker("floor lamp", -2.40f, 0.52f, 0.16f, 0.16f, 0f, 1.80f),
            new FurnitureBlocker("lamp shade", -2.40f, 0.52f, 0.22f, 0.22f, 1.50f, 1.80f),
            new FurnitureBlocker("plant P1", -1.05f, -3.76f, 0.30f, 0.30f, 0f, 1.20f),
            new FurnitureBlocker("plant P2", 2.80f, -3.68f, 0.30f, 0.30f, 0f, 1.20f),
            new FurnitureBlocker("plant P3", 2.82f, 0.70f, 0.30f, 0.30f, 0f, 1.20f),
            new FurnitureBlocker("plant P4", -2.88f, -1.88f, 0.30f, 0.30f, 0f, 1.20f),
            new FurnitureBlocker("loveseat", 1.65f, -2.83f, 0.63f, 0.43f, 0f, 0.85f, 48.7f),
            new FurnitureBlocker("coffee table", 0.85f, -3.13f, 0.30f, 0.30f, 0f, 0.45f),
            new FurnitureBlocker("hearth", 0.30f, -3.85f, 0.80f, 0.25f, 0f, 1.40f),
            new FurnitureBlocker("curtain near", 3.12f, 0.08f, 0.06f, 0.20f, 0f, 2.62f),
            new FurnitureBlocker("curtain far", 3.12f, -2.44f, 0.06f, 0.20f, 0f, 2.62f),
            new FurnitureBlocker("inside sill", 3.10f, -1.18f, 0.10f, 1.05f, 0.59f, 0.63f),
            new FurnitureBlocker("window glass (view kept clear above 0.85 m)", 3.15f, -1.18f, 0.05f, 0.90f, 0.85f, 2.40f),
            new FurnitureBlocker("bench", 0f, -0.10f, 0.38f, 0.17f, 0f, 0.54f),
        };

        public static double R4(double v) => Math.Round(v * 1e4, MidpointRounding.AwayFromZero) / 1e4;

        // ---------------------------------------------------------------- a small JSON reader

        sealed class Reader
        {
            readonly string _s;
            int _i;
            public Reader(string s) { _s = s; }

            void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

            public object Value()
            {
                Ws();
                char c = _s[_i];
                if (c == '{') return Object();
                if (c == '[') return Array();
                if (c == '"') return String();
                if (c == 't') { _i += 4; return true; }
                if (c == 'f') { _i += 5; return false; }
                if (c == 'n') { _i += 4; return null; }
                return Number();
            }

            Dictionary<string, object> Object()
            {
                var d = new Dictionary<string, object>();
                _i++;
                Ws();
                if (_s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    Ws();
                    string k = String();
                    Ws();
                    _i++;   // ':'
                    d[k] = Value();
                    Ws();
                    if (_s[_i++] == '}') return d;
                }
            }

            List<object> Array()
            {
                var l = new List<object>();
                _i++;
                Ws();
                if (_s[_i] == ']') { _i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    Ws();
                    if (_s[_i++] == ']') return l;
                }
            }

            string String()
            {
                var sb = new StringBuilder();
                _i++;
                while (_s[_i] != '"')
                {
                    char c = _s[_i++];
                    if (c == '\\')
                    {
                        char e = _s[_i++];
                        if (e == 'u') { sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber)); _i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                    }
                    else sb.Append(c);
                }
                _i++;
                return sb.ToString();
            }

            double Number()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
