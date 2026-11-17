// The bowls rig as data (bowls.md 5.1, 6): rig-local metres, origin on the floor under the
// stand-top centre, +z away from the player, +x toward the player's knees (the rig sits left of them, yaw 0).
// Phase A change (review item 15): the BIG bowl sits nearest the left shoulder (inner-back), the middle one outer-back,
// the small one in front. Measured from the left shoulder (-0.18, 1.05, 0) over every rim point: farthest 0.478 /
// 0.612 / 0.614 m (was big 0.613 m), nearest 0.375 / 0.496 / 0.518 m; rim gaps big-middle 62.8 mm, big-small 32.5 mm,
// middle-small 45.6 mm (was 42.5 / 28.9 / 30.2). No arrangement of these three bowls inside the 0.41 x 0.29 m pocket
// keeps every far rim under 0.61 m (a search over the stand top finds 0.614 m at best).
using UnityEngine;

namespace MusicRoom.Bowls
{
    public static class BowlsLayout
    {
        // ---- the stand (a small walnut table) ----
        public const float StandTop = 0.61f, StandHx = 0.205f, StandHz = 0.145f, TopThick = 0.022f, TopLip = 0.003f;
        public const float LegR = 0.015f, StretcherY = 0.14f, TopEdgeRound = 0.006f;
        /// <summary>The bowl base above the stand top (the ring cushion).</summary>
        public const float CushionLift = 0.025f;
        public const float BaseY = StandTop + CushionLift;

        // ---- the bowls (index 0 big G3, 1 middle C4, 2 small E4) ----
        public static readonly float[] RimDiam = { 0.175f, 0.150f, 0.125f };
        public static readonly float[] Height = { 0.080f, 0.068f, 0.056f };
        public static readonly float[] Wall = { 0.0038f, 0.0034f, 0.0031f };
        public static readonly string[] Key = { "big", "middle", "small" };
        public static readonly string[] Label = { "Big bowl", "Middle bowl", "Small bowl" };
        public static readonly string[] Note = { "G3", "C4", "E4" };
        public static readonly int[] Midi = { 55, 60, 64 };

        /// <summary>Bowl base centres on the stand top (rig-local x, z), in double (the hand logic) and float.</summary>
        public static readonly double[] BowlX = { 0.115, -0.110, -0.005 }, BowlZ = { -0.0575, -0.070, 0.080 };
        public static readonly Vector2[] BowlXZ = { new Vector2(0.115f, -0.0575f), new Vector2(-0.110f, -0.070f), new Vector2(-0.005f, 0.080f) };
        /// <summary>The bowls' shapes in double (bowls-design.mjs).</summary>
        public static readonly double[] RimDiamD = { 0.175, 0.150, 0.125 }, HeightD = { 0.080, 0.068, 0.056 }, WallD = { 0.0038, 0.0034, 0.0031 };
        public const double BaseYD = 0.61 + 0.025;

        public static float R(int i) => RimDiam[i] * 0.5f;
        public static float RimY(int i) => BaseY + Height[i];
        public static Vector3 BaseLocal(int i) => new Vector3(BowlXZ[i].x, BaseY, BowlXZ[i].y);

        /// <summary>Outer wall radius at height y above the base (a hammered bowl's rounded wall: 0.55 R at the base).</summary>
        public static double ROut(int i, double y)
        {
            double h = Height[i], r = R(i);
            return r * (0.55 + 0.45 * System.Math.Sin(0.5 * System.Math.PI * System.Math.Max(0, System.Math.Min(1, y / h))));
        }

        // ---- the puja (one turned walnut stick, its top 35 mm wrapped in suede) ----
        public const float PujaLength = 0.15f, PujaRadius = 0.011f, PujaSuedeLength = 0.035f, PujaSuedeRadius = 0.0135f;
        /// <summary>The grip point, measured from the suede end.</summary>
        public const float PujaGrip = 0.085f;
        /// <summary>Resting on the stand top along x, suede end toward +x (the knees), in the front corner nearest the
        /// player's left hand, clear of the cushions: grip 0.59 m from the left shoulder.</summary>
        public static readonly Vector3 PujaRestCentre = new Vector3(0.115f, StandTop + PujaRadius, 0.112f);

        // ---- seat (bowls.md 3.2 / 6.1): the pocket left of the knees ----
        public static readonly Vector3 HomeSeat = new Vector3(-0.46f, 0f, 0.25f);
        public const float HomeYawDeg = 0f;
        public const float ReachMax = 0.62f;
        public static readonly Vector2 PlayCentre = new Vector2(0f, -0.016f);
        /// <summary>The bowls' sound source: the centre of the three rims (rig-local).</summary>
        public static Vector3 SourceLocal
        {
            get
            {
                Vector3 c = Vector3.zero;
                for (int i = 0; i < 3; i++) c += new Vector3(BowlXZ[i].x, RimY(i), BowlXZ[i].y);
                return c / 3f;
            }
        }

        // ---- on display (the window sill, stand hidden): a row along the sill ----
        public static readonly float[] StowX = { -0.165f, 0f, 0.14f };
        /// <summary>The inside sill's top above the floor (the room's trim box: top y 0.63): the display row stands on it.</summary>
        public const float SillTop = 0.63f;
        public const float StowPujaZ = -0.075f;
    }
}
