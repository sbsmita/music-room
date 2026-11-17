// The display plan (12-new-instruments/platform.md 4.1, Node reference stow-plan.mjs): where each
// instrument stands, hangs or rests in the VR room while it is not at the seat, the spares a blocked spot falls back to,
// the ray summon's pick point and its cone pick. The spots (descriptors' Stow poses), the spares, the reference furniture
// and the saved stows are in ROOM coordinates (the room's own frame: front wall z = +1.12, window wall x = +3.2, left wall
// x = -3.2, fireplace wall z = -4.08); since 2026-11-11 the room is turned to face the fire (Core's RoomFrame, a half turn
// about the seat), so a Spot is turned into the WORLD when it is made (Spot.From, MovedToRoom, At) and everything here
// runs in world coordinates, as the at-seat boxes and IRoomFurniture do (fireplace wall world z = +3.58, window wall x = -3.2).
//
// Each instrument's spot comes from its descriptor (Stow: SpotId, Mount, Pose, Yaw, Boxes; Boxes null = its seat boxes
// without the air boxes, moved with the rig). A spot is blocked when an at-seat box comes within 3 cm of one of its
// displayed boxes (the seat frame follows the head: ARCHITECTURE 15.5 fitSeat, so another seat can put an instrument
// there); a blocked instrument moves to the first free spare (back-wall-left on the floor, then the coffee table's top at
// 0.45 m), else it is hidden ("[Layout] display blocked: <id>") until the seat moves. A spare is free when every box of
// the instrument there is inside the room and 3 cm clear of the furniture, of the at-seat boxes and of every other
// displayed instrument. Plain C#, double precision (the reference's numbers); runs on arrangements, never per frame.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using Box = MusicRoom.Layout.SeatPlanner.Box;
using FloorObb = MusicRoom.Layout.SeatPlanner.FloorObb;
using Pose2 = MusicRoom.Layout.SeatPlanner.Pose2;

namespace MusicRoom.Layout
{
    public sealed class StowPlan
    {
        public const double Gap = 0.03;
        /// <summary>Nothing on display is closer than this to the default seat's head (horizontal, to the play centre).</summary>
        public const double MinDistance = 1.3;
        /// <summary>The cone pick (platform.md 4.4 c): within 6 degrees of the ray and 6 m of the hand; ties within 0.5 deg go
        /// to the nearer; the pick point is the top centre of the displayed boxes, 5 cm down.</summary>
        public const double PickConeDeg = 6.0, PickMax = 6.0, PickTieDeg = 0.5, PickDrop = 0.05;

        public enum Placement : byte { Spot = 0, Spare, Hidden }

        /// <summary>A spare place for an instrument whose own spot is blocked (platform.md 4.1).</summary>
        public readonly struct Spare
        {
            public readonly string Id;
            public readonly StowMount Mount;
            public readonly double X, Z, Yaw, Surface;
            public Spare(string id, StowMount mount, double x, double z, double yaw, double surface)
            { Id = id; Mount = mount; X = x; Z = z; Yaw = yaw; Surface = surface; }
        }

        /// <summary>The spares in ROOM coordinates (Spot.At turns them into the world).</summary>
        public static readonly Spare[] Spares =
        {
            new Spare("back-wall-left", StowMount.Floor, -2.2, -3.85, 180.0, 0.0),   // left of the fireplace
            new Spare("coffee-table", StowMount.Surface, 0.85, -3.13, 180.0, 0.45),  // on its top, facing the room
        };

        /// <summary>stow-plan.mjs FURNITURE: approximations of RoomBuilder / Curtains, used only while no module registers
        /// IRoomFurniture (Room publishes the exact blockers). Room coordinates, but for the bench, which stands at the seat
        /// (world) however the room is turned (RoomBuilder.SeatFix).</summary>
        public static readonly FurnitureBlocker[] ReferenceFurniture =
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
            new FurnitureBlocker("window glass", 3.15f, -1.18f, 0.05f, 0.90f, 0.85f, 2.40f),
            new FurnitureBlocker("bench", 0f, -0.10f, 0.38f, 0.17f, 0f, 0.54f),
        };

        /// <summary>ReferenceFurniture turned into the world (RoomFrame): the fallback while no module registers IRoomFurniture.</summary>
        public static readonly FurnitureBlocker[] ReferenceFurnitureWorld = Array.ConvertAll(ReferenceFurniture, f => f.Name == SeatFurniture ? f : RoomFrame.ToWorld(f));
        /// <summary>The one reference blocker at the seat (it does not turn with the room).</summary>
        public const string SeatFurniture = "bench";

