// The player's real walls for the Room's wall paint (Core's IRealWalls; Room/Decor/WallPaint.cs): the scene model's walls
// (MRUK WallAnchors: their plane rectangles, the anchor's +Z faces into the room) and what must stay clear on them (the
// windows, doors and wall art as thin boxes, every furniture volume), read again on Refresh (a playback's start); with no
// scene model, the depth sensor's wall along a ray (MrModule's EnvironmentRaycastManager, the same flat-upright check as the
// snowy window's). Registered by MrModule.Init. Main thread.
using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.MR
{
    public sealed class MrPaintWalls : IRealWalls
    {
        public delegate bool DepthRay(Ray ray, out Vector3 point, out Vector3 inward);

        /// <summary>Plane anchors drawn over nothing (thin boxes this deep, m) and the labels of what stays clear.</summary>
        public const float PlaneDepth = 0.12f;
        public const MRUKAnchor.SceneLabels PlaneBlocks = MRUKAnchor.SceneLabels.WINDOW_FRAME | MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WALL_ART;
        public const MRUKAnchor.SceneLabels NotBlocks = MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE
            | MRUKAnchor.SceneLabels.INNER_WALL_FACE | MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.GLOBAL_MESH;

        readonly Func<MRUKRoom> _room;
        readonly DepthRay _depth;
        readonly List<RealWall> _walls = new List<RealWall>(16);
        readonly List<RealWallBlock> _blocks = new List<RealWallBlock>(32);
        int _fingerprint;

        public MrPaintWalls(Func<MRUKRoom> room, DepthRay depth)
        {
            _room = room;
            _depth = depth;
        }

        public int Version { get; private set; }
        public int WallCount => _walls.Count;
        public RealWall Wall(int i) => _walls[i];
        public int BlockCount => _blocks.Count;
        public RealWallBlock Block(int i) => _blocks[i];

        public bool Refresh()
        {
            _walls.Clear();
            _blocks.Clear();
            MRUKRoom room = null;
            try { room = _room?.Invoke(); } catch (Exception) { room = null; }
            int fp = 17;
            if (room != null)
            {
                foreach (MRUKAnchor a in room.WallAnchors)
                {
                    if (a == null || !a.PlaneRect.HasValue) continue;
                    Transform t = a.transform;
                    Rect r = a.PlaneRect.Value;
                    Vector3 inward = t.forward;
                    if (Mathf.Abs(inward.y) > 0.3f) continue;
                    _walls.Add(new RealWall { Centre = t.TransformPoint(r.center.x, r.center.y, 0f), Inward = inward, Size = r.size });
                    fp = fp * 31 + Hash(t.position) + Hash(r.size);
                }
                foreach (MRUKAnchor a in room.Anchors)
                {
                    if (a == null || a.HasAnyLabel(NotBlocks)) continue;
                    Transform t = a.transform;
                    if (a.HasAnyLabel(PlaneBlocks) && a.PlaneRect.HasValue)
                    {
                        Rect r = a.PlaneRect.Value;
                        _blocks.Add(new RealWallBlock { Centre = t.TransformPoint(r.center.x, r.center.y, 0f), Half = new Vector3(r.width * 0.5f, r.height * 0.5f, PlaneDepth), Rotation = t.rotation });
                    }
                    else if (a.VolumeBounds.HasValue)
                    {
                        Bounds b = a.VolumeBounds.Value;
                        _blocks.Add(new RealWallBlock { Centre = t.TransformPoint(b.center), Half = b.extents, Rotation = t.rotation });
                    }
                    else continue;
                    fp = fp * 31 + Hash(t.position);
                }
            }
            if (fp != _fingerprint) { _fingerprint = fp; Version++; }
            return _walls.Count > 0;
        }

        static int Hash(Vector3 v) => Mathf.RoundToInt(v.x * 100f) * 73856093 ^ Mathf.RoundToInt(v.y * 100f) * 19349663 ^ Mathf.RoundToInt(v.z * 100f) * 83492791;
        static int Hash(Vector2 v) => Mathf.RoundToInt(v.x * 100f) * 73856093 ^ Mathf.RoundToInt(v.y * 100f) * 19349663;

        public bool DepthWall(Ray ray, out Vector3 point, out Vector3 inward)
        {
            point = inward = default;
            return _depth != null && _depth(ray, out point, out inward);
        }
    }
}
