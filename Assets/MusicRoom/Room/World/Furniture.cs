// The VR room's furniture as data (12-new-instruments/platform.md 4.1, Core IRoomFurniture): every piece a displayed
// instrument keeps 3 cm clear of, measured from the real geometry while RoomBuilder builds it. RoomBuilder opens a
// Footprint around each piece (the bench, the bookshelf, the floor lamp and its shade, the loveseat, the coffee table,
// the fireplace with its hearth, the inside sill, the window frame, each plant) and every vertex it appends while the
// footprint is open grows the footprint's box in the piece's own frame (the right-handed source frame, as the room is built); the two
// curtain panels are measured from their own vertices in the fabric batch. RoomFurniture turns the footprints into
// FurnitureBlockers in room coordinates (Unity: centre, half sizes, yaw in degrees; heights above the floor). A piece
// whose frame is square to the room (every one but the loveseat) is published axis-aligned and clamped to the room's
// interior (the hearth stone, set 1.5 mm into the wall, ends at the wall). The window view is the window frame above
// 0.85 m, as deep as the inside sill (platform.md 4.1: nothing taller than 0.85 m stands in front of the glass). A floor
// plant's body (pot, soil, stems) is its blocker; its spread of leaves, up to 0.9 m from the pot, is Decor, as is what
// stands on a blocker (the fern on the bookshelf, the ivy on the sill, the candle on the coffee table): measured the same
// way, published separately, because the display plan was laid out without it (README, "Furniture"). Plants grow by
// their sway at the tip.
// The room faces the fire (Core's RoomFrame, 2026-11-11): the footprints and RoomBlockers / RoomDecor are in the ROOM
// frame (the numbers the room is built with); IRoomFurniture (FloorBlockers, Decor) publishes them turned into the world.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Room.Geometry;
using UnityEngine;

namespace MusicRoom.Room.World
{
    /// <summary>One piece of furniture measured while it is built: the box of every vertex appended while it was open, in
    /// the piece's frame (source frame; the frame is a translation and a yaw).</summary>
    public sealed class Footprint
    {
        public readonly string Name;
        /// <summary>Decor on a blocker (RoomFurniture.Decor), not a blocker itself.</summary>
        public readonly bool Decor;
        /// <summary>The piece's frame, source frame.</summary>
        public readonly Matrix4x4 Frame;
        readonly Matrix4x4 _inv;
        /// <summary>The box in the frame (source frame); valid once Any.</summary>
        public Vector3 Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        public bool Any;
        /// <summary>Extra horizontal clearance on every side (m): a plant's sway at its top.</summary>
        public float Grow;
        public int Vertices;

        public Footprint(string name, Matrix4x4 frame, bool decor = false)
        {
            Name = name;
            Frame = frame;
            _inv = frame.inverse;
            Decor = decor;
        }

        /// <summary>The vertices of <paramref name="g"/> placed by <paramref name="m"/> (source-frame world).</summary>
        public void Extend(Geo g, Matrix4x4 m)
        {
            Matrix4x4 k = _inv * m;
            for (int i = 0; i < g.P.Count; i++) Add(k.MultiplyPoint3x4(g.P[i]));
        }

        /// <summary>Vertices [from, to) of <paramref name="g"/>, already in source-frame world space (a batch).</summary>
        public void ExtendWorld(Geo g, int from, int to)
        {
            for (int i = from; i < to && i < g.P.Count; i++) Add(_inv.MultiplyPoint3x4(g.P[i]));
        }

        void Add(Vector3 l)
        {
            Min = Vector3.Min(Min, l);
            Max = Vector3.Max(Max, l);
            Any = true;
            Vertices++;
        }

