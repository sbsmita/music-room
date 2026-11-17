// The seat planner (12-new-instruments/platform.md 4.2): which seat slot each member of My set takes, where its rig goes,
// whether the set fits, and which member a new instrument replaces when it does not. It follows the Node
// reference planner (seat-planner.mjs, geometry.mjs) line for line in double precision, with the same loop order and so the same
// candidate count (SeatPlannerTests replays platform.vectors.json, JoinTests its joins).
//
// Rules (seat-planner.mjs):
// 1. The seated player (torso, knees, seat) is an obstacle for non-air boxes. The side table stands at its home; only if the
//    set cannot be planned that way is it tried at its alternate pose (behind-left).
// 2. Members in Priority order (big first); for each its slot preferences in order; for each slot the radial push-outs
//    0 / 5 / 10 / 15 cm, of which the first feasible one is used. A candidate is the member's canonical home rotated about the
//    head from its canonical slot to this slot, pushed out along the slot's direction. Every complete assignment is costed
//    (CostOf: preference rank + 2 for a rear slot + 0.25 per 5 cm push); the cheapest wins, ties to the first found.
//    Canonical members cost 0, so the default set comes out exactly at today's homes.
// 3. A candidate is accepted when its play centre is within ReachMax of the head (horizontal), no box overlaps (3 cm gap) a
//    box of the table, of an already placed member or (non-air boxes only) of the player, and every non-air box is inside
//    the room box at the default seat (other seats: PlacementSolver fits the seat as today).
// 4. One member per slot, at most MySetRules.Max members; a set of one is the instrument alone at its Single home.
// Mixed reality (platform.md 9.5): the rear slots cost +100 (no swivel); a set that then needs a rear slot is re-planned
// without the rear slots, else its last member is put away (PlanMixedReality).
//
// Seat frame: Unity's (x right, z ahead of the head, yaw degrees clockwise from above). Runs only when the set changes,
// the seat is re-fitted or the head re-centres, never per frame; its working lists are fields (a plan allocates its result).
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Layout
{
    public sealed class SeatPlanner
    {
        public const double Deg = Math.PI / 180.0;
        /// <summary>Clearance between two instruments' boxes (m).</summary>
        public const double Gap = 0.03;
        /// <summary>Radial push-outs tried per slot (m).</summary>
        public static readonly double[] Pushes = { 0.0, 0.05, 0.10, 0.15 };
        public const double RearCost = 2.0, MixedRealityRearCost = 100.0;
        /// <summary>The VR room box at the default seat, in the world (registry.mjs ROOM turned to face the fire: RoomFrame,
        /// x -3.2..3.2, z -1.62..3.58): non-air boxes stay inside it, 2 cm in.</summary>
        public static readonly double RoomX0 = RoomFrame.WorldX0, RoomX1 = RoomFrame.WorldX1, RoomZ0 = RoomFrame.WorldZ0, RoomZ1 = RoomFrame.WorldZ1;
        public const double RoomInset = 0.02;
        /// <summary>The seated player (registry.mjs PLAYER): obstacles for planned instruments, never for air boxes.</summary>
        public static readonly Box[] Player =
        {
            new Box(0.0, -0.05, 0.20, 0.15, 0.45, 1.50),   // torso
            new Box(0.0, 0.25, 0.22, 0.16, 0.40, 0.62),    // knees
            new Box(0.0, -0.10, 0.30, 0.17, 0.44, 0.54),   // seat (the bench ends may have stand feet beside them)
        };
        public static readonly string[] PlayerNames = { "torso", "knees", "seat" };

        // ---------------------------------------------------------------- data

        /// <summary>A footprint box (geometry.mjs box): rig-local centre and half sizes on the floor plane, heights above the floor.</summary>
        public struct Box
        {
            public double Cx, Cz, Hx, Hz, Y0, Y1;
            public bool Air;
            public Box(double cx, double cz, double hx, double hz, double y0, double y1, bool air = false)
            { Cx = cx; Cz = cz; Hx = hx; Hz = hz; Y0 = y0; Y1 = y1; Air = air; }
        }

        /// <summary>A rig pose on the floor plane: x, z (m) and yaw (deg, Unity).</summary>
        public struct Pose2
        {
            public double X, Z, Yaw;
            public Pose2(double x, double z, double yaw) { X = x; Z = z; Yaw = yaw; }
            public override string ToString() => $"({X:F4}, {Z:F4}) yaw {Yaw:F2}";
        }

        /// <summary>A world box (geometry.mjs worldBox): centre, the unit axis of Hx (Ux, Uz), half sizes and heights.</summary>
        public struct FloorObb
        {
            public double Cx, Cz, Ux, Uz, Hx, Hz, Y0, Y1;
            public bool Air;
        }

        /// <summary>One instrument as the planner sees it (registry.mjs INSTRUMENTS[id]).</summary>
        public sealed class Inst
        {
            public InstrumentKind Kind;
            public string Id;
            public SeatSlot Canonical;
            public SeatSlot[] Prefs = Array.Empty<SeatSlot>();
            public int Priority;
            public Pose2 Home, Single;
            public double ReachMax, PlayX, PlayZ;
            public Box[] Boxes = Array.Empty<Box>();

            /// <summary>The descriptor's seat data in double precision. Each float is rounded to 6 decimals first, so the value
            /// the descriptor was written with (0.62f) becomes that decimal (0.62) and the live planner decides exactly as the
            /// reference does on the same numbers.</summary>
            /// <param name="mirrored">The instrument is built mirrored (SeatSpec.MirrorKey): its boxes and play
            /// centre mirrored about the rig's x.</param>
            public static Inst From(InstrumentDescriptor d, bool mirrored = false)
            {
                if (d?.Seat == null) return null;
                SeatSpec s = d.Seat;
                FootBox[] foot = s.BoxesFor(mirrored);
                UnityEngine.Vector2 pc = s.PlayCentreFor(mirrored);
                var boxes = new Box[foot != null ? foot.Length : 0];
                for (int i = 0; i < boxes.Length; i++)
                {
                    FootBox b = foot[i];
                    boxes[i] = new Box(D(b.Cx), D(b.Cz), D(b.Hx), D(b.Hz), D(b.Y0), D(b.Y1), b.Air);
                }
                return new Inst
                {
                    Kind = d.Kind, Id = d.Id, Canonical = s.CanonicalSlot, Prefs = s.Prefs ?? Array.Empty<SeatSlot>(), Priority = s.Priority,
                    Home = new Pose2(D(s.Home.x), D(s.Home.z), D(s.HomeYaw)), Single = new Pose2(D(s.Single.x), D(s.Single.z), D(s.SingleYaw)),
                    ReachMax = D(s.ReachMax), PlayX = D(pc.x), PlayZ = D(pc.y), Boxes = boxes,
                };
            }

            public int PrefRank(SeatSlot slot) => Array.IndexOf(Prefs, slot);
            public override string ToString() => Id ?? Kind.ToString();
        }

        /// <summary>A planned member: its slot, the push-out, the rig pose in the seat frame and its reach.</summary>
        public struct Member
        {
            public InstrumentKind Kind;
            public SeatSlot Slot;
            public double Push;
            public Pose2 Pose;
            public double Reach;
            /// <summary>At its canonical slot with no push: the rig stands exactly at its home.</summary>
            public bool AtHome;
            public override string ToString() => $"{Kind}@{Slot} push {Push:F2} {Pose}";
        }

        public sealed class Plan
        {
            public bool Fits, TableAlt, Single;
            public double Cost;
            public Pose2 Table;
            /// <summary>The members in the order of the set asked for (seat-planner.mjs bySet).</summary>
            public readonly List<Member> Members = new List<Member>(MySetRules.Max);
            /// <summary>When it does not fit: the deepest member that failed (seat-planner.mjs failed).</summary>
            public InstrumentKind? Failed;
            public string FailedWhy;
            /// <summary>Candidates evaluated (seat-planner.mjs lastNodes()).</summary>
            public int Nodes;
            /// <summary>Mixed reality: members put away because the set needed a rear slot (PlanMixedReality).</summary>
            public readonly List<InstrumentKind> PutAway = new List<InstrumentKind>(2);

            public bool TryGet(InstrumentKind kind, out Member m)
            {
                for (int i = 0; i < Members.Count; i++) if (Members[i].Kind == kind) { m = Members[i]; return true; }
                m = default;
                return false;
            }

            public bool UsesRear
            {
                get { for (int i = 0; i < Members.Count; i++) if (SeatSlots.IsRear(Members[i].Slot)) return true; return false; }
            }
        }

        public readonly struct JoinResult
        {
            public readonly JoinAction Action;
            public readonly InstrumentKind? Replaced;
            public readonly Plan Plan;
            public JoinResult(JoinAction action, InstrumentKind? replaced, Plan plan) { Action = action; Replaced = replaced; Plan = plan; }
            public override string ToString() => Action + (Replaced.HasValue ? " " + Replaced.Value : "");
        }

        // ---------------------------------------------------------------- the planner's world

        readonly Inst[] _inst = new Inst[InstrumentIds.KindCount];
        public Pose2 TableHome = new Pose2(D(BuiltIns.TableHome.x), D(BuiltIns.TableHome.z), D(BuiltIns.TableHomeYaw));
        public Pose2 TableAlternate = new Pose2(D(BuiltIns.TableAlternate.x), D(BuiltIns.TableAlternate.z), D(BuiltIns.TableAlternateYaw));
        public Box[] TableBoxes = FromFoot(BuiltIns.TableBoxes);
        public int MaxSet = MySetRules.Max;

        /// <summary>A float as the decimal it was written as (6 decimals).</summary>
        public static double D(float f) => Math.Round((double)f, 6);

        static Box[] FromFoot(FootBox[] f)
        {
            var b = new Box[f.Length];
            for (int i = 0; i < f.Length; i++) b[i] = new Box(D(f[i].Cx), D(f[i].Cz), D(f[i].Hx), D(f[i].Hz), D(f[i].Y0), D(f[i].Y1), f[i].Air);
            return b;
        }

        /// <summary>Every playable descriptor of the registry (loaded or not: the planner plans for the menu's whole grid).</summary>
        /// <param name="store">The settings store, for the instruments built mirrored (SeatSpec.MirrorKey); null: none is.</param>
        public static SeatPlanner FromRegistry(IInstrumentRegistry registry, ISettingsStore store = null)
        {
            var p = new SeatPlanner();
            if (registry == null) return p;
            IReadOnlyList<InstrumentDescriptor> all = registry.All;
            for (int i = 0; i < all.Count; i++) p.Add(Inst.From(all[i], all[i] != null && all[i].IsMirrored(store)));
            return p;
        }

        public void Add(Inst inst)
        {
            if (inst == null || (int)inst.Kind < 0 || (int)inst.Kind >= _inst.Length) return;
            _inst[(int)inst.Kind] = inst;
        }

        public Inst Get(InstrumentKind kind) => (int)kind >= 0 && (int)kind < _inst.Length ? _inst[(int)kind] : null;

        // ---------------------------------------------------------------- geometry.mjs

        public static double Wrap(double a)
        {
            a %= 360.0;
            if (a > 180.0) a -= 360.0;
            else if (a <= -180.0) a += 360.0;
            return a;
        }

        /// <summary>Rig-local point to world, Unity yaw (Quaternion.Euler(0, yaw, 0)).</summary>
        public static void ToWorld(in Pose2 pose, double lx, double lz, out double x, out double z)
        {
            double c = Math.Cos(pose.Yaw * Deg), s = Math.Sin(pose.Yaw * Deg);
            x = pose.X + lx * c + lz * s;
            z = pose.Z - lx * s + lz * c;
        }

        /// <summary>A seat-frame pose rotated about the head (the origin) by d degrees.</summary>
        public static Pose2 RotatePose(in Pose2 pose, double d)
        {
            double c = Math.Cos(d * Deg), s = Math.Sin(d * Deg);
            return new Pose2(pose.X * c + pose.Z * s, -pose.X * s + pose.Z * c, Wrap(pose.Yaw + d));
        }

        public static FloorObb WorldBox(in Pose2 pose, in Box b)
        {
            ToWorld(pose, b.Cx, b.Cz, out double x, out double z);
            double yaw = pose.Yaw * Deg;
            return new FloorObb { Cx = x, Cz = z, Ux = Math.Cos(yaw), Uz = -Math.Sin(yaw), Hx = b.Hx, Hz = b.Hz, Y0 = b.Y0, Y1 = b.Y1, Air = b.Air };
        }

        /// <summary>Separating-axis test of two floor boxes, each grown by gap / 2; their heights must overlap too.</summary>
        public static bool Overlaps(in FloorObb a, in FloorObb b, double gap)
        {
            if (a.Y1 <= b.Y0 || b.Y1 <= a.Y0) return false;
            double g = gap / 2.0;
            double dx = b.Cx - a.Cx, dz = b.Cz - a.Cz;
            return !Separates(a, b, a.Ux, a.Uz, dx, dz, g) && !Separates(a, b, -a.Uz, a.Ux, dx, dz, g)
                && !Separates(a, b, b.Ux, b.Uz, dx, dz, g) && !Separates(a, b, -b.Uz, b.Ux, dx, dz, g);
        }

        static bool Separates(in FloorObb a, in FloorObb b, double nx, double nz, double dx, double dz, double g)
        {
            double ra = (a.Hx + g) * Math.Abs(a.Ux * nx + a.Uz * nz) + (a.Hz + g) * Math.Abs(-a.Uz * nx + a.Ux * nz);
            double rb = (b.Hx + g) * Math.Abs(b.Ux * nx + b.Uz * nz) + (b.Hz + g) * Math.Abs(-b.Uz * nx + b.Ux * nz);
            return Math.Abs(dx * nx + dz * nz) > ra + rb;
        }

        /// <summary>The axis-aligned world extent of a box (room-box tests).</summary>
        public static void Extent(in FloorObb o, out double x0, out double x1, out double z0, out double z1)
        {
            double ex = Math.Abs(o.Ux) * o.Hx + Math.Abs(o.Uz) * o.Hz;
            double ez = Math.Abs(o.Uz) * o.Hx + Math.Abs(o.Ux) * o.Hz;
            x0 = o.Cx - ex; x1 = o.Cx + ex; z0 = o.Cz - ez; z1 = o.Cz + ez;
        }

        public static bool InsideRoom(in FloorObb o)
        {
            Extent(o, out double x0, out double x1, out double z0, out double z1);
            return !(x0 < RoomX0 + RoomInset || x1 > RoomX1 - RoomInset || z0 < RoomZ0 + RoomInset || z1 > RoomZ1 - RoomInset);
        }

        // ---------------------------------------------------------------- seat-planner.mjs

        /// <summary>A member's slot cost: its preference rank, +2 for a rear slot (+100 in mixed reality), +0.25 per 5 cm push.</summary>
        public static double CostOf(Inst inst, SeatSlot slot, double push, bool mixedReality = false)
        {
            double h = SeatSlots.Heading(slot);
            double rear = h > 100.0 || h < -100.0 ? (mixedReality ? MixedRealityRearCost : RearCost) : 0.0;
            return inst.PrefRank(slot) + rear + Math.Floor(push / 0.05 + 0.5) * 0.25;   // JS Math.round
        }

        static Pose2 Candidate(Inst inst, SeatSlot slot, double push)
        {
            double d = SeatSlots.Heading(slot) - SeatSlots.Heading(inst.Canonical);
            Pose2 pose = RotatePose(inst.Home, d);
            double a = SeatSlots.Heading(slot) * Deg;
            pose.X += push * Math.Sin(a);
            pose.Z += push * Math.Cos(a);
            return pose;
        }

        // ---- the search's working state (fields: a plan allocates only its result)
        readonly List<FloorObb> _table = new List<FloorObb>(2), _player = new List<FloorObb>(3);
        readonly List<Inst> _order = new List<Inst>(MySetRules.Max + 1);
        readonly List<Placed> _placed = new List<Placed>(MySetRules.Max + 1);
        readonly Member[] _best = new Member[8];
        bool _mr, _noRear;
        int _nodes, _deepest;
        InstrumentKind? _failed;
        string _failedWhy;
        double _bestCost;
        bool _hasBest;
        SeatSlot[] _used = new SeatSlot[8];
        int _usedCount;

        sealed class Placed
        {
            public Inst Inst;
            public SeatSlot Slot;
            public double Push, Reach;
            public Pose2 Pose;
            public readonly List<FloorObb> Boxes = new List<FloorObb>(24);
        }
        readonly Stack<Placed> _pool = new Stack<Placed>();

        /// <summary>Candidates evaluated by the last Plan (the reference's lastNodes()).</summary>
        public int LastNodes { get; private set; }

        /// <summary>seat-planner.mjs plan(set): with the side table at its home, then (only if that fails) at its alternate pose.
        /// A set of one is the instrument alone at its Single home. Members of the result are in the order of
        /// <paramref name="set"/>.</summary>
        public Plan PlanSet(IReadOnlyList<InstrumentKind> set) => PlanSet(set, false, false);

        public Plan PlanSet(IReadOnlyList<InstrumentKind> set, bool mixedReality, bool noRear)
        {
            _nodes = 0;
            var result = new Plan();
            if (set != null && set.Count == 1)
            {
                Inst inst = Get(set[0]);
                if (inst == null) return Done(result, false);
                Pose2 pose = inst.Single;
                ToWorld(pose, inst.PlayX, inst.PlayZ, out double px, out double pz);
                result.Fits = true;
                result.Single = true;
                result.Table = TableHome;
                result.Members.Add(new Member { Kind = inst.Kind, Slot = SeatSlot.Front, Push = 0.0, Pose = pose, Reach = Hypot(px, pz), AtHome = true });
                return Done(result, true);
            }
            bool sizeFail;
            Plan a = PlanWith(set, false, mixedReality, noRear, out sizeFail);
            if (a.Fits || sizeFail) return Done(a, a.Fits);
            Plan b = PlanWith(set, true, mixedReality, noRear, out _);
            return Done(b.Fits ? b : a, b.Fits);
        }

        Plan Done(Plan p, bool fits)
        {
            p.Nodes = _nodes;
            LastNodes = _nodes;
            return p;
        }

        /// <summary>Mixed reality (platform.md 9.5): rear slots cost +100; a plan that still needs one is re-planned without the
        /// rear slots, and if that does not fit the last member is put away (Plan.PutAway), until it fits.</summary>
        public Plan PlanMixedReality(IReadOnlyList<InstrumentKind> set)
        {
            var work = new List<InstrumentKind>(set ?? Array.Empty<InstrumentKind>());
            var gone = new List<InstrumentKind>(2);
            while (true)
            {
                Plan p = PlanSet(work, true, false);
                if (p.Fits && !p.UsesRear) { p.PutAway.AddRange(gone); return p; }
                Plan q = PlanSet(work, true, true);
                if (q.Fits || work.Count <= 1) { q.PutAway.AddRange(gone); return q; }
                gone.Add(work[work.Count - 1]);
                work.RemoveAt(work.Count - 1);
            }
        }

        static double Hypot(double x, double z) => Math.Sqrt(x * x + z * z);

        Plan PlanWith(IReadOnlyList<InstrumentKind> set, bool alt, bool mr, bool noRear, out bool sizeFail)
        {
            var result = new Plan { TableAlt = alt, Table = alt ? TableAlternate : TableHome };
            sizeFail = set == null || set.Count == 0 || set.Count > MaxSet;
            if (sizeFail) return result;
            _order.Clear();
            for (int i = 0; i < set.Count; i++)
            {
                Inst inst = Get(set[i]);
                if (inst == null) { sizeFail = true; return result; }
                // members in Priority order (a stable insertion: the reference's sort by PRIORITY index)
                int at = _order.Count;
                while (at > 0 && _order[at - 1].Priority > inst.Priority) at--;
                _order.Insert(at, inst);
            }
            _table.Clear();
            for (int i = 0; i < TableBoxes.Length; i++) _table.Add(WorldBox(result.Table, TableBoxes[i]));
            _player.Clear();
            var origin = new Pose2(0.0, 0.0, 0.0);
            for (int i = 0; i < Player.Length; i++) _player.Add(WorldBox(origin, Player[i]));
            _mr = mr;
            _noRear = noRear;
            _deepest = -1;
            _failed = null;
            _failedWhy = null;
            _bestCost = double.PositiveInfinity;
            _hasBest = false;
            _usedCount = 0;
            ReleasePlaced();
            Dfs(0, 0.0);
            if (!_hasBest)
            {
                result.Failed = _failed;
                result.FailedWhy = _failedWhy;
                return result;
            }
            result.Fits = true;
            result.Cost = _bestCost;
            for (int i = 0; i < set.Count; i++)
                for (int k = 0; k < _order.Count; k++)
                    if (_best[k].Kind == set[i]) { result.Members.Add(_best[k]); break; }
            return result;
        }

        void Dfs(int k, double cost)
        {
            if (cost >= _bestCost) return;   // bound: costs only grow
            if (k == _order.Count)
            {
                _bestCost = cost;
                _hasBest = true;
                for (int i = 0; i < _placed.Count; i++)
                {
                    Placed p = _placed[i];
                    _best[i] = new Member
                    {
                        Kind = p.Inst.Kind, Slot = p.Slot, Push = p.Push, Pose = p.Pose, Reach = p.Reach,
                        AtHome = p.Slot == p.Inst.Canonical && p.Push == 0.0,
                    };
                }
                return;
            }
            Inst inst = _order[k];
            SeatSlot[] prefs = inst.Prefs;
            for (int si = 0; si < prefs.Length; si++)
            {
                SeatSlot slot = prefs[si];
                if (Used(slot)) continue;
                if (_noRear && SeatSlots.IsRear(slot)) continue;
                for (int pi = 0; pi < Pushes.Length; pi++)
                {
                    double push = Pushes[pi];
                    Pose2 pose = Candidate(inst, slot, push);
                    _nodes++;
                    string why = Check(inst, pose, out double reach, out Placed cand);
                    if (k >= _deepest) { _deepest = k; _failed = inst.Kind; _failedWhy = why; }
                    if (why != null)
                    {
                        if (cand != null) _pool.Push(cand);
                        if (why == "reach") break;   // pushing further out only gets farther
                        continue;
                    }
                    cand.Inst = inst; cand.Slot = slot; cand.Push = push; cand.Pose = pose; cand.Reach = reach;
                    _placed.Add(cand);
                    _used[_usedCount++] = slot;
                    Dfs(k + 1, cost + CostOf(inst, slot, push, _mr));
                    _placed.RemoveAt(_placed.Count - 1);
                    _pool.Push(cand);
                    _usedCount--;
                    break;   // the first feasible push of this slot is the one used
                }
            }
        }

        bool Used(SeatSlot s)
        {
            for (int i = 0; i < _usedCount; i++) if (_used[i] == s) return true;
            return false;
        }

        void ReleasePlaced()
        {
            for (int i = 0; i < _placed.Count; i++) _pool.Push(_placed[i]);
            _placed.Clear();
        }

        /// <summary>seat-planner.mjs check(): null when the candidate is accepted (its world boxes in <paramref name="cand"/>),
        /// else why not ("reach", "wall", "player:knees", "table", "overlap:harp").</summary>
        string Check(Inst inst, in Pose2 pose, out double reach, out Placed cand)
        {
            cand = null;
            ToWorld(pose, inst.PlayX, inst.PlayZ, out double px, out double pz);
            reach = Hypot(px, pz);
            if (reach > inst.ReachMax + 1e-9) return "reach";
            cand = _pool.Count > 0 ? _pool.Pop() : new Placed();
            cand.Boxes.Clear();
            for (int i = 0; i < inst.Boxes.Length; i++) cand.Boxes.Add(WorldBox(pose, inst.Boxes[i]));
            for (int i = 0; i < cand.Boxes.Count; i++)
            {
                FloorObb o = cand.Boxes[i];
                if (!o.Air)
                {
                    if (!InsideRoom(o)) return "wall";
                    for (int p = 0; p < _player.Count; p++) if (Overlaps(o, _player[p], Gap)) return "player:" + PlayerNames[p];
                }
                for (int t = 0; t < _table.Count; t++) if (Overlaps(o, _table[t], Gap)) return "table";
                for (int m = 0; m < _placed.Count; m++)
                {
                    List<FloorObb> q = _placed[m].Boxes;
                    for (int j = 0; j < q.Count; j++)
                    {
                        if (o.Air && q[j].Air) continue;
                        if (Overlaps(o, q[j], Gap)) return "overlap:" + _placed[m].Inst;
                    }
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- joining

        /// <summary>seat-planner.mjs join(set, add, lastPlayed): if set + new fits (and the set has room), it is added; else it
        /// replaces the member whose removal makes it fit, least recently played first (<paramref name="secondsAgo"/>: larger =
        /// longer ago, +inf = never), ties to the smaller instrument (later in Priority); if nothing fits it comes alone.</summary>
        public JoinResult Join(IReadOnlyList<InstrumentKind> set, InstrumentKind add, Func<InstrumentKind, double> secondsAgo = null)
        {
            for (int i = 0; i < set.Count; i++) if (set[i] == add) return new JoinResult(JoinAction.Already, null, PlanSet(set));
            if (set.Count < MaxSet)
            {
                var with = new List<InstrumentKind>(set.Count + 1);
                for (int i = 0; i < set.Count; i++) with.Add(set[i]);
                with.Add(add);
                Plan p = PlanSet(with);
                if (p.Fits) return new JoinResult(JoinAction.Add, null, p);
            }
            var cands = new List<InstrumentKind>(set.Count);
            for (int i = 0; i < set.Count; i++)
            {
                InstrumentKind c = set[i];
                int at = cands.Count;
                while (at > 0 && ReplaceOrder(c, cands[at - 1], secondsAgo) < 0) at--;   // stable insertion sort
                cands.Insert(at, c);
            }
            for (int i = 0; i < cands.Count; i++)
            {
                var next = new List<InstrumentKind>(set.Count);
                for (int k = 0; k < set.Count; k++) next.Add(set[k] == cands[i] ? add : set[k]);
                Plan p = PlanSet(next);
                if (p.Fits) return new JoinResult(JoinAction.Replace, cands[i], p);
            }
            return new JoinResult(JoinAction.Alone, null, PlanSet(new[] { add }));
        }

        /// <summary>The reference's comparator (a, b) =&gt; (ago(b) - ago(a)) || (PRIORITY(b) - PRIORITY(a)): negative puts a first.</summary>
        int ReplaceOrder(InstrumentKind a, InstrumentKind b, Func<InstrumentKind, double> secondsAgo)
        {
            double da = secondsAgo != null ? secondsAgo(a) : double.PositiveInfinity;
            double db = secondsAgo != null ? secondsAgo(b) : double.PositiveInfinity;
            double d = db - da;   // Infinity - Infinity is NaN: falls through to the priority, as in JS
            if (!double.IsNaN(d) && d != 0.0) return d > 0 ? 1 : -1;
            int pa = Get(a)?.Priority ?? 0, pb = Get(b)?.Priority ?? 0;
            return pb - pa;
        }

        /// <summary>seat-planner.mjs allSets(ids): every non-empty subset up to <paramref name="max"/>, by size and then by the
        /// joined ids (ordinal: lower-case ids and commas sort as the reference's localeCompare does).</summary>
        public static List<string[]> AllSets(IReadOnlyList<string> ids, int max = MySetRules.Max)
        {
            var output = new List<string[]>();
            int n = ids.Count;
            for (int mask = 1; mask < 1 << n; mask++)
            {
                int c = 0;
                for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) c++;
                if (c > max) continue;
                var s = new string[c];
                int k = 0;
                for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) s[k++] = ids[i];
                output.Add(s);
            }
            output.Sort((a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(string.Join(",", a), string.Join(",", b)));
            return output;
        }

        // ---------------------------------------------------------------- queries for Layout

        /// <summary>The world boxes of an instrument at a seat-frame pose (all of them; air boxes flagged).</summary>
        public void BoxesAt(InstrumentKind kind, in Pose2 pose, List<FloorObb> into)
        {
            Inst inst = Get(kind);
            if (inst == null) return;
            for (int i = 0; i < inst.Boxes.Length; i++) into.Add(WorldBox(pose, inst.Boxes[i]));
        }

        /// <summary>The side table at <paramref name="table"/> overlaps a member (see Collides).</summary>
        public bool TableCollides(in Pose2 table, IReadOnlyList<KeyValuePair<InstrumentKind, Pose2>> members, double slack = 0.03)
        {
            for (int t = 0; t < TableBoxes.Length; t++)
            {
                FloorObb o = WorldBox(table, TableBoxes[t]);
                for (int k = 0; k < members.Count; k++)
                {
                    Inst q = Get(members[k].Key);
                    if (q == null) continue;
                    for (int j = 0; j < q.Boxes.Length; j++)
                        if (!q.Boxes[j].Air && Overlaps(o, WorldBox(members[k].Value, q.Boxes[j]), -2.0 * slack)) return true;
                }
            }
            return false;
        }

        /// <summary>A saved arrangement pose collides (platform.md 4.7, merge on save): its non-air boxes overlap another
        /// member's or the side table's by more than the footprints' own 3 cm tolerance (each box shrunk by
        /// <paramref name="slack"/> before the test; the descriptor boxes may be up to 3 cm larger than the meshes, and the
        /// placement the player made keeps the real meshes 1 cm apart).</summary>
        public bool Collides(InstrumentKind kind, in Pose2 pose, IReadOnlyList<KeyValuePair<InstrumentKind, Pose2>> others, in Pose2 table, double slack = 0.03)
        {
            Inst inst = Get(kind);
            if (inst == null) return false;
            for (int i = 0; i < inst.Boxes.Length; i++)
            {
                if (inst.Boxes[i].Air) continue;
                FloorObb o = WorldBox(pose, inst.Boxes[i]);
                for (int t = 0; t < TableBoxes.Length; t++) if (Overlaps(o, WorldBox(table, TableBoxes[t]), -2.0 * slack)) return true;
                for (int k = 0; k < others.Count; k++)
                {
                    if (others[k].Key == kind) continue;
                    Inst q = Get(others[k].Key);
                    if (q == null) continue;
                    for (int j = 0; j < q.Boxes.Length; j++)
                    {
                        if (q.Boxes[j].Air) continue;
                        if (Overlaps(o, WorldBox(others[k].Value, q.Boxes[j]), -2.0 * slack)) return true;
                    }
                }
            }
            return false;
        }
    }
}
