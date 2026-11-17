// Room / decor contracts (spec 07). The Room module implements IRoom, IAmbienceSwitches, ISnowWeather.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public enum AmbienceKind { Fire = 0, Wind = 1, Snow = 2 }

    public interface IRoom
    {
        Transform Root { get; }
        bool Visible { get; }
        /// <summary>Hide the shell, decor and its lights (mixed reality); instruments stay.</summary>
        void SetVisible(bool visible);
        void SetCandles(bool on);
        /// <summary>The virtual room box in Unity world coordinates (RoomFrame.WorldBox: x +-3.2, z -1.62..3.58, y 0..2.9; the
        /// room is turned to face the fire, RoomFrame).</summary>
        Bounds RoomBox { get; }
    }

    public interface IAmbienceSwitches
    {
        bool Get(AmbienceKind kind);
        /// <summary>Flips the switch lever, the sound (IAudioEngine.SetFire/Wind/Snow) and publishes AmbienceEvent.</summary>
        void Set(AmbienceKind kind, bool on);
    }

    public struct SnowGust { public float Strength, Attack, Hold, Release; public float Duration => Attack + Hold + Release; }

    /// <summary>The snow weather (spec 07 section 5.3): one instance, ticked by the Room module in Decor. The Room keeps
    /// ticking it, and pushing the _Snow* shader globals, while the room itself is hidden (mixed reality: the room snow).</summary>
    public interface ISnowWeather
    {
        float Target { get; }
        float Level { get; }
        float Cover { get; }
        float Wind { get; }
        float WindVel { get; }
        /// <summary>The weather's own clock: the shared uTime of every snow field. The default (a
        /// weather without a clock) is 0.</summary>
        float Time => 0f;
        event Action<SnowGust> OnGust;
    }

    /// <summary>One piece of furniture as the display plan sees it (platform.md 4.1): an oriented box on the floor (centre,
    /// half sizes, yaw deg; IRoomFurniture publishes world coordinates, RoomFrame.ToWorld of the room's own) with a height
    /// range above the floor.</summary>
    public readonly struct FurnitureBlocker
    {
        public readonly string Name;
        public readonly Vector2 Centre, Half;
        public readonly float Yaw, Y0, Y1;
        public FurnitureBlocker(string name, float cx, float cz, float hx, float hz, float y0, float y1, float yaw = 0f)
        { Name = name; Centre = new Vector2(cx, cz); Half = new Vector2(hx, hz); Y0 = y0; Y1 = y1; Yaw = yaw; }
        public override string ToString() => Name;
    }

    /// <summary>The VR room's furniture as data (Room, platform.md 4.1): displayed instruments keep 3 cm clear of every
    /// blocker. Also the surfaces a Surface-mounted instrument may rest on (the window sill, the coffee table).</summary>
    public interface IRoomFurniture
    {
        IReadOnlyList<FurnitureBlocker> FloorBlockers { get; }
        /// <summary>Decor that is not a blocker (the ivy hanging from the sill, the plants' spread of leaves): display spots
        /// keep clear of it too (hand drums R7, integration). Default: none.</summary>
        IReadOnlyList<FurnitureBlocker> Decor => System.Array.Empty<FurnitureBlocker>();
    }
}