        /// <summary>The frame's Unity yaw (degrees, clockwise from above).</summary>
        public float YawDeg
        {
            get
            {
                Vector3 f = Rh.ToUnity(Frame.rotation) * Vector3.forward;
                return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>The frame origin in Unity coordinates.</summary>
        public Vector3 Origin => Rh.ToUnity((Vector3)Frame.GetColumn(3));

        /// <summary>The box centre in Unity world coordinates (y: the middle of the height range).</summary>
        public Vector3 Centre
        {
            get
            {
                // Source-frame local -> Unity local is the z mirror; the frame's rotation mirrors with it (Rh.ToUnity).
                var c = new Vector3((Min.x + Max.x) * 0.5f, (Min.y + Max.y) * 0.5f, -(Min.z + Max.z) * 0.5f);
                return Origin + Rh.ToUnity(Frame.rotation) * c;
            }
        }

        /// <summary>As a blocker: axis-aligned and clamped to <paramref name="room"/> when the frame is square to the room,
        /// else oriented by the frame's yaw.</summary>
        public FurnitureBlocker ToBlocker(Bounds room) => ToBlocker(Name, room);

        public FurnitureBlocker ToBlocker(string name, Bounds room)
        {
            Vector3 c = Centre;
            float hx = (Max.x - Min.x) * 0.5f + Grow, hz = (Max.z - Min.z) * 0.5f + Grow;
            float y0 = Origin.y + Min.y, y1 = Origin.y + Max.y;
            float yaw = YawDeg;
            float quarter = Mathf.Repeat(yaw + 45f, 90f) - 45f;   // the yaw's distance from the nearest multiple of 90
            if (Mathf.Abs(quarter) > 0.01f) return new FurnitureBlocker(name, c.x, c.z, hx, hz, y0, y1, yaw);
            // Square to the room: world-axis half sizes (a quarter turn swaps them), clamped to the room's interior.
            bool swap = Mathf.Abs(Mathf.Repeat(yaw, 180f) - 90f) < 45f;
            float ex = swap ? hz : hx, ez = swap ? hx : hz;
            float x0 = Mathf.Max(c.x - ex, room.min.x), x1 = Mathf.Min(c.x + ex, room.max.x);
            float z0 = Mathf.Max(c.z - ez, room.min.z), z1 = Mathf.Min(c.z + ez, room.max.z);
            y0 = Mathf.Max(y0, room.min.y);
            y1 = Mathf.Min(y1, room.max.y);
            return new FurnitureBlocker(name, (x0 + x1) * 0.5f, (z0 + z1) * 0.5f, (x1 - x0) * 0.5f, (z1 - z0) * 0.5f, y0, y1);
        }

        public override string ToString() => $"{Name}: {Vertices} vertices, local {Min:F3}..{Max:F3}";
    }

    /// <summary>IRoomFurniture: the blockers in the order of the display plan's list (platform.md 4.1), and the decor.</summary>
    public sealed class RoomFurniture : IRoomFurniture
    {
        /// <summary>The display plan's blockers, in its order (its FURNITURE table).</summary>
        public static readonly string[] BlockerNames =
        {
            Bookshelf, FloorLamp, LampShade, "plant P1", "plant P2", "plant P3", "plant P4", Loveseat, CoffeeTable, Hearth,
            CurtainNear, CurtainFar, InsideSill, WindowView, Bench,
        };
        public const string Bookshelf = "bookshelf", FloorLamp = "floor lamp", LampShade = "lamp shade", Loveseat = "loveseat",
            CoffeeTable = "coffee table", Hearth = "hearth", CurtainNear = "curtain near", CurtainFar = "curtain far",
            InsideSill = "inside sill", WindowView = "window view", Bench = "bench", WindowFrame = "window frame";
        /// <summary>The flowers (decor on the hearth, the sill and the bookshelf: World/FlowerBuilder.cs).</summary>
        public const string MantelFlowers = "flowers on the mantel", SillFlowers = "flowers on the sill", ShelfFlowers = "flowers on the bookshelf";
        /// <summary>platform.md 4.1: nothing taller than this stands in front of the window glass.</summary>
        public const float WindowViewFrom = 0.85f;

        readonly List<FurnitureBlocker> _blockers = new List<FurnitureBlocker>(16);
        readonly List<FurnitureBlocker> _decor = new List<FurnitureBlocker>(4);
        readonly List<FurnitureBlocker> _worldBlockers = new List<FurnitureBlocker>(16);
        readonly List<FurnitureBlocker> _worldDecor = new List<FurnitureBlocker>(4);

        /// <summary>The blockers in the WORLD (the room turned to face the fire: RoomFrame.ToWorld of RoomBlockers).</summary>
        public IReadOnlyList<FurnitureBlocker> FloorBlockers => _worldBlockers;
        /// <summary>Measured decor in the WORLD: the floor plants' leaves ("plant P1 leaves" ...) and what stands on a blocker
        /// (the fern on the bookshelf, the ivy on the sill, the coffee-table candle, the flowers on the mantel, the sill and
        /// the bookshelf).</summary>
        public IReadOnlyList<FurnitureBlocker> Decor => _worldDecor;
        /// <summary>The blockers in ROOM coordinates (the room's own frame: the display plan's reference table).</summary>
        public IReadOnlyList<FurnitureBlocker> RoomBlockers => _blockers;
        /// <summary>The decor in ROOM coordinates.</summary>
        public IReadOnlyList<FurnitureBlocker> RoomDecor => _decor;

        /// <summary>From the footprints RoomBuilder measured (RoomScene.Footprints); room = the interior in room coordinates
        /// (RoomFrame.LocalBox). The world lists are the room's turned by RoomFrame.</summary>
        public static RoomFurniture From(IReadOnlyList<Footprint> footprints, Bounds room)
        {
            var f = new RoomFurniture();
            Footprint Find(string name)
            {
                for (int i = 0; i < footprints.Count; i++) if (footprints[i].Name == name && footprints[i].Any) return footprints[i];
                return null;
            }
            foreach (string name in BlockerNames)
            {
                if (name == WindowView)
                {
                    Footprint frame = Find(WindowFrame), sill = Find(InsideSill);
                    if (frame == null || sill == null) continue;
                    // The frame above 0.85 m, from the sill's inner edge to the wall.
                    FurnitureBlocker fb = frame.ToBlocker(WindowView, room), sb = sill.ToBlocker(InsideSill, room);
                    float x0 = sb.Centre.x - sb.Half.x, x1 = room.max.x;
                    f._blockers.Add(new FurnitureBlocker(WindowView, (x0 + x1) * 0.5f, fb.Centre.y, (x1 - x0) * 0.5f, fb.Half.y, Mathf.Max(WindowViewFrom, fb.Y0), fb.Y1));
                    continue;
                }
                Footprint p = Find(name);
                if (p != null) f._blockers.Add(p.ToBlocker(room));
            }
            for (int i = 0; i < footprints.Count; i++)
                if (footprints[i].Decor && footprints[i].Any) f._decor.Add(footprints[i].ToBlocker(room));
            for (int i = 0; i < f._blockers.Count; i++) f._worldBlockers.Add(RoomFrame.ToWorld(f._blockers[i]));
            for (int i = 0; i < f._decor.Count; i++) f._worldDecor.Add(RoomFrame.ToWorld(f._decor[i]));
            return f;
        }

        /// <summary>A blocker or decor by name, in the WORLD (world = false: in room coordinates).</summary>
        public bool TryGet(string name, out FurnitureBlocker blocker, bool world = true)
        {
            IReadOnlyList<FurnitureBlocker> b = world ? _worldBlockers : _blockers, d = world ? _worldDecor : _decor;
            for (int i = 0; i < b.Count; i++) if (b[i].Name == name) { blocker = b[i]; return true; }
            for (int i = 0; i < d.Count; i++) if (d[i].Name == name) { blocker = d[i]; return true; }
            blocker = default;
            return false;
        }

        /// <summary>The world corners of a blocker's floor rectangle (x, z), counter-clockwise from (-hx, -hz).</summary>
        public static void Corners(in FurnitureBlocker b, Vector2[] into)
        {
            float a = b.Yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            for (int k = 0; k < 4; k++)
            {
                float lx = (k == 1 || k == 2) ? b.Half.x : -b.Half.x, lz = k >= 2 ? b.Half.y : -b.Half.y;
                into[k] = new Vector2(b.Centre.x + lx * c + lz * s, b.Centre.y - lx * s + lz * c);
            }
        }
    }
}