        /// <summary>A furniture blocker as a world box.</summary>
        public static FloorObb BlockerBox(in FurnitureBlocker f) => SeatPlanner.WorldBox(new Pose2(SeatPlanner.D(f.Centre.x), SeatPlanner.D(f.Centre.y), SeatPlanner.D(f.Yaw)),
            new Box(0.0, 0.0, SeatPlanner.D(f.Half.x), SeatPlanner.D(f.Half.y), SeatPlanner.D(f.Y0), SeatPlanner.D(f.Y1)));

        /// <summary>One instrument's display place: the rig pose in the WORLD (the room-frame spot turned by RoomFrame) and its
        /// displayed boxes (heights above the floor with the rig at Y).</summary>
        public sealed class Spot
        {
            public InstrumentKind Kind;
            public string Id;
            public StowMount Mount;
            public double X, Y, Z, Yaw;
            public Box[] Boxes = Array.Empty<Box>();
            /// <summary>The play centre (rig-local xz) for the distance rule.</summary>
            public double PlayX, PlayZ;
            /// <summary>A spot the player moved (musicroom.stow; set by Layout, kept by MovedTo): the descriptor's own spot it came
            /// from, else null. When the moved
            /// spot is blocked by what stands at the seat now, the instrument goes back to this one before any spare
            /// (2026-11-17: "why i cant see drums placed near wall": a kit moved on display once was hidden from then on).</summary>
            public Spot Home;

            public Pose2 Pose2 => new Pose2(X, Z, Yaw);

            /// <summary>The descriptor's spot (room coordinates, turned into the world). Stow.Boxes when given (heights with the
            /// rig at Stow.Pose), else the seat boxes without air, their heights moved by the rig's height difference
            /// (Stow.Pose.y - Seat.Home.y).</summary>
            public static Spot From(InstrumentDescriptor d)
            {
                if (d?.Stow == null) return null;
                StowSpec s = d.Stow;
                var boxes = new List<Box>(8);
                if (s.Boxes != null && s.Boxes.Length > 0)
                    foreach (FootBox b in s.Boxes) boxes.Add(new Box(SeatPlanner.D(b.Cx), SeatPlanner.D(b.Cz), SeatPlanner.D(b.Hx), SeatPlanner.D(b.Hz), SeatPlanner.D(b.Y0), SeatPlanner.D(b.Y1)));
                else if (d.Seat?.Boxes != null)
                {
                    double dy = SeatPlanner.D(s.Pose.y) - SeatPlanner.D(d.Seat.Home.y);
                    foreach (FootBox b in d.Seat.Boxes)
                        if (!b.Air) boxes.Add(new Box(SeatPlanner.D(b.Cx), SeatPlanner.D(b.Cz), SeatPlanner.D(b.Hx), SeatPlanner.D(b.Hz), SeatPlanner.D(b.Y0) + dy, SeatPlanner.D(b.Y1) + dy));
                }
                RoomFrame.ToWorld(SeatPlanner.D(s.Pose.x), SeatPlanner.D(s.Pose.z), out double wx, out double wz);
                return new Spot
                {
                    Kind = d.Kind, Id = s.SpotId, Mount = s.Mount,
                    X = wx, Y = SeatPlanner.D(s.Pose.y), Z = wz, Yaw = RoomFrame.YawToWorld(SeatPlanner.D(s.Yaw)),
                    Boxes = boxes.ToArray(),
                    PlayX = d.Seat != null ? SeatPlanner.D(d.Seat.PlayCentre.x) : 0.0, PlayZ = d.Seat != null ? SeatPlanner.D(d.Seat.PlayCentre.y) : 0.0,
                };
            }

            /// <summary>The same instrument moved to (x, z, yaw), its height kept (a spot the player moved in arrange mode).</summary>
            public Spot MovedTo(double x, double z, double yaw) => new Spot
            {
                Kind = Kind, Id = Id, Mount = Mount, X = x, Y = Y, Z = z, Yaw = yaw, Boxes = Boxes, PlayX = PlayX, PlayZ = PlayZ,
                Home = Home,
            };

            /// <summary>Moved to a ROOM-frame pose (a saved musicroom.stow value), turned into the world.</summary>
            public Spot MovedToRoom(double x, double z, double yaw)
            {
                RoomFrame.ToWorld(x, z, out double wx, out double wz);
                return MovedTo(wx, wz, RoomFrame.YawToWorld(yaw));
            }

            /// <summary>The pose in ROOM coordinates (what musicroom.stow saves).</summary>
            public void RoomPose(out double x, out double z, out double yaw)
            {
                RoomFrame.ToRoom(X, Z, out x, out z);
                yaw = RoomFrame.YawToRoom(Yaw);
            }

