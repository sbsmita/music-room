// The player's real walls in mixed reality, for what draws on them (the Room's wall paint: Room/Decor/WallPaint.cs). The MR
// module implements it (MR/MrPaintWalls.cs) from the room's scene model (MRUK walls, windows, doors and furniture) or, with
// no scene model, from the depth sensor (a wall along a ray). Room cannot see MR's types, so this is the meeting point.
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>One flat, upright wall rectangle in world space: its centre, the normal toward the player (into the room,
    /// horizontal) and its size (x along the wall, y up).</summary>
    public struct RealWall
    {
        public Vector3 Centre;
        public Vector3 Inward;
        public Vector2 Size;
    }

    /// <summary>Something standing on or against a wall that nothing should be drawn over (a window, a door, a shelf, a
    /// screen): an oriented box in world space (centre, half sizes along the rotation's axes).</summary>
    public struct RealWallBlock
    {
        public Vector3 Centre;
        public Vector3 Half;
        public Quaternion Rotation;
    }

    public interface IRealWalls
    {
        /// <summary>Read the scene model again (call when a drawing starts: it may have loaded or changed since). True when
        /// the room has a scene model with at least one wall.</summary>
        bool Refresh();
        /// <summary>Bumps every time Refresh found the walls or blocks different from before.</summary>
        int Version { get; }
        int WallCount { get; }
        RealWall Wall(int i);
        int BlockCount { get; }
        RealWallBlock Block(int i);
        /// <summary>No scene model: the depth sensor's flat, upright wall along a ray (headset only; false when there is none or
        /// the sensor cannot be used). Call it every frame while it is wanted: the sensor sleeps when nobody asks.</summary>
        bool DepthWall(Ray ray, out Vector3 point, out Vector3 inward);
    }
}
