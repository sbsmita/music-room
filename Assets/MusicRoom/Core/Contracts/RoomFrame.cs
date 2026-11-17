// The VR room's pose in the world (2026-11-11, "in music room VR i want to inverse my position. I should see the fire and
// whole room."). The room is built with its own numbers (Room's RoomDims, the source frame) in the ROOM FRAME, which was
// the world until then: fireplace at z -4.08 behind the seat, the window wall at x +3.2. The seat stays where it always was
// (the world origin, facing +z); the room is turned about it, yaw 180 degrees, and slid 0.5 m along the seat's forward
// axis, so the player faces the fire with the whole room in front: fireplace 3.58 m ahead, the window on the left wall,
// the old front wall 1.62 m behind. The slide (2026-11-12): with the wall 1.12 m behind, the drum kit at its home to
// the right (its stands and cymbals reach ~1.1 m behind the head) met that wall as soon as the head sat 10 cm back, and
// the resolver slid the kit 0.46 m sideways (DrumsPlayTests); 1.62 m leaves the kit, a half-turn spin of it and a
// leaning player room, and the front (3.58 m) is still far beyond any seat slot (the planner plans exactly as before).
//
// One source of truth: the Room root's local pose IS this pose (RoomBuilder), and everything that holds room coordinates
// turns them into the world here: the room box (GrabConstants.RoomBox, IRoom.RoomBox, the seat planner's box, the
// placement solver, the glides), the furniture (IRoomFurniture publishes world boxes), the display spots, spares and
// saved stows (Layout), the fixed sound sources and the acoustic box (Audio). Mixed reality is not affected: the
// virtual room is hidden there and nothing in MR uses the room frame.
using UnityEngine;

namespace MusicRoom.Core
{
    public static class RoomFrame
    {
        /// <summary>The room's yaw in the world (degrees, Unity: clockwise from above), about the seat.</summary>
        public const float Yaw = 180f;
        /// <summary>The room frame's origin in the world: 0.5 m behind the seat (the room slid toward the player's back, so
        /// the wall behind is 1.62 m away and the fireplace 3.58 m ahead).</summary>
        public static readonly Vector3 Position = new Vector3(0f, 0f, -0.5f);

        /// <summary>The room box in room coordinates (registry ROOM): x -3.2..3.2, z -4.08..1.12, y 0..2.9.</summary>
        public const double X0 = -3.2, X1 = 3.2, Z0 = -4.08, Z1 = 1.12, Height = 2.9;

        /// <summary>cos / sin of Yaw in double precision, rounded so a quarter or half turn is exact (-1, 0).</summary>
        public static readonly double Cos = System.Math.Round(System.Math.Cos(Yaw * System.Math.PI / 180.0), 12),
            Sin = System.Math.Round(System.Math.Sin(Yaw * System.Math.PI / 180.0), 12);

        /// <summary>The turn as a quaternion (exact for a half turn: (0, 1, 0, 0)).</summary>
        public static readonly Quaternion Rotation = new Quaternion(0f,
            (float)System.Math.Round(System.Math.Sin(Yaw * System.Math.PI / 360.0), 12), 0f,
            (float)System.Math.Round(System.Math.Cos(Yaw * System.Math.PI / 360.0), 12));

        /// <summary>The room box in room coordinates (the old world box: centre (0, 1.45, -1.48), 6.4 x 2.9 x 5.2).</summary>
        public static readonly Bounds LocalBox = new Bounds(
            new Vector3((float)((X0 + X1) / 2.0), (float)(Height / 2.0), (float)((Z0 + Z1) / 2.0)),
            new Vector3((float)(X1 - X0), (float)Height, (float)(Z1 - Z0)));

        /// <summary>The room box in the world, double precision (axis-aligned: the turn is a multiple of 90 degrees).</summary>
        public static readonly double WorldX0, WorldX1, WorldZ0, WorldZ1;

        /// <summary>The room box in the world (GrabConstants.RoomBox, IRoom.RoomBox): x +-3.2, z -1.62..3.58, y 0..2.9.</summary>
        public static readonly Bounds WorldBox;

