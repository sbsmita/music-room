// The player's own placement of a layout ("Move instruments"), kept in AppSettings between launches. Each rig's pose
// is stored relative to the seat it was arranged around (the head on the floor and its heading, LayoutModule's seat
// frame), so the next launch lays the instruments out the same way around wherever the player sits now: x right and
// z ahead of the head in metres, y the absolute height above the floor (a raised piano stays raised), yaw relative to
// the heading in degrees. Text format "a1;Piano:x,y,z,yaw;Drums:...", invariant culture. Pure code: EditMode tests.
// My set (platform.md 4.7): the full room's key is merged on save (Merge: entries of rigs not at the seat are kept), and a
// saved pose that now collides is dropped for the planner's (Usable).
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>A rig's pose in the seat frame: Local = (x right, y height above the floor, z ahead), Yaw degrees.</summary>
    public struct SeatPose
    {
        public Vector3 Local;
        public float Yaw;
        public override string ToString() => $"({Local.x:F3}, {Local.y:F3}, {Local.z:F3}) yaw {Yaw:F1}";
    }

    public static class ArrangementCodec
    {
        public const string Version = "a1";

        /// <summary>A world pose (rig origin, heading in degrees) in the seat frame of (<paramref name="seatHead"/>,
        /// <paramref name="seatYawRad"/>).</summary>
        public static SeatPose ToSeat(Vector3 worldPos, float worldYawDeg, Vector3 seatHead, float seatYawRad)
        {
            LayoutMath.RotY(worldPos.x - seatHead.x, worldPos.z - seatHead.z, -seatYawRad, out float x, out float z);
            return new SeatPose { Local = new Vector3(x, worldPos.y, z), Yaw = WrapDeg(worldYawDeg - seatYawRad * Mathf.Rad2Deg) };
        }

        /// <summary>The world pose of a seat pose about (<paramref name="seatHead"/>, <paramref name="seatYawRad"/>).</summary>
        public static void FromSeat(in SeatPose p, Vector3 seatHead, float seatYawRad, out Vector3 worldPos, out float worldYawDeg)
        {
            worldPos = LayoutMath.SeatPlace(seatHead, seatYawRad, p.Local);
            worldYawDeg = WrapDeg(p.Yaw + seatYawRad * Mathf.Rad2Deg);
        }

        public static float WrapDeg(float d)
        {
            d %= 360f;
            if (d > 180f) d -= 360f;
            else if (d <= -180f) d += 360f;
            return d;
        }

        public static string Encode(IReadOnlyList<KeyValuePair<RigId, SeatPose>> poses)
        {
            if (poses == null || poses.Count == 0) return "";
            var sb = new StringBuilder(Version, 160);
            for (int i = 0; i < poses.Count; i++)
            {
                SeatPose p = poses[i].Value;
                sb.Append(';').Append(poses[i].Key.ToString()).Append(':')
                  .Append(F(p.Local.x)).Append(',').Append(F(p.Local.y)).Append(',').Append(F(p.Local.z)).Append(',').Append(F(p.Yaw));
            }
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>Reads what Encode wrote into <paramref name="into"/> (cleared first). A wrong version, a rig name it does
        /// not know or a malformed number drops that entry (or everything for a wrong version); returns the entries read.</summary>
        public static int Decode(string text, Dictionary<RigId, SeatPose> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text)) return 0;
            string[] parts = text.Split(';');
            if (parts.Length == 0 || parts[0] != Version) return 0;
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                if (!System.Enum.TryParse(part.Substring(0, colon), out RigId id) || !System.Enum.IsDefined(typeof(RigId), id)) continue;
                string[] v = part.Substring(colon + 1).Split(',');
                if (v.Length != 4) continue;
                if (!P(v[0], 50f, out float x) || !P(v[1], 50f, out float y) || !P(v[2], 50f, out float z) || !P(v[3], 720f, out float yaw)) continue;
                into[id] = new SeatPose { Local = new Vector3(x, y, z), Yaw = WrapDeg(yaw) };
            }
            return into.Count;
        }

        /// <summary>Merge on save (platform.md 4.7): <paramref name="current"/> (the rigs at the seat now) replaces their
        /// entries in <paramref name="previous"/>; the entries of every other rig are kept, so an instrument that comes back
        /// to My set returns to where the player put it.</summary>
        public static string Merge(string previous, IReadOnlyList<KeyValuePair<RigId, SeatPose>> current)
        {
            var old = new Dictionary<RigId, SeatPose>();
            Decode(previous, old);
            var all = new List<KeyValuePair<RigId, SeatPose>>(current.Count + old.Count);
            for (int i = 0; i < current.Count; i++) all.Add(current[i]);
            for (int id = 0; id < 16; id++)
            {
                var rid = (RigId)id;
                if (!old.TryGetValue(rid, out SeatPose p)) continue;
                bool there = false;
                for (int i = 0; i < current.Count; i++) if (current[i].Key == rid) { there = true; break; }
                if (!there) all.Add(new KeyValuePair<RigId, SeatPose>(rid, p));
            }
            return Encode(all);
        }

        /// <summary>Which saved poses a plan of My set may use (platform.md 4.7): every saved member and the side table, unless
        /// it now collides (SeatPlanner.Collides: its footprint overlaps another member's or the table's by more than the
        /// footprints' 3 cm tolerance); a colliding one is dropped and its planner pose used (members in the plan's order, each
        /// checked against the poses chosen so far). With the table at its alternate pose its saved pose is not used. Entries
        /// of rigs outside the plan are kept for later and not returned. <paramref name="usable"/> is cleared first.</summary>
        public static void Usable(SeatPlanner planner, SeatPlanner.Plan plan, IReadOnlyDictionary<RigId, SeatPose> saved, HashSet<RigId> usable)
        {
            usable.Clear();
            if (planner == null || plan == null || !plan.Fits || saved == null) return;
            var chosen = new List<KeyValuePair<InstrumentKind, SeatPlanner.Pose2>>(plan.Members.Count);
            for (int i = 0; i < plan.Members.Count; i++)
            {
                SeatPlanner.Member m = plan.Members[i];
                chosen.Add(new KeyValuePair<InstrumentKind, SeatPlanner.Pose2>(m.Kind,
                    saved.TryGetValue(InstrumentIds.RigOf(m.Kind), out SeatPose sp) ? ToPose2(sp) : m.Pose));
            }
            SeatPlanner.Pose2 table = plan.Table;
            bool tableSaved = !plan.TableAlt && saved.TryGetValue(RigId.Table, out SeatPose ts);
            if (tableSaved) table = ToPose2(saved[RigId.Table]);
            if (tableSaved && planner.TableCollides(table, chosen)) { table = plan.Table; tableSaved = false; }
            for (int i = 0; i < plan.Members.Count; i++)
            {
                SeatPlanner.Member m = plan.Members[i];
                RigId rid = InstrumentIds.RigOf(m.Kind);
                if (!saved.ContainsKey(rid)) continue;
                if (planner.Collides(m.Kind, chosen[i].Value, chosen, table)) chosen[i] = new KeyValuePair<InstrumentKind, SeatPlanner.Pose2>(m.Kind, m.Pose);
                else usable.Add(rid);
            }
            if (tableSaved) usable.Add(RigId.Table);
        }

        /// <summary>A seat pose as the planner's floor pose (x, z, yaw).</summary>
        public static SeatPlanner.Pose2 ToPose2(in SeatPose p) => new SeatPlanner.Pose2(p.Local.x, p.Local.z, p.Yaw);

        /// <summary>A finite number no larger than <paramref name="limit"/> (metres: no room is 50 m; degrees: two turns).</summary>
        static bool P(string s, float limit, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) <= limit;
    }
}
