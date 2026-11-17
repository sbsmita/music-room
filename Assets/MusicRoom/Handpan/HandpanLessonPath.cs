// Where a lesson block flies (handpan.md 8.1, revision 2; lessons.mjs blockPath): in along the player's line of sight to the
// field's dimple apex, as the harp's capsules do, with a forward clamp so it never starts closer than 0.26 m to the eyes or
// 0.16 m to the eyes' plane. Pure maths: the platform's ILessonTargets.TryGetLanding (phase B) returns the landing pose
// (the apex, oriented to the field normal) and the approach direction from this.
namespace MusicRoom.Handpan
{
    public struct HandpanBlockPath
    {
        public D3 Apex, Normal, Dir, Start;
        /// <summary>How far the block travels (m): min(TRAVEL, |eyes - apex| - EYE_GAP, the forward clamp).</summary>
        public double Room;
        public double StartToEye, StartZ, HarpRuleStartZ;

        /// <summary>The block's position when it is `dist` metres (remaining time x SPEED) from landing.</summary>
        public D3 At(double dist) => Apex + Dir * System.Math.Min(System.Math.Max(0, dist), Room);
    }

    public static class HandpanLessonPath
    {
        public static HandpanBlockPath Of(int field, in HandpanPlace place, D3 eyes, double travel = HandpanSpec.LessonTravel)
        {
            D3 apex = HandpanMath.FieldApexWorld(field, place, out D3 nw);
            D3 v = eyes - apex;
            double len = v.Length;
            D3 dir = v * (1 / len);
            D3 c = place.ToWorld(new D3(0, 0, 0));
            D3 fwd = new D3(c.X - eyes.X, 0, c.Z - eyes.Z).Normalized;
            double Zf(D3 q) => D3.Dot(q - eyes, fwd);
            double dd = D3.Dot(dir, fwd);
            double room = System.Math.Min(travel, len - HandpanSpec.LessonEyeGap);
            if (dd < 0) room = System.Math.Min(room, (Zf(apex) - HandpanSpec.LessonZMin) / -dd);
            room = System.Math.Max(0, room);
            D3 start = apex + dir * room;
            D3 harp = apex + dir * System.Math.Min(travel, len - HandpanSpec.LessonEyeGap);
            return new HandpanBlockPath
            {
                Apex = apex, Normal = nw, Dir = dir, Room = room, Start = start,
                StartToEye = D3.Dist(start, eyes), StartZ = Zf(start), HarpRuleStartZ = Zf(harp),
            };
        }

        /// <summary>The seat's eyes (seat frame: the seated head).</summary>
        public static D3 SeatEyes => new D3(HandpanSpec.SeatHead[0], HandpanSpec.SeatHead[1], HandpanSpec.SeatHead[2]);
    }
}
