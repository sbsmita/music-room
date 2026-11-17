// The placement solver (spec 07 section 8): no instrument within EYE_CLEAR of the eyes, no two floor footprints overlapping
// (the lower priority moves: piano > drums > harp > table), the menu panel out of the instruments' volume, drags that
// stop at contact and slide, and the fitSeat room shift. Rigs are measured from their own renderers as oriented boxes
// in the rig frame (rig.worldToLocal * renderer.localToWorld); the floor footprint is each box's x/z extent with the
// boxes inside another dropped. The resolver moves the real rig transforms (the rigs' groups) and keeps
// every working list as a field, so a resolve allocates nothing once warm.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>One placement rig (piano, drums, harp, table) and everything measured about it.</summary>
    public sealed class RigEntry
    {
        public readonly Rig Rig;
        public Transform Group => Rig.Group;
        public int Prio => Rig.Priority;
        public string Name => Rig.Id.ToString();

        /// <summary>Renderers measured for the footprint and the eye clearance: the rig's grab handles plus the played
        /// parts of its instruments (not every visible mesh of the rig; contact shadows are neither).</summary>
        public readonly List<PartRef> Footprint = new List<PartRef>(64);
        /// <summary>Grab handles (Rig.Handles(), played parts excluded by the module), for GrabTarget.</summary>
        public readonly List<PartRef> Handles = new List<PartRef>(32);
        /// <summary>Every renderer under the group, for the zone (the world box of the group grown by 0.12 m).</summary>
        public readonly List<PartRef> ZoneParts = new List<PartRef>(64);
        /// <summary>The played parts' floor rectangles in the rig frame (keys, heads, strings, bars,
        /// rims): the reach band measures the near edge of the playing surface from these.</summary>
        public readonly List<LocalRect> PlayRects = new List<LocalRect>(32);
        /// <summary>The play centre in the rig frame (placement rigs' centre()); without one the footprint centre is used.</summary>
        public Vector3 CentreLocal;
        public bool HasCentre;

        // measured, rig frame
        public readonly List<Obb> Local = new List<Obb>(64);
        public readonly List<LocalRect> Rects = new List<LocalRect>(32);
        public bool HasBound;
        public LocalRect Bound;

        // posed, world
        public Matrix4x4 M = Matrix4x4.identity;
        public WorldRect[] WR = new WorldRect[16];
        public WorldRect WB;

        // zone (world AABB of every renderer, + ZonePad)
        public Bounds Zone;
        public bool HasZone;

        /// <summary>The riser's floor rectangle in the rig frame (its floor-standing parts' extent; RiserVisuals), worked out
        /// the first time it is needed after each Collect.</summary>
        public LocalRect Riser;
        public bool HasRiser, RiserReady;

        public RigEntry(Rig rig) { Rig = rig; }
        public override string ToString() => Name;
    }

    /// <summary>A renderer with its mesh looked up once (GetComponent never runs on the pinch path).</summary>
    public struct PartRef
    {
        public Renderer R;
        public Mesh Mesh;
        public PartRef(Renderer r)
        {
            R = r;
            Mesh = null;
            if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) Mesh = mf.sharedMesh;
            }
        }
    }

    /// <summary>The menu panel as the placement code sees it.</summary>
    public sealed class PanelInfo
    {
        public Transform Group;
        public bool Shown;
        /// <summary>The panel's renderers (every MeshRenderer under Group, collected by the owner).</summary>
        public readonly List<PartRef> Parts = new List<PartRef>(8);

        public void Collect(List<MeshRenderer> scratch)
        {
            Parts.Clear();
            if (Group == null) return;
            Group.GetComponentsInChildren(true, scratch);
            for (int i = 0; i < scratch.Count; i++) Parts.Add(new PartRef(scratch[i]));
        }
    }

    public sealed class PlacementSolver
    {
        public const float EyeClear = GrabConstants.EyeClear, EyeMargin = GrabConstants.EyeMargin, Gap = GrabConstants.Gap;
        public const float WallSlop = GrabConstants.WallSlop, Step = GrabConstants.Step, MaxPush = GrabConstants.MaxPush;
        public const float SeatHalf = GrabConstants.SeatHalf, SeatFront = GrabConstants.SeatFront, SeatBack = GrabConstants.SeatBack;
        public const float PanelMinEye = GrabConstants.PanelMinEye, PanelMaxRise = GrabConstants.PanelMaxRise;
        /// <summary>WallShift's margin past the slop.</summary>
        public const float WallMargin = 0.002f;
        /// <summary>The containment tolerance when dropping floor rectangles inside another.</summary>
        const float ContainEps = 1e-4f;

        public readonly List<RigEntry> Entries = new List<RigEntry>(8);
        /// <summary>Whether the VR room's walls apply (false in mixed reality).</summary>
        public bool InRoom = true;
        /// <summary>The VR room inside the walls (Unity x, z).</summary>
        public float RoomXMin, RoomXMax, RoomZMin, RoomZMax;
        public readonly PanelInfo Panel = new PanelInfo();
        /// <summary>Renderers to leave out of a measure (a stick held in a hand).</summary>
        public System.Func<Renderer, bool> Skip;
        /// <summary>Warnings the resolver raised, newest last.</summary>
        public readonly List<string> Log = new List<string>(8);
        public int LastFailed { get; private set; }
        public int LastMoves { get; private set; }

        WorldRect _seat;
        bool _hasSeat;
        readonly Vector3[] _corners = new Vector3[8];
        readonly List<RigEntry> _list = new List<RigEntry>(8), _placed = new List<RigEntry>(8), _settled = new List<RigEntry>(8), _others = new List<RigEntry>(8);
        readonly List<Vector2> _dirs = new List<Vector2>(32);
        readonly List<Obb> _panelBoxes = new List<Obb>(8);
        readonly List<MeshRenderer> _meshScratch = new List<MeshRenderer>(16);
        readonly List<float> _rx0 = new List<float>(64), _rx1 = new List<float>(64), _rz0 = new List<float>(64), _rz1 = new List<float>(64);

        public PlacementSolver()
        {
            Bounds b = GrabConstants.RoomBox;
            RoomXMin = b.min.x; RoomXMax = b.max.x; RoomZMin = b.min.z; RoomZMax = b.max.z;
        }

        public RigEntry Entry(Rig rig)
        {
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Rig == rig) return Entries[i];
            return null;
        }

        public RigEntry Entry(RigId id)
        {
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Rig.Id == id) return Entries[i];
            return null;
        }

        /// <summary>In the scene and shown (the group parented and active).</summary>
        public static bool InScene(RigEntry r) => r != null && r.Rig.InScene && r.Group != null && r.Group.gameObject.activeInHierarchy;

        /// <summary>In the scene with a footprint.</summary>
        public static bool Active(RigEntry r) => InScene(r) && r.HasBound;

        // ---------------------------------------------------------------- measuring

        /// <summary>Whether a renderer shows (visible up to the rig, not skipped).</summary>
        public bool Shows(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy && (Skip == null || !Skip(r));

        /// <summary>Shows and still belongs to the rig: a part its module moved out of the group since it was collected
        /// (a stick the Drums module reparented into a player's hand) is not measured.</summary>
        public bool Shows(Renderer r, Transform group) => Shows(r) && group != null && r.transform.IsChildOf(group);

        /// <summary>The part's own geometry box and the matrix from it to the world: mesh bounds and the renderer's matrix
        /// (the local bounds and their world matrix); renderers without a mesh fall back to their world AABB.</summary>
        public static bool GeometryBox(in PartRef p, out Bounds local, out Matrix4x4 toWorld)
        {
            Renderer r = p.R;
            if (p.Mesh != null)
            {
                local = p.Mesh.bounds;
                toWorld = r.localToWorldMatrix;
                return p.Mesh.vertexCount > 0;
            }
            if (r is MeshRenderer) { local = default; toWorld = Matrix4x4.identity; return false; }
            if (r is SkinnedMeshRenderer smr)
            {
                local = smr.localBounds;
                toWorld = smr.rootBone != null ? smr.rootBone.localToWorldMatrix : smr.localToWorldMatrix;
                return local.size.sqrMagnitude > 0f;
            }
            local = r.bounds;
            toWorld = Matrix4x4.identity;
            return local.size.sqrMagnitude > 0f;
        }

        /// <summary>An oriented box per shown part in the rig frame and the floor rectangles they
        /// cover, the ones inside another dropped, and their bound.</summary>
        public RigEntry Measure(RigEntry r)
        {
            r.Local.Clear();
            r.Rects.Clear();
            r.HasBound = false;
            if (r.Group == null) return r;
            Matrix4x4 mi = r.Group.worldToLocalMatrix;
            _rx0.Clear(); _rx1.Clear(); _rz0.Clear(); _rz1.Clear();
            for (int i = 0; i < r.Footprint.Count; i++)
            {
                PartRef part = r.Footprint[i];
                if (!Shows(part.R, r.Group) || !GeometryBox(part, out Bounds b, out Matrix4x4 tw)) continue;
                Obb box = Obb.FromBounds(b, mi * tw);
                r.Local.Add(box);
                box.Corners(_corners);
                float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 q = _corners[k];
                    if (q.x < x0) x0 = q.x;
                    if (q.x > x1) x1 = q.x;
                    if (q.z < z0) z0 = q.z;
                    if (q.z > z1) z1 = q.z;
                }
                _rx0.Add(x0); _rx1.Add(x1); _rz0.Add(z0); _rz1.Add(z1);
            }
            // Drop a rectangle inside another one (a drum head inside its shell, a key inside the keybed); of two equal
            // rectangles the first is kept.
            int n = _rx0.Count;
            float bx0 = float.PositiveInfinity, bx1 = float.NegativeInfinity, bz0 = float.PositiveInfinity, bz1 = float.NegativeInfinity;
            for (int a = 0; a < n; a++)
            {
                bool drop = false;
                for (int b = 0; b < n && !drop; b++)
                {
                    if (b == a || !Contains(b, a)) continue;
                    if (b < a || !Contains(a, b)) drop = true;
                }
                if (drop) continue;
                r.Rects.Add(new LocalRect((_rx0[a] + _rx1[a]) / 2f, (_rz0[a] + _rz1[a]) / 2f, (_rx1[a] - _rx0[a]) / 2f, (_rz1[a] - _rz0[a]) / 2f));
                bx0 = Mathf.Min(bx0, _rx0[a]); bx1 = Mathf.Max(bx1, _rx1[a]);
                bz0 = Mathf.Min(bz0, _rz0[a]); bz1 = Mathf.Max(bz1, _rz1[a]);
            }
            if (r.Rects.Count > 0)
            {
                r.HasBound = true;
                r.Bound = new LocalRect((bx0 + bx1) / 2f, (bz0 + bz1) / 2f, (bx1 - bx0) / 2f, (bz1 - bz0) / 2f);
            }
            if (r.WR.Length < r.Rects.Count) r.WR = new WorldRect[Mathf.NextPowerOfTwo(r.Rects.Count)];
            return r;
        }

        bool Contains(int b, int a) =>
            _rx0[b] <= _rx0[a] + ContainEps && _rx1[b] >= _rx1[a] - ContainEps && _rz0[b] <= _rz0[a] + ContainEps && _rz1[b] >= _rz1[a] - ContainEps;

        /// <summary>Every rig in the scene (before a drag: a stick may have been picked up).</summary>
        public void MeasureAll()
        {
            for (int i = 0; i < Entries.Count; i++) if (InScene(Entries[i])) Measure(Entries[i]);
        }

        /// <summary>The rig's world matrix as it stands and its floor rectangles in the world.</summary>
        public RigEntry Pose(RigEntry r)
        {
            r.M = r.Group.localToWorldMatrix;
            for (int i = 0; i < r.Rects.Count; i++) FloorRects.ToWorld(r.Rects[i], r.M, ref r.WR[i]);
            if (r.HasBound) FloorRects.ToWorld(r.Bound, r.M, ref r.WB);
            return r;
        }

        /// <summary>The deepest contact between two posed footprints (rectangles grown by gap).</summary>
        public static bool Touch(RigEntry a, RigEntry b, float gap, out float depth, out float nx, out float nz)
        {
            depth = nx = nz = 0f;
            if (!a.HasBound || !b.HasBound || !FloorRects.Overlap(a.WB, b.WB, gap)) return false;
            bool any = false;
            for (int i = 0; i < a.Rects.Count; i++)
                for (int j = 0; j < b.Rects.Count; j++)
                {
                    if (!FloorRects.Overlap(a.WR[i], b.WR[j], gap, out float d, out float x, out float z)) continue;
                    if (!any || d > depth) { depth = d; nx = x; nz = z; any = true; }
                }
            return any;
        }

        public static bool Touch(RigEntry a, RigEntry b, float gap) => Touch(a, b, gap, out _, out _, out _);

        /// <summary>Nearest distance from the eyes to any part of the posed rig (measured in
        /// the rig frame, so a rig scaled by a page turn reads its unscaled distance).</summary>
        public static float EyeDistance(RigEntry r, Vector3 eye)
        {
            Vector3 w = r.M.inverse.MultiplyPoint3x4(eye);
            float d = float.PositiveInfinity;
            for (int i = 0; i < r.Local.Count; i++) d = Mathf.Min(d, r.Local[i].DistanceTo(w));
            return d;
        }

        /// <summary>The posed footprint's floor extent.</summary>
        public static void Extent(RigEntry r, out float x0, out float x1, out float z0, out float z1)
        {
            x0 = z0 = float.PositiveInfinity;
            x1 = z1 = float.NegativeInfinity;
            for (int i = 0; i < r.Rects.Count; i++)
            {
                WorldRect w = r.WR[i];
                float ex = w.Hu * Mathf.Abs(w.Ux) + w.Hv * Mathf.Abs(w.Vx), ez = w.Hu * Mathf.Abs(w.Uz) + w.Hv * Mathf.Abs(w.Vz);
                x0 = Mathf.Min(x0, w.X - ex); x1 = Mathf.Max(x1, w.X + ex);
                z0 = Mathf.Min(z0, w.Z - ez); z1 = Mathf.Max(z1, w.Z + ez);
            }
        }

        public bool Inside(RigEntry r)
        {
            if (!InRoom) return true;
            Extent(r, out float x0, out float x1, out float z0, out float z1);
            float e = WallSlop;
            return x0 >= RoomXMin - e && x1 <= RoomXMax + e && z0 >= RoomZMin - e && z1 <= RoomZMax + e;
        }

        /// <summary>The least shift that brings a floor extent inside the room (centred on an axis
        /// it is too long for); nothing within WALL_SLOP.</summary>
        public Vector2 WallShift(float x0, float x1, float z0, float z1) =>
            new Vector2(Axis(x0, x1, RoomXMin, RoomXMax), Axis(z0, z1, RoomZMin, RoomZMax));

        static float Axis(float lo, float hi, float min, float max)
        {
            if (hi - lo > max - min) return (min + max) / 2f - (lo + hi) / 2f;
            if (lo < min - WallSlop) return min - lo + WallMargin;
            if (hi > max + WallSlop) return max - hi - WallMargin;
            return 0f;
        }

        /// <summary>In the VR room, in off the walls by the least shift.</summary>
        public void ClearWalls(RigEntry r)
        {
            if (!InRoom) return;
            Pose(r);
            Extent(r, out float x0, out float x1, out float z0, out float z1);
            Vector2 s = WallShift(x0, x1, z0, z1);
            if (s.x != 0f || s.y != 0f)
            {
                Vector3 p = r.Group.position;
                r.Group.position = new Vector3(p.x + s.x, p.y, p.z + s.y);
                Pose(r);
            }
        }

        /// <summary>The shift that brings every placed rig inside the VR room, or false. The seat
        /// moves by it (fitSeat) so the arrangement keeps its shape away from a wall.</summary>
        public bool RoomShift(out Vector2 shift)
        {
            shift = default;
            bool any = false;
            float u0 = 0f, u1 = 0f, v0 = 0f, v1 = 0f;
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry r = Entries[i];
                if (!InScene(r)) continue;
                Pose(Measure(r));
                if (!r.HasBound) continue;
                Extent(r, out float x0, out float x1, out float z0, out float z1);
                if (!any) { u0 = x0; u1 = x1; v0 = z0; v1 = z1; any = true; }
                else { u0 = Mathf.Min(u0, x0); u1 = Mathf.Max(u1, x1); v0 = Mathf.Min(v0, z0); v1 = Mathf.Max(v1, z1); }
            }
            if (!any) return false;
            shift = WallShift(u0, u1, v0, v1);
            return shift.x != 0f || shift.y != 0f;
        }

        /// <summary>fitSeat's dry run with the fix of spec 07 section 8 (the rigs already at their seat homes, unsettled):
        /// clear the eyes of each placed rig in priority order with the walls off, then measure the room shift of that
        /// arrangement. Without the clearEye step a kit pushed back for the eyes would land in the wall again and the
        /// resolver would slide it sideways (the single-mode cliff, spec 07 section 8); with it the seat moves back instead.</summary>
        public bool FitShift(Vector3 eye, float yawRad, out Vector2 shift)
        {
            bool inRoom = InRoom;
            InRoom = false;
            SeatRect(eye, yawRad);
            _placed.Clear();
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry r = Entries[i];
                if (!InScene(r)) continue;
                Measure(r);
                if (r.HasBound) InsertByPrio(_placed, r);
            }
            for (int i = 0; i < _placed.Count; i++)
            {
                Pose(_placed[i]);
                ClearEye(_placed[i], eye);
            }
            InRoom = true;
            bool any = RoomShift(out shift);
            InRoom = inRoom;
            return any;
        }

        // ---------------------------------------------------------------- after a placement

        /// <summary>Eye clearance for each rig, then overlaps in priority order. <paramref name="pinned"/>
        /// keeps its spot (a piano the player put on a real table); <paramref name="skip"/> is left out (the outgoing
        /// instrument of a page turn). Returns true when every rig ended up free.</summary>
        public bool Resolve(Vector3 eye, float yawRad, RigEntry pinned = null, RigEntry skip = null)
        {
            SeatRect(eye, yawRad);
            _list.Clear();
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry r = Entries[i];
                if (r == skip || !InScene(r)) continue;
                _list.Add(r);
                Measure(r);
            }
            _placed.Clear();
            for (int i = 0; i < _list.Count; i++) if (Active(_list[i])) InsertByPrio(_placed, _list[i]);
            _settled.Clear();
            int failed = 0, moves = 0;
            for (int i = 0; i < _placed.Count; i++)
            {
                RigEntry r = _placed[i];
                Pose(r);
                Vector3 from = r.Group.position;
                bool fixedRig = r == pinned;
                if (!fixedRig)
                {
                    ClearEye(r, eye);
                    ClearWalls(r);
                }
                if (!fixedRig && !Free(r, _settled, eye, Gap)) FindSpot(r, _settled, eye);
                if (!Free(r, _settled, eye, 0f))
                {
                    failed++;
                    string msg = "[Layout] " + r.Name + " could not be cleared of the others, the eyes and the walls";
                    AddLog(msg);
                    Debug.LogWarning(msg);
                }
                if ((r.Group.position - from).sqrMagnitude > 1e-10f) moves++;
                _settled.Add(r);
            }
            LastFailed = failed;
            LastMoves = moves;
            ClearPanel(eye, _placed);
            return failed == 0;
        }

        static void InsertByPrio(List<RigEntry> list, RigEntry r)
        {
            int at = list.Count;
            while (at > 0 && list[at - 1].Prio > r.Prio) at--;   // stable: equal priorities keep the registry order
            list.Insert(at, r);
        }

        void AddLog(string msg)
        {
            if (Log.Count >= 32) Log.RemoveAt(0);
            Log.Add(msg);
        }

        /// <summary>The seated player's own floor space (torso and bench) about the head,
        /// along its heading: a rig the resolver moves never lands on it.</summary>
        public WorldRect SeatRect(Vector3 eye, float yawRad)
        {
            float fx = Mathf.Sin(yawRad), fz = Mathf.Cos(yawRad);
            float mid = (SeatFront - SeatBack) / 2f;
            _seat = new WorldRect { X = eye.x + fx * mid, Z = eye.z + fz * mid, Ux = fz, Uz = -fx, Vx = fx, Vz = fz, Hu = SeatHalf, Hv = (SeatFront + SeatBack) / 2f };
            _hasSeat = true;
            return _seat;
        }

        public bool OnSeat(RigEntry r, float gap)
        {
            if (!_hasSeat || !r.HasBound || !FloorRects.Overlap(r.WB, _seat, gap)) return false;
            for (int i = 0; i < r.Rects.Count; i++) if (FloorRects.Overlap(r.WR[i], _seat, gap)) return true;
            return false;
        }

        /// <summary>No footprint contact with <paramref name="others"/>, clear of the eyes, inside the room
        /// and (for a spot the resolver picks) off the player's seat.</summary>
        public bool Free(RigEntry r, List<RigEntry> others, Vector3 eye, float gap, bool seat = false)
        {
            Pose(r);
            if (!Inside(r)) return false;
            for (int i = 0; i < others.Count; i++) if (others[i] != r && Touch(r, others[i], gap)) return false;
            if (seat && OnSeat(r, gap)) return false;
            return EyeDistance(r, eye) >= EyeClear - 1e-6f;
        }

        /// <summary>The play centre in the world (the rig must be posed).</summary>
        public static Vector3 CentreWorld(RigEntry r) =>
            r.HasCentre ? r.M.MultiplyPoint3x4(r.CentreLocal) : new Vector3(r.WB.X, 0f, r.WB.Z);

        /// <summary>The floor direction from the eyes to the rig's play centre.</summary>
        public static Vector3 PlayDir(RigEntry r, Vector3 eye)
        {
            Vector3 d = CentreWorld(r) - eye;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) d = new Vector3(r.WB.X - eye.x, 0f, r.WB.Z - eye.z);
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;   // the source frame's forward (0, 0, -1), z negated
            return d.normalized;
        }

        /// <summary>Straight back from the player, by the shortfall plus the margin, until the eyes
        /// are clear (at most six pushes).</summary>
        public void ClearEye(RigEntry r, Vector3 eye)
        {
            for (int k = 0; k < 6; k++)
            {
                Pose(r);
                float d = EyeDistance(r, eye);
                if (d >= EyeClear) return;
                Vector3 dir = PlayDir(r, eye);
                r.Group.position += dir * (EyeClear - d + EyeMargin);
            }
            Pose(r);
        }

        /// <summary>The shortest move that frees the rig, tried first away from whatever it touches,
        /// then away from the player, then round the compass (in STEP steps up to MAX_PUSH, staying off the seat), then
        /// backed off in five bisections to the nearest free spot along the winning direction.</summary>
        public bool FindSpot(RigEntry r, List<RigEntry> others, Vector3 eye)
        {
            Transform g = r.Group;
            Vector3 basePos = g.position;
            _dirs.Clear();
            for (int i = 0; i < others.Count; i++)
            {
                RigEntry o = others[i];
                if (!Touch(r, o, Gap, out _, out float nx, out float nz)) continue;
                _dirs.Add(new Vector2(nx, nz));
                _dirs.Add(new Vector2(r.WB.X - o.WB.X, r.WB.Z - o.WB.Z).normalized);
            }
            Vector3 pd = PlayDir(r, eye);
            _dirs.Add(new Vector2(pd.x, pd.z));
            // The compass (cos t, sin t) in the right-handed source x/z is (cos t, -sin t) in Unity's: same directions, same order.
            for (int i = 0; i < 16; i++)
            {
                float t = i / 16f * Mathf.PI * 2f;
                _dirs.Add(new Vector2(Mathf.Cos(t), -Mathf.Sin(t)));
            }
            int maxSteps = Mathf.FloorToInt(MaxPush / Step + 1e-4f);
            int bestStep = -1;
            Vector2 bestDir = default;
            for (int k = 0; k < _dirs.Count; k++)
            {
                Vector2 d = _dirs[k];
                if (!(d.sqrMagnitude > 0.5f)) continue;
                for (int i = 1; i <= maxSteps && (bestStep < 0 || i < bestStep); i++)
                {
                    float s = Step * i;
                    g.position = new Vector3(basePos.x + d.x * s, basePos.y, basePos.z + d.y * s);
                    if (Free(r, others, eye, Gap, true))
                    {
                        bestStep = i;
                        bestDir = d;
                        break;
                    }
                }
            }
            if (bestStep >= 0)
            {
                float best = Step * bestStep;
                float lo = Mathf.Max(0f, best - Step), hi = best;
                for (int i = 0; i < 5; i++)
                {
                    float mid = (lo + hi) / 2f;
                    g.position = new Vector3(basePos.x + bestDir.x * mid, basePos.y, basePos.z + bestDir.y * mid);
                    if (Free(r, others, eye, Gap, true)) hi = mid;
                    else lo = mid;
                }
                g.position = new Vector3(basePos.x + bestDir.x * hi, basePos.y, basePos.z + bestDir.y * hi);
            }
            else g.position = basePos;
            Pose(r);
            return bestStep >= 0;
        }

        // ---------------------------------------------------------------- the menu panel

        /// <summary>Out of the instruments' volume, pulled toward the player (never closer than
        /// PANEL_MIN_EYE to the eyes) or lifted, whichever is the smaller move, still facing the player (local +z away from
        /// the eyes, the Unity facing rule). Returns true when it moved clear.</summary>
        public bool ClearPanel(Vector3 eye, List<RigEntry> placed = null)
        {
            if (Panel.Group == null || !Panel.Shown) return false;
            if (placed == null)
            {
                _others.Clear();
                for (int i = 0; i < Entries.Count; i++) if (Active(Entries[i])) _others.Add(Entries[i]);
                placed = _others;
            }
            Transform g = Panel.Group;
            Panel.Collect(_meshScratch);   // the panel's meshes as they are now (it may have rebuilt its plates)
            if (!PanelHits(placed)) return false;
            Vector3 basePos = g.position;
            Vector3 toward = new Vector3(eye.x - basePos.x, 0f, eye.z - basePos.z);
            float room = toward.magnitude - PanelMinEye;
            toward = toward.normalized;
            bool done = false;
            for (int i = 1; !done; i++)
            {
                float s = Step * i;
                if (s > PanelMaxRise + 1e-6f) break;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = basePos;
                    if (k == 0 && s <= room) p += toward * s;
                    else if (k == 1) p.y += s;
                    else if (k == 2 && s / 2f <= room) { p += toward * (s / 2f); p.y += s / 2f; }
                    else continue;
                    g.SetPositionAndRotation(p, Quaternion.Euler(0f, Mathf.Atan2(p.x - eye.x, p.z - eye.z) * Mathf.Rad2Deg, 0f));
                    if (!PanelHits(placed)) { done = true; break; }
                }
            }
            if (!done)
            {
                g.position = basePos;
                const string msg = "[Layout] the menu panel could not be cleared of the instruments";
                AddLog(msg);
                Debug.LogWarning(msg);
            }
            return done;
        }

        /// <summary>The panel's boxes in the world (every visible mesh of the panel).</summary>
        public List<Obb> PanelBoxes()
        {
            _panelBoxes.Clear();
            if (Panel.Group == null) return _panelBoxes;
            List<PartRef> list = Panel.Parts;
            for (int i = 0; i < list.Count; i++)
            {
                PartRef p = list[i];
                if (p.R == null || !p.R.enabled || !p.R.gameObject.activeInHierarchy) continue;
                if (GeometryBox(p, out Bounds b, out Matrix4x4 tw)) _panelBoxes.Add(Obb.FromBounds(b, tw));
            }
            return _panelBoxes;
        }

        /// <summary>Whether any panel box meets any box of a placed rig.</summary>
        public bool PanelHits(List<RigEntry> placed)
        {
            List<Obb> pb = PanelBoxes();
            if (pb.Count == 0) return false;
            for (int i = 0; i < placed.Count; i++)
            {
                RigEntry r = placed[i];
                Pose(r);
                for (int k = 0; k < r.Local.Count; k++)
                {
                    Obb w = r.Local[k].Moved(r.M);
                    for (int j = 0; j < pb.Count; j++) if (Obb.Intersect(pb[j], w)) return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- while a rig is dragged

        bool Blocked(RigEntry r)
        {
            Pose(r);
            for (int i = 0; i < _others.Count; i++) if (Touch(r, _others[i], 0f)) return true;
            return false;
        }

        static void SetXZ(Transform g, float x, float z)
        {
            Vector3 p = g.position;
            g.position = new Vector3(x, p.y, z);
        }

        static void SetYaw(Transform g, float yawRad) => g.rotation = LayoutMath.YawRotation(yawRad);

        /// <summary>The rig is moved to (tx, tz, tyaw) where the hand wants it unless that runs it
        /// into another rig. A rig that was already touching something moves freely (out of it); a turn into contact is
        /// undone; a move into contact marches in 1 cm steps to the first contact, bisects it six times and slides (full,
        /// tangent, x or z, keeping the best gain). The result is left on the transform; <paramref name="yaw"/> returns the
        /// heading it kept. Returns true when the hand's pose was limited.</summary>
        public bool Constrain(RigEntry r, float prevX, float prevZ, float prevYaw, float tx, float tz, float tyaw, out float yaw)
        {
            yaw = tyaw;
            Transform g = r != null ? r.Group : null;
            if (g == null) return false;
            if (!r.HasBound)
            {
                SetXZ(g, tx, tz); SetYaw(g, tyaw);
                return false;
            }
            _others.Clear();
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry o = Entries[i];
                if (o == r || !Active(o)) continue;
                Pose(o);
                _others.Add(o);
            }
            SetXZ(g, prevX, prevZ);
            SetYaw(g, prevYaw);
            if (Blocked(r))
            {
                SetXZ(g, tx, tz);
                SetYaw(g, tyaw);
                Pose(r);
                return false;
            }
            bool limited = false;
            yaw = prevYaw;
            if (tyaw != prevYaw)
            {
                SetYaw(g, tyaw);
                if (Blocked(r)) { SetYaw(g, prevYaw); limited = true; }
                else yaw = tyaw;
            }
            float dx = tx - prevX, dz = tz - prevZ;
            if (dx * dx + dz * dz < 1e-12f) { Pose(r); return limited; }
            SetXZ(g, tx, tz);
            if (!Blocked(r)) { Pose(r); return limited; }
            // The contact normal at the target, to slide along.
            bool hasN = false;
            float nDepth = 0f, nnx = 0f, nnz = 0f;
            for (int i = 0; i < _others.Count; i++)
            {
                if (!Touch(r, _others[i], 0f, out float d, out float x, out float z)) continue;
                if (!hasN || d > nDepth) { nDepth = d; nnx = x; nnz = z; hasN = true; }
            }
            float bestX = prevX, bestZ = prevZ, bestGain = 0f;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            for (int c = 0; c < 4; c++)
            {
                float cx, cz;
                if (c == 0) { cx = dx; cz = dz; }
                else if (c == 1)
                {
                    if (!hasN) continue;
                    float into = dx * nnx + dz * nnz;
                    if (!(into < 0f)) continue;
                    cx = dx - into * nnx; cz = dz - into * nnz;
                }
                else if (c == 2) { cx = dx; cz = 0f; }
                else { cx = 0f; cz = dz; }
                float cl = Mathf.Sqrt(cx * cx + cz * cz);
                if (cl < 1e-9f) continue;
                // March from the free start in 1 cm steps to the first contact, then bisect it.
                int steps = Mathf.Max(1, Mathf.CeilToInt(cl / 0.01f));
                float ok = 0f;
                for (int i = 1; i <= steps; i++)
                {
                    float f = (float)i / steps;
                    SetXZ(g, prevX + cx * f, prevZ + cz * f);
                    if (Blocked(r)) break;
                    ok = f;
                }
                if (ok < 1f)
                {
                    float lo = ok, hi = Mathf.Min(1f, ok + 1f / steps);
                    for (int i = 0; i < 6; i++)
                    {
                        float mid = (lo + hi) / 2f;
                        SetXZ(g, prevX + cx * mid, prevZ + cz * mid);
                        if (Blocked(r)) hi = mid;
                        else lo = mid;
                    }
                    ok = lo;
                }
                float gain = (cx * ok * dx + cz * ok * dz) / len;
                if (gain > bestGain + 1e-6f) { bestX = prevX + cx * ok; bestZ = prevZ + cz * ok; bestGain = gain; }
            }
            SetXZ(g, bestX, bestZ);
            Pose(r);
            return true;
        }

        /// <summary>Whether the rig's footprint touches another placed rig's (a drag that pushed through).</summary>
        public bool TouchingAny(RigEntry r)
        {
            if (!Active(r)) return false;
            Pose(r);
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry o = Entries[i];
                if (o == r || !Active(o)) continue;
                Pose(o);
                if (Touch(r, o, 0f)) return true;
            }
            return false;
        }

        /// <summary>A rig just let go of, if it ended up in another rig, in the player's face or in a
        /// wall, alone moves the shortest way out. Returns the distance it moved (0 when it was already clear). The other
        /// rigs are posed after measuring, so their world rectangles always match their measured ones.</summary>
        public float SettleOne(RigEntry r, Vector3 eye, float yawRad) => SettleOne(r, eye, yawRad, false);

        /// <summary>SettleOne; <paramref name="offSeat"/> also moves it off the player's seat (a grab handle's drop).</summary>
        public float SettleOne(RigEntry r, Vector3 eye, float yawRad, bool offSeat)
        {
            if (!InScene(r)) return 0f;
            SeatRect(eye, yawRad);
            for (int i = 0; i < Entries.Count; i++) if (InScene(Entries[i])) Measure(Entries[i]);
            if (!Active(r)) return 0f;
            _settled.Clear();
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry o = Entries[i];
                if (o == r || !Active(o)) continue;
                Pose(o);
                _settled.Add(o);
            }
            if (Free(r, _settled, eye, 0f, offSeat)) return 0f;
            Vector3 from = r.Group.position;
            ClearEye(r, eye);
            ClearWalls(r);
            if (!Free(r, _settled, eye, 0f, offSeat)) FindSpot(r, _settled, eye);
            return Vector3.Distance(from, r.Group.position);
        }

        // ---------------------------------------------------------------- checks (for the tests)

        /// <summary>Every pair of placed rigs whose floor footprints touch (gap 0).</summary>
        public int Overlaps2D()
        {
            int n = 0;
            for (int i = 0; i < Entries.Count; i++) if (InScene(Entries[i])) Pose(Measure(Entries[i]));
            for (int i = 0; i < Entries.Count; i++)
                for (int j = i + 1; j < Entries.Count; j++)
                    if (Active(Entries[i]) && Active(Entries[j]) && Touch(Entries[i], Entries[j], 0f)) n++;
            return n;
        }

        /// <summary>Every pair of rigs (and the shown panel) with at least one pair of intersecting boxes.</summary>
        public int Overlaps3D()
        {
            int n = 0;
            for (int i = 0; i < Entries.Count; i++) if (InScene(Entries[i])) Pose(Measure(Entries[i]));
            for (int i = 0; i < Entries.Count; i++)
            {
                RigEntry a = Entries[i];
                if (!Active(a)) continue;
                for (int j = i + 1; j < Entries.Count; j++)
                {
                    RigEntry b = Entries[j];
                    if (!Active(b)) continue;
                    if (BoxesMeet(a, b)) n++;
                }
                if (Panel.Shown && Panel.Group != null)
                {
                    Panel.Collect(_meshScratch);
                    List<Obb> pb = PanelBoxes();
                    bool hit = false;
                    for (int k = 0; k < a.Local.Count && !hit; k++)
                    {
                        Obb w = a.Local[k].Moved(a.M);
                        for (int q = 0; q < pb.Count && !hit; q++) hit = Obb.Intersect(pb[q], w);
                    }
                    if (hit) n++;
                }
            }
            return n;
        }

        static bool BoxesMeet(RigEntry a, RigEntry b)
        {
            for (int i = 0; i < a.Local.Count; i++)
            {
                Obb wa = a.Local[i].Moved(a.M);
                for (int j = 0; j < b.Local.Count; j++) if (Obb.Intersect(wa, b.Local[j].Moved(b.M))) return true;
            }
            return false;
        }

        /// <summary>The eye distance of a rig at the current pose (+inf without a footprint).</summary>
        public float EyeReport(RigEntry r, Vector3 eye)
        {
            if (!InScene(r)) return float.PositiveInfinity;
            Pose(Measure(r));
            return r.HasBound ? EyeDistance(r, eye) : float.PositiveInfinity;
        }
    }
}
