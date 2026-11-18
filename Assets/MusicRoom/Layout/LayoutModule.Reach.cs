// The reach band: every instrument opens within a foot of the seated player. Its default pose (seat home, or its My set
// slot) is pulled straight toward the player until the near edge of its playing surface (the floor rectangles of its
// played parts: the piano's and marimba's key / bar fronts, the drum heads, the harp's strings, the rims of the handpan,
// hand drums and bowls) is at most ReachTarget from the chest point (the head on the floor,
// ChestBack behind it; ChestDown below the eyes is where the hands rest, which a horizontal measure does not need). The
// pull keeps the instrument's heading and height; the resolver then keeps it clear of the eyes, the seat and the other
// instruments, so in a crowded set a member may end a little farther out (MeasureReach reports every distance). The
// player's comfort offsets (ComfortCodec) are relative to this pulled pose, so a deliberate drag farther away is kept.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        /// <summary>The band: the playing surface's near edge at most ReachBand (0.45 m, 1.5 ft since 2026-11-18; was 0.30)
        /// horizontally from the chest point. The pull aims at ReachTarget, then the playing room (PlayRoomNear) moves it out
        /// to about 0.39 m.</summary>
        public const float ReachBand = 0.45f, ReachTarget = 0.27f, ChestDown = 0.25f, ChestBack = 0.10f;

        /// <summary>Boots pull instruments into the reach band. The PlayMode tests that compare the placement solver with its
        /// frozen reference layouts turn it off for their boot (the reference poses are the seat homes).</summary>
        public static bool ReachBandDefault = true;
        public bool ReachBandOn = true;

        /// <summary>The chest point on the floor in the seat frame (x right, z ahead of the head).</summary>
        public static Vector2 ChestSeat => new Vector2(0f, -ChestBack);

        /// <summary>The horizontal distance from the chest point to the nearest played-part rectangle of a rig standing at
        /// <paramref name="home"/> / <paramref name="yawDeg"/> in the seat frame (+inf without played parts).</summary>
        public static float NearEdge(RigEntry e, Vector3 home, float yawDeg)
        {
            if (e == null || e.PlayRects.Count == 0) return float.PositiveInfinity;
            Vector2 c = ChestSeat;
            LayoutMath.RotY(c.x - home.x, c.y - home.z, -yawDeg * Mathf.Deg2Rad, out float lx, out float lz);
            float best = float.PositiveInfinity;
            for (int i = 0; i < e.PlayRects.Count; i++)
            {
                LocalRect r = e.PlayRects[i];
                float dx = Mathf.Max(0f, Mathf.Abs(lx - r.X) - r.Hx), dz = Mathf.Max(0f, Mathf.Abs(lz - r.Z) - r.Hz);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        /// <summary>A little room in front of the player (2026-11-18, headset: "intruments are appearing very near. keep little
        /// diff of may half foot"): after the pull, an instrument whose playing surface's near edge is closer than
        /// PlayRoomNear to the chest point moves straight out (along the line from the head to its play centre) by up to
        /// PlayRoomMax, never so far that its play centre leaves the descriptor's Seat.ReachMax (still played seated); only an
        /// instrument alone in front (One alone, a My set of one: My set keeps the planner's slots). Measured (DeckFrontTests, Part 3 of TUNE-DECK-FRONT):
        /// the near edge 0.21-0.27 m before, about 0.34-0.39 m now; the harp (0.44 m, kept by the eye clearance) does not move.</summary>
        public const float PlayRoomNear = 0.39f, PlayRoomMax = 0.30f;

        /// <summary>While a recording plays back by itself in the VR room, the instruments it brought stand this much farther
        /// out (m, 3 ft, 2026-11-18 evening: "make all the instruments more far ... may be 3 feets"; was 0.45, then 0.60 m)
        /// and side by side on an arc in view (LayoutModule.Stage PushShowOut) so the player watches them play (2026-11-18: "when we play recording ... place all intruments little
        /// far like 2 feets ... or 1 and half feet"). Back at the playing distance the moment the player plays along (record on
        /// top, a loop overdub, REC) or takes the layout over, and the player's own layout comes back when it ends.</summary>
        public const float StageFarther = 0.90f;

        /// <summary>Moves a seat-frame default pose of an instrument toward the player, along the line from its play centre
        /// to the head, until its playing surface's near edge is within ReachTarget of the chest point; then out again by the
        /// playing room (PlayRoomNear / PlayRoomMax). Leaves the
        /// side table, the menu and rigs without played parts alone.</summary>
        void PullIntoReach(Rig r, ref Vector3 home, float yawDeg)
        {
            if (!ReachBandOn || r == null || !IsRowRig(r.Id)) return;
            RigEntry e = Placement.Entry(r);
            if (e == null || e.PlayRects.Count == 0) return;
            Vector2 c = PlayCentreOf(r);
            for (int k = 0; k < 4; k++)
            {
                float d = NearEdge(e, home, yawDeg);
                if (!(d > ReachTarget + 0.002f) || float.IsInfinity(d)) break;
                LayoutMath.RotY(c.x, c.y, yawDeg * Mathf.Deg2Rad, out float cx, out float cz);
                Vector2 b = ComfortCodec.Bearing(new Vector2(home.x + cx, home.z + cz));
                float step = d - ReachTarget;
                home.x -= b.x * step;
                home.z -= b.y * step;
            }
            float near = NearEdge(e, home, yawDeg);
            if (float.IsInfinity(near)) return;
            LayoutMath.RotY(c.x, c.y, yawDeg * Mathf.Deg2Rad, out float px, out float pz);
            Vector2 centre = new Vector2(home.x + px, home.z + pz);
            Vector2 dir = ComfortCodec.Bearing(centre);
            // The playing room only for an instrument alone in front (One alone, a My set of one), straight ahead. My set keeps
            // the planner's slots: moving its front member out made the resolver push a side member into the side table.
            bool alone = _mode == LayoutMode.Single || (_plan != null && _plan.Single);
            float room = alone && dir.y >= 0.9f ? Mathf.Clamp(PlayRoomNear - near, 0f, PlayRoomMax) : 0f;
            InstrumentDescriptor desc = _app?.Registry?.Get(KindOf(r.Id));
            if (desc?.Seat != null && desc.Seat.ReachMax > 0f) room = Mathf.Min(room, Mathf.Max(0f, desc.Seat.ReachMax - centre.magnitude));
            if (room <= 0f) return;
            home.x += dir.x * room;
            home.z += dir.y * room;
        }

        /// <summary>One alone's default pose of an instrument (seat frame): its single home with its play centre (the piano's
        /// mid key depth, the kit's mean piece, the harp's middle string, the descriptor's PlayCentre for the rest) moved
        /// onto the player's forward line, then pulled into the reach band straight toward the chest. Its heading and height
        /// are the single home's. The same pose every time it comes alone, whichever way it came (2026-11-17).</summary>
        void SingleHome(Rig r, out Vector3 home)
        {
            home = r.HomeSingle;
            CentreOnFrontLine(r, ref home, r.HomeYawSingle);
            PullIntoReach(r, ref home, r.HomeYawSingle);
        }

        /// <summary>Moves a seat-frame pose sideways so its play centre is straight ahead of the head (seat x = 0). With the
        /// reach band (the frozen reference layouts keep the homes as built).</summary>
        void CentreOnFrontLine(Rig r, ref Vector3 home, float yawDeg)
        {
            if (!ReachBandOn || r == null || !IsRowRig(r.Id)) return;
            Vector2 c = PlayCentreOf(r);
            LayoutMath.RotY(c.x, c.y, yawDeg * Mathf.Deg2Rad, out float cx, out _);
            home.x = -cx;
        }

        /// <summary>One instrument's reach as it stands now: the horizontal distance from the player's chest point (the head
        /// now, ChestBack behind it along the head's heading) to its playing surface's near edge.</summary>
        public float ReachOf(Rig r)
        {
            RigEntry e = Placement.Entry(r);
            if (e == null || r.Group == null || _app?.Head == null) return float.PositiveInfinity;
            ReadSeat(out Vector3 head, out float yaw);
            Transform g = r.Group;
            // The rig's pose in the seat frame of the head now.
            LayoutMath.RotY(g.position.x - head.x, g.position.z - head.z, -yaw, out float sx, out float sz);
            float ryaw = (LayoutMath.YawOf(g) - yaw) * Mathf.Rad2Deg;
            return NearEdge(e, new Vector3(sx, g.position.y, sz), ryaw);
        }

        /// <summary>After an arrangement: an instrument the resolver had to leave outside the band is logged with why (its
        /// eye distance, the walls) so a device log shows which ones and how far.</summary>
        void ReportReach()
        {
            if (!ReachBandOn || !Presenting) return;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id) || !r.InScene || r.Group == null || !r.Group.gameObject.activeInHierarchy) continue;
                float d = ReachOf(r);
                if (!(d > ReachBand + 0.005f) || float.IsInfinity(d)) continue;
                Debug.Log($"[Layout] reach: {r.Id} opens {d:F2} m from the chest (band {ReachBand:F2}); eyes {EyeDistance(r):F2} m, resolver moves {Placement.LastMoves}");
            }
        }

        /// <summary>Every at-seat instrument's reach now (tests, the report): kind and distance.</summary>
        public void MeasureReach(List<KeyValuePair<InstrumentKind, float>> into)
        {
            into.Clear();
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (!IsRowRig(r.Id) || !r.InScene || r.Group == null || !r.Group.gameObject.activeInHierarchy) continue;
                into.Add(new KeyValuePair<InstrumentKind, float>(KindOf(r.Id), ReachOf(r)));
            }
        }
    }
}
