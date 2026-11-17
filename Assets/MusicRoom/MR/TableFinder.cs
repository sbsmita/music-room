// Real tables from the user's Space Setup, for putting the piano on a real table in "Play in my room"
// (onTable = hit.y > 0.3 above the floor, as in a hit-test placement). MRUK TABLE anchors are
// volumes whose transform sits on the top surface with local +Z pointing up (verified: MRUKAnchor.cs:603,
// MRUKRoom.cs:971-972, sample JSON rotation x = 270); the top rectangle spans local X/Y.
using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MusicRoom.MR
{
    public struct TableSurface
    {
        public MRUKAnchor Anchor;
        public Vector3 TopCenter;    // world, centre of the top rectangle
        public Vector2 TopSize;      // m, along the anchor's local X and local Y (both horizontal)
        public float TopY;           // world y of the top
        public float TopHeight;      // m above the room's floor anchor
        public Vector3 EdgeMidpoint; // world, on the top, middle of the edge nearest the user
        public Vector3 TowardUser;   // world, horizontal unit normal of that edge, pointing at the user
        public float EdgeLength;     // m, length of that edge (room for the piano's 1.326 m body)
        public float Depth;          // m, extent of the top away from that edge
        public float Distance;       // m, horizontal: user to the nearest point of the top (0 when above it)
        public float Score;          // lower is better

        // For a piano model converted from the right-handed frame by z -> -z (+z toward the player becomes Unity -Z):
        // local +Z points away from the user.
        public Quaternion FacingUser => Quaternion.LookRotation(-TowardUser, Vector3.up);
    }

    [Serializable]
    public struct TableQuery
    {
        public float maxDistance;        // m from the user's head (horizontal) to the nearest point of the top
        public float minHeight;          // m above the floor; hits above 0.3 m count as a table
        public float maxHeight;          // m; counters and shelves above this are skipped
        public float preferredMinHeight; // m; comfortable seated playing band ...
        public float preferredMaxHeight; // ... outside it every metre costs heightWeight metres of score
        public float heightWeight;
        public float minEdgeLength;      // m; shorter edges are rejected
        public float minDepth;           // m
        public float wantedEdgeLength;   // m; the piano body width, shorter edges cost edgeWeight per metre
        public float edgeWeight;
        public float behindWeight;       // m of score for a table straight behind the user (0 in front)

        public static TableQuery Default => new TableQuery
        {
            maxDistance = 2.5f,
            minHeight = 0.30f,
            maxHeight = 1.20f,
            preferredMinHeight = 0.55f,
            preferredMaxHeight = 0.95f,
            heightWeight = 3f,
            minEdgeLength = 0.50f,
            // The piano body reaches BodyRear (.29 m) behind the key fronts, so a top shallower than that plus a margin
            // cannot hold it; deeper tops up to PushBack + BodyRear + EdgeMargin (.56 m) take it with a shorter push
            // (PianoPoseOnTable).
            minDepth = TableFinder.BodyRear + TableFinder.EdgeMargin,
            wantedEdgeLength = 1.326f,
            edgeWeight = 0.5f,
            behindWeight = 0.5f,
        };
    }

    public class TableFinder : MonoBehaviour
    {
        public const float PushBack = 0.25f;  // PUSH_BACK: key fronts this far past the chosen edge point
        public const float TableLift = 0.092f; // tableLift: body underside below the white key tops
        public const float BodyRear = 0.29f;   // REAR: the body's back this far behind the key fronts
        public const float EdgeMargin = 0.02f; // m kept free between the body's back and the far edge of the top

        [Tooltip("The player's head; defaults to Camera.main.")]
        public Transform head;
        public TableQuery query = TableQuery.Default;

        public bool TryGetBestTable(out TableSurface best)
        {
            best = default;
            var mruk = MRUK.Instance;
            var room = mruk != null ? mruk.GetCurrentRoom() : null;
            var h = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
            if (room == null || h == null) return false;
            return TryFindBest(room, h.position, h.forward, query, out best);
        }

        // Best TABLE of the room for the user at headPosition, or false when none passes the query.
        public static bool TryFindBest(MRUKRoom room, Vector3 headPosition, Vector3 headForward, in TableQuery q,
            out TableSurface best, List<TableSurface> allCandidates = null)
        {
            best = default;
            bool found = false;
            if (room == null) return false;
            float floorY = FloorY(room);
            Vector3 fwd = new Vector3(headForward.x, 0f, headForward.z);
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            foreach (var anchor in room.Anchors)
            {
                if (!TryDescribe(anchor, headPosition, floorY, out var s)) continue;
                if (s.Distance > q.maxDistance || s.TopHeight < q.minHeight || s.TopHeight > q.maxHeight) continue;
                if (s.EdgeLength < q.minEdgeLength || s.Depth < q.minDepth) continue;
                Vector3 toEdge = s.EdgeMidpoint - headPosition;
                toEdge.y = 0f;
                float facing = toEdge.sqrMagnitude > 1e-6f ? Vector3.Dot(fwd, toEdge.normalized) : 1f;
                s.Score = s.Distance
                    + q.heightWeight * (Mathf.Max(0f, q.preferredMinHeight - s.TopHeight) + Mathf.Max(0f, s.TopHeight - q.preferredMaxHeight))
                    + q.edgeWeight * Mathf.Max(0f, q.wantedEdgeLength - s.EdgeLength)
                    + q.behindWeight * 0.5f * (1f - facing);
                allCandidates?.Add(s);
                if (!found || s.Score < best.Score)
                {
                    best = s;
                    found = true;
                }
            }
            return found;
        }

        // The table the user points at (hand ray), with the hit point on its top: "pinch where you point".
        public static bool TryPointAtTable(MRUKRoom room, Ray ray, Vector3 headPosition, out TableSurface surface,
            out Vector3 hitPoint, float maxDistance = 6f)
        {
            surface = default;
            hitPoint = default;
            if (room == null) return false;
            var filter = new LabelFilter(MRUKAnchor.SceneLabels.TABLE);
            if (!room.Raycast(ray, maxDistance, filter, out RaycastHit hit, out MRUKAnchor anchor)) return false;
            if (hit.normal.y < 0.9f) return false; // the side of the table, not its top
            hitPoint = hit.point;
            return TryDescribe(anchor, headPosition, FloorY(room), out surface);
        }

        // Any upward real surface the ray hits (table-like tops or the floor), for the hit-test pinch.
        // onTable: more than 0.3 m above the floor (stand hidden, body on the top).
        public const MRUKAnchor.SceneLabels UpwardSurfaces = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.STORAGE
            | MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.OTHER | MRUKAnchor.SceneLabels.FLOOR;

        public static bool TryPointAtSurface(MRUKRoom room, Ray ray, out Vector3 point, out MRUKAnchor anchor,
            out bool onTable, float maxDistance = 6f)
        {
            point = default;
            anchor = null;
            onTable = false;
            if (room == null) return false;
            if (!room.Raycast(ray, maxDistance, new LabelFilter(UpwardSurfaces), out RaycastHit hit, out anchor)) return false;
            if (hit.normal.y < 0.9f) return false;
            point = hit.point;
            onTable = point.y - FloorY(room) > 0.3f;
            return true;
        }

        // Geometry of one TABLE anchor relative to the user. False for anything that is not an upward table top.
        public static bool TryDescribe(MRUKAnchor anchor, Vector3 headPosition, float floorY, out TableSurface s)
        {
            s = default;
            if (anchor == null || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) return false;
            Transform t = anchor.transform;
            if (Vector3.Dot(t.forward, Vector3.up) < 0.9f) return false;
            Rect top;
            float topZ;
            if (anchor.PlaneRect.HasValue)
            {
                top = anchor.PlaneRect.Value; // the top plane lies at local z = 0
                topZ = 0f;
            }
            else if (anchor.VolumeBounds.HasValue)
            {
                Bounds b = anchor.VolumeBounds.Value;
                top = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
                topZ = b.max.z;
            }
            else return false;

            Vector3 local = t.InverseTransformPoint(headPosition); // x, y across the top; z height above it
            Vector2 c = top.center;
            Vector2 half = top.size * 0.5f;
            Vector3 nearest = t.TransformPoint(Mathf.Clamp(local.x, top.xMin, top.xMax), Mathf.Clamp(local.y, top.yMin, top.yMax), topZ);

            // The edge facing the user: the side whose sector (split by the diagonals) holds the user,
            // the same choice as MRUKRoom.GetBestPoseFromRaycast (MRUKRoom.cs:774-800).
            float dx = local.x - c.x, dy = local.y - c.y;
            bool xSide = Mathf.Abs(dx) * Mathf.Max(half.y, 1e-3f) >= Mathf.Abs(dy) * Mathf.Max(half.x, 1e-3f);
            Vector3 edgeLocal, dirLocal;
            if (xSide)
            {
                float sign = dx >= 0f ? 1f : -1f;
                edgeLocal = new Vector3(c.x + sign * half.x, c.y, topZ);
                dirLocal = new Vector3(sign, 0f, 0f);
                s.EdgeLength = top.height;
                s.Depth = top.width;
            }
            else
            {
                float sign = dy >= 0f ? 1f : -1f;
                edgeLocal = new Vector3(c.x, c.y + sign * half.y, topZ);
                dirLocal = new Vector3(0f, sign, 0f);
                s.EdgeLength = top.width;
                s.Depth = top.height;
            }
            Vector3 toward = t.TransformDirection(dirLocal);
            toward.y = 0f;

            s.Anchor = anchor;
            s.TopCenter = t.TransformPoint(c.x, c.y, topZ);
            s.TopSize = top.size;
            s.TopY = s.TopCenter.y;
            s.TopHeight = s.TopY - floorY;
            s.EdgeMidpoint = t.TransformPoint(edgeLocal);
            s.TowardUser = toward.sqrMagnitude > 1e-8f ? toward.normalized : Vector3.forward;
            Vector3 d = nearest - headPosition;
            d.y = 0f;
            s.Distance = d.magnitude;
            return true;
        }

        // The piano's origin (front edge of the white key tops) on a table, as PianoPlacer.FromHit does:
        // at the chosen point plus TableLift, pushed PushBack away from the user, facing the user. On a top too shallow
        // for the full push the key fronts move toward the user just enough that the body (BodyRear behind them) still
        // ends EdgeMargin before the far edge (never past the chosen point).
        public static Pose PianoPoseOnTable(in TableSurface s, Vector3 chosenPoint)
        {
            Vector3 p = new Vector3(chosenPoint.x, s.TopY + TableLift, chosenPoint.z) - s.TowardUser * PushFor(s, chosenPoint);
            return new Pose(p, s.FacingUser);
        }

        /// <summary>How far the key fronts go beyond <paramref name="chosenPoint"/>: PushBack, or less on a shallow top.</summary>
        public static float PushFor(in TableSurface s, Vector3 chosenPoint)
        {
            Vector3 d = chosenPoint - s.EdgeMidpoint;
            d.y = 0f;
            float inset = Mathf.Max(0f, -Vector3.Dot(d, s.TowardUser)); // how far the point lies in from the near edge
            return Mathf.Clamp(s.Depth - inset - BodyRear - EdgeMargin, 0f, PushBack);
        }

        public static float FloorY(MRUKRoom room)
        {
            return room != null && room.FloorAnchors.Count > 0 ? room.FloorAnchors[0].transform.position.y : 0f;
        }
    }
}
