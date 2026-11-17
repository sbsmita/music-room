// The marimba's layout in its instrument-local frame (spec 4.1, 4.2, 5.1; design.mjs layout): the origin on the floor
// under the natural row's centre line, at the middle of the natural row; +x the player's right, +z away from the
// player, y up. Bars come from the sound unit's own tables (MarimbaTables: visual = acoustic for widths and
// thicknesses), so what is drawn is what is struck and what sounds. Shared by the interaction (strike planes,
// footprints, the mallets' rest poses) and the visuals. Pure data and formulas: no scene state.
using MusicRoom.Audio.Burst;
using UnityEngine;

namespace MusicRoom.Marimba
{
    public static class MarimbaGeometry
    {
        public const int Bars = MarimbaTables.Bars, Midi0 = MarimbaTables.Midi0;
        /// <summary>Natural bar tops at the default height (lift range 0.69..0.82, spec 4.1).</summary>
        public const float HDefault = 0.70f, HMin = 0.69f, HMax = 0.82f;
        /// <summary>The natural bar tops in the app (phase B): 2 cm above the spec's 0.70 so that, with the folded resonators,
        /// nothing in front of the player's knees hangs below 0.62 m (the seat planner's knee box). Seated elbow height is
        /// 0.64-0.75 m, so the mallets still angle down to the bars.</summary>
        public const float HSeat = 0.72f;
        /// <summary>The sharps' tops sit this much higher than the naturals'.</summary>
        public const float AccRaise = 0.022f;
        public const float NaturalGap = 0.006f, RowGap = 0.025f;
        /// <summary>The resonator mouths sit this far under the bar's underside (2.3).</summary>
        public const float TubeMouthGap = 0.025f;

        // ---- the mallets (spec 4.2, 5.1): rattan shaft, yarn head; the drumsticks' lever (grip 0.10 from the butt, head
        // centre 0.27 beyond the grip)
        public const float MalletGripFromButt = 0.10f, MalletHeadFromButt = 0.37f, MalletShaftRadius = 0.0045f;
        public const float MalletHeadRadius = 0.017f, MalletHeadWidth = 0.034f, MalletHeadHeight = 0.031f;
        /// <summary>The mallets' head edge from the butt (0.37 + 0.017).</summary>
        public const float MalletLength = MalletHeadFromButt + MalletHeadRadius;

        // ---- the tray (revision 2, 4.2: 0.82 m, mallets end to end, heads outward, butts 20 mm apart). Review item 11:
        // raised from H - 0.04 to H + 0.005 on the front rail, so in mixed reality it clears a seated player's thighs
        // (thigh tops 0.56-0.68 m at 0.265 m ahead of the eyes).
        public const float TrayLength = 0.82f, TrayDepth = 0.05f, TrayWell = 0.015f, TrayZ = -0.235f, TrayAboveH = 0.005f;
        /// <summary>The butts' x on the tray (left mallet at -ButtX, right at +ButtX).</summary>
        public const float TrayButtX = 0.010f;

        public static int MidiOf(int bar) => Midi0 + bar;
        public static bool Natural(int bar) => MarimbaTables.Natural[bar];
        public static float X(int bar) => (float)MarimbaTables.X[bar];
        public static float Z(int bar) => (float)MarimbaTables.Z[bar];
        public static float Length(int bar) => (float)MarimbaTables.Length[bar];
        public static float Width(int bar) => (float)MarimbaTables.Width[bar];
        public static float Thickness(int bar) => (float)MarimbaTables.H0[bar];
        public static float CordXi(int bar) => (float)MarimbaTables.CordXi[bar];
        /// <summary>The bar's top at natural height h.</summary>
        public static float Top(int bar, float h = HDefault) => h + (Natural(bar) ? 0f : AccRaise);
        public static float NearZ(int bar) => Z(bar) - Length(bar) * 0.5f;
        public static float FarZ(int bar) => Z(bar) + Length(bar) * 0.5f;
        /// <summary>The bar's top-centre point (instrument-local).</summary>
        public static Vector3 TopCentre(int bar, float h = HDefault) => new Vector3(X(bar), Top(bar, h), Z(bar));
        /// <summary>A point on the bar's top: u along (0 near end .. 1 far end), c across (-1 .. 1).</summary>
        public static Vector3 TopPoint(int bar, float u, float c, float h = HDefault) =>
            new Vector3(X(bar) + c * Width(bar) * 0.5f, Top(bar, h), NearZ(bar) + u * Length(bar));

        public static float TubeRadius(int bar) => (float)MarimbaTables.TubeRadius[bar];
        /// <summary>The tube's acoustic length (m): the length a straight tube is drawn at (C3 632 mm .. C6 63 mm).</summary>
        public static float TubeAcousticLength(int bar) => (float)MarimbaTables.TubeLacoustic[bar];

        public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        public static string NoteName(int bar) { int m = MidiOf(bar); return NoteNames[m % 12] + (m / 12 - 1); }

        /// <summary>The bar index of a note name ("C4", "F#4"), or -1.</summary>
        public static int BarOf(string note)
        {
            for (int i = 0; i < Bars; i++) if (NoteName(i) == note) return i;
            return -1;
        }

        /// <summary>A mallet's rest pose on the tray (side 0 left, 1 right): the butt and the axis (butt to head).</summary>
        public static void MalletRest(int side, float h, out Vector3 butt, out Vector3 axis)
        {
            float s = side == 0 ? -1f : 1f;
            // the heads rest on the felt at the bottom of the tray's well; the shaft runs level through the head centre
            float y = h + TrayAboveH - TrayWell + MalletHeadHeight * 0.5f;
            butt = new Vector3(s * TrayButtX, y, TrayZ);
            axis = new Vector3(s, 0f, 0f);
        }

        /// <summary>The grip point of a resting mallet (where a hand picks it up: 11 cm either side of the centre).</summary>
        public static Vector3 MalletRestGrip(int side, float h = HDefault)
        {
            MalletRest(side, h, out Vector3 butt, out Vector3 axis);
            return butt + axis * MalletGripFromButt;
        }
    }
}