            /// <summary>The instrument at a spare (room coordinates, turned into the world): on the floor spare as it stands (a wall instrument keeps its height on the back
            /// wall, a surface one comes down to the floor); on a surface spare its lowest box rests on the surface.</summary>
            public Spot At(in Spare spare)
            {
                double low = double.PositiveInfinity;
                for (int i = 0; i < Boxes.Length; i++) low = Math.Min(low, Boxes[i].Y0);
                if (double.IsInfinity(low)) low = 0.0;
                double dy = spare.Mount == StowMount.Surface ? spare.Surface - low : (Mount == StowMount.Surface ? -low : 0.0);
                var b = new Box[Boxes.Length];
                for (int i = 0; i < b.Length; i++) { b[i] = Boxes[i]; b[i].Y0 += dy; b[i].Y1 += dy; }
                RoomFrame.ToWorld(spare.X, spare.Z, out double wx, out double wz);
                return new Spot { Kind = Kind, Id = spare.Id, Mount = spare.Mount, X = wx, Y = Y + dy, Z = wz, Yaw = RoomFrame.YawToWorld(spare.Yaw), Boxes = b, PlayX = PlayX, PlayZ = PlayZ };
            }

            public void WorldBoxes(List<FloorObb> into)
            {
                Pose2 p = Pose2;
                for (int i = 0; i < Boxes.Length; i++) into.Add(SeatPlanner.WorldBox(p, Boxes[i]));
            }

            /// <summary>stow-plan.mjs pickPoint: the area-weighted centre of the top of its displayed boxes, 5 cm down (world).</summary>
            public Vector3 PickPoint()
            {
                Pose2 p = Pose2;
                double x = 0, z = 0, w = 0, top = 0;
                for (int i = 0; i < Boxes.Length; i++)
                {
                    FloorObb o = SeatPlanner.WorldBox(p, Boxes[i]);
                    double a = o.Hx * o.Hz;
                    x += o.Cx * a; z += o.Cz * a; w += a; top = Math.Max(top, o.Y1);
                }
                if (w <= 0.0) return new Vector3((float)X, (float)Y, (float)Z);
                return new Vector3((float)(x / w), (float)(top - PickDrop), (float)(z / w));
            }

            /// <summary>Horizontal distance from (hx, hz) to its play centre.</summary>
            public double Distance(double hx, double hz)
            {
                SeatPlanner.ToWorld(Pose2, PlayX, PlayZ, out double cx, out double cz);
                return Math.Sqrt((cx - hx) * (cx - hx) + (cz - hz) * (cz - hz));
            }
        }

        // ---------------------------------------------------------------- the assignment

        /// <summary>Where each displayed instrument is after the last Assign.</summary>
        public readonly struct Result
        {
            public readonly InstrumentKind Kind;
            public readonly Placement Placement;
            public readonly Spot Spot;
            public Result(InstrumentKind kind, Placement placement, Spot spot) { Kind = kind; Placement = placement; Spot = spot; }
        }

        readonly List<FloorObb> _furniture = new List<FloorObb>(24);
        readonly List<FloorObb> _scratch = new List<FloorObb>(32), _taken = new List<FloorObb>(64);
        public readonly List<Result> Results = new List<Result>(10);

        public void SetFurniture(IReadOnlyList<FurnitureBlocker> blockers)
        {
            _furniture.Clear();
            if (blockers == null) return;
            for (int i = 0; i < blockers.Count; i++) _furniture.Add(BlockerBox(blockers[i]));
        }

        public IReadOnlyList<FloorObb> Furniture => _furniture;

        static bool Hits(List<FloorObb> a, IReadOnlyList<FloorObb> b, double gap)
        {
            for (int i = 0; i < a.Count; i++)
                for (int k = 0; k < b.Count; k++)
                    if (SeatPlanner.Overlaps(a[i], b[k], gap)) return true;
            return false;
        }

