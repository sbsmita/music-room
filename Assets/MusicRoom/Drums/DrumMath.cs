// Pure formulas of the kit, the sticks and the nod kick, kept free of scene state so EditMode tests can pin them to the
// reference numbers (Docs/05-instruments.md curves) and every caller shares one definition. Allocation-free.
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    public static class DrumMath
    {
        /// <summary>Strike loudness from the downward speed through the head. Returns -1 when the
        /// stroke is too slow to sound (vy &lt; v0); otherwise clamp(((vy - v0)/(v1 - v0))^.7, .05, 1) so the gentlest
        /// stroke that counts is a quiet ghost note and a full stroke (3 m/s stick, 1.5 m/s finger) is the loudest.</summary>
        public static float StrikeVelocity(float vy, bool stick)
        {
            float v0 = stick ? DrumSpec.StickV0 : DrumSpec.FingerV0;
            float v1 = stick ? DrumSpec.StickV1 : DrumSpec.FingerV1;
            if (vy < v0) return -1f;
            float x = (vy - v0) / (v1 - v0);
            return Mathf.Clamp(Mathf.Pow(x, DrumSpec.VelocityExponent), DrumSpec.MinVelocity, 1f);
        }

        /// <summary>The modelled kit's implement for a pointer (drums.md 2.3): a stick's shoulder, its tip, or a fingertip
        /// (DrumSpec.FingertipImplement).</summary>
        public static DrumImplement ImplementOf(bool stick, bool shoulder) =>
            stick ? (shoulder ? DrumImplement.Shoulder : DrumImplement.Tip) : DrumSpec.FingertipImplement;

        /// <summary>Where on the piece a stroke landed (0 centre .. 1 edge) and the zone that shapes the
        /// tone: head or rim on a drum (no rim on the pad); bell, bow or edge on the crash and ride; top or edge on the
        /// hi-hat. <paramref name="open"/> is the hi-hat's state (false for every other piece).</summary>
        public static DrumStrike StrikeAt(DrumPieceType type, bool hiHat, bool pad, float r, float rr, bool open)
        {
            float pos = Mathf.Clamp(rr / r, 0f, 1f);
            DrumZone zone = DrumZone.Head;
            if (type == DrumPieceType.Cymbal)
            {
                if (hiHat) zone = pos > DrumSpec.HatEdgeFrom ? DrumZone.Edge : DrumZone.Top;
                else zone = pos < DrumSpec.BellBelow ? DrumZone.Bell : pos > DrumSpec.EdgeFrom ? DrumZone.Edge : DrumZone.Bow;
            }
            else if (!pad && rr > r * DrumSpec.RimFraction) zone = DrumZone.Rim;
            return new DrumStrike { Pos = pos, Zone = zone, Open = open };
        }

        /// <summary>Radius (in the head plane) where the path prev -> tip crossed y = 0. Callers have
        /// checked prev.y &gt; 0 &gt;= tip.y, so the denominator is positive.</summary>
        public static float CrossingRadius(Vector3 prev, Vector3 tip)
        {
            float t = prev.y / (prev.y - tip.y);
            float cx = prev.x + (tip.x - prev.x) * t, cz = prev.z + (tip.z - prev.z) * t;
            return Mathf.Sqrt(cx * cx + cz * cz);
        }

        /// <summary>Where the path crossed the strike plane (piece-local x, z): the same point CrossingRadius measures,
        /// where the head gives and the cymbal tips (E7).</summary>
        public static Vector2 CrossingPoint(Vector3 prev, Vector3 tip)
        {
            float t = prev.y / (prev.y - tip.y);
            return new Vector2(prev.x + (tip.x - prev.x) * t, prev.z + (tip.z - prev.z) * t);
        }

        /// <summary>The radius a crossing may land at and still sound: a drum answers its rim 3 cm past the head; the pad
        /// and the cymbals only on the plate.</summary>
        public static float StrikeReach(DrumPieceType type, bool pad, float r) =>
            type == DrumPieceType.Drum && !pad ? r + DrumSpec.RimExtra : r;

        /// <summary>Speed measured over the learned tracker interval (spec 05 section 1.3): max(sampleDt or dt, 1 ms).</summary>
        public static float SampleSeconds(float sampleDt, float dt) => Mathf.Max(sampleDt > 0f ? sampleDt : dt, DrumSpec.MinSampleDt);

        /// <summary>Kick loudness from the peak downward pitch rate of the nod.</summary>
        public static float NodVelocity(float peakRate) =>
            Mathf.Clamp(NodSpec.MinVelocity + 0.75f * (peakRate - NodSpec.NodSpeed) / (NodSpec.NodMax - NodSpec.NodSpeed), NodSpec.MinVelocity, 1f);

        /// <summary>Kit-local test of the hi-hat stand's pinch spot: within 8 cm of the
        /// pole, above .12 m and below the cymbals (hat.y - .08).</summary>
        public static bool InHatStandZone(Vector3 kitLocal, Vector3 hatLocal)
        {
            float dx = kitLocal.x - hatLocal.x, dz = kitLocal.z - hatLocal.z;
            return Mathf.Sqrt(dx * dx + dz * dz) < DrumSpec.HatStandRadius && kitLocal.y > DrumSpec.HatStandMinY
                && kitLocal.y < hatLocal.y - DrumSpec.HatStandBelowHat;
        }

        /// <summary>The play-surface test for one piece (piece-local point): -.12 &lt;= y &lt;= .25 and within r + .04.</summary>
        public static bool OverPiece(Vector3 pieceLocal, float r)
        {
            if (pieceLocal.y < -DrumSpec.ZoneBelow || pieceLocal.y > DrumSpec.ZoneAbove) return false;
            return Mathf.Sqrt(pieceLocal.x * pieceLocal.x + pieceLocal.z * pieceLocal.z) <= r + DrumSpec.ZonePad;
        }

        /// <summary>Distance from a point to the segment a-b (the stick's shaft distance). Core's HandToolMath since the platform.</summary>
        public static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p) => HandToolMath.SegmentDistance(a, b, p);

        /// <summary>The stick grip (HandToolMath.Grip with the stick's TILT -.22 rad): the grip and the stick axis.</summary>
        public static void StickGrip(Vector3 wrist, Quaternion wristRot, Vector3 thumbTip, Vector3 indexTip, out Vector3 grip, out Vector3 axis)
            => HandToolMath.Grip(wrist, wristRot, thumbTip, indexTip, StickSpec.TiltRad, out grip, out axis);

        /// <summary>World pose of a held stick from its grip and axis: butt = grip - axis * .13, +y along the axis.</summary>
        public static void StickPose(Vector3 grip, Vector3 axis, out Vector3 butt, out Quaternion rotation, out Vector3 tip, out Vector3 shoulder)
            => HandToolMath.Pose(grip, axis, StickSpec.GripFromButt, StickSpec.Length, StickSpec.ShoulderFromButt, out butt, out rotation, out tip, out shoulder);

        /// <summary>The settle easing k = 1 - (1 - settle)^3.</summary>
        public static float SettleEase(float settle) => HandToolMath.SettleEase(settle);

        /// <summary>The recorder's zone names (TakeEvent.zone), interned constants.</summary>
        public static string ZoneName(DrumZone zone) => DrumStrike.ZoneName(zone);
    }
}