        static RoomFrame()
        {
            ToWorld(X0, Z0, out double ax, out double az);
            ToWorld(X1, Z1, out double bx, out double bz);
            WorldX0 = System.Math.Min(ax, bx); WorldX1 = System.Math.Max(ax, bx);
            WorldZ0 = System.Math.Min(az, bz); WorldZ1 = System.Math.Max(az, bz);
            WorldBox = new Bounds(
                new Vector3((float)((WorldX0 + WorldX1) / 2.0), (float)(Height / 2.0), (float)((WorldZ0 + WorldZ1) / 2.0)),
                new Vector3((float)(WorldX1 - WorldX0), (float)Height, (float)(WorldZ1 - WorldZ0)));
        }

        // ---------------------------------------------------------------- points, directions, yaws

        /// <summary>A room-frame point (Unity axes) in the world.</summary>
        public static Vector3 ToWorld(Vector3 room) => new Vector3(
            Position.x + (float)(Cos * room.x + Sin * room.z), Position.y + room.y, Position.z + (float)(-Sin * room.x + Cos * room.z));

        /// <summary>A world point in room-frame coordinates.</summary>
        public static Vector3 ToRoom(Vector3 world)
        {
            double x = world.x - Position.x, z = world.z - Position.z;
            return new Vector3((float)(Cos * x - Sin * z), world.y - Position.y, (float)(Sin * x + Cos * z));
        }

        /// <summary>A room-frame direction in the world.</summary>
        public static Vector3 DirToWorld(Vector3 room) => new Vector3((float)(Cos * room.x + Sin * room.z), room.y, (float)(-Sin * room.x + Cos * room.z));

        /// <summary>A room-frame floor point (x, z) in the world, double precision (the planners).</summary>
        public static void ToWorld(double x, double z, out double wx, out double wz)
        {
            wx = Position.x + Cos * x + Sin * z;
            wz = Position.z - Sin * x + Cos * z;
        }

        /// <summary>A world floor point (x, z) in room coordinates, double precision.</summary>
        public static void ToRoom(double wx, double wz, out double x, out double z)
        {
            double dx = wx - Position.x, dz = wz - Position.z;
            x = Cos * dx - Sin * dz;
            z = Sin * dx + Cos * dz;
        }

        /// <summary>A room-frame yaw (degrees) in the world, wrapped to (-180, 180].</summary>
        public static double YawToWorld(double roomYaw) => Wrap(roomYaw + Yaw);

        /// <summary>A world yaw (degrees) in the room frame, wrapped to (-180, 180].</summary>
        public static double YawToRoom(double worldYaw) => Wrap(worldYaw - Yaw);

        static double Wrap(double a)
        {
            a %= 360.0;
            if (a > 180.0) a -= 360.0;
            else if (a <= -180.0) a += 360.0;
            return a;
        }

        // ---------------------------------------------------------------- boxes

        /// <summary>A furniture box in room coordinates as a world box: the centre turned; a box turned by a half turn is
        /// itself, so the yaw is kept modulo 180 degrees (an axis-aligned box stays yaw 0).</summary>
        public static FurnitureBlocker ToWorld(in FurnitureBlocker b)
        {
            ToWorld(b.Centre.x, b.Centre.y, out double x, out double z);
            double yaw = Wrap(b.Yaw + Yaw);
            if (yaw > 90.0 + 1e-6) yaw -= 180.0;
            else if (yaw <= -90.0 + 1e-6) yaw += 180.0;
            if (System.Math.Abs(yaw) < 1e-4) yaw = 0.0;
            return new FurnitureBlocker(b.Name, (float)x, (float)z, b.Half.x, b.Half.y, b.Y0, b.Y1, (float)yaw);
        }

        /// <summary>A room-frame floor rectangle (x, z) as the world rectangle that holds it.</summary>
        public static Rect ToWorld(Rect room)
        {
            ToWorld(room.xMin, room.yMin, out double ax, out double az);
            ToWorld(room.xMax, room.yMax, out double bx, out double bz);
            ToWorld(room.xMin, room.yMax, out double cx, out double cz);
            ToWorld(room.xMax, room.yMin, out double dx, out double dz);
            double x0 = System.Math.Min(System.Math.Min(ax, bx), System.Math.Min(cx, dx)), x1 = System.Math.Max(System.Math.Max(ax, bx), System.Math.Max(cx, dx));
            double z0 = System.Math.Min(System.Math.Min(az, bz), System.Math.Min(cz, dz)), z1 = System.Math.Max(System.Math.Max(az, bz), System.Math.Max(cz, dz));
            return Rect.MinMaxRect((float)x0, (float)z0, (float)x1, (float)z1);
        }
    }
}
