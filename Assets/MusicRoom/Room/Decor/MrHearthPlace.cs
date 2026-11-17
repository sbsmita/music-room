// Where the mixed reality fireplace stands (Decor/MrFireplace.cs; pure maths, no Unity objects but the inputs' value
// types, so EditMode tests drive it with made-up rooms). The hearth is World/HearthBuilder's at Scale (0.85: real rooms are
// smaller than the music room): a box 1.62 m across, 0.64 m deep, 1.08 m high (its mantel's top), its back to a wall; in a
// tight room a smaller one (Sizes: 0.8, then 0.65 of that) rather than none or one standing in the furniture.
//   - With the room's scene model (Core's IRealWalls, from MR): against a real wall in front of the player. Every wall
//     facing the player, at least MinWallHeight high and wide enough, is sampled every Step along its length; a spot is
//     free when the hearth's box stays clear of what the scene model knows stands there (windows, doors and wall art as
//     thin boxes, every furniture volume: BlockMargin), of the instruments on display (RigMargin, their visible boxes),
//     of the paintings hung on that wall (FrameMargin) and keeps HeadClear between the player's head and its nearest
//     point. Each free stretch of wall gives its spots at least KeepFromObstacle in from its ends (the hearth sits in the
//     free space, not jammed against a door), scored by the angle off the player's view, a comfortable distance (NearBest
//     .. FarBest), the distance from the stretch's middle, and whether an instrument hides the fire (the sight line from
//     the head to the upper flames through one of an instrument's parts). Nothing more than MaxAngle off the view.
//   - No free wall (no scene model, or a real room's walls lined with furniture, windows and doors): free-standing on the
//     floor, facing the player, FallbackDistance in front (its middle) when that is free, else the best free spot of
//     FallbackYaw x FallbackDist up to 90 degrees off the view (scored as above, plus the distance off FallbackDistance),
//     inside the walls, clear of furniture, instruments and the head as before.
//   - Order: in sight on a wall, on a wall hidden, on the floor in front - each at full size, then smaller; then further
//     round (a small room's free space may be behind the player: better than in the furniture); then the least bad
//     (inside the walls, clear of the instruments and the head, standing in the fewest blocks; Rough).
// The floor is the caller's (IMixedReality.FloorHeight through RoomSnowModel.MrFloor, as the room snow). Setup-time code:
// it allocates nothing itself but its Scene's lists are the caller's.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    public static class MrHearthPlace
    {
        /// <summary>The VR room's fireplace at this size (the hearth root's scale at Size 1).</summary>
        public const float Scale = 0.85f;
        /// <summary>The sizes tried, largest first (of Scale).</summary>
        public static readonly float[] Sizes = { 1f, 0.8f, 0.65f };
        /// <summary>The hearth's box in its own frame at Size 1 (x across, y up, z out from the wall; HearthBuilder's at Scale).</summary>
        public const float HalfWidth = 0.95f * Scale, Back = -0.015f * Scale, Front = 0.735f * Scale, Height = 1.27f * Scale;
        /// <summary>The hearth's back stands this far out from the wall (its stone 2 cm clear).</summary>
        public const float WallGap = 0.035f;
        /// <summary>Clearances (m): the head to the hearth's nearest point; scene-model blocks; instruments; paintings; wall ends.</summary>
        public const float HeadClear = 1.0f, BlockMargin = 0.05f, RigMargin = 0.15f, FrameMargin = 0.08f, EdgeMargin = 0.1f;
        /// <summary>Sampling along a wall (m) and how far a spot keeps in from the ends of its free stretch.</summary>
        public const float Step = 0.05f, KeepFromObstacle = 0.3f;
        public const float MinWallHeight = 1.4f;
        /// <summary>Nothing more than this off the player's view (deg) in front; further round only when nothing in front is free.</summary>
        public const float MaxAngle = 100f;
        /// <summary>The comfortable distance (m) from the head to the hearth's middle.</summary>
        public const float NearBest = 1.8f, FarBest = 3.6f;
        /// <summary>What the player should see: the upper flames' height above the floor, the sight lines' spread across, and
        /// how much of an instrument's part may be grazed (its box shrunk by this: a line over the top of a drum is not hidden).</summary>
        public const float SightY = 0.55f, SightSide = 0.5f, SightGraze = 0.03f;
        public const float FallbackDistance = 2.5f;
        public static readonly float[] FallbackYaw = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f, 90f, -90f,
            110f, -110f, 130f, -130f, 155f, -155f, 180f };
        public static readonly float[] FallbackDist = { FallbackDistance, 2.2f, 2.8f, 1.9f, 3.2f, 1.6f, 1.4f };
        /// <summary>Score weights: per metre outside NearBest .. FarBest, per metre off the stretch's middle, a side sight line
        /// hidden, the fire itself hidden, per metre a floor spot is off FallbackDistance.</summary>
        public const float FarCost = 12f, NearCost = 30f, OffMiddleCost = 4f, SideHiddenCost = 15f, HiddenCost = 80f, FallbackDistCost = 4f;

        public struct Spot
        {
            /// <summary>The hearth's root: on the floor, its back at the wall, rotation's forward into the room.</summary>
            public Pose Pose;
            public bool Found, OnWall, Hidden;
            /// <summary>No free spot anywhere: the least bad (it may stand in furniture the scene model knows).</summary>
            public bool Rough;
            public int Wall;
            /// <summary>Of the full hearth (Sizes); the root's scale is Scale x Size.</summary>
            public float Size;
            public float Score, Angle, Distance;
            public Vector3 Inward => Pose.rotation * Vector3.forward;
            /// <summary>The middle of the fire (the crackle's source, the light).</summary>
            public Vector3 FirePoint => Pose.position + Pose.rotation * (new Vector3(0f, 0.5f, 0.45f) * (Scale * Size));
            public override string ToString() => $"{(OnWall ? "wall " + Wall : "floor")} at {Pose.position:F2}, {Angle:F0} deg, {Distance:F2} m, size {Size:F2}{(Hidden ? ", hidden" : "")}{(Rough ? ", rough" : "")}";
        }

        /// <summary>What the placement looks at, gathered by the caller.</summary>
        public sealed class Scene
        {
            public Vector3 Head, Forward;
            public float Floor;
            public readonly List<RealWall> Walls = new List<RealWall>(16);
            public readonly List<RealWallBlock> Blocks = new List<RealWallBlock>(32);
            /// <summary>The instruments on display (world boxes).</summary>
            public readonly List<Bounds> Rigs = new List<Bounds>(12);
            /// <summary>The instruments' visible parts one by one (world boxes), for the sight lines (a drum kit's box would hide
            /// half the room; its drums and cymbals do not). Empty: the Rigs boxes.</summary>
            public readonly List<Bounds> Sight = new List<Bounds>(96);
            /// <summary>The paintings hung on the real walls (world boxes).</summary>
            public readonly List<Bounds> Frames = new List<Bounds>(8);
            /// <summary>Diagnostics: when set, Find writes why each wall gave no spot.</summary>
            public System.Text.StringBuilder Trace;
            // the obstacles in one wall's frame (Prep), with their margins
            internal readonly List<Vector3> Mn = new List<Vector3>(64), Mx = new List<Vector3>(64);
            internal readonly List<float> Margin = new List<float>(64);
            internal float Size = 1f;

            public void Clear() { Walls.Clear(); Blocks.Clear(); Rigs.Clear(); Sight.Clear(); Frames.Clear(); }

            public Vector3 FlatForward
            {
                get { Vector3 f = Forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }
            }
        }

        /// <summary>A frame on the floor: origin, across (A), out (N); y measured from the floor.</summary>
        struct Frame
        {
            public Vector3 O, A, N;
            public float Floor;
            public Vector3 To(Vector3 p) { Vector3 d = p - O; return new Vector3(Vector3.Dot(d, A), p.y - Floor, Vector3.Dot(d, N)); }
            public static Frame Of(Vector3 origin, Vector3 inward, float floor)
            {
                inward.y = 0f;
                inward = inward.sqrMagnitude > 1e-8f ? inward.normalized : Vector3.forward;
                return new Frame { O = new Vector3(origin.x, floor, origin.z), N = inward, A = Vector3.Cross(Vector3.up, inward).normalized, Floor = floor };
            }
        }

        /// <summary>The best spot (always Found); see the header for the order.</summary>
        public static Spot Find(Scene s)
        {
            Spot best = default;
            for (int k = 0; k < Sizes.Length && !best.Found; k++) best = OnWall(s, false, MaxAngle, Sizes[k]);
            for (int k = 0; k < Sizes.Length && !best.Found; k++)
            {
                best = OnWall(s, true, MaxAngle, Sizes[k]);
                if (!best.Found) best = InFront(s, 90f, Sizes[k], out _);
            }
            for (int k = 0; k < Sizes.Length && !best.Found; k++)
            {
                best = OnWall(s, true, 180f, Sizes[k]);
                if (!best.Found) best = InFront(s, 180f, Sizes[k], out _);
            }
            if (best.Found) return best;
            InFront(s, 180f, Sizes[Sizes.Length - 1], out Spot rough);
            return rough;
        }

        /// <summary>The best wall spot at most <paramref name="maxAngle"/> off the view for a hearth of <paramref name="size"/>
        /// (Found false: none). <paramref name="allowHidden"/> false: the fire must be in sight.</summary>
        public static Spot OnWall(Scene s, bool allowHidden, float maxAngle = MaxAngle, float size = 1f)
        {
            s.Size = size;
            var best = new Spot { Score = float.PositiveInfinity };
            Vector3 fwd = s.FlatForward;
            float hw = HalfWidth * size, front = Front * size;
            for (int w = 0; w < s.Walls.Count; w++)
            {
                RealWall wall = s.Walls[w];
                if (wall.Size.y < MinWallHeight || wall.Size.x < 2f * (hw + EdgeMargin)) { s.Trace?.Append($"wall {w}: too small {wall.Size:F2}; "); continue; }
                Frame f = Frame.Of(wall.Centre, wall.Inward, s.Floor);
                Vector3 head = f.To(s.Head);
                if (head.z <= front + WallGap) { s.Trace?.Append($"wall {w}: player behind or close ({head.z:F2}); "); continue; }
                Prep(s, f);
                int freeCount = 0;
                float half = 0.5f * wall.Size.x - hw - EdgeMargin;
                int n = Mathf.FloorToInt(2f * half / Step) + 1;
                float u0 = -half;
                int runStart = -1;
                for (int i = 0; i <= n; i++)
                {
                    float u = u0 + i * Step;
                    bool free = i < n && Free(s, u, head);
                    if (free) freeCount++;
                    if (free && runStart < 0) runStart = i;
                    if (!free && runStart >= 0)
                    {
                        Run(s, f, w, u0 + runStart * Step, u0 + (i - 1) * Step, fwd, allowHidden, maxAngle, ref best);
                        runStart = -1;
                    }
                }
                s.Trace?.Append($"wall {w} at size {size:F2}: {freeCount}/{n} free, size {wall.Size:F2}, head at {head:F2}; ");
            }
            if (!best.Found) best.Score = 0f;
            return best;
        }

        /// <summary>A free stretch [a, b] along a wall: its spots KeepFromObstacle in from the ends (its middle when shorter).</summary>
        static void Run(Scene s, Frame f, int wall, float a, float b, Vector3 fwd, bool allowHidden, float maxAngle, ref Spot best)
        {
            float mid = 0.5f * (a + b);
            float lo = Mathf.Min(a + KeepFromObstacle, mid), hi = Mathf.Max(b - KeepFromObstacle, mid);
            int n = Mathf.Max(0, Mathf.FloorToInt((hi - lo) / Step));
            for (int i = 0; i <= n; i++)
            {
                float u = n == 0 ? mid : lo + i * Step;
                if (u > hi + 1e-4f) u = hi;
                Vector3 root = f.O + f.A * u + f.N * WallGap;
                var pose = new Pose(new Vector3(root.x, s.Floor, root.z), Quaternion.LookRotation(f.N, Vector3.up));
                float score = Score(s, pose, fwd, out float angle, out float dist, out bool hidden);
                if (angle > maxAngle) continue;
                if (hidden && !allowHidden) continue;
                score += OffMiddleCost * Mathf.Abs(u - mid);
                if (score < best.Score)
                    best = new Spot { Pose = pose, Found = true, OnWall = true, Wall = wall, Hidden = hidden, Size = s.Size, Score = score, Angle = angle, Distance = dist };
            }
        }

        /// <summary>Every obstacle in the frame <paramref name="f"/> (blocks, instruments, paintings), once per wall.</summary>
        static void Prep(Scene s, Frame f)
        {
            s.Mn.Clear(); s.Mx.Clear(); s.Margin.Clear();
            for (int i = 0; i < s.Blocks.Count; i++) { RealWallBlock b = s.Blocks[i]; Add(s, f, b.Centre, b.Half, b.Rotation, BlockMargin); }
            for (int i = 0; i < s.Rigs.Count; i++) { Bounds b = s.Rigs[i]; Add(s, f, b.center, b.extents, Quaternion.identity, RigMargin); }
            for (int i = 0; i < s.Frames.Count; i++) { Bounds b = s.Frames[i]; Add(s, f, b.center, b.extents, Quaternion.identity, FrameMargin); }

            static void Add(Scene s, Frame f, Vector3 c, Vector3 half, Quaternion r, float margin)
            {
                Box(f, c, half, r, out Vector3 mn, out Vector3 mx);
                s.Mn.Add(mn); s.Mx.Add(mx); s.Margin.Add(margin);
            }
        }

        /// <summary>The hearth at <paramref name="u"/> along the prepared frame keeps clear of every obstacle and the head.</summary>
        static bool Free(Scene s, float u, Vector3 head)
        {
            float k = s.Size, x0 = u - HalfWidth * k, x1 = u + HalfWidth * k, z0 = WallGap + Back * k, z1 = WallGap + Front * k, h = Height * k;
            float dx = Mathf.Max(0f, Mathf.Max(x0 - head.x, head.x - x1)), dz = Mathf.Max(0f, Mathf.Max(z0 - head.z, head.z - z1));
            if (dx * dx + dz * dz < HeadClear * HeadClear) return false;
            for (int i = 0; i < s.Mn.Count; i++)
                if (Overlap(s.Mn[i], s.Mx[i], x0, x1, 0f, h, z0, z1, s.Margin[i])) return false;
            return true;
        }

        /// <summary>The spot's score (lower is better): the angle off the view, the distance outside the comfortable band, the
        /// side sight lines an instrument hides; <paramref name="hidden"/>: one hides the fire itself (HiddenCost added).</summary>
        static float Score(Scene s, Pose pose, Vector3 fwd, out float angle, out float dist, out bool hidden)
        {
            Vector3 n = pose.rotation * Vector3.forward, a = pose.rotation * Vector3.right;
            Vector3 mid = pose.position + n * (0.5f * (Back + Front) * s.Size) + Vector3.up * (SightY * s.Size);
            Vector3 to = mid - s.Head; to.y = 0f;
            dist = to.magnitude;
            angle = dist > 1e-4f ? Vector3.Angle(fwd, to) : 0f;
            hidden = Hides(s, s.Head, mid);
            int sides = (Hides(s, s.Head, mid + a * (SightSide * s.Size)) ? 1 : 0) + (Hides(s, s.Head, mid - a * (SightSide * s.Size)) ? 1 : 0);
            return angle + FarCost * Mathf.Max(0f, dist - FarBest) + NearCost * Mathf.Max(0f, NearBest - dist) + SideHiddenCost * sides
                   + (hidden ? HiddenCost : 0f);
        }

        /// <summary>The sight line from the head to <paramref name="target"/> passes through an instrument's part (grazing allowed).</summary>
        public static bool Hides(Scene s, Vector3 head, Vector3 target)
        {
            List<Bounds> boxes = s.Sight.Count > 0 ? s.Sight : s.Rigs;
            for (int i = 0; i < boxes.Count; i++)
            {
                Bounds b = boxes[i];
                if (b.Contains(head)) continue;   // something about the head itself (a held part): not between
                Vector3 e = b.extents - new Vector3(SightGraze, SightGraze, SightGraze);
                if (e.x <= 0f || e.y <= 0f || e.z <= 0f) continue;
                b.extents = e;
                if (Segment(head, target, b)) return true;
            }
            return false;
        }

        /// <summary>Free-standing on the floor, facing the player, at most <paramref name="maxYaw"/> off the view, a hearth of
        /// <paramref name="size"/>: the best free spot in sight, else the best free one (Found false: none free).
        /// <paramref name="rough"/>: the least bad of all (inside the walls, clear of the instruments and the head, standing in
        /// the fewest blocks), else straight ahead; always Found, Rough.</summary>
        public static Spot InFront(Scene s, float maxYaw, float size, out Spot rough)
        {
            s.Size = size;
            Vector3 fwd = s.FlatForward;
            Spot best = default, hidden = default, straight = default;
            rough = default;
            best.Score = hidden.Score = rough.Score = float.PositiveInfinity;
            for (int yi = 0; yi < FallbackYaw.Length; yi++)
                for (int di = 0; di < FallbackDist.Length; di++)
                {
                    if (Mathf.Abs(FallbackYaw[yi]) > maxYaw) continue;
                    Vector3 dir = Quaternion.AngleAxis(FallbackYaw[yi], Vector3.up) * fwd;
                    float d = FallbackDist[di] + 0.5f * (Back + Front) * size;
                    Vector3 root = s.Head + dir * d;
                    var pose = new Pose(new Vector3(root.x, s.Floor, root.z), Quaternion.LookRotation(-dir, Vector3.up));
                    float score = Score(s, pose, fwd, out float angle, out float dist, out bool isHidden) + FallbackDistCost * Mathf.Abs(dist - FallbackDistance);
                    var spot = new Spot { Pose = pose, Found = true, OnWall = false, Wall = -1, Hidden = isHidden, Size = size, Score = score, Angle = angle, Distance = dist };
                    if (yi == 0 && di == 0) straight = spot;
                    if (!InsideWalls(s, pose, size)) continue;
                    Frame f = Frame.Of(pose.position + dir * WallGap, -dir, s.Floor);   // as if against a wall there
                    Prep(s, f);
                    if (Free(s, 0f, f.To(s.Head)))
                    {
                        if (!isHidden) { if (score < best.Score) best = spot; }
                        else if (score < hidden.Score) hidden = spot;
                        continue;
                    }
                    bool clear = FlatDistance(pose, s.Head, size) >= HeadClear;
                    for (int i = 0; i < s.Rigs.Count && clear; i++) clear = !Overlaps(pose, s.Rigs[i], RigMargin, size);
                    if (!clear) continue;
                    float r = score + 50f * Overlapping(s, pose, size);
                    if (r < rough.Score) rough = spot;
                }
            if (!rough.Found) rough = straight;
            rough.Rough = true;
            return best.Found ? best : hidden;
        }

        /// <summary>How many of the scene model's blocks the hearth at <paramref name="pose"/> stands in.</summary>
        static int Overlapping(Scene s, Pose pose, float size)
        {
            int n = 0;
            for (int i = 0; i < s.Blocks.Count; i++) if (Overlaps(pose, s.Blocks[i], 0f, size)) n++;
            return n;
        }

        /// <summary>No corner of the hearth's footprint lies behind a wall it stands within the span of (a free-standing hearth
        /// in a small room).</summary>
        public static bool InsideWalls(Scene s, Pose pose, float size = 1f)
        {
            for (int w = 0; w < s.Walls.Count; w++)
            {
                RealWall wall = s.Walls[w];
                Frame f = Frame.Of(wall.Centre, wall.Inward, s.Floor);
                for (int i = 0; i < 4; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? -HalfWidth : HalfWidth, 0f, (i & 2) == 0 ? Back : Front) * size;
                    Vector3 q = f.To(pose.position + pose.rotation * c);
                    if (q.z < -0.01f && Mathf.Abs(q.x) < 0.5f * wall.Size.x) return false;   // behind the wall plane
                }
            }
            return true;
        }

        /// <summary>The hearth of <paramref name="size"/> at <paramref name="pose"/> overlaps the world box <paramref name="b"/>
        /// (grown by <paramref name="margin"/>).</summary>
        public static bool Overlaps(Pose pose, Bounds b, float margin, float size = 1f) =>
            Overlaps(pose, b.center, b.extents, Quaternion.identity, margin, size);

        /// <summary>The hearth of <paramref name="size"/> at <paramref name="pose"/> overlaps the scene model's block (grown by
        /// <paramref name="margin"/>).</summary>
        public static bool Overlaps(Pose pose, RealWallBlock b, float margin, float size = 1f) =>
            Overlaps(pose, b.Centre, b.Half, b.Rotation, margin, size);

        static bool Overlaps(Pose pose, Vector3 c, Vector3 half, Quaternion r, float margin, float size)
        {
            Frame f = Frame.Of(pose.position, pose.rotation * Vector3.forward, pose.position.y);
            Box(f, c, half, r, out Vector3 mn, out Vector3 mx);
            return Overlap(mn, mx, -HalfWidth * size, HalfWidth * size, 0f, Height * size, Back * size, Front * size, margin);
        }

        /// <summary>The hearth's box as a world box.</summary>
        public static Bounds WorldBox(Pose pose, float size = 1f)
        {
            var b = new Bounds(pose.position, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -HalfWidth : HalfWidth, (i & 2) == 0 ? 0f : Height, (i & 4) == 0 ? Back : Front) * size;
                b.Encapsulate(pose.position + pose.rotation * c);
            }
            return b;
        }

        /// <summary>The nearest distance on the floor from <paramref name="p"/> to the hearth's footprint.</summary>
        public static float FlatDistance(Pose pose, Vector3 p, float size = 1f)
        {
            Vector3 d = Quaternion.Inverse(pose.rotation) * (p - pose.position);
            float hw = HalfWidth * size;
            float dx = Mathf.Max(0f, Mathf.Max(-hw - d.x, d.x - hw)), dz = Mathf.Max(0f, Mathf.Max(Back * size - d.z, d.z - Front * size));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The oriented box (centre, half sizes along the rotation's axes) as a box in the frame.</summary>
        static void Box(Frame f, Vector3 c, Vector3 half, Quaternion r, out Vector3 mn, out Vector3 mx)
        {
            mn = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            mx = -mn;
            Vector3 ax = r * new Vector3(half.x, 0f, 0f), ay = r * new Vector3(0f, half.y, 0f), az = r * new Vector3(0f, 0f, half.z);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = c + ((i & 1) == 0 ? -ax : ax) + ((i & 2) == 0 ? -ay : ay) + ((i & 4) == 0 ? -az : az);
                Vector3 q = f.To(p);
                mn = Vector3.Min(mn, q);
                mx = Vector3.Max(mx, q);
            }
        }

        static bool Overlap(Vector3 mn, Vector3 mx, float x0, float x1, float y0, float y1, float z0, float z1, float m) =>
            mn.x < x1 + m && mx.x > x0 - m && mn.y < y1 + m && mx.y > y0 - m && mn.z < z1 + m && mx.z > z0 - m;

        /// <summary>The segment a..b passes through the box (slabs).</summary>
        static bool Segment(Vector3 a, Vector3 b, Bounds box)
        {
            Vector3 d = b - a, mn = box.min, mx = box.max;
            float t0 = 0f, t1 = 1f;
            for (int k = 0; k < 3; k++)
            {
                float o = a[k], dk = d[k], lo = mn[k], hi = mx[k];
                if (Mathf.Abs(dk) < 1e-7f) { if (o < lo || o > hi) return false; continue; }
                float ta = (lo - o) / dk, tb = (hi - o) / dk;
                if (ta > tb) { float t = ta; ta = tb; tb = t; }
                t0 = Mathf.Max(t0, ta);
                t1 = Mathf.Min(t1, tb);
                if (t0 > t1) return false;
            }
            return true;
        }
    }
}
