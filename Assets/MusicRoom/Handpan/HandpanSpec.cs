// The physical instrument (handpan.md 1, 6.1, 7.1): handpan-layout.mjs as data, shared by the play logic, the visuals,
// the lessons and the placement. Metres, Unity axes (+Y up, +Z away from the player, +X to the player's right). The
// instrument-local frame: the rim plane at y = 0, the top apex at y = TopH; the "map" is the geodesic polar map of the top
// cap, (X, Z) = (s sin az, -s cos az), az 0 = toward the player.
using System;

namespace MusicRoom.Handpan
{
    /// <summary>One tone field: the ding (field 0) or a ring note, an ellipse in the map with its long axis radial.</summary>
    public sealed class HandpanField
    {
        public int Index, Midi;
        public string Note;
        public bool Ding;
        /// <summary>Azimuth in degrees (0 toward the player, 90 to the right).</summary>
        public double Az;
        public double F0, Long, Short, S, DimpleFrac, DimpleDepth, FieldDepth, DomeH;
        /// <summary>The field centre in the map (the dimple apex).</summary>
        public double Cx, Cz;
        /// <summary>Radial and tangential unit vectors in the map.</summary>
        public double Rx, Rz, Tx, Tz;
    }

    public static class HandpanSpec
    {
        // ---- shell (two spherical caps joined at the rim)
        public const double Diameter = 0.53, TopH = 0.115, BotH = 0.105, Steel = 0.0012, GuD = 0.09, GuLip = 0.012, RimBand = 0.022, MassKg = 4.6;
        public const double RimR = Diameter / 2;
        public static readonly double CapR = (RimR * RimR + TopH * TopH) / (2 * TopH);     // 0.3629 m
        public static readonly double BotR = (RimR * RimR + BotH * BotH) / (2 * BotH);     // 0.3869 m
        public static readonly double RimArc = CapR * Math.Asin(RimR / CapR);               // 0.2974 m, apex to rim along the steel
        public static readonly double BotArc = BotR * Math.Asin(RimR / BotR);

        // ---- tone fields
        public const double RingS = 0.180;
        public const int FieldCount = 9, Tak = 9;
        /// <summary>A strike up to 6 mm outside the drawn edge still counts as that field.</summary>
        public const double EdgeMargin = 0.006;
        /// <summary>A tak's neighbour fall-off: w = exp(-d / 25 mm).</summary>
        public const double TakFallM = 0.025;
        /// <summary>The steel between A3 and C4, near the player (interaction case tak-between).</summary>
        public const double TakDefaultX = 0, TakDefaultZ = -0.19;

        public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        public static string NoteName(int midi) => NoteNames[midi % 12] + (midi / 12 - 1);
        public static double MidiHz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

        static readonly int[] Midis = { 50, 57, 60, 62, 64, 65, 67, 69, 72 };
        static readonly double[] Azs = { 0, 337.5, 22.5, 292.5, 67.5, 247.5, 112.5, 202.5, 157.5 };

        /// <summary>D "Celtic minor" 9: ding D3 + A3 C4 D4 E4 F4 G4 A4 C5, zigzag from the player.</summary>
        public static readonly HandpanField[] Fields = BuildFields();

        static HandpanField[] BuildFields()
        {
            var a = new HandpanField[FieldCount];
            for (int i = 0; i < FieldCount; i++)
            {
                bool ding = i == 0;
                double f0 = MidiHz(Midis[i]);
                double lg = ding ? 0.165 : 0.150 * Math.Pow(220 / f0, 0.45);
                double sh = ding ? 0.165 : 0.80 * lg;
                var F = new HandpanField
                {
                    Index = i, Midi = Midis[i], Note = NoteName(Midis[i]), Ding = ding, Az = Azs[i], F0 = f0, Long = lg, Short = sh,
                    S = ding ? 0 : RingS, DimpleFrac = ding ? 0.42 : 0.38, DimpleDepth = ding ? 0.004 : 0.004 + 0.003 * (lg / 0.15),
                    FieldDepth = ding ? 0 : 0.002, DomeH = ding ? 0.010 : 0,
                };
                double az = F.Az * Math.PI / 180;
                F.Cx = F.S * Math.Sin(az); F.Cz = -F.S * Math.Cos(az);
                F.Rx = Math.Sin(az); F.Rz = -Math.Cos(az); F.Tx = -F.Rz; F.Tz = F.Rx;
                a[i] = F;
            }
            return a;
        }

        public static int MidiOf(int field) => (uint)field < FieldCount ? Midis[field] : -1;

        // ---- placement (seat frame: the seated head at (0, 1.2, 0), facing +Z; handpan.md 6.6, 7.5)
        /// <summary>The rim centre in the seat frame and the tilt toward the player (far side up). Revision 2 had 0.72 m; the
        /// build raised it to 0.74 m (review item 9, the critique's c4-geometry sweep: the near fields' edge clears a
        /// 95th-percentile thigh, +0.4 cm instead of -1.6, the sweet spot by 4.5 cm, and the far fields are closer to the
        /// shoulder, 0.594 m at their edges).</summary>
        public static readonly double[] PlaceCentre = { 0, 0.74, 0.38 };
        public const double TiltDeg = 28, YawDeg = 0;
        /// <summary>The rig's home (seat frame): the stand stands on the floor at (0, 0, 0.38); the rim centre is RimHeight above.</summary>
        public const double RimHeight = 0.74, HomeZ = 0.38;

        public static readonly double[] SeatHead = { 0, 1.2, 0 };
        public static readonly double[] ShoulderL = { -0.18, 1.04, -0.03 }, ShoulderR = { 0.18, 1.04, -0.03 };
        public const double Reach = 0.60;

        // ---- stand (handpan.md 7.1)
        public const double StandRingD = 0.30, StandRingCentreY = 0.674;
        // ---- lessons (handpan.md 8.1)
        public const double LessonTravel = 0.30, LessonSpeed = 0.18, LessonEyeGap = 0.26, LessonZMin = 0.16;
    }
}