        /// <summary>Places every instrument of <paramref name="spots"/> (display order): at its own spot unless an at-seat box
        /// (<paramref name="atSeat"/>, non-air world boxes) comes within 3 cm, then the first free spare, else hidden.</summary>
        public void Assign(IReadOnlyList<Spot> spots, IReadOnlyList<FloorObb> atSeat)
        {
            Results.Clear();
            _taken.Clear();
            // Pass 1: every spot that is not blocked keeps it (the spots are clear of the furniture and of each other).
            for (int i = 0; i < spots.Count; i++)
            {
                Spot s = spots[i];
                _scratch.Clear();
                s.WorldBoxes(_scratch);
                bool blocked = Hits(_scratch, atSeat, Gap);
                Results.Add(new Result(s.Kind, blocked ? Placement.Hidden : Placement.Spot, blocked ? null : s));
                if (!blocked) _taken.AddRange(_scratch);
            }
            // Pass 2: the blocked ones, in display order, take the first free spare.
            for (int i = 0; i < Results.Count; i++)
            {
                if (Results[i].Placement != Placement.Hidden) continue;
                Spot s = spots[i];
                if (s.Home != null)
                {
                    // a moved spot that is blocked: back at the instrument's own spot when that is clear
                    _scratch.Clear();
                    s.Home.WorldBoxes(_scratch);
                    if (!Hits(_scratch, atSeat, Gap) && !Hits(_scratch, _taken, Gap))
                    {
                        Results[i] = new Result(s.Kind, Placement.Spot, s.Home);
                        _taken.AddRange(_scratch);
                        continue;
                    }
                }
                for (int k = 0; k < Spares.Length; k++)
                {
                    Spot at = s.At(Spares[k]);
                    _scratch.Clear();
                    at.WorldBoxes(_scratch);
                    if (!Free(_scratch, atSeat)) continue;
                    Results[i] = new Result(s.Kind, Placement.Spare, at);
                    _taken.AddRange(_scratch);
                    break;
                }
            }
        }

        bool Free(List<FloorObb> boxes, IReadOnlyList<FloorObb> atSeat)
        {
            for (int i = 0; i < boxes.Count; i++) if (!SeatPlanner.InsideRoom(boxes[i])) return false;
            return !Hits(boxes, _furniture, Gap) && !Hits(boxes, atSeat, Gap) && !Hits(boxes, _taken, Gap);
        }

        public bool TryGet(InstrumentKind kind, out Result r)
        {
            for (int i = 0; i < Results.Count; i++) if (Results[i].Kind == kind) { r = Results[i]; return true; }
            r = default;
            return false;
        }

        // ---------------------------------------------------------------- the cone pick

        /// <summary>stow-plan.mjs pick(origin, dir, stowed): among the pick points within PickConeDeg of the ray and PickMax of
        /// its origin, the smallest angle wins; ties within PickTieDeg go to the nearer. -1 when none. <paramref name="dir"/>
        /// must be a unit vector. Allocation-free.</summary>
        public static int Pick(Vector3 origin, Vector3 dir, IReadOnlyList<Vector3> points, int count, out double angle, out double distance)
        {
            int best = -1;
            angle = distance = 0.0;
            double ox = origin.x, oy = origin.y, oz = origin.z, dx = dir.x, dy = dir.y, dz = dir.z;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = points[i];
                double vx = p.x - ox, vy = p.y - oy, vz = p.z - oz;
                double dist = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                if (dist > PickMax || dist < 1e-6) continue;
                double cos = (vx * dx + vy * dy + vz * dz) / dist;
                double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI;
                if (ang > PickConeDeg) continue;
                if (best < 0 || ang < angle - PickTieDeg || (Math.Abs(ang - angle) <= PickTieDeg && dist < distance))
                {
                    best = i;
                    angle = ang;
                    distance = dist;
                }
            }
            return best;
        }

        /// <summary>The same pick in doubles (the reference vectors' rays).</summary>
        public static int Pick(double ox, double oy, double oz, double dx, double dy, double dz, IReadOnlyList<double[]> points, out double angle, out double distance)
        {
            int best = -1;
            angle = distance = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                double[] p = points[i];
                double vx = p[0] - ox, vy = p[1] - oy, vz = p[2] - oz;
                double dist = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                if (dist > PickMax || dist < 1e-6) continue;
                double cos = (vx * dx + vy * dy + vz * dz) / dist;
                double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI;
                if (ang > PickConeDeg) continue;
                if (best < 0 || ang < angle - PickTieDeg || (Math.Abs(ang - angle) <= PickTieDeg && dist < distance))
                {
                    best = i;
                    angle = ang;
                    distance = dist;
                }
            }
            return best;
        }

        /// <summary>stow-plan.mjs pickPoint in doubles (tests): the top centre of the boxes at the spot, 5 cm down.</summary>
        public static double[] PickPointD(Spot s)
        {
            double x = 0, z = 0, w = 0, top = 0;
            Pose2 p = s.Pose2;
            for (int i = 0; i < s.Boxes.Length; i++)
            {
                FloorObb o = SeatPlanner.WorldBox(p, s.Boxes[i]);
                double a = o.Hx * o.Hz;
                x += o.Cx * a; z += o.Cz * a; w += a; top = Math.Max(top, o.Y1);
            }
            return new[] { x / w, top - PickDrop, z / w };
        }
    }
}
